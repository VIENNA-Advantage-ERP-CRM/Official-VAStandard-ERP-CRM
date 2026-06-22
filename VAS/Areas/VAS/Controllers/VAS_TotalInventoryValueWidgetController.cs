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
    /// Module Name : VAS_TotalInventoryValueWidget
    /// Purpose     : Supplies schema-currency inventory valuation.
    /// Chronological development:
    ///   VAI154      2026-06-21 Created
    ///   VAI154      2026-06-22 Updated for schema currency
    /// </summary>
    public class VAS_TotalInventoryValueWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_TotalInventoryValueWidgetController).FullName);

        /// <summary>
        /// Returns inventory value and schema currency formatting metadata.
        /// </summary>
        /// <returns>JSON inventory value result.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetTotalInventoryValue()
        {
            if (Session["ctx"] == null)
            {
                return Json("", JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            try
            {
                VAS_TotalInventoryValueWidgetModel model = new VAS_TotalInventoryValueWidgetModel();
                VAS_TotalInventoryValueResult result = model.GetTotalInventoryValue(ctx);
                string json = JsonConvert.SerializeObject(new
                {
                    total_inventory_value = result.TotalInventoryValue,
                    currency_symbol = result.CurrencySymbol,
                    currency_iso = result.CurrencyIso,
                    std_precision = result.StdPrecision
                });
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_TotalInventoryValueWidget.GetTotalInventoryValue", ex);
                string json = JsonConvert.SerializeObject(new { error = Msg.GetMsg(ctx, "Error") ?? "Error" });
                return Json(json, JsonRequestBehavior.AllowGet);
            }
        }
    }
}
