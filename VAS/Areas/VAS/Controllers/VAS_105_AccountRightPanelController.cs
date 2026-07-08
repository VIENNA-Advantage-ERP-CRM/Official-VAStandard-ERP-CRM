/********************************************************
 * Module Name    : CRM Extension VAS
 * Purpose        : Account Right Detail Panel — controller
 * Employee Code  : VAI154
 * Date           : 09-Jun-2026
 ******************************************************/
using System;
using System.Web.Mvc;
using Newtonsoft.Json;
using VAdvantage.Utility;
using VAS.Models;

namespace VAS.Controllers
{
    /// <summary>
    /// Module Name   : CRM Extension VAS
    /// Purpose       : Account Right Detail Panel controller
    /// Chronological development:
    ///   VAI154  09-Jun-2026
    /// </summary>
    public class VAS_105_AccountRightPanelController : Controller
    {
        // ─────────────────────────────────────────────────────────
        // §1  Overview
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// Returns account header, company info and key facts for the selected account.
        /// </summary>
        /// <param name="bPartnerId">C_BPartner_ID of the selected customer account.</param>
        /// <returns>Double-serialized JSON with account overview data.</returns>
        [HttpPost]
        public ActionResult GetOverview(int bPartnerId)
        {
            int safeBpId = Util.GetValueOfInt(bPartnerId);
            if (safeBpId <= 0)
                return Json(JsonConvert.SerializeObject(new { error = "Invalid account ID" }), JsonRequestBehavior.AllowGet);

            Ctx ctx    = (Ctx)Session["ctx"];
            var model  = new VAS_105_AccountRightPanelModel();
            var result = model.GetOverview(ctx, safeBpId);
            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }

        // ─────────────────────────────────────────────────────────
        // §2  Contacts
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the list of contacts associated with the selected account.
        /// </summary>
        /// <param name="bPartnerId">C_BPartner_ID of the selected customer account.</param>
        /// <returns>Double-serialized JSON with contacts list.</returns>
        [HttpPost]
        public ActionResult GetContacts(int bPartnerId)
        {
            int safeBpId = Util.GetValueOfInt(bPartnerId);
            if (safeBpId <= 0)
                return Json(JsonConvert.SerializeObject(new { error = "Invalid account ID" }), JsonRequestBehavior.AllowGet);

            Ctx ctx    = (Ctx)Session["ctx"];
            var model  = new VAS_105_AccountRightPanelModel();
            var result = model.GetContacts(ctx, safeBpId);
            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }

        // ─────────────────────────────────────────────────────────
        // §3  Locations
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the list of locations (addresses) associated with the selected account.
        /// </summary>
        /// <param name="bPartnerId">C_BPartner_ID of the selected customer account.</param>
        /// <returns>Double-serialized JSON with locations list.</returns>
        [HttpPost]
        public ActionResult GetLocations(int bPartnerId)
        {
            int safeBpId = Util.GetValueOfInt(bPartnerId);
            if (safeBpId <= 0)
                return Json(JsonConvert.SerializeObject(new { error = "Invalid account ID" }), JsonRequestBehavior.AllowGet);

            Ctx ctx    = (Ctx)Session["ctx"];
            var model  = new VAS_105_AccountRightPanelModel();
            var result = model.GetLocations(ctx, safeBpId);
            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }

        // ─────────────────────────────────────────────────────────
        // §4  Opportunities
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the list of sales opportunities linked to the selected account.
        /// </summary>
        /// <param name="bPartnerId">C_BPartner_ID of the selected customer account.</param>
        /// <returns>Double-serialized JSON with opportunities list.</returns>
        [HttpPost]
        public ActionResult GetOpportunities(int bPartnerId)
        {
            int safeBpId = Util.GetValueOfInt(bPartnerId);
            if (safeBpId <= 0)
                return Json(JsonConvert.SerializeObject(new { error = "Invalid account ID" }), JsonRequestBehavior.AllowGet);

            Ctx ctx    = (Ctx)Session["ctx"];
            var model  = new VAS_105_AccountRightPanelModel();
            var result = model.GetOpportunities(ctx, safeBpId);
            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }

