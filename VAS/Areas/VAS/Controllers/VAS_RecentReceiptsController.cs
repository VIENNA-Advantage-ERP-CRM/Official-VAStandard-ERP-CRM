/******************************************************
 * Module Name    : VAS
 * Purpose        : Recent AR Receipts dashboard widget endpoint
 * chronological  : Development
 * Created Date   : 2026-05-29
 * Created by     : VAI145
 ******************************************************/

using Newtonsoft.Json;
using System.Web.Mvc;
using VAdvantage.Utility;
using VASLogic.Models;

namespace VAS.Controllers
{
    public class VAS_RecentReceiptsController : Controller
    {
        public ActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// Returns one page of recent AR receipts for the dashboard widget,
        /// newest first. Window: last 30 days inclusive of today. Paging is
        /// server-side via OFFSET / FETCH NEXT — the response carries
        /// totalRecords and totalPages so the widget can render its pager
        /// without a second round-trip.
        /// </summary>
        public JsonResult GetRecentReceipts(int pageNo = 1, int pageSize = 5)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_RecentReceiptsModel model = new VAS_RecentReceiptsModel();
                retJSON = JsonConvert.SerializeObject(model.GetRecentReceipts(ctx, pageNo, pageSize));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Returns the allocation detail for a single receipt, used by the
        /// row-click modal: receipt header plus one line per posted
        /// allocation against an invoice.
        /// </summary>
        public JsonResult GetReceiptAllocations(int C_Payment_ID)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_RecentReceiptsModel model = new VAS_RecentReceiptsModel();
                retJSON = JsonConvert.SerializeObject(model.GetReceiptAllocations(ctx, C_Payment_ID));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Returns AD_Process_ID + AD_Table_ID + Record_ID so the
        /// receipt-detail modal's Print button can call VIS.APrint
        /// against the C_Payment record. The process is the standard
        /// Payment-Print report, resolved server-side via the same
        /// AD_Process.Value chain used elsewhere in this codebase.
        /// </summary>
        public JsonResult GetPaymentPrintInfo(int C_Payment_ID)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_RecentReceiptsModel model = new VAS_RecentReceiptsModel();
                retJSON = JsonConvert.SerializeObject(model.GetPaymentPrintInfo(ctx, C_Payment_ID));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
    }
}
