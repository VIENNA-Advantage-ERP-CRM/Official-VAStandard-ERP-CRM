/********************************************************
 * Module Name    : VAS
 * Purpose        : Sales Quotation Right Detail Panel — AJAX controller
 * Employee Code  : VAI154
 * Date           : 20-Jul-2026
 ******************************************************/

using Newtonsoft.Json;
using System.Collections.Generic;
using System.Web.Mvc;
using VAdvantage.Model;
using VAdvantage.Utility;
using VAS.Models;
using VASLogic.Models;
using VIS.Filters;
using VIS.Models;

namespace VAS.Controllers
{
    /// <summary>
    /// Module Name : VAS
    /// Purpose     : Sales Quotation Right Detail Panel — AJAX controller
    /// Chronological development:
    ///   VAI154  20-Jul-2026
    /// </summary>
    public class VAS_123_QuotationRightPanelController : Controller
    {
        // ─────────────────────────────────────────────────────────
        // §1  GetHeader
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the header details of the selected Sales Quotation.
        /// </summary>
        /// <param name="C_Order_ID">C_Order_ID of the Sales Quotation</param>
        /// <returns>Double-serialized JSON result</returns>
        [HttpPost]
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetHeader(int C_Order_ID)
        {
            // Reject invalid or missing primary key immediately — no DB call needed.
            int orderId = Util.GetValueOfInt(C_Order_ID);
            if (orderId <= 0)
                return Json(JsonConvert.SerializeObject(new { error = "Invalid C_Order_ID" }), JsonRequestBehavior.AllowGet);

            if (Session["ctx"] == null)
                return Json(JsonConvert.SerializeObject(new { error = "session_expired" }), JsonRequestBehavior.AllowGet);

            Ctx ctx = Session["ctx"] as Ctx;
            VAS_123_QuotationRightPanelModel model = new VAS_123_QuotationRightPanelModel();
            object result = model.GetHeader(ctx, orderId);
            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }

        // ─────────────────────────────────────────────────────────
        // §2  GetNextMeeting
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the next upcoming meeting linked to the selected Sales Quotation.
        /// A null result means no upcoming meeting is scheduled.
        /// </summary>
        /// <param name="C_Order_ID">C_Order_ID of the Sales Quotation</param>
        /// <returns>Double-serialized JSON result</returns>
        [HttpPost]
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetNextMeeting(int C_Order_ID)
        {
            int orderId = Util.GetValueOfInt(C_Order_ID);
            if (orderId <= 0)
                return Json(JsonConvert.SerializeObject(new { error = "Invalid C_Order_ID" }), JsonRequestBehavior.AllowGet);

            if (Session["ctx"] == null)
                return Json(JsonConvert.SerializeObject(new { error = "session_expired" }), JsonRequestBehavior.AllowGet);

            Ctx ctx = Session["ctx"] as Ctx;
            VAS_123_QuotationRightPanelModel model = new VAS_123_QuotationRightPanelModel();

            // Resolve the AD_Table_ID for C_Order so the meeting query can filter by
            // the correct record-table combination without a hard-coded constant.
            int tableId = model.GetCOrderTableId(ctx);

            // A null return value means no upcoming meeting — pass it through as-is.
            object result = model.GetNextMeeting(ctx, orderId, tableId);
            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }

