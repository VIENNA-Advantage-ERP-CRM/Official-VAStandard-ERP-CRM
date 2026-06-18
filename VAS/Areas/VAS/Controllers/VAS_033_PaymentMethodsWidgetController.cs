using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Web.Mvc;
using VAdvantage.Classes;
using VAdvantage.DataBase;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VAS.Controllers
{
    /*
     * Labels / Message Keys
     * 1 | Payment methods                                        | VAS_033_MessagePaymentMethods
     * 2 | Upi is cheapest - shift small payments where possible | VAS_033_MessagePaymentMethodWhy
     * 3 | Not Specified                                          | VAS_033_MessageNotSpecified
     */
    public class VAS_033_PaymentMethodsWidgetController : Controller
    {
        private const string PeriodFilterMonth = "MONTH";
        private const string PeriodFilterYTD = "YTD";

        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetPaymentMethods()
        {
            string periodFilter = PeriodFilterMonth;

            Ctx ctx = GetContext();

            if (ctx == null)
            {
                return GetSessionExpiredResult();
            }

            IDataReader dr = null;
            string sql = string.Empty;

            try
            {
                if (string.IsNullOrWhiteSpace(periodFilter))
                {
                    periodFilter = PeriodFilterMonth;
                }

                periodFilter = periodFilter.ToUpperInvariant();

                bool isYTD =
                    periodFilter == PeriodFilterYTD;

                sql = BuildPaymentMethodsSql(
                    ctx,
                    isYTD
                );

                dr = DB.ExecuteReader(
                    sql,
                    null,
                    null
                );

                List<PaymentMethodSummary> rows =
                    new List<PaymentMethodSummary>();

                decimal totalAmount = 0;

                int cCurrencyId = 0;
                int stdPrecision = 2;

                string currencyISO = string.Empty;
                string currencySymbol = string.Empty;

                while (dr != null && dr.Read())
                {
                    int rowPrecision =
                        NormalizePrecision(
                            GetInt(
                                dr,
                                "StdPrecision",
                                2
                            )
                        );

                    decimal paymentAmount =
                        Math.Round(
                            GetDecimal(
                                dr,
                                "PaymentAmount",
                                0
                            ),
                            rowPrecision,
                            MidpointRounding.AwayFromZero
                        );

                    int rowCurrencyId =
                        GetInt(
                            dr,
                            "C_Currency_ID"
                        );

                    string rowCurrencyISO =
                        GetString(
                            dr,
                            "CurrencyISO",
                            string.Empty
                        );

                    string rowCurrencySymbol =
                        GetString(
                            dr,
                            "CurrencySymbol",
                            string.Empty
                        );

                    if (string.IsNullOrWhiteSpace(rowCurrencySymbol))
                    {
                        rowCurrencySymbol = rowCurrencyISO;
                    }

                    if (cCurrencyId == 0)
                    {
                        cCurrencyId = rowCurrencyId;
                        stdPrecision = rowPrecision;
                        currencyISO = rowCurrencyISO;
                        currencySymbol = rowCurrencySymbol;
                    }

                    string paymentMethodName =
                        FirstNotEmpty(
                            GetString(
                                dr,
                                "PaymentMethodName",
                                string.Empty
                            ),

                            GetString(
                                dr,
                                "PaymentRule",
                                string.Empty
                            )
                        );

                    rows.Add(
                        new PaymentMethodSummary
                        {
                            PaymentMethodName =
                                paymentMethodName,

                            PaymentCount =
                                GetInt(
                                    dr,
                                    "PaymentCount"
                                ),

                            PaymentAmount =
                                paymentAmount,

                            CCurrencyId =
                                rowCurrencyId,

                            StdPrecision =
                                rowPrecision,

                            CurrencyISO =
                                rowCurrencyISO,

                            CurrencySymbol =
                                rowCurrencySymbol
                        }
                    );

                    totalAmount += paymentAmount;
                }

                CloseReader(dr);
                dr = null;

                List<object> methods =
                    new List<object>();

                foreach (PaymentMethodSummary row in rows)
                {
                    string paymentMethodName =
                        row.PaymentMethodName;

                    if (string.IsNullOrWhiteSpace(paymentMethodName))
                    {
                        paymentMethodName = GetMsg(
                            ctx,
                            "VAS_033_MessageNotSpecified",
                            "Not Specified"
                        );
                    }

                    decimal percentage = 0;

                    if (totalAmount > 0)
                    {
                        percentage = decimal.Round(
                            row.PaymentAmount * 100M /
                            totalAmount,
                            2,
                            MidpointRounding.AwayFromZero
                        );
                    }

                    methods.Add(
                        new
                        {
                            paymentMethodName =
                                paymentMethodName,

                            paymentCount =
                                row.PaymentCount,

                            paymentAmount =
                                row.PaymentAmount,

                            cCurrencyId =
                                row.CCurrencyId,

                            stdPrecision =
                                row.StdPrecision,

                            currencyISO =
                                row.CurrencyISO,

                            currencySymbol =
                                row.CurrencySymbol,

                            symbol =
                                row.CurrencySymbol,

                            percentage =
                                percentage
                        }
                    );
                }

                DateRangeResult dateRange =
                    GetPeriodDateRange(
                        ctx,
                        isYTD
                    );

                return Json(
                    new
                    {
                        title = GetMsg(
                            ctx,
                            "VAS_033_MessagePaymentMethods",
                            "Payment methods"
                        ),

                        description = GetMsg(
                            ctx,
                            "VAS_033_MessagePaymentMethodWhy",
                            "Upi is cheapest - shift small payments where possible"
                        ),

                        totalAmount =
                            Math.Round(
                                totalAmount,
                                NormalizePrecision(stdPrecision),
                                MidpointRounding.AwayFromZero
                            ),

                        cCurrencyId = cCurrencyId,
                        stdPrecision = stdPrecision,
                        currencyISO = currencyISO,
                        currencySymbol = currencySymbol,
                        symbol = currencySymbol,

                        dateFrom =
                            dateRange != null &&
                            dateRange.DateFrom.HasValue
                                ? FormatDate(
                                    dateRange.DateFrom.Value
                                )
                                : string.Empty,

                        dateTo =
                            dateRange != null &&
                            dateRange.DateTo.HasValue
                                ? FormatDate(
                                    dateRange.DateTo.Value
                                )
                                : string.Empty,

                        periodFilter =
                            isYTD
                                ? PeriodFilterYTD
                                : PeriodFilterMonth,

                        methods = methods
                    },
                    JsonRequestBehavior.AllowGet
                );
            }
            catch (Exception ex)
            {
                return Json(
                    new
                    {
                        error = ex.Message,
                        errorText = ex.Message,
                        sql = sql
                    },
                    JsonRequestBehavior.AllowGet
                );
            }
            finally
            {
                CloseReader(dr);
            }
        }

        private string BuildPaymentMethodsSql(
            Ctx ctx,
            bool isYTD
        )
        {
            bool hasPaymentMethod =
                HasPaymentMethodColumn();

            bool hasPaymentRule =
                HasPaymentRuleColumn();

            string clientIdSql =
                ctx.GetAD_Client_ID().ToString(
                    CultureInfo.InvariantCulture
                );

            string paymentMethodNameColumn =
                hasPaymentMethod
                    ? GetPaymentMethodNameColumn(
                        "PaymentMethod"
                    )
                    : string.Empty;

            bool usePaymentMethodTable =
                hasPaymentMethod &&
                !string.IsNullOrWhiteSpace(
                    paymentMethodNameColumn
                );

            string paymentMethodIdSelect =
                hasPaymentMethod
                    ? "COALESCE(Payment.VA009_PaymentMethod_ID, 0)"
                    : "0";

            string paymentMethodNameSelect =
                usePaymentMethodTable
                    ? paymentMethodNameColumn
                    : "NULL";

            string paymentRuleSelect =
                hasPaymentRule
                    ? "Payment.PaymentRule"
                    : "NULL";

            string paymentMethodColumnInAccess =
                hasPaymentMethod
                    ? @",
    Payment.VA009_PaymentMethod_ID"
                    : string.Empty;

            string paymentRuleColumnInAccess =
                hasPaymentRule
                    ? @",
    Payment.PaymentRule"
                    : string.Empty;

            string paymentMethodJoin =
                usePaymentMethodTable
                    ? @"
LEFT OUTER JOIN VA009_PaymentMethod PaymentMethod ON
(
    PaymentMethod.VA009_PaymentMethod_ID =
    Payment.VA009_PaymentMethod_ID
)"
                    : string.Empty;

            string paymentMethodGroupBy =
                usePaymentMethodTable
                    ? @",
    " + paymentMethodNameColumn
                    : string.Empty;

            string paymentRuleGroupBy =
                hasPaymentRule
                    ? @",
    Payment.PaymentRule"
                    : string.Empty;

            string schemaCurrencySql = @"
SchemaCurrency AS
(
    SELECT
        ClientInfo.AD_Client_ID,

        AcctSchema.C_Currency_ID,

        Currency.StdPrecision,

        Currency.ISO_Code,

        Currency.CurSymbol

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

    AND ClientInfo.AD_Client_ID =
        " + clientIdSql + @"
)";

            string currentPeriodSql = @"
CurrentPeriod AS
(
    SELECT
        Period.C_Period_ID,
        Period.C_Year_ID,
        Period.StartDate,
        Period.EndDate

    FROM AD_ClientInfo ClientInfo

    INNER JOIN C_Year YearData ON
    (
        YearData.C_Calendar_ID =
        ClientInfo.C_Calendar_ID
    )

    INNER JOIN C_Period Period ON
    (
        Period.C_Year_ID =
        YearData.C_Year_ID
    )

    WHERE ClientInfo.IsActive = 'Y'

    AND ClientInfo.AD_Client_ID =
        " + clientIdSql + @"

    AND " + GetCurrentDateSql() + @" >=
        Period.StartDate

    AND " + GetCurrentDateSql() + @" <
        " + GetDateToExclusiveSql(
            "Period.EndDate"
        ) + @"
)";

            string periodRangeSql;

            if (isYTD)
            {
                periodRangeSql = @"
PeriodRange AS
(
    SELECT
        MIN(
            Period.StartDate
        ) AS StartDate,

        MAX(
            CurrentPeriod.EndDate
        ) AS EndDate,

        MAX
        (
            " + GetDateToExclusiveSql(
                "CurrentPeriod.EndDate"
            ) + @"
        ) AS EndDateExclusive

    FROM CurrentPeriod CurrentPeriod

    INNER JOIN C_Period Period ON
    (
        Period.C_Year_ID =
        CurrentPeriod.C_Year_ID
    )

    WHERE Period.StartDate <=
        CurrentPeriod.EndDate
)";
            }
            else
            {
                periodRangeSql = @"
PeriodRange AS
(
    SELECT
        CurrentPeriod.StartDate AS StartDate,

        CurrentPeriod.EndDate AS EndDate,

        " + GetDateToExclusiveSql(
            "CurrentPeriod.EndDate"
        ) + @" AS EndDateExclusive

    FROM CurrentPeriod CurrentPeriod
)";
            }

            string paymentAccessSql = @"
