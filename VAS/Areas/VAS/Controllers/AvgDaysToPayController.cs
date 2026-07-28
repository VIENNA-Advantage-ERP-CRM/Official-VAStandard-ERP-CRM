using Newtonsoft.Json;
using System.Data;
using System.Web.Mvc;
using VAdvantage.Classes;
using VAdvantage.DataBase;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VIS.Controllers
{
    public class AvgDaysToPayController : Controller
    {
        /// <summary>
        /// Returns the weighted-average days-to-pay for the current quarter (amount-weighted),
        /// the same figure for the previous quarter, the day difference, and a display label
        /// such as "3 days faster than last quarter". Uses C_InvoicePaySchedule joined to
        /// C_AllocationLine and its settlement - either C_Payment or a cash-journal
        /// C_CashLine -> C_Cash - filtered to IsSoTrx = 'Y', DocStatus IN ('CO','CL').
        /// Quarter boundaries are derived from the client's configured fiscal calendar.
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetAvgDaysToPay()
        {
            if (Session["ctx"] == null)
            {
                return Json(new { error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired" }, JsonRequestBehavior.AllowGet);
            }


            Ctx ctx = Session["ctx"] as Ctx;

            /* Base (accounting-schema) currency for this client — the allocation amount is
               amount-weighted in this currency. Fetched as a scalar and inlined as an integer
               literal so it is never referenced as a CTE alias inside the MRole-wrapped body
               (the access parser cannot resolve CTE aliases as physical tables). */
            string baseCurrencySql = @"SELECT acc.C_Currency_ID
                FROM AD_ClientInfo ci
                INNER JOIN C_AcctSchema acc ON (ci.C_AcctSchema1_ID=acc.C_AcctSchema_ID)
                WHERE ci.AD_Client_ID=" + ctx.GetAD_Client_ID();
            int baseCurrencyId = DB.GetSQLValue(null, baseCurrencySql);

            /* Allocation amount converted from the ALLOCATION's own currency to the base currency,
               using the allocation header's DateAcct as the conversion date and its
               C_ConversionType_ID, with the client/org taken from the allocation line. When the
               allocation is already in base currency — or the base currency cannot be resolved — the
               raw line amount is used as-is. */
            string allocAmountExpr = baseCurrencyId > 0
                ? @"(CASE WHEN ah.C_Currency_ID=" + baseCurrencyId + @" THEN COALESCE(al.Amount, 0)
                        ELSE CurrencyConvert(COALESCE(al.Amount, 0), ah.C_Currency_ID, " + baseCurrencyId + @", ah.DateAcct, ah.C_ConversionType_ID, al.AD_Client_ID, al.AD_Org_ID) END)"
                : "COALESCE(al.Amount, 0)";

            string currentPeriodDateCondition = "";
            string daysToPayCondition = "";
            
            if (DB.IsPostgreSQL())
            {
                currentPeriodDateCondition = " CAST(CURRENT_DATE AS DATE) BETWEEN CAST(p.StartDate AS DATE) AND CAST(p.EndDate AS DATE) ";
                /* PostgreSQL: timestamp - timestamp yields an INTERVAL; date_part('epoch', ...)/86400
                   converts it to fractional days. Cast to NUMERIC so the downstream
                   ROUND(...) is valid (PG has no ROUND(double precision, integer)).
                   NOTE: date_part('epoch', ...) is used instead of the equivalent
                   EXTRACT(EPOCH FROM ...) on purpose. This expression sits in the SELECT
                   list of paymentsSql, which is passed to MRole.AddAccessSQL. The core
                   parses the SQL for its main FROM clause to anchor the access filter; the
                   FROM token inside EXTRACT(...) would be mistaken for that clause and break
                   MRole injection. date_part(...) carries no FROM keyword and avoids this. */
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
                WHERE ci.AD_Client_ID=" + ctx.GetAD_Client_ID() + @"
                AND " + currentPeriodDateCondition + @"
                FETCH FIRST 1 ROW ONLY";

            /* Per-payment facts for completed AR invoices. ONLY physical tables are joined
               here so MRole.AddAccessSQL receives a clean query. The fiscal-period compare
               (CurrentPeriod) is intentionally NOT cross-joined inside this MRole-wrapped
               body — doing so passed a CTE alias to AddAccessSQL, which the core could not
               resolve as a physical table and led to a runaway/OutOfMemoryException. */
            /* Settlement date = the payment's DateAcct, or - for cash-journal settlements
               (C_AllocationLine.C_CashLine_ID) - the parent C_Cash.DateAcct. Both are joined
               as LEFT so an allocation settled either way contributes; the active flags (and,
               for cash, DocStatus <> 'VO' to exclude voided journals) are pushed into the ON so
               an inactive/voided payment/cash yields NULL and is dropped by the "settlement
               date IS NOT NULL" guard (this preserves the original payment-only behaviour
               exactly and just ADDS the non-voided cash rows). */
            string paymentsSql = @"SELECT CAST(TO_CHAR(COALESCE(pay.DateAcct, csh.DateAcct), 'Q') AS NUMERIC) AS PayQuarter,
                       CAST(TO_CHAR(COALESCE(pay.DateAcct, csh.DateAcct), 'YYYY') AS NUMERIC) AS PayYear,
                       " + daysToPayCondition + @" AS Days_To_Pay,
                       " + allocAmountExpr + @" AS Amount
                FROM C_Invoice i
                INNER JOIN C_InvoicePaySchedule ips ON (ips.C_Invoice_ID=i.C_Invoice_ID)
                INNER JOIN C_AllocationLine al ON (al.C_InvoicePaySchedule_ID=ips.C_InvoicePaySchedule_ID)
                INNER JOIN C_AllocationHdr ah ON (ah.C_AllocationHdr_ID=al.C_AllocationHdr_ID)
                LEFT JOIN C_Payment pay ON (pay.C_Payment_ID=al.C_Payment_ID AND pay.IsActive='Y')
                LEFT JOIN C_CashLine cl ON (cl.C_CashLine_ID=al.C_CashLine_ID AND cl.IsActive='Y')
                LEFT JOIN C_Cash csh ON (csh.C_Cash_ID=cl.C_Cash_ID AND csh.IsActive='Y' AND csh.DocStatus NOT IN ('VO'))
                WHERE i.IsSoTrx='Y' AND i.IsReturnTrx = 'N' 
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

            /* CurrentPeriod (single row) is cross-joined in a separate, MRole-free CTE to
               classify each payment as Current/Previous quarter. The final aggregate then
               produces the amount-weighted average days-to-pay per quarter. The quarter
               difference is derived on the server side (C#) to keep the SQL lean. */
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
            int diffDays = 0;
            string displayText = Msg.GetMsg(ctx, "NoChange") ?? "No change";

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql);
                if (dr != null && dr.Read())
                {
                    currentAvg = Util.GetValueOfInt(dr["Current_Quarter_Avg_Days"]);
                    previousAvg = Util.GetValueOfInt(dr["Previous_Quarter_Avg_Days"]);
                    diffDays = currentAvg - previousAvg;

                    if (diffDays < 0)
                    {
                        displayText = System.Math.Abs(diffDays).ToString() + (Msg.GetMsg(ctx, "VAS_DaysFasterThanLastQuarter") ?? " days faster than last quarter");
                    }
                    else if (diffDays > 0)
                    {
                        displayText = diffDays.ToString() + (Msg.GetMsg(ctx, "VAS_DaysSlowerThanLastQuarter") ?? " days slower than last quarter");
                    }
                    else
                    {
                        displayText = Msg.GetMsg(ctx, "VIS_NoChange") ?? "No change";
                    }
                }
            }
            finally
            {
                if (dr != null)
                {
                    dr.Close();
                }
            }

            var result = new
            {
                currentAvgDays = currentAvg,
                previousAvgDays = previousAvg,
                differenceDays = diffDays,
                displayText = displayText
            };

            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }
    }
}
