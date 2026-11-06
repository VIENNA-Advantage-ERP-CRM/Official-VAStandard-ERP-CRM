/************************************************************
 * Module Name    : VAS
 * Purpose        : VAS_312 - My Requisitions (3x2) on the Requisition screen.
 *                  Requisitions of the logged-in user (requester, or creator when no requester
 *                  is set) dated in the last 12 months, newest first, with the live status.
 *                  Voided and reversed requisitions are left out (cancelled, nothing to follow up).
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
    public class VAS_312_MyRequisitionsWidgetController : Controller
    {
        /// <summary>The user's requisitions, newest first.</summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetMyRequisitions()
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null)
            {
                return VAS_ReqWidgetData.Error(Env.GetCtx(), "SessionExpired", "Session expired.");
            }
            try
            {
                DateTime today = DateTime.Now.Date;
                int userId = ctx.GetAD_User_ID();
                VAS_ReqCurrency cur = VAS_ReqWidgetData.GetSchemaCurrency(ctx);
                List<SqlParameter> p = new List<SqlParameter>
                {
                    new SqlParameter("@RQ_User1", userId),
                    new SqlParameter("@RQ_User2", userId),
                    new SqlParameter("@RQ_From", today.AddMonths(-12))
                };
                List<VAS_ReqLine> lines = VAS_ReqWidgetData.LoadLines(ctx, cur,
                    VAS_ReqWidgetData.RequestedBySql() + " AND r.DateDoc >= @RQ_From AND r.DocStatus NOT IN ('VO', 'RE')", p);

                var rows = lines.GroupBy(l => l.RequisitionId).Select(g =>
                {
                    VAS_ReqLine h = g.First();
                    string bucket = VAS_ReqWidgetData.StatusBucket(h.DocStatus, g);
                    DateTime? need = g.Where(l => l.OpenQty > 0 && l.NeedBy.HasValue).Select(l => l.NeedBy).Min() ?? g.Select(l => l.NeedBy).Min();
                    return new
                    {
                        requisitionId = h.RequisitionId,
                        documentNo = h.DocumentNo,
                        dateDoc = VAS_ReqWidgetData.D(h.DateDoc),
                        needBy = VAS_ReqWidgetData.D(need),
                        amount = g.Sum(l => l.Amount(l.Qty)),
                        lines = g.Count(),
                        status = bucket,
                        statusName = VAS_ReqWidgetData.BucketName(ctx, bucket),
                        isOpen = h.DocStatus == "CO" && g.Any(l => l.OpenQty > 0),
                        isLate = h.DocStatus == "CO" && g.Any(l => l.IsLate(today)),
                        sortDate = h.DateDoc
                    };
                }).OrderByDescending(x => x.sortDate).ThenByDescending(x => x.documentNo).ToList();

                var data = new
                {
                    count = rows.Count,
                    openCount = rows.Count(r => r.isOpen),
                    currency = cur,
                    rows = rows.Select(r => new { r.requisitionId, r.documentNo, r.dateDoc, r.needBy, r.amount, r.lines, r.status, r.statusName, r.isOpen, r.isLate })
                };
                return Json(JsonConvert.SerializeObject(data), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                VAS_ReqWidgetData.Log.Log(Level.SEVERE, "VAS_312_MyRequisitionsWidget.GetMyRequisitions", ex);
                return VAS_ReqWidgetData.Error(ctx, "VAS_305_LoadError", "Could not load the data.");
            }
        }
    }
}
