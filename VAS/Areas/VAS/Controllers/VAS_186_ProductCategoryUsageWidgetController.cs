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



        /// <summary>Endpoint A: Category usage aggregates for selected month and year.</summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetCategoryUsage(int month, int year)
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null) { return Json("", JsonRequestBehavior.AllowGet); }

            var categories = new List<object>();
            DateTime monthStart = new DateTime(year > 0 ? year : DateTime.Now.Year, month > 0 ? month : DateTime.Now.Month, 1);

            try
            {
                DateTime nextMonthStart = monthStart.AddMonths(1);
                string msl = ToSqlDate(monthStart);
                string nmsl = ToSqlDate(nextMonthStart);

                // AddAccessSQL appends its predicate at the end of the statement, so it must be
                // applied to a plain SELECT (no GROUP BY / ORDER BY) where the alias is in scope.
                // Applying it to the aggregate query instead yields ORA-00933.
                string invAccessSql = BuildAccessibleInventorySql(ctx, msl, nmsl);

                // NULLIF guards are required, not cosmetic: line.CurrentCostPrice is a literal 0
                // (not NULL) on many issue lines, so a plain COALESCE returns 0 and never reaches
                // a fallback - those lines added nothing to the Value measure. On FSMTesting6 the
                // main 'Standard' category for July read 90,941.81 instead of 286,062.81 (3.1x low),
                // which silently mis-ranked the bars whenever the user switched to Value.
                string sql = @"
                    SELECT
                      pc.M_Product_Category_ID,
                      pc.Name AS CategoryName,
                      SUM(line.QtyInternalUse) AS TotalQty,
                      SUM(line.QtyInternalUse * COALESCE(NULLIF(line.CurrentCostPrice, 0), NULLIF(line.PriceCost, 0), NULLIF(line.VA024_CostPrice, 0), pcst.CurrentCostPrice, 0)) AS TotalValue
                    FROM M_InventoryLine line
                    INNER JOIN (" + invAccessSql + @") ai ON ai.M_Inventory_ID = line.M_Inventory_ID
                    INNER JOIN M_Product p ON p.M_Product_ID = line.M_Product_ID
                    INNER JOIN M_Product_Category pc ON pc.M_Product_Category_ID = p.M_Product_Category_ID
                    LEFT JOIN (" + ProductCurrentCostSql + @") pcst ON pcst.M_Product_ID = line.M_Product_ID
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
            return Json(JsonConvert.SerializeObject(new { categories = categories, success = true }), JsonRequestBehavior.AllowGet);
        }

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
                            attribute = NormalizeAttributes(Util.GetValueOfString(dr["Attribute"])),
                            uomName = Util.GetValueOfString(dr["UomName"]),
                            whLoc = BuildWarehouseLocator(Util.GetValueOfString(dr["WarehouseName"]), Util.GetValueOfString(dr["LocatorCode"])),
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

        /// <summary>
        /// An attribute set instance with no attributes stores a dash placeholder in Description
        /// (e.g. "---"). The drill-down modal renders the attribute meta line whenever it is truthy,
        /// so the placeholder would print as a literal "---" under the product name.
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

        /// <summary>
        /// "Warehouse / Bin" for the modal's WH + Loc column. Concatenating unconditionally emits a
        /// bare " / " when a line has no locator (and therefore no warehouse), so drop the missing
        /// side rather than rendering the separator on its own.
        /// </summary>
        private static string BuildWarehouseLocator(string warehouseName, string locatorCode)
        {
            bool hasWarehouse = !string.IsNullOrWhiteSpace(warehouseName);
            bool hasLocator = !string.IsNullOrWhiteSpace(locatorCode);

            if (hasWarehouse && hasLocator) { return warehouseName + " / " + locatorCode; }
            if (hasWarehouse) { return warehouseName; }
            if (hasLocator) { return locatorCode; }
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

