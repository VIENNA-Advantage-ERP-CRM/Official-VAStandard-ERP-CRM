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
    /// Module Name : VAS_184_HighValueUsageWidget
    /// Purpose     : Supplies top 10 high-value consumed products and their issue history modal data.
    /// Chronological development:
    ///   AI-Dev      2026-08-02 Created
    /// </summary>
    public class VAS_184_HighValueUsageWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_184_HighValueUsageWidgetController).FullName);

// ===== NEW CODE START — currency format (agent A06, 2026-08-19) =====
        private object GetCurrencyInfo(Ctx ctx)
        {
            string iso = "";
            string symbol = "";
            int stdPrecision = 2;

            if (ctx != null)
            {
                int currencyId = ctx.GetContextAsInt("$C_Currency_ID");
                if (currencyId > 0)
                {
                    string sql = "SELECT ISO_Code, CurSymbol, StdPrecision FROM C_Currency WHERE C_Currency_ID = @Cur";
                    SqlParameter[] param = new SqlParameter[] { new SqlParameter("@Cur", currencyId) };
                    using (IDataReader dr = DB.ExecuteReader(sql, param, null))
                    {
                        if (dr != null && dr.Read())
                        {
                            iso = Util.GetValueOfString(dr["ISO_Code"]);
                            symbol = Util.GetValueOfString(dr["CurSymbol"]);
                            stdPrecision = Util.GetValueOfInt(dr["StdPrecision"]);
                        }
                    }
                }
                if (string.IsNullOrEmpty(iso))
                {
                    string sql = @"SELECT c.ISO_Code, c.CurSymbol, c.StdPrecision 
                                   FROM C_AcctSchema acs 
                                   JOIN C_Currency c ON c.C_Currency_ID = acs.C_Currency_ID 
                                   WHERE acs.AD_Client_ID = @Client AND acs.IsActive='Y'";
                    SqlParameter[] param = new SqlParameter[] { new SqlParameter("@Client", ctx.GetAD_Client_ID()) };
                    using (IDataReader dr = DB.ExecuteReader(sql, param, null))
                    {
                        if (dr != null && dr.Read())
                        {
                            iso = Util.GetValueOfString(dr["ISO_Code"]);
                            symbol = Util.GetValueOfString(dr["CurSymbol"]);
                            stdPrecision = Util.GetValueOfInt(dr["StdPrecision"]);
                        }
                    }
                }
            }
            if (string.IsNullOrEmpty(symbol))
            {
                symbol = iso;
            }
            return new { iso = iso, symbol = symbol, stdPrecision = stdPrecision };
        }
// ===== NEW CODE END — currency format =====

        /// <summary>Endpoint A: Top 10 high-value products consumed in selected month and year.</summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetHighValueProducts(int month, int year)
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null) { return Json("", JsonRequestBehavior.AllowGet); }

            var rows = new List<object>();
            DateTime monthStart = new DateTime(year > 0 ? year : DateTime.Now.Year, month > 0 ? month : DateTime.Now.Month, 1);

            try
            {
                DateTime nextMonthStart = monthStart.AddMonths(1);
                string msl = ToSqlDate(monthStart);
                string nmsl = ToSqlDate(nextMonthStart);

                // Role access is applied to the inner header SELECT; applying it to this wrapped
                // aggregate would append the predicate outside the subquery (ORA-00907).
                string invAccessSql = BuildAccessibleInventorySql(ctx, msl, nmsl);

                // Cost is line.CurrentCostPrice ONLY. The previous
                // COALESCE(CurrentCostPrice, PriceCost, VA024_CostPrice, 0) fallback chain silently
                // substituted a different price whenever the line carried no current cost, and the
                // final ", 0" turned a line with no price at all into a zero-value row that still
                // occupied one of the ten slots. Lines with no CurrentCostPrice are now excluded
                // outright, which is also what keeps zero-value products off the widget:
                // CurrentCostPrice > 0 is false for NULL as well as for 0.
                string sql = @"
                    SELECT * FROM (
                      SELECT
                        p.M_Product_ID,
                        p.Name AS ProductName,
                        asi.Description AS Attribute,
                        uom.Name AS UomName,
                        MAX(line.CurrentCostPrice) AS CostPrice,
                        SUM(line.QtyInternalUse) AS TotalIssuedQty,
                        SUM(line.QtyInternalUse * line.CurrentCostPrice) AS TotalIssuedValue
                      FROM M_InventoryLine line
                      INNER JOIN (" + invAccessSql + @") ai ON ai.M_Inventory_ID = line.M_Inventory_ID
                      INNER JOIN M_Product p ON p.M_Product_ID = line.M_Product_ID
                      LEFT JOIN C_UOM uom ON uom.C_UOM_ID = line.C_UOM_ID
                      LEFT JOIN M_AttributeSetInstance asi ON asi.M_AttributeSetInstance_ID = line.M_AttributeSetInstance_ID
                      WHERE line.IsActive = 'Y'
                        AND COALESCE(line.QtyInternalUse, 0) > 0
                        AND line.CurrentCostPrice > 0
                      GROUP BY p.M_Product_ID, p.Name, asi.Description, uom.Name
                      HAVING SUM(line.QtyInternalUse * line.CurrentCostPrice) > 0
                      ORDER BY MAX(line.CurrentCostPrice) DESC,
                               SUM(line.QtyInternalUse * line.CurrentCostPrice) DESC
                    ) WHERE ROWNUM <= 10";

                using (IDataReader dr = DB.ExecuteReader(sql, null, null))
                {
                    while (dr != null && dr.Read())
                    {
                        rows.Add(new
                        {
                            productId = Util.GetValueOfInt(dr["M_Product_ID"]),
                            productName = Util.GetValueOfString(dr["ProductName"]),
                            attribute = Util.GetValueOfString(dr["Attribute"]),
                            uomName = Util.GetValueOfString(dr["UomName"]),
                            costPrice = Util.GetValueOfDecimal(dr["CostPrice"]),
                            issuedQty = Util.GetValueOfDecimal(dr["TotalIssuedQty"]),
                            issuedValue = Util.GetValueOfDecimal(dr["TotalIssuedValue"])
                        });
                    }
                }

            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_184_HighValueUsageWidget.GetHighValueProducts", ex);
                return Json(JsonConvert.SerializeObject(new { error = Msg.GetMsg(ctx, "Error") ?? "Error" }), JsonRequestBehavior.AllowGet);
            }
