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
///   VAI163   2026-07-01  Recent Activity now a real typed event feed: chat
///                        notes (CM_ChatEntry) merged with goods receipts
///                        (M_InOut), vendor invoices (C_Invoice), allocated
///                        payments (C_Payment) and the order create / approve
///                        milestones (C_Order), newest-first. ActivityData gains
///                        Type / DocumentNo / Count.
///   VAI163   2026-07-17  Priority now read from C_Order.PriorityRule (was
///                        derived). Added PriceListName. Generated From reworked
///                        to real origins only: requisition (M_Requisition) and
///                        contract reference (VAS_ContractMaster) via LoadOrigins.
///                        Added Notes (LoadNotes, mirrors Sales Order overview)
///                        and line change history (LoadHistory / C_OrderLineHistory).
///                        Landed cost: per-line distribution breakdown
///                        (LoadDistributionLines / C_ExpectedCostDistribution) and
///                        actuals now restricted to completed vendor invoices.
///   VAI163   2026-07-17  Surfaced OrderCompletedDate (DocComplete workflow node)
///                        on the payload — the value was fetched but never mapped,
///                        so the progress stepper fell back to DateOrdered for the
///                        Completed stage.
///   VAI163   2026-07-17  Moved OrderCompletedDate out of the main SELECT into
///                        GetOrderCompletedDate(). As a subselect its workflow
///                        aliases were picked up by MRole.AddAccessSQL and emitted
///                        as outer-WHERE access filters, so the whole overview
///                        failed with ORA-00904 and the panel showed "no data".
///   VAI163   2026-07-17  Added Documents (LoadDocuments): the GRNs (M_InOut) and
///                        vendor invoices (C_Invoice) prepared from this PO, each
///                        carrying TableName + RecordId so the client can open the
///                        document. Includes in-progress documents, unlike the
///                        activity feed which only reports completed ones.
///   VAI163   2026-07-17  Landed cost: expected components now load for CO *and*
///                        CL (reusing IsCompleted) — a closed order was silently
///                        dropping its expected costs and distribution lines,
///                        leaving every component actual-only.
///   VAI163   2026-07-24  Corrected the delivery state: the qty ordered/delivered,
///                        fully-received and new deliverable-line counts now count
///                        only stockable item lines (M_Product ProductType='I' and
///                        IsStocked='Y'), computed in a standalone LoadDeliveryStats
///                        query. Charge / service / non-stocked lines carry
///                        QtyOrdered but are never received, so a PO with a freight
///                        or landed-cost charge line no longer reads as "Partially
///                        Received" once its goods are fully received. Kept out of
///                        the MRole-rewritten main SELECT to avoid the subselect
///                        alias ORA-00904 (same reason as GetOrderCompletedDate).
///                        Line history now filters on C_OrderlineHistory.C_Order_ID
///                        with a LEFT JOIN to C_OrderLine so a removed line's
///                        snapshots still show. Landed cost: expected costs and the
///                        per-line distribution breakdown now load regardless of
///                        CO/CL and tolerate a null IsActive on
///                        C_ExpectedCostDistribution.
///   VAI163   2026-07-27  Line items now carry the Attribute Set Instance
///                        description (M_AttributeSetInstance.Description via
///                        C_OrderLine.M_AttributeSetInstance_ID) so the panel can
///                        show size / lot / serial attributes per product.
///   VAI163   2026-07-27  - Line items and line history now carry QtyEntered
///                          (the entered-UOM quantity) so the panel shows the qty
///                          as keyed on the order, not the base-UOM QtyOrdered.
///                        - Landed cost expected components tolerate a null
///                          IsActive (NVL(ec.IsActive,'Y')='Y') so the section
///                          shows whenever C_ExpectedCost rows exist, regardless
///                          of the order's document status (drafted included).
///   VAI163   2026-07-27  - Landed cost distribution method set explicitly from
///                          C_ExpectedCost.LandedCostDistribution on the expected
///                          pass so it always reflects the expected value.
///                        - Notes now prefix each line note with its line number
///                          so the C_OrderLine.Description is clearly shown.
///                        - GRN documents now carry a received value
///                          (Σ M_InOutLine.MovementQty × C_OrderLine.PriceActual).
///                        - Payment stage is "done" only when every invoice is
///                          fully paid (→ "Payment Completed"); a partial / no
///                          payment leaves it not-done (→ "Pending Amount").
///   VAI163   2026-07-27  - Documents now include the AP payments (C_Payment,
///                          IsReceipt='N') allocated to the order's invoices,
///                          carrying PayAmt + DiscountAmt and opening the AP
///                          Payment window (LoadPaymentDocuments).
///                        - Tax now read from SUM(C_OrderTax.TaxAmt) and SubTotal
///                          = GrandTotal - Tax, so a tax-inclusive price list
///                          (IsTaxIncluded='Y') shows the net subtotal and the
///                          extracted tax correctly (SubTotal + Tax = GrandTotal).
///                          Landed value goods base now uses SubTotal.
///                        - Surfaced C_Order.Posted for the panel's Posted badge.
///                        - Notes header (C_Order.Description) now loads via a
///                          LEFT JOIN to C_OrderLine so a line-less PO (manual /
///                          contract) still shows the Notes section.
///   VAI163   2026-07-27  - Received-card quantities now count Item-type products
///                          only (M_Product.ProductType='I'); the deliverable /
///                          fully-received line counts stay stockable-only.
///                        - Surfaced the GL budget breach: C_Order.IsBudgetViolated
///                          + MaxBudgetViolationAmount and C_OrderLine.
///                          BudgetViolationAmount for the panel's Budget section.
///   VAI163   2026-07-27  - LastInvoiceDate now comes from a standalone query
///                          (GetLastInvoiceDate) — the invoice DocComplete workflow
///                          timestamp — moved out of the main SELECT for the same
///                          MRole ORA-00904 reason as GetOrderCompletedDate.
///   VAI163   2026-07-27  - Contract origin now loads in two steps: the
///                          VAS_ContractMaster_ID from C_Order first (so the
///                          Generated From chip always shows when the order has
///                          one), then the DocumentNo enriched separately.
///   VAI163   2026-07-27  - Attribute Set Instance join now matches only a real
///                          instance (M_AttributeSetInstance_ID > 0) so a line
///                          with no ASI no longer picks up the zero-record's "--"
///                          description.
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
                              o.PriorityRule,
                              o.Posted,
                              o.IsBudgetViolated,
                              o.MaxBudgetViolationAmount,
                              o.Ref_Order_ID,
                              bp.Name             AS VendorName,
                              bpc.Name            AS ContactName,
                              bpc.Phone           AS ContactPhone,
                              bpc.EMail           AS ContactEmail,
                              sr.Name             AS BuyerName,
                              cu.Name             AS CreatedByName,
                              pt.Name             AS PaymentTermName,
                              pl.Name             AS PriceListName,
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
                              (SELECT MAX(p.DateTrx)
                                 FROM C_Payment p
                                 INNER JOIN C_AllocationLine al ON (al.C_Payment_ID = p.C_Payment_ID)
                                 INNER JOIN C_Invoice ci2       ON (al.C_Invoice_ID = ci2.C_Invoice_ID)
                                WHERE ci2.C_Order_ID = o.C_Order_ID
                                  AND p.IsActive     = 'Y'
                                  AND p.DocStatus IN ('CO', 'CL'))             AS LastPaymentDate
                            FROM C_Order o
                            INNER JOIN C_BPartner bp        ON (o.C_BPartner_ID          = bp.C_BPartner_ID)
                            LEFT OUTER JOIN AD_User bpc      ON (o.AD_User_ID             = bpc.AD_User_ID)
                            LEFT OUTER JOIN AD_User sr       ON (o.SalesRep_ID            = sr.AD_User_ID)
                            LEFT OUTER JOIN AD_User cu       ON (o.CreatedBy              = cu.AD_User_ID)
                            LEFT OUTER JOIN C_PaymentTerm pt ON (o.C_PaymentTerm_ID       = pt.C_PaymentTerm_ID)
                            LEFT OUTER JOIN M_PriceList pl   ON (o.M_PriceList_ID         = pl.M_PriceList_ID)
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
            result.PriorityRule  = Util.GetValueOfString(r["PriorityRule"]);
            result.Posted        = Util.GetValueOfString(r["Posted"]) == "Y";

            result.VendorName    = Util.GetValueOfString(r["VendorName"]);
            result.ContactName   = Util.GetValueOfString(r["ContactName"]);
            result.ContactPhone  = Util.GetValueOfString(r["ContactPhone"]);
            result.ContactEmail  = Util.GetValueOfString(r["ContactEmail"]);
            result.BuyerName     = Util.GetValueOfString(r["BuyerName"]);
            result.CreatedByName = Util.GetValueOfString(r["CreatedByName"]);
            result.PaymentTermName = Util.GetValueOfString(r["PaymentTermName"]);
            result.PriceListName = Util.GetValueOfString(r["PriceListName"]);
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
            //  Tax is read from C_OrderTax (the extracted per-tax amounts), not
            //  derived as GrandTotal - TotalLines. That derivation is wrong for a
            //  tax-inclusive price list (IsTaxIncluded='Y'), where C_Order.TotalLines
            //  already carries the tax-inclusive amount and GrandTotal == TotalLines,
            //  so the old formula reported zero tax. Using SUM(C_OrderTax.TaxAmt)
            //  gives the real tax for both inclusive and exclusive pricing, and the
            //  net subtotal is then GrandTotal - Tax (which equals TotalLines for a
            //  tax-exclusive order, so nothing changes there). SubTotal + TaxAmt
            //  always equals GrandTotal.
            result.GrandTotal    = Util.GetValueOfDecimal(r["GrandTotal"]);
            result.TotalLines    = Util.GetValueOfDecimal(r["TotalLines"]);
            result.TaxAmt        = GetOrderTaxAmt(C_Order_ID);
            result.SubTotal      = result.GrandTotal - result.TaxAmt;

            // ----- Budget control (GL budget breach) -----
            //  The platform's budget check (ModelLibrary BudgetCheck) stamps the
            //  order with IsBudgetViolated / MaxBudgetViolationAmount and each line
            //  with BudgetViolationAmount when the PO's committed spend exceeds the
            //  available GL budget. Surfaced here so the panel can flag the breach
            //  and show how far over budget the order (and each line) is. Amounts
            //  are in the accounting currency the budget is kept in.
            result.IsBudgetViolated          = Util.GetValueOfString(r["IsBudgetViolated"]) == "Y";
            result.MaxBudgetViolationAmount  = Util.GetValueOfDecimal(r["MaxBudgetViolationAmount"]);

            // ----- Stat-strip aggregates -----
            //  TotalQtyOrdered / TotalQtyDelivered / FullyReceivedLineCount /
            //  DeliverableLineCount are filled by LoadDeliveryStats below (stockable
            //  goods only, kept out of the MRole-rewritten main SELECT).
            result.TotalQtyInvoiced      = Util.GetValueOfDecimal(r["TotalQtyInvoiced"]);
            result.LineCount             = Util.GetValueOfInt(r["LineCount"]);

            // ----- Delivery aggregates (stockable goods only) -----
            LoadDeliveryStats(C_Order_ID, result);

            // ----- Linked / origin documents -----
            result.RefOrderDocNo      = Util.GetValueOfString(r["RefOrderDocNo"]);
            result.RefOrderId         = Util.GetValueOfInt(r["Ref_Order_ID"]);
            result.RequisitionLineCount = Util.GetValueOfInt(r["RequisitionLineCount"]);

            // ----- 7-stage progress -----
            int orderInvoiceCount = Util.GetValueOfInt(r["OrderInvoiceCount"]);
            int paidInvoiceCount  = Util.GetValueOfInt(r["PaidInvoiceCount"]);

            DateTime? lastPaymentDate = Util.GetValueOfDateTime(r["LastPaymentDate"]);

            bool isCompleted    = result.DocStatus == "CO" || result.DocStatus == "CL";
            bool delivered      = result.TotalQtyDelivered > 0;
            // Fully delivered only when every deliverable (stockable item) line is
            // fully received. Both counts already exclude charge / service / non-
            // stocked lines (see LoadDeliveryStats), so a fully received order with
            // a freight or landed-cost charge line no longer stays "partial".
            // Count-based (not a sum comparison) so an over-received line cannot
            // mask a short one.
            bool fullyDelivered = result.DeliverableLineCount > 0
                                  && result.FullyReceivedLineCount >= result.DeliverableLineCount;
            bool invoiced       = orderInvoiceCount > 0;
            // Payment is "done" (→ "Payment Completed", green) only when every
            // invoice of the order is fully paid. Any other state (no payment or a
            // partial payment leaving a balance) is treated as not done and the
            // panel shows "Pending Amount".
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
            result.OrderCompletedDate = GetOrderCompletedDate(C_Order_ID);
            result.LastReceiptDate = Util.GetValueOfDateTime(r["LastReceiptDate"]);
            result.LastInvoiceDate = GetLastInvoiceDate(C_Order_ID);
            result.LastPaymentDate = lastPaymentDate;

            // ----- Line items -----
            result.Lines = LoadLines(C_Order_ID, result.StdPrecision);

            // ----- Origin documents (sales order / requisition / contract) -----
            //        Requisition and contract reference are loaded here so the
            //        Generated From strip only ever shows origins that exist.
            LoadOrigins(C_Order_ID, result);

            // ----- Notes (order header + per-line, mirrors Sales Order overview) -----
            result.Notes = LoadNotes(C_Order_ID);

            // ----- Recent activity (CM_ChatEntry + document milestones) -----
            result.Activity = LoadActivity(C_Order_ID);

            // ----- Documents raised from this PO (openable GRNs / invoices) -----
            result.Documents = LoadDocuments(C_Order_ID);

            // ----- Line change history (C_OrderLineHistory) -----
            result.History = LoadHistory(C_Order_ID);

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
        /// Returns the order's total tax = SUM(C_OrderTax.TaxAmt). Works for both
        /// tax-exclusive and tax-inclusive (IsTaxIncluded='Y') price lists — the
        /// platform stores the extracted tax here in both cases. Kept as a
        /// standalone query (child of an already authorized order) so it never
        /// reaches the MRole rewriter on the main SELECT.
        /// </summary>
        /// <param name="C_Order_ID">Owning purchase order id.</param>
        private decimal GetOrderTaxAmt(int C_Order_ID)
        {
            try
            {
                string sql = @"SELECT NVL(SUM(ot.TaxAmt), 0) AS TaxAmt
                                 FROM C_OrderTax ot
                                WHERE ot.C_Order_ID = @C_Order_ID
                                  AND ot.IsActive   = 'Y'";
                DataSet ds = DB.ExecuteDataset(sql, OrderParam(C_Order_ID), null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
                    return 0;
                return Util.GetValueOfDecimal(ds.Tables[0].Rows[0]["TaxAmt"]);
            }
            catch (Exception ex)
            {
                _log.Severe("GetOrderTaxAmt (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
                return 0;
            }
        }

        /// <summary>
        /// Computes the delivery aggregates over the order's *deliverable* lines
        /// only — stockable item products (M_Product.ProductType = 'I' AND
        /// IsStocked = 'Y'). Charge, service and non-stocked product lines carry a
        /// QtyOrdered but are never updated with a QtyDelivered by a goods receipt
        /// (mirrors the platform's own receivable-line test in
        /// MInOut.AddServiceLines / MOrder.ReserveStock), so counting them made a
        /// fully received order look only partially delivered.
        ///
        /// Deliberately a standalone query (not subselects in the main SELECT):
        /// the main query is rewritten by MRole.AddAccessSQL, and a joined
        /// M_Product alias living only inside a subselect there is prone to the
        /// same ORA-00904 that moved GetOrderCompletedDate out. Run separately it
        /// never reaches that rewriter. Child of an already authorized order.
        /// </summary>
        /// <param name="C_Order_ID">Owning purchase order id.</param>
        /// <param name="d">Overview data to populate (delivery fields only).</param>
        private void LoadDeliveryStats(int C_Order_ID, PurchaseOrderOverviewData d)
        {
            try
            {
                //  Received-card quantities count Item-type products only
                //  (M_Product.ProductType = 'I') — charge lines (no product) are
                //  dropped by the INNER JOIN, and every non-item product type
                //  (service / expense / resource / online) is excluded by the
                //  CASE, so the card reflects goods quantities alone. The
                //  deliverable / fully-received line counts stay narrower
                //  (stockable items, IsStocked = 'Y') because only stocked lines
                //  ever receive a QtyDelivered from a goods receipt; that keeps the
                //  "fully received" determination correct without letting a non-
                //  stocked item hold the card below 100%.
                string sql = @"SELECT
                                  NVL(SUM(CASE WHEN p.ProductType = 'I'
                                               THEN ol.QtyOrdered   ELSE 0 END), 0) AS TotalQtyOrdered,
                                  NVL(SUM(CASE WHEN p.ProductType = 'I'
                                               THEN ol.QtyDelivered ELSE 0 END), 0) AS TotalQtyDelivered,
                                  SUM(CASE WHEN p.ProductType = 'I' AND p.IsStocked = 'Y'
                                           THEN 1 ELSE 0 END)  AS DeliverableLineCount,
                                  SUM(CASE WHEN p.ProductType = 'I' AND p.IsStocked = 'Y'
                                                AND ol.QtyDelivered >= ol.QtyOrdered
                                           THEN 1 ELSE 0 END)  AS FullyReceivedLineCount
                               FROM C_OrderLine ol
                               INNER JOIN M_Product p ON (p.M_Product_ID = ol.M_Product_ID)
                              WHERE ol.C_Order_ID  = @C_Order_ID
                                AND ol.IsActive    = 'Y'
                                AND ol.QtyOrdered  > 0";
                DataSet ds = DB.ExecuteDataset(sql, OrderParam(C_Order_ID), null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
                    return;

                DataRow r = ds.Tables[0].Rows[0];
                d.TotalQtyOrdered        = Util.GetValueOfDecimal(r["TotalQtyOrdered"]);
                d.TotalQtyDelivered      = Util.GetValueOfDecimal(r["TotalQtyDelivered"]);
                d.DeliverableLineCount   = Util.GetValueOfInt(r["DeliverableLineCount"]);
                d.FullyReceivedLineCount = Util.GetValueOfInt(r["FullyReceivedLineCount"]);
            }
            catch (Exception ex)
            {
                _log.Severe("LoadDeliveryStats (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
            }
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
                              ol.QtyEntered,
                              ol.QtyOrdered,
                              ol.QtyDelivered,
                              ol.QtyInvoiced,
                              ol.PriceActual,
                              ol.LineNetAmt,
                              ol.BudgetViolationAmount,
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
                              asi.Description   AS AttributeSetInstance,
                              NVL(pl.PricePrecision, 2) AS PricePrecision
                           FROM C_OrderLine ol
                           LEFT OUTER JOIN M_Product   p   ON (ol.M_Product_ID = p.M_Product_ID)
                           LEFT OUTER JOIN C_Charge    ch  ON (ol.C_Charge_ID  = ch.C_Charge_ID)
                           LEFT OUTER JOIN C_UOM       uom ON (ol.C_UOM_ID     = uom.C_UOM_ID)
                           LEFT OUTER JOIN M_AttributeSetInstance asi ON (asi.M_AttributeSetInstance_ID = ol.M_AttributeSetInstance_ID
                                                                          AND ol.M_AttributeSetInstance_ID > 0)
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
                ln.QtyEntered     = Util.GetValueOfDecimal(r["QtyEntered"]);
                ln.QtyOrdered     = Util.GetValueOfDecimal(r["QtyOrdered"]);
                ln.QtyDelivered   = Util.GetValueOfDecimal(r["QtyDelivered"]);
                ln.QtyInvoiced    = Util.GetValueOfDecimal(r["QtyInvoiced"]);
                ln.PriceActual    = Util.GetValueOfDecimal(r["PriceActual"]);
                ln.LineNetAmt     = Util.GetValueOfDecimal(r["LineNetAmt"]);
                ln.BudgetViolationAmount = Util.GetValueOfDecimal(r["BudgetViolationAmount"]);
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
                ln.AttributeSetInstance = Util.GetValueOfString(r["AttributeSetInstance"]);
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
        /// Loads the PO's origin references shown in the Generated From strip so
        /// it only ever renders origins that actually exist:
        ///   - Requisition(s): the requisition whose lines were converted into
        ///     this PO (M_RequisitionLine.C_OrderLine_ID -> this order's lines).
        ///   - Contract reference: C_Order.VAS_ContractMaster_ID ->
        ///     VAS_ContractMaster.DocumentNo. That column is module-optional, so
        ///     it is read under its own guard.
        /// (The sales-order origin, Ref_Order_ID, is read in the main query.)
        /// </summary>
        private void LoadOrigins(int C_Order_ID, PurchaseOrderOverviewData d)
        {
            // --- Requisition(s): first requisition + distinct requisition count. ---
            try
            {
                string sql = @"SELECT r.M_Requisition_ID, r.DocumentNo
                                 FROM M_RequisitionLine rl
                                 INNER JOIN M_Requisition r ON (rl.M_Requisition_ID = r.M_Requisition_ID)
                                 INNER JOIN C_OrderLine ol  ON (rl.C_OrderLine_ID   = ol.C_OrderLine_ID)
                                WHERE ol.C_Order_ID = @C_Order_ID
                                  AND r.IsActive    = 'Y'
                                GROUP BY r.M_Requisition_ID, r.DocumentNo
                                ORDER BY r.M_Requisition_ID";
                DataSet ds = DB.ExecuteDataset(sql, OrderParam(C_Order_ID), null);
                if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                {
                    DataRow r0 = ds.Tables[0].Rows[0];
                    d.RequisitionId    = Util.GetValueOfInt(r0["M_Requisition_ID"]);
                    d.RequisitionDocNo = Util.GetValueOfString(r0["DocumentNo"]);
                    d.RequisitionCount = ds.Tables[0].Rows.Count;
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadOrigins/Requisition (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
            }

            // --- Contract reference (module-optional VAS_ContractMaster_ID). ---
            //  Read in two independent steps so the Generated From strip shows the
            //  contract chip whenever the order carries a VAS_ContractMaster_ID,
            //  even if the master row itself can't be read: (1) the id from
            //  C_Order alone (no join, so a missing/edge-case VAS_ContractMaster
            //  table can't suppress the chip); (2) the human DocumentNo enriched
            //  separately. If step 2 fails the chip still renders (with "#id").
            try
            {
                string sqlId = @"SELECT o.VAS_ContractMaster_ID AS ContractId
                                   FROM C_Order o
                                  WHERE o.C_Order_ID = @C_Order_ID";
                DataSet dsId = DB.ExecuteDataset(sqlId, OrderParam(C_Order_ID), null);
                if (dsId != null && dsId.Tables.Count > 0 && dsId.Tables[0].Rows.Count > 0)
                    d.ContractMasterId = Util.GetValueOfInt(dsId.Tables[0].Rows[0]["ContractId"]);
            }
            catch (Exception ex)
            {
                // A deployment without the VAS_ContractMaster_ID column reaches
                // here; keep the overview working (no contract chip).
                _log.Severe("LoadOrigins/ContractId (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
            }

            if (d.ContractMasterId > 0)
            {
                try
                {
                    string sqlNo = @"SELECT cm.DocumentNo AS ContractNo
                                       FROM VAS_ContractMaster cm
                                      WHERE cm.VAS_ContractMaster_ID = @VAS_ContractMaster_ID";
                    SqlParameter[] p = new SqlParameter[]
                    {
                        new SqlParameter("@VAS_ContractMaster_ID", d.ContractMasterId)
                    };
                    DataSet dsNo = DB.ExecuteDataset(sqlNo, p, null);
                    if (dsNo != null && dsNo.Tables.Count > 0 && dsNo.Tables[0].Rows.Count > 0)
                        d.ContractMasterNo = Util.GetValueOfString(dsNo.Tables[0].Rows[0]["ContractNo"]);
                }
                catch (Exception ex)
                {
                    // Contract number is a nicety; the chip still shows the id.
                    _log.Severe("LoadOrigins/ContractNo (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
                }
            }
        }

        /// <summary>
        /// Loads notes for the Notes section, mirroring the Sales Order overview:
        /// the order header note (C_Order.Description) followed by each line's own
        /// note (product / charge name + line description). Composed in C# so the
        /// SQL stays portable (no DB-specific string functions).
        /// </summary>
        private List<NoteData> LoadNotes(int C_Order_ID)
        {
            List<NoteData> notes = new List<NoteData>();
            try
            {
                //  LEFT OUTER JOIN to C_OrderLine (with IsActive in the join, not
                //  the WHERE) so the C_Order row — and thus the header note
                //  (C_Order.Description) — is always returned even when the order
                //  has no active lines. An INNER JOIN dropped the whole result for
                //  a line-less PO (e.g. created manually or from a contract), which
                //  hid the Notes section even though Description had a value.
                string sql = @"SELECT o.Description  AS OrderNote,
                                      ol.Line        AS LineNo,
                                      ol.Description AS LineDescription,
                                      p.Name         AS ProductName,
                                      ch.Name        AS ChargeName
                                 FROM C_Order o
                                 LEFT OUTER JOIN C_OrderLine ol ON (ol.C_Order_ID = o.C_Order_ID
                                                                    AND ol.IsActive = 'Y')
                                 LEFT OUTER JOIN M_Product p  ON (p.M_Product_ID = ol.M_Product_ID)
                                 LEFT OUTER JOIN C_Charge  ch ON (ch.C_Charge_ID  = ol.C_Charge_ID)
                                WHERE o.C_Order_ID = @C_Order_ID
                                  AND o.IsActive   = 'Y'
                                ORDER BY ol.Line";
                DataSet ds = DB.ExecuteDataset(sql, OrderParam(C_Order_ID), null);
                if (ds == null || ds.Tables.Count == 0) return notes;

                bool headerAdded = false;
                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    if (!headerAdded)
                    {
                        string headerNote = Util.GetValueOfString(r["OrderNote"]);
                        if (!string.IsNullOrEmpty(headerNote))
                            notes.Add(new NoteData { NoteType = "header", Text = headerNote.Trim() });
                        headerAdded = true;
                    }

                    // Per-line note = the description entered on C_OrderLine.
                    string lineDesc = Util.GetValueOfString(r["LineDescription"]);
                    if (string.IsNullOrEmpty(lineDesc)) continue;

                    string prod = Util.GetValueOfString(r["ProductName"]);
                    if (string.IsNullOrEmpty(prod)) prod = Util.GetValueOfString(r["ChargeName"]);

                    // Prefix with the line number (and product / charge, when present)
                    // so the C_OrderLine.Description is clearly shown and attributable.
                    int lineNo = Util.GetValueOfInt(r["LineNo"]);
                    string label = lineNo > 0 ? "#" + lineNo : "";
                    if (!string.IsNullOrEmpty(prod))
                        label = string.IsNullOrEmpty(label) ? prod.Trim() : label + " " + prod.Trim();

                    string text = string.IsNullOrEmpty(label)
                        ? lineDesc.Trim()
                        : label + " — " + lineDesc.Trim();
                    notes.Add(new NoteData { NoteType = "line", Text = text });
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadNotes (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
            }
            return notes;
        }

        /// <summary>
        /// Loads the line change history (C_OrderLineHistory) for every line of
        /// the order — the prior versions the platform snapshots when a completed
        /// order line is re-activated / edited — newest change first per line.
        /// Column set mirrors the platform's proven PO line-history query.
        /// </summary>
        private List<HistoryData> LoadHistory(int C_Order_ID)
        {
            List<HistoryData> history = new List<HistoryData>();
            try
            {
                //  Filter on the history row's own C_Order_ID (populated by the
                //  platform snapshot) and LEFT JOIN the current order line, so a
                //  line that was later removed still shows its history — falling
                //  back to the snapshot's own Line sequence for display.
                string sql = @"SELECT olh.C_OrderLine_ID,
                                      NVL(ol.Line, olh.Line) AS LineNo,
                                      olh.Updated      AS ChangedOn,
                                      NVL(olh.QtyEntered, olh.QtyOrdered) AS QtyEntered,
                                      olh.QtyOrdered,
                                      olh.PriceActual,
                                      olh.LineNetAmt,
                                      olh.Discount,
                                      olh.Description  AS LineDescription,
                                      p.Name           AS ProductName,
                                      ch.Name          AS ChargeName,
                                      uom.UOMSymbol    AS UOMSymbol,
                                      NVL(uom.StdPrecision, 0) AS UOMPrecision,
                                      cur.StdPrecision AS StdPrecision
                                 FROM C_OrderLineHistory olh
                                 INNER JOIN C_Order o        ON (o.C_Order_ID = olh.C_Order_ID)
                                 LEFT OUTER JOIN C_OrderLine ol ON (ol.C_OrderLine_ID = olh.C_OrderLine_ID)
                                 LEFT OUTER JOIN M_Product p ON (p.M_Product_ID = olh.M_Product_ID)
                                 LEFT OUTER JOIN C_Charge  ch ON (ch.C_Charge_ID = olh.C_Charge_ID)
                                 LEFT OUTER JOIN C_UOM uom   ON (uom.C_UOM_ID = olh.C_UOM_ID)
                                 INNER JOIN C_Currency cur   ON (cur.C_Currency_ID = o.C_Currency_ID)
                                WHERE olh.C_Order_ID = @C_Order_ID
                                ORDER BY NVL(ol.Line, olh.Line), olh.Updated DESC";
                DataSet ds = DB.ExecuteDataset(sql, OrderParam(C_Order_ID), null);
                if (ds == null || ds.Tables.Count == 0) return history;

                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    HistoryData h = new HistoryData();
                    h.C_OrderLine_ID = Util.GetValueOfInt(r["C_OrderLine_ID"]);
                    h.LineNo         = Util.GetValueOfInt(r["LineNo"]);
                    h.ChangedOn      = Util.GetValueOfDateTime(r["ChangedOn"]);
                    h.QtyEntered     = Util.GetValueOfDecimal(r["QtyEntered"]);
                    h.QtyOrdered     = Util.GetValueOfDecimal(r["QtyOrdered"]);
                    h.PriceActual    = Util.GetValueOfDecimal(r["PriceActual"]);
                    h.LineNetAmt     = Util.GetValueOfDecimal(r["LineNetAmt"]);
                    h.Discount       = Util.GetValueOfDecimal(r["Discount"]);
                    h.Description    = Util.GetValueOfString(r["LineDescription"]);
                    h.ProductName    = Util.GetValueOfString(r["ProductName"]);
                    h.ChargeName     = Util.GetValueOfString(r["ChargeName"]);
                    h.UOMSymbol      = Util.GetValueOfString(r["UOMSymbol"]);
                    h.UOMPrecision   = Util.GetValueOfInt(r["UOMPrecision"]);
                    h.StdPrecision   = Util.GetValueOfInt(r["StdPrecision"]);
                    if (string.IsNullOrEmpty(h.ProductName) && !string.IsNullOrEmpty(h.ChargeName))
                        h.ProductName = h.ChargeName;
                    history.Add(h);
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadHistory (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
            }
            return history;
        }

        /// <summary>
        /// Builds the Recent Activity feed from real purchase-order events —
        /// chat notes (CM_ChatEntry), goods receipts (M_InOut), vendor invoices
        /// (C_Invoice), allocated payments (C_Payment) and the order's own
        /// create / approve milestones (C_Order) — merged and ordered
        /// newest-first (capped at the most recent entries). Each source is
        /// loaded under its own guard so a DB-level issue with one degrades to a
        /// partial feed (logged via _log.Severe) rather than breaking the
        /// overview. The resulting rows carry a <see cref="ActivityData.Type"/>
        /// (note | grn | invoice | payment | approval | created) that the client
        /// maps to a tag + icon.
        /// </summary>
        /// <param name="C_Order_ID">Owning purchase order id.</param>
        /// <returns>Activity rows ordered newest-first (may be empty).</returns>
        private List<ActivityData> LoadActivity(int C_Order_ID)
        {
            const int MAX_ENTRIES = 12;

            List<ActivityData> activity = new List<ActivityData>();
            LoadNoteActivity(C_Order_ID, activity);
            LoadReceiptActivity(C_Order_ID, activity);
            LoadInvoiceActivity(C_Order_ID, activity);
            LoadPaymentActivity(C_Order_ID, activity);
            LoadOrderMilestoneActivity(C_Order_ID, activity);

            // Newest first; entries with no timestamp sink to the bottom.
            activity.Sort((a, b) =>
                b.Created.GetValueOrDefault(DateTime.MinValue)
                 .CompareTo(a.Created.GetValueOrDefault(DateTime.MinValue)));

            if (activity.Count > MAX_ENTRIES)
                activity = activity.GetRange(0, MAX_ENTRIES);
            return activity;
        }

        /// <summary>
        /// Loads free-text chat notes (CM_ChatEntry) logged against this order
        /// (via CM_Chat where AD_Table_ID = C_Order's table id and Record_ID =
        /// the order id) as "note" activity rows.
        /// </summary>
        private void LoadNoteActivity(int C_Order_ID, List<ActivityData> list)
        {
            try
            {
                string sql = @"SELECT ce.CM_ChatEntry_ID,
                                      ce.AD_User_ID,
                                      ce.CharacterData,
                                      ce.Created,
                                      u.Name AS UserName
                                 FROM CM_ChatEntry ce
                                 INNER JOIN CM_Chat ch     ON (ce.CM_Chat_ID = ch.CM_Chat_ID)
                                 LEFT OUTER JOIN AD_User u ON (ce.AD_User_ID = u.AD_User_ID)
                                WHERE ch.AD_Table_ID =
                                      (SELECT t.AD_Table_ID FROM AD_Table t WHERE t.TableName = 'C_Order')
                                  AND ch.Record_ID = @C_Order_ID
                                  AND ce.IsActive  = 'Y'";
                DataSet ds = DB.ExecuteDataset(sql, OrderParam(C_Order_ID), null);
                if (ds == null || ds.Tables.Count == 0) return;
                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    list.Add(new ActivityData
                    {
                        Type            = "note",
                        CM_ChatEntry_ID = Util.GetValueOfInt(r["CM_ChatEntry_ID"]),
                        AD_User_ID      = Util.GetValueOfInt(r["AD_User_ID"]),
                        UserName        = Util.GetValueOfString(r["UserName"]),
                        Text            = Util.GetValueOfString(r["CharacterData"]),
                        Created         = Util.GetValueOfDateTime(r["Created"])
                    });
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadNoteActivity (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// Loads completed goods receipts (M_InOut, IsSoTrx = 'N') for this order
        /// as "grn" activity rows, carrying the receipt document number and its
        /// active line count.
        /// </summary>
        private void LoadReceiptActivity(int C_Order_ID, List<ActivityData> list)
        {
            try
            {
                string sql = @"SELECT io.DocumentNo,
                                      io.Created,
                                      u.Name AS UserName,
                                      (SELECT COUNT(*)
                                         FROM M_InOutLine iol
                                        WHERE iol.M_InOut_ID = io.M_InOut_ID
                                          AND iol.IsActive   = 'Y') AS LineCnt
                                 FROM M_InOut io
                                 LEFT OUTER JOIN AD_User u ON (io.CreatedBy = u.AD_User_ID)
                                WHERE io.C_Order_ID = @C_Order_ID
                                  AND io.IsActive   = 'Y'
                                  AND io.IsSoTrx    = 'N'
                                  AND io.DocStatus IN ('CO', 'CL')";
                DataSet ds = DB.ExecuteDataset(sql, OrderParam(C_Order_ID), null);
                if (ds == null || ds.Tables.Count == 0) return;
                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    list.Add(new ActivityData
                    {
                        Type       = "grn",
                        DocumentNo = Util.GetValueOfString(r["DocumentNo"]),
                        Count      = Util.GetValueOfInt(r["LineCnt"]),
                        UserName   = Util.GetValueOfString(r["UserName"]),
                        Created    = Util.GetValueOfDateTime(r["Created"])
                    });
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadReceiptActivity (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// Loads completed vendor invoices (C_Invoice, IsSoTrx = 'N') for this
        /// order as "invoice" activity rows.
        /// </summary>
        private void LoadInvoiceActivity(int C_Order_ID, List<ActivityData> list)
        {
            try
            {
                string sql = @"SELECT ci.DocumentNo,
                                      ci.Created,
                                      u.Name AS UserName
                                 FROM C_Invoice ci
                                 LEFT OUTER JOIN AD_User u ON (ci.CreatedBy = u.AD_User_ID)
                                WHERE ci.C_Order_ID = @C_Order_ID
                                  AND ci.IsActive   = 'Y'
                                  AND ci.IsSoTrx    = 'N'
                                  AND ci.DocStatus IN ('CO', 'CL')";
                DataSet ds = DB.ExecuteDataset(sql, OrderParam(C_Order_ID), null);
                if (ds == null || ds.Tables.Count == 0) return;
                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    list.Add(new ActivityData
                    {
                        Type       = "invoice",
                        DocumentNo = Util.GetValueOfString(r["DocumentNo"]),
                        UserName   = Util.GetValueOfString(r["UserName"]),
                        Created    = Util.GetValueOfDateTime(r["Created"])
                    });
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadInvoiceActivity (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// Loads completed payments allocated to this order's invoices
        /// (C_Payment via C_AllocationLine -> C_Invoice) as "payment" activity
        /// rows. DISTINCT so a payment allocated across several of the order's
        /// invoices is reported once.
        /// </summary>
        private void LoadPaymentActivity(int C_Order_ID, List<ActivityData> list)
        {
            try
            {
                string sql = @"SELECT DISTINCT p.C_Payment_ID,
                                               p.DocumentNo,
                                               p.Created,
                                               u.Name AS UserName
                                 FROM C_Payment p
                                 INNER JOIN C_AllocationLine al ON (al.C_Payment_ID = p.C_Payment_ID)
                                 INNER JOIN C_Invoice ci        ON (al.C_Invoice_ID = ci.C_Invoice_ID)
                                 LEFT OUTER JOIN AD_User u      ON (p.CreatedBy = u.AD_User_ID)
                                WHERE ci.C_Order_ID = @C_Order_ID
                                  AND p.IsActive    = 'Y'
                                  AND p.DocStatus IN ('CO', 'CL')";
                DataSet ds = DB.ExecuteDataset(sql, OrderParam(C_Order_ID), null);
                if (ds == null || ds.Tables.Count == 0) return;
                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    list.Add(new ActivityData
                    {
                        Type       = "payment",
                        DocumentNo = Util.GetValueOfString(r["DocumentNo"]),
                        UserName   = Util.GetValueOfString(r["UserName"]),
                        Created    = Util.GetValueOfDateTime(r["Created"])
                    });
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadPaymentActivity (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// Adds the order's own milestones as activity rows: a "created" entry
        /// (C_Order.Created / CreatedBy) and, when the order is completed /
        /// closed, an "approval" entry (approximated by C_Order.Updated /
        /// UpdatedBy — the latest change that carried it to CO/CL).
        /// </summary>
        private void LoadOrderMilestoneActivity(int C_Order_ID, List<ActivityData> list)
        {
            try
            {
                string sql = @"SELECT o.Created,
                                      o.Updated,
                                      o.DocStatus,
                                      cu.Name AS CreatedByName,
                                      uu.Name AS UpdatedByName
                                 FROM C_Order o
                                 LEFT OUTER JOIN AD_User cu ON (o.CreatedBy = cu.AD_User_ID)
                                 LEFT OUTER JOIN AD_User uu ON (o.UpdatedBy = uu.AD_User_ID)
                                WHERE o.C_Order_ID = @C_Order_ID";
                DataSet ds = DB.ExecuteDataset(sql, OrderParam(C_Order_ID), null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return;

                DataRow r = ds.Tables[0].Rows[0];
                list.Add(new ActivityData
                {
                    Type     = "created",
                    UserName = Util.GetValueOfString(r["CreatedByName"]),
                    Created  = Util.GetValueOfDateTime(r["Created"])
                });

                string docStatus = Util.GetValueOfString(r["DocStatus"]);
                if (docStatus == "CO" || docStatus == "CL")
                {
                    list.Add(new ActivityData
                    {
                        Type     = "approval",
                        UserName = Util.GetValueOfString(r["UpdatedByName"]),
                        Created  = Util.GetValueOfDateTime(r["Updated"])
                    });
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadOrderMilestoneActivity (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
            }
        }

        /// <summary>Single-parameter helper for the C_Order-scoped activity queries.</summary>
        private SqlParameter[] OrderParam(int C_Order_ID)
        {
            return new SqlParameter[] { new SqlParameter("@C_Order_ID", C_Order_ID) };
        }

        /// <summary>
        /// Returns the moment the order was completed: the Created stamp of its
        /// workflow DocComplete activity (AD_WF_Process -> AD_WF_Activity ->
        /// AD_WF_Node), or null when the order has no completed workflow node.
        ///
        /// Deliberately a standalone query rather than a subselect in the main
        /// SELECT. The main query is rewritten by MRole.AddAccessSQL with
        /// SQL_FULLYQUALIFIED, whose parser walks every FROM/JOIN it can read —
        /// including inside subselects — and appends private-access filters for
        /// those tables to the OUTER WHERE. Aliases that live only inside a
        /// subselect are out of scope there, so the rewritten SQL died with
        /// ORA-00904 ("ADT"."AD_TABLE_ID": invalid identifier) and the whole
        /// overview returned no rows. Run separately, this SQL never reaches
        /// that rewriter.
        /// </summary>
        /// <param name="C_Order_ID">Selected purchase order id.</param>
        private DateTime? GetOrderCompletedDate(int C_Order_ID)
        {
            try
            {
                string sql = @"SELECT MAX(wfa.Created) AS OrderCompletedDate
                                 FROM AD_WF_Process wfp
                                 INNER JOIN AD_WF_Activity wfa
                                         ON (wfa.AD_WF_Process_ID = wfp.AD_WF_Process_ID)
                                 INNER JOIN AD_WF_Node wfn
                                         ON (wfn.AD_WF_Node_ID = wfa.AD_WF_Node_ID)
                                 INNER JOIN AD_Table adt
                                         ON (adt.AD_Table_ID = wfp.AD_Table_ID)
                                WHERE wfp.Record_ID = @C_Order_ID
                                  AND adt.TableName = 'C_Order'
                                  AND wfp.IsActive  = 'Y'
                                  AND wfa.IsActive  = 'Y'
                                  AND wfn.IsActive  = 'Y'
                                  AND wfa.WFState   = 'CC'
                                  AND UPPER(TRIM(wfn.Value)) IN ('DOCCOMPLETE', 'COMPLETE', '(DOCCOMPLETE)')";
                DataSet ds = DB.ExecuteDataset(sql, OrderParam(C_Order_ID), null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
                    return null;
                return Util.GetValueOfDateTime(ds.Tables[0].Rows[0]["OrderCompletedDate"]);
            }
            catch (Exception ex)
            {
                // Non-fatal: the stepper falls back to DateOrdered.
                _log.Severe("GetOrderCompletedDate (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Returns the moment the order's vendor invoice was completed: the
        /// Created stamp of the invoice's workflow DocComplete activity
        /// (C_Invoice -> AD_WF_Process -> AD_WF_Activity -> AD_WF_Node), taking
        /// the latest across every completed invoice raised from the order, or
        /// null when none has a completed workflow node.
        ///
        /// Extracted into a standalone query for the SAME reason as
        /// GetOrderCompletedDate: as a subselect in the main SELECT this workflow
        /// join (AD_WF_Process / AD_WF_Activity / AD_WF_Node / AD_Table) is walked
        /// by MRole.AddAccessSQL's SQL_FULLYQUALIFIED rewriter, which appends
        /// private-access filters referencing aliases that live only inside the
        /// subselect and dies with ORA-00904, returning the whole overview empty.
        /// Run separately (child of an already authorized order) it never reaches
        /// that rewriter.
        /// </summary>
        /// <param name="C_Order_ID">Selected purchase order id.</param>
        private DateTime? GetLastInvoiceDate(int C_Order_ID)
        {
            try
            {
                string sql = @"SELECT MAX(invoice_activity.Created) AS LastInvoiceDate
                                 FROM C_Invoice invoice_header
                                 INNER JOIN AD_WF_Process invoice_process
                                         ON (invoice_process.Record_ID = invoice_header.C_Invoice_ID)
                                 INNER JOIN AD_Table invoice_table
                                         ON (invoice_table.AD_Table_ID = invoice_process.AD_Table_ID)
                                 INNER JOIN AD_WF_Activity invoice_activity
                                         ON (invoice_activity.AD_WF_Process_ID = invoice_process.AD_WF_Process_ID)
                                 INNER JOIN AD_WF_Node invoice_node
                                         ON (invoice_node.AD_WF_Node_ID = invoice_activity.AD_WF_Node_ID)
                                WHERE invoice_header.C_Order_ID = @C_Order_ID
                                  AND invoice_header.IsActive   = 'Y'
                                  AND invoice_header.DocStatus IN ('CO', 'CL')
                                  AND invoice_table.TableName   = 'C_Invoice'
                                  AND invoice_process.IsActive  = 'Y'
                                  AND invoice_activity.IsActive = 'Y'
                                  AND invoice_node.IsActive     = 'Y'
                                  AND invoice_activity.WFState  = 'CC'
                                  AND UPPER(TRIM(invoice_node.Value)) IN ('DOCCOMPLETE', 'COMPLETE', '(DOCCOMPLETE)')";
                DataSet ds = DB.ExecuteDataset(sql, OrderParam(C_Order_ID), null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
                    return null;
                return Util.GetValueOfDateTime(ds.Tables[0].Rows[0]["LastInvoiceDate"]);
            }
            catch (Exception ex)
            {
                // Non-fatal: the Invoice Raised stage falls back to no date.
                _log.Severe("GetLastInvoiceDate (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
                return null;
            }
        }

        // ----------------------------------------------------------------- //
        //  Documents raised against the order (read side)                    //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// Loads the documents prepared from this purchase order — goods receipts
        /// (M_InOut) and vendor invoices (C_Invoice) — newest first. Unlike the
        /// activity feed these carry the table name + record id so the client can
        /// open the document, and they include in-progress documents (drafted /
        /// in-review), not just completed ones, so a receipt awaiting completion
        /// is still reachable. Reversed and voided documents are excluded. Each
        /// side is guarded so a DB issue degrades to a partial list rather than
        /// breaking the overview.
        /// </summary>
        /// <param name="C_Order_ID">Owning purchase order id.</param>
        /// <returns>Document rows ordered newest-first (may be empty).</returns>
        private List<DocumentData> LoadDocuments(int C_Order_ID)
        {
            List<DocumentData> docs = new List<DocumentData>();
            LoadReceiptDocuments(C_Order_ID, docs);
            LoadInvoiceDocuments(C_Order_ID, docs);
            LoadPaymentDocuments(C_Order_ID, docs);

            // Newest first; entries with no document date sink to the bottom.
            docs.Sort((a, b) =>
                b.DocDate.GetValueOrDefault(DateTime.MinValue)
                 .CompareTo(a.DocDate.GetValueOrDefault(DateTime.MinValue)));
            return docs;
        }

        /// <summary>
        /// Adds the goods receipts (M_InOut, IsSoTrx = 'N') raised against this
        /// order, carrying the receipt's active line count.
        /// </summary>
        private void LoadReceiptDocuments(int C_Order_ID, List<DocumentData> list)
        {
            try
            {
                string sql = @"SELECT io.M_InOut_ID,
                                      io.DocumentNo,
                                      io.DocStatus,
                                      io.MovementDate,
                                      (SELECT COUNT(*)
                                         FROM M_InOutLine iol
                                        WHERE iol.M_InOut_ID = io.M_InOut_ID
                                          AND iol.IsActive   = 'Y') AS LineCnt,
                                      (SELECT NVL(SUM(iol.MovementQty * NVL(ol.PriceActual, 0)), 0)
                                         FROM M_InOutLine iol
                                         LEFT OUTER JOIN C_OrderLine ol
                                                ON (ol.C_OrderLine_ID = iol.C_OrderLine_ID)
                                        WHERE iol.M_InOut_ID = io.M_InOut_ID
                                          AND iol.IsActive   = 'Y') AS ReceivedValue
                                 FROM M_InOut io
                                WHERE io.C_Order_ID = @C_Order_ID
                                  AND io.IsActive   = 'Y'
                                  AND io.IsSoTrx    = 'N'
                                  AND io.DocStatus NOT IN ('RE', 'VO')";
                DataSet ds = DB.ExecuteDataset(sql, OrderParam(C_Order_ID), null);
                if (ds == null || ds.Tables.Count == 0) return;
                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    list.Add(new DocumentData
                    {
                        Type       = "grn",
                        TableName  = "M_InOut",
                        RecordId   = Util.GetValueOfInt(r["M_InOut_ID"]),
                        DocumentNo = Util.GetValueOfString(r["DocumentNo"]),
                        DocStatus  = Util.GetValueOfString(r["DocStatus"]),
                        DocDate    = Util.GetValueOfDateTime(r["MovementDate"]),
                        LineCount  = Util.GetValueOfInt(r["LineCnt"]),
                        // Total received value = Σ (received qty × order-line price).
                        Amount     = Util.GetValueOfDecimal(r["ReceivedValue"])
                    });
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadReceiptDocuments (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// Adds the vendor invoices (C_Invoice, IsSoTrx = 'N') raised against this
        /// order, carrying the invoice total and paid flag.
        /// </summary>
        private void LoadInvoiceDocuments(int C_Order_ID, List<DocumentData> list)
        {
            try
            {
                string sql = @"SELECT inv.C_Invoice_ID,
                                      inv.DocumentNo,
                                      inv.DocStatus,
                                      inv.DateInvoiced,
                                      NVL(inv.GrandTotal, 0) AS GrandTotal,
                                      inv.IsPaid
                                 FROM C_Invoice inv
                                WHERE inv.C_Order_ID = @C_Order_ID
                                  AND inv.IsActive   = 'Y'
                                  AND inv.IsSoTrx    = 'N'
                                  AND inv.DocStatus NOT IN ('RE', 'VO')";
                DataSet ds = DB.ExecuteDataset(sql, OrderParam(C_Order_ID), null);
                if (ds == null || ds.Tables.Count == 0) return;
                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    list.Add(new DocumentData
                    {
                        Type       = "invoice",
                        TableName  = "C_Invoice",
                        RecordId   = Util.GetValueOfInt(r["C_Invoice_ID"]),
                        DocumentNo = Util.GetValueOfString(r["DocumentNo"]),
                        DocStatus  = Util.GetValueOfString(r["DocStatus"]),
                        DocDate    = Util.GetValueOfDateTime(r["DateInvoiced"]),
                        Amount     = Util.GetValueOfDecimal(r["GrandTotal"]),
                        IsPaid     = Util.GetValueOfString(r["IsPaid"]) == "Y"
                    });
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadInvoiceDocuments (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// Adds the AP payments (C_Payment, IsReceipt = 'N') allocated to this
        /// order's vendor invoices (via C_AllocationLine -> C_Invoice), carrying
        /// the payment amount (PayAmt) and discount taken (DiscountAmt). DISTINCT
        /// so a payment allocated across several of the order's invoices is listed
        /// once. TableName + RecordId open the AP Payment window on the client.
        /// Reversed / voided payments are excluded.
        /// </summary>
        private void LoadPaymentDocuments(int C_Order_ID, List<DocumentData> list)
        {
            try
            {
                string sql = @"SELECT DISTINCT p.C_Payment_ID,
                                               p.DocumentNo,
                                               p.DocStatus,
                                               p.DateTrx,
                                               NVL(p.PayAmt, 0)      AS PayAmt,
                                               NVL(p.DiscountAmt, 0) AS DiscountAmt
                                 FROM C_Payment p
                                 INNER JOIN C_AllocationLine al ON (al.C_Payment_ID = p.C_Payment_ID)
                                 INNER JOIN C_Invoice ci        ON (al.C_Invoice_ID = ci.C_Invoice_ID)
                                WHERE ci.C_Order_ID = @C_Order_ID
                                  AND p.IsActive    = 'Y'
                                  AND p.IsReceipt   = 'N'
                                  AND p.DocStatus NOT IN ('RE', 'VO')";
                DataSet ds = DB.ExecuteDataset(sql, OrderParam(C_Order_ID), null);
                if (ds == null || ds.Tables.Count == 0) return;
                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    list.Add(new DocumentData
                    {
                        Type        = "payment",
                        TableName   = "C_Payment",
                        RecordId    = Util.GetValueOfInt(r["C_Payment_ID"]),
                        DocumentNo  = Util.GetValueOfString(r["DocumentNo"]),
                        DocStatus   = Util.GetValueOfString(r["DocStatus"]),
                        DocDate     = Util.GetValueOfDateTime(r["DateTrx"]),
                        Amount      = Util.GetValueOfDecimal(r["PayAmt"]),
                        DiscountAmt = Util.GetValueOfDecimal(r["DiscountAmt"])
                    });
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadPaymentDocuments (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
            }
        }

        // ----------------------------------------------------------------- //
        //  Landed cost (read side)                                           //
        // ----------------------------------------------------------------- //
        //  Rendered per cost-component (one row per cost element + distribution
        //  method): the expected budget (C_ExpectedCost, available once the PO
        //  is completed — CO or CL) collapsed onto the matching invoice-linked actual
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
        /// Expected components are loaded whenever C_ExpectedCost rows exist for
        /// the order, regardless of document status (drafted / in-progress /
        /// completed / closed) — the section shows as soon as the data exists;
        /// actual components are always considered.
        /// </summary>
        private List<LandedCostComponentData> LoadLandedCostComponents(PurchaseOrderOverviewData d)
        {
            List<LandedCostComponentData> components = new List<LandedCostComponentData>();

            // Keyed by cost element + distribution so an expected component and
            // its actual invoice line collapse onto a single row.
            Dictionary<string, LandedCostComponentData> map =
                new Dictionary<string, LandedCostComponentData>();

            // Expected costs (and their per-line distribution) are generated when
            // the PO is completed, but a completed-then-reopened order still
            // carries them — so load them unconditionally and let the queries
            // self-gate (they return nothing when no expected cost is defined)
            // rather than hiding a reopened order's distribution breakdown behind
            // a CO/CL check.
            LoadExpectedComponents(d.C_Order_ID, map);
            // Per-line distribution breakdown for each expected component.
            LoadDistributionLines(d.C_Order_ID, map);

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
            // PO goods value = net line amount excluding tax (SubTotal), not the
            // tax/freight-inclusive grand total. SubTotal equals C_Order.TotalLines
            // for a tax-exclusive order and the tax-extracted net for a
            // tax-inclusive one, so the landed value stays correct in both cases.
            decimal poGoodsValue = d.SubTotal;
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
                                                   WHERE NVL(ecd.IsActive, 'Y') = 'Y'
                                                   GROUP BY ecd.C_ExpectedCost_ID) ead
                                        ON (ead.C_ExpectedCost_ID = ec.C_ExpectedCost_ID)
                                 LEFT OUTER JOIN M_CostElement ce
                                        ON (ce.M_CostElement_ID = ec.M_CostElement_ID)
                                WHERE ec.C_Order_ID = @C_Order_ID
                                  AND NVL(ec.IsActive, 'Y') = 'Y'
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
                    // Distribution method comes from C_ExpectedCost.LandedCostDistribution.
                    // Set it explicitly (not just via the map key) so the expected
                    // value always drives the displayed method, even if an actual
                    // (C_LandedCost) row registered the component first.
                    c.DistributionCode = Util.GetValueOfString(r["DistCode"]);
                }
            }
            catch (Exception ex)
            {
                // Non-fatal: keep the overview working, just drop expected costs.
                _log.Severe("LoadExpectedComponents (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// Loads the per-line distribution breakdown for each expected cost
        /// component. Every C_ExpectedCost (one component + distribution method)
        /// carries C_ExpectedCostDistribution rows recording exactly how much of
        /// that cost was distributed onto each order line (Amt), plus the base /
        /// quantity the distribution was computed against. Rows are attached to
        /// the matching component (cost element + distribution).
        /// </summary>
        private void LoadDistributionLines(
            int C_Order_ID, Dictionary<string, LandedCostComponentData> map)
        {
            try
            {
                string sql = @"SELECT ec.M_CostElement_ID       AS CostElementId,
                                      ec.LandedCostDistribution  AS DistCode,
                                      ol.Line                    AS LineNo,
                                      NVL(p.Name, NVL(ch.Name, ol.Description)) AS LineLabel,
                                      NVL(ecd.Amt, 0)            AS Amt,
                                      NVL(ecd.Base, 0)           AS Base,
                                      NVL(ecd.Qty, 0)            AS Qty
                                 FROM C_ExpectedCostDistribution ecd
                                 INNER JOIN C_ExpectedCost ec
                                        ON (ec.C_ExpectedCost_ID = ecd.C_ExpectedCost_ID)
                                 INNER JOIN C_OrderLine ol
                                        ON (ol.C_OrderLine_ID = ecd.C_OrderLine_ID)
                                 LEFT OUTER JOIN M_Product p ON (p.M_Product_ID = ol.M_Product_ID)
                                 LEFT OUTER JOIN C_Charge  ch ON (ch.C_Charge_ID = ol.C_Charge_ID)
                                WHERE ec.C_Order_ID = @C_Order_ID
                                  AND NVL(ecd.IsActive, 'Y') = 'Y'
                                ORDER BY ec.M_CostElement_ID, ol.Line";
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
                    if (c.DistributionLines == null)
                        c.DistributionLines = new List<DistributionLineData>();
                    c.DistributionLines.Add(new DistributionLineData
                    {
                        LineNo    = Util.GetValueOfInt(r["LineNo"]),
                        LineLabel = Util.GetValueOfString(r["LineLabel"]),
                        Amt       = Util.GetValueOfDecimal(r["Amt"]),
                        Base      = Util.GetValueOfDecimal(r["Base"]),
                        Qty       = Util.GetValueOfDecimal(r["Qty"])
                    });
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadDistributionLines (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
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
                                 INNER JOIN C_Invoice inv
                                        ON (inv.C_Invoice_ID = il.C_Invoice_ID
                                            AND inv.IsActive = 'Y'
                                            AND inv.IsSoTrx = 'N'
                                            AND inv.DocStatus IN ('CO', 'CL'))
                                 LEFT OUTER JOIN C_BPartner bp
                                        ON (bp.C_BPartner_ID = inv.C_BPartner_ID)
                                 LEFT OUTER JOIN M_CostElement ce
                                        ON (ce.M_CostElement_ID = NVL(lc.M_CostElement_ID, lca.M_CostElement_ID))
                                 LEFT OUTER JOIN C_Charge ch
                                        ON (ch.C_Charge_ID = il.C_Charge_ID)
                                WHERE lca.IsActive = 'Y'
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

        /// <summary>
        /// A document prepared from the order (goods receipt or vendor invoice).
        /// TableName + RecordId are what the client opens the document with.
        /// </summary>
        public class DocumentData
        {
            public string    Type        { get; set; }   // grn | invoice | payment
            public string    TableName   { get; set; }   // M_InOut | C_Invoice | C_Payment
            public int       RecordId    { get; set; }   // M_InOut_ID | C_Invoice_ID | C_Payment_ID
            public string    DocumentNo  { get; set; }
            public string    DocStatus   { get; set; }   // DocStatus code
            public DateTime? DocDate     { get; set; }   // MovementDate | DateInvoiced | DateTrx
            public decimal?  Amount      { get; set; }   // invoice grand total | GRN received value | payment PayAmt
            public int       LineCount   { get; set; }   // receipt line count
            public bool      IsPaid      { get; set; }   // invoice only
            public decimal?  DiscountAmt { get; set; }   // AP payment discount taken
        }

        public class PurchaseOrderLineData
        {
            public int      C_OrderLine_ID { get; set; }
            public int      Line           { get; set; }
            public decimal  QtyEntered     { get; set; }   // C_OrderLine.QtyEntered (entered UOM) — the displayed qty
            public decimal  QtyOrdered     { get; set; }
            public decimal  QtyDelivered   { get; set; }
            public decimal  QtyInvoiced    { get; set; }
            public decimal  PriceActual    { get; set; }
            public decimal  LineNetAmt     { get; set; }
            public decimal  BudgetViolationAmount { get; set; }   // C_OrderLine.BudgetViolationAmount (0 = within budget)
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
            public string   AttributeSetInstance { get; set; }   // M_AttributeSetInstance.Description (size / lot / serial ...)
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
            // Per-line distribution breakdown (C_ExpectedCostDistribution).
            public List<DistributionLineData> DistributionLines { get; set; }
        }

        public class DistributionLineData
        {
            public int      LineNo    { get; set; }   // C_OrderLine.Line
            public string   LineLabel { get; set; }   // product / charge / description
            public decimal  Amt       { get; set; }   // distributed amount for this line
            public decimal  Base      { get; set; }   // base value the split was computed on
            public decimal  Qty       { get; set; }   // quantity the split was computed on
        }

        public class NoteData
        {
            public string NoteType { get; set; }   // header | line
            public string Text     { get; set; }
        }

        public class HistoryData
        {
            public int      C_OrderLine_ID { get; set; }
            public int      LineNo         { get; set; }
            public DateTime? ChangedOn     { get; set; }
            public decimal  QtyEntered     { get; set; }   // snapshot C_OrderLineHistory.QtyEntered (entered UOM)
            public decimal  QtyOrdered     { get; set; }
            public decimal  PriceActual    { get; set; }
            public decimal  LineNetAmt     { get; set; }
            public decimal  Discount       { get; set; }
            public string   Description    { get; set; }
            public string   ProductName    { get; set; }
            public string   ChargeName     { get; set; }
            public string   UOMSymbol      { get; set; }
            public int      UOMPrecision   { get; set; }
            public int      StdPrecision   { get; set; }
        }

        public class ActivityData
        {
            public string    Type            { get; set; }   // note | grn | invoice | payment | approval | created
            public int       CM_ChatEntry_ID { get; set; }
            public int       AD_User_ID      { get; set; }
            public string    UserName        { get; set; }   // actor
            public string    Text            { get; set; }   // free text (notes only)
            public string    DocumentNo      { get; set; }   // related document (grn / invoice / payment)
            public int       Count           { get; set; }   // grn line count (0 otherwise)
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
            public string    PriorityRule    { get; set; }   // C_Order.PriorityRule (1/3/5/7/9)
            public bool      Posted          { get; set; }   // C_Order.Posted = 'Y'
            public bool      IsBudgetViolated         { get; set; }   // C_Order.IsBudgetViolated = 'Y'
            public decimal   MaxBudgetViolationAmount { get; set; }   // C_Order.MaxBudgetViolationAmount (acct currency)

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
            public string    PriceListName   { get; set; }
            public string    WarehouseName   { get; set; }   // Ship To
            public string    OrgName         { get; set; }   // Bill To

            // Currency
            public string    CurSymbol       { get; set; }
            public string    ISO_Code        { get; set; }
            public int       StdPrecision    { get; set; }

            // Totals
            public decimal   GrandTotal      { get; set; }
            public decimal   TotalLines      { get; set; }   // C_Order.TotalLines (raw)
            public decimal   SubTotal        { get; set; }   // net of tax = GrandTotal - TaxAmt
            public decimal   TaxAmt          { get; set; }   // SUM(C_OrderTax.TaxAmt)

            // Stat-strip aggregates
            public decimal   TotalQtyOrdered        { get; set; }
            public decimal   TotalQtyDelivered      { get; set; }
            public decimal   TotalQtyInvoiced       { get; set; }
            public int       LineCount              { get; set; }
            public int       FullyReceivedLineCount { get; set; }
            public int       DeliverableLineCount   { get; set; }   // stockable item lines

            // Linked / origin documents (Generated From strip)
            public string    RefOrderDocNo        { get; set; }   // originating sales order
            public int       RefOrderId           { get; set; }   // C_Order.Ref_Order_ID
            public int       RequisitionLineCount { get; set; }
            public int       RequisitionId        { get; set; }   // first M_Requisition
            public string    RequisitionDocNo     { get; set; }
            public int       RequisitionCount     { get; set; }   // distinct requisitions
            public int       ContractMasterId     { get; set; }   // C_Order.VAS_ContractMaster_ID
            public string    ContractMasterNo     { get; set; }

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
            public DateTime? OrderCompletedDate { get; set; }    // DocComplete workflow node
            public DateTime? LastReceiptDate    { get; set; }    // latest goods receipt
            public DateTime? LastInvoiceDate    { get; set; }    // latest vendor invoice
            public DateTime? LastPaymentDate    { get; set; }    // latest payment allocated

            // Collections
            public List<PurchaseOrderLineData> Lines     { get; set; }
            public List<NoteData>              Notes     { get; set; }
            public List<ActivityData>          Activity  { get; set; }
            public List<HistoryData>           History   { get; set; }
            public List<DocumentData>          Documents { get; set; }

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
