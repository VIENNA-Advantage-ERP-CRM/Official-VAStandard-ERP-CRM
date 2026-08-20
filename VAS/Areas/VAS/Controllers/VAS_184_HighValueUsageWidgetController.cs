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
        /// <summary>
        /// The product's CURRENT cost price, as a derived table (M_Product_ID, CurrentCostPrice).
        /// Picks the M_Cost row whose cost element matches the accounting schema's own costing
        /// method, so landed-cost and other cost COMPONENT rows are excluded. A plain
        /// MAX(M_Cost.CurrentCostPrice) is NOT the product cost - on FSMTesting6 it reports
        /// 'Air Filter (7 micron)' at 80,142.29 (a Landed Cost component) against a true standard
        /// cost of 2,599.
        /// </summary>
        private const string ProductCurrentCostSql = @"
                    SELECT c.M_Product_ID, MAX(c.CurrentCostPrice) AS CurrentCostPrice
                    FROM M_Cost c
                    INNER JOIN M_CostElement ce ON ce.M_CostElement_ID = c.M_CostElement_ID
                    INNER JOIN C_AcctSchema acs ON acs.C_AcctSchema_ID = c.C_AcctSchema_ID
                                               AND acs.M_CostType_ID   = c.M_CostType_ID
                    WHERE c.IsActive = 'Y'
                      AND ce.CostingMethod IS NOT NULL
                      AND ce.CostingMethod = acs.CostingMethod
                    GROUP BY c.M_Product_ID";


        /// <summary>
        /// The product's CURRENT cost price - the live valuation cost the spec ranks this widget by
        /// </summary>

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

                // Ranking measure is the PRODUCT's current cost price, per spec §3/§8 - explicitly
                // "not the historical issue cost on the line". Ranking by the line cost put
                // genuinely expensive products at the bottom of a high-value widget: on
                // FSMTesting6 'Valves or nozzles', 'Air Filter (7 micron)', 'Spray balls' and
                // 'Brake fluid tester' all carry NO cost on their issue lines, so they scored 0
                // against real current costs of 2,699 / 2,599 / 2,499 / 2,399.
                //
                // Issued value still prefers the line's own cost (that is what the issue actually
                // cost); the product cost is the fallback so a line with no recorded cost
                // contributes its valuation rather than zero.
                string sql = @"
                    SELECT * FROM (
                      SELECT
                        p.M_Product_ID,
                        p.Name AS ProductName,
                        asi.Description AS Attribute,
                        uom.Name AS UomName,
                        MAX(COALESCE(pc.CurrentCostPrice, NULLIF(line.CurrentCostPrice, 0), NULLIF(line.PriceCost, 0), NULLIF(line.VA024_CostPrice, 0), 0)) AS CostPrice,
                        SUM(line.QtyInternalUse) AS TotalIssuedQty,
                        SUM(line.QtyInternalUse * COALESCE(NULLIF(line.CurrentCostPrice, 0), NULLIF(line.PriceCost, 0), NULLIF(line.VA024_CostPrice, 0), pc.CurrentCostPrice, 0)) AS TotalIssuedValue
                      FROM M_InventoryLine line
                      INNER JOIN (" + invAccessSql + @") ai ON ai.M_Inventory_ID = line.M_Inventory_ID
                      INNER JOIN M_Product p ON p.M_Product_ID = line.M_Product_ID
                      LEFT JOIN C_UOM uom ON uom.C_UOM_ID = line.C_UOM_ID
                      LEFT JOIN M_AttributeSetInstance asi ON asi.M_AttributeSetInstance_ID = line.M_AttributeSetInstance_ID
                      LEFT JOIN (" + ProductCurrentCostSql + @") pc ON pc.M_Product_ID = p.M_Product_ID
                      WHERE line.IsActive = 'Y'
                        AND COALESCE(line.QtyInternalUse, 0) > 0
                        AND line.CurrentCostPrice > 0
                      GROUP BY p.M_Product_ID, p.Name, asi.Description, uom.Name
                      ORDER BY MAX(COALESCE(pc.CurrentCostPrice, NULLIF(line.CurrentCostPrice, 0), NULLIF(line.PriceCost, 0), NULLIF(line.VA024_CostPrice, 0), 0)) DESC,
                               SUM(line.QtyInternalUse * COALESCE(NULLIF(line.CurrentCostPrice, 0), NULLIF(line.PriceCost, 0), NULLIF(line.VA024_CostPrice, 0), pc.CurrentCostPrice, 0)) DESC
                    ) WHERE ROWNUM <= 10";

                using (IDataReader dr = DB.ExecuteReader(sql, null, null))
                {
                    while (dr != null && dr.Read())
                    {
                        rows.Add(new
                        {
                            productId = Util.GetValueOfInt(dr["M_Product_ID"]),
                            productName = Util.GetValueOfString(dr["ProductName"]),
                            attribute = NormalizeAttributes(Util.GetValueOfString(dr["Attribute"])),
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
                      (line.QtyInternalUse * COALESCE(NULLIF(line.CurrentCostPrice, 0), NULLIF(line.PriceCost, 0), NULLIF(line.VA024_CostPrice, 0), pc.CurrentCostPrice, 0)) AS LineValue
                    FROM M_InventoryLine line
                    INNER JOIN (" + invAccessSql + @") ai ON ai.M_Inventory_ID = line.M_Inventory_ID
                    LEFT JOIN M_Locator loc ON loc.M_Locator_ID = line.M_Locator_ID
                    LEFT JOIN M_Warehouse wh ON wh.M_Warehouse_ID = loc.M_Warehouse_ID
                    LEFT JOIN (" + ProductCurrentCostSql + @") pc ON pc.M_Product_ID = line.M_Product_ID
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

        /// <summary>
        /// An attribute set instance with no attributes stores a dash placeholder in Description
        /// (e.g. "---"). The widget renders the attribute into the row meta line whenever it is
        /// truthy, so the placeholder would show as "--- - 34 Nos". Collapse dash-only values to
        /// empty and let the widget's own fallback handle it.
        /// </summary>
        private static string NormalizeAttributes(string description)
        {
            if (string.IsNullOrWhiteSpace(description)) { return ""; }

            string trimmed = description.Trim();
            foreach (char c in trimmed)
            {
                if (c != '-' && c != '_' && c != '.' && !char.IsWhiteSpace(c))
                {
                    return trimmed;
                }
            }
            return "";
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