        // ─────────────────────────────────────────────────────────
        // §5  Contracts
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the list of contracts associated with the selected account.
        /// </summary>
        /// <param name="bPartnerId">C_BPartner_ID of the selected customer account.</param>
        /// <returns>Double-serialized JSON with contracts list.</returns>
        [HttpPost]
        public ActionResult GetContracts(int bPartnerId)
        {
            int safeBpId = Util.GetValueOfInt(bPartnerId);
            if (safeBpId <= 0)
                return Json(JsonConvert.SerializeObject(new { error = "Invalid account ID" }), JsonRequestBehavior.AllowGet);

            Ctx ctx    = (Ctx)Session["ctx"];
            var model  = new VAS_105_AccountRightPanelModel();
            var result = model.GetContracts(ctx, safeBpId);
            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }

        // ─────────────────────────────────────────────────────────
        // §6  Tickets  (paged, state filter)
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// Returns a paged list of support tickets for the selected account.
        /// </summary>
        /// <param name="bPartnerId">C_BPartner_ID of the selected customer account.</param>
        /// <param name="state">"open" for active tickets or "past" for closed/resolved tickets.</param>
        /// <param name="pageOffset">Zero-based page offset (clamped to &gt;= 0).</param>
        /// <param name="pageSize">Number of rows per page (clamped to 1–100; default 10).</param>
        /// <returns>Double-serialized JSON with paged tickets list.</returns>
        [HttpPost]
        public ActionResult GetTickets(int bPartnerId, string state, int? pageOffset, int? pageSize)
        {
            try
            {
                int safeBpId = Util.GetValueOfInt(bPartnerId);
                if (safeBpId <= 0)
                    return Json(JsonConvert.SerializeObject(new { error = "Invalid account ID" }), JsonRequestBehavior.AllowGet);

                string safeState  = (Util.GetValueOfString(state) == "past") ? "past" : "open";
                int safeOffset    = pageOffset.HasValue ? pageOffset.Value : 0;
                int safeSize      = pageSize.HasValue   ? pageSize.Value   : 20;
                if (safeOffset < 0)   safeOffset = 0;
                if (safeSize   < 1)   safeSize   = 20;
                if (safeSize   > 100) safeSize   = 100;

                Ctx ctx    = (Ctx)Session["ctx"];
                var model  = new VAS_105_AccountRightPanelModel();
                var result = model.GetTickets(ctx, safeBpId, safeState, safeOffset, safeSize);
                return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(JsonConvert.SerializeObject(new { error = ex.Message, stackTrace = ex.StackTrace }), JsonRequestBehavior.AllowGet);
            }
        }

        // ─────────────────────────────────────────────────────────
        // §7  Orders  (paged)
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// Returns a paged list of sales orders for the selected account.
        /// </summary>
        /// <param name="bPartnerId">C_BPartner_ID of the selected customer account.</param>
        /// <param name="pageOffset">Zero-based page offset (clamped to &gt;= 0).</param>
        /// <param name="pageSize">Number of rows per page (clamped to 1–100; default 5).</param>
        /// <returns>Double-serialized JSON with paged orders list.</returns>
        [HttpPost]
        public ActionResult GetOrders(int bPartnerId, int pageOffset, int pageSize)
        {
            int safeBpId = Util.GetValueOfInt(bPartnerId);
            if (safeBpId <= 0)
                return Json(JsonConvert.SerializeObject(new { error = "Invalid account ID" }), JsonRequestBehavior.AllowGet);

            int safeOffset = Util.GetValueOfInt(pageOffset);
            int safeSize   = Util.GetValueOfInt(pageSize);
            if (safeOffset < 0) safeOffset = 0;
            if (safeSize   < 1) safeSize   = 5;
            if (safeSize   > 100) safeSize = 100;

            Ctx ctx    = (Ctx)Session["ctx"];
            var model  = new VAS_105_AccountRightPanelModel();
            var result = model.GetOrders(ctx, safeBpId, safeOffset, safeSize);
            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }

