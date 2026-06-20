/******************************************************
 * Module Name    : VAS
 * Purpose        : AP Payment Match Suggestions Widget
 * Chronological  : Development
 * Created Date   : 2026-06-17
 * Created by     : VAI145
 ******************************************************/

/*
 * AP Payment Match Suggestions Widget Controller
 *
 * Labels / Message Keys
 * #  | Current Text                         | Message Key
 * ---+--------------------------------------+--------------------------------
 * 1  | Session Expired                      | SessionExpired
 * 2  | Could not load AP payment...         | VAS_072_LoadError
 * 3  | Could not complete allocation        | VAS_072_ApplyError
 * 4  | Allocation completed successfully    | VAS_072_ApplySuccess
 * 5  | Could not open allocation form       | VAS_072_OpenFormError
 * 6  | Could not load match details         | VAS_072_LoadDetailError
 * 7  | AP payment or purchase invoice...    | VAS_072_DetailNotFound
 * 8  | Vendor matches                       | VAS_072_VendorMatches
 * 9  | Vendor differs                       | VAS_072_VendorDiffers
 * 10 | Amount matches                       | VAS_035_AmountMatches
 * 11 | Amount differs                       | VAS_035_AmountDiffers
 * 12 | Reference cited                      | VAS_035_ReferenceCited
 * 13 | No reference cited                   | VAS_035_NoReferenceCited
 * 14 | Within due window                    | VAS_035_WithinDueWindow
 * 15 | Outside due window                   | VAS_035_OutsideDueWindow
 */

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Web.Mvc;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Process;
using VAdvantage.Utility;
using VIS.Filters;

namespace VAS.Controllers
{
    public class VAS_072_MatchSuggestionAPPaymentWidgetController : Controller
    {
        private const int DefaultPageSize = 5;
        private const int MaximumPageSize = 25;
        private const int PaymentWindowDays = 30;
        private const int DateWindowDays = 30;

        private const decimal AmountTolerance = 0.01M;
        private const decimal HighPercentageThreshold = 5M;
        private const decimal ReviewPercentageThreshold = 20M;

        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetMatchSuggestions(
            int pageNo = 1,
            int pageSize = DefaultPageSize)
        {
            Ctx ctx = GetContext();

            if (ctx == null)
            {
                string sessionExpired =
                    GetMsg(
                        Env.GetCtx(),
                        "SessionExpired",
                        "Session Expired"
                    );

                return Json(
                    JsonConvert.SerializeObject(
                        new
                        {
                            success = false,
                            error = sessionExpired,
                            errorText = sessionExpired,
                            hasData = false
                        }
                    ),
                    JsonRequestBehavior.AllowGet
                );
            }

            try
            {
                pageNo = Math.Max(
                    1,
                    pageNo
                );

                pageSize = Math.Max(
                    1,
                    Math.Min(
                        MaximumPageSize,
                        pageSize
                    )
                );

                int offsetRows =
                    (pageNo - 1) * pageSize;

                MatchQueryResult queryResult =
                    ExecuteMatchQuery(
                        ctx,
                        0,
                        0,
                        0,
                        offsetRows,
                        pageSize,
                        null
                    );

                int totalPages =
                    queryResult.TotalRecords == 0
                        ? 0
                        : Convert.ToInt32(
                            Math.Ceiling(
                                queryResult.TotalRecords /
                                (decimal)pageSize
                            )
                        );

                object response = new
                {
                    success = true,
                    error = string.Empty,

                    hasData =
                        queryResult.TotalRecords > 0,

                    pageNo = pageNo,
                    pageSize = pageSize,
                    totalPages = totalPages,

                    totalRecords =
                        queryResult.TotalRecords,

                    totalReadyAmount =
                        queryResult.TotalAccountingAmount,

                    highConfidenceCount =
                        queryResult.HighConfidenceCount,

                    cCurrencyId =
                        queryResult.SchemaCurrencyId,

                    currencyISOCode =
                        queryResult.SchemaCurrencyISOCode,

                    currencySymbol =
                        queryResult.SchemaCurrencySymbol,

                    stdPrecision =
                        queryResult.SchemaStdPrecision,

                    allocationFormId = 0,
                    allocationWindowId = 0,

                    rows = queryResult.Rows.Select(
                        row => new
                        {
                            paymentId =
                                row.PaymentId,

                            invoiceId =
                                row.InvoiceId,

                            payScheduleId =
                                row.InvoicePayScheduleId,

                            vendorId =
                                row.VendorId,

                            vendorName =
                                row.VendorName,

                            paymentDocumentNo =
                                row.PaymentDocumentNo,

                            invoiceDocumentNo =
                                row.InvoiceDocumentNo,

                            paymentDate =
                                FormatDate(
                                    row.PaymentDate
                                ),

                            invoiceDate =
                                FormatDate(
                                    row.InvoiceDate
                                ),

                            dueDate =
                                FormatDate(
                                    row.DueDate
                                ),

                            paymentAmount =
                                row.PaymentOriginalAmount,

                            paymentAllocatedAmount =
                                row.PaymentAllocatedAmount,

                            paymentOpenAmount =
                                row.PaymentOpenAmount,

                            invoiceAmount =
                                row.InvoiceOriginalAmount,

                            invoiceAllocatedAmount =
                                row.InvoiceAllocatedAmount,

                            invoiceOpenAmount =
                                row.InvoiceOpenAmountPaymentCurrency,

                            readyAmount =
                                row.ReadyAmount,

                            accountingAmount =
                                row.AccountingAmount,

                            confidence =
                                row.Confidence,

                            score =
                                row.Score,

                            cCurrencyId =
                                row.PaymentCurrencyId,

                            currencyISOCode =
                                row.PaymentCurrencyISOCode,

                            currencySymbol =
                                row.PaymentCurrencySymbol,

                            stdPrecision =
                                row.PaymentPrecision
                        }
                    )
                };

                return Json(
                    JsonConvert.SerializeObject(
                        response
                    ),
                    JsonRequestBehavior.AllowGet
                );
            }
            catch (Exception ex)
            {
                string errorMessage =
                    GetMsg(
                        ctx,
                        "VAS_072_LoadError",
                        "Could not load AP payment match suggestions"
                    );

                return Json(
                    JsonConvert.SerializeObject(
                        new
                        {
                            success = false,
                            error = errorMessage,
                            errorText =
                                errorMessage + " - " + ex.Message,
                            hasData = false
                        }
                    ),
                    JsonRequestBehavior.AllowGet
                );
            }
        }

        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetMatchDetail(
            int paymentId = 0,
            int invoiceId = 0,
            int payScheduleId = 0)
        {
            Ctx ctx = GetContext();

            if (ctx == null)
            {
                string sessionExpired =
                    GetMsg(
                        Env.GetCtx(),
                        "SessionExpired",
                        "Session Expired"
                    );

                return Json(
                    JsonConvert.SerializeObject(
                        new
                        {
                            success = false,
                            error = sessionExpired,
                            errorText = sessionExpired
                        }
                    ),
                    JsonRequestBehavior.AllowGet
                );
            }

            if (
                paymentId <= 0 ||
                invoiceId <= 0 ||
                payScheduleId <= 0
            )
            {
                return Json(
                    JsonConvert.SerializeObject(
                        new
                        {
                            success = false,

                            error = GetMsg(
                                ctx,
                                "FillMandatory",
                                "Mandatory values are missing"
                            )
                        }
                    ),
                    JsonRequestBehavior.AllowGet
                );
            }

            try
            {
                MatchQueryResult queryResult =
                    ExecuteMatchQuery(
                        ctx,
                        paymentId,
                        invoiceId,
                        payScheduleId,
                        0,
                        0,
                        null
                    );

                MatchQueryRow row =
                    queryResult.Rows.FirstOrDefault(
                        item =>
                            item.PaymentId == paymentId &&
                            item.InvoiceId == invoiceId &&
                            item.InvoicePayScheduleId ==
                            payScheduleId
                    );

                if (row == null)
                {
                    return Json(
                        JsonConvert.SerializeObject(
                            new
                            {
                                success = false,

                                error = GetMsg(
                                    ctx,
                                    "VIS_NoRecordFound",
                                    "No record found"
                                )
                            }
                        ),
                        JsonRequestBehavior.AllowGet
                    );
                }

                object detail = new
                {
                    paymentId =
                        row.PaymentId,

                    paymentDocumentNo =
                        row.PaymentDocumentNo,

                    paymentDate =
                        FormatDate(
                            row.PaymentDate
                        ),

                    vendorId =
                        row.VendorId,

                    vendorName =
                        row.VendorName,

                    paymentMethod =
                        row.PaymentMethod,

                    reference =
                        row.ReferenceNo,

                    bankName =
                        row.BankName,

                    accountNo =
                        row.AccountNo,

                    paymentCurrencyId =
                        row.PaymentCurrencyId,

                    paymentCurrencyISOCode =
                        row.PaymentCurrencyISOCode,

                    paymentCurrencySymbol =
                        row.PaymentCurrencySymbol,

                    paymentPrecision =
                        row.PaymentPrecision,

                    paymentOriginalAmount =
                        row.PaymentOriginalAmount,

                    paymentAllocatedAmount =
                        row.PaymentAllocatedAmount,

                    paymentOpenAmount =
                        row.PaymentOpenAmount,

                    invoiceId =
                        row.InvoiceId,

                    invoicePayScheduleId =
                        row.InvoicePayScheduleId,

                    invoiceDocumentNo =
                        row.InvoiceDocumentNo,

                    invoiceDate =
                        FormatDate(
                            row.InvoiceDate
                        ),

                    dueDate =
                        FormatDate(
                            row.DueDate
                        ),

                    paymentTerms =
                        row.PaymentTerms,

                    invoiceCurrencyId =
                        row.InvoiceCurrencyId,

                    invoiceCurrencyISOCode =
                        row.InvoiceCurrencyISOCode,

                    invoiceCurrencySymbol =
                        row.InvoiceCurrencySymbol,

                    invoicePrecision =
                        row.InvoicePrecision,

                    invoiceOriginalAmount =
                        row.InvoiceOriginalAmount,

                    invoiceAllocatedAmount =
                        row.InvoiceAllocatedAmount,

                    invoiceOpenAmount =
                        row.InvoiceOpenAmount,

                    invoiceOpenAmountPaymentCurrency =
                        row.InvoiceOpenAmountPaymentCurrency,

                    readyAmount =
                        row.ReadyAmount,

                    accountingAmount =
                        row.AccountingAmount,

                    balanceAfterApply =
                        Math.Round(
                            row.InvoiceOpenAmountPaymentCurrency -
                            row.ReadyAmount,
                            row.PaymentPrecision,
                            MidpointRounding.AwayFromZero
                        ),

                    partnerOk = true,

                    amountOk =
                        IsAmountMatch(
                            row.PaymentOpenAmount,
                            row.InvoiceOpenAmountPaymentCurrency
                        ),

                    referenceOk =
                        IsReferenceMatch(
                            row.ReferenceNo,
                            row.InvoiceDocumentNo
                        ),

                    dateOk =
                        IsDateMatch(
                            row.PaymentDate,
                            row.DueDate
                        ),

                    score =
                        row.Score,

                    confidence =
                        row.Confidence
                };

                return Json(
                    JsonConvert.SerializeObject(
                        new
                        {
                            success = true,
                            error = string.Empty,
                            detail = detail
                        }
                    ),
                    JsonRequestBehavior.AllowGet
                );
            }
            catch (Exception ex)
            {
                return Json(
                    JsonConvert.SerializeObject(
                        new
                        {
                            success = false,

                            error = GetMsg(
                                ctx,
                                "VAS_072_LoadDetailError",
                                "Could not load match details"
                            ) + " - " + ex.Message
                        }
                    ),
                    JsonRequestBehavior.AllowGet
                );
            }
        }

