using Newtonsoft.Json;
using System;
using System.Web.Mvc;
using VAdvantage.Logging;
using VAdvantage.Utility;
using VASLogic.Models;
using VIS.Filters;

namespace VAS.Controllers
{
    /// <summary>
    /// Module Name : VAS_NewStockAdjustmentWidget
    /// Purpose     : Supplies the portable Physical Inventory window reference.
    /// Chronological development:
    ///   VAI154      2026-06-22 Created
    /// </summary>
    public class VAS_NewStockAdjustmentWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_NewStockAdjustmentWidgetController).FullName);

        /// <summary>Returns the active Physical Inventory window ID resolved by Export_ID.</summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetInventoryCountWindowId()
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null) { return Json("", JsonRequestBehavior.AllowGet); }

            try
            {
                VAS_NewStockAdjustmentWidgetModel model = new VAS_NewStockAdjustmentWidgetModel();
                string json = JsonConvert.SerializeObject(new
                {
                    window_id = model.GetInventoryCountWindowId(ctx)
                });
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_NewStockAdjustmentWidget.GetInventoryCountWindowId", ex);
                string json = JsonConvert.SerializeObject(new { error = Msg.GetMsg(ctx, "Error") ?? "Error" });
                return Json(json, JsonRequestBehavior.AllowGet);
            }
        }
    }
}
