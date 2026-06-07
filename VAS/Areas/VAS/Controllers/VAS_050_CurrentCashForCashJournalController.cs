using System;
using System.Collections.Generic;
using System.Data;
using System.Web.Mvc;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VAS.Controllers
{
    /// <summary>
    /// Module Name : Cash Journal
    /// Purpose     : Provides latest cash book ending balance for Current Cash dashboard widget.
    /// Chronological development:
    ///   VAS   Created 2026-06-06
    /// </summary>
    public class VAS_050_CurrentCashForCashJournalController : Controller
    {
       
        /// <summary>
        /// Gets latest ending balance for selected Cash Book / Drawer.
        /// </summary>
        /// <param name="cashBookId">Selected C_CashBook_ID. Pass 0 to use first accessible drawer with latest cash journal.</param>
        /// <returns>JSON response for Current Cash widget.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetCurrentCash(int cashBookId = 0)
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

                string cashBookFilter = string.Empty;

                if (cashBookId > 0)
                {
                    cashBookFilter = @"
                    AND CashBook.C_CashBook_ID=" + cashBookId;
                }

                string convertedEndingBalanceExpression = @"
                    CASE
                        WHEN CashBook.C_Currency_ID=SchemaCurrency.C_Currency_ID THEN COALESCE(CashHeader.EndingBalance, 0)
                        ELSE CurrencyConvert(
                            COALESCE(CashHeader.EndingBalance, 0),
                            CashBook.C_Currency_ID,
                            SchemaCurrency.C_Currency_ID,
                            CashHeader.DateAcct,
                            0,
                            CashHeader.AD_Client_ID,
                            CashHeader.AD_Org_ID
                        )
                    END";

                string rankedCashSql = @"
                    SELECT CashHeader.C_Cash_ID,
                           CashHeader.C_CashBook_ID,
                           CashBook.Name AS CashBookName,
                           CashHeader.DocumentNo,
                           CashHeader.StatementDate,
                           CashHeader.DateAcct,
                           ROUND(
                               " + convertedEndingBalanceExpression + @",
                               COALESCE(SchemaCurrency.StdPrecision, 2)
                           ) AS CurrentBalance,
                           COALESCE(SchemaCurrency.StdPrecision, 2) AS StdPrecision,
                           SchemaCurrency.C_Currency_ID AS C_Currency_ID,
                           SchemaCurrency.ISO_Code AS CurrencyISO,
                           SchemaCurrency.Cur_Symbol AS CurrencySymbol,
                           ROW_NUMBER() OVER (
                               PARTITION BY CashHeader.C_CashBook_ID
                               ORDER BY CashHeader.StatementDate DESC, CashHeader.C_Cash_ID DESC
                           ) AS RowNo
                    FROM C_Cash CashHeader
                    INNER JOIN C_CashBook CashBook
                        ON (CashHeader.C_CashBook_ID=CashBook.C_CashBook_ID)
                    INNER JOIN SchemaCurrency SchemaCurrency
                        ON (SchemaCurrency.AD_Client_ID=CashHeader.AD_Client_ID)
                    WHERE CashHeader.IsActive='Y'
                    AND CashBook.IsActive='Y'"
                    + cashBookFilter;

                rankedCashSql = MRole.GetDefault(ctx).AddAccessSQL(rankedCashSql, "CashHeader", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                string sql = @"
                    WITH SchemaCurrency AS (
                        " + schemaCurrencySql + @"
                    ),
                    RankedCash AS (
                        " + rankedCashSql + @"
                    )
                    SELECT RankedCash.C_Cash_ID,
                           RankedCash.C_CashBook_ID,
                           RankedCash.CashBookName,
                           RankedCash.DocumentNo,
                           RankedCash.StatementDate,
                           RankedCash.DateAcct,
                           RankedCash.CurrentBalance,
                           RankedCash.StdPrecision,
                           RankedCash.C_Currency_ID,
                           RankedCash.CurrencyISO,
                           RankedCash.CurrencySymbol
                    FROM RankedCash RankedCash
                    WHERE RankedCash.RowNo=1
                    ORDER BY RankedCash.CashBookName";

                int selectedCashBookId = 0;
                int cashId = 0;
                int stdPrecision = 2;
                int currencyId = 0;
                decimal currentBalance = 0;
                string cashBookName = string.Empty;
                string documentNo = string.Empty;
                string statementDate = string.Empty;
                string currencyISO = string.Empty;
                string currencySymbol = string.Empty;
                List<object> cashBooks = new List<object>();

                reader = DB.ExecuteReader(sql, null, null);

                bool selectedRowRead = false;

                while (reader.Read())
                {
                    int rowCashBookId = GetInt(reader, "C_CashBook_ID");
                    string rowCashBookName = GetString(reader, "CashBookName");

                    cashBooks.Add(new
                    {
                        cCashBookId = rowCashBookId,
                        name = rowCashBookName
                    });

                    if (!selectedRowRead)
                    {
                        selectedRowRead = true;
                        selectedCashBookId = rowCashBookId;
                        cashId = GetInt(reader, "C_Cash_ID");
                        cashBookName = rowCashBookName;
                        documentNo = GetString(reader, "DocumentNo");
                        statementDate = FormatDbDate(reader["StatementDate"]);
                        currentBalance = GetDecimal(reader, "CurrentBalance");
                        stdPrecision = GetInt(reader, "StdPrecision", 2);
                        currencyId = GetInt(reader, "C_Currency_ID");
                        currencyISO = GetString(reader, "CurrencyISO");
                        currencySymbol = GetString(reader, "CurrencySymbol");
                    }
                }

                if (!selectedRowRead)
                {
                    return Json(new
                    {
                        success = true,
                        error = string.Empty,
                        hasData = false,
                        mainMetric = 0,
                        mainMetricText = "0",
                        description = string.Empty,
                        badgeText = GetMsg(ctx, "VAS_050_Live", "Live"),
                        cashBooks = cashBooks
                    }, JsonRequestBehavior.AllowGet);
                }

                decimal footerAmount = Math.Abs(currentBalance);

                return Json(new
                {
                    success = true,
                    error = string.Empty,
                    title = GetMsg(ctx, "VAS_050_CurrentCash", "Current cash"),
                    mainMetric = currentBalance,
                    mainMetricText = currentBalance.ToString(),
                    footerAmount = footerAmount,
                    description = GetCurrentCashDescription(ctx, currentBalance),
                    badgeText = GetMsg(ctx, "VAS_050_Live", "Live"),
                    cCashId = cashId,
                    cCashBookId = selectedCashBookId,
                    cashBookName = cashBookName,
                    documentNo = documentNo,
                    statementDate = statementDate,
                    currencyISO = currencyISO,
                    currencySymbol = currencySymbol,
                    cCurrencyId = currencyId,
                    stdPrecision = stdPrecision,
                    cashBooks = cashBooks,
                    hasData = true
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
             
                return Json(new
                {
                    success = false,
                    error = GetMsg(ctx, "VAS_050_LoadError", "Unable to load current cash"),
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

        /// <summary>
        /// Builds database-specific half-open date filter.
        /// </summary>
        /// <param name="columnName">Hardcoded date column name.</param>
        /// <param name="dateFrom">Start date inclusive.</param>
        /// <param name="dateTo">End date exclusive.</param>
        /// <returns>SQL date filter text.</returns>
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

        /// <summary>
        /// Formats date using fixed yyyy-MM-dd pattern.
        /// </summary>
        /// <param name="date">Date value.</param>
        /// <returns>Formatted date text.</returns>
        private string FormatDate(DateTime date)
        {
            return date.ToString("yyyy-MM-dd");
        }

        /// <summary>
        /// Gets translated message with fallback.
        /// </summary>
        /// <param name="ctx">VIS context.</param>
        /// <param name="key">Message key.</param>
        /// <param name="fallback">Fallback text.</param>
        /// <returns>Translated message or fallback.</returns>
        private string GetMsg(Ctx ctx, string key, string fallback)
        {
            string msg = Msg.GetMsg(ctx, key);

            if (string.IsNullOrEmpty(msg) || msg == key || msg == "[" + key + "]")
            {
                return fallback;
            }

            return msg;
        }

        /// <summary>
        /// Gets status message for current cash balance.
        /// </summary>
        /// <param name="ctx">VIS context.</param>
        /// <param name="currentBalance">Latest cash ending balance.</param>
        /// <returns>Translated balance status text.</returns>
        private string GetCurrentCashDescription(Ctx ctx, decimal currentBalance)
        {
            if (currentBalance < 0)
            {
                return GetMsg(ctx, "VAS_050_ShortOfFloat", "short of float");
            }

            if (currentBalance > 0)
            {
                return GetMsg(ctx, "VAS_050_CashOnHand", "cash on hand");
            }

            return GetMsg(ctx, "VAS_050_NoCashLeft", "no cash left");
        }

        /// <summary>
        /// Safely reads decimal value from data reader by column name.
        /// </summary>
        /// <param name="reader">Data reader.</param>
        /// <param name="columnName">Column name.</param>
        /// <returns>Decimal value.</returns>
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

        /// <summary>
        /// Safely reads integer value from data reader by column name.
        /// </summary>
        /// <param name="reader">Data reader.</param>
        /// <param name="columnName">Column name.</param>
        /// <param name="fallback">Fallback value.</param>
        /// <returns>Integer value.</returns>
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

        /// <summary>
        /// Safely reads string value from data reader by column name.
        /// </summary>
        /// <param name="reader">Data reader.</param>
        /// <param name="columnName">Column name.</param>
        /// <returns>String value.</returns>
        private string GetString(IDataReader reader, string columnName)
        {
            object value = reader[columnName];

            if (value == null || value == DBNull.Value)
            {
                return string.Empty;
            }

            return value.ToString();
        }

        /// <summary>
        /// Safely formats a database date value.
        /// </summary>
        /// <param name="value">Database date value.</param>
        /// <returns>Formatted date text.</returns>
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
    }
}