        [HttpPost]
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult ApplyAllocation(
            int paymentId = 0,
            int invoiceId = 0,
            int payScheduleId = 0)
        {
            Ctx ctx = GetContext();

            if (ctx == null)
            {
                string sessionExpired =
                    GetMsg(
                        Env.GetCtx(),
                        "SessionExpired",
                        "Session Expired"
                    );

                return Json(
                    JsonConvert.SerializeObject(
                        new
                        {
                            success = false,
                            error = sessionExpired,
                            message = sessionExpired
                        }
                    )
                );
            }

            ApplyResult result =
                ApplyMatchAllocation(
                    ctx,
                    paymentId,
                    invoiceId,
                    payScheduleId
                );

            return Json(
                JsonConvert.SerializeObject(
                    new
                    {
                        success =
                            result.Success,

                        documentNo =
                            result.DocumentNo,

                        message =
                            result.Message
                    }
                )
            );
        }

        [HttpPost]
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult ApplyHighConfidence()
        {
            Ctx ctx = GetContext();

            if (ctx == null)
            {
                string sessionExpired =
                    GetMsg(
                        Env.GetCtx(),
                        "SessionExpired",
                        "Session Expired"
                    );

                return Json(
                    JsonConvert.SerializeObject(
                        new
                        {
                            success = false,
                            error = sessionExpired,
                            message = sessionExpired
                        }
                    )
                );
            }

            try
            {
                MatchQueryResult queryResult =
                    ExecuteMatchQuery(
                        ctx,
                        0,
                        0,
                        0,
                        0,
                        0,
                        null
                    );

                List<MatchQueryRow> highConfidenceRows =
                    queryResult.Rows
                        .Where(
                            row => string.Equals(
                                row.Confidence,
                                "HIGH",
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                        .ToList();

                int appliedCount = 0;

                List<string> errors =
                    new List<string>();

                foreach (
                    MatchQueryRow row
                    in highConfidenceRows
                )
                {
                    ApplyResult applyResult =
                        ApplyMatchAllocation(
                            ctx,
                            row.PaymentId,
                            row.InvoiceId,
                            row.InvoicePayScheduleId
                        );

                    if (applyResult.Success)
                    {
                        appliedCount++;
                    }
                    else if (
                        !string.IsNullOrWhiteSpace(
                            applyResult.Message
                        )
                    )
                    {
                        errors.Add(
                            applyResult.Message
                        );
                    }
                }

                bool success =
                    errors.Count == 0;

                string message =
                    success
                        ? GetMsg(
                            ctx,
                            "VAS_072_ApplySuccess",
                            "Allocation completed successfully"
                        ) +
                        " (" +
                        appliedCount +
                        ")"
                        : GetMsg(
                            ctx,
                            "VAS_072_ApplyError",
                            "Could not complete allocation"
                        ) +
                        ". " +
                        string.Join(
                            " | ",
                            errors
                        );

                return Json(
                    JsonConvert.SerializeObject(
                        new
                        {
                            success = success,

                            appliedCount =
                                appliedCount,

                            failedCount =
                                errors.Count,

                            message = message
                        }
                    )
                );
            }
            catch (Exception ex)
            {
                string errorMessage =
                    GetMsg(
                        ctx,
                        "VAS_072_ApplyError",
                        "Could not complete allocation"
                    );

                return Json(
                    JsonConvert.SerializeObject(
                        new
                        {
                            success = false,
                            error = errorMessage,
                            message =
                                errorMessage + " - " + ex.Message
                        }
                    )
                );
            }
        }

        private MatchQueryResult ExecuteMatchQuery(
            Ctx ctx,
            int paymentId,
            int invoiceId,
            int payScheduleId,
            int offsetRows,
            int pageSize,
            Trx trx)
        {
            MatchQueryResult result =
                new MatchQueryResult();

            if (ctx == null)
            {
                return result;
            }

            int clientId =
                ctx.GetAD_Client_ID();

            string clientIdSql =
                ToSqlInteger(clientId);

            string paymentIdSql =
                ToSqlInteger(
                    Math.Max(
                        0,
                        paymentId
                    )
                );

            string invoiceIdSql =
                ToSqlInteger(
                    Math.Max(
                        0,
                        invoiceId
                    )
                );

            string payScheduleIdSql =
                ToSqlInteger(
                    Math.Max(
                        0,
                        payScheduleId
                    )
                );

            int safeOffsetRows =
                Math.Max(
                    0,
                    offsetRows
                );

            int safePageSize =
                Math.Max(
                    0,
                    pageSize
                );

            string dateGapDaysSql =
                GetDateDifferenceDaysSql(
                    "CandidateRows.PaymentDateAcct",
                    "CandidateRows.DueDate"
                );

            string pagingCondition;

            if (safePageSize == 0)
            {
                pagingCondition = "1 = 1";
            }
            else
            {
                int lastRow =
                    safeOffsetRows +
                    safePageSize;

                pagingCondition = @"
NumberedRows.ResultRowNo >
    " + ToSqlInteger(
                        safeOffsetRows
                    ) + @"

AND NumberedRows.ResultRowNo <=
    " + ToSqlInteger(
                        lastRow
                    );
            }

            string paymentAccessSql = @"
SELECT
    Payment.AD_Client_ID,
    Payment.AD_Org_ID,
    Payment.C_Payment_ID,
    Payment.C_BPartner_ID,
    Payment.DocumentNo,
    Payment.DateAcct,
    Payment.C_Currency_ID,
    Payment.C_ConversionType_ID,
    Payment.PayAmt,
    Payment.TrxNo,
    Payment.CheckNo,
    Payment.VA009_PaymentMethod_ID,
    Payment.C_BankAccount_ID

FROM C_Payment Payment

WHERE Payment.IsActive = 'Y'

AND Payment.AD_Client_ID =
    " + clientIdSql + @"

AND Payment.Processed = 'Y'

AND Payment.IsReceipt = 'N'

AND Payment.DocStatus IN
(
    'CO',
    'CL'
)

AND Payment.C_BPartner_ID IS NOT NULL

AND Payment.DateAcct >=
    " + GetDateAddDaysSql(
                    GetCurrentDateSql(),
                    -PaymentWindowDays
                ) + @"

AND Payment.DateAcct <
    " + GetDateAddDaysSql(
                    GetCurrentDateSql(),
                    1
                );

