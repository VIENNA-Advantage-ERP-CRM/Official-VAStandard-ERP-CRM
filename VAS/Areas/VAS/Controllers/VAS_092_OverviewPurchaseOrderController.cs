/// <summary>
/// Module Name : VAS
/// Purpose     : Purchase Order Overview tab panel endpoint. Returns the
///               read-only overview payload for the selected purchase order
///               (C_Order, IsSoTrx = 'N') consumed by the
///               VAS.VAS_OverviewPurchaseOrder tab panel.
/// Chronological development:
///   VAI163   2026-06-10  Created
/// </summary>

using Newtonsoft.Json;
using System.Web.Mvc;
using VAdvantage.Utility;
using VASLogic.Models;

namespace VAS.Controllers
{
    public class VAS_092_OverviewPurchaseOrderController : Controller
    {
        public ActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// Returns the overview details for the selected C_Order purchase order.
        /// </summary>
        /// <param name="C_Order_ID">Selected purchase order id.</param>
        /// <returns>JSON-serialized <see cref="VAS_092_OverviewPurchaseOrderModel.PurchaseOrderOverviewData"/>.</returns>
        public JsonResult GetPurchaseOrderOverview(int C_Order_ID)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_092_OverviewPurchaseOrderModel model = new VAS_092_OverviewPurchaseOrderModel();
                retJSON = JsonConvert.SerializeObject(
                    model.GetPurchaseOrderOverview(ctx, C_Order_ID));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
    }
}
