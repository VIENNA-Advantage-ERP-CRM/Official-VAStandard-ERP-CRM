/******************************************************
 * Module Name    : VASLogic
 * Purpose        : Budget Summary by account type dashboard widget data
 * chronological  : Development
 * Created Date   : 2026-09-10
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
    /// Module Name : VAS_250_BudgetSummary
    /// Purpose     : Backs the VAS_250_BudgetSummaryWidget dashboard widget - approved budget
    ///               against posted actual for one financial year, summarised by ACCOUNT TYPE:
    ///
    ///                 Budget    PostingType 'B', summed in the account's own direction.
    ///                 Actual    the same over PostingType 'A'.
    ///                 Balance   Budget - Actual.
    ///                 Utilized  Actual / Budget * 100.
    ///
    ///               BUDGETED LEDGER ACCOUNTS ONLY, EVERYWHERE ON THIS CARD. An account counts
    ///               towards a figure here - budget, actual, balance or utilization - only when
    ///               that ACCOUNT carries a budget fact for the year. This is a BUDGET report:
    ///               every number on it answers "against what was approved", and an actual posted
    ///               to an account nobody budgeted has no approved line to be measured against.
    ///               Such spending is not lost, it is the unbudgeted card's subject (VAS_256).
    ///
    ///               ONE SCOPE, TWO LEVELS OF DETAIL. The drill-down lists those same budgeted
    ///               accounts for one type and sums exactly them, so the row's Actual cell and
    ///               the panel's Actual metric are equal by construction - the same population,
    ///               aggregated once per type and once per account.
    ///
    ///               THREE ROWS, ALWAYS, WHATEVER THE YEAR HOLDS. Expense ('E'), Revenue ('R')
    ///               and Asset ('A') are returned in that order whether or not a single account
    ///               was budgeted under them - a type with nothing budgeted is a zero row, not a
    ///               missing row. A card whose shape changed as the year filled in would make
    ///               "no expense budget yet" indistinguishable from "expense budget not shown".
    ///
    ///               THE FIGURES ARE POSITIVE BUSINESS VALUES, NOT RAW LEDGER SIDES. An expense
    ///               or asset account is summed AmtAcctDr - AmtAcctCr and a revenue account
    ///               AmtAcctCr - AmtAcctDr, so all three rows read in the direction the
    ///               business states them and a revenue budget does not print negative beside
    ///               an expense one. The normalisation happens HERE, once, in SQL - the client
    ///               never re-signs an amount it was given.
    ///
    ///               ONE SCAN, AND NOT A CTE. The specification expresses the aggregation as a
    ///               WITH clause; it is written here as a per-account grouped SELECT wrapped in
    ///               an outer total. Same rows, same numbers - and the access clause is applied
    ///               to the inner query BEFORE the wrap, so MRole only ever sees the one physical
    ///               table the user is reading from (Fact_Acct fa) and is never asked to resolve
    ///               the derived alias through AD_Table.
    ///
    ///               PRIMARY ACCOUNTING SCHEMA ONLY. AD_ClientInfo.C_AcctSchema1_ID decides the
    ///               ledger and its currency; a budget posted in a secondary schema must not be
    ///               compared against an actual in the primary one. AmtAcct* is already stated
    ///               in that schema's currency, so there is no currencyConvert call anywhere in
    ///               this model and nothing is converted.
    ///
    ///               THE FISCAL YEAR IS THE CALENDAR'S, NEVER A CALENDAR YEAR. The date window
    ///               is MIN(StartDate) / MAX(EndDate) over the year's own ACTIVE periods, read
    ///               with the year list itself, so a year that does not run January to December
    ///               needs no special case and no month arithmetic happens anywhere.
    ///
    ///               "POSTED THROUGH" IS THE LATEST ACTUAL DateAcct inside that window, sent as
    ///               yyyy-MM-dd and formatted in the reader's own locale by the client - never
    ///               with TO_CHAR, which is neither portable nor translatable.
    ///
    ///               MRole row-level security is applied to Fact_Acct fa on both reads and to
    ///               C_Year y on the year list. The joined C_ElementValue rows are a reference
    ///               lookup by primary key and inherit the parent's filter; AD_ClientInfo,
    ///               C_AcctSchema, C_Currency and C_Period are configuration reads. GROUP BY is
    ///               appended AFTER AddAccessSQL so its FROM-clause parser never meets a
    ///               trailing clause, and every join ON is a plain equality so it never meets a
    ///               function call either. Compatible with PostgreSQL and Oracle.
    /// Chronological development:
    ///   VAI145      2026-09-10 Created
    /// </summary>
    public class VAS_250_BudgetSummaryModel
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_250_BudgetSummaryModel).FullName);

        /* Fact_Acct.PostingType stored codes. Stored codes are compared bare - never with an
           N prefix, which would force a Unicode comparison against a VARCHAR column. */
        private const string POSTINGTYPE_Actual = "A";
        private const string POSTINGTYPE_Budget = "B";

        /* C_ElementValue.AccountType - the three types this card reports, in the order it
           reports them. Expense first: it is what a budget is mostly about. */
        private const string ACCOUNTTYPE_Expense = "E";
        private const string ACCOUNTTYPE_Revenue = "R";
        private const string ACCOUNTTYPE_Asset = "A";

        /* Error tokens exchanged with the client; the client resolves the label from
           AD_Message, so no display text is produced here. */
        public const string ERROR_NO_CALENDAR = "NOCALENDAR";
        public const string ERROR_NO_ACCTSCHEMA = "NOACCTSCHEMA";
        public const string ERROR_NO_YEAR = "NOYEAR";

        /* One account type's drill-down is one payload, paged in the panel. An account type has a
           bounded number of BUDGETED ledger accounts, so the cap is a payload guard rather than a
           pager - the panel's metrics always come from the whole type, so they stay true when the
           list is trimmed. */
        private const int MAX_DetailRows = 200;

        // ─────────────────────────────────────────────────────────────────────
        // §1  Entry point
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Everything the card shows in one round trip: the selectable financial years, the
        /// year actually used, the accounting-schema currency, one row per account type with
        /// its budget, actual, balance and utilization, the four totals above them, and the
        /// date the ledger is posted through.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="yearId">C_Year_ID the user selected, or 0 to default to the financial
        /// year containing today.</param>
        /// <returns>Populated <see cref="SummaryResult"/> (never null). Loaded is false only
        /// when there is no context or the tenant is not configured; a year with nothing posted
        /// returns Loaded=true with three zero rows, because "nothing posted yet" is a real
        /// answer rather than an error.</returns>
        public SummaryResult GetSummary(Ctx ctx, int yearId)
        {
            SummaryResult result = new SummaryResult();
            result.Rows = new List<TypeRow>();
            result.Years = new List<YearOption>();

            if (ctx == null) { return result; }

            /* The accounting context is a CONFIGURATION precondition, not a filter: without a
               primary calendar there is no year list to build, and without a primary accounting
               schema there is no ledger to read. Neither is silently replaced by "some other"
               calendar or schema. */
            AcctContext acct = GetAcctContext(ctx);
            result.Schema = acct;

            if (acct.C_Calendar_ID <= 0)
            {
                result.ErrorCode = ERROR_NO_CALENDAR;
                Log.Log(Level.WARNING, "VAS_250_BudgetSummary: AD_ClientInfo.C_Calendar_ID not configured for AD_Client_ID="
                    + ctx.GetAD_Client_ID());
                return result;
            }

            if (acct.C_AcctSchema_ID <= 0)
            {
                result.ErrorCode = ERROR_NO_ACCTSCHEMA;
                Log.Log(Level.WARNING, "VAS_250_BudgetSummary: AD_ClientInfo.C_AcctSchema1_ID not configured for AD_Client_ID="
                    + ctx.GetAD_Client_ID());
                return result;
            }

            /* The year list travels with the summary, so the widget is one round trip on load
               and the pill can never name a year the figures were not read for. */
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

            /* THE THREE ROWS ARE THE SPINE. They are built first and in their own right, so
               every one of them reaches the card whether or not a fact was ever posted against
               it; the scan below fills in the ones that were. */
            result.Rows = BuildTypeRows();
            ApplyAmounts(ctx, acct, year, result.Rows);
            ApplyRowMeasures(result.Rows);
            ApplyTotals(result);

            result.PostedThrough = ReadPostedThrough(ctx, acct, year);
            result.HasActual = result.PostedThrough.Length > 0;

            result.Loaded = true;
            return result;
        }

        // ─────────────────────────────────────────────────────────────────────
        // §2  Accounting context and the financial-year list
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The tenant's accounting context: the primary calendar, the PRIMARY accounting schema
        /// and the currency every figure on the card is expressed in. Both ids come from
        /// AD_ClientInfo - never from a search over all calendars or all schemas.
        ///
        /// Reads only client-scoped configuration and reference tables, so no MRole predicate is
        /// applied - the same treatment the sibling Budgeting widgets give this lookup.
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
               against the amount and only falls back to the code when the currency has no
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
                /* The join to C_AcctSchema is INNER, so a tenant with a calendar but no primary
                   schema lands here too. Read the calendar on its own so the caller can tell the
                   two configuration errors apart. */
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
        /// The primary calendar on its own. Only reached when the accounting-context query found
        /// nothing, to distinguish "no calendar" from "no accounting schema".
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
        /// The window is MIN(StartDate) / MAX(EndDate) over the year's ACTIVE periods - never a
        /// January-to-December assumption. This is the specification's "Query 2" folded into the
        /// year list: the window arrives with the year that needs it, so no second round trip
        /// happens when the reader changes year and no query is ever run inside a loop. A year
        /// with no active period has no window at all and is therefore not offered - there would
        /// be nothing to read.
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

            /* GROUP BY and ORDER BY go on AFTER the access SQL - its FROM-clause parser must not
               meet a trailing clause. Ordered by the year's own start date, not by FiscalYear:
               FiscalYear is free text and sorts alphabetically. */
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
        /// A requested id is honoured only when it is one of the years this role may see on this
        /// tenant's primary calendar - a stale or forged selection falls back to the default
        /// rather than reaching Fact_Acct. The default is the year containing today, then the
        /// most recent year that has already started, then the newest year in the list.
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
        // §3  The three rows, and the figures laid over them
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The card's three rows, zeroed, in display order: Expense, Revenue, Asset. Built
        /// before anything is read so a type with no postings is a ZERO row rather than a
        /// missing one.
        /// </summary>
        /// <returns>Three rows in display order.</returns>
        private List<TypeRow> BuildTypeRows()
        {
            List<TypeRow> items = new List<TypeRow>();

            items.Add(new TypeRow { AccountType = ACCOUNTTYPE_Expense });
            items.Add(new TypeRow { AccountType = ACCOUNTTYPE_Revenue });
            items.Add(new TypeRow { AccountType = ACCOUNTTYPE_Asset });

            return items;
        }

        /// <summary>
        /// Fills each account type's budget, actual and actual-posting count from ONE grouped
        /// scan of Fact_Acct, and merges the result onto the three rows in C# so a type the year
        /// never touched keeps its zeros.
        ///
        /// BUDGETED ACCOUNTS ONLY, ACCOUNT BY ACCOUNT. The scan groups by ACCOUNT first and keeps
        /// only the accounts that carry a budget fact for the year; the types are then totalled
        /// from what survives. An account posted to with no budget behind it therefore
        /// contributes nothing - not to the row's Actual, not to the four totals above the grid.
        ///
        /// THIS IS WHAT MAKES THE CARD AND ITS DRILL-DOWN THE SAME NUMBER. The panel lists the
        /// budgeted accounts of one type and sums exactly them; the card sums the same accounts
        /// one level up. The two are the same population read at two levels of detail, so the
        /// row's Actual cell and the panel's Actual metric are equal by construction rather than
        /// by coincidence.
        ///
        /// THE SIGN IS NORMALISED IN SQL, ONCE. Expense and asset accounts are summed
        /// AmtAcctDr - AmtAcctCr and revenue accounts AmtAcctCr - AmtAcctDr, which is the
        /// direction each type is stated in by the business. The client is handed finished
        /// numbers and never re-signs them.
        ///
        /// Both the year's period window and the posting-type filter are applied here, so the
        /// optimiser gets the DateAcct range to work with and the scan touches only what the
        /// card reports on.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="acct">Resolved accounting context (schema and currency).</param>
        /// <param name="year">The selected financial year and its date window.</param>
        /// <param name="rows">The three rows, completed in place.</param>
        private void ApplyAmounts(Ctx ctx, AcctContext acct, YearOption year, List<TypeRow> rows)
        {
            if (year == null || year.StartDate > year.EndDate) { return; }

            Dictionary<string, TypeRow> byType = new Dictionary<string, TypeRow>();
            for (int i = 0; i < rows.Count; i++) { byType[rows[i].AccountType] = rows[i]; }

            /* Bind order is appearance order in the finished statement, because the backend
               adapters bind POSITIONALLY - a name is only a label. The five binds inside the
               SELECT list therefore come first, ahead of everything in the WHERE, and every
               occurrence carries its own name. */
            List<SqlParameter> parameters = new List<SqlParameter>();
            parameters.Add(new SqlParameter("@PostingType_Budget_Sel", POSTINGTYPE_Budget));
            parameters.Add(new SqlParameter("@AccountType_Revenue_Bud", ACCOUNTTYPE_Revenue));
            parameters.Add(new SqlParameter("@PostingType_Actual_Sel", POSTINGTYPE_Actual));
            parameters.Add(new SqlParameter("@AccountType_Revenue_Act", ACCOUNTTYPE_Revenue));
            parameters.Add(new SqlParameter("@PostingType_Actual_Cnt", POSTINGTYPE_Actual));
            parameters.Add(new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()));
            parameters.Add(new SqlParameter("@C_AcctSchema_ID", acct.C_AcctSchema_ID));
            parameters.Add(new SqlParameter("@PostingType_Budget", POSTINGTYPE_Budget));
            parameters.Add(new SqlParameter("@PostingType_Actual", POSTINGTYPE_Actual));
            parameters.Add(new SqlParameter("@DateFrom", year.StartDate));
            parameters.Add(new SqlParameter("@DateTo", year.EndDate));
            parameters.Add(new SqlParameter("@AccountType_E", ACCOUNTTYPE_Expense));
            parameters.Add(new SqlParameter("@AccountType_R", ACCOUNTTYPE_Revenue));
            parameters.Add(new SqlParameter("@AccountType_A", ACCOUNTTYPE_Asset));
            /* Last in the list because it is last in the finished statement - the HAVING clause
               is appended after the access SQL, behind everything in the WHERE. */
            parameters.Add(new SqlParameter("@PostingType_Budget_Have", POSTINGTYPE_Budget));

            StringBuilder sql = new StringBuilder();

            /* ONE scan, both posting types, as a FLAT SUM(CASE WHEN ...) per side plus a COUNT
               of the actual side. The count is a measure in its own right: it - and not the
               amount - is what says an account has been posted to, since postings that net to
               nothing have still been made. C_ElementValue is joined for the account's TYPE
               alone: a reference lookup by primary key, with a plain-equality ON so the access
               parser has nothing to trip on.

               GROUPED BY ACCOUNT, NOT BY TYPE, because "has a budget" is a property of an
               ACCOUNT: the HAVING below drops the accounts that carry no budget fact, and the
               types are totalled from the ones that survive. */
            sql.Append(@"
                SELECT ev.AccountType AS Account_Type,
                       fa.Account_ID AS Account_ID,
                       ").Append(SignedSumSql("@PostingType_Budget_Sel", "@AccountType_Revenue_Bud")).Append(@" AS Budget_Amt,
                       ").Append(SignedSumSql("@PostingType_Actual_Sel", "@AccountType_Revenue_Act")).Append(@" AS Actual_Amt,
                       COALESCE(SUM(CASE WHEN fa.PostingType=@PostingType_Actual_Cnt THEN 1 ELSE 0 END),0) AS Actual_Cnt
                FROM Fact_Acct fa
                INNER JOIN C_ElementValue ev ON (ev.C_ElementValue_ID=fa.Account_ID)
                WHERE fa.AD_Client_ID=@AD_Client_ID
                  AND fa.C_AcctSchema_ID=@C_AcctSchema_ID
                  AND fa.IsActive='Y'
                  AND (fa.PostingType=@PostingType_Budget OR fa.PostingType=@PostingType_Actual)
                  AND fa.DateAcct>=@DateFrom
                  AND fa.DateAcct<=@DateTo
                  AND ev.IsActive='Y'
                  AND ev.AccountType IN (@AccountType_E,@AccountType_R,@AccountType_A)");

            /* Fact_Acct fa is the main physical table the user is reading from: the role's access
               clause goes HERE, on the base query, and never on a derived alias - which is also
               why the per-account query is finished BEFORE it is wrapped below. */
            string accessSql = MRole.GetDefault(ctx).AddAccessSQL(sql.ToString(), "fa",
                MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            /* GROUP BY and HAVING after the access SQL - its FROM-clause parser must not meet a
               trailing clause.

               THE HAVING IS THE "HAS A BUDGET" TEST, and it counts FACTS rather than summing
               amounts: an account whose budget lines net to zero has still been budgeted for, and
               a filter on the amount would silently drop it. It is the SAME test the drill-down
               applies, which is what keeps the row and the panel equal. */
            accessSql += " GROUP BY ev.AccountType,fa.Account_ID"
                + " HAVING SUM(CASE WHEN fa.PostingType=@PostingType_Budget_Have THEN 1 ELSE 0 END)>0";

            /* THE TYPES ARE TOTALLED FROM THE SURVIVING ACCOUNTS. The wrap is applied AFTER the
               access parser has already run over the query it needs to read, so the parser never
               meets the derived table and MRole is never asked to resolve `b` through AD_Table -
               which is the whole point of flattening the specification's CTE into this shape. A
               derived table in the FROM is portable to both PostgreSQL and Oracle, and the outer
               SELECT introduces no binds, so the positional order of the inner ones is
               untouched. */
            string finalSql = "SELECT b.Account_Type AS Account_Type,"
                + "COALESCE(SUM(b.Budget_Amt),0) AS Budget_Amt,"
                + "COALESCE(SUM(b.Actual_Amt),0) AS Actual_Amt,"
                + "COALESCE(SUM(b.Actual_Cnt),0) AS Actual_Cnt"
                + " FROM (" + accessSql + ") b"
                + " GROUP BY b.Account_Type";

            DataSet ds = DB.ExecuteDataset(finalSql, parameters.ToArray(), null);
            if (ds == null || ds.Tables.Count == 0) { return; }

            DataTable dt = ds.Tables[0];
            for (int i = 0; i < dt.Rows.Count; i++)
            {
                DataRow row = dt.Rows[i];

                string accountType = Util.GetValueOfString(row["Account_Type"]);
                if (accountType == null || !byType.ContainsKey(accountType)) { continue; }

                TypeRow item = byType[accountType];
                item.Budget = Util.GetValueOfDecimal(row["Budget_Amt"]);
                item.Actual = Util.GetValueOfDecimal(row["Actual_Amt"]);
                item.ActualCount = Util.GetValueOfInt(row["Actual_Cnt"]);
            }
        }

        /// <summary>
        /// ONE DEFINITION OF AN AMOUNT, USED BY EVERY READ IN THIS MODEL: the sum over one
        /// posting type, taken in the account's own direction - AmtAcctCr - AmtAcctDr for a
        /// revenue account and AmtAcctDr - AmtAcctCr for everything else.
        ///
        /// The card's three rows and the drill-down's ledger list are built from this same
        /// string, which is what makes the modal reconcile to the row that opened it: the two
        /// cannot drift apart, because there is only one expression to change.
        ///
        /// Both arguments are BIND NAMES from this class's own constants - never client text -
        /// so the concatenation carries no value into the SQL.
        /// </summary>
        /// <param name="postingTypeBind">Bind name holding the posting type to sum ('A'/'B').</param>
        /// <param name="revenueBind">Bind name holding the revenue account-type code.</param>
        /// <returns>A COALESCE(SUM(CASE ...),0) expression for the SELECT list.</returns>
        private string SignedSumSql(string postingTypeBind, string revenueBind)
        {
            return "COALESCE(SUM(CASE WHEN fa.PostingType=" + postingTypeBind
                + " THEN CASE WHEN ev.AccountType=" + revenueBind
                + " THEN COALESCE(fa.AmtAcctCr,0)-COALESCE(fa.AmtAcctDr,0)"
                + " ELSE COALESCE(fa.AmtAcctDr,0)-COALESCE(fa.AmtAcctCr,0) END ELSE 0 END),0)";
        }

        /// <summary>
        /// Each row's balance and utilization, computed where the amounts are - the client
        /// formats figures, it does not derive them.
        ///
        /// ZERO BUDGET IS NOT A DIVISION. A type with no approved budget reports
        /// HasUtilization=false and the client prints a dash instead of a ratio nobody can
        /// defend; a type with actuals but no budget additionally reports IsOverBudget, which is
        /// the honest reading of spending against nothing and keeps Infinity and NaN off the
        /// card entirely.
        /// </summary>
        /// <param name="rows">The three rows, completed in place.</param>
        private void ApplyRowMeasures(List<TypeRow> rows)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                TypeRow item = rows[i];

                item.Balance = item.Budget - item.Actual;

                if (item.Budget != 0)
                {
                    item.HasUtilization = true;
                    item.UtilizedPct = item.Actual * 100m / item.Budget;
                    item.IsOverBudget = item.UtilizedPct >= 100m;
                }
                else
                {
                    item.IsOverBudget = item.Actual > 0;
                }
            }
        }

        /// <summary>
        /// The four figures above the grid: total budget, total actual, what is left of the
        /// budget, and how much of it has been used. Summed from the SAME rows the grid prints,
        /// so the band and the grid can never disagree.
        /// </summary>
        /// <param name="result">Result being filled.</param>
        private void ApplyTotals(SummaryResult result)
        {
            decimal budget = 0m;
            decimal actual = 0m;

            for (int i = 0; i < result.Rows.Count; i++)
            {
                budget += result.Rows[i].Budget;
                actual += result.Rows[i].Actual;
            }

            result.TotalBudget = budget;
            result.TotalActual = actual;
            result.Balance = budget - actual;

            if (budget != 0)
            {
                result.HasUtilization = true;
                result.UtilizedPct = actual * 100m / budget;
                result.IsOverBudget = result.UtilizedPct >= 100m;
            }
            else
            {
                result.IsOverBudget = actual > 0;
            }
        }

        /// <summary>
        /// The latest ACTUAL posting date inside the selected year - the subtitle's watermark.
        ///
        /// DELIBERATELY NOT BUDGET-SCOPED, and the one figure on the card that is not. The
        /// sentence says how far the BOOKS have been posted, which is a property of the ledger:
        /// narrowing it to budgeted accounts would move the watermark backwards whenever the most
        /// recent posting happened to land on an account nobody budgeted, and report the ledger
        /// as less current than it is. It is a date, not an amount, so it totals nothing and
        /// cannot disagree with the grid.
        ///
        /// Returned as yyyy-MM-dd for the wire. The client renders it as "Month Year" in the
        /// reader's own locale - the format is never baked in here with TO_CHAR, which is
        /// neither portable across the two backends nor translatable.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="acct">Resolved accounting context (schema and currency).</param>
        /// <param name="year">The selected financial year and its date window.</param>
        /// <returns>yyyy-MM-dd, or an empty string when the year carries no actual posting.</returns>
        private string ReadPostedThrough(Ctx ctx, AcctContext acct, YearOption year)
        {
            if (year == null || year.StartDate > year.EndDate) { return ""; }

            string sql = @"
                SELECT MAX(fa.DateAcct) AS Last_Actual_Date
                FROM Fact_Acct fa
                WHERE fa.AD_Client_ID=@AD_Client_ID
                  AND fa.C_AcctSchema_ID=@C_AcctSchema_ID
                  AND fa.IsActive='Y'
                  AND fa.PostingType=@PostingType_Actual
                  AND fa.DateAcct>=@DateFrom
                  AND fa.DateAcct<=@DateTo";

            /* Fact_Acct fa is the main physical table this read fetches from. */
            string finalSql = MRole.GetDefault(ctx).AddAccessSQL(sql, "fa",
                MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@C_AcctSchema_ID", acct.C_AcctSchema_ID),
                new SqlParameter("@PostingType_Actual", POSTINGTYPE_Actual),
                new SqlParameter("@DateFrom", year.StartDate),
                new SqlParameter("@DateTo", year.EndDate)
            };

            DataSet ds = DB.ExecuteDataset(finalSql, parameters, null);
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) { return ""; }

            /* MAX over no rows is NULL, not zero - an unposted year lands here and says so with
               an empty string rather than a date nobody posted on. */
            DateTime? last = Util.GetValueOfDateTime(ds.Tables[0].Rows[0]["Last_Actual_Date"]);
            if (!last.HasValue) { return ""; }

            return last.Value.ToString("yyyy-MM-dd");
        }

        // ─────────────────────────────────────────────────────────────────────
        // §4  The drill-down - the ledger accounts behind one account type
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The BUDGETED LEDGER ACCOUNTS behind one row of the card: every account of that type
        /// that has a budget defined for the selected year, with its budget, its actual and the
        /// balance between them - and the four metrics summed over exactly those accounts.
        ///
        /// ONLY ACCOUNTS WITH A BUDGET ARE LISTED. The panel answers "what was approved here and
        /// how much of it has gone", which is a question about budget lines; an account posted to
        /// with no budget behind it has no line to report against and belongs to the unbudgeted
        /// card (VAS_256), not to this list.
        ///
        /// AN ACCOUNT WITH A BUDGET AND NOTHING SPENT STILL APPEARS - that is the whole point of
        /// the Balance column, and dropping it would leave the reader unable to see what is still
        /// available.
        ///
        /// WHAT COUNTS AS "BUDGETED" IS A POSTING, NOT AN AMOUNT. The filter is "the account
        /// carries at least one budget fact", so a budget line that nets to zero is still an
        /// approved line and is still listed; reading the amount instead would silently drop it.
        ///
        /// THE PANEL ADDS UP TO ITSELF, AND TO THE ROW THAT OPENED IT. Its metrics sum the
        /// accounts it lists, so every figure at the top is accounted for by a row underneath;
        /// and because the card is scoped to the same budgeted accounts, those metrics equal the
        /// row's own cells. The two cannot disagree - they are the same population, aggregated at
        /// two levels of detail from the same signed-amount expression and the same budget test.
        ///
        /// Read only when a row is actually opened: the card never carries this.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="accountType">C_ElementValue.AccountType the reader clicked: 'E', 'R' or
        /// 'A'. Anything else is refused rather than queried.</param>
        /// <param name="yearId">C_Year_ID the card is showing, re-resolved here against the
        /// tenant's own primary calendar.</param>
        /// <returns>Populated <see cref="TypeDetailResult"/> (never null). Loaded is false when
        /// there is no context, the tenant is not configured, or the account type is not one of
        /// the three this card reports; a type with no accounts is Loaded=true and an empty
        /// list.</returns>
        public TypeDetailResult GetTypeDetail(Ctx ctx, string accountType, int yearId)
        {
            TypeDetailResult result = new TypeDetailResult();
            result.Rows = new List<LedgerRow>();

            if (ctx == null) { return result; }

            /* THE TYPE IS A STORED CODE, NOT FREE TEXT. It is checked against the three the card
               reports before it reaches a query at all - a value from anywhere else never
               becomes a predicate. */
            if (accountType != ACCOUNTTYPE_Expense && accountType != ACCOUNTTYPE_Revenue
                && accountType != ACCOUNTTYPE_Asset)
            {
                Log.Log(Level.WARNING, "VAS_250_BudgetSummary: unsupported AccountType requested for AD_Client_ID="
                    + ctx.GetAD_Client_ID());
                return result;
            }

            result.AccountType = accountType;

            AcctContext acct = GetAcctContext(ctx);
            result.Schema = acct;

            if (acct.C_Calendar_ID <= 0) { result.ErrorCode = ERROR_NO_CALENDAR; return result; }
            if (acct.C_AcctSchema_ID <= 0) { result.ErrorCode = ERROR_NO_ACCTSCHEMA; return result; }

            /* The year is re-resolved from the tenant's own calendar rather than trusted from
               the browser: a stale or forged id falls back to the default instead of reaching
               Fact_Acct. */
            List<YearOption> years = GetYears(ctx, acct.C_Calendar_ID);
            if (years.Count == 0) { result.ErrorCode = ERROR_NO_YEAR; return result; }

            YearOption year = PickYear(years, yearId, DateTime.Now.Date);
            result.C_Year_ID = year.C_Year_ID;
            result.FiscalYear = year.FiscalYear;

            /* THE METRICS ARE THE LIST'S OWN TOTALS, summed inside the read below over the same
               budgeted accounts the list is drawn from - so the panel is internally consistent:
               every figure at the top is accounted for by a row underneath it. */
            ReadLedgerRows(ctx, acct, year, accountType, result);

            result.Loaded = true;
            return result;
        }

        /// <summary>
        /// One account type's ledger accounts in the selected year: budget and actual per
        /// account, from ONE grouped scan and the same signed-amount definition the card itself
        /// uses.
        ///
        /// ORDERED BY THE ACCOUNT CODE, not by size. The panel carries two figures per row and
        /// neither is "the" magnitude to rank by - a large unspent budget matters as much as a
        /// large spend - and a chart of accounts is looked up, read and spoken about in code
        /// order. It also keeps the ordering expression free of a repeated aggregate, which on a
        /// positionally-bound statement would mean repeating its binds as well.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="acct">Resolved accounting context (schema and currency).</param>
        /// <param name="year">The selected financial year and its date window.</param>
        /// <param name="accountType">The validated account type being opened.</param>
        /// <param name="result">Result being filled.</param>
        private void ReadLedgerRows(Ctx ctx, AcctContext acct, YearOption year, string accountType,
            TypeDetailResult result)
        {
            if (year == null || year.StartDate > year.EndDate) { return; }

            /* Bind order is appearance order in the finished statement - the adapters bind
               POSITIONALLY - so the two binds inside each SELECT expression come first. */
            List<SqlParameter> parameters = new List<SqlParameter>();
            parameters.Add(new SqlParameter("@PostingType_Budget_Sel", POSTINGTYPE_Budget));
            parameters.Add(new SqlParameter("@AccountType_Revenue_Bud", ACCOUNTTYPE_Revenue));
            parameters.Add(new SqlParameter("@PostingType_Actual_Sel", POSTINGTYPE_Actual));
            parameters.Add(new SqlParameter("@AccountType_Revenue_Act", ACCOUNTTYPE_Revenue));
            parameters.Add(new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()));
            parameters.Add(new SqlParameter("@C_AcctSchema_ID", acct.C_AcctSchema_ID));
            parameters.Add(new SqlParameter("@PostingType_Budget", POSTINGTYPE_Budget));
            parameters.Add(new SqlParameter("@PostingType_Actual", POSTINGTYPE_Actual));
            parameters.Add(new SqlParameter("@DateFrom", year.StartDate));
            parameters.Add(new SqlParameter("@DateTo", year.EndDate));
            parameters.Add(new SqlParameter("@AccountType", accountType));
            /* Last in the list because it is last in the finished statement - the HAVING clause
               is appended after the access SQL, behind everything in the WHERE. */
            parameters.Add(new SqlParameter("@PostingType_Budget_Have", POSTINGTYPE_Budget));

            StringBuilder sql = new StringBuilder();

            /* C_ElementValue names the account and carries its type; it is a reference lookup by
               primary key and inherits the parent's access filter, and its ON is a plain equality
               so the access parser has nothing to trip on. */
            sql.Append(@"
                SELECT fa.Account_ID AS Account_ID,
                       COALESCE(ev.Value,N'') AS Account_Value,
                       COALESCE(ev.Name,N'') AS Account_Name,
                       ").Append(SignedSumSql("@PostingType_Budget_Sel", "@AccountType_Revenue_Bud")).Append(@" AS Budget_Amt,
                       ").Append(SignedSumSql("@PostingType_Actual_Sel", "@AccountType_Revenue_Act")).Append(@" AS Actual_Amt
                FROM Fact_Acct fa
                INNER JOIN C_ElementValue ev ON (ev.C_ElementValue_ID=fa.Account_ID)
                WHERE fa.AD_Client_ID=@AD_Client_ID
                  AND fa.C_AcctSchema_ID=@C_AcctSchema_ID
                  AND fa.IsActive='Y'
                  AND (fa.PostingType=@PostingType_Budget OR fa.PostingType=@PostingType_Actual)
                  AND fa.DateAcct>=@DateFrom
                  AND fa.DateAcct<=@DateTo
                  AND ev.IsActive='Y'
                  AND ev.AccountType=@AccountType");

            /* Fact_Acct fa is the main physical table this read fetches from. */
            string finalSql = MRole.GetDefault(ctx).AddAccessSQL(sql.ToString(), "fa",
                MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            /* GROUP BY, HAVING and ORDER BY after the access SQL - its FROM-clause parser must not
               meet a trailing clause.

               THE HAVING IS THE "HAS A BUDGET" TEST, and it counts FACTS rather than summing
               amounts: an account whose budget lines net to zero has still been budgeted for, and
               a filter on the amount would drop it. An account with actuals and no budget fact at
               all fails the test and never reaches the panel. */
            finalSql += " GROUP BY fa.Account_ID,ev.Value,ev.Name"
                + " HAVING SUM(CASE WHEN fa.PostingType=@PostingType_Budget_Have THEN 1 ELSE 0 END)>0"
                + " ORDER BY ev.Value,fa.Account_ID";

            DataSet ds = DB.ExecuteDataset(finalSql, parameters.ToArray(), null);
            if (ds == null || ds.Tables.Count == 0) { return; }

            DataTable dt = ds.Tables[0];
            result.TotalRows = dt.Rows.Count;

            decimal budgetTotal = 0m;
            decimal actualTotal = 0m;

            for (int i = 0; i < dt.Rows.Count; i++)
            {
                DataRow row = dt.Rows[i];

                /* THE METRICS SUM EVERY BUDGETED ACCOUNT, THE LIST SHOWS THE FIRST 200. The
                   totals are accumulated before the cap is applied, so a trimmed list still
                   carries the whole budgeted picture at the top of the panel. */
                budgetTotal += Util.GetValueOfDecimal(row["Budget_Amt"]);
                actualTotal += Util.GetValueOfDecimal(row["Actual_Amt"]);

                if (i >= MAX_DetailRows) { continue; }

                LedgerRow item = new LedgerRow();
                item.Account_ID = Util.GetValueOfInt(row["Account_ID"]);
                item.AccountValue = Util.GetValueOfString(row["Account_Value"]);
                item.AccountName = Util.GetValueOfString(row["Account_Name"]);
                item.Budget = Util.GetValueOfDecimal(row["Budget_Amt"]);
                item.Actual = Util.GetValueOfDecimal(row["Actual_Amt"]);
                item.Balance = item.Budget - item.Actual;

                if (item.Budget != 0)
                {
                    item.HasUtilization = true;
                    item.IsOverBudget = item.Actual * 100m / item.Budget >= 100m;
                }
                else
                {
                    item.IsOverBudget = item.Actual > 0;
                }

                result.Rows.Add(item);
            }

            ApplyDetailMeasures(result, budgetTotal, actualTotal);
        }

        /// <summary>
        /// The panel's four metrics, from the totals of the budgeted accounts it lists.
        ///
        /// The balance and the utilization are derived by <see cref="ApplyRowMeasures"/> - the
        /// CARD's own method, applied to the panel's own figures - so the two cannot diverge the
        /// day the zero-budget or over-budget rule is retuned.
        /// </summary>
        /// <param name="result">Result being filled.</param>
        /// <param name="budget">Summed budget over every budgeted account of the type.</param>
        /// <param name="actual">Summed actual over those same accounts.</param>
        private void ApplyDetailMeasures(TypeDetailResult result, decimal budget, decimal actual)
        {
            List<TypeRow> one = new List<TypeRow>();
            one.Add(new TypeRow { AccountType = result.AccountType, Budget = budget, Actual = actual });

            ApplyRowMeasures(one);

            result.Budget = one[0].Budget;
            result.Actual = one[0].Actual;
            result.Balance = one[0].Balance;
            result.UtilizedPct = one[0].UtilizedPct;
            result.HasUtilization = one[0].HasUtilization;
            result.IsOverBudget = one[0].IsOverBudget;
        }

        // ─────────────────────────────────────────────────────────────────────
        // §5  Transfer objects
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>The whole card in one payload: the four totals, the three rows and what the
        /// subtitle is built from.</summary>
        public class SummaryResult
        {
            /// <summary>One row per account type, in display order: Expense, Revenue, Asset.
            /// Always three, whatever the year holds.</summary>
            public List<TypeRow> Rows { get; set; }

            /// <summary>The financial years the filter can offer, newest first.</summary>
            public List<YearOption> Years { get; set; }

            /// <summary>The accounting schema and its currency - the card's only currency.</summary>
            public AcctContext Schema { get; set; }

            /// <summary>C_Year_ID actually read, after defaulting and validation.</summary>
            public int C_Year_ID { get; set; }

            /// <summary>C_Year.FiscalYear of that year - the pill's label.</summary>
            public string FiscalYear { get; set; }

            /// <summary>SUM of every row's approved budget.</summary>
            public decimal TotalBudget { get; set; }

            /// <summary>SUM of every row's posted actual.</summary>
            public decimal TotalActual { get; set; }

            /// <summary>TotalBudget - TotalActual: the remaining balance.</summary>
            public decimal Balance { get; set; }

            /// <summary>TotalActual / TotalBudget * 100; meaningless unless
            /// HasUtilization.</summary>
            public decimal UtilizedPct { get; set; }

            /// <summary>False when the year has no budget at all - a ratio of nothing is not a
            /// number, and the client prints a dash instead.</summary>
            public bool HasUtilization { get; set; }

            /// <summary>True when the year has consumed its whole budget, or has actuals with no
            /// budget behind them at all.</summary>
            public bool IsOverBudget { get; set; }

            /// <summary>The latest actual posting date in the year, as yyyy-MM-dd; empty when
            /// nothing has been posted yet.</summary>
            public string PostedThrough { get; set; }

            /// <summary>True when the year carries any actual posting - what decides whether the
            /// subtitle names a date or says there are none.</summary>
            public bool HasActual { get; set; }

            /// <summary>ERROR_* token when the tenant is not configured; empty otherwise.</summary>
            public string ErrorCode { get; set; }

            /// <summary>False only on a failure or a missing configuration; a year with nothing
            /// posted is Loaded=true with three zero rows.</summary>
            public bool Loaded { get; set; }
        }

        /// <summary>
        /// One account type's budget against its actual. Amounts are in the PRIMARY accounting
        /// schema currency, never converted, and already stated in the direction the business
        /// reads that type in - the client never re-signs them.
        /// </summary>
        public class TypeRow
        {
            /// <summary>C_ElementValue.AccountType: 'E', 'R' or 'A'. The client resolves the row
            /// label from it - no display text crosses the wire.</summary>
            public string AccountType { get; set; }

            /// <summary>Approved budget for the type: PostingType 'B'.</summary>
            public decimal Budget { get; set; }

            /// <summary>Posted actual for the type: PostingType 'A'.</summary>
            public decimal Actual { get; set; }

            /// <summary>Budget - Actual.</summary>
            public decimal Balance { get; set; }

            /// <summary>Actual / Budget * 100; meaningless unless HasUtilization.</summary>
            public decimal UtilizedPct { get; set; }

            /// <summary>False when the type has no approved budget - the client prints a dash
            /// rather than dividing by zero.</summary>
            public bool HasUtilization { get; set; }

            /// <summary>True when the type has consumed its whole budget, or has actuals with no
            /// budget behind them at all.</summary>
            public bool IsOverBudget { get; set; }

            /// <summary>How many ACTUAL facts the type's BUDGETED accounts carry - postings that
            /// net to nothing have still been made, so the count and not the amount is what says
            /// a type has been posted to.</summary>
            public int ActualCount { get; set; }
        }

        /// <summary>One account type's drill-down: its own totals and the ledger accounts behind
        /// them.</summary>
        public class TypeDetailResult
        {
            /// <summary>The BUDGETED ledger accounts of the type, in account-code order - an
            /// account posted to without a budget is not one of them.</summary>
            public List<LedgerRow> Rows { get; set; }

            /// <summary>The accounting schema and its currency.</summary>
            public AcctContext Schema { get; set; }

            /// <summary>C_ElementValue.AccountType the panel was opened for: 'E', 'R' or 'A'.
            /// The client resolves the panel's title from it - no display text crosses the
            /// wire.</summary>
            public string AccountType { get; set; }

            /// <summary>C_Year_ID actually read, after defaulting and validation.</summary>
            public int C_Year_ID { get; set; }

            /// <summary>C_Year.FiscalYear of that year - the panel's meta line.</summary>
            public string FiscalYear { get; set; }

            /// <summary>Approved budget over the BUDGETED accounts of the type - the sum of the
            /// Budget column, taken before the payload cap.</summary>
            public decimal Budget { get; set; }

            /// <summary>Posted actual over those same budgeted accounts - the sum of the Actual
            /// column, and equal to the card row's Actual cell, which is scoped the same
            /// way.</summary>
            public decimal Actual { get; set; }

            /// <summary>Budget - Actual, over the budgeted accounts.</summary>
            public decimal Balance { get; set; }

            /// <summary>Actual / Budget * 100; meaningless unless HasUtilization.</summary>
            public decimal UtilizedPct { get; set; }

            /// <summary>False when the type has no approved budget - the panel prints a
            /// dash.</summary>
            public bool HasUtilization { get; set; }

            /// <summary>True when the type has consumed its whole budget, or has actuals with no
            /// budget behind them at all.</summary>
            public bool IsOverBudget { get; set; }

            /// <summary>Budgeted ledger accounts in total, before the payload cap.</summary>
            public int TotalRows { get; set; }

            /// <summary>ERROR_* token when the tenant is not configured; empty otherwise.</summary>
            public string ErrorCode { get; set; }

            /// <summary>False when the type or the tenant could not be resolved.</summary>
            public bool Loaded { get; set; }
        }

        /// <summary>One ledger account inside an account type: what was approved for it and what
        /// has been posted to it.</summary>
        public class LedgerRow
        {
            /// <summary>Fact_Acct.Account_ID - the natural account (C_ElementValue_ID).</summary>
            public int Account_ID { get; set; }

            /// <summary>C_ElementValue.Value - the account code.</summary>
            public string AccountValue { get; set; }

            /// <summary>C_ElementValue.Name - the account description.</summary>
            public string AccountName { get; set; }

            /// <summary>Approved budget for the account: PostingType 'B'.</summary>
            public decimal Budget { get; set; }

            /// <summary>Posted actual for the account: PostingType 'A'.</summary>
            public decimal Actual { get; set; }

            /// <summary>Budget - Actual.</summary>
            public decimal Balance { get; set; }

            /// <summary>False when the account has no approved budget.</summary>
            public bool HasUtilization { get; set; }

            /// <summary>True when the account has consumed its whole budget, or has actuals with
            /// no budget behind them at all.</summary>
            public bool IsOverBudget { get; set; }
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
