/// <summary>
/// Module Name : VAS
/// Purpose     : Internal Use / Material Issue Overview tab panel endpoint.
///               Returns the read-only overview payload for the selected
///               internal-use material issue (M_Inventory where IsInternalUse is
///               'Y') consumed by the VAS.VAS_102_OverviewInternalUse tab panel.
/// Chronological development:
///   VAI163   2026-07-07  Created
/// </summary>

using Newtonsoft.Json;
using System.Web.Mvc;
using VAdvantage.Utility;
using VASLogic.Models;

namespace VAS.Controllers
{
    public class VAS_102_OverviewInternalUseController : Controller
    {
        public ActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// Returns the overview details for the selected internal-use issue.
        /// </summary>
        /// <param name="M_Inventory_ID">Selected internal-use issue id.</param>
        /// <returns>JSON-serialized <see cref="VAS_102_OverviewInternalUseModel.InternalUseOverviewData"/>.</returns>
        public JsonResult GetInternalUseOverview(int M_Inventory_ID)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_102_OverviewInternalUseModel model = new VAS_102_OverviewInternalUseModel();
                retJSON = JsonConvert.SerializeObject(
                    model.GetInternalUseOverview(ctx, M_Inventory_ID));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
    }
}
