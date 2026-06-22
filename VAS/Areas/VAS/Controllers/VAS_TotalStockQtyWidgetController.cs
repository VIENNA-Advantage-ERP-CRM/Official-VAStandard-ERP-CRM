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
    /// Module Name : VAS_TotalStockQtyWidget
    /// Purpose     : Supplies the Total Stock Qty KPI.
    /// Chronological development:
    ///   VAI154      2026-06-21 Created
    ///   VAI154      2026-06-22 Compliance update
    /// </summary>
    public class VAS_TotalStockQtyWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_TotalStockQtyWidgetController).FullName);

        /// <summary>
        /// Returns secured active on-hand quantity.
        /// </summary>
        /// <returns>JSON total stock quantity.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetTotalStockQty()
        {
            if (Session["ctx"] == null)
            {
                return Json("", JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            try
            {
                VAS_TotalStockQtyWidgetModel model = new VAS_TotalStockQtyWidgetModel();
                decimal totalStockQty = model.GetTotalStockQty(ctx);
                string json = JsonConvert.SerializeObject(new { total_stock_qty = totalStockQty });
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_TotalStockQtyWidget.GetTotalStockQty", ex);
                string json = JsonConvert.SerializeObject(new { error = Msg.GetMsg(ctx, "Error") ?? "Error" });
                return Json(json, JsonRequestBehavior.AllowGet);
            }
        }
    }
}
