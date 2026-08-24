/******************************************************
 * Module Name    : VAS
 * Purpose        : Suspense Balances dashboard widget endpoints
 * chronological  : Development
 * Created Date   : 2026-08-24
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
    /// Module Name : VAS_202_SuspenseBalancesWidget
    /// Purpose     : Thin AJAX endpoints for the Suspense Balances widget. All business
    ///               logic lives in VASLogic.Models.VAS_202_SuspenseBalancesModel; these
    ///               actions only resolve the session context, pass the selected ids
    ///               through and serialize the model result.
    ///
    ///               The browser sends nothing authoritative. The tenant always comes
    ///               from the authenticated context; the calendar, the accounting
    ///               schema, the suspense account ids and the role's organization access
    ///               are all resolved server-side. The two values the client DOES send -
    ///               the period and the account - are re-validated by the model before
    ///               a single Fact_Acct row is read: the period must still be active,
    ///               open, standard and on the primary calendar, and the account must be
    ///               one of the three the tenant's own primary accounting schema names
    ///               as a suspense or rounding account.
    ///
    ///               Three actions: bootstrap on load, the account figures on a period
    ///               change, and one page of postings when an account is opened.
    ///               Postings are never fetched with the figures.
    ///
    ///               No exception detail reaches the browser - a failure serializes as
    ///               { error: true } and is logged with its stack trace server-side.
    /// Chronological development:
    ///   VAI154      2026-08-24 Created
    /// </summary>
    public class VAS_202_SuspenseBalancesWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_202_SuspenseBalancesWidgetController).FullName);

        /// <summary>
        /// Returns the tenant's accounting context, the selectable open periods of the
        /// primary calendar, the period to preselect, and that period's suspense
        /// figures.
        /// </summary>
        /// <returns>JSON-serialized SuspenseBootstrap, "" without a session, or { error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetBootstrap()
        {
            string retJSON = "";

            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                try
                {
                    VAS_202_SuspenseBalancesModel model = new VAS_202_SuspenseBalancesModel();
                    retJSON = JsonConvert.SerializeObject(model.GetBootstrap(ctx));
                }
                catch (Exception ex)
                {
                    Log.Log(Level.SEVERE, "VAS_202_SuspenseBalancesWidget.GetBootstrap", ex);
                    retJSON = JsonConvert.SerializeObject(new { error = true });
                }
            }

            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Returns the three suspense account figures of one period.
        /// </summary>
        /// <param name="periodId">C_Period_ID selected by the user.</param>
        /// <returns>JSON-serialized PeriodData, or { error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetPeriodData(int periodId)
        {
            string retJSON = "";

            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                try
                {
                    VAS_202_SuspenseBalancesModel model = new VAS_202_SuspenseBalancesModel();
                    retJSON = JsonConvert.SerializeObject(model.GetPeriodData(ctx, periodId));
                }
                catch (Exception ex)
                {
                    Log.Log(Level.SEVERE, "VAS_202_SuspenseBalancesWidget.GetPeriodData", ex);
                    retJSON = JsonConvert.SerializeObject(new { error = true });
                }
            }

            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Returns one page of the postings sitting on one suspense account, plus the
        /// total count. The account id is authorized against the tenant's own
        /// configuration and the page size is clamped by the model, so a crafted request
        /// can neither read an arbitrary account nor pull a whole table in one response.
        /// </summary>
        /// <param name="periodId">C_Period_ID selected by the user.</param>
        /// <param name="accountId">Natural Account_ID of the clicked row.</param>
        /// <param name="pageNo">1-based page number.</param>
        /// <param name="pageSize">Requested rows per page.</param>
        /// <returns>JSON-serialized PostingPage, or { error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetPostings(int periodId, int accountId, int pageNo, int pageSize)
        {
            string retJSON = "";

            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                try
                {
                    VAS_202_SuspenseBalancesModel model = new VAS_202_SuspenseBalancesModel();
                    retJSON = JsonConvert.SerializeObject(
                        model.GetPostings(ctx, periodId, accountId, pageNo, pageSize));
                }
                catch (Exception ex)
                {
                    Log.Log(Level.SEVERE, "VAS_202_SuspenseBalancesWidget.GetPostings", ex);
                    retJSON = JsonConvert.SerializeObject(new { error = true });
                }
            }

            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
    }
}
