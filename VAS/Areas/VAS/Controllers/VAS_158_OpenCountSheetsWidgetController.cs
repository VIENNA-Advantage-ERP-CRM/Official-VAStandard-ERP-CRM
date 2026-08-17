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
        /// Statuses that make a count sheet "open": Drafted, In Progress, Waiting Confirmation.
        /// Previously only 'DR' was counted, so a sheet that had been started or sent for
        /// confirmation silently dropped off the widget while still being open work.
        /// (The source prompt's approved rule said 'DR' alone; the user widened it on 2026-08-16.)
        /// </summary>
        private const string OpenStatusFilter = "i.DocStatus IN ('DR', 'IP', 'WC')";

        /// <summary>
        /// Inventory COUNT documents only. M_Inventory.IsInternalUse is the discriminator:
        /// 'N' = physical inventory count, 'Y' = internal use / material issue. The filter was
        /// missing entirely, so every open internal-use document was being counted as a count sheet.
        /// </summary>
        private const string CountSheetFilter = "COALESCE(i.IsInternalUse, 'N') = 'N'";

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
                string sql = "SELECT COUNT(*) FROM M_Inventory i WHERE i.IsActive = 'Y' AND "
                    + CountSheetFilter + " AND " + OpenStatusFilter;
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
                // The line/locator sub-selects now filter il.IsActive = 'Y', per the source prompt's
                // approved rule "Count only active M_InventoryLine rows for the Lines value and
                // locator derivation". Inactive lines were previously inflating the Lines column
                // and could turn a single-locator sheet into "Multiple".
                string sql = @"SELECT i.M_Inventory_ID,
                                      i.DocumentNo,
                                      w.Name AS WarehouseName,
                                      (SELECT COUNT(DISTINCT il.M_Locator_ID) FROM M_InventoryLine il WHERE il.M_Inventory_ID = i.M_Inventory_ID AND il.IsActive = 'Y') AS LocatorCount,
                                      (SELECT MAX(loc.Value) FROM M_InventoryLine il JOIN M_Locator loc ON (il.M_Locator_ID = loc.M_Locator_ID) WHERE il.M_Inventory_ID = i.M_Inventory_ID AND il.IsActive = 'Y') AS SingleLocator,
                                      (SELECT COUNT(*) FROM M_InventoryLine il WHERE il.M_Inventory_ID = i.M_Inventory_ID AND il.IsActive = 'Y') AS LineCount,
                                      i.Created,
                                      i.DocStatus
                               FROM M_Inventory i
                               LEFT JOIN M_Warehouse w ON (i.M_Warehouse_ID = w.M_Warehouse_ID)
                               WHERE i.IsActive = 'Y' AND " + CountSheetFilter + " AND " + OpenStatusFilter;

                sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "i", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                sql += " ORDER BY i.Created DESC";

                dr = DB.ExecuteReader(sql, null, null);
                while (dr != null && dr.Read())
                {
                    int locatorCount = Util.GetValueOfInt(dr["LocatorCount"]);
                    // Source prompt locator display rule: none -> em dash, exactly one -> its value,
                    // more than one -> "Multiple". "N/A" was not one of the approved displays.
                    string locatorStr = "—";
                    if (locatorCount == 1)
                    {
                        locatorStr = Util.GetValueOfString(dr["SingleLocator"]);
                    }
                    else if (locatorCount > 1)
                    {
                        locatorStr = Msg.GetMsg(ctx, "VAS_Multiple") ?? "Multiple";
                    }

                    DateTime? createdDate = Util.GetValueOfDateTime(dr["Created"]);
                    string docStatus = Util.GetValueOfString(dr["DocStatus"]);

                    list.Add(new
                    {
                        InventoryId = Util.GetValueOfInt(dr["M_Inventory_ID"]),
                        DocumentNo = Util.GetValueOfString(dr["DocumentNo"]),
                        Warehouse = Util.GetValueOfString(dr["WarehouseName"]),
                        Locator = locatorStr,
                        Lines = Util.GetValueOfInt(dr["LineCount"]),
                        Started = (createdDate.HasValue && createdDate.Value != DateTime.MinValue) ? createdDate.Value.ToString("dd MMM") : "",
                        // Was the hardcoded literal "Draft" for every row - wrong now that three
                        // statuses are returned, and a static literal besides.
                        DocStatus = docStatus,
                        Status = GetStatusLabel(ctx, docStatus)
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

        /// <summary>
        /// Translated display label for an open count-sheet status, taken from the standard
        /// document-status message keys so the modal is localized rather than carrying literals.
        /// </summary>
        private static string GetStatusLabel(Ctx ctx, string docStatus)
        {
            switch (docStatus)
            {
                case "DR": return Msg.GetMsg(ctx, "Drafted") ?? "Drafted";
                case "IP": return Msg.GetMsg(ctx, "InProgress") ?? "In Progress";
                case "WC": return Msg.GetMsg(ctx, "WaitingConfirmation") ?? "Waiting Confirmation";
                default: return docStatus;
            }
        }
    }
}
