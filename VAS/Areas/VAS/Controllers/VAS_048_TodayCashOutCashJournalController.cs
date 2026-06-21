using System;
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
    /// Purpose     : Provides today's cash out and comparison
    ///               with previous 7-day average.
    /// </summary>
    public class VAS_048_TodayCashOutCashJournalController : Controller
    {
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetTodayCashOut()
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

            IDataReader reader =
                null;

            try
            {
                SqlQueryData queryData =
                    BuildTodayCashOutSql(
                        ctx
                    );

                reader =
                    DB.ExecuteReader(
                        queryData.Sql,
                        queryData.Parameters,
                        null
                    );

                decimal todayAmount =
                    0;

                decimal sevenDayAmount =
                    0;

                decimal sevenDayAverage =
                    0;

                int disbursementCount =
                    0;

                int sevenDayDisbursementCount =
                    0;

                int stdPrecision =
                    2;

                int currencyId =
                    0;

                string currencyISO =
                    string.Empty;

                string currencySymbol =
                    string.Empty;

                DateTime? dateFrom =
                    null;

                DateTime? dateTo =
                    null;

                if (
                    reader != null &&
                    reader.Read()
                )
                {
                    todayAmount =
                        GetDecimal(
                            reader,
                            "TodayAmount"
                        );

                    sevenDayAmount =
                        GetDecimal(
                            reader,
                            "SevenDayAmount"
                        );

                    disbursementCount =
                        GetInt(
                            reader,
                            "DisbursementCount"
                        );

                    sevenDayDisbursementCount =
                        GetInt(
                            reader,
                            "SevenDayDisbursementCount"
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

                    if (
                        reader["DateFrom"] !=
                        DBNull.Value
                    )
                    {
                        dateFrom =
                            Util.GetValueOfDateTime(
                                reader["DateFrom"]
                            );
                    }

                    if (
                        reader["DateTo"] !=
                        DBNull.Value
                    )
                    {
                        dateTo =
                            Util.GetValueOfDateTime(
                                reader["DateTo"]
                            );
                    }
                }

                sevenDayAverage =
                    Math.Round(
                        sevenDayAmount / 7,
                        stdPrecision
                    );

                decimal deltaPercent =
                    0;

                if (sevenDayAverage != 0)
                {
                    deltaPercent =
                        Math.Round(
                            (
                                (
                                    todayAmount -
                                    sevenDayAverage
                                )
                                /
                                sevenDayAverage
                            )
                            *
                            100,
                            0
                        );
                }

                bool hasData =
                    disbursementCount > 0;

                return Json(
                    new
                    {
                        success = true,
                        error = string.Empty,

                        hasData =
                            hasData,

                        title = GetMsg(
                            ctx,
                            "VAS_048_CashOut",
                            "Cash out"
                        ),

                        mainMetric =
                            hasData
                                ? todayAmount
                                : 0,

                        mainMetricText =
                            hasData
                                ? todayAmount.ToString()
                                : "0",

                        description = GetMsg(
                            ctx,
                            "VAS_048_VsSevenDayAvg",
                            "vs 7-day avg"
                        ),

                        badgeText = GetMsg(
                            ctx,
                            "VAS_048_Today",
                            "Today"
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

                        deltaPercent =
                            deltaPercent,

                        sevenDayAverage =
                            sevenDayAverage,

                        disbursementCount =
                            disbursementCount,

                        sevenDayDisbursementCount =
                            sevenDayDisbursementCount
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
                            "VAS_048_LoadError",
                            "Unable to load cash out"
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

        private SqlQueryData BuildTodayCashOutSql(
            Ctx ctx
        )
        {
            /*
             * Oracle:
             * TRUNC(CURRENT_DATE)
             *
             * PostgreSQL:
             * CURRENT_DATE
             */
            string todayDateSql =
                GetTodayDateSql();

            /*
             * Oracle requires FROM DUAL for a SELECT that
             * contains only bind parameters.
             *
             * PostgreSQL does not require FROM.
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
             * Avoid fixed-width CHAR padding and keep the
             * text expressions compatible with both databases.
             */
            string textType =
                DB.IsOracle()
                    ? "VARCHAR2(255)"
                    : "VARCHAR(255)";

            /*
             * The runtime bind variable appears exactly once.
             * Other CTEs read the value from QueryParameters.
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
            " + todayDateSql + @"
            AS TIMESTAMP
        ) AS TodayStart,

        CAST
        (
            " + todayDateSql + @" + 1
            AS TIMESTAMP
        ) AS TodayEnd,

        CAST
        (
            " + todayDateSql + @" - 7
            AS TIMESTAMP
        ) AS SevenDayStart,

        CAST
        (
            " + todayDateSql + @"
            AS TIMESTAMP
        ) AS SevenDayEnd

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
    CashLine.Amount

FROM C_CashLine CashLine

WHERE CashLine.IsActive = 'Y'

AND CashLine.Amount < 0";

            /*
             * Apply MRole only to the main physical table query.
             */
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
        CashHeader.AD_Client_ID,
        CashHeader.AD_Org_ID,
        CashHeader.StatementDate,
        CashHeader.DateAcct,

        CashBook.C_Currency_ID

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
    WHEN CashData.C_Currency_ID =
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
            CashData.C_Currency_ID,
            SchemaCurrency.C_Currency_ID,
            CashData.DateAcct,
            0,
            CashData.AD_Client_ID,
            CashData.AD_Org_ID
        )
END";

            string cashOutDataSql = @"
CashOutData AS
(
    SELECT
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
                                CAST
                                (
                                    CashData.StatementDate
                                    AS TIMESTAMP
                                ) >=
                                DateRange.TodayStart

                                AND CAST
                                (
                                    CashData.StatementDate
                                    AS TIMESTAMP
                                ) <
                                DateRange.TodayEnd

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
        ) AS TodayAmount,

        COUNT
        (
            CASE
                WHEN
                    CAST
                    (
                        CashData.StatementDate
                        AS TIMESTAMP
                    ) >=
                    DateRange.TodayStart

                    AND CAST
                    (
                        CashData.StatementDate
                        AS TIMESTAMP
                    ) <
                    DateRange.TodayEnd

                THEN CashData.C_CashLine_ID

                ELSE NULL
            END
        ) AS DisbursementCount,

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
                                CAST
                                (
                                    CashData.StatementDate
                                    AS TIMESTAMP
                                ) >=
                                DateRange.SevenDayStart

                                AND CAST
                                (
                                    CashData.StatementDate
                                    AS TIMESTAMP
                                ) <
                                DateRange.SevenDayEnd

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
        ) AS SevenDayAmount,

        COUNT
        (
            CASE
                WHEN
                    CAST
                    (
                        CashData.StatementDate
                        AS TIMESTAMP
                    ) >=
                    DateRange.SevenDayStart

                    AND CAST
                    (
                        CashData.StatementDate
                        AS TIMESTAMP
                    ) <
                    DateRange.SevenDayEnd

                THEN CashData.C_CashLine_ID

                ELSE NULL
            END
        ) AS SevenDayDisbursementCount,

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

    FROM SchemaCurrency SchemaCurrency

    INNER JOIN DateRange DateRange ON
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
        DateRange.SevenDayStart

        AND CAST
        (
            CashData.StatementDate
            AS TIMESTAMP
        ) <
        DateRange.TodayEnd
    )
)";

            string sql = @"
WITH
" + queryParametersSql + @",
" + dateRangeSql + @",
" + schemaCurrencySql + @",
" + cashLineAccessCteSql + @",
" + cashDataSql + @",
" + cashOutDataSql + @"
SELECT
    CashOutData.TodayAmount,
    CashOutData.DisbursementCount,
    CashOutData.SevenDayAmount,
    CashOutData.SevenDayDisbursementCount,
    CashOutData.StdPrecision,
    CashOutData.C_Currency_ID,
    CashOutData.CurrencyISO,
    CashOutData.CurrencySymbol,

    DateRange.TodayDate AS DateFrom,
    DateRange.TodayDate AS DateTo

FROM CashOutData CashOutData

INNER JOIN DateRange DateRange ON
(
    1 = 1
)";

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