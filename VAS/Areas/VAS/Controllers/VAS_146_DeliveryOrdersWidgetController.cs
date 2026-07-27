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
    /// Module Name : VAS_146_DeliveryOrdersWidget (Delivery Order dashboard)
    /// Purpose     : Data endpoints for the 6x3 "Delivery Orders" widget - a
    ///               read-only monitor of customer delivery orders (M_InOut,
    ///               IsSOTrx='Y', MovementType='C-', reversed/voided excluded)
    ///               filtered to one calendar month/year, with a line-item
    ///               drill-down modal. Status mapping per the widget spec:
    ///               Invoiced (header C_Invoice_ID or any invoiced active line),
    ///               else Completed (CO/CL), else Drafted.
    ///               MRole is applied to the primary fetched table (M_InOut) on
    ///               every read; all input is parameterized; the SQL uses only
    ///               COALESCE / CASE / EXISTS / date-range parameters so it runs
    ///               unchanged on Oracle and PostgreSQL (no TO_CHAR / TRUNC /
    ///               EXTRACT / DATE_TRUNC).
    /// Widget number 146.
    /// Chronological development:
    ///   &lt;EmpCode&gt;   2026-07-18 Created
    /// </summary>
    public class VAS_146_DeliveryOrdersWidgetController : Controller
    {
        /// <summary>
        /// Distinct delivery-order dates (customer DOs only), newest first. The
        /// widget builds its Month and Year dropdown options from these dates in
        /// JavaScript and defaults both to the newest date returned.
        /// </summary>
        /// <returns>JSON { rows: ["yyyy-MM-dd", ...] }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetDeliveryOrderDates()
        {
            if (Session["ctx"] == null)
            {
                return Fail(Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired");
            }

            Ctx ctx = Session["ctx"] as Ctx;

            // DISTINCT keeps the payload small (one row per movement date);
            // month/year de-duplication happens client-side per the spec, so no
            // DB-specific date functions are needed.
            string sql = @"
                SELECT DISTINCT DeliveryOrder.MovementDate AS Do_Date
                FROM M_InOut DeliveryOrder
                WHERE DeliveryOrder.IsActive='Y'
                  AND DeliveryOrder.IsSOTrx='Y'
                  AND DeliveryOrder.MovementType='C-'
                  AND DeliveryOrder.MovementDate IS NOT NULL
                  AND DeliveryOrder.DocStatus NOT IN ('RE','VO')
                  AND DeliveryOrder.AD_Client_ID=@AD_Client_ID";

            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql,
                "DeliveryOrder",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            // Appended AFTER the access SQL (AddAccessSQL appends its predicate
            // to the end of the string it is given).
            sql += " ORDER BY Do_Date DESC";

            List<string> rows = new List<string>();
            IDataReader dr = null;

            try
            {
                dr = DB.ExecuteReader(sql, new SqlParameter[]
                {
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
                });

                while (dr != null && dr.Read())
                {
                    DateTime? doDate = Util.GetValueOfDateTime(dr["Do_Date"]);
                    if (doDate.HasValue)
                    {
                        rows.Add(doDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                    }
                }

                return Ok(new { rows = rows });
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
        /// Customer delivery orders of one calendar month/year, newest first,
        /// with customer name, package count, gross/tare weight and the mapped
        /// status code ('IV' Invoiced, 'CO' Completed, 'DR' Drafted). The
        /// widget pages the rows client-side (6 per page).
        /// </summary>
        /// <param name="year">Calendar year of the selected month.</param>
        /// <param name="month">Calendar month (1-12).</param>
        /// <returns>JSON { rows[] }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetDeliveryOrders(int year = 0, int month = 0)
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

            // The month window is computed here (first day of the selected month
            // to the first day of the next month) and bound as plain DateTime
            // parameters - no DB-specific date functions.
            DateTime monthStart = new DateTime(year, month, 1);
            DateTime nextMonthStart = monthStart.AddMonths(1);

            string sql = @"
                SELECT DeliveryOrder.M_InOut_ID AS Delivery_Order_ID,
                       DeliveryOrder.DocumentNo AS Do_Number,
                       DeliveryOrder.MovementDate AS Do_Date,
                       Customer.Name AS Customer_Name,
                       COALESCE(DeliveryOrder.NoPackages, 0) AS Package_Count,
                       COALESCE(DeliveryOrder.VAS_GrossWeight, 0) AS Gross_Weight,
                       COALESCE(DeliveryOrder.VAS_TareWeight, 0) AS Tare_Weight,
                       CASE
                           WHEN DeliveryOrder.C_Invoice_ID IS NOT NULL THEN 'IV'
                           WHEN __VAS_HAS_INVOICED_LINE__ THEN 'IV'
                           WHEN DeliveryOrder.DocStatus IN ('CO','CL') THEN 'CO'
                           ELSE 'DR'
                       END AS Status_Code
                FROM M_InOut DeliveryOrder
                INNER JOIN C_BPartner Customer ON (Customer.C_BPartner_ID=DeliveryOrder.C_BPartner_ID)
                WHERE DeliveryOrder.IsActive='Y'
                  AND DeliveryOrder.IsSOTrx='Y'
                  AND DeliveryOrder.MovementType='C-'
                  AND DeliveryOrder.AD_Client_ID=@AD_Client_ID
                  AND DeliveryOrder.MovementDate>=@Month_Start
                  AND DeliveryOrder.MovementDate<@Next_Month_Start
                  AND DeliveryOrder.DocStatus NOT IN ('RE','VO')";

            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql,
                "DeliveryOrder",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            // The invoiced-line EXISTS subquery is injected only after MRole has
            // added its access predicates: on roles with record-access rules
            // AddAccessSQL appends a predicate for every table alias it finds -
            // including the subquery's M_InOutLine alias - to the OUTER
            // where-clause, where the alias is out of scope (ORA-00904).
            sql = sql.Replace("__VAS_HAS_INVOICED_LINE__", @"EXISTS (
                           SELECT 1
                           FROM M_InOutLine InvoicedLine
                           WHERE InvoicedLine.M_InOut_ID=DeliveryOrder.M_InOut_ID
                             AND InvoicedLine.IsActive='Y'
                             AND InvoicedLine.IsInvoiced='Y'
                       )");

            sql += " ORDER BY DeliveryOrder.MovementDate DESC, DeliveryOrder.M_InOut_ID DESC";

            List<object> rows = new List<object>();
            IDataReader dr = null;

            try
            {
                dr = DB.ExecuteReader(sql, new SqlParameter[]
                {
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                    new SqlParameter("@Month_Start", SqlDbType.DateTime) { Value = monthStart },
                    new SqlParameter("@Next_Month_Start", SqlDbType.DateTime) { Value = nextMonthStart }
                });

                while (dr != null && dr.Read())
                {
                    DateTime? doDate = Util.GetValueOfDateTime(dr["Do_Date"]);
                    rows.Add(new
                    {
                        deliveryOrderId = Util.GetValueOfInt(dr["Delivery_Order_ID"]),
                        doNumber = Util.GetValueOfString(dr["Do_Number"]),
                        doDate = doDate.HasValue ? doDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                        customerName = Util.GetValueOfString(dr["Customer_Name"]),
                        packageCount = Util.GetValueOfDecimal(dr["Package_Count"]),
                        grossWeight = Util.GetValueOfDecimal(dr["Gross_Weight"]),
                        tareWeight = Util.GetValueOfDecimal(dr["Tare_Weight"]),
                        statusCode = Util.GetValueOfString(dr["Status_Code"])
                    });
                }

                return Ok(new { rows = rows });
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
        /// Active line items of one customer delivery order (item name with
        /// charge/description fallback, quantity, locator), in line order. The
        /// header is joined and re-filtered so a foreign or non-customer-DO id
        /// returns nothing.
        /// </summary>
        /// <param name="deliveryOrderId">M_InOut_ID of the delivery order.</param>
        /// <returns>JSON { rows[] }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetDeliveryOrderLines(int deliveryOrderId = 0)
        {
            if (Session["ctx"] == null)
            {
                return Fail(Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired");
            }

            Ctx ctx = Session["ctx"] as Ctx;

            if (deliveryOrderId <= 0)
            {
                return Fail("Delivery Order is required.");
            }

            // LocatorCombination is not present on every database release;
            // fall back to the locator's search key when the column is missing.
            string locatorSql = HasColumn("M_Locator", "LocatorCombination")
                ? "COALESCE(Locator.LocatorCombination, Locator.Value)"
                : "Locator.Value";

            string sql = @"
                SELECT Line.M_InOutLine_ID AS Line_ID,
                       Line.Line AS Line_No,
                       COALESCE(Product.Name, Charge.Name, Line.Description) AS Item_Name,
                       COALESCE(Line.MovementQty, Line.QtyEntered, 0) AS Quantity,
                       " + locatorSql + @" AS Locator_Name
                FROM M_InOut DeliveryOrder
                INNER JOIN M_InOutLine Line ON (Line.M_InOut_ID=DeliveryOrder.M_InOut_ID AND Line.IsActive='Y')
                LEFT OUTER JOIN M_Product Product ON (Product.M_Product_ID=Line.M_Product_ID)
                LEFT OUTER JOIN C_Charge Charge ON (Charge.C_Charge_ID=Line.C_Charge_ID)
                LEFT OUTER JOIN M_Locator Locator ON (Locator.M_Locator_ID=Line.M_Locator_ID)
                WHERE DeliveryOrder.IsActive='Y'
                  AND DeliveryOrder.IsSOTrx='Y'
                  AND DeliveryOrder.MovementType='C-'
                  AND DeliveryOrder.DocStatus NOT IN ('RE','VO')
                  AND DeliveryOrder.AD_Client_ID=@AD_Client_ID
                  AND DeliveryOrder.M_InOut_ID=@Delivery_Order_ID";

            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql,
                "DeliveryOrder",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            sql += " ORDER BY Line.Line";

            List<object> rows = new List<object>();
            IDataReader dr = null;

            try
            {
                dr = DB.ExecuteReader(sql, new SqlParameter[]
                {
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                    new SqlParameter("@Delivery_Order_ID", deliveryOrderId)
                });

                while (dr != null && dr.Read())
                {
                    rows.Add(new
                    {
                        lineId = Util.GetValueOfInt(dr["Line_ID"]),
                        lineNo = Util.GetValueOfInt(dr["Line_No"]),
                        itemName = Util.GetValueOfString(dr["Item_Name"]),
                        quantity = Util.GetValueOfDecimal(dr["Quantity"]),
                        locatorName = Util.GetValueOfString(dr["Locator_Name"])
                    });
                }

                return Ok(new { rows = rows });
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

        /// <summary>True when the physical column exists on the active database.</summary>
        private bool HasColumn(string tableName, string columnName)
        {
            string sql;
            if (DB.IsPostgreSQL())
            {
                sql = @"
                    SELECT COUNT(1)
                    FROM information_schema.columns
                    WHERE UPPER(table_name)=UPPER(@TableName)
                      AND UPPER(column_name)=UPPER(@ColumnName)";
            }
            else
            {
                sql = @"
                    SELECT COUNT(1)
                    FROM USER_TAB_COLUMNS
                    WHERE TABLE_NAME=UPPER(@TableName)
                      AND COLUMN_NAME=UPPER(@ColumnName)";
            }

            try
            {
                return Util.GetValueOfInt(DB.ExecuteScalar(sql, new SqlParameter[]
                {
                    new SqlParameter("@TableName", tableName),
                    new SqlParameter("@ColumnName", columnName)
                }, null)) > 0;
            }
            catch
            {
                return false;
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
