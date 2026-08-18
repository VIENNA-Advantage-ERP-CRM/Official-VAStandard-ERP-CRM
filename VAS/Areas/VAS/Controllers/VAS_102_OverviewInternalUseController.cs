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

        /// <summary>
        /// Resolves a window's AD_Window_ID from its name, for the panel's
        /// record-open path. Used where the source document's screen is not its
        /// table's default zoom target — a requisition opens the VAS_Requisition
        /// window rather than whatever M_Requisition's zoom target resolves to.
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
                VAS_102_OverviewInternalUseModel model = new VAS_102_OverviewInternalUseModel();
                windowId = model.GetWindowId(ctx, fields);
            }
            return Json(JsonConvert.SerializeObject(windowId), JsonRequestBehavior.AllowGet);
        }
    }
}
