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
    /// Module Name : VAS_113_CategoryMixWidget
    /// Purpose     : Supplies the "Category Mix" widget for the Product / Item
    ///               Master dashboard - the share of active items across product
    ///               categories (count per category and % of all active items),
    ///               plus a drill-in list of the items in a chosen category with
    ///               on-hand quantity and schema-currency stock value. Active =
    ///               IsActive and not discontinued. Stock value uses the same
    ///               cost-fallback valuation as VAS_079. Widget number 113 -
    ///               reassign on hand-off.
    /// Chronological development:
    ///   113         2026-07-16 Created
    /// </summary>
    public class VAS_113_CategoryMixWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_113_CategoryMixWidgetController).FullName);

        /// <summary>
        /// Returns the categories ranked by active-item count, with the total.
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetCategoryMix()
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null)
            {
                return Json("", JsonRequestBehavior.AllowGet);
            }

            try
            {
                CategoryMixResult result = GetCategoryMixData(ctx);
                return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_113_CategoryMixWidget.GetCategoryMix", ex);
                string json = JsonConvert.SerializeObject(new { error = Msg.GetMsg(ctx, "Error") ?? "Error" });
                return Json(json, JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Returns the active items in one category (SKU, name, on-hand, stock
        /// value) for the drill-in list; the client pages this in the modal.
        /// </summary>
        /// <param name="categoryId">Selected product category.</param>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetCategoryItems(int categoryId)
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null)
            {
                return Json(JsonConvert.SerializeObject(new { error = "Session Expired" }), JsonRequestBehavior.AllowGet);
            }

            try
            {
                CategoryItemsResult result = GetCategoryItemsData(ctx, categoryId);
                return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_113_CategoryMixWidget.GetCategoryItems", ex);
                string json = JsonConvert.SerializeObject(new { error = Msg.GetMsg(ctx, "Error") ?? "Error" });
                return Json(json, JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Counts active, non-discontinued items per active category, ordered by
        /// count descending. The total is summed in C# for the share %.
        /// </summary>
        private CategoryMixResult GetCategoryMixData(Ctx ctx)
        {
            CategoryMixResult result = new CategoryMixResult { rows = new List<CategoryShare>() };
            if (ctx == null) { return result; }

            // MRole on the main table (Category); GROUP BY / ORDER BY appended
            // after the access predicate (ORA-00907 lesson). The item join
            // predicate lives in the ON clause so categories with zero active
            // items still appear.
            string sql = @"
                SELECT Category.M_Product_Category_ID,
                       Category.Name AS Category_Name,
                       COUNT(Prod.M_Product_ID) AS Active_Items
                FROM M_Product_Category Category
                LEFT OUTER JOIN M_Product Prod ON (Prod.M_Product_Category_ID=Category.M_Product_Category_ID
                    AND Prod.IsActive='Y'
                    AND (Prod.Discontinued IS NULL OR Prod.Discontinued='N')
                    AND Prod.AD_Client_ID=@Prod_Client_ID
                    AND Prod.AD_Org_ID IN (0,COALESCE(NULLIF(@Prod_Org_ID,0),Prod.AD_Org_ID)))
                WHERE Category.IsActive='Y'
                  AND Category.AD_Client_ID=@Cat_Client_ID
                  AND Category.AD_Org_ID IN (0,COALESCE(NULLIF(@Cat_Org_ID,0),Category.AD_Org_ID))";

            sql = AddAccessSql(ctx, sql, "Category");
            sql += @"
                GROUP BY Category.M_Product_Category_ID, Category.Name
                ORDER BY COUNT(Prod.M_Product_ID) DESC, Category.Name";

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@Prod_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@Prod_Org_ID", ctx.GetAD_Org_ID()),
                new SqlParameter("@Cat_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@Cat_Org_ID", ctx.GetAD_Org_ID())
            };

            int total = 0;
            IDataReader reader = null;
            try
            {
                reader = DB.ExecuteReader(sql, parameters);
                while (reader != null && reader.Read())
                {
                    int count = Util.GetValueOfInt(reader["Active_Items"]);
                    result.rows.Add(new CategoryShare
                    {
                        category_id = Util.GetValueOfInt(reader["M_Product_Category_ID"]),
                        name = Util.GetValueOfString(reader["Category_Name"]),
                        active_items = count
                    });
                    total += count;
                }
            }
            finally
            {
                CloseReader(reader);
            }

            result.total_active_items = total;
            return result;
        }

        /// <summary>
        /// Loads the active, non-discontinued items of one category with their
        /// summed on-hand quantity and schema-currency stock value. Stock value
        /// values each storage row with the best available cost (org/warehouse/
        /// ASI/product fallback, same as VAS_079). MRole on each physical block;
        /// the final SELECT reads only CTE aliases. Plain ASCII literals for
        /// Oracle + PostgreSQL.
        /// </summary>
        private CategoryItemsResult GetCategoryItemsData(Ctx ctx, int categoryId)
        {
            CategoryItemsResult result = new CategoryItemsResult { rows = new List<CategoryItem>() };
            if (ctx == null || categoryId <= 0) { return result; }

            SchemaCurrency currency = GetSchemaCurrency(ctx);
            result.currency_symbol = currency.Symbol;
            result.currency_iso = currency.IsoCode;
            result.std_precision = currency.StdPrecision;

            string storageSql = @"
                SELECT Storage.AD_Org_ID,
                       Storage.M_Locator_ID,
                       Storage.M_Product_ID,
                       COALESCE(Storage.M_AttributeSetInstance_ID,0) AS M_AttributeSetInstance_ID,
                       Storage.QtyOnHand
                FROM M_Storage Storage
                WHERE Storage.IsActive='Y'
                  AND Storage.AD_Client_ID=@Storage_Client_ID
                  AND Storage.AD_Org_ID IN (0,COALESCE(NULLIF(@Storage_Org_ID,0),Storage.AD_Org_ID))";

            string locatorSql = @"
                SELECT Locator.M_Locator_ID,
                       Locator.M_Warehouse_ID
                FROM M_Locator Locator
                WHERE Locator.IsActive='Y'
                  AND Locator.AD_Client_ID=@Locator_Client_ID
                  AND Locator.AD_Org_ID IN (0,COALESCE(NULLIF(@Locator_Org_ID,0),Locator.AD_Org_ID))";

            string productSql = @"
                SELECT Product.M_Product_ID,
                       Product.Value AS Product_Code,
                       Product.Name AS Product_Name
                FROM M_Product Product
                WHERE Product.IsActive='Y'
                  AND (Product.Discontinued IS NULL OR Product.Discontinued='N')
                  AND Product.M_Product_Category_ID=@Category_ID
                  AND Product.AD_Client_ID=@Product_Client_ID
                  AND Product.AD_Org_ID IN (0,COALESCE(NULLIF(@Product_Org_ID,0),Product.AD_Org_ID))";

            string costSql = @"
                SELECT Cost.AD_Org_ID,
                       Cost.M_Product_ID,
                       COALESCE(Cost.M_Warehouse_ID,0) AS M_Warehouse_ID,
                       COALESCE(Cost.M_AttributeSetInstance_ID,0) AS M_AttributeSetInstance_ID,
                       SUM(COALESCE(Cost.CurrentCostPrice,0)) AS Unit_Cost
                FROM M_Cost Cost
                INNER JOIN M_Product CostProduct ON (CostProduct.M_Product_ID=Cost.M_Product_ID AND CostProduct.IsActive='Y')
                LEFT OUTER JOIN M_Product_Category CostCategory ON (CostCategory.M_Product_Category_ID=CostProduct.M_Product_Category_ID AND CostCategory.IsActive='Y')
                INNER JOIN M_CostElement CostElement ON (
                    CostElement.M_CostElement_ID=Cost.M_CostElement_ID
                    AND CostElement.IsActive='Y'
                    AND CostElement.CostingMethod=COALESCE(NULLIF(CostCategory.CostingMethod,''),@Element_Costing_Method)
                )
                WHERE Cost.IsActive='Y'
                  AND Cost.C_AcctSchema_ID=@C_AcctSchema_ID
                  AND Cost.M_CostType_ID=@M_CostType_ID
                  AND Cost.AD_Client_ID=@Cost_Client_ID
                  AND Cost.AD_Org_ID IN (0,COALESCE(NULLIF(@Cost_Org_ID,0),Cost.AD_Org_ID))
                  AND (
                      COALESCE(NULLIF(CostCategory.CostingMethod,''),@Filter_Costing_Method)<>'C'
                      OR Cost.M_CostElement_ID=COALESCE(NULLIF(CostCategory.M_CostElement_ID,0),@M_CostElement_ID)
                  )";

            storageSql = AddAccessSql(ctx, storageSql, "Storage");
            locatorSql = AddAccessSql(ctx, locatorSql, "Locator");
            productSql = AddAccessSql(ctx, productSql, "Product");
            costSql = AddAccessSql(ctx, costSql, "Cost");
            costSql += @"
                GROUP BY Cost.AD_Org_ID,
                         Cost.M_Product_ID,
                         COALESCE(Cost.M_Warehouse_ID,0),
                         COALESCE(Cost.M_AttributeSetInstance_ID,0)";

            string sql = string.Format(@"
                WITH StorageRows AS (
                    {0}
                ),
                LocatorRows AS (
                    {1}
                ),
                Products AS (
                    {2}
                ),
                CostValues AS (
                    {3}
                ),
                ValuedStock AS (
                    SELECT StorageRows.M_Product_ID,
                           StorageRows.QtyOnHand,
                           StorageRows.QtyOnHand * COALESCE(
                               OrgExact.Unit_Cost,
                               OrgWarehouse.Unit_Cost,
                               OrgAttribute.Unit_Cost,
                               OrgProduct.Unit_Cost,
                               ClientExact.Unit_Cost,
                               ClientWarehouse.Unit_Cost,
                               ClientAttribute.Unit_Cost,
                               ClientProduct.Unit_Cost,
                               0
                           ) AS Line_Value
                    FROM StorageRows
                    INNER JOIN LocatorRows ON (LocatorRows.M_Locator_ID=StorageRows.M_Locator_ID)
                    LEFT OUTER JOIN CostValues OrgExact ON (OrgExact.AD_Org_ID=StorageRows.AD_Org_ID AND OrgExact.M_Product_ID=StorageRows.M_Product_ID AND OrgExact.M_Warehouse_ID=LocatorRows.M_Warehouse_ID AND OrgExact.M_AttributeSetInstance_ID=StorageRows.M_AttributeSetInstance_ID)
                    LEFT OUTER JOIN CostValues OrgWarehouse ON (OrgWarehouse.AD_Org_ID=StorageRows.AD_Org_ID AND OrgWarehouse.M_Product_ID=StorageRows.M_Product_ID AND OrgWarehouse.M_Warehouse_ID=LocatorRows.M_Warehouse_ID AND OrgWarehouse.M_AttributeSetInstance_ID=0)
                    LEFT OUTER JOIN CostValues OrgAttribute ON (OrgAttribute.AD_Org_ID=StorageRows.AD_Org_ID AND OrgAttribute.M_Product_ID=StorageRows.M_Product_ID AND OrgAttribute.M_Warehouse_ID=0 AND OrgAttribute.M_AttributeSetInstance_ID=StorageRows.M_AttributeSetInstance_ID)
                    LEFT OUTER JOIN CostValues OrgProduct ON (OrgProduct.AD_Org_ID=StorageRows.AD_Org_ID AND OrgProduct.M_Product_ID=StorageRows.M_Product_ID AND OrgProduct.M_Warehouse_ID=0 AND OrgProduct.M_AttributeSetInstance_ID=0)
                    LEFT OUTER JOIN CostValues ClientExact ON (ClientExact.AD_Org_ID=0 AND ClientExact.M_Product_ID=StorageRows.M_Product_ID AND ClientExact.M_Warehouse_ID=LocatorRows.M_Warehouse_ID AND ClientExact.M_AttributeSetInstance_ID=StorageRows.M_AttributeSetInstance_ID)
                    LEFT OUTER JOIN CostValues ClientWarehouse ON (ClientWarehouse.AD_Org_ID=0 AND ClientWarehouse.M_Product_ID=StorageRows.M_Product_ID AND ClientWarehouse.M_Warehouse_ID=LocatorRows.M_Warehouse_ID AND ClientWarehouse.M_AttributeSetInstance_ID=0)
                    LEFT OUTER JOIN CostValues ClientAttribute ON (ClientAttribute.AD_Org_ID=0 AND ClientAttribute.M_Product_ID=StorageRows.M_Product_ID AND ClientAttribute.M_Warehouse_ID=0 AND ClientAttribute.M_AttributeSetInstance_ID=StorageRows.M_AttributeSetInstance_ID)
                    LEFT OUTER JOIN CostValues ClientProduct ON (ClientProduct.AD_Org_ID=0 AND ClientProduct.M_Product_ID=StorageRows.M_Product_ID AND ClientProduct.M_Warehouse_ID=0 AND ClientProduct.M_AttributeSetInstance_ID=0)
                ),
                StockAgg AS (
                    SELECT ValuedStock.M_Product_ID,
                           SUM(COALESCE(ValuedStock.QtyOnHand,0)) AS On_Hand,
                           SUM(COALESCE(ValuedStock.Line_Value,0)) AS Stock_Value
                    FROM ValuedStock
                    GROUP BY ValuedStock.M_Product_ID
                )
                SELECT Products.Product_Code,
                       Products.Product_Name,
                       COALESCE(StockAgg.On_Hand,0) AS On_Hand,
                       COALESCE(StockAgg.Stock_Value,0) AS Stock_Value
                FROM Products
                LEFT OUTER JOIN StockAgg ON (StockAgg.M_Product_ID=Products.M_Product_ID)
                ORDER BY Products.Product_Name",
                storageSql,
                locatorSql,
                productSql,
                costSql
            );

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@Storage_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@Storage_Org_ID", ctx.GetAD_Org_ID()),
                new SqlParameter("@Locator_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@Locator_Org_ID", ctx.GetAD_Org_ID()),
                new SqlParameter("@Category_ID", categoryId),
                new SqlParameter("@Product_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@Product_Org_ID", ctx.GetAD_Org_ID()),
                new SqlParameter("@Element_Costing_Method", SqlDbType.VarChar) { Value = currency.CostingMethod },
                new SqlParameter("@C_AcctSchema_ID", currency.AcctSchemaId),
                new SqlParameter("@M_CostType_ID", currency.CostTypeId),
                new SqlParameter("@Cost_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@Cost_Org_ID", ctx.GetAD_Org_ID()),
                new SqlParameter("@Filter_Costing_Method", SqlDbType.VarChar) { Value = currency.CostingMethod },
                new SqlParameter("@M_CostElement_ID", currency.CostElementId)
            };

            IDataReader reader = null;
            try
            {
                reader = DB.ExecuteReader(sql, parameters);
                while (reader != null && reader.Read())
                {
                    result.rows.Add(new CategoryItem
                    {
                        sku = Util.GetValueOfString(reader["Product_Code"]),
                        name = Util.GetValueOfString(reader["Product_Name"]),
                        on_hand = Util.GetValueOfDecimal(reader["On_Hand"]),
                        stock_value = Util.GetValueOfDecimal(reader["Stock_Value"])
                    });
                }
            }
            finally
            {
                CloseReader(reader);
            }

            return result;
        }

        /// <summary>
        /// Gets the tenant accounting-schema currency, cost type / element and
        /// standard precision (same source as VAS_079).
        /// </summary>
        private SchemaCurrency GetSchemaCurrency(Ctx ctx)
        {
            SchemaCurrency currency = new SchemaCurrency();
            if (ctx == null) { return currency; }

            string sql = @"
                SELECT ClientInfo.C_AcctSchema1_ID AS C_AcctSchema_ID,
                       AcctSchema.C_Currency_ID,
                       AcctSchema.M_CostType_ID,
                       AcctSchema.M_CostElement_ID,
                       COALESCE(NULLIF(AcctSchema.CostingMethod,''),'S') AS Costing_Method,
                       Currency.StdPrecision,
                       CASE
                           WHEN Currency.CurSymbol IS NOT NULL THEN Currency.CurSymbol
                           ELSE Currency.ISO_Code
                       END AS Currency_Symbol,
                       Currency.ISO_Code AS Currency_ISO
                FROM AD_ClientInfo ClientInfo
                INNER JOIN C_AcctSchema AcctSchema ON (AcctSchema.C_AcctSchema_ID=ClientInfo.C_AcctSchema1_ID AND AcctSchema.IsActive='Y')
                INNER JOIN C_Currency Currency ON (Currency.C_Currency_ID=AcctSchema.C_Currency_ID AND Currency.IsActive='Y')
                WHERE ClientInfo.IsActive='Y'
                  AND ClientInfo.AD_Client_ID=@AD_Client_ID";

            sql = AddAccessSql(ctx, sql, "ClientInfo");

            IDataReader reader = null;
            try
            {
                reader = DB.ExecuteReader(
                    sql,
                    new SqlParameter[] { new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()) }
                );

                if (reader == null || !reader.Read()) { return currency; }

                currency.AcctSchemaId = Util.GetValueOfInt(reader["C_AcctSchema_ID"]);
                currency.CurrencyId = Util.GetValueOfInt(reader["C_Currency_ID"]);
                currency.CostTypeId = Util.GetValueOfInt(reader["M_CostType_ID"]);
                currency.CostElementId = Util.GetValueOfInt(reader["M_CostElement_ID"]);
                currency.CostingMethod = Util.GetValueOfString(reader["Costing_Method"]);
                currency.Symbol = Util.GetValueOfString(reader["Currency_Symbol"]);
                currency.IsoCode = Util.GetValueOfString(reader["Currency_ISO"]);
                currency.StdPrecision = Util.GetValueOfInt(reader["StdPrecision"]);
                return currency;
            }
            finally
            {
                CloseReader(reader);
            }
        }

        private string AddAccessSql(Ctx ctx, string sql, string tableAlias)
        {
            return MRole.GetDefault(ctx).AddAccessSQL(
                sql,
                tableAlias,
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );
        }

        private void CloseReader(IDataReader reader)
        {
            if (reader == null) { return; }
            reader.Close();
            reader.Dispose();
        }

        /// <summary>Category Mix response.</summary>
        private class CategoryMixResult
        {
            public int total_active_items { get; set; }
            public List<CategoryShare> rows { get; set; }
        }

        /// <summary>One category's active-item share.</summary>
        private class CategoryShare
        {
            public int category_id { get; set; }
            public string name { get; set; }
            public int active_items { get; set; }
        }

        /// <summary>Category drill-in response.</summary>
        private class CategoryItemsResult
        {
            public string currency_symbol { get; set; }
            public string currency_iso { get; set; }
            public int std_precision { get; set; }
            public List<CategoryItem> rows { get; set; }
        }

        /// <summary>One item in a category's drill-in list.</summary>
        private class CategoryItem
        {
            public string sku { get; set; }
            public string name { get; set; }
            public decimal on_hand { get; set; }
            public decimal stock_value { get; set; }
        }

        /// <summary>Schema currency and costing metadata.</summary>
        private class SchemaCurrency
        {
            public int AcctSchemaId { get; set; }
            public int CurrencyId { get; set; }
            public int CostTypeId { get; set; }
            public int CostElementId { get; set; }
            public string CostingMethod { get; set; }
            public string Symbol { get; set; }
            public string IsoCode { get; set; }
            public int StdPrecision { get; set; }
        }
    }
}
