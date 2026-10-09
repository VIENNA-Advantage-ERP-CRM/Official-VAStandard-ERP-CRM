using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Web.Mvc;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VAS.Controllers
{
    /*
     * TABLE & FIELD MAPPING FOR INVENTORY AGING REPORT:
     * - Stock movements (in AND out): M_Transaction (M_Product_ID, M_AttributeSetInstance_ID,
     *   M_Locator_ID, MovementDate, MovementQty[, IsReversed]). MovementQty > 0 is stock-in (receipt,
     *   production, inbound move, positive inventory), MovementQty < 0 is stock-out (shipment / sale,
     *   issue, outbound move, negative inventory).
     * - Costing policy: M_Product_Category.MMPolicy (F = FIFO, L = LIFO) of the product's own
     *   M_Product.M_Product_Category_ID; AD_Client.MMPolicy and finally FIFO when it is not set.
     * - Product Master: M_Product (M_Product_ID, Name, M_Product_Category_ID)
     * - Attribute Instance: M_AttributeSetInstance (M_AttributeSetInstance_ID, Description)
     * - Locator: M_Locator (M_Locator_ID, M_Warehouse_ID, Value, LocatorCombination)
     *   Displayed as COALESCE(LocatorCombination, Value).
     * - Warehouse: M_Warehouse (M_Warehouse_ID, Value, Name)
     * Slabs: Fresh Stock (0-30 days), Normal Turnover (31-90), Slow Moving - Watch (91-180), Dead Stock (180+).
     * Cross-Database: day grouping uses DB.IsPostgreSQL() vs Oracle TRUNC; everything else is ANSI SQL.
     */

    [AjaxAuthorize]
    [AjaxSessionFilter]
    public class VAS_163_InventoryAgingReportWidgetController : Controller
    {
        private static readonly VLogger _log = VLogger.GetVLogger(typeof(VAS_163_InventoryAgingReportWidgetController));

        // Column-existence guards (a column missing on a deployment fails the whole query). Same
        // AD_Column check VAS_078 / VAS_161-165 use; results are cached per column.
        private static readonly Dictionary<string, bool> _columnExists = new Dictionary<string, bool>();
        private static readonly object _columnLock = new object();

        private static bool ColumnExists(string tableName, string columnName)
        {
            string cacheKey = tableName.ToUpperInvariant() + "." + columnName.ToUpperInvariant();
            lock (_columnLock)
            {
                bool cached;
                if (_columnExists.TryGetValue(cacheKey, out cached)) { return cached; }
            }

            string sql = @"
                SELECT COUNT(1)
                FROM AD_Column ColumnInfo
                INNER JOIN AD_Table TableInfo ON (TableInfo.AD_Table_ID=ColumnInfo.AD_Table_ID AND TableInfo.IsActive='Y')
                WHERE ColumnInfo.IsActive='Y'
                  AND UPPER(TableInfo.TableName)='" + tableName.ToUpperInvariant() + @"'
                  AND UPPER(ColumnInfo.ColumnName)='" + columnName.ToUpperInvariant() + "'";

            bool exists = Util.GetValueOfInt(DB.ExecuteScalar(sql, null, null)) > 0;
            lock (_columnLock) { _columnExists[cacheKey] = exists; }
            return exists;
        }

        /*
         * AGING BASIS (revised 2026-10-08, user instruction: "calculate inventory based on both
         * stock-in (product movement) and stock-out (issue/sale) transactions ... When a product is
         * issued or sold, reduce the stock quantity from the appropriate aging slab ... Use the
         * MMPolicy configured for that product category: FIFO deduct the oldest first, LIFO deduct the
         * newest first ... the widget always shows the actual remaining inventory by age").
         *
         * Before this the widget only summed inbound movements (MovementQty > 0), so issued and sold
         * stock was never taken off any slab and the slabs overstated what is on hand.
         *
         * Each stock position is one Product + AttributeSetInstance + Locator (the "stock layer
         * owner"). Its movements are replayed day by day in date order:
         *   - stock-in of a day adds a layer (that movement date, that quantity);
         *   - stock-out of a day then consumes layers - the OLDEST first under FIFO, the NEWEST first
         *     under LIFO - using the MMPolicy of that product's category. A stock-out only ever
         *     consumes layers that existed on or before its own day, because later receipts have not
         *     been replayed yet, and it only ever touches its own position, so it can never reduce
         *     another product, attribute or locator.
         *   - a stock-out larger than the stock then available (negative stock) removes only what is
         *     there; the excess is ignored rather than creating a negative layer.
         * What is left in the layers is the real remaining stock by age. Because it is replayed from
         * the movements on every request, the slabs are recalculated after every stock-out with no
         * stored state to keep in step. Summary and drill-down share ComputeRemainingLayers, so the
         * two can never disagree.
         *
         * Within one day receipts are applied before issues (the time of day is not used), which is
         * the usual reading when a product is received and shipped on the same date.
         *
         * This is computed straight from M_Transaction. The fuller allocation table the source prompt
         * mentions (M_TransactionAllocation) does not exist anywhere in this solution, so it is not
         * relied on. Reversed movements (IsReversed = 'Y', where the column exists) are excluded in
         * both directions, as before.
         */

        private const string DefaultPolicy = "F"; // FIFO

        private class StockLayer
        {
            public int ProductId;
            public int AttributeSetInstanceId;
            public int LocatorId;
            public int WarehouseId;
            public DateTime Day;
            public decimal Qty;
        }

        private class DayMovement
        {
            public DateTime Day;
            public decimal InQty;
            public decimal OutQty;
        }

        private class PositionKey
        {
            public int ProductId;
            public int AttributeSetInstanceId;
            public int LocatorId;
            public int WarehouseId;
        }

        private static string GetDayExpression(string dateCol)
        {
            if (DB.IsPostgreSQL())
            {
                return "CAST(COALESCE(" + dateCol + ") AS DATE)";
            }
            return "TRUNC(COALESCE(" + dateCol + "))";
        }

        /// <summary>
        /// MMPolicy per product: the product category's policy, else the client's, else FIFO.
        /// </summary>
        private static Dictionary<int, string> LoadProductPolicies(Ctx ctx)
        {
            Dictionary<int, string> map = new Dictionary<int, string>();
            int clientId = ctx.GetAD_Client_ID();

            string clientPolicy = DefaultPolicy;
            if (ColumnExists("AD_Client", "MMPolicy"))
            {
                string cp = Util.GetValueOfString(DB.ExecuteScalar(
                    "SELECT MMPolicy FROM AD_Client WHERE AD_Client_ID = " + clientId, null, null));
                if (!string.IsNullOrEmpty(cp)) { clientPolicy = cp; }
            }

            bool categoryHasPolicy = ColumnExists("M_Product_Category", "MMPolicy");
            string sql = categoryHasPolicy
                ? @"SELECT p.M_Product_ID AS M_Product_ID, pc.MMPolicy AS MMPolicy
                    FROM M_Product p
                    LEFT JOIN M_Product_Category pc ON (pc.M_Product_Category_ID = p.M_Product_Category_ID)
                    WHERE p.AD_Client_ID IN (0, " + clientId + ")"
                : @"SELECT p.M_Product_ID AS M_Product_ID, NULL AS MMPolicy
                    FROM M_Product p
                    WHERE p.AD_Client_ID IN (0, " + clientId + ")";

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, null, null);
                while (dr != null && dr.Read())
                {
                    string policy = Util.GetValueOfString(dr["MMPolicy"]);
                    map[Util.GetValueOfInt(dr["M_Product_ID"])] = string.IsNullOrEmpty(policy) ? clientPolicy : policy;
                }
            }
            finally
            {
                if (dr != null) { dr.Close(); dr.Dispose(); }
            }
            return map;
        }

        /// <summary>
        /// Replays every stock-in and stock-out movement of each Product + Attribute + Locator and
        /// returns the layers that are still on hand, with the policy-driven deductions applied.
        /// </summary>
        private static List<StockLayer> ComputeRemainingLayers(Ctx ctx, int? warehouseId)
        {
            string whFilter = (warehouseId.HasValue && warehouseId.Value > 0)
                ? " AND loc.M_Warehouse_ID = " + warehouseId.Value
                : "";
            string reversedFilter = ColumnExists("M_Transaction", "IsReversed")
                ? " AND COALESCE(t.IsReversed, 'N') = 'N'"
                : "";

            // Plain SELECT first so MRole.AddAccessSQL can append its predicate at the end; the
            // aggregation wraps it afterwards (an aggregate or GROUP BY inside breaks the parser).
            string txSql = @"SELECT t.M_Product_ID,
                                    COALESCE(t.M_AttributeSetInstance_ID, 0) AS M_AttributeSetInstance_ID,
                                    t.M_Locator_ID,
                                    loc.M_Warehouse_ID,
                                    t.MovementQty,
                                    " + GetDayExpression("t.MovementDate, t.Created") + @" AS MoveDay
                             FROM M_Transaction t
                             JOIN M_Locator loc ON (t.M_Locator_ID = loc.M_Locator_ID)
                             WHERE t.IsActive = 'Y' AND t.MovementQty <> 0" + reversedFilter + whFilter;
            txSql = MRole.GetDefault(ctx).AddAccessSQL(txSql, "t", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string sql = @"SELECT M_Product_ID, M_AttributeSetInstance_ID, M_Locator_ID, M_Warehouse_ID, MoveDay,
                                  SUM(CASE WHEN MovementQty > 0 THEN MovementQty ELSE 0 END) AS InQty,
                                  SUM(CASE WHEN MovementQty < 0 THEN -MovementQty ELSE 0 END) AS OutQty
                           FROM (" + txSql + @") tx
                           GROUP BY M_Product_ID, M_AttributeSetInstance_ID, M_Locator_ID, M_Warehouse_ID, MoveDay
                           ORDER BY M_Product_ID, M_AttributeSetInstance_ID, M_Locator_ID, MoveDay";

            Dictionary<int, string> policies = LoadProductPolicies(ctx);

            List<StockLayer> remaining = new List<StockLayer>();
            PositionKey currentKey = null;
            List<DayMovement> currentDays = new List<DayMovement>();

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, null, null);
                while (dr != null && dr.Read())
                {
                    PositionKey key = new PositionKey
                    {
                        ProductId = Util.GetValueOfInt(dr["M_Product_ID"]),
                        AttributeSetInstanceId = Util.GetValueOfInt(dr["M_AttributeSetInstance_ID"]),
                        LocatorId = Util.GetValueOfInt(dr["M_Locator_ID"]),
                        WarehouseId = Util.GetValueOfInt(dr["M_Warehouse_ID"])
                    };

                    if (currentKey != null && !SamePosition(currentKey, key))
                    {
                        ReplayPosition(currentKey, currentDays, policies, remaining);
                        currentDays = new List<DayMovement>();
                    }
                    currentKey = key;

                    DateTime? day = Util.GetValueOfDateTime(dr["MoveDay"]);
                    currentDays.Add(new DayMovement
                    {
                        Day = (day.HasValue ? day.Value : DateTime.Today).Date,
                        InQty = Util.GetValueOfDecimal(dr["InQty"]),
                        OutQty = Util.GetValueOfDecimal(dr["OutQty"])
                    });
                }
                if (currentKey != null)
                {
                    ReplayPosition(currentKey, currentDays, policies, remaining);
                }
            }
            finally
            {
                if (dr != null) { dr.Close(); dr.Dispose(); }
            }

            return remaining;
        }

        private static bool SamePosition(PositionKey a, PositionKey b)
        {
            return a.ProductId == b.ProductId
                && a.AttributeSetInstanceId == b.AttributeSetInstanceId
                && a.LocatorId == b.LocatorId;
        }

        /// <summary>
        /// Replays one position's days (already in date order) and appends its remaining layers.
        /// </summary>
        private static void ReplayPosition(PositionKey key, List<DayMovement> days,
            Dictionary<int, string> policies, List<StockLayer> output)
        {
            string policy;
            if (!policies.TryGetValue(key.ProductId, out policy) || string.IsNullOrEmpty(policy))
            {
                policy = DefaultPolicy;
            }
            bool lifo = string.Equals(policy, "L", StringComparison.OrdinalIgnoreCase);

            LinkedList<StockLayer> layers = new LinkedList<StockLayer>();
            foreach (DayMovement d in days)
            {
                // Stock-in of the day: a new, newest layer.
                if (d.InQty > 0)
                {
                    layers.AddLast(new StockLayer
                    {
                        ProductId = key.ProductId,
                        AttributeSetInstanceId = key.AttributeSetInstanceId,
                        LocatorId = key.LocatorId,
                        WarehouseId = key.WarehouseId,
                        Day = d.Day,
                        Qty = d.InQty
                    });
                }

                // Stock-out of the day: consume oldest first (FIFO) or newest first (LIFO).
                decimal toDeduct = d.OutQty;
                while (toDeduct > 0 && layers.Count > 0)
                {
                    LinkedListNode<StockLayer> node = lifo ? layers.Last : layers.First;
                    decimal take = Math.Min(node.Value.Qty, toDeduct);
                    node.Value.Qty -= take;
                    toDeduct -= take;
                    if (node.Value.Qty <= 0) { layers.Remove(node); }
                }
                // Anything still left in toDeduct was issued from stock that was not on hand
                // (negative stock); it cannot reduce a slab below zero, so it is ignored.
            }

            foreach (StockLayer layer in layers)
            {
                if (layer.Qty > 0) { output.Add(layer); }
            }
        }

        private static int AgeInDays(StockLayer layer, DateTime today)
        {
            return (int)(today - layer.Day).TotalDays;
        }

        private static string BucketOf(int ageDays)
        {
            if (ageDays <= 30) { return "0-30"; }
            if (ageDays <= 90) { return "31-90"; }
            if (ageDays <= 180) { return "91-180"; }
            return "180+";
        }

        /// <summary>
        /// Gets the list of user-accessible active warehouses.
        /// </summary>
        [HttpGet]
        public JsonResult GetWarehouses()
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null)
            {
                return Json(new { error = "Unauthorized context." }, JsonRequestBehavior.AllowGet);
            }

            List<object> list = new List<object>();
            IDataReader dr = null;
            try
            {
                string sql = "SELECT M_Warehouse_ID, Value, Name FROM M_Warehouse WHERE IsActive = 'Y'";
                sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "M_Warehouse", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                sql += " ORDER BY Name ASC";

                dr = DB.ExecuteReader(sql, null, null);
                while (dr != null && dr.Read())
                {
                    list.Add(new
                    {
                        warehouseId = Util.GetValueOfInt(dr["M_Warehouse_ID"]),
                        code = Util.GetValueOfString(dr["Value"]),
                        name = Util.GetValueOfString(dr["Name"])
                    });
                }
            }
            catch (Exception ex)
            {
                _log.Severe("VAS_163_InventoryAgingReportWidgetController.GetWarehouses: " + ex.Message);
            }
            finally
            {
                if (dr != null)
                {
                    dr.Close();
                    dr.Dispose();
                }
            }

            return Json(new { warehouses = list }, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Gets the REMAINING stock quantity per slab: Fresh Stock (0-30), Normal Turnover (31-90),
        /// Slow Moving - Watch (91-180), Dead Stock (180+), after stock-out movements have been
        /// deducted from the layers per the product category's FIFO / LIFO policy.
        /// </summary>
        [HttpGet]
        public JsonResult GetAgingSummary(int? warehouseId)
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null)
            {
                return Json(new { error = "Unauthorized context." }, JsonRequestBehavior.AllowGet);
            }

            decimal b0_30 = 0;
            decimal b31_90 = 0;
            decimal b91_180 = 0;
            decimal b180_plus = 0;

            try
            {
                DateTime today = DateTime.Today;
                foreach (StockLayer layer in ComputeRemainingLayers(ctx, warehouseId))
                {
                    switch (BucketOf(AgeInDays(layer, today)))
                    {
                        case "0-30": b0_30 += layer.Qty; break;
                        case "31-90": b31_90 += layer.Qty; break;
                        case "91-180": b91_180 += layer.Qty; break;
                        default: b180_plus += layer.Qty; break;
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Severe("VAS_163_InventoryAgingReportWidgetController.GetAgingSummary: " + ex.Message);
            }

            decimal total = b0_30 + b31_90 + b91_180 + b180_plus;

            return Json(new
            {
                b0_30 = b0_30,
                b31_90 = b31_90,
                b91_180 = b91_180,
                b180_plus = b180_plus,
                totalQty = total
            }, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Gets per-product remaining quantities for one slab (and optional warehouse): Product +
        /// Attribute + Locator, with the quantity left in that slab after the FIFO / LIFO
        /// deductions, and the age (in days) of the newest layer in it.
        /// </summary>
        [HttpGet]
        public JsonResult GetBucketDetail(string bucketId, int? warehouseId)
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null)
            {
                return Json(new { error = "Unauthorized context." }, JsonRequestBehavior.AllowGet);
            }

            List<object> lines = new List<object>();
            try
            {
                DateTime today = DateTime.Today;

                // Layers in the requested slab, summed per Product + Attribute + Locator.
                var groups = ComputeRemainingLayers(ctx, warehouseId)
                    .Where(layer => BucketOf(AgeInDays(layer, today)) == bucketId)
                    .GroupBy(layer => new { layer.ProductId, layer.AttributeSetInstanceId, layer.LocatorId, layer.WarehouseId })
                    .Select(g => new
                    {
                        g.Key.ProductId,
                        g.Key.AttributeSetInstanceId,
                        g.Key.LocatorId,
                        g.Key.WarehouseId,
                        Qty = g.Sum(x => x.Qty),
                        AgeDays = g.Min(x => AgeInDays(x, today))
                    })
                    .ToList();

                if (groups.Count > 0)
                {
                    Dictionary<int, string> productNames = LoadNameMap(
                        "SELECT M_Product_ID AS ID, Name AS Label FROM M_Product WHERE M_Product_ID IN ({0})",
                        groups.Select(x => x.ProductId));
                    Dictionary<int, string> attributes = LoadNameMap(
                        "SELECT M_AttributeSetInstance_ID AS ID, Description AS Label FROM M_AttributeSetInstance WHERE M_AttributeSetInstance_ID IN ({0})",
                        groups.Where(x => x.AttributeSetInstanceId > 0).Select(x => x.AttributeSetInstanceId));
                    Dictionary<int, string> warehouses = LoadNameMap(
                        "SELECT M_Warehouse_ID AS ID, Name AS Label FROM M_Warehouse WHERE M_Warehouse_ID IN ({0})",
                        groups.Select(x => x.WarehouseId));
                    // LocatorCombination is the full "Warehouse.Aisle.Bin.Level"-style locator name;
                    // Value alone is a numeric surrogate. Falls back to Value where the column is absent.
                    string locatorLabel = ColumnExists("M_Locator", "LocatorCombination")
                        ? "COALESCE(LocatorCombination, Value)"
                        : "Value";
                    Dictionary<int, string> locators = LoadNameMap(
                        "SELECT M_Locator_ID AS ID, " + locatorLabel + " AS Label FROM M_Locator WHERE M_Locator_ID IN ({0})",
                        groups.Select(x => x.LocatorId));

                    foreach (var g in groups
                        .OrderByDescending(x => x.AgeDays)
                        .ThenBy(x => NameOf(productNames, x.ProductId), StringComparer.OrdinalIgnoreCase))
                    {
                        // No attribute set instance -> BLANK, not a placeholder.
                        lines.Add(new
                        {
                            product = NameOf(productNames, g.ProductId),
                            attribute = g.AttributeSetInstanceId > 0 ? NameOf(attributes, g.AttributeSetInstanceId) : "",
                            warehouse = NameOf(warehouses, g.WarehouseId),
                            locator = NameOf(locators, g.LocatorId),
                            qty = g.Qty,
                            ageDays = g.AgeDays
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Severe("VAS_163_InventoryAgingReportWidgetController.GetBucketDetail: " + ex.Message);
            }

            return Json(new { details = lines, totalCount = lines.Count }, JsonRequestBehavior.AllowGet);
        }

        private static string NameOf(Dictionary<int, string> map, int id)
        {
            string name;
            return (map != null && map.TryGetValue(id, out name)) ? (name ?? "") : "";
        }

        /// <summary>
        /// Loads id -> label pairs for a set of ids (the {0} placeholder takes the id list). The ids
        /// are integers, so they are inlined; chunked to stay under the IN-list limit of Oracle.
        /// </summary>
        private static Dictionary<int, string> LoadNameMap(string sqlTemplate, IEnumerable<int> ids)
        {
            Dictionary<int, string> map = new Dictionary<int, string>();
            List<int> distinct = ids.Where(i => i > 0).Distinct().ToList();

            for (int offset = 0; offset < distinct.Count; offset += 500)
            {
                string inList = string.Join(",", distinct.Skip(offset).Take(500).Select(i => i.ToString()));
                IDataReader dr = null;
                try
                {
                    dr = DB.ExecuteReader(string.Format(sqlTemplate, inList), null, null);
                    while (dr != null && dr.Read())
                    {
                        map[Util.GetValueOfInt(dr["ID"])] = Util.GetValueOfString(dr["Label"]);
                    }
                }
                finally
                {
                    if (dr != null) { dr.Close(); dr.Dispose(); }
                }
            }
            return map;
        }
    }
}
