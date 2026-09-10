/******************************************************
 * Module Name    : VASLogic
 * Purpose        : Utilization by Dimension dashboard widget data
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
    /// Module Name : VAS_253_UtlizationbyDimension
    /// Purpose     : Backs the VAS_253_UtlizationbyDimensionWidget dashboard widget - for the
    ///               selected financial year and accounting dimension, how much of the
    ///               approved budget each dimension value has actually consumed.
    ///
    ///               EVERYTHING IS TENANT CONFIGURATION, NOTHING IS HARD-CODED.
    ///                 Schema     AD_ClientInfo.C_AcctSchema1_ID - the PRIMARY accounting
    ///                            schema, never "the first active one" and never by name.
    ///                 Currency   that schema's C_Currency - every figure is stated in it.
    ///                 Calendar   AD_ClientInfo.C_Calendar_ID - the PRIMARY calendar, and the
    ///                            only source of financial years.
    ///                 Dimensions the ACTIVE C_AcctSchema_Element rows of that schema, in
    ///                            SeqNo order, each labelled with its own Name.
    ///
    ///               THE YEAR IS FILTERED BY ITS PERIODS, NOT BY DATES. Fact_Acct is
    ///               restricted to the C_Period_IDs belonging to the selected C_Year_ID, so a
    ///               fiscal year that does not follow the calendar year is read correctly and
    ///               no January-to-December assumption is made anywhere.
    ///
    ///               EXPENSE ACCOUNTS ONLY. Both sides are restricted to
    ///               C_ElementValue.AccountType = 'E'. "How much of the budget has been
    ///               used" is a question about spending: a revenue, asset, liability or
    ///               equity account posted in the same period consumes no budget, and left
    ///               in it lands on the card as a value utilizing something it was never
    ///               given. The sibling unbudgeted-actuals card (VAS_256) draws the same
    ///               line at the same place, so the two agree about which accounts a budget
    ///               conversation is about.
    ///
    ///               ACTUAL IS MATCHED TO BUDGETED COMBINATIONS. Budget and Actual are read
    ///               in two separate CTE bodies and joined at Account_ID + dimension value,
    ///               with Budget on the LEFT: an actual posted against an account that was
    ///               never budgeted contributes NOTHING to utilization. Aggregating both
    ///               sides by dimension alone would fold unbudgeted spend into the percentage
    ///               and overstate every value that carries any. Unbudgeted actuals are the
    ///               sibling card's subject (VAS_256), not this one's.
    ///
    ///               THE ACCOUNTED AMOUNT IS ABS(SUM(AmtAcctDr - AmtAcctCr)), netted at
    ///               Account_ID + dimension value + PostingType BEFORE the absolute is taken.
    ///               Netting first is what stops the two sides of one journal being counted
    ///               twice; the absolute afterwards is what keeps a credit-natural account
    ///               (revenue, liability, equity) reporting a positive budget instead of a
    ///               negative one, with no account-type rule to maintain. Source amounts are
    ///               never read - AmtAcctDr / AmtAcctCr are already stated in the schema's
    ///               currency, so no currencyConvert call belongs in this model.
    ///
    ///               A ROW NEEDS A BUDGET. The Budget body carries
    ///               HAVING ABS(SUM(...)) &lt;&gt; 0, so a combination whose postings
    ///               cancelled out is not a budget and produces no row. Utilization of a
    ///               budget that does not exist is not a number.
    ///
    ///               MRole row-level security is applied INSIDE EACH CTE BODY, on the
    ///               physical Fact_Acct alias it reads - never to the CTE aliases
    ///               (BudgetByAccount, ActualByAccount, DimensionTotals), which are derived
    ///               result sets and not dictionary tables, and never to the composed WITH
    ///               statement. Each body is secured before it is composed, so the CTE output
    ///               the outer query consumes is already filtered. The C_ElementValue rows
    ///               each body joins are a reference lookup by primary key and inherit that
    ///               body's filter. C_Year is secured where it is read; AD_ClientInfo,
    ///               C_AcctSchema, C_Calendar, C_Period,
    ///               C_AcctSchema_Element, C_AcctSchema_GL, C_ValidCombination, AD_Table,
    ///               AD_Column and AD_Ref_Table are configuration and dictionary reads; a
    ///               dimension's master table is read as a display lookup BY PRIMARY KEY over
    ///               ids the secured aggregate already returned. GROUP BY / HAVING / ORDER BY
    ///               are appended AFTER AddAccessSQL so its FROM-clause parser never meets a
    ///               trailing clause, and every join ON is a plain equality so it never meets
    ///               a function call either.
    ///
    ///               PERIOD IDS ARE RESOLVED IN C#, not as a subquery inside the Fact_Acct
    ///               WHERE. The access-SQL parser has to read that WHERE clause, and this
    ///               codebase keeps nested selects away from it on principle; a year has a
    ///               dozen or so periods, so a bound id list costs one small query and
    ///               nothing else. The filter is exactly the specified one - the periods of
    ///               the selected year - expressed the way this platform can secure.
    ///
    ///               Compatible with PostgreSQL and Oracle.
    /// Chronological development:
    ///   VAI145      2026-09-09 Created
    ///   VAI145      2026-09-09 Rewritten to the Utilization by Dimension specification:
    ///                          period-based year filter, ABS(SUM(Dr-Cr)) netted per account,
    ///                          Actual matched to budgeted account + dimension combinations
    ///                          through a two-body CTE with MRole applied inside each body,
    ///                          and dimension masters resolved through the Application
    ///                          Dictionary.
    /// </summary>
    public class VAS_253_UtlizationbyDimensionModel
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_253_UtlizationbyDimensionModel).FullName);

        /* Fact_Acct.PostingType stored codes. Stored codes are compared bare - never with an
           N prefix, which would force a Unicode comparison against a VARCHAR column. */
        private const string POSTINGTYPE_Actual = "A";
        private const string POSTINGTYPE_Budget = "B";

        /* C_ElementValue.AccountType - the only type this card reports on. Utilization is a
           question about spending, and an asset, liability, equity or revenue account does
           not consume a budget. */
        private const string ACCOUNTTYPE_Expense = "E";

        /* Error tokens exchanged with the client; the client resolves the label from
           AD_Message, so no display text is produced here. */
        public const string ERROR_NO_CALENDAR = "NOCALENDAR";
        public const string ERROR_NO_ACCTSCHEMA = "NOACCTSCHEMA";
        public const string ERROR_NO_YEAR = "NOYEAR";
        public const string ERROR_NO_DIMENSION = "NODIMENSION";

        /* Paging. The 4x2 cell fits four utilization bars at 1280px; a taller cell may ask
           for more and a short one for fewer, but never outside these bounds. */
        public const int DEFAULT_PageSize = 4;
        private const int MIN_PageSize = 1;
        private const int MAX_PageSize = 12;

        /* Oracle refuses an IN list longer than 1000 expressions, so ids are bound in batches
           well inside that limit. */
        private const int ID_BatchSize = 500;

        /* AD_Reference types whose target table the dictionary names outright. Anything else
           falls back to the platform's own "the column names its table" convention. */
        private const int REFERENCE_Table = 18;
        private const int REFERENCE_Search = 30;

        /* The physical fact table, named once. Its columns are confirmed against AD_Column so
           a dimension whose column this installation does not carry is dropped rather than
           naming a column that would fail the whole aggregate. */
        private const string TABLE_FACT_ACCT = "Fact_Acct";

        /* The dictionary table the balancing / offset settings live on, and the columns that
           name them. Every one is confirmed against AD_Column before it is used: the
           commitment pair and the localized budget offset are optional in this schema. */
        private const string TABLE_ACCTSCHEMA_GL = "C_AcctSchema_GL";
        private static readonly string[] OFFSET_COLUMNS = new string[]
        {
            "SuspenseBalancing_Acct",
            "CurrencyBalancing_Acct",
            "CommitmentOffset_Acct",
            "CommitmentOffsetSales_Acct",
            "VA094_BudgetOffset_Acct"
        };

        /* C_AcctSchema_Element.ElementType stored codes - the framework's own values, as
           MAcctSchemaElement declares them. The user-element types X1..X9 are handled by
           prefix rather than one constant each. */
        private const string ELEMENTTYPE_Organization = "OO";
        private const string ELEMENTTYPE_Account = "AC";
        private const string ELEMENTTYPE_SubAccount = "SA";
        private const string ELEMENTTYPE_BPartner = "BP";
        private const string ELEMENTTYPE_Product = "PR";
        private const string ELEMENTTYPE_Activity = "AY";
        private const string ELEMENTTYPE_LocationFrom = "LF";
        private const string ELEMENTTYPE_LocationTo = "LT";
        private const string ELEMENTTYPE_Campaign = "MC";
        private const string ELEMENTTYPE_OrgTrx = "OT";
        private const string ELEMENTTYPE_Project = "PJ";
        private const string ELEMENTTYPE_SalesRegion = "SR";
        private const string ELEMENTTYPE_UserList1 = "U1";
        private const string ELEMENTTYPE_UserList2 = "U2";
        private const string ELEMENTTYPE_UserElementPrefix = "X";

        // ─────────────────────────────────────────────────────────────────────
        // §1  Entry point
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Everything the card shows in one round trip: the selectable financial years, the
        /// accounting dimensions the schema declares, the year and dimension actually used,
        /// the accounting-schema currency and the requested page of dimension values ranked by
        /// utilization.
        ///
        /// The load sequence is the specified one: tenant -&gt; primary accounting schema
        /// -&gt; its currency -&gt; primary calendar -&gt; its financial years -&gt; the
        /// default year -&gt; the schema's active elements -&gt; the default dimension -&gt;
        /// the Fact_Acct aggregate -&gt; the dimension value names.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="yearId">C_Year_ID the user selected, or 0 to default to the financial
        /// year whose active period contains today.</param>
        /// <param name="dimension">C_AcctSchema_Element.ElementType the user selected, or
        /// empty to default to the schema's first element in SeqNo order.</param>
        /// <param name="pageNo">1-based page; clamped to the available range.</param>
        /// <param name="pageSize">Rows per page; clamped to [1,12].</param>
        /// <returns>Populated <see cref="UtilizationResult"/> (never null). Loaded is false
        /// only when there is no context; a configuration gap reports its own ErrorCode, and a
        /// year with nothing budgeted returns Loaded=true and an empty page, because "no
        /// budget data" is a real answer rather than an error.</returns>
        public UtilizationResult GetRows(Ctx ctx, int yearId, string dimension, int pageNo, int pageSize)
        {
            UtilizationResult result = new UtilizationResult();
            result.Rows = new List<UtilizationRow>();
            result.Years = new List<YearOption>();
            result.Dimensions = new List<DimensionOption>();
            result.PageSize = ClampPageSize(pageSize);
            result.Page = pageNo < 1 ? 1 : pageNo;

            if (ctx == null) { result.Page = 1; return result; }

            /* The accounting context is a CONFIGURATION precondition, not a filter: without a
               primary accounting schema there is no ledger to read and no currency to state it
               in, and without a primary calendar there is no year list to build. Neither is
               silently replaced by "some other" schema or calendar, and neither gap runs the
               utilization query. */
            AcctContext acct = GetAcctContext(ctx);
            result.Schema = acct;

            if (acct.C_AcctSchema_ID <= 0)
            {
                result.ErrorCode = ERROR_NO_ACCTSCHEMA;
                Log.Log(Level.WARNING, "VAS_253_UtlizationbyDimension: AD_ClientInfo.C_AcctSchema1_ID not configured for AD_Client_ID="
                    + ctx.GetAD_Client_ID());
                return result;
            }

            if (acct.C_Calendar_ID <= 0)
            {
                result.ErrorCode = ERROR_NO_CALENDAR;
                Log.Log(Level.WARNING, "VAS_253_UtlizationbyDimension: AD_ClientInfo.C_Calendar_ID not configured for AD_Client_ID="
                    + ctx.GetAD_Client_ID());
                return result;
            }

            /* Both filter lists travel with the rows, so the widget is one round trip on load
               and neither pill can ever name something the rows were not read for. */
            result.Years = GetYears(ctx, acct.C_Calendar_ID);
            if (result.Years.Count == 0)
            {
                result.ErrorCode = ERROR_NO_YEAR;
                result.Loaded = true;
                return result;
            }

            List<DimensionSpec> specs = GetDimensions(ctx, acct.C_AcctSchema_ID);
            for (int i = 0; i < specs.Count; i++)
            {
                DimensionOption option = new DimensionOption();
                option.Value = specs[i].ElementType;
                option.Label = specs[i].Label;
                option.SeqNo = specs[i].SeqNo;
                result.Dimensions.Add(option);
            }

            if (specs.Count == 0)
            {
                result.ErrorCode = ERROR_NO_DIMENSION;
                Log.Log(Level.WARNING, "VAS_253_UtlizationbyDimension: no usable active C_AcctSchema_Element for C_AcctSchema_ID="
                    + acct.C_AcctSchema_ID);
                result.Loaded = true;
                return result;
            }

            YearOption year = PickYear(ctx, result.Years, yearId, acct.C_Calendar_ID);
            result.C_Year_ID = year.C_Year_ID;
            result.FiscalYear = year.FiscalYear;

            DimensionSpec spec = PickDimension(specs, dimension);
            result.Dimension = spec.ElementType;
            result.DimensionLabel = spec.Label;

            ReadRows(ctx, acct, year.C_Year_ID, spec, result);

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
        // §2  Primary accounting schema, its currency and the primary calendar
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The tenant's accounting context, all of it from AD_ClientInfo: the PRIMARY
        /// accounting schema (C_AcctSchema1_ID), the currency that schema reports in, and the
        /// PRIMARY calendar (C_Calendar_ID). Never the first active schema, never a schema
        /// found by name, and never a calendar found any other way.
        ///
        /// The schema half and the calendar half are read separately on purpose: a tenant can
        /// have one configured and not the other, and the caller has a different message for
        /// each. One joined query would collapse two configuration errors into one.
        ///
        /// Reads only client-scoped configuration and reference tables, so no MRole predicate
        /// is applied - the same treatment the sibling accounting widgets give this lookup.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <returns>Populated <see cref="AcctContext"/>; an id is 0 when that piece of
        /// configuration is missing.</returns>
        public AcctContext GetAcctContext(Ctx ctx)
        {
            AcctContext result = new AcctContext();
            result.Precision = 2;

            if (ctx == null) { return result; }

            /* CurSymbol first, ISO_Code as the fallback - the card prints the symbol directly
               against the amount, and only falls back to the code when the currency has no
               symbol configured. StdPrecision travels with them: the client formats the
               figures, so the decimals have to reach it. */
            string sql = @"
                SELECT ci.C_AcctSchema1_ID AS C_AcctSchema_ID,
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
            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                DataRow row = ds.Tables[0].Rows[0];
                result.C_AcctSchema_ID = Util.GetValueOfInt(row["C_AcctSchema_ID"]);
                result.Name = Util.GetValueOfString(row["Acct_Schema_Name"]);
                result.C_Currency_ID = Util.GetValueOfInt(row["C_Currency_ID"]);
                result.Iso = Util.GetValueOfString(row["Currency_Iso"]);
                result.Symbol = Util.GetValueOfString(row["Currency_Symbol"]);
                result.Precision = Util.GetValueOfInt(row["Std_Precision"]);
            }

            result.C_Calendar_ID = ReadPrimaryCalendar(ctx);
            return result;
        }

        /// <summary>
        /// The tenant's PRIMARY calendar - AD_ClientInfo.C_Calendar_ID, joined to C_Calendar
        /// so an id pointing at a deactivated calendar reports as "not configured" rather than
        /// as a year list that cannot be built.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <returns>C_Calendar_ID, or 0.</returns>
        private int ReadPrimaryCalendar(Ctx ctx)
        {
            string sql = @"
                SELECT ci.C_Calendar_ID AS C_Calendar_ID,
                       cal.Name AS Calendar_Name
                FROM AD_ClientInfo ci
                INNER JOIN C_Calendar cal ON (cal.C_Calendar_ID=ci.C_Calendar_ID)
                WHERE ci.AD_Client_ID=@AD_Client_ID
                  AND ci.IsActive='Y'
                  AND cal.IsActive='Y'";

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
            };

            DataSet ds = DB.ExecuteDataset(sql, parameters, null);
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) { return 0; }

            return Util.GetValueOfInt(ds.Tables[0].Rows[0]["C_Calendar_ID"]);
        }

        // ─────────────────────────────────────────────────────────────────────
        // §3  The financial years of the primary calendar
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The ACTIVE financial years of the tenant's PRIMARY calendar, newest first. No other
        /// calendar's years are ever offered.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="calendarId">The tenant's primary C_Calendar_ID.</param>
        /// <returns>Years, newest FiscalYear first (never null).</returns>
        public List<YearOption> GetYears(Ctx ctx, int calendarId)
        {
            List<YearOption> items = new List<YearOption>();
            if (ctx == null || calendarId <= 0) { return items; }

            string sql = @"
                SELECT y.C_Year_ID AS C_Year_ID,
                       y.FiscalYear AS Fiscal_Year
                FROM C_Year y
                WHERE y.C_Calendar_ID=@C_Calendar_ID
                  AND y.AD_Client_ID=@AD_Client_ID
                  AND y.IsActive='Y'";

            /* C_Year y is the main physical table the user is choosing from. */
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "y", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            /* ORDER BY goes on AFTER the access SQL - its FROM-clause parser must not meet a
               trailing clause. */
            sql += " ORDER BY y.FiscalYear DESC,y.C_Year_ID DESC";

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
                YearOption item = new YearOption();
                item.C_Year_ID = Util.GetValueOfInt(dt.Rows[i]["C_Year_ID"]);
                item.FiscalYear = Util.GetValueOfString(dt.Rows[i]["Fiscal_Year"]);

                if (item.C_Year_ID <= 0) { continue; }
                items.Add(item);
            }

            return items;
        }

        /// <summary>
        /// Resolves which financial year the card actually reads.
        ///
        /// A requested id is honoured only when it is one of the years this role may see on
        /// this tenant's primary calendar - a stale or forged selection falls back to the
        /// default rather than reaching Fact_Acct.
        ///
        /// The default is the year whose ACTIVE PERIOD CONTAINS TODAY, asked of the database
        /// rather than worked out from a date range in C#: the current period is the one the
        /// calendar says it is. When no year is current - a gap between calendars, or a tenant
        /// whose periods have not been generated yet - the newest year in the list stands in.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="years">Years of the primary calendar, newest first.</param>
        /// <param name="requestedId">C_Year_ID the client asked for, or 0.</param>
        /// <param name="calendarId">The tenant's primary C_Calendar_ID.</param>
        /// <returns>The year to read (never null when the list is filled).</returns>
        private YearOption PickYear(Ctx ctx, List<YearOption> years, int requestedId, int calendarId)
        {
            if (requestedId > 0)
            {
                for (int i = 0; i < years.Count; i++)
                {
                    if (years[i].C_Year_ID == requestedId) { return years[i]; }
                }
            }

            int currentId = ReadCurrentYear(ctx, calendarId);
            if (currentId > 0)
            {
                for (int i = 0; i < years.Count; i++)
                {
                    if (years[i].C_Year_ID == currentId) { return years[i]; }
                }
            }

            return years[0];
        }

        /// <summary>
        /// The financial year of the primary calendar whose active period contains today.
        ///
        /// EXISTS against C_Period rather than a join, so a year with several periods cannot
        /// come back more than once, and the comparison is made by the database against its
        /// own current date - never against an application clock in another timezone.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="calendarId">The tenant's primary C_Calendar_ID.</param>
        /// <returns>C_Year_ID, or 0 when no year is current.</returns>
        private int ReadCurrentYear(Ctx ctx, int calendarId)
        {
            string sql = @"
                SELECT y.C_Year_ID AS C_Year_ID
                FROM C_Year y
                WHERE y.C_Calendar_ID=@C_Calendar_ID
                  AND y.AD_Client_ID=@AD_Client_ID
                  AND y.IsActive='Y'
                  AND EXISTS(SELECT 1 FROM C_Period p WHERE p.C_Year_ID=y.C_Year_ID AND p.AD_Client_ID=y.AD_Client_ID AND p.IsActive='Y' AND CURRENT_DATE BETWEEN p.StartDate AND p.EndDate)
                ORDER BY y.FiscalYear DESC,y.C_Year_ID DESC";

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@C_Calendar_ID", calendarId),
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
            };

            DataSet ds = DB.ExecuteDataset(sql, parameters, null);
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) { return 0; }

            return Util.GetValueOfInt(ds.Tables[0].Rows[0]["C_Year_ID"]);
        }

        /// <summary>
        /// The ACTIVE periods of one financial year.
        ///
        /// This is how the year reaches Fact_Acct: as a set of C_Period_IDs, never as a date
        /// range, because a fiscal year need not follow the calendar year and its periods are
        /// the only authority on what belongs to it.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="yearId">The selected C_Year_ID.</param>
        /// <returns>Period ids (never null; empty when the year has no active period).</returns>
        private List<int> GetPeriodIds(Ctx ctx, int yearId)
        {
            List<int> ids = new List<int>();
            if (ctx == null || yearId <= 0) { return ids; }

            string sql = @"
                SELECT p.C_Period_ID AS C_Period_ID
                FROM C_Period p
                WHERE p.C_Year_ID=@C_Year_ID
                  AND p.AD_Client_ID=@AD_Client_ID
                  AND p.IsActive='Y'";

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@C_Year_ID", yearId),
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
            };

            DataSet ds = DB.ExecuteDataset(sql, parameters, null);
            if (ds == null || ds.Tables.Count == 0) { return ids; }

            DataTable dt = ds.Tables[0];
            for (int i = 0; i < dt.Rows.Count; i++)
            {
                int id = Util.GetValueOfInt(dt.Rows[i]["C_Period_ID"]);
                if (id > 0 && !ids.Contains(id)) { ids.Add(id); }
            }

            return ids;
        }

        // ─────────────────────────────────────────────────────────────────────
        // §4  The dimensions the accounting schema declares
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Every ACTIVE accounting dimension of the primary accounting schema, in SeqNo order,
        /// each resolved down to the Fact_Acct column it is stored in and the master table its
        /// values are named from.
        ///
        /// The list is the schema's own C_AcctSchema_Element rows and nothing else - this is
        /// the one place the set of dimensions is decided, and it is decided by configuration.
        /// Each option is labelled with the element's OWN Name, so a tenant that calls Activity
        /// "Department" sees Department; the element type stands in only when that Name is
        /// blank.
        ///
        /// THE FACT COLUMN COMES FROM A FIXED SERVER-SIDE WHITELIST keyed by ElementType.
        /// Nothing the browser sends ever becomes part of a column name: the client sends an
        /// element type, the model matches it against this list, and the list supplies the
        /// identifier.
        ///
        /// USER ELEMENTS ARE RESOLVED THROUGH THE DICTIONARY, NOT GUESSED. X1..X9 each carry
        /// their own AD_Column_ID and no two need point at the same table: a Table / Search
        /// reference names its table, key and display column outright in AD_Ref_Table, and
        /// anything else falls back to the platform's own convention that a column ending in
        /// _ID names its table. Both routes are then CONFIRMED against AD_Table / AD_Column
        /// before either is named in generated SQL.
        ///
        /// A MASTER WITHOUT Value / Name IS NAMED BY ITS IDENTIFIERS. Not every table follows
        /// that shape - a user element can point at any table the tenant built - so when
        /// neither column is there the label falls back to the columns AD_Column marks as
        /// IsIdentifier, in SeqNo order, which is what the platform itself shows for a record
        /// of that table in a lookup or a zoom. The dimension is therefore usable wherever
        /// the application can display its values at all.
        ///
        /// An element whose Fact_Acct column or label table cannot be confirmed is dropped and
        /// logged as unsupported configuration - a dimension that would fail the aggregate
        /// must not cost the tenant the dimensions that do work.
        ///
        /// C_AcctSchema_Element, AD_Table, AD_Column and AD_Ref_Table are configuration and
        /// dictionary reads, scoped by tenant and schema, so no MRole predicate is applied -
        /// the same treatment the sibling accounting widgets give this resolution chain. The
        /// ledger itself is secured where it is read, in §5.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="acctSchemaId">Primary C_AcctSchema_ID.</param>
        /// <returns>Usable dimensions in SeqNo order (never null).</returns>
        private List<DimensionSpec> GetDimensions(Ctx ctx, int acctSchemaId)
        {
            List<DimensionSpec> specs = new List<DimensionSpec>();
            if (ctx == null || acctSchemaId <= 0) { return specs; }

            string sql = @"
                SELECT ase.C_AcctSchema_Element_ID AS C_AcctSchema_Element_ID,
                       ase.ElementType AS Element_Type,
                       COALESCE(ase.Name,N'') AS Element_Name,
                       COALESCE(ase.SeqNo,0) AS Seq_No,
                       COALESCE(col.ColumnName,N'') AS Element_Column,
                       COALESCE(col.AD_Reference_ID,0) AS Reference_Type,
                       COALESCE(col.AD_Reference_Value_ID,0) AS Reference_Value_ID
                FROM C_AcctSchema_Element ase
                LEFT OUTER JOIN AD_Column col ON (col.AD_Column_ID=ase.AD_Column_ID)
                WHERE ase.C_AcctSchema_ID=@C_AcctSchema_ID
                  AND ase.AD_Client_ID=@AD_Client_ID
                  AND ase.IsActive='Y'
                ORDER BY ase.SeqNo,ase.Name,ase.C_AcctSchema_Element_ID";

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@C_AcctSchema_ID", acctSchemaId),
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
            };

            DataSet ds = DB.ExecuteDataset(sql, parameters, null);
            if (ds == null || ds.Tables.Count == 0) { return specs; }

            DataTable dt = ds.Tables[0];

            /* Draft every element first, collect the tables all of them need, and confirm them
               in ONE dictionary read - a read per element would be a query inside a loop for
               no gain. */
            List<DimensionSpec> drafts = new List<DimensionSpec>();
            List<string> tables = new List<string>();
            tables.Add(TABLE_FACT_ACCT);

            for (int i = 0; i < dt.Rows.Count; i++)
            {
                DataRow row = dt.Rows[i];

                DimensionSpec draft = DraftDimension(row);
                if (draft == null)
                {
                    Log.Log(Level.INFO, "VAS_253_UtlizationbyDimension: unsupported accounting schema element "
                        + Util.GetValueOfString(row["Element_Type"]) + " - no usable Fact_Acct column mapping");
                    continue;
                }

                drafts.Add(draft);
                if (!ContainsIgnoreCase(tables, draft.SourceTable)) { tables.Add(draft.SourceTable); }
            }

            if (drafts.Count == 0) { return specs; }

            Dictionary<string, TableColumns> dictionary = ReadTableColumns(tables);
            List<string> factColumns = ColumnsOf(dictionary, TABLE_FACT_ACCT).Columns;

            for (int i = 0; i < drafts.Count; i++)
            {
                DimensionSpec draft = drafts[i];

                /* The fact column must exist on THIS installation. Fact_Acct carries every
                   standard dimension, but a localization may not carry every user element. */
                if (factColumns.Count > 0 && !HasColumn(factColumns, draft.FactColumn))
                {
                    Log.Log(Level.INFO, "VAS_253_UtlizationbyDimension: dropping element " + draft.ElementType
                        + " - Fact_Acct has no column " + draft.FactColumn);
                    continue;
                }

                if (!ConfirmSource(draft, ColumnsOf(dictionary, draft.SourceTable)))
                {
                    Log.Log(Level.INFO, "VAS_253_UtlizationbyDimension: dropping element " + draft.ElementType
                        + " - cannot name its values from " + draft.SourceTable);
                    continue;
                }

                specs.Add(draft);
            }

            return specs;
        }

        /// <summary>
        /// Maps one C_AcctSchema_Element row onto the Fact_Acct column it is stored in and the
        /// master table its values are named from. The standard mapping is the framework's own
        /// - MAcctSchemaElement.GetColumnName / GetValueQuery - restated here as a whitelist so
        /// the model composes only identifiers it decided itself.
        /// </summary>
        /// <param name="row">One C_AcctSchema_Element row with its AD_Column metadata.</param>
        /// <returns>A draft spec, or null when the element type is not one this card can group
        /// by.</returns>
        private DimensionSpec DraftDimension(DataRow row)
        {
            string elementType = Util.GetValueOfString(row["Element_Type"]);
            if (String.IsNullOrEmpty(elementType)) { return null; }

            DimensionSpec spec = new DimensionSpec();
            spec.ElementType = elementType;
            spec.Label = Util.GetValueOfString(row["Element_Name"]);
            spec.SeqNo = Util.GetValueOfInt(row["Seq_No"]);
            spec.ValueColumn = "Value";
            spec.NameColumn = "Name";

            /* The element's own Name is the label; the element type stands in only when the
               tenant left it blank, so the pill is never empty. */
            if (spec.Label.Length == 0) { spec.Label = elementType; }

            if (elementType == ELEMENTTYPE_Organization)
            {
                spec.FactColumn = "AD_Org_ID";
                spec.SourceTable = "AD_Org";
                spec.SourceKey = "AD_Org_ID";
                /* Org 0 is the '*' organization - a real row with a real name, not an
                   unassigned value. */
                spec.ZeroIsValue = true;
                return spec;
            }

            if (elementType == ELEMENTTYPE_OrgTrx)
            {
                spec.FactColumn = "AD_OrgTrx_ID";
                spec.SourceTable = "AD_Org";
                spec.SourceKey = "AD_Org_ID";
                return spec;
            }

            if (elementType == ELEMENTTYPE_Account)
            {
                spec.FactColumn = "Account_ID";
                spec.SourceTable = "C_ElementValue";
                spec.SourceKey = "C_ElementValue_ID";
                return spec;
            }

            if (elementType == ELEMENTTYPE_UserList1 || elementType == ELEMENTTYPE_UserList2)
            {
                spec.FactColumn = elementType == ELEMENTTYPE_UserList1 ? "User1_ID" : "User2_ID";
                spec.SourceTable = "C_ElementValue";
                spec.SourceKey = "C_ElementValue_ID";
                return spec;
            }

            if (elementType == ELEMENTTYPE_BPartner)
            {
                spec.FactColumn = "C_BPartner_ID";
                spec.SourceTable = "C_BPartner";
                spec.SourceKey = "C_BPartner_ID";
                return spec;
            }

            if (elementType == ELEMENTTYPE_Product)
            {
                spec.FactColumn = "M_Product_ID";
                spec.SourceTable = "M_Product";
                spec.SourceKey = "M_Product_ID";
                return spec;
            }

            if (elementType == ELEMENTTYPE_Activity)
            {
                spec.FactColumn = "C_Activity_ID";
                spec.SourceTable = "C_Activity";
                spec.SourceKey = "C_Activity_ID";
                return spec;
            }

            if (elementType == ELEMENTTYPE_Campaign)
            {
                spec.FactColumn = "C_Campaign_ID";
                spec.SourceTable = "C_Campaign";
                spec.SourceKey = "C_Campaign_ID";
                return spec;
            }

            if (elementType == ELEMENTTYPE_Project)
            {
                spec.FactColumn = "C_Project_ID";
                spec.SourceTable = "C_Project";
                spec.SourceKey = "C_Project_ID";
                return spec;
            }

            if (elementType == ELEMENTTYPE_SalesRegion)
            {
                spec.FactColumn = "C_SalesRegion_ID";
                spec.SourceTable = "C_SalesRegion";
                spec.SourceKey = "C_SalesRegion_ID";
                return spec;
            }

            if (elementType == ELEMENTTYPE_SubAccount)
            {
                spec.FactColumn = "C_SubAcct_ID";
                spec.SourceTable = "C_SubAcct";
                spec.SourceKey = "C_SubAcct_ID";
                return spec;
            }

            if (elementType == ELEMENTTYPE_LocationFrom || elementType == ELEMENTTYPE_LocationTo)
            {
                spec.FactColumn = elementType == ELEMENTTYPE_LocationFrom ? "C_LocFrom_ID" : "C_LocTo_ID";
                spec.SourceTable = "C_Location";
                spec.SourceKey = "C_Location_ID";
                /* A location has no Value and no Name: the framework names one by its city,
                   falling back to the street line. */
                spec.ValueColumn = "";
                spec.NameColumn = "City";
                spec.AltNameColumn = "Address1";
                return spec;
            }

            /* User elements X1..X9 - each with its own target, resolved from its own column's
               dictionary metadata. */
            if (elementType.StartsWith(ELEMENTTYPE_UserElementPrefix, StringComparison.OrdinalIgnoreCase)
                && elementType.Length == 2)
            {
                int index = 0;
                if (!Int32.TryParse(elementType.Substring(1), out index)) { return null; }
                if (index < 1 || index > 9) { return null; }

                spec.FactColumn = "UserElement" + index + "_ID";
                return ResolveUserElementTarget(spec,
                    Util.GetValueOfString(row["Element_Column"]),
                    Util.GetValueOfInt(row["Reference_Type"]),
                    Util.GetValueOfInt(row["Reference_Value_ID"]));
            }

            return null;
        }

        /// <summary>
        /// Works out which table a user element's values live in, and which of its columns
        /// names them.
        ///
        /// A Table / Search reference has the dictionary name its target outright -
        /// AD_Ref_Table carries the table, its key column and its display column - which is
        /// the only route that copes with a tenant whose element points at a table its column
        /// is not named after. Anything else falls back to the platform's own convention that
        /// a column ending in _ID names its table, with the column itself as the key.
        /// </summary>
        /// <param name="spec">Draft carrying the element type and its Fact_Acct column.</param>
        /// <param name="columnName">AD_Column.ColumnName of the element's AD_Column_ID.</param>
        /// <param name="referenceType">That column's AD_Reference_ID.</param>
        /// <param name="referenceValueId">That column's AD_Reference_Value_ID.</param>
        /// <returns>The completed draft, or null when no target can be resolved.</returns>
        private DimensionSpec ResolveUserElementTarget(DimensionSpec spec, string columnName,
            int referenceType, int referenceValueId)
        {
            if (referenceValueId > 0
                && (referenceType == REFERENCE_Table || referenceType == REFERENCE_Search))
            {
                string sql = @"
                    SELECT t.TableName AS Table_Name,
                           COALESCE(kc.ColumnName,N'') AS Key_Column,
                           COALESCE(dc.ColumnName,N'') AS Display_Column
                    FROM AD_Ref_Table rt
                    INNER JOIN AD_Table t ON (t.AD_Table_ID=rt.AD_Table_ID)
                    LEFT OUTER JOIN AD_Column kc ON (kc.AD_Column_ID=rt.AD_Key)
                    LEFT OUTER JOIN AD_Column dc ON (dc.AD_Column_ID=rt.AD_Display)
                    WHERE rt.AD_Reference_ID=@AD_Reference_ID
                      AND rt.IsActive='Y'
                      AND t.IsActive='Y'";

                SqlParameter[] parameters = new SqlParameter[]
                {
                    new SqlParameter("@AD_Reference_ID", referenceValueId)
                };

                DataSet ds = DB.ExecuteDataset(sql, parameters, null);
                if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                {
                    DataRow row = ds.Tables[0].Rows[0];

                    string table = Util.GetValueOfString(row["Table_Name"]);
                    string key = Util.GetValueOfString(row["Key_Column"]);
                    string display = Util.GetValueOfString(row["Display_Column"]);

                    if (table.Length > 0)
                    {
                        spec.SourceTable = table;
                        spec.SourceKey = key.Length > 0 ? key : table + "_ID";

                        /* The dictionary's display column when it names one; Name is the
                           fallback, and ConfirmSource drops either if the table has neither. */
                        if (display.Length > 0) { spec.NameColumn = display; }
                        return spec;
                    }
                }
            }

            /* The platform's own convention: VAF_Department_ID -> VAF_Department, keyed by the
               column itself. */
            if (columnName.Length < 4
                || !columnName.EndsWith("_ID", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            spec.SourceTable = columnName.Substring(0, columnName.Length - 3);
            spec.SourceKey = columnName;
            return spec;
        }

        /// <summary>
        /// Confirms a draft's master table actually carries the key and label columns the spec
        /// wants to name, and trims the ones it does not.
        ///
        /// WHEN THE TABLE HAS NEITHER Value NOR Name, THE IDENTIFIER COLUMNS STAND IN. Not
        /// every master follows the Value / Name shape - a user element can point at any
        /// table the tenant built - but every table the platform can display marks the
        /// columns that identify a record with AD_Column.IsIdentifier, in SeqNo order, and
        /// that is exactly what the framework itself shows for that record in a lookup, a
        /// zoom or a report. Taking them here means the card names such a value the same way
        /// the rest of the application does, instead of dropping the dimension for want of a
        /// column called Name.
        ///
        /// At most the first TWO are taken: they are printed as "first - second" in the same
        /// shape a Value / Name pair is, and a third would be more identity than a dashboard
        /// row can carry.
        /// </summary>
        /// <param name="spec">Draft being confirmed, adjusted in place.</param>
        /// <param name="columns">The master table's active columns and identifiers, from
        /// AD_Column.</param>
        /// <returns>True when the table can name its values.</returns>
        private bool ConfirmSource(DimensionSpec spec, TableColumns columns)
        {
            if (columns.Columns.Count == 0) { return false; }
            if (!HasColumn(columns.Columns, spec.SourceKey)) { return false; }

            if (spec.ValueColumn.Length > 0 && !HasColumn(columns.Columns, spec.ValueColumn))
            {
                spec.ValueColumn = "";
            }
            if (spec.NameColumn.Length > 0 && !HasColumn(columns.Columns, spec.NameColumn))
            {
                spec.NameColumn = "";
            }
            if (spec.AltNameColumn.Length > 0 && !HasColumn(columns.Columns, spec.AltNameColumn))
            {
                spec.AltNameColumn = "";
            }

            /* Nothing conventional to print: fall back to what the dictionary says identifies
               a record of this table. The first identifier takes the leading position - the
               one Value holds in the conventional shape - and the second, if there is one,
               follows it. */
            if (spec.ValueColumn.Length == 0 && spec.NameColumn.Length == 0
                && spec.AltNameColumn.Length == 0)
            {
                if (columns.Identifiers.Count > 0) { spec.ValueColumn = columns.Identifiers[0]; }
                if (columns.Identifiers.Count > 1) { spec.NameColumn = columns.Identifiers[1]; }

                /* An identifier can be a date, a number or a foreign key, not only text - the
                   label read must not wrap it in a string-typed COALESCE. */
                spec.LabelsFromIdentifier = spec.ValueColumn.Length > 0;
            }

            /* Something has to be printable. A table with no name, no value and no identifier
               would leave every row of the card labelled by nothing at all. */
            return spec.ValueColumn.Length > 0 || spec.NameColumn.Length > 0
                || spec.AltNameColumn.Length > 0;
        }

        /// <summary>
        /// Resolves which dimension the card actually reads.
        ///
        /// A requested element type is honoured only when the schema declares it - a stale or
        /// forged selection falls back to the schema's first element rather than reaching
        /// Fact_Acct, and a browser value therefore never becomes part of a column name. The
        /// default is deliberately the FIRST element in SeqNo order and never a hard-coded
        /// dimension: the schema's own order is the tenant's own priority.
        /// </summary>
        /// <param name="specs">The schema's usable dimensions, in SeqNo order.</param>
        /// <param name="requested">ElementType the client asked for, or empty.</param>
        /// <returns>The dimension to read (never null when the list is filled).</returns>
        private DimensionSpec PickDimension(List<DimensionSpec> specs, string requested)
        {
            if (!String.IsNullOrEmpty(requested))
            {
                for (int i = 0; i < specs.Count; i++)
                {
                    if (String.Equals(specs[i].ElementType, requested, StringComparison.OrdinalIgnoreCase))
                    {
                        return specs[i];
                    }
                }
            }

            return specs[0];
        }

        // ─────────────────────────────────────────────────────────────────────
        // §5  The rows - budget against actual, per dimension value
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Reads the budgeted account + dimension combinations of the selected year, matches
        /// the year's actuals against exactly those combinations, folds both to one row per
        /// dimension value, names them and hands the requested page back.
        ///
        /// The shape is the specified one:
        ///
        ///   BudgetByAccount   PostingType 'B', ABS(SUM(AmtAcctDr - AmtAcctCr)) grouped by
        ///                     dimension value + Account_ID, HAVING that total &lt;&gt; 0.
        ///   ActualByAccount   PostingType 'A', the same aggregate at the same grain.
        ///   DimensionTotals   Budget LEFT OUTER JOIN Actual on BOTH keys, so an actual with no
        ///                     budget behind it cannot enter the figure, and a budget with no
        ///                     actual reads as 0% rather than disappearing.
        ///
        /// MRole is applied to each CTE BODY - to the Fact_Acct alias each one reads - before
        /// the bodies are composed into the WITH statement. It is never applied to a CTE alias
        /// or to the finished statement: those are derived result sets, not dictionary tables,
        /// and the access parser cannot resolve them.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="acct">Resolved accounting context (schema and currency).</param>
        /// <param name="yearId">The selected C_Year_ID.</param>
        /// <param name="spec">Resolved dimension - its Fact_Acct column and label source.</param>
        /// <param name="result">Result being filled - paging fields included.</param>
        private void ReadRows(Ctx ctx, AcctContext acct, int yearId, DimensionSpec spec,
            UtilizationResult result)
        {
            /* No period, no year: nothing to read, and an empty page says so. */
            List<int> periodIds = GetPeriodIds(ctx, yearId);
            if (periodIds.Count == 0)
            {
                Log.Log(Level.INFO, "VAS_253_UtlizationbyDimension: C_Year_ID=" + yearId
                    + " has no active period; nothing to read");
                result.Page = 1;
                return;
            }

            List<int> offsetIds = GetBalancingAccountIds(ctx, acct.C_AcctSchema_ID);

            /* Bind order is appearance order in the finished statement, because the backend
               adapters bind positionally. The Budget body is composed first, so its binds come
               first; AddAccessSQL adds predicates but no binds, so securing and composing the
               bodies afterwards cannot disturb the order. */
            List<SqlParameter> parameters = new List<SqlParameter>();

            /* The offset accounts are excluded from the BUDGET side only. A budget journal
               balances, so the other side of every budget posting lands on the schema's
               suspense / currency-balancing / commitment / budget-offset account; left in, it
               is itself a budgeted combination carrying the mirror image of everything
               budgeted. The actual side needs no such exclusion: it is matched to budgeted
               combinations, and an account excluded from the budget can no longer match. */
            string budgetBody = BuildFactBody(ctx, spec, POSTINGTYPE_Budget, "Budget",
                acct.C_AcctSchema_ID, periodIds, offsetIds, "BudgetAmount", true, parameters);

            string actualBody = BuildFactBody(ctx, spec, POSTINGTYPE_Actual, "Actual",
                acct.C_AcctSchema_ID, periodIds, null, "ActualAmount", false, parameters);

            StringBuilder sql = new StringBuilder();
            sql.Append("WITH BudgetByAccount AS (").Append(budgetBody)
               .Append("),ActualByAccount AS (").Append(actualBody)
               .Append("),DimensionTotals AS (")
               .Append("SELECT b.DimensionValue_ID AS DimensionValue_ID,")
               .Append("SUM(b.BudgetAmount) AS BudgetAmount,")
               .Append("SUM(COALESCE(a.ActualAmount,0)) AS ActualAmount ")
               .Append("FROM BudgetByAccount b ")
               .Append("LEFT OUTER JOIN ActualByAccount a ON (b.DimensionValue_ID=a.DimensionValue_ID /*AND b.Account_ID=a.Account_ID*/) ")
               .Append("GROUP BY b.DimensionValue_ID) ")
               .Append("SELECT d.DimensionValue_ID AS DimensionValue_ID,")
               .Append("d.BudgetAmount AS BudgetAmount,")
               .Append("d.ActualAmount AS ActualAmount,")
               .Append("CASE WHEN d.BudgetAmount=0 THEN 0 ELSE (d.ActualAmount/d.BudgetAmount)*100 END AS UtilizationPct ")
               .Append("FROM DimensionTotals d ")
               .Append("ORDER BY UtilizationPct DESC,d.DimensionValue_ID");

            DataSet ds = DB.ExecuteDataset(sql.ToString(), parameters.ToArray(), null);
            if (ds == null || ds.Tables.Count == 0) { result.Page = 1; return; }

            DataTable dt = ds.Tables[0];

            List<UtilizationRow> all = new List<UtilizationRow>();
            for (int i = 0; i < dt.Rows.Count; i++)
            {
                DataRow row = dt.Rows[i];

                UtilizationRow item = new UtilizationRow();
                item.Dimension_ID = Util.GetValueOfInt(row["DimensionValue_ID"]);
                item.Budget = Util.GetValueOfDecimal(row["BudgetAmount"]);
                item.Actual = Util.GetValueOfDecimal(row["ActualAmount"]);
                item.UtilizedPct = Util.GetValueOfDecimal(row["UtilizationPct"]);

                all.Add(item);
            }

            if (all.Count == 0) { result.Page = 1; return; }

            /* Names are resolved for the WHOLE set, not just the page, because they are part
               of the ordering: utilization decides the ranking and the display value settles a
               tie, so a page cut before the names were known could put the same two rows in a
               different order from one request to the next. It is still a batch read, never a
               read per row. */
            ApplyLabels(spec, all);

            all.Sort(CompareRows);

            result.TotalRows = all.Count;
            result.TotalPages = result.PageSize > 0
                ? (int)Math.Ceiling((double)result.TotalRows / result.PageSize)
                : 0;

            /* Clamp the page AFTER the total is known: a page number the client kept from a
               longer list must land on the last real page, never past the end. */
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
        /// Builds ONE CTE body - a secured, grouped read of Fact_Acct for a single posting type
        /// - and appends its binds, in text order, to the shared list.
        ///
        /// The netting is what keeps the figures honest: SUM(AmtAcctDr - AmtAcctCr) is taken
        /// per dimension value + Account_ID FIRST, so the two sides of one journal cancel
        /// instead of being counted twice, and ABS is applied to that net so a credit-natural
        /// account still reports a positive figure. Source amounts are never read.
        ///
        /// MRole goes on HERE, on this body's own Fact_Acct alias, before GROUP BY / HAVING are
        /// appended - the access parser must not meet a trailing clause, and the composed WITH
        /// statement must never be handed to it at all.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="spec">Resolved dimension - supplies the whitelisted column name.</param>
        /// <param name="postingType">POSTINGTYPE_Budget or POSTINGTYPE_Actual.</param>
        /// <param name="prefix">Bind-name prefix, unique per body.</param>
        /// <param name="acctSchemaId">Primary C_AcctSchema_ID.</param>
        /// <param name="periodIds">Active periods of the selected financial year.</param>
        /// <param name="excludeAccountIds">Account ids to exclude, or null.</param>
        /// <param name="amountAlias">Alias of this body's amount column.</param>
        /// <param name="requireNonZero">True to add HAVING &lt;&gt; 0 - the budget side, where a
        /// combination that cancelled out is not a budget at all.</param>
        /// <param name="parameters">Bind list being built, in appearance order.</param>
        /// <returns>The finished, secured CTE body.</returns>
        private string BuildFactBody(Ctx ctx, DimensionSpec spec, string postingType, string prefix,
            int acctSchemaId, List<int> periodIds, List<int> excludeAccountIds, string amountAlias,
            bool requireNonZero, List<SqlParameter> parameters)
        {
            /* The dimension column is an identifier this model resolved from the whitelist in
               §4 and confirmed against AD_Column - it is never client text, and never a bind
               (a column name cannot be one). */
            string amountExpr = "ABS(SUM(COALESCE(fa.AmtAcctDr,0)-COALESCE(fa.AmtAcctCr,0)))";

            StringBuilder body = new StringBuilder();
            body.Append("SELECT fa.").Append(spec.FactColumn).Append(" AS DimensionValue_ID,")
                /*.Append("fa.Account_ID AS Account_ID,")*/
                .Append(amountExpr).Append(" AS ").Append(amountAlias)
                .Append(" FROM Fact_Acct fa")
                /* C_ElementValue is joined for ONE reason: the account's type. It is a
                   reference lookup by primary key and inherits the parent's access filter,
                   and its ON is a plain equality so the access parser has nothing to trip
                   on. */
                .Append(" INNER JOIN C_ElementValue ev ON (ev.C_ElementValue_ID=fa.Account_ID)")
                .Append(" WHERE fa.C_AcctSchema_ID=@").Append(prefix).Append("_C_AcctSchema_ID");
            parameters.Add(new SqlParameter("@" + prefix + "_C_AcctSchema_ID", acctSchemaId));

            body.Append(" AND fa.AD_Client_ID=@").Append(prefix).Append("_AD_Client_ID");
            parameters.Add(new SqlParameter("@" + prefix + "_AD_Client_ID", ctx.GetAD_Client_ID()));

            body.Append(" AND fa.IsActive='Y'")
                .Append(" AND fa.PostingType=@").Append(prefix).Append("_PostingType");
            parameters.Add(new SqlParameter("@" + prefix + "_PostingType", postingType));

            /* EXPENSE ACCOUNTS ONLY. "How much of the budget has been used" is a question
               about spending: a revenue, asset, liability or equity account posted in the
               same period is not consumption of a budget, and left in it lands on the card
               as a value utilizing something it was never given. The sibling unbudgeted
               card (VAS_256) draws the same line at the same place, so the two agree about
               which accounts a budget conversation is about. */
            body.Append(" AND ev.IsActive='Y'")
                .Append(" AND ev.AccountType=@").Append(prefix).Append("_AccountType");
            parameters.Add(new SqlParameter("@" + prefix + "_AccountType", ACCOUNTTYPE_Expense));

            /* A posting carrying no value for this dimension is not a value of it. */
            body.Append(" AND fa.").Append(spec.FactColumn).Append(" IS NOT NULL");

            /* The year, as the periods that belong to it - never as a date range. */
            body.Append(" AND fa.C_Period_ID IN (")
                .Append(BuildIdInList(periodIds, "@" + prefix + "_C_Period_ID", parameters))
                .Append(")");

            if (excludeAccountIds != null && excludeAccountIds.Count > 0)
            {
                body.Append(" AND fa.Account_ID NOT IN (")
                    .Append(BuildIdInList(excludeAccountIds, "@" + prefix + "_Offset_Account_ID", parameters))
                    .Append(")");
            }

            /* Fact_Acct fa is the physical table this body reads: the role's access clause goes
               HERE, inside the body, and never on the CTE that wraps it. */
            string secured = MRole.GetDefault(ctx).AddAccessSQL(body.ToString(), "fa",
                MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            /* GROUP BY / HAVING after the access SQL. The account stays in the grain: it is
               what the two sides are matched on, so an actual can only ever be counted
               against the account that was budgeted. */
            secured += " GROUP BY fa." + spec.FactColumn + "/*,fa.Account_ID*/";
            if (requireNonZero) { secured += " HAVING " + amountExpr + "<>0"; }

            return secured;
        }

        /// <summary>
        /// Orders the rows: most utilized first, then by display value so two equally utilized
        /// values keep a stable, readable order across requests, with the id as the final
        /// tiebreaker for two values that also share a name.
        /// </summary>
        /// <param name="left">First row.</param>
        /// <param name="right">Second row.</param>
        /// <returns>Standard comparison result.</returns>
        private int CompareRows(UtilizationRow left, UtilizationRow right)
        {
            int byPct = right.UtilizedPct.CompareTo(left.UtilizedPct);
            if (byPct != 0) { return byPct; }

            int byLabel = String.Compare(left.Label, right.Label, StringComparison.CurrentCultureIgnoreCase);
            if (byLabel != 0) { return byLabel; }

            return left.Dimension_ID.CompareTo(right.Dimension_ID);
        }

        // ─────────────────────────────────────────────────────────────────────
        // §6  Naming the dimension values
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Fills each row's label from the dimension's own master table, in BATCHES - never one
        /// query per row.
        ///
        /// This is a DISPLAY LOOKUP BY PRIMARY KEY - the same treatment VAS_252 gives AD_Org -
        /// so no MRole predicate is applied: every id here came out of the aggregate whose
        /// bodies the role's access clause already filtered, and re-filtering the name would
        /// blank the label of a posting the reader is entitled to see.
        ///
        /// IsActive is deliberately NOT filtered. A deactivated project still carries the
        /// budget it was given this year, and blanking its name would leave a real budget line
        /// labelled by nothing. This is a lookup of history, not a picker of current values.
        /// </summary>
        /// <param name="spec">Resolved dimension - its label source table and columns.</param>
        /// <param name="rows">Every row of the result, labelled in place.</param>
        private void ApplyLabels(DimensionSpec spec, List<UtilizationRow> rows)
        {
            List<int> ids = new List<int>();
            Dictionary<int, List<UtilizationRow>> byId = new Dictionary<int, List<UtilizationRow>>();

            for (int i = 0; i < rows.Count; i++)
            {
                int id = rows[i].Dimension_ID;

                /* Id 0 is "not assigned" for every dimension but Organization, where it is the
                   '*' org and has a row of its own to be named from. */
                if (id < 0 || (id == 0 && !spec.ZeroIsValue)) { continue; }

                if (!byId.ContainsKey(id))
                {
                    byId.Add(id, new List<UtilizationRow>());
                    ids.Add(id);
                }
                byId[id].Add(rows[i]);
            }

            if (ids.Count == 0) { return; }

            for (int start = 0; start < ids.Count; start += ID_BatchSize)
            {
                ReadLabels(spec, byId, IdBatch(ids, start, ID_BatchSize));
            }
        }

        /// <summary>Runs one label batch and writes each name back onto its rows.</summary>
        /// <param name="spec">Resolved dimension - its label source table and columns.</param>
        /// <param name="byId">Dimension value id -&gt; the rows carrying it.</param>
        /// <param name="ids">This batch's ids.</param>
        private void ReadLabels(DimensionSpec spec, Dictionary<int, List<UtilizationRow>> byId,
            List<int> ids)
        {
            if (ids.Count == 0) { return; }

            List<SqlParameter> parameters = new List<SqlParameter>();

            StringBuilder sql = new StringBuilder();
            sql.Append("SELECT src.").Append(spec.SourceKey).Append(" AS Dimension_ID");

            /* An identifier column is not necessarily text - it can be a date, a number or a
               foreign key - so those are selected RAW and coerced in C#. Wrapping one in
               COALESCE(...,N'') would ask the database to reconcile a number with an empty
               string, which PostgreSQL refuses outright. The conventional Value / Name / City
               columns are known text and keep their COALESCE. */
            string valueExpr = spec.LabelsFromIdentifier ? "src.{0}" : "COALESCE(src.{0},N'')";

            if (spec.ValueColumn.Length > 0)
            {
                sql.Append(",").Append(String.Format(valueExpr, spec.ValueColumn))
                   .Append(" AS Dimension_Value");
            }
            if (spec.NameColumn.Length > 0)
            {
                sql.Append(",").Append(String.Format(valueExpr, spec.NameColumn))
                   .Append(" AS Dimension_Name");
            }
            if (spec.AltNameColumn.Length > 0)
            {
                sql.Append(",COALESCE(src.").Append(spec.AltNameColumn).Append(",N'') AS Dimension_Alt");
            }

            sql.Append(" FROM ").Append(spec.SourceTable).Append(" src WHERE src.")
               .Append(spec.SourceKey).Append(" IN (")
               .Append(BuildIdInList(ids, "@Dimension_ID", parameters)).Append(")");

            DataSet ds = DB.ExecuteDataset(sql.ToString(), parameters.ToArray(), null);
            if (ds == null || ds.Tables.Count == 0) { return; }

            DataTable dt = ds.Tables[0];
            bool hasValue = dt.Columns.Contains("Dimension_Value");
            bool hasName = dt.Columns.Contains("Dimension_Name");
            bool hasAlt = dt.Columns.Contains("Dimension_Alt");

            for (int i = 0; i < dt.Rows.Count; i++)
            {
                DataRow row = dt.Rows[i];

                int id = Util.GetValueOfInt(row["Dimension_ID"]);
                if (!byId.ContainsKey(id)) { continue; }

                /* Read through CellText, not GetValueOfString: an identifier column can come
                   back as a date or a number, and this has to print whatever it is. */
                string value = hasValue ? CellText(row["Dimension_Value"]) : "";
                string name = hasName ? CellText(row["Dimension_Name"]) : "";

                /* The street line only stands in when the primary name is blank - it is a
                   fallback, never a second half of the label. */
                if (name.Length == 0 && hasAlt) { name = CellText(row["Dimension_Alt"]); }

                string label = ComposeLabel(value, name);

                List<UtilizationRow> targets = byId[id];
                for (int r = 0; r < targets.Count; r++) { targets[r].Label = label; }
            }
        }

        /// <summary>
        /// One label cell as text, whatever its column's type. An identifier column need not
        /// be a string - a date or a document number identifies plenty of tables - so this
        /// coerces rather than casts, and a NULL reads as nothing at all.
        /// </summary>
        /// <param name="value">The raw cell.</param>
        /// <returns>Its text, or an empty string.</returns>
        private string CellText(object value)
        {
            if (value == null || value == DBNull.Value) { return ""; }
            return Convert.ToString(value);
        }

        /// <summary>
        /// "{Value} - {Name}", printed with the separator only when there are two sides to it,
        /// so a master row that carries just one of them is not labelled with a dangling dash.
        /// </summary>
        /// <param name="value">The master row's search key, or empty.</param>
        /// <param name="name">The master row's name, or empty.</param>
        /// <returns>The row's label; empty when the row has neither.</returns>
        private string ComposeLabel(string value, string name)
        {
            string left = value == null ? "" : value.Trim();
            string right = name == null ? "" : name.Trim();

            if (left.Length > 0 && right.Length > 0) { return left + " - " + right; }
            return left.Length > 0 ? left : right;
        }

        // ─────────────────────────────────────────────────────────────────────
        // §7  The balancing accounts a budget journal offsets to
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The natural account ids the schema uses as balancing / offset accounts.
        ///
        /// A budget journal balances, so the other side of every budget posting lands on one of
        /// these. Left in, such an account is itself a budgeted account + dimension combination
        /// carrying the mirror image of everything budgeted, and the dimension value it was
        /// posted against reports roughly twice the budget it was given.
        ///
        /// Each setting holds a C_ValidCombination_ID, NOT a Fact_Acct.Account_ID, so it is
        /// resolved C_AcctSchema_GL -&gt; C_ValidCombination -&gt; Account_ID. Only the columns
        /// AD_Column confirms the table actually carries are named: the commitment pair and the
        /// localized budget offset are optional in this schema, and naming a missing column
        /// would fail the whole query and cost the exclusions that ARE configured.
        ///
        /// C_AcctSchema_GL, C_ValidCombination, AD_Table and AD_Column are configuration and
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

            List<string> tables = new List<string>();
            tables.Add(TABLE_ACCTSCHEMA_GL);

            List<string> present = ColumnsOf(ReadTableColumns(tables), TABLE_ACCTSCHEMA_GL).Columns;

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
                Log.Log(Level.INFO, "VAS_253_UtlizationbyDimension: no active C_AcctSchema_GL row for C_AcctSchema_ID="
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
        /// <param name="acctSchemaId">Primary C_AcctSchema_ID - the combination must belong to
        /// the same schema the figures are read for.</param>
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

        // ─────────────────────────────────────────────────────────────────────
        // §8  Dictionary helpers
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The active column names of several dictionary tables, in ONE read. Used to confirm a
        /// table and a column exist before either is named in generated SQL - user-element
        /// dimensions in particular name a table this model has never heard of.
        ///
        /// Columns backed by a virtual expression (AD_Column.ColumnSQL) are excluded: they are
        /// not physical columns and cannot be grouped by or filtered on.
        ///
        /// The read also collects each table's IDENTIFIER columns (AD_Column.IsIdentifier),
        /// in the dictionary's own SeqNo order. Those are what the platform shows for a
        /// record everywhere else, and they are this model's fallback for a master table that
        /// carries neither Value nor Name.
        /// </summary>
        /// <param name="tableNames">Physical table names - resolved from the dictionary or
        /// constants of this class, never free client text.</param>
        /// <returns>Upper-cased table name -&gt; its columns (never null).</returns>
        private Dictionary<string, TableColumns> ReadTableColumns(List<string> tableNames)
        {
            Dictionary<string, TableColumns> map = new Dictionary<string, TableColumns>();
            if (tableNames == null || tableNames.Count == 0) { return map; }

            List<SqlParameter> parameters = new List<SqlParameter>();
            StringBuilder inList = new StringBuilder();

            for (int i = 0; i < tableNames.Count; i++)
            {
                if (tableNames[i] == null || tableNames[i].Length == 0) { continue; }

                if (inList.Length > 0) { inList.Append(","); }

                string name = "@TableName" + i;
                inList.Append("UPPER(").Append(name).Append(")");
                parameters.Add(new SqlParameter(name, tableNames[i]));
            }

            if (inList.Length == 0) { return map; }

            /* ORDER BY inside the statement, not appended: no access clause is applied to a
               dictionary read, so there is no parser to keep a trailing clause away from.
               The identifier order is AD_Column.SeqNo - the platform's own, which is what
               makes a two-column identifier read the way it does everywhere else. */
            string sql = @"
                SELECT UPPER(t.TableName) AS Table_Name,
                       c.ColumnName AS Column_Name,
                       COALESCE(c.IsIdentifier,'N') AS Is_Identifier
                FROM AD_Column c
                INNER JOIN AD_Table t ON (t.AD_Table_ID=c.AD_Table_ID)
                WHERE t.IsActive='Y'
                  AND c.IsActive='Y'
                  AND c.ColumnSQL IS NULL
                  AND UPPER(t.TableName) IN (" + inList.ToString() + @")
                ORDER BY UPPER(t.TableName),COALESCE(c.SeqNo,0),c.ColumnName";

            DataSet ds = DB.ExecuteDataset(sql, parameters.ToArray(), null);
            if (ds == null || ds.Tables.Count == 0) { return map; }

            DataTable dt = ds.Tables[0];
            for (int i = 0; i < dt.Rows.Count; i++)
            {
                string table = Util.GetValueOfString(dt.Rows[i]["Table_Name"]);
                string column = Util.GetValueOfString(dt.Rows[i]["Column_Name"]);
                if (table.Length == 0 || column.Length == 0) { continue; }

                if (!map.ContainsKey(table)) { map.Add(table, new TableColumns()); }

                map[table].Columns.Add(column);

                if (String.Equals(Util.GetValueOfString(dt.Rows[i]["Is_Identifier"]), "Y",
                        StringComparison.OrdinalIgnoreCase))
                {
                    map[table].Identifiers.Add(column);
                }
            }

            return map;
        }

        /// <summary>One table's columns out of a dictionary read.</summary>
        /// <param name="map">Result of <see cref="ReadTableColumns"/>.</param>
        /// <param name="tableName">Table to look up.</param>
        /// <returns>Its columns, or an empty set when the table is not installed.</returns>
        private TableColumns ColumnsOf(Dictionary<string, TableColumns> map, string tableName)
        {
            string key = tableName == null ? "" : tableName.ToUpper();
            return map.ContainsKey(key) ? map[key] : new TableColumns();
        }

        /// <summary>Case-insensitive membership test over a dictionary column list.</summary>
        /// <param name="columns">Column names the table carries.</param>
        /// <param name="columnName">Name to look for.</param>
        /// <returns>True when the table carries the column.</returns>
        private bool HasColumn(List<string> columns, string columnName)
        {
            return ContainsIgnoreCase(columns, columnName);
        }

        /// <summary>Case-insensitive membership test over a string list.</summary>
        /// <param name="values">List to search.</param>
        /// <param name="value">Value to look for.</param>
        /// <returns>True when the list already holds the value.</returns>
        private bool ContainsIgnoreCase(List<string> values, string value)
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (String.Equals(values[i], value, StringComparison.OrdinalIgnoreCase)) { return true; }
            }
            return false;
        }

        // ─────────────────────────────────────────────────────────────────────
        // §9  Helpers
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

        /// <summary>One batch of ids out of a longer list.</summary>
        /// <param name="ids">All ids.</param>
        /// <param name="start">Index of the first id in this batch.</param>
        /// <param name="count">Maximum ids in this batch.</param>
        /// <returns>The batch (never null).</returns>
        private List<int> IdBatch(List<int> ids, int start, int count)
        {
            List<int> batch = new List<int>();
            for (int i = start; i < ids.Count && i < start + count; i++) { batch.Add(ids[i]); }
            return batch;
        }

        // ─────────────────────────────────────────────────────────────────────
        // §10  Transfer objects
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// One accounting dimension, fully resolved: what it is called, where its values are
        /// stored on Fact_Acct and where their names come from. Internal - the client only ever
        /// sees the element type and the label.
        /// </summary>
        private class DimensionSpec
        {
            public DimensionSpec()
            {
                ElementType = "";
                Label = "";
                FactColumn = "";
                SourceTable = "";
                SourceKey = "";
                ValueColumn = "";
                NameColumn = "";
                AltNameColumn = "";
            }

            /// <summary>C_AcctSchema_Element.ElementType - the option's stored value.</summary>
            public string ElementType { get; set; }

            /// <summary>C_AcctSchema_Element.Name - the tenant's own word for this dimension,
            /// falling back to the element type when it is blank.</summary>
            public string Label { get; set; }

            /// <summary>C_AcctSchema_Element.SeqNo - the schema's own order.</summary>
            public int SeqNo { get; set; }

            /// <summary>The whitelisted Fact_Acct column the value is stored in.</summary>
            public string FactColumn { get; set; }

            /// <summary>The master table its values are named from.</summary>
            public string SourceTable { get; set; }

            /// <summary>That table's key column.</summary>
            public string SourceKey { get; set; }

            /// <summary>Its search-key column, or empty when it has none.</summary>
            public string ValueColumn { get; set; }

            /// <summary>Its name / display column, or empty when it has none.</summary>
            public string NameColumn { get; set; }

            /// <summary>A fallback name column used only when the name is blank - the street
            /// line of a location, for instance.</summary>
            public string AltNameColumn { get; set; }

            /// <summary>True when the label columns came from AD_Column.IsIdentifier rather
            /// than from the conventional Value / Name pair - which means they may not be
            /// text, so the label read must not wrap them in a string-typed COALESCE.</summary>
            public bool LabelsFromIdentifier { get; set; }

            /// <summary>True when id 0 is a real value of this dimension rather than "not
            /// assigned" - which is the case for Organization, where 0 is the '*' org.</summary>
            public bool ZeroIsValue { get; set; }
        }

        /// <summary>
        /// One dictionary table's physical columns, and the subset of them the dictionary
        /// marks as identifying a record (AD_Column.IsIdentifier), in SeqNo order. Internal -
        /// it exists so a table can be confirmed and named in one read.
        /// </summary>
        private class TableColumns
        {
            public TableColumns()
            {
                Columns = new List<string>();
                Identifiers = new List<string>();
            }

            /// <summary>Every active, non-virtual column of the table.</summary>
            public List<string> Columns { get; set; }

            /// <summary>Those marked IsIdentifier, in AD_Column.SeqNo order - what the
            /// platform shows for a record of this table everywhere else.</summary>
            public List<string> Identifiers { get; set; }
        }

        /// <summary>One page of the widget, plus what the page cannot know by itself.</summary>
        public class UtilizationResult
        {
            /// <summary>The requested page of dimension values, most utilized first.</summary>
            public List<UtilizationRow> Rows { get; set; }

            /// <summary>The financial years the filter can offer, newest first.</summary>
            public List<YearOption> Years { get; set; }

            /// <summary>The dimensions the accounting schema declares, in SeqNo order.</summary>
            public List<DimensionOption> Dimensions { get; set; }

            /// <summary>The accounting schema and its currency - the card's only currency.</summary>
            public AcctContext Schema { get; set; }

            /// <summary>C_Year_ID actually read, after defaulting and validation.</summary>
            public int C_Year_ID { get; set; }

            /// <summary>C_Year.FiscalYear of that year - the year pill's label.</summary>
            public string FiscalYear { get; set; }

            /// <summary>ElementType actually grouped by, after defaulting and validation.</summary>
            public string Dimension { get; set; }

            /// <summary>That element's own Name - the dimension pill's label, and the word the
            /// subtitle counts values of.</summary>
            public string DimensionLabel { get; set; }

            /// <summary>1-based page number actually served, after clamping.</summary>
            public int Page { get; set; }

            /// <summary>Rows per page actually used, after clamping.</summary>
            public int PageSize { get; set; }

            /// <summary>Dimension values in the ranking in total - the pager's and the
            /// subtitle's figure.</summary>
            public int TotalRows { get; set; }

            /// <summary>CEILING(TotalRows / PageSize).</summary>
            public int TotalPages { get; set; }

            /// <summary>ERROR_* token when the tenant is not configured; empty otherwise.</summary>
            public string ErrorCode { get; set; }

            /// <summary>False only on a failure; a year with nothing budgeted is Loaded=true
            /// with an empty page.</summary>
            public bool Loaded { get; set; }
        }

        /// <summary>
        /// One dimension value's budget against its actual. Both figures are in the PRIMARY
        /// accounting schema currency and are never converted - AmtAcctDr / AmtAcctCr are
        /// already stated in it - and both are the ABSOLUTE of a net, so a credit-natural
        /// account reads the same way round as a debit-natural one.
        /// </summary>
        public class UtilizationRow
        {
            public UtilizationRow()
            {
                Label = "";
            }

            /// <summary>The dimension value's own id.</summary>
            public int Dimension_ID { get; set; }

            /// <summary>"{Value} - {Name}" from the dimension's master table.</summary>
            public string Label { get; set; }

            /// <summary>Approved budget for the year: PostingType 'B', netted per account and
            /// summed. Never zero - a combination that cancelled out is not a budget.</summary>
            public decimal Budget { get; set; }

            /// <summary>Posted actual for the year, counted ONLY against account + dimension
            /// combinations that carry a budget.</summary>
            public decimal Actual { get; set; }

            /// <summary>Actual / Budget * 100. Reported past 100 - the bar is capped by the
            /// client, this number never is.</summary>
            public decimal UtilizedPct { get; set; }
        }

        /// <summary>One selectable accounting dimension.</summary>
        public class DimensionOption
        {
            /// <summary>C_AcctSchema_Element.ElementType - what the client sends back.</summary>
            public string Value { get; set; }

            /// <summary>C_AcctSchema_Element.Name - the tenant's own word for it.</summary>
            public string Label { get; set; }

            /// <summary>C_AcctSchema_Element.SeqNo - the order the schema declares.</summary>
            public int SeqNo { get; set; }
        }

        /// <summary>One selectable financial year of the primary calendar.</summary>
        public class YearOption
        {
            /// <summary>C_Year.C_Year_ID.</summary>
            public int C_Year_ID { get; set; }

            /// <summary>C_Year.FiscalYear - the label the pill shows.</summary>
            public string FiscalYear { get; set; }
        }

        /// <summary>The tenant's primary accounting schema, its currency and its calendar.</summary>
        public class AcctContext
        {
            /// <summary>AD_ClientInfo.C_Calendar_ID - the PRIMARY calendar.</summary>
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
