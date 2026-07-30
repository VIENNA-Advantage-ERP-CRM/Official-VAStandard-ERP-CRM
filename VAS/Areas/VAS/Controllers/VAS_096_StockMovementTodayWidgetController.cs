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

        // Review #36: M_Transaction.IsReversed exists on some deployments (the
        // Oracle test DB) but not on others (the PostgreSQL one); referencing a
        // missing column fails the movements query. Cached per app lifetime.
        private static bool? _transactionHasIsReversed;

        /// <summary>True when the application dictionary has M_Transaction.IsReversed.</summary>
        private bool TransactionHasIsReversed()
        {
            if (_transactionHasIsReversed.HasValue) { return _transactionHasIsReversed.Value; }

            string sql = @"
                SELECT COUNT(1)
                FROM AD_Column ColumnInfo
                INNER JOIN AD_Table TableInfo ON (TableInfo.AD_Table_ID=ColumnInfo.AD_Table_ID AND TableInfo.IsActive='Y')
                WHERE ColumnInfo.IsActive='Y'
                  AND UPPER(TableInfo.TableName)='M_TRANSACTION'
                  AND UPPER(ColumnInfo.ColumnName)='ISREVERSED'";

            _transactionHasIsReversed = Util.GetValueOfInt(DB.ExecuteScalar(sql, new SqlParameter[0], null)) > 0;
            return _transactionHasIsReversed.Value;
        }

        // Review #37: the widget pages through today's movements instead of
        // showing only the latest few; the browsable window is capped at the
        // 50 most recent transactions of the day.
        private const int MaxTodaysTransactions = 50;

        /// <summary>Returns one page of today's active, non-reversed stock movements (newest first, capped at 50).</summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetStockMovements(int pageNo = 1, int pageSize = 5)
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null) { return Json("", JsonRequestBehavior.AllowGet); }

            if (pageSize < 1) { pageSize = 5; }
            if (pageSize > MaxTodaysTransactions) { pageSize = MaxTodaysTransactions; }
            if (pageNo < 1) { pageNo = 1; }

            // Keep the requested page inside the 50-transaction window.
            int lastPage = (int)Math.Ceiling((double)MaxTodaysTransactions / pageSize);
            if (pageNo > lastPage) { pageNo = lastPage; }

            try
            {
                string json = JsonConvert.SerializeObject(GetStockMovementData(ctx, pageNo, pageSize));
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_096_StockMovementTodayWidget.GetStockMovements", ex);
                string json = JsonConvert.SerializeObject(new { error = Msg.GetMsg(ctx, "Error") ?? "Error" });
                return Json(json, JsonRequestBehavior.AllowGet);
            }
        }

        private StockMovementResult GetStockMovementData(Ctx ctx, int pageNo, int pageSize)
        {
            StockMovementResult result = new StockMovementResult
            {
                rows = new List<StockMovementRow>(),
                page_no = pageNo,
                page_size = pageSize
            };
            if (ctx == null) { return result; }

            // Review #37: page inside the 50-newest window; the last page may be
            // shorter so the fetch never reaches past transaction #50.
            int offset = (pageNo - 1) * pageSize;
            int fetchCount = Math.Min(pageSize, MaxTodaysTransactions - offset);
            if (fetchCount < 1) { fetchCount = 1; }

            string movementSql = @"
                SELECT Movement.M_Transaction_ID,
                       Movement.M_Product_ID,
                       Movement.M_Locator_ID,
                       COALESCE(Movement.M_AttributeSetInstance_ID,0) AS M_AttributeSetInstance_ID,
                       Movement.MovementType,
                       Movement.MovementQty,
                       Movement.MovementDate,
                       Movement.Created
                FROM M_Transaction Movement
                WHERE Movement.IsActive='Y'
                  AND Movement.AD_Client_ID=@Movement_Client_ID
                  AND Movement.AD_Org_ID IN (0,COALESCE(NULLIF(@Movement_Org_ID,0),Movement.AD_Org_ID))
                  AND Movement.MovementDate>=@Today
                  AND Movement.MovementDate<@Tomorrow";

            // Review #36: filter reversed movements only where the column exists.
            if (TransactionHasIsReversed())
            {
                movementSql += @"
                  AND COALESCE(Movement.IsReversed,'N')='N'";
            }

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
                       AttributeInstance.Description AS Attribute_Description,
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
                LEFT JOIN M_AttributeSetInstance AttributeInstance ON (AttributeInstance.M_AttributeSetInstance_ID=MovementRows.M_AttributeSetInstance_ID)
                LEFT JOIN StorageRows ON (
                    StorageRows.M_Product_ID=MovementRows.M_Product_ID
                    AND StorageRows.M_Locator_ID=MovementRows.M_Locator_ID
                )
                ORDER BY MovementRows.MovementDate DESC,
                         MovementRows.Created DESC,
                         MovementRows.M_Transaction_ID DESC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY",
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
                new SqlParameter("@Storage_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@Offset", offset),
                new SqlParameter("@PageSize", fetchCount)
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
                        attribute = Util.GetValueOfString(reader["Attribute_Description"]),
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

            // Review #37: the widget browses at most the 50 newest transactions.
            if (result.total_records > MaxTodaysTransactions) { result.total_records = MaxTodaysTransactions; }

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
            public int page_no { get; set; }
            public int page_size { get; set; }
            public List<StockMovementRow> rows { get; set; }
        }

        private class StockMovementRow
        {
            public int transaction_id { get; set; }
            public string item_name { get; set; }
            // Review #38: attribute set instance (batch / serial / variant).
            public string attribute { get; set; }
            public string movement_type { get; set; }
            public decimal movement_qty { get; set; }
            public string warehouse_name { get; set; }
            public string locator_value { get; set; }
            public string movement_date { get; set; }
            public decimal qty_on_hand { get; set; }
        }
    }
}
