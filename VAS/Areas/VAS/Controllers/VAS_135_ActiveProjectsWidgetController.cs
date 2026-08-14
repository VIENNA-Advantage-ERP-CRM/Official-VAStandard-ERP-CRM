/******************************************************
 * Module Name    : VAS
 * Purpose        : Customers module Active Projects widget endpoints
 * chronological  : Development
 * Created Date   : 2026-07-21
 * Created by     : VAI052
 ******************************************************/

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Web.Mvc;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VAS.Controllers
{
    /// <summary>
    /// Module Name : VAS_135_ActiveProjectsWidget
    /// Purpose     : 3x2 list - customers with active delivery projects
    ///               (C_Project.VAS_ProjectStatus NOT IN ('DR','IP') - matching the
    ///               VAS_126 detail "Projects" count; in this deployment 'DR'/'IP'
    ///               are pipeline/opportunity stages, not delivery projects), clients
    ///               with delays ranked
    ///               first. Each project's "activities" are its C_ProjectPhase rows;
    ///               the five activity states (Delayed / On time / Ongoing / Upcoming
    ///               / Completed) are derived from C_ProjectPhase.VAS_PhaseStatus +
    ///               StartDate/EndDate with a mutually-exclusive precedence. Progress
    ///               is completed phases / total phases. MRole (tenant + org + record
    ///               access) is applied to the main physical table C_Project.
    ///
    /// Note        : The supplied Querry.md derived states from C_ProjectTask, but in
    ///               this schema VAS_PhaseStatus lives on C_ProjectPhase (which also
    ///               carries StartDate/EndDate), and VA052_POCPercent / task status
    ///               columns are not present - so phases are the activity unit here.
    ///               VAS_PhaseStatus is assumed to use the sibling status codes
    ///               'CO' (Completed) and 'IP' (In progress); adjust if the tenant
    ///               uses different codes.
    /// Chronological development:
    ///   VAI052      2026-07-21 Created
    /// </summary>
    public class VAS_135_ActiveProjectsWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_135_ActiveProjectsWidgetController).FullName);

        // 2026-08-06: widget page size raised 3 -> 5 so the rows fill the card
        // instead of leaving its lower half empty. The client always sends an
        // explicit limit, so this is the fallback default only.
        private const int WidgetPageSize = 5;
        private const int MaxListPageSize = 25;

        /// <summary>
        /// Mutually-exclusive activity-state CASE over a C_ProjectPhase alias "ph",
        /// dialect-correct for Oracle vs PostgreSQL. Order of the WHEN clauses is the
        /// required precedence: completed -> delayed -> upcoming -> ongoing -> ontime.
        /// </summary>
        private string PhaseStateCase()
        {
            string today = DB.IsPostgreSQL() ? "CURRENT_DATE" : "TRUNC(SYSDATE)";
            string endDate = DB.IsPostgreSQL() ? "CAST(ph.EndDate AS DATE)" : "TRUNC(ph.EndDate)";
            string startDate = DB.IsPostgreSQL() ? "CAST(ph.StartDate AS DATE)" : "TRUNC(ph.StartDate)";

            return @"CASE
                        WHEN ph.VAS_PhaseStatus = 'CO' THEN 'completed'
                        WHEN ph.EndDate IS NOT NULL AND " + endDate + " < " + today + @" THEN 'delayed'
                        WHEN ph.StartDate IS NOT NULL AND " + startDate + " > " + today + @" THEN 'upcoming'
                        WHEN ph.VAS_PhaseStatus = 'IP' AND (ph.StartDate IS NULL OR " + startDate + " <= " + today + @") THEN 'ongoing'
                        ELSE 'ontime'
                    END";
        }

        /// <summary>
        /// Paged customer summary: customers with an active (IP) project, ranked with
        /// delayed customers first. Supplies the widget rows plus header totals.
        /// </summary>
        /// <param name="offset">Zero-based paging offset.</param>
        /// <param name="limit">Page size (5 for the widget, up to 25 for the full list).</param>
        /// <returns>JSON { items:[...], total, customersWithDelays, offset, limit } or { error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetList(int offset = 0, int limit = WidgetPageSize)
        {
            if (Session["ctx"] == null)
            {
                return Json(new { error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired" }, JsonRequestBehavior.AllowGet);
            }

            if (offset < 0) { offset = 0; }
            if (limit <= 0 || limit > MaxListPageSize) { limit = WidgetPageSize; }

            Ctx ctx = Session["ctx"] as Ctx;

            try
            {
                // Per active project: its delayed-phase count (LEFT JOIN so projects
                // with no phases still count with zero delays). MRole on the main
                // physical table alias "p".
                string projectDelayedSql = @"
                    SELECT p.C_Project_ID AS C_Project_ID,
                           p.C_BPartner_ID AS C_BPartner_ID,
                           COALESCE(p.PlannedAmt, 0) AS Planned,
                           p.SalesRep_ID AS SalesRep_ID,
                           SUM(CASE WHEN " + PhaseStateCase() + @" = 'delayed' THEN 1 ELSE 0 END) AS Delayed_Phase_Count
                    FROM C_Project p
                    LEFT OUTER JOIN C_ProjectPhase ph ON (ph.C_Project_ID=p.C_Project_ID AND ph.AD_Client_ID=p.AD_Client_ID AND ph.IsActive = 'Y')
                    WHERE p.IsActive = 'Y'
                      AND (p.VAS_ProjectStatus IS NULL OR p.VAS_ProjectStatus NOT IN ('DR', 'IP'))
                      AND p.AD_Client_ID = @Client_ID
                      AND p.C_BPartner_ID IS NOT NULL";
                projectDelayedSql = MRole.GetDefault(ctx).AddAccessSQL(projectDelayedSql, "p", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                projectDelayedSql += @"
                    GROUP BY p.C_Project_ID, p.C_BPartner_ID, p.PlannedAmt, p.SalesRep_ID";

                string sql = @"
                    WITH ProjectDelayed AS (
                        " + projectDelayedSql + @"
                    ),
                    CustomerAgg AS (
                        SELECT pd.C_BPartner_ID AS C_BPartner_ID,
                               COUNT(*) AS Project_Count,
                               COALESCE(SUM(pd.Planned), 0) AS Planned,
                               SUM(pd.Delayed_Phase_Count) AS Delayed_Count,
                               MAX(pd.SalesRep_ID) AS SalesRep_ID
                        FROM ProjectDelayed pd
                        GROUP BY pd.C_BPartner_ID
                    )
                    SELECT bp.C_BPartner_ID AS Customer_Id,
                           bp.Name AS Customer_Name,
                           ca.Project_Count AS Project_Count,
                           ca.Delayed_Count AS Delayed_Count,
                           COALESCE(owner.Name, N'') AS Owner_Name,
                           COUNT(1) OVER () AS Total_Customers,
                           SUM(CASE WHEN ca.Delayed_Count > 0 THEN 1 ELSE 0 END) OVER () AS Customers_With_Delays
                    FROM CustomerAgg ca
                    INNER JOIN C_BPartner bp ON (bp.C_BPartner_ID=ca.C_BPartner_ID AND bp.AD_Client_ID = @Client_ID AND bp.IsActive = 'Y' AND bp.IsCustomer = 'Y')
                    LEFT OUTER JOIN AD_User owner ON (owner.AD_User_ID=ca.SalesRep_ID AND owner.AD_Client_ID = @Client_ID AND owner.IsActive = 'Y')
                    ORDER BY CASE WHEN ca.Delayed_Count > 0 THEN 0 ELSE 1 END,
                             ca.Delayed_Count DESC,
                             ca.Planned DESC,
                             bp.Name ASC,
                             ca.C_BPartner_ID ASC
                    OFFSET " + offset + @" ROWS FETCH NEXT " + limit + @" ROWS ONLY";

                List<object> items = new List<object>();
                int total = 0;
                int customersWithDelays = 0;

                IDataReader dr = null;
                try
                {
                    dr = DB.ExecuteReader(sql, new SqlParameter[] { new SqlParameter("@Client_ID", ctx.GetAD_Client_ID()) });
                    while (dr != null && dr.Read())
                    {
                        total = Util.GetValueOfInt(dr["Total_Customers"]);
                        customersWithDelays = Util.GetValueOfInt(dr["Customers_With_Delays"]);
                        int delayed = Util.GetValueOfInt(dr["Delayed_Count"]);
                        items.Add(new
                        {
                            customerId = Util.GetValueOfInt(dr["Customer_Id"]),
                            customerName = Util.GetValueOfString(dr["Customer_Name"]),
                            projectCount = Util.GetValueOfInt(dr["Project_Count"]),
                            delayedCount = delayed,
                            hasDelayedProject = delayed > 0,
                            ownerName = Util.GetValueOfString(dr["Owner_Name"])
                        });
                    }
                }
                finally
                {
                    CloseReader(dr);
                }

                var result = new
                {
                    items = items,
                    total = total,
                    customersWithDelays = customersWithDelays,
                    offset = offset,
                    limit = limit
                };
                return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_135_ActiveProjectsWidget.GetList", ex);
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// One customer's active projects with phase-state counts, progress, status,
        /// and due date - drives the modal (project rows + the five summary cells).
        /// </summary>
        /// <param name="C_BPartner_ID">Customer whose projects are requested.</param>
        /// <returns>JSON { projects:[...], summary:{...} } or { error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetProjects(int C_BPartner_ID)
        {
            if (Session["ctx"] == null)
            {
                return Json(new { error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired" }, JsonRequestBehavior.AllowGet);
            }

            if (C_BPartner_ID <= 0)
            {
                return Json(new { error = "Invalid customer" }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            try
            {
                // Phase states for this customer's active projects.
                string phaseStateSql = @"
                    SELECT ph.C_Project_ID AS C_Project_ID,
                           " + PhaseStateCase() + @" AS Activity_State
                    FROM C_Project p
                    INNER JOIN C_ProjectPhase ph ON (ph.C_Project_ID=p.C_Project_ID AND ph.AD_Client_ID=p.AD_Client_ID AND ph.IsActive = 'Y')
                    WHERE p.IsActive = 'Y'
                      AND (p.VAS_ProjectStatus IS NULL OR p.VAS_ProjectStatus NOT IN ('DR', 'IP'))
                      AND p.C_BPartner_ID = @BP_ID
                      AND p.AD_Client_ID = @Client_ID";
                phaseStateSql = MRole.GetDefault(ctx).AddAccessSQL(phaseStateSql, "p", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                string projectBodySql = @"
                    SELECT p.C_Project_ID AS Project_Id,
                           p.Name AS Project_Name,
                           p.DateContract AS Start_Date,
                           p.DateFinish AS Due_Date,
                           p.VAS_ProjectStatus AS Status_Code,
                           COALESCE(u.Name, N'') AS Owner_Name,
                           COUNT(ps.Activity_State) AS Total_Activities,
                           SUM(CASE WHEN ps.Activity_State = 'delayed' THEN 1 ELSE 0 END) AS Delayed,
                           SUM(CASE WHEN ps.Activity_State = 'ontime' THEN 1 ELSE 0 END) AS OnTime,
                           SUM(CASE WHEN ps.Activity_State = 'ongoing' THEN 1 ELSE 0 END) AS Ongoing,
                           SUM(CASE WHEN ps.Activity_State = 'upcoming' THEN 1 ELSE 0 END) AS Upcoming,
                           SUM(CASE WHEN ps.Activity_State = 'completed' THEN 1 ELSE 0 END) AS Completed
                    FROM C_Project p
                    LEFT OUTER JOIN PhaseState ps ON (ps.C_Project_ID=p.C_Project_ID)
                    LEFT OUTER JOIN AD_User u ON (u.AD_User_ID=p.SalesRep_ID AND u.AD_Client_ID=p.AD_Client_ID AND u.IsActive = 'Y')
                    WHERE p.IsActive = 'Y'
                      AND (p.VAS_ProjectStatus IS NULL OR p.VAS_ProjectStatus NOT IN ('DR', 'IP'))
                      AND p.C_BPartner_ID = @BP_ID
                      AND p.AD_Client_ID = @Client_ID";
                projectBodySql = MRole.GetDefault(ctx).AddAccessSQL(projectBodySql, "p", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                projectBodySql += @"
                    GROUP BY p.C_Project_ID, p.Name, p.DateContract, p.DateFinish, p.VAS_ProjectStatus, u.Name";

                string sql = @"
                    WITH PhaseState AS (
                        " + phaseStateSql + @"
                    )
                    SELECT ProjectRows.Project_Id,
                           ProjectRows.Project_Name,
                           ProjectRows.Start_Date,
                           ProjectRows.Due_Date,
                           ProjectRows.Status_Code,
                           ProjectRows.Owner_Name,
                           ProjectRows.Total_Activities,
                           ProjectRows.Delayed,
                           ProjectRows.OnTime,
                           ProjectRows.Ongoing,
                           ProjectRows.Upcoming,
                           ProjectRows.Completed
                    FROM (
                        " + projectBodySql + @"
                    ) ProjectRows
                    ORDER BY CASE WHEN ProjectRows.Delayed > 0 THEN 0 ELSE 1 END,
                             ProjectRows.Due_Date,
                             ProjectRows.Project_Name";

                List<object> projects = new List<object>();
                int sumDelayed = 0, sumOnTime = 0, sumOngoing = 0, sumUpcoming = 0, sumCompleted = 0;

                IDataReader dr = null;
                try
                {
                    dr = DB.ExecuteReader(sql, new SqlParameter[]
                    {
                        new SqlParameter("@Client_ID", ctx.GetAD_Client_ID()),
                        new SqlParameter("@BP_ID", C_BPartner_ID)
                    });
                    while (dr != null && dr.Read())
                    {
                        int total = Util.GetValueOfInt(dr["Total_Activities"]);
                        int delayed = Util.GetValueOfInt(dr["Delayed"]);
                        int ontime = Util.GetValueOfInt(dr["OnTime"]);
                        int ongoing = Util.GetValueOfInt(dr["Ongoing"]);
                        int upcoming = Util.GetValueOfInt(dr["Upcoming"]);
                        int completed = Util.GetValueOfInt(dr["Completed"]);

                        sumDelayed += delayed;
                        sumOnTime += ontime;
                        sumOngoing += ongoing;
                        sumUpcoming += upcoming;
                        sumCompleted += completed;

                        int progress = total > 0 ? (int)Math.Round(100.0 * completed / total) : 0;

                        projects.Add(new
                        {
                            id = Util.GetValueOfInt(dr["Project_Id"]),
                            name = Util.GetValueOfString(dr["Project_Name"]),
                            startDate = FormatDate(Util.GetValueOfDateTime(dr["Start_Date"])),
                            dueDate = FormatDate(Util.GetValueOfDateTime(dr["Due_Date"])),
                            statusCode = Util.GetValueOfString(dr["Status_Code"]),
                            owner = Util.GetValueOfString(dr["Owner_Name"]),
                            total = total,
                            delayed = delayed,
                            ontime = ontime,
                            ongoing = ongoing,
                            upcoming = upcoming,
                            completed = completed,
                            progressPercent = progress,
                            status = DisplayStatus(delayed, ongoing, upcoming)
                        });
                    }
                }
                finally
                {
                    CloseReader(dr);
                }

                var result = new
                {
                    projects = projects,
                    summary = new
                    {
                        delayed = sumDelayed,
                        ontime = sumOnTime,
                        ongoing = sumOngoing,
                        upcoming = sumUpcoming,
                        completed = sumCompleted
                    }
                };
                return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_135_ActiveProjectsWidget.GetProjects", ex);
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Phases (activities) in one state for a customer - drives the state-cell
        /// filter in the modal. The state is validated against the known set (never
        /// concatenated raw).
        /// </summary>
        /// <param name="C_BPartner_ID">Customer.</param>
        /// <param name="state">One of delayed/ontime/ongoing/upcoming/completed.</param>
        /// <returns>JSON { items:[...] } or { error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        [ValidateInput(false)]
        public JsonResult GetActivityDetail(int C_BPartner_ID, string state)
        {
            if (Session["ctx"] == null)
            {
                return Json(new { error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired" }, JsonRequestBehavior.AllowGet);
            }

            if (C_BPartner_ID <= 0)
            {
                return Json(new { error = "Invalid customer" }, JsonRequestBehavior.AllowGet);
            }

            // Validate the state against the known set (parameter-bound below).
            string wanted = (state ?? "").Trim().ToLowerInvariant();
            if (wanted != "delayed" && wanted != "ontime" && wanted != "ongoing" && wanted != "upcoming" && wanted != "completed")
            {
                return Json(JsonConvert.SerializeObject(new { items = new List<object>() }), JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            try
            {
                string activitiesSql = @"
                    SELECT p.Name AS Project_Name,
                           ph.Name AS Phase_Name,
                           ph.StartDate AS Start_Date,
                           ph.EndDate AS Due_Date,
                           " + PhaseStateCase() + @" AS Activity_State
                    FROM C_Project p
                    INNER JOIN C_ProjectPhase ph ON (ph.C_Project_ID=p.C_Project_ID AND ph.AD_Client_ID=p.AD_Client_ID AND ph.IsActive = 'Y')
                    WHERE p.IsActive = 'Y'
                      AND (p.VAS_ProjectStatus IS NULL OR p.VAS_ProjectStatus NOT IN ('DR', 'IP'))
                      AND p.C_BPartner_ID = @BP_ID
                      AND p.AD_Client_ID = @Client_ID";
                activitiesSql = MRole.GetDefault(ctx).AddAccessSQL(activitiesSql, "p", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                string sql = @"
                    WITH Activities AS (
                        " + activitiesSql + @"
                    )
                    SELECT Activities.Project_Name,
                           Activities.Phase_Name,
                           Activities.Start_Date,
                           Activities.Due_Date
                    FROM Activities
                    WHERE Activities.Activity_State = @State
                    ORDER BY Activities.Due_Date,
                             Activities.Project_Name,
                             Activities.Phase_Name";

                List<object> items = new List<object>();
                IDataReader dr = null;
                try
                {
                    dr = DB.ExecuteReader(sql, new SqlParameter[]
                    {
                        new SqlParameter("@Client_ID", ctx.GetAD_Client_ID()),
                        new SqlParameter("@BP_ID", C_BPartner_ID),
                        new SqlParameter("@State", SqlDbType.VarChar) { Value = wanted }
                    });
                    while (dr != null && dr.Read())
                    {
                        items.Add(new
                        {
                            projectName = Util.GetValueOfString(dr["Project_Name"]),
                            phaseName = Util.GetValueOfString(dr["Phase_Name"]),
                            startDate = FormatDate(Util.GetValueOfDateTime(dr["Start_Date"])),
                            dueDate = FormatDate(Util.GetValueOfDateTime(dr["Due_Date"]))
                        });
                    }
                }
                finally
                {
                    CloseReader(dr);
                }

                return Json(JsonConvert.SerializeObject(new { items = items }), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_135_ActiveProjectsWidget.GetActivityDetail", ex);
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>Operational status from the phase-state counts (delayed wins).</summary>
        private string DisplayStatus(int delayed, int ongoing, int upcoming)
        {
            if (delayed > 0) { return "Delayed"; }
            if (ongoing > 0) { return "Ongoing"; }
            if (upcoming > 0) { return "Upcoming"; }
            return "On time";
        }

        private string FormatDate(DateTime? value)
        {
            return value.HasValue ? value.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "";
        }

        private void CloseReader(IDataReader reader)
        {
            if (reader == null) { return; }
            reader.Close();
            reader.Dispose();
        }
    }
}
