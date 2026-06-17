
/******************************************************
 * Module Name    : VAS
 * Purpose        : AP Payment Match Suggestions Widget
 * Chronological  : Development
 * Created Date   : 2026-06-17
 * Created by     : VAI145
 ******************************************************/

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
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
            Ctx ctx = Session["ctx"] as Ctx;

            if (ctx == null)
            {
                return Json(
                    JsonConvert.SerializeObject(new
                    {
                        success = false,
                        error = "Session Expired",
                        errorText = "Session Expired",
                        hasData = false
                    }),
                    JsonRequestBehavior.AllowGet
                );
            }

            try
            {
                pageNo = Math.Max(1, pageNo);

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
                    error = "",
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
                        })
                };

                return Json(
                    JsonConvert.SerializeObject(response),
                    JsonRequestBehavior.AllowGet
                );
            }
            catch (Exception)
            {
                string errorMessage =
                    GetMsg(
                        ctx,
                        "VAS_072_LoadError",
                        "Could not load AP payment match suggestions"
                    );

                return Json(
                    JsonConvert.SerializeObject(new
                    {
                        success = false,
                        error = errorMessage,
                        errorText = errorMessage,
                        hasData = false
                    }),
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
            Ctx ctx = Session["ctx"] as Ctx;

            if (ctx == null)
            {
                return Json(
                    JsonConvert.SerializeObject(new
                    {
                        success = false,
                        error = "Session Expired",
                        errorText = "Session Expired"
                    }),
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
                    JsonConvert.SerializeObject(new
                    {
                        success = false,
                        error = GetMsg(
                            ctx,
                            "FillMandatory",
                            "Mandatory values are missing"
                        )
                    }),
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
                        JsonConvert.SerializeObject(new
                        {
                            success = false,
                            error = GetMsg(
                                ctx,
                                "VIS_NoRecordFound",
                                "No record found"
                            )
                        }),
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
                    JsonConvert.SerializeObject(new
                    {
                        success = true,
                        error = "",
                        detail = detail
                    }),
                    JsonRequestBehavior.AllowGet
                );
            }
            catch (Exception)
            {
                return Json(
                    JsonConvert.SerializeObject(new
                    {
                        success = false,
                        error = GetMsg(
                            ctx,
                            "VAS_072_LoadDetailError",
                            "Could not load match details"
                        )
                    }),
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
            Ctx ctx = Session["ctx"] as Ctx;

            if (ctx == null)
            {
                return Json(
                    JsonConvert.SerializeObject(new
                    {
                        success = false,
                        error = "Session Expired",
                        message = "Session Expired"
                    })
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
                JsonConvert.SerializeObject(new
                {
                    success =
                        result.Success,

                    documentNo =
                        result.DocumentNo,

                    message =
                        result.Message
                })
            );
        }

        [HttpPost]
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult ApplyHighConfidence()
        {
            Ctx ctx = Session["ctx"] as Ctx;

            if (ctx == null)
            {
                return Json(
                    JsonConvert.SerializeObject(new
                    {
                        success = false,
                        error = "Session Expired",
                        message = "Session Expired"
                    })
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
                        !string.IsNullOrEmpty(
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
                    JsonConvert.SerializeObject(new
                    {
                        success = success,
                        appliedCount =
                            appliedCount,
                        failedCount =
                            errors.Count,
                        message = message
                    })
                );
            }
            catch (Exception)
            {
                string errorMessage =
                    GetMsg(
                        ctx,
                        "VAS_072_ApplyError",
                        "Could not complete allocation"
                    );

                return Json(
                    JsonConvert.SerializeObject(new
                    {
                        success = false,
                        error = errorMessage,
                        message = errorMessage
                    })
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

            /*
             * MRole is applied independently to each primary
             * physical table.
             *
             * It is not applied to CTE aliases or to the final query.
             */
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
WHERE Payment.IsActive='Y'
AND Payment.Processed='Y'
AND Payment.IsReceipt='N'
AND Payment.DocStatus IN ('CO','CL')
AND Payment.C_BPartner_ID IS NOT NULL
AND Payment.DateAcct>=CURRENT_DATE-" +
                PaymentWindowDays +
                @"
AND Payment.DateAcct<CURRENT_DATE+1";

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
WHERE Invoice.IsActive='Y'
AND Invoice.Processed='Y'
AND Invoice.IsSOTrx='N'
AND Invoice.DocStatus IN ('CO','CL')
AND COALESCE(Invoice.IsPaid,'N')='N'
AND COALESCE(Invoice.IsReturnTrx,'N')='N'";

            invoiceAccessSql =
                MRole.GetDefault(ctx)
                    .AddAccessSQL(
                        invoiceAccessSql,
                        "Invoice",
                        MRole.SQL_FULLYQUALIFIED,
                        MRole.SQL_RO
                    );

            string parameterSource =
                DB.IsPostgreSQL()
                    ? ""
                    : " FROM DUAL";

            string sql = @"
WITH QueryParameters AS
(
SELECT
@AD_Client_ID AS AD_Client_ID,
@C_Payment_ID AS C_Payment_ID,
@C_Invoice_ID AS C_Invoice_ID,
@C_InvoicePaySchedule_ID AS C_InvoicePaySchedule_ID,
@OffsetRows AS OffsetRows,
@PageSize AS PageSize" +
                parameterSource +
                @"
),
SchemaCurrency AS
(
SELECT
ClientInfo.AD_Client_ID,
AcctSchema.C_Currency_ID AS SchemaCurrencyId,
Currency.ISO_Code AS SchemaCurrencyISO,
CASE
WHEN Currency.CurSymbol IS NOT NULL
THEN Currency.CurSymbol
ELSE Currency.ISO_Code
END AS SchemaCurrencySymbol,
Currency.StdPrecision AS SchemaPrecision
FROM AD_ClientInfo ClientInfo
INNER JOIN C_AcctSchema AcctSchema ON
(
AcctSchema.C_AcctSchema_ID=
ClientInfo.C_AcctSchema1_ID
)
INNER JOIN C_Currency Currency ON
(
Currency.C_Currency_ID=
AcctSchema.C_Currency_ID
)
CROSS JOIN QueryParameters QueryParameters
WHERE ClientInfo.IsActive='Y'
AND ClientInfo.AD_Client_ID=
QueryParameters.AD_Client_ID
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
AccessiblePayments.DocumentNo AS PaymentDocNo,
AccessiblePayments.DateAcct AS PaymentDateAcct,
AccessiblePayments.C_Currency_ID AS PaymentCurrencyId,
COALESCE(
AccessiblePayments.C_ConversionType_ID,
0
) AS PaymentConversionTypeId,
ABS(
COALESCE(
AccessiblePayments.PayAmt,
0
)
) AS PaymentOriginalAmt,
BusinessPartner.Name AS VendorName,
PaymentMethod.VA009_Name AS PaymentMethod,
COALESCE(
AccessiblePayments.TrxNo,
AccessiblePayments.CheckNo
) AS ReferenceNo,
Bank.Name AS BankName,
BankAccount.AccountNo AS AccountNo,
PaymentCurrency.ISO_Code AS PaymentCurrencyISO,
CASE
WHEN PaymentCurrency.CurSymbol IS NOT NULL
THEN PaymentCurrency.CurSymbol
ELSE PaymentCurrency.ISO_Code
END AS PaymentCurrencySymbol,
PaymentCurrency.StdPrecision AS PaymentPrecision
FROM AccessiblePayments AccessiblePayments
INNER JOIN C_BPartner BusinessPartner ON
(
BusinessPartner.C_BPartner_ID=
AccessiblePayments.C_BPartner_ID
)
INNER JOIN C_Currency PaymentCurrency ON
(
PaymentCurrency.C_Currency_ID=
AccessiblePayments.C_Currency_ID
)
LEFT OUTER JOIN VA009_PaymentMethod PaymentMethod ON
(
PaymentMethod.VA009_PaymentMethod_ID=
AccessiblePayments.VA009_PaymentMethod_ID
)
LEFT OUTER JOIN C_BankAccount BankAccount ON
(
BankAccount.C_BankAccount_ID=
AccessiblePayments.C_BankAccount_ID
)
LEFT OUTER JOIN C_Bank Bank ON
(
Bank.C_Bank_ID=
BankAccount.C_Bank_ID
)
CROSS JOIN QueryParameters QueryParameters
WHERE AccessiblePayments.AD_Client_ID=
QueryParameters.AD_Client_ID
AND
(
QueryParameters.C_Payment_ID=0
OR AccessiblePayments.C_Payment_ID=
QueryParameters.C_Payment_ID
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
WHEN PaymentRecords.PaymentOriginalAmt>
ABS(
COALESCE(
ALLOCPAYMENTAVAILABLE(
PaymentRecords.C_Payment_ID
),
0
)
)
THEN PaymentRecords.PaymentOriginalAmt-
ABS(
COALESCE(
ALLOCPAYMENTAVAILABLE(
PaymentRecords.C_Payment_ID
),
0
)
)
ELSE 0
END AS PaymentAllocatedAmt,
ABS(
COALESCE(
ALLOCPAYMENTAVAILABLE(
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
WHERE ABS(
COALESCE(
ALLOCPAYMENTAVAILABLE(
PaymentRecords.C_Payment_ID
),
0
)
)>0
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
AccessibleInvoices.DocumentNo AS InvoiceDocNo,
AccessibleInvoices.DateInvoiced AS InvoiceDate,
AccessibleInvoices.DateAcct AS InvoiceDateAcct,
AccessibleInvoices.C_Currency_ID AS InvoiceCurrencyId,
AccessibleInvoices.GrandTotal,
AccessibleInvoices.C_PaymentTerm_ID,
InvoicePaySchedule.C_InvoicePaySchedule_ID,
InvoicePaySchedule.DueDate,
ABS(
COALESCE(
InvoicePaySchedule.DueAmt,
0
)
) AS InvoiceOriginalAmt,
ABS(
COALESCE(
InvoiceOpen(
AccessibleInvoices.C_Invoice_ID,
InvoicePaySchedule.C_InvoicePaySchedule_ID
),
0
)
) AS InvoiceOpenAmt,
InvoiceCurrency.ISO_Code AS InvoiceCurrencyISO,
CASE
WHEN InvoiceCurrency.CurSymbol IS NOT NULL
THEN InvoiceCurrency.CurSymbol
ELSE InvoiceCurrency.ISO_Code
END AS InvoiceCurrencySymbol,
InvoiceCurrency.StdPrecision AS InvoicePrecision,
PaymentTerm.Name AS PaymentTerms
FROM AccessibleInvoices AccessibleInvoices
INNER JOIN C_InvoicePaySchedule InvoicePaySchedule ON
(
InvoicePaySchedule.C_Invoice_ID=
AccessibleInvoices.C_Invoice_ID
)
INNER JOIN C_Currency InvoiceCurrency ON
(
InvoiceCurrency.C_Currency_ID=
AccessibleInvoices.C_Currency_ID
)
LEFT OUTER JOIN C_PaymentTerm PaymentTerm ON
(
PaymentTerm.C_PaymentTerm_ID=
AccessibleInvoices.C_PaymentTerm_ID
)
CROSS JOIN QueryParameters QueryParameters
WHERE AccessibleInvoices.AD_Client_ID=
QueryParameters.AD_Client_ID
AND InvoicePaySchedule.IsActive='Y'
AND COALESCE(
InvoicePaySchedule.IsValid,
'Y'
)='Y'
AND COALESCE(
InvoicePaySchedule.VA009_IsPaid,
'N'
)='N'
AND
(
QueryParameters.C_Invoice_ID=0
OR AccessibleInvoices.C_Invoice_ID=
QueryParameters.C_Invoice_ID
)
AND
(
QueryParameters.C_InvoicePaySchedule_ID=0
OR InvoicePaySchedule.C_InvoicePaySchedule_ID=
QueryParameters.C_InvoicePaySchedule_ID
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
WHEN InvoiceRecords.InvoiceOriginalAmt>
InvoiceRecords.InvoiceOpenAmt
THEN InvoiceRecords.InvoiceOriginalAmt-
InvoiceRecords.InvoiceOpenAmt
ELSE 0
END AS InvoiceAllocatedAmt,
InvoiceRecords.InvoiceOpenAmt,
InvoiceRecords.InvoiceCurrencyISO,
InvoiceRecords.InvoiceCurrencySymbol,
InvoiceRecords.InvoicePrecision,
InvoiceRecords.PaymentTerms
FROM InvoiceRecords InvoiceRecords
WHERE InvoiceRecords.InvoiceOpenAmt>0
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
WHEN InvoiceAmounts.InvoiceCurrencyId=
PaymentAmounts.PaymentCurrencyId
THEN InvoiceAmounts.InvoiceOpenAmt
ELSE COALESCE(
CurrencyConvert(
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
InvoiceAmounts.AD_Client_ID=
PaymentAmounts.AD_Client_ID
AND InvoiceAmounts.C_BPartner_ID=
PaymentAmounts.C_BPartner_ID
AND
(
InvoiceAmounts.AD_Org_ID=
PaymentAmounts.AD_Org_ID
OR InvoiceAmounts.AD_Org_ID=0
OR PaymentAmounts.AD_Org_ID=0
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
ABS(
CandidateRows.PaymentOpenAmt-
CandidateRows.InvoiceOpenPayAmt
) AS DifferenceAmt,
CASE
WHEN CandidateRows.InvoiceOpenPayAmt=0
THEN 100
ELSE
ABS(
CandidateRows.PaymentOpenAmt-
CandidateRows.InvoiceOpenPayAmt
)*100/
ABS(
CandidateRows.InvoiceOpenPayAmt
)
END AS DifferencePct,
CASE
WHEN CandidateRows.DueDate IS NULL
THEN 999999
ELSE ABS(
CAST(
CandidateRows.PaymentDateAcct AS DATE
)-
CAST(
CandidateRows.DueDate AS DATE
)
)
END AS DateGapDays
FROM CandidateRows CandidateRows
WHERE CandidateRows.PaymentOpenAmt>0
AND CandidateRows.InvoiceOpenPayAmt>0
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
10+
CASE
WHEN CandidateMetrics.DifferenceAmt<=" +
                AmountTolerance.ToString(
                    CultureInfo.InvariantCulture
                ) +
                @"
THEN 70
WHEN CandidateMetrics.DifferencePct<=" +
                HighPercentageThreshold.ToString(
                    CultureInfo.InvariantCulture
                ) +
                @"
THEN 60
WHEN CandidateMetrics.DifferencePct<=" +
                ReviewPercentageThreshold.ToString(
                    CultureInfo.InvariantCulture
                ) +
                @"
THEN 35
ELSE 10
END+
CASE
WHEN CandidateMetrics.DateGapDays<=7
THEN 20
WHEN CandidateMetrics.DateGapDays<=" +
                DateWindowDays +
                @"
THEN 10
ELSE 0
END AS MatchScore
FROM CandidateMetrics CandidateMetrics
),
ClassifiedCandidates AS
(
SELECT
CandidateScores.AD_Client_ID,
CandidateScores.AD_Org_ID,
CandidateScores.C_Payment_ID,
CandidateScores.C_BPartner_ID,
CandidateScores.PaymentDocNo,
CandidateScores.PaymentDateAcct,
CandidateScores.PaymentCurrencyId,
CandidateScores.PaymentConversionTypeId,
CandidateScores.PaymentOriginalAmt,
CandidateScores.PaymentAllocatedAmt,
CandidateScores.PaymentOpenAmt,
CandidateScores.VendorName,
CandidateScores.PaymentMethod,
CandidateScores.ReferenceNo,
CandidateScores.BankName,
CandidateScores.AccountNo,
CandidateScores.PaymentCurrencyISO,
CandidateScores.PaymentCurrencySymbol,
CandidateScores.PaymentPrecision,
CandidateScores.C_Invoice_ID,
CandidateScores.C_InvoicePaySchedule_ID,
CandidateScores.InvoiceDocNo,
CandidateScores.InvoiceDate,
CandidateScores.DueDate,
CandidateScores.PaymentTerms,
CandidateScores.InvoiceCurrencyId,
CandidateScores.InvoiceCurrencyISO,
CandidateScores.InvoiceCurrencySymbol,
CandidateScores.InvoicePrecision,
CandidateScores.InvoiceOriginalAmt,
CandidateScores.InvoiceAllocatedAmt,
CandidateScores.InvoiceOpenAmt,
CandidateScores.InvoiceOpenPayAmt,
CandidateScores.DifferenceAmt,
CandidateScores.DifferencePct,
CandidateScores.DateGapDays,
CandidateScores.MatchScore,
CASE
WHEN CandidateScores.MatchScore>=80
THEN 'HIGH'
WHEN CandidateScores.MatchScore>=55
THEN 'REVIEW'
ELSE 'LOW'
END AS MatchConfidence
FROM CandidateScores CandidateScores
),
PaymentRanked AS
(
SELECT
ClassifiedCandidates.AD_Client_ID,
ClassifiedCandidates.AD_Org_ID,
ClassifiedCandidates.C_Payment_ID,
ClassifiedCandidates.C_BPartner_ID,
ClassifiedCandidates.PaymentDocNo,
ClassifiedCandidates.PaymentDateAcct,
ClassifiedCandidates.PaymentCurrencyId,
ClassifiedCandidates.PaymentConversionTypeId,
ClassifiedCandidates.PaymentOriginalAmt,
ClassifiedCandidates.PaymentAllocatedAmt,
ClassifiedCandidates.PaymentOpenAmt,
ClassifiedCandidates.VendorName,
ClassifiedCandidates.PaymentMethod,
ClassifiedCandidates.ReferenceNo,
ClassifiedCandidates.BankName,
ClassifiedCandidates.AccountNo,
ClassifiedCandidates.PaymentCurrencyISO,
ClassifiedCandidates.PaymentCurrencySymbol,
ClassifiedCandidates.PaymentPrecision,
ClassifiedCandidates.C_Invoice_ID,
ClassifiedCandidates.C_InvoicePaySchedule_ID,
ClassifiedCandidates.InvoiceDocNo,
ClassifiedCandidates.InvoiceDate,
ClassifiedCandidates.DueDate,
ClassifiedCandidates.PaymentTerms,
ClassifiedCandidates.InvoiceCurrencyId,
ClassifiedCandidates.InvoiceCurrencyISO,
ClassifiedCandidates.InvoiceCurrencySymbol,
ClassifiedCandidates.InvoicePrecision,
ClassifiedCandidates.InvoiceOriginalAmt,
ClassifiedCandidates.InvoiceAllocatedAmt,
ClassifiedCandidates.InvoiceOpenAmt,
ClassifiedCandidates.InvoiceOpenPayAmt,
ClassifiedCandidates.DifferenceAmt,
ClassifiedCandidates.DifferencePct,
ClassifiedCandidates.DateGapDays,
ClassifiedCandidates.MatchScore,
ClassifiedCandidates.MatchConfidence,
ROW_NUMBER() OVER
(
PARTITION BY ClassifiedCandidates.C_Payment_ID
ORDER BY
ClassifiedCandidates.MatchScore DESC,
ClassifiedCandidates.DifferenceAmt,
ClassifiedCandidates.DateGapDays,
ClassifiedCandidates.DueDate,
ClassifiedCandidates.InvoiceDocNo,
ClassifiedCandidates.C_InvoicePaySchedule_ID
) AS PaymentRank
FROM ClassifiedCandidates ClassifiedCandidates
),
BestPerPayment AS
(
SELECT
PaymentRanked.AD_Client_ID,
PaymentRanked.AD_Org_ID,
PaymentRanked.C_Payment_ID,
PaymentRanked.C_BPartner_ID,
PaymentRanked.PaymentDocNo,
PaymentRanked.PaymentDateAcct,
PaymentRanked.PaymentCurrencyId,
PaymentRanked.PaymentConversionTypeId,
PaymentRanked.PaymentOriginalAmt,
PaymentRanked.PaymentAllocatedAmt,
PaymentRanked.PaymentOpenAmt,
PaymentRanked.VendorName,
PaymentRanked.PaymentMethod,
PaymentRanked.ReferenceNo,
PaymentRanked.BankName,
PaymentRanked.AccountNo,
PaymentRanked.PaymentCurrencyISO,
PaymentRanked.PaymentCurrencySymbol,
PaymentRanked.PaymentPrecision,
PaymentRanked.C_Invoice_ID,
PaymentRanked.C_InvoicePaySchedule_ID,
PaymentRanked.InvoiceDocNo,
PaymentRanked.InvoiceDate,
PaymentRanked.DueDate,
PaymentRanked.PaymentTerms,
PaymentRanked.InvoiceCurrencyId,
PaymentRanked.InvoiceCurrencyISO,
PaymentRanked.InvoiceCurrencySymbol,
PaymentRanked.InvoicePrecision,
PaymentRanked.InvoiceOriginalAmt,
PaymentRanked.InvoiceAllocatedAmt,
PaymentRanked.InvoiceOpenAmt,
PaymentRanked.InvoiceOpenPayAmt,
PaymentRanked.DifferenceAmt,
PaymentRanked.DifferencePct,
PaymentRanked.DateGapDays,
PaymentRanked.MatchScore,
PaymentRanked.MatchConfidence
FROM PaymentRanked PaymentRanked
WHERE PaymentRanked.PaymentRank=1
AND PaymentRanked.MatchConfidence IN ('HIGH','REVIEW')
),
ScheduleRanked AS
(
SELECT
BestPerPayment.AD_Client_ID,
BestPerPayment.AD_Org_ID,
BestPerPayment.C_Payment_ID,
BestPerPayment.C_BPartner_ID,
BestPerPayment.PaymentDocNo,
BestPerPayment.PaymentDateAcct,
BestPerPayment.PaymentCurrencyId,
BestPerPayment.PaymentConversionTypeId,
BestPerPayment.PaymentOriginalAmt,
BestPerPayment.PaymentAllocatedAmt,
BestPerPayment.PaymentOpenAmt,
BestPerPayment.VendorName,
BestPerPayment.PaymentMethod,
BestPerPayment.ReferenceNo,
BestPerPayment.BankName,
BestPerPayment.AccountNo,
BestPerPayment.PaymentCurrencyISO,
BestPerPayment.PaymentCurrencySymbol,
BestPerPayment.PaymentPrecision,
BestPerPayment.C_Invoice_ID,
BestPerPayment.C_InvoicePaySchedule_ID,
BestPerPayment.InvoiceDocNo,
BestPerPayment.InvoiceDate,
BestPerPayment.DueDate,
BestPerPayment.PaymentTerms,
BestPerPayment.InvoiceCurrencyId,
BestPerPayment.InvoiceCurrencyISO,
BestPerPayment.InvoiceCurrencySymbol,
BestPerPayment.InvoicePrecision,
BestPerPayment.InvoiceOriginalAmt,
BestPerPayment.InvoiceAllocatedAmt,
BestPerPayment.InvoiceOpenAmt,
BestPerPayment.InvoiceOpenPayAmt,
BestPerPayment.DifferenceAmt,
BestPerPayment.DifferencePct,
BestPerPayment.DateGapDays,
BestPerPayment.MatchScore,
BestPerPayment.MatchConfidence,
ROW_NUMBER() OVER
(
PARTITION BY BestPerPayment.C_InvoicePaySchedule_ID
ORDER BY
BestPerPayment.MatchScore DESC,
BestPerPayment.DifferenceAmt,
BestPerPayment.DateGapDays,
BestPerPayment.PaymentDateAcct,
BestPerPayment.C_Payment_ID
) AS ScheduleRank
FROM BestPerPayment BestPerPayment
),
UniqueMatches AS
(
SELECT
ScheduleRanked.AD_Client_ID,
ScheduleRanked.AD_Org_ID,
ScheduleRanked.C_Payment_ID,
ScheduleRanked.C_BPartner_ID,
ScheduleRanked.PaymentDocNo,
ScheduleRanked.PaymentDateAcct,
ScheduleRanked.PaymentCurrencyId,
ScheduleRanked.PaymentConversionTypeId,
ScheduleRanked.PaymentOriginalAmt,
ScheduleRanked.PaymentAllocatedAmt,
ScheduleRanked.PaymentOpenAmt,
ScheduleRanked.VendorName,
ScheduleRanked.PaymentMethod,
ScheduleRanked.ReferenceNo,
ScheduleRanked.BankName,
ScheduleRanked.AccountNo,
ScheduleRanked.PaymentCurrencyISO,
ScheduleRanked.PaymentCurrencySymbol,
ScheduleRanked.PaymentPrecision,
ScheduleRanked.C_Invoice_ID,
ScheduleRanked.C_InvoicePaySchedule_ID,
ScheduleRanked.InvoiceDocNo,
ScheduleRanked.InvoiceDate,
ScheduleRanked.DueDate,
ScheduleRanked.PaymentTerms,
ScheduleRanked.InvoiceCurrencyId,
ScheduleRanked.InvoiceCurrencyISO,
ScheduleRanked.InvoiceCurrencySymbol,
ScheduleRanked.InvoicePrecision,
ScheduleRanked.InvoiceOriginalAmt,
ScheduleRanked.InvoiceAllocatedAmt,
ScheduleRanked.InvoiceOpenAmt,
ScheduleRanked.InvoiceOpenPayAmt,
ScheduleRanked.DifferenceAmt,
ScheduleRanked.DifferencePct,
ScheduleRanked.DateGapDays,
ScheduleRanked.MatchScore,
ScheduleRanked.MatchConfidence,
CASE
WHEN ScheduleRanked.PaymentOpenAmt<
ScheduleRanked.InvoiceOpenPayAmt
THEN ScheduleRanked.PaymentOpenAmt
ELSE ScheduleRanked.InvoiceOpenPayAmt
END AS ReadyAmt
FROM ScheduleRanked ScheduleRanked
WHERE ScheduleRanked.ScheduleRank=1
),
AccountingRows AS
(
SELECT
UniqueMatches.AD_Client_ID,
UniqueMatches.AD_Org_ID,
UniqueMatches.C_Payment_ID,
UniqueMatches.C_BPartner_ID,
UniqueMatches.PaymentDocNo,
UniqueMatches.PaymentDateAcct,
UniqueMatches.PaymentCurrencyId,
UniqueMatches.PaymentConversionTypeId,
UniqueMatches.PaymentOriginalAmt,
UniqueMatches.PaymentAllocatedAmt,
UniqueMatches.PaymentOpenAmt,
UniqueMatches.VendorName,
UniqueMatches.PaymentMethod,
UniqueMatches.ReferenceNo,
UniqueMatches.BankName,
UniqueMatches.AccountNo,
UniqueMatches.PaymentCurrencyISO,
UniqueMatches.PaymentCurrencySymbol,
UniqueMatches.PaymentPrecision,
UniqueMatches.C_Invoice_ID,
UniqueMatches.C_InvoicePaySchedule_ID,
UniqueMatches.InvoiceDocNo,
UniqueMatches.InvoiceDate,
UniqueMatches.DueDate,
UniqueMatches.PaymentTerms,
UniqueMatches.InvoiceCurrencyId,
UniqueMatches.InvoiceCurrencyISO,
UniqueMatches.InvoiceCurrencySymbol,
UniqueMatches.InvoicePrecision,
UniqueMatches.InvoiceOriginalAmt,
UniqueMatches.InvoiceAllocatedAmt,
UniqueMatches.InvoiceOpenAmt,
UniqueMatches.InvoiceOpenPayAmt,
UniqueMatches.DifferenceAmt,
UniqueMatches.DifferencePct,
UniqueMatches.DateGapDays,
UniqueMatches.MatchScore,
UniqueMatches.MatchConfidence,
UniqueMatches.ReadyAmt,
CASE
WHEN UniqueMatches.PaymentCurrencyId=
SchemaCurrency.SchemaCurrencyId
THEN UniqueMatches.ReadyAmt
ELSE COALESCE(
CurrencyConvert(
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
SchemaCurrency.AD_Client_ID=
UniqueMatches.AD_Client_ID
)
),
SummaryData AS
(
SELECT
COUNT(*) AS TotalRecords,
ROUND(
COALESCE(
SUM(
AccountingRows.AccountingAmt
),
0
),
COALESCE(
MAX(
SchemaCurrency.SchemaPrecision
),
2
)
) AS TotalAccountingAmt,
COALESCE(
SUM(
CASE
WHEN AccountingRows.MatchConfidence='HIGH'
THEN 1
ELSE 0
END
),
0
) AS HighConfidenceCount
FROM AccountingRows AccountingRows
CROSS JOIN SchemaCurrency SchemaCurrency
),
NumberedRows AS
(
SELECT
AccountingRows.AD_Client_ID,
AccountingRows.AD_Org_ID,
AccountingRows.C_Payment_ID,
AccountingRows.C_BPartner_ID,
AccountingRows.PaymentDocNo,
AccountingRows.PaymentDateAcct,
AccountingRows.PaymentCurrencyId,
AccountingRows.PaymentConversionTypeId,
AccountingRows.PaymentOriginalAmt,
AccountingRows.PaymentAllocatedAmt,
AccountingRows.PaymentOpenAmt,
AccountingRows.VendorName,
AccountingRows.PaymentMethod,
AccountingRows.ReferenceNo,
AccountingRows.BankName,
AccountingRows.AccountNo,
AccountingRows.PaymentCurrencyISO,
AccountingRows.PaymentCurrencySymbol,
AccountingRows.PaymentPrecision,
AccountingRows.C_Invoice_ID,
AccountingRows.C_InvoicePaySchedule_ID,
AccountingRows.InvoiceDocNo,
AccountingRows.InvoiceDate,
AccountingRows.DueDate,
AccountingRows.PaymentTerms,
AccountingRows.InvoiceCurrencyId,
AccountingRows.InvoiceCurrencyISO,
AccountingRows.InvoiceCurrencySymbol,
AccountingRows.InvoicePrecision,
AccountingRows.InvoiceOriginalAmt,
AccountingRows.InvoiceAllocatedAmt,
AccountingRows.InvoiceOpenAmt,
AccountingRows.InvoiceOpenPayAmt,
AccountingRows.ReadyAmt,
AccountingRows.AccountingAmt,
AccountingRows.MatchScore,
AccountingRows.MatchConfidence,
ROW_NUMBER() OVER
(
ORDER BY
CASE
WHEN AccountingRows.MatchConfidence='HIGH'
THEN 1
ELSE 2
END,
AccountingRows.MatchScore DESC,
AccountingRows.DifferenceAmt,
AccountingRows.PaymentDateAcct DESC,
AccountingRows.C_Payment_ID
) AS ResultRowNo
FROM AccountingRows AccountingRows
),
PagedRows AS
(
SELECT
NumberedRows.AD_Client_ID,
NumberedRows.AD_Org_ID,
NumberedRows.C_Payment_ID,
NumberedRows.C_BPartner_ID,
NumberedRows.PaymentDocNo,
NumberedRows.PaymentDateAcct,
NumberedRows.PaymentCurrencyId,
NumberedRows.PaymentConversionTypeId,
NumberedRows.PaymentOriginalAmt,
NumberedRows.PaymentAllocatedAmt,
NumberedRows.PaymentOpenAmt,
NumberedRows.VendorName,
NumberedRows.PaymentMethod,
NumberedRows.ReferenceNo,
NumberedRows.BankName,
NumberedRows.AccountNo,
NumberedRows.PaymentCurrencyISO,
NumberedRows.PaymentCurrencySymbol,
NumberedRows.PaymentPrecision,
NumberedRows.C_Invoice_ID,
NumberedRows.C_InvoicePaySchedule_ID,
NumberedRows.InvoiceDocNo,
NumberedRows.InvoiceDate,
NumberedRows.DueDate,
NumberedRows.PaymentTerms,
NumberedRows.InvoiceCurrencyId,
NumberedRows.InvoiceCurrencyISO,
NumberedRows.InvoiceCurrencySymbol,
NumberedRows.InvoicePrecision,
NumberedRows.InvoiceOriginalAmt,
NumberedRows.InvoiceAllocatedAmt,
NumberedRows.InvoiceOpenAmt,
NumberedRows.InvoiceOpenPayAmt,
NumberedRows.ReadyAmt,
NumberedRows.AccountingAmt,
NumberedRows.MatchScore,
NumberedRows.MatchConfidence,
NumberedRows.ResultRowNo
FROM NumberedRows NumberedRows
CROSS JOIN QueryParameters QueryParameters
WHERE
(
QueryParameters.PageSize=0
OR
(
NumberedRows.ResultRowNo>
QueryParameters.OffsetRows
AND NumberedRows.ResultRowNo<=
(
QueryParameters.OffsetRows+
QueryParameters.PageSize
)
)
)
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
(1=1)
ORDER BY
PagedRows.ResultRowNo";

            /*
             * Placeholder count: 6
             * Parameter count:   6
             *
             * Each placeholder occurs exactly once
             * inside QueryParameters.
             */
            SqlParameter[] parameters =
                new SqlParameter[]
                {
                    new SqlParameter(
                        "@AD_Client_ID",
                        ctx.GetAD_Client_ID()
                    ),

                    new SqlParameter(
                        "@C_Payment_ID",
                        Math.Max(
                            0,
                            paymentId
                        )
                    ),

                    new SqlParameter(
                        "@C_Invoice_ID",
                        Math.Max(
                            0,
                            invoiceId
                        )
                    ),

                    new SqlParameter(
                        "@C_InvoicePaySchedule_ID",
                        Math.Max(
                            0,
                            payScheduleId
                        )
                    ),

                    new SqlParameter(
                        "@OffsetRows",
                        Math.Max(
                            0,
                            offsetRows
                        )
                    ),

                    new SqlParameter(
                        "@PageSize",
                        Math.Max(
                            0,
                            pageSize
                        )
                    )
                };

            IDataReader reader = null;

            try
            {
                reader =
                    DB.ExecuteReader(
                        sql,
                        parameters,
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

                    result.SchemaStdPrecision =
                        GetInt(
                            reader,
                            "SchemaPrecision"
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
                                GetString(
                                    reader,
                                    "PaymentCurrencyISO"
                                ),

                            PaymentCurrencySymbol =
                                GetString(
                                    reader,
                                    "PaymentCurrencySymbol"
                                ),

                            PaymentPrecision =
                                GetInt(
                                    reader,
                                    "PaymentPrecision"
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
                                GetString(
                                    reader,
                                    "InvoiceCurrencyISO"
                                ),

                            InvoiceCurrencySymbol =
                                GetString(
                                    reader,
                                    "InvoiceCurrencySymbol"
                                ),

                            InvoicePrecision =
                                GetInt(
                                    reader,
                                    "InvoicePrecision"
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
                if (reader != null)
                {
                    reader.Close();
                    reader.Dispose();
                }
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
                    DocumentNo = "",
                    Message = ""
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
                                : ""
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
                                : ""
                        );

                    return result;
                }

                if (
                    !allocation.ProcessIt(
                        DocActionVariables
                            .ACTION_COMPLETE
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
                                : ""
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
                                : ""
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
            catch (Exception)
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
                    );

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

        private bool IsAmountMatch(
            decimal paymentOpenAmount,
            decimal invoiceOpenAmount)
        {
            decimal difference =
                Math.Abs(
                    paymentOpenAmount -
                    invoiceOpenAmount
                );

            if (
                difference <=
                AmountTolerance
            )
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
                !string.IsNullOrEmpty(
                    referenceNo
                ) &&
                !string.IsNullOrEmpty(
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
                : "";
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
                string.IsNullOrEmpty(msg) ||
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
                    : Convert.ToInt32(
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
                    : Convert.ToDecimal(
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
                    ? ""
                    : Convert.ToString(
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

            return Convert.ToDateTime(
                value
            );
        }

        private class MatchQueryResult
        {
            public MatchQueryResult()
            {
                Rows =
                    new List<MatchQueryRow>();

                SchemaCurrencyISOCode = "";
                SchemaCurrencySymbol = "";
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
            public int PaymentId
            {
                get;
                set;
            }

            public int InvoiceId
            {
                get;
                set;
            }

            public int InvoicePayScheduleId
            {
                get;
                set;
            }

            public int VendorId
            {
                get;
                set;
            }

            public string VendorName
            {
                get;
                set;
            }

            public string PaymentDocumentNo
            {
                get;
                set;
            }

            public DateTime? PaymentDate
            {
                get;
                set;
            }

            public int PaymentCurrencyId
            {
                get;
                set;
            }

            public int PaymentConversionTypeId
            {
                get;
                set;
            }

            public decimal PaymentOriginalAmount
            {
                get;
                set;
            }

            public decimal PaymentAllocatedAmount
            {
                get;
                set;
            }

            public decimal PaymentOpenAmount
            {
                get;
                set;
            }

            public string PaymentMethod
            {
                get;
                set;
            }

            public string ReferenceNo
            {
                get;
                set;
            }

            public string BankName
            {
                get;
                set;
            }

            public string AccountNo
            {
                get;
                set;
            }

            public string PaymentCurrencyISOCode
            {
                get;
                set;
            }

            public string PaymentCurrencySymbol
            {
                get;
                set;
            }

            public int PaymentPrecision
            {
                get;
                set;
            }

            public string InvoiceDocumentNo
            {
                get;
                set;
            }

            public DateTime? InvoiceDate
            {
                get;
                set;
            }

            public DateTime? DueDate
            {
                get;
                set;
            }

            public string PaymentTerms
            {
                get;
                set;
            }

            public int InvoiceCurrencyId
            {
                get;
                set;
            }

            public string InvoiceCurrencyISOCode
            {
                get;
                set;
            }

            public string InvoiceCurrencySymbol
            {
                get;
                set;
            }

            public int InvoicePrecision
            {
                get;
                set;
            }

            public decimal InvoiceOriginalAmount
            {
                get;
                set;
            }

            public decimal InvoiceAllocatedAmount
            {
                get;
                set;
            }

            public decimal InvoiceOpenAmount
            {
                get;
                set;
            }

            public decimal InvoiceOpenAmountPaymentCurrency
            {
                get;
                set;
            }

            public decimal ReadyAmount
            {
                get;
                set;
            }

            public decimal AccountingAmount
            {
                get;
                set;
            }

            public string Confidence
            {
                get;
                set;
            }

            public int Score
            {
                get;
                set;
            }
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
 
