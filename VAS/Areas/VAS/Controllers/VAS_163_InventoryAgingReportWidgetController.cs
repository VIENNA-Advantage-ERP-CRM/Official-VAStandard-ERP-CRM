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
     * - Locator: M_Locator (M_Locator_ID, M_Warehouse_ID, Value)
     * - Warehouse: M_Warehouse (M_Warehouse_ID, Value, Name)
     * Cross-Database: Age calculation uses DB.IsPostgreSQL() vs Oracle DB.TO_DATE/SYSDATE and ANSI COALESCE.
     */

    [AjaxAuthorize]
    [AjaxSessionFilter]
    public class VAS_163_InventoryAgingReportWidgetController : Controller
    {
        private static readonly VLogger _log = VLogger.GetVLogger(typeof(VAS_163_InventoryAgingReportWidgetController));

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

                string ageExpr = GetAgeDaysExpression("s.DateLastInventory, s.Created");

                string sql = @"SELECT s.M_Product_ID, 
                                      s.M_AttributeSetInstance_ID, 
                                      MIN(" + ageExpr + @") AS AgeDays
                               FROM M_Storage s
                               JOIN M_Locator loc ON (s.M_Locator_ID = loc.M_Locator_ID)
                               WHERE s.IsActive = 'Y' AND s.QtyOnHand > 0" + whFilter;

                sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "s", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                sql += " GROUP BY s.M_Product_ID, s.M_AttributeSetInstance_ID";

                dr = DB.ExecuteReader(sql, null, null);
                while (dr != null && dr.Read())
                {
                    int ageDays = Util.GetValueOfInt(dr["AgeDays"]);
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

                string ageExpr = GetAgeDaysExpression("s.DateLastInventory, s.Created");

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
                                      loc.Value AS LocatorValue, 
                                      s.QtyOnHand, 
                                      " + ageExpr + @" AS AgeDays
                               FROM M_Storage s
                               JOIN M_Product p ON (s.M_Product_ID = p.M_Product_ID)
                               JOIN M_Locator loc ON (s.M_Locator_ID = loc.M_Locator_ID)
                               JOIN M_Warehouse w ON (loc.M_Warehouse_ID = w.M_Warehouse_ID)
                               LEFT JOIN M_AttributeSetInstance asi ON (s.M_AttributeSetInstance_ID = asi.M_AttributeSetInstance_ID)
                               WHERE s.IsActive = 'Y' AND s.QtyOnHand > 0" + whFilter + ageClause;

                sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "s", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                sql += " ORDER BY AgeDays DESC, p.Name ASC";

                dr = DB.ExecuteReader(sql, null, null);
                while (dr != null && dr.Read())
                {
                    string attribute = Util.GetValueOfString(dr["AttributeDesc"]);
                    if (string.IsNullOrEmpty(attribute))
                    {
                        attribute = Msg.GetMsg(ctx, "VAS_Standard") ?? "Standard";
                    }

                    lines.Add(new
                    {
                        product = Util.GetValueOfString(dr["ProductName"]),
                        attribute = attribute,
                        warehouse = Util.GetValueOfString(dr["WarehouseName"]),
                        locator = Util.GetValueOfString(dr["LocatorValue"]),
                        qty = Util.GetValueOfDecimal(dr["QtyOnHand"]),
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

