/******************************************************
 * Module Name    : VAS
 * Purpose        : Account-wise Balance dashboard widget endpoints
 * chronological  : Development
 * Created Date   : 2026-09-03
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
    /// Module Name : VAS_234_AccountBalanceWidget
    /// Purpose     : Thin AJAX endpoint for the Account-wise Balance widget. All business
    ///               logic lives in VASLogic.Models.VAS_234_AccountBalanceModel; this action
    ///               only resolves the session context and serializes the model result.
    ///
    ///               The browser sends three values and NONE of them is authoritative: a
    ///               page number, a page size and a sort key. The model clamps the first
    ///               two and maps the third onto a fixed whitelist, so no client string
    ///               ever reaches an ORDER BY. The tenant, the accounting calendar, the
    ///               current and previous reporting periods, each account's currency and
    ///               the role's organization access are all resolved server-side, and the
    ///               account numbers are already masked by the time they are serialized -
    ///               a full account number never crosses this boundary.
    ///
    ///               ONE call per page or sort change. The response carries the page, the
    ///               paging totals and the shared bar scale, because the last two are
    ///               properties of the whole accessible set that a single page could not
    ///               work out for itself.
    ///
    ///               No exception detail reaches the browser - a failure serializes as
    ///               { error: true } and is logged with its stack trace server-side.
    /// Chronological development:
    ///   VAI154      2026-09-03 Created
    /// </summary>
    public class VAS_234_AccountBalanceWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_234_AccountBalanceWidgetController).FullName);

        /// <summary>
        /// Returns one page of account rows with the period labels, the paging totals and
        /// the shared bar scale.
        /// </summary>
        /// <param name="pageNo">1-based page; clamped by the model.</param>
        /// <param name="pageSize">Rows per page; clamped by the model to [1,5].</param>
        /// <param name="sortKey">One of the model's SORT_* keys; anything else falls back
        /// to the default.</param>
        /// <returns>JSON-serialized AccountBalanceResult, "" without a session, or { error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetAccountBalances(int pageNo = 1, int pageSize = VAS_234_AccountBalanceModel.DEFAULT_PageSize,
            string sortKey = VAS_234_AccountBalanceModel.SORT_NetVariance)
        {
            string retJSON = "";

            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                try
                {
                    VAS_234_AccountBalanceModel model = new VAS_234_AccountBalanceModel();
                    retJSON = JsonConvert.SerializeObject(model.GetAccountBalances(ctx, pageNo, pageSize, sortKey));
                }
                catch (Exception ex)
                {
                    Log.Log(Level.SEVERE, "VAS_234_AccountBalanceWidget.GetAccountBalances", ex);
                    retJSON = JsonConvert.SerializeObject(new { error = true });
                }
            }

            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
    }
}
