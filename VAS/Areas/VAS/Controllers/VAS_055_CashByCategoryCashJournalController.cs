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
    /// Purpose     : Provides today's cash out distribution by cash type for Cash Journal dashboard widget.
    /// </summary>
    public class VAS_055_CashByCategoryCashJournalController : Controller
    {
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetTodayCashOutByCategory()
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
                string language = Env.GetAD_Language(ctx);

                if (string.IsNullOrEmpty(language))
                {
                    language = "en_US";
                }

                SqlQueryData queryData = BuildTodayCashOutByCategorySql(ctx, language);

                reader = DB.ExecuteReader(queryData.Sql, queryData.Parameters, null);

                List<CategoryCashOut> categories = new List<CategoryCashOut>();

                int totalLineCount = 0;
                decimal totalCashOut = 0;
                int stdPrecision = 2;
                int currencyId = 0;
                string currencyISO = string.Empty;
                string currencySymbol = string.Empty;
                string dateTo = string.Empty;

                while (reader != null && reader.Read())
                {
                    if (string.IsNullOrEmpty(dateTo))
                    {
                        dateTo = FormatDbDate(reader["DateTo"]);
                    }

                    int lineCount = GetInt(reader, "LineCount");

                    if (lineCount <= 0)
                    {
                        continue;
                    }

                    string categoryName = GetString(reader, "CategoryName");
                    decimal cashOutAmount = GetDecimal(reader, "CashOutAmount");
                    stdPrecision = GetInt(reader, "StdPrecision", stdPrecision);
                    currencyId = GetInt(reader, "C_Currency_ID", currencyId);
                    currencyISO = GetString(reader, "CurrencyISO");
                    currencySymbol = GetString(reader, "CurrencySymbol");

                    if (string.IsNullOrWhiteSpace(categoryName))
                    {
                        categoryName = GetMsg(ctx, "VAS_055_Other", "Other");
                    }

                    totalLineCount += lineCount;
                    totalCashOut += cashOutAmount;

                    categories.Add(new CategoryCashOut
                    {
                        CategoryName = categoryName,
                        CashOutAmount = cashOutAmount,
                        LineCount = lineCount
                    });
                }

                List<CategoryCashOut> displayCategories = new List<CategoryCashOut>();

                for (int index = 0; index < categories.Count && index < 2; index++)
                {
                    displayCategories.Add(categories[index]);
                }

                if (categories.Count > 2)
                {
                    CategoryCashOut otherCategory = new CategoryCashOut
                    {
                        CategoryName = GetMsg(ctx, "VAS_055_Other", "Other"),
                        CashOutAmount = 0,
                        LineCount = 0
                    };

                    for (int index = 2; index < categories.Count; index++)
                    {
                        otherCategory.CashOutAmount += categories[index].CashOutAmount;
                        otherCategory.LineCount += categories[index].LineCount;
                    }

                    displayCategories.Add(otherCategory);
                }

                List<object> items = new List<object>();

                for (int index = 0; index < displayCategories.Count; index++)
                {
                    CategoryCashOut category = displayCategories[index];

                    decimal percent = totalCashOut > 0
                        ? Math.Round((category.CashOutAmount / totalCashOut) * 100, 0)
                        : 0;

                    items.Add(new
                    {
                        name = category.CategoryName,
                        cashOutAmount = category.CashOutAmount,
                        lineCount = category.LineCount,
                        percent = percent,
                        cCurrencyId = currencyId,
                        currencyISO = currencyISO,
                        currencySymbol = currencySymbol,
                        stdPrecision = stdPrecision,
                        colorClass = "VAS_055_cash-category-bar-" + (index + 1).ToString()
                    });
                }

                return Json(new
                {
                    success = true,
                    error = string.Empty,
                    title = GetMsg(ctx, "VAS_055_CashOutByCategory", "Cash Out by Category"),
                    metaText = GetMsg(ctx, "VAS_055_Today", "Today"),
                    whyLabel = GetMsg(ctx, "VAS_055_Why", "Why"),
                    whyText = GetMsg(ctx, "VAS_055_WhyText", "Grouped by cash type for today."),
                    noDataText = GetMsg(ctx, "VAS_055_NoData", "No cash out today"),
                    dateTo = dateTo,
                    items = items,
                    totalCashOut = totalCashOut,
                    cCurrencyId = currencyId,
                    currencyISO = currencyISO,
                    currencySymbol = currencySymbol,
                    stdPrecision = stdPrecision,
                    hasData = totalLineCount > 0
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception)
            {
                return Json(new
                {
                    success = false,
                    error = GetMsg(ctx, "VAS_055_LoadError", "Unable to load cash out by category"),
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

        private SqlQueryData BuildTodayCashOutByCategorySql(Ctx ctx, string language)
        {
            string todayDateSql = GetTodayDateSql();

            string dateRangeSql = @"
DateRange AS
(
SELECT
ClientInfo.AD_Client_ID,
" + todayDateSql + @" AS TodayDate,
CAST(" + todayDateSql + @" AS TIMESTAMP) AS TodayStart,
CAST(" + todayDateSql + @" + 1 AS TIMESTAMP) AS TodayEnd
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
INNER JOIN DateRange DateRange ON (ClientInfo.AD_Client_ID = DateRange.AD_Client_ID)
INNER JOIN C_AcctSchema AcctSchema ON (ClientInfo.C_AcctSchema1_ID = AcctSchema.C_AcctSchema_ID)
INNER JOIN C_Currency Currency ON (AcctSchema.C_Currency_ID = Currency.C_Currency_ID)
WHERE ClientInfo.IsActive = 'Y'
)";

            string cashLineAccessSql = @"
SELECT
CashLine.C_CashLine_ID,
CashLine.C_Cash_ID,
CashLine.Amount,
CashLine.CashType
FROM C_CashLine CashLine
WHERE CashLine.IsActive = 'Y'
AND CashLine.Amount < 0";

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

            string cashLineAccessCteSql = @"
CashLineAccess AS
(
" + cashLineAccessSql + @"
)";

            string cashTypeListSql = @"
CashTypeList AS
(
SELECT
RefList.Value,
COALESCE(RefListTrl.Name, RefList.Name) AS Name
FROM AD_Reference ReferenceInfo
INNER JOIN AD_Ref_List RefList ON (ReferenceInfo.AD_Reference_ID = RefList.AD_Reference_ID)
LEFT OUTER JOIN AD_Ref_List_Trl RefListTrl ON (RefList.AD_Ref_List_ID = RefListTrl.AD_Ref_List_ID AND RefListTrl.AD_Language = @AD_Language)
WHERE ReferenceInfo.Name = @ReferenceName
AND ReferenceInfo.IsActive = 'Y'
AND RefList.IsActive = 'Y'
)";

            string rawCashOutSql = @"
RawCashOut AS
(
SELECT
CashLine.C_CashLine_ID,
CashLine.Amount,
CashTypeList.Name AS CashType,
CASE WHEN CashBook.C_Currency_ID = SchemaCurrency.C_Currency_ID THEN COALESCE(CashLine.Amount, 0) ELSE CurrencyConvert(COALESCE(CashLine.Amount, 0), CashBook.C_Currency_ID, SchemaCurrency.C_Currency_ID, CashHeader.DateAcct, 0, CashHeader.AD_Client_ID, CashHeader.AD_Org_ID) END AS ConvertedAmount,
COALESCE(SchemaCurrency.StdPrecision, 2) AS StdPrecision,
SchemaCurrency.C_Currency_ID AS C_Currency_ID,
SchemaCurrency.ISO_Code AS CurrencyISO,
SchemaCurrency.Cur_Symbol AS CurrencySymbol
FROM CashLineAccess CashLine
INNER JOIN C_Cash CashHeader ON (CashLine.C_Cash_ID = CashHeader.C_Cash_ID)
INNER JOIN C_CashBook CashBook ON (CashHeader.C_CashBook_ID = CashBook.C_CashBook_ID)
INNER JOIN SchemaCurrency SchemaCurrency ON (SchemaCurrency.AD_Client_ID = CashHeader.AD_Client_ID)
INNER JOIN DateRange DateRange ON (CAST(CashHeader.StatementDate AS TIMESTAMP) >= DateRange.TodayStart AND CAST(CashHeader.StatementDate AS TIMESTAMP) < DateRange.TodayEnd)
LEFT OUTER JOIN CashTypeList CashTypeList ON (CashTypeList.Value = CashLine.CashType)
WHERE CashHeader.IsActive = 'Y'
AND CashBook.IsActive = 'Y'
AND CashHeader.DocStatus IN ('CO', 'CL')
)";

            string categoryTotalsSql = @"
CategoryTotals AS
(
SELECT
RawCashOut.CashType AS CategoryName,
ROUND(COALESCE(SUM(0 - COALESCE(RawCashOut.ConvertedAmount, 0)), 0), COALESCE(MAX(RawCashOut.StdPrecision), 2)) AS CashOutAmount,
COUNT(RawCashOut.C_CashLine_ID) AS LineCount,
COALESCE(MAX(RawCashOut.StdPrecision), 2) AS StdPrecision,
MAX(RawCashOut.C_Currency_ID) AS C_Currency_ID,
MAX(RawCashOut.CurrencyISO) AS CurrencyISO,
MAX(RawCashOut.CurrencySymbol) AS CurrencySymbol
FROM RawCashOut RawCashOut
GROUP BY RawCashOut.CashType
)";

            string sql = @"
WITH " + dateRangeSql + @",
" + schemaCurrencySql + @",
" + cashLineAccessCteSql + @",
" + cashTypeListSql + @",
" + rawCashOutSql + @",
" + categoryTotalsSql + @"
SELECT
CategoryTotals.CategoryName,
COALESCE(CategoryTotals.CashOutAmount, 0) AS CashOutAmount,
COALESCE(CategoryTotals.LineCount, 0) AS LineCount,
COALESCE(CategoryTotals.StdPrecision, SchemaCurrency.StdPrecision, 2) AS StdPrecision,
COALESCE(CategoryTotals.C_Currency_ID, SchemaCurrency.C_Currency_ID, 0) AS C_Currency_ID,
COALESCE(CategoryTotals.CurrencyISO, SchemaCurrency.ISO_Code, '') AS CurrencyISO,
COALESCE(CategoryTotals.CurrencySymbol, SchemaCurrency.Cur_Symbol, '') AS CurrencySymbol,
DateRange.TodayDate AS DateTo
FROM DateRange DateRange
INNER JOIN SchemaCurrency SchemaCurrency ON (1 = 1)
LEFT OUTER JOIN CategoryTotals CategoryTotals ON (1 = 1)
ORDER BY CategoryTotals.CashOutAmount DESC";

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@AD_Language", language),
                new SqlParameter("@ReferenceName", "C_Cash Trx Type")
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

        private class CategoryCashOut
        {
            public string CategoryName { get; set; }
            public decimal CashOutAmount { get; set; }
            public int LineCount { get; set; }
        }

        private class SqlQueryData
        {
            public string Sql { get; set; }
            public SqlParameter[] Parameters { get; set; }
        }
    }
}
