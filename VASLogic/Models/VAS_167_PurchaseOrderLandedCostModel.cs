/// <summary>
/// Module Name : VASLogic
/// Purpose     : Expected Landed Cost tab panel for Purchase Orders
///               (C_Order, IsSOTrx = 'N'). Read side returns the order
///               identity, its expected landed cost entries (C_ExpectedCost)
///               with the amount converted into the order's document currency
///               through the selected C_ConversionType_ID, the generated
///               distribution lines (C_ExpectedCostDistribution) and the
///               lookups the draft form needs (M_CostElement, C_Currency,
///               C_ConversionType). Write side creates / updates / deletes
///               C_ExpectedCost through MExpectedCost and is only ever allowed
///               while the order is drafted (DocStatus = 'DR').
///
///               No new table or column is introduced: the panel reads and
///               writes the platform's own expected landed cost tables. The
///               distribution lines themselves are NOT generated here — the
///               platform already creates them on completion
///               (MOrder.CompleteIt -> ExpectedlandedCostDistribution ->
///               MExpectedCost.DistributeLandedCost), which deletes and
///               re-inserts each entry's lines and is therefore idempotent.
///
///               CURRENCY CONVENTION — read before changing any amount here.
///               C_ExpectedCostDistribution.Amt is stored in the ENTERED
///               currency (C_ExpectedCost.C_Currency_ID), not in the order's
///               document currency: DistributeLandedCost splits Amt as keyed.
///               That is deliberate — the consumers convert it themselves at
///               consumption time, on the accounting date:
///                 - MCostQueue (expected landed cost from GRN) converts from
///                   C_ExpectedCost.C_Currency_ID to the accounting-schema
///                   currency using M_InOut.DateAcct;
///                 - MInvoiceLine.GetLandedCostDifferenceAmt converts from the
///                   same currency to the invoice currency.
///               So each generated line is reported here in the entry's entered
///               currency (ExpectedCostData.EnteredCurrencyCode) and the
///               document-currency figure is shown alongside it, converted from
///               the parent amount. Converting the stored lines instead would
///               double-convert in both consumers and corrupt inventory cost
///               and the GL variance.
///
///               SQL is kept portable between Oracle and PostgreSQL: COALESCE
///               (never NVL / DECODE / TRUNC / SYSDATE), standard joins,
///               parameters, and no database-specific date or number
///               formatting — all formatting happens on the client.
/// Chronological development:
///   VAI163   2026-07-30  Created
/// </summary>

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;

namespace VASLogic.Models
{
    public class VAS_167_PurchaseOrderLandedCostModel
    {
        private static readonly VLogger _log =
            VLogger.GetVLogger(typeof(VAS_167_PurchaseOrderLandedCostModel).FullName);

        #region Constants

        /// <summary>Drafted document status — the only status this panel may write in.</summary>
        private const string DOCSTATUS_Drafted = "DR";

        /// <summary>Completed document status — entries and their lines are frozen.</summary>
        private const string DOCSTATUS_Completed = "CO";

        /// <summary>
        /// The three C_LandedCostDistribution values this panel exposes. The
        /// reference list carries more (C = Costs, V = Volume, W = Weight); they
        /// are deliberately not offered here and a create / update carrying one
        /// is rejected.
        /// </summary>
        private static readonly string[] ALLOWED_DISTRIBUTIONS = new string[] { "Q", "I", "L" };

        #endregion

        #region Read

