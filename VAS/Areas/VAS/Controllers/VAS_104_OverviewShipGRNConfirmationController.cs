/// <summary>
/// Module Name : VAS
/// Purpose     : Ship / GRN Confirmation Overview tab panel endpoint. Returns the
///               read-only overview payload for the selected in/out confirmation
///               (M_InOutConfirm) consumed by the
///               VAS.VAS_104_OverviewShipGRNConfirmation tab panel.
/// Chronological development:
///   VAI163   2026-07-07  Created
/// </summary>

using Newtonsoft.Json;
using System.Web.Mvc;
using VAdvantage.Utility;
using VASLogic.Models;

namespace VAS.Controllers
{
    public class VAS_104_OverviewShipGRNConfirmationController : Controller
    {
        public ActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// Returns the overview details for the selected ship/GRN confirmation.
        /// </summary>
        /// <param name="M_InOutConfirm_ID">Selected confirmation id.</param>
        /// <returns>JSON-serialized <see cref="VAS_104_OverviewShipGRNConfirmationModel.ShipGRNConfirmationOverviewData"/>.</returns>
        public JsonResult GetShipGRNConfirmationOverview(int M_InOutConfirm_ID)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_104_OverviewShipGRNConfirmationModel model = new VAS_104_OverviewShipGRNConfirmationModel();
                retJSON = JsonConvert.SerializeObject(
                    model.GetShipGRNConfirmationOverview(ctx, M_InOutConfirm_ID));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
    }
}
