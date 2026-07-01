
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
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
    /// Purpose     : Provides the latest cash journal ending balance
    ///               and its related cash journal lines.
    /// </summary>
    public class VAS_050_CurrentCashForCashJournalController : Controller
    {
        /// <summary>
        /// Returns the ending balance of the latest accessible cash journal.
        ///
        /// Latest means:
        /// 1. Highest StatementDate.
        /// 2. Highest C_Cash_ID when more than one record has the same date.
        ///
        /// When cashBookId is greater than zero, the latest journal is selected
        /// only from that Cash Book.
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetCurrentCash(
            int cashBookId = 0
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
                        error = GetMsg(
                            Env.GetCtx(),
                            "VAS_050_SessionExpired",
                            "Session Expired"
                        ),
                        hasData = false
                    },
                    JsonRequestBehavior.AllowGet
                );
            }

            IDataReader reader = null;
            IDataReader cashBookReader = null;

            try
            {
                List<object> cashBooks =
                    ReadCashBooks(
                        ctx,
                        ref cashBookReader
                    );

                SqlQueryData queryData =
                    BuildCurrentCashSql(
                        ctx,
                        cashBookId
                    );

                reader =
                    DB.ExecuteReader(
                        queryData.Sql,
                        queryData.Parameters,
                        null
                    );

                if (
                    reader == null ||
                    !reader.Read()
                )
                {
                    return Json(
                        new
                        {
                            success = true,
                            error = string.Empty,
                            hasData = false,

                            title = GetMsg(
                                ctx,
                                "VAS_050_CurrentCash",
                                "Current Cash"
                            ),

                            mainMetric = 0,
                            mainMetricText = "0",
                            footerAmount = 0,

                            description = GetMsg(
                                ctx,
                                "VAS_050_NoCashLeft",
                                "No Cash Left"
                            ),

                            badgeText = GetMsg(
                                ctx,
                                "VAS_050_Live",
                                "Live"
                            ),

                            cCashId = 0,
                            cCashBookId = 0,
                            cashBookName = string.Empty,
                            documentNo = string.Empty,
                            statementDate = string.Empty,
                            dateAcct = string.Empty,

                            currencyISO = string.Empty,
                            currencySymbol = string.Empty,
                            cCurrencyId = 0,
                            stdPrecision = 2,

                            cashBooks = cashBooks
                        },
                        JsonRequestBehavior.AllowGet
                    );
                }

                int stdPrecision =
                    NormalizePrecision(
                        GetInt(
                            reader,
                            "StdPrecision",
                            2
                        )
                    );

                decimal currentBalance =
                    GetRoundedDecimal(
                        reader,
                        "CurrentBalance",
                        stdPrecision
                    );

                DateTime? statementDate =
                    GetDate(
                        reader,
                        "StatementDate"
                    );

                DateTime? dateAcct =
                    GetDate(
                        reader,
                        "DateAcct"
                    );

                return Json(
                    new
                    {
                        success = true,
                        error = string.Empty,
                        hasData = true,

                        title = GetMsg(
                            ctx,
                            "VAS_050_CurrentCash",
                            "Current Cash"
                        ),

                        mainMetric =
                            currentBalance,

                        mainMetricText =
                            currentBalance.ToString(
                                "F" + stdPrecision,
                                CultureInfo.InvariantCulture
                            ),

                        footerAmount =
                            Math.Abs(
                                currentBalance
                            ),

                        description =
                            GetCurrentCashDescription(
                                ctx,
                                currentBalance
                            ),

                        badgeText = GetMsg(
                            ctx,
                            "VAS_050_Live",
                            "Live"
                        ),

                        cCashId =
                            GetInt(
                                reader,
                                "C_Cash_ID"
                            ),

                        cCashBookId =
                            GetInt(
                                reader,
                                "C_CashBook_ID"
                            ),

                        cashBookName =
                            GetString(
                                reader,
                                "CashBookName"
                            ),

                        documentNo =
                            GetString(
                                reader,
                                "DocumentNo"
                            ),

                        statementDate =
                            statementDate.HasValue
                                ? FormatDate(
                                    statementDate.Value
                                )
                                : string.Empty,

                        dateAcct =
                            dateAcct.HasValue
                                ? FormatDate(
                                    dateAcct.Value
                                )
                                : string.Empty,

                        currencyISO =
                            GetString(
                                reader,
                                "CurrencyISO"
                            ),

                        currencySymbol =
                            GetString(
                                reader,
                                "CurrencySymbol"
                            ),

                        cCurrencyId =
                            GetInt(
                                reader,
                                "C_Currency_ID"
                            ),

                        stdPrecision =
                            stdPrecision,

                        cashBooks =
                            cashBooks
                    },
                    JsonRequestBehavior.AllowGet
                );
            }
            catch (Exception ex)
            {
                VLogger.Get().SaveError(
                    "VAS_050_GetCurrentCash",
                    ex
                );

                return Json(
                    new
                    {
                        success = false,

                        error = GetMsg(
                            ctx,
                            "VAS_050_LoadError",
                            "Unable To Load Current Cash"
                        ),

                        hasData = false
                    },
                    JsonRequestBehavior.AllowGet
                );
            }
            finally
            {
                CloseReader(
                    reader
                );

                CloseReader(
                    cashBookReader
                );
            }
        }

        /// <summary>
        /// Returns one page of C_CashLine records belonging to the same
        /// latest C_Cash record displayed by GetCurrentCash.
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetCurrentCashRows(
            int cashBookId,
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
                        error = GetMsg(
                            Env.GetCtx(),
                            "VAS_050_SessionExpired",
                            "Session Expired"
                        ),
                        hasData = false
                    },
                    JsonRequestBehavior.AllowGet
                );
            }

            pageNo =
                Math.Max(
                    pageNo,
                    1
                );

            pageSize =
                Math.Max(
                    1,
                    Math.Min(
                        pageSize,
                        50
                    )
                );

            IDataReader reader = null;

            try
            {
                SqlQueryData queryData =
                    BuildCurrentCashRowsSql(
                        ctx,
                        cashBookId,
                        pageNo,
                        pageSize
                    );

                reader =
                    DB.ExecuteReader(
                        queryData.Sql,
                        queryData.Parameters,
                        null
                    );

                List<object> rows =
                    new List<object>();

                int totalRecords = 0;
                int stdPrecision = 2;
                decimal totalAmount = 0;

                int selectedCashId = 0;
                int selectedCashBookId = 0;

                string cashBookName =
                    string.Empty;

                string documentNo =
                    string.Empty;

                string selectedStatementDate =
                    string.Empty;

                string selectedDateAcct =
                    string.Empty;

                while (
                    reader != null &&
                    reader.Read()
                )
                {
                    totalRecords =
                        GetInt(
                            reader,
                            "TotalRecords"
                        );

                    stdPrecision =
                        NormalizePrecision(
                            GetInt(
                                reader,
                                "StdPrecision",
                                2
                            )
                        );

                    totalAmount =
                        GetRoundedDecimal(
                            reader,
                            "TotalAmount",
                            stdPrecision
                        );

                    selectedCashId =
                        GetInt(
                            reader,
                            "C_Cash_ID"
                        );

                    selectedCashBookId =
                        GetInt(
                            reader,
                            "C_CashBook_ID"
                        );

                    cashBookName =
                        GetString(
                            reader,
                            "CashBookName"
                        );

                    documentNo =
                        GetString(
                            reader,
                            "DocumentNo"
                        );

                    DateTime? statementDate =
                        GetDate(
                            reader,
                            "StatementDate"
                        );

                    DateTime? dateAcct =
                        GetDate(
                            reader,
                            "DateAcct"
                        );

                    selectedStatementDate =
                        statementDate.HasValue
                            ? FormatDate(
                                statementDate.Value
                            )
                            : string.Empty;

                    selectedDateAcct =
                        dateAcct.HasValue
                            ? FormatDate(
                                dateAcct.Value
                            )
                            : string.Empty;

                    int cashLineId =
                        GetInt(
                            reader,
                            "C_CashLine_ID"
                        );

                    if (cashLineId <= 0)
                    {
                        continue;
                    }

                    string cashTypeValue =
                        GetString(
                            reader,
                            "CashTypeValue"
                        );

                    string cashTypeName =
                        FirstNotEmpty(
                            GetString(
                                reader,
                                "CashTypeTranslatedName"
                            ),

                            GetString(
                                reader,
                                "CashTypeBaseName"
                            ),

                            cashTypeValue
                        );

                    string docStatusValue =
                        GetString(
                            reader,
                            "DocStatus"
                        );

                    string docStatusName =
                        FirstNotEmpty(
                            GetString(
                                reader,
                                "StatusTranslatedName"
                            ),

                            GetString(
                                reader,
                                "StatusBaseName"
                            ),

                            docStatusValue
                        );

                    rows.Add(
                        new
                        {
                            cCashId =
                                selectedCashId,

                            cCashBookId =
                                selectedCashBookId,

                            cCashLineId =
                                cashLineId,

                            documentNo =
                                documentNo,

                            statementDate =
                                selectedStatementDate,

                            dateAcct =
                                selectedDateAcct,

                            description =
                                GetString(
                                    reader,
                                    "Description"
                                ),

                            amount =
                                GetRoundedDecimal(
                                    reader,
                                    "Amount",
                                    stdPrecision
                                ),

                            cashTypeValue =
                                cashTypeValue,

                            cashTypeName =
                                cashTypeName,

                            cChargeId =
                                GetInt(
                                    reader,
                                    "C_Charge_ID"
                                ),

                            chargeName =
                                GetString(
                                    reader,
                                    "ChargeName"
                                ),

                            cashBookName =
                                cashBookName,

                            docStatusValue =
                                docStatusValue,

                            docStatusName =
                                docStatusName,

                            currencyISO =
                                GetString(
                                    reader,
                                    "CurrencyISO"
                                ),

                            currencySymbol =
                                GetString(
                                    reader,
                                    "CurrencySymbol"
                                ),

                            stdPrecision =
                                stdPrecision
                        }
                    );
                }

                int totalPages =
                    totalRecords <= 0
                        ? 0
                        : Convert.ToInt32(
                            Math.Ceiling(
                                (decimal)totalRecords /
                                pageSize
                            )
                        );

                return Json(
                    new
                    {
                        success = true,
                        error = string.Empty,

                        title = GetMsg(
                            ctx,
                            "VAS_050_DialogTitle",
                            "Current Cash Details"
                        ),

                        rows =
                            rows,

                        pageNo =
                            pageNo,

                        pageSize =
                            pageSize,

                        totalRecords =
                            totalRecords,

                        totalPages =
                            totalPages,

                        totalAmount =
                            totalAmount,

                        stdPrecision =
                            stdPrecision,

                        cCashId =
                            selectedCashId,

                        cCashBookId =
                            selectedCashBookId,

                        cashBookName =
                            cashBookName,

                        documentNo =
                            documentNo,

                        statementDate =
                            selectedStatementDate,

                        dateAcct =
                            selectedDateAcct,

                        hasData =
                            totalRecords > 0
                    },
                    JsonRequestBehavior.AllowGet
                );
            }
            catch (Exception ex)
            {
                VLogger.Get().SaveError(
                    "VAS_050_GetCurrentCashRows",
                    ex
                );

                return Json(
                    new
                    {
                        success = false,

                        error = GetMsg(
                            ctx,
                            "VAS_050_LoadError",
                            "Could Not Load Data"
                        ),

                        hasData = false
                    },
                    JsonRequestBehavior.AllowGet
                );
            }
            finally
            {
                CloseReader(
                    reader
                );
            }
        }

        private List<object> ReadCashBooks(
            Ctx ctx,
            ref IDataReader reader
        )
        {
            List<object> result =
                new List<object>();

            string sql = @"
SELECT
    CashBook.C_CashBook_ID,
    CashBook.Name

FROM C_CashBook CashBook

WHERE CashBook.IsActive = 'Y'

AND CashBook.AD_Client_ID =
    @AD_Client_ID";

            sql =
                MRole.GetDefault(ctx)
                    .AddAccessSQL(
                        sql,
                        "CashBook",
                        MRole.SQL_FULLYQUALIFIED,
                        MRole.SQL_RO
                    );

            sql += @"

ORDER BY
    CashBook.Name,
    CashBook.C_CashBook_ID";

            SqlParameter[] parameters =
                new SqlParameter[]
                {
                    new SqlParameter(
                        "@AD_Client_ID",
                        ctx.GetAD_Client_ID()
                    )
                };

            reader =
                DB.ExecuteReader(
                    sql,
                    parameters,
                    null
                );

            while (
                reader != null &&
                reader.Read()
            )
            {
                result.Add(
                    new
                    {
                        cCashBookId =
                            GetInt(
                                reader,
                                "C_CashBook_ID"
                            ),

                        name =
                            GetString(
                                reader,
                                "Name"
                            )
                    }
                );
            }

            CloseReader(
                reader
            );

            reader = null;

            return result;
        }

        private SqlQueryData BuildCurrentCashSql(
            Ctx ctx,
            int cashBookId
        )
        {
            string queryParametersFrom =
                DB.IsOracle()
                    ? " FROM DUAL"
                    : string.Empty;

            string numericType =
                DB.IsOracle()
                    ? "NUMBER"
                    : "NUMERIC";

            string queryParametersSql = @"
QueryParameters AS
(
    SELECT
        @AD_Client_ID AS AD_Client_ID,
        @C_CashBook_ID AS C_CashBook_ID"
        + queryParametersFrom + @"
)";

            string schemaCurrencySql = @"
SchemaCurrency AS
(
    SELECT
        ClientInfo.AD_Client_ID,
        AcctSchema.C_Currency_ID,
        Currency.StdPrecision,
        Currency.ISO_Code,

        CASE
            WHEN Currency.CurSymbol IS NOT NULL
            THEN Currency.CurSymbol
            ELSE Currency.ISO_Code
        END AS CurSymbol

    FROM AD_ClientInfo ClientInfo

    INNER JOIN C_AcctSchema AcctSchema ON
    (
        AcctSchema.C_AcctSchema_ID =
        ClientInfo.C_AcctSchema1_ID
    )

    INNER JOIN C_Currency Currency ON
    (
        Currency.C_Currency_ID =
        AcctSchema.C_Currency_ID
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

            string protectedCashSql = @"
SELECT
    CashHeader.C_Cash_ID,
    CashHeader.AD_Client_ID,
    CashHeader.AD_Org_ID,
    CashHeader.C_CashBook_ID,
    CashHeader.DocumentNo,
    CashHeader.StatementDate,
    CashHeader.DateAcct,
    CashHeader.EndingBalance,
    CashHeader.DocStatus

FROM C_Cash CashHeader

WHERE CashHeader.IsActive = 'Y'

AND CashHeader.AD_Client_ID =
(
    SELECT
        QueryParameters.AD_Client_ID

    FROM QueryParameters QueryParameters
)

AND CashHeader.DocStatus IN
(
    'DR',
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
)";

            protectedCashSql =
                MRole.GetDefault(ctx)
                    .AddAccessSQL(
                        protectedCashSql,
                        "CashHeader",
                        MRole.SQL_FULLYQUALIFIED,
                        MRole.SQL_RO
                    );

            string convertedBalanceSql = @"
CASE
    WHEN CashBook.C_Currency_ID =
        SchemaCurrency.C_Currency_ID

    THEN COALESCE
    (
        ProtectedCash.EndingBalance,
        0
    )

    ELSE CurrencyConvert
    (
        COALESCE
        (
            ProtectedCash.EndingBalance,
            0
        ),
        CashBook.C_Currency_ID,
        SchemaCurrency.C_Currency_ID,
        ProtectedCash.DateAcct,
        0,
        ProtectedCash.AD_Client_ID,
        ProtectedCash.AD_Org_ID
    )
END";

            string sql = @"
WITH
" + queryParametersSql + @",
" + schemaCurrencySql + @",
ProtectedCash AS
(
" + protectedCashSql + @"
),
RankedCash AS
(
    SELECT
        ProtectedCash.C_Cash_ID,
        ProtectedCash.AD_Client_ID,
        ProtectedCash.AD_Org_ID,
        ProtectedCash.C_CashBook_ID,
        ProtectedCash.DocumentNo,
        ProtectedCash.StatementDate,
        ProtectedCash.DateAcct,

        CashBook.Name AS CashBookName,

        ROUND
        (
            CAST
            (
                " + convertedBalanceSql + @"
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
        ) AS CurrentBalance,

        COALESCE
        (
            SchemaCurrency.StdPrecision,
            2
        ) AS StdPrecision,

        SchemaCurrency.C_Currency_ID,

        SchemaCurrency.ISO_Code
            AS CurrencyISO,

        SchemaCurrency.CurSymbol
            AS CurrencySymbol,

        ROW_NUMBER() OVER
        (
            ORDER BY
                ProtectedCash.StatementDate DESC,
                ProtectedCash.C_Cash_ID DESC
        ) AS RowNumber

    FROM ProtectedCash ProtectedCash

    INNER JOIN C_CashBook CashBook ON
    (
        CashBook.C_CashBook_ID =
        ProtectedCash.C_CashBook_ID
    )

    INNER JOIN SchemaCurrency SchemaCurrency ON
    (
        SchemaCurrency.AD_Client_ID =
        ProtectedCash.AD_Client_ID
    )

    WHERE CashBook.IsActive = 'Y'

    AND CashBook.AD_Client_ID =
        ProtectedCash.AD_Client_ID
)
SELECT
    RankedCash.C_Cash_ID,
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

WHERE RankedCash.RowNumber = 1";

            SqlParameter[] parameters =
                new SqlParameter[]
                {
                    new SqlParameter(
                        "@AD_Client_ID",
                        ctx.GetAD_Client_ID()
                    ),

                    new SqlParameter(
                        "@C_CashBook_ID",
                        cashBookId
                    )
                };

            return new SqlQueryData
            {
                Sql = sql,
                Parameters = parameters
            };
        }

        private SqlQueryData BuildCurrentCashRowsSql(
            Ctx ctx,
            int cashBookId,
            int pageNo,
            int pageSize
        )
        {
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
                ((pageNo - 1) * pageSize) + 1;

            int endRow =
                pageNo * pageSize;

            string protectedCashSql = @"
SELECT
    CashHeader.C_Cash_ID,
    CashHeader.AD_Client_ID,
    CashHeader.AD_Org_ID,
    CashHeader.C_CashBook_ID,
    CashHeader.DocumentNo,
    CashHeader.StatementDate,
    CashHeader.DateAcct,
    CashHeader.EndingBalance,
    CashHeader.DocStatus

FROM C_Cash CashHeader

WHERE CashHeader.IsActive = 'Y'

AND CashHeader.AD_Client_ID =
(
    SELECT
        QueryParameters.AD_Client_ID

    FROM QueryParameters QueryParameters
)

AND CashHeader.DocStatus IN
(
    'DR',
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
)";

            protectedCashSql =
                MRole.GetDefault(ctx)
                    .AddAccessSQL(
                        protectedCashSql,
                        "CashHeader",
                        MRole.SQL_FULLYQUALIFIED,
                        MRole.SQL_RO
                    );

            string sql = @"
WITH QueryParameters AS
(
    SELECT
        @AD_Client_ID AS AD_Client_ID,
        @C_CashBook_ID AS C_CashBook_ID,
        @AD_Language AS AD_Language,
        @StartRow AS StartRow,
        @EndRow AS EndRow"
        + queryParametersFrom + @"
),
ProtectedCash AS
(
" + protectedCashSql + @"
),
RankedCash AS
(
    SELECT
        ProtectedCash.C_Cash_ID,
        ProtectedCash.AD_Client_ID,
        ProtectedCash.AD_Org_ID,
        ProtectedCash.C_CashBook_ID,
        ProtectedCash.DocumentNo,
        ProtectedCash.StatementDate,
        ProtectedCash.DateAcct,
        ProtectedCash.DocStatus,

        ROW_NUMBER() OVER
        (
            ORDER BY
                ProtectedCash.StatementDate DESC,
                ProtectedCash.C_Cash_ID DESC
        ) AS RowNumber

    FROM ProtectedCash ProtectedCash
),
LatestCash AS
(
    SELECT
        RankedCash.C_Cash_ID,
        RankedCash.AD_Client_ID,
        RankedCash.AD_Org_ID,
        RankedCash.C_CashBook_ID,
        RankedCash.DocumentNo,
        RankedCash.StatementDate,
        RankedCash.DateAcct,
        RankedCash.DocStatus

    FROM RankedCash RankedCash

    WHERE RankedCash.RowNumber = 1
),
StatusReference AS
(
    SELECT DISTINCT
        TRIM
        (
            CAST
            (
                RefList.Value
                AS " + textType + @"
            )
        ) AS StatusValue,

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

        AND RefListTrl.IsActive = 'Y'
    )

    WHERE TableInfo.TableName = 'C_Cash'

    AND ColumnInfo.ColumnName = 'DocStatus'

    AND TableInfo.IsActive = 'Y'

    AND ColumnInfo.IsActive = 'Y'

    AND ReferenceInfo.IsActive = 'Y'

    AND RefList.IsActive = 'Y'
),
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
        ) AS CashTypeValue,

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

        AND RefListTrl.IsActive = 'Y'
    )

    WHERE TableInfo.TableName = 'C_CashLine'

    AND ColumnInfo.ColumnName = 'CashType'

    AND TableInfo.IsActive = 'Y'

    AND ColumnInfo.IsActive = 'Y'

    AND ReferenceInfo.IsActive = 'Y'

    AND RefList.IsActive = 'Y'
),
CashLineRows AS
(
    SELECT
        LatestCash.C_Cash_ID,
        LatestCash.C_CashBook_ID,
        LatestCash.DocumentNo,
        LatestCash.StatementDate,
        LatestCash.DateAcct,
        LatestCash.DocStatus,

        StatusReference.BaseName
            AS StatusBaseName,

        StatusReference.TranslatedName
            AS StatusTranslatedName,

        CashLine.C_CashLine_ID,
        CashLine.Description,
        CashLine.Amount,
        CashLine.C_Charge_ID,

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

        Charge.Name
            AS ChargeName,

        CashBook.Name
            AS CashBookName,

        Currency.ISO_Code
            AS CurrencyISO,

        Currency.CurSymbol
            AS CurrencySymbol,

        COALESCE
        (
            Currency.StdPrecision,
            2
        ) AS StdPrecision

    FROM LatestCash LatestCash

    INNER JOIN C_CashLine CashLine ON
    (
        CashLine.C_Cash_ID =
        LatestCash.C_Cash_ID
    )

    INNER JOIN C_CashBook CashBook ON
    (
        CashBook.C_CashBook_ID =
        LatestCash.C_CashBook_ID

        AND CashBook.IsActive = 'Y'
    )

    INNER JOIN C_Currency Currency ON
    (
        Currency.C_Currency_ID =
        CashBook.C_Currency_ID

        AND Currency.IsActive = 'Y'
    )

    LEFT OUTER JOIN C_Charge Charge ON
    (
        Charge.C_Charge_ID =
        CashLine.C_Charge_ID
    )

    LEFT OUTER JOIN StatusReference StatusReference ON
    (
        StatusReference.StatusValue =
        TRIM
        (
            CAST
            (
                LatestCash.DocStatus
                AS " + textType + @"
            )
        )
    )

    LEFT OUTER JOIN CashTypeReference CashTypeReference ON
    (
        CashTypeReference.CashTypeValue =
        TRIM
        (
            CAST
            (
                CashLine.CashType
                AS " + textType + @"
            )
        )
    )

    WHERE CashLine.IsActive = 'Y'
),
NumberedRows AS
(
    SELECT
        CashLineRows.C_Cash_ID,
        CashLineRows.C_CashBook_ID,
        CashLineRows.DocumentNo,
        CashLineRows.StatementDate,
        CashLineRows.DateAcct,
        CashLineRows.DocStatus,
        CashLineRows.StatusBaseName,
        CashLineRows.StatusTranslatedName,
        CashLineRows.C_CashLine_ID,
        CashLineRows.Description,
        CashLineRows.Amount,
        CashLineRows.C_Charge_ID,
        CashLineRows.CashTypeValue,
        CashLineRows.CashTypeBaseName,
        CashLineRows.CashTypeTranslatedName,
        CashLineRows.ChargeName,
        CashLineRows.CashBookName,
        CashLineRows.CurrencyISO,
        CashLineRows.CurrencySymbol,
        CashLineRows.StdPrecision,

        COUNT
        (
            1
        ) OVER () AS TotalRecords,

        ROUND
        (
            CAST
            (
                SUM
                (
                    COALESCE
                    (
                        CashLineRows.Amount,
                        0
                    )
                ) OVER ()
                AS " + numericType + @"
            ),

            CAST
            (
                CashLineRows.StdPrecision
                AS INTEGER
            )
        ) AS TotalAmount,

        ROW_NUMBER() OVER
        (
            ORDER BY
                CashLineRows.C_CashLine_ID
        ) AS RowNo

    FROM CashLineRows CashLineRows
)
SELECT
    NumberedRows.C_Cash_ID,
    NumberedRows.C_CashBook_ID,
    NumberedRows.DocumentNo,
    NumberedRows.StatementDate,
    NumberedRows.DateAcct,
    NumberedRows.DocStatus,
    NumberedRows.StatusBaseName,
    NumberedRows.StatusTranslatedName,
    NumberedRows.C_CashLine_ID,
    NumberedRows.Description,
    NumberedRows.Amount,
    NumberedRows.C_Charge_ID,
    NumberedRows.CashTypeValue,
    NumberedRows.CashTypeBaseName,
    NumberedRows.CashTypeTranslatedName,
    NumberedRows.ChargeName,
    NumberedRows.CashBookName,
    NumberedRows.CurrencyISO,
    NumberedRows.CurrencySymbol,
    NumberedRows.StdPrecision,
    NumberedRows.TotalRecords,
    NumberedRows.TotalAmount

