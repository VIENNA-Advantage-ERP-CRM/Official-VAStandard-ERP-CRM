
using CoreLibrary.DataBase;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Web.Mvc;
using VAdvantage.Model;
using VAdvantage.Utility;

/*
 * GL Journal Volume Controller
 *
 * -- Labels / Message Keys --------------------------------------------
 *  #  | Current Text                         | Message Key
 * ----+--------------------------------------+--------------------------------
 *  1  | Session Expired                      | VAS_042_SessionExpired
 *  2  | Error Loading Data                   | VAS_042_ErrorLoadingData
 *  3  | GL Journal Volume                    | VAS_042_GLJournalVolume
 *  4  | No Data                              | VIS_NoData
 * ---------------------------------------------------------------------
 */

namespace VAS.Controllers
{
    /// <summary>
    /// Controller for the GL Journal Volume by Day chart widget.
    ///
    /// Labels / Message Keys
    /// 1 | Session Expired        | VAS_042_SessionExpired
    /// 2 | Error loading data     | VAS_042_ErrorLoadingData
    /// 3 | GL Journal Volume      | VAS_042_GLJournalVolume
    /// 4 | No data available      | VIS_NoData
    /// </summary>
    public class VAS_042_GLJournalVolumeWidgetController : Controller
    {
        public JsonResult GetVolumeByDay(string period)
        {
            if (Session["ctx"] == null)
            {
                return Json(JsonConvert.SerializeObject(new
                {
                    success = false,
                    errorKey = "SessionExpired",
                    messageKey = "SessionExpired",
                    error = "Session Expired",
                    hasData = false
                }), JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            if (ctx == null)
            {
                return Json(JsonConvert.SerializeObject(new
                {
                    success = false,
                    errorKey = "SessionExpired",
                    messageKey = "SessionExpired",
                    error = "Session Expired",
                    hasData = false
                }), JsonRequestBehavior.AllowGet);
            }

            bool isWeek = string.Compare(
                period,
                "week",
                StringComparison.OrdinalIgnoreCase
            ) == 0;

            DateTime today = DateTime.Now.Date;

            int daysFromMonday =
                (int)today.DayOfWeek -
                (int)DayOfWeek.Monday;

            if (daysFromMonday < 0)
            {
                daysFromMonday += 7;
            }

            DateTime weekStart =
                today.AddDays(
                    -daysFromMonday
                );

            DateTime weekEndExclusive =
                weekStart.AddDays(7);

            string dateFilter = isWeek
                ? GetDateFilter(
                    "ProtectedJournal.DateAcct",
                    weekStart,
                    weekEndExclusive
                )
                : @"
AND ProtectedJournal.DateAcct >=
    PeriodRange.MonthDateFrom

AND ProtectedJournal.DateAcct <
    " + GetDateToExclusiveExpression(
                    "PeriodRange.MonthDateTo"
                );

            string monthJoinType =
                isWeek
                    ? "LEFT OUTER JOIN"
                    : "INNER JOIN";

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
             *
             * ROUND(value, precision)
             *
             * Oracle uses NUMBER.
             */
            string numericType =
                DB.IsOracle()
                    ? "NUMBER"
                    : "NUMERIC";

            string sql = @"
WITH QueryParameters AS
(
    SELECT
        @AD_Client_ID AS AD_Client_ID"
        + queryParametersFrom + @"
),
SchemaCurrency AS
(
    SELECT
        ClientInfo.AD_Client_ID,
        AcctSchema.C_AcctSchema_ID,
        AcctSchema.Name AS AcctSchemaName,
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

    AND ClientInfo.AD_Client_ID =
    (
        SELECT
            QueryParameters.AD_Client_ID

        FROM QueryParameters QueryParameters
    )
),
PeriodRange AS
(
    SELECT
        ClientInfo.AD_Client_ID,
        CurrentPeriod.StartDate AS MonthDateFrom,
        CurrentPeriod.EndDate AS MonthDateTo,
        CurrentPeriod.C_Year_ID

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

    AND CurrentPeriod.IsActive = 'Y'

    AND ClientInfo.AD_Client_ID =
    (
        SELECT
            QueryParameters.AD_Client_ID

        FROM QueryParameters QueryParameters
    )

    AND CURRENT_DATE >=
        CurrentPeriod.StartDate

    AND CURRENT_DATE <
        " + GetDateToExclusiveExpression(
                    "CurrentPeriod.EndDate"
                ) + @"
),
ProtectedJournal AS
(
" + BuildProtectedJournalSql(ctx) + @"
),
JournalData AS
(
    SELECT
        ProtectedJournal.GL_Journal_ID,
        ProtectedJournal.AD_Client_ID,
        ProtectedJournal.AD_Org_ID,
        ProtectedJournal.C_AcctSchema_ID,
        ProtectedJournal.DateAcct,
        GL_JournalLine.AmtAcctDr,
        GL_JournalLine.AmtAcctCr

    FROM ProtectedJournal ProtectedJournal

    INNER JOIN SchemaCurrency SchemaCurrency ON
    (
        SchemaCurrency.AD_Client_ID =
        ProtectedJournal.AD_Client_ID

        AND SchemaCurrency.C_AcctSchema_ID =
        ProtectedJournal.C_AcctSchema_ID
    )

    " + monthJoinType + @" PeriodRange PeriodRange ON
    (
        PeriodRange.AD_Client_ID =
        ProtectedJournal.AD_Client_ID
    )

    LEFT OUTER JOIN GL_JournalLine GL_JournalLine ON
    (
        ProtectedJournal.GL_Journal_ID =
        GL_JournalLine.GL_Journal_ID

        AND GL_JournalLine.IsActive = 'Y'
    )

    WHERE 1 = 1

" + dateFilter + @"
),
DailyData AS
(
    SELECT
        JournalData.DateAcct,

        COUNT
        (
            DISTINCT JournalData.GL_Journal_ID
        ) AS JournalCount,

        ROUND
        (
            CAST
            (
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
        ) AS NetValue,

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
            PeriodRange.MonthDateFrom
        ) AS MonthDateFrom,

        MAX
        (
            PeriodRange.MonthDateTo
        ) AS MonthDateTo

    FROM JournalData JournalData

    INNER JOIN SchemaCurrency SchemaCurrency ON
    (
        SchemaCurrency.AD_Client_ID =
        JournalData.AD_Client_ID

        AND SchemaCurrency.C_AcctSchema_ID =
        JournalData.C_AcctSchema_ID
    )

    LEFT OUTER JOIN PeriodRange PeriodRange ON
    (
        PeriodRange.AD_Client_ID =
        JournalData.AD_Client_ID
    )

    GROUP BY
        JournalData.DateAcct
)
SELECT
    DailyData.DateAcct,
    DailyData.JournalCount,
    DailyData.NetValue,
    DailyData.CurSymbol,
    DailyData.ISOCode,
    DailyData.StdPrecision,
    DailyData.MonthDateFrom,
    DailyData.MonthDateTo

FROM DailyData DailyData

ORDER BY
    DailyData.DateAcct ASC";

            SqlParameter[] parameters =
            {
                new SqlParameter(
                    "@AD_Client_ID",
                    ctx.GetAD_Client_ID()
                )
            };

            try
            {
                DataSet ds =
                    DB.ExecuteDataset(
                        sql,
                        parameters,
                        null
                    );

                Dictionary<int, int> countMap =
                    new Dictionary<int, int>();

                Dictionary<int, decimal> netMap =
                    new Dictionary<int, decimal>();

                string curSymbol = "";
                string isoCode = "";
                int stdPrecision = 2;

                DateTime? monthDateFrom = null;
                DateTime? monthDateTo = null;

                if (
                    ds != null &&
                    ds.Tables.Count > 0 &&
                    ds.Tables[0].Rows.Count > 0
                )
                {
                    foreach (
                        DataRow row in
                        ds.Tables[0].Rows
                    )
                    {
                        DateTime? dateAcct =
                            Util.GetValueOfDateTime(
                                row["DateAcct"]
                            );

                        if (!dateAcct.HasValue)
                        {
                            continue;
                        }

                        int ymd =
                            ToYmd(
                                dateAcct.Value
                            );

                        countMap[ymd] =
                            Util.GetValueOfInt(
                                row["JournalCount"]
                            );

                        netMap[ymd] =
                            Decimal.Round(
                                Util.GetValueOfDecimal(
                                    row["NetValue"]
                                ),
                                GetSafePrecision(
                                    Util.GetValueOfInt(
                                        row["StdPrecision"]
                                    )
                                ),
                                MidpointRounding.AwayFromZero
                            );

                        curSymbol =
                            Util.GetValueOfString(
                                row["CurSymbol"]
                            );

                        isoCode =
                            Util.GetValueOfString(
                                row["ISOCode"]
                            );

                        stdPrecision =
                            GetSafePrecision(
                                Util.GetValueOfInt(
                                    row["StdPrecision"]
                                )
                            );

                        if (!monthDateFrom.HasValue)
                        {
                            monthDateFrom =
                                Util.GetValueOfDateTime(
                                    row["MonthDateFrom"]
                                );
                        }

                        if (!monthDateTo.HasValue)
                        {
                            monthDateTo =
                                Util.GetValueOfDateTime(
                                    row["MonthDateTo"]
                                );
                        }
                    }
                }

                DateTime dateFrom;
                DateTime dateToExclusive;

                if (isWeek)
                {
                    dateFrom =
                        weekStart;

                    dateToExclusive =
                        weekEndExclusive;
                }
                else
                {
                    dateFrom =
                        monthDateFrom.HasValue
                            ? monthDateFrom.Value.Date
                            : new DateTime(
                                today.Year,
                                today.Month,
                                1
                            );

                    dateToExclusive =
                        monthDateTo.HasValue
                            ? monthDateTo.Value.Date.AddDays(1)
                            : dateFrom.AddMonths(1);
                }

                List<object> days =
                    new List<object>();

                for (
                    DateTime date = dateFrom;
                    date < dateToExclusive;
                    date = date.AddDays(1)
                )
                {
                    int ymd =
                        ToYmd(date);

                    int journalCount =
                        countMap.ContainsKey(ymd)
                            ? countMap[ymd]
                            : 0;

                    decimal netValue =
                        netMap.ContainsKey(ymd)
                            ? netMap[ymd]
                            : 0m;

                    bool isWeekendDay =
                        date.DayOfWeek ==
                            DayOfWeek.Saturday ||
                        date.DayOfWeek ==
                            DayOfWeek.Sunday;

                    bool isThisWeek =
                        date >= weekStart &&
                        date < weekEndExclusive;

                    days.Add(new
                    {
                        DayNum =
                            date.Day,

                        DateStr =
                            date.ToString(
                                "MMM dd"
                            ),

                        DayOfWeek =
                            (int)date.DayOfWeek,

                        JournalCount =
                            journalCount,

                        NetValue =
                            netValue,

                        IsWeekend =
                            isWeekendDay,

                        IsThisWeek =
                            isThisWeek
                    });
                }

                string periodLabel =
                    BuildPeriodLabel(
                        dateFrom,
                        dateToExclusive.AddDays(-1)
                    );

                return Json(
                    JsonConvert.SerializeObject(
                        new
                        {
                            success = true,
                            error = "",

                            title = GetMsg(
                                ctx,
                                "VAS_042_GLJournalVolume",
                                "GL Journal Volume"
                            ),

                            Days =
                                days,

                            PeriodLabel =
                                periodLabel,

                            Period =
                                isWeek
                                    ? "week"
                                    : "month",

                            CurSymbol =
                                curSymbol,

                            ISOCode =
                                isoCode,

                            StdPrecision =
                                stdPrecision,

                            dateFrom =
                                FormatDate(
                                    dateFrom
                                ),

                            dateTo =
                                FormatDate(
                                    dateToExclusive.AddDays(-1)
                                ),

                            hasData =
                                days.Count > 0
                        }
                    ),
                    JsonRequestBehavior.AllowGet
                );
            }
            catch
            {
                return Json(
                    JsonConvert.SerializeObject(
                        new
                        {
                            success = false,

                            error = GetMsg(
                                ctx,
                                "VAS_042_ErrorLoadingData",
                                "Error loading data."
                            ),

                            hasData = false
                        }
                    ),
                    JsonRequestBehavior.AllowGet
                );
            }
        }

