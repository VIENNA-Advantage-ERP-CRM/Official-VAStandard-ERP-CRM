/******************************************************
 * Module Name    : VAS
 * Purpose        : Customers module Onboarding Status widget endpoints
 * chronological  : Development
 * Created Date   : 2026-07-22
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
    /// Module Name : VAS_136_OnboardingStatusWidget
    /// Purpose     : 3x1 donut - share of active customers fully onboarded vs still
    ///               in progress. Profile completion is scored 20 points per profile
    ///               section that has data (see ProfileCompletionExpr) - there is no
    ///               VAS_ProfileCompletion column. Onboarded = 100, In progress =
    ///               &lt; 100. Legend rows drill into the filtered,
    ///               paged customer list. MRole (tenant + org + record access) is
    ///               applied to the single physical table C_BPartner.
    /// Chronological development:
    ///   VAI052      2026-07-22 Created
    /// </summary>
    public class VAS_136_OnboardingStatusWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_136_OnboardingStatusWidgetController).FullName);

        private const int MaxPageSize = 7;

        // Profile completion % (0..100). There is no VAS_ProfileCompletion column;
        // completion is scored 20 points per profile section that has data - the
        // customer itself, a location (C_BPartner_Location), a contact (AD_User), a
        // bank account (C_BP_BankAccount) and a customer accounting record
        // (FRPT_BP_Customer_Acct). Each DISTINCT 20 line yields one 20 only when the
        // section exists, so the sum is a multiple of 20 up to 100. Correlated to the
        // outer C_BPartner alias "bp"; portable across Oracle and PostgreSQL.
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

        /// <summary>
        /// Donut aggregate: total active customers and the onboarded / in-progress
        /// split (a single aggregate scan).
        /// </summary>
        /// <returns>JSON { totalCustomers, onboardedCount, inProgressCount, onboardedPercent, inProgressPercent } or { error }.</returns>
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
                string customerScopeSql = @"
                    SELECT bp.C_BPartner_ID AS C_BPartner_ID,
                           " + ProfileCompletionExpr + @" AS Onb_Progress
                    FROM C_BPartner bp
                    WHERE bp.IsActive = 'Y'
                      AND bp.IsCustomer = 'Y'
                      AND bp.AD_Client_ID = @Client_ID";
                customerScopeSql = MRole.GetDefault(ctx).AddAccessSQL(customerScopeSql, "bp", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                string sql = @"
                    WITH CustomerScope AS (
                        " + customerScopeSql + @"
                    )
                    SELECT COUNT(1) AS Total_Customers,
                           SUM(CASE WHEN cs.Onb_Progress >= 100 THEN 1 ELSE 0 END) AS Onboarded_Count,
                           SUM(CASE WHEN cs.Onb_Progress < 100 THEN 1 ELSE 0 END) AS In_Progress_Count
                    FROM CustomerScope cs";

                int total = 0, onboarded = 0, inProgress = 0;
                IDataReader dr = null;
                try
                {
                    dr = DB.ExecuteReader(sql, new SqlParameter[] { new SqlParameter("@Client_ID", ctx.GetAD_Client_ID()) });
                    if (dr != null && dr.Read())
                    {
                        total = Util.GetValueOfInt(dr["Total_Customers"]);
                        onboarded = Util.GetValueOfInt(dr["Onboarded_Count"]);
                        inProgress = Util.GetValueOfInt(dr["In_Progress_Count"]);
                    }
                }
                finally
                {
                    CloseReader(dr);
                }

                // Percentages computed here to keep the SQL dialect-neutral.
                double onboardedPercent = total > 0 ? Math.Round(100.0 * onboarded / total, 1) : 0;
                double inProgressPercent = total > 0 ? Math.Round(100.0 * inProgress / total, 1) : 0;

                var result = new
                {
                    totalCustomers = total,
                    onboardedCount = onboarded,
                    inProgressCount = inProgress,
                    onboardedPercent = onboardedPercent,
                    inProgressPercent = inProgressPercent
                };
                return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_136_OnboardingStatusWidget.GetSummary", ex);
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Paged drill-down list of customers in one onboarding state.
        /// </summary>
        /// <param name="status">'done' (onboarded) or 'incomplete' (in progress).</param>
        /// <param name="offset">Zero-based paging offset.</param>
        /// <param name="limit">Page size; clamped to 1..7.</param>
        /// <returns>JSON { items:[...], total, offset, limit } or { error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetList(string status, int offset = 0, int limit = MaxPageSize)
        {
            if (Session["ctx"] == null)
            {
                return Json(new { error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired" }, JsonRequestBehavior.AllowGet);
            }

            // Validate status against the allow-list.
            string state = (status ?? "").Trim().ToLowerInvariant();
            if (state != "done" && state != "incomplete")
            {
                return Json(new { error = "Invalid status" }, JsonRequestBehavior.AllowGet);
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

                string customerScopeSql = @"
                    SELECT bp.C_BPartner_ID AS Id,
                           bp.Value AS Customer_Code,
                           bp.Name AS Customer_Name,
                           bp.SalesRep_ID AS SalesRep_ID,
                           bp.Updated AS Updated_At,
                           " + ProfileCompletionExpr + @" AS Onb_Progress
                    FROM C_BPartner bp
                    WHERE bp.IsActive = 'Y'
                      AND bp.IsCustomer = 'Y'
                      AND bp.AD_Client_ID = @Client_ID";
                customerScopeSql = MRole.GetDefault(ctx).AddAccessSQL(customerScopeSql, "bp", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                // Order: in-progress lists least-onboarded first (progress ASC); the
                // done list ties on progress and falls through to name. CASE avoids
                // dialect-specific NULLS FIRST/LAST.
                string sql = @"
                    WITH RankedContacts AS (
                        " + rankedContactsSql + @"
                    ),
                    CustomerScope AS (
                        " + customerScopeSql + @"
                    )
                    SELECT cs.Id,
                           cs.Customer_Code,
                           cs.Customer_Name,
                           COALESCE(c.Contact_Name, N'') AS Contact,
                           COALESCE(rep.Name, N'') AS Rep,
                           cs.Onb_Progress,
                           cs.Updated_At,
                           COUNT(1) OVER () AS Total_Rows
                    FROM CustomerScope cs
                    LEFT OUTER JOIN RankedContacts c ON (c.C_BPartner_ID=cs.Id AND c.RN=1)
                    LEFT OUTER JOIN AD_User rep ON (rep.AD_User_ID=cs.SalesRep_ID AND rep.AD_Client_ID = @Client_ID AND rep.IsActive = 'Y')
                    WHERE (@Status = 'done' AND cs.Onb_Progress >= 100)
                       OR (@Status = 'incomplete' AND cs.Onb_Progress < 100)
                    ORDER BY CASE WHEN @Status = 'incomplete' THEN cs.Onb_Progress ELSE 0 END ASC,
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
                        new SqlParameter("@Status", SqlDbType.VarChar) { Value = state }
                    });
                    while (dr != null && dr.Read())
                    {
                        total = Util.GetValueOfInt(dr["Total_Rows"]);
                        int progress = Util.GetValueOfInt(dr["Onb_Progress"]);
                        items.Add(new
                        {
                            id = Util.GetValueOfInt(dr["Id"]),
                            customerCode = Util.GetValueOfString(dr["Customer_Code"]),
                            name = Util.GetValueOfString(dr["Customer_Name"]),
                            contact = Util.GetValueOfString(dr["Contact"]),
                            rep = Util.GetValueOfString(dr["Rep"]),
                            onbDone = progress >= 100,
                            onbProgress = progress,
                            onbStep = OnbStep(progress),
                            lastUpdated = FormatDate(Util.GetValueOfDateTime(dr["Updated_At"]))
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
                Log.Log(Level.SEVERE, "VAS_136_OnboardingStatusWidget.GetList", ex);
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>Presentation label derived from the verified percentage (not a stored stage).</summary>
        private string OnbStep(int progress)
        {
            if (progress >= 100) { return "Complete"; }
            if (progress >= 75) { return "Final review"; }
            if (progress >= 25) { return "Profile setup"; }
            return "Started";
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
