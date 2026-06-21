using System;

/*
 * Paid This Month AP Payment Widget Controller
 *
 * Labels / Message Keys
 * #  | Current Text                         | Message Key
 * ---+--------------------------------------+--------------------------------
 * 1  | Paid this month                      | VAS_028_MessagePaidThisMonth
 * 2  | Cash paid                            | VAS_028_MessageCashPaid
 * 3  | Could not load data                  | VAS_ErrorLoading
 * 4  | Session Expired                      | SessionExpired
 * 5  | Paid to                              | VAS_028_MessagePaidTo
 * 6  | so far this month.                   | VAS_028_MessageSoFarThisMonth
 * 7  | No payments this month.              | VAS_028_MessageNoPaymentsThisMonth
 * 8  | vendor                               | VAS_028_MessageVendor
 * 9  | vendors                              | VAS_028_MessageVendors
 * 10 | payment                              | VAS_028_MessagePayment
 * 11 | payments                             | VAS_028_MessagePayments
 * 12 | Not Specified                        | VAS_028_MessageNotSpecified
 * 13 | Cleared                              | VAS_028_MessageCleared
 * 14 | Bounced                              | VAS_028_MessageBounced
 * 15 | In transit                           | VAS_028_MessageInTransit
 */

using System.Collections.Generic;
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
    public class VAS_028_PaidThisMonthAPPaymentWidgetController : Controller
    {
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetPaidThisMonth()
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
                    BuildPaidThisMonthSql(ctx);

                dr = DB.ExecuteReader(
                    queryData.Sql,
                    queryData.Parameters,
                    null
                );

                decimal paidThisMonth = 0;

                int vendorCount = 0;
                int paymentCount = 0;
                int cCurrencyId = 0;
                int precision = 2;

                string currencyISO = string.Empty;
                string currencySymbol = string.Empty;

                DateTime? dateFrom = null;
                DateTime? dateTo = null;

                if (dr != null && dr.Read())
                {
                    paidThisMonth = GetDecimal(
                        dr,
                        "PaidThisMonth",
                        0
                    );

                    vendorCount = GetInt(
                        dr,
                        "VendorCount"
                    );

                    paymentCount = GetInt(
                        dr,
                        "PaymentCount"
                    );

                    cCurrencyId = GetInt(
                        dr,
                        "C_Currency_ID"
                    );

                    precision = GetInt(
                        dr,
                        "StdPrecision",
                        2
                    );

                    currencyISO = GetString(
                        dr,
                        "CurrencyISO",
                        string.Empty
                    );

                    currencySymbol = GetString(
                        dr,
                        "CurrencySymbol",
                        string.Empty
                    );

                    if (string.IsNullOrWhiteSpace(currencySymbol))
                    {
                        currencySymbol = currencyISO;
                    }

                    if (dr["DateFrom"] != DBNull.Value)
                    {
                        dateFrom = Util.GetValueOfDateTime(
                            dr["DateFrom"]
                        );
                    }

                    if (dr["DateTo"] != DBNull.Value)
                    {
                        dateTo = Util.GetValueOfDateTime(
                            dr["DateTo"]
                        );
                    }
                }

                if (precision < 0)
                {
                    precision = 2;
                }

                paidThisMonth = decimal.Round(
                    paidThisMonth,
                    precision,
                    MidpointRounding.AwayFromZero
                );

                return Json(
                    new
                    {
                        title = GetMsg(
                            ctx,
                            "VAS_028_MessagePaidThisMonth",
                            "Paid This Month"
                        ),

                        subtitle = GetPaidThisMonthSubtitle(
                            ctx,
                            dateFrom,
                            paymentCount,
                            paidThisMonth,
                            currencySymbol,
                            currencyISO,
                            precision
                        ),

                        description = GetMsg(
                            ctx,
                            "VAS_028_MessageOutgoingPaymentsPostedSoFar",
                            "Outgoing Payments Posted So Far"
                        ),

                        value = paidThisMonth,
                        paidThisMonth = paidThisMonth,
                        totalPaidAmount = paidThisMonth,

                        vendorCount = vendorCount,
                        paymentCount = paymentCount,

                        cCurrencyId = cCurrencyId,
                        currencyISO = currencyISO,
                        currencySymbol = currencySymbol,
                        symbol = currencySymbol,
                        precision = precision,

                        dateFrom = FormatNullableDate(dateFrom),
                        dateTo = FormatNullableDate(dateTo)
                    },
                    JsonRequestBehavior.AllowGet
                );
            }
            catch (Exception)
            {
                string errorMessage = GetMsg(
                    ctx,
                    "VAS_ErrorLoading",
                    "Could Not Load Data"
                );

                return Json(
                    new
                    {
                        error = true,
                        errorText = errorMessage
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
        public JsonResult GetPaidThisMonthRows(
            int pageNo = 1,
            int pageSize = 10
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
                pageSize = 10;
            }

            if (pageSize > 50)
            {
                pageSize = 50;
            }

            IDataReader dr = null;

            try
            {
                SqlQueryData queryData =
                    BuildPaidThisMonthRowsSql(
                        ctx,
                        pageNo,
                        pageSize
                    );

                dr = DB.ExecuteReader(
                    queryData.Sql,
                    queryData.Parameters,
                    null
                );

                List<object> rows = new List<object>();

                int totalRecords = 0;

                DateTime? dateFrom = null;
                DateTime? dateTo = null;

                while (dr != null && dr.Read())
                {
                    totalRecords = GetInt(
                        dr,
                        "TotalRecords"
                    );

                    DateTime? paymentDate = null;

                    if (dr["PaymentDate"] != DBNull.Value)
                    {
                        paymentDate = Util.GetValueOfDateTime(
                            dr["PaymentDate"]
                        );
                    }

                    if (dr["DateFrom"] != DBNull.Value)
                    {
                        dateFrom = Util.GetValueOfDateTime(
                            dr["DateFrom"]
                        );
                    }

                    if (dr["DateTo"] != DBNull.Value)
                    {
                        dateTo = Util.GetValueOfDateTime(
                            dr["DateTo"]
                        );
                    }

                    int rowCurrencyId = GetInt(
                        dr,
                        "C_Currency_ID"
                    );

                    int rowPrecision = GetInt(
                        dr,
                        "StdPrecision",
                        2
                    );

                    if (rowPrecision < 0)
                    {
                        rowPrecision = 2;
                    }

                    string rowCurrencyISO = GetString(
                        dr,
                        "CurrencyISO",
                        string.Empty
                    );

                    string rowCurrencySymbol = GetString(
                        dr,
                        "CurrencySymbol",
                        string.Empty
                    );

                    if (string.IsNullOrWhiteSpace(rowCurrencySymbol))
                    {
                        rowCurrencySymbol = rowCurrencyISO;
                    }

                    string bankName = GetString(
                        dr,
                        "BankName",
                        string.Empty
                    );

                    string bankAccountName = GetString(
                        dr,
                        "BankAccountName",
                        string.Empty
                    );

                    if (string.IsNullOrWhiteSpace(bankName))
                    {
                        bankName = bankAccountName;
                    }

                    string isReconciled = GetString(
                        dr,
                        "IsReconciled",
                        "N"
                    );

                    string executionStatus = GetString(
                        dr,
                        "VA009_ExecutionStatus",
                        string.Empty
                    );

                    string executionStatusName = GetString(
                        dr,
                        "TranslatedExecutionStatusName",
                        string.Empty
                    );

                    if (string.IsNullOrWhiteSpace(executionStatusName))
                    {
                        executionStatusName = GetString(
                            dr,
                            "ExecutionStatusName",
                            string.Empty
                        );
                    }

                    if (string.IsNullOrWhiteSpace(executionStatusName))
                    {
                        executionStatusName = executionStatus;
                    }

                    string statusType = GetStatusType(
                        isReconciled,
                        executionStatus
                    );

                    string statusName = GetStatusMessage(
                        ctx,
                        statusType,
                        executionStatusName
                    );

                    string paymentMethodName = GetPaymentMethodName(
                        ctx,
                        GetString(
                            dr,
                            "PaymentMethodName",
                            string.Empty
                        )
                    );

                    string vendorName = GetString(
                        dr,
                        "VendorName",
                        string.Empty
                    );

                    string formattedDate = paymentDate.HasValue
                        ? FormatDate(paymentDate.Value)
                        : string.Empty;

                    decimal amount = GetDecimal(
                        dr,
                        "Amount",
                        0
                    );

                    amount = Math.Round(
                        amount,
                        rowPrecision,
                        MidpointRounding.AwayFromZero
                    );

                    rows.Add(
                        new
                        {
                            paymentId = GetInt(
                                dr,
                                "C_Payment_ID"
                            ),

                            paymentDate = formattedDate,
                            date = formattedDate,

                            documentNo = GetString(
                                dr,
                                "DocumentNo",
                                string.Empty
                            ),

                            vendorName = vendorName,
                            supplier = vendorName,

                            bankName = bankName,

                            accountNo = GetString(
                                dr,
                                "AccountNo",
                                string.Empty
                            ),

                            amount = amount,

                            cCurrencyId = rowCurrencyId,
                            currencyISO = rowCurrencyISO,
                            currencySymbol = rowCurrencySymbol,
                            stdPrecision = rowPrecision,

                            paymentCurrency = rowCurrencyISO,
                            paymentCurrencySymbol = rowCurrencySymbol,

                            paymentMethodId = GetInt(
                                dr,
                                "VA009_PaymentMethod_ID"
                            ),

                            paymentMethodName = paymentMethodName,

                            isReconciled = string.Equals(
                                isReconciled,
                                "Y",
                                StringComparison.OrdinalIgnoreCase
                            ),

                            executionStatus = executionStatus,

                            executionStatusName =
                                executionStatusName,

                            statusType = statusType,
                            statusValue = executionStatus,
                            statusName = statusName,

                            status = new
                            {
                                value = executionStatus,
                                name = statusName
                            }
                        }
                    );
                }

                int totalPages = totalRecords == 0
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
                        title = GetMsg(
                            ctx,
                            "VAS_028_MessagePaidThisMonth",
                            "Paid This Month"
                        ),

                        subtitle = GetRowsSubtitle(
                            ctx,
                            dateFrom,
                            totalRecords
                        ),

                        rows = rows,

                        pageNo = pageNo,
                        pageSize = pageSize,
                        totalRecords = totalRecords,
                        totalPages = totalPages,
                        paymentCount = totalRecords,

                        dateFrom = FormatNullableDate(dateFrom),
                        dateTo = FormatNullableDate(dateTo)
                    },
                    JsonRequestBehavior.AllowGet
                );
            }
            catch (Exception)
            {
                string errorMessage = GetMsg(
                    ctx,
                    "VAS_ErrorLoading",
                    "Could Not Load Data"
                );

                return Json(
                    new
                    {
                        error = true,
                        errorText = errorMessage
                    },
                    JsonRequestBehavior.AllowGet
                );
            }
            finally
            {
                CloseReader(dr);
            }
        }

        private SqlQueryData BuildPaidThisMonthSql(
            Ctx ctx
        )
        {
            string queryParametersFrom =
                DB.IsOracle()
                    ? " FROM DUAL"
                    : string.Empty;

            bool hasExecutionStatus =
                HasPaymentExecutionStatusColumn();

            string executionStatusFilter =
                hasExecutionStatus
                    ? @"
AND COALESCE
(
    Payment.VA009_ExecutionStatus,
    'R'
) NOT IN
(
    'B',
    'C'
)"
                    : string.Empty;

            string paymentAccessSql = @"
SELECT
    Payment.C_Payment_ID,
    Payment.AD_Client_ID,
    Payment.AD_Org_ID,
    Payment.C_BPartner_ID,
    Payment.C_Currency_ID,
    Payment.C_ConversionType_ID,
    Payment.DateAcct,
    Payment.PayAmt

FROM C_Payment Payment

WHERE Payment.IsActive = 'Y'

AND Payment.IsReceipt = 'N'

AND Payment.DocStatus IN
(
    'CO',
    'CL'
)

AND Payment.AD_Client_ID =
(
    SELECT
        QueryParameters.AD_Client_ID

    FROM QueryParameters QueryParameters
)"
                + executionStatusFilter;

            paymentAccessSql =
                MRole.GetDefault(ctx).AddAccessSQL(
                    paymentAccessSql,
                    "Payment",
                    MRole.SQL_FULLYQUALIFIED,
                    MRole.SQL_RO
                );

            string amountSumExpression = @"
COALESCE
(
    SUM
    (
        PaidThisMonthData.PaidAmount
    ),
    0
)";

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
    (
        SELECT
            QueryParameters.AD_Client_ID

        FROM QueryParameters QueryParameters
    )
),
CurrentPeriod AS
(
    SELECT
        ClientInfo.AD_Client_ID,
        MIN
        (
            Period.StartDate
        ) AS DateFrom,

        MAX
        (
            Period.EndDate
        ) AS DateTo

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

    AND CURRENT_DATE >=
        Period.StartDate

    AND CURRENT_DATE <
        Period.EndDate + 1

    GROUP BY
        ClientInfo.AD_Client_ID
),
PaymentSecured AS
(
" + paymentAccessSql + @"
),
PaymentFiltered AS
(
    SELECT
        Payment.C_Payment_ID,
        Payment.AD_Client_ID,
        Payment.AD_Org_ID,
        Payment.C_BPartner_ID,
        Payment.C_Currency_ID,
        Payment.C_ConversionType_ID,
        Payment.DateAcct,
        Payment.PayAmt

    FROM PaymentSecured Payment

    INNER JOIN CurrentPeriod CurrentPeriod ON
    (
        CurrentPeriod.AD_Client_ID =
        Payment.AD_Client_ID
    )

    WHERE Payment.DateAcct >=
        CurrentPeriod.DateFrom

    AND Payment.DateAcct <
        CurrentPeriod.DateTo + 1
),
PaidThisMonthData AS
(
    SELECT
        Payment.C_Payment_ID,
        Payment.AD_Client_ID,
        Payment.C_BPartner_ID,

        CASE
            WHEN Payment.C_Currency_ID =
                 SchemaCurrency.C_Currency_ID

            THEN COALESCE
            (
                Payment.PayAmt,
                0
            )

            ELSE CurrencyConvert
            (
                COALESCE
                (
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
        END AS PaidAmount

    FROM PaymentFiltered Payment

    INNER JOIN SchemaCurrency SchemaCurrency ON
    (
        SchemaCurrency.AD_Client_ID =
        Payment.AD_Client_ID
    )
)
SELECT
    ROUND
    (
        " + CastNumberSql(amountSumExpression) + @",

        CAST
        (
            COALESCE
            (
                MAX
                (
                    SchemaCurrency.StdPrecision
                ),
                2
            ) AS INTEGER
        )
    ) AS PaidThisMonth,

    COUNT
    (
        DISTINCT PaidThisMonthData.C_BPartner_ID
    ) AS VendorCount,

    COUNT
    (
        PaidThisMonthData.C_Payment_ID
    ) AS PaymentCount,

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
    ) AS CurrencySymbol,

    MAX
    (
        SchemaCurrency.StdPrecision
    ) AS StdPrecision,

    MAX
    (
        CurrentPeriod.DateFrom
    ) AS DateFrom,

    MAX
    (
        CurrentPeriod.DateTo
    ) AS DateTo

FROM SchemaCurrency SchemaCurrency

LEFT OUTER JOIN CurrentPeriod CurrentPeriod ON
(
    CurrentPeriod.AD_Client_ID =
    SchemaCurrency.AD_Client_ID
)

LEFT OUTER JOIN PaidThisMonthData PaidThisMonthData ON
(
    PaidThisMonthData.AD_Client_ID =
    SchemaCurrency.AD_Client_ID
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
                Sql = sql,
                Parameters = parameters
            };
        }

        private SqlQueryData BuildPaidThisMonthRowsSql(
            Ctx ctx,
            int pageNo,
            int pageSize
        )
        {
            string queryParametersFrom =
                DB.IsOracle()
                    ? " FROM DUAL"
                    : string.Empty;

            int startRow =
                ((pageNo - 1) * pageSize) + 1;

            int endRow =
                pageNo * pageSize;

            bool hasExecutionStatus =
                HasPaymentExecutionStatusColumn();

            string executionStatusFilter =
                hasExecutionStatus
                    ? @"
AND COALESCE
(
    Payment.VA009_ExecutionStatus,
    'R'
) NOT IN
(
    'B',
    'C'
)"
                    : string.Empty;

            string executionStatusColumn =
                hasExecutionStatus
                    ? "Payment.VA009_ExecutionStatus"
                    : "NULL AS VA009_ExecutionStatus";

            string paymentAccessSql = @"
SELECT
    Payment.C_Payment_ID,
    Payment.AD_Client_ID,
    Payment.AD_Org_ID,
    Payment.C_BPartner_ID,
    Payment.C_BankAccount_ID,
    Payment.C_Currency_ID,
    Payment.VA009_PaymentMethod_ID,
    Payment.DateAcct,
    Payment.DocumentNo,
    Payment.DocStatus,
    Payment.IsReconciled,
    Payment.PayAmt,
    " + executionStatusColumn + @"

FROM C_Payment Payment

WHERE Payment.IsActive = 'Y'

AND Payment.IsReceipt = 'N'

AND Payment.DocStatus IN
(
    'CO',
    'CL'
)

AND Payment.AD_Client_ID =
(
    SELECT
        QueryParameters.AD_Client_ID

    FROM QueryParameters QueryParameters
)"
                + executionStatusFilter;

            paymentAccessSql =
                MRole.GetDefault(ctx).AddAccessSQL(
                    paymentAccessSql,
                    "Payment",
                    MRole.SQL_FULLYQUALIFIED,
                    MRole.SQL_RO
                );

            string statusListCte = string.Empty;
            string statusColumns;
            string statusJoin;

            if (hasExecutionStatus)
            {
                statusListCte = @",
StatusListSource AS
(
    SELECT
        " + GetTextCastSql("RefList.Value") + @" AS StatusValue,
        " + GetTextCastSql("RefList.Name") + @" AS ExecutionStatusName,
        " + GetTextCastSql("RefListTrl.Name") + @" AS TranslatedExecutionStatusName,

        ROW_NUMBER() OVER
        (
            PARTITION BY
                " + GetTextCastSql("RefList.Value") + @"

            ORDER BY
                CASE
                    WHEN RefListTrl.Name IS NOT NULL
                    THEN 0
                    ELSE 1
                END,
                RefList.AD_Ref_List_ID
        ) AS StatusRowNumber

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
    )

    WHERE TableInfo.TableName = 'C_Payment'

    AND ColumnInfo.ColumnName = 'VA009_ExecutionStatus'

    AND TableInfo.IsActive = 'Y'

    AND ColumnInfo.IsActive = 'Y'

    AND ReferenceInfo.IsActive = 'Y'

    AND RefList.IsActive = 'Y'
),
StatusList AS
(
    SELECT
        StatusListSource.StatusValue,
        StatusListSource.ExecutionStatusName,
        StatusListSource.TranslatedExecutionStatusName

    FROM StatusListSource StatusListSource

    WHERE StatusListSource.StatusRowNumber = 1
)";

                statusColumns = @"
StatusList.StatusValue,
StatusList.ExecutionStatusName,
StatusList.TranslatedExecutionStatusName,";

                statusJoin = @"
LEFT OUTER JOIN StatusList StatusList ON
(
    StatusList.StatusValue =
    " + GetTextCastSql("Payment.VA009_ExecutionStatus") + @"
)";
            }
            else
            {
                statusColumns = @"
NULL AS StatusValue,
NULL AS ExecutionStatusName,
NULL AS TranslatedExecutionStatusName,";

                statusJoin = string.Empty;
            }

            string amountExpression =
                CastNumberSql(
                    "COALESCE(Payment.PayAmt, 0)"
                );

            string sql = @"
