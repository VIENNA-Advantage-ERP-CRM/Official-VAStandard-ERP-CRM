using System;
using System.Data;
using System.Globalization;
using System.Web.Mvc;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VAS.Controllers
{
    public class VAS_048_TodayCashOutCashJournalController : Controller
    {
       
        public JsonResult GetTodayCashOut()
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

            IDataReader reader = null;

            try
            {
                DateTime today = DateTime.Today;
                DateTime dateFrom = today;
                DateTime dateTo = today.AddDays(1);
                DateTime sevenDayFrom = today.AddDays(-7);
                DateTime sevenDayTo = today;

                string todayDateFilter = GetDateFilter("CashHeader.StatementDate", dateFrom, dateTo);
                string sevenDayDateFilter = GetDateFilter("CashHeader.StatementDate", sevenDayFrom, sevenDayTo);

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
                        ON (ClientInfo.C_AcctSchema1_ID=AcctSchema.C_AcctSchema_ID)
                    INNER JOIN C_Currency Currency
                        ON (AcctSchema.C_Currency_ID=Currency.C_Currency_ID)";

                string convertedAmountExpression = @"
                    CASE
                        WHEN CashHeader.C_Currency_ID=SchemaCurrency.C_Currency_ID THEN COALESCE(CashLine.Amount, 0)
                        ELSE CurrencyConvert(
                            COALESCE(CashLine.Amount, 0),
                            CashHeader.C_Currency_ID,
                            SchemaCurrency.C_Currency_ID,
                            CashHeader.DateAcct,
                            0,
                            CashHeader.AD_Client_ID,
                            CashHeader.AD_Org_ID
                        )
                    END";

                string todayCashSql = @"
                    SELECT ROUND(
                               COALESCE(SUM(0 - (" + convertedAmountExpression + @")), 0),
                               COALESCE(MAX(SchemaCurrency.StdPrecision), 2)
                           ) AS TodayAmount,
                           COUNT(CashLine.C_CashLine_ID) AS DisbursementCount,
                           COALESCE(MAX(SchemaCurrency.StdPrecision), 2) AS StdPrecision,
                           MAX(SchemaCurrency.C_Currency_ID) AS C_Currency_ID,
                           MAX(SchemaCurrency.ISO_Code) AS CurrencyISO,
                           MAX(SchemaCurrency.Cur_Symbol) AS CurrencySymbol
                    FROM C_CashLine CashLine
                    INNER JOIN C_Cash CashHeader
                        ON (CashLine.C_Cash_ID=CashHeader.C_Cash_ID)
                    INNER JOIN SchemaCurrency SchemaCurrency
                        ON (SchemaCurrency.AD_Client_ID=CashHeader.AD_Client_ID)
                    WHERE CashLine.IsActive='Y'
                    AND CashHeader.IsActive='Y'
                    AND CashHeader.DocStatus IN ('CO','CL')
                    AND CashLine.Amount < 0"
                    + todayDateFilter;

                todayCashSql = MRole.GetDefault(ctx).AddAccessSQL(todayCashSql, "CashLine", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                string sevenDayCashSql = @"
                    SELECT ROUND(
                               COALESCE(SUM(0 - (" + convertedAmountExpression + @")), 0),
                               COALESCE(MAX(SchemaCurrency.StdPrecision), 2)
                           ) AS SevenDayAmount,
                           COUNT(CashLine.C_CashLine_ID) AS SevenDayDisbursementCount,
                           COALESCE(MAX(SchemaCurrency.StdPrecision), 2) AS StdPrecision
                    FROM C_CashLine CashLine
                    INNER JOIN C_Cash CashHeader
                        ON (CashLine.C_Cash_ID=CashHeader.C_Cash_ID)
                    INNER JOIN SchemaCurrency SchemaCurrency
                        ON (SchemaCurrency.AD_Client_ID=CashHeader.AD_Client_ID)
                    WHERE CashLine.IsActive='Y'
                    AND CashHeader.IsActive='Y'
                    AND CashHeader.DocStatus IN ('CO','CL')
                    AND CashLine.Amount < 0"
                    + sevenDayDateFilter;

                sevenDayCashSql = MRole.GetDefault(ctx).AddAccessSQL(sevenDayCashSql, "CashLine", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                string sql = @"
                    WITH SchemaCurrency AS (
                        " + schemaCurrencySql + @"
                    ),
                    TodayCash AS (
                        " + todayCashSql + @"
                    ),
                    SevenDayCash AS (
                        " + sevenDayCashSql + @"
                    )
                    SELECT TodayCash.TodayAmount,
                           TodayCash.DisbursementCount,
                           ROUND(
                               COALESCE(SevenDayCash.SevenDayAmount, 0) / 7,
                               COALESCE(TodayCash.StdPrecision, SevenDayCash.StdPrecision, 2)
                           ) AS SevenDayAverage,
                           TodayCash.StdPrecision,
                           TodayCash.C_Currency_ID,
                           TodayCash.CurrencyISO,
                           TodayCash.CurrencySymbol
                    FROM TodayCash TodayCash
                    INNER JOIN SevenDayCash SevenDayCash
                        ON (1=1)";

                decimal todayAmount = 0;
                decimal sevenDayAverage = 0;
                int disbursementCount = 0;
                int stdPrecision = 2;
                int currencyId = 0;
                string currencyISO = string.Empty;
                string currencySymbol = string.Empty;

                reader = DB.ExecuteReader(sql, null, null);

                if (reader.Read())
                {
                    todayAmount = GetDecimal(reader, "TodayAmount");
                    sevenDayAverage = GetDecimal(reader, "SevenDayAverage");
                    disbursementCount = GetInt(reader, "DisbursementCount");
                    stdPrecision = GetInt(reader, "StdPrecision", 2);
                    currencyId = GetInt(reader, "C_Currency_ID");
                    currencyISO = GetString(reader, "CurrencyISO");
                    currencySymbol = GetString(reader, "CurrencySymbol");
                }

                decimal deltaPercent = 0;

                if (sevenDayAverage != 0)
                {
                    deltaPercent = Math.Round(((todayAmount - sevenDayAverage) / sevenDayAverage) * 100, 0);
                }

                bool hasData = disbursementCount > 0;

                if (!hasData)
                {
                    return Json(new
                    {
                        success = true,
                        error = string.Empty,
                        hasData = false,
                        mainMetric = 0,
                        mainMetricText = "0",
                        description = string.Empty,
                        badgeText = GetMsg(ctx, "VAS_048_Today", "Today"),
                        dateFrom = FormatDate(dateFrom),
                        dateTo = FormatDate(dateTo.AddDays(-1))
                    }, JsonRequestBehavior.AllowGet);
                }

                return Json(new
                {
                    success = true,
                    error = string.Empty,
                    title = GetMsg(ctx, "VAS_048_CashOut", "Cash out"),
                    mainMetric = todayAmount,
                    mainMetricText = todayAmount.ToString(),
                    description = GetMsg(ctx, "VAS_048_VsSevenDayAvg", "vs 7-day avg"),
                    badgeText = GetMsg(ctx, "VAS_048_Today", "Today"),
                    dateFrom = FormatDate(dateFrom),
                    dateTo = FormatDate(dateTo.AddDays(-1)),
                    currencyISO = currencyISO,
                    currencySymbol = currencySymbol,
                    cCurrencyId = currencyId,
                    stdPrecision = stdPrecision,
                    deltaPercent = deltaPercent,
                    sevenDayAverage = sevenDayAverage,
                    disbursementCount = disbursementCount,
                    hasData = true
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                 

                return Json(new
                {
                    success = false,
                    error = GetMsg(ctx, "VAS_048_LoadError", "Unable to load cash out"),
                    hasData = false
                }, JsonRequestBehavior.AllowGet);
            }
            finally
            {
                if (reader != null)
                {
                    reader.Close();
                    reader.Dispose();
                }
            }
        }

        private string GetDateFilter(string columnName, DateTime dateFrom, DateTime dateTo)
        {
            return @"
                AND " + columnName + @" >= " + ToSqlDate(dateFrom) + @"
                AND " + columnName + @" < " + ToSqlDate(dateTo) + @"
            ";
        }

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

        private string FormatDate(DateTime date)
        {
            return date.ToString("yyyy-MM-dd");
        }

        private string GetMsg(Ctx ctx, string key, string fallback)
        {
            string msg = Msg.GetMsg(ctx, key);

            if (string.IsNullOrEmpty(msg) || msg == key || msg == "[" + key + "]")
            {
                return fallback;
            }

            return msg;
        }

        private decimal GetDecimal(IDataReader reader, string columnName)
        {
            object value = reader[columnName];

            if (value == null || value == DBNull.Value)
            {
                return 0;
            }

            decimal result;
            return decimal.TryParse(value.ToString(), out result) ? result : 0;
        }

        private int GetInt(IDataReader reader, string columnName, int fallback = 0)
        {
            object value = reader[columnName];

            if (value == null || value == DBNull.Value)
            {
                return fallback;
            }

            int result;
            return int.TryParse(value.ToString(), out result) ? result : fallback;
        }

        private string GetString(IDataReader reader, string columnName)
        {
            object value = reader[columnName];

            if (value == null || value == DBNull.Value)
            {
                return string.Empty;
            }

            return value.ToString();
        }
    }
}
