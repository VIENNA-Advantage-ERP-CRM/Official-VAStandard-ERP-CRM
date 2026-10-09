using Newtonsoft.Json;
using System;
using System.Data;
using System.Globalization;
using System.Web.Mvc;
using VAdvantage.DataBase;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VAS.Controllers
{
    /// <summary>
    /// Module Name : VAS_010_CashGlCashInController
    /// Purpose     : Supplies KPI data for the Cash Journal "Cash In" dashboard widget.
    ///               Returns today's cash-in total converted to the accounting-schema base
    ///               currency, receipt count, and the 7-day daily average.
    ///               Supports PostgreSQL and Oracle.
    /// Chronological development:
    ///   &lt;EmployeeCode&gt;   2026-06-01
    /// </summary>
    public class VAS_010_CashGlCashInController : Controller
    {
        /// <summary>
        /// Returns today's cash-in total, receipt count, 7-day daily average, currency
        /// symbol, and schema precision for the Cash In KPI widget.
        /// Every CashLine amount is converted to the accounting-schema base currency
        /// before aggregation so mixed-currency cash books are handled correctly.
        /// </summary>
        /// <returns>JSON: todayAmount, todayCount, avgDailyAmount, symbol, precision</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetCashInKpi()
        {
            if (Session["ctx"] == null)
            {
                return Json(
                    new { error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired" },
                    JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            // Date boundaries computed in C# so no DB-function strings are
            // concatenated into SQL. ToSqlDate() formats them as typed literals
            // (TO_DATE for Oracle, DB.TO_DATE for PostgreSQL) — no injection surface.
            DateTime today        = DateTime.Today;
            DateTime tomorrow     = today.AddDays(1);
            DateTime sevenDaysAgo = today.AddDays(-7);

            // ── SchemaCurrency CTE ───────────────────────────────────────────
            // No user-provided values concatenated. AD_Client_ID scoping is handled
            // through the JOIN ON (Sch.AD_Client_ID=Cash.AD_Client_ID) inside each
            // CTE body that references this result set.
            string schemaCurrencySql =
                "SELECT ClientInfo.AD_Client_ID," +
                "       AcctSchema.C_Currency_ID AS C_Currency_ID," +
                "       Currency.StdPrecision," +
                "       CASE WHEN Currency.CurSymbol IS NOT NULL THEN Currency.CurSymbol ELSE Currency.ISO_Code END AS Cur_Symbol" +
                " FROM AD_ClientInfo ClientInfo" +
                " INNER JOIN C_AcctSchema AcctSchema ON (ClientInfo.C_AcctSchema1_ID=AcctSchema.C_AcctSchema_ID)" +
                " INNER JOIN C_Currency Currency ON (AcctSchema.C_Currency_ID=Currency.C_Currency_ID)";

            // ── TodayData CTE body ───────────────────────────────────────────
            // Date range: Cash.DateAcct >= today AND < tomorrow
            // Using a range on the raw column (no truncation in WHERE) so the index
            // on DateAcct can be used. ToSqlDate() values are C#-computed — no injection.
            // Each CashLine amount is converted to the base currency via CurrencyConvert
            // so cash books denominated in different currencies sum correctly.
            // MRole applied on C_Cash (alias Cash) — primary physical table only.
            string todayCteBody =
                "SELECT COALESCE(SUM(" +
                "    CASE WHEN Cash.C_Currency_ID=Sch.C_Currency_ID" +
                "    THEN CashLine.Amount" +
                "    ELSE CurrencyConvert(CashLine.Amount, Cash.C_Currency_ID, Sch.C_Currency_ID, Cash.DateAcct, NULL, Cash.AD_Client_ID, Cash.AD_Org_ID)" +
                "    END" +
                "), 0) AS TodayAmount," +
                "       COUNT(1) AS TodayCount," +
                "       MIN(Sch.Cur_Symbol) AS Cur_Symbol," +
                "       MIN(Sch.StdPrecision) AS StdPrecision" +
                " FROM C_CashLine CashLine" +
                " INNER JOIN C_Cash Cash ON (Cash.C_Cash_ID=CashLine.C_Cash_ID)" +
                " INNER JOIN SchemaCurrency Sch ON (Sch.AD_Client_ID=Cash.AD_Client_ID)" +
                " WHERE Cash.IsActive='Y'" +
                "   AND CashLine.IsActive='Y'" +
                "   AND Cash.DocStatus IN ('CO','CL')" +
                "   AND CashLine.Amount>0" +
                "   AND Cash.DateAcct>=" + ToSqlDate(today) +
                "   AND Cash.DateAcct<" + ToSqlDate(tomorrow);

            todayCteBody = MRole.GetDefault(ctx).AddAccessSQL(
                todayCteBody, "Cash", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            // ── DailyTotals CTE body ─────────────────────────────────────────
            // Same currency-conversion pattern. Grouped by calendar day so each day
            // carries equal weight in the 7-day average.
            // TruncColumn is used only in SELECT and GROUP BY on the hardcoded column
            // name "Cash.DateAcct" — not on any user-controlled value.
            // MRole is applied before GROUP BY is appended.
            string dailyCteBody =
                "SELECT " + TruncColumn("Cash.DateAcct") + " AS CashDate," +
                "       SUM(" +
                "    CASE WHEN Cash.C_Currency_ID=Sch.C_Currency_ID" +
                "    THEN CashLine.Amount" +
                "    ELSE CurrencyConvert(CashLine.Amount, Cash.C_Currency_ID, Sch.C_Currency_ID, Cash.DateAcct, NULL, Cash.AD_Client_ID, Cash.AD_Org_ID)" +
                "    END" +
                "       ) AS DailyAmount" +
                " FROM C_CashLine CashLine" +
                " INNER JOIN C_Cash Cash ON (Cash.C_Cash_ID=CashLine.C_Cash_ID)" +
                " INNER JOIN SchemaCurrency Sch ON (Sch.AD_Client_ID=Cash.AD_Client_ID)" +
                " WHERE Cash.IsActive='Y'" +
                "   AND CashLine.IsActive='Y'" +
                "   AND Cash.DocStatus IN ('CO','CL')" +
                "   AND CashLine.Amount>0" +
                "   AND Cash.DateAcct>=" + ToSqlDate(sevenDaysAgo) +
                "   AND Cash.DateAcct<" + ToSqlDate(today);

            dailyCteBody = MRole.GetDefault(ctx).AddAccessSQL(
                dailyCteBody, "Cash", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            // GROUP BY on a hardcoded column name — not user-controlled.
            // TruncColumn strips the time component so each calendar day is one group.
            dailyCteBody += " GROUP BY " + TruncColumn("Cash.DateAcct");

            // ── Final assembled query ────────────────────────────────────────
            // MRole is NOT applied here — access filtering already lives inside
            // TodayData and DailyTotals per the CTE/MRole rule.
            string sql =
                "WITH SchemaCurrency AS (" + schemaCurrencySql + ")," +
                " TodayData AS (" + todayCteBody + ")," +
                " DailyTotals AS (" + dailyCteBody + ")" +
                " SELECT TodayData.TodayAmount," +
                "        TodayData.TodayCount," +
                "        (SELECT AVG(D.DailyAmount) FROM DailyTotals D) AS AvgDailyAmount," +
                "        TodayData.Cur_Symbol," +
                "        TodayData.StdPrecision" +
                " FROM TodayData";

            decimal todayAmount    = 0;
            int     todayCount     = 0;
            decimal avgDailyAmount = 0;
            string  curSymbol      = "";
            // StdPrecision is read from C_Currency.StdPrecision via SchemaCurrency — never hardcoded.
            int     stdPrecision   = 0;

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql);
                if (dr != null && dr.Read())
                {
                    todayAmount    = Util.GetValueOfDecimal(dr["TodayAmount"]);
                    todayCount     = Util.GetValueOfInt(dr["TodayCount"]);
                    avgDailyAmount = Util.GetValueOfDecimal(dr["AvgDailyAmount"]);
                    curSymbol      = Util.GetValueOfString(dr["Cur_Symbol"]);
                    stdPrecision   = Util.GetValueOfInt(dr["StdPrecision"]);
                }
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
            finally
            {
                if (dr != null) { dr.Close(); dr.Dispose(); }
            }

            var result = new
            {
                todayAmount    = Math.Round(todayAmount,    stdPrecision),
                todayCount     = todayCount,
                avgDailyAmount = Math.Round(avgDailyAmount, stdPrecision),
                symbol         = curSymbol,
                precision      = stdPrecision
            };

            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Formats a C#-computed DateTime as a typed date literal safe to embed in SQL.
        /// Oracle : TO_DATE('yyyy-MM-dd','YYYY-MM-DD')
        /// PostgreSQL : DB.TO_DATE(day, true)
        /// The value is always server-side computed — never from user input.
        /// </summary>
        /// <param name="date">Server-side computed date</param>
        private static string ToSqlDate(DateTime date)
        {
            DateTime day = date.Date;

            if (DB.IsOracle())
            {
                return "TO_DATE('"
                    + day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    + "','YYYY-MM-DD')";
            }

            return DB.TO_DATE(day, true);
        }

        /// <summary>
        /// Wraps a hardcoded column name in the DB-specific expression that returns
        /// a plain calendar date, used in SELECT and GROUP BY to strip the time component.
        /// Oracle : TRUNC(col) — PostgreSQL : CAST(col AS DATE)
        /// The input is always a hardcoded column reference — never from user input.
        /// </summary>
        /// <param name="columnExpression">Hardcoded column, e.g. "Cash.DateAcct"</param>
        private static string TruncColumn(string columnExpression)
        {
            if (DB.IsOracle())
            {
                return "TRUNC(" + columnExpression + ")";
            }

            return "CAST(" + columnExpression + " AS DATE)";
        }
    }
}
