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
    /// Module Name : VAS_132_OutOfStockWidget (Product Master dashboard)
    /// Purpose     : Data endpoint for the 2x1 "Out of Stock" KPI tile -
    ///               counts active, stocked, non-discontinued products whose
    ///               total on-hand quantity (SUM of M_Storage.QtyOnHand across
    ///               all active storage rows, no reservation/allocation
    ///               subtracted) is <= 0, plus how many of those also appear
    ///               on an open sales order line (IsSOTrx='Y') and how many
    ///               appear on an open purchase order line (IsSOTrx='N').
    ///               "Open" line = parent order active, non-return,
    ///               DocStatus='CO', and the line itself active with
    ///               QtyOrdered &gt; QtyDelivered. All three numbers come from
    ///               one query pass so they stay consistent with each other.
    ///               MRole is applied to each CTE's primary physical table
    ///               (M_Product for the out-of-stock set, C_Order for the
    ///               open-SO/open-PO sets); all input is parameterized; the
    ///               SQL uses only COALESCE / CASE / HAVING (no NVL, DECODE,
    ///               LIMIT/OFFSET/FETCH, DB date formatting or DB-specific
    ///               upsert), so it runs unchanged on Oracle and PostgreSQL.
    ///               Display-only widget - no modal, no drill-down, read-only.
    /// Widget size : 2 columns x 1 row.
    /// Widget number 132.
    /// Chronological development:
    ///   &lt;EmpCode&gt;   2026-07-21 Created
    /// </summary>
    public class VAS_132_OutOfStockWidgetController : Controller
    {
        /// <summary>
        /// Returns the out-of-stock product count and how many of those
        /// products appear on an open sales order line / open purchase
        /// order line.
        /// </summary>
        /// <returns>JSON { outOfStock, inOpenSO, inOpenPO }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetSummary()
        {
            if (Session["ctx"] == null)
            {
                return Fail(Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired");
            }

            Ctx ctx = Session["ctx"] as Ctx;

            try
            {
                // Out-of-stock set: active, stocked, non-discontinued products
                // whose summed active-storage QtyOnHand is <= 0 (a product
                // with no storage row at all counts as 0).
                string oosInner = @"
                    SELECT p.M_Product_ID AS ProductId
                    FROM M_Product p
                    LEFT JOIN M_Storage s ON (s.M_Product_ID = p.M_Product_ID AND s.AD_Client_ID = p.AD_Client_ID AND s.IsActive = 'Y')
                    WHERE p.AD_Client_ID = @AD_Client_ID
                      AND p.IsActive = 'Y'
                      AND p.IsStocked = 'Y'
                      AND COALESCE(p.Discontinued, 'N') = 'N'";

                // AddAccessSQL appends its predicate to the END, so GROUP BY /
                // HAVING are appended after the call (widget rule #1).
                oosInner = MRole.GetDefault(ctx).AddAccessSQL(oosInner, "p", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                oosInner += @"
                    GROUP BY p.M_Product_ID
                    HAVING COALESCE(SUM(COALESCE(s.QtyOnHand, 0)), 0) <= 0";

                string soInner = @"
                    SELECT DISTINCT ol.M_Product_ID AS ProductId
                    FROM C_Order o
                    JOIN C_OrderLine ol ON (ol.C_Order_ID = o.C_Order_ID AND ol.AD_Client_ID = o.AD_Client_ID AND ol.IsActive = 'Y')
                    WHERE o.AD_Client_ID = @AD_Client_ID
                      AND o.IsActive = 'Y'
                      AND o.IsSOTrx = 'Y'
                      AND COALESCE(o.IsReturnTrx, 'N') = 'N'
                      AND o.DocStatus = 'CO'
                      AND ol.M_Product_ID IS NOT NULL
                      AND COALESCE(ol.QtyOrdered, 0) > COALESCE(ol.QtyDelivered, 0)";
                soInner = MRole.GetDefault(ctx).AddAccessSQL(soInner, "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                string poInner = @"
                    SELECT DISTINCT ol.M_Product_ID AS ProductId
                    FROM C_Order o
                    JOIN C_OrderLine ol ON (ol.C_Order_ID = o.C_Order_ID AND ol.AD_Client_ID = o.AD_Client_ID AND ol.IsActive = 'Y')
                    WHERE o.AD_Client_ID = @AD_Client_ID
                      AND o.IsActive = 'Y'
                      AND o.IsSOTrx = 'N'
                      AND COALESCE(o.IsReturnTrx, 'N') = 'N'
                      AND o.DocStatus = 'CO'
                      AND ol.M_Product_ID IS NOT NULL
                      AND COALESCE(ol.QtyOrdered, 0) > COALESCE(ol.QtyDelivered, 0)";
                poInner = MRole.GetDefault(ctx).AddAccessSQL(poInner, "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                string sql = @"
                    WITH out_of_stock_products AS (
                        " + oosInner + @"
                    ),
                    open_sales_products AS (
                        " + soInner + @"
                    ),
                    open_purchase_products AS (
                        " + poInner + @"
                    )
                    SELECT
                        COUNT(*) AS OutOfStockCount,
                        COALESCE(SUM(CASE WHEN so.ProductId IS NOT NULL THEN 1 ELSE 0 END), 0) AS InOpenSoCount,
                        COALESCE(SUM(CASE WHEN po.ProductId IS NOT NULL THEN 1 ELSE 0 END), 0) AS InOpenPoCount
                    FROM out_of_stock_products oos
                    LEFT JOIN open_sales_products so ON (so.ProductId = oos.ProductId)
                    LEFT JOIN open_purchase_products po ON (po.ProductId = oos.ProductId)";

                int outOfStock = 0, inOpenSo = 0, inOpenPo = 0;

                IDataReader dr = null;
                try
                {
                    dr = DB.ExecuteReader(sql, new SqlParameter[]
                    {
                        new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
                    });

                    if (dr != null && dr.Read())
                    {
                        outOfStock = Util.GetValueOfInt(dr["OutOfStockCount"]);
                        inOpenSo = Util.GetValueOfInt(dr["InOpenSoCount"]);
                        inOpenPo = Util.GetValueOfInt(dr["InOpenPoCount"]);
                    }
                }
                finally
                {
                    if (dr != null) { dr.Close(); dr.Dispose(); }
                }

                return Ok(new { outOfStock = outOfStock, inOpenSO = inOpenSo, inOpenPO = inOpenPo });
            }
            catch (Exception)
            {
                // Never leak SQL text or stack traces to the browser.
                return Fail(Msg.GetMsg(ctx, "Error") ?? "Error");
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
