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
    /// Module Name : VAS_079_TopValueItemsWidget
    /// Purpose     : Supplies warehouse options and paged high-value stock.
    /// Chronological development:
    ///   VAI154      2026-06-21 Created
    ///   VAI154      2026-06-22 Added schema currency and paging
    ///   VAI154      2026-06-23 Fixed Oracle parameter binding
    /// </summary>
    public class VAS_079_TopValueItemsWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_079_TopValueItemsWidgetController).FullName);

        /// <summary>Returns active warehouses available to the current role.</summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetWarehouses()
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null) { return Json("", JsonRequestBehavior.AllowGet); }

            try
            {
                return Json(JsonConvert.SerializeObject(GetWarehousesData(ctx)), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return ErrorResult(ctx, "GetWarehouses", ex);
            }
        }

        /// <summary>Returns one secured page of high-value stock.</summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetTopValueItems(int? warehouseId, int pageNo = 1, int pageSize = 4)
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null) { return Json("", JsonRequestBehavior.AllowGet); }

            try
            {
                return Json(JsonConvert.SerializeObject(GetTopValueItemsData(ctx, warehouseId, pageNo, pageSize)), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return ErrorResult(ctx, "GetTopValueItems", ex);
            }
        }

        private List<TopValueWarehouse> GetWarehousesData(Ctx ctx)
        {
            List<TopValueWarehouse> warehouses = new List<TopValueWarehouse>();
            if (ctx == null) { return warehouses; }

            string sql = @"
                SELECT Warehouse.M_Warehouse_ID AS Warehouse_ID,
                       Warehouse.Name AS Warehouse_Name
                FROM M_Warehouse Warehouse
                WHERE Warehouse.IsActive=N'Y'
                  AND Warehouse.AD_Client_ID=@Warehouse_Client_ID
                  AND Warehouse.AD_Org_ID IN (0,COALESCE(NULLIF(@Warehouse_Org_ID,0),Warehouse.AD_Org_ID))";

            sql = AddAccessSql(ctx, sql, "Warehouse");
            sql += " ORDER BY Warehouse.Name";

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@Warehouse_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@Warehouse_Org_ID", ctx.GetAD_Org_ID())
            };

            IDataReader reader = null;
            try
            {
                reader = DB.ExecuteReader(sql, parameters);
                while (reader != null && reader.Read())
                {
                    warehouses.Add(new TopValueWarehouse
                    {
                        warehouse_id = Util.GetValueOfInt(reader["Warehouse_ID"]),
                        warehouse_name = Util.GetValueOfString(reader["Warehouse_Name"])
                    });
                }
            }
            finally
            {
                CloseReader(reader);
            }

            return warehouses;
        }

        private TopValueResult GetTopValueItemsData(Ctx ctx, int? warehouseId, int pageNo, int pageSize)
        {
            TopValueResult result = new TopValueResult
            {
                items = new List<TopValueItem>()
            };
            if (ctx == null) { return result; }

            if (pageNo < 1) { pageNo = 1; }
            if (pageSize < 1 || pageSize > 20) { pageSize = 4; }

            SchemaCurrency currency = GetSchemaCurrency(ctx);
            result.currency_symbol = currency.Symbol;
            result.currency_iso = currency.IsoCode;
            result.std_precision = currency.StdPrecision;

            string warehouseSql = @"
                SELECT Warehouse.M_Warehouse_ID,
                       Warehouse.Name
                FROM M_Warehouse Warehouse
                WHERE Warehouse.IsActive=N'Y'
                  AND Warehouse.AD_Client_ID=@Warehouse_Client_ID
                  AND Warehouse.AD_Org_ID IN (0,COALESCE(NULLIF(@Warehouse_Org_ID,0),Warehouse.AD_Org_ID))";

            string locatorSql = @"
                SELECT Locator.M_Locator_ID,
                       Locator.M_Warehouse_ID
                FROM M_Locator Locator
                WHERE Locator.IsActive=N'Y'
                  AND Locator.AD_Client_ID=@Locator_Client_ID
                  AND Locator.AD_Org_ID IN (0,COALESCE(NULLIF(@Locator_Org_ID,0),Locator.AD_Org_ID))";

            string storageSql = @"
                SELECT Storage.AD_Org_ID,
                       Storage.M_Locator_ID,
                       Storage.M_Product_ID,
                       COALESCE(Storage.M_AttributeSetInstance_ID,0) AS M_AttributeSetInstance_ID,
                       Storage.QtyOnHand
                FROM M_Storage Storage
                WHERE Storage.IsActive=N'Y'
                  AND Storage.AD_Client_ID=@Storage_Client_ID
                  AND Storage.AD_Org_ID IN (0,COALESCE(NULLIF(@Storage_Org_ID,0),Storage.AD_Org_ID))";

            string productSql = @"
                SELECT Product.M_Product_ID,
                       Product.Name
                FROM M_Product Product
                WHERE Product.IsActive=N'Y'
                  AND Product.ProductType=N'I'
                  AND Product.AD_Client_ID=@Product_Client_ID
                  AND Product.AD_Org_ID IN (0,COALESCE(NULLIF(@Product_Org_ID,0),Product.AD_Org_ID))";

            string costSql = @"
                SELECT Cost.AD_Org_ID,
                       Cost.M_Product_ID,
                       COALESCE(Cost.M_Warehouse_ID,0) AS M_Warehouse_ID,
                       COALESCE(Cost.M_AttributeSetInstance_ID,0) AS M_AttributeSetInstance_ID,
                       SUM(COALESCE(Cost.CurrentCostPrice,0)) AS Unit_Cost
                FROM M_Cost Cost
                INNER JOIN M_Product CostProduct ON (CostProduct.M_Product_ID=Cost.M_Product_ID AND CostProduct.IsActive=N'Y')
                LEFT OUTER JOIN M_Product_Category CostCategory ON (CostCategory.M_Product_Category_ID=CostProduct.M_Product_Category_ID AND CostCategory.IsActive=N'Y')
                INNER JOIN M_CostElement CostElement ON (
                    CostElement.M_CostElement_ID=Cost.M_CostElement_ID
                    AND CostElement.IsActive=N'Y'
                    AND CostElement.CostingMethod=COALESCE(NULLIF(CostCategory.CostingMethod,''),@Element_Costing_Method)
                )
                WHERE Cost.IsActive=N'Y'
                  AND Cost.C_AcctSchema_ID=@C_AcctSchema_ID
                  AND Cost.M_CostType_ID=@M_CostType_ID
                  AND Cost.AD_Client_ID=@Cost_Client_ID
                  AND Cost.AD_Org_ID IN (0,COALESCE(NULLIF(@Cost_Org_ID,0),Cost.AD_Org_ID))
                  AND (
                      COALESCE(NULLIF(CostCategory.CostingMethod,''),@Filter_Costing_Method)<>'C'
                      OR Cost.M_CostElement_ID=COALESCE(NULLIF(CostCategory.M_CostElement_ID,0),@M_CostElement_ID)
                  )";

            warehouseSql = AddAccessSql(ctx, warehouseSql, "Warehouse");
            locatorSql = AddAccessSql(ctx, locatorSql, "Locator");
            storageSql = AddAccessSql(ctx, storageSql, "Storage");
            productSql = AddAccessSql(ctx, productSql, "Product");
            costSql = AddAccessSql(ctx, costSql, "Cost");
            costSql += @"
                GROUP BY Cost.AD_Org_ID,
                         Cost.M_Product_ID,
                         COALESCE(Cost.M_Warehouse_ID,0),
                         COALESCE(Cost.M_AttributeSetInstance_ID,0)";

            string sql = @"
                WITH WarehouseRows AS (
                    " + warehouseSql + @"
                ),
                LocatorRows AS (
                    " + locatorSql + @"
                ),
                StorageRows AS (
                    " + storageSql + @"
                ),
                ProductRows AS (
                    " + productSql + @"
                ),
                CostValues AS (
                    " + costSql + @"
                ),
                AggregatedItems AS (
                SELECT ProductRows.M_Product_ID,
                       ProductRows.Name AS Product_Name,
                       CASE
                            WHEN @Name_Warehouse_ID IS NULL THEN NULL
                            ELSE MAX(WarehouseRows.Name)
                       END AS Warehouse_Name,
                       SUM(StorageRows.QtyOnHand) AS Qty_On_Hand,
                       SUM(
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
                           )
                       ) AS Carrying_Value
                FROM StorageRows
                INNER JOIN ProductRows ON (ProductRows.M_Product_ID=StorageRows.M_Product_ID)
                INNER JOIN LocatorRows ON (LocatorRows.M_Locator_ID=StorageRows.M_Locator_ID)
                INNER JOIN WarehouseRows ON (WarehouseRows.M_Warehouse_ID=LocatorRows.M_Warehouse_ID)
                LEFT OUTER JOIN CostValues OrgExact ON (OrgExact.AD_Org_ID=StorageRows.AD_Org_ID AND OrgExact.M_Product_ID=StorageRows.M_Product_ID AND OrgExact.M_Warehouse_ID=WarehouseRows.M_Warehouse_ID AND OrgExact.M_AttributeSetInstance_ID=StorageRows.M_AttributeSetInstance_ID)
                LEFT OUTER JOIN CostValues OrgWarehouse ON (OrgWarehouse.AD_Org_ID=StorageRows.AD_Org_ID AND OrgWarehouse.M_Product_ID=StorageRows.M_Product_ID AND OrgWarehouse.M_Warehouse_ID=WarehouseRows.M_Warehouse_ID AND OrgWarehouse.M_AttributeSetInstance_ID=0)
                LEFT OUTER JOIN CostValues OrgAttribute ON (OrgAttribute.AD_Org_ID=StorageRows.AD_Org_ID AND OrgAttribute.M_Product_ID=StorageRows.M_Product_ID AND OrgAttribute.M_Warehouse_ID=0 AND OrgAttribute.M_AttributeSetInstance_ID=StorageRows.M_AttributeSetInstance_ID)
                LEFT OUTER JOIN CostValues OrgProduct ON (OrgProduct.AD_Org_ID=StorageRows.AD_Org_ID AND OrgProduct.M_Product_ID=StorageRows.M_Product_ID AND OrgProduct.M_Warehouse_ID=0 AND OrgProduct.M_AttributeSetInstance_ID=0)
                LEFT OUTER JOIN CostValues ClientExact ON (ClientExact.AD_Org_ID=0 AND ClientExact.M_Product_ID=StorageRows.M_Product_ID AND ClientExact.M_Warehouse_ID=WarehouseRows.M_Warehouse_ID AND ClientExact.M_AttributeSetInstance_ID=StorageRows.M_AttributeSetInstance_ID)
                LEFT OUTER JOIN CostValues ClientWarehouse ON (ClientWarehouse.AD_Org_ID=0 AND ClientWarehouse.M_Product_ID=StorageRows.M_Product_ID AND ClientWarehouse.M_Warehouse_ID=WarehouseRows.M_Warehouse_ID AND ClientWarehouse.M_AttributeSetInstance_ID=0)
                LEFT OUTER JOIN CostValues ClientAttribute ON (ClientAttribute.AD_Org_ID=0 AND ClientAttribute.M_Product_ID=StorageRows.M_Product_ID AND ClientAttribute.M_Warehouse_ID=0 AND ClientAttribute.M_AttributeSetInstance_ID=StorageRows.M_AttributeSetInstance_ID)
                LEFT OUTER JOIN CostValues ClientProduct ON (ClientProduct.AD_Org_ID=0 AND ClientProduct.M_Product_ID=StorageRows.M_Product_ID AND ClientProduct.M_Warehouse_ID=0 AND ClientProduct.M_AttributeSetInstance_ID=0)
                WHERE (@Filter_Warehouse_ID IS NULL OR WarehouseRows.M_Warehouse_ID=@Selected_Warehouse_ID)
                 GROUP BY ProductRows.M_Product_ID,
                          ProductRows.Name
                )
                SELECT AggregatedItems.M_Product_ID,
                       AggregatedItems.Product_Name,
                       AggregatedItems.Warehouse_Name,
                       AggregatedItems.Qty_On_Hand,
                       AggregatedItems.Carrying_Value,
                       COUNT(*) OVER() AS Total_Rows
                FROM AggregatedItems
                ORDER BY Carrying_Value DESC
                OFFSET @Offset ROWS FETCH NEXT @Page_Size ROWS ONLY";

            object warehouseValue = warehouseId.HasValue ? (object)warehouseId.Value : DBNull.Value;
            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@Warehouse_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@Warehouse_Org_ID", ctx.GetAD_Org_ID()),
                new SqlParameter("@Locator_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@Locator_Org_ID", ctx.GetAD_Org_ID()),
                new SqlParameter("@Storage_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@Storage_Org_ID", ctx.GetAD_Org_ID()),
                new SqlParameter("@Product_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@Product_Org_ID", ctx.GetAD_Org_ID()),
                new SqlParameter("@Element_Costing_Method", SqlDbType.VarChar) { Value = currency.CostingMethod },
                new SqlParameter("@C_AcctSchema_ID", currency.AcctSchemaId),
                new SqlParameter("@M_CostType_ID", currency.CostTypeId),
                new SqlParameter("@Cost_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@Cost_Org_ID", ctx.GetAD_Org_ID()),
                new SqlParameter("@Filter_Costing_Method", SqlDbType.VarChar) { Value = currency.CostingMethod },
                new SqlParameter("@M_CostElement_ID", currency.CostElementId),
                new SqlParameter("@Name_Warehouse_ID", SqlDbType.Int) { Value = warehouseValue },
                new SqlParameter("@Filter_Warehouse_ID", SqlDbType.Int) { Value = warehouseValue },
                new SqlParameter("@Selected_Warehouse_ID", SqlDbType.Int) { Value = warehouseValue },
                new SqlParameter("@Offset", (pageNo - 1) * pageSize),
                new SqlParameter("@Page_Size", pageSize)
            };

            IDataReader reader = null;
            try
            {
                reader = DB.ExecuteReader(sql, parameters);
                while (reader != null && reader.Read())
                {
                    if (result.total_records == 0)
                    {
                        result.total_records = Util.GetValueOfInt(reader["Total_Rows"]);
                    }

                    result.items.Add(new TopValueItem
                    {
                        product_id = Util.GetValueOfInt(reader["M_Product_ID"]),
                        product_name = Util.GetValueOfString(reader["Product_Name"]),
                        warehouse_name = Util.GetValueOfString(reader["Warehouse_Name"]),
                        qty_on_hand = Util.GetValueOfDecimal(reader["Qty_On_Hand"]),
                        carrying_value = Util.GetValueOfDecimal(reader["Carrying_Value"])
                    });
                }
            }
            finally
            {
                CloseReader(reader);
            }

            return result;
        }

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
                INNER JOIN C_AcctSchema AcctSchema ON (AcctSchema.C_AcctSchema_ID=ClientInfo.C_AcctSchema1_ID AND AcctSchema.IsActive=N'Y')
                INNER JOIN C_Currency Currency ON (Currency.C_Currency_ID=AcctSchema.C_Currency_ID AND Currency.IsActive=N'Y')
                WHERE ClientInfo.IsActive=N'Y'
                  AND ClientInfo.AD_Client_ID=@AD_Client_ID";

            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql,
                "ClientInfo",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

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
                if (reader != null) { reader.Close(); reader.Dispose(); }
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

        private JsonResult ErrorResult(Ctx ctx, string action, Exception ex)
        {
            Log.Log(Level.SEVERE, "VAS_079_TopValueItemsWidget." + action, ex);
            string json = JsonConvert.SerializeObject(new { error = Msg.GetMsg(ctx, "Error") ?? "Error" });
            return Json(json, JsonRequestBehavior.AllowGet);
        }

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

        private class TopValueWarehouse
        {
            public int warehouse_id { get; set; }
            public string warehouse_name { get; set; }
        }

        private class TopValueResult
        {
            public List<TopValueItem> items { get; set; }
            public int total_records { get; set; }
            public string currency_symbol { get; set; }
            public string currency_iso { get; set; }
            public int std_precision { get; set; }
        }

        private class TopValueItem
        {
            public int product_id { get; set; }
            public string product_name { get; set; }
            public string warehouse_name { get; set; }
            public decimal qty_on_hand { get; set; }
            public decimal carrying_value { get; set; }
        }
    }
}
