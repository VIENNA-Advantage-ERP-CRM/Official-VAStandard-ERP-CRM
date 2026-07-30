
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
using System.Data.SqlClient;
using System.Globalization;
using System.Web.Mvc;
using VAdvantage.Classes;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VAS.Controllers
{
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
                SqlQueryData queryData =
                    BuildRecentPaymentsSql(
                        ctx
                    );

                sql =
                    queryData.Sql;

                dr = DB.ExecuteReader(
                    queryData.Sql,
                    queryData.Parameters,
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

        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetPaymentAllocationDetail(
    int paymentId)
        {
            Ctx ctx = GetContext();

            if (ctx == null)
            {
                return GetSessionExpiredResult();
            }

            if (paymentId <= 0)
            {
                return Json(
                    new
                    {
                        success = false,
                        error = GetMsg(
                            ctx,
                            "VAS_032_PaymentRequired",
                            "Payment is required."
                        ),
                        errorText = GetMsg(
                            ctx,
                            "VAS_032_PaymentRequired",
                            "Payment is required."
                        ),
                        hasData = false,
                        lines = new List<object>()
                    },
                    JsonRequestBehavior.AllowGet
                );
            }

            IDataReader dr = null;
            string sql = string.Empty;

            try
            {
                string queryParametersFrom =
                    DB.IsOracle()
                        ? " FROM DUAL"
                        : string.Empty;

                string emptyText =
                    DB.IsOracle()
                        ? "CAST('' AS VARCHAR2(4000))"
                        : "CAST('' AS VARCHAR(4000))";

                string paymentDocumentNoSql =
                    GetTextCastSql(
                        "Payment.DocumentNo"
                    );

                string paymentDocStatusSql =
                    GetTextCastSql(
                        "Payment.DocStatus"
                    );

                string paymentIsAllocatedSql =
                    GetTextCastSql(
                        "Payment.IsAllocated"
                    );

                string allocationDocumentNoSql =
                    GetTextCastSql(
                        "AllocationHdr.DocumentNo"
                    );

                string allocationDocStatusSql =
                    GetTextCastSql(
                        "AllocationHdr.DocStatus"
                    );

                string invoiceDocumentNoSql =
                    GetTextCastSql(
                        "Invoice.DocumentNo"
                    );

                string vendorNameSql =
                    GetTextCastSql(
                        "BusinessPartner.Name"
                    );

                string currencyISOSql =
                    GetTextCastSql(
                        "Currency.ISO_Code"
                    );

                string currencySymbolSql =
                    GetTextCastSql(
                        "Currency.CurSymbol"
                    );

                /*
                 * Apply MRole only on physical C_Payment table.
                 */
                string paymentAccessSql = @"
SELECT
    Payment.C_Payment_ID,
    Payment.AD_Client_ID,
    Payment.AD_Org_ID,
    Payment.C_BPartner_ID,
    Payment.C_Currency_ID,
    Payment.DocumentNo,
    Payment.DateTrx,
    Payment.DateAcct,
    Payment.PayAmt,
    Payment.DocStatus,
    Payment.IsAllocated

FROM C_Payment Payment

WHERE Payment.IsActive = 'Y'

AND Payment.AD_Client_ID =
(
    SELECT
        QueryParameters.AD_Client_ID

    FROM QueryParameters QueryParameters
)

AND Payment.C_Payment_ID =
(
    SELECT
        QueryParameters.C_Payment_ID

    FROM QueryParameters QueryParameters
)";

                paymentAccessSql =
                    MRole.GetDefault(ctx).AddAccessSQL(
                        paymentAccessSql,
                        "Payment",
                        MRole.SQL_FULLYQUALIFIED,
                        MRole.SQL_RO
                    );

                sql = @"
WITH QueryParameters AS
(
    SELECT
        @AD_Client_ID AS AD_Client_ID,
        @C_Payment_ID AS C_Payment_ID"
                + queryParametersFrom + @"
),
AccessiblePayment AS
(
" + paymentAccessSql + @"
)
SELECT
    Payment.C_Payment_ID,

    " + paymentDocumentNoSql + @"
        AS PaymentDocumentNo,

    Payment.DateTrx
        AS PaymentDate,

    Payment.PayAmt
        AS PaymentAmount,

    " + paymentDocStatusSql + @"
        AS PaymentDocStatus,

    " + paymentIsAllocatedSql + @"
        AS IsAllocated,

    AllocationHdr.C_AllocationHdr_ID,

    " + allocationDocumentNoSql + @"
        AS AllocationDocumentNo,

    AllocationHdr.DateTrx
        AS AllocationTransactionDate,

    AllocationHdr.DateAcct
        AS AllocationDate,

    " + allocationDocStatusSql + @"
        AS AllocationDocStatus,

    AllocationLine.C_AllocationLine_ID,

    AllocationLine.C_Invoice_ID,

    AllocationLine.C_InvoicePaySchedule_ID,

    CASE
        WHEN Invoice.DocumentNo IS NULL
        THEN " + emptyText + @"
        ELSE " + invoiceDocumentNoSql + @"
    END AS InvoiceDocumentNo,

    Invoice.DateInvoiced,

    COALESCE
    (
        InvoicePaySchedule.DueDate,
        Invoice.DateAcct
    ) AS DueDate,

    CASE
        WHEN BusinessPartner.Name IS NULL
        THEN " + emptyText + @"
        ELSE " + vendorNameSql + @"
    END AS VendorName,

    COALESCE
    (
        AllocationLine.Amount,
        0
    ) AS AllocatedAmount,

    COALESCE
    (
        AllocationLine.DiscountAmt,
        0
    ) AS DiscountAmt,

    COALESCE
    (
        AllocationLine.WriteOffAmt,
        0
    ) AS WriteOffAmt,

    COALESCE
    (
        AllocationLine.OverUnderAmt,
        0
    ) AS OverUnderAmt,

    (
        COALESCE
        (
            AllocationLine.Amount,
            0
        )
        +
        COALESCE
        (
            AllocationLine.DiscountAmt,
            0
        )
        +
        COALESCE
        (
            AllocationLine.WriteOffAmt,
            0
        )
    ) AS TotalAllocatedAmount,

    AllocationHdr.C_Currency_ID,

    CASE
        WHEN Currency.ISO_Code IS NULL
        THEN " + emptyText + @"
        ELSE " + currencyISOSql + @"
    END AS CurrencyISO,

    CASE
        WHEN Currency.CurSymbol IS NULL
        THEN " + emptyText + @"
        ELSE " + currencySymbolSql + @"
    END AS CurrencySymbol,

    COALESCE
    (
        Currency.StdPrecision,
        2
    ) AS StdPrecision

FROM AccessiblePayment Payment

INNER JOIN C_AllocationLine AllocationLine ON
(
    AllocationLine.C_Payment_ID =
    Payment.C_Payment_ID

    AND AllocationLine.IsActive = 'Y'

    AND AllocationLine.AD_Client_ID =
    Payment.AD_Client_ID
)

INNER JOIN C_AllocationHdr AllocationHdr ON
(
    AllocationHdr.C_AllocationHdr_ID =
    AllocationLine.C_AllocationHdr_ID

    AND AllocationHdr.IsActive = 'Y'

    AND AllocationHdr.AD_Client_ID =
    Payment.AD_Client_ID

    AND AllocationHdr.DocStatus IN
    (
        'CO',
        'CL'
    )
)

LEFT OUTER JOIN C_Invoice Invoice ON
(
    Invoice.C_Invoice_ID =
    AllocationLine.C_Invoice_ID

    AND Invoice.IsActive = 'Y'
)

LEFT OUTER JOIN C_InvoicePaySchedule InvoicePaySchedule ON
(
    InvoicePaySchedule.C_InvoicePaySchedule_ID =
    AllocationLine.C_InvoicePaySchedule_ID

    AND InvoicePaySchedule.IsActive = 'Y'
)

LEFT OUTER JOIN C_BPartner BusinessPartner ON
(
    BusinessPartner.C_BPartner_ID =
    Invoice.C_BPartner_ID
)

LEFT OUTER JOIN C_Currency Currency ON
(
    Currency.C_Currency_ID =
    AllocationHdr.C_Currency_ID
)

ORDER BY
    AllocationHdr.DateAcct DESC,
    AllocationHdr.C_AllocationHdr_ID DESC,
    AllocationLine.C_AllocationLine_ID DESC";

                SqlParameter[] parameters =
                    new SqlParameter[]
                    {
                new SqlParameter(
                    "@AD_Client_ID",
                    ctx.GetAD_Client_ID()
                ),

                new SqlParameter(
                    "@C_Payment_ID",
                    paymentId
                )
                    };

                dr = DB.ExecuteReader(
                    sql,
                    parameters,
                    null
                );

                List<object> lines =
                    new List<object>();

                decimal totalAllocatedAmount = 0;
                decimal totalDiscountAmount = 0;
                decimal totalWriteOffAmount = 0;
                decimal grandTotalAmount = 0;

                while (
                    dr != null &&
                    dr.Read()
                )
                {
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

                    decimal allocatedAmount =
                        Math.Round(
                            GetDecimal(
                                dr,
                                "AllocatedAmount",
                                0
                            ),
                            stdPrecision,
                            MidpointRounding.AwayFromZero
                        );

                    decimal discountAmt =
                        Math.Round(
                            GetDecimal(
                                dr,
                                "DiscountAmt",
                                0
                            ),
                            stdPrecision,
                            MidpointRounding.AwayFromZero
                        );

                    decimal writeOffAmt =
                        Math.Round(
                            GetDecimal(
                                dr,
                                "WriteOffAmt",
                                0
                            ),
                            stdPrecision,
                            MidpointRounding.AwayFromZero
                        );

                    decimal overUnderAmt =
                        Math.Round(
                            GetDecimal(
                                dr,
                                "OverUnderAmt",
                                0
                            ),
                            stdPrecision,
                            MidpointRounding.AwayFromZero
                        );

                    decimal totalAllocated =
                        Math.Round(
                            GetDecimal(
                                dr,
                                "TotalAllocatedAmount",
                                0
                            ),
                            stdPrecision,
                            MidpointRounding.AwayFromZero
                        );

                    totalAllocatedAmount +=
                        allocatedAmount;

                    totalDiscountAmount +=
                        discountAmt;

                    totalWriteOffAmount +=
                        writeOffAmt;

                    grandTotalAmount +=
                        totalAllocated;

                    lines.Add(
                        new
                        {
                            paymentId =
                                GetInt(
                                    dr,
                                    "C_Payment_ID"
                                ),

                            paymentDocumentNo =
                                GetString(
                                    dr,
                                    "PaymentDocumentNo",
                                    string.Empty
                                ),

                            paymentDate =
                                FormatDate(
                                    GetNullableDate(
                                        dr,
                                        "PaymentDate"
                                    )
                                ),

                            paymentAmount =
                                GetDecimal(
                                    dr,
                                    "PaymentAmount",
                                    0
                                ),

                            paymentDocStatus =
                                GetString(
                                    dr,
                                    "PaymentDocStatus",
                                    string.Empty
                                ),

                            isAllocated =
                                GetString(
                                    dr,
                                    "IsAllocated",
                                    "N"
                                ),

                            allocationHdrId =
                                GetInt(
                                    dr,
                                    "C_AllocationHdr_ID"
                                ),

                            allocationLineId =
                                GetInt(
                                    dr,
                                    "C_AllocationLine_ID"
                                ),

                            allocationDocumentNo =
                                GetString(
                                    dr,
                                    "AllocationDocumentNo",
                                    string.Empty
                                ),

                            allocationTransactionDate =
                                FormatDate(
                                    GetNullableDate(
                                        dr,
                                        "AllocationTransactionDate"
                                    )
                                ),

                            allocationDate =
                                FormatDate(
                                    GetNullableDate(
                                        dr,
                                        "AllocationDate"
                                    )
                                ),

                            allocationDocStatus =
                                GetString(
                                    dr,
                                    "AllocationDocStatus",
                                    string.Empty
                                ),

                            invoiceId =
                                GetInt(
                                    dr,
                                    "C_Invoice_ID"
                                ),

                            invoicePayScheduleId =
                                GetInt(
                                    dr,
                                    "C_InvoicePaySchedule_ID"
                                ),

                            invoiceDocumentNo =
                                GetString(
                                    dr,
                                    "InvoiceDocumentNo",
                                    string.Empty
                                ),

                            invoiceDate =
                                FormatDate(
                                    GetNullableDate(
                                        dr,
                                        "DateInvoiced"
                                    )
                                ),

                            dueDate =
                                FormatDate(
                                    GetNullableDate(
                                        dr,
                                        "DueDate"
                                    )
                                ),

                            vendorName =
                                GetString(
                                    dr,
                                    "VendorName",
                                    string.Empty
                                ),

                            allocatedAmount =
                                allocatedAmount,

                            discountAmt =
                                discountAmt,

                            writeOffAmt =
                                writeOffAmt,

                            overUnderAmt =
                                overUnderAmt,

                            totalAllocated =
                                totalAllocated,

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

                return Json(
                    new
                    {
                        success = true,
                        error = string.Empty,
                        hasData = lines.Count > 0,

                        totalAllocatedAmount =
                            totalAllocatedAmount,

                        totalDiscountAmount =
                            totalDiscountAmount,

                        totalWriteOffAmount =
                            totalWriteOffAmount,

                        grandTotalAmount =
                            grandTotalAmount,

                        lines =
                            lines
                    },
                    JsonRequestBehavior.AllowGet
                );
            }
            catch (Exception ex)
            {
                VLogger.Get().SaveError(
                    "VAS_032_GetPaymentAllocationDetail",
                    ex
                );

                string message =
                    GetMsg(
                        ctx,
                        "VAS_032_AllocationLoadError",
                        "Could not load allocation details."
                    );

                return Json(
                    new
                    {
                        success = false,
                        error = message,
                        errorText = message,
                        hasData = false,
                        lines = new List<object>()
                    },
                    JsonRequestBehavior.AllowGet
                );
            }
            finally
            {
                CloseReader(
                    dr
                );
            }
        }



        private SqlQueryData BuildRecentPaymentsSql(
            Ctx ctx
        )
        {
            string queryParametersFrom =
                DB.IsOracle()
                    ? " FROM DUAL"
                    : string.Empty;

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
                    ? GetPaymentMethodDisplayColumn(
                        "PaymentMethod"
                    )
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
ExecutionStatusListSource AS
(
    SELECT
        " + GetTextCastSql(
            "RefList.Value"
        ) + @" AS StatusValue,

        " + GetTextCastSql(
            "RefList.Name"
        ) + @" AS BaseStatusName,

        " + GetTextCastSql(
            "RefListTrl.Name"
        ) + @" AS TranslatedStatusName,

        ROW_NUMBER() OVER
        (
            PARTITION BY
                " + GetTextCastSql(
                    "RefList.Value"
                ) + @"

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

    WHERE TableInfo.TableName =
        'C_Payment'

    AND ColumnInfo.ColumnName =
        'VA009_ExecutionStatus'

    AND TableInfo.IsActive = 'Y'

    AND ColumnInfo.IsActive = 'Y'

    AND ReferenceInfo.IsActive = 'Y'

    AND RefList.IsActive = 'Y'
),
ExecutionStatusList AS
(
    SELECT
        ExecutionStatusListSource.StatusValue,
        ExecutionStatusListSource.BaseStatusName,
        ExecutionStatusListSource.TranslatedStatusName

    FROM ExecutionStatusListSource ExecutionStatusListSource

    WHERE ExecutionStatusListSource.StatusRowNumber = 1
)"
                    : string.Empty;

            string executionStatusJoin =
                hasExecutionStatusColumn
                    ? @"
LEFT OUTER JOIN ExecutionStatusList ExecutionStatusList ON
(
    ExecutionStatusList.StatusValue =
    " + GetTextCastSql(
        "Payment.VA009_ExecutionStatus"
    ) + @"
)"
                    : string.Empty;

            string executionStatusValueSelect =
                hasExecutionStatusColumn
                    ? GetTextCastSql(
                        "Payment.VA009_ExecutionStatus"
                    )
                    : GetTextCastSql(
                        "NULL"
                    );

            string executionStatusNameSelect =
                hasExecutionStatusColumn
                    ? "ExecutionStatusList.BaseStatusName"
                    : GetTextCastSql(
                        "NULL"
                    );

            string translatedExecutionStatusNameSelect =
                hasExecutionStatusColumn
                    ? "ExecutionStatusList.TranslatedStatusName"
                    : GetTextCastSql(
                        "NULL"
                    );

            string executionStatusGroupBy =
                hasExecutionStatusColumn
                    ? @",
    " + GetTextCastSql(
        "Payment.VA009_ExecutionStatus"
    ) + @",
    ExecutionStatusList.BaseStatusName,
    ExecutionStatusList.TranslatedStatusName"
                    : string.Empty;

            string queryParametersSql = @"
QueryParameters AS
(
    SELECT
        @AD_Client_ID AS AD_Client_ID,
        @AD_Language AS AD_Language"
        + queryParametersFrom + @"
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

    Payment.PayAmt"
        + paymentMethodColumnInAccess
        + executionStatusColumnInAccess + @"

FROM C_Payment Payment

WHERE Payment.IsActive = 'Y'

AND Payment.IsReceipt = 'N'

AND Payment.AD_Client_ID =
(
    SELECT
        QueryParameters.AD_Client_ID

    FROM QueryParameters QueryParameters
)

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
    Payment.PayAmt,
    0
)";

            string sql = @"
WITH
" + queryParametersSql + @",
PaymentFiltered AS
(
" + paymentAccessSql + @"
)"
    + executionStatusListCte + @"
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

    MAX
    (
        Invoice.DocumentNo
    ) AS InvoiceDocumentNo,

    MAX
    (
        SalesOrder.DocumentNo
    ) AS OrderDocumentNo,

    CASE
        WHEN MAX
        (
            Invoice.C_Invoice_ID
        ) IS NOT NULL

        OR MAX
        (
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
        " + CastNumberSql(
            amountExpression
        ) + @",

        CAST
        (
            COALESCE
            (
                MAX
                (
                    Currency.StdPrecision
                ),
                2
            ) AS INTEGER
        )
    ) AS Amount,

    MAX
    (
        Payment.C_Currency_ID
    ) AS C_Currency_ID,

    MAX
    (
        Currency.StdPrecision
    ) AS StdPrecision,

    MAX
    (
        Currency.ISO_Code
    ) AS CurrencyISO,

    MAX
    (
        CASE
            WHEN Currency.CurSymbol IS NOT NULL
            THEN Currency.CurSymbol
            ELSE Currency.ISO_Code
        END
    ) AS CurrencySymbol

