using CoreLibrary.DataBase;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Globalization;
using System.Web.Mvc;
using VAdvantage.Model;
using VAdvantage.Utility;

/*
 * GL Journal Total Debit Controller
 *
 * -- Labels / Message Keys --------------------------------------------
 *  #  | Current Text                         | Message Key
 * ----+--------------------------------------+--------------------------------
 *  1  | Invalid Column                       | VAS_037_InvalidColumn
 *  2  | Invalid Alias                        | VAS_037_InvalidAlias
 *  3  | Session Expired                      | VAS_037_SessionExpired
 *  4  | Could Not Load Data                  | VAS_037_ErrorLoadingData
 *  5  | Current Period Was Not Found         | VAS_037_PeriodNotFound
 *  6  | Total Debit                          | VAS_037_TotalDebitTitle
 *  7  | Total Credit                         | VAS_037_TotalCreditTitle
 *  8  | Net Difference                       | VAS_037_NetDifferenceTitle
 *  9  | Month                                | VAS_037_MonthBadge
 * 10  | YTD                                  | VAS_037_YTDBadge
 * 11  | Journal Count                        | VAS_037_JournalCountDescription
 * ---------------------------------------------------------------------
 */

namespace VAS.Controllers
{
    /// <summary>
    /// Controller for GL Journal amount KPI widgets.
    ///
    /// Supports:
    /// - Oracle
    /// - PostgreSQL
    /// </summary>
    public class VAS_037_GLJournalTotalDebitWidgetController : Controller
    {
        public JsonResult GetTotalDebit(
            string period
        )
        {
            if (Session["ctx"] == null)
            {
                return Json(
                    SessionExpired(),
                    JsonRequestBehavior.AllowGet
                );
            }

            Ctx ctx =
                Session["ctx"] as Ctx;

            return Json(
                GetJournalTotal(
                    ctx,
                    "AmtAcctDr",
                    "TotalDebit",
                    period
                ),
                JsonRequestBehavior.AllowGet
            );
        }

        public JsonResult GetTotalCredit(
            string period
        )
        {
            if (Session["ctx"] == null)
            {
                return Json(
                    SessionExpired(),
                    JsonRequestBehavior.AllowGet
                );
            }

            Ctx ctx =
                Session["ctx"] as Ctx;

            return Json(
                GetJournalTotal(
                    ctx,
                    "AmtAcctCr",
                    "TotalCredit",
                    period
                ),
                JsonRequestBehavior.AllowGet
            );
        }

        public JsonResult GetNetDifference(
            string period
        )
        {
            if (Session["ctx"] == null)
            {
                return Json(
                    SessionExpired(),
                    JsonRequestBehavior.AllowGet
                );
            }

            Ctx ctx =
                Session["ctx"] as Ctx;

            return Json(
                GetNetDifferenceData(
                    ctx,
                    period
                ),
                JsonRequestBehavior.AllowGet
            );
        }

