/******************************************************
 * Module Name    : VAS
 * Purpose        : Aging of Unreconciled Items dashboard widget endpoints
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
    /// Module Name : VAS_235_AgingUnreconciledWidget
    /// Purpose     : Thin AJAX endpoint for the Aging of Unreconciled Items widget. All
    ///               business logic lives in VASLogic.Models.VAS_235_AgingUnreconciledModel;
    ///               this action only resolves the session context and serializes the
    ///               model result.
    ///
    ///               The browser sends only a bucket KEY ("b1".."b5") and a page number,
    ///               and neither is authoritative: the key is mapped through a server-side
    ///               switch onto dates the server computed, and an unrecognised key is
    ///               refused rather than defaulted. No aging condition, column name or date
    ///               ever crosses this boundary from the client. The tenant, today's date,
    ///               the four bucket cut-offs, the accounting-schema currency and the role's
    ///               organization access are all resolved server-side, and account numbers
    ///               are already masked by the time they are serialized.
    ///
    ///               TWO ACTIONS. GetAging is the widget - one call on load or refresh,
    ///               carrying BOTH the line count and the converted amount for every bucket
    ///               so the Value / Count toggle re-scales what it already holds instead of
    ///               asking again. GetBucketDetail is the drill-down modal - one call per
    ///               page, so a bucket holding hundreds of payments never arrives at once.
    ///
    ///               No exception detail reaches the browser - a failure serializes as
    ///               { error: true } and is logged with its stack trace server-side.
    /// Chronological development:
    ///   VAI154      2026-09-03 Created
    /// </summary>
    public class VAS_235_AgingUnreconciledWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_235_AgingUnreconciledWidgetController).FullName);

        /// <summary>
        /// Returns the five age buckets with their receipt / payment counts and amounts,
        /// the totals behind the subtitle, the accounts the filter can offer, and the
        /// currency the amounts are stated in.
        /// </summary>
        /// <param name="bankAccountId">C_BankAccount_ID to restrict to, or 0 for all. An id
        /// the role cannot see simply returns nothing - it is an extra equality on top of
        /// the tenant filter and MRole's own access clause, never a way around them.</param>
        /// <returns>JSON-serialized AgingResult, "" without a session, or { error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetAging(int bankAccountId = 0)
        {
            string retJSON = "";

            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                try
                {
                    VAS_235_AgingUnreconciledModel model = new VAS_235_AgingUnreconciledModel();
                    retJSON = JsonConvert.SerializeObject(model.GetAging(ctx, bankAccountId));
                }
                catch (Exception ex)
                {
                    Log.Log(Level.SEVERE, "VAS_235_AgingUnreconciledWidget.GetAging", ex);
                    retJSON = JsonConvert.SerializeObject(new { error = true });
                }
            }

            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Returns one page of the unreconciled payments inside a single age bucket, each
        /// in its own payment currency.
        /// </summary>
        /// <param name="bucket">"b1".."b5"; anything else is refused by the model.</param>
        /// <param name="bankAccountId">The SAME account filter the widget was showing, or 0
        /// for all - so the list can only ever hold the payments behind the number that was
        /// clicked.</param>
        /// <param name="pageNo">1-based page; clamped by the model.</param>
        /// <param name="pageSize">Rows per page; clamped by the model to [1,50].</param>
        /// <returns>JSON-serialized BucketDetailResult, "" without a session, or { error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetBucketDetail(string bucket, int bankAccountId = 0, int pageNo = 1,
            int pageSize = VAS_235_AgingUnreconciledModel.DEFAULT_PageSize)
        {
            string retJSON = "";

            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                try
                {
                    VAS_235_AgingUnreconciledModel model = new VAS_235_AgingUnreconciledModel();
                    retJSON = JsonConvert.SerializeObject(
                        model.GetBucketDetail(ctx, bucket, bankAccountId, pageNo, pageSize));
                }
                catch (Exception ex)
                {
                    Log.Log(Level.SEVERE, "VAS_235_AgingUnreconciledWidget.GetBucketDetail", ex);
                    retJSON = JsonConvert.SerializeObject(new { error = true });
                }
            }

            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
    }
}