            paymentAccessSql =
                MRole.GetDefault(ctx)
                    .AddAccessSQL(
                        paymentAccessSql,
                        "Payment",
                        MRole.SQL_FULLYQUALIFIED,
                        MRole.SQL_RO
                    );

            string invoiceAccessSql = @"
SELECT
    Invoice.AD_Client_ID,
    Invoice.AD_Org_ID,
    Invoice.C_Invoice_ID,
    Invoice.C_BPartner_ID,
    Invoice.DocumentNo,
    Invoice.DateInvoiced,
    Invoice.DateAcct,
    Invoice.C_Currency_ID,
    Invoice.GrandTotal,
    Invoice.C_PaymentTerm_ID

FROM C_Invoice Invoice

WHERE Invoice.IsActive = 'Y'

AND Invoice.AD_Client_ID =
    " + clientIdSql + @"

AND Invoice.Processed = 'Y'

AND Invoice.IsSOTrx = 'N'

AND Invoice.DocStatus IN
(
    'CO',
    'CL'
)

AND COALESCE(
    Invoice.IsPaid,
    'N'
) = 'N'

AND COALESCE(
    Invoice.IsReturnTrx,
    'N'
) = 'N'";

            invoiceAccessSql =
                MRole.GetDefault(ctx)
                    .AddAccessSQL(
                        invoiceAccessSql,
                        "Invoice",
                        MRole.SQL_FULLYQUALIFIED,
                        MRole.SQL_RO
                    );

