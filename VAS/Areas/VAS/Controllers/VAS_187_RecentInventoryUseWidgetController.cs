using System.Data.SqlClient;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Web.Mvc;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VIS.Controllers
{
    /// <summary>
    /// Module Name : VAS_187_RecentInventoryUseWidget
    /// Purpose     : Real-time activity log of recent material issue transactions with status filtering and pagination.
    /// Chronological development:
    ///   AI-Dev      2026-08-02 Created
    /// </summary>
    public class VAS_187_RecentInventoryUseWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_187_RecentInventoryUseWidgetController).FullName);

        /// <summary>
        /// The product's CURRENT cost price, as a derived table (M_Product_ID, CurrentCostPrice).
        /// Picks the M_Cost row whose cost element matches the accounting schema's own costing
        /// method, so landed-cost and other cost COMPONENT rows are excluded. A plain
        /// MAX(M_Cost.CurrentCostPrice) is NOT the product cost - on FSMTesting6 it reports
        /// 'Air Filter (7 micron)' at 80,142.29 (a Landed Cost component) against a true standard
        /// cost of 2,599.
        /// </summary>
        private const string ProductCurrentCostSql = @"
                    SELECT c.M_Product_ID, MAX(c.CurrentCostPrice) AS CurrentCostPrice
                    FROM M_Cost c
                    INNER JOIN M_CostElement ce ON ce.M_CostElement_ID = c.M_CostElement_ID
                    INNER JOIN C_AcctSchema acs ON acs.C_AcctSchema_ID = c.C_AcctSchema_ID
                                               AND acs.M_CostType_ID   = c.M_CostType_ID
                    WHERE c.IsActive = 'Y'
                      AND ce.CostingMethod IS NOT NULL
                      AND ce.CostingMethod = acs.CostingMethod
                    GROUP BY c.M_Product_ID";

        /// <summary>
        /// Unit cost of an issue line: the line's own cost when it has one, otherwise the product's
        /// current cost, otherwise 0. Expects the line aliased as "line" and the product-cost
        /// derived table aliased as "pc".
        /// The NULLIF guards are essential: M_InventoryLine.CurrentCostPrice is a literal 0 (not
        /// NULL) on many issue lines, so a plain COALESCE returns 0 and never reaches the fallback.
        /// </summary>
        private const string LineUnitCostSql =
            "COALESCE(NULLIF(line.CurrentCostPrice, 0), NULLIF(line.PriceCost, 0), NULLIF(line.VA024_CostPrice, 0), pc.CurrentCostPrice, 0)";

        /// <summary>Returns paginated list of recent material issue transactions.</summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetRecentIssues(string status, int pageNo, int pageSize)
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null) { return Json("", JsonRequestBehavior.AllowGet); }

            int pNo = pageNo > 0 ? pageNo : 1;
            int pSize = pageSize > 0 ? pageSize : 4;
            int offset = (pNo - 1) * pSize;
            var list = new List<object>();
            int totalRecords = 0;

            try
            {
                string countSql = @"
                    SELECT COUNT(DISTINCT inv.M_Inventory_ID)
                    FROM M_Inventory inv
                    WHERE inv.IsActive = 'Y'
                      AND COALESCE(inv.IsInternalUse, 'N') = 'Y'";

                if (!string.IsNullOrEmpty(status) && status != "ALL")
                {
                    countSql += " AND inv.DocStatus = " + DB.TO_STRING(status);
                }

                countSql = MRole.GetDefault(ctx).AddAccessSQL(countSql, "inv", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                totalRecords = Util.GetValueOfInt(DB.ExecuteScalar(countSql, null, null));

                // Role access must be applied to a plain SELECT whose alias is in scope at the end
                // of the statement; applying it to the wrapped/aggregated query yields ORA-00907.
                string invAccessSql = @"
                    SELECT inv.M_Inventory_ID, inv.DocumentNo, inv.MovementDate, inv.DocStatus,
                           inv.AD_Org_ID, inv.M_Warehouse_ID
                    FROM M_Inventory inv
                    WHERE inv.IsActive = 'Y'
                      AND COALESCE(inv.IsInternalUse, 'N') = 'Y'";

                if (!string.IsNullOrEmpty(status) && status != "ALL")
                {
                    invAccessSql += " AND inv.DocStatus = " + DB.TO_STRING(status);
                }

                invAccessSql = MRole.GetDefault(ctx).AddAccessSQL(invAccessSql, "inv", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                string sql = @"
                    SELECT * FROM (
                      SELECT
                        ai.M_Inventory_ID,
                        ai.DocumentNo,
                        ai.MovementDate,
                        ai.DocStatus,
                        org.Name AS OrgName,
                        wh.Name AS WarehouseName,
                        COUNT(line.M_InventoryLine_ID) AS LineCount,
                        SUM(line.QtyInternalUse) AS TotalQty,
                        SUM(line.QtyInternalUse * " + LineUnitCostSql + @") AS TotalValue,
                        ROW_NUMBER() OVER (ORDER BY ai.MovementDate DESC, ai.M_Inventory_ID DESC) AS RowSeq
                      FROM (" + invAccessSql + @") ai
                      INNER JOIN AD_Org org ON org.AD_Org_ID = ai.AD_Org_ID
                      LEFT JOIN M_Warehouse wh ON wh.M_Warehouse_ID = ai.M_Warehouse_ID
                      -- IsActive belongs in the JOIN condition, not a WHERE clause: moving it to
                      -- WHERE would turn this LEFT JOIN into an inner join and drop line-less
                      -- documents, which this widget must still list (a Drafted issue with no
                      -- lines yet is exactly the sort of row it exists to surface).
                      LEFT JOIN M_InventoryLine line ON line.M_Inventory_ID = ai.M_Inventory_ID
                                                    AND line.IsActive = 'Y'
                      LEFT JOIN (" + ProductCurrentCostSql + @") pc ON pc.M_Product_ID = line.M_Product_ID
                      GROUP BY ai.M_Inventory_ID, ai.DocumentNo, ai.MovementDate, ai.DocStatus, org.Name, wh.Name
                    ) WHERE RowSeq > " + offset + " AND RowSeq <= " + (offset + pSize);

                using (IDataReader dr = DB.ExecuteReader(sql, null, null))
                {
                    while (dr != null && dr.Read())
                    {
                        list.Add(new
                        {
                            inventoryId = Util.GetValueOfInt(dr["M_Inventory_ID"]),
                            documentNo = Util.GetValueOfString(dr["DocumentNo"]),
                            movementDate = Convert.ToDateTime(dr["MovementDate"]).ToString("dd MMM yyyy"),
                            docStatus = Util.GetValueOfString(dr["DocStatus"]),
                            orgName = Util.GetValueOfString(dr["OrgName"]),
                            warehouseName = Util.GetValueOfString(dr["WarehouseName"]),
                            lineCount = Util.GetValueOfInt(dr["LineCount"]),
                            totalQty = Util.GetValueOfDecimal(dr["TotalQty"]),
                            totalValue = Util.GetValueOfDecimal(dr["TotalValue"])
                        });
                    }
                }

            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_187_RecentInventoryUseWidget.GetRecentIssues", ex);
                return Json(JsonConvert.SerializeObject(new { error = Msg.GetMsg(ctx, "Error") ?? "Error" }), JsonRequestBehavior.AllowGet);
            }

            return Json(JsonConvert.SerializeObject(new
            {
                records = list,
                totalRecords = totalRecords,
                pageNo = pNo,
                pageSize = pSize,
                success = true
            }), JsonRequestBehavior.AllowGet);
        }
    }
}
