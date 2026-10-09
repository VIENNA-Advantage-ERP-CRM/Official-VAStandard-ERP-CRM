/******************************************************
 * Module Name    : VAS
 * Purpose        : By Document Type dashboard widget endpoint
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
    /// Module Name : VAS_223_ByDocumentTypeWidget
    /// Purpose     : Thin AJAX endpoint for the By Document Type dashboard widget
    ///               (Recurring module). All business logic lives in
    ///               VASLogic.Models.VAS_223_ByDocumentTypeModel; this controller only
    ///               resolves the session context and serializes the model result.
    ///               The tenant is always taken from the authenticated context - the
    ///               client never supplies AD_Client_ID.
    /// Chronological development:
    ///   VAI154      2026-08-31 Created
    /// </summary>
    public class VAS_223_ByDocumentTypeWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_223_ByDocumentTypeWidgetController).FullName);

        /// <summary>
        /// Returns the active recurring setups of the session tenant grouped by the
        /// type of document they generate, largest bucket first.
        /// </summary>
        /// <returns>JSON-serialized ByDocumentTypeInfo, "" when there is no session,
        /// or an { error } payload when the lookup fails.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetByDocumentType()
        {
            string retJSON = "";

            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                try
                {
                    VAS_223_ByDocumentTypeModel model = new VAS_223_ByDocumentTypeModel();
                    retJSON = JsonConvert.SerializeObject(model.GetByDocumentType(ctx));
                }
                catch (Exception ex)
                {
                    Log.Log(Level.SEVERE, "VAS_223_ByDocumentTypeWidget.GetByDocumentType", ex);
                    retJSON = JsonConvert.SerializeObject(new { error = true });
                }
            }

            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
    }
}
