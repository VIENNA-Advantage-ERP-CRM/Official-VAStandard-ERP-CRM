using System;
using System.Collections.Generic;
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
    /// Module Name : Cash Journal
    /// Purpose     : Provides last 7 days cash flow values for Cash Journal dashboard widget.
    /// </summary>
    public class VAS_051_SevenDayCashFlowCashJournalController : Controller
    {
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetSevenDayCashFlow()
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
                DateTime dateFrom = today.AddDays(-6);
                DateTime dateTo = today.AddDays(1);

                string dateFilter = GetDateFilter("CashHeader.StatementDate", dateFrom, dateTo);

                string rawCashFlowSql = @"
                    SELECT CashHeader.StatementDate,
                           CashHeader.DateAcct,
                           CashHeader.AD_Client_ID,
                           CashHeader.AD_Org_ID,
                           CashBook.C_Currency_ID AS CashBookCurrency_ID,
                           CashLine.Amount
                    FROM C_CashLine CashLine
                    INNER JOIN C_Cash CashHeader
                        ON (CashLine.C_Cash_ID=CashHeader.C_Cash_ID)
                    INNER JOIN C_CashBook CashBook
                        ON (CashHeader.C_CashBook_ID=CashBook.C_CashBook_ID)
                    WHERE CashLine.IsActive='Y'
                    AND CashHeader.IsActive='Y'
                    AND CashBook.IsActive='Y'
                    AND CashHeader.DocStatus IN ('CO','CL')
                    " + dateFilter;

                rawCashFlowSql = MRole.GetDefault(ctx).AddAccessSQL(
                    rawCashFlowSql,
                    "CashLine",
                    MRole.SQL_FULLYQUALIFIED,
                    MRole.SQL_RO
                );

                string dayExpression = GetDayExpression("RawCashFlow.StatementDate");

                string convertedAmountExpression = @"
                    CASE
                        WHEN RawCashFlow.CashBookCurrency_ID = SchemaCurrency.C_Currency_ID THEN COALESCE(RawCashFlow.Amount, 0)
                        ELSE CurrencyConvert(
                            COALESCE(RawCashFlow.Amount, 0),
                            RawCashFlow.CashBookCurrency_ID,
                            SchemaCurrency.C_Currency_ID,
                            RawCashFlow.DateAcct,
                            0,
                            RawCashFlow.AD_Client_ID,
                            RawCashFlow.AD_Org_ID
                        )
                    END";

                string sql = @"
                    SELECT " + dayExpression + @" AS CashFlowDate,
                           ROUND(
                               COALESCE(SUM(
                                   CASE
                                       WHEN " + convertedAmountExpression + @" > 0 THEN " + convertedAmountExpression + @"
                                       ELSE 0
                                   END
                               ), 0),
                               COALESCE(MAX(SchemaCurrency.StdPrecision), 2)
                           ) AS CashInAmount,
                           ROUND(
                               COALESCE(SUM(
                                   CASE
                                       WHEN " + convertedAmountExpression + @" < 0 THEN 0 - (" + convertedAmountExpression + @")
                                       ELSE 0
                                   END
                               ), 0),
                               COALESCE(MAX(SchemaCurrency.StdPrecision), 2)
                           ) AS CashOutAmount,
                           ROUND(
                               COALESCE(SUM(" + convertedAmountExpression + @"), 0),
                               COALESCE(MAX(SchemaCurrency.StdPrecision), 2)
                           ) AS NetAmount,
                           COUNT(1) AS LineCount,
                           COALESCE(MAX(SchemaCurrency.StdPrecision), 2) AS StdPrecision,
                           MAX(SchemaCurrency.C_Currency_ID) AS C_Currency_ID,
                           MAX(SchemaCurrency.ISO_Code) AS CurrencyISO,
                           MAX(SchemaCurrency.Cur_Symbol) AS CurrencySymbol
                    FROM (" + rawCashFlowSql.Trim() + @") RawCashFlow
                    INNER JOIN (SELECT ClientInfo.AD_Client_ID,
                               AcctSchema.C_Currency_ID AS C_Currency_ID,
                               Curr.StdPrecision,
                               Curr.ISO_Code,
                               CASE
                                   WHEN Curr.CurSymbol IS NOT NULL THEN Curr.CurSymbol
                                   ELSE Curr.ISO_Code
                               END AS Cur_Symbol
                        FROM AD_ClientInfo ClientInfo
                        INNER JOIN C_AcctSchema AcctSchema
                            ON (ClientInfo.C_AcctSchema1_ID=AcctSchema.C_AcctSchema_ID)
                        INNER JOIN C_Currency Curr
                            ON (AcctSchema.C_Currency_ID=Curr.C_Currency_ID)
                    ) SchemaCurrency
                        ON (SchemaCurrency.AD_Client_ID=RawCashFlow.AD_Client_ID)
                    GROUP BY " + dayExpression + @"
                    ORDER BY " + dayExpression;

                Dictionary<string, CashFlowDay> dayMap = new Dictionary<string, CashFlowDay>();

                for (int index = 0; index < 7; index++)
                {
                    DateTime day = dateFrom.AddDays(index);
                    string key = FormatDate(day);

                    dayMap[key] = new CashFlowDay
                    {
                        CashFlowDate = key,
                        DayLabel = day.ToString("ddd"),
                        CashInAmount = 0,
                        CashOutAmount = 0,
                        NetAmount = 0,
                        LineCount = 0
                    };
                }

                int stdPrecision = 2;
                int currencyId = 0;
                string currencyISO = string.Empty;
                string currencySymbol = string.Empty;

                decimal totalCashIn = 0;
                decimal totalCashOut = 0;
                decimal totalNet = 0;
                int totalLineCount = 0;

                reader = DB.ExecuteReader(sql, null, null);

                while (reader.Read())
                {
                    string cashFlowDate = FormatDbDate(reader["CashFlowDate"]);

                    if (!dayMap.ContainsKey(cashFlowDate))
                    {
                        continue;
                    }

                    decimal cashInAmount = GetDecimal(reader, "CashInAmount");
                    decimal cashOutAmount = GetDecimal(reader, "CashOutAmount");
                    decimal netAmount = GetDecimal(reader, "NetAmount");
                    int lineCount = GetInt(reader, "LineCount");

                    stdPrecision = GetInt(reader, "StdPrecision", stdPrecision);
                    currencyId = GetInt(reader, "C_Currency_ID", currencyId);
                    currencyISO = GetString(reader, "CurrencyISO");
                    currencySymbol = GetString(reader, "CurrencySymbol");

                    dayMap[cashFlowDate].CashInAmount = cashInAmount;
                    dayMap[cashFlowDate].CashOutAmount = cashOutAmount;
                    dayMap[cashFlowDate].NetAmount = netAmount;
                    dayMap[cashFlowDate].LineCount = lineCount;

                    totalCashIn += cashInAmount;
                    totalCashOut += cashOutAmount;
                    totalNet += netAmount;
                    totalLineCount += lineCount;
                }

                List<object> items = new List<object>();

                for (int index = 0; index < 7; index++)
                {
                    DateTime day = dateFrom.AddDays(index);
                    string key = FormatDate(day);
                    CashFlowDay cashFlowDay = dayMap[key];

                    items.Add(new
                    {
                        date = cashFlowDay.CashFlowDate,
                        dayLabel = cashFlowDay.DayLabel,
                        cashInAmount = cashFlowDay.CashInAmount,
                        cashOutAmount = cashFlowDay.CashOutAmount,
                        netAmount = cashFlowDay.NetAmount,
                        lineCount = cashFlowDay.LineCount,
                        tooltip = cashFlowDay.DayLabel + " " + cashFlowDay.CashFlowDate
                            + " | In: " + cashFlowDay.CashInAmount.ToString()
                            + " | Out: " + cashFlowDay.CashOutAmount.ToString()
                            + " | Net: " + cashFlowDay.NetAmount.ToString()
                    });
                }

                return Json(new
                {
                    success = true,
                    error = string.Empty,
                    title = GetMsg(ctx, "VAS_051_SevenDayCashFlow", "7-Day Cash Flow"),
                    metaText = GetMsg(ctx, "VAS_051_Last7Days", "Last 7 days"),
                    dateFrom = FormatDate(dateFrom),
                    dateTo = FormatDate(today),
                    currencyISO = currencyISO,
                    currencySymbol = currencySymbol,
                    cCurrencyId = currencyId,
                    stdPrecision = stdPrecision,
                    items = items,
                    totalCashIn = totalCashIn,
                    totalCashOut = totalCashOut,
                    totalNet = totalNet,
                    hasData = totalLineCount > 0
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception)
            {
                return Json(new
                {
                    success = false,
                    error = GetMsg(ctx, "VAS_051_LoadError", "Unable to load seven day cash flow"),
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
                AND " + columnName + @" < " + ToSqlDate(dateTo);
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

        private string GetDayExpression(string columnName)
        {
            if (DB.IsOracle())
            {
                return "TRUNC(" + columnName + ")";
            }

            return "CAST(" + columnName + " AS DATE)";
        }

        private string FormatDate(DateTime date)
        {
            return date.ToString("yyyy-MM-dd");
        }

        private string FormatDbDate(object value)
        {
            if (value == null || value == DBNull.Value)
            {
                return string.Empty;
            }

            DateTime dateValue;

            if (DateTime.TryParse(value.ToString(), out dateValue))
            {
                return FormatDate(dateValue);
            }

            return string.Empty;
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

        private class CashFlowDay
        {
            public string CashFlowDate { get; set; }
            public string DayLabel { get; set; }
            public decimal CashInAmount { get; set; }
            public decimal CashOutAmount { get; set; }
            public decimal NetAmount { get; set; }
            public int LineCount { get; set; }
        }
    }
}
