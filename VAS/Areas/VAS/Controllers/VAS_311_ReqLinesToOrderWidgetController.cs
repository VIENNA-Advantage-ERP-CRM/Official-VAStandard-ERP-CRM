/************************************************************
 * Module Name    : VAS
 * Purpose        : VAS_311 - Lines to Order (6x2) on the Requisition screen.
 *                  Completed-requisition lines with quantity on no PO and no Material Transfer,
 *                  earliest need-by first, with the product's current vendor (M_Product_PO
 *                  IsCurrentVendor) as suggestion and the requisition's source warehouse.
 * Author         : VAS
 * Date           : 05 November 2026
 ***********************************************************/

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using VAdvantage.Logging;
using VAdvantage.Utility;
using VIS.Filters;

namespace VIS.Controllers
{
    public class VAS_311_ReqLinesToOrderWidgetController : Controller
    {
        /// <summary>Lines still to order, earliest need-by first.</summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetLines()
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null)
            {
                return VAS_ReqWidgetData.Error(Env.GetCtx(), "SessionExpired", "Session expired.");
            }
            try
            {
                DateTime today = DateTime.Now.Date;
                VAS_ReqCurrency cur = VAS_ReqWidgetData.GetSchemaCurrency(ctx);
                List<VAS_ReqLine> lines = VAS_ReqWidgetData.LoadLines(ctx, cur, "r.DocStatus = 'CO'", null)
                    .Where(l => l.NotOrderedQty > 0)
                    .OrderBy(l => l.NeedBy ?? DateTime.MaxValue).ThenBy(l => l.DocumentNo).ThenBy(l => l.LineNo).ToList();
                var rows = lines.Select(l => new
                {
                    requisitionId = l.RequisitionId,
                    documentNo = l.DocumentNo,
                    lineNo = l.LineNo,
                    product = l.ItemName,
                    productCode = l.ProductValue,
                    qty = l.NotOrderedQty,
                    uom = l.UomName,
                    amount = l.Amount(l.NotOrderedQty),
                    needBy = VAS_ReqWidgetData.D(l.NeedBy),
                    daysToNeed = l.NeedBy.HasValue ? (int)(l.NeedBy.Value.Date - today).TotalDays : 9999,
                    vendor = l.VendorName,
                    sourceWarehouse = l.SourceWarehouseName,
                    partlyOrdered = l.NotOrderedQty < l.Qty
                }).ToList();
                var data = new
                {
                    count = rows.Count,
                    amount = rows.Sum(r => r.amount),
                    currency = cur,
                    rows
                };
                return Json(JsonConvert.SerializeObject(data), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                VAS_ReqWidgetData.Log.Log(Level.SEVERE, "VAS_311_ReqLinesToOrderWidget.GetLines", ex);
                return VAS_ReqWidgetData.Error(ctx, "VAS_305_LoadError", "Could not load the data.");
            }
        }
    }
}
