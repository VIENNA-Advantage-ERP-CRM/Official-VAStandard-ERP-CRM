/******************************************************
 * Module Name    : VAS
 * Purpose        : Active Setups dashboard widget endpoint
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
    /// Module Name : VAS_219_ActiveSetupsWidget
    /// Purpose     : Thin AJAX endpoint for the Active Setups dashboard widget
    ///               (Recurring module). All business logic lives in
    ///               VASLogic.Models.VAS_219_ActiveSetupsModel; this controller only
    ///               resolves the session context and serializes the model result.
    ///               The tenant is always taken from the authenticated context - the
    ///               client never supplies AD_Client_ID.
    /// Chronological development:
    ///   VAI154      2026-08-31 Created
    /// </summary>
    public class VAS_219_ActiveSetupsWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_219_ActiveSetupsWidgetController).FullName);

        /// <summary>
        /// Returns the number of recurring setups of the session tenant that are
        /// active and still have remaining runs.
        /// </summary>
        /// <returns>JSON-serialized ActiveSetupsInfo, "" when there is no session,
        /// or an { error } payload when the lookup fails.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetActiveSetups()
        {
            string retJSON = "";

            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                try
                {
                    VAS_219_ActiveSetupsModel model = new VAS_219_ActiveSetupsModel();
                    retJSON = JsonConvert.SerializeObject(model.GetActiveSetups(ctx));
                }
                catch (Exception ex)
                {
                    Log.Log(Level.SEVERE, "VAS_219_ActiveSetupsWidget.GetActiveSetups", ex);
                    retJSON = JsonConvert.SerializeObject(new { error = true });
                }
            }

            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
    }
}
