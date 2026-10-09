using Newtonsoft.Json;
using System;
using System.Data.SqlClient;
using System.Web.Mvc;
using VAdvantage.DataBase;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VAS.Controllers
{
    /// <summary>
    /// Module Name : VAS_151_OnTimeRateWidget (Delivery Order dashboard)
    /// Purpose     : Data endpoint for the 2x1 "On-Time Rate" KPI tile - the
    ///               percentage of outbound customer Delivery Orders shipped on
    ///               time during the current month to date. Eligible DOs are
    ///               active, sales, non-return customer shipments (MovementType
    ///               'C-') in Completed (CO) or Closed (CL) status whose
    ///               MovementDate falls in the month-to-date window (first
    ///               calendar day of the current month .. tomorrow, half-open
    ///               so any time today is included). For each linked Sales
    ///               Order the promised date is the latest COALESCE(line
    ///               DatePromised, order DatePromised) across its active lines;
    ///               a DO is on time when MovementDate is strictly before that
    ///               latest promised date + 1. DOs with no resolvable promised
    ///               date are excluded from the denominator. Counts DO headers,
    ///               never M_InOutLine lines (no line join). MRole is applied to
    ///               the fetched table (M_InOut) on the read; the SQL uses only
    ///               portable constructs (WITH, COALESCE, NULLIF, CASE, ROUND,
    ///               CURRENT_DATE, EXTRACT, CAST, date +/- integer - no TRUNC/
    ///               SYSDATE/DATE_TRUNC/NOW/::date), so it runs unchanged on
    ///               Oracle and PostgreSQL.
    /// Widget number 151.
    /// Chronological development:
    ///   &lt;EmpCode&gt;   2026-07-22 Created
    /// </summary>
    public class VAS_151_OnTimeRateWidgetController : Controller
    {
        /// <summary>
        /// Month-to-date on-time delivery percentage for customer Delivery Orders.
        /// </summary>
        /// <returns>JSON { onTimeRate }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetSummary()
        {
            if (Session["ctx"] == null)
            {
                return Fail(Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired");
            }

            Ctx ctx = Session["ctx"] as Ctx;

            // AddAccessSQL appends its predicate for every table alias it finds in
            // the given string to the very END of that string. The CTE below has
            // its own aliases (o, ol) that go out of scope once the CTE closes, so
            // AddAccessSQL must run on the CTE body alone (ending at its WHERE,
            // before its own GROUP BY) and separately on the outer query (ending at
            // its WHERE) - never on the whole WITH...SELECT blob in one call, or the
            // o/ol predicates land in the outer WHERE where those aliases don't
            // exist (ORA-00904), which the controller's catch turns into "--%".
            string cteSql = @"
                SELECT
                    o.C_Order_ID,
                    MAX(COALESCE(ol.DatePromised, o.DatePromised)) AS latest_promised_date
                FROM C_Order o
                JOIN C_OrderLine ol
                  ON ol.C_Order_ID = o.C_Order_ID
                 AND ol.IsActive = 'Y'
                WHERE o.IsActive = 'Y'";

            cteSql = MRole.GetDefault(ctx).AddAccessSQL(cteSql, "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            cteSql += " GROUP BY o.C_Order_ID";

            string outerSql = @"
                SELECT
                    COALESCE(
                        ROUND(
                            100.0 * SUM(
                                CASE
                                    WHEN io.MovementDate < lpd.latest_promised_date + 1 THEN 1
                                    ELSE 0
                                END
                            ) / NULLIF(COUNT(*), 0),
                            2
                        ),
                        0
                    ) AS On_Time_Rate
                FROM M_InOut io
                JOIN latest_promised_dates lpd
                  ON lpd.C_Order_ID = io.C_Order_ID
                WHERE io.IsActive = 'Y'
                  AND io.IsSOTrx = 'Y'
                  AND io.IsReturnTrx = 'N'
                  AND io.MovementType = 'C-'
                  AND io.DocStatus IN ('CO', 'CL')
                  AND io.MovementDate >= CURRENT_DATE
                      - (CAST(EXTRACT(DAY FROM CURRENT_DATE) AS INTEGER) - 1)
                  AND io.MovementDate < CURRENT_DATE + 1
                  AND lpd.latest_promised_date IS NOT NULL";

            outerSql = MRole.GetDefault(ctx).AddAccessSQL(outerSql, "io", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string sql = "WITH latest_promised_dates AS (" + cteSql + ")" + outerSql;

            try
            {
                decimal rate = Util.GetValueOfDecimal(DB.ExecuteScalar(sql, new SqlParameter[0], null));
                return Ok(new { onTimeRate = rate });
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
            }
        }

        /// <summary>Wraps a success payload as a serialized JSON result.</summary>
        private JsonResult Ok(object result)
        {
            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }

        /// <summary>Wraps a failure message as a serialized JSON result.</summary>
        private JsonResult Fail(string message)
        {
            return Json(JsonConvert.SerializeObject(new
            {
                success = false,
                error = message,
                message = message
            }), JsonRequestBehavior.AllowGet);
        }
    }
}