        /// <summary>
        /// Returns the complete panel payload for the selected purchase order.
        /// MRole access filtering is applied on the main physical table
        /// (C_Order alias "o") only; the child queries inherit the parent's
        /// authorization.
        /// </summary>
        /// <param name="ctx">User context (client / org / role).</param>
        /// <param name="C_Order_ID">Selected purchase order id.</param>
        /// <returns>Populated <see cref="LandedCostPanelData"/>; an empty instance
        /// when the id is invalid or no accessible purchase order is found.</returns>
        public LandedCostPanelData GetLandedCostPanel(Ctx ctx, int C_Order_ID)
        {
            LandedCostPanelData result = new LandedCostPanelData();
            if (C_Order_ID <= 0) return result;

            string sql = @"SELECT
                              o.C_Order_ID,
                              o.DocumentNo,
                              o.DateOrdered,
                              o.DocStatus,
                              o.GrandTotal,
                              o.C_Currency_ID,
                              o.AD_Client_ID,
                              o.AD_Org_ID,
                              bp.Name          AS VendorName,
                              usr.Name         AS BuyerName,
                              dt.Name          AS DocumentTypeName,
                              cur.ISO_Code     AS DocumentCurrencyCode,
                              cur.CurSymbol    AS DocumentCurrencySymbol,
                              COALESCE(cur.StdPrecision, 2) AS DocumentCurrencyPrecision
                            FROM C_Order o
                            INNER JOIN C_BPartner bp        ON (bp.C_BPartner_ID  = o.C_BPartner_ID)
                            LEFT OUTER JOIN AD_User usr     ON (usr.AD_User_ID    = o.SalesRep_ID)
                            LEFT OUTER JOIN C_DocType dt    ON (dt.C_DocType_ID   = o.C_DocTypeTarget_ID)
                            LEFT OUTER JOIN C_Currency cur  ON (cur.C_Currency_ID = o.C_Currency_ID)
                            WHERE o.C_Order_ID = @C_Order_ID
                              AND o.IsActive   = 'Y'
                              AND o.IsSOTrx    = 'N'";

            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql, "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            DataSet ds = DB.ExecuteDataset(sql, OrderParam(C_Order_ID), null);
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
                return result;

            DataRow r = ds.Tables[0].Rows[0];

            result.PurchaseOrderId     = Util.GetValueOfInt(r["C_Order_ID"]);
            result.PurchaseOrderNumber = Util.GetValueOfString(r["DocumentNo"]);
            result.OrderDate           = Util.GetValueOfDateTime(r["DateOrdered"]);
            result.DocumentStatus      = Util.GetValueOfString(r["DocStatus"]);
            result.PurchaseOrderTotal  = Util.GetValueOfDecimal(r["GrandTotal"]);
            result.VendorName          = Util.GetValueOfString(r["VendorName"]);
            result.BuyerName           = Util.GetValueOfString(r["BuyerName"]);
            result.DocumentTypeName    = Util.GetValueOfString(r["DocumentTypeName"]);
            result.DocumentCurrencyId       = Util.GetValueOfInt(r["C_Currency_ID"]);
            result.DocumentCurrencyCode     = Util.GetValueOfString(r["DocumentCurrencyCode"]);
            result.DocumentCurrencySymbol   = Util.GetValueOfString(r["DocumentCurrencySymbol"]);
            result.DocumentCurrencyPrecision = Util.GetValueOfInt(r["DocumentCurrencyPrecision"]);

            // Editable only while drafted — the client mirrors this, the write
            // methods below re-check it against the database independently.
            result.IsDrafted   = result.DocumentStatus == DOCSTATUS_Drafted;
            result.IsCompleted = result.DocumentStatus == DOCSTATUS_Completed;

            int clientId = Util.GetValueOfInt(r["AD_Client_ID"]);
            int orgId    = Util.GetValueOfInt(r["AD_Org_ID"]);

            // Eligible allocation targets: active product lines. Surfaced so the
            // panel can say "nothing to allocate against" instead of the reader
            // discovering it only when completion fails.
            result.EligibleLineCount = GetEligibleLineCount(C_Order_ID);

            // ----- Expected landed cost entries (+ server-side conversion) -----
            result.ExpectedCosts = LoadExpectedCosts(ctx, result, clientId, orgId);

            // ----- Generated distribution lines, attached to their parent entry -----
            LoadGeneratedLines(C_Order_ID, result.ExpectedCosts);

            // ----- Totals (document currency only; never a mixed-currency sum) -----
            decimal expectedTotal = 0;
            foreach (ExpectedCostData c in result.ExpectedCosts)
            {
                if (c.IsConversionAvailable)
                    expectedTotal += c.ConvertedAmount;
                else
                    result.HasMissingConversion = true;
            }
            result.ExpectedCostTotalConverted = expectedTotal;
            result.ExpectedCostCount          = result.ExpectedCosts.Count;

            // ----- Lookups for the draft form (never hard-coded on the client) -----
            result.CostElements    = LoadCostElements(clientId);
            result.Currencies      = LoadCurrencies(clientId);
            result.ConversionTypes = LoadConversionTypes(clientId);
            result.Distributions   = LoadDistributions(ctx);

            return result;
        }

