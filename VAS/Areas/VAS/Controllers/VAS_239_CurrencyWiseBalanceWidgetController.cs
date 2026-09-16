/******************************************************
 * Module Name    : VAS
 * Purpose        : Currency-wise Balance dashboard widget endpoints
 * chronological  : Development
 * Created Date   : 2026-09-04
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
    /// Module Name : VAS_239_CurrencyWiseBalanceWidget
    /// Purpose     : Thin AJAX endpoint for the Currency-wise Balance widget. All business
    ///               logic lives in VASLogic.Models.VAS_239_CurrencyWiseBalanceModel; this
    ///               action only resolves the session context and serializes the result.
    ///
    ///               The browser sends a page number and a page size and NOTHING else - no
    ///               date, no currency, no tenant and no organisation. The as-of date, the
    ///               base currency, the conversion type and the role's organisation access are
    ///               all resolved server-side precisely because a browser value would change
    ///               which balances are reported and what they are worth.
    ///
    ///               ONE call per paint. The response carries the page, the base currency, the
    ///               paging totals and the grand total, because everything after the first is
    ///               a property of the whole set that a single page could not work out for
    ///               itself.
    ///
    ///               No exception detail reaches the browser - a failure serializes as
    ///               { error: true } and is logged with its stack trace server-side.
    /// Chronological development:
    ///   VAI154      2026-09-04 Created
    /// </summary>
    public class VAS_239_CurrencyWiseBalanceWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_239_CurrencyWiseBalanceWidgetController).FullName);

        /// <summary>
        /// Returns one page of currency rows, the base currency they are measured in and the
        /// grand total behind them.
        /// </summary>
        /// <param name="pageNo">1-based page; clamped by the model.</param>
        /// <param name="pageSize">Rows per page; clamped by the model to [1,10].</param>
        /// <returns>JSON-serialized CurrencyBalanceResult, "" without a session, or { error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetCurrencyBalances(int pageNo = 1,
            int pageSize = VAS_239_CurrencyWiseBalanceModel.DEFAULT_PageSize)
        {
            string retJSON = "";

            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                try
                {
                    VAS_239_CurrencyWiseBalanceModel model = new VAS_239_CurrencyWiseBalanceModel();
                    retJSON = JsonConvert.SerializeObject(
                        model.GetCurrencyBalances(ctx, pageNo, pageSize));
                }
                catch (Exception ex)
                {
                    Log.Log(Level.SEVERE, "VAS_239_CurrencyWiseBalanceWidget.GetCurrencyBalances", ex);
                    retJSON = JsonConvert.SerializeObject(new { error = true });
                }
            }

            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
    }
}
