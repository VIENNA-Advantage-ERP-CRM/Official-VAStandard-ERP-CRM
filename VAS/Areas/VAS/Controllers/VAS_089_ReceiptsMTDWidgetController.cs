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
    ///               MovementType 'V+') for the current calendar month-to-date, with the
    ///               previous calendar month's count for comparison. Reversed/Voided (RE, VO)
    ///               documents are excluded. Read-only, header-level count (never
    ///               joins M_InOutLine). Tenant/organization scope comes from
    ///               MRole, applied to the single physical table M_InOut.
    /// Chronological development:
    ///   &lt;EmpCode&gt;   2026-06-18 Created
    ///   2026-09-15 QA sheet GRN #3: calendar months instead of the financial calendar (C_Period);
    ///              one bind per placeholder occurrence, in order of appearance
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

            /* Month-to-date and the whole previous month come straight from the calendar. The
               financial calendar (C_Period) was used before: a tenant without periods got no figure
               at all ("No active financial period found"), and a period that is not a plain
               calendar month shifted the previous-month count away from the real monthly total. */
            DateTime today = DateTime.Today;
            DateTime currentStart = new DateTime(today.Year, today.Month, 1);
            DateTime currentEndNext = today.AddDays(1);
            DateTime previousStart = currentStart.AddMonths(-1);
            DateTime previousEndNext = currentStart;

            string sql = @"
                SELECT SUM(CASE
                           WHEN MInOut.MovementDate >= @CurrentStart
                            AND MInOut.MovementDate < @CurrentEndNext
                           THEN 1 ELSE 0 END) AS Receipts_MTD,
                       SUM(CASE
                           WHEN MInOut.MovementDate >= @PreviousStart
                            AND MInOut.MovementDate < @PreviousEndNext
                           THEN 1 ELSE 0 END) AS Receipts_Prev_Month
                FROM M_InOut MInOut
                WHERE MInOut.IsActive = 'Y'
                  AND MInOut.IsSOTrx = 'N'
                  AND MInOut.MovementType = 'V+'
                  AND MInOut.DocStatus NOT IN ('RE', 'VO')
                  AND MInOut.MovementDate >= @RangeStart
                  AND MInOut.MovementDate < @RangeEndNext";

            /* MRole supplies tenant + organization access on the only physical
               table; applied to the main table alias (shared notation/CTE rules). */
            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql,
                "MInOut",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            /* The database layer binds positionally: one parameter per placeholder occurrence,
               in the order the placeholders appear (the old statement bound 4 values to 6 markers). */
            SqlParameter[] parameters =
            {
                new SqlParameter("@CurrentStart", currentStart),
                new SqlParameter("@CurrentEndNext", currentEndNext),
                new SqlParameter("@PreviousStart", previousStart),
                new SqlParameter("@PreviousEndNext", previousEndNext),
                new SqlParameter("@RangeStart", previousStart),
                new SqlParameter("@RangeEndNext", currentEndNext)
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
    }
}
