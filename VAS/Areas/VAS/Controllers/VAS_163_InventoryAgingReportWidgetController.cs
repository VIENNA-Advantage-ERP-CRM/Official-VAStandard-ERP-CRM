using System;
using System.Collections.Generic;
using System.Data;
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
     * - Transactions / Aging source: M_Transaction (M_Product_ID, M_AttributeSetInstance_ID,
     *   M_Locator_ID, MovementDate, MovementQty) - the aging slabs are computed from the
     *   MovementDate age and the QUANTITY under each slab is the summed MovementQty, per the
     *   source specification. Only inbound stock counts as aging stock (MovementQty > 0).
     * - Product Master: M_Product (M_Product_ID, Name)
     * - Attribute Instance: M_AttributeSetInstance (M_AttributeSetInstance_ID, Description)
     * - Locator: M_Locator (M_Locator_ID, M_Warehouse_ID, Value, LocatorCombination)
     *   Displayed as COALESCE(LocatorCombination, Value) - the prompt's own mapping says
     *   "M_Locator.LocatorCombination: preferred locator display / M_Locator.Value: locator
     *   display fallback". Value alone is a numeric surrogate on this data.
     * - Movement history: M_Transaction (M_Product_ID, M_AttributeSetInstance_ID, M_Locator_ID,
     *   MovementDate, MovementQty[, IsReversed]) - the aging basis.
     * - Warehouse: M_Warehouse (M_Warehouse_ID, Value, Name)
     * Slabs: Fresh Stock (0-30 days), Normal Turnover (31-90), Slow Moving (91-180), Dead Stock (180+).
     * Cross-Database: Age calculation uses DB.IsPostgreSQL() vs Oracle DB.TO_DATE/SYSDATE and ANSI COALESCE.
     */

    [AjaxAuthorize]
    [AjaxSessionFilter]
    public class VAS_163_InventoryAgingReportWidgetController : Controller
    {
        private static readonly VLogger _log = VLogger.GetVLogger(typeof(VAS_163_InventoryAgingReportWidgetController));

        // M_Transaction.IsReversed exists on some deployments but not others; referencing a
        // missing column fails the whole query. Same guard and cache as
        // VAS_078_ProductSearchWidgetController.TransactionHasIsReversed().
        private static bool? _transactionHasIsReversed;

        private static bool TransactionHasIsReversed()
        {
            if (_transactionHasIsReversed.HasValue) { return _transactionHasIsReversed.Value; }

            string sql = @"
                SELECT COUNT(1)
                FROM AD_Column ColumnInfo
                INNER JOIN AD_Table TableInfo ON (TableInfo.AD_Table_ID=ColumnInfo.AD_Table_ID AND TableInfo.IsActive='Y')
                WHERE ColumnInfo.IsActive='Y'
                  AND UPPER(TableInfo.TableName)='M_TRANSACTION'
                  AND UPPER(ColumnInfo.ColumnName)='ISREVERSED'";

            _transactionHasIsReversed = Util.GetValueOfInt(DB.ExecuteScalar(sql, null, null)) > 0;
            return _transactionHasIsReversed.Value;
        }

        // M_Locator.LocatorCombination is not present on every database release (same guard
        // VAS_146/161-165/184/186/188 already use) - check first and fall back to Value.
        private static bool? _locatorHasCombination;

        private static bool LocatorHasCombination()
        {
            if (_locatorHasCombination.HasValue) { return _locatorHasCombination.Value; }

            string sql = @"
                SELECT COUNT(1)
                FROM AD_Column ColumnInfo
                INNER JOIN AD_Table TableInfo ON (TableInfo.AD_Table_ID=ColumnInfo.AD_Table_ID AND TableInfo.IsActive='Y')
                WHERE ColumnInfo.IsActive='Y'
                  AND UPPER(TableInfo.TableName)='M_LOCATOR'
                  AND UPPER(ColumnInfo.ColumnName)='LOCATORCOMBINATION'";

            _locatorHasCombination = Util.GetValueOfInt(DB.ExecuteScalar(sql, null, null)) > 0;
            return _locatorHasCombination.Value;
        }

        /*
         * AGING BASIS (user instruction 2026-08-29, reconfirmed 2026-09-25: "Fetch inventory
         * aging details from the M_Transaction table and calculate/display the quantity under
         * the Fresh Stock (0-30), Normal Turnover (31-90), Slow Moving (91-180) and Dead Stock
         * (180+) slabs based on the MovementDate and MovementQty fields, for the selected
         * warehouse").
         *
         * The age of one stock position is the age of the OLDEST INBOUND MOVEMENT that put stock
         * into it: MIN(M_Transaction.MovementDate) over rows with MovementQty > 0 for the same
         * Product + AttributeSetInstance + Locator - implemented below via AgingTransactionSql,
         * which both GetAgingSummary and GetBucketDetail share so the two can never disagree.
         *
         * This REPLACES COALESCE(s.DateLastInventory, s.Created), which the widget used before
         * this basis was chosen and is the original "incorrect data" report: DateLastInventory is
         * when a position was last COUNTED and Created is when the storage row was first written -
         * neither is when the stock actually arrived, so a long-held item could look fresh and
         * vice versa. (An earlier GetAgingJoin() helper carried this old M_Storage-joined
         * approach; it was dead code - never called - and has been removed so it cannot be
         * mistaken for the active implementation.)
         *
         * The source prompt specifies a fuller FIFO/LIFO algorithm using M_TransactionAllocation
         * remaining layers. That allocator does not exist: M_TransactionAllocation is referenced
         * NOWHERE in this solution. The user chose the simple oldest-inbound-MovementDate basis on
         * 2026-08-29 rather than have it built blind. Recorded so the prompt is not "restored"
         * later by mistake.
         *
         * Created remains the fallback (via GetAgeDaysExpression's COALESCE) when a position has
         * no inbound transaction at all, which is the fallback the prompt itself names.
         */
        private static string GetAgeDaysExpression(string dateCol)
        {
            string dateVal = "COALESCE(" + dateCol + ")";
            if (DB.IsPostgreSQL())
            {
                return "CAST(CURRENT_DATE - CAST(" + dateVal + " AS DATE) AS INTEGER)";
            }
            return "TRUNC(SYSDATE - " + dateVal + ")";
        }

        /// <summary>
        /// Aging source per the source specification: M_Transaction, slabs by
        /// MovementDate age, quantities summed from MovementQty. Only inbound
        /// movements (MovementQty &gt; 0) carry stock into a slab - issues,
        /// shipments and internal use are consumption, not aging stock. A reversed movement
        /// (IsReversed = 'Y', where the column exists) is excluded the same way - it was
        /// cancelled and never actually put stock into the position.
        /// M_Locator_ID is carried through (not just M_Warehouse_ID) so GetBucketDetail can join
        /// back to the transaction's ACTUAL locator instead of every locator in the warehouse.
        /// </summary>
        private static string AgingTransactionSql(string warehouseFilter, string extraWhere)
        {
            string ageExpr = GetAgeDaysExpression("t.MovementDate, t.Created");
            string reversedFilter = TransactionHasIsReversed()
                ? " AND COALESCE(t.IsReversed, 'N') = 'N'"
                : "";

            return @"SELECT t.M_Product_ID,
                           COALESCE(t.M_AttributeSetInstance_ID, 0) AS M_AttributeSetInstance_ID,
                           t.M_Locator_ID,
                           loc.M_Warehouse_ID,
                           t.MovementQty,
                           " + ageExpr + @" AS AgeDays
                    FROM M_Transaction t
                    JOIN M_Locator loc ON (t.M_Locator_ID = loc.M_Locator_ID)" +
                    " WHERE t.IsActive = 'Y' AND t.MovementQty > 0" + reversedFilter + warehouseFilter + extraWhere;
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
        /// Gets the quantity of aging stock per slab: Fresh Stock (0-30),
        /// Normal Turnover (31-90), Slow Moving (91-180), Dead Stock (180+).
        /// Quantities come from M_Transaction.MovementQty slabs by MovementDate.
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

            IDataReader dr = null;
            try
            {
                string whFilter = "";
                if (warehouseId.HasValue && warehouseId.Value > 0)
                {
                    whFilter = " AND loc.M_Warehouse_ID = " + warehouseId.Value;
                }

                string txSql = AgingTransactionSql(whFilter, "");

                // Role access applies to the plain SELECT before the aggregate wrapper
                // (AddAccessSQL appends its predicate at the end of the statement).
                txSql = MRole.GetDefault(ctx).AddAccessSQL(txSql, "t", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                string sql = @"SELECT
                                  SUM(CASE WHEN AgeDays <= 30 THEN MovementQty ELSE 0 END) AS B0_30,
                                  SUM(CASE WHEN AgeDays > 30 AND AgeDays <= 90 THEN MovementQty ELSE 0 END) AS B31_90,
                                  SUM(CASE WHEN AgeDays > 90 AND AgeDays <= 180 THEN MovementQty ELSE 0 END) AS B91_180,
                                  SUM(CASE WHEN AgeDays > 180 THEN MovementQty ELSE 0 END) AS B180_Plus
                               FROM (" + txSql + @") aged";

                dr = DB.ExecuteReader(sql, null, null);
                if (dr != null && dr.Read())
                {
                    b0_30 = Util.GetValueOfDecimal(dr["B0_30"]);
                    b31_90 = Util.GetValueOfDecimal(dr["B31_90"]);
                    b91_180 = Util.GetValueOfDecimal(dr["B91_180"]);
                    b180_plus = Util.GetValueOfDecimal(dr["B180_Plus"]);
                }
            }
            catch (Exception ex)
            {
                _log.Severe("VAS_163_InventoryAgingReportWidgetController.GetAgingSummary: " + ex.Message);
            }
            finally
            {
                if (dr != null)
                {
                    dr.Close();
                    dr.Dispose();
                }
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
        /// Gets per-product aging quantities for a specific slab and optional
        /// warehouse filter, from M_Transaction (MovementQty summed by product /
        /// ASI / warehouse, AgeDays = age of the newest inbound in the slab).
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
            IDataReader dr = null;
            try
            {
                string whFilter = "";
                if (warehouseId.HasValue && warehouseId.Value > 0)
                {
                    whFilter = " AND loc.M_Warehouse_ID = " + warehouseId.Value;
                }

                // Must match AgingTransactionSql's own internal AgeDays expression (and therefore
                // GetAgingSummary's bucketing) exactly: COALESCE(MovementDate, Created), not
                // MovementDate alone. A transaction row with a null MovementDate (Created still
                // set) ages correctly in the summary via that fallback, but this filter's bucket
                // comparison (e.g. "AND ageExpr > 30 AND ageExpr <= 90") evaluates to NULL - not
                // true - for such a row when the fallback is missing, silently dropping it from
                // the WHERE clause. That is what made a bucket read real quantity on the summary
                // card (878/120,888/151/23,983) yet return "0 products" in this drill-down.
                string ageExpr = GetAgeDaysExpression("t.MovementDate, t.Created");

                string ageClause = "";
                if (bucketId == "0-30")
                {
                    ageClause = " AND " + ageExpr + " <= 30";
                }
                else if (bucketId == "31-90")
                {
                    ageClause = " AND " + ageExpr + " > 30 AND " + ageExpr + " <= 90";
                }
                else if (bucketId == "91-180")
                {
                    ageClause = " AND " + ageExpr + " > 90 AND " + ageExpr + " <= 180";
                }
                else if (bucketId == "180+")
                {
                    ageClause = " AND " + ageExpr + " > 180";
                }

                string txSql = AgingTransactionSql(whFilter, ageClause);

                // Role access applies to the plain SELECT before the aggregate wrapper.
                txSql = MRole.GetDefault(ctx).AddAccessSQL(txSql, "t", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                // asi.Description is NVARCHAR2 (national character set); 'Standard' is a plain
                // literal. COALESCE across the two raises ORA-12704 "character set mismatch", the
                // whole statement fails, the catch below swallows it and the endpoint returns an
                // empty list - which the modal renders as a blank popup. The fallback is applied in
                // C# instead: no charset mixing, and it stays portable to PostgreSQL (which has
                // neither Oracle's N'' literal nor a to_char(text) overload).
                // LocatorCombination is the full "Warehouse.Aisle.Bin.Level"-style locator name;
                // Value alone is the numeric surrogate code, which read like a raw locator id to
                // the user. Matches the class header's own documented mapping (COALESCE
                // LocatorCombination/Value), which this query had not actually been applying.
                string locatorSql = LocatorHasCombination()
                    ? "COALESCE(whLoc.LocatorCombination, whLoc.Value)"
                    : "whLoc.Value";

                // Claude, 2026-09-25: whLoc used to join ON (whLoc.M_Warehouse_ID =
                // aged.M_Warehouse_ID) - every locator in the warehouse, not the transaction's
                // OWN locator (aged never carried M_Locator_ID at all). That fanned each
                // product/attribute out across every locator the warehouse has, duplicating its
                // full summed quantity onto each one (e.g. "Lenovo Laptop" showing qty 40 under
                // BOTH Locator 1234 and Locator 4321, when the real stock sits in only one).
                // AgingTransactionSql now carries M_Locator_ID through, so this joins to the
                // exact locator instead, and SUM(aged.MovementQty) aggregates per-locator
                // correctly.
                string sql = @"SELECT p.Name AS ProductName,
                                      asi.Description AS AttributeDesc,
                                      w.Name AS WarehouseName,
                                      " + locatorSql + @" AS LocatorValue,
                                      SUM(aged.MovementQty) AS SlabQty,
                                      MIN(aged.AgeDays) AS AgeDays
                               FROM (" + txSql + @") aged
                               JOIN M_Product p ON (aged.M_Product_ID = p.M_Product_ID)
                               JOIN M_Locator whLoc ON (whLoc.M_Locator_ID = aged.M_Locator_ID)
                               JOIN M_Warehouse w ON (aged.M_Warehouse_ID = w.M_Warehouse_ID)
                               LEFT JOIN M_AttributeSetInstance asi ON (aged.M_AttributeSetInstance_ID = asi.M_AttributeSetInstance_ID)
                               GROUP BY p.Name, asi.Description, w.Name, " + locatorSql + @"
                               ORDER BY AgeDays DESC, p.Name ASC";

                dr = DB.ExecuteReader(sql, null, null);
                while (dr != null && dr.Read())
                {
                    // No attribute set instance -> BLANK, not a placeholder (user request
                    // 2026-08-29). It used to fall back to
                    // Msg.GetMsg(ctx, "VAS_Standard") ?? "Standard", which carried the usual
                    // Msg.GetMsg trap too: that call returns "[VAS_Standard]" rather than null
                    // when the AD_Message row is missing, so the "??" never fired.
                    string attribute = Util.GetValueOfString(dr["AttributeDesc"]);

                    lines.Add(new
                    {
                        product = Util.GetValueOfString(dr["ProductName"]),
                        attribute = attribute,
                        warehouse = Util.GetValueOfString(dr["WarehouseName"]),
                        locator = Util.GetValueOfString(dr["LocatorValue"]),
                        qty = Util.GetValueOfDecimal(dr["SlabQty"]),
                        ageDays = Util.GetValueOfInt(dr["AgeDays"])
                    });
                }
            }
            catch (Exception ex)
            {
                _log.Severe("VAS_163_InventoryAgingReportWidgetController.GetBucketDetail: " + ex.Message);
            }
            finally
            {
                if (dr != null)
                {
                    dr.Close();
                    dr.Dispose();
                }
            }

            return Json(new { details = lines, totalCount = lines.Count }, JsonRequestBehavior.AllowGet);
        }
    }
}

