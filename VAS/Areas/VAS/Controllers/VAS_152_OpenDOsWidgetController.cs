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
    /// Module Name : VAS_152_OpenDOsWidget (Delivery Order dashboard)
    /// Purpose     : Data endpoint for the 2x1 "Open DOs" KPI tile - count of
    ///               active outbound customer Delivery Order headers not yet
    ///               shipped, i.e. customer shipment documents (MovementType
    ///               'C-') that are sales, non-return transactions whose
    ///               DocStatus is Drafted (DR), In Progress (IP) or Waiting
    ///               Confirmation (WC). Counts M_InOut headers - never
    ///               M_InOutLine lines; no line join is performed. Completed,
    ///               Closed, Voided, Reversed and other statuses are excluded
    ///               and no date filter is applied (all currently open DOs
    ///               visible to the user). MRole is applied to the fetched
    ///               table (M_InOut) on the read; the SQL uses only ANSI
    ///               constructs (no NVL/DECODE/TRUNC or database-specific
    ///               functions), so it runs unchanged on Oracle and PostgreSQL.
    /// Widget number 152.
    /// Chronological development:
    ///   &lt;EmpCode&gt;   2026-07-22 Created
    /// </summary>
    public class VAS_152_OpenDOsWidgetController : Controller
    {
        /// <summary>
        /// Count of open (not-yet-shipped) customer Delivery Order headers.
        /// </summary>
        /// <returns>JSON { openDoCount }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetSummary()
        {
            if (Session["ctx"] == null)
            {
                return Fail(Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired");
            }

            Ctx ctx = Session["ctx"] as Ctx;

            string sql = @"
                SELECT
                    COUNT(*) AS Open_Do_Count
                FROM M_InOut io
                WHERE io.IsActive = 'Y'
                  AND io.IsSOTrx = 'Y'
                  AND io.IsReturnTrx = 'N'
                  AND io.MovementType = 'C-'
                  AND io.DocStatus IN ('DR', 'IP', 'WC')";

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "io", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            try
            {
                int count = Util.GetValueOfInt(DB.ExecuteScalar(sql, new SqlParameter[0], null));
                return Ok(new { openDoCount = count });
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
