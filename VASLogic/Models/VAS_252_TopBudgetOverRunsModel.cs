/******************************************************
 * Module Name    : VASLogic
 * Purpose        : Top Budget Overruns dashboard widget data
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
    /// Module Name : VAS_252_TopBudgetOverRuns
    /// Purpose     : Backs the VAS_252_TopBudgetOverRunsWidget dashboard widget - the
    ///               account / transaction-organization combinations whose actual has
    ///               already passed its approved budget, worst first:
    ///
    ///                 Grain     Account_ID + AD_OrgTrx_ID. Both sides are aggregated at
    ///                           that pair and matched at it, so a budget posted for one
    ///                           transaction organization is NEVER compared against actuals
    ///                           belonging to another.
    ///                 Budget    SUM(AmtAcctDr - AmtAcctCr) over PostingType 'B'.
    ///                 Actual    SUM(AmtAcctDr - AmtAcctCr) over PostingType 'A'.
    ///                 Variance  Budget - Actual, always NEGATIVE on this card by
    ///                           construction - a row only qualifies when actual exceeds
    ///                           budget.
    ///                 Utilized  Actual / Budget * 100.
    ///                 Ranking   (Actual - Budget) DESCENDING, i.e. Variance ascending -
    ///                           by the VALUE of the overrun and deliberately NOT by
    ///                           percentage: a large account 5% over is a bigger problem
    ///                           than a small one 40% over, and the card surfaces money.
    ///
    ///               DIMENSION MEANS AD_OrgTrx_ID, AND ONLY THAT. Not Fact_Acct.AD_Org_ID,
    ///               not project, activity, campaign, product or a user element. There is
    ///               no dimension-type selector and the column cannot be switched: the
    ///               grain of the whole card is the account and its transaction
    ///               organization, so changing the dimension would change what is being
    ///               compared rather than merely what is displayed.
    ///
    ///               AD_OrgTrx_ID RESOLVES THROUGH AD_Org, IN THE MAIN QUERY. The
    ///               transaction organization is an organization - there is no separate
    ///               AD_OrgTrx table in this schema - so the display name comes from
    ///               AD_Org.Name, picked up by a LEFT OUTER JOIN on the same scan that
    ///               produces the figures rather than by a second lookup afterwards. A row
    ///               whose AD_OrgTrx_ID is null OR zero has no transaction organization, so
    ///               its name comes back EMPTY and the client draws its missing-value dash;
    ///               those rows stay GROUPED SEPARATELY from any real organization rather
    ///               than being folded into one.
    ///
    ///               NOTE on the zero: elsewhere in this codebase AD_Org_ID 0 is the
    ///               tenant-wide '*' organization and is treated as a real value. Here it
    ///               is deliberately NOT, by explicit specification - a posting stamped
    ///               with the '*' organization as its TRANSACTION organization is a
    ///               posting that was never attributed to one.
    ///
    ///               SIGNED AS POSTED, WITH NO NATURAL-SIDE CORRECTION. Both sides are
    ///               AmtAcctDr - AmtAcctCr exactly as the specification states, with no
    ///               per-account-type flip. That is not an oversight and it is not a gap:
    ///               combined with the Budget &gt; 0 test below it is what confines the
    ///               card to debit-natural spending. A revenue or liability budget is
    ///               credit-natural, so it sums NEGATIVE under Dr - Cr and fails Budget
    ///               &gt; 0 - which is the right outcome, because a revenue account that
    ///               beats its budget is good news and has no place on an overruns card.
    ///
    ///               THE OVERRUN TEST IS Budget &gt; 0 AND Actual &gt; Budget, applied in
    ///               SQL as a HAVING clause so only qualifying groups ever cross the wire.
    ///               Budget &gt; 0 is required rather than merely Actual &gt; Budget:
    ///               spend against no budget is UNBUDGETED, not over-budget, it is
    ///               reported by its own card (VAS_256) on the same dashboard, and it
    ///               would make Utilized a division by zero. The test is strict, so an
    ///               account at exactly 100% does not qualify.
    ///
    ///               No balancing-account exclusion list is needed here. The offsetting
    ///               side of a budget journal is a credit, so it sums negative under
    ///               Dr - Cr and is dropped by Budget &gt; 0 like any other credit-natural
    ///               total - the predicate does the work a configured exclusion would have
    ///               done, without reading C_AcctSchema_GL at all.
    ///
    ///               PHASE 1 IS ACTUAL ONLY. Commitments are not included until commitment
    ///               accounting is defined. The basis is returned to the client as a token
    ///               (BASIS_Actual) rather than assumed there, so the day it becomes
    ///               "actual + commitment" the card reports which one it is showing.
    ///
    ///               PRIMARY ACCOUNTING SCHEMA ONLY, on both sides. Resolved through
    ///               AD_ClientInfo.C_AcctSchema1_ID and applied as an equality on the
    ///               single scan that produces both figures, so a budget posted in a
    ///               secondary schema is never compared against an actual in the primary
    ///               one. AmtAcct* is already stated in that schema's currency, so there is
    ///               no currencyConvert call anywhere in this model.
    ///
    ///               FINANCIAL YEAR. AD_ClientInfo.C_Calendar_ID -> C_Year -> C_Period. The
    ///               accounting date window is MIN(StartDate) / MAX(EndDate) over the ACTIVE
    ///               periods of the selected C_Year_ID - never derived from the calendar
    ///               year of DateAcct.
    ///
    ///               MRole row-level security is applied to Fact_Acct fa on the rows query
    ///               and to C_Year y on the year list. The joined C_ElementValue rows are a
    ///               reference lookup and inherit the parent's filter; AD_Org is a display
    ///               lookup by primary key. GROUP BY / HAVING / ORDER BY are appended AFTER
    ///               AddAccessSQL so its FROM-clause parser never meets a trailing clause,
    ///               and every join ON is a plain equality so it never meets a function
    ///               call either. Compatible with PostgreSQL and Oracle.
    /// Chronological development:
    ///   VAI154      2026-09-08 Created
    ///   VAI154      2026-09-08 Regrained to Account_ID + AD_OrgTrx_ID per the
    ///                          AD_OrgTrx dimension specification; natural-side sign
    ///                          correction and the balancing-account exclusion removed,
    ///                          both subsumed by the Budget &gt; 0 predicate.
    /// </summary>
    public class VAS_252_TopBudgetOverRunsModel
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_252_TopBudgetOverRunsModel).FullName);

        /* Fact_Acct.PostingType stored codes. Stored codes are compared bare - never with
           an N prefix, which would force a Unicode comparison against a VARCHAR column.
           These are compile-time constants of this class and are composed straight into
           the aggregate expressions: they are never client input, and binding the same two
           letters six times would make the statement harder to read than it is safe. */
        private const string POSTINGTYPE_Actual = "A";
        private const string POSTINGTYPE_Budget = "B";

        /* What the Actual side of the comparison is made of. Phase 1 is posted actuals
           alone; the token exists so the client reports the basis rather than assuming it,
           and so the day commitments are added the card says which basis it is showing. */
        public const string BASIS_Actual = "actual";

        /* Error tokens exchanged with the client; the client resolves the label from
           AD_Message, so no display text is produced here. */
        public const string ERROR_NO_CALENDAR = "NOCALENDAR";
        public const string ERROR_NO_ACCTSCHEMA = "NOACCTSCHEMA";
        public const string ERROR_NO_YEAR = "NOYEAR";

        /* Paging. Two rows plus a pager at 1280px, more in a taller cell, and the server
           clamps whatever is asked for. */
        public const int DEFAULT_PageSize = 2;
        private const int MIN_PageSize = 1;
        private const int MAX_PageSize = 12;

        // ─────────────────────────────────────────────────────────────────────
        // §1  Entry point
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Everything the card shows in one round trip: the selectable financial years, the
        /// year actually used, the accounting-schema currency, the count of overrun rows and
        /// the requested page of them.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="yearId">C_Year_ID the user selected, or 0 to default to the financial
        /// year containing today.</param>
        /// <param name="pageNo">1-based page; clamped to the available range.</param>
        /// <param name="pageSize">Rows per page; clamped to [1,12].</param>
        /// <returns>Populated <see cref="OverRunResult"/> (never null). Loaded is false only
        /// when there is no context or the tenant is not configured; a year with nothing over
        /// budget returns Loaded=true and an empty page, because "nothing is over" is a real
        /// answer - and a good one - rather than an error.</returns>
        public OverRunResult GetRows(Ctx ctx, int yearId, int pageNo, int pageSize)
        {
            OverRunResult result = new OverRunResult();
            result.Rows = new List<OverRunRow>();
            result.Years = new List<YearOption>();
            result.Basis = BASIS_Actual;
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
                Log.Log(Level.WARNING, "VAS_252_TopBudgetOverRuns: AD_ClientInfo.C_Calendar_ID not configured for AD_Client_ID="
                    + ctx.GetAD_Client_ID());
                return result;
            }

            if (acct.C_AcctSchema_ID <= 0)
            {
                result.ErrorCode = ERROR_NO_ACCTSCHEMA;
                Log.Log(Level.WARNING, "VAS_252_TopBudgetOverRuns: AD_ClientInfo.C_AcctSchema1_ID not configured for AD_Client_ID="
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
        /// <returns>Populated <see cref="AcctContext"/>; the ids are 0 when the tenant has no
        /// primary calendar / accounting schema.</returns>
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
        /// never a January-to-December assumption, and never the calendar year of DateAcct.
        /// A year with no active period has no window at all and is therefore not offered:
        /// there would be no date range to read Fact_Acct with.
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
        // §3  The rows - where actual has passed budget, per account + AD_OrgTrx_ID
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The Budget aggregate expression, and the Actual one. Built once and reused in the
        /// SELECT list, the HAVING clause and the ORDER BY so the three can never drift
        /// apart - if the sum ever changes, it changes in all three places at once.
        /// </summary>
        /// <param name="postingType">POSTINGTYPE_Budget or POSTINGTYPE_Actual.</param>
        /// <returns>A flat SUM(CASE WHEN ...) expression over Fact_Acct fa.</returns>
        private string SumExpression(string postingType)
        {
            return "SUM(CASE WHEN fa.PostingType='" + postingType +
                "' THEN COALESCE(fa.AmtAcctDr,0)-COALESCE(fa.AmtAcctCr,0) ELSE 0 END)";
        }

        /// <summary>
        /// Reads the account / transaction-organization combinations that are over budget in
        /// the selected year, worst first, and hands the requested page back.
        ///
        /// The overrun test and the ranking are both done in SQL, so only qualifying groups
        /// cross the wire. The whole qualifying set is still materialised here rather than
        /// paged in SQL, because the subtitle reports how many overruns there are IN TOTAL
        /// and a single page cannot know that - and having read it, slicing costs nothing.
        /// The set is one row per over-budget account and organization, which is an
        /// exception list rather than a transaction list.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="acct">Resolved accounting context (schema and currency).</param>
        /// <param name="year">Resolved financial year with its date window.</param>
        /// <param name="result">Result being filled - paging fields included.</param>
        private void ReadRows(Ctx ctx, AcctContext acct, YearOption year, OverRunResult result)
        {
            string budgetSum = SumExpression(POSTINGTYPE_Budget);
            string actualSum = SumExpression(POSTINGTYPE_Actual);

            StringBuilder sql = new StringBuilder();

            /* ONE scan, both posting types, as a FLAT SUM(CASE WHEN ...) per side. Two
               derived sets joined together would read Fact_Acct twice and would have to be
               FULL OUTER joined to keep a pair that has one side and not the other, and
               this codebase's access-SQL parser is kept away from nested selects.

               AD_OrgTrx_ID is selected RAW alongside a null flag rather than only as
               COALESCE(...,0): the client is told which of the two "no organization" cases
               a row is, even though both carry the same label, and GROUP BY on the raw
               column keeps them as separate rows rather than folding them together.

               THE DIMENSION NAME IS RESOLVED IN THIS QUERY, by a LEFT OUTER JOIN to
               AD_Org - the transaction organization IS an organization, there being no
               separate AD_OrgTrx master in this schema. LEFT, because most postings carry
               no AD_OrgTrx_ID at all and an inner join would silently drop every one of
               them; LAST, because its ON is a plain equality and the access parser is
               happiest when the closing ON carries no function call and no bind; and with
               no IsActive filter, because an organization deactivated since the budget was
               posted still has the name the ledger was written under.

               The CASE in front of it is what keeps organization ZERO out. AD_Org_ID 0 is
               a real row - the tenant-wide '*' organization - so the join finds it and
               would hand back its name; this widget's specification says a posting stamped
               with 0 has no transaction organization, so the name is blanked at source
               rather than left for the client to second-guess. */
            sql.Append(@"
                SELECT fa.Account_ID AS Account_ID,
                       COALESCE(fa.AD_OrgTrx_ID,0) AS AD_OrgTrx_ID,
                       CASE WHEN fa.AD_OrgTrx_ID IS NULL THEN 'Y' ELSE 'N' END AS OrgTrx_Is_Null,
                       CASE WHEN COALESCE(fa.AD_OrgTrx_ID,0)=0 THEN N'' ELSE COALESCE(orgtrx.Name,N'') END AS Dimension_Name,
                       COALESCE(ev.Value,N'') AS Account_Value,
                       COALESCE(ev.Name,N'') AS Account_Name,
                       ").Append(budgetSum).Append(@" AS Budget_Amt,
                       ").Append(actualSum).Append(@" AS Actual_Amt
                FROM Fact_Acct fa
                INNER JOIN C_ElementValue ev ON (ev.C_ElementValue_ID=fa.Account_ID)
                LEFT JOIN AD_Org orgtrx ON (orgtrx.AD_Org_ID=fa.AD_OrgTrx_ID)
                WHERE fa.AD_Client_ID=@AD_Client_ID
                  AND fa.C_AcctSchema_ID=@C_AcctSchema_ID
                  AND fa.IsActive='Y'
                  AND (fa.PostingType='").Append(POSTINGTYPE_Budget)
                  .Append("' OR fa.PostingType='").Append(POSTINGTYPE_Actual).Append(@"')
                  AND fa.DateAcct>=@DateFrom
                  AND fa.DateAcct<=@DateTo
                  AND ev.IsActive='Y'");

            string rowSql = MRole.GetDefault(ctx).AddAccessSQL(sql.ToString(), "fa",
                MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            rowSql += " GROUP BY fa.Account_ID,fa.AD_OrgTrx_ID,ev.Value,ev.Name,orgtrx.Name"
                + " HAVING " + budgetSum + ">0 AND " + actualSum + ">" + budgetSum
                + " ORDER BY (" + actualSum + "-" + budgetSum + ") DESC,"
                + "COALESCE(ev.Value,N''),COALESCE(orgtrx.Name,N''),"
                + "fa.Account_ID,COALESCE(fa.AD_OrgTrx_ID,0)";

            /* Positional binding: the order the placeholders appear in the text. The two
               posting-type codes are class constants composed straight in above, so they
               are not binds. */
            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@C_AcctSchema_ID", acct.C_AcctSchema_ID),
                new SqlParameter("@DateFrom", year.StartDate),
                new SqlParameter("@DateTo", year.EndDate)
            };

            DataSet ds = DB.ExecuteDataset(rowSql, parameters, null);
            if (ds == null || ds.Tables.Count == 0) { return; }

            DataTable dt = ds.Tables[0];

            List<OverRunRow> all = new List<OverRunRow>();
            for (int i = 0; i < dt.Rows.Count; i++)
            {
                all.Add(MapRow(dt.Rows[i]));
            }

            /* The subtitle's figure: how many overruns there are in total, across every
               page. It counts exactly the rows the HAVING produced, so the sentence and the
               list can never disagree. */
            result.OverRunCount = all.Count;
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
        /// Materialises one account / transaction-organization group.
        ///
        /// Both sides are AmtAcctDr - AmtAcctCr exactly as posted, with no natural-side
        /// correction - see the class note. Utilized is safe to divide here because the
        /// HAVING clause guaranteed Budget &gt; 0 before the row was returned at all.
        /// </summary>
        /// <param name="row">Row carrying the group aliases.</param>
        /// <returns>Populated <see cref="OverRunRow"/>.</returns>
        private OverRunRow MapRow(DataRow row)
        {
            OverRunRow item = new OverRunRow();

            item.Account_ID = Util.GetValueOfInt(row["Account_ID"]);
            item.AccountValue = Util.GetValueOfString(row["Account_Value"]);
            item.AccountName = Util.GetValueOfString(row["Account_Name"]);

            item.AD_OrgTrx_ID = Util.GetValueOfInt(row["AD_OrgTrx_ID"]);
            item.IsOrgTrxNull = "Y".Equals(Util.GetValueOfString(row["OrgTrx_Is_Null"]));

            /* Already resolved, already blanked for organization zero - see the query. */
            item.DimensionName = Util.GetValueOfString(row["Dimension_Name"]);

            item.Budget = Util.GetValueOfDecimal(row["Budget_Amt"]);
            item.Actual = Util.GetValueOfDecimal(row["Actual_Amt"]);
            item.Variance = item.Budget - item.Actual;
            item.UtilizedPct = (item.Actual / item.Budget) * 100m;

            return item;
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
        public class OverRunResult
        {
            /// <summary>The requested page of overrun rows, largest overrun first.</summary>
            public List<OverRunRow> Rows { get; set; }

            /// <summary>The financial years the filter can offer, newest first.</summary>
            public List<YearOption> Years { get; set; }

            /// <summary>The accounting schema and its currency - the card's only currency.</summary>
            public AcctContext Schema { get; set; }

            /// <summary>What the Actual side is made of - BASIS_Actual in phase 1. The client
            /// reports this rather than assuming it.</summary>
            public string Basis { get; set; }

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

            /// <summary>Overrun rows in total - the pager's figure.</summary>
            public int TotalRows { get; set; }

            /// <summary>CEILING(TotalRows / PageSize).</summary>
            public int TotalPages { get; set; }

            /// <summary>How many account / organization combinations are over budget across
            /// every page - the subtitle's figure, from the same HAVING the rows come
            /// from.</summary>
            public int OverRunCount { get; set; }

            /// <summary>ERROR_* token when the tenant is not configured; empty otherwise.</summary>
            public string ErrorCode { get; set; }

            /// <summary>False only on a failure or a missing configuration; a year with
            /// nothing over budget is Loaded=true with an empty page.</summary>
            public bool Loaded { get; set; }
        }

        /// <summary>
        /// One account / transaction-organization combination that has passed its budget.
        /// Both figures are in the PRIMARY accounting schema currency and are never
        /// converted - AmtAcctDr / AmtAcctCr are already stated in it.
        /// </summary>
        public class OverRunRow
        {
            /// <summary>Fact_Acct.Account_ID - the natural account (C_ElementValue_ID).
            /// Returned even though the UI shows the code and name rather than the id.</summary>
            public int Account_ID { get; set; }

            /// <summary>C_ElementValue.Value - the account code.</summary>
            public string AccountValue { get; set; }

            /// <summary>C_ElementValue.Name - the account description.</summary>
            public string AccountName { get; set; }

            /// <summary>Fact_Acct.AD_OrgTrx_ID - the DIMENSION, and the only one this card
            /// knows about. Zero means either the row carried no transaction organization at
            /// all (IsOrgTrxNull) or it carried organization 0; by this widget's
            /// specification both are "no transaction organization".</summary>
            public int AD_OrgTrx_ID { get; set; }

            /// <summary>True when the postings carried NO AD_OrgTrx_ID at all, as distinct
            /// from carrying zero. The two are grouped separately and labelled the same.</summary>
            public bool IsOrgTrxNull { get; set; }

            /// <summary>AD_Org.Name of AD_OrgTrx_ID, resolved by the main query's LEFT OUTER
            /// JOIN. EMPTY whenever AD_OrgTrx_ID is zero or null, and left that way
            /// deliberately: the server does not invent display text for an absence. The
            /// client renders the empty value as its missing-value dash, the same as any
            /// other empty cell on the card.</summary>
            public string DimensionName { get; set; }

            /// <summary>Approved budget for the year at this grain: PostingType 'B'. Always
            /// greater than zero on a row that reaches the client.</summary>
            public decimal Budget { get; set; }

            /// <summary>Posted actual for the year at this grain: PostingType 'A'. Always
            /// greater than Budget on a row that reaches the client.</summary>
            public decimal Actual { get; set; }

            /// <summary>Budget - Actual. Always NEGATIVE on this card by construction.</summary>
            public decimal Variance { get; set; }

            /// <summary>Actual / Budget * 100. Always above 100 on this card.</summary>
            public decimal UtilizedPct { get; set; }
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
