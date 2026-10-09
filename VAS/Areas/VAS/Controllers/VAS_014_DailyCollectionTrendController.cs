/******************************************************
 * Module Name    : VAS
 * Purpose        : Daily Collection Trend dashboard widget endpoint
 * chronological  : Development
 * Created Date   : 2026-06-02
 * Created by     : VAI154
 ******************************************************/

using Newtonsoft.Json;
using System.Web.Mvc;
using VAdvantage.Utility;
using VASLogic.Models;
using VIS.Filters;

namespace VAS.Controllers
{
    /// <summary>
    /// Module Name : VAS_014_DailyCollectionTrend
    /// Purpose     : Thin AJAX endpoint for the Daily Collection Trend dashboard
    ///               widget. All business logic lives in
    ///               VASLogic.Models.VAS_014_DailyCollectionTrendModel; this
    ///               controller only resolves the session context and serializes
    ///               the model result.
    /// Chronological development:
    ///   VAI154      2026-06-02 Created
    /// </summary>
    public class VAS_014_DailyCollectionTrendController : Controller
    {
        /// <summary>
        /// Returns the daily collection trend (last N days) for the session
        /// client in the base/accounting currency, with the N-day average and
        /// peak day.
        /// </summary>
        /// <param name="days">Window length in days (default 14).</param>
        /// <returns>JSON-serialized DailyCollectionTrend, or "" when no session.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetDailyTrend(int days = 14)
        {
            string retJSON = "";

            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_014_DailyCollectionTrendModel model = new VAS_014_DailyCollectionTrendModel();
                retJSON = JsonConvert.SerializeObject(model.GetDailyTrend(ctx, days));
            }

            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
    }
}
