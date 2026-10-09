/// <summary>
/// Module Name : VAS
/// Purpose     : Purchase Order Overview tab panel endpoint. Returns the
///               read-only overview payload for the selected purchase order
///               (C_Order, IsSoTrx = 'N') consumed by the
///               VAS.VAS_092_OverviewPurchaseOrder tab panel.
/// Chronological development:
///   VAI163   2026-06-10  Created
///   VAI163   2026-08-04  Added GetWindow_ID: resolves a window by NAME for the
///                        panel's record-open path, so a record whose screen is
///                        not the table's default zoom target (the RFQ, which
///                        opens VAS_RFQ) can still be opened.
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

        /// <summary>
        /// Resolves a window's AD_Window_ID from its name, for the panel's
        /// record-open path. Used where the record's screen is not the table's
        /// default zoom target — the RFQ opens the VAS_RFQ window, not whatever
        /// C_RfQ's zoom target resolves to.
        /// </summary>
        /// <param name="fields">Window name (AD_Window.Name), as sent by
        /// VIS.dataContext.getJSONRecord.</param>
        /// <returns>The window id, or 0 when the name is unknown to this client.</returns>
        public JsonResult GetWindow_ID(string fields)
        {
            int windowId = 0;
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_092_OverviewPurchaseOrderModel model = new VAS_092_OverviewPurchaseOrderModel();
                windowId = model.GetWindowId(ctx, fields);
            }
            return Json(JsonConvert.SerializeObject(windowId), JsonRequestBehavior.AllowGet);
        }
    }
}
