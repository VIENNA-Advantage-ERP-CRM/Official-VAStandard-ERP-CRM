/******************************************************
 * Module Name    : VAS
 * Purpose        : AP Payment Match Suggestions Widget
 * Chronological  : Development
 * Created Date   : 2026-06-20
 * Created by     : VAI145
 ******************************************************/

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
    /// <summary>
    /// Returns AP payment-to-purchase-invoice suggestions and applies
    /// confirmed allocations through C_AllocationHdr / C_AllocationLine.
    ///
    /// Every controller operation has its own dedicated query method:
    /// 1. ReadMatchSuggestionList      - widget list and pagination.
    /// 2. ReadMatchDetail              - selected popup details.
    /// 3. ReadHighConfidenceCandidates - bulk high-confidence processing.
    /// 4. ReadAllocationValidation     - transaction-time revalidation.
    /// </summary>
    public class VAS_072_MatchSuggestionAPPaymentWidgetController : Controller
    {
        private const int DefaultPageSize = 6;
        private const int MaximumPageSize = 25;
        private const decimal ExactTolerance = 0.01M;
        private const decimal MaximumDifferencePercentage = 20M;
        private const int HighConfidenceScore = 85;

        #region Public Actions

        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetMatchSuggestions(
            int pageNo = 1,
            int pageSize = DefaultPageSize)
        {
            Ctx ctx = GetContext();

            if (ctx == null)
            {
                return SessionExpiredResult(true);
            }

            try
            {
                pageNo = Math.Max(1, pageNo);
                pageSize = Math.Max(1, Math.Min(MaximumPageSize, pageSize));

                int startRow = ((pageNo - 1) * pageSize) + 1;
                int endRow = startRow + pageSize - 1;

                MatchListResult queryResult = ReadMatchSuggestionList(
                    ctx,
                    startRow,
                    endRow
                );

                int totalPages = queryResult.TotalRecords <= 0
                    ? 0
                    : Convert.ToInt32(
                        Math.Ceiling(
                            queryResult.TotalRecords / (decimal)pageSize
                        )
                    );

                object response = new
                {
                    success = true,
                    error = string.Empty,
                    hasData = queryResult.TotalRecords > 0,
                    pageNo = pageNo,
                    pageSize = pageSize,
                    totalPages = totalPages,
                    totalRecords = queryResult.TotalRecords,
                    totalReadyAmount = queryResult.TotalAccountingAmount,
                    highConfidenceCount = queryResult.HighConfidenceCount,
                    cCurrencyId = queryResult.SchemaCurrencyId,
                    currencyISOCode = queryResult.SchemaCurrencyISOCode,
                    currencySymbol = queryResult.SchemaCurrencySymbol,
                    stdPrecision = queryResult.SchemaStdPrecision,
                    allocationFormId = GetAllocationFormId(ctx),
                    allocationWindowId = 0,
                    rows = queryResult.Rows.Select(
                        row => new
                        {
                            paymentId = row.PaymentId,
                            invoiceId = row.InvoiceId,
                            payScheduleId = row.InvoicePayScheduleId,
                            vendorId = row.VendorId,
                            vendorName = row.VendorName,
                            paymentDocumentNo = row.PaymentDocumentNo,
                            invoiceDocumentNo = row.InvoiceDocumentNo,
                            paymentDate = FormatDate(row.PaymentDate),
                            invoiceDate = FormatDate(row.InvoiceDate),
                            dueDate = FormatDate(row.DueDate),
                            isReturnCycle = row.IsReturnCycle,
                            paymentAmount = ApplyCycleSign(row.PaymentOpenAmount, row.IsReturnCycle),
                            paymentAllocatedAmount = ApplyCycleSign(row.PaymentAllocatedAmount, row.IsReturnCycle),
                            paymentOpenAmount = ApplyCycleSign(row.PaymentOpenAmount, row.IsReturnCycle),
                            invoiceAmount = ApplyCycleSign(row.InvoiceOriginalAmount, row.IsReturnCycle),
                            invoiceAllocatedAmount = ApplyCycleSign(row.InvoiceAllocatedAmount, row.IsReturnCycle),
                            invoiceOpenAmount = ApplyCycleSign(row.InvoiceOpenAmount, row.IsReturnCycle),
                            readyAmount = ApplyCycleSign(row.ReadyAmount, row.IsReturnCycle),
                            accountingAmount = row.AccountingAmount,
                            confidence = row.Confidence,
                            score = row.Score,
                            differenceAmount = row.DifferenceAmount,
                            differencePercentage = Math.Round(row.DifferencePercentage, 2),
                            dateGapDays = row.DateGapDays,
                            referenceMatch = row.ReferenceMatch,
                            isRecommended = true,
                            isAutoApplicable = row.IsAutoApplicable,
                            cCurrencyId = row.PaymentCurrencyId,
                            currencyISOCode = row.PaymentCurrencyISOCode,
                            currencySymbol = row.PaymentCurrencySymbol,
                            stdPrecision = row.PaymentPrecision,
                            status = new
                            {
                                value = row.Confidence,
                                name = GetConfidenceName(ctx, row.Confidence)
                            }
                        }
                    )
                };

                return Json(response, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                VLogger.Get().SaveError(
                    "VAS_072_GetMatchSuggestions",
                    ex
                );

                return Json(
                    new
                    {
                        success = false,
                        error = GetMsg(
                            ctx,
                            "VAS_072_LoadError",
                            "Could not load match suggestions"
                        ),
                        hasData = false
                    },
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
                return SessionExpiredResult(true);
            }

            if (paymentId <= 0 || invoiceId <= 0 || payScheduleId <= 0)
            {
                return Json(
                    new
                    {
                        success = false,
                        error = GetMsg(
                            ctx,
                            "FillMandatory",
                            "Mandatory values are missing"
                        )
                    },
                    JsonRequestBehavior.AllowGet
                );
            }

            try
            {
                MatchDetailRow row = ReadMatchDetail(
                    ctx,
                    paymentId,
                    invoiceId,
                    payScheduleId
                );

                if (row == null)
                {
                    return Json(
                        new
                        {
                            success = false,
                            error = GetMsg(
                                ctx,
                                "VAS_072_DetailNotFound",
                                "The selected match is no longer available"
                            )
                        },
                        JsonRequestBehavior.AllowGet
                    );
                }

                decimal balanceAfterApply = Math.Max(
                    0,
                    row.InvoiceOpenAmount - row.ReadyAmount
                );

                object detail = new
                {
                    paymentId = row.PaymentId,
                    isReturnCycle = row.IsReturnCycle,
                    paymentDocumentNo = row.PaymentDocumentNo,
                    paymentDate = FormatDate(row.PaymentDate),
                    vendorId = row.VendorId,
                    vendorName = row.VendorName,
                    paymentMethod = row.PaymentMethodCode,
                    paymentMethodName = row.PaymentMethodName,
                    paymentDocTypeName = row.PaymentDocTypeName,
                    reference = row.ReferenceNo,
                    bankName = row.BankName,
                    accountNo = row.AccountNo,
                    paymentCurrencyId = row.PaymentCurrencyId,
                    paymentCurrencyISOCode = row.PaymentCurrencyISOCode,
                    paymentCurrencySymbol = row.PaymentCurrencySymbol,
                    paymentPrecision = row.PaymentPrecision,
                    paymentOriginalAmount = ApplyCycleSign(row.PaymentOriginalAmount, row.IsReturnCycle),
                    paymentAllocatedAmount = ApplyCycleSign(row.PaymentAllocatedAmount, row.IsReturnCycle),
                    paymentOpenAmount = ApplyCycleSign(row.PaymentOpenAmount, row.IsReturnCycle),
                    invoiceId = row.InvoiceId,
                    invoicePayScheduleId = row.InvoicePayScheduleId,
                    invoiceDocumentNo = row.InvoiceDocumentNo,
                    invoiceDate = FormatDate(row.InvoiceDate),
                    dueDate = FormatDate(row.DueDate),
                    invoiceDocTypeName = row.InvoiceDocTypeName,
                    paymentTerms = row.PaymentTerms,
                    invoiceCurrencyId = row.InvoiceCurrencyId,
                    invoiceCurrencyISOCode = row.InvoiceCurrencyISOCode,
                    invoiceCurrencySymbol = row.InvoiceCurrencySymbol,
                    invoicePrecision = row.InvoicePrecision,
                    invoiceOriginalAmount = ApplyCycleSign(row.InvoiceOriginalAmount, row.IsReturnCycle),
                    invoiceAllocatedAmount = ApplyCycleSign(row.InvoiceAllocatedAmount, row.IsReturnCycle),
                    invoiceOpenAmount = ApplyCycleSign(row.InvoiceOpenAmount, row.IsReturnCycle),
                    invoiceOpenAmountPaymentCurrency = ApplyCycleSign(row.InvoiceOpenAmount, row.IsReturnCycle),
                    readyAmount = ApplyCycleSign(row.ReadyAmount, row.IsReturnCycle),
                    accountingAmount = ApplyCycleSign(row.ReadyAmount, row.IsReturnCycle),
                    balanceAfterApply = ApplyCycleSign(balanceAfterApply, row.IsReturnCycle),
                    partnerOk = row.PaymentVendorId == row.InvoiceVendorId,
                    amountOk = row.DifferenceAmount <= ExactTolerance,
                    referenceOk = row.ReferenceMatch,
                    dateOk = row.DateGapDays <= 7,
                    score = row.Score,
                    confidence = row.Confidence,
                    confidenceValue = row.Confidence,
                    confidenceName = GetConfidenceName(ctx, row.Confidence),
                    differenceAmount = row.DifferenceAmount,
                    differencePercentage = Math.Round(row.DifferencePercentage, 2),
                    dateGapDays = row.DateGapDays,
                    isRecommended = true,
                    isAutoApplicable = row.IsAutoApplicable,
                    status = new
                    {
                        value = row.Confidence,
                        name = GetConfidenceName(ctx, row.Confidence)
                    }
                };

                return Json(
                    new
                    {
                        success = true,
                        error = string.Empty,
                        detail = detail
                    },
                    JsonRequestBehavior.AllowGet
                );
            }
            catch (Exception ex)
            {
                VLogger.Get().SaveError(
                    "VAS_072_GetMatchDetail",
                    ex
                );

                return Json(
                    new
                    {
                        success = false,
                        error = GetMsg(
                            ctx,
                            "VAS_072_LoadDetailError",
                            "Could not load match details"
                        )
                    },
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
                return SessionExpiredResult(false);
            }

            ApplyResult result = ApplyMatchAllocation(
                ctx,
                paymentId,
                invoiceId,
                payScheduleId
            );

            return Json(
                new
                {
                    success = result.Success,
                    error = result.Success ? string.Empty : result.Message,
                    documentNo = result.DocumentNo,
                    message = result.Message
                }
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
                return SessionExpiredResult(false);
            }

            try
            {
                List<HighConfidenceCandidateRow> highConfidenceRows =
                    ReadHighConfidenceCandidates(ctx);

                int appliedCount = 0;
                List<string> errors = new List<string>();

                foreach (HighConfidenceCandidateRow row in highConfidenceRows)
                {
                    ApplyResult applyResult = ApplyMatchAllocation(
                        ctx,
                        row.PaymentId,
                        row.InvoiceId,
                        row.InvoicePayScheduleId
                    );

                    if (applyResult.Success)
                    {
                        appliedCount++;
                    }
                    else if (!string.IsNullOrWhiteSpace(applyResult.Message))
                    {
                        errors.Add(applyResult.Message);
                    }
                }

                bool success = errors.Count == 0;
                string message;

                if (success)
                {
                    message = GetMsg(
                        ctx,
                        "VAS_072_ApplySuccess",
                        "Allocation completed successfully"
                    ) + " (" + appliedCount + ")";
                }
                else
                {
                    message = GetMsg(
                        ctx,
                        "VAS_072_ApplyError",
                        "Could not complete allocation"
                    ) + ". " + string.Join(" | ", errors);
                }

                return Json(
                    new
                    {
                        success = success,
                        error = success ? string.Empty : message,
                        appliedCount = appliedCount,
                        failedCount = errors.Count,
                        message = message
                    }
                );
            }
            catch (Exception ex)
            {
                VLogger.Get().SaveError(
                    "VAS_072_ApplyHighConfidence",
                    ex
                );

                string errorMessage = GetMsg(
                    ctx,
                    "VAS_072_ApplyError",
                    "Could not complete allocation"
                );

                return Json(
                    new
                    {
                        success = false,
                        error = errorMessage,
                        message = errorMessage
                    }
                );
            }
        }

        #endregion

        #region Widget List Query

        /// <summary>
        /// Dedicated query for the widget list only.
        /// Parameters: AD_Client_ID, pagination and matching thresholds.
        /// It does not accept payment/invoice IDs or zero sentinel filters.
        /// </summary>
        private MatchListResult ReadMatchSuggestionList(
            Ctx ctx,
            int startRow,
            int endRow)
        {
            MatchListResult result = new MatchListResult();
            IDataReader reader = null;

            string paymentAccessSql = MRole.GetDefault(ctx).AddAccessSQL(
                @"
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
    Payment.VAS_UnAllocatedAmount,
    Payment.TrxNo,
    Payment.CheckNo,
    Payment.TenderType
FROM C_Payment Payment
WHERE Payment.IsActive='Y'
AND Payment.Processed='Y'
AND Payment.IsReceipt='N'
AND Payment.DocStatus IN ('CO','CL')
AND COALESCE(Payment.IsAllocated,'N')='N'
AND Payment.C_BPartner_ID IS NOT NULL
AND Payment.C_CashLine_ID IS NULL
AND (Payment.TenderType IS NULL OR Payment.TenderType<>'B')
AND Payment.AD_Client_ID=(SELECT QueryParameters.AD_Client_ID FROM QueryParameters QueryParameters)",
                "Payment",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            string invoiceAccessSql = MRole.GetDefault(ctx).AddAccessSQL(
                @"
SELECT
    Invoice.AD_Client_ID,
    Invoice.AD_Org_ID,
    Invoice.C_Invoice_ID,
    Invoice.C_BPartner_ID,
    Invoice.DocumentNo,
    Invoice.DateInvoiced,
    Invoice.C_Currency_ID,
    Invoice.GrandTotal,
    Invoice.GrandTotalAfterWithholding,
    Invoice.C_DocType_ID
FROM C_Invoice Invoice
WHERE Invoice.IsActive='Y'
AND Invoice.Processed='Y'
AND Invoice.IsSOTrx='N'
AND Invoice.DocStatus IN ('CO','CL')
AND COALESCE(Invoice.IsPaid,'N')='N'
AND Invoice.C_BPartner_ID IS NOT NULL
AND Invoice.AD_Client_ID=(SELECT QueryParameters.AD_Client_ID FROM QueryParameters QueryParameters)",
                "Invoice",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            string queryParametersFrom = DB.IsOracle()
                ? " FROM DUAL"
                : string.Empty;

            string sql = @"
WITH QueryParameters AS
(
    SELECT
        @AD_Client_ID AS AD_Client_ID,
        @StartRow AS StartRow,
        @EndRow AS EndRow,
        @ExactTolerance AS ExactTolerance,
        @MaximumDifferencePercentage AS MaximumDifferencePercentage,
        @HighConfidenceScore AS HighConfidenceScore"
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
            WHEN Currency.CurSymbol IS NOT NULL THEN Currency.CurSymbol
            ELSE Currency.ISO_Code
        END AS CurSymbol
    FROM AD_ClientInfo ClientInfo
    INNER JOIN C_AcctSchema AcctSchema ON
    (
        AcctSchema.C_AcctSchema_ID=ClientInfo.C_AcctSchema1_ID
    )
    INNER JOIN C_Currency Currency ON
    (
        Currency.C_Currency_ID=AcctSchema.C_Currency_ID
    )
    WHERE ClientInfo.IsActive='Y'
    AND ClientInfo.AD_Client_ID=(SELECT QueryParameters.AD_Client_ID FROM QueryParameters QueryParameters)
),
AccessiblePayments AS
(
    /*PAYMENT_ACCESS_SQL*/
),
AccessibleInvoices AS
(
    /*INVOICE_ACCESS_SQL*/
),
PaymentAllocated AS
(
    SELECT
        AllocationLine.C_Payment_ID,
        SUM(ABS(COALESCE(AllocationLine.Amount,0))) AS AllocatedAmount
    FROM C_AllocationLine AllocationLine
    INNER JOIN C_AllocationHdr AllocationHeader ON
    (
        AllocationHeader.C_AllocationHdr_ID=AllocationLine.C_AllocationHdr_ID
    )
    WHERE AllocationHeader.IsActive='Y'
    AND AllocationHeader.DocStatus IN ('CO','CL')
    AND AllocationHeader.AD_Client_ID=(SELECT QueryParameters.AD_Client_ID FROM QueryParameters QueryParameters)
    AND AllocationLine.C_Payment_ID IS NOT NULL
    GROUP BY AllocationLine.C_Payment_ID
),
InvoiceScheduleAllocated AS
(
    SELECT
        AllocationLine.C_InvoicePaySchedule_ID,
        SUM(ABS(COALESCE(AllocationLine.Amount,0))) AS AllocatedAmount
    FROM C_AllocationLine AllocationLine
    INNER JOIN C_AllocationHdr AllocationHeader ON
    (
        AllocationHeader.C_AllocationHdr_ID=AllocationLine.C_AllocationHdr_ID
    )
    WHERE AllocationHeader.IsActive='Y'
    AND AllocationHeader.DocStatus IN ('CO','CL')
    AND AllocationHeader.AD_Client_ID=(SELECT QueryParameters.AD_Client_ID FROM QueryParameters QueryParameters)
    AND AllocationLine.C_InvoicePaySchedule_ID IS NOT NULL
    GROUP BY AllocationLine.C_InvoicePaySchedule_ID
),
PaymentRows AS
(
    SELECT
        Payment.AD_Client_ID,
        Payment.AD_Org_ID,
        Payment.C_Payment_ID,
        Payment.C_BPartner_ID,
        Payment.DocumentNo AS PaymentDocumentNo,
        Payment.DateAcct AS PaymentDate,
        Payment.C_Currency_ID AS PaymentCurrencyId,
        COALESCE(Payment.C_ConversionType_ID,0) AS PaymentConversionTypeId,
        COALESCE(PaymentAllocated.AllocatedAmount,0) AS PaymentAllocatedAmount,
        CASE
            WHEN ABS(COALESCE(Payment.PayAmt,0))-COALESCE(PaymentAllocated.AllocatedAmount,0)<=0 THEN 0
            WHEN ABS(COALESCE(Payment.VAS_UnAllocatedAmount,0))>(SELECT QueryParameters.ExactTolerance FROM QueryParameters QueryParameters)
            AND ABS(COALESCE(Payment.VAS_UnAllocatedAmount,0))<ABS(COALESCE(Payment.PayAmt,0))-COALESCE(PaymentAllocated.AllocatedAmount,0)
                THEN ABS(COALESCE(Payment.VAS_UnAllocatedAmount,0))
            ELSE ABS(COALESCE(Payment.PayAmt,0))-COALESCE(PaymentAllocated.AllocatedAmount,0)
        END AS PaymentOpenAmount,
        BusinessPartner.Name AS VendorName,
        COALESCE(Payment.TrxNo,Payment.CheckNo,Payment.DocumentNo) AS ReferenceNo,
        PaymentCurrency.ISO_Code AS PaymentCurrencyISOCode,
        CASE
            WHEN PaymentCurrency.CurSymbol IS NOT NULL THEN PaymentCurrency.CurSymbol
            ELSE PaymentCurrency.ISO_Code
        END AS PaymentCurrencySymbol,
        COALESCE(PaymentCurrency.StdPrecision,2) AS PaymentPrecision
    FROM AccessiblePayments Payment
    INNER JOIN C_BPartner BusinessPartner ON
    (
        BusinessPartner.C_BPartner_ID=Payment.C_BPartner_ID
    )
    INNER JOIN C_Currency PaymentCurrency ON
    (
        PaymentCurrency.C_Currency_ID=Payment.C_Currency_ID
    )
    LEFT OUTER JOIN PaymentAllocated PaymentAllocated ON
    (
        PaymentAllocated.C_Payment_ID=Payment.C_Payment_ID
    )
    WHERE
    (
        CASE
            WHEN ABS(COALESCE(Payment.PayAmt,0))-COALESCE(PaymentAllocated.AllocatedAmount,0)<=0 THEN 0
            WHEN ABS(COALESCE(Payment.VAS_UnAllocatedAmount,0))>(SELECT QueryParameters.ExactTolerance FROM QueryParameters QueryParameters)
            AND ABS(COALESCE(Payment.VAS_UnAllocatedAmount,0))<ABS(COALESCE(Payment.PayAmt,0))-COALESCE(PaymentAllocated.AllocatedAmount,0)
                THEN ABS(COALESCE(Payment.VAS_UnAllocatedAmount,0))
            ELSE ABS(COALESCE(Payment.PayAmt,0))-COALESCE(PaymentAllocated.AllocatedAmount,0)
        END
    )>(SELECT QueryParameters.ExactTolerance FROM QueryParameters QueryParameters)
),
InvoiceRows AS
(
    SELECT
        Invoice.C_Invoice_ID,
        Invoice.C_BPartner_ID,
        Invoice.DocumentNo AS InvoiceDocumentNo,
        Invoice.DateInvoiced AS InvoiceDate,
        COALESCE(InvoicePaySchedule.DueDate,Invoice.DateInvoiced) AS DueDate,
        Invoice.C_Currency_ID AS InvoiceCurrencyId,
        COALESCE(InvoiceDocType.IsReturnTrx,'N') AS InvoiceIsReturn,
        InvoicePaySchedule.C_InvoicePaySchedule_ID,
        ABS(COALESCE(Invoice.GrandTotalAfterWithholding,Invoice.GrandTotal,0)) AS InvoiceOriginalAmount,
        COALESCE(InvoiceScheduleAllocated.AllocatedAmount,0) AS InvoiceAllocatedAmount,
        CASE
            WHEN ABS(COALESCE(InvoicePaySchedule.DueAmt,0))-COALESCE(InvoiceScheduleAllocated.AllocatedAmount,0)>0
                THEN ABS(COALESCE(InvoicePaySchedule.DueAmt,0))-COALESCE(InvoiceScheduleAllocated.AllocatedAmount,0)
            ELSE 0
        END AS InvoiceOpenAmount
    FROM AccessibleInvoices Invoice
    INNER JOIN C_InvoicePaySchedule InvoicePaySchedule ON
    (
        InvoicePaySchedule.C_Invoice_ID=Invoice.C_Invoice_ID
    )
    LEFT OUTER JOIN C_DocType InvoiceDocType ON
    (
        InvoiceDocType.C_DocType_ID=Invoice.C_DocType_ID
    )
    LEFT OUTER JOIN InvoiceScheduleAllocated InvoiceScheduleAllocated ON
    (
        InvoiceScheduleAllocated.C_InvoicePaySchedule_ID=InvoicePaySchedule.C_InvoicePaySchedule_ID
    )
    WHERE InvoicePaySchedule.IsActive='Y'
    AND COALESCE(InvoicePaySchedule.IsHoldPayment,'N')='N'
    AND
    (
        CASE
            WHEN ABS(COALESCE(InvoicePaySchedule.DueAmt,0))-COALESCE(InvoiceScheduleAllocated.AllocatedAmount,0)>0
                THEN ABS(COALESCE(InvoicePaySchedule.DueAmt,0))-COALESCE(InvoiceScheduleAllocated.AllocatedAmount,0)
            ELSE 0
        END
    )>(SELECT QueryParameters.ExactTolerance FROM QueryParameters QueryParameters)
),
CandidateMatches AS
(
    SELECT
        PaymentRows.AD_Client_ID,
        PaymentRows.AD_Org_ID,
        PaymentRows.C_Payment_ID,
        PaymentRows.C_BPartner_ID,
        PaymentRows.PaymentDocumentNo,
        PaymentRows.PaymentDate,
        PaymentRows.PaymentCurrencyId,
        PaymentRows.PaymentConversionTypeId,
        PaymentRows.PaymentAllocatedAmount,
        PaymentRows.PaymentOpenAmount,
        PaymentRows.VendorName,
        PaymentRows.ReferenceNo,
        PaymentRows.PaymentCurrencyISOCode,
        PaymentRows.PaymentCurrencySymbol,
        PaymentRows.PaymentPrecision,
        InvoiceRows.C_Invoice_ID,
        InvoiceRows.C_InvoicePaySchedule_ID,
        InvoiceRows.InvoiceDocumentNo,
        InvoiceRows.InvoiceDate,
        InvoiceRows.DueDate,
        InvoiceRows.InvoiceIsReturn,
        InvoiceRows.InvoiceOriginalAmount,
        InvoiceRows.InvoiceAllocatedAmount,
        InvoiceRows.InvoiceOpenAmount,
        CASE
            WHEN PaymentRows.PaymentOpenAmount<=InvoiceRows.InvoiceOpenAmount THEN PaymentRows.PaymentOpenAmount
            ELSE InvoiceRows.InvoiceOpenAmount
        END AS ReadyAmount,
        ABS(PaymentRows.PaymentOpenAmount-InvoiceRows.InvoiceOpenAmount) AS DifferenceAmount,
        ABS(PaymentRows.PaymentOpenAmount-InvoiceRows.InvoiceOpenAmount)*100/InvoiceRows.InvoiceOpenAmount AS DifferencePercentage,
        ABS(CAST(PaymentRows.PaymentDate AS DATE)-CAST(InvoiceRows.DueDate AS DATE)) AS DateGapDays,
        CASE
            WHEN PaymentRows.ReferenceNo IS NOT NULL
            AND InvoiceRows.InvoiceDocumentNo IS NOT NULL
            AND UPPER(PaymentRows.ReferenceNo) LIKE '%' || UPPER(InvoiceRows.InvoiceDocumentNo) || '%'
                THEN 1
            ELSE 0
        END AS ReferenceMatch,
        SchemaCurrency.C_Currency_ID AS SchemaCurrencyId,
        SchemaCurrency.ISO_Code AS SchemaCurrencyISOCode,
        SchemaCurrency.CurSymbol AS SchemaCurrencySymbol,
        COALESCE(SchemaCurrency.StdPrecision,2) AS SchemaStdPrecision
    FROM PaymentRows PaymentRows
    INNER JOIN InvoiceRows InvoiceRows ON
    (
        InvoiceRows.C_BPartner_ID=PaymentRows.C_BPartner_ID
        AND InvoiceRows.InvoiceCurrencyId=PaymentRows.PaymentCurrencyId
    )
    INNER JOIN SchemaCurrency SchemaCurrency ON
    (
        SchemaCurrency.AD_Client_ID=PaymentRows.AD_Client_ID
    )
    WHERE
    (
        ABS(PaymentRows.PaymentOpenAmount-InvoiceRows.InvoiceOpenAmount)<=(SELECT QueryParameters.ExactTolerance FROM QueryParameters QueryParameters)
        OR ABS(PaymentRows.PaymentOpenAmount-InvoiceRows.InvoiceOpenAmount)*100/InvoiceRows.InvoiceOpenAmount<=(SELECT QueryParameters.MaximumDifferencePercentage FROM QueryParameters QueryParameters)
    )
),
CandidateScores AS
(
    SELECT
        CandidateMatches.*,
        50
        +CASE
            WHEN CandidateMatches.DifferenceAmount<=(SELECT QueryParameters.ExactTolerance FROM QueryParameters QueryParameters) THEN 30
            WHEN CandidateMatches.DifferencePercentage<=5 THEN 20
            ELSE 10
        END
        +CASE
            WHEN CandidateMatches.DateGapDays<=3 THEN 15
            WHEN CandidateMatches.DateGapDays<=7 THEN 10
            ELSE 5
        END
        +CASE
            WHEN CandidateMatches.ReferenceMatch=1 THEN 5
            ELSE 0
        END AS MatchScore,
        CASE WHEN CandidateMatches.InvoiceIsReturn='Y' THEN -1 ELSE 1 END
        *CASE
            WHEN CandidateMatches.PaymentCurrencyId=CandidateMatches.SchemaCurrencyId THEN CandidateMatches.ReadyAmount
            ELSE COALESCE(
                CurrencyConvert(
                    CandidateMatches.ReadyAmount,
                    CandidateMatches.PaymentCurrencyId,
                    CandidateMatches.SchemaCurrencyId,
                    CandidateMatches.PaymentDate,
                    CandidateMatches.PaymentConversionTypeId,
                    CandidateMatches.AD_Client_ID,
                    CandidateMatches.AD_Org_ID
                ),
                0
            )
        END AS AccountingAmount
    FROM CandidateMatches CandidateMatches
),
RankedMatches AS
(
    SELECT
        CandidateScores.*,
        ROW_NUMBER() OVER
        (
            PARTITION BY CandidateScores.C_Payment_ID
            ORDER BY CandidateScores.MatchScore DESC,CandidateScores.DifferencePercentage,CandidateScores.DateGapDays,CandidateScores.DueDate,CandidateScores.C_Invoice_ID,CandidateScores.C_InvoicePaySchedule_ID
        ) AS PaymentRank,
        ROW_NUMBER() OVER
        (
            PARTITION BY CandidateScores.C_InvoicePaySchedule_ID
            ORDER BY CandidateScores.MatchScore DESC,CandidateScores.DifferencePercentage,CandidateScores.DateGapDays,CandidateScores.PaymentDate,CandidateScores.C_Payment_ID
        ) AS ScheduleRank
    FROM CandidateScores CandidateScores
),
BestMatches AS
(
    SELECT
        RankedMatches.*,
        CASE
            WHEN RankedMatches.MatchScore>=(SELECT QueryParameters.HighConfidenceScore FROM QueryParameters QueryParameters) THEN 'HIGH'
            ELSE 'REVIEW'
        END AS MatchConfidence,
        CASE
            WHEN RankedMatches.MatchScore>=(SELECT QueryParameters.HighConfidenceScore FROM QueryParameters QueryParameters)
            AND RankedMatches.ScheduleRank=1
            AND RankedMatches.DifferenceAmount<=(SELECT QueryParameters.ExactTolerance FROM QueryParameters QueryParameters)
                THEN 'Y'
            ELSE 'N'
        END AS IsAutoApplicable
    FROM RankedMatches RankedMatches
    WHERE RankedMatches.PaymentRank=1
),
SummaryRows AS
(
    SELECT
        BestMatches.*,
        COUNT(1) OVER () AS TotalRecords,
        ROUND(
            " + CastNumberSql(@"
SUM(BestMatches.AccountingAmount) OVER ()
") + @",
            CAST(BestMatches.SchemaStdPrecision AS INTEGER)
        ) AS TotalAccountingAmount,
        SUM(
            CASE
                WHEN BestMatches.MatchConfidence='HIGH'
                AND BestMatches.IsAutoApplicable='Y' THEN 1
                ELSE 0
            END
        ) OVER () AS HighConfidenceCount
    FROM BestMatches BestMatches
),
NumberedRows AS
(
    SELECT
        SummaryRows.*,
        ROW_NUMBER() OVER
        (
            ORDER BY SummaryRows.MatchScore DESC,SummaryRows.DifferencePercentage,SummaryRows.DateGapDays,SummaryRows.PaymentDate DESC,SummaryRows.C_Payment_ID
        ) AS PageRowNo
    FROM SummaryRows SummaryRows
)
SELECT
    NumberedRows.C_Payment_ID,
    NumberedRows.C_Invoice_ID,
    NumberedRows.C_InvoicePaySchedule_ID,
    NumberedRows.C_BPartner_ID,
    NumberedRows.VendorName,
    NumberedRows.PaymentDocumentNo,
    NumberedRows.InvoiceDocumentNo,
    NumberedRows.PaymentDate,
    NumberedRows.InvoiceDate,
    NumberedRows.DueDate,
    NumberedRows.PaymentCurrencyId,
    NumberedRows.PaymentCurrencyISOCode,
    NumberedRows.PaymentCurrencySymbol,
    NumberedRows.PaymentPrecision,
    NumberedRows.PaymentAllocatedAmount,
    NumberedRows.PaymentOpenAmount,
    NumberedRows.InvoiceOriginalAmount,
    NumberedRows.InvoiceAllocatedAmount,
    NumberedRows.InvoiceOpenAmount,
    NumberedRows.ReadyAmount,
    NumberedRows.AccountingAmount,
    NumberedRows.DifferenceAmount,
    NumberedRows.DifferencePercentage,
    NumberedRows.DateGapDays,
    NumberedRows.ReferenceMatch,
    NumberedRows.MatchScore,
    NumberedRows.MatchConfidence,
    NumberedRows.IsAutoApplicable,
    NumberedRows.InvoiceIsReturn,
    NumberedRows.SchemaCurrencyId,
    NumberedRows.SchemaCurrencyISOCode,
    NumberedRows.SchemaCurrencySymbol,
    NumberedRows.SchemaStdPrecision,
    NumberedRows.TotalRecords,
    NumberedRows.TotalAccountingAmount,
    NumberedRows.HighConfidenceCount,
    NumberedRows.PageRowNo
FROM NumberedRows NumberedRows
WHERE NumberedRows.PageRowNo BETWEEN (SELECT QueryParameters.StartRow FROM QueryParameters QueryParameters) AND (SELECT QueryParameters.EndRow FROM QueryParameters QueryParameters)
ORDER BY NumberedRows.PageRowNo";

            sql = sql.Replace("/*PAYMENT_ACCESS_SQL*/", paymentAccessSql);
            sql = sql.Replace("/*INVOICE_ACCESS_SQL*/", invoiceAccessSql);

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@StartRow", Math.Max(1, startRow)),
                new SqlParameter("@EndRow", Math.Max(startRow, endRow)),
                new SqlParameter("@ExactTolerance", ExactTolerance),
                new SqlParameter("@MaximumDifferencePercentage", MaximumDifferencePercentage),
                new SqlParameter("@HighConfidenceScore", HighConfidenceScore)
            };


            try
            {
                reader = DB.ExecuteReader(
                    sql,
                    parameters,
                    null
                );

                while (reader != null && reader.Read())
                {
                    MatchListRow row = new MatchListRow
                    {
                        PaymentId = GetSafeInt(
                            reader,
                            "C_Payment_ID"
                        ),

                        InvoiceId = GetSafeInt(
                            reader,
                            "C_Invoice_ID"
                        ),

                        InvoicePayScheduleId = GetSafeInt(
                            reader,
                            "C_InvoicePaySchedule_ID"
                        ),

                        VendorId = GetSafeInt(
                            reader,
                            "C_BPartner_ID"
                        ),

                        VendorName = GetSafeString(
                            reader,
                            "VendorName"
                        ),

                        PaymentDocumentNo = GetSafeString(
                            reader,
                            "PaymentDocumentNo"
                        ),

                        InvoiceDocumentNo = GetSafeString(
                            reader,
                            "InvoiceDocumentNo"
                        ),

                        PaymentDate = GetSafeDateTime(
                            reader,
                            "PaymentDate"
                        ),

                        InvoiceDate = GetSafeDateTime(
                            reader,
                            "InvoiceDate"
                        ),

                        DueDate = GetSafeDateTime(
                            reader,
                            "DueDate"
                        ),

                        PaymentCurrencyId = GetSafeInt(
                            reader,
                            "PaymentCurrencyId"
                        ),

                        PaymentCurrencyISOCode = GetSafeString(
                            reader,
                            "PaymentCurrencyISOCode"
                        ),

                        PaymentCurrencySymbol = GetSafeString(
                            reader,
                            "PaymentCurrencySymbol"
                        ),

                        PaymentPrecision = NormalizePrecision(
                            GetSafeInt(
                                reader,
                                "PaymentPrecision",
                                2
                            )
                        ),

                        PaymentAllocatedAmount = GetSafeDecimal(
                            reader,
                            "PaymentAllocatedAmount"
                        ),

                        PaymentOpenAmount = GetSafeDecimal(
                            reader,
                            "PaymentOpenAmount"
                        ),

                        InvoiceOriginalAmount = GetSafeDecimal(
                            reader,
                            "InvoiceOriginalAmount"
                        ),

                        InvoiceAllocatedAmount = GetSafeDecimal(
                            reader,
                            "InvoiceAllocatedAmount"
                        ),

                        InvoiceOpenAmount = GetSafeDecimal(
                            reader,
                            "InvoiceOpenAmount"
                        ),

                        ReadyAmount = GetSafeDecimal(
                            reader,
                            "ReadyAmount"
                        ),

                        AccountingAmount = GetSafeDecimal(
                            reader,
                            "AccountingAmount"
                        ),

                        DifferenceAmount = GetSafeDecimal(
                            reader,
                            "DifferenceAmount"
                        ),

                        DifferencePercentage = GetSafeDecimal(
                            reader,
                            "DifferencePercentage"
                        ),

                        DateGapDays = GetSafeRoundedInt(
                            reader,
                            "DateGapDays"
                        ),

                        ReferenceMatch =
                            GetSafeDecimal(
                                reader,
                                "ReferenceMatch"
                            ) == 1M,

                        Score = GetSafeRoundedInt(
                            reader,
                            "MatchScore"
                        ),

                        Confidence = GetSafeString(
                            reader,
                            "MatchConfidence"
                        ),

                        IsAutoApplicable = string.Equals(
                            GetSafeString(
                                reader,
                                "IsAutoApplicable"
                            ),
                            "Y",
                            StringComparison.OrdinalIgnoreCase
                        ),

                        IsReturnCycle = string.Equals(
                            GetSafeString(
                                reader,
                                "InvoiceIsReturn"
                            ),
                            "Y",
                            StringComparison.OrdinalIgnoreCase
                        )
                    };

                    result.Rows.Add(row);

                    if (!result.SummaryLoaded)
                    {
                        result.SchemaCurrencyId =
                            GetSafeInt(
                                reader,
                                "SchemaCurrencyId"
                            );

                        result.SchemaCurrencyISOCode =
                            GetSafeString(
                                reader,
                                "SchemaCurrencyISOCode"
                            );

                        result.SchemaCurrencySymbol =
                            GetSafeString(
                                reader,
                                "SchemaCurrencySymbol"
                            );

                        result.SchemaStdPrecision =
                            NormalizePrecision(
                                GetSafeInt(
                                    reader,
                                    "SchemaStdPrecision",
                                    2
                                )
                            );

                        result.TotalRecords =
                            GetSafeRoundedInt(
                                reader,
                                "TotalRecords"
                            );

                        result.TotalAccountingAmount =
                            GetSafeDecimal(
                                reader,
                                "TotalAccountingAmount"
                            );

                        result.HighConfidenceCount =
                            GetSafeRoundedInt(
                                reader,
                                "HighConfidenceCount"
                            );

                        result.SummaryLoaded = true;
                    }
                }
            }
            catch (Exception ex)
            {
                VLogger.Get().SaveError(
                    "VAS_072_ReadMatchSuggestionList",
                    ex
                );

                throw;
            }
            finally
            {
                CloseReader(reader);
            }

            return result;
        }

        #endregion


        #region Popup Detail Query

        /// <summary>
        /// Dedicated query for one popup record only.
        /// Parameters: payment, invoice and pay-schedule IDs.
        /// No pagination and no zero sentinel parameters are used.
        /// </summary>

        private MatchDetailRow ReadMatchDetail(
    Ctx ctx,
    int paymentId,
    int invoiceId,
    int payScheduleId)
        {
            IDataReader reader = null;

            string textType = DB.IsOracle()
                ? "VARCHAR2(4000)"
                : "VARCHAR(4000)";

            /*
             * Apply MRole only to the main physical C_Payment table.
             */
            string paymentAccessSql = MRole.GetDefault(ctx).AddAccessSQL(
                @"
SELECT
    Payment.AD_Client_ID,
    Payment.AD_Org_ID,
    Payment.C_Payment_ID,
    Payment.C_BPartner_ID,
    Payment.DocumentNo,
    Payment.DateAcct,
    Payment.C_Currency_ID,
    Payment.PayAmt,
    Payment.VAS_UnAllocatedAmount,
    Payment.TrxNo,
    Payment.CheckNo,
    Payment.TenderType,
    Payment.C_BankAccount_ID,
    Payment.VA009_PaymentMethod_ID,
    Payment.C_DocType_ID
FROM C_Payment Payment
WHERE Payment.IsActive='Y'
AND Payment.Processed='Y'
AND Payment.IsReceipt='N'
AND Payment.DocStatus IN ('CO','CL')
AND COALESCE(Payment.IsAllocated,'N')='N'
AND Payment.C_CashLine_ID IS NULL
AND
(
    Payment.TenderType IS NULL
    OR Payment.TenderType<>'B'
)
AND Payment.AD_Client_ID=
(
    SELECT QueryParameters.AD_Client_ID
    FROM QueryParameters QueryParameters
)
AND Payment.C_Payment_ID=
(
    SELECT QueryParameters.C_Payment_ID
    FROM QueryParameters QueryParameters
)",
                "Payment",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            /*
             * Apply MRole only to the main physical C_Invoice table.
             */
            string invoiceAccessSql = MRole.GetDefault(ctx).AddAccessSQL(
                @"
SELECT
    Invoice.AD_Client_ID,
    Invoice.AD_Org_ID,
    Invoice.C_Invoice_ID,
    Invoice.C_BPartner_ID,
    Invoice.DocumentNo,
    Invoice.DateInvoiced,
    Invoice.C_Currency_ID,
    Invoice.GrandTotal,
    Invoice.GrandTotalAfterWithholding,
    Invoice.C_PaymentTerm_ID,
    Invoice.C_DocType_ID,
    Invoice.IsReturnTrx
FROM C_Invoice Invoice
WHERE Invoice.IsActive='Y'
AND Invoice.Processed='Y'
AND Invoice.IsSOTrx='N'
AND Invoice.DocStatus IN ('CO','CL')
AND COALESCE(Invoice.IsPaid,'N')='N'
AND Invoice.AD_Client_ID=
(
    SELECT QueryParameters.AD_Client_ID
    FROM QueryParameters QueryParameters
)
AND Invoice.C_Invoice_ID=
(
    SELECT QueryParameters.C_Invoice_ID
    FROM QueryParameters QueryParameters
)",
                "Invoice",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            string queryParametersFrom = DB.IsOracle()
                ? " FROM DUAL"
                : string.Empty;

            string sql = @"
WITH QueryParameters AS
(
    SELECT
        @AD_Client_ID AS AD_Client_ID,
        @AD_Language AS AD_Language,
        @C_Payment_ID AS C_Payment_ID,
        @C_Invoice_ID AS C_Invoice_ID,
        @C_InvoicePaySchedule_ID AS C_InvoicePaySchedule_ID,
        @ExactTolerance AS ExactTolerance,
        @HighConfidenceScore AS HighConfidenceScore"
        + queryParametersFrom + @"
),
TenderTypeReference AS
(
    SELECT DISTINCT
        CAST
        (
            RefList.Value AS " + textType + @"
        ) AS ReferenceValue,

        CAST
        (
            RefList.Name AS " + textType + @"
        ) AS BaseName,

        CAST
        (
            RefListTrl.Name AS " + textType + @"
        ) AS TranslatedName

    FROM AD_Table TableInfo

    INNER JOIN AD_Column ColumnInfo ON
    (
        ColumnInfo.AD_Table_ID=
        TableInfo.AD_Table_ID
    )

    INNER JOIN AD_Reference ReferenceInfo ON
    (
        ReferenceInfo.AD_Reference_ID=
        ColumnInfo.AD_Reference_Value_ID
    )

    INNER JOIN AD_Ref_List RefList ON
    (
        RefList.AD_Reference_ID=
        ReferenceInfo.AD_Reference_ID
    )

    LEFT OUTER JOIN AD_Ref_List_Trl RefListTrl ON
    (
        RefListTrl.AD_Ref_List_ID=
        RefList.AD_Ref_List_ID

        AND CAST
        (
            RefListTrl.AD_Language AS " + textType + @"
        )=
        CAST
        (
            (
                SELECT QueryParameters.AD_Language
                FROM QueryParameters QueryParameters
            ) AS " + textType + @"
        )
    )

    WHERE TableInfo.TableName='C_Payment'

    AND ColumnInfo.ColumnName='TenderType'

    AND TableInfo.IsActive='Y'

    AND ColumnInfo.IsActive='Y'

    AND ReferenceInfo.IsActive='Y'

    AND RefList.IsActive='Y'
),
AccessiblePayment AS
(
    /*PAYMENT_ACCESS_SQL*/
),
AccessibleInvoice AS
(
    /*INVOICE_ACCESS_SQL*/
),
PaymentAllocated AS
(
    SELECT
        AllocationLine.C_Payment_ID,

        SUM
        (
            ABS
            (
                COALESCE
                (
                    AllocationLine.Amount,
                    0
                )
            )
        ) AS AllocatedAmount

    FROM C_AllocationLine AllocationLine

    INNER JOIN C_AllocationHdr AllocationHeader ON
    (
        AllocationHeader.C_AllocationHdr_ID=
        AllocationLine.C_AllocationHdr_ID
    )

    WHERE AllocationHeader.IsActive='Y'

    AND AllocationHeader.DocStatus IN ('CO','CL')

    AND AllocationLine.C_Payment_ID=
    (
        SELECT QueryParameters.C_Payment_ID
        FROM QueryParameters QueryParameters
    )

    GROUP BY
        AllocationLine.C_Payment_ID
),
ScheduleAllocated AS
(
    SELECT
        AllocationLine.C_InvoicePaySchedule_ID,

        SUM
        (
            ABS
            (
                COALESCE
                (
                    AllocationLine.Amount,
                    0
                )
            )
        ) AS AllocatedAmount

    FROM C_AllocationLine AllocationLine

    INNER JOIN C_AllocationHdr AllocationHeader ON
    (
        AllocationHeader.C_AllocationHdr_ID=
        AllocationLine.C_AllocationHdr_ID
    )

    WHERE AllocationHeader.IsActive='Y'

    AND AllocationHeader.DocStatus IN ('CO','CL')

    AND AllocationLine.C_InvoicePaySchedule_ID=
    (
        SELECT QueryParameters.C_InvoicePaySchedule_ID
        FROM QueryParameters QueryParameters
    )

    GROUP BY
        AllocationLine.C_InvoicePaySchedule_ID
),
DetailBase AS
(
    SELECT
        Payment.C_Payment_ID,

        Payment.C_BPartner_ID AS PaymentVendorId,

        Invoice.C_BPartner_ID AS InvoiceVendorId,

        BusinessPartner.Name AS VendorName,

        Payment.DocumentNo AS PaymentDocumentNo,

        Payment.DateAcct AS PaymentDate,

        CAST
        (
            Payment.TenderType AS " + textType + @"
        ) AS PaymentMethodCode,

        TenderTypeReference.BaseName AS PaymentMethodBaseName,

        TenderTypeReference.TranslatedName AS PaymentMethodTranslatedName,

        VA009PaymentMethod.VA009_Name AS PaymentMethodVA009Name,

        Payment.C_DocType_ID AS PaymentDocTypeId,

        PaymentDocType.Name AS PaymentDocTypeName,

        Invoice.C_DocType_ID AS InvoiceDocTypeId,

        InvoiceDocType.Name AS InvoiceDocTypeName,

        COALESCE
        (
            Payment.TrxNo,
            Payment.CheckNo,
            Payment.DocumentNo
        ) AS ReferenceNo,

        Bank.Name AS BankName,

        BankAccount.AccountNo AS AccountNo,

        Payment.C_Currency_ID AS PaymentCurrencyId,

        PaymentCurrency.ISO_Code AS PaymentCurrencyISOCode,

        CASE
            WHEN PaymentCurrency.CurSymbol IS NOT NULL
            THEN PaymentCurrency.CurSymbol
            ELSE PaymentCurrency.ISO_Code
        END AS PaymentCurrencySymbol,

        COALESCE
        (
            PaymentCurrency.StdPrecision,
            2
        ) AS PaymentPrecision,

        ABS
        (
            COALESCE
            (
                Payment.PayAmt,
                0
            )
        ) AS PaymentOriginalAmount,

        COALESCE
        (
            PaymentAllocated.AllocatedAmount,
            0
        ) AS PaymentAllocatedAmount,

        CASE
            WHEN
            (
                ABS
                (
                    COALESCE
                    (
                        Payment.PayAmt,
                        0
                    )
                )
                -
                COALESCE
                (
                    PaymentAllocated.AllocatedAmount,
                    0
                )
            )<=0
            THEN 0

            WHEN ABS
            (
                COALESCE
                (
                    Payment.VAS_UnAllocatedAmount,
                    0
                )
            )>
            (
                SELECT QueryParameters.ExactTolerance
                FROM QueryParameters QueryParameters
            )

            AND ABS
            (
                COALESCE
                (
                    Payment.VAS_UnAllocatedAmount,
                    0
                )
            )<
            (
                ABS
                (
                    COALESCE
                    (
                        Payment.PayAmt,
                        0
                    )
                )
                -
                COALESCE
                (
                    PaymentAllocated.AllocatedAmount,
                    0
                )
            )
            THEN ABS
            (
                COALESCE
                (
                    Payment.VAS_UnAllocatedAmount,
                    0
                )
            )

            ELSE
            (
                ABS
                (
                    COALESCE
                    (
                        Payment.PayAmt,
                        0
                    )
                )
                -
                COALESCE
                (
                    PaymentAllocated.AllocatedAmount,
                    0
                )
            )
        END AS PaymentOpenAmount,

        Invoice.C_Invoice_ID,

        InvoicePaySchedule.C_InvoicePaySchedule_ID,

        Invoice.DocumentNo AS InvoiceDocumentNo,

        Invoice.DateInvoiced AS InvoiceDate,

        COALESCE
        (
            InvoicePaySchedule.DueDate,
            Invoice.DateInvoiced
        ) AS DueDate,

        PaymentTerm.Name AS PaymentTerms,

        Invoice.C_Currency_ID AS InvoiceCurrencyId,

        InvoiceCurrency.ISO_Code AS InvoiceCurrencyISOCode,

        CASE
            WHEN InvoiceCurrency.CurSymbol IS NOT NULL
            THEN InvoiceCurrency.CurSymbol
            ELSE InvoiceCurrency.ISO_Code
        END AS InvoiceCurrencySymbol,

        COALESCE
        (
            InvoiceCurrency.StdPrecision,
            2
        ) AS InvoicePrecision,

        ABS
        (
            COALESCE
            (
                Invoice.GrandTotalAfterWithholding,
                Invoice.GrandTotal,
                0
            )
        ) AS InvoiceOriginalAmount,

        COALESCE
        (
            ScheduleAllocated.AllocatedAmount,
            0
        ) AS InvoiceAllocatedAmount,

        CASE
            WHEN
            (
                ABS
                (
                    COALESCE
                    (
                        InvoicePaySchedule.DueAmt,
                        0
                    )
                )
                -
                COALESCE
                (
                    ScheduleAllocated.AllocatedAmount,
                    0
                )
            )>0
            THEN
            (
                ABS
                (
                    COALESCE
                    (
                        InvoicePaySchedule.DueAmt,
                        0
                    )
                )
                -
                COALESCE
                (
                    ScheduleAllocated.AllocatedAmount,
                    0
                )
            )

            ELSE 0
        END AS InvoiceOpenAmount,

        COALESCE
        (
            InvoiceDocType.IsReturnTrx,
            'N'
        ) AS InvoiceIsReturn

    FROM AccessiblePayment Payment

    INNER JOIN AccessibleInvoice Invoice ON
    (
        Invoice.C_BPartner_ID=
        Payment.C_BPartner_ID

        AND Invoice.C_Currency_ID=
        Payment.C_Currency_ID
    )

    INNER JOIN C_InvoicePaySchedule InvoicePaySchedule ON
    (
        InvoicePaySchedule.C_Invoice_ID=
        Invoice.C_Invoice_ID

        AND InvoicePaySchedule.C_InvoicePaySchedule_ID=
        (
            SELECT QueryParameters.C_InvoicePaySchedule_ID
            FROM QueryParameters QueryParameters
        )
    )

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

    INNER JOIN C_Currency InvoiceCurrency ON
    (
        InvoiceCurrency.C_Currency_ID=
        Invoice.C_Currency_ID
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

    LEFT OUTER JOIN C_PaymentTerm PaymentTerm ON
    (
        PaymentTerm.C_PaymentTerm_ID=
        Invoice.C_PaymentTerm_ID
    )

    LEFT OUTER JOIN PaymentAllocated PaymentAllocated ON
    (
        PaymentAllocated.C_Payment_ID=
        Payment.C_Payment_ID
    )

    LEFT OUTER JOIN ScheduleAllocated ScheduleAllocated ON
    (
        ScheduleAllocated.C_InvoicePaySchedule_ID=
        InvoicePaySchedule.C_InvoicePaySchedule_ID
    )

    LEFT OUTER JOIN TenderTypeReference TenderTypeReference ON
    (
        TenderTypeReference.ReferenceValue=
        CAST
        (
            Payment.TenderType AS " + textType + @"
        )
    )

    LEFT OUTER JOIN VA009_PaymentMethod VA009PaymentMethod ON
    (
        VA009PaymentMethod.VA009_PaymentMethod_ID=
        Payment.VA009_PaymentMethod_ID
    )

    LEFT OUTER JOIN C_DocType PaymentDocType ON
    (
        PaymentDocType.C_DocType_ID=
        Payment.C_DocType_ID

        AND PaymentDocType.IsActive='Y'
    )

    LEFT OUTER JOIN C_DocType InvoiceDocType ON
    (
        InvoiceDocType.C_DocType_ID=
        Invoice.C_DocType_ID

        AND InvoiceDocType.IsActive='Y'
    )

    WHERE InvoicePaySchedule.IsActive='Y'

    AND COALESCE
    (
        InvoicePaySchedule.IsHoldPayment,
        'N'
    )='N'
),
DetailCalculated AS
(
    SELECT
        DetailBase.*,

        CASE
            WHEN DetailBase.PaymentOpenAmount<=
                 DetailBase.InvoiceOpenAmount
            THEN DetailBase.PaymentOpenAmount

            ELSE DetailBase.InvoiceOpenAmount
        END AS ReadyAmount,

        ABS
        (
            DetailBase.PaymentOpenAmount
            -
            DetailBase.InvoiceOpenAmount
        ) AS DifferenceAmount,

        CASE
            WHEN DetailBase.InvoiceOpenAmount<=
            (
                SELECT QueryParameters.ExactTolerance
                FROM QueryParameters QueryParameters
            )
            THEN 100

            ELSE
            (
                ABS
                (
                    DetailBase.PaymentOpenAmount
                    -
                    DetailBase.InvoiceOpenAmount
                )
                *
                100
                /
                DetailBase.InvoiceOpenAmount
            )
        END AS DifferencePercentage,

        ABS
        (
            CAST
            (
                DetailBase.PaymentDate AS DATE
            )
            -
            CAST
            (
                DetailBase.DueDate AS DATE
            )
        ) AS DateGapDays,

        CASE
            WHEN DetailBase.ReferenceNo IS NOT NULL

            AND DetailBase.InvoiceDocumentNo IS NOT NULL

            AND UPPER
            (
                DetailBase.ReferenceNo
            ) LIKE
            (
                '%'
                ||
                UPPER
                (
                    DetailBase.InvoiceDocumentNo
                )
                ||
                '%'
            )
            THEN 1

            ELSE 0
        END AS ReferenceMatch

    FROM DetailBase DetailBase

    WHERE DetailBase.PaymentOpenAmount>
    (
        SELECT QueryParameters.ExactTolerance
        FROM QueryParameters QueryParameters
    )

    AND DetailBase.InvoiceOpenAmount>
    (
        SELECT QueryParameters.ExactTolerance
        FROM QueryParameters QueryParameters
    )
),
DetailScored AS
(
    SELECT
        DetailCalculated.*,

        50
        +
        CASE
            WHEN DetailCalculated.DifferenceAmount<=
            (
                SELECT QueryParameters.ExactTolerance
                FROM QueryParameters QueryParameters
            )
            THEN 30

            WHEN DetailCalculated.DifferencePercentage<=5
            THEN 20

            ELSE 10
        END
        +
        CASE
            WHEN DetailCalculated.DateGapDays<=3
            THEN 15

            WHEN DetailCalculated.DateGapDays<=7
            THEN 10

            ELSE 5
        END
        +
        CASE
            WHEN DetailCalculated.ReferenceMatch=1
            THEN 5

            ELSE 0
        END AS MatchScore

    FROM DetailCalculated DetailCalculated
)
SELECT
    DetailScored.C_Payment_ID,

    DetailScored.PaymentVendorId,

    DetailScored.InvoiceVendorId,

    DetailScored.VendorName,

    DetailScored.PaymentDocumentNo,

    DetailScored.PaymentDate,

    DetailScored.PaymentMethodCode,

    DetailScored.PaymentMethodBaseName,

    DetailScored.PaymentMethodTranslatedName,

    DetailScored.PaymentMethodVA009Name,

    DetailScored.PaymentDocTypeId,

    DetailScored.PaymentDocTypeName,

    DetailScored.InvoiceDocTypeId,

    DetailScored.InvoiceDocTypeName,

    DetailScored.ReferenceNo,

    DetailScored.BankName,

    DetailScored.AccountNo,

    DetailScored.PaymentCurrencyId,

    DetailScored.PaymentCurrencyISOCode,

    DetailScored.PaymentCurrencySymbol,

    DetailScored.PaymentPrecision,

    DetailScored.PaymentOriginalAmount,

    DetailScored.PaymentAllocatedAmount,

    DetailScored.PaymentOpenAmount,

    DetailScored.C_Invoice_ID,

    DetailScored.C_InvoicePaySchedule_ID,

    DetailScored.InvoiceDocumentNo,

    DetailScored.InvoiceDate,

    DetailScored.DueDate,

    DetailScored.PaymentTerms,

    DetailScored.InvoiceCurrencyId,

    DetailScored.InvoiceCurrencyISOCode,

    DetailScored.InvoiceCurrencySymbol,

    DetailScored.InvoicePrecision,

    DetailScored.InvoiceOriginalAmount,

    DetailScored.InvoiceAllocatedAmount,

    DetailScored.InvoiceOpenAmount,

    DetailScored.ReadyAmount,

    DetailScored.DifferenceAmount,

    DetailScored.DifferencePercentage,

    DetailScored.DateGapDays,

    DetailScored.ReferenceMatch,

    DetailScored.MatchScore,

    DetailScored.InvoiceIsReturn,

    CASE
        WHEN DetailScored.MatchScore>=
        (
            SELECT QueryParameters.HighConfidenceScore
            FROM QueryParameters QueryParameters
        )
        THEN 'HIGH'

        ELSE 'REVIEW'
    END AS MatchConfidence,

    CASE
        WHEN DetailScored.MatchScore>=
        (
            SELECT QueryParameters.HighConfidenceScore
            FROM QueryParameters QueryParameters
        )

        AND DetailScored.DifferenceAmount<=
        (
            SELECT QueryParameters.ExactTolerance
            FROM QueryParameters QueryParameters
        )
        THEN 'Y'

        ELSE 'N'
    END AS IsAutoApplicable

FROM DetailScored DetailScored";

            sql = sql.Replace(
                "/*PAYMENT_ACCESS_SQL*/",
                paymentAccessSql
            );

            sql = sql.Replace(
                "/*INVOICE_ACCESS_SQL*/",
                invoiceAccessSql
            );

            SqlParameter[] parameters = new SqlParameter[]
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
            "@C_Payment_ID",
            paymentId
        ),

        new SqlParameter(
            "@C_Invoice_ID",
            invoiceId
        ),

        new SqlParameter(
            "@C_InvoicePaySchedule_ID",
            payScheduleId
        ),

        new SqlParameter(
            "@ExactTolerance",
            ExactTolerance
        ),

        new SqlParameter(
            "@HighConfidenceScore",
            HighConfidenceScore
        )
            };

            try
            {
                reader = DB.ExecuteReader(
                    sql,
                    parameters,
                    null
                );

                if (reader != null && reader.Read())
                {
                    MatchDetailRow detailRow = new MatchDetailRow
                    {
                        PaymentId = GetSafeInt(
                            reader,
                            "C_Payment_ID"
                        ),

                        PaymentVendorId = GetSafeInt(
                            reader,
                            "PaymentVendorId"
                        ),

                        InvoiceVendorId = GetSafeInt(
                            reader,
                            "InvoiceVendorId"
                        ),

                        VendorId = GetSafeInt(
                            reader,
                            "PaymentVendorId"
                        ),

                        VendorName = GetSafeString(
                            reader,
                            "VendorName"
                        ),

                        PaymentDocumentNo = GetSafeString(
                            reader,
                            "PaymentDocumentNo"
                        ),

                        PaymentDate = GetSafeDateTime(
                            reader,
                            "PaymentDate"
                        ),

                        /*
                         * Original value stored in C_Payment.TenderType.
                         */
                        PaymentMethodCode = GetSafeString(
                            reader,
                            "PaymentMethodCode"
                        ),

                        /*
                         * Display value:
                         * 1. VA009 payment method name
                         * 2. translated tender-type name
                         * 3. base tender-type name
                         * 4. translated Not Specified message
                         * The raw stored code (for example 'K') is never
                         * shown to the user.
                         */
                        PaymentMethodName = FirstNotEmpty(
                            GetSafeString(
                                reader,
                                "PaymentMethodVA009Name"
                            ),

                            GetSafeString(
                                reader,
                                "PaymentMethodTranslatedName"
                            ),

                            GetSafeString(
                                reader,
                                "PaymentMethodBaseName"
                            ),

                            GetMsg(
                                ctx,
                                "VAS_072_NotSpecified",
                                "Not Specified"
                            )
                        ),

                        PaymentDocTypeName = FirstNotEmpty(
                            GetSafeString(
                                reader,
                                "PaymentDocTypeName"
                            ),

                            GetMsg(
                                ctx,
                                "VAS_072_NotSpecified",
                                "Not Specified"
                            )
                        ),

                        InvoiceDocTypeName = FirstNotEmpty(
                            GetSafeString(
                                reader,
                                "InvoiceDocTypeName"
                            ),

                            GetMsg(
                                ctx,
                                "VAS_072_NotSpecified",
                                "Not Specified"
                            )
                        ),

                        ReferenceNo = GetSafeString(
                            reader,
                            "ReferenceNo"
                        ),

                        BankName = GetSafeString(
                            reader,
                            "BankName"
                        ),

                        AccountNo = GetSafeString(
                            reader,
                            "AccountNo"
                        ),

                        PaymentCurrencyId = GetSafeInt(
                            reader,
                            "PaymentCurrencyId"
                        ),

                        PaymentCurrencyISOCode = GetSafeString(
                            reader,
                            "PaymentCurrencyISOCode"
                        ),

                        PaymentCurrencySymbol = GetSafeString(
                            reader,
                            "PaymentCurrencySymbol"
                        ),

                        PaymentPrecision = NormalizePrecision(
                            GetSafeInt(
                                reader,
                                "PaymentPrecision",
                                2
                            )
                        ),

                        PaymentOriginalAmount = GetSafeDecimal(
                            reader,
                            "PaymentOriginalAmount"
                        ),

                        PaymentAllocatedAmount = GetSafeDecimal(
                            reader,
                            "PaymentAllocatedAmount"
                        ),

                        PaymentOpenAmount = GetSafeDecimal(
                            reader,
                            "PaymentOpenAmount"
                        ),

                        InvoiceId = GetSafeInt(
                            reader,
                            "C_Invoice_ID"
                        ),

                        InvoicePayScheduleId = GetSafeInt(
                            reader,
                            "C_InvoicePaySchedule_ID"
                        ),

                        InvoiceDocumentNo = GetSafeString(
                            reader,
                            "InvoiceDocumentNo"
                        ),

                        InvoiceDate = GetSafeDateTime(
                            reader,
                            "InvoiceDate"
                        ),

                        DueDate = GetSafeDateTime(
                            reader,
                            "DueDate"
                        ),

                        PaymentTerms = GetSafeString(
                            reader,
                            "PaymentTerms"
                        ),

                        InvoiceCurrencyId = GetSafeInt(
                            reader,
                            "InvoiceCurrencyId"
                        ),

                        InvoiceCurrencyISOCode = GetSafeString(
                            reader,
                            "InvoiceCurrencyISOCode"
                        ),

                        InvoiceCurrencySymbol = GetSafeString(
                            reader,
                            "InvoiceCurrencySymbol"
                        ),

                        InvoicePrecision = NormalizePrecision(
                            GetSafeInt(
                                reader,
                                "InvoicePrecision",
                                2
                            )
                        ),

                        InvoiceOriginalAmount = GetSafeDecimal(
                            reader,
                            "InvoiceOriginalAmount"
                        ),

                        InvoiceAllocatedAmount = GetSafeDecimal(
                            reader,
                            "InvoiceAllocatedAmount"
                        ),

                        InvoiceOpenAmount = GetSafeDecimal(
                            reader,
                            "InvoiceOpenAmount"
                        ),

                        ReadyAmount = GetSafeDecimal(
                            reader,
                            "ReadyAmount"
                        ),

                        DifferenceAmount = GetSafeDecimal(
                            reader,
                            "DifferenceAmount"
                        ),

                        DifferencePercentage = GetSafeDecimal(
                            reader,
                            "DifferencePercentage"
                        ),

                        DateGapDays = GetSafeRoundedInt(
                            reader,
                            "DateGapDays"
                        ),

                        ReferenceMatch =
                            GetSafeDecimal(
                                reader,
                                "ReferenceMatch"
                            ) == 1M,

                        Score = GetSafeRoundedInt(
                            reader,
                            "MatchScore"
                        ),

                        Confidence = GetSafeString(
                            reader,
                            "MatchConfidence"
                        ),

                        IsAutoApplicable = string.Equals(
                            GetSafeString(
                                reader,
                                "IsAutoApplicable"
                            ),
                            "Y",
                            StringComparison.OrdinalIgnoreCase
                        ),

                        IsReturnCycle = string.Equals(
                            GetSafeString(
                                reader,
                                "InvoiceIsReturn"
                            ),
                            "Y",
                            StringComparison.OrdinalIgnoreCase
                        )
                    };

                    return detailRow;
                }
            }
            catch (Exception ex)
            {
                VLogger.Get().SaveError(
                    "VAS_072_ReadMatchDetail",
                    ex
                );

                throw;
            }
            finally
            {
                CloseReader(reader);
            }

            return null;
        }


        #endregion

        #region High Confidence Query

        /// <summary>
        /// Dedicated query for ApplyHighConfidence only.
        /// It returns only the IDs required by the allocation process.
        /// </summary>
        /// <summary>
        /// Returns all high-confidence matches using the same matching logic
        /// as ReadMatchSuggestionList, but without pagination.
        /// </summary>
        private List<HighConfidenceCandidateRow> ReadHighConfidenceCandidates(
            Ctx ctx)
        {
            List<HighConfidenceCandidateRow> rows =
                new List<HighConfidenceCandidateRow>();

            IDataReader reader = null;

            string paymentAccessSql = MRole.GetDefault(ctx).AddAccessSQL(
                @"
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
    Payment.VAS_UnAllocatedAmount,
    Payment.TrxNo,
    Payment.CheckNo,
    Payment.TenderType
FROM C_Payment Payment
WHERE Payment.IsActive = 'Y'
AND Payment.Processed = 'Y'
AND Payment.IsReceipt = 'N'
AND Payment.DocStatus IN ('CO','CL')
AND COALESCE(Payment.IsAllocated,'N') = 'N'
AND Payment.C_BPartner_ID IS NOT NULL
AND Payment.C_CashLine_ID IS NULL
AND
(
    Payment.TenderType IS NULL
    OR Payment.TenderType <> 'B'
)
AND Payment.AD_Client_ID =
(
    SELECT QueryParameters.AD_Client_ID
    FROM QueryParameters QueryParameters
)",
                "Payment",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            string invoiceAccessSql = MRole.GetDefault(ctx).AddAccessSQL(
                @"
SELECT
    Invoice.AD_Client_ID,
    Invoice.AD_Org_ID,
    Invoice.C_Invoice_ID,
    Invoice.C_BPartner_ID,
    Invoice.DocumentNo,
    Invoice.DateInvoiced,
    Invoice.C_Currency_ID,
    Invoice.GrandTotal,
    Invoice.GrandTotalAfterWithholding,
    Invoice.IsReturnTrx
FROM C_Invoice Invoice
WHERE Invoice.IsActive = 'Y'
AND Invoice.Processed = 'Y'
AND Invoice.IsSOTrx = 'N'
AND Invoice.DocStatus IN ('CO','CL')
AND COALESCE(Invoice.IsPaid,'N') = 'N'
AND Invoice.C_BPartner_ID IS NOT NULL
AND Invoice.AD_Client_ID =
(
    SELECT QueryParameters.AD_Client_ID
    FROM QueryParameters QueryParameters
)",
                "Invoice",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            string queryParametersFrom = DB.IsOracle()
                ? " FROM DUAL"
                : string.Empty;

            string sql = @"
WITH QueryParameters AS
(
    SELECT
        @AD_Client_ID AS AD_Client_ID,
        @ExactTolerance AS ExactTolerance,
        @HighConfidenceScore AS HighConfidenceScore"
                + queryParametersFrom + @"
),
AccessiblePayments AS
(
    /*PAYMENT_ACCESS_SQL*/
),
AccessibleInvoices AS
(
    /*INVOICE_ACCESS_SQL*/
),
PaymentAllocated AS
(
    SELECT
        AllocationLine.C_Payment_ID,

        SUM
        (
            ABS
            (
                COALESCE
                (
                    AllocationLine.Amount,
                    0
                )
            )
        ) AS AllocatedAmount

    FROM C_AllocationLine AllocationLine

    INNER JOIN C_AllocationHdr AllocationHeader ON
    (
        AllocationHeader.C_AllocationHdr_ID =
        AllocationLine.C_AllocationHdr_ID
    )

    WHERE AllocationHeader.IsActive = 'Y'

    AND AllocationHeader.DocStatus IN
    (
        'CO',
        'CL'
    )

    AND AllocationHeader.AD_Client_ID =
    (
        SELECT QueryParameters.AD_Client_ID
        FROM QueryParameters QueryParameters
    )

    AND AllocationLine.C_Payment_ID IS NOT NULL

    GROUP BY
        AllocationLine.C_Payment_ID
),
ScheduleAllocated AS
(
    SELECT
        AllocationLine.C_InvoicePaySchedule_ID,

        SUM
        (
            ABS
            (
                COALESCE
                (
                    AllocationLine.Amount,
                    0
                )
            )
        ) AS AllocatedAmount

    FROM C_AllocationLine AllocationLine

    INNER JOIN C_AllocationHdr AllocationHeader ON
    (
        AllocationHeader.C_AllocationHdr_ID =
        AllocationLine.C_AllocationHdr_ID
    )

    WHERE AllocationHeader.IsActive = 'Y'

    AND AllocationHeader.DocStatus IN
    (
        'CO',
        'CL'
    )

    AND AllocationHeader.AD_Client_ID =
    (
        SELECT QueryParameters.AD_Client_ID
        FROM QueryParameters QueryParameters
    )

    AND AllocationLine.C_InvoicePaySchedule_ID IS NOT NULL

    GROUP BY
        AllocationLine.C_InvoicePaySchedule_ID
),
PaymentRowsBase AS
(
    SELECT
        Payment.AD_Client_ID,
        Payment.AD_Org_ID,
        Payment.C_Payment_ID,
        Payment.C_BPartner_ID,
        Payment.DocumentNo AS PaymentDocumentNo,
        Payment.DateAcct AS PaymentDate,

        Payment.C_Currency_ID AS PaymentCurrencyId,

        COALESCE
        (
            Payment.C_ConversionType_ID,
            0
        ) AS PaymentConversionTypeId,

        COALESCE
        (
            PaymentAllocated.AllocatedAmount,
            0
        ) AS PaymentAllocatedAmount,

        CASE
            WHEN
                ABS
                (
                    COALESCE
                    (
                        Payment.PayAmt,
                        0
                    )
                )
                -
                COALESCE
                (
                    PaymentAllocated.AllocatedAmount,
                    0
                )
                <= 0
            THEN 0

            WHEN
                ABS
                (
                    COALESCE
                    (
                        Payment.VAS_UnAllocatedAmount,
                        0
                    )
                )
                >
                (
                    SELECT QueryParameters.ExactTolerance
                    FROM QueryParameters QueryParameters
                )

                AND ABS
                (
                    COALESCE
                    (
                        Payment.VAS_UnAllocatedAmount,
                        0
                    )
                )
                <
                ABS
                (
                    COALESCE
                    (
                        Payment.PayAmt,
                        0
                    )
                )
                -
                COALESCE
                (
                    PaymentAllocated.AllocatedAmount,
                    0
                )

            THEN ABS
            (
                COALESCE
                (
                    Payment.VAS_UnAllocatedAmount,
                    0
                )
            )

            ELSE
                ABS
                (
                    COALESCE
                    (
                        Payment.PayAmt,
                        0
                    )
                )
                -
                COALESCE
                (
                    PaymentAllocated.AllocatedAmount,
                    0
                )
        END AS PaymentOpenAmount,

        COALESCE
        (
            Payment.TrxNo,
            Payment.CheckNo,
            Payment.DocumentNo
        ) AS ReferenceNo

    FROM AccessiblePayments Payment

    LEFT OUTER JOIN PaymentAllocated PaymentAllocated ON
    (
        PaymentAllocated.C_Payment_ID =
        Payment.C_Payment_ID
    )
),
PaymentRows AS
(
    SELECT
        PaymentRowsBase.*,

        ROW_NUMBER() OVER
        (
            PARTITION BY
                PaymentRowsBase.C_BPartner_ID,
                PaymentRowsBase.PaymentCurrencyId,
                PaymentRowsBase.PaymentOpenAmount

            ORDER BY
                PaymentRowsBase.PaymentDate,
                PaymentRowsBase.C_Payment_ID
        ) AS MatchSequence

    FROM PaymentRowsBase PaymentRowsBase

    WHERE PaymentRowsBase.PaymentOpenAmount >
    (
        SELECT QueryParameters.ExactTolerance
        FROM QueryParameters QueryParameters
    )
),
InvoiceRowsBase AS
(
    SELECT
        Invoice.C_Invoice_ID,
        Invoice.C_BPartner_ID,
        Invoice.DocumentNo AS InvoiceDocumentNo,
        Invoice.DateInvoiced AS InvoiceDate,

        COALESCE
        (
            InvoicePaySchedule.DueDate,
            Invoice.DateInvoiced
        ) AS DueDate,

        Invoice.C_Currency_ID AS InvoiceCurrencyId,
        COALESCE(Invoice.IsReturnTrx,'N') AS InvoiceIsReturn,
        InvoicePaySchedule.C_InvoicePaySchedule_ID,

        CASE
            WHEN
                ABS
                (
                    COALESCE
                    (
                        InvoicePaySchedule.DueAmt,
                        0
                    )
                )
                -
                COALESCE
                (
                    ScheduleAllocated.AllocatedAmount,
                    0
                )
                > 0

            THEN
                ABS
                (
                    COALESCE
                    (
                        InvoicePaySchedule.DueAmt,
                        0
                    )
                )
                -
                COALESCE
                (
                    ScheduleAllocated.AllocatedAmount,
                    0
                )

            ELSE 0
        END AS InvoiceOpenAmount

    FROM AccessibleInvoices Invoice

    INNER JOIN C_InvoicePaySchedule InvoicePaySchedule ON
    (
        InvoicePaySchedule.C_Invoice_ID =
        Invoice.C_Invoice_ID
    )

    LEFT OUTER JOIN ScheduleAllocated ScheduleAllocated ON
    (
        ScheduleAllocated.C_InvoicePaySchedule_ID =
        InvoicePaySchedule.C_InvoicePaySchedule_ID
    )

    WHERE InvoicePaySchedule.IsActive = 'Y'

    AND COALESCE
    (
        InvoicePaySchedule.IsHoldPayment,
        'N'
    ) = 'N'
),
InvoiceRows AS
(
    SELECT
        InvoiceRowsBase.*,

        ROW_NUMBER() OVER
        (
            PARTITION BY
                InvoiceRowsBase.C_BPartner_ID,
                InvoiceRowsBase.InvoiceCurrencyId,
                InvoiceRowsBase.InvoiceOpenAmount

            ORDER BY
                InvoiceRowsBase.DueDate,
                InvoiceRowsBase.C_Invoice_ID,
                InvoiceRowsBase.C_InvoicePaySchedule_ID
        ) AS MatchSequence

    FROM InvoiceRowsBase InvoiceRowsBase

    WHERE InvoiceRowsBase.InvoiceOpenAmount >
    (
        SELECT QueryParameters.ExactTolerance
        FROM QueryParameters QueryParameters
    )
),
CandidateMatches AS
(
    SELECT
        PaymentRows.C_Payment_ID,
        InvoiceRows.C_Invoice_ID,
        InvoiceRows.C_InvoicePaySchedule_ID,

        ABS
        (
            PaymentRows.PaymentOpenAmount
            -
            InvoiceRows.InvoiceOpenAmount
        ) AS DifferenceAmount,

        CASE
            WHEN InvoiceRows.InvoiceOpenAmount <=
            (
                SELECT QueryParameters.ExactTolerance
                FROM QueryParameters QueryParameters
            )
            THEN 100

            ELSE
                ABS
                (
                    PaymentRows.PaymentOpenAmount
                    -
                    InvoiceRows.InvoiceOpenAmount
                )
                *
                100
                /
                InvoiceRows.InvoiceOpenAmount
        END AS DifferencePercentage,

        ABS
        (
            CAST(PaymentRows.PaymentDate AS DATE)
            -
            CAST(InvoiceRows.DueDate AS DATE)
        ) AS DateGapDays,

        CASE
            WHEN PaymentRows.ReferenceNo IS NOT NULL
            AND InvoiceRows.InvoiceDocumentNo IS NOT NULL

            AND UPPER(PaymentRows.ReferenceNo) LIKE
                '%' ||
                UPPER(InvoiceRows.InvoiceDocumentNo) ||
                '%'

            THEN 1

            ELSE 0
        END AS ReferenceMatch

    FROM PaymentRows PaymentRows

    INNER JOIN InvoiceRows InvoiceRows ON
    (
        InvoiceRows.C_BPartner_ID =
        PaymentRows.C_BPartner_ID

        AND InvoiceRows.InvoiceCurrencyId =
        PaymentRows.PaymentCurrencyId

        AND InvoiceRows.MatchSequence =
        PaymentRows.MatchSequence
    )

    WHERE ABS
    (
        PaymentRows.PaymentOpenAmount
        -
        InvoiceRows.InvoiceOpenAmount
    )
    <=
    (
        SELECT QueryParameters.ExactTolerance
        FROM QueryParameters QueryParameters
    )
),
ScoredMatches AS
(
    SELECT
        CandidateMatches.*,

        50

        + CASE
            WHEN CandidateMatches.DifferenceAmount <=
            (
                SELECT QueryParameters.ExactTolerance
                FROM QueryParameters QueryParameters
            )
            THEN 30

            ELSE 10
          END

        + CASE
            WHEN CandidateMatches.DateGapDays <= 3
            THEN 15

            WHEN CandidateMatches.DateGapDays <= 7
            THEN 10

            ELSE 5
          END

        + CASE
            WHEN CandidateMatches.ReferenceMatch = 1
            THEN 5

            ELSE 0
          END AS MatchScore

    FROM CandidateMatches CandidateMatches
),
HighConfidenceMatches AS
(
    SELECT
        ScoredMatches.*

    FROM ScoredMatches ScoredMatches

    WHERE ScoredMatches.MatchScore >=
    (
        SELECT QueryParameters.HighConfidenceScore
        FROM QueryParameters QueryParameters
    )

    AND ScoredMatches.DifferenceAmount <=
    (
        SELECT QueryParameters.ExactTolerance
        FROM QueryParameters QueryParameters
    )
)
SELECT
    HighConfidenceMatches.C_Payment_ID,
    HighConfidenceMatches.C_Invoice_ID,
    HighConfidenceMatches.C_InvoicePaySchedule_ID

FROM HighConfidenceMatches HighConfidenceMatches

ORDER BY
    HighConfidenceMatches.MatchScore DESC,
    HighConfidenceMatches.DifferenceAmount,
    HighConfidenceMatches.DateGapDays,
    HighConfidenceMatches.C_Payment_ID,
    HighConfidenceMatches.C_Invoice_ID,
    HighConfidenceMatches.C_InvoicePaySchedule_ID";

            sql = sql.Replace(
                "/*PAYMENT_ACCESS_SQL*/",
                paymentAccessSql
            );

            sql = sql.Replace(
                "/*INVOICE_ACCESS_SQL*/",
                invoiceAccessSql
            );

            SqlParameter[] parameters = new SqlParameter[]
            {
        new SqlParameter(
            "@AD_Client_ID",
            ctx.GetAD_Client_ID()
        ),

        new SqlParameter(
            "@ExactTolerance",
            ExactTolerance
        ),

        new SqlParameter(
            "@HighConfidenceScore",
            HighConfidenceScore
        )
            };

            try
            {
                reader = DB.ExecuteReader(
                    sql,
                    parameters,
                    null
                );

                while (
                    reader != null &&
                    reader.Read()
                )
                {
                    int paymentId = GetSafeInt(
                        reader,
                        "C_Payment_ID"
                    );

                    int invoiceId = GetSafeInt(
                        reader,
                        "C_Invoice_ID"
                    );

                    int invoicePayScheduleId = GetSafeInt(
                        reader,
                        "C_InvoicePaySchedule_ID"
                    );

                    if (
                        paymentId <= 0 ||
                        invoiceId <= 0 ||
                        invoicePayScheduleId <= 0
                    )
                    {
                        continue;
                    }

                    rows.Add(
                        new HighConfidenceCandidateRow
                        {
                            PaymentId = paymentId,
                            InvoiceId = invoiceId,
                            InvoicePayScheduleId =
                                invoicePayScheduleId
                        }
                    );
                }
            }
            catch (Exception ex)
            {
                VLogger.Get().SaveError(
                    "VAS_072_ReadHighConfidenceCandidates",
                    ex
                );

                throw;
            }
            finally
            {
                CloseReader(reader);
            }

            return rows;
        }
        #endregion

        #region Allocation Validation Query

        /// <summary>
        /// Dedicated transaction-time validation query.
        /// It checks only the selected payment/invoice/schedule and returns
        /// the current unallocated amounts. It does not execute ranking,
        /// pagination, confidence scoring or widget summary logic.
        /// </summary>
        private AllocationValidationRow ReadAllocationValidation(
            Ctx ctx,
            int paymentId,
            int invoiceId,
            int payScheduleId,
            Trx trx)
        {
            IDataReader reader = null;

            string paymentAccessSql = MRole.GetDefault(ctx).AddAccessSQL(
                @"
SELECT
    Payment.AD_Client_ID,
    Payment.AD_Org_ID,
    Payment.C_Payment_ID,
    Payment.C_BPartner_ID,
    Payment.C_Currency_ID,
    Payment.PayAmt,
    Payment.VAS_UnAllocatedAmount
FROM C_Payment Payment
WHERE Payment.IsActive='Y'
AND Payment.Processed='Y'
AND Payment.IsReceipt='N'
AND Payment.DocStatus IN ('CO','CL')
AND COALESCE(Payment.IsAllocated,'N')='N'
AND Payment.AD_Client_ID=(SELECT QueryParameters.AD_Client_ID FROM QueryParameters QueryParameters)
AND Payment.C_Payment_ID=(SELECT QueryParameters.C_Payment_ID FROM QueryParameters QueryParameters)",
                "Payment",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            string invoiceAccessSql = MRole.GetDefault(ctx).AddAccessSQL(
                @"
SELECT
    Invoice.AD_Client_ID,
    Invoice.AD_Org_ID,
    Invoice.C_Invoice_ID,
    Invoice.C_BPartner_ID,
    Invoice.C_Currency_ID
FROM C_Invoice Invoice
WHERE Invoice.IsActive='Y'
AND Invoice.Processed='Y'
AND Invoice.IsSOTrx='N'
AND Invoice.DocStatus IN ('CO','CL')
AND COALESCE(Invoice.IsPaid,'N')='N'
AND Invoice.AD_Client_ID=(SELECT QueryParameters.AD_Client_ID FROM QueryParameters QueryParameters)
AND Invoice.C_Invoice_ID=(SELECT QueryParameters.C_Invoice_ID FROM QueryParameters QueryParameters)",
                "Invoice",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            string queryParametersFrom = DB.IsOracle()
                ? " FROM DUAL"
                : string.Empty;

            string sql = @"
WITH QueryParameters AS
(
    SELECT
        @AD_Client_ID AS AD_Client_ID,
        @C_Payment_ID AS C_Payment_ID,
        @C_Invoice_ID AS C_Invoice_ID,
        @C_InvoicePaySchedule_ID AS C_InvoicePaySchedule_ID,
        @ExactTolerance AS ExactTolerance"
+ queryParametersFrom + @"
),
AccessiblePayment AS
(
    /*PAYMENT_ACCESS_SQL*/
),
AccessibleInvoice AS
(
    /*INVOICE_ACCESS_SQL*/
),
PaymentAllocated AS
(
    SELECT
        AllocationLine.C_Payment_ID,
        SUM(ABS(COALESCE(AllocationLine.Amount,0))) AS AllocatedAmount
    FROM C_AllocationLine AllocationLine
    INNER JOIN C_AllocationHdr AllocationHeader ON
    (
        AllocationHeader.C_AllocationHdr_ID=AllocationLine.C_AllocationHdr_ID
    )
    WHERE AllocationHeader.IsActive='Y'
    AND AllocationHeader.DocStatus IN ('CO','CL')
    AND AllocationLine.C_Payment_ID=(SELECT QueryParameters.C_Payment_ID FROM QueryParameters QueryParameters)
    GROUP BY AllocationLine.C_Payment_ID
),
ScheduleAllocated AS
(
    SELECT
        AllocationLine.C_InvoicePaySchedule_ID,
        SUM(ABS(COALESCE(AllocationLine.Amount,0))) AS AllocatedAmount
    FROM C_AllocationLine AllocationLine
    INNER JOIN C_AllocationHdr AllocationHeader ON
    (
        AllocationHeader.C_AllocationHdr_ID=AllocationLine.C_AllocationHdr_ID
    )
    WHERE AllocationHeader.IsActive='Y'
    AND AllocationHeader.DocStatus IN ('CO','CL')
    AND AllocationLine.C_InvoicePaySchedule_ID=(SELECT QueryParameters.C_InvoicePaySchedule_ID FROM QueryParameters QueryParameters)
    GROUP BY AllocationLine.C_InvoicePaySchedule_ID
),
PaymentRow AS
(
    SELECT
        Payment.C_Payment_ID,
        Payment.C_BPartner_ID,
        Payment.C_Currency_ID,
        CASE
            WHEN ABS(COALESCE(Payment.PayAmt,0))-COALESCE(PaymentAllocated.AllocatedAmount,0)<=0 THEN 0
            WHEN ABS(COALESCE(Payment.VAS_UnAllocatedAmount,0))>(SELECT QueryParameters.ExactTolerance FROM QueryParameters QueryParameters)
            AND ABS(COALESCE(Payment.VAS_UnAllocatedAmount,0))<ABS(COALESCE(Payment.PayAmt,0))-COALESCE(PaymentAllocated.AllocatedAmount,0)
                THEN ABS(COALESCE(Payment.VAS_UnAllocatedAmount,0))
            ELSE ABS(COALESCE(Payment.PayAmt,0))-COALESCE(PaymentAllocated.AllocatedAmount,0)
        END AS PaymentOpenAmount
    FROM AccessiblePayment Payment
    LEFT OUTER JOIN PaymentAllocated PaymentAllocated ON
    (
        PaymentAllocated.C_Payment_ID=Payment.C_Payment_ID
    )
),
InvoiceRow AS
(
    SELECT
        Invoice.C_Invoice_ID,
        Invoice.C_BPartner_ID,
        Invoice.C_Currency_ID,
        InvoicePaySchedule.C_InvoicePaySchedule_ID,
        CASE
            WHEN ABS(COALESCE(InvoicePaySchedule.DueAmt,0))-COALESCE(ScheduleAllocated.AllocatedAmount,0)>0
                THEN ABS(COALESCE(InvoicePaySchedule.DueAmt,0))-COALESCE(ScheduleAllocated.AllocatedAmount,0)
            ELSE 0
        END AS InvoiceOpenAmount
    FROM AccessibleInvoice Invoice
    INNER JOIN C_InvoicePaySchedule InvoicePaySchedule ON
    (
        InvoicePaySchedule.C_Invoice_ID=Invoice.C_Invoice_ID
        AND InvoicePaySchedule.C_InvoicePaySchedule_ID=(SELECT QueryParameters.C_InvoicePaySchedule_ID FROM QueryParameters QueryParameters)
    )
    LEFT OUTER JOIN ScheduleAllocated ScheduleAllocated ON
    (
        ScheduleAllocated.C_InvoicePaySchedule_ID=InvoicePaySchedule.C_InvoicePaySchedule_ID
    )
    WHERE InvoicePaySchedule.IsActive='Y'
    AND COALESCE(InvoicePaySchedule.IsHoldPayment,'N')='N'
)
SELECT
    PaymentRow.C_Payment_ID,
    InvoiceRow.C_Invoice_ID,
    InvoiceRow.C_InvoicePaySchedule_ID,
    PaymentRow.C_BPartner_ID,
    PaymentRow.C_Currency_ID,
    PaymentRow.PaymentOpenAmount,
    InvoiceRow.InvoiceOpenAmount
FROM PaymentRow PaymentRow
INNER JOIN InvoiceRow InvoiceRow ON
(
    InvoiceRow.C_BPartner_ID=PaymentRow.C_BPartner_ID
    AND InvoiceRow.C_Currency_ID=PaymentRow.C_Currency_ID
)
WHERE PaymentRow.PaymentOpenAmount>(SELECT QueryParameters.ExactTolerance FROM QueryParameters QueryParameters)
AND InvoiceRow.InvoiceOpenAmount>(SELECT QueryParameters.ExactTolerance FROM QueryParameters QueryParameters)";

            sql = sql.Replace("/*PAYMENT_ACCESS_SQL*/", paymentAccessSql);
            sql = sql.Replace("/*INVOICE_ACCESS_SQL*/", invoiceAccessSql);

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@C_Payment_ID", paymentId),
                new SqlParameter("@C_Invoice_ID", invoiceId),
                new SqlParameter("@C_InvoicePaySchedule_ID", payScheduleId),
                new SqlParameter("@ExactTolerance", ExactTolerance)
            };

            try
            {
                reader = DB.ExecuteReader(sql, parameters, trx);

                if (reader != null && reader.Read())
                {
                    return new AllocationValidationRow
                    {
                        PaymentId = GetInt(reader, "C_Payment_ID"),
                        InvoiceId = GetInt(reader, "C_Invoice_ID"),
                        InvoicePayScheduleId = GetInt(reader, "C_InvoicePaySchedule_ID"),
                        VendorId = GetInt(reader, "C_BPartner_ID"),
                        CurrencyId = GetInt(reader, "C_Currency_ID"),
                        PaymentOpenAmount = GetDecimal(reader, "PaymentOpenAmount"),
                        InvoiceOpenAmount = GetDecimal(reader, "InvoiceOpenAmount")
                    };
                }
            }
            finally
            {
                CloseReader(reader);
            }

            return null;
        }

        #endregion

        #region Allocation Process

        private ApplyResult ApplyMatchAllocation(
            Ctx ctx,
            int paymentId,
            int invoiceId,
            int payScheduleId)
        {
            ApplyResult result = new ApplyResult();

            if (
                ctx == null ||
                paymentId <= 0 ||
                invoiceId <= 0 ||
                payScheduleId <= 0
            )
            {
                result.Message = GetMsg(
                    ctx,
                    "FillMandatory",
                    "Mandatory values are missing"
                );
                return result;
            }

            Trx trx = Trx.GetTrx(
                Trx.CreateTrxName("VAS072ALLOC")
            );

            try
            {
                AllocationValidationRow validation =
                    ReadAllocationValidation(
                        ctx,
                        paymentId,
                        invoiceId,
                        payScheduleId,
                        trx
                    );

                if (validation == null)
                {
                    trx.Rollback();
                    result.Message = GetMsg(
                        ctx,
                        "VAS_072_DetailNotFound",
                        "The selected match is no longer available"
                    );
                    return result;
                }

                MPayment payment = new MPayment(ctx, paymentId, trx);
                MInvoice invoice = new MInvoice(ctx, invoiceId, trx);

                if (
                    payment.Get_ID() <= 0 ||
                    invoice.Get_ID() <= 0 ||
                    payment.IsReceipt() ||
                    invoice.IsSOTrx() ||
                    !IsCompletedStatus(payment.GetDocStatus()) ||
                    !IsCompletedStatus(invoice.GetDocStatus())
                )
                {
                    trx.Rollback();
                    result.Message = GetMsg(
                        ctx,
                        "VIS_NoRecordFound",
                        "AP payment or purchase invoice was not found"
                    );
                    return result;
                }

                if (payment.IsAllocated())
                {
                    trx.Rollback();
                    result.Message = GetMsg(
                        ctx,
                        "PaymentIsAllocated",
                        "Payment is already allocated"
                    );
                    return result;
                }

                if (payment.GetC_BPartner_ID() != invoice.GetC_BPartner_ID())
                {
                    trx.Rollback();
                    result.Message = GetMsg(
                        ctx,
                        "VAS_072_DifferentBusinessPartner",
                        "Payment and invoice belong to different vendors"
                    );
                    return result;
                }

                if (payment.GetC_Currency_ID() != invoice.GetC_Currency_ID())
                {
                    trx.Rollback();
                    result.Message = GetMsg(
                        ctx,
                        "VAS_072_DifferentCurrency",
                        "Payment and invoice currencies do not match"
                    );
                    return result;
                }

                decimal availableAmount = Math.Abs(
                    validation.PaymentOpenAmount
                );
                decimal invoiceOpenAmount = Math.Abs(
                    validation.InvoiceOpenAmount
                );
                decimal appliedAmount = Math.Min(
                    availableAmount,
                    invoiceOpenAmount
                );

                if (appliedAmount <= ExactTolerance)
                {
                    trx.Rollback();
                    result.Message = GetMsg(
                        ctx,
                        "AmountIsZero",
                        "Available amount is zero"
                    );
                    return result;
                }

                DateTime? dateAcct = payment.GetDateAcct();

                MAllocationHdr allocation = new MAllocationHdr(
                    ctx,
                    true,
                    dateAcct,
                    payment.GetC_Currency_ID(),
                    GetMsg(
                        ctx,
                        "VAS_072_AllocationDescription",
                        "AP payment match suggestion"
                    ),
                    trx
                );

                allocation.SetAD_Org_ID(payment.GetAD_Org_ID());
                allocation.SetDateTrx(dateAcct);
                allocation.SetDateAcct(dateAcct);
                allocation.SetC_ConversionType_ID(
                    payment.GetC_ConversionType_ID()
                );

                if (!allocation.Save())
                {
                    trx.Rollback();
                    result.Message = GetSaveError(
                        ctx,
                        "VIS_AllocationHdrNotSaved",
                        "Allocation header was not saved"
                    );
                    return result;
                }

                decimal remainingInvoiceAmount = Math.Max(
                    0,
                    invoiceOpenAmount - appliedAmount
                );

                // Return cycle (negative refund payment allocated against a
                // purchase-return credit memo) flips the allocation-line
                // sign, mirroring the standard allocation form's MultiplierAP
                // convention: purchase invoice -> negative line, purchase
                // return -> positive line. The cycle is classified by the
                // invoice DOCUMENT TYPE flag (same source as the displayed
                // type name), not the invoice-header IsReturnTrx column,
                // which can disagree with the document type in legacy data.
                bool invoiceIsReturn = MDocType.Get(
                    ctx,
                    invoice.GetC_DocType_ID()
                ).IsReturnTrx();

                decimal cycleSign = invoiceIsReturn ? 1M : -1M;
                decimal allocationLineAmount = cycleSign * appliedAmount;
                decimal overUnderAmount = cycleSign * remainingInvoiceAmount;

                MAllocationLine allocationLine = new MAllocationLine(
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
                allocationLine.SetPaymentInfo(paymentId, 0);
                allocationLine.SetC_InvoicePaySchedule_ID(payScheduleId);
                allocationLine.SetDateTrx(dateAcct);

                if (!allocationLine.Save())
                {
                    trx.Rollback();
                    result.Message = GetSaveError(
                        ctx,
                        "VIS_AllocLineNotCreated",
                        "Allocation line was not created"
                    );
                    return result;
                }

                if (!allocation.ProcessIt(DocActionVariables.ACTION_COMPLETE))
                {
                    trx.Rollback();
                    result.Message = GetMsg(
                        ctx,
                        "VAS_AllocationNotCompDueTo",
                        "Allocation could not be completed due to"
                    ) + " " + allocation.GetProcessMsg();
                    return result;
                }

                if (!allocation.Save())
                {
                    trx.Rollback();
                    result.Message = GetSaveError(
                        ctx,
                        "VIS_AllocationHdrNotSaved",
                        "Completed allocation was not saved"
                    );
                    return result;
                }

                if (payment.TestAllocation() && !payment.Save())
                {
                    trx.Rollback();
                    result.Message = GetSaveError(
                        ctx,
                        "PaymentNotCreated",
                        "Payment allocation status was not saved"
                    );
                    return result;
                }

                trx.Commit();

                result.Success = true;
                result.DocumentNo = allocation.GetDocumentNo();
                result.Message = GetMsg(
                    ctx,
                    "VAS_072_ApplySuccess",
                    "Allocation completed successfully"
                );
                return result;
            }
            catch (Exception ex)
            {
                if (trx != null)
                {
                    trx.Rollback();
                }

                VLogger.Get().SaveError(
                    "VAS_072_ApplyMatchAllocation",
                    ex
                );

                result.Message = GetMsg(
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

        #endregion


        #region Helpers

        /// <summary>
        /// Return-cycle amounts (negative refund payment allocated against an
        /// APC purchase-return credit memo) display as negative values while
        /// the matching and allocation math stays on absolute values.
        /// </summary>
        private static decimal ApplyCycleSign(
            decimal amount,
            bool isReturnCycle)
        {
            return isReturnCycle
                ? -Math.Abs(amount)
                : amount;
        }

        private string FirstNotEmpty(
    params string[] values)
        {
            if (values == null)
            {
                return string.Empty;
            }

            foreach (string value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return string.Empty;
        }

        private string CastNumberSql(string expression)
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

        private Ctx GetContext()
        {
            return Session["ctx"] as Ctx;
        }

        private JsonResult SessionExpiredResult(bool allowGet)
        {
            Ctx ctx = Env.GetCtx();
            string message = GetMsg(
                ctx,
                "SessionExpired",
                "Session Expired"
            );

            object response = new
            {
                success = false,
                error = message,
                errorText = message,
                message = message,
                hasData = false
            };

            return allowGet
                ? Json(response, JsonRequestBehavior.AllowGet)
                : Json(response);
        }

        private string GetMsg(Ctx ctx, string key, string fallback)
        {
            string msg = Msg.GetMsg(ctx, key);

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

        private string GetConfidenceName(Ctx ctx, string value)
        {
            return string.Equals(
                value,
                "HIGH",
                StringComparison.OrdinalIgnoreCase
            )
                ? GetMsg(ctx, "VAS_072_High", "High")
                : GetMsg(ctx, "VAS_072_Review", "Review");
        }

   

        private int GetAllocationFormId(Ctx ctx)
        {
            int configuredFormId = ctx.GetContextAsInt(
                "VAS_AllocationForm_ID"
            );

            if (configuredFormId > 0)
            {
                return configuredFormId;
            }

            try
            {
                string sql = @"
SELECT
    MAX(FormData.AD_Form_ID)
FROM AD_Form FormData
WHERE FormData.IsActive='Y'
AND
(
    UPPER(FormData.Name)='ALLOCATION'
    OR UPPER(FormData.Classname) LIKE '%ALLOCATION%'
)";

                return Util.GetValueOfInt(DB.ExecuteScalar(sql));
            }
            catch (Exception ex)
            {
                VLogger.Get().SaveError(
                    "VAS_072_GetAllocationFormId",
                    ex
                );
                return 0;
            }
        }

        private string GetSaveError(
            Ctx ctx,
            string messageKey,
            string fallback)
        {
            ValueNamePair error = VLogger.RetrieveError();

            if (error != null && !string.IsNullOrWhiteSpace(error.GetName()))
            {
                VLogger.Get().SaveError(
                    "VAS_072_DatabaseSaveError",
                    new InvalidOperationException(error.GetName())
                );
            }

            return GetMsg(ctx, messageKey, fallback);
        }

        private bool IsCompletedStatus(string docStatus)
        {
            return string.Equals(
                docStatus,
                "CO",
                StringComparison.OrdinalIgnoreCase
            ) || string.Equals(
                docStatus,
                "CL",
                StringComparison.OrdinalIgnoreCase
            );
        }

        private string FormatDate(DateTime? date)
        {
            return date.HasValue
                ? date.Value.ToString(
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture
                )
                : string.Empty;
        }

        private int NormalizePrecision(int value)
        {
            return value < 0 || value > 10 ? 2 : value;
        }

        private int GetRoundedInt(
            IDataReader reader,
            string columnName)
        {
            return Convert.ToInt32(
                Math.Round(
                    GetDecimal(reader, columnName),
                    0,
                    MidpointRounding.AwayFromZero
                )
            );
        }

        private void CloseReader(IDataReader reader)
        {
            if (reader != null)
            {
                reader.Close();
                reader.Dispose();
            }
        }

        private int GetInt(
            IDataReader reader,
            string columnName,
            int fallback = 0)
        {
            object value = reader[columnName];

            if (value == null || value == DBNull.Value)
            {
                return fallback;
            }

            int parsed;
            return int.TryParse(value.ToString(), out parsed)
                ? parsed
                : fallback;
        }

        private decimal GetDecimal(
            IDataReader reader,
            string columnName)
        {
            object value = reader[columnName];

            if (value == null || value == DBNull.Value)
            {
                return 0;
            }

            decimal parsed;
            return decimal.TryParse(
                value.ToString(),
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out parsed
            )
                ? parsed
                : Util.GetValueOfDecimal(value);
        }



        private int GetColumnOrdinal(
            IDataReader reader,
            string columnName)
        {
            if (
                reader == null ||
                string.IsNullOrWhiteSpace(columnName)
            )
            {
                return -1;
            }

            try
            {
                return reader.GetOrdinal(columnName);
            }
            catch
            {
                return -1;
            }
        }

        private object GetSafeValue(
            IDataReader reader,
            string columnName)
        {
            int ordinal = GetColumnOrdinal(
                reader,
                columnName
            );

            if (ordinal < 0)
            {
                return null;
            }

            try
            {
                if (reader.IsDBNull(ordinal))
                {
                    return null;
                }

                object value = reader.GetValue(ordinal);

                if (
                    value == null ||
                    value == DBNull.Value
                )
                {
                    return null;
                }

                return UnwrapProviderValue(value);
            }
            catch
            {
                return null;
            }
        }

        private object UnwrapProviderValue(
            object value)
        {
            if (
                value == null ||
                value == DBNull.Value
            )
            {
                return null;
            }

            Type valueType = value.GetType();

            /*
             * يعالج الأنواع التي يرجعها Oracle Provider مثل:
             * OracleDecimal
             * OracleDate
             * OracleTimeStamp
             * OracleString
             */
            try
            {
                System.Reflection.PropertyInfo isNullProperty =
                    valueType.GetProperty("IsNull");

                if (
                    isNullProperty != null &&
                    isNullProperty.CanRead
                )
                {
                    object isNullValue =
                        isNullProperty.GetValue(
                            value,
                            null
                        );

                    if (
                        isNullValue is bool &&
                        (bool)isNullValue
                    )
                    {
                        return null;
                    }
                }
            }
            catch
            {
                // Continue with normal conversion.
            }

            try
            {
                System.Reflection.PropertyInfo valueProperty =
                    valueType.GetProperty("Value");

                if (
                    valueProperty != null &&
                    valueProperty.CanRead
                )
                {
                    object actualValue =
                        valueProperty.GetValue(
                            value,
                            null
                        );

                    if (
                        actualValue != null &&
                        actualValue != DBNull.Value
                    )
                    {
                        return actualValue;
                    }
                }
            }
            catch
            {
                // Return original provider value.
            }

            return value;
        }

        private int GetSafeInt(
            IDataReader reader,
            string columnName,
            int fallback = 0)
        {
            object value = GetSafeValue(
                reader,
                columnName
            );

            if (value == null)
            {
                return fallback;
            }

            try
            {
                if (value is int)
                {
                    return (int)value;
                }

                if (value is short)
                {
                    return Convert.ToInt32(
                        (short)value
                    );
                }

                if (value is long)
                {
                    long longValue = (long)value;

                    if (longValue > int.MaxValue)
                    {
                        return int.MaxValue;
                    }

                    if (longValue < int.MinValue)
                    {
                        return int.MinValue;
                    }

                    return Convert.ToInt32(longValue);
                }

                if (value is decimal)
                {
                    decimal decimalValue =
                        (decimal)value;

                    if (decimalValue > int.MaxValue)
                    {
                        return int.MaxValue;
                    }

                    if (decimalValue < int.MinValue)
                    {
                        return int.MinValue;
                    }

                    return Convert.ToInt32(
                        Math.Round(
                            decimalValue,
                            0,
                            MidpointRounding.AwayFromZero
                        )
                    );
                }

                if (value is double)
                {
                    double doubleValue =
                        (double)value;

                    if (doubleValue > int.MaxValue)
                    {
                        return int.MaxValue;
                    }

                    if (doubleValue < int.MinValue)
                    {
                        return int.MinValue;
                    }

                    return Convert.ToInt32(
                        Math.Round(
                            doubleValue,
                            0,
                            MidpointRounding.AwayFromZero
                        )
                    );
                }

                if (value is float)
                {
                    float floatValue =
                        (float)value;

                    if (floatValue > int.MaxValue)
                    {
                        return int.MaxValue;
                    }

                    if (floatValue < int.MinValue)
                    {
                        return int.MinValue;
                    }

                    return Convert.ToInt32(
                        Math.Round(
                            floatValue,
                            0,
                            MidpointRounding.AwayFromZero
                        )
                    );
                }

                return Convert.ToInt32(
                    value,
                    CultureInfo.InvariantCulture
                );
            }
            catch
            {
                decimal parsedValue;

                bool parsed = decimal.TryParse(
                    Convert.ToString(
                        value,
                        CultureInfo.InvariantCulture
                    ),
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out parsedValue
                );

                if (!parsed)
                {
                    return fallback;
                }

                if (parsedValue > int.MaxValue)
                {
                    return int.MaxValue;
                }

                if (parsedValue < int.MinValue)
                {
                    return int.MinValue;
                }

                return Convert.ToInt32(
                    Math.Round(
                        parsedValue,
                        0,
                        MidpointRounding.AwayFromZero
                    )
                );
            }
        }

        private decimal GetSafeDecimal(
            IDataReader reader,
            string columnName,
            decimal fallback = 0M)
        {
            object value = GetSafeValue(
                reader,
                columnName
            );

            if (value == null)
            {
                return fallback;
            }

            try
            {
                if (value is decimal)
                {
                    return (decimal)value;
                }

                if (value is int)
                {
                    return Convert.ToDecimal(
                        (int)value
                    );
                }

                if (value is long)
                {
                    return Convert.ToDecimal(
                        (long)value
                    );
                }

                if (value is double)
                {
                    return Convert.ToDecimal(
                        (double)value
                    );
                }

                if (value is float)
                {
                    return Convert.ToDecimal(
                        (float)value
                    );
                }

                return Convert.ToDecimal(
                    value,
                    CultureInfo.InvariantCulture
                );
            }
            catch
            {
                decimal parsedValue;

                bool parsed = decimal.TryParse(
                    Convert.ToString(
                        value,
                        CultureInfo.InvariantCulture
                    ),
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out parsedValue
                );

                if (parsed)
                {
                    return parsedValue;
                }

                try
                {
                    return Util.GetValueOfDecimal(value);
                }
                catch
                {
                    return fallback;
                }
            }
        }

        private int GetSafeRoundedInt(
            IDataReader reader,
            string columnName,
            int fallback = 0)
        {
            decimal value = GetSafeDecimal(
                reader,
                columnName,
                fallback
            );

            if (value > int.MaxValue)
            {
                return int.MaxValue;
            }

            if (value < int.MinValue)
            {
                return int.MinValue;
            }

            try
            {
                return Convert.ToInt32(
                    Math.Round(
                        value,
                        0,
                        MidpointRounding.AwayFromZero
                    )
                );
            }
            catch
            {
                return fallback;
            }
        }

        private string GetSafeString(
            IDataReader reader,
            string columnName,
            string fallback = "")
        {
            object value = GetSafeValue(
                reader,
                columnName
            );

            if (value == null)
            {
                return fallback;
            }

            try
            {
                string text = Convert.ToString(
                    value,
                    CultureInfo.InvariantCulture
                );

                return text ?? fallback;
            }
            catch
            {
                return fallback;
            }
        }

        private DateTime? GetSafeDateTime(
            IDataReader reader,
            string columnName)
        {
            int ordinal = GetColumnOrdinal(
                reader,
                columnName
            );

            if (ordinal < 0)
            {
                return null;
            }

            try
            {
                if (reader.IsDBNull(ordinal))
                {
                    return null;
                }

                /*
                 * الأفضل مع Oracle DATE وTIMESTAMP.
                 */
                return reader.GetDateTime(ordinal);
            }
            catch
            {
                object value = GetSafeValue(
                    reader,
                    columnName
                );

                if (value == null)
                {
                    return null;
                }

                if (value is DateTime)
                {
                    return (DateTime)value;
                }

                try
                {
                    return Convert.ToDateTime(
                        value,
                        CultureInfo.InvariantCulture
                    );
                }
                catch
                {
                    DateTime parsedDate;

                    string dateText = Convert.ToString(
                        value,
                        CultureInfo.InvariantCulture
                    );

                    bool parsed = DateTime.TryParse(
                        dateText,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AllowWhiteSpaces,
                        out parsedDate
                    );

                    if (parsed)
                    {
                        return parsedDate;
                    }

                    parsed = DateTime.TryParse(
                        dateText,
                        CultureInfo.CurrentCulture,
                        DateTimeStyles.AllowWhiteSpaces,
                        out parsedDate
                    );

                    return parsed
                        ? parsedDate
                        : (DateTime?)null;
                }
            }
        }



        private string GetString(
            IDataReader reader,
            string columnName)
        {
            object value = reader[columnName];
            return value == null || value == DBNull.Value
                ? string.Empty
                : value.ToString();
        }

        private DateTime? GetDateTime(
            IDataReader reader,
            string columnName)
        {
            object value = reader[columnName];

            if (value == null || value == DBNull.Value)
            {
                return null;
            }

            DateTime parsed;
            return DateTime.TryParse(value.ToString(), out parsed)
                ? parsed
                : (DateTime?)null;
        }

        #endregion

        #region Result Classes

        private sealed class MatchListResult
        {
            public MatchListResult()
            {
                Rows = new List<MatchListRow>();
                SchemaStdPrecision = 2;
            }

            public List<MatchListRow> Rows { get; private set; }
            public bool SummaryLoaded { get; set; }
            public int TotalRecords { get; set; }
            public decimal TotalAccountingAmount { get; set; }
            public int HighConfidenceCount { get; set; }
            public int SchemaCurrencyId { get; set; }
            public string SchemaCurrencyISOCode { get; set; }
            public string SchemaCurrencySymbol { get; set; }
            public int SchemaStdPrecision { get; set; }
        }

        private sealed class MatchListRow
        {
            public int PaymentId { get; set; }
            public int InvoiceId { get; set; }
            public int InvoicePayScheduleId { get; set; }
            public int VendorId { get; set; }
            public string VendorName { get; set; }
            public string PaymentDocumentNo { get; set; }
            public string InvoiceDocumentNo { get; set; }
            public DateTime? PaymentDate { get; set; }
            public DateTime? InvoiceDate { get; set; }
            public DateTime? DueDate { get; set; }
            public int PaymentCurrencyId { get; set; }
            public string PaymentCurrencyISOCode { get; set; }
            public string PaymentCurrencySymbol { get; set; }
            public int PaymentPrecision { get; set; }
            public decimal PaymentAllocatedAmount { get; set; }
            public decimal PaymentOpenAmount { get; set; }
            public decimal InvoiceOriginalAmount { get; set; }
            public decimal InvoiceAllocatedAmount { get; set; }
            public decimal InvoiceOpenAmount { get; set; }
            public decimal ReadyAmount { get; set; }
            public decimal AccountingAmount { get; set; }
            public decimal DifferenceAmount { get; set; }
            public decimal DifferencePercentage { get; set; }
            public int DateGapDays { get; set; }
            public bool ReferenceMatch { get; set; }
            public int Score { get; set; }
            public string Confidence { get; set; }
            public bool IsAutoApplicable { get; set; }
            public bool IsReturnCycle { get; set; }
        }

        private sealed class MatchDetailRow
        {
            public int PaymentId { get; set; }
            public int PaymentVendorId { get; set; }
            public int InvoiceVendorId { get; set; }
            public int VendorId { get; set; }
            public string VendorName { get; set; }
            public string PaymentDocumentNo { get; set; }
            public DateTime? PaymentDate { get; set; }
            public string PaymentMethodCode { get; set; }
            public string ReferenceNo { get; set; }
            public string BankName { get; set; }
            public string AccountNo { get; set; }
            public int PaymentCurrencyId { get; set; }
            public string PaymentCurrencyISOCode { get; set; }
            public string PaymentCurrencySymbol { get; set; }
            public int PaymentPrecision { get; set; }
            public decimal PaymentOriginalAmount { get; set; }
            public decimal PaymentAllocatedAmount { get; set; }
            public decimal PaymentOpenAmount { get; set; }
            public int InvoiceId { get; set; }
            public int InvoicePayScheduleId { get; set; }
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
            public decimal ReadyAmount { get; set; }
            public decimal DifferenceAmount { get; set; }
            public decimal DifferencePercentage { get; set; }
            public int DateGapDays { get; set; }
            public bool ReferenceMatch { get; set; }
            public int Score { get; set; }
            public string PaymentMethodName { get; set; }
            public string PaymentDocTypeName { get; set; }
            public string InvoiceDocTypeName { get; set; }
            public string Confidence { get; set; }
            public bool IsAutoApplicable { get; set; }
            public bool IsReturnCycle { get; set; }
        }

        private sealed class HighConfidenceCandidateRow
        {
            public int PaymentId { get; set; }
            public int InvoiceId { get; set; }
            public int InvoicePayScheduleId { get; set; }
        }

        private sealed class AllocationValidationRow
        {
            public int PaymentId { get; set; }
            public int InvoiceId { get; set; }
            public int InvoicePayScheduleId { get; set; }
            public int VendorId { get; set; }
            public int CurrencyId { get; set; }
            public decimal PaymentOpenAmount { get; set; }
            public decimal InvoiceOpenAmount { get; set; }
        }

        private sealed class ApplyResult
        {
            public bool Success { get; set; }
            public string DocumentNo { get; set; }
            public string Message { get; set; }
        }

        #endregion
    }
}
