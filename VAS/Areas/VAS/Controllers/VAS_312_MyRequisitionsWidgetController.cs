/************************************************************
 * Module Name    : VAS
 * Purpose        : VAS_312 - My Requisitions (3x2) on the Requisition screen.
 *                  Requisitions created by the logged-in user, dated in the selected month / year,
 *                  newest first, with the live status; every document status (VAS163 2026-11-20: Sr 45 / Sr 46).
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
        /// <summary>The user's requisitions of one month, newest first.</summary>
        /// <param name="year">VAS163 2026-11-20: Sr 45 - year of the Month / Year filter (0 = current)</param>
        /// <param name="month">VAS163 2026-11-20: Sr 45 - month 1-12 of the filter (0 = current)</param>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetMyRequisitions(int year = 0, int month = 0)
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
                // VAS163 2026-11-20: Sr 45 - requisitions dated (DateDoc) in the selected month of the selected year
                if (year < 1900 || year > 2999 || month < 1 || month > 12)
                {
                    year = today.Year;
                    month = today.Month;
                }
                DateTime from = new DateTime(year, month, 1);
                DateTime to = from.AddMonths(1);
                // VAS163 2026-11-20: Sr 46 - requisitions of every document status (voided / reversed included), created
                // by the logged-in user only (M_Requisition.CreatedBy; requester / preparer no longer counts)
                const string reqWhere = "r.CreatedBy = @RQ_User1 AND r.DateDoc >= @RQ_From AND r.DateDoc < @RQ_To";
                Func<List<SqlParameter>> prm = () => new List<SqlParameter>
                {
                    new SqlParameter("@RQ_User1", userId),
                    new SqlParameter("@RQ_From", from),
                    new SqlParameter("@RQ_To", to)
                };
                List<VAS_ReqLine> lines = VAS_ReqWidgetData.LoadLines(ctx, cur, reqWhere, prm());
                // VAS163 2026-11-20: Sr 46 / Sr 61 - drafts without lines appear too (header-only entry)
                lines.AddRange(VAS_ReqWidgetData.LoadHeadersWithoutLines(ctx, reqWhere, prm()));

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
                        lines = g.Count(l => l.LineId > 0),
                        status = bucket,
                        statusName = VAS_ReqWidgetData.BucketName(ctx, bucket),
                        isOpen = h.DocStatus == "CO" && g.Any(l => l.OpenQty > 0),
                        isLate = h.DocStatus == "CO" && g.Any(l => l.IsLate(today)),
                        sortDate = h.DateDoc
                    };
                }).OrderByDescending(x => x.sortDate).ThenByDescending(x => x.documentNo).ToList();

                var data = new
                {
                    year,
                    month,
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
