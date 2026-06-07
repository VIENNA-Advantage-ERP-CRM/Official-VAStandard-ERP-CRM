using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
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
    /// Purpose     : Provides today's cashbook entries for Cash Journal dashboard widget.
    /// Chronological development:
    ///   VAS   Created 2026-06-07
    /// </summary>
    public class VAS_052_TodaysCashbookCashJournalController : Controller
    {
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetTodaysCashbook(int cashBookId = 0)
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
                DateTime dateTo = today.AddDays(1);
                string dateFilter = GetDateFilter("CashHeader.StatementDate", today, dateTo);

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

                string cashLineSql = @"
                    SELECT CashLine.C_CashLine_ID,
                           CashLine.Created,
                           CashLine.Description,
                           CashLine.Amount,
                           ROUND(
                               " + convertedAmountExpression + @",
                               COALESCE(SchemaCurrency.StdPrecision, 2)
                           ) AS ConvertedAmount,
                           CashHeader.C_CashBook_ID,
                           CashBook.Name AS CashBookName,
                           Charge.Name AS CategoryName,
                           CreatedByUser.Name AS PostedBy,
                           COALESCE(SchemaCurrency.StdPrecision, 2) AS StdPrecision,
                           SchemaCurrency.C_Currency_ID AS C_Currency_ID,
                           SchemaCurrency.ISO_Code AS CurrencyISO,
                           SchemaCurrency.Cur_Symbol AS CurrencySymbol
                    FROM C_CashLine CashLine
                    INNER JOIN C_Cash CashHeader
                        ON (CashLine.C_Cash_ID=CashHeader.C_Cash_ID)
                    INNER JOIN C_CashBook CashBook
                        ON (CashHeader.C_CashBook_ID=CashBook.C_CashBook_ID)
                    INNER JOIN SchemaCurrency SchemaCurrency
                        ON (SchemaCurrency.AD_Client_ID=CashHeader.AD_Client_ID)
                    LEFT OUTER JOIN C_Charge Charge
                        ON (CashLine.C_Charge_ID=Charge.C_Charge_ID)
                    LEFT OUTER JOIN AD_User CreatedByUser
                        ON (CashLine.CreatedBy=CreatedByUser.AD_User_ID)
                    WHERE CashLine.IsActive='Y'
                    AND CashHeader.IsActive='Y'
                    AND CashBook.IsActive='Y'
                    AND CashHeader.DocStatus IN ('CO','CL')"
                    + dateFilter;

                if (cashBookId > 0)
                {
                    cashLineSql += @"
                    AND CashHeader.C_CashBook_ID=@CashBookId";
                }

                cashLineSql = MRole.GetDefault(ctx).AddAccessSQL(cashLineSql, "CashLine", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                string sql = @"
                    WITH SchemaCurrency AS (
                        " + schemaCurrencySql + @"
                    ),
                    CashRows AS (
                        " + cashLineSql + @"
                    )
                    SELECT CashRows.C_CashLine_ID,
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
                           CashRows.CurrencySymbol
                    FROM CashRows CashRows
                    ORDER BY CashRows.Created DESC,
                             CashRows.C_CashLine_ID DESC";

                List<object> entries = new List<object>();
                List<object> cashBooks = new List<object>();
                HashSet<int> cashBookIds = new HashSet<int>();
                int stdPrecision = 2;
                int currencyId = 0;
                string currencyISO = string.Empty;
                string currencySymbol = string.Empty;
                int totalEntries = 0;

                SqlParameter[] parameters = null;

                if (cashBookId > 0)
                {
                    parameters = new SqlParameter[]
                    {
                        new SqlParameter("@CashBookId", cashBookId)
                    };
                }

                reader = DB.ExecuteReader(sql, parameters, null);

                while (reader.Read())
                {
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

                    totalEntries++;

                    if (entries.Count >= 6)
                    {
                        continue;
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
                    hasData = totalEntries > 0
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception)
            {
                return Json(new
                {
                    success = false,
                    error = GetMsg(ctx, "VAS_052_LoadError", "Unable to load today's cashbook"),
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
    }
}
