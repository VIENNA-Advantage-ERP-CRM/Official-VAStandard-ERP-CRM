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
    /// Module Name : VAS_188_TopUsedProductsWidget
    /// Purpose     : Ranks top 10 products consumed by volume (quantity or value) with detail usage breakdown modal.
    /// Chronological development:
    ///   AI-Dev      2026-08-02 Created
    /// </summary>
    public class VAS_188_TopUsedProductsWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_188_TopUsedProductsWidgetController).FullName);

        /// <summary>Endpoint A: Top 10 used products for selected month and year.</summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetTopProducts(int month, int year, string measure)
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null) { return Json("", JsonRequestBehavior.AllowGet); }

            var products = new List<object>();
            DateTime monthStart = new DateTime(year > 0 ? year : DateTime.Now.Year, month > 0 ? month : DateTime.Now.Month, 1);

            try
            {
                DateTime nextMonthStart = monthStart.AddMonths(1);
                string msl = ToSqlDate(monthStart);
                string nmsl = ToSqlDate(nextMonthStart);
                // Value is qty x the line's CurrentCostPrice, matching the cost basis the user set
                // for this dashboard. The old COALESCE(CurrentCostPrice, PriceCost, VA024_CostPrice)
                // fallback chain mixed three different price columns into one figure.
                // The trailing ", 0" is kept HERE (unlike VAS_184, which excludes unpriced lines):
                // this widget ranks by QUANTITY, so a genuinely top-issued product must not fall out
                // of the ranking just because some of its lines carry no cost.
                string orderBy = (measure == "val")
                    ? "SUM(line.QtyInternalUse * COALESCE(line.CurrentCostPrice, 0)) DESC"
                    : "SUM(line.QtyInternalUse) DESC";

                // Role access is applied to the inner header SELECT; applying it to this wrapped
                // aggregate would append the predicate outside the subquery (ORA-00907).
                string invAccessSql = BuildAccessibleInventorySql(ctx, msl, nmsl);

                string sql = @"
                    SELECT * FROM (
                      SELECT
                        p.M_Product_ID,
                        p.Name AS ProductName,
                        asi.Description AS Attribute,
                        pc.Name AS CategoryName,
                        uom.Name AS UomName,
                        SUM(line.QtyInternalUse) AS TotalQty,
                        SUM(line.QtyInternalUse * COALESCE(line.CurrentCostPrice, 0)) AS TotalValue
                      FROM M_InventoryLine line
                      INNER JOIN (" + invAccessSql + @") ai ON ai.M_Inventory_ID = line.M_Inventory_ID
                      INNER JOIN M_Product p ON p.M_Product_ID = line.M_Product_ID
                      LEFT JOIN M_Product_Category pc ON pc.M_Product_Category_ID = p.M_Product_Category_ID
                      LEFT JOIN C_UOM uom ON uom.C_UOM_ID = line.C_UOM_ID
                      LEFT JOIN M_AttributeSetInstance asi ON asi.M_AttributeSetInstance_ID = line.M_AttributeSetInstance_ID
                      WHERE line.IsActive = 'Y'
                        AND COALESCE(line.QtyInternalUse, 0) > 0
                      GROUP BY p.M_Product_ID, p.Name, asi.Description, pc.Name, uom.Name
                      ORDER BY " + orderBy + @"
                    ) WHERE ROWNUM <= 10";

                using (IDataReader dr = DB.ExecuteReader(sql, null, null))
                {
                    while (dr != null && dr.Read())
                    {
                        products.Add(new
                        {
                            productId = Util.GetValueOfInt(dr["M_Product_ID"]),
                            productName = Util.GetValueOfString(dr["ProductName"]),
                            attribute = Util.GetValueOfString(dr["Attribute"]),
                            categoryName = Util.GetValueOfString(dr["CategoryName"]),
                            uomName = Util.GetValueOfString(dr["UomName"]),
                            totalQty = Util.GetValueOfDecimal(dr["TotalQty"]),
                            totalValue = Util.GetValueOfDecimal(dr["TotalValue"])
                        });
                    }
                }

            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_188_TopUsedProductsWidget.GetTopProducts", ex);
                return Json(JsonConvert.SerializeObject(new { error = Msg.GetMsg(ctx, "Error") ?? "Error" }), JsonRequestBehavior.AllowGet);
            }
            return Json(JsonConvert.SerializeObject(new { products = products, success = true }), JsonRequestBehavior.AllowGet);
        }

        /// <summary>Endpoint B: Individual issue lines for a specific product in selected period for modal.</summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetProductUsageDetails(int productId, int month, int year)
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null) { return Json("", JsonRequestBehavior.AllowGet); }

            DateTime monthStart = new DateTime(year > 0 ? year : DateTime.Now.Year, month > 0 ? month : DateTime.Now.Month, 1);
            var lines = new List<object>();

            try
            {
                DateTime nextMonthStart = monthStart.AddMonths(1);
                string msl = ToSqlDate(monthStart);
                string nmsl = ToSqlDate(nextMonthStart);

                string invAccessSql = BuildAccessibleInventorySql(ctx, msl, nmsl);

                string sql = @"
                    SELECT
                      ai.DocumentNo,
                      ai.MovementDate,
                      wh.Name AS WarehouseName,
                      loc.Value AS LocatorCode,
                      line.QtyInternalUse,
                      (line.QtyInternalUse * COALESCE(line.CurrentCostPrice, 0)) AS LineValue
                    FROM M_InventoryLine line
                    INNER JOIN (" + invAccessSql + @") ai ON ai.M_Inventory_ID = line.M_Inventory_ID
                    LEFT JOIN M_Locator loc ON loc.M_Locator_ID = line.M_Locator_ID
                    LEFT JOIN M_Warehouse wh ON wh.M_Warehouse_ID = loc.M_Warehouse_ID
                    WHERE line.IsActive = 'Y'
                      AND COALESCE(line.QtyInternalUse, 0) > 0
                      AND line.M_Product_ID = " + productId + @"
                    ORDER BY ai.MovementDate DESC, ai.DocumentNo DESC";

                using (IDataReader dr = DB.ExecuteReader(sql, null, null))
                {
                    while (dr != null && dr.Read())
                    {
                        lines.Add(new
                        {
                            documentNo = Util.GetValueOfString(dr["DocumentNo"]),
                            movementDate = Convert.ToDateTime(dr["MovementDate"]).ToString("dd MMM yyyy"),
                            whLoc = Util.GetValueOfString(dr["WarehouseName"]) + " / " + Util.GetValueOfString(dr["LocatorCode"]),
                            qty = Util.GetValueOfDecimal(dr["QtyInternalUse"]),
                            value = Util.GetValueOfDecimal(dr["LineValue"])
                        });
                    }
                }

            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_188_TopUsedProductsWidget.GetProductUsageDetails", ex);
                return Json(JsonConvert.SerializeObject(new { error = Msg.GetMsg(ctx, "Error") ?? "Error" }), JsonRequestBehavior.AllowGet);
            }
            return Json(JsonConvert.SerializeObject(new { lines = lines, success = true }), JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Role-filtered internal-use inventory headers for the period. Kept as a plain SELECT with
        /// no GROUP BY / ORDER BY: AddAccessSQL appends its predicate to the end of the statement,
        /// so it is only safe on a query whose alias is still in scope there.
        /// </summary>
        private static string BuildAccessibleInventorySql(Ctx ctx, string periodStart, string periodEnd)
        {
            // Source design (top-used-products-widget.md section 3): "Exclude documents with
            // DocStatus = Drafted (not yet actual consumption). Posted, Completed, and In Process
            // count." The previous IN ('CO','CL') silently dropped every In Process document, so
            // material already issued against an IP document never appeared in the ranking.
            // Voided ('VO') and Reversed ('RE') stay excluded - they are not consumption either,
            // which is why this is an explicit include-list rather than DocStatus <> 'DR'.
            string sql = @"
                    SELECT inv.M_Inventory_ID, inv.DocumentNo, inv.MovementDate
                    FROM M_Inventory inv
                    WHERE inv.IsActive = 'Y'
                      AND inv.DocStatus IN ('CO', 'CL', 'IP')
                      AND COALESCE(inv.IsInternalUse, 'N') = 'Y'
                      AND inv.MovementDate >= " + periodStart + @"
                      AND inv.MovementDate < " + periodEnd;

            return MRole.GetDefault(ctx).AddAccessSQL(sql, "inv", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
        }

        private static string ToSqlDate(DateTime date)
        {
            if (DB.IsOracle())
            {
                return "TO_DATE('" + date.ToString("yyyy-MM-dd") + "', 'YYYY-MM-DD')";
            }
            return "CAST('" + date.ToString("yyyy-MM-dd") + "' AS DATE)";
        }
    }
}
