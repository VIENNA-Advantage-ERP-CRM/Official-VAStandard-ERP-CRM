/******************************************************
 * Module Name    : VASLogic
 * Purpose        : Mandatory Close Checklist dashboard widget - shared framework
 * chronological  : Development
 * Created Date   : 2026-08-24
 * Created by     : VAI154
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
    /// Module Name : VAS_195_MandatoryChecklist
    /// Purpose     : Backs the VAS_195_MandatoryChecklistWidget dashboard widget - the
    ///               23 mandatory period-close checks evaluated against ONE open
    ///               accounting period, each classified as a BLOCKER, a WARNING or a
    ///               positive CHECK, and each openable as a paged list of the records
    ///               behind it.
    ///
    ///               This file carries the FRAMEWORK: the accounting context, the
    ///               period selector, the check registry, the close verdict, the
    ///               generic secured-query runner, the source-document resolver and the
    ///               data contracts. The 23 handlers themselves live in the partial
    ///               VAS_195_MandatoryChecklistChecks.cs - one Evaluate/Detail pair per
    ///               check, so a check can be read, changed or re-classified on its own.
    ///
    ///               Design notes that shape everything below:
    ///
    ///               GENERIC DETAIL CONTRACT. The 23 checks have 23 different row
    ///               shapes, so a check does not return a typed row list - it returns a
    ///               DetailSpec: one secured SQL statement plus the COLUMNS it declares.
    ///               The client renders any check's modal from those declared columns.
    ///               That is what keeps 23 checks from becoming 23 bespoke DTOs and 23
    ///               bespoke renderers, and it is why the same paging, the same
    ///               document resolver and the same MRole treatment apply uniformly.
    ///
    ///               MROLE. Every DetailSpec names the ONE main physical table alias it
    ///               is fetching from. AddAccessSQL is applied to that alias, on that
    ///               statement, before anything wraps it - never to a CTE alias, never
    ///               to a derived alias, never to a UNION result, and never twice. The
    ///               count runner wraps the ALREADY SECURED statement in a derived
    ///               table rather than re-securing it. Secondary aliases (a self-join
    ///               used only for comparison, a lookup join) inherit the parent's
    ///               filter and are deliberately left alone.
    ///
    ///               PERIOD BOUNDS. Every date filter is @PeriodStart inclusive /
    ///               @PeriodEndExclusive exclusive, both computed server-side from the
    ///               period's own StartDate and EndDate. No TRUNC, no month arithmetic,
    ///               no string dates - a document stamped with a time still falls
    ///               inside its period, on both backends.
    ///
    ///               OPTIONAL MODULES. Several checks target tables or columns that
    ///               only exist when a module is installed (fixed assets, FRPT forex
    ///               revaluation, localization matching flags). Nothing is assumed: the
    ///               dictionary is probed once per request and a check whose schema is
    ///               absent returns NOT_APPLICABLE with a reason rather than failing
    ///               the whole checklist. A missing module is never a false PASS and
    ///               never a crash.
    ///
    ///               Compatible with PostgreSQL and Oracle.
    /// Chronological development:
    ///   VAI154      2026-08-24 Created
    /// </summary>
    public partial class VAS_195_MandatoryChecklistModel
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_195_MandatoryChecklistModel).FullName);

        // ── Classification tokens (the client resolves the label) ────────────
        public const string CLASS_BLOCKER = "BLOCKER";
        public const string CLASS_WARNING = "WARNING";
        public const string CLASS_CHECK = "CHECK";

        // ── Status tokens ────────────────────────────────────────────────────
        public const string STATUS_PASS = "PASS";
        public const string STATUS_FAIL = "FAIL";
        public const string STATUS_WARNING = "WARNING";
        public const string STATUS_COMPLETE = "COMPLETE";
        public const string STATUS_INCOMPLETE = "INCOMPLETE";
        public const string STATUS_NOT_APPLICABLE = "NOT_APPLICABLE";
        public const string STATUS_CONFIGURATION_ERROR = "CONFIGURATION_ERROR";

        // ── Error tokens ─────────────────────────────────────────────────────
        public const string ERROR_INVALID_REQUEST = "INVALID";
        public const string ERROR_NO_PERIOD = "NOPERIOD";
        public const string ERROR_NO_CALENDAR = "NOCALENDAR";
        public const string ERROR_NO_ACCTSCHEMA = "NOACCTSCHEMA";
        public const string ERROR_NO_DETAIL = "NODETAIL";

        // ── Column type tokens for the generic detail contract ───────────────
        public const string COLTYPE_TEXT = "TEXT";
        public const string COLTYPE_DATE = "DATE";
        public const string COLTYPE_AMOUNT = "AMOUNT";
        public const string COLTYPE_DOCAMOUNT = "DOCAMOUNT";
        public const string COLTYPE_QTY = "QTY";
        public const string COLTYPE_NUMBER = "NUMBER";
        public const string COLTYPE_BADGE = "BADGE";
        public const string COLTYPE_DOC = "DOC";
        public const string COLTYPE_SCREEN = "SCREEN";

        /* Reserved SELECT aliases. A DetailSpec that exposes these lets the shared
           resolver fill in the screen label, the document display value and the
           navigation target - no check resolves a document number for itself. */
        public const string TECH_TABLE = "Tech_AD_Table_ID";
        public const string TECH_RECORD = "Tech_Record_ID";
        public const string TECH_WINDOW = "Tech_AD_Window_ID";

        /* Stored codes. */
        private const string PERIODSTATUS_Open = "O";
        private const string PERIODTYPE_Standard = "S";
        private const string POSTINGTYPE_Actual = "A";

        /* Document states. Two groups, deliberately kept apart: an OPEN document is
           still being worked on (check 01), a FINAL one is done and should therefore
           carry accounting (check 02). */
        private const string DOCSTATUS_OpenList = "'DR','IP','IN','WP','WC'";
        private const string DOCSTATUS_FinalList = "'CO','CL'";
        private const string DOCSTATUS_DeadList = "'VO','RE'";

        /* Detail paging guard rails. The client asks; the server clamps. */
        private const int PAGESIZE_MIN = 1;
        private const int PAGESIZE_MAX = 100;
        private const int PAGESIZE_DEFAULT = 25;

        /* Longest dictionary identifier accepted into generated SQL. */
        private const int IDENTIFIER_MAX = 60;

        /* At most this many IsIdentifier columns are concatenated into a display
           value. Beyond three the string stops being something a reader scans. */
        private const int IDENTIFIER_COLUMN_MAX = 3;

        /* Per-request caches. One modal page touches a handful of tables and one
           checklist evaluation probes the dictionary once - neither should re-read it. */
        private Dictionary<int, SourceTable> _sourceTables;
        private Dictionary<string, bool> _tableExists;
        private Dictionary<string, List<string>> _tableColumns;

        // ─────────────────────────────────────────────────────────────────────
        // §1  Bootstrap, accounting context and period selection
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Bootstraps the widget in one round trip: the tenant's accounting context,
        /// every selectable open period of the primary calendar, the period to
        /// preselect, and that period's 23 evaluated checks.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <returns>Populated <see cref="ChecklistBootstrap"/> (never null).</returns>
        public ChecklistBootstrap GetBootstrap(Ctx ctx)
        {
            ChecklistBootstrap result = new ChecklistBootstrap();
            result.Periods = new List<PeriodItem>();
            result.Data = new PeriodData();

            if (ctx == null) { return result; }

            /* The accounting context is a CONFIGURATION precondition, not a filter.
               Without a primary calendar there is no period list to build; without a
               primary accounting schema there is no ledger to check. Neither is ever
               silently replaced by "some other" calendar or schema. */
            AcctContext acct = GetAcctContext(ctx);
            result.Schema = acct;

            if (acct.C_Calendar_ID <= 0)
            {
                result.ErrorCode = ERROR_NO_CALENDAR;
                Log.Log(Level.WARNING, "VAS_195: AD_ClientInfo.C_Calendar_ID not configured, AD_Client_ID="
                    + ctx.GetAD_Client_ID());
                return result;
            }

            if (acct.C_AcctSchema_ID <= 0)
            {
                result.ErrorCode = ERROR_NO_ACCTSCHEMA;
                Log.Log(Level.WARNING, "VAS_195: AD_ClientInfo.C_AcctSchema1_ID not configured, AD_Client_ID="
                    + ctx.GetAD_Client_ID());
                return result;
            }

            result.Periods = GetOpenPeriods(ctx, acct.C_Calendar_ID);
            if (result.Periods.Count == 0) { return result; }

            PeriodItem selected = PickDefaultPeriod(result.Periods, DateTime.Now.Date);
            result.C_Period_ID = selected.C_Period_ID;
            result.PeriodName = selected.Name;
            result.Data = GetPeriodData(ctx, selected.C_Period_ID);

            return result;
        }

        /// <summary>
        /// The tenant's accounting context: the primary calendar and the primary
        /// accounting schema, with the currency every converted figure is expressed in.
        /// Both come from AD_ClientInfo - never from a search over all calendars or all
        /// schemas.
        ///
        /// Reads only client-scoped configuration and reference tables, so no MRole
        /// predicate is applied - the same treatment the sibling Period Control widgets
        /// give this lookup.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <returns>Populated <see cref="AcctContext"/>; ids are 0 when unconfigured.</returns>
        public AcctContext GetAcctContext(Ctx ctx)
        {
            AcctContext result = new AcctContext();
            result.Precision = 2;

            if (ctx == null) { return result; }

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
                   primary schema lands here too. Read the calendar alone so the caller
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
        /// The primary calendar on its own. Only reached when the accounting-context
        /// query found nothing, to tell "no calendar" apart from "no schema".
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
        /// The open standard periods of the tenant's primary calendar, newest first. A
        /// period qualifies when at least one of its active C_PeriodControl rows is
        /// Open - close readiness is not confined to one document base type, so
        /// requiring every control row to be open would hide the very periods this
        /// checklist exists to examine.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="calendarId">The tenant's primary C_Calendar_ID.</param>
        /// <returns>Open standard periods, newest StartDate first (never null).</returns>
        public List<PeriodItem> GetOpenPeriods(Ctx ctx, int calendarId)
        {
            List<PeriodItem> items = new List<PeriodItem>();
            if (ctx == null || calendarId <= 0) { return items; }

            /* EXISTS rather than a join, so several open base types cannot multiply the
               period out and no DISTINCT is needed. */
            string sql = @"
                SELECT p.C_Period_ID AS C_Period_ID,
                       p.Name AS Period_Name,
                       p.StartDate AS Start_Date,
                       p.EndDate AS End_Date,
                       y.C_Year_ID AS C_Year_ID,
                       y.FiscalYear AS Fiscal_Year,
                       y.C_Calendar_ID AS C_Calendar_ID
                FROM C_Period p
                INNER JOIN C_Year y ON (y.C_Year_ID=p.C_Year_ID)
                WHERE p.IsActive='Y'
                  AND p.PeriodType=@PeriodType
                  AND y.IsActive='Y'
                  AND y.C_Calendar_ID=@C_Calendar_ID
                  AND EXISTS(SELECT 1 FROM C_PeriodControl pc WHERE pc.C_Period_ID=p.C_Period_ID AND pc.IsActive='Y' AND pc.PeriodStatus=@PeriodStatus)";

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "p", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            sql += " ORDER BY p.StartDate DESC,p.EndDate DESC,p.C_Period_ID DESC";

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@PeriodType", PERIODTYPE_Standard),
                new SqlParameter("@C_Calendar_ID", calendarId),
                new SqlParameter("@PeriodStatus", PERIODSTATUS_Open)
            };

            DataSet ds = DB.ExecuteDataset(sql, parameters, null);
            if (ds == null || ds.Tables.Count == 0) { return items; }

            DataTable dt = ds.Tables[0];
            for (int i = 0; i < dt.Rows.Count; i++)
            {
                items.Add(ReadPeriodRow(dt.Rows[i]));
            }

            return items;
        }

        /// <summary>Materialises one C_Period row.</summary>
        /// <param name="row">Result row carrying the period aliases.</param>
        /// <returns>Populated <see cref="PeriodItem"/>.</returns>
        private PeriodItem ReadPeriodRow(DataRow row)
        {
            PeriodItem item = new PeriodItem();
            item.C_Period_ID = Util.GetValueOfInt(row["C_Period_ID"]);
            item.Name = Util.GetValueOfString(row["Period_Name"]);
            item.StartDate = Util.GetValueOfDateTime(row["Start_Date"]);
            item.EndDate = Util.GetValueOfDateTime(row["End_Date"]);

            if (row.Table.Columns.Contains("C_Year_ID"))
            {
                item.C_Year_ID = Util.GetValueOfInt(row["C_Year_ID"]);
                item.FiscalYear = Util.GetValueOfString(row["Fiscal_Year"]);
                item.C_Calendar_ID = Util.GetValueOfInt(row["C_Calendar_ID"]);
            }

            return item;
        }

        /// <summary>
        /// Chooses which open period the checklist opens on: the one containing today,
        /// otherwise the most recent one that has already started, otherwise the first
        /// of the list. The list is newest-first, so the first match wins.
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
        /// Re-reads one period and confirms it is still active, accessible, open, a
        /// standard period and on the tenant's primary calendar. The client only ever
        /// sends the id; every date the 23 checks run against comes from here.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="calendarId">The tenant's primary C_Calendar_ID.</param>
        /// <param name="periodId">C_Period_ID the client selected.</param>
        /// <returns>Populated <see cref="PeriodItem"/>, or null when it no longer qualifies.</returns>
        private PeriodItem GetOpenPeriod(Ctx ctx, int calendarId, int periodId)
        {
            if (ctx == null || calendarId <= 0 || periodId <= 0) { return null; }

            string sql = @"
                SELECT p.C_Period_ID AS C_Period_ID,
                       p.Name AS Period_Name,
                       p.StartDate AS Start_Date,
                       p.EndDate AS End_Date,
                       y.C_Year_ID AS C_Year_ID,
                       y.FiscalYear AS Fiscal_Year,
                       y.C_Calendar_ID AS C_Calendar_ID
                FROM C_Period p
                INNER JOIN C_Year y ON (y.C_Year_ID=p.C_Year_ID)
                WHERE p.C_Period_ID=@C_Period_ID
                  AND p.IsActive='Y'
                  AND p.PeriodType=@PeriodType
                  AND y.IsActive='Y'
                  AND y.C_Calendar_ID=@C_Calendar_ID
                  AND EXISTS(SELECT 1 FROM C_PeriodControl pc WHERE pc.C_Period_ID=p.C_Period_ID AND pc.IsActive='Y' AND pc.PeriodStatus=@PeriodStatus)";

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "p", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@C_Period_ID", periodId),
                new SqlParameter("@PeriodType", PERIODTYPE_Standard),
                new SqlParameter("@C_Calendar_ID", calendarId),
                new SqlParameter("@PeriodStatus", PERIODSTATUS_Open)
            };

            DataSet ds = DB.ExecuteDataset(sql, parameters, null);
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) { return null; }

            return ReadPeriodRow(ds.Tables[0].Rows[0]);
        }

        // ─────────────────────────────────────────────────────────────────────
        // §2  The check registry
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The 23 checks in their configured sequence, with the classification each is
        /// specified to carry. This is the ONLY place a check code is accepted from:
        /// a detail request naming anything not in this table is refused before any
        /// query is built, so the browser can never steer the server at a table.
        /// </summary>
        /// <returns>The registry, in sequence order (never null).</returns>
        public static List<CheckDef> GetRegistry()
        {
            List<CheckDef> registry = new List<CheckDef>();

            registry.Add(NewCheck("MPC_CLOSE_01", 1, CLASS_BLOCKER, "VAS_195_Chk01", "Unprocessed documents"));
            registry.Add(NewCheck("MPC_CLOSE_02", 2, CLASS_BLOCKER, "VAS_195_Chk02", "Unposted accounting entries"));
            registry.Add(NewCheck("MPC_CLOSE_03", 3, CLASS_WARNING, "VAS_195_Chk03", "Payment allocation status"));
            registry.Add(NewCheck("MPC_CLOSE_04", 4, CLASS_WARNING, "VAS_195_Chk04", "Bank reconciliation"));
            registry.Add(NewCheck("MPC_CLOSE_05", 5, CLASS_WARNING, "VAS_195_Chk05", "In-progress / bounced payments"));
            registry.Add(NewCheck("MPC_CLOSE_06", 6, CLASS_WARNING, "VAS_195_Chk06", "GRNs not invoiced"));
            registry.Add(NewCheck("MPC_CLOSE_07", 7, CLASS_WARNING, "VAS_195_Chk07", "Invoices without GRN"));
            registry.Add(NewCheck("MPC_CLOSE_08", 8, CLASS_WARNING, "VAS_195_Chk08", "Qty / price mismatches"));
            registry.Add(NewCheck("MPC_CLOSE_09", 9, CLASS_BLOCKER, "VAS_195_Chk09", "Suspense account balances"));
            registry.Add(NewCheck("MPC_CLOSE_10", 10, CLASS_WARNING, "VAS_195_Chk10", "Clearing account balances"));
            registry.Add(NewCheck("MPC_CLOSE_11", 11, CLASS_WARNING, "VAS_195_Chk11", "Incomplete allocations / settlements"));
            registry.Add(NewCheck("MPC_CLOSE_12", 12, CLASS_BLOCKER, "VAS_195_Chk12", "Missing recurring entries"));
            registry.Add(NewCheck("MPC_CLOSE_13", 13, CLASS_WARNING, "VAS_195_Chk13", "Missing accruals / provisions"));
            registry.Add(NewCheck("MPC_CLOSE_14", 14, CLASS_BLOCKER, "VAS_195_Chk14", "Fixed asset depreciation not processed"));
            registry.Add(NewCheck("MPC_CLOSE_15", 15, CLASS_BLOCKER, "VAS_195_Chk15", "Pending inventory transactions"));
            registry.Add(NewCheck("MPC_CLOSE_16", 16, CLASS_BLOCKER, "VAS_195_Chk16", "Inventory costing not completed"));
            registry.Add(NewCheck("MPC_CLOSE_17", 17, CLASS_WARNING, "VAS_195_Chk17", "Physical inventory adjustments pending"));
            registry.Add(NewCheck("MPC_CLOSE_18", 18, CLASS_WARNING, "VAS_195_Chk18", "Tax transactions pending posting"));
            registry.Add(NewCheck("MPC_CLOSE_19", 19, CLASS_BLOCKER, "VAS_195_Chk19", "Foreign currency revaluation not run"));
            registry.Add(NewCheck("MPC_CLOSE_20", 20, CLASS_BLOCKER, "VAS_195_Chk20", "Trial Balance debit <> credit"));
            registry.Add(NewCheck("MPC_CLOSE_21", 21, CLASS_CHECK, "VAS_195_Chk21", "Bank accounts fully reconciled"));
            registry.Add(NewCheck("MPC_CLOSE_22", 22, CLASS_WARNING, "VAS_195_Chk22", "Required document base types still open"));
            registry.Add(NewCheck("MPC_CLOSE_23", 23, CLASS_WARNING, "VAS_195_Chk23", "Prior period unexpectedly open"));

            return registry;
        }

        /// <summary>Builds one registry entry.</summary>
        /// <param name="code">MPC_CLOSE_* check code.</param>
        /// <param name="sequence">Configured display sequence.</param>
        /// <param name="classification">CLASS_* token.</param>
        /// <param name="titleKey">AD_Message key for the title.</param>
        /// <param name="titleText">English fallback for the title.</param>
        /// <returns>Populated <see cref="CheckDef"/>.</returns>
        private static CheckDef NewCheck(string code, int sequence, string classification,
            string titleKey, string titleText)
        {
            CheckDef def = new CheckDef();
            def.CheckCode = code;
            def.Sequence = sequence;
            def.Classification = classification;
            def.TitleKey = titleKey;
            def.TitleText = titleText;
            return def;
        }

        /// <summary>The registry entry for one code, or null when the code is unknown.</summary>
        /// <param name="checkCode">Code supplied by the client.</param>
        /// <returns>Matching <see cref="CheckDef"/>, or null.</returns>
        private CheckDef FindCheck(string checkCode)
        {
            if (string.IsNullOrEmpty(checkCode)) { return null; }

            List<CheckDef> registry = GetRegistry();
            for (int i = 0; i < registry.Count; i++)
            {
                if (registry[i].CheckCode.Equals(checkCode, StringComparison.Ordinal)) { return registry[i]; }
            }

            return null;
        }

        // ─────────────────────────────────────────────────────────────────────
        // §3  Evaluation of one period
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Evaluates all 23 checks for one period and returns them sorted by close
        /// priority, together with the close verdict.
        ///
        /// All 23 rows are ALWAYS returned - PASS, NOT_APPLICABLE and
        /// CONFIGURATION_ERROR included. A checklist that hides its satisfied rows
        /// cannot be used as evidence that the period was actually checked.
        ///
        /// One failing check never stops the others: each is evaluated inside its own
        /// guard, and a handler that throws becomes a CONFIGURATION_ERROR row naming
        /// itself rather than an empty widget. That is the one place a broad catch is
        /// justified here - the alternative is losing 22 good answers to one bad table.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="periodId">C_Period_ID selected by the user.</param>
        /// <returns>Populated <see cref="PeriodData"/> (never null).</returns>
        public PeriodData GetPeriodData(Ctx ctx, int periodId)
        {
            PeriodData result = new PeriodData();
            result.C_Period_ID = periodId;
            result.Items = new List<CheckResult>();
            result.Messages = new List<string>();

            if (ctx == null) { return result; }

            AcctContext acct = GetAcctContext(ctx);
            result.Schema = acct;

            if (acct.C_Calendar_ID <= 0) { result.ErrorCode = ERROR_NO_CALENDAR; return result; }
            if (acct.C_AcctSchema_ID <= 0) { result.ErrorCode = ERROR_NO_ACCTSCHEMA; return result; }

            PeriodItem period = GetOpenPeriod(ctx, acct.C_Calendar_ID, periodId);
            if (period == null) { result.ErrorCode = ERROR_NO_PERIOD; return result; }

            result.PeriodName = period.Name;
            result.PeriodStartDate = period.StartDate;
            result.PeriodEndDate = period.EndDate;
            result.FiscalYear = period.FiscalYear;

            CheckContext context = new CheckContext();
            context.Ctx = ctx;
            context.Acct = acct;
            context.Period = period;
            context.PeriodStart = PeriodStart(period);
            context.PeriodEndExclusive = PeriodEndExclusive(period);
            context.Tolerance = PrecisionTolerance(acct.Precision);

            List<CheckDef> registry = GetRegistry();
            for (int i = 0; i < registry.Count; i++)
            {
                result.Items.Add(EvaluateGuarded(context, registry[i]));
            }

            ApplyVerdict(result);
            SortByClosePriority(result.Items);

            return result;
        }

        /// <summary>
        /// Runs one check and converts any failure into a CONFIGURATION_ERROR row for
        /// that check alone.
        /// </summary>
        /// <param name="context">Shared per-request evaluation context.</param>
        /// <param name="def">Registry entry being evaluated.</param>
        /// <returns>Populated <see cref="CheckResult"/> (never null).</returns>
        private CheckResult EvaluateGuarded(CheckContext context, CheckDef def)
        {
            try
            {
                CheckResult result = Evaluate(context, def);
                if (result != null) { return result; }

                return Configured(def, "VAS_195_NotEvaluated", "Check could not be evaluated");
            }
            catch (Exception ex)
            {
                /* Enough context to find it in the log without leaking data. */
                Log.Log(Level.SEVERE, "VAS_195: check " + def.CheckCode
                    + " failed, AD_Client_ID=" + context.Ctx.GetAD_Client_ID()
                    + ", C_Period_ID=" + context.Period.C_Period_ID, ex);

                return Configured(def, "VAS_195_CheckFailed", "Check could not be evaluated");
            }
        }

        /// <summary>
        /// Sets the close verdict from the evaluated rows. Close is permitted only when
        /// no BLOCKER is failing or misconfigured; warnings stay visible and reviewable
        /// but never gate the action on their own.
        /// </summary>
        /// <param name="data">Evaluated period data being completed.</param>
        private void ApplyVerdict(PeriodData data)
        {
            for (int i = 0; i < data.Items.Count; i++)
            {
                CheckResult item = data.Items[i];

                if (item.IsBlocking) { data.BlockerFailCount++; }
                else if (STATUS_WARNING.Equals(item.Status)) { data.WarningCount++; }
                else if (STATUS_COMPLETE.Equals(item.Status)) { data.CheckCompleteCount++; }
            }

            data.CloseAllowed = data.BlockerFailCount == 0;
        }

        /// <summary>
        /// Orders the checklist the way it has to be worked: what stops the close, then
        /// what needs a decision, then what is merely unfinished, then the settled rows,
        /// and finally what does not apply. Configured sequence breaks ties inside each
        /// band, so the list is stable between refreshes.
        /// </summary>
        /// <param name="items">Evaluated rows, reordered in place.</param>
        private void SortByClosePriority(List<CheckResult> items)
        {
            items.Sort(delegate (CheckResult a, CheckResult b)
            {
                int rank = PriorityRank(a).CompareTo(PriorityRank(b));
                if (rank != 0) { return rank; }
                return a.Sequence.CompareTo(b.Sequence);
            });
        }

        /// <summary>The sort band one row belongs to (lower sorts first).</summary>
        /// <param name="item">Evaluated row.</param>
        /// <returns>Band 1-7.</returns>
        private int PriorityRank(CheckResult item)
        {
            if (STATUS_NOT_APPLICABLE.Equals(item.Status)) { return 7; }

            bool blocker = CLASS_BLOCKER.Equals(item.Classification);
            bool warning = CLASS_WARNING.Equals(item.Classification);

            if (blocker && (STATUS_FAIL.Equals(item.Status) || STATUS_CONFIGURATION_ERROR.Equals(item.Status))) { return 1; }
            if (warning && STATUS_WARNING.Equals(item.Status)) { return 2; }
            if (STATUS_INCOMPLETE.Equals(item.Status)) { return 3; }
            if (blocker && STATUS_PASS.Equals(item.Status)) { return 4; }
            if (warning && STATUS_PASS.Equals(item.Status)) { return 5; }
            if (STATUS_COMPLETE.Equals(item.Status)) { return 6; }

            return 6;
        }

        // ─────────────────────────────────────────────────────────────────────
        // §4  Detail (server-side paging)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// One page of the records behind a checklist row, plus the total count and the
        /// column set that check declares.
        ///
        /// The check code is validated against the registry before anything is built,
        /// and the period is re-validated exactly as it is for the summary. The browser
        /// contributes a code, a period, a page number and a page size - nothing that
        /// reaches SQL as text.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="periodId">C_Period_ID selected by the user.</param>
        /// <param name="checkCode">MPC_CLOSE_* code of the clicked row.</param>
        /// <param name="pageNo">1-based page number.</param>
        /// <param name="pageSize">Rows per page (clamped server-side).</param>
        /// <returns>Populated <see cref="DetailPage"/> (never null).</returns>
        public DetailPage GetDetail(Ctx ctx, int periodId, string checkCode, int pageNo, int pageSize)
        {
            DetailPage result = new DetailPage();
            result.Rows = new List<DetailRow>();
            result.Columns = new List<ColumnDef>();
            result.CheckCode = checkCode;
            result.C_Period_ID = periodId;
            result.PageNo = 1;
            result.PageSize = pageSize;

            if (ctx == null) { return result; }

            CheckDef def = FindCheck(checkCode);
            if (def == null)
            {
                result.ErrorCode = ERROR_INVALID_REQUEST;
                return result;
            }

            result.Classification = def.Classification;

            AcctContext acct = GetAcctContext(ctx);
            result.Schema = acct;

            if (acct.C_Calendar_ID <= 0) { result.ErrorCode = ERROR_NO_CALENDAR; return result; }
            if (acct.C_AcctSchema_ID <= 0) { result.ErrorCode = ERROR_NO_ACCTSCHEMA; return result; }

            PeriodItem period = GetOpenPeriod(ctx, acct.C_Calendar_ID, periodId);
            if (period == null) { result.ErrorCode = ERROR_NO_PERIOD; return result; }

            result.PeriodName = period.Name;

            if (pageSize < PAGESIZE_MIN || pageSize > PAGESIZE_MAX) { pageSize = PAGESIZE_DEFAULT; }
            if (pageNo < 1) { pageNo = 1; }
            result.PageSize = pageSize;

            CheckContext context = new CheckContext();
            context.Ctx = ctx;
            context.Acct = acct;
            context.Period = period;
            context.PeriodStart = PeriodStart(period);
            context.PeriodEndExclusive = PeriodEndExclusive(period);
            context.Tolerance = PrecisionTolerance(acct.Precision);

            DetailSpec spec;
            try
            {
                spec = BuildDetail(context, def);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_195: detail for " + def.CheckCode
                    + " failed, AD_Client_ID=" + ctx.GetAD_Client_ID()
                    + ", C_Period_ID=" + periodId, ex);
                result.ErrorCode = ERROR_NO_DETAIL;
                return result;
            }

            if (spec == null)
            {
                /* A check with nothing to drill into - a positive confirmation, a
                   not-applicable module, or a setup-needed row. Not an error. */
                result.ErrorCode = ERROR_NO_DETAIL;
                return result;
            }

            result.Columns = spec.Columns;

            result.Total = CountOf(ctx, spec);
            if (result.Total == 0) { return result; }

            int totalPages = (result.Total + pageSize - 1) / pageSize;
            if (pageNo > totalPages) { pageNo = totalPages; }
            result.PageNo = pageNo;

            result.Rows = PageOf(ctx, spec, pageNo, pageSize);

            ResolveDocuments(ctx, result.Rows);
            ApplyNavigability(ctx, result.Rows);

            return result;
        }

        // ─────────────────────────────────────────────────────────────────────
        // §5  Secured query runners
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Applies the role filter to a spec's MAIN physical table and returns the
        /// secured statement.
        ///
        /// This is the single place AddAccessSQL is called for check data, so the rule
        /// is enforced in one place rather than trusted to 23 handlers: one call, on the
        /// alias the spec itself names, on the statement while it is still a plain
        /// physical-table SELECT - before any wrapper, ORDER BY or paging clause exists
        /// for the FROM-clause parser to trip over. A spec with no main alias is a
        /// dictionary or configuration read and is deliberately left unfiltered.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="spec">Detail specification carrying the SQL and the alias.</param>
        /// <returns>Secured SQL statement.</returns>
        private string Secure(Ctx ctx, DetailSpec spec)
        {
            string sql = string.IsNullOrEmpty(spec.MainAlias)
                ? spec.Sql
                : MRole.GetDefault(ctx).AddAccessSQL(spec.Sql, spec.MainAlias,
                    MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            /* GROUP BY goes on only now - after the access predicate has been injected
               into the WHERE clause. Appending it to spec.Sql beforehand would leave the
               FROM-clause parser looking at a trailing clause it does not expect. */
            if (!string.IsNullOrEmpty(spec.GroupBy)) { sql += " GROUP BY " + spec.GroupBy; }

            return sql;
        }

        /// <summary>
        /// How many rows the check has.
        ///
        /// The ALREADY SECURED statement is wrapped in a derived table and counted -
        /// AddAccessSQL is not run again on the wrapper, because the wrapper's FROM is a
        /// derived alias, not a physical table. Counting this way also guarantees the
        /// count and the page can never disagree: they are literally the same statement.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="spec">Detail specification.</param>
        /// <returns>Total row count.</returns>
        private int CountOf(Ctx ctx, DetailSpec spec)
        {
            string sql = "SELECT COUNT(1) AS Record_Count FROM (" + Secure(ctx, spec) + ") CheckRows";

            DataSet ds = DB.ExecuteDataset(sql, spec.Params.ToArray(), null);
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) { return 0; }

            return Util.GetValueOfInt(ds.Tables[0].Rows[0]["Record_Count"]);
        }

        /// <summary>
        /// One page of a check's rows, materialised into the generic cell contract.
        ///
        /// Every declared column is read BY NAME from the result, so a spec that
        /// declares a column its SQL does not select yields an empty cell rather than an
        /// index-shifted row.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="spec">Detail specification.</param>
        /// <param name="pageNo">1-based page number (already clamped).</param>
        /// <param name="pageSize">Rows per page (already clamped).</param>
        /// <returns>Materialised rows (never null).</returns>
        private List<DetailRow> PageOf(Ctx ctx, DetailSpec spec, int pageNo, int pageSize)
        {
            List<DetailRow> rows = new List<DetailRow>();

            StringBuilder sql = new StringBuilder(Secure(ctx, spec));
            if (!string.IsNullOrEmpty(spec.OrderBy)) { sql.Append(" ORDER BY ").Append(spec.OrderBy); }
            sql.Append(PagingSuffix(pageSize, (pageNo - 1) * pageSize));

            DataSet ds = DB.ExecuteDataset(sql.ToString(), spec.Params.ToArray(), null);
            if (ds == null || ds.Tables.Count == 0) { return rows; }

            DataTable dt = ds.Tables[0];
            for (int i = 0; i < dt.Rows.Count; i++)
            {
                DataRow dr = dt.Rows[i];

                DetailRow row = new DetailRow();
                row.Cells = new Dictionary<string, object>();

                for (int c = 0; c < spec.Columns.Count; c++)
                {
                    ColumnDef column = spec.Columns[c];
                    if (!dt.Columns.Contains(column.Key)) { continue; }
                    row.Cells[column.Key] = NormalizeCell(dr[column.Key]);
                }

                /* Reserved technical aliases - present only on document-backed rows. */
                if (dt.Columns.Contains(TECH_TABLE)) { row.AD_Table_ID = Util.GetValueOfInt(dr[TECH_TABLE]); }
                if (dt.Columns.Contains(TECH_RECORD)) { row.Record_ID = Util.GetValueOfInt(dr[TECH_RECORD]); }
                if (dt.Columns.Contains(TECH_WINDOW)) { row.AD_Window_ID = Util.GetValueOfInt(dr[TECH_WINDOW]); }

                rows.Add(row);
            }

            return rows;
        }

        /// <summary>
        /// Converts one database value into something the JSON contract can carry
        /// unambiguously. DBNull becomes null; everything else keeps its own type so the
        /// client can format a date as a date and an amount as an amount.
        /// </summary>
        /// <param name="value">Raw column value.</param>
        /// <returns>Serialisable value, or null.</returns>
        private object NormalizeCell(object value)
        {
            if (value == null || value == DBNull.Value) { return null; }
            return value;
        }

        // ─────────────────────────────────────────────────────────────────────
        // §6  Source document resolution (shared by every check)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Fills the screen label and the document display value of every
        /// document-backed row on the page.
        ///
        /// Fact_Acct and the exception queries carry AD_Table_ID / Record_ID, not a
        /// document number, so the display value is fetched from the source table using
        /// Application Dictionary metadata rather than a hard-coded CASE over every
        /// transaction table. The page is GROUPED BY AD_Table_ID: two dictionary queries
        /// serve the whole page and then one query per distinct table fetches every
        /// record of it that the page needs. A page of any size costs (2 + tables)
        /// queries - never one per row.
        ///
        /// Nothing here originates from the browser: the table ids come out of the
        /// already-secured check query, and every name is re-validated by
        /// IsSafeIdentifier before it is concatenated.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="rows">Page rows to fill.</param>
        private void ResolveDocuments(Ctx ctx, List<DetailRow> rows)
        {
            if (ctx == null || rows == null || rows.Count == 0) { return; }

            Dictionary<int, List<int>> recordsByTable = new Dictionary<int, List<int>>();

            for (int i = 0; i < rows.Count; i++)
            {
                DetailRow row = rows[i];
                if (row.AD_Table_ID <= 0) { continue; }

                /* The last-resort fallback, applied up front so a document-backed row
                   can never come out of here with an empty document column. */
                if (row.Record_ID > 0)
                {
                    row.DocumentDisplayValue = "#" + row.Record_ID.ToString(CultureInfo.InvariantCulture);
                }

                if (row.Record_ID <= 0) { continue; }

                if (!recordsByTable.ContainsKey(row.AD_Table_ID))
                {
                    recordsByTable[row.AD_Table_ID] = new List<int>();
                }

                List<int> ids = recordsByTable[row.AD_Table_ID];
                if (!ids.Contains(row.Record_ID)) { ids.Add(row.Record_ID); }
            }

            if (recordsByTable.Count == 0) { return; }

            List<int> tableIds = new List<int>(recordsByTable.Keys);
            LoadSourceTables(tableIds);
            LoadDefaultWindows(ctx, rows, tableIds);
            LoadScreenNames(ctx, rows, tableIds);

            foreach (KeyValuePair<int, List<int>> entry in recordsByTable)
            {
                SourceTable table = FindSourceTable(entry.Key);
                if (table == null) { continue; }

                Dictionary<int, string> displayByRecord = ReadDisplayValues(ctx, table, entry.Value);
                if (displayByRecord == null || displayByRecord.Count == 0) { continue; }

                for (int i = 0; i < rows.Count; i++)
                {
                    if (rows[i].AD_Table_ID != entry.Key) { continue; }

                    string display;
                    if (!displayByRecord.TryGetValue(rows[i].Record_ID, out display)) { continue; }
                    if (string.IsNullOrEmpty(display)) { continue; }

                    rows[i].DocumentDisplayValue = display;
                    rows[i].IsRecordFound = true;
                }
            }
        }

        /// <summary>
        /// Gives every document-backed row a window to open, where its check did not
        /// name one.
        ///
        /// Unlike Fact_Acct, the transaction tables these checks read carry no
        /// AD_Window_ID - a purchase invoice row knows it is a C_Invoice, not which
        /// screen a user opens it on. Without this the document number would render as
        /// dead text on every check, so the table's primary window is resolved from the
        /// dictionary: the header tab (TabLevel 0) of an active, menu-reachable window
        /// over that table. Lowest window id breaks a tie, so the same installation
        /// always lands on the same screen.
        ///
        /// One query for the whole page. A table with no window simply keeps 0 and its
        /// rows stay non-navigable.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="rows">Page rows to fill.</param>
        /// <param name="tableIds">Distinct AD_Table_IDs on the page.</param>
        private void LoadDefaultWindows(Ctx ctx, List<DetailRow> rows, List<int> tableIds)
        {
            if (tableIds.Count == 0) { return; }

            List<SqlParameter> parameters = new List<SqlParameter>();
            string inList = BuildIdInList(tableIds, "@AD_Table_ID", parameters);

            string sql = @"
                SELECT tab.AD_Table_ID AS AD_Table_ID,
                       MIN(w.AD_Window_ID) AS AD_Window_ID
                FROM AD_Tab tab
                INNER JOIN AD_Window w ON (w.AD_Window_ID=tab.AD_Window_ID)
                INNER JOIN AD_Menu m ON (m.AD_Window_ID=w.AD_Window_ID)
                WHERE tab.IsActive='Y'
                  AND COALESCE(tab.TabLevel,0)=0
                  AND w.IsActive='Y'
                  AND m.IsActive='Y'
                  AND tab.AD_Table_ID IN (" + inList + @")
                GROUP BY tab.AD_Table_ID";

            Dictionary<int, int> windowByTable = new Dictionary<int, int>();

            DataSet ds = DB.ExecuteDataset(sql, parameters.ToArray(), null);
            if (ds != null && ds.Tables.Count > 0)
            {
                DataTable dt = ds.Tables[0];
                for (int i = 0; i < dt.Rows.Count; i++)
                {
                    int tableId = Util.GetValueOfInt(dt.Rows[i]["AD_Table_ID"]);
                    int windowId = Util.GetValueOfInt(dt.Rows[i]["AD_Window_ID"]);
                    if (tableId > 0 && windowId > 0) { windowByTable[tableId] = windowId; }
                }
            }

            for (int i = 0; i < rows.Count; i++)
            {
                DetailRow row = rows[i];

                /* A check that named its own window keeps it - that one is specific to
                   the posting, this one is only the table's default. */
                if (row.AD_Window_ID > 0 || row.AD_Table_ID <= 0) { continue; }

                int windowId;
                if (windowByTable.TryGetValue(row.AD_Table_ID, out windowId)) { row.AD_Window_ID = windowId; }
            }
        }

        /// <summary>
        /// Fills the ScreenDisplayName of every document-backed row, in one query.
        ///
        /// The label prefers the session language's window translation, then the
        /// window's DisplayName, then its Name, then the table's Name, then the physical
        /// TableName. Two names it deliberately does NOT reference:
        /// AD_Table.DisplayName and AD_Table_Trl.Name - neither exists in this schema
        /// (AD_Window is translated through AD_Window_Trl, AD_Table is not translated at
        /// all), so a table-sourced label is the untranslated dictionary name.
        ///
        /// A row whose check already supplied its own screen label keeps it: some checks
        /// span several source tables in one statement and name the screen themselves.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="rows">Page rows to fill.</param>
        /// <param name="tableIds">Distinct AD_Table_IDs on the page.</param>
        private void LoadScreenNames(Ctx ctx, List<DetailRow> rows, List<int> tableIds)
        {
            /* Window ids seen on the page, so a row can be labelled by the window its
               own posting names rather than by its table's default screen. */
            List<int> windowIds = new List<int>();
            for (int i = 0; i < rows.Count; i++)
            {
                int id = rows[i].AD_Window_ID;
                if (id > 0 && !windowIds.Contains(id)) { windowIds.Add(id); }
            }

            Dictionary<int, string> tableLabels = new Dictionary<int, string>();
            Dictionary<int, string> windowLabels = new Dictionary<int, string>();

            if (tableIds.Count > 0)
            {
                List<SqlParameter> parameters = new List<SqlParameter>();
                string inList = BuildIdInList(tableIds, "@AD_Table_ID", parameters);

                string sql = @"
                    SELECT t.AD_Table_ID AS AD_Table_ID,
                           COALESCE(t.Name,t.TableName,N'') AS Table_Label
                    FROM AD_Table t
                    WHERE t.IsActive='Y'
                      AND t.AD_Table_ID IN (" + inList + ")";

                DataSet ds = DB.ExecuteDataset(sql, parameters.ToArray(), null);
                if (ds != null && ds.Tables.Count > 0)
                {
                    DataTable dt = ds.Tables[0];
                    for (int i = 0; i < dt.Rows.Count; i++)
                    {
                        tableLabels[Util.GetValueOfInt(dt.Rows[i]["AD_Table_ID"])] =
                            Util.GetValueOfString(dt.Rows[i]["Table_Label"]);
                    }
                }
            }

            if (windowIds.Count > 0)
            {
                List<SqlParameter> parameters = new List<SqlParameter>();
                parameters.Add(new SqlParameter("@AD_Language", ctx.GetAD_Language()));
                string inList = BuildIdInList(windowIds, "@AD_Window_ID", parameters);

                string sql = @"
                    SELECT w.AD_Window_ID AS AD_Window_ID,
                           COALESCE(wtrl.Name,w.DisplayName,w.Name,N'') AS Window_Label
                    FROM AD_Window w
                    LEFT OUTER JOIN AD_Window_Trl wtrl ON (wtrl.AD_Window_ID=w.AD_Window_ID AND wtrl.AD_Language=@AD_Language AND wtrl.IsActive='Y')
                    WHERE w.IsActive='Y'
                      AND w.AD_Window_ID IN (" + inList + ")";

                DataSet ds = DB.ExecuteDataset(sql, parameters.ToArray(), null);
                if (ds != null && ds.Tables.Count > 0)
                {
                    DataTable dt = ds.Tables[0];
                    for (int i = 0; i < dt.Rows.Count; i++)
                    {
                        windowLabels[Util.GetValueOfInt(dt.Rows[i]["AD_Window_ID"])] =
                            Util.GetValueOfString(dt.Rows[i]["Window_Label"]);
                    }
                }
            }

            for (int i = 0; i < rows.Count; i++)
            {
                DetailRow row = rows[i];
                if (!string.IsNullOrEmpty(row.ScreenDisplayName)) { continue; }

                string label;
                if (row.AD_Window_ID > 0 && windowLabels.TryGetValue(row.AD_Window_ID, out label)
                    && !string.IsNullOrEmpty(label))
                {
                    row.ScreenDisplayName = label;
                    continue;
                }

                if (row.AD_Table_ID > 0 && tableLabels.TryGetValue(row.AD_Table_ID, out label)
                    && !string.IsNullOrEmpty(label))
                {
                    row.ScreenDisplayName = label;
                }
            }
        }

        /// <summary>
        /// Reads and caches the metadata of every source table the page touches: the
        /// physical name, the key column (the Zoom target), whether it carries
        /// DocumentNo / Value / AD_Client_ID, and its IsIdentifier columns in SeqNo
        /// order. Two queries for the whole page; already-cached tables are not re-read.
        /// </summary>
        /// <param name="tableIds">AD_Table_IDs seen on the page.</param>
        private void LoadSourceTables(List<int> tableIds)
        {
            if (_sourceTables == null) { _sourceTables = new Dictionary<int, SourceTable>(); }

            List<int> wanted = new List<int>();
            for (int i = 0; i < tableIds.Count; i++)
            {
                if (tableIds[i] > 0 && !_sourceTables.ContainsKey(tableIds[i])) { wanted.Add(tableIds[i]); }
            }

            if (wanted.Count == 0) { return; }

            /* The MAX(CASE ...) form gives one row per table however many columns it
               has. Views are excluded - there is no single record to open. */
            List<SqlParameter> parameters = new List<SqlParameter>();
            string inList = BuildIdInList(wanted, "@AD_Table_ID", parameters);

            string sql = @"
                SELECT t.AD_Table_ID AS AD_Table_ID,
                       t.TableName AS Table_Name,
                       MAX(CASE WHEN c.IsKey='Y' THEN c.ColumnName END) AS Key_Column,
                       MAX(CASE WHEN c.ColumnName='DocumentNo' THEN c.ColumnName END) AS Document_Column,
                       MAX(CASE WHEN c.ColumnName='Value' THEN c.ColumnName END) AS Value_Column,
                       MAX(CASE WHEN c.ColumnName='AD_Client_ID' THEN c.ColumnName END) AS Client_Column
                FROM AD_Table t
                INNER JOIN AD_Column c ON (c.AD_Table_ID=t.AD_Table_ID AND c.IsActive='Y')
                WHERE t.IsActive='Y'
                  /* COALESCE, not a bare comparison: AD_Table.IsView is NULLable and
                     unset on most tables, so 't.IsView=''N''' is NULL rather than true
                     and would resolve no source table at all. */
                  AND COALESCE(t.IsView,'N')='N'
                  AND t.AD_Table_ID IN (" + inList + @")
                GROUP BY t.AD_Table_ID,t.TableName";

            DataSet ds = DB.ExecuteDataset(sql, parameters.ToArray(), null);
            if (ds == null || ds.Tables.Count == 0) { return; }

            DataTable dt = ds.Tables[0];
            for (int i = 0; i < dt.Rows.Count; i++)
            {
                DataRow dr = dt.Rows[i];

                SourceTable table = new SourceTable();
                table.AD_Table_ID = Util.GetValueOfInt(dr["AD_Table_ID"]);
                table.TableName = Util.GetValueOfString(dr["Table_Name"]);
                table.KeyColumn = Util.GetValueOfString(dr["Key_Column"]);
                table.DocumentColumn = Util.GetValueOfString(dr["Document_Column"]);
                table.ValueColumn = Util.GetValueOfString(dr["Value_Column"]);
                table.ClientColumn = Util.GetValueOfString(dr["Client_Column"]);
                table.IdentifierColumns = new List<string>();

                /* No safe physical name or no key column means nothing can be read from
                   it; affected rows keep their #Record_ID fallback. */
                if (!IsSafeIdentifier(table.TableName) || !IsSafeIdentifier(table.KeyColumn)) { continue; }

                _sourceTables[table.AD_Table_ID] = table;
            }

            LoadIdentifierColumns(wanted);
        }

        /// <summary>
        /// Query 2 of the metadata pass: the IsIdentifier columns of every wanted table
        /// in SeqNo order - the dictionary's own answer to "what identifies a record of
        /// this table", used when a table has neither DocumentNo nor Value.
        /// </summary>
        /// <param name="tableIds">AD_Table_IDs being loaded.</param>
        private void LoadIdentifierColumns(List<int> tableIds)
        {
            List<SqlParameter> parameters = new List<SqlParameter>();
            string inList = BuildIdInList(tableIds, "@AD_Table_ID", parameters);

            string sql = @"
                SELECT c.AD_Table_ID AS AD_Table_ID,
                       c.ColumnName AS Column_Name
                FROM AD_Column c
                WHERE c.IsActive='Y'
                  AND c.IsIdentifier='Y'
                  AND c.AD_Table_ID IN (" + inList + @")
                ORDER BY c.AD_Table_ID,c.SeqNo,c.AD_Column_ID";

            DataSet ds = DB.ExecuteDataset(sql, parameters.ToArray(), null);
            if (ds == null || ds.Tables.Count == 0) { return; }

            DataTable dt = ds.Tables[0];
            for (int i = 0; i < dt.Rows.Count; i++)
            {
                int tableId = Util.GetValueOfInt(dt.Rows[i]["AD_Table_ID"]);
                string columnName = Util.GetValueOfString(dt.Rows[i]["Column_Name"]);

                SourceTable table = FindSourceTable(tableId);
                if (table == null) { continue; }
                if (!IsSafeIdentifier(columnName)) { continue; }
                if (table.IdentifierColumns.Count >= IDENTIFIER_COLUMN_MAX) { continue; }

                table.IdentifierColumns.Add(columnName);
            }
        }

        /// <summary>The cached metadata of one source table, or null.</summary>
        /// <param name="tableId">AD_Table_ID.</param>
        /// <returns>Cached <see cref="SourceTable"/>, or null when unusable.</returns>
        private SourceTable FindSourceTable(int tableId)
        {
            if (_sourceTables == null) { return null; }

            SourceTable table;
            return _sourceTables.TryGetValue(tableId, out table) ? table : null;
        }

        /// <summary>
        /// The display value of every wanted record of ONE source table, in one query.
        ///
        /// The read is scoped to the session tenant where the table carries an
        /// AD_Client_ID, and bounded by record ids that came out of the already
        /// role-filtered check query - the parent is what authorizes these rows, exactly
        /// as a lookup join would be.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="table">Validated source-table metadata.</param>
        /// <param name="recordIds">Record ids wanted from that table.</param>
        /// <returns>Record_ID -> display value (never null).</returns>
        private Dictionary<int, string> ReadDisplayValues(Ctx ctx, SourceTable table, List<int> recordIds)
        {
            Dictionary<int, string> result = new Dictionary<int, string>();
            if (table == null || recordIds == null || recordIds.Count == 0) { return result; }

            string displayExpr = BuildDisplayExpr(table);
            if (string.IsNullOrEmpty(displayExpr)) { return result; }

            List<SqlParameter> parameters = new List<SqlParameter>();

            StringBuilder sql = new StringBuilder();
            sql.Append("SELECT src.").Append(table.KeyColumn).Append(" AS Record_ID,")
               .Append(displayExpr).Append(" AS Display_Value")
               .Append(" FROM ").Append(table.TableName).Append(" src")
               .Append(" WHERE ");

            if (IsSafeIdentifier(table.ClientColumn))
            {
                sql.Append("src.").Append(table.ClientColumn).Append(" IN (0,@AD_Client_ID) AND ");
                parameters.Add(new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()));
            }

            sql.Append("src.").Append(table.KeyColumn).Append(" IN (")
               .Append(BuildIdInList(recordIds, "@Record_ID", parameters)).Append(")");

            DataSet ds;
            try
            {
                ds = DB.ExecuteDataset(sql.ToString(), parameters.ToArray(), null);
            }
            catch (Exception ex)
            {
                /* A source table the dictionary describes but the database cannot serve
                   this way must not take the modal down with it: affected rows keep
                   their #Record_ID fallback and stay readable. */
                Log.Log(Level.WARNING, "VAS_195: display lookup failed for " + table.TableName, ex);
                return result;
            }

            if (ds == null || ds.Tables.Count == 0) { return result; }

            DataTable dt = ds.Tables[0];
            for (int i = 0; i < dt.Rows.Count; i++)
            {
                int recordId = Util.GetValueOfInt(dt.Rows[i]["Record_ID"]);
                if (recordId <= 0) { continue; }

                result[recordId] = Util.GetValueOfString(dt.Rows[i]["Display_Value"]);
            }

            return result;
        }

        /// <summary>
        /// The display expression for one source table, in the documented resolution
        /// order: the active DocumentNo column, else the active Value column, else the
        /// IsIdentifier columns in SeqNo order.
        ///
        /// Several identifiers are joined with the ANSI concatenation operator, which
        /// both PostgreSQL and Oracle accept. Each part is CAST to a character type
        /// first, because an identifier column is not necessarily text and PostgreSQL
        /// will not concatenate two non-text values; TRIM removes the blank padding a
        /// fixed-width CAST introduces; and COALESCE guards the concatenation, since on
        /// both backends one NULL part would otherwise make the WHOLE value NULL.
        /// </summary>
        /// <param name="table">Validated source-table metadata.</param>
        /// <returns>SQL expression over the `src` alias, or "" when nothing is usable.</returns>
        private string BuildDisplayExpr(SourceTable table)
        {
            if (IsSafeIdentifier(table.DocumentColumn)) { return "src." + table.DocumentColumn; }
            if (IsSafeIdentifier(table.ValueColumn)) { return "src." + table.ValueColumn; }

            List<string> columns = table.IdentifierColumns;

            if (columns != null && columns.Count == 1) { return "src." + columns[0]; }

            if (columns != null && columns.Count > 1)
            {
                StringBuilder expr = new StringBuilder();
                for (int i = 0; i < columns.Count; i++)
                {
                    if (i > 0) { expr.Append(" || ' - ' || "); }
                    expr.Append("COALESCE(TRIM(CAST(src.").Append(columns[i]).Append(" AS CHAR(")
                        .Append(IDENTIFIER_MAX).Append("))),'')");
                }
                return expr.ToString();
            }

            /* Nothing identifies the record but its key - which is what the caller's
               #Record_ID fallback already says. */
            return "";
        }

        /// <summary>
        /// Decides which rows may be clicked through to their source screen.
        ///
        /// All four conditions must hold: an active window resolved, the role may open
        /// it, the row has a record to position on, and the source table has a key
        /// column to position BY. A row failing any of them still shows its screen label
        /// and its document value - it simply does not navigate.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="rows">Page rows to flag.</param>
        private void ApplyNavigability(Ctx ctx, List<DetailRow> rows)
        {
            if (ctx == null || rows == null) { return; }

            /* One access answer per window, however many rows share it. */
            Dictionary<int, bool> accessByWindow = new Dictionary<int, bool>();

            for (int i = 0; i < rows.Count; i++)
            {
                DetailRow row = rows[i];

                if (row.AD_Window_ID <= 0 || row.Record_ID <= 0) { continue; }

                SourceTable table = FindSourceTable(row.AD_Table_ID);
                if (table == null || !IsSafeIdentifier(table.KeyColumn)) { continue; }

                bool allowed;
                if (!accessByWindow.TryGetValue(row.AD_Window_ID, out allowed))
                {
                    allowed = HasWindowAccess(ctx, row.AD_Window_ID);
                    accessByWindow[row.AD_Window_ID] = allowed;
                }

                if (!allowed) { continue; }

                row.KeyColumnName = table.KeyColumn;
                row.CanNavigate = true;
            }
        }

        /// <summary>
        /// Whether the current role may open one window. Best-effort: a role that cannot
        /// be interrogated is treated as having no access, so a modal row never offers a
        /// link the framework would then refuse.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="windowId">AD_Window_ID to test.</param>
        /// <returns>true when the role may open the window.</returns>
        private bool HasWindowAccess(Ctx ctx, int windowId)
        {
            try
            {
                return MRole.GetDefault(ctx, false).GetWindowAccess(windowId) ?? false;
            }
            catch (Exception ex)
            {
                Log.Log(Level.WARNING, "VAS_195: window access check failed for AD_Window_ID=" + windowId, ex);
                return false;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // §7  Dictionary probes (optional modules and columns)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Whether a physical table exists and is active in this installation.
        ///
        /// Several checks target module tables (fixed assets, revaluation staging,
        /// localization matching) that are simply absent when the module is not
        /// installed. Probing the dictionary is what lets such a check answer
        /// NOT_APPLICABLE instead of failing the whole checklist with a missing-relation
        /// error. Cached per request.
        /// </summary>
        /// <param name="tableName">Physical table name from a server-side registry.</param>
        /// <returns>true when the table is present and active.</returns>
        protected bool TableExists(string tableName)
        {
            if (!IsSafeIdentifier(tableName)) { return false; }

            if (_tableExists == null)
            {
                _tableExists = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            }

            bool cached;
            if (_tableExists.TryGetValue(tableName, out cached)) { return cached; }

            string sql = @"
                SELECT COUNT(1) AS Table_Count
                FROM AD_Table t
                WHERE t.TableName=@TableName
                  AND t.IsActive='Y'";

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@TableName", tableName)
            };

            bool exists = false;
            DataSet ds = DB.ExecuteDataset(sql, parameters, null);
            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                exists = Util.GetValueOfInt(ds.Tables[0].Rows[0]["Table_Count"]) > 0;
            }

            _tableExists[tableName] = exists;
            return exists;
        }

        /// <summary>
        /// The active column names of one dictionary table, cached per request. Used to
        /// confirm an OPTIONAL column exists before it is named in generated SQL - a
        /// column that is not there would fail the whole statement.
        /// </summary>
        /// <param name="tableName">Physical table name from a server-side registry.</param>
        /// <returns>Column names (never null).</returns>
        protected List<string> TableColumns(string tableName)
        {
            List<string> columns = new List<string>();
            if (!IsSafeIdentifier(tableName)) { return columns; }

            if (_tableColumns == null)
            {
                _tableColumns = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            }

            List<string> cached;
            if (_tableColumns.TryGetValue(tableName, out cached)) { return cached; }

            string sql = @"
                SELECT c.ColumnName AS Column_Name
                FROM AD_Column c
                INNER JOIN AD_Table t ON (t.AD_Table_ID=c.AD_Table_ID)
                WHERE t.TableName=@TableName
                  AND t.IsActive='Y'
                  AND c.IsActive='Y'";

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@TableName", tableName)
            };

            DataSet ds = DB.ExecuteDataset(sql, parameters, null);
            if (ds != null && ds.Tables.Count > 0)
            {
                DataTable dt = ds.Tables[0];
                for (int i = 0; i < dt.Rows.Count; i++)
                {
                    columns.Add(Util.GetValueOfString(dt.Rows[i]["Column_Name"]));
                }
            }

            _tableColumns[tableName] = columns;
            return columns;
        }

        /// <summary>Whether a table carries a given active column.</summary>
        /// <param name="tableName">Physical table name.</param>
        /// <param name="columnName">Column being looked for.</param>
        /// <returns>true when present.</returns>
        protected bool ColumnExists(string tableName, string columnName)
        {
            return HasColumn(TableColumns(tableName), columnName);
        }

        /// <summary>Case-insensitive membership test over a column-name list.</summary>
        /// <param name="columns">Known column names.</param>
        /// <param name="name">Column being looked for.</param>
        /// <returns>true when present.</returns>
        protected bool HasColumn(List<string> columns, string name)
        {
            if (columns == null || string.IsNullOrEmpty(name)) { return false; }

            for (int i = 0; i < columns.Count; i++)
            {
                if (name.Equals(columns[i], StringComparison.OrdinalIgnoreCase)) { return true; }
            }

            return false;
        }

        /// <summary>
        /// The AD_Table_ID of a physical table, for the technical navigation fields.
        /// Resolved from the dictionary rather than hard-coded, since surrogate ids
        /// differ between installations.
        /// </summary>
        /// <param name="tableName">Physical table name from a server-side registry.</param>
        /// <returns>AD_Table_ID, or 0 when the table is absent.</returns>
        protected int TableId(string tableName)
        {
            if (!IsSafeIdentifier(tableName)) { return 0; }

            string sql = @"
                SELECT t.AD_Table_ID AS AD_Table_ID
                FROM AD_Table t
                WHERE t.TableName=@TableName
                  AND t.IsActive='Y'";

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@TableName", tableName)
            };

            DataSet ds = DB.ExecuteDataset(sql, parameters, null);
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) { return 0; }

            return Util.GetValueOfInt(ds.Tables[0].Rows[0]["AD_Table_ID"]);
        }

        /// <summary>
        /// The LEFT JOIN pair that resolves ONE list column to its translated name, in
        /// the form <see cref="VAS_195_MandatoryChecklistModel"/>'s checks already use.
        ///
        /// A list column stores a short code ('M', 'I', 'CO') and the readable text lives
        /// in AD_Ref_List, translated through AD_Ref_List_Trl. A check that shows the
        /// stored code is showing an internal value, not a name.
        ///
        /// The reference id is resolved INSIDE the statement, from AD_Column, rather than
        /// passed in as a literal: reference ids are surrogate keys and differ between
        /// installations, so a constant would be right on one and silently wrong on the
        /// next. Both joins are OUTER, so a code with no reference row at all still shows
        /// as the raw code rather than dropping the row.
        ///
        /// Aliases are supplied by the caller because one statement may resolve several
        /// list columns and each pair needs its own.
        /// </summary>
        /// <param name="listAlias">Alias for the AD_Ref_List row.</param>
        /// <param name="trlAlias">Alias for its translation row.</param>
        /// <param name="valueExpr">Qualified column holding the stored code.</param>
        /// <param name="tableName">Physical table the column belongs to.</param>
        /// <param name="columnName">The list column's name.</param>
        /// <param name="languageBind">Bind holding the session language, unique to this occurrence.</param>
        /// <returns>Two LEFT OUTER JOIN clauses, or "" when the names are unsafe.</returns>
        protected string ListNameJoin(string listAlias, string trlAlias, string valueExpr,
            string tableName, string columnName, string languageBind)
        {
            if (!IsSafeIdentifier(tableName) || !IsSafeIdentifier(columnName)) { return ""; }
            if (!IsSafeIdentifier(listAlias) || !IsSafeIdentifier(trlAlias)) { return ""; }

            return " LEFT OUTER JOIN AD_Ref_List " + listAlias
                 + " ON (" + listAlias + ".Value=" + valueExpr
                 + " AND " + listAlias + ".IsActive='Y'"
                 + " AND " + listAlias + ".AD_Reference_ID=(SELECT c2.AD_Reference_Value_ID FROM AD_Column c2"
                 + " INNER JOIN AD_Table t2 ON (t2.AD_Table_ID=c2.AD_Table_ID)"
                 + " WHERE t2.TableName='" + tableName + "' AND c2.ColumnName='" + columnName + "'"
                 + " AND c2.IsActive='Y'))"
                 + " LEFT OUTER JOIN AD_Ref_List_Trl " + trlAlias
                 + " ON (" + trlAlias + ".AD_Ref_List_ID=" + listAlias + ".AD_Ref_List_ID"
                 + " AND " + trlAlias + ".AD_Language=" + languageBind
                 + " AND " + trlAlias + ".IsActive='Y')";
        }

        // ─────────────────────────────────────────────────────────────────────
        // §8  Shared helpers
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>The period's inclusive lower date bound at midnight.</summary>
        /// <param name="period">Validated period.</param>
        /// <returns>Start timestamp.</returns>
        private DateTime PeriodStart(PeriodItem period)
        {
            return period.StartDate.HasValue ? period.StartDate.Value.Date : DateTime.MinValue;
        }

        /// <summary>
        /// The day AFTER the period's EndDate, at midnight.
        ///
        /// Exclusive on purpose: a document stamped 31-May 14:30 belongs to May, and an
        /// inclusive "&lt;=EndDate" comparison would drop it on any column that carries a
        /// time. This is what lets every check filter with a plain
        /// "&gt;=@PeriodStart AND &lt;@PeriodEndExclusive" and stay correct on both backends
        /// without TRUNC or a cast.
        /// </summary>
        /// <param name="period">Validated period.</param>
        /// <returns>End-exclusive timestamp.</returns>
        private DateTime PeriodEndExclusive(PeriodItem period)
        {
            if (!period.EndDate.HasValue) { return DateTime.MaxValue.Date; }
            return period.EndDate.Value.Date.AddDays(1);
        }

        /// <summary>
        /// The rounding tolerance implied by the accounting currency: 10^-precision.
        /// Anything smaller is a representation artefact, not a difference.
        /// </summary>
        /// <param name="precision">C_Currency.StdPrecision.</param>
        /// <returns>Tolerance amount.</returns>
        private decimal PrecisionTolerance(int precision)
        {
            if (precision < 0 || precision > 10) { precision = 2; }

            decimal tolerance = 1;
            for (int i = 0; i < precision; i++) { tolerance = tolerance / 10; }
            return tolerance;
        }

        /// <summary>
        /// Expands an id list into a parameterised IN list and appends the binds in
        /// order. Each id gets its own uniquely named bind: a repeated name is ambiguous
        /// under the positional binding the backend adapters use, and a comma-separated
        /// literal would not be parameterised at all.
        /// </summary>
        /// <param name="ids">Server-derived ids (never client text).</param>
        /// <param name="prefix">Bind-name stem, e.g. "@Account_ID".</param>
        /// <param name="parameters">Bind list being built, in appearance order.</param>
        /// <returns>The IN-list body.</returns>
        protected string BuildIdInList(List<int> ids, string prefix, List<SqlParameter> parameters)
        {
            StringBuilder list = new StringBuilder();

            for (int i = 0; i < ids.Count; i++)
            {
                string name = prefix + (i + 1).ToString(CultureInfo.InvariantCulture);
                if (i > 0) { list.Append(","); }
                list.Append(name);
                parameters.Add(new SqlParameter(name, ids[i]));
            }

            return list.ToString();
        }

        /// <summary>
        /// Guards every identifier that reaches generated SQL. The names come from the
        /// Application Dictionary and from server-side registries rather than from the
        /// client, so this is belt-and-braces - but a dictionary is data, and data can
        /// be edited, so nothing is concatenated that is not a plain unqualified
        /// identifier.
        /// </summary>
        /// <param name="identifier">Table or column name.</param>
        /// <returns>true when the name is safe to concatenate.</returns>
        protected bool IsSafeIdentifier(string identifier)
        {
            if (string.IsNullOrEmpty(identifier)) { return false; }
            if (identifier.Length > IDENTIFIER_MAX) { return false; }

            char first = identifier[0];
            if (!((first >= 'A' && first <= 'Z') || (first >= 'a' && first <= 'z'))) { return false; }

            for (int i = 1; i < identifier.Length; i++)
            {
                char ch = identifier[i];
                bool ok = (ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z')
                       || (ch >= '0' && ch <= '9') || ch == '_';
                if (!ok) { return false; }
            }

            return true;
        }

        /// <summary>
        /// Database-specific paging suffix: OFFSET / FETCH on Oracle, LIMIT / OFFSET
        /// elsewhere. Both values are server-clamped integers, never client text.
        /// </summary>
        /// <param name="pageSize">Rows to fetch.</param>
        /// <param name="offset">Rows to skip.</param>
        /// <returns>Paging clause.</returns>
        protected string PagingSuffix(int pageSize, int offset)
        {
            if (pageSize <= 0) { pageSize = PAGESIZE_DEFAULT; }
            if (offset < 0) { offset = 0; }

            if (DB.IsOracle())
            {
                return " OFFSET " + offset + " ROWS FETCH NEXT " + pageSize + " ROWS ONLY";
            }
            return " LIMIT " + pageSize + " OFFSET " + offset;
        }

        // ─────────────────────────────────────────────────────────────────────
        // §9  Result builders used by the 23 handlers
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>A blank result carrying the registry's own identity.</summary>
        /// <param name="def">Registry entry.</param>
        /// <returns>Initialised <see cref="CheckResult"/>.</returns>
        private CheckResult NewResult(CheckDef def)
        {
            CheckResult result = new CheckResult();
            result.CheckCode = def.CheckCode;
            result.Sequence = def.Sequence;
            result.Classification = def.Classification;
            result.TitleKey = def.TitleKey;
            result.TitleText = def.TitleText;
            result.IsApplicable = true;
            return result;
        }

        /// <summary>
        /// The ordinary outcome of an exception-counting check: the classification
        /// decides what a non-zero count MEANS. A blocker fails, a warning warns, and a
        /// positive check reports completeness - so a handler only has to count, and the
        /// registry decides the consequence.
        /// </summary>
        /// <param name="def">Registry entry.</param>
        /// <param name="count">Exception rows found.</param>
        /// <param name="summaryKey">AD_Message key for the non-zero summary.</param>
        /// <param name="summaryText">English fallback for the non-zero summary.</param>
        /// <param name="clearKey">AD_Message key for the zero summary.</param>
        /// <param name="clearText">English fallback for the zero summary.</param>
        /// <returns>Populated <see cref="CheckResult"/>.</returns>
        protected CheckResult Counted(CheckDef def, int count, string summaryKey, string summaryText,
            string clearKey, string clearText)
        {
            CheckResult result = NewResult(def);
            result.RecordCount = count;
            result.DetailAvailable = count > 0;

            if (count > 0)
            {
                result.SummaryKey = summaryKey;
                result.SummaryText = summaryText;

                if (CLASS_BLOCKER.Equals(def.Classification))
                {
                    result.Status = STATUS_FAIL;
                    result.IsBlocking = true;
                }
                else if (CLASS_WARNING.Equals(def.Classification))
                {
                    result.Status = STATUS_WARNING;
                }
                else
                {
                    result.Status = STATUS_INCOMPLETE;
                }
            }
            else
            {
                result.SummaryKey = clearKey;
                result.SummaryText = clearText;
                result.Status = CLASS_CHECK.Equals(def.Classification) ? STATUS_COMPLETE : STATUS_PASS;
            }

            return result;
        }

        /// <summary>
        /// A check that genuinely does not apply - the module is not installed, or there
        /// is nothing of the kind it examines. Never a PASS: "we did not look" and "we
        /// looked and it was clean" are different answers, and only one of them is
        /// evidence.
        /// </summary>
        /// <param name="def">Registry entry.</param>
        /// <param name="reasonKey">AD_Message key explaining why.</param>
        /// <param name="reasonText">English fallback.</param>
        /// <returns>Populated <see cref="CheckResult"/>.</returns>
        protected CheckResult NotApplicable(CheckDef def, string reasonKey, string reasonText)
        {
            CheckResult result = NewResult(def);
            result.Status = STATUS_NOT_APPLICABLE;
            result.IsApplicable = false;
            result.SummaryKey = reasonKey;
            result.SummaryText = reasonText;
            result.DetailAvailable = false;
            return result;
        }

        /// <summary>
        /// A check whose required setup is missing or broken. On a BLOCKER this prevents
        /// close, deliberately: a control that cannot be evaluated is not a control that
        /// passed.
        /// </summary>
        /// <param name="def">Registry entry.</param>
        /// <param name="reasonKey">AD_Message key explaining what is missing.</param>
        /// <param name="reasonText">English fallback.</param>
        /// <returns>Populated <see cref="CheckResult"/>.</returns>
        protected CheckResult Configured(CheckDef def, string reasonKey, string reasonText)
        {
            CheckResult result = NewResult(def);
            result.Status = STATUS_CONFIGURATION_ERROR;
            result.SummaryKey = reasonKey;
            result.SummaryText = reasonText;
            result.IsBlocking = CLASS_BLOCKER.Equals(def.Classification);
            result.DetailAvailable = false;
            return result;
        }

        /// <summary>Declares one column of a check's detail grid.</summary>
        /// <param name="key">SELECT alias the value is read from.</param>
        /// <param name="labelKey">AD_Message key for the caption.</param>
        /// <param name="labelText">English fallback caption.</param>
        /// <param name="type">COLTYPE_* token driving client formatting.</param>
        /// <param name="weight">Relative grid width.</param>
        /// <returns>Populated <see cref="ColumnDef"/>.</returns>
        protected ColumnDef Col(string key, string labelKey, string labelText, string type, decimal weight)
        {
            ColumnDef column = new ColumnDef();
            column.Key = key;
            column.LabelKey = labelKey;
            column.LabelText = labelText;
            column.Type = type;
            column.Weight = weight;
            return column;
        }

        // ─────────────────────────────────────────────────────────────────────
        // §10 Contracts
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>The tenant's primary calendar, accounting schema and currency.</summary>
        public class AcctContext
        {
            public int C_Calendar_ID { get; set; }
            public int C_AcctSchema_ID { get; set; }
            public string Name { get; set; }
            public int C_Currency_ID { get; set; }
            public string Iso { get; set; }
            public string Symbol { get; set; }
            public int Precision { get; set; }
        }

        /// <summary>One selectable open standard period of the primary calendar.</summary>
        public class PeriodItem
        {
            public int C_Period_ID { get; set; }
            public string Name { get; set; }
            public DateTime? StartDate { get; set; }
            public DateTime? EndDate { get; set; }
            public int C_Year_ID { get; set; }
            public string FiscalYear { get; set; }
            public int C_Calendar_ID { get; set; }
        }

        /// <summary>One entry of the server-side check registry.</summary>
        public class CheckDef
        {
            public string CheckCode { get; set; }
            public int Sequence { get; set; }

            /// <summary>CLASS_* token; the client resolves the label.</summary>
            public string Classification { get; set; }

            public string TitleKey { get; set; }
            public string TitleText { get; set; }
        }

        /// <summary>Everything shared by the 23 handlers for one evaluation.</summary>
        protected class CheckContext
        {
            public Ctx Ctx { get; set; }
            public AcctContext Acct { get; set; }
            public PeriodItem Period { get; set; }

            /// <summary>Inclusive lower bound for every date filter.</summary>
            public DateTime PeriodStart { get; set; }

            /// <summary>Exclusive upper bound for every date filter.</summary>
            public DateTime PeriodEndExclusive { get; set; }

            /// <summary>Currency-precision rounding tolerance.</summary>
            public decimal Tolerance { get; set; }
        }

        /// <summary>One evaluated checklist row.</summary>
        public class CheckResult
        {
            public string CheckCode { get; set; }
            public int Sequence { get; set; }

            /// <summary>CLASS_* token.</summary>
            public string Classification { get; set; }

            public string TitleKey { get; set; }
            public string TitleText { get; set; }

            /// <summary>STATUS_* token.</summary>
            public string Status { get; set; }

            /// <summary>true only for a BLOCKER that fails or cannot be evaluated.</summary>
            public bool IsBlocking { get; set; }

            public bool IsApplicable { get; set; }

            /// <summary>Exception / detail rows behind the row.</summary>
            public int RecordCount { get; set; }

            /// <summary>Distinct source documents, when the detail is line-level.</summary>
            public int DocumentCount { get; set; }

            /// <summary>Accounting-schema-currency figure, where the check has one.</summary>
            public decimal? Amount { get; set; }

            /// <summary>AD_Message key for the secondary summary line.</summary>
            public string SummaryKey { get; set; }

            /// <summary>English fallback for the secondary summary line.</summary>
            public string SummaryText { get; set; }

            /// <summary>true when clicking the row can produce records.</summary>
            public bool DetailAvailable { get; set; }
        }

        /// <summary>The checklist for one period, plus the close verdict.</summary>
        public class PeriodData
        {
            public int C_Period_ID { get; set; }
            public string PeriodName { get; set; }
            public DateTime? PeriodStartDate { get; set; }
            public DateTime? PeriodEndDate { get; set; }
            public string FiscalYear { get; set; }

            public AcctContext Schema { get; set; }

            public List<CheckResult> Items { get; set; }

            /// <summary>false while any BLOCKER is FAIL or CONFIGURATION_ERROR.</summary>
            public bool CloseAllowed { get; set; }

            public int BlockerFailCount { get; set; }
            public int WarningCount { get; set; }
            public int CheckCompleteCount { get; set; }

            public List<string> Messages { get; set; }

            /// <summary>One of the ERROR_* tokens; the client resolves the label.</summary>
            public string ErrorCode { get; set; }
        }

        /// <summary>Accounting context, period list, default selection and its checklist.</summary>
        public class ChecklistBootstrap
        {
            public AcctContext Schema { get; set; }
            public List<PeriodItem> Periods { get; set; }
            public int C_Period_ID { get; set; }
            public string PeriodName { get; set; }
            public PeriodData Data { get; set; }

            /// <summary>One of the ERROR_* tokens; the client resolves the label.</summary>
            public string ErrorCode { get; set; }
        }

        /// <summary>One declared column of a check's detail grid.</summary>
        public class ColumnDef
        {
            /// <summary>SELECT alias the cell value is read from.</summary>
            public string Key { get; set; }

            public string LabelKey { get; set; }
            public string LabelText { get; set; }

            /// <summary>COLTYPE_* token driving client-side formatting and alignment.</summary>
            public string Type { get; set; }

            /// <summary>Relative grid width.</summary>
            public decimal Weight { get; set; }
        }

        /// <summary>One detail row: declared cells plus the technical navigation fields.</summary>
        public class DetailRow
        {
            /// <summary>Column key -> value, for the columns the check declared.</summary>
            public Dictionary<string, object> Cells { get; set; }

            public int AD_Table_ID { get; set; }
            public int AD_Window_ID { get; set; }
            public int Record_ID { get; set; }

            /// <summary>The source table's key column - what the Zoom positions by.</summary>
            public string KeyColumnName { get; set; }

            /// <summary>Window name where one resolved, else the table's.</summary>
            public string ScreenDisplayName { get; set; }

            /// <summary>Resolved from the source record, else #Record_ID.</summary>
            public string DocumentDisplayValue { get; set; }

            /// <summary>true when the source record was actually found.</summary>
            public bool IsRecordFound { get; set; }

            /// <summary>true only when the row can really be opened.</summary>
            public bool CanNavigate { get; set; }
        }

        /// <summary>One page of a check's detail plus the paging state.</summary>
        public class DetailPage
        {
            public string CheckCode { get; set; }
            public string Classification { get; set; }
            public int C_Period_ID { get; set; }
            public string PeriodName { get; set; }

            public AcctContext Schema { get; set; }

            public List<ColumnDef> Columns { get; set; }
            public List<DetailRow> Rows { get; set; }

            public int Total { get; set; }
            public int PageNo { get; set; }
            public int PageSize { get; set; }

            /// <summary>One of the ERROR_* tokens; the client resolves the label.</summary>
            public string ErrorCode { get; set; }
        }

        /// <summary>
        /// One check's detail statement: the SQL, the ONE physical alias MRole is
        /// applied to, the sort, the binds and the declared columns.
        /// </summary>
        protected class DetailSpec
        {
            /// <summary>Plain physical-table SELECT - no ORDER BY, no paging, no wrapper.</summary>
            public string Sql { get; set; }

            /// <summary>The main physical table alias; "" for a dictionary/config read.</summary>
            public string MainAlias { get; set; }

            /// <summary>
            /// GROUP BY body for an aggregating check, WITHOUT the keyword. Held apart
            /// from Sql on purpose: it has to be appended AFTER AddAccessSQL, or the
            /// access parser meets a trailing clause where it expects the FROM list.
            /// </summary>
            public string GroupBy { get; set; }

            public string OrderBy { get; set; }
            public List<SqlParameter> Params { get; set; }
            public List<ColumnDef> Columns { get; set; }
        }

        /// <summary>
        /// Dictionary metadata of one source table, cached per request. Every name here
        /// has passed IsSafeIdentifier before use.
        /// </summary>
        private class SourceTable
        {
            public int AD_Table_ID { get; set; }
            public string TableName { get; set; }
            public string KeyColumn { get; set; }
            public string DocumentColumn { get; set; }
            public string ValueColumn { get; set; }
            public string ClientColumn { get; set; }
            public List<string> IdentifierColumns { get; set; }
        }
    }
}
