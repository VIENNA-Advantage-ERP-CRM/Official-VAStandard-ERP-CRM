/// <summary>
/// Module Name : VASLogic
/// Purpose     : Payment Overview tab panel data (read side). Returns a
///               contextual, read-only summary of the selected C_Payment record
///               for the VAS.VAS_191_APPaymentDetailPanel tab panel:
///
///                 - the payment header (document, business partner, direction,
///                   currency, dates, document / posting / execution status),
///                 - the allocation position (IsAllocated + the amount still
///                   available for allocation, taken from the framework function
///                   ALLOCPAYMENTAVAILABLE — this model never re-implements that
///                   financial calculation),
///                 - the "Payment Allocate" rows (C_PaymentAllocate — the
///                   allocation entries PREPARED on the payment),
///                 - the "Allocation Detail" rows (C_AllocationHdr +
///                   C_AllocationLine — the COMPLETED allocation history),
///                 - the withholding legs the payment carries (C_Withholding_ID /
///                   WithholdingAmt and BackupWithholding_ID /
///                   BackupWithholdingAmount, with the type name and
///                   C_Withholding.PayPercentage), and
///                 - the AD_Form_ID of the standard Payment Allocation form the
///                   panel's Apply Advance action opens.
///
///               The two allocation datasets are deliberately kept apart:
///               C_PaymentAllocate is preparation data and is NOT authoritative
///               allocation history; only C_AllocationHdr rows in DocStatus
///               'CO' / 'CL' are presented as final allocation history.
///
///               Every section is its own small query. One combined statement
///               would multiply rows across the payment's several one-to-many
///               relationships and make every figure on screen unreliable.
///
///               MRole access filtering is applied to the MAIN physical table of
///               each query — the alias the user actually fetches from — and
///               never to a derived table, a CTE alias or a lookup join. Where a
///               query needs an ORDER BY, it is appended AFTER AddAccessSQL,
///               because the rewriter appends its predicate to the end of the
///               WHERE clause it is given.
///
///               Portability: the SQL uses COALESCE / CASE / ANSI joins only —
///               no NVL, DECODE, ROWNUM, LIMIT or FETCH FIRST — so one statement
///               serves both Oracle and PostgreSQL. Free-text literals carry the
///               national-character prefix; stored code comparisons
///               (IsActive = 'Y', DocStatus = 'CO') deliberately do not.
///
///               Optional / module columns (C_PaymentAllocate.GL_JournalLine_ID,
///               C_AllocationLine.Ref_C_Invoice_ID, C_Payment.VA009_*) are
///               resolved through the dictionary first, so the panel works
///               whether or not those modules are installed.
/// Chronological development:
///   VAI145   2026-08-17  Created.
///   VAI145   2026-08-18  Business-partner LOCATION added to the header; withholding
///                        legs (C_Withholding / backup withholding) added.
/// </summary>

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Text;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;

namespace VASLogic.Models
{
    public partial class VAS_191_APPaymentDetailPanelModel
    {
        private static readonly VLogger _log =
            VLogger.GetVLogger(typeof(VAS_191_APPaymentDetailPanelModel).FullName);

        /// <summary>Class name of the standard Payment Allocation form (AD_Form.ClassName).
        /// Matching the exact class name is what keeps an unrelated form whose name merely
        /// contains "allocation" from being opened.</summary>
        private const string ALLOCATION_FORM_CLASSNAME = "VAdvantage.Apps.AForms.VAllocation";

        /// <summary>Allocation-history reference types handed to the client. The client
        /// picks the icon, the label and the zoom target from these.</summary>
        /// <summary>Posted-entry rows per page (server-side paged).</summary>
        private const int JOURNAL_PAGE_SIZE = 10;

        private const string REF_INVOICE = "Invoice";
        private const string REF_PAYMENT = "Payment";
        private const string REF_JOURNAL = "Journal";
        private const string REF_OTHER = "Other";

        // ----------------------------------------------------------------- //
        //  Entry point                                                       //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// Returns the full overview payload for the selected payment. The payment
        /// row itself is read under MRole, so an id the browser sent for a record
        /// the role cannot see comes back as an empty payload — the client-supplied
        /// id is never trusted on its own.
        /// </summary>
        /// <param name="ctx">User context (client / org / role).</param>
        /// <param name="C_Payment_ID">Selected payment id.</param>
        /// <returns>Populated <see cref="PaymentOverviewData"/>; an empty instance
        /// when the id is invalid or no accessible row exists.</returns>
        public PaymentOverviewData GetPaymentOverview(Ctx ctx, int C_Payment_ID)
        {
            PaymentOverviewData result = new PaymentOverviewData();
            result.PaymentAllocate = new List<PaymentAllocateRow>();
            result.AllocationDetail = new List<AllocationDetailRow>();
            result.Withholding = new List<WithholdingRow>();

            if (ctx == null || C_Payment_ID <= 0)
            {
                return result;
            }

            if (!LoadPayment(ctx, C_Payment_ID, result))
            {
                return result;   // not accessible to this role, or does not exist
            }

            /* Allocation availability comes from the framework function; the panel
               never derives it from PayAmt minus allocations on its own. */
            decimal available = LoadAvailableForAllocation(ctx, C_Payment_ID);
            result.AvailableSigned = available;
            result.UnallocatedAmount = Math.Abs(available);

            result.PaymentAllocate = LoadPaymentAllocate(ctx, C_Payment_ID, result.StdPrecision);
            result.AllocationDetail = LoadAllocationDetail(ctx, C_Payment_ID);

            /* Totals of the two datasets, so the panel can label its sections
               without re-adding the rows itself. The allocation aggregate is summed
               over the DEDUPED rows rather than by a separate SUM query: a mirror
               line would otherwise be counted a second time in the footer while the
               grid above it shows the allocation once. */
            foreach (PaymentAllocateRow row in result.PaymentAllocate)
            {
                result.PaymentAllocateTotal += row.Amount;
            }
            result.AllocationTotal = result.AllocationDetail.Count;
            foreach (AllocationDetailRow row in result.AllocationDetail)
            {
                result.AllocationAmount += Math.Abs(row.Amount);
                result.AllocationDiscount += Math.Abs(row.DiscountAmt);
                result.AllocationWriteOff += Math.Abs(row.WriteOffAmt);
            }

            /* Posted accounting facts. First page only — the section pages on the
               server through GetPostedJournalPage. */
            result.PostedJournal = LoadPostedJournal(ctx, C_Payment_ID, 0, JOURNAL_PAGE_SIZE, true);

            /* Server is the authority on what may be done to this document; the
               panel only mirrors these flags on its buttons. */
            ApplyActionFlags(result);

            result.AllocationFormId = GetAllocationFormId();

            return result;
        }

