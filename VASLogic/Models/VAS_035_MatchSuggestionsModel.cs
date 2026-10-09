/******************************************************
 * Module Name    : VASLogic
 * Purpose        : Match Suggestions dashboard widget data (Widget 19)
 * chronological  : Development
 * Created Date   : 2026-06-04
 * Created by     : VAI145
 ******************************************************/

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Process;
using VAdvantage.Utility;

namespace VASLogic.Models
{
    /// <summary>
    /// Module Name : VAS_035_MatchSuggestions
    /// Purpose     : Backs the VAS_035_MatchSuggestions dashboard widget. Pairs
    ///               each unallocated, completed customer receipt of the last
    ///               30 days (by accounting date) with its best-fit open sales
    ///               invoice for the same business partner and ranks the
    ///               pairing by an amount-agreement confidence (HIGH / REVIEW;
    ///               LOW pairings are filtered out). The invoice open amount is
    ///               converted to the RECEIPT (payment) currency at the payment
    ///               accounting date, so the compare — and everything the
    ///               widget displays — happens in the payment's own currency
    ///               (no base-currency conversion anywhere). ApplyAllocation
    ///               creates and completes a C_AllocationHdr (one line per
    ///               invoice pay schedule covered) dated on the payment
    ///               accounting date — the same outcome as the standard
    ///               Allocation form. MRole row-level security is applied only
    ///               on the main physical table (C_Payment, alias Payment)
    ///               inside the OpenReceipts / ReceiptData CTE bodies — never
    ///               on CTE aliases nor on the outer combined query. The only
    ///               dialect-specific SQL is the last-30-days receipt window
    ///               (the PostgreSQL date-diff quirk); the match-signal date
    ///               window is computed in C#.
    /// Chronological development:
    ///   VAI145      2026-06-04 Created
    ///   VAI145      2026-06-04 PaymentAmount instead of PayAmt; last-30-days
    ///                          receipt filter; invoice open amount converted to
    ///                          the payment currency at the payment date.
    ///   VAI145      2026-06-04 Dropped base-currency conversion; switched all
    ///                          receipt dates to DateAcct; ApplyAllocation
    ///                          creates + completes the allocation document.
    ///   VAI145      2026-06-04 Receipt open amount via ALLOCPAYMENTAVAILABLE;
    ///                          suggestions made (and applied) per individual
    ///                          C_InvoicePaySchedule_ID.
    ///   VAI_145    2026-07-17 Sign-aware pairing: schedule open amount signed by
    ///                          DocBaseType (ARI positive, ARC negative), so a
    ///                          positive receipt matches only regular AR invoices
    ///                          and a negative receipt matches only AR credit
    ///                          memos (ARC). Enforced across the list, the detail
    ///                          modal and ApplyAllocation (with a magnitude-aware
    ///                          applied amount and a server-side sign guard).
    /// </summary>
    public class VAS_035_MatchSuggestionsModel
    {
        /// <summary>Exact-amount tolerance in payment-currency units (PROMPT.md ":AmountTolerance").</summary>
        private const decimal AMOUNT_TOLERANCE = 1m;

        /// <summary>Amount difference percentage still considered a HIGH match.</summary>
        private const decimal HIGH_PCT_THRESHOLD = 10m;

        /// <summary>Amount difference percentage still surfaced for REVIEW; above this the pairing is dropped.</summary>
        private const decimal REVIEW_PCT_THRESHOLD = 30m;

        /// <summary>Receipt-date / invoice-due-date proximity window in days (PROMPT.md ":DateWindowDays").</summary>
        private const int DATE_WINDOW_DAYS = 10;

        /// <summary>Rolling window (days) of receipts considered by the widget.</summary>
        private const int RECEIPT_WINDOW_DAYS = 30;

        /// <summary>AD_Form classname of the standard Allocation form opened by the widget.</summary>
        private const string ALLOCATION_FORM_CLASSNAME = "VAdvantage.Apps.AForms.VAllocation";

