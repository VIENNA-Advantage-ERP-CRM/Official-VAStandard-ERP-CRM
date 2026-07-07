/// <summary>
/// Module Name : VAS
/// Purpose     : Inventory Count Overview tab panel endpoint. Returns the
///               read-only overview payload for the selected physical inventory
///               count (M_Inventory where IsInternalUse is not 'Y') consumed by
///               the VAS.VAS_101_OverviewInventoryCount tab panel.
/// Chronological development:
///   VAI163   2026-07-06  Created
/// </summary>

using Newtonsoft.Json;
using System.Web.Mvc;
using VAdvantage.Utility;
using VASLogic.Models;

namespace VAS.Controllers
{
    public class VAS_101_OverviewInventoryCountController : Controller
    {
        public ActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// Returns the overview details for the selected M_Inventory count.
        /// </summary>
        /// <param name="M_Inventory_ID">Selected inventory count id.</param>
        /// <returns>JSON-serialized <see cref="VAS_101_OverviewInventoryCountModel.InventoryCountOverviewData"/>.</returns>
        public JsonResult GetInventoryCountOverview(int M_Inventory_ID)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_101_OverviewInventoryCountModel model = new VAS_101_OverviewInventoryCountModel();
                retJSON = JsonConvert.SerializeObject(
                    model.GetInventoryCountOverview(ctx, M_Inventory_ID));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
    }
}
