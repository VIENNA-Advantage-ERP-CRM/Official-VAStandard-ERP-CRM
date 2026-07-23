/******************************************************
 * Module Name    : VAS
 * Purpose        : Customers module Customer Search widget endpoint
 * chronological  : Development
 * Created Date   : 2026-07-20
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
    /// Module Name : VAS_120_CustomerSearchWidget
    /// Purpose     : Provides secured, tenant-isolated customer suggestions to the
    ///               Customers module Customer Search widget. One deterministic
    ///               primary contact is resolved per customer (prefers a contact
    ///               that has an e-mail, then the most recently updated) so the
    ///               list needs a single query. MRole supplies tenant + record
    ///               access; per the CTE rule it is applied only to the main
    ///               physical table (C_BPartner), never to the CTE aliases or the
    ///               secondary AD_User / C_BP_Group joins.
    /// Chronological development:
    ///   VAI052      2026-07-20 Created
    /// </summary>
    public class VAS_120_CustomerSearchWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_120_CustomerSearchWidgetController).FullName);

        // Autosuggest is bounded to seven rows (design spec + Querry.md Query 1).
        private const int MaxSuggestRows = 7;

        // Server-side minimum trimmed characters before the DB is touched. The
        // client debounces and enforces the same floor; this is the backstop.
        private const int MinSearchLength = 2;

        /// <summary>
        /// Tier tag is only shown when the tenant has an explicit, approved mapping
        /// from C_BPartner.Rating to Platinum/Gold/Silver. The supplied application
        /// dictionary proves Rating is a list column but does NOT prove its stored
        /// codes, so this map is intentionally EMPTY: an unmapped rating yields a
        /// null tier and the UI shows no tag. Never label an unknown rating.
        /// Populate only after checking the target tenant's dictionary, e.g.
        /// { "A", "Platinum" }, { "B", "Gold" }, { "C", "Silver" }.
        /// </summary>
        private static readonly Dictionary<string, string> CustomerTierByRating =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Type-ahead customer search for the suggestion dropdown. Returns at most
        /// seven relevance-ranked, tenant-scoped active customers plus the total
        /// match count used by the "See all matches" affordance.
        /// </summary>
        /// <param name="q">Trimmed user search text (no % wrapping by the caller).</param>
        /// <param name="max">Requested row cap; clamped to seven.</param>
        /// <returns>JSON { Rows:[...], Total, MinLength } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        [ValidateInput(false)]
        public JsonResult SearchCustomers(string q, int max = MaxSuggestRows)
        {
            if (Session["ctx"] == null)
            {
                return Json("", JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            try
            {
                string json = JsonConvert.SerializeObject(SearchCustomersData(ctx, q, max));
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_120_CustomerSearchWidget.SearchCustomers", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// Builds the autosuggest rows and total count for the given search text.
        /// </summary>
        /// <param name="ctx">Authenticated request context (tenant/org/role).</param>
        /// <param name="searchText">Raw user input; trimmed and length-checked here.</param>
        /// <param name="maxRows">Requested cap, clamped to seven.</param>
        /// <returns>Populated result; empty rows when the text is too short.</returns>
        private CustomerSearchResult SearchCustomersData(Ctx ctx, string searchText, int maxRows)
        {
            CustomerSearchResult result = new CustomerSearchResult
            {
                Rows = new List<CustomerSearchRow>(),
                Total = 0,
                MinLength = MinSearchLength
            };

            if (ctx == null) { return result; }

            searchText = (searchText ?? "").Trim();
            if (searchText.Length < MinSearchLength) { return result; }

            if (maxRows <= 0 || maxRows > MaxSuggestRows) { maxRows = MaxSuggestRows; }

            int clientId = ctx.GetAD_Client_ID();
            int orgId = ctx.GetAD_Org_ID();
            string upperText = searchText.ToUpperInvariant();
            string likeValue = "%" + upperText + "%";
            string prefixValue = upperText + "%";

            // Shared CTE (ranked primary contact + secured customer_search body).
            // MRole is applied to the main physical table alias "bp" only.
            string cteSql = BuildCustomerSearchCte(ctx);

            string suggestSql = @"
                " + cteSql + @"
                SELECT CustomerSearch.Id,
                       CustomerSearch.Value,
                       CustomerSearch.Name,
                       CustomerSearch.Contact_Id,
                       CustomerSearch.Contact,
                       CustomerSearch.Segment_Id,
                       CustomerSearch.Segment,
                       CustomerSearch.Owner_Id,
                       CustomerSearch.Rep,
                       CustomerSearch.Tier_Code
                FROM CustomerSearch
                ORDER BY CustomerSearch.Relevance_Rank,
                         CustomerSearch.Name,
                         CustomerSearch.Id
                OFFSET 0 ROWS FETCH NEXT @Max_Rows ROWS ONLY";

            string countSql = @"
                " + cteSql + @"
                SELECT COUNT(1) AS Total_Count
                FROM CustomerSearch";

            // Rows.
            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(suggestSql, BuildSearchParameters(clientId, orgId, likeValue, upperText, prefixValue, maxRows));
                while (dr != null && dr.Read())
                {
                    string tierCode = Util.GetValueOfString(dr["Tier_Code"]);
                    string tier = MapTier(tierCode);
                    result.Rows.Add(new CustomerSearchRow
                    {
                        Id = Util.GetValueOfInt(dr["Id"]),
                        Value = Util.GetValueOfString(dr["Value"]),
                        Name = Util.GetValueOfString(dr["Name"]),
                        ContactId = Util.GetValueOfInt(dr["Contact_Id"]),
                        Contact = Util.GetValueOfString(dr["Contact"]),
                        SegmentId = Util.GetValueOfInt(dr["Segment_Id"]),
                        Segment = Util.GetValueOfString(dr["Segment"]),
                        OwnerId = Util.GetValueOfInt(dr["Owner_Id"]),
                        Rep = Util.GetValueOfString(dr["Rep"]),
                        TierCode = tierCode,
                        Tier = tier,
                        // Key-client is derived only from an explicitly approved
                        // configuration; never inferred silently (Querry.md).
                        IsKey = false
                    });
                }
            }
            finally
            {
                CloseReader(dr);
            }

            // Total (drives "See all N matches").
            IDataReader countReader = null;
            try
            {
                // The count query does not reference @Max_Rows, so it is bound with
                // the shared predicate parameters only (no unused bind).
                countReader = DB.ExecuteReader(countSql, BuildSearchParameters(clientId, orgId, likeValue, upperText, prefixValue, null));
                if (countReader != null && countReader.Read())
                {
                    result.Total = Util.GetValueOfInt(countReader["Total_Count"]);
                }
            }
            finally
            {
                CloseReader(countReader);
            }

            return result;
        }

        /// <summary>
        /// Builds the shared "WITH RankedContacts AS (...), CustomerSearch AS (...)"
        /// prologue. RankedContacts picks one deterministic contact per business
        /// partner (an e-mailed contact first, then most recently updated).
        /// CustomerSearch is the tenant/customer-scoped, search-filtered body with
        /// a relevance rank; MRole record/tenant access is injected on the main
        /// physical table (alias "bp") only, per the CTE MRole rule.
        /// </summary>
        /// <param name="ctx">Request context supplying the role for AddAccessSQL.</param>
        /// <returns>The two-CTE prologue string (no trailing outer SELECT).</returns>
        private string BuildCustomerSearchCte(Ctx ctx)
        {
            // Secondary source: AD_User is NOT MRole-filtered (CTE rule) but still
            // carries the mandatory IsActive + tenant predicates.
            string rankedContactsSql = @"
                SELECT Contact.C_BPartner_ID,
                       Contact.AD_User_ID,
                       Contact.Name AS Contact_Name,
                       Contact.EMail AS Contact_EMail,
                       ROW_NUMBER() OVER (
                           PARTITION BY Contact.C_BPartner_ID
                           ORDER BY CASE WHEN Contact.EMail IS NOT NULL THEN 0 ELSE 1 END,
                                    Contact.Updated DESC,
                                    Contact.AD_User_ID
                       ) AS RN
                FROM AD_User Contact
                WHERE Contact.IsActive = 'Y'
                  AND Contact.AD_Client_ID = @Client_ID";

            // Main physical table body. bp is the primary data source; grp / c /
            // owner are secondary joins used only to resolve display + search text.
            string customerSearchSql = @"
                SELECT bp.C_BPartner_ID AS Id,
                       bp.Value AS Value,
                       bp.Name AS Name,
                       c.AD_User_ID AS Contact_Id,
                       COALESCE(c.Contact_Name, bp.Name, N'') AS Contact,
                       COALESCE(c.Contact_EMail, bp.EMail, N'') AS Contact_Email,
                       grp.C_BP_Group_ID AS Segment_Id,
                       COALESCE(grp.Name, N'') AS Segment,
                       owner.AD_User_ID AS Owner_Id,
                       COALESCE(owner.Name, N'') AS Rep,
                       bp.Rating AS Tier_Code,
                       CASE
                           WHEN UPPER(COALESCE(bp.Name, N'')) = @Search_Exact THEN 0
                           WHEN UPPER(COALESCE(bp.Value, N'')) = @Search_Exact THEN 1
                           WHEN UPPER(COALESCE(bp.Name, N'')) LIKE @Search_Prefix THEN 2
                           WHEN UPPER(COALESCE(c.Contact_Name, bp.Name, N'')) LIKE @Search_Prefix THEN 3
                           ELSE 4
                       END AS Relevance_Rank
                FROM C_BPartner bp
                LEFT OUTER JOIN C_BP_Group grp ON (grp.C_BP_Group_ID = bp.C_BP_Group_ID AND grp.AD_Client_ID = bp.AD_Client_ID AND grp.IsActive = 'Y')
                LEFT OUTER JOIN RankedContacts c ON (c.C_BPartner_ID = bp.C_BPartner_ID AND c.RN = 1)
                LEFT OUTER JOIN AD_User owner ON (owner.AD_User_ID = bp.SalesRep_ID AND owner.AD_Client_ID = bp.AD_Client_ID AND owner.IsActive = 'Y')
                WHERE bp.IsActive = 'Y'
                  AND bp.IsCustomer = 'Y'
                  AND bp.AD_Client_ID = @Client_ID
                  AND bp.AD_Org_ID IN (0, COALESCE(NULLIF(@Org_ID, 0), bp.AD_Org_ID))
                  AND (
                      UPPER(COALESCE(bp.Name, N'')) LIKE @Search_Like
                      OR UPPER(COALESCE(bp.Value, N'')) LIKE @Search_Like
                      OR UPPER(COALESCE(c.Contact_Name, bp.Name, N'')) LIKE @Search_Like
                      OR UPPER(COALESCE(c.Contact_EMail, bp.EMail, N'')) LIKE @Search_Like
                      OR UPPER(COALESCE(grp.Name, N'')) LIKE @Search_Like
                      OR UPPER(COALESCE(owner.Name, N'')) LIKE @Search_Like
                  )";

            // MRole tenant + record access on the main physical table alias only.
            customerSearchSql = MRole.GetDefault(ctx).AddAccessSQL(
                customerSearchSql,
                "bp",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            return @"WITH RankedContacts AS (
                    " + rankedContactsSql + @"
                ),
                CustomerSearch AS (
                    " + customerSearchSql + @"
                )";
        }

        /// <summary>
        /// Fresh parameter array for one command execution (a SqlParameter cannot be
        /// shared across two commands, so each query builds its own).
        /// </summary>
        private SqlParameter[] BuildSearchParameters(int clientId, int orgId, string likeValue, string exactValue, string prefixValue, int? maxRows)
        {
            List<SqlParameter> parameters = new List<SqlParameter>
            {
                new SqlParameter("@Client_ID", clientId),
                new SqlParameter("@Org_ID", orgId),
                new SqlParameter("@Search_Like", SqlDbType.NVarChar) { Value = likeValue },
                new SqlParameter("@Search_Exact", SqlDbType.NVarChar) { Value = exactValue },
                new SqlParameter("@Search_Prefix", SqlDbType.NVarChar) { Value = prefixValue }
            };

            // @Max_Rows only for the suggest fetch; the count query omits it.
            if (maxRows.HasValue)
            {
                parameters.Add(new SqlParameter("@Max_Rows", maxRows.Value));
            }

            return parameters.ToArray();
        }

        /// <summary>
        /// Maps a raw C_BPartner.Rating code to a display tier using the approved
        /// configuration. Returns null for any unmapped code so the UI shows no tag.
        /// </summary>
        /// <param name="ratingCode">Stored Rating value (may be null/empty).</param>
        /// <returns>Platinum/Gold/Silver when mapped; otherwise null.</returns>
        private string MapTier(string ratingCode)
        {
            if (string.IsNullOrEmpty(ratingCode)) { return null; }
            string tier;
            return CustomerTierByRating.TryGetValue(ratingCode, out tier) ? tier : null;
        }

        private void CloseReader(IDataReader reader)
        {
            if (reader == null) { return; }
            reader.Close();
            reader.Dispose();
        }

        private JsonResult ErrorResult(Ctx ctx)
        {
            string message = Msg.GetMsg(ctx, "Error") ?? "Error";
            string json = JsonConvert.SerializeObject(new { Error = message });
            return Json(json, JsonRequestBehavior.AllowGet);
        }

        private class CustomerSearchResult
        {
            public List<CustomerSearchRow> Rows { get; set; }
            public int Total { get; set; }
            public int MinLength { get; set; }
        }

        private class CustomerSearchRow
        {
            public int Id { get; set; }
            public string Value { get; set; }
            public string Name { get; set; }
            public int ContactId { get; set; }
            public string Contact { get; set; }
            public int SegmentId { get; set; }
            public string Segment { get; set; }
            public int OwnerId { get; set; }
            public string Rep { get; set; }
            public string TierCode { get; set; }
            public string Tier { get; set; }
            public bool IsKey { get; set; }
        }
    }
}
