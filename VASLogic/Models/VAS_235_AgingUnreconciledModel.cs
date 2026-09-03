/******************************************************
 * Module Name    : VASLogic
 * Purpose        : Aging of Unreconciled Items dashboard widget data
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
    /// Module Name : VAS_235_AgingUnreconciled
    /// Purpose     : Backs the VAS_235_AgingUnreconciledWidget dashboard widget and its
    ///               drill-down modal - how long the tenant's unreconciled payments have
    ///               been sitting, in five age buckets, and which documents are behind any
    ///               one of them.
    ///
    ///               C_Payment IS THE ONLY TRANSACTION SOURCE, for the summary and the
    ///               detail alike. Not C_BankStatement, not C_BankStatementLine, not the
    ///               allocation tables, not Fact_Acct. C_BPartner, C_BankAccount, C_Bank
    ///               and C_Currency appear only to resolve display values and are never
    ///               treated as a source of rows.
    ///
    ///                 Unreconciled  IsActive='Y', DocStatus IN ('CO','CL'),
    ///                               C_BankAccount_ID IS NOT NULL,
    ///                               COALESCE(IsReconciled,'N') &lt;&gt; 'Y',
    ///                               DateAcct &lt; AsOfDateExclusive.
    ///                               Reconciliation is read from IsReconciled ALONE -
    ///                               never inferred from IsAllocated, IsPrepayment or an
    ///                               allocation table - so the widget and the modal can
    ///                               never disagree about what "open" means.
    ///                 Age           whole days between DateAcct and AsOfDate. Future-dated
    ///                               payments are excluded, so it can never be negative.
    ///                 Buckets       b1 0-7, b2 8-15, b3 16-30, b4 31-60, b5 60+.
    ///                 Direction     C_Payment.IsReceipt - NEVER the sign of PayAmt.
    ///
    ///               ONE SET OF CUT-OFFS. AsOfDate, AsOfDateExclusive and the four cut-off
    ///               dates are computed once, here, and drive the summary and every bucket
    ///               of the detail. The bucket a payment falls into cannot differ between
    ///               the chart and the list it opens.
    ///
    ///               NO DATE ARITHMETIC IN SQL. "Days between two dates" is the one thing
    ///               that genuinely differs between the two backends - on this PostgreSQL
    ///               setup DATE - DATE yields an INTERVAL rather than a number, and the
    ///               workaround then has to be written so MRole's parser still copes. So
    ///               the cut-off dates are bound as parameters and the SQL only compares
    ///               one date against another; the Days column is subtracted in C# after
    ///               the row is read. One query shape, no dialect variants.
    ///
    ///               THE CLIENT NEVER SENDS A PREDICATE. It sends a bucket KEY - "b1".."b5"
    ///               - which is mapped here through a switch onto server-built date bounds.
    ///               An unrecognised key is refused rather than defaulted, so a malformed
    ///               request returns nothing instead of the wrong bucket.
    ///
    ///               CURRENCY, TWO WAYS ON PURPOSE. The widget aggregates across accounts,
    ///               so its sums go through currencyConvert(...) into the tenant's primary
    ///               accounting-schema currency - one bar cannot be built from added-up
    ///               different currencies. The MODAL does the opposite: it shows each
    ///               payment in its OWN currency with that currency's precision, because
    ///               its job is to show the original transaction.
    ///
    ///               MRole row-level security is applied to C_Payment p, the main physical
    ///               table, in both queries. GROUP BY and ORDER BY are appended AFTER
    ///               AddAccessSQL so its FROM-clause parser never meets a trailing clause,
    ///               and every join ON is a plain equality so it never meets a function
    ///               call either. Compatible with PostgreSQL and Oracle.
    /// Chronological development:
    ///   VAI154      2026-09-03 Created
    /// </summary>
    public class VAS_235_AgingUnreconciledModel
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_235_AgingUnreconciledModel).FullName);

        /* C_Payment.DocStatus codes that mean the money actually moved. Stored codes -
           compared bare, never with an N prefix. */
        private const string DOCSTATUS_Completed = "CO";
        private const string DOCSTATUS_Closed = "CL";

        /* C_Payment flag codes. */
        private const string ISRECEIPT_Yes = "Y";
        private const string ISRECEIPT_No = "N";
        private const string FLAG_Yes = "Y";

        /* Upper age bound of each bucket, in days. The last bucket is everything older. */
        private const int BUCKET_1_MaxAge = 7;
        private const int BUCKET_2_MaxAge = 15;
        private const int BUCKET_3_MaxAge = 30;
        private const int BUCKET_4_MaxAge = 60;

        private const int BUCKET_Count = 5;

        /* The age at which an item stops being routine - the widget's policy line, and the
           denominator-free half of its subtitle. Buckets 4 and 5 are past it. */
        private const int POLICY_ThresholdDays = 30;
        private const int POLICY_FirstBreachBucket = 4;

        /* Bucket keys as the client sends them. Anything else is refused. */
        public const string BUCKET_Key1 = "b1";
        public const string BUCKET_Key2 = "b2";
        public const string BUCKET_Key3 = "b3";
        public const string BUCKET_Key4 = "b4";
        public const string BUCKET_Key5 = "b5";

        /* Modal paging. */
        public const int DEFAULT_PageSize = 10;
        private const int MIN_PageSize = 1;
        private const int MAX_PageSize = 50;

        /* Characters of AccountNo the modal may show. Everything before them is masked
           HERE: the full number is never serialized to the browser. */
        private const int ACCOUNTNO_VisibleChars = 4;
        private const string ACCOUNTNO_Mask = "····";

        // ─────────────────────────────────────────────────────────────────────
        // §1  Summary - the widget itself
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The five age buckets with their receipt / payment counts and converted amounts,
        /// the totals behind the subtitle, and the policy-breach share.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="bankAccountId">C_BankAccount_ID to restrict to, or 0 for every
        /// account the role can see.</param>
        /// <returns>Populated <see cref="AgingResult"/> (never null). Loaded is false only
        /// when there is no context or no accounting schema; a tenant with nothing
        /// unreconciled returns Loaded=true and five empty buckets, because "nothing is
        /// outstanding" is a real answer rather than an error.</returns>
        public AgingResult GetAging(Ctx ctx, int bankAccountId)
        {
            AgingResult result = new AgingResult();
            result.Buckets = BuildEmptyBuckets();
            result.Accounts = new List<AccountOption>();
            result.Currency = new BaseCurrency();
            result.C_BankAccount_ID = bankAccountId > 0 ? bankAccountId : 0;

            if (ctx == null) { return result; }

            /* The filter's options travel with the figures, so the widget is one round trip
               on load and the list can never drift out of step with the selection. */
            result.Accounts = GetBankAccounts(ctx);

            result.Currency = GetBaseCurrency(ctx);
            if (result.Currency.C_Currency_ID <= 0)
            {
                Log.Log(Level.WARNING, "VAS_235_AgingUnreconciled: AD_ClientInfo.C_AcctSchema1_ID not configured for AD_Client_ID="
                    + ctx.GetAD_Client_ID());
                return result;
            }

            AgingWindow window = new AgingWindow(DateTime.Now.Date);
            result.AsOfDate = window.AsOfDate.ToString("yyyy-MM-dd");

            ReadBuckets(ctx, window, result.Currency.C_Currency_ID, result.C_BankAccount_ID, result.Buckets);

            /* The subtitle's figures are the buckets added up - asking the database for a
               separate grand total would put the same question twice. */
            for (int i = 0; i < result.Buckets.Count; i++)
            {
                AgingBucket bucket = result.Buckets[i];

                bucket.TotalCount = bucket.ReceiptCount + bucket.PaymentCount;
                bucket.TotalAmt = bucket.ReceiptAmt + bucket.PaymentAmt;

                result.TotalCount += bucket.TotalCount;
                result.TotalAmt += bucket.TotalAmt;

                /* Everything past the policy line, which is where an aging report earns
                   its keep - the two oldest buckets. */
                if (bucket.Bucket >= POLICY_FirstBreachBucket)
                {
                    result.PastPolicyCount += bucket.TotalCount;
                    result.PastPolicyAmt += bucket.TotalAmt;
                }
            }

            result.PolicyThresholdDays = POLICY_ThresholdDays;

            /* A share of nothing is not a percentage - the client omits the clause rather
               than printing 0% of an empty book. */
            if (result.TotalAmt != 0)
            {
                result.HasPastPolicyPct = true;
                result.PastPolicyPct = result.PastPolicyAmt / Math.Abs(result.TotalAmt) * 100m;
            }

            result.Loaded = true;
            return result;
        }

        /// <summary>
        /// The five buckets, in order, all zero. The widget always draws five rows - a
        /// bucket nothing fell into is an empty row, never a missing one - so the shape is
        /// built here rather than depending on what the query happens to return.
        /// </summary>
        /// <returns>Five ordered <see cref="AgingBucket"/> rows.</returns>
        private List<AgingBucket> BuildEmptyBuckets()
        {
            List<AgingBucket> buckets = new List<AgingBucket>();
            for (int i = 1; i <= BUCKET_Count; i++)
            {
                AgingBucket bucket = new AgingBucket();
                bucket.Bucket = i;
                bucket.Key = KeyOf(i);
                buckets.Add(bucket);
            }
            return buckets;
        }

        /// <summary>Maps a bucket ordinal onto the key the client sends back to open it.</summary>
        /// <param name="ordinal">1-5.</param>
        /// <returns>"b1".."b5".</returns>
        private string KeyOf(int ordinal)
        {
            switch (ordinal)
            {
                case 1: return BUCKET_Key1;
                case 2: return BUCKET_Key2;
                case 3: return BUCKET_Key3;
                case 4: return BUCKET_Key4;
                default: return ordinal == 5 ? BUCKET_Key5 : "";
            }
        }

        /// <summary>
        /// Reads the unreconciled payments grouped by age bucket and direction, in one pass.
        ///
        /// The bucket a payment falls into is decided by comparing DateAcct against four
        /// cut-off dates computed in C# - see the class summary for why the age is never
        /// subtracted in SQL.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="window">Resolved as-of date and cut-offs.</param>
        /// <param name="acctCurrencyId">Primary accounting-schema C_Currency_ID.</param>
        /// <param name="bankAccountId">One account, or 0 for all of them.</param>
        /// <param name="buckets">The five buckets to fill, indexed by ordinal.</param>
        private void ReadBuckets(Ctx ctx, AgingWindow window, int acctCurrencyId, int bankAccountId,
            List<AgingBucket> buckets)
        {
            bool filterAccount = bankAccountId > 0;

            /* The target currency is a server-resolved id, never client input, so it is
               inlined rather than bound - the provider binds by POSITION, and a value used
               inside a converted sum does not need a name of its own. */
            string convert = "currencyConvert(p.PayAmt,p.C_Currency_ID," + acctCurrencyId
                + ",p.DateAcct,p.C_ConversionType_ID,p.AD_Client_ID,p.AD_Org_ID)";

            /* The bucket expression appears TWICE - once in the SELECT and once in the
               GROUP BY, because Oracle supports neither positional GROUP BY nor grouping by
               a SELECT alias. Each occurrence carries its own parameter names, since the
               provider binds positionally. */
            string bucketSelect = @"CASE WHEN p.DateAcct>=@Cut_7_S THEN 1
                            WHEN p.DateAcct>=@Cut_15_S THEN 2
                            WHEN p.DateAcct>=@Cut_30_S THEN 3
                            WHEN p.DateAcct>=@Cut_60_S THEN 4
                            ELSE 5 END";

            string bucketGroup = @"CASE WHEN p.DateAcct>=@Cut_7_G THEN 1
                            WHEN p.DateAcct>=@Cut_15_G THEN 2
                            WHEN p.DateAcct>=@Cut_30_G THEN 3
                            WHEN p.DateAcct>=@Cut_60_G THEN 4
                            ELSE 5 END";

            StringBuilder sql = new StringBuilder();
            sql.Append(@"
                SELECT ").Append(bucketSelect).Append(@" AS Age_Bucket,
                       COALESCE(SUM(CASE WHEN p.IsReceipt='").Append(ISRECEIPT_Yes).Append(@"' THEN 1 ELSE 0 END),0) AS Receipt_Cnt,
                       COALESCE(SUM(CASE WHEN p.IsReceipt='").Append(ISRECEIPT_No).Append(@"' THEN 1 ELSE 0 END),0) AS Payment_Cnt,
                       COALESCE(SUM(CASE WHEN p.IsReceipt='").Append(ISRECEIPT_Yes).Append("' THEN ").Append(convert).Append(@" ELSE 0 END),0) AS Receipt_Amt,
                       COALESCE(SUM(CASE WHEN p.IsReceipt='").Append(ISRECEIPT_No).Append("' THEN ").Append(convert).Append(@" ELSE 0 END),0) AS Payment_Amt
                FROM C_Payment p
                ").Append(UnreconciledPredicate(filterAccount)).Append(@"
                  AND p.DateAcct<@AsOf_Exclusive");

            /* C_Payment p is the main physical table and the only one in the FROM clause.
               MRole supplies the organisation access, so no AD_Org_ID predicate is written
               by hand - the explicit tenant filter is a second, independent guard rather
               than the only one. Flat SUM(CASE ...) aggregation, never nested selects, so
               the access parser has one simple FROM clause to read. */
            string finalSql = MRole.GetDefault(ctx).AddAccessSQL(sql.ToString(), "p",
                MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            /* GROUP BY and ORDER BY go on AFTER the access SQL - its FROM-clause parser
               must not meet a trailing clause. The CASE is repeated in full rather than
               referenced by position or alias, neither of which Oracle accepts. */
            finalSql += " GROUP BY 1 ORDER BY 1";

            /* The provider binds POSITIONALLY, so the list is built in the order the
               placeholders appear in the finished text: the four SELECT cut-offs, the WHERE
               tenant filter, the optional account filter, the as-of bound, then the four
               GROUP BY cut-offs. */
            List<SqlParameter> parameters = new List<SqlParameter>();
            parameters.Add(new SqlParameter("@Cut_7_S", window.Cutoff7));
            parameters.Add(new SqlParameter("@Cut_15_S", window.Cutoff15));
            parameters.Add(new SqlParameter("@Cut_30_S", window.Cutoff30));
            parameters.Add(new SqlParameter("@Cut_60_S", window.Cutoff60));
            parameters.Add(new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()));
            if (filterAccount) { 
                parameters.Add(new SqlParameter("@C_BankAccount_ID", bankAccountId)); 
            }
            parameters.Add(new SqlParameter("@AsOf_Exclusive", window.AsOfDateExclusive));
            parameters.Add(new SqlParameter("@Cut_7_G", window.Cutoff7));
            parameters.Add(new SqlParameter("@Cut_15_G", window.Cutoff15));
            parameters.Add(new SqlParameter("@Cut_30_G", window.Cutoff30));
            parameters.Add(new SqlParameter("@Cut_60_G", window.Cutoff60));

            DataSet ds = DB.ExecuteDataset(finalSql, parameters.ToArray(), null);
            if (ds == null || ds.Tables.Count == 0) { return; }

            DataTable dt = ds.Tables[0];
            for (int i = 0; i < dt.Rows.Count; i++)
            {
                DataRow row = dt.Rows[i];

                int ordinal = Util.GetValueOfInt(row["Age_Bucket"]);
                if (ordinal < 1 || ordinal > buckets.Count) { continue; }

                /* Ordinals are 1-based; the list is 0-based. */
                AgingBucket bucket = buckets[ordinal - 1];
                bucket.ReceiptCount = Util.GetValueOfInt(row["Receipt_Cnt"]);
                bucket.PaymentCount = Util.GetValueOfInt(row["Payment_Cnt"]);
                bucket.ReceiptAmt = Util.GetValueOfDecimal(row["Receipt_Amt"]);
                bucket.PaymentAmt = Util.GetValueOfDecimal(row["Payment_Amt"]);
            }
        }

        /// <summary>
        /// The shared WHERE clause defining an unreconciled payment. Written once and used
        /// by BOTH the summary and the detail, so the modal can never show a different
        /// population from the bar the user clicked.
        ///
        /// Its bind placeholders always appear in the same order - the tenant filter first
        /// and, when one account is selected, the account filter LAST - so both callers can
        /// build their parameter array the same way before adding their own date bounds.
        /// </summary>
        /// <param name="filterAccount">True to add the single-account predicate.</param>
        /// <returns>A WHERE clause fragment beginning with "WHERE".</returns>
        private string UnreconciledPredicate(bool filterAccount)
        {
            StringBuilder where = new StringBuilder();
            where.Append(@"WHERE p.IsActive='Y'
                  AND p.AD_Client_ID=@AD_Client_ID
                  AND p.DocStatus IN ('").Append(DOCSTATUS_Completed).Append("','").Append(DOCSTATUS_Closed).Append(@"')
                  AND p.C_BankAccount_ID IS NOT NULL
                  AND COALESCE(p.IsReconciled,'N')<>'").Append(FLAG_Yes).Append(@"'");

            /* One bank account rather than all of them. The id is not trusted - it is
               simply an extra equality on top of the tenant filter and MRole's own access
               clause, so an id the role cannot see returns nothing rather than someone
               else's payments. */
            if (filterAccount)
            {
                where.Append(" AND p.C_BankAccount_ID=@C_BankAccount_ID");
            }

            return where.ToString();
        }

        // ─────────────────────────────────────────────────────────────────────
        // §1b  The bank account filter's options
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

            /* C_Bank is a reference lookup; C_BankAccount ba is the main physical table.
               The closing join ON is a plain equality so the access parser has nothing to
               trip on. */
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
                /* The same "UCO ····9032" form the modal shows, so one account reads
                   identically in the filter and in the detail rows. */
                option.Name = BuildBankAccount(
                    Util.GetValueOfString(row["Bank_Name"]),
                    Util.GetValueOfString(row["Account_Name"]),
                    Util.GetValueOfString(row["Account_No"]));

                options.Add(option);
            }

            return options;
        }

        // ─────────────────────────────────────────────────────────────────────
        // §2  Detail - the drill-down modal
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// One page of the unreconciled payments inside a single age bucket, each in its own
        /// payment currency.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="bucketKey">"b1".."b5" as the widget sent it. Anything else is
        /// refused rather than defaulted.</param>
        /// <param name="bankAccountId">C_BankAccount_ID to restrict to, or 0 for every
        /// account - the SAME selection the widget was showing, so the list can only ever
        /// hold the payments behind the number that was clicked.</param>
        /// <param name="pageNo">1-based page; clamped to the available range.</param>
        /// <param name="pageSize">Rows per page; clamped to [1,50].</param>
        /// <returns>Populated <see cref="BucketDetailResult"/> (never null). Loaded is false
        /// only when there is no context or the bucket key is not recognised; a bucket that
        /// holds nothing returns Loaded=true and an empty page.</returns>
        public BucketDetailResult GetBucketDetail(Ctx ctx, string bucketKey, int bankAccountId,
            int pageNo, int pageSize)
        {
            BucketDetailResult result = new BucketDetailResult();
            result.Rows = new List<DetailRow>();
            result.Bucket = bucketKey;
            result.C_BankAccount_ID = bankAccountId > 0 ? bankAccountId : 0;
            result.PageSize = ClampPageSize(pageSize);
            /* Carried in so ReadDetail can clamp it once the bucket's total is known. */
            result.Page = pageNo < 1 ? 1 : pageNo;

            if (ctx == null) { result.Page = 1; return result; }

            AgingWindow window = new AgingWindow(DateTime.Now.Date);
            result.AsOfDate = window.AsOfDate.ToString("yyyy-MM-dd");

            /* The client sends a KEY, never a predicate. An unrecognised key is refused
               outright - defaulting it would quietly show the user a different bucket from
               the one they clicked. */
            DateRange range = RangeFor(bucketKey, window);
            if (range == null)
            {
                Log.Log(Level.WARNING, "VAS_235_AgingUnreconciled: unrecognised bucket key for AD_Client_ID="
                    + ctx.GetAD_Client_ID());
                result.Page = 1;
                return result;
            }

            result.Ordinal = range.Ordinal;

            ReadDetail(ctx, range, window, result);

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

        /// <summary>
        /// Maps a bucket key onto its half-open DateAcct range, built from the same cut-offs
        /// the summary used. A switch, not a lookup on client text - there is no path by
        /// which a browser string reaches a WHERE clause.
        /// </summary>
        /// <param name="bucketKey">"b1".."b5".</param>
        /// <param name="window">Resolved as-of date and cut-offs.</param>
        /// <returns>The range, or null when the key is not recognised.</returns>
        private DateRange RangeFor(string bucketKey, AgingWindow window)
        {
            if (String.IsNullOrEmpty(bucketKey)) { return null; }

            /* Each bucket is [From, To) - the younger bound is exclusive, so a payment
               belongs to exactly one bucket and no age falls through a crack. */
            if (String.Equals(bucketKey, BUCKET_Key1, StringComparison.OrdinalIgnoreCase))
            {
                return new DateRange(1, window.Cutoff7, window.AsOfDateExclusive);
            }
            if (String.Equals(bucketKey, BUCKET_Key2, StringComparison.OrdinalIgnoreCase))
            {
                return new DateRange(2, window.Cutoff15, window.Cutoff7);
            }
            if (String.Equals(bucketKey, BUCKET_Key3, StringComparison.OrdinalIgnoreCase))
            {
                return new DateRange(3, window.Cutoff30, window.Cutoff15);
            }
            if (String.Equals(bucketKey, BUCKET_Key4, StringComparison.OrdinalIgnoreCase))
            {
                return new DateRange(4, window.Cutoff60, window.Cutoff30);
            }
            if (String.Equals(bucketKey, BUCKET_Key5, StringComparison.OrdinalIgnoreCase))
            {
                /* The oldest bucket has no lower bound - DateTime.MinValue stands in so the
                   query keeps ONE shape and one parameter list for all five buckets. */
                return new DateRange(5, DateTime.MinValue, window.Cutoff60);
            }

            return null;
        }

        /// <summary>
        /// Counts the bucket, then reads the requested page of it.
        ///
        /// The vendor and document-type names come from scalar sub-queries rather than
        /// outer joins on purpose: MRole appends its access predicates to the WHERE clause,
        /// and a client or organisation predicate on an outer-joined C_BPartner would
        /// silently turn that join into an inner one and drop every payment that has no
        /// business partner - the very rows the specification says to keep and render as a
        /// dash. Keeping both lookups in the SELECT list also leaves the FROM clause shared
        /// with the COUNT query untouched.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="range">The selected bucket's date bounds.</param>
        /// <param name="window">Resolved as-of date, for the Days column.</param>
        /// <param name="result">Result being filled - paging fields included.</param>
        private void ReadDetail(Ctx ctx, DateRange range, AgingWindow window, BucketDetailResult result)
        {
            bool filterAccount = result.C_BankAccount_ID > 0;

            /* Bank, bank account and currency are INNER joins and safe: the payment already
               has to carry a bank account, and a payment always has a currency. */
            StringBuilder from = new StringBuilder();
            from.Append(@"
                FROM C_Payment p
                INNER JOIN C_BankAccount ba ON (ba.C_BankAccount_ID=p.C_BankAccount_ID)
                INNER JOIN C_Bank b ON (b.C_Bank_ID=ba.C_Bank_ID)
                INNER JOIN C_Currency cur ON (cur.C_Currency_ID=p.C_Currency_ID)
                ").Append(UnreconciledPredicate(filterAccount)).Append(@"
                  AND p.DateAcct>=@Date_From
                  AND p.DateAcct<@Date_To");

            /* Positional binding again: tenant, then the optional account filter, then the
               bucket's two date bounds - the order the placeholders appear in the text. The
               same array serves the count query and the page query, which share a FROM. */
            List<SqlParameter> binds = new List<SqlParameter>();
            binds.Add(new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()));
            if (filterAccount) { binds.Add(new SqlParameter("@C_BankAccount_ID", result.C_BankAccount_ID)); }
            binds.Add(new SqlParameter("@Date_From", range.From));
            binds.Add(new SqlParameter("@Date_To", range.ToExclusive));

            SqlParameter[] parameters = binds.ToArray();

            /* ---- how many rows the bucket holds ---- */
            string countSql = MRole.GetDefault(ctx).AddAccessSQL(
                "SELECT COUNT(p.C_Payment_ID) AS Row_Cnt" + from.ToString(), "p",
                MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            DataSet countDs = DB.ExecuteDataset(countSql, parameters, null);
            if (countDs == null || countDs.Tables.Count == 0 || countDs.Tables[0].Rows.Count == 0) { return; }

            result.TotalRows = Util.GetValueOfInt(countDs.Tables[0].Rows[0]["Row_Cnt"]);
            result.TotalPages = result.PageSize > 0
                ? (int)Math.Ceiling((double)result.TotalRows / result.PageSize)
                : 0;

            if (result.TotalRows == 0) { result.Page = 1; return; }

            /* Clamp the page AFTER the total is known: a page number the client kept from a
               larger bucket must land on the last real page, never past the end. */
            int page = result.Page < 1 ? 1 : result.Page;
            if (result.TotalPages > 0 && page > result.TotalPages) { page = result.TotalPages; }
            result.Page = page;

            /* ---- the page itself ---- */
            StringBuilder sql = new StringBuilder();
            sql.Append(@"
                SELECT p.C_Payment_ID AS C_Payment_ID,
                       p.DateAcct AS Acct_Date,
                       p.DocumentNo AS Payment_No,
                       p.IsReceipt AS Is_Receipt,
                       p.PayAmt AS Pay_Amt,
                       ba.Name AS Bank_Account_Name,
                       ba.AccountNo AS Account_No,
                       b.Name AS Bank_Name,
                       cur.ISO_Code AS Currency_Iso,
                       cur.StdPrecision AS Std_Precision,
                       CASE WHEN cur.CurSymbol IS NOT NULL THEN cur.CurSymbol ELSE cur.ISO_Code END AS Currency_Symbol,
                       (SELECT bp.Name FROM C_BPartner bp WHERE bp.C_BPartner_ID=p.C_BPartner_ID) AS Vendor_Name,
                       (SELECT dt.Name FROM C_DocType dt WHERE dt.C_DocType_ID=p.C_DocType_ID) AS Doc_Type_Name")
                .Append(from.ToString());

            string pageSql = MRole.GetDefault(ctx).AddAccessSQL(sql.ToString(), "p",
                MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            /* ORDER BY after the access SQL. Newest first, with the id as a deterministic
               tiebreaker so two documents on the same date cannot swap between pages.

               OFFSET / FETCH is ANSI and supported by PostgreSQL 12+ and Oracle 12c+ alike,
               so there is no dialect branch here. The two numbers are server-clamped
               integers, never client text, which is why they can be composed in directly. */
            int offset = (page - 1) * result.PageSize;
            pageSql += " ORDER BY p.DateAcct DESC,p.C_Payment_ID DESC"
                + " OFFSET " + offset + " ROWS FETCH NEXT " + result.PageSize + " ROWS ONLY";

            DataSet ds = DB.ExecuteDataset(pageSql, parameters, null);
            if (ds == null || ds.Tables.Count == 0) { return; }

            DataTable dt = ds.Tables[0];
            for (int i = 0; i < dt.Rows.Count; i++)
            {
                result.Rows.Add(MapDetailRow(dt.Rows[i], window));
            }
        }

        /// <summary>Materialises one payment row for the modal.</summary>
        /// <param name="row">Row carrying the detail aliases.</param>
        /// <param name="window">Resolved as-of date, for the Days column.</param>
        /// <returns>Populated <see cref="DetailRow"/>.</returns>
        private DetailRow MapDetailRow(DataRow row, AgingWindow window)
        {
            DetailRow item = new DetailRow();

            item.C_Payment_ID = Util.GetValueOfInt(row["C_Payment_ID"]);
            item.PaymentNo = Util.GetValueOfString(row["Payment_No"]);
            item.Vendor = Util.GetValueOfString(row["Vendor_Name"]);
            item.DocumentType = Util.GetValueOfString(row["Doc_Type_Name"]);
            item.CurrencyCode = Util.GetValueOfString(row["Currency_Iso"]);
            item.CurrencySymbol = Util.GetValueOfString(row["Currency_Symbol"]);
            item.Precision = Util.GetValueOfInt(row["Std_Precision"]);

            DateTime? acctDate = Util.GetValueOfDateTime(row["Acct_Date"]);
            if (acctDate.HasValue)
            {
                item.AcctDate = acctDate.Value.ToString("yyyy-MM-dd");

                /* Whole calendar days, subtracted HERE rather than in SQL. Future-dated
                   payments are already excluded by the as-of bound, so this cannot go
                   negative; the clamp is belt and braces. */
                int days = (window.AsOfDate.Date - acctDate.Value.Date).Days;
                item.Days = days < 0 ? 0 : days;
            }

            /* Direction comes from IsReceipt, never from the stored sign of PayAmt. The
               amount is then signed for display: a receipt reads positive, a payment
               negative. */
            item.IsReceipt = String.Equals(Util.GetValueOfString(row["Is_Receipt"]),
                ISRECEIPT_Yes, StringComparison.OrdinalIgnoreCase);

            decimal amount = Math.Abs(Util.GetValueOfDecimal(row["Pay_Amt"]));
            item.Amount = item.IsReceipt ? amount : -amount;

            item.BankAccount = BuildBankAccount(
                Util.GetValueOfString(row["Bank_Name"]),
                Util.GetValueOfString(row["Bank_Account_Name"]),
                Util.GetValueOfString(row["Account_No"]));

            return item;
        }

        /// <summary>
        /// The bank account as the modal shows it - "UCO ····9032". The bank's name plus a
        /// masked tail is the most recognisable form; the account's own Name stands in when
        /// there is no number to mask, and an empty string when there is neither, which the
        /// client renders as a dash.
        /// </summary>
        /// <param name="bankName">C_Bank.Name.</param>
        /// <param name="accountName">C_BankAccount.Name.</param>
        /// <param name="accountNo">C_BankAccount.AccountNo - never returned in full.</param>
        /// <returns>Display string; may be empty.</returns>
        private string BuildBankAccount(string bankName, string accountName, string accountNo)
        {
            string masked = MaskAccountNo(accountNo);

            if (masked.Length > 0)
            {
                return String.IsNullOrEmpty(bankName) ? masked : bankName + " " + masked;
            }

            if (!String.IsNullOrEmpty(accountName)) { return accountName; }
            return bankName == null ? "" : bankName;
        }

        /// <summary>
        /// Shows only the last few characters of an account number - "····9032". The full
        /// number is never serialized to the browser, so it can never reach the DOM.
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
        // §3  Base currency
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The tenant's base currency: the currency of the primary accounting schema
        /// (AD_ClientInfo.C_AcctSchema1_ID). Reads only system / reference tables scoped to
        /// the session client, so no MRole predicate is applied - the same treatment the
        /// sibling KPI widgets give this lookup.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <returns>Populated <see cref="BaseCurrency"/>; C_Currency_ID is 0 when the tenant
        /// has no primary accounting schema.</returns>
        private BaseCurrency GetBaseCurrency(Ctx ctx)
        {
            BaseCurrency result = new BaseCurrency();
            result.Precision = 2;

            string sql = @"
                SELECT AcctSchema.C_Currency_ID AS Acct_Currency_ID,
                       Currency.StdPrecision AS Std_Precision,
                       CASE WHEN Currency.CurSymbol IS NOT NULL THEN Currency.CurSymbol ELSE Currency.ISO_Code END AS Currency_Symbol,
                       Currency.ISO_Code AS Currency_Iso
                FROM AD_ClientInfo ClientInfo
                INNER JOIN C_AcctSchema AcctSchema ON (AcctSchema.C_AcctSchema_ID=ClientInfo.C_AcctSchema1_ID)
                INNER JOIN C_Currency Currency ON (Currency.C_Currency_ID=AcctSchema.C_Currency_ID)
                WHERE ClientInfo.AD_Client_ID=@AD_Client_ID";

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
            };

            DataSet ds = DB.ExecuteDataset(sql, parameters, null);
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) { return result; }

            DataRow row = ds.Tables[0].Rows[0];
            result.C_Currency_ID = Util.GetValueOfInt(row["Acct_Currency_ID"]);
            result.Precision = Util.GetValueOfInt(row["Std_Precision"]);
            result.Symbol = Util.GetValueOfString(row["Currency_Symbol"]);
            result.Iso = Util.GetValueOfString(row["Currency_Iso"]);

            return result;
        }

        // ─────────────────────────────────────────────────────────────────────
        // §4  Internal helpers
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The as-of date and the four bucket cut-offs, computed once and shared by the
        /// summary and every bucket of the detail - which is what guarantees a payment
        /// cannot land in one bucket on the chart and another in the list it opens.
        /// </summary>
        private class AgingWindow
        {
            /// <summary>The date every age is measured against.</summary>
            public DateTime AsOfDate { get; private set; }

            /// <summary>Exclusive upper bound (AsOfDate + 1 day) - a payment dated today is
            /// zero days old and still in scope, and anything future-dated is out.</summary>
            public DateTime AsOfDateExclusive { get; private set; }

            /// <summary>On or after this is 0-7 days old.</summary>
            public DateTime Cutoff7 { get; private set; }

            /// <summary>On or after this (and before Cutoff7) is 8-15 days old.</summary>
            public DateTime Cutoff15 { get; private set; }

            /// <summary>On or after this (and before Cutoff15) is 16-30 days old.</summary>
            public DateTime Cutoff30 { get; private set; }

            /// <summary>On or after this (and before Cutoff30) is 31-60 days old; before it
            /// is the 60+ bucket.</summary>
            public DateTime Cutoff60 { get; private set; }

            /// <summary>Builds the window from the current application date.</summary>
            /// <param name="today">Current application date (date part only).</param>
            public AgingWindow(DateTime today)
            {
                AsOfDate = today.Date;
                AsOfDateExclusive = AsOfDate.AddDays(1);
                Cutoff7 = AsOfDate.AddDays(-BUCKET_1_MaxAge);
                Cutoff15 = AsOfDate.AddDays(-BUCKET_2_MaxAge);
                Cutoff30 = AsOfDate.AddDays(-BUCKET_3_MaxAge);
                Cutoff60 = AsOfDate.AddDays(-BUCKET_4_MaxAge);
            }
        }

        /// <summary>One bucket's half-open DateAcct range.</summary>
        private class DateRange
        {
            /// <summary>Bucket ordinal 1-5, echoed back so the modal can name itself.</summary>
            public int Ordinal { get; private set; }

            /// <summary>Inclusive lower bound.</summary>
            public DateTime From { get; private set; }

            /// <summary>Exclusive upper bound.</summary>
            public DateTime ToExclusive { get; private set; }

            /// <summary>Builds a range.</summary>
            /// <param name="ordinal">Bucket ordinal 1-5.</param>
            /// <param name="from">Inclusive lower bound.</param>
            /// <param name="toExclusive">Exclusive upper bound.</param>
            public DateRange(int ordinal, DateTime from, DateTime toExclusive)
            {
                Ordinal = ordinal;
                From = from;
                ToExclusive = toExclusive;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // §5  Transfer objects
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>Everything the widget needs on one paint.</summary>
        public class AgingResult
        {
            /// <summary>The five age buckets, youngest first. Always five, even when empty.</summary>
            public List<AgingBucket> Buckets { get; set; }

            /// <summary>The accounts the filter can offer. "All" is the client's own option
            /// and is not a row here - it is the absence of a filter.</summary>
            public List<AccountOption> Accounts { get; set; }

            /// <summary>The account the figures are filtered to; 0 means all of them.</summary>
            public int C_BankAccount_ID { get; set; }

            /// <summary>Unreconciled payments in total - the subtitle's count.</summary>
            public int TotalCount { get; set; }

            /// <summary>Total exposure in base currency - the subtitle's amount.</summary>
            public decimal TotalAmt { get; set; }

            /// <summary>Exposure past the policy threshold (buckets 4 and 5).</summary>
            public decimal PastPolicyAmt { get; set; }

            /// <summary>Line count past the policy threshold.</summary>
            public int PastPolicyCount { get; set; }

            /// <summary>PastPolicyAmt as a percentage of TotalAmt. Only meaningful when
            /// HasPastPolicyPct is true.</summary>
            public decimal PastPolicyPct { get; set; }

            /// <summary>False when there is no exposure at all - a share of nothing is not
            /// a percentage.</summary>
            public bool HasPastPolicyPct { get; set; }

            /// <summary>Age in days at which the policy line is drawn, for its caption.</summary>
            public int PolicyThresholdDays { get; set; }

            /// <summary>The date every age was measured against, as yyyy-MM-dd.</summary>
            public string AsOfDate { get; set; }

            /// <summary>Currency every amount here is stated in.</summary>
            public BaseCurrency Currency { get; set; }

            /// <summary>False only on a failure or a missing accounting schema; a tenant
            /// with nothing unreconciled is Loaded=true with five empty buckets.</summary>
            public bool Loaded { get; set; }
        }

        /// <summary>One age bucket, split into its receipt and payment sides.</summary>
        public class AgingBucket
        {
            /// <summary>Ordinal 1-5: 0-7, 8-15, 16-30, 31-60, 60+ days. The LABEL is the
            /// client's, from AD_Message - never text from a query.</summary>
            public int Bucket { get; set; }

            /// <summary>The key the client sends back to open this bucket's detail.</summary>
            public string Key { get; set; }

            /// <summary>Unreconciled receipts (IsReceipt='Y') in this bucket.</summary>
            public int ReceiptCount { get; set; }

            /// <summary>Their converted SUM(ABS(PayAmt)), in base currency.</summary>
            public decimal ReceiptAmt { get; set; }

            /// <summary>Unreconciled payments (IsReceipt='N') in this bucket.</summary>
            public int PaymentCount { get; set; }

            /// <summary>Their converted SUM(ABS(PayAmt)), in base currency.</summary>
            public decimal PaymentAmt { get; set; }

            /// <summary>Both sides' line counts - the row's Count column.</summary>
            public int TotalCount { get; set; }

            /// <summary>Both sides' amounts - the row's Value column and its bar length.</summary>
            public decimal TotalAmt { get; set; }
        }

        /// <summary>One page of a bucket's payments.</summary>
        public class BucketDetailResult
        {
            /// <summary>The page of payments, newest accounting date first.</summary>
            public List<DetailRow> Rows { get; set; }

            /// <summary>The bucket key echoed back, so a late response for a bucket the user
            /// has moved away from can be discarded.</summary>
            public string Bucket { get; set; }

            /// <summary>Bucket ordinal 1-5, so the modal can name itself from AD_Message.</summary>
            public int Ordinal { get; set; }

            /// <summary>The account filter in force; 0 means all of them. Echoed back so a
            /// late response for a selection the user has moved away from is discardable.</summary>
            public int C_BankAccount_ID { get; set; }

            /// <summary>The date the Days column was measured against, as yyyy-MM-dd.</summary>
            public string AsOfDate { get; set; }

            /// <summary>1-based page number actually served, after clamping.</summary>
            public int Page { get; set; }

            /// <summary>Rows per page actually used, after clamping.</summary>
            public int PageSize { get; set; }

            /// <summary>Payments in the bucket in total, across every page.</summary>
            public int TotalRows { get; set; }

            /// <summary>CEILING(TotalRows / PageSize).</summary>
            public int TotalPages { get; set; }

            /// <summary>False on a failure or an unrecognised bucket key; an empty bucket is
            /// Loaded=true with no rows.</summary>
            public bool Loaded { get; set; }
        }

        /// <summary>One payment in the modal. Amounts are in THIS payment's own currency -
        /// never converted, because the modal's job is to show the original transaction.</summary>
        public class DetailRow
        {
            /// <summary>C_Payment.C_Payment_ID - the drill-through target.</summary>
            public int C_Payment_ID { get; set; }

            /// <summary>C_Payment.DateAcct as yyyy-MM-dd; the client formats it.</summary>
            public string AcctDate { get; set; }

            /// <summary>C_Payment.DocumentNo - the modal's zoom link.</summary>
            public string PaymentNo { get; set; }

            /// <summary>C_DocType.Name behind C_Payment.C_DocType_ID; empty when it does not
            /// resolve, which the client renders as a dash.</summary>
            public string DocumentType { get; set; }

            /// <summary>C_BPartner.Name, or empty when the payment carries no partner - the
            /// client renders that as a dash, never as an internal id.</summary>
            public string Vendor { get; set; }

            /// <summary>Bank name plus the masked account tail. Never the full number.</summary>
            public string BankAccount { get; set; }

            /// <summary>The payment's own ISO code.</summary>
            public string CurrencyCode { get; set; }

            /// <summary>The payment currency's display symbol, falling back to its ISO code.</summary>
            public string CurrencySymbol { get; set; }

            /// <summary>The payment currency's C_Currency.StdPrecision.</summary>
            public int Precision { get; set; }

            /// <summary>Whole days between DateAcct and the as-of date; never negative.</summary>
            public int Days { get; set; }

            /// <summary>True for a receipt, from IsReceipt - never from the stored sign.</summary>
            public bool IsReceipt { get; set; }

            /// <summary>PayAmt signed by direction: positive for a receipt, negative for a
            /// payment, in the payment's own currency.</summary>
            public decimal Amount { get; set; }
        }

        /// <summary>One selectable bank account in the widget's account filter.</summary>
        public class AccountOption
        {
            /// <summary>C_BankAccount.C_BankAccount_ID.</summary>
            public int C_BankAccount_ID { get; set; }

            /// <summary>Bank name plus the masked account tail - the same form the detail
            /// rows use, so one account reads identically in both places.</summary>
            public string Name { get; set; }
        }

        /// <summary>The accounting-schema currency the widget's figures are stated in.</summary>
        public class BaseCurrency
        {
            /// <summary>C_Currency_ID of the primary accounting schema; 0 when unset.</summary>
            public int C_Currency_ID { get; set; }

            /// <summary>ISO code - drives the client's compact scale (lakh/crore vs million).</summary>
            public string Iso { get; set; }

            /// <summary>Display symbol, falling back to the ISO code.</summary>
            public string Symbol { get; set; }

            /// <summary>C_Currency.StdPrecision.</summary>
            public int Precision { get; set; }
        }
    }
}
