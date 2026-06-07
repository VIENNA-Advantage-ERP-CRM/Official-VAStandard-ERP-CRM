using System;
using System.Collections.Generic;
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
    /// Purpose     : Provides last seven days cash in vs cash out values for Cash Journal dashboard widget.
    /// Chronological development:
    ///   VAS   Created 2026-06-06
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
                string dateExpression = GetDateOnlyExpression("CashHeader.StatementDate");
                string dateFilter = GetDateFilter("CashHeader.StatementDate", dateFrom, dateTo);

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

                string dailyCashSql = @"
                    SELECT " + dateExpression + @" AS FlowDate,
                           ROUND(
                               COALESCE(SUM(CASE WHEN CashLine.Amount > 0 THEN " + convertedAmountExpression + @" ELSE 0 END), 0),
                               COALESCE(MAX(SchemaCurrency.StdPrecision), 2)
                           ) AS CashInAmount,
                           ROUND(
                               COALESCE(SUM(CASE WHEN CashLine.Amount < 0 THEN 0 - (" + convertedAmountExpression + @") ELSE 0 END), 0),
                               COALESCE(MAX(SchemaCurrency.StdPrecision), 2)
                           ) AS CashOutAmount,
                           COUNT(CashLine.C_CashLine_ID) AS LineCount,
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
                    AND CashHeader.DocStatus IN ('CO','CL')"
                    + dateFilter + @"
                    GROUP BY " + dateExpression;

                dailyCashSql = MRole.GetDefault(ctx).AddAccessSQL(dailyCashSql, "CashLine", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                string sql = @"
                    WITH SchemaCurrency AS (
                        " + schemaCurrencySql + @"
                    ),
                    DailyCash AS (
                        " + dailyCashSql + @"
                    )
                    SELECT DailyCash.FlowDate,
                           DailyCash.CashInAmount,
                           DailyCash.CashOutAmount,
                           DailyCash.LineCount,
                           DailyCash.StdPrecision,
                           DailyCash.C_Currency_ID,
                           DailyCash.CurrencyISO,
                           DailyCash.CurrencySymbol
                    FROM DailyCash DailyCash
                    ORDER BY DailyCash.FlowDate";

                Dictionary<string, DayCashFlow> byDate = new Dictionary<string, DayCashFlow>();
                int stdPrecision = 2;
                int currencyId = 0;
                string currencyISO = string.Empty;
                string currencySymbol = string.Empty;
                int totalLineCount = 0;

                reader = DB.ExecuteReader(sql, null, null);

                while (reader.Read())
                {
                    DateTime flowDate = GetDate(reader, "FlowDate");
                    string dateKey = FormatDate(flowDate);
                    decimal cashInAmount = GetDecimal(reader, "CashInAmount");
                    decimal cashOutAmount = GetDecimal(reader, "CashOutAmount");
                    int lineCount = GetInt(reader, "LineCount");

                    stdPrecision = GetInt(reader, "StdPrecision", stdPrecision);
                    currencyId = GetInt(reader, "C_Currency_ID", currencyId);
                    currencyISO = GetString(reader, "CurrencyISO");
                    currencySymbol = GetString(reader, "CurrencySymbol");
                    totalLineCount += lineCount;

                    byDate[dateKey] = new DayCashFlow
                    {
                        Date = flowDate,
                        CashInAmount = cashInAmount,
                        CashOutAmount = cashOutAmount,
                        LineCount = lineCount
                    };
                }

                List<object> days = new List<object>();

                for (int index = 0; index < 7; index++)
                {
                    DateTime day = dateFrom.AddDays(index);
                    string dateKey = FormatDate(day);
                    DayCashFlow flow = byDate.ContainsKey(dateKey) ? byDate[dateKey] : new DayCashFlow { Date = day };

                    days.Add(new
                    {
                        date = dateKey,
                        dayLabel = day.ToString("ddd").ToUpperInvariant(),
                        cashInAmount = flow.CashInAmount,
                        cashOutAmount = flow.CashOutAmount,
                        lineCount = flow.LineCount
                    });
                }

                return Json(new
                {
                    success = true,
                    error = string.Empty,
                    title = GetMsg(ctx, "VAS_051_SevenDayCashFlow", "7-Day Cash Flow"),
                    metaText = GetMsg(ctx, "VAS_051_InVsOut", "In vs Out"),
                    inLabel = GetMsg(ctx, "VAS_051_In", "In"),
                    outLabel = GetMsg(ctx, "VAS_051_Out", "Out"),
                    dateFrom = FormatDate(dateFrom),
                    dateTo = FormatDate(today),
                    currencyISO = currencyISO,
                    currencySymbol = currencySymbol,
                    cCurrencyId = currencyId,
                    stdPrecision = stdPrecision,
                    days = days,
                    hasData = totalLineCount > 0
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception)
            {
                return Json(new
                {
                    success = false,
                    error = GetMsg(ctx, "VAS_051_LoadError", "Unable to load 7-day cash flow"),
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

        private string GetDateOnlyExpression(string columnName)
        {
            if (DB.IsOracle())
            {
                return "TRUNC(" + columnName + ")";
            }

            return "CAST(" + columnName + " AS DATE)";
        }

        private string GetDateFilter(string columnName, DateTime dateFrom, DateTime dateTo)
        {
            string dateFromText = FormatDate(dateFrom);
            string dateToText = FormatDate(dateTo);

            if (DB.IsOracle())
            {
                return @"
                    AND " + columnName + @" >= TO_DATE('" + dateFromText + @"', 'YYYY-MM-DD')
                    AND " + columnName + @" < TO_DATE('" + dateToText + @"', 'YYYY-MM-DD')
                ";
            }

            return @"
                AND " + columnName + @" >= DATE '" + dateFromText + @"'
                AND " + columnName + @" < DATE '" + dateToText + @"'
            ";
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

        private DateTime GetDate(IDataReader reader, string columnName)
        {
            object value = reader[columnName];

            if (value == null || value == DBNull.Value)
            {
                return DateTime.Today;
            }

            DateTime result;
            return DateTime.TryParse(value.ToString(), out result) ? result.Date : DateTime.Today;
        }

        private class DayCashFlow
        {
            public DateTime Date { get; set; }
            public decimal CashInAmount { get; set; }
            public decimal CashOutAmount { get; set; }
            public int LineCount { get; set; }
        }
    }
}
