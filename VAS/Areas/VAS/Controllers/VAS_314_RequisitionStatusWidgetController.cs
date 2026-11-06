/************************************************************
 * Module Name    : VAS
 * Purpose        : VAS_314 - Requisitions by Status (3x2 donut) on the Requisition screen.
 *                  Requisitions dated in the last 12 months by the system requisition status
 *                  (VAS_RequisitionStatus rule evaluated live: Open, Partially Ordered, Ordered,
 *                  Partially Delivered, Delivered) plus Draft and Closed / Voided from DocStatus.
 *                  Count and value per slice; the slice popup list comes from the same call.
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
    public class VAS_314_RequisitionStatusWidgetController : Controller
    {
        private static readonly string[] SliceOrder = { "OP", "PO", "OR", "PD", "DE", VAS_ReqWidgetData.StatusDraft, VAS_ReqWidgetData.StatusClosed };

        /// <summary>Slices of the status donut.</summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetStatusSummary()
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
                List<SqlParameter> p = new List<SqlParameter> { new SqlParameter("@RQ_From", today.AddMonths(-12)) };
                List<VAS_ReqLine> lines = VAS_ReqWidgetData.LoadLines(ctx, cur, "r.DateDoc >= @RQ_From", p);

                Dictionary<string, List<VAS_ReqLine>> bySlice = new Dictionary<string, List<VAS_ReqLine>>();
                foreach (IGrouping<int, VAS_ReqLine> g in lines.GroupBy(l => l.RequisitionId))
                {
                    string bucket = VAS_ReqWidgetData.StatusBucket(g.First().DocStatus, g);
                    if (!bySlice.ContainsKey(bucket)) { bySlice[bucket] = new List<VAS_ReqLine>(); }
                    bySlice[bucket].AddRange(g);
                }

                var slices = SliceOrder.Select(code =>
                {
                    List<VAS_ReqLine> ls = bySlice.ContainsKey(code) ? bySlice[code] : new List<VAS_ReqLine>();
                    return new
                    {
                        code,
                        name = VAS_ReqWidgetData.BucketName(ctx, code),
                        count = ls.Select(l => l.RequisitionId).Distinct().Count(),
                        amount = ls.Sum(l => l.Amount(l.Qty)),
                        records = VAS_ReqWidgetData.RequisitionRows(ctx, ls, null)
                    };
                }).ToList();

                var data = new
                {
                    total = slices.Sum(s => s.count),
                    amount = slices.Sum(s => s.amount),
                    currency = cur,
                    slices
                };
                return Json(JsonConvert.SerializeObject(data), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                VAS_ReqWidgetData.Log.Log(Level.SEVERE, "VAS_314_RequisitionStatusWidget.GetStatusSummary", ex);
                return VAS_ReqWidgetData.Error(ctx, "VAS_305_LoadError", "Could not load the data.");
            }
        }
    }
}
