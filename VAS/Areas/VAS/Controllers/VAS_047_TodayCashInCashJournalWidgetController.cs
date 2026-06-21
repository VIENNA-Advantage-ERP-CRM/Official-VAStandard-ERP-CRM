using System;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Web.Mvc;
using VAdvantage.Classes;
using VAdvantage.DataBase;
using VAdvantage.Model;
using VAdvantage.Utility;

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
                SqlQueryData queryData = BuildTodayCashInSql(ctx);

                dr = DB.ExecuteReader(queryData.Sql, queryData.Parameters, null);

                decimal mainMetric = 0;
                decimal avgDailyAmount = 0;
                int recordCount = 0;
                int currencyId = 0;
                string currencyISO = "";
                string currencySymbol = "";
                int stdPrecision = 2;
                DateTime? dateFrom = null;
                DateTime? dateTo = null;

                if (dr != null && dr.Read())
                {
                    stdPrecision = Util.GetValueOfInt(dr["StdPrecision"]);
                    mainMetric = GetRoundedDecimal(dr, "MainMetric", stdPrecision);
                    avgDailyAmount = GetRoundedDecimal(dr, "AvgDailyAmount", stdPrecision);
                    recordCount = Util.GetValueOfInt(dr["RecordCount"]);
                    currencyId = Util.GetValueOfInt(dr["C_Currency_ID"]);
                    currencyISO = Util.GetValueOfString(dr["CurrencyISO"]);
                    currencySymbol = Util.GetValueOfString(dr["CurrencySymbol"]);

                    if (dr["DateFrom"] != DBNull.Value)
                    {
                        dateFrom = Util.GetValueOfDateTime(dr["DateFrom"]);
                    }

                    if (dr["DateTo"] != DBNull.Value)
                    {
                        dateTo = Util.GetValueOfDateTime(dr["DateTo"]);
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
                    dateFrom = dateFrom.HasValue ? FormatDate(dateFrom.Value) : "",
                    dateTo = dateTo.HasValue ? FormatDate(dateTo.Value) : "",
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

        private SqlQueryData BuildTodayCashInSql(Ctx ctx)
        {
            string todayDateSql = GetTodayDateSql();
            string cashDateSql = GetDateOnlySql("CashData.StatementDate");

            string dateRangeSql = @"
DateRange AS
(
SELECT
" + todayDateSql + @" AS TodayDate,
CAST(" + todayDateSql + @" AS TIMESTAMP) AS TodayStart,
CAST(" + todayDateSql + @" + 1 AS TIMESTAMP) AS TodayEnd,
CAST(" + todayDateSql + @" - 7 AS TIMESTAMP) AS AverageStart,
CAST(" + todayDateSql + @" AS TIMESTAMP) AS AverageEnd
FROM AD_ClientInfo ClientInfo
WHERE ClientInfo.IsActive = 'Y'
AND ClientInfo.AD_Client_ID = @AD_Client_ID
)";

            string schemaCurrencySql = @"
SchemaCurrency AS
(
SELECT
ClientInfo.AD_Client_ID,
AcctSchema.C_Currency_ID AS C_Currency_ID,
Currency.StdPrecision,
TRIM(CAST(Currency.ISO_Code AS CHAR(255))) AS ISO_Code,
CASE WHEN Currency.CurSymbol IS NOT NULL THEN TRIM(CAST(Currency.CurSymbol AS CHAR(255))) ELSE TRIM(CAST(Currency.ISO_Code AS CHAR(255))) END AS Cur_Symbol
FROM AD_ClientInfo ClientInfo
INNER JOIN C_AcctSchema AcctSchema ON (ClientInfo.C_AcctSchema1_ID = AcctSchema.C_AcctSchema_ID)
INNER JOIN C_Currency Currency ON (AcctSchema.C_Currency_ID = Currency.C_Currency_ID)
WHERE ClientInfo.IsActive = 'Y'
AND ClientInfo.AD_Client_ID = @AD_Client_ID
)";

            string cashAccessSql = @"
SELECT
CashJournal.C_Cash_ID,
CashJournal.AD_Client_ID,
CashJournal.AD_Org_ID,
CashJournal.C_Currency_ID,
CashJournal.StatementDate
FROM C_Cash CashJournal
WHERE CashJournal.IsActive = 'Y'
AND CashJournal.DocStatus IN ('CO', 'CL')";

            /*
             * MRole Handling:
             * Apply MRole only on the main physical table C_Cash CashJournal.
             * Do not apply MRole on the final WITH query.
             * Do not apply MRole on CTE aliases.
             * Do not apply MRole on joined aliases.
             * Do not apply MRole on a query that already contains INNER JOIN,
             * because MRole parser can generate invalid aliases like Cash INNER.C_Cash_ID.
             */
            cashAccessSql = MRole.GetDefault(ctx).AddAccessSQL(
                cashAccessSql,
                "CashJournal",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            string cashAccessCteSql = @"
CashAccess AS
(
" + cashAccessSql + @"
)";

            string cashFilteredSql = @"
CashFiltered AS
(
SELECT
CashJournal.C_Cash_ID,
CashJournal.AD_Client_ID,
CashJournal.AD_Org_ID,
CashJournal.C_Currency_ID,
CashJournal.StatementDate,
CashLine.C_CashLine_ID,
CashLine.Amount
FROM CashAccess CashJournal
INNER JOIN C_CashLine CashLine ON (CashJournal.C_Cash_ID = CashLine.C_Cash_ID)
WHERE CashLine.IsActive = 'Y'
AND CashLine.Amount > 0
)";

            string todayDataSql = @"
TodayData AS
(
SELECT
ROUND(
COALESCE(
SUM(
CASE WHEN CashData.C_Currency_ID = SchemaCurrency.C_Currency_ID THEN COALESCE(CashData.Amount, 0) ELSE CurrencyConvert(COALESCE(CashData.Amount, 0), CashData.C_Currency_ID, SchemaCurrency.C_Currency_ID, CashData.StatementDate, 0, CashData.AD_Client_ID, CashData.AD_Org_ID) END
),
0
),
COALESCE(MAX(SchemaCurrency.StdPrecision), 2)
) AS MainMetric,
MAX(SchemaCurrency.C_Currency_ID) AS C_Currency_ID,
MAX(SchemaCurrency.ISO_Code) AS CurrencyISO,
MAX(SchemaCurrency.Cur_Symbol) AS CurrencySymbol,
COALESCE(MAX(SchemaCurrency.StdPrecision), 2) AS StdPrecision,
COUNT(CashData.C_CashLine_ID) AS RecordCount
FROM SchemaCurrency SchemaCurrency
INNER JOIN DateRange DateRange ON (1 = 1)
LEFT OUTER JOIN CashFiltered CashData ON (CashData.AD_Client_ID = SchemaCurrency.AD_Client_ID AND CAST(CashData.StatementDate AS TIMESTAMP) >= DateRange.TodayStart AND CAST(CashData.StatementDate AS TIMESTAMP) < DateRange.TodayEnd)
)";

            string dailyTotalsSql = @"
DailyTotals AS
(
SELECT
" + cashDateSql + @" AS CashDate,
SUM(
CASE WHEN CashData.C_Currency_ID = SchemaCurrency.C_Currency_ID THEN COALESCE(CashData.Amount, 0) ELSE CurrencyConvert(COALESCE(CashData.Amount, 0), CashData.C_Currency_ID, SchemaCurrency.C_Currency_ID, CashData.StatementDate, 0, CashData.AD_Client_ID, CashData.AD_Org_ID) END
) AS DailyAmount
FROM CashFiltered CashData
INNER JOIN SchemaCurrency SchemaCurrency ON (SchemaCurrency.AD_Client_ID = CashData.AD_Client_ID)
INNER JOIN DateRange DateRange ON (CAST(CashData.StatementDate AS TIMESTAMP) >= DateRange.AverageStart AND CAST(CashData.StatementDate AS TIMESTAMP) < DateRange.AverageEnd)
GROUP BY " + cashDateSql + @"
)";

            string averageDataSql = @"
AverageData AS
(
SELECT
COALESCE(AVG(DailyTotals.DailyAmount), 0) AS AvgDailyAmount
FROM DailyTotals DailyTotals
)";

            string sql = @"
WITH " + dateRangeSql + @",
" + schemaCurrencySql + @",
" + cashAccessCteSql + @",
" + cashFilteredSql + @",
" + todayDataSql + @",
" + dailyTotalsSql + @",
" + averageDataSql + @"
SELECT
TodayData.MainMetric,
TodayData.C_Currency_ID,
TodayData.CurrencyISO,
TodayData.CurrencySymbol,
TodayData.StdPrecision,
TodayData.RecordCount,
ROUND(AverageData.AvgDailyAmount, TodayData.StdPrecision) AS AvgDailyAmount,
DateRange.TodayDate AS DateFrom,
DateRange.TodayDate AS DateTo
FROM TodayData TodayData
INNER JOIN AverageData AverageData ON (1 = 1)
INNER JOIN DateRange DateRange ON (1 = 1)";

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
            };

            return new SqlQueryData
            {
                Sql = sql,
                Parameters = parameters
            };
        }

        private decimal GetRoundedDecimal(IDataReader reader, string columnName, int precision)
        {
            object value = reader[columnName];

            if (value == null || value == DBNull.Value)
            {
                return 0;
            }

            precision = Math.Max(0, Math.Min(precision, 28));

            decimal decimalValue;
            string invariantText = value.ToString();

            if (decimal.TryParse(invariantText, NumberStyles.Any, CultureInfo.InvariantCulture, out decimalValue)
                || decimal.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.CurrentCulture, out decimalValue))
            {
                return Math.Round(decimalValue, precision, MidpointRounding.AwayFromZero);
            }

            double doubleValue;

            if (double.TryParse(invariantText, NumberStyles.Any, CultureInfo.InvariantCulture, out doubleValue)
                && !double.IsNaN(doubleValue)
                && !double.IsInfinity(doubleValue))
            {
                return Convert.ToDecimal(
                    Math.Round(doubleValue, precision, MidpointRounding.AwayFromZero)
                );
            }

            return 0;
        }

        private string GetTodayDateSql()
        {
            if (DB.IsOracle())
            {
                return "TRUNC(CURRENT_DATE)";
            }

            return "CURRENT_DATE";
        }

        private string GetDateOnlySql(string columnName)
        {
            if (DB.IsOracle())
            {
                return "CAST(TRUNC(" + columnName + ") AS DATE)";
            }

            return "CAST(" + columnName + " AS DATE)";
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

            if (string.IsNullOrEmpty(msg) || msg == key || msg == "[" + key + "]")
            {
                return fallback;
            }

            return msg;
        }

        private class SqlQueryData
        {
            public string Sql { get; set; }
            public SqlParameter[] Parameters { get; set; }
        }
    }
}
