/******************************************************
 * Module Name    : VASLogic
 * Purpose        : Largest Budget Variances dashboard widget data
 * chronological  : Development
 * Created Date   : 2026-09-08
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
    /// Module Name : VAS_254_LargestBudgetVariance
    /// Purpose     : Backs the VAS_254_LargestBudgetVarianceWidget dashboard widget - the
    ///               accounts whose approved budget and posted actual are furthest apart,
    ///               in BOTH directions:
    ///
    ///                 Budget    Fact_Acct rows with PostingType 'B', summed per account.
    ///                 Actual    Fact_Acct rows with PostingType 'A', summed per account.
    ///                 Variance  Budget - Actual. POSITIVE is favourable (the account came
    ///                           in under its budget), NEGATIVE is unfavourable (it went
    ///                           over). The client prints the sign explicitly, so the
    ///                           reading never rests on colour alone.
    ///                 Ranking   ABS(Variance) descending, so a large underspend and a
    ///                           large overrun sit next to each other - the card is about
    ///                           the SIZE of the gap, not its direction.
    ///
    ///               BOTH SIDES COME FROM Fact_Acct, compared at Fact_Acct.Account_ID.
    ///               There is deliberately no separate budget-line matching model here:
    ///               this is not the unbudgeted-actuals card (VAS_256), which asks whether
    ///               a budget EXISTS at a dimension grain. This one asks how far apart two
    ///               postings of the same account are, so the natural account is the whole
    ///               of the comparison.
    ///
    ///               NOTHING IS INFERRED. No burn rate, no run rate, no projected
    ///               exhaustion date, no elapsed-time proration - all of those need an
    ///               assumed spending pattern, and this card deliberately has none. It is
    ///               budget minus actual and nothing else.
    ///
    ///               THE SIGN IS CORRECTED PER ACCOUNT TYPE. Fact_Acct stores debits and
    ///               credits, not "amounts": revenue, liability and owner's equity are
    ///               CREDIT-natural and are read as AmtAcctCr - AmtAcctDr, while asset,
    ///               expense and memo are DEBIT-natural and are read as AmtAcctDr -
    ///               AmtAcctCr. Without this a revenue budget reports as a negative actual
    ///               and every revenue account ranks as a huge false variance. The
    ///               correction is applied in C# from C_ElementValue.AccountType rather
    ///               than as a CASE inside the aggregate, so the SQL stays one flat
    ///               SUM(CASE WHEN ...) per posting type and the access parser meets
    ///               nothing nested.
    ///
    ///               ONE SCAN, TWO POSTING TYPES. Budget and actual are summed in the SAME
    ///               grouped query with SUM(CASE WHEN PostingType = ... ) rather than in
    ///               two queries joined together: a flat aggregate is what this codebase's
    ///               access-SQL parser copes with, and a full outer join between two
    ///               derived sets would drop every account that has one side and not the
    ///               other - which is exactly the largest variance there is.
    ///
    ///               PRIMARY ACCOUNTING SCHEMA ONLY, on both sides. A budget posted in a
    ///               secondary schema must not be compared against an actual in the
    ///               primary one, so the schema is an equality on the single scan that
    ///               produces both figures. AmtAcct* is already stated in that schema's
    ///               currency, so there is no currencyConvert call anywhere in this model.
    ///
    ///               FINANCIAL YEAR. AD_ClientInfo.C_Calendar_ID -> C_Year -> C_Period. The
    ///               accounting date window is MIN(StartDate) / MAX(EndDate) over the
    ///               ACTIVE periods of the selected C_Year_ID - never January to December,
    ///               and never derived from the calendar month.
    ///
    ///               BALANCING ACCOUNTS ARE EXCLUDED. A budget journal is balanced, so the
    ///               offsetting side lands on a technical account (the schema's suspense
    ///               balancing, currency balancing or commitment offset account). Left in,
    ///               that account carries the mirror image of every budget posted and
    ///               ranks at or near the top of a card about the largest variances, which
    ///               tells the reader nothing. The excluded ids are resolved from
    ///               C_AcctSchema_GL through C_ValidCombination, and each column is
    ///               confirmed against AD_Column before it is named - several are optional
    ///               in this schema.
    ///
    ///               MRole row-level security is applied to Fact_Acct fa on the rows query
    ///               and to C_Year y on the year list. The joined C_ElementValue rows are a
    ///               reference lookup and inherit the parent's filter; C_AcctSchema_GL,
    ///               C_ValidCombination and AD_Column are configuration and dictionary
    ///               reads. GROUP BY / HAVING / ORDER BY are appended AFTER AddAccessSQL so
    ///               its FROM-clause parser never meets a trailing clause, and every join
    ///               ON is a plain equality so it never meets a function call either.
    ///               Compatible with PostgreSQL and Oracle.
    /// Chronological development:
    ///   VAI154      2026-09-08 Created
    /// </summary>
    public class VAS_254_LargestBudgetVarianceModel
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_254_LargestBudgetVarianceModel).FullName);

        /* Fact_Acct.PostingType stored codes. Stored codes are compared bare - never with
           an N prefix, which would force a Unicode comparison against a VARCHAR column. */
        private const string POSTINGTYPE_Actual = "A";
        private const string POSTINGTYPE_Budget = "B";

        /* C_ElementValue.AccountType stored codes. The three CREDIT-natural types; every
           other type (asset, expense, memo) is debit-natural. */
        private const string ACCOUNTTYPE_Revenue = "R";
        private const string ACCOUNTTYPE_Liability = "L";
        private const string ACCOUNTTYPE_OwnersEquity = "O";

        /* Error tokens exchanged with the client; the client resolves the label from
           AD_Message, so no display text is produced here. */
        public const string ERROR_NO_CALENDAR = "NOCALENDAR";
        public const string ERROR_NO_ACCTSCHEMA = "NOACCTSCHEMA";
        public const string ERROR_NO_YEAR = "NOYEAR";

        /* Paging. The layout shows three rows at 1280px; a taller cell may ask for more
           and a short one for fewer, but never outside these bounds. */
        public const int DEFAULT_PageSize = 3;
        private const int MIN_PageSize = 1;
        private const int MAX_PageSize = 12;

        /* The dictionary table the balancing / offset settings live on, and the columns
           that name them. Every one is confirmed against AD_Column before it is used:
           the commitment pair in particular is optional in this schema. */
        private const string TABLE_ACCTSCHEMA_GL = "C_AcctSchema_GL";
        private static readonly string[] OFFSET_COLUMNS = new string[]
        {
            "SuspenseBalancing_Acct",
            "CurrencyBalancing_Acct",
            "CommitmentOffset_Acct",
            "CommitmentOffsetSales_Acct"
        };

        // ─────────────────────────────────────────────────────────────────────
        // §1  Entry point
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Everything the card shows in one round trip: the selectable financial years, the
        /// year actually used, the accounting-schema currency and the requested page of
        /// accounts ranked by absolute variance.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="yearId">C_Year_ID the user selected, or 0 to default to the
        /// financial year containing today.</param>
        /// <param name="pageNo">1-based page; clamped to the available range.</param>
        /// <param name="pageSize">Rows per page; clamped to [1,12].</param>
        /// <returns>Populated <see cref="VarianceResult"/> (never null). Loaded is false only
        /// when there is no context or the tenant is not configured; a year with nothing
        /// budgeted or posted returns Loaded=true and an empty page, because "no variances"
        /// is a real answer rather than an error.</returns>
        public VarianceResult GetRows(Ctx ctx, int yearId, int pageNo, int pageSize)
        {
            VarianceResult result = new VarianceResult();
            result.Rows = new List<VarianceRow>();
            result.Years = new List<YearOption>();
            result.PageSize = ClampPageSize(pageSize);
            result.Page = pageNo < 1 ? 1 : pageNo;

            if (ctx == null) { result.Page = 1; return result; }

            /* The accounting context is a CONFIGURATION precondition, not a filter: without
               a primary calendar there is no year list to build, and without a primary
               accounting schema there is no ledger to read. Neither is silently replaced by
               "some other" calendar or schema. */
            AcctContext acct = GetAcctContext(ctx);
            result.Schema = acct;

            if (acct.C_Calendar_ID <= 0)
            {
                result.ErrorCode = ERROR_NO_CALENDAR;
                Log.Log(Level.WARNING, "VAS_254_LargestBudgetVariance: AD_ClientInfo.C_Calendar_ID not configured for AD_Client_ID="
                    + ctx.GetAD_Client_ID());
                return result;
            }

            if (acct.C_AcctSchema_ID <= 0)
            {
                result.ErrorCode = ERROR_NO_ACCTSCHEMA;
                Log.Log(Level.WARNING, "VAS_254_LargestBudgetVariance: AD_ClientInfo.C_AcctSchema1_ID not configured for AD_Client_ID="
                    + ctx.GetAD_Client_ID());
                return result;
            }

            /* The year list travels with the rows, so the widget is one round trip on load
               and the pill can never name a year the rows were not read for. */
            result.Years = GetYears(ctx, acct.C_Calendar_ID);
            if (result.Years.Count == 0)
            {
                result.ErrorCode = ERROR_NO_YEAR;
                result.Loaded = true;
                return result;
            }

            YearOption year = PickYear(result.Years, yearId, DateTime.Now.Date);
            result.C_Year_ID = year.C_Year_ID;
            result.FiscalYear = year.FiscalYear;
            result.StartDate = ToIsoDate(year.StartDate);
            result.EndDate = ToIsoDate(year.EndDate);

            ReadRows(ctx, acct, year, result);

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
        // §2  Accounting context and the financial-year list
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The tenant's accounting context: the primary calendar, the PRIMARY accounting
        /// schema and the currency every figure on the card is expressed in. Both ids come
        /// from AD_ClientInfo - never from a search over all calendars or all schemas.
        ///
        /// Reads only client-scoped configuration and reference tables, so no MRole
        /// predicate is applied - the same treatment the sibling accounting widgets give
        /// this lookup.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <returns>Populated <see cref="AcctContext"/>; the ids are 0 when the tenant has
        /// no primary calendar / accounting schema.</returns>
        public AcctContext GetAcctContext(Ctx ctx)
        {
            AcctContext result = new AcctContext();
            result.Precision = 2;

            if (ctx == null) { return result; }

            /* CurSymbol first, ISO_Code as the fallback - the card prints the symbol
               directly against the amount, and only falls back to the code when the
               currency has no symbol configured. */
            string sql = @"
                SELECT ci.C_Calendar_ID AS C_Calendar_ID,
                       ci.C_AcctSchema1_ID AS C_AcctSchema_ID,
                       acs.Name AS Acct_Schema_Name,
                       acs.C_Currency_ID AS C_Currency_ID,
                       cur.ISO_Code AS Currency_Iso,
                       COALESCE(cur.CurSymbol,cur.ISO_Code) AS Currency_Symbol,
                       cur.StdPrecision AS Std_Precision
                FROM AD_ClientInfo ci
                INNER JOIN C_AcctSchema acs ON (acs.C_AcctSchema_ID=ci.C_AcctSchema1_ID)
                INNER JOIN C_Currency cur ON (cur.C_Currency_ID=acs.C_Currency_ID)
                WHERE ci.AD_Client_ID=@AD_Client_ID
                  AND ci.IsActive='Y'
                  AND acs.IsActive='Y'
                  AND cur.IsActive='Y'";

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
            };

            DataSet ds = DB.ExecuteDataset(sql, parameters, null);
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
            {
                /* The join to C_AcctSchema is INNER, so a tenant with a calendar but no
                   primary schema lands here too. Read the calendar on its own so the caller
                   can tell the two configuration errors apart. */
                result.C_Calendar_ID = ReadPrimaryCalendar(ctx);
                return result;
            }

            DataRow row = ds.Tables[0].Rows[0];
            result.C_Calendar_ID = Util.GetValueOfInt(row["C_Calendar_ID"]);
            result.C_AcctSchema_ID = Util.GetValueOfInt(row["C_AcctSchema_ID"]);
            result.Name = Util.GetValueOfString(row["Acct_Schema_Name"]);
            result.C_Currency_ID = Util.GetValueOfInt(row["C_Currency_ID"]);
            result.Iso = Util.GetValueOfString(row["Currency_Iso"]);
            result.Symbol = Util.GetValueOfString(row["Currency_Symbol"]);
            result.Precision = Util.GetValueOfInt(row["Std_Precision"]);

            return result;
        }

        /// <summary>
        /// The primary calendar on its own. Only reached when the accounting-context query
        /// found nothing, to distinguish "no calendar" from "no accounting schema".
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <returns>C_Calendar_ID, or 0.</returns>
        private int ReadPrimaryCalendar(Ctx ctx)
        {
            string sql = @"
                SELECT ci.C_Calendar_ID AS C_Calendar_ID
                FROM AD_ClientInfo ci
                WHERE ci.AD_Client_ID=@AD_Client_ID
                  AND ci.IsActive='Y'";

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
            };

            DataSet ds = DB.ExecuteDataset(sql, parameters, null);
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) { return 0; }

            return Util.GetValueOfInt(ds.Tables[0].Rows[0]["C_Calendar_ID"]);
        }

        /// <summary>
        /// The financial years of the tenant's PRIMARY calendar, newest first, each with the
        /// accounting date window derived from its own periods.
        ///
        /// The window is MIN(StartDate) / MAX(EndDate) over the year's ACTIVE periods -
        /// never a January-to-December assumption. A year with no active period has no
        /// window at all and is therefore not offered: there would be no date range to read
        /// Fact_Acct with.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="calendarId">The tenant's primary C_Calendar_ID.</param>
        /// <returns>Years, newest StartDate first (never null).</returns>
        public List<YearOption> GetYears(Ctx ctx, int calendarId)
        {
            List<YearOption> items = new List<YearOption>();
            if (ctx == null || calendarId <= 0) { return items; }

            string sql = @"
                SELECT y.C_Year_ID AS C_Year_ID,
                       y.FiscalYear AS Fiscal_Year,
                       MIN(p.StartDate) AS Start_Date,
                       MAX(p.EndDate) AS End_Date
                FROM C_Year y
                INNER JOIN C_Period p ON (p.C_Year_ID=y.C_Year_ID)
                WHERE y.C_Calendar_ID=@C_Calendar_ID
                  AND y.IsActive='Y'
                  AND p.IsActive='Y'";

            /* C_Year y is the main physical table the user is choosing from. */
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "y", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            /* GROUP BY and ORDER BY go on AFTER the access SQL - its FROM-clause parser must
               not meet a trailing clause. Ordered by the year's own start date, not by
               FiscalYear: FiscalYear is free text and sorts alphabetically. */
            sql += " GROUP BY y.C_Year_ID,y.FiscalYear ORDER BY MIN(p.StartDate) DESC,y.C_Year_ID DESC";

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@C_Calendar_ID", calendarId)
            };

            DataSet ds = DB.ExecuteDataset(sql, parameters, null);
            if (ds == null || ds.Tables.Count == 0) { return items; }

            DataTable dt = ds.Tables[0];
            for (int i = 0; i < dt.Rows.Count; i++)
            {
                DataRow row = dt.Rows[i];

                DateTime? from = Util.GetValueOfDateTime(row["Start_Date"]);
                DateTime? to = Util.GetValueOfDateTime(row["End_Date"]);
                if (!from.HasValue || !to.HasValue) { continue; }

                YearOption item = new YearOption();
                item.C_Year_ID = Util.GetValueOfInt(row["C_Year_ID"]);
                item.FiscalYear = Util.GetValueOfString(row["Fiscal_Year"]);
                item.StartDate = from.Value.Date;
                item.EndDate = to.Value.Date;
                items.Add(item);
            }

            return items;
        }

        /// <summary>
        /// Resolves which financial year the card actually reads.
        ///
        /// A requested id is honoured only when it is one of the years this role may see on
        /// this tenant's primary calendar - a stale or forged selection falls back to the
        /// default rather than reaching Fact_Acct. The default is the year containing today,
        /// then the most recent year that has already started, then the newest year in the
        /// list.
        /// </summary>
        /// <param name="years">Years of the primary calendar, newest first.</param>
        /// <param name="requestedId">C_Year_ID the client asked for, or 0.</param>
        /// <param name="today">Current application date (date part only).</param>
        /// <returns>The year to read (never null when the list is filled).</returns>
        private YearOption PickYear(List<YearOption> years, int requestedId, DateTime today)
        {
            if (requestedId > 0)
            {
                for (int i = 0; i < years.Count; i++)
                {
                    if (years[i].C_Year_ID == requestedId) { return years[i]; }
                }
            }

            YearOption started = null;

            for (int i = 0; i < years.Count; i++)
            {
                YearOption item = years[i];

                if (item.StartDate <= today && item.EndDate >= today) { return item; }
                if (started == null && item.StartDate <= today) { started = item; }
            }

            return started != null ? started : years[0];
        }

        // ─────────────────────────────────────────────────────────────────────
        // §3  The rows - budget against actual, per account
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Reads every account that carries a budget or an actual in the selected year,
        /// corrects each side for the account's natural balance, ranks by absolute variance
        /// and hands the requested page back.
        ///
        /// The WHOLE set is read rather than one page. The sign correction depends on
        /// C_ElementValue.AccountType and is applied in C#, so the ranking cannot be done in
        /// SQL without duplicating that CASE into the ORDER BY - and the result set is one
        /// row per account of the chart, an aggregate rather than a transaction list.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="acct">Resolved accounting context (schema and currency).</param>
        /// <param name="year">Resolved financial year with its date window.</param>
        /// <param name="result">Result being filled - paging fields included.</param>
        private void ReadRows(Ctx ctx, AcctContext acct, YearOption year, VarianceResult result)
        {
            List<int> offsetIds = GetBalancingAccountIds(ctx, acct.C_AcctSchema_ID);

            /* Bind order is appearance order in the finished statement, because the backend
               adapters bind positionally. The two posting-type binds sit inside the SELECT
               list, so they come FIRST - ahead of everything in the WHERE. */
            List<SqlParameter> parameters = new List<SqlParameter>();
            parameters.Add(new SqlParameter("@PostingType_Budget_Sel", POSTINGTYPE_Budget));
            parameters.Add(new SqlParameter("@PostingType_Actual_Sel", POSTINGTYPE_Actual));
            parameters.Add(new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()));
            parameters.Add(new SqlParameter("@C_AcctSchema_ID", acct.C_AcctSchema_ID));
            parameters.Add(new SqlParameter("@PostingType_Budget", POSTINGTYPE_Budget));
            parameters.Add(new SqlParameter("@PostingType_Actual", POSTINGTYPE_Actual));
            parameters.Add(new SqlParameter("@DateFrom", year.StartDate));
            parameters.Add(new SqlParameter("@DateTo", year.EndDate));

            StringBuilder sql = new StringBuilder();

            /* ONE scan, both posting types, as a FLAT SUM(CASE WHEN ...) per side. Two
               derived sets joined together would read Fact_Acct twice and would have to be
               FULL OUTER joined to keep an account that has a budget and no actual - which
               is the largest variance there is - and this codebase's access-SQL parser is
               kept away from nested selects on principle. */
            sql.Append(@"
                SELECT fa.Account_ID AS Account_ID,
                       COALESCE(ev.Value,N'') AS Account_Value,
                       COALESCE(ev.Name,N'') AS Account_Name,
                       COALESCE(ev.AccountType,'') AS Account_Type,
                       SUM(CASE WHEN fa.PostingType=@PostingType_Budget_Sel THEN COALESCE(fa.AmtAcctDr,0)-COALESCE(fa.AmtAcctCr,0) ELSE 0 END) AS Budget_Signed,
                       SUM(CASE WHEN fa.PostingType=@PostingType_Actual_Sel THEN COALESCE(fa.AmtAcctDr,0)-COALESCE(fa.AmtAcctCr,0) ELSE 0 END) AS Actual_Signed
                FROM Fact_Acct fa
                INNER JOIN C_ElementValue ev ON (ev.C_ElementValue_ID=fa.Account_ID)
                WHERE fa.AD_Client_ID=@AD_Client_ID
                  AND fa.C_AcctSchema_ID=@C_AcctSchema_ID
                  AND fa.IsActive='Y'
                  AND (fa.PostingType=@PostingType_Budget OR fa.PostingType=@PostingType_Actual)
                  AND fa.DateAcct>=@DateFrom
                  AND fa.DateAcct<=@DateTo
                  AND ev.IsActive='Y'");

            /* The balancing side of a budget journal, dropped by id. Server-resolved ids
               only - nothing here comes from the browser. */
            if (offsetIds.Count > 0)
            {
                sql.Append(" AND fa.Account_ID NOT IN (")
                   .Append(BuildIdInList(offsetIds, "@Offset_Account_ID", parameters))
                   .Append(")");
            }

            /* Fact_Acct fa is the main physical table the user is reading from: the role's
               access clause goes HERE, on the base query, and never on a derived alias. */
            string rowSql = MRole.GetDefault(ctx).AddAccessSQL(sql.ToString(), "fa",
                MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            /* GROUP BY after the access SQL. No HAVING and no ORDER BY: an account is
               dropped, and the ranking is decided, only after the sign correction below -
               which SQL cannot see. */
            rowSql += " GROUP BY fa.Account_ID,ev.Value,ev.Name,ev.AccountType";

            DataSet ds = DB.ExecuteDataset(rowSql, parameters.ToArray(), null);
            if (ds == null || ds.Tables.Count == 0) { return; }

            DataTable dt = ds.Tables[0];

            List<VarianceRow> all = new List<VarianceRow>();
            for (int i = 0; i < dt.Rows.Count; i++)
            {
                VarianceRow row = MapRow(dt.Rows[i]);

                /* An account with neither a budget nor an actual worth reporting is not a
                   variance. Both sides at nought means the year's postings cancelled out on
                   that account, and a row of three zeros spends a line of a three-row card
                   saying nothing. */
                if (row.Budget == 0 && row.Actual == 0) { continue; }

                all.Add(row);
            }

            /* ABS(variance) descending - the card is about the SIZE of the gap, so a large
               underspend and a large overrun rank together. The account value breaks a tie
               deterministically so a page boundary cannot shuffle between two requests. */
            all.Sort(CompareByAbsoluteVariance);

            result.TotalRows = all.Count;
            result.TotalPages = result.PageSize > 0
                ? (int)Math.Ceiling((double)result.TotalRows / result.PageSize)
                : 0;

            if (result.TotalRows == 0) { result.Page = 1; return; }

            /* Clamp the page AFTER the total is known: a page number the client kept from a
               longer year must land on the last real page, never past the end. */
            int page = result.Page;
            if (result.TotalPages > 0 && page > result.TotalPages) { page = result.TotalPages; }
            result.Page = page;

            int offset = (page - 1) * result.PageSize;
            for (int i = offset; i < all.Count && i < offset + result.PageSize; i++)
            {
                result.Rows.Add(all[i]);
            }
        }

        /// <summary>
        /// Materialises one account's row and applies the natural-balance sign correction.
        ///
        /// Fact_Acct stores debits and credits, not "amounts". Revenue, liability and
        /// owner's equity are CREDIT-natural, so their figure is AmtAcctCr - AmtAcctDr;
        /// everything else - asset, expense, memo - is debit-natural and keeps AmtAcctDr -
        /// AmtAcctCr. Skipping this reports every revenue budget as a negative and lands
        /// the whole revenue side at the top of a card about the largest variances.
        /// </summary>
        /// <param name="row">Row carrying the group aliases.</param>
        /// <returns>Populated <see cref="VarianceRow"/>.</returns>
        private VarianceRow MapRow(DataRow row)
        {
            VarianceRow item = new VarianceRow();

            item.Account_ID = Util.GetValueOfInt(row["Account_ID"]);
            item.AccountValue = Util.GetValueOfString(row["Account_Value"]);
            item.AccountName = Util.GetValueOfString(row["Account_Name"]);
            item.AccountType = Util.GetValueOfString(row["Account_Type"]);

            decimal budgetSigned = Util.GetValueOfDecimal(row["Budget_Signed"]);
            decimal actualSigned = Util.GetValueOfDecimal(row["Actual_Signed"]);

            if (IsCreditNatural(item.AccountType))
            {
                budgetSigned = -budgetSigned;
                actualSigned = -actualSigned;
            }

            item.Budget = budgetSigned;
            item.Actual = actualSigned;

            /* Budget - Actual. POSITIVE is favourable, NEGATIVE is unfavourable, in both
               the debit-natural and the credit-natural case - which is the point of having
               corrected the two sides first. */
            item.Variance = budgetSigned - actualSigned;

            return item;
        }

        /// <summary>
        /// True for the account types whose natural balance is a CREDIT - revenue,
        /// liability and owner's equity.
        /// </summary>
        /// <param name="accountType">C_ElementValue.AccountType stored code.</param>
        /// <returns>True when the account is credit-natural.</returns>
        private bool IsCreditNatural(string accountType)
        {
            return ACCOUNTTYPE_Revenue.Equals(accountType)
                || ACCOUNTTYPE_Liability.Equals(accountType)
                || ACCOUNTTYPE_OwnersEquity.Equals(accountType);
        }

        /// <summary>
        /// Ranks two rows by absolute variance, largest first, with the account value as a
        /// deterministic tiebreaker so paging is stable across requests.
        /// </summary>
        /// <param name="left">First row.</param>
        /// <param name="right">Second row.</param>
        /// <returns>Standard comparison result.</returns>
        private int CompareByAbsoluteVariance(VarianceRow left, VarianceRow right)
        {
            int bySize = Math.Abs(right.Variance).CompareTo(Math.Abs(left.Variance));
            if (bySize != 0) { return bySize; }

            int byValue = String.Compare(left.AccountValue, right.AccountValue, StringComparison.Ordinal);
            if (byValue != 0) { return byValue; }

            return left.Account_ID.CompareTo(right.Account_ID);
        }

        // ─────────────────────────────────────────────────────────────────────
        // §4  The balancing accounts a budget journal offsets to
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The natural account ids the schema uses as balancing / offset accounts.
        ///
        /// A budget journal balances, so the other side of every budget posting lands on one
        /// of these. Left in the set, such an account carries the mirror image of everything
        /// budgeted and ranks at or near the top of a card about the largest variances -
        /// which tells the reader nothing about their budget.
        ///
        /// Each setting holds a C_ValidCombination_ID, NOT a Fact_Acct.Account_ID, so it is
        /// resolved C_AcctSchema_GL -&gt; C_ValidCombination -&gt; Account_ID. Only the
        /// columns AD_Column confirms the table actually carries are named: the commitment
        /// pair is optional in this schema, and naming a missing column would fail the whole
        /// query and cost the exclusions that ARE configured.
        ///
        /// C_AcctSchema_GL, C_ValidCombination and AD_Column are configuration and
        /// dictionary reads, scoped by tenant and schema, so no MRole predicate is applied -
        /// the same treatment the sibling accounting widgets give this resolution chain.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="acctSchemaId">Primary C_AcctSchema_ID.</param>
        /// <returns>Distinct natural account ids to exclude (never null; often empty).</returns>
        private List<int> GetBalancingAccountIds(Ctx ctx, int acctSchemaId)
        {
            List<int> accountIds = new List<int>();
            if (ctx == null || acctSchemaId <= 0) { return accountIds; }

            List<string> present = ReadTableColumns(ctx, TABLE_ACCTSCHEMA_GL);

            List<string> columns = new List<string>();
            for (int i = 0; i < OFFSET_COLUMNS.Length; i++)
            {
                if (HasColumn(present, OFFSET_COLUMNS[i])) { columns.Add(OFFSET_COLUMNS[i]); }
            }

            if (columns.Count == 0) { return accountIds; }

            StringBuilder select = new StringBuilder();
            select.Append("SELECT ");
            for (int i = 0; i < columns.Count; i++)
            {
                if (i > 0) { select.Append(","); }
                select.Append("COALESCE(gl.").Append(columns[i]).Append(",0) AS Offset_").Append(i);
            }

            select.Append(@"
                FROM C_AcctSchema_GL gl
                WHERE gl.AD_Client_ID=@AD_Client_ID
                  AND gl.C_AcctSchema_ID=@C_AcctSchema_ID
                  AND gl.IsActive='Y'");

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@C_AcctSchema_ID", acctSchemaId)
            };

            DataSet ds = DB.ExecuteDataset(select.ToString(), parameters, null);
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
            {
                Log.Log(Level.INFO, "VAS_254_LargestBudgetVariance: no active C_AcctSchema_GL row for C_AcctSchema_ID="
                    + acctSchemaId + "; no balancing account is excluded");
                return accountIds;
            }

            DataRow row = ds.Tables[0].Rows[0];

            List<int> combinationIds = new List<int>();
            for (int i = 0; i < columns.Count; i++)
            {
                int id = Util.GetValueOfInt(row["Offset_" + i]);
                if (id > 0 && !combinationIds.Contains(id)) { combinationIds.Add(id); }
            }

            if (combinationIds.Count == 0) { return accountIds; }

            return ResolveCombinationAccounts(acctSchemaId, combinationIds);
        }

        /// <summary>
        /// Resolves valid-combination ids to the natural accounts behind them.
        /// </summary>
        /// <param name="acctSchemaId">Primary C_AcctSchema_ID - the combination must belong
        /// to the same schema the figures are read for.</param>
        /// <param name="combinationIds">C_ValidCombination_IDs to resolve.</param>
        /// <returns>Distinct Account_IDs (never null).</returns>
        private List<int> ResolveCombinationAccounts(int acctSchemaId, List<int> combinationIds)
        {
            List<int> accountIds = new List<int>();

            List<SqlParameter> parameters = new List<SqlParameter>();
            parameters.Add(new SqlParameter("@C_AcctSchema_ID", acctSchemaId));

            string inList = BuildIdInList(combinationIds, "@C_ValidCombination_ID", parameters);

            string sql = @"
                SELECT vc.Account_ID AS Account_ID
                FROM C_ValidCombination vc
                WHERE vc.C_AcctSchema_ID=@C_AcctSchema_ID
                  AND vc.IsActive='Y'
                  AND vc.C_ValidCombination_ID IN (" + inList + ")";

            DataSet ds = DB.ExecuteDataset(sql, parameters.ToArray(), null);
            if (ds == null || ds.Tables.Count == 0) { return accountIds; }

            DataTable dt = ds.Tables[0];
            for (int i = 0; i < dt.Rows.Count; i++)
            {
                int id = Util.GetValueOfInt(dt.Rows[i]["Account_ID"]);
                if (id > 0 && !accountIds.Contains(id)) { accountIds.Add(id); }
            }

            return accountIds;
        }

        /// <summary>
        /// The active column names of one dictionary table. Used to confirm an OPTIONAL
        /// column exists before it is named in generated SQL.
        /// </summary>
        /// <param name="ctx">Session context (unused today; kept for symmetry with the
        /// other reads).</param>
        /// <param name="tableName">Physical table name - a constant of this class, never
        /// client text.</param>
        /// <returns>Column names (never null).</returns>
        private List<string> ReadTableColumns(Ctx ctx, string tableName)
        {
            List<string> columns = new List<string>();

            string sql = @"
                SELECT c.ColumnName AS Column_Name
                FROM AD_Column c
                INNER JOIN AD_Table t ON (t.AD_Table_ID=c.AD_Table_ID)
                WHERE t.TableName=@TableName
                  AND t.IsActive='Y' AND c.columnsql IS NULL 
                  AND c.IsActive='Y'";

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@TableName", tableName)
            };

            DataSet ds = DB.ExecuteDataset(sql, parameters, null);
            if (ds == null || ds.Tables.Count == 0) { return columns; }

            DataTable dt = ds.Tables[0];
            for (int i = 0; i < dt.Rows.Count; i++)
            {
                columns.Add(Util.GetValueOfString(dt.Rows[i]["Column_Name"]));
            }

            return columns;
        }

        /// <summary>Case-insensitive membership test over a dictionary column list.</summary>
        /// <param name="columns">Column names the table carries.</param>
        /// <param name="columnName">Name to look for.</param>
        /// <returns>True when the table carries the column.</returns>
        private bool HasColumn(List<string> columns, string columnName)
        {
            for (int i = 0; i < columns.Count; i++)
            {
                if (String.Equals(columns[i], columnName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        // ─────────────────────────────────────────────────────────────────────
        // §5  Helpers
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Builds a parameterized IN list - "@Name0,@Name1,..." - and appends one bind per
        /// id. Every occurrence carries its own name because the backend adapters bind
        /// positionally, so a repeated name would be ambiguous.
        /// </summary>
        /// <param name="ids">Ids to bind (server-sourced, never client text).</param>
        /// <param name="prefix">Bind name prefix.</param>
        /// <param name="parameters">Bind list being built, in appearance order.</param>
        /// <returns>The comma-separated placeholder list.</returns>
        private string BuildIdInList(List<int> ids, string prefix, List<SqlParameter> parameters)
        {
            StringBuilder list = new StringBuilder();

            for (int i = 0; i < ids.Count; i++)
            {
                if (i > 0) { list.Append(","); }

                string name = prefix + i;
                list.Append(name);
                parameters.Add(new SqlParameter(name, ids[i]));
            }

            return list.ToString();
        }

        /// <summary>
        /// A date as yyyy-MM-dd for the wire. The client formats it in the reader's own
        /// locale - the format is never baked into the data layer with TO_CHAR.
        /// </summary>
        /// <param name="value">Date to serialize.</param>
        /// <returns>yyyy-MM-dd, or an empty string.</returns>
        private string ToIsoDate(DateTime? value)
        {
            return value.HasValue ? value.Value.ToString("yyyy-MM-dd") : "";
        }

        // ─────────────────────────────────────────────────────────────────────
        // §6  Transfer objects
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>One page of the widget, plus what the page cannot know by itself.</summary>
        public class VarianceResult
        {
            /// <summary>The requested page of accounts, largest absolute variance first.</summary>
            public List<VarianceRow> Rows { get; set; }

            /// <summary>The financial years the filter can offer, newest first.</summary>
            public List<YearOption> Years { get; set; }

            /// <summary>The accounting schema and its currency - the card's only currency.</summary>
            public AcctContext Schema { get; set; }

            /// <summary>C_Year_ID actually read, after defaulting and validation.</summary>
            public int C_Year_ID { get; set; }

            /// <summary>C_Year.FiscalYear of that year - the pill's label.</summary>
            public string FiscalYear { get; set; }

            /// <summary>First accounting date of the year, as yyyy-MM-dd.</summary>
            public string StartDate { get; set; }

            /// <summary>Last accounting date of the year, as yyyy-MM-dd.</summary>
            public string EndDate { get; set; }

            /// <summary>1-based page number actually served, after clamping.</summary>
            public int Page { get; set; }

            /// <summary>Rows per page actually used, after clamping.</summary>
            public int PageSize { get; set; }

            /// <summary>Accounts in the ranking in total - the pager's figure.</summary>
            public int TotalRows { get; set; }

            /// <summary>CEILING(TotalRows / PageSize).</summary>
            public int TotalPages { get; set; }

            /// <summary>ERROR_* token when the tenant is not configured; empty otherwise.</summary>
            public string ErrorCode { get; set; }

            /// <summary>False only on a failure or a missing configuration; a year with
            /// nothing budgeted or posted is Loaded=true with an empty page.</summary>
            public bool Loaded { get; set; }
        }

        /// <summary>
        /// One account's budget against its actual. Both figures are in the PRIMARY
        /// accounting schema currency and are never converted - AmtAcctDr / AmtAcctCr are
        /// already stated in it - and both have been corrected for the account's natural
        /// balance, so a revenue account reads the same way round as an expense one.
        /// </summary>
        public class VarianceRow
        {
            /// <summary>Fact_Acct.Account_ID - the natural account (C_ElementValue_ID).</summary>
            public int Account_ID { get; set; }

            /// <summary>C_ElementValue.Value - the account code.</summary>
            public string AccountValue { get; set; }

            /// <summary>C_ElementValue.Name - the account description.</summary>
            public string AccountName { get; set; }

            /// <summary>C_ElementValue.AccountType - carried so the client can explain the
            /// sign convention in a tooltip if it ever needs to.</summary>
            public string AccountType { get; set; }

            /// <summary>Approved budget for the year: PostingType 'B', natural-side
            /// corrected.</summary>
            public decimal Budget { get; set; }

            /// <summary>Posted actual for the year: PostingType 'A', natural-side
            /// corrected.</summary>
            public decimal Actual { get; set; }

            /// <summary>Budget - Actual. POSITIVE is favourable (came in under budget),
            /// NEGATIVE is unfavourable (went over).</summary>
            public decimal Variance { get; set; }
        }

        /// <summary>One selectable financial year, with the date window its periods span.</summary>
        public class YearOption
        {
            /// <summary>C_Year.C_Year_ID.</summary>
            public int C_Year_ID { get; set; }

            /// <summary>C_Year.FiscalYear - the label the pill shows.</summary>
            public string FiscalYear { get; set; }

            /// <summary>MIN(C_Period.StartDate) over the year's active periods.</summary>
            public DateTime StartDate { get; set; }

            /// <summary>MAX(C_Period.EndDate) over the year's active periods.</summary>
            public DateTime EndDate { get; set; }
        }

        /// <summary>The tenant's primary calendar, primary accounting schema and its currency.</summary>
        public class AcctContext
        {
            /// <summary>AD_ClientInfo.C_Calendar_ID.</summary>
            public int C_Calendar_ID { get; set; }

            /// <summary>AD_ClientInfo.C_AcctSchema1_ID - the PRIMARY schema, and the only one
            /// this widget reads.</summary>
            public int C_AcctSchema_ID { get; set; }

            /// <summary>C_AcctSchema.Name.</summary>
            public string Name { get; set; }

            /// <summary>C_AcctSchema.C_Currency_ID.</summary>
            public int C_Currency_ID { get; set; }

            /// <summary>The accounting currency's ISO code.</summary>
            public string Iso { get; set; }

            /// <summary>C_Currency.CurSymbol, falling back to ISO_Code.</summary>
            public string Symbol { get; set; }

            /// <summary>C_Currency.StdPrecision.</summary>
            public int Precision { get; set; }
        }
    }
}