SELECT
    Payment.C_Payment_ID,

    Payment.AD_Client_ID,

    Payment.AD_Org_ID,

    Payment.C_Currency_ID,

    Payment.C_ConversionType_ID,

    Payment.DateAcct,

    Payment.PayAmt" +
                paymentRuleColumnInAccess +
                paymentMethodColumnInAccess + @"

FROM C_Payment Payment

WHERE Payment.IsActive = 'Y'

AND Payment.IsReceipt = 'N'

AND Payment.AD_Client_ID =
    " + clientIdSql + @"

AND Payment.DocStatus IN
(
    'CO',
    'CL'
)";

            paymentAccessSql =
                MRole.GetDefault(ctx).AddAccessSQL(
                    paymentAccessSql,
                    "Payment",
                    MRole.SQL_FULLYQUALIFIED,
                    MRole.SQL_RO
                );

            string paymentDataSql = @"
PaymentData AS
(
    SELECT
        " + paymentMethodIdSelect + @"
            AS PaymentMethod_ID,

        " + paymentMethodNameSelect + @"
            AS PaymentMethodName,

        " + paymentRuleSelect + @"
            AS PaymentRule,

        COUNT(
            Payment.C_Payment_ID
        ) AS PaymentCount,

        ROUND
        (
            CAST
            (
                COALESCE
                (
                    SUM
                    (
                        CASE
                            WHEN Payment.C_Currency_ID =
                                 SchemaCurrency.C_Currency_ID

                            THEN COALESCE(
                                Payment.PayAmt,
                                0
                            )

                            ELSE CurrencyConvert
                            (
                                COALESCE(
                                    Payment.PayAmt,
                                    0
                                ),
                                Payment.C_Currency_ID,
                                SchemaCurrency.C_Currency_ID,
                                Payment.DateAcct,
                                Payment.C_ConversionType_ID,
                                Payment.AD_Client_ID,
                                Payment.AD_Org_ID
                            )
                        END
                    ),
                    0
                ) AS NUMERIC
            ),

            CAST
            (
                COALESCE
                (
                    MAX(
                        SchemaCurrency.StdPrecision
                    ),
                    2
                ) AS INTEGER
            )
        ) AS PaymentAmount,

        MAX(
            SchemaCurrency.C_Currency_ID
        ) AS C_Currency_ID,

        MAX(
            SchemaCurrency.StdPrecision
        ) AS StdPrecision,

        MAX(
            SchemaCurrency.ISO_Code
        ) AS CurrencyISO,

        MAX(
            SchemaCurrency.CurSymbol
        ) AS CurrencySymbol

    FROM PaymentFiltered Payment

    INNER JOIN SchemaCurrency SchemaCurrency ON
    (
        SchemaCurrency.AD_Client_ID =
        Payment.AD_Client_ID
    )

    INNER JOIN PeriodRange PeriodRange ON
    (
        Payment.DateAcct >=
            PeriodRange.StartDate

        AND Payment.DateAcct <
            PeriodRange.EndDateExclusive
    )

    " + paymentMethodJoin + @"

    GROUP BY
        " + paymentMethodIdSelect +
                paymentMethodGroupBy +
                paymentRuleGroupBy + @"
)";

            string sql = @"
