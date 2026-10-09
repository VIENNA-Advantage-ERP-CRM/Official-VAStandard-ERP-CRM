using Newtonsoft.Json;
using System;
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
    /// </summary>
    public class VAS_111_BelowMinWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_111_BelowMinWidgetController).FullName);

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
        /// Counts distinct items whose per-warehouse on-hand is at or below the
        /// per-warehouse reorder point (Level_Min for ReplenishType '1',
        /// Level_Max for '2'). Plain ASCII literals only - N'...' literals fail
        /// against CHAR columns on Oracle (ORA-12704) and are invalid on
        /// PostgreSQL (same fix as VAS_073, review #43).
        /// </summary>
        /// <param name="ctx">Current application context.</param>
        /// <returns>Distinct below-min item count.</returns>
        private int GetBelowMinData(Ctx ctx)
        {
            if (ctx == null) { return 0; }

            string storageSql = @"
                SELECT Storage.M_Product_ID,
                       Storage.M_Locator_ID,
                       Storage.QtyOnHand
                FROM M_Storage Storage
                WHERE Storage.IsActive='Y'
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
                WHERE Locator.IsActive='Y'
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
                WHERE Replenish.IsActive='Y'
                  AND Replenish.ReplenishType IN ('1','2')
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
                                   WHEN ReplenishRows.ReplenishType='1' THEN ReplenishRows.Level_Min
                                   WHEN ReplenishRows.ReplenishType='2' THEN ReplenishRows.Level_Max
                                   ELSE NULL
                               END
                           ) AS Reorder_Point
                    FROM StorageRows
                    INNER JOIN LocatorRows ON (LocatorRows.M_Locator_ID=StorageRows.M_Locator_ID)
                    INNER JOIN ReplenishRows ON (ReplenishRows.M_Product_ID=StorageRows.M_Product_ID AND ReplenishRows.M_Warehouse_ID=LocatorRows.M_Warehouse_ID)
                    GROUP BY StorageRows.M_Product_ID,
                             LocatorRows.M_Warehouse_ID
                )
                SELECT COUNT(DISTINCT StockLevels.M_Product_ID) AS Below_Min_Count
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
