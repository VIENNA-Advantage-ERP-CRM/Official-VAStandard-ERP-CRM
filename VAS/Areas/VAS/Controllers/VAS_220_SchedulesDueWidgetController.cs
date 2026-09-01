/******************************************************
 * Module Name    : VAS
 * Purpose        : Schedules Due dashboard widget endpoint
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
    /// Module Name : VAS_220_SchedulesDueWidget
    /// Purpose     : Thin AJAX endpoint for the Schedules Due dashboard widget
    ///               (Recurring module). All business logic lives in
    ///               VASLogic.Models.VAS_220_SchedulesDueModel; this controller only
    ///               resolves the session context and serializes the model result.
    ///               The KPI card and its drill-down modal share this one endpoint,
    ///               so the headline count can never disagree with the list behind
    ///               it. The tenant is always taken from the authenticated context -
    ///               the client never supplies AD_Client_ID.
    /// Chronological development:
    ///   VAI154      2026-08-31 Created
    /// </summary>
    public class VAS_220_SchedulesDueWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_220_SchedulesDueWidgetController).FullName);

        /// <summary>
        /// Returns the recurring setups of the session tenant that are due to
        /// generate inside the look-ahead window, together with the two headline
        /// figures the KPI card shows.
        /// </summary>
        /// <param name="days">Look-ahead window in days. Optional; defaults to the
        /// model's 30-day window and is clamped there, so an out-of-range or hostile
        /// value cannot widen the query.</param>
        /// <returns>JSON-serialized SchedulesDueInfo, "" when there is no session,
        /// or an { error } payload when the lookup fails.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetSchedulesDue(int? days)
        {
            string retJSON = "";

            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                try
                {
                    int windowDays = days.HasValue ? days.Value : VAS_220_SchedulesDueModel.DEFAULT_WINDOW_DAYS;

                    VAS_220_SchedulesDueModel model = new VAS_220_SchedulesDueModel();
                    retJSON = JsonConvert.SerializeObject(model.GetSchedulesDue(ctx, windowDays));
                }
                catch (Exception ex)
                {
                    Log.Log(Level.SEVERE, "VAS_220_SchedulesDueWidget.GetSchedulesDue", ex);
                    retJSON = JsonConvert.SerializeObject(new { error = true });
                }
            }

            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
    }
}
