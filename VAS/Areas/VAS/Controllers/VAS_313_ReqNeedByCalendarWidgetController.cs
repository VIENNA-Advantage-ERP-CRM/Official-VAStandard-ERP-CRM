/************************************************************
 * Module Name    : VAS
 * Purpose        : VAS_313 - Need-by Calendar (6x4) on the Requisition screen.
 *                  Completed-requisition lines grouped by need-by date and requisition for one
 *                  month. State per entry: late (need-by passed, quantity still to come),
 *                  notordered (something not on a PO / transfer), ordered (all ordered, not all
 *                  received) or received. Also the number of late requisitions overall.
 * Author         : VAS
 * Date           : 05 November 2026
 ***********************************************************/

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Web.Mvc;
using VAdvantage.Logging;
using VAdvantage.Utility;
using VIS.Filters;

namespace VIS.Controllers
{
    public class VAS_313_ReqNeedByCalendarWidgetController : Controller
    {
        /// <summary>Calendar entries of one month (month 1-12; 0 = current month).</summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetMonth(int year = 0, int month = 0)
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null)
            {
                return VAS_ReqWidgetData.Error(Env.GetCtx(), "SessionExpired", "Session expired.");
            }
            try
            {
                DateTime today = DateTime.Now.Date;
                if (year < 1900 || year > 2999 || month < 1 || month > 12)
                {
                    year = today.Year;
                    month = today.Month;
                }
                DateTime from = new DateTime(year, month, 1);
                DateTime to = from.AddMonths(1);
                string needBy = VAS_ReqWidgetData.NeedBySql();
                VAS_ReqCurrency cur = VAS_ReqWidgetData.GetSchemaCurrency(ctx);
                // VAS163 2026-11-20: Sr 50 - only the selected month: the "late" count in the header is the late
                // requisitions whose need-by date falls in this month (it was every late requisition up to today)
                List<SqlParameter> p = new List<SqlParameter>
                {
                    new SqlParameter("@RQ_From", from),
                    new SqlParameter("@RQ_To", to)
                };
                List<VAS_ReqLine> lines = VAS_ReqWidgetData.LoadLines(ctx, cur,
                    "r.DocStatus = 'CO' AND " + needBy + " >= @RQ_From AND " + needBy + " < @RQ_To", p);

                int lateCount = lines.Where(l => l.IsLate(today) && l.NeedBy.HasValue && l.NeedBy.Value >= from && l.NeedBy.Value < to)
                    .Select(l => l.RequisitionId).Distinct().Count();

                var entries = lines.Where(l => l.NeedBy.HasValue && l.NeedBy.Value >= from && l.NeedBy.Value < to)
                    .GroupBy(l => new { Day = l.NeedBy.Value.Date, l.RequisitionId })
                    .Select(g =>
                    {
                        List<VAS_ReqLine> ls = g.OrderBy(l => l.LineNo).ToList();
                        string state = ls.Any(l => l.IsLate(today)) ? "late"
                            : ls.All(l => l.OpenQty <= 0) ? "received"
                            : ls.Any(l => l.NotOrderedQty > 0) ? "notordered" : "ordered";
                        return new
                        {
                            date = g.Key.Day.ToString("yyyy-MM-dd"),
                            requisitionId = g.Key.RequisitionId,
                            documentNo = ls[0].DocumentNo,
                            requester = ls[0].RequesterName,
                            firstItem = ls[0].ItemName,
                            lineCount = ls.Count,
                            amount = ls.Sum(l => l.Amount(l.Qty)),
                            // VAS163 2026-11-20: Sr 47 - amount in the requisition's price-list currency with its sign
                            docAmount = ls.Sum(l => l.DocAmount(l.Qty)),
                            currencyIso = ls[0].CurrencyId > 0 ? ls[0].DocCurrencyIso : "",
                            currencySymbol = ls[0].CurrencyId > 0 ? ls[0].DocCurrencySymbol : "",
                            currencyPrecision = ls[0].CurrencyId > 0 ? ls[0].DocCurrencyPrecision : -1,
                            state,
                            rank = state == "late" ? 0 : state == "notordered" ? 1 : state == "ordered" ? 2 : 3,
                            items = ls.Select(l => new
                            {
                                product = l.ItemName,
                                // VAS163 2026-11-17: Sr 7 - quantities in the line's UOM (shown with it)
                                qty = l.QtyEntered,
                                uom = l.UomName,
                                pendingQty = l.OpenQty * l.UomFactor,
                                state = l.IsLate(today) ? "late" : l.State()
                            })
                        };
                    })
                    .OrderBy(e => e.date).ThenBy(e => e.rank).ThenBy(e => e.documentNo).ToList();

                var data = new
                {
                    year,
                    month,
                    today = today.ToString("yyyy-MM-dd"),
                    lateCount,
                    currency = cur,
                    entries
                };
                return Json(JsonConvert.SerializeObject(data), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                VAS_ReqWidgetData.Log.Log(Level.SEVERE, "VAS_313_ReqNeedByCalendarWidget.GetMonth", ex);
                return VAS_ReqWidgetData.Error(ctx, "VAS_305_LoadError", "Could not load the data.");
            }
        }
    }
}
