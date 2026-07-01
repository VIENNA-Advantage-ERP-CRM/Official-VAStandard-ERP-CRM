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
    /// Module Name : Receipts MTD (Material Receipt / GRN dashboard KPI)
    /// Purpose     : KPI = COUNT of vendor material receipts (M_InOut, IsSOTrx 'N',
    ///               MovementType 'V+') for the current month-to-date, with the
    ///               prior-month count for comparison. Reversed/Voided (RE, VO)
    ///               documents are excluded. Read-only, header-level count (never
    ///               joins M_InOutLine). Tenant/organization scope comes from
    ///               MRole, applied to the single physical table M_InOut.
    /// Chronological development:
    ///   &lt;EmpCode&gt;   2026-06-18 Created
    /// </summary>
    public class VAS_089_ReceiptsMTDWidgetController : Controller
    {
        /// <summary>
        /// KPI tile data: current-month and previous-month vendor receipt counts.
        /// </summary>
        /// <returns>JSON { receiptsMtd, receiptsPrevMonth }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetReceiptsMTD()
        {
            if (Session["ctx"] == null)
            {
                return Json(new
                {
                    error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired"
                }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            PeriodBounds bounds = GetPeriodBounds(ctx);
            if (bounds == null)
            {
                return Json(new
                {
                    error = Msg.GetMsg(ctx, "VAS_PeriodNotFound") ?? "No active financial period found"
                }, JsonRequestBehavior.AllowGet);
            }

            string sql = @"
                SELECT SUM(CASE
                           WHEN MInOut.MovementDate >= @CurrentPeriodStart
                            AND MInOut.MovementDate < @CurrentPeriodEndNext
                           THEN 1 ELSE 0 END) AS Receipts_MTD,
                       SUM(CASE
                           WHEN MInOut.MovementDate >= @PreviousPeriodStart
                            AND MInOut.MovementDate < @PreviousPeriodEndNext
                           THEN 1 ELSE 0 END) AS Receipts_Prev_Month
                FROM M_InOut MInOut
                WHERE MInOut.IsActive = 'Y'
                  AND MInOut.IsSOTrx = 'N'
                  AND MInOut.MovementType = 'V+'
                  AND MInOut.DocStatus NOT IN ('RE', 'VO')
                  AND MInOut.MovementDate >= @PreviousPeriodStart
                  AND MInOut.MovementDate < @CurrentPeriodEndNext";

            /* MRole supplies tenant + organization access on the only physical
               table; applied to the main table alias (shared notation/CTE rules). */
            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql,
                "MInOut",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            SqlParameter[] parameters =
            {
                new SqlParameter("@CurrentPeriodStart", bounds.CurrentStart),
                new SqlParameter("@CurrentPeriodEndNext", bounds.CurrentEndNext),
                new SqlParameter("@PreviousPeriodStart", bounds.PreviousStart),
                new SqlParameter("@PreviousPeriodEndNext", bounds.PreviousEndNext)
            };

            IDataReader dr = null;

            try
            {
                int receiptsMtd = 0;
                int receiptsPrevMonth = 0;

                dr = DB.ExecuteReader(sql, parameters);
                if (dr != null && dr.Read())
                {
                    receiptsMtd = Util.GetValueOfInt(dr["Receipts_MTD"]);
                    receiptsPrevMonth = Util.GetValueOfInt(dr["Receipts_Prev_Month"]);
                }

                var result = new
                {
                    receiptsMtd = receiptsMtd,
                    receiptsPrevMonth = receiptsPrevMonth
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

        private static PeriodBounds GetPeriodBounds(Ctx ctx)
        {
            string sql = @"
                WITH CurrentPeriod AS (
                    SELECT CalPeriod.StartDate,
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
                SELECT CurrentPeriod.StartDate AS Current_StartDate,
                       CurrentPeriod.EndDate AS Current_EndDate,
                       PreviousPeriod.StartDate AS Previous_StartDate,
                       PreviousPeriod.EndDate AS Previous_EndDate
                FROM CurrentPeriod
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

                DateTime? currentStart = Util.GetValueOfDateTime(dr["Current_StartDate"]);
                DateTime? currentEnd = Util.GetValueOfDateTime(dr["Current_EndDate"]);
                DateTime? previousStart = Util.GetValueOfDateTime(dr["Previous_StartDate"]);
                DateTime? previousEnd = Util.GetValueOfDateTime(dr["Previous_EndDate"]);

                if (!currentStart.HasValue || !currentEnd.HasValue)
                {
                    return null;
                }

                DateTime currentStartDate = currentStart.Value.Date;

                return new PeriodBounds
                {
                    CurrentStart = currentStartDate,
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

        private class PeriodBounds
        {
            public DateTime CurrentStart { get; set; }
            public DateTime CurrentEndNext { get; set; }
            public DateTime PreviousStart { get; set; }
            public DateTime PreviousEndNext { get; set; }
        }
    }
}
