/************************************************************
 * Module Name    : VAS
 * Purpose        : Controller for the AP "Avg Days to Pay" KPI Widget — the payables
 *                  analog of AvgDaysToPayController (AR). Serves the amount-weighted
 *                  average number of days we take to pay suppliers this quarter vs last.
 * chronological  : Development
 * Created Date   : 13 May 2026
 * Created by     : Humam Yousif
 ***********************************************************/
using CoreLibrary.DataBase;
using Newtonsoft.Json;
using System.Data;
using System.Data.SqlClient;
using System.Web.Mvc;
using VAdvantage.Model;
using VAdvantage.Utility;

namespace VAS.Areas.VAS.Controllers
{
    public class VAS_016_AvgPaymentPeriodWidgetController : Controller
    {
        [Authorize]
        public ActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// AP-side "Avg Days to Pay" — the payables analog of AvgDaysToPayController (AR). Returns the
        /// amount-weighted average number of days WE take to pay suppliers for the current fiscal
        /// quarter, the same figure for the previous quarter, and the day difference. Uses
        /// C_InvoicePaySchedule -> C_AllocationLine and its settlement (a vendor C_Payment with
        /// IsReceipt='N', or a cash-journal C_CashLine -> C_Cash), filtered to IsSOTrx='N',
        /// DocStatus IN ('CO','CL'). Quarter boundaries come from the client's configured fiscal
        /// calendar. MRole is applied only on the main physical table (C_Invoice). Oracle/PostgreSQL
        /// compatible. The widget composes the "faster/slower than last quarter" wording from the
        /// returned numbers, so no server-side message lookup is needed here.
        /// </summary>
        public JsonResult GetAvgDaysToPay()
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                int clientId = ctx.GetAD_Client_ID();

                /* Base (accounting-schema) currency for this client — the allocation amount is
                   amount-weighted in this currency. Fetched as a scalar and inlined as an integer
                   literal so it is never referenced as a CTE alias inside the MRole-wrapped body
                   (the access parser cannot resolve CTE aliases as physical tables). */
                int baseCurrencyId = 0;
                string baseCurrencySql = @"SELECT acc.C_Currency_ID AS Base_Currency_ID
                    FROM AD_ClientInfo ci
                    INNER JOIN C_AcctSchema acc ON (ci.C_AcctSchema1_ID=acc.C_AcctSchema_ID)
                    WHERE ci.AD_Client_ID=" + clientId;
                DataSet baseCurrencyDs = DB.ExecuteDataset(baseCurrencySql, new SqlParameter[] { }, null);
                if (baseCurrencyDs != null && baseCurrencyDs.Tables.Count > 0 && baseCurrencyDs.Tables[0].Rows.Count > 0)
                {
                    baseCurrencyId = Util.GetValueOfInt(baseCurrencyDs.Tables[0].Rows[0]["Base_Currency_ID"]);
                }

                /* Allocation amount converted from the ALLOCATION's own currency to the base
                   currency, using the allocation header's DateAcct as the conversion date and its
                   C_ConversionType_ID, with the client/org taken from the allocation line (per the
                   requirement). When the allocation is already in base currency — or the base currency
                   cannot be resolved — the raw line amount is used as-is. */
                string allocAmountExpr = baseCurrencyId > 0
                    ? @"(CASE WHEN ah.C_Currency_ID=" + baseCurrencyId + @" THEN COALESCE(al.Amount, 0)
                            ELSE CurrencyConvert(COALESCE(al.Amount, 0), ah.C_Currency_ID, " + baseCurrencyId + @", ah.DateAcct, ah.C_ConversionType_ID, al.AD_Client_ID, al.AD_Org_ID) END)"
                    : "COALESCE(al.Amount, 0)";

                string currentPeriodDateCondition;
                string daysToPayCondition;
                if (DB.IsPostgreSQL())
                {
                    currentPeriodDateCondition = " CAST(CURRENT_DATE AS DATE) BETWEEN CAST(p.StartDate AS DATE) AND CAST(p.EndDate AS DATE) ";
                    /* PostgreSQL: timestamp - timestamp yields an INTERVAL; date_part('epoch', ...)/86400
                       converts it to whole NUMERIC days. date_part(...) is used instead of EXTRACT(EPOCH
                       FROM ...) so the SELECT list carries no stray FROM keyword that would confuse
                       MRole.AddAccessSQL's FROM parsing. */
                    daysToPayCondition = " GREATEST(CAST(date_part('epoch', (CAST(COALESCE(pay.DateAcct, csh.DateAcct) AS TIMESTAMP) - CAST(i.DateInvoiced AS TIMESTAMP))) / 86400 AS NUMERIC), 0) ";
                }
                else
                {
                    currentPeriodDateCondition = " TRUNC(SYSDATE) BETWEEN TRUNC(p.StartDate) AND TRUNC(p.EndDate) ";
                    /* Oracle: DATE - DATE already returns the number of days. */
                    daysToPayCondition = " GREATEST(TRUNC(COALESCE(pay.DateAcct, csh.DateAcct)) - TRUNC(i.DateInvoiced), 0) ";
                }

                string currentPeriodSql = @"
                    SELECT CAST(TO_CHAR(p.StartDate, 'Q') AS NUMERIC) AS CurrentQuarter,
                           CAST(TO_CHAR(p.StartDate, 'YYYY') AS NUMERIC) AS CurrentYear
                    FROM AD_ClientInfo ci
                    INNER JOIN C_Calendar cal ON (ci.C_Calendar_ID=cal.C_Calendar_ID)
                    INNER JOIN C_Year yr ON (cal.C_Calendar_ID=yr.C_Calendar_ID)
                    INNER JOIN C_Period p ON (yr.C_Year_ID=p.C_Year_ID)
                    WHERE ci.AD_Client_ID=" + clientId + @"
                    AND " + currentPeriodDateCondition + @"
                    FETCH FIRST 1 ROW ONLY";

