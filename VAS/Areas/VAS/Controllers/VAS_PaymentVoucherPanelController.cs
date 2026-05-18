/******************************************************
 * Module Name    : VAS
 * Purpose        : Payment Voucher tab panel endpoint
 * chronological  : Development
 * Created Date   : 4 May 2026
 * Created by     : VAI154
 ******************************************************/

using Newtonsoft.Json;
using System.Web.Mvc;
using VAdvantage.Utility;
using VASLogic.Models;

namespace VAS.Controllers
{
    public class VAS_PaymentVoucherPanelController : Controller
    {
        public ActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// Returns the full panel payload for the selected C_Payment voucher.
        /// </summary>
        public JsonResult GetPaymentVoucher(int C_Payment_ID)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_PaymentVoucherPanelModel model = new VAS_PaymentVoucherPanelModel();
                retJSON = JsonConvert.SerializeObject(model.GetPaymentVoucher(ctx, C_Payment_ID));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Releases an approved payment voucher by completing the underlying
        /// C_Payment document. Refuses if the user is not authorised or the
        /// voucher is not approved.
        /// </summary>
        [HttpPost]
        public JsonResult ReleaseNow(int C_Payment_ID)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_PaymentVoucherPanelModel model = new VAS_PaymentVoucherPanelModel();
                retJSON = JsonConvert.SerializeObject(model.ReleaseNow(ctx, C_Payment_ID));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
    }
}
