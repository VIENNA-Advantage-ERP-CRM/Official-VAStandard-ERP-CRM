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
    /// Purpose     : Provides today's net cash KPI for Cash Journal dashboard widget.
    /// </summary>
    public class VAS_049_NetCashForCashJournalWidgetController : Controller
    {
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetNetCashForCashJournal()
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
                DateTime previousDateFrom = today.AddDays(-1);
                DateTime previousDateTo = today;

                string dateFromSql = ToSqlDate(dateFrom);
                string dateToSql = ToSqlDate(dateTo);
                string previousDateFromSql = ToSqlDate(previousDateFrom);
                string previousDateToSql = ToSqlDate(previousDateTo);

                string filteredCashLineSql = @"
SELECT CashLine.C_CashLine_ID,
CashLine.C_Cash_ID,
CashLine.Amount
FROM C_CashLine CashLine
WHERE CashLine.IsActive='Y'";

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

                string todayCondition = "CashRows.StatementDate >= " + dateFromSql + " AND CashRows.StatementDate < " + dateToSql;
                string previousCondition = "CashRows.StatementDate >= " + previousDateFromSql + " AND CashRows.StatementDate < " + previousDateToSql;

                string sql = @"
SELECT ROUND(COALESCE(SUM(CASE WHEN " + todayCondition + @" THEN CashRows.ConvertedAmount ELSE 0 END),0),COALESCE(MAX(CashRows.StdPrecision),2)) AS NetAmount,
ROUND(COALESCE(SUM(CASE WHEN " + todayCondition + @" AND CashRows.ConvertedAmount > 0 THEN CashRows.ConvertedAmount ELSE 0 END),0),COALESCE(MAX(CashRows.StdPrecision),2)) AS CashInAmount,
ROUND(COALESCE(SUM(CASE WHEN " + todayCondition + @" AND CashRows.ConvertedAmount < 0 THEN 0 - CashRows.ConvertedAmount ELSE 0 END),0),COALESCE(MAX(CashRows.StdPrecision),2)) AS CashOutAmount,
COUNT(CASE WHEN " + todayCondition + @" THEN CashRows.C_CashLine_ID ELSE NULL END) AS LineCount,
ROUND(COALESCE(SUM(CASE WHEN " + previousCondition + @" THEN CashRows.ConvertedAmount ELSE 0 END),0),COALESCE(MAX(CashRows.StdPrecision),2)) AS PreviousNetAmount,
COALESCE(MAX(CashRows.StdPrecision),2) AS StdPrecision,
MAX(CashRows.C_Currency_ID) AS C_Currency_ID,
MAX(CashRows.CurrencyISO) AS CurrencyISO,
MAX(CashRows.CurrencySymbol) AS CurrencySymbol
FROM (SELECT FilteredCashLine.C_CashLine_ID,
CashHeader.StatementDate,
" + convertedAmountExpression + @" AS ConvertedAmount,
SchemaCurrency.StdPrecision,
SchemaCurrency.C_Currency_ID,
SchemaCurrency.ISO_Code AS CurrencyISO,
SchemaCurrency.Cur_Symbol AS CurrencySymbol
FROM (" + filteredCashLineSql + @") FilteredCashLine
INNER JOIN C_Cash CashHeader ON (FilteredCashLine.C_Cash_ID=CashHeader.C_Cash_ID)
INNER JOIN C_CashBook CashBook ON (CashHeader.C_CashBook_ID=CashBook.C_CashBook_ID)
INNER JOIN (" + schemaCurrencySql + @") SchemaCurrency ON (SchemaCurrency.AD_Client_ID=CashHeader.AD_Client_ID)
WHERE CashHeader.IsActive='Y'
AND CashBook.IsActive='Y'
AND CashHeader.DocStatus IN ('CO','CL')
AND CashHeader.StatementDate >= " + previousDateFromSql + @"
AND CashHeader.StatementDate < " + dateToSql + @") CashRows";

                decimal netAmount = 0;
                decimal cashInAmount = 0;
                decimal cashOutAmount = 0;
                decimal previousNetAmount = 0;
                int lineCount = 0;
                int stdPrecision = 2;
                int currencyId = 0;
                string currencyISO = string.Empty;
                string currencySymbol = string.Empty;

                reader = DB.ExecuteReader(sql, null, null);

                if (reader.Read())
                {
                    netAmount = GetDecimal(reader, "NetAmount");
                    cashInAmount = GetDecimal(reader, "CashInAmount");
                    cashOutAmount = GetDecimal(reader, "CashOutAmount");
                    previousNetAmount = GetDecimal(reader, "PreviousNetAmount");
                    lineCount = GetInt(reader, "LineCount");
                    stdPrecision = GetInt(reader, "StdPrecision", 2);
                    currencyId = GetInt(reader, "C_Currency_ID");
                    currencyISO = GetString(reader, "CurrencyISO");
                    currencySymbol = GetString(reader, "CurrencySymbol");
                }

                decimal deltaAmount = netAmount - previousNetAmount;
                bool hasData = lineCount > 0;

                if (!hasData)
                {
                    return Json(new
                    {
                        success = true,
                        error = string.Empty,
                        hasData = false,
                        title = GetMsg(ctx, "VAS_049_NetCash", "Net cash"),
                        mainMetric = 0,
                        mainMetricText = "0",
                        description = string.Empty,
                        badgeText = GetMsg(ctx, "VAS_049_Today", "Today"),
                        dateFrom = FormatDate(dateFrom),
                        dateTo = FormatDate(dateTo.AddDays(-1)),
                        currencyISO = currencyISO,
                        currencySymbol = currencySymbol,
                        cCurrencyId = currencyId,
                        stdPrecision = stdPrecision,
                        deltaAmount = deltaAmount,
                        cashInAmount = cashInAmount,
                        cashOutAmount = cashOutAmount,
                        previousNetAmount = previousNetAmount
                    }, JsonRequestBehavior.AllowGet);
                }

                return Json(new
                {
                    success = true,
                    error = string.Empty,
                    title = GetMsg(ctx, "VAS_049_NetCash", "Net cash"),
                    mainMetric = netAmount,
                    mainMetricText = netAmount.ToString(),
                    description = GetNetCashDescription(ctx, netAmount),
                    badgeText = GetMsg(ctx, "VAS_049_Today", "Today"),
                    dateFrom = FormatDate(dateFrom),
                    dateTo = FormatDate(dateTo.AddDays(-1)),
                    currencyISO = currencyISO,
                    currencySymbol = currencySymbol,
                    cCurrencyId = currencyId,
                    stdPrecision = stdPrecision,
                    deltaAmount = deltaAmount,
                    cashInAmount = cashInAmount,
                    cashOutAmount = cashOutAmount,
                    previousNetAmount = previousNetAmount,
                    hasData = true
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception)
            {
                return Json(new
                {
                    success = false,
                    error = GetMsg(ctx, "VAS_049_LoadError", "Unable to load net cash"),
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
            string dateText = FormatDate(date.Date);

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

        private string GetNetCashDescription(Ctx ctx, decimal netAmount)
        {
            if (netAmount > 0)
            {
                return GetMsg(ctx, "VAS_049_PositiveCashDay", "Positive cash day");
            }

            if (netAmount < 0)
            {
                return GetMsg(ctx, "VAS_049_NegativeCashDay", "Negative cash day");
            }

            return GetMsg(ctx, "VAS_049_NeutralCashDay", "Neutral cash day");
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