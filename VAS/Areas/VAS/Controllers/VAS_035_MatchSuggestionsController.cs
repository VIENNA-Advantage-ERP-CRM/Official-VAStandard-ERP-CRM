/******************************************************
 * Module Name    : VAS
 * Purpose        : Match Suggestions dashboard widget endpoints (Widget 19)
 * chronological  : Development
 * Created Date   : 2026-06-04
 * Created by     : VAI145
 ******************************************************/

using Newtonsoft.Json;
using System.Web.Mvc;
using VAdvantage.Utility;
using VASLogic.Models;
using VIS.Filters;

namespace VAS.Controllers
{
    /// <summary>
    /// Module Name : VAS_035_MatchSuggestions
    /// Purpose     : Thin AJAX endpoints for the Match Suggestions dashboard
    ///               widget. All business logic lives in
    ///               VASLogic.Models.VAS_035_MatchSuggestionsModel; this
    ///               controller only resolves the session context and serializes
    ///               the model result.
    /// Chronological development:
    ///   VAI145      2026-06-04 Created
    /// </summary>
    public class VAS_035_MatchSuggestionsController : Controller
    {
        /// <summary>
        /// One page of receipt↔invoice match suggestions (best-fit invoice per
        /// unallocated receipt, HIGH before REVIEW), plus the full-set count,
        /// the accounting-currency total ready to allocate and the Allocation
        /// form id used by the Apply actions.
        /// </summary>
        /// <param name="pageNo">1-based page number.</param>
        /// <param name="pageSize">Rows per page (default 5).</param>
        /// <returns>JSON-serialized MatchSuggestionList, or "" when no session.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetMatchSuggestions(int pageNo = 1, int pageSize = 5)
        {
            string retJSON = "";

            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_035_MatchSuggestionsModel model = new VAS_035_MatchSuggestionsModel();
                retJSON = JsonConvert.SerializeObject(model.GetMatchSuggestions(ctx, pageNo, pageSize));
            }

            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Match-review detail for one pairing: payment (receipt) pane,
        /// suggested-invoice pane (the suggested pay schedule only), balance
        /// after apply and the four "why this match" signals with the evidence
        /// score.
        /// </summary>
        /// <param name="paymentId">C_Payment_ID of the receipt side.</param>
        /// <param name="invoiceId">C_Invoice_ID of the suggested invoice.</param>
        /// <param name="payScheduleId">C_InvoicePaySchedule_ID the suggestion was made with.</param>
        /// <returns>JSON-serialized MatchDetail (null when not found), or "" when no session.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetMatchDetail(int paymentId = 0, int invoiceId = 0, int payScheduleId = 0)
        {
            string retJSON = "";

            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_035_MatchSuggestionsModel model = new VAS_035_MatchSuggestionsModel();
                retJSON = JsonConvert.SerializeObject(model.GetMatchDetail(ctx, paymentId, invoiceId, payScheduleId));
            }

            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Applies one match suggestion: creates and completes a C_AllocationHdr
        /// with a single line against exactly the suggested invoice pay
        /// schedule, dated on the payment accounting date — the same outcome as
        /// the standard Allocation form.
        /// </summary>
        /// <param name="paymentId">C_Payment_ID of the receipt to apply.</param>
        /// <param name="invoiceId">C_Invoice_ID of the suggested invoice.</param>
        /// <param name="payScheduleId">C_InvoicePaySchedule_ID the suggestion was made with — the only schedule allocated.</param>
        /// <returns>JSON-serialized ApplyResult { Success, DocumentNo, Message }, or "" when no session.</returns>
        [HttpPost]
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult ApplyAllocation(int paymentId = 0, int invoiceId = 0, int payScheduleId = 0)
        {
            string retJSON = "";

            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_035_MatchSuggestionsModel model = new VAS_035_MatchSuggestionsModel();
                retJSON = JsonConvert.SerializeObject(model.ApplyAllocation(ctx, paymentId, invoiceId, payScheduleId));
            }

            return Json(retJSON);
        }
    }
}