        /// <summary>
        /// Returns one page of best-fit receipt↔invoice pairings (one suggestion
        /// per receipt of the last 30 accounting days, the closest-amount open
        /// invoice of the same partner), ordered HIGH before REVIEW and then by
        /// smallest amount difference. Amount comparison, the displayed invoice
        /// open amount and the footer ready-to-allocate total are all in the
        /// receipt's own currency (converted at the payment accounting date) —
        /// no base-currency conversion. The envelope also carries the full-set
        /// suggestion count and the AD_Form_ID of the standard Allocation form.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="pageNo">1-based page number.</param>
        /// <param name="pageSize">Rows per page (default 6).</param>
        /// <returns>Populated <see cref="MatchSuggestionList"/> (Rows may be empty).</returns>
        public MatchSuggestionList GetMatchSuggestions(Ctx ctx, int pageNo, int pageSize)
        {
            MatchSuggestionList result = new MatchSuggestionList();
            result.Rows = new List<MatchSuggestionRow>();

            if (ctx == null) { return result; }
            if (pageNo <= 0) { pageNo = 1; }
            if (pageSize <= 0) { pageSize = 5; }

            int clientId = ctx.GetAD_Client_ID();
            int offset = (pageNo - 1) * pageSize;

            /* Last-30-days window on the ACCOUNTING date, built per dialect.
               PostgreSQL: DateAcct behaves as a timestamp here, so
               (date - integer) is the only well-defined day arithmetic;
               CURRENT_DATE + 1 with < includes every receipt dated today
               regardless of its time part. Oracle: TRUNC drops the time
               component so the range is whole days. */
            string dateCondition;
            if (DB.IsPostgreSQL())
            {
                dateCondition = "Payment.DateAcct >= CURRENT_DATE - " + RECEIPT_WINDOW_DAYS + " AND Payment.DateAcct < CURRENT_DATE + 1";
            }
            else
            {
                dateCondition = "TRUNC(Payment.DateAcct) >= TRUNC(SYSDATE) - " + RECEIPT_WINDOW_DAYS + " AND TRUNC(Payment.DateAcct) <= TRUNC(SYSDATE)";
            }

            /* OpenReceipts CTE — the main physical table is C_Payment (alias
               Payment): completed, active, unallocated customer receipts of the
               last 30 accounting days. The amount open for allocation comes
               from the framework function ALLOCPAYMENTAVAILABLE (payment's own
               currency, net of existing allocations). MRole is applied to this
               body. */
            string openReceiptsSql = @"
                SELECT Payment.C_Payment_ID AS Payment_ID,
                       Payment.AD_Client_ID AS Client_ID,
                       Payment.AD_Org_ID AS Org_ID,
                       Payment.C_BPartner_ID AS BPartner_ID,
                       BPartner.Name AS Customer_Name,
                       Payment.DocumentNo AS Receipt_No,
                       Payment.DateAcct AS Receipt_Date,
                       Payment.C_Currency_ID AS Receipt_Currency_ID,
                       Payment.C_ConversionType_ID AS Receipt_ConversionType_ID,
                       ReceiptCurrency.ISO_Code AS Receipt_Currency,
                       CASE WHEN ReceiptCurrency.CurSymbol IS NOT NULL THEN ReceiptCurrency.CurSymbol ELSE ReceiptCurrency.ISO_Code END AS Receipt_Currency_Symbol,
                       ReceiptCurrency.StdPrecision AS Receipt_Precision,
                       COALESCE(ALLOCPAYMENTAVAILABLE(Payment.C_Payment_ID), 0) AS Receipt_Amount
                FROM C_Payment Payment
                INNER JOIN C_BPartner BPartner ON (BPartner.C_BPartner_ID=Payment.C_BPartner_ID)
                INNER JOIN C_Currency ReceiptCurrency ON (ReceiptCurrency.C_Currency_ID=Payment.C_Currency_ID)
                WHERE Payment.IsReceipt = 'Y'
                  AND Payment.IsActive = 'Y'
                  AND NVL(Payment.VA009_OrderPaySchedule_ID, 0) = 0 
                  AND Payment.DocStatus IN ('CO', 'CL')
                  AND COALESCE(Payment.IsAllocated, 'N') = 'N'
                  AND COALESCE(ALLOCPAYMENTAVAILABLE(Payment.C_Payment_ID), 0) != 0
                  AND " + dateCondition + @"
                  AND Payment.AD_Client_ID = " + clientId;

            /* MRole only on the main physical table (C_Payment / alias Payment). */
            openReceiptsSql = MRole.GetDefault(ctx).AddAccessSQL(
                openReceiptsSql,
                "Payment",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            string sql = @"
                WITH OpenReceipts AS (
                    " + openReceiptsSql + @"
                ),
                ReceiptPartners AS (
                    SELECT DISTINCT OpenReceipts.Client_ID, OpenReceipts.BPartner_ID, OpenReceipts.Org_ID  
                    FROM OpenReceipts OpenReceipts
                ),
                OpenSchedules AS (
                    SELECT PaySchedule.C_InvoicePaySchedule_ID AS PaySchedule_ID,
                           Invoice.C_Invoice_ID AS Invoice_ID,
                           Invoice.AD_Client_ID AS Client_ID,
                           Invoice.C_BPartner_ID AS BPartner_ID,
                           Invoice.DocumentNo AS Invoice_No,
                           Invoice.DateInvoiced AS Invoice_Date,
                           PaySchedule.DueDate AS Due_Date,
                           Invoice.C_Currency_ID AS Invoice_Currency_ID,
                           InvDocType.DocBaseType AS Doc_Base_Type,
                           CASE WHEN InvDocType.DocBaseType = 'ARC'
                                THEN -(COALESCE(PaySchedule.DueAmt, 0))
                                ELSE (COALESCE(PaySchedule.DueAmt, 0))
                           END AS Open_Amount
                    FROM ReceiptPartners ReceiptPartners
                    INNER JOIN C_Invoice Invoice ON (Invoice.AD_Client_ID=ReceiptPartners.Client_ID AND Invoice.AD_Org_ID=ReceiptPartners.Org_ID
                                AND Invoice.C_BPartner_ID=ReceiptPartners.BPartner_ID)
                    INNER JOIN C_DocType InvDocType ON (InvDocType.C_DocType_ID=Invoice.C_DocTypeTarget_ID)
                    INNER JOIN C_InvoicePaySchedule PaySchedule ON (PaySchedule.C_Invoice_ID=Invoice.C_Invoice_ID)
                    WHERE Invoice.IsSOTrx = 'Y'
                      AND Invoice.IsActive = 'Y'
                      AND Invoice.DocStatus IN ('CO', 'CL')
                      AND COALESCE(Invoice.IsPaid, 'N') = 'N'
                      AND InvDocType.DocBaseType IN ('ARI', 'ARC')
                      AND PaySchedule.IsActive = 'Y'
                      AND COALESCE(PaySchedule.IsValid, 'Y') = 'Y'
                      AND COALESCE(PaySchedule.VA009_IsPaid, 'N') = 'N'
                      AND COALESCE(PaySchedule.DueAmt, 0) <> 0
                ),
                MatchCandidates AS (
                    SELECT Receipt.Payment_ID,
                           Receipt.Customer_Name,
                           Receipt.Receipt_No,
                           Receipt.Receipt_Date,
                           Receipt.Receipt_Currency,
                           Receipt.Receipt_Currency_Symbol,
                           Receipt.Receipt_Precision,
                           Receipt.Receipt_Amount,
                           Schedule.PaySchedule_ID,
                           Schedule.Invoice_ID,
                           Schedule.Invoice_No,
                           Schedule.Invoice_Date,
                           Schedule.Due_Date,
                           /* Schedule open amount in the RECEIPT currency, converted
                              at the payment accounting date with the payment's
                              conversion type. */
                           CASE
                               WHEN Schedule.Invoice_Currency_ID = Receipt.Receipt_Currency_ID
                               THEN Schedule.Open_Amount
                               ELSE CurrencyConvert(
                                   Schedule.Open_Amount,
                                   Schedule.Invoice_Currency_ID,
                                   Receipt.Receipt_Currency_ID,
                                   Receipt.Receipt_Date,
                                   Receipt.Receipt_ConversionType_ID,
                                   Receipt.Client_ID,
                                   Receipt.Org_ID
                               )
                           END AS Open_Amount_Pay
                    FROM OpenReceipts Receipt
                    INNER JOIN OpenSchedules Schedule ON (Schedule.Client_ID=Receipt.Client_ID AND Schedule.BPartner_ID=Receipt.BPartner_ID
                                AND ((Receipt.Receipt_Amount > 0 AND Schedule.Doc_Base_Type = 'ARI')
                                  OR (Receipt.Receipt_Amount < 0 AND Schedule.Doc_Base_Type = 'ARC')))
                ),
                ScoredCandidates AS (
                    SELECT MatchCandidates.Payment_ID,
                           MatchCandidates.Customer_Name,
                           MatchCandidates.Receipt_No,
                           MatchCandidates.Receipt_Date,
                           MatchCandidates.Receipt_Currency,
                           MatchCandidates.Receipt_Currency_Symbol,
                           MatchCandidates.Receipt_Precision,
                           MatchCandidates.Receipt_Amount,
                           MatchCandidates.PaySchedule_ID,
                           MatchCandidates.Invoice_ID,
                           MatchCandidates.Invoice_No,
                           MatchCandidates.Invoice_Date,
                           MatchCandidates.Due_Date,
                           MatchCandidates.Open_Amount_Pay,
                           ABS(MatchCandidates.Receipt_Amount - MatchCandidates.Open_Amount_Pay) AS Difference_Amount,
                           CASE
                               WHEN ABS(MatchCandidates.Receipt_Amount - MatchCandidates.Open_Amount_Pay) <= " + AMOUNT_TOLERANCE.ToString(CultureInfo.InvariantCulture) + @" THEN 'HIGH'
                               WHEN MatchCandidates.Open_Amount_Pay <> 0
                                    AND ABS(MatchCandidates.Receipt_Amount - MatchCandidates.Open_Amount_Pay) * 100 / ABS(MatchCandidates.Open_Amount_Pay) <= " + HIGH_PCT_THRESHOLD.ToString(CultureInfo.InvariantCulture) + @" THEN 'HIGH'
                               WHEN MatchCandidates.Open_Amount_Pay <> 0
                                    AND ABS(MatchCandidates.Receipt_Amount - MatchCandidates.Open_Amount_Pay) * 100 / ABS(MatchCandidates.Open_Amount_Pay) <= " + REVIEW_PCT_THRESHOLD.ToString(CultureInfo.InvariantCulture) + @" THEN 'REVIEW'
                               ELSE 'LOW'
                           END AS Match_Confidence,
                           ROW_NUMBER() OVER (
                               PARTITION BY MatchCandidates.Payment_ID
                               ORDER BY ABS(MatchCandidates.Receipt_Amount - MatchCandidates.Open_Amount_Pay), MatchCandidates.Due_Date, MatchCandidates.Invoice_No, MatchCandidates.PaySchedule_ID
                           ) AS Match_Rank
                    FROM MatchCandidates
                )
                SELECT ScoredCandidates.Payment_ID,
                       ScoredCandidates.Customer_Name,
                       ScoredCandidates.Receipt_No,
                       ScoredCandidates.Receipt_Date,
                       ScoredCandidates.Receipt_Currency,
                       ScoredCandidates.Receipt_Currency_Symbol,
                       ScoredCandidates.Receipt_Precision,
                       ScoredCandidates.Receipt_Amount,
                       ScoredCandidates.PaySchedule_ID,
                       ScoredCandidates.Invoice_ID,
                       ScoredCandidates.Invoice_No,
                       ScoredCandidates.Invoice_Date,
                       ScoredCandidates.Due_Date,
                       ScoredCandidates.Open_Amount_Pay,
                       ScoredCandidates.Match_Confidence,
                       COUNT(*) OVER () AS Total_Records,
                       SUM(ScoredCandidates.Receipt_Amount) OVER () AS Ready_To_Allocate
                FROM ScoredCandidates
                WHERE ScoredCandidates.Match_Rank = 1
                  AND ScoredCandidates.Match_Confidence IN ('HIGH', 'REVIEW', 'LOW')
                ORDER BY CASE WHEN ScoredCandidates.Match_Confidence = 'HIGH' THEN 1
                              WHEN ScoredCandidates.Match_Confidence = 'REVIEW' THEN 2
                              ELSE 3 END,
                         ScoredCandidates.Customer_Name,
                         ScoredCandidates.Receipt_Date DESC,
                         ScoredCandidates.Difference_Amount
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@Offset", offset),
                new SqlParameter("@PageSize", pageSize)
            };

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, parameters);
                while (dr != null && dr.Read())
                {
                    int receiptPrecision = Util.GetValueOfInt(dr["Receipt_Precision"]);

                    /* Full-set figures ride on every row; capture once. The
                       footer total stays in the receipt currency (no
                       base-currency conversion), so its symbol/precision come
                       from the first (best-ranked) row. */
                    if (result.Rows.Count == 0)
                    {
                        result.TotalRecords = Util.GetValueOfInt(dr["Total_Records"]);
                        result.CurrencySymbol = Util.GetValueOfString(dr["Receipt_Currency_Symbol"]);
                        result.CurrencyIso = Util.GetValueOfString(dr["Receipt_Currency"]);
                        result.Precision = receiptPrecision;
                        result.ReadyToAllocate = Math.Round(
                            Util.GetValueOfDecimal(dr["Ready_To_Allocate"]),
                            receiptPrecision,
                            MidpointRounding.AwayFromZero
                        );
                    }

                    result.Rows.Add(new MatchSuggestionRow
                    {
                        PaymentId = Util.GetValueOfInt(dr["Payment_ID"]),
                        InvoiceId = Util.GetValueOfInt(dr["Invoice_ID"]),
                        InvoicePayScheduleId = Util.GetValueOfInt(dr["PaySchedule_ID"]),
                        Customer = Util.GetValueOfString(dr["Customer_Name"]),
                        ReceiptNo = Util.GetValueOfString(dr["Receipt_No"]),
                        ReceiptDate = FormatDate(Util.GetValueOfDateTime(dr["Receipt_Date"])),
                        ReceiptCurrency = Util.GetValueOfString(dr["Receipt_Currency"]),
                        ReceiptCurrencySymbol = Util.GetValueOfString(dr["Receipt_Currency_Symbol"]),
                        ReceiptPrecision = receiptPrecision,
                        ReceiptAmount = Math.Round(Util.GetValueOfDecimal(dr["Receipt_Amount"]), receiptPrecision, MidpointRounding.AwayFromZero),
                        InvoiceNo = Util.GetValueOfString(dr["Invoice_No"]),
                        InvoiceDate = FormatDate(Util.GetValueOfDateTime(dr["Invoice_Date"])),
                        DueDate = FormatDate(Util.GetValueOfDateTime(dr["Due_Date"])),
                        /* Invoice open amount already converted to the receipt
                           currency at the payment accounting date — display with
                           the receipt symbol/precision. */
                        OpenAmount = Math.Round(Util.GetValueOfDecimal(dr["Open_Amount_Pay"]), receiptPrecision, MidpointRounding.AwayFromZero),
                        Confidence = Util.GetValueOfString(dr["Match_Confidence"])
                    });
                }
            }
            finally
            {
                if (dr != null)
                {
                    dr.Close();
                    dr.Dispose();
                }
            }

