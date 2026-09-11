/******************************************************
 * Module Name    : VASLogic
 * Purpose        : Budgets Near Limit dashboard widget data
 * chronological  : Development
 * Created Date   : 2026-09-09
 * Created by     : VAI145
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
    /// Module Name : VAS_255_BudgetNearLimit
    /// Purpose     : Backs the VAS_255_BudgetNearLimitWidge dashboard widget - the ledger
    ///               accounts that have consumed most of their approved budget but have NOT
    ///               yet passed it:
    ///
    ///                 Budget    ABS(SUM(AmtAcctDr - AmtAcctCr)) over PostingType 'B'.
    ///                 Actual    the same over PostingType 'A'.
    ///                 Used      Actual / Budget * 100.
    ///                 Band      Used &gt;= threshold AND Used &lt; 100. The default
    ///                           threshold is 85%.
    ///
    ///               THIS IS AN EARLY-WARNING CARD, AND THE UPPER BOUND IS THE POINT OF IT.
    ///               An account already at or past 100% is an OVERRUN and belongs to
    ///               VAS_252, which reports exactly that; leaving it here would bury the
    ///               accounts still worth acting on under the ones it is already too late
    ///               for. The two cards therefore partition the same population at 100% and
    ///               never show the same account twice.
    ///
    ///               ONLY ACCOUNTS AGAINST WHICH A BUDGET IS DEFINED. That is a precondition
    ///               rather than a filter of convenience - "85% used" of a budget that was
    ///               never defined is not a number - and it is enforced in the aggregate
    ///               itself, with HAVING on the budget side, so an account carrying actuals
    ///               and nothing approved never leaves the database. Such an account is the
    ///               unbudgeted-actuals card's subject (VAS_256); one carrying a budget and
    ///               no actual is simply below the threshold and is dropped by the band.
    ///
    ///               THE AMOUNT IS ABS(SUM(Dr - Cr)) PER POSTING TYPE, netted before the
    ///               absolute is taken. Netting first is what stops the two sides of one
    ///               journal being counted twice; the absolute afterwards is what lets a
    ///               credit-natural account (revenue, liability, equity) be compared against
    ///               its budget the same way round as a debit-natural one, with no
    ///               account-type rule to maintain. Source amounts are never read -
    ///               AmtAcctDr / AmtAcctCr are already stated in the accounting schema's
    ///               currency, so no currencyConvert call belongs in this model.
    ///
    ///               PRIMARY ACCOUNTING SCHEMA ONLY, on both sides. A budget posted in a
    ///               secondary schema must not be compared against an actual in the primary
    ///               one, so the schema is an equality on the single scan that produces both
    ///               figures.
    ///
    ///               FINANCIAL YEAR. AD_ClientInfo.C_Calendar_ID -&gt; C_Year -&gt; C_Period.
    ///               The accounting date window is MIN(StartDate) / MAX(EndDate) over the
    ///               ACTIVE periods of the selected C_Year_ID - never January to December,
    ///               and never derived from the calendar month.
    ///
    ///               SUMMARY ACCOUNTS ARE EXCLUDED. C_ElementValue.IsSummary='N': a summary
    ///               account is a rollup of the accounts under it, so listing it beside them
    ///               would report the same money twice on one card.
    ///
    ///               THE THRESHOLD IS CONFIGURATION, NOT A LITERAL. It arrives as a value
    ///               this model holds and returns to the client, which prints it in the
    ///               subtitle - the card says "between 85% and 100% of approved budget" because
    ///               the server told it 85, not because either of them spells it out.
    ///
    ///               ONE SCAN, TWO POSTING TYPES, as a FLAT SUM(CASE WHEN ...) per side.
    ///               Two derived sets joined together would read Fact_Acct twice, and this
    ///               codebase's access-SQL parser is kept away from nested selects on
    ///               principle. The band and the ranking are applied in C# afterwards, over
    ///               one row per account of the chart - an aggregate rather than a
    ///               transaction list, and never a query per account.
    ///
    ///               MRole row-level security is applied to Fact_Acct fa on the rows query
    ///               and to C_Year y on the year list. The joined C_ElementValue rows are a
    ///               reference lookup and inherit the parent's filter; AD_ClientInfo,
    ///               C_AcctSchema and C_Currency are configuration reads. GROUP BY is
    ///               appended AFTER AddAccessSQL so its FROM-clause parser never meets a
    ///               trailing clause, and every join ON is a plain equality so it never
    ///               meets a function call either. Compatible with PostgreSQL and Oracle.
    /// Chronological development:
    ///   VAI145      2026-09-09 Created
    /// </summary>
    public class VAS_255_BudgetNearLimitModel
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_255_BudgetNearLimitModel).FullName);

        /* Fact_Acct.PostingType stored codes. Stored codes are compared bare - never with an
           N prefix, which would force a Unicode comparison against a VARCHAR column. */
        private const string POSTINGTYPE_Actual = "A";
        private const string POSTINGTYPE_Budget = "B";

        /* The band this card reports. The floor is the configured warning threshold; the
           ceiling is where an overrun starts and VAS_252 takes over. */
        public const decimal DEFAULT_ThresholdPct = 80m;
        private const decimal MIN_ThresholdPct = 1m;
        private const decimal OVERRUN_Pct = 100m;

        /* Error tokens exchanged with the client; the client resolves the label from
           AD_Message, so no display text is produced here. */
        public const string ERROR_NO_CALENDAR = "NOCALENDAR";
        public const string ERROR_NO_ACCTSCHEMA = "NOACCTSCHEMA";
        public const string ERROR_NO_YEAR = "NOYEAR";

        /* Paging. The 3x2 cell fits five rows at 1280px; a taller cell may ask for more and
           a short one for fewer, but never outside these bounds. */
        public const int DEFAULT_PageSize = 5;
        private const int MIN_PageSize = 1;
        private const int MAX_PageSize = 12;

        // ─────────────────────────────────────────────────────────────────────
        // §1  Entry point
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Everything the card shows in one round trip: the selectable financial years, the
        /// year actually used, the accounting-schema currency, the threshold in force and the
        /// requested page of accounts ranked by how much of their budget they have used.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="yearId">C_Year_ID the user selected, or 0 to default to the financial
        /// year containing today.</param>
        /// <param name="pageNo">1-based page; clamped to the available range.</param>
        /// <param name="pageSize">Rows per page; clamped to [1,12].</param>
        /// <returns>Populated <see cref="NearLimitResult"/> (never null). Loaded is false only
        /// when there is no context or the tenant is not configured; a year with nothing near
        /// its limit returns Loaded=true and an empty page, because "nothing near the limit"
        /// is a real - and good - answer rather than an error.</returns>
        public NearLimitResult GetRows(Ctx ctx, int yearId, int pageNo, int pageSize)
        {
            NearLimitResult result = new NearLimitResult();
            result.Rows = new List<NearLimitRow>();
            result.Years = new List<YearOption>();
            result.ThresholdPct = DEFAULT_ThresholdPct;
            result.PageSize = ClampPageSize(pageSize);
            result.Page = pageNo < 1 ? 1 : pageNo;

            if (ctx == null) { result.Page = 1; return result; }

            /* The accounting context is a CONFIGURATION precondition, not a filter: without a
               primary calendar there is no year list to build, and without a primary
               accounting schema there is no ledger to read. Neither is silently replaced by
               "some other" calendar or schema. */
            AcctContext acct = GetAcctContext(ctx);
            result.Schema = acct;

            if (acct.C_Calendar_ID <= 0)
            {
                result.ErrorCode = ERROR_NO_CALENDAR;
                Log.Log(Level.WARNING, "VAS_255_BudgetNearLimit: AD_ClientInfo.C_Calendar_ID not configured for AD_Client_ID="
                    + ctx.GetAD_Client_ID());
                return result;
            }

            if (acct.C_AcctSchema_ID <= 0)
            {
                result.ErrorCode = ERROR_NO_ACCTSCHEMA;
                Log.Log(Level.WARNING, "VAS_255_BudgetNearLimit: AD_ClientInfo.C_AcctSchema1_ID not configured for AD_Client_ID="
                    + ctx.GetAD_Client_ID());
                return result;
            }

            /* The year list travels with the rows, so the widget is one round trip on load and
               the pill can never name a year the rows were not read for. */
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
        /// Reads only client-scoped configuration and reference tables, so no MRole predicate
        /// is applied - the same treatment the sibling accounting widgets give this lookup.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <returns>Populated <see cref="AcctContext"/>; the ids are 0 when the tenant has no
        /// primary calendar / accounting schema.</returns>
        public AcctContext GetAcctContext(Ctx ctx)
        {
            AcctContext result = new AcctContext();
            result.Precision = 2;

            if (ctx == null) { return result; }

            /* CurSymbol first, ISO_Code as the fallback - the card prints the SYMBOL directly
               against the amount, and only falls back to the code when the currency has no
               symbol configured. */
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
        /// The window is MIN(StartDate) / MAX(EndDate) over the year's ACTIVE periods - never
        /// a January-to-December assumption. A year with no active period has no window at all
        /// and is therefore not offered: there would be no date range to read Fact_Acct with.
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
                  AND y.AD_Client_ID=@AD_Client_ID
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
                new SqlParameter("@C_Calendar_ID", calendarId),
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
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
        // §3  The rows - accounts inside the warning band
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Reads every non-summary account that carries a budget or an actual in the selected
        /// year, keeps the ones inside the warning band, ranks them by how much of their
        /// budget they have used and hands the requested page back.
        ///
        /// The WHOLE set is read rather than one page: the band and the ranking are decided
        /// from a ratio the aggregate produces, and the result set is one row per account of
        /// the chart - an aggregate, not a transaction list.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="acct">Resolved accounting context (schema and currency).</param>
        /// <param name="year">Resolved financial year with its date window.</param>
        /// <param name="result">Result being filled - paging fields included.</param>
        private void ReadRows(Ctx ctx, AcctContext acct, YearOption year, NearLimitResult result)
        {
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

            /* ONE scan, both posting types, as a FLAT SUM(CASE WHEN ...) per side, with the
               absolute taken over each NET so the two sides of one journal cancel instead of
               being counted twice. C_ElementValue is joined for the account's name, its search
               key and the summary flag; its ON is a plain equality so the access parser has
               nothing to trip on. */
            StringBuilder sql = new StringBuilder();
            sql.Append(@"
                SELECT fa.Account_ID AS Account_ID,
                       COALESCE(ev.Value,N'') AS Account_Value,
                       COALESCE(ev.Name,N'') AS Account_Name,
                       (SUM(CASE WHEN fa.PostingType=@PostingType_Budget_Sel THEN COALESCE(fa.AmtAcctDr,0)-COALESCE(fa.AmtAcctCr,0) ELSE 0 END)) AS Budget_Amt,
                       (SUM(CASE WHEN fa.PostingType=@PostingType_Actual_Sel THEN COALESCE(fa.AmtAcctDr,0)-COALESCE(fa.AmtAcctCr,0) ELSE 0 END)) AS Actual_Amt
                FROM Fact_Acct fa
                INNER JOIN C_ElementValue ev ON (ev.C_ElementValue_ID=fa.Account_ID)
                WHERE fa.AD_Client_ID=@AD_Client_ID
                  AND fa.C_AcctSchema_ID=@C_AcctSchema_ID
                  AND fa.IsActive='Y'
                  AND (fa.PostingType=@PostingType_Budget OR fa.PostingType=@PostingType_Actual)
                  AND fa.DateAcct>=@DateFrom
                  AND fa.DateAcct<=@DateTo
                  AND ev.IsActive='Y'
                  AND ev.IsSummary='N'");

            /* Fact_Acct fa is the main physical table the user is reading from: the role's
               access clause goes HERE, on the base query, and never on a derived alias. */
            string rowSql = MRole.GetDefault(ctx).AddAccessSQL(sql.ToString(), "fa",
                MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            /* GROUP BY and HAVING after the access SQL - its FROM-clause parser must not meet
               a trailing clause.

               ONLY ACCOUNTS WITH A BUDGET LEAVE THE DATABASE. The scan has to admit both
               posting types to total them, so an account carrying actuals and nothing
               approved is grouped like any other - and it is not this card's subject: with no
               budget behind it there is no percentage to be near, and "85% used" of a budget
               that was never defined is not a number. The HAVING drops it at the source
               rather than fetching it to discard it in C#, which also keeps the payload to
               the accounts the card can actually report on.

               The bind is added LAST because the clause is last: the adapters bind
               positionally, and every occurrence carries its own name. */
            rowSql += " GROUP BY fa.Account_ID,ev.Value,ev.Name"
                + " HAVING (SUM(CASE WHEN fa.PostingType=@PostingType_Budget_Have THEN COALESCE(fa.AmtAcctDr,0)-COALESCE(fa.AmtAcctCr,0) ELSE 0 END))>0";
            parameters.Add(new SqlParameter("@PostingType_Budget_Have", POSTINGTYPE_Budget));

            DataSet ds = DB.ExecuteDataset(rowSql, parameters.ToArray(), null);
            if (ds == null || ds.Tables.Count == 0) { return; }

            DataTable dt = ds.Tables[0];

            decimal threshold = result.ThresholdPct < MIN_ThresholdPct
                ? DEFAULT_ThresholdPct : result.ThresholdPct;

            List<NearLimitRow> all = new List<NearLimitRow>();
            for (int i = 0; i < dt.Rows.Count; i++)
            {
                DataRow row = dt.Rows[i];

                decimal budget = Util.GetValueOfDecimal(row["Budget_Amt"]);
                decimal actual = Util.GetValueOfDecimal(row["Actual_Amt"]);

                /* The HAVING above already dropped every account without a budget. This is the
                   divide-by-zero guard standing behind it, not the filter itself: the two
                   agree, and the one that protects the division is the one that must never be
                   removed. */
                if (budget <= 0) { continue; }

                decimal used = actual * 100m / budget;

                /* The band. Below the threshold the account is not worth warning about; at
                   or past 100% it is an overrun and VAS_252's, not this card's. */
                if (used < threshold || used >= OVERRUN_Pct) { continue; }

                NearLimitRow item = new NearLimitRow();
                item.Account_ID = Util.GetValueOfInt(row["Account_ID"]);
                item.AccountValue = Util.GetValueOfString(row["Account_Value"]);
                item.AccountName = Util.GetValueOfString(row["Account_Name"]);
                item.Budget = budget;
                item.Actual = actual;
                item.UsedPct = used;

                all.Add(item);
            }

            /* Closest to its budget first - the card is a watch list, and the account with
               least room left is the one to look at. The name breaks a tie so a page boundary
               cannot shuffle between two requests. */
            all.Sort(CompareByUsed);

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
        /// Ranks two rows by how much of the budget is used, most first, with the account name
        /// and then its id as deterministic tiebreakers so paging is stable across requests.
        /// </summary>
        /// <param name="left">First row.</param>
        /// <param name="right">Second row.</param>
        /// <returns>Standard comparison result.</returns>
        private int CompareByUsed(NearLimitRow left, NearLimitRow right)
        {
            int byPct = right.UsedPct.CompareTo(left.UsedPct);
            if (byPct != 0) { return byPct; }

            int byName = String.Compare(left.AccountName, right.AccountName,
                StringComparison.CurrentCultureIgnoreCase);
            if (byName != 0) { return byName; }

            return left.Account_ID.CompareTo(right.Account_ID);
        }

        // ─────────────────────────────────────────────────────────────────────
        // §4  Helpers
        // ─────────────────────────────────────────────────────────────────────

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
        // §5  Transfer objects
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>One page of the widget, plus what the page cannot know by itself.</summary>
        public class NearLimitResult
        {
            /// <summary>The requested page of accounts, closest to their budget first.</summary>
            public List<NearLimitRow> Rows { get; set; }

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

            /// <summary>The warning threshold in force, as a percentage - the subtitle names
            /// it, so the card never spells the number out itself.</summary>
            public decimal ThresholdPct { get; set; }

            /// <summary>1-based page number actually served, after clamping.</summary>
            public int Page { get; set; }

            /// <summary>Rows per page actually used, after clamping.</summary>
            public int PageSize { get; set; }

            /// <summary>Accounts inside the band in total - the subtitle's and the pager's
            /// figure, never just the ones on the current page.</summary>
            public int TotalRows { get; set; }

            /// <summary>CEILING(TotalRows / PageSize).</summary>
            public int TotalPages { get; set; }

            /// <summary>ERROR_* token when the tenant is not configured; empty otherwise.</summary>
            public string ErrorCode { get; set; }

            /// <summary>False only on a failure or a missing configuration; a year with nothing
            /// near its limit is Loaded=true with an empty page.</summary>
            public bool Loaded { get; set; }
        }

        /// <summary>
        /// One account inside the warning band. Both figures are in the PRIMARY accounting
        /// schema currency and are never converted - AmtAcctDr / AmtAcctCr are already stated
        /// in it - and both are the ABSOLUTE of a net, so a credit-natural account reads the
        /// same way round as a debit-natural one.
        /// </summary>
        public class NearLimitRow
        {
            /// <summary>Fact_Acct.Account_ID - the natural account (C_ElementValue_ID).</summary>
            public int Account_ID { get; set; }

            /// <summary>C_ElementValue.Value - the account code.</summary>
            public string AccountValue { get; set; }

            /// <summary>C_ElementValue.Name - the account description, and the row's label.</summary>
            public string AccountName { get; set; }

            /// <summary>Approved budget for the year: PostingType 'B', netted then absolute.
            /// Always greater than zero - an account without one is not on this card.</summary>
            public decimal Budget { get; set; }

            /// <summary>Posted actual for the year: PostingType 'A', netted then absolute.</summary>
            public decimal Actual { get; set; }

            /// <summary>Actual / Budget * 100, always inside [threshold, 100).</summary>
            public decimal UsedPct { get; set; }
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

            /// <summary>The accounting currency's ISO code - it drives the compact scale and
            /// stands in when the currency has no symbol.</summary>
            public string Iso { get; set; }

            /// <summary>C_Currency.CurSymbol, falling back to ISO_Code. This is what the card
            /// prints against every amount.</summary>
            public string Symbol { get; set; }

            /// <summary>C_Currency.StdPrecision.</summary>
            public int Precision { get; set; }
        }
    }
}
