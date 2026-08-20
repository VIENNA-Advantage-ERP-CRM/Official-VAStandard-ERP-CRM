using System.Data.SqlClient;
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
    /// Module Name : VAS_186_ProductCategoryUsageWidget
    /// Purpose     : Supplies category-wise consumption data (quantity and value) and category drill-down issue lines.
    /// Chronological development:
    ///   AI-Dev      2026-08-02 Created
    /// </summary>
    public class VAS_186_ProductCategoryUsageWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_186_ProductCategoryUsageWidgetController).FullName);

// ===== NEW CODE START — currency format (agent A08, 2026-08-19) =====
        /// <summary>Endpoint C: Gets organizational currency info (ISO code and symbol).</summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetCurrencyInfo()
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null) { return Json("", JsonRequestBehavior.AllowGet); }
            var currencyInfo = GetCurrencyInfoData(ctx);
            return Json(JsonConvert.SerializeObject(currencyInfo), JsonRequestBehavior.AllowGet);
        }

        private static object GetCurrencyInfoData(Ctx ctx)
        {
            string iso = "";
            string symbol = "";
            int currencyId = ctx.GetContextAsInt("$C_Currency_ID");
            if (currencyId == 0)
            {
                currencyId = ctx.GetContextAsInt("C_Currency_ID");
            }

            try
            {
                if (currencyId > 0)
                {
                    string sql = "SELECT ISO_Code, CurSymbol FROM C_Currency WHERE C_Currency_ID = @param1 AND IsActive = 'Y'";
                    SqlParameter[] param = new SqlParameter[]
                    {
                        new SqlParameter("@param1", currencyId)
                    };
                    using (IDataReader dr = DB.ExecuteReader(sql, param, null))
                    {
                        if (dr != null && dr.Read())
                        {
                            iso = Util.GetValueOfString(dr["ISO_Code"]);
                            symbol = Util.GetValueOfString(dr["CurSymbol"]);
                        }
                    }
                }

                if (string.IsNullOrEmpty(iso))
                {
                    int clientId = ctx.GetAD_Client_ID();
                    string sqlFallback = @"SELECT c.ISO_Code, c.CurSymbol 
                                           FROM C_AcctSchema a 
                                           INNER JOIN C_Currency c ON c.C_Currency_ID = a.C_Currency_ID 
                                           WHERE a.AD_Client_ID = @param1 AND a.IsActive = 'Y' 
                                           ORDER BY a.C_AcctSchema_ID";
                    SqlParameter[] paramFallback = new SqlParameter[]
                    {
                        new SqlParameter("@param1", clientId)
                    };
                    using (IDataReader dr = DB.ExecuteReader(sqlFallback, paramFallback, null))
                    {
                        if (dr != null && dr.Read())
                        {
                            iso = Util.GetValueOfString(dr["ISO_Code"]);
                            symbol = Util.GetValueOfString(dr["CurSymbol"]);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_186_ProductCategoryUsageWidget.GetCurrencyInfoData", ex);
            }

            return new { iso = iso, symbol = symbol };
        }

        /// <summary>Endpoint A: Category usage aggregates for selected month and year.</summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetCategoryUsage(int month, int year)
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null) { return Json("", JsonRequestBehavior.AllowGet); }

            var categories = new List<object>();
            DateTime monthStart = new DateTime(year > 0 ? year : DateTime.Now.Year, month > 0 ? month : DateTime.Now.Month, 1);
            var currencyInfo = GetCurrencyInfoData(ctx);

            try
            {
                DateTime nextMonthStart = monthStart.AddMonths(1);
                string msl = ToSqlDate(monthStart);
                string nmsl = ToSqlDate(nextMonthStart);

                // AddAccessSQL appends its predicate at the end of the statement, so it must be
                // applied to a plain SELECT (no GROUP BY / ORDER BY) where the alias is in scope.
                // Applying it to the aggregate query instead yields ORA-00933.
                string invAccessSql = BuildAccessibleInventorySql(ctx, msl, nmsl);

                string sql = @"
                    SELECT
                      pc.M_Product_Category_ID,
                      pc.Name AS CategoryName,
                      SUM(line.QtyInternalUse) AS TotalQty,
                      SUM(line.QtyInternalUse * COALESCE(line.CurrentCostPrice, line.PriceCost, line.VA024_CostPrice, 0)) AS TotalValue
                    FROM M_InventoryLine line
                    INNER JOIN (" + invAccessSql + @") ai ON ai.M_Inventory_ID = line.M_Inventory_ID
                    INNER JOIN M_Product p ON p.M_Product_ID = line.M_Product_ID
                    INNER JOIN M_Product_Category pc ON pc.M_Product_Category_ID = p.M_Product_Category_ID
                    WHERE line.IsActive = 'Y'
                      AND COALESCE(line.QtyInternalUse, 0) > 0
                    GROUP BY pc.M_Product_Category_ID, pc.Name
                    ORDER BY SUM(line.QtyInternalUse) DESC";

                using (IDataReader dr = DB.ExecuteReader(sql, null, null))
                {
                    while (dr != null && dr.Read())
                    {
                        categories.Add(new
                        {
                            categoryId = Util.GetValueOfInt(dr["M_Product_Category_ID"]),
                            categoryName = Util.GetValueOfString(dr["CategoryName"]),
                            totalQty = Util.GetValueOfDecimal(dr["TotalQty"]),
                            totalValue = Util.GetValueOfDecimal(dr["TotalValue"])
                        });
                    }
                }

            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_186_ProductCategoryUsageWidget.GetCategoryUsage", ex);
                return Json(JsonConvert.SerializeObject(new { error = Msg.GetMsg(ctx, "Error") ?? "Error" }), JsonRequestBehavior.AllowGet);
            }
            return Json(JsonConvert.SerializeObject(new { categories = categories, currency = currencyInfo, success = true }), JsonRequestBehavior.AllowGet);
        }
// ===== NEW CODE END — currency format =====
// ----- OLD CODE (kept for rollback, do not delete) -----
//        /// <summary>Endpoint A: Category usage aggregates for selected month and year.</summary>
//        [AjaxAuthorizeAttribute]
//        [AjaxSessionFilterAttribute]
//        public JsonResult GetCategoryUsage(int month, int year)
//        {
//            Ctx ctx = Session["ctx"] as Ctx;
//            if (ctx == null) { return Json("", JsonRequestBehavior.AllowGet); }
//
//            var categories = new List<object>();
//            DateTime monthStart = new DateTime(year > 0 ? year : DateTime.Now.Year, month > 0 ? month : DateTime.Now.Month, 1);
//
//            try
//            {
//                DateTime nextMonthStart = monthStart.AddMonths(1);
//                string msl = ToSqlDate(monthStart);
//                string nmsl = ToSqlDate(nextMonthStart);
//
//                // AddAccessSQL appends its predicate at the end of the statement, so it must be
//                // applied to a plain SELECT (no GROUP BY / ORDER BY) where the alias is in scope.
//                // Applying it to the aggregate query instead yields ORA-00933.
//                string invAccessSql = BuildAccessibleInventorySql(ctx, msl, nmsl);
//
//                string sql = @"
//                    SELECT
//                      pc.M_Product_Category_ID,
//                      pc.Name AS CategoryName,
//                      SUM(line.QtyInternalUse) AS TotalQty,
//                      SUM(line.QtyInternalUse * COALESCE(line.CurrentCostPrice, line.PriceCost, line.VA024_CostPrice, 0)) AS TotalValue
//                    FROM M_InventoryLine line
//                    INNER JOIN (" + invAccessSql + @") ai ON ai.M_Inventory_ID = line.M_Inventory_ID
//                    INNER JOIN M_Product p ON p.M_Product_ID = line.M_Product_ID
//                    INNER JOIN M_Product_Category pc ON pc.M_Product_Category_ID = p.M_Product_Category_ID
//                    WHERE line.IsActive = 'Y'
//                      AND COALESCE(line.QtyInternalUse, 0) > 0
//                    GROUP BY pc.M_Product_Category_ID, pc.Name
//                    ORDER BY SUM(line.QtyInternalUse) DESC";
//
//                using (IDataReader dr = DB.ExecuteReader(sql, null, null))
//                {
//                    while (dr != null && dr.Read())
//                    {
//                        categories.Add(new
//                        {
//                            categoryId = Util.GetValueOfInt(dr["M_Product_Category_ID"]),
//                            categoryName = Util.GetValueOfString(dr["CategoryName"]),
//                            totalQty = Util.GetValueOfDecimal(dr["TotalQty"]),
//                            totalValue = Util.GetValueOfDecimal(dr["TotalValue"])
//                        });
//                    }
//                }
//
//            }
//            catch (Exception ex)
//            {
//                Log.Log(Level.SEVERE, "VAS_186_ProductCategoryUsageWidget.GetCategoryUsage", ex);
//                return Json(JsonConvert.SerializeObject(new { error = Msg.GetMsg(ctx, "Error") ?? "Error" }), JsonRequestBehavior.AllowGet);
//            }
//            return Json(JsonConvert.SerializeObject(new { categories = categories, success = true }), JsonRequestBehavior.AllowGet);
//        }
// ----- END OLD CODE -----

        /// <summary>Endpoint B: Individual issue lines for a specific category in selected period.</summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetCategoryIssueLines(int categoryId, int month, int year)
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null) { return Json("", JsonRequestBehavior.AllowGet); }

            DateTime monthStart = new DateTime(year > 0 ? year : DateTime.Now.Year, month > 0 ? month : DateTime.Now.Month, 1);
            var lines = new List<object>();

            try
            {
                DateTime nextMonthStart = monthStart.AddMonths(1);
                string msl = ToSqlDate(monthStart);
                string nmsl = ToSqlDate(nextMonthStart);

                string invAccessSql = BuildAccessibleInventorySql(ctx, msl, nmsl);

                string sql = @"
                    SELECT
                      ai.DocumentNo,
                      p.Name AS ProductName,
                      asi.Description AS Attribute,
                      uom.Name AS UomName,
                      wh.Name AS WarehouseName,
                      loc.Value AS LocatorCode,
                      line.QtyInternalUse,
                      ai.MovementDate
                    FROM M_InventoryLine line
                    INNER JOIN (" + invAccessSql + @") ai ON ai.M_Inventory_ID = line.M_Inventory_ID
                    INNER JOIN M_Product p ON p.M_Product_ID = line.M_Product_ID
                    LEFT JOIN C_UOM uom ON uom.C_UOM_ID = line.C_UOM_ID
                    LEFT JOIN M_AttributeSetInstance asi ON asi.M_AttributeSetInstance_ID = line.M_AttributeSetInstance_ID
                    LEFT JOIN M_Locator loc ON loc.M_Locator_ID = line.M_Locator_ID
                    LEFT JOIN M_Warehouse wh ON wh.M_Warehouse_ID = loc.M_Warehouse_ID
                    WHERE line.IsActive = 'Y'
                      AND COALESCE(line.QtyInternalUse, 0) > 0
                      AND p.M_Product_Category_ID = " + categoryId + @"
                    ORDER BY ai.MovementDate DESC, ai.DocumentNo DESC";

                using (IDataReader dr = DB.ExecuteReader(sql, null, null))
                {
                    while (dr != null && dr.Read())
                    {
                        lines.Add(new
                        {
                            documentNo = Util.GetValueOfString(dr["DocumentNo"]),
                            productName = Util.GetValueOfString(dr["ProductName"]),
                            attribute = Util.GetValueOfString(dr["Attribute"]),
                            uomName = Util.GetValueOfString(dr["UomName"]),
                            whLoc = Util.GetValueOfString(dr["WarehouseName"]) + " / " + Util.GetValueOfString(dr["LocatorCode"]),
                            qty = Util.GetValueOfDecimal(dr["QtyInternalUse"]),
                            movementDate = Convert.ToDateTime(dr["MovementDate"]).ToString("dd MMM")
                        });
                    }
                }

            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_186_ProductCategoryUsageWidget.GetCategoryIssueLines", ex);
                return Json(JsonConvert.SerializeObject(new { error = Msg.GetMsg(ctx, "Error") ?? "Error" }), JsonRequestBehavior.AllowGet);
            }
            return Json(JsonConvert.SerializeObject(new { lines = lines, success = true }), JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Role-filtered internal-use inventory headers for the period. Kept as a plain SELECT with
        /// no GROUP BY / ORDER BY: AddAccessSQL appends its predicate to the end of the statement,
        /// so it is only safe on a query whose alias is still in scope there.
        /// </summary>
        private static string BuildAccessibleInventorySql(Ctx ctx, string periodStart, string periodEnd)
        {
            string sql = @"
                    SELECT inv.M_Inventory_ID, inv.DocumentNo, inv.MovementDate
                    FROM M_Inventory inv
                    WHERE inv.IsActive = 'Y'
                      AND inv.DocStatus IN ('CO', 'CL')
                      AND COALESCE(inv.IsInternalUse, 'N') = 'Y'
                      AND inv.MovementDate >= " + periodStart + @"
                      AND inv.MovementDate < " + periodEnd;

            return MRole.GetDefault(ctx).AddAccessSQL(sql, "inv", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
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

