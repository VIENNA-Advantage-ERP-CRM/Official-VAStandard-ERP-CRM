
using System;

/*
 * Cleared AP Payment Widget Controller
 *
 * Labels / Message Keys
 * #  | Current Text                                   | Message Key
 * ---+------------------------------------------------+------------------------------------------
 * 1  | Unreconciled                                   | VAS_027_messageCleared
 * 2  | Of last month's AP payments reconciled         | VAS_027_messageAPPaymentClearedWhy
 * 3  | Session Expired                                | SessionExpired
 * 4  | Payments                                       | VAS_027_messagePayments
 * 5  | Awaiting bank match                            | VAS_027_messageAwaitingBankMatch
 */

using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Web.Mvc;
using VAdvantage.DataBase;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VAS.Controllers
{
    public class VAS_027_ClearedAPPaymentWidgetController : Controller
    {
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetClearedAPPayment()
        {
            Ctx ctx = GetContext();

            if (ctx == null)
            {
                return GetSessionExpiredResult();
            }

            IDataReader dr = null;

            try
            {
                SqlQueryData queryData =
                    BuildClearedAPPaymentSql(ctx);

                dr = DB.ExecuteReader(
                    queryData.Sql,
                    queryData.Parameters,
                    null
                );

                int totalPayments = 0;
                int clearedPayments = 0;
                decimal clearedPercentage = 0;

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

                    clearedPayments =
                        Util.GetValueOfInt(
                            dr["ClearedPayments"]
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
                    clearedPercentage =
                        Math.Round(
                            clearedPayments *
                            100M /
                            totalPayments,
                            2,
                            MidpointRounding.AwayFromZero
                        );
                }

                return Json(
                    new
                    {
                        title = GetMsg(
                            ctx,
                            "VAS_027_messageCleared",
                            "Cleared"
                        ),

                        description = GetMsg(
                            ctx,
                            "VAS_027_messageAPPaymentClearedWhy",
                            "Of last month's AP payments reconciled"
                        ),

                        value =
                            clearedPercentage,

                        clearedPercentage =
                            clearedPercentage,

                        totalPayments =
                            totalPayments,

                        clearedPayments =
                            clearedPayments,

                        precision = 2,

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
                                : ""
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

        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetUnreconciledAPPayments(
            int pageNo = 1,
            int pageSize = 5
        )
        {
            Ctx ctx = GetContext();

            if (ctx == null)
            {
                return GetSessionExpiredResult();
            }

            if (pageNo <= 0)
            {
                pageNo = 1;
            }

            if (pageSize <= 0)
            {
                pageSize = 8;
            }

            if (pageSize > 20)
            {
                pageSize = 8;
            }

            string paymentAccessSql =
                BuildUnreconciledPaymentAccessSql(
                    ctx
                );

            string paymentsDataSql =
                BuildPaymentsDataSql(
                    paymentAccessSql
                );

            string queryParametersFrom =
                DB.IsOracle()
                    ? " FROM DUAL"
                    : string.Empty;

            string summarySql = @"
WITH QueryParameters AS
(
    SELECT
        @AD_Client_ID AS AD_Client_ID"
        + queryParametersFrom + @"
),
PaymentsData AS
(
" + paymentsDataSql + @"
)
SELECT
    COUNT(1) AS TotalRecords,

    COALESCE
    (
        SUM
        (
            PaymentsData.Pay_Amount
        ),
        0
    ) AS TotalAmount,

    MIN
    (
        PaymentsData.Trx_Date
    ) AS OldestDate,

    COALESCE
    (
        SUM
        (
            PaymentsData.Auto_Match_Candidate
        ),
        0
    ) AS AutoMatchCandidates,

    COUNT
    (
        DISTINCT PaymentsData.C_Currency_ID
    ) AS CurrencyCount

FROM PaymentsData PaymentsData";

            SqlParameter[] summaryParameters =
            {
                new SqlParameter(
                    "@AD_Client_ID",
                    ctx.GetAD_Client_ID()
                )
            };

            List<object> rows =
                new List<object>();

            int totalRecords = 0;
            int totalPages = 0;
            int autoMatchCandidates = 0;
            int currencyCount = 0;

            decimal totalAmount = 0;

            DateTime? oldestDate = null;
            DateTime currentDate = DateTime.Today;

            string summaryCurrencyIso =
                string.Empty;

            string summaryCurrencySymbol =
                string.Empty;

            int summaryPrecision = 2;

            IDataReader summaryReader = null;
            IDataReader rowsReader = null;

            string rowsSql = string.Empty;

            try
            {
                summaryReader =
                    DB.ExecuteReader(
                        summarySql,
                        summaryParameters,
                        null
                    );

                if (
                    summaryReader != null &&
                    summaryReader.Read()
                )
                {
                    totalRecords =
                        Util.GetValueOfInt(
                            summaryReader[
                                "TotalRecords"
                            ]
                        );

                    totalAmount =
                        Util.GetValueOfDecimal(
                            summaryReader[
                                "TotalAmount"
                            ]
                        );

                    autoMatchCandidates =
                        Util.GetValueOfInt(
                            summaryReader[
                                "AutoMatchCandidates"
                            ]
                        );

                    currencyCount =
                        Util.GetValueOfInt(
                            summaryReader[
                                "CurrencyCount"
                            ]
                        );

                    if (
                        summaryReader["OldestDate"] !=
                        DBNull.Value
                    )
                    {
                        oldestDate =
                            Util.GetValueOfDateTime(
                                summaryReader[
                                    "OldestDate"
                                ]
                            );
                    }
                }

                CloseReader(summaryReader);
                summaryReader = null;

                totalPages =
                    totalRecords > 0
                        ? Convert.ToInt32(
                            Math.Ceiling(
                                (decimal)totalRecords /
                                pageSize
                            )
                        )
                        : 0;

                if (
                    totalPages > 0 &&
                    pageNo > totalPages
                )
                {
                    pageNo = totalPages;
                }

                int startRow =
                    ((pageNo - 1) * pageSize) + 1;

                int endRow =
                    pageNo * pageSize;

                rowsSql = @"
WITH QueryParameters AS
(
    SELECT
        @AD_Client_ID AS AD_Client_ID,
        @StartRow AS StartRow,
        @EndRow AS EndRow"
        + queryParametersFrom + @"
),
PaymentsData AS
(
" + paymentsDataSql + @"
),
NumberedData AS
(
    SELECT
        PaymentsData.Payment_ID,
        PaymentsData.Trx_Date,
        PaymentsData.Document_No,
        PaymentsData.Vendor_Name,
        PaymentsData.Bank_Name,
        PaymentsData.Account_No,
        PaymentsData.Pay_Amount,
        PaymentsData.C_Currency_ID,
        PaymentsData.Payment_Currency,
        PaymentsData.Payment_Currency_Symbol,
        PaymentsData.Std_Precision,
        PaymentsData.Payment_Method,

        ROW_NUMBER() OVER
        (
            ORDER BY
                PaymentsData.Trx_Date DESC,
                PaymentsData.Document_No DESC,
                PaymentsData.Payment_ID DESC
        ) AS RowNumber

    FROM PaymentsData PaymentsData
)
SELECT
    NumberedData.Payment_ID,
    NumberedData.Trx_Date,
    NumberedData.Document_No,
    NumberedData.Vendor_Name,
    NumberedData.Bank_Name,
    NumberedData.Account_No,
    NumberedData.Pay_Amount,
    NumberedData.C_Currency_ID,
    NumberedData.Payment_Currency,
    NumberedData.Payment_Currency_Symbol,
    NumberedData.Std_Precision,
    NumberedData.Payment_Method,
    CURRENT_DATE AS CurrentDate

FROM NumberedData NumberedData

WHERE NumberedData.RowNumber >=
(
    SELECT
        QueryParameters.StartRow

    FROM QueryParameters QueryParameters
)

AND NumberedData.RowNumber <=
(
    SELECT
        QueryParameters.EndRow

    FROM QueryParameters QueryParameters
)

ORDER BY
    NumberedData.RowNumber";

                SqlParameter[] rowsParameters =
                {
                    new SqlParameter(
                        "@AD_Client_ID",
                        ctx.GetAD_Client_ID()
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

                rowsReader =
                    DB.ExecuteReader(
                        rowsSql,
                        rowsParameters,
                        null
                    );

                while (
                    rowsReader != null &&
                    rowsReader.Read()
                )
                {
                    if (
                        rowsReader["CurrentDate"] !=
                        DBNull.Value
                    )
                    {
                        DateTime? databaseDate =
                            Util.GetValueOfDateTime(
                                rowsReader[
                                    "CurrentDate"
                                ]
                            );

                        if (databaseDate.HasValue)
                        {
                            currentDate =
                                databaseDate.Value.Date;
                        }
                    }

                    DateTime? trxDate =
                        Util.GetValueOfDateTime(
                            rowsReader[
                                "Trx_Date"
                            ]
                        );

                    int rowPrecision = 2;

                    if (
                        rowsReader["Std_Precision"] !=
                        DBNull.Value
                    )
                    {
                        rowPrecision =
                            Util.GetValueOfInt(
                                rowsReader[
                                    "Std_Precision"
                                ]
                            );
                    }

                    if (rowPrecision < 0)
                    {
                        rowPrecision = 2;
                    }

                    decimal amount =
                        Math.Round(
                            Util.GetValueOfDecimal(
                                rowsReader[
                                    "Pay_Amount"
                                ]
                            ),
                            rowPrecision,
                            MidpointRounding.AwayFromZero
                        );

                    string documentNo =
                        Util.GetValueOfString(
                            rowsReader[
                                "Document_No"
                            ]
                        );

                    string vendor =
                        Util.GetValueOfString(
                            rowsReader[
                                "Vendor_Name"
                            ]
                        );

                    string bankName =
                        Util.GetValueOfString(
                            rowsReader[
                                "Bank_Name"
                            ]
                        );

                    string accountNo =
                        Util.GetValueOfString(
                            rowsReader[
                                "Account_No"
                            ]
                        );

                    string currencyIso =
                        Util.GetValueOfString(
                            rowsReader[
                                "Payment_Currency"
                            ]
                        );

                    string currencySymbol =
                        Util.GetValueOfString(
                            rowsReader[
                                "Payment_Currency_Symbol"
                            ]
                        );

                    string paymentMethod =
                        Util.GetValueOfString(
                            rowsReader[
                                "Payment_Method"
                            ]
                        );

                    if (
                        string.IsNullOrWhiteSpace(
                            summaryCurrencyIso
                        )
                    )
                    {
                        summaryCurrencyIso =
                            currencyIso;

                        summaryCurrencySymbol =
                            currencySymbol;

                        summaryPrecision =
                            rowPrecision;
                    }

                    rows.Add(
                        new
                        {
                            paymentId =
                                Util.GetValueOfInt(
                                    rowsReader[
                                        "Payment_ID"
                                    ]
                                ),

                            date =
                                trxDate.HasValue
                                    ? trxDate.Value.ToString(
                                        "yyyy-MM-dd",
                                        CultureInfo.InvariantCulture
                                    )
                                    : "",

                            paymentNo =
                                documentNo,

                            vendor =
                                vendor,

                            bankName =
                                bankName,

                            accountNo =
                                accountNo,

                            amount =
                                amount,

                            currencyIso =
                                currencyIso,

                            curSymbol =
                                currencySymbol,

                            stdPrecision =
                                rowPrecision,

                            method =
                                paymentMethod,

                            whyUnreconciled =
                                GetUnreconciledReason(
                                    currentDate,
                                    trxDate,
                                    documentNo,
                                    bankName,
                                    accountNo
                                )
                        }
                    );
                }

                int oldestDays =
                    oldestDate.HasValue
                        ? Math.Max(
                            0,
                            Convert.ToInt32(
                                (
                                    currentDate.Date -
                                    oldestDate.Value.Date
                                ).TotalDays
                            )
                        )
                        : 0;

                int autoMatchRate =
                    totalRecords > 0
                        ? Convert.ToInt32(
                            Math.Round(
                                autoMatchCandidates *
                                100M /
                                totalRecords,
                                0,
                                MidpointRounding.AwayFromZero
                            )
                        )
                        : 0;

                bool hasMixedCurrencies =
                    currencyCount > 1;

                return Json(
                    new
                    {
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
                            hasMixedCurrencies
                                ? 0
                                : Math.Round(
                                    totalAmount,
                                    summaryPrecision,
                                    MidpointRounding.AwayFromZero
                                ),

                        currencyIso =
                            hasMixedCurrencies
                                ? ""
                                : summaryCurrencyIso,

                        curSymbol =
                            hasMixedCurrencies
                                ? ""
                                : summaryCurrencySymbol,

                        stdPrecision =
                            summaryPrecision,

                        hasMixedCurrencies =
                            hasMixedCurrencies,

                        oldestDays =
                            oldestDays,

                        autoMatchRate =
                            autoMatchRate
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
                        summarySql = summarySql,
                        rowsSql = rowsSql
                    },
                    JsonRequestBehavior.AllowGet
                );
            }
            finally
            {
                CloseReader(summaryReader);
                CloseReader(rowsReader);
            }
        }

        private SqlQueryData BuildClearedAPPaymentSql(
            Ctx ctx
        )
        {
            string queryParametersFrom =
                DB.IsOracle()
                    ? " FROM DUAL"
                    : string.Empty;

            string textType =
                DB.IsOracle()
                    ? "VARCHAR2(4000)"
                    : "VARCHAR(4000)";

            string paymentAccessSql = @"
SELECT
    Payment.C_Payment_ID,
    Payment.DateTrx,

    CAST
    (
        Payment.IsReconciled
        AS " + textType + @"
    ) AS IsReconciled

FROM C_Payment Payment

WHERE CAST
(
    Payment.IsActive
    AS " + textType + @"
) = 'Y'

AND CAST
(
    Payment.IsReceipt
    AS " + textType + @"
) = 'N'

AND CAST
(
    Payment.DocStatus
    AS " + textType + @"
) IN
(
    'CO',
    'CL'
)

AND Payment.AD_Client_ID =
(
    SELECT
        QueryParameters.AD_Client_ID

    FROM QueryParameters QueryParameters
)";

            paymentAccessSql =
                MRole.GetDefault(ctx)
                    .AddAccessSQL(
                        paymentAccessSql,
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
CurrentPeriodSource AS
(
    SELECT
        YearData.C_Calendar_ID,
        Period.C_Period_ID,
        Period.StartDate,
        Period.EndDate,

        ROW_NUMBER() OVER
        (
            ORDER BY
                Period.StartDate DESC,
                Period.C_Period_ID DESC
        ) AS PeriodRowNumber

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
    (
        SELECT
            QueryParameters.AD_Client_ID

        FROM QueryParameters QueryParameters
    )

    AND CURRENT_DATE >=
        Period.StartDate

    AND CURRENT_DATE <
        Period.EndDate + 1
),
CurrentPeriod AS
(
    SELECT
        CurrentPeriodSource.C_Calendar_ID,
        CurrentPeriodSource.C_Period_ID,
        CurrentPeriodSource.StartDate,
        CurrentPeriodSource.EndDate

    FROM CurrentPeriodSource CurrentPeriodSource

    WHERE CurrentPeriodSource.PeriodRowNumber = 1
),
PreviousPeriodSource AS
(
    SELECT
        Period.C_Period_ID,
        Period.StartDate,
        Period.EndDate,

        ROW_NUMBER() OVER
        (
            ORDER BY
                Period.EndDate DESC,
                Period.C_Period_ID DESC
        ) AS PeriodRowNumber

    FROM CurrentPeriod CurrentPeriod

    INNER JOIN C_Year YearData ON
    (
        YearData.C_Calendar_ID =
        CurrentPeriod.C_Calendar_ID
    )

    INNER JOIN C_Period Period ON
    (
        Period.C_Year_ID =
        YearData.C_Year_ID
    )

    WHERE Period.EndDate <
        CurrentPeriod.StartDate
),
PeriodRange AS
(
    SELECT
        PreviousPeriodSource.StartDate
            AS DateFrom,

        PreviousPeriodSource.EndDate
            AS DateTo

    FROM PreviousPeriodSource PreviousPeriodSource

    WHERE PreviousPeriodSource.PeriodRowNumber = 1
),
PaymentFiltered AS
(
" + paymentAccessSql + @"
)
SELECT
    COUNT
    (
        PaymentFiltered.C_Payment_ID
    ) AS TotalPayments,

    COALESCE
    (
        SUM
        (
            CASE
                WHEN PaymentFiltered.IsReconciled = 'Y'
                THEN 1
                ELSE 0
            END
        ),
        0
    ) AS ClearedPayments,

    MIN
    (
        PeriodRange.DateFrom
    ) AS DateFrom,

    MAX
    (
        PeriodRange.DateTo
    ) AS DateTo

FROM PaymentFiltered PaymentFiltered

INNER JOIN PeriodRange PeriodRange ON
(
    PaymentFiltered.DateTrx >=
        PeriodRange.DateFrom

    AND PaymentFiltered.DateTrx <
        PeriodRange.DateTo + 1
)";

            SqlParameter[] parameters =
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

        private string BuildUnreconciledPaymentAccessSql(
            Ctx ctx
        )
        {
            string textType =
                DB.IsOracle()
                    ? "VARCHAR2(4000)"
                    : "VARCHAR(4000)";

            string paymentRoleSql = @"
SELECT
    Payment.C_Payment_ID,
    Payment.DateTrx,

    CAST
    (
        Payment.DocumentNo
        AS " + textType + @"
    ) AS DocumentNo,

    Payment.C_BPartner_ID,
    Payment.C_BankAccount_ID,
    Payment.C_Currency_ID,
    Payment.VA009_PaymentMethod_ID,
    Payment.PayAmt

FROM C_Payment Payment

WHERE CAST
(
    Payment.IsActive
    AS " + textType + @"
) = 'Y'

AND CAST
(
    Payment.IsReceipt
    AS " + textType + @"
) = 'N'

AND CAST
(
    Payment.DocStatus
    AS " + textType + @"
) IN
(
    'CO',
    'CL'
)

AND
(
    Payment.IsReconciled IS NULL

    OR CAST
    (
        Payment.IsReconciled
        AS " + textType + @"
    ) = 'N'
)

AND Payment.AD_Client_ID =
(
    SELECT
        QueryParameters.AD_Client_ID

    FROM QueryParameters QueryParameters
)

AND EXISTS
(
    SELECT
        1

    FROM AD_ClientInfo ClientInfo

    INNER JOIN C_Year CurrentYear ON
    (
        CurrentYear.C_Calendar_ID =
        ClientInfo.C_Calendar_ID
    )

    INNER JOIN C_Period CurrentPeriod ON
    (
        CurrentPeriod.C_Year_ID =
        CurrentYear.C_Year_ID
    )

    INNER JOIN C_Year PreviousYear ON
    (
        PreviousYear.C_Calendar_ID =
        CurrentYear.C_Calendar_ID
    )

    INNER JOIN C_Period PreviousPeriod ON
    (
        PreviousPeriod.C_Year_ID =
        PreviousYear.C_Year_ID
    )

    WHERE ClientInfo.IsActive = 'Y'

    AND ClientInfo.AD_Client_ID =
    (
        SELECT
            QueryParameters.AD_Client_ID

        FROM QueryParameters QueryParameters
    )

    AND CURRENT_DATE >=
        CurrentPeriod.StartDate

    AND CURRENT_DATE <
        CurrentPeriod.EndDate + 1

    AND PreviousPeriod.EndDate =
    (
        SELECT
            MAX
            (
                PeriodData.EndDate
            )

        FROM C_Period PeriodData

        INNER JOIN C_Year YearData ON
        (
            PeriodData.C_Year_ID =
            YearData.C_Year_ID
        )

        WHERE YearData.C_Calendar_ID =
            CurrentYear.C_Calendar_ID

        AND PeriodData.EndDate <
            CurrentPeriod.StartDate
    )

    AND Payment.DateTrx >=
        PreviousPeriod.StartDate

    AND Payment.DateTrx <
        PreviousPeriod.EndDate + 1
)";

            paymentRoleSql =
                MRole.GetDefault(ctx)
                    .AddAccessSQL(
                        paymentRoleSql,
                        "Payment",
                        MRole.SQL_FULLYQUALIFIED,
                        MRole.SQL_RO
                    );

            return paymentRoleSql;
        }

        private string BuildPaymentsDataSql(
            string paymentAccessSql
        )
        {
            string textType =
                DB.IsOracle()
                    ? "VARCHAR2(4000)"
                    : "VARCHAR(4000)";

            return @"
SELECT
    PaymentAccess.C_Payment_ID
        AS Payment_ID,

    PaymentAccess.DateTrx
        AS Trx_Date,

    CAST
    (
        PaymentAccess.DocumentNo
        AS " + textType + @"
    ) AS Document_No,

    CAST
    (
        BPartner.Name
        AS " + textType + @"
    ) AS Vendor_Name,

    CASE
        WHEN Bank.Name IS NOT NULL
        THEN
            CAST
            (
                Bank.Name
                AS " + textType + @"
            )

        ELSE
            CAST
            (
                BankAccount.Name
                AS " + textType + @"
            )
    END AS Bank_Name,

    CAST
    (
        BankAccount.AccountNo
        AS " + textType + @"
    ) AS Account_No,

    PaymentAccess.PayAmt
        AS Pay_Amount,

    PaymentAccess.C_Currency_ID,

    CAST
    (
        Currency.ISO_Code
        AS " + textType + @"
    ) AS Payment_Currency,

    CASE
        WHEN Currency.CurSymbol IS NOT NULL
        THEN
            CAST
            (
                Currency.CurSymbol
                AS " + textType + @"
            )

        ELSE
            CAST
            (
                Currency.ISO_Code
                AS " + textType + @"
            )
    END AS Payment_Currency_Symbol,

    Currency.StdPrecision
        AS Std_Precision,

    CAST
    (
        PaymentMethod.VA009_Name
        AS " + textType + @"
    ) AS Payment_Method,

    CASE
        WHEN PaymentAccess.C_BankAccount_ID > 0

        AND PaymentAccess.DocumentNo IS NOT NULL

        THEN 1

        ELSE 0
    END AS Auto_Match_Candidate

FROM
(
" + paymentAccessSql + @"
) PaymentAccess

INNER JOIN C_BPartner BPartner ON
(
    BPartner.C_BPartner_ID =
    PaymentAccess.C_BPartner_ID
)

LEFT OUTER JOIN C_BankAccount BankAccount ON
(
    BankAccount.C_BankAccount_ID =
    PaymentAccess.C_BankAccount_ID
)

LEFT OUTER JOIN C_Bank Bank ON
(
    Bank.C_Bank_ID =
    BankAccount.C_Bank_ID
)

INNER JOIN C_Currency Currency ON
(
    Currency.C_Currency_ID =
    PaymentAccess.C_Currency_ID
)

LEFT OUTER JOIN VA009_PaymentMethod PaymentMethod ON
(
    PaymentMethod.VA009_PaymentMethod_ID =
    PaymentAccess.VA009_PaymentMethod_ID
)";
        }

        private string GetUnreconciledReason(
            DateTime currentDate,
            DateTime? trxDate,
            string documentNo,
            string bankName,
            string accountNo
        )
        {
            if (
                string.IsNullOrWhiteSpace(bankName) ||
                string.IsNullOrWhiteSpace(accountNo)
            )
            {
                return "Missing bank account";
            }

            if (
                string.IsNullOrWhiteSpace(
                    documentNo
                )
            )
            {
                return "Reference mismatch";
            }

            if (
                trxDate.HasValue &&
                (
                    currentDate.Date -
                    trxDate.Value.Date
                ).TotalDays <= 3
            )
            {
                return "Awaiting bank statement";
            }

            return "Statement loaded - partial match";
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

            string sessionExpired =
                GetMsg(
                    ctx,
                    "SessionExpired",
                    "Session Expired"
                );

            return Json(
                new
                {
                    error =
                        sessionExpired,

                    errorText =
                        sessionExpired
                },
                JsonRequestBehavior.AllowGet
            );
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
            string message =
                Msg.GetMsg(
                    ctx,
                    key
                );

            return
                !string.IsNullOrWhiteSpace(
                    message
                ) &&
                message != "[" + key + "]"
                    ? message
                    : fallback;
        }

        private void CloseReader(
            IDataReader dr
        )
        {
            if (dr == null)
            {
                return;
            }

            dr.Close();
            dr.Dispose();
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

