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
            string sql = string.Empty;

            try
            {
                sql = BuildPaidThisMonthSql(ctx);

                dr = DB.ExecuteReader(
                    sql,
                    null,
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
                            "Paid this month"
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
                            "Outgoing payments posted so far"
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
            catch (Exception ex)
            {
                return Json(
                    new
                    {
                        error = true,
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

            if (pageSize > 100)
            {
                pageSize = 100;
            }

            IDataReader dr = null;
            string sql = string.Empty;

            try
            {
                sql = BuildPaidThisMonthRowsSql(
                    ctx,
                    pageNo,
                    pageSize
                );

                dr = DB.ExecuteReader(
                    sql,
                    null,
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

                    string statusType = GetStatusType(
                        isReconciled,
                        executionStatus
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

                            isReconciled = isReconciled == "Y",
                            executionStatus = executionStatus,

                            statusType = statusType,

                            statusName = GetStatusMessage(
                                ctx,
                                statusType,
                                executionStatusName
                            )
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
                            "Paid this month"
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
            catch (Exception ex)
            {
                return Json(
                    new
                    {
                        error = true,
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

        private string BuildPaidThisMonthSql(
            Ctx ctx
        )
        {
            DateTime monthStart = new DateTime(
                DateTime.Today.Year,
                DateTime.Today.Month,
                1
            );

            DateTime nextMonthStart =
                monthStart.AddMonths(1);

            string clientId = ctx
                .GetAD_Client_ID()
                .ToString(CultureInfo.InvariantCulture);

            string executionStatusFilter =
                HasPaymentExecutionStatusColumn()
                    ? @"
AND COALESCE(
    Payment.VA009_ExecutionStatus,
    'R'
) NOT IN ('B', 'C')"
                    : string.Empty;

            string schemaCurrencySql = @"
SchemaCurrency AS
(
    SELECT
        ClientInfo.AD_Client_ID,
        AcctSchema.C_Currency_ID,
        Currency.StdPrecision,
        Currency.ISO_Code,
        Currency.CurSymbol AS Cur_Symbol

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
    AND ClientInfo.AD_Client_ID = " + clientId + @"
)";

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
AND Payment.DocStatus IN ('CO', 'CL')
AND Payment.AD_Client_ID = " + clientId + @"

AND Payment.DateAcct >= " +
                ToSqlDate(monthStart) + @"

AND Payment.DateAcct < " +
                ToSqlDate(nextMonthStart) +
                executionStatusFilter;

            paymentAccessSql =
                MRole.GetDefault(ctx).AddAccessSQL(
                    paymentAccessSql,
                    "Payment",
                    MRole.SQL_FULLYQUALIFIED,
                    MRole.SQL_RO
                );

            string sql = @"
WITH " + schemaCurrencySql + @",

PaymentFiltered AS
(
" + paymentAccessSql + @"
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
            THEN COALESCE(Payment.PayAmt, 0)

            ELSE CurrencyConvert
            (
                COALESCE(Payment.PayAmt, 0),
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
        COALESCE
        (
            SUM(PaidThisMonthData.PaidAmount),
            0
        ),
        CAST
        (
            COALESCE(
                MAX(SchemaCurrency.StdPrecision),
                2
            ) AS INTEGER
        )
    ) AS PaidThisMonth,

    COUNT
    (
        DISTINCT
        PaidThisMonthData.C_BPartner_ID
    ) AS VendorCount,

    COUNT
    (
        PaidThisMonthData.C_Payment_ID
    ) AS PaymentCount,

    MAX(
        SchemaCurrency.C_Currency_ID
    ) AS C_Currency_ID,

    MAX(
        SchemaCurrency.ISO_Code
    ) AS CurrencyISO,

    MAX(
        SchemaCurrency.Cur_Symbol
    ) AS CurrencySymbol,

    MAX(
        SchemaCurrency.StdPrecision
    ) AS StdPrecision,

    " + ToSqlDate(monthStart) + @" AS DateFrom,

    " + ToSqlDate(
        nextMonthStart.AddDays(-1)
    ) + @" AS DateTo

FROM SchemaCurrency SchemaCurrency

LEFT OUTER JOIN PaidThisMonthData PaidThisMonthData ON
(
    PaidThisMonthData.AD_Client_ID =
    SchemaCurrency.AD_Client_ID
)";

            return sql;
        }

        private string BuildPaidThisMonthRowsSql(
            Ctx ctx,
            int pageNo,
            int pageSize
        )
        {
            DateTime monthStart = new DateTime(
                DateTime.Today.Year,
                DateTime.Today.Month,
                1
            );

            DateTime nextMonthStart =
                monthStart.AddMonths(1);

            int startRow =
                ((pageNo - 1) * pageSize) + 1;

            int endRow =
                pageNo * pageSize;

            string clientId = ctx
                .GetAD_Client_ID()
                .ToString(CultureInfo.InvariantCulture);

            string language = ToSqlString(
                ctx.GetAD_Language()
            );

            bool hasExecutionStatus =
                HasPaymentExecutionStatusColumn();

            string executionStatusFilter =
                hasExecutionStatus
                    ? @"
AND COALESCE(
    Payment.VA009_ExecutionStatus,
    'R'
) NOT IN ('B', 'C')"
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
AND Payment.DocStatus IN ('CO', 'CL')
AND Payment.AD_Client_ID = " + clientId + @"

AND Payment.DateAcct >= " +
                ToSqlDate(monthStart) + @"

AND Payment.DateAcct < " +
                ToSqlDate(nextMonthStart) +
                executionStatusFilter;

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
StatusList AS
(
    SELECT
        RefList.Value,
        RefList.Name AS ExecutionStatusName,
        RefListTrl.Name AS TranslatedExecutionStatusName

    FROM AD_Ref_List RefList

    INNER JOIN AD_Reference ReferenceInfo ON
    (
        ReferenceInfo.AD_Reference_ID =
        RefList.AD_Reference_ID
    )

    LEFT OUTER JOIN AD_Ref_List_Trl RefListTrl ON
    (
        RefListTrl.AD_Ref_List_ID =
        RefList.AD_Ref_List_ID

        AND RefListTrl.AD_Language =
        " + language + @"
    )

    WHERE ReferenceInfo.Name =
        'VA009_ExecutionStatus'
)";

                statusColumns = @"
StatusList.ExecutionStatusName,
StatusList.TranslatedExecutionStatusName,";

                statusJoin = @"
LEFT OUTER JOIN StatusList StatusList ON
(
    StatusList.Value =
    Payment.VA009_ExecutionStatus
)";
            }
            else
            {
                statusColumns = @"
NULL AS ExecutionStatusName,
NULL AS TranslatedExecutionStatusName,";

                statusJoin = string.Empty;
            }

            string sql = @"
WITH PaymentFiltered AS
(
" + paymentAccessSql + @"
)
" + statusListCte + @",

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
            COALESCE(Payment.PayAmt, 0),
            CAST(
                PaymentCurrency.StdPrecision
                AS INTEGER
            )
        ) AS Amount,

        Payment.C_Currency_ID,
        PaymentCurrency.StdPrecision,
        PaymentCurrency.ISO_Code AS CurrencyISO,
        PaymentCurrency.CurSymbol AS CurrencySymbol,

        " + ToSqlDate(monthStart) + @" AS DateFrom,

        " + ToSqlDate(
            nextMonthStart.AddDays(-1)
        ) + @" AS DateTo

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

WHERE NumberedRows.RowNumber >= " +
                startRow.ToString(
                    CultureInfo.InvariantCulture
                ) + @"

AND NumberedRows.RowNumber <= " +
                endRow.ToString(
                    CultureInfo.InvariantCulture
                ) + @"

ORDER BY
    NumberedRows.RowNumber";

            return sql;
        }

        private string ToSqlDate(
            DateTime date
        )
        {
            return "DATE '"
                + date.Date.ToString(
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture
                )
                + "'";
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
    'VA009_ExecutionStatus'";

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
            return Json(
                new
                {
                    error = true,

                    errorText = Msg.GetMsg(
                        Env.GetCtx(),
                        "SessionExpired"
                    ) ?? "Session Expired"
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
                    "This month"
                );

            string paymentLabel = paymentCount == 1
                ? GetMsg(
                    ctx,
                    "VAS_028_MessagePayment",
                    "payment"
                )
                : GetMsg(
                    ctx,
                    "VAS_028_MessagePayments",
                    "payments"
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
                    "This month"
                );

            string paymentLabel = paymentCount == 1
                ? GetMsg(
                    ctx,
                    "VAS_028_MessagePayment",
                    "payment"
                )
                : GetMsg(
                    ctx,
                    "VAS_028_MessagePayments",
                    "payments"
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
            string currency =
                !string.IsNullOrWhiteSpace(currencySymbol)
                    ? currencySymbol
                    : currencyISO;

            return currency
                + value.ToString(
                    "N" + precision,
                    CultureInfo.InvariantCulture
                );
        }

        private string GetStatusType(
            string isReconciled,
            string executionStatus
        )
        {
            if (isReconciled == "Y")
            {
                return "cleared";
            }

            if (
                executionStatus == "B" ||
                executionStatus == "C"
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
                    "VAS_032_MessageCleared",
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
                        "VAS_032_MessageBounced",
                        "Bounced"
                    );
            }

            return GetMsg(
                ctx,
                "VAS_032_MessageInTransit",
                "In transit"
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
                    "VAS_032_MessageNotSpecified",
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
    }
}