/******************************************************
 * Module Name    : VAS
 * Purpose        : Top Budget Overruns dashboard widget endpoints
 * chronological  : Development
 * Created Date   : 2026-09-08
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
    /// Module Name : VAS_252_TopBudgetOverRunsWidget
    /// Purpose     : Thin AJAX endpoint for the Top Budget Overruns widget. All business
    ///               logic lives in VASLogic.Models.VAS_252_TopBudgetOverRunsModel; this
    ///               action only resolves the session context and serializes the model
    ///               result.
    ///
    ///               NOTHING THE BROWSER SENDS IS AUTHORITATIVE. The client supplies a
    ///               financial year and a page position; the model re-resolves the year
    ///               against the tenant's own primary calendar, clamps the page numbers,
    ///               and takes the tenant, the primary accounting schema and the role's
    ///               organization access from the server side alone. A year id the role
    ///               cannot see falls back to the default rather than reaching Fact_Acct.
    ///
    ///               ONE call per load, per year change and per page turn: the response
    ///               carries the page, the year list, the pager's total and the count of
    ///               over-budget accounts, because the last three are properties of the
    ///               whole set that a single page could not work out for itself.
    ///
    ///               No exception detail reaches the browser - a failure serializes as
    ///               { error: true } and is logged with its stack trace server-side.
    /// Chronological development:
    ///   VAI154      2026-09-08 Created
    /// </summary>
    public class VAS_252_TopBudgetOverRunsWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_252_TopBudgetOverRunsWidgetController).FullName);

        /// <summary>
        /// Returns one page of accounts whose actual has passed their approved budget, the
        /// financial years the filter can offer, the pager's total and the subtitle's count.
        /// </summary>
        /// <param name="yearId">C_Year_ID to read, or 0 to default to the financial year
        /// containing today.</param>
        /// <param name="pageNo">1-based page; clamped by the model.</param>
        /// <param name="pageSize">Rows per page; clamped by the model to [1,12].</param>
        /// <returns>JSON-serialized OverRunResult, "" without a session, or { error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetRows(int yearId = 0, int pageNo = 1,
            int pageSize = VAS_252_TopBudgetOverRunsModel.DEFAULT_PageSize)
        {
            string retJSON = "";

            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                try
                {
                    VAS_252_TopBudgetOverRunsModel model = new VAS_252_TopBudgetOverRunsModel();
                    retJSON = JsonConvert.SerializeObject(
                        model.GetRows(ctx, yearId, pageNo, pageSize));
                }
                catch (Exception ex)
                {
                    Log.Log(Level.SEVERE, "VAS_252_TopBudgetOverRunsWidget.GetRows", ex);
                    retJSON = JsonConvert.SerializeObject(new { error = true });
                }
            }

            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
    }
}