        // ─────────────────────────────────────────────────────────
        // §8  Invoices  (paged)
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// Returns a paged list of invoices for the selected account.
        /// </summary>
        /// <param name="bPartnerId">C_BPartner_ID of the selected customer account.</param>
        /// <param name="pageOffset">Zero-based page offset (clamped to &gt;= 0).</param>
        /// <param name="pageSize">Number of rows per page (clamped to 1–100; default 5).</param>
        /// <returns>Double-serialized JSON with paged invoices list.</returns>
        [HttpPost]
        public ActionResult GetInvoices(int bPartnerId, int pageOffset, int pageSize)
        {
            int safeBpId = Util.GetValueOfInt(bPartnerId);
            if (safeBpId <= 0)
                return Json(JsonConvert.SerializeObject(new { error = "Invalid account ID" }), JsonRequestBehavior.AllowGet);

            int safeOffset = Util.GetValueOfInt(pageOffset);
            int safeSize   = Util.GetValueOfInt(pageSize);
            if (safeOffset < 0) safeOffset = 0;
            if (safeSize   < 1) safeSize   = 5;
            if (safeSize   > 100) safeSize = 100;

            Ctx ctx    = (Ctx)Session["ctx"];
            var model  = new VAS_105_AccountRightPanelModel();
            var result = model.GetInvoices(ctx, safeBpId, safeOffset, safeSize);
            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }

        // ─────────────────────────────────────────────────────────
        // §9  Projects
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the list of projects associated with the selected account.
        /// </summary>
        /// <param name="bPartnerId">C_BPartner_ID of the selected customer account.</param>
        /// <returns>Double-serialized JSON with projects list.</returns>
        [HttpPost]
        public ActionResult GetProjects(int bPartnerId)
        {
            int safeBpId = Util.GetValueOfInt(bPartnerId);
            if (safeBpId <= 0)
                return Json(JsonConvert.SerializeObject(new { error = "Invalid account ID" }), JsonRequestBehavior.AllowGet);

            Ctx ctx    = (Ctx)Session["ctx"];
            var model  = new VAS_105_AccountRightPanelModel();
            var result = model.GetProjects(ctx, safeBpId);
            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }

        // ─────────────────────────────────────────────────────────
        // §10  Timeline  (paged, best-effort)
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// Returns a paged engagement timeline for the selected account (best-effort;
        /// partial results are returned when some source tables are absent).
        /// </summary>
        /// <param name="bPartnerId">C_BPartner_ID of the selected customer account.</param>
        /// <param name="pageOffset">Zero-based page offset (clamped to &gt;= 0).</param>
        /// <param name="pageSize">Number of rows per page (clamped to 1–100; default 10).</param>
        /// <returns>Double-serialized JSON with paged timeline entries.</returns>
        [HttpPost]
        public ActionResult GetTimeline(int bPartnerId, int pageOffset, int pageSize)
        {
            int safeBpId = Util.GetValueOfInt(bPartnerId);
            if (safeBpId <= 0)
                return Json(JsonConvert.SerializeObject(new { error = "Invalid account ID" }), JsonRequestBehavior.AllowGet);

            int safeOffset = Util.GetValueOfInt(pageOffset);
            int safeSize   = Util.GetValueOfInt(pageSize);
            if (safeOffset < 0) safeOffset = 0;
            if (safeSize   < 1) safeSize   = 10;
            if (safeSize   > 100) safeSize = 100;

            Ctx ctx    = (Ctx)Session["ctx"];
            var model  = new VAS_105_AccountRightPanelModel();
            var result = model.GetTimeline(ctx, safeBpId, safeOffset, safeSize);
            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }

        // ─────────────────────────────────────────────────────────
        // §11  Notes  (extension — graceful degradation)
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the list of notes for the selected account.
        /// Returns an empty list if the VA_AccountNote extension table has not yet been created.
        /// </summary>
        /// <param name="bPartnerId">C_BPartner_ID of the selected customer account.</param>
        /// <returns>Double-serialized JSON with notes list (may be empty).</returns>
        [HttpPost]
        public ActionResult GetNotes(int bPartnerId)
        {
            int safeBpId = Util.GetValueOfInt(bPartnerId);
            if (safeBpId <= 0)
                return Json(JsonConvert.SerializeObject(new { error = "Invalid account ID" }), JsonRequestBehavior.AllowGet);

            Ctx ctx    = (Ctx)Session["ctx"];
            var model  = new VAS_105_AccountRightPanelModel();
            var result = model.GetNotes(ctx, safeBpId);
            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }

        // ─────────────────────────────────────────────────────────
        // §12  Post Note  (stub)
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// Saves a new note against the selected account.
        /// Returns a descriptive error until the VA_AccountNote extension table is created.
        /// </summary>
        /// <param name="bPartnerId">C_BPartner_ID of the selected customer account.</param>
        /// <param name="noteText">Body text of the note to save.</param>
        /// <returns>Double-serialized JSON indicating success or the reason for failure.</returns>
        [HttpPost]
        public ActionResult PostNote(int bPartnerId, string noteText)
        {
            int safeBpId = Util.GetValueOfInt(bPartnerId);
            if (safeBpId <= 0)
                return Json(JsonConvert.SerializeObject(new { error = "Invalid account ID" }), JsonRequestBehavior.AllowGet);

            string safeNote = Util.GetValueOfString(noteText);
            if (string.IsNullOrWhiteSpace(safeNote))
                return Json(JsonConvert.SerializeObject(new { error = "Note text is required" }), JsonRequestBehavior.AllowGet);

            Ctx ctx    = (Ctx)Session["ctx"];
            var model  = new VAS_105_AccountRightPanelModel();
            var result = model.PostNote(ctx, safeBpId, safeNote);
            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }

        // ─────────────────────────────────────────────────────────
        // §13  Post Activity  (stub)
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// Logs an activity against the selected account.
        /// Currently a stub — returns success so the client can optimistically update the UI.
        /// </summary>
        /// <param name="bPartnerId">C_BPartner_ID of the selected customer account.</param>
        /// <param name="actType">Activity type identifier (e.g. "call", "email").</param>
        /// <param name="subject">Subject or short description of the activity.</param>
        /// <param name="duration">Duration of the activity (free-form text).</param>
        /// <param name="date">Date of the activity in a parseable string format.</param>
        /// <returns>Double-serialized JSON with success flag.</returns>
        [HttpPost]
        public ActionResult PostActivity(int bPartnerId, string actType, string subject, string duration, string date)
        {
            int safeBpId = Util.GetValueOfInt(bPartnerId);
            if (safeBpId <= 0)
                return Json(JsonConvert.SerializeObject(new { error = "Invalid account ID" }), JsonRequestBehavior.AllowGet);

            // Stub: optimistic success — client handles display.
            return Json(JsonConvert.SerializeObject(new { success = true }), JsonRequestBehavior.AllowGet);
        }

