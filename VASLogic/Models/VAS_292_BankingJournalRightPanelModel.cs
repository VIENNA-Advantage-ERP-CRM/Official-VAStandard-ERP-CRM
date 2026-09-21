/// <summary>
/// Module Name : VASLogic
/// Purpose     : Banking Journal right panel data (read side). Returns a
///               contextual, read-only summary of the selected C_BankStatement
///               record for the VAS.VAS_292_BankingJournalRightPanel tab panel:
///
///                 - the statement header with its organisation, bank account,
///                   bank and the BANK ACCOUNT currency (symbol / ISO / precision),
///                   plus the audit names (creator / last updater) in one read,
///                 - one aggregate over the active C_BankStatementLine rows
///                   (count, matched / unmatched, inflow, outflow, charges,
///                   interest) - never one query per line,
///                 - the journal lines paged on the SERVER (LINES_PAGE_SIZE per
///                   request) in Line order, with payment / partner / charge
///                   resolved for the tooltip; the initial payload carries page
///                   0, further pages arrive through GetJournalLines,
///                 - the accounting impact from Fact_Acct (Actual posting type
///                   only): entry count, Dr / Cr totals and the per-account
///                   breakdown - read only when the statement is posted,
///                 - the workflow activities recorded against the record
///                   (AD_WF_Process -> AD_WF_Activity -> AD_WF_Node: document
///                   action nodes for Prepare / Complete, user nodes for
///                   Approve) and the posting moment + user from Fact_Acct,
///                   from which the panel composes the Audit trail (Drafted ->
///                   In progress -> Approved -> Completed -> Posted).
///
///               C_BankStatement is the primary record table. Fact_Acct is read
///               only to show the accounting impact - it is never the source of
///               the journal itself.
///
///               MRole access filtering is applied to the MAIN physical table of
///               each query - the alias the user actually fetches from - and never
///               to a lookup join (AD_Org, C_BankAccount, C_Bank, C_Currency,
///               AD_User, C_ElementValue). ORDER BY and GROUP BY are appended AFTER
///               AddAccessSQL, because the rewriter appends its predicate to the
///               end of the WHERE clause it is given. The last JOIN of every
///               statement keeps a plain ON (no function call), which the access
///               parser needs.
///
///               Portability: COALESCE / CASE / ANSI joins only - no NVL, DECODE
///               or ROWNUM - so one statement serves both Oracle and PostgreSQL;
///               the only DB-specific piece is the row-limit suffix for paging
///               (PagingSuffix), appended last. Free-text literals carry the
///               national-character prefix; stored code comparisons
///               (IsActive='Y', PostingType='A') deliberately do not. The account
///               number is never masked in SQL - the client masks it.
///
///               List-reference columns (DocStatus, BankAccountType, WFState) are
///               resolved to their translated names through AD_Ref_List /
///               AD_Ref_List_Trl; the panel never renders a raw code. Posted /
///               Processed / IsApproved / IsManual / MatchStatement are Yes-No
///               flags and are labelled on the client through AD_Message.
/// Chronological development:
///   VAI145   2026-09-21  Created.
///   VAI145   2026-09-21  Lines paged on the server (LINES_PAGE_SIZE = 20) through
///                        GetJournalLines; workflow activities + posting moment
///                        read for the Audit trail.
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
    public class VAS_292_BankingJournalRightPanelModel
    {
        private static readonly VLogger _log =
            VLogger.GetVLogger(typeof(VAS_292_BankingJournalRightPanelModel).FullName);

        /// <summary>Journal lines per page (server-side paged). The initial payload
        /// carries page 0; the panel asks for further pages through GetJournalLines.</summary>
        public const int LINES_PAGE_SIZE = 20;

        // ----------------------------------------------------------------- //
        //  Entry point                                                       //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// Returns the full panel payload for the selected banking journal. The
        /// statement row itself is read under MRole, so an id the browser sent for
        /// a record the role cannot see comes back as an empty payload - the
        /// client-supplied id is never trusted on its own.
        /// </summary>
        /// <param name="ctx">User context (client / org / role / language).</param>
        /// <param name="C_BankStatement_ID">Selected banking journal id.</param>
        /// <returns>Populated <see cref="BankingJournalPanelData"/>; an empty instance
        /// (C_BankStatement_ID = 0) when the id is invalid or no accessible row exists.</returns>
        public BankingJournalPanelData GetJournalOverview(Ctx ctx, int C_BankStatement_ID)
        {
            BankingJournalPanelData result = new BankingJournalPanelData();
            result.Lines = new List<JournalLineRow>();
            result.Accounts = new List<AccountRow>();
            result.WorkflowSteps = new List<WorkflowStepRow>();

            if (ctx == null || C_BankStatement_ID <= 0)
            {
                return result;
            }

            if (!LoadHeader(ctx, C_BankStatement_ID, result))
            {
                return result;   // not accessible to this role, or does not exist
            }

            /* One aggregate over every active line - the count, matched / unmatched
               split and the money totals all come from it; the rows are the first
               page only. Further pages arrive through GetJournalLines. */
            LoadLineSummary(ctx, C_BankStatement_ID, result);
            result.LinesPageSize = LINES_PAGE_SIZE;
            result.Lines = LoadLines(ctx, C_BankStatement_ID, 0, LINES_PAGE_SIZE);

            /* The Audit trail is composed on the client from these facts. */
            result.WorkflowSteps = LoadWorkflowSteps(ctx, C_BankStatement_ID);

            /* Balance checks are decided here, on the decimal values, at the bank
               account currency's precision - never on formatted strings. */
            result.IsBalanced = IsWithinTolerance(result.StatementDifference, result.StdPrecision);

            /* Fact_Acct is read only when accounting data can exist: a statement that
               was never posted has no facts, and the section then reads "Unposted". */
            if (result.Posted == "Y")
            {
                LoadAccountingSummary(ctx, C_BankStatement_ID, result);
                if (result.EntryCount > 0)
                {
                    result.Accounts = LoadAccountBreakdown(ctx, C_BankStatement_ID);
                }
                LoadPostedOn(ctx, C_BankStatement_ID, result);
            }
            result.IsPostingBalanced = IsWithinTolerance(result.TotalDebit - result.TotalCredit, result.StdPrecision);

            return result;
        }

        // ----------------------------------------------------------------- //
        //  1. Header + bank account + audit                                  //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// Reads the statement identity, status flags, balances, organisation, bank
        /// account, bank, the bank account currency and the audit names. This is the
        /// query that decides whether the caller may see the record at all, so MRole
        /// is applied here, on C_BankStatement. AD_User is joined LEFT OUTER so a
        /// record whose creator was deleted still comes back.
        /// </summary>
        /// <param name="ctx">User context.</param>
        /// <param name="C_BankStatement_ID">Selected banking journal id.</param>
        /// <param name="result">Payload filled in place.</param>
        /// <returns>True when an accessible row was found.</returns>
        private bool LoadHeader(Ctx ctx, int C_BankStatement_ID, BankingJournalPanelData result)
        {
            StringBuilder sql = new StringBuilder();
            sql.Append(@"SELECT bs.C_BankStatement_ID,
                                bs.AD_Org_ID,
                                org.Name AS OrgName,
                                bs.DocumentNo,
                                bs.StatementDate,
                                COALESCE(bs.Name, N'') AS Name,
                                COALESCE(bs.Description, N'') AS Description,
                                bs.BeginningBalance,
                                bs.EndingBalance,
                                bs.StatementDifference,
                                bs.DocStatus,
                                bs.Posted,
                                bs.Processed,
                                COALESCE(bs.IsApproved, 'N') AS IsApproved,
                                COALESCE(bs.IsManual, 'N') AS IsManual,
                                COALESCE(bs.MatchStatement, 'N') AS MatchStatement,
                                bs.Created,
                                bs.Updated,
                                ba.C_BankAccount_ID,
                                ba.Name AS BankAccountName,
                                ba.AccountNo,
                                ba.IBAN,
                                ba.BankAccountType,
                                ba.CurrentBalance,
                                ba.UnMatchedBalance,
                                b.C_Bank_ID,
                                b.Name AS BankName,
                                b.SwiftCode,
                                b.RoutingNo,
                                cur.C_Currency_ID,
                                cur.ISO_Code AS CurISO,
                                CASE WHEN cur.CurSymbol IS NOT NULL THEN cur.CurSymbol ELSE cur.ISO_Code END AS CurSymbol,
                                cur.StdPrecision,
                                cu.Name AS CreatedByName,
                                uu.Name AS UpdatedByName
                         FROM C_BankStatement bs
                         INNER JOIN AD_Org org ON (org.AD_Org_ID=bs.AD_Org_ID)
                         INNER JOIN C_BankAccount ba ON (ba.C_BankAccount_ID=bs.C_BankAccount_ID)
                         INNER JOIN C_Bank b ON (b.C_Bank_ID=ba.C_Bank_ID)
                         INNER JOIN C_Currency cur ON (cur.C_Currency_ID=ba.C_Currency_ID)
                         LEFT OUTER JOIN AD_User cu ON (cu.AD_User_ID=bs.CreatedBy)
                         LEFT OUTER JOIN AD_User uu ON (uu.AD_User_ID=bs.UpdatedBy)
                         WHERE bs.C_BankStatement_ID=@C_BankStatement_ID
                           AND bs.IsActive='Y'
                           AND bs.AD_Client_ID=@AD_Client_ID");

            string accessSql = MRole.GetDefault(ctx).AddAccessSQL(
                sql.ToString(), "bs", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            /* Two binds, each occurring once, in the order they appear. */
            SqlParameter[] param = new SqlParameter[]
            {
                new SqlParameter("@C_BankStatement_ID", C_BankStatement_ID),
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
            };

            DataSet ds = null;
            try
            {
                ds = DB.ExecuteDataset(accessSql, param, null);
            }
            catch (Exception ex)
            {
                _log.Severe("VAS_292 LoadHeader(" + C_BankStatement_ID + "): " + ex.Message);
                return false;
            }

            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
            {
                return false;
            }

            DataRow r = ds.Tables[0].Rows[0];

            result.C_BankStatement_ID = Util.GetValueOfInt(r["C_BankStatement_ID"]);
            result.AD_Org_ID = Util.GetValueOfInt(r["AD_Org_ID"]);
            result.OrgName = Util.GetValueOfString(r["OrgName"]);
            result.DocumentNo = Util.GetValueOfString(r["DocumentNo"]);
            result.StatementDate = FormatDate(Util.GetValueOfDateTime(r["StatementDate"]));
            result.Name = Util.GetValueOfString(r["Name"]);
            result.Description = Util.GetValueOfString(r["Description"]);

            result.DocStatus = Util.GetValueOfString(r["DocStatus"]);
            /* DocStatus is a LIST column stored as a short code; the panel must never
               render the raw code, so the dictionary resolves the display name. */
            result.DocStatusName = GetListReferenceName(ctx, "C_BankStatement", "DocStatus", result.DocStatus);
            result.Posted = Util.GetValueOfString(r["Posted"]);
            result.Processed = Util.GetValueOfString(r["Processed"]) == "Y";
            result.IsApproved = Util.GetValueOfString(r["IsApproved"]) == "Y";
            result.IsManual = Util.GetValueOfString(r["IsManual"]) == "Y";
            result.MatchStatement = Util.GetValueOfString(r["MatchStatement"]) == "Y";

            /* Bank account currency drives every header / summary amount. */
            result.C_Currency_ID = Util.GetValueOfInt(r["C_Currency_ID"]);
            result.CurISO = Util.GetValueOfString(r["CurISO"]);
            result.CurSymbol = Util.GetValueOfString(r["CurSymbol"]);
            result.StdPrecision = Util.GetValueOfInt(r["StdPrecision"]);

            result.BeginningBalance = Round(Util.GetValueOfDecimal(r["BeginningBalance"]), result.StdPrecision);
            result.EndingBalance = Round(Util.GetValueOfDecimal(r["EndingBalance"]), result.StdPrecision);
            result.StatementDifference = Round(Util.GetValueOfDecimal(r["StatementDifference"]), result.StdPrecision);
            /* Headline figure: aligned with the statement balances, not the lines. */
            result.NetMovement = result.EndingBalance - result.BeginningBalance;

            result.C_BankAccount_ID = Util.GetValueOfInt(r["C_BankAccount_ID"]);
            result.BankAccountName = Util.GetValueOfString(r["BankAccountName"]);
            /* Full number leaves the server; the client masks it to the last four. */
            result.AccountNo = Util.GetValueOfString(r["AccountNo"]);
            result.IBAN = Util.GetValueOfString(r["IBAN"]);
            result.BankAccountType = Util.GetValueOfString(r["BankAccountType"]);
            result.BankAccountTypeName = GetListReferenceName(ctx, "C_BankAccount", "BankAccountType", result.BankAccountType);
            result.CurrentBalance = Round(Util.GetValueOfDecimal(r["CurrentBalance"]), result.StdPrecision);
            result.UnMatchedBalance = Round(Util.GetValueOfDecimal(r["UnMatchedBalance"]), result.StdPrecision);

            result.C_Bank_ID = Util.GetValueOfInt(r["C_Bank_ID"]);
            result.BankName = Util.GetValueOfString(r["BankName"]);
            result.SwiftCode = Util.GetValueOfString(r["SwiftCode"]);
            result.RoutingNo = Util.GetValueOfString(r["RoutingNo"]);

            result.Created = FormatStamp(Util.GetValueOfDateTime(r["Created"]));
            result.CreatedByName = Util.GetValueOfString(r["CreatedByName"]);
            result.Updated = FormatStamp(Util.GetValueOfDateTime(r["Updated"]));
            result.UpdatedByName = Util.GetValueOfString(r["UpdatedByName"]);

            return true;
        }

        // ----------------------------------------------------------------- //
        //  2. Line aggregate                                                 //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// Count, matched / unmatched split and money totals over EVERY active line,
        /// in one aggregate. Inflow is the sum of positive StmtAmt, outflow the
        /// absolute sum of negative StmtAmt - StmtAmt is the bank statement amount
        /// and is never inferred from TrxAmt. Flat SUM(CASE ...) only: no nested
        /// selects for the access parser to choke on.
        /// </summary>
        /// <param name="ctx">User context.</param>
        /// <param name="C_BankStatement_ID">Selected banking journal id.</param>
        /// <param name="result">Payload filled in place (LineCount, MatchedCount,
        /// UnmatchedCount, InflowAmount, OutflowAmount, ChargeAmount, InterestAmount).</param>
        private void LoadLineSummary(Ctx ctx, int C_BankStatement_ID, BankingJournalPanelData result)
        {
            string sql = @"SELECT COUNT(bsl.C_BankStatementLine_ID) AS LineCount,
                                  SUM(CASE WHEN bsl.MatchStatement='Y' THEN 1 ELSE 0 END) AS MatchedCount,
                                  SUM(CASE WHEN COALESCE(bsl.MatchStatement, 'N')<>'Y' THEN 1 ELSE 0 END) AS UnmatchedCount,
                                  SUM(CASE WHEN bsl.StmtAmt>0 THEN bsl.StmtAmt ELSE 0 END) AS InflowAmount,
                                  SUM(CASE WHEN bsl.StmtAmt<0 THEN ABS(bsl.StmtAmt) ELSE 0 END) AS OutflowAmount,
                                  SUM(COALESCE(bsl.ChargeAmt, 0)) AS ChargeAmount,
                                  SUM(COALESCE(bsl.InterestAmt, 0)) AS InterestAmount
                             FROM C_BankStatementLine bsl
                            WHERE bsl.C_BankStatement_ID=@C_BankStatement_ID
                              AND bsl.IsActive='Y'
                              AND bsl.AD_Client_ID=@AD_Client_ID";

            string accessSql = MRole.GetDefault(ctx).AddAccessSQL(
                sql, "bsl", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            /* Two binds, each occurring once, in the order they appear. */
            SqlParameter[] param = new SqlParameter[]
            {
                new SqlParameter("@C_BankStatement_ID", C_BankStatement_ID),
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
            };

            try
            {
                DataSet ds = DB.ExecuteDataset(accessSql, param, null);
                if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                {
                    DataRow r = ds.Tables[0].Rows[0];
                    int p = result.StdPrecision;
                    result.LineCount = Util.GetValueOfInt(r["LineCount"]);
                    result.MatchedCount = Util.GetValueOfInt(r["MatchedCount"]);
                    result.UnmatchedCount = Util.GetValueOfInt(r["UnmatchedCount"]);
                    result.InflowAmount = Round(Util.GetValueOfDecimal(r["InflowAmount"]), p);
                    result.OutflowAmount = Round(Util.GetValueOfDecimal(r["OutflowAmount"]), p);
                    result.ChargeAmount = Round(Util.GetValueOfDecimal(r["ChargeAmount"]), p);
                    result.InterestAmount = Round(Util.GetValueOfDecimal(r["InterestAmount"]), p);
                }
            }
            catch (Exception ex)
            {
                _log.Severe("VAS_292 LoadLineSummary(" + C_BankStatement_ID + "): " + ex.Message);
            }
        }

        // ----------------------------------------------------------------- //
        //  3. Journal lines (paged)                                          //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// One page of the statement's lines, for the panel's pager. Only the rows
        /// are returned - the count and the totals came with the initial payload
        /// and do not change between pages. The statement itself is re-read under
        /// MRole first, so a page can never be fetched for a record the role cannot
        /// see.
        /// </summary>
        /// <param name="ctx">User context.</param>
        /// <param name="C_BankStatement_ID">Selected banking journal id.</param>
        /// <param name="page">Zero-based page index.</param>
        /// <param name="pageSize">Rows per page; 0 or less means LINES_PAGE_SIZE.</param>
        /// <returns>Populated <see cref="JournalLinesPage"/>; empty Rows when the id
        /// is invalid, not accessible, or the page is past the end.</returns>
        public JournalLinesPage GetJournalLines(Ctx ctx, int C_BankStatement_ID, int page, int pageSize)
        {
            JournalLinesPage result = new JournalLinesPage();
            result.Rows = new List<JournalLineRow>();
            result.Page = page < 0 ? 0 : page;
            result.PageSize = pageSize > 0 ? pageSize : LINES_PAGE_SIZE;

            if (ctx == null || C_BankStatement_ID <= 0)
            {
                return result;
            }

            /* The access test is the header read, exactly as on the initial load. */
            BankingJournalPanelData probe = new BankingJournalPanelData();
            if (!LoadHeader(ctx, C_BankStatement_ID, probe))
            {
                return result;
            }

            result.C_BankStatement_ID = C_BankStatement_ID;
            result.Rows = LoadLines(ctx, C_BankStatement_ID, result.Page, result.PageSize);
            return result;
        }

        /// <summary>
        /// One page of the active lines in Line order, with the line's own currency
        /// (kept when it differs from the bank account's), the payment document,
        /// the business partner and the charge resolved for the tooltip. All
        /// lookups are LEFT OUTER so a line without them still comes back. ORDER BY
        /// and the paging suffix go AFTER the access filter.
        /// </summary>
        /// <param name="ctx">User context.</param>
        /// <param name="C_BankStatement_ID">Selected banking journal id.</param>
        /// <param name="page">Zero-based page index.</param>
        /// <param name="pageSize">Rows per page.</param>
        /// <returns>Lines of that page in Line order; empty past the end.</returns>
        private List<JournalLineRow> LoadLines(Ctx ctx, int C_BankStatement_ID, int page, int pageSize)
        {
            List<JournalLineRow> rows = new List<JournalLineRow>();

            string sql = @"SELECT bsl.C_BankStatementLine_ID,
                                  bsl.Line,
                                  bsl.StatementLineDate,
                                  bsl.ValutaDate,
                                  bsl.DateAcct,
                                  COALESCE(bsl.Description, N'') AS Description,
                                  COALESCE(bsl.ReferenceNo, N'') AS ReferenceNo,
                                  bsl.StmtAmt,
                                  bsl.TrxAmt,
                                  bsl.ChargeAmt,
                                  bsl.InterestAmt,
                                  COALESCE(bsl.MatchStatement, 'N') AS MatchStatement,
                                  bsl.Processed,
                                  COALESCE(bsl.IsManual, 'N') AS IsManual,
                                  COALESCE(bsl.IsReversal, 'N') AS IsReversal,
                                  bsl.C_Payment_ID,
                                  bsl.C_BPartner_ID,
                                  bsl.C_Charge_ID,
                                  bsl.C_Currency_ID,
                                  cur.ISO_Code AS CurISO,
                                  CASE WHEN cur.CurSymbol IS NOT NULL THEN cur.CurSymbol ELSE cur.ISO_Code END AS CurSymbol,
                                  cur.StdPrecision,
                                  pay.DocumentNo AS PaymentDocumentNo,
                                  bp.Name AS BPartnerName,
                                  chg.Name AS ChargeName
                             FROM C_BankStatementLine bsl
                             LEFT OUTER JOIN C_Currency cur ON (cur.C_Currency_ID=bsl.C_Currency_ID)
                             LEFT OUTER JOIN C_Payment pay ON (pay.C_Payment_ID=bsl.C_Payment_ID)
                             LEFT OUTER JOIN C_BPartner bp ON (bp.C_BPartner_ID=bsl.C_BPartner_ID)
                             LEFT OUTER JOIN C_Charge chg ON (chg.C_Charge_ID=bsl.C_Charge_ID)
                            WHERE bsl.C_BankStatement_ID=@C_BankStatement_ID
                              AND bsl.IsActive='Y'
                              AND bsl.AD_Client_ID=@AD_Client_ID";

            string accessSql = MRole.GetDefault(ctx).AddAccessSQL(
                sql, "bsl", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            /* ORDER BY and the paging suffix go AFTER the access filter, so the
               predicate lands in the WHERE clause and not behind a trailing clause. */
            accessSql += " ORDER BY bsl.Line, bsl.C_BankStatementLine_ID";
            accessSql += PagingSuffix(page, pageSize);

            /* Two binds, each occurring once, in the order they appear. */
            SqlParameter[] param = new SqlParameter[]
            {
                new SqlParameter("@C_BankStatement_ID", C_BankStatement_ID),
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
            };

            DataSet ds = null;
            try
            {
                ds = DB.ExecuteDataset(accessSql, param, null);
            }
            catch (Exception ex)
            {
                _log.Severe("VAS_292 LoadLines(" + C_BankStatement_ID + "): " + ex.Message);
                return rows;
            }

            if (ds == null || ds.Tables.Count == 0)
            {
                return rows;
            }

            foreach (DataRow r in ds.Tables[0].Rows)
            {
                JournalLineRow row = new JournalLineRow();
                row.C_BankStatementLine_ID = Util.GetValueOfInt(r["C_BankStatementLine_ID"]);
                row.Line = Util.GetValueOfInt(r["Line"]);
                row.StatementLineDate = FormatDate(Util.GetValueOfDateTime(r["StatementLineDate"]));
                row.ValutaDate = FormatDate(Util.GetValueOfDateTime(r["ValutaDate"]));
                row.DateAcct = FormatDate(Util.GetValueOfDateTime(r["DateAcct"]));
                row.Description = Util.GetValueOfString(r["Description"]);
                row.ReferenceNo = Util.GetValueOfString(r["ReferenceNo"]);

                /* Line currency is kept as-is; the client falls back to the bank
                   account currency when the line carries none. */
                row.C_Currency_ID = Util.GetValueOfInt(r["C_Currency_ID"]);
                row.CurISO = Util.GetValueOfString(r["CurISO"]);
                row.CurSymbol = Util.GetValueOfString(r["CurSymbol"]);
                row.StdPrecision = Util.GetValueOfInt(r["StdPrecision"]);
                int p = row.C_Currency_ID > 0 ? row.StdPrecision : 2;

                row.StmtAmt = Round(Util.GetValueOfDecimal(r["StmtAmt"]), p);
                row.TrxAmt = Round(Util.GetValueOfDecimal(r["TrxAmt"]), p);
                row.ChargeAmt = Round(Util.GetValueOfDecimal(r["ChargeAmt"]), p);
                row.InterestAmt = Round(Util.GetValueOfDecimal(r["InterestAmt"]), p);

                row.IsMatched = Util.GetValueOfString(r["MatchStatement"]) == "Y";
                row.Processed = Util.GetValueOfString(r["Processed"]) == "Y";
                row.IsManual = Util.GetValueOfString(r["IsManual"]) == "Y";
                row.IsReversal = Util.GetValueOfString(r["IsReversal"]) == "Y";

                row.C_Payment_ID = Util.GetValueOfInt(r["C_Payment_ID"]);
                row.PaymentDocumentNo = Util.GetValueOfString(r["PaymentDocumentNo"]);
                row.C_BPartner_ID = Util.GetValueOfInt(r["C_BPartner_ID"]);
                row.BPartnerName = Util.GetValueOfString(r["BPartnerName"]);
                row.C_Charge_ID = Util.GetValueOfInt(r["C_Charge_ID"]);
                row.ChargeName = Util.GetValueOfString(r["ChargeName"]);

                rows.Add(row);
            }

            return rows;
        }

        // ----------------------------------------------------------------- //
        //  4. Accounting impact - Fact_Acct                                  //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// Entry count and accounted Dr / Cr totals of the Actual posting for the
        /// statement. The AD_Table_ID of C_BankStatement is dictionary metadata this
        /// model resolves itself through the model class (never a hard-coded
        /// number), so it is an integer literal here rather than a bind - that
        /// keeps the statement to the two bound values, in order.
        /// </summary>
        /// <param name="ctx">User context.</param>
        /// <param name="C_BankStatement_ID">Selected banking journal id.</param>
        /// <param name="result">Payload filled in place (EntryCount, TotalDebit, TotalCredit).</param>
        private void LoadAccountingSummary(Ctx ctx, int C_BankStatement_ID, BankingJournalPanelData result)
        {
            int tableId = MBankStatement.Table_ID;
            if (tableId <= 0)
            {
                return;
            }

            string sql = @"SELECT COUNT(fa.Fact_Acct_ID) AS EntryCount,
                                  SUM(COALESCE(fa.AmtAcctDr, 0)) AS TotalDebit,
                                  SUM(COALESCE(fa.AmtAcctCr, 0)) AS TotalCredit
                             FROM Fact_Acct fa
                            WHERE fa.AD_Table_ID=" + tableId + @"
                              AND fa.Record_ID=@C_BankStatement_ID
                              AND fa.PostingType='A'
                              AND fa.IsActive='Y'
                              AND fa.AD_Client_ID=@AD_Client_ID";

            string accessSql = MRole.GetDefault(ctx).AddAccessSQL(
                sql, "fa", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            /* Two binds, each occurring once, in the order they appear. */
            SqlParameter[] param = new SqlParameter[]
            {
                new SqlParameter("@C_BankStatement_ID", C_BankStatement_ID),
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
            };

            try
            {
                DataSet ds = DB.ExecuteDataset(accessSql, param, null);
                if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                {
                    DataRow r = ds.Tables[0].Rows[0];
                    result.EntryCount = Util.GetValueOfInt(r["EntryCount"]);
                    result.TotalDebit = Round(Util.GetValueOfDecimal(r["TotalDebit"]), result.StdPrecision);
                    result.TotalCredit = Round(Util.GetValueOfDecimal(r["TotalCredit"]), result.StdPrecision);
                }
            }
            catch (Exception ex)
            {
                _log.Severe("VAS_292 LoadAccountingSummary(" + C_BankStatement_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// The Actual posting grouped by ledger account, in account-value order.
        /// GROUP BY and ORDER BY are appended after the access filter. Only called
        /// when the summary found at least one entry, so an empty grid is never
        /// drawn.
        /// </summary>
        /// <param name="ctx">User context.</param>
        /// <param name="C_BankStatement_ID">Selected banking journal id.</param>
        /// <returns>One row per account; empty when nothing is posted.</returns>
        private List<AccountRow> LoadAccountBreakdown(Ctx ctx, int C_BankStatement_ID)
        {
            List<AccountRow> rows = new List<AccountRow>();

            int tableId = MBankStatement.Table_ID;
            if (tableId <= 0)
            {
                return rows;
            }

            /* Table id is a resolved dictionary literal (see LoadAccountingSummary). */
            string sql = @"SELECT fa.Account_ID,
                                  ev.Value AS AccountValue,
                                  ev.Name AS AccountName,
                                  SUM(COALESCE(fa.AmtAcctDr, 0)) AS DebitAmount,
                                  SUM(COALESCE(fa.AmtAcctCr, 0)) AS CreditAmount
                             FROM Fact_Acct fa
                             INNER JOIN C_ElementValue ev ON (ev.C_ElementValue_ID=fa.Account_ID)
                            WHERE fa.AD_Table_ID=" + tableId + @"
                              AND fa.Record_ID=@C_BankStatement_ID
                              AND fa.PostingType='A'
                              AND fa.IsActive='Y'
                              AND fa.AD_Client_ID=@AD_Client_ID";

            string accessSql = MRole.GetDefault(ctx).AddAccessSQL(
                sql, "fa", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            accessSql += " GROUP BY fa.Account_ID, ev.Value, ev.Name ORDER BY ev.Value";

            /* Two binds, each occurring once, in the order they appear. */
            SqlParameter[] param = new SqlParameter[]
            {
                new SqlParameter("@C_BankStatement_ID", C_BankStatement_ID),
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
            };

            DataSet ds = null;
            try
            {
                ds = DB.ExecuteDataset(accessSql, param, null);
            }
            catch (Exception ex)
            {
                _log.Severe("VAS_292 LoadAccountBreakdown(" + C_BankStatement_ID + "): " + ex.Message);
                return rows;
            }

            if (ds == null || ds.Tables.Count == 0)
            {
                return rows;
            }

            foreach (DataRow r in ds.Tables[0].Rows)
            {
                AccountRow row = new AccountRow();
                row.Account_ID = Util.GetValueOfInt(r["Account_ID"]);
                row.AccountValue = Util.GetValueOfString(r["AccountValue"]);
                row.AccountName = Util.GetValueOfString(r["AccountName"]);
                row.DebitAmount = Util.GetValueOfDecimal(r["DebitAmount"]);
                row.CreditAmount = Util.GetValueOfDecimal(r["CreditAmount"]);
                rows.Add(row);
            }

            return rows;
        }

        // ----------------------------------------------------------------- //
        //  5. Audit trail facts - workflow activities + posting moment       //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// Workflow activities recorded against the statement, read through the
        /// platform's own workflow-to-record link (AD_WF_Process.AD_Table_ID +
        /// Record_ID). Document-action nodes (Action 'D') carry the Prepare /
        /// Complete transitions with their actor and moment; user-choice / window
        /// / form nodes ('C' / 'W' / 'X') are the approval steps a person acted
        /// on. The engine's other bookkeeping nodes are left out.
        /// </summary>
        /// <param name="ctx">User context.</param>
        /// <param name="C_BankStatement_ID">Selected banking journal id.</param>
        /// <returns>Activities in the order they were raised; empty when no workflow ran.</returns>
        private List<WorkflowStepRow> LoadWorkflowSteps(Ctx ctx, int C_BankStatement_ID)
        {
            List<WorkflowStepRow> steps = new List<WorkflowStepRow>();

            int tableId = MBankStatement.Table_ID;
            if (tableId <= 0)
            {
                return steps;
            }

            /* Table id is a resolved dictionary literal (see LoadAccountingSummary). */
            string sql = @"SELECT wfa.AD_WF_Activity_ID,
                                  wfa.WFState,
                                  wfa.Created,
                                  wfa.Updated,
                                  wfn.Name AS NodeName,
                                  wfn.Action AS NodeAction,
                                  wfn.DocAction,
                                  au.Name AS AssigneeName,
                                  uu.Name AS UpdatedByName
                             FROM AD_WF_Activity wfa
                             INNER JOIN AD_WF_Process wfp ON (wfp.AD_WF_Process_ID=wfa.AD_WF_Process_ID)
                             INNER JOIN AD_WF_Node wfn ON (wfn.AD_WF_Node_ID=wfa.AD_WF_Node_ID)
                             LEFT OUTER JOIN AD_User au ON (au.AD_User_ID=wfa.AD_User_ID)
                             LEFT OUTER JOIN AD_User uu ON (uu.AD_User_ID=wfa.UpdatedBy)
                            WHERE wfp.AD_Table_ID=" + tableId + @"
                              AND wfp.Record_ID=@C_BankStatement_ID
                              AND wfa.IsActive='Y'
                              AND wfp.IsActive='Y'
                              AND wfn.Action IN ('C','W','X','D')";

            string accessSql = MRole.GetDefault(ctx).AddAccessSQL(
                sql, "wfa", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            accessSql += " ORDER BY wfa.Created, wfa.AD_WF_Activity_ID";

            DataSet ds = null;
            try
            {
                ds = DB.ExecuteDataset(accessSql, new SqlParameter[]
                {
                    new SqlParameter("@C_BankStatement_ID", C_BankStatement_ID)
                }, null);
            }
            catch (Exception ex)
            {
                _log.Severe("VAS_292 LoadWorkflowSteps(" + C_BankStatement_ID + "): " + ex.Message);
                return steps;
            }

            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
            {
                return steps;
            }

            /* WFState is a LIST column; one dictionary read serves every row. */
            Dictionary<string, string> stateLabels = LoadRefListLabels(ctx, "AD_WF_Activity", "WFState");

            foreach (DataRow r in ds.Tables[0].Rows)
            {
                WorkflowStepRow step = new WorkflowStepRow();
                step.AD_WF_Activity_ID = Util.GetValueOfInt(r["AD_WF_Activity_ID"]);
                step.NodeName = Util.GetValueOfString(r["NodeName"]);
                step.NodeAction = Util.GetValueOfString(r["NodeAction"]);
                step.DocAction = Util.GetValueOfString(r["DocAction"]);
                step.WFState = Util.GetValueOfString(r["WFState"]);
                step.WFStateName = (!string.IsNullOrEmpty(step.WFState) && stateLabels.ContainsKey(step.WFState))
                    ? stateLabels[step.WFState]
                    : step.WFState;
                /* The person who closed the step is the actor; while it is still
                   open the assignee is the only name there is. */
                string actor = Util.GetValueOfString(r["UpdatedByName"]);
                if (string.IsNullOrEmpty(actor) || step.WFState != "CC")
                {
                    actor = Util.GetValueOfString(r["AssigneeName"]);
                }
                step.ActorName = actor;
                step.Created = FormatStamp(Util.GetValueOfDateTime(r["Created"]));
                step.Updated = FormatStamp(Util.GetValueOfDateTime(r["Updated"]));
                steps.Add(step);
            }

            return steps;
        }

        /// <summary>
        /// The moment the statement was posted and who posted it - the earliest
        /// Fact_Acct row written for the record and its creator. C_BankStatement
        /// itself carries no posting timestamp. One row is enough, so the paging
        /// suffix limits the read to the first.
        /// </summary>
        /// <param name="ctx">User context.</param>
        /// <param name="C_BankStatement_ID">Selected banking journal id.</param>
        /// <param name="result">Payload filled in place (PostedOn, PostedByName).</param>
        private void LoadPostedOn(Ctx ctx, int C_BankStatement_ID, BankingJournalPanelData result)
        {
            int tableId = MBankStatement.Table_ID;
            if (tableId <= 0)
            {
                return;
            }

            /* Table id is a resolved dictionary literal (see LoadAccountingSummary). */
            string sql = @"SELECT fa.Created AS PostedOn,
                                  pu.Name AS PostedByName
                             FROM Fact_Acct fa
                             LEFT OUTER JOIN AD_User pu ON (pu.AD_User_ID=fa.CreatedBy)
                            WHERE fa.AD_Table_ID=" + tableId + @"
                              AND fa.Record_ID=@C_BankStatement_ID
                              AND fa.IsActive='Y'
                              AND fa.AD_Client_ID=@AD_Client_ID";

            string accessSql = MRole.GetDefault(ctx).AddAccessSQL(
                sql, "fa", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            accessSql += " ORDER BY fa.Created, fa.Fact_Acct_ID";
            accessSql += PagingSuffix(0, 1);

            try
            {
                /* Two binds, each occurring once, in the order they appear. */
                DataSet ds = DB.ExecuteDataset(accessSql, new SqlParameter[]
                {
                    new SqlParameter("@C_BankStatement_ID", C_BankStatement_ID),
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
                }, null);
                if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                {
                    DataRow r = ds.Tables[0].Rows[0];
                    result.PostedOn = FormatStamp(Util.GetValueOfDateTime(r["PostedOn"]));
                    result.PostedByName = Util.GetValueOfString(r["PostedByName"]);
                }
            }
            catch (Exception ex)
            {
                _log.Severe("VAS_292 LoadPostedOn(" + C_BankStatement_ID + "): " + ex.Message);
            }
        }

        // ----------------------------------------------------------------- //
        //  Shared helpers                                                    //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// The display labels of a LIST column's reference values, keyed by their
        /// stored code, in the logged-in user's language. Used where one column is
        /// resolved for many rows, so the dictionary is read once instead of once
        /// per row.
        /// </summary>
        /// <param name="ctx">User context (supplies the UI language).</param>
        /// <param name="tableName">Physical table owning the column.</param>
        /// <param name="columnName">List-reference column.</param>
        /// <returns>Code → display name map; empty when the column has no list.</returns>
        private Dictionary<string, string> LoadRefListLabels(Ctx ctx, string tableName, string columnName)
        {
            Dictionary<string, string> labels = new Dictionary<string, string>();
            if (ctx == null)
            {
                return labels;
            }

            string sql = @"SELECT rl.Value,
                                  COALESCE(rlt.Name, rl.Name, rl.Value) AS DisplayName
                             FROM AD_Column col
                             INNER JOIN AD_Table tbl ON (tbl.AD_Table_ID=col.AD_Table_ID)
                             INNER JOIN AD_Ref_List rl ON (rl.AD_Reference_ID=col.AD_Reference_Value_ID)
                             LEFT OUTER JOIN AD_Ref_List_Trl rlt ON (rlt.AD_Ref_List_ID=rl.AD_Ref_List_ID
                                                                     AND rlt.AD_Language=@Language
                                                                     AND rlt.IsActive='Y')
                            WHERE tbl.TableName=@TableName
                              AND col.ColumnName=@ColumnName
                              AND col.IsActive='Y'
                              AND rl.IsActive='Y'";

            try
            {
                /* Three binds, each occurring once, in the order they appear. */
                DataSet ds = DB.ExecuteDataset(sql, new SqlParameter[]
                {
                    new SqlParameter("@Language", ctx.GetAD_Language()),
                    new SqlParameter("@TableName", tableName),
                    new SqlParameter("@ColumnName", columnName)
                }, null);

                if (ds != null && ds.Tables.Count > 0)
                {
                    foreach (DataRow r in ds.Tables[0].Rows)
                    {
                        string code = Util.GetValueOfString(r["Value"]);
                        if (!string.IsNullOrEmpty(code) && !labels.ContainsKey(code))
                        {
                            labels[code] = Util.GetValueOfString(r["DisplayName"]);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Severe("VAS_292 LoadRefListLabels(" + tableName + "." + columnName + "): " + ex.Message);
            }

            return labels;
        }

        /// <summary>
        /// DB-specific row-limit suffix for server-side paging: Oracle uses
        /// OFFSET / FETCH, PostgreSQL LIMIT / OFFSET. page and pageSize are integers
        /// the server owns (no injection risk), so they are inlined - some engines
        /// reject a bound LIMIT.
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
                pageSize = LINES_PAGE_SIZE;
            }
            int offset = page * pageSize;
            if (DB.IsOracle())
            {
                return " OFFSET " + offset + " ROWS FETCH NEXT " + pageSize + " ROWS ONLY";
            }
            return " LIMIT " + pageSize + " OFFSET " + offset;
        }

        /// <summary>
        /// Resolves the translated display name of one List-reference value from
        /// AD_Ref_List / AD_Ref_List_Trl for the session language. The AD_Column is
        /// located by TableName + ColumnName - stable across environments - rather
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
                /* Four binds, each occurring once, in the order they appear. */
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
                _log.Severe("VAS_292 GetListReferenceName(" + tableName + "." + columnName + "): " + ex.Message);
                return code;
            }
        }

        /// <summary>
        /// True when the amount is zero within the currency's precision - half of
        /// the smallest representable unit (0.005 at two decimals). Decided on the
        /// decimal value, never on a formatted string.
        /// </summary>
        /// <param name="value">Amount to test.</param>
        /// <param name="precision">Currency standard precision.</param>
        /// <returns>True when |value| is within the tolerance.</returns>
        private static bool IsWithinTolerance(decimal value, int precision)
        {
            int p = (precision >= 0 && precision <= 10) ? precision : 2;
            decimal tolerance = 0.5m / (decimal)Math.Pow(10, p);
            return Math.Abs(value) <= tolerance;
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

        /// <summary>Formats a nullable timestamp as yyyy-MM-dd HH:mm (empty when
        /// null). The client reads it as UTC and shows it in the browser's zone.</summary>
        /// <param name="value">Timestamp to format.</param>
        /// <returns>Timestamp string, or "".</returns>
        private static string FormatStamp(DateTime? value)
        {
            return value.HasValue
                ? value.Value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
                : "";
        }

        // ----------------------------------------------------------------- //
        //  Payload                                                           //
        // ----------------------------------------------------------------- //

        /// <summary>Everything the panel renders for one banking journal.</summary>
        public class BankingJournalPanelData
        {
            public int C_BankStatement_ID { get; set; }
            public int AD_Org_ID { get; set; }
            public string OrgName { get; set; }
            public string DocumentNo { get; set; }
            /// <summary>ISO yyyy-MM-dd.</summary>
            public string StatementDate { get; set; }
            public string Name { get; set; }
            public string Description { get; set; }

            /// <summary>Stored DocStatus code and its translated name.</summary>
            public string DocStatus { get; set; }
            public string DocStatusName { get; set; }
            /// <summary>Stored Posted flag (Y / N).</summary>
            public string Posted { get; set; }
            public bool Processed { get; set; }
            public bool IsApproved { get; set; }
            public bool IsManual { get; set; }
            public bool MatchStatement { get; set; }

            /// <summary>Bank account currency - drives every header / summary amount.</summary>
            public int C_Currency_ID { get; set; }
            public string CurISO { get; set; }
            public string CurSymbol { get; set; }
            public int StdPrecision { get; set; }

            public decimal BeginningBalance { get; set; }
            public decimal EndingBalance { get; set; }
            public decimal StatementDifference { get; set; }
            /// <summary>EndingBalance - BeginningBalance.</summary>
            public decimal NetMovement { get; set; }
            /// <summary>|StatementDifference| within the currency precision tolerance.</summary>
            public bool IsBalanced { get; set; }

            public int C_BankAccount_ID { get; set; }
            public string BankAccountName { get; set; }
            /// <summary>Full account number - the CLIENT masks it to the last four.</summary>
            public string AccountNo { get; set; }
            public string IBAN { get; set; }
            /// <summary>Stored BankAccountType code and its translated name.</summary>
            public string BankAccountType { get; set; }
            public string BankAccountTypeName { get; set; }
            public decimal CurrentBalance { get; set; }
            public decimal UnMatchedBalance { get; set; }

            public int C_Bank_ID { get; set; }
            public string BankName { get; set; }
            public string SwiftCode { get; set; }
            public string RoutingNo { get; set; }

            /// <summary>Aggregate over EVERY active line.</summary>
            public int LineCount { get; set; }
            public int MatchedCount { get; set; }
            public int UnmatchedCount { get; set; }
            public decimal InflowAmount { get; set; }
            public decimal OutflowAmount { get; set; }
            public decimal ChargeAmount { get; set; }
            public decimal InterestAmount { get; set; }
            /// <summary>Rows per page the server pages with; Lines is page 0.</summary>
            public int LinesPageSize { get; set; }
            public List<JournalLineRow> Lines { get; set; }

            /// <summary>Actual posting from Fact_Acct; zero / empty when not posted.</summary>
            public int EntryCount { get; set; }
            public decimal TotalDebit { get; set; }
            public decimal TotalCredit { get; set; }
            /// <summary>|TotalDebit - TotalCredit| within the currency precision tolerance.</summary>
            public bool IsPostingBalanced { get; set; }
            public List<AccountRow> Accounts { get; set; }

            /// <summary>yyyy-MM-dd HH:mm, read as UTC by the client.</summary>
            public string Created { get; set; }
            public string CreatedByName { get; set; }
            public string Updated { get; set; }
            public string UpdatedByName { get; set; }
            /// <summary>Earliest Fact_Acct.Created for the record and its creator; "" when not posted.</summary>
            public string PostedOn { get; set; }
            public string PostedByName { get; set; }

            /// <summary>Workflow activities the Audit trail is composed from.</summary>
            public List<WorkflowStepRow> WorkflowSteps { get; set; }

            /// <summary>AD_Table_ID of C_BankStatement, for the accounting viewer.</summary>
            public int Table_ID { get { return MBankStatement.Table_ID; } }
            /// <summary>AD_Table_ID of C_BankStatementLine, so the panel can find the
            /// journal's line tab in its own window by table rather than by name.</summary>
            public int LineTable_ID { get { return MBankStatementLine.Table_ID; } }
        }

        /// <summary>One journal line for the "Journal lines" section.</summary>
        public class JournalLineRow
        {
            public int C_BankStatementLine_ID { get; set; }
            public int Line { get; set; }
            /// <summary>ISO yyyy-MM-dd.</summary>
            public string StatementLineDate { get; set; }
            public string ValutaDate { get; set; }
            public string DateAcct { get; set; }
            public string Description { get; set; }
            public string ReferenceNo { get; set; }

            /// <summary>Line currency; 0 / empty when the line carries none.</summary>
            public int C_Currency_ID { get; set; }
            public string CurISO { get; set; }
            public string CurSymbol { get; set; }
            public int StdPrecision { get; set; }

            public decimal StmtAmt { get; set; }
            public decimal TrxAmt { get; set; }
            public decimal ChargeAmt { get; set; }
            public decimal InterestAmt { get; set; }

            public bool IsMatched { get; set; }
            public bool Processed { get; set; }
            public bool IsManual { get; set; }
            public bool IsReversal { get; set; }

            public int C_Payment_ID { get; set; }
            public string PaymentDocumentNo { get; set; }
            public int C_BPartner_ID { get; set; }
            public string BPartnerName { get; set; }
            public int C_Charge_ID { get; set; }
            public string ChargeName { get; set; }
        }

        /// <summary>One ledger account of the Actual posting.</summary>
        public class AccountRow
        {
            public int Account_ID { get; set; }
            public string AccountValue { get; set; }
            public string AccountName { get; set; }
            public decimal DebitAmount { get; set; }
            public decimal CreditAmount { get; set; }
        }

        /// <summary>One page of journal lines, for the pager.</summary>
        public class JournalLinesPage
        {
            public int C_BankStatement_ID { get; set; }
            /// <summary>Zero-based page index actually served.</summary>
            public int Page { get; set; }
            public int PageSize { get; set; }
            public List<JournalLineRow> Rows { get; set; }
        }

        /// <summary>One workflow activity raised against the statement.</summary>
        public class WorkflowStepRow
        {
            public int AD_WF_Activity_ID { get; set; }
            public string NodeName { get; set; }
            /// <summary>AD_WF_Node.Action code: D = document action, C / W / X = user step.</summary>
            public string NodeAction { get; set; }
            /// <summary>AD_WF_Node.DocAction code for a document-action node (PR, CO, AP, ...).</summary>
            public string DocAction { get; set; }
            /// <summary>Stored WFState code (CC / OR / OS / CA / CT / ON) and its translated name.</summary>
            public string WFState { get; set; }
            public string WFStateName { get; set; }
            public string ActorName { get; set; }
            public string Created { get; set; }
            public string Updated { get; set; }
        }
    }
}
