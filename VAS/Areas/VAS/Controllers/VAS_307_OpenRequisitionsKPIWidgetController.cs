/************************************************************
 * Module Name    : VAS
 * Purpose        : VAS_307 - Open Requisitions KPI (2x1) on the Requisition screen.
 *                  VAS163 2026-11-17: Sr 26 - completed / closed requisitions with no document created
 *                  against any line (PO, blanket PO, Material Transfer, Inventory Use, RFQ). Count +
 *                  amount in the accounting-schema currency; the popup list comes from the same call.
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
                // VAS163 2026-11-17: Sr 26 - completed or closed requisitions against which no document has been created
                // yet (no PO / blanket PO, Material Transfer, Inventory Use or RFQ on any line; drafts count as created,
                // voided / reversed documents do not). Value = whole requisition amount.
                List<VAS_ReqLine> open = VAS_ReqWidgetData.LoadLines(ctx, cur,
                    "r.DocStatus IN ('CO', 'CL') AND " + VAS_ReqWidgetData.NoDocumentSql(), null);
                var data = new
                {
                    count = open.Select(l => l.RequisitionId).Distinct().Count(),
                    amount = open.Sum(l => l.Amount(l.Qty)),
                    lineCount = open.Count,
                    currency = cur,
                    // VAS163 2026-11-17: Sr 27 - rows also carry the value in the requisition's price-list currency
                    records = VAS_ReqWidgetData.RequisitionRows(ctx, open, l => l.Amount(l.Qty), l => l.DocAmount(l.Qty))
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
