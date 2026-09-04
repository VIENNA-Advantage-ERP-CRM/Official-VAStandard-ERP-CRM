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
///   VAI163   2026-08-04  Surfaced DefaultConversionTypeId — the rate type a new
///                        entry comes up on. It is Spot (C_ConversionType.Value
///                        = 'S', or a rate type named Spot) resolved against the
///                        tenant's own rows, falling back to the client's default
///                        rate type in the platform's own order (IsDefault first,
///                        tenant rows before system rows). The Value column is
///                        read only where the dictionary carries it.
///   VAI163   2026-08-04  Entries are writable until the order is COMPLETED, not
///                        only while it is drafted: IsEditableStatus / IsEditable
///                        replace the drafted-only test on both the read payload
///                        and the write guard, locking on CO / CL / VO / RE. The
///                        generated distribution lines now carry the order line's
///                        Attribute Set Instance description.
///   VAI163   2026-08-04  Added the origins the order was raised from — the
///                        contract (LoadContractReference /
///                        C_Order.VAS_ContractMaster_ID, read under its own guard
///                        as the module column it is) and the RFQ
///                        (LoadRfqReference / C_RfQResponse.C_Order_ID, its
///                        identifier chosen under a DocumentNo column guard) and
///                        the project (LoadProjectReference /
///                        C_ProjectLine.C_OrderPO_ID) and the requisition
///                        (LoadRequisitionReference /
///                        M_RequisitionLine.C_OrderLine_ID, walking
///                        Requisition -> RFQ -> PO when there is no direct link)
///                        — and GetWindowId, which resolves an AD_Window_ID from
///                        a window NAME so the panel can open each of them in its
///                        own window (VAS_ContractMaster / VAS_RFQ / VAS_Project
///                        / VAS_Requisition).
///   VAI163   2026-08-07  Added the MRP plan the order was generated by
///                        (LoadPlanReference / VAMRP_PlanRun_ID) as a further
///                        origin: PlanRunId / PlanRunNo / PlanRunCount. VAMRP is
///                        an optional module and is not part of this solution, so
///                        its tables are reached through plain SQL under
///                        AD_Column guards — the id column is looked for on
///                        C_Order first and C_OrderLine second (the module stamps
///                        it in different places across revisions) and the plan
///                        run's identifier is whichever of DocumentNo / Name /
///                        Value / Description the schema carries
///                        (FirstExistingColumn, added with it). A deployment
///                        without VAMRP returns no plan and behaves exactly as
///                        before. Ported from VAS_092's LoadPlanOrigin.
///   VAI163   2026-08-07  Added the blanket purchase order this order was
///                        released against (LoadBlanketOrderReference /
///                        C_Order.C_Order_Blanket): BlanketOrderId /
///                        BlanketOrderNo. IsBlanketTrx = 'Y' is re-checked on the
///                        parent so a stale reference to an ordinary order is not
///                        named as a blanket, and C_Order_Blanket is read under an
///                        AD_Column guard as the module column it is. Ported from
///                        VAS_092's LoadOrigins/BlanketOrder.
///   VAI163   2026-08-19  The blanket order reference is read the way VAS_092
///                        reads it. This was ported from that panel BEFORE its
///                        two corrections, so it kept all three of the faults
///                        they fixed and a release order's card showed no blanket
///                        at all:
///                          - the ColumnExists("C_Order","C_Order_Blanket") gate
///                            is gone (it answers "absent" whenever AD_Column
///                            lacks the row, and its scalar sub-select RAISES
///                            where AD_Table has more than one row named C_Order —
///                            the catch turning that into "no such column"). The
///                            statement is attempted, and a schema that really
///                            has no such column reports it once per process;
///                          - IsBlanketTrx = 'Y' is no longer required on the
///                            parent, a second optional flag that hid the
///                            reference wherever it is not carried;
///                          - and the LINES are read when the header names
///                            nothing (LoadBlanketOrderReferenceFromLines). The
///                            two records of the link are written by DIFFERENT
///                            code — C_Order.C_Order_Blanket only by
///                            CreateReleaseDocFromBO, C_OrderLine.
///                            C_OrderLine_Blanket_ID by MOrder.CopyFrom for any
///                            release document type — so an order raised through
///                            any other path carries it on its lines ONLY.
///                        NVL gave way to COALESCE, and BlanketOrderCount reports
///                        a release drawing on more than one blanket.
/// </summary>

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Text;
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

        /// <summary>Drafted document status.</summary>
        private const string DOCSTATUS_Drafted = "DR";

        /// <summary>Completed document status — entries and their lines are frozen.</summary>
        private const string DOCSTATUS_Completed = "CO";

        /// <summary>
        /// The document statuses that freeze the expected landed cost. Everything
        /// before completion — drafted, in progress, invalid, waiting, not
        /// approved — is still editable: the platform generates the distribution
        /// lines when the order is completed, so an entry only becomes the
        /// platform's own output at that point. Closed / voided / reversed orders
        /// are past that point and are locked with completed ones.
        /// </summary>
        private static readonly string[] DOCSTATUS_Locked =
            new string[] { DOCSTATUS_Completed, "CL", "VO", "RE" };

        /// <summary>
        /// True while the order's expected landed cost may still be changed —
        /// any status that is not one of <see cref="DOCSTATUS_Locked"/>. An order
        /// with no status at all is treated as locked.
        /// </summary>
        private static bool IsEditableStatus(string docStatus)
        {
            if (string.IsNullOrEmpty(docStatus)) return false;
            for (int i = 0; i < DOCSTATUS_Locked.Length; i++)
            {
                if (DOCSTATUS_Locked[i] == docStatus) return false;
            }
            return true;
        }

        /// <summary>
        /// The C_LandedCostDistribution values this panel exposes. The reference
        /// list carries more (I = Import Value); it is deliberately not offered
        /// here and a create / update carrying it is rejected. The same set is
        /// the AD_Ref_List filter used by <see cref="LoadDistributions"/>, so the
        /// dropdown and the server-side check can never drift apart.
        /// </summary>
        private static readonly string[] ALLOWED_DISTRIBUTIONS =
            new string[] { "C", "L", "Q", "V", "W" };

        /// <summary>
        /// Cost elements this panel offers: material elements that carry no
        /// costing method — the ones a landed cost may be booked against.
        /// </summary>
        private const string COSTELEMENTTYPE_Material = "M";

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

            // Editable until the order is completed — the client mirrors this, the
            // write methods below re-check it against the database independently.
            result.IsDrafted   = result.DocumentStatus == DOCSTATUS_Drafted;
            result.IsCompleted = result.DocumentStatus == DOCSTATUS_Completed;
            result.IsEditable  = IsEditableStatus(result.DocumentStatus);

            int clientId = Util.GetValueOfInt(r["AD_Client_ID"]);
            int orgId    = Util.GetValueOfInt(r["AD_Org_ID"]);

            // Eligible allocation targets: active product lines. Surfaced so the
            // panel can say "nothing to allocate against" instead of the reader
            // discovering it only when completion fails.
            result.EligibleLineCount = GetEligibleLineCount(C_Order_ID);

            // ----- Origin references the order was raised from -----
            LoadSalesOrderReference(C_Order_ID, result);
            LoadContractReference(C_Order_ID, result);
            LoadRfqReference(C_Order_ID, result);
            LoadProjectReference(C_Order_ID, result);
            // After the RFQ: a requisition reached only through it needs that id.
            LoadRequisitionReference(C_Order_ID, result);
            // Blanket this order was released against (module-optional
            // C_Order.C_Order_Blanket).
            LoadBlanketOrderReference(C_Order_ID, result);
            // MRP plan run (module-optional VAMRP_PlanRun_ID).
            LoadPlanReference(C_Order_ID, result);

            // Reference-list labels for C_LandedCostDistribution, read once and
            // shared by the entry rows and the dropdown below.
            Dictionary<string, string> distributionNames = LoadDistributionNames();

            // ----- Expected landed cost entries (+ server-side conversion) -----
            result.ExpectedCosts = LoadExpectedCosts(ctx, result, clientId, orgId, distributionNames);

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
            result.Distributions   = LoadDistributions(distributionNames);
            // The rate type a new entry comes up on (Spot where the tenant has it).
            result.DefaultConversionTypeId = GetDefaultConversionTypeId(clientId);

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
            Ctx ctx, LandedCostPanelData order, int clientId, int orgId,
            Dictionary<string, string> distributionNames)
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
                    c.DistributionLabel   = GetDistributionLabel(distributionNames, c.DistributionCode);
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
                                  asi.Description                   AS AttributeSetInstance,
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
                                -- Attribute Set Instance of the allocated line, and
                                -- only a real one: the zero record carries a
                                -- placeholder description that is not an attribute.
                                LEFT OUTER JOIN M_AttributeSetInstance asi
                                       ON (asi.M_AttributeSetInstance_ID = ol.M_AttributeSetInstance_ID
                                           AND ol.M_AttributeSetInstance_ID > 0)
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
                    g.AttributeSetInstance = Util.GetValueOfString(r["AttributeSetInstance"]);
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

        /// <summary>
        /// Active cost elements (M_CostElement) visible to the client, restricted
        /// to material elements with no costing method. An element that carries a
        /// costing method drives product costing itself and is not a landed cost
        /// bucket, so it is kept out of the dropdown — and out of a save, see
        /// <see cref="IsValidCostElement"/>.
        /// </summary>
        private List<LookupItemData> LoadCostElements(int clientId)
        {
            return LoadLookup(
                @"SELECT ce.M_CostElement_ID AS Id, ce.Name AS Name
                    FROM M_CostElement ce
                   WHERE ce.IsActive = 'Y'
                     AND ce.AD_Client_ID IN (0, @AD_Client_ID)
                     AND ce.CostElementType = '" + COSTELEMENTTYPE_Material + @"'
                     AND ce.CostingMethod IS NULL
                   ORDER BY ce.Name",
                clientId, "LoadCostElements");
        }

        /// <summary>
        /// Active currencies (C_Currency) the tenant actually transacts in
        /// (IsMyCurrency = 'Y'), shown by ISO code. The form preselects the order's
        /// own currency; an order in a currency that is not flagged simply comes up
        /// with no preselection rather than widening the list.
        /// </summary>
        private List<LookupItemData> LoadCurrencies(int clientId)
        {
            return LoadLookup(
                @"SELECT c.C_Currency_ID AS Id, c.ISO_Code AS Name
                    FROM C_Currency c
                   WHERE c.IsActive = 'Y'
                     AND c.AD_Client_ID IN (0, @AD_Client_ID)
                     AND c.IsMyCurrency = 'Y'
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
        /// The rate type the form comes up on: Spot (C_ConversionType.Value = 'S'
        /// in the platform's own seed), which is what a landed cost is normally
        /// converted at. Where Spot cannot be identified — a tenant that renamed
        /// or re-seeded its rate types — this falls back to the client's default
        /// rate type, ordered exactly as the platform's own MConversionType
        /// .GetDefault does (IsDefault first, tenant rows before system rows).
        /// Returns 0 when there is nothing to preselect.
        /// </summary>
        private int GetDefaultConversionTypeId(int clientId)
        {
            try
            {
                // Value is the seed's stable code; it is read only where the
                // column exists, so a schema without it still resolves a default.
                string codeExpr = ColumnExists("C_ConversionType", "Value")
                    ? "ct.Value" : "NULL";

                string sql = @"SELECT ct.C_ConversionType_ID AS Id,
                                      " + codeExpr + @"      AS Code,
                                      ct.Name                AS Name
                                 FROM C_ConversionType ct
                                WHERE ct.IsActive = 'Y'
                                  AND ct.AD_Client_ID IN (0, @AD_Client_ID)
                                ORDER BY ct.IsDefault DESC, ct.AD_Client_ID DESC,
                                         ct.C_ConversionType_ID";
                SqlParameter[] param = new SqlParameter[]
                {
                    new SqlParameter("@AD_Client_ID", clientId)
                };
                DataSet ds = DB.ExecuteDataset(sql, param, null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
                    return 0;

                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    string code = Util.GetValueOfString(r["Code"]);
                    string name = Util.GetValueOfString(r["Name"]);
                    if (code == "S" ||
                        (name != null && name.Trim().StartsWith("Spot",
                            StringComparison.OrdinalIgnoreCase)))
                    {
                        return Util.GetValueOfInt(r["Id"]);
                    }
                }
                // No Spot: the client's default rate type (first row of the order).
                return Util.GetValueOfInt(ds.Tables[0].Rows[0]["Id"]);
            }
            catch (Exception ex)
            {
                _log.Severe("GetDefaultConversionTypeId (AD_Client_ID=" + clientId + "): " + ex.Message);
                return 0;
            }
        }

        /// <summary>
        /// True when the column exists in the dictionary. Used to keep a query off
        /// a column a given schema may not carry.
        /// </summary>
        private bool ColumnExists(string tableName, string columnName)
        {
            try
            {
                string sql = @"SELECT COUNT(*) FROM AD_Column
                                WHERE UPPER(ColumnName) = UPPER(@ColumnName)
                                  AND AD_Table_ID = (SELECT AD_Table_ID FROM AD_Table
                                                      WHERE UPPER(TableName) = UPPER(@TableName))";
                SqlParameter[] param = new SqlParameter[]
                {
                    new SqlParameter("@ColumnName", columnName),
                    new SqlParameter("@TableName", tableName)
                };
                return Util.GetValueOfInt(DB.ExecuteScalar(sql, param, null)) > 0;
            }
            catch (Exception ex)
            {
                _log.Severe("ColumnExists (" + tableName + "." + columnName + "): " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Returns the first of the candidate columns that exists on the table, or
        /// an empty string when the table has none of them. Used where an optional
        /// module names the same concept differently across its revisions.
        /// </summary>
        private string FirstExistingColumn(string tableName, string[] candidates)
        {
            for (int i = 0; i < candidates.Length; i++)
            {
                if (ColumnExists(tableName, candidates[i])) return candidates[i];
            }
            return "";
        }

        /// <summary>
        /// The cost distributions this panel exposes, read from the platform's own
        /// reference list (AD_Ref_List) behind C_ExpectedCost.LandedCostDistribution
        /// and narrowed to <see cref="ALLOWED_DISTRIBUTIONS"/>. Codes and labels are
        /// both the platform's — nothing is hard-coded here and no list value is
        /// created. If the reference cannot be resolved the codes are still offered,
        /// labelled with their raw value, so the form never comes up empty.
        /// </summary>
        private List<LookupItemData> LoadDistributions(Dictionary<string, string> names)
        {
            List<LookupItemData> list = new List<LookupItemData>();
            foreach (string code in ALLOWED_DISTRIBUTIONS)
            {
                string name;
                if (!names.TryGetValue(code, out name) || string.IsNullOrEmpty(name))
                    name = code;
                list.Add(new LookupItemData { Code = code, Name = name });
            }
            return list;
        }

        /// <summary>
        /// Reads the contract the order was raised under
        /// (C_Order.VAS_ContractMaster_ID) so the panel can name it and open it.
        ///
        /// Read in two independent steps, as the Purchase Order Overview does:
        /// (1) the id from C_Order alone, so a missing or unreadable
        /// VAS_ContractMaster table cannot suppress the reference; (2) the human
        /// DocumentNo enriched separately. The column is module-optional, so both
        /// steps are guarded and a deployment without it simply shows no contract.
        /// </summary>
        private void LoadContractReference(int C_Order_ID, LandedCostPanelData d)
        {
            try
            {
                string sql = @"SELECT o.VAS_ContractMaster_ID AS ContractId
                                 FROM C_Order o
                                WHERE o.C_Order_ID = @C_Order_ID";
                DataSet ds = DB.ExecuteDataset(sql, OrderParam(C_Order_ID), null);
                if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                    d.ContractMasterId = Util.GetValueOfInt(ds.Tables[0].Rows[0]["ContractId"]);
            }
            catch (Exception ex)
            {
                // A deployment without the column reaches here; keep the panel
                // working, just with no contract reference.
                _log.Severe("LoadContractReference/Id (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
            }

            if (d.ContractMasterId <= 0) return;

            try
            {
                string sql = @"SELECT cm.DocumentNo AS ContractNo
                                 FROM VAS_ContractMaster cm
                                WHERE cm.VAS_ContractMaster_ID = @VAS_ContractMaster_ID";
                SqlParameter[] param = new SqlParameter[]
                {
                    new SqlParameter("@VAS_ContractMaster_ID", d.ContractMasterId)
                };
                DataSet ds = DB.ExecuteDataset(sql, param, null);
                if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                    d.ContractMasterNo = Util.GetValueOfString(ds.Tables[0].Rows[0]["ContractNo"]);
            }
            catch (Exception ex)
            {
                // The number is a nicety; the reference still opens with its id.
                _log.Severe("LoadContractReference/No (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// Reads the request for quotation the order was raised from, so the panel
        /// can name it and open it. RfQCreatePO writes the order back onto the
        /// winning response (C_RfQResponse.C_Order_ID), so the response is what
        /// ties the two together. C_RfQ carries Name always and DocumentNo only in
        /// schemas that have it, so the identifier is chosen under a column guard.
        /// Non-fatal: a failure just leaves the panel without the reference.
        /// </summary>
        private void LoadRfqReference(int C_Order_ID, LandedCostPanelData d)
        {
            try
            {
                string rfqNoExpr = ColumnExists("C_RfQ", "DocumentNo")
                    ? "COALESCE(rq.DocumentNo, rq.Name)"
                    : "rq.Name";

                string sql = @"SELECT rq.C_RfQ_ID        AS RfqId,
                                      " + rfqNoExpr + @" AS RfqNo
                                 FROM C_RfQResponse rr
                                 INNER JOIN C_RfQ rq ON (rq.C_RfQ_ID = rr.C_RfQ_ID)
                                WHERE rr.C_Order_ID = @C_Order_ID
                                  AND COALESCE(rr.IsActive, 'Y') = 'Y'
                                  AND COALESCE(rq.IsActive, 'Y') = 'Y'
                                GROUP BY rq.C_RfQ_ID, " + rfqNoExpr + @"
                                ORDER BY rq.C_RfQ_ID";
                DataSet ds = DB.ExecuteDataset(sql, OrderParam(C_Order_ID), null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
                    return;

                DataRow r = ds.Tables[0].Rows[0];
                d.RfqId = Util.GetValueOfInt(r["RfqId"]);
                d.RfqNo = Util.GetValueOfString(r["RfqNo"]);
            }
            catch (Exception ex)
            {
                _log.Severe("LoadRfqReference (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// Reads the project the order was generated for, so the panel can name it
        /// and open it. ProjectGenPO stamps the order onto the project line it
        /// raised (C_ProjectLine.C_OrderPO_ID), so that column is the link. The
        /// project is identified by its Value, falling back to its Name.
        /// Non-fatal: a failure just leaves the panel without the reference.
        /// </summary>
        private void LoadProjectReference(int C_Order_ID, LandedCostPanelData d)
        {
            try
            {
                string sql = @"SELECT pj.C_Project_ID        AS ProjectId,
                                      COALESCE(pj.Value, pj.Name) AS ProjectNo
                                 FROM C_ProjectLine pl
                                 INNER JOIN C_Project pj ON (pj.C_Project_ID = pl.C_Project_ID)
                                WHERE pl.C_OrderPO_ID = @C_Order_ID
                                  AND COALESCE(pl.IsActive, 'Y') = 'Y'
                                  AND COALESCE(pj.IsActive, 'Y') = 'Y'
                                GROUP BY pj.C_Project_ID, COALESCE(pj.Value, pj.Name)
                                ORDER BY pj.C_Project_ID";
                DataSet ds = DB.ExecuteDataset(sql, OrderParam(C_Order_ID), null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
                    return;

                DataRow r = ds.Tables[0].Rows[0];
                d.ProjectId = Util.GetValueOfInt(r["ProjectId"]);
                d.ProjectNo = Util.GetValueOfString(r["ProjectNo"]);
            }
            catch (Exception ex)
            {
                _log.Severe("LoadProjectReference (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// Reads the requisition the order was raised from, so the panel can name
        /// it and open it. The direct path is the requisition line the order line
        /// was converted from (M_RequisitionLine.C_OrderLine_ID). A PO raised from
        /// an RFQ has no such link of its own, so the chain
        /// Requisition -> RFQ -> PO is walked through C_RfQ.M_Requisition_ID
        /// instead — which is why this runs after <see cref="LoadRfqReference"/>.
        /// Non-fatal: a failure just leaves the panel without the reference.
        /// </summary>
        // VAI163 2026-08-05: added LoadSalesOrderReference so the panel can name
        // and open the sales order behind a PO raised from one.

        /// <summary>
        /// Reads the sales order this purchase order was raised against
        /// (C_Order.Ref_Order_ID), so the panel can name it and open it.
        ///
        /// Both sides live in C_Order, so the referenced row is required to be a
        /// sales transaction — a Ref_Order_ID pointing at anything else is not a
        /// sales-order origin and is left alone. Non-fatal: a failure just leaves
        /// the panel without the reference.
        /// </summary>
        /// <param name="C_Order_ID">Selected purchase order id.</param>
        /// <param name="d">Panel payload being populated.</param>
        private void LoadSalesOrderReference(int C_Order_ID, LandedCostPanelData d)
        {
            try
            {
                string sql = @"SELECT so.C_Order_ID  AS SalesOrderId,
                                      so.DocumentNo  AS SalesOrderNo
                                 FROM C_Order o
                                 INNER JOIN C_Order so ON (so.C_Order_ID = o.Ref_Order_ID)
                                WHERE o.C_Order_ID = @C_Order_ID
                                  AND COALESCE(so.IsSOTrx, 'N') = 'Y'
                                  AND COALESCE(so.IsActive, 'Y') = 'Y'";
                DataSet ds = DB.ExecuteDataset(sql, OrderParam(C_Order_ID), null);
                if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                {
                    DataRow r0 = ds.Tables[0].Rows[0];
                    d.SalesOrderId = Util.GetValueOfInt(r0["SalesOrderId"]);
                    d.SalesOrderNo = Util.GetValueOfString(r0["SalesOrderNo"]);
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadSalesOrderReference (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
            }
        }

        private void LoadRequisitionReference(int C_Order_ID, LandedCostPanelData d)
        {
            try
            {
                string sql = @"SELECT r.M_Requisition_ID AS RequisitionId,
                                      r.DocumentNo       AS RequisitionNo
                                 FROM M_RequisitionLine rl
                                 INNER JOIN M_Requisition r ON (rl.M_Requisition_ID = r.M_Requisition_ID)
                                 INNER JOIN C_OrderLine ol  ON (rl.C_OrderLine_ID   = ol.C_OrderLine_ID)
                                WHERE ol.C_Order_ID = @C_Order_ID
                                  AND COALESCE(r.IsActive, 'Y') = 'Y'
                                GROUP BY r.M_Requisition_ID, r.DocumentNo
                                ORDER BY r.M_Requisition_ID";
                DataSet ds = DB.ExecuteDataset(sql, OrderParam(C_Order_ID), null);
                if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                {
                    DataRow r0 = ds.Tables[0].Rows[0];
                    d.RequisitionId = Util.GetValueOfInt(r0["RequisitionId"]);
                    d.RequisitionNo = Util.GetValueOfString(r0["RequisitionNo"]);
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadRequisitionReference (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
            }

            // Nothing direct: walk the rest of the chain through the RFQ.
            if (d.RequisitionId > 0 || d.RfqId <= 0) return;

            try
            {
                string sql = @"SELECT r.M_Requisition_ID AS RequisitionId,
                                      r.DocumentNo       AS RequisitionNo
                                 FROM C_RfQ rq
                                 INNER JOIN M_Requisition r
                                        ON (r.M_Requisition_ID = rq.M_Requisition_ID)
                                WHERE rq.C_RfQ_ID = @C_RfQ_ID
                                  AND COALESCE(r.IsActive, 'Y') = 'Y'";
                SqlParameter[] param = new SqlParameter[]
                {
                    new SqlParameter("@C_RfQ_ID", d.RfqId)
                };
                DataSet ds = DB.ExecuteDataset(sql, param, null);
                if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                {
                    DataRow r0 = ds.Tables[0].Rows[0];
                    d.RequisitionId = Util.GetValueOfInt(r0["RequisitionId"]);
                    d.RequisitionNo = Util.GetValueOfString(r0["RequisitionNo"]);
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadRequisitionReference/ViaRfQ (C_Order_ID="
                            + C_Order_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// Remembers whether the blanket lookup is usable against this schema, so
        /// a database that genuinely has no C_Order_Blanket column reports its
        /// error once rather than on every order the panel opens.
        /// Null = not tried yet, false = the statement failed, true = it ran.
        /// </summary>
        private static bool? _blanketLookupUsable;

        /// <summary>
        /// Reads the blanket purchase order this order was released against, so
        /// the panel can name it and open it.
        ///
        /// Three things this deliberately no longer does, each of which was on its
        /// own enough to leave a release order's card with no blanket on it — and
        /// each already corrected on VAS_092, which this reader was ported from
        /// before those corrections were made:
        ///
        ///   - It is not gated on ColumnExists("C_Order","C_Order_Blanket"). That
        ///     dictionary guard was the single point at which the reference failed
        ///     on BOTH databases: the column is written by the platform
        ///     (MOrder.SetC_Order_Blanket), but a deployment whose AD_Column has no
        ///     row for it — or whose AD_Table carries more than one row named
        ///     C_Order, which made the guard's scalar sub-select raise rather than
        ///     answer — reported it absent however good the data was. The statement
        ///     is attempted instead, and a genuinely missing column throws once.
        ///   - It does not require IsBlanketTrx = 'Y' on the parent. The release
        ///     order's own reference is the part of the link the platform always
        ///     writes; demanding a second optional flag on the other end hid the
        ///     reference wherever that flag is not carried.
        ///   - It does not stop at the header. See
        ///     <see cref="LoadBlanketOrderReferenceFromLines"/> — the link is
        ///     recorded in two places, by different code, and the header is the
        ///     half that is often not written.
        ///
        /// COALESCE rather than NVL, so the statement reads the same on Oracle and
        /// PostgreSQL.
        /// </summary>
        /// <param name="C_Order_ID">Selected purchase order id.</param>
        /// <param name="d">Panel payload being populated.</param>
        private void LoadBlanketOrderReference(int C_Order_ID, LandedCostPanelData d)
        {
            // The header reference first — it names the blanket outright.
            if (_blanketLookupUsable != false)
            {
                try
                {
                    string sql = @"SELECT bo.C_Order_ID  AS BlanketId,
                                          bo.DocumentNo  AS BlanketNo
                                     FROM C_Order o
                                     INNER JOIN C_Order bo
                                            ON (bo.C_Order_ID = o.C_Order_Blanket)
                                    WHERE o.C_Order_ID = @C_Order_ID
                                      AND COALESCE(bo.IsActive, 'Y') = 'Y'";
                    DataSet ds = DB.ExecuteDataset(sql, OrderParam(C_Order_ID), null);
                    _blanketLookupUsable = true;
                    if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                    {
                        DataRow r0 = ds.Tables[0].Rows[0];
                        d.BlanketOrderId = Util.GetValueOfInt(r0["BlanketId"]);
                        d.BlanketOrderNo = Util.GetValueOfString(r0["BlanketNo"]);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    // Almost certainly "no such column" on a schema without the
                    // blanket module. Recorded so the next order skips the attempt.
                    _blanketLookupUsable = false;
                    _log.Severe("LoadBlanketOrderReference/header (C_Order_ID="
                                + C_Order_ID + "): " + ex.Message);
                }
            }

            LoadBlanketOrderReferenceFromLines(C_Order_ID, d);
        }

        /// <summary>
        /// Remembers whether the line-level blanket lookup is usable against this
        /// schema, so a database without C_OrderLine_Blanket_ID reports it once
        /// rather than on every order the panel opens.
        /// </summary>
        private static bool? _blanketLineLookupUsable;

        /// <summary>
        /// The blanket this order was released against, resolved through its LINES
        /// (C_OrderLine.C_OrderLine_Blanket_ID -> the blanket's own order line ->
        /// that line's order).
        ///
        /// This is what makes the reference appear for release orders the header
        /// column does not describe, and that is not a rare case: the two records
        /// of the link are written by DIFFERENT code. C_Order.C_Order_Blanket is
        /// stamped only by the CreateReleaseDocFromBO process, which sets it
        /// explicitly after copying, whereas the LINE reference is written by
        /// MOrder.CopyFrom for ANY document whose type IsReleaseDocument(). A
        /// release order raised through any other path records its blanket on the
        /// lines ONLY, and a reader that stops at the header finds nothing to show
        /// — on every database, which is why this never looked like a portability
        /// fault.
        ///
        /// Distinct blanket orders are counted, so a release drawing on more than
        /// one blanket can report the first with a tally. Ordered by id so the
        /// choice is stable between loads.
        /// </summary>
        /// <param name="C_Order_ID">Selected purchase order id.</param>
        /// <param name="d">Panel payload being populated.</param>
        private void LoadBlanketOrderReferenceFromLines(int C_Order_ID, LandedCostPanelData d)
        {
            if (_blanketLineLookupUsable == false) return;

            try
            {
                string sql = @"SELECT bo.C_Order_ID AS BlanketId,
                                      MAX(bo.DocumentNo) AS BlanketNo
                                 FROM C_OrderLine ol
                                INNER JOIN C_OrderLine bol
                                        ON (bol.C_OrderLine_ID = ol.C_OrderLine_Blanket_ID)
                                INNER JOIN C_Order bo
                                        ON (bo.C_Order_ID = bol.C_Order_ID)
                                WHERE ol.C_Order_ID = @C_Order_ID
                                  AND COALESCE(ol.IsActive, 'Y') = 'Y'
                                  AND COALESCE(bo.IsActive, 'Y') = 'Y'
                                GROUP BY bo.C_Order_ID
                                ORDER BY bo.C_Order_ID";
                DataSet ds = DB.ExecuteDataset(sql, OrderParam(C_Order_ID), null);
                _blanketLineLookupUsable = true;
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return;

                DataRow r = ds.Tables[0].Rows[0];
                d.BlanketOrderId    = Util.GetValueOfInt(r["BlanketId"]);
                d.BlanketOrderNo    = Util.GetValueOfString(r["BlanketNo"]);
                d.BlanketOrderCount = ds.Tables[0].Rows.Count;
            }
            catch (Exception ex)
            {
                // A schema without C_OrderLine_Blanket_ID simply has no line-level
                // link to read. Recorded so the next order skips the attempt.
                _blanketLineLookupUsable = false;
                _log.Severe("LoadBlanketOrderReference/lines (C_Order_ID="
                            + C_Order_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// Fills PlanRunId / PlanRunNo / PlanRunCount from the MRP plan run that
        /// generated this order (VAMRP_PlanRun_ID), so a purchase order raised by
        /// a planning run names its plan on the details card and can open it.
        ///
        /// VAMRP is an optional module and is not part of this solution, so its
        /// tables are reached through plain SQL under AD_Column guards: the id
        /// column is looked for on C_Order first and on C_OrderLine second (the
        /// module stamps it in different places across revisions), and without
        /// either this is a no-op that leaves the card exactly as it was.
        ///
        /// Read in two independent steps, like the contract reference above: the
        /// id comes from the order alone, so an unreadable VAMRP_PlanRun table
        /// cannot suppress the reference — it just renders from the id.
        /// </summary>
        /// <param name="C_Order_ID">Selected purchase order id.</param>
        /// <param name="d">Panel payload being populated.</param>
        private void LoadPlanReference(int C_Order_ID, LandedCostPanelData d)
        {
            bool onHeader = ColumnExists("C_Order", "VAMRP_PlanRun_ID");
            bool onLine   = ColumnExists("C_OrderLine", "VAMRP_PlanRun_ID");
            if (!onHeader && !onLine) return;

            // --- Step 1: the plan run id(s) the order carries. ---
            try
            {
                string sql = onHeader
                    ? @"SELECT DISTINCT o.VAMRP_PlanRun_ID AS PlanRunId
                          FROM C_Order o
                         WHERE o.C_Order_ID = @C_Order_ID
                           AND COALESCE(o.VAMRP_PlanRun_ID, 0) > 0"
                    : @"SELECT DISTINCT ol.VAMRP_PlanRun_ID AS PlanRunId
                          FROM C_OrderLine ol
                         WHERE ol.C_Order_ID = @C_Order_ID
                           AND COALESCE(ol.IsActive, 'Y') = 'Y'
                           AND COALESCE(ol.VAMRP_PlanRun_ID, 0) > 0";
                DataSet ds = DB.ExecuteDataset(sql, OrderParam(C_Order_ID), null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return;

                d.PlanRunId = Util.GetValueOfInt(ds.Tables[0].Rows[0]["PlanRunId"]);
                // Several plan runs can feed one order when the id sits on the
                // lines; the card names the first and hints the rest with "+n".
                d.PlanRunCount = ds.Tables[0].Rows.Count;
            }
            catch (Exception ex)
            {
                _log.Severe("LoadPlanReference/PlanRunId (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
                return;
            }

            if (d.PlanRunId <= 0) return;

            // --- Step 2: the plan run's human identifier, whichever column this
            // revision of the module names it with. ---
            try
            {
                string noCol = FirstExistingColumn("VAMRP_PlanRun", new string[]
                {
                    "DocumentNo", "Name", "Value", "Description"
                });
                if (string.IsNullOrEmpty(noCol)) return;

                string sql = "SELECT pr." + noCol + @" AS PlanRunNo
                                FROM VAMRP_PlanRun pr
                               WHERE pr.VAMRP_PlanRun_ID = @VAMRP_PlanRun_ID";
                SqlParameter[] param = new SqlParameter[]
                {
                    new SqlParameter("@VAMRP_PlanRun_ID", d.PlanRunId)
                };
                DataSet ds = DB.ExecuteDataset(sql, param, null);
                if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                    d.PlanRunNo = Util.GetValueOfString(ds.Tables[0].Rows[0]["PlanRunNo"]);
            }
            catch (Exception ex)
            {
                // The number is a nicety; the reference still opens with its id.
                _log.Severe("LoadPlanReference/PlanRunNo (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// Resolves a window's AD_Window_ID from its name (AD_Window.Name) for the
        /// panel's record-open path — the contract, RFQ, project and requisition
        /// references open the VAS_ContractMaster / VAS_RFQ / VAS_Project /
        /// VAS_Requisition windows, named rather than derived from a zoom target.
        ///
        /// Restricted to windows this tenant can see (AD_Client_ID 0 or its own),
        /// preferring the tenant's own row over the system one. Whether the ROLE
        /// may open it is the platform's call, made when the window is started.
        /// </summary>
        /// <param name="ctx">User context (client).</param>
        /// <param name="windowName">Window name to resolve.</param>
        /// <returns>The window id, or 0 when the name resolves to nothing.</returns>
        public int GetWindowId(Ctx ctx, string windowName)
        {
            if (string.IsNullOrEmpty(windowName)) return 0;
            try
            {
                string sql = @"SELECT w.AD_Window_ID
                                 FROM AD_Window w
                                WHERE w.Name         = @Name
                                  AND w.IsActive     = 'Y'
                                  AND w.AD_Client_ID IN (0, @AD_Client_ID)
                                ORDER BY w.AD_Client_ID DESC";
                SqlParameter[] param = new SqlParameter[]
                {
                    new SqlParameter("@Name", windowName.Trim()),
                    new SqlParameter("@AD_Client_ID", ctx == null ? 0 : ctx.GetAD_Client_ID())
                };
                DataSet ds = DB.ExecuteDataset(sql, param, null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
                    return 0;
                return Util.GetValueOfInt(ds.Tables[0].Rows[0]["AD_Window_ID"]);
            }
            catch (Exception ex)
            {
                _log.Severe("GetWindowId (" + windowName + "): " + ex.Message);
                return 0;
            }
        }

        /// <summary>
        /// Code -> name for the allowed C_LandedCostDistribution reference-list
        /// values. The reference is resolved through AD_Column rather than by a
        /// hard-coded AD_Reference_ID, so a re-seeded dictionary cannot break it.
        /// Degrades to an empty map, never throws.
        /// </summary>
        private Dictionary<string, string> LoadDistributionNames()
        {
            Dictionary<string, string> map = new Dictionary<string, string>();
            try
            {
                string sql = @"SELECT rl.Value AS Code, rl.Name AS Name
                                 FROM AD_Ref_List rl
                                WHERE rl.IsActive = 'Y'
                                  AND rl.Value IN (" + AllowedDistributionSqlList() + @")
                                  AND rl.AD_Reference_ID = (SELECT c.AD_Reference_Value_ID
                                                              FROM AD_Column c
                                                             INNER JOIN AD_Table t
                                                                    ON (t.AD_Table_ID = c.AD_Table_ID)
                                                             WHERE t.TableName  = 'C_ExpectedCost'
                                                               AND c.ColumnName = 'LandedCostDistribution')";
                DataSet ds = DB.ExecuteDataset(sql, null, null);
                if (ds == null || ds.Tables.Count == 0) return map;
                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    string code = Util.GetValueOfString(r["Code"]);
                    if (!string.IsNullOrEmpty(code) && !map.ContainsKey(code))
                        map[code] = Util.GetValueOfString(r["Name"]);
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadDistributionNames: " + ex.Message);
            }
            return map;
        }

        /// <summary>
        /// <see cref="ALLOWED_DISTRIBUTIONS"/> as a quoted SQL IN list. The values
        /// are compile-time constants of this class, never request input.
        /// </summary>
        private static string AllowedDistributionSqlList()
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < ALLOWED_DISTRIBUTIONS.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append("'").Append(ALLOWED_DISTRIBUTIONS[i]).Append("'");
            }
            return sb.ToString();
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
        /// the distribution must be one of C / L / Q / V / W; the cost element must
        /// be an active material element with no costing method; the currency must
        /// be a transacting currency (IsMyCurrency = 'Y'); the rate type must exist
        /// and be active; and the amount must be greater than zero. A manipulated
        /// browser cannot bypass any of them.
        /// </summary>
        /// <param name="ctx">User context.</param>
        /// <param name="expectedCostId">0 to create, else the entry to update.</param>
        /// <param name="purchaseOrderId">Owning purchase order (create only).</param>
        /// <param name="distributionCode">C | L | Q | V | W.</param>
        /// <param name="costElementId">M_CostElement_ID (material, no costing method).</param>
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
                if (!guard.IsEditable)
                {
                    result.Message = GetMsg(ctx, "VAS_167_OnlyBeforeCompleted",
                        "Expected landed cost can be changed only before the purchase order is completed.");
                    return result;
                }

                if (!IsValidCostElement(costElementId, guard.ClientId))
                {
                    result.Message = GetMsg(ctx, "VAS_167_InvalidCostElement",
                        "Select a valid cost element.");
                    return result;
                }
                if (!IsValidCurrency(currencyId, guard.ClientId))
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
                if (!guard.Exists || !guard.IsPurchase || !guard.IsEditable)
                {
                    result.Message = GetMsg(ctx, "VAS_167_OnlyBeforeCompleted",
                        "Expected landed cost can be changed only before the purchase order is completed.");
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
                guard.IsEditable = IsEditableStatus(Util.GetValueOfString(r["DocStatus"]));
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
        /// Cost element check for a save: active, visible to the tenant, and the
        /// same material / no-costing-method restriction the dropdown applies.
        /// A browser posting an element that is not on the list is rejected.
        /// </summary>
        private bool IsValidCostElement(int costElementId, int clientId)
        {
            if (costElementId <= 0) return false;
            try
            {
                string sql = @"SELECT COUNT(*) AS Found
                                 FROM M_CostElement ce
                                WHERE ce.M_CostElement_ID = @Id
                                  AND ce.IsActive = 'Y'
                                  AND ce.AD_Client_ID IN (0, @AD_Client_ID)
                                  AND ce.CostElementType = '" + COSTELEMENTTYPE_Material + @"'
                                  AND ce.CostingMethod IS NULL";
                SqlParameter[] param = new SqlParameter[]
                {
                    new SqlParameter("@Id", costElementId),
                    new SqlParameter("@AD_Client_ID", clientId)
                };
                DataSet ds = DB.ExecuteDataset(sql, param, null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
                    return false;
                return Util.GetValueOfInt(ds.Tables[0].Rows[0]["Found"]) > 0;
            }
            catch (Exception ex)
            {
                _log.Severe("IsValidCostElement (M_CostElement_ID=" + costElementId + "): " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Currency check for a save: active, visible to the tenant and flagged as
        /// a transacting currency (IsMyCurrency = 'Y') — exactly what the dropdown
        /// offers.
        /// </summary>
        private bool IsValidCurrency(int currencyId, int clientId)
        {
            if (currencyId <= 0) return false;
            try
            {
                string sql = @"SELECT COUNT(*) AS Found
                                 FROM C_Currency c
                                WHERE c.C_Currency_ID = @Id
                                  AND c.IsActive = 'Y'
                                  AND c.AD_Client_ID IN (0, @AD_Client_ID)
                                  AND c.IsMyCurrency = 'Y'";
                SqlParameter[] param = new SqlParameter[]
                {
                    new SqlParameter("@Id", currencyId),
                    new SqlParameter("@AD_Client_ID", clientId)
                };
                DataSet ds = DB.ExecuteDataset(sql, param, null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
                    return false;
                return Util.GetValueOfInt(ds.Tables[0].Rows[0]["Found"]) > 0;
            }
            catch (Exception ex)
            {
                _log.Severe("IsValidCurrency (C_Currency_ID=" + currencyId + "): " + ex.Message);
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
        /// Display label for a stored C_LandedCostDistribution code, taken from
        /// the reference list. Only the codes this panel exposes are in the map;
        /// anything else (an entry created elsewhere with I = Import Value) shows
        /// its raw code rather than being silently relabelled.
        /// </summary>
        private string GetDistributionLabel(Dictionary<string, string> names, string code)
        {
            if (string.IsNullOrEmpty(code)) return code;
            string name;
            if (names != null && names.TryGetValue(code, out name) && !string.IsNullOrEmpty(name))
                return name;
            return code;
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
            /// <summary>True while the expected landed cost may still be changed —
            /// every status before the order is completed (drafted, in progress …).
            /// This is what the panel's edit affordances are driven by.</summary>
            public bool      IsEditable          { get; set; }
            public decimal   PurchaseOrderTotal  { get; set; }
            /// <summary>Contract the order was raised under —
            /// C_Order.VAS_ContractMaster_ID; 0 when there is none.</summary>
            public int       ContractMasterId    { get; set; }
            /// <summary>VAS_ContractMaster.DocumentNo of that contract.</summary>
            public string    ContractMasterNo    { get; set; }
            /// <summary>RFQ the order was raised from, reached through the winning
            /// response (C_RfQResponse.C_Order_ID); 0 when there is none.</summary>
            public int       RfqId               { get; set; }
            /// <summary>That RFQ's DocumentNo, or its Name where the schema
            /// carries no DocumentNo.</summary>
            public string    RfqNo               { get; set; }
            /// <summary>Project the order was generated for, via
            /// C_ProjectLine.C_OrderPO_ID; 0 when there is none.</summary>
            public int       ProjectId           { get; set; }
            /// <summary>That project's Value, falling back to its Name.</summary>
            public string    ProjectNo           { get; set; }
            /// <summary>Requisition the order was raised from, directly or through
            /// the RFQ; 0 when there is none.</summary>
            public int       RequisitionId       { get; set; }
            /// <summary>That requisition's DocumentNo.</summary>
            public string    RequisitionNo       { get; set; }
            /// <summary>Sales order the PO was raised against
            /// (C_Order.Ref_Order_ID); 0 when there is none.</summary>
            public int       SalesOrderId        { get; set; }
            /// <summary>That sales order's DocumentNo.</summary>
            public string    SalesOrderNo        { get; set; }
            /// <summary>Blanket purchase order this order was released against
            /// (C_Order.C_Order_Blanket, else the blanket reached through the
            /// order's LINES); 0 when there is none.</summary>
            public int       BlanketOrderId      { get; set; }
            /// <summary>That blanket order's DocumentNo.</summary>
            public string    BlanketOrderNo      { get; set; }
            /// <summary>Distinct blanket orders this one draws on. More than one
            /// is possible when the link is carried by the LINES, which can each
            /// release from a different blanket; the card names the first and
            /// hints the rest. 0 or 1 when the header named it outright.</summary>
            public int       BlanketOrderCount   { get; set; }
            /// <summary>MRP plan run the order was generated by
            /// (VAMRP_PlanRun_ID); 0 when there is none, and always 0 on a
            /// deployment without the optional VAMRP module.</summary>
            public int       PlanRunId           { get; set; }
            /// <summary>That plan run's identifier — whichever of DocumentNo /
            /// Name / Value / Description the module's schema carries.</summary>
            public string    PlanRunNo           { get; set; }
            /// <summary>Distinct plan runs feeding this order. More than one is
            /// possible when the id sits on the lines rather than the header; the
            /// card names the first and hints the rest.</summary>
            public int       PlanRunCount        { get; set; }

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
            /// <summary>Rate type a new entry comes up on — Spot where the tenant
            /// has it, else the client's default. 0 = nothing to preselect.</summary>
            public int DefaultConversionTypeId { get; set; }

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
            /// <summary>M_AttributeSetInstance.Description of the allocated order
            /// line (size / lot / serial ...); empty when the line carries no
            /// instance.</summary>
            public string  AttributeSetInstance { get; set; }
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
            /// <summary>Order is not yet completed, so its entries may be written.</summary>
            public bool IsEditable { get; set; }
            public int  ClientId   { get; set; }
            public int  OrgId      { get; set; }
        }

        #endregion
    }
}
