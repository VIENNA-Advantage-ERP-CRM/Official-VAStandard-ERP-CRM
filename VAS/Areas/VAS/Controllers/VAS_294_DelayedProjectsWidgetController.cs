/******************************************************
 * Module Name    : VAS
 * Purpose        : Customers module Delayed Projects widget endpoints
 * chronological  : Development
 * Created Date   : 2026-09-23
 * Created by     : VAI052
 ******************************************************/

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Web.Mvc;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VAS.Controllers
{
    /// <summary>
    /// Module Name : VAS_294_DelayedProjectsWidget
    /// Purpose     : 3x1 compact list - customers whose delivery projects
    ///               (C_Project.VAS_ProjectStatus NOT IN ('DR','IP') - matching the
    ///               VAS_135 "delivery project" definition, since in this deployment
    ///               'DR'/'IP' are pipeline/opportunity stages, not delivery projects)
    ///               have at least one delayed activity, ranked by total delayed
    ///               activities desc. Each row names the customer's first delayed
    ///               project. A project's "activities" are its C_ProjectPhase rows;
    ///               a phase counts as delayed the same way VAS_135 counts it -
    ///               not completed (VAS_PhaseStatus &lt;&gt; 'CO') and its EndDate is
    ///               before today. MRole (tenant + org + record access) is applied to
    ///               the main physical table C_Project only (CTE rule).
    ///
    ///               This widget shares the activity-state breakdown ("Projects")
    ///               modal with VAS_135_ActiveProjectsWidget instead of duplicating
    ///               it - the client calls VAS_135_ActiveProjectsWidget/GetProjects
    ///               and /GetActivityDetail directly (same pattern VAS_138 already
    ///               uses to reuse VAS_135's project endpoints).
    /// Chronological development:
    ///   VAI052      2026-09-23 Created
    /// </summary>
    public class VAS_294_DelayedProjectsWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_294_DelayedProjectsWidgetController).FullName);

        // Compact 3x1 widget: 3 rows per page. The client always sends an explicit
        // limit, so this is the fallback default only (matches VAS_135's WidgetPageSize
        // pattern, sized for this widget's compact grid cell instead of 3x2).
        private const int WidgetPageSize = 3;
        private const int MaxListPageSize = 25;

        /// <summary>
        /// Whether a C_ProjectPhase row (alias "ph") is delayed: not completed and its
        /// EndDate has passed. Same precedence as VAS_135.PhaseStateCase()'s 'delayed'
        /// branch (completed wins over delayed), kept as a single boolean expression
        /// here because this widget only needs the delayed count for ranking - the
        /// full five-state breakdown is VAS_135's GetProjects/GetActivityDetail job.
        /// Dialect-correct for Oracle vs PostgreSQL.
        /// </summary>
        private string PhaseDelayedExpr()
        {
            string today = DB.IsPostgreSQL() ? "CURRENT_DATE" : "TRUNC(SYSDATE)";
            string endDate = DB.IsPostgreSQL() ? "CAST(ph.EndDate AS DATE)" : "TRUNC(ph.EndDate)";

            return "(COALESCE(ph.VAS_PhaseStatus, N'') <> 'CO' AND ph.EndDate IS NOT NULL AND " + endDate + " < " + today + ")";
        }

        /// <summary>
        /// Paged customer summary: customers with at least one delayed project
        /// activity, ranked by delayed-activity count desc. Supplies the widget rows
        /// (limit 3) and the "All" list (limit up to 25).
        /// </summary>
        /// <param name="offset">Zero-based paging offset.</param>
        /// <param name="limit">Page size (3 for the widget, up to 25 for the full list).</param>
        /// <returns>JSON { items:[...], total, offset, limit } or { error }.</returns>
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
                // Per active project: its name (for the "first delayed project" text)
                // and its delayed-phase count (LEFT JOIN so a project with no phases
                // still counts with zero delays). MRole on the main physical table
                // alias "p".
                string projectDelayedSql = @"
                    SELECT p.C_Project_ID AS C_Project_ID,
                           p.C_BPartner_ID AS C_BPartner_ID,
                           p.Name AS Project_Name,
                           p.SalesRep_ID AS SalesRep_ID,
                           SUM(CASE WHEN " + PhaseDelayedExpr() + @" THEN 1 ELSE 0 END) AS Delayed_Phase_Count
                    FROM C_Project p
                    LEFT OUTER JOIN C_ProjectPhase ph ON (ph.C_Project_ID=p.C_Project_ID AND ph.AD_Client_ID=p.AD_Client_ID AND ph.IsActive = 'Y')
                    WHERE p.IsActive = 'Y'
                      AND (p.VAS_ProjectStatus IS NULL OR p.VAS_ProjectStatus NOT IN ('DR', 'IP'))
                      AND p.AD_Client_ID = @Client_ID
                      AND p.C_BPartner_ID IS NOT NULL";
                projectDelayedSql = MRole.GetDefault(ctx).AddAccessSQL(projectDelayedSql, "p", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                projectDelayedSql += @"
                    GROUP BY p.C_Project_ID, p.C_BPartner_ID, p.Name, p.SalesRep_ID";

                string sql = @"
                    WITH ProjectDelayed AS (
                        " + projectDelayedSql + @"
                    ),
                    CustomerAgg AS (
                        SELECT pd.C_BPartner_ID AS C_BPartner_ID,
                               SUM(pd.Delayed_Phase_Count) AS Delayed_Count,
                               MAX(pd.SalesRep_ID) AS SalesRep_ID,
                               MIN(CASE WHEN pd.Delayed_Phase_Count > 0 THEN pd.Project_Name END) AS First_Delayed_Project
                        FROM ProjectDelayed pd
                        GROUP BY pd.C_BPartner_ID
                    )
                    SELECT bp.C_BPartner_ID AS Customer_Id,
                           bp.Name AS Customer_Name,
                           ca.Delayed_Count AS Delayed_Count,
                           COALESCE(ca.First_Delayed_Project, N'') AS First_Delayed_Project,
                           COALESCE(owner.Name, N'') AS Owner_Name,
                           COUNT(1) OVER () AS Total_Customers
                    FROM CustomerAgg ca
                    INNER JOIN C_BPartner bp ON (bp.C_BPartner_ID=ca.C_BPartner_ID AND bp.AD_Client_ID = @Client_ID AND bp.IsActive = 'Y' AND bp.IsCustomer = 'Y')
                    LEFT OUTER JOIN AD_User owner ON (owner.AD_User_ID=ca.SalesRep_ID AND owner.AD_Client_ID = @Client_ID AND owner.IsActive = 'Y')
                    WHERE ca.Delayed_Count > 0
                    ORDER BY ca.Delayed_Count DESC,
                             bp.Name ASC,
                             ca.C_BPartner_ID ASC
                    OFFSET " + offset + @" ROWS FETCH NEXT " + limit + @" ROWS ONLY";

                List<object> items = new List<object>();
                int total = 0;

                IDataReader dr = null;
                try
                {
                    dr = DB.ExecuteReader(sql, new SqlParameter[] { new SqlParameter("@Client_ID", ctx.GetAD_Client_ID()) });
                    while (dr != null && dr.Read())
                    {
                        total = Util.GetValueOfInt(dr["Total_Customers"]);
                        items.Add(new
                        {
                            customerId = Util.GetValueOfInt(dr["Customer_Id"]),
                            customerName = Util.GetValueOfString(dr["Customer_Name"]),
                            delayedCount = Util.GetValueOfInt(dr["Delayed_Count"]),
                            firstDelayedProject = Util.GetValueOfString(dr["First_Delayed_Project"]),
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
                    offset = offset,
                    limit = limit
                };
                return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_294_DelayedProjectsWidget.GetList", ex);
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        private void CloseReader(IDataReader reader)
        {
            if (reader == null) { return; }
            reader.Close();
            reader.Dispose();
        }
    }
}
