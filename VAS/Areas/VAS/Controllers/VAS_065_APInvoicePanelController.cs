/******************************************************
 * Module Name    : VAS
 * Purpose        : AP Invoice / AP Credit Note overview tab panel endpoints
 * chronological  : Development
 *   VAI_145        Created  16 June 2026
 ******************************************************/

using Newtonsoft.Json;
using System.Web.Mvc;
using VAdvantage.Utility;
using VASLogic.Models;
using VIS.Filters;

namespace VAS.Controllers
{
    /// <summary>
    /// AJAX endpoints for the VAS_065_APInvoicePanel tab panel. Each action
    /// reads the session Ctx, delegates to <see cref="VAS_065_APInvoicePanelModel"/>,
    /// and returns the serialized view model / result.
    /// </summary>
    public class VAS_065_APInvoicePanelController : Controller
    {
        /// <summary>Returns the full panel view model for the selected invoice.</summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetPanelData(int C_Invoice_ID)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_065_APInvoicePanelModel model = new VAS_065_APInvoicePanelModel();
                retJSON = JsonConvert.SerializeObject(model.GetPanelData(ctx, C_Invoice_ID));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>Returns the Record Payment / Allocate Credit Note modal meta.</summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetPaymentModalMeta(int C_Invoice_ID)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_065_APInvoicePanelModel model = new VAS_065_APInvoicePanelModel();
                retJSON = JsonConvert.SerializeObject(model.GetPaymentModalMeta(ctx, C_Invoice_ID));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>Converts the invoice open amount into the selected bank account's currency.</summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult ConvertOpenAmount(int C_Invoice_ID, int C_Currency_ID, int C_ConversionType_ID, decimal Amount, string Date)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                System.DateTime parsedDate;
                var req = new VAS_065_APInvoicePanelModel.ConvertAmountRequest
                {
                    C_Invoice_ID = C_Invoice_ID,
                    C_Currency_ID = C_Currency_ID,
                    C_ConversionType_ID = C_ConversionType_ID,
                    Amount = Amount,
                    Date = System.DateTime.TryParse(Date, System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out parsedDate)
                        ? (System.DateTime?)parsedDate : null
                };
                VAS_065_APInvoicePanelModel model = new VAS_065_APInvoicePanelModel();
                retJSON = JsonConvert.SerializeObject(model.ConvertOpenAmount(ctx, req));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>Applies the selected on-account payments / vendor credit notes to the invoice.</summary>
        [HttpPost]
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult ApplyCredits(string payload)
        {
            string retJSON = "";
            if (Session["ctx"] != null && !string.IsNullOrEmpty(payload))
            {
                Ctx ctx = Session["ctx"] as Ctx;
                var req = JsonConvert.DeserializeObject<VAS_065_APInvoicePanelModel.ApplyCreditsRequest>(payload);
                VAS_065_APInvoicePanelModel model = new VAS_065_APInvoicePanelModel();
                retJSON = JsonConvert.SerializeObject(model.ApplyCredits(ctx, req));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>Creates and completes the vendor payment + allocation for the invoice.</summary>
        [HttpPost]
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult RecordPayment(string payload)
        {
            string retJSON = "";
            if (Session["ctx"] != null && !string.IsNullOrEmpty(payload))
            {
                Ctx ctx = Session["ctx"] as Ctx;
                var req = JsonConvert.DeserializeObject<VAS_065_APInvoicePanelModel.RecordPaymentRequest>(payload);
                VAS_065_APInvoicePanelModel model = new VAS_065_APInvoicePanelModel();
                retJSON = JsonConvert.SerializeObject(model.RecordPayment(ctx, req));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>Allocates the current AP credit note to the selected open AP invoices.</summary>
        [HttpPost]
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult AllocateCreditNote(string payload)
        {
            string retJSON = "";
            if (Session["ctx"] != null && !string.IsNullOrEmpty(payload))
            {
                Ctx ctx = Session["ctx"] as Ctx;
                var req = JsonConvert.DeserializeObject<VAS_065_APInvoicePanelModel.AllocateCreditNoteRequest>(payload);
                VAS_065_APInvoicePanelModel model = new VAS_065_APInvoicePanelModel();
                retJSON = JsonConvert.SerializeObject(model.AllocateCreditNote(ctx, req));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
    }
}
