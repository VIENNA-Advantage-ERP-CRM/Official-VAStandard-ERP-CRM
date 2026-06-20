using System;
/*
 * Recent AP Payments Widget Controller
 *
 * Labels / Message Keys
 * #  | Current Text                         | Message Key
 * ---+--------------------------------------+--------------------------------
 * 1  | Recent payments                      | VAS_032_MessageRecentPayments
 * 2  | + New payment                        | VAS_032_MessageNewPayment
 * 3  | Review                               | VAS_032_MessageReview
 * 4  | Bounced                              | VAS_032_MessageBounced
 * 5  | Cleared                              | VAS_032_MessageCleared
 * 6  | In transit                           | VAS_032_MessageInTransit
 * 7  | Not Specified                        | VAS_032_MessageNotSpecified
 * 8  | Session Expired                      | SessionExpired
 */

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
     * 1 | Recent payments | VAS_032_MessageRecentPayments
     * 2 | + New payment   | VAS_032_MessageNewPayment
     * 3 | Review          | VAS_032_MessageReview
     * 4 | Bounced         | VAS_032_MessageBounced
     * 5 | Cleared         | VAS_032_MessageCleared
     * 6 | In transit      | VAS_032_MessageInTransit
     * 7 | Not Specified   | VAS_032_MessageNotSpecified
     */
    public class VAS_032_RecentAPPaymentsWidgetController : Controller
    {
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetRecentAPPayments()
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
                sql = BuildRecentPaymentsSql(ctx);

                dr = DB.ExecuteReader(
                    sql,
                    null,
                    null
                );

                List<object> payments =
                    new List<object>();

                List<string> autoMatchedRefs =
                    new List<string>();

                int autoMatchedCount = 0;

                while (
                    dr != null &&
                    dr.Read() &&
                    payments.Count < 30
                )
                {
                    AddPaymentRow(
                        ctx,
                        dr,
                        payments,
                        autoMatchedRefs,
                        ref autoMatchedCount
                    );
                }

                return Json(
                    new
                    {
                        title = GetMsg(
                            ctx,
                            "VAS_032_MessageRecentPayments",
                            "Recent payments"
                        ),

                        newPaymentText = GetMsg(
                            ctx,
                            "VAS_032_MessageNewPayment",
                            "+ New payment"
                        ),

                        autoMatchedCount =
                            autoMatchedCount,

                        autoMatchedRefs =
                            autoMatchedRefs,

                        payments =
                            payments
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

        private string BuildRecentPaymentsSql(Ctx ctx)
        {
            string clientIdSql =
                ctx.GetAD_Client_ID()
                    .ToString(
                        CultureInfo.InvariantCulture
                    );

            string languageSql =
                ToSqlString(
                    ctx.GetAD_Language()
                );

            bool hasPaymentMethodColumn =
                HasColumn(
                    "C_Payment",
                    "VA009_PaymentMethod_ID"
                );

            bool hasExecutionStatusColumn =
                HasColumn(
                    "C_Payment",
                    "VA009_ExecutionStatus"
                );

            string paymentMethodDisplayColumn =
                hasPaymentMethodColumn
                    ? GetPaymentMethodDisplayColumn("PaymentMethod")
                    : string.Empty;

            bool usePaymentMethodJoin =
                hasPaymentMethodColumn &&
                !string.IsNullOrWhiteSpace(
                    paymentMethodDisplayColumn
                );

            string paymentMethodColumnInAccess =
                hasPaymentMethodColumn
                    ? @",
    Payment.VA009_PaymentMethod_ID"
                    : string.Empty;

            string executionStatusColumnInAccess =
                hasExecutionStatusColumn
                    ? @",
    Payment.VA009_ExecutionStatus"
                    : string.Empty;

            string paymentMethodNameSelect =
                usePaymentMethodJoin
                    ? paymentMethodDisplayColumn
                    : "NULL";

            string paymentMethodJoin =
                usePaymentMethodJoin
                    ? @"
LEFT OUTER JOIN VA009_PaymentMethod PaymentMethod ON
(
    PaymentMethod.VA009_PaymentMethod_ID =
    Payment.VA009_PaymentMethod_ID
)"
                    : string.Empty;

            string paymentMethodGroupBy =
                usePaymentMethodJoin
                    ? @",
    " + paymentMethodDisplayColumn
                    : string.Empty;

            string executionStatusListCte =
                hasExecutionStatusColumn
                    ? @",

ExecutionStatusList AS
(
    SELECT
        RefList.Value AS StatusValue,

        RefList.Name AS BaseStatusName,

        RefListTrl.Name AS TranslatedStatusName

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
            " + languageSql + @"
    )

    WHERE ReferenceInfo.Name =
        'VA009_ExecutionStatus'

    AND ReferenceInfo.IsActive = 'Y'

    AND RefList.IsActive = 'Y'
)"
                    : string.Empty;

            string executionStatusJoin =
                hasExecutionStatusColumn
                    ? @"
LEFT OUTER JOIN ExecutionStatusList ExecutionStatusList ON
(
    ExecutionStatusList.StatusValue =
    Payment.VA009_ExecutionStatus
)"
                    : string.Empty;

            string executionStatusValueSelect =
                hasExecutionStatusColumn
                    ? "Payment.VA009_ExecutionStatus"
                    : "NULL";

            string executionStatusNameSelect =
                hasExecutionStatusColumn
                    ? "ExecutionStatusList.BaseStatusName"
                    : "NULL";

            string translatedExecutionStatusNameSelect =
                hasExecutionStatusColumn
                    ? "ExecutionStatusList.TranslatedStatusName"
                    : "NULL";

            string executionStatusGroupBy =
                hasExecutionStatusColumn
                    ? @",
    Payment.VA009_ExecutionStatus,
    ExecutionStatusList.BaseStatusName,
    ExecutionStatusList.TranslatedStatusName"
                    : string.Empty;

            string schemaCurrencyCte = @"
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

            string paymentAccessSql = @"
SELECT
    Payment.C_Payment_ID,

    Payment.AD_Client_ID,

    Payment.AD_Org_ID,

    Payment.C_BPartner_ID,

    Payment.C_BankAccount_ID,

    Payment.C_Currency_ID,

    Payment.C_ConversionType_ID,

    Payment.DateAcct,

    Payment.DocumentNo,

    Payment.DocStatus,

    Payment.IsReconciled,

    Payment.PayAmt" +
                paymentMethodColumnInAccess +
                executionStatusColumnInAccess + @"

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

            string amountExpression = @"
COALESCE
(
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
    END,
    0
)";

            string sql = @"
WITH
" + schemaCurrencyCte + @",

PaymentFiltered AS
(
" + paymentAccessSql + @"
)
" + executionStatusListCte + @"

SELECT
    Payment.C_Payment_ID,

    Payment.DateAcct AS PaymentDate,

    Payment.DocumentNo AS DocumentNo,

    BPartner.Name AS VendorName,

    " + paymentMethodNameSelect + @"
        AS PaymentMethodName,

    BankAccount.Name AS BankAccountName,

    BankAccount.AccountNo AS BankAccountNo,

    Bank.Name AS BankName,

    MAX(
        Invoice.DocumentNo
    ) AS InvoiceDocumentNo,

    MAX(
        SalesOrder.DocumentNo
    ) AS OrderDocumentNo,

    CASE
        WHEN MAX(
            Invoice.C_Invoice_ID
        ) IS NOT NULL

        OR MAX(
            SalesOrder.C_Order_ID
        ) IS NOT NULL

        THEN 'Y'
        ELSE 'N'
    END AS HasBusinessRef,

    Payment.DocStatus,

    Payment.IsReconciled,

    " + executionStatusValueSelect + @"
        AS VA009_ExecutionStatus,

    " + executionStatusNameSelect + @"
        AS ExecutionStatusName,

    " + translatedExecutionStatusNameSelect + @"
        AS TranslatedExecutionStatusName,

    ROUND
    (
        " + CastNumberSql(amountExpression) + @",

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
    ) AS Amount,

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

LEFT OUTER JOIN C_BPartner BPartner ON
(
    BPartner.C_BPartner_ID =
    Payment.C_BPartner_ID
)

" + paymentMethodJoin + @"

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

LEFT OUTER JOIN C_AllocationLine AllocationLine ON
(
    AllocationLine.C_Payment_ID =
    Payment.C_Payment_ID
)

LEFT OUTER JOIN C_Invoice Invoice ON
(
    Invoice.C_Invoice_ID =
    AllocationLine.C_Invoice_ID
)

LEFT OUTER JOIN C_Order SalesOrder ON
(
    SalesOrder.C_Order_ID =
    Invoice.C_Order_ID
)

" + executionStatusJoin + @"

GROUP BY
    Payment.C_Payment_ID,
    Payment.DateAcct,
    Payment.DocumentNo,
    BPartner.Name,
    BankAccount.Name,
    BankAccount.AccountNo,
    Bank.Name,
    Payment.DocStatus,
    Payment.IsReconciled,
    Payment.PayAmt,
    Payment.C_Currency_ID,
    Payment.C_ConversionType_ID,
    Payment.AD_Client_ID,
    Payment.AD_Org_ID,
    SchemaCurrency.C_Currency_ID" +
                paymentMethodGroupBy +
                executionStatusGroupBy + @"

ORDER BY
    Payment.DateAcct DESC,
    Payment.C_Payment_ID DESC";

            return sql;
        }

        private void AddPaymentRow(
            Ctx ctx,
            IDataReader dr,
            List<object> payments,
            List<string> autoMatchedRefs,
            ref int autoMatchedCount)
        {
            string invoiceDocumentNo =
                GetString(
                    dr,
                    "InvoiceDocumentNo",
                    string.Empty
                );

            string orderDocumentNo =
                GetString(
                    dr,
                    "OrderDocumentNo",
                    string.Empty
                );

            string documentNo =
                GetString(
                    dr,
                    "DocumentNo",
                    string.Empty
                );

            string referenceNo =
                FirstNotEmpty(
                    invoiceDocumentNo,
                    orderDocumentNo,
                    documentNo
                );

            string hasBusinessRef =
                GetString(
                    dr,
                    "HasBusinessRef",
                    "N"
                );

            if (hasBusinessRef == "Y")
            {
                autoMatchedCount++;

                if (
                    !string.IsNullOrWhiteSpace(
                        referenceNo
                    ) &&
                    autoMatchedRefs.Count < 3
                )
                {
                    autoMatchedRefs.Add(
                        referenceNo
                    );
                }
            }

            string isReconciled =
                GetString(
                    dr,
                    "IsReconciled",
                    "N"
                );

            string executionStatus =
                GetString(
                    dr,
                    "VA009_ExecutionStatus",
                    string.Empty
                );

            string executionStatusName =
                FirstNotEmpty(
                    GetString(
                        dr,
                        "TranslatedExecutionStatusName",
                        string.Empty
                    ),

                    GetString(
                        dr,
                        "ExecutionStatusName",
                        string.Empty
                    )
                );

            string statusType =
                GetStatusType(
                    isReconciled,
                    executionStatus
                );

            string statusText =
                GetStatusMessageText(
                    ctx,
                    statusType,
                    executionStatusName
                );

            string currencyISO =
                GetString(
                    dr,
                    "CurrencyISO",
                    string.Empty
                );

            string currencySymbol =
                GetString(
                    dr,
                    "CurrencySymbol",
                    string.Empty
                );

            if (
                string.IsNullOrWhiteSpace(
                    currencySymbol
                )
            )
            {
                currencySymbol =
                    currencyISO;
            }

            int stdPrecision =
                NormalizePrecision(
                    GetInt(
                        dr,
                        "StdPrecision",
                        2
                    )
                );

            decimal amount =
                Math.Round(
                    GetDecimal(
                        dr,
                        "Amount",
                        0
                    ),
                    stdPrecision,
                    MidpointRounding.AwayFromZero
                );

            payments.Add(
                new
                {
                    paymentId =
                        GetInt(
                            dr,
                            "C_Payment_ID"
                        ),

                    paymentDate =
                        FormatDate(
                            GetNullableDate(
                                dr,
                                "PaymentDate"
                            )
                        ),

                    documentNo =
                        documentNo,

                    value =
                        documentNo,

                    vendorName =
                        GetString(
                            dr,
                            "VendorName",
                            string.Empty
                        ),

                    paymentMethodName =
                        GetPaymentMethodName(
                            ctx,
                            GetString(
                                dr,
                                "PaymentMethodName",
                                string.Empty
                            )
                        ),

                    bankAccountName =
                        GetString(
                            dr,
                            "BankAccountName",
                            string.Empty
                        ),

                    bankAccountNo =
                        GetString(
                            dr,
                            "BankAccountNo",
                            string.Empty
                        ),

                    bankName =
                        GetString(
                            dr,
                            "BankName",
                            string.Empty
                        ),

                    referenceNo =
                        referenceNo,

                    docStatus =
                        GetString(
                            dr,
                            "DocStatus",
                            string.Empty
                        ),

                    statusType =
                        statusType,

                    statusKey =
                        statusText,

                    statusName =
                        statusText,

                    amount =
                        amount,

                    cCurrencyId =
                        GetInt(
                            dr,
                            "C_Currency_ID"
                        ),

                    currencyISO =
                        currencyISO,

                    currencySymbol =
                        currencySymbol,

                    stdPrecision =
                        stdPrecision
                }
            );
        }

        private string GetStatusType(
            string isReconciled,
            string executionStatus)
        {
            if (isReconciled == "Y")
            {
                return "cleared";
            }

            if (!string.IsNullOrWhiteSpace(executionStatus))
            {
                return executionStatus;
            }

            return "intransit";
        }

        private string GetStatusMessageText(
            Ctx ctx,
            string statusType,
            string executionStatusName)
        {
            if (statusType == "cleared")
            {
                return GetMsg(
                    ctx,
                    "VAS_032_MessageCleared",
                    "Cleared"
                );
            }

            if (!string.IsNullOrWhiteSpace(executionStatusName))
            {
                return executionStatusName;
            }

            return GetMsg(
                ctx,
                "VAS_032_MessageInTransit",
                "In transit"
            );
        }

        private string GetPaymentMethodName(
            Ctx ctx,
            string paymentMethodName)
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

        private string GetPaymentMethodDisplayColumn(
            string tableAlias)
        {
            if (
                HasColumn(
                    "VA009_PaymentMethod",
                    "VA009_Name"
                )
            )
            {
                return tableAlias + ".VA009_Name";
            }

            if (
                HasColumn(
                    "VA009_PaymentMethod",
                    "Name"
                )
            )
            {
                return tableAlias + ".Name";
            }

            if (
                HasColumn(
                    "VA009_PaymentMethod",
                    "Value"
                )
            )
            {
                return tableAlias + ".Value";
            }

            return string.Empty;
        }

        private bool HasColumn(
            string tableName,
            string columnName)
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

        private string CastNumberSql(
            string expression)
        {
            if (DB.IsOracle())
            {
                return "CAST(" +
                    expression +
                    " AS NUMBER)";
            }

            return "CAST(" +
                expression +
                " AS NUMERIC)";
        }

        private string ToSqlString(
            string value)
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
            params string[] values)
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
            int precision)
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
            int fallback = 0)
        {
            object value =
                reader[columnName];

            return
                value == null ||
                value == DBNull.Value
                    ? fallback
                    : Util.GetValueOfInt(value);
        }

        private decimal GetDecimal(
            IDataReader reader,
            string columnName,
            decimal fallback = 0)
        {
            object value =
                reader[columnName];

            return
                value == null ||
                value == DBNull.Value
                    ? fallback
                    : Util.GetValueOfDecimal(value);
        }

        private string GetString(
            IDataReader reader,
            string columnName,
            string fallback)
        {
            object value =
                reader[columnName];

            return
                value == null ||
                value == DBNull.Value
                    ? fallback
                    : Util.GetValueOfString(value);
        }

        private DateTime? GetNullableDate(
            IDataReader reader,
            string columnName)
        {
            object value =
                reader[columnName];

            if (
                value == null ||
                value == DBNull.Value
            )
            {
                return null;
            }

            return Util.GetValueOfDateTime(value);
        }

        private string FormatDate(
            DateTime? date)
        {
            return date.HasValue
                ? date.Value.ToString(
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture
                )
                : string.Empty;
        }

        private string GetMsg(
            Ctx ctx,
            string key,
            string fallback)
        {
            string msg =
                Msg.GetMsg(
                    ctx,
                    key
                );

            return
                !string.IsNullOrWhiteSpace(msg) &&
                msg != "[" + key + "]"
                    ? msg
                    : fallback;
        }

        private void CloseReader(
            IDataReader reader)
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
