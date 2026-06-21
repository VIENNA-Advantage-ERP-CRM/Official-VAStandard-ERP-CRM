
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
 * GL Journal Top Movement Controller
 *
 * -- Labels / Message Keys --------------------------------------------
 *  #  | Current Text                         | Message Key
 * ----+--------------------------------------+--------------------------------
 *  1  | YTD                                  | VAS_043_YTD
 *  2  | Top Ledger Movement                  | VAS_043_TopLedgerMovement
 *  3  | No Data                              | VAS_043_NoData
 *  4  | Top 10                               | VAS_043_Top10
 *  5  | Could Not Load Data                  | VAS_043_ErrorLoadingData
 * ---------------------------------------------------------------------
 */

namespace VAS.Controllers
{
    public class VAS_043_GLJournalTopMovementWidgetController : Controller
    {
        private class MovementRow
        {
            public string AccountCode
            {
                get;
                set;
            }

            public string AccountName
            {
                get;
                set;
            }

            public decimal TotalDebit
            {
                get;
                set;
            }

            public decimal TotalCredit
            {
                get;
                set;
            }

            public decimal NetMovement
            {
                get;
                set;
            }

            public bool IsCredit
            {
                get;
                set;
            }

            public int BarPct
            {
                get;
                set;
            }

            public int RankNo
            {
                get;
                set;
            }
        }

