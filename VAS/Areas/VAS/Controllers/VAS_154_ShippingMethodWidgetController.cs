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
    /// Module Name : VAS_154_ShippingMethodWidget (Delivery Order dashboard)
    /// Purpose     : Backend for the 3x2 "Shipping Method" widget - a read-only
    ///               donut + legend that, for a selected month/year, splits
    ///               outbound customer Delivery Orders (M_InOut) by shipping
    ///               method DeliveryViaRule (D = Delivery, P = Pickup,
    ///               S = Shipper), giving each method a DO count and a total
    ///               value, plus a drill-down list per method.
    ///
    ///               DELIVERY ORDER AMOUNT (revised rule):
    ///               The originally specified header column
    ///               M_InOut.VA077_TotalSalesAmt is an obsolete column that does
    ///               not exist in this environment and is to be ignored (per the
    ///               spec owner, 2026-07-22). The amount is instead COMPUTED from
    ///               the delivered quantity and the Sales Order line unit price
    ///               including its tax:
    ///                 DO amount = SUM over the DO's active lines of
    ///                   M_InOutLine.MovementQty
    ///                   * COALESCE(C_OrderLine.PriceActual, 0)
    ///                   * (1 + COALESCE(C_Tax.Rate, 0) / 100)
    ///               (C_OrderLine.C_Tax_ID -> C_Tax.Rate). This correctly tracks
    ///               PARTIAL Delivery Orders because it uses the DO's own
    ///               MovementQty, not the full Sales Order total. Because the
    ///               amount now derives from lines, the summary joins
    ///               M_InOutLine/C_OrderLine (the spec's original "header only"
    ///               restriction assumed the obsolete header column).
    ///
    ///               Read-only: creates/updates nothing. MRole is applied to the
    ///               fetched table (M_InOut) on every read; month filtering uses
    ///               a bound half-open date range (>= start, < next-month start)
    ///               - no TRUNC / EXTRACT / DATE_TRUNC - so the SQL runs unchanged
    ///               on Oracle and PostgreSQL; all input is parameterized and
    ///               method_code is validated to D/P/S before use.
    /// Widget number 154.
    /// Chronological development:
    ///   &lt;EmpCode&gt;   2026-07-22 Created (amount = computed line unit price + tax x DO qty)
    /// </summary>
    public class VAS_154_ShippingMethodWidgetController : Controller
    {
        // Computed DO amount expression, correlated to the outer M_InOut alias "i".
        private const string DoAmountSubquery = @"
            (SELECT COALESCE(SUM(iol.MovementQty * COALESCE(ol.PriceActual, 0)
                                 * (1 + COALESCE(t.Rate, 0) / 100)), 0)
             FROM M_InOutLine iol
             JOIN C_OrderLine ol ON ol.C_OrderLine_ID = iol.C_OrderLine_ID
             LEFT JOIN C_Tax t ON t.C_Tax_ID = ol.C_Tax_ID
             WHERE iol.M_InOut_ID = i.M_InOut_ID AND iol.IsActive = 'Y')";

        // Delivery Order qualification filter shared by summary and drill-down.
        private const string DoFilter = @"
            i.IsActive = 'Y'
            AND i.IsSOTrx = 'Y'
            AND COALESCE(i.IsReturnTrx, 'N') = 'N'
            AND i.DocStatus NOT IN ('VO', 'RE')
            AND i.MovementDate >= @Start_Date
            AND i.MovementDate < @End_Date";

        /// <summary>
        /// Per-method summary (count + computed total value) for a month, plus the
        /// list of years that have Delivery Order activity.
        /// </summary>
        /// <param name="month">1-12; defaults to the current month when out of range.</param>
        /// <param name="year">4-digit year; defaults to the current year when &lt;= 0.</param>
        /// <returns>JSON { rows[], totalDoCount, month, year, years[] }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetSummary(int month = 0, int year = 0)
        {
            if (Session["ctx"] == null)
            {
                return Fail(Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired");
            }
            Ctx ctx = Session["ctx"] as Ctx;

            DateTime today = DateTime.Today;
            if (month <= 0 || month > 12) { month = today.Month; }
            if (year <= 0) { year = today.Year; }
            DateTime start = new DateTime(year, month, 1);
            DateTime end = start.AddMonths(1);

            try
            {
                List<int> years = GetYears(ctx, today.Year);

                string sql = @"
                    SELECT i.DeliveryViaRule AS Method_Code,
                           COUNT(DISTINCT i.M_InOut_ID) AS Do_Count,
                           COALESCE(SUM(iol.MovementQty * COALESCE(ol.PriceActual, 0)
                                        * (1 + COALESCE(t.Rate, 0) / 100)), 0) AS Total_Amount
                    FROM M_InOut i
                    LEFT JOIN M_InOutLine iol ON iol.M_InOut_ID = i.M_InOut_ID AND iol.IsActive = 'Y'
                    LEFT JOIN C_OrderLine ol ON ol.C_OrderLine_ID = iol.C_OrderLine_ID
                    LEFT JOIN C_Tax t ON t.C_Tax_ID = ol.C_Tax_ID
                    WHERE " + DoFilter + @"
                      AND i.DeliveryViaRule IN ('D', 'P', 'S')";

                // AddAccessSQL appends its access predicate to the END of the string,
                // so apply it BEFORE the GROUP BY is attached - otherwise the
                // predicate lands after GROUP BY (ORA-00933 -> "Data unavailable").
                sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "i", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                sql += " GROUP BY i.DeliveryViaRule";

                List<object> rows = new List<object>();
                int totalDoCount = 0;
                IDataReader dr = null;
                try
                {
                    dr = DB.ExecuteReader(sql, new SqlParameter[]
                    {
                        new SqlParameter("@Start_Date", start),
                        new SqlParameter("@End_Date", end)
                    });
                    while (dr != null && dr.Read())
                    {
                        int count = Util.GetValueOfInt(dr["Do_Count"]);
                        totalDoCount += count;
                        rows.Add(new
                        {
                            methodCode = Util.GetValueOfString(dr["Method_Code"]),
                            doCount = count,
                            totalAmount = Util.GetValueOfDecimal(dr["Total_Amount"])
                        });
                    }
                }
                finally
                {
                    if (dr != null) { dr.Close(); dr.Dispose(); }
                }

                return Ok(new { rows = rows, totalDoCount = totalDoCount, month = month, year = year, years = years, currency = GetCurrencyInfo(ctx) });
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
            }
        }

        /// <summary>
        /// Drill-down: the Delivery Orders for one shipping method in a month,
        /// each with its computed amount, ordered by date then document number.
        /// </summary>
        /// <param name="methodCode">D, P or S (validated).</param>
        /// <param name="month">1-12.</param>
        /// <param name="year">4-digit year.</param>
        /// <returns>JSON { rows[], totalRecords, totalAmount }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetDrillDown(string methodCode, int month, int year)
        {
            if (Session["ctx"] == null)
            {
                return Fail(Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired");
            }
            Ctx ctx = Session["ctx"] as Ctx;

            if (methodCode != "D" && methodCode != "P" && methodCode != "S")
            {
                return Fail("Invalid shipping method.");
            }
            if (month <= 0 || month > 12 || year <= 0) { return Fail("Invalid period."); }
            DateTime start = new DateTime(year, month, 1);
            DateTime end = start.AddMonths(1);

            string sql = @"
                SELECT i.M_InOut_ID AS Do_Id,
                       i.DocumentNo AS Do_Number,
                       o.C_Order_ID AS So_Id,
                       o.DocumentNo AS So_Number,
                       bp.Name AS Customer_Name,
                       i.MovementDate AS Do_Date,
                       o.C_Currency_ID AS Currency_Id,
                       __DO_AMOUNT__ AS Do_Amount
                FROM M_InOut i
                LEFT JOIN C_Order o ON o.C_Order_ID = i.C_Order_ID
                LEFT JOIN C_BPartner bp ON bp.C_BPartner_ID = i.C_BPartner_ID
                WHERE " + DoFilter + @"
                  AND i.DeliveryViaRule = @Method_Code";

            // Run AddAccessSQL on the outer query first (only i/o/bp aliases in
            // scope), THEN inject the amount subquery and append ORDER BY. Injecting
            // the subquery earlier would expose its iol/ol/t aliases to AddAccessSQL,
            // which appends predicates for them to the outer WHERE (ORA-00904); and
            // AddAccessSQL must precede ORDER BY or the predicate lands after it.
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "i", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            sql = sql.Replace("__DO_AMOUNT__", DoAmountSubquery);
            sql += " ORDER BY i.MovementDate ASC, i.DocumentNo ASC";

            List<object> rows = new List<object>();
            decimal totalAmount = 0;
            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, new SqlParameter[]
                {
                    new SqlParameter("@Start_Date", start),
                    new SqlParameter("@End_Date", end),
                    new SqlParameter("@Method_Code", methodCode)
                });
                while (dr != null && dr.Read())
                {
                    decimal amount = Util.GetValueOfDecimal(dr["Do_Amount"]);
                    totalAmount += amount;
                    DateTime? d = Util.GetValueOfDateTime(dr["Do_Date"]);
                    rows.Add(new
                    {
                        doId = Util.GetValueOfInt(dr["Do_Id"]),
                        doNumber = Util.GetValueOfString(dr["Do_Number"]),
                        soId = Util.GetValueOfInt(dr["So_Id"]),
                        soNumber = Util.GetValueOfString(dr["So_Number"]),
                        customerName = Util.GetValueOfString(dr["Customer_Name"]),
                        doDate = d.HasValue ? d.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                        doAmount = amount
                    });
                }
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
            }
            finally
            {
                if (dr != null) { dr.Close(); dr.Dispose(); }
            }

            return Ok(new { rows = rows, totalRecords = rows.Count, totalAmount = totalAmount, currency = GetCurrencyInfo(ctx) });
        }

        /// <summary>Distinct years with qualifying Delivery Order activity (desc), current year guaranteed present.</summary>
        /// <summary>
        /// The system currency (the session's base currency, $C_Currency_ID) as
        /// ISO code + symbol, so amounts show in the tenant's real currency instead
        /// of a hardcoded rupee. Returns empties if unavailable.
        /// </summary>
        private object GetCurrencyInfo(Ctx ctx)
        {
            string iso = "";
            string symbol = "";
            int currencyId = ctx.GetContextAsInt("$C_Currency_ID");
            if (currencyId > 0)
            {
                IDataReader cdr = null;
                try
                {
                    cdr = DB.ExecuteReader(
                        "SELECT ISO_Code, CurSymbol FROM C_Currency WHERE C_Currency_ID = @Cur",
                        new SqlParameter[] { new SqlParameter("@Cur", currencyId) });
                    if (cdr != null && cdr.Read())
                    {
                        iso = Util.GetValueOfString(cdr["ISO_Code"]);
                        symbol = Util.GetValueOfString(cdr["CurSymbol"]);
                    }
                }
                finally { if (cdr != null) { cdr.Close(); cdr.Dispose(); } }
            }
            return new { iso = iso, symbol = symbol };
        }

        private List<int> GetYears(Ctx ctx, int currentYear)
        {
            string sql = @"
                SELECT DISTINCT EXTRACT(YEAR FROM i.MovementDate) AS Yr
                FROM M_InOut i
                WHERE i.IsActive = 'Y' AND i.IsSOTrx = 'Y' AND COALESCE(i.IsReturnTrx, 'N') = 'N'
                  AND i.DocStatus NOT IN ('VO', 'RE')
                  AND i.DeliveryViaRule IN ('D', 'P', 'S')
                  AND i.MovementDate IS NOT NULL";
            // EXTRACT(YEAR ...) in the SELECT only builds the dropdown list (allowed).
            // AddAccessSQL runs before the ORDER BY is appended (it appends to the
            // string end -> ORA-00933 if the ORDER BY is already there).
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "i", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            sql += " ORDER BY Yr DESC";

            List<int> years = new List<int>();
            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, new SqlParameter[0]);
                while (dr != null && dr.Read())
                {
                    int y = Util.GetValueOfInt(dr["Yr"]);
                    if (y > 0) { years.Add(y); }
                }
            }
            finally
            {
                if (dr != null) { dr.Close(); dr.Dispose(); }
            }

            if (!years.Contains(currentYear))
            {
                years.Add(currentYear);
                years.Sort();
                years.Reverse();
            }
            return years;
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
