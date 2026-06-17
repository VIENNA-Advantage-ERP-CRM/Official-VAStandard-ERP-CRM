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
using System.Linq;
using System.Web.Mvc;
using VAdvantage.DataBase;
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

        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetMatchSuggestions(
            int pageNo = 1,
            int pageSize = DefaultPageSize)
        {
            if (Session["ctx"] == null)
            {
                return Json(
                    JsonConvert.SerializeObject(
                        new
                        {
                            success = false,
                            error = "Session Expired",
                            errorText = "Session Expired"
                        }
                    ),
                    JsonRequestBehavior.AllowGet
                );
            }

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
                        }
                    ),
                    JsonRequestBehavior.AllowGet
                );
            }

            try
            {
                pageNo = Math.Max(1, pageNo);

                pageSize = Math.Max(
                    1,
                    Math.Min(MaximumPageSize, pageSize)
                );

                MatchSuggestionData queryResult =
                    ExecuteMatchSuggestionQuery(
                        ctx,
                        0,
                        0,
                        0,
                        null
                    );

                List<MatchSuggestionRow> suggestions =
                    GetBestSuggestions(queryResult.Rows);

                int totalRecords = suggestions.Count;

                int totalPages = totalRecords == 0
                    ? 0
                    : (int)Math.Ceiling(
                        totalRecords / (decimal)pageSize
                    );

                if (
                    totalPages > 0 &&
                    pageNo > totalPages
                )
                {
                    pageNo = totalPages;
                }

                List<MatchSuggestionRow> rows =
                    suggestions
                        .Skip((pageNo - 1) * pageSize)
                        .Take(pageSize)
                        .ToList();

                decimal totalReadyAmount =
                    suggestions.Sum(
                        row => row.AccountingAmount
                    );

                int highConfidenceCount =
                    suggestions.Count(
                        row => row.Confidence == "HIGH"
                    );

                object result = new
                {
                    success = true,
                    error = "",
                    hasData = totalRecords > 0,

                    pageNo = pageNo,
                    pageSize = pageSize,
                    totalPages = totalPages,
                    totalRecords = totalRecords,

                    totalReadyAmount = totalReadyAmount,
                    highConfidenceCount = highConfidenceCount,

                    cCurrencyId =
                        queryResult.SchemaCurrency.C_Currency_ID,

                    currencyISOCode =
                        queryResult.SchemaCurrency.ISO_Code,

                    currencySymbol =
                        queryResult.SchemaCurrency.CurSymbol,

                    stdPrecision =
                        queryResult.SchemaCurrency.StdPrecision,

                    allocationWindowId = 0,

                    rows = rows.Select(
                        row => new
                        {
                            paymentId =
                                row.C_Payment_ID,

                            invoiceId =
                                row.C_Invoice_ID,

                            payScheduleId =
                                row.C_InvoicePaySchedule_ID,

                            vendorId =
                                row.C_BPartner_ID,

                            vendorName =
                                row.VendorName,

                            paymentDocumentNo =
                                row.PaymentDocumentNo,

                            invoiceDocumentNo =
                                row.InvoiceDocumentNo,

                            paymentAmount =
                                row.PaymentAmount,

                            paymentOpenAmount =
                                row.PaymentOpenAmount,

                            invoiceOpenAmount =
                                row.InvoiceOpenAmount,

                            readyAmount =
                                row.ReadyAmount,

                            accountingAmount =
                                row.AccountingAmount,

                            dueDate =
                                row.DueDate == DateTime.MinValue
                                    ? null
                                    : (DateTime?)row.DueDate,

                            confidence =
                                row.Confidence,

                            score =
                                row.Score,

                            cCurrencyId =
                                row.C_Currency_ID,

                            currencyISOCode =
                                row.CurrencyISOCode,

                            currencySymbol =
                                row.CurrencySymbol
                        }
                    )
                };

                return Json(
                    JsonConvert.SerializeObject(result),
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
                            errorText = GetMsg(
                                ctx,
                                "VAS_072_LoadError",
                                "Could not load match suggestions"
                            )
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
            if (Session["ctx"] == null)
            {
                return Json(
                    JsonConvert.SerializeObject(
                        new
                        {
                            success = false,
                            error = "Session Expired",
                            message = "Session Expired"
                        }
                    )
                );
            }

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
                        }
                    )
                );
            }

            try
            {
                ApplyResult result = ApplySingleAllocation(
                    ctx,
                    paymentId,
                    invoiceId,
                    payScheduleId
                );

                return Json(
                    JsonConvert.SerializeObject(
                        new
                        {
                            success = result.Success,
                            documentNo = result.DocumentNo,
                            message = result.Message
                        }
                    )
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
                            message = GetMsg(
                                ctx,
                                "VAS_072_ApplyError",
                                "Could not complete allocation"
                            )
                        }
                    )
                );
            }
        }

        [HttpPost]
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult ApplyHighConfidence()
        {
            if (Session["ctx"] == null)
            {
                return Json(
                    JsonConvert.SerializeObject(
                        new
                        {
                            success = false,
                            error = "Session Expired",
                            message = "Session Expired"
                        }
                    )
                );
            }

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
                        }
                    )
                );
            }

            try
            {
                MatchSuggestionData queryResult =
                    ExecuteMatchSuggestionQuery(
                        ctx,
                        0,
                        0,
                        0,
                        null
                    );

                List<MatchSuggestionRow> suggestions =
                    GetBestSuggestions(queryResult.Rows)
                        .Where(
                            row => row.Confidence == "HIGH"
                        )
                        .ToList();

                int appliedCount = 0;
                List<string> errors = new List<string>();

                foreach (MatchSuggestionRow suggestion in suggestions)
                {
                    ApplyResult result = ApplySingleAllocation(
                        ctx,
                        suggestion.C_Payment_ID,
                        suggestion.C_Invoice_ID,
                        suggestion.C_InvoicePaySchedule_ID
                    );

                    if (result.Success)
                    {
                        appliedCount++;
                    }
                    else if (!string.IsNullOrEmpty(result.Message))
                    {
                        errors.Add(result.Message);
                    }
                }

                bool success = errors.Count == 0;
                string message;

                if (success)
                {
                    message =
                        GetMsg(
                            ctx,
                            "VAS_072_ApplySuccess",
                            "Allocation completed successfully"
                        ) +
                        " (" +
                        appliedCount +
                        ")";
                }
                else
                {
                    message =
                        GetMsg(
                            ctx,
                            "VAS_072_ApplyError",
                            "Could not complete allocation"
                        ) +
                        ". " +
                        string.Join(" | ", errors);
                }

                return Json(
                    JsonConvert.SerializeObject(
                        new
                        {
                            success = success,
                            appliedCount = appliedCount,
                            failedCount = errors.Count,
                            message = message
                        }
                    )
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
                            message = GetMsg(
                                ctx,
                                "VAS_072_ApplyError",
                                "Could not complete allocation"
                            )
                        }
                    )
                );
            }
        }

        private List<MatchSuggestionRow> GetBestSuggestions(
            List<MatchSuggestionRow> candidates)
        {
            if (candidates == null)
            {
                return new List<MatchSuggestionRow>();
            }

            return candidates
                .GroupBy(
                    row => row.C_Payment_ID
                )
                .Select(
                    group => group
                        .OrderByDescending(
                            row => row.Score
                        )
                        .ThenBy(
                            row => Math.Abs(
                                row.PaymentOpenAmount -
                                row.InvoiceOpenAmount
                            )
                        )
                        .ThenBy(
                            row => row.DueDate
                        )
                        .ThenBy(
                            row => row.C_InvoicePaySchedule_ID
                        )
                        .First()
                )
                .OrderByDescending(
                    row => row.Confidence == "HIGH"
                )
                .ThenByDescending(
                    row => row.Score
                )
                .ThenBy(
                    row => row.DueDate
                )
                .ThenByDescending(
                    row => row.ReadyAmount
                )
                .ToList();
        }

        private MatchSuggestionData ExecuteMatchSuggestionQuery(
            Ctx ctx,
            int paymentId,
            int invoiceId,
            int payScheduleId,
            Trx trx)
        {
            MatchSuggestionData result =
                new MatchSuggestionData
                {
                    SchemaCurrency =
                        new SchemaCurrencyInfo
                        {
                            C_Currency_ID = 0,
                            ISO_Code = "",
                            CurSymbol = "",
                            StdPrecision = 2
                        },

                    Rows = new List<MatchSuggestionRow>()
                };

            string paymentSql = @"
SELECT
Payment.C_Payment_ID,
Payment.C_BPartner_ID,
Payment.DocumentNo AS PaymentDocumentNo,
Payment.DateAcct AS PaymentDateAcct,
ABS(COALESCE(Payment.PayAmt,0)) AS PaymentAmount,
ABS(COALESCE(Payment.PayAmt,0))-
COALESCE(PaymentAllocated.AllocatedAmount,0) AS PaymentOpenAmount,
Payment.C_Currency_ID,
Payment.C_ConversionType_ID,
Payment.AD_Client_ID,
Payment.AD_Org_ID
FROM C_Payment Payment
LEFT OUTER JOIN PaymentAllocated PaymentAllocated ON
(Payment.C_Payment_ID=PaymentAllocated.C_Payment_ID)
WHERE Payment.IsActive='Y'
AND Payment.Processed='Y'
AND Payment.IsReceipt='N'
AND COALESCE(Payment.IsAllocated,'N')<>'Y'
AND Payment.C_BPartner_ID IS NOT NULL
AND Payment.DocStatus IN ('CO','CL')
AND Payment.AD_Client_ID=@AD_Client_ID
AND (@C_Payment_ID=0 OR Payment.C_Payment_ID=@C_Payment_ID)
AND ABS(COALESCE(Payment.PayAmt,0))-
COALESCE(PaymentAllocated.AllocatedAmount,0)>0";

            paymentSql = MRole
                .GetDefault(ctx)
                .AddAccessSQL(
                    paymentSql,
                    "Payment",
                    MRole.SQL_FULLYQUALIFIED,
                    MRole.SQL_RO
                );

            string sql = @"
WITH SchemaCurrency AS
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
(ClientInfo.C_AcctSchema1_ID=AcctSchema.C_AcctSchema_ID)
INNER JOIN C_Currency Currency ON
(AcctSchema.C_Currency_ID=Currency.C_Currency_ID)
WHERE ClientInfo.IsActive='Y'
AND ClientInfo.AD_Client_ID=@AD_Client_ID
),
PaymentAllocated AS
(
SELECT
AllocationLine.C_Payment_ID,
SUM(
ABS(COALESCE(AllocationLine.Amount,0))
) AS AllocatedAmount
FROM C_AllocationLine AllocationLine
INNER JOIN C_AllocationHdr AllocationHeader ON
(AllocationLine.C_AllocationHdr_ID=AllocationHeader.C_AllocationHdr_ID)
WHERE AllocationLine.IsActive='Y'
AND AllocationHeader.IsActive='Y'
AND AllocationHeader.DocStatus IN ('CO','CL')
AND AllocationLine.C_Payment_ID IS NOT NULL
GROUP BY
AllocationLine.C_Payment_ID
),
EligiblePayments AS
(
" + paymentSql + @"
),
InvoiceAllocated AS
(
SELECT
AllocationLine.C_InvoicePaySchedule_ID,
SUM(
ABS(COALESCE(AllocationLine.Amount,0))+
ABS(COALESCE(AllocationLine.DiscountAmt,0))+
ABS(COALESCE(AllocationLine.WriteOffAmt,0))
) AS AllocatedAmount
FROM C_AllocationLine AllocationLine
INNER JOIN C_AllocationHdr AllocationHeader ON
(AllocationLine.C_AllocationHdr_ID=AllocationHeader.C_AllocationHdr_ID)
WHERE AllocationLine.IsActive='Y'
AND AllocationHeader.IsActive='Y'
AND AllocationHeader.DocStatus IN ('CO','CL')
AND AllocationLine.C_InvoicePaySchedule_ID IS NOT NULL
GROUP BY
AllocationLine.C_InvoicePaySchedule_ID
),
OpenCandidates AS
(
SELECT
EligiblePayments.AD_Client_ID,
EligiblePayments.AD_Org_ID,
EligiblePayments.C_Payment_ID,
EligiblePayments.C_BPartner_ID,
EligiblePayments.PaymentDocumentNo,
EligiblePayments.PaymentDateAcct,
EligiblePayments.PaymentAmount,
EligiblePayments.PaymentOpenAmount,
EligiblePayments.C_Currency_ID,
EligiblePayments.C_ConversionType_ID,
BusinessPartner.Name AS VendorName,
Invoice.C_Invoice_ID,
Invoice.DocumentNo AS InvoiceDocumentNo,
Invoice.DateInvoiced,
InvoicePaySchedule.C_InvoicePaySchedule_ID,
InvoicePaySchedule.DueDate,
CASE
WHEN InvoiceAllocated.AllocatedAmount IS NULL
THEN COALESCE(InvoicePaySchedule.DueAmt,0)
ELSE
COALESCE(InvoicePaySchedule.DueAmt,0)-
InvoiceAllocated.AllocatedAmount
END AS InvoiceOpenAmount,
PaymentCurrency.ISO_Code AS CurrencyISOCode,
CASE
WHEN PaymentCurrency.CurSymbol IS NOT NULL
THEN PaymentCurrency.CurSymbol
ELSE PaymentCurrency.ISO_Code
END AS CurrencySymbol
FROM EligiblePayments
INNER JOIN C_BPartner BusinessPartner ON
(EligiblePayments.C_BPartner_ID=BusinessPartner.C_BPartner_ID)
INNER JOIN C_Invoice Invoice ON
(EligiblePayments.C_BPartner_ID=Invoice.C_BPartner_ID
AND EligiblePayments.AD_Client_ID=Invoice.AD_Client_ID)
INNER JOIN C_InvoicePaySchedule InvoicePaySchedule ON
(Invoice.C_Invoice_ID=InvoicePaySchedule.C_Invoice_ID)
LEFT OUTER JOIN InvoiceAllocated InvoiceAllocated ON
(InvoicePaySchedule.C_InvoicePaySchedule_ID=
InvoiceAllocated.C_InvoicePaySchedule_ID)
INNER JOIN C_Currency PaymentCurrency ON
(EligiblePayments.C_Currency_ID=PaymentCurrency.C_Currency_ID)
WHERE Invoice.IsActive='Y'
AND Invoice.Processed='Y'
AND Invoice.IsSOTrx='N'
AND COALESCE(Invoice.IsReturnTrx,'N')<>'Y'
AND Invoice.DocStatus IN ('CO','CL')
AND Invoice.C_Currency_ID=EligiblePayments.C_Currency_ID
AND InvoicePaySchedule.IsActive='Y'
AND COALESCE(InvoicePaySchedule.VA009_IsPaid,'N')<>'Y'
AND COALESCE(InvoicePaySchedule.IsHoldPayment,'N')<>'Y'
AND COALESCE(InvoicePaySchedule.DueAmt,0)>0
AND (@C_Invoice_ID=0 OR Invoice.C_Invoice_ID=@C_Invoice_ID)
AND
(
@C_InvoicePaySchedule_ID=0
OR InvoicePaySchedule.C_InvoicePaySchedule_ID=
@C_InvoicePaySchedule_ID
)
AND
(
InvoiceAllocated.AllocatedAmount IS NULL
OR COALESCE(InvoicePaySchedule.DueAmt,0)>
InvoiceAllocated.AllocatedAmount
)
),
CandidateRows AS
(
SELECT
OpenCandidates.AD_Client_ID,
OpenCandidates.AD_Org_ID,
OpenCandidates.C_Payment_ID,
OpenCandidates.C_BPartner_ID,
OpenCandidates.PaymentDocumentNo,
OpenCandidates.PaymentDateAcct,
OpenCandidates.PaymentAmount,
OpenCandidates.PaymentOpenAmount,
OpenCandidates.C_Currency_ID,
OpenCandidates.C_ConversionType_ID,
OpenCandidates.VendorName,
OpenCandidates.C_Invoice_ID,
OpenCandidates.InvoiceDocumentNo,
OpenCandidates.DateInvoiced,
OpenCandidates.C_InvoicePaySchedule_ID,
OpenCandidates.DueDate,
OpenCandidates.InvoiceOpenAmount,
OpenCandidates.CurrencyISOCode,
OpenCandidates.CurrencySymbol,
CASE
WHEN OpenCandidates.PaymentOpenAmount<
OpenCandidates.InvoiceOpenAmount
THEN OpenCandidates.PaymentOpenAmount
ELSE OpenCandidates.InvoiceOpenAmount
END AS ReadyAmount
FROM OpenCandidates
WHERE OpenCandidates.InvoiceOpenAmount>0
)
SELECT
SchemaCurrency.C_Currency_ID AS SchemaCurrency_ID,
SchemaCurrency.ISO_Code AS SchemaCurrencyISOCode,
SchemaCurrency.CurSymbol AS SchemaCurrencySymbol,
SchemaCurrency.StdPrecision AS SchemaStdPrecision,
CandidateRows.C_Payment_ID,
CandidateRows.C_BPartner_ID,
CandidateRows.PaymentDocumentNo,
CandidateRows.PaymentDateAcct,
CandidateRows.PaymentAmount,
CandidateRows.PaymentOpenAmount,
CandidateRows.C_Currency_ID,
CandidateRows.VendorName,
CandidateRows.C_Invoice_ID,
CandidateRows.InvoiceDocumentNo,
CandidateRows.DateInvoiced,
CandidateRows.C_InvoicePaySchedule_ID,
CandidateRows.DueDate,
CandidateRows.InvoiceOpenAmount,
CandidateRows.ReadyAmount,
CandidateRows.CurrencyISOCode,
CandidateRows.CurrencySymbol,
CASE
WHEN CandidateRows.C_Payment_ID IS NULL THEN 0
WHEN CandidateRows.C_Currency_ID=SchemaCurrency.C_Currency_ID
THEN CandidateRows.ReadyAmount
ELSE COALESCE(
CurrencyConvert(
CandidateRows.ReadyAmount,
CandidateRows.C_Currency_ID,
SchemaCurrency.C_Currency_ID,
CandidateRows.PaymentDateAcct,
COALESCE(CandidateRows.C_ConversionType_ID,0),
CandidateRows.AD_Client_ID,
CandidateRows.AD_Org_ID
),
0
)
END AS AccountingAmount
FROM SchemaCurrency SchemaCurrency
LEFT OUTER JOIN CandidateRows CandidateRows ON
(SchemaCurrency.AD_Client_ID=CandidateRows.AD_Client_ID)
ORDER BY
CandidateRows.C_Payment_ID,
CandidateRows.C_Invoice_ID,
CandidateRows.C_InvoicePaySchedule_ID";

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
                    ),
                    new SqlParameter(
                        "@C_Invoice_ID",
                        invoiceId
                    ),
                    new SqlParameter(
                        "@C_InvoicePaySchedule_ID",
                        payScheduleId
                    )
                };

            using (
                IDataReader reader = DB.ExecuteReader(
                    sql,
                    parameters,
                    trx
                )
            )
            {
                while (
                    reader != null &&
                    reader.Read()
                )
                {
                    if (
                        result.SchemaCurrency.C_Currency_ID <= 0
                    )
                    {
                        result.SchemaCurrency.C_Currency_ID =
                            GetInt(
                                reader,
                                "SchemaCurrency_ID"
                            );

                        result.SchemaCurrency.ISO_Code =
                            GetString(
                                reader,
                                "SchemaCurrencyISOCode"
                            );

                        result.SchemaCurrency.CurSymbol =
                            GetString(
                                reader,
                                "SchemaCurrencySymbol"
                            );

                        result.SchemaCurrency.StdPrecision =
                            GetInt(
                                reader,
                                "SchemaStdPrecision"
                            );
                    }

                    int currentPaymentId =
                        GetInt(
                            reader,
                            "C_Payment_ID"
                        );

                    if (currentPaymentId <= 0)
                    {
                        continue;
                    }

                    MatchSuggestionRow row =
                        new MatchSuggestionRow
                        {
                            C_Payment_ID =
                                currentPaymentId,

                            C_BPartner_ID =
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

                            PaymentDateAcct =
                                GetDateTime(
                                    reader,
                                    "PaymentDateAcct"
                                ),

                            PaymentAmount =
                                GetDecimal(
                                    reader,
                                    "PaymentAmount"
                                ),

                            PaymentOpenAmount =
                                GetDecimal(
                                    reader,
                                    "PaymentOpenAmount"
                                ),

                            C_Invoice_ID =
                                GetInt(
                                    reader,
                                    "C_Invoice_ID"
                                ),

                            InvoiceDocumentNo =
                                GetString(
                                    reader,
                                    "InvoiceDocumentNo"
                                ),

                            DateInvoiced =
                                GetDateTime(
                                    reader,
                                    "DateInvoiced"
                                ),

                            C_InvoicePaySchedule_ID =
                                GetInt(
                                    reader,
                                    "C_InvoicePaySchedule_ID"
                                ),

                            DueDate =
                                GetDateTime(
                                    reader,
                                    "DueDate"
                                ),

                            InvoiceOpenAmount =
                                GetDecimal(
                                    reader,
                                    "InvoiceOpenAmount"
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

                            C_Currency_ID =
                                GetInt(
                                    reader,
                                    "C_Currency_ID"
                                ),

                            CurrencyISOCode =
                                GetString(
                                    reader,
                                    "CurrencyISOCode"
                                ),

                            CurrencySymbol =
                                GetString(
                                    reader,
                                    "CurrencySymbol"
                                )
                        };

                    if (
                        row.PaymentOpenAmount <= 0 ||
                        row.InvoiceOpenAmount <= 0 ||
                        row.ReadyAmount <= 0
                    )
                    {
                        continue;
                    }

                    CalculateScore(row);
                    result.Rows.Add(row);
                }
            }

            return result;
        }

        private void CalculateScore(
            MatchSuggestionRow row)
        {
            decimal difference = Math.Abs(
                row.PaymentOpenAmount -
                row.InvoiceOpenAmount
            );

            decimal baseAmount = Math.Max(
                Math.Abs(row.PaymentOpenAmount),
                Math.Abs(row.InvoiceOpenAmount)
            );

            decimal amountDifferencePercent =
                baseAmount == 0
                    ? 100
                    : difference / baseAmount * 100;

            double dateDifference = 999999;

            if (
                row.PaymentDateAcct != DateTime.MinValue &&
                row.DueDate != DateTime.MinValue
            )
            {
                dateDifference = Math.Abs(
                    (
                        row.PaymentDateAcct -
                        row.DueDate
                    ).TotalDays
                );
            }

            int score = 40;

            if (difference == 0)
            {
                score += 45;
            }
            else if (amountDifferencePercent <= 1)
            {
                score += 38;
            }
            else if (amountDifferencePercent <= 5)
            {
                score += 28;
            }
            else if (amountDifferencePercent <= 10)
            {
                score += 18;
            }
            else if (amountDifferencePercent <= 20)
            {
                score += 8;
            }

            if (dateDifference <= 7)
            {
                score += 15;
            }
            else if (dateDifference <= 30)
            {
                score += 10;
            }
            else if (dateDifference <= 60)
            {
                score += 5;
            }

            row.Score = Math.Min(
                100,
                score
            );

            row.Confidence =
                row.Score >= 85 &&
                amountDifferencePercent <= 5
                    ? "HIGH"
                    : "REVIEW";
        }

        private ApplyResult ApplySingleAllocation(
            Ctx ctx,
            int paymentId,
            int invoiceId,
            int payScheduleId)
        {
            if (
                paymentId <= 0 ||
                invoiceId <= 0 ||
                payScheduleId <= 0
            )
            {
                return new ApplyResult
                {
                    Success = false,
                    DocumentNo = "",
                    Message = GetMsg(
                        ctx,
                        "VAS_072_InvalidParameters",
                        "Invalid payment or invoice parameters"
                    )
                };
            }

            string trxName =
                Trx.CreateTrxName(
                    "VAS072Allocation"
                );

            Trx trx = Trx.Get(
                trxName,
                true
            );

            try
            {
                MatchSuggestionData validationData =
                    ExecuteMatchSuggestionQuery(
                        ctx,
                        paymentId,
                        invoiceId,
                        payScheduleId,
                        trx
                    );

                MatchSuggestionRow suggestion =
                    validationData.Rows
                        .FirstOrDefault(
                            row =>
                                row.C_Payment_ID == paymentId &&
                                row.C_Invoice_ID == invoiceId &&
                                row.C_InvoicePaySchedule_ID ==
                                    payScheduleId
                        );

                if (suggestion == null)
                {
                    throw new InvalidOperationException(
                        GetMsg(
                            ctx,
                            "VAS_072_MatchNotAvailable",
                            "The selected match suggestion is no longer available"
                        )
                    );
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
                    invoice.Get_ID() <= 0
                )
                {
                    throw new InvalidOperationException(
                        GetMsg(
                            ctx,
                            "VAS_072_RecordNotFound",
                            "Payment or invoice was not found"
                        )
                    );
                }

                if (payment.IsReceipt())
                {
                    throw new InvalidOperationException(
                        GetMsg(
                            ctx,
                            "VAS_072_NotAPPayment",
                            "The selected payment is not an AP payment"
                        )
                    );
                }

                if (payment.IsAllocated())
                {
                    throw new InvalidOperationException(
                        GetMsg(
                            ctx,
                            "VAS_072_AlreadyAllocated",
                            "The selected payment is already allocated"
                        )
                    );
                }

                if (invoice.IsSOTrx())
                {
                    throw new InvalidOperationException(
                        GetMsg(
                            ctx,
                            "VAS_072_NotPurchaseInvoice",
                            "The selected invoice is not a purchase invoice"
                        )
                    );
                }

                if (
                    payment.GetC_BPartner_ID() !=
                    invoice.GetC_BPartner_ID()
                )
                {
                    throw new InvalidOperationException(
                        GetMsg(
                            ctx,
                            "VAS_072_DifferentBusinessPartner",
                            "Payment and invoice belong to different business partners"
                        )
                    );
                }

                if (
                    payment.GetC_Currency_ID() !=
                    invoice.GetC_Currency_ID()
                )
                {
                    throw new InvalidOperationException(
                        GetMsg(
                            ctx,
                            "VAS_072_DifferentCurrency",
                            "Payment and invoice currencies do not match"
                        )
                    );
                }

                decimal allocationAmount =
                    Math.Abs(
                        suggestion.ReadyAmount
                    );

                if (allocationAmount <= 0)
                {
                    throw new InvalidOperationException(
                        GetMsg(
                            ctx,
                            "VAS_072_NoOpenAmount",
                            "There is no open amount available for allocation"
                        )
                    );
                }

                decimal allocationLineAmount =
                    -allocationAmount;

                MAllocationHdr allocation =
                    new MAllocationHdr(
                        ctx,
                        true,
                        payment.GetDateAcct(),
                        payment.GetC_Currency_ID(),
                        GetMsg(
                            ctx,
                            "VAS_072_AllocationDescription",
                            "AP payment match suggestion"
                        ),
                        trx
                    );

                allocation.SetAD_Org_ID(
                    payment.GetAD_Org_ID()
                );

                if (!allocation.Save())
                {
                    throw new InvalidOperationException(
                        GetMsg(
                            ctx,
                            "VAS_072_SaveHeaderError",
                            "Could not save allocation header"
                        )
                    );
                }

                MAllocationLine allocationLine =
                    new MAllocationLine(
                        allocation,
                        allocationLineAmount,
                        0,
                        0,
                        0
                    );

                allocationLine.SetC_BPartner_ID(
                    payment.GetC_BPartner_ID()
                );

                allocationLine.SetC_Payment_ID(
                    payment.GetC_Payment_ID()
                );

                allocationLine.SetC_Invoice_ID(
                    invoice.GetC_Invoice_ID()
                );

                allocationLine.SetC_InvoicePaySchedule_ID(
                    payScheduleId
                );

                if (!allocationLine.Save())
                {
                    throw new InvalidOperationException(
                        GetMsg(
                            ctx,
                            "VAS_072_SaveLineError",
                            "Could not save allocation line"
                        )
                    );
                }

                if (
                    !allocation.ProcessIt(
                        DocActionVariables.ACTION_COMPLETE
                    )
                )
                {
                    string processMessage =
                        allocation.GetProcessMsg();

                    throw new InvalidOperationException(
                        string.IsNullOrEmpty(processMessage)
                            ? GetMsg(
                                ctx,
                                "VAS_072_CompleteError",
                                "Could not complete allocation"
                            )
                            : processMessage
                    );
                }

                if (!allocation.Save())
                {
                    throw new InvalidOperationException(
                        GetMsg(
                            ctx,
                            "VAS_072_SaveHeaderError",
                            "Could not save completed allocation"
                        )
                    );
                }

                trx.Commit();

                return new ApplyResult
                {
                    Success = true,
                    DocumentNo = allocation.GetDocumentNo(),
                    Message = GetMsg(
                        ctx,
                        "VAS_072_ApplySuccess",
                        "Allocation completed successfully"
                    )
                };
            }
            catch (Exception ex)
            {
                if (trx != null)
                {
                    trx.Rollback();
                }

                return new ApplyResult
                {
                    Success = false,
                    DocumentNo = "",
                    Message = ex.Message
                };
            }
            finally
            {
                if (trx != null)
                {
                    trx.Close();
                }
            }
        }

        private string GetMsg(
            Ctx ctx,
            string messageKey,
            string fallback)
        {
            string message = Msg.GetMsg(
                ctx,
                messageKey
            );

            if (
                string.IsNullOrEmpty(message) ||
                message == messageKey ||
                message == "[" + messageKey + "]"
            )
            {
                return fallback;
            }

            return message;
        }

        private int GetInt(
            IDataRecord record,
            string columnName)
        {
            object value = record[columnName];

            return value == null ||
                   value == DBNull.Value
                ? 0
                : Convert.ToInt32(value);
        }

        private decimal GetDecimal(
            IDataRecord record,
            string columnName)
        {
            object value = record[columnName];

            return value == null ||
                   value == DBNull.Value
                ? 0
                : Convert.ToDecimal(value);
        }

        private string GetString(
            IDataRecord record,
            string columnName)
        {
            object value = record[columnName];

            return value == null ||
                   value == DBNull.Value
                ? ""
                : Convert.ToString(value);
        }

        private DateTime GetDateTime(
            IDataRecord record,
            string columnName)
        {
            object value = record[columnName];

            return value == null ||
                   value == DBNull.Value
                ? DateTime.MinValue
                : Convert.ToDateTime(value);
        }

        private class MatchSuggestionData
        {
            public MatchSuggestionData()
            {
                Rows = new List<MatchSuggestionRow>();
                SchemaCurrency = new SchemaCurrencyInfo();
            }

            public SchemaCurrencyInfo SchemaCurrency { get; set; }
            public List<MatchSuggestionRow> Rows { get; set; }
        }

        private class MatchSuggestionRow
        {
            public int C_Payment_ID { get; set; }
            public int C_Invoice_ID { get; set; }
            public int C_InvoicePaySchedule_ID { get; set; }
            public int C_BPartner_ID { get; set; }
            public int C_Currency_ID { get; set; }

            public string VendorName { get; set; }
            public string PaymentDocumentNo { get; set; }
            public string InvoiceDocumentNo { get; set; }
            public string CurrencyISOCode { get; set; }
            public string CurrencySymbol { get; set; }
            public string Confidence { get; set; }

            public DateTime PaymentDateAcct { get; set; }
            public DateTime DateInvoiced { get; set; }
            public DateTime DueDate { get; set; }

            public decimal PaymentAmount { get; set; }
            public decimal PaymentOpenAmount { get; set; }
            public decimal InvoiceOpenAmount { get; set; }
            public decimal ReadyAmount { get; set; }
            public decimal AccountingAmount { get; set; }

            public int Score { get; set; }
        }

        private class SchemaCurrencyInfo
        {
            public int C_Currency_ID { get; set; }
            public string ISO_Code { get; set; }
            public string CurSymbol { get; set; }
            public int StdPrecision { get; set; }
        }

        private class ApplyResult
        {
            public bool Success { get; set; }
            public string DocumentNo { get; set; }
            public string Message { get; set; }
        }
    }
}