// ===== NEW CODE START — currency format (agent A06, 2026-08-19) =====
            return Json(JsonConvert.SerializeObject(new { products = rows, currency = GetCurrencyInfo(ctx), success = true }), JsonRequestBehavior.AllowGet);
// ===== NEW CODE END — currency format =====
// ----- OLD CODE (kept for rollback, do not delete) -----
//          return Json(JsonConvert.SerializeObject(new { products = rows, success = true }), JsonRequestBehavior.AllowGet);
// ----- END OLD CODE -----
        }

        /// <summary>Endpoint B: Individual issue entries for a specific product in selected period.</summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetProductIssueHistory(int productId, int month, int year)
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null) { return Json("", JsonRequestBehavior.AllowGet); }

            DateTime monthStart = new DateTime(year > 0 ? year : DateTime.Now.Year, month > 0 ? month : DateTime.Now.Month, 1);
            var issues = new List<object>();

            try
            {
                DateTime nextMonthStart = monthStart.AddMonths(1);
                string msl = ToSqlDate(monthStart);
                string nmsl = ToSqlDate(nextMonthStart);

                string invAccessSql = BuildAccessibleInventorySql(ctx, msl, nmsl);

                // Same cost rule and same zero exclusion as GetHighValueProducts - if the two drifted,
                // the modal's line values would no longer add up to the total shown on the row.
                string sql = @"
                    SELECT
                      ai.DocumentNo,
                      ai.MovementDate,
                      wh.Name AS WarehouseName,
                      loc.Value AS LocatorCode,
                      line.QtyInternalUse,
                      (line.QtyInternalUse * line.CurrentCostPrice) AS LineValue
                    FROM M_InventoryLine line
                    INNER JOIN (" + invAccessSql + @") ai ON ai.M_Inventory_ID = line.M_Inventory_ID
                    LEFT JOIN M_Locator loc ON loc.M_Locator_ID = line.M_Locator_ID
                    LEFT JOIN M_Warehouse wh ON wh.M_Warehouse_ID = loc.M_Warehouse_ID
                    WHERE line.IsActive = 'Y'
                      AND COALESCE(line.QtyInternalUse, 0) > 0
                      AND line.CurrentCostPrice > 0
                      AND line.M_Product_ID = " + productId + @"
                    ORDER BY ai.MovementDate DESC, ai.DocumentNo DESC";

                using (IDataReader dr = DB.ExecuteReader(sql, null, null))
                {
                    while (dr != null && dr.Read())
                    {
                        issues.Add(new
                        {
                            documentNo = Util.GetValueOfString(dr["DocumentNo"]),
                            movementDate = Convert.ToDateTime(dr["MovementDate"]).ToString("dd MMM yyyy"),
                            warehouseLoc = Util.GetValueOfString(dr["WarehouseName"]) + " / " + Util.GetValueOfString(dr["LocatorCode"]),
                            qty = Util.GetValueOfDecimal(dr["QtyInternalUse"]),
                            value = Util.GetValueOfDecimal(dr["LineValue"])
                        });
                    }
                }

            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_184_HighValueUsageWidget.GetProductIssueHistory", ex);
                return Json(JsonConvert.SerializeObject(new { error = Msg.GetMsg(ctx, "Error") ?? "Error" }), JsonRequestBehavior.AllowGet);
            }
// ===== NEW CODE START — currency format (agent A06, 2026-08-19) =====
            return Json(JsonConvert.SerializeObject(new { issues = issues, currency = GetCurrencyInfo(ctx), success = true }), JsonRequestBehavior.AllowGet);
// ===== NEW CODE END — currency format =====
// ----- OLD CODE (kept for rollback, do not delete) -----
//          return Json(JsonConvert.SerializeObject(new { issues = issues, success = true }), JsonRequestBehavior.AllowGet);
// ----- END OLD CODE -----
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

