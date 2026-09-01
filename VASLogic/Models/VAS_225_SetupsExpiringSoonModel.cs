/******************************************************
 * Module Name    : VASLogic
 * Purpose        : Setups Expiring Soon dashboard widget data
 * chronological  : Development
 * Created Date   : 2026-08-31
 * Created by     : VAI154
 ******************************************************/

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;

namespace VASLogic.Models
{
    /// <summary>
    /// Module Name : VAS_225_SetupsExpiringSoon
    /// Purpose     : Backs the VAS_225_SetupsExpiringSoonWidget dashboard widget
    ///               (Recurring module, 3x2 list). Answers "which recurring setups
    ///               run out completely within the next 60 days?" - that is, which
    ///               ones reach RunsRemaining = 0 inside the window.
    ///
    ///               C_Recurring carries no end-date column, so the end has to be
    ///               projected: it is the date of the LAST remaining run, reached by
    ///               stepping DateNextRun forward (RunsRemaining - 1) times at the
    ///               setup's own frequency.
    ///
    ///               That projection is done in code, not in SQL, for two reasons.
    ///               It removes the PostgreSQL INTERVAL / Oracle ADD_MONTHS split the
    ///               build pack needed, and it lets the schedule be stepped ONE RUN AT
    ///               A TIME, exactly the way MRecurring.SetDateNextRun advances it,
    ///               rather than multiplying the interval. That matters at month ends:
    ///               stepping 31 Jan by one month twice lands on 28 Mar, while adding
    ///               two months at once lands on 31 Mar. Only the first agrees with
    ///               what the platform will actually generate.
    ///
    ///               Because the end date is derived rather than stored, the window
    ///               filter is applied in two stages:
    ///                 1. SQL narrows to the only rows that CAN qualify. The last run
    ///                    is never earlier than the next one, so a setup whose
    ///                    DateNextRun already falls beyond the window cannot possibly
    ///                    end inside it. That is a sound necessary condition and it
    ///                    keeps the candidate set small.
    ///                 2. Code projects each candidate's end and keeps the ones that
    ///                    land on or before the window's last day.
    ///               The whole filtered list is returned in one response and the
    ///               widget pages through it client-side.
    ///
    ///               No display text is produced by the query. The stored
    ///               RecurringType code is returned raw and the client resolves the
    ///               label from AD_Message.
    ///
    ///               MRole row-level security is applied to the single main physical
    ///               table the widget fetches from (C_Recurring, alias r). There is no
    ///               join and no CTE, so there is no secondary alias to exclude.
    ///               ORDER BY and the candidate cap are appended AFTER AddAccessSQL so
    ///               the FROM-clause parser is not confused by a trailing clause.
    ///
    ///               The tenant always comes from the authenticated context; the
    ///               client never supplies AD_Client_ID.
    /// Chronological development:
    ///   VAI154      2026-08-31 Created
    /// </summary>
    public class VAS_225_SetupsExpiringSoonModel
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_225_SetupsExpiringSoonModel).FullName);

        /// <summary>Default look-ahead window, in days, matching the widget design.</summary>
        public const int WINDOW_DAYS_DEFAULT = 60;

        /// <summary>Smallest accepted window (today only).</summary>
        public const int WINDOW_DAYS_MIN = 1;

        /// <summary>Largest accepted window - beyond this the list stops meaning
        /// "expiring soon".</summary>
        public const int WINDOW_DAYS_MAX = 365;

        /// <summary>Default rows per page when the client asks for none.</summary>
        public const int PAGESIZE_DEFAULT = 4;

        /// <summary>Largest accepted page size - the widget sizes its own page from
        /// the cell height, so anything beyond this is not a real request.</summary>
        public const int PAGESIZE_MAX = 50;

        /// <summary>Ceiling on how many candidate setups are read before projecting.
        /// A safety valve, not a business rule: the SQL stage already narrows to
        /// setups whose next run falls inside the window, and hitting this cap is
        /// logged rather than passed over in silence.</summary>
        public const int CANDIDATE_LIMIT = 500;

        /* C_Recurring.FrequencyType stored codes (list reference). The stepping below
           mirrors MRecurring.SetDateNextRun for each of them. */
        public const string FREQUENCYTYPE_Daily = "D";
        public const string FREQUENCYTYPE_Weekly = "W";
        public const string FREQUENCYTYPE_Monthly = "M";
        public const string FREQUENCYTYPE_Quarterly = "Q";

        /// <summary>
        /// Returns one page of the active setups that run out completely - reach zero
        /// remaining runs - on or before the last day of the window, soonest end
        /// first.
        ///
        /// The page is cut server side: the response carries only the requested slice
        /// plus the true size of the filtered set, so the client never holds rows it
        /// is not showing. The database read still spans the candidate set rather than
        /// one page, because the filter is on a projected date that no column holds -
        /// see the class summary.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="page">Zero-based page index; negative values are treated as 0.</param>
        /// <param name="pageSize">Rows per page; clamped to [1, PAGESIZE_MAX].</param>
        /// <param name="windowDays">Look-ahead window in days; clamped to
        /// [WINDOW_DAYS_MIN, WINDOW_DAYS_MAX].</param>
        /// <returns>Populated <see cref="ExpiringSoonInfo"/> (never null). Loaded is
        /// false only when the context is missing or the query failed; a tenant with
        /// nothing ending in the window returns Loaded=true with an empty list.</returns>
        public ExpiringSoonInfo GetExpiringSoon(Ctx ctx, int page, int pageSize, int windowDays)
        {
            ExpiringSoonInfo result = new ExpiringSoonInfo();
            result.Loaded = false;
            result.Rows = new List<ExpiringSoonRow>();

            if (ctx == null) { return result; }

            /* All three arrive from the client, so all three are clamped rather than
               trusted - an unbounded page size would defeat the point of paging. */
            if (page < 0) { page = 0; }
            if (pageSize <= 0) { pageSize = PAGESIZE_DEFAULT; }
            if (pageSize > PAGESIZE_MAX) { pageSize = PAGESIZE_MAX; }
            if (windowDays < WINDOW_DAYS_MIN) { windowDays = WINDOW_DAYS_DEFAULT; }
            if (windowDays > WINDOW_DAYS_MAX) { windowDays = WINDOW_DAYS_MAX; }

            DateTime today = DateTime.Now.Date;
            DateTime dateToInclusive = today.AddDays(windowDays);

            result.Page = page;
            result.PageSize = pageSize;
            result.WindowDays = windowDays;
            result.DateFrom = today.ToString("yyyy-MM-dd");
            result.DateTo = dateToInclusive.ToString("yyyy-MM-dd");

            try
            {
                List<ExpiringSoonRow> matched = BuildMatchedRows(ctx, today, dateToInclusive, result);

                result.TotalRows = matched.Count;

                /* A stale page index is corrected rather than returning an empty list
                   with no explanation - the widget echoes back whatever page it is
                   actually given. */
                int totalPages = matched.Count > 0
                    ? (int)Math.Ceiling((double)matched.Count / pageSize)
                    : 1;
                if (page > totalPages - 1) { page = totalPages - 1; }
                if (page < 0) { page = 0; }
                result.Page = page;

                int offset = page * pageSize;
                if (offset < matched.Count)
                {
                    int take = Math.Min(pageSize, matched.Count - offset);
                    result.Rows = matched.GetRange(offset, take);
                }

                result.Loaded = true;
            }
            catch (Exception ex)
            {
                /* Caught here so the caller can tell "query failed" apart from
                   "nothing is expiring" - the widget renders those two cases
                   differently. */
                Log.Log(Level.SEVERE, "VAS_225_SetupsExpiringSoon.GetExpiringSoon AD_Client_ID="
                    + ctx.GetAD_Client_ID() + " WindowDays=" + windowDays, ex);
                result.Loaded = false;
                result.Rows.Clear();
            }

            return result;
        }

        /// <summary>
        /// Reads the candidate setups and keeps the ones whose projected end lands
        /// inside the window, soonest end first. The caller cuts the page from this.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="today">First day of the window.</param>
        /// <param name="dateToInclusive">Last day of the window.</param>
        /// <param name="result">Envelope whose Truncated flag is set when the
        /// candidate cap bites.</param>
        /// <returns>Every matching setup, ordered for display.</returns>
        private List<ExpiringSoonRow> BuildMatchedRows(Ctx ctx, DateTime today, DateTime dateToInclusive, ExpiringSoonInfo result)
        {
            /* Stage 1 - narrow in SQL to the rows that CAN qualify.
               The last remaining run is never earlier than the next one, so any setup
               whose DateNextRun already falls past the window cannot end inside it.
               Filtering on the raw column keeps it index-usable, and the half-open
               upper bound tolerates a DateNextRun that carries a time part.
               No org predicate: MRole.AddAccessSQL appends the organisation access
               clause for the main table itself. */
            string sql = @"
                SELECT r.C_Recurring_ID AS C_Recurring_ID,
                       r.Name AS Recurring_Name,
                       r.RecurringType AS Recurring_Type,
                       r.FrequencyType AS Frequency_Type,
                       r.Frequency AS Frequency_Value,
                       r.RunsMax AS Runs_Max,
                       r.RunsRemaining AS Runs_Remaining,
                       r.DateNextRun AS Date_Next_Run
                FROM C_Recurring r
                WHERE r.IsActive='Y'
                  AND r.AD_Client_ID IN (@AD_Client_ID)
                  AND COALESCE(r.RunsRemaining,0)>0
                  AND r.DateNextRun<@DateToExclusive";

            /* MRole only on the main physical table (C_Recurring / alias r). It
               supplies the organisation access clause, and the explicit tenant filter
               above is a second, independent guard rather than the only one. */
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "r", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            /* Fewest runs left first, so if the safety cap ever bites it keeps the
               setups closest to running out. Appended after AddAccessSQL by design,
               the cap after that. */
            sql += @"
                ORDER BY r.RunsRemaining,r.DateNextRun,r.Name,r.C_Recurring_ID";
            sql += RowLimitSuffix(CANDIDATE_LIMIT);

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@DateToExclusive", dateToInclusive.AddDays(1))
            };

            List<ExpiringSoonRow> matched = new List<ExpiringSoonRow>();

            DataSet ds = DB.ExecuteDataset(sql, parameters, null);
            if (ds == null || ds.Tables.Count == 0) { return matched; }

            DataTable dt = ds.Tables[0];

            if (dt.Rows.Count >= CANDIDATE_LIMIT)
            {
                /* The cap is a safety valve, so say when it bites rather than letting
                   a truncated list read as a complete one. */
                Log.Log(Level.WARNING, "VAS_225_SetupsExpiringSoon: candidate cap of " + CANDIDATE_LIMIT
                    + " reached for AD_Client_ID=" + ctx.GetAD_Client_ID()
                    + ". Setups ending furthest out may be missing from the list.");
                result.Truncated = true;
            }

            for (int i = 0; i < dt.Rows.Count; i++)
            {
                DataRow dr = dt.Rows[i];

                DateTime? nextRun = Util.GetValueOfDateTime(dr["Date_Next_Run"]);
                string frequencyType = Util.GetValueOfString(dr["Frequency_Type"]);
                int frequency = Util.GetValueOfInt(dr["Frequency_Value"]);
                int runsRemaining = Util.GetValueOfInt(dr["Runs_Remaining"]);

                /* Stage 2 - project the end and keep only the setups that actually run
                   out inside the window. A setup with plenty of runs left has a next
                   run inside the window but an end far beyond it, and is dropped
                   here. */
                DateTime? projectedEnd = ProjectEndDate(nextRun, frequencyType, frequency, runsRemaining);
                if (!projectedEnd.HasValue) { continue; }
                if (projectedEnd.Value.Date > dateToInclusive) { continue; }

                ExpiringSoonRow row = new ExpiringSoonRow();
                row.C_Recurring_ID = Util.GetValueOfInt(dr["C_Recurring_ID"]);
                row.RecurringName = Util.GetValueOfString(dr["Recurring_Name"]);
                row.RecurringType = Util.GetValueOfString(dr["Recurring_Type"]);
                row.FrequencyType = frequencyType;
                row.Frequency = frequency;
                row.RunsMax = Util.GetValueOfInt(dr["Runs_Max"]);
                row.RunsRemaining = runsRemaining;
                row.DateNextRun = nextRun.HasValue ? nextRun.Value.ToString("yyyy-MM-dd") : "";
                row.ProjectedEndDate = projectedEnd.Value.ToString("yyyy-MM-dd");

                matched.Add(row);
            }

            /* Soonest to run out first - that is the widget's whole point. The SQL
               order was chosen for the cap, not for display, so the projected end
               drives the final order. */
            matched.Sort(delegate (ExpiringSoonRow left, ExpiringSoonRow right)
            {
                int byDate = string.Compare(left.ProjectedEndDate, right.ProjectedEndDate, StringComparison.Ordinal);
                if (byDate != 0) { return byDate; }

                int byRuns = left.RunsRemaining.CompareTo(right.RunsRemaining);
                if (byRuns != 0) { return byRuns; }

                return string.Compare(left.RecurringName, right.RecurringName, StringComparison.CurrentCulture);
            });

            return matched;
        }

        /// <summary>
        /// Projects the date of a setup's LAST remaining run by stepping its schedule
        /// forward one run at a time, exactly as MRecurring.SetDateNextRun does:
        /// daily adds Frequency days, weekly adds 7 x Frequency days, monthly adds
        /// Frequency months and quarterly adds 3 x Frequency months. Frequency below 1
        /// is coerced to 1, again matching the framework.
        ///
        /// Stepping rather than multiplying is deliberate. Repeated month arithmetic
        /// clamps at short months and does not commute: 31 Jan stepped twice by one
        /// month is 28 Mar, while 31 Jan plus two months is 31 Mar. The platform will
        /// produce the former, so the projection must too.
        /// </summary>
        /// <param name="dateNextRun">The next scheduled run; null when unscheduled.</param>
        /// <param name="frequencyType">Stored FrequencyType code.</param>
        /// <param name="frequency">Stored Frequency; coerced to at least 1.</param>
        /// <param name="runsRemaining">Runs still to generate, including the next one.</param>
        /// <returns>Date of the final run, or null when it cannot be projected.</returns>
        private DateTime? ProjectEndDate(DateTime? dateNextRun, string frequencyType, int frequency, int runsRemaining)
        {
            if (!dateNextRun.HasValue) { return null; }
            if (runsRemaining <= 1) { return dateNextRun.Value.Date; }

            if (frequency < 1) { frequency = 1; }

            /* An unrecognised frequency type cannot be stepped, so the schedule cannot
               be projected at all. Dropping the row is more honest than treating the
               next run as the end and reporting a setup as expiring when it may not
               be. */
            if (frequencyType != FREQUENCYTYPE_Daily
                && frequencyType != FREQUENCYTYPE_Weekly
                && frequencyType != FREQUENCYTYPE_Monthly
                && frequencyType != FREQUENCYTYPE_Quarterly)
            {
                return null;
            }

            DateTime cursor = dateNextRun.Value.Date;

            /* RunsRemaining includes the next run, so the last one is that many steps
               minus one further on. */
            for (int step = 1; step < runsRemaining; step++)
            {
                if (frequencyType == FREQUENCYTYPE_Daily) { cursor = cursor.AddDays(frequency); }
                else if (frequencyType == FREQUENCYTYPE_Weekly) { cursor = cursor.AddDays(7 * frequency); }
                else if (frequencyType == FREQUENCYTYPE_Monthly) { cursor = cursor.AddMonths(frequency); }
                else { cursor = cursor.AddMonths(3 * frequency); }
            }

            return cursor;
        }

        /// <summary>
        /// Database-specific row cap: FETCH FIRST on Oracle, LIMIT elsewhere. The value
        /// is a server-side constant, never client text.
        /// </summary>
        /// <param name="rowCount">Maximum rows to read.</param>
        /// <returns>Row-limit clause.</returns>
        private string RowLimitSuffix(int rowCount)
        {
            if (rowCount <= 0) { rowCount = CANDIDATE_LIMIT; }

            if (DB.IsOracle())
            {
                return " FETCH FIRST " + rowCount + " ROWS ONLY";
            }
            return " LIMIT " + rowCount;
        }

        /// <summary>
        /// Result envelope for the widget: every setup ending inside the window, and
        /// the window it was measured against.
        /// </summary>
        public class ExpiringSoonInfo
        {
            /// <summary>False only when the data could not be read. A tenant with
            /// nothing ending in the window is Loaded=true with an empty list.</summary>
            public bool Loaded { get; set; }

            /// <summary>Zero-based page index actually served, after clamping.</summary>
            public int Page { get; set; }

            public int PageSize { get; set; }

            /// <summary>Resolved look-ahead window in days, after clamping.</summary>
            public int WindowDays { get; set; }

            /// <summary>First day of the window, yyyy-MM-dd.</summary>
            public string DateFrom { get; set; }

            /// <summary>Last day of the window (inclusive), yyyy-MM-dd.</summary>
            public string DateTo { get; set; }

            /// <summary>Size of the whole filtered set, not of this page.</summary>
            public int TotalRows { get; set; }

            /// <summary>True when the candidate cap was reached and setups ending
            /// furthest out may be missing.</summary>
            public bool Truncated { get; set; }

            /// <summary>This page only, soonest to run out first.</summary>
            public List<ExpiringSoonRow> Rows { get; set; }
        }

        /// <summary>One setup that runs out inside the window.</summary>
        public class ExpiringSoonRow
        {
            public int C_Recurring_ID { get; set; }
            public string RecurringName { get; set; }

            /// <summary>C_Recurring.RecurringType stored code (B/G/I/J/O/P).</summary>
            public string RecurringType { get; set; }

            /// <summary>C_Recurring.FrequencyType stored code (D/W/M/Q).</summary>
            public string FrequencyType { get; set; }

            public int Frequency { get; set; }
            public int RunsMax { get; set; }

            /// <summary>Runs still to generate, including the next one.</summary>
            public int RunsRemaining { get; set; }

            /// <summary>yyyy-MM-dd. Date formatting for display is done client-side.</summary>
            public string DateNextRun { get; set; }

            /// <summary>Projected date of the final run - the day the setup reaches
            /// zero remaining runs, yyyy-MM-dd.</summary>
            public string ProjectedEndDate { get; set; }
        }
    }
}
