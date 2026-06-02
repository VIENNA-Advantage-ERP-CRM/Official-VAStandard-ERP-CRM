/******************************************************
 * Module Name    : VAS
 * Purpose        : Outstanding vs Received dashboard widget endpoint
 * chronological  : Development
 * Created Date   : 2026-06-02
 * Created by     : VAI145
 ******************************************************/

using Newtonsoft.Json;
using System.Web.Mvc;
using VAdvantage.Utility;
using VASLogic.Models;
using VIS.Filters;

namespace VAS.Controllers
{
    /// <summary>
    /// Module Name : VAS_015_OutstandingVsReceived
    /// Purpose     : Thin AJAX endpoint for the Outstanding vs Received dashboard
    ///               widget. All business logic lives in
    ///               VASLogic.Models.VAS_015_OutstandingVsReceivedModel; this
    ///               controller only resolves the session context and serializes
    ///               the model result.
    /// Chronological development:
    ///   VAI145      2026-06-02 Created
    /// </summary>
    public class VAS_015_OutstandingVsReceivedController : Controller
    {
        /// <summary>
        /// Returns the per-month outstanding receivable vs received series (last N
        /// months) for the session client in the base/accounting currency.
        /// </summary>
        /// <param name="months">Window length in months (default 6).</param>
        /// <returns>JSON-serialized OutstandingVsReceived, or "" when no session.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetSeries(int months = 6)
        {
            string retJSON = "";

            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_015_OutstandingVsReceivedModel model = new VAS_015_OutstandingVsReceivedModel();
                retJSON = JsonConvert.SerializeObject(model.GetSeries(ctx, months));
            }

            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
    }
}
