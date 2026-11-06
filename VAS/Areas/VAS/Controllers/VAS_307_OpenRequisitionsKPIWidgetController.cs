/************************************************************
 * Module Name    : VAS
 * Purpose        : VAS_307 - Open Requisitions KPI (2x1) on the Requisition screen.
 *                  Completed requisitions with quantity still to come (not ordered, or ordered
 *                  on a PO / transfer and not yet received). Count + open amount in the
 *                  accounting-schema currency; the popup list comes from the same call.
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
    public class VAS_307_OpenRequisitionsKPIWidgetController : Controller
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
                List<VAS_ReqLine> open = VAS_ReqWidgetData.LoadLines(ctx, cur, "r.DocStatus = 'CO'", null)
                    .Where(l => l.OpenQty > 0).ToList();
                var data = new
                {
                    count = open.Select(l => l.RequisitionId).Distinct().Count(),
                    amount = open.Sum(l => l.Amount(l.OpenQty)),
                    lineCount = open.Count,
                    currency = cur,
                    records = VAS_ReqWidgetData.RequisitionRows(ctx, open, l => l.Amount(l.OpenQty))
                };
                return Json(JsonConvert.SerializeObject(data), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                VAS_ReqWidgetData.Log.Log(Level.SEVERE, "VAS_307_OpenRequisitionsKPIWidget.GetData", ex);
                return VAS_ReqWidgetData.Error(ctx, "VAS_305_LoadError", "Could not load the data.");
            }
        }
    }
}
