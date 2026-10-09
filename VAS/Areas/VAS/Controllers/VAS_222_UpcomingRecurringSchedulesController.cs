/******************************************************
 * Module Name    : VAS
 * Purpose        : Upcoming Recurring Schedules widget endpoints
 * chronological  : Development
 * Created Date   : 2026-08-31
 * Created by     : VAI154
 ******************************************************/

using Newtonsoft.Json;
using System;
using System.Web.Mvc;
using VAdvantage.Logging;
using VAdvantage.Utility;
using VASLogic.Models;
using VIS.Filters;

namespace VAS.Controllers
{
    /// <summary>
    /// Module Name : VAS_222_UpcomingRecurringSchedules
    /// Purpose     : Thin AJAX endpoints for the Upcoming Recurring Schedules
    ///               dashboard widget (Recurring module). All business logic lives in
    ///               VASLogic.Models.VAS_222_UpcomingSchedulesModel; this controller
    ///               only resolves the session context and serializes the result.
    ///               The tenant is always taken from the authenticated context, and
    ///               the queue's date bound is always derived server side - the client
    ///               supplies neither AD_Client_ID nor a date.
    /// Chronological development:
    ///   VAI154      2026-08-31 Created
    /// </summary>
    public class VAS_222_UpcomingRecurringSchedulesController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_222_UpcomingRecurringSchedulesController).FullName);

        /// <summary>
        /// Returns one page of the recurring setups whose next run falls today or
        /// later, soonest first.
        /// </summary>
        /// <param name="page">Zero-based page index. Optional; defaults to the first
        /// page and is clamped in the model.</param>
        /// <param name="pageSize">Rows per page - the widget derives this from its own
        /// cell height. Optional; clamped in the model, so an out-of-range or hostile
        /// value cannot pull an unbounded result set.</param>
        /// <returns>JSON-serialized UpcomingSchedulesPage, "" when there is no
        /// session, or an { error } payload when the lookup fails.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetUpcomingSchedules(int? page, int? pageSize)
        {
            string retJSON = "";

            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                try
                {
                    int pageIndex = page.HasValue ? page.Value : 0;
                    int size = pageSize.HasValue ? pageSize.Value : VAS_222_UpcomingSchedulesModel.PAGESIZE_DEFAULT;

                    VAS_222_UpcomingSchedulesModel model = new VAS_222_UpcomingSchedulesModel();
                    retJSON = JsonConvert.SerializeObject(model.GetUpcomingSchedules(ctx, pageIndex, size));
                }
                catch (Exception ex)
                {
                    Log.Log(Level.SEVERE, "VAS_222_UpcomingRecurringSchedules.GetUpcomingSchedules", ex);
                    retJSON = JsonConvert.SerializeObject(new { error = true });
                }
            }

            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Runs one recurring setup, creating the next document in its series.
        ///
        /// This endpoint CHANGES data - it is POST-only so it cannot be triggered by a
        /// link, a prefetch or a stray GET. The widget confirms the action with the
        /// user before calling it.
        /// </summary>
        /// <param name="C_Recurring_ID">Setup to run. Ownership is re-checked against
        /// the session tenant in the model.</param>
        /// <returns>JSON-serialized GenerateResult, "" when there is no session, or an
        /// { error } payload when the run could not be attempted.</returns>
        [HttpPost]
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GenerateRun(int C_Recurring_ID)
        {
            string retJSON = "";

            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                try
                {
                    VAS_222_UpcomingSchedulesModel model = new VAS_222_UpcomingSchedulesModel();
                    retJSON = JsonConvert.SerializeObject(model.GenerateRun(ctx, C_Recurring_ID));
                }
                catch (Exception ex)
                {
                    Log.Log(Level.SEVERE, "VAS_222_UpcomingRecurringSchedules.GenerateRun C_Recurring_ID="
                        + C_Recurring_ID, ex);
                    retJSON = JsonConvert.SerializeObject(new { error = true });
                }
            }

            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
    }
}
