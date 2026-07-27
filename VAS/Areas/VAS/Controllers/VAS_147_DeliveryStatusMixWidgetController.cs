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
    /// Module Name : VAS_147_DeliveryStatusMixWidget (Delivery Order dashboard)
    /// Purpose     : Backend for the 4x2 "Delivery Orders Status Mix" analytics
    ///               widget. For a selected Month + Year it groups outbound
    ///               customer Delivery Orders (M_InOut, IsSOTrx 'Y', MovementType
    ///               'C-') by sales representative (SalesRep_ID -> AD_User.Name)
    ///               and buckets each rep's document counts into four status
    ///               segments - In Progress (IP/WC/IN), Draft (DR), Completed
    ///               (CO/CL) and Voided (VO/RE) - with a per-rep total. Clicking
    ///               a rep opens a drill-down list of that rep's Delivery Orders
    ///               for the period with a computed value (sum of line
    ///               MovementQty * C_OrderLine.PriceActual - no dependency on any
    ///               optional amount column). Read-only: creates nothing. MRole
    ///               is applied to the primary fetched table (M_InOut) on every
    ///               read; all input is parameterized; the SQL uses only ANSI
    ///               constructs (EXTRACT, CASE, COUNT, OFFSET/FETCH) and never
    ///               COALESCEs the (possibly national-character) rep/customer
    ///               name against a literal, so it runs unchanged on Oracle and
    ///               PostgreSQL.
    /// Widget number 147.
    /// Chronological development:
    ///   &lt;EmpCode&gt;   2026-07-22 Created
    /// </summary>
    public class VAS_147_DeliveryStatusMixWidgetController : Controller
    {
        // Bucketed statuses only, so a rep's total equals the sum of the four
        // segments (unbucketed statuses such as Approved/Waiting-Payment are
        // excluded from this status-mix view).
        private const string BucketedStatuses = "('IP','WC','IN','DR','CO','CL','VO','RE')";

        /// <summary>
        /// Status-mix rows (per rep, four status buckets + total) for a period,
        /// plus the list of years that have Delivery Order activity.
        /// </summary>
        /// <param name="month">1-12; defaults to the current month when &lt;= 0.</param>
        /// <param name="year">4-digit year; defaults to the current year when &lt;= 0.</param>
        /// <returns>JSON { years[], month, year, rows[] }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetStatusMix(int month = 0, int year = 0)
        {
            if (Session["ctx"] == null)
            {
                return Fail(Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired");
            }
            Ctx ctx = Session["ctx"] as Ctx;

            DateTime today = DateTime.Today;
            if (month <= 0 || month > 12) { month = today.Month; }
            if (year <= 0) { year = today.Year; }

            try
            {
                List<int> years = GetYears(ctx, today.Year);

                // Half-open month window (>= first day of the month, < first day of
                // the next month), bound as DateTime params. Portable across Oracle
                // and PostgreSQL - no EXTRACT/TRUNC on the filtered column.
                DateTime dateFrom = new DateTime(year, month, 1);
                DateTime dateTo = dateFrom.AddMonths(1);

                string rawSql = @"
                    SELECT io.SalesRep_ID AS Rep_ID, u.Name AS Rep_Name,
                        SUM(CASE WHEN io.DocStatus IN ('IP','WC','IN') THEN 1 ELSE 0 END) AS In_Progress,
                        SUM(CASE WHEN io.DocStatus = 'DR' THEN 1 ELSE 0 END) AS Draft,
                        SUM(CASE WHEN io.DocStatus IN ('CO','CL') THEN 1 ELSE 0 END) AS Completed,
                        SUM(CASE WHEN io.DocStatus IN ('VO','RE') THEN 1 ELSE 0 END) AS Voided,
                        COUNT(*) AS Total_Count
                    FROM M_InOut io
                    LEFT JOIN AD_User u ON u.AD_User_ID = io.SalesRep_ID
                    WHERE io.IsActive = 'Y'
                      AND io.IsSOTrx = 'Y'
                      AND io.MovementType = 'C-'
                      AND io.DocStatus IN " + BucketedStatuses + @"
                      AND io.MovementDate >= @Date_From
                      AND io.MovementDate < @Date_To";

                // AddAccessSQL appends its access predicate to the END of the string,
                // so it must run on the SELECT...WHERE BEFORE the GROUP BY is attached
                // - otherwise the predicate lands after GROUP BY (ORA-00907).
                rawSql = MRole.GetDefault(ctx).AddAccessSQL(rawSql, "io", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                rawSql += " GROUP BY io.SalesRep_ID, u.Name";

                string sql = @"
                    SELECT Mix.Rep_ID, Mix.Rep_Name, Mix.In_Progress, Mix.Draft, Mix.Completed, Mix.Voided, Mix.Total_Count
                    FROM ( " + rawSql + @" ) Mix
                    ORDER BY Mix.Total_Count DESC, Mix.Rep_Name ASC";

                List<object> rows = new List<object>();
                IDataReader dr = null;
                try
                {
                    dr = DB.ExecuteReader(sql, new SqlParameter[]
                    {
                        new SqlParameter("@Date_From", SqlDbType.DateTime) { Value = dateFrom },
                        new SqlParameter("@Date_To", SqlDbType.DateTime) { Value = dateTo }
                    });
                    while (dr != null && dr.Read())
                    {
                        string repName = Util.GetValueOfString(dr["Rep_Name"]);
                        rows.Add(new
                        {
                            repId = Util.GetValueOfInt(dr["Rep_ID"]),
                            repName = string.IsNullOrEmpty(repName) ? "" : repName,
                            inProgress = Util.GetValueOfInt(dr["In_Progress"]),
                            draft = Util.GetValueOfInt(dr["Draft"]),
                            completed = Util.GetValueOfInt(dr["Completed"]),
                            voided = Util.GetValueOfInt(dr["Voided"]),
                            total = Util.GetValueOfInt(dr["Total_Count"])
                        });
                    }
                }
                finally
                {
                    if (dr != null) { dr.Close(); dr.Dispose(); }
                }

                return Ok(new { years = years, month = month, year = year, rows = rows });
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
            }
        }

        /// <summary>
        /// Drill-down: the Delivery Orders for one rep in a given period, each
        /// with a computed value (sum of line MovementQty * C_OrderLine.PriceActual).
        /// </summary>
        /// <param name="repId">SalesRep_ID; 0 means the unassigned (null rep) bucket.</param>
        /// <param name="month">1-12.</param>
        /// <param name="year">4-digit year.</param>
        /// <returns>JSON { rows[], totalValue }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetRepDeliveryOrders(int repId, int month, int year)
        {
            if (Session["ctx"] == null)
            {
                return Fail(Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired");
            }
            Ctx ctx = Session["ctx"] as Ctx;
            if (month <= 0 || month > 12 || year <= 0) { return Fail("Invalid period."); }

            string repFilter = repId > 0 ? " AND io.SalesRep_ID = @Rep_ID " : " AND io.SalesRep_ID IS NULL ";

            // Half-open month window (portable, no EXTRACT on the filtered column).
            DateTime dateFrom = new DateTime(year, month, 1);
            DateTime dateTo = dateFrom.AddMonths(1);

            // The DO value is a correlated subquery injected AFTER AddAccessSQL: with
            // SQL_FULLYQUALIFIED, AddAccessSQL would otherwise append predicates for
            // the subquery's M_InOutLine/C_OrderLine aliases to the OUTER where-clause
            // where they are out of scope (ORA-00904).
            string sql = @"
                SELECT io.DocumentNo, bp.Name AS Customer_Name, io.MovementDate, io.DocStatus,
                    COALESCE(__DO_VALUE__, 0) AS DO_Value
                FROM M_InOut io
                LEFT JOIN C_BPartner bp ON bp.C_BPartner_ID = io.C_BPartner_ID
                WHERE io.IsActive = 'Y'
                  AND io.IsSOTrx = 'Y'
                  AND io.MovementType = 'C-'
                  AND io.DocStatus IN " + BucketedStatuses + @"
                  AND io.MovementDate >= @Date_From
                  AND io.MovementDate < @Date_To "
                  + repFilter;

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "io", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            sql = sql.Replace("__DO_VALUE__", @"(
                     SELECT SUM(iol.MovementQty * COALESCE(ol.PriceActual, 0))
                     FROM M_InOutLine iol
                     LEFT JOIN C_OrderLine ol ON ol.C_OrderLine_ID = iol.C_OrderLine_ID
                     WHERE iol.M_InOut_ID = io.M_InOut_ID AND iol.IsActive = 'Y')");

            sql += " ORDER BY io.MovementDate DESC, io.DocumentNo DESC";

            List<SqlParameter> parameters = new List<SqlParameter>();
            parameters.Add(new SqlParameter("@Date_From", SqlDbType.DateTime) { Value = dateFrom });
            parameters.Add(new SqlParameter("@Date_To", SqlDbType.DateTime) { Value = dateTo });
            if (repId > 0) { parameters.Add(new SqlParameter("@Rep_ID", repId)); }

            List<object> rows = new List<object>();
            decimal totalValue = 0;
            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, parameters.ToArray());
                while (dr != null && dr.Read())
                {
                    decimal value = Util.GetValueOfDecimal(dr["DO_Value"]);
                    totalValue += value;
                    DateTime? d = Util.GetValueOfDateTime(dr["MovementDate"]);
                    rows.Add(new
                    {
                        documentNo = Util.GetValueOfString(dr["DocumentNo"]),
                        customerName = Util.GetValueOfString(dr["Customer_Name"]),
                        movementDate = d.HasValue ? d.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                        docStatus = Util.GetValueOfString(dr["DocStatus"]),
                        value = value
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

            return Ok(new { rows = rows, totalValue = totalValue, currency = GetCurrencyInfo(ctx) });
        }

        /// <summary>
        /// The system currency (the session's base currency, $C_Currency_ID) as
        /// ISO code + symbol, so the modal shows amounts in the tenant's real
        /// currency instead of a hardcoded rupee. Returns empties if unavailable.
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

        /// <summary>Distinct years with Delivery Order activity (desc), with the current year guaranteed present.</summary>
        private List<int> GetYears(Ctx ctx, int currentYear)
        {
            // EXTRACT(YEAR ...) in the SELECT list is fine - it only builds the
            // dropdown list, it is not a filter. AddAccessSQL still runs before the
            // ORDER BY is appended (it appends to the string end -> ORA-00907 if the
            // ORDER BY is already there).
            string sql = @"
                SELECT DISTINCT EXTRACT(YEAR FROM io.MovementDate) AS Yr
                FROM M_InOut io
                WHERE io.IsActive = 'Y' AND io.IsSOTrx = 'Y' AND io.MovementType = 'C-'
                  AND io.DocStatus IN " + BucketedStatuses + @"
                  AND io.MovementDate IS NOT NULL";
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "io", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
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