            string sql = @"
WITH SchemaCurrency AS
(
    SELECT
        ClientInfo.AD_Client_ID,

        AcctSchema.C_Currency_ID
            AS SchemaCurrencyId,

        Currency.ISO_Code
            AS SchemaCurrencyISO,

        Currency.CurSymbol
            AS SchemaCurrencySymbol,

        Currency.StdPrecision
            AS SchemaPrecision

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
),

AccessiblePayments AS
(
" + paymentAccessSql + @"
),

PaymentRecords AS
(
    SELECT
        AccessiblePayments.AD_Client_ID,
        AccessiblePayments.AD_Org_ID,
        AccessiblePayments.C_Payment_ID,
        AccessiblePayments.C_BPartner_ID,

        AccessiblePayments.DocumentNo
            AS PaymentDocNo,

        AccessiblePayments.DateAcct
            AS PaymentDateAcct,

        AccessiblePayments.C_Currency_ID
            AS PaymentCurrencyId,

        COALESCE
        (
            AccessiblePayments.C_ConversionType_ID,
            0
        ) AS PaymentConversionTypeId,

        ABS
        (
            COALESCE
            (
                AccessiblePayments.PayAmt,
                0
            )
        ) AS PaymentOriginalAmt,

        BusinessPartner.Name
            AS VendorName,

        PaymentMethod.VA009_Name
            AS PaymentMethod,

        COALESCE
        (
            AccessiblePayments.TrxNo,
            AccessiblePayments.CheckNo
        ) AS ReferenceNo,

        Bank.Name
            AS BankName,

        BankAccount.AccountNo
            AS AccountNo,

        PaymentCurrency.ISO_Code
            AS PaymentCurrencyISO,

        PaymentCurrency.CurSymbol
            AS PaymentCurrencySymbol,

        PaymentCurrency.StdPrecision
            AS PaymentPrecision

    FROM AccessiblePayments AccessiblePayments

    INNER JOIN C_BPartner BusinessPartner ON
    (
        BusinessPartner.C_BPartner_ID =
        AccessiblePayments.C_BPartner_ID
    )

    INNER JOIN C_Currency PaymentCurrency ON
    (
        PaymentCurrency.C_Currency_ID =
        AccessiblePayments.C_Currency_ID
    )

    LEFT OUTER JOIN VA009_PaymentMethod PaymentMethod ON
    (
        PaymentMethod.VA009_PaymentMethod_ID =
        AccessiblePayments.VA009_PaymentMethod_ID
    )

    LEFT OUTER JOIN C_BankAccount BankAccount ON
    (
        BankAccount.C_BankAccount_ID =
        AccessiblePayments.C_BankAccount_ID
    )

    LEFT OUTER JOIN C_Bank Bank ON
    (
        Bank.C_Bank_ID =
        BankAccount.C_Bank_ID
    )

    WHERE
    (
        " + paymentIdSql + @" = 0

        OR AccessiblePayments.C_Payment_ID =
            " + paymentIdSql + @"
    )
),

PaymentAmounts AS
(
    SELECT
        PaymentRecords.AD_Client_ID,
        PaymentRecords.AD_Org_ID,
        PaymentRecords.C_Payment_ID,
        PaymentRecords.C_BPartner_ID,
        PaymentRecords.PaymentDocNo,
        PaymentRecords.PaymentDateAcct,
        PaymentRecords.PaymentCurrencyId,
        PaymentRecords.PaymentConversionTypeId,
        PaymentRecords.PaymentOriginalAmt,

        CASE
            WHEN PaymentRecords.PaymentOriginalAmt >
                 ABS
                 (
                     COALESCE
                     (
                         ALLOCPAYMENTAVAILABLE
                         (
                             PaymentRecords.C_Payment_ID
                         ),
                         0
                     )
                 )

            THEN PaymentRecords.PaymentOriginalAmt -
                 ABS
                 (
                     COALESCE
                     (
                         ALLOCPAYMENTAVAILABLE
                         (
                             PaymentRecords.C_Payment_ID
                         ),
                         0
                     )
                 )

            ELSE 0
        END AS PaymentAllocatedAmt,

        ABS
        (
            COALESCE
            (
                ALLOCPAYMENTAVAILABLE
                (
                    PaymentRecords.C_Payment_ID
                ),
                0
            )
        ) AS PaymentOpenAmt,

        PaymentRecords.VendorName,
        PaymentRecords.PaymentMethod,
        PaymentRecords.ReferenceNo,
        PaymentRecords.BankName,
        PaymentRecords.AccountNo,
        PaymentRecords.PaymentCurrencyISO,
        PaymentRecords.PaymentCurrencySymbol,
        PaymentRecords.PaymentPrecision

    FROM PaymentRecords PaymentRecords

    WHERE ABS
    (
        COALESCE
        (
            ALLOCPAYMENTAVAILABLE
            (
                PaymentRecords.C_Payment_ID
            ),
            0
        )
    ) > 0
),

AccessibleInvoices AS
(
" + invoiceAccessSql + @"
),

InvoiceRecords AS
(
    SELECT
        AccessibleInvoices.AD_Client_ID,
        AccessibleInvoices.AD_Org_ID,
        AccessibleInvoices.C_Invoice_ID,
        AccessibleInvoices.C_BPartner_ID,

        AccessibleInvoices.DocumentNo
            AS InvoiceDocNo,

        AccessibleInvoices.DateInvoiced
            AS InvoiceDate,

        AccessibleInvoices.DateAcct
            AS InvoiceDateAcct,

        AccessibleInvoices.C_Currency_ID
            AS InvoiceCurrencyId,

        AccessibleInvoices.GrandTotal,

        AccessibleInvoices.C_PaymentTerm_ID,

        InvoicePaySchedule.C_InvoicePaySchedule_ID,

        InvoicePaySchedule.DueDate,

        ABS
        (
            COALESCE
            (
                InvoicePaySchedule.DueAmt,
                0
            )
        ) AS InvoiceOriginalAmt,

        ABS
        (
            COALESCE
            (
                InvoiceOpen
                (
                    AccessibleInvoices.C_Invoice_ID,
                    InvoicePaySchedule.C_InvoicePaySchedule_ID
                ),
                0
            )
        ) AS InvoiceOpenAmt,

        InvoiceCurrency.ISO_Code
            AS InvoiceCurrencyISO,

        InvoiceCurrency.CurSymbol
            AS InvoiceCurrencySymbol,

        InvoiceCurrency.StdPrecision
            AS InvoicePrecision,

        PaymentTerm.Name
            AS PaymentTerms

    FROM AccessibleInvoices AccessibleInvoices

    INNER JOIN C_InvoicePaySchedule InvoicePaySchedule ON
    (
        InvoicePaySchedule.C_Invoice_ID =
        AccessibleInvoices.C_Invoice_ID
    )

    INNER JOIN C_Currency InvoiceCurrency ON
    (
        InvoiceCurrency.C_Currency_ID =
        AccessibleInvoices.C_Currency_ID
    )

    LEFT OUTER JOIN C_PaymentTerm PaymentTerm ON
    (
        PaymentTerm.C_PaymentTerm_ID =
        AccessibleInvoices.C_PaymentTerm_ID
    )

    WHERE InvoicePaySchedule.IsActive = 'Y'

    AND COALESCE
    (
        InvoicePaySchedule.IsValid,
        'Y'
    ) = 'Y'

    AND COALESCE
    (
        InvoicePaySchedule.VA009_IsPaid,
        'N'
    ) = 'N'

    AND
    (
        " + invoiceIdSql + @" = 0

        OR AccessibleInvoices.C_Invoice_ID =
            " + invoiceIdSql + @"
    )

    AND
    (
        " + payScheduleIdSql + @" = 0

        OR InvoicePaySchedule.C_InvoicePaySchedule_ID =
            " + payScheduleIdSql + @"
    )
),

InvoiceAmounts AS
(
    SELECT
        InvoiceRecords.AD_Client_ID,
        InvoiceRecords.AD_Org_ID,
        InvoiceRecords.C_Invoice_ID,
        InvoiceRecords.C_BPartner_ID,
        InvoiceRecords.InvoiceDocNo,
        InvoiceRecords.InvoiceDate,
        InvoiceRecords.InvoiceDateAcct,
        InvoiceRecords.InvoiceCurrencyId,
        InvoiceRecords.C_InvoicePaySchedule_ID,
        InvoiceRecords.DueDate,
        InvoiceRecords.InvoiceOriginalAmt,

        CASE
            WHEN InvoiceRecords.InvoiceOriginalAmt >
                 InvoiceRecords.InvoiceOpenAmt

            THEN InvoiceRecords.InvoiceOriginalAmt -
                 InvoiceRecords.InvoiceOpenAmt

            ELSE 0
        END AS InvoiceAllocatedAmt,

        InvoiceRecords.InvoiceOpenAmt,
        InvoiceRecords.InvoiceCurrencyISO,
        InvoiceRecords.InvoiceCurrencySymbol,
        InvoiceRecords.InvoicePrecision,
        InvoiceRecords.PaymentTerms

    FROM InvoiceRecords InvoiceRecords

    WHERE InvoiceRecords.InvoiceOpenAmt > 0
),

CandidateRows AS
(
    SELECT
        PaymentAmounts.AD_Client_ID,
        PaymentAmounts.AD_Org_ID,
        PaymentAmounts.C_Payment_ID,
        PaymentAmounts.C_BPartner_ID,
        PaymentAmounts.PaymentDocNo,
        PaymentAmounts.PaymentDateAcct,
        PaymentAmounts.PaymentCurrencyId,
        PaymentAmounts.PaymentConversionTypeId,
        PaymentAmounts.PaymentOriginalAmt,
        PaymentAmounts.PaymentAllocatedAmt,
        PaymentAmounts.PaymentOpenAmt,
        PaymentAmounts.VendorName,
        PaymentAmounts.PaymentMethod,
        PaymentAmounts.ReferenceNo,
        PaymentAmounts.BankName,
        PaymentAmounts.AccountNo,
        PaymentAmounts.PaymentCurrencyISO,
        PaymentAmounts.PaymentCurrencySymbol,
        PaymentAmounts.PaymentPrecision,

        InvoiceAmounts.C_Invoice_ID,
        InvoiceAmounts.C_InvoicePaySchedule_ID,
        InvoiceAmounts.InvoiceDocNo,
        InvoiceAmounts.InvoiceDate,
        InvoiceAmounts.DueDate,
        InvoiceAmounts.PaymentTerms,
        InvoiceAmounts.InvoiceCurrencyId,
        InvoiceAmounts.InvoiceCurrencyISO,
        InvoiceAmounts.InvoiceCurrencySymbol,
        InvoiceAmounts.InvoicePrecision,
        InvoiceAmounts.InvoiceOriginalAmt,
        InvoiceAmounts.InvoiceAllocatedAmt,
        InvoiceAmounts.InvoiceOpenAmt,

        CASE
            WHEN InvoiceAmounts.InvoiceCurrencyId =
                 PaymentAmounts.PaymentCurrencyId

            THEN InvoiceAmounts.InvoiceOpenAmt

            ELSE COALESCE
            (
                CurrencyConvert
                (
                    InvoiceAmounts.InvoiceOpenAmt,
                    InvoiceAmounts.InvoiceCurrencyId,
                    PaymentAmounts.PaymentCurrencyId,
                    PaymentAmounts.PaymentDateAcct,
                    PaymentAmounts.PaymentConversionTypeId,
                    PaymentAmounts.AD_Client_ID,
                    PaymentAmounts.AD_Org_ID
                ),
                0
            )
        END AS InvoiceOpenPayAmt

    FROM PaymentAmounts PaymentAmounts

    INNER JOIN InvoiceAmounts InvoiceAmounts ON
    (
        InvoiceAmounts.AD_Client_ID =
        PaymentAmounts.AD_Client_ID

        AND InvoiceAmounts.C_BPartner_ID =
        PaymentAmounts.C_BPartner_ID

        AND
        (
            InvoiceAmounts.AD_Org_ID =
            PaymentAmounts.AD_Org_ID

            OR InvoiceAmounts.AD_Org_ID = 0

            OR PaymentAmounts.AD_Org_ID = 0
        )
    )
),

CandidateMetrics AS
(
    SELECT
        CandidateRows.AD_Client_ID,
        CandidateRows.AD_Org_ID,
        CandidateRows.C_Payment_ID,
        CandidateRows.C_BPartner_ID,
        CandidateRows.PaymentDocNo,
        CandidateRows.PaymentDateAcct,
        CandidateRows.PaymentCurrencyId,
        CandidateRows.PaymentConversionTypeId,
        CandidateRows.PaymentOriginalAmt,
        CandidateRows.PaymentAllocatedAmt,
        CandidateRows.PaymentOpenAmt,
        CandidateRows.VendorName,
        CandidateRows.PaymentMethod,
        CandidateRows.ReferenceNo,
        CandidateRows.BankName,
        CandidateRows.AccountNo,
        CandidateRows.PaymentCurrencyISO,
        CandidateRows.PaymentCurrencySymbol,
        CandidateRows.PaymentPrecision,
        CandidateRows.C_Invoice_ID,
        CandidateRows.C_InvoicePaySchedule_ID,
        CandidateRows.InvoiceDocNo,
        CandidateRows.InvoiceDate,
        CandidateRows.DueDate,
        CandidateRows.PaymentTerms,
        CandidateRows.InvoiceCurrencyId,
        CandidateRows.InvoiceCurrencyISO,
        CandidateRows.InvoiceCurrencySymbol,
        CandidateRows.InvoicePrecision,
        CandidateRows.InvoiceOriginalAmt,
        CandidateRows.InvoiceAllocatedAmt,
        CandidateRows.InvoiceOpenAmt,
        CandidateRows.InvoiceOpenPayAmt,

        ABS
        (
            CandidateRows.PaymentOpenAmt -
            CandidateRows.InvoiceOpenPayAmt
        ) AS DifferenceAmt,

        CASE
            WHEN CandidateRows.InvoiceOpenPayAmt = 0
            THEN 100

            ELSE
                ABS
                (
                    CandidateRows.PaymentOpenAmt -
                    CandidateRows.InvoiceOpenPayAmt
                ) * 100 /
                ABS(
                    CandidateRows.InvoiceOpenPayAmt
                )
        END AS DifferencePct,

        CASE
            WHEN CandidateRows.DueDate IS NULL
            THEN 999999

            ELSE " + dateGapDaysSql + @"
        END AS DateGapDays

    FROM CandidateRows CandidateRows

    WHERE CandidateRows.PaymentOpenAmt > 0

    AND CandidateRows.InvoiceOpenPayAmt > 0
),

CandidateScores AS
(
    SELECT
        CandidateMetrics.AD_Client_ID,
        CandidateMetrics.AD_Org_ID,
        CandidateMetrics.C_Payment_ID,
        CandidateMetrics.C_BPartner_ID,
        CandidateMetrics.PaymentDocNo,
        CandidateMetrics.PaymentDateAcct,
        CandidateMetrics.PaymentCurrencyId,
        CandidateMetrics.PaymentConversionTypeId,
        CandidateMetrics.PaymentOriginalAmt,
        CandidateMetrics.PaymentAllocatedAmt,
        CandidateMetrics.PaymentOpenAmt,
        CandidateMetrics.VendorName,
        CandidateMetrics.PaymentMethod,
        CandidateMetrics.ReferenceNo,
        CandidateMetrics.BankName,
        CandidateMetrics.AccountNo,
        CandidateMetrics.PaymentCurrencyISO,
        CandidateMetrics.PaymentCurrencySymbol,
        CandidateMetrics.PaymentPrecision,
        CandidateMetrics.C_Invoice_ID,
        CandidateMetrics.C_InvoicePaySchedule_ID,
        CandidateMetrics.InvoiceDocNo,
        CandidateMetrics.InvoiceDate,
        CandidateMetrics.DueDate,
        CandidateMetrics.PaymentTerms,
        CandidateMetrics.InvoiceCurrencyId,
        CandidateMetrics.InvoiceCurrencyISO,
        CandidateMetrics.InvoiceCurrencySymbol,
        CandidateMetrics.InvoicePrecision,
        CandidateMetrics.InvoiceOriginalAmt,
        CandidateMetrics.InvoiceAllocatedAmt,
        CandidateMetrics.InvoiceOpenAmt,
        CandidateMetrics.InvoiceOpenPayAmt,
        CandidateMetrics.DifferenceAmt,
        CandidateMetrics.DifferencePct,
        CandidateMetrics.DateGapDays,

        10 +

        CASE
            WHEN CandidateMetrics.DifferenceAmt <=
                " + AmountTolerance.ToString(
                    CultureInfo.InvariantCulture
                ) + @"
            THEN 70

