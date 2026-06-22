using System.Data.SqlClient;
using VAdvantage.DataBase;
using VAdvantage.Model;
using VAdvantage.Utility;

namespace VASLogic.Models
{
    /// <summary>
    /// Module Name : VAS_LowStockCountWidget
    /// Purpose     : Counts active product/warehouse stock at or below reorder point.
    /// Chronological development:
    ///   VAI154      2026-06-21 Created
    ///   VAI154      2026-06-22 Compliance update
    /// </summary>
    public class VAS_LowStockCountWidgetModel
    {
        /// <summary>
        /// Counts accessible active product/warehouse combinations below reorder point.
        /// </summary>
        /// <param name="ctx">Current session context.</param>
        /// <returns>Low-stock combination count.</returns>
        public int GetLowStockCount(Ctx ctx)
        {
            if (ctx == null) { return 0; }

            string storageSql = @"
                SELECT Storage.M_Product_ID,
                       Storage.M_Locator_ID,
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

            string replenishSql = @"
                SELECT Replenish.M_Product_ID,
                       Replenish.M_Warehouse_ID,
                       Replenish.ReplenishType,
                       Replenish.Level_Min,
                       Replenish.Level_Max
                FROM M_Replenish Replenish
                WHERE Replenish.IsActive=N'Y'
                  AND Replenish.ReplenishType IN (N'1',N'2')
                  AND Replenish.AD_Client_ID=@Replenish_Client_ID
                  AND Replenish.AD_Org_ID IN (0,COALESCE(NULLIF(@Replenish_Org_ID,0),Replenish.AD_Org_ID))";

            replenishSql = MRole.GetDefault(ctx).AddAccessSQL(
                replenishSql,
                "Replenish",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            string sql = @"
                WITH StorageRows AS (
                    " + storageSql + @"
                ),
                LocatorRows AS (
                    " + locatorSql + @"
                ),
                ReplenishRows AS (
                    " + replenishSql + @"
                ),
                StockLevels AS (
                    SELECT StorageRows.M_Product_ID,
                           LocatorRows.M_Warehouse_ID,
                           SUM(StorageRows.QtyOnHand) AS Qty_On_Hand,
                           MAX(
                               CASE
                                   WHEN ReplenishRows.ReplenishType=N'1' THEN ReplenishRows.Level_Min
                                   WHEN ReplenishRows.ReplenishType=N'2' THEN ReplenishRows.Level_Max
                                   ELSE NULL
                               END
                           ) AS Reorder_Point
                    FROM StorageRows
                    INNER JOIN LocatorRows ON (LocatorRows.M_Locator_ID=StorageRows.M_Locator_ID)
                    INNER JOIN ReplenishRows ON (ReplenishRows.M_Product_ID=StorageRows.M_Product_ID AND ReplenishRows.M_Warehouse_ID=LocatorRows.M_Warehouse_ID)
                    GROUP BY StorageRows.M_Product_ID,
                             LocatorRows.M_Warehouse_ID
                )
                SELECT COUNT(*) AS Low_Stock_Count
                FROM StockLevels
                WHERE StockLevels.Reorder_Point IS NOT NULL
                  AND StockLevels.Qty_On_Hand<=StockLevels.Reorder_Point";

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@Storage_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@Storage_Org_ID", ctx.GetAD_Org_ID()),
                new SqlParameter("@Locator_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@Locator_Org_ID", ctx.GetAD_Org_ID()),
                new SqlParameter("@Replenish_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@Replenish_Org_ID", ctx.GetAD_Org_ID())
            };

            return Util.GetValueOfInt(DB.ExecuteScalar(sql, parameters, null));
        }
    }
}
