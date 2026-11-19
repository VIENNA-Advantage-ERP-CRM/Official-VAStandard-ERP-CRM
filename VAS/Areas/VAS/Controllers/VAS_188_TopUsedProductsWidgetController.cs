using System.Data.SqlClient;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
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
    ///   Claude      2026-09-18 Usage-breakdown modal Qty column now sources
    ///                          M_InventoryLine.QtyEntered instead of QtyInternalUse.
    ///   Claude      2026-09-18 GetProductUsageDetails now also returns each line's UOM
    ///                          (C_UOM.Name) and its base-UOM quantity (QtyInternalUse, as
    ///                          qtyBaseUom) alongside the selected-UOM QtyEntered, so the
    ///                          modal can show both scales per line.
    ///   Claude      2026-09-18 GetProductUsageDetails also returns the PRODUCT's base UOM
    ///                          name (M_Product.C_UOM_ID, as baseUomName) - not a per-line
    ///                          entered UOM, which can vary line to line - for the "Consumed
    ///                          Qty (In Base UOM)" summary field to display alongside its number.
    /// </summary>
    public class VAS_188_TopUsedProductsWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_188_TopUsedProductsWidgetController).FullName);

        /// <summary>
        /// Factor that converts an issue-line quantity from the line's entered UOM to the
        /// product's own (selected) UOM, so quantities and unit prices are reported in the UOM
        /// the product is defined with - e.g. a line issued in Litres reads as millilitres when
        /// the product's UOM is MILLILITRE.
        ///
        /// C_UOM_Conversion stores the product-specific rate with C_UOM_ID = product UOM and
        /// C_UOM_To_ID = entered UOM; qty(product UOM) = qty(entered) * DivideRate (1 BOX = 126
        /// Each is stored as DivideRate 126). Same UOM short-circuits to 1, and an issue line
        /// with no conversion defined is left unchanged rather than dropped.
        /// Client/org-specific rows win, matching MUOMConversion's lookup order.
        /// Expects the line aliased as "line" and the product as "p".
        /// </summary>
        private const string UomToProductFactorSql = @"
                      COALESCE(
                        CASE WHEN COALESCE(line.C_UOM_ID, 0) = COALESCE(p.C_UOM_ID, 0) THEN 1 END,
                        (SELECT conv.DivideRate
                         FROM (SELECT conv0.DivideRate
                               FROM C_UOM_Conversion conv0
                               WHERE conv0.IsActive = 'Y'
                                 AND conv0.M_Product_ID = p.M_Product_ID
                                 AND conv0.C_UOM_ID = p.C_UOM_ID
                                 AND conv0.C_UOM_To_ID = line.C_UOM_ID
                                 AND COALESCE(conv0.DivideRate, 0) <> 0
                               ORDER BY conv0.AD_Client_ID DESC, conv0.AD_Org_ID DESC
                              ) conv
                         WHERE ROWNUM = 1),
                        1)";

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
                // VAS163 2026-11-13: rows 23/45/46/52/53/76 one Inventory Use rule - quantity is the
                // line's selected-UOM QtyEntered (QtyInternalUse is already base UOM, so multiplying it
                // by the UOM factor doubled 1 UNITS = 2 Each into 4 Each), one row per product + line UOM
                // (same as VAS_184), value = QtyInternalUse x M_InventoryLine.CurrentCostPrice.
                string orderBy = (measure == "val")
                    ? "SUM(line.QtyInternalUse * COALESCE(line.CurrentCostPrice, 0)) DESC"
                    : "SUM(line.QtyEntered) DESC";

                // Role access is applied to the inner header SELECT; applying it to this wrapped
                // aggregate would append the predicate outside the subquery (ORA-00907).
                string invAccessSql = BuildAccessibleInventorySql(ctx, msl, nmsl);

                // Ranking measure is the product's CURRENT cost price, per spec §3/§8 - the
                // quantity is converted to the product's selected UOM first so products whose
                // issue lines were entered in a different UOM still rank on comparable volume.
                string sql = @"
                    SELECT * FROM (
                      SELECT
                        p.M_Product_ID,
                        p.Name AS ProductName,
                        asi.Description AS Attribute,
                        pcat.Name AS CategoryName,
                        COALESCE(line.C_UOM_ID, 0) AS UomId,
                        luom.Name AS UomName,
                        SUM(line.QtyEntered) AS TotalQty,
                        SUM(line.QtyInternalUse * COALESCE(line.CurrentCostPrice, 0)) AS TotalValue
                      FROM M_InventoryLine line
                      INNER JOIN (" + invAccessSql + @") ai ON ai.M_Inventory_ID = line.M_Inventory_ID
                      INNER JOIN M_Product p ON p.M_Product_ID = line.M_Product_ID
                      LEFT JOIN M_Product_Category pcat ON pcat.M_Product_Category_ID = p.M_Product_Category_ID
                      LEFT JOIN C_UOM luom ON luom.C_UOM_ID = line.C_UOM_ID
                      LEFT JOIN M_AttributeSetInstance asi ON asi.M_AttributeSetInstance_ID = line.M_AttributeSetInstance_ID
                      WHERE line.IsActive = 'Y'
                        AND COALESCE(line.QtyInternalUse, 0) > 0
                      GROUP BY p.M_Product_ID, p.Name, asi.Description, pcat.Name, COALESCE(line.C_UOM_ID, 0), luom.Name
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
                            uomId = Util.GetValueOfInt(dr["UomId"]), // VAS163 2026-11-13: row 23 line UOM of this row
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
// ===== NEW CODE START — currency format (agent A10, 2026-08-19) =====
            return Json(JsonConvert.SerializeObject(new { products = products, currency = GetCurrencyInfo(ctx), success = true }), JsonRequestBehavior.AllowGet);
// ===== NEW CODE END — currency format =====
// ----- OLD CODE (kept for rollback, do not delete) -----
//          return Json(JsonConvert.SerializeObject(new { products = products, success = true }), JsonRequestBehavior.AllowGet);
// ----- END OLD CODE -----
        }

        /// <summary>Endpoint B: Individual issue lines for a specific product in selected period for modal.</summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetProductUsageDetails(int productId, int month, int year, int uomId = 0)
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

                // LocatorCombination is the full "Warehouse.Aisle.Bin.Level"-style locator name;
                // Value alone is often just an auto-generated numeric code (e.g. "1000004"), which
                // read like a raw ID to the user. Not present on every database release, so check
                // first and fall back to Value - same pattern as VAS_146/VAS_164/VAS_165/VAS_186.
                string locatorSql = HasColumn("M_Locator", "LocatorCombination")
                    ? "COALESCE(loc.LocatorCombination, loc.Value)"
                    : "loc.Value";

                string sql = @"
                    SELECT
                      ai.M_Inventory_ID AS InventoryId,
                      ai.DocumentNo,
                      ai.MovementDate,
                      wh.Name AS WarehouseName,
                      " + locatorSql + @" AS LocatorCode,
                      line.QtyEntered,
                      uom.Name AS UomName,
                      line.QtyInternalUse AS QtyBaseUOM,
                      baseUom.Name AS BaseUomName,
                      (line.QtyInternalUse * COALESCE(line.CurrentCostPrice, 0)) AS LineValue
                    FROM M_InventoryLine line
                    INNER JOIN (" + invAccessSql + @") ai ON ai.M_Inventory_ID = line.M_Inventory_ID
                    INNER JOIN M_Product prod ON prod.M_Product_ID = line.M_Product_ID
                    LEFT JOIN M_Locator loc ON loc.M_Locator_ID = line.M_Locator_ID
                    LEFT JOIN M_Warehouse wh ON wh.M_Warehouse_ID = loc.M_Warehouse_ID
                    LEFT JOIN C_UOM uom ON uom.C_UOM_ID = line.C_UOM_ID
                    LEFT JOIN C_UOM baseUom ON baseUom.C_UOM_ID = prod.C_UOM_ID
                    WHERE line.IsActive = 'Y'
                      AND COALESCE(line.QtyInternalUse, 0) > 0
                      AND line.M_Product_ID = " + productId + @"
                      " + (uomId > 0 ? "AND line.C_UOM_ID = " + uomId : "") + @"
                    ORDER BY ai.MovementDate DESC, ai.DocumentNo DESC";
                // VAS163 2026-11-13: row 23 - value = QtyInternalUse x M_InventoryLine.CurrentCostPrice (same
                // as the tile); uomId limits the lines to the product+UOM row the modal was opened from.

                string baseUomName = "";

                using (IDataReader dr = DB.ExecuteReader(sql, null, null))
                {
                    while (dr != null && dr.Read())
                    {
                        if (string.IsNullOrEmpty(baseUomName))
                        {
                            baseUomName = Util.GetValueOfString(dr["BaseUomName"]);
                        }
                        lines.Add(new
                        {
                            inventoryId = Util.GetValueOfInt(dr["InventoryId"]),
                            documentNo = Util.GetValueOfString(dr["DocumentNo"]),
                            movementDate = Convert.ToDateTime(dr["MovementDate"]).ToString("dd MMM yyyy"),
                            // VAS163 2026-11-10: browser locale (6-Nov retest2) - client formats this ISO date.
                            movementDateIso = Convert.ToDateTime(dr["MovementDate"]).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                            whLoc = BuildWarehouseLocator(Util.GetValueOfString(dr["WarehouseName"]), Util.GetValueOfString(dr["LocatorCode"])),
                            qty = Util.GetValueOfDecimal(dr["QtyEntered"]),
                            uomName = Util.GetValueOfString(dr["UomName"]),
                            qtyBaseUom = Util.GetValueOfDecimal(dr["QtyBaseUOM"]),
                            value = Util.GetValueOfDecimal(dr["LineValue"])
                        });
                    }
                }

