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
                return Json(new { error = "Session Expired" }, JsonRequestBehavior.AllowGet);
            }


            Ctx ctx = Session["ctx"] as Ctx;

            string currentPeriodDateCondition = "";
            string daysToPayCondition = "";
            
            if (DB.IsPostgreSQL())
            {
                currentPeriodDateCondition = " CAST(CURRENT_DATE AS DATE) BETWEEN CAST(p.StartDate AS DATE) AND CAST(p.EndDate AS DATE) ";
                daysToPayCondition = " GREATEST(CAST(pay.DateAcct AS DATE) - CAST(ips.DueDate AS DATE), 0) ";
            }
            else
            {
                currentPeriodDateCondition = " TRUNC(SYSDATE) BETWEEN TRUNC(p.StartDate) AND TRUNC(p.EndDate) ";
                daysToPayCondition = " GREATEST(TRUNC(pay.DateAcct) - TRUNC(ips.DueDate), 0) ";
            }

            string currentPeriodSql = @"
                SELECT p.C_Year_ID,
                       CAST(TO_CHAR(p.StartDate, 'Q') AS NUMERIC) AS CurrentQuarter,
                       CAST(TO_CHAR(p.StartDate, 'YYYY') AS NUMERIC) AS CurrentYear
                FROM AD_ClientInfo ci
                INNER JOIN C_Calendar cal ON (ci.C_Calendar_ID=cal.C_Calendar_ID)
                INNER JOIN C_Year yr ON (cal.C_Calendar_ID=yr.C_Calendar_ID)
                INNER JOIN C_Period p ON (yr.C_Year_ID=p.C_Year_ID)
                WHERE ci.AD_Client_ID=" + ctx.GetAD_Client_ID() + @"
                AND " + currentPeriodDateCondition + @"
                FETCH FIRST 1 ROW ONLY";

            string quarterDataSql = @"
                SELECT CASE
                           WHEN CAST(TO_CHAR(pay.DateAcct, 'Q') AS NUMERIC)=cp.CurrentQuarter
                           AND CAST(TO_CHAR(pay.DateAcct, 'YYYY') AS NUMERIC)=cp.CurrentYear THEN 'Current'
                           WHEN CAST(TO_CHAR(pay.DateAcct, 'Q') AS NUMERIC)=(CASE WHEN cp.CurrentQuarter=1 THEN 4 ELSE cp.CurrentQuarter - 1 END)
                           AND ((cp.CurrentQuarter > 1 AND CAST(TO_CHAR(pay.DateAcct, 'YYYY') AS NUMERIC)=cp.CurrentYear)
                           OR (cp.CurrentQuarter=1 AND CAST(TO_CHAR(pay.DateAcct, 'YYYY') AS NUMERIC)=cp.CurrentYear - 1)) THEN 'Previous'
                       END AS QuarterFlag,
                       " + daysToPayCondition + @" AS Days_To_Pay,
                       NVL(al.Amount, 0) AS Amount
                FROM C_Invoice i
                INNER JOIN C_InvoicePaySchedule ips ON (ips.C_Invoice_ID=i.C_Invoice_ID)
                INNER JOIN C_AllocationLine al ON (al.C_InvoicePaySchedule_ID=ips.C_InvoicePaySchedule_ID)
                INNER JOIN C_Payment pay ON (al.C_Payment_ID=pay.C_Payment_ID)
                CROSS JOIN CurrentPeriod cp
                WHERE i.IsSoTrx='Y'
                AND i.DocStatus IN ('CO', 'CL')
                AND al.C_Payment_ID IS NOT NULL
                AND i.IsActive='Y'
                AND ips.IsActive='Y'
                AND pay.IsActive='Y'";

            quarterDataSql = MRole.GetDefault(ctx).AddAccessSQL(
                quarterDataSql,
                "i",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            string quarterAggSql = @"
                SELECT QuarterFlag,
                       NVL(SUM(Days_To_Pay * Amount) / NULLIF(SUM(Amount), 0), 0) AS AvgDaysToPay
                FROM QuarterData
                WHERE QuarterFlag IS NOT NULL
                GROUP BY QuarterFlag";

            string finalCalcSql = @"
                SELECT ROUND(NVL(MAX(CASE WHEN QuarterFlag='Current' THEN AvgDaysToPay END), 0), 0) AS Curr,
                       ROUND(NVL(MAX(CASE WHEN QuarterFlag='Previous' THEN AvgDaysToPay END), 0), 0) AS Prev
                FROM QuarterAgg";

            string sql = @"
                WITH CurrentPeriod AS (
                    " + currentPeriodSql + @"
                ),
                QuarterData AS (
                    " + quarterDataSql + @"
                ),
                QuarterAgg AS (
                    " + quarterAggSql + @"
                ),
                FinalCalc AS (
                    " + finalCalcSql + @"
                )
                SELECT Curr AS Current_Quarter_Avg_Days,
                       Prev AS Previous_Quarter_Avg_Days,
                       Curr - Prev AS Difference_Days
                FROM FinalCalc";

            int currentAvg = 0;
            int previousAvg = 0;
            int diffDays = 0;
            string displayText = "No change";

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql);
                if (dr != null && dr.Read())
                {
                    currentAvg = Util.GetValueOfInt(dr["Current_Quarter_Avg_Days"]);
                    previousAvg = Util.GetValueOfInt(dr["Previous_Quarter_Avg_Days"]);
                    diffDays = Util.GetValueOfInt(dr["Difference_Days"]);
                    
                    if (diffDays < 0)
                    {
                        displayText = System.Math.Abs(diffDays).ToString() + " days faster than last quarter";
                    }
                    else if (diffDays > 0)
                    {
                        displayText = diffDays.ToString() + " days slower than last quarter";
                    }
                    else
                    {
                        displayText = "No change";
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
