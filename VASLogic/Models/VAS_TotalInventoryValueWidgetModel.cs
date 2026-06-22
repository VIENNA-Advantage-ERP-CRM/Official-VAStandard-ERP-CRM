using System.Data;
using System.Data.SqlClient;
using VAdvantage.DataBase;
using VAdvantage.Model;
using VAdvantage.Utility;

namespace VASLogic.Models
{
    /// <summary>
    /// Module Name : VAS_TotalInventoryValueWidget
    /// Purpose     : Loads the schema-currency carrying value of active on-hand stock.
    /// Chronological development:
    ///   VAI154      2026-06-21 Created
    ///   VAI154      2026-06-22 Updated for schema currency
    /// </summary>
    public class VAS_TotalInventoryValueWidgetModel
    {
        /// <summary>
        /// Loads the accumulated inventory value and schema currency configuration.
        /// </summary>
        /// <param name="ctx">Current session context.</param>
        /// <returns>Total value in the primary accounting-schema currency.</returns>
        public VAS_TotalInventoryValueResult GetTotalInventoryValue(Ctx ctx)
        {
            VAS_TotalInventoryValueResult result = new VAS_TotalInventoryValueResult();
            if (ctx == null) { return result; }

            VAS_InventorySchemaCurrency currency = VAS_InventoryWidgetSupport.GetSchemaCurrency(ctx);
            result.CurrencySymbol = currency.Symbol;
            result.CurrencyIso = currency.IsoCode;
            result.StdPrecision = currency.StdPrecision;

            if (currency.AcctSchemaId <= 0) { return result; }

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

            result.TotalInventoryValue = Util.GetValueOfDecimal(DB.ExecuteScalar(sql, parameters, null));
            return result;
        }
    }

    /// <summary>
    /// Module Name : VAS_TotalInventoryValueWidget
    /// Purpose     : Inventory value KPI response.
    /// Chronological development:
    ///   VAI154      2026-06-22 Created
    /// </summary>
    public class VAS_TotalInventoryValueResult
    {
        public decimal TotalInventoryValue { get; set; }
        public string CurrencySymbol { get; set; }
        public string CurrencyIso { get; set; }
        public int StdPrecision { get; set; }
    }
}