        private object GetJournalTotal(
            Ctx ctx,
            string amountColumn,
            string resultAlias,
            string period
        )
        {
            if (
                amountColumn != "AmtAcctDr" &&
                amountColumn != "AmtAcctCr"
            )
            {
                return Error(
                    GetMsg(
                        ctx,
                        "VAS_037_InvalidColumn",
                        "Invalid Column"
                    )
                );
            }

            if (
                resultAlias != "TotalDebit" &&
                resultAlias != "TotalCredit"
            )
            {
                return Error(
                    GetMsg(
                        ctx,
                        "VAS_037_InvalidAlias",
                        "Invalid Alias"
                    )
                );
            }

            try
            {
                bool isYTD =
                    IsYTD(period);

                string titleKey =
                    resultAlias == "TotalDebit"
                        ? "VAS_037_TotalDebitTitle"
                        : "VAS_037_TotalCreditTitle";

                string titleFallback =
                    resultAlias == "TotalDebit"
                        ? "Total Debit"
                        : "Total Credit";

                string queryParametersSql =
                    BuildQueryParametersSql();

                string schemaCurrencySql =
                    BuildSchemaCurrencySql();

                string periodRangeSql =
                    BuildPeriodRangeSql();

                string protectedJournalSql =
                    BuildProtectedJournalSql(
                        ctx
                    );

                string journalDataSql =
                    BuildJournalDataSql();

                string dateFromColumn =
                    isYTD
                        ? "PeriodRange.YTDDateFrom"
                        : "PeriodRange.MonthDateFrom";

                string dateToColumn =
                    isYTD
                        ? "PeriodRange.YTDDateTo"
                        : "PeriodRange.MonthDateTo";

                string dateToExclusiveExpression =
                    GetDateToExclusiveExpression(
                        dateToColumn
                    );

                string amountSumExpression = @"
COALESCE
(
    SUM
    (
        COALESCE
        (
            JournalData." + amountColumn + @",
            0
        )
    ),
    0
)";

                string roundedAmountExpression =
                    BuildRoundedAmountExpression(
                        amountSumExpression
                    );

                string sql = @"
WITH QueryParameters AS
(
" + queryParametersSql + @"
),
SchemaCurrency AS
(
" + schemaCurrencySql + @"
),
PeriodRange AS
(
" + periodRangeSql + @"
),
ProtectedJournal AS
(
" + protectedJournalSql + @"
),
JournalData AS
(
" + journalDataSql + @"
)
SELECT
    " + roundedAmountExpression + @" AS " + resultAlias + @",

    COUNT
    (
        DISTINCT
        JournalData.GL_Journal_ID
    ) AS JournalCount,

    MAX
    (
        SchemaCurrency.Cur_Symbol
    ) AS CurSymbol,

    MAX
    (
        SchemaCurrency.ISO_Code
    ) AS ISOCode,

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
        " + dateFromColumn + @"
    ) AS DateFrom,

    MAX
    (
        " + dateToColumn + @"
    ) AS DateTo

FROM PeriodRange PeriodRange

INNER JOIN SchemaCurrency SchemaCurrency ON
(
    SchemaCurrency.AD_Client_ID =
        PeriodRange.AD_Client_ID
)

LEFT OUTER JOIN JournalData JournalData ON
(
    JournalData.AD_Client_ID =
        SchemaCurrency.AD_Client_ID

    AND JournalData.C_AcctSchema_ID =
        SchemaCurrency.C_AcctSchema_ID

    AND JournalData.DateAcct >=
        " + dateFromColumn + @"

    AND JournalData.DateAcct <
        " + dateToExclusiveExpression + @"
)";

                SqlParameter[] parameters =
                {
                    new SqlParameter(
                        "@AD_Client_ID",
                        ctx.GetAD_Client_ID()
                    )
                };

                decimal total = 0;
                int journalCount = 0;
                string curSymbol = string.Empty;
                string isoCode = string.Empty;
                int stdPrecision = 2;

                DateTime? dateFromValue =
                    null;

                DateTime? dateToValue =
                    null;

                using (
                    IDataReader reader =
                        DB.ExecuteReader(
                            sql,
                            parameters,
                            null
                        )
                )
                {
                    if (
                        reader != null &&
                        reader.Read()
                    )
                    {
                        stdPrecision =
                            NormalizePrecision(
                                Util.GetValueOfInt(
                                    reader["StdPrecision"]
                                )
                            );

                        total =
                            GetSafeDecimal(
                                reader[resultAlias],
                                stdPrecision
                            );

                        journalCount =
                            Util.GetValueOfInt(
                                reader["JournalCount"]
                            );

                        curSymbol =
                            Util.GetValueOfString(
                                reader["CurSymbol"]
                            );

                        isoCode =
                            Util.GetValueOfString(
                                reader["ISOCode"]
                            );

                        dateFromValue =
                            GetNullableDateTime(
                                reader["DateFrom"]
                            );

                        dateToValue =
                            GetNullableDateTime(
                                reader["DateTo"]
                            );
                    }
                }

                if (
                    !dateFromValue.HasValue ||
                    !dateToValue.HasValue
                )
                {
                    return Error(
                        GetMsg(
                            ctx,
                            "VAS_037_PeriodNotFound",
                            "Current period was not found"
                        )
                    );
                }

                if (string.IsNullOrWhiteSpace(
                    curSymbol
                ))
                {
                    curSymbol =
                        isoCode;
                }

                bool hasData =
                    journalCount > 0;

                string description =
                    string.Format(
                        GetMsg(
                            ctx,
                            "VAS_037_JournalCountDescription",
                            "{0} journal(s)"
                        ),
                        journalCount
                    );

                return new
                {
                    success = true,
                    error = string.Empty,

                    title = GetMsg(
                        ctx,
                        titleKey,
                        titleFallback
                    ),

                    mainMetric = total,

                    mainMetricText =
                        total.ToString(
                            CultureInfo.InvariantCulture
                        ),

                    description = description,

                    badgeText =
                        GetPeriodBadgeText(
                            ctx,
                            isYTD
                        ),

                    dateFrom =
                        FormatDate(
                            dateFromValue.Value
                        ),

                    dateTo =
                        FormatDate(
                            dateToValue.Value
                        ),

                    currencyISO =
                        isoCode,

                    currencySymbol =
                        curSymbol,

                    stdPrecision =
                        stdPrecision,

                    hasData =
                        hasData,

                    Total =
                        total,

                    JournalCount =
                        journalCount,

                    CurSymbol =
                        curSymbol,

                    ISOCode =
                        isoCode,

                    StdPrecision =
                        stdPrecision,

                    MonthAbbr =
                        DateTime.Now.ToString(
                            "MMM",
                            CultureInfo.InvariantCulture
                        )
                };
            }
            catch (Exception ex)
            {
                Trace.TraceError(
                    ex.ToString()
                );

                return Error(
                    GetMsg(
                        ctx,
                        "VAS_037_ErrorLoadingData",
                        "Could not load data"
                    )
                );
            }
        }

