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

                string sql = @"SELECT
                                   COALESCE(SUM(CASE WHEN il.AdjustmentType = 'A' THEN 1 ELSE 0 END), 0) AS AsOnDateCount,
                                   COALESCE(SUM(CASE WHEN il.AdjustmentType = 'D' THEN 1 ELSE 0 END), 0) AS QtyDiffCount,
                                   COALESCE(SUM(CASE WHEN il.AdjustmentType = 'D' THEN COALESCE(il.DifferenceQty, COALESCE(il.QtyCount, 0) - COALESCE(il.QtyBook, 0)) ELSE 0 END), 0) AS NetDiffQty
                               FROM M_Inventory i
                               JOIN M_InventoryLine il ON (il.M_Inventory_ID = i.M_Inventory_ID)
                               WHERE i.IsActive = 'Y' 
                                 AND il.IsActive = 'Y'
                                 AND COALESCE(i.IsInternalUse, 'N') = 'N'
                                 AND i.DocStatus IN ('CO', 'CL')
                                 AND il.AdjustmentType IN ('A', 'D')
                                 AND i.MovementDate >= " + DB.TO_DATE(startDate, true) + @"
                                 AND i.MovementDate <= " + DB.TO_DATE(endDate, true);

                sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "i", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                dr = DB.ExecuteReader(sql, null, null);
                if (dr != null && dr.Read())
                {
                    asOnDateRecordCount = Util.GetValueOfInt(dr["AsOnDateCount"]);
                    qtyDiffRecordCount = Util.GetValueOfInt(dr["QtyDiffCount"]);
                    netDiffQty = Util.GetValueOfDecimal(dr["NetDiffQty"]);
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

                string typeFilter = "";
                if (type == "AS_ON_DATE" || type == "A")
                {
                    typeFilter = " AND il.AdjustmentType = 'A'";
                }
                else if (type == "QTY_DIFF" || type == "D")
                {
                    typeFilter = " AND il.AdjustmentType = 'D'";
                }

                // asi.Description is selected raw. It was COALESCE(asi.Description, N'Standard'),
                // which printed "Standard" on every line that simply has no attribute set. The
                // source prompt already specifies "Attribute = M_AttributeSetInstance.Description,
                // or an EMPTY display when attribute ID is null/0"; the user confirmed the same.
                // Raw select also avoids the ORA-12704 charset trap the N'' prefix was working
                // around (see VAS_163 / VAS_165), and keeps the query portable to PostgreSQL,
                // which has no N'' literal.
                string sql = @"SELECT p.Name AS ProductName,
                                       asi.Description AS AttributeDesc,
                                       i.DocumentNo AS DocumentNo,
                                       COALESCE(il.QtyBook, 0) AS QtyBook,
                                       COALESCE(il.DifferenceQty, COALESCE(il.QtyCount, 0) - COALESCE(il.QtyBook, 0)) AS DiffQty, 
                                       COALESCE(il.AsOnDateCount, COALESCE(il.QtyCount, COALESCE(il.QtyBook, 0) + COALESCE(il.DifferenceQty, 0))) AS AsOnDateCount, 
                                       i.MovementDate
                                FROM M_InventoryLine il
                                JOIN M_Inventory i ON (il.M_Inventory_ID = i.M_Inventory_ID)
                                JOIN M_Product p ON (il.M_Product_ID = p.M_Product_ID)
                                LEFT JOIN M_AttributeSetInstance asi ON (il.M_AttributeSetInstance_ID = asi.M_AttributeSetInstance_ID)
                                WHERE i.IsActive = 'Y' 
                                  AND il.IsActive = 'Y'
                                  AND COALESCE(i.IsInternalUse, 'N') = 'N'
                                  AND i.DocStatus IN ('CO', 'CL')
                                  AND i.MovementDate >= " + DB.TO_DATE(startDate, true) + @"
                                  AND i.MovementDate <= " + DB.TO_DATE(endDate, true) + typeFilter;

                sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "i", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                sql += " ORDER BY p.Name ASC";

                dr = DB.ExecuteReader(sql, null, null);
                while (dr != null && dr.Read())
                {
                    details.Add(new
                    {
                        product = Util.GetValueOfString(dr["ProductName"]),
                        // Empty string when the line carries no attribute set instance - no
                        // "Standard" placeholder.
                        attribute = Util.GetValueOfString(dr["AttributeDesc"]),
                        documentNo = Util.GetValueOfString(dr["DocumentNo"]),
                        qty = Util.GetValueOfDecimal(dr["QtyBook"]),
                        diffQty = Util.GetValueOfDecimal(dr["DiffQty"]),
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

