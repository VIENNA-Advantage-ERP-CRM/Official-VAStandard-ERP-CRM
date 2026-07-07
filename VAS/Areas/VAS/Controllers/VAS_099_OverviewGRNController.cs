/// <summary>
/// Module Name : VAS
/// Purpose     : Goods Receipt Note (GRN) Overview tab panel endpoint. Returns
///               the read-only overview payload for the selected goods receipt
///               (M_InOut, IsSOTrx = 'N') consumed by the
///               VAS.VAS_099_OverviewGRN tab panel.
/// Chronological development:
///   VAI163   2026-07-06  Created
/// </summary>

using Newtonsoft.Json;
using System.Web.Mvc;
using VAdvantage.Utility;
using VASLogic.Models;

namespace VAS.Controllers
{
    public class VAS_099_OverviewGRNController : Controller
    {
        public ActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// Returns the overview details for the selected M_InOut goods receipt.
        /// </summary>
        /// <param name="M_InOut_ID">Selected goods receipt id.</param>
        /// <returns>JSON-serialized <see cref="VAS_099_OverviewGRNModel.GRNOverviewData"/>.</returns>
        public JsonResult GetGRNOverview(int M_InOut_ID)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_099_OverviewGRNModel model = new VAS_099_OverviewGRNModel();
                retJSON = JsonConvert.SerializeObject(
                    model.GetGRNOverview(ctx, M_InOut_ID));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
    }
}
