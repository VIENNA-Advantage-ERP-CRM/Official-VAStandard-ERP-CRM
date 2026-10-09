/// <summary>
/// Module Name : VAS
/// Purpose     : Inventory Count Overview tab panel endpoint. Returns the
///               read-only overview payload for the selected physical inventory
///               count (M_Inventory where IsInternalUse is not 'Y') consumed by
///               the VAS.VAS_101_OverviewInventoryCount tab panel.
/// Chronological development:
///   VAI163   2026-07-06  Created
///   VAI163   2026-08-12  Added GetWindow_ID: resolves a window by NAME for the
///                        panel's record-open path, so a Related Documents row
///                        whose screen is not the table's default zoom target can
///                        still be opened. Ported from VAS_092.
///   VAI163   2026-08-14  Added GetWindowIdByTable: the record-open path's last
///                        resort, asking the dictionary which window a TABLE opens
///                        in. The work order row needs it — VA075 is not part of
///                        this solution, so its screen cannot be named on the
///                        client and the browser-side zoom lookup only knows tables
///                        the client has cached. Ported from VAS_102.
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

        /// <summary>
        /// Resolves a window's AD_Window_ID from its name, for the panel's
        /// record-open path — a Related Documents row opens the screen named for
        /// its table rather than whatever the table's zoom target resolves to.
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
                VAS_101_OverviewInventoryCountModel model = new VAS_101_OverviewInventoryCountModel();
                windowId = model.GetWindowId(ctx, fields);
            }
            return Json(JsonConvert.SerializeObject(windowId), JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Resolves the window a TABLE's records open in — the record-open path's
        /// last resort, for a row whose screen cannot be named on the client. The
        /// work order row needs it: VA075 ships its own window and is not part of
        /// this solution.
        /// </summary>
        /// <param name="fields">Physical table name, as sent by
        /// VIS.dataContext.getJSONRecord.</param>
        /// <returns>The window id, or 0 when the table has no window at all.</returns>
        public JsonResult GetWindowIdByTable(string fields)
        {
            int windowId = 0;
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_101_OverviewInventoryCountModel model = new VAS_101_OverviewInventoryCountModel();
                windowId = model.GetWindowIdByTable(ctx, fields);
            }
            return Json(JsonConvert.SerializeObject(windowId), JsonRequestBehavior.AllowGet);
        }
    }
}
