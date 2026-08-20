/******************************************************
 * Module Name    : VASLogic
 * Purpose        : Period Control Matrix dashboard widget data
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
using VAdvantage.Process;
using VAdvantage.ProcessEngine;
using VAdvantage.Utility;

namespace VASLogic.Models
{
    /// <summary>
    /// Module Name : VAS_196_PeriodControlMatrix
    /// Purpose     : Backs the VAS_196_PeriodControlMatrixWidget dashboard widget.
    ///               Serves the cascading Calendar -> Year -> Period -> PeriodControl
    ///               hierarchy and performs the open/close status change for one
    ///               C_PeriodControl row.
    ///
    ///               Every list is a separate, narrow request so the browser never
    ///               receives the whole hierarchy at once (only the default path is
    ///               bootstrapped in one call, and that is one calendar / one year /
    ///               one period - not the full tree).
    ///
    ///               PeriodStatus is NEVER written by this class. The status change
    ///               follows the standard flow: set C_PeriodControl.PeriodAction
    ///               through MPeriodControl, save, then execute the standard process
    ///               attached to the C_PeriodControl.Processing column
    ///               (VAdvantage.Process.PeriodControlStatus). The AD_Process_ID is
    ///               resolved from AD_Column metadata at runtime - never hard-coded -
    ///               and the resulting status is re-read from the database rather
    ///               than assumed.
    ///
    ///               MRole row-level security is applied to the main physical table
    ///               of every query; joined AD_Table / AD_Column / AD_Ref_List rows
    ///               are dictionary lookups and inherit that filter. GROUP BY /
    ///               ORDER BY are appended AFTER AddAccessSQL so the FROM-clause
    ///               parser is not confused by a trailing clause. Compatible with
    ///               PostgreSQL and Oracle.
    /// Chronological development:
    ///   VAI145      2026-08-19 Created
    /// </summary>
    public class VAS_196_PeriodControlMatrixModel
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_196_PeriodControlMatrixModel).FullName);

        /* C_Period.PeriodType stored code for a standard accounting period. */
        private const string PERIODTYPE_StandardPeriod = "S";

        /* Dictionary coordinates of the standard open/close process. The numeric
           AD_Process_ID is resolved from these at runtime and cached per
           application domain - the ID itself differs per environment and must never
           be hard-coded. */
        private const string TABLENAME_PeriodControl = "C_PeriodControl";
        private const string COLUMNNAME_Processing = "Processing";

        /* Resolved once per app domain; 0 = not looked up yet, -1 = looked up and
           not found (so a broken dictionary is not re-queried on every click). */
        private static int _periodControlProcessId = 0;

        // ─────────────────────────────────────────────────────────────────────
        // §1  Cascading lookups
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Bootstraps the widget: the accessible calendars plus the single default
        /// path (years of the default calendar, periods of the default year) and the
        /// ids the client should preselect. The default path is derived from the
        /// period that contains the current application date, so the widget opens on
        /// the period the user is actually working in.
        /// Only ONE calendar's years and ONE year's periods are returned - the full
        /// hierarchy is never shipped to the browser.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <returns>Populated <see cref="MatrixBootstrap"/> (never null).</returns>
        public MatrixBootstrap GetBootstrap(Ctx ctx)
        {
            MatrixBootstrap result = new MatrixBootstrap();
            result.Calendars = new List<LookupItem>();
            result.Years = new List<LookupItem>();
            result.Periods = new List<LookupItem>();

            if (ctx == null) { return result; }

            result.Calendars = GetCalendars(ctx);
            if (result.Calendars.Count == 0) { return result; }

            /* Preferred default: the calendar / year / period that hold today.
               Reused from the Current Period widget so all Period Control widgets
               agree on what "the current period" is. */
            VAS_192_CurrentPeriodModel.CurrentPeriodInfo current =
                new VAS_192_CurrentPeriodModel().GetCurrentPeriod(ctx);

            int calendarId = 0;
            if (current != null && current.Found && ContainsId(result.Calendars, current.C_Calendar_ID))
            {
                calendarId = current.C_Calendar_ID;
            }
            if (calendarId <= 0) { calendarId = result.Calendars[0].Id; }

            result.Years = GetYears(ctx, calendarId);
            result.C_Calendar_ID = calendarId;
            if (result.Years.Count == 0) { return result; }

            int yearId = 0;
            if (current != null && current.Found && ContainsId(result.Years, current.C_Year_ID))
            {
                yearId = current.C_Year_ID;
            }
            if (yearId <= 0) { yearId = result.Years[0].Id; }

            result.Periods = GetPeriods(ctx, yearId);
            result.C_Year_ID = yearId;
            if (result.Periods.Count == 0) { return result; }

            int periodId = 0;
            if (current != null && current.Found && ContainsId(result.Periods, current.C_Period_ID))
            {
                periodId = current.C_Period_ID;
            }
            if (periodId <= 0) { periodId = result.Periods[0].Id; }

            result.C_Period_ID = periodId;

            return result;
        }

        /// <summary>
        /// Active calendars the role may read.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <returns>Calendar id / name pairs, ordered by name (never null).</returns>
        public List<LookupItem> GetCalendars(Ctx ctx)
        {
            List<LookupItem> items = new List<LookupItem>();
            if (ctx == null) { return items; }

            string sql = @"
                SELECT cal.C_Calendar_ID AS Item_ID,
                       cal.Name AS Item_Name
                FROM C_Calendar cal
                WHERE cal.IsActive='Y'";

            /* MRole supplies the tenant/org predicates on the fetched table. */
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "cal", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            sql += " ORDER BY cal.Name";

            return ReadLookup(sql, null, items);
        }

        /// <summary>
        /// Active fiscal years of one calendar, newest first.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="calendarId">C_Calendar_ID selected by the user.</param>
        /// <returns>Year id / FiscalYear pairs (never null).</returns>
        public List<LookupItem> GetYears(Ctx ctx, int calendarId)
        {
            List<LookupItem> items = new List<LookupItem>();
            if (ctx == null || calendarId <= 0) { return items; }

            string sql = @"
                SELECT y.C_Year_ID AS Item_ID,
                       y.FiscalYear AS Item_Name
                FROM C_Year y
                WHERE y.C_Calendar_ID=@C_Calendar_ID
                  AND y.IsActive='Y'";

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "y", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            sql += " ORDER BY y.FiscalYear DESC";

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@C_Calendar_ID", calendarId)
            };

            return ReadLookup(sql, parameters, items);
        }

        /// <summary>
        /// Active standard periods of one fiscal year, in calendar order. Periods are
        /// always driven by the selected year - never derived month-wise.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="yearId">C_Year_ID selected by the user.</param>
        /// <returns>Period id / name pairs (never null).</returns>
        public List<LookupItem> GetPeriods(Ctx ctx, int yearId)
        {
            List<LookupItem> items = new List<LookupItem>();
            if (ctx == null || yearId <= 0) { return items; }

            string sql = @"
                SELECT p.C_Period_ID AS Item_ID,
                       p.Name AS Item_Name
                FROM C_Period p
                WHERE p.C_Year_ID=@C_Year_ID
                  AND p.IsActive='Y'
                  AND p.PeriodType=@PeriodType";

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "p", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            sql += " ORDER BY p.StartDate,p.PeriodNo";

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@C_Year_ID", yearId),
                new SqlParameter("@PeriodType", PERIODTYPE_StandardPeriod)
            };

            return ReadLookup(sql, parameters, items);
        }

        /// <summary>
        /// Active period controls of one period - one row per DocBaseType (per
        /// organization when the tenant maintains control per org), with the document
        /// base type resolved to its C_DocBaseType name and the owning AD_Org name.
        /// The whole set is returned in one call: it is bounded by the number of
        /// document base types (a handful of rows), so the widget pages it on the
        /// client and a page change costs no round trip.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="periodId">C_Period_ID selected by the user.</param>
        /// <returns>Period control rows ordered by display name (never null).</returns>
        public List<PeriodControlRow> GetPeriodControls(Ctx ctx, int periodId)
        {
            List<PeriodControlRow> rows = new List<PeriodControlRow>();
            if (ctx == null || periodId <= 0) { return rows; }

            string sql = @"
                SELECT pc.C_PeriodControl_ID AS C_PeriodControl_ID,
                       pc.C_Period_ID AS C_Period_ID,
                       pc.AD_Org_ID AS AD_Org_ID,
                       COALESCE(org.Name,N'') AS Org_Name,
                       pc.DocBaseType AS Doc_Base_Type,
                       dbt.C_DocBaseType_ID AS C_DocBaseType_ID,
                       COALESCE(dbt.Name,pc.DocBaseType) AS Doc_Base_Type_Name,
                       pc.PeriodStatus AS Period_Status,
                       pc.PeriodAction AS Period_Action
                FROM C_PeriodControl pc
                INNER JOIN C_DocBaseType dbt ON (dbt.docbasetype = pc.DocBaseType)
                LEFT OUTER JOIN AD_Org org ON (org.AD_Org_ID=pc.AD_Org_ID AND org.IsActive='Y')
                WHERE pc.C_Period_ID=@C_Period_ID
                  AND pc.IsActive='Y'";

            /* C_PeriodControl is the main physical table; the dictionary joins are
               lookups and inherit the filter. */
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "pc", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            /* Tenant-wide controls (AD_Org_ID=0) first, then the org-specific ones
               grouped by org name, and by document base type inside each group -
               so the matrix reads as one block per organization when several exist. */
            sql += " ORDER BY pc.AD_Org_ID,COALESCE(org.Name,N''),COALESCE(dbt.Name,pc.DocBaseType)";

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@C_Period_ID", periodId)
            };

            DataSet ds = DB.ExecuteDataset(sql, parameters, null);
            if (ds == null || ds.Tables.Count == 0) { return rows; }

            DataTable dt = ds.Tables[0];
            for (int i = 0; i < dt.Rows.Count; i++)
            {
                PeriodControlRow row = new PeriodControlRow();
                row.C_PeriodControl_ID = Util.GetValueOfInt(dt.Rows[i]["C_PeriodControl_ID"]);
                row.C_Period_ID = Util.GetValueOfInt(dt.Rows[i]["C_Period_ID"]);
                row.AD_Org_ID = Util.GetValueOfInt(dt.Rows[i]["AD_Org_ID"]);
                row.OrgName = Util.GetValueOfString(dt.Rows[i]["Org_Name"]);
                row.DocBaseType = Util.GetValueOfString(dt.Rows[i]["Doc_Base_Type"]);
                row.C_DocBaseType_ID = Util.GetValueOfInt(dt.Rows[i]["C_DocBaseType_ID"]);
                row.DocBaseTypeName = Util.GetValueOfString(dt.Rows[i]["Doc_Base_Type_Name"]);
                row.PeriodStatus = Util.GetValueOfString(dt.Rows[i]["Period_Status"]);
                row.PeriodAction = Util.GetValueOfString(dt.Rows[i]["Period_Action"]);

                /* A permanently closed control can never be reopened or closed
                   again - the client disables the button, and ChangePeriodStatus
                   rejects it a second time server-side. */
                row.CanToggle = !MPeriodControl.PERIODSTATUS_PermanentlyClosed.Equals(row.PeriodStatus);

                rows.Add(row);
            }

            return rows;
        }

        // ─────────────────────────────────────────────────────────────────────
        // §2  Status change (standard process only)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Toggles one period control between Open and Closed by setting
        /// C_PeriodControl.PeriodAction and executing the standard process attached
        /// to the Processing column. PeriodStatus is never written here; the value
        /// returned to the client is re-read from the database after the process has
        /// finished, so a failed run reports the unchanged status.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="calendarId">C_Calendar_ID the client had selected.</param>
        /// <param name="yearId">C_Year_ID the client had selected.</param>
        /// <param name="periodId">C_Period_ID the client had selected.</param>
        /// <param name="periodControlId">C_PeriodControl_ID of the clicked row.</param>
        /// <returns>Populated <see cref="StatusChangeResult"/> (never null).</returns>
        public StatusChangeResult ChangePeriodStatus(Ctx ctx, int calendarId, int yearId, int periodId, int periodControlId)
        {
            StatusChangeResult result = new StatusChangeResult();
            result.Success = false;
            result.C_PeriodControl_ID = periodControlId;

            if (ctx == null || periodControlId <= 0)
            {
                result.ErrorCode = ERROR_INVALID_REQUEST;
                return result;
            }

            /* 1 - the selection the browser sent must still describe a real,
                   active, accessible hierarchy. Anything else is rejected before a
                   single row is touched. */
            if (!IsHierarchyValid(ctx, calendarId, yearId, periodId, periodControlId))
            {
                result.ErrorCode = ERROR_INVALID_REQUEST;
                return result;
            }

            /* 2 - re-read the record itself; never trust the status the browser
                   painted. */
            MPeriodControl pc = new MPeriodControl(ctx, periodControlId, null);
            if (pc.Get_ID() == 0)
            {
                result.ErrorCode = ERROR_NOT_FOUND;
                return result;
            }

            if (pc.GetAD_Client_ID() != ctx.GetAD_Client_ID() || !pc.IsActive())
            {
                result.ErrorCode = ERROR_INVALID_REQUEST;
                return result;
            }

            string currentStatus = pc.GetPeriodStatus();
            result.PeriodStatus = currentStatus;

            /* Kept in step with PeriodStatus on every exit path below, so a rejected
               or failed attempt repaints the row exactly as it was - only a
               permanently closed control comes back read-only. */
            result.CanToggle = !MPeriodControl.PERIODSTATUS_PermanentlyClosed.Equals(currentStatus);

            /* 3 - a permanently closed control is read-only. Validated here as well
                   as in the UI, because the UI can be bypassed. */
            if (MPeriodControl.PERIODSTATUS_PermanentlyClosed.Equals(currentStatus))
            {
                result.ErrorCode = ERROR_PERMANENTLY_CLOSED;
                return result;
            }

            /* 4 - Open -> Close, Closed / Never opened -> Open. Permanent close is
                   deliberately not reachable from this widget. */
            string action = MPeriodControl.PERIODSTATUS_Open.Equals(currentStatus)
                ? MPeriodControl.PERIODACTION_ClosePeriod
                : MPeriodControl.PERIODACTION_OpenPeriod;

            /* 5 - the process must exist before the action is written, otherwise the
                   record would be left carrying a pending action nothing executes. */
            int processId = GetPeriodControlProcessId(ctx);
            if (processId <= 0)
            {
                result.ErrorCode = ERROR_NO_PROCESS;
                Log.Log(Level.SEVERE, "VAS_196_PeriodControlMatrix: no AD_Process_ID on "
                    + TABLENAME_PeriodControl + "." + COLUMNNAME_Processing
                    + " - the open/close process is not configured in this environment.");
                return result;
            }

            pc.SetPeriodAction(action);
            if (!pc.Save())
            {
                result.ErrorCode = ERROR_SAVE_FAILED;
                ValueNamePair error = VLogger.RetrieveError();
                if (error != null) { result.ErrorMessage = error.GetName(); }
                return result;
            }

            /* 6 - run the standard process. It is the only thing allowed to change
                   PeriodStatus. */
            string processError = RunPeriodControlProcess(ctx, processId, periodControlId);

            /* 7 - re-read the record either way: on success to report the real new
                   status, on failure to prove nothing changed. */
            MPeriodControl reread = new MPeriodControl(ctx, periodControlId, null);
            result.PeriodStatus = reread.Get_ID() > 0 ? reread.GetPeriodStatus() : currentStatus;
            result.CanToggle = !MPeriodControl.PERIODSTATUS_PermanentlyClosed.Equals(result.PeriodStatus);

            if (!string.IsNullOrEmpty(processError))
            {
                result.ErrorCode = ERROR_PROCESS_FAILED;
                result.ErrorMessage = processError;

                /* Leave no pending action behind on a failed run - otherwise the next
                   unrelated save of this record would apply it silently. */
                if (reread.Get_ID() > 0
                    && !MPeriodControl.PERIODACTION_NoAction.Equals(reread.GetPeriodAction()))
                {
                    reread.SetPeriodAction(MPeriodControl.PERIODACTION_NoAction);
                    if (!reread.Save())
                    {
                        Log.Log(Level.WARNING, "VAS_196_PeriodControlMatrix: could not reset PeriodAction on"
                            + " C_PeriodControl_ID=" + periodControlId + " after a failed process run.");
                    }
                }
                return result;
            }

            result.Success = true;
            result.StatusChanged = !string.Equals(currentStatus, result.PeriodStatus, StringComparison.Ordinal);
            return result;
        }

        /// <summary>
        /// Validates that the clicked control really hangs under the selected
        /// period / year / calendar and that every level of that chain is active and
        /// accessible. Rejecting here keeps a stale or forged client selection from
        /// reaching the process.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="calendarId">C_Calendar_ID the client had selected.</param>
        /// <param name="yearId">C_Year_ID the client had selected.</param>
        /// <param name="periodId">C_Period_ID the client had selected.</param>
        /// <param name="periodControlId">C_PeriodControl_ID of the clicked row.</param>
        /// <returns>True when the whole hierarchy resolves to exactly this control.</returns>
        private bool IsHierarchyValid(Ctx ctx, int calendarId, int yearId, int periodId, int periodControlId)
        {
            if (calendarId <= 0 || yearId <= 0 || periodId <= 0 || periodControlId <= 0) { return false; }

            string sql = @"
                SELECT pc.C_PeriodControl_ID AS C_PeriodControl_ID
                FROM C_PeriodControl pc
                INNER JOIN C_Period p ON (p.C_Period_ID=pc.C_Period_ID)
                INNER JOIN C_Year y ON (y.C_Year_ID=p.C_Year_ID)
                INNER JOIN C_Calendar cal ON (cal.C_Calendar_ID=y.C_Calendar_ID)
                WHERE pc.C_PeriodControl_ID=@C_PeriodControl_ID
                  AND pc.C_Period_ID=@C_Period_ID
                  AND p.C_Year_ID=@C_Year_ID
                  AND y.C_Calendar_ID=@C_Calendar_ID
                  AND pc.IsActive='Y'
                  AND p.IsActive='Y'
                  AND y.IsActive='Y'
                  AND cal.IsActive='Y'";

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "pc", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@C_PeriodControl_ID", periodControlId),
                new SqlParameter("@C_Period_ID", periodId),
                new SqlParameter("@C_Year_ID", yearId),
                new SqlParameter("@C_Calendar_ID", calendarId)
            };

            DataSet ds = DB.ExecuteDataset(sql, parameters, null);
            return ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0;
        }

        /// <summary>
        /// Resolves the AD_Process_ID attached to C_PeriodControl.Processing from
        /// Application Dictionary metadata. The numeric id differs per environment,
        /// so it is looked up rather than hard-coded, and cached per app domain
        /// because dictionary metadata does not change at runtime.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <returns>AD_Process_ID, or 0 when the column carries no process.</returns>
        public int GetPeriodControlProcessId(Ctx ctx)
        {
            if (_periodControlProcessId > 0) { return _periodControlProcessId; }
            if (_periodControlProcessId < 0) { return 0; }      // looked up, not configured
            if (ctx == null) { return 0; }

            string sql = @"
                SELECT c.AD_Process_ID AS AD_Process_ID
                FROM AD_Table t
                INNER JOIN AD_Column c ON (c.AD_Table_ID=t.AD_Table_ID)
                WHERE t.TableName=@TableName
                  AND c.ColumnName=@ColumnName
                  AND t.IsActive='Y'
                  AND c.IsActive='Y'
                  AND c.AD_Process_ID IS NOT NULL";

            /* AD_Column is the main physical table of this dictionary lookup. */
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "c", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@TableName", TABLENAME_PeriodControl),
                new SqlParameter("@ColumnName", COLUMNNAME_Processing)
            };

            int processId = 0;
            DataSet ds = DB.ExecuteDataset(sql, parameters, null);
            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                processId = Util.GetValueOfInt(ds.Tables[0].Rows[0]["AD_Process_ID"]);
            }

            _periodControlProcessId = processId > 0 ? processId : -1;
            return processId;
        }

        /// <summary>
        /// Executes the standard open/close process for one period control through
        /// the normal process engine (AD_PInstance + ProcessInfo + ProcessCtl). The
        /// process class is never called directly.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="processId">AD_Process_ID resolved from column metadata.</param>
        /// <param name="periodControlId">Record the process runs against.</param>
        /// <returns>Empty string on success, otherwise the process error summary.</returns>
        private string RunPeriodControlProcess(Ctx ctx, int processId, int periodControlId)
        {
            try
            {
                MPInstance instance = new MPInstance(ctx, processId, periodControlId);
                if (!instance.Save())
                {
                    Log.Log(Level.SEVERE, "VAS_196_PeriodControlMatrix: could not create AD_PInstance for"
                        + " AD_Process_ID=" + processId + ", C_PeriodControl_ID=" + periodControlId);
                    return "process_instance_failed";
                }

                ProcessInfo pi = new ProcessInfo("", processId);
                pi.SetAD_PInstance_ID(instance.GetAD_PInstance_ID());
                pi.SetRecord_ID(periodControlId);
                pi.SetAD_Client_ID(ctx.GetAD_Client_ID());
                pi.SetAD_Org_ID(ctx.GetAD_Org_ID());
                pi.SetAD_User_ID(ctx.GetAD_User_ID());

                ProcessCtl worker = new ProcessCtl(ctx, null, pi, null);
                worker.Run();

                string summary = pi.GetSummary();
                if (pi.IsError())
                {
                    Log.Log(Level.SEVERE, "VAS_196_PeriodControlMatrix: open/close process failed for"
                        + " C_PeriodControl_ID=" + periodControlId + " - " + summary);
                    return string.IsNullOrEmpty(summary) ? "process_error" : summary;
                }

                if (!string.IsNullOrEmpty(summary))
                {
                    Log.Info("VAS_196_PeriodControlMatrix: open/close process summary - " + summary);
                }
                return "";
            }
            catch (Exception ex)
            {
                /* The process engine touches the database and other modules, so it is
                   one of the few places that genuinely needs a guard. */
                Log.Log(Level.SEVERE, "VAS_196_PeriodControlMatrix.RunPeriodControlProcess", ex);
                return ex.Message;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // §3  Helpers
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Runs a two-column id/name lookup query and materialises it.
        /// </summary>
        /// <param name="sql">Prepared statement (access SQL already applied).</param>
        /// <param name="parameters">Bind values, or null.</param>
        /// <param name="items">List to fill.</param>
        /// <returns>The same list, filled.</returns>
        private List<LookupItem> ReadLookup(string sql, SqlParameter[] parameters, List<LookupItem> items)
        {
            DataSet ds = DB.ExecuteDataset(sql, parameters, null);
            if (ds == null || ds.Tables.Count == 0) { return items; }

            DataTable dt = ds.Tables[0];
            for (int i = 0; i < dt.Rows.Count; i++)
            {
                LookupItem item = new LookupItem();
                item.Id = Util.GetValueOfInt(dt.Rows[i]["Item_ID"]);
                item.Name = Util.GetValueOfString(dt.Rows[i]["Item_Name"]);
                items.Add(item);
            }
            return items;
        }

        /// <summary>
        /// True when the list already holds the given id (used to check whether a
        /// preferred default is actually one of the accessible options).
        /// </summary>
        /// <param name="items">Lookup list.</param>
        /// <param name="id">Candidate id.</param>
        /// <returns>True when present.</returns>
        private bool ContainsId(List<LookupItem> items, int id)
        {
            if (items == null || id <= 0) { return false; }
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].Id == id) { return true; }
            }
            return false;
        }

        // ─────────────────────────────────────────────────────────────────────
        // §4  Contracts
        // ─────────────────────────────────────────────────────────────────────

        /* Error tokens. The client maps each to a localized AD_Message, so no
           display text is produced here. */
        public const string ERROR_INVALID_REQUEST = "INVALID";
        public const string ERROR_NOT_FOUND = "NOTFOUND";
        public const string ERROR_PERMANENTLY_CLOSED = "PERMCLOSED";
        public const string ERROR_NO_PROCESS = "NOPROCESS";
        public const string ERROR_SAVE_FAILED = "SAVEFAILED";
        public const string ERROR_PROCESS_FAILED = "PROCESSFAILED";

        /// <summary>Id / name pair for one selector option.</summary>
        public class LookupItem
        {
            public int Id { get; set; }
            public string Name { get; set; }
        }

        /// <summary>Selector options plus the default selection for the first paint.</summary>
        public class MatrixBootstrap
        {
            public List<LookupItem> Calendars { get; set; }
            public List<LookupItem> Years { get; set; }
            public List<LookupItem> Periods { get; set; }

            public int C_Calendar_ID { get; set; }
            public int C_Year_ID { get; set; }
            public int C_Period_ID { get; set; }
        }

        /// <summary>One C_PeriodControl row of the selected period.</summary>
        public class PeriodControlRow
        {
            public int C_PeriodControl_ID { get; set; }
            public int C_Period_ID { get; set; }

            /// <summary>0 for a tenant-wide control; the client only shows the
            /// organization column when at least one row carries a real org.</summary>
            public int AD_Org_ID { get; set; }

            /// <summary>AD_Org.Name of the owning organization ('*' for AD_Org_ID 0).</summary>
            public string OrgName { get; set; }

            /// <summary>Stored code; the client keys its status/tone map off it.</summary>
            public string DocBaseType { get; set; }

            /// <summary>C_DocBaseType key, so the filter dialog's lookup value can be
            /// matched against a row without comparing display names.</summary>
            public int C_DocBaseType_ID { get; set; }

            /// <summary>C_DocBaseType name, falling back to the stored code.</summary>
            public string DocBaseTypeName { get; set; }

            public string PeriodStatus { get; set; }
            public string PeriodAction { get; set; }

            /// <summary>False for a permanently closed control (button read-only).</summary>
            public bool CanToggle { get; set; }
        }

        /// <summary>Outcome of one open/close attempt.</summary>
        public class StatusChangeResult
        {
            public bool Success { get; set; }
            public int C_PeriodControl_ID { get; set; }

            /// <summary>Status re-read from the database after the process ran.</summary>
            public string PeriodStatus { get; set; }

            public bool CanToggle { get; set; }
            public bool StatusChanged { get; set; }

            /// <summary>One of the ERROR_* tokens; the client resolves the label.</summary>
            public string ErrorCode { get; set; }

            /// <summary>Raw process/save message, shown as detail when present.</summary>
            public string ErrorMessage { get; set; }
        }
    }
}
