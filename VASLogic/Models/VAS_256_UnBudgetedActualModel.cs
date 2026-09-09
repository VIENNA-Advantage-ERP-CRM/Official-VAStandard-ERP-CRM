/******************************************************
 * Module Name    : VASLogic
 * Purpose        : Unbudgeted Actuals dashboard widget data
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
    /// Module Name : VAS_256_UnBudgetedActual
    /// Purpose     : Backs the VAS_256_UnBudgetedActualWidget dashboard widget - the
    ///               EXPENSE accounts that carry Actual postings in the selected
    ///               financial year with NO matching Budget posting behind them:
    ///
    ///                 Source        Fact_Acct, and only Fact_Acct. This is an accounting
    ///                               finding, so it is read from the ledger rather than
    ///                               from any document table.
    ///                 Schema        AD_ClientInfo.C_AcctSchema1_ID, the PRIMARY
    ///                               accounting schema, on both sides. A Budget posted in
    ///                               a secondary schema must not suppress an Actual in the
    ///                               primary one, so the schema is an equality on the
    ///                               Actual row AND is carried into the Budget existence
    ///                               test from that same row.
    ///                 Accounts      C_ElementValue.AccountType='E' only. Revenue, asset
    ///                               and liability accounts are a different conversation.
    ///                 Year          AD_ClientInfo.C_Calendar_ID -> C_Year -> C_Period.
    ///                               The accounting date window is MIN(StartDate) /
    ///                               MAX(EndDate) over the ACTIVE periods of the selected
    ///                               C_Year_ID - never January to December, and never
    ///                               derived from the calendar month.
    ///                 Grain         Account_ID + AD_OrgTrx_ID. AD_OrgTrx_ID is the
    ///                               TRANSACTION organization and is this widget's
    ///                               Dimension; Fact_Acct.AD_Org_ID is deliberately NOT
    ///                               used for it.
    ///                 Amount        SUM(AmtAcctDr - AmtAcctCr). AmtAcct* is already
    ///                               stated in the accounting schema currency, so there is
    ///                               no currencyConvert call anywhere in this model.
    ///                 Last posted   MAX(DateAcct) of the qualifying Actual postings.
    ///
    ///               THE TEST IS EXISTENCE, NOT COMPARISON. This is not a variance or an
    ///               overspend card: a row qualifies when an Actual exists and a Budget
    ///               posting for the SAME account, the same AD_OrgTrx_ID, the same
    ///               financial year and the same accounting schema does not. A budget of
    ///               zero is still a budget, which is why the predicate is NOT EXISTS
    ///               rather than a sum comparison.
    ///
    ///               A NET-ZERO ACTUAL IS NOT A FINDING. Groups whose debits and credits
    ///               cancel out over the year are dropped by a HAVING clause: the card
    ///               reports money that left the business outside the budget structure,
    ///               and where the net is nil nothing left. Note this is the OPPOSITE
    ///               treatment from the Budget side above, and deliberately so - a budget
    ///               of zero is a decision someone recorded, an actual of zero is an
    ///               absence of money. The two zeros do not mean the same thing.
    ///
    ///               NULL IS ITS OWN DIMENSION VALUE. A posting with no AD_OrgTrx_ID is
    ///               matched only against a Budget posting that also has none, and it is
    ///               grouped separately from AD_OrgTrx_ID 0 - which is the tenant-wide
    ///               '*' organization, a real value rather than an "empty" marker. The
    ///               row therefore carries both the id and an IsOrgTrxNull flag, and the
    ///               drill-down re-applies whichever of the two the row was built from.
    ///
    ///               NO CTE, DELIBERATELY. The widget specification's reference query
    ///               expresses the same result with three CTEs. It is written flat here
    ///               because MRole.AddAccessSQL has to be applied to the MAIN PHYSICAL
    ///               table the user is reading from - Fact_Acct fa - and a CTE alias is
    ///               not a physical table it can resolve through AD_Table. Flattening
    ///               leaves exactly one place for the access clause to go and keeps the
    ///               access parser away from nested derived tables, which it is known to
    ///               struggle with. The two CTEs that only supplied constants (the
    ///               primary schema and the year's date window) are resolved in C# and
    ///               bound as parameters instead. Should a CTE ever be reintroduced here,
    ///               the rule is unchanged: secure each CTE BODY on its own main table
    ///               before the bodies are combined, never the finished statement and
    ///               never the CTE alias.
    ///
    ///               MRole row-level security is applied to Fact_Acct fa on the rows
    ///               query, on the drill-down count and on the drill-down page, and to
    ///               C_Year y on the year list. The joined C_ElementValue / AD_Org /
    ///               AD_Table / AD_Window rows are reference and dictionary lookups and
    ///               inherit the parent's filter. GROUP BY / ORDER BY / the paging suffix
    ///               are appended AFTER AddAccessSQL so its FROM-clause parser never
    ///               meets a trailing clause, and every join ON is a plain equality so it
    ///               never meets a function call either. Compatible with PostgreSQL and
    ///               Oracle.
    /// Chronological development:
    ///   VAI154      2026-09-08 Created
    /// </summary>
    public class VAS_256_UnBudgetedActualModel
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_256_UnBudgetedActualModel).FullName);

        /* Fact_Acct.PostingType stored codes. Stored codes are compared bare - never with
           an N prefix, which would force a Unicode comparison against a VARCHAR column. */
        private const string POSTINGTYPE_Actual = "A";
        private const string POSTINGTYPE_Budget = "B";

        /* C_ElementValue.AccountType stored code for an Expense account. */
        private const string ACCOUNTTYPE_Expense = "E";

        /* Error tokens exchanged with the client; the client resolves the label from
           AD_Message, so no display text is produced here. */
        public const string ERROR_INVALID_REQUEST = "INVALID";
        public const string ERROR_NO_CALENDAR = "NOCALENDAR";
        public const string ERROR_NO_ACCTSCHEMA = "NOACCTSCHEMA";
        public const string ERROR_NO_YEAR = "NOYEAR";

        /* Widget paging. The layout shows three rows; a taller cell may ask for more and
           a short one for fewer, but never outside these bounds. */
        public const int DEFAULT_PageSize = 3;
        private const int MIN_PageSize = 1;
        private const int MAX_PageSize = 12;

        /* Drill-down paging - a separate, larger band: the dialog is a document-baseline
           surface with far more room than a dashboard cell. */
        public const int DEFAULT_TrxPageSize = 8;
        private const int MIN_TrxPageSize = 1;
        private const int MAX_TrxPageSize = 100;

        // ─────────────────────────────────────────────────────────────────────
        // §1  Entry point - the widget's rows
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Everything the card shows in one round trip: the selectable financial years,
        /// the year actually used, the accounting-schema currency, the requested page of
        /// unbudgeted rows, and the total the subtitle reports.
        ///
        /// The TOTAL is the sum over the WHOLE qualifying set, not over the page - the
        /// subtitle states the exposure of the finding, and a per-page figure would
        /// change every time the reader turned a page.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="yearId">C_Year_ID the user selected, or 0 to default to the
        /// financial year containing today.</param>
        /// <param name="pageNo">1-based page; clamped to the available range.</param>
        /// <param name="pageSize">Rows per page; clamped to [1,12].</param>
        /// <returns>Populated <see cref="UnbudgetedResult"/> (never null). Loaded is false
        /// only when there is no context or the tenant is not configured; a tenant with
        /// nothing unbudgeted returns Loaded=true and an empty page, because "nothing
        /// unbudgeted" is a real answer rather than an error.</returns>
        public UnbudgetedResult GetRows(Ctx ctx, int yearId, int pageNo, int pageSize)
        {
            UnbudgetedResult result = new UnbudgetedResult();
            result.Rows = new List<UnbudgetedRow>();
            result.Years = new List<YearOption>();
            result.PageSize = ClampPageSize(pageSize);
            result.Page = pageNo < 1 ? 1 : pageNo;

            if (ctx == null) { result.Page = 1; return result; }

            /* The accounting context is a CONFIGURATION precondition, not a filter:
               without a primary calendar there is no year list to build, and without a
               primary accounting schema there is no ledger to read. Neither is silently
               replaced by "some other" calendar or schema. */
            AcctContext acct = GetAcctContext(ctx);
            result.Schema = acct;

            if (acct.C_Calendar_ID <= 0)
            {
                result.ErrorCode = ERROR_NO_CALENDAR;
                Log.Log(Level.WARNING, "VAS_256_UnBudgetedActual: AD_ClientInfo.C_Calendar_ID not configured for AD_Client_ID="
                    + ctx.GetAD_Client_ID());
                return result;
            }

            if (acct.C_AcctSchema_ID <= 0)
            {
                result.ErrorCode = ERROR_NO_ACCTSCHEMA;
                Log.Log(Level.WARNING, "VAS_256_UnBudgetedActual: AD_ClientInfo.C_AcctSchema1_ID not configured for AD_Client_ID="
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

        /// <summary>Keeps the widget page size inside the range the design allows.</summary>
        /// <param name="pageSize">Requested size.</param>
        /// <returns>Size within [MIN_PageSize, MAX_PageSize].</returns>
        private int ClampPageSize(int pageSize)
        {
            if (pageSize < MIN_PageSize) { return DEFAULT_PageSize; }
            if (pageSize > MAX_PageSize) { return MAX_PageSize; }
            return pageSize;
        }

        /// <summary>Keeps the drill-down page size inside its own range.</summary>
        /// <param name="pageSize">Requested size.</param>
        /// <returns>Size within [MIN_TrxPageSize, MAX_TrxPageSize].</returns>
        private int ClampTrxPageSize(int pageSize)
        {
            if (pageSize < MIN_TrxPageSize || pageSize > MAX_TrxPageSize) { return DEFAULT_TrxPageSize; }
            return pageSize;
        }

        // ─────────────────────────────────────────────────────────────────────
        // §2  Accounting context and the financial-year list
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The tenant's accounting context: the primary calendar, the PRIMARY accounting
        /// schema and the currency every figure on the card is expressed in. Both ids
        /// come from AD_ClientInfo - never from a search over all calendars or all
        /// schemas.
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

            /* CurSymbol first, ISO_Code as the fallback - the widget prints the symbol
               directly against the amount ("$168K"), and only falls back to the code when
               the currency has no symbol configured. */
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
                   primary schema lands here too. Read the calendar on its own so the
                   caller can tell the two configuration errors apart. */
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
        /// The financial years of the tenant's PRIMARY calendar, newest first, each with
        /// the accounting date window derived from its own periods.
        ///
        /// The window is MIN(StartDate) / MAX(EndDate) over the year's ACTIVE periods -
        /// never a January-to-December assumption. A year with no active period has no
        /// window at all and is therefore not offered: there would be no date range to
        /// read Fact_Acct with.
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

            /* GROUP BY and ORDER BY go on AFTER the access SQL - its FROM-clause parser
               must not meet a trailing clause. Ordered by the year's own start date, not
               by FiscalYear: FiscalYear is free text and sorts alphabetically. */
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
        /// A requested id is honoured only when it is one of the years this role may see
        /// on this tenant's primary calendar - a stale or forged selection falls back to
        /// the default rather than reaching Fact_Acct. The default is the year containing
        /// today, then the most recent year that has already started, then the newest year
        /// in the list.
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
        // §3  The rows - Actual exists, matching Budget does not
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Reads every qualifying Account_ID / AD_OrgTrx_ID group of the selected year,
        /// sums the total for the subtitle, and hands the requested page back.
        ///
        /// The WHOLE set is read rather than one page: the subtitle states the total
        /// exposure across every page, so the aggregate has to be computed over all
        /// groups anyway, and the result set is one row per expense account per
        /// transaction organization - an aggregate, not a transaction list. The SQL still
        /// carries the ORDER BY so the slice is deterministic across requests.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="acct">Resolved accounting context (schema and currency).</param>
        /// <param name="year">Resolved financial year with its date window.</param>
        /// <param name="result">Result being filled - paging fields included.</param>
        private void ReadRows(Ctx ctx, AcctContext acct, YearOption year, UnbudgetedResult result)
        {
            StringBuilder sql = new StringBuilder();

            /* AD_Org is joined LEFT and LAST, with a plain equality ON: a posting may
               carry no AD_OrgTrx_ID at all, and the access parser must not meet a
               function call in the closing ON clause.

               AD_OrgTrx_ID is selected RAW, with a separate null flag beside it. Selecting
               COALESCE(...,0) alone would merge two genuinely different dimension values -
               "no transaction organization" and the tenant-wide '*' organization, which is
               AD_Org_ID 0 and a real org - into one row. */
            sql.Append(@"
                SELECT fa.Account_ID AS Account_ID,
                       COALESCE(fa.AD_OrgTrx_ID,0) AS AD_OrgTrx_ID,
                       CASE WHEN fa.AD_OrgTrx_ID IS NULL THEN 'Y' ELSE 'N' END AS OrgTrx_Is_Null,
                       COALESCE(ev.Value,N'') AS Account_Value,
                       COALESCE(ev.Name,N'') AS Account_Name,
                       COALESCE(trxorg.Name,N'') AS Dimension_Name,
                       SUM(COALESCE(fa.AmtAcctDr,0)-COALESCE(fa.AmtAcctCr,0)) AS Actual_Amount,
                       MAX(fa.DateAcct) AS Last_Posted
                FROM Fact_Acct fa
                INNER JOIN C_ElementValue ev ON (ev.C_ElementValue_ID=fa.Account_ID)
                LEFT OUTER JOIN AD_Org trxorg ON (trxorg.AD_Org_ID=fa.AD_OrgTrx_ID)
                WHERE ").Append(RowWhere());

            /* Fact_Acct fa is the main physical table the user is reading from: the role's
               access clause goes HERE, on the base query, and never on a derived alias. */
            string rowSql = MRole.GetDefault(ctx).AddAccessSQL(sql.ToString(), "fa",
                MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            /* GROUP BY, HAVING and ORDER BY after the access SQL - its FROM-clause parser
               must not meet a trailing clause. The grain is the raw AD_OrgTrx_ID, so NULL
               forms its own group and is never folded into org 0.

               HAVING <> 0 DROPS THE NET-ZERO GROUPS. An account whose debits and credits
               cancel out over the year - posted and then fully reversed, or cleared to
               nil - has no unbudgeted exposure to report: the card exists to show money
               that left the business outside the budget structure, and nothing left. Such
               rows are also the ones a reader can do least with, because a $0.00 line
               offers nothing to act on while still costing a row of a three-row card.
               They contribute nothing to the subtitle total either, so removing them
               changes the count and the pages but never the headline figure.

               This is a filter on the NET, not on the postings: a group is dropped only
               when its debits and credits sum to nothing, never because an individual
               posting was zero. The drill-down still shows every posting behind a row
               that survives, including any zero-value ones.

               Note this is deliberately the OPPOSITE treatment from the Budget side,
               where a zero-value budget row still counts as a budget (see the NOT EXISTS
               in RowWhere). A budget of zero is a decision someone made; an actual of
               zero is an absence of money.

               Largest absolute exposure first - a credit-heavy expense account is as much
               of a finding as a debit-heavy one - with the account value and the dimension
               as deterministic tiebreakers so a page boundary cannot shuffle. */
            rowSql += " GROUP BY fa.Account_ID,fa.AD_OrgTrx_ID,ev.Value,ev.Name,trxorg.Name"
                + " HAVING SUM(COALESCE(fa.AmtAcctDr,0)-COALESCE(fa.AmtAcctCr,0))<>0"
                + " ORDER BY ABS(SUM(COALESCE(fa.AmtAcctDr,0)-COALESCE(fa.AmtAcctCr,0))) DESC,"
                + "COALESCE(ev.Value,N''),COALESCE(trxorg.Name,N''),fa.Account_ID";

            DataSet ds = DB.ExecuteDataset(rowSql, RowParameters(ctx, acct, year), null);
            if (ds == null || ds.Tables.Count == 0) { return; }

            DataTable dt = ds.Tables[0];

            List<UnbudgetedRow> all = new List<UnbudgetedRow>();
            decimal total = 0;

            for (int i = 0; i < dt.Rows.Count; i++)
            {
                UnbudgetedRow row = MapRow(dt.Rows[i]);
                total += row.Amount;
                all.Add(row);
            }

            result.TotalAmount = total;
            result.TotalRows = all.Count;
            result.TotalPages = result.PageSize > 0
                ? (int)Math.Ceiling((double)result.TotalRows / result.PageSize)
                : 0;

            if (result.TotalRows == 0) { result.Page = 1; return; }

            /* Clamp the page AFTER the total is known: a page number the client kept from
               a longer year must land on the last real page, never past the end. */
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
        /// The predicate that defines an unbudgeted actual, shared by nothing else so it
        /// can state the rule once and in full.
        ///
        /// The Budget existence test carries the CLIENT and the ACCOUNTING SCHEMA over
        /// from the Actual row rather than re-binding them, so a Budget in another schema
        /// can never suppress an Actual in the primary one. The dimension test spells the
        /// null case out explicitly because NULL = NULL is unknown in SQL, and a posting
        /// with no transaction organization must be matched by a Budget that also has
        /// none.
        /// </summary>
        /// <returns>Predicate binding @AD_Client_ID, @C_AcctSchema_ID, @PostingType_Actual,
        /// @DateFrom, @DateTo, @AccountType_Expense, @PostingType_Budget, @BudgetDateFrom
        /// and @BudgetDateTo, in that order.</returns>
        private string RowWhere()
        {
            return @"fa.AD_Client_ID=@AD_Client_ID
                  AND fa.C_AcctSchema_ID=@C_AcctSchema_ID
                  AND fa.PostingType=@PostingType_Actual
                  AND fa.IsActive='Y'
                  AND fa.DateAcct>=@DateFrom
                  AND fa.DateAcct<=@DateTo
                  AND ev.IsActive='Y'
                  AND ev.AccountType=@AccountType_Expense
                  AND NOT EXISTS(SELECT 1 FROM Fact_Acct fb WHERE fb.AD_Client_ID=fa.AD_Client_ID AND fb.C_AcctSchema_ID=fa.C_AcctSchema_ID AND fb.PostingType=@PostingType_Budget AND fb.IsActive='Y' AND fb.Account_ID=fa.Account_ID AND fb.DateAcct>=@BudgetDateFrom AND fb.DateAcct<=@BudgetDateTo AND (fb.AD_OrgTrx_ID=fa.AD_OrgTrx_ID OR (fb.AD_OrgTrx_ID IS NULL AND fa.AD_OrgTrx_ID IS NULL)))";
        }

        /// <summary>
        /// Bind values for <see cref="RowWhere"/>, in the order the placeholders appear in
        /// the SQL text - the backend adapters bind positionally, so every occurrence
        /// carries its own name and the order is the contract.
        /// </summary>
        /// <param name="ctx">Session context (supplies the tenant).</param>
        /// <param name="acct">Resolved accounting context.</param>
        /// <param name="year">Resolved financial year with its date window.</param>
        /// <returns>Bind array.</returns>
        private SqlParameter[] RowParameters(Ctx ctx, AcctContext acct, YearOption year)
        {
            return new SqlParameter[]
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@C_AcctSchema_ID", acct.C_AcctSchema_ID),
                new SqlParameter("@PostingType_Actual", POSTINGTYPE_Actual),
                new SqlParameter("@DateFrom", year.StartDate),
                new SqlParameter("@DateTo", year.EndDate),
                new SqlParameter("@AccountType_Expense", ACCOUNTTYPE_Expense),
                new SqlParameter("@PostingType_Budget", POSTINGTYPE_Budget),
                new SqlParameter("@BudgetDateFrom", year.StartDate),
                new SqlParameter("@BudgetDateTo", year.EndDate)
            };
        }

        /// <summary>Materialises one Account / Dimension group.</summary>
        /// <param name="row">Row carrying the group aliases.</param>
        /// <returns>Populated <see cref="UnbudgetedRow"/>.</returns>
        private UnbudgetedRow MapRow(DataRow row)
        {
            UnbudgetedRow item = new UnbudgetedRow();

            item.Account_ID = Util.GetValueOfInt(row["Account_ID"]);
            item.AD_OrgTrx_ID = Util.GetValueOfInt(row["AD_OrgTrx_ID"]);
            item.IsOrgTrxNull = "Y".Equals(Util.GetValueOfString(row["OrgTrx_Is_Null"]));
            item.AccountValue = Util.GetValueOfString(row["Account_Value"]);
            item.AccountName = Util.GetValueOfString(row["Account_Name"]);
            item.Dimension = Util.GetValueOfString(row["Dimension_Name"]);
            item.Amount = Util.GetValueOfDecimal(row["Actual_Amount"]);
            item.LastPosted = ToIsoDate(Util.GetValueOfDateTime(row["Last_Posted"]));

            return item;
        }

        // ─────────────────────────────────────────────────────────────────────
        // §4  Drill-down - the Actual postings behind one row
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// One page of the Actual postings behind a single widget row.
        ///
        /// The dialog must reconcile to the row that opened it, so it re-applies exactly
        /// the same filters the row was built from: the primary accounting schema, the
        /// selected financial year's date window, PostingType 'A', the account, and the
        /// SAME transaction organization - including the "no organization at all" case,
        /// which is re-applied as IS NULL rather than as an equality with 0.
        ///
        /// Nothing the client sends is trusted: the year is re-resolved against the
        /// tenant's own calendar, the schema comes from AD_ClientInfo, and the account has
        /// to be an active Expense account or the request is refused.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="yearId">C_Year_ID the row was read for.</param>
        /// <param name="accountId">C_ElementValue_ID of the row's account.</param>
        /// <param name="orgTrxId">AD_OrgTrx_ID of the row; ignored when isOrgTrxNull.</param>
        /// <param name="isOrgTrxNull">True when the row's dimension is "no transaction
        /// organization" rather than a real one.</param>
        /// <param name="pageNo">1-based page number.</param>
        /// <param name="pageSize">Rows per page; clamped to [1,100].</param>
        /// <returns>Populated <see cref="TransactionPage"/> (never null).</returns>
        public TransactionPage GetTransactions(Ctx ctx, int yearId, int accountId, int orgTrxId,
            bool isOrgTrxNull, int pageNo, int pageSize)
        {
            TransactionPage result = new TransactionPage();
            result.Rows = new List<TransactionRow>();
            result.Account_ID = accountId;
            result.AD_OrgTrx_ID = orgTrxId;
            result.IsOrgTrxNull = isOrgTrxNull;
            result.Page = 1;
            result.PageSize = ClampTrxPageSize(pageSize);

            if (ctx == null) { return result; }

            if (accountId <= 0)
            {
                result.ErrorCode = ERROR_INVALID_REQUEST;
                return result;
            }

            AcctContext acct = GetAcctContext(ctx);
            result.Schema = acct;

            if (acct.C_Calendar_ID <= 0) { result.ErrorCode = ERROR_NO_CALENDAR; return result; }
            if (acct.C_AcctSchema_ID <= 0) { result.ErrorCode = ERROR_NO_ACCTSCHEMA; return result; }

            List<YearOption> years = GetYears(ctx, acct.C_Calendar_ID);
            if (years.Count == 0) { result.ErrorCode = ERROR_NO_YEAR; return result; }

            YearOption year = PickYear(years, yearId, DateTime.Now.Date);
            result.C_Year_ID = year.C_Year_ID;
            result.FiscalYear = year.FiscalYear;

            /* Authorization, not decoration: only an ACTIVE EXPENSE account is readable
               through this method, because only expense accounts can produce a widget row
               in the first place. */
            AccountLabel account = GetExpenseAccount(ctx, accountId);
            if (account == null)
            {
                result.ErrorCode = ERROR_INVALID_REQUEST;
                return result;
            }

            result.AccountValue = account.Value;
            result.AccountName = account.Name;
            result.Dimension = GetOrgName(ctx, orgTrxId, isOrgTrxNull);

            result.Total = CountTransactions(ctx, acct, year, accountId, orgTrxId, isOrgTrxNull);
            result.TotalPages = result.PageSize > 0
                ? (int)Math.Ceiling((double)result.Total / result.PageSize)
                : 0;

            if (result.Total == 0) { return result; }

            int page = pageNo < 1 ? 1 : pageNo;
            if (result.TotalPages > 0 && page > result.TotalPages) { page = result.TotalPages; }
            result.Page = page;

            ReadTransactions(ctx, acct, year, accountId, orgTrxId, isOrgTrxNull, result);
            ApplyNavigability(result.Rows);

            return result;
        }

        /// <summary>
        /// How many Actual postings the row stands for. Counted with the same predicate
        /// and the same role filter as the page itself, so the pager can never promise a
        /// page the list will not produce.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="acct">Resolved accounting context.</param>
        /// <param name="year">Resolved financial year with its date window.</param>
        /// <param name="accountId">Authorized expense account id.</param>
        /// <param name="orgTrxId">AD_OrgTrx_ID of the row.</param>
        /// <param name="isOrgTrxNull">True when the row's dimension is null.</param>
        /// <returns>Row count.</returns>
        private int CountTransactions(Ctx ctx, AcctContext acct, YearOption year, int accountId,
            int orgTrxId, bool isOrgTrxNull)
        {
            string sql = @"
                SELECT COUNT(fa.Fact_Acct_ID) AS Row_Cnt
                FROM Fact_Acct fa
                WHERE " + TransactionWhere(isOrgTrxNull);

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "fa", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            DataSet ds = DB.ExecuteDataset(sql,
                TransactionParameters(ctx, acct, year, accountId, orgTrxId, isOrgTrxNull), null);

            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) { return 0; }

            return Util.GetValueOfInt(ds.Tables[0].Rows[0]["Row_Cnt"]);
        }

        /// <summary>
        /// One page of Actual postings, newest accounting date first.
        ///
        /// AD_Table and AD_Window are joined LEFT: a posting whose source table has since
        /// been deactivated still has an amount the widget row counted, and dropping it
        /// here would leave the list short of the total the pager was built from. Such a
        /// row falls back to its raw AD_Table_ID for a label and is simply not navigable.
        /// Fact_Acct.AD_Window_ID is legitimately null or zero on many postings, which is
        /// the other reason AD_Window cannot be INNER.
        ///
        /// The screen label prefers the session language's window translation, then the
        /// window's DisplayName, then its Name, then the table's Name, then the physical
        /// TableName. Two names it deliberately does NOT reference: AD_Table.DisplayName
        /// and AD_Table_Trl.Name - neither exists in this schema.
        ///
        /// C_BPartner is joined LEFT and LAST. LEFT because Fact_Acct.C_BPartner_ID is
        /// null on any posting with no counterparty - a depreciation run, an accrual, a
        /// manual journal - and those are exactly the postings an unbudgeted expense
        /// tends to be; an INNER join would silently drop them and leave the list short
        /// of the total the pager was built from. LAST because its ON is a plain equality,
        /// and the access parser is happiest when the closing ON carries no function call
        /// and no bind. It is a display lookup with no IsActive filter: a partner
        /// deactivated since the posting was made still has the name the ledger was
        /// written under, which is the name the reader needs.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="acct">Resolved accounting context.</param>
        /// <param name="year">Resolved financial year with its date window.</param>
        /// <param name="accountId">Authorized expense account id.</param>
        /// <param name="orgTrxId">AD_OrgTrx_ID of the row.</param>
        /// <param name="isOrgTrxNull">True when the row's dimension is null.</param>
        /// <param name="result">Page being filled.</param>
        private void ReadTransactions(Ctx ctx, AcctContext acct, YearOption year, int accountId,
            int orgTrxId, bool isOrgTrxNull, TransactionPage result)
        {
            string sql = @"
                SELECT fa.Fact_Acct_ID AS Fact_Acct_ID,
                       fa.AD_Table_ID AS AD_Table_ID,
                       COALESCE(fa.Record_ID,0) AS Record_ID,
                       COALESCE(w.AD_Window_ID,0) AS Window_Resolved_ID,
                       fa.DateAcct AS Date_Acct,
                       COALESCE(fa.Description,N'') AS Fact_Description,
                       COALESCE(fa.AmtAcctDr,0) AS Amt_Acct_Dr,
                       COALESCE(fa.AmtAcctCr,0) AS Amt_Acct_Cr,
                       COALESCE(wtrl.Name,w.DisplayName,w.Name,t.Name,t.TableName,N'') AS Screen_Name,
                       COALESCE(bp.Name,N'') AS Partner_Name
                FROM Fact_Acct fa
                LEFT OUTER JOIN AD_Table t ON (t.AD_Table_ID=fa.AD_Table_ID AND t.IsActive='Y')
                LEFT OUTER JOIN AD_Window w ON (w.AD_Window_ID=fa.AD_Window_ID AND w.IsActive='Y')
                LEFT OUTER JOIN AD_Window_Trl wtrl ON (wtrl.AD_Window_ID=w.AD_Window_ID AND wtrl.AD_Language=@AD_Language AND wtrl.IsActive='Y')
                LEFT OUTER JOIN C_BPartner bp ON (bp.C_BPartner_ID=fa.C_BPartner_ID)
                WHERE " + TransactionWhere(isOrgTrxNull);

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "fa", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            /* Sort and paging after the access SQL. Fact_Acct_ID breaks the DateAcct tie so
               paging is stable across requests. OFFSET / FETCH is ANSI and is supported by
               PostgreSQL 12+ and Oracle 12c+ alike, so there is no dialect branch here; the
               two numbers are server-clamped integers, never client text. */
            int offset = (result.Page - 1) * result.PageSize;
            sql += " ORDER BY fa.DateAcct DESC,fa.Fact_Acct_ID DESC"
                + " OFFSET " + offset + " ROWS FETCH NEXT " + result.PageSize + " ROWS ONLY";

            /* Appearance order: the window-translation join binds first, then the WHERE. */
            List<SqlParameter> parameters = new List<SqlParameter>();
            parameters.Add(new SqlParameter("@AD_Language", ctx.GetAD_Language()));
            parameters.AddRange(TransactionParameters(ctx, acct, year, accountId, orgTrxId, isOrgTrxNull));

            DataSet ds = DB.ExecuteDataset(sql, parameters.ToArray(), null);
            if (ds == null || ds.Tables.Count == 0) { return; }

            DataTable dt = ds.Tables[0];
            for (int i = 0; i < dt.Rows.Count; i++)
            {
                DataRow dr = dt.Rows[i];

                TransactionRow row = new TransactionRow();
                row.Fact_Acct_ID = Util.GetValueOfInt(dr["Fact_Acct_ID"]);
                row.AD_Table_ID = Util.GetValueOfInt(dr["AD_Table_ID"]);
                row.Record_ID = Util.GetValueOfInt(dr["Record_ID"]);
                row.AD_Window_ID = Util.GetValueOfInt(dr["Window_Resolved_ID"]);
                row.DateAcct = ToIsoDate(Util.GetValueOfDateTime(dr["Date_Acct"]));
                row.Description = Util.GetValueOfString(dr["Fact_Description"]);
                row.Debit = Util.GetValueOfDecimal(dr["Amt_Acct_Dr"]);
                row.Credit = Util.GetValueOfDecimal(dr["Amt_Acct_Cr"]);
                row.Amount = row.Debit - row.Credit;
                row.ScreenDisplayName = Util.GetValueOfString(dr["Screen_Name"]);
                row.BusinessPartner = Util.GetValueOfString(dr["Partner_Name"]);

                /* Neither the window nor the table resolved - name the row by the raw table
                   id rather than leaving the Screen column blank. */
                if (String.IsNullOrEmpty(row.ScreenDisplayName))
                {
                    row.ScreenDisplayName = "#" + row.AD_Table_ID;
                }

                result.Rows.Add(row);
            }
        }

        /// <summary>
        /// The predicate shared by the drill-down count and the drill-down page, so the
        /// two can never drift apart.
        ///
        /// The dimension clause has two shapes because NULL and 0 are different dimension
        /// values: a row built from postings with no transaction organization is re-read
        /// with IS NULL, and one built from a real organization - the tenant-wide '*' org,
        /// AD_Org_ID 0, included - with an equality.
        /// </summary>
        /// <param name="isOrgTrxNull">True when the row's dimension is null.</param>
        /// <returns>Predicate binding @AD_Client_ID, @C_AcctSchema_ID, @PostingType_Actual,
        /// @Account_ID, @DateFrom, @DateTo and - unless isOrgTrxNull - @AD_OrgTrx_ID, in
        /// that order.</returns>
        private string TransactionWhere(bool isOrgTrxNull)
        {
            StringBuilder where = new StringBuilder();
            where.Append(@"fa.AD_Client_ID=@AD_Client_ID
                  AND fa.C_AcctSchema_ID=@C_AcctSchema_ID
                  AND fa.PostingType=@PostingType_Actual
                  AND fa.IsActive='Y'
                  AND fa.Account_ID=@Account_ID
                  AND fa.DateAcct>=@DateFrom
                  AND fa.DateAcct<=@DateTo");

            where.Append(isOrgTrxNull
                ? " AND fa.AD_OrgTrx_ID IS NULL"
                : " AND fa.AD_OrgTrx_ID=@AD_OrgTrx_ID");

            return where.ToString();
        }

        /// <summary>Bind values for <see cref="TransactionWhere"/>, in appearance order.</summary>
        /// <param name="ctx">Session context (supplies the tenant).</param>
        /// <param name="acct">Resolved accounting context.</param>
        /// <param name="year">Resolved financial year with its date window.</param>
        /// <param name="accountId">Authorized expense account id.</param>
        /// <param name="orgTrxId">AD_OrgTrx_ID of the row.</param>
        /// <param name="isOrgTrxNull">True when the row's dimension is null.</param>
        /// <returns>Bind array.</returns>
        private SqlParameter[] TransactionParameters(Ctx ctx, AcctContext acct, YearOption year,
            int accountId, int orgTrxId, bool isOrgTrxNull)
        {
            List<SqlParameter> parameters = new List<SqlParameter>();
            parameters.Add(new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()));
            parameters.Add(new SqlParameter("@C_AcctSchema_ID", acct.C_AcctSchema_ID));
            parameters.Add(new SqlParameter("@PostingType_Actual", POSTINGTYPE_Actual));
            parameters.Add(new SqlParameter("@Account_ID", accountId));
            parameters.Add(new SqlParameter("@DateFrom", year.StartDate));
            parameters.Add(new SqlParameter("@DateTo", year.EndDate));

            if (!isOrgTrxNull) { parameters.Add(new SqlParameter("@AD_OrgTrx_ID", orgTrxId)); }

            return parameters.ToArray();
        }

        /// <summary>
        /// The Value / Name of one ACTIVE EXPENSE account, or null when the id is not one.
        /// This is the drill-down's gate: only an account that could have produced a widget
        /// row in the first place is readable through it.
        ///
        /// C_ElementValue is the same reference lookup the rows query joins, so it is
        /// resolved the same way - without its own MRole predicate. The authorization that
        /// matters is applied where the data actually is: the ledger read that follows is
        /// MRole-secured on Fact_Acct, the main physical table. Filtering the reference
        /// table too would let the dialog refuse a row the card had already displayed.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="accountId">C_ElementValue_ID the client sent.</param>
        /// <returns>Populated <see cref="AccountLabel"/>, or null.</returns>
        private AccountLabel GetExpenseAccount(Ctx ctx, int accountId)
        {
            string sql = @"
                SELECT COALESCE(ev.Value,N'') AS Account_Value,
                       COALESCE(ev.Name,N'') AS Account_Name
                FROM C_ElementValue ev
                WHERE ev.C_ElementValue_ID=@C_ElementValue_ID
                  AND ev.IsActive='Y'
                  AND ev.AccountType=@AccountType_Expense";

            /* Appearance order, because the backend adapters bind positionally. The two
               predicates that remain are exactly the ones the rows query applies to
               C_ElementValue, so the dialog qualifies an account on the same terms the card
               did. C_ElementValue_ID is the primary key, so no tenant predicate is needed
               to identify the row, and the ledger read that follows is tenant-filtered and
               MRole-secured on Fact_Acct. */
            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@C_ElementValue_ID", accountId),
                new SqlParameter("@AccountType_Expense", ACCOUNTTYPE_Expense)
            };

            DataSet ds = DB.ExecuteDataset(sql, parameters, null);
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) { return null; }

            DataRow row = ds.Tables[0].Rows[0];

            AccountLabel label = new AccountLabel();
            label.Value = Util.GetValueOfString(row["Account_Value"]);
            label.Name = Util.GetValueOfString(row["Account_Name"]);

            return label;
        }

        /// <summary>
        /// The organization name behind a row's Dimension, for the dialog's header.
        ///
        /// AD_Org_ID 0 is looked up like any other id - it is the tenant-wide '*'
        /// organization, a real row of AD_Org. Only the null case has no name at all, and
        /// the client supplies its own label for that.
        ///
        /// This is the same display lookup the rows query performs as a LEFT OUTER JOIN,
        /// so it is resolved the same way - without its own MRole predicate. AD_OrgTrx_ID
        /// is a DIMENSION on the posting rather than the posting's owning organization,
        /// and it may legitimately name an organization outside the role's access; adding
        /// the access clause here would blank a name the card had already shown.
        /// </summary>
        /// <param name="ctx">Session context (unused today; kept for symmetry with the
        /// other reads, and so an access predicate can be added here without a signature
        /// change should AD_OrgTrx_ID ever become role-scoped).</param>
        /// <param name="orgTrxId">AD_OrgTrx_ID of the row.</param>
        /// <param name="isOrgTrxNull">True when the row's dimension is null.</param>
        /// <returns>Organization name, or an empty string.</returns>
        private string GetOrgName(Ctx ctx, int orgTrxId, bool isOrgTrxNull)
        {
            if (isOrgTrxNull || orgTrxId < 0) { return ""; }

            string sql = @"
                SELECT COALESCE(org.Name,N'') AS Org_Name
                FROM AD_Org org
                WHERE org.AD_Org_ID=@AD_Org_ID";

            /* No tenant predicate and no IsActive predicate, both deliberately. AD_Org_ID
               is the table's primary key, so the equality already identifies exactly one
               row, and the id itself came out of the already-secured Fact_Acct read rather
               than from the browser. A tenant filter would be actively wrong here: the '*'
               organization is AD_Org_ID 0 and belongs to the SYSTEM client, so filtering by
               the session's client would blank the very dimension value the card shows most
               often. An organization deactivated since the posting was made still has the
               name the ledger was written under, which is the name the reader needs. */
            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@AD_Org_ID", orgTrxId)
            };

            DataSet ds = DB.ExecuteDataset(sql, parameters, null);
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) { return ""; }

            return Util.GetValueOfString(ds.Tables[0].Rows[0]["Org_Name"]);
        }

        // ─────────────────────────────────────────────────────────────────────
        // §5  Drill-down navigability
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Marks which drill-down rows can open their source record, and with which key
        /// column.
        ///
        /// Fact_Acct stores AD_Table_ID / Record_ID rather than a window and a key, so the
        /// key column is read from the Application Dictionary - ONE query for the whole
        /// page, grouped by table, never one per row. A row is navigable only when it has
        /// a real record, an active window the posting itself names, and a single-column
        /// key to position by; anything less stays plain text, because a link that
        /// navigates nowhere is worse than no link.
        ///
        /// AD_Column is dictionary metadata rather than tenant data and carries no
        /// organization dimension, so no MRole predicate is applied to it - the same
        /// treatment the sibling accounting widgets give this lookup. No name here ever
        /// originates from the browser: the table ids come out of the already-secured
        /// Fact_Acct page.
        /// </summary>
        /// <param name="rows">Page rows to mark.</param>
        private void ApplyNavigability(List<TransactionRow> rows)
        {
            if (rows == null || rows.Count == 0) { return; }

            List<int> tableIds = new List<int>();
            for (int i = 0; i < rows.Count; i++)
            {
                int id = rows[i].AD_Table_ID;
                if (id > 0 && !tableIds.Contains(id)) { tableIds.Add(id); }
            }

            if (tableIds.Count == 0) { return; }

            List<SqlParameter> parameters = new List<SqlParameter>();
            string inList = BuildIdInList(tableIds, "@AD_Table_ID", parameters);

            string sql = @"
                SELECT c.AD_Table_ID AS AD_Table_ID,
                       c.ColumnName AS Column_Name
                FROM AD_Column c
                WHERE c.IsActive='Y'
                  AND c.IsKey='Y'
                  AND c.AD_Table_ID IN (" + inList + @")
                ORDER BY c.AD_Table_ID,c.AD_Column_ID";

            DataSet ds = DB.ExecuteDataset(sql, parameters.ToArray(), null);
            if (ds == null || ds.Tables.Count == 0) { return; }

            Dictionary<int, string> keyByTable = new Dictionary<int, string>();
            DataTable dt = ds.Tables[0];

            for (int i = 0; i < dt.Rows.Count; i++)
            {
                int tableId = Util.GetValueOfInt(dt.Rows[i]["AD_Table_ID"]);
                string columnName = Util.GetValueOfString(dt.Rows[i]["Column_Name"]);

                /* A multi-column key has no single column to position a window on; the
                   first one alone would land on the wrong record, so such a table is left
                   un-navigable. */
                if (String.IsNullOrEmpty(columnName)) { continue; }

                if (keyByTable.ContainsKey(tableId)) { keyByTable[tableId] = ""; }
                else { keyByTable[tableId] = columnName; }
            }

            for (int i = 0; i < rows.Count; i++)
            {
                TransactionRow row = rows[i];
                if (row.Record_ID <= 0 || row.AD_Window_ID <= 0) { continue; }

                string keyColumn;
                if (!keyByTable.TryGetValue(row.AD_Table_ID, out keyColumn)) { continue; }
                if (String.IsNullOrEmpty(keyColumn)) { continue; }

                row.KeyColumnName = keyColumn;
                row.CanNavigate = true;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // §6  Helpers
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Builds a parameterized IN list - "@Name0,@Name1,..." - and appends one bind per
        /// id. Every occurrence carries its own name because the backend adapters bind
        /// positionally, so a repeated name would be ambiguous.
        /// </summary>
        /// <param name="ids">Ids to bind (server-sourced, never client text).</param>
        /// <param name="prefix">Bind name prefix, e.g. "@AD_Table_ID".</param>
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
        /// <param name="value">Date to serialize; may be null.</param>
        /// <returns>yyyy-MM-dd, or an empty string.</returns>
        private string ToIsoDate(DateTime? value)
        {
            return value.HasValue ? value.Value.ToString("yyyy-MM-dd") : "";
        }

        // ─────────────────────────────────────────────────────────────────────
        // §7  Transfer objects
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>One page of the widget, plus what the page cannot know by itself.</summary>
        public class UnbudgetedResult
        {
            /// <summary>The requested page of unbudgeted rows, largest exposure first.</summary>
            public List<UnbudgetedRow> Rows { get; set; }

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

            /// <summary>Qualifying Account / Dimension groups in total - the pager's figure.</summary>
            public int TotalRows { get; set; }

            /// <summary>CEILING(TotalRows / PageSize).</summary>
            public int TotalPages { get; set; }

            /// <summary>SUM of the Actual amount across EVERY qualifying row, not just the
            /// page - the figure the subtitle reports.</summary>
            public decimal TotalAmount { get; set; }

            /// <summary>ERROR_* token when the tenant is not configured; empty otherwise.</summary>
            public string ErrorCode { get; set; }

            /// <summary>False only on a failure or a missing configuration; a tenant with
            /// nothing unbudgeted is Loaded=true with an empty page.</summary>
            public bool Loaded { get; set; }
        }

        /// <summary>
        /// One Account / Dimension group with no matching budget line. Amounts are in the
        /// PRIMARY accounting schema currency and are never converted - AmtAcctDr /
        /// AmtAcctCr are already stated in it.
        /// </summary>
        public class UnbudgetedRow
        {
            /// <summary>Fact_Acct.Account_ID - the natural account (C_ElementValue_ID).</summary>
            public int Account_ID { get; set; }

            /// <summary>C_ElementValue.Value - the account code.</summary>
            public string AccountValue { get; set; }

            /// <summary>C_ElementValue.Name - the account description.</summary>
            public string AccountName { get; set; }

            /// <summary>Fact_Acct.AD_OrgTrx_ID, the widget's Dimension. 0 means either the
            /// tenant-wide '*' organization or "none at all" - IsOrgTrxNull tells the two
            /// apart, and the drill-down needs both.</summary>
            public int AD_OrgTrx_ID { get; set; }

            /// <summary>True when the postings carry NO transaction organization.</summary>
            public bool IsOrgTrxNull { get; set; }

            /// <summary>AD_Org.Name of AD_OrgTrx_ID. The card labels this column
            /// "Organization Unit"; the property keeps the accounting term because
            /// AD_OrgTrx_ID is a Fact_Acct DIMENSION, and renaming the wire contract to a
            /// display caption would tie it to one screen's wording.
            ///
            /// EMPTY is a real answer - a posting with no transaction organization has no
            /// name to carry - and the client renders it as a centred dash rather than
            /// inventing a word for it.</summary>
            public string Dimension { get; set; }

            /// <summary>SUM(AmtAcctDr - AmtAcctCr) over the group. Never zero - a group
            /// whose debits and credits cancel out is filtered off by the HAVING clause
            /// in ReadRows, because a nil net is not unbudgeted exposure. May be negative
            /// on a credit-heavy expense account, which IS a finding.</summary>
            public decimal Amount { get; set; }

            /// <summary>MAX(DateAcct) over the group, as yyyy-MM-dd.</summary>
            public string LastPosted { get; set; }
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

            /// <summary>AD_ClientInfo.C_AcctSchema1_ID - the PRIMARY schema, and the only
            /// one this widget reads.</summary>
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

        /// <summary>One page of the drill-down dialog.</summary>
        public class TransactionPage
        {
            /// <summary>The requested page of Actual postings, newest first.</summary>
            public List<TransactionRow> Rows { get; set; }

            /// <summary>The accounting schema and its currency.</summary>
            public AcctContext Schema { get; set; }

            /// <summary>C_Year_ID the postings were read for.</summary>
            public int C_Year_ID { get; set; }

            /// <summary>C_Year.FiscalYear of that year.</summary>
            public string FiscalYear { get; set; }

            /// <summary>The account the dialog was opened on.</summary>
            public int Account_ID { get; set; }

            /// <summary>C_ElementValue.Value of that account.</summary>
            public string AccountValue { get; set; }

            /// <summary>C_ElementValue.Name of that account.</summary>
            public string AccountName { get; set; }

            /// <summary>AD_OrgTrx_ID the dialog was opened on.</summary>
            public int AD_OrgTrx_ID { get; set; }

            /// <summary>True when the dialog was opened on the "no transaction
            /// organization" dimension rather than a real one.</summary>
            public bool IsOrgTrxNull { get; set; }

            /// <summary>AD_Org.Name of that organization; empty when there is none.</summary>
            public string Dimension { get; set; }

            /// <summary>1-based page number actually served.</summary>
            public int Page { get; set; }

            /// <summary>Rows per page actually used, after clamping.</summary>
            public int PageSize { get; set; }

            /// <summary>Postings in total behind the widget row.</summary>
            public int Total { get; set; }

            /// <summary>CEILING(Total / PageSize).</summary>
            public int TotalPages { get; set; }

            /// <summary>ERROR_* token when the request could not be served; empty otherwise.</summary>
            public string ErrorCode { get; set; }
        }

        /// <summary>One Actual posting behind a widget row.</summary>
        public class TransactionRow
        {
            /// <summary>Fact_Acct.Fact_Acct_ID.</summary>
            public int Fact_Acct_ID { get; set; }

            /// <summary>Fact_Acct.AD_Table_ID - the source document's table.</summary>
            public int AD_Table_ID { get; set; }

            /// <summary>Fact_Acct.Record_ID - the source document's id.</summary>
            public int Record_ID { get; set; }

            /// <summary>The window the posting itself names, confirmed active.</summary>
            public int AD_Window_ID { get; set; }

            /// <summary>Key column of the source table, when it has exactly one.</summary>
            public string KeyColumnName { get; set; }

            /// <summary>True when the row can open its source record.</summary>
            public bool CanNavigate { get; set; }

            /// <summary>Fact_Acct.DateAcct as yyyy-MM-dd; the client formats it.</summary>
            public string DateAcct { get; set; }

            /// <summary>The source screen's translated name.</summary>
            public string ScreenDisplayName { get; set; }

            /// <summary>Fact_Acct.Description; may be empty.</summary>
            public string Description { get; set; }

            /// <summary>C_BPartner.Name of Fact_Acct.C_BPartner_ID - the counterparty the
            /// posting was made against. EMPTY is a real answer and a common one: a
            /// depreciation run, an accrual or a manual journal has no counterparty at
            /// all. The client renders that as its centred dash.</summary>
            public string BusinessPartner { get; set; }

            /// <summary>Fact_Acct.AmtAcctDr.</summary>
            public decimal Debit { get; set; }

            /// <summary>Fact_Acct.AmtAcctCr.</summary>
            public decimal Credit { get; set; }

            /// <summary>Debit - Credit, the posting's net effect on the account.</summary>
            public decimal Amount { get; set; }
        }

        /// <summary>An account's code and description, for the dialog header.</summary>
        private class AccountLabel
        {
            /// <summary>C_ElementValue.Value.</summary>
            public string Value { get; set; }

            /// <summary>C_ElementValue.Name.</summary>
            public string Name { get; set; }
        }
    }
}