// ===== NEW CODE START — currency format (agent A10, 2026-08-19) =====
                return Json(JsonConvert.SerializeObject(new { lines = lines, baseUomName = baseUomName, currency = GetCurrencyInfo(ctx), success = true }), JsonRequestBehavior.AllowGet);
// ===== NEW CODE END — currency format =====
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_188_TopUsedProductsWidget.GetProductUsageDetails", ex);
                return Json(JsonConvert.SerializeObject(new { error = Msg.GetMsg(ctx, "Error") ?? "Error" }), JsonRequestBehavior.AllowGet);
            }
// ----- OLD CODE (kept for rollback, do not delete) -----
//          return Json(JsonConvert.SerializeObject(new { lines = lines, success = true }), JsonRequestBehavior.AllowGet);
// ----- END OLD CODE -----
        }

        /// <summary>
        /// Role-filtered internal-use inventory headers for the period. Kept as a plain SELECT with
        /// no GROUP BY / ORDER BY: AddAccessSQL appends its predicate to the end of the statement,
        /// so it is only safe on a query whose alias is still in scope there.
        /// </summary>
        private static string BuildAccessibleInventorySql(Ctx ctx, string periodStart, string periodEnd)
        {
            // Source design (top-used-products-widget.md section 3): "Exclude documents with
            // DocStatus = Drafted (not yet actual consumption). Posted, Completed, and In Process
            // count." The previous IN ('CO','CL') silently dropped every In Process document, so
            // material already issued against an IP document never appeared in the ranking.
            // Voided ('VO') and Reversed ('RE') stay excluded - they are not consumption either,
            // which is why this is an explicit include-list rather than DocStatus <> 'DR'.
            string sql = @"
                    SELECT inv.M_Inventory_ID, inv.DocumentNo, inv.MovementDate
                    FROM M_Inventory inv
                    WHERE inv.IsActive = 'Y'
                      AND inv.DocStatus IN ('CO', 'CL', 'IP')
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

        /// <summary>Same dynamic column-existence check VAS_146/VAS_161-165/VAS_186 already use to guard LocatorCombination.</summary>
        private bool HasColumn(string tableName, string columnName)
        {
            string sql;
            if (DB.IsPostgreSQL())
            {
                sql = @"
                    SELECT COUNT(1)
                    FROM information_schema.columns
                    WHERE UPPER(table_name)=UPPER(@TableName)
                      AND UPPER(column_name)=UPPER(@ColumnName)";
            }
            else
            {
                sql = @"
                    SELECT COUNT(1)
                    FROM USER_TAB_COLUMNS
                    WHERE TABLE_NAME=UPPER(@TableName)
                      AND COLUMN_NAME=UPPER(@ColumnName)";
            }

            try
            {
                return Util.GetValueOfInt(DB.ExecuteScalar(sql, new SqlParameter[]
                {
                    new SqlParameter("@TableName", tableName),
                    new SqlParameter("@ColumnName", columnName)
                }, null)) > 0;
            }
            catch
            {
                return false;
            }
        }

// ===== NEW CODE START — currency format (agent A10, 2026-08-19) =====
        /// <summary>
        /// Retrieves tenant/organization base currency info (ISO code and symbol).
        /// Checks $C_Currency_ID from context first; falls back to C_AcctSchema if absent.
        /// </summary>
        private object GetCurrencyInfo(Ctx ctx)
        {
            string iso = "";
            string symbol = "";
            int stdPrecision = 2; // VAS163 2026-11-12: currency precision sent to the UI (C_Currency.StdPrecision)
            int currencyId = ctx.GetContextAsInt("$C_Currency_ID");
            if (currencyId > 0)
            {
                IDataReader cdr = null;
                try
                {
                    cdr = DB.ExecuteReader(
                        "SELECT ISO_Code, CurSymbol, StdPrecision FROM C_Currency WHERE C_Currency_ID = @Cur",
                        new SqlParameter[] { new SqlParameter("@Cur", currencyId) });
                    if (cdr != null && cdr.Read())
                    {
                        iso = Util.GetValueOfString(cdr["ISO_Code"]);
                        symbol = Util.GetValueOfString(cdr["CurSymbol"]);
                        stdPrecision = Util.GetValueOfInt(cdr["StdPrecision"]);
                    }
                }
                finally { if (cdr != null) { cdr.Close(); cdr.Dispose(); } }
            }

            if (string.IsNullOrEmpty(iso))
            {
                int clientId = ctx.GetAD_Client_ID();
                IDataReader cdr = null;
                try
                {
                    string sql = @"
                        SELECT c.ISO_Code, c.CurSymbol, c.StdPrecision
                        FROM AD_ClientInfo ci
                        INNER JOIN C_AcctSchema a ON (a.C_AcctSchema_ID = ci.C_AcctSchema1_ID AND a.IsActive = 'Y')
                        INNER JOIN C_Currency c ON (c.C_Currency_ID = a.C_Currency_ID AND c.IsActive = 'Y')
                        WHERE ci.AD_Client_ID = @ClientId";
                    cdr = DB.ExecuteReader(sql, new SqlParameter[] { new SqlParameter("@ClientId", clientId) });
                    if (cdr != null && cdr.Read())
                    {
                        iso = Util.GetValueOfString(cdr["ISO_Code"]);
                        symbol = Util.GetValueOfString(cdr["CurSymbol"]);
                        stdPrecision = Util.GetValueOfInt(cdr["StdPrecision"]);
                    }
                }
                finally { if (cdr != null) { cdr.Close(); cdr.Dispose(); } }
            }

            return new { iso = iso, symbol = symbol, stdPrecision = stdPrecision };
        }
// ===== NEW CODE END — currency format =====
    }
}

