/******************************************************
 * Module Name    : VASLogic
 * Purpose        : Bank Balance dashboard widget data
 * chronological  : Development
 * Created Date   : 2026-09-04
 * Created by     : VAI154
 ******************************************************/

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;

namespace VASLogic.Models
{
    /// <summary>
    /// Module Name : VAS_230_BankBalance
    /// Purpose     : Backs the VAS_230_BankBalanceWidget dashboard widget - ONE bank
    ///               account's current balance, and nothing else:
    ///
    ///                 Balance   the LATEST C_BankAccountLine.EndingBalance for the selected
    ///                           account, dated on or before the as-of date.
    ///                 Currency  the ACCOUNT's own C_Currency - symbol, ISO code and
    ///                           StdPrecision - so no tenant currency is ever assumed.
    ///                 Accounts  the active accounts the role may see, for the selector, each
    ///                           labelled "&lt;Bank&gt; · &lt;AccountNo&gt;" with the number IN
    ///                           FULL by explicit request - not masked, because the selector
    ///                           is where two accounts at one bank are told apart.
    ///
    ///               THE BALANCE SOURCE IS C_BankAccountLine.EndingBalance - deliberately not
    ///               C_BankAccount.CurrentBalance, not C_BankStatement.EndingBalance, not
    ///               payment totals and not Fact_Acct.
    ///
    ///               THE LATEST LINE, NEVER A SUM. C_BankAccountLine keeps a history row per
    ///               account, so adding them would add an account's past balances to its
    ///               present one. The read is ordered StatementDate DESC then
    ///               C_BankAccountLine_ID DESC (the id breaks a same-date tie
    ///               deterministically) and takes exactly ONE row - there is no aggregate
    ///               anywhere in this model.
    ///
    ///               NO COMPARISON, BY REQUIREMENT. No previous line is read and no change
    ///               amount, percentage or direction is computed or returned. The card shows
    ///               what the account stands at, full stop.
    ///
    ///               NO WINDOW FUNCTION. "Latest per account" would normally be ROW_NUMBER()
    ///               OVER (PARTITION BY ... ORDER BY ...), but that puts an ORDER BY inside
    ///               the SELECT list and MRole's AddAccessSQL parser has to be kept clear of
    ///               clauses of that kind. For ONE account it is not needed at all: an ORDER
    ///               BY with an ANSI FETCH FIRST clause - both appended AFTER the access SQL -
    ///               returns the single newest row directly.
    ///
    ///               THE SELECTED ACCOUNT IS VALIDATED, NOT TRUSTED. The browser sends an id;
    ///               it is honoured only if it appears in the MRole-filtered account list this
    ///               model reads for the selector. Anything else - an unknown id, another
    ///               tenant's id, an id the role cannot see - falls back to the first
    ///               accessible account rather than reaching the balance query. That check
    ///               costs no extra round trip: the list is read for the selector anyway.
    ///
    ///               MRole row-level security is applied to the main physical table of each
    ///               query - C_BankAccount ba for the selector, C_BankAccountLine bal for the
    ///               balance - and never to a joined reference table (C_Bank, C_Currency).
    ///               ORDER BY and the FETCH clause are appended AFTER AddAccessSQL so its
    ///               FROM-clause parser never meets a trailing clause, and every join ON is a
    ///               plain equality so it never meets a function call either. Compatible with
    ///               PostgreSQL and Oracle.
    /// Chronological development:
    ///   VAI154      2026-09-04 Created
    /// </summary>
    public class VAS_230_BankBalanceModel
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_230_BankBalanceModel).FullName);

        // ─────────────────────────────────────────────────────────────────────
        // §1  Entry point
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The selected account's latest balance, plus the accounts the selector can offer.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="bankAccountId">C_BankAccount_ID the client is asking for, or 0 for the
        /// first accessible account. Validated against the role's own account list before it
        /// reaches any balance query.</param>
        /// <returns>Populated <see cref="BankBalanceResult"/> (never null). Loaded is false
        /// only when there is no context; a tenant with no accessible bank account returns
        /// Loaded=true with NoAccounts, and an account with no balance line returns
        /// Loaded=true with HasBalance=false - both are real answers rather than errors.</returns>
        public BankBalanceResult GetBankBalance(Ctx ctx, int bankAccountId)
        {
            BankBalanceResult result = new BankBalanceResult();
            result.Accounts = new List<AccountOption>();

            if (ctx == null) { return result; }

            /* The Banking dashboard has no shared as-of date context, so the date is resolved
               HERE and never accepted from the browser. */
            DateTime asOf = DateTime.Now.Date;
            result.AsOfDate = asOf.ToString("yyyy-MM-dd");

            /* The selector's options travel with the balance, so the widget is one round trip
               per paint and the pill can never name an account the balance is not for. */
            List<AccountOption> accounts = GetBankAccounts(ctx);
            result.Accounts = accounts;

            if (accounts.Count == 0)
            {
                /* No accessible bank account at all. A real state of its own: there is nothing
                   to select and nothing to show a balance for. */
                result.NoAccounts = true;
                result.Loaded = true;
                return result;
            }

            AccountOption selected = ResolveAccount(accounts, bankAccountId);

            result.C_BankAccount_ID = selected.C_BankAccount_ID;
            result.BankName = selected.BankName;
            result.AccountName = selected.AccountName;
            result.AccountNo = selected.AccountNo;
            result.AccountLabel = selected.Name;
            result.CurrencyCode = selected.CurrencyCode;
            result.CurrencySymbol = selected.CurrencySymbol;
            result.Precision = selected.Precision;

            ApplyLatestBalance(ctx, selected.C_BankAccount_ID, asOf, result);

            result.Loaded = true;
            return result;
        }

        /// <summary>
        /// Picks the account the balance will be read for: the requested one when the role can
        /// actually see it, otherwise the first accessible account.
        ///
        /// This IS the server-side validation of the client's id - the list it is matched
        /// against is the MRole-filtered one - so no unvalidated id ever reaches the balance
        /// query, and changing the id in the request payload can only ever select another
        /// account the user was already entitled to.
        /// </summary>
        /// <param name="accounts">Accessible accounts, in display order.</param>
        /// <param name="bankAccountId">Requested id, or 0.</param>
        /// <returns>The account to report on; never null when the list is non-empty.</returns>
        private AccountOption ResolveAccount(List<AccountOption> accounts, int bankAccountId)
        {
            if (bankAccountId > 0)
            {
                for (int i = 0; i < accounts.Count; i++)
                {
                    if (accounts[i].C_BankAccount_ID == bankAccountId) { return accounts[i]; }
                }

                /* Requested but not accessible. Logged as information rather than a warning -
                   it is also what a stale dashboard shows after an account is deactivated. */
                Log.Log(Level.INFO, "VAS_230_BankBalance: C_BankAccount_ID=" + bankAccountId
                    + " is not accessible to this role; falling back to the first account");
            }

            /* The default: the first accessible account in the selector's own order, so the
               card opens on the same account the list opens on. No id is hard-coded. */
            return accounts[0];
        }

        // ─────────────────────────────────────────────────────────────────────
        // §2  The accounts the selector can offer
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The active bank accounts the role may see, each carrying its own currency's symbol,
        /// ISO code and StdPrecision - read HERE so the balance query never has to look up
        /// currency metadata a second time.
        ///
        /// The account NUMBER is read and shown IN FULL, by explicit request - it is what
        /// tells two accounts at the same bank apart, which is exactly the job of a selector.
        /// It is not masked.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <returns>Accounts ordered by bank then account name (never null).</returns>
        private List<AccountOption> GetBankAccounts(Ctx ctx)
        {
            List<AccountOption> options = new List<AccountOption>();

            /* C_Bank and C_Currency are display lookups; C_BankAccount ba is the main physical
               table. Both joins are INNER and safe: an account always has a bank and a
               currency. The closing ON is a plain equality so the access parser has nothing to
               trip on. */
            string sql = @"
                SELECT ba.C_BankAccount_ID AS C_BankAccount_ID,
                       ba.Name AS Account_Name,
                       ba.AccountNo AS Account_No,
                       b.Name AS Bank_Name,
                       cur.ISO_Code AS Currency_Iso,
                       cur.StdPrecision AS Std_Precision,
                       CASE WHEN cur.CurSymbol IS NOT NULL THEN cur.CurSymbol ELSE cur.ISO_Code END AS Currency_Symbol
                FROM C_BankAccount ba
                INNER JOIN C_Bank b ON (b.C_Bank_ID=ba.C_Bank_ID)
                INNER JOIN C_Currency cur ON (cur.C_Currency_ID=ba.C_Currency_ID)
                WHERE ba.IsActive='Y'
                  AND b.IsActive='Y'
                  AND cur.IsActive='Y'
                  AND ba.AD_Client_ID=@AD_Client_ID";

            /* MRole supplies the organisation access clause, so no AD_Org_ID predicate is
               written by hand - the explicit tenant filter is a second, independent guard
               rather than the only one. */
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "ba", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            /* ORDER BY goes on AFTER the access SQL. The id ends every ordering so two
               identically named accounts cannot swap places between two requests - which
               matters here because row 0 is also the default selection. */
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
                option.BankName = Util.GetValueOfString(row["Bank_Name"]);
                option.AccountName = Util.GetValueOfString(row["Account_Name"]);
                option.CurrencyCode = Util.GetValueOfString(row["Currency_Iso"]);
                option.CurrencySymbol = Util.GetValueOfString(row["Currency_Symbol"]);
                option.Precision = Util.GetValueOfInt(row["Std_Precision"]);

                /* Shown in full, by explicit request - not masked. */
                string accountNo = Util.GetValueOfString(row["Account_No"]);
                option.AccountNo = accountNo == null ? "" : accountNo.Trim();

                option.Name = BuildAccountLabel(option.BankName, option.AccountName, option.AccountNo);

                options.Add(option);
            }

            return options;
        }

        /// <summary>
        /// The account as the selector shows it - "HDFC · 12340000009032": the bank's name and
        /// the account NUMBER in full, joined by a middle dot.
        ///
        /// The number rather than the account's own name, by explicit request: two accounts at
        /// one bank can be named alike, and the selector is where they have to be told apart.
        /// The number is not masked - the same call the sibling card's account filter makes.
        /// The account's Name is still returned separately and carries the card's tooltip, so
        /// nothing is lost by leaving it out of the pill.
        ///
        /// Either half stands alone when the other is blank - an account with no number falls
        /// back to its Name - so a row is never nameless and two values are never concatenated
        /// around a null.
        /// </summary>
        /// <param name="bankName">C_Bank.Name.</param>
        /// <param name="accountName">C_BankAccount.Name - the fallback when there is no number.</param>
        /// <param name="accountNo">C_BankAccount.AccountNo, as stored.</param>
        /// <returns>Display string; may be empty.</returns>
        private string BuildAccountLabel(string bankName, string accountName, string accountNo)
        {
            bool hasBank = !String.IsNullOrEmpty(bankName);

            /* The number is what the label is FOR; the account's name only stands in when
               there is no number to show. */
            string tail = String.IsNullOrEmpty(accountNo) ? accountName : accountNo;
            bool hasTail = !String.IsNullOrEmpty(tail);

            if (hasBank && hasTail) { return bankName + " · " + tail; }
            if (hasBank) { return bankName; }
            return hasTail ? tail : "";
        }

        // ─────────────────────────────────────────────────────────────────────
        // §3  The balance
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Reads the ONE latest balance line for the selected account and writes its
        /// EndingBalance onto the result.
        ///
        /// Newest first, then exactly one row: no aggregate, no window function, and no second
        /// query for a previous balance. An account with no line on or before the as-of date
        /// leaves HasBalance false - the card then says so rather than printing a zero, because
        /// a zero balance is a real figure and would be read as one.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="bankAccountId">The validated account id.</param>
        /// <param name="asOf">The as-of date; bounds StatementDate exclusively at asOf + 1 day,
        /// which keeps a future-dated line from being treated as the current balance while
        /// still admitting one stamped today.</param>
        /// <param name="result">Result being filled.</param>
        private void ApplyLatestBalance(Ctx ctx, int bankAccountId, DateTime asOf, BankBalanceResult result)
        {
            string sql = @"
                SELECT bal.C_BankAccountLine_ID AS C_BankAccountLine_ID,
                       bal.StatementDate AS Statement_Date,
                       bal.EndingBalance AS Ending_Balance
                FROM C_BankAccountLine bal
                WHERE bal.IsActive='Y'
                  AND bal.AD_Client_ID=@AD_Client_ID
                  AND bal.C_BankAccount_ID=@C_BankAccount_ID
                  AND bal.StatementDate<@As_Of_Exclusive";

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "bal", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            /* ORDER BY and the paging clause go on AFTER the access SQL. Newest line first,
               with the id as a deterministic tiebreaker, and exactly one row returned.

               OFFSET / FETCH is ANSI and supported by PostgreSQL 12+ and Oracle 12c+ alike, so
               there is no dialect branch here, and the numbers are literals of this model's
               own - never client text. */
            sql += " ORDER BY bal.StatementDate DESC,bal.C_BankAccountLine_ID DESC"
                + " OFFSET 0 ROWS FETCH NEXT 1 ROWS ONLY";

            /* Positional binding: tenant, account, date - the order the placeholders appear in
               the text. */
            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@C_BankAccount_ID", bankAccountId),
                new SqlParameter("@As_Of_Exclusive", asOf.Date.AddDays(1))
            };

            DataSet ds = DB.ExecuteDataset(sql, parameters, null);
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) { return; }

            DataRow row = ds.Tables[0].Rows[0];

            result.EndingBalance = Util.GetValueOfDecimal(row["Ending_Balance"]);
            result.HasBalance = true;

            DateTime? statementDate = Util.GetValueOfDateTime(row["Statement_Date"]);
            if (statementDate.HasValue)
            {
                result.StatementDate = statementDate.Value.ToString("yyyy-MM-dd");
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // §4  Transfer objects
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>One paint of the widget: the selected account, its balance, and the
        /// accounts the selector can offer.</summary>
        public class BankBalanceResult
        {
            /// <summary>The accounts the selector can offer, in display order.</summary>
            public List<AccountOption> Accounts { get; set; }

            /// <summary>The account actually reported on, after validation. 0 when the tenant
            /// has no accessible account.</summary>
            public int C_BankAccount_ID { get; set; }

            /// <summary>C_Bank.Name of that account.</summary>
            public string BankName { get; set; }

            /// <summary>C_BankAccount.Name of that account.</summary>
            public string AccountName { get; set; }

            /// <summary>C_BankAccount.AccountNo IN FULL, by explicit request - not masked.</summary>
            public string AccountNo { get; set; }

            /// <summary>The selector's label for it - "HDFC · 12340000009032".</summary>
            public string AccountLabel { get; set; }

            /// <summary>The account currency's ISO code.</summary>
            public string CurrencyCode { get; set; }

            /// <summary>The account currency's display symbol, falling back to its ISO code.</summary>
            public string CurrencySymbol { get; set; }

            /// <summary>The account currency's C_Currency.StdPrecision.</summary>
            public int Precision { get; set; }

            /// <summary>The latest C_BankAccountLine.EndingBalance, at full stored precision.
            /// Meaningless unless HasBalance.</summary>
            public decimal EndingBalance { get; set; }

            /// <summary>False when the account has no balance line on or before the as-of date.
            /// The card then says so - it never prints a zero it did not read.</summary>
            public bool HasBalance { get; set; }

            /// <summary>StatementDate of the line the balance came from, as yyyy-MM-dd.</summary>
            public string StatementDate { get; set; }

            /// <summary>The date the balance was taken as of, as yyyy-MM-dd.</summary>
            public string AsOfDate { get; set; }

            /// <summary>True when the role can see no active bank account at all.</summary>
            public bool NoAccounts { get; set; }

            /// <summary>False only on a failure; a tenant with no accounts, or an account with
            /// no balance line, is Loaded=true with the matching flag above.</summary>
            public bool Loaded { get; set; }
        }

        /// <summary>One selectable bank account, carrying its own currency so the balance
        /// query never has to look currency metadata up again.</summary>
        public class AccountOption
        {
            /// <summary>C_BankAccount.C_BankAccount_ID.</summary>
            public int C_BankAccount_ID { get; set; }

            /// <summary>The selector's label - "HDFC · 12340000009032".</summary>
            public string Name { get; set; }

            /// <summary>C_Bank.Name on its own.</summary>
            public string BankName { get; set; }

            /// <summary>C_BankAccount.Name on its own - the pill's fallback when the account
            /// has no number, and part of the card's tooltip either way.</summary>
            public string AccountName { get; set; }

            /// <summary>C_BankAccount.AccountNo IN FULL, by explicit request - not masked.</summary>
            public string AccountNo { get; set; }

            /// <summary>The account currency's ISO code.</summary>
            public string CurrencyCode { get; set; }

            /// <summary>The account currency's display symbol, falling back to its ISO code.</summary>
            public string CurrencySymbol { get; set; }

            /// <summary>The account currency's C_Currency.StdPrecision.</summary>
            public int Precision { get; set; }
        }
    }
}
