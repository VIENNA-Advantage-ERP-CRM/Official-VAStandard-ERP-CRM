/// <summary>
/// Module Name : VASLogic
/// Purpose     : Purchase Order Overview tab panel data (read side).
///               Returns header identity, vendor, linked origin documents,
///               stat strip, 7-stage progress, line items with received
///               progress, terms/notes and recent activity for a selected
///               purchase order (C_Order with IsSoTrx = 'N').
/// Chronological development:
///   VAI163   2026-06-10  Created
///   VAI163   2026-06-15  Added per-stage progress dates (receipt / invoice /
///                        payment) and the landed-cost section (expected from
///                        C_ExpectedCostDistribution, actual from
///                        C_LandedCostAllocation, distribution from
///                        C_ExpectedCost).
///   VAI163   2026-06-17  Reworked landed cost into a per-component view
///                        (one row per cost element + distribution) combining
///                        expected (C_ExpectedCost) with invoice-linked actual
///                        (C_LandedCostAllocation) by cost element, with section
///                        roll-ups. Fixed the expected-amount column name
///                        (C_ExpectedCost.Amt / C_ExpectedCostDistribution.Amt,
///                        was Amount).
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
    public class VAS_092_OverviewPurchaseOrderModel
    {
        private static readonly VLogger _log =
            VLogger.GetVLogger(typeof(VAS_092_OverviewPurchaseOrderModel).FullName);

        /// <summary>
        /// Returns the full overview payload for the selected purchase order.
        /// MRole access filtering is applied ONLY on the main physical table
        /// (C_Order alias "o"); child line/notes queries inherit the parent's
        /// authorization and are not separately role-filtered.
        /// </summary>
        /// <param name="ctx">User context (client/org/role).</param>
        /// <param name="C_Order_ID">Selected purchase order id.</param>
        /// <returns>Populated <see cref="PurchaseOrderOverviewData"/>; an empty
        /// instance when the id is invalid or no accessible row is found.</returns>
        public PurchaseOrderOverviewData GetPurchaseOrderOverview(Ctx ctx, int C_Order_ID)
        {
            PurchaseOrderOverviewData result = new PurchaseOrderOverviewData();
            if (C_Order_ID <= 0) return result;

            string sql = @"SELECT
                              o.C_Order_ID,
                              o.DocumentNo,
                              o.DateOrdered,
                              o.DatePromised,
                              o.DocStatus,
                              o.Created,
                              o.GrandTotal,
                              o.TotalLines,
                              o.C_BPartner_ID,
                              o.Description       AS OrderDescription,
                              o.POReference,
                              o.Ref_Order_ID,
                              bp.Name             AS VendorName,
                              bpc.Name            AS ContactName,
                              bpc.Phone           AS ContactPhone,
                              bpc.EMail           AS ContactEmail,
                              sr.Name             AS BuyerName,
                              cu.Name             AS CreatedByName,
                              pt.Name             AS PaymentTermName,
                              cur.CurSymbol       AS CurSymbol,
                              cur.ISO_Code        AS ISO_Code,
                              cur.StdPrecision    AS StdPrecision,
                              wh.Name             AS WarehouseName,
                              org.Name            AS OrgName,
                              loc.Address1        AS Address1,
                              loc.Address2        AS Address2,
                              loc.City            AS City,
                              loc.Postal          AS Postal,
                              ctry.Name           AS CountryName,
                              reg.Name            AS RegionName,
                              refo.DocumentNo     AS RefOrderDocNo,
                              (SELECT NVL(SUM(ol.QtyOrdered), 0)
                                 FROM C_OrderLine ol
                                WHERE ol.C_Order_ID = o.C_Order_ID
                                  AND ol.IsActive   = 'Y')                       AS TotalQtyOrdered,
                              (SELECT NVL(SUM(ol.QtyDelivered), 0)
                                 FROM C_OrderLine ol
                                WHERE ol.C_Order_ID = o.C_Order_ID
                                  AND ol.IsActive   = 'Y')                       AS TotalQtyDelivered,
                              (SELECT NVL(SUM(ol.QtyInvoiced), 0)
                                 FROM C_OrderLine ol
                                WHERE ol.C_Order_ID = o.C_Order_ID
                                  AND ol.IsActive   = 'Y')                       AS TotalQtyInvoiced,
                              (SELECT COUNT(*)
                                 FROM C_OrderLine ol
                                WHERE ol.C_Order_ID = o.C_Order_ID
                                  AND ol.IsActive   = 'Y'
                                  AND (ol.M_Product_ID > 0 OR ol.C_Charge_ID > 0)) AS LineCount,
                              (SELECT COUNT(*)
                                 FROM C_OrderLine ol
                                WHERE ol.C_Order_ID  = o.C_Order_ID
                                  AND ol.IsActive    = 'Y'
                                  AND ol.QtyOrdered  > 0
                                  AND ol.QtyDelivered >= ol.QtyOrdered)          AS FullyReceivedLineCount,
                              (SELECT COUNT(*)
                                 FROM M_RequisitionLine rl
                                 INNER JOIN C_OrderLine ol2
                                    ON (rl.C_OrderLine_ID = ol2.C_OrderLine_ID)
                                WHERE ol2.C_Order_ID = o.C_Order_ID)             AS RequisitionLineCount,
                              (SELECT COUNT(*)
                                 FROM C_Invoice ci
                                WHERE ci.C_Order_ID = o.C_Order_ID
                                  AND ci.IsActive   = 'Y'
                                  AND ci.DocStatus IN ('CO', 'CL'))             AS OrderInvoiceCount,
                              (SELECT COUNT(*)
                                 FROM C_Invoice ci
                                WHERE ci.C_Order_ID = o.C_Order_ID
                                  AND ci.IsActive   = 'Y'
                                  AND ci.IsPaid     = 'Y'
                                  AND ci.DocStatus IN ('CO', 'CL'))             AS PaidInvoiceCount,
                              (SELECT MAX(io.MovementDate)
                                 FROM M_InOut io
                                WHERE io.C_Order_ID = o.C_Order_ID
                                  AND io.IsActive   = 'Y'
                                  AND io.DocStatus IN ('CO', 'CL'))             AS LastReceiptDate,
                              (SELECT MAX(ci.DateInvoiced)
                                 FROM C_Invoice ci
                                WHERE ci.C_Order_ID = o.C_Order_ID
                                  AND ci.IsActive   = 'Y'
                                  AND ci.DocStatus IN ('CO', 'CL'))             AS LastInvoiceDate,
                              (SELECT MAX(p.DateTrx)
                                 FROM C_Payment p
                                 INNER JOIN C_AllocationLine al ON (al.C_Payment_ID = p.C_Payment_ID)
                                 INNER JOIN C_Invoice ci2       ON (al.C_Invoice_ID = ci2.C_Invoice_ID)
                                WHERE ci2.C_Order_ID = o.C_Order_ID
                                  AND p.IsActive     = 'Y'
                                  AND p.DocStatus IN ('CO', 'CL'))             AS LastPaymentDate,
                              TRUNC(CURRENT_DATE) AS SystemDate
                            FROM C_Order o
                            INNER JOIN C_BPartner bp        ON (o.C_BPartner_ID          = bp.C_BPartner_ID)
                            LEFT OUTER JOIN AD_User bpc      ON (o.AD_User_ID             = bpc.AD_User_ID)
                            LEFT OUTER JOIN AD_User sr       ON (o.SalesRep_ID            = sr.AD_User_ID)
                            LEFT OUTER JOIN AD_User cu       ON (o.CreatedBy              = cu.AD_User_ID)
                            LEFT OUTER JOIN C_PaymentTerm pt ON (o.C_PaymentTerm_ID       = pt.C_PaymentTerm_ID)
                            INNER JOIN C_Currency cur        ON (o.C_Currency_ID          = cur.C_Currency_ID)
                            LEFT OUTER JOIN M_Warehouse wh   ON (o.M_Warehouse_ID         = wh.M_Warehouse_ID)
                            LEFT OUTER JOIN AD_Org org       ON (o.AD_Org_ID              = org.AD_Org_ID)
                            LEFT OUTER JOIN C_BPartner_Location bpl ON (o.C_BPartner_Location_ID = bpl.C_BPartner_Location_ID)
                            LEFT OUTER JOIN C_Location loc   ON (bpl.C_Location_ID        = loc.C_Location_ID)
                            LEFT OUTER JOIN C_Country ctry   ON (loc.C_Country_ID         = ctry.C_Country_ID)
                            LEFT OUTER JOIN C_Region reg     ON (loc.C_Region_ID          = reg.C_Region_ID)
                            LEFT OUTER JOIN C_Order refo     ON (o.Ref_Order_ID           = refo.C_Order_ID)
                            WHERE o.C_Order_ID = @C_Order_ID
                              AND o.IsSoTrx    = 'N'";

            // MRole access only on the primary physical table the user is fetching.
            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql, "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            SqlParameter[] param = new SqlParameter[]
            {
                new SqlParameter("@C_Order_ID", C_Order_ID)
            };

            DataSet ds = DB.ExecuteDataset(sql, param, null);
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
                return result;

            DataRow r = ds.Tables[0].Rows[0];

            // ----- Header / identity -----
            result.C_Order_ID    = Util.GetValueOfInt(r["C_Order_ID"]);
            result.DocumentNo    = Util.GetValueOfString(r["DocumentNo"]);
            result.DateOrdered   = Util.GetValueOfDateTime(r["DateOrdered"]);
            result.DatePromised  = Util.GetValueOfDateTime(r["DatePromised"]);
            result.DocStatus     = Util.GetValueOfString(r["DocStatus"]);
            result.Created       = Util.GetValueOfDateTime(r["Created"]);
            result.C_BPartner_ID = Util.GetValueOfInt(r["C_BPartner_ID"]);
            result.OrderDescription = Util.GetValueOfString(r["OrderDescription"]);
            result.POReference   = Util.GetValueOfString(r["POReference"]);

            result.VendorName    = Util.GetValueOfString(r["VendorName"]);
            result.ContactName   = Util.GetValueOfString(r["ContactName"]);
            result.ContactPhone  = Util.GetValueOfString(r["ContactPhone"]);
            result.ContactEmail  = Util.GetValueOfString(r["ContactEmail"]);
            result.BuyerName     = Util.GetValueOfString(r["BuyerName"]);
            result.CreatedByName = Util.GetValueOfString(r["CreatedByName"]);
            result.PaymentTermName = Util.GetValueOfString(r["PaymentTermName"]);
            result.WarehouseName = Util.GetValueOfString(r["WarehouseName"]);
            result.OrgName       = Util.GetValueOfString(r["OrgName"]);

            result.CurSymbol     = Util.GetValueOfString(r["CurSymbol"]);
            result.ISO_Code      = Util.GetValueOfString(r["ISO_Code"]);
            result.StdPrecision  = Util.GetValueOfInt(r["StdPrecision"]);

            result.VendorAddress = BuildAddress(
                Util.GetValueOfString(r["Address1"]),
                Util.GetValueOfString(r["Address2"]),
                Util.GetValueOfString(r["City"]),
                Util.GetValueOfString(r["RegionName"]),
                Util.GetValueOfString(r["Postal"]),
                Util.GetValueOfString(r["CountryName"]));

            // ----- Header totals -----
            result.GrandTotal    = Util.GetValueOfDecimal(r["GrandTotal"]);
            result.TotalLines    = Util.GetValueOfDecimal(r["TotalLines"]);
            result.TaxAmt        = result.GrandTotal - result.TotalLines;

            // ----- Stat-strip aggregates -----
            result.TotalQtyOrdered       = Util.GetValueOfDecimal(r["TotalQtyOrdered"]);
            result.TotalQtyDelivered     = Util.GetValueOfDecimal(r["TotalQtyDelivered"]);
            result.TotalQtyInvoiced      = Util.GetValueOfDecimal(r["TotalQtyInvoiced"]);
            result.LineCount             = Util.GetValueOfInt(r["LineCount"]);
            result.FullyReceivedLineCount = Util.GetValueOfInt(r["FullyReceivedLineCount"]);

            // ----- Linked / origin documents -----
            result.RefOrderDocNo      = Util.GetValueOfString(r["RefOrderDocNo"]);
            result.RequisitionLineCount = Util.GetValueOfInt(r["RequisitionLineCount"]);

            // ----- 7-stage progress -----
            int orderInvoiceCount = Util.GetValueOfInt(r["OrderInvoiceCount"]);
            int paidInvoiceCount  = Util.GetValueOfInt(r["PaidInvoiceCount"]);

            bool isCompleted    = result.DocStatus == "CO" || result.DocStatus == "CL";
            bool delivered      = result.TotalQtyDelivered > 0;
            bool fullyDelivered = result.TotalQtyOrdered > 0
                                  && result.TotalQtyDelivered >= result.TotalQtyOrdered;
            bool invoiced       = orderInvoiceCount > 0;
            bool paid           = orderInvoiceCount > 0 && paidInvoiceCount >= orderInvoiceCount;

            result.IsCompleted        = isCompleted;
            result.IsWithVendor       = isCompleted;
            result.IsExpectedDelivery = isCompleted && result.DatePromised.HasValue;
            result.IsPartialDelivered = delivered;
            result.IsFullyDelivered   = fullyDelivered;
            result.IsInvoiceRaised    = invoiced;
            result.IsPaymentDone      = paid;
            result.CurrentStage       = ComputeCurrentStage(result);

            // ----- Per-stage action dates (for the progress stepper) -----
            result.LastReceiptDate = Util.GetValueOfDateTime(r["LastReceiptDate"]);
            result.LastInvoiceDate = Util.GetValueOfDateTime(r["LastInvoiceDate"]);
            result.LastPaymentDate = Util.GetValueOfDateTime(r["LastPaymentDate"]);

            // ----- Derived priority (no stored PO priority column) -----
            DateTime systemDate = Util.GetValueOfDateTime(r["SystemDate"])
                                      .GetValueOrDefault(DateTime.Today);
            result.Priority = ComputePriority(result, systemDate);

            // ----- Line items -----
            result.Lines = LoadLines(C_Order_ID, result.StdPrecision);

            // ----- Terms & notes / recent activity (CM_ChatEntry) -----
            result.Activity = LoadActivity(C_Order_ID);

            // ----- Landed cost (per cost-component: expected vs actual) -----
            result.LandedCostComponents = LoadLandedCostComponents(result);

            return result;
        }

        /// <summary>
        /// Determines the current progress stage (1..7) as the highest reached
        /// milestone. Stage 5 (Partial Delivered) is "reached" once any quantity
        /// has been received.
        /// </summary>
        /// <param name="d">Partly-populated overview data.</param>
        /// <returns>The 1-based current stage number.</returns>
        private int ComputeCurrentStage(PurchaseOrderOverviewData d)
        {
            int stage = 1;                              // Drafted (always reached)
            if (d.IsCompleted)        stage = 3;        // Completed + With Vendor
            if (d.IsExpectedDelivery) stage = 4;        // Expected Delivery scheduled
            if (d.IsPartialDelivered) stage = 5;        // Partial / received
            if (d.IsInvoiceRaised)    stage = 6;        // Invoice Raised
            if (d.IsPaymentDone)      stage = 7;        // Payment Done
            return stage;
        }

        /// <summary>
        /// Derives a display priority from delivery state and promised date,
        /// since C_Order carries no stored priority for the overview.
        /// </summary>
        /// <param name="d">Partly-populated overview data.</param>
        /// <param name="systemDate">DB system date (TRUNC CURRENT_DATE).</param>
        /// <returns>"high", "med" or "low".</returns>
        private string ComputePriority(PurchaseOrderOverviewData d, DateTime systemDate)
        {
            if (d.IsFullyDelivered) return "low";
            if (d.DatePromised.HasValue)
            {
                int days = (int)Math.Round(
                    (d.DatePromised.Value.Date - systemDate.Date).TotalDays);
                if (days < 0) return "high";            // promised date passed, not fully received
                if (days <= 7) return "med";
            }
            return "low";
        }

        /// <summary>
        /// Concatenates the available C_Location parts into a single display
        /// address, skipping empty segments.
        /// </summary>
        private string BuildAddress(string address1, string address2, string city,
                                    string region, string postal, string country)
        {
            List<string> parts = new List<string>();
            if (!string.IsNullOrEmpty(address1)) parts.Add(address1.Trim());
            if (!string.IsNullOrEmpty(address2)) parts.Add(address2.Trim());

            List<string> cityLine = new List<string>();
            if (!string.IsNullOrEmpty(city))    cityLine.Add(city.Trim());
            if (!string.IsNullOrEmpty(region))  cityLine.Add(region.Trim());
            if (!string.IsNullOrEmpty(postal))  cityLine.Add(postal.Trim());
            if (cityLine.Count > 0) parts.Add(string.Join(" ", cityLine));

            if (!string.IsNullOrEmpty(country)) parts.Add(country.Trim());
            return string.Join(", ", parts);
        }

        /// <summary>
        /// Loads C_OrderLine rows for the given purchase order with product /
        /// charge metadata, UOM symbol, price precision and a derived received
        /// state per line. Child of an already authorized order, so no separate
        /// MRole filter is applied here.
        /// </summary>
        /// <param name="C_Order_ID">Owning purchase order id.</param>
        /// <param name="defaultPrecision">Currency precision fallback.</param>
        /// <returns>Ordered list of line rows (may be empty).</returns>
        private List<PurchaseOrderLineData> LoadLines(int C_Order_ID, int defaultPrecision)
        {
            List<PurchaseOrderLineData> lines = new List<PurchaseOrderLineData>();

            string sql = @"SELECT
                              ol.C_OrderLine_ID,
                              ol.Line,
                              ol.QtyOrdered,
                              ol.QtyDelivered,
                              ol.QtyInvoiced,
                              ol.PriceActual,
                              ol.LineNetAmt,
                              ol.DatePromised,
                              ol.Description    AS LineDescription,
                              ol.M_Product_ID,
                              ol.C_Charge_ID,
                              p.Name            AS ProductName,
                              p.Value           AS ProductValue,
                              p.ProductType     AS ProductType,
                              ch.Name           AS ChargeName,
                              uom.UOMSymbol     AS UOMSymbol,
                              uom.StdPrecision  AS UOMPrecision,
                              NVL(pl.PricePrecision, 2) AS PricePrecision
                           FROM C_OrderLine ol
                           LEFT OUTER JOIN M_Product   p   ON (ol.M_Product_ID = p.M_Product_ID)
                           LEFT OUTER JOIN C_Charge    ch  ON (ol.C_Charge_ID  = ch.C_Charge_ID)
                           LEFT OUTER JOIN C_UOM       uom ON (ol.C_UOM_ID     = uom.C_UOM_ID)
                           LEFT OUTER JOIN C_Order     o   ON (ol.C_Order_ID   = o.C_Order_ID)
                           LEFT OUTER JOIN M_PriceList pl  ON (o.M_PriceList_ID = pl.M_PriceList_ID)
                           WHERE ol.C_Order_ID = @C_Order_ID
                             AND ol.IsActive   = 'Y'
                           ORDER BY ol.Line";

            SqlParameter[] param = new SqlParameter[]
            {
                new SqlParameter("@C_Order_ID", C_Order_ID)
            };

            DataSet ds = DB.ExecuteDataset(sql, param, null);
            if (ds == null || ds.Tables.Count == 0)
                return lines;

            foreach (DataRow r in ds.Tables[0].Rows)
            {
                PurchaseOrderLineData ln = new PurchaseOrderLineData();
                ln.C_OrderLine_ID = Util.GetValueOfInt(r["C_OrderLine_ID"]);
                ln.Line           = Util.GetValueOfInt(r["Line"]);
                ln.QtyOrdered     = Util.GetValueOfDecimal(r["QtyOrdered"]);
                ln.QtyDelivered   = Util.GetValueOfDecimal(r["QtyDelivered"]);
                ln.QtyInvoiced    = Util.GetValueOfDecimal(r["QtyInvoiced"]);
                ln.PriceActual    = Util.GetValueOfDecimal(r["PriceActual"]);
                ln.LineNetAmt     = Util.GetValueOfDecimal(r["LineNetAmt"]);
                ln.DatePromised   = Util.GetValueOfDateTime(r["DatePromised"]);
                ln.Description    = Util.GetValueOfString(r["LineDescription"]);
                ln.M_Product_ID   = Util.GetValueOfInt(r["M_Product_ID"]);
                ln.C_Charge_ID    = Util.GetValueOfInt(r["C_Charge_ID"]);
                ln.ProductName    = Util.GetValueOfString(r["ProductName"]);
                ln.ProductValue   = Util.GetValueOfString(r["ProductValue"]);
                ln.ProductType    = Util.GetValueOfString(r["ProductType"]);
                ln.ChargeName     = Util.GetValueOfString(r["ChargeName"]);
                ln.UOMSymbol      = Util.GetValueOfString(r["UOMSymbol"]);
                ln.UOMPrecision   = Util.GetValueOfInt(r["UOMPrecision"]);
                ln.PricePrecision = Util.GetValueOfInt(r["PricePrecision"]);

                if (string.IsNullOrEmpty(ln.ProductName) && !string.IsNullOrEmpty(ln.ChargeName))
                    ln.ProductName = ln.ChargeName;

                // Derived received state: full / part / none.
                if (ln.QtyOrdered > 0 && ln.QtyDelivered >= ln.QtyOrdered)
                    ln.RecvState = "full";
                else if (ln.QtyDelivered > 0)
                    ln.RecvState = "part";
                else
                    ln.RecvState = "none";

                lines.Add(ln);
            }
            return lines;
        }

        /// <summary>
        /// Loads CM_ChatEntry rows logged against this purchase order (via
        /// CM_Chat where AD_Table_ID = C_Order's table id and Record_ID = the
        /// order id), newest first, for the Recent Activity panel.
        /// </summary>
        /// <param name="C_Order_ID">Owning purchase order id.</param>
        /// <returns>Activity rows ordered newest-first (may be empty).</returns>
        private List<ActivityData> LoadActivity(int C_Order_ID)
        {
            List<ActivityData> activity = new List<ActivityData>();

            string sql = @"SELECT
                              ce.CM_ChatEntry_ID,
                              ce.AD_User_ID,
                              ce.CharacterData,
                              ce.Created,
                              u.Name              AS UserName
                           FROM CM_ChatEntry ce
                           INNER JOIN CM_Chat ch    ON (ce.CM_Chat_ID = ch.CM_Chat_ID)
                           LEFT OUTER JOIN AD_User u ON (ce.AD_User_ID = u.AD_User_ID)
                           WHERE ch.AD_Table_ID =
                                 (SELECT t.AD_Table_ID
                                    FROM AD_Table t
                                   WHERE t.TableName = 'C_Order')
                             AND ch.Record_ID = @C_Order_ID
                             AND ce.IsActive  = 'Y'
                           ORDER BY ce.Created DESC";

            SqlParameter[] param = new SqlParameter[]
            {
                new SqlParameter("@C_Order_ID", C_Order_ID)
            };

            DataSet ds = DB.ExecuteDataset(sql, param, null);
            if (ds == null || ds.Tables.Count == 0)
                return activity;

            foreach (DataRow r in ds.Tables[0].Rows)
            {
                ActivityData a = new ActivityData();
                a.CM_ChatEntry_ID = Util.GetValueOfInt(r["CM_ChatEntry_ID"]);
                a.AD_User_ID      = Util.GetValueOfInt(r["AD_User_ID"]);
                a.UserName        = Util.GetValueOfString(r["UserName"]);
                a.Text            = Util.GetValueOfString(r["CharacterData"]);
                a.Created         = Util.GetValueOfDateTime(r["Created"]);
                activity.Add(a);
            }
            return activity;
        }

        // ----------------------------------------------------------------- //
        //  Landed cost (read side)                                           //
        // ----------------------------------------------------------------- //
        //  Rendered per cost-component (one row per cost element + distribution
        //  method): the expected budget (C_ExpectedCost, available once the PO
        //  is completed) collapsed onto the matching invoice-linked actual
        //  (C_LandedCostAllocation -> C_LandedCost -> vendor invoice, reached
        //  through the GRN line M_InOutLine back to this PO). Each side is
        //  loaded under its own guard so a DB-level issue degrades to a hidden
        //  / partial section (logged via _log.Severe) rather than breaking the
        //  whole overview. Expected amounts read C_ExpectedCost.Amt (falling
        //  back to the sum of its C_ExpectedCostDistribution.Amt lines); actual
        //  amounts read SUM(C_LandedCostAllocation.Amt). A missing actual stays
        //  null ("Awaiting invoice") rather than being shown as zero.

        /// <summary>
        /// Builds the per-component landed-cost list for the order and rolls up
        /// the section totals (expected, actual-to-date, open/not-invoiced,
        /// landed value, component / invoiced counts) onto <paramref name="d"/>.
        /// Expected components are only considered once the PO is completed
        /// (DocStatus = 'CO'); actual components are always considered.
        /// </summary>
        private List<LandedCostComponentData> LoadLandedCostComponents(PurchaseOrderOverviewData d)
        {
            List<LandedCostComponentData> components = new List<LandedCostComponentData>();

            // Keyed by cost element + distribution so an expected component and
            // its actual invoice line collapse onto a single row.
            Dictionary<string, LandedCostComponentData> map =
                new Dictionary<string, LandedCostComponentData>();

            if (d.DocStatus == "CO")
                LoadExpectedComponents(d.C_Order_ID, map);

            LoadActualComponents(d.C_Order_ID, map);

            foreach (LandedCostComponentData c in map.Values)
            {
                // Variance + status only once an actual (invoice) exists.
                if (c.IsInvoiced)
                {
                    c.VarianceAmt = c.ActualAmt.GetValueOrDefault() - c.ExpectedAmt;
                    if (c.ActualAmt.GetValueOrDefault() > c.ExpectedAmt)      c.VarianceStatus = "over";
                    else if (c.ActualAmt.GetValueOrDefault() < c.ExpectedAmt) c.VarianceStatus = "under";
                    else                                                      c.VarianceStatus = "on_budget";
                }
                else
                {
                    c.VarianceStatus = "not_actualized";
                }
                components.Add(c);
            }

            components.Sort((a, b) => string.Compare(
                a.ComponentName, b.ComponentName, StringComparison.OrdinalIgnoreCase));

            // ----- Section roll-ups -----
            // PO goods value = sum of line net amounts (C_Order.TotalLines), not
            // the tax/freight-inclusive grand total.
            decimal poGoodsValue = d.TotalLines;
            decimal expectedTotal = 0, actualTotal = 0, openTotal = 0, landedComponents = 0;
            int invoicedCount = 0;
            foreach (LandedCostComponentData c in components)
            {
                expectedTotal += c.ExpectedAmt;
                if (c.IsInvoiced)
                {
                    actualTotal      += c.ActualAmt.GetValueOrDefault();
                    landedComponents += c.ActualAmt.GetValueOrDefault();
                    invoicedCount++;
                }
                else
                {
                    openTotal        += c.ExpectedAmt;
                    landedComponents += c.ExpectedAmt;
                }
            }

            d.ExpectedLandedCost     = expectedTotal;
            d.ActualToDate           = actualTotal;
            d.OpenNotInvoiced        = openTotal;
            d.LandedValue            = poGoodsValue + landedComponents;
            d.LandedComponentCount   = components.Count;
            d.InvoicedComponentCount = invoicedCount;

            return components;
        }

        /// <summary>
        /// Loads the expected landed-cost components (C_ExpectedCost) grouped by
        /// cost element + distribution. The expected amount is C_ExpectedCost.Amt,
        /// falling back to the sum of the component's C_ExpectedCostDistribution.Amt
        /// lines when Amt is null. Component name comes from M_CostElement.Name and
        /// the source sub-label from C_ExpectedCost.Description.
        /// </summary>
        private void LoadExpectedComponents(
            int C_Order_ID, Dictionary<string, LandedCostComponentData> map)
        {
            try
            {
                string sql = @"SELECT ec.M_CostElement_ID                    AS CostElementId,
                                      ec.LandedCostDistribution              AS DistCode,
                                      MAX(ce.Name)                           AS ComponentName,
                                      MAX(ec.Description)                    AS SourceLabel,
                                      SUM(NVL(ec.Amt, NVL(ead.AllocAmt, 0))) AS ExpectedAmt
                                 FROM C_ExpectedCost ec
                                 LEFT OUTER JOIN (SELECT ecd.C_ExpectedCost_ID,
                                                         SUM(NVL(ecd.Amt, 0)) AS AllocAmt
                                                    FROM C_ExpectedCostDistribution ecd
                                                   WHERE ecd.IsActive = 'Y'
                                                   GROUP BY ecd.C_ExpectedCost_ID) ead
                                        ON (ead.C_ExpectedCost_ID = ec.C_ExpectedCost_ID)
                                 LEFT OUTER JOIN M_CostElement ce
                                        ON (ce.M_CostElement_ID = ec.M_CostElement_ID)
                                WHERE ec.C_Order_ID = @C_Order_ID
                                  AND ec.IsActive   = 'Y'
                                GROUP BY ec.M_CostElement_ID, ec.LandedCostDistribution";
                SqlParameter[] param = new SqlParameter[]
                {
                    new SqlParameter("@C_Order_ID", C_Order_ID)
                };
                DataSet ds = DB.ExecuteDataset(sql, param, null);
                if (ds == null || ds.Tables.Count == 0) return;
                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    LandedCostComponentData c = GetOrAddComponent(
                        map, Util.GetValueOfInt(r["CostElementId"]),
                        Util.GetValueOfString(r["DistCode"]));
                    c.ComponentName = Util.GetValueOfString(r["ComponentName"]);
                    c.SourceLabel   = Util.GetValueOfString(r["SourceLabel"]);
                    c.ExpectedAmt   = Util.GetValueOfDecimal(r["ExpectedAmt"]);
                }
            }
            catch (Exception ex)
            {
                // Non-fatal: keep the overview working, just drop expected costs.
                _log.Severe("LoadExpectedComponents (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// Loads the actual landed-cost components from invoice-linked
        /// C_LandedCostAllocation rows, reached through the GRN line
        /// (M_InOutLine) back to this PO's order lines and grouped by cost
        /// element + distribution. Only allocations that resolve to a vendor
        /// invoice line are treated as actualised; the actual amount is
        /// SUM(C_LandedCostAllocation.Amt). Component name prefers
        /// M_CostElement.Name then C_Charge.Name then C_LandedCost.Description;
        /// the source sub-label prefers the invoice vendor / reference.
        /// </summary>
        private void LoadActualComponents(
            int C_Order_ID, Dictionary<string, LandedCostComponentData> map)
        {
            try
            {
                string sql = @"SELECT lc.M_CostElement_ID      AS CostElementId,
                                      lc.LandedCostDistribution AS DistCode,
                                      MAX(NVL(ce.Name, NVL(ch.Name, lc.Description)))                       AS ComponentName,
                                      MAX(NVL(bp.Name, NVL(inv.InvoiceReference, NVL(inv.DocumentNo, lc.Description)))) AS SourceLabel,
                                      SUM(NVL(lca.Amt, 0))      AS ActualAmt,
                                      MAX(inv.DocumentNo)       AS InvoiceNo,
                                      MAX(inv.InvoiceReference) AS InvoiceReference,
                                      MAX(inv.DateInvoiced)     AS LatestInvoiceDate
                                 FROM C_LandedCostAllocation lca
                                 INNER JOIN C_LandedCost lc
                                        ON (lc.C_LandedCost_ID = lca.C_LandedCost_ID
                                            AND lc.IsActive = 'Y')
                                 INNER JOIN M_InOutLine iol
                                        ON (iol.M_InOutLine_ID = lca.M_InOutLine_ID
                                            AND iol.IsActive = 'Y')
                                 INNER JOIN M_InOut io
                                        ON (io.M_InOut_ID = iol.M_InOut_ID
                                            AND io.IsActive = 'Y'
                                            AND io.IsSoTrx = 'N'
                                            AND io.DocStatus IN ('CO', 'CL'))
                                 INNER JOIN C_OrderLine ol
                                        ON (ol.C_OrderLine_ID = iol.C_OrderLine_ID
                                            AND ol.C_Order_ID = @C_Order_ID)
                                 LEFT OUTER JOIN C_InvoiceLine il
                                        ON (il.C_InvoiceLine_ID = NVL(lca.C_InvoiceLine_ID,
                                                                      NVL(lc.C_InvoiceLine_ID, lc.Ref_InvoiceLine_ID))
                                            AND il.IsActive = 'Y')
                                 LEFT OUTER JOIN C_Invoice inv
                                        ON (inv.C_Invoice_ID = il.C_Invoice_ID
                                            AND inv.IsActive = 'Y'
                                            AND inv.IsSoTrx = 'N')
                                 LEFT OUTER JOIN C_BPartner bp
                                        ON (bp.C_BPartner_ID = inv.C_BPartner_ID)
                                 LEFT OUTER JOIN M_CostElement ce
                                        ON (ce.M_CostElement_ID = NVL(lc.M_CostElement_ID, lca.M_CostElement_ID))
                                 LEFT OUTER JOIN C_Charge ch
                                        ON (ch.C_Charge_ID = il.C_Charge_ID)
                                WHERE lca.IsActive = 'Y'
                                  AND il.C_InvoiceLine_ID IS NOT NULL
                                GROUP BY lc.M_CostElement_ID, lc.LandedCostDistribution";
                SqlParameter[] param = new SqlParameter[]
                {
                    new SqlParameter("@C_Order_ID", C_Order_ID)
                };
                DataSet ds = DB.ExecuteDataset(sql, param, null);
                if (ds == null || ds.Tables.Count == 0) return;
                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    LandedCostComponentData c = GetOrAddComponent(
                        map, Util.GetValueOfInt(r["CostElementId"]),
                        Util.GetValueOfString(r["DistCode"]));

                    // Prefer an explicit component name / vendor source from the
                    // actual side; otherwise keep whatever the expected side set.
                    string actualName = Util.GetValueOfString(r["ComponentName"]);
                    string actualSrc  = Util.GetValueOfString(r["SourceLabel"]);
                    if (!string.IsNullOrEmpty(actualName)) c.ComponentName = actualName;
                    if (!string.IsNullOrEmpty(actualSrc))  c.SourceLabel   = actualSrc;

                    c.ActualAmt         = Util.GetValueOfDecimal(r["ActualAmt"]);
                    c.IsInvoiced        = true;
                    c.InvoiceNo         = Util.GetValueOfString(r["InvoiceNo"]);
                    c.InvoiceReference  = Util.GetValueOfString(r["InvoiceReference"]);
                    c.LatestInvoiceDate = Util.GetValueOfDateTime(r["LatestInvoiceDate"]);
                }
            }
            catch (Exception ex)
            {
                // Non-fatal: keep the overview working, just drop actual costs.
                _log.Severe("LoadActualComponents (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// Returns the component row for the given cost element + distribution,
        /// creating and registering it on first use so the expected and actual
        /// passes can populate the same row.
        /// </summary>
        private LandedCostComponentData GetOrAddComponent(
            Dictionary<string, LandedCostComponentData> map, int costElementId, string distCode)
        {
            string key = costElementId + "|" + (distCode ?? "");
            LandedCostComponentData c;
            if (!map.TryGetValue(key, out c))
            {
                c = new LandedCostComponentData
                {
                    M_CostElement_ID = costElementId,
                    DistributionCode = distCode
                };
                map[key] = c;
            }
            return c;
        }

        // ----------------------------------------------------------------- //
        //  Data carriers                                                     //
        // ----------------------------------------------------------------- //

        public class PurchaseOrderLineData
        {
            public int      C_OrderLine_ID { get; set; }
            public int      Line           { get; set; }
            public decimal  QtyOrdered     { get; set; }
            public decimal  QtyDelivered   { get; set; }
            public decimal  QtyInvoiced    { get; set; }
            public decimal  PriceActual    { get; set; }
            public decimal  LineNetAmt     { get; set; }
            public DateTime? DatePromised  { get; set; }
            public string   Description    { get; set; }
            public int      M_Product_ID   { get; set; }
            public int      C_Charge_ID    { get; set; }
            public string   ProductName    { get; set; }
            public string   ProductValue   { get; set; }   // SKU
            public string   ProductType    { get; set; }
            public string   ChargeName     { get; set; }
            public string   UOMSymbol      { get; set; }
            public int      UOMPrecision   { get; set; }
            public int      PricePrecision { get; set; }
            public string   RecvState      { get; set; }    // full | part | none
        }

        public class LandedCostComponentData
        {
            public int      M_CostElement_ID   { get; set; }
            public string   ComponentName      { get; set; }   // cost element / charge name
            public string   SourceLabel        { get; set; }   // vendor / reference sub-label
            public string   DistributionCode   { get; set; }   // raw LandedCostDistribution (I/Q/W/V/L/C)
            public decimal  ExpectedAmt        { get; set; }   // budgeted amount (0 if none)
            public decimal? ActualAmt          { get; set; }   // null = awaiting invoice
            public decimal? VarianceAmt        { get; set; }   // null until actualised
            public string   VarianceStatus     { get; set; }   // over | under | on_budget | not_actualized
            public bool     IsInvoiced         { get; set; }   // an invoice-linked actual exists
            public string   InvoiceNo          { get; set; }
            public string   InvoiceReference   { get; set; }
            public DateTime? LatestInvoiceDate { get; set; }
        }

        public class ActivityData
        {
            public int       CM_ChatEntry_ID { get; set; }
            public int       AD_User_ID      { get; set; }
            public string    UserName        { get; set; }
            public string    Text            { get; set; }
            public DateTime? Created         { get; set; }
        }

        public class PurchaseOrderOverviewData
        {
            // Header / identity
            public int       C_Order_ID      { get; set; }
            public string    DocumentNo      { get; set; }
            public DateTime? DateOrdered     { get; set; }
            public DateTime? DatePromised    { get; set; }
            public string    DocStatus       { get; set; }
            public DateTime? Created         { get; set; }
            public int       C_BPartner_ID   { get; set; }
            public string    OrderDescription { get; set; }
            public string    POReference     { get; set; }

            // Vendor
            public string    VendorName      { get; set; }
            public string    VendorAddress   { get; set; }
            public string    ContactName     { get; set; }
            public string    ContactPhone    { get; set; }
            public string    ContactEmail    { get; set; }

            // Meta
            public string    BuyerName       { get; set; }
            public string    CreatedByName   { get; set; }
            public string    PaymentTermName { get; set; }
            public string    WarehouseName   { get; set; }   // Ship To
            public string    OrgName         { get; set; }   // Bill To

            // Currency
            public string    CurSymbol       { get; set; }
            public string    ISO_Code        { get; set; }
            public int       StdPrecision    { get; set; }

            // Totals
            public decimal   GrandTotal      { get; set; }
            public decimal   TotalLines      { get; set; }
            public decimal   TaxAmt          { get; set; }

            // Stat-strip aggregates
            public decimal   TotalQtyOrdered        { get; set; }
            public decimal   TotalQtyDelivered      { get; set; }
            public decimal   TotalQtyInvoiced       { get; set; }
            public int       LineCount              { get; set; }
            public int       FullyReceivedLineCount { get; set; }

            // Linked / origin documents
            public string    RefOrderDocNo       { get; set; }   // originating sales order
            public int       RequisitionLineCount { get; set; }

            // 7-stage progress
            public bool      IsCompleted        { get; set; }
            public bool      IsWithVendor       { get; set; }
            public bool      IsExpectedDelivery { get; set; }
            public bool      IsPartialDelivered { get; set; }
            public bool      IsFullyDelivered   { get; set; }
            public bool      IsInvoiceRaised    { get; set; }
            public bool      IsPaymentDone      { get; set; }
            public int       CurrentStage       { get; set; }    // 1..7

            // Per-stage action dates (progress stepper sub-line)
            public DateTime? LastReceiptDate    { get; set; }    // latest goods receipt
            public DateTime? LastInvoiceDate    { get; set; }    // latest vendor invoice
            public DateTime? LastPaymentDate    { get; set; }    // latest payment allocated

            // Derived priority
            public string    Priority           { get; set; }    // high | med | low

            // Collections
            public List<PurchaseOrderLineData> Lines    { get; set; }
            public List<ActivityData>          Activity { get; set; }

            // Landed cost (per-component list + section roll-ups)
            public List<LandedCostComponentData> LandedCostComponents { get; set; }
            public decimal ExpectedLandedCost     { get; set; }   // Σ expected
            public decimal ActualToDate           { get; set; }   // Σ actual (invoiced)
            public decimal OpenNotInvoiced        { get; set; }   // Σ expected still open
            public decimal LandedValue            { get; set; }   // goods value + landed cost
            public int     LandedComponentCount   { get; set; }
            public int     InvoicedComponentCount { get; set; }
        }
    }
}
