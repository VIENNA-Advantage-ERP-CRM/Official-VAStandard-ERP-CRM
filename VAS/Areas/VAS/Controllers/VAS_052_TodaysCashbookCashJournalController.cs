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
        public JsonResult GetTodaysCashbook(
            int cashBookId = 0,
            int pageNo = 1,
            int pageSize = 6
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
                        error = "VAS_052_SessionExpired",
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
                pageSize = 3;
            }

            int startRow =
                ((pageNo - 1) * pageSize) + 1;

            int endRow =
                pageNo * pageSize;

            IDataReader reader =
                null;

            try
            {
                SqlQueryData queryData =
                    BuildTodaysCashbookSql(
                        ctx,
                        cashBookId,
                        startRow,
                        endRow
                    );

                reader =
                    DB.ExecuteReader(
                        queryData.Sql,
                        queryData.Parameters,
                        null
                    );

                List<object> entries =
                    new List<object>();

                List<object> cashBooks =
                    new List<object>();

                HashSet<int> cashBookIds =
                    new HashSet<int>();

                int stdPrecision = 2;
                int currencyId = 0;
                int totalEntries = 0;

                string currencyISO =
                    string.Empty;

                string currencySymbol =
                    string.Empty;

                while (
                    reader != null &&
                    reader.Read()
                )
                {
                    totalEntries =
                        GetInt(
                            reader,
                            "TotalRecords",
                            totalEntries
                        );

                    int rowCashBookId =
                        GetInt(
                            reader,
                            "C_CashBook_ID"
                        );

                    if (
                        !cashBookIds.Contains(
                            rowCashBookId
                        )
                    )
                    {
                        cashBookIds.Add(
                            rowCashBookId
                        );

                        cashBooks.Add(
                            new
                            {
                                cCashBookId =
                                    rowCashBookId,

                                name =
                                    GetString(
                                        reader,
                                        "CashBookName"
                                    )
                            }
                        );
                    }

                    decimal amount =
                        GetDecimal(
                            reader,
                            "ConvertedAmount"
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

                    entries.Add(
                        new
                        {
                            cCashLineId =
                                GetInt(
                                    reader,
                                    "C_CashLine_ID"
                                ),

                            timeText =
                                FormatTime(
                                    reader["Created"]
                                ),

                            charge =
                                GetCategory(
                                    ctx,
                                    reader
                                ),

                            categoryClass =
                                GetCategoryClass(
                                    reader
                                ),

                            cashTypeValue =
                                GetString(
                                    reader,
                                    "CashTypeValue"
                                ),

                            cashType =
                                GetCashType(
                                    reader
                                ),

                            cashTypeName =
                                GetCashType(
                                    reader
                                ),

                            postedBy =
                                GetPostedBy(
                                    ctx,
                                    reader
                                ),

                            cashInAmount =
                                amount > 0
                                    ? amount
                                    : 0,

                            cashOutAmount =
                                amount < 0
                                    ? Math.Abs(amount)
                                    : 0,

                            documentNo =
                                GetString(
                                    reader,
                                    "DocumentNo"
                                ),

                            description =
                                GetDescription(
                                    ctx,
                                    reader
                                )
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
                            "VAS_052_TodaysCashbook",
                            "Today's Cashbook"
                        ),

                        metaText =
                            totalEntries.ToString() +
                            " " +
                            GetMsg(
                                ctx,
                                "VAS_052_Entries",
                                "entries"
                            ),

                        actionText = GetMsg(
                            ctx,
                            "VAS_052_Entry",
                            "+ Entry"
                        ),

                        currencyISO =
                            currencyISO,

                        currencySymbol =
                            currencySymbol,

                        cCurrencyId =
                            currencyId,

                        stdPrecision =
                            stdPrecision,

                        cashBooks =
                            cashBooks,

                        entries =
                            entries,

                        totalEntries =
                            totalEntries,

                        totalRecords =
                            totalEntries,

                        totalPages =
                            pageSize == 0
                                ? 0
                                : Convert.ToInt32(
                                    Math.Ceiling(
                                        (decimal)totalEntries /
                                        pageSize
                                    )
                                ),

                        pageNo =
                            pageNo,

                        pageSize =
                            pageSize,

                        hasData =
                            totalEntries > 0
                    },
                    JsonRequestBehavior.AllowGet
                );
            }
            catch (Exception ex)
            {
                return Json(
                    new
                    {
                        success = false,
                        error = ex.Message,
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

        private SqlQueryData BuildTodaysCashbookSql(
            Ctx ctx,
            int cashBookId,
            int startRow,
            int endRow
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

            string queryParametersSql = @"
QueryParameters AS
(
    SELECT
        @AD_Client_ID AS AD_Client_ID,
        @AD_Language AS AD_Language,
        @C_CashBook_ID AS C_CashBook_ID,
        @StartRow AS StartRow,
        @EndRow AS EndRow"
        + queryParametersFrom + @"
)";

            string dateRangeSql = @"
DateRange AS
(
    SELECT
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

        TRIM
        (
            CAST
            (
                Currency.ISO_Code
                AS " + textType + @"
            )
        ) AS ISO_Code,

        CASE
            WHEN Currency.CurSymbol IS NOT NULL
            THEN
                TRIM
                (
                    CAST
                    (
                        Currency.CurSymbol
                        AS " + textType + @"
                    )
                )

            ELSE
                TRIM
                (
                    CAST
                    (
                        Currency.ISO_Code
                        AS " + textType + @"
                    )
                )
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

            string cashLineAccessSql = @"
SELECT
    CashLine.C_CashLine_ID,
    CashLine.C_Cash_ID,
    CashLine.Created,
    CashLine.CreatedBy,
    CashLine.Description,
    CashLine.Amount,
    CashLine.C_Charge_ID,
    CashLine.CashType

FROM C_CashLine CashLine

WHERE CashLine.IsActive = 'Y'";

            cashLineAccessSql =
                MRole.GetDefault(ctx)
                    .AddAccessSQL(
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

            string cashTypeReferenceSql = @"
CashTypeReference AS
(
    SELECT DISTINCT
        TRIM
        (
            CAST
            (
                RefList.Value
                AS " + textType + @"
            )
        ) AS ReferenceValue,

        TRIM
        (
            CAST
            (
                RefList.Name
                AS " + textType + @"
            )
        ) AS BaseName,

        TRIM
        (
            CAST
            (
                RefListTrl.Name
                AS " + textType + @"
            )
        ) AS TranslatedName

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

    WHERE TableInfo.TableName = 'C_CashLine'

    AND ColumnInfo.ColumnName = 'CashType'

    AND TableInfo.IsActive = 'Y'

    AND ColumnInfo.IsActive = 'Y'

    AND ReferenceInfo.IsActive = 'Y'

    AND RefList.IsActive = 'Y'
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

            string cashRowsSql = @"
CashRows AS
(
    SELECT
        CashLine.C_CashLine_ID,
        CashLine.Created,
        CashLine.Description,
        CashLine.Amount,

        TRIM
        (
            CAST
            (
                CashLine.CashType
                AS " + textType + @"
            )
        ) AS CashTypeValue,

        CashTypeReference.BaseName
            AS CashTypeBaseName,

        CashTypeReference.TranslatedName
            AS CashTypeTranslatedName,

        ROUND
        (
            CAST
            (
                " + convertedAmountExpression + @"
                AS " + numericType + @"
            ),

            CAST
            (
                COALESCE
                (
                    SchemaCurrency.StdPrecision,
                    2
                )
                AS INTEGER
            )
        ) AS ConvertedAmount,

        CashHeader.C_CashBook_ID,

        CashHeader.DocumentNo
            AS DocumentNo,

        CashBook.Name
            AS CashBookName,

        Charge.Name
            AS CategoryName,

        CreatedByUser.Name
            AS PostedBy,

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
        CAST
        (
            CashHeader.StatementDate
            AS TIMESTAMP
        ) >=
        DateRange.TodayStart

        AND CAST
        (
            CashHeader.StatementDate
            AS TIMESTAMP
        ) <
        DateRange.TodayEnd
    )

    LEFT OUTER JOIN CashTypeReference CashTypeReference ON
    (
        CashTypeReference.ReferenceValue =
        TRIM
        (
            CAST
            (
                CashLine.CashType
                AS " + textType + @"
            )
        )
    )

    LEFT OUTER JOIN C_Charge Charge ON
    (
        CashLine.C_Charge_ID =
        Charge.C_Charge_ID
    )

    LEFT OUTER JOIN AD_User CreatedByUser ON
    (
        CashLine.CreatedBy =
        CreatedByUser.AD_User_ID
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

    AND
    (
        (
            SELECT
                QueryParameters.C_CashBook_ID

            FROM QueryParameters QueryParameters
        ) <= 0

        OR CashHeader.C_CashBook_ID =
        (
            SELECT
                QueryParameters.C_CashBook_ID

            FROM QueryParameters QueryParameters
        )
    )
),
CountData AS
(
    SELECT
        COUNT(1) AS TotalRecords

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
        CashRows.DocumentNo,
        CashRows.CashBookName,
        CashRows.CategoryName,
        CashRows.CashTypeValue,
        CashRows.CashTypeBaseName,
        CashRows.CashTypeTranslatedName,
        CashRows.PostedBy,
        CashRows.StdPrecision,
        CashRows.C_Currency_ID,
        CashRows.CurrencyISO,
        CashRows.CurrencySymbol,
        CountData.TotalRecords,

        ROW_NUMBER() OVER
        (
            ORDER BY
                CashRows.Created DESC,
                CashRows.C_CashLine_ID DESC
        ) AS RowNo

    FROM CashRows CashRows

    INNER JOIN CountData CountData ON
    (
        1 = 1
    )
)";

            string sql = @"
WITH
" + queryParametersSql + @",
" + dateRangeSql + @",
" + schemaCurrencySql + @",
" + cashLineAccessSqlCte + @",
" + cashTypeReferenceSql + @",
" + cashRowsSql + @"
SELECT
    PagedRows.C_CashLine_ID,
    PagedRows.Created,
    PagedRows.Description,
    PagedRows.ConvertedAmount,
    PagedRows.C_CashBook_ID,
    PagedRows.DocumentNo,
    PagedRows.CashBookName,
    PagedRows.CategoryName,
    PagedRows.CashTypeValue,
    PagedRows.CashTypeBaseName,
    PagedRows.CashTypeTranslatedName,
    PagedRows.PostedBy,
    PagedRows.StdPrecision,
    PagedRows.C_Currency_ID,
    PagedRows.CurrencyISO,
    PagedRows.CurrencySymbol,
    PagedRows.TotalRecords

FROM PagedRows PagedRows

WHERE PagedRows.RowNo >=
(
    SELECT
        QueryParameters.StartRow

    FROM QueryParameters QueryParameters
)

AND PagedRows.RowNo <=
(
    SELECT
        QueryParameters.EndRow

    FROM QueryParameters QueryParameters
)

ORDER BY
    PagedRows.RowNo";

            SqlParameter[] parameters =
                new SqlParameter[]
                {
                    new SqlParameter(
                        "@AD_Client_ID",
                        ctx.GetAD_Client_ID()
                    ),

                    new SqlParameter(
                        "@AD_Language",
                        ctx.GetAD_Language()
                    ),

                    new SqlParameter(
                        "@C_CashBook_ID",
                        cashBookId
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

        private string FormatTime(
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
                return dateValue.ToString(
                    "HH:mm"
                );
            }

            return string.Empty;
        }

        private string GetDescription(
            Ctx ctx,
            IDataReader reader
        )
        {
            string description =
                GetString(
                    reader,
                    "Description"
                );

            return string.IsNullOrEmpty(
                description
            )
                ? GetMsg(
                    ctx,
                    "VAS_052_CashEntry",
                    "Cash entry"
                )
                : description;
        }

        private string GetCategory(
            Ctx ctx,
            IDataReader reader
        )
        {
            string category =
                GetString(
                    reader,
                    "CategoryName"
                );

            return string.IsNullOrEmpty(
                category
            )
                ? GetMsg(
                    ctx,
                    "VAS_052_Other",
                    "Other"
                )
                : category;
        }

        private string GetCashType(
            IDataReader reader
        )
        {
            return FirstNotEmpty(
                GetString(
                    reader,
                    "CashTypeTranslatedName"
                ),
                GetString(
                    reader,
                    "CashTypeBaseName"
                ),
                GetString(
                    reader,
                    "CashTypeValue"
                )
            );
        }

        private string FirstNotEmpty(
            params string[] values
        )
        {
            if (values == null)
            {
                return string.Empty;
            }

            for (
                int index = 0;
                index < values.Length;
                index++
            )
            {
                if (
                    !string.IsNullOrWhiteSpace(
                        values[index]
                    )
                )
                {
                    return values[index];
                }
            }

            return string.Empty;
        }

        private string GetCategoryClass(
            IDataReader reader
        )
        {
            string category =
                GetString(
                    reader,
                    "CategoryName"
                ).ToLowerInvariant();

            if (category.Contains("office"))
            {
                return "office";
            }

            if (category.Contains("travel"))
            {
                return "travel";
            }

            if (
                category.Contains("ar") ||
                category.Contains("receivable") ||
                category.Contains("customer")
            )
            {
                return "ar";
            }

            return "other";
        }

        private string GetPostedBy(
            Ctx ctx,
            IDataReader reader
        )
        {
            string postedBy =
                GetString(
                    reader,
                    "PostedBy"
                );

            return string.IsNullOrEmpty(
                postedBy
            )
                ? GetMsg(
                    ctx,
                    "VAS_052_System",
                    "System"
                )
                : postedBy;
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
                string.IsNullOrEmpty(msg) ||
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

