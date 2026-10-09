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
    /// Module Name : VAS_153_ShippedTodayWidget (Delivery Order dashboard)
    /// Purpose     : Data endpoint for the 2x1 "Shipped Today" KPI tile. The
    ///               main value counts distinct outbound customer Delivery
    ///               Order headers shipped today - active, sales, non-return
    ///               customer shipments (MovementType 'C-') in Completed (CO)
    ///               or Closed (CL) status whose MovementDate falls within
    ///               today (half-open range CURRENT_DATE .. CURRENT_DATE+1 so
    ///               any timestamp today is included). The meta value counts
    ///               distinct linked Sales Order lines that are still partial
    ///               - i.e. lines delivered on those DOs (MovementQty > 0)
    ///               whose current cumulative QtyDelivered is below QtyOrdered;
    ///               each C_OrderLine_ID is counted once via COUNT(DISTINCT).
    ///               MRole is applied to the primary fetched table (M_InOut) on
    ///               the read; the SQL uses only ANSI constructs (COALESCE,
    ///               CURRENT_DATE, date + 1 - no TRUNC/DATE_TRUNC/SYSDATE/NOW/
    ///               ::date), so it runs unchanged on Oracle and PostgreSQL.
    /// Widget number 153.
    /// Chronological development:
    ///   &lt;EmpCode&gt;   2026-07-22 Created
    /// </summary>
    public class VAS_153_ShippedTodayWidgetController : Controller
    {
        /// <summary>
        /// Count of customer Delivery Orders shipped today plus the number of
        /// partial Sales Order lines included in them.
        /// </summary>
        /// <returns>JSON { shippedTodayCount, partialLineCount }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetSummary()
        {
            if (Session["ctx"] == null)
            {
                return Fail(Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired");
            }

            Ctx ctx = Session["ctx"] as Ctx;

            string sql = @"
                SELECT
                    COUNT(DISTINCT io.M_InOut_ID) AS Shipped_Today_Count,
                    COUNT(
                        DISTINCT CASE
                            WHEN COALESCE(iol.MovementQty, 0) > 0
                             AND COALESCE(ol.QtyDelivered, 0) < COALESCE(ol.QtyOrdered, 0)
                            THEN ol.C_OrderLine_ID
                        END
                    ) AS Partial_Line_Count
                FROM M_InOut io
                LEFT JOIN M_InOutLine iol
                       ON iol.M_InOut_ID = io.M_InOut_ID
                      AND iol.IsActive = 'Y'
                LEFT JOIN C_OrderLine ol
                       ON ol.C_OrderLine_ID = iol.C_OrderLine_ID
                      AND ol.IsActive = 'Y'
                WHERE io.IsActive = 'Y'
                  AND io.IsSOTrx = 'Y'
                  AND io.IsReturnTrx = 'N'
                  AND io.MovementType = 'C-'
                  AND io.DocStatus IN ('CO', 'CL')
                  AND io.MovementDate >= CURRENT_DATE
                  AND io.MovementDate < CURRENT_DATE + 1";

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "io", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            int shipped = 0;
            int partial = 0;
            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, new SqlParameter[0]);
                if (dr != null && dr.Read())
                {
                    shipped = Util.GetValueOfInt(dr["Shipped_Today_Count"]);
                    partial = Util.GetValueOfInt(dr["Partial_Line_Count"]);
                }
                return Ok(new { shippedTodayCount = shipped, partialLineCount = partial });
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
            }
            finally
            {
                if (dr != null) { dr.Close(); dr.Dispose(); }
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
