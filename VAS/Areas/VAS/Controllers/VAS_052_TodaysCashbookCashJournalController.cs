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
    /// Purpose     : Provides today's cashbook entries for Cash Journal dashboard widget.
    /// Chronological development:
    ///   VAS   Created 2026-06-07
    /// </summary>
    public class VAS_052_TodaysCashbookCashJournalController : Controller
    {
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetTodaysCashbook(int cashBookId = 0, int pageNo = 1, int pageSize = 6)
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

            if (pageNo <= 0)
            {
                pageNo = 1;
            }

            if (pageSize <= 0)
            {
                pageSize = 4;
            }

            int startRow = ((pageNo - 1) * pageSize) + 1;
            int endRow = pageNo * pageSize;

            IDataReader reader = null;

            try
            {
                SqlQueryData queryData = BuildTodaysCashbookSql(ctx, cashBookId, startRow, endRow);

                reader = DB.ExecuteReader(queryData.Sql, queryData.Parameters, null);

                List<object> entries = new List<object>();
                List<object> cashBooks = new List<object>();
                HashSet<int> cashBookIds = new HashSet<int>();

                int stdPrecision = 2;
                int currencyId = 0;
                string currencyISO = string.Empty;
                string currencySymbol = string.Empty;
                int totalEntries = 0;

                while (reader != null && reader.Read())
                {
                    totalEntries = GetInt(reader, "TotalRecords", totalEntries);

                    int rowCashBookId = GetInt(reader, "C_CashBook_ID");

                    if (!cashBookIds.Contains(rowCashBookId))
                    {
                        cashBookIds.Add(rowCashBookId);

                        cashBooks.Add(new
                        {
                            cCashBookId = rowCashBookId,
                            name = GetString(reader, "CashBookName")
                        });
                    }

                    decimal amount = GetDecimal(reader, "ConvertedAmount");

                    stdPrecision = GetInt(reader, "StdPrecision", stdPrecision);
                    currencyId = GetInt(reader, "C_Currency_ID", currencyId);
                    currencyISO = GetString(reader, "CurrencyISO");
                    currencySymbol = GetString(reader, "CurrencySymbol");

                    entries.Add(new
                    {
                        cCashLineId = GetInt(reader, "C_CashLine_ID"),
                        timeText = FormatTime(reader["Created"]),
                        description = GetDescription(ctx, reader),
                        category = GetCategory(ctx, reader),
                        categoryClass = GetCategoryClass(reader),
                        postedBy = GetPostedBy(ctx, reader),
                        cashInAmount = amount > 0 ? amount : 0,
                        cashOutAmount = amount < 0 ? Math.Abs(amount) : 0
                    });
                }

                return Json(new
                {
                    success = true,
                    error = string.Empty,
                    title = GetMsg(ctx, "VAS_052_TodaysCashbook", "Today's Cashbook"),
                    metaText = totalEntries.ToString() + " " + GetMsg(ctx, "VAS_052_Entries", "entries"),
                    actionText = GetMsg(ctx, "VAS_052_Entry", "+ Entry"),
                    currencyISO = currencyISO,
                    currencySymbol = currencySymbol,
                    cCurrencyId = currencyId,
                    stdPrecision = stdPrecision,
                    cashBooks = cashBooks,
                    entries = entries,
                    totalEntries = totalEntries,
                    totalRecords = totalEntries,
                    totalPages = pageSize == 0 ? 0 : Convert.ToInt32(Math.Ceiling((decimal)totalEntries / pageSize)),
                    pageNo = pageNo,
                    pageSize = pageSize,
                    hasData = totalEntries > 0
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    error = ex.Message,
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

        private SqlQueryData BuildTodaysCashbookSql(Ctx ctx, int cashBookId, int startRow, int endRow)
        {
            string todayDateSql = GetTodayDateSql();

            string cashBookFilter = string.Empty;

            if (cashBookId > 0)
            {
                cashBookFilter = @"
AND CashHeader.C_CashBook_ID = @CashBookId";
            }

            string dateRangeSql = @"
DateRange AS
(
SELECT
CAST(" + todayDateSql + @" AS TIMESTAMP) AS TodayStart,
CAST(" + todayDateSql + @" + 1 AS TIMESTAMP) AS TodayEnd
FROM AD_ClientInfo ClientInfo
WHERE ClientInfo.IsActive = 'Y'
AND ClientInfo.AD_Client_ID = @AD_Client_ID_DateRange
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
AND ClientInfo.AD_Client_ID = @AD_Client_ID_SchemaCurrency
)";

            string cashLineAccessSql = @"
SELECT
CashLine.C_CashLine_ID,
CashLine.C_Cash_ID,
CashLine.Created,
CashLine.CreatedBy,
CashLine.Description,
CashLine.Amount,
CashLine.C_Charge_ID
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

            string cashRowsSql = @"
CashRows AS
(
SELECT
CashLine.C_CashLine_ID,
CashLine.Created,
CashLine.Description,
CashLine.Amount,
ROUND(" + convertedAmountExpression + @", COALESCE(SchemaCurrency.StdPrecision, 2)) AS ConvertedAmount,
CashHeader.C_CashBook_ID,
CashBook.Name AS CashBookName,
Charge.Name AS CategoryName,
CreatedByUser.Name AS PostedBy,
COALESCE(SchemaCurrency.StdPrecision, 2) AS StdPrecision,
SchemaCurrency.C_Currency_ID AS C_Currency_ID,
SchemaCurrency.ISO_Code AS CurrencyISO,
SchemaCurrency.Cur_Symbol AS CurrencySymbol
FROM CashLineAccess CashLine
INNER JOIN C_Cash CashHeader ON (CashLine.C_Cash_ID = CashHeader.C_Cash_ID)
INNER JOIN C_CashBook CashBook ON (CashHeader.C_CashBook_ID = CashBook.C_CashBook_ID)
INNER JOIN SchemaCurrency SchemaCurrency ON (SchemaCurrency.AD_Client_ID = CashHeader.AD_Client_ID)
INNER JOIN DateRange DateRange ON (CAST(CashHeader.StatementDate AS TIMESTAMP) >= DateRange.TodayStart AND CAST(CashHeader.StatementDate AS TIMESTAMP) < DateRange.TodayEnd)
LEFT OUTER JOIN C_Charge Charge ON (CashLine.C_Charge_ID = Charge.C_Charge_ID)
LEFT OUTER JOIN AD_User CreatedByUser ON (CashLine.CreatedBy = CreatedByUser.AD_User_ID)
WHERE CashHeader.IsActive = 'Y'
AND CashBook.IsActive = 'Y'
AND CashHeader.DocStatus IN ('CO', 'CL')" + cashBookFilter + @"
),
CountData AS
(
SELECT COUNT(1) AS TotalRecords
FROM CashRows CashRows
),
PagedRows AS
(
SELECT
CashRows.C_CashLine_ID,
CashRows.Created,
CashRows.Description,
CashRows.ConvertedAmount,
CashRows.C_CashBook_ID,
CashRows.CashBookName,
CashRows.CategoryName,
CashRows.PostedBy,
CashRows.StdPrecision,
CashRows.C_Currency_ID,
CashRows.CurrencyISO,
CashRows.CurrencySymbol,
CountData.TotalRecords,
ROW_NUMBER() OVER (ORDER BY CashRows.Created DESC, CashRows.C_CashLine_ID DESC) AS RowNo
FROM CashRows CashRows
CROSS JOIN CountData CountData
)";

            string sql = @"
WITH " + dateRangeSql + @",
" + schemaCurrencySql + @",
" + cashLineAccessSqlCte + @",
" + cashRowsSql + @"
SELECT
PagedRows.C_CashLine_ID,
PagedRows.Created,
PagedRows.Description,
PagedRows.ConvertedAmount,
PagedRows.C_CashBook_ID,
PagedRows.CashBookName,
PagedRows.CategoryName,
PagedRows.PostedBy,
PagedRows.StdPrecision,
PagedRows.C_Currency_ID,
PagedRows.CurrencyISO,
PagedRows.CurrencySymbol,
PagedRows.TotalRecords
FROM PagedRows PagedRows
WHERE PagedRows.RowNo >= @StartRow
AND PagedRows.RowNo <= @EndRow
ORDER BY PagedRows.RowNo";

            List<SqlParameter> parameters = new List<SqlParameter>
            {
                new SqlParameter("@AD_Client_ID_DateRange", ctx.GetAD_Client_ID()),
                new SqlParameter("@AD_Client_ID_SchemaCurrency", ctx.GetAD_Client_ID())
            };

            if (cashBookId > 0)
            {
                parameters.Add(new SqlParameter("@CashBookId", cashBookId));
            }

            parameters.Add(new SqlParameter("@StartRow", startRow));
            parameters.Add(new SqlParameter("@EndRow", endRow));

            return new SqlQueryData
            {
                Sql = sql,
                Parameters = parameters.ToArray()
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

        private string FormatTime(object value)
        {
            if (value == null || value == DBNull.Value)
            {
                return string.Empty;
            }

            DateTime dateValue;

            if (DateTime.TryParse(value.ToString(), out dateValue))
            {
                return dateValue.ToString("HH:mm");
            }

            return string.Empty;
        }

        private string GetDescription(Ctx ctx, IDataReader reader)
        {
            string description = GetString(reader, "Description");

            return string.IsNullOrEmpty(description)
                ? GetMsg(ctx, "VAS_052_CashEntry", "Cash entry")
                : description;
        }

        private string GetCategory(Ctx ctx, IDataReader reader)
        {
            string category = GetString(reader, "CategoryName");

            return string.IsNullOrEmpty(category)
                ? GetMsg(ctx, "VAS_052_Other", "Other")
                : category;
        }

        private string GetCategoryClass(IDataReader reader)
        {
            string category = GetString(reader, "CategoryName").ToLowerInvariant();

            if (category.Contains("office"))
            {
                return "office";
            }

            if (category.Contains("travel"))
            {
                return "travel";
            }

            if (category.Contains("ar") || category.Contains("receivable") || category.Contains("customer"))
            {
                return "ar";
            }

            return "other";
        }

        private string GetPostedBy(Ctx ctx, IDataReader reader)
        {
            string postedBy = GetString(reader, "PostedBy");

            return string.IsNullOrEmpty(postedBy)
                ? GetMsg(ctx, "VAS_052_System", "System")
                : postedBy;
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