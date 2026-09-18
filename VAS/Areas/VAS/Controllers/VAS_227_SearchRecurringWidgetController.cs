/******************************************************
 * Module Name    : VAS
 * Purpose        : Search Recurring widget endpoints
 * chronological  : Development
 * Created Date   : 2026-09-01
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
    /// Module Name : VAS_227_SearchRecurringWidget
    /// Purpose     : Thin AJAX endpoint for the Search Recurring dashboard search bar
    ///               (Recurring module). All business logic lives in
    ///               VASLogic.Models.VAS_227_SearchRecurringModel; this controller
    ///               only resolves the session context and serializes the result.
    ///               The tenant is always taken from the authenticated context - the
    ///               client supplies no AD_Client_ID - and the term is bound by the
    ///               model, never concatenated into SQL.
    ///
    ///               Read-only: only SELECT queries are executed here.
    /// Chronological development:
    ///   VAI154      2026-09-01 Created
    /// </summary>
    public class VAS_227_SearchRecurringWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_227_SearchRecurringWidgetController).FullName);

        /// <summary>
        /// Returns one page of the recurring setups matching the free-text term,
        /// best match first.
        /// </summary>
        /// <param name="query">Free-text term as typed by the user. A term shorter
        /// than the model's minimum returns an empty page rather than the whole
        /// table.</param>
        /// <param name="maxRows">Rows per page - the dropdown's page size. Optional;
        /// clamped in the model, so an out-of-range or hostile value cannot pull an
        /// unbounded result set.</param>
        /// <param name="offset">Rows to skip - the dropdown's scroll paging.
        /// Optional; clamped in the model.</param>
        /// <returns>JSON-serialized RecurringSearchPage, "" when there is no session,
        /// or an { error } payload when the lookup fails.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult SearchRecurring(string query, int? maxRows, int? offset)
        {
            string retJSON = "";

            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                try
                {
                    int rows = maxRows.HasValue ? maxRows.Value : VAS_227_SearchRecurringModel.MAXROWS_DEFAULT;
                    int skip = offset.HasValue ? offset.Value : 0;

                    VAS_227_SearchRecurringModel model = new VAS_227_SearchRecurringModel();
                    retJSON = JsonConvert.SerializeObject(model.Search(ctx, query, rows, skip));
                }
                catch (Exception ex)
                {
                    Log.Log(Level.SEVERE, "VAS_227_SearchRecurringWidget.SearchRecurring", ex);
                    retJSON = JsonConvert.SerializeObject(new { error = true });
                }
            }

            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
    }
}
