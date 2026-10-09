/******************************************************
 * Module Name    : VASLogic
 * Purpose        : Records Generated dashboard widget data
 * chronological  : Development
 * Created Date   : 2026-08-31
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
    /// Module Name : VAS_221_RecordGenerated
    /// Purpose     : Backs the VAS_221_RecordGeneratedWidget dashboard widget
    ///               (Recurring module, 2x1 KPI + drill-down modal). Answers
    ///               "how many records did recurring runs generate in the current
    ///               period, and how does that compare with the period before?".
    ///
    ///               The reporting period is the tenant's ACCOUNTING period, not a
    ///               calendar month: the current and previous periods are resolved by
    ///               reusing VAS_192_CurrentPeriodModel / VAS_193_PreviousPeriodModel
    ///               so this widget, the Period Control widgets and the ledger all
    ///               agree on where a period starts and ends. That matters for any
    ///               tenant whose fiscal calendar is not twelve calendar months
    ///               (4-4-5, 13-period, offset year-end). When the tenant has no
    ///               usable accounting calendar the model falls back to calendar
    ///               months and says so through IsCalendarFallback, rather than
    ///               silently reporting a different window than the label implies.
    ///
    ///               Both period windows are expressed half-open
    ///               (DateDoc >= From AND DateDoc < ToExclusive) rather than with
    ///               BETWEEN over a truncated column: C_Recurring_Run.DateDoc can
    ///               carry a time part on Oracle, and wrapping the column in
    ///               TRUNC / CAST would hide those rows and stop the column being
    ///               used as an index.
    ///
    ///               The KPI counts and the drill-down rows are separate requests on
    ///               purpose. A busy tenant generates four-figure run counts in a
    ///               period, so the row list is paged in the database rather than
    ///               materialised whole and sliced in the browser.
    ///
    ///               MRole row-level security is applied to the main physical table
    ///               of every query (C_Recurring_Run, alias rr). The parent setup,
    ///               document, journal, project, partner and currency joins are
    ///               lookups that inherit that filter. There is no CTE, so no CTE
    ///               alias is passed to MRole. ORDER BY and the paging suffix are
    ///               appended AFTER AddAccessSQL so the FROM-clause parser is not
    ///               confused by a trailing clause.
    ///
    ///               The tenant always comes from the authenticated context; the
    ///               client never supplies AD_Client_ID and never supplies the date
    ///               range - both period windows are derived server side.
    ///               Compatible with PostgreSQL and Oracle.
    /// Chronological development:
    ///   VAI154      2026-08-31 Created
    /// </summary>
    public class VAS_221_RecordGeneratedModel
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_221_RecordGeneratedModel).FullName);

        /// <summary>Default drill-down page size.</summary>
        public const int PAGESIZE_DEFAULT = 10;

        /// <summary>Largest accepted drill-down page size - guards the row volume of
        /// a list that can run to four figures.</summary>
        public const int PAGESIZE_MAX = 100;

        /* Transport format shared with the period models. */
        private const string TRANSPORT_DATE_FORMAT = "yyyy-MM-dd";

        /* Derived document-type codes, aligned with C_Recurring.RecurringType so the
           client can use one label map for the whole Recurring family. Returned raw;
           the client resolves each to a localized AD_Message label, so no display
           text is ever produced by the query or by this layer. */
        public const string RECURRINGTYPE_GLJournal = "B";
        public const string RECURRINGTYPE_GLJournalBatch = "G";
        public const string RECURRINGTYPE_Invoice = "I";
        public const string RECURRINGTYPE_Project = "J";
        public const string RECURRINGTYPE_Order = "O";
        public const string RECURRINGTYPE_Payment = "P";

        // ─────────────────────────────────────────────────────────────────────
        // §1  KPI - current period, previous period, change
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Counts the records generated by recurring runs in the tenant's current
        /// accounting period and in the period before it, and derives the change
        /// between them.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <returns>Populated <see cref="RecordsGeneratedInfo"/> (never null). Loaded
        /// is false only when the context is missing or a query failed; a tenant that
        /// generated nothing returns Loaded=true with zero counts - zero is a real
        /// answer, not an error state.</returns>
        public RecordsGeneratedInfo GetRecordsGenerated(Ctx ctx)
        {
            RecordsGeneratedInfo result = new RecordsGeneratedInfo();
            result.Loaded = false;

            if (ctx == null) { return result; }

            try
            {
                PeriodWindow current = ResolveCurrentWindow(ctx);
                PeriodWindow previous = ResolvePreviousWindow(ctx, current);

                result.IsCalendarFallback = current.IsCalendarFallback;

                result.CurrentPeriodName = current.Name;
                result.CurrentDateFrom = current.From.ToString(TRANSPORT_DATE_FORMAT);
                result.CurrentDateTo = current.ToInclusive.ToString(TRANSPORT_DATE_FORMAT);

                result.PreviousPeriodFound = previous.Found;
                result.PreviousPeriodName = previous.Name;
                result.PreviousDateFrom = previous.Found ? previous.From.ToString(TRANSPORT_DATE_FORMAT) : "";
                result.PreviousDateTo = previous.Found ? previous.ToInclusive.ToString(TRANSPORT_DATE_FORMAT) : "";

                result.CurrentCount = CountRuns(ctx, current.From, current.ToExclusive);
                result.PreviousCount = previous.Found ? CountRuns(ctx, previous.From, previous.ToExclusive) : 0;

                /* Change is only meaningful against a non-zero base. Growth "from
                   zero" is not a percentage, so it is reported as null and the client
                   shows the comparison as unavailable rather than inventing an
                   infinity or a misleading 100%. */
                if (previous.Found && result.PreviousCount > 0)
                {
                    double change = ((double)(result.CurrentCount - result.PreviousCount) / result.PreviousCount) * 100d;
                    result.ChangePercent = Math.Round(change, 1);
                }
                else
                {
                    result.ChangePercent = null;
                }

                result.Loaded = true;
            }
            catch (Exception ex)
            {
                /* Caught here so the caller can tell "query failed" apart from
                   "nothing was generated" - the widget renders those two cases
                   differently. */
                Log.Log(Level.SEVERE, "VAS_221_RecordGenerated.GetRecordsGenerated AD_Client_ID="
                    + ctx.GetAD_Client_ID(), ex);
                result.Loaded = false;
                result.CurrentCount = 0;
                result.PreviousCount = 0;
                result.ChangePercent = null;
            }

            return result;
        }

        /// <summary>
        /// Counts active recurring runs whose DateDoc falls inside a half-open
        /// window. Shared by the current and previous period so the two figures can
        /// never be produced by two slightly different predicates.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="from">First instant of the window (inclusive).</param>
        /// <param name="toExclusive">First instant after the window (exclusive).</param>
        /// <returns>Number of matching runs.</returns>
        private int CountRuns(Ctx ctx, DateTime from, DateTime toExclusive)
        {
            /* No org predicate is written here: MRole.AddAccessSQL appends the
               organisation access clause for the main table itself, so restating it
               would duplicate the filter and risk disagreeing with the role's own
               rule. */
            StringBuilder sql = new StringBuilder();
            sql.Append(@"
                SELECT COUNT(1) AS Records_Generated
                FROM C_Recurring_Run rr
                WHERE rr.IsActive='Y'
                  AND rr.AD_Client_ID IN (@AD_Client_ID)
                  AND rr.DateDoc>=@DateFrom
                  AND rr.DateDoc<@DateToExclusive");

            /* MRole only on the main physical table (C_Recurring_Run / alias rr). It
               supplies the organisation access clause, and the explicit tenant filter
               above is a second, independent guard rather than the only one. */
            string finalSql = MRole.GetDefault(ctx).AddAccessSQL(
                sql.ToString(), "rr", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            /* The provider binds positionally, so every parameter is added in the
               order its placeholder appears in the statement text. */
            List<SqlParameter> parameters = new List<SqlParameter>();
            parameters.Add(new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()));
            parameters.Add(new SqlParameter("@DateFrom", from));
            parameters.Add(new SqlParameter("@DateToExclusive", toExclusive));

            return Util.GetValueOfInt(DB.ExecuteScalar(finalSql, parameters.ToArray(), null));
        }

        // ─────────────────────────────────────────────────────────────────────
        // §2  Drill-down - the generated documents of the current period
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns one page of the documents generated by recurring runs in the
        /// tenant's current accounting period, newest first.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="page">Zero-based page index; negative values are treated as 0.</param>
        /// <param name="pageSize">Rows per page; clamped to [1, PAGESIZE_MAX].</param>
        /// <returns>Populated <see cref="GeneratedRecordsPage"/> (never null).</returns>
        public GeneratedRecordsPage GetGeneratedRecords(Ctx ctx, int page, int pageSize)
        {
            GeneratedRecordsPage result = new GeneratedRecordsPage();
            result.Loaded = false;
            result.Rows = new List<GeneratedRecordRow>();

            if (ctx == null) { return result; }

            /* Paging inputs arrive from the client, so they are clamped rather than
               trusted - an unbounded page size would defeat the point of paging. */
            if (page < 0) { page = 0; }
            if (pageSize <= 0) { pageSize = PAGESIZE_DEFAULT; }
            if (pageSize > PAGESIZE_MAX) { pageSize = PAGESIZE_MAX; }

            result.Page = page;
            result.PageSize = pageSize;

            try
            {
                /* The window is re-derived here rather than accepted from the client:
                   a caller must not be able to widen the range the KPI advertised. */
                PeriodWindow current = ResolveCurrentWindow(ctx);

                result.IsCalendarFallback = current.IsCalendarFallback;
                result.PeriodName = current.Name;
                result.DateFrom = current.From.ToString(TRANSPORT_DATE_FORMAT);
                result.DateTo = current.ToInclusive.ToString(TRANSPORT_DATE_FORMAT);

                /* Count first, so the pager knows the dataset size and a page index
                   past the end can be corrected instead of returning an empty list
                   with no explanation. */
                result.TotalRows = CountRuns(ctx, current.From, current.ToExclusive);

                int totalPages = result.TotalRows > 0
                    ? (int)Math.Ceiling((double)result.TotalRows / pageSize)
                    : 1;
                if (page > totalPages - 1) { page = totalPages - 1; }
                if (page < 0) { page = 0; }
                result.Page = page;

                if (result.TotalRows > 0)
                {
                    LoadRows(ctx, current, page * pageSize, pageSize, result);
                }

                result.Loaded = true;
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_221_RecordGenerated.GetGeneratedRecords AD_Client_ID="
                    + ctx.GetAD_Client_ID() + " Page=" + page, ex);
                result.Loaded = false;
                result.Rows.Clear();
            }

            return result;
        }

        /// <summary>
        /// Reads one page of generated documents and folds each run row into a
        /// display-ready record.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="window">Resolved current-period window.</param>
        /// <param name="offset">Rows to skip.</param>
        /// <param name="pageSize">Rows to fetch.</param>
        /// <param name="result">Page envelope whose Rows list is filled in.</param>
        /// <returns>void</returns>
        private void LoadRows(Ctx ctx, PeriodWindow window,
            int offset, int pageSize, GeneratedRecordsPage result)
        {
            /* The amount is reported in the generated document's OWN currency - no
               conversion. Each row therefore carries its currency alongside the
               figure, and the list shows a currency column rather than implying one
               shared unit across rows that may not share one. A project run has no
               monetary total of its own and correctly falls through to 0. */
            const string amountExpression =
                "COALESCE(inv.GrandTotal,ord.GrandTotal,pay.PayAmt,glj.TotalDr,glb.TotalDr,0)";

            const string documentCurrencyExpression =
                "COALESCE(inv.C_Currency_ID,ord.C_Currency_ID,pay.C_Currency_ID,glj.C_Currency_ID,glb.C_Currency_ID)";

            /* JOIN ORDER IS LOAD-BEARING - the two COALESCE joins must not be last.
               AccessSqlParser strips each ON condition at its closing parenthesis, and
               for the LAST ON (where there is no following " ON " to search back from)
               it takes the FIRST ')' in the clause - which, with a COALESCE in that
               condition, is the function's. It then leaves a stray ')' behind and reads
               the trailing alias as "doccur)", so MRole emits access predicates against
               a nonexistent alias and the statement fails at the database.

               glj / glb cannot go last here: the currency and amount expressions read
               them, so they must be joined before doccur. C_Recurring r can - nothing
               else's ON references it - so it closes the clause with a function-free
               ON. Any new join with a function in its ON belongs BEFORE it. */
            StringBuilder sql = new StringBuilder();
            sql.Append(@"
                SELECT rr.C_Recurring_Run_ID AS C_Recurring_Run_ID,
                       rr.DateDoc AS Date_Doc,
                       rr.C_Recurring_ID AS C_Recurring_ID,
                       rr.C_Invoice_ID AS Run_C_Invoice_ID,
                       rr.C_Order_ID AS Run_C_Order_ID,
                       rr.C_Payment_ID AS Run_C_Payment_ID,
                       rr.C_Project_ID AS Run_C_Project_ID,
                       rr.GL_Journal_ID AS Run_GL_Journal_ID,
                       rr.GL_JournalBatch_ID AS Run_GL_JournalBatch_ID,
                       r.Name AS Recurring_Name,
                       r.RecurringType AS Recurring_Type,
                       COALESCE(inv.DocumentNo,ord.DocumentNo,pay.DocumentNo,glj.DocumentNo,glb.DocumentNo,prj.Value) AS Document_No,
                       bp.Name AS BPartner_Name,
                       COALESCE(glj.Description,glb.Description) AS Journal_Description,
                       ").Append(amountExpression).Append(@" AS Amount_Document,
                       doccur.ISO_Code AS Amount_Currency_Iso,
                       doccur.CurSymbol AS Amount_Currency_Symbol,
                       doccur.StdPrecision AS Amount_Currency_Precision
                FROM C_Recurring_Run rr
                LEFT OUTER JOIN C_Invoice inv ON (inv.C_Invoice_ID=rr.C_Invoice_ID)
                LEFT OUTER JOIN C_Order ord ON (ord.C_Order_ID=rr.C_Order_ID)
                LEFT OUTER JOIN C_Payment pay ON (pay.C_Payment_ID=rr.C_Payment_ID)
                LEFT OUTER JOIN C_Project prj ON (prj.C_Project_ID=rr.C_Project_ID)
                LEFT OUTER JOIN GL_Journal glj ON (glj.GL_Journal_ID=rr.GL_Journal_ID)
                LEFT OUTER JOIN GL_JournalBatch glb ON (glb.GL_JournalBatch_ID=rr.GL_JournalBatch_ID)
                LEFT OUTER JOIN C_BPartner bp ON (bp.C_BPartner_ID=COALESCE(inv.C_BPartner_ID,ord.C_BPartner_ID,pay.C_BPartner_ID,prj.C_BPartner_ID))
                LEFT OUTER JOIN C_Currency doccur ON (doccur.C_Currency_ID=").Append(documentCurrencyExpression).Append(@")
                LEFT OUTER JOIN C_Recurring r ON (r.C_Recurring_ID=rr.C_Recurring_ID)
                WHERE rr.IsActive='Y'
                  AND rr.AD_Client_ID IN (@AD_Client_ID)
                  AND rr.DateDoc>=@DateFrom
                  AND rr.DateDoc<@DateToExclusive");

            /* MRole only on the main physical table (C_Recurring_Run / alias rr). It
               supplies the organisation access clause, so no org predicate is written
               by hand above - restating it would duplicate the filter and risk
               disagreeing with the role's own rule. */
            string finalSql = MRole.GetDefault(ctx).AddAccessSQL(
                sql.ToString(), "rr", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            /* Newest first, with the surrogate key breaking a same-day tie so paging
               is stable and a row can never appear on two pages. Appended after
               AddAccessSQL by design, and the paging suffix after that. */
            finalSql += @"
                ORDER BY rr.DateDoc DESC,rr.C_Recurring_Run_ID DESC";
            finalSql += PagingSuffix(pageSize, offset);

            /* Parameters in the order their placeholders appear in the statement. Each
               occurrence carries its own unique name because the provider binds
               positionally. */
            List<SqlParameter> parameters = new List<SqlParameter>();
            parameters.Add(new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()));
            parameters.Add(new SqlParameter("@DateFrom", window.From));
            parameters.Add(new SqlParameter("@DateToExclusive", window.ToExclusive));

            DataSet ds = DB.ExecuteDataset(finalSql, parameters.ToArray(), null);
            if (ds == null || ds.Tables.Count == 0) { return; }

            DataTable dt = ds.Tables[0];
            for (int i = 0; i < dt.Rows.Count; i++)
            {
                DataRow dr = dt.Rows[i];

                DateTime? dateDoc = Util.GetValueOfDateTime(dr["Date_Doc"]);

                /* Standard precision of the row's OWN currency, so a zero-decimal
                   currency is not printed with two. Defaults to 2 when the run
                   produced a document that carries no currency. */
                int precision = Util.GetValueOfInt(dr["Amount_Currency_Precision"]);
                if (precision < 0 || precision > 6) { precision = 2; }

                GeneratedRecordRow row = new GeneratedRecordRow();
                row.C_Recurring_Run_ID = Util.GetValueOfInt(dr["C_Recurring_Run_ID"]);
                row.C_Recurring_ID = Util.GetValueOfInt(dr["C_Recurring_ID"]);
                row.DateDoc = dateDoc.HasValue ? dateDoc.Value.ToString(TRANSPORT_DATE_FORMAT) : "";
                row.RecurringName = Util.GetValueOfString(dr["Recurring_Name"]);
                row.DocumentNo = Util.GetValueOfString(dr["Document_No"]);
                row.BPartnerName = Util.GetValueOfString(dr["BPartner_Name"]);
                row.JournalDescription = Util.GetValueOfString(dr["Journal_Description"]);
                row.Amount = Util.GetValueOfDecimal(dr["Amount_Document"]);
                row.AmountCurrencyIso = Util.GetValueOfString(dr["Amount_Currency_Iso"]);
                row.AmountCurrencySymbol = Util.GetValueOfString(dr["Amount_Currency_Symbol"]);
                row.AmountPrecision = precision;

                /* The run row records what was actually created, so the document type
                   is derived from its own foreign keys. The parent setup's
                   RecurringType is only the fallback: a setup can be edited after a
                   run, and the run must keep reporting what it really produced. */
                row.DocumentType = DeriveDocumentType(dr);

                result.Rows.Add(row);
            }
        }

        /// <summary>
        /// Derives the type of document a run produced from the run's own foreign
        /// keys, falling back to the parent setup's RecurringType when the run
        /// carries none.
        /// </summary>
        /// <param name="dr">Run row with the Run_* id columns selected.</param>
        /// <returns>A RECURRINGTYPE_* code, or an empty string when undetermined.</returns>
        private string DeriveDocumentType(DataRow dr)
        {
            if (Util.GetValueOfInt(dr["Run_C_Invoice_ID"]) > 0) { return RECURRINGTYPE_Invoice; }
            if (Util.GetValueOfInt(dr["Run_C_Order_ID"]) > 0) { return RECURRINGTYPE_Order; }
            if (Util.GetValueOfInt(dr["Run_C_Payment_ID"]) > 0) { return RECURRINGTYPE_Payment; }
            if (Util.GetValueOfInt(dr["Run_GL_Journal_ID"]) > 0) { return RECURRINGTYPE_GLJournal; }
            if (Util.GetValueOfInt(dr["Run_GL_JournalBatch_ID"]) > 0) { return RECURRINGTYPE_GLJournalBatch; }
            if (Util.GetValueOfInt(dr["Run_C_Project_ID"]) > 0) { return RECURRINGTYPE_Project; }

            return Util.GetValueOfString(dr["Recurring_Type"]);
        }

        // ─────────────────────────────────────────────────────────────────────
        // §3  Period windows
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The accounting period containing today, reusing the Period Control
        /// resolver so every widget agrees on where the period starts and ends.
        /// Falls back to the calendar month when the tenant has no usable accounting
        /// calendar, and flags that it did.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <returns>Resolved window; never null.</returns>
        private PeriodWindow ResolveCurrentWindow(Ctx ctx)
        {
            DateTime today = DateTime.Now.Date;

            VAS_192_CurrentPeriodModel.CurrentPeriodInfo period =
                new VAS_192_CurrentPeriodModel().GetCurrentPeriod(ctx, today);

            DateTime start;
            DateTime end;
            if (period != null && period.Found
                && TryParseTransportDate(period.StartDate, out start)
                && TryParseTransportDate(period.EndDate, out end))
            {
                return new PeriodWindow
                {
                    Found = true,
                    IsCalendarFallback = false,
                    Name = period.PeriodName,
                    From = start,
                    ToInclusive = end
                };
            }

            /* No accounting calendar covers today. The calendar month keeps the widget
               useful, and IsCalendarFallback lets the client label the period honestly
               instead of implying a fiscal period that was never resolved. */
            DateTime monthStart = new DateTime(today.Year, today.Month, 1);
            return new PeriodWindow
            {
                Found = true,
                IsCalendarFallback = true,
                Name = "",
                From = monthStart,
                ToInclusive = monthStart.AddMonths(1).AddDays(-1)
            };
        }

        /// <summary>
        /// The accounting period immediately before the current one, reusing the
        /// Period Control resolver. When the current window came from the calendar
        /// fallback, the previous window is the preceding calendar month so the two
        /// figures stay comparable.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="current">Already-resolved current window.</param>
        /// <returns>Resolved window; Found is false when there is no earlier period.</returns>
        private PeriodWindow ResolvePreviousWindow(Ctx ctx, PeriodWindow current)
        {
            if (current != null && current.IsCalendarFallback)
            {
                DateTime prevStart = current.From.AddMonths(-1);
                return new PeriodWindow
                {
                    Found = true,
                    IsCalendarFallback = true,
                    Name = "",
                    From = prevStart,
                    ToInclusive = current.From.AddDays(-1)
                };
            }

            VAS_193_PreviousPeriodModel.PreviousPeriodInfo period =
                new VAS_193_PreviousPeriodModel().GetPreviousPeriod(ctx, DateTime.Now.Date);

            DateTime start;
            DateTime end;
            if (period != null && period.Found
                && TryParseTransportDate(period.StartDate, out start)
                && TryParseTransportDate(period.EndDate, out end))
            {
                return new PeriodWindow
                {
                    Found = true,
                    IsCalendarFallback = false,
                    Name = period.PeriodName,
                    From = start,
                    ToInclusive = end
                };
            }

            /* The current period is the first one of the tenant calendar. There is
               nothing to compare against - reported honestly rather than substituted
               with "last month", which would be a different window than the label. */
            return new PeriodWindow { Found = false };
        }

        /// <summary>
        /// Parses a yyyy-MM-dd transport date produced by the period models.
        /// </summary>
        /// <param name="value">Transport date string.</param>
        /// <param name="parsed">Parsed date on success; DateTime.MinValue otherwise.</param>
        /// <returns>True when the value was a usable date.</returns>
        private bool TryParseTransportDate(string value, out DateTime parsed)
        {
            parsed = DateTime.MinValue;
            if (string.IsNullOrEmpty(value)) { return false; }

            return DateTime.TryParseExact(value, TRANSPORT_DATE_FORMAT,
                CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed);
        }

        // ─────────────────────────────────────────────────────────────────────
        // §4  Shared query fragments
        // ─────────────────────────────────────────────────────────────────────

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
        // §5  Contracts
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>Result envelope for the KPI card.</summary>
        public class RecordsGeneratedInfo
        {
            /// <summary>False only when the data could not be read. A tenant that
            /// generated nothing is Loaded=true with zero counts.</summary>
            public bool Loaded { get; set; }

            /// <summary>True when no accounting calendar covered today and calendar
            /// months were used instead; the client labels the period accordingly.</summary>
            public bool IsCalendarFallback { get; set; }

            public int CurrentCount { get; set; }
            public int PreviousCount { get; set; }

            /// <summary>Percentage change against the previous period, rounded to one
            /// decimal. Null when there is no earlier period or its count is zero -
            /// growth from zero is not a percentage.</summary>
            public double? ChangePercent { get; set; }

            /// <summary>C_Period.Name; empty under the calendar fallback, where the
            /// client derives the label from the date range.</summary>
            public string CurrentPeriodName { get; set; }

            /// <summary>yyyy-MM-dd. Date formatting for display is done client-side.</summary>
            public string CurrentDateFrom { get; set; }
            public string CurrentDateTo { get; set; }

            /// <summary>False when the current period is the first of the calendar.</summary>
            public bool PreviousPeriodFound { get; set; }

            public string PreviousPeriodName { get; set; }
            public string PreviousDateFrom { get; set; }
            public string PreviousDateTo { get; set; }
        }

        /// <summary>One page of the drill-down list.</summary>
        public class GeneratedRecordsPage
        {
            public bool Loaded { get; set; }
            public bool IsCalendarFallback { get; set; }

            public string PeriodName { get; set; }
            public string DateFrom { get; set; }
            public string DateTo { get; set; }

            /// <summary>Zero-based page index actually served, after clamping.</summary>
            public int Page { get; set; }

            public int PageSize { get; set; }

            /// <summary>Size of the whole dataset, not of this page.</summary>
            public int TotalRows { get; set; }

            public List<GeneratedRecordRow> Rows { get; set; }
        }

        /// <summary>One document produced by a recurring run.</summary>
        public class GeneratedRecordRow
        {
            public int C_Recurring_Run_ID { get; set; }
            public int C_Recurring_ID { get; set; }

            /// <summary>yyyy-MM-dd. Date formatting for display is done client-side.</summary>
            public string DateDoc { get; set; }

            /// <summary>Document number of the generated document (C_Project uses its
            /// Value).</summary>
            public string DocumentNo { get; set; }

            /// <summary>Name of the setup that produced it.</summary>
            public string RecurringName { get; set; }

            /// <summary>Derived RECURRINGTYPE_* code; the client resolves the label.</summary>
            public string DocumentType { get; set; }

            /// <summary>Partner of the generated document; empty for GL runs, which
            /// the client labels as internal.</summary>
            public string BPartnerName { get; set; }

            /// <summary>Journal / journal-batch description, shown beside the internal
            /// label for GL runs.</summary>
            public string JournalDescription { get; set; }

            /// <summary>Generated-document amount, untouched and in its own currency -
            /// nothing is converted.</summary>
            public decimal Amount { get; set; }

            /// <summary>ISO code of the currency Amount is expressed in; empty when
            /// the generated document carries no currency (a project run).</summary>
            public string AmountCurrencyIso { get; set; }

            /// <summary>Display symbol of the same currency, when one is defined.</summary>
            public string AmountCurrencySymbol { get; set; }

            /// <summary>Standard precision of that currency, so a zero-decimal
            /// currency is not printed with two.</summary>
            public int AmountPrecision { get; set; }
        }

        /// <summary>A resolved reporting window.</summary>
        private class PeriodWindow
        {
            public bool Found { get; set; }
            public bool IsCalendarFallback { get; set; }
            public string Name { get; set; }
            public DateTime From { get; set; }

            /// <summary>Last day of the window, inclusive.</summary>
            public DateTime ToInclusive { get; set; }

            /// <summary>First instant after the window. Half-open upper bound, so a
            /// row stamped with a time on the last day is still inside.</summary>
            public DateTime ToExclusive
            {
                get { return ToInclusive.Date.AddDays(1); }
            }
        }
    }
}
