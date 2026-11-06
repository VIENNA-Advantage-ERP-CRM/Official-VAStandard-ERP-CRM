/************************************************************
 * Module Name    : VAS
 * Purpose        : VAS_310 - Late Requisitions KPI (2x1) on the Requisition screen.
 *                  Completed requisitions with a line whose need-by date has passed while
 *                  quantity is still to come (not ordered or not received). Count + amount
 *                  of the late quantity; popup list per requisition from the same call.
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
    public class VAS_310_LateRequisitionsWidgetController : Controller
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
                List<VAS_ReqLine> late = VAS_ReqWidgetData.LoadLines(ctx, cur, "r.DocStatus = 'CO'", null)
                    .Where(l => l.IsLate(today)).ToList();
                int oldest = late.Count == 0 ? 0 : late.Max(l => (int)(today - l.NeedBy.Value.Date).TotalDays);
                var data = new
                {
                    count = late.Select(l => l.RequisitionId).Distinct().Count(),
                    amount = late.Sum(l => l.Amount(l.OpenQty)),
                    lineCount = late.Count,
                    oldestDays = oldest,
                    currency = cur,
                    records = VAS_ReqWidgetData.RequisitionRows(ctx, late, l => l.Amount(l.OpenQty))
                };
                return Json(JsonConvert.SerializeObject(data), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                VAS_ReqWidgetData.Log.Log(Level.SEVERE, "VAS_310_LateRequisitionsWidget.GetData", ex);
                return VAS_ReqWidgetData.Error(ctx, "VAS_305_LoadError", "Could not load the data.");
            }
        }
    }
}
