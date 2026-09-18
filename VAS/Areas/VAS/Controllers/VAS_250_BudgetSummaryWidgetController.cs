/******************************************************
 * Module Name    : VAS
 * Purpose        : Budget Summary dashboard widget endpoints
 * chronological  : Development
 * Created Date   : 2026-09-10
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
    /// Module Name : VAS_250_BudgetSummaryWidget
    /// Purpose     : Thin AJAX endpoint for the Budget Summary widget. All business logic lives
    ///               in VASLogic.Models.VAS_250_BudgetSummaryModel; this action only resolves
    ///               the session context and serializes the model result.
    ///
    ///               NOTHING THE BROWSER SENDS IS AUTHORITATIVE. The client supplies a financial
    ///               year and nothing else; the model re-resolves that year against the tenant's
    ///               own primary calendar and takes the tenant, the primary accounting schema,
    ///               its currency and the role's organization access from the server side alone.
    ///               A year id the role cannot see falls back to the default rather than
    ///               reaching Fact_Acct.
    ///
    ///               ONE call per load, per Refresh and per year change: the response carries
    ///               the three account-type rows, the four totals above them, the year list and
    ///               the posted-through date together, because every one of them belongs to the
    ///               same read and a second round trip could only disagree with the first.
    ///
    ///               No exception detail reaches the browser - a failure serializes as
    ///               { error: true } and is logged with its stack trace server-side.
    /// Chronological development:
    ///   VAI145      2026-09-10 Created
    /// </summary>
    public class VAS_250_BudgetSummaryWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_250_BudgetSummaryWidgetController).FullName);

        /// <summary>
        /// Returns the selected year's budget summary: one row per account type (Expense,
        /// Revenue, Asset), the four totals, the accounting-schema currency, the financial years
        /// the filter can offer and the date the ledger is posted through.
        /// </summary>
        /// <param name="yearId">C_Year_ID to read, or 0 to default to the financial year
        /// containing today.</param>
        /// <returns>JSON-serialized SummaryResult, "" without a session, or { error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetSummary(int yearId = 0)
        {
            string retJSON = "";

            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                try
                {
                    VAS_250_BudgetSummaryModel model = new VAS_250_BudgetSummaryModel();
                    retJSON = JsonConvert.SerializeObject(model.GetSummary(ctx, yearId));
                }
                catch (Exception ex)
                {
                    Log.Log(Level.SEVERE, "VAS_250_BudgetSummaryWidget.GetSummary", ex);
                    retJSON = JsonConvert.SerializeObject(new { error = true });
                }
            }

            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Returns the ledger accounts behind one row of the card - budget, actual and balance
        /// per account - together with that row's own totals, so the panel reconciles to the row
        /// that opened it. Read only when a row is opened.
        /// </summary>
        /// <param name="accountType">C_ElementValue.AccountType the reader clicked: 'E', 'R' or
        /// 'A'. The model refuses anything else rather than querying it.</param>
        /// <param name="yearId">C_Year_ID the card is showing; re-resolved by the model against
        /// the tenant's own primary calendar.</param>
        /// <returns>JSON-serialized TypeDetailResult, "" without a session, or { error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetTypeDetail(string accountType = "", int yearId = 0)
        {
            string retJSON = "";

            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                try
                {
                    VAS_250_BudgetSummaryModel model = new VAS_250_BudgetSummaryModel();
                    retJSON = JsonConvert.SerializeObject(model.GetTypeDetail(ctx, accountType, yearId));
                }
                catch (Exception ex)
                {
                    Log.Log(Level.SEVERE, "VAS_250_BudgetSummaryWidget.GetTypeDetail", ex);
                    retJSON = JsonConvert.SerializeObject(new { error = true });
                }
            }

            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
    }
}