        public JsonResult GetTopMovement(
            string period,
            int pageNo = 1
        )
        {
            if (Session["ctx"] == null)
            {
                return Json(
                    JsonConvert.SerializeObject(
                        new
                        {
                            success = false,
                            error = "Session Expired",
                            hasData = false
                        }
                    ),
                    JsonRequestBehavior.AllowGet
                );
            }

            Ctx ctx =
                Session["ctx"] as Ctx;

            if (ctx == null)
            {
                return Json(
                    JsonConvert.SerializeObject(
                        new
                        {
                            success = false,
                            error = "Session Expired",
                            hasData = false
                        }
                    ),
                    JsonRequestBehavior.AllowGet
                );
            }

            IDataReader dr =
                null;

            try
            {
                bool isYtd =
                    string.Compare(
                        period,
                        "ytd",
                        StringComparison.OrdinalIgnoreCase
                    ) == 0;

                int pageSize =
                    5;

                int topLimit =
                    10;

                string periodStartExpression =
                    isYtd
                        ? "CurrentPeriod.YearStart"
                        : "CurrentPeriod.PeriodStart";

                string periodEndExclusiveExpression =
                    DB.IsOracle()
                        ? "CurrentPeriod.PeriodEnd + 1"
                        : "CurrentPeriod.PeriodEnd + INTERVAL '1 day'";

                string currentDateEndExpression =
                    DB.IsOracle()
                        ? "PeriodData.EndDate + 1"
                        : "PeriodData.EndDate + INTERVAL '1 day'";

                string clientParamSql =
                    DB.IsOracle()
                        ? "SELECT @ClientID AS AD_Client_ID FROM DUAL"
                        : "SELECT @ClientID AS AD_Client_ID";

                /*
                 * Oracle:
                 * ROUND requires NUMBER.
                 *
                 * PostgreSQL:
                 * ROUND(value, precision) requires NUMERIC.
                 */
                string numericType =
                    DB.IsOracle()
                        ? "NUMBER"
                        : "NUMERIC";

                string schemaCurrencySql = @"
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

FROM ClientParam ClientParam

INNER JOIN AD_ClientInfo ClientInfo ON
(
    ClientInfo.AD_Client_ID =
    ClientParam.AD_Client_ID
)

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

AND AcctSchema.IsActive = 'Y'";

                string currentPeriodSql = @"
SELECT
    CurrentPeriodData.C_Year_ID,
    CurrentPeriodData.PeriodName,
    CurrentPeriodData.YearName,
    CurrentPeriodData.PeriodStart,
    CurrentPeriodData.PeriodEnd,
    CurrentPeriodData.YearStart

FROM
(
    SELECT
        PeriodData.C_Year_ID,
        PeriodData.Name AS PeriodName,
        YearData.FiscalYear AS YearName,
        PeriodData.StartDate AS PeriodStart,
        PeriodData.EndDate AS PeriodEnd,
        YearRange.YearStart,

        ROW_NUMBER() OVER
        (
            ORDER BY
                PeriodData.StartDate DESC,
                PeriodData.C_Period_ID DESC
        ) AS RowNo

    FROM ClientParam ClientParam

    INNER JOIN AD_ClientInfo ClientInfo ON
    (
        ClientInfo.AD_Client_ID =
        ClientParam.AD_Client_ID
    )

    INNER JOIN C_Year YearData ON
    (
        YearData.C_Calendar_ID =
        ClientInfo.C_Calendar_ID
    )

    INNER JOIN C_Period PeriodData ON
    (
        PeriodData.C_Year_ID =
        YearData.C_Year_ID
    )

    INNER JOIN
    (
        SELECT
            C_Period.C_Year_ID,

            MIN
            (
                C_Period.StartDate
            ) AS YearStart

        FROM C_Period C_Period

        WHERE C_Period.IsActive = 'Y'

        GROUP BY
            C_Period.C_Year_ID
    ) YearRange ON
    (
        YearRange.C_Year_ID =
        PeriodData.C_Year_ID
    )

    WHERE ClientInfo.IsActive = 'Y'

    AND YearData.IsActive = 'Y'

    AND PeriodData.IsActive = 'Y'

    AND CURRENT_DATE >=
        PeriodData.StartDate

    AND CURRENT_DATE <
        " + currentDateEndExpression + @"
) CurrentPeriodData

WHERE CurrentPeriodData.RowNo = 1";

                string secureJournalSql = @"
SELECT
    GL_Journal.GL_Journal_ID,
    GL_Journal.AD_Client_ID,
    GL_Journal.AD_Org_ID,
    GL_Journal.C_AcctSchema_ID,
    GL_Journal.DateAcct

FROM GL_Journal GL_Journal

WHERE GL_Journal.DocStatus = 'CO'

AND GL_Journal.Posted = 'Y'

AND GL_Journal.IsActive = 'Y'";

                secureJournalSql =
                    MRole.GetDefault(ctx)
                        .AddAccessSQL(
                            secureJournalSql,
                            "GL_Journal",
                            MRole.SQL_FULLYQUALIFIED,
                            MRole.SQL_RO
                        );

                string ledgerMovementSql = @"
SELECT
    C_ElementValue.Value AS AccountCode,
    C_ElementValue.Name AS AccountName,

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
                        GL_JournalLine.AmtAcctDr,
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
                SchemaCurrency.StdPrecision,
                2
            )
            AS INTEGER
        )
    ) AS TotalDebit,

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
                        GL_JournalLine.AmtAcctCr,
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
                SchemaCurrency.StdPrecision,
                2
            )
            AS INTEGER
        )
    ) AS TotalCredit,

    ABS
    (
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
                            GL_JournalLine.AmtAcctDr,
                            0
                        )
                    ),
                    0
                )
                -
                COALESCE
                (
                    SUM
                    (
                        COALESCE
                        (
                            GL_JournalLine.AmtAcctCr,
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
                    SchemaCurrency.StdPrecision,
                    2
                )
                AS INTEGER
            )
        )
    ) AS NetMovement,

    CASE
        WHEN
            COALESCE
            (
                SUM
                (
                    COALESCE
                    (
                        GL_JournalLine.AmtAcctDr,
                        0
                    )
                ),
                0
            )
            -
            COALESCE
            (
                SUM
                (
                    COALESCE
                    (
                        GL_JournalLine.AmtAcctCr,
                        0
                    )
                ),
                0
            ) < 0

        THEN 'Y'

        ELSE 'N'
    END AS IsCredit,

    SchemaCurrency.Cur_Symbol,
    SchemaCurrency.ISO_Code,
    SchemaCurrency.StdPrecision,
    CurrentPeriod.PeriodName,
    CurrentPeriod.YearName,
    CurrentPeriod.PeriodStart,
    CurrentPeriod.PeriodEnd,
    CurrentPeriod.YearStart

FROM SecureJournal GL_Journal

INNER JOIN GL_JournalLine GL_JournalLine ON
(
    GL_Journal.GL_Journal_ID =
    GL_JournalLine.GL_Journal_ID

    AND GL_JournalLine.IsActive = 'Y'
)

INNER JOIN C_ElementValue C_ElementValue ON
(
    GL_JournalLine.Account_ID =
    C_ElementValue.C_ElementValue_ID

    AND C_ElementValue.IsActive = 'Y'
)

INNER JOIN SchemaCurrency SchemaCurrency ON
(
    SchemaCurrency.C_AcctSchema_ID =
    GL_Journal.C_AcctSchema_ID
)

INNER JOIN CurrentPeriod CurrentPeriod ON
(
    1 = 1
)

WHERE GL_Journal.C_AcctSchema_ID =
    SchemaCurrency.C_AcctSchema_ID

AND GL_Journal.DateAcct >=
    " + periodStartExpression + @"

AND GL_Journal.DateAcct <
    " + periodEndExclusiveExpression + @"