        /// <summary>
        /// Counts the order's eligible allocation targets — active lines carrying
        /// a product. Charge / description-only lines are never allocation targets.
        /// </summary>
        private int GetEligibleLineCount(int C_Order_ID)
        {
            try
            {
                string sql = @"SELECT COUNT(*) AS LineCount
                                 FROM C_OrderLine ol
                                WHERE ol.C_Order_ID   = @C_Order_ID
                                  AND ol.IsActive     = 'Y'
                                  AND ol.M_Product_ID IS NOT NULL";
                DataSet ds = DB.ExecuteDataset(sql, OrderParam(C_Order_ID), null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
                    return 0;
                return Util.GetValueOfInt(ds.Tables[0].Rows[0]["LineCount"]);
            }
            catch (Exception ex)
            {
                _log.Severe("GetEligibleLineCount (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
                return 0;
            }
        }

        /// <summary>
        /// Loads the order's C_ExpectedCost entries with their cost element,
        /// entered currency and currency rate type, and converts each entered
        /// amount into the order's document currency through the platform's own
        /// conversion service (MConversionRate) using the entry's
        /// C_ConversionType_ID and the order date. No exchange rate is ever
        /// computed in SQL or on the client.
        /// </summary>
        private List<ExpectedCostData> LoadExpectedCosts(
            Ctx ctx, LandedCostPanelData order, int clientId, int orgId)
        {
            List<ExpectedCostData> list = new List<ExpectedCostData>();
            try
            {
                string sql = @"SELECT
                                  ec.C_ExpectedCost_ID       AS ExpectedCostId,
                                  ec.C_Order_ID              AS PurchaseOrderId,
                                  ec.LandedCostDistribution  AS DistributionCode,
                                  ec.M_CostElement_ID        AS CostElementId,
                                  ce.Name                    AS CostElementName,
                                  ec.Description             AS Description,
                                  ec.Amt                     AS EnteredAmount,
                                  ec.C_Currency_ID           AS EnteredCurrencyId,
                                  curr.ISO_Code              AS EnteredCurrencyCode,
                                  COALESCE(curr.StdPrecision, 2) AS EnteredCurrencyPrecision,
                                  ec.C_ConversionType_ID     AS ConversionTypeId,
                                  ct.Name                    AS ConversionTypeName
                                FROM C_ExpectedCost ec
                                INNER JOIN M_CostElement ce
                                       ON (ce.M_CostElement_ID = ec.M_CostElement_ID)
                                LEFT OUTER JOIN C_Currency curr
                                       ON (curr.C_Currency_ID = ec.C_Currency_ID)
                                LEFT OUTER JOIN C_ConversionType ct
                                       ON (ct.C_ConversionType_ID = ec.C_ConversionType_ID)
                                WHERE ec.C_Order_ID = @C_Order_ID
                                  AND COALESCE(ec.IsActive, 'Y') = 'Y'
                                ORDER BY ec.C_ExpectedCost_ID";
                DataSet ds = DB.ExecuteDataset(sql, OrderParam(order.PurchaseOrderId), null);
                if (ds == null || ds.Tables.Count == 0) return list;

                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    ExpectedCostData c = new ExpectedCostData();
                    c.ExpectedCostId      = Util.GetValueOfInt(r["ExpectedCostId"]);
                    c.PurchaseOrderId     = Util.GetValueOfInt(r["PurchaseOrderId"]);
                    c.DistributionCode    = Util.GetValueOfString(r["DistributionCode"]);
                    c.DistributionLabel   = GetDistributionLabel(ctx, c.DistributionCode);
                    c.CostElementId       = Util.GetValueOfInt(r["CostElementId"]);
                    c.CostElementName     = Util.GetValueOfString(r["CostElementName"]);
                    c.Description         = Util.GetValueOfString(r["Description"]);
                    c.EnteredAmount       = Util.GetValueOfDecimal(r["EnteredAmount"]);
                    c.EnteredCurrencyId   = Util.GetValueOfInt(r["EnteredCurrencyId"]);
                    c.EnteredCurrencyCode = Util.GetValueOfString(r["EnteredCurrencyCode"]);
                    c.EnteredCurrencyPrecision = Util.GetValueOfInt(r["EnteredCurrencyPrecision"]);
                    c.ConversionTypeId    = Util.GetValueOfInt(r["ConversionTypeId"]);
                    c.ConversionTypeName  = Util.GetValueOfString(r["ConversionTypeName"]);
                    c.DocumentCurrencyCode = order.DocumentCurrencyCode;

                    ConvertToDocumentCurrency(ctx, c, order, clientId, orgId);

                    list.Add(c);
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadExpectedCosts (C_Order_ID=" + order.PurchaseOrderId + "): " + ex.Message);
            }
            return list;
        }

        /// <summary>
        /// Converts one entry's entered amount into the order's document currency
        /// through MConversionRate, using the entry's currency rate type
        /// (C_ConversionType_ID) and the order date. Same currency is a no-op.
        /// A missing rate leaves <see cref="ExpectedCostData.IsConversionAvailable"/>
        /// false so the panel can flag it instead of showing a wrong number, and
        /// the entry is left out of the converted total.
        /// </summary>
        private void ConvertToDocumentCurrency(
            Ctx ctx, ExpectedCostData c, LandedCostPanelData order, int clientId, int orgId)
        {
            c.IsSameCurrency = c.EnteredCurrencyId <= 0
                               || c.EnteredCurrencyId == order.DocumentCurrencyId;
            if (c.IsSameCurrency)
            {
                c.ConvertedAmount      = c.EnteredAmount;
                c.IsConversionAvailable = true;
                return;
            }

            try
            {
                decimal rate = MConversionRate.GetRate(
                    c.EnteredCurrencyId, order.DocumentCurrencyId, order.OrderDate,
                    c.ConversionTypeId, clientId, orgId);
                if (rate == 0)
                {
                    // No rate defined for this currency pair / rate type / date.
                    c.IsConversionAvailable = false;
                    c.ConvertedAmount       = 0;
                    return;
                }

                c.ConvertedAmount = MConversionRate.Convert(
                    ctx, c.EnteredAmount, c.EnteredCurrencyId, order.DocumentCurrencyId,
                    order.OrderDate, c.ConversionTypeId, clientId, orgId);
                c.IsConversionAvailable = true;
            }
            catch (Exception ex)
            {
                _log.Severe("ConvertToDocumentCurrency (C_ExpectedCost_ID="
                            + c.ExpectedCostId + "): " + ex.Message);
                c.IsConversionAvailable = false;
                c.ConvertedAmount       = 0;
            }
        }

        /// <summary>
        /// Loads the generated distribution lines (C_ExpectedCostDistribution) for
        /// every expected cost entry of the order and attaches them to their
        /// parent. The allocation itself is never recomputed — these rows are the
        /// platform's own output, written on completion. The per-entry total base
        /// is summed here so the client can render the audit basis
        /// ("Qty 12 of 22") without doing arithmetic on money.
        ///
        /// Each line's amount is reported in its parent entry's ENTERED currency,
        /// which is what the column stores (see the currency convention in the
        /// class header) — never relabelled as the document currency.
        /// </summary>
        private void LoadGeneratedLines(int C_Order_ID, List<ExpectedCostData> costs)
        {
            if (costs == null || costs.Count == 0) return;

            Dictionary<int, ExpectedCostData> byId = new Dictionary<int, ExpectedCostData>();
            foreach (ExpectedCostData c in costs)
            {
                c.GeneratedLines = new List<GeneratedLineData>();
                if (!byId.ContainsKey(c.ExpectedCostId))
                    byId.Add(c.ExpectedCostId, c);
            }

            try
            {
                string sql = @"SELECT
                                  ecd.C_ExpectedCostDistribution_ID AS DistributionLineId,
                                  ecd.C_ExpectedCost_ID             AS ExpectedCostId,
                                  ecd.C_OrderLine_ID                AS PurchaseOrderLineId,
                                  ol.Line                           AS LineNumber,
                                  p.Name                            AS ProductName,
                                  p.Value                           AS ProductCode,
                                  COALESCE(ecd.Base, 0)             AS AllocationBase,
                                  COALESCE(ecd.Qty, 0)              AS LineQuantity,
                                  COALESCE(ecd.Amt, 0)              AS AllocatedAmount
                                FROM C_ExpectedCostDistribution ecd
                                INNER JOIN C_ExpectedCost ec
                                       ON (ec.C_ExpectedCost_ID = ecd.C_ExpectedCost_ID)
                                INNER JOIN C_OrderLine ol
                                       ON (ol.C_OrderLine_ID = ecd.C_OrderLine_ID)
                                INNER JOIN M_Product p
                                       ON (p.M_Product_ID = ol.M_Product_ID)
                                WHERE ec.C_Order_ID = @C_Order_ID
                                  AND COALESCE(ecd.IsActive, 'Y') = 'Y'
                                  AND COALESCE(ec.IsActive, 'Y')  = 'Y'
                                ORDER BY ecd.C_ExpectedCost_ID, ol.Line";
                DataSet ds = DB.ExecuteDataset(sql, OrderParam(C_Order_ID), null);
                if (ds == null || ds.Tables.Count == 0) return;

                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    int parentId = Util.GetValueOfInt(r["ExpectedCostId"]);
                    if (!byId.ContainsKey(parentId)) continue;

                    GeneratedLineData g = new GeneratedLineData();
                    g.DistributionLineId  = Util.GetValueOfInt(r["DistributionLineId"]);
                    g.ExpectedCostId      = parentId;
                    g.PurchaseOrderLineId = Util.GetValueOfInt(r["PurchaseOrderLineId"]);
                    g.LineNumber          = Util.GetValueOfInt(r["LineNumber"]);
                    g.ProductName         = Util.GetValueOfString(r["ProductName"]);
                    g.ProductCode         = Util.GetValueOfString(r["ProductCode"]);
                    g.AllocationBase      = Util.GetValueOfDecimal(r["AllocationBase"]);
                    g.LineQuantity        = Util.GetValueOfDecimal(r["LineQuantity"]);
                    g.AllocatedAmount     = Util.GetValueOfDecimal(r["AllocatedAmount"]);
                    // The stored Amt is in the parent entry's entered currency.
                    g.AmountCurrencyCode  = byId[parentId].EnteredCurrencyCode;
                    byId[parentId].GeneratedLines.Add(g);
                }

                // Per-entry roll-ups: the total base every line's share was taken
                // against, and the distributed total the footer reports.
                foreach (ExpectedCostData c in costs)
                {
                    decimal totalBase = 0, distributed = 0;
                    foreach (GeneratedLineData g in c.GeneratedLines)
                    {
                        totalBase   += g.AllocationBase;
                        distributed += g.AllocatedAmount;
                    }
                    c.TotalAllocationBase = totalBase;
                    c.DistributedAmount   = distributed;
                    // The generated lines must add back up to the entry's entered
                    // amount (DistributeLandedCost pushes the rounding residual
                    // onto the largest line to guarantee it). Surfaced so the panel
                    // can flag an entry whose lines no longer reconcile — e.g. one
                    // edited after generation — instead of quietly showing a footer
                    // that disagrees with the row above it.
                    c.IsReconciled = c.GeneratedLines.Count == 0
                                     || Math.Abs(distributed - c.EnteredAmount) < 0.005m;
                    foreach (GeneratedLineData g in c.GeneratedLines)
                        g.TotalAllocationBase = totalBase;
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadGeneratedLines (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
            }
        }

        #endregion

        #region Lookups

        /// <summary>Active cost elements (M_CostElement) visible to the client.</summary>
        private List<LookupItemData> LoadCostElements(int clientId)
        {
            return LoadLookup(
                @"SELECT ce.M_CostElement_ID AS Id, ce.Name AS Name
                    FROM M_CostElement ce
                   WHERE ce.IsActive = 'Y'
                     AND ce.AD_Client_ID IN (0, @AD_Client_ID)
                   ORDER BY ce.Name",
                clientId, "LoadCostElements");
        }

        /// <summary>Active currencies (C_Currency), shown by ISO code.</summary>
        private List<LookupItemData> LoadCurrencies(int clientId)
        {
            return LoadLookup(
                @"SELECT c.C_Currency_ID AS Id, c.ISO_Code AS Name
                    FROM C_Currency c
                   WHERE c.IsActive = 'Y'
                     AND c.AD_Client_ID IN (0, @AD_Client_ID)
                   ORDER BY c.ISO_Code",
                clientId, "LoadCurrencies");
        }

        /// <summary>Active currency rate types (C_ConversionType).</summary>
        private List<LookupItemData> LoadConversionTypes(int clientId)
        {
            return LoadLookup(
                @"SELECT ct.C_ConversionType_ID AS Id, ct.Name AS Name
                    FROM C_ConversionType ct
                   WHERE ct.IsActive = 'Y'
                     AND ct.AD_Client_ID IN (0, @AD_Client_ID)
                   ORDER BY ct.Name",
                clientId, "LoadConversionTypes");
        }

        /// <summary>
        /// The three cost distributions this panel exposes, in display order.
        /// Stored codes stay the platform's own reference-list values (Q / I / L);
        /// only the labels are panel-side, and no new list value is created.
        /// </summary>
        private List<LookupItemData> LoadDistributions(Ctx ctx)
        {
            List<LookupItemData> list = new List<LookupItemData>();
            foreach (string code in ALLOWED_DISTRIBUTIONS)
            {
                list.Add(new LookupItemData
                {
                    Code = code,
                    Name = GetDistributionLabel(ctx, code)
                });
            }
            return list;
        }

        /// <summary>Runs an id/name lookup query, degrading to an empty list.</summary>
        private List<LookupItemData> LoadLookup(string sql, int clientId, string label)
        {
            List<LookupItemData> list = new List<LookupItemData>();
            try
            {
                SqlParameter[] param = new SqlParameter[]
                {
                    new SqlParameter("@AD_Client_ID", clientId)
                };
                DataSet ds = DB.ExecuteDataset(sql, param, null);
                if (ds == null || ds.Tables.Count == 0) return list;
                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    list.Add(new LookupItemData
                    {
                        Id   = Util.GetValueOfInt(r["Id"]),
                        Name = Util.GetValueOfString(r["Name"])
                    });
                }
            }
            catch (Exception ex)
            {
                _log.Severe(label + " (AD_Client_ID=" + clientId + "): " + ex.Message);
            }
            return list;
        }

        #endregion

        #region Write

        /// <summary>
        /// Creates or updates one expected landed cost entry (C_ExpectedCost)
        /// through the MExpectedCost model, so tenant, organization, audit and
        /// active columns are populated the platform's way.
        ///
        /// Every rule is enforced here, independently of the client: the order
        /// must exist, be a purchase order (IsSOTrx = 'N') and still be drafted;
        /// the distribution must be Q, I or L; cost element, currency and rate
        /// type must exist and be active; and the amount must be greater than
        /// zero. A manipulated browser cannot bypass any of them.
        /// </summary>
        /// <param name="ctx">User context.</param>
        /// <param name="expectedCostId">0 to create, else the entry to update.</param>
        /// <param name="purchaseOrderId">Owning purchase order (create only).</param>
        /// <param name="distributionCode">Q | I | L.</param>
        /// <param name="costElementId">M_CostElement_ID.</param>
        /// <param name="description">Optional description.</param>
        /// <param name="amount">Entered amount, must be &gt; 0.</param>
        /// <param name="currencyId">C_Currency_ID of the entered amount.</param>
        /// <param name="conversionTypeId">C_ConversionType_ID (currency rate type).</param>
        public SaveResultData SaveExpectedCost(
            Ctx ctx, int expectedCostId, int purchaseOrderId, string distributionCode,
            int costElementId, string description, decimal amount,
            int currencyId, int conversionTypeId)
        {
            SaveResultData result = new SaveResultData();

            // ----- Field validation (before any database write) -----
            if (amount <= 0)
            {
                result.Message = GetMsg(ctx, "VAS_167_AmountMustBePositive",
                    "Amount must be greater than zero.");
                return result;
            }
            if (!IsAllowedDistribution(distributionCode))
            {
                result.Message = GetMsg(ctx, "VAS_167_InvalidDistribution",
                    "Select a valid cost distribution.");
                return result;
            }

            Trx trx = Trx.GetTrx(Trx.CreateTrxName("VAS167ELC"));
            try
            {
                MExpectedCost expectedCost;
                if (expectedCostId > 0)
                {
                    expectedCost = new MExpectedCost(ctx, expectedCostId, trx);
                    if (expectedCost.GetC_ExpectedCost_ID() <= 0)
                    {
                        result.Message = GetMsg(ctx, "VAS_167_EntryNotFound",
                            "This expected landed cost entry no longer exists.");
                        return result;
                    }
                    // The parent is taken from the stored record, never from the
                    // request, so an entry can never be re-parented onto another
                    // purchase order.
                    purchaseOrderId = expectedCost.GetC_Order_ID();
                }
                else
                {
                    expectedCost = null;
                }

                OrderGuardData guard = LoadOrderGuard(purchaseOrderId);
                if (!guard.Exists)
                {
                    result.Message = GetMsg(ctx, "VAS_167_OrderNotFound",
                        "Purchase order not found.");
                    return result;
                }
                if (!guard.IsPurchase)
                {
                    result.Message = GetMsg(ctx, "VAS_167_NotAPurchaseOrder",
                        "Expected landed cost applies to purchase orders only.");
                    return result;
                }
                if (!guard.IsDrafted)
                {
                    result.Message = GetMsg(ctx, "VAS_167_OnlyWhenDrafted",
                        "Expected landed cost can be changed only while the purchase order is drafted.");
                    return result;
                }

                if (!IsActiveLookup("M_CostElement", "M_CostElement_ID", costElementId, guard.ClientId))
                {
                    result.Message = GetMsg(ctx, "VAS_167_InvalidCostElement",
                        "Select a valid cost element.");
                    return result;
                }
                if (!IsActiveLookup("C_Currency", "C_Currency_ID", currencyId, guard.ClientId))
                {
                    result.Message = GetMsg(ctx, "VAS_167_InvalidCurrency",
                        "Select a valid currency.");
                    return result;
                }
                if (!IsActiveLookup("C_ConversionType", "C_ConversionType_ID", conversionTypeId, guard.ClientId))
                {
                    result.Message = GetMsg(ctx, "VAS_167_InvalidConversionType",
                        "Select a valid currency rate type.");
                    return result;
                }

                if (expectedCost == null)
                {
                    expectedCost = new MExpectedCost(ctx, 0, trx);
                    expectedCost.SetClientOrg(guard.ClientId, guard.OrgId);
                    expectedCost.SetC_Order_ID(purchaseOrderId);
                }

                expectedCost.SetLandedCostDistribution(distributionCode);
                expectedCost.SetM_CostElement_ID(costElementId);
                expectedCost.SetDescription(description);
                expectedCost.SetAmt(amount);
                // C_Currency_ID / C_ConversionType_ID have no generated typed
                // setter on X_C_ExpectedCost, so they are written through the
                // generic column accessor (same as MExpectedCost reads them).
                expectedCost.Set_Value("C_Currency_ID", currencyId);
                expectedCost.Set_Value("C_ConversionType_ID", conversionTypeId);

                if (!expectedCost.Save(trx))
                {
                    trx.Rollback();
                    result.Message = SaveErrorMessage(ctx);
                    return result;
                }

                trx.Commit();
                result.Success        = true;
                result.ExpectedCostId = expectedCost.GetC_ExpectedCost_ID();
            }
            catch (Exception ex)
            {
                try { trx.Rollback(); } catch { /* ignore */ }
                _log.Severe("SaveExpectedCost (C_ExpectedCost_ID=" + expectedCostId + "): " + ex.Message);
                result.Message = GetMsg(ctx, "VAS_167_SaveFailed",
                    "The expected landed cost could not be saved.");
            }
            finally
            {
                try { trx.Close(); } catch { /* ignore */ }
            }

            return result;
        }

        /// <summary>
        /// Deletes one expected landed cost entry. Only allowed while the parent
        /// purchase order is still drafted — re-checked against the database, so
        /// a completed order's entries cannot be removed whatever the client
        /// sends. The platform cascades the entry's C_ExpectedCostDistribution
        /// rows; a drafted order has none anyway.
        /// </summary>
        public SaveResultData DeleteExpectedCost(Ctx ctx, int expectedCostId)
        {
            SaveResultData result = new SaveResultData();
            if (expectedCostId <= 0)
            {
                result.Message = GetMsg(ctx, "VAS_167_EntryNotFound",
                    "This expected landed cost entry no longer exists.");
                return result;
            }

            Trx trx = Trx.GetTrx(Trx.CreateTrxName("VAS167ELCDel"));
            try
            {
                MExpectedCost expectedCost = new MExpectedCost(ctx, expectedCostId, trx);
                if (expectedCost.GetC_ExpectedCost_ID() <= 0)
                {
                    result.Message = GetMsg(ctx, "VAS_167_EntryNotFound",
                        "This expected landed cost entry no longer exists.");
                    return result;
                }

                OrderGuardData guard = LoadOrderGuard(expectedCost.GetC_Order_ID());
                if (!guard.Exists || !guard.IsPurchase || !guard.IsDrafted)
                {
                    result.Message = GetMsg(ctx, "VAS_167_OnlyWhenDrafted",
                        "Expected landed cost can be changed only while the purchase order is drafted.");
                    return result;
                }

                if (!expectedCost.Delete(true, trx))
                {
                    trx.Rollback();
                    result.Message = SaveErrorMessage(ctx);
                    return result;
                }

                trx.Commit();
                result.Success        = true;
                result.ExpectedCostId = expectedCostId;
            }
            catch (Exception ex)
            {
                try { trx.Rollback(); } catch { /* ignore */ }
                _log.Severe("DeleteExpectedCost (C_ExpectedCost_ID=" + expectedCostId + "): " + ex.Message);
                result.Message = GetMsg(ctx, "VAS_167_DeleteFailed",
                    "The expected landed cost could not be removed.");
            }
            finally
            {
                try { trx.Close(); } catch { /* ignore */ }
            }

            return result;
        }

        /// <summary>
        /// Reads the parent order's guard state (exists / purchase / drafted) plus
        /// its client and organization, straight from the database — the single
        /// source of truth for the write rules.
        /// </summary>
        private OrderGuardData LoadOrderGuard(int C_Order_ID)
        {
            OrderGuardData guard = new OrderGuardData();
            if (C_Order_ID <= 0) return guard;
            try
            {
                string sql = @"SELECT o.DocStatus, o.IsSOTrx, o.AD_Client_ID, o.AD_Org_ID
                                 FROM C_Order o
                                WHERE o.C_Order_ID = @C_Order_ID
                                  AND o.IsActive   = 'Y'";
                DataSet ds = DB.ExecuteDataset(sql, OrderParam(C_Order_ID), null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
                    return guard;

                DataRow r = ds.Tables[0].Rows[0];
                guard.Exists     = true;
                guard.IsPurchase = Util.GetValueOfString(r["IsSOTrx"]) == "N";
                guard.IsDrafted  = Util.GetValueOfString(r["DocStatus"]) == DOCSTATUS_Drafted;
                guard.ClientId   = Util.GetValueOfInt(r["AD_Client_ID"]);
                guard.OrgId      = Util.GetValueOfInt(r["AD_Org_ID"]);
            }
            catch (Exception ex)
            {
                _log.Severe("LoadOrderGuard (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
            }
            return guard;
        }

        /// <summary>
        /// Returns true when the given id exists, is active and is visible to the
        /// client on the given master table. Table and key column are supplied
        /// only from this class's own constants, never from a request.
        /// </summary>
        private bool IsActiveLookup(string tableName, string keyColumn, int id, int clientId)
        {
            if (id <= 0) return false;
            try
            {
                string sql = "SELECT COUNT(*) AS Found FROM " + tableName +
                             " WHERE " + keyColumn + " = @Id" +
                             " AND IsActive = 'Y' AND AD_Client_ID IN (0, @AD_Client_ID)";
                SqlParameter[] param = new SqlParameter[]
                {
                    new SqlParameter("@Id", id),
                    new SqlParameter("@AD_Client_ID", clientId)
                };
                DataSet ds = DB.ExecuteDataset(sql, param, null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
                    return false;
                return Util.GetValueOfInt(ds.Tables[0].Rows[0]["Found"]) > 0;
            }
            catch (Exception ex)
            {
                _log.Severe("IsActiveLookup (" + tableName + "=" + id + "): " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Turns the model layer's last error into a short, business-friendly
        /// message. MExpectedCost.BeforeSave raises its own (duplicate entry,
        /// zero amount) — those are worth showing; anything else falls back to a
        /// generic sentence so no SQL or stack detail reaches the browser.
        /// </summary>
        private string SaveErrorMessage(Ctx ctx)
        {
            try
            {
                ValueNamePair pp = VLogger.RetrieveError();
                if (pp != null)
                {
                    string name = pp.GetName();
                    if (!string.IsNullOrEmpty(name)) return name;
                    string value = pp.GetValue();
                    if (!string.IsNullOrEmpty(value))
                    {
                        string translated = Msg.GetMsg(ctx, value);
                        if (!string.IsNullOrEmpty(translated)) return translated;
                    }
                }
            }
            catch { /* fall through to the generic message */ }
            return GetMsg(ctx, "VAS_167_SaveFailed",
                "The expected landed cost could not be saved.");
        }

        #endregion

        #region Helpers

        private SqlParameter[] OrderParam(int C_Order_ID)
        {
            return new SqlParameter[] { new SqlParameter("@C_Order_ID", C_Order_ID) };
        }

        private bool IsAllowedDistribution(string code)
        {
            if (string.IsNullOrEmpty(code)) return false;
            foreach (string allowed in ALLOWED_DISTRIBUTIONS)
            {
                if (allowed == code) return true;
            }
            return false;
        }

        /// <summary>
        /// Display label for a stored C_LandedCostDistribution code. Only the
        /// three codes this panel exposes are mapped; anything else (an entry
        /// created elsewhere with C / V / W) shows its raw code rather than being
        /// silently relabelled.
        /// </summary>
        private string GetDistributionLabel(Ctx ctx, string code)
        {
            switch (code)
            {
                case "Q": return GetMsg(ctx, "VAS_167_ByQuantity", "By Quantity");
                case "I": return GetMsg(ctx, "VAS_167_ByValue",    "By Value");
                case "L": return GetMsg(ctx, "VAS_167_Equally",    "Equally");
                default:  return code;
            }
        }

        /// <summary>
        /// Prefers the seeded AD_Message, else the English fallback, so a
        /// deployment without the VAS_167_* keys never shows a raw key.
        /// </summary>
        private static string GetMsg(Ctx ctx, string key, string fallback)
        {
            try
            {
                string m = Msg.GetMsg(ctx, key);
                if (!string.IsNullOrEmpty(m) && m != key) return m;
            }
            catch { /* fall back */ }
            return fallback;
        }

        #endregion

        #region DTOs

        /// <summary>Full read payload for the panel.</summary>
        public class LandedCostPanelData
        {
            // ----- Order identity (read-only for this panel) -----
            public int       PurchaseOrderId     { get; set; }
            public string    PurchaseOrderNumber { get; set; }
            public DateTime? OrderDate           { get; set; }
            public string    DocumentTypeName    { get; set; }
            public string    VendorName          { get; set; }
            public string    BuyerName           { get; set; }
            public string    DocumentStatus      { get; set; }
            public bool      IsDrafted           { get; set; }
            public bool      IsCompleted         { get; set; }
            public decimal   PurchaseOrderTotal  { get; set; }

            // ----- Document currency -----
            public int    DocumentCurrencyId        { get; set; }
            public string DocumentCurrencyCode      { get; set; }
            public string DocumentCurrencySymbol    { get; set; }
            public int    DocumentCurrencyPrecision { get; set; }

            // ----- Allocation targets -----
            public int EligibleLineCount { get; set; }

            // ----- Entries + roll-ups -----
            public List<ExpectedCostData> ExpectedCosts { get; set; }
            public int     ExpectedCostCount           { get; set; }
            public decimal ExpectedCostTotalConverted  { get; set; }
            /// <summary>True when at least one entry has no usable exchange rate.</summary>
            public bool    HasMissingConversion        { get; set; }

            // ----- Lookups for the draft form -----
            public List<LookupItemData> CostElements    { get; set; }
            public List<LookupItemData> Currencies      { get; set; }
            public List<LookupItemData> ConversionTypes { get; set; }
            public List<LookupItemData> Distributions   { get; set; }

            public LandedCostPanelData()
            {
                ExpectedCosts   = new List<ExpectedCostData>();
                CostElements    = new List<LookupItemData>();
                Currencies      = new List<LookupItemData>();
                ConversionTypes = new List<LookupItemData>();
                Distributions   = new List<LookupItemData>();
            }
        }

        /// <summary>One C_ExpectedCost entry with its generated lines.</summary>
        public class ExpectedCostData
        {
            public int     ExpectedCostId      { get; set; }
            public int     PurchaseOrderId     { get; set; }
            public string  DistributionCode    { get; set; }
            public string  DistributionLabel   { get; set; }
            public int     CostElementId       { get; set; }
            public string  CostElementName     { get; set; }
            public string  Description         { get; set; }
            public decimal EnteredAmount       { get; set; }
            public int     EnteredCurrencyId   { get; set; }
            public string  EnteredCurrencyCode { get; set; }
            /// <summary>Precision of the entered currency — the generated lines are in it.</summary>
            public int     EnteredCurrencyPrecision { get; set; }
            public int     ConversionTypeId    { get; set; }
            public string  ConversionTypeName  { get; set; }

            /// <summary>Entered amount expressed in the order's document currency.</summary>
            public decimal ConvertedAmount      { get; set; }
            public string  DocumentCurrencyCode { get; set; }
            /// <summary>True when entered currency = document currency (no conversion needed).</summary>
            public bool    IsSameCurrency        { get; set; }
            /// <summary>False when no exchange rate could be resolved for this entry.</summary>
            public bool    IsConversionAvailable { get; set; }

            /// <summary>Sum of the generated lines' Base (the split denominator).</summary>
            public decimal TotalAllocationBase { get; set; }
            /// <summary>
            /// Sum of the generated lines' allocated amounts, in the ENTERED
            /// currency (that is what C_ExpectedCostDistribution.Amt stores).
            /// </summary>
            public decimal DistributedAmount   { get; set; }
            /// <summary>False when the generated lines no longer add up to EnteredAmount.</summary>
            public bool    IsReconciled        { get; set; }

            public List<GeneratedLineData> GeneratedLines { get; set; }

            public ExpectedCostData()
            {
                GeneratedLines = new List<GeneratedLineData>();
            }
        }

        /// <summary>One generated C_ExpectedCostDistribution row.</summary>
        public class GeneratedLineData
        {
            public int     DistributionLineId   { get; set; }
            public int     ExpectedCostId       { get; set; }
            public int     PurchaseOrderLineId  { get; set; }
            public int     LineNumber           { get; set; }
            public string  ProductName          { get; set; }
            public string  ProductCode          { get; set; }
            public decimal AllocationBase       { get; set; }
            public decimal TotalAllocationBase  { get; set; }
            public decimal LineQuantity         { get; set; }
            /// <summary>Allocated amount as stored — in <see cref="AmountCurrencyCode"/>.</summary>
            public decimal AllocatedAmount      { get; set; }
            /// <summary>ISO code the allocated amount is in: the parent entry's entered currency.</summary>
            public string  AmountCurrencyCode   { get; set; }
        }

        /// <summary>Id / name (or code / name) pair for a form dropdown.</summary>
        public class LookupItemData
        {
            public int    Id   { get; set; }
            public string Code { get; set; }
            public string Name { get; set; }
        }

        /// <summary>Result of a create / update / delete.</summary>
        public class SaveResultData
        {
            public bool   Success        { get; set; }
            public string Message        { get; set; }
            public int    ExpectedCostId { get; set; }
        }

        /// <summary>Parent order state the write rules are checked against.</summary>
        private class OrderGuardData
        {
            public bool Exists     { get; set; }
            public bool IsPurchase { get; set; }
            public bool IsDrafted  { get; set; }
            public int  ClientId   { get; set; }
            public int  OrgId      { get; set; }
        }

        #endregion
    }
}