WITH
" + schemaCurrencySql + @",

" + currentPeriodSql + @",

" + periodRangeSql + @",

PaymentFiltered AS
(
" + paymentAccessSql + @"
),

" + paymentDataSql + @"

SELECT
    PaymentData.PaymentMethodName,

    PaymentData.PaymentRule,

    PaymentData.PaymentCount,

    PaymentData.PaymentAmount,

    PaymentData.C_Currency_ID,

    PaymentData.StdPrecision,

    PaymentData.CurrencyISO,

    PaymentData.CurrencySymbol

FROM PaymentData PaymentData

ORDER BY
    PaymentData.PaymentAmount DESC,
    PaymentData.PaymentMethodName ASC";

            return sql;
        }

        private DateRangeResult GetPeriodDateRange(
            Ctx ctx,
            bool isYTD
        )
        {
            string clientIdSql =
                ctx.GetAD_Client_ID().ToString(
                    CultureInfo.InvariantCulture
                );

            string currentPeriodSql = @"
CurrentPeriod AS
(
    SELECT
        Period.C_Year_ID,
        Period.StartDate,
        Period.EndDate

    FROM AD_ClientInfo ClientInfo

    INNER JOIN C_Year YearData ON
    (
        YearData.C_Calendar_ID =
        ClientInfo.C_Calendar_ID
    )

    INNER JOIN C_Period Period ON
    (
        Period.C_Year_ID =
        YearData.C_Year_ID
    )

    WHERE ClientInfo.IsActive = 'Y'

    AND ClientInfo.AD_Client_ID =
        " + clientIdSql + @"

    AND " + GetCurrentDateSql() + @" >=
        Period.StartDate

    AND " + GetCurrentDateSql() + @" <
        " + GetDateToExclusiveSql(
            "Period.EndDate"
        ) + @"
)";

            string sql;

            if (isYTD)
            {
                sql = @"
WITH
" + currentPeriodSql + @"

SELECT
    MIN(
        Period.StartDate
    ) AS DateFrom,

    MAX(
        CurrentPeriod.EndDate
    ) AS DateTo

FROM CurrentPeriod CurrentPeriod

INNER JOIN C_Period Period ON
(
    Period.C_Year_ID =
    CurrentPeriod.C_Year_ID
)

WHERE Period.StartDate <=
    CurrentPeriod.EndDate";
            }
            else
            {
                sql = @"
WITH
" + currentPeriodSql + @"

SELECT
    CurrentPeriod.StartDate AS DateFrom,

    CurrentPeriod.EndDate AS DateTo

FROM CurrentPeriod CurrentPeriod";
            }

            IDataReader dr = null;

            try
            {
                dr = DB.ExecuteReader(
                    sql,
                    null,
                    null
                );

                if (dr != null && dr.Read())
                {
                    return new DateRangeResult
                    {
                        DateFrom =
                            dr["DateFrom"] != DBNull.Value
                                ? Util.GetValueOfDateTime(
                                    dr["DateFrom"]
                                )
                                : null,

                        DateTo =
                            dr["DateTo"] != DBNull.Value
                                ? Util.GetValueOfDateTime(
                                    dr["DateTo"]
                                )
                                : null
                    };
                }
            }
            finally
            {
                CloseReader(dr);
            }

            return new DateRangeResult
            {
                DateFrom = null,
                DateTo = null
            };
        }

        private string GetCurrentDateSql()
        {
            if (DB.IsOracle())
            {
                return "TRUNC(CURRENT_DATE)";
            }

            return "CURRENT_DATE";
        }

        private string GetDateToExclusiveSql(
            string columnName
        )
        {
            if (DB.IsOracle())
            {
                return "TRUNC("
                    + columnName
                    + ") + 1";
            }

            return "CAST("
                + columnName
                + " AS DATE) + 1";
        }

        private string GetPaymentMethodNameColumn(
            string tableAlias
        )
        {
            if (
                HasColumn(
                    "VA009_PaymentMethod",
                    "VA009_Name"
                )
            )
            {
                return tableAlias +
                    ".VA009_Name";
            }

            if (
                HasColumn(
                    "VA009_PaymentMethod",
                    "Name"
                )
            )
            {
                return tableAlias +
                    ".Name";
            }

            if (
                HasColumn(
                    "VA009_PaymentMethod",
                    "Value"
                )
            )
            {
                return tableAlias +
                    ".Value";
            }

            return string.Empty;
        }

        private bool HasPaymentMethodColumn()
        {
            return HasColumn(
                "C_Payment",
                "VA009_PaymentMethod_ID"
            );
        }

        private bool HasPaymentRuleColumn()
        {
            return HasColumn(
                "C_Payment",
                "PaymentRule"
            );
        }

        private bool HasColumn(
            string tableName,
            string columnName
        )
        {
            string sql = @"
SELECT
    COUNT(1)

FROM AD_Table TableData

INNER JOIN AD_Column ColumnData ON
(
    ColumnData.AD_Table_ID =
    TableData.AD_Table_ID
)

WHERE TableData.TableName =
    " + ToSqlString(tableName) + @"

AND ColumnData.ColumnName =
    " + ToSqlString(columnName);

            return Util.GetValueOfInt(
                DB.ExecuteScalar(sql)
            ) > 0;
        }

        private string ToSqlString(
            string value
        )
        {
            return "'"
                + (value ?? string.Empty)
                    .Replace("'", "''")
                + "'";
        }

        private Ctx GetContext()
        {
            if (Session["ctx"] == null)
            {
                return null;
            }

            return Session["ctx"] as Ctx;
        }

        private JsonResult GetSessionExpiredResult()
        {
            Ctx ctx = Env.GetCtx();
            string sessionExpired = GetMsg(ctx, "SessionExpired", "Session Expired");

            return Json(
                new
                {
                    error = sessionExpired,
                    errorText = sessionExpired
                },
                JsonRequestBehavior.AllowGet
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

            for (int i = 0; i < values.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(values[i]))
                {
                    return values[i];
                }
            }

            return string.Empty;
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

        private int GetInt(
            IDataReader reader,
            string columnName,
            int fallback = 0
        )
        {
            object value = reader[columnName];

            return
                value == null ||
                value == DBNull.Value
                    ? fallback
                    : Util.GetValueOfInt(value);
        }

        private decimal GetDecimal(
            IDataReader reader,
            string columnName,
            decimal fallback
        )
        {
            object value = reader[columnName];

            return
                value == null ||
                value == DBNull.Value
                    ? fallback
                    : Util.GetValueOfDecimal(value);
        }

        private string GetString(
            IDataReader reader,
            string columnName,
            string fallback
        )
        {
            object value = reader[columnName];

            return
                value == null ||
                value == DBNull.Value
                    ? fallback
                    : Util.GetValueOfString(value);
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

        private string FormatDate(
            DateTime date
        )
        {
            return date.ToString(
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture
            );
        }

        private string GetMsg(
            Ctx ctx,
            string key,
            string fallback
        )
        {
            string msg = Msg.GetMsg(
                ctx,
                key
            );

            return
                !string.IsNullOrWhiteSpace(msg) &&
                msg != "[" + key + "]"
                    ? msg
                    : fallback;
        }

        private class PaymentMethodSummary
        {
            public string PaymentMethodName
            {
                get;
                set;
            }

            public int PaymentCount
            {
                get;
                set;
            }

            public decimal PaymentAmount
            {
                get;
                set;
            }

            public int CCurrencyId
            {
                get;
                set;
            }

            public int StdPrecision
            {
                get;
                set;
            }

            public string CurrencyISO
            {
                get;
                set;
            }

            public string CurrencySymbol
            {
                get;
                set;
            }
        }

        private class DateRangeResult
        {
            public DateTime? DateFrom
            {
                get;
                set;
            }

            public DateTime? DateTo
            {
                get;
                set;
            }
        }
    }
}