WITH QueryParameters AS
(
    SELECT
        @AD_Client_ID AS AD_Client_ID,
        @AD_Language AS AD_Language,
        @StartRow AS StartRow,
        @EndRow AS EndRow"
        + queryParametersFrom + @"
),
CurrentPeriod AS
(
    SELECT
        ClientInfo.AD_Client_ID,

        MIN
        (
            Period.StartDate
        ) AS DateFrom,

        MAX
        (
            Period.EndDate
        ) AS DateTo

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

    AND CURRENT_DATE >=
        Period.StartDate

    AND CURRENT_DATE <
        Period.EndDate + 1

    GROUP BY
        ClientInfo.AD_Client_ID
),
PaymentSecured AS
(
" + paymentAccessSql + @"
),
PaymentFiltered AS
(
    SELECT
        Payment.C_Payment_ID,
        Payment.AD_Client_ID,
        Payment.AD_Org_ID,
        Payment.C_BPartner_ID,
        Payment.C_BankAccount_ID,
        Payment.C_Currency_ID,
        Payment.VA009_PaymentMethod_ID,
        Payment.DateAcct,
        Payment.DocumentNo,
        Payment.DocStatus,
        Payment.IsReconciled,
        Payment.PayAmt,
        Payment.VA009_ExecutionStatus,
        CurrentPeriod.DateFrom,
        CurrentPeriod.DateTo

    FROM PaymentSecured Payment

    INNER JOIN CurrentPeriod CurrentPeriod ON
    (
        CurrentPeriod.AD_Client_ID =
        Payment.AD_Client_ID
    )

    WHERE Payment.DateAcct >=
        CurrentPeriod.DateFrom

    AND Payment.DateAcct <
        CurrentPeriod.DateTo + 1
)"
        + statusListCte + @",
