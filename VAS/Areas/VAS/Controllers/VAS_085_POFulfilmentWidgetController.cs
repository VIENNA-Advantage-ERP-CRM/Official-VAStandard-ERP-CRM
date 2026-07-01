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
    /// Purpose     : KPI = overall purchase-order fulfilment percentage
    ///               (SUM(QtyDelivered) / SUM(QtyOrdered) * 100) across COMPLETED
    ///               purchase orders whose effective date (line promised, else
    ///               header promised, else order date) falls in the current
    ///               financial year through the active period, plus the
    ///               percentage-point change vs the previous period. Read-only.
    ///               MRole is applied to the C_Order body inside EACH CTE (the
    ///               main physical table); never to the CTE aliases nor the outer
    ///               query (shared Prompt_Instructions CTE rule).
    /// Chronological development:
    ///   &lt;EmpCode&gt;   2026-06-18 Created
    /// </summary>
    public class VAS_085_POFulfilmentWidgetController : Controller
    {
        /// <summary>
        /// KPI tile data: current overall PO fulfilment % and the point change
        /// versus last month.
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

            FulfilmentPeriods periods = GetFulfilmentPeriods(ctx);
            if (periods == null)
            {
                return Json(new
                {
                    error = Msg.GetMsg(ctx, "VAS_PeriodNotFound") ?? "No active financial period found"
                }, JsonRequestBehavior.AllowGet);
            }

            /* Per-CTE body: ordered + delivered totals over completed POs in a
               date window. The effective date prefers the line promised date,
               then the header promised date, then the order date. */
            string currentOverallSql = @"
                SELECT SUM(COALESCE(OrdLine.QtyOrdered, 0)) AS Ordered_Qty,
                       SUM(COALESCE(OrdLine.QtyDelivered, 0)) AS Delivered_Qty
                FROM C_Order Ord
                INNER JOIN C_OrderLine OrdLine ON (OrdLine.C_Order_ID=Ord.C_Order_ID)
                WHERE Ord.IsActive='Y'
                  AND OrdLine.IsActive='Y'
                  AND Ord.IsSOTrx='N'
                  AND Ord.DocStatus='CO'
                  AND COALESCE(OrdLine.DatePromised, Ord.DatePromised, Ord.DateOrdered) >= @YearStart
                  AND COALESCE(OrdLine.DatePromised, Ord.DatePromised, Ord.DateOrdered) < @CurrentPeriodEndNext";

            currentOverallSql = MRole.GetDefault(ctx).AddAccessSQL(
                currentOverallSql, "Ord", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string lastMonthSql = @"
                SELECT SUM(COALESCE(OrdLine.QtyOrdered, 0)) AS Ordered_Qty,
                       SUM(COALESCE(OrdLine.QtyDelivered, 0)) AS Delivered_Qty
                FROM C_Order Ord
                INNER JOIN C_OrderLine OrdLine ON (OrdLine.C_Order_ID=Ord.C_Order_ID)
                WHERE Ord.IsActive='Y'
                  AND OrdLine.IsActive='Y'
                  AND Ord.IsSOTrx='N'
                  AND Ord.DocStatus='CO'
                  AND COALESCE(OrdLine.DatePromised, Ord.DatePromised, Ord.DateOrdered) >= @PreviousPeriodStart
                  AND COALESCE(OrdLine.DatePromised, Ord.DatePromised, Ord.DateOrdered) < @PreviousPeriodEndNext";

            lastMonthSql = MRole.GetDefault(ctx).AddAccessSQL(
                lastMonthSql, "Ord", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string sql = @"
                WITH CurrentOverall AS (
                    " + currentOverallSql + @"
                ),
                LastMonth AS (
                    " + lastMonthSql + @"
                ),
                Calculated AS (
                    SELECT CASE WHEN COALESCE(CurrentOverall.Ordered_Qty, 0)=0 THEN 0
                                ELSE ROUND((COALESCE(CurrentOverall.Delivered_Qty, 0)*100)/CurrentOverall.Ordered_Qty, 0)
                           END AS Current_Fulfillment_Percent,
                           CASE WHEN COALESCE(LastMonth.Ordered_Qty, 0)=0 THEN 0
                                ELSE ROUND((COALESCE(LastMonth.Delivered_Qty, 0)*100)/LastMonth.Ordered_Qty, 0)
                           END AS Last_Month_Fulfillment_Percent
                    FROM CurrentOverall
                    CROSS JOIN LastMonth
                )
                SELECT Calculated.Current_Fulfillment_Percent,
                       Calculated.Current_Fulfillment_Percent - Calculated.Last_Month_Fulfillment_Percent AS Fulfillment_Change_Percent
                FROM Calculated";

            SqlParameter[] parameters =
            {
                new SqlParameter("@YearStart", periods.YearStart),
                new SqlParameter("@CurrentPeriodEndNext", periods.CurrentEndNext),
                new SqlParameter("@PreviousPeriodStart", periods.PreviousStart),
                new SqlParameter("@PreviousPeriodEndNext", periods.PreviousEndNext)
            };

            IDataReader dr = null;

            try
            {
                int currentPercent = 0;
                int changePercent = 0;

                dr = DB.ExecuteReader(sql, parameters);
                if (dr != null && dr.Read())
                {
                    currentPercent = Util.GetValueOfInt(dr["Current_Fulfillment_Percent"]);
                    changePercent = Util.GetValueOfInt(dr["Fulfillment_Change_Percent"]);
                }

                var result = new
                {
                    currentPercent = currentPercent,
                    changePercent = changePercent
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

        private static FulfilmentPeriods GetFulfilmentPeriods(Ctx ctx)
        {
            string sql = @"
                WITH CurrentPeriod AS (
                    SELECT CalPeriod.C_Year_ID,
                           CalPeriod.StartDate,
                           CalPeriod.EndDate,
                           CalYear.C_Calendar_ID,
                           ROW_NUMBER() OVER (
                               ORDER BY CalPeriod.StartDate DESC,
                                        CalPeriod.C_Period_ID DESC
                           ) AS SeqNo
                    FROM AD_ClientInfo ClientInfo
                    INNER JOIN C_Year CalYear ON (CalYear.C_Calendar_ID = ClientInfo.C_Calendar_ID)
                    INNER JOIN C_Period CalPeriod ON (CalPeriod.C_Year_ID = CalYear.C_Year_ID)
                    WHERE ClientInfo.IsActive = 'Y'
                      AND CalYear.IsActive = 'Y'
                      AND CalPeriod.IsActive = 'Y'
                      AND ClientInfo.AD_Client_ID = @AD_Client_ID
                      AND CURRENT_DATE BETWEEN CalPeriod.StartDate AND CalPeriod.EndDate
                ),
                YearBounds AS (
                    SELECT MIN(YearPeriod.StartDate) AS Year_StartDate
                    FROM CurrentPeriod CurPeriod
                    INNER JOIN C_Period YearPeriod ON (YearPeriod.C_Year_ID = CurPeriod.C_Year_ID)
                    WHERE CurPeriod.SeqNo = 1
                      AND YearPeriod.IsActive = 'Y'
                ),
                PreviousPeriod AS (
                    SELECT CalPeriod.StartDate,
                           CalPeriod.EndDate,
                           ROW_NUMBER() OVER (
                               ORDER BY CalPeriod.EndDate DESC,
                                        CalPeriod.StartDate DESC,
                                        CalPeriod.C_Period_ID DESC
                           ) AS SeqNo
                    FROM CurrentPeriod CurPeriod
                    INNER JOIN C_Year CalYear ON (CalYear.C_Calendar_ID = CurPeriod.C_Calendar_ID)
                    INNER JOIN C_Period CalPeriod ON (CalPeriod.C_Year_ID = CalYear.C_Year_ID)
                    WHERE CurPeriod.SeqNo = 1
                      AND CalYear.IsActive = 'Y'
                      AND CalPeriod.IsActive = 'Y'
                      AND CalPeriod.EndDate < CurPeriod.StartDate
                )
                SELECT YearBounds.Year_StartDate,
                       CurrentPeriod.StartDate AS Current_StartDate,
                       CurrentPeriod.EndDate AS Current_EndDate,
                       PreviousPeriod.StartDate AS Previous_StartDate,
                       PreviousPeriod.EndDate AS Previous_EndDate
                FROM CurrentPeriod
                CROSS JOIN YearBounds
                LEFT OUTER JOIN PreviousPeriod ON (PreviousPeriod.SeqNo = 1)
                WHERE CurrentPeriod.SeqNo = 1";

            SqlParameter[] parameters = { new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()) };
            IDataReader dr = null;

            try
            {
                dr = DB.ExecuteReader(sql, parameters);
                if (dr == null || !dr.Read())
                {
                    return null;
                }

                DateTime? yearStart = Util.GetValueOfDateTime(dr["Year_StartDate"]);
                DateTime? currentStart = Util.GetValueOfDateTime(dr["Current_StartDate"]);
                DateTime? currentEnd = Util.GetValueOfDateTime(dr["Current_EndDate"]);
                DateTime? previousStart = Util.GetValueOfDateTime(dr["Previous_StartDate"]);
                DateTime? previousEnd = Util.GetValueOfDateTime(dr["Previous_EndDate"]);

                if (!yearStart.HasValue || !currentStart.HasValue || !currentEnd.HasValue)
                {
                    return null;
                }

                DateTime currentStartDate = currentStart.Value.Date;

                return new FulfilmentPeriods
                {
                    YearStart = yearStart.Value.Date,
                    CurrentEndNext = currentEnd.Value.Date.AddDays(1),
                    PreviousStart = previousStart.HasValue ? previousStart.Value.Date : currentStartDate,
                    PreviousEndNext = previousEnd.HasValue ? previousEnd.Value.Date.AddDays(1) : currentStartDate
                };
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

        private class FulfilmentPeriods
        {
            public DateTime YearStart { get; set; }
            public DateTime CurrentEndNext { get; set; }
            public DateTime PreviousStart { get; set; }
            public DateTime PreviousEndNext { get; set; }
        }
    }
}
