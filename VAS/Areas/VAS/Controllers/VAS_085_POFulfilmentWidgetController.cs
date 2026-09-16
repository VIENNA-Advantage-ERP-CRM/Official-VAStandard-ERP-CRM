using Newtonsoft.Json;
using System;
using System.Data;
using System.Data.SqlClient;
using System.Web.Mvc;
using VAdvantage.Classes;
using VAdvantage.DataBase;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VIS.Controllers
{
    /// <summary>
    /// Module Name : PO Fulfilment % (Material Receipt / GRN dashboard KPI)
    /// Purpose     : KPI = purchase-order fulfilment percentage for purchase orders raised in
    ///               the current calendar month (received qty / ordered qty over Item-type
    ///               lines, received capped at ordered per line), plus the point change vs the
    ///               POs raised in the previous calendar month. Completed / closed vendor POs,
    ///               no returns, no blanket orders. Read-only. MRole on C_Order.
    /// Chronological development:
    ///   &lt;EmpCode&gt;   2026-06-18 Created
    ///   2026-09-15 QA sheet GRN #4/#5: month of the PO instead of a financial year-to-date window,
    ///              Item lines only, received capped at ordered, one-decimal result
    /// </summary>
    public class VAS_085_POFulfilmentWidgetController : Controller
    {
        /// <summary>
        /// KPI tile data: current-month PO fulfilment % and the point change versus last month.
        /// </summary>
        /// <returns>JSON { currentPercent, changePercent }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetPOFulfilment()
        {
            if (Session["ctx"] == null)
            {
                return Json(new
                {
                    error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired"
                }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            /* The old window was the whole financial year to date by promised date, rounded to a
               whole percent: one new PO or one completed GRN could not move the figure, a PO promised
               beyond the current period was ignored, and a tenant without C_Period rows got no value.
               Now: POs raised this calendar month versus those raised last calendar month. */
            DateTime today = DateTime.Today;
            DateTime currentStart = new DateTime(today.Year, today.Month, 1);
            DateTime currentEndNext = today.AddDays(1);
            DateTime previousStart = currentStart.AddMonths(-1);
            DateTime previousEndNext = currentStart;

            string sql = @"
                SELECT SUM(CASE WHEN Ord.DateOrdered >= @CurrentStart1 AND Ord.DateOrdered < @CurrentEndNext1
                                THEN COALESCE(OrdLine.QtyOrdered, 0) ELSE 0 END) AS Current_Ordered,
                       SUM(CASE WHEN Ord.DateOrdered >= @CurrentStart2 AND Ord.DateOrdered < @CurrentEndNext2
                                THEN LEAST(COALESCE(OrdLine.QtyDelivered, 0), COALESCE(OrdLine.QtyOrdered, 0)) ELSE 0 END) AS Current_Delivered,
                       SUM(CASE WHEN Ord.DateOrdered >= @PreviousStart1 AND Ord.DateOrdered < @PreviousEndNext1
                                THEN COALESCE(OrdLine.QtyOrdered, 0) ELSE 0 END) AS Previous_Ordered,
                       SUM(CASE WHEN Ord.DateOrdered >= @PreviousStart2 AND Ord.DateOrdered < @PreviousEndNext2
                                THEN LEAST(COALESCE(OrdLine.QtyDelivered, 0), COALESCE(OrdLine.QtyOrdered, 0)) ELSE 0 END) AS Previous_Delivered
                FROM C_Order Ord
                INNER JOIN C_OrderLine OrdLine ON (OrdLine.C_Order_ID=Ord.C_Order_ID)
                INNER JOIN M_Product Product ON (Product.M_Product_ID=OrdLine.M_Product_ID)
                WHERE Ord.IsActive='Y'
                  AND OrdLine.IsActive='Y'
                  AND Ord.IsSOTrx='N'
                  AND COALESCE(Ord.IsReturnTrx, 'N')='N'
                  AND COALESCE(Ord.IsBlanketTrx, 'N')='N'
                  AND Ord.DocStatus IN ('CO', 'CL')
                  AND Product.ProductType='I'
                  AND Ord.DateOrdered >= @RangeStart
                  AND Ord.DateOrdered < @RangeEndNext";

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "Ord", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            /* The database layer binds positionally: one parameter per placeholder occurrence, in order. */
            SqlParameter[] parameters =
            {
                new SqlParameter("@CurrentStart1", currentStart),
                new SqlParameter("@CurrentEndNext1", currentEndNext),
                new SqlParameter("@CurrentStart2", currentStart),
                new SqlParameter("@CurrentEndNext2", currentEndNext),
                new SqlParameter("@PreviousStart1", previousStart),
                new SqlParameter("@PreviousEndNext1", previousEndNext),
                new SqlParameter("@PreviousStart2", previousStart),
                new SqlParameter("@PreviousEndNext2", previousEndNext),
                new SqlParameter("@RangeStart", previousStart),
                new SqlParameter("@RangeEndNext", currentEndNext)
            };

            IDataReader dr = null;

            try
            {
                decimal currentPercent = 0;
                decimal previousPercent = 0;

                dr = DB.ExecuteReader(sql, parameters);
                if (dr != null && dr.Read())
                {
                    currentPercent = Percent(Util.GetValueOfDecimal(dr["Current_Delivered"]), Util.GetValueOfDecimal(dr["Current_Ordered"]));
                    previousPercent = Percent(Util.GetValueOfDecimal(dr["Previous_Delivered"]), Util.GetValueOfDecimal(dr["Previous_Ordered"]));
                }

                var result = new
                {
                    currentPercent = currentPercent,
                    changePercent = Math.Round(currentPercent - previousPercent, 1, MidpointRounding.AwayFromZero)
                };

                return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
            finally
            {
                if (dr != null)
                {
                    dr.Close();
                    dr.Dispose();
                }
            }
        }

        /// <summary>Received / ordered as a percentage with one decimal; 0 when nothing was ordered.</summary>
        private static decimal Percent(decimal delivered, decimal ordered)
        {
            if (ordered <= 0)
            {
                return 0;
            }
            return Math.Round(delivered * 100 / ordered, 1, MidpointRounding.AwayFromZero);
        }
    }
}