PaidRows AS
(
    SELECT
        Payment.C_Payment_ID,
        Payment.DateAcct AS PaymentDate,
        Payment.DocumentNo,
        BPartner.Name AS VendorName,
        Bank.Name AS BankName,
        BankAccount.Name AS BankAccountName,
        BankAccount.AccountNo,
        Payment.VA009_PaymentMethod_ID,
        PaymentMethod.VA009_Name AS PaymentMethodName,
        Payment.DocStatus,
        Payment.IsReconciled,
        Payment.VA009_ExecutionStatus,
        " + statusColumns + @"

        ROUND
        (
            " + amountExpression + @",

            CAST
            (
                COALESCE
                (
                    PaymentCurrency.StdPrecision,
                    2
                ) AS INTEGER
            )
        ) AS Amount,

        Payment.C_Currency_ID,
        PaymentCurrency.StdPrecision,
        PaymentCurrency.ISO_Code AS CurrencyISO,

        CASE
            WHEN PaymentCurrency.CurSymbol IS NOT NULL
            THEN PaymentCurrency.CurSymbol
            ELSE PaymentCurrency.ISO_Code
        END AS CurrencySymbol,

        Payment.DateFrom,
        Payment.DateTo

    FROM PaymentFiltered Payment

    INNER JOIN C_Currency PaymentCurrency ON
    (
        PaymentCurrency.C_Currency_ID =
        Payment.C_Currency_ID
    )

    LEFT OUTER JOIN C_BPartner BPartner ON
    (
        BPartner.C_BPartner_ID =
        Payment.C_BPartner_ID
    )

    LEFT OUTER JOIN C_BankAccount BankAccount ON
    (
        BankAccount.C_BankAccount_ID =
        Payment.C_BankAccount_ID
    )

    LEFT OUTER JOIN C_Bank Bank ON
    (
        Bank.C_Bank_ID =
        BankAccount.C_Bank_ID
    )

    LEFT OUTER JOIN VA009_PaymentMethod PaymentMethod ON
    (
        PaymentMethod.VA009_PaymentMethod_ID =
        Payment.VA009_PaymentMethod_ID
    )
    " + statusJoin + @"
),
NumberedRows AS
(
    SELECT
        PaidRows.*,

        COUNT(1) OVER () AS TotalRecords,

        ROW_NUMBER() OVER
        (
            ORDER BY
                PaidRows.PaymentDate DESC,
                PaidRows.C_Payment_ID DESC
        ) AS RowNumber

    FROM PaidRows PaidRows
)
SELECT
    NumberedRows.C_Payment_ID,
    NumberedRows.PaymentDate,
    NumberedRows.DocumentNo,
    NumberedRows.VendorName,
    NumberedRows.BankName,
    NumberedRows.BankAccountName,
    NumberedRows.AccountNo,
    NumberedRows.VA009_PaymentMethod_ID,
    NumberedRows.PaymentMethodName,
    NumberedRows.DocStatus,
    NumberedRows.IsReconciled,
    NumberedRows.VA009_ExecutionStatus,
    NumberedRows.StatusValue,
    NumberedRows.ExecutionStatusName,
    NumberedRows.TranslatedExecutionStatusName,
    NumberedRows.Amount,
    NumberedRows.C_Currency_ID,
    NumberedRows.StdPrecision,
    NumberedRows.CurrencyISO,
    NumberedRows.CurrencySymbol,
    NumberedRows.DateFrom,
    NumberedRows.DateTo,
    NumberedRows.TotalRecords

