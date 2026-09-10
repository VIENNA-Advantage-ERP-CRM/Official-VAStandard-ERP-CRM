/******************************************************
 * Module Name    : VASLogic
 * Purpose        : Budget vs Actual by Period dashboard widget data
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
    /// Module Name : VAS_251_BudgetvsActualPeriod
    /// Purpose     : Backs the VAS_251_BudgetvsActualPeriodWidget dashboard widget - approved
    ///               budget against posted actual, one pair of bars per accounting period of
    ///               the selected financial year, and the ledger accounts behind any period
    ///               that has been posted to:
    ///
    ///                 Budget    SUM(AmtAcctDr - AmtAcctCr) over PostingType 'B'.
    ///                 Actual    the same over PostingType 'A'.
    ///                 Count     how many ACTUAL facts the period carries - which is what
    ///                           decides whether it can be opened, not its amount.
    ///
    ///               EVERY ACTIVE PERIOD IS RETURNED, POSTED OR NOT. A period with no
    ///               postings still has an approved budget worth drawing, and a year that
    ///               dropped its empty periods would silently change shape as the year filled
    ///               in. The periods are the spine of the answer and the facts are laid over
    ///               them - which is why the merge happens in C# rather than as a LEFT OUTER
    ///               JOIN over a date range: the join would be a non-equi join across every
    ///               fact of the year, and the access parser would have to read it.
    ///
    ///               THE PERIOD IS THE LEDGER'S OWN, NOT A DATE BUCKET. Facts are grouped by
    ///               Fact_Acct.C_Period_ID, the period the posting was actually stamped with,
    ///               and the year's date window is applied as a second, independent guard. A
    ///               fiscal year that does not follow the calendar year therefore needs no
    ///               special case anywhere, and no month arithmetic is done at all.
    ///
    ///               EXPENSE ACCOUNTS ONLY. Both series and the drill-down are restricted to
    ///               C_ElementValue.AccountType = 'E'. Budget against actual is a question
    ///               about SPENDING: a revenue, asset, liability or equity account posted in
    ///               the same period consumes no budget, and left in it moves both bars for a
    ///               reason the card does not report. The predicate is applied in the SAME
    ///               place on both reads, which is what keeps the modal's "Total posted"
    ///               equal to the bar that opened it - filtering one and not the other would
    ///               leave the drill-down unable to reconcile. The sibling budget cards
    ///               (VAS_253, VAS_256) draw the same line at the same place.
    ///
    ///               NO ABS. The specification is explicit: the accounting sign convention is
    ///               preserved, so a period whose postings net negative reports a negative
    ///               figure rather than being quietly turned positive. The BAR HEIGHTS are
    ///               computed from magnitudes - a bar is a size, and this chart's floor is
    ///               zero - and the sign is carried by the figures and the tooltip.
    ///
    ///               "POSTED THROUGH" IS DECIDED BY THE POSTING COUNT, never by the amount.
    ///               A period can carry postings that net to nothing; reading the amount
    ///               would report that period as unposted and move the subtitle's watermark
    ///               backwards. The latest period by StartDate whose ActualCount &gt; 0 is
    ///               the answer, and it is the same test that decides which bars are
    ///               clickable.
    ///
    ///               PRIMARY ACCOUNTING SCHEMA ONLY, on both sides and in the drill-down. A
    ///               budget posted in a secondary schema must not be compared against an
    ///               actual in the primary one. AmtAcct* is already stated in that schema's
    ///               currency, so there is no currencyConvert call anywhere in this model.
    ///
    ///               THE DRILL-DOWN READS ACTUALS ONLY, grouped by account and organisation,
    ///               and its total is the period's own actual - the modal reconciles to the
    ///               bar that opened it by construction, because both come from the same
    ///               definition applied to the same period.
    ///
    ///               MRole row-level security is applied to Fact_Acct fa on both reads and to
    ///               C_Year y on the year list. The joined C_ElementValue and AD_Org rows are
    ///               reference lookups by primary key and inherit the parent's filter; AD_ClientInfo,
    ///               C_AcctSchema, C_Currency and C_Period are configuration reads. GROUP BY
    ///               and ORDER BY are appended AFTER AddAccessSQL so its FROM-clause parser
    ///               never meets a trailing clause, and every join ON is a plain equality so
    ///               it never meets a function call either. Compatible with PostgreSQL and
    ///               Oracle.
    /// Chronological development:
    ///   VAI145      2026-09-09 Created
    /// </summary>
    public class VAS_251_BudgetvsActualPeriodModel
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_251_BudgetvsActualPeriodModel).FullName);

        /* Fact_Acct.PostingType stored codes. Stored codes are compared bare - never with an
           N prefix, which would force a Unicode comparison against a VARCHAR column. */
        private const string POSTINGTYPE_Actual = "A";
        private const string POSTINGTYPE_Budget = "B";

        /* C_ElementValue.AccountType - the only type this card reports on. Budget against
           actual is a question about SPENDING, and an asset, liability, equity or revenue
           account consumes no budget. */
        private const string ACCOUNTTYPE_Expense = "E";

        /* Error tokens exchanged with the client; the client resolves the label from
           AD_Message, so no display text is produced here. */
        public const string ERROR_NO_CALENDAR = "NOCALENDAR";
        public const string ERROR_NO_ACCTSCHEMA = "NOACCTSCHEMA";
        public const string ERROR_NO_YEAR = "NOYEAR";

        /* The tallest bar is drawn at 1/1.12 of the plot, so the busiest period never touches
           the ceiling and the eye can still tell two near-equal bars apart. */
        private const decimal BAR_Headroom = 1.12m;
        private const decimal BAR_MaxPct = 100m;

        /* One period's drill-down is one page: an accounting period has a bounded number of
           accounts posted to it, and the modal scrolls. The cap is a payload guard, not a
           pager - the TOTAL always comes from the whole period, so the modal still reconciles
           to the bar when the list is trimmed. */
        private const int MAX_DetailRows = 200;

        /* Oracle refuses an IN list longer than 1000 expressions. A financial year has a
           dozen or so periods, so one batch always covers it - the guard is here so a tenant
           with adjusting periods cannot walk off the end. */
        private const int MAX_PeriodIds = 500;

        // ─────────────────────────────────────────────────────────────────────
        // §1  Entry point - the chart
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Everything the card shows in one round trip: the selectable financial years, the
        /// year actually used, the accounting-schema currency, every active period of that
        /// year with its budget, its actual and its bar heights, and the totals the subtitle
        /// is built from.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="yearId">C_Year_ID the user selected, or 0 to default to the financial
        /// year containing today.</param>
        /// <returns>Populated <see cref="ChartResult"/> (never null). Loaded is false only
        /// when there is no context or the tenant is not configured; a year with no postings
        /// at all returns Loaded=true with its periods and zero figures, because "nothing
        /// posted yet" is a real answer rather than an error.</returns>
        public ChartResult GetChart(Ctx ctx, int yearId)
        {
            ChartResult result = new ChartResult();
            result.Periods = new List<PeriodPoint>();
            result.Years = new List<YearOption>();

            if (ctx == null) { return result; }

            /* The accounting context is a CONFIGURATION precondition, not a filter: without a
               primary calendar there is no year list to build, and without a primary
               accounting schema there is no ledger to read. Neither is silently replaced by
               "some other" calendar or schema. */
            AcctContext acct = GetAcctContext(ctx);
            result.Schema = acct;

            if (acct.C_Calendar_ID <= 0)
            {
                result.ErrorCode = ERROR_NO_CALENDAR;
                Log.Log(Level.WARNING, "VAS_251_BudgetvsActualPeriod: AD_ClientInfo.C_Calendar_ID not configured for AD_Client_ID="
                    + ctx.GetAD_Client_ID());
                return result;
            }

            if (acct.C_AcctSchema_ID <= 0)
            {
                result.ErrorCode = ERROR_NO_ACCTSCHEMA;
                Log.Log(Level.WARNING, "VAS_251_BudgetvsActualPeriod: AD_ClientInfo.C_AcctSchema1_ID not configured for AD_Client_ID="
                    + ctx.GetAD_Client_ID());
                return result;
            }

            /* The year list travels with the chart, so the widget is one round trip on load
               and the pill can never name a year the bars were not read for. */
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

            /* THE PERIODS ARE THE SPINE. They are read first and in their own right, so every
               one of them reaches the chart whether or not a single fact was ever posted
               against it. */
            result.Periods = GetPeriods(ctx, year.C_Year_ID);
            result.PeriodCount = result.Periods.Count;

            if (result.Periods.Count > 0)
            {
                ApplyAmounts(ctx, acct, result.Periods);
                ApplyTotals(result);
                ApplyBarPercents(result.Periods);
            }

            result.Loaded = true;
            return result;
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
        /// date window its own active periods span.
        ///
        /// The window is MIN(StartDate) / MAX(EndDate) over the year's ACTIVE periods - never
        /// a January-to-December assumption. A year with no active period has no window at all
        /// and is therefore not offered: there would be nothing to chart.
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
        // §3  The periods, and the figures laid over them
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Every ACTIVE period of the selected year, in StartDate order - which is the
        /// chart's X axis and the only order it is ever drawn in.
        ///
        /// C_Period is the authority on what a period is and when it runs. No month
        /// arithmetic is done anywhere in this model, so a 4-4-5 calendar, a 13-period year
        /// or an adjusting period all chart correctly with no special case.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="yearId">The selected C_Year_ID.</param>
        /// <returns>Periods in StartDate order (never null).</returns>
        private List<PeriodPoint> GetPeriods(Ctx ctx, int yearId)
        {
            List<PeriodPoint> items = new List<PeriodPoint>();
            if (ctx == null || yearId <= 0) { return items; }

            string sql = @"
                SELECT p.C_Period_ID AS C_Period_ID,
                       p.Name AS Period_Name,
                       p.StartDate AS Start_Date,
                       p.EndDate AS End_Date
                FROM C_Period p
                WHERE p.C_Year_ID=@C_Year_ID
                  AND p.AD_Client_ID=@AD_Client_ID
                  AND p.IsActive='Y'
                ORDER BY p.StartDate,p.C_Period_ID";

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@C_Year_ID", yearId),
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

                PeriodPoint item = new PeriodPoint();
                item.C_Period_ID = Util.GetValueOfInt(row["C_Period_ID"]);
                item.Name = Util.GetValueOfString(row["Period_Name"]);
                item.StartDate = ToIsoDate(from.Value);
                item.EndDate = ToIsoDate(to.Value);

                if (item.C_Period_ID <= 0) { continue; }
                items.Add(item);
            }

            return items;
        }

        /// <summary>
        /// Fills each period's budget, actual and actual-posting count from ONE grouped scan
        /// of Fact_Acct.
        ///
        /// The facts are grouped by Fact_Acct.C_Period_ID - the period the ledger itself
        /// stamped the posting with - and the merge back onto the period list happens in C#,
        /// so a period with no facts keeps its zeros and still reaches the chart. That is the
        /// LEFT OUTER JOIN of the specification, done where it costs nothing: joining a period
        /// list to the facts on a date RANGE would be a non-equi join across the whole year.
        ///
        /// The year's date window is applied as well as the period ids. The ids are the
        /// authority - a posting belongs to the period it was booked into - and the window is
        /// an independent guard that also gives the optimiser the DateAcct range to work with.
        ///
        /// NO ABS: the accounting sign convention is preserved exactly as the specification
        /// requires, and the client draws its bars from the magnitudes.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="acct">Resolved accounting context (schema and currency).</param>
        /// <param name="periods">The year's periods, completed in place.</param>
        private void ApplyAmounts(Ctx ctx, AcctContext acct, List<PeriodPoint> periods)
        {
            List<int> periodIds = new List<int>();
            Dictionary<int, PeriodPoint> byPeriod = new Dictionary<int, PeriodPoint>();

            DateTime from = DateTime.MaxValue;
            DateTime to = DateTime.MinValue;

            for (int i = 0; i < periods.Count && periodIds.Count < MAX_PeriodIds; i++)
            {
                PeriodPoint item = periods[i];

                periodIds.Add(item.C_Period_ID);
                byPeriod[item.C_Period_ID] = item;

                DateTime start = FromIsoDate(item.StartDate);
                DateTime end = FromIsoDate(item.EndDate);
                if (start < from) { from = start; }
                if (end > to) { to = end; }
            }

            if (periodIds.Count == 0 || from > to) { return; }

            /* Bind order is appearance order in the finished statement, because the backend
               adapters bind positionally. The three posting-type binds sit inside the SELECT
               list, so they come FIRST - ahead of everything in the WHERE. */
            List<SqlParameter> parameters = new List<SqlParameter>();
            parameters.Add(new SqlParameter("@PostingType_Budget_Sel", POSTINGTYPE_Budget));
            parameters.Add(new SqlParameter("@PostingType_Actual_Sel", POSTINGTYPE_Actual));
            parameters.Add(new SqlParameter("@PostingType_Actual_Cnt", POSTINGTYPE_Actual));
            parameters.Add(new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()));
            parameters.Add(new SqlParameter("@C_AcctSchema_ID", acct.C_AcctSchema_ID));
            parameters.Add(new SqlParameter("@PostingType_Budget", POSTINGTYPE_Budget));
            parameters.Add(new SqlParameter("@PostingType_Actual", POSTINGTYPE_Actual));
            parameters.Add(new SqlParameter("@DateFrom", from));
            parameters.Add(new SqlParameter("@DateTo", to));

            StringBuilder sql = new StringBuilder();

            /* ONE scan, both posting types, as a FLAT SUM(CASE WHEN ...) per side plus a
               COUNT of the actual side. The count is a measure in its own right: it, and not
               the amount, decides whether a period was posted to. */
            sql.Append(@"
                SELECT fa.C_Period_ID AS C_Period_ID,
                       COALESCE(SUM(CASE WHEN fa.PostingType=@PostingType_Budget_Sel THEN COALESCE(fa.AmtAcctDr,0)-COALESCE(fa.AmtAcctCr,0) ELSE 0 END),0) AS Budget_Amt,
                       COALESCE(SUM(CASE WHEN fa.PostingType=@PostingType_Actual_Sel THEN COALESCE(fa.AmtAcctDr,0)-COALESCE(fa.AmtAcctCr,0) ELSE 0 END),0) AS Actual_Amt,
                       COALESCE(SUM(CASE WHEN fa.PostingType=@PostingType_Actual_Cnt THEN 1 ELSE 0 END),0) AS Actual_Cnt
                FROM Fact_Acct fa
                INNER JOIN C_ElementValue ev ON (ev.C_ElementValue_ID=fa.Account_ID)
                WHERE fa.AD_Client_ID=@AD_Client_ID
                  AND fa.C_AcctSchema_ID=@C_AcctSchema_ID
                  AND fa.IsActive='Y'
                  AND (fa.PostingType=@PostingType_Budget OR fa.PostingType=@PostingType_Actual)
                  AND fa.DateAcct>=@DateFrom
                  AND fa.DateAcct<=@DateTo
                  AND fa.C_Period_ID IN (").Append(BuildIdInList(periodIds, "@C_Period_ID", parameters)).Append(")");

            /* EXPENSE ACCOUNTS ONLY, on BOTH series. Budget against actual is a question
               about spending: a revenue, asset, liability or equity account posted in the
               same period consumes no budget, and left in it moves both bars for a reason
               the card does not report. C_ElementValue is joined for the account's type
               alone - a reference lookup by primary key, with a plain-equality ON so the
               access parser has nothing to trip on.

               The drill-down applies the SAME predicate, which is what keeps the modal's
               "Total posted" equal to the bar that opened it. The bind is added LAST because
               the clause is last: the adapters bind positionally. */
            sql.Append(" AND ev.IsActive='Y' AND ev.AccountType=@AccountType_Expense");
            parameters.Add(new SqlParameter("@AccountType_Expense", ACCOUNTTYPE_Expense));

            /* Fact_Acct fa is the main physical table the user is reading from: the role's
               access clause goes HERE, on the base query, and never on a derived alias. */
            string finalSql = MRole.GetDefault(ctx).AddAccessSQL(sql.ToString(), "fa",
                MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            /* GROUP BY after the access SQL. No ORDER BY: the periods carry the order, and
               the rows are merged onto them by id. */
            finalSql += " GROUP BY fa.C_Period_ID";

            DataSet ds = DB.ExecuteDataset(finalSql, parameters.ToArray(), null);
            if (ds == null || ds.Tables.Count == 0) { return; }

            DataTable dt = ds.Tables[0];
            for (int i = 0; i < dt.Rows.Count; i++)
            {
                DataRow row = dt.Rows[i];

                int periodId = Util.GetValueOfInt(row["C_Period_ID"]);
                if (!byPeriod.ContainsKey(periodId)) { continue; }

                PeriodPoint item = byPeriod[periodId];
                item.Budget = Util.GetValueOfDecimal(row["Budget_Amt"]);
                item.Actual = Util.GetValueOfDecimal(row["Actual_Amt"]);
                item.ActualCount = Util.GetValueOfInt(row["Actual_Cnt"]);

                /* WHAT MAKES A PERIOD OPENABLE: that it carries actual postings, not that
                   they add up to anything. A period whose postings net to nothing has still
                   been posted to, and its accounts are still worth looking at. */
                item.HasActual = item.ActualCount > 0;
            }
        }

        /// <summary>
        /// Totals the year and works out how far it has been posted.
        ///
        /// "Posted through" is the LATEST period by StartDate that carries actual postings -
        /// decided by the count, never by the amount, so a period whose postings cancel out
        /// cannot move the watermark backwards.
        /// </summary>
        /// <param name="result">Result being filled.</param>
        private void ApplyTotals(ChartResult result)
        {
            decimal budget = 0m;
            decimal actual = 0m;
            int postedCount = 0;

            for (int i = 0; i < result.Periods.Count; i++)
            {
                PeriodPoint item = result.Periods[i];

                budget += item.Budget;
                actual += item.Actual;

                if (item.HasActual)
                {
                    postedCount++;
                    /* The list is in StartDate order, so the last one to pass is the latest. */
                    result.PostedThrough = item.Name;
                    result.PostedThroughPeriodId = item.C_Period_ID;
                }
            }

            result.TotalBudget = budget;
            result.TotalActual = actual;
            result.PostedPeriodCount = postedCount;

            /* Utilization of a budget that does not exist is not a number - the client prints
               a dash rather than a division nobody can defend. */
            if (budget != 0)
            {
                result.HasUtilization = true;
                result.UtilizedPct = actual * 100m / budget;
            }
        }

        /// <summary>
        /// Turns each period's figures into bar heights, as a percentage of the plot.
        ///
        /// The scale is the tallest bar of EITHER series across the whole year, with headroom
        /// so the busiest period does not touch the ceiling. Both series share one scale -
        /// that is the entire point of a grouped chart, and scaling them apart would let a
        /// small actual look like a large one.
        ///
        /// THE HEIGHTS ARE MAGNITUDES even though the amounts are signed. A bar is a size,
        /// this chart's floor is zero, and a period that netted negative still moved that
        /// much money; the sign is read from the figures and the tooltip, which keep it.
        /// </summary>
        /// <param name="periods">The year's periods, completed in place.</param>
        private void ApplyBarPercents(List<PeriodPoint> periods)
        {
            decimal scale = 0m;

            for (int i = 0; i < periods.Count; i++)
            {
                decimal budget = Math.Abs(periods[i].Budget);
                decimal actual = Math.Abs(periods[i].Actual);

                if (budget > scale) { scale = budget; }
                if (actual > scale) { scale = actual; }
            }

            if (scale <= 0) { return; }

            scale = scale * BAR_Headroom;

            for (int i = 0; i < periods.Count; i++)
            {
                PeriodPoint item = periods[i];

                item.BudgetBarPct = ClampPct(Math.Abs(item.Budget) / scale * BAR_MaxPct);
                item.ActualBarPct = ClampPct(Math.Abs(item.Actual) / scale * BAR_MaxPct);
            }
        }

        /// <summary>Keeps a bar inside its plot.</summary>
        /// <param name="value">Computed percentage.</param>
        /// <returns>A percentage within [0,100].</returns>
        private decimal ClampPct(decimal value)
        {
            if (value < 0) { return 0m; }
            return value > BAR_MaxPct ? BAR_MaxPct : value;
        }

        // ─────────────────────────────────────────────────────────────────────
        // §4  The drill-down - the ledger accounts behind one period
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The ACTUAL postings of one period, grouped by ledger account and organisation,
        /// largest movement first - plus that period's own budget, actual and variance, so the
        /// modal reconciles to the bar that opened it by construction rather than by the
        /// client passing figures back.
        ///
        /// Read only when a period is actually opened: the chart never carries this.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="periodId">C_Period_ID the user clicked.</param>
        /// <returns>Populated <see cref="PeriodDetailResult"/> (never null). Loaded is false
        /// when there is no context, no accounting schema, or the period is not one of this
        /// tenant's - a period with no postings is Loaded=true and an empty list.</returns>
        public PeriodDetailResult GetPeriodDetail(Ctx ctx, int periodId)
        {
            PeriodDetailResult result = new PeriodDetailResult();
            result.Rows = new List<DetailRow>();

            if (ctx == null || periodId <= 0) { return result; }

            AcctContext acct = GetAcctContext(ctx);
            result.Schema = acct;

            if (acct.C_AcctSchema_ID <= 0)
            {
                result.ErrorCode = ERROR_NO_ACCTSCHEMA;
                return result;
            }

            /* The period is re-read from the tenant's own calendar rather than trusted from
               the browser: its name, its dates and the fact that it exists at all are decided
               here, and a period id belonging to somebody else's calendar reads nothing. */
            PeriodPoint period = ReadPeriod(ctx, periodId, acct.C_Calendar_ID);
            if (period == null)
            {
                Log.Log(Level.WARNING, "VAS_251_BudgetvsActualPeriod: C_Period_ID=" + periodId
                    + " is not an active period of the primary calendar for AD_Client_ID=" + ctx.GetAD_Client_ID());
                return result;
            }

            result.C_Period_ID = period.C_Period_ID;
            result.PeriodName = period.Name;
            result.StartDate = period.StartDate;
            result.EndDate = period.EndDate;
            result.FiscalYear = period.FiscalYear;

            /* The period's own figures, from the same definition the chart uses - one period
               instead of the year. */
            List<PeriodPoint> one = new List<PeriodPoint>();
            one.Add(period);
            ApplyAmounts(ctx, acct, one);

            result.Budget = period.Budget;
            result.Actual = period.Actual;
            result.Variance = period.Budget - period.Actual;

            if (period.Budget != 0)
            {
                result.HasUtilization = true;
                result.UtilizedPct = period.Actual * 100m / period.Budget;
            }

            ReadDetailRows(ctx, acct, period, result);

            result.Loaded = true;
            return result;
        }

        /// <summary>
        /// One period of the tenant's PRIMARY calendar, with the financial year it belongs to.
        ///
        /// The calendar is part of the predicate on purpose: it is what stops a period id from
        /// another calendar - or another tenant's - being charted as though it were this
        /// year's.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="periodId">C_Period_ID to resolve.</param>
        /// <param name="calendarId">The tenant's primary C_Calendar_ID.</param>
        /// <returns>The period, or null when it is not this tenant's to read.</returns>
        private PeriodPoint ReadPeriod(Ctx ctx, int periodId, int calendarId)
        {
            if (calendarId <= 0) { return null; }

            string sql = @"
                SELECT p.C_Period_ID AS C_Period_ID,
                       p.Name AS Period_Name,
                       p.StartDate AS Start_Date,
                       p.EndDate AS End_Date,
                       y.FiscalYear AS Fiscal_Year
                FROM C_Period p
                INNER JOIN C_Year y ON (y.C_Year_ID=p.C_Year_ID)
                WHERE p.C_Period_ID=@C_Period_ID
                  AND p.AD_Client_ID=@AD_Client_ID
                  AND y.C_Calendar_ID=@C_Calendar_ID
                  AND p.IsActive='Y'
                  AND y.IsActive='Y'";

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@C_Period_ID", periodId),
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@C_Calendar_ID", calendarId)
            };

            DataSet ds = DB.ExecuteDataset(sql, parameters, null);
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) { return null; }

            DataRow row = ds.Tables[0].Rows[0];

            DateTime? from = Util.GetValueOfDateTime(row["Start_Date"]);
            DateTime? to = Util.GetValueOfDateTime(row["End_Date"]);
            if (!from.HasValue || !to.HasValue) { return null; }

            PeriodPoint item = new PeriodPoint();
            item.C_Period_ID = Util.GetValueOfInt(row["C_Period_ID"]);
            item.Name = Util.GetValueOfString(row["Period_Name"]);
            item.StartDate = ToIsoDate(from.Value);
            item.EndDate = ToIsoDate(to.Value);
            item.FiscalYear = Util.GetValueOfString(row["Fiscal_Year"]);

            return item;
        }

        /// <summary>
        /// The ledger accounts posted to in one period: ACTUALS ONLY, grouped by account and
        /// organisation, with how many postings each pair carries and what they add up to.
        ///
        /// Ordered by the SIZE of the movement, so the accounts that made the period what it
        /// is come first whichever way they went; the account's own code breaks a tie.
        ///
        /// The list is capped, the TOTAL is not: the total is the period's whole actual, so a
        /// trimmed list still reconciles to the bar that opened the modal.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="acct">Resolved accounting context (schema and currency).</param>
        /// <param name="period">The period being opened.</param>
        /// <param name="result">Result being filled.</param>
        private void ReadDetailRows(Ctx ctx, AcctContext acct, PeriodPoint period,
            PeriodDetailResult result)
        {
            List<SqlParameter> parameters = new List<SqlParameter>();
            parameters.Add(new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()));
            parameters.Add(new SqlParameter("@C_AcctSchema_ID", acct.C_AcctSchema_ID));
            parameters.Add(new SqlParameter("@PostingType_Actual", POSTINGTYPE_Actual));
            parameters.Add(new SqlParameter("@C_Period_ID", period.C_Period_ID));
            parameters.Add(new SqlParameter("@DateFrom", FromIsoDate(period.StartDate)));
            parameters.Add(new SqlParameter("@DateTo", FromIsoDate(period.EndDate)));
            parameters.Add(new SqlParameter("@AccountType_Expense", ACCOUNTTYPE_Expense));

            /* C_ElementValue names the account and AD_Org the organisation the posting was
               stamped with; both are reference lookups by primary key and inherit the parent's
               access filter. Every ON is a plain equality so the access parser has nothing to
               trip on. */
            string sql = @"
                SELECT fa.Account_ID AS Account_ID,
                       COALESCE(ev.Value,N'') AS Account_Value,
                       COALESCE(ev.Name,N'') AS Account_Name,
                       fa.AD_Org_ID AS AD_Org_ID,
                       COALESCE(org.Name,N'') AS Org_Name,
                       COUNT(1) AS Posting_Cnt,
                       COALESCE(SUM(COALESCE(fa.AmtAcctDr,0)-COALESCE(fa.AmtAcctCr,0)),0) AS Actual_Amt
                FROM Fact_Acct fa
                INNER JOIN C_ElementValue ev ON (ev.C_ElementValue_ID=fa.Account_ID)
                LEFT OUTER JOIN AD_Org org ON (org.AD_Org_ID=fa.AD_Org_ID)
                WHERE fa.AD_Client_ID=@AD_Client_ID
                  AND fa.C_AcctSchema_ID=@C_AcctSchema_ID
                  AND fa.IsActive='Y'
                  AND fa.PostingType=@PostingType_Actual
                  AND fa.C_Period_ID=@C_Period_ID
                  AND fa.DateAcct>=@DateFrom
                  AND fa.DateAcct<=@DateTo
                  AND ev.IsActive='Y'
                  AND ev.AccountType=@AccountType_Expense";

            /* Fact_Acct fa is the main physical table this read fetches from. */
            string finalSql = MRole.GetDefault(ctx).AddAccessSQL(sql, "fa",
                MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            /* GROUP BY and ORDER BY after the access SQL. The ordering expression repeats the
               aggregate rather than referencing its alias - Oracle will not order by a SELECT
               alias inside an aggregate expression. */
            finalSql += " GROUP BY fa.Account_ID,ev.Value,ev.Name,fa.AD_Org_ID,org.Name"
                + " ORDER BY ABS(COALESCE(SUM(COALESCE(fa.AmtAcctDr,0)-COALESCE(fa.AmtAcctCr,0)),0)) DESC,ev.Value";

            DataSet ds = DB.ExecuteDataset(finalSql, parameters.ToArray(), null);
            if (ds == null || ds.Tables.Count == 0) { return; }

            DataTable dt = ds.Tables[0];
            result.TotalRows = dt.Rows.Count;

            for (int i = 0; i < dt.Rows.Count && i < MAX_DetailRows; i++)
            {
                DataRow row = dt.Rows[i];

                DetailRow item = new DetailRow();
                item.Account_ID = Util.GetValueOfInt(row["Account_ID"]);
                item.AccountValue = Util.GetValueOfString(row["Account_Value"]);
                item.AccountName = Util.GetValueOfString(row["Account_Name"]);
                item.AD_Org_ID = Util.GetValueOfInt(row["AD_Org_ID"]);
                item.DimensionName = Util.GetValueOfString(row["Org_Name"]);
                item.PostingCount = Util.GetValueOfInt(row["Posting_Cnt"]);
                item.Amount = Util.GetValueOfDecimal(row["Actual_Amt"]);

                result.Rows.Add(item);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // §5  Helpers
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Builds a parameterized IN list - "@Name0,@Name1,..." - and appends one bind per id.
        /// Every occurrence carries its own name because the backend adapters bind
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
        /// A date as yyyy-MM-dd for the wire. The client formats it in the reader's own locale
        /// - the format is never baked into the data layer with TO_CHAR.
        /// </summary>
        /// <param name="value">Date to serialize.</param>
        /// <returns>yyyy-MM-dd, or an empty string.</returns>
        private string ToIsoDate(DateTime value)
        {
            return value.ToString("yyyy-MM-dd");
        }

        /// <summary>Reads back a date this model serialized itself.</summary>
        /// <param name="value">yyyy-MM-dd.</param>
        /// <returns>The date, or DateTime.MinValue when it cannot be read.</returns>
        private DateTime FromIsoDate(string value)
        {
            DateTime parsed;
            if (DateTime.TryParse(value, out parsed)) { return parsed.Date; }
            return DateTime.MinValue;
        }

        // ─────────────────────────────────────────────────────────────────────
        // §6  Transfer objects
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>The whole chart in one payload, plus what the subtitle is built from.</summary>
        public class ChartResult
        {
            /// <summary>Every active period of the year, in StartDate order - posted or
            /// not.</summary>
            public List<PeriodPoint> Periods { get; set; }

            /// <summary>The financial years the filter can offer, newest first.</summary>
            public List<YearOption> Years { get; set; }

            /// <summary>The accounting schema and its currency - the chart's only currency.</summary>
            public AcctContext Schema { get; set; }

            /// <summary>C_Year_ID actually read, after defaulting and validation.</summary>
            public int C_Year_ID { get; set; }

            /// <summary>C_Year.FiscalYear of that year - the pill's label.</summary>
            public string FiscalYear { get; set; }

            /// <summary>Active periods in the year - the subtitle's first figure.</summary>
            public int PeriodCount { get; set; }

            /// <summary>How many of them carry actual postings.</summary>
            public int PostedPeriodCount { get; set; }

            /// <summary>SUM of every period's budget.</summary>
            public decimal TotalBudget { get; set; }

            /// <summary>SUM of every period's actual.</summary>
            public decimal TotalActual { get; set; }

            /// <summary>TotalActual / TotalBudget * 100; meaningless unless
            /// HasUtilization.</summary>
            public decimal UtilizedPct { get; set; }

            /// <summary>False when the year has no budget at all - a ratio of nothing is not a
            /// number, and the client prints a dash instead.</summary>
            public bool HasUtilization { get; set; }

            /// <summary>C_Period.Name of the latest period carrying actual postings; empty
            /// when nothing has been posted yet.</summary>
            public string PostedThrough { get; set; }

            /// <summary>That period's id.</summary>
            public int PostedThroughPeriodId { get; set; }

            /// <summary>ERROR_* token when the tenant is not configured; empty otherwise.</summary>
            public string ErrorCode { get; set; }

            /// <summary>False only on a failure or a missing configuration; a year with no
            /// postings is Loaded=true with its periods and zeros.</summary>
            public bool Loaded { get; set; }
        }

        /// <summary>
        /// One accounting period and what was budgeted and posted in it. Amounts are in the
        /// PRIMARY accounting schema currency, never converted, and SIGNED - the accounting
        /// convention is preserved.
        /// </summary>
        public class PeriodPoint
        {
            /// <summary>C_Period.C_Period_ID - what the drill-down is opened with.</summary>
            public int C_Period_ID { get; set; }

            /// <summary>C_Period.Name - the X-axis label.</summary>
            public string Name { get; set; }

            /// <summary>C_Period.StartDate, as yyyy-MM-dd. Also the chart's ordering.</summary>
            public string StartDate { get; set; }

            /// <summary>C_Period.EndDate, as yyyy-MM-dd.</summary>
            public string EndDate { get; set; }

            /// <summary>C_Year.FiscalYear - carried on a single period read, for the modal's
            /// title and meta line.</summary>
            public string FiscalYear { get; set; }

            /// <summary>Approved budget for the period: PostingType 'B'.</summary>
            public decimal Budget { get; set; }

            /// <summary>Posted actual for the period: PostingType 'A'.</summary>
            public decimal Actual { get; set; }

            /// <summary>How many ACTUAL facts the period carries.</summary>
            public int ActualCount { get; set; }

            /// <summary>True when the period has been posted to at all - which is what makes
            /// its bar openable. Decided by the COUNT, never by the amount.</summary>
            public bool HasActual { get; set; }

            /// <summary>The budget bar's height, as a percentage of the plot.</summary>
            public decimal BudgetBarPct { get; set; }

            /// <summary>The actual bar's height, as a percentage of the plot.</summary>
            public decimal ActualBarPct { get; set; }
        }

        /// <summary>One period's drill-down: its own figures and the accounts behind them.</summary>
        public class PeriodDetailResult
        {
            /// <summary>The accounts posted to, largest movement first.</summary>
            public List<DetailRow> Rows { get; set; }

            /// <summary>The accounting schema and its currency.</summary>
            public AcctContext Schema { get; set; }

            /// <summary>C_Period_ID actually read.</summary>
            public int C_Period_ID { get; set; }

            /// <summary>C_Period.Name - the modal's title.</summary>
            public string PeriodName { get; set; }

            /// <summary>C_Year.FiscalYear the period belongs to - the modal's meta line.</summary>
            public string FiscalYear { get; set; }

            /// <summary>C_Period.StartDate, as yyyy-MM-dd.</summary>
            public string StartDate { get; set; }

            /// <summary>C_Period.EndDate, as yyyy-MM-dd.</summary>
            public string EndDate { get; set; }

            /// <summary>The period's approved budget.</summary>
            public decimal Budget { get; set; }

            /// <summary>The period's posted actual - what the row list totals to.</summary>
            public decimal Actual { get; set; }

            /// <summary>Budget - Actual.</summary>
            public decimal Variance { get; set; }

            /// <summary>Actual / Budget * 100; meaningless unless HasUtilization.</summary>
            public decimal UtilizedPct { get; set; }

            /// <summary>False when the period has no budget - the modal prints a dash.</summary>
            public bool HasUtilization { get; set; }

            /// <summary>Account / organisation pairs in total, before the payload cap.</summary>
            public int TotalRows { get; set; }

            /// <summary>ERROR_* token when the tenant is not configured; empty otherwise.</summary>
            public string ErrorCode { get; set; }

            /// <summary>False when the period could not be resolved for this tenant.</summary>
            public bool Loaded { get; set; }
        }

        /// <summary>One ledger account and organisation posted to inside a period.</summary>
        public class DetailRow
        {
            /// <summary>Fact_Acct.Account_ID - the natural account (C_ElementValue_ID).</summary>
            public int Account_ID { get; set; }

            /// <summary>C_ElementValue.Value - the account code.</summary>
            public string AccountValue { get; set; }

            /// <summary>C_ElementValue.Name - the account description.</summary>
            public string AccountName { get; set; }

            /// <summary>Fact_Acct.AD_Org_ID the postings were stamped with.</summary>
            public int AD_Org_ID { get; set; }

            /// <summary>AD_Org.Name - the Dimension column.</summary>
            public string DimensionName { get; set; }

            /// <summary>How many facts this account / organisation pair carries.</summary>
            public int PostingCount { get; set; }

            /// <summary>SUM(AmtAcctDr - AmtAcctCr) over them, signed.</summary>
            public decimal Amount { get; set; }
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

            /// <summary>C_Currency.CurSymbol, falling back to ISO_Code.</summary>
            public string Symbol { get; set; }

            /// <summary>C_Currency.StdPrecision.</summary>
            public int Precision { get; set; }
        }
    }
}
