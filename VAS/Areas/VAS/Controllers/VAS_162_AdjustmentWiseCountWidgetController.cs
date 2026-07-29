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
     * TABLE & FIELD MAPPING FOR ADJUSTMENT WISE COUNT:
     * - Adjustment Header: M_Inventory (DocStatus IN ('CO', 'CL'), MovementDate for filtering)
     * - Adjustment Lines: M_InventoryLine (M_InventoryLine_ID, M_Inventory_ID, M_Product_ID, M_AttributeSetInstance_ID, QtyBook, QtyCount)
     * - Product Master: M_Product (M_Product_ID, Name)
     * - Variant/Attribute: M_AttributeSetInstance (M_AttributeSetInstance_ID, Description)
     * Cross-Database: COALESCE used for Oracle and PostgreSQL compatibility.
     */

    [AjaxAuthorize]
    [AjaxSessionFilter]
    public class VAS_162_AdjustmentWiseCountWidgetController : Controller
    {
        private static readonly VLogger _log = VLogger.GetVLogger(typeof(VAS_162_AdjustmentWiseCountWidgetController));

        /// <summary>
        /// Gets available distinct years from inventory adjustment records.
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
                _log.Severe("VAS_162_AdjustmentWiseCountWidgetController.GetAvailableYears: " + ex.Message);
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
        /// Gets summary data for As-on-Date Count and Quantity Difference for selected month & year.
        /// </summary>
        [HttpGet]
        public JsonResult GetSummary(int month, int year)
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null)
            {
                return Json(new { error = "Unauthorized context." }, JsonRequestBehavior.AllowGet);
            }

            int asOnDateRecordCount = 0;
            int qtyDiffRecordCount = 0;
            decimal netDiffQty = 0;

            IDataReader dr = null;
            try
            {
                DateTime startDate = new DateTime(year, month, 1);
                DateTime endDate = startDate.AddMonths(1).AddDays(-1);

                string sql = @"SELECT i.M_Inventory_ID,
                                       (SELECT COUNT(*) FROM M_InventoryLine il WHERE il.M_Inventory_ID = i.M_Inventory_ID) AS LineCount,
                                       (SELECT COALESCE(SUM(il.QtyCount - il.QtyBook), 0) FROM M_InventoryLine il WHERE il.M_Inventory_ID = i.M_Inventory_ID) AS TotalDiff
                                FROM M_Inventory i
                                WHERE i.IsActive = 'Y' 
                                  AND i.DocStatus IN ('CO', 'CL')
                                  AND i.MovementDate >= " + DB.TO_DATE(startDate, true) + @"
                                  AND i.MovementDate <= " + DB.TO_DATE(endDate, true);

                sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "i", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                dr = DB.ExecuteReader(sql, null, null);
                while (dr != null && dr.Read())
                {
                    int lineCount = Util.GetValueOfInt(dr["LineCount"]);
                    decimal diff = Util.GetValueOfDecimal(dr["TotalDiff"]);

                    if (lineCount > 0)
                    {
                        asOnDateRecordCount++;
                        qtyDiffRecordCount++;
                        netDiffQty += diff;
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Severe("VAS_162_AdjustmentWiseCountWidgetController.GetSummary: " + ex.Message);
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
                asOnDateRecordCount = asOnDateRecordCount,
                qtyDiffRecordCount = qtyDiffRecordCount,
                netDiffQty = netDiffQty
            }, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Gets line-level details for modal grid for selected period and adjustment type.
        /// </summary>
        [HttpGet]
        public JsonResult GetDetails(string type, int month, int year)
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null)
            {
                return Json(new { error = "Unauthorized context." }, JsonRequestBehavior.AllowGet);
            }

            List<object> details = new List<object>();
            IDataReader dr = null;
            try
            {
                DateTime startDate = new DateTime(year, month, 1);
                DateTime endDate = startDate.AddMonths(1).AddDays(-1);

                string sql = @"SELECT p.Name AS ProductName, 
                                       COALESCE(asi.Description, 'Standard') AS AttributeDesc, 
                                       il.QtyBook, 
                                       (il.QtyCount - il.QtyBook) AS DiffQty, 
                                       il.QtyCount AS AsOnDateCount, 
                                       i.MovementDate
                                FROM M_InventoryLine il
                                JOIN M_Inventory i ON (il.M_Inventory_ID = i.M_Inventory_ID)
                                JOIN M_Product p ON (il.M_Product_ID = p.M_Product_ID)
                                LEFT JOIN M_AttributeSetInstance asi ON (il.M_AttributeSetInstance_ID = asi.M_AttributeSetInstance_ID)
                                WHERE i.IsActive = 'Y' 
                                  AND i.DocStatus IN ('CO', 'CL')
                                  AND i.MovementDate >= " + DB.TO_DATE(startDate, true) + @"
                                  AND i.MovementDate <= " + DB.TO_DATE(endDate, true);

                sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "i", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                sql += " ORDER BY p.Name ASC";

                dr = DB.ExecuteReader(sql, null, null);
                while (dr != null && dr.Read())
                {
                    decimal diffQty = Util.GetValueOfDecimal(dr["DiffQty"]);

                    if (type == "QTY_DIFF" && diffQty == 0)
                    {
                        // Skip lines with no diff if strictly querying quantity differences
                    }

                    details.Add(new
                    {
                        product = Util.GetValueOfString(dr["ProductName"]),
                        attribute = Util.GetValueOfString(dr["AttributeDesc"]),
                        qty = Util.GetValueOfDecimal(dr["QtyBook"]),
                        diffQty = diffQty,
                        asOnDateCount = Util.GetValueOfDecimal(dr["AsOnDateCount"])
                    });
                }
            }
            catch (Exception ex)
            {
                _log.Severe("VAS_162_AdjustmentWiseCountWidgetController.GetDetails: " + ex.Message);
            }
            finally
            {
                if (dr != null)
                {
                    dr.Close();
                    dr.Dispose();
                }
            }

            return Json(new { details = details }, JsonRequestBehavior.AllowGet);
        }
    }
}
