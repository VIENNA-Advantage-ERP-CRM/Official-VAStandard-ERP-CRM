/******************************************************
 * Module Name    : VAS
 * Purpose        : Customers module Recent Updates widget endpoints
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
    /// Module Name : VAS_298_RecentUpdatesWidget
    /// Purpose     : 3x2 list - customers with a CRM activity logged in the last
    ///               12 days, newest first. A customer's "update" is the newest
    ///               of their R_Request rows - the same table VAS_126's Log
    ///               popup (SaveActivityLog) writes to, one CLOSED R_Request per
    ///               logged Call / Note / Meeting / Email, with the kind stored
    ///               as a "[Kind] " prefix on Summary (R_Request carries no
    ///               separate kind column). A request whose Summary carries no
    ///               recognised prefix - e.g. a genuine support ticket rather
    ///               than a popup-logged interaction - reads as "Note", the
    ///               popup's own default. Tier resolution (Rating -> AD_Ref_List
    ///               label, star-fold to Platinum/Gold/Silver) is identical to
    ///               VAS_126/120 so the same customer reads the same tier
    ///               everywhere. MRole (tenant + org + record access) is applied
    ///               to the main physical table R_Request only.
    /// Chronological development:
    ///   VAI052      2026-09-23 Created
    /// </summary>
    public class VAS_298_RecentUpdatesWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_298_RecentUpdatesWidgetController).FullName);

        private const int WidgetPageSize = 7;
        private const int MaxListPageSize = 25;

        /// <summary>An update counts as "recent" within this many days.</summary>
        private const int RecentWindowDays = 12;

        // Display tiers. The client colours these three by name (violet / amber /
        // info); any other label falls back to its neutral tag. Matches VAS_126.
        private const string TierPlatinum = "Platinum";
        private const string TierGold = "Gold";
        private const string TierSilver = "Silver";

        /// <summary>
        /// Optional OVERRIDE of the tier label per Rating code. Intentionally
        /// EMPTY - the query resolves the tag from the tenant's own application
        /// dictionary. Same contract as VAS_120/126.
        /// </summary>
        private static readonly Dictionary<string, string> CustomerTierByRating =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Dictionary joins that resolve C_BPartner.Rating to its tenant label:
        /// AD_Table -> AD_Column('Rating') -> AD_Ref_List, translated through
        /// AD_Ref_List_Trl when the session language has an entry. Written against
        /// the alias "bp". Requires an @AD_Language parameter on the query.
        /// Matches VAS_126.RatingLabelJoins exactly.
        /// </summary>
        private const string RatingLabelJoins = @"
                    LEFT OUTER JOIN AD_Table BPartnerTable ON (BPartnerTable.TableName = 'C_BPartner')
                    LEFT OUTER JOIN AD_Column RatingColumn ON (RatingColumn.AD_Table_ID = BPartnerTable.AD_Table_ID AND RatingColumn.ColumnName = 'Rating' AND RatingColumn.IsActive = 'Y')
                    LEFT OUTER JOIN AD_Ref_List RatingList ON (RatingList.AD_Reference_ID = RatingColumn.AD_Reference_Value_ID AND RatingList.Value = bp.Rating AND RatingList.IsActive = 'Y')
                    LEFT OUTER JOIN AD_Ref_List_Trl RatingTrl ON (RatingTrl.AD_Ref_List_ID = RatingList.AD_Ref_List_ID AND RatingTrl.AD_Language = @AD_Language)";

        /// <summary>Tenant-local "today" as a DATE, dialect-specific. Matches VAS_138/295/297.</summary>
        private string TodayDateExpr()
        {
            return DB.IsPostgreSQL()
                ? "CAST(CURRENT_DATE AS DATE)"
                : "TRUNC(SYSDATE)";
        }

        /// <summary>
        /// The date RecentWindowDays ago, dialect-specific. Deliberately NOT
        /// "CAST(CURRENT_DATE AS DATE) - N": the framework rewrites "AS DATE" casts
        /// to "AS TIMESTAMP" for PostgreSQL (ADempiere date columns are uniformly
        /// TIMESTAMP), and Postgres has no "timestamp - integer" operator, only
        /// "date - integer" (same live SQLSTATE 42883 error as VAS_297). Bare
        /// CURRENT_DATE is not touched by that rewrite, so "CURRENT_DATE - N" stays
        /// valid date arithmetic.
        /// </summary>
        private string RecentSinceExpr()
        {
            return DB.IsPostgreSQL()
                ? "(CURRENT_DATE - " + RecentWindowDays + ")"
                : "(TRUNC(SYSDATE) - " + RecentWindowDays + ")";
        }

        /// <summary>Session language for the AD_Ref_List_Trl join, falling back to en_US.</summary>
        private string GetLanguage(Ctx ctx)
        {
            string language = ctx == null ? string.Empty : ctx.GetAD_Language();
            return string.IsNullOrEmpty(language) ? "en_US" : language;
        }

        /// <summary>
        /// Paged customer list: customers whose newest logged activity falls
        /// within the last 12 days, newest first.
        /// </summary>
        /// <param name="offset">Zero-based paging offset.</param>
        /// <param name="limit">Page size (7 for the widget, up to 25 for the full list).</param>
        /// <returns>JSON { items:[...], total, offset, limit } or { error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetRows(int offset = 0, int limit = WidgetPageSize)
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
                // Each customer's accessible requests, MRole'd on "r" first - a plain
                // SELECT with no ORDER BY anywhere in it, so AddAccessSQL's "insert
                // before the first ORDER BY" logic can only land in this WHERE clause.
                // (Calling AddAccessSQL directly on a query whose SELECT list already
                // has "ROW_NUMBER() OVER (... ORDER BY ...)" makes it splice the access
                // predicate INSIDE that window function's OVER(), which is invalid SQL -
                // confirmed via a live "syntax error at or near WHERE" from Postgres.)
                // The window function itself is layered on afterwards, over the
                // already-filtered result, in requestRankSql below.
                string requestScopeSql = @"
                    SELECT r.C_BPartner_ID AS C_BPartner_ID,
                           r.Summary AS Summary,
                           r.Created AS Created_At,
                           r.R_Request_ID AS R_Request_ID
                    FROM R_Request r
                    WHERE r.IsActive = 'Y'
                      AND r.AD_Client_ID = @Client_ID
                      AND r.C_BPartner_ID IS NOT NULL";
                requestScopeSql = MRole.GetDefault(ctx).AddAccessSQL(requestScopeSql, "r", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                string rankedRequestSql = @"
                    SELECT rs.C_BPartner_ID AS C_BPartner_ID,
                           rs.Summary AS Summary,
                           rs.Created_At AS Created_At,
                           ROW_NUMBER() OVER (
                               PARTITION BY rs.C_BPartner_ID
                               ORDER BY rs.Created_At DESC, rs.R_Request_ID DESC
                           ) AS RN
                    FROM (
                        " + requestScopeSql + @"
                    ) rs";

                string sql = @"
                    WITH RankedRequest AS (
                        " + rankedRequestSql + @"
                    )
                    SELECT bp.C_BPartner_ID AS Customer_Id,
                           bp.Name AS Customer_Name,
                           rr.Summary AS Summary,
                           rr.Created_At AS Created_At,
                           " + TodayDateExpr() + @" AS As_Of_Date,
                           bp.Rating AS Tier_Code,
                           COALESCE(RatingTrl.Name, RatingList.Name, N'') AS Tier_Name,
                           COUNT(1) OVER () AS Total_Rows
                    FROM RankedRequest rr
                    INNER JOIN C_BPartner bp ON (bp.C_BPartner_ID=rr.C_BPartner_ID AND bp.AD_Client_ID = @Client_ID AND bp.IsActive = 'Y' AND bp.IsCustomer = 'Y')" + RatingLabelJoins + @"
                    WHERE rr.RN = 1
                      AND rr.Created_At >= " + RecentSinceExpr() + @"
                    ORDER BY rr.Created_At DESC,
                             bp.Name ASC,
                             bp.C_BPartner_ID ASC
                    OFFSET " + offset + @" ROWS FETCH NEXT " + limit + @" ROWS ONLY";

                List<object> items = new List<object>();
                int total = 0;

                IDataReader dr = null;
                try
                {
                    dr = DB.ExecuteReader(sql, new SqlParameter[]
                    {
                        new SqlParameter("@Client_ID", ctx.GetAD_Client_ID()),
                        new SqlParameter("@AD_Language", SqlDbType.NVarChar) { Value = GetLanguage(ctx) }
                    });
                    while (dr != null && dr.Read())
                    {
                        total = Util.GetValueOfInt(dr["Total_Rows"]);

                        DateTime? createdAt = dr["Created_At"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr["Created_At"]);
                        DateTime? asOfDate = dr["As_Of_Date"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr["As_Of_Date"]);
                        // Days since the activity was logged. Computed in C# to
                        // avoid the SQL date subtraction returning an
                        // interval/TimeSpan (matches VAS_138/295/297).
                        int days = 0;
                        if (createdAt.HasValue && asOfDate.HasValue)
                        {
                            days = (int)(asOfDate.Value.Date - createdAt.Value.Date).TotalDays;
                            if (days < 0) { days = 0; }
                        }

                        string kind, text;
                        SplitSummary(Util.GetValueOfString(dr["Summary"]), out kind, out text);

                        string tierCode = Util.GetValueOfString(dr["Tier_Code"]);
                        string tierName = Util.GetValueOfString(dr["Tier_Name"]);

                        items.Add(new
                        {
                            customerId = Util.GetValueOfInt(dr["Customer_Id"]),
                            customerName = Util.GetValueOfString(dr["Customer_Name"]),
                            updateType = kind,
                            updateText = text,
                            days = days,
                            tier = MapTier(tierCode, tierName)
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
                Log.Log(Level.SEVERE, "VAS_298_RecentUpdatesWidget.GetRows", ex);
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Splits a logged R_Request Summary ("[Kind] free text", written by
        /// VAS_126.SaveActivityLog) into its kind and text. A Summary with no
        /// recognised "[Kind] " prefix - a genuine support ticket rather than a
        /// popup-logged interaction - reads as "Note", the popup's own default.
        /// </summary>
        private void SplitSummary(string summary, out string kind, out string text)
        {
            kind = "Note";
            text = summary ?? "";

            if (string.IsNullOrEmpty(summary) || summary.Length < 2 || summary[0] != '[') { return; }

            int close = summary.IndexOf(']');
            if (close <= 1) { return; }

            string candidate = summary.Substring(1, close - 1);
            if (candidate != "Call" && candidate != "Note" && candidate != "Meeting" && candidate != "Email") { return; }

            kind = candidate;
            text = summary.Substring(close + 1).TrimStart();
        }

        /// <summary>Resolves the display tier for one row. Identical rule to VAS_126.MapTier.</summary>
        private string MapTier(string ratingCode, string ratingName)
        {
            if (string.IsNullOrEmpty(ratingCode)) { return null; }

            string tier;
            if (CustomerTierByRating.TryGetValue(ratingCode, out tier)) { return tier; }

            int stars = CountRatingStars(ratingName);
            if (stars >= 3) { return TierPlatinum; }
            if (stars == 2) { return TierGold; }
            if (stars == 1) { return TierSilver; }

            if (string.IsNullOrWhiteSpace(ratingName)) { return null; }

            string trimmed = ratingName.Trim();
            return trimmed == "-" ? null : trimmed;
        }

        /// <summary>Counts star glyphs in a rating label. Identical rule to VAS_126.CountRatingStars.</summary>
        private static int CountRatingStars(string ratingName)
        {
            if (string.IsNullOrEmpty(ratingName)) { return 0; }

            int stars = 0;
            foreach (char character in ratingName)
            {
                if (character == '*' || character == '★' || character == '☆')
                {
                    stars++;
                }
            }

            return stars;
        }

        private void CloseReader(IDataReader reader)
        {
            if (reader == null) { return; }
            reader.Close();
            reader.Dispose();
        }
    }
}