        // ─────────────────────────────────────────────────────────
        // §3  ExecuteDocAction
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// Executes a document action (Complete, Reverse, Void, Close) on the
        /// selected Sales Quotation. Only the four allowed action codes are accepted;
        /// any other value is rejected with an error response.
        /// </summary>
        /// <param name="C_Order_ID">C_Order_ID of the Sales Quotation</param>
        /// <param name="DocAction">Action code: CO (Complete), RE (Reverse), VO (Void), CL (Close)</param>
        /// <returns>Double-serialized JSON result</returns>
        [HttpPost]
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult ExecuteDocAction(int C_Order_ID, string DocAction)
        {
            int orderId = Util.GetValueOfInt(C_Order_ID);
            if (orderId <= 0)
                return Json(JsonConvert.SerializeObject(new { error = "Invalid C_Order_ID" }), JsonRequestBehavior.AllowGet);

            // Whitelist check — only the four permitted action codes are allowed.
            // Reject anything else to prevent unintended document transitions.
            string docAction = Util.GetValueOfString(DocAction);
            if (docAction != "CO" && docAction != "RE" && docAction != "VO" && docAction != "CL")
                return Json(JsonConvert.SerializeObject(new { error = "Invalid DocAction. Allowed values: CO, RE, VO, CL" }), JsonRequestBehavior.AllowGet);

            if (Session["ctx"] == null)
                return Json(JsonConvert.SerializeObject(new { error = "session_expired" }), JsonRequestBehavior.AllowGet);

            Ctx ctx = Session["ctx"] as Ctx;
            VAS_123_QuotationRightPanelModel model = new VAS_123_QuotationRightPanelModel();
            object result = model.ExecuteDocAction(ctx, orderId, docAction);
            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }

        // ─────────────────────────────────────────────────────────
        // §4  ConvertToSalesOrder
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// Converts the selected Sales Quotation into a confirmed Sales Order.
        /// </summary>
        /// <param name="C_Order_ID">C_Order_ID of the Sales Quotation</param>
        /// <returns>Double-serialized JSON result</returns>
        [HttpPost]
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult ConvertToSalesOrder(int C_Order_ID)
        {
            int orderId = Util.GetValueOfInt(C_Order_ID);
            if (orderId <= 0)
                return Json(JsonConvert.SerializeObject(new { error = "Invalid C_Order_ID" }), JsonRequestBehavior.AllowGet);

            if (Session["ctx"] == null)
                return Json(JsonConvert.SerializeObject(new { error = "session_expired" }), JsonRequestBehavior.AllowGet);

            Ctx ctx = Session["ctx"] as Ctx;
            VAS_123_QuotationRightPanelModel model = new VAS_123_QuotationRightPanelModel();
            object result = model.ConvertToSalesOrder(ctx, orderId);
            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }

        // ─────────────────────────────────────────────────────────────────────
        // §5  GetGeneratedOrders
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns all sales orders generated from the selected Sales Quotation,
        /// identified by Ref_Order_ID = C_Order_ID of the quotation.
        /// </summary>
        /// <param name="C_Order_ID">C_Order_ID of the Sales Quotation</param>
        /// <returns>Double-serialized JSON array of order rows</returns>
        [HttpPost]
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetGeneratedOrders(int C_Order_ID)
        {
            int orderId = Util.GetValueOfInt(C_Order_ID);
            if (orderId <= 0)
                return Json(JsonConvert.SerializeObject(new { error = "Invalid C_Order_ID" }), JsonRequestBehavior.AllowGet);

            if (Session["ctx"] == null)
                return Json(JsonConvert.SerializeObject(new { error = "session_expired" }), JsonRequestBehavior.AllowGet);

            Ctx ctx = Session["ctx"] as Ctx;
            VAS_123_QuotationRightPanelModel model = new VAS_123_QuotationRightPanelModel();
            object result = model.GetGeneratedOrders(ctx, orderId);
            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }

        // ─────────────────────────────────────────────────────────────────────
        // §6  GetOpportunity
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the VAS_Opportunity_ID linked to the quotation, or null when none is set.
        /// </summary>
        /// <param name="C_Order_ID">C_Order_ID of the Sales Quotation</param>
        /// <returns>Double-serialized JSON object with vAS_Opportunity_ID, or null</returns>
        [HttpPost]
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetOpportunity(int C_Order_ID)
        {
            int orderId = Util.GetValueOfInt(C_Order_ID);
            if (orderId <= 0)
                return Json(JsonConvert.SerializeObject(new { error = "Invalid C_Order_ID" }), JsonRequestBehavior.AllowGet);

            if (Session["ctx"] == null)
                return Json(JsonConvert.SerializeObject(new { error = "session_expired" }), JsonRequestBehavior.AllowGet);

            Ctx ctx = Session["ctx"] as Ctx;
            VAS_123_QuotationRightPanelModel model = new VAS_123_QuotationRightPanelModel();
            object result = model.GetOpportunity(ctx, orderId);
            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }

