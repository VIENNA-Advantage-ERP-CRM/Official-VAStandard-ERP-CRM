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

        // Display tiers. The client colours these three by name (violet / amber /
        // info); any other label falls back to its neutral tag.
        private const string TierPlatinum = "Platinum";
        private const string TierGold = "Gold";
        private const string TierSilver = "Silver";

        /// <summary>
        /// Optional OVERRIDE of the tier label per Rating code. Intentionally EMPTY,
        /// and normally stays that way: the query now resolves the tag from the
        /// tenant's own application dictionary (AD_Column('Rating') -> AD_Ref_List,
        /// translated via AD_Ref_List_Trl), so no hard-coded table is needed and no
        /// rating is ever labelled by guesswork. Populate this only to force different
        /// wording than the reference list carries. Entries here win over the
        /// reference-list name. Same contract as VAS_120.
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
                           COALESCE(seg.Segment_Name, N'') AS Segment,
                           COALESCE(seg.Segment_Count, 0) AS Segment_Count,
                           COALESCE(owner.Name, N'') AS Rep,
                           bp.Rating AS Tier_Code,
                           COALESCE(RatingTrl.Name, RatingList.Name, N'') AS Tier_Name,
                           COALESCE(bp.ActualLifeTimeValue, 0) AS Cust_Value
                    FROM C_BPartner bp
                    INNER JOIN OpenTicketCustomers otc ON (otc.C_BPartner_ID=bp.C_BPartner_ID)
                    LEFT OUTER JOIN RankedContacts c ON (c.C_BPartner_ID=bp.C_BPartner_ID AND c.RN=1)
                    LEFT OUTER JOIN CustomerSegment seg ON (seg.C_BPartner_ID=bp.C_BPartner_ID)
                    LEFT OUTER JOIN AD_User owner ON (owner.AD_User_ID=bp.SalesRep_ID AND owner.AD_Client_ID=bp.AD_Client_ID AND owner.IsActive = 'Y')" + RatingLabelJoins + @"
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
                    CustomerSegment AS (
                        " + CustomerSegmentCte + @"
                    ),
                    CustomerRows AS (
                        " + customerRowsSql + @"
                    )
                    SELECT cr.Customer_Id,
                           cr.Customer_Name,
                           cr.Contact,
                           cr.Segment,
                           cr.Segment_Count,
                           cr.Rep,
                           cr.Tier_Code,
                           cr.Tier_Name,
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
                            new SqlParameter("@Client_ID", ctx.GetAD_Client_ID()),
                            new SqlParameter("@AD_Language", SqlDbType.NVarChar) { Value = GetLanguage(ctx) }
                        }
                    );

                    while (dr != null && dr.Read())
                    {
                        total = Util.GetValueOfInt(dr["Total_Customers"]);
                        string tierCode = Util.GetValueOfString(dr["Tier_Code"]);
                        string tierName = Util.GetValueOfString(dr["Tier_Name"]);
                        items.Add(new
                        {
                            customerId = Util.GetValueOfInt(dr["Customer_Id"]),
                            customerName = Util.GetValueOfString(dr["Customer_Name"]),
                            contact = Util.GetValueOfString(dr["Contact"]),
                            segment = Util.GetValueOfString(dr["Segment"]),
                            segmentCount = Util.GetValueOfInt(dr["Segment_Count"]),
                            rep = Util.GetValueOfString(dr["Rep"]),
                            tierCode = tierCode,
                            tier = MapTier(tierCode, tierName),
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

                // Order follows first textual appearance in profileSql (Oracle binds
                // positionally): @Client_ID and @BP_ID inside the RankedContacts CTE,
                // then @AD_Language in the rating-label joins of the profile body.
                SqlParameter[] keyParams = new SqlParameter[]
                {
                    new SqlParameter("@Client_ID", clientId),
                    new SqlParameter("@BP_ID", C_BPartner_ID),
                    new SqlParameter("@AD_Language", SqlDbType.NVarChar) { Value = GetLanguage(ctx) }
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
                           COALESCE(seg.Segment_Name, N'') AS Segment,
                           COALESCE(seg.Segment_Count, 0) AS Segment_Count,
                           COALESCE(owner.Name, N'') AS Rep,
                           bp.Rating AS Tier_Code,
                           COALESCE(RatingTrl.Name, RatingList.Name, N'') AS Tier_Name,
                           COALESCE(bp.ActualLifeTimeValue, 0) AS Cust_Value,
                           COALESCE(bp.C_BP_Group_ID, 0) AS Group_Id,
                           " + ProfileCompletionExpr + @" AS Onb_Progress
                    FROM C_BPartner bp
                    LEFT OUTER JOIN RankedContacts c ON (c.RN=1)
                    LEFT OUTER JOIN CustomerSegment seg ON (seg.C_BPartner_ID=bp.C_BPartner_ID)
                    LEFT OUTER JOIN AD_User owner ON (owner.AD_User_ID=bp.SalesRep_ID AND owner.AD_Client_ID=bp.AD_Client_ID AND owner.IsActive = 'Y')" + RatingLabelJoins + @"
                    WHERE bp.C_BPartner_ID = @BP_ID
                      AND bp.IsActive = 'Y'
                      AND bp.AD_Client_ID = @Client_ID";
                profileBodySql = MRole.GetDefault(ctx).AddAccessSQL(profileBodySql, "bp", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                string profileSql = "WITH RankedContacts AS (" + rankedContactsSql + "), CustomerSegment AS (" + CustomerSegmentCte + ") " + profileBodySql;

                string customerName = null;
                string contactName = "", contactTitle = "", contactEmail = "", segment = "", rep = "", tierCode = "", tierName = "";
                decimal custValue = 0;
                int groupId = 0, segmentCount = 0, onboardingPercent = 0;

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
                        segmentCount = Util.GetValueOfInt(dr["Segment_Count"]);
                        rep = Util.GetValueOfString(dr["Rep"]);
                        tierCode = Util.GetValueOfString(dr["Tier_Code"]);
                        tierName = Util.GetValueOfString(dr["Tier_Name"]);
                        custValue = Util.GetValueOfDecimal(dr["Cust_Value"]);
                        groupId = Util.GetValueOfInt(dr["Group_Id"]);
                        onboardingPercent = Util.GetValueOfInt(dr["Onb_Progress"]);
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

                // 3. Active (non-opportunity) projects: count + the first project name.
                string projectName;
                int projects = GetActiveProjects(ctx, C_BPartner_ID, out projectName);

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
                    segmentCount = segmentCount,
                    rep = rep,
                    tierCode = tierCode,
                    tier = MapTier(tierCode, tierName),
                    isKeyClient = isKeyClient,
                    value = custValue,
                    currency_symbol = currency.Symbol,
                    currency_iso = currency.IsoCode,
                    std_precision = currency.StdPrecision,
                    openTickets = openTickets,
                    projects = projects,
                    projectName = projectName,
                    pipelineValue = pipelineValue,
                    pipelineCount = pipelineCount,
                    overdueAmount = overdueAmount,
                    overdueInvoiceCount = overdueInvoiceCount,
                    overdueDays = overdueDays,
                    overdueInvoice = overdueInvoice,
                    // Onboarding completion for this customer, 0..100. Same scoring
                    // rule as the VAS_136 donut and the VAS_137 "profile incomplete"
                    // reason, so the detail panel agrees with both.
                    onboardingPercent = onboardingPercent
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

        /// <summary>
        /// Active delivery projects for one customer: how many, and the name of the
        /// first (alphabetically) so the detail panel can name the project rather than
        /// only counting it. C_Project is joined on C_BPartner_ID, per the supplied
        /// query.
        ///
        /// The 'DR'/'IP' exclusion is retained: in this deployment those are
        /// pipeline/opportunity stages rather than delivery projects, as documented on
        /// VAS_135_ActiveProjectsWidget, which counts the same population. Dropping it
        /// here would put opportunities in the Projects fact and disagree with that
        /// widget - e.g. Devcast Inc has 7 C_Project rows of which only 1 is a delivery
        /// project, and Himalayan Foods has 3 of which none are.
        ///
        /// MIN(Name) rather than a string aggregate: STRING_AGG / LISTAGG differ across
        /// the SQL Server, Oracle and PostgreSQL targets. The count travels alongside
        /// so the client can render "Name +N".
        /// </summary>
        /// <param name="ctx">Authenticated request context.</param>
        /// <param name="bpId">Customer to inspect.</param>
        /// <param name="projectName">First project name; empty when there are none.</param>
        /// <returns>Active delivery project count.</returns>
        private int GetActiveProjects(Ctx ctx, int bpId, out string projectName)
        {
            projectName = "";

            string sql = @"
                SELECT COUNT(1) AS Cnt,
                       MIN(p.Name) AS First_Name
                FROM C_Project p
                WHERE p.IsActive = 'Y'
                  AND p.AD_Client_ID = @Client_ID
                  AND p.C_BPartner_ID = @BP_ID
                  AND (p.VAS_ProjectStatus IS NULL OR p.VAS_ProjectStatus NOT IN ('DR', 'IP'))";
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "p", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            int count = 0;
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
                    count = Util.GetValueOfInt(dr["Cnt"]);
                    projectName = Util.GetValueOfString(dr["First_Name"]);
                }
            }
            finally
            {
                CloseReader(dr);
            }

            return count;
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
        /// Resolves the display tier for one row. An explicit tenant mapping in
        /// CustomerTierByRating wins when present; otherwise the tenant's own
        /// AD_Ref_List label for the Rating code (already translated by the query) is
        /// used. Returns null when the rating is unset or carries no list entry, so the
        /// UI shows no tag and an unknown rating is never labelled. Identical rule to
        /// VAS_120 so the same customer reads the same tier in both widgets.
        /// </summary>
        /// <param name="ratingCode">Stored Rating value (may be null/empty).</param>
        /// <param name="ratingName">AD_Ref_List/Trl name for that code (may be empty).</param>
        /// <returns>The configured tier, else the folded star tier, else the
        /// reference-list label, else null.</returns>
        private string MapTier(string ratingCode, string ratingName)
        {
            if (string.IsNullOrEmpty(ratingCode)) { return null; }

            // An explicit per-tenant override always wins.
            string tier;
            if (CustomerTierByRating.TryGetValue(ratingCode, out tier)) { return tier; }

            // Star-rating lists (the stock _Rating reference labels its entries
            // "-", "*", "**", ...) are folded onto the three display tiers per the
            // approved business rule: 3+ stars Platinum, 2 Gold, 1 Silver. Reading
            // the star COUNT off the label keeps this independent of the stored
            // Value codes, which differ between tenants.
            int stars = CountRatingStars(ratingName);
            if (stars >= 3) { return TierPlatinum; }
            if (stars == 2) { return TierGold; }
            if (stars == 1) { return TierSilver; }

            // Not a star list: fall back to the tenant's own label (e.g. "Preferred").
            // The zero-star entry is "not rated", so it -- like a blank label -- draws
            // no tag; an unknown rating is still never named.
            if (string.IsNullOrWhiteSpace(ratingName)) { return null; }

            string trimmed = ratingName.Trim();
            return trimmed == "-" ? null : trimmed;
        }

        /// <summary>
        /// Counts star glyphs in a rating label. Both the ASCII asterisk used by the
        /// stock reference data and the unicode star some tenants store are counted,
        /// so the fold works whichever form the dictionary holds.
        /// </summary>
        /// <param name="ratingName">Reference-list label (may be null/empty).</param>
        /// <returns>Number of star glyphs; 0 when the label carries none.</returns>
        private static int CountRatingStars(string ratingName)
        {
            if (string.IsNullOrEmpty(ratingName)) { return 0; }

            int stars = 0;
            foreach (char character in ratingName)
            {
                // U+2605 BLACK STAR and U+2606 WHITE STAR alongside the ASCII
                // asterisk, so the count works whichever glyph the list uses.
                if (character == '*' || character == '★' || character == '☆')
                {
                    stars++;
                }
            }

            return stars;
        }

        /// <summary>
        /// Customer segment = the marketing target list the customer belongs to.
        /// C_TargetList holds the membership rows and C_MasterTargetList carries the
        /// segment name ("West Region Prospect", ...); C_CampaignTargetList is what
        /// points a campaign at those same master lists.
        ///
        /// The membership is joined on C_TargetList.C_BPartner_ID only, per the
        /// supplied join path. NOTE: C_TargetList also carries Ref_BPartner_ID, and the
        /// two are mutually exclusive - a row populates one or the other. Rows that use
        /// Ref_BPartner_ID therefore resolve to no segment here (2 of the 11 active
        /// rows in the reference tenant: Fortec Web Solutions, H&T Company). Widen the
        /// join to COALESCE(tl.C_BPartner_ID, tl.Ref_BPartner_ID) if those should count.
        ///
        /// A customer can sit in several segments. Rather than a non-portable string
        /// aggregate (STRING_AGG / LISTAGG differ across the SQL Server, Oracle and
        /// PostgreSQL targets), this returns the alphabetically first segment plus the
        /// total count, and the client renders "First segment +N" - so extra segments
        /// are surfaced rather than silently dropped.
        ///
        /// Secondary source: NOT MRole-filtered, matching the contacts lookup in
        /// VAS_122 - the customers themselves are already access-filtered by the query
        /// this CTE is joined into. IsActive and AD_Client_ID are kept for tenancy.
        /// </summary>
        private const string CustomerSegmentCte = @"
            SELECT tl.C_BPartner_ID AS C_BPartner_ID,
                   MIN(mtl.Name) AS Segment_Name,
                   COUNT(DISTINCT mtl.C_MasterTargetList_ID) AS Segment_Count
            FROM C_TargetList tl
            INNER JOIN C_MasterTargetList mtl ON (mtl.C_MasterTargetList_ID=tl.C_MasterTargetList_ID AND mtl.AD_Client_ID=tl.AD_Client_ID AND mtl.IsActive = 'Y')
            WHERE tl.IsActive = 'Y'
              AND tl.AD_Client_ID = @Client_ID
              AND tl.C_BPartner_ID IS NOT NULL
            GROUP BY tl.C_BPartner_ID";

        /// <summary>
        /// Onboarding / profile completion % (0..100), correlated to the outer
        /// C_BPartner alias "bp". Scored 20 points per profile section that holds data
        /// - the customer itself, a location (C_BPartner_Location), a contact
        /// (AD_User), a bank account (C_BP_BankAccount) and a customer accounting
        /// record (FRPT_BP_Customer_Acct). Each DISTINCT 20 line contributes its 20
        /// only when that section exists, so the sum is a multiple of 20 up to 100.
        /// There is no VAS_ProfileCompletion column. Identical rule to VAS_136 and
        /// VAS_137, so the same customer reads the same percentage everywhere.
        /// Portable across Oracle and PostgreSQL.
        /// </summary>
        private const string ProfileCompletionExpr = @"COALESCE((
                        SELECT SUM(t.Cnt)
                        FROM (
                            SELECT DISTINCT 20 AS Cnt FROM C_BPartner
                            UNION ALL SELECT DISTINCT 20 AS Cnt FROM C_BPartner_Location bpl WHERE bpl.C_BPartner_ID = bp.C_BPartner_ID
                            UNION ALL SELECT DISTINCT 20 AS Cnt FROM AD_User bpu WHERE bpu.C_BPartner_ID = bp.C_BPartner_ID
                            UNION ALL SELECT DISTINCT 20 AS Cnt FROM C_BP_BankAccount bpb WHERE bpb.C_BPartner_ID = bp.C_BPartner_ID
                            UNION ALL SELECT DISTINCT 20 AS Cnt FROM FRPT_BP_Customer_Acct bpc WHERE bpc.C_BPartner_ID = bp.C_BPartner_ID
                        ) t
                    ), 0)";

        /// <summary>Session language for the AD_Ref_List_Trl join, falling back to en_US.</summary>
        private string GetLanguage(Ctx ctx)
        {
            string language = ctx == null ? string.Empty : ctx.GetAD_Language();
            return string.IsNullOrEmpty(language) ? "en_US" : language;
        }

        /// <summary>
        /// Dictionary joins that resolve C_BPartner.Rating to its tenant label:
        /// AD_Table -> AD_Column('Rating') -> AD_Ref_List, translated through
        /// AD_Ref_List_Trl when the session language has an entry. Written against the
        /// alias "bp". Requires an @AD_Language parameter on the query.
        /// </summary>
        private const string RatingLabelJoins = @"
                    LEFT OUTER JOIN AD_Table BPartnerTable ON (BPartnerTable.TableName = 'C_BPartner')
                    LEFT OUTER JOIN AD_Column RatingColumn ON (RatingColumn.AD_Table_ID = BPartnerTable.AD_Table_ID AND RatingColumn.ColumnName = 'Rating' AND RatingColumn.IsActive = 'Y')
                    LEFT OUTER JOIN AD_Ref_List RatingList ON (RatingList.AD_Reference_ID = RatingColumn.AD_Reference_Value_ID AND RatingList.Value = bp.Rating AND RatingList.IsActive = 'Y')
                    LEFT OUTER JOIN AD_Ref_List_Trl RatingTrl ON (RatingTrl.AD_Ref_List_ID = RatingList.AD_Ref_List_ID AND RatingTrl.AD_Language = @AD_Language)";

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