                /* Per-payment facts for completed AP invoices. ONLY physical tables are joined here so
                   MRole.AddAccessSQL receives a clean query (the fiscal-period compare is cross-joined
                   later, outside MRole scope). Settlement date = the vendor payment's DateAcct or — for
                   cash-journal settlements — the parent C_Cash.DateAcct. Both LEFT-joined so an
                   allocation settled either way contributes; the receipt-side payments and voided cash
                   journals fall out via the "settlement date IS NOT NULL" guard. */
                string paymentsSql = @"SELECT CAST(TO_CHAR(COALESCE(pay.DateAcct, csh.DateAcct), 'Q') AS NUMERIC) AS PayQuarter,
                           CAST(TO_CHAR(COALESCE(pay.DateAcct, csh.DateAcct), 'YYYY') AS NUMERIC) AS PayYear,
                           " + daysToPayCondition + @" AS Days_To_Pay,
                           " + allocAmountExpr + @" AS Amount
                    FROM C_Invoice i
                    INNER JOIN C_InvoicePaySchedule ips ON (ips.C_Invoice_ID=i.C_Invoice_ID)
                    INNER JOIN C_AllocationLine al ON (al.C_InvoicePaySchedule_ID=ips.C_InvoicePaySchedule_ID)
                    INNER JOIN C_AllocationHdr ah ON (ah.C_AllocationHdr_ID=al.C_AllocationHdr_ID)
                    LEFT JOIN C_Payment pay ON (pay.C_Payment_ID=al.C_Payment_ID AND pay.IsActive='Y' AND pay.IsReceipt='N')
                    LEFT JOIN C_CashLine cl ON (cl.C_CashLine_ID=al.C_CashLine_ID AND cl.IsActive='Y')
                    LEFT JOIN C_Cash csh ON (csh.C_Cash_ID=cl.C_Cash_ID AND csh.IsActive='Y' AND csh.DocStatus NOT IN ('VO'))
                    WHERE i.IsSoTrx='N' AND i.IsReturnTrx = 'N'
                    AND i.DocStatus IN ('CO', 'CL')
                    AND (al.C_Payment_ID IS NOT NULL OR al.C_CashLine_ID IS NOT NULL)
                    AND i.IsActive='Y'
                    AND ips.IsActive='Y'
                    AND al.IsActive='Y'
                    AND COALESCE(pay.DateAcct, csh.DateAcct) IS NOT NULL";

                /* MRole only on the main physical table (C_Invoice, alias i) in the CTE body. */
                paymentsSql = MRole.GetDefault(ctx).AddAccessSQL(
                    paymentsSql,
                    "i",
                    MRole.SQL_FULLYQUALIFIED,
                    MRole.SQL_RO
                );

                string sql = @"
                    WITH CurrentPeriod AS (
                        " + currentPeriodSql + @"
                    ),
                    Payments AS (
                        " + paymentsSql + @"
                    ),
                    ClassifiedPayments AS (
                        SELECT p.Days_To_Pay,
                               p.Amount,
                               CASE
                                   WHEN p.PayQuarter=cp.CurrentQuarter AND p.PayYear=cp.CurrentYear THEN 'Current'
                                   WHEN p.PayQuarter=(CASE WHEN cp.CurrentQuarter=1 THEN 4 ELSE cp.CurrentQuarter - 1 END)
                                   AND ((cp.CurrentQuarter > 1 AND p.PayYear=cp.CurrentYear)
                                   OR (cp.CurrentQuarter=1 AND p.PayYear=cp.CurrentYear - 1)) THEN 'Previous'
                               END AS QuarterFlag
                        FROM Payments p
                        CROSS JOIN CurrentPeriod cp
                    )
                    SELECT ROUND(COALESCE(SUM(CASE WHEN QuarterFlag='Current' THEN Days_To_Pay * Amount ELSE 0 END) / NULLIF(SUM(CASE WHEN QuarterFlag='Current' THEN Amount ELSE 0 END), 0), 0), 0) AS Current_Quarter_Avg_Days,
                           ROUND(COALESCE(SUM(CASE WHEN QuarterFlag='Previous' THEN Days_To_Pay * Amount ELSE 0 END) / NULLIF(SUM(CASE WHEN QuarterFlag='Previous' THEN Amount ELSE 0 END), 0), 0), 0) AS Previous_Quarter_Avg_Days
                    FROM ClassifiedPayments
                    WHERE QuarterFlag IS NOT NULL";

                int currentAvg = 0;
                int previousAvg = 0;
                DataSet ds = DB.ExecuteDataset(sql, new SqlParameter[] { }, null);
                if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                {
                    currentAvg = Util.GetValueOfInt(ds.Tables[0].Rows[0]["Current_Quarter_Avg_Days"]);
                    previousAvg = Util.GetValueOfInt(ds.Tables[0].Rows[0]["Previous_Quarter_Avg_Days"]);
                }

                var result = new
                {
                    currentAvgDays = currentAvg,
                    previousAvgDays = previousAvg,
                    differenceDays = currentAvg - previousAvg
                };
                retJSON = JsonConvert.SerializeObject(result);
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
    }
}
