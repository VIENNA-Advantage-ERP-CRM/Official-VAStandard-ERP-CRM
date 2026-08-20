/******************************************************
 * Module Name    : VASLogic
 * Purpose        : Bank Reconciliation dashboard widget data
 * chronological  : Development
 * Created Date   : 2026-08-20
 * Created by     : VAI145
 ******************************************************/

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
    /// <summary>
    /// Module Name : VAS_200_BankReconcilation
    /// Purpose     : Backs the VAS_200_BankReconcilationWidget dashboard widget.
    ///               Two views of ONE open accounting period, side by side:
    ///                 left  - the reconciliation status of its payments, three
    ///                         mutually exclusive buckets with a count and a total
    ///                         in the tenant's base currency;
    ///                 right - the bank accounts those payments actually moved
    ///                         through, each with the date it was last reconciled
    ///                         and how far behind the period end that leaves it.
    ///
    ///               Period source: the open periods of the tenant's PRIMARY
    ///               calendar (AD_ClientInfo.C_Calendar_ID) - a period qualifies
    ///               when at least one active C_PeriodControl row of it is Open.
    ///               Nothing is derived from the calendar month, and the selected
    ///               period's own StartDate / EndDate bound C_Payment.DateAcct.
    ///
    ///               Buckets (both reconciliation buckets are scoped to completed /
    ///               closed documents - they are two halves of one work queue):
    ///                 Reconciled            IsReconciled='Y' AND DocStatus IN ('CO','CL')
    ///                 Unreconciled          IsReconciled<>'Y' AND DocStatus IN ('CO','CL')
    ///                 In progress / bounced DocStatus='IP', OR the payment carries a
    ///                                       bounced / rejected VA009 execution status
    ///                                       and is not voided or reversed
    ///               The right-hand accounts list and its detail modal carry the same
    ///               completed / closed scope; only the in-progress bucket steps
    ///               outside it, since that is what it exists to report.
    ///               The bounce rule is NOT invented here: it is the condition this
    ///               product already uses (VAS BouncedChequesController), and it is
    ///               only applied when the VA009 module's column is actually present
    ///               in the environment - resolved from AD_Column at runtime.
    ///
    ///               Every monetary figure is converted to the primary accounting
    ///               schema's currency (AD_ClientInfo.C_AcctSchema1_ID) through the
    ///               standard currencyConvert function at the payment's accounting
    ///               date - never summed raw across currencies.
    ///
    ///               "Days behind" is measured against the SELECTED PERIOD END, never
    ///               against today, and is worked out in C# so no dialect-specific
    ///               date arithmetic is needed.
    ///
    ///               MRole row-level security is applied to the main physical table
    ///               of every query (C_Period alias p, C_Payment alias p,
    ///               C_BankAccountLine alias bal); the joined C_Year / AD_ClientInfo /
    ///               C_Bank / C_BankAccount / C_BPartner / C_Currency rows are lookup
    ///               tables and inherit that filter, and the C_PeriodControl EXISTS
    ///               check is a child-table predicate. GROUP BY, ORDER BY and the
    ///               paging suffix are appended AFTER AddAccessSQL so the FROM-clause
    ///               parser is not confused by a trailing clause. Compatible with
    ///               PostgreSQL and Oracle.
    /// Chronological development:
    ///   VAI154      2026-08-20 Created
    /// </summary>
    public class VAS_200_BankReconcilationModel
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_200_BankReconcilationModel).FullName);

        /* Bucket tokens exchanged with the client. The client maps each to a
           localized AD_Message label, so no display text is produced here. */
        public const string CATEGORY_RECONCILED = "RECONCILED";
        public const string CATEGORY_UNRECONCILED = "UNRECONCILED";
        public const string CATEGORY_INPROGRESS = "INPROGRESS";

        /* The right-hand list's own detail view: every payment of one bank account
           in the period, whatever its reconciliation state. */
        public const string CATEGORY_ACCOUNT = "ACCOUNT";

        /* Error tokens; the client resolves the label. */
        public const string ERROR_INVALID_REQUEST = "INVALID";
        public const string ERROR_NO_PERIOD = "NOPERIOD";

        /* C_PeriodControl.PeriodStatus stored code for an open control row. */
        private const string PERIODSTATUS_Open = "O";

        /* Reconciliation only concerns documents that actually happened. Every view
           of the card and of its detail lists is scoped to completed / closed
           payments - the one exception is the in-progress / bounced bucket, whose
           whole point is the documents that have not got there yet. */
        private const string DOCSTATUS_COMPLETED_CLOSED = "p.DocStatus IN ('CO','CL')";

        /* The VA009 execution-status column carries this product's bounce state. The
           module may not be installed, so its presence is resolved from AD_Column at
           runtime and memoised per app domain: 0 = not looked up, 1 = present,
           -1 = absent. */
        private const string TABLENAME_Payment = "C_Payment";
        private const string COLUMNNAME_ExecutionStatus = "VA009_ExecutionStatus";
        private static int _hasExecutionStatus = 0;

        /* Detail paging guard rails. The client asks for a page size; anything
           outside this band is clamped so a crafted request cannot pull the whole
           payment table into one response. */
        private const int PAGESIZE_MIN = 1;
        private const int PAGESIZE_MAX = 100;
        private const int PAGESIZE_DEFAULT = 8;

        // ─────────────────────────────────────────────────────────────────────
        // §1  Period list and bootstrap
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Bootstraps the widget in one round trip: every selectable open period of
        /// the primary calendar, the period to preselect, and that period's status
        /// buckets and bank accounts.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <returns>Populated <see cref="ReconciliationBootstrap"/> (never null).</returns>
        public ReconciliationBootstrap GetBootstrap(Ctx ctx)
        {
            ReconciliationBootstrap result = new ReconciliationBootstrap();
            result.Periods = new List<PeriodItem>();
            result.Data = new PeriodData();

            if (ctx == null) { return result; }

            result.Periods = GetOpenPeriods(ctx);
            if (result.Periods.Count == 0) { return result; }

            PeriodItem selected = PickDefaultPeriod(result.Periods, DateTime.Now.Date);
            result.C_Period_ID = selected.C_Period_ID;
            result.PeriodName = selected.Name;
            result.Data = GetPeriodData(ctx, selected.C_Period_ID);

            return result;
        }

        /// <summary>
        /// The open periods of the tenant's PRIMARY calendar, newest first. A period
        /// qualifies when at least one of its active C_PeriodControl rows is Open; it
        /// appears once however many document base types are open for it.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <returns>Open periods, newest StartDate first (never null).</returns>
        public List<PeriodItem> GetOpenPeriods(Ctx ctx)
        {
            List<PeriodItem> items = new List<PeriodItem>();
            if (ctx == null) { return items; }

            /* AD_ClientInfo pins the calendar to the tenant rather than searching all
               calendars, and the open-control test is an EXISTS predicate, not a
               join, so several open base types cannot multiply the period out. */
            string sql = @"
                SELECT p.C_Period_ID AS C_Period_ID,
                       p.Name AS Period_Name,
                       p.StartDate AS Start_Date,
                       p.EndDate AS End_Date,
                       y.C_Year_ID AS C_Year_ID,
                       y.FiscalYear AS Fiscal_Year,
                       ci.C_Calendar_ID AS C_Calendar_ID
                FROM C_Period p
                INNER JOIN C_Year y ON (y.C_Year_ID=p.C_Year_ID)
                INNER JOIN AD_ClientInfo ci ON (ci.C_Calendar_ID=y.C_Calendar_ID AND ci.AD_Client_ID=@AD_Client_ID)
                WHERE p.IsActive='Y'
                  AND y.IsActive='Y'
                  AND EXISTS(SELECT 1 FROM C_PeriodControl pc WHERE pc.C_Period_ID=p.C_Period_ID AND pc.IsActive='Y' AND pc.PeriodStatus=@PeriodStatus)";

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "p", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            sql += " ORDER BY p.StartDate DESC,p.EndDate DESC,p.C_Period_ID DESC";

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@PeriodStatus", PERIODSTATUS_Open)
            };

            DataSet ds = DB.ExecuteDataset(sql, parameters, null);
            if (ds == null || ds.Tables.Count == 0) { return items; }

            DataTable dt = ds.Tables[0];
            for (int i = 0; i < dt.Rows.Count; i++)
            {
                PeriodItem item = new PeriodItem();
                item.C_Period_ID = Util.GetValueOfInt(dt.Rows[i]["C_Period_ID"]);
                item.Name = Util.GetValueOfString(dt.Rows[i]["Period_Name"]);
                item.StartDate = Util.GetValueOfDateTime(dt.Rows[i]["Start_Date"]);
                item.EndDate = Util.GetValueOfDateTime(dt.Rows[i]["End_Date"]);
                item.C_Year_ID = Util.GetValueOfInt(dt.Rows[i]["C_Year_ID"]);
                item.FiscalYear = Util.GetValueOfString(dt.Rows[i]["Fiscal_Year"]);
                item.C_Calendar_ID = Util.GetValueOfInt(dt.Rows[i]["C_Calendar_ID"]);
                items.Add(item);
            }

            return items;
        }

        /// <summary>
        /// Chooses which open period the widget opens on: the one containing today,
        /// otherwise the most recent one that has already started, otherwise the
        /// first of the list. The list is newest-first, so the first match wins.
        /// </summary>
        /// <param name="periods">Open periods, newest StartDate first.</param>
        /// <param name="today">Current application date (date part only).</param>
        /// <returns>The period to preselect (never null when the list is filled).</returns>
        private PeriodItem PickDefaultPeriod(List<PeriodItem> periods, DateTime today)
        {
            PeriodItem started = null;

            for (int i = 0; i < periods.Count; i++)
            {
                PeriodItem item = periods[i];
                if (!item.StartDate.HasValue || !item.EndDate.HasValue) { continue; }

                DateTime from = item.StartDate.Value.Date;
                DateTime to = item.EndDate.Value.Date;

                if (from <= today && to >= today) { return item; }
                if (started == null && from <= today) { started = item; }
            }

            return started != null ? started : periods[0];
        }

        /// <summary>
        /// Re-reads one period and confirms it is still active, accessible, open and
        /// on the tenant's primary calendar. The client only ever sends the id; the
        /// date range the queries run against always comes from here.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="periodId">C_Period_ID the client selected.</param>
        /// <returns>Populated <see cref="PeriodItem"/>, or null when it no longer qualifies.</returns>
        private PeriodItem GetOpenPeriod(Ctx ctx, int periodId)
        {
            if (ctx == null || periodId <= 0) { return null; }

            string sql = @"
                SELECT p.C_Period_ID AS C_Period_ID,
                       p.Name AS Period_Name,
                       p.StartDate AS Start_Date,
                       p.EndDate AS End_Date
                FROM C_Period p
                INNER JOIN C_Year y ON (y.C_Year_ID=p.C_Year_ID)
                INNER JOIN AD_ClientInfo ci ON (ci.C_Calendar_ID=y.C_Calendar_ID AND ci.AD_Client_ID=@AD_Client_ID)
                WHERE p.C_Period_ID=@C_Period_ID
                  AND p.IsActive='Y'
                  AND y.IsActive='Y'
                  AND EXISTS(SELECT 1 FROM C_PeriodControl pc WHERE pc.C_Period_ID=p.C_Period_ID AND pc.IsActive='Y' AND pc.PeriodStatus=@PeriodStatus)";

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "p", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@C_Period_ID", periodId),
                new SqlParameter("@PeriodStatus", PERIODSTATUS_Open)
            };

            DataSet ds = DB.ExecuteDataset(sql, parameters, null);
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) { return null; }

            DataRow row = ds.Tables[0].Rows[0];

            PeriodItem item = new PeriodItem();
            item.C_Period_ID = Util.GetValueOfInt(row["C_Period_ID"]);
            item.Name = Util.GetValueOfString(row["Period_Name"]);
            item.StartDate = Util.GetValueOfDateTime(row["Start_Date"]);
            item.EndDate = Util.GetValueOfDateTime(row["End_Date"]);

            return item;
        }

        // ─────────────────────────────────────────────────────────────────────
        // §2  Everything the card shows for one period
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The three status buckets and the bank accounts of one period. Two queries
        /// in total for the whole card: one aggregated pass over C_Payment for the
        /// buckets and the per-account figures, and one grouped read of
        /// C_BankAccountLine for the last statement dates.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="periodId">C_Period_ID selected by the user.</param>
        /// <returns>Populated <see cref="PeriodData"/> (never null).</returns>
        public PeriodData GetPeriodData(Ctx ctx, int periodId)
        {
            PeriodData result = new PeriodData();
            result.C_Period_ID = periodId;
            result.Buckets = new List<StatusBucket>();
            result.Accounts = new List<BankAccountRow>();

            if (ctx == null) { return result; }

            PeriodItem period = GetOpenPeriod(ctx, periodId);
            if (period == null)
            {
                result.ErrorCode = ERROR_NO_PERIOD;
                return result;
            }

            result.PeriodName = period.Name;
            result.PeriodEndDate = period.EndDate;

            BaseCurrency baseCurrency = GetBaseCurrency(ctx);
            result.BaseCurrencyIso = baseCurrency.Iso;
            result.BaseCurrencySymbol = baseCurrency.Symbol;
            result.BaseCurrencyPrecision = baseCurrency.Precision;

            result.Buckets = ReadBuckets(ctx, period, baseCurrency);
            result.Accounts = ReadAccounts(ctx, period, baseCurrency);

            return result;
        }

        /// <summary>
        /// The three status buckets, each with its record count and its total in the
        /// base currency, from a single aggregated pass over C_Payment.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="period">Validated period carrying the date bounds.</param>
        /// <param name="baseCurrency">Tenant base currency for the conversion.</param>
        /// <returns>Three buckets in display order (never null).</returns>
        private List<StatusBucket> ReadBuckets(Ctx ctx, PeriodItem period, BaseCurrency baseCurrency)
        {
            List<StatusBucket> buckets = new List<StatusBucket>();

            /* Flat SUM(CASE ...) aggregation rather than one query per bucket: three
               passes over the same rows would cost three times as much, and nested
               counting subqueries can exhaust the AddAccessSQL parser.
               The conversion appears once per bucket and each occurrence carries its
               OWN parameter name - a repeated name is ambiguous under positional
               binding, which is what the backend adapters use. */
            StringBuilder sql = new StringBuilder();
            sql.Append(@"
                SELECT SUM(CASE WHEN ").Append(BucketWhere(CATEGORY_RECONCILED)).Append(@" THEN 1 ELSE 0 END) AS Reconciled_Count,
                       SUM(CASE WHEN ").Append(BucketWhere(CATEGORY_RECONCILED)).Append(" THEN ").Append(BaseAmountExpr(baseCurrency, 1)).Append(@" ELSE 0 END) AS Reconciled_Amount,
                       SUM(CASE WHEN ").Append(BucketWhere(CATEGORY_UNRECONCILED)).Append(@" THEN 1 ELSE 0 END) AS Unreconciled_Count,
                       SUM(CASE WHEN ").Append(BucketWhere(CATEGORY_UNRECONCILED)).Append(" THEN ").Append(BaseAmountExpr(baseCurrency, 2)).Append(@" ELSE 0 END) AS Unreconciled_Amount,
                       SUM(CASE WHEN ").Append(BucketWhere(CATEGORY_INPROGRESS)).Append(@" THEN 1 ELSE 0 END) AS InProgress_Count,
                       SUM(CASE WHEN ").Append(BucketWhere(CATEGORY_INPROGRESS)).Append(" THEN ").Append(BaseAmountExpr(baseCurrency, 3)).Append(@" ELSE 0 END) AS InProgress_Amount
                FROM C_Payment p
                WHERE ");
            sql.Append(CommonPaymentWhere());

            string accessSql = MRole.GetDefault(ctx).AddAccessSQL(sql.ToString(), "p",
                MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            /* Bind values in the order they APPEAR: the three conversions sit in the
               SELECT list, ahead of the period bounds in the WHERE. */
            List<SqlParameter> parameters = new List<SqlParameter>();
            AddBaseCurrencyParameters(parameters, baseCurrency, 3);
            parameters.AddRange(PeriodBoundParameters(period));

            DataSet ds = DB.ExecuteDataset(accessSql, parameters.ToArray(), null);

            int reconciledCount = 0, unreconciledCount = 0, inProgressCount = 0;
            decimal reconciledAmount = 0, unreconciledAmount = 0, inProgressAmount = 0;

            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                DataRow row = ds.Tables[0].Rows[0];
                reconciledCount = Util.GetValueOfInt(row["Reconciled_Count"]);
                reconciledAmount = Util.GetValueOfDecimal(row["Reconciled_Amount"]);
                unreconciledCount = Util.GetValueOfInt(row["Unreconciled_Count"]);
                unreconciledAmount = Util.GetValueOfDecimal(row["Unreconciled_Amount"]);
                inProgressCount = Util.GetValueOfInt(row["InProgress_Count"]);
                inProgressAmount = Util.GetValueOfDecimal(row["InProgress_Amount"]);
            }

            buckets.Add(NewBucket(CATEGORY_RECONCILED, reconciledCount, reconciledAmount));
            buckets.Add(NewBucket(CATEGORY_UNRECONCILED, unreconciledCount, unreconciledAmount));
            buckets.Add(NewBucket(CATEGORY_INPROGRESS, inProgressCount, inProgressAmount));

            return buckets;
        }

        /// <summary>Builds one bucket entry.</summary>
        /// <param name="category">CATEGORY_* token.</param>
        /// <param name="count">Record count.</param>
        /// <param name="amount">Total in base currency.</param>
        /// <returns>The bucket.</returns>
        private StatusBucket NewBucket(string category, int count, decimal amount)
        {
            StatusBucket bucket = new StatusBucket();
            bucket.Category = category;
            bucket.RecordCount = count;
            bucket.BaseAmount = amount;
            return bucket;
        }

        /// <summary>
        /// The bank accounts the period's payments actually moved through - driven by
        /// payment activity, not by the list of configured accounts - with the date
        /// each was last reconciled and how far that leaves it behind the period end.
        /// Scoped to completed / closed payments, the same scope its detail modal
        /// uses, so an account listed here can never open an empty list.
        ///
        /// Two queries, deliberately: joining C_BankAccountLine into the payment
        /// aggregation would multiply every payment by the number of statement lines
        /// on its account and inflate both the count and the total. So the payments
        /// are grouped first, and the last statement date is then read for exactly
        /// the accounts that came back.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="period">Validated period carrying the date bounds.</param>
        /// <param name="baseCurrency">Tenant base currency for the conversion.</param>
        /// <returns>Accounts ordered by bank then account number (never null).</returns>
        private List<BankAccountRow> ReadAccounts(Ctx ctx, PeriodItem period, BaseCurrency baseCurrency)
        {
            List<BankAccountRow> rows = new List<BankAccountRow>();

            StringBuilder sql = new StringBuilder();
            sql.Append(@"
                SELECT ba.C_BankAccount_ID AS C_BankAccount_ID,
                       COALESCE(b.Name,N'') AS Bank_Name,
                       COALESCE(ba.AccountNo,N'') AS Account_No,
                       COUNT(1) AS Payment_Count,
                       SUM(CASE WHEN ").Append(BucketWhere(CATEGORY_UNRECONCILED)).Append(@" THEN 1 ELSE 0 END) AS Unreconciled_Count,
                       SUM(CASE WHEN ").Append(BucketWhere(CATEGORY_UNRECONCILED)).Append(" THEN ").Append(BaseAmountExpr(baseCurrency, 1)).Append(@" ELSE 0 END) AS Unreconciled_Amount
                FROM C_Payment p
                INNER JOIN C_BankAccount ba ON (ba.C_BankAccount_ID=p.C_BankAccount_ID)
                INNER JOIN C_Bank b ON (b.C_Bank_ID=ba.C_Bank_ID)
                WHERE ba.IsActive='Y'
                  AND b.IsActive='Y'
                  AND p.C_BankAccount_ID IS NOT NULL
                  AND " + DOCSTATUS_COMPLETED_CLOSED + @"
                  AND ");
            sql.Append(CommonPaymentWhere());

            string accessSql = MRole.GetDefault(ctx).AddAccessSQL(sql.ToString(), "p",
                MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            /* GROUP BY / ORDER BY go on AFTER the access SQL. */
            accessSql += " GROUP BY ba.C_BankAccount_ID,COALESCE(b.Name,N''),COALESCE(ba.AccountNo,N'')";
            accessSql += " ORDER BY COALESCE(b.Name,N''),COALESCE(ba.AccountNo,N'')";

            List<SqlParameter> parameters = new List<SqlParameter>();
            AddBaseCurrencyParameters(parameters, baseCurrency, 1);
            parameters.AddRange(PeriodBoundParameters(period));

            DataSet ds = DB.ExecuteDataset(accessSql, parameters.ToArray(), null);
            if (ds == null || ds.Tables.Count == 0) { return rows; }

            DataTable dt = ds.Tables[0];
            List<int> accountIds = new List<int>();

            for (int i = 0; i < dt.Rows.Count; i++)
            {
                DataRow dr = dt.Rows[i];

                BankAccountRow row = new BankAccountRow();
                row.C_BankAccount_ID = Util.GetValueOfInt(dr["C_BankAccount_ID"]);
                row.BankName = Util.GetValueOfString(dr["Bank_Name"]);
                row.AccountNo = Util.GetValueOfString(dr["Account_No"]);
                row.PaymentCount = Util.GetValueOfInt(dr["Payment_Count"]);
                row.UnreconciledCount = Util.GetValueOfInt(dr["Unreconciled_Count"]);
                row.UnreconciledAmount = Util.GetValueOfDecimal(dr["Unreconciled_Amount"]);
                row.DaysBehind = -1;                    // -1 = never reconciled

                if (row.C_BankAccount_ID > 0)
                {
                    accountIds.Add(row.C_BankAccount_ID);
                    rows.Add(row);
                }
            }

            if (rows.Count == 0) { return rows; }

            /* Last reconciliation date per account, then the day gap in C# - the
               comparison date is the SELECTED PERIOD END, never today. */
            Dictionary<int, DateTime> lastDates = ReadLastStatementDates(ctx, accountIds);
            DateTime periodEnd = period.EndDate.HasValue ? period.EndDate.Value.Date : DateTime.Now.Date;

            for (int i = 0; i < rows.Count; i++)
            {
                BankAccountRow row = rows[i];
                if (!lastDates.ContainsKey(row.C_BankAccount_ID)) { continue; }

                DateTime last = lastDates[row.C_BankAccount_ID].Date;
                row.LastReconciledDate = last;

                double days = (periodEnd - last).TotalDays;
                row.DaysBehind = days > 0 ? (int)Math.Floor(days) : 0;
            }

            return rows;
        }

        /// <summary>
        /// Latest C_BankAccountLine.StatementDate per bank account, for the accounts
        /// the period's payments used. One grouped query - never one per account.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="accountIds">Bank account ids resolved from payment activity.</param>
        /// <returns>Account id -> last statement date (never null; missing = never reconciled).</returns>
        private Dictionary<int, DateTime> ReadLastStatementDates(Ctx ctx, List<int> accountIds)
        {
            Dictionary<int, DateTime> result = new Dictionary<int, DateTime>();
            if (accountIds == null || accountIds.Count == 0) { return result; }

            /* The id list is built from the previous query's own integers, so inlining
               it carries no injection risk - and an IN list cannot be bound as a
               single parameter on either backend. */
            StringBuilder ids = new StringBuilder();
            for (int i = 0; i < accountIds.Count; i++)
            {
                if (ids.Length > 0) { ids.Append(","); }
                ids.Append(accountIds[i].ToString(CultureInfo.InvariantCulture));
            }

            string sql = @"
                SELECT bal.C_BankAccount_ID AS C_BankAccount_ID,
                       MAX(bal.StatementDate) AS Last_Statement_Date
                FROM C_BankAccountLine bal
                WHERE bal.IsActive='Y'
                  AND bal.C_BankAccount_ID IN (" + ids.ToString() + ")";

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "bal", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            sql += " GROUP BY bal.C_BankAccount_ID";

            DataSet ds = DB.ExecuteDataset(sql, null, null);
            if (ds == null || ds.Tables.Count == 0) { return result; }

            DataTable dt = ds.Tables[0];
            for (int i = 0; i < dt.Rows.Count; i++)
            {
                int accountId = Util.GetValueOfInt(dt.Rows[i]["C_BankAccount_ID"]);
                DateTime? last = Util.GetValueOfDateTime(dt.Rows[i]["Last_Statement_Date"]);

                if (accountId > 0 && last.HasValue && !result.ContainsKey(accountId))
                {
                    result.Add(accountId, last.Value);
                }
            }

            return result;
        }

        // ─────────────────────────────────────────────────────────────────────
        // §3  Detail (server-side paging)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// One page of the payments behind a status bucket, or behind one bank
        /// account, plus the total row count so the client can page without holding
        /// the whole set. Detail rows are only ever read through this method - the
        /// card itself never loads them.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="periodId">C_Period_ID selected by the user.</param>
        /// <param name="category">CATEGORY_* token; ACCOUNT needs bankAccountId.</param>
        /// <param name="bankAccountId">C_BankAccount_ID for the ACCOUNT category.</param>
        /// <param name="pageNo">1-based page number.</param>
        /// <param name="pageSize">Rows per page (clamped server-side).</param>
        /// <returns>Populated <see cref="PaymentPage"/> (never null).</returns>
        public PaymentPage GetPayments(Ctx ctx, int periodId, string category, int bankAccountId,
            int pageNo, int pageSize)
        {
            PaymentPage result = new PaymentPage();
            result.Rows = new List<PaymentRow>();
            result.Category = category;
            result.C_Period_ID = periodId;
            result.C_BankAccount_ID = bankAccountId;

            if (ctx == null) { return result; }

            bool isAccount = CATEGORY_ACCOUNT.Equals(category);
            if (isAccount && bankAccountId <= 0)
            {
                result.ErrorCode = ERROR_INVALID_REQUEST;
                return result;
            }

            /* The account view lists that account's reconciliation work, so it is
               scoped to completed / closed documents like the two buckets it sits
               beside - it is simply not filtered by reconciliation state. */
            string categoryWhere = isAccount
                ? "p.C_BankAccount_ID=@C_BankAccount_ID AND " + DOCSTATUS_COMPLETED_CLOSED
                : BucketWhere(category);
            if (categoryWhere == null)
            {
                result.ErrorCode = ERROR_INVALID_REQUEST;
                return result;
            }

            PeriodItem period = GetOpenPeriod(ctx, periodId);
            if (period == null)
            {
                result.ErrorCode = ERROR_NO_PERIOD;
                return result;
            }

            result.PeriodName = period.Name;

            if (pageSize < PAGESIZE_MIN || pageSize > PAGESIZE_MAX) { pageSize = PAGESIZE_DEFAULT; }
            if (pageNo < 1) { pageNo = 1; }

            result.PageSize = pageSize;

            /* COUNT first: an out-of-range page (the set shrank since the user last
               paged) is clamped before the row query runs, so the modal can never
               land on an empty page while records still exist. */
            result.Total = CountPayments(ctx, period, categoryWhere, isAccount, bankAccountId);
            if (result.Total == 0)
            {
                result.PageNo = 1;
                return result;
            }

            int totalPages = (result.Total + pageSize - 1) / pageSize;
            if (pageNo > totalPages) { pageNo = totalPages; }
            result.PageNo = pageNo;

            result.Rows = ReadPayments(ctx, period, categoryWhere, isAccount, bankAccountId,
                pageNo, pageSize);

            return result;
        }

        /// <summary>
        /// Total number of payments behind one bucket / account in one period.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="period">Validated period carrying the date bounds.</param>
        /// <param name="categoryWhere">Predicate from <see cref="BucketWhere"/>.</param>
        /// <param name="isAccount">True when the predicate binds @C_BankAccount_ID.</param>
        /// <param name="bankAccountId">Bank account for the ACCOUNT category.</param>
        /// <returns>Row count, 0 when nothing matches.</returns>
        private int CountPayments(Ctx ctx, PeriodItem period, string categoryWhere,
            bool isAccount, int bankAccountId)
        {
            StringBuilder sql = new StringBuilder();
            sql.Append(@"
                SELECT COUNT(1) AS Record_Count
                FROM C_Payment p
                WHERE ");
            sql.Append(categoryWhere).Append(" AND ").Append(CommonPaymentWhere());

            string accessSql = MRole.GetDefault(ctx).AddAccessSQL(sql.ToString(), "p",
                MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            List<SqlParameter> parameters = new List<SqlParameter>();
            if (isAccount) { parameters.Add(new SqlParameter("@C_BankAccount_ID", bankAccountId)); }
            parameters.AddRange(PeriodBoundParameters(period));

            DataSet ds = DB.ExecuteDataset(accessSql, parameters.ToArray(), null);
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) { return 0; }

            return Util.GetValueOfInt(ds.Tables[0].Rows[0]["Record_Count"]);
        }

        /// <summary>
        /// One page of payment rows, newest accounting date first. The account view
        /// puts the unreconciled ones first - they are the reason to open it.
        ///
        /// Amounts here are NOT converted: a detail list reports each payment in the
        /// currency it was actually made in. The base-currency view is the card's
        /// job, where figures of different currencies have to be added together.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="period">Validated period carrying the date bounds.</param>
        /// <param name="categoryWhere">Predicate from <see cref="BucketWhere"/>.</param>
        /// <param name="isAccount">True when the predicate binds @C_BankAccount_ID.</param>
        /// <param name="bankAccountId">Bank account for the ACCOUNT category.</param>
        /// <param name="pageNo">1-based page number (already clamped).</param>
        /// <param name="pageSize">Rows per page (already clamped).</param>
        /// <returns>Materialised rows (never null).</returns>
        private List<PaymentRow> ReadPayments(Ctx ctx, PeriodItem period, string categoryWhere,
            bool isAccount, int bankAccountId, int pageNo, int pageSize)
        {
            List<PaymentRow> rows = new List<PaymentRow>();

            StringBuilder sql = new StringBuilder();
            sql.Append(@"
                SELECT p.C_Payment_ID AS C_Payment_ID,
                       p.DocumentNo AS Document_No,
                       p.DateAcct AS Date_Acct,
                       p.DocStatus AS Doc_Status,
                       p.IsReceipt AS Is_Receipt,
                       COALESCE(p.IsReconciled,'N') AS Is_Reconciled,
                       p.C_BPartner_ID AS C_BPartner_ID,
                       COALESCE(bp.Name,N'') AS Business_Partner_Name,
                       p.C_BankAccount_ID AS C_BankAccount_ID,
                       COALESCE(b.Name,N'') AS Bank_Name,
                       COALESCE(ba.AccountNo,N'') AS Account_No,
                       p.C_Currency_ID AS C_Currency_ID,
                       cur.ISO_Code AS Currency_Iso,
                       COALESCE(cur.CurSymbol,cur.ISO_Code) AS Currency_Symbol,
                       cur.StdPrecision AS Currency_Precision,
                       COALESCE(p.PayAmt,0) AS Pay_Amt
                FROM C_Payment p
                INNER JOIN C_Currency cur ON (cur.C_Currency_ID=p.C_Currency_ID)
                INNER JOIN C_BankAccount ba ON (ba.C_BankAccount_ID=p.C_BankAccount_ID)
                INNER JOIN C_Bank b ON (b.C_Bank_ID=ba.C_Bank_ID)
                LEFT OUTER JOIN C_BPartner bp ON (bp.C_BPartner_ID=p.C_BPartner_ID)
                WHERE ");
            sql.Append(categoryWhere).Append(" AND ").Append(CommonPaymentWhere());

            string accessSql = MRole.GetDefault(ctx).AddAccessSQL(sql.ToString(), "p",
                MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            /* ORDER BY and the paging suffix go on AFTER the access SQL. */
            accessSql += isAccount
                ? " ORDER BY COALESCE(p.IsReconciled,'N'),p.DateAcct DESC,p.DocumentNo DESC,p.C_Payment_ID DESC"
                : " ORDER BY p.DateAcct DESC,p.DocumentNo DESC,p.C_Payment_ID DESC";
            accessSql += PagingSuffix(pageSize, (pageNo - 1) * pageSize);

            /* Appearance order: the account predicate opens the WHERE, the period
               bounds close it. No conversion here - the detail list reports each
               payment in its OWN currency. */
            List<SqlParameter> parameters = new List<SqlParameter>();
            if (isAccount) { parameters.Add(new SqlParameter("@C_BankAccount_ID", bankAccountId)); }
            parameters.AddRange(PeriodBoundParameters(period));

            DataSet ds = DB.ExecuteDataset(accessSql, parameters.ToArray(), null);
            if (ds == null || ds.Tables.Count == 0) { return rows; }

            DataTable dt = ds.Tables[0];
            for (int i = 0; i < dt.Rows.Count; i++)
            {
                DataRow dr = dt.Rows[i];

                PaymentRow row = new PaymentRow();
                row.C_Payment_ID = Util.GetValueOfInt(dr["C_Payment_ID"]);
                row.DocumentNo = Util.GetValueOfString(dr["Document_No"]);
                row.DateAcct = Util.GetValueOfDateTime(dr["Date_Acct"]);
                row.DocStatus = Util.GetValueOfString(dr["Doc_Status"]);
                row.IsReceipt = "Y".Equals(Util.GetValueOfString(dr["Is_Receipt"]));
                row.IsReconciled = "Y".Equals(Util.GetValueOfString(dr["Is_Reconciled"]));
                row.C_BPartner_ID = Util.GetValueOfInt(dr["C_BPartner_ID"]);
                row.BusinessPartnerName = Util.GetValueOfString(dr["Business_Partner_Name"]);
                row.C_BankAccount_ID = Util.GetValueOfInt(dr["C_BankAccount_ID"]);
                row.BankName = Util.GetValueOfString(dr["Bank_Name"]);
                row.AccountNo = Util.GetValueOfString(dr["Account_No"]);
                row.C_Currency_ID = Util.GetValueOfInt(dr["C_Currency_ID"]);
                row.CurrencyIso = Util.GetValueOfString(dr["Currency_Iso"]);
                row.CurrencySymbol = Util.GetValueOfString(dr["Currency_Symbol"]);
                row.CurrencyPrecision = Util.GetValueOfInt(dr["Currency_Precision"]);
                row.PayAmt = Util.GetValueOfDecimal(dr["Pay_Amt"]);

                rows.Add(row);
            }

            return rows;
        }

        // ─────────────────────────────────────────────────────────────────────
        // §4  Query building blocks
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The predicate every reconciliation query shares: active, and accounted
        /// inside the selected period. DateAcct - not DateTrx - and the bounds are
        /// the period's own dates rather than any month or year function.
        /// </summary>
        /// <returns>WHERE fragment binding @StartDate and @EndDate.</returns>
        private string CommonPaymentWhere()
        {
            /* Compare date values only. Oracle DATE always carries a time part
               (TRUNC drops it); PostgreSQL needs an explicit CAST when the column is
               materialised as a timestamp. Only the COLUMN side is normalised - the
               bind values are already midnight, and casting a bind variable leaves
               its type undetermined on PostgreSQL. */
            string dateCondition = DB.IsOracle()
                ? "TRUNC(p.DateAcct)>=@StartDate AND TRUNC(p.DateAcct)<=@EndDate"
                : "CAST(p.DateAcct AS DATE)>=@StartDate AND CAST(p.DateAcct AS DATE)<=@EndDate";

            return "p.IsActive='Y' AND " + dateCondition;
        }

        /// <summary>
        /// The predicate that isolates one status bucket.
        ///
        /// The bounce half of "in progress / bounced" is NOT invented here: it is the
        /// condition this product already ships (VA009 execution status Bounced or
        /// Rejected, excluding voided and reversed documents - see
        /// VAS BouncedChequesController), and it is only added when the VA009 column
        /// actually exists in this environment. Without the module the bucket is
        /// simply "in progress".
        /// </summary>
        /// <param name="category">One of the CATEGORY_* tokens.</param>
        /// <returns>WHERE fragment, or null when the token is not recognised.</returns>
        private string BucketWhere(string category)
        {
            /* Both reconciliation buckets are scoped to completed / closed documents:
               they are two halves of the same work queue, so a draft or voided
               payment must not swell one of them and not the other. */
            if (CATEGORY_RECONCILED.Equals(category))
            {
                return "COALESCE(p.IsReconciled,'N')='Y' AND " + DOCSTATUS_COMPLETED_CLOSED;
            }

            if (CATEGORY_UNRECONCILED.Equals(category))
            {
                return "COALESCE(p.IsReconciled,'N')='N' AND " + DOCSTATUS_COMPLETED_CLOSED;
            }

            if (CATEGORY_INPROGRESS.Equals(category))
            {
                if (!HasExecutionStatusColumn())
                {
                    return "p.DocStatus='IP'";
                }

                return "(p.DocStatus='IP' OR (p." + COLUMNNAME_ExecutionStatus + " IN ('"
                    + X_C_Payment.VA009_EXECUTIONSTATUS_Bounced + "','"
                    + X_C_Payment.VA009_EXECUTIONSTATUS_Rejected
                    + "') AND p.DocStatus NOT IN ('VO','RE')))";
            }

            return null;
        }

        /// <summary>
        /// The payment amount expressed in the tenant's base currency: converted
        /// through the standard function at the payment's accounting date, or taken
        /// as-is when the payment is already in that currency.
        /// </summary>
        /// <param name="baseCurrency">Tenant base currency.</param>
        /// <param name="ordinal">1-based occurrence number - each occurrence carries
        /// its own parameter name, because a repeated name is ambiguous under the
        /// positional binding the backend adapters use.</param>
        /// <returns>SQL expression, or a literal 0 when no schema currency resolved.</returns>
        private string BaseAmountExpr(BaseCurrency baseCurrency, int ordinal)
        {
            if (baseCurrency.C_Currency_ID <= 0) { return "0"; }

            string p1 = "@BaseCurrencyId" + ordinal.ToString(CultureInfo.InvariantCulture) + "A";
            string p2 = "@BaseCurrencyId" + ordinal.ToString(CultureInfo.InvariantCulture) + "B";

            return "CASE WHEN p.C_Currency_ID=" + p1 + " THEN ABS(COALESCE(p.PayAmt,0))"
                 + " ELSE ABS(COALESCE(currencyConvert(COALESCE(p.PayAmt,0),p.C_Currency_ID," + p2
                 + ",p.DateAcct,p.C_ConversionType_ID,p.AD_Client_ID,p.AD_Org_ID),0)) END";
        }

        /// <summary>
        /// Binds the currency id behind every <see cref="BaseAmountExpr"/> occurrence,
        /// in the order the occurrences appear in the statement.
        /// </summary>
        /// <param name="parameters">List being built.</param>
        /// <param name="baseCurrency">Tenant base currency.</param>
        /// <param name="occurrences">How many times the expression was emitted.</param>
        private void AddBaseCurrencyParameters(List<SqlParameter> parameters, BaseCurrency baseCurrency,
            int occurrences)
        {
            if (baseCurrency.C_Currency_ID <= 0) { return; }

            for (int i = 1; i <= occurrences; i++)
            {
                string suffix = i.ToString(CultureInfo.InvariantCulture);
                parameters.Add(new SqlParameter("@BaseCurrencyId" + suffix + "A", baseCurrency.C_Currency_ID));
                parameters.Add(new SqlParameter("@BaseCurrencyId" + suffix + "B", baseCurrency.C_Currency_ID));
            }
        }

        /// <summary>
        /// The period bounds every payment query binds. Date parts only, so a payment
        /// stamped with a time still falls inside its period.
        /// </summary>
        /// <param name="period">Validated period.</param>
        /// <returns>Bind values for @StartDate and @EndDate.</returns>
        private SqlParameter[] PeriodBoundParameters(PeriodItem period)
        {
            DateTime from = period.StartDate.HasValue ? period.StartDate.Value.Date : DateTime.MinValue;
            DateTime to = period.EndDate.HasValue ? period.EndDate.Value.Date : DateTime.MaxValue.Date;

            return new SqlParameter[]
            {
                new SqlParameter("@StartDate", from),
                new SqlParameter("@EndDate", to)
            };
        }

        /// <summary>
        /// Database-specific paging suffix: OFFSET / FETCH on Oracle, LIMIT / OFFSET
        /// elsewhere. Both values are server-clamped integers, never client text.
        /// </summary>
        /// <param name="pageSize">Rows to fetch.</param>
        /// <param name="offset">Rows to skip.</param>
        /// <returns>Paging clause.</returns>
        private string PagingSuffix(int pageSize, int offset)
        {
            if (pageSize <= 0) { pageSize = PAGESIZE_DEFAULT; }
            if (offset < 0) { offset = 0; }

            if (DB.IsOracle())
            {
                return " OFFSET " + offset + " ROWS FETCH NEXT " + pageSize + " ROWS ONLY";
            }
            return " LIMIT " + pageSize + " OFFSET " + offset;
        }

        /// <summary>
        /// True when C_Payment carries the VA009 execution-status column in this
        /// environment. The module is optional, so the bounce half of the third
        /// bucket has to be resolved from the dictionary rather than assumed;
        /// memoised per app domain because dictionary metadata does not change at
        /// runtime.
        /// </summary>
        /// <returns>True when the column exists and is active.</returns>
        private bool HasExecutionStatusColumn()
        {
            if (_hasExecutionStatus > 0) { return true; }
            if (_hasExecutionStatus < 0) { return false; }

            string sql = @"
                SELECT COUNT(1) AS Column_Count
                FROM AD_Table t
                INNER JOIN AD_Column c ON (c.AD_Table_ID=t.AD_Table_ID)
                WHERE t.TableName=@TableName
                  AND c.ColumnName=@ColumnName
                  AND t.IsActive='Y'
                  AND c.IsActive='Y'";

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@TableName", TABLENAME_Payment),
                new SqlParameter("@ColumnName", COLUMNNAME_ExecutionStatus)
            };

            bool present = Util.GetValueOfInt(DB.ExecuteScalar(sql, parameters, null)) > 0;
            _hasExecutionStatus = present ? 1 : -1;

            if (!present)
            {
                Log.Info("VAS_200_BankReconcilation: " + TABLENAME_Payment + "."
                    + COLUMNNAME_ExecutionStatus + " is not present - the in-progress bucket"
                    + " reports document status IP only.");
            }

            return present;
        }

        /// <summary>
        /// The tenant's base currency: the currency of the primary accounting schema
        /// (AD_ClientInfo.C_AcctSchema1_ID). Reads only system / reference tables
        /// scoped to the session client, so no MRole predicate is applied - the same
        /// treatment the sibling KPI widgets give this lookup.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <returns>Populated <see cref="BaseCurrency"/>; C_Currency_ID is 0 when the
        /// tenant has no primary accounting schema.</returns>
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
        // §5  Contracts
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>One selectable open accounting period of the primary calendar.</summary>
        public class PeriodItem
        {
            public int C_Period_ID { get; set; }
            public string Name { get; set; }

            /// <summary>Inclusive lower bound applied to C_Payment.DateAcct.</summary>
            public DateTime? StartDate { get; set; }

            /// <summary>Inclusive upper bound applied to C_Payment.DateAcct, and the
            /// date every "days behind" is measured against.</summary>
            public DateTime? EndDate { get; set; }

            public int C_Year_ID { get; set; }
            public string FiscalYear { get; set; }
            public int C_Calendar_ID { get; set; }
        }

        /// <summary>One reconciliation status bucket.</summary>
        public class StatusBucket
        {
            /// <summary>CATEGORY_* token; the client resolves the label.</summary>
            public string Category { get; set; }

            public int RecordCount { get; set; }

            /// <summary>Total in the tenant's base currency.</summary>
            public decimal BaseAmount { get; set; }
        }

        /// <summary>One bank account the period's payments moved through.</summary>
        public class BankAccountRow
        {
            public int C_BankAccount_ID { get; set; }
            public string BankName { get; set; }

            /// <summary>Full account number; the client shows only its last digits.</summary>
            public string AccountNo { get; set; }

            /// <summary>Payments of this account inside the period.</summary>
            public int PaymentCount { get; set; }

            public int UnreconciledCount { get; set; }

            /// <summary>Unreconciled total in the tenant's base currency.</summary>
            public decimal UnreconciledAmount { get; set; }

            /// <summary>Latest C_BankAccountLine.StatementDate; null = never reconciled.</summary>
            public DateTime? LastReconciledDate { get; set; }

            /// <summary>Whole days from the last statement date to the SELECTED PERIOD
            /// END (never to today); 0 when up to date, -1 when never reconciled.</summary>
            public int DaysBehind { get; set; }
        }

        /// <summary>Everything the card shows for one period.</summary>
        public class PeriodData
        {
            public int C_Period_ID { get; set; }
            public string PeriodName { get; set; }

            /// <summary>The date "days behind" is measured against.</summary>
            public DateTime? PeriodEndDate { get; set; }

            public List<StatusBucket> Buckets { get; set; }
            public List<BankAccountRow> Accounts { get; set; }

            public string BaseCurrencyIso { get; set; }
            public string BaseCurrencySymbol { get; set; }
            public int BaseCurrencyPrecision { get; set; }

            /// <summary>One of the ERROR_* tokens; the client resolves the label.</summary>
            public string ErrorCode { get; set; }
        }

        /// <summary>Period list, default selection and its data, in one payload.</summary>
        public class ReconciliationBootstrap
        {
            public List<PeriodItem> Periods { get; set; }

            public int C_Period_ID { get; set; }
            public string PeriodName { get; set; }

            public PeriodData Data { get; set; }
        }

        /// <summary>One payment behind a bucket or a bank account.</summary>
        public class PaymentRow
        {
            public int C_Payment_ID { get; set; }
            public string DocumentNo { get; set; }
            public DateTime? DateAcct { get; set; }

            /// <summary>Stored code; kept only so the row can be keyed by it.</summary>
            public string DocStatus { get; set; }

            /// <summary>Decides which standard window the client zooms to: a receipt
            /// and a vendor payment are different screens.</summary>
            public bool IsReceipt { get; set; }

            public bool IsReconciled { get; set; }

            public int C_BPartner_ID { get; set; }
            public string BusinessPartnerName { get; set; }

            public int C_BankAccount_ID { get; set; }
            public string BankName { get; set; }
            public string AccountNo { get; set; }

            public int C_Currency_ID { get; set; }
            public string CurrencyIso { get; set; }
            public string CurrencySymbol { get; set; }
            public int CurrencyPrecision { get; set; }

            /// <summary>C_Payment.PayAmt in the payment's own currency - the detail
            /// list shows each payment as it was made, not converted.</summary>
            public decimal PayAmt { get; set; }
        }

        /// <summary>One page of detail plus the paging state.</summary>
        public class PaymentPage
        {
            public string Category { get; set; }
            public int C_Period_ID { get; set; }
            public int C_BankAccount_ID { get; set; }
            public string PeriodName { get; set; }

            public List<PaymentRow> Rows { get; set; }

            public int Total { get; set; }
            public int PageNo { get; set; }
            public int PageSize { get; set; }

            /// <summary>One of the ERROR_* tokens; the client resolves the label.</summary>
            public string ErrorCode { get; set; }
        }

        /// <summary>The tenant's primary accounting-schema currency.</summary>
        private class BaseCurrency
        {
            public int C_Currency_ID { get; set; }
            public string Iso { get; set; }
            public string Symbol { get; set; }
            public int Precision { get; set; }
        }
    }
}