FROM NumberedRows NumberedRows

WHERE NumberedRows.RowNumber >=
(
    SELECT
        QueryParameters.StartRow

    FROM QueryParameters QueryParameters
)

AND NumberedRows.RowNumber <=
(
    SELECT
        QueryParameters.EndRow

    FROM QueryParameters QueryParameters
)

ORDER BY
    NumberedRows.RowNumber";

            SqlParameter[] parameters =
            {
                new SqlParameter(
                    "@AD_Client_ID",
                    ctx.GetAD_Client_ID()
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

        private string CastNumberSql(
            string expression
        )
        {
            if (DB.IsOracle())
            {
                return "CAST("
                    + expression
                    + " AS NUMBER)";
            }

            return "CAST("
                + expression
                + " AS NUMERIC)";
        }

        private string GetTextCastSql(
            string expression
        )
        {
            if (DB.IsOracle())
            {
                return "CAST("
                    + expression
                    + " AS VARCHAR2(4000))";
            }

            return "CAST("
                + expression
                + " AS VARCHAR(4000))";
        }

        private bool HasPaymentExecutionStatusColumn()
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

WHERE TableData.TableName = 'C_Payment'

AND ColumnData.ColumnName =
    'VA009_ExecutionStatus'

AND TableData.IsActive = 'Y'

AND ColumnData.IsActive = 'Y'";

            return Util.GetValueOfInt(
                DB.ExecuteScalar(sql)
            ) > 0;
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

            string sessionExpired = GetMsg(
                ctx,
                "SessionExpired",
                "Session Expired"
            );

            return Json(
                new
                {
                    error = true,
                    errorText = sessionExpired
                },
                JsonRequestBehavior.AllowGet
            );
        }

        private string GetPaidThisMonthSubtitle(
            Ctx ctx,
            DateTime? dateFrom,
            int paymentCount,
            decimal totalPaid,
            string currencySymbol,
            string currencyISO,
            int precision
        )
        {
            string periodText = dateFrom.HasValue
                ? dateFrom.Value.ToString(
                    "MMM yyyy",
                    CultureInfo.InvariantCulture
                )
                : GetMsg(
                    ctx,
                    "VAS_028_MessageThisMonth",
                    "This Month"
                );

            string paymentLabel = paymentCount == 1
                ? GetMsg(
                    ctx,
                    "VAS_028_MessagePayment",
                    "Payment"
                )
                : GetMsg(
                    ctx,
                    "VAS_028_MessagePayments",
                    "Payments"
                );

            return periodText
                + " · "
                + paymentCount
                + " "
                + paymentLabel
                + " · MTD "
                + FormatDisplayAmount(
                    totalPaid,
                    currencySymbol,
                    currencyISO,
                    precision
                );
        }

        private string GetRowsSubtitle(
            Ctx ctx,
            DateTime? dateFrom,
            int paymentCount
        )
        {
            string periodText = dateFrom.HasValue
                ? dateFrom.Value.ToString(
                    "MMM yyyy",
                    CultureInfo.InvariantCulture
                )
                : GetMsg(
                    ctx,
                    "VAS_028_MessageThisMonth",
                    "This Month"
                );

            string paymentLabel = paymentCount == 1
                ? GetMsg(
                    ctx,
                    "VAS_028_MessagePayment",
                    "Payment"
                )
                : GetMsg(
                    ctx,
                    "VAS_028_MessagePayments",
                    "Payments"
                );

            return periodText
                + " · "
                + paymentCount
                + " "
                + paymentLabel;
        }

        private string FormatDisplayAmount(
            decimal value,
            string currencySymbol,
            string currencyISO,
            int precision
        )
        {
            return value.ToString(
                "N" + precision,
                CultureInfo.InvariantCulture
            );
        }

        private string GetStatusType(
            string isReconciled,
            string executionStatus
        )
        {
            if (string.Equals(
                isReconciled,
                "Y",
                StringComparison.OrdinalIgnoreCase
            ))
            {
                return "cleared";
            }

            if (
                string.Equals(
                    executionStatus,
                    "B",
                    StringComparison.OrdinalIgnoreCase
                ) ||
                string.Equals(
                    executionStatus,
                    "C",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return "bounced";
            }

            return "intransit";
        }

        private string GetStatusMessage(
            Ctx ctx,
            string statusType,
            string executionStatusName
        )
        {
            if (statusType == "cleared")
            {
                return GetMsg(
                    ctx,
                    "VAS_028_MessageCleared",
                    "Cleared"
                );
            }

            if (statusType == "bounced")
            {
                return !string.IsNullOrWhiteSpace(
                    executionStatusName
                )
                    ? executionStatusName
                    : GetMsg(
                        ctx,
                        "VAS_028_MessageBounced",
                        "Bounced"
                    );
            }

            return GetMsg(
                ctx,
                "VAS_028_MessageInTransit",
                "In Transit"
            );
        }

        private string GetPaymentMethodName(
            Ctx ctx,
            string paymentMethodName
        )
        {
            if (string.IsNullOrWhiteSpace(paymentMethodName))
            {
                return GetMsg(
                    ctx,
                    "VAS_028_MessageNotSpecified",
                    "Not Specified"
                );
            }

            return paymentMethodName;
        }

        private int GetInt(
            IDataReader reader,
            string columnName,
            int fallback = 0
        )
        {
            object value = reader[columnName];

            return value == null ||
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

            return value == null ||
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

            return value == null ||
                   value == DBNull.Value
                ? fallback
                : Util.GetValueOfString(value);
        }

        private string GetMsg(
            Ctx ctx,
            string key,
            string fallback
        )
        {
            string message = Msg.GetMsg(
                ctx,
                key
            );

            return
                !string.IsNullOrEmpty(message) &&
                message != key &&
                message != "[" + key + "]"
                    ? message
                    : fallback;
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

        private string FormatNullableDate(
            DateTime? date
        )
        {
            return date.HasValue
                ? FormatDate(date.Value)
                : string.Empty;
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