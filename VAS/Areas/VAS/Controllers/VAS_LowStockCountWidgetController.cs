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
    /// Module Name : VAS_LowStockCountWidget
    /// Purpose     : Supplies the Low Stock Count KPI.
    /// Chronological development:
    ///   VAI154      2026-06-21 Created
    ///   VAI154      2026-06-22 Compliance update
    /// </summary>
    public class VAS_LowStockCountWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_LowStockCountWidgetController).FullName);

        /// <summary>
        /// Returns the secured low-stock count.
        /// </summary>
        /// <returns>JSON low-stock count.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetLowStockCount()
        {
            if (Session["ctx"] == null)
            {
                return Json("", JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            try
            {
                VAS_LowStockCountWidgetModel model = new VAS_LowStockCountWidgetModel();
                int lowStockCount = model.GetLowStockCount(ctx);
                string json = JsonConvert.SerializeObject(new { low_stock_count = lowStockCount });
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_LowStockCountWidget.GetLowStockCount", ex);
                string json = JsonConvert.SerializeObject(new { error = Msg.GetMsg(ctx, "Error") ?? "Error" });
                return Json(json, JsonRequestBehavior.AllowGet);
            }
        }
    }
}
