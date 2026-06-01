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
        /// C_AllocationLine and C_Payment, filtered to IsSoTrx = 'Y', DocStatus IN ('CO','CL').
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
                daysToPayCondition = " GREATEST(CAST(date_part('epoch', (CAST(pay.DateAcct AS TIMESTAMP) - CAST(ips.DueDate AS TIMESTAMP))) / 86400 AS NUMERIC), 0) ";
            }
            else
            {
                currentPeriodDateCondition = " TRUNC(SYSDATE) BETWEEN TRUNC(p.StartDate) AND TRUNC(p.EndDate) ";
                /* Oracle: DATE - DATE already returns the number of days. */
                daysToPayCondition = " GREATEST(TRUNC(pay.DateAcct) - TRUNC(ips.DueDate), 0) ";
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
            string paymentsSql = @"SELECT CAST(TO_CHAR(pay.DateAcct, 'Q') AS NUMERIC) AS PayQuarter,
                       CAST(TO_CHAR(pay.DateAcct, 'YYYY') AS NUMERIC) AS PayYear,
                       " + daysToPayCondition + @" AS Days_To_Pay,
                       COALESCE(al.Amount, 0) AS Amount
                FROM C_Invoice i
                INNER JOIN C_InvoicePaySchedule ips ON (ips.C_Invoice_ID=i.C_Invoice_ID)
                INNER JOIN C_AllocationLine al ON (al.C_InvoicePaySchedule_ID=ips.C_InvoicePaySchedule_ID)
                INNER JOIN C_Payment pay ON (pay.C_Payment_ID=al.C_Payment_ID)
                WHERE i.IsSoTrx='Y'
                AND i.DocStatus IN ('CO', 'CL')
                AND al.C_Payment_ID IS NOT NULL
                AND i.IsActive='Y'
                AND ips.IsActive='Y'
                AND al.IsActive='Y'
                AND pay.IsActive='Y'";

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