        // ─────────────────────────────────────────────────────────
        // §14  Post Meeting  (stub)
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// Schedules a meeting for the selected account.
        /// Currently a stub — returns success so the client can optimistically update the UI.
        /// </summary>
        /// <param name="bPartnerId">C_BPartner_ID of the selected customer account.</param>
        /// <param name="title">Title of the meeting.</param>
        /// <param name="meetingDate">Date of the meeting in a parseable string format.</param>
        /// <param name="meetingTime">Time of the meeting in a parseable string format.</param>
        /// <param name="location">Location or venue of the meeting.</param>
        /// <returns>Double-serialized JSON with success flag.</returns>
        [HttpPost]
        public ActionResult PostMeeting(int bPartnerId, string title, string startDateTime, string endDateTime, string location, string description)
        {
            int safeBpId = Util.GetValueOfInt(bPartnerId);
            if (safeBpId <= 0)
                return Json(JsonConvert.SerializeObject(new { error = "Invalid account ID" }), JsonRequestBehavior.AllowGet);

            string safeTitle = Util.GetValueOfString(title);
            if (string.IsNullOrWhiteSpace(safeTitle))
                return Json(JsonConvert.SerializeObject(new { error = "Title is required" }), JsonRequestBehavior.AllowGet);

            Ctx ctx    = (Ctx)Session["ctx"];
            var model  = new VAS_105_AccountRightPanelModel();
            var result = model.PostMeeting(ctx, safeBpId, safeTitle,
                             Util.GetValueOfString(startDateTime),
                             Util.GetValueOfString(endDateTime),
                             Util.GetValueOfString(location),
                             Util.GetValueOfString(description));
            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }

        // ─────────────────────────────────────────────────────────
        // §15  GetWhatsAppChat
        // ─────────────────────────────────────────────────────────

        [HttpPost]
        public ActionResult GetWhatsAppChat(int bPartnerId, int topicId = 0)
        {
            int safeBpId    = Util.GetValueOfInt(bPartnerId);
            int safeTopicId = Util.GetValueOfInt(topicId);
            if (safeBpId <= 0)
                return Json(JsonConvert.SerializeObject(new { error = "Invalid account ID" }), JsonRequestBehavior.AllowGet);

            Ctx ctx    = (Ctx)Session["ctx"];
            var model  = new VAS_105_AccountRightPanelModel();
            var result = model.GetWhatsAppChat(ctx, safeBpId, safeTopicId);
            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }

        // ─────────────────────────────────────────────────────────
        // §16  GetNoteDetail
        // ─────────────────────────────────────────────────────────

        [HttpPost]
        public ActionResult GetNoteDetail(int noteId)
        {
            int safeId = Util.GetValueOfInt(noteId);
            if (safeId <= 0)
                return Json(JsonConvert.SerializeObject(new { id = 0 }), JsonRequestBehavior.AllowGet);

            Ctx ctx    = (Ctx)Session["ctx"];
            var model  = new VAS_105_AccountRightPanelModel();
            var result = model.GetNoteDetail(ctx, safeId);
            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }

        // §16b  GetEmailDetail
        // ─────────────────────────────────────────────────────────

        [HttpPost]
        public ActionResult GetEmailDetail(int emailId)
        {
            int safeId = Util.GetValueOfInt(emailId);
            if (safeId <= 0)
                return Json(JsonConvert.SerializeObject(new { id = 0 }), JsonRequestBehavior.AllowGet);

            Ctx ctx    = (Ctx)Session["ctx"];
            var model  = new VAS_105_AccountRightPanelModel();
            var result = model.GetEmailDetail(ctx, safeId);
            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }

        // §16c  GetMeetingDetail
        // ─────────────────────────────────────────────────────────

        [HttpPost]
        public ActionResult GetMeetingDetail(int meetingId)
        {
            int safeId = Util.GetValueOfInt(meetingId);
            if (safeId <= 0)
                return Json(JsonConvert.SerializeObject(new { error = "Invalid meeting ID" }), JsonRequestBehavior.AllowGet);

            Ctx ctx    = (Ctx)Session["ctx"];
            var model  = new VAS_105_AccountRightPanelModel();
            var result = model.GetMeetingDetail(ctx, safeId);
            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }

        // §16d  SaveMeetingComments
        // ─────────────────────────────────────────────────────────

