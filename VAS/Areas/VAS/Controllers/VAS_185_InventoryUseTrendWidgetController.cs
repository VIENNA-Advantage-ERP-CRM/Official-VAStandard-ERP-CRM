using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Web.Mvc;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VIS.Controllers
{
    /// <summary>
    /// Module Name : VAS_185_InventoryUseTrendWidget
    /// Purpose     : Supplies monthly quantity, value, and document count buckets for 3M/6M/12M trend analysis.
    /// Chronological development:
    ///   AI-Dev      2026-08-02 Created
    /// </summary>
    public class VAS_185_InventoryUseTrendWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_185_InventoryUseTrendWidgetController).FullName);

        private class MonthBucket
        {
            public decimal qty;
            public decimal val;
            public int docs;
        }

        /// <summary>Returns monthly trend series for the specified rolling window (3, 6, or 12 months).</summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetTrendData(int months)
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null) { return Json("", JsonRequestBehavior.AllowGet); }

            int windowMonths = (months == 3 || months == 12) ? months : 6;
            DateTime now = DateTime.Now;
            DateTime endMonthStart = new DateTime(now.Year, now.Month, 1).AddMonths(1);
            DateTime startMonthStart = new DateTime(now.Year, now.Month, 1).AddMonths(-(windowMonths - 1));
            var dbDict = new Dictionary<string, MonthBucket>();

            try
            {
                string smsl = ToSqlDate(startMonthStart);
                string emsl = ToSqlDate(endMonthStart);

                // AddAccessSQL appends its predicate at the end of the statement, so it must be
                // applied to a plain SELECT (no GROUP BY / ORDER BY) where the alias is in scope.
                string invAccessSql = @"
                    SELECT inv.M_Inventory_ID, inv.MovementDate
                    FROM M_Inventory inv
                    WHERE inv.IsActive = 'Y'
                      AND inv.DocStatus IN ('CO', 'CL')
                      AND COALESCE(inv.IsInternalUse, 'N') = 'Y'
                      AND inv.MovementDate >= " + smsl + @"
                      AND inv.MovementDate < " + emsl;

                invAccessSql = MRole.GetDefault(ctx).AddAccessSQL(invAccessSql, "inv", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                string sql = @"
                    SELECT
                      TO_CHAR(ai.MovementDate, 'YYYY-MM') AS MonthBucket,
                      SUM(line.QtyInternalUse) AS TotalQty,
                      SUM(line.QtyInternalUse * COALESCE(line.CurrentCostPrice, line.PriceCost, line.VA024_CostPrice, 0)) AS TotalValue,
                      COUNT(DISTINCT ai.M_Inventory_ID) AS DocCount
                    FROM M_InventoryLine line
                    INNER JOIN (" + invAccessSql + @") ai ON ai.M_Inventory_ID = line.M_Inventory_ID
                    WHERE line.IsActive = 'Y'
                      AND COALESCE(line.QtyInternalUse, 0) > 0
                    GROUP BY TO_CHAR(ai.MovementDate, 'YYYY-MM')
                    ORDER BY MonthBucket ASC";

                using (IDataReader dr = DB.ExecuteReader(sql, null, null))
                {
                    while (dr != null && dr.Read())
                    {
                        string bucket = Util.GetValueOfString(dr["MonthBucket"]);
                        dbDict[bucket] = new MonthBucket
                        {
                            qty = Util.GetValueOfDecimal(dr["TotalQty"]),
                            val = Util.GetValueOfDecimal(dr["TotalValue"]),
                            docs = Util.GetValueOfInt(dr["DocCount"])
                        };
                    }
                }

            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_185_InventoryUseTrendWidget.GetTrendData", ex);
                return Json(JsonConvert.SerializeObject(new { error = Msg.GetMsg(ctx, "Error") ?? "Error" }), JsonRequestBehavior.AllowGet);
            }

            {
                var series = new List<object>();
                string[] monthNames = new string[] { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };

                for (int i = 0; i < windowMonths; i++)
                {
                    DateTime dt = startMonthStart.AddMonths(i);
                    string key = dt.ToString("yyyy-MM");
                    string labelName = monthNames[dt.Month - 1] + (windowMonths == 12 ? (" '" + dt.ToString("yy")) : "");

                    decimal qty = 0;
                    decimal val = 0;
                    int docs = 0;

                    if (dbDict.ContainsKey(key))
                    {
                        qty = dbDict[key].qty;
                        val = dbDict[key].val;
                        docs = dbDict[key].docs;
                    }

                    series.Add(new
                    {
                        key = key,
                        label = labelName,
                        fullMonth = monthNames[dt.Month - 1] + " " + dt.Year,
                        qty = qty,
                        val = val,
                        docs = docs
                    });
                }

                return Json(JsonConvert.SerializeObject(new { series = series, success = true }), JsonRequestBehavior.AllowGet);
            }
        }

        private static string ToSqlDate(DateTime date)
        {
            if (DB.IsOracle())
            {
                return "TO_DATE('" + date.ToString("yyyy-MM-dd") + "', 'YYYY-MM-DD')";
            }
            return "CAST('" + date.ToString("yyyy-MM-dd") + "' AS DATE)";
        }
    }
}
