/******************************************************
 * Module Name    : VAS
 * Purpose        : AP Payment Match Suggestions dashboard widget endpoints
 * Chronological  : Development
 * Created Date   : 2026-06-20
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
    /// Module Name : VAS_072_MatchSuggestionAPPayment
    /// Purpose     : Thin AJAX endpoints for the AP Payment Match Suggestions
    ///               dashboard widget. All business logic lives in
    ///               VASLogic.Models.VAS_072_MatchSuggestionAPPaymentModel;
    ///               this controller only resolves the session context and
    ///               serializes the model result.
    /// Chronological development:
    ///   VAI145      2026-06-20 Created
    ///   VAI145      2026-08-05 Query + allocation logic moved to
    ///                          VAS_072_MatchSuggestionAPPaymentModel (the
    ///                          payment-side port of VAS_035); controller
    ///                          reduced to the VAS_035 thin-endpoint shape and
    ///                          the dead ApplyHighConfidence bulk endpoint
    ///                          removed.
    /// </summary>
    public class VAS_072_MatchSuggestionAPPaymentWidgetController : Controller
    {
        /// <summary>
        /// One page of payment↔invoice match suggestions (best-fit purchase
        /// invoice schedule per unallocated vendor payment, HIGH before REVIEW
        /// before LOW), plus the full-set count, the payment-currency total
        /// ready to allocate and the Allocation form id used by the header
        /// Open-allocation-form action.
        /// </summary>
        /// <param name="pageNo">1-based page number.</param>
        /// <param name="pageSize">Rows per page (default 6).</param>
        /// <returns>JSON-serialized MatchSuggestionList, or "" when no session.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetMatchSuggestions(int pageNo = 1, int pageSize = 6)
        {
            string retJSON = "";

            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_072_MatchSuggestionAPPaymentModel model = new VAS_072_MatchSuggestionAPPaymentModel();
                retJSON = JsonConvert.SerializeObject(model.GetMatchSuggestions(ctx, pageNo, pageSize));
            }

            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Match-review detail for one pairing: payment pane, suggested-invoice
        /// pane (the suggested pay schedule only), balance after apply and the
        /// four "why this match" signals with the evidence score.
        /// </summary>
        /// <param name="paymentId">C_Payment_ID of the payment side.</param>
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
                VAS_072_MatchSuggestionAPPaymentModel model = new VAS_072_MatchSuggestionAPPaymentModel();
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
        /// <param name="paymentId">C_Payment_ID of the payment to apply.</param>
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
                VAS_072_MatchSuggestionAPPaymentModel model = new VAS_072_MatchSuggestionAPPaymentModel();
                retJSON = JsonConvert.SerializeObject(model.ApplyAllocation(ctx, paymentId, invoiceId, payScheduleId));
            }

            return Json(retJSON);
        }
    }
}
