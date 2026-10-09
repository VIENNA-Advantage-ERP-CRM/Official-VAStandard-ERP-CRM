/******************************************************
 * Module Name    : VAS
 * Purpose        : AR Invoice / AR Credit Note detail tab panel endpoints
 * chronological  : Development
 *   VAI_145        Created  04 August 2026
 ******************************************************/

using Newtonsoft.Json;
using System;
using System.Globalization;
using System.Web.Mvc;
using VAdvantage.Utility;
using VASLogic.Models;
using VIS.Filters;

namespace VAS.Controllers
{
    /// <summary>
    /// Module Name : VAS_189_ARInvoiceDetailPanel
    /// Purpose     : AJAX endpoints for the AR invoice detail tab panel. Each
    ///               action reads the session Ctx, delegates to
    ///               <see cref="VAS_189_ARInvoiceDetailPanelModel"/> and returns
    ///               the serialized view model / result.
    /// Chronological development:
    ///   VAI_145   04 August 2026
    /// </summary>
    public class VAS_189_ARInvoiceDetailPanelController : Controller
    {
        /// <summary>Returns the full panel view model for the selected invoice.</summary>
        /// <param name="C_Invoice_ID">target invoice</param>
        /// <param name="IsSOTrx">sales transaction flag passed by the panel</param>
        /// <returns>serialized panel view model</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetPanelData(int C_Invoice_ID, bool IsSOTrx = true)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_189_ARInvoiceDetailPanelModel model = new VAS_189_ARInvoiceDetailPanelModel();
                retJSON = JsonConvert.SerializeObject(model.GetPanelData(ctx, C_Invoice_ID, IsSOTrx));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>Returns a page of payment-schedule rows (server-side paging).</summary>
        /// <param name="C_Invoice_ID">target invoice</param>
        /// <param name="page">zero-based page index</param>
        /// <param name="pageSize">rows per page</param>
        /// <returns>serialized schedule rows</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetSchedulePage(int C_Invoice_ID, int page, int pageSize)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                VAS_189_ARInvoiceDetailPanelModel model = new VAS_189_ARInvoiceDetailPanelModel();
                retJSON = JsonConvert.SerializeObject(model.GetSchedulePage(C_Invoice_ID, page, pageSize));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>Returns a page of allocation rows (server-side paging).</summary>
        /// <param name="C_Invoice_ID">target invoice</param>
        /// <param name="page">zero-based page index</param>
        /// <param name="pageSize">rows per page</param>
        /// <returns>serialized allocation rows</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetAllocationsPage(int C_Invoice_ID, int page, int pageSize)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_189_ARInvoiceDetailPanelModel model = new VAS_189_ARInvoiceDetailPanelModel();
                retJSON = JsonConvert.SerializeObject(model.GetAllocationsPage(ctx, C_Invoice_ID, page, pageSize));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>Returns a page of posted-journal rows (server-side paging).</summary>
        /// <param name="C_Invoice_ID">target invoice</param>
        /// <param name="page">zero-based page index</param>
        /// <param name="pageSize">rows per page</param>
        /// <returns>serialized posted journal page</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetPostedJournalPage(int C_Invoice_ID, int page, int pageSize)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_189_ARInvoiceDetailPanelModel model = new VAS_189_ARInvoiceDetailPanelModel();
                retJSON = JsonConvert.SerializeObject(model.GetPostedJournalPage(ctx, C_Invoice_ID, page, pageSize));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>Returns the Record Payment / Allocate Credit Note modal meta.</summary>
        /// <param name="C_Invoice_ID">target invoice</param>
        /// <param name="IsSOTrx">sales transaction flag passed by the panel</param>
        /// <returns>serialized modal meta</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetPaymentModalMeta(int C_Invoice_ID, bool IsSOTrx = true)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_189_ARInvoiceDetailPanelModel model = new VAS_189_ARInvoiceDetailPanelModel();
                retJSON = JsonConvert.SerializeObject(model.GetPaymentModalMeta(ctx, C_Invoice_ID, IsSOTrx));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Returns a page of on-account receipt rows. Customer / credit-note flag
        /// / invoice currency are passed from the modal meta so the invoice
        /// header is not re-queried per page.
        /// </summary>
        /// <param name="C_BPartner_ID">customer</param>
        /// <param name="IsCreditNote">credit-note mode</param>
        /// <param name="C_Currency_ID">invoice currency</param>
        /// <param name="page">zero-based page index</param>
        /// <param name="pageSize">rows per page</param>
        /// <returns>serialized on-account rows</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetOnAccountPaymentsPage(int C_BPartner_ID, bool IsCreditNote, int C_Currency_ID, int page, int pageSize)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_189_ARInvoiceDetailPanelModel model = new VAS_189_ARInvoiceDetailPanelModel();
                retJSON = JsonConvert.SerializeObject(
                    model.GetOnAccountPaymentsPage(ctx, C_BPartner_ID, IsCreditNote, C_Currency_ID, page, pageSize));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>Converts the invoice open amount into the selected currency.</summary>
        /// <param name="C_Invoice_ID">target invoice</param>
        /// <param name="C_Currency_ID">target currency</param>
        /// <param name="C_ConversionType_ID">conversion type (0 = default)</param>
        /// <param name="Amount">amount in invoice currency</param>
        /// <param name="Date">conversion date (yyyy-MM-dd)</param>
        /// <returns>serialized conversion result</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult ConvertOpenAmount(int C_Invoice_ID, int C_Currency_ID, int C_ConversionType_ID, decimal Amount, string Date)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                DateTime parsedDate;
                var req = new VAS_189_ARInvoiceDetailPanelModel.ConvertAmountRequest
                {
                    C_Invoice_ID = C_Invoice_ID,
                    C_Currency_ID = C_Currency_ID,
                    C_ConversionType_ID = C_ConversionType_ID,
                    Amount = Amount,
                    Date = DateTime.TryParse(Date, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedDate)
                        ? (DateTime?)parsedDate : null
                };
                VAS_189_ARInvoiceDetailPanelModel model = new VAS_189_ARInvoiceDetailPanelModel();
                retJSON = JsonConvert.SerializeObject(model.ConvertOpenAmount(ctx, req));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>Applies the selected on-account receipts to the invoice.</summary>
        /// <param name="payload">JSON ApplyCreditsRequest</param>
        /// <returns>serialized allocation result</returns>
        [HttpPost]
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult ApplyCredits(string payload)
        {
            string retJSON = "";
            if (Session["ctx"] != null && !string.IsNullOrEmpty(payload))
            {
                Ctx ctx = Session["ctx"] as Ctx;
                var req = JsonConvert.DeserializeObject<VAS_189_ARInvoiceDetailPanelModel.ApplyCreditsRequest>(payload);
                VAS_189_ARInvoiceDetailPanelModel model = new VAS_189_ARInvoiceDetailPanelModel();
                retJSON = JsonConvert.SerializeObject(model.ApplyCredits(ctx, req));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>Creates and completes the customer receipt + allocation.</summary>
        /// <param name="payload">JSON RecordPaymentRequest</param>
        /// <returns>serialized payment result</returns>
        [HttpPost]
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult RecordPayment(string payload)
        {
            string retJSON = "";
            if (Session["ctx"] != null && !string.IsNullOrEmpty(payload))
            {
                Ctx ctx = Session["ctx"] as Ctx;
                var req = JsonConvert.DeserializeObject<VAS_189_ARInvoiceDetailPanelModel.RecordPaymentRequest>(payload);
                VAS_189_ARInvoiceDetailPanelModel model = new VAS_189_ARInvoiceDetailPanelModel();
                retJSON = JsonConvert.SerializeObject(model.RecordPayment(ctx, req));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>Allocates the current AR credit note to the selected open invoices.</summary>
        /// <param name="payload">JSON AllocateCreditNoteRequest</param>
        /// <returns>serialized allocation result</returns>
        [HttpPost]
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult AllocateCreditNote(string payload)
        {
            string retJSON = "";
            if (Session["ctx"] != null && !string.IsNullOrEmpty(payload))
            {
                Ctx ctx = Session["ctx"] as Ctx;
                var req = JsonConvert.DeserializeObject<VAS_189_ARInvoiceDetailPanelModel.AllocateCreditNoteRequest>(payload);
                VAS_189_ARInvoiceDetailPanelModel model = new VAS_189_ARInvoiceDetailPanelModel();
                retJSON = JsonConvert.SerializeObject(model.AllocateCreditNote(ctx, req));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>Returns the source-invoice summary + existing recurring schedule.</summary>
        /// <param name="C_Invoice_ID">source invoice</param>
        /// <param name="IsSOTrx">sales transaction flag passed by the panel</param>
        /// <returns>serialized recurring dialog meta</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetRecurringMeta(int C_Invoice_ID, bool IsSOTrx = true)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_189_ARInvoiceDetailPanelModel model = new VAS_189_ARInvoiceDetailPanelModel();
                retJSON = JsonConvert.SerializeObject(model.GetRecurringMeta(ctx, C_Invoice_ID, IsSOTrx));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>Creates or updates the recurring schedule for the invoice.</summary>
        /// <param name="payload">JSON RecurringSaveRequest</param>
        /// <returns>serialized save result</returns>
        [HttpPost]
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult SaveRecurring(string payload)
        {
            string retJSON = "";
            if (Session["ctx"] != null && !string.IsNullOrEmpty(payload))
            {
                Ctx ctx = Session["ctx"] as Ctx;
                var req = JsonConvert.DeserializeObject<VAS_189_ARInvoiceDetailPanelModel.RecurringSaveRequest>(payload);
                VAS_189_ARInvoiceDetailPanelModel model = new VAS_189_ARInvoiceDetailPanelModel();
                retJSON = JsonConvert.SerializeObject(model.SaveRecurring(ctx, req));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
    }
}