GROUP BY
    C_ElementValue.Value,
    C_ElementValue.Name,
    SchemaCurrency.Cur_Symbol,
    SchemaCurrency.ISO_Code,
    SchemaCurrency.StdPrecision,
    CurrentPeriod.PeriodName,
    CurrentPeriod.YearName,
    CurrentPeriod.PeriodStart,
    CurrentPeriod.PeriodEnd,
    CurrentPeriod.YearStart";

                string sql = @"
WITH ClientParam AS
(
" + clientParamSql + @"
),
SchemaCurrency AS
(
" + schemaCurrencySql + @"
),
CurrentPeriod AS
(
" + currentPeriodSql + @"
),
SecureJournal AS
(
" + secureJournalSql + @"
),
LedgerMovement AS
(
" + ledgerMovementSql + @"
),
RankedMovement AS
(
    SELECT
        LedgerMovement.*,

        ROW_NUMBER() OVER
        (
            ORDER BY
                LedgerMovement.NetMovement DESC
        ) AS RowNo

    FROM LedgerMovement LedgerMovement
),
TopMovement AS
(
    SELECT
        RankedMovement.*

    FROM RankedMovement RankedMovement

    WHERE RankedMovement.RowNo <= 10
)
SELECT
    TopMovement.AccountCode,
    TopMovement.AccountName,
    TopMovement.TotalDebit,
    TopMovement.TotalCredit,
    TopMovement.NetMovement,
    TopMovement.IsCredit,
    TopMovement.Cur_Symbol,
    TopMovement.ISO_Code,
    TopMovement.StdPrecision,
    TopMovement.PeriodName,
    TopMovement.YearName,
    TopMovement.PeriodStart,
    TopMovement.PeriodEnd,
    TopMovement.YearStart,
    TopMovement.RowNo,

    COUNT(1) OVER
    (
    ) AS TotalCount,

    10 AS TopLimit,

    5 AS PageSize

FROM TopMovement TopMovement

