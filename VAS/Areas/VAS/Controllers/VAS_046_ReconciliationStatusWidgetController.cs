
using System;
/*
 * AP Reconciliation Status Widget Controller
 *
 * Labels / Message Keys
 * #  | Current Text                         | Message Key
 * ---+--------------------------------------+--------------------------------
 * 1  | Reconciliation status                | VAS_046_ReconciliationStatus
 * 2  | Matched to bills + bank              | VAS_046_MatchedToBillsBank
 * 3  | Matched                              | VAS_046_Matched
 * 4  | Session Expired                      | SessionExpired
 */

using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Web.Mvc;
using VAdvantage.Classes;
using VAdvantage.DataBase;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VAS.Controllers
{
    public class VAS_046_ReconciliationStatusWidgetController : Controller
    {
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetReconciliationStatus()
        {
            if (Session["ctx"] == null)
            {
                Ctx fallbackCtx = Env.GetCtx();

                string sessionExpired = GetMsg(
                    fallbackCtx,
                    "SessionExpired",
                    "Session Expired"
                );

                return Json(
                    new
                    {
                        error = sessionExpired,
                        errorText = sessionExpired
                    },
                    JsonRequestBehavior.AllowGet
                );
            }

            Ctx ctx = Session["ctx"] as Ctx;

            if (ctx == null)
            {
                string sessionExpired = GetMsg(
                    Env.GetCtx(),
                    "SessionExpired",
                    "Session Expired"
                );

                return Json(
                    new
                    {
                        error = sessionExpired,
                        errorText = sessionExpired
                    },
                    JsonRequestBehavior.AllowGet
                );
            }

            IDataReader dr = null;

            try
            {
                SqlQueryData queryData =
                    BuildReconciliationStatusSql(
                        ctx
                    );

                dr = DB.ExecuteReader(
                    queryData.Sql,
                    queryData.Parameters,
                    null
                );

                int totalPayments = 0;
                int matchedPayments = 0;
                int manualMatchCount = 0;

                decimal matchedPercentage = 0;

                DateTime? dateFrom = null;
                DateTime? dateTo = null;

                if (
                    dr != null &&
                    dr.Read()
                )
                {
                    totalPayments =
                        Util.GetValueOfInt(
                            dr["TotalPayments"]
                        );

                    matchedPayments =
                        Util.GetValueOfInt(
                            dr["MatchedPayments"]
                        );

                    if (
                        dr["DateFrom"] !=
                        DBNull.Value
                    )
                    {
                        dateFrom =
                            Util.GetValueOfDateTime(
                                dr["DateFrom"]
                            );
                    }

                    if (
                        dr["DateTo"] !=
                        DBNull.Value
                    )
                    {
                        dateTo =
                            Util.GetValueOfDateTime(
                                dr["DateTo"]
                            );
                    }
                }

                if (totalPayments > 0)
                {
                    matchedPercentage =
                        decimal.Round(
                            matchedPayments * 100M /
                            totalPayments,
                            2,
                            MidpointRounding.AwayFromZero
                        );

                    manualMatchCount =
                        totalPayments -
                        matchedPayments;
                }

                return Json(
                    new
                    {
                        title = GetMsg(
                            ctx,
                            "VAS_046_ReconciliationStatus",
                            "Reconciliation status"
                        ),

                        subTitle = GetMsg(
                            ctx,
                            "VAS_046_MatchedToBillsBank",
                            "Matched to bills + bank"
                        ),

                        matchedLabel = GetMsg(
                            ctx,
                            "VAS_046_Matched",
                            "Matched"
                        ),

                        matchedPayments =
                            matchedPayments,

                        totalPayments =
                            totalPayments,

                        manualMatchCount =
                            manualMatchCount,

                        matchedPercentage =
                            matchedPercentage,

                        dateFrom =
                            dateFrom.HasValue
                                ? FormatDate(
                                    dateFrom.Value
                                )
                                : string.Empty,

                        dateTo =
                            dateTo.HasValue
                                ? FormatDate(
                                    dateTo.Value
                                )
                                : string.Empty
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
                        errorText = ex.Message
                    },
                    JsonRequestBehavior.AllowGet
                );
            }
            finally
            {
                CloseReader(dr);
            }
        }

        private SqlQueryData BuildReconciliationStatusSql(
            Ctx ctx
        )
        {
            string queryParametersFrom =
                DB.IsOracle()
                    ? " FROM DUAL"
                    : string.Empty;

            string currentDateSql =
                DB.IsOracle()
                    ? "TRUNC(CURRENT_DATE)"
                    : "CURRENT_DATE";

            /*
             * CAST(... AS DATE) + 1 works on both:
             * Oracle     : DATE + integer
             * PostgreSQL : DATE + integer
             */
            string periodEndExclusiveSql =
                "CAST(Period.EndDate AS DATE) + 1";

            string paymentFilteredSql = @"
SELECT
    Payment.C_Payment_ID,
    Payment.IsReconciled,
    Payment.DateAcct

FROM C_Payment Payment

WHERE Payment.IsActive = 'Y'

AND Payment.IsReceipt = 'N'

AND Payment.DocStatus IN
(
    'CO',
    'CL'
)";

            /*
             * Apply MRole only to the physical C_Payment table query.
             * Do not apply it to the final WITH query or CTE aliases.
             */
            paymentFilteredSql =
                MRole.GetDefault(ctx)
                    .AddAccessSQL(
                        paymentFilteredSql,
                        "Payment",
                        MRole.SQL_FULLYQUALIFIED,
                        MRole.SQL_RO
                    );

            string sql = @"
WITH QueryParameters AS
(
    SELECT
        @AD_Client_ID AS AD_Client_ID"
        + queryParametersFrom + @"
),
PeriodRange AS
(
    SELECT
        MIN
        (
            Period.StartDate
        ) AS DateFrom,

        MAX
        (
            Period.EndDate
        ) AS DateTo,

        MAX
        (
            CAST
            (
                Period.EndDate AS DATE
            ) + 1
        ) AS DateToExclusive

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

    AND YearData.IsActive = 'Y'

    AND Period.IsActive = 'Y'

    AND ClientInfo.AD_Client_ID =
    (
        SELECT
            QueryParameters.AD_Client_ID

        FROM QueryParameters QueryParameters
    )

    AND " + currentDateSql + @" >=
        Period.StartDate

    AND " + currentDateSql + @" <
        " + periodEndExclusiveSql + @"
),
PaymentFiltered AS
(
" + paymentFilteredSql + @"
)
SELECT
    COUNT
    (
        Payment.C_Payment_ID
    ) AS TotalPayments,

    COALESCE
    (
        SUM
        (
            CASE
                WHEN COALESCE
                (
                    Payment.IsReconciled,
                    'N'
                ) = 'Y'

                THEN 1
                ELSE 0
            END
        ),
        0
    ) AS MatchedPayments,

    MIN
    (
        PeriodRange.DateFrom
    ) AS DateFrom,

    MAX
    (
        PeriodRange.DateTo
    ) AS DateTo

FROM PaymentFiltered Payment

INNER JOIN PeriodRange PeriodRange ON
(
    Payment.DateAcct >=
        PeriodRange.DateFrom

    AND Payment.DateAcct <
        PeriodRange.DateToExclusive
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
                Sql = sql,
                Parameters = parameters
            };
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
            string msg =
                Msg.GetMsg(
                    ctx,
                    key
                );

            return
                !string.IsNullOrEmpty(msg) &&
                msg != key &&
                msg != "[" + key + "]"
                    ? msg
                    : fallback;
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
