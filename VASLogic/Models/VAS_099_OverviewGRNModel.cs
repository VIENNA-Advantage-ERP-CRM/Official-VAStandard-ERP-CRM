/// <summary>
/// Module Name : VASLogic
/// Purpose     : Goods Receipt Note (GRN) Overview tab panel data (read side).
///               Returns header identity, vendor, linked purchase order,
///               KPI aggregates (received value / lines / received qty / QC
///               applicable lines), a compact receipt timeline (PO -> expected
///               -> received -> posted) and the material lines for a selected
///               goods receipt (M_InOut with IsSOTrx = 'N').
/// Chronological development:
///   VAI163   2026-07-06  Created. Optional module columns
///                        (VA010_QualityPlan_ID, CurrentCostPrice,
///                        VA024_UnitPrice) are guarded through AD_Column so the
///                        panel works whether or not those modules are installed.
///   VAI163   2026-07-17  ReferenceInvoice now carries the linked AP invoice
///                        document no (M_InOut -> C_Order -> C_Invoice, latest
///                        first) instead of the free-text M_InOut.POReference.
///                        Added RefOrderDocNo (originating sales order, read
///                        through the linked PO's Ref_Order_ID, as VAS_092
///                        does). Supplier* members renamed to Vendor*.
///   VAI163   2026-07-27  Surfaced M_InOut.IsDropShip (drop-shipment flag) and the
///                        per-line Attribute Set Instance description (join on a
///                        real M_AttributeSetInstance_ID > 0 only).
///   VAI163   2026-07-28  Added the fields the panel's References / Notes /
///                        document-status timeline sections need:
///                        M_InOut.Description (GRN header note), C_Order.POReference
///                        (vendor's PO reference), M_InOut.Created / Updated
///                        (drafted / in-progress stage dates) and the linked AP
///                        invoice's DateInvoiced (invoiced stage date, read
///                        alongside its document no in one query).
///   VAI163   2026-07-28  Quality inspection + invoice-link additions:
///                        - AcceptedQty / RejectedQty aggregates, summed from
///                          M_InOutLineConfirm.ConfirmedQty / ScrappedQty over the
///                          receipt's confirmation lines flagged for quality check
///                          (VA010_QualCheckMArk = 'Y').
///                        - IsConfirmationDocType, from C_DocType.IsShipConfirm /
///                          IsPickQAConfirm — i.e. an "MM Receipt with
///                          Confirmation" document type.
///                        - QualityParams: the per-product VA010 inspection rows
///                          (VA010_ShipConfParameters joined to VA010_TestParameter
///                          and VA010_TestPrmtrList) with a derived pass / fail /
///                          pending status.
///                        - ReferenceInvoice now prefers the receipt-scoped link
///                          (C_InvoiceLine.M_InOutLine_ID -> this receipt's lines)
///                          and only falls back to the order-scoped lookup, so an
///                          invoice generated from the GRN itself is found.
///                        Every VA010 table / column is guarded through the
///                        dictionary, so the panel works with or without the
///                        quality module installed.
///   VAI163   2026-07-28  - PostedDate: the moment the receipt was actually
///                          posted, read from the Created stamp of its Fact_Acct
///                          rows rather than the accounting/movement date, so the
///                          timeline's Posting stage shows when posting really
///                          happened.
///                        - Activity: the receipt's audit trail (created,
///                          updated, completed, posted, confirmations, invoices
///                          and chat notes), newest-first.
///                        Both the posting-date and workflow-completion lookups
///                        are deliberately standalone queries, never subselects
///                        of the main SELECT — see GetPostedDate for why
///                        MRole.AddAccessSQL makes that mandatory.
///   VAI163   2026-07-28  - Purchase order and sales order are now resolved by
///                          C_Order.IsSOTrx instead of by position. A drop-ship
///                          receipt can link straight to the SALES order with the
///                          purchase order on Ref_Order_ID — the reverse of the
///                          normal case — which made Reference Sales Order and the
///                          PO fields show each other's document.
///                        - ReferenceInvoice now starts from M_InOut.C_Invoice_ID
///                          (the invoice the receipt was raised against), then the
///                          receipt-scoped line link, then the PURCHASE order. The
///                          order-scoped fallback previously used the raw
///                          C_Order_ID, which on a drop shipment is the sales
///                          order.
///                        - A receipt raised against an AP invoice prices its lines
///                          from that invoice (C_InvoiceLine.PriceActual, matched
///                          on product) instead of leaving Rate at 0, and
///                          ReceivedValue is recomputed from the lines so the KPI
///                          and totals footer agree with the rows on screen.
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
    public class VAS_099_OverviewGRNModel
    {
        private static readonly VLogger _log =
            VLogger.GetVLogger(typeof(VAS_099_OverviewGRNModel).FullName);

        /// <summary>
        /// Returns the full overview payload for the selected goods receipt.
        /// MRole access filtering is applied ONLY on the main physical table
        /// (M_InOut alias "io"); the child line query inherits the parent's
        /// authorization and is not separately role-filtered.
        /// </summary>
        /// <param name="ctx">User context (client/org/role).</param>
        /// <param name="M_InOut_ID">Selected goods receipt id.</param>
        /// <returns>Populated <see cref="GRNOverviewData"/>; an empty instance
        /// when the id is invalid or no accessible row is found.</returns>
        public GRNOverviewData GetGRNOverview(Ctx ctx, int M_InOut_ID)
        {
            GRNOverviewData result = new GRNOverviewData();
            if (M_InOut_ID <= 0) return result;

            // Optional module columns on M_InOutLine — resolved once so the SQL
            // below only references columns that actually exist in this schema.
            bool hasQualityPlan = ColumnExists("M_InOutLine", "VA010_QualityPlan_ID");
            bool hasCurrentCost = ColumnExists("M_InOutLine", "CurrentCostPrice");
            bool hasUnitPrice   = ColumnExists("M_InOutLine", "VA024_UnitPrice");

            // Quality-check mark on the receipt confirmation line — the accepted /
            // rejected quantities are only summed over lines carrying it.
            bool hasQcMark = ColumnExists("M_InOutLineConfirm", "VA010_QualCheckMArk");
            string qcConfirmFilter = hasQcMark
                ? " AND COALESCE(lc.VA010_QualCheckMArk, 'N') = 'Y'"
                : "";

            // "MM Receipt with Confirmation" document types are the ones that ask
            // for a ship/receipt or pick-QA confirmation. Both flags are core
            // columns, but they are guarded anyway so an older dictionary degrades
            // to "no confirmation" rather than failing the whole query.
            bool hasShipConfirm   = ColumnExists("C_DocType", "IsShipConfirm");
            bool hasPickQAConfirm = ColumnExists("C_DocType", "IsPickQAConfirm");
            string confirmDocExpr = BuildConfirmDocExpr(hasShipConfirm, hasPickQAConfirm);

            // COALESCE(ol.PriceActual, [l.CurrentCostPrice], [l.VA024_UnitPrice], 0).
            // The header aggregate keeps this order-based form; the line query gets
            // an invoice-aware variant once the referenced invoice id is known, and
            // the received value is then recomputed from those lines.
            string rateExpr = BuildRateExpr(hasCurrentCost, hasUnitPrice);
            // 1 when the line carries a quality plan, else 0.
            string qcCountExpr = hasQualityPlan
                ? "CASE WHEN l.VA010_QualityPlan_ID IS NOT NULL THEN 1 ELSE 0 END"
                : "0";

            string sql = @"SELECT
                              io.M_InOut_ID,
                              io.DocumentNo,
                              io.DocStatus,
                              io.Processed,
                              io.Posted,
                              io.MovementDate,
                              io.DateAcct,
                              io.PriorityRule,
                              io.IsDropShip,
                              io.Description   AS HeaderDescription,
                              io.Created,
                              io.Updated,
                              io.C_Order_ID,
                              io.C_Invoice_ID,
                              /* Purchase / sales order are resolved by IsSOTrx, not
                                 by position. On a drop shipment the receipt can be
                                 linked directly to the SALES order, with the
                                 purchase order hanging off Ref_Order_ID — the
                                 reverse of the normal case. Picking by flag keeps
                                 each field showing the document it is labelled
                                 with either way. */
                              CASE WHEN ord.IsSOTrx  = 'N' THEN ord.C_Order_ID
                                   WHEN refo.IsSOTrx = 'N' THEN refo.C_Order_ID
                              END                                 AS PO_Order_ID,
                              CASE WHEN ord.IsSOTrx  = 'N' THEN ord.DocumentNo
                                   WHEN refo.IsSOTrx = 'N' THEN refo.DocumentNo
                              END                                 AS PO_DocumentNo,
                              CASE WHEN ord.IsSOTrx  = 'N' THEN ord.DateOrdered
                                   WHEN refo.IsSOTrx = 'N' THEN refo.DateOrdered
                              END                                 AS PODate,
                              CASE WHEN ord.IsSOTrx  = 'N' THEN ord.DatePromised
                                   WHEN refo.IsSOTrx = 'N' THEN refo.DatePromised
                              END                                 AS ExpectedDate,
                              CASE WHEN ord.IsSOTrx  = 'N' THEN ord.POReference
                                   WHEN refo.IsSOTrx = 'N' THEN refo.POReference
                              END                                 AS PO_Reference,
                              CASE WHEN ord.IsSOTrx  = 'Y' THEN ord.DocumentNo
                                   WHEN refo.IsSOTrx = 'Y' THEN refo.DocumentNo
                              END                                 AS RefOrderDocNo,
                              bp.Name          AS VendorName,
                              bp.TaxID         AS VendorTaxID,
                              bpl.Name         AS VendorLocationName,
                              loc.Address1     AS Address1,
                              loc.Address2     AS Address2,
                              loc.City         AS City,
                              loc.Postal       AS Postal,
                              ctry.Name        AS CountryName,
                              reg.Name         AS RegionName,
                              contact.Name     AS ContactName,
                              contact.Phone2   AS ContactPhone,
                              contact.EMail    AS ContactEmail,
                              wh.Name          AS WarehouseName,
                              receiver.Name    AS ReceivedBy,
                              cur.CurSymbol    AS CurSymbol,
                              cur.ISO_Code     AS ISO_Code,
                              cur.StdPrecision AS StdPrecision,
                              dt.Name          AS DocTypeName,
                              " + confirmDocExpr + @"                             AS IsConfirmationDocType,
                              (SELECT NVL(SUM(NVL(lc.ConfirmedQty, 0)), 0)
                                 FROM M_InOutLineConfirm lc
                                INNER JOIN M_InOutLine cl ON (cl.M_InOutLine_ID = lc.M_InOutLine_ID
                                                              AND cl.IsActive    = 'Y')
                                WHERE cl.M_InOut_ID = io.M_InOut_ID
                                  AND lc.IsActive   = 'Y'" + qcConfirmFilter + @")  AS AcceptedQty,
                              (SELECT NVL(SUM(NVL(lc.ScrappedQty, 0)), 0)
                                 FROM M_InOutLineConfirm lc
                                INNER JOIN M_InOutLine cl ON (cl.M_InOutLine_ID = lc.M_InOutLine_ID
                                                              AND cl.IsActive    = 'Y')
                                WHERE cl.M_InOut_ID = io.M_InOut_ID
                                  AND lc.IsActive   = 'Y'" + qcConfirmFilter + @")  AS RejectedQty,
                              (SELECT COUNT(*)
                                 FROM M_InOutLine l
                                WHERE l.M_InOut_ID = io.M_InOut_ID
                                  AND l.IsActive   = 'Y')                       AS LineCount,
                              (SELECT NVL(SUM(NVL(l.MovementQty, 0)), 0)
                                 FROM M_InOutLine l
                                WHERE l.M_InOut_ID = io.M_InOut_ID
                                  AND l.IsActive   = 'Y')                       AS ReceivedQty,
                              (SELECT NVL(SUM(" + qcCountExpr + @"), 0)
                                 FROM M_InOutLine l
                                WHERE l.M_InOut_ID = io.M_InOut_ID
                                  AND l.IsActive   = 'Y')                       AS QcLineCount,
                              (SELECT NVL(SUM(NVL(l.MovementQty, 0) * " + rateExpr + @"), 0)
                                 FROM M_InOutLine l
                                 LEFT OUTER JOIN C_OrderLine ol ON (ol.C_OrderLine_ID = l.C_OrderLine_ID)
                                WHERE l.M_InOut_ID = io.M_InOut_ID
                                  AND l.IsActive   = 'Y')                       AS ReceivedValue
                            FROM M_InOut io
                            INNER JOIN C_BPartner bp        ON (io.C_BPartner_ID          = bp.C_BPartner_ID)
                            LEFT OUTER JOIN C_Order ord      ON (io.C_Order_ID            = ord.C_Order_ID)
                            LEFT OUTER JOIN C_Order refo     ON (ord.Ref_Order_ID         = refo.C_Order_ID)
                            LEFT OUTER JOIN C_BPartner_Location bpl ON (io.C_BPartner_Location_ID = bpl.C_BPartner_Location_ID)
                            LEFT OUTER JOIN C_Location loc   ON (bpl.C_Location_ID         = loc.C_Location_ID)
                            LEFT OUTER JOIN C_Country ctry   ON (loc.C_Country_ID          = ctry.C_Country_ID)
                            LEFT OUTER JOIN C_Region reg     ON (loc.C_Region_ID           = reg.C_Region_ID)
                            LEFT OUTER JOIN AD_User contact  ON (io.AD_User_ID             = contact.AD_User_ID)
                            LEFT OUTER JOIN M_Warehouse wh   ON (io.M_Warehouse_ID         = wh.M_Warehouse_ID)
                            LEFT OUTER JOIN AD_User receiver ON (io.SalesRep_ID            = receiver.AD_User_ID)
                            LEFT OUTER JOIN C_Currency cur   ON (ord.C_Currency_ID         = cur.C_Currency_ID)
                            LEFT OUTER JOIN C_DocType dt     ON (io.C_DocType_ID           = dt.C_DocType_ID)
                            WHERE io.M_InOut_ID = @M_InOut_ID
                              AND io.IsActive   = 'Y'
                              AND io.IsSOTrx    = 'N'";

            // MRole access only on the primary physical table the user is fetching.
            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql, "io", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            SqlParameter[] param = new SqlParameter[]
            {
                new SqlParameter("@M_InOut_ID", M_InOut_ID)
            };

            DataSet ds = DB.ExecuteDataset(sql, param, null);
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
                return result;

            DataRow r = ds.Tables[0].Rows[0];

            // ----- Header / identity -----
            result.M_InOut_ID       = Util.GetValueOfInt(r["M_InOut_ID"]);
            result.DocumentNo       = Util.GetValueOfString(r["DocumentNo"]);
            result.StatusCode       = Util.GetValueOfString(r["DocStatus"]);
            result.Processed        = Util.GetValueOfString(r["Processed"]) == "Y";
            result.Posted           = Util.GetValueOfString(r["Posted"]) == "Y";
            result.MovementDate     = Util.GetValueOfDateTime(r["MovementDate"]);
            result.PostingDate      = Util.GetValueOfDateTime(r["DateAcct"]);
            result.PriorityCode     = Util.GetValueOfString(r["PriorityRule"]);
            result.IsDropShip       = Util.GetValueOfString(r["IsDropShip"]) == "Y";
            result.Description      = Util.GetValueOfString(r["HeaderDescription"]);
            result.Created          = Util.GetValueOfDateTime(r["Created"]);
            result.Updated          = Util.GetValueOfDateTime(r["Updated"]);
            result.C_Order_ID       = Util.GetValueOfInt(r["C_Order_ID"]);
            result.C_Invoice_ID     = Util.GetValueOfInt(r["C_Invoice_ID"]);
            result.PurchaseOrderId  = Util.GetValueOfInt(r["PO_Order_ID"]);
            result.PONo             = Util.GetValueOfString(r["PO_DocumentNo"]);
            result.PODate           = Util.GetValueOfDateTime(r["PODate"]);
            result.ExpectedDate     = Util.GetValueOfDateTime(r["ExpectedDate"]);
            result.POReference      = Util.GetValueOfString(r["PO_Reference"]);
            result.RefOrderDocNo    = Util.GetValueOfString(r["RefOrderDocNo"]);

            // Invoice raised for this receipt. The receipt-scoped link is tried
            // first (invoice lines pointing at this receipt's lines) so an invoice
            // generated from the GRN itself is found; only when there is none does
            // it fall back to the order-scoped lookup. The invoice date comes back
            // with it — the timeline's "Invoiced" stage captions with it.
            // The order-scoped fallback must use the PURCHASE order — on a drop
            // shipment C_Order_ID can be the sales order, and an AP invoice is
            // never raised against that.
            DateTime? invoiceDate;
            result.ReferenceInvoice     = GetReferenceInvoice(
                M_InOut_ID, result.C_Invoice_ID, result.PurchaseOrderId, out invoiceDate);
            result.ReferenceInvoiceDate = invoiceDate;

            result.VendorName         = Util.GetValueOfString(r["VendorName"]);
            result.VendorTaxID        = Util.GetValueOfString(r["VendorTaxID"]);
            result.VendorLocationName = Util.GetValueOfString(r["VendorLocationName"]);
            result.ContactName        = Util.GetValueOfString(r["ContactName"]);
            result.ContactPhone       = Util.GetValueOfString(r["ContactPhone"]);
            result.ContactEmail       = Util.GetValueOfString(r["ContactEmail"]);
            result.WarehouseName      = Util.GetValueOfString(r["WarehouseName"]);
            result.ReceivedBy         = Util.GetValueOfString(r["ReceivedBy"]);

            result.VendorAddress = BuildAddress(
                Util.GetValueOfString(r["Address1"]),
                Util.GetValueOfString(r["Address2"]),
                Util.GetValueOfString(r["City"]),
                Util.GetValueOfString(r["RegionName"]),
                Util.GetValueOfString(r["Postal"]),
                Util.GetValueOfString(r["CountryName"]));

            // ----- Currency -----
            result.CurSymbol    = Util.GetValueOfString(r["CurSymbol"]);
            result.ISO_Code     = Util.GetValueOfString(r["ISO_Code"]);
            result.StdPrecision = Util.GetValueOfInt(r["StdPrecision"]);

            // ----- KPI aggregates -----
            result.LineCount     = Util.GetValueOfInt(r["LineCount"]);
            result.ReceivedQty   = Util.GetValueOfDecimal(r["ReceivedQty"]);
            result.QcLineCount   = Util.GetValueOfInt(r["QcLineCount"]);
            result.ReceivedValue = Util.GetValueOfDecimal(r["ReceivedValue"]);
            result.AcceptedQty   = Util.GetValueOfDecimal(r["AcceptedQty"]);
            result.RejectedQty   = Util.GetValueOfDecimal(r["RejectedQty"]);

            // ----- Document type / confirmation -----
            result.DocTypeName           = Util.GetValueOfString(r["DocTypeName"]);
            result.IsConfirmationDocType =
                Util.GetValueOfString(r["IsConfirmationDocType"]) == "Y";

            // ----- Material lines -----
            // A receipt raised against an AP invoice prices its lines from that
            // invoice; everything else keeps the order-based rate.
            string lineRateExpr = result.C_Invoice_ID > 0
                ? BuildInvoiceRateExpr(hasCurrentCost, hasUnitPrice)
                : rateExpr;
            result.Lines = LoadLines(M_InOut_ID, result.C_Invoice_ID,
                                     result.StdPrecision, lineRateExpr, hasQualityPlan);

            // Keep the KPI card and the totals footer equal to the sum of the rows
            // on screen — the header aggregate above cannot see the invoice price.
            if (result.Lines.Count > 0)
            {
                decimal linesTotal = 0;
                foreach (GRNLineData ln in result.Lines) linesTotal += ln.LineValue;
                result.ReceivedValue = linesTotal;
            }

            // ----- Quality inspection parameters (VA010) -----
            result.QualityParams = LoadQualityParams(M_InOut_ID);

            // ----- When the receipt was actually posted -----
            // Fact_Acct rows are written at posting time, so their Created stamp is
            // the real posting moment. DateAcct is only the accounting date the
            // posting was booked to — on most receipts it equals the movement date,
            // which is exactly what the timeline must not show here.
            result.PostedDate = GetPostedDate(M_InOut_ID);

            // ----- Audit trail -----
            result.Activity = LoadActivity(M_InOut_ID, result.StatusCode);

            return result;
        }

        /// <summary>
        /// Builds the unit-rate SQL expression, preferring the linked PO line
        /// price and falling back through whichever optional cost columns exist
        /// on M_InOutLine, ending at 0.
        /// </summary>
        private string BuildRateExpr(bool hasCurrentCost, bool hasUnitPrice)
        {
            StringBuilder sb = new StringBuilder("COALESCE(ol.PriceActual");
            if (hasCurrentCost) sb.Append(", l.CurrentCostPrice");
            if (hasUnitPrice)   sb.Append(", l.VA024_UnitPrice");
            sb.Append(", 0)");
            return sb.ToString();
        }

        /// <summary>
        /// Builds the line-level unit-rate expression for a receipt that was
        /// created against an AP invoice. The referenced invoice's own line price
        /// wins over the purchase-order price — that is the whole point of raising
        /// the receipt from the invoice — falling back through the same optional
        /// cost columns as <see cref="BuildRateExpr"/>.
        ///
        /// The invoice price is a scalar subquery, never a join: a product billed
        /// on more than one invoice line would otherwise multiply the receipt's
        /// rows. MAX picks one deterministic price when that happens.
        /// </summary>
        private string BuildInvoiceRateExpr(bool hasCurrentCost, bool hasUnitPrice)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("COALESCE((SELECT MAX(ivl.PriceActual) FROM C_InvoiceLine ivl");
            sb.Append(" WHERE ivl.C_Invoice_ID = @C_Invoice_ID");
            sb.Append(" AND ivl.M_Product_ID = l.M_Product_ID");
            sb.Append(" AND ivl.IsActive = 'Y')");
            sb.Append(", ol.PriceActual");
            if (hasCurrentCost) sb.Append(", l.CurrentCostPrice");
            if (hasUnitPrice)   sb.Append(", l.VA024_UnitPrice");
            sb.Append(", 0)");
            return sb.ToString();
        }

        /// <summary>
        /// Builds the 'Y'/'N' expression that marks a document type as one that
        /// requires a confirmation (an "MM Receipt with Confirmation"). Either the
        /// ship/receipt or the pick-QA confirmation flag qualifies; with neither
        /// column present the expression is a constant 'N'.
        /// </summary>
        private string BuildConfirmDocExpr(bool hasShipConfirm, bool hasPickQAConfirm)
        {
            List<string> flags = new List<string>();
            if (hasShipConfirm)   flags.Add("COALESCE(dt.IsShipConfirm, 'N')   = 'Y'");
            if (hasPickQAConfirm) flags.Add("COALESCE(dt.IsPickQAConfirm, 'N') = 'Y'");
            if (flags.Count == 0) return "'N'";
            return "CASE WHEN " + string.Join(" OR ", flags.ToArray()) + " THEN 'Y' ELSE 'N' END";
        }

        /// <summary>
        /// Returns the document no of the invoice raised for this goods receipt.
        /// The receipt-scoped link is authoritative — C_InvoiceLine carries
        /// M_InOutLine_ID, so an invoice generated from the GRN itself is matched
        /// exactly. Only when no such invoice exists does it fall back to the
        /// order-scoped lookup (any AP invoice against the parent PO), which is
        /// broader: on a part-received PO that invoice may cover other receipts.
        /// </summary>
        /// <param name="M_InOut_ID">Selected goods receipt id.</param>
        /// <param name="C_Invoice_ID">Invoice the receipt was raised against; 0 when none.</param>
        /// <param name="C_Order_ID">Parent PURCHASE order id; 0 when unlinked. Never
        /// the sales order — a drop-ship receipt can link to the sales order, and no
        /// AP invoice is ever raised against that.</param>
        /// <param name="dateInvoiced">Receives the invoice date; null when none.</param>
        private string GetReferenceInvoice(int M_InOut_ID, int C_Invoice_ID, int C_Order_ID,
                                           out DateTime? dateInvoiced)
        {
            dateInvoiced = null;
            if (M_InOut_ID <= 0) return "";

            // The receipt names its invoice outright — nothing to infer.
            if (C_Invoice_ID > 0)
            {
                string referenced = GetInvoiceById(C_Invoice_ID, out dateInvoiced);
                if (!string.IsNullOrEmpty(referenced)) return referenced;
            }

            try
            {
                string sql = @"SELECT inv.DocumentNo, inv.DateInvoiced
                                 FROM C_Invoice inv
                                WHERE inv.IsActive   = 'Y'
                                  AND inv.IsSOTrx    = 'N'
                                  AND inv.DocStatus NOT IN ('RE', 'VO')
                                  AND EXISTS (SELECT 1
                                                FROM C_InvoiceLine il
                                               INNER JOIN M_InOutLine iol
                                                       ON (iol.M_InOutLine_ID = il.M_InOutLine_ID)
                                               WHERE il.C_Invoice_ID = inv.C_Invoice_ID
                                                 AND il.IsActive     = 'Y'
                                                 AND iol.M_InOut_ID  = @M_InOut_ID)
                                ORDER BY inv.DateInvoiced DESC, inv.C_Invoice_ID DESC";
                SqlParameter[] param = new SqlParameter[]
                {
                    new SqlParameter("@M_InOut_ID", M_InOut_ID)
                };

                DataSet ds = DB.ExecuteDataset(sql, param, null);
                if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                {
                    DataRow row = ds.Tables[0].Rows[0];
                    dateInvoiced = Util.GetValueOfDateTime(row["DateInvoiced"]);
                    return Util.GetValueOfString(row["DocumentNo"]);
                }
            }
            catch (Exception ex)
            {
                _log.Severe("GetReferenceInvoice (M_InOut_ID=" + M_InOut_ID + "): " + ex.Message);
            }

            // Nothing links back to this receipt — fall back to the parent order.
            return GetLatestInvoice(C_Order_ID, out dateInvoiced);
        }

        /// <summary>
        /// Reads one invoice's document no and date by id, ignoring reversed and
        /// voided documents.
        /// </summary>
        private string GetInvoiceById(int C_Invoice_ID, out DateTime? dateInvoiced)
        {
            dateInvoiced = null;
            if (C_Invoice_ID <= 0) return "";

            try
            {
                string sql = @"SELECT inv.DocumentNo, inv.DateInvoiced
                                 FROM C_Invoice inv
                                WHERE inv.C_Invoice_ID = @C_Invoice_ID
                                  AND inv.IsActive     = 'Y'
                                  AND inv.DocStatus NOT IN ('RE', 'VO')";
                SqlParameter[] param = new SqlParameter[]
                {
                    new SqlParameter("@C_Invoice_ID", C_Invoice_ID)
                };

                DataSet ds = DB.ExecuteDataset(sql, param, null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
                    return "";

                DataRow row = ds.Tables[0].Rows[0];
                dateInvoiced = Util.GetValueOfDateTime(row["DateInvoiced"]);
                return Util.GetValueOfString(row["DocumentNo"]);
            }
            catch (Exception ex)
            {
                _log.Severe("GetInvoiceById (C_Invoice_ID=" + C_Invoice_ID + "): " + ex.Message);
                return "";
            }
        }

        /// <summary>
        /// Returns the document no of the most recent AP invoice raised against
        /// the receipt's purchase order, or an empty string when there is none.
        /// M_InOut has no invoice FK — C_Invoice carries C_Order_ID — so the
        /// invoice is reached through the parent PO. That link is order-scoped,
        /// not receipt-scoped: on a part-received PO the invoice returned may
        /// cover other receipts of the same order. Reversed and voided invoices
        /// are excluded. A DB issue degrades to "" so the overview still renders.
        /// </summary>
        /// Public wrapper over <see cref="GetLatestInvoice"/> so the controller can
        /// report the invoice document generated for an order.
        /// </summary>
        /// <param name="C_Order_ID">Parent purchase order id; 0 when unlinked.</param>
        public string GetLatestInvoiceDocNoForOrder(int C_Order_ID)
        {
            DateTime? ignored;
            return GetLatestInvoice(C_Order_ID, out ignored);
        }

        /// <summary>
        /// Reads the latest AP invoice's document no and invoice date in one go.
        /// </summary>
        /// <param name="C_Order_ID">Parent purchase order id; 0 when unlinked.</param>
        /// <param name="dateInvoiced">Receives the invoice date; null when none.</param>
        private string GetLatestInvoice(int C_Order_ID, out DateTime? dateInvoiced)
        {
            dateInvoiced = null;
            if (C_Order_ID <= 0) return "";

            try
            {
                string sql = @"SELECT inv.DocumentNo, inv.DateInvoiced
                                 FROM C_Invoice inv
                                WHERE inv.C_Order_ID = @C_Order_ID
                                  AND inv.IsActive   = 'Y'
                                  AND inv.IsSOTrx    = 'N'
                                  AND inv.DocStatus NOT IN ('RE', 'VO')
                                ORDER BY inv.DateInvoiced DESC, inv.C_Invoice_ID DESC";
                SqlParameter[] param = new SqlParameter[]
                {
                    new SqlParameter("@C_Order_ID", C_Order_ID)
                };

                DataSet ds = DB.ExecuteDataset(sql, param, null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
                    return "";

                DataRow row = ds.Tables[0].Rows[0];
                dateInvoiced = Util.GetValueOfDateTime(row["DateInvoiced"]);
                return Util.GetValueOfString(row["DocumentNo"]);
            }
            catch (Exception ex)
            {
                _log.Severe("GetLatestInvoice (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
                return "";
            }
        }

        /// <summary>
        /// Loads M_InOutLine rows for the goods receipt with product, locator and
        /// UOM metadata, the linked PO line ordered qty and a derived unit rate /
        /// line value. Child of an already authorized receipt, so no separate
        /// MRole filter is applied here.
        /// </summary>
        /// <param name="M_InOut_ID">Owning goods receipt id.</param>
        /// <param name="C_Invoice_ID">Referenced AP invoice id; 0 when none. Bound
        /// for the invoice-aware rate expression.</param>
        /// <param name="defaultPrecision">Currency precision fallback.</param>
        /// <param name="rateExpr">Unit-rate SQL expression (schema-aware).</param>
        /// <param name="hasQualityPlan">Whether the quality-plan column exists.</param>
        /// <returns>Ordered list of line rows (may be empty).</returns>
        private List<GRNLineData> LoadLines(
            int M_InOut_ID, int C_Invoice_ID, int defaultPrecision, string rateExpr, bool hasQualityPlan)
        {
            List<GRNLineData> lines = new List<GRNLineData>();

            string qcCase = hasQualityPlan
                ? "CASE WHEN l.VA010_QualityPlan_ID IS NOT NULL THEN 'Y' ELSE 'N' END"
                : "'N'";

            string sql = @"SELECT
                              l.M_InOutLine_ID,
                              l.Line,
                              l.Description     AS LineDescription,
                              l.M_Product_ID,
                              NVL(l.MovementQty, 0) AS ReceivedQty,
                              p.Value           AS ProductCode,
                              p.Name            AS ProductName,
                              loc.Value         AS LocatorCode,
                              COALESCE(loc.LocatorCombination, loc.Bin, loc.Value) AS LocatorName,
                              u.Name            AS UOMName,
                              NVL(u.StdPrecision, 0) AS UOMPrecision,
                              NVL(ol.QtyOrdered, 0)  AS OrderedQty,
                              asi.Description   AS AttributeSetInstance,
                              " + rateExpr + @"                       AS UnitRate,
                              NVL(l.MovementQty, 0) * " + rateExpr + @" AS LineValue,
                              " + qcCase + @"                          AS QualityApplicable
                           FROM M_InOutLine l
                           LEFT OUTER JOIN C_OrderLine ol ON (ol.C_OrderLine_ID = l.C_OrderLine_ID)
                           LEFT OUTER JOIN M_Product   p  ON (p.M_Product_ID    = l.M_Product_ID)
                           LEFT OUTER JOIN C_UOM       u  ON (u.C_UOM_ID         = l.C_UOM_ID)
                           LEFT OUTER JOIN M_Locator   loc ON (loc.M_Locator_ID  = l.M_Locator_ID)
                           LEFT OUTER JOIN M_AttributeSetInstance asi ON (asi.M_AttributeSetInstance_ID = l.M_AttributeSetInstance_ID
                                                                          AND l.M_AttributeSetInstance_ID > 0)
                           WHERE l.M_InOut_ID = @M_InOut_ID
                             AND l.IsActive   = 'Y'
                           ORDER BY l.Line";

            // @C_Invoice_ID only appears in the invoice-aware rate expression —
            // binding a parameter the statement never references is an error on
            // Oracle, so it is added only when the SQL actually contains it.
            List<SqlParameter> param = new List<SqlParameter>();
            param.Add(new SqlParameter("@M_InOut_ID", M_InOut_ID));
            if (sql.IndexOf("@C_Invoice_ID", StringComparison.OrdinalIgnoreCase) >= 0)
                param.Add(new SqlParameter("@C_Invoice_ID", C_Invoice_ID));

            DataSet ds = DB.ExecuteDataset(sql, param.ToArray(), null);
            if (ds == null || ds.Tables.Count == 0)
                return lines;

            foreach (DataRow r in ds.Tables[0].Rows)
            {
                GRNLineData ln = new GRNLineData();
                ln.M_InOutLine_ID     = Util.GetValueOfInt(r["M_InOutLine_ID"]);
                ln.Line               = Util.GetValueOfInt(r["Line"]);
                ln.Description        = Util.GetValueOfString(r["LineDescription"]);
                ln.M_Product_ID       = Util.GetValueOfInt(r["M_Product_ID"]);
                ln.ReceivedQty        = Util.GetValueOfDecimal(r["ReceivedQty"]);
                ln.ProductCode        = Util.GetValueOfString(r["ProductCode"]);
                ln.ProductName        = Util.GetValueOfString(r["ProductName"]);
                ln.LocatorCode        = Util.GetValueOfString(r["LocatorCode"]);
                ln.LocatorName        = Util.GetValueOfString(r["LocatorName"]);
                ln.UOMName            = Util.GetValueOfString(r["UOMName"]);
                ln.UOMPrecision       = Util.GetValueOfInt(r["UOMPrecision"]);
                ln.OrderedQty         = Util.GetValueOfDecimal(r["OrderedQty"]);
                ln.AttributeSetInstance = Util.GetValueOfString(r["AttributeSetInstance"]);
                ln.UnitRate           = Util.GetValueOfDecimal(r["UnitRate"]);
                ln.LineValue          = Util.GetValueOfDecimal(r["LineValue"]);
                ln.QualityApplicable  = Util.GetValueOfString(r["QualityApplicable"]) == "Y";

                lines.Add(ln);
            }
            return lines;
        }

        /// <summary>
        /// Loads the VA010 quality-inspection rows recorded against this receipt's
        /// confirmation lines — one row per product / test parameter, carrying the
        /// quantity to verify, the configured acceptable value, the inspected
        /// actual value and the QA/QC date.
        ///
        /// The whole VA010 schema is optional, so the table and its display columns
        /// are resolved through the dictionary first; anything missing yields an
        /// empty list and the panel simply omits the section.
        ///
        /// VA010_ActualValue is a VARCHAR reference holding the chosen
        /// VA010_TestPrmtrList_ID rather than free text. It is deliberately NOT
        /// joined in SQL — a non-numeric value would abort the query on a numeric
        /// cast — so the ids are parsed in managed code and resolved to names in
        /// one follow-up lookup.
        /// </summary>
        /// <param name="M_InOut_ID">Owning goods receipt id.</param>
        /// <returns>Ordered list of inspection rows (empty when VA010 is absent).</returns>
        private List<GRNQualityParamData> LoadQualityParams(int M_InOut_ID)
        {
            List<GRNQualityParamData> rows = new List<GRNQualityParamData>();

            if (!TableExists("VA010_ShipConfParameters")) return rows;

            // Display columns differ between VA010 revisions — probe for the one
            // this schema actually has, exactly as the QA Holds widget does.
            string paramNameCol = FindDisplayColumn("VA010_TestParameter",
                new string[] { "VA010_TestPrmtrName", "Name", "Description", "Value" });
            string valueNameCol = FindDisplayColumn("VA010_TestPrmtrList",
                new string[] { "VA010_ParameterValue", "Name", "Description", "Value" });

            string paramNameExpr = !string.IsNullOrEmpty(paramNameCol)
                ? "tp." + paramNameCol : "CAST(NULL AS VARCHAR(255))";
            string paramJoin = !string.IsNullOrEmpty(paramNameCol)
                ? @"LEFT OUTER JOIN VA010_TestParameter tp
                            ON (tp.VA010_TestParameter_ID = qp.VA010_TestParameter_ID
                                AND tp.IsActive = 'Y')"
                : "";

            string acceptExpr = !string.IsNullOrEmpty(valueNameCol)
                ? "acc." + valueNameCol : "CAST(NULL AS VARCHAR(255))";
            string acceptJoin = !string.IsNullOrEmpty(valueNameCol)
                ? @"LEFT OUTER JOIN VA010_TestPrmtrList acc
                            ON (acc.VA010_TestPrmtrList_ID = qp.VA010_TestPrmtrList_ID
                                AND acc.IsActive = 'Y')"
                : "";

            // Only sort by the parameter name when there actually is such a column.
            string orderBy = !string.IsNullOrEmpty(paramNameCol)
                ? "ORDER BY l.Line, " + paramNameExpr
                : "ORDER BY l.Line";

            try
            {
                // The QA row may name its own product; when it does not, the one on
                // the receipt line stands in (mirrors the QA Holds widget).
                string sql = @"SELECT
                                  l.Line              AS LineNo,
                                  COALESCE(NULLIF(qp.M_Product_ID, 0), l.M_Product_ID) AS M_Product_ID,
                                  COALESCE(qpp.Value, lp.Value) AS ProductCode,
                                  COALESCE(qpp.Name,  lp.Name)  AS ProductName,
                                  " + paramNameExpr + @"  AS ParameterName,
                                  NVL(qp.VA010_QuantityToVerify, 0) AS QuantityToVerify,
                                  qp.VA010_TestPrmtrList_ID AS AcceptableValueId,
                                  " + acceptExpr + @"     AS AcceptableValue,
                                  qp.VA010_ActualValue  AS ActualValueRaw,
                                  qp.VA010_QAQCDate     AS QAQCDate,
                                  qp.Remark             AS Remark
                               FROM VA010_ShipConfParameters qp
                               INNER JOIN M_InOutLineConfirm lc
                                       ON (lc.M_InOutLineConfirm_ID = qp.M_InOutLineConfirm_ID
                                           AND lc.IsActive = 'Y')
                               INNER JOIN M_InOutLine l
                                       ON (l.M_InOutLine_ID = lc.M_InOutLine_ID
                                           AND l.IsActive = 'Y')
                               LEFT OUTER JOIN M_Product qpp ON (qpp.M_Product_ID = qp.M_Product_ID)
                               LEFT OUTER JOIN M_Product lp  ON (lp.M_Product_ID  = l.M_Product_ID)
                               " + paramJoin + @"
                               " + acceptJoin + @"
                               WHERE l.M_InOut_ID = @M_InOut_ID
                                 AND qp.IsActive  = 'Y'
                               " + orderBy;

                SqlParameter[] param = new SqlParameter[]
                {
                    new SqlParameter("@M_InOut_ID", M_InOut_ID)
                };

                DataSet ds = DB.ExecuteDataset(sql, param, null);
                if (ds == null || ds.Tables.Count == 0) return rows;

                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    GRNQualityParamData q = new GRNQualityParamData();
                    q.LineNo            = Util.GetValueOfInt(r["LineNo"]);
                    q.M_Product_ID      = Util.GetValueOfInt(r["M_Product_ID"]);
                    q.ProductCode       = Util.GetValueOfString(r["ProductCode"]);
                    q.ProductName       = Util.GetValueOfString(r["ProductName"]);
                    q.ParameterName     = Util.GetValueOfString(r["ParameterName"]);
                    q.QuantityToVerify  = Util.GetValueOfDecimal(r["QuantityToVerify"]);
                    q.AcceptableValueId = Util.GetValueOfInt(r["AcceptableValueId"]);
                    q.AcceptableValue   = Util.GetValueOfString(r["AcceptableValue"]);
                    q.QAQCDate          = Util.GetValueOfDateTime(r["QAQCDate"]);
                    q.Remark            = Util.GetValueOfString(r["Remark"]);

                    // Reference into VA010_TestPrmtrList; anything unparseable is
                    // treated as "not yet inspected".
                    int actualId;
                    string raw = Util.GetValueOfString(r["ActualValueRaw"]);
                    q.ActualValueId = int.TryParse(raw, out actualId) && actualId > 0 ? actualId : 0;

                    rows.Add(q);
                }

                ResolveActualValueNames(rows, valueNameCol);
                foreach (GRNQualityParamData q in rows) q.StatusCode = DeriveQcStatus(q);
            }
            catch (Exception ex)
            {
                _log.Severe("LoadQualityParams (M_InOut_ID=" + M_InOut_ID + "): " + ex.Message);
                return new List<GRNQualityParamData>();
            }

            return rows;
        }

        /// <summary>
        /// Fills <see cref="GRNQualityParamData.ActualValue"/> for every row that
        /// carries a parsed value-list id, using a single lookup over the ids in
        /// play. A failure leaves the names blank — the rows still render.
        /// </summary>
        private void ResolveActualValueNames(List<GRNQualityParamData> rows, string valueNameCol)
        {
            if (rows.Count == 0 || string.IsNullOrEmpty(valueNameCol)) return;

            List<string> ids = new List<string>();
            foreach (GRNQualityParamData q in rows)
            {
                string id = q.ActualValueId.ToString();
                if (q.ActualValueId > 0 && !ids.Contains(id)) ids.Add(id);
            }
            if (ids.Count == 0) return;

            try
            {
                // The id list is built from parsed integers only, so it is safe to
                // inline — no user text reaches the statement.
                string sql = @"SELECT VA010_TestPrmtrList_ID AS ValueId,
                                      " + valueNameCol + @" AS ValueName
                                 FROM VA010_TestPrmtrList
                                WHERE IsActive = 'Y'
                                  AND VA010_TestPrmtrList_ID IN (" + string.Join(",", ids.ToArray()) + ")";

                DataSet ds = DB.ExecuteDataset(sql, null, null);
                if (ds == null || ds.Tables.Count == 0) return;

                Dictionary<int, string> names = new Dictionary<int, string>();
                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    names[Util.GetValueOfInt(r["ValueId"])] = Util.GetValueOfString(r["ValueName"]);
                }

                foreach (GRNQualityParamData q in rows)
                {
                    if (q.ActualValueId > 0 && names.ContainsKey(q.ActualValueId))
                        q.ActualValue = names[q.ActualValueId];
                }
            }
            catch (Exception ex)
            {
                _log.Severe("ResolveActualValueNames: " + ex.Message);
            }
        }

        /// <summary>
        /// Pass / fail / pending for one inspection row: "P" when the inspected
        /// value is the configured acceptable one, "F" when it is a different
        /// value, "N" while no actual value has been recorded yet.
        /// </summary>
        private string DeriveQcStatus(GRNQualityParamData q)
        {
            if (q.ActualValueId <= 0) return "N";
            return q.ActualValueId == q.AcceptableValueId ? "P" : "F";
        }

        // ----------------------------------------------------------------- //
        //  Posting date + audit trail                                        //
        // ----------------------------------------------------------------- //

        /// <summary>Single-parameter helper for the receipt-scoped queries.</summary>
        private SqlParameter[] InOutParam(int M_InOut_ID)
        {
            return new SqlParameter[] { new SqlParameter("@M_InOut_ID", M_InOut_ID) };
        }

        /// <summary>
        /// Returns the moment the receipt was actually posted — the earliest
        /// Created stamp across the Fact_Acct rows written for it — or null when it
        /// has never been posted.
        ///
        /// Deliberately a standalone query rather than a subselect in the main
        /// SELECT. That query is rewritten by MRole.AddAccessSQL with
        /// SQL_FULLYQUALIFIED, whose parser walks every FROM/JOIN it can read —
        /// including inside subselects — and appends private-access filters to the
        /// OUTER WHERE. Aliases living only inside a subselect are out of scope
        /// there, so the rewritten SQL dies with ORA-00904 and the whole overview
        /// returns no rows. Run separately (child of an already authorized
        /// receipt) it never reaches that rewriter.
        /// </summary>
        /// <param name="M_InOut_ID">Selected goods receipt id.</param>
        private DateTime? GetPostedDate(int M_InOut_ID)
        {
            try
            {
                string sql = @"SELECT MIN(fa.Created) AS PostedDate
                                 FROM Fact_Acct fa
                                INNER JOIN AD_Table adt ON (adt.AD_Table_ID = fa.AD_Table_ID)
                                WHERE fa.Record_ID  = @M_InOut_ID
                                  AND adt.TableName = 'M_InOut'";
                DataSet ds = DB.ExecuteDataset(sql, InOutParam(M_InOut_ID), null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
                    return null;
                return Util.GetValueOfDateTime(ds.Tables[0].Rows[0]["PostedDate"]);
            }
            catch (Exception ex)
            {
                // Non-fatal: the timeline falls back to the accounting date.
                _log.Severe("GetPostedDate (M_InOut_ID=" + M_InOut_ID + "): " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Builds the receipt's audit trail: who created it, who last changed it
        /// and when, when it was completed and by whom, when it was posted, plus
        /// the confirmations raised, invoices billed against it and any chat notes
        /// — merged newest-first and capped.
        ///
        /// Each source runs under its own guard so a DB-level problem with one
        /// degrades to a partial trail (logged) rather than breaking the overview.
        /// </summary>
        /// <param name="M_InOut_ID">Selected goods receipt id.</param>
        /// <param name="docStatus">The receipt's DocStatus, for the completion entry.</param>
        /// <returns>Activity rows ordered newest-first (may be empty).</returns>
        private List<GRNActivityData> LoadActivity(int M_InOut_ID, string docStatus)
        {
            const int MAX_ENTRIES = 15;

            List<GRNActivityData> activity = new List<GRNActivityData>();
            LoadReceiptMilestones(M_InOut_ID, docStatus, activity);
            LoadPostingActivity(M_InOut_ID, activity);
            LoadConfirmationActivity(M_InOut_ID, activity);
            LoadInvoiceActivity(M_InOut_ID, activity);
            LoadNoteActivity(M_InOut_ID, activity);

            // Newest first; entries with no timestamp sink to the bottom.
            activity.Sort(delegate (GRNActivityData a, GRNActivityData b)
            {
                return b.Created.GetValueOrDefault(DateTime.MinValue)
                        .CompareTo(a.Created.GetValueOrDefault(DateTime.MinValue));
            });

            if (activity.Count > MAX_ENTRIES)
                activity = activity.GetRange(0, MAX_ENTRIES);
            return activity;
        }

        /// <summary>
        /// The receipt's own milestones: "created" (Created / CreatedBy), "updated"
        /// (Updated / UpdatedBy, only when it differs from the create stamp) and,
        /// for a completed or closed receipt, "completed". The completion moment
        /// comes from the document's workflow DocComplete node when there is one,
        /// falling back to the last-updated stamp.
        /// </summary>
        private void LoadReceiptMilestones(int M_InOut_ID, string docStatus, List<GRNActivityData> list)
        {
            try
            {
                string sql = @"SELECT io.Created,
                                      io.Updated,
                                      cu.Name AS CreatedByName,
                                      uu.Name AS UpdatedByName
                                 FROM M_InOut io
                                 LEFT OUTER JOIN AD_User cu ON (io.CreatedBy = cu.AD_User_ID)
                                 LEFT OUTER JOIN AD_User uu ON (io.UpdatedBy = uu.AD_User_ID)
                                WHERE io.M_InOut_ID = @M_InOut_ID";
                DataSet ds = DB.ExecuteDataset(sql, InOutParam(M_InOut_ID), null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return;

                DataRow r = ds.Tables[0].Rows[0];
                DateTime? created = Util.GetValueOfDateTime(r["Created"]);
                DateTime? updated = Util.GetValueOfDateTime(r["Updated"]);
                string updatedBy  = Util.GetValueOfString(r["UpdatedByName"]);

                list.Add(new GRNActivityData
                {
                    Type     = "created",
                    UserName = Util.GetValueOfString(r["CreatedByName"]),
                    Created  = created
                });

                // A receipt saved once has Updated == Created; that is not an edit.
                if (updated.HasValue &&
                    (!created.HasValue || updated.Value > created.Value.AddSeconds(1)))
                {
                    list.Add(new GRNActivityData
                    {
                        Type     = "updated",
                        UserName = updatedBy,
                        Created  = updated
                    });
                }

                if (docStatus == "CO" || docStatus == "CL")
                {
                    DateTime? completedAt = GetCompletedDate(M_InOut_ID);
                    string completedBy    = _lastCompletedByName;

                    list.Add(new GRNActivityData
                    {
                        Type     = "completed",
                        UserName = !string.IsNullOrEmpty(completedBy) ? completedBy : updatedBy,
                        Created  = completedAt.HasValue ? completedAt : updated
                    });
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadReceiptMilestones (M_InOut_ID=" + M_InOut_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// Set by <see cref="GetCompletedDate"/> alongside its return value, so the
        /// caller gets both the moment and the actor from one query.
        /// </summary>
        private string _lastCompletedByName;

        /// <summary>
        /// Returns the moment the receipt was completed: the Created stamp of its
        /// workflow DocComplete activity, or null when it has no such node (a
        /// receipt completed outside the workflow engine). Standalone query for the
        /// same MRole reason documented on <see cref="GetPostedDate"/>.
        /// </summary>
        private DateTime? GetCompletedDate(int M_InOut_ID)
        {
            _lastCompletedByName = "";
            try
            {
                string sql = @"SELECT wfa.Created, u.Name AS UserName
                                 FROM AD_WF_Process wfp
                                INNER JOIN AD_WF_Activity wfa
                                        ON (wfa.AD_WF_Process_ID = wfp.AD_WF_Process_ID)
                                INNER JOIN AD_WF_Node wfn
                                        ON (wfn.AD_WF_Node_ID = wfa.AD_WF_Node_ID)
                                INNER JOIN AD_Table adt
                                        ON (adt.AD_Table_ID = wfp.AD_Table_ID)
                                 LEFT OUTER JOIN AD_User u ON (wfa.CreatedBy = u.AD_User_ID)
                                WHERE wfp.Record_ID = @M_InOut_ID
                                  AND adt.TableName = 'M_InOut'
                                  AND wfp.IsActive  = 'Y'
                                  AND wfa.IsActive  = 'Y'
                                  AND wfn.IsActive  = 'Y'
                                  AND wfa.WFState   = 'CC'
                                  AND UPPER(TRIM(wfn.Value)) IN ('DOCCOMPLETE', 'COMPLETE', '(DOCCOMPLETE)')
                                ORDER BY wfa.Created DESC";
                DataSet ds = DB.ExecuteDataset(sql, InOutParam(M_InOut_ID), null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
                    return null;

                DataRow r = ds.Tables[0].Rows[0];
                _lastCompletedByName = Util.GetValueOfString(r["UserName"]);
                return Util.GetValueOfDateTime(r["Created"]);
            }
            catch (Exception ex)
            {
                // Non-fatal: the caller falls back to the last-updated stamp.
                _log.Severe("GetCompletedDate (M_InOut_ID=" + M_InOut_ID + "): " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Adds a "posted" entry from the earliest Fact_Acct row written for the
        /// receipt, carrying the user who ran the posting.
        /// </summary>
        private void LoadPostingActivity(int M_InOut_ID, List<GRNActivityData> list)
        {
            try
            {
                string sql = @"SELECT fa.Created, u.Name AS UserName
                                 FROM Fact_Acct fa
                                INNER JOIN AD_Table adt ON (adt.AD_Table_ID = fa.AD_Table_ID)
                                 LEFT OUTER JOIN AD_User u ON (fa.CreatedBy = u.AD_User_ID)
                                WHERE fa.Record_ID  = @M_InOut_ID
                                  AND adt.TableName = 'M_InOut'
                                ORDER BY fa.Created";
                DataSet ds = DB.ExecuteDataset(sql, InOutParam(M_InOut_ID), null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return;

                DataRow r = ds.Tables[0].Rows[0];
                list.Add(new GRNActivityData
                {
                    Type     = "posted",
                    UserName = Util.GetValueOfString(r["UserName"]),
                    Created  = Util.GetValueOfDateTime(r["Created"])
                });
            }
            catch (Exception ex)
            {
                _log.Severe("LoadPostingActivity (M_InOut_ID=" + M_InOut_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// Adds one "confirmation" entry per receipt confirmation document raised
        /// against this receipt.
        /// </summary>
        private void LoadConfirmationActivity(int M_InOut_ID, List<GRNActivityData> list)
        {
            try
            {
                string sql = @"SELECT c.DocumentNo, c.Created, u.Name AS UserName
                                 FROM M_InOutConfirm c
                                 LEFT OUTER JOIN AD_User u ON (c.CreatedBy = u.AD_User_ID)
                                WHERE c.M_InOut_ID = @M_InOut_ID
                                  AND c.IsActive   = 'Y'";
                DataSet ds = DB.ExecuteDataset(sql, InOutParam(M_InOut_ID), null);
                if (ds == null || ds.Tables.Count == 0) return;

                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    list.Add(new GRNActivityData
                    {
                        Type       = "confirmation",
                        DocumentNo = Util.GetValueOfString(r["DocumentNo"]),
                        UserName   = Util.GetValueOfString(r["UserName"]),
                        Created    = Util.GetValueOfDateTime(r["Created"])
                    });
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadConfirmationActivity (M_InOut_ID=" + M_InOut_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// Adds one "invoice" entry per AP invoice billed against this receipt's
        /// lines (the same receipt-scoped link the Reference Invoice field uses).
        /// </summary>
        private void LoadInvoiceActivity(int M_InOut_ID, List<GRNActivityData> list)
        {
            try
            {
                string sql = @"SELECT inv.DocumentNo, inv.Created, u.Name AS UserName
                                 FROM C_Invoice inv
                                 LEFT OUTER JOIN AD_User u ON (inv.CreatedBy = u.AD_User_ID)
                                WHERE inv.IsActive   = 'Y'
                                  AND inv.IsSOTrx    = 'N'
                                  AND inv.DocStatus NOT IN ('RE', 'VO')
                                  AND EXISTS (SELECT 1
                                                FROM C_InvoiceLine il
                                               INNER JOIN M_InOutLine iol
                                                       ON (iol.M_InOutLine_ID = il.M_InOutLine_ID)
                                               WHERE il.C_Invoice_ID = inv.C_Invoice_ID
                                                 AND il.IsActive     = 'Y'
                                                 AND iol.M_InOut_ID  = @M_InOut_ID)";
                DataSet ds = DB.ExecuteDataset(sql, InOutParam(M_InOut_ID), null);
                if (ds == null || ds.Tables.Count == 0) return;

                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    list.Add(new GRNActivityData
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
                _log.Severe("LoadInvoiceActivity (M_InOut_ID=" + M_InOut_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// Adds free-text chat notes (CM_ChatEntry) logged against this receipt.
        /// </summary>
        private void LoadNoteActivity(int M_InOut_ID, List<GRNActivityData> list)
        {
            try
            {
                string sql = @"SELECT ce.CharacterData, ce.Created, u.Name AS UserName
                                 FROM CM_ChatEntry ce
                                INNER JOIN CM_Chat ch     ON (ce.CM_Chat_ID = ch.CM_Chat_ID)
                                 LEFT OUTER JOIN AD_User u ON (ce.AD_User_ID = u.AD_User_ID)
                                WHERE ch.AD_Table_ID =
                                      (SELECT t.AD_Table_ID FROM AD_Table t WHERE t.TableName = 'M_InOut')
                                  AND ch.Record_ID = @M_InOut_ID
                                  AND ce.IsActive  = 'Y'";
                DataSet ds = DB.ExecuteDataset(sql, InOutParam(M_InOut_ID), null);
                if (ds == null || ds.Tables.Count == 0) return;

                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    list.Add(new GRNActivityData
                    {
                        Type     = "note",
                        Text     = Util.GetValueOfString(r["CharacterData"]),
                        UserName = Util.GetValueOfString(r["UserName"]),
                        Created  = Util.GetValueOfDateTime(r["Created"])
                    });
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadNoteActivity (M_InOut_ID=" + M_InOut_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// Returns the first of the candidate columns that exists on the table, or
        /// an empty string when the table has none of them.
        /// </summary>
        private string FindDisplayColumn(string tableName, string[] candidates)
        {
            for (int i = 0; i < candidates.Length; i++)
            {
                if (ColumnExists(tableName, candidates[i])) return candidates[i];
            }
            return "";
        }

        /// <summary>
        /// Returns true when the given table exists in the AD_Table dictionary. A
        /// DB issue degrades to "absent" (false), which simply hides the optional
        /// section that depends on it.
        /// </summary>
        private bool TableExists(string tableName)
        {
            try
            {
                string sql = @"SELECT COUNT(*) FROM AD_Table
                                WHERE UPPER(TableName) = UPPER(@TableName)";
                SqlParameter[] param = new SqlParameter[]
                {
                    new SqlParameter("@TableName", tableName)
                };
                return Util.GetValueOfInt(DB.ExecuteScalar(sql, param, null)) > 0;
            }
            catch (Exception ex)
            {
                _log.Severe("TableExists (" + tableName + "): " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Returns true when the given column exists on the given table, using
        /// the AD_Column dictionary. A DB issue degrades to "absent" (false) so a
        /// lookup failure never breaks the overview query.
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

        // ----------------------------------------------------------------- //
        //  Data carriers                                                     //
        // ----------------------------------------------------------------- //

        public class GRNLineData
        {
            public int      M_InOutLine_ID   { get; set; }
            public int      Line             { get; set; }
            public string   Description      { get; set; }
            public int      M_Product_ID     { get; set; }
            public string   ProductCode      { get; set; }   // SKU
            public string   ProductName      { get; set; }
            public string   LocatorCode      { get; set; }
            public string   LocatorName      { get; set; }
            public string   UOMName          { get; set; }
            public int      UOMPrecision     { get; set; }
            public string   AttributeSetInstance { get; set; }   // M_AttributeSetInstance.Description (blank when none)
            public decimal  OrderedQty       { get; set; }
            public decimal  ReceivedQty      { get; set; }
            public decimal  UnitRate         { get; set; }
            public decimal  LineValue        { get; set; }
            public bool     QualityApplicable { get; set; }
        }

        /// <summary>
        /// One VA010 quality-inspection row: a single test parameter checked
        /// against one product on the receipt.
        /// </summary>
        public class GRNQualityParamData
        {
            public int      LineNo            { get; set; }   // owning M_InOutLine.Line
            public int      M_Product_ID      { get; set; }
            public string   ProductCode       { get; set; }
            public string   ProductName       { get; set; }
            public string   ParameterName     { get; set; }   // Colour / Size / Grade ...
            public decimal  QuantityToVerify  { get; set; }
            public int      AcceptableValueId { get; set; }   // configured VA010_TestPrmtrList_ID
            public string   AcceptableValue   { get; set; }
            public int      ActualValueId     { get; set; }   // inspected value; 0 = not inspected
            public string   ActualValue       { get; set; }
            public DateTime? QAQCDate         { get; set; }
            public string   Remark            { get; set; }
            public string   StatusCode        { get; set; }   // P = passed, F = failed, N = pending
        }

        /// <summary>
        /// One audit-trail entry. <see cref="Type"/> drives the tag + icon the
        /// client renders: created | updated | completed | posted | confirmation |
        /// invoice | note.
        /// </summary>
        public class GRNActivityData
        {
            public string    Type       { get; set; }
            public string    UserName   { get; set; }   // actor
            public string    Text       { get; set; }   // free text (notes only)
            public string    DocumentNo { get; set; }   // related document, when any
            public DateTime? Created    { get; set; }
        }

        public class GRNOverviewData
        {
            // Header / identity
            public int       M_InOut_ID       { get; set; }
            public string    DocumentNo       { get; set; }
            public string    StatusCode       { get; set; }   // DocStatus code
            public bool      Processed        { get; set; }
            public bool      Posted           { get; set; }
            public bool      IsDropShip       { get; set; }   // M_InOut.IsDropShip (drop shipment)
            public DateTime? MovementDate     { get; set; }   // received date
            public DateTime? PostingDate      { get; set; }   // DateAcct (accounting date)
            public DateTime? PostedDate       { get; set; }   // when posting actually ran (Fact_Acct)
            public string    PriorityCode     { get; set; }   // PriorityRule code
            public string    Description      { get; set; }   // GRN header description -> Notes section
            public DateTime? Created          { get; set; }   // drafted-on
            public DateTime? Updated          { get; set; }   // last document change

            // References
            public string    ReferenceInvoice     { get; set; }   // linked AP invoice DocumentNo
            public DateTime? ReferenceInvoiceDate { get; set; }   // its DateInvoiced

            // Linked purchase order
            public int       C_Order_ID       { get; set; }   // raw M_InOut.C_Order_ID
            public int       C_Invoice_ID     { get; set; }   // AP invoice the receipt was raised against
            public int       PurchaseOrderId  { get; set; }   // the order that is actually the PO
            public string    PONo             { get; set; }
            public DateTime? PODate           { get; set; }
            public DateTime? ExpectedDate     { get; set; }
            public string    POReference      { get; set; }   // C_Order.POReference (vendor's ref)
            public string    RefOrderDocNo    { get; set; }   // originating sales order

            // Vendor
            public string    VendorName         { get; set; }
            public string    VendorTaxID        { get; set; }   // GSTIN / Tax ID
            public string    VendorLocationName { get; set; }
            public string    VendorAddress      { get; set; }
            public string    ContactName        { get; set; }
            public string    ContactPhone       { get; set; }
            public string    ContactEmail       { get; set; }

            // Receipt
            public string    WarehouseName    { get; set; }
            public string    ReceivedBy       { get; set; }

            // Currency
            public string    CurSymbol        { get; set; }
            public string    ISO_Code         { get; set; }
            public int       StdPrecision     { get; set; }

            // Document type / confirmation
            public string    DocTypeName           { get; set; }
            public bool      IsConfirmationDocType { get; set; }   // "MM Receipt with Confirmation"

            // KPI aggregates
            public int       LineCount        { get; set; }
            public decimal   ReceivedQty      { get; set; }
            public int       QcLineCount      { get; set; }
            public decimal   ReceivedValue    { get; set; }
            public decimal   AcceptedQty      { get; set; }   // passed QC (ConfirmedQty)
            public decimal   RejectedQty      { get; set; }   // failed QC (ScrappedQty)

            // Collections
            public List<GRNLineData> Lines    { get; set; }
            public List<GRNQualityParamData> QualityParams { get; set; }
            public List<GRNActivityData>     Activity      { get; set; }
        }
    }
}
