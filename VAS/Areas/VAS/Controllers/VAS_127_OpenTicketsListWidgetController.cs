/******************************************************
 * Module Name    : VAS
 * Purpose        : Customers module Open Tickets triage list widget endpoints
 * chronological  : Development
 * Created Date   : 2026-07-21
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
    /// Module Name : VAS_127_OpenTicketsListWidget
    /// Purpose     : 3x2 triage list - customers with open support tickets, key
    ///               clients ranked first, then by open-ticket count. "Open" is
    ///               driven by R_Status.IsOpen='Y' (never status names), and the
    ///               widget is restricted to configured support request types
    ///               (SupportRequestTypeIds). Key-client and the tier/category come
    ///               from configuration + C_BP_Group (no key/tier column exists).
    ///               MRole (tenant + org + record access) is applied to the main
    ///               physical table of each CTE (R_Request, then C_BPartner).
    /// Chronological development:
    ///   VAI052      2026-07-21 Created
    /// </summary>
    public class VAS_127_OpenTicketsListWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_127_OpenTicketsListWidgetController).FullName);

        private const int WidgetPageSize = 5;
        private const int MaxListPageSize = 25;

        /// <summary>
        /// Support-ticket request types are resolved per tenant from R_RequestType
        /// where VA092_ReqType is 'ST' or 'LG' (see GetSupportRequestTypeIds). The
        /// widget restricts open tickets to these types; if the tenant has none it
        /// counts all open tickets. Cached per client (configuration ids, not query
        /// results, per the performance rule).
        /// </summary>
        private static readonly Dictionary<int, int[]> SupportTypeCache = new Dictionary<int, int[]>();
        private static readonly object SupportTypeLock = new object();

        /// <summary>
        /// Configured key-client C_BP_Group_ID values. Empty => no customer is a key
        /// client and "0 key clients affected" is shown. Populate per tenant.
        /// </summary>
        private static readonly int[] KeyClientGroupIds = new int[0];

        /// <summary>Server-int IN() list, or a never-true predicate when empty.</summary>
        private string InClause(string column, int[] ids)
        {
            return ids.Length > 0 ? column + " IN (" + string.Join(",", ids) + ")" : "1=0";
        }

        /// <summary>
        /// R_RequestType join, present only when the tenant has support types so the
        /// widget can restrict to them; empty otherwise (all open tickets, matching
        /// the VAS_126 KPI).
        /// </summary>
        private string TypeJoin(int[] supportTypeIds)
        {
            return supportTypeIds.Length > 0
                ? "INNER JOIN R_RequestType rt ON (rt.R_RequestType_ID=r.R_RequestType_ID AND rt.AD_Client_ID=r.AD_Client_ID AND rt.IsActive = 'Y')"
                : "";
        }

        /// <summary>Support request-type predicate; empty when the tenant has no support types.</summary>
        private string TypeFilter(int[] supportTypeIds)
        {
            return supportTypeIds.Length > 0
                ? " AND r.R_RequestType_ID IN (" + string.Join(",", supportTypeIds) + ")"
                : "";
        }

        /// <summary>
        /// Resolves the tenant's support-ticket R_RequestType_ID values -
        /// active request types (system + tenant) whose VA092_ReqType is 'ST' or
        /// 'LG'. Cached per client so it is not re-queried on every widget refresh.
        /// </summary>
        /// <param name="ctx">Authenticated request context.</param>
        /// <returns>Configured support request-type ids (may be empty).</returns>
        private int[] GetSupportRequestTypeIds(Ctx ctx)
        {
            int clientId = ctx.GetAD_Client_ID();
            lock (SupportTypeLock)
            {
                int[] cached;
                if (SupportTypeCache.TryGetValue(clientId, out cached)) { return cached; }
            }

            string sql = @"
                SELECT rt.R_RequestType_ID AS R_RequestType_ID
                FROM R_RequestType rt
                WHERE rt.AD_Client_ID IN (0, @Client_ID)
                  AND rt.IsActive = 'Y'
                  AND rt.VA092_ReqType IN ('ST', 'LG')";
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "rt", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            List<int> ids = new List<int>();
            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, new SqlParameter[] { new SqlParameter("@Client_ID", clientId) });
                while (dr != null && dr.Read())
                {
                    int id = Util.GetValueOfInt(dr["R_RequestType_ID"]);
                    if (id > 0) { ids.Add(id); }
                }
            }
            finally
            {
                CloseReader(dr);
            }

            int[] result = ids.ToArray();
            lock (SupportTypeLock)
            {
                SupportTypeCache[clientId] = result;
            }
            return result;
        }

        /// <summary>
        /// Header summary: open-ticket count, affected-customer count, key-client count.
        /// </summary>
        /// <returns>JSON { configured, openTicketCount, affectedCustomerCount, keyClientCount } or { error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetSummary()
        {
            if (Session["ctx"] == null)
            {
                return Json(new { error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired" }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            try
            {
                // Resolve the tenant's support request types; restrict to them when
                // present, otherwise count all open tickets (consistent with VAS_126).
                int[] supportTypeIds = GetSupportRequestTypeIds(ctx);
                string typeJoin = TypeJoin(supportTypeIds);
                string typeFilter = TypeFilter(supportTypeIds);

                string sql = @"
                    SELECT COUNT(DISTINCT r.R_Request_ID) AS Open_Ticket_Count,
                           COUNT(DISTINCT r.C_BPartner_ID) AS Affected_Customer_Count,
                           COUNT(DISTINCT CASE WHEN " + InClause("bp.C_BP_Group_ID", KeyClientGroupIds) + @" THEN r.C_BPartner_ID END) AS Key_Client_Count
                    FROM R_Request r
                    INNER JOIN R_Status s ON (s.R_Status_ID=r.R_Status_ID AND s.AD_Client_ID=r.AD_Client_ID AND s.IsActive = 'Y')
                    " + typeJoin + @"
                    INNER JOIN C_BPartner bp ON (bp.C_BPartner_ID=r.C_BPartner_ID AND bp.AD_Client_ID=r.AD_Client_ID AND bp.IsActive = 'Y' AND bp.IsCustomer = 'Y')
                    WHERE r.IsActive = 'Y'
                      AND r.AD_Client_ID = @Client_ID
                      AND s.IsOpen = 'Y'
                      AND COALESCE(s.IsClosed, 'N') = 'N'" + typeFilter;
                sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "r", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                int openTickets = 0, affected = 0, keyClients = 0;
                IDataReader dr = null;
                try
                {
                    dr = DB.ExecuteReader(sql, new SqlParameter[] { new SqlParameter("@Client_ID", ctx.GetAD_Client_ID()) });
                    if (dr != null && dr.Read())
                    {
                        openTickets = Util.GetValueOfInt(dr["Open_Ticket_Count"]);
                        affected = Util.GetValueOfInt(dr["Affected_Customer_Count"]);
                        keyClients = Util.GetValueOfInt(dr["Key_Client_Count"]);
                    }
                }
                finally
                {
                    CloseReader(dr);
                }

                var result = new
                {
                    configured = true,
                    openTicketCount = openTickets,
                    affectedCustomerCount = affected,
                    keyClientCount = keyClients
                };
                return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_127_OpenTicketsListWidget.GetSummary", ex);
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Ranked customer rows (key clients first, then open-ticket count), paged.
        /// The optional search filters name / code / category / owner for the "All" list.
        /// </summary>
        /// <param name="offset">Zero-based paging offset.</param>
        /// <param name="limit">Page size; 7 for the widget, up to 25 for the full list.</param>
        /// <param name="search">Optional search text for the full list.</param>
        /// <returns>JSON { configured, items:[...], total, offset, limit } or { error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        [ValidateInput(false)]
        public JsonResult GetRows(int offset = 0, int limit = WidgetPageSize, string search = null)
        {
            if (Session["ctx"] == null)
            {
                return Json(new { error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired" }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            if (offset < 0) { offset = 0; }
            if (limit <= 0 || limit > MaxListPageSize) { limit = WidgetPageSize; }

            string searchText = (search ?? "").Trim();
            bool hasSearch = searchText.Length > 0;
            string likeValue = "%" + searchText.ToUpperInvariant() + "%";

            try
            {
                int[] supportTypeIds = GetSupportRequestTypeIds(ctx);
                string typeJoin = TypeJoin(supportTypeIds);
                string typeFilter = TypeFilter(supportTypeIds);

                // Aggregate tickets per customer BEFORE joining display masters so a
                // customer is counted once. MRole on the ticket source alias "r".
                string ticketByCustomerSql = @"
                    SELECT r.C_BPartner_ID AS C_BPartner_ID,
                           COUNT(DISTINCT r.R_Request_ID) AS Open_Ticket_Count,
                           MIN(r.Priority) AS Highest_Priority
                    FROM R_Request r
                    INNER JOIN R_Status s ON (s.R_Status_ID=r.R_Status_ID AND s.AD_Client_ID=r.AD_Client_ID AND s.IsActive = 'Y')
                    " + typeJoin + @"
                    INNER JOIN C_BPartner bpf ON (bpf.C_BPartner_ID=r.C_BPartner_ID AND bpf.AD_Client_ID=r.AD_Client_ID AND bpf.IsActive = 'Y' AND bpf.IsCustomer = 'Y')
                    WHERE r.IsActive = 'Y'
                      AND r.AD_Client_ID = @Client_ID
                      AND s.IsOpen = 'Y'
                      AND COALESCE(s.IsClosed, 'N') = 'N'" + typeFilter;
                ticketByCustomerSql = MRole.GetDefault(ctx).AddAccessSQL(ticketByCustomerSql, "r", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                ticketByCustomerSql += @"
                    GROUP BY r.C_BPartner_ID";

                // Customer display row. MRole applied independently on "bp".
                string rankedCustomerSql = @"
                    SELECT bp.C_BPartner_ID AS Customer_Id,
                           bp.Name AS Customer_Name,
                           COALESCE(grp.Name, N'') AS Tier,
                           CASE WHEN " + InClause("bp.C_BP_Group_ID", KeyClientGroupIds) + @" THEN 1 ELSE 0 END AS Is_Key,
                           t.Open_Ticket_Count AS Open_Ticket_Count,
                           t.Highest_Priority AS Highest_Priority,
                           COALESCE(owner.Name, N'') AS Owner_Name
                    FROM TicketByCustomer t
                    INNER JOIN C_BPartner bp ON (bp.C_BPartner_ID=t.C_BPartner_ID AND bp.AD_Client_ID = @Client_ID)
                    LEFT OUTER JOIN C_BP_Group grp ON (grp.C_BP_Group_ID=bp.C_BP_Group_ID AND grp.AD_Client_ID=bp.AD_Client_ID AND grp.IsActive = 'Y')
                    LEFT OUTER JOIN AD_User owner ON (owner.AD_User_ID=bp.SalesRep_ID AND owner.AD_Client_ID=bp.AD_Client_ID AND owner.IsActive = 'Y')
                    WHERE bp.IsActive = 'Y'
                      AND bp.IsCustomer = 'Y'";
                if (hasSearch)
                {
                    rankedCustomerSql += @"
                      AND (
                          UPPER(COALESCE(bp.Name, N'')) LIKE @Search
                          OR UPPER(COALESCE(bp.Value, N'')) LIKE @Search
                          OR UPPER(COALESCE(grp.Name, N'')) LIKE @Search
                          OR UPPER(COALESCE(owner.Name, N'')) LIKE @Search
                      )";
                }
                rankedCustomerSql = MRole.GetDefault(ctx).AddAccessSQL(rankedCustomerSql, "bp", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                // COALESCE priority to a value after '9' so null priority sorts last,
                // deterministically across Oracle/PostgreSQL (no NULLS FIRST/LAST).
                string sql = @"
                    WITH TicketByCustomer AS (
                        " + ticketByCustomerSql + @"
                    ),
                    RankedCustomer AS (
                        " + rankedCustomerSql + @"
                    )
                    SELECT rc.Customer_Id,
                           rc.Customer_Name,
                           rc.Tier,
                           rc.Is_Key,
                           rc.Open_Ticket_Count,
                           rc.Highest_Priority,
                           rc.Owner_Name,
                           COUNT(1) OVER () AS Total_Customers
                    FROM RankedCustomer rc
                    ORDER BY rc.Is_Key DESC,
                             rc.Open_Ticket_Count DESC,
                             COALESCE(rc.Highest_Priority, '99') ASC,
                             rc.Customer_Name ASC,
                             rc.Customer_Id ASC
                    OFFSET " + offset + @" ROWS FETCH NEXT " + limit + @" ROWS ONLY";

                List<SqlParameter> parameters = new List<SqlParameter> { new SqlParameter("@Client_ID", ctx.GetAD_Client_ID()) };
                if (hasSearch)
                {
                    parameters.Add(new SqlParameter("@Search", SqlDbType.NVarChar) { Value = likeValue });
                }

                List<object> items = new List<object>();
                int total = 0;

                IDataReader dr = null;
                try
                {
                    dr = DB.ExecuteReader(sql, parameters.ToArray());
                    while (dr != null && dr.Read())
                    {
                        total = Util.GetValueOfInt(dr["Total_Customers"]);
                        string priorityCode = Util.GetValueOfString(dr["Highest_Priority"]);
                        items.Add(new
                        {
                            customerId = Util.GetValueOfInt(dr["Customer_Id"]),
                            customerName = Util.GetValueOfString(dr["Customer_Name"]),
                            tier = Util.GetValueOfString(dr["Tier"]),
                            isKeyClient = Util.GetValueOfInt(dr["Is_Key"]) == 1,
                            openTicketCount = Util.GetValueOfInt(dr["Open_Ticket_Count"]),
                            priorityCode = priorityCode,
                            priority = PriorityLabel(priorityCode),
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
                    configured = true,
                    items = items,
                    total = total,
                    offset = offset,
                    limit = limit
                };
                return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_127_OpenTicketsListWidget.GetRows", ex);
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>Maps a stored priority code to a display label.</summary>
        private string PriorityLabel(string code)
        {
            switch (code)
            {
                case "1": return "Urgent";
                case "3": return "High";
                case "5": return "Medium";
                case "7": return "Low";
                case "9": return "Minor";
                default: return "";
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
