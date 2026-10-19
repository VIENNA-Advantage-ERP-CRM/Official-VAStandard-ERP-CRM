using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Web.Mvc;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VAS.Controllers
{
    /// <summary>
    /// Module Name : VAS_111_BelowMinWidget
    /// Purpose     : Supplies the "Below Min" KPI for the Product / Item Master
    ///               dashboard - the count of items whose on-hand stock is at or
    ///               below the item's reorder point (per-warehouse minimum from
    ///               M_Replenish, the same reorder-point source as VAS_073). An
    ///               item low in more than one warehouse is counted once.
    ///               Widget number 111 - reassign on hand-off.
    /// Chronological development:
    ///   111         2026-07-15 Created
    ///   Claude      2026-10-15 GetBelowMinDetails added: one row per product + warehouse below its reorder
    ///                          point (Warehouse, Product, Attribute, Min/Max, On Hand) for the dialog opened
    ///                          from the tile; same stock / reorder-point logic as the tile count.
    ///   Claude      2026-10-15 Dialog rows: a single reorder value chosen by ReplenishType (Level_Min for
    ///                          type 1, Level_Max for type 2) instead of Min/Max; a product held with several
    ///                          attribute set instances is listed once per attribute with that attribute's own
    ///                          on-hand.
    ///   Claude      2026-10-15 The attribute column of M_Replenish is now honoured (see BuildBelowMinCtes):
    ///                          a replenish record that names an attribute set instance is compared with the
    ///                          stock of THAT attribute only; a record without one keeps comparing the product +
    ///                          warehouse total. The tile count and the dialog now share one query builder so
    ///                          they always agree.
    ///   Claude      2026-10-15 Driven by M_Replenish only: one row per replenish record (no per-attribute
    ///                          breakdown of all M_Storage stock for a record without an attribute); stock is
    ///                          LEFT-joined, so a record whose product holds no stock counts as 0 on hand.
    ///                          Rows sharing warehouse + product + attribute description are merged.
    /// </summary>
    public class VAS_111_BelowMinWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_111_BelowMinWidgetController).FullName);

        // Name of the attribute column on M_Replenish (the generated X_M_Replenish model has none, so the
        // data dictionary is asked once per app lifetime). Without it every record counts as "no attribute".
        private const string ReplenishAttributeColumn = "M_AttributeSetInstance_ID";
        private static bool? _replenishHasAttributeColumn;

        /// <summary>True when the application dictionary has M_Replenish.M_AttributeSetInstance_ID.</summary>
        private bool ReplenishHasAttributeColumn()
        {
            if (_replenishHasAttributeColumn.HasValue) { return _replenishHasAttributeColumn.Value; }

            string sql = @"
                SELECT COUNT(1)
                FROM AD_Column ColumnInfo
                INNER JOIN AD_Table TableInfo ON (TableInfo.AD_Table_ID=ColumnInfo.AD_Table_ID AND TableInfo.IsActive='Y')
                WHERE ColumnInfo.IsActive='Y'
                  AND UPPER(TableInfo.TableName)='M_REPLENISH'
                  AND UPPER(ColumnInfo.ColumnName)='M_ATTRIBUTESETINSTANCE_ID'";

            _replenishHasAttributeColumn = Util.GetValueOfInt(DB.ExecuteScalar(sql, new SqlParameter[0], null)) > 0;
            return _replenishHasAttributeColumn.Value;
        }

        /// <summary>
        /// Returns the count of items below their reorder point.
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetBelowMin()
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null)
            {
                return Json("", JsonRequestBehavior.AllowGet);
            }

            try
            {
                int belowMinCount = GetBelowMinData(ctx);
                string json = JsonConvert.SerializeObject(new { below_min_count = belowMinCount });
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_111_BelowMinWidget.GetBelowMin", ex);
                string json = JsonConvert.SerializeObject(new { error = Msg.GetMsg(ctx, "Error") ?? "Error" });
                return Json(json, JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Returns the product / warehouse / attribute rows below their reorder point for the
        /// "Below Min" detail dialog (Warehouse, Product, Attribute, Min/Max, On Hand).
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetBelowMinDetails()
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null)
            {
                return Json("", JsonRequestBehavior.AllowGet);
            }

            try
            {
                List<BelowMinRow> rows = GetBelowMinRows(ctx);
                string json = JsonConvert.SerializeObject(new { rows = rows });
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_111_BelowMinWidget.GetBelowMinDetails", ex);
                string json = JsonConvert.SerializeObject(new { error = Msg.GetMsg(ctx, "Error") ?? "Error" });
                return Json(json, JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Builds the shared "WITH ..." prefix that ends in a BelowRows CTE - one row per product +
        /// warehouse + attribute set instance whose on-hand is at or below its reorder point
        /// (columns M_Product_ID, M_Warehouse_ID, M_AttributeSetInstance_ID, Reorder_Point, Qty_On_Hand).
        ///
        /// The result is driven by the M_Replenish records only - one row per replenish record, never a
        /// breakdown of everything held in M_Storage. Reorder point = Level_Min for ReplenishType '1',
        /// Level_Max for '2'. M_Replenish's attribute column decides what the record is compared with:
        ///   - no attribute (0 / null): the product + warehouse TOTAL on hand, shown as one row;
        ///   - an attribute: only the stock of THAT attribute set instance in that warehouse, one row.
        /// A record whose product has no stock row at all is compared as 0 on hand (so it is below minimum).
        /// Without the attribute column on M_Replenish every record falls in the first group.
        ///
        /// The role-secured tables are CTEs of their own (each AddAccessSQL text is complete); the other CTEs
        /// only read CTE aliases. Placeholders are bound positionally and each appears once, in text order.
        /// </summary>
        private string BuildBelowMinCtes(Ctx ctx, List<SqlParameter> parameters)
        {
            MRole role = MRole.GetDefault(ctx);
            string replenishAttribute = ReplenishHasAttributeColumn()
                ? "COALESCE(Replenish." + ReplenishAttributeColumn + ",0)"
                : "0";

            string storageSql = role.AddAccessSQL(@"
                SELECT Storage.M_Product_ID,
                       Storage.M_Locator_ID,
                       Storage.M_AttributeSetInstance_ID,
                       Storage.QtyOnHand
                FROM M_Storage Storage
                WHERE Storage.IsActive='Y'
                  AND Storage.AD_Client_ID=@Storage_Client_ID
                  AND Storage.AD_Org_ID IN (0,COALESCE(NULLIF(@Storage_Org_ID,0),Storage.AD_Org_ID))",
                "Storage", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string locatorSql = role.AddAccessSQL(@"
                SELECT Locator.M_Locator_ID,
                       Locator.M_Warehouse_ID
                FROM M_Locator Locator
                WHERE Locator.IsActive='Y'
                  AND Locator.AD_Client_ID=@Locator_Client_ID
                  AND Locator.AD_Org_ID IN (0,COALESCE(NULLIF(@Locator_Org_ID,0),Locator.AD_Org_ID))",
                "Locator", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string replenishSql = role.AddAccessSQL(@"
                SELECT Replenish.M_Product_ID,
                       Replenish.M_Warehouse_ID,
                       " + replenishAttribute + @" AS M_AttributeSetInstance_ID,
                       CASE
                           WHEN Replenish.ReplenishType='1' THEN Replenish.Level_Min
                           WHEN Replenish.ReplenishType='2' THEN Replenish.Level_Max
                           ELSE NULL
                       END AS Reorder_Point
                FROM M_Replenish Replenish
                WHERE Replenish.IsActive='Y'
                  AND Replenish.ReplenishType IN ('1','2')
                  AND Replenish.AD_Client_ID=@Replenish_Client_ID
                  AND Replenish.AD_Org_ID IN (0,COALESCE(NULLIF(@Replenish_Org_ID,0),Replenish.AD_Org_ID))",
                "Replenish", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            parameters.Add(new SqlParameter("@Storage_Client_ID", ctx.GetAD_Client_ID()));
            parameters.Add(new SqlParameter("@Storage_Org_ID", ctx.GetAD_Org_ID()));
            parameters.Add(new SqlParameter("@Locator_Client_ID", ctx.GetAD_Client_ID()));
            parameters.Add(new SqlParameter("@Locator_Org_ID", ctx.GetAD_Org_ID()));
            parameters.Add(new SqlParameter("@Replenish_Client_ID", ctx.GetAD_Client_ID()));
            parameters.Add(new SqlParameter("@Replenish_Org_ID", ctx.GetAD_Org_ID()));

            return @"
                WITH StorageRows AS (
                    " + storageSql + @"
                ),
                LocatorRows AS (
                    " + locatorSql + @"
                ),
                ReplenishRows AS (
                    " + replenishSql + @"
                ),
                AttributeStock AS (
                    SELECT StorageRows.M_Product_ID,
                           LocatorRows.M_Warehouse_ID,
                           COALESCE(StorageRows.M_AttributeSetInstance_ID,0) AS M_AttributeSetInstance_ID,
                           SUM(StorageRows.QtyOnHand) AS Qty_On_Hand
                    FROM StorageRows
                    INNER JOIN LocatorRows ON (LocatorRows.M_Locator_ID=StorageRows.M_Locator_ID)
                    GROUP BY StorageRows.M_Product_ID,
                             LocatorRows.M_Warehouse_ID,
                             COALESCE(StorageRows.M_AttributeSetInstance_ID,0)
                ),
                WarehouseStock AS (
                    SELECT AttributeStock.M_Product_ID,
                           AttributeStock.M_Warehouse_ID,
                           SUM(AttributeStock.Qty_On_Hand) AS Qty_On_Hand
                    FROM AttributeStock
                    GROUP BY AttributeStock.M_Product_ID,
                             AttributeStock.M_Warehouse_ID
                ),
                BelowRows AS (
                    SELECT ReplenishRows.M_Product_ID,
                           ReplenishRows.M_Warehouse_ID,
                           0 AS M_AttributeSetInstance_ID,
                           ReplenishRows.Reorder_Point,
                           COALESCE(WarehouseStock.Qty_On_Hand,0) AS Qty_On_Hand
                    FROM ReplenishRows
                    LEFT OUTER JOIN WarehouseStock ON (WarehouseStock.M_Product_ID=ReplenishRows.M_Product_ID AND WarehouseStock.M_Warehouse_ID=ReplenishRows.M_Warehouse_ID)
                    WHERE ReplenishRows.M_AttributeSetInstance_ID=0
                      AND ReplenishRows.Reorder_Point IS NOT NULL
                      AND COALESCE(WarehouseStock.Qty_On_Hand,0)<=ReplenishRows.Reorder_Point
                    UNION ALL
                    SELECT ReplenishRows.M_Product_ID,
                           ReplenishRows.M_Warehouse_ID,
                           ReplenishRows.M_AttributeSetInstance_ID,
                           ReplenishRows.Reorder_Point,
                           COALESCE(AttributeStock.Qty_On_Hand,0) AS Qty_On_Hand
                    FROM ReplenishRows
                    LEFT OUTER JOIN AttributeStock ON (AttributeStock.M_Product_ID=ReplenishRows.M_Product_ID AND AttributeStock.M_Warehouse_ID=ReplenishRows.M_Warehouse_ID AND AttributeStock.M_AttributeSetInstance_ID=ReplenishRows.M_AttributeSetInstance_ID)
                    WHERE ReplenishRows.M_AttributeSetInstance_ID<>0
                      AND ReplenishRows.Reorder_Point IS NOT NULL
                      AND COALESCE(AttributeStock.Qty_On_Hand,0)<=ReplenishRows.Reorder_Point
                )";
        }

        /// <summary>
        /// One row per product + warehouse + attribute below its reorder point, with names, for the dialog.
        /// Product and warehouse names come from two more role-secured CTEs (their parameters follow the
        /// shared ones in text order). Rows are grouped by warehouse + product + attribute DESCRIPTION (with the
        /// quantities summed): several attribute set instances can carry the same description, and listing
        /// them separately showed duplicate-looking rows.
        /// </summary>
        private List<BelowMinRow> GetBelowMinRows(Ctx ctx)
        {
            List<BelowMinRow> result = new List<BelowMinRow>();
            if (ctx == null) { return result; }

            List<SqlParameter> parameters = new List<SqlParameter>();
            string shared = BuildBelowMinCtes(ctx, parameters);
            MRole role = MRole.GetDefault(ctx);

            string productSql = role.AddAccessSQL(@"
                SELECT Product.M_Product_ID,
                       Product.Name AS Product_Name,
                       Product.Value AS Product_Code
                FROM M_Product Product
                WHERE Product.IsActive='Y'
                  AND Product.AD_Client_ID=@Product_Client_ID",
                "Product", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string warehouseSql = role.AddAccessSQL(@"
                SELECT Warehouse.M_Warehouse_ID,
                       Warehouse.Name AS Warehouse_Name
                FROM M_Warehouse Warehouse
                WHERE Warehouse.IsActive='Y'
                  AND Warehouse.AD_Client_ID=@Warehouse_Client_ID",
                "Warehouse", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            parameters.Add(new SqlParameter("@Product_Client_ID", ctx.GetAD_Client_ID()));
            parameters.Add(new SqlParameter("@Warehouse_Client_ID", ctx.GetAD_Client_ID()));

            string sql = shared + @",
                ProductRows AS (
                    " + productSql + @"
                ),
                WarehouseRows AS (
                    " + warehouseSql + @"
                )
                SELECT WarehouseRows.Warehouse_Name,
                       ProductRows.Product_Name,
                       ProductRows.Product_Code,
                       AttributeInstance.Description AS Attribute_Description,
                       BelowRows.Reorder_Point,
                       SUM(BelowRows.Qty_On_Hand) AS Qty_On_Hand
                FROM BelowRows
                INNER JOIN ProductRows ON (ProductRows.M_Product_ID=BelowRows.M_Product_ID)
                INNER JOIN WarehouseRows ON (WarehouseRows.M_Warehouse_ID=BelowRows.M_Warehouse_ID)
                LEFT OUTER JOIN M_AttributeSetInstance AttributeInstance ON (AttributeInstance.M_AttributeSetInstance_ID=BelowRows.M_AttributeSetInstance_ID)
                GROUP BY WarehouseRows.Warehouse_Name,
                         ProductRows.Product_Name,
                         ProductRows.Product_Code,
                         AttributeInstance.Description,
                         BelowRows.Reorder_Point
                ORDER BY WarehouseRows.Warehouse_Name, ProductRows.Product_Name, AttributeInstance.Description";

            IDataReader reader = null;
            try
            {
                reader = DB.ExecuteReader(sql, parameters.ToArray());
                while (reader != null && reader.Read())
                {
                    result.Add(new BelowMinRow
                    {
                        warehouse = Util.GetValueOfString(reader["Warehouse_Name"]),
                        product = Util.GetValueOfString(reader["Product_Name"]),
                        product_code = Util.GetValueOfString(reader["Product_Code"]),
                        attribute = Util.GetValueOfString(reader["Attribute_Description"]),
                        reorder_point = Util.GetValueOfDecimal(reader["Reorder_Point"]),
                        on_hand = Util.GetValueOfDecimal(reader["Qty_On_Hand"])
                    });
                }
            }
            finally
            {
                if (reader != null) { reader.Close(); reader.Dispose(); }
            }

            return MergeRows(result);
        }

        /// <summary>
        /// An attribute set instance of a product without attributes carries the description "---".
        /// That is "no attribute", so it is blanked, and rows that become identical (same warehouse,
        /// product, attribute and reorder value) are merged with their quantities added. Order is kept.
        /// </summary>
        private static List<BelowMinRow> MergeRows(List<BelowMinRow> rows)
        {
            List<BelowMinRow> merged = new List<BelowMinRow>();
            Dictionary<string, BelowMinRow> byKey = new Dictionary<string, BelowMinRow>();

            foreach (BelowMinRow row in rows)
            {
                string attribute = (row.attribute ?? "").Trim();
                if (attribute.Trim('-', ' ', '—', '–').Length == 0) { attribute = ""; }
                row.attribute = attribute;

                string key = row.warehouse + "|" + row.product_code + "|" + row.product + "|" + attribute + "|" + row.reorder_point;
                BelowMinRow existing;
                if (byKey.TryGetValue(key, out existing))
                {
                    existing.on_hand += row.on_hand;
                }
                else
                {
                    byKey[key] = row;
                    merged.Add(row);
                }
            }

            return merged;
        }

        /// <summary>One product / warehouse / attribute below its reorder point.</summary>
        private class BelowMinRow
        {
            public string warehouse { get; set; }
            public string product { get; set; }
            public string product_code { get; set; }
            public string attribute { get; set; }
            // Single value chosen by ReplenishType: Level_Min for type '1', Level_Max for type '2'.
            public decimal reorder_point { get; set; }
            // Stock of this product + warehouse + attribute set instance (one row per attribute).
            public decimal on_hand { get; set; }
        }

        /// <summary>
        /// Counts distinct items below their reorder point in at least one warehouse (and, for a replenish
        /// record that names an attribute, in that attribute). Plain ASCII literals only - N'...' literals
        /// fail against CHAR columns on Oracle (ORA-12704) and are invalid on PostgreSQL (same fix as
        /// VAS_073, review #43).
        /// </summary>
        /// <param name="ctx">Current application context.</param>
        /// <returns>Distinct below-min item count.</returns>
        private int GetBelowMinData(Ctx ctx)
        {
            if (ctx == null) { return 0; }

            List<SqlParameter> parameters = new List<SqlParameter>();
            string sql = BuildBelowMinCtes(ctx, parameters) + @"
                SELECT COUNT(DISTINCT BelowRows.M_Product_ID) AS Below_Min_Count
                FROM BelowRows";

            return Util.GetValueOfInt(DB.ExecuteScalar(sql, parameters.ToArray(), null));
        }
    }
}
