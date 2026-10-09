/// <summary>
/// Module Name : VAS
/// Purpose     : Material Transfer Overview tab panel endpoint. Returns the
///               read-only overview payload for the selected stock movement
///               (M_Movement) consumed by the VAS.VAS_103_MaterialTransfer tab
///               panel.
/// Chronological development:
///   VAI163   2026-07-07  Created
///   VAI163   2026-08-12  Added GetWindow_ID and GetWindowIdByTable for the
///                        Generated From chips' record-open path: the requisition
///                        opens a window named here, and the production order —
///                        maintained by a VAMFG window whose name cannot be
///                        hard-coded — falls back to the window the DICTIONARY
///                        says its table opens in. Ported from VAS_106.
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

        /// <summary>
        /// Resolves a window's AD_Window_ID from its name, for the Generated From
        /// chips' record-open path.
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
                VAS_103_MaterialTransferModel model = new VAS_103_MaterialTransferModel();
                windowId = model.GetWindowId(ctx, fields);
            }
            return Json(JsonConvert.SerializeObject(windowId), JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Resolves the window a TABLE's records open in, for a record whose screen
        /// cannot be named on the client — the production order is maintained by a
        /// VAMFG window whose name cannot be hard-coded, and the browser-side zoom
        /// lookup only knows tables the client has cached.
        /// </summary>
        /// <param name="fields">Physical table name, as sent by
        /// VIS.dataContext.getJSONRecord.</param>
        /// <returns>The window id, or 0 when the table has no window.</returns>
        public JsonResult GetWindowIdByTable(string fields)
        {
            int windowId = 0;
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_103_MaterialTransferModel model = new VAS_103_MaterialTransferModel();
                windowId = model.GetWindowIdByTable(ctx, fields);
            }
            return Json(JsonConvert.SerializeObject(windowId), JsonRequestBehavior.AllowGet);
        }
    }
}