FROM PaymentFiltered Payment

LEFT OUTER JOIN C_BPartner BPartner ON
(
    BPartner.C_BPartner_ID =
    Payment.C_BPartner_ID
)
"
    + paymentMethodJoin + @"
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

LEFT OUTER JOIN C_Currency Currency ON
(
    Currency.C_Currency_ID =
    Payment.C_Currency_ID
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
"
    + executionStatusJoin + @"
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
    Payment.AD_Org_ID"
    + paymentMethodGroupBy
    + executionStatusGroupBy + @"

ORDER BY
    Payment.DateAcct DESC,
    Payment.C_Payment_ID DESC";

            SqlParameter[] parameters =
                new SqlParameter[]
                {
                    new SqlParameter(
                        "@AD_Client_ID",
                        ctx.GetAD_Client_ID()
                    ),

                    new SqlParameter(
                        "@AD_Language",
                        ctx.GetAD_Language()
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
                    ),

                    executionStatus
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

                    status =
                        new
                        {
                            value =
                                executionStatus,

                            name =
                                statusText
                        },

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
                return "CAST("
                    + expression
                    + " AS NUMBER)";
            }

            return "CAST("
                + expression
                + " AS NUMERIC)";
        }

        private string GetTextCastSql(
            string expression)
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
            Ctx ctx =
                Env.GetCtx();

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

        private string FirstNotEmpty(
            params string[] values)
        {
            if (values == null)
            {
                return string.Empty;
            }

            for (
                int i = 0;
                i < values.Length;
                i++
            )
            {
                if (
                    !string.IsNullOrWhiteSpace(
                        values[i]
                    )
                )
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
                    : Util.GetValueOfInt(
                        value
                    );
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
                    : Util.GetValueOfDecimal(
                        value
                    );
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
                    : Util.GetValueOfString(
                        value
                    );
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

            return Util.GetValueOfDateTime(
                value
            );
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
                !string.IsNullOrWhiteSpace(
                    msg
                ) &&
                msg != key &&
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