            WHEN CandidateMetrics.DifferencePct <=
                " + HighPercentageThreshold.ToString(
                    CultureInfo.InvariantCulture
                ) + @"
            THEN 60

            WHEN CandidateMetrics.DifferencePct <=
                " + ReviewPercentageThreshold.ToString(
                    CultureInfo.InvariantCulture
                ) + @"
            THEN 35

            ELSE 10
        END +

        CASE
            WHEN CandidateMetrics.DateGapDays <= 7
            THEN 20

            WHEN CandidateMetrics.DateGapDays <=
                " + DateWindowDays.ToString(
                    CultureInfo.InvariantCulture
                ) + @"
            THEN 10

            ELSE 0
        END AS MatchScore

    FROM CandidateMetrics CandidateMetrics
),

ClassifiedCandidates AS
(
    SELECT
        CandidateScores.*,

        CASE
            WHEN CandidateScores.MatchScore >= 80
            THEN 'HIGH'

            WHEN CandidateScores.MatchScore >= 55
            THEN 'REVIEW'

            ELSE 'LOW'
        END AS MatchConfidence

    FROM CandidateScores CandidateScores
),

PaymentRanked AS
(
    SELECT
        ClassifiedCandidates.*,

        ROW_NUMBER() OVER
        (
            PARTITION BY
                ClassifiedCandidates.C_Payment_ID

            ORDER BY
                ClassifiedCandidates.MatchScore DESC,
                ClassifiedCandidates.DifferenceAmt ASC,
                ClassifiedCandidates.DateGapDays ASC,
                ClassifiedCandidates.DueDate ASC,
                ClassifiedCandidates.InvoiceDocNo ASC,
                ClassifiedCandidates.C_InvoicePaySchedule_ID ASC
        ) AS PaymentRank

    FROM ClassifiedCandidates ClassifiedCandidates
),

BestPerPayment AS
(
    SELECT
        PaymentRanked.*

    FROM PaymentRanked PaymentRanked

    WHERE PaymentRanked.PaymentRank = 1

    AND PaymentRanked.MatchConfidence IN
    (
        'HIGH',
        'REVIEW'
    )
),

ScheduleRanked AS
(
    SELECT
        BestPerPayment.*,

        ROW_NUMBER() OVER
        (
            PARTITION BY
                BestPerPayment.C_InvoicePaySchedule_ID

            ORDER BY
                BestPerPayment.MatchScore DESC,
                BestPerPayment.DifferenceAmt ASC,
                BestPerPayment.DateGapDays ASC,
                BestPerPayment.PaymentDateAcct ASC,
                BestPerPayment.C_Payment_ID ASC
        ) AS ScheduleRank

    FROM BestPerPayment BestPerPayment
),

UniqueMatches AS
(
    SELECT
        ScheduleRanked.*,

        CASE
            WHEN ScheduleRanked.PaymentOpenAmt <
                 ScheduleRanked.InvoiceOpenPayAmt

            THEN ScheduleRanked.PaymentOpenAmt

            ELSE ScheduleRanked.InvoiceOpenPayAmt
        END AS ReadyAmt

    FROM ScheduleRanked ScheduleRanked

    WHERE ScheduleRanked.ScheduleRank = 1
),

AccountingRows AS
(
    SELECT
        UniqueMatches.*,

        CASE
            WHEN UniqueMatches.PaymentCurrencyId =
                 SchemaCurrency.SchemaCurrencyId

            THEN UniqueMatches.ReadyAmt

            ELSE COALESCE
            (
                CurrencyConvert
                (
                    UniqueMatches.ReadyAmt,
                    UniqueMatches.PaymentCurrencyId,
                    SchemaCurrency.SchemaCurrencyId,
                    UniqueMatches.PaymentDateAcct,
                    UniqueMatches.PaymentConversionTypeId,
                    UniqueMatches.AD_Client_ID,
                    UniqueMatches.AD_Org_ID
                ),
                0
            )
        END AS AccountingAmt

    FROM UniqueMatches UniqueMatches

    INNER JOIN SchemaCurrency SchemaCurrency ON
    (
        SchemaCurrency.AD_Client_ID =
        UniqueMatches.AD_Client_ID
    )
),