        // ─────────────────────────────────────────────────────────────────────
        // §7  GetQuotationLines
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns all active quotation lines for the selected Sales Quotation.
        /// </summary>
        /// <param name="C_Order_ID">C_Order_ID of the Sales Quotation</param>
        /// <returns>Double-serialized JSON array of line rows</returns>
        [HttpPost]
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetQuotationLines(int C_Order_ID)
        {
            int orderId = Util.GetValueOfInt(C_Order_ID);
            if (orderId <= 0)
                return Json(JsonConvert.SerializeObject(new { error = "Invalid C_Order_ID" }), JsonRequestBehavior.AllowGet);

            if (Session["ctx"] == null)
                return Json(JsonConvert.SerializeObject(new { error = "session_expired" }), JsonRequestBehavior.AllowGet);

            Ctx ctx = Session["ctx"] as Ctx;
            VAS_123_QuotationRightPanelModel model = new VAS_123_QuotationRightPanelModel();
            object result = model.GetQuotationLines(ctx, orderId);
            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }

        // ─────────────────────────────────────────────────────────────────────
        // §8  GetAddresses
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the billing and shipping addresses selected on the quotation
        /// (Bill_Location_ID and C_BPartner_Location_ID).
        /// </summary>
        /// <param name="C_Order_ID">C_Order_ID of the Sales Quotation</param>
        /// <returns>Double-serialized JSON object with billing and shipping address fields</returns>
        [HttpPost]
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetAddresses(int C_Order_ID)
        {
            int orderId = Util.GetValueOfInt(C_Order_ID);
            if (orderId <= 0)
                return Json(JsonConvert.SerializeObject(new { error = "Invalid C_Order_ID" }), JsonRequestBehavior.AllowGet);

            if (Session["ctx"] == null)
                return Json(JsonConvert.SerializeObject(new { error = "session_expired" }), JsonRequestBehavior.AllowGet);

            Ctx ctx = Session["ctx"] as Ctx;
            VAS_123_QuotationRightPanelModel model = new VAS_123_QuotationRightPanelModel();
            object result = model.GetAddresses(ctx, orderId);
            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }

        // ─────────────────────────────────────────────────────────────────────
        // §9  GetOpenOpportunities
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns open opportunities available for the customer to link to the quotation.
        /// Currently returns an empty list — the Opportunity table mapping is not yet confirmed.
        /// </summary>
        /// <param name="C_BPartner_ID">C_BPartner_ID of the customer</param>
        /// <returns>Double-serialized JSON array (empty until Opportunity mapping is confirmed)</returns>
        [HttpPost]
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetOpenOpportunities(int C_BPartner_ID)
        {
            int bPartnerId = Util.GetValueOfInt(C_BPartner_ID);
            if (bPartnerId <= 0)
                return Json(JsonConvert.SerializeObject(new { error = "Invalid C_BPartner_ID" }), JsonRequestBehavior.AllowGet);

            if (Session["ctx"] == null)
                return Json(JsonConvert.SerializeObject(new { error = "session_expired" }), JsonRequestBehavior.AllowGet);

            Ctx ctx = Session["ctx"] as Ctx;
            VAS_123_QuotationRightPanelModel model = new VAS_123_QuotationRightPanelModel();
            object result = model.GetOpenOpportunities(ctx, bPartnerId);
            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }

        // ─────────────────────────────────────────────────────────────────────
        // §10  LinkOpportunity
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Links the given opportunity to the quotation by writing VAS_Opportunity_ID
        /// on the C_Order record.
        /// </summary>
        /// <param name="C_Order_ID">C_Order_ID of the Sales Quotation</param>
        /// <param name="VAS_Opportunity_ID">Opportunity ID to link</param>
        /// <returns>Double-serialized JSON result with success flag</returns>
        [HttpPost]
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult LinkOpportunity(int C_Order_ID, int VAS_Opportunity_ID)
        {
            int orderId        = Util.GetValueOfInt(C_Order_ID);
            int opportunityId  = Util.GetValueOfInt(VAS_Opportunity_ID);

            if (orderId <= 0)
                return Json(JsonConvert.SerializeObject(new { error = "Invalid C_Order_ID" }), JsonRequestBehavior.AllowGet);

            if (opportunityId <= 0)
                return Json(JsonConvert.SerializeObject(new { error = "Invalid VAS_Opportunity_ID" }), JsonRequestBehavior.AllowGet);

            if (Session["ctx"] == null)
                return Json(JsonConvert.SerializeObject(new { error = "session_expired" }), JsonRequestBehavior.AllowGet);

            Ctx ctx = Session["ctx"] as Ctx;
            VAS_123_QuotationRightPanelModel model = new VAS_123_QuotationRightPanelModel();
            object result = model.LinkOpportunity(ctx, orderId, opportunityId);
            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }

        // ─────────────────────────────────────────────────────────────────────
        // §11  GetPricingTerms
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns pricing and payment-terms data for the selected Sales Quotation.
        /// Isolated from GetHeader so that a missing column in older schema deployments
        /// (PaymentRule, PriorityRule) only affects the terms section.
        /// </summary>
        /// <param name="C_Order_ID">C_Order_ID of the Sales Quotation</param>
        /// <returns>Double-serialized JSON object with pricing/terms fields, or null</returns>
        [HttpPost]
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetPricingTerms(int C_Order_ID)
        {
            int orderId = Util.GetValueOfInt(C_Order_ID);
            if (orderId <= 0)
                return Json(JsonConvert.SerializeObject(new { error = "Invalid C_Order_ID" }), JsonRequestBehavior.AllowGet);

            if (Session["ctx"] == null)
                return Json(JsonConvert.SerializeObject(new { error = "session_expired" }), JsonRequestBehavior.AllowGet);

            Ctx ctx = Session["ctx"] as Ctx;
            VAS_123_QuotationRightPanelModel model = new VAS_123_QuotationRightPanelModel();
            object result = model.GetPricingTerms(ctx, orderId);
            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }

        // ─────────────────────────────────────────────────────────────────────
        // §13  GetTasks
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns all R_Request tasks linked to the selected Sales Quotation.
        /// If the R_Request table is not present in the schema, returns an empty list.
        /// </summary>
        /// <param name="C_Order_ID">C_Order_ID of the Sales Quotation</param>
        /// <returns>Double-serialized JSON array of task rows</returns>
        [HttpPost]
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetTasks(int C_Order_ID)
        {
            int orderId = Util.GetValueOfInt(C_Order_ID);
            if (orderId <= 0)
                return Json(JsonConvert.SerializeObject(new { error = "Invalid C_Order_ID" }), JsonRequestBehavior.AllowGet);

            if (Session["ctx"] == null)
                return Json(JsonConvert.SerializeObject(new { error = "session_expired" }), JsonRequestBehavior.AllowGet);

            Ctx ctx = Session["ctx"] as Ctx;
            VAS_123_QuotationRightPanelModel model = new VAS_123_QuotationRightPanelModel();

            // Resolve the AD_Table_ID for C_Order so the task query filters by the correct table.
            int tableId = model.GetCOrderTableId(ctx);

            object result = model.GetTasks(ctx, orderId, tableId);
            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }

        // ─────────────────────────────────────────────────────────────────────
        // §14  GetEngagement
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the combined engagement timeline (meetings, notes, emails, calls, WhatsApp)
        /// linked to the selected Sales Quotation.  Response format: { counts, items }.
        /// </summary>
        /// <param name="C_Order_ID">C_Order_ID of the Sales Quotation</param>
        /// <returns>Double-serialized JSON object with counts and items array</returns>
        [HttpPost]
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetEngagement(int C_Order_ID)
        {
            int orderId = Util.GetValueOfInt(C_Order_ID);
            if (orderId <= 0)
                return Json(JsonConvert.SerializeObject(new { error = "Invalid C_Order_ID" }), JsonRequestBehavior.AllowGet);

            if (Session["ctx"] == null)
                return Json(JsonConvert.SerializeObject(new { error = "session_expired" }), JsonRequestBehavior.AllowGet);

            Ctx ctx = Session["ctx"] as Ctx;
            VAS_123_QuotationRightPanelModel model = new VAS_123_QuotationRightPanelModel();
            int tableId = model.GetCOrderTableId(ctx);
            object result = model.GetEngagement(ctx, orderId, tableId);
            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }

        // ─────────────────────────────────────────────────────────────────────
        // §15  PostNote
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Saves a new note against the selected Sales Quotation in the AD_Note table.
        /// </summary>
        /// <param name="C_Order_ID">C_Order_ID of the Sales Quotation</param>
        /// <param name="NoteText">Plain-text note content (required, max 2000 chars)</param>
        /// <returns>Double-serialized JSON object with a success flag or error message</returns>
        [HttpPost]
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult PostNote(int C_Order_ID, string NoteText)
        {
            int orderId = Util.GetValueOfInt(C_Order_ID);
            if (orderId <= 0)
                return Json(JsonConvert.SerializeObject(new { error = "Invalid C_Order_ID" }), JsonRequestBehavior.AllowGet);

            string noteText = Util.GetValueOfString(NoteText);
            if (string.IsNullOrWhiteSpace(noteText))
                return Json(JsonConvert.SerializeObject(new { error = "NoteText is required" }), JsonRequestBehavior.AllowGet);

            if (Session["ctx"] == null)
                return Json(JsonConvert.SerializeObject(new { error = "session_expired" }), JsonRequestBehavior.AllowGet);

            Ctx ctx = Session["ctx"] as Ctx;
            VAS_123_QuotationRightPanelModel model = new VAS_123_QuotationRightPanelModel();
            int tableId = model.GetCOrderTableId(ctx);
            object result = model.PostNote(ctx, orderId, tableId, noteText);
            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }

        // ─────────────────────────────────────────────────────────────────────
        // §19  GetNoteDetail
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns full content of a single CM_ChatEntry note for the engagement modal.
        /// </summary>
        /// <param name="noteId">CM_ChatEntry_ID to retrieve</param>
        /// <returns>Double-serialized JSON with id, title, body, whenTs, who</returns>
        [HttpPost]
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetNoteDetail(int noteId)
        {
            int safeId = Util.GetValueOfInt(noteId);
            if (safeId <= 0)
                return Json(JsonConvert.SerializeObject(new { id = 0 }), JsonRequestBehavior.AllowGet);

            if (Session["ctx"] == null)
                return Json(JsonConvert.SerializeObject(new { error = "session_expired" }), JsonRequestBehavior.AllowGet);

            Ctx ctx = Session["ctx"] as Ctx;
            VAS_123_QuotationRightPanelModel model = new VAS_123_QuotationRightPanelModel();
            object result = model.GetNoteDetail(ctx, safeId);
            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }

        // ─────────────────────────────────────────────────────────────────────
        // §20  GetEmailDetail
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns full content of a single MailAttachment1 email for the engagement modal.
        /// </summary>
        /// <param name="emailId">MailAttachment1_ID to retrieve</param>
        /// <returns>Double-serialized JSON with id, subject, body, whenTs, direction, fromEmail, toEmail, who</returns>
        [HttpPost]
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetEmailDetail(int emailId)
        {
            int safeId = Util.GetValueOfInt(emailId);
            if (safeId <= 0)
                return Json(JsonConvert.SerializeObject(new { id = 0 }), JsonRequestBehavior.AllowGet);

            if (Session["ctx"] == null)
                return Json(JsonConvert.SerializeObject(new { error = "session_expired" }), JsonRequestBehavior.AllowGet);

            Ctx ctx = Session["ctx"] as Ctx;
            VAS_123_QuotationRightPanelModel model = new VAS_123_QuotationRightPanelModel();
            object result = model.GetEmailDetail(ctx, safeId);
            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }

        // ─────────────────────────────────────────────────────────────────────
        // §21  GetMeetingDetail
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns full meeting detail including transcript and attendees for the engagement modal.
        /// </summary>
        /// <param name="meetingId">AppointmentsInfo_ID to retrieve</param>
        /// <returns>Double-serialized JSON with id, subject, startDate, endDate, location, meetingUrl, comments, transcript, attendees, durationMins</returns>
        [HttpPost]
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetMeetingDetail(int meetingId)
        {
            int safeId = Util.GetValueOfInt(meetingId);
            if (safeId <= 0)
                return Json(JsonConvert.SerializeObject(new { error = "Invalid meeting ID" }), JsonRequestBehavior.AllowGet);

            if (Session["ctx"] == null)
                return Json(JsonConvert.SerializeObject(new { error = "session_expired" }), JsonRequestBehavior.AllowGet);

            Ctx ctx = Session["ctx"] as Ctx;
            VAS_123_QuotationRightPanelModel model = new VAS_123_QuotationRightPanelModel();
            object result = model.GetMeetingDetail(ctx, safeId);
            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }

        // ─────────────────────────────────────────────────────────────────────
        // §22  SaveMeetingComments
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Saves comments and meeting URL on an AppointmentsInfo meeting record.
        /// </summary>
        /// <param name="meetingId">AppointmentsInfo_ID to update</param>
        /// <param name="comments">New comments text</param>
        /// <param name="meetingUrl">New meeting URL</param>
        /// <returns>Double-serialized JSON with success flag</returns>
        [HttpPost]
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult SaveMeetingComments(int meetingId, string comments, string meetingUrl)
        {
            int safeId = Util.GetValueOfInt(meetingId);
            if (safeId <= 0)
                return Json(JsonConvert.SerializeObject(new { error = "Invalid meeting ID" }), JsonRequestBehavior.AllowGet);

            if (Session["ctx"] == null)
                return Json(JsonConvert.SerializeObject(new { error = "session_expired" }), JsonRequestBehavior.AllowGet);

            Ctx ctx = Session["ctx"] as Ctx;
            VAS_123_QuotationRightPanelModel model = new VAS_123_QuotationRightPanelModel();
            object result = model.SaveMeetingComments(ctx, safeId,
                Util.GetValueOfString(comments), Util.GetValueOfString(meetingUrl));
            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }

        // ─────────────────────────────────────────────────────────────────────
        // §23  GetWhatsAppChat
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the most recent WhatsApp chat topic and messages linked to the quotation.
        /// </summary>
        /// <param name="C_Order_ID">C_Order_ID of the Sales Quotation</param>
        /// <param name="topicId">Specific WSP_SMChatTopic_ID; 0 = most recent for the order</param>
        /// <returns>Double-serialized JSON with topic and messages</returns>
        [HttpPost]
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetWhatsAppChat(int C_Order_ID, int topicId = 0)
        {
            int orderId     = Util.GetValueOfInt(C_Order_ID);
            int safeTopicId = Util.GetValueOfInt(topicId);
            if (orderId <= 0)
                return Json(JsonConvert.SerializeObject(new { error = "Invalid C_Order_ID" }), JsonRequestBehavior.AllowGet);

            if (Session["ctx"] == null)
                return Json(JsonConvert.SerializeObject(new { error = "session_expired" }), JsonRequestBehavior.AllowGet);

            Ctx ctx = Session["ctx"] as Ctx;
            VAS_123_QuotationRightPanelModel model = new VAS_123_QuotationRightPanelModel();
            int tableId = model.GetCOrderTableId(ctx);
            object result = model.GetWhatsAppChat(ctx, orderId, tableId, safeTopicId);
            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }

        // ─────────────────────────────────────────────────────────────────────
        // §24  GetWhatsAppTopicMeta
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns chatId and mobile number for a WSP chat topic.
        /// Used by the send-message flow in the WhatsApp engagement modal.
        /// </summary>
        /// <param name="topicId">WSP_SMChatTopic_ID to look up</param>
        /// <returns>Double-serialized JSON with chatId and mobile</returns>
        [HttpPost]
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetWhatsAppTopicMeta(int topicId)
        {
            int safeId = Util.GetValueOfInt(topicId);
            if (safeId <= 0)
                return Json(JsonConvert.SerializeObject(new { chatId = 0, mobile = "" }), JsonRequestBehavior.AllowGet);

            if (Session["ctx"] == null)
                return Json(JsonConvert.SerializeObject(new { error = "session_expired" }), JsonRequestBehavior.AllowGet);

            Ctx ctx = Session["ctx"] as Ctx;
            VAS_123_QuotationRightPanelModel model = new VAS_123_QuotationRightPanelModel();
            object result = model.GetWhatsAppTopicMeta(ctx, safeId);
            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }

        // ─────────────────────────────────────────────────────────────────────
        // §25  GetLinkedQuotations
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns all active Sales Quotations linked to the given opportunity,
        /// so the user can see which other quotations share the same opportunity.
        /// </summary>
        /// <param name="VAS_Opportunity_ID">The opportunity whose linked quotations are required</param>
        /// <returns>Double-serialized JSON array of quotation rows</returns>
        [HttpPost]
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetLinkedQuotations(int VAS_Opportunity_ID)
        {
            int opportunityId = Util.GetValueOfInt(VAS_Opportunity_ID);
            if (opportunityId <= 0)
                return Json(JsonConvert.SerializeObject(new { error = "Invalid VAS_Opportunity_ID" }), JsonRequestBehavior.AllowGet);

            if (Session["ctx"] == null)
                return Json(JsonConvert.SerializeObject(new { error = "session_expired" }), JsonRequestBehavior.AllowGet);

            Ctx ctx = Session["ctx"] as Ctx;
            VAS_123_QuotationRightPanelModel model = new VAS_123_QuotationRightPanelModel();
            object result = model.GetLinkedQuotations(ctx, opportunityId);
            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }

        // ─────────────────────────────────────────────────────────────────────
        // §26  GetLineHistory
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns C_OrderLineHistory rows for all lines of the Sales Quotation,
        /// newest change first per line. The JS client groups them by C_OrderLine_ID
        /// and renders a collapsible history drawer below each line card.
        /// </summary>
        /// <param name="C_Order_ID">C_Order_ID of the Sales Quotation</param>
        /// <returns>Double-serialized JSON array of history rows</returns>
        [HttpPost]
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetLineHistory(int C_Order_ID)
        {
            int orderId = Util.GetValueOfInt(C_Order_ID);
            if (orderId <= 0)
                return Json(JsonConvert.SerializeObject(new { error = "Invalid C_Order_ID" }), JsonRequestBehavior.AllowGet);

            if (Session["ctx"] == null)
                return Json(JsonConvert.SerializeObject(new { error = "session_expired" }), JsonRequestBehavior.AllowGet);

            Ctx ctx = Session["ctx"] as Ctx;
            VAS_123_QuotationRightPanelModel model = new VAS_123_QuotationRightPanelModel();
            object result = model.GetLineHistory(ctx, orderId);
            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }

        // §16  GetUserSuggest
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns up to 10 user-directory suggestions matching the supplied query string,
        /// excluding the user IDs listed in ExcludeIds (comma-separated integers).
        /// Used by the assignee typeahead in the Task form modal.
        /// </summary>
        /// <param name="Query">Partial name or email to search (required)</param>
        /// <param name="ExcludeIds">Comma-separated AD_User_IDs to omit from results</param>
        /// <returns>Double-serialized JSON array of user suggestion rows</returns>
        [HttpPost]
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetUserSuggest(string Query, string ExcludeIds)
        {
            string query = Util.GetValueOfString(Query);
            if (string.IsNullOrWhiteSpace(query))
                return Json(JsonConvert.SerializeObject(new List<object>()), JsonRequestBehavior.AllowGet);

            if (Session["ctx"] == null)
                return Json(JsonConvert.SerializeObject(new { error = "session_expired" }), JsonRequestBehavior.AllowGet);

            string excludeIds = Util.GetValueOfString(ExcludeIds);
            Ctx ctx = Session["ctx"] as Ctx;
            VAS_123_QuotationRightPanelModel model = new VAS_123_QuotationRightPanelModel();
            object result = model.GetUserSuggest(ctx, query, excludeIds);
            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }
    }
}