        private object GetNetDifferenceData(
            Ctx ctx,
            string period
        )
        {
            try
            {
                bool isYTD =
                    IsYTD(period);

                string queryParametersSql =
                    BuildQueryParametersSql();

                string schemaCurrencySql =
                    BuildSchemaCurrencySql();

                string periodRangeSql =
                    BuildPeriodRangeSql();

                string protectedJournalSql =
                    BuildProtectedJournalSql(
                        ctx
                    );

                string journalDataSql =
                    BuildJournalDataSql();

                string dateFromColumn =
                    isYTD
                        ? "PeriodRange.YTDDateFrom"
                        : "PeriodRange.MonthDateFrom";

                string dateToColumn =
                    isYTD
                        ? "PeriodRange.YTDDateTo"
                        : "PeriodRange.MonthDateTo";

                string dateToExclusiveExpression =
                    GetDateToExclusiveExpression(
                        dateToColumn
                    );

                string netDifferenceExpression = @"
COALESCE
(
    SUM
    (
        COALESCE
        (
            JournalData.AmtAcctDr,
            0
        )
        -
        COALESCE
        (
            JournalData.AmtAcctCr,
            0
        )
    ),
    0
)";

                string roundedNetDifference =
                    BuildRoundedAmountExpression(
                        netDifferenceExpression
                    );

                string sql = @"
WITH QueryParameters AS
(
" + queryParametersSql + @"
),
SchemaCurrency AS
(
" + schemaCurrencySql + @"
),
PeriodRange AS
(
" + periodRangeSql + @"
),
ProtectedJournal AS
(
" + protectedJournalSql + @"
),
JournalData AS
(
" + journalDataSql + @"
)
SELECT
    " + roundedNetDifference + @" AS NetDiff,

    COUNT
    (
        DISTINCT
        JournalData.GL_Journal_ID
    ) AS JournalCount,

    MAX
    (
        SchemaCurrency.Cur_Symbol
    ) AS CurSymbol,

    MAX
    (
        SchemaCurrency.ISO_Code
    ) AS ISOCode,

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
        " + dateFromColumn + @"
    ) AS DateFrom,

    MAX
    (
        " + dateToColumn + @"
    ) AS DateTo

FROM PeriodRange PeriodRange

INNER JOIN SchemaCurrency SchemaCurrency ON
(
    SchemaCurrency.AD_Client_ID =
        PeriodRange.AD_Client_ID
)

LEFT OUTER JOIN JournalData JournalData ON
(
    JournalData.AD_Client_ID =
        SchemaCurrency.AD_Client_ID

    AND JournalData.C_AcctSchema_ID =
        SchemaCurrency.C_AcctSchema_ID

    AND JournalData.DateAcct >=
        " + dateFromColumn + @"

    AND JournalData.DateAcct <
        " + dateToExclusiveExpression + @"
)";

                SqlParameter[] parameters =
                {
                    new SqlParameter(
                        "@AD_Client_ID",
                        ctx.GetAD_Client_ID()
                    )
                };

                decimal netDifference = 0;
                int journalCount = 0;
                string curSymbol = string.Empty;
                string isoCode = string.Empty;
                int stdPrecision = 2;

                DateTime? dateFromValue =
                    null;

                DateTime? dateToValue =
                    null;

                using (
                    IDataReader reader =
                        DB.ExecuteReader(
                            sql,
                            parameters,
                            null
                        )
                )
                {
                    if (
                        reader != null &&
                        reader.Read()
                    )
                    {
                        stdPrecision =
                            NormalizePrecision(
                                Util.GetValueOfInt(
                                    reader["StdPrecision"]
                                )
                            );

                        netDifference =
                            GetSafeDecimal(
                                reader["NetDiff"],
                                stdPrecision
                            );

                        journalCount =
                            Util.GetValueOfInt(
                                reader["JournalCount"]
                            );

                        curSymbol =
                            Util.GetValueOfString(
                                reader["CurSymbol"]
                            );

                        isoCode =
                            Util.GetValueOfString(
                                reader["ISOCode"]
                            );

                        dateFromValue =
                            GetNullableDateTime(
                                reader["DateFrom"]
                            );

                        dateToValue =
                            GetNullableDateTime(
                                reader["DateTo"]
                            );
                    }
                }

                if (
                    !dateFromValue.HasValue ||
                    !dateToValue.HasValue
                )
                {
                    return Error(
                        GetMsg(
                            ctx,
                            "VAS_037_PeriodNotFound",
                            "Current period was not found"
                        )
                    );
                }

                if (string.IsNullOrWhiteSpace(
                    curSymbol
                ))
                {
                    curSymbol =
                        isoCode;
                }

                bool hasData =
                    journalCount > 0;

                string description =
                    string.Format(
                        GetMsg(
                            ctx,
                            "VAS_037_JournalCountDescription",
                            "{0} journal(s)"
                        ),
                        journalCount
                    );

                return new
                {
                    success = true,
                    error = string.Empty,

                    title = GetMsg(
                        ctx,
                        "VAS_037_NetDifferenceTitle",
                        "Net Difference"
                    ),

                    mainMetric =
                        netDifference,

                    mainMetricText =
                        netDifference.ToString(
                            CultureInfo.InvariantCulture
                        ),

                    description =
                        description,

                    badgeText =
                        GetPeriodBadgeText(
                            ctx,
                            isYTD
                        ),

                    dateFrom =
                        FormatDate(
                            dateFromValue.Value
                        ),

                    dateTo =
                        FormatDate(
                            dateToValue.Value
                        ),

                    currencyISO =
                        isoCode,

                    currencySymbol =
                        curSymbol,

                    stdPrecision =
                        stdPrecision,

                    hasData =
                        hasData,

                    NetDiff =
                        netDifference,

                    IsBalanced =
                        netDifference == 0,

                    JournalCount =
                        journalCount,

                    CurSymbol =
                        curSymbol,

                    ISOCode =
                        isoCode,

                    StdPrecision =
                        stdPrecision,

                    MonthAbbr =
                        DateTime.Now.ToString(
                            "MMM",
                            CultureInfo.InvariantCulture
                        )
                };
            }
            catch (Exception ex)
            {
                Trace.TraceError(
                    ex.ToString()
                );

                return Error(
                    GetMsg(
                        ctx,
                        "VAS_037_ErrorLoadingData",
                        "Could not load data"
                    )
                );
            }
        }

        /// <summary>
        /// Contains every runtime bind variable exactly once.
        ///
        /// Oracle requires FROM DUAL.
        /// PostgreSQL does not require FROM.
        /// </summary>
        private string BuildQueryParametersSql()
        {
            string queryParametersFrom =
                DB.IsOracle()
                    ? " FROM DUAL"
                    : string.Empty;

            return @"
SELECT
    @AD_Client_ID AS AD_Client_ID"
                + queryParametersFrom;
        }

        private string BuildProtectedJournalSql(
            Ctx ctx
        )
        {
            string protectedJournalSql = @"
SELECT
    GL_Journal.GL_Journal_ID,
    GL_Journal.AD_Client_ID,
    GL_Journal.AD_Org_ID,
    GL_Journal.C_AcctSchema_ID,
    GL_Journal.DateAcct

FROM GL_Journal GL_Journal

WHERE GL_Journal.PostingType = 'A'

AND GL_Journal.IsActive = 'Y'

AND GL_Journal.AD_Client_ID =
(
    SELECT
        QueryParameters.AD_Client_ID

    FROM QueryParameters QueryParameters
)";

            protectedJournalSql =
                MRole.GetDefault(ctx)
                    .AddAccessSQL(
                        protectedJournalSql,
                        "GL_Journal",
                        MRole.SQL_FULLYQUALIFIED,
                        MRole.SQL_RO
                    );

            return protectedJournalSql;
        }

        private string BuildJournalDataSql()
        {
            return @"
SELECT
    ProtectedJournal.GL_Journal_ID,
    ProtectedJournal.AD_Client_ID,
    ProtectedJournal.AD_Org_ID,
    ProtectedJournal.C_AcctSchema_ID,
    ProtectedJournal.DateAcct,
    GL_JournalLine.AmtAcctDr,
    GL_JournalLine.AmtAcctCr

FROM ProtectedJournal ProtectedJournal

INNER JOIN GL_JournalLine GL_JournalLine ON
(
    ProtectedJournal.GL_Journal_ID =
        GL_JournalLine.GL_Journal_ID
)

WHERE GL_JournalLine.IsActive = 'Y'";
        }

        private string BuildSchemaCurrencySql()
        {
            return @"
SELECT
    ClientInfo.AD_Client_ID,
    AcctSchema.C_AcctSchema_ID,
    AcctSchema.C_Currency_ID,
    Currency.StdPrecision,
    Currency.ISO_Code,

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
)";
        }

        private string BuildPeriodRangeSql()
        {
            string currentDateSql =
                GetCurrentDateSql();

            string periodEndExclusiveSql =
                GetDateToExclusiveExpression(
                    "CurrentPeriod.EndDate"
                );

            return @"
SELECT
    ClientInfo.AD_Client_ID,

    CurrentPeriod.StartDate AS MonthDateFrom,

    CurrentPeriod.EndDate AS MonthDateTo,

    (
        SELECT
            MIN
            (
                PeriodYTD.StartDate
            )

        FROM C_Period PeriodYTD

        WHERE PeriodYTD.C_Year_ID =
            CurrentPeriod.C_Year_ID

        AND PeriodYTD.IsActive = 'Y'
    ) AS YTDDateFrom,

    CurrentPeriod.EndDate AS YTDDateTo

FROM AD_ClientInfo ClientInfo

INNER JOIN C_Year YearData ON
(
    YearData.C_Calendar_ID =
        ClientInfo.C_Calendar_ID
)

INNER JOIN C_Period CurrentPeriod ON
(
    CurrentPeriod.C_Year_ID =
        YearData.C_Year_ID
)

WHERE ClientInfo.IsActive = 'Y'

AND YearData.IsActive = 'Y'

AND CurrentPeriod.IsActive = 'Y'

AND ClientInfo.AD_Client_ID =
(
    SELECT
        QueryParameters.AD_Client_ID

    FROM QueryParameters QueryParameters
)

AND " + currentDateSql + @" >=
    CurrentPeriod.StartDate

AND " + currentDateSql + @" <
    " + periodEndExclusiveSql;
        }

        /// <summary>
        /// PostgreSQL requires the first argument of ROUND(value, precision)
        /// to be NUMERIC rather than DOUBLE PRECISION.
        /// </summary>
        private string BuildRoundedAmountExpression(
            string amountExpression
        )
        {
            string numericType =
                DB.IsOracle()
                    ? "NUMBER"
                    : "NUMERIC";

            return @"
ROUND
(
    CAST
    (
        " + amountExpression + @"
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
)";
        }

        private string GetCurrentDateSql()
        {
            if (DB.IsOracle())
            {
                return "TRUNC(CURRENT_DATE)";
            }

            return "CURRENT_DATE";
        }

        private string GetDateToExclusiveExpression(
            string dateColumn
        )
        {
            if (DB.IsOracle())
            {
                return "CAST("
                    + dateColumn
                    + " AS DATE) + 1";
            }

            return "CAST("
                + dateColumn
                + " AS DATE) + INTERVAL '1 day'";
        }

        private DateTime? GetNullableDateTime(
            object value
        )
        {
            if (
                value == null ||
                value == DBNull.Value
            )
            {
                return null;
            }

            DateTime parsedDate;

            if (value is DateTime)
            {
                return (DateTime)value;
            }

            if (
                DateTime.TryParse(
                    Convert.ToString(
                        value,
                        CultureInfo.InvariantCulture
                    ),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out parsedDate
                )
            )
            {
                return parsedDate;
            }

            return null;
        }

        /// <summary>
        /// Safely reads numeric provider values without assuming
        /// that Oracle and PostgreSQL return the same CLR type.
        /// </summary>
        private decimal GetSafeDecimal(
            object value,
            int precision
        )
        {
            if (
                value == null ||
                value == DBNull.Value
            )
            {
                return 0M;
            }

            precision =
                NormalizePrecision(
                    precision
                );

            try
            {
                decimal decimalValue =
                    Convert.ToDecimal(
                        value,
                        CultureInfo.InvariantCulture
                    );

                return Math.Round(
                    decimalValue,
                    precision,
                    MidpointRounding.AwayFromZero
                );
            }
            catch (
                InvalidCastException
            )
            {
                return ParseDecimalFallback(
                    value,
                    precision
                );
            }
            catch (
                FormatException
            )
            {
                return ParseDecimalFallback(
                    value,
                    precision
                );
            }
            catch (
                OverflowException
            )
            {
                return ParseDecimalFallback(
                    value,
                    precision
                );
            }
        }

        private decimal ParseDecimalFallback(
            object value,
            int precision
        )
        {
            string text =
                Convert.ToString(
                    value,
                    CultureInfo.InvariantCulture
                );

            decimal decimalResult;

            if (
                decimal.TryParse(
                    text,
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out decimalResult
                )
            )
            {
                return Math.Round(
                    decimalResult,
                    precision,
                    MidpointRounding.AwayFromZero
                );
            }

            double doubleResult;

            if (
                double.TryParse(
                    text,
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out doubleResult
                )
            )
            {
                double roundedValue =
                    Math.Round(
                        doubleResult,
                        precision,
                        MidpointRounding.AwayFromZero
                    );

                if (
                    roundedValue >
                        Convert.ToDouble(
                            decimal.MaxValue
                        ) ||
                    roundedValue <
                        Convert.ToDouble(
                            decimal.MinValue
                        )
                )
                {
                    return 0M;
                }

                return Convert.ToDecimal(
                    roundedValue,
                    CultureInfo.InvariantCulture
                );
            }

            return 0M;
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

        private string FormatDate(
            DateTime date
        )
        {
            return date.ToString(
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture
            );
        }

        private bool IsYTD(
            string period
        )
        {
            return
                !string.IsNullOrEmpty(
                    period
                )
                &&
                string.Equals(
                    period,
                    "ytd",
                    StringComparison.OrdinalIgnoreCase
                );
        }

        private string GetPeriodBadgeText(
            Ctx ctx,
            bool isYTD
        )
        {
            if (isYTD)
            {
                return GetMsg(
                    ctx,
                    "VAS_037_YTDBadge",
                    "YTD"
                );
            }

            return GetMsg(
                ctx,
                "VAS_037_MonthBadge",
                "Month"
            );
        }

        private string GetMsg(
            Ctx ctx,
            string key,
            string fallback
        )
        {
            if (ctx == null)
            {
                return fallback;
            }

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

        private object SessionExpired()
        {
            Ctx ctx = GetContext();
            string message = GetMsg(
                ctx,
                "SessionExpired",
                "Session Expired"
            );

            return new
            {
                success = false,
                errorKey = "SessionExpired",
                messageKey = "SessionExpired",
                error = message,
                hasData = false
            };
        }

        private Ctx GetContext()
        {
            if (Session["ctx"] == null)
            {
                return null;
            }

            return Session["ctx"] as Ctx;
        }

        private object Error(
            string message
        )
        {
            return new
            {
                success = false,
                errorKey = "VAS_ErrorLoading",
                messageKey = "VAS_ErrorLoading",
                error = message,
                hasData = false
            };
        }
    }
}