        // ----------------------------------------------------------------- //
        //  1. Payment header                                                 //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// Reads the payment identity, direction, currency, dates, document /
        /// posting / execution status and the related documents. This is the query
        /// that decides whether the caller may see the record at all, so MRole is
        /// applied here, on C_Payment.
        /// </summary>
        /// <param name="ctx">User context.</param>
        /// <param name="C_Payment_ID">Selected payment id.</param>
        /// <param name="result">Payload filled in place.</param>
        /// <returns>True when an accessible row was found.</returns>
        private bool LoadPayment(Ctx ctx, int C_Payment_ID, PaymentOverviewData result)
        {
            StringBuilder sql = new StringBuilder();
            sql.Append(@"SELECT pay.C_Payment_ID,
                                pay.AD_Client_ID,
                                pay.AD_Org_ID,
                                org.Name AS OrgName,
                                pay.DocumentNo,
                                pay.DateTrx,
                                pay.DateAcct,
                                pay.PayAmt,
                                pay.PaymentAmount,
                                pay.DiscountAmt,
                                pay.WriteOffAmt,
                                pay.OverUnderAmt,
                                pay.IsReceipt,
                                pay.IsPrepayment,
                                pay.IsAllocated,
                                pay.IsReconciled,
                                COALESCE(pay.IsApproved, 'N') AS IsApproved,
                                pay.DocStatus,
                                pay.DocAction,
                                pay.Processed,
                                pay.Posted,
                                pay.TenderType,
                                pay.CheckNo,
                                pay.CheckDate,
                                COALESCE(pay.Description, N'') AS Description,
                                pay.C_BPartner_ID,
                                bp.Name AS BPName,
                                bp.Value AS BPValue,
                                pay.C_BPartner_Location_ID,
                                bpl.Name AS BPLocationName,
                                pay.C_Currency_ID,
                                pay.C_ConversionType_ID,
                                cur.ISO_Code AS CurISO,
                                CASE WHEN cur.CurSymbol IS NOT NULL THEN cur.CurSymbol ELSE cur.ISO_Code END AS CurSymbol,
                                cur.StdPrecision,
                                pay.C_Invoice_ID,
                                inv.DocumentNo AS InvoiceDocumentNo,
                                pay.C_InvoicePaySchedule_ID,
                                ips.DueDate AS ScheduleDueDate,
                                pay.C_Charge_ID,
                                chg.Name AS ChargeName,
                                pay.C_Withholding_ID,
                                wh.Name AS WithholdingName,
                                wh.PayPercentage AS WithholdingRate,
                                pay.WithholdingAmt,
                                pay.BackupWithholding_ID,
                                bwh.Name AS BackupWithholdingName,
                                bwh.PayPercentage AS BackupWithholdingRate,
                                pay.BackupWithholdingAmount,
                                pay.C_BankAccount_ID,
                                ba.AccountNo AS BankAccountNo,
                                bnk.Name AS BankName,
                                dt.Name AS DocTypeName,
                                dt.DocBaseType,
                                usr.Name AS CreatedByName,
                                pay.Created,
                                pay.Updated,
                                inv.isexpenseinvoice, bpUsr.EMail ");
            sql.Append(@", pay.VA009_PaymentMethod_ID, pm.VA009_Name AS PaymentMethodName");
            sql.Append(@",pay.VA009_ExecutionStatus");
            sql.Append(@"
                         FROM C_Payment pay
                         INNER JOIN C_Currency cur ON (cur.C_Currency_ID=pay.C_Currency_ID)
                         INNER JOIN AD_Org org ON (org.AD_Org_ID=pay.AD_Org_ID)
                         INNER JOIN C_BankAccount ba ON (ba.C_BankAccount_ID=pay.C_BankAccount_ID)
                         INNER JOIN C_Bank bnk ON (bnk.C_Bank_ID=ba.C_Bank_ID)
                         INNER JOIN C_DocType dt ON (dt.C_DocType_ID=pay.C_DocType_ID)
                         LEFT OUTER JOIN C_BPartner bp ON (bp.C_BPartner_ID=pay.C_BPartner_ID)
                         LEFT OUTER JOIN C_BPartner_Location bpl ON (bpl.C_BPartner_Location_ID=pay.C_BPartner_Location_ID)
                         LEFT OUTER JOIN AD_User bpUsr ON (bpUsr.C_BPartner_ID=bp.C_BPartner_ID AND bpUsr.EMail IS NOT NULL)
                         LEFT OUTER JOIN C_Withholding wh ON (wh.C_Withholding_ID=pay.C_Withholding_ID)
                         LEFT OUTER JOIN C_Withholding bwh ON (bwh.C_Withholding_ID=pay.BackupWithholding_ID)
                         LEFT OUTER JOIN C_Invoice inv ON (inv.C_Invoice_ID=pay.C_Invoice_ID)
                         LEFT OUTER JOIN C_InvoicePaySchedule ips ON (ips.C_InvoicePaySchedule_ID=pay.C_InvoicePaySchedule_ID)
                         LEFT OUTER JOIN C_Charge chg ON (chg.C_Charge_ID=pay.C_Charge_ID)
                         LEFT OUTER JOIN AD_User usr ON (usr.AD_User_ID=pay.CreatedBy)");
            sql.Append(@" INNER JOIN VA009_PaymentMethod pm ON (pm.VA009_PaymentMethod_ID=pay.VA009_PaymentMethod_ID)");
            sql.Append(@" WHERE pay.C_Payment_ID=@C_Payment_ID
                          AND pay.IsActive='Y'
                          AND pay.AD_Client_ID=@AD_Client_ID");

            string accessSql = MRole.GetDefault(ctx).AddAccessSQL(
                sql.ToString(), "pay", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            /* Two binds, each occurring once, in the order they appear. */
            SqlParameter[] param = new SqlParameter[]
            {
                new SqlParameter("@C_Payment_ID", C_Payment_ID),
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
            };

            DataSet ds = null;
            try
            {
                ds = DB.ExecuteDataset(accessSql, param, null);
            }
            catch (Exception ex)
            {
                _log.Severe("VAS_191 LoadPayment(" + C_Payment_ID + "): " + ex.Message);
                return false;
            }

            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
            {
                return false;
            }

            DataRow r = ds.Tables[0].Rows[0];

            result.C_Payment_ID = Util.GetValueOfInt(r["C_Payment_ID"]);
            result.AD_Org_ID = Util.GetValueOfInt(r["AD_Org_ID"]);
            result.OrgName = Util.GetValueOfString(r["OrgName"]);
            result.DocumentNo = Util.GetValueOfString(r["DocumentNo"]);
            result.DateTrx = FormatDate(Util.GetValueOfDateTime(r["DateTrx"]));
            result.DateAcct = FormatDate(Util.GetValueOfDateTime(r["DateAcct"]));

            result.StdPrecision = Util.GetValueOfInt(r["StdPrecision"]);
            result.PayAmt = Round(Util.GetValueOfDecimal(r["PayAmt"]), result.StdPrecision);
            result.PaymentAmount = Round(Util.GetValueOfDecimal(r["PaymentAmount"]), result.StdPrecision);
            result.DiscountAmt = Round(Util.GetValueOfDecimal(r["DiscountAmt"]), result.StdPrecision);
            result.WriteOffAmt = Round(Util.GetValueOfDecimal(r["WriteOffAmt"]), result.StdPrecision);
            result.OverUnderAmt = Round(Util.GetValueOfDecimal(r["OverUnderAmt"]), result.StdPrecision);

            result.IsReceipt = Util.GetValueOfString(r["IsReceipt"]) == "Y";
            result.IsPrepayment = Util.GetValueOfString(r["IsPrepayment"]) == "Y";
            result.IsAllocated = Util.GetValueOfString(r["IsAllocated"]) == "Y";
            result.IsReconciled = Util.GetValueOfString(r["IsReconciled"]) == "Y";
            result.IsApproved = Util.GetValueOfString(r["IsApproved"]) == "Y";
            result.Processed = Util.GetValueOfString(r["Processed"]) == "Y";

            result.DocStatus = Util.GetValueOfString(r["DocStatus"]);
            result.DocAction = Util.GetValueOfString(r["DocAction"]);
            result.Posted = Util.GetValueOfString(r["Posted"]);
            /* List-reference columns are stored as short codes; the panel must never
               render the raw code, so the dictionary resolves the display name. */
            result.DocStatusName = GetListReferenceName(ctx, "C_Payment", "DocStatus", result.DocStatus);
            result.PostedName = GetListReferenceName(ctx, "C_Payment", "Posted", result.Posted);

            result.TenderType = Util.GetValueOfString(r["TenderType"]);
            result.TenderTypeName = GetListReferenceName(ctx, "C_Payment", "TenderType", result.TenderType);
            result.CheckNo = Util.GetValueOfString(r["CheckNo"]);
            result.CheckDate = FormatDate(Util.GetValueOfDateTime(r["CheckDate"]));
            result.Description = Util.GetValueOfString(r["Description"]);

            result.C_BPartner_ID = Util.GetValueOfInt(r["C_BPartner_ID"]);
            result.BPName = Util.GetValueOfString(r["BPName"]);
            result.BPValue = Util.GetValueOfString(r["BPValue"]);
            result.C_BPartner_Location_ID = Util.GetValueOfInt(r["C_BPartner_Location_ID"]);
            result.BPLocationName = Util.GetValueOfString(r["BPLocationName"]);
            result.BPEMail = Util.GetValueOfString(r["EMail"]);

            result.C_Currency_ID = Util.GetValueOfInt(r["C_Currency_ID"]);
            result.C_ConversionType_ID = Util.GetValueOfInt(r["C_ConversionType_ID"]);
            result.CurISO = Util.GetValueOfString(r["CurISO"]);
            result.CurSymbol = Util.GetValueOfString(r["CurSymbol"]);

            result.C_Invoice_ID = Util.GetValueOfInt(r["C_Invoice_ID"]);
            result.InvoiceDocumentNo = Util.GetValueOfString(r["InvoiceDocumentNo"]);
            result.C_InvoicePaySchedule_ID = Util.GetValueOfInt(r["C_InvoicePaySchedule_ID"]);
            result.ScheduleDueDate = FormatDate(Util.GetValueOfDateTime(r["ScheduleDueDate"]));
            result.C_Charge_ID = Util.GetValueOfInt(r["C_Charge_ID"]);
            result.ChargeName = Util.GetValueOfString(r["ChargeName"]);

            result.Withholding = BuildWithholdingRows(r, result.StdPrecision);
            foreach (WithholdingRow wr in result.Withholding)
            {
                result.WithholdingTotal += wr.Amount;
            }

            result.C_BankAccount_ID = Util.GetValueOfInt(r["C_BankAccount_ID"]);
            result.BankAccountNo = MaskedTail(Util.GetValueOfString(r["BankAccountNo"]), 4);
            result.BankName = Util.GetValueOfString(r["BankName"]);
            result.DocTypeName = Util.GetValueOfString(r["DocTypeName"]);
            result.DocBaseType = Util.GetValueOfString(r["DocBaseType"]);

            result.CreatedByName = Util.GetValueOfString(r["CreatedByName"]);
            result.Created = FormatDate(Util.GetValueOfDateTime(r["Created"]));
            result.Updated = FormatDate(Util.GetValueOfDateTime(r["Updated"]));

            result.VA009_PaymentMethod_ID = Util.GetValueOfInt(r["VA009_PaymentMethod_ID"]);
            result.PaymentMethodName = Util.GetValueOfString(r["PaymentMethodName"]);
            result.ExecutionStatus = Util.GetValueOfString(r["VA009_ExecutionStatus"]);
            result.ExecutionStatusName = GetListReferenceName(ctx, "C_Payment", "VA009_ExecutionStatus", result.ExecutionStatus);

            return result.C_Payment_ID > 0;
        }

        /// <summary>
        /// The payment's withholding position, built from the header row that
        /// LoadPayment already fetched — C_Payment carries both legs itself
        /// (C_Withholding_ID / WithholdingAmt and BackupWithholding_ID /
        /// BackupWithholdingAmount), so this needs no second query.
        ///
        /// A leg is reported only when it names a withholding type; a type with no
        /// amount is still reported, because "TDS applies, nothing withheld yet" is
        /// a fact the panel should show rather than hide.
        ///
        /// The RATE is C_Withholding.PayPercentage — the same column the allocation
        /// code withholds with (amount * PayPercentage / 100) — so the panel can
        /// never disagree with what was actually taken. C_Payment stores no base
        /// amount, so the base is derived back from the amount and that rate and is
        /// left at zero when there is no percentage to derive it from; the panel
        /// then shows a dash rather than a made-up figure.
        /// </summary>
        /// <param name="r">The payment header row.</param>
        /// <param name="precision">Payment currency precision for rounding.</param>
        /// <returns>Zero, one or two rows — withholding first, backup withholding second.</returns>
        private static List<WithholdingRow> BuildWithholdingRows(DataRow r, int precision)
        {
            List<WithholdingRow> list = new List<WithholdingRow>();

            AddWithholdingRow(list, false,
                Util.GetValueOfInt(r["C_Withholding_ID"]),
                Util.GetValueOfString(r["WithholdingName"]),
                Util.GetValueOfDecimal(r["WithholdingRate"]),
                Util.GetValueOfDecimal(r["WithholdingAmt"]),
                precision);

            AddWithholdingRow(list, true,
                Util.GetValueOfInt(r["BackupWithholding_ID"]),
                Util.GetValueOfString(r["BackupWithholdingName"]),
                Util.GetValueOfDecimal(r["BackupWithholdingRate"]),
                Util.GetValueOfDecimal(r["BackupWithholdingAmount"]),
                precision);

            return list;
        }

        /// <summary>Appends one withholding leg when the payment carries it.</summary>
        /// <param name="list">Target list.</param>
        /// <param name="isBackup">True for the backup-withholding leg.</param>
        /// <param name="withholdingId">C_Withholding_ID of the leg.</param>
        /// <param name="name">Withholding type name.</param>
        /// <param name="rate">C_Withholding.PayPercentage.</param>
        /// <param name="amount">Amount withheld, as stored on the payment.</param>
        /// <param name="precision">Payment currency precision for rounding.</param>
        private static void AddWithholdingRow(List<WithholdingRow> list, bool isBackup,
            int withholdingId, string name, decimal rate, decimal amount, int precision)
        {
            if (withholdingId <= 0)
            {
                return;
            }

            WithholdingRow row = new WithholdingRow();
            row.IsBackup = isBackup;
            row.C_Withholding_ID = withholdingId;
            row.Name = name;
            row.Rate = rate;
            row.Amount = Round(amount, precision);
            /* amount = base * rate / 100, so the base is the amount grossed back up.
               Without a rate there is nothing to divide by and the base stays 0. */
            row.BaseAmount = rate != 0
                ? Round((row.Amount * 100m) / rate, precision)
                : 0;

            list.Add(row);
        }

        // ----------------------------------------------------------------- //
        //  2. Allocation availability                                        //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// Amount of this payment still available for allocation, in the payment's
        /// own currency, straight from the framework function ALLOCPAYMENTAVAILABLE
        /// (payment amount net of the allocations already made against it). The
        /// value is SIGNED — a purchase payment reports it negative — so callers
        /// compare it against zero with <c>!= 0</c> and display its magnitude.
        ///
        /// A missing / failing function costs the availability figure only: the
        /// panel keeps rendering and simply shows nothing available.
        /// </summary>
        /// <param name="ctx">User context.</param>
        /// <param name="C_Payment_ID">Selected payment id.</param>
        /// <returns>Signed available amount; 0 when it cannot be determined.</returns>
        private decimal LoadAvailableForAllocation(Ctx ctx, int C_Payment_ID)
        {
            string sql = @"SELECT COALESCE(ALLOCPAYMENTAVAILABLE(pay.C_Payment_ID), 0) AS AvailableAmount
                             FROM C_Payment pay
                            WHERE pay.C_Payment_ID=@C_Payment_ID
                              AND pay.IsActive='Y'
                              AND pay.AD_Client_ID=@AD_Client_ID";

            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql, "pay", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            SqlParameter[] param = new SqlParameter[]
            {
                new SqlParameter("@C_Payment_ID", C_Payment_ID),
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
            };

            try
            {
                return Util.GetValueOfDecimal(DB.ExecuteScalar(sql, param, null));
            }
            catch (Exception ex)
            {
                _log.Severe("VAS_191 LoadAvailableForAllocation(" + C_Payment_ID + "): " + ex.Message);
                return 0;
            }
        }

        // ----------------------------------------------------------------- //
        //  3. Payment Allocate (C_PaymentAllocate)                           //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// Allocation entries PREPARED on the payment (C_PaymentAllocate): the
        /// invoice / pay-schedule / GL-journal-line the payment is earmarked
        /// against, with its amount, discount, write-off and over-under.
        ///
        /// This is preparation data, not allocation history — the completed
        /// history is read separately from C_AllocationHdr / C_AllocationLine.
        /// The client never sees a raw id: each row carries the resolved document
        /// number and the schedule's due date.
        /// </summary>
        /// <param name="ctx">User context.</param>
        /// <param name="C_Payment_ID">Selected payment id.</param>
        /// <param name="precision">Payment currency precision for rounding.</param>
        /// <returns>Ordered rows; empty when the payment has none.</returns>
        private List<PaymentAllocateRow> LoadPaymentAllocate(Ctx ctx, int C_Payment_ID, int precision)
        {
            List<PaymentAllocateRow> list = new List<PaymentAllocateRow>();

            /* The GL-journal leg is an optional column on this table. */
            bool hasJournalLine = ColumnExists("C_PaymentAllocate", "GL_JournalLine_ID");

            StringBuilder sql = new StringBuilder();
            sql.Append(@"SELECT pa.C_PaymentAllocate_ID,
                                pa.C_Invoice_ID,
                                inv.DocumentNo AS InvoiceDocumentNo,
                                inv.DateInvoiced,
                                COALESCE(inv.IsExpenseInvoice, 'N') AS InvoiceIsExpense,
                                pa.C_InvoicePaySchedule_ID,
                                ips.DueDate AS ScheduleDueDate,
                                ips.DueAmt AS ScheduleDueAmt,
                                pa.Amount,
                                pa.DiscountAmt,
                                pa.WriteOffAmt,
                                pa.OverUnderAmt");
            if (hasJournalLine)
            {
                sql.Append(@",
                                pa.GL_JournalLine_ID,
                                jl.Line AS JournalLineNo,
                                jl.GL_Journal_ID,
                                jnl.DocumentNo AS JournalDocumentNo,
                                jnl.DateAcct AS JournalDateAcct");
            }
            sql.Append(@"
                         FROM C_PaymentAllocate pa
                         LEFT OUTER JOIN C_Invoice inv ON (inv.C_Invoice_ID=pa.C_Invoice_ID)
                         LEFT OUTER JOIN C_InvoicePaySchedule ips ON (ips.C_InvoicePaySchedule_ID=pa.C_InvoicePaySchedule_ID)");
            if (hasJournalLine)
            {
                sql.Append(@"
                         LEFT OUTER JOIN GL_JournalLine jl ON (jl.GL_JournalLine_ID=pa.GL_JournalLine_ID)
                         LEFT OUTER JOIN GL_Journal jnl ON (jnl.GL_Journal_ID=jl.GL_Journal_ID)");
            }
            sql.Append(@"
                        WHERE pa.C_Payment_ID=@C_Payment_ID
                          AND pa.IsActive='Y'
                          AND pa.AD_Client_ID=@AD_Client_ID");

            /* MRole on the MAIN physical table only; the lookup joins inherit the
               authorization of the row that produced them. ORDER BY is appended
               after the rewriter has done its work. */
            string accessSql = MRole.GetDefault(ctx).AddAccessSQL(
                sql.ToString(), "pa", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            accessSql += " ORDER BY pa.C_PaymentAllocate_ID";

            SqlParameter[] param = new SqlParameter[]
            {
                new SqlParameter("@C_Payment_ID", C_Payment_ID),
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
            };

            DataSet ds = null;
            try
            {
                ds = DB.ExecuteDataset(accessSql, param, null);
            }
            catch (Exception ex)
            {
                _log.Severe("VAS_191 LoadPaymentAllocate(" + C_Payment_ID + "): " + ex.Message);
                return list;
            }

            if (ds == null || ds.Tables.Count == 0)
            {
                return list;
            }

            foreach (DataRow r in ds.Tables[0].Rows)
            {
                PaymentAllocateRow row = new PaymentAllocateRow();
                row.C_PaymentAllocate_ID = Util.GetValueOfInt(r["C_PaymentAllocate_ID"]);
                row.C_Invoice_ID = Util.GetValueOfInt(r["C_Invoice_ID"]);
                row.C_InvoicePaySchedule_ID = Util.GetValueOfInt(r["C_InvoicePaySchedule_ID"]);
                row.ScheduleDate = FormatDate(Util.GetValueOfDateTime(r["ScheduleDueDate"]));
                row.ScheduleAmount = Round(Util.GetValueOfDecimal(r["ScheduleDueAmt"]), precision);
                row.Amount = Round(Util.GetValueOfDecimal(r["Amount"]), precision);
                row.DiscountAmt = Round(Util.GetValueOfDecimal(r["DiscountAmt"]), precision);
                row.WriteOffAmt = Round(Util.GetValueOfDecimal(r["WriteOffAmt"]), precision);
                row.OverUnderAmt = Round(Util.GetValueOfDecimal(r["OverUnderAmt"]), precision);

                if (hasJournalLine)
                {
                    row.GL_JournalLine_ID = Util.GetValueOfInt(r["GL_JournalLine_ID"]);
                    row.GL_Journal_ID = Util.GetValueOfInt(r["GL_Journal_ID"]);
                }

                /* The row names the document it allocates against — the invoice, or
                   the PARENT GL journal of the journal line. A journal line id is
                   never the user-facing value. */
                if (row.C_Invoice_ID > 0)
                {
                    row.ReferenceType = REF_INVOICE;
                    row.ReferenceID = row.C_Invoice_ID;
                    row.ReferenceDocumentNo = Util.GetValueOfString(r["InvoiceDocumentNo"]);
                    row.ReferenceDocumentDate = FormatDate(Util.GetValueOfDateTime(r["DateInvoiced"]));
                    /* An expense invoice lives in its own window, so the panel has
                       to know which kind of invoice this row points at. */
                    row.IsExpenseInvoice = Util.GetValueOfString(r["InvoiceIsExpense"]) == "Y";
                }
                else if (row.GL_Journal_ID > 0)
                {
                    row.ReferenceType = REF_JOURNAL;
                    row.ReferenceID = row.GL_Journal_ID;
                    row.ReferenceDocumentNo = Util.GetValueOfString(r["JournalDocumentNo"]);
                    row.ReferenceDocumentDate = FormatDate(Util.GetValueOfDateTime(r["JournalDateAcct"]));
                    row.JournalLineNo = Util.GetValueOfInt(r["JournalLineNo"]);
                }
                else
                {
                    row.ReferenceType = REF_OTHER;
                }

                list.Add(row);
            }

            return list;
        }

        // ----------------------------------------------------------------- //
        //  4. Allocation Detail (C_AllocationHdr + C_AllocationLine)         //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// The payment's COMPLETED allocation history. Only allocation headers in
        /// DocStatus 'CO' / 'CL' count as final allocation history — a drafted
        /// allocation header is not history.
        ///
        /// The payment can sit on either side of an allocation line:
        ///   - C_AllocationLine.C_Payment_ID     = this payment (the normal case), or
        ///   - C_AllocationLine.Ref_Payment_ID   = this payment (the mirror line a
        ///     payment-to-payment allocation writes).
        /// Both are read, and the COUNTERPART is resolved deterministically from
        /// the side the current payment is NOT on, so the reference column never
        /// shows the current payment back to the user. Mirror rows that describe
        /// an allocation already listed from the other side are dropped, so one
        /// logical allocation appears once.
        ///
        /// Amounts keep their stored sign (C_AllocationLine.Amount carries the
        /// transaction direction) and are reported in the ALLOCATION header's
        /// currency, which need not be the payment's currency.
        /// </summary>
        /// <param name="ctx">User context.</param>
        /// <param name="C_Payment_ID">Selected payment id.</param>
        /// <returns>Ordered rows, newest allocation first; empty when none exist.</returns>
        private List<AllocationDetailRow> LoadAllocationDetail(Ctx ctx, int C_Payment_ID)
        {
            List<AllocationDetailRow> list = new List<AllocationDetailRow>();

            /* Counterpart columns of the credit-note / reference legs are optional
               in older dictionaries, and the payment method belongs to the VA009
               module. Both are resolved through the dictionary first. */
            bool hasRefInvoice = ColumnExists("C_AllocationLine", "Ref_C_Invoice_ID");

            StringBuilder sql = new StringBuilder();
            sql.Append(@"SELECT al.C_AllocationLine_ID,
                                al.C_AllocationHdr_ID,
                                ah.DocumentNo AS AllocationNo,
                                ah.DateAcct AS AllocationDateAcct,
                                ah.DocStatus AS AllocationDocStatus,
                                ah.C_Currency_ID AS AllocationCurrencyID,
                                acur.ISO_Code AS AllocationCurISO,
                                CASE WHEN acur.CurSymbol IS NOT NULL THEN acur.CurSymbol ELSE acur.ISO_Code END AS AllocationCurSymbol,
                                acur.StdPrecision AS AllocationPrecision,
                                al.C_Payment_ID,
                                al.Ref_Payment_ID,
                                al.C_Invoice_ID,
                                al.Amount,
                                al.DiscountAmt,
                                al.WriteOffAmt,
                                al.OverUnderAmt,
                                al.C_InvoicePaySchedule_ID,
                                inv.DocumentNo AS InvoiceDocumentNo,
                                inv.DateInvoiced AS InvoiceDate,
                                invdt.Name AS InvoiceDocTypeName,
                                COALESCE(inv.IsExpenseInvoice, 'N') AS InvoiceIsExpense,
                                linepay.DocumentNo AS LinePaymentDocumentNo,
                                linepay.DateTrx AS LinePaymentDate,
                                COALESCE(linepay.IsReconciled, 'N') AS LinePaymentReconciled,
                                linepaydt.Name AS LinePaymentDocTypeName,
                                refpay.DocumentNo AS RefPaymentDocumentNo,
                                refpay.DateTrx AS RefPaymentDate,
                                COALESCE(refpay.IsReconciled, 'N') AS RefPaymentReconciled,
                                refpaydt.Name AS RefPaymentDocTypeName,
                                jl.GL_Journal_ID,
                                jnl.DocumentNo AS JournalDocumentNo,
                                jnl.DateAcct AS JournalDate,
                                jnldt.Name AS JournalDocTypeName,
                                (SELECT COUNT(1)
                                   FROM C_InvoicePaySchedule ips2
                                  WHERE ips2.C_Invoice_ID=ips.C_Invoice_ID
                                    AND ips2.IsActive='Y'
                                    AND (ips2.DueDate<ips.DueDate
                                         OR (ips2.DueDate=ips.DueDate
                                             AND ips2.C_InvoicePaySchedule_ID<=ips.C_InvoicePaySchedule_ID))) AS ScheduleNo");
            sql.Append(@",
                                linepaypm.VA009_Name AS LinePaymentMethodName,
                                refpaypm.VA009_Name AS RefPaymentMethodName");
            if (hasRefInvoice)
            {
                sql.Append(@",
                                al.Ref_C_Invoice_ID,
                                refinv.DocumentNo AS RefInvoiceDocumentNo,
                                refinv.DateInvoiced AS RefInvoiceDate,
                                refinvdt.Name AS RefInvoiceDocTypeName,
                                COALESCE(refinv.IsExpenseInvoice, 'N') AS RefInvoiceIsExpense");
            }
            sql.Append(@" FROM C_AllocationLine al
                         INNER JOIN C_AllocationHdr ah ON (ah.C_AllocationHdr_ID=al.C_AllocationHdr_ID)
                         INNER JOIN C_Currency acur ON (acur.C_Currency_ID=ah.C_Currency_ID)
                         LEFT OUTER JOIN C_Invoice inv ON (inv.C_Invoice_ID=al.C_Invoice_ID)
                         LEFT OUTER JOIN C_DocType invdt ON (invdt.C_DocType_ID=inv.C_DocTypeTarget_ID)
                         LEFT OUTER JOIN C_InvoicePaySchedule ips ON (ips.C_InvoicePaySchedule_ID=al.C_InvoicePaySchedule_ID)
                         LEFT OUTER JOIN C_Payment linepay ON (linepay.C_Payment_ID=al.C_Payment_ID)
                         LEFT OUTER JOIN C_DocType linepaydt ON (linepaydt.C_DocType_ID=linepay.C_DocType_ID)
                         LEFT OUTER JOIN C_Payment refpay ON (refpay.C_Payment_ID=al.Ref_Payment_ID)
                         LEFT OUTER JOIN C_DocType refpaydt ON (refpaydt.C_DocType_ID=refpay.C_DocType_ID)
                         LEFT OUTER JOIN GL_JournalLine jl ON (jl.GL_JournalLine_ID=al.GL_JournalLine_ID)
                         LEFT OUTER JOIN GL_Journal jnl ON (jnl.GL_Journal_ID=jl.GL_Journal_ID)
                         LEFT OUTER JOIN C_DocType jnldt ON (jnldt.C_DocType_ID=jnl.C_DocType_ID) ");
            sql.Append(@"LEFT OUTER JOIN VA009_PaymentMethod linepaypm ON (linepaypm.VA009_PaymentMethod_ID=linepay.VA009_PaymentMethod_ID)
                         LEFT OUTER JOIN VA009_PaymentMethod refpaypm ON (refpaypm.VA009_PaymentMethod_ID=refpay.VA009_PaymentMethod_ID) ");
            if (hasRefInvoice)
            {
                sql.Append(@"LEFT OUTER JOIN C_Invoice refinv ON (refinv.C_Invoice_ID=al.Ref_C_Invoice_ID)
                         LEFT OUTER JOIN C_DocType refinvdt ON (refinvdt.C_DocType_ID=refinv.C_DocTypeTarget_ID)");
            }
            sql.Append(@"
                        WHERE (al.C_Payment_ID=@C_Payment_ID OR al.Ref_Payment_ID=@Ref_C_Payment_ID)
                          AND al.IsActive='Y'
                          AND ah.IsActive='Y'
                          AND ah.DocStatus NOT IN ('RE','VO')
                          AND al.AD_Client_ID=@AD_Client_ID");

            string accessSql = MRole.GetDefault(ctx).AddAccessSQL(
                sql.ToString(), "al", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            /* Lines carrying the payment on the primary side are read first, so a
               mirror row describing the same allocation is the one dropped. */
            accessSql += @" ORDER BY CASE WHEN al.C_Payment_ID=@Order_C_Payment_ID THEN 0 ELSE 1 END,
                                     ah.DateAcct DESC,
                                     al.C_AllocationLine_ID DESC";

            /* Four binds, each occurring once, in the order they appear — the same
               value is passed under separate names because positional binding
               cannot reuse one. */
            SqlParameter[] param = new SqlParameter[]
            {
                new SqlParameter("@C_Payment_ID", C_Payment_ID),
                new SqlParameter("@Ref_C_Payment_ID", C_Payment_ID),
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@Order_C_Payment_ID", C_Payment_ID)
            };

            DataSet ds = null;
            try
            {
                ds = DB.ExecuteDataset(accessSql, param, null);
            }
            catch (Exception ex)
            {
                _log.Severe("VAS_191 LoadAllocationDetail(" + C_Payment_ID + "): " + ex.Message);
                return list;
            }

            if (ds == null || ds.Tables.Count == 0)
            {
                return list;
            }

            /* One logical allocation per (header, counterpart, amount). The mirror
               line of a payment-to-payment allocation carries the same three, so it
               is recognised here and skipped. */
            HashSet<string> seen = new HashSet<string>();

            foreach (DataRow r in ds.Tables[0].Rows)
            {
                int linePaymentId = Util.GetValueOfInt(r["C_Payment_ID"]);
                int refPaymentId = Util.GetValueOfInt(r["Ref_Payment_ID"]);
                bool isMirror = linePaymentId != C_Payment_ID && refPaymentId == C_Payment_ID;

                AllocationDetailRow row = new AllocationDetailRow();
                row.C_AllocationLine_ID = Util.GetValueOfInt(r["C_AllocationLine_ID"]);
                row.C_AllocationHdr_ID = Util.GetValueOfInt(r["C_AllocationHdr_ID"]);
                row.AllocationNo = Util.GetValueOfString(r["AllocationNo"]);
                row.AllocationDate = FormatDate(Util.GetValueOfDateTime(r["AllocationDateAcct"]));
                row.AllocationDocStatus = Util.GetValueOfString(r["AllocationDocStatus"]);
                row.C_Currency_ID = Util.GetValueOfInt(r["AllocationCurrencyID"]);
                row.CurrencyIso = Util.GetValueOfString(r["AllocationCurISO"]);
                row.CurrencySymbol = Util.GetValueOfString(r["AllocationCurSymbol"]);
                row.Precision = Util.GetValueOfInt(r["AllocationPrecision"]);

                /* Stored signs are preserved: C_AllocationLine.Amount carries the
                   transaction direction and is not forced positive. */
                row.Amount = Round(Util.GetValueOfDecimal(r["Amount"]), row.Precision);
                row.DiscountAmt = Round(Util.GetValueOfDecimal(r["DiscountAmt"]), row.Precision);
                row.WriteOffAmt = Round(Util.GetValueOfDecimal(r["WriteOffAmt"]), row.Precision);
                row.OverUnderAmt = Round(Util.GetValueOfDecimal(r["OverUnderAmt"]), row.Precision);

                int invoiceId = Util.GetValueOfInt(r["C_Invoice_ID"]);
                int journalId = Util.GetValueOfInt(r["GL_Journal_ID"]);
                int refInvoiceId = hasRefInvoice ? Util.GetValueOfInt(r["Ref_C_Invoice_ID"]) : 0;

                /* The instalment the line settled, as its 1-based ordinal within the
                   invoice ("Schedule 2"), never as the surrogate schedule id. */
                row.C_InvoicePaySchedule_ID = Util.GetValueOfInt(r["C_InvoicePaySchedule_ID"]);
                row.ScheduleNo = row.C_InvoicePaySchedule_ID > 0
                    ? Util.GetValueOfInt(r["ScheduleNo"])
                    : 0;

                if (isMirror)
                {
                    /* The current payment sits on the Ref side, so the counterpart is
                       the payment on the line's own side — never the current one. */
                    row.ReferenceType = REF_PAYMENT;
                    row.ReferenceID = linePaymentId;
                    row.ReferenceDocumentNo = Util.GetValueOfString(r["LinePaymentDocumentNo"]);
                    row.ReferenceDocumentDate = FormatDate(Util.GetValueOfDateTime(r["LinePaymentDate"]));
                    row.DocTypeName = Util.GetValueOfString(r["LinePaymentDocTypeName"]);
                    row.IsReconciled = Util.GetValueOfString(r["LinePaymentReconciled"]) == "Y";
                    row.HasReconciliation = true;
                    row.PaymentMethodName = Util.GetValueOfString(r["LinePaymentMethodName"]);
                }
                else if (invoiceId > 0)
                {
                    row.ReferenceType = REF_INVOICE;
                    row.ReferenceID = invoiceId;
                    row.ReferenceDocumentNo = Util.GetValueOfString(r["InvoiceDocumentNo"]);
                    row.ReferenceDocumentDate = FormatDate(Util.GetValueOfDateTime(r["InvoiceDate"]));
                    row.DocTypeName = Util.GetValueOfString(r["InvoiceDocTypeName"]);
                    /* An expense invoice lives in its own window, so the panel has
                       to know which kind of invoice this row points at. */
                    row.IsExpenseInvoice = Util.GetValueOfString(r["InvoiceIsExpense"]) == "Y";
                }
                else if (refPaymentId > 0)
                {
                    row.ReferenceType = REF_PAYMENT;
                    row.ReferenceID = refPaymentId;
                    row.ReferenceDocumentNo = Util.GetValueOfString(r["RefPaymentDocumentNo"]);
                    row.ReferenceDocumentDate = FormatDate(Util.GetValueOfDateTime(r["RefPaymentDate"]));
                    row.DocTypeName = Util.GetValueOfString(r["RefPaymentDocTypeName"]);
                    row.IsReconciled = Util.GetValueOfString(r["RefPaymentReconciled"]) == "Y";
                    row.HasReconciliation = true;
                    row.PaymentMethodName = Util.GetValueOfString(r["RefPaymentMethodName"]);
                }
                else if (journalId > 0)
                {
                    /* The parent GL journal is the user-facing document, not the line.
                       A journal entry is not a payment, so it carries neither a payment
                       method nor a reconciliation state. */
                    row.ReferenceType = REF_JOURNAL;
                    row.ReferenceID = journalId;
                    row.ReferenceDocumentNo = Util.GetValueOfString(r["JournalDocumentNo"]);
                    row.ReferenceDocumentDate = FormatDate(Util.GetValueOfDateTime(r["JournalDate"]));
                    row.DocTypeName = Util.GetValueOfString(r["JournalDocTypeName"]);
                }
                else if (refInvoiceId > 0)
                {
                    row.ReferenceType = REF_INVOICE;
                    row.ReferenceID = refInvoiceId;
                    row.ReferenceDocumentNo = Util.GetValueOfString(r["RefInvoiceDocumentNo"]);
                    row.ReferenceDocumentDate = FormatDate(Util.GetValueOfDateTime(r["RefInvoiceDate"]));
                    row.DocTypeName = Util.GetValueOfString(r["RefInvoiceDocTypeName"]);
                    row.IsExpenseInvoice = Util.GetValueOfString(r["RefInvoiceIsExpense"]) == "Y";
                }
                else
                {
                    /* Nothing on the counterpart side: the allocation document itself
                       is the only reference the row can name. */
                    row.ReferenceType = REF_OTHER;
                    row.ReferenceID = row.C_AllocationHdr_ID;
                    row.ReferenceDocumentNo = row.AllocationNo;
                    row.ReferenceDocumentDate = row.AllocationDate;
                }

                string key = row.C_AllocationHdr_ID + "|" + row.ReferenceType + "|"
                             + row.ReferenceID + "|"
                             + Math.Abs(row.Amount).ToString(CultureInfo.InvariantCulture);
                if (!seen.Add(key))
                {
                    continue;   // mirror of an allocation already listed
                }

                list.Add(row);
            }

            return list;
        }

        // ----------------------------------------------------------------- //
        //  5. Posted entry (Fact_Acct)                                       //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// Loads one page of the payment's posted accounting facts, in the primary
        /// accounting schema only, with the account and the dimensions the client
        /// shows as columns. Amounts are ACCOUNTED amounts, so they carry the
        /// accounting schema's currency and precision — not the payment's.
        ///
        /// An unposted payment simply has no facts, and the section is then not
        /// rendered at all.
        /// </summary>
        /// <param name="ctx">User context.</param>
        /// <param name="C_Payment_ID">Selected payment id.</param>
        /// <param name="page">Zero-based page index.</param>
        /// <param name="pageSize">Rows per page.</param>
        /// <param name="includeTotals">Also compute the count + Dr/Cr aggregate over
        /// ALL fact lines (they do not change between pages, so page fetches skip it).</param>
        /// <returns>Posted-journal info; an empty Rows list when nothing is posted.</returns>
        private PostedJournalInfo LoadPostedJournal(Ctx ctx, int C_Payment_ID, int page, int pageSize, bool includeTotals)
        {
            PostedJournalInfo pj = new PostedJournalInfo();
            pj.Rows = new List<JournalRow>();

            int tableId = MPayment.Table_ID;
            if (ctx == null || C_Payment_ID <= 0 || tableId <= 0)
            {
                return pj;
            }

            /* The table id is dictionary metadata this model resolved itself, so it
               is an integer literal here rather than a bind — that keeps the
               statement to a single bound value for positional binding. */
            string sql = @"SELECT fa.DateAcct,
                                  ev.Value AS AccountValue,
                                  ev.Name AS AccountName,
                                  fa.AmtAcctDr,
                                  fa.AmtAcctCr,
                                  fa.Description,
                                  org.Name AS OrgName,
                                  bp.Name AS BPName,
                                  orgtrx.Name AS OrgTrxName,
                                  per.Name AS PeriodName,
                                  cur.CurSymbol AS AcctCurSymbol,
                                  cur.StdPrecision AS AcctStdPrecision
                             FROM Fact_Acct fa
                             INNER JOIN C_ElementValue ev ON (ev.C_ElementValue_ID=fa.Account_ID)
                             INNER JOIN AD_Org org ON (org.AD_Org_ID=fa.AD_Org_ID)
                             INNER JOIN C_Period per ON (per.C_Period_ID=fa.C_Period_ID)
                             INNER JOIN AD_ClientInfo ci ON (ci.AD_Client_ID=fa.AD_Client_ID)
                             INNER JOIN C_AcctSchema acs ON (acs.C_AcctSchema_ID=ci.C_AcctSchema1_ID)
                             INNER JOIN C_Currency cur ON (cur.C_Currency_ID=acs.C_Currency_ID)
                             LEFT OUTER JOIN C_BPartner bp ON (bp.C_BPartner_ID=fa.C_BPartner_ID)
                             LEFT OUTER JOIN AD_Org orgtrx ON (orgtrx.AD_Org_ID=fa.AD_OrgTrx_ID)
                            WHERE fa.AD_Table_ID=" + tableId + @"
                              AND fa.Record_ID=@C_Payment_ID
                              AND fa.C_AcctSchema_ID=ci.C_AcctSchema1_ID";

            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql, "fa", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            /* ORDER BY and the paging suffix go AFTER the access filter, so the
               predicate lands in the WHERE clause and not behind a trailing clause. */
            sql += " ORDER BY fa.AmtAcctDr DESC, fa.Fact_Acct_ID";
            sql += PagingSuffix(page, pageSize);

            try
            {
                DataSet ds = DB.ExecuteDataset(sql, new SqlParameter[]
                {
                    new SqlParameter("@C_Payment_ID", C_Payment_ID)
                }, null);

                if (ds != null && ds.Tables.Count > 0)
                {
                    foreach (DataRow r in ds.Tables[0].Rows)
                    {
                        JournalRow jr = new JournalRow();
                        jr.AccountValue = Util.GetValueOfString(r["AccountValue"]);
                        jr.AccountName = Util.GetValueOfString(r["AccountName"]);
                        jr.Description = Util.GetValueOfString(r["Description"]);
                        jr.OrgName = Util.GetValueOfString(r["OrgName"]);
                        jr.BPName = Util.GetValueOfString(r["BPName"]);
                        jr.OrgTrxName = Util.GetValueOfString(r["OrgTrxName"]);
                        jr.AmtAcctDr = Util.GetValueOfDecimal(r["AmtAcctDr"]);
                        jr.AmtAcctCr = Util.GetValueOfDecimal(r["AmtAcctCr"]);
                        pj.Rows.Add(jr);

                        if (string.IsNullOrEmpty(pj.PostingDate))
                        {
                            pj.PostingDate = FormatDate(Util.GetValueOfDateTime(r["DateAcct"]));
                        }
                        if (string.IsNullOrEmpty(pj.PeriodName))
                        {
                            pj.PeriodName = Util.GetValueOfString(r["PeriodName"]);
                        }
                        if (string.IsNullOrEmpty(pj.CurSymbol))
                        {
                            pj.CurSymbol = Util.GetValueOfString(r["AcctCurSymbol"]);
                            pj.StdPrecision = Util.GetValueOfInt(r["AcctStdPrecision"]);
                        }
                    }
                }

                if (includeTotals)
                {
                    string aggSql = @"SELECT COUNT(fa.Fact_Acct_ID) AS Total,
                                             COALESCE(SUM(fa.AmtAcctDr), 0) AS TotDr,
                                             COALESCE(SUM(fa.AmtAcctCr), 0) AS TotCr
                                        FROM Fact_Acct fa
                                        INNER JOIN AD_ClientInfo ci ON (ci.AD_Client_ID=fa.AD_Client_ID)
                                       WHERE fa.AD_Table_ID=" + tableId + @"
                                         AND fa.Record_ID=@C_Payment_ID
                                         AND fa.C_AcctSchema_ID=ci.C_AcctSchema1_ID";
                    aggSql = MRole.GetDefault(ctx).AddAccessSQL(
                        aggSql, "fa", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                    DataSet ads = DB.ExecuteDataset(aggSql, new SqlParameter[]
                    {
                        new SqlParameter("@C_Payment_ID", C_Payment_ID)
                    }, null);

                    if (ads != null && ads.Tables.Count > 0 && ads.Tables[0].Rows.Count > 0)
                    {
                        DataRow ar = ads.Tables[0].Rows[0];
                        pj.Total = Util.GetValueOfInt(ar["Total"]);
                        pj.TotalDr = Util.GetValueOfDecimal(ar["TotDr"]);
                        pj.TotalCr = Util.GetValueOfDecimal(ar["TotCr"]);
                    }
                }
            }
            catch (Exception ex)
            {
                /* Non-fatal: the posting section is dropped, the rest of the panel
                   still renders. */
                _log.Severe("VAS_191 LoadPostedJournal(" + C_Payment_ID + "): " + ex.Message);
            }

            return pj;
        }

        /// <summary>
        /// One page of posted-journal rows for the section pager. Totals and the row
        /// count are already on the client from the initial load, so they are not
        /// recomputed here.
        /// </summary>
        /// <param name="ctx">User context.</param>
        /// <param name="C_Payment_ID">Selected payment id.</param>
        /// <param name="page">Zero-based page index.</param>
        /// <param name="pageSize">Rows per page.</param>
        /// <returns>Posted-journal info carrying the requested page only.</returns>
        public PostedJournalInfo GetPostedJournalPage(Ctx ctx, int C_Payment_ID, int page, int pageSize)
        {
            return LoadPostedJournal(ctx, C_Payment_ID, page,
                                     pageSize > 0 ? pageSize : JOURNAL_PAGE_SIZE, false);
        }

        /// <summary>
        /// DB-specific row-limit suffix for server-side paging: Oracle uses
        /// OFFSET / FETCH, PostgreSQL LIMIT / OFFSET. page and pageSize are
        /// integers the server owns (no injection risk), so they are inlined — some
        /// engines reject a bound LIMIT.
        /// </summary>
        /// <param name="page">Zero-based page index.</param>
        /// <param name="pageSize">Rows per page.</param>
        /// <returns>SQL suffix.</returns>
        private static string PagingSuffix(int page, int pageSize)
        {
            if (page < 0)
            {
                page = 0;
            }
            if (pageSize <= 0)
            {
                pageSize = JOURNAL_PAGE_SIZE;
            }
            int offset = page * pageSize;
            if (DB.IsOracle())
            {
                return " OFFSET " + offset + " ROWS FETCH NEXT " + pageSize + " ROWS ONLY";
            }
            return " LIMIT " + pageSize + " OFFSET " + offset;
        }

        // ----------------------------------------------------------------- //
        //  6. Standard Payment Allocation form                               //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// Resolves the AD_Form_ID of the standard Payment Allocation form
        /// (VAdvantage.Apps.AForms.VAllocation), which the panel's Apply Advance
        /// action opens through VIS.viewManager.startForm. The panel builds no
        /// allocation UI of its own.
        /// </summary>
        /// <returns>AD_Form_ID, or 0 when the form is not registered.</returns>
        private static int GetAllocationFormId()
        {
            string sql = @"SELECT frm.AD_Form_ID
                             FROM AD_Form frm
                            WHERE frm.ClassName=@ClassName
                              AND frm.IsActive='Y'";

            SqlParameter[] param = new SqlParameter[]
            {
                new SqlParameter("@ClassName", ALLOCATION_FORM_CLASSNAME)
            };

            try
            {
                return Util.GetValueOfInt(DB.ExecuteScalar(sql, param, null));
            }
            catch (Exception ex)
            {
                _log.Severe("VAS_191 GetAllocationFormId: " + ex.Message);
                return 0;
            }
        }

        /// <summary>
        /// Resolves an AD_Window_ID from a window NAME for the panel's record-open
        /// path. Window ids differ between environments, so the panel never carries
        /// a numeric one — it names the window and asks for the id.
        /// </summary>
        /// <param name="ctx">User context.</param>
        /// <param name="windowName">AD_Window.Name to resolve.</param>
        /// <returns>The window id, or 0 when the name is unknown to this tenant.</returns>
        public int GetWindowId(Ctx ctx, string windowName)
        {
            return new PoReceiptTabPanelModel().GetWindowId(windowName, "");
        }

        // ----------------------------------------------------------------- //
        //  Shared helpers                                                    //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// Resolves the translated display name of a List-reference column value
        /// (DocStatus, Posted, TenderType, VA009_ExecutionStatus) from
        /// AD_Ref_List / AD_Ref_List_Trl for the session language. The AD_Column is
        /// located by TableName + ColumnName — stable across environments — rather
        /// than by a hard-coded AD_Column_ID.
        /// </summary>
        /// <param name="ctx">User context (supplies the UI language).</param>
        /// <param name="tableName">Physical table owning the column.</param>
        /// <param name="columnName">List-reference column.</param>
        /// <param name="code">Stored short code.</param>
        /// <returns>Translated display name, or the code itself when unmapped.</returns>
        private string GetListReferenceName(Ctx ctx, string tableName, string columnName, string code)
        {
            if (ctx == null || string.IsNullOrEmpty(code))
            {
                return code;
            }

            string sql = @"SELECT COALESCE(rlt.Name, rl.Name, rl.Value) AS DisplayName
                             FROM AD_Column col
                             INNER JOIN AD_Table tbl ON (tbl.AD_Table_ID=col.AD_Table_ID)
                             INNER JOIN AD_Ref_List rl ON (rl.AD_Reference_ID=col.AD_Reference_Value_ID)
                             LEFT OUTER JOIN AD_Ref_List_Trl rlt ON (rlt.AD_Ref_List_ID=rl.AD_Ref_List_ID
                                                                     AND rlt.AD_Language=@Language
                                                                     AND rlt.IsActive='Y')
                            WHERE tbl.TableName=@TableName
                              AND col.ColumnName=@ColumnName
                              AND col.IsActive='Y'
                              AND rl.IsActive='Y'
                              AND rl.Value=@Code";

            try
            {
                string name = Util.GetValueOfString(DB.ExecuteScalar(sql, new SqlParameter[]
                {
                    new SqlParameter("@Language", ctx.GetAD_Language()),
                    new SqlParameter("@TableName", tableName),
                    new SqlParameter("@ColumnName", columnName),
                    new SqlParameter("@Code", code)
                }, null));

                return string.IsNullOrEmpty(name) ? code : name;
            }
            catch (Exception ex)
            {
                _log.Severe("VAS_191 GetListReferenceName(" + tableName + "." + columnName + "): " + ex.Message);
                return code;
            }
        }

        /// <summary>
        /// True when the column exists on the table in the application dictionary.
        /// A lookup problem degrades to "absent", which drops the optional column
        /// from the query instead of failing the whole panel.
        /// </summary>
        /// <param name="tableName">Physical table name.</param>
        /// <param name="columnName">Column to test.</param>
        /// <returns>True when the dictionary knows the column.</returns>
        private bool ColumnExists(string tableName, string columnName)
        {
            try
            {
                string sql = @"SELECT COUNT(1)
                                 FROM AD_Column col
                                 INNER JOIN AD_Table tbl ON (tbl.AD_Table_ID=col.AD_Table_ID)
                                WHERE UPPER(col.ColumnName)=UPPER(@ColumnName)
                                  AND UPPER(tbl.TableName)=UPPER(@TableName)";

                /* Two binds, each occurring once, in the order they appear. */
                SqlParameter[] param = new SqlParameter[]
                {
                    new SqlParameter("@ColumnName", columnName),
                    new SqlParameter("@TableName", tableName)
                };

                return Util.GetValueOfInt(DB.ExecuteScalar(sql, param, null)) > 0;
            }
            catch (Exception ex)
            {
                _log.Severe("VAS_191 ColumnExists(" + tableName + "." + columnName + "): " + ex.Message);
                return false;
            }
        }

        /// <summary>Rounds an amount to the currency precision, away from zero.</summary>
        /// <param name="value">Raw amount.</param>
        /// <param name="precision">Currency standard precision.</param>
        /// <returns>Rounded amount.</returns>
        private static decimal Round(decimal value, int precision)
        {
            int p = (precision >= 0 && precision <= 28) ? precision : 2;
            return Math.Round(value, p, MidpointRounding.AwayFromZero);
        }

        /// <summary>Formats a nullable date as ISO yyyy-MM-dd (empty when null); the
        /// client owns the display format so the user's locale decides it.</summary>
        /// <param name="value">Date to format.</param>
        /// <returns>ISO date string, or "".</returns>
        private static string FormatDate(DateTime? value)
        {
            return value.HasValue
                ? value.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : "";
        }

        /// <summary>Masks all but the last <paramref name="keep"/> characters of an
        /// account number, so a bank account can be identified without exposing it.</summary>
        /// <param name="value">Account number.</param>
        /// <param name="keep">Trailing characters to keep.</param>
        /// <returns>Masked account number, or "" when there is nothing to mask.</returns>
        private static string MaskedTail(string value, int keep)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "";
            }
            string trimmed = value.Trim();
            if (trimmed.Length <= keep)
            {
                return trimmed;
            }
            return "****" + trimmed.Substring(trimmed.Length - keep);
        }

        // ----------------------------------------------------------------- //
        //  Payload                                                           //
        // ----------------------------------------------------------------- //

        /// <summary>Everything the panel renders for one payment.</summary>
        public class PaymentOverviewData
        {
            public int C_Payment_ID { get; set; }
            public string DocumentNo { get; set; }
            public int AD_Org_ID { get; set; }
            public string OrgName { get; set; }

            /// <summary>Y = money received (Receipt); N = money paid (Payment). The
            /// panel labels itself from this — no AR / AP wording is hard-coded.</summary>
            public bool IsReceipt { get; set; }
            public bool IsPrepayment { get; set; }
            public bool IsAllocated { get; set; }
            public bool IsReconciled { get; set; }
            /// <summary>Approval flag - the chip and the first rail step of the
            /// Approval section read from it.</summary>
            public bool IsApproved { get; set; }
            public bool Processed { get; set; }

            public int C_BPartner_ID { get; set; }
            public string BPName { get; set; }
            public string BPValue { get; set; }
            /// <summary>The partner LOCATION the payment was made to / received at.
            /// The panel names this location under the partner instead of repeating
            /// the partner's code and the booking organisation.</summary>
            public int C_BPartner_Location_ID { get; set; }
            public string BPLocationName { get; set; }
            public string BPEMail { get; set; }
            public int C_Currency_ID { get; set; }
            public int C_ConversionType_ID { get; set; }
            public string CurISO { get; set; }
            public string CurSymbol { get; set; }
            public int StdPrecision { get; set; }

            public string DateTrx { get; set; }
            public string DateAcct { get; set; }
            public decimal PayAmt { get; set; }
            public decimal PaymentAmount { get; set; }
            public decimal DiscountAmt { get; set; }
            public decimal WriteOffAmt { get; set; }
            public decimal OverUnderAmt { get; set; }

            public string DocStatus { get; set; }
            public string DocStatusName { get; set; }
            public string DocAction { get; set; }
            public string Posted { get; set; }
            public string PostedName { get; set; }
            public string DocTypeName { get; set; }
            /// <summary>C_DocType.DocBaseType — 'ARR' for an AR receipt, 'APP' for an
            /// AP payment. The panel's direction wording falls back to this when the
            /// IsReceipt flag disagrees with the document the payment was booked on.</summary>
            public string DocBaseType { get; set; }

            public string TenderType { get; set; }
            public string TenderTypeName { get; set; }
            public string CheckNo { get; set; }
            public string CheckDate { get; set; }
            public string Description { get; set; }

            public int VA009_PaymentMethod_ID { get; set; }
            public string PaymentMethodName { get; set; }
            public string ExecutionStatus { get; set; }
            public string ExecutionStatusName { get; set; }

            public int C_BankAccount_ID { get; set; }
            public string BankAccountNo { get; set; }
            public string BankName { get; set; }

            public int C_Invoice_ID { get; set; }
            public string InvoiceDocumentNo { get; set; }
            public int C_InvoicePaySchedule_ID { get; set; }
            public string ScheduleDueDate { get; set; }
            public int C_Charge_ID { get; set; }
            public string ChargeName { get; set; }

            /// <summary>Withholding legs carried by the payment — the withholding and
            /// the backup withholding — each with its type, rate, derived base and
            /// the amount withheld. Empty when the payment names neither, and the
            /// section is then not rendered at all.</summary>
            public List<WithholdingRow> Withholding { get; set; }
            /// <summary>Sum of the withheld amounts shown, for the section footer.</summary>
            public decimal WithholdingTotal { get; set; }

            public string CreatedByName { get; set; }
            public string Created { get; set; }
            public string Updated { get; set; }

            /// <summary>ALLOCPAYMENTAVAILABLE as stored — signed (negative on a
            /// purchase payment). Kept so the client can tell "nothing available"
            /// from "available in the other direction".</summary>
            public decimal AvailableSigned { get; set; }
            /// <summary>Magnitude of the amount still available for allocation.</summary>
            public decimal UnallocatedAmount { get; set; }

            public bool CanComplete { get; set; }
            public bool CanReverse { get; set; }
            public bool CanApplyAdvance { get; set; }

            /// <summary>AD_Form_ID of the standard Payment Allocation form; 0 when
            /// it is not registered, and the Apply Advance action then reports why
            /// rather than dying silently.</summary>
            public int AllocationFormId { get; set; }

            public List<PaymentAllocateRow> PaymentAllocate { get; set; }
            public decimal PaymentAllocateTotal { get; set; }

            public List<AllocationDetailRow> AllocationDetail { get; set; }
            /// <summary>Number of distinct allocations shown (mirror lines already
            /// collapsed), for the section's "{0} allocation(s)" count.</summary>
            public int AllocationTotal { get; set; }
            /// <summary>Footer aggregates over the rows shown. Magnitudes, so the
            /// band reads the same for a payment and a receipt; the individual rows
            /// keep their stored sign.</summary>
            public decimal AllocationAmount { get; set; }
            public decimal AllocationDiscount { get; set; }
            public decimal AllocationWriteOff { get; set; }

            /// <summary>Posted accounting facts (Fact_Acct) of this payment; null /
            /// empty when the document is not posted, and the section is then not
            /// rendered at all.</summary>
            public PostedJournalInfo PostedJournal { get; set; }
        }

        /// <summary>One withholding leg of the payment (C_Payment + C_Withholding).</summary>
        public class WithholdingRow
        {
            /// <summary>True for the BACKUP withholding leg
            /// (BackupWithholding_ID / BackupWithholdingAmount), false for the
            /// primary one (C_Withholding_ID / WithholdingAmt).</summary>
            public bool IsBackup { get; set; }
            public int C_Withholding_ID { get; set; }
            /// <summary>C_Withholding.Name — the withholding TYPE ("TDS"), never the id.</summary>
            public string Name { get; set; }
            /// <summary>C_Withholding.PayPercentage. 0 when the type is not a
            /// percentage one, and the panel then shows no rate and no base.</summary>
            public decimal Rate { get; set; }
            /// <summary>Amount the payment carries for this leg.</summary>
            public decimal Amount { get; set; }
            /// <summary>Amount grossed back up by the rate. Derived — C_Payment
            /// stores no base — and 0 when there is no rate to derive it from.</summary>
            public decimal BaseAmount { get; set; }
        }

        /// <summary>One prepared allocation entry (C_PaymentAllocate).</summary>
        public class PaymentAllocateRow
        {
            public int C_PaymentAllocate_ID { get; set; }
            /// <summary>Invoice / Journal / Other — the client's icon + zoom target.</summary>
            public string ReferenceType { get; set; }
            /// <summary>Id of the referenced document (C_Invoice_ID or GL_Journal_ID).</summary>
            public int ReferenceID { get; set; }
            public string ReferenceDocumentNo { get; set; }
            public string ReferenceDocumentDate { get; set; }

            public int C_Invoice_ID { get; set; }
            /// <summary>C_Invoice.IsExpenseInvoice - an expense invoice opens in its
            /// own window, so the row cannot be sent to the AR / AP invoice one.</summary>
            public bool IsExpenseInvoice { get; set; }
            public int C_InvoicePaySchedule_ID { get; set; }
            public string ScheduleDate { get; set; }
            public decimal ScheduleAmount { get; set; }

            public int GL_JournalLine_ID { get; set; }
            public int GL_Journal_ID { get; set; }
            public int JournalLineNo { get; set; }

            public decimal Amount { get; set; }
            public decimal DiscountAmt { get; set; }
            public decimal WriteOffAmt { get; set; }
            public decimal OverUnderAmt { get; set; }
        }

        /// <summary>One completed allocation line (C_AllocationHdr + C_AllocationLine).</summary>
        public class AllocationDetailRow
        {
            public int C_AllocationHdr_ID { get; set; }
            public int C_AllocationLine_ID { get; set; }
            public string AllocationNo { get; set; }
            public string AllocationDate { get; set; }
            public string AllocationDocStatus { get; set; }

            /// <summary>Invoice / Payment / Journal / Other.</summary>
            public string ReferenceType { get; set; }
            public int ReferenceID { get; set; }
            public string ReferenceDocumentNo { get; set; }
            public string ReferenceDocumentDate { get; set; }
            /// <summary>Document type of the counterpart, for the reference sub-line.</summary>
            public string DocTypeName { get; set; }
            /// <summary>C_Invoice.IsExpenseInvoice of the counterpart invoice - an
            /// expense invoice opens in its own window. False for any other
            /// counterpart type.</summary>
            public bool IsExpenseInvoice { get; set; }
            /// <summary>Configured payment method of a counterpart payment (VA009),
            /// never derived from the tender code.</summary>
            public string PaymentMethodName { get; set; }
            /// <summary>True only when the counterpart is a payment — a GL journal or
            /// an invoice has no reconciliation state to report.</summary>
            public bool HasReconciliation { get; set; }
            public bool IsReconciled { get; set; }
            /// <summary>The pay schedule the line settled.</summary>
            public int C_InvoicePaySchedule_ID { get; set; }
            /// <summary>1-based ordinal of that schedule inside its invoice
            /// ("Schedule 2"); 0 when the line settles no schedule.</summary>
            public int ScheduleNo { get; set; }

            public decimal Amount { get; set; }
            public decimal DiscountAmt { get; set; }
            public decimal WriteOffAmt { get; set; }
            public decimal OverUnderAmt { get; set; }

            /// <summary>Currency of the ALLOCATION header — not necessarily the
            /// payment's currency, so the panel always names it.</summary>
            public int C_Currency_ID { get; set; }
            public string CurrencyIso { get; set; }
            public string CurrencySymbol { get; set; }
            public int Precision { get; set; }
        }

        /// <summary>The payment's posted accounting facts plus what the footer needs.</summary>
        public class PostedJournalInfo
        {
            public List<JournalRow> Rows { get; set; }
            /// <summary>Fact-line count over the whole document (all pages).</summary>
            public int Total { get; set; }
            public decimal TotalDr { get; set; }
            public decimal TotalCr { get; set; }
            public string PeriodName { get; set; }
            public string PostingDate { get; set; }
            /// <summary>Accounting-schema currency — posted amounts are ACCOUNTED
            /// amounts, so the payment's own currency does not apply here.</summary>
            public string CurSymbol { get; set; }
            public int StdPrecision { get; set; }
        }

        /// <summary>One posted accounting fact line.</summary>
        public class JournalRow
        {
            public string AccountValue { get; set; }
            public string AccountName { get; set; }
            public string Description { get; set; }
            public string OrgName { get; set; }
            public string BPName { get; set; }
            public string OrgTrxName { get; set; }
            public decimal AmtAcctDr { get; set; }
            public decimal AmtAcctCr { get; set; }
        }
    }
}