            result.PageNo = pageNo;
            result.PageSize = pageSize;
            result.TotalPages = pageSize == 0 ? 0 : Convert.ToInt32(Math.Ceiling((decimal)result.TotalRecords / pageSize));
            result.AllocationFormId = GetAllocationFormId();

            return result;
        }

        /// <summary>
        /// Returns the match-review detail for a single receipt↔invoice-schedule
        /// pairing: the payment (receipt) pane, the suggested-invoice pane (open
        /// amount / due date of the SUGGESTED pay schedule only), the
        /// balance-after-apply (receipt currency, converted at the payment
        /// accounting date) and the four "why this match" signal flags plus the
        /// 0..100 evidence score. The signals are computed in C# from the
        /// fetched values so no dialect-specific date arithmetic is needed in SQL.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="paymentId">C_Payment_ID of the receipt side.</param>
        /// <param name="invoiceId">C_Invoice_ID of the suggested invoice.</param>
        /// <param name="payScheduleId">C_InvoicePaySchedule_ID the suggestion was made with.</param>
        /// <returns>Populated <see cref="MatchDetail"/>, or null when either side is not found / not accessible.</returns>
        public MatchDetail GetMatchDetail(Ctx ctx, int paymentId, int invoiceId, int payScheduleId)
        {
            if (ctx == null || paymentId <= 0 || invoiceId <= 0 || payScheduleId <= 0) { return null; }

            /* ReceiptData CTE — main physical table C_Payment (alias Payment);
               MRole keeps a hand-crafted ID from leaking another org's receipt. */
            string receiptDataSql = @"
                SELECT Payment.C_Payment_ID AS Payment_ID,
                       Payment.AD_Client_ID AS Client_ID,
                       Payment.AD_Org_ID AS Org_ID,
                       Payment.C_BPartner_ID AS Receipt_BPartner_ID,
                       Payment.DocumentNo AS Receipt_No,
                       Payment.DateAcct AS Receipt_Date,
                       Payment.C_Currency_ID AS Receipt_Currency_ID,
                       Payment.C_ConversionType_ID AS Receipt_ConversionType_ID,
                       BPartner.Name AS Receipt_Customer,
                       DocType.Name AS Receipt_DocType,
                       DocType.DocBaseType AS Receipt_DocBaseType,
                       PayMethod.VA009_Name AS Payment_Method,
                       COALESCE(Payment.TrxNo, Payment.CheckNo) AS Reference_No,
                       Bank.Name AS Bank_Name,
                       BankAccount.AccountNo AS Account_No,
                       ReceiptCurrency.ISO_Code AS Receipt_Currency,
                       CASE WHEN ReceiptCurrency.CurSymbol IS NOT NULL THEN ReceiptCurrency.CurSymbol ELSE ReceiptCurrency.ISO_Code END AS Receipt_Currency_Symbol,
                       ReceiptCurrency.StdPrecision AS Receipt_Precision,
                       COALESCE(ALLOCPAYMENTAVAILABLE(Payment.C_Payment_ID), 0) AS Receipt_Amount
                FROM C_Payment Payment
                INNER JOIN C_BPartner BPartner ON (BPartner.C_BPartner_ID=Payment.C_BPartner_ID)
                INNER JOIN C_DocType DocType ON (DocType.C_DocType_ID=Payment.C_DocType_ID)
                INNER JOIN C_Currency ReceiptCurrency ON (ReceiptCurrency.C_Currency_ID=Payment.C_Currency_ID)
                INNER JOIN VA009_PaymentMethod PayMethod ON (PayMethod.VA009_PaymentMethod_ID=Payment.VA009_PaymentMethod_ID)
                INNER JOIN C_BankAccount BankAccount ON (BankAccount.C_BankAccount_ID=Payment.C_BankAccount_ID)
                INNER JOIN C_Bank Bank ON (Bank.C_Bank_ID=BankAccount.C_Bank_ID)
                WHERE Payment.C_Payment_ID = @PaymentId
                  AND Payment.IsReceipt = 'Y'
                  AND Payment.IsActive = 'Y'
                  AND Payment.DocStatus IN ('CO', 'CL')";

            /* MRole only on the main physical table (C_Payment / alias Payment). */
            receiptDataSql = MRole.GetDefault(ctx).AddAccessSQL(
                receiptDataSql,
                "Payment",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            /* InvoiceData CTE — main physical table C_Invoice (alias Invoice);
               MRole keeps a hand-crafted ID from leaking another org's invoice. */
            string invoiceDataSql = @"
                    SELECT Invoice.C_Invoice_ID AS Invoice_ID,
                           PaySchedule.C_InvoicePaySchedule_ID AS PaySchedule_ID,
                           Invoice.C_BPartner_ID AS Invoice_BPartner_ID,
                           Invoice.DocumentNo AS Invoice_No,
                           Invoice.DateInvoiced AS Invoice_Date,
                           BPartner.Name AS Invoice_Customer,
                           DocType.Name AS Invoice_DocType,
                           DocType.DocBaseType AS Invoice_DocBaseType,
                           PaymentTerm.Name AS Payment_Terms,
                           PaySchedule.DueDate AS Due_Date,
                           Invoice.GrandTotal AS Grand_Total,
                           Invoice.C_Currency_ID AS Invoice_Currency_ID,
                           InvoiceCurrency.ISO_Code AS Invoice_Currency,
                           CASE WHEN InvoiceCurrency.CurSymbol IS NOT NULL THEN InvoiceCurrency.CurSymbol ELSE InvoiceCurrency.ISO_Code END AS Invoice_Currency_Symbol,
                           InvoiceCurrency.StdPrecision AS Invoice_Precision,
                           /* Signed open amount — AR credit memos (ARC) come back
                              negative so the modal, the balance line and the amount
                              signal all agree in sign with a negative receipt. */
                           CASE WHEN DocType.DocBaseType = 'ARC'
                                THEN -(COALESCE(PaySchedule.DueAmt, 0))
                                ELSE (COALESCE(PaySchedule.DueAmt, 0))
                           END AS Open_Amount
                    FROM C_Invoice Invoice
                    INNER JOIN C_BPartner BPartner ON (BPartner.C_BPartner_ID=Invoice.C_BPartner_ID)
                    INNER JOIN C_DocType DocType ON (DocType.C_DocType_ID=Invoice.C_DocTypeTarget_ID)
                    INNER JOIN C_InvoicePaySchedule PaySchedule ON (PaySchedule.C_Invoice_ID=Invoice.C_Invoice_ID)
                    INNER JOIN C_Currency InvoiceCurrency ON (InvoiceCurrency.C_Currency_ID=Invoice.C_Currency_ID)
                    INNER JOIN C_PaymentTerm PaymentTerm ON (PaymentTerm.C_PaymentTerm_ID=Invoice.C_PaymentTerm_ID)
                    WHERE Invoice.C_Invoice_ID = @InvoiceId
                      AND PaySchedule.C_InvoicePaySchedule_ID = @PayScheduleId
                      AND Invoice.IsSOTrx = 'Y'
                      AND Invoice.IsActive = 'Y'
                      AND Invoice.DocStatus IN ('CO', 'CL')
                      AND PaySchedule.IsActive = 'Y'
                      AND COALESCE(PaySchedule.IsValid, 'Y') = 'Y'
                      AND COALESCE(PaySchedule.VA009_IsPaid, 'N') = 'N'
                      AND COALESCE(PaySchedule.DueAmt, 0) <> 0";

            /* MRole only on the main physical table (C_Invoice / alias Invoice). */
            invoiceDataSql = MRole.GetDefault(ctx).AddAccessSQL(
                invoiceDataSql,
                "Invoice",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            string sql = @"
                WITH ReceiptData AS (
                    " + receiptDataSql + @"
                ),
                InvoiceData AS (
                    " + invoiceDataSql + @"
                )
                SELECT ReceiptData.Payment_ID,
                       ReceiptData.Receipt_BPartner_ID,
                       ReceiptData.Receipt_No,
                       ReceiptData.Receipt_Date,
                       ReceiptData.Receipt_Customer,
                       ReceiptData.Receipt_DocType,
                       ReceiptData.Receipt_DocBaseType,
                       ReceiptData.Payment_Method,
                       ReceiptData.Reference_No,
                       ReceiptData.Bank_Name,
                       ReceiptData.Account_No,
                       ReceiptData.Receipt_Currency,
                       ReceiptData.Receipt_Currency_Symbol,
                       ReceiptData.Receipt_Precision,
                       ReceiptData.Receipt_Amount,
                       InvoiceData.Invoice_ID,
                       InvoiceData.PaySchedule_ID,
                       InvoiceData.Invoice_BPartner_ID,
                       InvoiceData.Invoice_No,
                       InvoiceData.Invoice_Date,
                       InvoiceData.Invoice_Customer,
                       InvoiceData.Invoice_DocType,
                       InvoiceData.Invoice_DocBaseType,
                       InvoiceData.Payment_Terms,
                       InvoiceData.Due_Date,
                       InvoiceData.Grand_Total,
                       InvoiceData.Invoice_Currency,
                       InvoiceData.Invoice_Currency_Symbol,
                       InvoiceData.Invoice_Precision,
                       InvoiceData.Open_Amount,
                       /* Invoice open amount in the RECEIPT currency, converted
                          at the payment accounting date with the payment's
                          conversion type. */
                       CASE
                           WHEN InvoiceData.Invoice_Currency_ID = ReceiptData.Receipt_Currency_ID
                           THEN InvoiceData.Open_Amount
                           ELSE CurrencyConvert(
                               InvoiceData.Open_Amount,
                               InvoiceData.Invoice_Currency_ID,
                               ReceiptData.Receipt_Currency_ID,
                               ReceiptData.Receipt_Date,
                               ReceiptData.Receipt_ConversionType_ID,
                               ReceiptData.Client_ID,
                               ReceiptData.Org_ID
                           )
                       END AS Open_Amount_Pay
                FROM ReceiptData ReceiptData
                CROSS JOIN InvoiceData InvoiceData";

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@PaymentId", paymentId),
                new SqlParameter("@InvoiceId", invoiceId),
                new SqlParameter("@PayScheduleId", payScheduleId)
            };

            MatchDetail detail = null;

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, parameters);
                if (dr != null && dr.Read())
                {
                    int receiptPrecision = Util.GetValueOfInt(dr["Receipt_Precision"]);
                    int invoicePrecision = Util.GetValueOfInt(dr["Invoice_Precision"]);

                    DateTime? receiptDate = Util.GetValueOfDateTime(dr["Receipt_Date"]);
                    DateTime? dueDate = Util.GetValueOfDateTime(dr["Due_Date"]);

                    decimal receiptAmount = Util.GetValueOfDecimal(dr["Receipt_Amount"]);
                    /* Invoice open amount in the receipt currency (payment accounting-date rate). */
                    decimal openAmountPay = Util.GetValueOfDecimal(dr["Open_Amount_Pay"]);

                    string receiptNo = Util.GetValueOfString(dr["Receipt_No"]);
                    string invoiceNo = Util.GetValueOfString(dr["Invoice_No"]);
                    string referenceNo = Util.GetValueOfString(dr["Reference_No"]);

                    /* ── "Why this match" signals (PROMPT.md match scoring) ── */

                    /* Partner matches — both legs belong to the same business partner. */
                    bool partnerOk = Util.GetValueOfInt(dr["Receipt_BPartner_ID"]) == Util.GetValueOfInt(dr["Invoice_BPartner_ID"]);

                    /* Amount matches — within the absolute tolerance or the HIGH
                       percentage window, compared in the receipt currency. */
                    decimal differencePay = Math.Abs(receiptAmount - openAmountPay);
                    decimal differencePct = openAmountPay == 0 ? 100 : differencePay * 100 / Math.Abs(openAmountPay);
                    bool amountOk = differencePay <= AMOUNT_TOLERANCE || differencePct <= HIGH_PCT_THRESHOLD;

                    /* Reference cited — the invoice DocumentNo appears in the receipt's TrxNo / CheckNo. */
                    bool refOk = !string.IsNullOrEmpty(invoiceNo)
                        && !string.IsNullOrEmpty(referenceNo)
                        && referenceNo.IndexOf(invoiceNo, StringComparison.OrdinalIgnoreCase) >= 0;

                    /* Within due window — receipt accounting date within DATE_WINDOW_DAYS of the earliest open due date. */
                    int dateGapDays = receiptDate.HasValue && dueDate.HasValue
                        ? Math.Abs((int)(receiptDate.Value.Date - dueDate.Value.Date).TotalDays)
                        : int.MaxValue;
                    bool dateOk = dateGapDays <= DATE_WINDOW_DAYS;

                    /* Evidence score (0..100): amount 55 / 35 / 20,
                       due window +13, partner +32 — exact + cited + in-window = 100. */
                    int score = (amountOk ? 55 : (receiptAmount < openAmountPay ? 35 : 20))
                        + (dateOk ? 13 : 0)
                        + (partnerOk ? 32 : 0);

                    /* Verdict mirrors the list query's amount-agreement confidence. */
                    string confidence = (differencePay <= AMOUNT_TOLERANCE || differencePct <= HIGH_PCT_THRESHOLD)
                        ? "HIGH"
                        : (differencePct <= REVIEW_PCT_THRESHOLD ? "REVIEW" : "LOW");

                    detail = new MatchDetail
                    {
                        PaymentId = Util.GetValueOfInt(dr["Payment_ID"]),
                        ReceiptNo = receiptNo,
                        ReceiptDate = FormatDate(receiptDate),
                        ReceiptCustomer = Util.GetValueOfString(dr["Receipt_Customer"]),
                        ReceiptDocType = Util.GetValueOfString(dr["Receipt_DocType"]),
                        ReceiptDocBaseType = Util.GetValueOfString(dr["Receipt_DocBaseType"]),
                        PaymentMethod = Util.GetValueOfString(dr["Payment_Method"]),
                        Reference = referenceNo,
                        BankName = Util.GetValueOfString(dr["Bank_Name"]),
                        AccountNo = Util.GetValueOfString(dr["Account_No"]),
                        ReceiptCurrency = Util.GetValueOfString(dr["Receipt_Currency"]),
                        ReceiptCurrencySymbol = Util.GetValueOfString(dr["Receipt_Currency_Symbol"]),
                        ReceiptPrecision = receiptPrecision,
                        ReceiptAmount = Math.Round(receiptAmount, receiptPrecision, MidpointRounding.AwayFromZero),

                        InvoiceId = Util.GetValueOfInt(dr["Invoice_ID"]),
                        InvoicePayScheduleId = Util.GetValueOfInt(dr["PaySchedule_ID"]),
                        InvoiceNo = invoiceNo,
                        InvoiceDate = FormatDate(Util.GetValueOfDateTime(dr["Invoice_Date"])),
                        InvoiceCustomer = Util.GetValueOfString(dr["Invoice_Customer"]),
                        InvoiceDocType = Util.GetValueOfString(dr["Invoice_DocType"]),
                        InvoiceDocBaseType = Util.GetValueOfString(dr["Invoice_DocBaseType"]),
                        PaymentTerms = Util.GetValueOfString(dr["Payment_Terms"]),
                        DueDate = FormatDate(dueDate),
                        GrandTotal = Math.Round(Util.GetValueOfDecimal(dr["Grand_Total"]), invoicePrecision, MidpointRounding.AwayFromZero),
                        InvoiceCurrency = Util.GetValueOfString(dr["Invoice_Currency"]),
                        InvoiceCurrencySymbol = Util.GetValueOfString(dr["Invoice_Currency_Symbol"]),
                        InvoicePrecision = invoicePrecision,
                        OpenAmount = Math.Round(Util.GetValueOfDecimal(dr["Open_Amount"]), invoicePrecision, MidpointRounding.AwayFromZero),

                        /* Open amount in the receipt currency (payment accounting-date
                           rate) — also the basis of the balance line and amount signal. */
                        OpenAmountPay = Math.Round(openAmountPay, receiptPrecision, MidpointRounding.AwayFromZero),
                        /* > 0 ⇒ the receipt is short and the apply becomes a part-payment. */
                        BalanceAfterApply = Math.Round(openAmountPay - receiptAmount, receiptPrecision, MidpointRounding.AwayFromZero),

                        PartnerOk = partnerOk,
                        AmountOk = amountOk,
                        RefOk = refOk,
                        DateOk = dateOk,
                        DateGapDays = dateGapDays == int.MaxValue ? -1 : dateGapDays,
                        Score = score > 100 ? 100 : score,
                        Confidence = confidence
                    };
                }
            }
            finally
            {
                if (dr != null)
                {
                    dr.Close();
                    dr.Dispose();
                }
            }

            return detail;
        }

        /// <summary>
        /// Applies one match suggestion: creates a manual C_AllocationHdr in the
        /// payment's currency, dated (DateTrx + DateAcct) on the PAYMENT
        /// ACCOUNTING DATE, with a single C_AllocationLine against EXACTLY the
        /// invoice pay schedule the suggestion was made with (the receipt's
        /// open-for-allocation amount comes from ALLOCPAYMENTAVAILABLE; an
        /// under-payment goes to OverUnderAmt so the schedule balance stays
        /// open), then completes the allocation document and flags the payment
        /// allocated — the same outcome as the standard Allocation form
        /// (vallocation).
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="paymentId">C_Payment_ID of the receipt to apply.</param>
        /// <param name="invoiceId">C_Invoice_ID of the suggested invoice.</param>
        /// <param name="payScheduleId">C_InvoicePaySchedule_ID the suggestion was made with — the only schedule allocated.</param>
        /// <returns><see cref="ApplyResult"/> with Success, the completed allocation DocumentNo and a user message.</returns>
        public ApplyResult ApplyAllocation(Ctx ctx, int paymentId, int invoiceId, int payScheduleId)
        {
            ApplyResult result = new ApplyResult();
            result.Success = false;

            if (ctx == null || paymentId <= 0 || invoiceId <= 0 || payScheduleId <= 0)
            {
                result.Message = Msg.GetMsg(ctx, "FillMandatory");
                return result;
            }

            Trx trx = Trx.GetTrx(Trx.CreateTrxName("AL"));
            try
            {
                /* ── Receipt side ── */
                MPayment payment = new MPayment(ctx, paymentId, trx);
                if (payment.Get_ID() == 0
                    || !payment.IsReceipt()
                    || !(payment.GetDocStatus() == "CO" || payment.GetDocStatus() == "CL"))
                {
                    trx.Rollback();
                    result.Message = Msg.GetMsg(ctx, "VIS_NoRecordFound");
                    return result;
                }

                if (payment.IsAllocated())
                {
                    trx.Rollback();
                    result.Message = Msg.GetMsg(ctx, "PaymentIsAllocated");
                    return result;
                }

                /* Open-for-allocation amount from the framework function —
                   payment's own currency, net of existing allocations. */
                decimal availableAmt = Util.GetValueOfDecimal(DB.ExecuteScalar(
                    "SELECT COALESCE(ALLOCPAYMENTAVAILABLE(" + paymentId + "), 0) FROM C_Payment WHERE C_Payment_ID = " + paymentId,
                    null, trx));
                if (availableAmt == 0)
                {
                    trx.Rollback();
                    result.Message = Msg.GetMsg(ctx, "AmountIsZero");
                    return result;
                }

                DateTime? paymentDateAcct = payment.GetDateAcct();

                /* ── Invoice side: ONLY the pay schedule the suggestion was made with ── */
                string scheduleSql = @"
                    SELECT PaySchedule.C_InvoicePaySchedule_ID AS PaySchedule_ID,
                           COALESCE(PaySchedule.DueAmt, 0) AS Due_Amount,
                           InvDocType.DocBaseType AS Doc_Base_Type,
                           Invoice.C_Invoice_ID AS Invoice_ID,
                           Invoice.C_BPartner_ID AS Invoice_BPartner_ID,
                           Invoice.C_Currency_ID AS Invoice_Currency_ID,
                           Invoice.AD_Client_ID AS Client_ID,
                           Invoice.AD_Org_ID AS Org_ID
                    FROM C_InvoicePaySchedule PaySchedule
                    INNER JOIN C_Invoice Invoice ON (Invoice.C_Invoice_ID=PaySchedule.C_Invoice_ID)
                    INNER JOIN C_DocType InvDocType ON (InvDocType.C_DocType_ID=Invoice.C_DocTypeTarget_ID)
                    WHERE Invoice.C_Invoice_ID = " + invoiceId + @"
                      AND PaySchedule.C_InvoicePaySchedule_ID = " + payScheduleId + @"
                      AND Invoice.IsSOTrx = 'Y'
                      AND Invoice.IsActive = 'Y'
                      AND Invoice.DocStatus IN ('CO', 'CL')
                      AND InvDocType.DocBaseType IN ('ARI', 'ARC')
                      AND PaySchedule.IsActive = 'Y'
                      AND COALESCE(PaySchedule.IsValid, 'Y') = 'Y'
                      AND COALESCE(PaySchedule.VA009_IsPaid, 'N') = 'N'
                      AND COALESCE(PaySchedule.DueAmt, 0) <> 0";

                DataSet dsSchedules = DB.ExecuteDataset(scheduleSql, null, trx);
                if (dsSchedules == null || dsSchedules.Tables.Count == 0 || dsSchedules.Tables[0].Rows.Count == 0)
                {
                    trx.Rollback();
                    result.Message = Msg.GetMsg(ctx, "VIS_NoRecordFound");
                    return result;
                }

                DataRow schedule = dsSchedules.Tables[0].Rows[0];

                string docBaseType = Util.GetValueOfString(schedule["Doc_Base_Type"]);

                /* Sign gate (mirrors the suggestion query): a negative receipt may only
                   be applied to an AR credit memo (ARC); a positive receipt only to a
                   regular AR invoice (ARI). Defends this POST endpoint against a
                   hand-crafted, sign-mismatched pairing. */
                bool signMatches = (availableAmt < 0 && docBaseType == "ARC")
                                   || (availableAmt > 0 && docBaseType == "ARI");
                if (!signMatches)
                {
                    trx.Rollback();
                    result.Message = Msg.GetMsg(ctx, "VIS_NoRecordFound");
                    return result;
                }

                /* Signed schedule due — ARC negative so it nets against the negative
                   receipt (ABS() normalises whatever sign DueAmt is physically stored
                   with). */
                decimal dueAmt = docBaseType == "ARC"
                    ? -Math.Abs(Util.GetValueOfDecimal(schedule["Due_Amount"]))
                    : Math.Abs(Util.GetValueOfDecimal(schedule["Due_Amount"]));
                int invoiceCurrencyId = Util.GetValueOfInt(schedule["Invoice_Currency_ID"]);

                /* Schedule due in the PAYMENT currency, converted at the
                   payment accounting date (same basis as the suggestion). */
                decimal duePay = dueAmt;
                if (invoiceCurrencyId != payment.GetC_Currency_ID())
                {
                    duePay = MConversionRate.Convert(ctx, dueAmt,
                        invoiceCurrencyId, payment.GetC_Currency_ID(),
                        paymentDateAcct, payment.GetC_ConversionType_ID(),
                        Util.GetValueOfInt(schedule["Client_ID"]),
                        Util.GetValueOfInt(schedule["Org_ID"]));
                }

                if (duePay == 0)
                {
                    trx.Rollback();
                    result.Message = Msg.GetMsg(ctx, "AmountIsZero");
                    return result;
                }

                /* ── Allocation header — dated on the payment accounting date ── */
                MAllocationHdr alloc = new MAllocationHdr(ctx, true,    /* manual */
                    paymentDateAcct, payment.GetC_Currency_ID(),
                    ctx.GetContext("#AD_User_Name"), trx);
                alloc.SetAD_Org_ID(payment.GetAD_Org_ID());
                alloc.SetDateTrx(paymentDateAcct);
                alloc.SetDateAcct(paymentDateAcct);
                alloc.SetC_ConversionType_ID(payment.GetC_ConversionType_ID());
                if (!alloc.Save())
                {
                    trx.Rollback();
                    ValueNamePair pp = VLogger.RetrieveError();
                    string val = pp.GetName();
                    if (pp != null)
                    {
                        if (String.IsNullOrEmpty(val))
                        {
                            val = pp.GetValue();
                        }
                    }
                    result.Message = Msg.GetMsg(ctx, "VIS_AllocationHdrNotSaved")
                        + (!string.IsNullOrEmpty(val) ? " :- " + val : "");
                    return result;
                }

                /* ── Single line against the suggested schedule (payment currency) ──
                   Work in magnitudes then reapply the receipt sign, so we apply the
                   smaller of the receipt's open amount and the schedule due. An
                   over-payment leaves the receipt open; an under-payment leaves the
                   schedule balance open — correct for both positive receipt↔ARI and
                   negative receipt↔ARC (a raw numeric MIN/MAX would pick the wrong
                   operand once the amounts go negative). */
                int applySign = availableAmt < 0 ? -1 : 1;
                decimal appliedAmt = applySign * Math.Min(Math.Abs(availableAmt), Math.Abs(duePay));
                /* Under-payment keeps the remaining schedule balance open. */
                decimal overUnderAmt = duePay - appliedAmt;

                MAllocationLine aLine = new MAllocationLine(alloc, appliedAmt,
                    Env.ZERO, Env.ZERO, overUnderAmt);
                aLine.SetDocInfo(payment.GetC_BPartner_ID(), 0, invoiceId);
                aLine.SetPaymentInfo(payment.GetC_Payment_ID(), 0);
                if (Env.IsModuleInstalled("VA009_"))
                {
                    aLine.SetC_InvoicePaySchedule_ID(payScheduleId);
                }
                aLine.SetDateTrx(paymentDateAcct);
                if (!aLine.Save())
                {
                    trx.Rollback();
                    ValueNamePair pp = VLogger.RetrieveError();
                    string val = pp.GetName();
                    if (pp != null)
                    {
                        if (String.IsNullOrEmpty(val))
                        {
                            val = pp.GetValue();
                        }
                    }
                    result.Message = Msg.GetMsg(ctx, "VIS_AllocLineNotCreated")
                        + (!string.IsNullOrEmpty(val) ? " :- " + val : "");
                    return result;
                }

                /* ── Complete the allocation document ── */
                if (!alloc.ProcessIt(DocActionVariables.ACTION_COMPLETE) || alloc.GetProcessMsg() != null)
                {
                    trx.Rollback();
                    result.Message = Msg.GetMsg(ctx, "VAS_AllocationNotCompDueTo")
                        + " " + alloc.GetProcessMsg();
                    return result;
                }

                if (!alloc.Save())
                {
                    trx.Rollback();
                    ValueNamePair pp = VLogger.RetrieveError();
                    string val = pp.GetName();
                    if (pp != null)
                    {
                        if (String.IsNullOrEmpty(val))
                        {
                            val = pp.GetValue();
                        }
                    }
                    result.Message = Msg.GetMsg(ctx, "VIS_AllocationHdrNotSaved")
                        + (!string.IsNullOrEmpty(val) ? " :- " + val : "");
                    return result;
                }

                /* ── Flag the payment allocated when fully applied ── */
                if (payment.TestAllocation())
                {
                    if (!payment.Save())
                    {
                        trx.Rollback();
                        ValueNamePair pp = VLogger.RetrieveError();
                        string val = pp.GetName();
                        if (pp != null)
                        {
                            if (String.IsNullOrEmpty(val))
                            {
                                val = pp.GetValue();
                            }
                        }
                        result.Message = Msg.GetMsg(ctx, "PaymentNotCreated")
                            + (!string.IsNullOrEmpty(val) ? " :- " + val : "");
                        return result;
                    }
                }

                trx.Commit();

                result.Success = true;
                result.DocumentNo = alloc.GetDocumentNo();
                result.Message = Msg.GetMsg(ctx, "AllocationIsCreated") + " " + alloc.GetDocumentNo();
                return result;
            }
            catch (Exception ex)
            {
                trx.Rollback();
                result.Message = ex.Message;
                return result;
            }
            finally
            {
                trx.Close();
            }
        }

        /// <summary>
        /// Resolves the AD_Form_ID of the standard Allocation form
        /// (VAdvantage.Apps.AForms.VAllocation) the widget opens via
        /// VIS.viewManager.startForm. Returns 0 when the form is not registered.
        /// </summary>
        /// <returns>AD_Form_ID or 0.</returns>
        private static int GetAllocationFormId()
        {
            string sql = @"
                SELECT Form.AD_Form_ID AS Form_ID
                FROM AD_Form Form
                WHERE Form.ClassName = @ClassName
                  AND Form.IsActive = 'Y'";

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@ClassName", ALLOCATION_FORM_CLASSNAME)
            };

            return Util.GetValueOfInt(DB.ExecuteScalar(sql, parameters, null));
        }

        /// <summary>Formats a nullable date as ISO yyyy-MM-dd (empty when null) for the client.</summary>
        /// <param name="value">Date to format.</param>
        /// <returns>ISO date string or "".</returns>
        private static string FormatDate(DateTime? value)
        {
            return value.HasValue
                ? value.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : "";
        }

        /// <summary>
        /// Result envelope for the suggestion list: paged rows plus the full-set
        /// suggestion count, the ready-to-allocate total (receipt currency) and
        /// the Allocation form id for the Open-allocation-form action.
        /// </summary>
        public class MatchSuggestionList
        {
            public List<MatchSuggestionRow> Rows { get; set; }
            public int PageNo { get; set; }
            public int PageSize { get; set; }
            public int TotalRecords { get; set; }
            public int TotalPages { get; set; }
            /// <summary>Sum of the suggestion receipts' open-for-allocation amounts (ALLOCPAYMENTAVAILABLE, receipt currency).</summary>
            public decimal ReadyToAllocate { get; set; }
            public string CurrencySymbol { get; set; }
            public string CurrencyIso { get; set; }
            public int Precision { get; set; }
            public int AllocationFormId { get; set; }
        }

        /// <summary>
        /// One suggestion row: the receipt leg, its best-fit invoice leg and the
        /// confidence tag. OpenAmount is already in the RECEIPT currency
        /// (converted at the payment accounting date), so the receipt
        /// symbol/precision apply to both amounts.
        /// </summary>
        public class MatchSuggestionRow
        {
            public int PaymentId { get; set; }
            public int InvoiceId { get; set; }
            /// <summary>The exact pay schedule the suggestion was made with — the Apply target.</summary>
            public int InvoicePayScheduleId { get; set; }
            public string Customer { get; set; }
            public string ReceiptNo { get; set; }
            public string ReceiptDate { get; set; }
            public string ReceiptCurrency { get; set; }
            public string ReceiptCurrencySymbol { get; set; }
            public int ReceiptPrecision { get; set; }
            public decimal ReceiptAmount { get; set; }
            public string InvoiceNo { get; set; }
            public string InvoiceDate { get; set; }
            public string DueDate { get; set; }
            /// <summary>Invoice open amount in the receipt currency (payment accounting-date rate).</summary>
            public decimal OpenAmount { get; set; }
            /// <summary>HIGH or REVIEW (LOW pairings never reach the widget).</summary>
            public string Confidence { get; set; }
        }

        /// <summary>Match-review modal payload: both panes, balance line and the four match signals.</summary>
        public class MatchDetail
        {
            public int PaymentId { get; set; }
            public string ReceiptNo { get; set; }
            public string ReceiptDate { get; set; }
            public string ReceiptCustomer { get; set; }
            public string ReceiptDocType { get; set; }
            public string ReceiptDocBaseType { get; set; }
            public string PaymentMethod { get; set; }
            public string Reference { get; set; }
            public string BankName { get; set; }
            public string AccountNo { get; set; }
            public string ReceiptCurrency { get; set; }
            public string ReceiptCurrencySymbol { get; set; }
            public int ReceiptPrecision { get; set; }
            public decimal ReceiptAmount { get; set; }

            public int InvoiceId { get; set; }
            /// <summary>The exact pay schedule the suggestion was made with — the Apply target.</summary>
            public int InvoicePayScheduleId { get; set; }
            public string InvoiceNo { get; set; }
            public string InvoiceDate { get; set; }
            public string InvoiceCustomer { get; set; }
            public string InvoiceDocType { get; set; }
            public string InvoiceDocBaseType { get; set; }
            public string PaymentTerms { get; set; }
            public string DueDate { get; set; }
            public decimal GrandTotal { get; set; }
            public string InvoiceCurrency { get; set; }
            public string InvoiceCurrencySymbol { get; set; }
            public int InvoicePrecision { get; set; }
            /// <summary>Open amount in the invoice's own currency (pane display).</summary>
            public decimal OpenAmount { get; set; }

            /// <summary>Open amount in the RECEIPT currency, converted at the payment accounting date.</summary>
            public decimal OpenAmountPay { get; set; }
            /// <summary>OpenAmountPay − receipt amount, receipt currency; &gt; 0 ⇒ part-payment.</summary>
            public decimal BalanceAfterApply { get; set; }

            public bool PartnerOk { get; set; }
            public bool AmountOk { get; set; }
            public bool RefOk { get; set; }
            public bool DateOk { get; set; }
            /// <summary>Whole-day gap between receipt accounting date and due date (-1 when unknown).</summary>
            public int DateGapDays { get; set; }
            /// <summary>Evidence score 0..100 shown in the modal banner.</summary>
            public int Score { get; set; }
            /// <summary>HIGH / REVIEW / LOW verdict driving the banner colour.</summary>
            public string Confidence { get; set; }
        }

        /// <summary>Outcome of ApplyAllocation: Success flag, allocation DocumentNo and a user message.</summary>
        public class ApplyResult
        {
            public bool Success { get; set; }
            public string DocumentNo { get; set; }
            public string Message { get; set; }
        }
    }
}
