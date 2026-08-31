/******************************************************
 * Module Name    : VASLogic
 * Purpose        : Upcoming Recurring Schedules widget data
 * chronological  : Development
 * Created Date   : 2026-08-31
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
    /// Module Name : VAS_222_UpcomingSchedules
    /// Purpose     : Backs the VAS_222_UpcomingRecurringSchedules dashboard widget
    ///               (Recurring module, 6x2 grid). Answers "which recurring setups
    ///               are next in the generation queue?" and lets one of them be
    ///               generated from the row.
    ///
    ///               The list is paged in the database, not in the browser: a tenant
    ///               can hold hundreds of active setups and the widget shows a
    ///               handful of rows at a time, so the page size the client asks for
    ///               is the page size the query fetches.
    ///
    ///               Amounts come from whichever source document the setup copies
    ///               (invoice / order / payment) and are reported UNCONVERTED, in
    ///               that document's own currency. Each row therefore carries its
    ///               currency ISO, symbol and standard precision.
    ///
    ///               Row status is derived in code, never in SQL, so no display text
    ///               is produced by the query: TODAY when DateNextRun is today,
    ///               HOLD when the setup has no runs left, SCHEDULED otherwise.
    ///
    ///               MRole row-level security is applied to the main physical table
    ///               only (C_Recurring, alias r). The document, journal, project,
    ///               partner and currency joins are lookups that inherit that filter.
    ///               There is no CTE, so no CTE alias is passed to MRole. ORDER BY
    ///               and the paging suffix are appended AFTER AddAccessSQL so the
    ///               FROM-clause parser is not confused by a trailing clause.
    ///
    ///               The tenant always comes from the authenticated context; the
    ///               client never supplies AD_Client_ID and never supplies the date
    ///               bound. Compatible with PostgreSQL and Oracle.
    /// Chronological development:
    ///   VAI154      2026-08-31 Created
    /// </summary>
    public class VAS_222_UpcomingSchedulesModel
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_222_UpcomingSchedulesModel).FullName);

        /// <summary>Default rows per page when the client asks for none.</summary>
        public const int PAGESIZE_DEFAULT = 6;

        /// <summary>Largest accepted page size - the widget sizes its own page from
        /// the cell height, so anything beyond this is not a real request.</summary>
        public const int PAGESIZE_MAX = 50;

        /* Row status tokens returned to the client, which maps each to a localized
           AD_Message label and a chip tone. */
        public const string STATUS_TODAY = "TODAY";
        public const string STATUS_SCHEDULED = "SCHEDULED";
        public const string STATUS_ON_HOLD = "HOLD";

        /* C_Recurring.RecurringType stored codes (list reference), shared with the
           sibling Recurring widgets. Returned raw; the client resolves the labels. */
        public const string RECURRINGTYPE_GLJournal = "B";
        public const string RECURRINGTYPE_GLJournalBatch = "G";
        public const string RECURRINGTYPE_Invoice = "I";
        public const string RECURRINGTYPE_Project = "J";
        public const string RECURRINGTYPE_Order = "O";
        public const string RECURRINGTYPE_Payment = "P";

        // ─────────────────────────────────────────────────────────────────────
        // §1  The queue
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns one page of the recurring setups whose next run falls today or
        /// later, soonest first.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="page">Zero-based page index; negative values are treated as 0.</param>
        /// <param name="pageSize">Rows per page; clamped to [1, PAGESIZE_MAX].</param>
        /// <returns>Populated <see cref="UpcomingSchedulesPage"/> (never null). Loaded
        /// is false only when the context is missing or a query failed; a tenant with
        /// nothing queued returns Loaded=true and an empty list.</returns>
        public UpcomingSchedulesPage GetUpcomingSchedules(Ctx ctx, int page, int pageSize)
        {
            UpcomingSchedulesPage result = new UpcomingSchedulesPage();
            result.Loaded = false;
            result.Rows = new List<UpcomingScheduleRow>();

            if (ctx == null) { return result; }

            /* Paging inputs arrive from the client, so they are clamped rather than
               trusted - an unbounded page size would defeat the point of paging. */
            if (page < 0) { page = 0; }
            if (pageSize <= 0) { pageSize = PAGESIZE_DEFAULT; }
            if (pageSize > PAGESIZE_MAX) { pageSize = PAGESIZE_MAX; }

            result.Page = page;
            result.PageSize = pageSize;

            DateTime today = DateTime.Now.Date;

            try
            {
                /* Count first, so the pager knows the dataset size and a page index
                   past the end can be corrected instead of returning an empty list
                   with no explanation. */
                result.TotalRows = CountQueue(ctx, today);

                int totalPages = result.TotalRows > 0
                    ? (int)Math.Ceiling((double)result.TotalRows / pageSize)
                    : 1;
                if (page > totalPages - 1) { page = totalPages - 1; }
                if (page < 0) { page = 0; }
                result.Page = page;

                if (result.TotalRows > 0)
                {
                    LoadRows(ctx, today, page * pageSize, pageSize, result);
                }

                result.Loaded = true;
            }
            catch (Exception ex)
            {
                /* Caught here so the caller can tell "query failed" apart from
                   "nothing is queued" - the widget renders those two cases
                   differently. */
                Log.Log(Level.SEVERE, "VAS_222_UpcomingSchedules.GetUpcomingSchedules AD_Client_ID="
                    + ctx.GetAD_Client_ID() + " Page=" + page, ex);
                result.Loaded = false;
                result.Rows.Clear();
            }

            return result;
        }

        /// <summary>
        /// Counts the setups in the queue. Shares its predicate with
        /// <see cref="LoadRows"/> through <see cref="QueuePredicate"/> so the pager
        /// total can never disagree with the rows behind it.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="today">Server date the queue starts from.</param>
        /// <returns>Number of queued setups.</returns>
        private int CountQueue(Ctx ctx, DateTime today)
        {
            StringBuilder sql = new StringBuilder();
            sql.Append(@"
                SELECT COUNT(1) AS Queued_Count
                FROM C_Recurring r");
            sql.Append(QueuePredicate());

            /* MRole only on the main physical table (C_Recurring / alias r). It
               supplies the organisation access clause, so no org predicate is written
               by hand above. */
            string finalSql = MRole.GetDefault(ctx).AddAccessSQL(
                sql.ToString(), "r", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            List<SqlParameter> parameters = new List<SqlParameter>();
            AddQueueParameters(parameters, ctx, today);

            return Util.GetValueOfInt(DB.ExecuteScalar(finalSql, parameters.ToArray(), null));
        }

        /// <summary>
        /// Reads one page of the queue and folds each setup into a display-ready row.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="today">Server date the queue starts from.</param>
        /// <param name="offset">Rows to skip.</param>
        /// <param name="pageSize">Rows to fetch.</param>
        /// <param name="result">Page envelope whose Rows list is filled in.</param>
        /// <returns>void</returns>
        private void LoadRows(Ctx ctx, DateTime today, int offset, int pageSize, UpcomingSchedulesPage result)
        {
            /* The amount is reported in the source document's OWN currency - no
               conversion. GL and project setups have no schema-backed amount of their
               own and correctly fall through to 0. */
            const string amountExpression = "COALESCE(inv.GrandTotal,ord.GrandTotal,pay.PayAmt,0)";

            const string documentCurrencyExpression =
                "COALESCE(inv.C_Currency_ID,ord.C_Currency_ID,pay.C_Currency_ID)";

            StringBuilder sql = new StringBuilder();
            sql.Append(@"
                SELECT r.C_Recurring_ID AS C_Recurring_ID,
                       r.Name AS Recurring_Name,
                       r.DateNextRun AS Date_Next_Run,
                       r.DateLastRun AS Date_Last_Run,
                       r.RecurringType AS Recurring_Type,
                       r.FrequencyType AS Frequency_Type,
                       r.Frequency AS Frequency_Value,
                       r.RunsMax AS Runs_Max,
                       r.RunsRemaining AS Runs_Remaining,
                       bp.Name AS BPartner_Name,
                       COALESCE(glj.Description,glb.Description) AS Journal_Description,
                       ").Append(amountExpression).Append(@" AS Amount_Document,
                       doccur.ISO_Code AS Amount_Currency_Iso,
                       doccur.CurSymbol AS Amount_Currency_Symbol,
                       doccur.StdPrecision AS Amount_Currency_Precision
                FROM C_Recurring r
                LEFT OUTER JOIN C_Invoice inv ON (inv.C_Invoice_ID=r.C_Invoice_ID)
                LEFT OUTER JOIN C_Order ord ON (ord.C_Order_ID=r.C_Order_ID)
                LEFT OUTER JOIN C_Payment pay ON (pay.C_Payment_ID=r.C_Payment_ID)
                LEFT OUTER JOIN C_Project prj ON (prj.C_Project_ID=r.C_Project_ID)
                LEFT OUTER JOIN GL_Journal glj ON (glj.GL_Journal_ID=r.GL_Journal_ID)
                LEFT OUTER JOIN GL_JournalBatch glb ON (glb.GL_JournalBatch_ID=r.GL_JournalBatch_ID)
                LEFT OUTER JOIN C_BPartner bp ON (bp.C_BPartner_ID=COALESCE(inv.C_BPartner_ID,ord.C_BPartner_ID,pay.C_BPartner_ID,prj.C_BPartner_ID))
                LEFT OUTER JOIN C_Currency doccur ON (doccur.C_Currency_ID=").Append(documentCurrencyExpression).Append(@")");
            sql.Append(QueuePredicate());

            /* MRole only on the main physical table (C_Recurring / alias r). It
               supplies the organisation access clause, so no org predicate is written
               by hand above. */
            string finalSql = MRole.GetDefault(ctx).AddAccessSQL(
                sql.ToString(), "r", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            /* Soonest first, then by name, with the surrogate key breaking a tie so
               paging is stable and a row can never appear on two pages. Appended after
               AddAccessSQL by design, and the paging suffix after that. */
            finalSql += @"
                ORDER BY r.DateNextRun,r.Name,r.C_Recurring_ID";
            finalSql += PagingSuffix(pageSize, offset);

            List<SqlParameter> parameters = new List<SqlParameter>();
            AddQueueParameters(parameters, ctx, today);

            DataSet ds = DB.ExecuteDataset(finalSql, parameters.ToArray(), null);
            if (ds == null || ds.Tables.Count == 0) { return; }

            DataTable dt = ds.Tables[0];
            for (int i = 0; i < dt.Rows.Count; i++)
            {
                DataRow dr = dt.Rows[i];

                DateTime? nextRun = Util.GetValueOfDateTime(dr["Date_Next_Run"]);
                DateTime? lastRun = Util.GetValueOfDateTime(dr["Date_Last_Run"]);
                int runsRemaining = Util.GetValueOfInt(dr["Runs_Remaining"]);

                /* Standard precision of the row's OWN currency, so a zero-decimal
                   currency is not printed with two. Defaults to 2 when the setup has
                   no source document to take a currency from. */
                int precision = Util.GetValueOfInt(dr["Amount_Currency_Precision"]);
                if (precision < 0 || precision > 6) { precision = 2; }

                UpcomingScheduleRow row = new UpcomingScheduleRow();
                row.C_Recurring_ID = Util.GetValueOfInt(dr["C_Recurring_ID"]);
                row.RecurringName = Util.GetValueOfString(dr["Recurring_Name"]);
                row.DateNextRun = nextRun.HasValue ? nextRun.Value.ToString("yyyy-MM-dd") : "";
                row.DateLastRun = lastRun.HasValue ? lastRun.Value.ToString("yyyy-MM-dd") : "";
                row.RecurringType = Util.GetValueOfString(dr["Recurring_Type"]);
                row.FrequencyType = Util.GetValueOfString(dr["Frequency_Type"]);
                row.Frequency = Util.GetValueOfInt(dr["Frequency_Value"]);
                row.RunsMax = Util.GetValueOfInt(dr["Runs_Max"]);
                row.RunsRemaining = runsRemaining;
                row.BPartnerName = Util.GetValueOfString(dr["BPartner_Name"]);
                row.JournalDescription = Util.GetValueOfString(dr["Journal_Description"]);
                row.Amount = Util.GetValueOfDecimal(dr["Amount_Document"]);
                row.AmountCurrencyIso = Util.GetValueOfString(dr["Amount_Currency_Iso"]);
                row.AmountCurrencySymbol = Util.GetValueOfString(dr["Amount_Currency_Symbol"]);
                row.AmountPrecision = precision;

                bool isDueToday = nextRun.HasValue && nextRun.Value.Date == today;

                if (runsRemaining <= 0) { row.StatusCode = STATUS_ON_HOLD; }
                else if (isDueToday) { row.StatusCode = STATUS_TODAY; }
                else { row.StatusCode = STATUS_SCHEDULED; }

                /* MRecurring.ExecuteRun only generates when DateNextRun IS today (it
                   compares the stored value against the current date and otherwise
                   returns a "not yet due" message without creating anything), and it
                   throws outright when no runs are left. Both conditions are surfaced
                   here so the widget can disable the row action instead of offering a
                   button that would quietly do nothing. */
                row.CanGenerate = isDueToday && runsRemaining > 0;

                result.Rows.Add(row);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // §2  Generate one schedule
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Runs one recurring setup through the standard MRecurring.ExecuteRun path -
        /// the same code the Recurring process uses - creating the next document in
        /// its series.
        ///
        /// This CREATES a document (invoice / order / payment / journal / project) and
        /// decrements the setup's remaining runs. It is not reversible from here, so
        /// the caller is expected to have confirmed the action with the user first.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="C_Recurring_ID">Setup to run.</param>
        /// <returns>Populated <see cref="GenerateResult"/> (never null); Success is
        /// false with a Message when the setup is not due, has no runs left, or the
        /// run failed.</returns>
        public GenerateResult GenerateRun(Ctx ctx, int C_Recurring_ID)
        {
            GenerateResult result = new GenerateResult();
            result.Success = false;
            result.C_Recurring_ID = C_Recurring_ID;

            if (ctx == null || C_Recurring_ID <= 0)
            {
                result.MessageCode = "InvalidRequest";
                return result;
            }

            Trx trx = Trx.GetTrx(Trx.CreateTrxName("VAS222_Generate"));

            try
            {
                MRecurring recurring = new MRecurring(ctx, C_Recurring_ID, trx);

                /* The id arrives from the client, so tenant ownership is re-checked
                   here rather than assumed from the list the client was shown. */
                if (recurring.Get_ID() != C_Recurring_ID
                    || recurring.GetAD_Client_ID() != ctx.GetAD_Client_ID())
                {
                    result.MessageCode = "RecordNotFound";
                    return result;
                }

                if (!recurring.IsActive())
                {
                    result.MessageCode = "RecordNotFound";
                    return result;
                }

                if (recurring.GetRunsRemaining() <= 0)
                {
                    result.MessageCode = "VAS_222_NoRunsLeft";
                    return result;
                }

                DateTime? nextRun = recurring.GetDateNextRun();
                if (nextRun.HasValue && nextRun.Value.Date != DateTime.Now.Date)
                {
                    /* ExecuteRun would return its own "not due yet" text without
                       creating anything. Refuse up front so the caller gets a clear
                       reason instead of a silent no-op reported as success. */
                    result.MessageCode = "VAS_222_NotDueYet";
                    result.DateNextRun = nextRun.Value.ToString("yyyy-MM-dd");
                    return result;
                }

                string message = recurring.ExecuteRun();

                trx.Commit();

                result.Success = true;
                result.Message = message;
                result.RunsRemaining = recurring.GetRunsRemaining();

                DateTime? newNextRun = recurring.GetDateNextRun();
                result.DateNextRun = newNextRun.HasValue ? newNextRun.Value.ToString("yyyy-MM-dd") : "";
            }
            catch (Exception ex)
            {
                try { trx.Rollback(); } catch (Exception rollbackEx) {
                    Log.Log(Level.WARNING, "VAS_222_UpcomingSchedules.GenerateRun rollback failed for C_Recurring_ID="
                        + C_Recurring_ID, rollbackEx);
                }

                Log.Log(Level.SEVERE, "VAS_222_UpcomingSchedules.GenerateRun C_Recurring_ID=" + C_Recurring_ID, ex);
                result.Success = false;
                result.MessageCode = "VAS_222_GenerateFailed";
                result.Message = ex.Message;
            }
            finally
            {
                /* A started transaction must be closed and released on every return
                   path, success or failure, so no connection is left open. */
                if (trx != null)
                {
                    trx.Close();
                    trx = null;
                }
            }

            return result;
        }

        // ─────────────────────────────────────────────────────────────────────
        // §3  Shared query fragments
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The WHERE clause shared by the count and the row page, so the pager total
        /// and the rows can never be produced by two slightly different predicates.
        ///
        /// No org predicate is written here: MRole.AddAccessSQL appends the
        /// organisation access clause for the main table itself, so restating it
        /// would duplicate the filter and risk disagreeing with the role's own rule.
        ///
        /// Note the deliberate absence of a RunsRemaining filter. The sibling KPI
        /// widgets exclude exhausted setups, but this widget has an explicit "On Hold"
        /// row state for exactly that case - filtering them out would make that state
        /// unreachable and hide setups the user needs to notice and top up.
        ///
        /// The date bound is half-open on the lower side only (DateNextRun >= today):
        /// the queue runs forward with no end, and comparing the raw column keeps it
        /// index-usable instead of wrapping it in TRUNC / CAST.
        /// </summary>
        /// <returns>WHERE clause fragment.</returns>
        private string QueuePredicate()
        {
            return @"
                WHERE r.IsActive='Y'
                  AND r.AD_Client_ID IN (@AD_Client_ID)
                  AND r.DateNextRun>=@DateFrom";
        }

        /// <summary>
        /// Adds the binds for <see cref="QueuePredicate"/> in the order their
        /// placeholders appear in the statement - the provider binds positionally, so
        /// the predicate and its parameters must be built from the same rule.
        /// </summary>
        /// <param name="parameters">Parameter list being built, in statement order.</param>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="today">Server date the queue starts from.</param>
        /// <returns>void</returns>
        private void AddQueueParameters(List<SqlParameter> parameters, Ctx ctx, DateTime today)
        {
            parameters.Add(new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()));
            parameters.Add(new SqlParameter("@DateFrom", today));
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

        // ─────────────────────────────────────────────────────────────────────
        // §4  Contracts
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>One page of the upcoming-schedules queue.</summary>
        public class UpcomingSchedulesPage
        {
            /// <summary>False only when the data could not be read. A tenant with
            /// nothing queued is Loaded=true with an empty list.</summary>
            public bool Loaded { get; set; }

            /// <summary>Zero-based page index actually served, after clamping.</summary>
            public int Page { get; set; }

            public int PageSize { get; set; }

            /// <summary>Size of the whole queue, not of this page.</summary>
            public int TotalRows { get; set; }

            public List<UpcomingScheduleRow> Rows { get; set; }
        }

        /// <summary>One queued recurring setup.</summary>
        public class UpcomingScheduleRow
        {
            public int C_Recurring_ID { get; set; }
            public string RecurringName { get; set; }

            /// <summary>yyyy-MM-dd. Date formatting for display is done client-side.</summary>
            public string DateNextRun { get; set; }

            public string DateLastRun { get; set; }

            /// <summary>C_Recurring.RecurringType stored code (B/G/I/J/O/P).</summary>
            public string RecurringType { get; set; }

            /// <summary>C_Recurring.FrequencyType stored code (D/W/M/Q).</summary>
            public string FrequencyType { get; set; }

            public int Frequency { get; set; }
            public int RunsMax { get; set; }
            public int RunsRemaining { get; set; }

            /// <summary>Partner of the source document; empty for GL setups, which the
            /// client labels as internal.</summary>
            public string BPartnerName { get; set; }

            /// <summary>Journal / journal-batch description, shown beside the internal
            /// label for GL setups.</summary>
            public string JournalDescription { get; set; }

            /// <summary>Source-document amount, untouched and in its own currency -
            /// nothing is converted.</summary>
            public decimal Amount { get; set; }

            public string AmountCurrencyIso { get; set; }
            public string AmountCurrencySymbol { get; set; }
            public int AmountPrecision { get; set; }

            /// <summary>One of the STATUS_* tokens; the client resolves the label.</summary>
            public string StatusCode { get; set; }

            /// <summary>False when generating now would do nothing - the setup is not
            /// due today, or it has no runs left.</summary>
            public bool CanGenerate { get; set; }
        }

        /// <summary>Outcome of one Generate action.</summary>
        public class GenerateResult
        {
            public bool Success { get; set; }
            public int C_Recurring_ID { get; set; }

            /// <summary>AD_Message key describing why the run was refused; empty on
            /// success. The client resolves the label.</summary>
            public string MessageCode { get; set; }

            /// <summary>Raw text from the framework run (already localized by it), or
            /// the exception text on failure. Shown as supporting detail only.</summary>
            public string Message { get; set; }

            /// <summary>Setup state after a successful run, so the row can be updated
            /// without a full reload.</summary>
            public int RunsRemaining { get; set; }

            public string DateNextRun { get; set; }
        }
    }
}
