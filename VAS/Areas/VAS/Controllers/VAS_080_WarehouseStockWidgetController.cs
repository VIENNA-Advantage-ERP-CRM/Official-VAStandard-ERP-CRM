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
    /// Module Name : VAS_080_WarehouseStockWidget
    /// Purpose     : Supplies warehouse and locator stock-ageing data.
    /// Chronological development:
    ///   VAI154      2026-06-21 Created
    ///   VAI154      2026-06-22 Updated for schema currency
    ///   VAI154      2026-06-24 Fixed ORA-12704: replaced 'Y'/'N' with 'Y'/'N' in Oracle CHAR comparisons
    /// </summary>
    public class VAS_080_WarehouseStockWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_080_WarehouseStockWidgetController).FullName);

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

        /// <summary>Returns secured locator stock and schema currency.</summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetStockRows()
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null) { return Json("", JsonRequestBehavior.AllowGet); }

            try
            {
                return Json(JsonConvert.SerializeObject(GetStockRowsData(ctx)), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return ErrorResult(ctx, "GetStockRows", ex);
            }
        }

        private List<WarehouseRow> GetWarehousesData(Ctx ctx)
        {
            List<WarehouseRow> warehouses = new List<WarehouseRow>();
            if (ctx == null) { return warehouses; }

            string sql = @"
                SELECT Warehouse.M_Warehouse_ID AS Warehouse_ID,
                       Warehouse.Value AS Warehouse_Code,
                       Warehouse.Name AS Warehouse_Name
                FROM M_Warehouse Warehouse
                WHERE Warehouse.IsActive='Y'
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
                    warehouses.Add(new WarehouseRow
                    {
                        warehouse_id = Util.GetValueOfInt(reader["Warehouse_ID"]),
                        warehouse_code = Util.GetValueOfString(reader["Warehouse_Code"]),
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

        private StockResult GetStockRowsData(Ctx ctx)
        {
            StockResult result = new StockResult
            {
                rows = new List<StockRow>()
            };
            if (ctx == null) { return result; }

            SchemaCurrency currency = GetSchemaCurrency(ctx);
            if (string.IsNullOrEmpty(currency.CostingMethod))
            {
                // GetSchemaCurrency returned no data; default so CHAR parameters are never null
                currency.CostingMethod = "S";
                Log.Log(Level.WARNING, "VAS_080_WarehouseStockWidget.GetStockRowsData: GetSchemaCurrency returned no data for client " + ctx.GetAD_Client_ID());
            }
            result.currency_symbol = currency.Symbol;
            result.currency_iso = currency.IsoCode;
            result.std_precision = currency.StdPrecision;

            string warehouseSql = @"
                SELECT Warehouse.M_Warehouse_ID,
                       Warehouse.Value,
                       Warehouse.Name
                FROM M_Warehouse Warehouse
                WHERE Warehouse.IsActive='Y'
                  AND Warehouse.AD_Client_ID=@Warehouse_Client_ID
                  AND Warehouse.AD_Org_ID IN (0,COALESCE(NULLIF(@Warehouse_Org_ID,0),Warehouse.AD_Org_ID))";

            string locatorSql = @"
                SELECT Locator.M_Locator_ID,
                       Locator.M_Warehouse_ID,
                       Locator.Value,
                       Locator.LocatorCombination,
                       Locator.Bin
                FROM M_Locator Locator
                WHERE Locator.IsActive='Y'
                  AND Locator.AD_Client_ID=@Locator_Client_ID
                  AND Locator.AD_Org_ID IN (0,COALESCE(NULLIF(@Locator_Org_ID,0),Locator.AD_Org_ID))";

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

            string costSql = @"
                SELECT Cost.AD_Org_ID,
                       COALESCE(Cost.M_Warehouse_ID,0) AS M_Warehouse_ID,
                       Cost.M_Product_ID,
                       COALESCE(Cost.M_AttributeSetInstance_ID,0) AS M_AttributeSetInstance_ID,
                       SUM(COALESCE(Cost.CurrentCostPrice,0)) AS Unit_Cost
                FROM M_Cost Cost
                INNER JOIN M_Product CostProduct ON (CostProduct.M_Product_ID=Cost.M_Product_ID AND CostProduct.IsActive='Y')
                LEFT OUTER JOIN M_Product_Category CostCategory ON (CostCategory.M_Product_Category_ID=CostProduct.M_Product_Category_ID AND CostCategory.IsActive='Y')
                INNER JOIN M_CostElement CostElement ON (
                    CostElement.M_CostElement_ID=Cost.M_CostElement_ID
                    AND CostElement.IsActive='Y'
                    AND CostElement.CostingMethod=COALESCE(NULLIF(CostCategory.CostingMethod,''),@Costing_Method)
                )
                WHERE Cost.IsActive='Y'
                  AND Cost.C_AcctSchema_ID=@C_AcctSchema_ID
                  AND Cost.M_CostType_ID=@M_CostType_ID
                  AND Cost.AD_Client_ID=@Cost_Client_ID
                  AND Cost.AD_Org_ID IN (0,COALESCE(NULLIF(@Cost_Org_ID,0),Cost.AD_Org_ID))
                  AND (
                      COALESCE(NULLIF(CostCategory.CostingMethod,''),@Costing_Method2)<>'C'
                      OR Cost.M_CostElement_ID=COALESCE(NULLIF(CostCategory.M_CostElement_ID,0),@M_CostElement_ID)
                  )";

            string movementSql = @"
                SELECT Movement.M_Locator_ID,
                       Movement.M_Product_ID,
                       COALESCE(Movement.M_AttributeSetInstance_ID,0) AS M_AttributeSetInstance_ID,
                       Movement.MovementDate
                FROM M_Transaction Movement
                WHERE Movement.IsActive='Y'
                  AND COALESCE(CAST(Movement.IsReversed AS VARCHAR(1)),'N')='N'
                  AND Movement.AD_Client_ID=@Movement_Client_ID
                  AND Movement.AD_Org_ID IN (0,COALESCE(NULLIF(@Movement_Org_ID,0),Movement.AD_Org_ID))";

            warehouseSql = AddAccessSql(ctx, warehouseSql, "Warehouse");
            locatorSql = AddAccessSql(ctx, locatorSql, "Locator");
            storageSql = AddAccessSql(ctx, storageSql, "Storage");
            costSql = AddAccessSql(ctx, costSql, "Cost");
            movementSql = AddAccessSql(ctx, movementSql, "Movement");
            costSql += @"
                GROUP BY Cost.AD_Org_ID,
                         COALESCE(Cost.M_Warehouse_ID,0),
                         Cost.M_Product_ID,
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
                CostRows AS (
                    " + costSql + @"
                ),
                MovementRows AS (
                    " + movementSql + @"
                ),
                ProductStock AS (
                    SELECT StorageRows.AD_Org_ID AS Org_ID,
                           WarehouseRows.M_Warehouse_ID AS Warehouse_ID,
                           WarehouseRows.Value AS Warehouse_Code,
                           WarehouseRows.Name AS Warehouse_Name,
                           LocatorRows.M_Locator_ID AS Locator_ID,
                           LocatorRows.Value AS Locator_Code,
                           COALESCE(CAST(LocatorRows.LocatorCombination AS VARCHAR(255)),CAST(LocatorRows.Bin AS VARCHAR(255)),CAST(LocatorRows.Value AS VARCHAR(255))) AS Locator_Name,
                           StorageRows.M_Product_ID AS Product_ID,
                           StorageRows.M_AttributeSetInstance_ID AS ASI_ID,
                           SUM(COALESCE(StorageRows.QtyOnHand,0)) AS Qty_On_Hand
                    FROM StorageRows
                    INNER JOIN LocatorRows ON (LocatorRows.M_Locator_ID=StorageRows.M_Locator_ID)
                    INNER JOIN WarehouseRows ON (WarehouseRows.M_Warehouse_ID=LocatorRows.M_Warehouse_ID)
                    GROUP BY StorageRows.AD_Org_ID,
                             WarehouseRows.M_Warehouse_ID,
                             WarehouseRows.Value,
                             WarehouseRows.Name,
                             LocatorRows.M_Locator_ID,
                             LocatorRows.Value,
                             COALESCE(CAST(LocatorRows.LocatorCombination AS VARCHAR(255)),CAST(LocatorRows.Bin AS VARCHAR(255)),CAST(LocatorRows.Value AS VARCHAR(255))),
                             StorageRows.M_Product_ID,
                             StorageRows.M_AttributeSetInstance_ID
                    HAVING SUM(COALESCE(StorageRows.QtyOnHand,0))<>0
                ),
                LastMove AS (
                    SELECT MovementRows.M_Locator_ID AS Locator_ID,
                           MovementRows.M_Product_ID AS Product_ID,
                           MovementRows.M_AttributeSetInstance_ID AS ASI_ID,
                           MAX(MovementRows.MovementDate) AS Last_Movement_Date
                    FROM MovementRows
                    GROUP BY MovementRows.M_Locator_ID,
                             MovementRows.M_Product_ID,
                             MovementRows.M_AttributeSetInstance_ID
                ),
                LineValues AS (
                    SELECT ProductStock.Warehouse_ID,
                           ProductStock.Warehouse_Code,
                           ProductStock.Warehouse_Name,
                           ProductStock.Locator_ID,
                           ProductStock.Locator_Code,
                           ProductStock.Locator_Name,
                           ProductStock.Qty_On_Hand,
                           ProductStock.Qty_On_Hand * COALESCE(
                               OrgExact.Unit_Cost,
                               OrgWarehouse.Unit_Cost,
                               OrgAttribute.Unit_Cost,
                               OrgProduct.Unit_Cost,
                               ClientExact.Unit_Cost,
                               ClientWarehouse.Unit_Cost,
                               ClientAttribute.Unit_Cost,
                               ClientProduct.Unit_Cost,
                               0
                           ) AS Stock_Value,
                           COALESCE(
                               CAST(CURRENT_DATE AS DATE)-CAST(LastMove.Last_Movement_Date AS DATE),
                               99999
                           ) AS Age_Days
                    FROM ProductStock
                    LEFT OUTER JOIN CostRows OrgExact ON (OrgExact.AD_Org_ID=ProductStock.Org_ID AND OrgExact.M_Product_ID=ProductStock.Product_ID AND OrgExact.M_Warehouse_ID=ProductStock.Warehouse_ID AND OrgExact.M_AttributeSetInstance_ID=ProductStock.ASI_ID)
                    LEFT OUTER JOIN CostRows OrgWarehouse ON (OrgWarehouse.AD_Org_ID=ProductStock.Org_ID AND OrgWarehouse.M_Product_ID=ProductStock.Product_ID AND OrgWarehouse.M_Warehouse_ID=ProductStock.Warehouse_ID AND OrgWarehouse.M_AttributeSetInstance_ID=0)
                    LEFT OUTER JOIN CostRows OrgAttribute ON (OrgAttribute.AD_Org_ID=ProductStock.Org_ID AND OrgAttribute.M_Product_ID=ProductStock.Product_ID AND OrgAttribute.M_Warehouse_ID=0 AND OrgAttribute.M_AttributeSetInstance_ID=ProductStock.ASI_ID)
                    LEFT OUTER JOIN CostRows OrgProduct ON (OrgProduct.AD_Org_ID=ProductStock.Org_ID AND OrgProduct.M_Product_ID=ProductStock.Product_ID AND OrgProduct.M_Warehouse_ID=0 AND OrgProduct.M_AttributeSetInstance_ID=0)
                    LEFT OUTER JOIN CostRows ClientExact ON (ClientExact.AD_Org_ID=0 AND ClientExact.M_Product_ID=ProductStock.Product_ID AND ClientExact.M_Warehouse_ID=ProductStock.Warehouse_ID AND ClientExact.M_AttributeSetInstance_ID=ProductStock.ASI_ID)
                    LEFT OUTER JOIN CostRows ClientWarehouse ON (ClientWarehouse.AD_Org_ID=0 AND ClientWarehouse.M_Product_ID=ProductStock.Product_ID AND ClientWarehouse.M_Warehouse_ID=ProductStock.Warehouse_ID AND ClientWarehouse.M_AttributeSetInstance_ID=0)
                    LEFT OUTER JOIN CostRows ClientAttribute ON (ClientAttribute.AD_Org_ID=0 AND ClientAttribute.M_Product_ID=ProductStock.Product_ID AND ClientAttribute.M_Warehouse_ID=0 AND ClientAttribute.M_AttributeSetInstance_ID=ProductStock.ASI_ID)
                    LEFT OUTER JOIN CostRows ClientProduct ON (ClientProduct.AD_Org_ID=0 AND ClientProduct.M_Product_ID=ProductStock.Product_ID AND ClientProduct.M_Warehouse_ID=0 AND ClientProduct.M_AttributeSetInstance_ID=0)
                    LEFT OUTER JOIN LastMove ON (LastMove.Locator_ID=ProductStock.Locator_ID AND LastMove.Product_ID=ProductStock.Product_ID AND LastMove.ASI_ID=ProductStock.ASI_ID)
                )
                SELECT LineValues.Warehouse_ID,
                       LineValues.Warehouse_Code,
                       LineValues.Warehouse_Name,
                       LineValues.Locator_ID,
                       LineValues.Locator_Code,
                       LineValues.Locator_Name,
                       SUM(LineValues.Qty_On_Hand) AS Total_Qty,
                       SUM(LineValues.Stock_Value) AS Total_Value,
                       SUM(CASE WHEN LineValues.Age_Days>=0 AND LineValues.Age_Days<=30 THEN LineValues.Stock_Value ELSE 0 END) AS Value_0_30,
                       SUM(CASE WHEN LineValues.Age_Days>30 AND LineValues.Age_Days<=90 THEN LineValues.Stock_Value ELSE 0 END) AS Value_31_90,
                       SUM(CASE WHEN LineValues.Age_Days>90 AND LineValues.Age_Days<=365 THEN LineValues.Stock_Value ELSE 0 END) AS Value_91_365,
                       SUM(CASE WHEN LineValues.Age_Days>365 THEN LineValues.Stock_Value ELSE 0 END) AS Value_Over_365
                FROM LineValues
                GROUP BY LineValues.Warehouse_ID,
                         LineValues.Warehouse_Code,
                         LineValues.Warehouse_Name,
                         LineValues.Locator_ID,
                         LineValues.Locator_Code,
                         LineValues.Locator_Name
                ORDER BY LineValues.Warehouse_Name,
                         LineValues.Locator_Code";

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@Warehouse_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@Warehouse_Org_ID", ctx.GetAD_Org_ID()),
                new SqlParameter("@Locator_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@Locator_Org_ID", ctx.GetAD_Org_ID()),
                new SqlParameter("@Storage_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@Storage_Org_ID", ctx.GetAD_Org_ID()),
                new SqlParameter("@Costing_Method", SqlDbType.VarChar) { Value = currency.CostingMethod },
                new SqlParameter("@C_AcctSchema_ID", currency.AcctSchemaId),
                new SqlParameter("@M_CostType_ID", currency.CostTypeId),
                new SqlParameter("@Cost_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@Cost_Org_ID", ctx.GetAD_Org_ID()),
                new SqlParameter("@Costing_Method2", SqlDbType.VarChar) { Value = currency.CostingMethod },
                new SqlParameter("@M_CostElement_ID", currency.CostElementId),
                new SqlParameter("@Movement_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@Movement_Org_ID", ctx.GetAD_Org_ID())
            };

            IDataReader reader = null;
            try
            {
                reader = DB.ExecuteReader(sql, parameters);
                while (reader != null && reader.Read())
                {
                    result.rows.Add(new StockRow
                    {
                        warehouse_id = Util.GetValueOfInt(reader["Warehouse_ID"]),
                        warehouse_code = Util.GetValueOfString(reader["Warehouse_Code"]),
                        warehouse_name = Util.GetValueOfString(reader["Warehouse_Name"]),
                        locator_id = Util.GetValueOfInt(reader["Locator_ID"]),
                        locator_code = Util.GetValueOfString(reader["Locator_Code"]),
                        locator_name = Util.GetValueOfString(reader["Locator_Name"]),
                        total_qty = Util.GetValueOfDecimal(reader["Total_Qty"]),
                        total_value = Util.GetValueOfDecimal(reader["Total_Value"]),
                        value_0_30 = Util.GetValueOfDecimal(reader["Value_0_30"]),
                        value_31_90 = Util.GetValueOfDecimal(reader["Value_31_90"]),
                        value_91_365 = Util.GetValueOfDecimal(reader["Value_91_365"]),
                        value_over_365 = Util.GetValueOfDecimal(reader["Value_Over_365"])
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
                INNER JOIN C_AcctSchema AcctSchema ON (AcctSchema.C_AcctSchema_ID=ClientInfo.C_AcctSchema1_ID AND AcctSchema.IsActive='Y')
                INNER JOIN C_Currency Currency ON (Currency.C_Currency_ID=AcctSchema.C_Currency_ID AND Currency.IsActive='Y')
                WHERE ClientInfo.IsActive='Y'
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
            Log.Log(Level.SEVERE, "VAS_080_WarehouseStockWidget." + action, ex);
            string label = Msg.GetMsg(ctx, "Error");
            if (string.IsNullOrEmpty(label)) { label = "Error"; }
            string json = JsonConvert.SerializeObject(new { error = label, detail = ex.Message });
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

        private class WarehouseRow
        {
            public int warehouse_id { get; set; }
            public string warehouse_code { get; set; }
            public string warehouse_name { get; set; }
        }

        private class StockResult
        {
            public List<StockRow> rows { get; set; }
            public string currency_symbol { get; set; }
            public string currency_iso { get; set; }
            public int std_precision { get; set; }
        }

        private class StockRow
        {
            public int warehouse_id { get; set; }
            public string warehouse_code { get; set; }
            public string warehouse_name { get; set; }
            public int locator_id { get; set; }
            public string locator_code { get; set; }
            public string locator_name { get; set; }
            public decimal total_qty { get; set; }
            public decimal total_value { get; set; }
            public decimal value_0_30 { get; set; }
            public decimal value_31_90 { get; set; }
            public decimal value_91_365 { get; set; }
            public decimal value_over_365 { get; set; }
        }
    }
}
