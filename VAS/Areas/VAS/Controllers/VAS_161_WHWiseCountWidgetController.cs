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
     * TABLE & FIELD MAPPING FOR WH WISE COUNT WIDGET:
     * - Warehouse: M_Warehouse (M_Warehouse_ID, Value, Name)
     * - Inventory Header: M_Inventory (M_Inventory_ID, M_Warehouse_ID, MovementDate, DocStatus IN ('CO', 'CL'))
     * - Inventory Line: M_InventoryLine (M_InventoryLine_ID, M_Inventory_ID, M_Product_ID, M_Locator_ID, M_AttributeSetInstance_ID, QtyCount, C_Charge_ID)
     * - Locator: M_Locator (M_Locator_ID, Value)
     * - Product: M_Product (M_Product_ID, Name)
     * - Attribute: M_AttributeSetInstance (M_AttributeSetInstance_ID, Description)
     * Cross-Database: COALESCE used for Oracle and PostgreSQL compatibility.
     */

    [AjaxAuthorize]
    [AjaxSessionFilter]
    public class VAS_161_WHWiseCountWidgetController : Controller
    {
        private static readonly VLogger _log = VLogger.GetVLogger(typeof(VAS_161_WHWiseCountWidgetController));

        /// <summary>
        /// Gets available distinct years from inventory count records.
        /// </summary>
        [HttpGet]
        public JsonResult GetAvailableYears()
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null)
            {
                return Json(new { error = "Unauthorized context." }, JsonRequestBehavior.AllowGet);
            }

            List<int> years = new List<int>();
            IDataReader dr = null;
            try
            {
                string sql = "SELECT DISTINCT EXTRACT(YEAR FROM i.MovementDate) AS MovementYear FROM M_Inventory i WHERE i.IsActive = 'Y' AND i.MovementDate IS NOT NULL";
                sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "i", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                sql += " ORDER BY MovementYear DESC";

                dr = DB.ExecuteReader(sql, null, null);
                while (dr != null && dr.Read())
                {
                    int year = Util.GetValueOfInt(dr["MovementYear"]);
                    if (year > 0 && !years.Contains(year))
                    {
                        years.Add(year);
                    }
                }

                if (years.Count == 0)
                {
                    int currentYear = DateTime.Now.Year;
                    years.Add(currentYear);
                    years.Add(currentYear - 1);
                    years.Add(currentYear - 2);
                }
            }
            catch (Exception ex)
            {
                _log.Severe("VAS_161_WHWiseCountWidgetController.GetAvailableYears: " + ex.Message);
            }
            finally
            {
                if (dr != null)
                {
                    dr.Close();
                    dr.Dispose();
                }
            }

            return Json(new { years = years }, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Query A: Warehouse Summary for selected month and year.
        /// </summary>
        [HttpGet]
        public JsonResult GetWarehouseSummary(int month, int year)
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
                DateTime startDate = new DateTime(year, month, 1);
                DateTime endDate = startDate.AddMonths(1).AddDays(-1);

                string sql = @"SELECT w.M_Warehouse_ID, 
                                      w.Value AS WarehouseCode, 
                                      w.Name AS WarehouseName, 
                                      COUNT(DISTINCT i.M_Inventory_ID) AS SessionCount, 
                                      COALESCE(SUM(il.QtyCount), 0) AS TotalQtyCounted
                               FROM M_Inventory i 
                               JOIN M_Warehouse w ON (i.M_Warehouse_ID = w.M_Warehouse_ID) 
                               JOIN M_InventoryLine il ON (i.M_Inventory_ID = il.M_Inventory_ID) 
                               WHERE i.IsActive = 'Y' 
                                 AND i.DocStatus IN ('CO', 'CL') 
                                 AND i.MovementDate >= " + DB.TO_DATE(startDate, true) + @" 
                                 AND i.MovementDate <= " + DB.TO_DATE(endDate, true);

                sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "i", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                sql += " GROUP BY w.M_Warehouse_ID, w.Value, w.Name ORDER BY TotalQtyCounted DESC";

                dr = DB.ExecuteReader(sql, null, null);
                while (dr != null && dr.Read())
                {
                    int whId = Util.GetValueOfInt(dr["M_Warehouse_ID"]);
                    string locatorsStr = GetWarehouseLocatorsString(ctx, whId, startDate, endDate);

                    list.Add(new
                    {
                        warehouseId = whId,
                        warehouseCode = Util.GetValueOfString(dr["WarehouseCode"]),
                        warehouseName = Util.GetValueOfString(dr["WarehouseName"]),
                        locators = locatorsStr,
                        sessionCount = Util.GetValueOfInt(dr["SessionCount"]),
                        totalQtyCounted = Util.GetValueOfDecimal(dr["TotalQtyCounted"])
                    });
                }
            }
            catch (Exception ex)
            {
                _log.Severe("VAS_161_WHWiseCountWidgetController.GetWarehouseSummary: " + ex.Message);
            }
            finally
            {
                if (dr != null)
                {
                    dr.Close();
                    dr.Dispose();
                }
            }

            return Json(new { data = list }, JsonRequestBehavior.AllowGet);
        }

        private string GetWarehouseLocatorsString(Ctx ctx, int warehouseId, DateTime startDate, DateTime endDate)
        {
            List<string> locators = new List<string>();
            IDataReader dr = null;
            try
            {
                string sql = @"SELECT DISTINCT loc.Value AS LocatorValue 
                               FROM M_InventoryLine il 
                               JOIN M_Inventory i ON (il.M_Inventory_ID = i.M_Inventory_ID) 
                               JOIN M_Locator loc ON (il.M_Locator_ID = loc.M_Locator_ID) 
                               WHERE i.IsActive = 'Y' 
                                 AND i.DocStatus IN ('CO', 'CL') 
                                 AND i.M_Warehouse_ID = " + warehouseId + @" 
                                 AND i.MovementDate >= " + DB.TO_DATE(startDate, true) + @" 
                                 AND i.MovementDate <= " + DB.TO_DATE(endDate, true);

                sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "i", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                sql += " ORDER BY loc.Value ASC";

                dr = DB.ExecuteReader(sql, null, null);
                while (dr != null && dr.Read())
                {
                    string locVal = Util.GetValueOfString(dr["LocatorValue"]);
                    if (!string.IsNullOrEmpty(locVal) && !locators.Contains(locVal))
                    {
                        locators.Add(locVal);
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Severe("VAS_161_WHWiseCountWidgetController.GetWarehouseLocatorsString: " + ex.Message);
            }
            finally
            {
                if (dr != null)
                {
                    dr.Close();
                    dr.Dispose();
                }
            }

            return locators.Count > 0 ? string.Join(" · ", locators) : "None";
        }

        /// <summary>
        /// Query B: Warehouse Detail lines for selected month, year, and warehouse ID.
        /// </summary>
        [HttpGet]
        public JsonResult GetWarehouseDetail(int month, int year, int warehouseId)
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null)
            {
                return Json(new { error = "Unauthorized context." }, JsonRequestBehavior.AllowGet);
            }

            List<object> lines = new List<object>();
            HashSet<int> sessionIds = new HashSet<int>();
            HashSet<string> locatorValues = new HashSet<string>();
            decimal totalQty = 0;

            IDataReader dr = null;
            try
            {
                DateTime startDate = new DateTime(year, month, 1);
                DateTime endDate = startDate.AddMonths(1).AddDays(-1);

                // asi.Description is NVARCHAR2 (national character set); 'Standard' is a plain
                // literal. COALESCE across the two raises ORA-12704 "character set mismatch", the
                // statement fails, the catch below swallows it and the endpoint returns an empty
                // list - which the modal renders as a blank popup. Reproduced on DB 2.
                // Selected raw; the attribute is left BLANK when the line has none, per the user's
                // instruction not to display a "Standard" placeholder.
                string sql = @"SELECT p.Name AS ProductName,
                                      asi.Description AS AttributeDesc,
                                      loc.Value AS LocatorValue,
                                      CASE WHEN il.C_Charge_ID IS NOT NULL THEN 'Charge Account' ELSE 'Inventory Difference' END AS InventoryType, 
                                      il.QtyCount AS Qty, 
                                      i.M_Inventory_ID 
                               FROM M_InventoryLine il 
                               JOIN M_Inventory i ON (il.M_Inventory_ID = i.M_Inventory_ID) 
                               JOIN M_Product p ON (il.M_Product_ID = p.M_Product_ID) 
                               LEFT JOIN M_Locator loc ON (il.M_Locator_ID = loc.M_Locator_ID) 
                               LEFT JOIN M_AttributeSetInstance asi ON (il.M_AttributeSetInstance_ID = asi.M_AttributeSetInstance_ID) 
                               WHERE i.IsActive = 'Y' 
                                 AND i.DocStatus IN ('CO', 'CL') 
                                 AND i.M_Warehouse_ID = " + warehouseId + @" 
                                 AND i.MovementDate >= " + DB.TO_DATE(startDate, true) + @" 
                                 AND i.MovementDate <= " + DB.TO_DATE(endDate, true);

                sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "i", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                sql += " ORDER BY p.Name ASC";

                dr = DB.ExecuteReader(sql, null, null);
                while (dr != null && dr.Read())
                {
                    int invId = Util.GetValueOfInt(dr["M_Inventory_ID"]);
                    string locVal = Util.GetValueOfString(dr["LocatorValue"]);
                    decimal qty = Util.GetValueOfDecimal(dr["Qty"]);

                    sessionIds.Add(invId);
                    if (!string.IsNullOrEmpty(locVal))
                    {
                        locatorValues.Add(locVal);
                    }
                    totalQty += qty;

                    lines.Add(new
                    {
                        product = Util.GetValueOfString(dr["ProductName"]),
                        attribute = Util.GetValueOfString(dr["AttributeDesc"]),
                        locator = !string.IsNullOrEmpty(locVal) ? locVal : "N/A",
                        inventoryType = Util.GetValueOfString(dr["InventoryType"]),
                        qty = qty
                    });
                }
            }
            catch (Exception ex)
            {
                _log.Severe("VAS_161_WHWiseCountWidgetController.GetWarehouseDetail: " + ex.Message);
            }
            finally
            {
                if (dr != null)
                {
                    dr.Close();
                    dr.Dispose();
                }
            }

            return Json(new
            {
                lines = lines,
                totalLines = lines.Count,
                sessionCount = sessionIds.Count,
                locatorCount = locatorValues.Count,
                totalQty = totalQty
            }, JsonRequestBehavior.AllowGet);
        }
    }
}

