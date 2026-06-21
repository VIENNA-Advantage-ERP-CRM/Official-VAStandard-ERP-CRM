
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
        public JsonResult GetTodayCashOutByCategory(
            int pageNo = 1,
            int pageSize = 3
        )
        {
            Ctx ctx =
                Session["ctx"] as Ctx;

            if (ctx == null)
            {
                return Json(
                    new
                    {
                        success = false,
                        error = "Session Expired",
                        hasData = false
                    },
                    JsonRequestBehavior.AllowGet
                );
            }

            if (pageNo <= 0)
            {
                pageNo = 1;
            }

            if (pageSize <= 0)
            {
                pageSize = 2;
            }

            int offset =
                (pageNo - 1) *
                pageSize;

            IDataReader reader =
                null;

            try
            {
                string language =
                    Env.GetAD_Language(ctx);

                if (
                    string.IsNullOrEmpty(
                        language
                    )
                )
                {
                    language =
                        "en_US";
                }

                SqlQueryData queryData =
                    BuildTodayCashOutByCategorySql(
                        ctx,
                        language,
                        offset,
                        pageSize
                    );

                reader =
                    DB.ExecuteReader(
                        queryData.Sql,
                        queryData.Parameters,
                        null
                    );

                List<CategoryCashOut> categories =
                    new List<CategoryCashOut>();

                int totalLineCount =
                    0;

                int totalRecords =
                    0;

                decimal totalCashOut =
                    0;

                int stdPrecision =
                    2;

                int currencyId =
                    0;

                string currencyISO =
                    string.Empty;

                string currencySymbol =
                    string.Empty;

                string dateTo =
                    string.Empty;

                while (
                    reader != null &&
                    reader.Read()
                )
                {
                    if (
                        string.IsNullOrEmpty(
                            dateTo
                        )
                    )
                    {
                        dateTo =
                            FormatDbDate(
                                reader["DateTo"]
                            );
                    }

                    totalRecords =
                        GetInt(
                            reader,
                            "TotalRecords",
                            totalRecords
                        );

                    totalLineCount =
                        GetInt(
                            reader,
                            "TotalLineCount",
                            totalLineCount
                        );

                    totalCashOut =
                        GetDecimal(
                            reader,
                            "TotalCashOut"
                        );

                    int lineCount =
                        GetInt(
                            reader,
                            "LineCount"
                        );

                    if (lineCount <= 0)
                    {
                        continue;
                    }

                    string categoryName =
                        GetString(
                            reader,
                            "CategoryName"
                        );

                    decimal cashOutAmount =
                        GetDecimal(
                            reader,
                            "CashOutAmount"
                        );

                    stdPrecision =
                        GetInt(
                            reader,
                            "StdPrecision",
                            stdPrecision
                        );

                    currencyId =
                        GetInt(
                            reader,
                            "C_Currency_ID",
                            currencyId
                        );

                    currencyISO =
                        GetString(
                            reader,
                            "CurrencyISO"
                        );

                    currencySymbol =
                        GetString(
                            reader,
                            "CurrencySymbol"
                        );

                    if (
                        string.IsNullOrWhiteSpace(
                            categoryName
                        )
                    )
                    {
                        categoryName =
                            GetMsg(
                                ctx,
                                "VAS_055_Other",
                                "Other"
                            );
                    }

                    categories.Add(
                        new CategoryCashOut
                        {
                            CategoryName =
                                categoryName,

                            CashOutAmount =
                                cashOutAmount,

                            LineCount =
                                lineCount
                        }
                    );
                }

                List<object> items =
                    new List<object>();

                for (
                    int index = 0;
                    index < categories.Count;
                    index++
                )
                {
                    CategoryCashOut category =
                        categories[index];

                    decimal percent =
                        totalCashOut > 0
                            ? Math.Round(
                                (
                                    category.CashOutAmount /
                                    totalCashOut
                                )
                                *
                                100,
                                0
                            )
                            : 0;

                    items.Add(
                        new
                        {
                            name =
                                category.CategoryName,

                            cashOutAmount =
                                category.CashOutAmount,

                            lineCount =
                                category.LineCount,

                            percent =
                                percent,

                            cCurrencyId =
                                currencyId,

                            currencyISO =
                                currencyISO,

                            currencySymbol =
                                currencySymbol,

                            stdPrecision =
                                stdPrecision,

                            colorClass =
                                "VAS_055_cash-category-bar-" +
                                (index + 1).ToString()
                        }
                    );
                }

                return Json(
                    new
                    {
                        success = true,
                        error = string.Empty,

                        title = GetMsg(
                            ctx,
                            "VAS_055_CashOutByCategory",
                            "Cash Out by Category"
                        ),

                        metaText = GetMsg(
                            ctx,
                            "VAS_055_Today",
                            "Today"
                        ),

                        whyLabel = GetMsg(
                            ctx,
                            "VAS_055_Why",
                            "Why"
                        ),

                        whyText = GetMsg(
                            ctx,
                            "VAS_055_WhyText",
                            "Grouped by cash type for today."
                        ),

                        noDataText = GetMsg(
                            ctx,
                            "VAS_055_NoData",
                            "No cash out today"
                        ),

                        dateTo =
                            dateTo,

                        items =
                            items,

                        totalCashOut =
                            totalCashOut,

                        totalRecords =
                            totalRecords,

                        totalPages =
                            pageSize == 0
                                ? 0
                                : Convert.ToInt32(
                                    Math.Ceiling(
                                        (decimal)totalRecords /
                                        pageSize
                                    )
                                ),

                        pageNo =
                            pageNo,

                        pageSize =
                            pageSize,

                        cCurrencyId =
                            currencyId,

                        currencyISO =
                            currencyISO,

                        currencySymbol =
                            currencySymbol,

                        stdPrecision =
                            stdPrecision,

                        hasData =
                            totalLineCount > 0
                    },
                    JsonRequestBehavior.AllowGet
                );
            }
            catch (Exception)
            {
                return Json(
                    new
                    {
                        success = false,

                        error = GetMsg(
                            ctx,
                            "VAS_055_LoadError",
                            "Unable to load cash out by category"
                        ),

                        hasData = false
                    },
                    JsonRequestBehavior.AllowGet
                );
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

        private SqlQueryData BuildTodayCashOutByCategorySql(
            Ctx ctx,
            string language,
            int offset,
            int pageSize
        )
        {
            string todayDateSql =
                GetTodayDateSql();

            string queryParametersFrom =
                DB.IsOracle()
                    ? " FROM DUAL"
                    : string.Empty;

            string numericType =
                DB.IsOracle()
                    ? "NUMBER"
                    : "NUMERIC";

            string textType =
                DB.IsOracle()
                    ? "VARCHAR2(4000)"
                    : "VARCHAR(4000)";

            int startRow =
                offset + 1;

            int endRow =
                offset + pageSize;

            /*
             * Each bind variable appears exactly once.
             *
             * Oracle requires FROM DUAL.
             * PostgreSQL does not require a FROM clause.
             */
            string queryParametersSql = @"
QueryParameters AS
(
    SELECT
        @AD_Client_ID AS AD_Client_ID,
        @AD_Language AS AD_Language,
        @StartRow AS StartRow,
        @EndRow AS EndRow"
        + queryParametersFrom + @"
)";

            string dateRangeSql = @"
DateRange AS
(
    SELECT
        QueryParameters.AD_Client_ID,

        " + todayDateSql + @"
            AS TodayDate,

        CAST
        (
            " + todayDateSql + @"
            AS TIMESTAMP
        ) AS TodayStart,

        CAST
        (
            " + todayDateSql + @" + 1
            AS TIMESTAMP
        ) AS TodayEnd

    FROM QueryParameters QueryParameters
)";

            string schemaCurrencySql = @"
SchemaCurrency AS
(
    SELECT
        ClientInfo.AD_Client_ID,

        AcctSchema.C_Currency_ID
            AS C_Currency_ID,

        Currency.StdPrecision,

        Currency.ISO_Code
            AS ISO_Code,

        CASE
            WHEN Currency.CurSymbol IS NOT NULL
            THEN Currency.CurSymbol
            ELSE Currency.ISO_Code
        END AS Cur_Symbol

    FROM AD_ClientInfo ClientInfo

    INNER JOIN C_AcctSchema AcctSchema ON
    (
        ClientInfo.C_AcctSchema1_ID =
        AcctSchema.C_AcctSchema_ID
    )

    INNER JOIN C_Currency Currency ON
    (
        AcctSchema.C_Currency_ID =
        Currency.C_Currency_ID
    )

    WHERE ClientInfo.IsActive = 'Y'

    AND AcctSchema.IsActive = 'Y'

    AND Currency.IsActive = 'Y'

    AND ClientInfo.AD_Client_ID =
    (
        SELECT
            QueryParameters.AD_Client_ID

        FROM QueryParameters QueryParameters
    )
)";

            /*
             * MRole is applied only to the physical main table.
             */
            string cashLineAccessSql = @"
SELECT
    CashLine.C_CashLine_ID,
    CashLine.C_Cash_ID,
    CashLine.Amount,
    CashLine.CashType

FROM C_CashLine CashLine

WHERE CashLine.IsActive = 'Y'

AND CashLine.Amount < 0";

            cashLineAccessSql =
                MRole.GetDefault(ctx)
                    .AddAccessSQL(
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

            /*
             * Resolve CashType reference through AD_Table and AD_Column.
             * No reference name is hardcoded.
             *
             * Text values are cast to the same type to prevent
             * Oracle ORA-12704.
             */
            string cashTypeReferenceSql = @"
CashTypeReferenceSource AS
(
    SELECT
        CAST
        (
            RefList.Value
            AS " + textType + @"
        ) AS ReferenceValue,

        CAST
        (
            RefList.Name
            AS " + textType + @"
        ) AS BaseName,

        CAST
        (
            RefListTrl.Name
            AS " + textType + @"
        ) AS TranslatedName,

        ROW_NUMBER() OVER
        (
            PARTITION BY
                CAST
                (
                    RefList.Value
                    AS " + textType + @"
                )

            ORDER BY
                CASE
                    WHEN RefListTrl.Name IS NOT NULL
                    THEN 0
                    ELSE 1
                END,

                RefList.AD_Ref_List_ID
        ) AS ReferenceRowNumber

    FROM AD_Table TableInfo

    INNER JOIN AD_Column ColumnInfo ON
    (
        ColumnInfo.AD_Table_ID =
        TableInfo.AD_Table_ID
    )

    INNER JOIN AD_Reference ReferenceInfo ON
    (
        ReferenceInfo.AD_Reference_ID =
        ColumnInfo.AD_Reference_Value_ID
    )

    INNER JOIN AD_Ref_List RefList ON
    (
        RefList.AD_Reference_ID =
        ReferenceInfo.AD_Reference_ID
    )

    LEFT OUTER JOIN AD_Ref_List_Trl RefListTrl ON
    (
        RefListTrl.AD_Ref_List_ID =
        RefList.AD_Ref_List_ID

        AND RefListTrl.AD_Language =
        (
            SELECT
                QueryParameters.AD_Language

            FROM QueryParameters QueryParameters
        )
    )

    WHERE TableInfo.TableName =
        'C_CashLine'

    AND ColumnInfo.ColumnName =
        'CashType'

    AND TableInfo.IsActive = 'Y'

    AND ColumnInfo.IsActive = 'Y'

    AND ReferenceInfo.IsActive = 'Y'

    AND RefList.IsActive = 'Y'
),
CashTypeReference AS
(
    SELECT
        CashTypeReferenceSource.ReferenceValue,
        CashTypeReferenceSource.BaseName,
        CashTypeReferenceSource.TranslatedName

    FROM CashTypeReferenceSource CashTypeReferenceSource

    WHERE CashTypeReferenceSource.ReferenceRowNumber = 1
)";

            string convertedAmountExpression = @"
CASE
    WHEN CashBook.C_Currency_ID =
        SchemaCurrency.C_Currency_ID

    THEN
        COALESCE
        (
            CashLine.Amount,
            0
        )

    ELSE
        CurrencyConvert
        (
            COALESCE
            (
                CashLine.Amount,
                0
            ),
            CashBook.C_Currency_ID,
            SchemaCurrency.C_Currency_ID,
            CashHeader.DateAcct,
            0,
            CashHeader.AD_Client_ID,
            CashHeader.AD_Org_ID
        )
END";

            string rawCashOutSql = @"
RawCashOut AS
(
    SELECT
        CashLine.C_CashLine_ID,
        CashLine.Amount,

        CASE
            WHEN CashTypeReference.TranslatedName IS NOT NULL
            THEN CashTypeReference.TranslatedName

            WHEN CashTypeReference.BaseName IS NOT NULL
            THEN CashTypeReference.BaseName

            ELSE
                CAST
                (
                    CashLine.CashType
                    AS " + textType + @"
                )
        END AS CashType,

        CAST
        (
            " + convertedAmountExpression + @"
            AS " + numericType + @"
        ) AS ConvertedAmount,

        COALESCE
        (
            SchemaCurrency.StdPrecision,
            2
        ) AS StdPrecision,

        SchemaCurrency.C_Currency_ID
            AS C_Currency_ID,

        SchemaCurrency.ISO_Code
            AS CurrencyISO,

        SchemaCurrency.Cur_Symbol
            AS CurrencySymbol

    FROM CashLineAccess CashLine

    INNER JOIN C_Cash CashHeader ON
    (
        CashLine.C_Cash_ID =
        CashHeader.C_Cash_ID
    )

    INNER JOIN C_CashBook CashBook ON
    (
        CashHeader.C_CashBook_ID =
        CashBook.C_CashBook_ID
    )

    INNER JOIN SchemaCurrency SchemaCurrency ON
    (
        SchemaCurrency.AD_Client_ID =
        CashHeader.AD_Client_ID
    )

    INNER JOIN DateRange DateRange ON
    (
        CashHeader.StatementDate >=
        DateRange.TodayStart

        AND CashHeader.StatementDate <
        DateRange.TodayEnd
    )

    LEFT OUTER JOIN CashTypeReference CashTypeReference ON
    (
        CashTypeReference.ReferenceValue =
        CAST
        (
            CashLine.CashType
            AS " + textType + @"
        )
    )

    WHERE CashHeader.IsActive = 'Y'

    AND CashBook.IsActive = 'Y'

    AND CashHeader.AD_Client_ID =
    (
        SELECT
            QueryParameters.AD_Client_ID

        FROM QueryParameters QueryParameters
    )

    AND CashHeader.DocStatus IN
    (
        'CO',
        'CL'
    )
)";

            string categoryTotalsSql = @"
CategoryTotals AS
(
    SELECT
        RawCashOut.CashType
            AS CategoryName,

        ROUND
        (
            CAST
            (
                COALESCE
                (
                    SUM
                    (
                        0 -
                        COALESCE
                        (
                            RawCashOut.ConvertedAmount,
                            0
                        )
                    ),
                    0
                )
                AS " + numericType + @"
            ),

            CAST
            (
                COALESCE
                (
                    MAX
                    (
                        RawCashOut.StdPrecision
                    ),
                    2
                )
                AS INTEGER
            )
        ) AS CashOutAmount,

        COUNT
        (
            RawCashOut.C_CashLine_ID
        ) AS LineCount,

        COALESCE
        (
            MAX
            (
                RawCashOut.StdPrecision
            ),
            2
        ) AS StdPrecision,

        MAX
        (
            RawCashOut.C_Currency_ID
        ) AS C_Currency_ID,

        MAX
        (
            RawCashOut.CurrencyISO
        ) AS CurrencyISO,

        MAX
        (
            RawCashOut.CurrencySymbol
        ) AS CurrencySymbol

    FROM RawCashOut RawCashOut

    GROUP BY
        RawCashOut.CashType
),
CountData AS
(
    SELECT
        COUNT(1)
            AS TotalRecords,

        ROUND
        (
            CAST
            (
                COALESCE
                (
                    SUM
                    (
                        CategoryTotals.CashOutAmount
                    ),
                    0
                )
                AS " + numericType + @"
            ),

            CAST
            (
                COALESCE
                (
                    MAX
                    (
                        CategoryTotals.StdPrecision
                    ),
                    2
                )
                AS INTEGER
            )
        ) AS TotalCashOut,

        COALESCE
        (
            SUM
            (
                CategoryTotals.LineCount
            ),
            0
        ) AS TotalLineCount

    FROM CategoryTotals CategoryTotals
),
NumberedCategoryTotals AS
(
    SELECT
        CategoryTotals.CategoryName,
        CategoryTotals.CashOutAmount,
        CategoryTotals.LineCount,
        CategoryTotals.StdPrecision,
        CategoryTotals.C_Currency_ID,
        CategoryTotals.CurrencyISO,
        CategoryTotals.CurrencySymbol,

        ROW_NUMBER() OVER
        (
            ORDER BY
                CategoryTotals.CashOutAmount DESC,
                CategoryTotals.CategoryName
        ) AS RowNumber

    FROM CategoryTotals CategoryTotals
),
PagedCategoryTotals AS
(
    SELECT
        NumberedCategoryTotals.CategoryName,
        NumberedCategoryTotals.CashOutAmount,
        NumberedCategoryTotals.LineCount,
        NumberedCategoryTotals.StdPrecision,
        NumberedCategoryTotals.C_Currency_ID,
        NumberedCategoryTotals.CurrencyISO,
        NumberedCategoryTotals.CurrencySymbol

    FROM NumberedCategoryTotals NumberedCategoryTotals

    WHERE NumberedCategoryTotals.RowNumber >=
    (
        SELECT
            QueryParameters.StartRow

        FROM QueryParameters QueryParameters
    )

    AND NumberedCategoryTotals.RowNumber <=
    (
        SELECT
            QueryParameters.EndRow

        FROM QueryParameters QueryParameters
    )
)";

            string sql = @"
WITH
" + queryParametersSql + @",
" + dateRangeSql + @",
" + schemaCurrencySql + @",
" + cashLineAccessCteSql + @",
" + cashTypeReferenceSql + @",
" + rawCashOutSql + @",
" + categoryTotalsSql + @"
SELECT
    CategoryTotals.CategoryName,

    COALESCE
    (
        CategoryTotals.CashOutAmount,
        0
    ) AS CashOutAmount,

    COALESCE
    (
        CategoryTotals.LineCount,
        0
    ) AS LineCount,

    COALESCE
    (
        CategoryTotals.StdPrecision,
        SchemaCurrency.StdPrecision,
        2
    ) AS StdPrecision,

    COALESCE
    (
        CategoryTotals.C_Currency_ID,
        SchemaCurrency.C_Currency_ID,
        0
    ) AS C_Currency_ID,

    CASE
        WHEN CategoryTotals.CurrencyISO IS NOT NULL
        THEN CategoryTotals.CurrencyISO
        ELSE SchemaCurrency.ISO_Code
    END AS CurrencyISO,

    CASE
        WHEN CategoryTotals.CurrencySymbol IS NOT NULL
        THEN CategoryTotals.CurrencySymbol
        ELSE SchemaCurrency.Cur_Symbol
    END AS CurrencySymbol,

    DateRange.TodayDate
        AS DateTo,

    CountData.TotalRecords,
    CountData.TotalCashOut,
    CountData.TotalLineCount

FROM DateRange DateRange

INNER JOIN SchemaCurrency SchemaCurrency ON
(
    SchemaCurrency.AD_Client_ID =
    DateRange.AD_Client_ID
)

INNER JOIN CountData CountData ON
(
    1 = 1
)

LEFT OUTER JOIN PagedCategoryTotals CategoryTotals ON
(
    1 = 1
)

ORDER BY
    CategoryTotals.CashOutAmount DESC,
    CategoryTotals.CategoryName";

            SqlParameter[] parameters =
                new SqlParameter[]
                {
                    new SqlParameter(
                        "@AD_Client_ID",
                        ctx.GetAD_Client_ID()
                    ),

                    new SqlParameter(
                        "@AD_Language",
                        language
                    ),

                    new SqlParameter(
                        "@StartRow",
                        startRow
                    ),

                    new SqlParameter(
                        "@EndRow",
                        endRow
                    )
                };

            return new SqlQueryData
            {
                Sql =
                    sql,

                Parameters =
                    parameters
            };
        }

        private string GetTodayDateSql()
        {
            if (DB.IsOracle())
            {
                return
                    "TRUNC(CURRENT_DATE)";
            }

            return
                "CURRENT_DATE";
        }

        private string FormatDate(
            DateTime date
        )
        {
            return date.ToString(
                "yyyy-MM-dd"
            );
        }

        private string FormatDbDate(
            object value
        )
        {
            if (
                value == null ||
                value == DBNull.Value
            )
            {
                return string.Empty;
            }

            DateTime dateValue;

            if (
                DateTime.TryParse(
                    value.ToString(),
                    out dateValue
                )
            )
            {
                return FormatDate(
                    dateValue
                );
            }

            return string.Empty;
        }

        private string GetMsg(
            Ctx ctx,
            string key,
            string fallback
        )
        {
            string msg =
                Msg.GetMsg(
                    ctx,
                    key
                );

            if (
                string.IsNullOrEmpty(
                    msg
                ) ||
                msg == key ||
                msg == "[" + key + "]"
            )
            {
                return fallback;
            }

            return msg;
        }

        private decimal GetDecimal(
            IDataReader reader,
            string columnName
        )
        {
            object value =
                reader[columnName];

            if (
                value == null ||
                value == DBNull.Value
            )
            {
                return 0;
            }

            decimal result;

            return decimal.TryParse(
                value.ToString(),
                out result
            )
                ? result
                : 0;
        }

        private int GetInt(
            IDataReader reader,
            string columnName,
            int fallback = 0
        )
        {
            object value =
                reader[columnName];

            if (
                value == null ||
                value == DBNull.Value
            )
            {
                return fallback;
            }

            int result;

            return int.TryParse(
                value.ToString(),
                out result
            )
                ? result
                : fallback;
        }

        private string GetString(
            IDataReader reader,
            string columnName
        )
        {
            object value =
                reader[columnName];

            if (
                value == null ||
                value == DBNull.Value
            )
            {
                return string.Empty;
            }

            return value.ToString();
        }

        private class CategoryCashOut
        {
            public string CategoryName
            {
                get;
                set;
            }

            public decimal CashOutAmount
            {
                get;
                set;
            }

            public int LineCount
            {
                get;
                set;
            }
        }

        private class SqlQueryData
        {
            public string Sql
            {
                get;
                set;
            }

            public SqlParameter[] Parameters
            {
                get;
                set;
            }
        }
    }
}