FROM NumberedRows NumberedRows

WHERE NumberedRows.RowNo >=
(
    SELECT
        QueryParameters.StartRow

    FROM QueryParameters QueryParameters
)

AND NumberedRows.RowNo <=
(
    SELECT
        QueryParameters.EndRow

    FROM QueryParameters QueryParameters
)

ORDER BY
    NumberedRows.RowNo";

            SqlParameter[] parameters =
                new SqlParameter[]
                {
                    new SqlParameter(
                        "@AD_Client_ID",
                        ctx.GetAD_Client_ID()
                    ),

                    new SqlParameter(
                        "@C_CashBook_ID",
                        cashBookId
                    ),

                    new SqlParameter(
                        "@AD_Language",
                        ctx.GetAD_Language()
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
                Sql = sql,
                Parameters = parameters
            };
        }

        private int NormalizePrecision(
            int precision
        )
        {
            if (
                precision < 0 ||
                precision > 28
            )
            {
                return 2;
            }

            return precision;
        }

        private decimal GetRoundedDecimal(
            IDataReader reader,
            string columnName,
            int precision
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

            precision =
                NormalizePrecision(
                    precision
                );

            decimal decimalValue;

            string invariantText =
                Convert.ToString(
                    value,
                    CultureInfo.InvariantCulture
                );

            if (
                decimal.TryParse(
                    invariantText,
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out decimalValue
                )
            )
            {
                return Math.Round(
                    decimalValue,
                    precision,
                    MidpointRounding.AwayFromZero
                );
            }

            double doubleValue;

            if (
                double.TryParse(
                    invariantText,
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out doubleValue
                )
                &&
                !double.IsNaN(
                    doubleValue
                )
                &&
                !double.IsInfinity(
                    doubleValue
                )
            )
            {
                double roundedDouble =
                    Math.Round(
                        doubleValue,
                        precision,
                        MidpointRounding.AwayFromZero
                    );

                if (
                    roundedDouble <=
                    Convert.ToDouble(
                        decimal.MaxValue
                    )
                    &&
                    roundedDouble >=
                    Convert.ToDouble(
                        decimal.MinValue
                    )
                )
                {
                    return Convert.ToDecimal(
                        roundedDouble
                    );
                }
            }

            return 0;
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
                Convert.ToString(
                    value,
                    CultureInfo.InvariantCulture
                ),
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
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

            return Convert.ToString(
                value,
                CultureInfo.InvariantCulture
            );
        }

        private DateTime? GetDate(
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
                return null;
            }

            DateTime dateValue;

            if (
                value is DateTime
            )
            {
                return (DateTime)value;
            }

            return DateTime.TryParse(
                Convert.ToString(
                    value,
                    CultureInfo.InvariantCulture
                ),
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out dateValue
            )
                ? dateValue
                : (DateTime?)null;
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

        private string FormatDate(
            DateTime date
        )
        {
            return date.ToString(
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture
            );
        }

        private string GetCurrentCashDescription(
            Ctx ctx,
            decimal currentBalance
        )
        {
            if (currentBalance < 0)
            {
                return GetMsg(
                    ctx,
                    "VAS_050_ShortOfFloat",
                    "Short Of Float"
                );
            }

            if (currentBalance > 0)
            {
                return GetMsg(
                    ctx,
                    "VAS_050_CashOnHand",
                    "Cash On Hand"
                );
            }

            return GetMsg(
                ctx,
                "VAS_050_NoCashLeft",
                "No Cash Left"
            );
        }

        private string GetMsg(
            Ctx ctx,
            string key,
            string fallback
        )
        {
            string message =
                Msg.GetMsg(
                    ctx,
                    key
                );

            if (
                string.IsNullOrEmpty(
                    message
                )
                ||
                message == key
                ||
                message == "[" + key + "]"
            )
            {
                return fallback;
            }

            return message;
        }

        private void CloseReader(
            IDataReader reader
        )
        {
            if (reader == null)
            {
                return;
            }

            reader.Close();
            reader.Dispose();
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