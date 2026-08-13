/// <summary>
/// Module Name : VAS
/// Purpose     : Ship / GRN Confirmation Overview tab panel endpoint. Returns the
///               read-only overview payload for the selected in/out confirmation
///               (M_InOutConfirm) consumed by the
///               VAS.VAS_104_OverviewShipGRNConfirmation tab panel.
/// Chronological development:
///   VAI163   2026-07-07  Created
///   VAI163   2026-08-12  Added GetWindow_ID: resolves a window by NAME for the
///                        panel's source-document link, so the confirmation's GRN
///                        opens the Material Receipt screen and its shipment the
///                        Delivery Order one. M_InOut serves both sides, and the
///                        browser's zoom lookup cannot tell which is wanted.
///                        Ported from VAS_106.
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

        /// <summary>
        /// Resolves a window's AD_Window_ID from its name, for the panel's
        /// source-document link. The source is an M_InOut record on either side of
        /// the trade — a goods receipt or a delivery order — and the browser's zoom
        /// lookup cannot tell which screen is wanted, so the window is named.
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
                VAS_104_OverviewShipGRNConfirmationModel model = new VAS_104_OverviewShipGRNConfirmationModel();
                windowId = model.GetWindowId(ctx, fields);
            }
            return Json(JsonConvert.SerializeObject(windowId), JsonRequestBehavior.AllowGet);
        }
    }
}
