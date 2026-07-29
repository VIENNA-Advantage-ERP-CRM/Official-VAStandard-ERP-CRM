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
    [AjaxAuthorize]
    [AjaxSessionFilter]
    public class VAS_158_OpenCountSheetsWidgetController : Controller
    {
        private static readonly VLogger _log = VLogger.GetVLogger(typeof(VAS_158_OpenCountSheetsWidgetController));

        /// <summary>
        /// Gets the total count of drafted (open) physical inventory count sheets.
        /// </summary>
        [HttpGet]
        public JsonResult GetDraftCount()
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null)
            {
                return Json(new { error = "Unauthorized context." }, JsonRequestBehavior.AllowGet);
            }

            int count = 0;
            try
            {
                string sql = "SELECT COUNT(*) FROM M_Inventory i WHERE i.IsActive = 'Y' AND i.DocStatus = 'DR'";
                sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "i", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                count = Util.GetValueOfInt(DB.ExecuteScalar(sql, null, null));
            }
            catch (Exception ex)
            {
                _log.Severe("VAS_158_OpenCountSheetsWidgetController.GetDraftCount: " + ex.Message);
            }

            return Json(new { count = count }, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Gets the list of drafted physical inventory count sheets with header details.
        /// </summary>
        [HttpGet]
        public JsonResult GetDraftSheetsList()
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
                string sql = @"SELECT i.M_Inventory_ID, 
                                      i.DocumentNo, 
                                      w.Name AS WarehouseName, 
                                      (SELECT COUNT(DISTINCT il.M_Locator_ID) FROM M_InventoryLine il WHERE il.M_Inventory_ID = i.M_Inventory_ID) AS LocatorCount, 
                                      (SELECT MAX(loc.Value) FROM M_InventoryLine il JOIN M_Locator loc ON (il.M_Locator_ID = loc.M_Locator_ID) WHERE il.M_Inventory_ID = i.M_Inventory_ID) AS SingleLocator, 
                                      (SELECT COUNT(*) FROM M_InventoryLine il WHERE il.M_Inventory_ID = i.M_Inventory_ID) AS LineCount, 
                                      i.Created, 
                                      i.DocStatus 
                               FROM M_Inventory i 
                               LEFT JOIN M_Warehouse w ON (i.M_Warehouse_ID = w.M_Warehouse_ID) 
                               WHERE i.IsActive = 'Y' AND i.DocStatus = 'DR'";

                sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "i", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                sql += " ORDER BY i.Created DESC";

                dr = DB.ExecuteReader(sql, null, null);
                while (dr != null && dr.Read())
                {
                    int locatorCount = Util.GetValueOfInt(dr["LocatorCount"]);
                    string locatorStr = "N/A";
                    if (locatorCount == 1)
                    {
                        locatorStr = Util.GetValueOfString(dr["SingleLocator"]);
                    }
                    else if (locatorCount > 1)
                    {
                        locatorStr = "Multiple";
                    }

                    DateTime? createdDate = Util.GetValueOfDateTime(dr["Created"]);

                    list.Add(new
                    {
                        InventoryId = Util.GetValueOfInt(dr["M_Inventory_ID"]),
                        DocumentNo = Util.GetValueOfString(dr["DocumentNo"]),
                        Warehouse = Util.GetValueOfString(dr["WarehouseName"]),
                        Locator = locatorStr,
                        Lines = Util.GetValueOfInt(dr["LineCount"]),
                        Started = (createdDate.HasValue && createdDate.Value != DateTime.MinValue) ? createdDate.Value.ToString("dd MMM") : "",
                        Status = "Draft"
                    });
                }
            }
            catch (Exception ex)
            {
                _log.Severe("VAS_158_OpenCountSheetsWidgetController.GetDraftSheetsList: " + ex.Message);
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
    }
}