SummaryData AS
(
    SELECT
        COUNT(
            AccountingRows.C_Payment_ID
        ) AS TotalRecords,

        ROUND
        (
            CAST
            (
                COALESCE
                (
                    SUM(
                        AccountingRows.AccountingAmt
                    ),
                    0
                ) AS NUMERIC
            ),

            CAST
            (
                COALESCE
                (
                    MAX(
                        SchemaCurrency.SchemaPrecision
                    ),
                    2
                ) AS INTEGER
            )
        ) AS TotalAccountingAmt,

        COALESCE
        (
            SUM
            (
                CASE
                    WHEN AccountingRows.MatchConfidence =
                         'HIGH'
                    THEN 1
                    ELSE 0
                END
            ),
            0
        ) AS HighConfidenceCount

    FROM SchemaCurrency SchemaCurrency

    LEFT OUTER JOIN AccountingRows AccountingRows ON
    (
        AccountingRows.AD_Client_ID =
        SchemaCurrency.AD_Client_ID
    )
),

NumberedRows AS
(
    SELECT
        AccountingRows.*,

        ROW_NUMBER() OVER
        (
            ORDER BY
                CASE
                    WHEN AccountingRows.MatchConfidence =
                         'HIGH'
                    THEN 1
                    ELSE 2
                END,

                AccountingRows.MatchScore DESC,
                AccountingRows.DifferenceAmt ASC,
                AccountingRows.PaymentDateAcct DESC,
                AccountingRows.C_Payment_ID ASC
        ) AS ResultRowNo

    FROM AccountingRows AccountingRows
),

PagedRows AS
(
    SELECT
        NumberedRows.*

    FROM NumberedRows NumberedRows

    WHERE
        " + pagingCondition + @"
)

SELECT
    SchemaCurrency.SchemaCurrencyId,
    SchemaCurrency.SchemaCurrencyISO,
    SchemaCurrency.SchemaCurrencySymbol,
    SchemaCurrency.SchemaPrecision,

    SummaryData.TotalRecords,
    SummaryData.TotalAccountingAmt,
    SummaryData.HighConfidenceCount,

    PagedRows.C_Payment_ID,
    PagedRows.C_BPartner_ID,
    PagedRows.PaymentDocNo,
    PagedRows.PaymentDateAcct,
    PagedRows.PaymentCurrencyId,
    PagedRows.PaymentConversionTypeId,
    PagedRows.PaymentOriginalAmt,
    PagedRows.PaymentAllocatedAmt,
    PagedRows.PaymentOpenAmt,
    PagedRows.VendorName,
    PagedRows.PaymentMethod,
    PagedRows.ReferenceNo,
    PagedRows.BankName,
    PagedRows.AccountNo,
    PagedRows.PaymentCurrencyISO,
    PagedRows.PaymentCurrencySymbol,
    PagedRows.PaymentPrecision,

    PagedRows.C_Invoice_ID,
    PagedRows.C_InvoicePaySchedule_ID,
    PagedRows.InvoiceDocNo,
    PagedRows.InvoiceDate,
    PagedRows.DueDate,
    PagedRows.PaymentTerms,
    PagedRows.InvoiceCurrencyId,
    PagedRows.InvoiceCurrencyISO,
    PagedRows.InvoiceCurrencySymbol,
    PagedRows.InvoicePrecision,
    PagedRows.InvoiceOriginalAmt,
    PagedRows.InvoiceAllocatedAmt,
    PagedRows.InvoiceOpenAmt,
    PagedRows.InvoiceOpenPayAmt,

    PagedRows.ReadyAmt,
    PagedRows.AccountingAmt,
    PagedRows.MatchScore,
    PagedRows.MatchConfidence,
    PagedRows.ResultRowNo

FROM SchemaCurrency SchemaCurrency

CROSS JOIN SummaryData SummaryData

LEFT OUTER JOIN PagedRows PagedRows ON
(
    1 = 1
)

