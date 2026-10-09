/******************************************************
 * Module Name    : VASLogic
 * Purpose        : Recent AR Receipts dashboard widget data
 * chronological  : Development
 * Created Date   : 2026-05-29
 * Created by     : VAI154
 ******************************************************/

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using VAdvantage.DataBase;
using VAdvantage.Model;
using VAdvantage.Utility;

namespace VASLogic.Models
{
    /// <summary>
    /// Backs the VAS_RecentReceipts dashboard widget. Returns one page of the
    /// most recent AR receipts (C_Payment with IsReceipt='Y') for the session
    /// client and the per-row allocation detail used by the row-click modal.
    /// All amounts stay in the receipt's own currency (no conversion). MRole
    /// row-level security is applied only on each query's main physical table
    /// (C_Payment for the list / header, C_AllocationLine for the modal lines);
    /// the allocated-invoice document-number fallback shown on each list row
    /// is resolved by an OUTER wrapper applied AFTER MRole, so the core does
    /// not pick up the nested C_Invoice alias and append an access predicate
    /// referencing a scope the outer query cannot see. Compatible with Oracle
    /// and PostgreSQL.
    /// </summary>
    public class VAS_RecentReceiptsModel
    {
        /// <summary>
        /// Returns one page of recent AR receipts for the session client,
        /// newest first. Window: receipts whose DateAcct falls in the last
        /// 30 days (inclusive of today). Only completed / closed receipts
        /// are returned; voided / reversed payments are excluded.
        /// </summary>
        public RecentReceiptsPage GetRecentReceipts(Ctx ctx, int pageNo, int pageSize)
        {
            RecentReceiptsPage page = new RecentReceiptsPage();
            page.Rows = new List<RecentReceiptRow>();

            if (ctx == null) { return page; }
            if (pageNo <= 0) { pageNo = 1; }
            if (pageSize <= 0) { pageSize = 5; }

            int offset = (pageNo - 1) * pageSize;
            int clientId = ctx.GetAD_Client_ID();

            /* Last-30-days window built per dialect.
               PostgreSQL: in this deployment DateAcct behaves as a timestamp, so
               TRUNC()-style date arithmetic and DATE-DATE both fail / return an
               INTERVAL. Compare the raw column against CURRENT_DATE +/- N — the
               (date - integer) form is well-defined on both dialects and never
               yields an INTERVAL. CURRENT_DATE + 1 with the < operator includes
               every receipt dated today regardless of the time component.
               Oracle: TRUNC drops the time component so the range is whole days. */
            string fromCondition;
            if (DB.IsPostgreSQL())
            {
                fromCondition = "Payment.DateAcct >= CURRENT_DATE - 29 AND Payment.DateAcct < CURRENT_DATE + 1";
            }
            else
            {
                fromCondition = "TRUNC(Payment.DateAcct) >= TRUNC(SYSDATE) - 29 AND TRUNC(Payment.DateAcct) <= TRUNC(SYSDATE)";
            }

            /* Inner query selects only from C_Payment + clean lookup joins, so
               MRole.AddAccessSQL sees a single physical FROM (alias Payment).
               The allocated-invoice document-number fallback is added in the
               OUTER wrapper below; otherwise MRole picks up the nested
               C_Invoice alias used by the correlated subquery and appends an
               access predicate to the outer WHERE referencing an alias that
               is not in scope. Same pattern as ReceiptsController.SearchReceipts. */
            string innerSql = @"
                SELECT Payment.C_Payment_ID AS Payment_ID,
                       Payment.DocumentNo AS Document_No,
                       Payment.DateAcct AS Date_Acct,
                       BPartner.Name AS Customer_Name,
                       Bank.Name AS Bank_Name,
                       BankAccount.AccountNo AS Account_No,
                       Payment.PaymentAmount AS Pay_Amount,
                       Currency.ISO_Code AS Currency_ISO,
                       CASE WHEN Currency.CurSymbol IS NOT NULL THEN Currency.CurSymbol ELSE Currency.ISO_Code END AS Cur_Symbol,
                       Currency.StdPrecision AS Std_Precision,
                       Payment.DocStatus AS Doc_Status,
                       Payment.IsReconciled AS Is_Reconciled,
                       Payment.IsAllocated AS Is_Allocated,
                       Payment.TenderType AS Tender_Type,
                       Payment.IsReceipt AS Is_Receipt,
                       COALESCE(Payment.Description, Payment.CheckNo) AS Payment_Reference,
                       Invoice.DocumentNo AS Direct_Invoice_Document_No,
                       PM.VA009_Name AS Payment_Method_Name
                FROM C_Payment Payment
                INNER JOIN C_Currency Currency ON (Payment.C_Currency_ID=Currency.C_Currency_ID)
                INNER JOIN C_BankAccount BankAccount ON (Payment.C_BankAccount_ID=BankAccount.C_BankAccount_ID)
                INNER JOIN C_Bank Bank ON (BankAccount.C_Bank_ID=Bank.C_Bank_ID)
                INNER JOIN VA009_PaymentMethod PM ON (Payment.VA009_PaymentMethod_ID=PM.VA009_PaymentMethod_ID)
                LEFT OUTER JOIN C_BPartner BPartner ON (Payment.C_BPartner_ID=BPartner.C_BPartner_ID)
                LEFT OUTER JOIN C_Invoice Invoice ON (Payment.C_Invoice_ID=Invoice.C_Invoice_ID)
                WHERE Payment.IsReceipt = 'Y'
                  AND Payment.IsActive = 'Y'
                  AND Payment.DocStatus IN ('CO', 'CL')
                  AND Payment.AD_Client_ID = " + clientId + @"
                  AND " + fromCondition;

            /* MRole only on the main physical table (C_Payment / alias Payment). */
            innerSql = MRole.GetDefault(ctx).AddAccessSQL(
                innerSql,
                "Payment",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            List<SqlParameter> parameters = new List<SqlParameter>();
            parameters.Add(new SqlParameter("@Offset", offset));
            parameters.Add(new SqlParameter("@PageSize", pageSize));

            /* Count_Data CTE is computed once from the secured inner result and
               cross-joined to every page row, so one round-trip delivers the
               page plus Total_Records for the pager. The allocated-invoice
               COALESCE / correlated subquery lives here (outside MRole). */
            string sql = @"
                WITH Receipts AS (
                    " + innerSql + @"
                ),
                Count_Data AS (
                    SELECT COUNT(1) AS Total_Records FROM Receipts
                )
                SELECT Receipts.Payment_ID,
                       Receipts.Document_No,
                       Receipts.Date_Acct,
                       Receipts.Customer_Name,
                       Receipts.Bank_Name,
                       Receipts.Account_No,
                       Receipts.Pay_Amount,
                       Receipts.Currency_ISO,
                       Receipts.Cur_Symbol,
                       Receipts.Std_Precision,
                       Receipts.Doc_Status,
                       Receipts.Is_Reconciled,
                       Receipts.Is_Allocated,
                       Receipts.Tender_Type,
                       Receipts.Is_Receipt,
                       Receipts.Payment_Reference,
                       Receipts.Payment_Method_Name,
                       COALESCE(
                           Receipts.Direct_Invoice_Document_No,
                           (
                               SELECT MIN(AllocInvoiceDisplay.DocumentNo)
                               FROM C_PaymentAllocate PaymentAllocateDisplay
                               INNER JOIN C_Invoice AllocInvoiceDisplay
                                   ON (PaymentAllocateDisplay.C_Invoice_ID=AllocInvoiceDisplay.C_Invoice_ID)
                               WHERE PaymentAllocateDisplay.C_Payment_ID = Receipts.Payment_ID
                                 AND PaymentAllocateDisplay.IsActive = 'Y'
                                 AND AllocInvoiceDisplay.IsActive = 'Y'
                           ),
                           N''
                       ) AS Invoice_Document_No,
                       Count_Data.Total_Records
                FROM Receipts
                CROSS JOIN Count_Data
                ORDER BY Receipts.Date_Acct DESC, Receipts.Payment_ID DESC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

            int totalRecords = 0;
            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, parameters.ToArray());
                while (dr != null && dr.Read())
                {
                    totalRecords = Util.GetValueOfInt(dr["Total_Records"]);
                    int stdPrecision = Util.GetValueOfInt(dr["Std_Precision"]);
                    DateTime? dateAcct = Util.GetValueOfDateTime(dr["Date_Acct"]);

                    decimal payAmount = Math.Round(
                        Util.GetValueOfDecimal(dr["Pay_Amount"]),
                        stdPrecision,
                        MidpointRounding.AwayFromZero
                    );

                    page.Rows.Add(new RecentReceiptRow
                    {
                        PaymentId = Util.GetValueOfInt(dr["Payment_ID"]),
                        DocumentNo = Util.GetValueOfString(dr["Document_No"]),
                        DateAcct = dateAcct.HasValue
                            ? dateAcct.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                            : "",
                        CustomerName = Util.GetValueOfString(dr["Customer_Name"]),
                        BankName = Util.GetValueOfString(dr["Bank_Name"]),
                        AccountNo = Util.GetValueOfString(dr["Account_No"]),
                        PayAmount = payAmount,
                        CurrencyIso = Util.GetValueOfString(dr["Currency_ISO"]),
                        CurSymbol = Util.GetValueOfString(dr["Cur_Symbol"]),
                        StdPrecision = stdPrecision,
                        DocStatus = Util.GetValueOfString(dr["Doc_Status"]),
                        IsReconciled = Util.GetValueOfString(dr["Is_Reconciled"]) == "Y",
                        IsAllocated = Util.GetValueOfString(dr["Is_Allocated"]) == "Y",
                        IsReceipt = Util.GetValueOfString(dr["Is_Receipt"]) == "Y",
                        TenderType = Util.GetValueOfString(dr["Tender_Type"]),
                        Reference = Util.GetValueOfString(dr["Payment_Reference"]),
                        InvoiceDocumentNo = Util.GetValueOfString(dr["Invoice_Document_No"]),
                        PaymentMethodName = Util.GetValueOfString(dr["Payment_Method_Name"])
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

            page.PageNo = pageNo;
            page.PageSize = pageSize;
            page.TotalRecords = totalRecords;
            page.TotalPages = pageSize == 0
                ? 0
                : Convert.ToInt32(Math.Ceiling((decimal)totalRecords / pageSize));

            return page;
        }

        /// <summary>
        /// Returns the allocation detail for a single receipt, used by the
        /// row-click modal: the receipt header plus one line per posted
        /// allocation (C_AllocationLine.Amount / DiscountAmt / WriteOffAmt /
        /// OverUnderAmt) against the linked invoice. Only allocation headers
        /// in DocStatus 'CO' or 'CL' are returned.
        /// </summary>
        public ReceiptAllocationDetail GetReceiptAllocations(Ctx ctx, int C_Payment_ID)
        {
            ReceiptAllocationDetail detail = new ReceiptAllocationDetail();
            detail.Lines = new List<ReceiptAllocationLine>();

            if (ctx == null || C_Payment_ID <= 0) { return detail; }

            int clientId = ctx.GetAD_Client_ID();

            /* Header — main physical table is C_Payment; lookup joins inherit
               the access filter applied on the Payment alias. */
            string headerSql = @"
                SELECT Payment.C_Payment_ID AS Payment_ID,
                       Payment.DocumentNo AS Document_No,
                       Payment.DateAcct AS Date_Acct,
                       Payment.PayAmt AS Pay_Amount,
                       Payment.DocStatus AS Doc_Status,
                       Payment.IsReconciled AS Is_Reconciled,
                       Payment.IsAllocated AS Is_Allocated,
                       Payment.IsReceipt AS Is_Receipt,
                       Payment.TenderType AS Tender_Type,
                       COALESCE(Payment.Description, Payment.CheckNo) AS Payment_Reference,
                       BPartner.Name AS Customer_Name,
                       Bank.Name AS Bank_Name,
                       BankAccount.AccountNo AS Account_No,
                       Currency.ISO_Code AS Currency_ISO,
                       CASE WHEN Currency.CurSymbol IS NOT NULL THEN Currency.CurSymbol ELSE Currency.ISO_Code END AS Cur_Symbol,
                       Currency.StdPrecision AS Std_Precision,
                       PM.VA009_Name AS Payment_Method_Name
                FROM C_Payment Payment
                INNER JOIN C_Currency Currency ON (Payment.C_Currency_ID=Currency.C_Currency_ID)
                INNER JOIN C_BankAccount BankAccount ON (Payment.C_BankAccount_ID=BankAccount.C_BankAccount_ID)
                INNER JOIN C_Bank Bank ON (BankAccount.C_Bank_ID=Bank.C_Bank_ID)
                INNER JOIN VA009_PaymentMethod PM ON (Payment.VA009_PaymentMethod_ID=PM.VA009_PaymentMethod_ID)
                LEFT OUTER JOIN C_BPartner BPartner ON (Payment.C_BPartner_ID=BPartner.C_BPartner_ID)  
                WHERE Payment.C_Payment_ID = @C_Payment_ID
                  AND Payment.AD_Client_ID = " + clientId;

            headerSql = MRole.GetDefault(ctx).AddAccessSQL(
                headerSql,
                "Payment",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            SqlParameter[] headerParams = new SqlParameter[]
            {
                new SqlParameter("@C_Payment_ID", C_Payment_ID)
            };

            int stdPrecision = 2;
            IDataReader hdr = null;
            try
            {
                hdr = DB.ExecuteReader(headerSql, headerParams);
                if (hdr != null && hdr.Read())
                {
                    stdPrecision = Util.GetValueOfInt(hdr["Std_Precision"]);
                    DateTime? dateAcct = Util.GetValueOfDateTime(hdr["Date_Acct"]);

                    decimal payAmount = Math.Round(
                        Util.GetValueOfDecimal(hdr["Pay_Amount"]),
                        stdPrecision,
                        MidpointRounding.AwayFromZero
                    );

                    detail.PaymentId = Util.GetValueOfInt(hdr["Payment_ID"]);
                    detail.DocumentNo = Util.GetValueOfString(hdr["Document_No"]);
                    detail.DateAcct = dateAcct.HasValue
                        ? dateAcct.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                        : "";
                    detail.CustomerName = Util.GetValueOfString(hdr["Customer_Name"]);
                    detail.BankName = Util.GetValueOfString(hdr["Bank_Name"]);
                    detail.AccountNo = Util.GetValueOfString(hdr["Account_No"]);
                    detail.PayAmount = payAmount;
                    detail.CurrencyIso = Util.GetValueOfString(hdr["Currency_ISO"]);
                    detail.CurSymbol = Util.GetValueOfString(hdr["Cur_Symbol"]);
                    detail.StdPrecision = stdPrecision;
                    detail.DocStatus = Util.GetValueOfString(hdr["Doc_Status"]);
                    detail.IsReconciled = Util.GetValueOfString(hdr["Is_Reconciled"]) == "Y";
                    detail.IsAllocated = Util.GetValueOfString(hdr["Is_Allocated"]) == "Y";
                    detail.IsReceipt = Util.GetValueOfString(hdr["Is_Receipt"]) == "Y";
                    detail.TenderType = Util.GetValueOfString(hdr["Tender_Type"]);
                    detail.PaymentMethodName = Util.GetValueOfString(hdr["Payment_Method_Name"]);
                    detail.Reference = Util.GetValueOfString(hdr["Payment_Reference"]);
                }
            }
            finally
            {
                if (hdr != null)
                {
                    hdr.Close();
                    hdr.Dispose();
                }
            }

            /* Receipt not visible to this role / not found — return the empty
               detail; lines below would either be empty or also blocked. */
            if (detail.PaymentId <= 0) { return detail; }

            /* Allocation lines — main physical table is C_AllocationLine. Each
               line can reference exactly one of three targets:
                 1. C_Invoice_ID       — receipt allocated against an invoice
                 2. Ref_Payment_ID     — receipt allocated against another payment
                 3. GL_JournalLine_ID  — receipt allocated against a GL journal line
               All three lookups are LEFT OUTER JOINed; the Line_Type CASE and the
               COALESCEd Ref_Document_No / Ref_Document_Date / Ref_Document_Amount
               columns collapse them into a single uniform shape for the client.
               MRole is applied only on AllocLine — the lookups inherit row-level
               security via the access predicate on the payment row that produced
               them; ORDER BY is appended AFTER MRole so the FROM-clause parser
               isn't confused by a trailing clause. */
            string lineSql = @"
                SELECT AllocLine.C_AllocationLine_ID AS Alloc_Line_ID,
                       AllocHdr.C_AllocationHdr_ID AS Alloc_Hdr_ID,
                       AllocHdr.DocumentNo AS Alloc_Document_No,
                       AllocHdr.DateAcct AS Alloc_Date,
                       AllocHdr.DocStatus AS Alloc_Doc_Status,
                       CASE
                           WHEN COALESCE(AllocLine.C_Invoice_ID, 0) > 0      THEN 'Invoice'
                           WHEN COALESCE(AllocLine.Ref_Payment_ID, 0) > 0    THEN 'Payment'
                           WHEN COALESCE(AllocLine.GL_JournalLine_ID, 0) > 0 THEN 'Journal'
                           ELSE 'Other'
                       END AS Line_Type,
                       Invoice.C_Invoice_ID AS C_Invoice_ID,
                       Invoice.DocumentNo AS Invoice_Document_No,
                       Invoice.DateInvoiced AS Invoice_Date,
                       Invoice.GrandTotal AS Invoice_Grand_Total,
                       RefPayment.C_Payment_ID AS Ref_Payment_ID,
                       RefPayment.DocumentNo AS Ref_Payment_Document_No,
                       RefPayment.DateAcct AS Ref_Payment_Date,
                       RefPayment.PayAmt AS Ref_Payment_Amount,
                       GLJournalLine.GL_JournalLine_ID AS GL_JournalLine_ID,
                       GLJournalLine.Line AS GL_JournalLine_No,
                       GLJournalLine.GL_Journal_ID AS GL_Journal_ID,
                       GLJournal.DocumentNo AS GL_Journal_Document_No,
                       GLJournal.DateAcct AS GL_Journal_Date,
                       (COALESCE(GLJournalLine.AmtSourceDr, 0) - COALESCE(GLJournalLine.AmtSourceCr, 0)) AS GL_Journal_Amount,
                       COALESCE(Invoice.DocumentNo, RefPayment.DocumentNo, GLJournal.DocumentNo, N'') AS Ref_Document_No,
                       COALESCE(Invoice.DateInvoiced, RefPayment.DateAcct, GLJournal.DateAcct) AS Ref_Document_Date,
                       COALESCE(
                           Invoice.GrandTotal,
                           RefPayment.PayAmt,
                           ABS(COALESCE(GLJournalLine.AmtSourceDr, 0) - COALESCE(GLJournalLine.AmtSourceCr, 0)),
                           0
                       ) AS Ref_Document_Amount,
                       AllocLine.Amount AS Applied_Amount,
                       AllocLine.DiscountAmt AS Discount_Amount,
                       AllocLine.WriteOffAmt AS Write_Off_Amount,
                       AllocLine.OverUnderAmt AS Over_Under_Amount
                FROM C_AllocationLine AllocLine
                INNER JOIN C_AllocationHdr AllocHdr ON (AllocLine.C_AllocationHdr_ID=AllocHdr.C_AllocationHdr_ID)
                LEFT OUTER JOIN C_Invoice Invoice ON (AllocLine.C_Invoice_ID=Invoice.C_Invoice_ID)
                LEFT OUTER JOIN C_Payment RefPayment ON (AllocLine.Ref_Payment_ID=RefPayment.C_Payment_ID)
                LEFT OUTER JOIN GL_JournalLine GLJournalLine ON (AllocLine.GL_JournalLine_ID=GLJournalLine.GL_JournalLine_ID)
                LEFT OUTER JOIN GL_Journal GLJournal ON (GLJournalLine.GL_Journal_ID=GLJournal.GL_Journal_ID)
                WHERE AllocLine.C_Payment_ID = @C_Payment_ID
                  AND AllocLine.IsActive = 'Y'
                  AND AllocHdr.IsActive = 'Y'
                  AND AllocHdr.DocStatus IN ('CO', 'CL')
                  AND AllocLine.AD_Client_ID = " + clientId;

            lineSql = MRole.GetDefault(ctx).AddAccessSQL(
                lineSql,
                "AllocLine",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            string orderedLineSql = lineSql + @"
                ORDER BY AllocHdr.DateAcct DESC, AllocLine.C_AllocationLine_ID DESC";

            SqlParameter[] lineParams = new SqlParameter[]
            {
                new SqlParameter("@C_Payment_ID", C_Payment_ID)
            };

            IDataReader ld = null;
            try
            {
                ld = DB.ExecuteReader(orderedLineSql, lineParams);
                while (ld != null && ld.Read())
                {
                    DateTime? allocDate = Util.GetValueOfDateTime(ld["Alloc_Date"]);
                    DateTime? invDate = Util.GetValueOfDateTime(ld["Invoice_Date"]);
                    DateTime? refPayDate = Util.GetValueOfDateTime(ld["Ref_Payment_Date"]);
                    DateTime? gljDate = Util.GetValueOfDateTime(ld["GL_Journal_Date"]);
                    DateTime? refDate = Util.GetValueOfDateTime(ld["Ref_Document_Date"]);

                    decimal applied = Math.Round(
                        Util.GetValueOfDecimal(ld["Applied_Amount"]),
                        stdPrecision,
                        MidpointRounding.AwayFromZero
                    );
                    decimal discount = Math.Round(
                        Util.GetValueOfDecimal(ld["Discount_Amount"]),
                        stdPrecision,
                        MidpointRounding.AwayFromZero
                    );
                    decimal writeOff = Math.Round(
                        Util.GetValueOfDecimal(ld["Write_Off_Amount"]),
                        stdPrecision,
                        MidpointRounding.AwayFromZero
                    );
                    decimal overUnder = Math.Round(
                        Util.GetValueOfDecimal(ld["Over_Under_Amount"]),
                        stdPrecision,
                        MidpointRounding.AwayFromZero
                    );
                    decimal grand = Math.Round(
                        Util.GetValueOfDecimal(ld["Invoice_Grand_Total"]),
                        stdPrecision,
                        MidpointRounding.AwayFromZero
                    );
                    decimal refPayAmount = Math.Round(
                        Util.GetValueOfDecimal(ld["Ref_Payment_Amount"]),
                        stdPrecision,
                        MidpointRounding.AwayFromZero
                    );
                    decimal gljAmount = Math.Round(
                        Util.GetValueOfDecimal(ld["GL_Journal_Amount"]),
                        stdPrecision,
                        MidpointRounding.AwayFromZero
                    );
                    decimal refDocAmount = Math.Round(
                        Util.GetValueOfDecimal(ld["Ref_Document_Amount"]),
                        stdPrecision,
                        MidpointRounding.AwayFromZero
                    );

                    detail.Lines.Add(new ReceiptAllocationLine
                    {
                        AllocationLineId = Util.GetValueOfInt(ld["Alloc_Line_ID"]),
                        AllocationHdrId = Util.GetValueOfInt(ld["Alloc_Hdr_ID"]),
                        AllocationDocumentNo = Util.GetValueOfString(ld["Alloc_Document_No"]),
                        AllocationDate = allocDate.HasValue
                            ? allocDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                            : "",
                        AllocationDocStatus = Util.GetValueOfString(ld["Alloc_Doc_Status"]),
                        LineType = Util.GetValueOfString(ld["Line_Type"]),
                        CInvoiceId = Util.GetValueOfInt(ld["C_Invoice_ID"]),
                        InvoiceDocumentNo = Util.GetValueOfString(ld["Invoice_Document_No"]),
                        InvoiceDate = invDate.HasValue
                            ? invDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                            : "",
                        InvoiceGrandTotal = grand,
                        RefPaymentId = Util.GetValueOfInt(ld["Ref_Payment_ID"]),
                        RefPaymentDocumentNo = Util.GetValueOfString(ld["Ref_Payment_Document_No"]),
                        RefPaymentDate = refPayDate.HasValue
                            ? refPayDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                            : "",
                        RefPaymentAmount = refPayAmount,
                        GLJournalLineId = Util.GetValueOfInt(ld["GL_JournalLine_ID"]),
                        GLJournalLineNo = Util.GetValueOfInt(ld["GL_JournalLine_No"]),
                        GLJournalId = Util.GetValueOfInt(ld["GL_Journal_ID"]),
                        GLJournalDocumentNo = Util.GetValueOfString(ld["GL_Journal_Document_No"]),
                        GLJournalDate = gljDate.HasValue
                            ? gljDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                            : "",
                        GLJournalAmount = gljAmount,
                        RefDocumentNo = Util.GetValueOfString(ld["Ref_Document_No"]),
                        RefDocumentDate = refDate.HasValue
                            ? refDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                            : "",
                        RefDocumentAmount = refDocAmount,
                        AppliedAmount = applied,
                        DiscountAmount = discount,
                        WriteOffAmount = writeOff,
                        OverUnderAmount = overUnder
                    });
                }
            }
            finally
            {
                if (ld != null)
                {
                    ld.Close();
                    ld.Dispose();
                }
            }

            return detail;
        }

        /// <summary>
        /// Returns the AD_Process_ID + AD_Table_ID needed to drive
        /// VIS.APrint against a C_Payment record from the receipt-detail
        /// modal's Print button.
        ///
        /// AD_Table_ID comes from the X_C_Payment generated constant.
        /// AD_Process_ID is the process linked on the FIRST tab (tab index 0) of
        /// the VAS_ARReceipt window, and only when that tab is the C_Payment tab:
        ///     'VAS_ARReceipt' → AD_Window_ID → first AD_Tab by SeqNo → AD_Process_ID
        /// AD_Tab carries the print/report process for the tab; the standard
        /// window toolbar's Print button reads the same value when the user is on
        /// the Payment window, so deriving it the same way keeps both entry points
        /// aligned. A process linked on any OTHER tab does not count.
        ///
        /// AD_Process_ID comes back 0 when that tab carries none — the caller then
        /// hides its Print button rather than offering an action that cannot run.
        /// C_Payment_ID 0 is therefore allowed: the widget probes for print
        /// availability once, before any receipt is open.
        /// </summary>
        public PaymentPrintInfo GetPaymentPrintInfo(Ctx ctx, int C_Payment_ID)
        {
            PaymentPrintInfo info = new PaymentPrintInfo();
            if (ctx == null) { return info; }

            int adTableId = X_C_Payment.Table_ID;

            PoReceiptTabPanelModel obj = new PoReceiptTabPanelModel();
            int AD_Window_ID = obj.GetWindowId("VAS_ARReceipt", "Payment");
            if (AD_Window_ID <= 0) { return info; }

            SqlParameter[] pParams = new SqlParameter[]
            {
                 new SqlParameter("@AD_Table_ID", adTableId),
                new SqlParameter("@AD_Window_ID", AD_Window_ID)
            };

            DataSet ds = DB.ExecuteDataset(
                @"SELECT t.AD_Table_ID, t.AD_Process_ID
                  FROM AD_Tab t
                  INNER JOIN AD_Window w ON (t.AD_Window_ID = w.AD_Window_ID)
                  WHERE t.AD_Table_ID = @AD_Table_ID
                    AND t.AD_Window_ID = @AD_Window_ID
                    AND NVL(t.AD_Process_ID, 0) > 0
                    AND t.IsActive = 'Y'
                    AND w.IsActive = 'Y'
                  ORDER BY t.SeqNo, t.AD_Tab_ID
                  FETCH FIRST 1 ROW ONLY",
                pParams, null);
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
            {
                return info;
            }

            DataRow tab = ds.Tables[0].Rows[0];
            int adProcessId = Util.GetValueOfInt(tab["AD_Process_ID"]);
            if (adProcessId <= 0)
            {
                return info;
            }

            info.AD_Process_ID = adProcessId;
            info.AD_Table_ID = adTableId;
            info.Record_ID = C_Payment_ID;
            return info;
        }

        public class PaymentPrintInfo
        {
            public int AD_Process_ID { get; set; }
            public int AD_Table_ID { get; set; }
            public int Record_ID { get; set; }
        }

        public class RecentReceiptsPage
        {
            public List<RecentReceiptRow> Rows { get; set; }
            public int PageNo { get; set; }
            public int PageSize { get; set; }
            public int TotalRecords { get; set; }
            public int TotalPages { get; set; }
        }

        public class RecentReceiptRow
        {
            public int PaymentId { get; set; }
            public string DocumentNo { get; set; }
            public string DateAcct { get; set; }
            public string CustomerName { get; set; }
            public string BankName { get; set; }
            public string AccountNo { get; set; }
            public decimal PayAmount { get; set; }
            public string CurrencyIso { get; set; }
            public string CurSymbol { get; set; }
            public int StdPrecision { get; set; }
            public string DocStatus { get; set; }
            public bool IsReconciled { get; set; }
            public bool IsAllocated { get; set; }
            public bool IsReceipt { get; set; }
            public string TenderType { get; set; }
            public string Reference { get; set; }
            public string InvoiceDocumentNo { get; set; }
            public string PaymentMethodName { get; set; }   
        }

        public class ReceiptAllocationDetail
        {
            public int PaymentId { get; set; }
            public string DocumentNo { get; set; }
            public string DateAcct { get; set; }
            public string CustomerName { get; set; }
            public string BankName { get; set; }
            public string AccountNo { get; set; }
            public decimal PayAmount { get; set; }
            public string CurrencyIso { get; set; }
            public string CurSymbol { get; set; }
            public int StdPrecision { get; set; }
            public string DocStatus { get; set; }
            public bool IsReconciled { get; set; }
            public bool IsAllocated { get; set; }
            public bool IsReceipt { get; set; }
            public string TenderType { get; set; }
            /// <summary>VA009_PaymentMethod.Name — what the modal shows as the payment method.</summary>
            public string PaymentMethodName { get; set; }
            public string Reference { get; set; }
            public List<ReceiptAllocationLine> Lines { get; set; }
        }

        public class ReceiptAllocationLine
        {
            public int AllocationLineId { get; set; }
            /* Allocation header the line belongs to — the record the
               Allocation No. column zooms to (window VAS_ViewAllocation). */
            public int AllocationHdrId { get; set; }
            public string AllocationDocumentNo { get; set; }
            public string AllocationDate { get; set; }
            public string AllocationDocStatus { get; set; }

            /* "Invoice" / "Payment" / "Journal" / "Other" — drives the type
               chip and which Ref_* lookup the client should render. */
            public string LineType { get; set; }

            /* Case 1: receipt allocated against an invoice. */
            public int CInvoiceId { get; set; }
            public string InvoiceDocumentNo { get; set; }
            public string InvoiceDate { get; set; }
            public decimal InvoiceGrandTotal { get; set; }

            /* Case 2: receipt allocated against another payment. */
            public int RefPaymentId { get; set; }
            public string RefPaymentDocumentNo { get; set; }
            public string RefPaymentDate { get; set; }
            public decimal RefPaymentAmount { get; set; }

            /* Case 3: receipt allocated against a GL journal line. */
            public int GLJournalLineId { get; set; }
            public int GLJournalLineNo { get; set; }
            public int GLJournalId { get; set; }
            public string GLJournalDocumentNo { get; set; }
            public string GLJournalDate { get; set; }
            public decimal GLJournalAmount { get; set; }

            /* Generic reference triple — COALESCEd over the three cases so a
               client can render any line with the same column shape. */
            public string RefDocumentNo { get; set; }
            public string RefDocumentDate { get; set; }
            public decimal RefDocumentAmount { get; set; }

            public decimal AppliedAmount { get; set; }
            public decimal DiscountAmount { get; set; }
            public decimal WriteOffAmount { get; set; }
            public decimal OverUnderAmount { get; set; }
        }
    }
}
