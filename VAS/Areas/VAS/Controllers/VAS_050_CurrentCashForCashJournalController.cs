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
    /// Purpose     : Provides latest cash book ending balance
    ///               for Current Cash dashboard widget.
    /// </summary>
    public class VAS_050_CurrentCashForCashJournalController : Controller
    {
        /// <summary>
        /// Gets latest ending balance for selected Cash Book / Drawer.
        /// </summary>
        /// <param name="cashBookId">
        /// Selected C_CashBook_ID.
        /// Pass 0 to use the first accessible drawer
        /// with a latest cash journal.
        /// </param>
        /// <returns>JSON response for Current Cash widget.</returns>
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
                        error = "VAS_050_SessionExpired",
                        hasData = false
                    },
                    JsonRequestBehavior.AllowGet
                );
            }

            IDataReader reader =
                null;

            IDataReader cashBookReader =
                null;

            try
            {
                List<object> cashBooks =
                    new List<object>();

                /*
                 * Apply MRole only to the physical C_CashBook query.
                 * Append ORDER BY after MRole is applied.
                 */
                string cashBookAccessSql = @"
SELECT
    CashBook.C_CashBook_ID,
    CashBook.Name

FROM C_CashBook CashBook

WHERE CashBook.IsActive = 'Y'

AND CashBook.AD_Client_ID =
    @ListClientID";

                cashBookAccessSql =
                    MRole.GetDefault(ctx)
                        .AddAccessSQL(
                            cashBookAccessSql,
                            "CashBook",
                            MRole.SQL_FULLYQUALIFIED,
                            MRole.SQL_RO
                        );

                string cashBookListSql =
                    cashBookAccessSql + @"

ORDER BY
    CashBook.Name,
    CashBook.C_CashBook_ID";

                SqlParameter[] cashBookParameters =
                    new SqlParameter[]
                    {
                        new SqlParameter(
                            "@ListClientID",
                            ctx.GetAD_Client_ID()
                        )
                    };

                cashBookReader =
                    DB.ExecuteReader(
                        cashBookListSql,
                        cashBookParameters,
                        null
                    );

                while (
                    cashBookReader != null &&
                    cashBookReader.Read()
                )
                {
                    cashBooks.Add(
                        new
                        {
                            cCashBookId =
                                GetInt(
                                    cashBookReader,
                                    "C_CashBook_ID"
                                ),

                            name =
                                GetString(
                                    cashBookReader,
                                    "Name"
                                )
                        }
                    );
                }

                if (cashBookReader != null)
                {
                    cashBookReader.Close();
                    cashBookReader.Dispose();
                    cashBookReader = null;
                }

                /*
                 * Oracle requires FROM DUAL for a parameter-only SELECT.
                 * PostgreSQL does not require a FROM clause.
                 */
                string queryParametersFrom =
                    DB.IsOracle()
                        ? " FROM DUAL"
                        : string.Empty;

                /*
                 * PostgreSQL requires NUMERIC as the first argument
                 * of ROUND(value, precision).
                 *
                 * Oracle uses NUMBER.
                 */
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
                 * Apply MRole only to the main physical C_Cash table.
                 *
                 * Do not apply MRole to:
                 * - the final WITH query
                 * - ProtectedCash CTE
                 * - RankedCash CTE
                 * - secondary aliases
                 */
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
)";

                protectedCashSql =
                    MRole.GetDefault(ctx)
                        .AddAccessSQL(
                            protectedCashSql,
                            "CashHeader",
                            MRole.SQL_FULLYQUALIFIED,
                            MRole.SQL_RO
                        );

                string protectedCashCteSql = @"
ProtectedCash AS
(
" + protectedCashSql + @"
)";

                string convertedEndingBalanceExpression = @"
CASE
    WHEN CashBook.C_Currency_ID =
        SchemaCurrency.C_Currency_ID

    THEN
        COALESCE
        (
            ProtectedCash.EndingBalance,
            0
        )

    ELSE
        CurrencyConvert
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

                /*
                 * ROW_NUMBER is compatible with Oracle and PostgreSQL.
                 *
                 * It replaces the correlated NOT EXISTS lookup and
                 * deterministically selects the latest journal per Cash Book.
                 */
                string rankedCashSql = @"
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
                " + convertedEndingBalanceExpression + @"
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

        SchemaCurrency.C_Currency_ID
            AS C_Currency_ID,

        SchemaCurrency.ISO_Code
            AS CurrencyISO,

        SchemaCurrency.Cur_Symbol
            AS CurrencySymbol,

        ROW_NUMBER() OVER
        (
            PARTITION BY
                ProtectedCash.C_CashBook_ID

            ORDER BY
                ProtectedCash.StatementDate DESC,
                ProtectedCash.C_Cash_ID DESC
        ) AS RowNumber

    FROM ProtectedCash ProtectedCash

    INNER JOIN C_CashBook CashBook ON
    (
        ProtectedCash.C_CashBook_ID =
        CashBook.C_CashBook_ID
    )

    INNER JOIN SchemaCurrency SchemaCurrency ON
    (
        SchemaCurrency.AD_Client_ID =
        ProtectedCash.AD_Client_ID
    )

    WHERE CashBook.IsActive = 'Y'

    AND CashBook.AD_Client_ID =
        ProtectedCash.AD_Client_ID

    AND
    (
        (
            SELECT
                QueryParameters.C_CashBook_ID

            FROM QueryParameters QueryParameters
        ) <= 0

        OR CashBook.C_CashBook_ID =
        (
            SELECT
                QueryParameters.C_CashBook_ID

            FROM QueryParameters QueryParameters
        )
    )
)";

                string latestCashSql = @"
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
        RankedCash.CashBookName,
        RankedCash.CurrentBalance,
        RankedCash.StdPrecision,
        RankedCash.C_Currency_ID,
        RankedCash.CurrencyISO,
        RankedCash.CurrencySymbol

    FROM RankedCash RankedCash

    WHERE RankedCash.RowNumber = 1
)";

                string sql = @"
