using Newtonsoft.Json;
using System;
using System.Data;
using System.Data.SqlClient;
using System.Web.Mvc;
using VAdvantage.DataBase;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VAS.Controllers
{
    /// <summary>
    /// Module Name : VAS_142_BackordersWidget (Delivery Order dashboard)
    /// Purpose     : Data endpoint for the 2x1 "Backorders" KPI tile - count of
    ///               active Sales Order lines (completed, non-return sales
    ///               transactions with a warehouse) whose remaining quantity
    ///               (QtyOrdered - QtyDelivered) is greater than zero AND exceeds
    ///               the matching on-hand stock (M_Storage.QtyOnHand summed
    ///               across all active locators of the order's warehouse,
    ///               narrowed to the exact same Product + Attribute Set Instance,
    ///               COALESCE(...,0) on both sides so null/zero attribute set
    ///               instances compare consistently). Counts each C_OrderLine_ID
    ///               once - never Delivery Order headers/lines. No reserved/
    ///               allocated quantity is subtracted, no reorder-point/ATP
    ///               formula is used, per the confirmed business definition.
    ///               MRole is applied to the primary fetched table (C_Order) on
    ///               the read; all input is parameterized; the SQL uses only
    ///               COALESCE (no NVL, DECODE, TRUNC, database-specific
    ///               functions), so it runs unchanged on Oracle and PostgreSQL.
    /// Widget number 142.
    /// Chronological development:
    ///   &lt;EmpCode&gt;   2026-07-21 Created
    /// </summary>
    public class VAS_142_BackordersWidgetController : Controller
    {
        /// <summary>
        /// Count of Sales Order lines short of matching on-hand stock.
        /// </summary>
        /// <returns>JSON { backorderLineCount }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetSummary()
        {
            if (Session["ctx"] == null)
            {
                return Fail(Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired");
            }

            Ctx ctx = Session["ctx"] as Ctx;

            string orderSql = @"
                SELECT
                    COUNT(*) AS Backorder_Line_Count
                FROM C_Order o
                JOIN C_OrderLine ol
                  ON ol.C_Order_ID = o.C_Order_ID
                 AND ol.IsActive = 'Y'
                JOIN M_Product p
                  ON p.M_Product_ID = ol.M_Product_ID
                 AND p.IsActive = 'Y'
                 AND p.IsStocked = 'Y'
                LEFT JOIN (
                    SELECT
                        l.M_Warehouse_ID,
                        s.M_Product_ID,
                        COALESCE(s.M_AttributeSetInstance_ID, 0) AS M_AttributeSetInstance_ID,
                        SUM(COALESCE(s.QtyOnHand, 0)) AS Qty_On_Hand
                    FROM M_Locator l
                    JOIN M_Storage s
                      ON s.M_Locator_ID = l.M_Locator_ID
                     AND s.IsActive = 'Y'
                    WHERE l.IsActive = 'Y'
                    GROUP BY l.M_Warehouse_ID, s.M_Product_ID, COALESCE(s.M_AttributeSetInstance_ID, 0)
                ) sw
                  ON sw.M_Warehouse_ID = o.M_Warehouse_ID
                 AND sw.M_Product_ID = ol.M_Product_ID
                 AND sw.M_AttributeSetInstance_ID = COALESCE(ol.M_AttributeSetInstance_ID, 0)
                WHERE o.IsActive = 'Y'
                  AND o.IsSOTrx = 'Y'
                  AND o.IsReturnTrx = 'N'
                  AND o.DocStatus = 'CO'
                  AND o.M_Warehouse_ID IS NOT NULL
                  AND COALESCE(ol.QtyOrdered, 0) - COALESCE(ol.QtyDelivered, 0) > 0
                  AND COALESCE(ol.QtyOrdered, 0) - COALESCE(ol.QtyDelivered, 0) > COALESCE(sw.Qty_On_Hand, 0)";

            orderSql = MRole.GetDefault(ctx).AddAccessSQL(orderSql, "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            try
            {
                int count = Util.GetValueOfInt(DB.ExecuteScalar(orderSql, new SqlParameter[0], null));
                return Ok(new { backorderLineCount = count });
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
            }
        }

        /// <summary>Wraps a success payload as a serialized JSON result.</summary>
        private JsonResult Ok(object result)
        {
            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }

        /// <summary>Wraps a failure message as a serialized JSON result.</summary>
        private JsonResult Fail(string message)
        {
            return Json(JsonConvert.SerializeObject(new
            {
                success = false,
                error = message,
                message = message
            }), JsonRequestBehavior.AllowGet);
        }
    }
}
