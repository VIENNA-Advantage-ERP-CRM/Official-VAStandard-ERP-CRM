/******************************************************
 * Module Name    : VASLogic
 * Purpose        : Suspense Balances dashboard widget data
 * chronological  : Development
 * Created Date   : 2026-08-24
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
    /// Module Name : VAS_202_SuspenseBalances
    /// Purpose     : Backs the VAS_202_SuspenseBalancesWidget dashboard widget.
    ///               The four CONTROL accounts of the tenant's primary accounting
    ///               schema that should carry nothing at all, each with the number of
    ///               postings sitting on it in ONE open accounting period and its net
    ///               balance, and each openable as a paged list of those postings:
    ///
    ///                 Suspense Balancing   C_AcctSchema_GL.SuspenseBalancing_Acct
    ///                 Suspense Error       C_AcctSchema_GL.SuspenseError_Acct
    ///                 Currency Balancing   C_AcctSchema_GL.CurrencyBalancing_Acct
    ///                 Rounding Off         C_AcctSchema_GL.FRPT_RoundingOff_Acct
    ///
    ///               Currency Balancing sits right after Suspense Error because it is
    ///               the same kind of finding: Fact.cs posts the rounding difference of
    ///               a multi-currency document to it when the schema has
    ///               UseCurrencyBalancing set, so a non-zero balance there is money to
    ///               explain, not a normal operating account.
    ///
    ///               Resolution chain, deliberately three hops long: those four
    ///               settings hold a C_ValidCombination_ID, NOT a Fact_Acct.Account_ID,
    ///               so each is resolved
    ///
    ///                 C_AcctSchema_GL.&lt;setting&gt;
    ///                   -> C_ValidCombination.C_ValidCombination_ID
    ///                   -> C_ValidCombination.Account_ID
    ///                   -> C_ElementValue (Value / Name shown on the card)
    ///
    ///               and only the resolved NATURAL account id is ever compared with
    ///               Fact_Acct.Account_ID. Comparing the setting directly would match
    ///               nothing (or, worse, the wrong account).
    ///
    ///               Period source: the open STANDARD periods (C_Period.PeriodType='S')
    ///               of the tenant's PRIMARY calendar (AD_ClientInfo.C_Calendar_ID) - a
    ///               period qualifies when at least one active C_PeriodControl row of it
    ///               is Open. Suspense postings are not confined to one document base
    ///               type, so every control row does NOT have to be open. Nothing is
    ///               derived from the calendar month, and Fact_Acct is bounded by
    ///               C_Period_ID, never by a date range.
    ///
    ///               Amounts are read as AmtAcctDr / AmtAcctCr, which are ALREADY in
    ///               the primary accounting-schema currency - there is deliberately no
    ///               currencyConvert call anywhere in this model.
    ///
    ///               The card total is SUM(ABS(net balance)) over the DISTINCT resolved
    ///               account ids: one suspense account is never netted against another
    ///               (they are different errors), and an account configured into two of
    ///               the four settings is counted once.
    ///
    ///               Document numbers: Fact_Acct stores AD_Table_ID / Record_ID, not a
    ///               document number, so each posting's source is resolved from
    ///               Application Dictionary metadata (AD_Table + AD_Column) rather than
    ///               from a hard-coded table-by-table CASE - DocumentNo, else Value,
    ///               else the IsIdentifier columns in SeqNo order, else #Record_ID. The
    ///               rows of one page are GROUPED BY AD_Table_ID and resolved one query
    ///               per table (two dictionary queries for the whole page, whatever its
    ///               size), never one query per row. Every table and column name is
    ///               re-validated by IsSafeIdentifier before it is concatenated, and no
    ///               name ever originates from the browser.
    ///
    ///               MRole row-level security is applied to the main physical table of
    ///               each user-facing query: C_Period alias p, Fact_Acct alias fa. It is
    ///               never applied to a derived alias or to a combined statement, and
    ///               GROUP BY / ORDER BY / the paging suffix are appended AFTER
    ///               AddAccessSQL so its FROM-clause parser is not confused by a
    ///               trailing clause. The joined AD_Table / AD_Window / C_ElementValue
    ///               rows are dictionary and reference lookups and inherit the parent's
    ///               filter; the source-document reads are bounded by record ids that
    ///               came out of the already-secured Fact_Acct query. Compatible with
    ///               PostgreSQL and Oracle.
    /// Chronological development:
    ///   VAI145      2026-08-24 Created
    /// </summary>
    public class VAS_202_SuspenseBalancesModel
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_202_SuspenseBalancesModel).FullName);

        /* Account-type tokens exchanged with the client. The client maps each to a
           localized AD_Message label, so no display text is produced here. */
        public const string ACCOUNTTYPE_SUSPENSE_BALANCING = "SuspenseBalancing";
        public const string ACCOUNTTYPE_SUSPENSE_ERROR = "SuspenseError";
        public const string ACCOUNTTYPE_CURRENCY_BALANCING = "CurrencyBalancing";
        public const string ACCOUNTTYPE_ROUNDING_OFF = "RoundingOff";

        /* Dr / Cr tokens for the detail list. */
        public const string DRCR_DEBIT = "Debit";
        public const string DRCR_CREDIT = "Credit";

        /* Error tokens; the client resolves the label. */
        public const string ERROR_INVALID_REQUEST = "INVALID";
        public const string ERROR_NO_PERIOD = "NOPERIOD";
        public const string ERROR_NO_CALENDAR = "NOCALENDAR";
        public const string ERROR_NO_ACCTSCHEMA = "NOACCTSCHEMA";

        /* Warning tokens. A warning never blocks the card - it explains a row that
           cannot be drilled into. */
        public const string WARNING_NOT_CONFIGURED = "NOTCONFIGURED";
        public const string WARNING_UNRESOLVED = "UNRESOLVED";
        public const string WARNING_DUPLICATE_ACCOUNT = "DUPLICATE";

        /* C_PeriodControl.PeriodStatus stored code for an open control row. */
        private const string PERIODSTATUS_Open = "O";

        /* C_Period.PeriodType stored code for a standard (non-adjustment) period. */
        private const string PERIODTYPE_Standard = "S";

        /* Fact_Acct.PostingType stored code for Actual. Budget / commitment postings
           are not suspense to clear. */
        private const string POSTINGTYPE_Actual = "A";

        /* The four C_AcctSchema_GL settings, in card display order. SuspenseBalancing /
           SuspenseError / CurrencyBalancing are standard columns of the table (MSetup
           seeds all three), so they are named directly. FRPT_RoundingOff_Acct is an
           optional column in this schema (MAcctSchema probes it with Get_ColumnIndex
           before reading it), so its presence is confirmed against AD_Column before it
           is named in any SQL. */
        private const string COLUMN_SUSPENSE_BALANCING = "SuspenseBalancing_Acct";
        private const string COLUMN_SUSPENSE_ERROR = "SuspenseError_Acct";
        private const string COLUMN_CURRENCY_BALANCING = "CurrencyBalancing_Acct";
        private const string COLUMN_ROUNDING_OFF = "FRPT_RoundingOff_Acct";
        private const string COLUMN_USE_SUSPENSE_BALANCING = "UseSuspenseBalancing";
        private const string COLUMN_USE_SUSPENSE_ERROR = "UseSuspenseError";
        private const string COLUMN_USE_CURRENCY_BALANCING = "UseCurrencyBalancing";
        private const string COLUMN_USE_ROUNDING_OFF = "FRPT_IsRoundingOff";

        /* Preferred source-document display columns, in resolution order. */
        private const string COLUMN_DOCUMENTNO = "DocumentNo";
        private const string COLUMN_VALUE = "Value";
        private const string COLUMN_AD_CLIENT_ID = "AD_Client_ID";

        /* The dictionary table the suspense settings live on. */
        private const string TABLE_ACCTSCHEMA_GL = "C_AcctSchema_GL";

        /* Detail paging guard rails. The client asks for a page size; anything outside
           this band is clamped so a crafted request cannot pull a whole table into one
           response. */
        private const int PAGESIZE_MIN = 1;
        private const int PAGESIZE_MAX = 100;
        private const int PAGESIZE_DEFAULT = 8;

        /* At most this many identifier columns are concatenated into a display value.
           Beyond three the string is no longer something a reader scans. */
        private const int IDENTIFIER_COLUMN_MAX = 3;

        /* Longest identifier the dictionary may hand back. */
        private const int IDENTIFIER_MAX = 60;

        /* Per-request cache of source-table metadata, keyed by AD_Table_ID. One modal
           page touches a handful of tables; this keeps a second page of the same
           category from re-reading the dictionary. */
        private Dictionary<int, SourceTable> _sourceTables;

        // ─────────────────────────────────────────────────────────────────────
        // §1  Bootstrap, accounting context and period list
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Bootstraps the widget in one round trip: the tenant's accounting context,
        /// every selectable open period of the primary calendar, the period to
        /// preselect, and that period's four suspense figures.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <returns>Populated <see cref="SuspenseBootstrap"/> (never null).</returns>
        public SuspenseBootstrap GetBootstrap(Ctx ctx)
        {
            SuspenseBootstrap result = new SuspenseBootstrap();
            result.Periods = new List<PeriodItem>();
            result.Data = new PeriodData();

            if (ctx == null) { return result; }

            /* The accounting context is a CONFIGURATION precondition, not a filter:
               without a primary calendar there is no period list to build, and without
               a primary accounting schema there is no account to read. Neither is
               silently replaced by "some other" calendar or schema. */
            AcctContext acct = GetAcctContext(ctx);
            result.Schema = acct;

            if (acct.C_Calendar_ID <= 0)
            {
                result.ErrorCode = ERROR_NO_CALENDAR;
                Log.Log(Level.WARNING, "VAS_202_SuspenseBalances: AD_ClientInfo.C_Calendar_ID not configured for AD_Client_ID="
                    + ctx.GetAD_Client_ID());
                return result;
            }

            if (acct.C_AcctSchema_ID <= 0)
            {
                result.ErrorCode = ERROR_NO_ACCTSCHEMA;
                Log.Log(Level.WARNING, "VAS_202_SuspenseBalances: AD_ClientInfo.C_AcctSchema1_ID not configured for AD_Client_ID="
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
        /// accounting schema, with the currency every figure on the card is expressed
        /// in. Both come from AD_ClientInfo - never from a search over all calendars or
        /// all schemas.
        ///
        /// Reads only client-scoped configuration and reference tables, so no MRole
        /// predicate is applied - the same treatment the sibling Period Control widgets
        /// give this lookup.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <returns>Populated <see cref="AcctContext"/>; the ids are 0 when the tenant
        /// has no primary calendar / accounting schema.</returns>
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
        /// The primary calendar on its own. Only reached when the accounting-context
        /// query found nothing, to distinguish "no calendar" from "no schema".
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
        /// Open; it appears once however many document base types are open for it -
        /// suspense postings are not confined to one base type, so requiring every
        /// control row to be open would hide periods that genuinely carry suspense.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="calendarId">The tenant's primary C_Calendar_ID.</param>
        /// <returns>Open periods, newest StartDate first (never null).</returns>
        public List<PeriodItem> GetOpenPeriods(Ctx ctx, int calendarId)
        {
            List<PeriodItem> items = new List<PeriodItem>();
            if (ctx == null || calendarId <= 0) { return items; }

            /* The open-control test is an EXISTS predicate rather than a join, so
               several open base types cannot multiply the period out and no DISTINCT
               is needed. */
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
        /// sends the id; everything the queries run against comes from here.
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
                       p.EndDate AS End_Date
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

            DataRow row = ds.Tables[0].Rows[0];

            PeriodItem item = new PeriodItem();
            item.C_Period_ID = Util.GetValueOfInt(row["C_Period_ID"]);
            item.Name = Util.GetValueOfString(row["Period_Name"]);
            item.StartDate = Util.GetValueOfDateTime(row["Start_Date"]);
            item.EndDate = Util.GetValueOfDateTime(row["End_Date"]);

            return item;
        }

        // ─────────────────────────────────────────────────────────────────────
        // §2  The configured suspense accounts
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The four configured control accounts of one accounting schema, resolved
        /// from C_AcctSchema_GL through C_ValidCombination to the natural account.
        ///
        /// All four rows are ALWAYS returned, configured or not: these are control
        /// settings, and a missing one is itself the finding the card has to show. An
        /// unconfigured or unresolvable row carries Account_ID 0, which is never sent
        /// to Fact_Acct.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="acctSchemaId">Primary C_AcctSchema_ID.</param>
        /// <param name="warnings">Warning list being built (may be null).</param>
        /// <returns>Exactly four rows in card display order (never null).</returns>
        public List<SuspenseAccount> GetConfiguredAccounts(Ctx ctx, int acctSchemaId,
            List<WarningItem> warnings)
        {
            List<SuspenseAccount> accounts = new List<SuspenseAccount>();
            accounts.Add(NewAccount(ACCOUNTTYPE_SUSPENSE_BALANCING));
            accounts.Add(NewAccount(ACCOUNTTYPE_SUSPENSE_ERROR));
            accounts.Add(NewAccount(ACCOUNTTYPE_CURRENCY_BALANCING));
            accounts.Add(NewAccount(ACCOUNTTYPE_ROUNDING_OFF));

            if (ctx == null || acctSchemaId <= 0) { return accounts; }

            /* FRPT_RoundingOff_Acct / FRPT_IsRoundingOff are optional in this schema, so
               the SELECT list is built from what the dictionary confirms C_AcctSchema_GL
               actually carries. Naming a column that is not there would fail the whole
               query and cost the two accounts that ARE configured. */
            List<string> glColumns = ReadTableColumns(ctx, TABLE_ACCTSCHEMA_GL);

            bool hasRoundingAcct = HasColumn(glColumns, COLUMN_ROUNDING_OFF);
            bool hasRoundingFlag = HasColumn(glColumns, COLUMN_USE_ROUNDING_OFF);

            StringBuilder select = new StringBuilder();
            select.Append(@"
                SELECT gl.C_AcctSchema_GL_ID AS C_AcctSchema_GL_ID,
                       COALESCE(gl.").Append(COLUMN_SUSPENSE_BALANCING).Append(@",0) AS Balancing_Combination_ID,
                       COALESCE(gl.").Append(COLUMN_USE_SUSPENSE_BALANCING).Append(@",'N') AS Balancing_Enabled,
                       COALESCE(gl.").Append(COLUMN_SUSPENSE_ERROR).Append(@",0) AS Error_Combination_ID,
                       COALESCE(gl.").Append(COLUMN_USE_SUSPENSE_ERROR).Append(@",'N') AS Error_Enabled,
                       COALESCE(gl.").Append(COLUMN_CURRENCY_BALANCING).Append(@",0) AS Currency_Combination_ID,
                       COALESCE(gl.").Append(COLUMN_USE_CURRENCY_BALANCING).Append(@",'N') AS Currency_Enabled");

            select.Append(hasRoundingAcct
                ? ",COALESCE(gl." + COLUMN_ROUNDING_OFF + ",0) AS Rounding_Combination_ID"
                : ",0 AS Rounding_Combination_ID");

            select.Append(hasRoundingFlag
                ? ",COALESCE(gl." + COLUMN_USE_ROUNDING_OFF + ",'N') AS Rounding_Enabled"
                : ",'N' AS Rounding_Enabled");

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
                Log.Log(Level.WARNING, "VAS_202_SuspenseBalances: no active C_AcctSchema_GL row for C_AcctSchema_ID="
                    + acctSchemaId);
                AddWarnings(warnings, accounts, WARNING_NOT_CONFIGURED);
                return accounts;
            }

            DataRow row = ds.Tables[0].Rows[0];

            accounts[0].ConfiguredValidCombination_ID = Util.GetValueOfInt(row["Balancing_Combination_ID"]);
            accounts[0].IsEnabledBySetup = "Y".Equals(Util.GetValueOfString(row["Balancing_Enabled"]));
            accounts[1].ConfiguredValidCombination_ID = Util.GetValueOfInt(row["Error_Combination_ID"]);
            accounts[1].IsEnabledBySetup = "Y".Equals(Util.GetValueOfString(row["Error_Enabled"]));
            accounts[2].ConfiguredValidCombination_ID = Util.GetValueOfInt(row["Currency_Combination_ID"]);
            accounts[2].IsEnabledBySetup = "Y".Equals(Util.GetValueOfString(row["Currency_Enabled"]));
            accounts[3].ConfiguredValidCombination_ID = Util.GetValueOfInt(row["Rounding_Combination_ID"]);
            accounts[3].IsEnabledBySetup = "Y".Equals(Util.GetValueOfString(row["Rounding_Enabled"]));

            /* One query resolves all four combinations - the settings often point at the
               same accounting element, and a per-setting join chain would read the same
               rows four times. */
            ResolveCombinations(ctx, acctSchemaId, accounts);

            for (int i = 0; i < accounts.Count; i++)
            {
                SuspenseAccount account = accounts[i];

                if (account.ConfiguredValidCombination_ID <= 0)
                {
                    AddWarning(warnings, WARNING_NOT_CONFIGURED, account.AccountType);
                }
                else if (account.Account_ID <= 0)
                {
                    /* Configured, but the combination or its element is gone / inactive.
                       Distinct from "not configured": the setup was made and has since
                       broken, and Fact_Acct must not be queried for it. */
                    AddWarning(warnings, WARNING_UNRESOLVED, account.AccountType);
                    Log.Log(Level.WARNING, "VAS_202_SuspenseBalances: C_ValidCombination_ID "
                        + account.ConfiguredValidCombination_ID + " (" + account.AccountType
                        + ") does not resolve to an active natural account");
                }
                else
                {
                    account.IsConfigured = true;
                }
            }

            FlagDuplicateAccounts(accounts, warnings);

            return accounts;
        }

        /// <summary>
        /// Resolves the configured C_ValidCombination_ID of every row to its natural
        /// account, and reads the element's Value / Name for display.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="acctSchemaId">Primary C_AcctSchema_ID - the combination must
        /// belong to the same schema the figures are read for.</param>
        /// <param name="accounts">Rows carrying the configured combination ids.</param>
        private void ResolveCombinations(Ctx ctx, int acctSchemaId, List<SuspenseAccount> accounts)
        {
            List<int> combinationIds = new List<int>();
            for (int i = 0; i < accounts.Count; i++)
            {
                int id = accounts[i].ConfiguredValidCombination_ID;
                if (id > 0 && !combinationIds.Contains(id)) { combinationIds.Add(id); }
            }

            if (combinationIds.Count == 0) { return; }

            /* Every bind carries its own name: a repeated name is ambiguous under the
               positional binding the backend adapters use. */
            List<SqlParameter> parameters = new List<SqlParameter>();
            parameters.Add(new SqlParameter("@C_AcctSchema_ID", acctSchemaId));

            string inList = BuildIdInList(combinationIds, "@C_ValidCombination_ID", parameters);

            string sql = @"
                SELECT vc.C_ValidCombination_ID AS C_ValidCombination_ID,
                       vc.Account_ID AS Account_ID,
                       COALESCE(ev.Value,N'') AS Account_Value,
                       COALESCE(ev.Name,N'') AS Account_Name
                FROM C_ValidCombination vc
                INNER JOIN C_ElementValue ev ON (ev.C_ElementValue_ID=vc.Account_ID AND ev.IsActive='Y')
                WHERE vc.C_AcctSchema_ID=@C_AcctSchema_ID
                  AND vc.IsActive='Y'
                  AND vc.C_ValidCombination_ID IN (" + inList + ")";

            DataSet ds = DB.ExecuteDataset(sql, parameters.ToArray(), null);
            if (ds == null || ds.Tables.Count == 0) { return; }

            DataTable dt = ds.Tables[0];
            for (int i = 0; i < dt.Rows.Count; i++)
            {
                DataRow dr = dt.Rows[i];
                int combinationId = Util.GetValueOfInt(dr["C_ValidCombination_ID"]);

                for (int a = 0; a < accounts.Count; a++)
                {
                    if (accounts[a].ConfiguredValidCombination_ID != combinationId) { continue; }

                    accounts[a].Account_ID = Util.GetValueOfInt(dr["Account_ID"]);
                    accounts[a].AccountValue = Util.GetValueOfString(dr["Account_Value"]);
                    accounts[a].AccountName = Util.GetValueOfString(dr["Account_Name"]);
                }
            }
        }

        /// <summary>
        /// Marks the rows that share one natural account. The labels stay distinct - the
        /// setup really does name that account twice - but the footer must count the
        /// balance once, and the user is told why two rows read the same figure.
        /// </summary>
        /// <param name="accounts">Resolved rows.</param>
        /// <param name="warnings">Warning list being built (may be null).</param>
        private void FlagDuplicateAccounts(List<SuspenseAccount> accounts, List<WarningItem> warnings)
        {
            for (int i = 0; i < accounts.Count; i++)
            {
                if (accounts[i].Account_ID <= 0) { continue; }

                for (int j = i + 1; j < accounts.Count; j++)
                {
                    if (accounts[j].Account_ID != accounts[i].Account_ID) { continue; }

                    accounts[i].IsDuplicateAccount = true;
                    accounts[j].IsDuplicateAccount = true;
                    AddWarning(warnings, WARNING_DUPLICATE_ACCOUNT, accounts[j].AccountType);
                }
            }
        }

        /// <summary>A zeroed row for one configuration slot.</summary>
        /// <param name="accountType">ACCOUNTTYPE_* token.</param>
        /// <returns>New <see cref="SuspenseAccount"/>.</returns>
        private SuspenseAccount NewAccount(string accountType)
        {
            SuspenseAccount account = new SuspenseAccount();
            account.AccountType = accountType;
            return account;
        }

        // ─────────────────────────────────────────────────────────────────────
        // §3  The card figures
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Everything the card shows for one period: the four configured control
        /// accounts, each with its posting count and net balance, and the total that
        /// still has to be cleared.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="periodId">C_Period_ID selected by the user.</param>
        /// <returns>Populated <see cref="PeriodData"/> (never null).</returns>
        public PeriodData GetPeriodData(Ctx ctx, int periodId)
        {
            PeriodData result = new PeriodData();
            result.C_Period_ID = periodId;
            result.Accounts = new List<SuspenseAccount>();
            result.Warnings = new List<WarningItem>();

            if (ctx == null) { return result; }

            AcctContext acct = GetAcctContext(ctx);
            result.Schema = acct;

            if (acct.C_Calendar_ID <= 0) { result.ErrorCode = ERROR_NO_CALENDAR; return result; }
            if (acct.C_AcctSchema_ID <= 0) { result.ErrorCode = ERROR_NO_ACCTSCHEMA; return result; }

            PeriodItem period = GetOpenPeriod(ctx, acct.C_Calendar_ID, periodId);
            if (period == null)
            {
                result.ErrorCode = ERROR_NO_PERIOD;
                return result;
            }

            result.PeriodName = period.Name;
            result.Accounts = GetConfiguredAccounts(ctx, acct.C_AcctSchema_ID, result.Warnings);

            ReadBalances(ctx, acct.C_AcctSchema_ID, period.C_Period_ID, result.Accounts);
            result.TotalSuspenseToClear = SumAbsoluteDistinctBalances(result.Accounts);

            return result;
        }

        /// <summary>
        /// Fills the posting count and the debit / credit / net figures of every
        /// RESOLVED account from Fact_Acct, in ONE grouped query.
        ///
        /// AmtAcctDr / AmtAcctCr are already stated in the accounting schema's own
        /// currency, so nothing is converted here - a second conversion would restate
        /// figures that are already correct.
        ///
        /// Accounts with no Fact_Acct row simply keep their zeros: a control account
        /// reading 0 is the good news the card exists to deliver, so the row stays
        /// visible rather than being dropped.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="acctSchemaId">Primary C_AcctSchema_ID.</param>
        /// <param name="periodId">Validated C_Period_ID.</param>
        /// <param name="accounts">Rows to fill.</param>
        private void ReadBalances(Ctx ctx, int acctSchemaId, int periodId, List<SuspenseAccount> accounts)
        {
            List<int> accountIds = DistinctAccountIds(accounts);
            if (accountIds.Count == 0) { return; }

            /* Bind order is appearance order in the finished statement. */
            List<SqlParameter> parameters = new List<SqlParameter>();
            parameters.Add(new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()));
            parameters.Add(new SqlParameter("@C_AcctSchema_ID", acctSchemaId));
            parameters.Add(new SqlParameter("@C_Period_ID", periodId));
            parameters.Add(new SqlParameter("@PostingType", POSTINGTYPE_Actual));

            string inList = BuildIdInList(accountIds, "@Account_ID", parameters);

            string sql = @"
                SELECT fa.Account_ID AS Account_ID,
                       COUNT(fa.Fact_Acct_ID) AS Entry_Count,
                       SUM(COALESCE(fa.AmtAcctDr,0)) AS Debit_Amount,
                       SUM(COALESCE(fa.AmtAcctCr,0)) AS Credit_Amount,
                       SUM(COALESCE(fa.AmtAcctDr,0)-COALESCE(fa.AmtAcctCr,0)) AS Net_Balance
                FROM Fact_Acct fa
                WHERE fa.AD_Client_ID=@AD_Client_ID
                  AND fa.C_AcctSchema_ID=@C_AcctSchema_ID
                  AND fa.C_Period_ID=@C_Period_ID
                  AND fa.PostingType=@PostingType
                  AND fa.IsActive='Y'
                  AND fa.Account_ID IN (" + inList + ")";

            /* Fact_Acct fa is the main physical table: the role's organization access
               is applied HERE, to the base query, and never to a derived alias. */
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "fa", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            /* GROUP BY goes on AFTER the access SQL - its FROM-clause parser must not
               meet a trailing clause. */
            sql += " GROUP BY fa.Account_ID ORDER BY fa.Account_ID";

            DataSet ds = DB.ExecuteDataset(sql, parameters.ToArray(), null);
            if (ds == null || ds.Tables.Count == 0) { return; }

            DataTable dt = ds.Tables[0];
            for (int i = 0; i < dt.Rows.Count; i++)
            {
                DataRow dr = dt.Rows[i];
                int accountId = Util.GetValueOfInt(dr["Account_ID"]);

                /* One Fact_Acct group can feed more than one card row when the same
                   account is configured into two settings. */
                for (int a = 0; a < accounts.Count; a++)
                {
                    if (accounts[a].Account_ID != accountId) { continue; }

                    accounts[a].EntryCount = Util.GetValueOfInt(dr["Entry_Count"]);
                    accounts[a].DebitAmount = Util.GetValueOfDecimal(dr["Debit_Amount"]);
                    accounts[a].CreditAmount = Util.GetValueOfDecimal(dr["Credit_Amount"]);
                    accounts[a].Balance = Util.GetValueOfDecimal(dr["Net_Balance"]);
                }
            }
        }

        /// <summary>
        /// The footer figure: the sum of the ABSOLUTE net balance of every DISTINCT
        /// resolved account.
        ///
        /// Absolute, because a debit suspense and a credit suspense are two things to
        /// investigate, not one that cancels out - netting them would report zero work
        /// on a set of books carrying two errors. Distinct, because an account named by
        /// two settings holds one balance, not two.
        /// </summary>
        /// <param name="accounts">Rows carrying the filled balances.</param>
        /// <returns>Total still to clear, in the accounting schema currency.</returns>
        private decimal SumAbsoluteDistinctBalances(List<SuspenseAccount> accounts)
        {
            decimal total = 0;
            List<int> counted = new List<int>();

            for (int i = 0; i < accounts.Count; i++)
            {
                int accountId = accounts[i].Account_ID;
                if (accountId <= 0 || counted.Contains(accountId)) { continue; }

                counted.Add(accountId);
                total += Math.Abs(accounts[i].Balance);
            }

            return total;
        }

        /// <summary>The resolved natural account ids, each once.</summary>
        /// <param name="accounts">Configured rows.</param>
        /// <returns>Distinct ids greater than zero (never null).</returns>
        private List<int> DistinctAccountIds(List<SuspenseAccount> accounts)
        {
            List<int> ids = new List<int>();
            if (accounts == null) { return ids; }

            for (int i = 0; i < accounts.Count; i++)
            {
                int id = accounts[i].Account_ID;
                if (id > 0 && !ids.Contains(id)) { ids.Add(id); }
            }

            return ids;
        }

        // ─────────────────────────────────────────────────────────────────────
        // §4  Detail (server-side paging)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// One page of the postings sitting on one suspense account, plus the total row
        /// count so the client can page without holding the whole set.
        ///
        /// The requested account is re-validated against the tenant's OWN configuration
        /// before anything is read: only the natural accounts that the primary
        /// accounting schema actually names as suspense or rounding accounts are
        /// readable through this method, whatever the browser asks for.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="periodId">C_Period_ID selected by the user.</param>
        /// <param name="accountId">Natural Account_ID the user clicked.</param>
        /// <param name="pageNo">1-based page number.</param>
        /// <param name="pageSize">Rows per page (clamped server-side).</param>
        /// <returns>Populated <see cref="PostingPage"/> (never null).</returns>
        public PostingPage GetPostings(Ctx ctx, int periodId, int accountId, int pageNo, int pageSize)
        {
            PostingPage result = new PostingPage();
            result.Rows = new List<PostingRow>();
            result.C_Period_ID = periodId;
            result.Account_ID = accountId;
            result.PageNo = 1;
            result.PageSize = pageSize;

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

            PeriodItem period = GetOpenPeriod(ctx, acct.C_Calendar_ID, periodId);
            if (period == null)
            {
                result.ErrorCode = ERROR_NO_PERIOD;
                return result;
            }

            result.PeriodName = period.Name;

            /* Authorization, not convenience: the id has to be one of THIS tenant's
               configured suspense accounts, or the request is refused. */
            SuspenseAccount account = FindConfiguredAccount(ctx, acct.C_AcctSchema_ID, accountId);
            if (account == null)
            {
                result.ErrorCode = ERROR_INVALID_REQUEST;
                return result;
            }

            result.AccountValue = account.AccountValue;
            result.AccountName = account.AccountName;
            result.AccountType = account.AccountType;

            if (pageSize < PAGESIZE_MIN || pageSize > PAGESIZE_MAX) { pageSize = PAGESIZE_DEFAULT; }
            if (pageNo < 1) { pageNo = 1; }
            result.PageSize = pageSize;

            result.Total = CountPostings(ctx, acct.C_AcctSchema_ID, period.C_Period_ID, accountId);
            if (result.Total == 0) { return result; }

            int totalPages = (result.Total + pageSize - 1) / pageSize;
            if (pageNo > totalPages) { pageNo = totalPages; }
            result.PageNo = pageNo;

            result.Rows = ReadPostings(ctx, acct.C_AcctSchema_ID, period.C_Period_ID, accountId,
                pageNo, pageSize);

            /* The document numbers are resolved for the whole page at once, grouped by
               source table - never one query per row. */
            ResolveDocumentNumbers(ctx, result.Rows);
            ApplyNavigability(ctx, result.Rows);

            return result;
        }

        /// <summary>
        /// The configured row for one natural account id, or null when the tenant's
        /// primary accounting schema does not name that account at all.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="acctSchemaId">Primary C_AcctSchema_ID.</param>
        /// <param name="accountId">Natural Account_ID to authorize.</param>
        /// <returns>The matching configured row, or null.</returns>
        private SuspenseAccount FindConfiguredAccount(Ctx ctx, int acctSchemaId, int accountId)
        {
            List<SuspenseAccount> accounts = GetConfiguredAccounts(ctx, acctSchemaId, null);

            for (int i = 0; i < accounts.Count; i++)
            {
                if (accounts[i].Account_ID == accountId && accounts[i].Account_ID > 0)
                {
                    return accounts[i];
                }
            }

            return null;
        }

        /// <summary>
        /// How many postings the account carries in the period. Counted with the same
        /// predicates and the same role filter as the page itself, so the pager can
        /// never promise a page the list will not produce.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="acctSchemaId">Primary C_AcctSchema_ID.</param>
        /// <param name="periodId">Validated C_Period_ID.</param>
        /// <param name="accountId">Authorized natural Account_ID.</param>
        /// <returns>Row count.</returns>
        private int CountPostings(Ctx ctx, int acctSchemaId, int periodId, int accountId)
        {
            string sql = @"
                SELECT COUNT(fa.Fact_Acct_ID) AS Record_Count
                FROM Fact_Acct fa
                WHERE " + PostingWhere();

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "fa", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            DataSet ds = DB.ExecuteDataset(sql,
                PostingWhereParameters(ctx, acctSchemaId, periodId, accountId), null);

            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) { return 0; }

            return Util.GetValueOfInt(ds.Tables[0].Rows[0]["Record_Count"]);
        }

        /// <summary>
        /// One page of postings, newest accounting date first.
        ///
        /// AD_Table is joined LEFT, not INNER: a posting whose source table has since
        /// been deactivated still has an amount that the card counted, and dropping it
        /// here would leave the list short of the total the pager was built from. Such a
        /// row falls back to its raw AD_Table_ID for a label and is simply not
        /// navigable. AD_Window is LEFT for the same reason and because
        /// Fact_Acct.AD_Window_ID is legitimately null or zero on many postings.
        ///
        /// The screen label prefers the session language's window translation, then the
        /// window's DisplayName, then its Name, then the table's Name, then the physical
        /// TableName - the order §"Screen Display Name Resolution" asks for, with the
        /// translation in front. Two names it deliberately does NOT reference:
        /// AD_Table.DisplayName and AD_Table_Trl.Name - neither exists in this schema
        /// (AD_Window is translated through AD_Window_Trl, AD_Table is not translated at
        /// all), so a table-sourced label is the untranslated dictionary name.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="acctSchemaId">Primary C_AcctSchema_ID.</param>
        /// <param name="periodId">Validated C_Period_ID.</param>
        /// <param name="accountId">Authorized natural Account_ID.</param>
        /// <param name="pageNo">1-based page number (already clamped).</param>
        /// <param name="pageSize">Rows per page (already clamped).</param>
        /// <returns>Materialised rows (never null).</returns>
        private List<PostingRow> ReadPostings(Ctx ctx, int acctSchemaId, int periodId, int accountId,
            int pageNo, int pageSize)
        {
            List<PostingRow> rows = new List<PostingRow>();

            string sql = @"
                SELECT fa.Fact_Acct_ID AS Fact_Acct_ID,
                       fa.AD_Table_ID AS AD_Table_ID,
                       COALESCE(fa.Record_ID,0) AS Record_ID,
                       fa.DateTrx AS Date_Trx,
                       fa.DateAcct AS Date_Acct,
                       COALESCE(fa.Description,N'') AS Fact_Description,
                       COALESCE(fa.AmtAcctDr,0) AS Amt_Acct_Dr,
                       COALESCE(fa.AmtAcctCr,0) AS Amt_Acct_Cr,
                       CASE WHEN COALESCE(fa.AmtAcctDr,0)<>0 THEN '" + DRCR_DEBIT + @"' ELSE '" + DRCR_CREDIT + @"' END AS Dr_Cr,
                       CASE WHEN COALESCE(fa.AmtAcctDr,0)<>0 THEN COALESCE(fa.AmtAcctDr,0) ELSE COALESCE(fa.AmtAcctCr,0) END AS Posting_Amount,
                       COALESCE(wtrl.Name,w.DisplayName,w.Name,t.Name,t.TableName,N'') AS Screen_Name,
                       COALESCE(t.TableName,N'') AS Table_Name,
                       COALESCE(w.AD_Window_ID,0) AS Window_Resolved_ID
                FROM Fact_Acct fa
                LEFT OUTER JOIN AD_Table t ON (t.AD_Table_ID=fa.AD_Table_ID AND t.IsActive='Y')
                LEFT OUTER JOIN AD_Window w ON (w.AD_Window_ID=fa.AD_Window_ID AND w.IsActive='Y')
                LEFT OUTER JOIN AD_Window_Trl wtrl ON (wtrl.AD_Window_ID=w.AD_Window_ID AND wtrl.AD_Language=@AD_Language AND wtrl.IsActive='Y')
                WHERE " + PostingWhere();

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "fa", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            /* Sort and paging after the access SQL. Fact_Acct_ID breaks the DateAcct tie
               so paging is stable across requests. */
            sql += " ORDER BY fa.DateAcct DESC,fa.Fact_Acct_ID DESC";
            sql += PagingSuffix(pageSize, (pageNo - 1) * pageSize);

            /* Appearance order: the window-translation join binds first, then the WHERE.
               Every occurrence carries its own name - a repeated name is ambiguous under
               the positional binding the backend adapters use. */
            List<SqlParameter> parameters = new List<SqlParameter>();
            parameters.Add(new SqlParameter("@AD_Language", ctx.GetAD_Language()));
            parameters.AddRange(PostingWhereParameters(ctx, acctSchemaId, periodId, accountId));

            DataSet ds = DB.ExecuteDataset(sql, parameters.ToArray(), null);
            if (ds == null || ds.Tables.Count == 0) { return rows; }

            DataTable dt = ds.Tables[0];
            for (int i = 0; i < dt.Rows.Count; i++)
            {
                DataRow dr = dt.Rows[i];

                PostingRow row = new PostingRow();
                row.Fact_Acct_ID = Util.GetValueOfInt(dr["Fact_Acct_ID"]);
                row.AD_Table_ID = Util.GetValueOfInt(dr["AD_Table_ID"]);
                row.AD_Window_ID = Util.GetValueOfInt(dr["Window_Resolved_ID"]);
                row.Record_ID = Util.GetValueOfInt(dr["Record_ID"]);
                row.DocumentDate = Util.GetValueOfDateTime(dr["Date_Trx"]);
                row.AccountDate = Util.GetValueOfDateTime(dr["Date_Acct"]);
                row.Description = Util.GetValueOfString(dr["Fact_Description"]);
                row.DrCr = Util.GetValueOfString(dr["Dr_Cr"]);
                row.Amount = Util.GetValueOfDecimal(dr["Posting_Amount"]);
                row.TableName = Util.GetValueOfString(dr["Table_Name"]);
                row.ScreenDisplayName = Util.GetValueOfString(dr["Screen_Name"]);

                /* Neither the window nor the table resolved - name the row by the raw
                   table id rather than leaving the Screen column blank. */
                if (string.IsNullOrEmpty(row.ScreenDisplayName))
                {
                    row.ScreenDisplayName = "#" + row.AD_Table_ID.ToString(CultureInfo.InvariantCulture);
                }

                rows.Add(row);
            }

            return rows;
        }

        /// <summary>
        /// The WHERE body shared by the posting count and the posting page, so the two
        /// can never drift apart. C_Period_ID is the period filter - Fact_Acct carries
        /// the period it was posted into, and re-deriving it from a date range would
        /// disagree with the ledger.
        /// </summary>
        /// <returns>Predicate binding @AD_Client_ID, @C_AcctSchema_ID, @C_Period_ID,
        /// @Account_ID and @PostingType, in that order.</returns>
        private string PostingWhere()
        {
            return @"fa.AD_Client_ID=@AD_Client_ID
                  AND fa.C_AcctSchema_ID=@C_AcctSchema_ID
                  AND fa.C_Period_ID=@C_Period_ID
                  AND fa.Account_ID=@Account_ID
                  AND fa.PostingType=@PostingType
                  AND fa.IsActive='Y'";
        }

        /// <summary>Bind values for <see cref="PostingWhere"/>, in appearance order.</summary>
        /// <param name="ctx">Session context (supplies the tenant).</param>
        /// <param name="acctSchemaId">Primary C_AcctSchema_ID.</param>
        /// <param name="periodId">Validated C_Period_ID.</param>
        /// <param name="accountId">Authorized natural Account_ID.</param>
        /// <returns>Bind array.</returns>
        private SqlParameter[] PostingWhereParameters(Ctx ctx, int acctSchemaId, int periodId,
            int accountId)
        {
            return new SqlParameter[]
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@C_AcctSchema_ID", acctSchemaId),
                new SqlParameter("@C_Period_ID", periodId),
                new SqlParameter("@Account_ID", accountId),
                new SqlParameter("@PostingType", POSTINGTYPE_Actual)
            };
        }

        // ─────────────────────────────────────────────────────────────────────
        // §5  Source document resolution
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Fills the DocumentNo of every row on the page.
        ///
        /// Fact_Acct stores only AD_Table_ID / Record_ID, so the display value has to be
        /// fetched from the source table. That is done from Application Dictionary
        /// metadata rather than from a hard-coded CASE over every transaction table:
        /// the metadata for all of the page's tables is read in TWO queries, then ONE
        /// query per distinct table fetches every record of that table the page needs.
        /// A page of any size therefore costs (2 + tables) queries, never one per row.
        ///
        /// Nothing here originates from the browser: the table ids come out of the
        /// already-secured Fact_Acct page, and every table and column name is
        /// re-validated by IsSafeIdentifier before it is concatenated.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="rows">Page rows to fill.</param>
        private void ResolveDocumentNumbers(Ctx ctx, List<PostingRow> rows)
        {
            if (ctx == null || rows == null || rows.Count == 0) { return; }

            /* Group the page by source table: one fetch per table, not per row. */
            Dictionary<int, List<int>> recordsByTable = new Dictionary<int, List<int>>();

            for (int i = 0; i < rows.Count; i++)
            {
                PostingRow row = rows[i];

                /* The final fallback, applied up front so a row can never come out of
                   here with an empty document column. */
                row.DocumentNo = "#" + row.Record_ID.ToString(CultureInfo.InvariantCulture);

                if (row.AD_Table_ID <= 0 || row.Record_ID <= 0) { continue; }

                if (!recordsByTable.ContainsKey(row.AD_Table_ID))
                {
                    recordsByTable[row.AD_Table_ID] = new List<int>();
                }

                List<int> ids = recordsByTable[row.AD_Table_ID];
                if (!ids.Contains(row.Record_ID)) { ids.Add(row.Record_ID); }
            }

            if (recordsByTable.Count == 0) { return; }

            List<int> tableIds = new List<int>(recordsByTable.Keys);
            LoadSourceTables(ctx, tableIds);

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

                    rows[i].DocumentNo = display;
                    rows[i].IsRecordFound = true;
                }
            }
        }

        /// <summary>
        /// Reads and caches the metadata of every source table the page touches: the
        /// physical name, the key column (the Zoom target), whether it carries
        /// DocumentNo / Value / AD_Client_ID, and its IsIdentifier columns in SeqNo
        /// order. Two queries for the whole page, and already-cached tables are not
        /// re-read.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="tableIds">AD_Table_IDs seen on the page.</param>
        private void LoadSourceTables(Ctx ctx, List<int> tableIds)
        {
            if (_sourceTables == null) { _sourceTables = new Dictionary<int, SourceTable>(); }

            List<int> wanted = new List<int>();
            for (int i = 0; i < tableIds.Count; i++)
            {
                if (tableIds[i] > 0 && !_sourceTables.ContainsKey(tableIds[i])) { wanted.Add(tableIds[i]); }
            }

            if (wanted.Count == 0) { return; }

            /* Query 1: the table itself and its three probed columns. The MAX(CASE ...)
               form gives one row per table however many columns it has. Views are
               excluded - there is no single record to open and no key to zoom to. */
            List<SqlParameter> parameters = new List<SqlParameter>();
            string inList = BuildIdInList(wanted, "@AD_Table_ID", parameters);

            string sql = @"
                SELECT t.AD_Table_ID AS AD_Table_ID,
                       t.TableName AS Table_Name,
                       MAX(CASE WHEN c.IsKey='Y' THEN c.ColumnName END) AS Key_Column,
                       MAX(CASE WHEN c.ColumnName='" + COLUMN_DOCUMENTNO + @"' THEN c.ColumnName END) AS Document_Column,
                       MAX(CASE WHEN c.ColumnName='" + COLUMN_VALUE + @"' THEN c.ColumnName END) AS Value_Column,
                       MAX(CASE WHEN c.ColumnName='" + COLUMN_AD_CLIENT_ID + @"' THEN c.ColumnName END) AS Client_Column
                FROM AD_Table t
                INNER JOIN AD_Column c ON (c.AD_Table_ID=t.AD_Table_ID AND c.IsActive='Y')
                WHERE t.IsActive='Y'
                  /* COALESCE, not a bare comparison: AD_Table.IsView is NULLable and
                     unset on most tables, so 't.IsView=''N''' is NULL rather than true
                     and no source table would resolve at all - every document number in
                     the modal would fall back to #Record_ID. */
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
                   it; the row keeps its #Record_ID fallback. */
                if (!IsSafeIdentifier(table.TableName) || !IsSafeIdentifier(table.KeyColumn)) { continue; }

                _sourceTables[table.AD_Table_ID] = table;
            }

            LoadIdentifierColumns(ctx, wanted);
        }

        /// <summary>
        /// Query 2 of the metadata pass: the IsIdentifier columns of every wanted
        /// table, in SeqNo order - the dictionary's own answer to "what identifies a
        /// record of this table", and the fallback used when a table has neither
        /// DocumentNo nor Value.
        /// </summary>
        /// <param name="ctx">Session context (unused today; kept for symmetry).</param>
        /// <param name="tableIds">AD_Table_IDs being loaded.</param>
        private void LoadIdentifierColumns(Ctx ctx, List<int> tableIds)
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
        /// role-filtered Fact_Acct page - the parent query is what authorizes these
        /// rows, exactly as a lookup join would be.
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
                   this way must not take the modal down with it: the affected rows keep
                   their #Record_ID fallback and stay readable. */
                Log.Log(Level.WARNING, "VAS_202_SuspenseBalances: display lookup failed for "
                    + table.TableName, ex);
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
        /// IsIdentifier columns in SeqNo order, else the key itself.
        ///
        /// Several identifiers are joined with the ANSI concatenation operator, which
        /// both PostgreSQL and Oracle accept. Each part is CAST to a character type
        /// first, because an identifier column is not necessarily text and PostgreSQL
        /// will not concatenate two non-text values; TRIM removes the blank padding a
        /// fixed-width CAST introduces; and COALESCE guards the concatenation, since on
        /// both backends one NULL part would otherwise make the WHOLE display value NULL
        /// and cost the record a name it half had.
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
               #Record_ID fallback already says, so there is no expression worth running. */
            return "";
        }

        /// <summary>
        /// Decides which rows may be clicked through to their source screen.
        ///
        /// All four conditions have to hold: the window resolved to an active window,
        /// the role may open it, the row has a record to position on, and the source
        /// table has a key column to position BY. A row that fails any of them still
        /// shows its screen label and its document value - it simply does not navigate.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="rows">Page rows to flag.</param>
        private void ApplyNavigability(Ctx ctx, List<PostingRow> rows)
        {
            if (ctx == null || rows == null) { return; }

            /* One access answer per window, however many rows share it. */
            Dictionary<int, bool> accessByWindow = new Dictionary<int, bool>();

            for (int i = 0; i < rows.Count; i++)
            {
                PostingRow row = rows[i];

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
        /// Whether the current role may open one window. Best-effort: a role that
        /// cannot be interrogated is treated as having no access, so a modal row never
        /// offers a link the framework would then refuse.
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
                Log.Log(Level.WARNING, "VAS_202_SuspenseBalances: window access check failed for AD_Window_ID="
                    + windowId, ex);
                return false;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // §6  Shared helpers
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Expands an id list into a parameterised IN list and appends the binds to the
        /// caller's list, in order. Each id gets its own uniquely named bind: a repeated
        /// name is ambiguous under the positional binding the backend adapters use, and
        /// a comma-separated literal would not be parameterised at all.
        /// </summary>
        /// <param name="ids">Server-derived ids (never client text).</param>
        /// <param name="prefix">Bind-name stem, e.g. "@Account_ID".</param>
        /// <param name="parameters">Bind list being built, in appearance order.</param>
        /// <returns>The IN-list body, e.g. "@Account_ID1,@Account_ID2".</returns>
        private string BuildIdInList(List<int> ids, string prefix, List<SqlParameter> parameters)
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
        /// The active column names of one dictionary table. Used to confirm an OPTIONAL
        /// column exists before it is named in generated SQL.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="tableName">Physical table name.</param>
        /// <returns>Column names (never null).</returns>
        private List<string> ReadTableColumns(Ctx ctx, string tableName)
        {
            List<string> columns = new List<string>();

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
            if (ds == null || ds.Tables.Count == 0) { return columns; }

            DataTable dt = ds.Tables[0];
            for (int i = 0; i < dt.Rows.Count; i++)
            {
                columns.Add(Util.GetValueOfString(dt.Rows[i]["Column_Name"]));
            }

            return columns;
        }

        /// <summary>Case-insensitive membership test over a column-name list.</summary>
        /// <param name="columns">Known column names.</param>
        /// <param name="name">Column being looked for.</param>
        /// <returns>true when present.</returns>
        private bool HasColumn(List<string> columns, string name)
        {
            if (columns == null || string.IsNullOrEmpty(name)) { return false; }

            for (int i = 0; i < columns.Count; i++)
            {
                if (name.Equals(columns[i], StringComparison.OrdinalIgnoreCase)) { return true; }
            }

            return false;
        }

        /// <summary>
        /// Guards every identifier that reaches the generated SQL. The names come from
        /// the Application Dictionary rather than from the client, so this is a
        /// belt-and-braces check - but a dictionary is data, and data can be edited, so
        /// nothing is concatenated that is not a plain unqualified identifier.
        /// </summary>
        /// <param name="identifier">Table or column name returned by the dictionary.</param>
        /// <returns>true when the name is safe to concatenate.</returns>
        private bool IsSafeIdentifier(string identifier)
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

        /// <summary>Adds one warning, skipping duplicates.</summary>
        /// <param name="warnings">List being built (may be null).</param>
        /// <param name="code">WARNING_* token.</param>
        /// <param name="accountType">ACCOUNTTYPE_* token the warning is about.</param>
        private void AddWarning(List<WarningItem> warnings, string code, string accountType)
        {
            if (warnings == null) { return; }

            for (int i = 0; i < warnings.Count; i++)
            {
                if (code.Equals(warnings[i].Code) && accountType.Equals(warnings[i].AccountType)) { return; }
            }

            WarningItem item = new WarningItem();
            item.Code = code;
            item.AccountType = accountType;
            warnings.Add(item);
        }

        /// <summary>Adds the same warning for every configuration slot.</summary>
        /// <param name="warnings">List being built (may be null).</param>
        /// <param name="accounts">Configuration rows.</param>
        /// <param name="code">WARNING_* token.</param>
        private void AddWarnings(List<WarningItem> warnings, List<SuspenseAccount> accounts, string code)
        {
            if (warnings == null || accounts == null) { return; }

            for (int i = 0; i < accounts.Count; i++)
            {
                AddWarning(warnings, code, accounts[i].AccountType);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // §7  Contracts
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The tenant's accounting context: the primary calendar, the primary
        /// accounting schema and the currency every figure is expressed in.
        /// </summary>
        public class AcctContext
        {
            /// <summary>AD_ClientInfo.C_Calendar_ID; 0 when not configured.</summary>
            public int C_Calendar_ID { get; set; }

            /// <summary>AD_ClientInfo.C_AcctSchema1_ID; 0 when not configured.</summary>
            public int C_AcctSchema_ID { get; set; }

            public string Name { get; set; }

            public int C_Currency_ID { get; set; }
            public string Iso { get; set; }
            public string Symbol { get; set; }

            /// <summary>C_Currency.StdPrecision - the decimals every amount is shown at.</summary>
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

        /// <summary>One configured control account and its figures for the period.</summary>
        public class SuspenseAccount
        {
            /// <summary>ACCOUNTTYPE_* token; the client resolves the label.</summary>
            public string AccountType { get; set; }

            /// <summary>The C_ValidCombination_ID the setup holds; 0 when unset.</summary>
            public int ConfiguredValidCombination_ID { get; set; }

            /// <summary>The NATURAL account the combination resolves to; 0 when unresolved.</summary>
            public int Account_ID { get; set; }

            /// <summary>C_ElementValue.Value - the account code.</summary>
            public string AccountValue { get; set; }

            /// <summary>C_ElementValue.Name.</summary>
            public string AccountName { get; set; }

            /// <summary>true only when the setting is filled AND resolves to an account.</summary>
            public bool IsConfigured { get; set; }

            /// <summary>The schema's own Use / Is flag for this setting.</summary>
            public bool IsEnabledBySetup { get; set; }

            /// <summary>true when another slot names the same natural account.</summary>
            public bool IsDuplicateAccount { get; set; }

            public int EntryCount { get; set; }
            public decimal DebitAmount { get; set; }
            public decimal CreditAmount { get; set; }

            /// <summary>Dr - Cr, in the accounting schema currency. Signed.</summary>
            public decimal Balance { get; set; }
        }

        /// <summary>One configuration problem the card should surface.</summary>
        public class WarningItem
        {
            /// <summary>WARNING_* token; the client resolves the label.</summary>
            public string Code { get; set; }

            /// <summary>ACCOUNTTYPE_* token the warning is about.</summary>
            public string AccountType { get; set; }
        }

        /// <summary>Everything the card shows for one period.</summary>
        public class PeriodData
        {
            public int C_Period_ID { get; set; }
            public string PeriodName { get; set; }

            public AcctContext Schema { get; set; }

            public List<SuspenseAccount> Accounts { get; set; }

            /// <summary>SUM(ABS(balance)) over the distinct resolved accounts.</summary>
            public decimal TotalSuspenseToClear { get; set; }

            public List<WarningItem> Warnings { get; set; }

            /// <summary>One of the ERROR_* tokens; the client resolves the label.</summary>
            public string ErrorCode { get; set; }
        }

        /// <summary>Accounting context, period list, default selection and its data.</summary>
        public class SuspenseBootstrap
        {
            public AcctContext Schema { get; set; }

            public List<PeriodItem> Periods { get; set; }

            public int C_Period_ID { get; set; }
            public string PeriodName { get; set; }

            public PeriodData Data { get; set; }

            /// <summary>One of the ERROR_* tokens; the client resolves the label.</summary>
            public string ErrorCode { get; set; }
        }

        /// <summary>One posting sitting on a suspense account.</summary>
        public class PostingRow
        {
            public int Fact_Acct_ID { get; set; }

            /* Technical fields - the client needs them to navigate, not to display. */
            public int AD_Table_ID { get; set; }

            /// <summary>The RESOLVED active window; 0 when there is none.</summary>
            public int AD_Window_ID { get; set; }

            public int Record_ID { get; set; }
            public string TableName { get; set; }

            /// <summary>The source table's key column - what the Zoom positions by.</summary>
            public string KeyColumnName { get; set; }

            /// <summary>Window name where one resolved, else the table's, else #AD_Table_ID.</summary>
            public string ScreenDisplayName { get; set; }

            /// <summary>Resolved from the source record, else #Record_ID.</summary>
            public string DocumentNo { get; set; }

            /// <summary>true when the source record was actually found.</summary>
            public bool IsRecordFound { get; set; }

            /// <summary>Fact_Acct.DateTrx.</summary>
            public DateTime? DocumentDate { get; set; }

            /// <summary>Fact_Acct.DateAcct.</summary>
            public DateTime? AccountDate { get; set; }

            /// <summary>DRCR_* token; the client resolves the label.</summary>
            public string DrCr { get; set; }

            /// <summary>The non-zero side, in the accounting schema currency.</summary>
            public decimal Amount { get; set; }

            public string Description { get; set; }

            /// <summary>true only when the row can really be opened.</summary>
            public bool CanNavigate { get; set; }
        }

        /// <summary>One page of postings plus the paging state.</summary>
        public class PostingPage
        {
            public int C_Period_ID { get; set; }
            public string PeriodName { get; set; }

            public int Account_ID { get; set; }
            public string AccountValue { get; set; }
            public string AccountName { get; set; }

            /// <summary>ACCOUNTTYPE_* token of the row that was clicked.</summary>
            public string AccountType { get; set; }

            public AcctContext Schema { get; set; }

            public List<PostingRow> Rows { get; set; }

            public int Total { get; set; }
            public int PageNo { get; set; }
            public int PageSize { get; set; }

            /// <summary>One of the ERROR_* tokens; the client resolves the label.</summary>
            public string ErrorCode { get; set; }
        }

        /// <summary>
        /// Dictionary metadata of one source table, cached per request. Every name in
        /// here has passed IsSafeIdentifier before it is used.
        /// </summary>
        private class SourceTable
        {
            public int AD_Table_ID { get; set; }
            public string TableName { get; set; }

            /// <summary>IsKey column - the Zoom target and the lookup's join key.</summary>
            public string KeyColumn { get; set; }

            /// <summary>"DocumentNo" when the table has one, else "".</summary>
            public string DocumentColumn { get; set; }

            /// <summary>"Value" when the table has one, else "".</summary>
            public string ValueColumn { get; set; }

            /// <summary>"AD_Client_ID" when the table has one, else "".</summary>
            public string ClientColumn { get; set; }

            /// <summary>IsIdentifier columns in SeqNo order, capped.</summary>
            public List<string> IdentifierColumns { get; set; }
        }
    }
}
