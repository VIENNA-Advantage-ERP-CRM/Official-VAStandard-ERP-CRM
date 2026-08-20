/******************************************************
 * Module Name    : VAS
 * Purpose        : Current Period dashboard widget endpoint
 * chronological  : Development
 * Created Date   : 2026-08-19
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
    /// Module Name : VAS_192_CurrentPeriodWidget
    /// Purpose     : Thin AJAX endpoint for the Current Period dashboard widget.
    ///               All business logic lives in
    ///               VASLogic.Models.VAS_192_CurrentPeriodModel; this controller
    ///               only resolves the session context and serializes the model
    ///               result. The tenant is always taken from the authenticated
    ///               context - the client never supplies AD_Client_ID - and the
    ///               period is always derived from the server's current date.
    /// Chronological development:
    ///   VAI154      2026-08-19 Created
    /// </summary>
    public class VAS_192_CurrentPeriodWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_192_CurrentPeriodWidgetController).FullName);

        /// <summary>
        /// Returns the accounting period containing the current server date for
        /// the session tenant, together with its summarised period-control status.
        /// </summary>
        /// <returns>JSON-serialized CurrentPeriodInfo, "" when there is no session,
        /// or an { error } payload when the lookup fails.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetCurrentPeriod()
        {
            string retJSON = "";

            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                try
                {
                    VAS_192_CurrentPeriodModel model = new VAS_192_CurrentPeriodModel();
                    retJSON = JsonConvert.SerializeObject(model.GetCurrentPeriod(ctx));
                }
                catch (Exception ex)
                {
                    Log.Log(Level.SEVERE, "VAS_192_CurrentPeriodWidget.GetCurrentPeriod", ex);
                    retJSON = JsonConvert.SerializeObject(new { error = true });
                }
            }

            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
    }
}
