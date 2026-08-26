/******************************************************
 * Module Name    : VASLogic
 * Purpose        : Previous Period dashboard widget data
 * chronological  : Development
 * Created Date   : 2026-08-19
 * Created by     : VAI154
 ******************************************************/

using System;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;

namespace VASLogic.Models
{
    /// <summary>
    /// Module Name : VAS_193_PreviousPeriod
    /// Purpose     : Backs the VAS_193_PreviousPeriodWidget dashboard widget.
    ///               Resolves the accounting period that immediately PRECEDES the
    ///               period containing the current application date, for the
    ///               logged-in tenant only.
    ///
    ///               Two steps, both driven by real C_Period rows - nothing is
    ///               derived from calendar months or from PeriodNo arithmetic:
    ///                 1. The current period is resolved by reusing
    ///                    VAS_192_CurrentPeriodModel (same calendar walk:
    ///                    AD_ClientInfo.C_Calendar_ID -> C_Year -> C_Period).
    ///                 2. The previous period is the active C_Period of the SAME
    ///                    calendar and PeriodType with the greatest StartDate that
    ///                    is still earlier than the current period's StartDate.
    ///                    Because the search is by StartDate across the whole
    ///                    calendar - not inside one C_Year - the first period of a
    ///                    fiscal year correctly returns the last period of the
    ///                    PREVIOUS fiscal year.
    ///
    ///               The summarised badge status comes from every active
    ///               C_PeriodControl row of the resolved period, reduced by the
    ///               shared VAS_192_CurrentPeriodModel.SummarizeStatus rule so both
    ///               widgets can never drift apart.
    ///
    ///               MRole row-level security is applied to the main physical table
    ///               of each query (C_Period alias p, C_PeriodControl alias pc); the
    ///               joined C_Year / C_Calendar rows are lookup tables and inherit
    ///               that filter. ORDER BY is appended AFTER AddAccessSQL so the
    ///               FROM-clause parser is not confused by a trailing clause.
    ///               Compatible with PostgreSQL and Oracle.
    /// Chronological development:
    ///   VAI154      2026-08-19 Created
    /// </summary>
    public class VAS_193_PreviousPeriodModel
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_193_PreviousPeriodModel).FullName);

        /* Transport format produced by VAS_192_CurrentPeriodModel.FormatDate. */
        private const string TRANSPORT_DATE_FORMAT = "yyyy-MM-dd";

        /* C_Period.PeriodType stored code for a standard accounting period; used
           only as the fallback when the current period carries no type. */
        private const string PERIODTYPE_StandardPeriod = "S";

        /// <summary>
        /// Resolves the accounting period preceding the one that contains the
        /// current application date for the session tenant.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <returns>Populated <see cref="PreviousPeriodInfo"/> (never null).</returns>
        public PreviousPeriodInfo GetPreviousPeriod(Ctx ctx)
        {
            return GetPreviousPeriod(ctx, DateTime.Now);
        }

        /// <summary>
        /// Resolves the accounting period preceding the one that contains
        /// <paramref name="currentDate"/>. The date parameter exists so the rule can
        /// be exercised on period and fiscal-year boundaries in testing; production
        /// callers use the overload above, which always passes the current
        /// application date.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="currentDate">Date whose period the search starts from.</param>
        /// <returns>Populated <see cref="PreviousPeriodInfo"/>; Found is false when
        /// the current date sits outside every active period, or when the current
        /// period is the very first period of the tenant calendar.</returns>
        public PreviousPeriodInfo GetPreviousPeriod(Ctx ctx, DateTime currentDate)
        {
            PreviousPeriodInfo result = new PreviousPeriodInfo();
            result.Found = false;
            result.StatusCode = VAS_192_CurrentPeriodModel.STATUS_NO_PERIOD;

            if (ctx == null) { return result; }

            /* Step 1 - locate the current period. Reused wholesale so both widgets
               agree on which period "today" belongs to, including the standard-period
               preference and the overlap warning. */
            VAS_192_CurrentPeriodModel.CurrentPeriodInfo current =
                new VAS_192_CurrentPeriodModel().GetCurrentPeriod(ctx, currentDate);

            if (current == null || !current.Found)
            {
                /* Without an anchor period there is nothing to step back from. Say so
                   honestly - never fall back to the latest period or to "last month". */
                return result;
            }

            result.CurrentPeriodFound = true;
            result.CurrentC_Period_ID = current.C_Period_ID;
            result.CurrentPeriodName = current.PeriodName;

            DateTime currentStartDate;
            if (!DateTime.TryParseExact(current.StartDate, TRANSPORT_DATE_FORMAT,
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out currentStartDate))
            {
                /* C_Period.StartDate is mandatory, so an unparseable value means the
                   calendar row itself is broken. Report it rather than guessing. */
                Log.Log(Level.WARNING, "VAS_193_PreviousPeriod: C_Period_ID=" + current.C_Period_ID
                    + " has no usable StartDate for AD_Client_ID=" + ctx.GetAD_Client_ID()
                    + ". Cannot resolve the previous period.");
                return result;
            }

            /* Step 2 - the previous period. Compare date values only: Oracle DATE
               always carries a time part (TRUNC drops it), PostgreSQL needs an
               explicit CAST when the column is materialised as a timestamp. Only the
               COLUMN side is normalised - the bind value is already midnight, and
               casting a bind variable leaves its type undetermined on PostgreSQL.
               The bound is strict (<) so the current period can never select itself. */
            string dateCondition;
            if (DB.IsOracle())
            {
                dateCondition = "TRUNC(p.StartDate)<@CurrentStartDate";
            }
            else
            {
                dateCondition = "CAST(p.StartDate AS DATE)<@CurrentStartDate";
            }

            /* Searched across the whole calendar, not inside one C_Year, so the
               period before the first period of a fiscal year is the last period of
               the preceding fiscal year. PeriodType is pinned to the current
               period's type so an adjustment period never poses as the previous
               standard period. */
            string sql = @"
                SELECT p.C_Period_ID AS C_Period_ID,
                       p.Name AS Period_Name,
                       p.PeriodNo AS Period_No,
                       p.StartDate AS Start_Date,
                       p.EndDate AS End_Date,
                       p.PeriodType AS Period_Type,
                       y.C_Year_ID AS C_Year_ID,
                       y.FiscalYear AS Fiscal_Year,
                       cal.C_Calendar_ID AS C_Calendar_ID,
                       cal.Name AS Calendar_Name
                FROM C_Period p
                INNER JOIN C_Year y ON (y.C_Year_ID=p.C_Year_ID)
                INNER JOIN C_Calendar cal ON (cal.C_Calendar_ID=y.C_Calendar_ID)
                WHERE cal.C_Calendar_ID=@C_Calendar_ID
                  AND cal.IsActive='Y'
                  AND y.IsActive='Y'
                  AND p.IsActive='Y'
                  AND p.PeriodType=@PeriodType
                  AND " + dateCondition;

            /* MRole only on the main physical table (C_Period / alias p); it also
               supplies the p.AD_Client_ID / p.AD_Org_ID predicates. */
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "p", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            /* Latest qualifying period first, so row 0 is the previous period.
               C_Period_ID breaks a tie deterministically. Appended after
               AddAccessSQL by design. */
            sql += @"
                ORDER BY p.StartDate DESC,
                         p.C_Period_ID DESC";

            /* Bind names listed in the order they appear in the statement:
               @C_Calendar_ID, @PeriodType, @CurrentStartDate. The calendar and the
               period type both come from the resolved current period, never from the
               client. */
            string periodType = string.IsNullOrEmpty(current.PeriodType)
                ? PERIODTYPE_StandardPeriod
                : current.PeriodType;

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@C_Calendar_ID", current.C_Calendar_ID),
                new SqlParameter("@PeriodType", periodType),
                new SqlParameter("@CurrentStartDate", currentStartDate.Date)
            };

            DataSet ds = DB.ExecuteDataset(sql, parameters, null);
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
            {
                /* The current period is the first period of the tenant calendar. */
                return result;
            }

            DataTable dt = ds.Tables[0];

            result.Found = true;
            result.C_Period_ID = Util.GetValueOfInt(dt.Rows[0]["C_Period_ID"]);
            result.PeriodName = Util.GetValueOfString(dt.Rows[0]["Period_Name"]);
            result.PeriodNo = Util.GetValueOfInt(dt.Rows[0]["Period_No"]);
            result.PeriodNoDisplay = result.PeriodNo.ToString("00");
            result.PeriodType = Util.GetValueOfString(dt.Rows[0]["Period_Type"]);
            result.StartDate = FormatDate(dt.Rows[0]["Start_Date"]);
            result.EndDate = FormatDate(dt.Rows[0]["End_Date"]);
            result.C_Year_ID = Util.GetValueOfInt(dt.Rows[0]["C_Year_ID"]);
            result.FiscalYear = Util.GetValueOfString(dt.Rows[0]["Fiscal_Year"]);
            result.C_Calendar_ID = Util.GetValueOfInt(dt.Rows[0]["C_Calendar_ID"]);
            result.CalendarName = Util.GetValueOfString(dt.Rows[0]["Calendar_Name"]);

            /* The previous period belongs to an earlier fiscal year whenever the
               current period is the first one of its year - a normal, expected case
               that the meta line surfaces through the fiscal year name. */
            result.IsPriorFiscalYear = (result.C_Year_ID != current.C_Year_ID);

            /* Two active periods of the same type starting on the same day make the
               "previous" period ambiguous. Surface the configuration problem instead
               of hiding it behind the deterministic winner.
               Rows come back ordered by StartDate DESC, so only the row directly
               behind the winner can tie with it. */
            if (dt.Rows.Count > 1)
            {
                DateTime? winnerStart = Util.GetValueOfDateTime(dt.Rows[0]["Start_Date"]);
                DateTime? runnerUpStart = Util.GetValueOfDateTime(dt.Rows[1]["Start_Date"]);

                if (winnerStart.HasValue && runnerUpStart.HasValue
                    && winnerStart.Value.Date == runnerUpStart.Value.Date)
                {
                    result.HasOverlap = true;
                    Log.Log(Level.WARNING, "VAS_193_PreviousPeriod: more than one active period starts on "
                        + winnerStart.Value.ToString(TRANSPORT_DATE_FORMAT)
                        + " for AD_Client_ID=" + ctx.GetAD_Client_ID()
                        + ". Using C_Period_ID=" + result.C_Period_ID
                        + ". Check the accounting calendar for overlapping periods.");
                }
            }

            LoadControlStatus(ctx, result);

            return result;
        }

        /// <summary>
        /// Reads every active C_PeriodControl row of the resolved period and folds
        /// them into the single badge status through the shared summary rule.
        /// Kept as a second, narrow query so the period lookup above returns one row
        /// per period instead of one row per period and DocBaseType.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="result">Resolved period; its StatusCode and control counts are filled in.</param>
        /// <returns>void</returns>
        private void LoadControlStatus(Ctx ctx, PreviousPeriodInfo result)
        {
            string sql = @"
                SELECT pc.C_PeriodControl_ID AS C_PeriodControl_ID,
                       pc.DocBaseType AS Doc_Base_Type,
                       pc.PeriodStatus AS Period_Status
                FROM C_PeriodControl pc
                WHERE pc.C_Period_ID=@C_Period_ID
                  AND pc.IsActive='Y'";

            /* C_PeriodControl is the main physical table of this query. */
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "pc", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            /* Stable, reproducible order; appended after AddAccessSQL by design. */
            sql += @"
                ORDER BY pc.DocBaseType";

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@C_Period_ID", result.C_Period_ID)
            };

            DataSet ds = DB.ExecuteDataset(sql, parameters, null);

            int openCount = 0;
            int closedCount = 0;
            int permanentlyClosedCount = 0;
            int neverOpenedCount = 0;
            int totalCount = 0;

            if (ds != null && ds.Tables.Count > 0)
            {
                DataTable dt = ds.Tables[0];
                for (int i = 0; i < dt.Rows.Count; i++)
                {
                    if (Util.GetValueOfInt(dt.Rows[i]["C_PeriodControl_ID"]) <= 0) { continue; }

                    totalCount++;
                    string status = Util.GetValueOfString(dt.Rows[i]["Period_Status"]);
                    if (status == VAS_192_CurrentPeriodModel.PERIODSTATUS_Open) { openCount++; }
                    else if (status == VAS_192_CurrentPeriodModel.PERIODSTATUS_Closed) { closedCount++; }
                    else if (status == VAS_192_CurrentPeriodModel.PERIODSTATUS_PermanentlyClosed) { permanentlyClosedCount++; }
                    else if (status == VAS_192_CurrentPeriodModel.PERIODSTATUS_NeverOpened) { neverOpenedCount++; }
                }
            }

            result.OpenControlCount = openCount;
            result.TotalControlCount = totalCount;
            result.StatusCode = VAS_192_CurrentPeriodModel.SummarizeStatus(
                openCount, closedCount, permanentlyClosedCount, neverOpenedCount, totalCount);
        }

        /// <summary>
        /// Renders a period boundary as an ISO date string. Date formatting for
        /// display is done client-side; this is only a stable, culture-independent
        /// transport format.
        /// </summary>
        /// <param name="value">Raw DataRow value.</param>
        /// <returns>yyyy-MM-dd, or an empty string when the value is null.</returns>
        private string FormatDate(object value)
        {
            if (value == null || value == DBNull.Value) { return ""; }
            DateTime? parsed = Util.GetValueOfDateTime(value);
            return parsed.HasValue ? parsed.Value.ToString(TRANSPORT_DATE_FORMAT) : "";
        }

        /// <summary>
        /// Result envelope for the widget: the resolved previous period, its
        /// calendar/year descriptors and the summarised control status.
        /// </summary>
        public class PreviousPeriodInfo
        {
            /// <summary>False when there is no period before the current one, or no current period at all.</summary>
            public bool Found { get; set; }

            /// <summary>True when the current date did fall inside an active period; lets the client tell "no calendar" from "no earlier period".</summary>
            public bool CurrentPeriodFound { get; set; }

            /// <summary>The anchor period the search stepped back from.</summary>
            public int CurrentC_Period_ID { get; set; }

            public string CurrentPeriodName { get; set; }

            public int C_Period_ID { get; set; }
            public string PeriodName { get; set; }
            public int PeriodNo { get; set; }

            /// <summary>Two-digit PeriodNo for display only; the stored value is unchanged.</summary>
            public string PeriodNoDisplay { get; set; }

            public string PeriodType { get; set; }
            public string StartDate { get; set; }
            public string EndDate { get; set; }
            public int C_Year_ID { get; set; }
            public string FiscalYear { get; set; }
            public int C_Calendar_ID { get; set; }
            public string CalendarName { get; set; }

            /// <summary>True when the previous period sits in an earlier fiscal year than the current one.</summary>
            public bool IsPriorFiscalYear { get; set; }

            /// <summary>One of the VAS_192_CurrentPeriodModel.STATUS_* tokens; the client resolves the label.</summary>
            public string StatusCode { get; set; }

            public int OpenControlCount { get; set; }
            public int TotalControlCount { get; set; }

            /// <summary>True when more than one active period competes for "previous".</summary>
            public bool HasOverlap { get; set; }
        }
    }
}
