using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Web.Mvc;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VAS.Controllers
{
    /// <summary>
    /// Module Name : VAS_096_StockMovementTodayWidget
    /// Purpose     : Supplies today's latest stock movements for the Overall Inventory dashboard.
    /// Chronological development:
    ///   VAI154      2026-06-23 Created
    /// </summary>
    public class VAS_096_StockMovementTodayWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_096_StockMovementTodayWidgetController).FullName);

        /// <summary>Returns the five latest active, non-reversed stock movements from today.</summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetStockMovements()
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null) { return Json("", JsonRequestBehavior.AllowGet); }

            try
            {
                string json = JsonConvert.SerializeObject(GetStockMovementData(ctx));
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_096_StockMovementTodayWidget.GetStockMovements", ex);
                string json = JsonConvert.SerializeObject(new { error = Msg.GetMsg(ctx, "Error") ?? "Error" });
                return Json(json, JsonRequestBehavior.AllowGet);
            }
        }

        private StockMovementResult GetStockMovementData(Ctx ctx)
        {
            StockMovementResult result = new StockMovementResult
            {
                rows = new List<StockMovementRow>()
            };
            if (ctx == null) { return result; }

            string movementSql = @"
                SELECT Movement.M_Transaction_ID,
                       Movement.M_Product_ID,
                       Movement.M_Locator_ID,
                       Movement.MovementType,
                       Movement.MovementQty,
                       Movement.MovementDate,
                       Movement.Created
                FROM M_Transaction Movement
                WHERE Movement.IsActive='Y'
                  AND COALESCE(Movement.IsReversed,'N')='N'
                  AND Movement.AD_Client_ID=@Movement_Client_ID
                  AND Movement.AD_Org_ID IN (0,COALESCE(NULLIF(@Movement_Org_ID,0),Movement.AD_Org_ID))
                  AND Movement.MovementDate>=@Today
                  AND Movement.MovementDate<@Tomorrow";

            string productSql = @"
                SELECT Product.M_Product_ID,
                       Product.Name
                FROM M_Product Product
                WHERE Product.IsActive='Y'
                  AND Product.AD_Client_ID=@Product_Client_ID
                  AND Product.AD_Org_ID IN (0,COALESCE(NULLIF(@Product_Org_ID,0),Product.AD_Org_ID))";

            string locatorSql = @"
                SELECT Locator.M_Locator_ID,
                       Locator.M_Warehouse_ID,
                       Locator.Value
                FROM M_Locator Locator
                WHERE Locator.IsActive='Y'
                  AND Locator.AD_Client_ID=@Locator_Client_ID
                  AND Locator.AD_Org_ID IN (0,COALESCE(NULLIF(@Locator_Org_ID,0),Locator.AD_Org_ID))";

            string warehouseSql = @"
                SELECT Warehouse.M_Warehouse_ID,
                       Warehouse.Name
                FROM M_Warehouse Warehouse
                WHERE Warehouse.IsActive='Y'
                  AND Warehouse.AD_Client_ID=@Warehouse_Client_ID
                  AND Warehouse.AD_Org_ID IN (0,COALESCE(NULLIF(@Warehouse_Org_ID,0),Warehouse.AD_Org_ID))";

            movementSql = AddAccessSql(ctx, movementSql, "Movement");
            productSql = AddAccessSql(ctx, productSql, "Product");
            locatorSql = AddAccessSql(ctx, locatorSql, "Locator");
            warehouseSql = AddAccessSql(ctx, warehouseSql, "Warehouse");

            string sql = string.Format(@"
                WITH MovementRows AS (
                    {0}
                ),
                ProductRows AS (
                    {1}
                ),
                LocatorRows AS (
                    {2}
                ),
                WarehouseRows AS (
                    {3}
                ),
                StorageRows AS (
                    SELECT M_Product_ID,
                           M_Locator_ID,
                           SUM(QtyOnHand) AS QtyOnHand
                    FROM M_Storage
                    WHERE IsActive='Y'
                      AND AD_Client_ID=@Storage_Client_ID
                    GROUP BY M_Product_ID, M_Locator_ID
                )
                SELECT MovementRows.M_Transaction_ID,
                       ProductRows.Name AS Item_Name,
                       MovementRows.MovementType,
                       MovementRows.MovementQty,
                       WarehouseRows.Name AS Warehouse_Name,
                       LocatorRows.Value AS Locator_Value,
                       MovementRows.MovementDate,
                       COALESCE(StorageRows.QtyOnHand, 0) AS QtyOnHand,
                       COUNT(*) OVER() AS Total_Rows
                FROM MovementRows
                INNER JOIN ProductRows ON (ProductRows.M_Product_ID=MovementRows.M_Product_ID)
                INNER JOIN LocatorRows ON (LocatorRows.M_Locator_ID=MovementRows.M_Locator_ID)
                INNER JOIN WarehouseRows ON (WarehouseRows.M_Warehouse_ID=LocatorRows.M_Warehouse_ID)
                LEFT JOIN StorageRows ON (
                    StorageRows.M_Product_ID=MovementRows.M_Product_ID
                    AND StorageRows.M_Locator_ID=MovementRows.M_Locator_ID
                )
                ORDER BY MovementRows.MovementDate DESC,
                         MovementRows.Created DESC,
                         MovementRows.M_Transaction_ID DESC
                FETCH FIRST 5 ROWS ONLY",
                movementSql,
                productSql,
                locatorSql,
                warehouseSql
            );

            DateTime today = DateTime.Now.Date;
            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@Movement_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@Movement_Org_ID", ctx.GetAD_Org_ID()),
                new SqlParameter("@Today", SqlDbType.DateTime) { Value = today },
                new SqlParameter("@Tomorrow", SqlDbType.DateTime) { Value = today.AddDays(1) },
                new SqlParameter("@Product_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@Product_Org_ID", ctx.GetAD_Org_ID()),
                new SqlParameter("@Locator_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@Locator_Org_ID", ctx.GetAD_Org_ID()),
                new SqlParameter("@Warehouse_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@Warehouse_Org_ID", ctx.GetAD_Org_ID()),
                new SqlParameter("@Storage_Client_ID", ctx.GetAD_Client_ID())
            };

            IDataReader reader = null;
            try
            {
                reader = DB.ExecuteReader(sql, parameters);
                while (reader != null && reader.Read())
                {
                    result.total_records = Util.GetValueOfInt(reader["Total_Rows"]);
                    result.rows.Add(new StockMovementRow
                    {
                        transaction_id = Util.GetValueOfInt(reader["M_Transaction_ID"]),
                        item_name = Util.GetValueOfString(reader["Item_Name"]),
                        movement_type = Util.GetValueOfString(reader["MovementType"]),
                        movement_qty = Util.GetValueOfDecimal(reader["MovementQty"]),
                        warehouse_name = Util.GetValueOfString(reader["Warehouse_Name"]),
                        locator_value = Util.GetValueOfString(reader["Locator_Value"]),
                        movement_date = FormatDateTime(Util.GetValueOfDateTime(reader["MovementDate"])),
                        qty_on_hand = Util.GetValueOfDecimal(reader["QtyOnHand"])
                    });
                }
            }
            finally
            {
                CloseReader(reader);
            }

            return result;
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

        private string FormatDateTime(DateTime? value)
        {
            return value.HasValue
                ? value.Value.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)
                : "";
        }

        private void CloseReader(IDataReader reader)
        {
            if (reader == null) { return; }
            reader.Close();
            reader.Dispose();
        }

        private class StockMovementResult
        {
            public int total_records { get; set; }
            public List<StockMovementRow> rows { get; set; }
        }

        private class StockMovementRow
        {
            public int transaction_id { get; set; }
            public string item_name { get; set; }
            public string movement_type { get; set; }
            public decimal movement_qty { get; set; }
            public string warehouse_name { get; set; }
            public string locator_value { get; set; }
            public string movement_date { get; set; }
            public decimal qty_on_hand { get; set; }
        }
    }
}
