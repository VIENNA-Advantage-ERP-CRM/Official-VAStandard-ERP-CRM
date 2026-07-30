/******************************************************
 * Module Name    : VAS
 * Purpose        : Customers module Open Tickets KPI widget endpoints
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
    /// Module Name : VAS_126_OpenTicketsWidget
    /// Purpose     : Clickable 2x1 KPI tile - the count of open support tickets
    ///               (R_Request whose R_Status.IsOpen='Y' and IsClosed<>'Y') and the
    ///               number of distinct KEY-CLIENT customers affected, in the
    ///               logged-in tenant and the organizations the role may access.
    ///               Clicking opens a paged triage list of customers with open
    ///               tickets (key clients first, then by open-ticket count).
    ///               "Open" is driven by the R_Status master flags, never by close
    ///               date or status name. Key-client classification is configuration-
    ///               driven (C_BP_Group_ID in KeyClientGroupIds) because the schema
    ///               has no key-client/tier column. MRole (tenant + org + record
    ///               access) is applied to the main physical table R_Request only.
    /// Chronological development:
    ///   VAI052      2026-07-21 Created
    /// </summary>
    public class VAS_126_OpenTicketsWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_126_OpenTicketsWidgetController).FullName);

        // Standard widget page size for the triage list (spec: default 7, max 7).
        private const int MaxPageSize = 7;

        /// <summary>
        /// Customer groups (C_BP_Group_ID) the business classifies as key clients.
        /// Intentionally EMPTY: the supplied metadata has no key-client / tier column,
        /// so this must be configured per tenant. While empty, "key clients affected"
        /// is 0 and no row is tagged Key client. Populate with the approved
        /// C_BP_Group_ID values (e.g. Platinum / Strategic groups). These are trusted
        /// server-side integers, so they are safe to embed in the IN() list.
        /// </summary>
        private static readonly int[] KeyClientGroupIds = new int[0];

        /// <summary>
        /// Optional configured mapping from C_BPartner.Rating to a display tier
        /// (Platinum/Gold/Silver) for the triage list Tier column. Intentionally
        /// EMPTY: the schema does not prove Rating's stored codes, so an unmapped
        /// rating shows the raw code as a neutral tag rather than a guessed tier
        /// name (matching VAS_122). Populate per tenant, e.g. { "A", "Platinum" }.
        /// </summary>
        private static readonly Dictionary<string, string> CustomerTierByRating =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Builds the key-client predicate from the configured group ids (integers
        /// only, so no injection risk). Resolves to a never-true predicate when no
        /// group is configured.
        /// </summary>
        private string KeyClientPredicate()
        {
            return KeyClientGroupIds.Length > 0
                ? "bp.C_BP_Group_ID IN (" + string.Join(",", KeyClientGroupIds) + ")"
                : "1=0";
        }

        /// <summary>
        /// KPI aggregate: open-ticket count and distinct affected key clients.
        /// </summary>
        /// <returns>JSON { open_ticket_count, key_clients_affected } or { error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetOpenTickets()
        {
            if (Session["ctx"] == null)
            {
                return Json(new
                {
                    error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired"
                }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            try
            {
                // Count open tickets that belong to an active customer, consistent
                // with the VAS_127 triage list (which shows customer rows). The
                // customer join is INNER so both widgets report the same total; the
                // key-client subset is restricted to a configured key group.
                string sql = @"
                    SELECT COUNT(DISTINCT r.R_Request_ID) AS Open_Ticket_Count,
                           COUNT(DISTINCT CASE WHEN " + KeyClientPredicate() + @" THEN bp.C_BPartner_ID END) AS Key_Clients_Affected
                    FROM R_Request r
                    INNER JOIN R_Status s ON (s.R_Status_ID=r.R_Status_ID AND s.AD_Client_ID=r.AD_Client_ID AND s.IsActive = 'Y')
                    INNER JOIN C_BPartner bp ON (bp.C_BPartner_ID=r.C_BPartner_ID AND bp.AD_Client_ID=r.AD_Client_ID AND bp.IsActive = 'Y' AND bp.IsCustomer = 'Y')
                    WHERE r.IsActive = 'Y'
                      AND r.AD_Client_ID = @Client_ID
                      AND s.IsOpen = 'Y'
                      AND COALESCE(s.IsClosed, 'N') = 'N'";

                // MRole tenant + org + record access on the main physical table alias "r".
                sql = MRole.GetDefault(ctx).AddAccessSQL(
                    sql,
                    "r",
                    MRole.SQL_FULLYQUALIFIED,
                    MRole.SQL_RO
                );

                int openTicketCount = 0;
                int keyClientsAffected = 0;

                IDataReader dr = null;
                try
                {
                    dr = DB.ExecuteReader(
                        sql,
                        new SqlParameter[] { new SqlParameter("@Client_ID", ctx.GetAD_Client_ID()) }
                    );

                    if (dr != null && dr.Read())
                    {
                        openTicketCount = Util.GetValueOfInt(dr["Open_Ticket_Count"]);
                        keyClientsAffected = Util.GetValueOfInt(dr["Key_Clients_Affected"]);
                    }
                }
                finally
                {
                    CloseReader(dr);
                }

                var result = new
                {
                    open_ticket_count = openTicketCount,
                    key_clients_affected = keyClientsAffected
                };

                return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_126_OpenTicketsWidget.GetOpenTickets", ex);
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Paged triage list: one row per customer with open tickets, key clients
        /// first then by open-ticket count (deterministic tie-breakers).
        /// </summary>
        /// <param name="offset">Zero-based paging offset.</param>
        /// <param name="limit">Page size; clamped to a server maximum of 7.</param>
        /// <returns>JSON { items:[...], total, offset, limit } or { error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetAffectedCustomers(int offset = 0, int limit = MaxPageSize)
        {
            if (Session["ctx"] == null)
            {
                return Json(new
                {
                    error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired"
                }, JsonRequestBehavior.AllowGet);
            }

            if (offset < 0) { offset = 0; }
            if (limit <= 0 || limit > MaxPageSize) { limit = MaxPageSize; }

            Ctx ctx = Session["ctx"] as Ctx;

            try
            {
                // One deterministic primary contact per business partner (prefers a
                // contact with an e-mail, then most recently updated) - same rule as
                // VAS_120. Secondary source: not MRole-filtered (CTE rule).
                string rankedContactsSql = @"
                    SELECT Contact.C_BPartner_ID AS C_BPartner_ID,
                           Contact.Name AS Contact_Name,
                           ROW_NUMBER() OVER (
                               PARTITION BY Contact.C_BPartner_ID
                               ORDER BY CASE WHEN Contact.EMail IS NOT NULL THEN 0 ELSE 1 END,
                                        Contact.Updated DESC,
                                        Contact.AD_User_ID
                           ) AS RN
                    FROM AD_User Contact
                    WHERE Contact.IsActive = 'Y'
                      AND Contact.AD_Client_ID = @Client_ID";

                // Distinct customers that have at least one open ticket. Ticket source
                // - MRole applied to the main physical table alias "r".
                string openTicketCustomersSql = @"
                    SELECT DISTINCT r.C_BPartner_ID AS C_BPartner_ID
                    FROM R_Request r
                    INNER JOIN R_Status s ON (s.R_Status_ID=r.R_Status_ID AND s.AD_Client_ID=r.AD_Client_ID AND s.IsActive = 'Y')
                    WHERE r.IsActive = 'Y'
                      AND r.AD_Client_ID = @Client_ID
                      AND r.C_BPartner_ID IS NOT NULL
                      AND s.IsOpen = 'Y'
                      AND COALESCE(s.IsClosed, 'N') = 'N'";

                openTicketCustomersSql = MRole.GetDefault(ctx).AddAccessSQL(
                    openTicketCustomersSql,
                    "r",
                    MRole.SQL_FULLYQUALIFIED,
                    MRole.SQL_RO
                );

                // Customer profile rows for the affected customers. Customer source -
                // MRole applied independently to the main physical table alias "bp"
                // (CTE rule: apply per CTE body to its own primary table).
                string customerRowsSql = @"
                    SELECT bp.C_BPartner_ID AS Customer_Id,
                           bp.Name AS Customer_Name,
                           COALESCE(c.Contact_Name, N'') AS Contact,
                           COALESCE(grp.Name, N'') AS Segment,
                           COALESCE(owner.Name, N'') AS Rep,
                           bp.Rating AS Tier_Code,
                           COALESCE(bp.ActualLifeTimeValue, 0) AS Cust_Value
                    FROM C_BPartner bp
                    INNER JOIN OpenTicketCustomers otc ON (otc.C_BPartner_ID=bp.C_BPartner_ID)
                    LEFT OUTER JOIN RankedContacts c ON (c.C_BPartner_ID=bp.C_BPartner_ID AND c.RN=1)
                    LEFT OUTER JOIN C_BP_Group grp ON (grp.C_BP_Group_ID=bp.C_BP_Group_ID AND grp.AD_Client_ID=bp.AD_Client_ID AND grp.IsActive = 'Y')
                    LEFT OUTER JOIN AD_User owner ON (owner.AD_User_ID=bp.SalesRep_ID AND owner.AD_Client_ID=bp.AD_Client_ID AND owner.IsActive = 'Y')
                    WHERE bp.IsActive = 'Y'
                      AND bp.IsCustomer = 'Y'
                      AND bp.AD_Client_ID = @Client_ID";

                customerRowsSql = MRole.GetDefault(ctx).AddAccessSQL(
                    customerRowsSql,
                    "bp",
                    MRole.SQL_FULLYQUALIFIED,
                    MRole.SQL_RO
                );

                // Sorted by customer value (shown as ARR) desc, then name - matches
                // the reference triage list.
                string sql = @"
                    WITH RankedContacts AS (
                        " + rankedContactsSql + @"
                    ),
                    OpenTicketCustomers AS (
                        " + openTicketCustomersSql + @"
                    ),
                    CustomerRows AS (
                        " + customerRowsSql + @"
                    )
                    SELECT cr.Customer_Id,
                           cr.Customer_Name,
                           cr.Contact,
                           cr.Segment,
                           cr.Rep,
                           cr.Tier_Code,
                           cr.Cust_Value,
                           COUNT(1) OVER () AS Total_Customers
                    FROM CustomerRows cr
                    ORDER BY cr.Cust_Value DESC,
                             cr.Customer_Name ASC
                    OFFSET " + offset + @" ROWS FETCH NEXT " + limit + @" ROWS ONLY";

                List<object> items = new List<object>();
                int total = 0;

                IDataReader dr = null;
                try
                {
                    dr = DB.ExecuteReader(
                        sql,
                        new SqlParameter[]
                        {
                            new SqlParameter("@Client_ID", ctx.GetAD_Client_ID())
                        }
                    );

                    while (dr != null && dr.Read())
                    {
                        total = Util.GetValueOfInt(dr["Total_Customers"]);
                        string tierCode = Util.GetValueOfString(dr["Tier_Code"]);
                        items.Add(new
                        {
                            customerId = Util.GetValueOfInt(dr["Customer_Id"]),
                            customerName = Util.GetValueOfString(dr["Customer_Name"]),
                            contact = Util.GetValueOfString(dr["Contact"]),
                            segment = Util.GetValueOfString(dr["Segment"]),
                            rep = Util.GetValueOfString(dr["Rep"]),
                            tierCode = tierCode,
                            tier = MapTier(tierCode),
                            value = Util.GetValueOfDecimal(dr["Cust_Value"])
                        });
                    }
                }
                finally
                {
                    CloseReader(dr);
                }

                SchemaCurrency currency = GetSchemaCurrency(ctx);

                var result = new
                {
                    items = items,
                    total = total,
                    offset = offset,
                    limit = limit,
                    currency_symbol = currency.Symbol,
                    currency_iso = currency.IsoCode,
                    std_precision = currency.StdPrecision
                };

                return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_126_OpenTicketsWidget.GetAffectedCustomers", ex);
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Rich customer-detail card for one business partner: profile (contact,
        /// tier, segment, owner, value), open-ticket count, active project count,
        /// open-pipeline value, and overdue receivables - each from its own secured
        /// query (MRole on the respective main physical table). Used by the triage
        /// list's row click.
        /// </summary>
        /// <param name="C_BPartner_ID">Customer whose detail is requested.</param>
        /// <returns>JSON detail object or { error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetCustomerDetail(int C_BPartner_ID)
        {
            if (Session["ctx"] == null)
            {
                return Json(new
                {
                    error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired"
                }, JsonRequestBehavior.AllowGet);
            }

            if (C_BPartner_ID <= 0)
            {
                return Json(new { error = "Invalid customer" }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            try
            {
                int clientId = ctx.GetAD_Client_ID();
                SchemaCurrency currency = GetSchemaCurrency(ctx);

                SqlParameter[] keyParams = new SqlParameter[]
                {
                    new SqlParameter("@Client_ID", clientId),
                    new SqlParameter("@BP_ID", C_BPartner_ID)
                };

                // 1. Profile (contact/title/email, segment, owner, tier, value, group).
                string rankedContactsSql = @"
                    SELECT Contact.Name AS Contact_Name,
                           Contact.EMail AS Contact_Email,
                           ROW_NUMBER() OVER (
                               ORDER BY CASE WHEN Contact.EMail IS NOT NULL THEN 0 ELSE 1 END,
                                        Contact.Updated DESC,
                                        Contact.AD_User_ID
                           ) AS RN
                    FROM AD_User Contact
                    WHERE Contact.IsActive = 'Y'
                      AND Contact.AD_Client_ID = @Client_ID
                      AND Contact.C_BPartner_ID = @BP_ID";

                string profileBodySql = @"
                    SELECT bp.Name AS Customer_Name,
                           COALESCE(c.Contact_Name, N'') AS Contact_Name,
                           COALESCE(c.Contact_Email, bp.EMail, N'') AS Contact_Email,
                           COALESCE(grp.Name, N'') AS Segment,
                           COALESCE(owner.Name, N'') AS Rep,
                           bp.Rating AS Tier_Code,
                           COALESCE(bp.ActualLifeTimeValue, 0) AS Cust_Value,
                           COALESCE(bp.C_BP_Group_ID, 0) AS Group_Id
                    FROM C_BPartner bp
                    LEFT OUTER JOIN RankedContacts c ON (c.RN=1)
                    LEFT OUTER JOIN C_BP_Group grp ON (grp.C_BP_Group_ID=bp.C_BP_Group_ID AND grp.AD_Client_ID=bp.AD_Client_ID AND grp.IsActive = 'Y')
                    LEFT OUTER JOIN AD_User owner ON (owner.AD_User_ID=bp.SalesRep_ID AND owner.AD_Client_ID=bp.AD_Client_ID AND owner.IsActive = 'Y')
                    WHERE bp.C_BPartner_ID = @BP_ID
                      AND bp.IsActive = 'Y'
                      AND bp.AD_Client_ID = @Client_ID";
                profileBodySql = MRole.GetDefault(ctx).AddAccessSQL(profileBodySql, "bp", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                string profileSql = "WITH RankedContacts AS (" + rankedContactsSql + ") " + profileBodySql;

                string customerName = null;
                string contactName = "", contactTitle = "", contactEmail = "", segment = "", rep = "", tierCode = "";
                decimal custValue = 0;
                int groupId = 0;

                IDataReader dr = null;
                try
                {
                    dr = DB.ExecuteReader(profileSql, keyParams);
                    if (dr != null && dr.Read())
                    {
                        customerName = Util.GetValueOfString(dr["Customer_Name"]);
                        contactName = Util.GetValueOfString(dr["Contact_Name"]);
                        contactEmail = Util.GetValueOfString(dr["Contact_Email"]);
                        segment = Util.GetValueOfString(dr["Segment"]);
                        rep = Util.GetValueOfString(dr["Rep"]);
                        tierCode = Util.GetValueOfString(dr["Tier_Code"]);
                        custValue = Util.GetValueOfDecimal(dr["Cust_Value"]);
                        groupId = Util.GetValueOfInt(dr["Group_Id"]);
                    }
                }
                finally
                {
                    CloseReader(dr);
                }

                if (customerName == null)
                {
                    return Json(new { error = "Customer not found" }, JsonRequestBehavior.AllowGet);
                }

                bool isKeyClient = Array.IndexOf(KeyClientGroupIds, groupId) >= 0;

                // 2. Open ticket count for this customer.
                int openTickets = CountOpenTickets(ctx, C_BPartner_ID);

                // 3. Active (non-opportunity) project count.
                int projects = CountActiveProjects(ctx, C_BPartner_ID);

                // 4. Open-pipeline value + count.
                decimal pipelineValue = 0;
                int pipelineCount = 0;
                GetCustomerPipeline(ctx, C_BPartner_ID, currency.CurrencyId, out pipelineValue, out pipelineCount);

                // 5. Overdue receivables (amount, oldest invoice + days past due).
                decimal overdueAmount = 0;
                int overdueInvoiceCount = 0;
                int overdueDays = 0;
                string overdueInvoice = "";
                GetCustomerOverdue(ctx, C_BPartner_ID, currency.CurrencyId, currency.StdPrecision,
                    out overdueAmount, out overdueInvoiceCount, out overdueDays, out overdueInvoice);

                var result = new
                {
                    customerId = C_BPartner_ID,
                    name = customerName,
                    contactName = contactName,
                    contactTitle = contactTitle,
                    contactEmail = contactEmail,
                    segment = segment,
                    rep = rep,
                    tierCode = tierCode,
                    tier = MapTier(tierCode),
                    isKeyClient = isKeyClient,
                    value = custValue,
                    currency_symbol = currency.Symbol,
                    currency_iso = currency.IsoCode,
                    std_precision = currency.StdPrecision,
                    openTickets = openTickets,
                    projects = projects,
                    pipelineValue = pipelineValue,
                    pipelineCount = pipelineCount,
                    overdueAmount = overdueAmount,
                    overdueInvoiceCount = overdueInvoiceCount,
                    overdueDays = overdueDays,
                    overdueInvoice = overdueInvoice,
                    // No onboarding-status column in the schema; the UI shows a dash.
                    onboarding = (string)null
                };

                return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_126_OpenTicketsWidget.GetCustomerDetail", ex);
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        // Activity types offered by the Log popup; stored as a summary prefix so the
        // logged R_Request records the kind of interaction.
        private static readonly string[] ActivityTypes = { "Call", "Note", "Meeting", "Email" };

        /// <summary>
        /// Saves a logged CRM activity (Call/Note/Meeting/Email + summary) against a
        /// customer. Persisted through the MRequest (R_Request) M-class - never a raw
        /// INSERT - as a CLOSED request so it records the interaction without
        /// inflating the Open Tickets KPI. The activity type is captured as a summary
        /// prefix.
        /// </summary>
        /// <param name="C_BPartner_ID">Customer the activity is logged against.</param>
        /// <param name="activityType">Call/Note/Meeting/Email.</param>
        /// <param name="summary">Free-text "what happened / next step".</param>
        /// <returns>JSON { success, R_Request_ID } or { error }.</returns>
        [HttpPost]
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        [ValidateInput(false)]
        public JsonResult SaveActivityLog(int C_BPartner_ID, string activityType, string summary)
        {
            if (Session["ctx"] == null)
            {
                return Json(new
                {
                    error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired"
                }, JsonRequestBehavior.AllowGet);
            }

            if (C_BPartner_ID <= 0)
            {
                return Json(new { error = "Invalid customer" }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            summary = (summary ?? "").Trim();
            if (summary.Length == 0)
            {
                return Json(new
                {
                    error = Msg.GetMsg(ctx, "VAS_126_SummaryRequired") ?? "Please enter a summary."
                }, JsonRequestBehavior.AllowGet);
            }

            // Only accept a known type; default to Note.
            string type = "Note";
            for (int i = 0; i < ActivityTypes.Length; i++)
            {
                if (string.Equals(ActivityTypes[i], activityType, StringComparison.OrdinalIgnoreCase))
                {
                    type = ActivityTypes[i];
                    break;
                }
            }

            try
            {
                MRequestType defaultType = MRequestType.GetDefault(ctx);
                if (defaultType == null)
                {
                    return Json(new
                    {
                        error = Msg.GetMsg(ctx, "VAS_126_NoRequestType") ?? "No default request type is configured."
                    }, JsonRequestBehavior.AllowGet);
                }

                string prefixedSummary = "[" + type + "] " + summary;
                if (prefixedSummary.Length > 2000)
                {
                    prefixedSummary = prefixedSummary.Substring(0, 2000);
                }

                // Persist via the M-class (no raw INSERT). Current user is the logger.
                MRequest request = new MRequest(ctx, ctx.GetAD_User_ID(), defaultType.GetR_RequestType_ID(), prefixedSummary, false, null);
                request.SetC_BPartner_ID(C_BPartner_ID);
                if (request.GetAD_Org_ID() == 0)
                {
                    request.SetAD_Org_ID(ctx.GetAD_Org_ID());
                }

                // Closed status so the logged activity is not counted as an open ticket.
                int closedStatusId = GetClosedStatusId(ctx);
                if (closedStatusId > 0)
                {
                    request.SetR_Status_ID(closedStatusId);
                }
                else
                {
                    request.SetR_Status_ID();
                }

                if (!request.Save())
                {
                    // Detailed save error is captured by the framework log; the client
                    // gets an unobtrusive message.
                    return Json(new
                    {
                        error = Msg.GetMsg(ctx, "VAS_126_LogSaveFailed") ?? "Could not save the activity."
                    }, JsonRequestBehavior.AllowGet);
                }

                return Json(new { success = true, R_Request_ID = request.GetR_Request_ID() }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_126_OpenTicketsWidget.SaveActivityLog", ex);
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Resolves a closed R_Status for the tenant so the logged activity does not
        /// appear as an open ticket. Returns 0 when none is configured.
        /// </summary>
        private int GetClosedStatusId(Ctx ctx)
        {
            string sql = @"
                SELECT rs.R_Status_ID
                FROM R_Status rs
                WHERE rs.IsActive = 'Y'
                  AND COALESCE(rs.IsClosed, 'N') = 'Y'
                  AND rs.AD_Client_ID IN (0, @Client_ID)
                ORDER BY rs.AD_Client_ID DESC, rs.R_Status_ID";
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "rs", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            return Util.GetValueOfInt(DB.ExecuteScalar(sql, new SqlParameter[] { new SqlParameter("@Client_ID", ctx.GetAD_Client_ID()) }, null));
        }

        /// <summary>Open-ticket count for one customer (MRole on R_Request).</summary>
        private int CountOpenTickets(Ctx ctx, int bpId)
        {
            string sql = @"
                SELECT COUNT(DISTINCT r.R_Request_ID) AS Cnt
                FROM R_Request r
                INNER JOIN R_Status s ON (s.R_Status_ID=r.R_Status_ID AND s.AD_Client_ID=r.AD_Client_ID AND s.IsActive = 'Y')
                WHERE r.IsActive = 'Y'
                  AND r.AD_Client_ID = @Client_ID
                  AND r.C_BPartner_ID = @BP_ID
                  AND s.IsOpen = 'Y'
                  AND COALESCE(s.IsClosed, 'N') = 'N'";
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "r", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            return ReadInt(sql, ctx, bpId, "Cnt");
        }

        /// <summary>Active non-opportunity project count for one customer.</summary>
        private int CountActiveProjects(Ctx ctx, int bpId)
        {
            string sql = @"
                SELECT COUNT(1) AS Cnt
                FROM C_Project p
                WHERE p.IsActive = 'Y'
                  AND p.AD_Client_ID = @Client_ID
                  AND p.C_BPartner_ID = @BP_ID
                  AND (p.VAS_ProjectStatus IS NULL OR p.VAS_ProjectStatus NOT IN ('DR', 'IP'))";
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "p", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            return ReadInt(sql, ctx, bpId, "Cnt");
        }

        /// <summary>Open-pipeline value (base currency) + count for one customer.</summary>
        private void GetCustomerPipeline(Ctx ctx, int bpId, int currencyId, out decimal value, out int count)
        {
            value = 0;
            count = 0;

            string conversionDate = DB.IsPostgreSQL() ? "CURRENT_DATE" : "TRUNC(SYSDATE)";
            int conversionTypeId = MConversionType.GetDefault(ctx.GetAD_Client_ID());

            string convertedAmount = @"CurrencyConvert(COALESCE(p.PlannedAmt, 0), p.C_Currency_ID, @Ccy_ID, "
                + conversionDate + @", @ConvType_ID, p.AD_Client_ID, p.AD_Org_ID)";

            string sql = @"
                SELECT COALESCE(SUM(CASE WHEN p.C_Currency_ID = @Ccy_ID THEN COALESCE(p.PlannedAmt, 0) ELSE " + convertedAmount + @" END), 0) AS Val,
                       COUNT(DISTINCT p.C_Project_ID) AS Cnt
                FROM C_Project p
                WHERE p.IsActive = 'Y'
                  AND p.C_BPartner_ID = @BP_ID
                  AND p.AD_Client_ID = @Client_ID
                  AND p.VAS_ProjectStatus IN ('DR', 'IP')";
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "p", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, new SqlParameter[]
                {
                    new SqlParameter("@Client_ID", ctx.GetAD_Client_ID()),
                    new SqlParameter("@BP_ID", bpId),
                    new SqlParameter("@Ccy_ID", currencyId),
                    new SqlParameter("@ConvType_ID", conversionTypeId)
                });
                if (dr != null && dr.Read())
                {
                    value = Util.GetValueOfDecimal(dr["Val"]);
                    count = Util.GetValueOfInt(dr["Cnt"]);
                }
            }
            finally
            {
                CloseReader(dr);
            }
        }

        /// <summary>Overdue receivables for one customer: base-currency amount, invoice
        /// count, the oldest overdue invoice number, and its days past due.</summary>
        private void GetCustomerOverdue(Ctx ctx, int bpId, int currencyId, int precision,
            out decimal amount, out int invoiceCount, out int daysPastDue, out string invoiceNo)
        {
            amount = 0;
            invoiceCount = 0;
            daysPastDue = 0;
            invoiceNo = "";

            string overdueDateCondition = DB.IsPostgreSQL()
                ? " AND CAST(ips.DueDate AS DATE) < CAST(CURRENT_DATE AS DATE)"
                : " AND TRUNC(ips.DueDate) < TRUNC(SYSDATE)";

            SqlParameter[] pars = new SqlParameter[]
            {
                new SqlParameter("@Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@BP_ID", bpId),
                new SqlParameter("@Ccy_ID", currencyId)
            };

            // Aggregate amount + count + oldest due date.
            string aggSql = @"
                SELECT COALESCE(SUM(CASE
                           WHEN i.IsReturnTrx = 'N' THEN CurrencyConvert(COALESCE(ips.DueAmt, 0), i.C_Currency_ID, @Ccy_ID, i.DateAcct, i.C_ConversionType_ID, i.AD_Client_ID, i.AD_Org_ID)
                           WHEN i.IsReturnTrx = 'Y' THEN -CurrencyConvert(COALESCE(ips.DueAmt, 0), i.C_Currency_ID, @Ccy_ID, i.DateAcct, i.C_ConversionType_ID, i.AD_Client_ID, i.AD_Org_ID)
                           ELSE 0 END), 0) AS Amt,
                       COUNT(DISTINCT i.C_Invoice_ID) AS Cnt,
                       MIN(ips.DueDate) AS Oldest_Due
                FROM C_InvoicePaySchedule ips
                INNER JOIN C_Invoice i ON (ips.C_Invoice_ID=i.C_Invoice_ID AND i.IsActive = 'Y')
                WHERE ips.IsActive = 'Y'
                  AND ips.VA009_IsPaid = 'N'" + overdueDateCondition + @"
                  AND i.DocStatus IN ('CO', 'CL')
                  AND i.IsSOTrx = 'Y'
                  AND i.C_BPartner_ID = @BP_ID
                  AND i.AD_Client_ID = @Client_ID";
            aggSql = MRole.GetDefault(ctx).AddAccessSQL(aggSql, "ips", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            DateTime? oldestDue = null;
            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(aggSql, pars);
                if (dr != null && dr.Read())
                {
                    amount = Math.Round(Util.GetValueOfDecimal(dr["Amt"]), precision);
                    invoiceCount = Util.GetValueOfInt(dr["Cnt"]);
                    if (dr["Oldest_Due"] != null && dr["Oldest_Due"] != DBNull.Value)
                    {
                        oldestDue = Util.GetValueOfDateTime(dr["Oldest_Due"]);
                    }
                }
            }
            finally
            {
                CloseReader(dr);
            }

            if (oldestDue.HasValue)
            {
                daysPastDue = (DateTime.Today - oldestDue.Value.Date).Days;
                if (daysPastDue < 0) { daysPastDue = 0; }
            }

            if (invoiceCount == 0) { return; }

            // Oldest overdue invoice number for the signal line.
            string topSql = @"
                SELECT i.DocumentNo AS DocNo
                FROM C_InvoicePaySchedule ips
                INNER JOIN C_Invoice i ON (ips.C_Invoice_ID=i.C_Invoice_ID AND i.IsActive = 'Y')
                WHERE ips.IsActive = 'Y'
                  AND ips.VA009_IsPaid = 'N'" + overdueDateCondition + @"
                  AND i.DocStatus IN ('CO', 'CL')
                  AND i.IsSOTrx = 'Y'
                  AND i.C_BPartner_ID = @BP_ID
                  AND i.AD_Client_ID = @Client_ID";
            topSql = MRole.GetDefault(ctx).AddAccessSQL(topSql, "ips", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            topSql += @"
                ORDER BY ips.DueDate ASC
                OFFSET 0 ROWS FETCH NEXT 1 ROWS ONLY";

            IDataReader topReader = null;
            try
            {
                topReader = DB.ExecuteReader(topSql, new SqlParameter[]
                {
                    new SqlParameter("@Client_ID", ctx.GetAD_Client_ID()),
                    new SqlParameter("@BP_ID", bpId),
                    new SqlParameter("@Ccy_ID", currencyId)
                });
                if (topReader != null && topReader.Read())
                {
                    invoiceNo = Util.GetValueOfString(topReader["DocNo"]);
                }
            }
            finally
            {
                CloseReader(topReader);
            }
        }

        /// <summary>Reads a single integer column from a customer-scoped query.</summary>
        private int ReadInt(string sql, Ctx ctx, int bpId, string column)
        {
            int value = 0;
            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, new SqlParameter[]
                {
                    new SqlParameter("@Client_ID", ctx.GetAD_Client_ID()),
                    new SqlParameter("@BP_ID", bpId)
                });
                if (dr != null && dr.Read())
                {
                    value = Util.GetValueOfInt(dr[column]);
                }
            }
            finally
            {
                CloseReader(dr);
            }
            return value;
        }

        /// <summary>
        /// Maps a raw C_BPartner.Rating code to a display tier via the approved
        /// configuration; null when unmapped (the client then shows the raw code as
        /// a neutral tag rather than a guessed tier name).
        /// </summary>
        private string MapTier(string ratingCode)
        {
            if (string.IsNullOrEmpty(ratingCode)) { return null; }
            string tier;
            return CustomerTierByRating.TryGetValue(ratingCode, out tier) ? tier : null;
        }

        /// <summary>
        /// Reads the tenant accounting currency (symbol, ISO, precision) for the
        /// triage list ARR/value formatting.
        /// </summary>
        private SchemaCurrency GetSchemaCurrency(Ctx ctx)
        {
            SchemaCurrency currency = new SchemaCurrency();
            if (ctx == null) { return currency; }

            string sql = @"
                SELECT cs.C_Currency_ID AS Currency_Id,
                       cur.StdPrecision AS Std_Precision,
                       CASE WHEN cur.CurSymbol IS NOT NULL THEN cur.CurSymbol ELSE cur.ISO_Code END AS Cur_Symbol,
                       cur.ISO_Code AS ISO_Code
                FROM AD_ClientInfo ci
                INNER JOIN C_AcctSchema cs ON (cs.C_AcctSchema_ID=ci.C_AcctSchema1_ID AND cs.IsActive = 'Y')
                INNER JOIN C_Currency cur ON (cur.C_Currency_ID=cs.C_Currency_ID AND cur.IsActive = 'Y')
                WHERE ci.IsActive = 'Y'
                  AND ci.AD_Client_ID = @Client_ID";

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "ci", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, new SqlParameter[] { new SqlParameter("@Client_ID", ctx.GetAD_Client_ID()) });
                if (dr != null && dr.Read())
                {
                    currency.CurrencyId = Util.GetValueOfInt(dr["Currency_Id"]);
                    currency.StdPrecision = Util.GetValueOfInt(dr["Std_Precision"]);
                    currency.Symbol = Util.GetValueOfString(dr["Cur_Symbol"]);
                    currency.IsoCode = Util.GetValueOfString(dr["ISO_Code"]);
                }
            }
            finally
            {
                CloseReader(dr);
            }

            return currency;
        }

        private void CloseReader(IDataReader reader)
        {
            if (reader == null) { return; }
            reader.Close();
            reader.Dispose();
        }

        private class SchemaCurrency
        {
            public int CurrencyId { get; set; }
            public string Symbol { get; set; }
            public string IsoCode { get; set; }
            public int StdPrecision { get; set; }
        }
    }
}
