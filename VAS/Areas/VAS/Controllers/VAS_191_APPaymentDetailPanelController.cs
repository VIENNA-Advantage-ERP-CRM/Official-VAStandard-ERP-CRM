/// <summary>
/// Module Name : VAS
/// Purpose     : Payment Overview tab panel endpoints. Serves the
///               VAS.VAS_191_APPaymentDetailPanel tab panel:
///
///                 GetPaymentOverview — the read payload for the selected
///                                      C_Payment (header, allocation position,
///                                      Payment Allocate rows, Allocation Detail
///                                      rows, action availability, the standard
///                                      Payment Allocation form id).
///                 CompletePayment    — runs the document engine's Complete.
///                 ReversePayment     — runs the document engine's Reverse-Correct.
///                 GetWindow_ID       — resolves a window id from its name for the
///                                      panel's record-open path.
///
///               The payment id arriving from the browser is never trusted on its
///               own: the read path reads C_Payment under MRole, and both write
///               actions re-read the record and re-validate its document status in
///               the model before the document engine is touched. UI button
///               visibility is a convenience, not the authority.
/// Chronological development:
///   VAI145   2026-08-17  Created.
/// </summary>

using Newtonsoft.Json;
using System.Web.Mvc;
using VAdvantage.Utility;
using VASLogic.Models;

namespace VAS.Controllers
{
    public class VAS_191_APPaymentDetailPanelController : Controller
    {
        public ActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// Returns the overview payload for the selected payment.
        /// </summary>
        /// <param name="C_Payment_ID">Selected payment id.</param>
        /// <returns>JSON-serialized
        /// <see cref="VAS_191_APPaymentDetailPanelModel.PaymentOverviewData"/>.</returns>
        public JsonResult GetPaymentOverview(int C_Payment_ID)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_191_APPaymentDetailPanelModel model = new VAS_191_APPaymentDetailPanelModel();
                retJSON = JsonConvert.SerializeObject(
                    model.GetPaymentOverview(ctx, C_Payment_ID));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// One page of the payment's posted accounting facts, for the Posted entry
        /// section's pager. Only the rows are returned — the count and the Dr / Cr
        /// totals came with the initial payload and do not change between pages.
        /// </summary>
        /// <param name="C_Payment_ID">Selected payment id.</param>
        /// <param name="page">Zero-based page index.</param>
        /// <param name="pageSize">Rows per page.</param>
        /// <returns>JSON-serialized
        /// <see cref="VAS_191_APPaymentDetailPanelModel.PostedJournalInfo"/>.</returns>
        public JsonResult GetPostedJournalPage(int C_Payment_ID, int page, int pageSize)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_191_APPaymentDetailPanelModel model = new VAS_191_APPaymentDetailPanelModel();
                retJSON = JsonConvert.SerializeObject(
                    model.GetPostedJournalPage(ctx, C_Payment_ID, page, pageSize));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Completes the payment through the standard document engine. POST only —
        /// it changes the document.
        /// </summary>
        /// <param name="C_Payment_ID">Payment to complete.</param>
        /// <returns>JSON-serialized
        /// <see cref="VAS_191_APPaymentDetailPanelModel.PaymentActionResult"/>.</returns>
        [HttpPost]
        public JsonResult CompletePayment(int C_Payment_ID)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_191_APPaymentDetailPanelModel model = new VAS_191_APPaymentDetailPanelModel();
                retJSON = JsonConvert.SerializeObject(
                    model.CompletePayment(ctx, C_Payment_ID));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Reverses the payment through the standard document engine. POST only —
        /// it changes the document.
        /// </summary>
        /// <param name="C_Payment_ID">Payment to reverse.</param>
        /// <returns>JSON-serialized
        /// <see cref="VAS_191_APPaymentDetailPanelModel.PaymentActionResult"/>.</returns>
        [HttpPost]
        public JsonResult ReversePayment(int C_Payment_ID)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_191_APPaymentDetailPanelModel model = new VAS_191_APPaymentDetailPanelModel();
                retJSON = JsonConvert.SerializeObject(
                    model.ReversePayment(ctx, C_Payment_ID));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Resolves a window's AD_Window_ID from its name, for the panel's
        /// record-open path. Ids differ per environment, so the panel carries names
        /// and asks for the id at runtime.
        /// </summary>
        /// <param name="fields">Window name (AD_Window.Name).</param>
        /// <returns>JSON carrying the window id; 0 when the name is unknown.</returns>
        public JsonResult GetWindow_ID(string fields)
        {
            int windowId = 0;
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_191_APPaymentDetailPanelModel model = new VAS_191_APPaymentDetailPanelModel();
                windowId = model.GetWindowId(ctx, fields);
            }
            return Json(JsonConvert.SerializeObject(windowId), JsonRequestBehavior.AllowGet);
        }
    }
}
