/************************************************************
 * Module Name    : VAS
 * Purpose        : VAS_309 - Not Yet Ordered KPI (2x1) on the Requisition screen.
 *                  Completed-requisition lines with quantity that is on no purchase order and
 *                  no Material Transfer and not delivered. Count of lines + amount; popup list
 *                  per requisition from the same call.
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
    public class VAS_309_ReqNotYetOrderedWidgetController : Controller
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
                VAS_ReqCurrency cur = VAS_ReqWidgetData.GetSchemaCurrency(ctx);
                List<VAS_ReqLine> lines = VAS_ReqWidgetData.LoadLines(ctx, cur, "r.DocStatus = 'CO'", null)
                    .Where(l => l.NotOrderedQty > 0).ToList();
                var data = new
                {
                    count = lines.Count,
                    amount = lines.Sum(l => l.Amount(l.NotOrderedQty)),
                    requisitions = lines.Select(l => l.RequisitionId).Distinct().Count(),
                    currency = cur,
                    records = VAS_ReqWidgetData.RequisitionRows(ctx, lines, l => l.Amount(l.NotOrderedQty))
                };
                return Json(JsonConvert.SerializeObject(data), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                VAS_ReqWidgetData.Log.Log(Level.SEVERE, "VAS_309_ReqNotYetOrderedWidget.GetData", ex);
                return VAS_ReqWidgetData.Error(ctx, "VAS_305_LoadError", "Could not load the data.");
            }
        }
    }
}
