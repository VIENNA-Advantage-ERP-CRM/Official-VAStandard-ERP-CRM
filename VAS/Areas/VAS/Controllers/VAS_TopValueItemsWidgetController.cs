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
    /// Module Name : VAS_TopValueItemsWidget
    /// Purpose     : Supplies warehouse options and paged high-value stock.
    /// Chronological development:
    ///   VAI154      2026-06-21 Created
    ///   VAI154      2026-06-22 Added schema currency and paging
    /// </summary>
    public class VAS_TopValueItemsWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_TopValueItemsWidgetController).FullName);

        /// <summary>Returns active warehouses available to the current role.</summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetWarehouses()
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null) { return Json("", JsonRequestBehavior.AllowGet); }

            try
            {
                VAS_TopValueItemsWidgetModel model = new VAS_TopValueItemsWidgetModel();
                return Json(JsonConvert.SerializeObject(model.GetWarehouses(ctx)), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return ErrorResult(ctx, "GetWarehouses", ex);
            }
        }

        /// <summary>Returns one secured page of high-value stock.</summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetTopValueItems(int? warehouseId, int pageNo = 1, int pageSize = 4)
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null) { return Json("", JsonRequestBehavior.AllowGet); }

            try
            {
                VAS_TopValueItemsWidgetModel model = new VAS_TopValueItemsWidgetModel();
                return Json(JsonConvert.SerializeObject(model.GetTopValueItems(ctx, warehouseId, pageNo, pageSize)), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return ErrorResult(ctx, "GetTopValueItems", ex);
            }
        }

        /// <summary>Logs an endpoint failure and returns a localized error.</summary>
        private JsonResult ErrorResult(Ctx ctx, string action, Exception ex)
        {
            Log.Log(Level.SEVERE, "VAS_TopValueItemsWidget." + action, ex);
            string json = JsonConvert.SerializeObject(new { error = Msg.GetMsg(ctx, "Error") ?? "Error" });
            return Json(json, JsonRequestBehavior.AllowGet);
        }
    }
}
