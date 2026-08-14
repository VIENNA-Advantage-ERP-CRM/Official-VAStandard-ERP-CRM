/// <summary>
/// Module Name : VAS
/// Purpose     : Delivery Order (DO) Overview tab panel endpoint. Returns the
///               read-only overview payload for the selected delivery order
///               (M_InOut, IsSOTrx = 'Y') consumed by the
///               VAS.VAS_100_OverviewDO tab panel.
/// Chronological development:
///   VAI163   2026-07-06  Created
///   VAI163   2026-08-13  Added GetWindow_ID: resolves a window by NAME for the
///                        panel's record-open path, so a Reference chip whose
///                        screen is not the table's default zoom target (the
///                        sales order, which opens VAS_SalesOrder) can still be
///                        opened.
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

        /// <summary>
        /// Resolves a window's AD_Window_ID from its name, for the panel's
        /// record-open path. Used where the record's screen is not the table's
        /// default zoom target — the originating sales order opens the
        /// VAS_SalesOrder window, not whatever C_Order's zoom target resolves to.
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
                VAS_100_OverviewDOModel model = new VAS_100_OverviewDOModel();
                windowId = model.GetWindowId(ctx, fields);
            }
            return Json(JsonConvert.SerializeObject(windowId), JsonRequestBehavior.AllowGet);
        }
    }
}
