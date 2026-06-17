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
                    JsonConvert.SerializeObject(
                        new
                        {
                            success = false,
                            error = "Session Expired",
                            errorText = "Session Expired"
                        }),
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
            catch (Exception ex)
            {
                return Json(
                    JsonConvert.SerializeObject(
                        new
                        {
                            success = false,
                            error = ex.Message,
                            errorText = GetMessage(
                                ctx,
                                "VAS_072_LoadError",
                                "Could not load AP payment match suggestions"
                            )
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
                    JsonConvert.SerializeObject(
                        new
                        {
                            success = false,
                            error = "Session Expired",
                            errorText = "Session Expired"
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
                        JsonConvert.SerializeObject(
                            new
                            {
                                success = false,
                                error = GetMessage(
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
                    JsonConvert.SerializeObject(
                        new
                        {
                            success = true,
                            error = "",
                            detail = detail
                        }),
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
                            error = ex.Message,
                            errorText = GetMessage(
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
                    JsonConvert.SerializeObject(
                        new
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
                JsonConvert.SerializeObject(
                    new
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
                    JsonConvert.SerializeObject(
                        new
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
                        ? GetMessage(
                            ctx,
                            "VAS_072_ApplySuccess",
                            "Allocation completed successfully"
                        ) +
                        " (" +
                        appliedCount +
                        ")"
                        : GetMessage(
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
                        })
                );
            }
            catch (Exception ex)
            {
                return Json(
                    JsonConvert.SerializeObject(
                        new
                        {
                            success = false,
                            error = ex.Message,
                            message = GetMessage(
                                ctx,
                                "VAS_072_ApplyError",
                                "Could not complete allocation"
                            )
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
             * This query contains no runtime SQL parameters.
             * MRole is applied only to the physical C_Payment query
             * before the query is inserted into AccessiblePayments.
             */
            string paymentAccessSql = @"
SELECT
Payment.AD_Client_ID,
Payment.AD_Org_ID,
Payment.C_Payment_ID,
Payment.C_BPartner_ID,
Payment.DocumentNo AS PaymentDocumentNo,
Payment.DateAcct AS PaymentDateAcct,
Payment.C_Currency_ID AS PaymentCurrencyId,
COALESCE(
Payment.C_ConversionType_ID,
0
) AS PaymentConversionTypeId,
ABS(
COALESCE(
Payment.PayAmt,
0
)
) AS PaymentOriginalAmount,
BusinessPartner.Name AS VendorName,
PaymentMethod.VA009_Name AS PaymentMethod,
COALESCE(
Payment.TrxNo,
Payment.CheckNo
) AS ReferenceNo,
Bank.Name AS BankName,
BankAccount.AccountNo AS AccountNo,
PaymentCurrency.ISO_Code AS PaymentCurrencyISOCode,
CASE
WHEN PaymentCurrency.CurSymbol IS NOT NULL
THEN PaymentCurrency.CurSymbol
ELSE PaymentCurrency.ISO_Code
END AS PaymentCurrencySymbol,
PaymentCurrency.StdPrecision AS PaymentPrecision
FROM C_Payment Payment
INNER JOIN C_BPartner BusinessPartner ON
(
BusinessPartner.C_BPartner_ID=
Payment.C_BPartner_ID
)
INNER JOIN C_Currency PaymentCurrency ON
(
PaymentCurrency.C_Currency_ID=
Payment.C_Currency_ID
)
LEFT OUTER JOIN VA009_PaymentMethod PaymentMethod ON
(
PaymentMethod.VA009_PaymentMethod_ID=
Payment.VA009_PaymentMethod_ID
)
LEFT OUTER JOIN C_BankAccount BankAccount ON
(
BankAccount.C_BankAccount_ID=
Payment.C_BankAccount_ID
)
LEFT OUTER JOIN C_Bank Bank ON
(
Bank.C_Bank_ID=
BankAccount.C_Bank_ID
)
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

            /*
             * Oracle requires FROM DUAL for a SELECT without a table.
             * PostgreSQL does not use DUAL.
             */
            string parameterSource =
                DB.IsPostgreSQL()
                    ? ""
                    : " FROM DUAL";

            string sql = @"
WITH QueryParameters AS
(
SELECT
";

            sql += ToSqlInt(
                    ctx.GetAD_Client_ID()
                ) +
                @" AS AD_Client_ID,
" +
                ToSqlInt(
                    Math.Max(
                        0,
                        paymentId
                    )
                ) +
                @" AS C_Payment_ID,
" +
                ToSqlInt(
                    Math.Max(
                        0,
                        invoiceId
                    )
                ) +
                @" AS C_Invoice_ID,
" +
                ToSqlInt(
                    Math.Max(
                        0,
                        payScheduleId
                    )
                ) +
                @" AS C_InvoicePaySchedule_ID,
" +
                ToSqlInt(
                    Math.Max(
                        0,
                        offsetRows
                    )
                ) +
                @" AS OffsetRows,
" +
                ToSqlInt(
                    Math.Max(
                        0,
                        pageSize
                    )
                ) +
                @" AS PageSize" +
                parameterSource +
                @"
),
SchemaCurrency AS
(
SELECT
ClientInfo.AD_Client_ID,
AcctSchema.C_Currency_ID AS SchemaCurrencyId,
Currency.ISO_Code AS SchemaCurrencyISOCode,
CASE
WHEN Currency.CurSymbol IS NOT NULL
THEN Currency.CurSymbol
ELSE Currency.ISO_Code
END AS SchemaCurrencySymbol,
Currency.StdPrecision AS SchemaStdPrecision
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
INNER JOIN QueryParameters QueryParameters ON
(1=1)
WHERE ClientInfo.IsActive='Y'
AND ClientInfo.AD_Client_ID=
QueryParameters.AD_Client_ID
),
PaymentAllocated AS
(
SELECT
AllocationLine.C_Payment_ID,
SUM(
ABS(
COALESCE(
AllocationLine.Amount,
0
)
)
) AS PaymentAllocatedAmount
FROM C_AllocationLine AllocationLine
INNER JOIN C_AllocationHdr AllocationHeader ON
(
AllocationHeader.C_AllocationHdr_ID=
AllocationLine.C_AllocationHdr_ID
)
WHERE AllocationLine.IsActive='Y'
AND AllocationHeader.IsActive='Y'
AND AllocationHeader.DocStatus IN ('CO','CL')
AND AllocationLine.C_Payment_ID IS NOT NULL
GROUP BY
AllocationLine.C_Payment_ID
),
AccessiblePayments AS
(
" + paymentAccessSql + @"
),
MainPayments AS
(
SELECT
AccessiblePayments.AD_Client_ID,
AccessiblePayments.AD_Org_ID,
AccessiblePayments.C_Payment_ID,
AccessiblePayments.C_BPartner_ID,
AccessiblePayments.PaymentDocumentNo,
AccessiblePayments.PaymentDateAcct,
AccessiblePayments.PaymentCurrencyId,
AccessiblePayments.PaymentConversionTypeId,
AccessiblePayments.PaymentOriginalAmount,
COALESCE(
PaymentAllocated.PaymentAllocatedAmount,
0
) AS PaymentAllocatedAmount,
AccessiblePayments.PaymentOriginalAmount-
COALESCE(
PaymentAllocated.PaymentAllocatedAmount,
0
) AS PaymentOpenAmount,
AccessiblePayments.VendorName,
AccessiblePayments.PaymentMethod,
AccessiblePayments.ReferenceNo,
AccessiblePayments.BankName,
AccessiblePayments.AccountNo,
AccessiblePayments.PaymentCurrencyISOCode,
AccessiblePayments.PaymentCurrencySymbol,
AccessiblePayments.PaymentPrecision
FROM AccessiblePayments AccessiblePayments
INNER JOIN QueryParameters QueryParameters ON
(1=1)
LEFT OUTER JOIN PaymentAllocated PaymentAllocated ON
(
PaymentAllocated.C_Payment_ID=
AccessiblePayments.C_Payment_ID
)
WHERE AccessiblePayments.AD_Client_ID=
QueryParameters.AD_Client_ID
AND
(
QueryParameters.C_Payment_ID=0
OR AccessiblePayments.C_Payment_ID=
QueryParameters.C_Payment_ID
)
AND
(
AccessiblePayments.PaymentOriginalAmount-
COALESCE(
PaymentAllocated.PaymentAllocatedAmount,
0
)
)>0
),
InvoiceAllocated AS
(
SELECT
AllocationLine.C_InvoicePaySchedule_ID,
SUM(
ABS(
COALESCE(
AllocationLine.Amount,
0
)
)
+
ABS(
COALESCE(
AllocationLine.DiscountAmt,
0
)
)
+
ABS(
COALESCE(
AllocationLine.WriteOffAmt,
0
)
)
) AS InvoiceAllocatedAmount
FROM C_AllocationLine AllocationLine
INNER JOIN C_AllocationHdr AllocationHeader ON
(
AllocationHeader.C_AllocationHdr_ID=
AllocationLine.C_AllocationHdr_ID
)
WHERE AllocationLine.IsActive='Y'
AND AllocationHeader.IsActive='Y'
AND AllocationHeader.DocStatus IN ('CO','CL')
AND AllocationLine.C_InvoicePaySchedule_ID IS NOT NULL
GROUP BY
AllocationLine.C_InvoicePaySchedule_ID
),
OpenInvoices AS
(
SELECT
Invoice.AD_Client_ID,
Invoice.AD_Org_ID,
Invoice.C_Invoice_ID,
Invoice.C_BPartner_ID,
Invoice.DocumentNo AS InvoiceDocumentNo,
Invoice.DateInvoiced AS InvoiceDate,
Invoice.C_Currency_ID AS InvoiceCurrencyId,
InvoiceCurrency.ISO_Code AS InvoiceCurrencyISOCode,
CASE
WHEN InvoiceCurrency.CurSymbol IS NOT NULL
THEN InvoiceCurrency.CurSymbol
ELSE InvoiceCurrency.ISO_Code
END AS InvoiceCurrencySymbol,
InvoiceCurrency.StdPrecision AS InvoicePrecision,
PaymentTerm.Name AS PaymentTerms,
InvoicePaySchedule.C_InvoicePaySchedule_ID,
InvoicePaySchedule.DueDate,
ABS(
COALESCE(
InvoicePaySchedule.DueAmt,
0
)
) AS InvoiceOriginalAmount,
COALESCE(
InvoiceAllocated.InvoiceAllocatedAmount,
0
) AS InvoiceAllocatedAmount,
ABS(
COALESCE(
InvoicePaySchedule.DueAmt,
0
)
)-
COALESCE(
InvoiceAllocated.InvoiceAllocatedAmount,
0
) AS InvoiceOpenAmount
FROM C_Invoice Invoice
INNER JOIN C_InvoicePaySchedule InvoicePaySchedule ON
(
InvoicePaySchedule.C_Invoice_ID=
Invoice.C_Invoice_ID
)
INNER JOIN C_Currency InvoiceCurrency ON
(
InvoiceCurrency.C_Currency_ID=
Invoice.C_Currency_ID
)
LEFT OUTER JOIN C_PaymentTerm PaymentTerm ON
(
PaymentTerm.C_PaymentTerm_ID=
Invoice.C_PaymentTerm_ID
)
LEFT OUTER JOIN InvoiceAllocated InvoiceAllocated ON
(
InvoiceAllocated.C_InvoicePaySchedule_ID=
InvoicePaySchedule.C_InvoicePaySchedule_ID
)
INNER JOIN QueryParameters QueryParameters ON
(1=1)
WHERE Invoice.IsActive='Y'
AND Invoice.Processed='Y'
AND Invoice.IsSOTrx='N'
AND Invoice.DocStatus IN ('CO','CL')
AND COALESCE(
Invoice.IsPaid,
'N'
)='N'
AND COALESCE(
Invoice.IsReturnTrx,
'N'
)='N'
AND InvoicePaySchedule.IsActive='Y'
AND COALESCE(
InvoicePaySchedule.IsValid,
'Y'
)='Y'
AND COALESCE(
InvoicePaySchedule.VA009_IsPaid,
'N'
)='N'
AND Invoice.AD_Client_ID=
QueryParameters.AD_Client_ID
AND
(
QueryParameters.C_Invoice_ID=0
OR Invoice.C_Invoice_ID=
QueryParameters.C_Invoice_ID
)
AND
(
QueryParameters.C_InvoicePaySchedule_ID=0
OR InvoicePaySchedule.C_InvoicePaySchedule_ID=
QueryParameters.C_InvoicePaySchedule_ID
)
AND
(
ABS(
COALESCE(
InvoicePaySchedule.DueAmt,
0
)
)-
COALESCE(
InvoiceAllocated.InvoiceAllocatedAmount,
0
)
)>0
),
CandidateRows AS
(
SELECT
MainPayments.AD_Client_ID,
MainPayments.AD_Org_ID,
MainPayments.C_Payment_ID,
MainPayments.C_BPartner_ID,
MainPayments.PaymentDocumentNo,
MainPayments.PaymentDateAcct,
MainPayments.PaymentCurrencyId,
MainPayments.PaymentConversionTypeId,
MainPayments.PaymentOriginalAmount,
MainPayments.PaymentAllocatedAmount,
MainPayments.PaymentOpenAmount,
MainPayments.VendorName,
MainPayments.PaymentMethod,
MainPayments.ReferenceNo,
MainPayments.BankName,
MainPayments.AccountNo,
MainPayments.PaymentCurrencyISOCode,
MainPayments.PaymentCurrencySymbol,
MainPayments.PaymentPrecision,
OpenInvoices.C_Invoice_ID,
OpenInvoices.C_InvoicePaySchedule_ID,
OpenInvoices.InvoiceDocumentNo,
OpenInvoices.InvoiceDate,
OpenInvoices.DueDate,
OpenInvoices.PaymentTerms,
OpenInvoices.InvoiceCurrencyId,
OpenInvoices.InvoiceCurrencyISOCode,
OpenInvoices.InvoiceCurrencySymbol,
OpenInvoices.InvoicePrecision,
OpenInvoices.InvoiceOriginalAmount,
OpenInvoices.InvoiceAllocatedAmount,
OpenInvoices.InvoiceOpenAmount,
CASE
WHEN OpenInvoices.InvoiceCurrencyId=
MainPayments.PaymentCurrencyId
THEN OpenInvoices.InvoiceOpenAmount
ELSE COALESCE(
CurrencyConvert(
OpenInvoices.InvoiceOpenAmount,
OpenInvoices.InvoiceCurrencyId,
MainPayments.PaymentCurrencyId,
MainPayments.PaymentDateAcct,
MainPayments.PaymentConversionTypeId,
MainPayments.AD_Client_ID,
MainPayments.AD_Org_ID
),
0
)
END AS InvoiceOpenAmountPaymentCurrency
FROM MainPayments MainPayments
INNER JOIN OpenInvoices OpenInvoices ON
(
OpenInvoices.AD_Client_ID=
MainPayments.AD_Client_ID
AND OpenInvoices.C_BPartner_ID=
MainPayments.C_BPartner_ID
)
),
ScoredCandidates AS
(
SELECT
CandidateRows.AD_Client_ID,
CandidateRows.AD_Org_ID,
CandidateRows.C_Payment_ID,
CandidateRows.C_BPartner_ID,
CandidateRows.PaymentDocumentNo,
CandidateRows.PaymentDateAcct,
CandidateRows.PaymentCurrencyId,
CandidateRows.PaymentConversionTypeId,
CandidateRows.PaymentOriginalAmount,
CandidateRows.PaymentAllocatedAmount,
CandidateRows.PaymentOpenAmount,
CandidateRows.VendorName,
CandidateRows.PaymentMethod,
CandidateRows.ReferenceNo,
CandidateRows.BankName,
CandidateRows.AccountNo,
CandidateRows.PaymentCurrencyISOCode,
CandidateRows.PaymentCurrencySymbol,
CandidateRows.PaymentPrecision,
CandidateRows.C_Invoice_ID,
CandidateRows.C_InvoicePaySchedule_ID,
CandidateRows.InvoiceDocumentNo,
CandidateRows.InvoiceDate,
CandidateRows.DueDate,
CandidateRows.PaymentTerms,
CandidateRows.InvoiceCurrencyId,
CandidateRows.InvoiceCurrencyISOCode,
CandidateRows.InvoiceCurrencySymbol,
CandidateRows.InvoicePrecision,
CandidateRows.InvoiceOriginalAmount,
CandidateRows.InvoiceAllocatedAmount,
CandidateRows.InvoiceOpenAmount,
CandidateRows.InvoiceOpenAmountPaymentCurrency,
ABS(
CandidateRows.PaymentOpenAmount-
CandidateRows.InvoiceOpenAmountPaymentCurrency
) AS DifferenceAmount,
CASE
WHEN ABS(
CandidateRows.PaymentOpenAmount-
CandidateRows.InvoiceOpenAmountPaymentCurrency
)<=" +
                AmountTolerance.ToString(
                    CultureInfo.InvariantCulture
                ) +
                @"
THEN 'HIGH'
WHEN CandidateRows.InvoiceOpenAmountPaymentCurrency>0
AND
(
ABS(
CandidateRows.PaymentOpenAmount-
CandidateRows.InvoiceOpenAmountPaymentCurrency
)*100/
CandidateRows.InvoiceOpenAmountPaymentCurrency
)<=" +
                HighPercentageThreshold.ToString(
                    CultureInfo.InvariantCulture
                ) +
                @"
THEN 'HIGH'
ELSE 'REVIEW'
END AS MatchConfidence,
ROW_NUMBER() OVER
(
PARTITION BY CandidateRows.C_Payment_ID
ORDER BY
ABS(
CandidateRows.PaymentOpenAmount-
CandidateRows.InvoiceOpenAmountPaymentCurrency
),
CandidateRows.DueDate,
CandidateRows.InvoiceDocumentNo,
CandidateRows.C_InvoicePaySchedule_ID
) AS MatchRank
FROM CandidateRows CandidateRows
WHERE CandidateRows.InvoiceOpenAmountPaymentCurrency>0
),
BestMatches AS
(
SELECT
ScoredCandidates.AD_Client_ID,
ScoredCandidates.AD_Org_ID,
ScoredCandidates.C_Payment_ID,
ScoredCandidates.C_BPartner_ID,
ScoredCandidates.PaymentDocumentNo,
ScoredCandidates.PaymentDateAcct,
ScoredCandidates.PaymentCurrencyId,
ScoredCandidates.PaymentConversionTypeId,
ScoredCandidates.PaymentOriginalAmount,
ScoredCandidates.PaymentAllocatedAmount,
ScoredCandidates.PaymentOpenAmount,
ScoredCandidates.VendorName,
ScoredCandidates.PaymentMethod,
ScoredCandidates.ReferenceNo,
ScoredCandidates.BankName,
ScoredCandidates.AccountNo,
ScoredCandidates.PaymentCurrencyISOCode,
ScoredCandidates.PaymentCurrencySymbol,
ScoredCandidates.PaymentPrecision,
ScoredCandidates.C_Invoice_ID,
ScoredCandidates.C_InvoicePaySchedule_ID,
ScoredCandidates.InvoiceDocumentNo,
ScoredCandidates.InvoiceDate,
ScoredCandidates.DueDate,
ScoredCandidates.PaymentTerms,
ScoredCandidates.InvoiceCurrencyId,
ScoredCandidates.InvoiceCurrencyISOCode,
ScoredCandidates.InvoiceCurrencySymbol,
ScoredCandidates.InvoicePrecision,
ScoredCandidates.InvoiceOriginalAmount,
ScoredCandidates.InvoiceAllocatedAmount,
ScoredCandidates.InvoiceOpenAmount,
ScoredCandidates.InvoiceOpenAmountPaymentCurrency,
ScoredCandidates.DifferenceAmount,
ScoredCandidates.MatchConfidence,
CASE
WHEN ScoredCandidates.PaymentOpenAmount<
ScoredCandidates.InvoiceOpenAmountPaymentCurrency
THEN ScoredCandidates.PaymentOpenAmount
ELSE ScoredCandidates.InvoiceOpenAmountPaymentCurrency
END AS ReadyAmount
FROM ScoredCandidates ScoredCandidates
WHERE ScoredCandidates.MatchRank=1
),
AccountingRows AS
(
SELECT
BestMatches.AD_Client_ID,
BestMatches.AD_Org_ID,
BestMatches.C_Payment_ID,
BestMatches.C_BPartner_ID,
BestMatches.PaymentDocumentNo,
BestMatches.PaymentDateAcct,
BestMatches.PaymentCurrencyId,
BestMatches.PaymentConversionTypeId,
BestMatches.PaymentOriginalAmount,
BestMatches.PaymentAllocatedAmount,
BestMatches.PaymentOpenAmount,
BestMatches.VendorName,
BestMatches.PaymentMethod,
BestMatches.ReferenceNo,
BestMatches.BankName,
BestMatches.AccountNo,
BestMatches.PaymentCurrencyISOCode,
BestMatches.PaymentCurrencySymbol,
BestMatches.PaymentPrecision,
BestMatches.C_Invoice_ID,
BestMatches.C_InvoicePaySchedule_ID,
BestMatches.InvoiceDocumentNo,
BestMatches.InvoiceDate,
BestMatches.DueDate,
BestMatches.PaymentTerms,
BestMatches.InvoiceCurrencyId,
BestMatches.InvoiceCurrencyISOCode,
BestMatches.InvoiceCurrencySymbol,
BestMatches.InvoicePrecision,
BestMatches.InvoiceOriginalAmount,
BestMatches.InvoiceAllocatedAmount,
BestMatches.InvoiceOpenAmount,
BestMatches.InvoiceOpenAmountPaymentCurrency,
BestMatches.DifferenceAmount,
BestMatches.MatchConfidence,
BestMatches.ReadyAmount,
CASE
WHEN BestMatches.PaymentCurrencyId=
SchemaCurrency.SchemaCurrencyId
THEN BestMatches.ReadyAmount
ELSE COALESCE(
CurrencyConvert(
BestMatches.ReadyAmount,
BestMatches.PaymentCurrencyId,
SchemaCurrency.SchemaCurrencyId,
BestMatches.PaymentDateAcct,
BestMatches.PaymentConversionTypeId,
BestMatches.AD_Client_ID,
BestMatches.AD_Org_ID
),
0
)
END AS AccountingAmount
FROM BestMatches BestMatches
INNER JOIN SchemaCurrency SchemaCurrency ON
(
SchemaCurrency.AD_Client_ID=
BestMatches.AD_Client_ID
)
),
SummaryData AS
(
SELECT
COUNT(*) AS TotalRecords,
COALESCE(
SUM(
AccountingRows.AccountingAmount
),
0
) AS TotalAccountingAmount,
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
),
NumberedRows AS
(
SELECT
AccountingRows.AD_Client_ID,
AccountingRows.AD_Org_ID,
AccountingRows.C_Payment_ID,
AccountingRows.C_BPartner_ID,
AccountingRows.PaymentDocumentNo,
AccountingRows.PaymentDateAcct,
AccountingRows.PaymentCurrencyId,
AccountingRows.PaymentConversionTypeId,
AccountingRows.PaymentOriginalAmount,
AccountingRows.PaymentAllocatedAmount,
AccountingRows.PaymentOpenAmount,
AccountingRows.VendorName,
AccountingRows.PaymentMethod,
AccountingRows.ReferenceNo,
AccountingRows.BankName,
AccountingRows.AccountNo,
AccountingRows.PaymentCurrencyISOCode,
AccountingRows.PaymentCurrencySymbol,
AccountingRows.PaymentPrecision,
AccountingRows.C_Invoice_ID,
AccountingRows.C_InvoicePaySchedule_ID,
AccountingRows.InvoiceDocumentNo,
AccountingRows.InvoiceDate,
AccountingRows.DueDate,
AccountingRows.PaymentTerms,
AccountingRows.InvoiceCurrencyId,
AccountingRows.InvoiceCurrencyISOCode,
AccountingRows.InvoiceCurrencySymbol,
AccountingRows.InvoicePrecision,
AccountingRows.InvoiceOriginalAmount,
AccountingRows.InvoiceAllocatedAmount,
AccountingRows.InvoiceOpenAmount,
AccountingRows.InvoiceOpenAmountPaymentCurrency,
AccountingRows.DifferenceAmount,
AccountingRows.MatchConfidence,
AccountingRows.ReadyAmount,
AccountingRows.AccountingAmount,
ROW_NUMBER() OVER
(
ORDER BY
CASE
WHEN AccountingRows.MatchConfidence='HIGH'
THEN 1
ELSE 2
END,
AccountingRows.DifferenceAmount,
AccountingRows.PaymentDateAcct DESC,
AccountingRows.C_Payment_ID
) AS ResultRowNumber
FROM AccountingRows AccountingRows
),
PagedRows AS
(
SELECT
NumberedRows.AD_Client_ID,
NumberedRows.AD_Org_ID,
NumberedRows.C_Payment_ID,
NumberedRows.C_BPartner_ID,
NumberedRows.PaymentDocumentNo,
NumberedRows.PaymentDateAcct,
NumberedRows.PaymentCurrencyId,
NumberedRows.PaymentConversionTypeId,
NumberedRows.PaymentOriginalAmount,
NumberedRows.PaymentAllocatedAmount,
NumberedRows.PaymentOpenAmount,
NumberedRows.VendorName,
NumberedRows.PaymentMethod,
NumberedRows.ReferenceNo,
NumberedRows.BankName,
NumberedRows.AccountNo,
NumberedRows.PaymentCurrencyISOCode,
NumberedRows.PaymentCurrencySymbol,
NumberedRows.PaymentPrecision,
NumberedRows.C_Invoice_ID,
NumberedRows.C_InvoicePaySchedule_ID,
NumberedRows.InvoiceDocumentNo,
NumberedRows.InvoiceDate,
NumberedRows.DueDate,
NumberedRows.PaymentTerms,
NumberedRows.InvoiceCurrencyId,
NumberedRows.InvoiceCurrencyISOCode,
NumberedRows.InvoiceCurrencySymbol,
NumberedRows.InvoicePrecision,
NumberedRows.InvoiceOriginalAmount,
NumberedRows.InvoiceAllocatedAmount,
NumberedRows.InvoiceOpenAmount,
NumberedRows.InvoiceOpenAmountPaymentCurrency,
NumberedRows.DifferenceAmount,
NumberedRows.MatchConfidence,
NumberedRows.ReadyAmount,
NumberedRows.AccountingAmount,
NumberedRows.ResultRowNumber
FROM NumberedRows NumberedRows
INNER JOIN QueryParameters QueryParameters ON
(1=1)
WHERE
(
QueryParameters.PageSize=0
OR
(
NumberedRows.ResultRowNumber>
QueryParameters.OffsetRows
AND NumberedRows.ResultRowNumber<=
(
QueryParameters.OffsetRows+
QueryParameters.PageSize
)
)
)
)
SELECT
SchemaCurrency.SchemaCurrencyId,
SchemaCurrency.SchemaCurrencyISOCode,
SchemaCurrency.SchemaCurrencySymbol,
SchemaCurrency.SchemaStdPrecision,
SummaryData.TotalRecords,
SummaryData.TotalAccountingAmount,
SummaryData.HighConfidenceCount,
PagedRows.C_Payment_ID,
PagedRows.C_BPartner_ID,
PagedRows.PaymentDocumentNo,
PagedRows.PaymentDateAcct,
PagedRows.PaymentCurrencyId,
PagedRows.PaymentConversionTypeId,
PagedRows.PaymentOriginalAmount,
PagedRows.PaymentAllocatedAmount,
PagedRows.PaymentOpenAmount,
PagedRows.VendorName,
PagedRows.PaymentMethod,
PagedRows.ReferenceNo,
PagedRows.BankName,
PagedRows.AccountNo,
PagedRows.PaymentCurrencyISOCode,
PagedRows.PaymentCurrencySymbol,
PagedRows.PaymentPrecision,
PagedRows.C_Invoice_ID,
PagedRows.C_InvoicePaySchedule_ID,
PagedRows.InvoiceDocumentNo,
PagedRows.InvoiceDate,
PagedRows.DueDate,
PagedRows.PaymentTerms,
PagedRows.InvoiceCurrencyId,
PagedRows.InvoiceCurrencyISOCode,
PagedRows.InvoiceCurrencySymbol,
PagedRows.InvoicePrecision,
PagedRows.InvoiceOriginalAmount,
PagedRows.InvoiceAllocatedAmount,
PagedRows.InvoiceOpenAmount,
PagedRows.InvoiceOpenAmountPaymentCurrency,
PagedRows.ReadyAmount,
PagedRows.AccountingAmount,
PagedRows.MatchConfidence,
PagedRows.ResultRowNumber
FROM SchemaCurrency SchemaCurrency
INNER JOIN SummaryData SummaryData ON
(1=1)
LEFT OUTER JOIN PagedRows PagedRows ON
(1=1)
ORDER BY
PagedRows.ResultRowNumber";

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
                            "SchemaCurrencyISOCode"
                        );

                    result.SchemaCurrencySymbol =
                        GetString(
                            reader,
                            "SchemaCurrencySymbol"
                        );

                    result.SchemaStdPrecision =
                        GetInt(
                            reader,
                            "SchemaStdPrecision"
                        );

                    result.TotalRecords =
                        GetInt(
                            reader,
                            "TotalRecords"
                        );

                    result.TotalAccountingAmount =
                        GetDecimal(
                            reader,
                            "TotalAccountingAmount"
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
                                    "PaymentDocumentNo"
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
                                    "PaymentOriginalAmount"
                                ),

                            PaymentAllocatedAmount =
                                GetDecimal(
                                    reader,
                                    "PaymentAllocatedAmount"
                                ),

                            PaymentOpenAmount =
                                GetDecimal(
                                    reader,
                                    "PaymentOpenAmount"
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
                                    "PaymentCurrencyISOCode"
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
                                    "InvoiceDocumentNo"
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
                                    "InvoiceCurrencyISOCode"
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
                                    "InvoiceOriginalAmount"
                                ),

                            InvoiceAllocatedAmount =
                                GetDecimal(
                                    reader,
                                    "InvoiceAllocatedAmount"
                                ),

                            InvoiceOpenAmount =
                                GetDecimal(
                                    reader,
                                    "InvoiceOpenAmount"
                                ),

                            InvoiceOpenAmountPaymentCurrency =
                                GetDecimal(
                                    reader,
                                    "InvoiceOpenAmountPaymentCurrency"
                                ),

                            ReadyAmount =
                                GetDecimal(
                                    reader,
                                    "ReadyAmount"
                                ),

                            AccountingAmount =
                                GetDecimal(
                                    reader,
                                    "AccountingAmount"
                                ),

                            Confidence =
                                GetString(
                                    reader,
                                    "MatchConfidence"
                                )
                        };

                    row.Score =
                        CalculateScore(
                            row.PaymentOpenAmount,
                            row.InvoiceOpenAmountPaymentCurrency,
                            row.PaymentDate,
                            row.DueDate
                        );

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
                    GetMessage(
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
                        GetMessage(
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
                    invoice.IsSOTrx()
                )
                {
                    trx.Rollback();

                    result.Message =
                        GetMessage(
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
                        GetMessage(
                            ctx,
                            "VAS_072_DifferentBusinessPartner",
                            "Payment and invoice belong to different vendors"
                        );

                    return result;
                }

                if (payment.IsAllocated())
                {
                    trx.Rollback();

                    result.Message =
                        GetMessage(
                            ctx,
                            "PaymentIsAllocated",
                            "Payment is already allocated"
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
                        GetMessage(
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
                        GetMessage(
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
                        GetMessage(
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
                        GetMessage(
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
                        GetMessage(
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

                if (payment.TestAllocation())
                {
                    if (!payment.Save())
                    {
                        trx.Rollback();

                        ValueNamePair error =
                            VLogger.RetrieveError();

                        result.Message =
                            GetMessage(
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
                }

                trx.Commit();

                result.Success = true;

                result.DocumentNo =
                    allocation.GetDocumentNo();

                result.Message =
                    GetMessage(
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

        private int CalculateScore(
            decimal paymentOpenAmount,
            decimal invoiceOpenAmount,
            DateTime? paymentDate,
            DateTime? dueDate)
        {
            decimal difference =
                Math.Abs(
                    paymentOpenAmount -
                    invoiceOpenAmount
                );

            decimal comparisonAmount =
                Math.Max(
                    Math.Abs(
                        paymentOpenAmount
                    ),
                    Math.Abs(
                        invoiceOpenAmount
                    )
                );

            decimal differencePercentage =
                comparisonAmount == 0
                    ? 100
                    : difference *
                      100 /
                      comparisonAmount;

            int score = 40;

            if (
                difference <=
                AmountTolerance
            )
            {
                score += 45;
            }
            else if (
                differencePercentage <= 1
            )
            {
                score += 38;
            }
            else if (
                differencePercentage <=
                HighPercentageThreshold
            )
            {
                score += 28;
            }
            else if (
                differencePercentage <= 10
            )
            {
                score += 18;
            }
            else if (
                differencePercentage <=
                ReviewPercentageThreshold
            )
            {
                score += 8;
            }

            if (
                paymentDate.HasValue &&
                dueDate.HasValue
            )
            {
                double dateDifference =
                    Math.Abs(
                        (
                            paymentDate.Value.Date -
                            dueDate.Value.Date
                        ).TotalDays
                    );

                if (dateDifference <= 7)
                {
                    score += 15;
                }
                else if (
                    dateDifference <=
                    DateWindowDays
                )
                {
                    score += 10;
                }
                else if (
                    dateDifference <= 60
                )
                {
                    score += 5;
                }
            }

            return Math.Min(
                100,
                score
            );
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

        private string GetMessage(
            Ctx ctx,
            string key,
            string fallback)
        {
            if (ctx == null)
            {
                return fallback;
            }

            string message =
                Msg.GetMsg(
                    ctx,
                    key
                );

            if (
                string.IsNullOrEmpty(message) ||
                message == key ||
                message == "[" + key + "]"
            )
            {
                return fallback;
            }

            return message;
        }

        private string ToSqlInt(int value)
        {
            return value.ToString(
                CultureInfo.InvariantCulture
            );
        }

        private int GetInt(
            IDataRecord record,
            string columnName)
        {
            object value =
                record[columnName];

            return value == null ||
                   value == DBNull.Value
                ? 0
                : Convert.ToInt32(value);
        }

        private decimal GetDecimal(
            IDataRecord record,
            string columnName)
        {
            object value =
                record[columnName];

            return value == null ||
                   value == DBNull.Value
                ? 0
                : Convert.ToDecimal(value);
        }

        private string GetString(
            IDataRecord record,
            string columnName)
        {
            object value =
                record[columnName];

            return value == null ||
                   value == DBNull.Value
                ? ""
                : Convert.ToString(value);
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

            return Convert.ToDateTime(value);
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
