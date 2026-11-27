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
    ///   VAS163      2026-11-27 Sr 65/66 amount = delivered qty x SO price without tax (per product UOM),
    ///                          converted to the base currency; totals summed from the converted amounts.
    /// </summary>
    public class VAS_154_ShippingMethodWidgetController : Controller
    {
        /// <summary>
        /// VAS163 2026-11-27: Sr 65/66 computed DO amount, correlated to the outer M_InOut alias "i": the sum over
        /// the DO's active lines of MovementQty (product UOM) x the SO line price per product UOM without tax
        /// (PriceActual * QtyEntered / QtyOrdered; on a tax-inclusive order - totals-based rule as VAS_285 / VAS_280:
        /// 2 * (GrandTotal - TotalLines) &lt; SUM(C_OrderTax.TaxAmt) - reduced by the net / gross ratio of the line's
        /// tax), converted from the SO currency to the base currency with the SO's conversion type and
        /// COALESCE(DateAcct, DateOrdered). A missing rate gives 0, as in the other SO widgets.
        /// </summary>
        /// <param name="baseCurrencyId">Base currency (accounting schema 1 of the tenant); 0 = no conversion.</param>
        /// <returns>Scalar sub query text.</returns>
        private static string BuildDoAmountSubquery(int baseCurrencyId)
        {
            string uomPriceSql = "(CASE WHEN COALESCE(ol.QtyOrdered, 0) <> 0 AND COALESCE(ol.QtyEntered, 0) <> 0 " +
                                 "THEN ol.PriceActual * ol.QtyEntered / ol.QtyOrdered ELSE COALESCE(ol.PriceActual, 0) END)";
            string netPriceSql = @"(CASE WHEN 2 * (COALESCE(so.GrandTotal, 0) - COALESCE(so.TotalLines, 0)) <
                                 (SELECT COALESCE(SUM(COALESCE(ot.TaxAmt, 0)), 0) FROM C_OrderTax ot
                                   WHERE ot.C_Order_ID = so.C_Order_ID AND ot.IsActive = 'Y')
                            THEN " + uomPriceSql + @" * COALESCE(1 -
                                 (SELECT COALESCE(SUM(COALESCE(otx.TaxAmt, 0)), 0) FROM C_OrderTax otx
                                   WHERE otx.C_Order_ID = so.C_Order_ID AND otx.C_Tax_ID = ol.C_Tax_ID AND otx.IsActive = 'Y')
                                 / NULLIF((SELECT SUM(olx.LineNetAmt) FROM C_OrderLine olx
                                            WHERE olx.C_Order_ID = so.C_Order_ID AND olx.C_Tax_ID = ol.C_Tax_ID AND olx.IsActive = 'Y'), 0), 1)
                            ELSE " + uomPriceSql + @" END)";
            string amountSql = "COALESCE(iol.MovementQty, 0) * " + netPriceSql;
            string convertedSql = baseCurrencyId > 0
                ? "(CASE WHEN so.C_Currency_ID = " + baseCurrencyId + " THEN " + amountSql + " " +
                  "ELSE COALESCE(currencyConvert(" + amountSql + ", so.C_Currency_ID, " + baseCurrencyId + ", " +
                  "COALESCE(so.DateAcct, so.DateOrdered), so.C_ConversionType_ID, so.AD_Client_ID, so.AD_Org_ID), 0) END)"
                : amountSql;

            return @"
            (SELECT COALESCE(SUM(" + convertedSql + @"), 0)
             FROM M_InOutLine iol
             INNER JOIN C_OrderLine ol ON (ol.C_OrderLine_ID = iol.C_OrderLine_ID)
             INNER JOIN C_Order so ON (so.C_Order_ID = ol.C_Order_ID)
             WHERE iol.M_InOut_ID = i.M_InOut_ID AND iol.IsActive = 'Y')";
        }

        /// <summary>VAS163 2026-11-27: Sr 66 base currency = currency of the tenant's accounting schema 1 (as VAS_285).</summary>
        /// <param name="ctx">Context.</param>
        /// <returns>C_Currency_ID or 0.</returns>
        private static int GetBaseCurrencyId(Ctx ctx)
        {
            if (ctx == null) { return 0; }
            return Util.GetValueOfInt(DB.ExecuteScalar(@"
                SELECT cs.C_Currency_ID
                  FROM AD_ClientInfo ci
                 INNER JOIN C_AcctSchema cs ON (cs.C_AcctSchema_ID = ci.C_AcctSchema1_ID)
                 WHERE ci.AD_Client_ID = @AD_Client_ID",
                new[] { new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()) }, null));
        }

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
                int baseCurrencyId = GetBaseCurrencyId(ctx);

                // VAS163 2026-11-27: Sr 65/66 one row per DO with its amount (without tax, base currency), summed per
                // method outside. AddAccessSQL runs on the M_InOut-only inner query, the amount sub query is injected
                // afterwards (its aliases must not reach AddAccessSQL) and the GROUP BY sits on the outer query.
                string innerSql = @"
                    SELECT i.DeliveryViaRule AS Method_Code,
                           i.M_InOut_ID AS M_InOut_ID,
                           __DO_AMOUNT__ AS Do_Amount
                    FROM M_InOut i
                    WHERE " + DoFilter + @"
                      AND i.DeliveryViaRule IN ('D', 'P', 'S')";
                innerSql = MRole.GetDefault(ctx).AddAccessSQL(innerSql, "i", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                innerSql = innerSql.Replace("__DO_AMOUNT__", BuildDoAmountSubquery(baseCurrencyId));

                string sql = @"
                    SELECT x.Method_Code AS Method_Code,
                           COUNT(DISTINCT x.M_InOut_ID) AS Do_Count,
                           COALESCE(SUM(x.Do_Amount), 0) AS Total_Amount
                    FROM (" + innerSql + @") x
                    GROUP BY x.Method_Code";

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

                return Ok(new { rows = rows, totalDoCount = totalDoCount, month = month, year = year, years = years, currency = GetCurrencyInfo(baseCurrencyId) });
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
            List<object> rows = new List<object>();
            decimal totalAmount = 0;
            // VAS163 2026-11-27: Sr 66 every DO amount is in the base currency, so each row carries the base currency.
            object[] cur = null;
            IDataReader dr = null;
            try
            {
                int baseCurrencyId = GetBaseCurrencyId(ctx);
                cur = LookupCurrency(baseCurrencyId);
                sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "i", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                sql = sql.Replace("__DO_AMOUNT__", BuildDoAmountSubquery(baseCurrencyId));
                sql += " ORDER BY i.MovementDate ASC, i.DocumentNo ASC";

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
                        doAmount = amount,
                        currencyIso = (string)cur[0],
                        currencySymbol = (string)cur[1],
                        currencyPrecision = (int)cur[2]
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

            return Ok(new { rows = rows, totalRecords = rows.Count, totalAmount = totalAmount, currency = new { iso = (string)cur[0], symbol = (string)cur[1], precision = (int)cur[2] } });
        }

        /// <summary>Distinct years with qualifying Delivery Order activity (desc), current year guaranteed present.</summary>
        /// <summary>
        /// The system currency (the session's base currency, $C_Currency_ID) as
        /// ISO code + symbol, so amounts show in the tenant's real currency instead
        /// of a hardcoded rupee. Returns empties if unavailable.
        /// </summary>
        private object GetCurrencyInfo(int baseCurrencyId)
        {
            // VAS163 2026-11-12: StdPrecision sent too (the widget showed 0 decimals).
            // VAS163 2026-11-27: Sr 66 the base currency of the accounting schema (the amounts are converted to it).
            object[] cur = LookupCurrency(baseCurrencyId);
            return new { iso = (string)cur[0], symbol = (string)cur[1], precision = (int)cur[2] };
        }

        /// <summary>VAS163 2026-11-12: { ISO_Code, CurSymbol, StdPrecision } of a currency; empties / 2 if unavailable.</summary>
        private object[] LookupCurrency(int currencyId)
        {
            string iso = "";
            string symbol = "";
            int precision = 2;
            if (currencyId > 0)
            {
                IDataReader cdr = null;
                try
                {
                    cdr = DB.ExecuteReader(
                        "SELECT ISO_Code, CurSymbol, StdPrecision FROM C_Currency WHERE C_Currency_ID = @Cur",
                        new SqlParameter[] { new SqlParameter("@Cur", currencyId) });
                    if (cdr != null && cdr.Read())
                    {
                        iso = Util.GetValueOfString(cdr["ISO_Code"]);
                        symbol = Util.GetValueOfString(cdr["CurSymbol"]);
                        precision = Util.GetValueOfInt(cdr["StdPrecision"]);
                    }
                }
                finally { if (cdr != null) { cdr.Close(); cdr.Dispose(); } }
            }
            return new object[] { iso, symbol, precision };
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
