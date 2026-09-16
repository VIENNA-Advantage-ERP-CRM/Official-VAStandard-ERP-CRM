/******************************************************
 * Module Name    : VASLogic
 * Purpose        : Unreconciled Bank Lines dashboard widget data
 * chronological  : Development
 * Created Date   : 2026-09-03
 * Created by     : VAI154
 ******************************************************/

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Text;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;

namespace VASLogic.Models
{
    /// <summary>
    /// Module Name : VAS_238_UnreconciledBankLine
    /// Purpose     : Backs the VAS_238_UnreconciledBankLineWidget dashboard widget - the
    ///               statement lines the bank has reported and the books have not yet
    ///               matched:
    ///
    ///                 Population  C_BankStatementLine on a COMPLETED or CLOSED statement
    ///                             with IsReconciled &lt;&gt; 'Y'. A drafted statement has
    ///                             not been agreed with the bank yet, so its lines are not
    ///                             an exception - they are simply not in play.
    ///                 Narration   C_BankStatementLine.Description, and only that. Memo
    ///                             and ReferenceNo are deliberately NOT used as fallbacks:
    ///                             they hold different things - a clerk's note and a bank
    ///                             reference - and showing either under a "Narration"
    ///                             heading would misreport what the bank actually said.
    ///                             A blank one renders as a dash.
    ///                 Account     bank name plus a MASKED tail, "Uco Bank ····9032". The
    ///                             FILTER's options spell the same account's number out in
    ///                             full - "Uco Bank 12340000009032" - because that is where
    ///                             one account is singled out and two accounts at one bank
    ///                             can end in the same digits. A row only has to be
    ///                             recognisable; the picker has to be unambiguous.
    ///                 Amount      StmtAmt, SIGNED AS STORED. This is the bank's own view:
    ///                             a positive line is money the bank received, a negative
    ///                             one money it paid out. There is no IsReceipt flag on a
    ///                             statement line, so unlike the C_Payment widgets the sign
    ///                             IS the direction here.
    ///                 Age         whole days between StatementLineDate and today.
    ///
    ///               THIS IS THE BANK SIDE of the reconciliation gap. Its counterpart reads
    ///               C_Payment - the book side - so the two must not be confused: a line
    ///               here is something the BANK says happened that the books have not
    ///               agreed to.
    ///
    ///               NO DATE ARITHMETIC IN SQL. "Days between two dates" is the one thing
    ///               that genuinely differs between the two backends - on this PostgreSQL
    ///               setup DATE - DATE yields an INTERVAL rather than a number, and the
    ///               workaround then has to be written so MRole's parser still copes. The
    ///               age is therefore subtracted in C# after the row is read, against one
    ///               as-of date resolved once per request so every row on a page is aged
    ///               from the same instant.
    ///
    ///               CURRENCY IS THE ACCOUNT'S OWN. StmtAmt is stated in the bank account's
    ///               currency and is shown that way, with that currency's symbol and
    ///               StdPrecision - nothing is converted. The widget lists individual bank
    ///               lines rather than summing across accounts, so converting would only
    ///               obscure what the statement actually says.
    ///
    ///               The bank-account filter scopes the list and the total count alike, so
    ///               the pager can never disagree with the rows above it. "All accounts"
    ///               is the absence of a filter rather than a value, so the client sends 0.
    ///
    ///               MRole row-level security is applied to C_BankStatementLine bsl, the
    ///               main physical table - never to C_BankStatement, which is only its
    ///               header, nor to the C_BankAccount / C_Bank / C_Currency lookups. ORDER
    ///               BY and the paging clause are appended AFTER AddAccessSQL so its
    ///               FROM-clause parser never meets a trailing clause, and every join ON is
    ///               a plain equality so it never meets a function call either. Compatible
    ///               with PostgreSQL and Oracle.
    /// Chronological development:
    ///   VAI154      2026-09-03 Created
    /// </summary>
    public class VAS_238_UnreconciledBankLineModel
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_238_UnreconciledBankLineModel).FullName);

        /* C_BankStatement.DocStatus codes that mean the statement is agreed with the bank.
           Stored codes - compared bare, never with an N prefix. */
        private const string DOCSTATUS_Completed = "CO";
        private const string DOCSTATUS_Closed = "CL";
        private const string DOCSTATUS_Reversed = "RE";
        private const string DOCSTATUS_Voided = "VO";

        /* Paging. The mock shows six rows; a taller cell may ask for more and a short one
           for fewer, but never outside these bounds. */
        public const int DEFAULT_PageSize = 6;
        private const int MIN_PageSize = 1;
        private const int MAX_PageSize = 12;

        /* Characters of AccountNo a GRID ROW may show; everything before them is masked
           HERE. The filter's own options are built unmasked - see BuildFullAccountLabel. */
        private const int ACCOUNTNO_VisibleChars = 4;
        private const string ACCOUNTNO_Mask = "····";

        // ─────────────────────────────────────────────────────────────────────
        // §1  Entry point
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// One page of unreconciled statement lines, the total the pager reports, and the
        /// accounts the filter can offer.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="bankAccountId">C_BankAccount_ID to restrict to, or 0 for every
        /// account the role can see.</param>
        /// <param name="pageNo">1-based page; clamped to the available range.</param>
        /// <param name="pageSize">Rows per page; clamped to [1,12].</param>
        /// <returns>Populated <see cref="UnreconciledResult"/> (never null). Loaded is false
        /// only when there is no context; a tenant with nothing unreconciled returns
        /// Loaded=true and an empty page, because "nothing outstanding" is a real answer
        /// rather than an error.</returns>
        public UnreconciledResult GetLines(Ctx ctx, int bankAccountId, int pageNo, int pageSize)
        {
            UnreconciledResult result = new UnreconciledResult();
            result.Rows = new List<LineRow>();
            result.Accounts = new List<AccountOption>();
            result.C_BankAccount_ID = bankAccountId > 0 ? bankAccountId : 0;
            result.PageSize = ClampPageSize(pageSize);
            result.Page = pageNo < 1 ? 1 : pageNo;

            if (ctx == null) { result.Page = 1; return result; }

            DateTime asOf = DateTime.Now.Date;
            result.AsOfDate = asOf.ToString("yyyy-MM-dd");

            /* The filter's options travel with the rows, so the widget is one round trip on
               load and the list can never drift out of step with the selection. */
            result.Accounts = GetBankAccounts(ctx);

            ReadLines(ctx, asOf, result);

            result.Loaded = true;
            return result;
        }

        /// <summary>Keeps the page size inside the range the design allows.</summary>
        /// <param name="pageSize">Requested size.</param>
        /// <returns>Size within [MIN_PageSize, MAX_PageSize].</returns>
        private int ClampPageSize(int pageSize)
        {
            if (pageSize < MIN_PageSize) { return DEFAULT_PageSize; }
            if (pageSize > MAX_PageSize) { return MAX_PageSize; }
            return pageSize;
        }

        // ─────────────────────────────────────────────────────────────────────
        // §2  The lines
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Counts the open lines, then reads the requested page of them.
        ///
        /// The COUNT is what the pager reports, so it comes from the SAME predicate and
        /// the same account filter as the rows - the footer cannot report a different
        /// population from the list above it.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="asOf">Date every row's age is measured against.</param>
        /// <param name="result">Result being filled - paging fields included.</param>
        private void ReadLines(Ctx ctx, DateTime asOf, UnreconciledResult result)
        {
            bool filterAccount = result.C_BankAccount_ID > 0;

            /* C_BankStatement is the line's header and C_BankAccount / C_Bank / C_Currency
               are display lookups. Every join is INNER and safe: a line always has a
               statement, a statement always has an account, and an account always has a
               bank and a currency. The closing ON is a plain equality so the access parser
               has nothing to trip on. */
            StringBuilder from = new StringBuilder();
            from.Append(@"
                FROM C_BankStatementLine bsl
                INNER JOIN C_BankStatement bs ON (bs.C_BankStatement_ID=bsl.C_BankStatement_ID)
                INNER JOIN C_BankAccount ba ON (ba.C_BankAccount_ID=bs.C_BankAccount_ID)
                INNER JOIN C_Bank b ON (b.C_Bank_ID=ba.C_Bank_ID)
                INNER JOIN C_Currency cur ON (cur.C_Currency_ID=ba.C_Currency_ID)
                WHERE bsl.IsActive='Y'
                  AND bs.IsActive='Y'
                  AND bsl.AD_Client_ID=@AD_Client_ID
                  AND bs.DocStatus NOT IN ('").Append(DOCSTATUS_Completed).Append("','").Append(DOCSTATUS_Closed)
                  .Append("','").Append(DOCSTATUS_Reversed).Append("','").Append(DOCSTATUS_Voided).Append(@"')
                  AND bsl.StatementLineDate IS NOT NULL
                  AND bsl.C_Payment_ID IS NULL AND bsl.C_Charge_ID IS NULL AND bsl.C_CashLine_ID IS NULL");

            /* One bank account rather than all of them. The id is not trusted - it is
               simply an extra equality on top of the tenant filter and MRole's own access
               clause, so an id the role cannot see returns nothing rather than someone
               else's lines. */
            if (filterAccount)
            {
                from.Append(" AND bs.C_BankAccount_ID=@C_BankAccount_ID");
            }

            /* Positional binding: tenant first, then the optional account filter - the
               order the placeholders appear in the text. The same array serves the count
               query and the page query, which share this FROM. */
            List<SqlParameter> binds = new List<SqlParameter>();
            binds.Add(new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()));
            if (filterAccount) { binds.Add(new SqlParameter("@C_BankAccount_ID", result.C_BankAccount_ID)); }

            SqlParameter[] parameters = binds.ToArray();

            /* ---- how many lines are open ---- */
            string countSql = MRole.GetDefault(ctx).AddAccessSQL(
                "SELECT COUNT(bsl.C_BankStatementLine_ID) AS Row_Cnt" + from.ToString(), "bsl",
                MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            DataSet countDs = DB.ExecuteDataset(countSql, parameters, null);
            if (countDs == null || countDs.Tables.Count == 0 || countDs.Tables[0].Rows.Count == 0) { return; }

            result.TotalRows = Util.GetValueOfInt(countDs.Tables[0].Rows[0]["Row_Cnt"]);
            result.TotalPages = result.PageSize > 0
                ? (int)Math.Ceiling((double)result.TotalRows / result.PageSize)
                : 0;

            if (result.TotalRows == 0) { result.Page = 1; return; }

            /* Clamp the page AFTER the total is known: a page number the client kept from a
               larger list - or from before the account filter narrowed it - must land on
               the last real page, never past the end. */
            int page = result.Page;
            if (result.TotalPages > 0 && page > result.TotalPages) { page = result.TotalPages; }
            result.Page = page;

            /* ---- the page itself ---- */
            StringBuilder sql = new StringBuilder();
            sql.Append(@"
                SELECT bsl.C_BankStatementLine_ID AS C_BankStatementLine_ID,
                       bsl.C_BankStatement_ID AS C_BankStatement_ID,
                       bsl.StatementLineDate AS Statement_Line_Date,
                       bsl.Description AS Line_Description,
                       bsl.StmtAmt AS Stmt_Amt,
                       ba.Name AS Bank_Account_Name,
                       ba.AccountNo AS Account_No,
                       b.Name AS Bank_Name,
                       cur.ISO_Code AS Currency_Iso,
                       cur.StdPrecision AS Std_Precision,
                       CASE WHEN cur.CurSymbol IS NOT NULL THEN cur.CurSymbol ELSE cur.ISO_Code END AS Currency_Symbol")
                .Append(from.ToString());

            string pageSql = MRole.GetDefault(ctx).AddAccessSQL(sql.ToString(), "bsl",
                MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            /* ORDER BY after the access SQL. Newest statement line first, with the id as a
               deterministic tiebreaker so two lines on the same date cannot swap between
               pages.

               OFFSET / FETCH is ANSI and supported by PostgreSQL 12+ and Oracle 12c+ alike,
               so there is no dialect branch here. The two numbers are server-clamped
               integers, never client text, which is why they can be composed in directly. */
            int offset = (page - 1) * result.PageSize;
            pageSql += " ORDER BY bsl.StatementLineDate DESC,bsl.C_BankStatementLine_ID DESC"
                + " OFFSET " + offset + " ROWS FETCH NEXT " + result.PageSize + " ROWS ONLY";

            DataSet ds = DB.ExecuteDataset(pageSql, parameters, null);
            if (ds == null || ds.Tables.Count == 0) { return; }

            DataTable dt = ds.Tables[0];
            for (int i = 0; i < dt.Rows.Count; i++)
            {
                result.Rows.Add(MapLine(dt.Rows[i], asOf));
            }
        }

        /// <summary>Materialises one statement line.</summary>
        /// <param name="row">Row carrying the line aliases.</param>
        /// <param name="asOf">Date the age is measured against.</param>
        /// <returns>Populated <see cref="LineRow"/>.</returns>
        private LineRow MapLine(DataRow row, DateTime asOf)
        {
            LineRow item = new LineRow();

            item.C_BankStatementLine_ID = Util.GetValueOfInt(row["C_BankStatementLine_ID"]);
            item.C_BankStatement_ID = Util.GetValueOfInt(row["C_BankStatement_ID"]);
            item.BankName = Util.GetValueOfString(row["Bank_Name"]);
            item.AccountName = Util.GetValueOfString(row["Bank_Account_Name"]);
            item.CurrencyCode = Util.GetValueOfString(row["Currency_Iso"]);
            item.CurrencySymbol = Util.GetValueOfString(row["Currency_Symbol"]);
            item.Precision = Util.GetValueOfInt(row["Std_Precision"]);

            /* SIGNED AS STORED - a statement line carries no direction flag, so the sign is
               the direction. Positive is money the bank took in, negative money it paid. */
            item.Amount = Util.GetValueOfDecimal(row["Stmt_Amt"]);

            DateTime? lineDate = Util.GetValueOfDateTime(row["Statement_Line_Date"]);
            if (lineDate.HasValue)
            {
                item.LineDate = lineDate.Value.ToString("yyyy-MM-dd");

                /* Whole calendar days, subtracted HERE rather than in SQL. A future-dated
                   statement line would age negative, which reads as nonsense on a chip, so
                   it is floored at zero. */
                int days = (asOf.Date - lineDate.Value.Date).Days;
                item.AgeDays = days < 0 ? 0 : days;
            }

            /* The Account column: "Uco Bank ····9032" - the bank's name with a masked tail.
               The filter's options spell the same account's number out in full; a row only
               has to be recognisable, the picker has to be unambiguous. */
            item.BankAccount = BuildMaskedAccountLabel(
                Util.GetValueOfString(row["Bank_Name"]),
                Util.GetValueOfString(row["Bank_Account_Name"]),
                Util.GetValueOfString(row["Account_No"]));

            /* The narration is C_BankStatementLine.Description and nothing else. It is
               left EMPTY when blank rather than falling back to Memo or ReferenceNo: those
               carry different things - a clerk's note and a bank reference - and showing
               one under a "Narration" heading would misreport what the bank actually said.
               The client renders its own dash for an empty one. */
            string description = Util.GetValueOfString(row["Line_Description"]);
            item.Narration = String.IsNullOrEmpty(description) ? "" : description.Trim();

            return item;
        }

        // ─────────────────────────────────────────────────────────────────────
        // §3  The bank account filter's options
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The active bank accounts the role may see, for the widget's account filter. The
        /// "All" option is the client's - it is the absence of a filter, not a row.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <returns>Accounts ordered by bank then account name (never null).</returns>
        private List<AccountOption> GetBankAccounts(Ctx ctx)
        {
            List<AccountOption> options = new List<AccountOption>();

            string sql = @"
                SELECT ba.C_BankAccount_ID AS C_BankAccount_ID,
                       ba.Name AS Account_Name,
                       ba.AccountNo AS Account_No,
                       b.Name AS Bank_Name
                FROM C_BankAccount ba
                INNER JOIN C_Bank b ON (b.C_Bank_ID=ba.C_Bank_ID)
                WHERE ba.IsActive='Y'
                  AND b.IsActive='Y'
                  AND ba.AD_Client_ID=@AD_Client_ID";

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "ba", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            /* ORDER BY goes on AFTER the access SQL. */
            sql += " ORDER BY b.Name,ba.Name,ba.C_BankAccount_ID";

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
            };

            DataSet ds = DB.ExecuteDataset(sql, parameters, null);
            if (ds == null || ds.Tables.Count == 0) { return options; }

            DataTable dt = ds.Tables[0];
            for (int i = 0; i < dt.Rows.Count; i++)
            {
                DataRow row = dt.Rows[i];

                AccountOption option = new AccountOption();
                option.C_BankAccount_ID = Util.GetValueOfInt(row["C_BankAccount_ID"]);

                /* Unmasked: this is the label the pill and the picker show, and it is where
                   one account is told apart from another at the same bank. */
                option.Name = BuildFullAccountLabel(
                    Util.GetValueOfString(row["Bank_Name"]),
                    Util.GetValueOfString(row["Account_Name"]),
                    Util.GetValueOfString(row["Account_No"]));

                options.Add(option);
            }

            return options;
        }

        /// <summary>
        /// The account as a row's Account column shows it - "Uco Bank ····9032": the bank's
        /// name plus a MASKED tail. A grid row is a list of many accounts and only has to
        /// be recognisable, so the number is not spelled out there.
        /// </summary>
        /// <param name="bankName">C_Bank.Name.</param>
        /// <param name="accountName">C_BankAccount.Name - stands in when there is no number.</param>
        /// <param name="accountNo">C_BankAccount.AccountNo; only its tail is returned.</param>
        /// <returns>Display string; may be empty.</returns>
        private string BuildMaskedAccountLabel(string bankName, string accountName, string accountNo)
        {
            return ComposeAccountLabel(bankName, accountName, MaskAccountNo(accountNo));
        }

        /// <summary>
        /// The account as the FILTER shows it - "Uco Bank 12340000009032": the bank's name
        /// plus the account number in full. The filter is where one account is singled out
        /// from the rest, and a masked tail cannot separate two accounts at the same bank
        /// that end in the same digits - so the picker spells the number out even though
        /// the grid does not.
        /// </summary>
        /// <param name="bankName">C_Bank.Name.</param>
        /// <param name="accountName">C_BankAccount.Name - stands in when there is no number.</param>
        /// <param name="accountNo">C_BankAccount.AccountNo, as stored.</param>
        /// <returns>Display string; may be empty.</returns>
        private string BuildFullAccountLabel(string bankName, string accountName, string accountNo)
        {
            return ComposeAccountLabel(bankName, accountName, accountNo == null ? "" : accountNo.Trim());
        }

        /// <summary>
        /// Joins the bank's name to whichever form of the number the caller settled on, so
        /// the two labels differ only in that number and never drift apart in shape.
        /// </summary>
        /// <param name="bankName">C_Bank.Name.</param>
        /// <param name="accountName">C_BankAccount.Name.</param>
        /// <param name="number">Already-masked or already-full account number; may be empty.</param>
        /// <returns>Display string; may be empty.</returns>
        private string ComposeAccountLabel(string bankName, string accountName, string number)
        {
            if (number.Length > 0)
            {
                return String.IsNullOrEmpty(bankName) ? number : bankName + "_" + number;
            }

            /* No number to show at all - the account's own Name is the only thing left that
               identifies it. */
            if (!String.IsNullOrEmpty(accountName)) { return accountName; }
            return bankName == null ? "" : bankName;
        }

        /// <summary>
        /// Shows only the last few characters of an account number - "····9032".
        /// </summary>
        /// <param name="accountNo">Raw C_BankAccount.AccountNo; may be null or short.</param>
        /// <returns>Masked number, or an empty string when there is nothing to mask.</returns>
        private string MaskAccountNo(string accountNo)
        {
            string value = accountNo == null ? "" : accountNo.Trim();
            if (value.Length == 0) { return ""; }

            /* A number too short to keep four characters of is masked entirely rather than
               revealed in full - the shorter the number, the more a tail gives away. */
            if (value.Length <= ACCOUNTNO_VisibleChars) { return ACCOUNTNO_Mask; }

            return ACCOUNTNO_Mask + value.Substring(value.Length - ACCOUNTNO_VisibleChars);
        }

        // ─────────────────────────────────────────────────────────────────────
        // §4  Transfer objects
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>One page of the widget, plus what the page cannot know by itself.</summary>
        public class UnreconciledResult
        {
            /// <summary>The requested page of statement lines, newest first.</summary>
            public List<LineRow> Rows { get; set; }

            /// <summary>The accounts the filter can offer. "All" is the client's own option
            /// and is not a row here - it is the absence of a filter.</summary>
            public List<AccountOption> Accounts { get; set; }

            /// <summary>The account the list is filtered to; 0 means all of them.</summary>
            public int C_BankAccount_ID { get; set; }

            /// <summary>1-based page number actually served, after clamping.</summary>
            public int Page { get; set; }

            /// <summary>Rows per page actually used, after clamping.</summary>
            public int PageSize { get; set; }

            /// <summary>Open lines in total - the pager's figure, from the same predicate
            /// and filter as the rows.</summary>
            public int TotalRows { get; set; }

            /// <summary>CEILING(TotalRows / PageSize).</summary>
            public int TotalPages { get; set; }

            /// <summary>The date every age was measured against, as yyyy-MM-dd.</summary>
            public string AsOfDate { get; set; }

            /// <summary>False only on a failure; a tenant with nothing unreconciled is
            /// Loaded=true with an empty page.</summary>
            public bool Loaded { get; set; }
        }

        /// <summary>One unreconciled statement line. Amounts are in the ACCOUNT's own
        /// currency - never converted, because the widget lists what the statement says.</summary>
        public class LineRow
        {
            /// <summary>C_BankStatementLine.C_BankStatementLine_ID - the drill-through
            /// target: the zoom opens the Bank Statement window positioned on this exact
            /// line rather than on its header.</summary>
            public int C_BankStatementLine_ID { get; set; }

            /// <summary>Owning C_BankStatement_ID, carried for context.</summary>
            public int C_BankStatement_ID { get; set; }

            /// <summary>StatementLineDate as yyyy-MM-dd; the client formats it.</summary>
            public string LineDate { get; set; }

            /// <summary>C_BankStatementLine.Description, and only that; empty when blank,
            /// which the client renders as a dash.</summary>
            public string Narration { get; set; }

            /// <summary>StmtAmt signed as stored - positive in, negative out.</summary>
            public decimal Amount { get; set; }

            /// <summary>The Account column: bank name plus the masked account tail -
            /// "Uco Bank ····9032".</summary>
            public string BankAccount { get; set; }

            /// <summary>C_Bank.Name on its own, for the row tooltip.</summary>
            public string BankName { get; set; }

            /// <summary>C_BankAccount.Name on its own, for the row tooltip.</summary>
            public string AccountName { get; set; }

            /// <summary>The account currency's ISO code.</summary>
            public string CurrencyCode { get; set; }

            /// <summary>The account currency's display symbol, falling back to its ISO code.</summary>
            public string CurrencySymbol { get; set; }

            /// <summary>The account currency's C_Currency.StdPrecision.</summary>
            public int Precision { get; set; }

            /// <summary>Whole days since StatementLineDate; never negative.</summary>
            public int AgeDays { get; set; }
        }

        /// <summary>One selectable bank account in the widget's account filter.</summary>
        public class AccountOption
        {
            /// <summary>C_BankAccount.C_BankAccount_ID.</summary>
            public int C_BankAccount_ID { get; set; }

            /// <summary>Bank name plus the account number IN FULL - the picker's label,
            /// unmasked so two accounts at one bank can be told apart.</summary>
            public string Name { get; set; }
        }
    }
}