        private string BuildProtectedJournalSql(
            Ctx ctx
        )
        {
            string sql = @"
SELECT
    GL_Journal.GL_Journal_ID,
    GL_Journal.AD_Client_ID,
    GL_Journal.AD_Org_ID,
    GL_Journal.C_AcctSchema_ID,
    GL_Journal.DocumentNo,
    GL_Journal.DateAcct

FROM GL_Journal GL_Journal

WHERE GL_Journal.IsActive = 'Y'

AND GL_Journal.AD_Client_ID =
(
    SELECT
        QueryParameters.AD_Client_ID

    FROM QueryParameters QueryParameters
)";

            sql =
                MRole.GetDefault(ctx)
                    .AddAccessSQL(
                        sql,
                        "GL_Journal",
                        MRole.SQL_FULLYQUALIFIED,
                        MRole.SQL_RO
                    );

            return sql;
        }

        private string GetDateFilter(
            string columnName,
            DateTime dateFrom,
            DateTime dateTo
        )
        {
            string dateFromText =
                FormatDate(
                    dateFrom
                );

            string dateToText =
                FormatDate(
                    dateTo
                );

            if (DB.IsOracle())
            {
                return @"
AND " + columnName + @" >=
    TO_DATE
    (
        '" + dateFromText + @"',
        'YYYY-MM-DD'
    )

AND " + columnName + @" <
    TO_DATE
    (
        '" + dateToText + @"',
        'YYYY-MM-DD'
    )";
            }

            return @"
AND " + columnName + @" >=
    DATE '" + dateFromText + @"'

AND " + columnName + @" <
    DATE '" + dateToText + @"'";
        }

        private string GetDateToExclusiveExpression(
            string dateColumn
        )
        {
            if (DB.IsOracle())
            {
                return
                    dateColumn +
                    " + 1";
            }

            return
                dateColumn +
                " + INTERVAL '1 day'";
        }

        private string FormatDate(
            DateTime date
        )
        {
            return date.ToString(
                "yyyy-MM-dd"
            );
        }

        private int ToYmd(
            DateTime date
        )
        {
            return
                date.Year * 10000 +
                date.Month * 100 +
                date.Day;
        }

        private int GetSafePrecision(
            int precision
        )
        {
            if (precision < 0)
            {
                return 2;
            }

            return precision;
        }

        private string BuildPeriodLabel(
            DateTime dateFrom,
            DateTime dateTo
        )
        {
            bool sameMonth =
                dateFrom.Month == dateTo.Month &&
                dateFrom.Year == dateTo.Year;

            string endFormat =
                sameMonth
                    ? "d"
                    : "MMM d";

            return
                dateFrom.ToString("MMM d") +
                " – " +
                dateTo.ToString(endFormat);
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
                msg == key
            )
            {
                return fallback;
            }

            return msg;
        }
    }
}

