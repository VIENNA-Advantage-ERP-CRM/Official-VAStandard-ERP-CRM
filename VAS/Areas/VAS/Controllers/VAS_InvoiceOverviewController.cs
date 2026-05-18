/******************************************************
 * Module Name    : VAS
 * Purpose        : Invoice Overview tab panel endpoint
 * chronological  : Development
 * Created Date   : 30 April 2026
 * Created by     : VAI154
 ******************************************************/

using Newtonsoft.Json;
using System.Web.Mvc;
using VAdvantage.Utility;
using VASLogic.Models;

namespace VAS.Controllers
{
    public class VAS_InvoiceOverviewController : Controller
    {
        public ActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// Returns the overview details for the selected C_Invoice record.
        /// </summary>
        public JsonResult GetInvoiceOverview(int C_Invoice_ID)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_InvoiceOverviewModel model = new VAS_InvoiceOverviewModel();
                retJSON = JsonConvert.SerializeObject(model.GetInvoiceOverview(ctx, C_Invoice_ID));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Duplicates the given invoice into a fresh draft (header + lines)
        /// via the platform's MInvoice.CopyFrom helper. Refuses when the
        /// source invoice is voided or reversed.
        /// </summary>
        [HttpPost]
        public JsonResult DuplicateInvoice(int C_Invoice_ID)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_InvoiceOverviewModel model = new VAS_InvoiceOverviewModel();
                retJSON = JsonConvert.SerializeObject(model.DuplicateInvoice(ctx, C_Invoice_ID));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Returns the metadata required to populate the Record Payment dialog.
        /// </summary>
        public JsonResult GetRecordPaymentMeta(int C_Invoice_ID)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_InvoiceOverviewModel model = new VAS_InvoiceOverviewModel();
                retJSON = JsonConvert.SerializeObject(model.GetRecordPaymentMeta(ctx, C_Invoice_ID));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Creates an AR Receipt + payment allocations for the given invoice.
        /// </summary>
        [HttpPost]
        public JsonResult RecordPayment(string payload)
        {
            string retJSON = "";
            if (Session["ctx"] != null && !string.IsNullOrEmpty(payload))
            {
                Ctx ctx = Session["ctx"] as Ctx;
                var req = JsonConvert.DeserializeObject<VAS_InvoiceOverviewModel.RecordPaymentRequest>(payload);
                VAS_InvoiceOverviewModel model = new VAS_InvoiceOverviewModel();
                retJSON = JsonConvert.SerializeObject(model.RecordPayment(ctx, req));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
    }
}
