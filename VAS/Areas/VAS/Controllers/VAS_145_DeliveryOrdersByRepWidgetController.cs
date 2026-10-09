using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Web.Mvc;
using VAdvantage.DataBase;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VAS.Controllers
{
    /// <summary>
    /// Module Name : VAS_145_DeliveryOrdersByRepWidget (Delivery Order dashboard)
    /// Purpose     : Data endpoints for the 4x2 "Delivery Orders by
    ///               Representative" widget - per-representative status-mix
    ///               counts of outbound customer delivery orders (M_InOut,
    ///               IsSOTrx='Y', MovementType='C-', returns excluded) for one
    ///               calendar month, and the representative's delivery orders
    ///               for the drill-down modal. The representative comes
    ///               STRICTLY from M_InOut.SalesRep_ID (never the sales order,
    ///               customer or creator) joined to AD_User for the name. The
    ///               delivery-order value is the actual line sales value
    ///               SUM(M_InOutLine.MovementQty * C_OrderLine.PriceActual) -
    ///               never C_Order.GrandTotal and never cost. Read-only.
    ///               MRole is applied to the primary fetched table (M_InOut) on
    ///               every read; all input is parameterized; the SQL uses only
    ///               CASE / COALESCE / COUNT / GROUP BY and half-open date-range
    ///               parameters computed server-side, so it runs unchanged on
    ///               Oracle and PostgreSQL (no TRUNC / DATE_TRUNC / ADD_MONTHS /
    ///               TO_CHAR / EXTRACT).
    /// Widget size : 4 columns x 2 rows.
    /// Widget number 145.
    /// Chronological development:
    ///   &lt;EmpCode&gt;   2026-07-18 Created
    /// </summary>
    public class VAS_145_DeliveryOrdersByRepWidgetController : Controller
    {
        /// <summary>
        /// Per-representative delivery-order counts of one calendar month,
        /// split by document status (in progress / draft / completed / voided),
        /// most orders first then representative name. One row per M_InOut
        /// header - the lines are intentionally NOT joined here.
        /// </summary>
        /// <param name="year">Calendar year of the selected month.</param>
        /// <param name="month">Calendar month (1-12).</param>
        /// <returns>JSON { items[] }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetRepresentativeSummary(int year = 0, int month = 0)
        {
            if (Session["ctx"] == null)
            {
                return Fail(Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired");
            }

            Ctx ctx = Session["ctx"] as Ctx;

            if (year < 1900 || year > 9999 || month < 1 || month > 12)
            {
                return Fail("Invalid month/year filter.");
            }

            DateTime dateFrom = new DateTime(year, month, 1);
            DateTime dateTo = dateFrom.AddMonths(1);

            // Status buckets are baked into the CASEs below and mirrored by the
            // widget's single frontend mapping constant: DR = Draft, CO/CL =
            // Completed, VO/RE = Voided, everything else = In Progress.
            string rawSql = @"
                SELECT io.SalesRep_ID AS Representative_ID,
                       rep.Name AS Representative_Name,
                       SUM(CASE
                               WHEN io.DocStatus = 'DR' THEN 0
                               WHEN io.DocStatus IN ('CO','CL') THEN 0
                               WHEN io.DocStatus IN ('VO','RE') THEN 0
                               ELSE 1
                           END) AS In_Progress_Count,
                       SUM(CASE WHEN io.DocStatus = 'DR' THEN 1 ELSE 0 END) AS Draft_Count,
                       SUM(CASE WHEN io.DocStatus IN ('CO','CL') THEN 1 ELSE 0 END) AS Completed_Count,
                       SUM(CASE WHEN io.DocStatus IN ('VO','RE') THEN 1 ELSE 0 END) AS Voided_Count,
                       COUNT(*) AS Total_Count
                FROM M_InOut io
                INNER JOIN AD_User rep ON (rep.AD_User_ID = io.SalesRep_ID)
                WHERE io.IsActive = 'Y'
                  AND io.IsSOTrx = 'Y'
                  AND io.MovementType = 'C-'
                  AND COALESCE(io.IsReturnTrx, 'N') = 'N'
                  AND io.SalesRep_ID IS NOT NULL
                  AND io.AD_Client_ID = @AD_Client_ID
                  AND io.MovementDate >= @Date_From
                  AND io.MovementDate < @Date_To";

            // AddAccessSQL appends its predicate to the END of the given string,
            // so GROUP BY / ORDER BY are appended only afterwards.
            rawSql = MRole.GetDefault(ctx).AddAccessSQL(
                rawSql,
                "io",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            rawSql += @"
                GROUP BY io.SalesRep_ID, rep.Name
                ORDER BY Total_Count DESC, Representative_Name ASC";

            List<object> items = new List<object>();
            IDataReader dr = null;

            try
            {
                dr = DB.ExecuteReader(rawSql, new SqlParameter[]
                {
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                    new SqlParameter("@Date_From", SqlDbType.DateTime) { Value = dateFrom },
                    new SqlParameter("@Date_To", SqlDbType.DateTime) { Value = dateTo }
                });

                while (dr != null && dr.Read())
                {
                    items.Add(new
                    {
                        representativeId = Util.GetValueOfInt(dr["Representative_ID"]),
                        representativeName = Util.GetValueOfString(dr["Representative_Name"]),
                        inProgressCount = Util.GetValueOfInt(dr["In_Progress_Count"]),
                        draftCount = Util.GetValueOfInt(dr["Draft_Count"]),
                        completedCount = Util.GetValueOfInt(dr["Completed_Count"]),
                        voidedCount = Util.GetValueOfInt(dr["Voided_Count"]),
                        totalCount = Util.GetValueOfInt(dr["Total_Count"])
                    });
                }

                return Ok(new { items = items });
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

        /// <summary>
        /// One representative's delivery orders of the selected month for the
        /// drill-down modal: DO number, customer, date, raw document status and
        /// the derived value (actual line qty x linked sales-order line price)
        /// with the document currency's ISO code. Rows whose lines carry no
        /// sales-order link contribute zero value (never cost). Newest first.
        /// </summary>
        /// <param name="year">Calendar year of the selected month.</param>
        /// <param name="month">Calendar month (1-12).</param>
        /// <param name="representativeId">M_InOut.SalesRep_ID of the clicked row.</param>
        /// <returns>JSON { representativeId, representativeName, items[] }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetRepresentativeOrders(int year = 0, int month = 0, int representativeId = 0)
        {
            if (Session["ctx"] == null)
            {
                return Fail(Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired");
            }

            Ctx ctx = Session["ctx"] as Ctx;

            if (year < 1900 || year > 9999 || month < 1 || month > 12)
            {
                return Fail("Invalid month/year filter.");
            }
            if (representativeId <= 0)
            {
                return Fail("Representative is required.");
            }

            DateTime dateFrom = new DateTime(year, month, 1);
            DateTime dateTo = dateFrom.AddMonths(1);

            // The value aggregate is a correlated scalar subquery injected AFTER
            // the access SQL is applied: on roles with record-access rules
            // AddAccessSQL appends a predicate for every table alias it finds -
            // including the subquery's M_InOutLine / C_OrderLine aliases - to
            // the OUTER where-clause, where those aliases are out of scope
            // (ORA-00904). Logically it is the spec's per-DO
            // SUM(MovementQty * PriceActual) over the DO's active lines.
            string rawSql = @"
                SELECT io.M_InOut_ID AS Delivery_Order_ID,
                       io.DocumentNo AS Delivery_Order_Number,
                       bp.Name AS Customer_Name,
                       io.MovementDate AS Document_Date,
                       io.DocStatus AS Document_Status,
                       COALESCE(__VAS_DO_LINE_VALUE__, 0) AS Delivery_Order_Value,
                       curr.ISO_Code AS Currency_ISO
                FROM M_InOut io
                INNER JOIN C_BPartner bp ON (bp.C_BPartner_ID = io.C_BPartner_ID)
                LEFT OUTER JOIN C_Order ord ON (ord.C_Order_ID = io.C_Order_ID)
                LEFT OUTER JOIN C_Currency curr ON (curr.C_Currency_ID = ord.C_Currency_ID)
                WHERE io.IsActive = 'Y'
                  AND io.IsSOTrx = 'Y'
                  AND io.MovementType = 'C-'
                  AND COALESCE(io.IsReturnTrx, 'N') = 'N'
                  AND io.AD_Client_ID = @AD_Client_ID
                  AND io.SalesRep_ID = @SalesRep_ID
                  AND io.MovementDate >= @Date_From
                  AND io.MovementDate < @Date_To";

            rawSql = MRole.GetDefault(ctx).AddAccessSQL(
                rawSql,
                "io",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            rawSql = rawSql.Replace("__VAS_DO_LINE_VALUE__", @"(
                           SELECT SUM(COALESCE(iol.MovementQty, 0) * COALESCE(ol.PriceActual, 0))
                           FROM M_InOutLine iol
                           LEFT OUTER JOIN C_OrderLine ol ON (ol.C_OrderLine_ID = iol.C_OrderLine_ID)
                           WHERE iol.M_InOut_ID = io.M_InOut_ID
                             AND iol.IsActive = 'Y'
                       )");

            rawSql += " ORDER BY io.MovementDate DESC, io.DocumentNo DESC";

            // The representative name for the modal title (same strict source:
            // the id IS M_InOut.SalesRep_ID, resolved through AD_User).
            string representativeName = Util.GetValueOfString(DB.ExecuteScalar(
                @"SELECT Name FROM AD_User WHERE AD_User_ID=@Rep_ID",
                new SqlParameter[] { new SqlParameter("@Rep_ID", representativeId) }, null));

            // Session/tenant default currency ISO - used only when a delivery
            // order has no linked sales order (and therefore no document
            // currency). One portable parameterized lookup, resolved once.
            string defaultCurrencyIso = Util.GetValueOfString(DB.ExecuteScalar(
                @"SELECT ISO_Code FROM C_Currency WHERE C_Currency_ID=@Currency_ID",
                new SqlParameter[] { new SqlParameter("@Currency_ID", ctx.GetContextAsInt("$C_Currency_ID")) }, null));

            List<object> items = new List<object>();
            IDataReader dr = null;

            try
            {
                dr = DB.ExecuteReader(rawSql, new SqlParameter[]
                {
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                    new SqlParameter("@SalesRep_ID", representativeId),
                    new SqlParameter("@Date_From", SqlDbType.DateTime) { Value = dateFrom },
                    new SqlParameter("@Date_To", SqlDbType.DateTime) { Value = dateTo }
                });

                while (dr != null && dr.Read())
                {
                    DateTime? documentDate = Util.GetValueOfDateTime(dr["Document_Date"]);
                    string currencyIso = Util.GetValueOfString(dr["Currency_ISO"]);

                    items.Add(new
                    {
                        deliveryOrderId = Util.GetValueOfInt(dr["Delivery_Order_ID"]),
                        deliveryOrderNumber = Util.GetValueOfString(dr["Delivery_Order_Number"]),
                        customerName = Util.GetValueOfString(dr["Customer_Name"]),
                        documentDate = documentDate.HasValue ? documentDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                        documentStatus = Util.GetValueOfString(dr["Document_Status"]),
                        value = Util.GetValueOfDecimal(dr["Delivery_Order_Value"]),
                        currencyIso = string.IsNullOrEmpty(currencyIso) ? defaultCurrencyIso : currencyIso
                    });
                }

                return Ok(new
                {
                    representativeId = representativeId,
                    representativeName = representativeName,
                    items = items
                });
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
