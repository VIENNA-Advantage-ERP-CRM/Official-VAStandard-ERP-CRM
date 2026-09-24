/******************************************************
 * Module Name    : VAS
 * Purpose        : Customers module Not Segmented Yet widget endpoints
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
    /// Module Name : VAS_302_NotSegmentedWidget
    /// Purpose     : 3x2 list - customers with no active membership in any
    ///               target list (C_TargetList), ranked by ARR desc so the
    ///               highest-value gaps surface first. Identical eligibility to
    ///               VAS_141's own "Segment ->" bulk-assign population
    ///               (GetUnsegmented), with tier added for the row display -
    ///               tier resolution (Rating -&gt; AD_Ref_List label, star-fold to
    ///               Platinum/Gold/Silver) is identical to VAS_126/120/298 so
    ///               the same customer reads the same tier everywhere. This is
    ///               the dedicated, actionable counterpart to the "Not
    ///               segmented" tile on VAS_137's Needs Attention widget.
    ///
    ///               The segment-assignment action itself (single row or bulk)
    ///               is NOT duplicated here - it reuses VAS_141's own
    ///               GetUnsegmented (segment selector source, for the bulk
    ///               checklist) and AssignSegment (the write) endpoints, the
    ///               same cross-widget endpoint-reuse pattern VAS_244 already
    ///               established against VAS_241/VAS_120 against VAS_126: one
    ///               write path for "assign a customer to a target list", not
    ///               two. MRole (tenant + org + record access) is applied to
    ///               the single physical table C_BPartner.
    /// Chronological development:
    ///   VAI052      2026-09-23 Created
    /// </summary>
    public class VAS_302_NotSegmentedWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_302_NotSegmentedWidgetController).FullName);

        private const int WidgetPageSize = 7;
        private const int MaxListPageSize = 25;

        // Display tiers. The client colours these three by name (violet / amber /
        // info); any other label falls back to its neutral tag. Matches VAS_126/298.
        private const string TierPlatinum = "Platinum";
        private const string TierGold = "Gold";
        private const string TierSilver = "Silver";

        /// <summary>
        /// Optional OVERRIDE of the tier label per Rating code. Intentionally
        /// EMPTY - the query resolves the tag from the tenant's own application
        /// dictionary. Same contract as VAS_120/126/298.
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

        /// <summary>Session language for the AD_Ref_List_Trl join, falling back to en_US.</summary>
        private string GetLanguage(Ctx ctx)
        {
            string language = ctx == null ? string.Empty : ctx.GetAD_Language();
            return string.IsNullOrEmpty(language) ? "en_US" : language;
        }

        /// <summary>
        /// Paged customer list: active customers with no active target-list
        /// membership, ranked by ARR (C_BPartner.ActualLifeTimeValue) desc.
        /// </summary>
        /// <param name="offset">Zero-based paging offset.</param>
        /// <param name="limit">Page size (7 for the widget, up to 25 for the full list).</param>
        /// <returns>JSON { items:[...], total, offset, limit, currency_* } or { error }.</returns>
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
                // Active customers with no active membership in any active target
                // list. Plain SELECT (no GROUP BY / ORDER BY) so AddAccessSQL's
                // predicate lands in the WHERE clause where alias "bp" is in scope.
                // Matches VAS_141.GetUnsegmented's population exactly.
                string customerScopeSql = @"
                    SELECT bp.C_BPartner_ID AS Id,
                           bp.Name AS Customer_Name,
                           bp.SalesRep_ID AS SalesRep_ID,
                           COALESCE(bp.ActualLifeTimeValue, 0) AS Arr,
                           bp.Rating AS Tier_Code,
                           COALESCE(RatingTrl.Name, RatingList.Name, N'') AS Tier_Name
                    FROM C_BPartner bp" + RatingLabelJoins + @"
                    WHERE bp.IsActive = 'Y'
                      AND bp.IsCustomer = 'Y'
                      AND bp.AD_Client_ID = @Client_ID
                      AND NOT EXISTS (
                          SELECT 1 FROM C_TargetList tl
                          INNER JOIN C_MasterTargetList mtl ON (mtl.C_MasterTargetList_ID=tl.C_MasterTargetList_ID AND mtl.AD_Client_ID=tl.AD_Client_ID AND mtl.IsActive = 'Y')
                          WHERE tl.C_BPartner_ID=bp.C_BPartner_ID
                            AND tl.IsActive = 'Y'
                            AND tl.AD_Client_ID = @Client_ID
                      )";
                customerScopeSql = MRole.GetDefault(ctx).AddAccessSQL(customerScopeSql, "bp", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                string sql = @"
                    WITH CustomerScope AS (
                        " + customerScopeSql + @"
                    )
                    SELECT cs.Id,
                           cs.Customer_Name,
                           COALESCE(rep.Name, N'') AS Rep_Name,
                           cs.Arr,
                           cs.Tier_Code,
                           cs.Tier_Name,
                           COUNT(1) OVER () AS Total_Rows
                    FROM CustomerScope cs
                    LEFT OUTER JOIN AD_User rep ON (rep.AD_User_ID=cs.SalesRep_ID AND rep.AD_Client_ID = @Client_ID AND rep.IsActive = 'Y')
                    ORDER BY cs.Arr DESC,
                             cs.Customer_Name ASC,
                             cs.Id ASC
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
                        string tierCode = Util.GetValueOfString(dr["Tier_Code"]);
                        string tierName = Util.GetValueOfString(dr["Tier_Name"]);
                        items.Add(new
                        {
                            customerId = Util.GetValueOfInt(dr["Id"]),
                            customerName = Util.GetValueOfString(dr["Customer_Name"]),
                            ownerName = Util.GetValueOfString(dr["Rep_Name"]),
                            arr = Util.GetValueOfDecimal(dr["Arr"]),
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
                Log.Log(Level.SEVERE, "VAS_302_NotSegmentedWidget.GetRows", ex);
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
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
