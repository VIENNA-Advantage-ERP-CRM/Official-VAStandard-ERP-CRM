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
    ///               the current calendar month (GRN-received qty net of vendor returns / ordered
    ///               qty over Item-type lines, received capped at ordered per line), plus the point change vs the
    ///               POs raised in the previous calendar month. Completed / closed vendor POs,
    ///               no returns, no blanket orders. Read-only. MRole on C_Order.
    /// Chronological development:
    ///   &lt;EmpCode&gt;   2026-06-18 Created
    ///   2026-09-15 QA sheet GRN #4/#5: month of the PO instead of a financial year-to-date window,
    ///              Item lines only, received capped at ordered, one-decimal result
    ///   VAI163     2026-10-29 Closed POs measured against the quantity originally ordered
    ///              (QtyOrdered + QtyLostSales) instead of the post-close QtyOrdered, which
    ///              CloseIt sets equal to QtyDelivered and so showed every closed PO as 100%
    ///   VAS163     2026-11-05 Received qty taken from completed material receipts (GRN lines) against
    ///              the PO line, net of completed vendor returns, instead of C_OrderLine.QtyDelivered
    ///              (match-driven: capped at the invoiced qty when the invoice came first, and not
    ///              reduced by vendor returns). Per line: LEAST(GREATEST(received - returned, 0), ordered).
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
            /* Whole calendar month: a PO of this month dated after today was left out before. */
            DateTime currentEndNext = currentStart.AddMonths(1);
            DateTime previousStart = currentStart.AddMonths(-1);
            DateTime previousEndNext = currentStart;

            /* Closing a PO (MOrder.CloseIt) rewrites QtyOrdered to QtyDelivered and parks the
               short-received rest in QtyLostSales, so a closed PO read as 100% fulfilled whatever
               was actually received. The quantity originally ordered is QtyOrdered + QtyLostSales
               on a closed PO (void is excluded by the DocStatus filter). */
            string orderedQty = "(COALESCE(OrdLine.QtyOrdered, 0) + CASE WHEN Ord.DocStatus = 'CL' THEN COALESCE(OrdLine.QtyLostSales, 0) ELSE 0 END)";

            /* The PO lines in scope (single WHERE, so MRole is applied here, before the wrapping). */
            string poLines = @"
                SELECT Ord.DateOrdered, OrdLine.C_OrderLine_ID, " + orderedQty + @" AS OrderedQty
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

            poLines = MRole.GetDefault(ctx).AddAccessSQL(poLines, "Ord", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            /* 5-Nov "incorrect data": the received quantity was read from C_OrderLine.QtyDelivered, which
               for purchase lines is maintained by M_MatchPO - when a vendor invoice was entered before the
               receipt, the match (and so QtyDelivered) is capped at the invoiced quantity, and goods sent
               back on a vendor return stay counted as received. The received quantity is now what the
               completed material receipts (GRNs) actually brought in against the PO line, less what
               completed vendor returns sent back against it (return-order lines pointing at the PO line
               via Orig_OrderLine_ID). Drafted / in-progress / reversed / voided receipts never count. */
            string sql = @"
                SELECT SUM(CASE WHEN l.DateOrdered >= @CurrentStart1 AND l.DateOrdered < @CurrentEndNext1
                                THEN l.OrderedQty ELSE 0 END) AS Current_Ordered,
                       SUM(CASE WHEN l.DateOrdered >= @CurrentStart2 AND l.DateOrdered < @CurrentEndNext2
                                THEN LEAST(GREATEST(l.ReceivedQty - l.ReturnedQty, 0), l.OrderedQty) ELSE 0 END) AS Current_Delivered,
                       SUM(CASE WHEN l.DateOrdered >= @PreviousStart1 AND l.DateOrdered < @PreviousEndNext1
                                THEN l.OrderedQty ELSE 0 END) AS Previous_Ordered,
                       SUM(CASE WHEN l.DateOrdered >= @PreviousStart2 AND l.DateOrdered < @PreviousEndNext2
                                THEN LEAST(GREATEST(l.ReceivedQty - l.ReturnedQty, 0), l.OrderedQty) ELSE 0 END) AS Previous_Delivered
                FROM (
                    SELECT po.DateOrdered, po.OrderedQty,
                           COALESCE((SELECT SUM(RcvLine.MovementQty)
                                     FROM M_InOutLine RcvLine
                                     INNER JOIN M_InOut Rcv ON (Rcv.M_InOut_ID=RcvLine.M_InOut_ID)
                                     WHERE RcvLine.C_OrderLine_ID=po.C_OrderLine_ID
                                       AND Rcv.IsSOTrx='N'
                                       AND COALESCE(Rcv.IsReturnTrx, 'N')='N'
                                       AND Rcv.DocStatus IN ('CO', 'CL')), 0) AS ReceivedQty,
                           COALESCE((SELECT SUM(RetInLine.MovementQty)
                                     FROM M_InOutLine RetInLine
                                     INNER JOIN M_InOut RetIn ON (RetIn.M_InOut_ID=RetInLine.M_InOut_ID)
                                     INNER JOIN C_OrderLine RetLine ON (RetLine.C_OrderLine_ID=RetInLine.C_OrderLine_ID)
                                     WHERE RetLine.Orig_OrderLine_ID=po.C_OrderLine_ID
                                       AND RetIn.IsSOTrx='N'
                                       AND RetIn.IsReturnTrx='Y'
                                       AND RetIn.DocStatus IN ('CO', 'CL')), 0) AS ReturnedQty
                    FROM (" + poLines + @") po
                ) l";

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
