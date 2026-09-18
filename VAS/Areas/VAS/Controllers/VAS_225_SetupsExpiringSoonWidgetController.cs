/******************************************************
 * Module Name    : VAS
 * Purpose        : Setups Expiring Soon dashboard widget endpoint
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
    /// Module Name : VAS_225_SetupsExpiringSoonWidget
    /// Purpose     : Thin AJAX endpoint for the Setups Expiring Soon dashboard widget
    ///               (Recurring module). All business logic lives in
    ///               VASLogic.Models.VAS_225_SetupsExpiringSoonModel; this controller
    ///               only resolves the session context and serializes the result.
    ///               The tenant is always taken from the authenticated context - the
    ///               client never supplies AD_Client_ID.
    /// Chronological development:
    ///   VAI154      2026-08-31 Created
    /// </summary>
    public class VAS_225_SetupsExpiringSoonWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_225_SetupsExpiringSoonWidgetController).FullName);

        /// <summary>
        /// Returns one page of the session tenant's active recurring setups that run
        /// out completely - reach zero remaining runs - inside the look-ahead window,
        /// soonest end first. The page is cut server side, so the response carries
        /// only the requested slice plus the true size of the filtered set.
        /// </summary>
        /// <param name="page">Zero-based page index. Optional; defaults to the first
        /// page and is clamped in the model.</param>
        /// <param name="pageSize">Rows per page - the widget derives this from its own
        /// cell height. Optional; clamped in the model, so an out-of-range or hostile
        /// value cannot pull an unbounded result set.</param>
        /// <param name="windowDays">Look-ahead window in days. Optional; defaults to
        /// the model's 60-day window and is clamped there, so an out-of-range value
        /// cannot widen the list into "every setup the tenant has".</param>
        /// <returns>JSON-serialized ExpiringSoonInfo, "" when there is no session, or
        /// an { error } payload when the lookup fails.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetExpiringSoon(int? page, int? pageSize, int? windowDays)
        {
            string retJSON = "";

            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                try
                {
                    int pageIndex = page.HasValue ? page.Value : 0;
                    int size = pageSize.HasValue ? pageSize.Value : VAS_225_SetupsExpiringSoonModel.PAGESIZE_DEFAULT;
                    int window = windowDays.HasValue
                        ? windowDays.Value
                        : VAS_225_SetupsExpiringSoonModel.WINDOW_DAYS_DEFAULT;

                    VAS_225_SetupsExpiringSoonModel model = new VAS_225_SetupsExpiringSoonModel();
                    retJSON = JsonConvert.SerializeObject(model.GetExpiringSoon(ctx, pageIndex, size, window));
                }
                catch (Exception ex)
                {
                    Log.Log(Level.SEVERE, "VAS_225_SetupsExpiringSoonWidget.GetExpiringSoon", ex);
                    retJSON = JsonConvert.SerializeObject(new { error = true });
                }
            }

            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
    }
}
