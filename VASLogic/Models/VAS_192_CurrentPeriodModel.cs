/******************************************************
 * Module Name    : VASLogic
 * Purpose        : Current Period dashboard widget data
 * chronological  : Development
 * Created Date   : 2026-08-19
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
    /// Module Name : VAS_192_CurrentPeriod
    /// Purpose     : Backs the VAS_192_CurrentPeriodWidget dashboard widget.
    ///               Resolves the accounting period that contains the current
    ///               application date for the logged-in tenant, walking
    ///               AD_ClientInfo.C_Calendar_ID -> C_Year -> C_Period and reading
    ///               every active C_PeriodControl row of that period so one
    ///               deterministic overall status can be summarised in code.
    ///               Nothing about the month, year, period number or status is
    ///               hard-coded and no other tenant's calendar is ever considered.
    ///               MRole row-level security is applied to the single main
    ///               physical table the widget fetches from (C_Period, alias p);
    ///               the joined C_Year / C_Calendar / AD_ClientInfo / C_PeriodControl
    ///               rows are lookup and child tables and inherit that filter.
    ///               ORDER BY is appended AFTER AddAccessSQL so the FROM-clause
    ///               parser is not confused by a trailing clause. Compatible with
    ///               PostgreSQL and Oracle.
    /// Chronological development:
    ///   VAI154      2026-08-19 Created
    /// </summary>
    public class VAS_192_CurrentPeriodModel
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_192_CurrentPeriodModel).FullName);

        /* Overall status tokens returned to the client. The client maps each token
           to a localized AD_Message label and a badge tone, so no display text is
           ever produced by the query or by this layer. */
        public const string STATUS_OPEN = "OPEN";
        public const string STATUS_PARTIALLY_OPEN = "PARTIAL";
        public const string STATUS_CLOSED = "CLOSED";
        public const string STATUS_PERMANENTLY_CLOSED = "PERMCLOSED";
        public const string STATUS_NEVER_OPENED = "NEVER";
        public const string STATUS_NOT_CONFIGURED = "NOTCONFIG";
        public const string STATUS_NO_PERIOD = "NOPERIOD";

        /* C_PeriodControl.PeriodStatus stored codes (list reference). */
        private const string PERIODSTATUS_Open = "O";
        private const string PERIODSTATUS_Closed = "C";
        private const string PERIODSTATUS_NeverOpened = "N";
        private const string PERIODSTATUS_PermanentlyClosed = "P";

        /* C_Period.PeriodType stored code for a standard accounting period. */
        private const string PERIODTYPE_StandardPeriod = "S";

        /// <summary>
        /// Resolves the accounting period containing the current application date
        /// for the session tenant.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <returns>Populated <see cref="CurrentPeriodInfo"/>; Found is false when
        /// the current date falls outside every active period of the tenant calendar.</returns>
        public CurrentPeriodInfo GetCurrentPeriod(Ctx ctx)
        {
            return GetCurrentPeriod(ctx, DateTime.Now);
        }

        /// <summary>
        /// Resolves the accounting period containing <paramref name="currentDate"/>.
        /// The date parameter exists so the rule can be exercised on period
        /// boundaries in testing; production callers use the overload above, which
        /// always passes the current application date.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="currentDate">Date to locate inside the tenant calendar.</param>
        /// <returns>Populated <see cref="CurrentPeriodInfo"/> (never null).</returns>
        public CurrentPeriodInfo GetCurrentPeriod(Ctx ctx, DateTime currentDate)
        {
            CurrentPeriodInfo result = new CurrentPeriodInfo();
            result.Found = false;
            result.StatusCode = STATUS_NO_PERIOD;

            if (ctx == null) { return result; }

            /* Compare date values only - StartDate / EndDate may carry a time
               component in either backend. Oracle DATE always carries a time part
               (TRUNC drops it); PostgreSQL needs an explicit CAST when the column
               is materialised as a timestamp. Only the COLUMN side is normalised -
               the bind value is already midnight (currentDate.Date), and casting a
               bind variable leaves its type undetermined on PostgreSQL. Both bounds
               are inclusive, so a date landing exactly on StartDate or EndDate
               belongs to the period. */
            string dateCondition;
            if (DB.IsOracle())
            {
                dateCondition = "TRUNC(p.StartDate)<=@PeriodFromDate AND TRUNC(p.EndDate)>=@PeriodToDate";
            }
            else
            {
                dateCondition = "CAST(p.StartDate AS DATE)<=@PeriodFromDate AND CAST(p.EndDate AS DATE)>=@PeriodToDate";
            }

            /* One lightweight request: the period, its year/calendar descriptors and
               every active control row of that period come back together, so the
               overall status never needs a second round trip or a query per
               DocBaseType. C_Period is the main physical table; AD_ClientInfo pins
               the calendar to the tenant through AD_ClientInfo.C_Calendar_ID rather
               than searching all calendars. */
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
                       cal.Name AS Calendar_Name,
                       pc.C_PeriodControl_ID AS C_PeriodControl_ID,
                       pc.DocBaseType AS Doc_Base_Type,
                       pc.PeriodStatus AS Period_Status
                FROM C_Period p
                INNER JOIN C_Year y ON (y.C_Year_ID=p.C_Year_ID)
                INNER JOIN C_Calendar cal ON (cal.C_Calendar_ID=y.C_Calendar_ID)
                INNER JOIN AD_ClientInfo ci ON (ci.C_Calendar_ID=cal.C_Calendar_ID)
                LEFT OUTER JOIN C_PeriodControl pc ON (pc.C_Period_ID=p.C_Period_ID AND pc.IsActive='Y')
                WHERE ci.AD_Client_ID=@AD_Client_ID
                  AND cal.IsActive='Y'
                  AND y.IsActive='Y'
                  AND p.IsActive='Y'
                  AND " + dateCondition;

            /* MRole only on the main physical table (C_Period / alias p). It also
               supplies the p.AD_Client_ID / p.AD_Org_ID predicates, so the tenant
               filter is enforced on the fetched table as well as on AD_ClientInfo. */
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "p", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            /* Standard periods first so an overlapping adjustment period can never
               win the selection; DocBaseType keeps the control rows in a stable,
               reproducible order. Appended after AddAccessSQL by design. */
            sql += @"
                ORDER BY CASE WHEN p.PeriodType='" + PERIODTYPE_StandardPeriod + @"' THEN 0 ELSE 1 END,
                         p.PeriodNo,
                         p.C_Period_ID,
                         pc.DocBaseType";

            /* Three bind names, each occurring exactly once, listed in the order
               they appear in the statement: @AD_Client_ID, then @PeriodFromDate,
               then @PeriodToDate. The two date binds carry the same value but need
               distinct names because the provider binds positionally. The tenant
               always comes from the authenticated context, never from the client. */
            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@PeriodFromDate", currentDate.Date),
                new SqlParameter("@PeriodToDate", currentDate.Date)
            };

            DataSet ds = DB.ExecuteDataset(sql, parameters, null);
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
            {
                /* No active period of the tenant calendar contains the current date.
                   Report that honestly - never fall back to the latest period, the
                   previous period, the first open period or the calendar month. */
                return result;
            }

            DataTable dt = ds.Tables[0];

            /* The ordering above makes row 0 the deterministic winner (standard
               period first, then lowest PeriodNo / C_Period_ID). */
            int selectedPeriodId = Util.GetValueOfInt(dt.Rows[0]["C_Period_ID"]);

            result.Found = true;
            result.C_Period_ID = selectedPeriodId;
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

            /* A date should sit inside exactly one standard period. When more than
               one active period matches, the configuration is wrong - surface it
               instead of hiding it, and still return the deterministic winner. */
            List<int> matchedPeriodIds = new List<int>();
            for (int i = 0; i < dt.Rows.Count; i++)
            {
                int periodId = Util.GetValueOfInt(dt.Rows[i]["C_Period_ID"]);
                if (!matchedPeriodIds.Contains(periodId)) { matchedPeriodIds.Add(periodId); }
            }

            if (matchedPeriodIds.Count > 1)
            {
                result.HasOverlap = true;
                Log.Log(Level.WARNING, "VAS_192_CurrentPeriod: " + matchedPeriodIds.Count
                    + " active periods contain " + currentDate.ToString("yyyy-MM-dd")
                    + " for AD_Client_ID=" + ctx.GetAD_Client_ID()
                    + ". Using C_Period_ID=" + selectedPeriodId
                    + ". Check the accounting calendar for overlapping periods.");
            }

            /* Collect the active control rows of the SELECTED period only. The
               LEFT OUTER JOIN yields one row with a null C_PeriodControl_ID when a
               period has no active control at all. */
            int openCount = 0;
            int closedCount = 0;
            int permanentlyClosedCount = 0;
            int neverOpenedCount = 0;
            int totalCount = 0;

            for (int i = 0; i < dt.Rows.Count; i++)
            {
                if (Util.GetValueOfInt(dt.Rows[i]["C_Period_ID"]) != selectedPeriodId) { continue; }
                if (Util.GetValueOfInt(dt.Rows[i]["C_PeriodControl_ID"]) <= 0) { continue; }

                totalCount++;
                string status = Util.GetValueOfString(dt.Rows[i]["Period_Status"]);
                if (status == PERIODSTATUS_Open) { openCount++; }
                else if (status == PERIODSTATUS_Closed) { closedCount++; }
                else if (status == PERIODSTATUS_PermanentlyClosed) { permanentlyClosedCount++; }
                else if (status == PERIODSTATUS_NeverOpened) { neverOpenedCount++; }
            }

            result.OpenControlCount = openCount;
            result.TotalControlCount = totalCount;
            result.StatusCode = SummarizeStatus(openCount, closedCount, permanentlyClosedCount, neverOpenedCount, totalCount);

            return result;
        }

        /// <summary>
        /// Reduces every active C_PeriodControl row of one period to the single
        /// status the badge shows. Control is maintained per DocBaseType, so the
        /// summary must represent the complete configuration - never one arbitrary
        /// row picked with MAX / MIN / FETCH FIRST.
        /// </summary>
        /// <param name="openCount">Controls with PeriodStatus 'O'.</param>
        /// <param name="closedCount">Controls with PeriodStatus 'C'.</param>
        /// <param name="permanentlyClosedCount">Controls with PeriodStatus 'P'.</param>
        /// <param name="neverOpenedCount">Controls with PeriodStatus 'N'.</param>
        /// <param name="totalCount">Total active controls of the period.</param>
        /// <returns>One of the STATUS_* tokens.</returns>
        private string SummarizeStatus(int openCount, int closedCount, int permanentlyClosedCount, int neverOpenedCount, int totalCount)
        {
            if (totalCount <= 0) { return STATUS_NOT_CONFIGURED; }
            if (openCount == totalCount) { return STATUS_OPEN; }
            if (openCount > 0) { return STATUS_PARTIALLY_OPEN; }
            if (permanentlyClosedCount == totalCount) { return STATUS_PERMANENTLY_CLOSED; }
            if (neverOpenedCount == totalCount) { return STATUS_NEVER_OPENED; }
            if (closedCount > 0) { return STATUS_CLOSED; }

            /* Every control carries a status outside the four known codes. Treat it
               as a configuration gap rather than inventing a label for it. */
            return STATUS_NOT_CONFIGURED;
        }

        /// <summary>
        /// Renders a period boundary as an ISO date string. Date/currency
        /// formatting for display is done client-side; this is only a stable,
        /// culture-independent transport format.
        /// </summary>
        /// <param name="value">Raw DataRow value.</param>
        /// <returns>yyyy-MM-dd, or an empty string when the value is null.</returns>
        private string FormatDate(object value)
        {
            if (value == null || value == DBNull.Value) { return ""; }
            DateTime? parsed = Util.GetValueOfDateTime(value);
            return parsed.HasValue ? parsed.Value.ToString("yyyy-MM-dd") : "";
        }

        /// <summary>
        /// Result envelope for the widget: the resolved period, its calendar/year
        /// descriptors and the summarised control status.
        /// </summary>
        public class CurrentPeriodInfo
        {
            /// <summary>False when no active period of the tenant calendar contains the current date.</summary>
            public bool Found { get; set; }

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

            /// <summary>One of the STATUS_* tokens; the client resolves the label.</summary>
            public string StatusCode { get; set; }

            public int OpenControlCount { get; set; }
            public int TotalControlCount { get; set; }

            /// <summary>True when more than one active period contains the current date.</summary>
            public bool HasOverlap { get; set; }
        }
    }
}
