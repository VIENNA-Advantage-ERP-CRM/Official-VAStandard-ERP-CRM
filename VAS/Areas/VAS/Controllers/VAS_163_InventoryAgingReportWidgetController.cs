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
     * - Storage / On-Hand: M_Storage (M_Product_ID, M_AttributeSetInstance_ID, M_Locator_ID, QtyOnHand, DateLastInventory, Created)
     * - Product Master: M_Product (M_Product_ID, Name)
     * - Attribute Instance: M_AttributeSetInstance (M_AttributeSetInstance_ID, Description)
     * - Locator: M_Locator (M_Locator_ID, M_Warehouse_ID, Value, LocatorCombination)
     *   Displayed as COALESCE(LocatorCombination, Value) - the prompt's own mapping says
     *   "M_Locator.LocatorCombination: preferred locator display / M_Locator.Value: locator
     *   display fallback". Value alone is a numeric surrogate on this data.
     * - Movement history: M_Transaction (M_Product_ID, M_AttributeSetInstance_ID, M_Locator_ID,
     *   MovementDate, MovementQty[, IsReversed]) - the aging basis.
     * - Warehouse: M_Warehouse (M_Warehouse_ID, Value, Name)
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

        /*
         * AGING BASIS (user instruction 2026-08-29: "Fetch inventory aging details from the
         * M_Transaction table and calculate the quantity under the slabs based on the MovementDate
         * and MovementQty fields").
         *
         * The age of one stock position is the age of the OLDEST INBOUND MOVEMENT that put stock
         * into it: MIN(M_Transaction.MovementDate) over rows with MovementQty > 0 for the same
         * Product + AttributeSetInstance + Locator.
         *
         * This REPLACES COALESCE(s.DateLastInventory, s.Created), which is what the widget used
         * before and is the reported "incorrect data": DateLastInventory is when the position was
         * last COUNTED and Created is when the storage row was first written - neither is when the
         * stock actually arrived, so a long-held item could look fresh and vice versa.
         *
         * The source prompt specifies a fuller FIFO/LIFO algorithm using M_TransactionAllocation
         * remaining layers. That allocator does not exist: M_TransactionAllocation is referenced
         * NOWHERE in this solution. The user chose the simple oldest-inbound-MovementDate basis on
         * 2026-08-29 rather than have it built blind. Recorded so the prompt is not "restored"
         * later by mistake.
         *
         * s.Created remains the fallback when a position has no inbound transaction at all, which
         * is the fallback the prompt itself names.
         */
        private static string GetAgingJoin()
        {
            string reversedFilter = TransactionHasIsReversed()
                ? " AND COALESCE(t.IsReversed, 'N') = 'N'"
                : "";

            return @"
                LEFT JOIN (
                    SELECT t.M_Product_ID,
                           COALESCE(t.M_AttributeSetInstance_ID, 0) AS M_AttributeSetInstance_ID,
                           t.M_Locator_ID,
                           MIN(t.MovementDate) AS FirstInboundDate
                    FROM M_Transaction t
                    WHERE t.MovementQty > 0" + reversedFilter + @"
                    GROUP BY t.M_Product_ID,
                             COALESCE(t.M_AttributeSetInstance_ID, 0),
                             t.M_Locator_ID
                ) tx ON (tx.M_Product_ID = s.M_Product_ID
                     AND tx.M_AttributeSetInstance_ID = COALESCE(s.M_AttributeSetInstance_ID, 0)
                     AND tx.M_Locator_ID = s.M_Locator_ID)";
        }

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
        /// Gets bucket summary product counts for 4 age buckets (0-30, 31-90, 91-180, 180+).
        /// </summary>
        [HttpGet]
        public JsonResult GetAgingSummary(int? warehouseId)
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null)
            {
                return Json(new { error = "Unauthorized context." }, JsonRequestBehavior.AllowGet);
            }

            int b0_30 = 0;
            int b31_90 = 0;
            int b91_180 = 0;
            int b180_plus = 0;

            IDataReader dr = null;
            try
            {
                string whFilter = "";
                if (warehouseId.HasValue && warehouseId.Value > 0)
                {
                    whFilter = " AND loc.M_Warehouse_ID = " + warehouseId.Value;
                }

                string ageExpr = GetAgeDaysExpression("tx.FirstInboundDate, s.Created");

                /*
                 * ONE ROW PER STOCK POSITION - Product + Attribute + Warehouse + Locator.
                 *
                 * This used to GROUP BY (M_Product_ID, M_AttributeSetInstance_ID) and take
                 * MIN(age), i.e. it collapsed every locator of a product into ONE counted item,
                 * while GetBucketDetail below lists one row per M_Storage row (which is per
                 * locator). That is exactly the reported defect: the tile said "1 product" and the
                 * popup then listed 2.
                 *
                 * The prompt settles the unit explicitly: "one displayed product count means one
                 * Product + Attribute + Warehouse + Locator stock position", and "the same Product
                 * + Attribute may be counted more than once when it exists in different warehouses
                 * or locators".
                 *
                 * M_Storage is already keyed per product + attribute + locator, so counting its
                 * rows IS that unit - and it makes the tile and the popup agree by construction
                 * rather than by two queries happening to match.
                 */
                string sql = @"SELECT " + ageExpr + @" AS AgeDays
                               FROM M_Storage s
                               JOIN M_Locator loc ON (s.M_Locator_ID = loc.M_Locator_ID)" + GetAgingJoin() + @"
                               WHERE s.IsActive = 'Y' AND s.QtyOnHand > 0" + whFilter;

                sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "s", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                dr = DB.ExecuteReader(sql, null, null);
                while (dr != null && dr.Read())
                {
                    // Clamp a future aging date to 0 rather than letting it go negative -
                    // the prompt requires it, and the previous code did not do it.
                    int ageDays = Math.Max(0, Util.GetValueOfInt(dr["AgeDays"]));
                    if (ageDays <= 30)
                    {
                        b0_30++;
                    }
                    else if (ageDays <= 90)
                    {
                        b31_90++;
                    }
                    else if (ageDays <= 180)
                    {
                        b91_180++;
                    }
                    else
                    {
                        b180_plus++;
                    }
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

            int total = b0_30 + b31_90 + b91_180 + b180_plus;

            return Json(new
            {
                b0_30 = b0_30,
                b31_90 = b31_90,
                b91_180 = b91_180,
                b180_plus = b180_plus,
                totalProducts = total
            }, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Gets product detail lines for a specific age bucket and optional warehouse filter.
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

                string ageExpr = GetAgeDaysExpression("tx.FirstInboundDate, s.Created");

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

                // asi.Description is NVARCHAR2 (national character set); 'Standard' is a plain
                // literal. COALESCE across the two raises ORA-12704 "character set mismatch", the
                // whole statement fails, the catch below swallows it and the endpoint returns an
                // empty list - which the modal renders as a blank popup. The fallback is applied in
                // C# instead: no charset mixing, and it stays portable to PostgreSQL (which has
                // neither Oracle's N'' literal nor a to_char(text) overload).
                string sql = @"SELECT p.Name AS ProductName,
                                      asi.Description AS AttributeDesc,
                                      w.Name AS WarehouseName,
                                      COALESCE(loc.LocatorCombination, loc.Value) AS LocatorValue, 
                                      s.QtyOnHand, 
                                      " + ageExpr + @" AS AgeDays
                               FROM M_Storage s
                               JOIN M_Product p ON (s.M_Product_ID = p.M_Product_ID)
                               JOIN M_Locator loc ON (s.M_Locator_ID = loc.M_Locator_ID)
                               JOIN M_Warehouse w ON (loc.M_Warehouse_ID = w.M_Warehouse_ID)
                               LEFT JOIN M_AttributeSetInstance asi ON (s.M_AttributeSetInstance_ID = asi.M_AttributeSetInstance_ID)" + GetAgingJoin() + @"
                               WHERE s.IsActive = 'Y' AND s.QtyOnHand > 0" + whFilter + ageClause;

                sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "s", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                sql += " ORDER BY AgeDays DESC, p.Name ASC";

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
                        qty = Util.GetValueOfDecimal(dr["QtyOnHand"]),
                        ageDays = Math.Max(0, Util.GetValueOfInt(dr["AgeDays"]))
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

