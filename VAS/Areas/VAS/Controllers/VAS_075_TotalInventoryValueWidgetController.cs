using Newtonsoft.Json;
using System;
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
    /// Module Name : VAS_075_TotalInventoryValueWidget
    /// Purpose     : Supplies schema-currency inventory valuation.
    /// Chronological development:
    ///   VAI154      2026-06-21 Created
    ///   VAI154      2026-06-22 Updated for schema currency
    /// </summary>
    public class VAS_075_TotalInventoryValueWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_075_TotalInventoryValueWidgetController).FullName);

        /// <summary>
        /// Returns inventory value and schema currency formatting metadata.
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetTotalInventoryValue()
        {
            if (Session["ctx"] == null)
            {
                return Json("", JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            try
            {
                SchemaCurrency currency = GetSchemaCurrency(ctx);
                decimal totalValue = GetTotalInventoryValueData(ctx, currency);
                string json = JsonConvert.SerializeObject(new
                {
                    total_inventory_value = totalValue,
                    currency_symbol = currency.Symbol,
                    currency_iso = currency.IsoCode,
                    std_precision = currency.StdPrecision
                });
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_075_TotalInventoryValueWidget.GetTotalInventoryValue", ex);
                string json = JsonConvert.SerializeObject(new { error = Msg.GetMsg(ctx, "Error") ?? "Error" });
                return Json(json, JsonRequestBehavior.AllowGet);
            }
        }

        private decimal GetTotalInventoryValueData(Ctx ctx, SchemaCurrency currency)
        {
            if (ctx == null || currency.AcctSchemaId <= 0) { return 0; }

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

            storageSql = MRole.GetDefault(ctx).AddAccessSQL(
                storageSql,
                "Storage",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            string locatorSql = @"
                SELECT Locator.M_Locator_ID,
                       Locator.M_Warehouse_ID
                FROM M_Locator Locator
                WHERE Locator.IsActive=N'Y'
                  AND Locator.AD_Client_ID=@Locator_Client_ID
                  AND Locator.AD_Org_ID IN (0,COALESCE(NULLIF(@Locator_Org_ID,0),Locator.AD_Org_ID))";

            locatorSql = MRole.GetDefault(ctx).AddAccessSQL(
                locatorSql,
                "Locator",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

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
                    AND CostElement.CostingMethod=COALESCE(NULLIF(CostCategory.CostingMethod,''),@Costing_Method)
                )
                WHERE Cost.IsActive=N'Y'
                  AND Cost.C_AcctSchema_ID=@C_AcctSchema_ID
                  AND Cost.M_CostType_ID=@M_CostType_ID
                  AND Cost.AD_Client_ID=@Cost_Client_ID
                  AND Cost.AD_Org_ID IN (0,COALESCE(NULLIF(@Cost_Org_ID,0),Cost.AD_Org_ID))
                  AND (
                      COALESCE(NULLIF(CostCategory.CostingMethod,''),@Costing_Method)<>'C'
                      OR Cost.M_CostElement_ID=COALESCE(NULLIF(CostCategory.M_CostElement_ID,0),@M_CostElement_ID)
                  )";

            costSql = MRole.GetDefault(ctx).AddAccessSQL(
                costSql,
                "Cost",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            costSql += @"
                GROUP BY Cost.AD_Org_ID,
                         Cost.M_Product_ID,
                         COALESCE(Cost.M_Warehouse_ID,0),
                         COALESCE(Cost.M_AttributeSetInstance_ID,0)";

            string sql = @"
                WITH StorageRows AS (
                    " + storageSql + @"
                ),
                LocatorRows AS (
                    " + locatorSql + @"
                ),
                CostValues AS (
                    " + costSql + @"
                ),
                StorageValues AS (
                    SELECT StorageRows.AD_Org_ID,
                           StorageRows.M_Product_ID,
                           LocatorRows.M_Warehouse_ID,
                           StorageRows.M_AttributeSetInstance_ID,
                           StorageRows.QtyOnHand
                    FROM StorageRows
                    INNER JOIN LocatorRows ON (LocatorRows.M_Locator_ID=StorageRows.M_Locator_ID)
                )
                SELECT COALESCE(
                           SUM(
                               COALESCE(StorageValues.QtyOnHand,0)
                               * COALESCE(
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
                           ),
                           0
                       ) AS Total_Inventory_Value
                FROM StorageValues
                LEFT OUTER JOIN CostValues OrgExact ON (OrgExact.AD_Org_ID=StorageValues.AD_Org_ID AND OrgExact.M_Product_ID=StorageValues.M_Product_ID AND OrgExact.M_Warehouse_ID=StorageValues.M_Warehouse_ID AND OrgExact.M_AttributeSetInstance_ID=StorageValues.M_AttributeSetInstance_ID)
                LEFT OUTER JOIN CostValues OrgWarehouse ON (OrgWarehouse.AD_Org_ID=StorageValues.AD_Org_ID AND OrgWarehouse.M_Product_ID=StorageValues.M_Product_ID AND OrgWarehouse.M_Warehouse_ID=StorageValues.M_Warehouse_ID AND OrgWarehouse.M_AttributeSetInstance_ID=0)
                LEFT OUTER JOIN CostValues OrgAttribute ON (OrgAttribute.AD_Org_ID=StorageValues.AD_Org_ID AND OrgAttribute.M_Product_ID=StorageValues.M_Product_ID AND OrgAttribute.M_Warehouse_ID=0 AND OrgAttribute.M_AttributeSetInstance_ID=StorageValues.M_AttributeSetInstance_ID)
                LEFT OUTER JOIN CostValues OrgProduct ON (OrgProduct.AD_Org_ID=StorageValues.AD_Org_ID AND OrgProduct.M_Product_ID=StorageValues.M_Product_ID AND OrgProduct.M_Warehouse_ID=0 AND OrgProduct.M_AttributeSetInstance_ID=0)
                LEFT OUTER JOIN CostValues ClientExact ON (ClientExact.AD_Org_ID=0 AND ClientExact.M_Product_ID=StorageValues.M_Product_ID AND ClientExact.M_Warehouse_ID=StorageValues.M_Warehouse_ID AND ClientExact.M_AttributeSetInstance_ID=StorageValues.M_AttributeSetInstance_ID)
                LEFT OUTER JOIN CostValues ClientWarehouse ON (ClientWarehouse.AD_Org_ID=0 AND ClientWarehouse.M_Product_ID=StorageValues.M_Product_ID AND ClientWarehouse.M_Warehouse_ID=StorageValues.M_Warehouse_ID AND ClientWarehouse.M_AttributeSetInstance_ID=0)
                LEFT OUTER JOIN CostValues ClientAttribute ON (ClientAttribute.AD_Org_ID=0 AND ClientAttribute.M_Product_ID=StorageValues.M_Product_ID AND ClientAttribute.M_Warehouse_ID=0 AND ClientAttribute.M_AttributeSetInstance_ID=StorageValues.M_AttributeSetInstance_ID)
                LEFT OUTER JOIN CostValues ClientProduct ON (ClientProduct.AD_Org_ID=0 AND ClientProduct.M_Product_ID=StorageValues.M_Product_ID AND ClientProduct.M_Warehouse_ID=0 AND ClientProduct.M_AttributeSetInstance_ID=0)";

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@Storage_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@Storage_Org_ID", ctx.GetAD_Org_ID()),
                new SqlParameter("@Locator_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@Locator_Org_ID", ctx.GetAD_Org_ID()),
                new SqlParameter("@C_AcctSchema_ID", currency.AcctSchemaId),
                new SqlParameter("@M_CostType_ID", currency.CostTypeId),
                new SqlParameter("@M_CostElement_ID", currency.CostElementId),
                new SqlParameter("@Costing_Method", SqlDbType.VarChar) { Value = currency.CostingMethod },
                new SqlParameter("@Cost_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@Cost_Org_ID", ctx.GetAD_Org_ID())
            };

            return Util.GetValueOfDecimal(DB.ExecuteScalar(sql, parameters, null));
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
                if (reader != null)
                {
                    reader.Close();
                    reader.Dispose();
                }
            }
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
    }
}
