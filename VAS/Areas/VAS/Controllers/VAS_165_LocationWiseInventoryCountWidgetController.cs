using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Web.Mvc;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VAS.Controllers
{
    /*
     * TABLE & FIELD MAPPING FOR LOCATION WISE INVENTORY COUNT WIDGET:
     * - Inventory Header: M_Inventory (M_Inventory_ID, MovementDate, DocStatus IN ('CO', 'CL'))
     * - Inventory Line: M_InventoryLine (M_InventoryLine_ID, M_Inventory_ID, M_Product_ID, M_Locator_ID, M_AttributeSetInstance_ID, QtyCount, C_Charge_ID)
     * - Locator: M_Locator (M_Locator_ID, M_Warehouse_ID, Value)
     * - Warehouse: M_Warehouse (M_Warehouse_ID, Value, Name)
     * - Product: M_Product (M_Product_ID, Name)
     * - Attribute: M_AttributeSetInstance (M_AttributeSetInstance_ID, Description)
     * Cross-Database: COALESCE used for Oracle and PostgreSQL compatibility.
     */

    [AjaxAuthorize]
    [AjaxSessionFilter]
    public class VAS_165_LocationWiseInventoryCountWidgetController : Controller
    {
        private static readonly VLogger _log = VLogger.GetVLogger(typeof(VAS_165_LocationWiseInventoryCountWidgetController));

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
                _log.Severe("VAS_165_LocationWiseInventoryCountWidgetController.GetAvailableYears: " + ex.Message);
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
        /// Queries summary of counts grouped by location for a selected month and year.
        /// </summary>
        [HttpGet]
        public JsonResult GetLocationSummary(int month, int year)
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

                string sql = @"SELECT loc.Value AS LocatorCode, 
                                      w.Name AS WarehouseName, 
                                      COUNT(DISTINCT i.M_Inventory_ID) AS SessionCount, 
                                      COALESCE(SUM(il.QtyCount), 0) AS TotalQtyCounted
                               FROM M_InventoryLine il 
                               JOIN M_Inventory i ON (il.M_Inventory_ID = i.M_Inventory_ID) 
                               JOIN M_Locator loc ON (il.M_Locator_ID = loc.M_Locator_ID) 
                               JOIN M_Warehouse w ON (loc.M_Warehouse_ID = w.M_Warehouse_ID) 
                               WHERE i.IsActive = 'Y' 
                                 AND i.DocStatus IN ('CO', 'CL') 
                                 AND i.MovementDate >= " + DB.TO_DATE(startDate, true) + @" 
                                 AND i.MovementDate <= " + DB.TO_DATE(endDate, true);

                sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "i", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                sql += " GROUP BY loc.Value, w.Name ORDER BY TotalQtyCounted DESC";

                dr = DB.ExecuteReader(sql, null, null);
                while (dr != null && dr.Read())
                {
                    list.Add(new
                    {
                        locator = Util.GetValueOfString(dr["LocatorCode"]),
                        warehouse = Util.GetValueOfString(dr["WarehouseName"]),
                        sessionCount = Util.GetValueOfInt(dr["SessionCount"]),
                        totalQtyCounted = Util.GetValueOfDecimal(dr["TotalQtyCounted"])
                    });
                }
            }
            catch (Exception ex)
            {
                _log.Severe("VAS_165_LocationWiseInventoryCountWidgetController.GetLocationSummary: " + ex.Message);
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

        /// <summary>
        /// Queries line-level count details for a specific location in a selected month and year.
        /// </summary>
        [HttpGet]
        public JsonResult GetLocationDetail(int month, int year, string locatorCode)
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null)
            {
                return Json(new { error = "Unauthorized context." }, JsonRequestBehavior.AllowGet);
            }

            List<object> lines = new List<object>();
            HashSet<int> sessionIds = new HashSet<int>();
            decimal totalQty = 0;

            IDataReader dr = null;
            try
            {
                DateTime startDate = new DateTime(year, month, 1);
                DateTime endDate = startDate.AddMonths(1).AddDays(-1);

                // asi.Description is NVARCHAR2 (national character set); 'Standard' is a plain
                // literal. COALESCE across the two raises ORA-12704 "character set mismatch", the
                // statement fails, the catch below swallows it, and the endpoint returns an empty
                // list - which the modal renders as a blank popup. Reproduced on DB 2.
                // The fallback is applied in C# below instead; that also stays portable to
                // PostgreSQL, which has neither Oracle's N'' literal nor a to_char(text) overload.
                //
                // locatorCode is now a bind parameter. It arrives straight from the query string and
                // was being concatenated into the WHERE clause, so a value containing a quote both
                // broke the statement and was injectable.
                string sql = @"SELECT p.Name AS ProductName,
                                      asi.Description AS AttributeDesc,
                                      loc.Value AS LocatorValue,
                                      CASE WHEN il.C_Charge_ID IS NOT NULL THEN 'Charge Account' ELSE 'Inventory Difference' END AS InventoryType,
                                      il.QtyCount AS Qty,
                                      i.M_Inventory_ID
                               FROM M_InventoryLine il
                               JOIN M_Inventory i ON (il.M_Inventory_ID = i.M_Inventory_ID)
                               JOIN M_Product p ON (il.M_Product_ID = p.M_Product_ID)
                               JOIN M_Locator loc ON (il.M_Locator_ID = loc.M_Locator_ID)
                               LEFT JOIN M_AttributeSetInstance asi ON (il.M_AttributeSetInstance_ID = asi.M_AttributeSetInstance_ID)
                               WHERE i.IsActive = 'Y'
                                 AND i.DocStatus IN ('CO', 'CL')
                                 AND loc.Value = @LocatorCode
                                 AND i.MovementDate >= " + DB.TO_DATE(startDate, true) + @"
                                 AND i.MovementDate <= " + DB.TO_DATE(endDate, true);

                sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "i", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                sql += " ORDER BY p.Name ASC";

                SqlParameter[] parameters = { new SqlParameter("@LocatorCode", locatorCode ?? "") };

                dr = DB.ExecuteReader(sql, parameters);
                while (dr != null && dr.Read())
                {
                    int invId = Util.GetValueOfInt(dr["M_Inventory_ID"]);
                    decimal qty = Util.GetValueOfDecimal(dr["Qty"]);

                    sessionIds.Add(invId);
                    totalQty += qty;

                    string attribute = Util.GetValueOfString(dr["AttributeDesc"]);
                    if (string.IsNullOrEmpty(attribute))
                    {
                        attribute = Msg.GetMsg(ctx, "VAS_Standard") ?? "Standard";
                    }

                    lines.Add(new
                    {
                        product = Util.GetValueOfString(dr["ProductName"]),
                        attribute = attribute,
                        locator = Util.GetValueOfString(dr["LocatorValue"]),
                        inventoryType = Util.GetValueOfString(dr["InventoryType"]),
                        qty = qty
                    });
                }
            }
            catch (Exception ex)
            {
                _log.Severe("VAS_165_LocationWiseInventoryCountWidgetController.GetLocationDetail: " + ex.Message);
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
                totalQty = totalQty
            }, JsonRequestBehavior.AllowGet);
        }
    }
}
