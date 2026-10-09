/******************************************************
 * Module Name    : VAS
 * Purpose        : Customers module Delayed Payments triage list widget endpoints
 * chronological  : Development
 * Created Date   : 2026-07-22
 * Created by     : VAI052
 ******************************************************/

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Web.Mvc;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VAS.Controllers
{
    /// <summary>
    /// Module Name : VAS_138_DelayedPaymentsListWidget
    /// Purpose     : 3x2 triage list - customers with overdue receivable payment
    ///               schedules, ranked by outstanding amount. Each row shows the
    ///               customer, invoice number, days overdue and the outstanding
    ///               amount; the header shows distinct overdue customers and the
    ///               total overdue amount. Overdue = C_InvoicePaySchedule rows still
    ///               unpaid (VA009_IsPaid='N') whose DueDate is before today, on
    ///               completed/closed customer sales invoices (DocStatus IN
    ///               ('CO','CL'), IsSOTrx='Y', IsReturnTrx='N'). The schedule
    ///               outstanding is DueAmt - VA009_PaidAmntInvce - VA009_Variance
    ///               (correct for partial payments; VA009_OpenAmnt is not maintained
    ///               in this deployment) converted to the tenant accounting currency
    ///               via CurrencyConvert, so multi-currency receivables share one
    ///               correct base figure that reconciles with the VAS_124 KPI.
    ///               MRole (tenant + org + record access) is applied to the main
    ///               physical table C_InvoicePaySchedule only.
    /// Chronological development:
    ///   VAI052      2026-07-22 Created
    ///   VAI052      2026-08-10 GetRows now returns one row per customer in arrears
    ///                          (invoice count, worst days overdue, summed outstanding)
    ///                          instead of one row per payment-schedule instalment, so
    ///                          the list length matches the overdue customer count the
    ///                          KPI tile and summary header already report
    /// </summary>
    public class VAS_138_DelayedPaymentsListWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_138_DelayedPaymentsListWidgetController).FullName);

        private const int WidgetPageSize = 7;
        private const int MaxListPageSize = 25;

        /// <summary>Single-row tenant accounting currency (symbol, ISO, precision).</summary>
        private const string SchemaCurrencySql = @"
            SELECT ci.AD_Client_ID AS AD_Client_ID,
                   cs.C_Currency_ID AS Acct_Currency_ID,
                   cur.StdPrecision AS Std_Precision,
                   cur.ISO_Code AS ISO_Code,
                   CASE WHEN cur.CurSymbol IS NOT NULL THEN cur.CurSymbol ELSE cur.ISO_Code END AS Cur_Symbol
            FROM AD_ClientInfo ci
            INNER JOIN C_AcctSchema cs ON (cs.C_AcctSchema_ID=ci.C_AcctSchema1_ID AND cs.IsActive = 'Y')
            INNER JOIN C_Currency cur ON (cur.C_Currency_ID=cs.C_Currency_ID AND cur.IsActive = 'Y')
            WHERE ci.IsActive = 'Y'
              AND ci.AD_Client_ID = @Client_ID";

        /// <summary>Outstanding installment amount, correct for partial payments.</summary>
        private const string OpenAmtExpr =
            "(COALESCE(ips.DueAmt, 0) - COALESCE(ips.VA009_PaidAmntInvce, 0) - COALESCE(ips.VA009_Variance, 0))";

        /// <summary>Outstanding installment converted to the tenant accounting currency.</summary>
        private const string BaseAmtExpr =
            "CurrencyConvert(" + OpenAmtExpr + ", i.C_Currency_ID, sc.Acct_Currency_ID, i.DateAcct, i.C_ConversionType_ID, i.AD_Client_ID, i.AD_Org_ID)";

        /// <summary>
        /// Overdue = due strictly before today. Dialect-specific date truncation keeps
        /// the comparison stable across a DueDate that may carry a time component
        /// (mirrors VAS_124 / OverdueController).
        /// </summary>
        private string OverdueDateCondition()
        {
            return DB.IsPostgreSQL()
                ? " AND CAST(ips.DueDate AS DATE) < CAST(CURRENT_DATE AS DATE)"
                : " AND TRUNC(ips.DueDate) < TRUNC(SYSDATE)";
        }

        /// <summary>
        /// Tenant-local "today" as a DATE, dialect-specific. Returned as its own
        /// column so overdue days are computed in C# (DateTime - DateTime); doing the
        /// day subtraction in SQL yields an interval that the data provider maps to a
        /// System.TimeSpan, which Util.GetValueOfInt cannot convert.
        /// </summary>
        private string TodayDateExpr()
        {
            return DB.IsPostgreSQL()
                ? "CAST(CURRENT_DATE AS DATE)"
                : "TRUNC(SYSDATE)";
        }

        /// <summary>
        /// The shared FROM/WHERE for the overdue-receivable population. Every row is a
        /// still-unpaid schedule, past due, on a completed customer sales invoice with
        /// a positive rounded outstanding. Alias "ips" is the MRole physical table.
        /// </summary>
        private string OverdueFromWhere()
        {
            return @"
                FROM C_InvoicePaySchedule ips
                INNER JOIN C_Invoice i ON (ips.C_Invoice_ID=i.C_Invoice_ID AND i.IsActive = 'Y')
                INNER JOIN C_BPartner bp ON (bp.C_BPartner_ID=i.C_BPartner_ID AND bp.AD_Client_ID=i.AD_Client_ID AND bp.IsActive = 'Y' AND bp.IsCustomer = 'Y')
                INNER JOIN schema_currency sc ON (sc.AD_Client_ID=i.AD_Client_ID)
                WHERE ips.IsActive = 'Y'
                  AND ips.VA009_IsPaid = 'N'" + OverdueDateCondition() + @"
                  AND i.DocStatus IN ('CO', 'CL')
                  AND i.IsSOTrx = 'Y'
                  AND i.IsReturnTrx = 'N'
                  AND i.AD_Client_ID = @Client_ID
                  AND ROUND(" + OpenAmtExpr + ", COALESCE(sc.Std_Precision, 2)) > 0";
        }

        /// <summary>
        /// Header summary: distinct overdue customers, distinct invoices, schedule
        /// count and the total outstanding in the tenant accounting currency.
        /// </summary>
        /// <returns>
        /// JSON { overdue_amount, overdue_customer_count, overdue_invoice_count,
        /// overdue_schedule_count, currency_symbol, currency_iso, std_precision } or { error }.
        /// </returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetSummary()
        {
            if (Session["ctx"] == null)
            {
                return Json(new { error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired" }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            try
            {
                // Aggregate the overdue figures in one subquery. An aggregate with no
                // GROUP BY always yields exactly one row, so CROSS JOINing it with the
                // single-row currency CTE always carries the currency - even at zero.
                string overdueSql = @"
                    SELECT COALESCE(SUM(" + BaseAmtExpr + @"), 0) AS Total_Raw,
                           COUNT(DISTINCT i.C_BPartner_ID) AS Overdue_Customer_Count,
                           COUNT(DISTINCT i.C_Invoice_ID) AS Overdue_Invoice_Count,
                           COUNT(*) AS Overdue_Schedule_Count
                    " + OverdueFromWhere();

                // MRole on the main physical table alias "ips" only (CTE rule: main
                // data source, not the C_Invoice/C_BPartner joins or the currency CTE).
                overdueSql = MRole.GetDefault(ctx).AddAccessSQL(overdueSql, "ips", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                string sql = @"
                    WITH schema_currency AS (
                        " + SchemaCurrencySql + @"
                    ),
                    overdue AS (
                        " + overdueSql + @"
                    )
                    SELECT ROUND(o.Total_Raw, sc.Std_Precision) AS Overdue_Amount,
                           o.Overdue_Customer_Count AS Overdue_Customer_Count,
                           o.Overdue_Invoice_Count AS Overdue_Invoice_Count,
                           o.Overdue_Schedule_Count AS Overdue_Schedule_Count,
                           sc.Cur_Symbol AS Cur_Symbol,
                           sc.ISO_Code AS ISO_Code,
                           sc.Std_Precision AS Std_Precision
                    FROM schema_currency sc
                    CROSS JOIN overdue o";

                decimal overdueAmount = 0;
                int customerCount = 0, invoiceCount = 0, scheduleCount = 0;
                string currencySymbol = "", isoCode = "";
                int stdPrecision = 2;

                IDataReader dr = null;
                try
                {
                    dr = DB.ExecuteReader(sql, new SqlParameter[] { new SqlParameter("@Client_ID", ctx.GetAD_Client_ID()) });
                    if (dr != null && dr.Read())
                    {
                        overdueAmount = Util.GetValueOfDecimal(dr["Overdue_Amount"]);
                        customerCount = Util.GetValueOfInt(dr["Overdue_Customer_Count"]);
                        invoiceCount = Util.GetValueOfInt(dr["Overdue_Invoice_Count"]);
                        scheduleCount = Util.GetValueOfInt(dr["Overdue_Schedule_Count"]);
                        currencySymbol = Util.GetValueOfString(dr["Cur_Symbol"]);
                        isoCode = Util.GetValueOfString(dr["ISO_Code"]);
                        if (dr["Std_Precision"] != null && dr["Std_Precision"] != DBNull.Value)
                        {
                            stdPrecision = Util.GetValueOfInt(dr["Std_Precision"]);
                        }
                    }
                }
                finally
                {
                    CloseReader(dr);
                }

                var result = new
                {
                    overdue_amount = overdueAmount,
                    overdue_customer_count = customerCount,
                    overdue_invoice_count = invoiceCount,
                    overdue_schedule_count = scheduleCount,
                    currency_symbol = currencySymbol,
                    currency_iso = isoCode,
                    std_precision = stdPrecision
                };
                return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_138_DelayedPaymentsListWidget.GetSummary", ex);
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// One row per CUSTOMER in arrears - not per payment-schedule instalment - so
        /// the list length and its "total" equal the distinct overdue customer count
        /// the KPI tile and the summary header report. Each row aggregates that
        /// customer's overdue AR invoices: how many invoices are overdue, the worst
        /// (oldest) overdue due date, and the summed outstanding. Ranked by outstanding
        /// (desc), then oldest due date, then name, then id (stable paging). The
        /// outstanding is converted to the tenant accounting currency so every row
        /// shares one currency and the sort is meaningful.
        /// </summary>
        /// <param name="offset">Zero-based paging offset.</param>
        /// <param name="limit">Page size; 7 for the widget, up to 25 for the full list.</param>
        /// <returns>JSON { items:[...], total, offset, limit, currency_* } or { error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetRows(int offset = 0, int limit = WidgetPageSize)
        {
            if (Session["ctx"] == null)
            {
                return Json(new { error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired" }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            if (offset < 0) { offset = 0; }
            if (limit <= 0 || limit > MaxListPageSize) { limit = WidgetPageSize; }

            try
            {
                // Instalment-level projection over the shared overdue population; it is
                // rolled up to one row per customer below. MRole on "ips". No ORDER BY
                // here - AddAccessSQL splices its predicate ahead of the first ORDER BY
                // it finds, so the sort must live in the outer query.
                string overdueRowsSql = @"
                    SELECT bp.C_BPartner_ID AS Customer_Id,
                           bp.Name AS Customer_Name,
                           i.C_Invoice_ID AS Invoice_Id,
                           ips.DueDate AS Due_Date,
                           ROUND(" + BaseAmtExpr + @", sc.Std_Precision) AS Overdue_Amt
                    " + OverdueFromWhere();
                overdueRowsSql = MRole.GetDefault(ctx).AddAccessSQL(overdueRowsSql, "ips", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                string sql = @"
                    WITH schema_currency AS (
                        " + SchemaCurrencySql + @"
                    ),
                    overdue_rows AS (
                        " + overdueRowsSql + @"
                    ),
                    by_customer AS (
                        SELECT orr.Customer_Id AS Customer_Id,
                               orr.Customer_Name AS Customer_Name,
                               COUNT(DISTINCT orr.Invoice_Id) AS Invoice_Count,
                               MIN(orr.Due_Date) AS Oldest_Due_Date,
                               SUM(orr.Overdue_Amt) AS Overdue_Amt
                        FROM overdue_rows orr
                        GROUP BY orr.Customer_Id, orr.Customer_Name
                    )
                    SELECT c.Customer_Id,
                           c.Customer_Name,
                           c.Invoice_Count,
                           c.Oldest_Due_Date,
                           " + TodayDateExpr() + @" AS As_Of_Date,
                           c.Overdue_Amt,
                           sc.Cur_Symbol AS Cur_Symbol,
                           sc.ISO_Code AS ISO_Code,
                           sc.Std_Precision AS Std_Precision,
                           COUNT(1) OVER () AS Total_Rows
                    FROM by_customer c
                    CROSS JOIN schema_currency sc
                    ORDER BY c.Overdue_Amt DESC,
                             c.Oldest_Due_Date ASC,
                             c.Customer_Name ASC,
                             c.Customer_Id ASC
                    OFFSET " + offset + @" ROWS FETCH NEXT " + limit + @" ROWS ONLY";

                List<object> items = new List<object>();
                int total = 0;
                string currencySymbol = "", isoCode = "";
                int stdPrecision = 2;

                IDataReader dr = null;
                try
                {
                    dr = DB.ExecuteReader(sql, new SqlParameter[] { new SqlParameter("@Client_ID", ctx.GetAD_Client_ID()) });
                    while (dr != null && dr.Read())
                    {
                        total = Util.GetValueOfInt(dr["Total_Rows"]);
                        currencySymbol = Util.GetValueOfString(dr["Cur_Symbol"]);
                        isoCode = Util.GetValueOfString(dr["ISO_Code"]);
                        if (dr["Std_Precision"] != null && dr["Std_Precision"] != DBNull.Value)
                        {
                            stdPrecision = Util.GetValueOfInt(dr["Std_Precision"]);
                        }
                        DateTime? dueDate = dr["Oldest_Due_Date"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr["Oldest_Due_Date"]);
                        DateTime? asOfDate = dr["As_Of_Date"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr["As_Of_Date"]);
                        // Worst (largest) days overdue for this customer, which is the
                        // age of their OLDEST overdue due date. Computed in C# to avoid
                        // the SQL date subtraction returning an interval/TimeSpan.
                        int overdueDays = 0;
                        if (dueDate.HasValue && asOfDate.HasValue)
                        {
                            overdueDays = (int)(asOfDate.Value.Date - dueDate.Value.Date).TotalDays;
                            if (overdueDays < 0) { overdueDays = 0; }
                        }
                        items.Add(new
                        {
                            customerId = Util.GetValueOfInt(dr["Customer_Id"]),
                            customerName = Util.GetValueOfString(dr["Customer_Name"]),
                            invoiceCount = Util.GetValueOfInt(dr["Invoice_Count"]),
                            oldestDueDate = dueDate.HasValue ? dueDate.Value.ToString("yyyy-MM-dd") : "",
                            overdueDays = overdueDays,
                            overdueAmt = Util.GetValueOfDecimal(dr["Overdue_Amt"])
                        });
                    }
                }
                finally
                {
                    CloseReader(dr);
                }

                var result = new
                {
                    items = items,
                    total = total,
                    offset = offset,
                    limit = limit,
                    currency_symbol = currencySymbol,
                    currency_iso = isoCode,
                    std_precision = stdPrecision
                };
                return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_138_DelayedPaymentsListWidget.GetRows", ex);
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        private void CloseReader(IDataReader reader)
        {
            if (reader == null) { return; }
            reader.Close();
            reader.Dispose();
        }
    }
}
