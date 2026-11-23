/******************************************************
 * Module Name    : VAS
 * Purpose        : Sales Order dashboard "SO Pending for Payment" collection worklist widget endpoints
 * chronological  : Development
 * Created Date   : 2026-09-15
 * Created by     : VAI052
 ******************************************************/

using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Web.Mvc;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VAS.Controllers
{
    /// <summary>
    /// Module Name : VAS_286_SOPendingForPaymentWidget
    /// Purpose     : Data endpoints for the 6x2 "SO Pending for Payment" wide worklist
    ///               widget on the Sales Order dashboard - Sales Orders with a real
    ///               outstanding payment obligation, plus the shared Sales Order
    ///               record-preview data for the record modal opened directly from a
    ///               row click.
    ///
    ///   Business definition per
    ///   19_SO_Pending_For_Payment_Claude_Development_Prompt.txt (CRITICAL USER
    ///   OVERRIDE - the paired HTML mock's "Delivered against SO, payment not yet
    ///   received" framing is NOT the production rule; no shipment/delivery
    ///   condition is required for either path below, so an advance-payment SO can
    ///   appear before any M_InOut exists):
    ///     A. INVOICED OUTSTANDING - one or more completed/closed ('CO'/'CL') sales
    ///        invoices linked to the order have C_Invoice.VA009_OpenAmount > 0. Amount
    ///        = SUM(VA009_OpenAmount) across those open invoices; due date = the
    ///        earliest DueDate of their UNPAID payment schedules
    ///        (C_InvoicePaySchedule, VA009_IsPaid = 'N' - where the due date really
    ///        lives; C_Invoice.DueDate is usually empty, which left the "Payment due"
    ///        column blank, Sr 8.34), falling back to C_Invoice.DueDate and then the
    ///        invoice date when an invoice has no schedule.
    ///     B. ADVANCE OUTSTANDING - only when path A does not apply (an order with a
    ///        qualifying open invoice never also gets an Advance row - one SO appears
    ///        once only): the order is Completed/Closed, has no completed sales invoice
    ///        yet, its payment term is an advance term (C_PaymentTerm.VA009_Advance =
    ///        'Y' OR any active C_PaySchedule row of that term has VA009_Advance = 'Y',
    ///        5-Nov row 31) and C_Order.GrandTotal minus completed ('CO'/'CL') customer receipts
    ///        (C_Payment.IsReceipt='Y') linked to that order is > 0. Amount = that
    ///        difference (GrandTotal, never TotalLines, because payment can include
    ///        tax/freight). Due date = C_Order.DateOrdered - the schema exposes no
    ///        separate advance-due date, so advance is treated as due from booking.
    ///     - Cohort excludes drafted/voided/reversed/in-process/not-active-related
    ///       document statuses (DocStatus NOT IN ('DR','VO','RE','IN','NA')) and, per
    ///       the general rules, non-quotation/non-return real Sales Orders only.
    ///     - Sorted payment-due-date ascending (oldest/most overdue first); "days
    ///       overdue" is a calendar-day difference computed here in C#, never in SQL
    ///       (cross-database rule - Oracle/PostgreSQL date arithmetic differs).
    ///     - The header "total due" chip sums the amount across the COMPLETE result
    ///       (a window aggregate computed before OFFSET/FETCH trims to the page), so
    ///       it always reconciles with the visible column.
    ///
    ///   Per the CTE/MRole rule (Prompt_Instructions.txt "Case 1"): the query isolates
    ///   a single "FROM C_Order o" WHERE-only fragment (no GROUP BY) and calls
    ///   AddAccessSQL ONLY on that isolated fragment - never on a fragment that already
    ///   carries its own GROUP BY. The invoice_due/received aggregates are separate
    ///   CTEs joined back to the already-filtered base_orders fragment, so access
    ///   control is inherited through that join rather than re-applied to them.
    ///   Read-only here; no writes.
    /// Chronological development:
    ///   VAI052      2026-09-15 Created
    ///   VAS163      2026-11-20 Sr 234/243 Amount without tax in the order currency (invoice /
    ///                          receipt currencies converted), total-due chip in the base currency;
    ///                          Sr 237-242 SO lines: charge name, item-only qty/status, line UOM.
    /// </summary>
    [System.Web.Mvc.SessionState(System.Web.SessionState.SessionStateBehavior.ReadOnly)]
    public class VAS_286_SOPendingForPaymentWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_286_SOPendingForPaymentWidgetController).FullName);

        private const int DefaultPageSize = 6;
        private const int MaxPageSize = 10;
        private const int MaxLineRows = 200;

        /// <summary>
        /// One page of Sales Orders with a real outstanding payment obligation
        /// (invoiced or advance basis), plus the total due across the complete result.
        /// </summary>
        /// <param name="page">Zero-based page index.</param>
        /// <param name="size">Rows per page (capped at <see cref="MaxPageSize"/>).</param>
        /// <param name="sortBy">"date" (SO date) or "due" (payment due, the default).</param>
        /// <param name="sortDir">"asc" (default) or "desc".</param>
        /// <returns>JSON { TotalDue, Total, Rows:[...] } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetPendingPayment(int page = 0, int size = DefaultPageSize, string sortBy = "due", string sortDir = "asc")
        {
            if (Session["ctx"] == null) { return Json("", JsonRequestBehavior.AllowGet); }

            Ctx ctx = Session["ctx"] as Ctx;
            if (page < 0) { page = 0; }
            if (size <= 0 || size > MaxPageSize) { size = DefaultPageSize; }

            try
            {
                PendingPaymentResult data = GetPendingPaymentData(ctx, page, size, sortBy, sortDir);
                FillBaseCurrency(ctx, data);
                string json = JsonConvert.SerializeObject(data);
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_286_SOPendingForPaymentWidget.GetPendingPayment", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// Full data for the shared Sales Order record-preview modal (record view and
        /// lines view both render from this one payload): header stats and every active
        /// line (capped at <see cref="MaxLineRows"/> - the client paginates client-side,
        /// sized to the space actually available, so the modal body never scrolls).
        /// </summary>
        /// <param name="id">C_Order_ID.</param>
        /// <returns>JSON { Order:{...}, Lines:[...] } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetSalesOrderDetail(int id)
        {
            if (Session["ctx"] == null) { return Json("", JsonRequestBehavior.AllowGet); }

            Ctx ctx = Session["ctx"] as Ctx;

            try
            {
                string json = JsonConvert.SerializeObject(GetSalesOrderDetailData(ctx, id));
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_286_SOPendingForPaymentWidget.GetSalesOrderDetail", ex);
                return ErrorResult(ctx);
            }
        }

        // VAS163 2026-11-20: Sr 243 / 254 tax-inclusive is read from the order's own totals (same rule
        // as VAS_271/273/274: the flags are not reliable) - the tax is inside TotalLines when GrandTotal
        // is not raised by it (GrandTotal - TotalLines below half the C_OrderTax total).
        private const string TaxIncludedSql =
            "(CASE WHEN 2 * (COALESCE(o.GrandTotal, 0) - COALESCE(o.TotalLines, 0)) < " +
            "(SELECT COALESCE(SUM(COALESCE(ott.TaxAmt, 0)), 0) FROM C_OrderTax ott WHERE ott.C_Order_ID = o.C_Order_ID AND ott.IsActive = 'Y') " +
            "THEN 'Y' ELSE 'N' END)";

        // VAS163 2026-11-20: Sr 243 order value without tax (TotalLines, less the C_OrderTax total on a
        // tax-inclusive order) - as VAS_271 / VAS_287.
        private const string OrderValueExclTaxSql =
            "CASE WHEN " + TaxIncludedSql + " = 'Y' " +
            "THEN COALESCE(o.TotalLines, 0) - (SELECT COALESCE(SUM(COALESCE(ot.TaxAmt, 0)), 0) FROM C_OrderTax ot WHERE ot.C_Order_ID = o.C_Order_ID AND ot.IsActive = 'Y') " +
            "ELSE COALESCE(o.TotalLines, 0) END";

        // VAS163 2026-11-20: Sr 243 share of an invoice's grand total that is not tax (alias "i" =
        // C_Invoice), same totals-based tax-inclusive rule on C_InvoiceTax; 1 when GrandTotal is 0.
        private const string InvoiceExTaxRatioSql =
            "(CASE WHEN COALESCE(i.GrandTotal, 0) = 0 THEN 1 ELSE " +
            "(CASE WHEN 2 * (COALESCE(i.GrandTotal, 0) - COALESCE(i.TotalLines, 0)) < " +
            "(SELECT COALESCE(SUM(COALESCE(itt.TaxAmt, 0)), 0) FROM C_InvoiceTax itt WHERE itt.C_Invoice_ID = i.C_Invoice_ID AND itt.IsActive = 'Y') " +
            "THEN COALESCE(i.TotalLines, 0) - (SELECT COALESCE(SUM(COALESCE(it.TaxAmt, 0)), 0) FROM C_InvoiceTax it WHERE it.C_Invoice_ID = i.C_Invoice_ID AND it.IsActive = 'Y') " +
            "ELSE COALESCE(i.TotalLines, 0) END) / i.GrandTotal END)";

        /// <summary>
        /// The single physical-table fragment - one "FROM C_Order o", WHERE-only, no
        /// GROUP BY (Prompt_Instructions.txt "Case 1"). The ONLY thing AddAccessSQL is
        /// ever applied to for this widget.
        /// </summary>
        private static string BuildBaseOrdersSql()
        {
            return @"
                SELECT o.C_Order_ID AS C_Order_ID,
                       o.DocumentNo AS Document_No,
                       o.DateOrdered AS Date_Ordered,
                       o.C_BPartner_ID AS BPartner_Id,
                       o.M_Warehouse_ID AS Warehouse_Id,
                       o.C_PaymentTerm_ID AS Payment_Term_Id,
                       o.GrandTotal AS Grand_Total,
                       o.C_Currency_ID AS Currency_Id,
                       o.DocStatus AS Doc_Status_Code,
                       " + OrderValueExclTaxSql + @" AS Order_Ex_Tax,
                       COALESCE(o.DateAcct, o.DateOrdered) AS Date_Acct,
                       o.C_ConversionType_ID AS Conversion_Type_Id,
                       o.AD_Client_ID AS Client_Id,
                       o.AD_Org_ID AS Org_Id
                  FROM C_Order o
                 WHERE o.AD_Client_ID = @AD_Client_ID
                   AND o.IsActive = 'Y'
                   AND o.IsSOTrx = 'Y'
                   AND EXISTS (SELECT 1 FROM C_DocType dt WHERE dt.C_DocType_ID = o.C_DocTypeTarget_ID AND dt.DocSubTypeSO IN ('SO', 'WR', 'BO'))
                   AND COALESCE(o.IsReturnTrx, 'N') = 'N'
                   AND o.DocStatus NOT IN ('DR', 'VO', 'RE', 'IN', 'NA')";
        }

        private PendingPaymentResult GetPendingPaymentData(Ctx ctx, int page, int size, string sortBy, string sortDir)
        {
            PendingPaymentResult result = new PendingPaymentResult { Rows = new List<PendingPaymentRow>() };
            if (ctx == null) { return result; }

            string baseOrdersSql = MRole.GetDefault(ctx).AddAccessSQL(BuildBaseOrdersSql(), "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            // 5-Nov row 32: column-heading sort, applied before OFFSET/FETCH so it holds
            // across every page. Whitelisted - the client value never reaches the SQL text.
            string dir = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase) ? "DESC" : "ASC";
            string dueExpr = "CASE WHEN c.Invoice_Order_Id IS NOT NULL THEN c.Invoice_Due_Date ELSE c.Date_Ordered END";
            string orderBy;
            if (string.Equals(sortBy, "date", StringComparison.OrdinalIgnoreCase))
            {
                orderBy = "c.Date_Ordered " + dir + ", c.Document_No " + dir;
            }
            else
            {
                orderBy = dueExpr + " " + dir + ", c.Document_No " + dir;
            }

            // VAS163 2026-11-20: Sr 234 / 243 the Amount column is the outstanding amount WITHOUT tax in
            // the order's (price list) currency: an invoice's open amount is scaled by that invoice's
            // ex-tax share and converted from the invoice currency into the order currency; customer
            // receipts are converted from the payment currency; the advance outstanding is scaled by the
            // order's ex-tax share. The header chip (Total_Due) sums each row converted to the base
            // (accounting-schema) currency - it used to add up amounts of different currencies.
            // Currency ids are ints read from the DB, inlined like VAS_271.
            int baseCurrencyId = GetBaseCurrencyId(ctx);
            string invoiceOpenExTaxSql = "ROUND(COALESCE(i.VA009_OpenAmount, 0) * " + InvoiceExTaxRatioSql + ", 6)";
            string invoiceOpenDocSql = "CASE WHEN i.C_Currency_ID = bo.Currency_Id THEN " + invoiceOpenExTaxSql +
                " ELSE COALESCE(currencyConvert(" + invoiceOpenExTaxSql + ", i.C_Currency_ID, bo.Currency_Id, COALESCE(i.DateAcct, i.DateInvoiced), i.C_ConversionType_ID, i.AD_Client_ID, i.AD_Org_ID), 0) END";
            string receivedDocSql = "CASE WHEN p.C_Currency_ID = bo.Currency_Id THEN COALESCE(p.PayAmt, 0)" +
                " ELSE COALESCE(currencyConvert(COALESCE(p.PayAmt, 0), p.C_Currency_ID, bo.Currency_Id, COALESCE(p.DateAcct, p.DateTrx), p.C_ConversionType_ID, p.AD_Client_ID, p.AD_Org_ID), 0) END";
            string totalDueSql = baseCurrencyId > 0
                ? "SUM(CASE WHEN c.Currency_Id = " + baseCurrencyId + " THEN c.Amount " +
                  "ELSE COALESCE(currencyConvert(c.Amount, c.Currency_Id, " + baseCurrencyId + ", c.Date_Acct, c.Conversion_Type_Id, c.Client_Id, c.Org_Id), 0) END) OVER ()"
                : "SUM(c.Amount) OVER ()";

            string sql = @"
                WITH base_orders AS (" + baseOrdersSql + @"),
                invoice_open AS (
                    -- VAS163 2026-11-20: Sr 234 / 243 per open invoice (scalar sub-selects kept out of the aggregate).
                    SELECT
                        i.C_Order_ID AS C_Order_ID,
                        COALESCE(i.DueDate, i.DateInvoiced) AS Due_Date,
                        COALESCE(i.VA009_OpenAmount, 0) AS Open_Amount,
                        " + invoiceOpenDocSql + @" AS Open_Amount_Doc
                      FROM C_Invoice i
                      INNER JOIN base_orders bo ON ( bo.C_Order_ID = i.C_Order_ID )
                     WHERE i.IsActive = 'Y'
                       AND i.IsSOTrx = 'Y'
                       AND COALESCE(i.IsReturnTrx, 'N') = 'N'
                       AND i.DocStatus IN ('CO', 'CL')
                       AND COALESCE(i.VA009_OpenAmount, 0) > 0
                ),
                invoice_due AS (
                    SELECT
                        io.C_Order_ID AS C_Order_ID,
                        MIN(io.Due_Date) AS Payment_Due_Date,
                        SUM(io.Open_Amount) AS Open_Amount,
                        ROUND(SUM(io.Open_Amount_Doc), 6) AS Open_Amount_Doc
                      FROM invoice_open io
                     GROUP BY io.C_Order_ID
                ),
                schedule_due AS (
                    SELECT
                        i.C_Order_ID AS C_Order_ID,
                        MIN(ips.DueDate) AS Schedule_Due_Date
                      FROM C_Invoice i
                      INNER JOIN base_orders bo ON ( bo.C_Order_ID = i.C_Order_ID )
                      INNER JOIN C_InvoicePaySchedule ips ON ( ips.C_Invoice_ID = i.C_Invoice_ID AND ips.IsActive = 'Y' AND ips.VA009_IsPaid = 'N' )
                     WHERE i.IsActive = 'Y'
                       AND i.IsSOTrx = 'Y'
                       AND COALESCE(i.IsReturnTrx, 'N') = 'N'
                       AND i.DocStatus IN ('CO', 'CL')
                       AND COALESCE(i.VA009_OpenAmount, 0) > 0
                     GROUP BY i.C_Order_ID
                ),
                received AS (
                    SELECT
                        p.C_Order_ID AS C_Order_ID,
                        ROUND(SUM(" + receivedDocSql + @"), 6) AS Received_Amount
                      FROM C_Payment p
                      INNER JOIN base_orders bo ON ( bo.C_Order_ID = p.C_Order_ID )
                     WHERE p.IsActive = 'Y'
                       AND p.DocStatus IN ('CO', 'CL')
                       AND p.IsReceipt = 'Y'
                       AND p.IsSOTrx = 'Y'
                     GROUP BY p.C_Order_ID
                ),
                candidate AS (
                    SELECT
                        bo.C_Order_ID AS C_Order_ID,
                        bo.Document_No AS Document_No,
                        bo.Date_Ordered AS Date_Ordered,
                        bo.BPartner_Id AS BPartner_Id,
                        bo.Warehouse_Id AS Warehouse_Id,
                        bo.Grand_Total AS Grand_Total,
                        bo.Currency_Id AS Currency_Id,
                        bo.Doc_Status_Code AS Doc_Status_Code,
                        bo.Order_Ex_Tax AS Order_Ex_Tax,
                        bo.Date_Acct AS Date_Acct,
                        bo.Conversion_Type_Id AS Conversion_Type_Id,
                        bo.Client_Id AS Client_Id,
                        bo.Org_Id AS Org_Id,
                        inv.Open_Amount_Doc AS Invoice_Open_Amount_Doc,
                        -- 5-Nov row 31: advance term = VA009_Advance on the payment term
                        -- itself OR on any of its active payment schedule rows.
                        CASE WHEN COALESCE(pt.VA009_Advance, 'N') = 'Y'
                               OR EXISTS (SELECT 1 FROM C_PaySchedule ps
                                           WHERE ps.C_PaymentTerm_ID = bo.Payment_Term_Id
                                             AND ps.IsActive = 'Y'
                                             AND ps.VA009_Advance = 'Y')
                             THEN 'Y' ELSE 'N'
                        END AS Advance_Flag,
                        CASE WHEN EXISTS (SELECT 1 FROM C_Invoice ci
                                           WHERE ci.C_Order_ID = bo.C_Order_ID
                                             AND ci.IsActive = 'Y'
                                             AND ci.IsSOTrx = 'Y'
                                             AND COALESCE(ci.IsReturnTrx, 'N') = 'N'
                                             AND ci.DocStatus IN ('CO', 'CL'))
                             THEN 'Y' ELSE 'N'
                        END AS Has_Invoice,
                        inv.C_Order_ID AS Invoice_Order_Id,
                        COALESCE(sd.Schedule_Due_Date, inv.Payment_Due_Date) AS Invoice_Due_Date,
                        inv.Open_Amount AS Invoice_Open_Amount,
                        COALESCE(r.Received_Amount, 0) AS Received_Amount
                      FROM base_orders bo
                      LEFT OUTER JOIN C_PaymentTerm pt ON ( pt.C_PaymentTerm_ID = bo.Payment_Term_Id )
                      LEFT OUTER JOIN invoice_due inv ON ( inv.C_Order_ID = bo.C_Order_ID )
                      LEFT OUTER JOIN schedule_due sd ON ( sd.C_Order_ID = bo.C_Order_ID )
                      LEFT OUTER JOIN received r ON ( r.C_Order_ID = bo.C_Order_ID )
                ),
                due_rows AS (
                    -- VAS163 2026-11-20: Sr 234 / 243 qualification unchanged (gross amounts); Amount is the
                    -- outstanding WITHOUT tax in the order currency (advance: the order's ex-tax share).
                    SELECT
                        cd.C_Order_ID AS C_Order_ID,
                        cd.Document_No AS Document_No,
                        cd.Date_Ordered AS Date_Ordered,
                        cd.BPartner_Id AS BPartner_Id,
                        cd.Warehouse_Id AS Warehouse_Id,
                        cd.Currency_Id AS Currency_Id,
                        cd.Date_Acct AS Date_Acct,
                        cd.Conversion_Type_Id AS Conversion_Type_Id,
                        cd.Client_Id AS Client_Id,
                        cd.Org_Id AS Org_Id,
                        cd.Invoice_Order_Id AS Invoice_Order_Id,
                        cd.Invoice_Due_Date AS Invoice_Due_Date,
                        CASE WHEN cd.Invoice_Order_Id IS NOT NULL THEN COALESCE(cd.Invoice_Open_Amount_Doc, 0)
                             WHEN COALESCE(cd.Grand_Total, 0) = 0 THEN (cd.Grand_Total - cd.Received_Amount)
                             ELSE ROUND((cd.Grand_Total - cd.Received_Amount) * cd.Order_Ex_Tax / cd.Grand_Total, 6)
                        END AS Amount
                      FROM candidate cd
                     WHERE cd.Invoice_Open_Amount > 0
                        OR (
                            cd.Invoice_Order_Id IS NULL
                            AND cd.Advance_Flag = 'Y'
                            AND cd.Doc_Status_Code IN ('CO', 'CL')
                            AND cd.Has_Invoice = 'N'
                            AND (cd.Grand_Total - cd.Received_Amount) > 0
                        )
                )
                SELECT
                    c.C_Order_ID AS Order_Id,
                    c.Document_No AS Document_No,
                    c.Date_Ordered AS Date_Ordered,
                    COALESCE(bp.Name, N'') AS Customer_Name,
                    COALESCE(w.Name, N'') AS Warehouse_Name,
                    CASE WHEN c.Invoice_Order_Id IS NOT NULL THEN 'Invoice' ELSE 'Advance' END AS Payment_Type,
                    CASE WHEN c.Invoice_Order_Id IS NOT NULL THEN c.Invoice_Due_Date ELSE c.Date_Ordered END AS Payment_Due_Date,
                    c.Amount AS Amount,
                    cur.CurSymbol AS Currency_Symbol,
                    cur.ISO_Code AS Currency_Code,
                    cur.StdPrecision AS Currency_Precision,
                    COUNT(1) OVER () AS Total_Rows,
                    ROUND(" + totalDueSql + @", 6) AS Total_Due
                  FROM due_rows c
                  INNER JOIN C_BPartner bp ON ( bp.C_BPartner_ID = c.BPartner_Id )
                  LEFT OUTER JOIN M_Warehouse w ON ( w.M_Warehouse_ID = c.Warehouse_Id )
                  LEFT OUTER JOIN C_Currency cur ON ( cur.C_Currency_ID = c.Currency_Id )
                 ORDER BY " + orderBy + @"
                 OFFSET @Row_Offset ROWS FETCH NEXT @Page_Size ROWS ONLY";

            DateTime today = DateTime.Today;

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, new[]
                {
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                    new SqlParameter("@Row_Offset", page * size),
                    new SqlParameter("@Page_Size", size)
                });

                int total = 0;
                decimal totalDue = 0m;
                while (dr != null && dr.Read())
                {
                    total = dr["Total_Rows"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Total_Rows"]);
                    totalDue = dr["Total_Due"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Total_Due"]);

                    DateTime? dateOrdered = dr["Date_Ordered"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr["Date_Ordered"]);
                    DateTime? paymentDueDate = dr["Payment_Due_Date"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr["Payment_Due_Date"]);

                    bool isOverdue = paymentDueDate.HasValue && paymentDueDate.Value.Date < today;
                    int daysOverdue = isOverdue ? (today - paymentDueDate.Value.Date).Days : 0;
                    // VAS163 2026-11-11: Row 93 - amount shown with the order's (price list) currency.
                    string rowCurIso = (Util.GetValueOfString(dr["Currency_Code"]) ?? "").Trim();
                    string rowCurSymbol = (Util.GetValueOfString(dr["Currency_Symbol"]) ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(rowCurSymbol)) { rowCurSymbol = rowCurIso; }

                    result.Rows.Add(new PendingPaymentRow
                    {
                        SalesOrderId = Util.GetValueOfInt(dr["Order_Id"]),
                        SalesOrderNumber = Util.GetValueOfString(dr["Document_No"]),
                        SalesOrderDate = dateOrdered.HasValue ? dateOrdered.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                        CustomerName = Util.GetValueOfString(dr["Customer_Name"]),
                        WarehouseName = Util.GetValueOfString(dr["Warehouse_Name"]),
                        PaymentType = Util.GetValueOfString(dr["Payment_Type"]),
                        PaymentDueDate = paymentDueDate.HasValue ? paymentDueDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                        IsOverdue = isOverdue,
                        DaysOverdue = daysOverdue,
                        Amount = dr["Amount"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Amount"]),
                        CurrencySymbol = rowCurSymbol,
                        CurrencyIso = rowCurIso,
                        CurrencyPrecision = dr["Currency_Precision"] == DBNull.Value ? 2 : Util.GetValueOfInt(dr["Currency_Precision"])
                    });
                }
                result.Total = total;
                result.TotalDue = totalDue;
            }
            finally
            {
                CloseReader(dr);
            }

            return result;
        }

        private OrderDetailResult GetSalesOrderDetailData(Ctx ctx, int orderId)
        {
            OrderDetailResult result = new OrderDetailResult { Lines = new List<OrderLineRow>() };
            if (ctx == null || orderId <= 0) { return result; }

            string language = GetLanguage(ctx);
            Dictionary<string, string> docStatusMap = GetDecodeMap("C_Order", "DocStatus", language);

            string headerSql = @"
                SELECT o.C_Order_ID AS Order_Id,
                       o.DocumentNo AS Document_No,
                       o.DateOrdered AS Date_Ordered,
                       o.DatePromised AS Date_Promised,
                       o.DocStatus AS Doc_Status_Code,
                       " + OrderValueExclTaxSql + @" AS Order_Value,
                       " + TaxIncludedSql + @" AS Tax_Included,
                       o.DeliveryViaRule AS Delivery_Via_Rule,
                       o.M_Warehouse_ID AS Warehouse_Id,
                       COALESCE(bp.Name, N'') AS Customer_Name,
                       COALESCE(w.Name, N'') AS Warehouse_Name,
                       (SELECT hc.ISO_Code FROM C_Currency hc WHERE hc.C_Currency_ID = o.C_Currency_ID) AS Header_Currency_Code,
                       (SELECT hc.CurSymbol FROM C_Currency hc WHERE hc.C_Currency_ID = o.C_Currency_ID) AS Header_Currency_Symbol,
                       (SELECT hc.StdPrecision FROM C_Currency hc WHERE hc.C_Currency_ID = o.C_Currency_ID) AS Header_Currency_Precision
                  FROM C_Order o
                  INNER JOIN C_BPartner bp ON ( bp.C_BPartner_ID = o.C_BPartner_ID )
                  LEFT OUTER JOIN M_Warehouse w ON ( w.M_Warehouse_ID = o.M_Warehouse_ID )
                 WHERE o.C_Order_ID = @Order_Id
                   AND o.AD_Client_ID = @AD_Client_ID
                   AND o.IsActive = 'Y'";

            headerSql = MRole.GetDefault(ctx).AddAccessSQL(headerSql, "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            int warehouseId = 0;
            bool taxIncluded = false; // VAS163 2026-11-20: Sr 243 line amounts restated without tax

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(headerSql, new[]
                {
                    new SqlParameter("@Order_Id", orderId),
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
                });

                if (dr == null || !dr.Read()) { return result; }

                string docStatusCode = Util.GetValueOfString(dr["Doc_Status_Code"]);
                warehouseId = dr["Warehouse_Id"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Warehouse_Id"]);
                taxIncluded = string.Equals((Util.GetValueOfString(dr["Tax_Included"]) ?? "").Trim(), "Y", StringComparison.OrdinalIgnoreCase);
                DateTime? dateOrdered = dr["Date_Ordered"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr["Date_Ordered"]);
                DateTime? datePromised = dr["Date_Promised"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr["Date_Promised"]);

                result.Order = new OrderHeader
                {
                    // VAS163 2026-11-12: order (document) currency of OrderValue / line amounts.
                    CurrencyIso = (Util.GetValueOfString(dr["Header_Currency_Code"]) ?? "").Trim(),
                    CurrencySymbol = (Util.GetValueOfString(dr["Header_Currency_Symbol"]) ?? "").Trim(),
                    CurrencyPrecision = dr["Header_Currency_Precision"] == DBNull.Value ? 2 : Util.GetValueOfInt(dr["Header_Currency_Precision"]),
                    SalesOrderId = Util.GetValueOfInt(dr["Order_Id"]),
                    SalesOrderNumber = Util.GetValueOfString(dr["Document_No"]),
                    SalesOrderDate = dateOrdered.HasValue ? dateOrdered.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                    DatePromised = datePromised.HasValue ? datePromised.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                    CustomerName = Util.GetValueOfString(dr["Customer_Name"]),
                    DocumentStatus = DecodeLabel(docStatusMap, docStatusCode),
                    OrderValue = dr["Order_Value"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Order_Value"]),
                    WarehouseName = Util.GetValueOfString(dr["Warehouse_Name"]),
                    DeliveryMode = DecodeLabel(GetDecodeMap("C_Order", "DeliveryViaRule", language), Util.GetValueOfString(dr["Delivery_Via_Rule"]))
                };
            }
            finally
            {
                CloseReader(dr);
            }

            if (result.Order == null) { return result; }

            LoadOrderLines(ctx, result, orderId, warehouseId, taxIncluded, result.Order.CurrencyPrecision);

            // VAS163 2026-11-20: Sr 242 the Delivery status card counts Item-type product lines only
            // (charges and other product types never make an order (un)delivered).
            bool anyDelivered = false, allDelivered = true;
            bool hasLines = false;
            foreach (OrderLineRow line in result.Lines)
            {
                if (!line.IsItem) { continue; }
                hasLines = true;
                if (line.QtyDelivered > 0) { anyDelivered = true; }
                if (line.QtyDelivered < line.QtyOrdered) { allDelivered = false; }
            }

            string deliveryStatusKey, deliveryStatusFallback;
            if (hasLines && allDelivered) { deliveryStatusKey = "VAS_286_DeliveryFull"; deliveryStatusFallback = "Delivered"; }
            else if (anyDelivered) { deliveryStatusKey = "VAS_286_DeliveryPartial"; deliveryStatusFallback = "Partially delivered"; }
            else { deliveryStatusKey = "VAS_286_DeliveryNone"; deliveryStatusFallback = "Not delivered"; }
            result.Order.DeliveryStatus = Msg.GetMsg(ctx, deliveryStatusKey, true) ?? deliveryStatusFallback; // VAS163 2026-11-11: text-only label (3-arg GetMsg, no tip appended)

            return result;
        }

        /// <summary>Every active line of the order, each carrying its own free-stock figure.</summary>
        /// <param name="ctx">Login context.</param>
        /// <param name="result">Detail payload the lines are added to.</param>
        /// <param name="orderId">C_Order_ID.</param>
        /// <param name="headerWarehouseId">Order warehouse (fallback for the line warehouse).</param>
        /// <param name="taxIncluded">VAS163 2026-11-20: Sr 243 order prices include tax - amounts restated without tax.</param>
        /// <param name="stdPrecision">Order currency precision.</param>
        private void LoadOrderLines(Ctx ctx, OrderDetailResult result, int orderId, int headerWarehouseId, bool taxIncluded, int stdPrecision)
        {
            // VAS163 2026-11-20: Sr 237-241 (as VAS_271): charge name for charge lines, product type,
            // QtyEntered (selected UOM), base UOM name, tax data. ProductType (CHAR) and the names are
            // read raw and resolved in C# (no COALESCE against a literal).
            string sql = @"
                SELECT ol.C_OrderLine_ID AS Line_Id,
                       ol.Line AS Line_No,
                       ol.M_Product_ID AS Product_Id,
                       ol.C_Charge_ID AS Charge_Id,
                       ol.Description AS Description,
                       ol.QtyOrdered AS Qty_Ordered,
                       ol.QtyEntered AS Qty_Entered,
                       ol.QtyDelivered AS Qty_Delivered,
                       ol.PriceActual AS Price_Actual,
                       ol.PriceEntered AS Price_Entered,
                       ol.LineNetAmt AS Line_Net_Amt,
                       ol.TaxAmt AS Line_Tax_Amt,
                       ol.C_Tax_ID AS Line_Tax_Id,
                       tx.Rate AS Tax_Rate,
                       ol.M_AttributeSetInstance_ID AS Asi_Id,
                       ol.C_UOM_ID AS Uom_Id,
                       ol.M_Warehouse_ID AS Line_Warehouse_Id,
                       COALESCE(p.Name, p.Value) AS Product_Name,
                       p.ProductType AS Product_Type,
                       ch.Name AS Charge_Name,
                       COALESCE(asi.Description, N'') AS Attribute_Text,
                       COALESCE(u.Name, N'') AS Uom_Name,
                       COALESCE(bu.Name, N'') AS Base_Uom_Name
                  FROM C_OrderLine ol
                  LEFT OUTER JOIN M_Product p ON ( p.M_Product_ID = ol.M_Product_ID )
                  LEFT OUTER JOIN C_Charge ch ON ( ch.C_Charge_ID = ol.C_Charge_ID )
                  LEFT OUTER JOIN C_Tax tx ON ( tx.C_Tax_ID = ol.C_Tax_ID )
                  LEFT OUTER JOIN M_AttributeSetInstance asi ON ( asi.M_AttributeSetInstance_ID = ol.M_AttributeSetInstance_ID )
                  LEFT OUTER JOIN C_UOM u ON ( u.C_UOM_ID = ol.C_UOM_ID )
                  LEFT OUTER JOIN C_UOM bu ON ( bu.C_UOM_ID = p.C_UOM_ID )
                 WHERE ol.C_Order_ID = @Order_Id
                   AND ol.IsActive = 'Y'
                 ORDER BY ol.Line ASC
                 OFFSET 0 ROWS FETCH NEXT @Max_Line_Rows ROWS ONLY";

            Dictionary<int, decimal> netRatioByTax = taxIncluded ? GetOrderTaxNetRatios(orderId) : new Dictionary<int, decimal>();

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, new[]
                {
                    new SqlParameter("@Order_Id", orderId),
                    new SqlParameter("@Max_Line_Rows", MaxLineRows)
                });

                while (dr != null && dr.Read())
                {
                    decimal qtyOrderedBase = dr["Qty_Ordered"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Qty_Ordered"]);
                    decimal qtyEntered = dr["Qty_Entered"] == DBNull.Value ? qtyOrderedBase : Util.GetValueOfDecimal(dr["Qty_Entered"]);
                    decimal qtyDeliveredBase = dr["Qty_Delivered"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Qty_Delivered"]);
                    int productId = dr["Product_Id"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Product_Id"]);
                    int lineWarehouseId = dr["Line_Warehouse_Id"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Line_Warehouse_Id"]);
                    int warehouseId = lineWarehouseId > 0 ? lineWarehouseId : headerWarehouseId;

                    // VAS163 2026-11-20: Sr 240 Ordered / Delivered / Pending in the line's selected UOM
                    // (QtyEntered; base quantities scaled by QtyEntered / QtyOrdered).
                    decimal uomRatio = qtyOrderedBase != 0m ? (qtyEntered / qtyOrderedBase) : 1m;
                    decimal qtyOrdered = qtyEntered;
                    decimal qtyDelivered = Math.Round(qtyDeliveredBase * uomRatio, 6, MidpointRounding.AwayFromZero);

                    // VAS163 2026-11-20: Sr 238 delivered / pending / stock / line status only for Item-type products.
                    bool isItem = productId > 0
                        && string.Equals((Util.GetValueOfString(dr["Product_Type"]) ?? "").Trim(), "I", StringComparison.OrdinalIgnoreCase);

                    // VAS163 2026-11-20: Sr 237 a charge line shows the charge's name.
                    string productName = Util.GetValueOfString(dr["Product_Name"]);
                    if (string.IsNullOrEmpty(productName)) { productName = Util.GetValueOfString(dr["Charge_Name"]); }
                    if (string.IsNullOrEmpty(productName)) { productName = Util.GetValueOfString(dr["Description"]); }

                    // VAS163 2026-11-20: Sr 243 line amount without tax (as VAS_271): on a tax-inclusive order
                    // LineNetAmt is scaled by its tax's net ratio from C_OrderTax, else line TaxAmt / rate is taken off.
                    decimal lineGross = dr["Line_Net_Amt"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Line_Net_Amt"]);
                    decimal lineAmt = lineGross;
                    int lineTaxId = dr["Line_Tax_Id"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Line_Tax_Id"]);
                    if (taxIncluded && netRatioByTax.ContainsKey(lineTaxId))
                    {
                        lineAmt = Math.Round(lineGross * netRatioByTax[lineTaxId], stdPrecision, MidpointRounding.AwayFromZero);
                    }
                    else if (taxIncluded)
                    {
                        decimal lineTax = dr["Line_Tax_Amt"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Line_Tax_Amt"]);
                        decimal taxRate = dr["Tax_Rate"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Tax_Rate"]);
                        if (lineTax == 0m && taxRate > 0m)
                        {
                            lineTax = Math.Round(lineGross * taxRate / (100m + taxRate), stdPrecision, MidpointRounding.AwayFromZero);
                        }
                        lineAmt = lineGross - lineTax;
                    }

                    // Rate per selected UOM (PriceEntered, fallback PriceActual), restated without tax with the same proportion.
                    decimal rate = dr["Price_Entered"] == DBNull.Value
                        ? (dr["Price_Actual"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Price_Actual"]))
                        : Util.GetValueOfDecimal(dr["Price_Entered"]);
                    if (taxIncluded && lineGross != 0m)
                    {
                        rate = Math.Round(rate * lineAmt / lineGross, 6, MidpointRounding.AwayFromZero);
                    }

                    // VAS163 2026-11-20: Sr 241 In stock stays in the product's base UOM (name sent with it);
                    // the shortage is judged in the line's UOM. Stock is only looked up for item lines.
                    decimal onHandBase = isItem ? Math.Max(0m, ResolveFreeStock(ctx, productId, warehouseId)) : 0m;
                    decimal qtyPending = isItem ? Math.Max(0m, qtyOrdered - qtyDelivered) : 0m;

                    result.Lines.Add(new OrderLineRow
                    {
                        LineNo = Util.GetValueOfInt(dr["Line_No"]),
                        ProductName = productName,
                        AttributeText = Util.GetValueOfString(dr["Attribute_Text"]),
                        UomName = Util.GetValueOfString(dr["Uom_Name"]),
                        BaseUomName = Util.GetValueOfString(dr["Base_Uom_Name"]),
                        IsItem = isItem,
                        HasAttribute = dr["Asi_Id"] != DBNull.Value && Util.GetValueOfInt(dr["Asi_Id"]) > 0,
                        QtyOrdered = qtyOrdered,
                        QtyDelivered = isItem ? qtyDelivered : 0m,
                        QtyPending = qtyPending,
                        // VAS163 2026-11-11: row 58 In stock never negative - show 0.
                        FreeStock = onHandBase,
                        QtyShort = isItem ? Math.Max(0m, qtyPending - onHandBase * uomRatio) : 0m,
                        Rate = rate,
                        Amount = lineAmt
                    });
                }
            }
            finally
            {
                CloseReader(dr);
            }
        }

        /// <summary>
        /// Warehouse-level free stock for one product: QtyOnHand - QtyReserved -
        /// QtyDedicated - QtyAllocated, summed across the warehouse's active locators.
        /// </summary>
        private decimal ResolveFreeStock(Ctx ctx, int productId, int warehouseId)
        {
            if (productId <= 0 || warehouseId <= 0) { return 0m; }

            // VAS163 2026-11-13: rows 1029/1064 one In-stock definition in every SO lines pop-up (same as VAS_270/271/272):
            // on-hand of the order's warehouse in the base UOM (the reservation already holds this order), clamped at 0 by the caller.
            string sql = @"
                SELECT COALESCE(SUM(COALESCE(s.QtyOnHand, 0)), 0) AS Free_Stock
                  FROM M_Storage s
                  INNER JOIN M_Locator loc ON ( loc.M_Locator_ID = s.M_Locator_ID )
                 WHERE s.M_Product_ID = @M_Product_ID
                   AND loc.M_Warehouse_ID = @M_Warehouse_ID
                   AND loc.IsActive = 'Y'";

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "s", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            return Util.GetValueOfDecimal(DB.ExecuteScalar(sql, new[]
            {
                new SqlParameter("@M_Product_ID", productId),
                new SqlParameter("@M_Warehouse_ID", warehouseId)
            }, null));
        }

        private static string DecodeLabel(Dictionary<string, string> map, string code)
        {
            if (string.IsNullOrEmpty(code)) { return ""; }
            string label;
            return map != null && map.TryGetValue(code, out label) ? label : code;
        }

        private static readonly ConcurrentDictionary<string, Dictionary<string, string>> DecodeMapCache =
            new ConcurrentDictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        private Dictionary<string, string> GetDecodeMap(string tableName, string columnName, string language)
        {
            string cacheKey = tableName + "." + columnName + "|" + language;

            Dictionary<string, string> cached;
            if (DecodeMapCache.TryGetValue(cacheKey, out cached)) { return cached; }

            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int columnId = GetColumnId(tableName, columnName);

            if (columnId > 0)
            {
                string sql = @"
                    SELECT rl.Value AS Code, COALESCE(rlt.Name, rl.Name, rl.Value) AS Label
                      FROM AD_Ref_List rl
                      INNER JOIN AD_Column col ON ( col.AD_Reference_Value_ID = rl.AD_Reference_ID )
                      LEFT OUTER JOIN AD_Ref_List_Trl rlt ON ( rlt.AD_Ref_List_ID = rl.AD_Ref_List_ID AND rlt.AD_Language = @AD_Language )
                     WHERE col.AD_Column_ID = @AD_Column_ID
                       AND rl.IsActive = 'Y'
                       AND col.IsActive = 'Y'
                     ORDER BY rl.Value";

                IDataReader dr = null;
                try
                {
                    dr = DB.ExecuteReader(sql, new[]
                    {
                        new SqlParameter("@AD_Language", SqlDbType.NVarChar) { Value = language },
                        new SqlParameter("@AD_Column_ID", SqlDbType.Int) { Value = columnId }
                    });
                    while (dr != null && dr.Read())
                    {
                        string code = Util.GetValueOfString(dr["Code"]);
                        if (!string.IsNullOrEmpty(code))
                        {
                            map[code] = Util.GetValueOfString(dr["Label"]);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Log(Level.SEVERE, "VAS_286_SOPendingForPaymentWidget.GetDecodeMap(" + tableName + "." + columnName + ")", ex);
                }
                finally
                {
                    CloseReader(dr);
                }
            }

            DecodeMapCache[cacheKey] = map;
            return map;
        }

        private static readonly ConcurrentDictionary<string, int> ColumnIdCache = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private static int GetColumnId(string tableName, string columnName)
        {
            string cacheKey = tableName + "." + columnName;

            int cached;
            if (ColumnIdCache.TryGetValue(cacheKey, out cached)) { return cached; }

            int columnId = Util.GetValueOfInt(DB.ExecuteScalar(
                "SELECT AD_Column_ID FROM AD_Column WHERE ColumnName = @Col AND AD_Table_ID = (SELECT AD_Table_ID FROM AD_Table WHERE TableName = @Tbl)",
                new[]
                {
                    new SqlParameter("@Col", SqlDbType.NVarChar) { Value = columnName },
                    new SqlParameter("@Tbl", SqlDbType.NVarChar) { Value = tableName }
                },
                null));

            ColumnIdCache[cacheKey] = columnId;
            return columnId;
        }

        private string GetLanguage(Ctx ctx)
        {
            string language = ctx == null ? string.Empty : ctx.GetAD_Language();
            return string.IsNullOrWhiteSpace(language) ? "en_US" : language;
        }

        private void CloseReader(IDataReader reader)
        {
            if (reader == null) { return; }
            reader.Close();
            reader.Dispose();
        }

        /// <summary>
        /// VAS163 2026-11-10: base currency of the login client's primary accounting schema
        /// (AD_ClientInfo.C_AcctSchema1_ID -> C_AcctSchema.C_Currency_ID, same lookup as
        /// VAS_205) so the widget labels its amounts with it instead of a hard-coded rupee.
        /// Left empty (client shows no symbol) when it cannot be resolved.
        /// </summary>
        private static void FillBaseCurrency(Ctx ctx, CurrencyPayload target)
        {
            if (ctx == null || target == null) { return; }
            try
            {
                DataSet ds = DB.ExecuteDataset(@"
                    SELECT c.ISO_Code AS Currency_Code, c.CurSymbol AS Currency_Symbol, c.StdPrecision AS Currency_Precision
                      FROM AD_ClientInfo ci
                     INNER JOIN C_AcctSchema cs ON ( cs.C_AcctSchema_ID = ci.C_AcctSchema1_ID )
                     INNER JOIN C_Currency c ON ( c.C_Currency_ID = cs.C_Currency_ID )
                     WHERE ci.AD_Client_ID = @AD_Client_ID",
                    new[] { new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()) }, null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) { return; }
                DataRow r = ds.Tables[0].Rows[0];
                target.CurrencyIso = (Util.GetValueOfString(r["Currency_Code"]) ?? string.Empty).Trim();
                target.CurrencySymbol = (Util.GetValueOfString(r["Currency_Symbol"]) ?? string.Empty).Trim();
                if (r["Currency_Precision"] != DBNull.Value) { target.CurrencyPrecision = Util.GetValueOfInt(r["Currency_Precision"]); }
            }
            catch (Exception ex)
            {
                Log.Log(Level.WARNING, "FillBaseCurrency", ex);
            }
        }

        /// <summary>
        /// VAS163 2026-11-20: Sr 234 C_Currency_ID of the login client's primary accounting schema
        /// (same lookup as FillBaseCurrency / VAS_271); 0 when it cannot be resolved (the chip then
        /// sums unconverted).
        /// </summary>
        /// <param name="ctx">Login context.</param>
        /// <returns>Base currency id or 0.</returns>
        private static int GetBaseCurrencyId(Ctx ctx)
        {
            if (ctx == null) { return 0; }
            try
            {
                return Util.GetValueOfInt(DB.ExecuteScalar(@"
                    SELECT cs.C_Currency_ID
                      FROM AD_ClientInfo ci
                     INNER JOIN C_AcctSchema cs ON ( cs.C_AcctSchema_ID = ci.C_AcctSchema1_ID )
                     WHERE ci.AD_Client_ID = @AD_Client_ID",
                    new[] { new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()) }, null));
            }
            catch (Exception ex)
            {
                Log.Log(Level.WARNING, "VAS_286_SOPendingForPaymentWidget.GetBaseCurrencyId", ex);
                return 0;
            }
        }

        /// <summary>
        /// VAS163 2026-11-20: Sr 243 (copied from VAS_271). Tax-inclusive order only: per C_Tax_ID,
        /// the share of the lines' gross (SUM(LineNetAmt)) that is NOT tax, from the order's own tax
        /// summary (C_OrderTax.TaxAmt). A tax without a plausible summary row is left out and the
        /// caller falls back to the line's own TaxAmt / rate.
        /// </summary>
        /// <param name="orderId">C_Order_ID.</param>
        /// <returns>C_Tax_ID -> net ratio.</returns>
        private static Dictionary<int, decimal> GetOrderTaxNetRatios(int orderId)
        {
            Dictionary<int, decimal> ratios = new Dictionary<int, decimal>();
            try
            {
                Dictionary<int, decimal> grossByTax = new Dictionary<int, decimal>();
                DataSet grossDs = DB.ExecuteDataset(@"
                    SELECT ol.C_Tax_ID AS Tax_Id, COALESCE(SUM(ol.LineNetAmt), 0) AS Gross
                      FROM C_OrderLine ol
                     WHERE ol.C_Order_ID = @Order_Id
                       AND ol.IsActive = 'Y'
                     GROUP BY ol.C_Tax_ID", new[] { new SqlParameter("@Order_Id", orderId) }, null);
                if (grossDs != null && grossDs.Tables.Count > 0)
                {
                    foreach (DataRow row in grossDs.Tables[0].Rows)
                    {
                        grossByTax[Util.GetValueOfInt(row["Tax_Id"])] = Util.GetValueOfDecimal(row["Gross"]);
                    }
                }

                DataSet taxDs = DB.ExecuteDataset(@"
                    SELECT ot.C_Tax_ID AS Tax_Id, COALESCE(SUM(ot.TaxAmt), 0) AS Tax_Amt
                      FROM C_OrderTax ot
                     WHERE ot.C_Order_ID = @Order_Id
                       AND ot.IsActive = 'Y'
                     GROUP BY ot.C_Tax_ID", new[] { new SqlParameter("@Order_Id", orderId) }, null);
                if (taxDs != null && taxDs.Tables.Count > 0)
                {
                    foreach (DataRow row in taxDs.Tables[0].Rows)
                    {
                        int taxId = Util.GetValueOfInt(row["Tax_Id"]);
                        decimal taxAmt = Util.GetValueOfDecimal(row["Tax_Amt"]);
                        decimal gross;
                        if (grossByTax.TryGetValue(taxId, out gross) && gross > 0m && taxAmt >= 0m && taxAmt < gross)
                        {
                            ratios[taxId] = (gross - taxAmt) / gross;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_286_SOPendingForPaymentWidget.GetOrderTaxNetRatios", ex);
            }
            return ratios;
        }

        /// <summary>VAS163 2026-11-10: base (accounting-schema) currency carried by the widget's first payload.</summary>
        private class CurrencyPayload
        {
            public string CurrencyIso { get; set; } = string.Empty;
            public string CurrencySymbol { get; set; } = string.Empty;
            public int? CurrencyPrecision { get; set; }
        }

        private JsonResult ErrorResult(Ctx ctx)
        {
            string message = Msg.GetMsg(ctx, "Error", true) ?? "Error"; // VAS163 2026-11-11: text-only label (3-arg GetMsg, no tip appended)
            string json = JsonConvert.SerializeObject(new { Error = message });
            return Json(json, JsonRequestBehavior.AllowGet);
        }

        private class PendingPaymentResult : CurrencyPayload
        {
            public decimal TotalDue { get; set; }
            public int Total { get; set; }
            public List<PendingPaymentRow> Rows { get; set; }
        }

        private class PendingPaymentRow
        {
            public int SalesOrderId { get; set; }
            public string SalesOrderNumber { get; set; }
            public string SalesOrderDate { get; set; }
            public string CustomerName { get; set; }
            public string WarehouseName { get; set; }
            public string PaymentType { get; set; }
            public string PaymentDueDate { get; set; }
            public bool IsOverdue { get; set; }
            public int DaysOverdue { get; set; }
            public decimal Amount { get; set; }
            /// <summary>VAS163 2026-11-11: Row 93 - document (price list) currency of Amount.</summary>
            public string CurrencySymbol { get; set; }
            public string CurrencyIso { get; set; }
            public int CurrencyPrecision { get; set; }
        }

        private class OrderDetailResult
        {
            public OrderHeader Order { get; set; }
            public List<OrderLineRow> Lines { get; set; }
        }

        private class OrderHeader
        {
            // VAS163 2026-11-12: order (document) currency.
            public string CurrencyIso { get; set; }
            public string CurrencySymbol { get; set; }
            public int CurrencyPrecision { get; set; }
            public int SalesOrderId { get; set; }
            public string SalesOrderNumber { get; set; }
            public string SalesOrderDate { get; set; }
            public string DatePromised { get; set; }
            public string CustomerName { get; set; }
            public string DocumentStatus { get; set; }
            public decimal OrderValue { get; set; }
            public string WarehouseName { get; set; }
            public string DeliveryMode { get; set; }
            public string DeliveryStatus { get; set; }
        }

        private class OrderLineRow
        {
            public int LineNo { get; set; }
            public string ProductName { get; set; }
            public string AttributeText { get; set; }
            public string UomName { get; set; }
            /// <summary>VAS163 2026-11-20: Sr 238-241 product base UOM, Item-type flag, ASI present, shortage in line UOM.</summary>
            public string BaseUomName { get; set; }
            public bool IsItem { get; set; }
            public bool HasAttribute { get; set; }
            public decimal QtyShort { get; set; }
            public decimal QtyOrdered { get; set; }
            public decimal QtyDelivered { get; set; }
            public decimal QtyPending { get; set; }
            public decimal FreeStock { get; set; }
            public decimal Rate { get; set; }
            public decimal Amount { get; set; }
        }
    }
}
