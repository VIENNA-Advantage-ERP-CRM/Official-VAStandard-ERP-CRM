/******************************************************
 * Module Name    : VAS
 * Purpose        : Sales Order dashboard "SOs on Credit Hold" KPI widget endpoints
 * chronological  : Development
 * Created Date   : 2026-09-14
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
    /// Module Name : VAS_275_SOsOnCreditHoldWidget
    /// Purpose     : Data endpoints for the 2x1 "SOs on Credit Hold" KPI tile on the
    ///               Sales Order dashboard - orders commercially agreed and
    ///               operationally ready but blocked from dispatch by the customer's
    ///               credit position. (1) the tile/summary figures - held count,
    ///               blocked value, distinct customer count and the age (by
    ///               DateOrdered) of the oldest held Sales Order, and (2) the
    ///               drill-down modal's paginated documents list (highest order value
    ///               first) with a single fixed-vocabulary hold reason per row, and
    ///               (3) the shared Sales Order record-preview data (header stats +
    ///               paginated lines with per-line free stock) reused for both the
    ///               "record" and "lines" child modals.
    ///
    ///   Business definition per
    ///   08_SOs_On_Credit_Hold_Claude_Development_Prompt.txt:
    ///     - Real active Sales Order, DocStatus = 'CO' only, non-return, non-quotation.
    ///     - Credit level is EFFECTIVE, not always customer-level:
    ///       C_BPartner.CreditStatusSettingOn = 'CL' -&gt; use the order's own
    ///       C_BPartner_Location's SOCreditStatus/CreditValidation/SO_CreditLimit/
    ///       SO_CreditUsed; otherwise use the customer-level (C_BPartner) fields.
    ///     - Held = effective SOCreditStatus = 'H' AND effective CreditValidation is a
    ///       shipment-blocking code (B/D/E/F only - 'J' Warning on All is explicitly
    ///       NOT a block and must not be counted).
    ///     - "Oldest held SO" (the confirmed replacement for the original draft's
    ///       "Oldest hold") is calendar days from the oldest held order's DateOrdered
    ///       to today - no hold-timestamp column is added or assumed.
    ///     - Outstanding (5-Nov row 37) = the effective level's TotalOpenBalance (the
    ///       customer's open AR that VA credit management compares with the limit),
    ///       SO_CreditUsed when that is empty.
    ///     - Overdue invoices (5-Nov row 37) = count + open amount of the customer's
    ///       completed/closed, non-return AR invoices with VA009_OpenAmount &gt; 0 whose
    ///       due date (earliest unpaid C_InvoicePaySchedule, else DueDate, else
    ///       DateInvoiced) is before today.
    ///     - Hold reason is a fixed vocabulary evaluated as a priority chain (never
    ///       free text, never more than one value per row): (1) "Limit exceeded" when
    ///       credit limit &gt; 0 and Outstanding &gt; limit; (2) "Invoice overdue
    ///       60d" / "30d" / "Invoice overdue" by the oldest overdue invoice; (3)
    ///       "Awaiting advance" when the order's payment term has VA009_Advance='Y' AND
    ///       no completed receipt is linked to this order; (4) otherwise "Credit status
    ///       on hold" (status set to Credit Hold without either condition).
    ///
    ///   Per the CTE/MRole rule (Prompt_Instructions.txt "Case 1"), AddAccessSQL is
    ///   applied to the isolated "base_orders" fragment (BuildBaseOrdersSql) BEFORE it
    ///   is embedded into any larger statement. Unlike VAS_270-274, this fragment is
    ///   not single-table: the effective-credit-level CASE expressions in its own WHERE
    ///   clause need C_BPartner/C_BPartner_Location/C_PaymentTerm joined in the SAME
    ///   query, so base_orders has ONE "FROM C_Order o" plus plain JOINs (no nested
    ///   CTEs) - the exact shape VAS_268's own header/recent-orders queries already
    ///   prove safe for AddAccessSQL (a single FROM keyword, regardless of how many
    ///   JOINs hang off it). The ar_overdue (RV_OpenItem) and order_receipt (C_Payment)
    ///   CTEs used only by GetOrdersData - which DO introduce additional FROM clauses -
    ///   stay outside that fragment and are joined afterward as secondary aggregates,
    ///   never re-run through AddAccessSQL, mirroring the fix already applied to
    ///   VAS_270-274 after VAS_270 hit ORA-00904 from applying AddAccessSQL to a
    ///   multi-FROM-clause statement.
    ///   Read-only here; no writes.
    /// Chronological development:
    ///   VAI052      2026-09-14 Created
    /// </summary>
    [System.Web.Mvc.SessionState(System.Web.SessionState.SessionStateBehavior.ReadOnly)]
    public class VAS_275_SOsOnCreditHoldWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_275_SOsOnCreditHoldWidgetController).FullName);

        private const int DefaultPageSize = 10;
        private const int MaxPageSize = 10;
        private const int MaxLineRows = 200;

        /// <summary>
        /// Summary figures for the tile and the modal's stat strip.
        /// </summary>
        /// <returns>JSON SummaryResult or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetSummary()
        {
            if (Session["ctx"] == null) { return Json("", JsonRequestBehavior.AllowGet); }

            Ctx ctx = Session["ctx"] as Ctx;

            try
            {
                SummaryResult data = GetSummaryData(ctx);
                FillBaseCurrency(ctx, data);
                string json = JsonConvert.SerializeObject(data);
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_275_SOsOnCreditHoldWidget.GetSummary", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// One page of the underlying held sales orders for the modal's table, highest
        /// order value first.
        /// </summary>
        /// <param name="page">Zero-based page index.</param>
        /// <param name="size">Rows per page (capped at <see cref="MaxPageSize"/>).</param>
        /// <returns>JSON { Total, Rows:[...] } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetOrders(int page = 0, int size = DefaultPageSize)
        {
            if (Session["ctx"] == null) { return Json("", JsonRequestBehavior.AllowGet); }

            Ctx ctx = Session["ctx"] as Ctx;
            if (page < 0) { page = 0; }
            if (size <= 0 || size > MaxPageSize) { size = DefaultPageSize; }

            try
            {
                string json = JsonConvert.SerializeObject(GetOrdersData(ctx, page, size));
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_275_SOsOnCreditHoldWidget.GetOrders", ex);
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
                Log.Log(Level.SEVERE, "VAS_275_SOsOnCreditHoldWidget.GetSalesOrderDetail", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// The base_orders fragment - the ONLY fragment AddAccessSQL is ever applied to
        /// (Prompt_Instructions.txt "Case 1"): a single "FROM C_Order o" with plain
        /// JOINs (no nested CTEs, so no "more than one FROM clause" risk) to
        /// C_BPartner/C_BPartner_Location/C_PaymentTerm, needed because the
        /// effective-credit-level CASE expressions that decide whether an order is held
        /// live in this same query's own WHERE clause.
        /// </summary>
        private static string BuildBaseOrdersSql()
        {
            // VAS163 2026-11-11: rows 15/62-68 new cohort. Effective credit level from
            // C_BPartner.CreditStatusSettingOn ('CL' Location Level -> the order's
            // C_BPartner_Location, otherwise 'CH' Customer Level -> C_BPartner).
            // Only SOCreditStatus = 'H' (Credit Hold) and only the CreditValidation
            // codes that stop the Sales Order: 'A' Stop SO, 'D' Stop All, 'E' Stop SO &
            // Shipment ('J' Warning on All and the shipment/invoice-only codes B/C/F list
            // nothing). Those orders cannot be completed, so the cohort is now the
            // Drafted orders only ('DR'), as the 11-Nov sheet rows 63-65 specify.
            // Credit_Level / Bpartner_Location_Id let the list compute Outstanding and
            // Overdue invoices for the row's own customer or location.
            return @"
                SELECT o.C_Order_ID AS C_Order_ID,
                       o.DocumentNo AS Document_No,
                       o.DateOrdered AS Date_Ordered,
                       o.C_BPartner_ID AS Bpartner_Id,
                       o.C_BPartner_Location_ID AS Bpartner_Location_Id,
                       o.TotalLines AS Order_Value,
                       o.C_Currency_ID AS Currency_Id,
                       COALESCE(bp.Name, N'') AS Customer_Name,
                       CASE WHEN bp.CreditStatusSettingOn = 'CL' THEN 'CL' ELSE 'CH' END AS Credit_Level,
                       CASE
                           WHEN bp.CreditStatusSettingOn = 'CL' THEN COALESCE(bpl.SO_CreditLimit, 0)
                           ELSE COALESCE(bp.SO_CreditLimit, 0)
                       END AS Credit_Limit,
                       CASE WHEN COALESCE(pt.VA009_Advance, 'N') = 'Y' THEN 'Y' ELSE 'N' END AS Advance_Term_Flag
                  FROM C_Order o
                  INNER JOIN C_BPartner bp ON ( bp.C_BPartner_ID = o.C_BPartner_ID )
                  LEFT OUTER JOIN C_BPartner_Location bpl ON ( bpl.C_BPartner_Location_ID = o.C_BPartner_Location_ID )
                  LEFT OUTER JOIN C_PaymentTerm pt ON ( pt.C_PaymentTerm_ID = o.C_PaymentTerm_ID )
                 WHERE o.AD_Client_ID = @AD_Client_ID
                   AND o.IsActive = 'Y'
                   AND o.IsSOTrx = 'Y'
                   AND COALESCE(o.IsReturnTrx, 'N') = 'N'
                   AND EXISTS (SELECT 1 FROM C_DocType dt WHERE dt.C_DocType_ID = o.C_DocTypeTarget_ID AND dt.DocSubTypeSO IN ('SO', 'WR', 'BO'))
                   AND o.DocStatus = 'DR'
                   AND (
                         CASE
                             WHEN bp.CreditStatusSettingOn = 'CL' THEN bpl.SOCreditStatus
                             ELSE bp.SOCreditStatus
                         END
                       ) = 'H'
                   AND (
                         CASE
                             WHEN bp.CreditStatusSettingOn = 'CL' THEN bpl.CreditValidation
                             ELSE bp.CreditValidation
                         END
                       ) IN ('A', 'D', 'E')";
        }

        private SummaryResult GetSummaryData(Ctx ctx)
        {
            SummaryResult result = new SummaryResult();
            if (ctx == null) { return result; }

            // The only AddAccessSQL call for this statement - applied to the isolated
            // base_orders body (single FROM, plain JOINs) BEFORE it is embedded below.
            string baseOrdersSql = MRole.GetDefault(ctx).AddAccessSQL(BuildBaseOrdersSql(), "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string sql = @"
                WITH base_orders AS (" + baseOrdersSql + @")
                SELECT
                    COUNT(1) AS Held_Count,
                    SUM(COALESCE(Order_Value, 0)) AS Blocked_Value,
                    COUNT(DISTINCT Bpartner_Id) AS Customer_Count,
                    CAST(DAYSBETWEEN(CURRENT_DATE, MIN(Date_Ordered)) AS INTEGER) AS Oldest_Held_So_Days
                  FROM base_orders";

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, new[]
                {
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
                });

                if (dr != null && dr.Read())
                {
                    result.HeldCount = dr["Held_Count"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Held_Count"]);
                    result.BlockedValue = dr["Blocked_Value"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Blocked_Value"]);
                    result.CustomerCount = dr["Customer_Count"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Customer_Count"]);
                    result.OldestHeldSoDays = dr["Oldest_Held_So_Days"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Oldest_Held_So_Days"]);
                }
            }
            finally
            {
                CloseReader(dr);
            }

            return result;
        }

        private OrdersResult GetOrdersData(Ctx ctx, int page, int size)
        {
            OrdersResult result = new OrdersResult { Rows = new List<OrderRow>() };
            if (ctx == null) { return result; }

            string baseOrdersSql = MRole.GetDefault(ctx).AddAccessSQL(BuildBaseOrdersSql(), "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            DateTime today = DateTime.Today;
            DateTime overdue30Cutoff = today.AddDays(-30);
            DateTime overdue60Cutoff = today.AddDays(-60);

            // open_inv / ar_overdue (C_Invoice + C_InvoicePaySchedule) and order_receipt
            // (C_Payment) are secondary aggregates joined onto the already-access-filtered
            // base_orders cohort - never AddAccessSQL targets themselves (Prompt_Instructions "Case 1").
            //
            // 5-Nov row 37: overdue invoices used to come from RV_OpenItem, which counted
            // one row per payment-schedule line (so a single invoice could count several
            // times) and does not follow the VA009 paid flags. Now: one row per customer
            // AR invoice (completed/closed, non-return, VA009_OpenAmount > 0) whose due
            // date - the earliest UNPAID schedule (VA009_IsPaid='N'), else the invoice's
            // DueDate, else DateInvoiced - is before today. Count + open amount are returned.
            // VAS163 2026-11-11: rows 15/62-68 Outstanding / Overdue invoices / Hold reason
            // are now computed for the row's OWN effective credit level: Customer level
            // -> all open AR invoices of the order's customer; Location level -> only
            // the open AR invoices of the order's C_BPartner_Location. Outstanding is
            // the open AR invoice amount (VA009_OpenAmount) of that customer/location
            // converted to the base (accounting-schema) currency, the overdue amount the
            // same for invoices past due. (It used to be the customer header's
            // TotalOpenBalance even for location-level customers, so every row of a
            // customer repeated the same customer-level figures.)
            int baseCurrencyId = GetBaseCurrencyId(ctx);

            string sql = @"
                WITH base_orders AS (" + baseOrdersSql + @"),
                open_inv AS (
                    SELECT
                        i.C_Invoice_ID AS C_Invoice_ID,
                        i.C_BPartner_ID AS C_BPartner_ID,
                        i.C_BPartner_Location_ID AS C_BPartner_Location_ID,
                        COALESCE(
                            CASE
                                WHEN i.C_Currency_ID = @Base_Currency_ID THEN COALESCE(i.VA009_OpenAmount, 0)
                                ELSE CurrencyConvert(COALESCE(i.VA009_OpenAmount, 0), i.C_Currency_ID, @Base_Currency_ID, i.DateAcct, i.C_ConversionType_ID, i.AD_Client_ID, i.AD_Org_ID)
                            END, 0) AS Open_Amount,
                        COALESCE(
                            (SELECT MIN(ips.DueDate)
                               FROM C_InvoicePaySchedule ips
                              WHERE ips.C_Invoice_ID = i.C_Invoice_ID
                                AND ips.IsActive = 'Y'
                                AND ips.VA009_IsPaid = 'N'),
                            i.DueDate,
                            i.DateInvoiced
                        ) AS Due_Date
                      FROM C_Invoice i
                     WHERE i.AD_Client_ID = @AD_Client_ID
                       AND i.IsActive = 'Y'
                       AND i.IsSOTrx = 'Y'
                       AND COALESCE(i.IsReturnTrx, 'N') = 'N'
                       AND i.DocStatus IN ('CO', 'CL')
                       AND COALESCE(i.VA009_OpenAmount, 0) > 0
                       AND i.C_BPartner_ID IN (SELECT bo0.Bpartner_Id FROM base_orders bo0)
                ),
                inv_loc AS (
                    SELECT
                        oi.C_BPartner_ID AS C_BPartner_ID,
                        oi.C_BPartner_Location_ID AS C_BPartner_Location_ID,
                        SUM(oi.Open_Amount) AS Open_Amount,
                        SUM(CASE WHEN oi.Due_Date < @Today THEN 1 ELSE 0 END) AS Overdue_Invoice_Count,
                        SUM(CASE WHEN oi.Due_Date < @Today THEN oi.Open_Amount ELSE 0 END) AS Overdue_Amount,
                        SUM(CASE WHEN oi.Due_Date <= @Overdue_60_Cutoff THEN 1 ELSE 0 END) AS Overdue_60_Count,
                        SUM(CASE WHEN oi.Due_Date <= @Overdue_30_Cutoff THEN 1 ELSE 0 END) AS Overdue_30_Count
                      FROM open_inv oi
                     GROUP BY oi.C_BPartner_ID, oi.C_BPartner_Location_ID
                ),
                inv_bp AS (
                    SELECT
                        il0.C_BPartner_ID AS C_BPartner_ID,
                        SUM(il0.Open_Amount) AS Open_Amount,
                        SUM(il0.Overdue_Invoice_Count) AS Overdue_Invoice_Count,
                        SUM(il0.Overdue_Amount) AS Overdue_Amount,
                        SUM(il0.Overdue_60_Count) AS Overdue_60_Count,
                        SUM(il0.Overdue_30_Count) AS Overdue_30_Count
                      FROM inv_loc il0
                     GROUP BY il0.C_BPartner_ID
                ),
                order_receipt AS (
                    SELECT
                        p.C_Order_ID AS C_Order_ID,
                        COUNT(1) AS Receipt_Count
                      FROM C_Payment p
                     WHERE p.AD_Client_ID = @AD_Client_ID
                       AND p.IsActive = 'Y'
                       AND p.IsReceipt = 'Y'
                       AND p.DocStatus = 'CO'
                       AND COALESCE(p.PayAmt, 0) > 0
                       AND p.C_Order_ID IS NOT NULL
                     GROUP BY p.C_Order_ID
                )
                SELECT
                    bo.C_Order_ID AS Order_Id,
                    bo.Document_No AS Document_No,
                    bo.Date_Ordered AS Date_Ordered,
                    bo.Customer_Name AS Customer_Name,
                    bo.Credit_Limit AS Credit_Limit,
                    COALESCE(il.Open_Amount, ib.Open_Amount, 0) AS Outstanding,
                    bo.Order_Value AS Order_Value,
                    cur.CurSymbol AS Currency_Symbol,
                    cur.ISO_Code AS Currency_Code,
                    cur.StdPrecision AS Currency_Precision,
                    bo.Advance_Term_Flag AS Advance_Term_Flag,
                    COALESCE(il.Overdue_Invoice_Count, ib.Overdue_Invoice_Count, 0) AS Overdue_Invoice_Count,
                    COALESCE(il.Overdue_Amount, ib.Overdue_Amount, 0) AS Overdue_Amount,
                    COALESCE(il.Overdue_60_Count, ib.Overdue_60_Count, 0) AS Overdue_60_Count,
                    COALESCE(il.Overdue_30_Count, ib.Overdue_30_Count, 0) AS Overdue_30_Count,
                    COALESCE(rc.Receipt_Count, 0) AS Receipt_Count,
                    COUNT(1) OVER () AS Total_Rows
                  FROM base_orders bo
                  LEFT OUTER JOIN inv_loc il ON ( bo.Credit_Level = 'CL' AND il.C_BPartner_ID = bo.Bpartner_Id AND il.C_BPartner_Location_ID = bo.Bpartner_Location_Id )
                  LEFT OUTER JOIN inv_bp ib ON ( bo.Credit_Level <> 'CL' AND ib.C_BPartner_ID = bo.Bpartner_Id )
                  LEFT OUTER JOIN order_receipt rc ON ( rc.C_Order_ID = bo.C_Order_ID )
                  LEFT OUTER JOIN C_Currency cur ON ( cur.C_Currency_ID = bo.Currency_Id )
                 ORDER BY bo.Order_Value DESC, bo.Date_Ordered ASC
                 OFFSET @Row_Offset ROWS FETCH NEXT @Page_Size ROWS ONLY";

            IDataReader dr = null;
            try
            {
                // This DB layer binds parameters POSITIONALLY (matching left-to-right
                // textual occurrence, not by name): "@AD_Client_ID" occurs three times
                // (base_orders, open_inv, order_receipt) and ar_overdue references
                // "@Overdue_60_Cutoff", "@Overdue_30_Cutoff" then "@Today" - every
                // occurrence needs its own array entry, in that exact order.
                // VAS163 2026-11-11: positional order after the rows 15/62-68 rework:
                // base_orders client, open_inv base currency x2 + client, inv_loc
                // today x2 + 60d + 30d cutoffs, order_receipt client, paging.
                dr = DB.ExecuteReader(sql, new[]
                {
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                    new SqlParameter("@Base_Currency_ID", baseCurrencyId),
                    new SqlParameter("@Base_Currency_ID", baseCurrencyId),
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                    new SqlParameter("@Today", today),
                    new SqlParameter("@Today", today),
                    new SqlParameter("@Overdue_60_Cutoff", overdue60Cutoff),
                    new SqlParameter("@Overdue_30_Cutoff", overdue30Cutoff),
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                    new SqlParameter("@Row_Offset", page * size),
                    new SqlParameter("@Page_Size", size)
                });

                int total = 0;
                while (dr != null && dr.Read())
                {
                    total = dr["Total_Rows"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Total_Rows"]);
                    DateTime? dateOrdered = dr["Date_Ordered"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr["Date_Ordered"]);

                    bool awaitingAdvance = Util.GetValueOfString(dr["Advance_Term_Flag"]) == "Y"
                        && (dr["Receipt_Count"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Receipt_Count"])) == 0;
                    int overdue60Count = dr["Overdue_60_Count"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Overdue_60_Count"]);
                    int overdue30Count = dr["Overdue_30_Count"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Overdue_30_Count"]);
                    int overdueCount = dr["Overdue_Invoice_Count"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Overdue_Invoice_Count"]);
                    decimal creditLimit = dr["Credit_Limit"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Credit_Limit"]);
                    decimal outstanding = dr["Outstanding"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Outstanding"]);
                    // VAS163 2026-11-11: Row 93 - Order value in the document (price list) currency.
                    string rowCurIso = (Util.GetValueOfString(dr["Currency_Code"]) ?? "").Trim();
                    string rowCurSymbol = (Util.GetValueOfString(dr["Currency_Symbol"]) ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(rowCurSymbol)) { rowCurSymbol = rowCurIso; }

                    // 5-Nov row 37: the reason now explains WHY the customer's credit status
                    // is Hold, in this priority: (1) the open balance is above the credit
                    // limit (the condition that sets Credit Hold automatically); (2) the
                    // customer has overdue AR invoices (60d / 30d / any); (3) an advance-term
                    // order with no receipt yet; (4) otherwise the status was put on Credit
                    // Hold without either condition (manual hold). The old chain put
                    // "Awaiting advance" first and reported "Limit exceeded" as the catch-all
                    // even when the balance was within the limit.
                    string holdReasonKey, holdReasonFallback;
                    if (creditLimit > 0 && outstanding > creditLimit)
                    {
                        holdReasonKey = "VAS_275_ReasonLimitExceeded"; holdReasonFallback = "Limit exceeded";
                    }
                    else if (overdue60Count > 0)
                    {
                        holdReasonKey = "VAS_275_ReasonOverdue60"; holdReasonFallback = "Invoice overdue 60d";
                    }
                    else if (overdue30Count > 0)
                    {
                        holdReasonKey = "VAS_275_ReasonOverdue30"; holdReasonFallback = "Invoice overdue 30d";
                    }
                    else if (overdueCount > 0)
                    {
                        holdReasonKey = "VAS_275_ReasonOverdue"; holdReasonFallback = "Invoice overdue";
                    }
                    else if (awaitingAdvance)
                    {
                        holdReasonKey = "VAS_275_ReasonAwaitingAdvance"; holdReasonFallback = "Awaiting advance";
                    }
                    else
                    {
                        holdReasonKey = "VAS_275_ReasonCreditHold"; holdReasonFallback = "Credit status on hold";
                    }

                    result.Rows.Add(new OrderRow
                    {
                        SalesOrderId = Util.GetValueOfInt(dr["Order_Id"]),
                        SalesOrderNumber = Util.GetValueOfString(dr["Document_No"]),
                        SalesOrderDate = dateOrdered.HasValue ? dateOrdered.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                        CustomerName = Util.GetValueOfString(dr["Customer_Name"]),
                        CreditLimit = creditLimit,
                        Outstanding = outstanding,
                        OrderValue = dr["Order_Value"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Order_Value"]),
                        CurrencySymbol = rowCurSymbol,
                        CurrencyIso = rowCurIso,
                        CurrencyPrecision = dr["Currency_Precision"] == DBNull.Value ? 2 : Util.GetValueOfInt(dr["Currency_Precision"]),
                        OverdueInvoiceCount = overdueCount,
                        OverdueAmount = dr["Overdue_Amount"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Overdue_Amount"]),
                        HoldReason = GetText(ctx, holdReasonKey, holdReasonFallback)
                    });
                }
                result.Total = total;
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
                       o.TotalLines AS Order_Value,
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

            string docStatusCode = "";
            int warehouseId = 0;

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(headerSql, new[]
                {
                    new SqlParameter("@Order_Id", orderId),
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
                });

                if (dr == null || !dr.Read()) { return result; }

                docStatusCode = Util.GetValueOfString(dr["Doc_Status_Code"]);
                warehouseId = dr["Warehouse_Id"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Warehouse_Id"]);
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
                    DocumentStatusCode = docStatusCode,
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

            LoadOrderLines(ctx, result, orderId, warehouseId);

            bool anyDelivered = false, allDelivered = true;
            bool hasLines = result.Lines.Count > 0;
            foreach (OrderLineRow line in result.Lines)
            {
                if (line.QtyDelivered > 0) { anyDelivered = true; }
                if (line.QtyDelivered < line.QtyOrdered) { allDelivered = false; }
            }

            string deliveryStatusKey, deliveryStatusFallback;
            if (hasLines && allDelivered)
            {
                deliveryStatusKey = "VAS_275_DeliveryFull"; deliveryStatusFallback = "Delivered";
            }
            else if (anyDelivered)
            {
                deliveryStatusKey = "VAS_275_DeliveryPartial"; deliveryStatusFallback = "Partially delivered";
            }
            else
            {
                deliveryStatusKey = "VAS_275_DeliveryNone"; deliveryStatusFallback = "Not delivered";
            }
            result.Order.DeliveryStatus = Msg.GetMsg(ctx, deliveryStatusKey, true) ?? deliveryStatusFallback; // VAS163 2026-11-11: text-only label (3-arg GetMsg, no tip appended)

            return result;
        }

        /// <summary>
        /// Every active line of the order, each carrying its own free-stock figure
        /// (per <see cref="ResolveFreeStock"/>) and a derived line-status chip. This
        /// widget's cohort is always DocStatus='CO', so line status only ever
        /// distinguishes fully/partly delivered from still-in-process.
        /// </summary>
        private void LoadOrderLines(Ctx ctx, OrderDetailResult result, int orderId, int headerWarehouseId)
        {
            string sql = @"
                SELECT ol.C_OrderLine_ID AS Line_Id,
                       ol.Line AS Line_No,
                       ol.M_Product_ID AS Product_Id,
                       ol.QtyOrdered AS Qty_Ordered,
                       ol.QtyDelivered AS Qty_Delivered,
                       ol.PriceActual AS Price_Actual,
                       ol.LineNetAmt AS Line_Net_Amt,
                       ol.M_AttributeSetInstance_ID AS Asi_Id,
                       ol.C_UOM_ID AS Uom_Id,
                       ol.M_Warehouse_ID AS Line_Warehouse_Id,
                       COALESCE(p.Name, p.Value) AS Product_Name,
                       COALESCE(asi.Description, N'') AS Attribute_Text,
                       COALESCE(u.Name, N'') AS Uom_Name
                  FROM C_OrderLine ol
                  LEFT OUTER JOIN M_Product p ON ( p.M_Product_ID = ol.M_Product_ID )
                  LEFT OUTER JOIN M_AttributeSetInstance asi ON ( asi.M_AttributeSetInstance_ID = ol.M_AttributeSetInstance_ID )
                  LEFT OUTER JOIN C_UOM u ON ( u.C_UOM_ID = ol.C_UOM_ID )
                 WHERE ol.C_Order_ID = @Order_Id
                   AND ol.IsActive = 'Y'
                 ORDER BY ol.Line ASC
                 OFFSET 0 ROWS FETCH NEXT @Max_Line_Rows ROWS ONLY";

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
                    decimal qtyOrdered = dr["Qty_Ordered"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Qty_Ordered"]);
                    decimal qtyDelivered = dr["Qty_Delivered"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Qty_Delivered"]);
                    int productId = dr["Product_Id"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Product_Id"]);
                    int lineWarehouseId = dr["Line_Warehouse_Id"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Line_Warehouse_Id"]);
                    int warehouseId = lineWarehouseId > 0 ? lineWarehouseId : headerWarehouseId;

                    result.Lines.Add(new OrderLineRow
                    {
                        LineNo = Util.GetValueOfInt(dr["Line_No"]),
                        ProductName = Util.GetValueOfString(dr["Product_Name"]),
                        AttributeText = Util.GetValueOfString(dr["Attribute_Text"]),
                        UomName = Util.GetValueOfString(dr["Uom_Name"]),
                        QtyOrdered = qtyOrdered,
                        QtyDelivered = qtyDelivered,
                        QtyPending = qtyOrdered - qtyDelivered,
                        // VAS163 2026-11-11: row 58 In stock never negative - show 0.
                        FreeStock = Math.Max(0m, ResolveFreeStock(ctx, productId, warehouseId)),
                        Rate = dr["Price_Actual"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Price_Actual"]),
                        Amount = dr["Line_Net_Amt"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Line_Net_Amt"])
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
        /// QtyDedicated - QtyAllocated, summed across the warehouse's active
        /// locators (M_Storage joined to M_Locator). A product/warehouse with no
        /// M_Storage rows reads as zero, never null.
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

            // "s" (M_Storage) is the main physical table here - M_Locator is only joined
            // to scope by warehouse (Prompt_Instructions "Case 1").
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "s", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            return Util.GetValueOfDecimal(DB.ExecuteScalar(sql, new[]
            {
                new SqlParameter("@M_Product_ID", productId),
                new SqlParameter("@M_Warehouse_ID", warehouseId)
            }, null));
        }

        /// <summary>The tenant's own label for a stored code, or the code itself when the dictionary carries no entry (never invented English).</summary>
        private static string DecodeLabel(Dictionary<string, string> map, string code)
        {
            if (string.IsNullOrEmpty(code)) { return ""; }
            string label;
            return map != null && map.TryGetValue(code, out label) ? label : code;
        }

        /// <summary>
        /// AD_Ref_List(_Trl) Code -&gt; Label map for one column, cached per
        /// column+language - never a hard-coded code table (Prompt_Instructions
        /// "Fetch Reference List Values for a Column").
        /// </summary>
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
                    // A decode-map lookup must never take the whole endpoint down - worst
                    // case the raw stored code is shown instead of its label
                    // (DecodeLabel's own fallback).
                    Log.Log(Level.SEVERE, "VAS_275_SOsOnCreditHoldWidget.GetDecodeMap(" + tableName + "." + columnName + ")", ex);
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

        /// <summary>Message text for a key, or the English fallback when the key is not released (never a raw "[KEY]").</summary>
        private static string GetText(Ctx ctx, string key, string fallback)
        {
            string text = Msg.GetMsg(ctx, key, true); // VAS163 2026-11-11: text-only label (3-arg GetMsg, no tip appended)
            if (string.IsNullOrEmpty(text) || text == key || text == "[" + key + "]") { return fallback; }
            return text;
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
        /// VAS163 2026-11-11: rows 15/62-68 C_Currency_ID of the login client's primary
        /// accounting schema (same lookup as <see cref="FillBaseCurrency"/>), used to convert
        /// open invoice amounts. Falls back to the login context currency.
        /// </summary>
        private static int GetBaseCurrencyId(Ctx ctx)
        {
            int currencyId = Util.GetValueOfInt(DB.ExecuteScalar(@"
                SELECT cs.C_Currency_ID
                  FROM AD_ClientInfo ci
                 INNER JOIN C_AcctSchema cs ON ( cs.C_AcctSchema_ID = ci.C_AcctSchema1_ID )
                 WHERE ci.AD_Client_ID = @AD_Client_ID",
                new[] { new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()) }, null));
            if (currencyId <= 0) { currencyId = ctx.GetContextAsInt("$C_Currency_ID"); }
            return currencyId;
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

        private class SummaryResult : CurrencyPayload
        {
            public int HeldCount { get; set; }
            public decimal BlockedValue { get; set; }
            public int CustomerCount { get; set; }
            public int OldestHeldSoDays { get; set; }
        }

        private class OrdersResult
        {
            public int Total { get; set; }
            public List<OrderRow> Rows { get; set; }
        }

        private class OrderRow
        {
            public int SalesOrderId { get; set; }
            public string SalesOrderNumber { get; set; }
            public string SalesOrderDate { get; set; }
            public string CustomerName { get; set; }
            public decimal CreditLimit { get; set; }
            public decimal Outstanding { get; set; }
            public decimal OrderValue { get; set; }
            /// <summary>VAS163 2026-11-11: Row 93 - document (price list) currency of OrderValue.</summary>
            public string CurrencySymbol { get; set; }
            public string CurrencyIso { get; set; }
            public int CurrencyPrecision { get; set; }
            public int OverdueInvoiceCount { get; set; }
            public decimal OverdueAmount { get; set; }
            public string HoldReason { get; set; }
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
            public string DocumentStatusCode { get; set; }
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
            public decimal QtyOrdered { get; set; }
            public decimal QtyDelivered { get; set; }
            public decimal QtyPending { get; set; }
            public decimal FreeStock { get; set; }
            public decimal Rate { get; set; }
            public decimal Amount { get; set; }
        }
    }
}
