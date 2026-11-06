/************************************************************
 * Module Name    : VAS
 * Purpose        : VAS_315 - Category wise Requisition (3x2) on the Requisition screen.
 *                  Requested value of completed / closed requisitions dated in the last
 *                  12 months by product category (charges and products without a category in
 *                  their own group), largest first, with the lines for the category popup.
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
    public class VAS_315_CategoryWiseRequisitionWidgetController : Controller
    {
        /// <summary>Requested value per product category.</summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetCategories()
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
                List<VAS_ReqLine> lines = VAS_ReqWidgetData.LoadLines(ctx, cur, "r.DocStatus IN ('CO', 'CL') AND r.DateDoc >= @RQ_From", p);

                var cats = lines.GroupBy(l => new { l.CategoryId, l.CategoryName })
                    .Select(g => new
                    {
                        categoryId = g.Key.CategoryId,
                        name = g.Key.CategoryName,
                        amount = g.Sum(l => l.Amount(l.Qty)),
                        requisitions = g.Select(l => l.RequisitionId).Distinct().Count(),
                        lineCount = g.Count(),
                        lines = g.OrderBy(l => l.NeedBy ?? DateTime.MaxValue).Select(l => new
                        {
                            requisitionId = l.RequisitionId,
                            documentNo = l.DocumentNo,
                            product = l.ItemName,
                            qty = l.Qty,
                            uom = l.UomName,
                            needBy = VAS_ReqWidgetData.D(l.NeedBy),
                            amount = l.Amount(l.Qty),
                            state = l.IsLate(today) ? "late" : l.State()
                        })
                    })
                    .OrderByDescending(c => c.amount).ThenBy(c => c.name).ToList();

                var data = new
                {
                    total = cats.Sum(c => c.amount),
                    currency = cur,
                    categories = cats
                };
                return Json(JsonConvert.SerializeObject(data), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                VAS_ReqWidgetData.Log.Log(Level.SEVERE, "VAS_315_CategoryWiseRequisitionWidget.GetCategories", ex);
                return VAS_ReqWidgetData.Error(ctx, "VAS_305_LoadError", "Could not load the data.");
            }
        }
    }
}
