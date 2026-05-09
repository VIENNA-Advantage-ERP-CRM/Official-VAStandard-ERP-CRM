using Newtonsoft.Json;
using System.Data;
using System.Web.Mvc;
using VAdvantage.Classes;
using VAdvantage.DataBase;
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
                return Json(new { error = "Session Expired" }, JsonRequestBehavior.AllowGet);

            Ctx ctx = Session["ctx"] as Ctx;

            string sql = @"
                WITH CurrentPeriod AS (
                    SELECT
                        p.C_Year_ID,
                        TO_NUMBER(TO_CHAR(p.StartDate, 'Q'))    AS CurrentQuarter,
                        TO_NUMBER(TO_CHAR(p.StartDate, 'YYYY')) AS CurrentYear
                    FROM AD_ClientInfo ci
                    JOIN C_Calendar cal ON ci.C_Calendar_ID  = cal.C_Calendar_ID
                    JOIN C_Year     yr  ON cal.C_Calendar_ID = yr.C_Calendar_ID
                    JOIN C_Period   p   ON yr.C_Year_ID      = p.C_Year_ID
                    WHERE ci.AD_Client_ID = " + ctx.GetAD_Client_ID() + @"
                      AND TRUNC(SYSDATE) BETWEEN TRUNC(p.StartDate) AND TRUNC(p.EndDate)
                    FETCH FIRST 1 ROW ONLY
                ),
                QuarterData AS (
                    SELECT
                        CASE
                            WHEN TO_NUMBER(TO_CHAR(pay.DateAcct, 'Q'))    = cp.CurrentQuarter
                             AND TO_NUMBER(TO_CHAR(pay.DateAcct, 'YYYY')) = cp.CurrentYear
                                THEN 'Current'

                            WHEN TO_NUMBER(TO_CHAR(pay.DateAcct, 'Q')) =
                                 CASE WHEN cp.CurrentQuarter = 1 THEN 4 ELSE cp.CurrentQuarter - 1 END
                             AND (
                                 (cp.CurrentQuarter > 1 AND TO_NUMBER(TO_CHAR(pay.DateAcct, 'YYYY')) = cp.CurrentYear)
                                 OR
                                 (cp.CurrentQuarter = 1 AND TO_NUMBER(TO_CHAR(pay.DateAcct, 'YYYY')) = cp.CurrentYear - 1)
                             )
                                THEN 'Previous'
                        END AS QuarterFlag,

                        GREATEST(TRUNC(pay.DateAcct) - TRUNC(ips.DueDate), 0) AS Days_To_Pay,
                        NVL(al.Amount, 0) AS Amount

                    FROM C_Invoice i
                    JOIN C_InvoicePaySchedule ips ON ips.C_Invoice_ID            = i.C_Invoice_ID
                    JOIN C_AllocationLine     al  ON al.C_InvoicePaySchedule_ID  = ips.C_InvoicePaySchedule_ID
                    JOIN C_Payment            pay ON al.C_Payment_ID             = pay.C_Payment_ID
                    CROSS JOIN CurrentPeriod cp
                    WHERE i.IsSoTrx    = 'Y'
                      AND i.DocStatus IN ('CO', 'CL')
                      AND al.C_Payment_ID IS NOT NULL
                      AND i.IsActive   = 'Y'
                      AND ips.IsActive = 'Y'
                      AND pay.IsActive = 'Y'
                      --AND i.AD_Client_ID = " + ctx.GetAD_Client_ID() + @"
                ),
                QuarterAgg AS (
                    SELECT
                        QuarterFlag,
                        NVL(
                            SUM(Days_To_Pay * Amount) / NULLIF(SUM(Amount), 0),
                            0
                        ) AS AvgDaysToPay
                    FROM QuarterData
                    WHERE QuarterFlag IS NOT NULL
                    GROUP BY QuarterFlag
                ),
                FinalCalc AS (
                    SELECT
                        ROUND(NVL(MAX(CASE WHEN QuarterFlag = 'Current'  THEN AvgDaysToPay END), 0), 0) AS curr,
                        ROUND(NVL(MAX(CASE WHEN QuarterFlag = 'Previous' THEN AvgDaysToPay END), 0), 0) AS prev
                    FROM QuarterAgg
                )
                SELECT
                    curr AS Current_Quarter_Avg_Days,
                    prev AS Previous_Quarter_Avg_Days,
                    curr - prev AS Difference_Days,
                    CASE
                        WHEN curr - prev < 0 THEN ABS(curr - prev) || ' days faster than last quarter'
                        WHEN curr - prev > 0 THEN (curr - prev)    || ' days slower than last quarter'
                        ELSE 'No change'
                    END AS Display_Text
                FROM FinalCalc";

            int    currentAvg   = 0;
            int    previousAvg  = 0;
            int    diffDays     = 0;
            string displayText  = "No change";

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql);
                if (dr != null && dr.Read())
                {
                    currentAvg  = Util.GetValueOfInt(dr["Current_Quarter_Avg_Days"]);
                    previousAvg = Util.GetValueOfInt(dr["Previous_Quarter_Avg_Days"]);
                    diffDays    = Util.GetValueOfInt(dr["Difference_Days"]);
                    displayText = dr["Display_Text"]?.ToString() ?? "No change";
                }
            }
            finally
            {
                dr?.Close();
            }

            var result = new
            {
                currentAvgDays  = currentAvg,
                previousAvgDays = previousAvg,
                differenceDays  = diffDays,
                displayText     = displayText
            };

            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }
    }
}
