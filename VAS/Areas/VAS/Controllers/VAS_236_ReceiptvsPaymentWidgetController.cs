/******************************************************
 * Module Name    : VAS
 * Purpose        : Receipts vs Payments Trend dashboard widget endpoints
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
    /// Module Name : VAS_236_ReceiptvsPaymentWidget
    /// Purpose     : Thin AJAX endpoint for the Receipts vs Payments Trend widget. All
    ///               business logic lives in VASLogic.Models.VAS_236_ReceiptvsPaymentModel;
    ///               this action only resolves the session context and serializes the
    ///               model result.
    ///
    ///               The browser sends two values and neither is authoritative: a grain
    ///               key and a range in days, both mapped through server-side whitelists.
    ///               An unrecognised grain falls back to daily and an unlisted range to the
    ///               default - the range in particular is what decides how much of the
    ///               ledger is scanned, so it is refused rather than merely clamped. No
    ///               date expression, column name or bucket rule ever crosses this boundary
    ///               from the client. The tenant, today's date, the accounting-schema
    ///               currency and the role's organization access are all resolved
    ///               server-side.
    ///
    ///               ONE call per view. The response carries the whole series - every
    ///               bucket in the range, empty ones included - so the chart redraws on
    ///               resize without asking again.
    ///
    ///               No exception detail reaches the browser - a failure serializes as
    ///               { error: true } and is logged with its stack trace server-side.
    /// Chronological development:
    ///   VAI154      2026-09-03 Created
    /// </summary>
    public class VAS_236_ReceiptvsPaymentWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_236_ReceiptvsPaymentWidgetController).FullName);

        /// <summary>
        /// Returns the receipts / payments / net series for the requested grain and range,
        /// the range totals, and the currency the amounts are stated in.
        /// </summary>
        /// <param name="grain">"day", "week" or "month"; anything else falls back to day.</param>
        /// <param name="rangeDays">7, 14, 30 or 90; anything else falls back to 14.</param>
        /// <returns>JSON-serialized TrendResult, "" without a session, or { error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetTrend(string grain = VAS_236_ReceiptvsPaymentModel.GRAIN_Day,
            int rangeDays = VAS_236_ReceiptvsPaymentModel.RANGE_Default)
        {
            string retJSON = "";

            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                try
                {
                    VAS_236_ReceiptvsPaymentModel model = new VAS_236_ReceiptvsPaymentModel();
                    retJSON = JsonConvert.SerializeObject(model.GetTrend(ctx, grain, rangeDays));
                }
                catch (Exception ex)
                {
                    Log.Log(Level.SEVERE, "VAS_236_ReceiptvsPaymentWidget.GetTrend", ex);
                    retJSON = JsonConvert.SerializeObject(new { error = true });
                }
            }

            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
    }
}
