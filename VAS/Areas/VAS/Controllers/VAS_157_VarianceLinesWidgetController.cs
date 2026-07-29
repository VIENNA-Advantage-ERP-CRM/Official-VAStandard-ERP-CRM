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
    /// Module Name : Variance Lines KPI Widget (Inventory Count / Physical Inventory Dashboard)
    /// Purpose     : Read-only 2x1 glass KPI card showing current month-to-date count lines
    ///               whose difference quantity exceeds product accepted tolerance, plus percentage of total counted lines.
    /// Prefix      : VAS_000_
    /// </summary>
    public class VAS_157_VarianceLinesWidgetController : Controller
    {
        /// <summary>
        /// Gets current month-to-date variance lines and total counted lines.
        /// </summary>
        /// <returns>JSON { varianceLines, countedLines }</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetVarianceLinesData()
        {
            if (Session["ctx"] == null)
            {
                return Json(new
                {
                    error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired"
                }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            // Calculate current calendar month MTD date window in application context
            DateTime now = DateTime.Now;
            DateTime monthStart = new DateTime(now.Year, now.Month, 1);
            DateTime nextMonthStart = monthStart.AddMonths(1);

            string sql = @"
                SELECT
                    COALESCE(
                        SUM(
                            CASE
                                WHEN (
                                    il.QtyBook = 0
                                    AND il.DifferenceQty <> 0
                                )
                                OR (
                                    il.QtyBook <> 0
                                    AND ABS(il.DifferenceQty) >
                                        ABS(il.QtyBook) * COALESCE(p.QtyTolerance, 0) / 100
                                )
                                THEN 1
                                ELSE 0
                            END
                        ),
                        0
                    ) AS variance_lines,
                    COUNT(*) AS counted_lines
                FROM M_Inventory i
                JOIN M_InventoryLine il
                    ON il.M_Inventory_ID = i.M_Inventory_ID
                JOIN M_Product p
                    ON p.M_Product_ID = il.M_Product_ID
                WHERE i.IsActive = 'Y'
                  AND il.IsActive = 'Y'
                  AND i.IsInternalUse = 'N'
                  AND i.DocStatus IN ('CO', 'CL')
                  AND i.MovementDate >= @MonthStart
                  AND i.MovementDate < @NextMonthStart";

            // Role-based tenant & org security filter
            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql,
                "i",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            SqlParameter[] parameters =
            {
                new SqlParameter("@MonthStart", monthStart),
                new SqlParameter("@NextMonthStart", nextMonthStart)
            };

            IDataReader dr = null;

            try
            {
                int varianceLines = 0;
                int countedLines = 0;

                dr = DB.ExecuteReader(sql, parameters);
                if (dr != null && dr.Read())
                {
                    varianceLines = Util.GetValueOfInt(dr["variance_lines"]);
                    countedLines = Util.GetValueOfInt(dr["counted_lines"]);
                }

                var result = new
                {
                    varianceLines = varianceLines,
                    countedLines = countedLines
                };

                return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                VAdvantage.Logging.VLogger.Get().Severe("Error fetching Variance Lines data: " + ex.Message);
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