ORDER BY
    PagedRows.ResultRowNo";

            IDataReader reader = null;

            try
            {
                reader = DB.ExecuteReader(
                    sql,
                    null,
                    trx
                );

                while (
                    reader != null &&
                    reader.Read()
                )
                {
                    result.SchemaCurrencyId =
                        GetInt(
                            reader,
                            "SchemaCurrencyId"
                        );

                    result.SchemaCurrencyISOCode =
                        GetString(
                            reader,
                            "SchemaCurrencyISO"
                        );

                    result.SchemaCurrencySymbol =
                        GetString(
                            reader,
                            "SchemaCurrencySymbol"
                        );

                    if (
                        string.IsNullOrWhiteSpace(
                            result.SchemaCurrencySymbol
                        )
                    )
                    {
                        result.SchemaCurrencySymbol =
                            result.SchemaCurrencyISOCode;
                    }

                    result.SchemaStdPrecision =
                        NormalizePrecision(
                            GetInt(
                                reader,
                                "SchemaPrecision"
                            )
                        );

                    result.TotalRecords =
                        GetInt(
                            reader,
                            "TotalRecords"
                        );

                    result.TotalAccountingAmount =
                        GetDecimal(
                            reader,
                            "TotalAccountingAmt"
                        );

                    result.HighConfidenceCount =
                        GetInt(
                            reader,
                            "HighConfidenceCount"
                        );

                    int currentPaymentId =
                        GetInt(
                            reader,
                            "C_Payment_ID"
                        );

                    if (currentPaymentId <= 0)
                    {
                        continue;
                    }

                    string paymentCurrencyISO =
                        GetString(
                            reader,
                            "PaymentCurrencyISO"
                        );

                    string paymentCurrencySymbol =
                        GetString(
                            reader,
                            "PaymentCurrencySymbol"
                        );

                    if (
                        string.IsNullOrWhiteSpace(
                            paymentCurrencySymbol
                        )
                    )
                    {
                        paymentCurrencySymbol =
                            paymentCurrencyISO;
                    }

                    string invoiceCurrencyISO =
                        GetString(
                            reader,
                            "InvoiceCurrencyISO"
                        );

                    string invoiceCurrencySymbol =
                        GetString(
                            reader,
                            "InvoiceCurrencySymbol"
                        );

                    if (
                        string.IsNullOrWhiteSpace(
                            invoiceCurrencySymbol
                        )
                    )
                    {
                        invoiceCurrencySymbol =
                            invoiceCurrencyISO;
                    }

                    MatchQueryRow row =
                        new MatchQueryRow
                        {
                            PaymentId =
                                currentPaymentId,

                            InvoiceId =
                                GetInt(
                                    reader,
                                    "C_Invoice_ID"
                                ),

                            InvoicePayScheduleId =
                                GetInt(
                                    reader,
                                    "C_InvoicePaySchedule_ID"
                                ),

                            VendorId =
                                GetInt(
                                    reader,
                                    "C_BPartner_ID"
                                ),

                            VendorName =
                                GetString(
                                    reader,
                                    "VendorName"
                                ),

                            PaymentDocumentNo =
                                GetString(
                                    reader,
                                    "PaymentDocNo"
                                ),

                            PaymentDate =
                                GetNullableDateTime(
                                    reader,
                                    "PaymentDateAcct"
                                ),

                            PaymentCurrencyId =
                                GetInt(
                                    reader,
                                    "PaymentCurrencyId"
                                ),

                            PaymentConversionTypeId =
                                GetInt(
                                    reader,
                                    "PaymentConversionTypeId"
                                ),

                            PaymentOriginalAmount =
                                GetDecimal(
                                    reader,
                                    "PaymentOriginalAmt"
                                ),

                            PaymentAllocatedAmount =
                                GetDecimal(
                                    reader,
                                    "PaymentAllocatedAmt"
                                ),

                            PaymentOpenAmount =
                                GetDecimal(
                                    reader,
                                    "PaymentOpenAmt"
                                ),

                            PaymentMethod =
                                GetString(
                                    reader,
                                    "PaymentMethod"
                                ),

                            ReferenceNo =
                                GetString(
                                    reader,
                                    "ReferenceNo"
                                ),

                            BankName =
                                GetString(
                                    reader,
                                    "BankName"
                                ),

                            AccountNo =
                                GetString(
                                    reader,
                                    "AccountNo"
                                ),

                            PaymentCurrencyISOCode =
                                paymentCurrencyISO,

                            PaymentCurrencySymbol =
                                paymentCurrencySymbol,

                            PaymentPrecision =
                                NormalizePrecision(
                                    GetInt(
                                        reader,
                                        "PaymentPrecision"
                                    )
                                ),

                            InvoiceDocumentNo =
                                GetString(
                                    reader,
                                    "InvoiceDocNo"
                                ),

                            InvoiceDate =
                                GetNullableDateTime(
                                    reader,
                                    "InvoiceDate"
                                ),

                            DueDate =
                                GetNullableDateTime(
                                    reader,
                                    "DueDate"
                                ),

                            PaymentTerms =
                                GetString(
                                    reader,
                                    "PaymentTerms"
                                ),

                            InvoiceCurrencyId =
                                GetInt(
                                    reader,
                                    "InvoiceCurrencyId"
                                ),

                            InvoiceCurrencyISOCode =
                                invoiceCurrencyISO,

                            InvoiceCurrencySymbol =
                                invoiceCurrencySymbol,

                            InvoicePrecision =
                                NormalizePrecision(
                                    GetInt(
                                        reader,
                                        "InvoicePrecision"
                                    )
                                ),

                            InvoiceOriginalAmount =
                                GetDecimal(
                                    reader,
                                    "InvoiceOriginalAmt"
                                ),

                            InvoiceAllocatedAmount =
                                GetDecimal(
                                    reader,
                                    "InvoiceAllocatedAmt"
                                ),

                            InvoiceOpenAmount =
                                GetDecimal(
                                    reader,
                                    "InvoiceOpenAmt"
                                ),

                            InvoiceOpenAmountPaymentCurrency =
                                GetDecimal(
                                    reader,
                                    "InvoiceOpenPayAmt"
                                ),

                            ReadyAmount =
                                GetDecimal(
                                    reader,
                                    "ReadyAmt"
                                ),

                            AccountingAmount =
                                GetDecimal(
                                    reader,
                                    "AccountingAmt"
                                ),

                            Score =
                                GetInt(
                                    reader,
                                    "MatchScore"
                                ),

                            Confidence =
                                GetString(
                                    reader,
                                    "MatchConfidence"
                                )
                        };

                    result.Rows.Add(row);
                }
            }
            finally
            {
                CloseReader(reader);
            }

            return result;
        }

        private ApplyResult ApplyMatchAllocation(
            Ctx ctx,
            int paymentId,
            int invoiceId,
            int payScheduleId)
        {
            ApplyResult result =
                new ApplyResult
                {
                    Success = false,
                    DocumentNo = string.Empty,
                    Message = string.Empty
                };

            if (
                ctx == null ||
                paymentId <= 0 ||
                invoiceId <= 0 ||
                payScheduleId <= 0
            )
            {
                result.Message =
                    GetMsg(
                        ctx,
                        "FillMandatory",
                        "Mandatory values are missing"
                    );

                return result;
            }

            Trx trx =
                Trx.GetTrx(
                    Trx.CreateTrxName(
                        "VAS072AL"
                    )
                );

            try
            {
                MatchQueryResult queryResult =
                    ExecuteMatchQuery(
                        ctx,
                        paymentId,
                        invoiceId,
                        payScheduleId,
                        0,
                        0,
                        trx
                    );

                MatchQueryRow matchRow =
                    queryResult.Rows.FirstOrDefault(
                        row =>
                            row.PaymentId == paymentId &&
                            row.InvoiceId == invoiceId &&
                            row.InvoicePayScheduleId ==
                            payScheduleId
                    );

                if (matchRow == null)
                {
                    trx.Rollback();

                    result.Message =
                        GetMsg(
                            ctx,
                            "VIS_NoRecordFound",
                            "The selected match is no longer available"
                        );

                    return result;
                }

                MPayment payment =
                    new MPayment(
                        ctx,
                        paymentId,
                        trx
                    );

                MInvoice invoice =
                    new MInvoice(
                        ctx,
                        invoiceId,
                        trx
                    );

                if (
                    payment.Get_ID() <= 0 ||
                    invoice.Get_ID() <= 0 ||
                    payment.IsReceipt() ||
                    invoice.IsSOTrx() ||
                    !(
                        payment.GetDocStatus() == "CO" ||
                        payment.GetDocStatus() == "CL"
                    ) ||
                    !(
                        invoice.GetDocStatus() == "CO" ||
                        invoice.GetDocStatus() == "CL"
                    )
                )
                {
                    trx.Rollback();

                    result.Message =
                        GetMsg(
                            ctx,
                            "VIS_NoRecordFound",
                            "AP payment or purchase invoice was not found"
                        );

                    return result;
                }

                if (
                    payment.GetC_BPartner_ID() !=
                    invoice.GetC_BPartner_ID()
                )
                {
                    trx.Rollback();

                    result.Message =
                        GetMsg(
                            ctx,
                            "VAS_072_DifferentBusinessPartner",
                            "Payment and invoice belong to different vendors"
                        );

                    return result;
                }

                decimal availableAmount =
                    Math.Abs(
                        matchRow.PaymentOpenAmount
                    );

                decimal invoiceOpenAmount =
                    Math.Abs(
                        matchRow.InvoiceOpenAmountPaymentCurrency
                    );

                decimal appliedAmount =
                    Math.Min(
                        availableAmount,
                        invoiceOpenAmount
                    );

                if (appliedAmount <= 0)
                {
                    trx.Rollback();

                    result.Message =
                        GetMsg(
                            ctx,
                            "AmountIsZero",
                            "Available amount is zero"
                        );

                    return result;
                }

                DateTime? paymentDateAcct =
                    payment.GetDateAcct();

                MAllocationHdr allocation =
                    new MAllocationHdr(
                        ctx,
                        true,
                        paymentDateAcct,
                        payment.GetC_Currency_ID(),
                        ctx.GetContext(
                            "#AD_User_Name"
                        ),
                        trx
                    );

                allocation.SetAD_Org_ID(
                    payment.GetAD_Org_ID()
                );

                allocation.SetDateTrx(
                    paymentDateAcct
                );

                allocation.SetDateAcct(
                    paymentDateAcct
                );

                allocation.SetC_ConversionType_ID(
                    payment.GetC_ConversionType_ID()
                );

                if (!allocation.Save())
                {
                    trx.Rollback();

                    ValueNamePair error =
                        VLogger.RetrieveError();

                    result.Message =
                        GetMsg(
                            ctx,
                            "VIS_AllocationHdrNotSaved",
                            "Allocation header was not saved"
                        ) +
                        (
                            error != null
                                ? " :- " +
                                  error.GetName()
                                : string.Empty
                        );

                    return result;
                }

                decimal remainingInvoiceAmount =
                    invoiceOpenAmount -
                    appliedAmount;

                decimal allocationLineAmount =
                    -appliedAmount;

                decimal overUnderAmount =
                    -remainingInvoiceAmount;

                MAllocationLine allocationLine =
                    new MAllocationLine(
                        allocation,
                        allocationLineAmount,
                        Env.ZERO,
                        Env.ZERO,
                        overUnderAmount
                    );

                allocationLine.SetDocInfo(
                    payment.GetC_BPartner_ID(),
                    0,
                    invoiceId
                );

                allocationLine.SetPaymentInfo(
                    payment.GetC_Payment_ID(),
                    0
                );

                if (
                    Env.IsModuleInstalled(
                        "VA009_"
                    )
                )
                {
                    allocationLine
                        .SetC_InvoicePaySchedule_ID(
                            payScheduleId
                        );
                }

                allocationLine.SetDateTrx(
                    paymentDateAcct
                );

                if (!allocationLine.Save())
                {
                    trx.Rollback();

                    ValueNamePair error =
                        VLogger.RetrieveError();

                    result.Message =
                        GetMsg(
                            ctx,
                            "VIS_AllocLineNotCreated",
                            "Allocation line was not created"
                        ) +
                        (
                            error != null
                                ? " :- " +
                                  error.GetName()
                                : string.Empty
                        );

                    return result;
                }

                if (
                    !allocation.ProcessIt(
                        DocActionVariables.ACTION_COMPLETE
                    )
                )
                {
                    trx.Rollback();

                    result.Message =
                        GetMsg(
                            ctx,
                            "VAS_AllocationNotCompDueTo",
                            "Allocation could not be completed due to"
                        ) +
                        " " +
                        allocation.GetProcessMsg();

                    return result;
                }

                if (!allocation.Save())
                {
                    trx.Rollback();

                    ValueNamePair error =
                        VLogger.RetrieveError();

                    result.Message =
                        GetMsg(
                            ctx,
                            "VIS_AllocationHdrNotSaved",
                            "Completed allocation was not saved"
                        ) +
                        (
                            error != null
                                ? " :- " +
                                  error.GetName()
                                : string.Empty
                        );

                    return result;
                }

                if (
                    payment.TestAllocation() &&
                    !payment.Save()
                )
                {
                    trx.Rollback();

                    ValueNamePair error =
                        VLogger.RetrieveError();

                    result.Message =
                        GetMsg(
                            ctx,
                            "PaymentNotCreated",
                            "Payment allocation status was not saved"
                        ) +
                        (
                            error != null
                                ? " :- " +
                                  error.GetName()
                                : string.Empty
                        );

                    return result;
                }

                trx.Commit();

                result.Success = true;

                result.DocumentNo =
                    allocation.GetDocumentNo();

                result.Message =
                    GetMsg(
                        ctx,
                        "AllocationIsCreated",
                        "Allocation is created"
                    ) +
                    " " +
                    allocation.GetDocumentNo();

                return result;
            }
            catch (Exception ex)
            {
                if (trx != null)
                {
                    trx.Rollback();
                }

                result.Message =
                    GetMsg(
                        ctx,
                        "VAS_072_ApplyError",
                        "Could not complete allocation"
                    ) +
                    " - " +
                    ex.Message;

                return result;
            }
            finally
            {
                if (trx != null)
                {
                    trx.Close();
                }
            }
        }

        private string GetCurrentDateSql()
        {
            if (DB.IsOracle())
            {
                return "TRUNC(CURRENT_DATE)";
            }

            return "CURRENT_DATE";
        }

        private string GetDateAddDaysSql(
            string dateExpression,
            int days)
        {
            return "(" +
                dateExpression +
                " + " +
                days.ToString(
                    CultureInfo.InvariantCulture
                ) +
                ")";
        }

        private string GetDateDifferenceDaysSql(
            string firstDateExpression,
            string secondDateExpression)
        {
            if (DB.IsOracle())
            {
                return @"
ABS
(
    TRUNC(" + firstDateExpression + @") -
    TRUNC(" + secondDateExpression + @")
)";
            }

            return @"
ABS
(
    CAST(
        " + firstDateExpression + @"
        AS DATE
    ) -
    CAST(
        " + secondDateExpression + @"
        AS DATE
    )
)";
        }

        private string ToSqlInteger(
            int value)
        {
            return value.ToString(
                CultureInfo.InvariantCulture
            );
        }

        private Ctx GetContext()
        {
            if (Session["ctx"] == null)
            {
                return null;
            }

            return Session["ctx"] as Ctx;
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

        private bool IsAmountMatch(
            decimal paymentOpenAmount,
            decimal invoiceOpenAmount)
        {
            decimal difference =
                Math.Abs(
                    paymentOpenAmount -
                    invoiceOpenAmount
                );

            if (difference <= AmountTolerance)
            {
                return true;
            }

            if (invoiceOpenAmount == 0)
            {
                return false;
            }

            decimal differencePercentage =
                difference *
                100 /
                Math.Abs(
                    invoiceOpenAmount
                );

            return differencePercentage <=
                   HighPercentageThreshold;
        }

        private bool IsReferenceMatch(
            string referenceNo,
            string invoiceDocumentNo)
        {
            return
                !string.IsNullOrWhiteSpace(
                    referenceNo
                ) &&
                !string.IsNullOrWhiteSpace(
                    invoiceDocumentNo
                ) &&
                referenceNo.IndexOf(
                    invoiceDocumentNo,
                    StringComparison.OrdinalIgnoreCase
                ) >= 0;
        }

        private bool IsDateMatch(
            DateTime? paymentDate,
            DateTime? dueDate)
        {
            if (
                !paymentDate.HasValue ||
                !dueDate.HasValue
            )
            {
                return false;
            }

            return Math.Abs(
                (
                    paymentDate.Value.Date -
                    dueDate.Value.Date
                ).TotalDays
            ) <= DateWindowDays;
        }

        private string FormatDate(
            DateTime? value)
        {
            return value.HasValue
                ? value.Value.ToString(
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
                string.IsNullOrWhiteSpace(msg) ||
                msg == key ||
                msg == "[" + key + "]"
            )
            {
                return fallback;
            }

            return msg;
        }

        private int GetInt(
            IDataRecord record,
            string columnName)
        {
            object value =
                record[columnName];

            return
                value == null ||
                value == DBNull.Value
                    ? 0
                    : Util.GetValueOfInt(
                        value
                    );
        }

        private decimal GetDecimal(
            IDataRecord record,
            string columnName)
        {
            object value =
                record[columnName];

            return
                value == null ||
                value == DBNull.Value
                    ? 0
                    : Util.GetValueOfDecimal(
                        value
                    );
        }

        private string GetString(
            IDataRecord record,
            string columnName)
        {
            object value =
                record[columnName];

            return
                value == null ||
                value == DBNull.Value
                    ? string.Empty
                    : Util.GetValueOfString(
                        value
                    );
        }

        private DateTime? GetNullableDateTime(
            IDataRecord record,
            string columnName)
        {
            object value =
                record[columnName];

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

        private class MatchQueryResult
        {
            public MatchQueryResult()
            {
                Rows =
                    new List<MatchQueryRow>();

                SchemaCurrencyISOCode =
                    string.Empty;

                SchemaCurrencySymbol =
                    string.Empty;

                SchemaStdPrecision = 2;
            }

            public int SchemaCurrencyId
            {
                get;
                set;
            }

            public string SchemaCurrencyISOCode
            {
                get;
                set;
            }

            public string SchemaCurrencySymbol
            {
                get;
                set;
            }

            public int SchemaStdPrecision
            {
                get;
                set;
            }

            public int TotalRecords
            {
                get;
                set;
            }

            public decimal TotalAccountingAmount
            {
                get;
                set;
            }

            public int HighConfidenceCount
            {
                get;
                set;
            }

            public List<MatchQueryRow> Rows
            {
                get;
                set;
            }
        }

        private class MatchQueryRow
        {
            public int PaymentId { get; set; }

            public int InvoiceId { get; set; }

            public int InvoicePayScheduleId { get; set; }

            public int VendorId { get; set; }

            public string VendorName { get; set; }

            public string PaymentDocumentNo { get; set; }

            public DateTime? PaymentDate { get; set; }

            public int PaymentCurrencyId { get; set; }

            public int PaymentConversionTypeId { get; set; }

            public decimal PaymentOriginalAmount { get; set; }

            public decimal PaymentAllocatedAmount { get; set; }

            public decimal PaymentOpenAmount { get; set; }

            public string PaymentMethod { get; set; }

            public string ReferenceNo { get; set; }

            public string BankName { get; set; }

            public string AccountNo { get; set; }

            public string PaymentCurrencyISOCode { get; set; }

            public string PaymentCurrencySymbol { get; set; }

            public int PaymentPrecision { get; set; }

            public string InvoiceDocumentNo { get; set; }

            public DateTime? InvoiceDate { get; set; }

            public DateTime? DueDate { get; set; }

            public string PaymentTerms { get; set; }

            public int InvoiceCurrencyId { get; set; }

            public string InvoiceCurrencyISOCode { get; set; }

            public string InvoiceCurrencySymbol { get; set; }

            public int InvoicePrecision { get; set; }

            public decimal InvoiceOriginalAmount { get; set; }

            public decimal InvoiceAllocatedAmount { get; set; }

            public decimal InvoiceOpenAmount { get; set; }

            public decimal InvoiceOpenAmountPaymentCurrency { get; set; }

            public decimal ReadyAmount { get; set; }

            public decimal AccountingAmount { get; set; }

            public string Confidence { get; set; }

            public int Score { get; set; }
        }

        private class ApplyResult
        {
            public bool Success
            {
                get;
                set;
            }

            public string DocumentNo
            {
                get;
                set;
            }

            public string Message
            {
                get;
                set;
            }
        }
    }
}