ORDER BY
    TopMovement.RowNo";

                SqlParameter[] parameters =
                {
                    new SqlParameter(
                        "@ClientID",
                        ctx.GetAD_Client_ID()
                    )
                };

                dr =
                    DB.ExecuteReader(
                        sql,
                        parameters,
                        null
                    );

                List<MovementRow> rows =
                    new List<MovementRow>();

                string curSymbol =
                    "";

                string isoCode =
                    "";

                int stdPrecision =
                    2;

                string periodName =
                    "";

                string yearName =
                    "";

                DateTime periodStart =
                    DateTime.MinValue;

                DateTime periodEnd =
                    DateTime.MinValue;

                DateTime yearStart =
                    DateTime.MinValue;

                int totalCount =
                    0;

                decimal maxAbs =
                    0m;

                while (
                    dr != null &&
                    dr.Read()
                )
                {
                    if (rows.Count == 0)
                    {
                        curSymbol =
                            Util.GetValueOfString(
                                dr["Cur_Symbol"]
                            );

                        isoCode =
                            Util.GetValueOfString(
                                dr["ISO_Code"]
                            );

                        stdPrecision =
                            Util.GetValueOfInt(
                                dr["StdPrecision"]
                            );

                        if (stdPrecision <= 0)
                        {
                            stdPrecision =
                                2;
                        }

                        periodName =
                            Util.GetValueOfString(
                                dr["PeriodName"]
                            );

                        yearName =
                            Util.GetValueOfString(
                                dr["YearName"]
                            );

                        totalCount =
                            Util.GetValueOfInt(
                                dr["TotalCount"]
                            );

                        if (
                            dr["PeriodStart"] !=
                            DBNull.Value
                        )
                        {
                            periodStart =
                                Convert.ToDateTime(
                                    dr["PeriodStart"]
                                ).Date;
                        }

                        if (
                            dr["PeriodEnd"] !=
                            DBNull.Value
                        )
                        {
                            periodEnd =
                                Convert.ToDateTime(
                                    dr["PeriodEnd"]
                                ).Date;
                        }

                        if (
                            dr["YearStart"] !=
                            DBNull.Value
                        )
                        {
                            yearStart =
                                Convert.ToDateTime(
                                    dr["YearStart"]
                                ).Date;
                        }
                    }

                    decimal netMovement =
                        Util.GetValueOfDecimal(
                            dr["NetMovement"]
                        );

                    if (netMovement > maxAbs)
                    {
                        maxAbs =
                            netMovement;
                    }

                    rows.Add(
                        new MovementRow
                        {
                            AccountCode =
                                Util.GetValueOfString(
                                    dr["AccountCode"]
                                ),

                            AccountName =
                                Util.GetValueOfString(
                                    dr["AccountName"]
                                ),

                            TotalDebit =
                                Util.GetValueOfDecimal(
                                    dr["TotalDebit"]
                                ),

                            TotalCredit =
                                Util.GetValueOfDecimal(
                                    dr["TotalCredit"]
                                ),

                            NetMovement =
                                netMovement,

                            IsCredit =
                                Util.GetValueOfString(
                                    dr["IsCredit"]
                                ) == "Y",

                            BarPct =
                                0,

                            RankNo =
                                Util.GetValueOfInt(
                                    dr["RowNo"]
                                )
                        }
                    );
                }

                if (dr != null)
                {
                    dr.Close();
                    dr = null;
                }

                for (
                    int index = 0;
                    index < rows.Count;
                    index++
                )
                {
                    rows[index].BarPct =
                        maxAbs > 0m
                            ? (int)Math.Round(
                                rows[index].NetMovement /
                                maxAbs *
                                100m,
                                MidpointRounding.AwayFromZero
                            )
                            : 0;
                }

                int totalPages =
                    totalCount > 0
                        ? (int)Math.Ceiling(
                            (decimal)totalCount /
                            pageSize
                        )
                        : 0;

                string periodLabel =
                    isYtd
                        ? GetMsg(
                            ctx,
                            "VAS_043_YTD",
                            "YTD"
                        ) +
                        " " +
                        yearName

                        : periodName +
                        " " +
                        yearName;

                DateTime displayStart =
                    isYtd
                        ? yearStart
                        : periodStart;

                if (rows.Count == 0)
                {
                    return Json(
                        JsonConvert.SerializeObject(
                            new
                            {
                                success = true,
                                error = "",

                                title = GetMsg(
                                    ctx,
                                    "VAS_043_TopLedgerMovement",
                                    "Top Ledger Movement"
                                ),

                                mainMetric = 0,
                                mainMetricText = "0",

                                description = GetMsg(
                                    ctx,
                                    "VAS_043_NoData",
                                    "No data found"
                                ),

                                badgeText =
                                    periodLabel,

                                dateFrom =
                                    displayStart ==
                                    DateTime.MinValue
                                        ? ""
                                        : FormatDate(
                                            displayStart
                                        ),

                                dateTo =
                                    periodEnd ==
                                    DateTime.MinValue
                                        ? ""
                                        : FormatDate(
                                            periodEnd
                                        ),

                                currencyISO =
                                    isoCode,

                                currencySymbol =
                                    curSymbol,

                                stdPrecision =
                                    stdPrecision,

                                hasData = false,

                                Accounts =
                                    rows,

                                TotalCount = 0,
                                TotalPages = 0,

                                PageNo =
                                    pageNo,

                                PageSize =
                                    pageSize,

                                TopLimit =
                                    topLimit
                            }
                        ),
                        JsonRequestBehavior.AllowGet
                    );
                }

                return Json(
                    JsonConvert.SerializeObject(
                        new
                        {
                            success = true,
                            error = "",

                            title = GetMsg(
                                ctx,
                                "VAS_043_TopLedgerMovement",
                                "Top Ledger Movement"
                            ),

                            mainMetric =
                                totalCount,

                            mainMetricText =
                                totalCount.ToString(),

                            description =
                                periodLabel,

                            badgeText = GetMsg(
                                ctx,
                                "VAS_043_Top10",
                                "Top 10"
                            ),

                            dateFrom =
                                displayStart ==
                                DateTime.MinValue
                                    ? ""
                                    : FormatDate(
                                        displayStart
                                    ),

                            dateTo =
                                periodEnd ==
                                DateTime.MinValue
                                    ? ""
                                    : FormatDate(
                                        periodEnd
                                    ),

                            currencyISO =
                                isoCode,

                            currencySymbol =
                                curSymbol,

                            stdPrecision =
                                stdPrecision,

                            hasData = true,

                            Accounts =
                                rows,

                            TotalCount =
                                totalCount,

                            TotalPages =
                                totalPages,

                            PageNo =
                                pageNo,

                            PageSize =
                                pageSize,

                            TopLimit =
                                topLimit
                        }
                    ),
                    JsonRequestBehavior.AllowGet
                );
            }
            catch
            {
                if (dr != null)
                {
                    dr.Close();
                    dr = null;
                }

                return Json(
                    JsonConvert.SerializeObject(
                        new
                        {
                            success = false,

                            error = GetMsg(
                                ctx,
                                "VAS_043_ErrorLoadingData",
                                "Could not load data"
                            ),

                            hasData = false
                        }
                    ),
                    JsonRequestBehavior.AllowGet
                );
            }
            finally
            {
                if (dr != null)
                {
                    dr.Close();
                }
            }
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
                ) ||
                msg == key
            )
            {
                return fallback;
            }

            return msg;
        }
    }
}
