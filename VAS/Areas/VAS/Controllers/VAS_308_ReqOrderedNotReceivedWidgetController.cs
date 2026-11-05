/************************************************************
 * Module Name    : VAS
 * Purpose        : VAS_308 - Ordered, Not Received KPI (2x1) on the Requisition screen.
 *                  Requisition lines on a purchase order without GRN for the full quantity,
 *                  or on a Material Transfer that is not completed yet. Count of lines +
 *                  amount still to receive; popup list per requisition from the same call.
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
    public class VAS_308_ReqOrderedNotReceivedWidgetController : Controller
    {
        /// <summary>KPI figures and the requisitions behind them.</summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetData()
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
                    .Where(l => l.InProgressQty > 0).ToList();
                var data = new
                {
                    count = lines.Count,
                    amount = lines.Sum(l => l.Amount(l.InProgressQty)),
                    pastNeedBy = lines.Count(l => l.NeedBy.HasValue && l.NeedBy.Value.Date < today),
                    onPurchaseOrder = lines.Count(l => l.PoQty > l.PoRcvQty),
                    onTransfer = lines.Count(l => l.TrfOpenQty > 0),
                    currency = cur,
                    records = VAS_ReqWidgetData.RequisitionRows(ctx, lines, l => l.Amount(l.InProgressQty))
                };
                return Json(JsonConvert.SerializeObject(data), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                VAS_ReqWidgetData.Log.Log(Level.SEVERE, "VAS_308_ReqOrderedNotReceivedWidget.GetData", ex);
                return VAS_ReqWidgetData.Error(ctx, "VAS_305_LoadError", "Could not load the data.");
            }
        }
    }
}
