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
    /// Purpose     : Backs the VAS_253_UtlizationbyDimensionWidget dashboard widget - how
    ///               much of the approved budget each value of ONE accounting dimension has
    ///               actually consumed:
    ///
    ///                 Budget    Fact_Acct rows with PostingType 'B', summed per dimension
    ///                           value.
    ///                 Actual    Fact_Acct rows with PostingType 'A', summed the same way.
    ///                 Utilized  Actual / Budget * 100. Reported past 100% - the bar is
    ///                           capped by the client, the number never is.
    ///
    ///               THE DIMENSION IS CONFIGURATION, NOT CODE. The selector is built from
    ///               the ACTIVE C_AcctSchema_Element rows of the tenant's PRIMARY accounting
    ///               schema, in SeqNo order, and each option is labelled with that element's
    ///               OWN Name. A tenant that represents departments as Activity, or renames
    ///               "User List 1" to "Cost centre", gets its own words with no change here.
    ///               Nothing in this model, and no word in the widget, names a dimension.
    ///
    ///               EVERY ELEMENT THE SCHEMA DECLARES IS OFFERED - not only the ones that
    ///               happen to carry budget postings today. An element that is configured
    ///               and active is a dimension the tenant budgets by, and a year in which
    ///               nothing was posted against it is an empty list, not a missing option.
    ///               An element is dropped ONLY when its Fact_Acct column or its label table
    ///               cannot be confirmed against the AD dictionary, which is logged.
    ///
    ///               BOTH SIDES COME FROM Fact_Acct, one scan, two posting types, as a flat
    ///               SUM(CASE WHEN ...) per side. Two derived sets joined together would
    ///               read Fact_Acct twice and would have to be FULL OUTER joined to keep a
    ///               dimension value that has a budget and no actual - which is 0% utilized
    ///               and belongs on the card - and this codebase's access-SQL parser is kept
    ///               away from nested selects on principle.
    ///
    ///               THE SIGN IS CORRECTED PER ACCOUNT TYPE. Fact_Acct stores debits and
    ///               credits, not "amounts": revenue, liability and owner's equity are
    ///               CREDIT-natural and are read as AmtAcctCr - AmtAcctDr, while asset,
    ///               expense and memo are DEBIT-natural and are read as AmtAcctDr -
    ///               AmtAcctCr. Without it a revenue budget reports as a negative and its
    ///               whole dimension value drops out of the card. The aggregate therefore
    ///               groups by the dimension AND by C_ElementValue.AccountType, and the
    ///               correction is applied in C# before the two are folded together - so the
    ///               SQL stays one flat aggregate and the access parser meets nothing nested.
    ///
    ///               A ROW NEEDS A BUDGET. Utilization of a budget that does not exist is
    ///               not a number, so a dimension value with no positive budget is left out
    ///               rather than shown as 0% or as an infinite bar. Actuals posted with no
    ///               budget behind them are the sibling card's subject (VAS_256), not this
    ///               one's.
    ///
    ///               THE UNASSIGNED VALUE IS REPORTED, NOT HIDDEN. A budget posted without a
    ///               project (or without whichever dimension is selected) is real money, and
    ///               dropping it would make the card's own figures disagree with the ledger.
    ///               It is grouped under id 0 and the client names it from AD_Message. The
    ///               one exception is Organization, where id 0 is the '*' organization and
    ///               AD_Org names it like any other.
    ///
    ///               PRIMARY ACCOUNTING SCHEMA ONLY, on both sides. A budget posted in a
    ///               secondary schema must not be compared against an actual in the primary
    ///               one, so the schema is an equality on the single scan that produces both
    ///               figures. AmtAcct* is already stated in that schema's currency, so there
    ///               is no currencyConvert call anywhere in this model.
    ///
    ///               FINANCIAL YEAR. AD_ClientInfo.C_Calendar_ID -&gt; C_Year -&gt; C_Period.
    ///               The accounting date window is MIN(StartDate) / MAX(EndDate) over the
    ///               ACTIVE periods of the selected C_Year_ID - never January to December,
    ///               and never derived from the calendar month.
    ///
    ///               BALANCING ACCOUNTS ARE EXCLUDED. A budget journal is balanced, so the
    ///               offsetting side lands on a technical account (the schema's suspense
    ///               balancing, currency balancing, commitment offset or budget offset
    ///               account). Left in, that account carries the mirror image of every
    ///               budget posted and inflates whichever dimension value it was posted
    ///               against. The excluded ids are resolved from C_AcctSchema_GL through
    ///               C_ValidCombination, and each column is confirmed against AD_Column
    ///               before it is named - several are optional in this schema.
    ///
    ///               MRole row-level security is applied to Fact_Acct fa on the aggregate
    ///               and to C_Year y on the year list. The joined C_ElementValue rows are a
    ///               reference lookup and inherit the parent's filter; the dimension's own
    ///               master table is read as a display lookup BY PRIMARY KEY over ids the
    ///               secured aggregate already returned; AD_ClientInfo, C_AcctSchema,
    ///               C_AcctSchema_Element, C_AcctSchema_GL, C_ValidCombination, AD_Table and
    ///               AD_Column are configuration and dictionary reads. GROUP BY / ORDER BY
    ///               are appended AFTER AddAccessSQL so its FROM-clause parser never meets a
    ///               trailing clause, and every join ON is a plain equality so it never
    ///               meets a function call either. Compatible with PostgreSQL and Oracle.
    /// Chronological development:
    ///   VAI154      2026-09-09 Created
    /// </summary>
    public class VAS_253_UtlizationbyDimensionModel
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_253_UtlizationbyDimensionModel).FullName);

        /* Fact_Acct.PostingType stored codes. Stored codes are compared bare - never with an
           N prefix, which would force a Unicode comparison against a VARCHAR column. */
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
        public const string ERROR_NO_DIMENSION = "NODIMENSION";

        /* Paging. The 4x2 cell fits four utilization bars at 1280px; a taller cell may ask
           for more and a short one for fewer, but never outside these bounds. */
        public const int DEFAULT_PageSize = 4;
        private const int MIN_PageSize = 1;
        private const int MAX_PageSize = 12;

        /* The physical fact table, named once. Its columns are read from AD_Column so a
           dimension whose column this installation does not carry is dropped rather than
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
        /// the accounting-schema currency and the requested page of dimension values ranked
        /// by utilization.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="yearId">C_Year_ID the user selected, or 0 to default to the financial
        /// year containing today.</param>
        /// <param name="dimension">C_AcctSchema_Element.ElementType the user selected, or
        /// empty to default to the schema's first element in SeqNo order.</param>
        /// <param name="pageNo">1-based page; clamped to the available range.</param>
        /// <param name="pageSize">Rows per page; clamped to [1,12].</param>
        /// <returns>Populated <see cref="UtilizationResult"/> (never null). Loaded is false
        /// only when there is no context or the tenant is not configured; a year with nothing
        /// budgeted returns Loaded=true and an empty page, because "nothing budgeted against
        /// this dimension" is a real answer rather than an error.</returns>
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
               primary calendar there is no year list to build, and without a primary
               accounting schema there is neither a ledger to read nor an element list to
               offer. Neither is silently replaced by "some other" calendar or schema. */
            AcctContext acct = GetAcctContext(ctx);
            result.Schema = acct;

            if (acct.C_Calendar_ID <= 0)
            {
                result.ErrorCode = ERROR_NO_CALENDAR;
                Log.Log(Level.WARNING, "VAS_253_UtlizationbyDimension: AD_ClientInfo.C_Calendar_ID not configured for AD_Client_ID="
                    + ctx.GetAD_Client_ID());
                return result;
            }

            if (acct.C_AcctSchema_ID <= 0)
            {
                result.ErrorCode = ERROR_NO_ACCTSCHEMA;
                Log.Log(Level.WARNING, "VAS_253_UtlizationbyDimension: AD_ClientInfo.C_AcctSchema1_ID not configured for AD_Client_ID="
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

            YearOption year = PickYear(result.Years, yearId, DateTime.Now.Date);
            result.C_Year_ID = year.C_Year_ID;
            result.FiscalYear = year.FiscalYear;
            result.StartDate = ToIsoDate(year.StartDate);
            result.EndDate = ToIsoDate(year.EndDate);

            DimensionSpec spec = PickDimension(specs, dimension);
            result.Dimension = spec.ElementType;
            result.DimensionLabel = spec.Label;

            ReadRows(ctx, acct, year, spec, result);

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

            /* CurSymbol first, ISO_Code as the fallback - the card prints the symbol directly
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
        /// a January-to-December assumption. A year with no active period has no window at
        /// all and is therefore not offered: there would be no date range to read Fact_Acct
        /// with.
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
        // §3  The dimensions the accounting schema declares
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Every ACTIVE accounting dimension of the primary accounting schema, in SeqNo
        /// order, each resolved down to the Fact_Acct column it is stored in and the master
        /// table its values are named from.
        ///
        /// The list is the schema's own C_AcctSchema_Element rows and nothing else - this is
        /// the one place the set of dimensions is decided, and it is decided by
        /// configuration. Each option is labelled with the element's OWN Name, so a tenant
        /// that calls Activity "Department" sees Department.
        ///
        /// USER ELEMENTS ARE RESOLVED, NOT GUESSED. X1..X9 carry an AD_Column_ID naming the
        /// column the tenant hung on the dimension (say VAF_Department_ID); the master table
        /// is that column's name without the "_ID" suffix, which is the same rule the
        /// framework's own posting viewer applies. The table and its columns are then
        /// CONFIRMED against AD_Table / AD_Column before either is named in generated SQL.
        ///
        /// An element whose Fact_Acct column or label table cannot be confirmed is dropped
        /// and logged: a dimension that would fail the aggregate must not cost the tenant the
        /// dimensions that do work.
        ///
        /// C_AcctSchema_Element, AD_Table and AD_Column are configuration and dictionary
        /// reads, scoped by tenant and schema, so no MRole predicate is applied - the same
        /// treatment the sibling accounting widgets give this resolution chain. The ledger
        /// itself is secured where it is read, in §4.
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
                       COALESCE(col.ColumnName,N'') AS Element_Column
                FROM C_AcctSchema_Element ase
                LEFT OUTER JOIN AD_Column col ON (col.AD_Column_ID=ase.AD_Column_ID)
                WHERE ase.C_AcctSchema_ID=@C_AcctSchema_ID
                  AND ase.AD_Client_ID=@AD_Client_ID
                  AND ase.IsActive='Y'
                ORDER BY ase.SeqNo,ase.C_AcctSchema_Element_ID";

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@C_AcctSchema_ID", acctSchemaId),
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
            };

            DataSet ds = DB.ExecuteDataset(sql, parameters, null);
            if (ds == null || ds.Tables.Count == 0) { return specs; }

            DataTable dt = ds.Tables[0];

            /* Draft every element first, collect the tables all of them need, and confirm
               them in ONE dictionary read - a read per element would be a query inside a
               loop for no gain. */
            List<DimensionSpec> drafts = new List<DimensionSpec>();
            List<string> tables = new List<string>();
            tables.Add(TABLE_FACT_ACCT);

            for (int i = 0; i < dt.Rows.Count; i++)
            {
                DataRow row = dt.Rows[i];

                DimensionSpec draft = DraftDimension(
                    Util.GetValueOfString(row["Element_Type"]),
                    Util.GetValueOfString(row["Element_Name"]),
                    Util.GetValueOfInt(row["Seq_No"]),
                    Util.GetValueOfString(row["Element_Column"]));

                if (draft == null) { continue; }

                drafts.Add(draft);
                if (!ContainsIgnoreCase(tables, draft.SourceTable)) { tables.Add(draft.SourceTable); }
            }

            if (drafts.Count == 0) { return specs; }

            Dictionary<string, List<string>> dictionary = ReadTableColumns(tables);

            List<string> factColumns = ColumnsOf(dictionary, TABLE_FACT_ACCT);

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
        /// Maps one C_AcctSchema_Element row onto the Fact_Acct column it is stored in and
        /// the master table its values are named from. The mapping is the framework's own -
        /// MAcctSchemaElement.GetColumnName / GetValueQuery - restated here so the widget
        /// composes only names it decided itself.
        /// </summary>
        /// <param name="elementType">C_AcctSchema_Element.ElementType stored code.</param>
        /// <param name="elementName">C_AcctSchema_Element.Name - the option's label.</param>
        /// <param name="seqNo">C_AcctSchema_Element.SeqNo - the option's order.</param>
        /// <param name="elementColumn">AD_Column.ColumnName of the element's AD_Column_ID;
        /// only user elements carry one.</param>
        /// <returns>A draft spec, or null when the element type is not one this card can
        /// group by.</returns>
        private DimensionSpec DraftDimension(string elementType, string elementName, int seqNo,
            string elementColumn)
        {
            if (String.IsNullOrEmpty(elementType)) { return null; }

            DimensionSpec spec = new DimensionSpec();
            spec.ElementType = elementType;
            spec.Label = elementName;
            spec.SeqNo = seqNo;
            spec.ValueColumn = "Value";
            spec.NameColumn = "Name";

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

            /* User elements X1..X9. The tenant's own column names the master table, by the
               framework's own convention: VAF_Department_ID -> VAF_Department. Everything
               here is confirmed against the dictionary before it is used. */
            if (elementType.StartsWith(ELEMENTTYPE_UserElementPrefix, StringComparison.OrdinalIgnoreCase)
                && elementType.Length == 2)
            {
                int index = 0;
                if (!Int32.TryParse(elementType.Substring(1), out index)) { return null; }
                if (index < 1 || index > 9) { return null; }

                if (elementColumn.Length < 4
                    || !elementColumn.EndsWith("_ID", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                spec.FactColumn = "UserElement" + index + "_ID";
                spec.SourceTable = elementColumn.Substring(0, elementColumn.Length - 3);
                spec.SourceKey = elementColumn;
                return spec;
            }

            return null;
        }

        /// <summary>
        /// Confirms a draft's master table actually carries the key and label columns the
        /// spec wants to name, and trims the ones it does not.
        /// </summary>
        /// <param name="spec">Draft being confirmed, adjusted in place.</param>
        /// <param name="columns">The master table's active columns, from AD_Column.</param>
        /// <returns>True when the table can name its values.</returns>
        private bool ConfirmSource(DimensionSpec spec, List<string> columns)
        {
            if (columns.Count == 0) { return false; }
            if (!HasColumn(columns, spec.SourceKey)) { return false; }

            if (spec.ValueColumn.Length > 0 && !HasColumn(columns, spec.ValueColumn))
            {
                spec.ValueColumn = "";
            }
            if (spec.NameColumn.Length > 0 && !HasColumn(columns, spec.NameColumn))
            {
                spec.NameColumn = "";
            }
            if (spec.AltNameColumn.Length > 0 && !HasColumn(columns, spec.AltNameColumn))
            {
                spec.AltNameColumn = "";
            }

            /* Something has to be printable. A table with neither a name nor a value would
               leave every row of the card labelled by nothing at all. */
            return spec.ValueColumn.Length > 0 || spec.NameColumn.Length > 0
                || spec.AltNameColumn.Length > 0;
        }

        /// <summary>
        /// Resolves which dimension the card actually reads.
        ///
        /// A requested element type is honoured only when the schema declares it - a stale or
        /// forged selection falls back to the schema's first element rather than reaching
        /// Fact_Acct. The default is deliberately the FIRST element in SeqNo order and never
        /// a hard-coded dimension: the schema's own order is the tenant's own priority.
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
        // §4  The rows - budget against actual, per dimension value
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Reads every value of the selected dimension that carries a budget or an actual in
        /// the selected year, corrects each side for the account's natural balance, ranks by
        /// utilization and hands the requested page back with its labels.
        ///
        /// The WHOLE set is read rather than one page: the sign correction depends on
        /// C_ElementValue.AccountType and is applied in C#, so neither the utilization nor
        /// the ranking can be decided in SQL without duplicating that CASE into the ORDER BY.
        /// The set is one row per dimension value, an aggregate rather than a transaction
        /// list, so it stays small.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="acct">Resolved accounting context (schema and currency).</param>
        /// <param name="year">Resolved financial year with its date window.</param>
        /// <param name="spec">Resolved dimension - its Fact_Acct column and label source.</param>
        /// <param name="result">Result being filled - paging fields included.</param>
        private void ReadRows(Ctx ctx, AcctContext acct, YearOption year, DimensionSpec spec,
            UtilizationResult result)
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

            /* ONE scan, both posting types, as a FLAT SUM(CASE WHEN ...) per side, grouped by
               the dimension AND by the account type the sign correction needs. The dimension
               column is a name this model resolved from C_AcctSchema_Element and confirmed
               against AD_Column - it is never client text. */
            sql.Append(@"
                SELECT COALESCE(fa.").Append(spec.FactColumn).Append(@",0) AS Dimension_ID,
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

            /* GROUP BY after the access SQL. No HAVING and no ORDER BY: a value is dropped,
               and the ranking is decided, only after the sign correction below - which SQL
               cannot see. */
            rowSql += " GROUP BY COALESCE(fa." + spec.FactColumn + ",0),ev.AccountType";

            DataSet ds = DB.ExecuteDataset(rowSql, parameters.ToArray(), null);
            if (ds == null || ds.Tables.Count == 0) { return; }

            DataTable dt = ds.Tables[0];

            /* Fold the account types back together per dimension value. The split existed
               only so the natural-balance correction could be applied to the right rows. */
            Dictionary<int, UtilizationRow> byDimension = new Dictionary<int, UtilizationRow>();
            List<UtilizationRow> all = new List<UtilizationRow>();

            for (int i = 0; i < dt.Rows.Count; i++)
            {
                DataRow row = dt.Rows[i];

                int dimensionId = Util.GetValueOfInt(row["Dimension_ID"]);
                string accountType = Util.GetValueOfString(row["Account_Type"]);

                decimal budget = Util.GetValueOfDecimal(row["Budget_Signed"]);
                decimal actual = Util.GetValueOfDecimal(row["Actual_Signed"]);

                if (IsCreditNatural(accountType))
                {
                    budget = -budget;
                    actual = -actual;
                }

                UtilizationRow item;
                if (byDimension.ContainsKey(dimensionId))
                {
                    item = byDimension[dimensionId];
                }
                else
                {
                    item = new UtilizationRow();
                    item.Dimension_ID = dimensionId;
                    byDimension.Add(dimensionId, item);
                    all.Add(item);
                }

                item.Budget += budget;
                item.Actual += actual;
            }

            /* Utilization of a budget that does not exist is not a number. A value with
               nothing approved is left out rather than shown as 0% or as an infinite bar -
               unbudgeted actuals are the sibling card's subject, not this one's. */
            List<UtilizationRow> budgeted = new List<UtilizationRow>();
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].Budget <= 0) { continue; }

                all[i].UtilizedPct = all[i].Actual * 100m / all[i].Budget;
                budgeted.Add(all[i]);
            }

            /* Most consumed first - the card is a watch list. The dimension id breaks a tie
               deterministically so a page boundary cannot shuffle between two requests. */
            budgeted.Sort(CompareByUtilization);

            result.TotalRows = budgeted.Count;
            result.TotalPages = result.PageSize > 0
                ? (int)Math.Ceiling((double)result.TotalRows / result.PageSize)
                : 0;

            if (result.TotalRows == 0) { result.Page = 1; return; }

            /* Clamp the page AFTER the total is known: a page number the client kept from a
               longer list must land on the last real page, never past the end. */
            int page = result.Page;
            if (result.TotalPages > 0 && page > result.TotalPages) { page = result.TotalPages; }
            result.Page = page;

            int offset = (page - 1) * result.PageSize;
            for (int i = offset; i < budgeted.Count && i < offset + result.PageSize; i++)
            {
                result.Rows.Add(budgeted[i]);
            }

            /* Labels are read for the PAGE only. The ranking never needed them, and reading
               a name for every value of a chart of accounts to print four of them is work the
               answer does not require. */
            ApplyLabels(ctx, spec, result.Rows);
        }

        /// <summary>
        /// True for the account types whose natural balance is a CREDIT - revenue, liability
        /// and owner's equity.
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
        /// Ranks two rows by utilization, most consumed first, with the dimension id as a
        /// deterministic tiebreaker so paging is stable across requests.
        /// </summary>
        /// <param name="left">First row.</param>
        /// <param name="right">Second row.</param>
        /// <returns>Standard comparison result.</returns>
        private int CompareByUtilization(UtilizationRow left, UtilizationRow right)
        {
            int byPct = right.UtilizedPct.CompareTo(left.UtilizedPct);
            if (byPct != 0) { return byPct; }

            int bySize = right.Budget.CompareTo(left.Budget);
            if (bySize != 0) { return bySize; }

            return left.Dimension_ID.CompareTo(right.Dimension_ID);
        }

        // ─────────────────────────────────────────────────────────────────────
        // §5  Naming the page's dimension values
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Fills each row's label from the dimension's own master table.
        ///
        /// One query for the whole page, keyed by the ids the ranking already chose. This is
        /// a DISPLAY LOOKUP BY PRIMARY KEY - the same treatment VAS_252 gives AD_Org - so no
        /// MRole predicate is applied: every id here came out of the aggregate that the
        /// role's access clause already filtered, and re-filtering the name would blank the
        /// label of a posting the reader is entitled to see, which reads as an unassigned
        /// value rather than as a hidden one.
        ///
        /// IsActive is deliberately NOT filtered here. A deactivated project still posted the
        /// facts this year, and blanking its name would leave a real budget line labelled by
        /// nothing. This is a lookup of history, not a picker of current values.
        ///
        /// A row that resolves to no label keeps an empty one and the client names it from
        /// AD_Message - the unassigned value is a real answer, and the text for it belongs in
        /// the message table rather than in a query.
        /// </summary>
        /// <param name="ctx">Session context (unused today; kept for symmetry with the other
        /// reads).</param>
        /// <param name="spec">Resolved dimension - its label source table and columns.</param>
        /// <param name="rows">The page's rows, labelled in place.</param>
        private void ApplyLabels(Ctx ctx, DimensionSpec spec, List<UtilizationRow> rows)
        {
            List<int> ids = new List<int>();
            Dictionary<int, UtilizationRow> byId = new Dictionary<int, UtilizationRow>();

            for (int i = 0; i < rows.Count; i++)
            {
                int id = rows[i].Dimension_ID;

                /* Id 0 is "not assigned" for every dimension but Organization, where it is
                   the '*' org and has a row of its own to be named from. */
                if (id < 0 || (id == 0 && !spec.ZeroIsValue)) { continue; }
                if (byId.ContainsKey(id)) { continue; }

                byId.Add(id, rows[i]);
                ids.Add(id);
            }

            if (ids.Count == 0) { return; }

            List<SqlParameter> parameters = new List<SqlParameter>();

            StringBuilder sql = new StringBuilder();
            sql.Append("SELECT src.").Append(spec.SourceKey).Append(" AS Dimension_ID");

            if (spec.ValueColumn.Length > 0)
            {
                sql.Append(",COALESCE(src.").Append(spec.ValueColumn).Append(",N'') AS Dimension_Value");
            }
            if (spec.NameColumn.Length > 0)
            {
                sql.Append(",COALESCE(src.").Append(spec.NameColumn).Append(",N'') AS Dimension_Name");
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

                string value = hasValue ? Util.GetValueOfString(row["Dimension_Value"]) : "";
                string name = hasName ? Util.GetValueOfString(row["Dimension_Name"]) : "";

                /* The street line only stands in when the primary name is blank - it is a
                   fallback, never a second half of the label. */
                if (name.Length == 0 && hasAlt) { name = Util.GetValueOfString(row["Dimension_Alt"]); }

                byId[id].Label = ComposeLabel(value, name);
            }
        }

        /// <summary>
        /// "{Value} - {Name}", printed with the separator only when there are two sides to
        /// it, so a master row that carries just one of them is not labelled with a dangling
        /// dash.
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
        // §6  The balancing accounts a budget journal offsets to
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The natural account ids the schema uses as balancing / offset accounts.
        ///
        /// A budget journal balances, so the other side of every budget posting lands on one
        /// of these. Left in the set, such an account carries the mirror image of everything
        /// budgeted and inflates whichever dimension value it was posted against.
        ///
        /// Each setting holds a C_ValidCombination_ID, NOT a Fact_Acct.Account_ID, so it is
        /// resolved C_AcctSchema_GL -&gt; C_ValidCombination -&gt; Account_ID. Only the
        /// columns AD_Column confirms the table actually carries are named: the commitment
        /// pair and the localized budget offset are optional in this schema, and naming a
        /// missing column would fail the whole query and cost the exclusions that ARE
        /// configured.
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

            List<string> present = ColumnsOf(ReadTableColumns(tables), TABLE_ACCTSCHEMA_GL);

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
        // §7  Dictionary helpers
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The active column names of several dictionary tables, in ONE read. Used to confirm
        /// a table and a column exist before either is named in generated SQL - user-element
        /// dimensions in particular name a table this model has never heard of.
        ///
        /// Columns backed by a virtual expression (AD_Column.ColumnSQL) are excluded: they
        /// are not physical columns and cannot be grouped by or filtered on.
        /// </summary>
        /// <param name="tableNames">Physical table names - resolved from the dictionary or
        /// constants of this class, never free client text.</param>
        /// <returns>Upper-cased table name -&gt; its column names (never null).</returns>
        private Dictionary<string, List<string>> ReadTableColumns(List<string> tableNames)
        {
            Dictionary<string, List<string>> map = new Dictionary<string, List<string>>();
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

            string sql = @"
                SELECT UPPER(t.TableName) AS Table_Name,
                       c.ColumnName AS Column_Name
                FROM AD_Column c
                INNER JOIN AD_Table t ON (t.AD_Table_ID=c.AD_Table_ID)
                WHERE t.IsActive='Y'
                  AND c.IsActive='Y'
                  AND c.ColumnSQL IS NULL
                  AND UPPER(t.TableName) IN (" + inList.ToString() + ")";

            DataSet ds = DB.ExecuteDataset(sql, parameters.ToArray(), null);
            if (ds == null || ds.Tables.Count == 0) { return map; }

            DataTable dt = ds.Tables[0];
            for (int i = 0; i < dt.Rows.Count; i++)
            {
                string table = Util.GetValueOfString(dt.Rows[i]["Table_Name"]);
                string column = Util.GetValueOfString(dt.Rows[i]["Column_Name"]);
                if (table.Length == 0 || column.Length == 0) { continue; }

                if (!map.ContainsKey(table)) { map.Add(table, new List<string>()); }
                map[table].Add(column);
            }

            return map;
        }

        /// <summary>One table's columns out of a dictionary read.</summary>
        /// <param name="map">Result of <see cref="ReadTableColumns"/>.</param>
        /// <param name="tableName">Table to look up.</param>
        /// <returns>Its columns, or an empty list when the table is not installed.</returns>
        private List<string> ColumnsOf(Dictionary<string, List<string>> map, string tableName)
        {
            string key = tableName == null ? "" : tableName.ToUpper();
            return map.ContainsKey(key) ? map[key] : new List<string>();
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
        // §8  Helpers
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
        // §9  Transfer objects
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// One accounting dimension, fully resolved: what it is called, where its values are
        /// stored on Fact_Acct and where their names come from. Internal - the client only
        /// ever sees the element type and the label.
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

            /// <summary>C_AcctSchema_Element.Name - the tenant's own word for this dimension.</summary>
            public string Label { get; set; }

            /// <summary>C_AcctSchema_Element.SeqNo - the schema's own order.</summary>
            public int SeqNo { get; set; }

            /// <summary>The Fact_Acct column the dimension's value is stored in.</summary>
            public string FactColumn { get; set; }

            /// <summary>The master table its values are named from.</summary>
            public string SourceTable { get; set; }

            /// <summary>That table's key column.</summary>
            public string SourceKey { get; set; }

            /// <summary>Its search-key column, or empty when it has none.</summary>
            public string ValueColumn { get; set; }

            /// <summary>Its name column, or empty when it has none.</summary>
            public string NameColumn { get; set; }

            /// <summary>A fallback name column used only when the name is blank - the street
            /// line of a location, for instance.</summary>
            public string AltNameColumn { get; set; }

            /// <summary>True when id 0 is a real value of this dimension rather than "not
            /// assigned" - which is the case for Organization, where 0 is the '*' org.</summary>
            public bool ZeroIsValue { get; set; }
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

            /// <summary>First accounting date of the year, as yyyy-MM-dd.</summary>
            public string StartDate { get; set; }

            /// <summary>Last accounting date of the year, as yyyy-MM-dd.</summary>
            public string EndDate { get; set; }

            /// <summary>ElementType actually grouped by, after defaulting and validation.</summary>
            public string Dimension { get; set; }

            /// <summary>That element's own Name - the dimension pill's label.</summary>
            public string DimensionLabel { get; set; }

            /// <summary>1-based page number actually served, after clamping.</summary>
            public int Page { get; set; }

            /// <summary>Rows per page actually used, after clamping.</summary>
            public int PageSize { get; set; }

            /// <summary>Dimension values in the ranking in total - the pager's figure.</summary>
            public int TotalRows { get; set; }

            /// <summary>CEILING(TotalRows / PageSize).</summary>
            public int TotalPages { get; set; }

            /// <summary>ERROR_* token when the tenant is not configured; empty otherwise.</summary>
            public string ErrorCode { get; set; }

            /// <summary>False only on a failure or a missing configuration; a year with
            /// nothing budgeted is Loaded=true with an empty page.</summary>
            public bool Loaded { get; set; }
        }

        /// <summary>
        /// One dimension value's budget against its actual. Both figures are in the PRIMARY
        /// accounting schema currency and are never converted - AmtAcctDr / AmtAcctCr are
        /// already stated in it - and both have been corrected for the account's natural
        /// balance, so a revenue budget reads the same way round as an expense one.
        /// </summary>
        public class UtilizationRow
        {
            public UtilizationRow()
            {
                Label = "";
            }

            /// <summary>The dimension value's own id; 0 when the postings carry no value for
            /// the selected dimension.</summary>
            public int Dimension_ID { get; set; }

            /// <summary>"{Value} - {Name}" from the dimension's master table; empty when the
            /// value is unassigned, which the client names from AD_Message.</summary>
            public string Label { get; set; }

            /// <summary>Approved budget for the year: PostingType 'B', natural-side
            /// corrected. Always greater than zero - a value without one is not on the
            /// card.</summary>
            public decimal Budget { get; set; }

            /// <summary>Posted actual for the year: PostingType 'A', natural-side
            /// corrected.</summary>
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
