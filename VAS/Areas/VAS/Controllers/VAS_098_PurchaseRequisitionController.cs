/// <summary>
/// Module Name : VAS
/// Purpose     : Purchase Requisition overview tab panel endpoint. Returns the
///               read-only overview payload for the selected requisition
///               (M_Requisition) consumed by the
///               VAS.VAS_098_PurchaseRequisition tab panel.
/// Chronological development:
///   VAI163   2026-07-01  Created
/// </summary>

using Newtonsoft.Json;
using System.Web.Mvc;
using VAdvantage.Utility;
using VASLogic.Models;

namespace VAS.Controllers
{
    public class VAS_098_PurchaseRequisitionController : Controller
    {
        public ActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// Returns the overview details for the selected M_Requisition record.
        /// </summary>
        /// <param name="M_Requisition_ID">Selected requisition id.</param>
        /// <returns>JSON-serialized <see cref="VAS_098_PurchaseRequisitionModel.RequisitionOverviewData"/>.</returns>
        public JsonResult GetRequisitionOverview(int M_Requisition_ID)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_098_PurchaseRequisitionModel model = new VAS_098_PurchaseRequisitionModel();
                retJSON = JsonConvert.SerializeObject(
                    model.GetRequisitionOverview(ctx, M_Requisition_ID));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
    }
}
