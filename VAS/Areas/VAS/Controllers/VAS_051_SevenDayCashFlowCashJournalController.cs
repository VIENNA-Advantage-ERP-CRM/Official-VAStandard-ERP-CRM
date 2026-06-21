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
    /// Purpose     : Provides last 7 days cash flow values for Cash Journal dashboard widget.
    /// </summary>
    public class VAS_051_SevenDayCashFlowCashJournalController : Controller
    {
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetSevenDayCashFlow()
        {
            Ctx ctx = Session["ctx"] as Ctx;

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

            IDataReader reader = null;

            try
            {
                SqlQueryData queryData =
                    BuildSevenDayCashFlowSql(ctx);

                reader = DB.ExecuteReader(
                    queryData.Sql,
                    queryData.Parameters,
                    null
                );

                List<object> items =
                    new List<object>();

                int stdPrecision = 2;
                int currencyId = 0;

                string currencyISO =
                    string.Empty;

                string currencySymbol =
                    string.Empty;

                decimal totalCashIn = 0;
                decimal totalCashOut = 0;
                decimal totalNet = 0;

                int totalLineCount = 0;

                DateTime? dateFrom = null;
                DateTime? dateTo = null;

                while (
                    reader != null &&
                    reader.Read()
                )
                {
                    DateTime cashFlowDateValue =
                        Util.GetValueOfDateTime(
                            reader["CashFlowDate"]
                        ) ?? DateTime.Now;

                    string cashFlowDate =
                        FormatDate(
                            cashFlowDateValue
                        );

                    string dayLabel =
                        cashFlowDateValue.ToString(
                            "ddd"
                        );

                    decimal cashInAmount =
                        GetDecimal(
                            reader,
                            "CashInAmount"
                        );

                    decimal cashOutAmount =
                        GetDecimal(
                            reader,
                            "CashOutAmount"
                        );

                    decimal netAmount =
                        GetDecimal(
                            reader,
                            "NetAmount"
                        );

                    int lineCount =
                        GetInt(
                            reader,
                            "LineCount"
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

                    if (!dateFrom.HasValue)
                    {
                        dateFrom =
                            cashFlowDateValue;
                    }

                    dateTo =
                        cashFlowDateValue;

                    totalCashIn +=
                        cashInAmount;

                    totalCashOut +=
                        cashOutAmount;

                    totalNet +=
                        netAmount;

                    totalLineCount +=
                        lineCount;

                    items.Add(
                        new
                        {
                            date =
                                cashFlowDate,

                            dayLabel =
                                dayLabel,

                            cashInAmount =
                                cashInAmount,

                            cashOutAmount =
                                cashOutAmount,

                            netAmount =
                                netAmount,

                            lineCount =
                                lineCount,

                            currencyISO =
                                currencyISO,

                            currencySymbol =
                                currencySymbol,

                            stdPrecision =
                                stdPrecision,

                            tooltip =
                                dayLabel +
                                " " +
                                cashFlowDate +
                                " | In: " +
                                cashInAmount.ToString() +
                                " | Out: " +
                                cashOutAmount.ToString() +
                                " | Net: " +
                                netAmount.ToString()
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
                            "VAS_051_SevenDayCashFlow",
                            "7-Day Cash Flow"
                        ),

                        metaText = GetMsg(
                            ctx,
                            "VAS_051_Last7Days",
                            "Last 7 days"
                        ),

                        dateFrom =
                            dateFrom.HasValue
                                ? FormatDate(
                                    dateFrom.Value
                                )
                                : "",

                        dateTo =
                            dateTo.HasValue
                                ? FormatDate(
                                    dateTo.Value
                                )
                                : "",

                        currencyISO =
                            currencyISO,

                        currencySymbol =
                            currencySymbol,

                        cCurrencyId =
                            currencyId,

                        stdPrecision =
                            stdPrecision,

                        items =
                            items,

                        totalCashIn =
                            totalCashIn,

                        totalCashOut =
                            totalCashOut,

                        totalNet =
                            totalNet,

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
                            "VAS_051_LoadError",
                            "Unable to load seven day cash flow"
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

        private SqlQueryData BuildSevenDayCashFlowSql(
            Ctx ctx
        )
        {
            string todayDateSql =
                GetTodayDateSql();

            string cashFlowDateSql =
                GetDateOnlySql(
                    "CashFlowDays.CashFlowDate"
                );

            /*
             * Oracle requires FROM DUAL for a parameter-only SELECT.
             * PostgreSQL does not require a FROM clause.
             */
            string queryParametersFrom =
                DB.IsOracle()
                    ? " FROM DUAL"
                    : string.Empty;

            /*
             * PostgreSQL requires NUMERIC for:
             * ROUND(value, precision)
             *
             * Oracle uses NUMBER.
             */
            string numericType =
                DB.IsOracle()
                    ? "NUMBER"
                    : "NUMERIC";

            /*
             * The bind variable appears once only.
             * All other CTEs read AD_Client_ID from QueryParameters.
             */
            string queryParametersSql = @"
QueryParameters AS
(
    SELECT
        @AD_Client_ID AS AD_Client_ID"
        + queryParametersFrom + @"
)";

            string dateRangeSql = @"
DateRange AS
(
    SELECT
        " + todayDateSql + @" AS TodayDate,

        CAST
        (
            " + todayDateSql + @" - 6
            AS TIMESTAMP
        ) AS DateFrom,

        CAST
        (
            " + todayDateSql + @" + 1
            AS TIMESTAMP
        ) AS DateTo

    FROM QueryParameters QueryParameters
)";

            /*
             * Creates seven rows without database-specific
             * CONNECT BY or GENERATE_SERIES syntax.
             */
            string dateSeedSql = @"
DateSeed AS
(
    SELECT
        0 AS DayOffset

    FROM QueryParameters QueryParameters

    UNION ALL

    SELECT
        1 AS DayOffset

    FROM QueryParameters QueryParameters

    UNION ALL

    SELECT
        2 AS DayOffset

    FROM QueryParameters QueryParameters

    UNION ALL

    SELECT
        3 AS DayOffset

    FROM QueryParameters QueryParameters

    UNION ALL

    SELECT
        4 AS DayOffset

    FROM QueryParameters QueryParameters

    UNION ALL

    SELECT
        5 AS DayOffset

    FROM QueryParameters QueryParameters

    UNION ALL

    SELECT
        6 AS DayOffset

    FROM QueryParameters QueryParameters
)";

            string cashFlowDaysSql = @"
CashFlowDays AS
(
    SELECT
        CAST
        (
            DateRange.TodayDate -
            (
                6 -
                DateSeed.DayOffset
            )
            AS DATE
        ) AS CashFlowDate,

        CAST
        (
            DateRange.TodayDate -
            (
                6 -
                DateSeed.DayOffset
            )
            AS TIMESTAMP
        ) AS DateStart,

        CAST
        (
            DateRange.TodayDate -
            (
                6 -
                DateSeed.DayOffset
            )
            +
            1
            AS TIMESTAMP
        ) AS DateEnd,

        DateSeed.DayOffset

    FROM DateRange DateRange

    INNER JOIN DateSeed DateSeed ON
    (
        1 = 1
    )
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
             * MRole is applied only to the main physical table.
             */
            string cashLineAccessSql = @"
SELECT
    CashLine.C_CashLine_ID,
    CashLine.C_Cash_ID,
    CashLine.Amount

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

            string cashLineAccessCteSql = @"
CashLineAccess AS
(
" + cashLineAccessSql + @"
)";

            string cashDataSql = @"
CashData AS
(
    SELECT
        CashLine.C_CashLine_ID,
        CashLine.Amount,

        CashHeader.C_Cash_ID,
        CashHeader.StatementDate,
        CashHeader.DateAcct,
        CashHeader.AD_Client_ID,
        CashHeader.AD_Org_ID,

        CashBook.C_Currency_ID
            AS CashBookCurrency_ID

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

    INNER JOIN DateRange DateRange ON
    (
        CAST
        (
            CashHeader.StatementDate
            AS TIMESTAMP
        ) >=
        DateRange.DateFrom

        AND CAST
        (
            CashHeader.StatementDate
            AS TIMESTAMP
        ) <
        DateRange.DateTo
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

            string convertedAmountExpression = @"
CASE
    WHEN CashData.C_CashLine_ID IS NULL
    THEN 0

    WHEN CashData.CashBookCurrency_ID =
        SchemaCurrency.C_Currency_ID
    THEN
        COALESCE
        (
            CashData.Amount,
            0
        )

    ELSE
        CurrencyConvert
        (
            COALESCE
            (
                CashData.Amount,
                0
            ),
            CashData.CashBookCurrency_ID,
            SchemaCurrency.C_Currency_ID,
            CashData.DateAcct,
            0,
            CashData.AD_Client_ID,
            CashData.AD_Org_ID
        )
END";

            string cashFlowDataSql = @"
CashFlowData AS
(
    SELECT
        " + cashFlowDateSql + @" AS CashFlowDate,

        CashFlowDays.DayOffset,

        ROUND
        (
            CAST
            (
                COALESCE
                (
                    SUM
                    (
                        CASE
                            WHEN
                            (
                                " + convertedAmountExpression + @"
                            ) > 0

                            THEN
                                " + convertedAmountExpression + @"

                            ELSE 0
                        END
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
                        SchemaCurrency.StdPrecision
                    ),
                    2
                )
                AS INTEGER
            )
        ) AS CashInAmount,

        ROUND
        (
            CAST
            (
                COALESCE
                (
                    SUM
                    (
                        CASE
                            WHEN
                            (
                                " + convertedAmountExpression + @"
                            ) < 0

                            THEN
                                0 -
                                (
                                    " + convertedAmountExpression + @"
                                )

                            ELSE 0
                        END
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
                        SchemaCurrency.StdPrecision
                    ),
                    2
                )
                AS INTEGER
            )
        ) AS CashOutAmount,

        ROUND
        (
            CAST
            (
                COALESCE
                (
                    SUM
                    (
                        " + convertedAmountExpression + @"
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
                        SchemaCurrency.StdPrecision
                    ),
                    2
                )
                AS INTEGER
            )
        ) AS NetAmount,

        COUNT
        (
            CashData.C_CashLine_ID
        ) AS LineCount,

        COALESCE
        (
            MAX
            (
                SchemaCurrency.StdPrecision
            ),
            2
        ) AS StdPrecision,

        MAX
        (
            SchemaCurrency.C_Currency_ID
        ) AS C_Currency_ID,

        MAX
        (
            SchemaCurrency.ISO_Code
        ) AS CurrencyISO,

        MAX
        (
            SchemaCurrency.Cur_Symbol
        ) AS CurrencySymbol

    FROM CashFlowDays CashFlowDays

    INNER JOIN SchemaCurrency SchemaCurrency ON
    (
        1 = 1
    )

    LEFT OUTER JOIN CashData CashData ON
    (
        CashData.AD_Client_ID =
        SchemaCurrency.AD_Client_ID

        AND CAST
        (
            CashData.StatementDate
            AS TIMESTAMP
        ) >=
        CashFlowDays.DateStart

        AND CAST
        (
            CashData.StatementDate
            AS TIMESTAMP
        ) <
        CashFlowDays.DateEnd
    )

    GROUP BY
        " + cashFlowDateSql + @",
        CashFlowDays.DayOffset
)";

            string sql = @"
WITH
" + queryParametersSql + @",
" + dateRangeSql + @",
" + dateSeedSql + @",
" + cashFlowDaysSql + @",
" + schemaCurrencySql + @",
" + cashLineAccessCteSql + @",
" + cashDataSql + @",
" + cashFlowDataSql + @"
SELECT
    CashFlowData.CashFlowDate,
    CashFlowData.CashInAmount,
    CashFlowData.CashOutAmount,
    CashFlowData.NetAmount,
    CashFlowData.LineCount,
    CashFlowData.StdPrecision,
    CashFlowData.C_Currency_ID,
    CashFlowData.CurrencyISO,
    CashFlowData.CurrencySymbol

FROM CashFlowData CashFlowData

ORDER BY
    CashFlowData.DayOffset";

            SqlParameter[] parameters =
                new SqlParameter[]
                {
                    new SqlParameter(
                        "@AD_Client_ID",
                        ctx.GetAD_Client_ID()
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

        private string GetDateOnlySql(
            string columnName
        )
        {
            if (DB.IsOracle())
            {
                return
                    "CAST(TRUNC("
                    + columnName
                    + ") AS DATE)";
            }

            return
                "CAST("
                + columnName
                + " AS DATE)";
        }

        private string FormatDate(
            DateTime date
        )
        {
            return date.ToString(
                "yyyy-MM-dd"
            );
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