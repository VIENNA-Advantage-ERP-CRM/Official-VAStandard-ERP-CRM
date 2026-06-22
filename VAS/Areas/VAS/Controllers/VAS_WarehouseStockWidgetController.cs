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
    /// Module Name : VAS_WarehouseStockWidget
    /// Purpose     : Supplies warehouse and locator stock-ageing data.
    /// Chronological development:
    ///   VAI154      2026-06-21 Created
    ///   VAI154      2026-06-22 Updated for schema currency
    /// </summary>
    public class VAS_WarehouseStockWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_WarehouseStockWidgetController).FullName);

        /// <summary>Returns active warehouses available to the current role.</summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetWarehouses()
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null) { return Json("", JsonRequestBehavior.AllowGet); }

            try
            {
                VAS_WarehouseStockWidgetModel model = new VAS_WarehouseStockWidgetModel();
                return Json(JsonConvert.SerializeObject(model.GetWarehouses(ctx)), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return ErrorResult(ctx, "GetWarehouses", ex);
            }
        }

        /// <summary>Returns secured locator stock and schema currency.</summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetStockRows()
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null) { return Json("", JsonRequestBehavior.AllowGet); }

            try
            {
                VAS_WarehouseStockWidgetModel model = new VAS_WarehouseStockWidgetModel();
                return Json(JsonConvert.SerializeObject(model.GetStockRows(ctx)), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return ErrorResult(ctx, "GetStockRows", ex);
            }
        }

        /// <summary>Logs an endpoint failure and returns a localized error.</summary>
        private JsonResult ErrorResult(Ctx ctx, string action, Exception ex)
        {
            Log.Log(Level.SEVERE, "VAS_WarehouseStockWidget." + action, ex);
            string json = JsonConvert.SerializeObject(new { error = Msg.GetMsg(ctx, "Error") ?? "Error" });
            return Json(json, JsonRequestBehavior.AllowGet);
        }
    }
}
