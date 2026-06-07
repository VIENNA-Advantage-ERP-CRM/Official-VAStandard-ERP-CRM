using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Web.Mvc;
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
                DateTime today = DateTime.Today;
                DateTime dateTo = today.AddDays(1);
                string dateFilter = GetDateFilter("CashHeader.StatementDate", today, dateTo);

                string language = Env.GetAD_Language(ctx);

                if (string.IsNullOrEmpty(language))
                {
                    language = "en_US";
                }

                string filteredCashLineSql = @"
SELECT CashLine.C_CashLine_ID,
CashLine.C_Cash_ID,
CashLine.Amount,
CashLine.CashType
FROM C_CashLine CashLine
WHERE CashLine.IsActive='Y'
AND CashLine.Amount < 0";

                filteredCashLineSql = MRole.GetDefault(ctx).AddAccessSQL(
                    filteredCashLineSql,
                    "CashLine",
                    MRole.SQL_FULLYQUALIFIED,
                    MRole.SQL_RO
                );

                string sql = @"
SELECT RawCashOut.CashType AS CategoryName,
ROUND(COALESCE(SUM(0 - COALESCE(RawCashOut.Amount,0)),0),2) AS CashOutAmount,
COUNT(RawCashOut.C_CashLine_ID) AS LineCount
FROM (SELECT FilteredCashLine.C_CashLine_ID,
FilteredCashLine.Amount,
CashTypeList.Name AS CashType
FROM (" + filteredCashLineSql + @") FilteredCashLine
INNER JOIN C_Cash CashHeader ON (FilteredCashLine.C_Cash_ID=CashHeader.C_Cash_ID)
INNER JOIN C_CashBook CashBook ON (CashHeader.C_CashBook_ID=CashBook.C_CashBook_ID)
LEFT OUTER JOIN (SELECT RefList.Value,
COALESCE(RefListTrl.Name,RefList.Name) AS Name
FROM AD_Reference ReferenceInfo
INNER JOIN AD_Ref_List RefList ON (ReferenceInfo.AD_Reference_ID=RefList.AD_Reference_ID)
LEFT OUTER JOIN AD_Ref_List_Trl RefListTrl ON (RefList.AD_Ref_List_ID=RefListTrl.AD_Ref_List_ID AND RefListTrl.AD_Language=@AD_Language)
WHERE ReferenceInfo.Name=@ReferenceName
AND ReferenceInfo.IsActive='Y'
AND RefList.IsActive='Y') CashTypeList ON (CashTypeList.Value=FilteredCashLine.CashType)
WHERE CashHeader.IsActive='Y'
AND CashBook.IsActive='Y'
AND CashHeader.DocStatus IN ('CO','CL')
" + dateFilter + @") RawCashOut
GROUP BY RawCashOut.CashType
ORDER BY CashOutAmount DESC";

                SqlParameter[] parameters = new SqlParameter[2];
                parameters[0] = new SqlParameter("@AD_Language", language);
                parameters[1] = new SqlParameter("@ReferenceName", "C_Cash Trx Type");

                List<CategoryCashOut> categories = new List<CategoryCashOut>();

                int totalLineCount = 0;
                decimal totalCashOut = 0;

                reader = DB.ExecuteReader(sql, parameters, null);

                while (reader.Read())
                {
                    string categoryName = GetString(reader, "CategoryName");
                    decimal cashOutAmount = GetDecimal(reader, "CashOutAmount");
                    int lineCount = GetInt(reader, "LineCount");

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
                    dateTo = FormatDate(today),
                    items = items,
                    totalCashOut = totalCashOut,
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

        private string GetDateFilter(string columnName, DateTime dateFrom, DateTime dateTo)
        {
            string dateFromText = FormatDate(dateFrom);
            string dateToText = FormatDate(dateTo);

            if (DB.IsOracle())
            {
                return @"
AND " + columnName + @" >= TO_DATE('" + dateFromText + @"','YYYY-MM-DD')
AND " + columnName + @" < TO_DATE('" + dateToText + @"','YYYY-MM-DD')";
            }

            return @"
AND " + columnName + @" >= DATE '" + dateFromText + @"'
AND " + columnName + @" < DATE '" + dateToText + @"'";
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

        private class CategoryCashOut
        {
            public string CategoryName { get; set; }
            public decimal CashOutAmount { get; set; }
            public int LineCount { get; set; }
        }
    }
}