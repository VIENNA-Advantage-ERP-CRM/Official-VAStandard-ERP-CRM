/******************************************************
 * Module Name    : VAS
 * Purpose        : Overall Inventory product search widget endpoints
 * chronological  : Development
 * Created Date   : 2026-06-21
 * Created by     : VAI154
 ******************************************************/

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
    /// Module Name : VAS_ProductSearchWidget
    /// Purpose     : Provides secured product suggestions and product-detail
    ///               data to the Overall Inventory Product Search widget.
    /// Chronological development:
    ///   VAI154      2026-06-21 Created
    /// </summary>
    public class VAS_ProductSearchWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_ProductSearchWidgetController).FullName);

        /// <summary>
        /// Searches products for the type-ahead suggestion dropdown.
        /// </summary>
        /// <param name="q">Product name, code, SKU, UPC, type, or category text.</param>
        /// <param name="max">Maximum suggestions; capped at seven.</param>
        /// <returns>JSON-serialized product suggestions.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult SearchProducts(string q, int max = 7)
        {
            if (Session["ctx"] == null)
            {
                return Json("", JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            try
            {
                VAS_ProductSearchWidgetModel model = new VAS_ProductSearchWidgetModel();
                string json = JsonConvert.SerializeObject(model.SearchProducts(ctx, q, max));
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_ProductSearchWidget.SearchProducts", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// Loads the selected product's overview, stock, orders, movements, and requisitions.
        /// </summary>
        /// <param name="M_Product_ID">Selected product identifier.</param>
        /// <returns>JSON-serialized product detail.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetProductDetail(int M_Product_ID)
        {
            if (Session["ctx"] == null)
            {
                return Json("", JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            try
            {
                VAS_ProductSearchWidgetModel model = new VAS_ProductSearchWidgetModel();
                string json = JsonConvert.SerializeObject(model.GetProductDetail(ctx, M_Product_ID));
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_ProductSearchWidget.GetProductDetail", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// Creates a localized error payload without exposing database details.
        /// </summary>
        /// <param name="ctx">Current session context.</param>
        /// <returns>JSON error response.</returns>
        private JsonResult ErrorResult(Ctx ctx)
        {
            string message = Msg.GetMsg(ctx, "Error") ?? "Error";
            string json = JsonConvert.SerializeObject(new { Error = message });
            return Json(json, JsonRequestBehavior.AllowGet);
        }
    }
}
