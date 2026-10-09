/******************************************************
 * Module Name    : VAS
 * Purpose        : Payment Allocation Status dashboard widget endpoints
 * chronological  : Development
 * Created Date   : 2026-08-20
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
    /// Module Name : VAS_199_PaymentAllocationStatusWidget
    /// Purpose     : Thin AJAX endpoints for the Payment Allocation Status widget.
    ///               All business logic lives in
    ///               VASLogic.Models.VAS_199_PaymentAllocationStatusModel; these
    ///               actions only resolve the session context, pass the selected ids
    ///               through and serialize the model result. The tenant always comes
    ///               from the authenticated context - the client never supplies
    ///               AD_Client_ID - and the selected period is re-validated
    ///               server-side (still active, still open) inside the model before
    ///               any payment is read.
    ///               Three actions, one per interaction: bootstrap on load, counts
    ///               on a period change, and one page of detail rows when a category
    ///               is opened. Detail rows are never fetched with the counts.
    /// Chronological development:
    ///   VAI154      2026-08-20 Created
    /// </summary>
    public class VAS_199_PaymentAllocationStatusWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_199_PaymentAllocationStatusWidgetController).FullName);

        /// <summary>
        /// Returns the selectable open periods, the period to preselect and its
        /// three category counts.
        /// </summary>
        /// <returns>JSON-serialized StatusBootstrap, "" without a session, or { error }.</returns>
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
                    VAS_199_PaymentAllocationStatusModel model = new VAS_199_PaymentAllocationStatusModel();
                    retJSON = JsonConvert.SerializeObject(model.GetBootstrap(ctx));
                }
                catch (Exception ex)
                {
                    Log.Log(Level.SEVERE, "VAS_199_PaymentAllocationStatusWidget.GetBootstrap", ex);
                    retJSON = JsonConvert.SerializeObject(new { error = true });
                }
            }

            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Returns the three category counts of one period.
        /// </summary>
        /// <param name="periodId">C_Period_ID selected by the user.</param>
        /// <returns>JSON-serialized CategoryCounts, or { error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetCounts(int periodId)
        {
            string retJSON = "";

            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                try
                {
                    VAS_199_PaymentAllocationStatusModel model = new VAS_199_PaymentAllocationStatusModel();
                    retJSON = JsonConvert.SerializeObject(model.GetCounts(ctx, periodId));
                }
                catch (Exception ex)
                {
                    Log.Log(Level.SEVERE, "VAS_199_PaymentAllocationStatusWidget.GetCounts", ex);
                    retJSON = JsonConvert.SerializeObject(new { error = true });
                }
            }

            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Returns one page of the payments behind a category, plus the total count.
        /// The page size is clamped by the model, so a crafted request cannot pull
        /// the whole table in one response.
        /// </summary>
        /// <param name="periodId">C_Period_ID selected by the user.</param>
        /// <param name="category">Category token: SETTLE, ADVANCE or ALLOC.</param>
        /// <param name="pageNo">1-based page number.</param>
        /// <param name="pageSize">Requested rows per page.</param>
        /// <returns>JSON-serialized PaymentPage, or { error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetPayments(int periodId, string category, int pageNo, int pageSize)
        {
            string retJSON = "";

            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                try
                {
                    VAS_199_PaymentAllocationStatusModel model = new VAS_199_PaymentAllocationStatusModel();
                    retJSON = JsonConvert.SerializeObject(
                        model.GetPayments(ctx, periodId, category, pageNo, pageSize));
                }
                catch (Exception ex)
                {
                    Log.Log(Level.SEVERE, "VAS_199_PaymentAllocationStatusWidget.GetPayments", ex);
                    retJSON = JsonConvert.SerializeObject(new { error = true });
                }
            }

            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
    }
}
