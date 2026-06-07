using System;
using System.Data;
using System.Globalization;
using System.Web.Mvc;
using VAdvantage.DataBase;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Controllers;

namespace VAS.Controllers
{
    /// <summary>
    /// Module Name : Cash Journal Dashboard Widget
    /// Purpose     : Loads today's cash-in amount from Cash Journal.
    /// Chronological development:
    ///   VAS   Created 2026-06-06
    /// </summary>
    public class VAS_047_TodayCashInCashJournalWidgetController : Controller
    {
        /// <summary>
        /// Gets today's cash-in amount from completed or closed cash journals.
        /// </summary>
        /// <returns>JSON result containing widget KPI data.</returns>
        public JsonResult GetTodayCashInData()
        {
            Ctx ctx = Session["ctx"] as Ctx;

            if (ctx == null)
            {
                return Json(new
                {
                    success = false,
                    error = "Session Expired",
                    hasData = false
                }, JsonRequestBehavior.AllowGet);
            }

            IDataReader dr = null;

            try
            {
                DateTime today = DateTime.Now.Date;
                DateTime dateFrom = today;
                DateTime dateTo = today.AddDays(1);
                DateTime averageDateFrom = today.AddDays(-7);

                string dateFilter = GetDateFilter("c.StatementDate", dateFrom, dateTo);
                string averageDateFilter = GetDateFilter("c.StatementDate", averageDateFrom, today);

                string schemaCurrencySql = @"
                    SELECT ClientInfo.AD_Client_ID,
                           AcctSchema.C_Currency_ID AS C_Currency_ID,
                           Currency.StdPrecision,
                           Currency.ISO_Code AS ISO_Code,
                           CASE
                               WHEN Currency.CurSymbol IS NOT NULL THEN Currency.CurSymbol
                               ELSE Currency.ISO_Code
                           END AS Cur_Symbol
                    FROM AD_ClientInfo ClientInfo
                    INNER JOIN C_AcctSchema AcctSchema
                        ON (ClientInfo.C_AcctSchema1_ID = AcctSchema.C_AcctSchema_ID)
                    INNER JOIN C_Currency Currency
                        ON (AcctSchema.C_Currency_ID = Currency.C_Currency_ID)";

                string mainSql = @"
                    SELECT ROUND(
                               COALESCE(SUM(
                                   CASE
                                       WHEN c.C_Currency_ID = SchemaCurrency.C_Currency_ID THEN COALESCE(cl.Amount, 0)
                                       ELSE CurrencyConvert(
                                           COALESCE(cl.Amount, 0),
                                           c.C_Currency_ID,
                                           SchemaCurrency.C_Currency_ID,
                                           c.StatementDate,
                                           0,
                                           c.AD_Client_ID,
                                           c.AD_Org_ID
                                       )
                                   END
                               ), 0),
                               COALESCE(MAX(SchemaCurrency.StdPrecision), 2)
                           ) AS MainMetric,
                           MAX(SchemaCurrency.C_Currency_ID) AS C_Currency_ID,
                           MAX(SchemaCurrency.ISO_Code) AS CurrencyISO,
                           MAX(SchemaCurrency.Cur_Symbol) AS CurrencySymbol,
                           COALESCE(MAX(SchemaCurrency.StdPrecision), 2) AS StdPrecision,
                           COUNT(cl.C_CashLine_ID) AS RecordCount
                    FROM C_Cash c
                    INNER JOIN C_CashLine cl
                        ON (c.C_Cash_ID = cl.C_Cash_ID)
                    INNER JOIN SchemaCurrency SchemaCurrency
                        ON (SchemaCurrency.AD_Client_ID = c.AD_Client_ID)
                    WHERE c.IsActive = 'Y'
                    AND cl.IsActive = 'Y'
                    AND c.DocStatus IN ('CO','CL')
                    AND cl.Amount > 0
                    " + dateFilter;

                mainSql = MRole.GetDefault(ctx).AddAccessSQL(mainSql, "c", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                string dailySql = @"
                    SELECT " + TruncColumn("c.StatementDate") + @" AS CashDate,
                           SUM(
                               CASE
                                   WHEN c.C_Currency_ID = SchemaCurrency.C_Currency_ID THEN COALESCE(cl.Amount, 0)
                                   ELSE CurrencyConvert(
                                       COALESCE(cl.Amount, 0),
                                       c.C_Currency_ID,
                                       SchemaCurrency.C_Currency_ID,
                                       c.StatementDate,
                                       0,
                                       c.AD_Client_ID,
                                       c.AD_Org_ID
                                   )
                               END
                           ) AS DailyAmount
                    FROM C_Cash c
                    INNER JOIN C_CashLine cl
                        ON (c.C_Cash_ID = cl.C_Cash_ID)
                    INNER JOIN SchemaCurrency SchemaCurrency
                        ON (SchemaCurrency.AD_Client_ID = c.AD_Client_ID)
                    WHERE c.IsActive = 'Y'
                    AND cl.IsActive = 'Y'
                    AND c.DocStatus IN ('CO','CL')
                    AND cl.Amount > 0
                    " + averageDateFilter;

                dailySql = MRole.GetDefault(ctx).AddAccessSQL(dailySql, "c", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                dailySql += " GROUP BY " + TruncColumn("c.StatementDate");

                string sql = @"
                    WITH SchemaCurrency AS (
                        " + schemaCurrencySql + @"
                    ),
                    TodayData AS (
                        " + mainSql + @"
                    ),
                    DailyTotals AS (
                        " + dailySql + @"
                    )
                    SELECT TodayData.MainMetric,
                           TodayData.C_Currency_ID,
                           TodayData.CurrencyISO,
                           TodayData.CurrencySymbol,
                           TodayData.StdPrecision,
                           TodayData.RecordCount,
                           COALESCE((SELECT AVG(DailyTotals.DailyAmount) FROM DailyTotals), 0) AS AvgDailyAmount
                    FROM TodayData";

                decimal mainMetric = 0;
                decimal avgDailyAmount = 0;
                int recordCount = 0;
                int currencyId = 0;
                string currencyISO = "";
                string currencySymbol = "";
                int stdPrecision = 2;

                dr = DB.ExecuteReader(sql, null, null);

                if (dr.Read())
                {
                    if (dr["MainMetric"] != DBNull.Value)
                    {
                        mainMetric = Convert.ToDecimal(dr["MainMetric"]);
                    }

                    if (dr["RecordCount"] != DBNull.Value)
                    {
                        recordCount = Convert.ToInt32(dr["RecordCount"]);
                    }

                    if (dr["AvgDailyAmount"] != DBNull.Value)
                    {
                        avgDailyAmount = Convert.ToDecimal(dr["AvgDailyAmount"]);
                    }

                    if (dr["C_Currency_ID"] != DBNull.Value)
                    {
                        currencyId = Convert.ToInt32(dr["C_Currency_ID"]);
                    }

                    if (dr["CurrencyISO"] != DBNull.Value)
                    {
                        currencyISO = Convert.ToString(dr["CurrencyISO"]);
                    }

                    if (dr["CurrencySymbol"] != DBNull.Value)
                    {
                        currencySymbol = Convert.ToString(dr["CurrencySymbol"]);
                    }

                    if (dr["StdPrecision"] != DBNull.Value)
                    {
                        stdPrecision = Convert.ToInt32(dr["StdPrecision"]);
                    }
                }

                mainMetric = Math.Round(mainMetric, stdPrecision);
                avgDailyAmount = Math.Round(avgDailyAmount, stdPrecision);

                int deltaPercent = 0;
                if (avgDailyAmount > 0)
                {
                    deltaPercent = Convert.ToInt32(Math.Round(((mainMetric - avgDailyAmount) / avgDailyAmount) * 100, 0, MidpointRounding.AwayFromZero));
                }

                return Json(new
                {
                    success = true,
                    error = "",
                    title = GetMsg(ctx, "VAS_047_TodayCashIn", "Today cash in"),
                    mainMetric = mainMetric,
                    mainMetricText = mainMetric.ToString("F" + stdPrecision),
                    avgDailyAmount = avgDailyAmount,
                    deltaPercent = deltaPercent,
                    description = GetMsg(ctx, "VAS_047_TodayCashInDesc", "Total cash received in cash journal today"),
                    badgeText = GetMsg(ctx, "VAS_047_Today", "Today"),
                    noDataText = GetMsg(ctx, "VAS_047_NoCashInToday", "No cash in today"),
                    dateFrom = FormatDate(dateFrom),
                    dateTo = FormatDate(dateTo.AddDays(-1)),
                    cCurrencyId = currencyId,
                    currencyISO = currencyISO,
                    currencyISOCode = currencyISO,
                    currencySymbol = currencySymbol,
                    stdPrecision = stdPrecision,
                    recordCount = recordCount,
                    hasData = recordCount > 0
                }, JsonRequestBehavior.AllowGet);
            }
            catch
            {
                return Json(new
                {
                    success = false,
                    error = GetMsg(ctx, "VAS_047_TodayCashInLoadError", "Unable to load today cash in"),
                    hasData = false
                }, JsonRequestBehavior.AllowGet);
            }
            finally
            {
                if (dr != null)
                {
                    dr.Close();
                    dr.Dispose();
                }
            }
        }

        /// <summary>
        /// Builds database-specific half-open date range filter.
        /// </summary>
        /// <param name="columnName">Date column name with table alias.</param>
        /// <param name="dateFrom">Inclusive start date.</param>
        /// <param name="dateTo">Exclusive end date.</param>
        /// <returns>SQL date filter.</returns>
        private string GetDateFilter(string columnName, DateTime dateFrom, DateTime dateTo)
        {
            return @"
                AND " + columnName + @" >= " + ToSqlDate(dateFrom) + @"
                AND " + columnName + @" < " + ToSqlDate(dateTo) + @"
            ";
        }

        /// <summary>
        /// Formats a server-computed date as a typed database date literal.
        /// </summary>
        /// <param name="date">Date value.</param>
        /// <returns>Database-specific date literal.</returns>
        private string ToSqlDate(DateTime date)
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
        /// Returns a database-specific expression for grouping by calendar day.
        /// </summary>
        /// <param name="columnExpression">Date column expression.</param>
        /// <returns>Truncated date expression.</returns>
        private string TruncColumn(string columnExpression)
        {
            if (DB.IsOracle())
            {
                return "TRUNC(" + columnExpression + ")";
            }

            return "CAST(" + columnExpression + " AS DATE)";
        }

        /// <summary>
        /// Formats date using fixed SQL-safe format.
        /// </summary>
        /// <param name="date">Date value.</param>
        /// <returns>Formatted date text.</returns>
        private string FormatDate(DateTime date)
        {
            return date.ToString("yyyy-MM-dd");
        }

        /// <summary>
        /// Gets translated message with fallback text.
        /// </summary>
        /// <param name="ctx">Current context.</param>
        /// <param name="key">Message key.</param>
        /// <param name="fallback">Fallback text.</param>
        /// <returns>Translated or fallback message.</returns>
        private string GetMsg(Ctx ctx, string key, string fallback)
        {
            string msg = Msg.GetMsg(ctx, key);

            if (string.IsNullOrEmpty(msg) || msg == key)
            {
                return fallback;
            }

            return msg;
        }
    }
}
