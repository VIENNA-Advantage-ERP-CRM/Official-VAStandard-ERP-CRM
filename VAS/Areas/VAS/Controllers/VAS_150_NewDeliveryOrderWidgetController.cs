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
    /// Module Name : VAS_150_NewDeliveryOrderWidget (Delivery Order dashboard)
    /// Purpose     : Backend for the 1x1 "New Delivery Order" quick-action tile.
    ///               The tile is a launcher: clicking it opens the outbound
    ///               customer Delivery Order (Shipment) window on a new record
    ///               through the dashboard widget framework. This endpoint only
    ///               resolves the AD_Window id of that window so the front end
    ///               can open it - it creates no document itself. The window is
    ///               resolved by NAME (never a hardcoded id, which differs
    ///               between databases): the VAStandard "VAS_DeliveryOrder"
    ///               window first, falling back to the core "Shipment (Customer)"
    ///               window. MRole is applied to the fetched table (AD_Window) on
    ///               the read and only client 0 / the session client are visible,
    ///               so the returned window already respects role access. All
    ///               input is parameterized.
    /// Widget number 150.
    /// Chronological development:
    ///   &lt;EmpCode&gt;   2026-07-22 Created
    /// </summary>
    public class VAS_150_NewDeliveryOrderWidgetController : Controller
    {
        /// <summary>
        /// Active, role-accessible AD_Window id for the Delivery Order window, or 0.
        /// </summary>
        /// <returns>JSON { windowId }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetDeliveryWindowId()
        {
            if (Session["ctx"] == null)
            {
                return Fail(Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired");
            }

            Ctx ctx = Session["ctx"] as Ctx;

            try
            {
                int windowId = GetWindowIdByName(ctx, "VAS_DeliveryOrder");
                if (windowId <= 0) { windowId = GetWindowIdByName(ctx, "Shipment (Customer)"); }
                return Ok(new { windowId = windowId });
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
            }
        }

        /// <summary>Active, role-accessible AD_Window id for one window name, or 0.</summary>
        private int GetWindowIdByName(Ctx ctx, string windowName)
        {
            string sql = @"
                SELECT DoWindow.AD_Window_ID
                FROM AD_Window DoWindow
                WHERE DoWindow.IsActive='Y'
                  AND DoWindow.Name=@Window_Name
                  AND DoWindow.AD_Client_ID IN (0,@Window_Client_ID)";

            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql,
                "DoWindow",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );
            sql += " ORDER BY DoWindow.AD_Window_ID DESC OFFSET 0 ROWS FETCH NEXT 1 ROWS ONLY";

            // Parameter order matches placeholder appearance (positional binding).
            return Util.GetValueOfInt(DB.ExecuteScalar(sql, new SqlParameter[]
            {
                new SqlParameter("@Window_Name", windowName),
                new SqlParameter("@Window_Client_ID", ctx.GetAD_Client_ID())
            }, null));
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
