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
    /// Module Name : VAS_188_TopUsedProductsWidget
    /// Purpose     : Ranks top 10 products consumed by volume (quantity or value) with detail usage breakdown modal.
    /// Chronological development:
    ///   AI-Dev      2026-08-02 Created
    /// </summary>
    public class VAS_188_TopUsedProductsWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_188_TopUsedProductsWidgetController).FullName);

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
        /// Unit cost of an issue line: the line's own cost when it has one, otherwise the product's
        /// current cost, otherwise 0. Expects the line aliased as "line" and the product-cost
        /// derived table aliased as "pc".
        /// The NULLIF guards are essential: M_InventoryLine.CurrentCostPrice is a literal 0 (not
        /// NULL) on many issue lines, so a plain COALESCE returns 0 and never reaches the fallback.
        /// </summary>
        private const string LineUnitCostSql =
            "COALESCE(NULLIF(line.CurrentCostPrice, 0), NULLIF(line.PriceCost, 0), NULLIF(line.VA024_CostPrice, 0), pc.CurrentCostPrice, 0)";

        /// <summary>Endpoint A: Top 10 used products for selected month and year.</summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetTopProducts(int month, int year, string measure)
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null) { return Json("", JsonRequestBehavior.AllowGet); }

            var products = new List<object>();
            DateTime monthStart = new DateTime(year > 0 ? year : DateTime.Now.Year, month > 0 ? month : DateTime.Now.Month, 1);

            try
            {
                DateTime nextMonthStart = monthStart.AddMonths(1);
                string msl = ToSqlDate(monthStart);
                string nmsl = ToSqlDate(nextMonthStart);
                // The value expression must match the one selected below, or ordering by value would
                // rank on numbers the widget never displays. See LineUnitCostSql
                // for why the NULLIF guards are required.
                string orderBy = (measure == "val")
                    ? "SUM(line.QtyInternalUse * " + LineUnitCostSql + ") DESC"
                    : "SUM(line.QtyInternalUse) DESC";

                // Role access is applied to the inner header SELECT; applying it to this wrapped
                // aggregate would append the predicate outside the subquery (ORA-00907).
                string invAccessSql = BuildAccessibleInventorySql(ctx, msl, nmsl);

                string sql = @"
                    SELECT * FROM (
                      SELECT
                        p.M_Product_ID,
                        p.Name AS ProductName,
                        asi.Description AS Attribute,
                        pcat.Name AS CategoryName,
                        uom.Name AS UomName,
                        SUM(line.QtyInternalUse) AS TotalQty,
                        SUM(line.QtyInternalUse * " + LineUnitCostSql + @") AS TotalValue
                      FROM M_InventoryLine line
                      INNER JOIN (" + invAccessSql + @") ai ON ai.M_Inventory_ID = line.M_Inventory_ID
                      INNER JOIN M_Product p ON p.M_Product_ID = line.M_Product_ID
                      LEFT JOIN M_Product_Category pcat ON pcat.M_Product_Category_ID = p.M_Product_Category_ID
                      LEFT JOIN C_UOM uom ON uom.C_UOM_ID = line.C_UOM_ID
                      LEFT JOIN M_AttributeSetInstance asi ON asi.M_AttributeSetInstance_ID = line.M_AttributeSetInstance_ID
                      LEFT JOIN (" + ProductCurrentCostSql + @") pc ON pc.M_Product_ID = line.M_Product_ID
                      WHERE line.IsActive = 'Y'
                        AND COALESCE(line.QtyInternalUse, 0) > 0
                      GROUP BY p.M_Product_ID, p.Name, asi.Description, pcat.Name, uom.Name
                      ORDER BY " + orderBy + @"
                    ) WHERE ROWNUM <= 10";

                using (IDataReader dr = DB.ExecuteReader(sql, null, null))
                {
                    while (dr != null && dr.Read())
                    {
                        products.Add(new
                        {
                            productId = Util.GetValueOfInt(dr["M_Product_ID"]),
                            productName = Util.GetValueOfString(dr["ProductName"]),
                            attribute = NormalizeAttributes(Util.GetValueOfString(dr["Attribute"])),
                            categoryName = Util.GetValueOfString(dr["CategoryName"]),
                            uomName = Util.GetValueOfString(dr["UomName"]),
                            totalQty = Util.GetValueOfDecimal(dr["TotalQty"]),
                            totalValue = Util.GetValueOfDecimal(dr["TotalValue"])
                        });
                    }
                }

            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_188_TopUsedProductsWidget.GetTopProducts", ex);
                return Json(JsonConvert.SerializeObject(new { error = Msg.GetMsg(ctx, "Error") ?? "Error" }), JsonRequestBehavior.AllowGet);
            }
            return Json(JsonConvert.SerializeObject(new { products = products, success = true }), JsonRequestBehavior.AllowGet);
        }

        /// <summary>Endpoint B: Individual issue lines for a specific product in selected period for modal.</summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetProductUsageDetails(int productId, int month, int year)
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
                      ai.MovementDate,
                      wh.Name AS WarehouseName,
                      loc.Value AS LocatorCode,
                      line.QtyInternalUse,
                      (line.QtyInternalUse * " + LineUnitCostSql + @") AS LineValue
                    FROM M_InventoryLine line
                    INNER JOIN (" + invAccessSql + @") ai ON ai.M_Inventory_ID = line.M_Inventory_ID
                    LEFT JOIN M_Locator loc ON loc.M_Locator_ID = line.M_Locator_ID
                    LEFT JOIN M_Warehouse wh ON wh.M_Warehouse_ID = loc.M_Warehouse_ID
                    LEFT JOIN (" + ProductCurrentCostSql + @") pc ON pc.M_Product_ID = line.M_Product_ID
                    WHERE line.IsActive = 'Y'
                      AND COALESCE(line.QtyInternalUse, 0) > 0
                      AND line.M_Product_ID = " + productId + @"
                    ORDER BY ai.MovementDate DESC, ai.DocumentNo DESC";

                using (IDataReader dr = DB.ExecuteReader(sql, null, null))
                {
                    while (dr != null && dr.Read())
                    {
                        lines.Add(new
                        {
                            documentNo = Util.GetValueOfString(dr["DocumentNo"]),
                            movementDate = Convert.ToDateTime(dr["MovementDate"]).ToString("dd MMM yyyy"),
                            whLoc = BuildWarehouseLocator(Util.GetValueOfString(dr["WarehouseName"]), Util.GetValueOfString(dr["LocatorCode"])),
                            qty = Util.GetValueOfDecimal(dr["QtyInternalUse"]),
                            value = Util.GetValueOfDecimal(dr["LineValue"])
                        });
                    }
                }

            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_188_TopUsedProductsWidget.GetProductUsageDetails", ex);
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
        /// (e.g. "---"). The spec renders the attribute immediately after the product name, so the
        /// placeholder would print as a literal "---" beside every product.
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
        /// "Warehouse / Bin". Concatenating unconditionally emits a bare " / " when a line has no
        /// locator (and so no warehouse via the loc -> wh join), so drop the missing side.
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
