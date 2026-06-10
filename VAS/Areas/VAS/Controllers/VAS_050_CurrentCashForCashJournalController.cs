using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
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
    /// </summary>
    public class VAS_050_CurrentCashForCashJournalController : Controller
    {
        /// <summary>
        /// Gets latest ending balance for selected Cash Book / Drawer.
        /// </summary>
        /// <param name="cashBookId">
        /// Selected C_CashBook_ID. Pass 0 to use first accessible drawer with latest cash journal.
        /// </param>
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
            IDataReader cashBookReader = null;

            try
            {
                List<object> cashBooks = new List<object>();

                string cashBookListSql = @"
                    SELECT CashBook.C_CashBook_ID,
                           CashBook.Name
                    FROM C_CashBook CashBook
                    WHERE CashBook.IsActive = 'Y'
                    ORDER BY CashBook.Name";

                cashBookListSql = MRole.GetDefault(ctx).AddAccessSQL(
                    cashBookListSql,
                    "CashBook",
                    MRole.SQL_FULLYQUALIFIED,
                    MRole.SQL_RO
                );

                cashBookReader = DB.ExecuteReader(cashBookListSql, null, null);

                while (cashBookReader.Read())
                {
                    cashBooks.Add(new
                    {
                        cCashBookId = GetInt(cashBookReader, "C_CashBook_ID"),
                        name = GetString(cashBookReader, "Name")
                    });
                }

                cashBookReader.Close();
                cashBookReader.Dispose();
                cashBookReader = null;

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
                    AND CashBook.C_CashBook_ID=@CashBookId";
                }

                string convertedEndingBalanceExpression = @"
                    CASE
                        WHEN CashBook.C_Currency_ID = SchemaCurrency.C_Currency_ID THEN COALESCE(CashHeader.EndingBalance, 0)
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

                string latestCashSql = @"
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
                           SchemaCurrency.Cur_Symbol AS CurrencySymbol
                    FROM C_Cash CashHeader
                    INNER JOIN C_CashBook CashBook
                        ON (CashHeader.C_CashBook_ID=CashBook.C_CashBook_ID)
                    INNER JOIN SchemaCurrency SchemaCurrency
                        ON (SchemaCurrency.AD_Client_ID=CashHeader.AD_Client_ID)
                    WHERE CashHeader.IsActive='Y'
                    AND CashHeader.DocStatus IN ('DR','CO','CL')
                    AND CashBook.IsActive='Y'
                    " + cashBookFilter + @"
                    AND NOT EXISTS (SELECT 1
                        FROM C_Cash CashHeader2
                        WHERE CashHeader2.IsActive='Y'
                        AND CashHeader2.DocStatus IN ('DR','CO','CL')
                        AND CashHeader2.C_CashBook_ID=CashHeader.C_CashBook_ID
                        AND (
                            CashHeader2.StatementDate > CashHeader.StatementDate
                            OR (
                                CashHeader2.StatementDate=CashHeader.StatementDate
                                AND CashHeader2.C_Cash_ID > CashHeader.C_Cash_ID
                                )
                            )
                    )";

                latestCashSql = MRole.GetDefault(ctx).AddAccessSQL(
                    latestCashSql,
                    "CashHeader",
                    MRole.SQL_FULLYQUALIFIED,
                    MRole.SQL_RO
                );

                string sql = @"
                    WITH SchemaCurrency AS (
                        " + schemaCurrencySql + @"
                    )
                    SELECT LatestCash.C_Cash_ID,
                           LatestCash.C_CashBook_ID,
                           LatestCash.CashBookName,
                           LatestCash.DocumentNo,
                           LatestCash.StatementDate,
                           LatestCash.DateAcct,
                           LatestCash.CurrentBalance,
                           LatestCash.StdPrecision,
                           LatestCash.C_Currency_ID,
                           LatestCash.CurrencyISO,
                           LatestCash.CurrencySymbol
                    FROM (" + latestCashSql.Trim() + @") LatestCash
                    ORDER BY LatestCash.CashBookName";

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

                SqlParameter[] parameters = null;

                if (cashBookId > 0)
                {
                    parameters = new SqlParameter[]
                    {
                        new SqlParameter("@CashBookId", cashBookId)
                    };
                }

                reader = DB.ExecuteReader(sql, parameters, null);

                bool selectedRowRead = false;

                while (reader.Read())
                {
                    if (!selectedRowRead)
                    {
                        selectedRowRead = true;

                        selectedCashBookId = GetInt(reader, "C_CashBook_ID");
                        cashId = GetInt(reader, "C_Cash_ID");
                        cashBookName = GetString(reader, "CashBookName");
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
                        title = GetMsg(ctx, "VAS_050_CurrentCash", "Current cash"),
                        mainMetric = 0,
                        mainMetricText = "0",
                        footerAmount = 0,
                        description = GetMsg(ctx, "VAS_050_NoCashLeft", "no cash left"),
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
                VLogger.Get().SaveError("GetCurrentCash", ex);

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

                if (cashBookReader != null)
                {
                    cashBookReader.Close();
                    cashBookReader.Dispose();
                }
            }
        }

        /// <summary>
        /// Formats date using fixed yyyy-MM-dd pattern.
        /// </summary>
        private string FormatDate(DateTime date)
        {
            return date.ToString("yyyy-MM-dd");
        }

        /// <summary>
        /// Gets translated message with fallback.
        /// </summary>
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
