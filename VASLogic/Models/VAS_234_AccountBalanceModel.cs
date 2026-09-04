/******************************************************
 * Module Name    : VASLogic
 * Purpose        : Account-wise Balance dashboard widget data
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
    /// Module Name : VAS_234_AccountBalance
    /// Purpose     : Backs the VAS_234_AccountBalanceWidget dashboard widget - one row per
    ///               active bank account, showing what moved through it this reporting
    ///               period and what it closed at:
    ///
    ///                 Inflow    SUM(ABS(PayAmt)) WHERE IsReceipt='Y', DocStatus IN
    ///                           ('CO','CL'), DateAcct inside the current period.
    ///                 Outflow   the same for IsReceipt='N', always a POSITIVE magnitude -
    ///                           the direction is carried by which side of the bar's axis
    ///                           it is drawn on, never by a minus sign.
    ///                 Net       Inflow - Outflow, the figure over the bar's axis.
    ///                 Closing   EndingBalance of the LATEST settled statement dated before
    ///                           the period end, falling back to C_BankAccount.CurrentBalance
    ///                           when the account has no statement at all.
    ///
    ///               There is deliberately NO comparison against a previous period. The card
    ///               reports what this period did and where the account stands; no figure on
    ///               it is measured against a prior balance, so no previous period is read,
    ///               named or returned.
    ///
    ///               PERIOD. The Banking dashboard's period context is the tenant's
    ///               accounting calendar, not the calendar month - the sibling cards
    ///               (VAS_231 Net Movement, VAS_233 Bank Charges) already read it that way,
    ///               and a figure on one card must not silently cover a different window
    ///               from a figure on another. The period is the one containing today on
    ///               AD_ClientInfo.C_Calendar_ID, falling back to the most recently started
    ///               one. Its name is returned for the subtitle, so no label is ever
    ///               hard-coded.
    ///
    ///               ACCOUNT NUMBER. C_BankAccount.AccountNo is returned and displayed IN
    ///               FULL, by explicit request. It is not masked.
    ///
    ///               CURRENCY. Every figure in a row is stated in THAT ACCOUNT's currency,
    ///               which is what makes the row internally comparable. Statement balances
    ///               are already in it. Payments are not necessarily, so PayAmt goes
    ///               through the currencyConvert(...) DB function into the account's
    ///               currency, dated on the payment's own DateAcct and using its own
    ///               conversion type - amounts in different currencies are never summed
    ///               raw. Each row also carries its own symbol, ISO code and StdPrecision,
    ///               so the client never assumes a tenant currency.
    ///
    ///               SORT is a server-side WHITELIST. The client sends a key, never an
    ///               expression, and an unknown key falls back to the default rather than
    ///               reaching any query. Default is Net variance = ABS(Net) descending, so
    ///               the accounts that moved most are surfaced first.
    ///
    ///               PAGING. The client is served ONE page, with the totals and the bar
    ///               scale it needs to render it. Both of those are properties of the whole
    ///               accessible set rather than of one page: the bar must stay comparable
    ///               across pages, so MaxFlow is the largest single gross flow anywhere in
    ///               the set. That is why the accessible accounts are resolved in full and
    ///               then sliced here rather than paged in SQL - the set is bounded by the
    ///               tenant's bank accounts (tens, not millions), and paging in SQL would
    ///               cost an extra COUNT query and an extra MAX query to recover the two
    ///               values the page cannot know about itself.
    ///
    ///               No window functions anywhere: ROW_NUMBER() OVER (... ORDER BY ...)
    ///               puts an ORDER BY inside the SELECT list, and every clause of that kind
    ///               has to stay clear of AddAccessSQL's parser. The current and previous
    ///               statement per account therefore come from ONE date-ordered read and a
    ///               single forward pass. On a tenant with a very long statement history
    ///               this is the query to revisit first; it is bounded here to statements
    ///               dated before the current period end.
    ///
    ///               MRole row-level security is applied to the main physical table of every
    ///               query - C_Period p, C_BankAccount ba, C_Payment p, C_BankStatement bs -
    ///               and never to a joined reference table (C_Bank, C_Currency, C_Year,
    ///               AD_ClientInfo). GROUP BY and ORDER BY are appended AFTER AddAccessSQL
    ///               so its FROM-clause parser never meets a trailing clause, and every join
    ///               ON is a plain equality so it never meets a function call either.
    ///               Compatible with PostgreSQL and Oracle.
    /// Chronological development:
    ///   VAI154      2026-09-03 Created
    /// </summary>
    public class VAS_234_AccountBalanceModel
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_234_AccountBalanceModel).FullName);

        /* DocStatus codes that mean the document actually settled. Stored codes -
           compared bare, never with an N prefix. */
        private const string DOCSTATUS_Completed = "CO";
        private const string DOCSTATUS_Closed = "CL";

        /* C_Payment.IsReceipt stored codes. */
        private const string ISRECEIPT_Yes = "Y";
        private const string ISRECEIPT_No = "N";

        /* Paging. The specification fixes the page at five rows; the client may ask for
           fewer when its cell genuinely cannot hold five without scrolling, which the
           design forbids. It may never ask for more. */
        public const int DEFAULT_PageSize = 5;
        private const int MIN_PageSize = 1;
        private const int MAX_PageSize = 5;

        /* Sort whitelist. The client sends one of these keys and NOTHING else ever
           reaches an ORDER BY - there is no free-form sort text anywhere in this model. */
        public const string SORT_NetVariance = "netVariance";
        public const string SORT_ClosingBalance = "closingBalance";
        public const string SORT_Inflow = "inflow";
        public const string SORT_Outflow = "outflow";
        public const string SORT_AccountName = "accountName";

        /* Each side of the diverging bar may occupy at most half its track. */
        private const decimal BAR_HalfWidthPct = 50m;

        // ─────────────────────────────────────────────────────────────────────
        // §1  Entry point
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Everything the widget needs for one paint: the period labels, the requested
        /// page of account rows, the paging totals and the shared bar scale.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="pageNo">1-based page the client is asking for; clamped to the
        /// available range so a stale page number can never return nothing.</param>
        /// <param name="pageSize">Rows per page; clamped to [1,5].</param>
        /// <param name="sortKey">One of the SORT_* keys; anything unrecognised falls back
        /// to the default rather than reaching a query.</param>
        /// <returns>Populated <see cref="AccountBalanceResult"/> (never null). Loaded is
        /// false only when there is no context or no accounting period; a tenant with no
        /// accessible bank accounts returns Loaded=true and an empty page, because "no
        /// accounts" is a real answer rather than an error.</returns>
        public AccountBalanceResult GetAccountBalances(Ctx ctx, int pageNo, int pageSize, string sortKey)
        {
            AccountBalanceResult result = new AccountBalanceResult();
            result.Rows = new List<AccountRow>();
            result.Sort = NormalizeSort(sortKey);
            result.PageSize = ClampPageSize(pageSize);
            result.Page = 1;

            if (ctx == null) { return result; }

            PeriodWindow period = GetPeriodWindow(ctx, DateTime.Now.Date);
            if (period == null)
            {
                Log.Log(Level.WARNING, "VAS_234_AccountBalance: no started period on the primary calendar for AD_Client_ID="
                    + ctx.GetAD_Client_ID());
                return result;
            }

            result.CurrentPeriodLabel = period.CurrentName;

            List<AccountRow> rows = GetAccounts(ctx);
            if (rows.Count > 0)
            {
                ApplyPaymentFlows(ctx, period, rows);
                ApplyStatementBalances(ctx, period, rows);
                Finalise(rows);
            }

            /* MaxFlow and the totals belong to the WHOLE accessible set, so they are taken
               before the page is cut - a bar on page 3 has to be readable against a bar on
               page 1. */
            result.MaxFlow = MaxFlow(rows);
            ApplyBarPercents(rows, result.MaxFlow);

            SortRows(rows, result.Sort);

            result.TotalRows = rows.Count;
            result.TotalPages = result.PageSize > 0
                ? (int)Math.Ceiling((double)rows.Count / result.PageSize)
                : 0;

            /* Clamp the page AFTER the total is known: a page number the client kept from
               a larger set must land on the last real page, never past the end. */
            int page = pageNo < 1 ? 1 : pageNo;
            if (result.TotalPages > 0 && page > result.TotalPages) { page = result.TotalPages; }
            result.Page = page;

            int start = (page - 1) * result.PageSize;
            for (int i = start; i < rows.Count && i < start + result.PageSize; i++)
            {
                result.Rows.Add(rows[i]);
            }

            result.Loaded = true;
            return result;
        }

        // ─────────────────────────────────────────────────────────────────────
        // §2  Sort and paging guards
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Maps a client-supplied sort key onto the whitelist. Anything unrecognised -
        /// including null, empty and any attempt at SQL - becomes the default.
        /// </summary>
        /// <param name="sortKey">Raw key from the request.</param>
        /// <returns>One of the SORT_* constants.</returns>
        private string NormalizeSort(string sortKey)
        {
            if (String.IsNullOrEmpty(sortKey)) { return SORT_NetVariance; }

            if (String.Equals(sortKey, SORT_ClosingBalance, StringComparison.OrdinalIgnoreCase)) { return SORT_ClosingBalance; }
            if (String.Equals(sortKey, SORT_Inflow, StringComparison.OrdinalIgnoreCase)) { return SORT_Inflow; }
            if (String.Equals(sortKey, SORT_Outflow, StringComparison.OrdinalIgnoreCase)) { return SORT_Outflow; }
            if (String.Equals(sortKey, SORT_AccountName, StringComparison.OrdinalIgnoreCase)) { return SORT_AccountName; }

            return SORT_NetVariance;
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

        /// <summary>
        /// Orders the whole set by the chosen whitelist key. Every ordering ends on
        /// C_BankAccount_ID so equal rows cannot reshuffle between two page requests.
        /// </summary>
        /// <param name="rows">Rows to order in place.</param>
        /// <param name="sort">Normalised sort key.</param>
        private void SortRows(List<AccountRow> rows, string sort)
        {
            rows.Sort(delegate (AccountRow a, AccountRow b)
            {
                int cmp = 0;

                if (sort == SORT_ClosingBalance) { cmp = b.ClosingBalance.CompareTo(a.ClosingBalance); }
                else if (sort == SORT_Inflow) { cmp = b.Inflow.CompareTo(a.Inflow); }
                else if (sort == SORT_Outflow) { cmp = b.Outflow.CompareTo(a.Outflow); }
                else if (sort == SORT_AccountName)
                {
                    cmp = String.Compare(a.AccountName, b.AccountName, StringComparison.CurrentCultureIgnoreCase);
                }
                else
                {
                    /* Net VARIANCE is the size of the movement, not its direction - an
                       account that paid out heavily is as interesting as one that took in
                       heavily, so the magnitude sorts and the sign does not. */
                    cmp = Math.Abs(b.Net).CompareTo(Math.Abs(a.Net));
                }

                return cmp != 0 ? cmp : a.BankAccountId.CompareTo(b.BankAccountId);
            });
        }

        // ─────────────────────────────────────────────────────────────────────
        // §3  Period
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The reporting period on the tenant's primary accounting calendar: the period
        /// containing today, or - when today falls in a gap or past the last defined
        /// period - the most recently started one.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="today">Current application date (date part only).</param>
        /// <returns>Populated <see cref="PeriodWindow"/>, or null when the tenant has no
        /// calendar or no started period.</returns>
        private PeriodWindow GetPeriodWindow(Ctx ctx, DateTime today)
        {
            /* Every join ON here is a plain equality: no function call, no nested
               parenthesis. AccessSqlParser strips the LAST ON at the first ')' it finds,
               so a COALESCE / CAST in the closing join would break the access SQL. */
            string sql = @"
                SELECT p.C_Period_ID AS C_Period_ID,
                       p.Name AS Period_Name,
                       p.StartDate AS Start_Date,
                       p.EndDate AS End_Date
                FROM C_Period p
                INNER JOIN C_Year y ON (y.C_Year_ID=p.C_Year_ID)
                INNER JOIN AD_ClientInfo ci ON (ci.C_Calendar_ID=y.C_Calendar_ID AND ci.AD_Client_ID=@AD_Client_ID)
                WHERE p.IsActive='Y'
                  AND y.IsActive='Y'
                  AND p.AD_Client_ID=@AD_Client_ID_Period
                  AND p.StartDate<=@Today";

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "p", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            /* ORDER BY goes on AFTER the access SQL. Newest started period first, so row 0
               is the current period. */
            sql += " ORDER BY p.StartDate DESC,p.EndDate DESC,p.C_Period_ID DESC";

            /* The provider binds POSITIONALLY, so the client id appears under two distinct
               names, added in the order their placeholders appear in the text. */
            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@AD_Client_ID_Period", ctx.GetAD_Client_ID()),
                new SqlParameter("@Today", today)
            };

            DataSet ds = DB.ExecuteDataset(sql, parameters, null);
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) { return null; }

            DataRow current = ds.Tables[0].Rows[0];

            DateTime? start = Util.GetValueOfDateTime(current["Start_Date"]);
            DateTime? end = Util.GetValueOfDateTime(current["End_Date"]);
            if (!start.HasValue || !end.HasValue) { return null; }

            PeriodWindow window = new PeriodWindow();
            window.CurrentName = Util.GetValueOfString(current["Period_Name"]);
            window.CurrentStart = start.Value.Date;
            /* Exclusive upper bound: a document stamped on the last day of the period at
               14:30 is still inside it, and no time-of-day comparison is ever needed. */
            window.CurrentEndExclusive = end.Value.Date.AddDays(1);

            return window;
        }

        // ─────────────────────────────────────────────────────────────────────
        // §4  The accounts
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// One row per active bank account the role may see, carrying its display name,
        /// full account number and its OWN currency. Flows and balances are filled in
        /// afterwards.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <returns>Account rows, unsorted (never null).</returns>
        private List<AccountRow> GetAccounts(Ctx ctx)
        {
            List<AccountRow> rows = new List<AccountRow>();

            /* C_Bank and C_Currency are reference lookups; C_BankAccount ba is the main
               physical table the user is reading from. The closing join ON is a plain
               equality so the access parser has nothing to trip on.

               BankAccountType is deliberately not read: the account's own Name is the
               label, and C_Bank.Name alone stands in when that is blank. */
            string sql = @"
                SELECT ba.C_BankAccount_ID AS C_BankAccount_ID,
                       ba.Name AS Account_Name,
                       ba.AccountNo AS Account_No,
                       ba.CurrentBalance AS Current_Balance,
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

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "ba", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
            };

            DataSet ds = DB.ExecuteDataset(sql, parameters, null);
            if (ds == null || ds.Tables.Count == 0) { return rows; }

            DataTable dt = ds.Tables[0];
            for (int i = 0; i < dt.Rows.Count; i++)
            {
                DataRow row = dt.Rows[i];

                AccountRow item = new AccountRow();
                item.BankAccountId = Util.GetValueOfInt(row["C_BankAccount_ID"]);
                item.BankName = Util.GetValueOfString(row["Bank_Name"]);
                item.CurrencyCode = Util.GetValueOfString(row["Currency_Iso"]);
                item.CurrencySymbol = Util.GetValueOfString(row["Currency_Symbol"]);
                item.Precision = Util.GetValueOfInt(row["Std_Precision"]);

                /* Shown in full, by explicit request - not masked. */
                string accountNo = Util.GetValueOfString(row["Account_No"]);
                item.AccountNo = accountNo == null ? "" : accountNo.Trim();

                /* No statement anywhere for this account means the closing balance falls
                   back to the account's own running balance - set here so the statement
                   pass only ever has to overwrite it. */
                item.ClosingBalance = Util.GetValueOfDecimal(row["Current_Balance"]);

                /* The account's own name is the label; the bank's name stands in only when
                   that is blank, so a row is never nameless and two values are never
                   concatenated around a null. */
                string accountName = Util.GetValueOfString(row["Account_Name"]);
                item.AccountName = String.IsNullOrEmpty(accountName) ? item.BankName : accountName;

                rows.Add(item);
            }

            return rows;
        }

        // ─────────────────────────────────────────────────────────────────────
        // §5  Flows
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Fills Inflow and Outflow from the settled payments booked against each bank
        /// account inside the current period.
        ///
        /// Both sides are summed as POSITIVE magnitudes: the direction is carried by which
        /// side of the bar's axis the figure is drawn on, so a negative outgoing amount in
        /// the accounting data must not flip the bar back to the left.
        ///
        /// PayAmt is stated in the PAYMENT's currency, which need not be the bank account's,
        /// so it is converted with currencyConvert(...) into the ACCOUNT's currency - dated
        /// on the payment's own DateAcct and using its own conversion type. Amounts in
        /// different currencies are never summed raw.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="period">Resolved period, supplying the date bounds.</param>
        /// <param name="rows">Account rows to fill, keyed by C_BankAccount_ID.</param>
        private void ApplyPaymentFlows(Ctx ctx, PeriodWindow period, List<AccountRow> rows)
        {
            Dictionary<int, AccountRow> byAccount = IndexByAccount(rows);

            /* The conversion call appears twice and the provider binds POSITIONALLY, so
               nothing inside it is bound - every argument is a column. */
            string convert = "ABS(currencyConvert(p.PayAmt,p.C_Currency_ID,ba.C_Currency_ID,p.DateAcct,p.C_ConversionType_ID,p.AD_Client_ID,p.AD_Org_ID))";

            StringBuilder sql = new StringBuilder();
            sql.Append(@"
                SELECT p.C_BankAccount_ID AS C_BankAccount_ID,
                       COALESCE(SUM(CASE WHEN p.IsReceipt='").Append(ISRECEIPT_Yes).Append("' THEN ").Append(convert).Append(@" ELSE 0 END),0) AS Inflow_Amt,
                       COALESCE(SUM(CASE WHEN p.IsReceipt='").Append(ISRECEIPT_No).Append("' THEN ").Append(convert).Append(@" ELSE 0 END),0) AS Outflow_Amt,
                       COALESCE(SUM(CASE WHEN p.IsReceipt='").Append(ISRECEIPT_Yes).Append(@"' THEN 1 ELSE 0 END),0) AS Inflow_Cnt,
                       COALESCE(SUM(CASE WHEN p.IsReceipt='").Append(ISRECEIPT_No).Append(@"' THEN 1 ELSE 0 END),0) AS Outflow_Cnt
                FROM C_Payment p
                INNER JOIN C_BankAccount ba ON (ba.C_BankAccount_ID=p.C_BankAccount_ID)
                WHERE p.IsActive='Y'
                  AND ba.IsActive='Y'
                  AND p.AD_Client_ID=@AD_Client_ID
                  AND p.DocStatus IN ('").Append(DOCSTATUS_Completed).Append("','").Append(DOCSTATUS_Closed).Append(@"')
                  AND p.DateAcct>=@Date_From
                  AND p.DateAcct<@Date_To_Exclusive");

            /* C_Payment p is the main physical table; C_BankAccount ba is joined only to
               reach the account's currency. MRole supplies the organisation access clause,
               so no AD_Org_ID predicate is written by hand - the explicit tenant filter is
               a second, independent guard rather than the only one. Flat SUM(CASE ...)
               aggregation, never nested selects, so the access parser has one simple FROM
               clause to read. */
            string finalSql = MRole.GetDefault(ctx).AddAccessSQL(sql.ToString(), "p",
                MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            /* GROUP BY goes on AFTER the access SQL, for the same reason ORDER BY does. */
            finalSql += " GROUP BY p.C_BankAccount_ID";

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@Date_From", period.CurrentStart),
                new SqlParameter("@Date_To_Exclusive", period.CurrentEndExclusive)
            };

            DataSet ds = DB.ExecuteDataset(finalSql, parameters, null);
            if (ds == null || ds.Tables.Count == 0) { return; }

            DataTable dt = ds.Tables[0];
            for (int i = 0; i < dt.Rows.Count; i++)
            {
                DataRow row = dt.Rows[i];

                int accountId = Util.GetValueOfInt(row["C_BankAccount_ID"]);
                if (!byAccount.ContainsKey(accountId)) { continue; }

                AccountRow item = byAccount[accountId];
                item.Inflow = Util.GetValueOfDecimal(row["Inflow_Amt"]);
                item.Outflow = Util.GetValueOfDecimal(row["Outflow_Amt"]);
                item.InflowCount = Util.GetValueOfInt(row["Inflow_Cnt"]);
                item.OutflowCount = Util.GetValueOfInt(row["Outflow_Cnt"]);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // §6  Balances
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Fills ClosingBalance from C_BankStatement: the EndingBalance of the account's
        /// latest settled statement dated before the period ends. Rows arrive oldest-first,
        /// so a single forward pass ends up holding the newest one per account.
        ///
        /// EndingBalance is already stated in the bank account's own currency, which is the
        /// currency the whole row is stated in, so no conversion applies here.
        ///
        /// An account with no settled statement at all keeps the C_BankAccount.CurrentBalance
        /// fallback set when the row was built.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="period">Resolved period, supplying the cut-off.</param>
        /// <param name="rows">Account rows to fill, keyed by C_BankAccount_ID.</param>
        private void ApplyStatementBalances(Ctx ctx, PeriodWindow period, List<AccountRow> rows)
        {
            Dictionary<int, AccountRow> byAccount = IndexByAccount(rows);

            StringBuilder sql = new StringBuilder();
            sql.Append(@"
                SELECT bs.C_BankAccount_ID AS C_BankAccount_ID,
                       bs.EndingBalance AS Ending_Balance
                FROM C_BankStatement bs
                WHERE bs.IsActive='Y'
                  AND bs.AD_Client_ID=@AD_Client_ID
                  AND bs.DocStatus IN ('").Append(DOCSTATUS_Completed).Append("','").Append(DOCSTATUS_Closed).Append(@"')
                  AND bs.StatementDate<@Period_End_Exclusive");

            string finalSql = MRole.GetDefault(ctx).AddAccessSQL(sql.ToString(), "bs",
                MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            /* ORDER BY after the access SQL. Oldest first per account, so the forward pass
               below can simply overwrite and end up holding the newest one. */
            finalSql += " ORDER BY bs.C_BankAccount_ID,bs.StatementDate,bs.C_BankStatement_ID";

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@Period_End_Exclusive", period.CurrentEndExclusive)
            };

            DataSet ds = DB.ExecuteDataset(finalSql, parameters, null);
            if (ds == null || ds.Tables.Count == 0) { return; }

            DataTable dt = ds.Tables[0];
            for (int i = 0; i < dt.Rows.Count; i++)
            {
                DataRow row = dt.Rows[i];

                int accountId = Util.GetValueOfInt(row["C_BankAccount_ID"]);
                if (!byAccount.ContainsKey(accountId)) { continue; }

                AccountRow item = byAccount[accountId];

                /* Every row is below the period end, so the last one seen is the closing
                   balance - the fallback set at build time is overwritten from here on. */
                item.ClosingBalance = Util.GetValueOfDecimal(row["Ending_Balance"]);
                item.HasStatement = true;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // §7  Derived figures
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Computes the per-row figures that depend on the reads above: the net movement
        /// and whether anything moved at all.
        /// </summary>
        /// <param name="rows">Rows to complete in place.</param>
        private void Finalise(List<AccountRow> rows)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                AccountRow row = rows[i];

                row.Net = row.Inflow - row.Outflow;

                /* "No activity" is about the FLOWS only. An account can be perfectly still
                   all period and still hold a large balance, so this must never be confused
                   with a zero closing balance. */
                row.HasActivity = row.Inflow != 0 || row.Outflow != 0;
            }
        }

        /// <summary>
        /// The largest single gross flow - in or out - anywhere in the accessible set. This
        /// is the ONE scale every bar on every page is drawn against, which is what makes
        /// two bars comparable whether or not they are on the same page.
        /// </summary>
        /// <param name="rows">The whole accessible set.</param>
        /// <returns>The maximum, or 0 when nothing moved at all.</returns>
        private decimal MaxFlow(List<AccountRow> rows)
        {
            decimal max = 0m;
            for (int i = 0; i < rows.Count; i++)
            {
                decimal outflow = Math.Abs(rows[i].Outflow);
                decimal inflow = Math.Abs(rows[i].Inflow);
                if (outflow > max) { max = outflow; }
                if (inflow > max) { max = inflow; }
            }
            return max;
        }

        /// <summary>
        /// Turns each row's two gross flows into bar widths. Each side may occupy at most
        /// half the track, so the largest flow in the set exactly fills its half and every
        /// other bar reads as a fraction of it.
        /// </summary>
        /// <param name="rows">Rows to complete in place.</param>
        /// <param name="maxFlow">Shared scale from <see cref="MaxFlow"/>.</param>
        private void ApplyBarPercents(List<AccountRow> rows, decimal maxFlow)
        {
            if (maxFlow <= 0) { return; }

            for (int i = 0; i < rows.Count; i++)
            {
                AccountRow row = rows[i];
                row.InflowBarPct = Math.Abs(row.Inflow) / maxFlow * BAR_HalfWidthPct;
                row.OutflowBarPct = Math.Abs(row.Outflow) / maxFlow * BAR_HalfWidthPct;
            }
        }

        /// <summary>Indexes the row list by account id so a result set can be merged in one
        /// pass instead of a nested search per row.</summary>
        /// <param name="rows">Account rows.</param>
        /// <returns>C_BankAccount_ID -> row.</returns>
        private Dictionary<int, AccountRow> IndexByAccount(List<AccountRow> rows)
        {
            Dictionary<int, AccountRow> byAccount = new Dictionary<int, AccountRow>();
            for (int i = 0; i < rows.Count; i++)
            {
                if (!byAccount.ContainsKey(rows[i].BankAccountId))
                {
                    byAccount.Add(rows[i].BankAccountId, rows[i]);
                }
            }
            return byAccount;
        }

        // ─────────────────────────────────────────────────────────────────────
        // §8  Transfer objects
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>The reporting period. Internal - the client only sees the label and
        /// never a raw date.</summary>
        private class PeriodWindow
        {
            /// <summary>The period's display name.</summary>
            public string CurrentName { get; set; }

            /// <summary>Inclusive first day of the period.</summary>
            public DateTime CurrentStart { get; set; }

            /// <summary>Exclusive upper bound of the period (EndDate + 1 day).</summary>
            public DateTime CurrentEndExclusive { get; set; }
        }

        /// <summary>One page of the widget, plus everything about the whole set the page
        /// cannot know by itself.</summary>
        public class AccountBalanceResult
        {
            /// <summary>The requested page of account rows.</summary>
            public List<AccountRow> Rows { get; set; }

            /// <summary>The reporting period's display name - the subtitle's label.</summary>
            public string CurrentPeriodLabel { get; set; }

            /// <summary>The sort actually applied, echoed back so the control can show it.</summary>
            public string Sort { get; set; }

            /// <summary>1-based page number actually served, after clamping.</summary>
            public int Page { get; set; }

            /// <summary>Rows per page actually used, after clamping.</summary>
            public int PageSize { get; set; }

            /// <summary>Accessible bank accounts in total, across every page.</summary>
            public int TotalRows { get; set; }

            /// <summary>CEILING(TotalRows / PageSize).</summary>
            public int TotalPages { get; set; }

            /// <summary>Largest gross flow in the whole set - the shared bar scale.</summary>
            public decimal MaxFlow { get; set; }

            /// <summary>False only on a failure or a missing accounting calendar; a tenant
            /// with no accessible bank accounts is Loaded=true with an empty page.</summary>
            public bool Loaded { get; set; }
        }

        /// <summary>One bank account's row. Every amount is in THIS account's currency.</summary>
        public class AccountRow
        {
            /// <summary>C_BankAccount.C_BankAccount_ID - the drill-down target.</summary>
            public int BankAccountId { get; set; }

            /// <summary>Row label: C_BankAccount.Name, falling back to C_Bank.Name.</summary>
            public string AccountName { get; set; }

            /// <summary>C_Bank.Name on its own, for the tooltip.</summary>
            public string BankName { get; set; }

            /// <summary>C_BankAccount.AccountNo IN FULL, by explicit request - not masked.</summary>
            public string AccountNo { get; set; }

            /// <summary>The account's own ISO code - shown beside the masked number.</summary>
            public string CurrencyCode { get; set; }

            /// <summary>The account's own display symbol, falling back to its ISO code.</summary>
            public string CurrencySymbol { get; set; }

            /// <summary>The account currency's C_Currency.StdPrecision.</summary>
            public int Precision { get; set; }

            /// <summary>Money in during the period, as a positive magnitude.</summary>
            public decimal Inflow { get; set; }

            /// <summary>Money out during the period, as a positive magnitude.</summary>
            public decimal Outflow { get; set; }

            /// <summary>Inflow minus Outflow - the figure over the bar's axis.</summary>
            public decimal Net { get; set; }

            /// <summary>Number of receipts behind Inflow.</summary>
            public int InflowCount { get; set; }

            /// <summary>Number of payments behind Outflow.</summary>
            public int OutflowCount { get; set; }

            /// <summary>False when nothing moved either way this period - a state about the
            /// FLOWS, never about the balance.</summary>
            public bool HasActivity { get; set; }

            /// <summary>Latest settled EndingBalance before the period end, or
            /// C_BankAccount.CurrentBalance when the account has no statement.</summary>
            public decimal ClosingBalance { get; set; }

            /// <summary>False when the fallback was used - no settled statement exists.</summary>
            public bool HasStatement { get; set; }

            /// <summary>Right half of the bar, 0-50 percent of the whole track.</summary>
            public decimal InflowBarPct { get; set; }

            /// <summary>Left half of the bar, 0-50 percent of the whole track.</summary>
            public decimal OutflowBarPct { get; set; }
        }
    }
}
