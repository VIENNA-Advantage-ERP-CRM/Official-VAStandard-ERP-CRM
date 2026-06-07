using System;
using System.Data;
using System.Web.Mvc;
using VAdvantage.DataBase;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VAS.Controllers
{
    /// <summary>
    /// Module Name : Cash Journal
    /// Purpose     : Provides today's cash out and comparison with previous 7-day average.
    /// </summary>
    public class VAS_048_TodayCashOutCashJournalController : Controller
    {
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
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

                string todayFromSql = ToSqlDate(dateFrom);
                string todayToSql = ToSqlDate(dateTo);
                string sevenDayFromSql = ToSqlDate(sevenDayFrom);
                string sevenDayToSql = ToSqlDate(sevenDayTo);

                string filteredCashLineSql = @"
SELECT CashLine.C_CashLine_ID,
CashLine.C_Cash_ID,
CashLine.Amount
FROM C_CashLine CashLine
WHERE CashLine.IsActive='Y'
AND CashLine.Amount < 0";

                filteredCashLineSql = MRole.GetDefault(ctx).AddAccessSQL(
                    filteredCashLineSql,
                    "CashLine",
                    MRole.SQL_FULLYQUALIFIED,
                    MRole.SQL_RO
                );

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
INNER JOIN C_AcctSchema AcctSchema ON (ClientInfo.C_AcctSchema1_ID=AcctSchema.C_AcctSchema_ID)
INNER JOIN C_Currency Currency ON (AcctSchema.C_Currency_ID=Currency.C_Currency_ID)";

                string convertedAmountExpression = @"
CASE
WHEN CashBook.C_Currency_ID=SchemaCurrency.C_Currency_ID THEN COALESCE(FilteredCashLine.Amount,0)
ELSE CurrencyConvert(COALESCE(FilteredCashLine.Amount,0),CashBook.C_Currency_ID,SchemaCurrency.C_Currency_ID,CashHeader.DateAcct,0,CashHeader.AD_Client_ID,CashHeader.AD_Org_ID)
END";

                string todayCondition = @"
CashHeader.StatementDate >= " + todayFromSql + @"
AND CashHeader.StatementDate < " + todayToSql;

                string sevenDayCondition = @"
CashHeader.StatementDate >= " + sevenDayFromSql + @"
AND CashHeader.StatementDate < " + sevenDayToSql;

                string sql = @"
SELECT ROUND(COALESCE(SUM(CASE WHEN " + todayCondition + @" THEN 0 - (" + convertedAmountExpression + @") ELSE 0 END),0),COALESCE(MAX(SchemaCurrency.StdPrecision),2)) AS TodayAmount,
COUNT(CASE WHEN " + todayCondition + @" THEN FilteredCashLine.C_CashLine_ID ELSE NULL END) AS DisbursementCount,
ROUND(COALESCE(SUM(CASE WHEN " + sevenDayCondition + @" THEN 0 - (" + convertedAmountExpression + @") ELSE 0 END),0),COALESCE(MAX(SchemaCurrency.StdPrecision),2)) AS SevenDayAmount,
COUNT(CASE WHEN " + sevenDayCondition + @" THEN FilteredCashLine.C_CashLine_ID ELSE NULL END) AS SevenDayDisbursementCount,
COALESCE(MAX(SchemaCurrency.StdPrecision),2) AS StdPrecision,
MAX(SchemaCurrency.C_Currency_ID) AS C_Currency_ID,
MAX(SchemaCurrency.ISO_Code) AS CurrencyISO,
MAX(SchemaCurrency.Cur_Symbol) AS CurrencySymbol
FROM (" + filteredCashLineSql + @") FilteredCashLine
INNER JOIN C_Cash CashHeader ON (FilteredCashLine.C_Cash_ID=CashHeader.C_Cash_ID)
INNER JOIN C_CashBook CashBook ON (CashHeader.C_CashBook_ID=CashBook.C_CashBook_ID)
INNER JOIN (" + schemaCurrencySql + @") SchemaCurrency ON (SchemaCurrency.AD_Client_ID=CashHeader.AD_Client_ID)
WHERE CashHeader.IsActive='Y'
AND CashBook.IsActive='Y'
AND CashHeader.DocStatus IN ('CO','CL')
AND CashHeader.StatementDate >= " + sevenDayFromSql + @"
AND CashHeader.StatementDate < " + todayToSql;

                decimal todayAmount = 0;
                decimal sevenDayAmount = 0;
                decimal sevenDayAverage = 0;
                int disbursementCount = 0;
                int sevenDayDisbursementCount = 0;
                int stdPrecision = 2;
                int currencyId = 0;
                string currencyISO = string.Empty;
                string currencySymbol = string.Empty;

                reader = DB.ExecuteReader(sql, null, null);

                if (reader.Read())
                {
                    todayAmount = GetDecimal(reader, "TodayAmount");
                    sevenDayAmount = GetDecimal(reader, "SevenDayAmount");
                    disbursementCount = GetInt(reader, "DisbursementCount");
                    sevenDayDisbursementCount = GetInt(reader, "SevenDayDisbursementCount");
                    stdPrecision = GetInt(reader, "StdPrecision", 2);
                    currencyId = GetInt(reader, "C_Currency_ID");
                    currencyISO = GetString(reader, "CurrencyISO");
                    currencySymbol = GetString(reader, "CurrencySymbol");
                }

                sevenDayAverage = Math.Round(sevenDayAmount / 7, stdPrecision);

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
                        title = GetMsg(ctx, "VAS_048_CashOut", "Cash out"),
                        mainMetric = 0,
                        mainMetricText = "0",
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
                        sevenDayDisbursementCount = sevenDayDisbursementCount
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
                    sevenDayDisbursementCount = sevenDayDisbursementCount,
                    hasData = true
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception)
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

        private string ToSqlDate(DateTime date)
        {
            string dateText = FormatDate(date);

            if (DB.IsOracle())
            {
                return "TO_DATE('" + dateText + "','YYYY-MM-DD')";
            }

            return "DATE '" + dateText + "'";
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