        [HttpPost]
        public ActionResult SaveMeetingComments(int meetingId, string comments, string meetingUrl)
        {
            int safeId = Util.GetValueOfInt(meetingId);
            if (safeId <= 0)
                return Json(JsonConvert.SerializeObject(new { error = "Invalid meeting ID" }), JsonRequestBehavior.AllowGet);

            Ctx ctx    = (Ctx)Session["ctx"];
            var model  = new VAS_105_AccountRightPanelModel();
            var result = model.SaveMeetingComments(ctx, safeId, Util.GetValueOfString(comments), Util.GetValueOfString(meetingUrl));
            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }

        // §16e  GetEngagement
        // ─────────────────────────────────────────────────────────

        [HttpPost]
        public ActionResult GetEngagement(int bPartnerId)
        {
            int safeBpId = Util.GetValueOfInt(bPartnerId);
            if (safeBpId <= 0)
                return Json(JsonConvert.SerializeObject(new { counts = new { total = 0, meetings = 0, notes = 0, emails = 0, calls = 0, chat = 0 }, items = new object[0] }), JsonRequestBehavior.AllowGet);

            Ctx ctx    = (Ctx)Session["ctx"];
            var model  = new VAS_105_AccountRightPanelModel();
            var result = model.GetEngagement(ctx, safeBpId);
            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }

        // §T1  GetTasks
        // ─────────────────────────────────────────────────────────

        [HttpPost]
        public ActionResult GetTasks(int bPartnerId)
        {
            int safeBpId = Util.GetValueOfInt(bPartnerId);
            if (safeBpId <= 0)
                return Json(JsonConvert.SerializeObject(new { items = new object[0], total = 0 }), JsonRequestBehavior.AllowGet);
            Ctx ctx    = (Ctx)Session["ctx"];
            var model  = new VAS_105_AccountRightPanelModel();
            var result = model.GetTasks(ctx, safeBpId);
            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }

        // §T2  CompleteTask
        // ─────────────────────────────────────────────────────────

        [HttpPost]
        public ActionResult CompleteTask(int taskId)
        {
            int safeId = Util.GetValueOfInt(taskId);
            if (safeId <= 0)
                return Json(JsonConvert.SerializeObject(new { error = "Invalid task ID" }), JsonRequestBehavior.AllowGet);
            Ctx ctx    = (Ctx)Session["ctx"];
            var model  = new VAS_105_AccountRightPanelModel();
            var result = model.CompleteTask(ctx, safeId);
            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }

        // §T3  ReopenTask
        // ─────────────────────────────────────────────────────────

        [HttpPost]
        public ActionResult ReopenTask(int taskId)
        {
            int safeId = Util.GetValueOfInt(taskId);
            if (safeId <= 0)
                return Json(JsonConvert.SerializeObject(new { error = "Invalid task ID" }), JsonRequestBehavior.AllowGet);
            Ctx ctx    = (Ctx)Session["ctx"];
            var model  = new VAS_105_AccountRightPanelModel();
            var result = model.ReopenTask(ctx, safeId);
            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }

        // §17  GetWhatsAppTopicMeta
        // ─────────────────────────────────────────────────────────

        [HttpPost]
        public ActionResult GetWhatsAppTopicMeta(int topicId)
        {
            int safeTopicId = Util.GetValueOfInt(topicId);
            if (safeTopicId <= 0)
                return Json(JsonConvert.SerializeObject(new { chatId = 0, mobile = "" }), JsonRequestBehavior.AllowGet);

            Ctx ctx    = (Ctx)Session["ctx"];
            var model  = new VAS_105_AccountRightPanelModel();
            var result = model.GetWhatsAppTopicMeta(ctx, safeTopicId);
            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }

        // §18  GetWindowId — window navigation helper
        // ─────────────────────────────────────────────────────────

        [HttpPost]
        public ActionResult GetWindowId(string windowName)
        {
            Ctx ctx    = (Ctx)Session["ctx"];
            var model  = new VAS_105_AccountRightPanelModel();
            var result = model.GetWindowId(ctx, Util.GetValueOfString(windowName));
            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }
    }
}
