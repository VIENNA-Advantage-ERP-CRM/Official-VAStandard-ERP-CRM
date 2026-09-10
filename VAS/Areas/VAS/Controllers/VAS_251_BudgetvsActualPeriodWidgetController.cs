/******************************************************
 * Module Name    : VAS
 * Purpose        : Budget vs Actual by Period dashboard widget endpoints
 * chronological  : Development
 * Created Date   : 2026-09-09
 * Created by     : VAI145
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
    /// Module Name : VAS_251_BudgetvsActualPeriodWidget
    /// Purpose     : Thin AJAX endpoints for the Budget vs Actual by Period widget. All
    ///               business logic lives in
    ///               VASLogic.Models.VAS_251_BudgetvsActualPeriodModel; these actions only
    ///               resolve the session context and serialize the model result.
    ///
    ///               TWO ENDPOINTS, AND THE SECOND IS ONLY EVER CALLED ON DEMAND. The chart
    ///               is one read; the ledger accounts behind a period are a second read that
    ///               happens when - and only when - a posted period is opened. The drill-down
    ///               is never carried in the chart payload, which is what keeps a 12-period
    ///               year one small response.
    ///
    ///               NOTHING THE BROWSER SENDS IS AUTHORITATIVE. The client supplies a
    ///               financial year and a period id; the model re-resolves the year against
    ///               the tenant's own primary calendar and the period against that same
    ///               calendar, and takes the tenant, the primary accounting schema and the
    ///               role's organization access from the server side alone. An id that
    ///               belongs to another calendar reads nothing rather than reaching
    ///               Fact_Acct with it.
    ///
    ///               No exception detail reaches the browser - a failure serializes as
    ///               { error: true } and is logged with its stack trace server-side.
    /// Chronological development:
    ///   VAI145      2026-09-09 Created
    /// </summary>
    public class VAS_251_BudgetvsActualPeriodWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_251_BudgetvsActualPeriodWidgetController).FullName);

        /// <summary>
        /// Returns the whole chart for one financial year: every active period with its
        /// budget, its actual and its bar heights, the years the filter can offer, the
        /// accounting currency and the totals the subtitle is built from.
        /// </summary>
        /// <param name="yearId">C_Year_ID to read, or 0 to default to the financial year
        /// containing today.</param>
        /// <returns>JSON-serialized ChartResult, "" without a session, or { error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetChart(int yearId = 0)
        {
            string retJSON = "";

            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                try
                {
                    VAS_251_BudgetvsActualPeriodModel model = new VAS_251_BudgetvsActualPeriodModel();
                    retJSON = JsonConvert.SerializeObject(model.GetChart(ctx, yearId));
                }
                catch (Exception ex)
                {
                    Log.Log(Level.SEVERE, "VAS_251_BudgetvsActualPeriodWidget.GetChart", ex);
                    retJSON = JsonConvert.SerializeObject(new { error = true });
                }
            }

            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Returns the ledger accounts posted to inside ONE period, with that period's own
        /// budget, actual, variance and utilization - so the modal reconciles to the bar that
        /// opened it without the client passing any figure back.
        /// </summary>
        /// <param name="periodId">C_Period_ID the user clicked; re-validated against the
        /// tenant's primary calendar by the model.</param>
        /// <returns>JSON-serialized PeriodDetailResult, "" without a session, or { error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetPeriodDetail(int periodId = 0)
        {
            string retJSON = "";

            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                try
                {
                    VAS_251_BudgetvsActualPeriodModel model = new VAS_251_BudgetvsActualPeriodModel();
                    retJSON = JsonConvert.SerializeObject(model.GetPeriodDetail(ctx, periodId));
                }
                catch (Exception ex)
                {
                    Log.Log(Level.SEVERE, "VAS_251_BudgetvsActualPeriodWidget.GetPeriodDetail", ex);
                    retJSON = JsonConvert.SerializeObject(new { error = true });
                }
            }

            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
    }
}
