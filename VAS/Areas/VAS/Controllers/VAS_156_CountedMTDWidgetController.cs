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
    /// Module Name : Counted MTD Widget (Inventory Count / Physical Inventory dashboard KPI)
    /// Purpose     : Read-only 2x1 summary card showing total count lines completed month-to-date
    ///               (M_Inventory, M_InventoryLine) and distinct products counted.
    /// Prefix      : VAS_000_
    /// </summary>
    public class VAS_156_CountedMTDWidgetController : Controller
    {
        /// <summary>
        /// Gets month-to-date counted lines and distinct products count for the active context.
        /// </summary>
        /// <returns>JSON { countedLines, productsCounted }</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetCountedMTDData()
        {
            if (Session["ctx"] == null)
            {
                return Json(new
                {
                    error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired"
                }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            DateTime now = DateTime.Now;
            DateTime monthStart = new DateTime(now.Year, now.Month, 1);
            DateTime nextMonthStart = monthStart.AddMonths(1);

            string sql = @"
                SELECT
                    COUNT(*) AS counted_lines,
                    COUNT(DISTINCT il.M_Product_ID) AS products_counted
                FROM M_Inventory i
                JOIN M_InventoryLine il
                    ON il.M_Inventory_ID = i.M_Inventory_ID
                WHERE i.IsActive = 'Y'
                  AND il.IsActive = 'Y'
                  AND i.IsInternalUse = 'N'
                  AND i.DocStatus IN ('CO', 'CL')
                  AND i.MovementDate >= @MonthStart
                  AND i.MovementDate < @NextMonthStart";

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
                int countedLines = 0;
                int productsCounted = 0;

                dr = DB.ExecuteReader(sql, parameters);
                if (dr != null && dr.Read())
                {
                    countedLines = Util.GetValueOfInt(dr["counted_lines"]);
                    productsCounted = Util.GetValueOfInt(dr["products_counted"]);
                }

                var result = new
                {
                    countedLines = countedLines,
                    productsCounted = productsCounted
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
