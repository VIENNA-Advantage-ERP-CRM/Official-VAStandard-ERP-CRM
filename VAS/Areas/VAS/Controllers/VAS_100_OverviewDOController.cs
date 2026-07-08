/// <summary>
/// Module Name : VAS
/// Purpose     : Delivery Order (DO) Overview tab panel endpoint. Returns the
///               read-only overview payload for the selected delivery order
///               (M_InOut, IsSOTrx = 'Y') consumed by the
///               VAS.VAS_100_OverviewDO tab panel.
/// Chronological development:
///   VAI163   2026-07-06  Created
/// </summary>

using Newtonsoft.Json;
using System.Web.Mvc;
using VAdvantage.Utility;
using VASLogic.Models;

namespace VAS.Controllers
{
    public class VAS_100_OverviewDOController : Controller
    {
        public ActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// Returns the overview details for the selected M_InOut delivery order.
        /// </summary>
        /// <param name="M_InOut_ID">Selected delivery order id.</param>
        /// <returns>JSON-serialized <see cref="VAS_100_OverviewDOModel.DOOverviewData"/>.</returns>
        public JsonResult GetDOOverview(int M_InOut_ID)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_100_OverviewDOModel model = new VAS_100_OverviewDOModel();
                retJSON = JsonConvert.SerializeObject(
                    model.GetDOOverview(ctx, M_InOut_ID));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
    }
}
