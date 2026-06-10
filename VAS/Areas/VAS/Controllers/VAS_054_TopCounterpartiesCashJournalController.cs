using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Web.Mvc;
using VAdvantage.Classes;
using VAdvantage.DataBase;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VAS.Controllers
{
    /// <summary>
    /// Module Name : Cash Journal
    /// Purpose     : Provides top counterparties from Cash Journal lines.
    /// Chronological development:
    ///   VAS   Created 2026-06-07
    /// </summary>
    public class VAS_054_TopCounterpartiesCashJournalController : Controller
    {
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetTopCounterparties()
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
                SqlQueryData queryData = BuildTopCounterpartiesSql(ctx);

                reader = DB.ExecuteReader(queryData.Sql, queryData.Parameters, null);

                List<object> items = new List<object>();
                int totalCounterparties = 0;
                int stdPrecision = 2;
                int currencyId = 0;
                string currencyISO = string.Empty;
                string currencySymbol = string.Empty;
                string dateFrom = string.Empty;
                string dateTo = string.Empty;

                while (reader != null && reader.Read())
                {
                    totalCounterparties++;

                    if (string.IsNullOrEmpty(dateFrom))
                    {
                        dateFrom = FormatDbDate(reader["DateFrom"]);
                    }

                    if (string.IsNullOrEmpty(dateTo))
                    {
                        dateTo = FormatDbDate(reader["DateTo"]);
                    }

                    if (items.Count >= 5)
                    {
                        continue;
                    }

                    decimal netAmount = GetDecimal(reader, "NetAmount");
                    decimal inAmount = GetDecimal(reader, "InAmount");
                    decimal outAmount = GetDecimal(reader, "OutAmount");
                    string counterpartyType = GetString(reader, "CounterpartyType");
                    string counterpartyName = GetString(reader, "CounterpartyName");

                    stdPrecision = GetInt(reader, "StdPrecision", stdPrecision);
                    currencyId = GetInt(reader, "C_Currency_ID", currencyId);
                    currencyISO = GetString(reader, "CurrencyISO");
                    currencySymbol = GetString(reader, "CurrencySymbol");

                    items.Add(new
                    {
                        counterpartyType = counterpartyType,
                        counterpartyId = GetInt(reader, "Counterparty_ID"),
                        name = string.IsNullOrWhiteSpace(counterpartyName) ? GetMsg(ctx, "VAS_054_Unknown", "Unknown") : counterpartyName,
                        initials = GetInitials(counterpartyName),
                        typeText = GetTypeText(ctx, counterpartyType, netAmount),
                        typeClass = GetTypeClass(counterpartyType, netAmount),
                        entryCount = GetInt(reader, "EntryCount"),
                        netAmount = netAmount,
                        displayAmount = Math.Abs(netAmount),
                        inAmount = inAmount,
                        outAmount = outAmount,
                        trend = GetTrendClass(netAmount),
                        currencyISO = currencyISO,
                        currencySymbol = currencySymbol,
                        stdPrecision = stdPrecision
                    });
                }

                return Json(new
                {
                    success = true,
                    error = string.Empty,
                    title = GetMsg(ctx, "VAS_054_TopCounterparties", "Top Counterparties"),
                    metaText = GetMsg(ctx, "VAS_054_Last30Days", "Last 30 Days"),
                    actionText = GetMsg(ctx, "VAS_054_AllParties", "All parties ->"),
                    noDataText = GetMsg(ctx, "VAS_054_NoData", "No counterparties found"),
                    dateFrom = dateFrom,
                    dateTo = dateTo,
                    totalCounterparties = totalCounterparties,
                    cCurrencyId = currencyId,
                    currencyISO = currencyISO,
                    currencySymbol = currencySymbol,
                    stdPrecision = stdPrecision,
                    items = items,
                    hasData = totalCounterparties > 0
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception)
            {
                return Json(new
                {
                    success = false,
                    error = GetMsg(ctx, "VAS_054_LoadError", "Unable to load top counterparties"),
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

        private SqlQueryData BuildTopCounterpartiesSql(Ctx ctx)
        {
            string todayDateSql = GetTodayDateSql();

            string dateRangeSql = @"
DateRange AS
(
SELECT
CAST(" + todayDateSql + @" - 29 AS TIMESTAMP) AS DateFrom,
CAST(" + todayDateSql + @" + 1 AS TIMESTAMP) AS DateToExclusive,
" + todayDateSql + @" AS DateTo
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

            string cashLineAccessSql = @"
SELECT
CashLine.C_CashLine_ID,
CashLine.C_Cash_ID,
CashLine.C_BPartner_ID,
CashLine.C_CashBook_ID,
CashLine.C_BankAccount_ID,
CashLine.Description,
CashLine.Amount
FROM C_CashLine CashLine
WHERE CashLine.IsActive = 'Y'";

            /*
             * MRole Handling:
             * Apply MRole only on the main physical table C_CashLine CashLine.
             * Do not apply MRole on the final WITH query.
             * Do not apply MRole on CTE aliases.
             * Do not apply MRole on joined aliases.
             * Do not apply MRole on a query that already contains INNER JOIN.
             */
            cashLineAccessSql = MRole.GetDefault(ctx).AddAccessSQL(
                cashLineAccessSql,
                "CashLine",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            string cashLineAccessSqlCte = @"
CashLineAccess AS
(
" + cashLineAccessSql + @"
)";

            string convertedAmountExpression = @"
CASE WHEN CashHeader.C_Currency_ID = SchemaCurrency.C_Currency_ID THEN COALESCE(CashLine.Amount, 0) ELSE CurrencyConvert(COALESCE(CashLine.Amount, 0), CashHeader.C_Currency_ID, SchemaCurrency.C_Currency_ID, CashHeader.DateAcct, 0, CashHeader.AD_Client_ID, CashHeader.AD_Org_ID) END";

            string counterpartyLinesSql = @"
CounterpartyLines AS
(
SELECT
CASE WHEN CashLine.C_BPartner_ID IS NOT NULL AND CashLine.C_BPartner_ID > 0 THEN 'BP' WHEN CashLine.C_CashBook_ID IS NOT NULL AND CashLine.C_CashBook_ID > 0 THEN 'CASHBOOK' WHEN CashLine.C_BankAccount_ID IS NOT NULL AND CashLine.C_BankAccount_ID > 0 THEN 'BANK' ELSE 'OTHER' END AS CounterpartyType,
CASE WHEN CashLine.C_BPartner_ID IS NOT NULL AND CashLine.C_BPartner_ID > 0 THEN CashLine.C_BPartner_ID WHEN CashLine.C_CashBook_ID IS NOT NULL AND CashLine.C_CashBook_ID > 0 THEN CashLine.C_CashBook_ID WHEN CashLine.C_BankAccount_ID IS NOT NULL AND CashLine.C_BankAccount_ID > 0 THEN CashLine.C_BankAccount_ID ELSE 0 END AS Counterparty_ID,
CASE WHEN CashLine.C_BPartner_ID IS NOT NULL AND CashLine.C_BPartner_ID > 0 THEN BPartner.Name WHEN CashLine.C_CashBook_ID IS NOT NULL AND CashLine.C_CashBook_ID > 0 THEN LineCashBook.Name WHEN CashLine.C_BankAccount_ID IS NOT NULL AND CashLine.C_BankAccount_ID > 0 THEN COALESCE(Bank.Name, BankAccount.AccountNo) ELSE COALESCE(CashLine.Description, CashHeader.DocumentNo) END AS CounterpartyName,
" + convertedAmountExpression + @" AS ConvertedAmount,
CashHeader.StatementDate,
COALESCE(SchemaCurrency.StdPrecision, 2) AS StdPrecision,
SchemaCurrency.C_Currency_ID AS C_Currency_ID,
SchemaCurrency.ISO_Code AS CurrencyISO,
SchemaCurrency.Cur_Symbol AS CurrencySymbol
FROM CashLineAccess CashLine
INNER JOIN C_Cash CashHeader ON (CashLine.C_Cash_ID = CashHeader.C_Cash_ID)
INNER JOIN SchemaCurrency SchemaCurrency ON (SchemaCurrency.AD_Client_ID = CashHeader.AD_Client_ID)
INNER JOIN DateRange DateRange ON (CAST(CashHeader.StatementDate AS TIMESTAMP) >= DateRange.DateFrom AND CAST(CashHeader.StatementDate AS TIMESTAMP) < DateRange.DateToExclusive)
LEFT OUTER JOIN C_BPartner BPartner ON (CashLine.C_BPartner_ID = BPartner.C_BPartner_ID)
LEFT OUTER JOIN C_CashBook LineCashBook ON (CashLine.C_CashBook_ID = LineCashBook.C_CashBook_ID)
LEFT OUTER JOIN C_BankAccount BankAccount ON (CashLine.C_BankAccount_ID = BankAccount.C_BankAccount_ID)
LEFT OUTER JOIN C_Bank Bank ON (BankAccount.C_Bank_ID = Bank.C_Bank_ID)
WHERE CashHeader.IsActive = 'Y'
AND CashHeader.DocStatus IN ('CO', 'CL')
)";

            string counterpartyTotalsSql = @"
CounterpartyTotals AS
(
SELECT
CounterpartyLines.CounterpartyType,
CounterpartyLines.Counterparty_ID,
MAX(CounterpartyLines.CounterpartyName) AS CounterpartyName,
ROUND(SUM(CounterpartyLines.ConvertedAmount), COALESCE(MAX(CounterpartyLines.StdPrecision), 2)) AS NetAmount,
ROUND(SUM(CASE WHEN CounterpartyLines.ConvertedAmount > 0 THEN CounterpartyLines.ConvertedAmount ELSE 0 END), COALESCE(MAX(CounterpartyLines.StdPrecision), 2)) AS InAmount,
ROUND(SUM(CASE WHEN CounterpartyLines.ConvertedAmount < 0 THEN 0 - CounterpartyLines.ConvertedAmount ELSE 0 END), COALESCE(MAX(CounterpartyLines.StdPrecision), 2)) AS OutAmount,
COUNT(1) AS EntryCount,
MAX(CounterpartyLines.StdPrecision) AS StdPrecision,
MAX(CounterpartyLines.C_Currency_ID) AS C_Currency_ID,
MAX(CounterpartyLines.CurrencyISO) AS CurrencyISO,
MAX(CounterpartyLines.CurrencySymbol) AS CurrencySymbol
FROM CounterpartyLines CounterpartyLines
GROUP BY CounterpartyLines.CounterpartyType, CounterpartyLines.Counterparty_ID
)";

            string sql = @"
WITH " + dateRangeSql + @",
" + schemaCurrencySql + @",
" + cashLineAccessSqlCte + @",
" + counterpartyLinesSql + @",
" + counterpartyTotalsSql + @"
SELECT
CounterpartyTotals.CounterpartyType,
CounterpartyTotals.Counterparty_ID,
CounterpartyTotals.CounterpartyName,
CounterpartyTotals.NetAmount,
CounterpartyTotals.InAmount,
CounterpartyTotals.OutAmount,
CounterpartyTotals.EntryCount,
CounterpartyTotals.StdPrecision,
CounterpartyTotals.C_Currency_ID,
CounterpartyTotals.CurrencyISO,
CounterpartyTotals.CurrencySymbol,
DateRange.DateFrom AS DateFrom,
DateRange.DateTo AS DateTo
FROM CounterpartyTotals CounterpartyTotals
INNER JOIN DateRange DateRange ON (1 = 1)
ORDER BY ABS(CounterpartyTotals.NetAmount) DESC, CounterpartyTotals.EntryCount DESC";

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

        private string GetTodayDateSql()
        {
            if (DB.IsOracle())
            {
                return "TRUNC(CURRENT_DATE)";
            }

            return "CURRENT_DATE";
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

        private string GetTypeText(Ctx ctx, string counterpartyType, decimal netAmount)
        {
            if (counterpartyType == "BANK")
            {
                return GetMsg(ctx, "VAS_054_Bank", "Bank");
            }

            if (counterpartyType == "CASHBOOK")
            {
                return GetMsg(ctx, "VAS_054_Cashbook", "Cashbook");
            }

            if (counterpartyType == "BP")
            {
                return netAmount >= 0
                    ? GetMsg(ctx, "VAS_054_Customer", "Customer")
                    : GetMsg(ctx, "VAS_054_Vendor", "Vendor");
            }

            return GetMsg(ctx, "VAS_054_Other", "Other");
        }

        private string GetTypeClass(string counterpartyType, decimal netAmount)
        {
            if (counterpartyType == "BANK")
            {
                return "bank";
            }

            if (counterpartyType == "CASHBOOK")
            {
                return "cashbook";
            }

            if (counterpartyType == "BP")
            {
                return netAmount >= 0 ? "customer" : "vendor";
            }

            return "other";
        }

        private string GetTrendClass(decimal netAmount)
        {
            if (netAmount > 0)
            {
                return "up";
            }

            if (netAmount < 0)
            {
                return "down";
            }

            return "flat";
        }

        private string GetInitials(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "--";
            }

            string[] parts = name.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 1)
            {
                return parts[0].Substring(0, Math.Min(2, parts[0].Length)).ToUpperInvariant();
            }

            return (parts[0].Substring(0, 1) + parts[parts.Length - 1].Substring(0, 1)).ToUpperInvariant();
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

        private class SqlQueryData
        {
            public string Sql { get; set; }
            public SqlParameter[] Parameters { get; set; }
        }
    }
}