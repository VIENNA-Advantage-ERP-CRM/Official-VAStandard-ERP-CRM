/// <summary>
/// Module Name : VAS
/// Purpose     : Material Transfer Overview tab panel endpoint. Returns the
///               read-only overview payload for the selected stock movement
///               (M_Movement) consumed by the VAS.VAS_103_MaterialTransfer tab
///               panel.
/// Chronological development:
///   VAI163   2026-07-07  Created
/// </summary>

using Newtonsoft.Json;
using System.Web.Mvc;
using VAdvantage.Utility;
using VASLogic.Models;

namespace VAS.Controllers
{
    public class VAS_103_MaterialTransferController : Controller
    {
        public ActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// Returns the overview details for the selected material transfer.
        /// </summary>
        /// <param name="M_Movement_ID">Selected stock movement id.</param>
        /// <returns>JSON-serialized <see cref="VAS_103_MaterialTransferModel.MaterialTransferOverviewData"/>.</returns>
        public JsonResult GetMaterialTransferOverview(int M_Movement_ID)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_103_MaterialTransferModel model = new VAS_103_MaterialTransferModel();
                retJSON = JsonConvert.SerializeObject(
                    model.GetMaterialTransferOverview(ctx, M_Movement_ID));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
    }
}