WITH
" + queryParametersSql + @",
" + schemaCurrencySql + @",
" + protectedCashCteSql + @",
" + rankedCashSql + @",
" + latestCashSql + @"
SELECT
    LatestCash.C_Cash_ID,
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

FROM LatestCash LatestCash

ORDER BY
    LatestCash.CashBookName,
    LatestCash.C_CashBook_ID";

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

                reader =
                    DB.ExecuteReader(
                        sql,
                        parameters,
                        null
                    );

                int selectedCashBookId =
                    0;

                int cashId =
                    0;

                int stdPrecision =
                    2;

                int currencyId =
                    0;

                decimal currentBalance =
                    0;

                string cashBookName =
                    string.Empty;

                string documentNo =
                    string.Empty;

                string statementDate =
                    string.Empty;

                string currencyISO =
                    string.Empty;

                string currencySymbol =
                    string.Empty;

                bool selectedRowRead =
                    false;

                while (
                    reader != null &&
                    reader.Read()
                )
                {
                    if (!selectedRowRead)
                    {
                        selectedRowRead =
                            true;

                        selectedCashBookId =
                            GetInt(
                                reader,
                                "C_CashBook_ID"
                            );

                        cashId =
                            GetInt(
                                reader,
                                "C_Cash_ID"
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

                        statementDate =
                            FormatDbDate(
                                reader["StatementDate"]
                            );

                        currentBalance =
                            GetDecimal(
                                reader,
                                "CurrentBalance"
                            );

                        stdPrecision =
                            GetInt(
                                reader,
                                "StdPrecision",
                                2
                            );

                        currencyId =
                            GetInt(
                                reader,
                                "C_Currency_ID"
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
                    }
                }

                if (!selectedRowRead)
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
                                "Current cash"
                            ),

                            mainMetric = 0,
                            mainMetricText = "0",
                            footerAmount = 0,

                            description = GetMsg(
                                ctx,
                                "VAS_050_NoCashLeft",
                                "no cash left"
                            ),

                            badgeText = GetMsg(
                                ctx,
                                "VAS_050_Live",
                                "Live"
                            ),

                            cashBooks =
                                cashBooks
                        },
                        JsonRequestBehavior.AllowGet
                    );
                }

                decimal footerAmount =
                    Math.Abs(
                        currentBalance
                    );

                return Json(
                    new
                    {
                        success = true,
                        error = string.Empty,

                        title = GetMsg(
                            ctx,
                            "VAS_050_CurrentCash",
                            "Current cash"
                        ),

                        mainMetric =
                            currentBalance,

                        mainMetricText =
                            currentBalance.ToString(),

                        footerAmount =
                            footerAmount,

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
                            cashId,

                        cCashBookId =
                            selectedCashBookId,

                        cashBookName =
                            cashBookName,

                        documentNo =
                            documentNo,

                        statementDate =
                            statementDate,

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

                        hasData = true
                    },
                    JsonRequestBehavior.AllowGet
                );
            }
            catch (Exception ex)
            {
                VLogger.Get().SaveError(
                    "GetCurrentCash",
                    ex
                );

                return Json(
                    new
                    {
                        success = false,

                        error = GetMsg(
                            ctx,
                            "VAS_050_LoadError",
                            "Unable to load current cash"
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
        private string FormatDate(
            DateTime date
        )
        {
            return date.ToString(
                "yyyy-MM-dd"
            );
        }

        /// <summary>
        /// Gets translated message with fallback.
        /// </summary>
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
                )
                ||
                msg == key
                ||
                msg == "[" + key + "]"
            )
            {
                return fallback;
            }

            return msg;
        }

        /// <summary>
        /// Gets status message for current cash balance.
        /// </summary>
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
                    "short of float"
                );
            }

            if (currentBalance > 0)
            {
                return GetMsg(
                    ctx,
                    "VAS_050_CashOnHand",
                    "cash on hand"
                );
            }

            return GetMsg(
                ctx,
                "VAS_050_NoCashLeft",
                "no cash left"
            );
        }

        /// <summary>
        /// Safely reads decimal value from data reader.
        /// </summary>
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

        /// <summary>
        /// Safely reads integer value from data reader.
        /// </summary>
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

        /// <summary>
        /// Safely reads string value from data reader.
        /// </summary>
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

        /// <summary>
        /// Safely formats a database date value.
        /// </summary>
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
    }
}
