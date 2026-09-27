/******************************************************
 * Module Name    : VAS
 * Purpose        : Customers module Recent New Customers widget endpoints
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
    /// Module Name : VAS_297_RecentNewCustomersWidget
    /// Purpose     : 3x3 list - customers created in the last 45 days
    ///               (C_BPartner.Created), newest first, each showing whether
    ///               onboarding is complete or its current step. Onboarding
    ///               progress follows VAS_136's Profile Completion scoring -
    ///               20 points per profile section that has data (customer
    ///               itself, a location, a contact, a bank account, a customer
    ///               accounting record) - there is no VAS_ProfileCompletion
    ///               column. MRole (tenant + org + record access) is applied to
    ///               the single physical table C_BPartner.
    /// Chronological development:
    ///   VAI052      2026-09-23 Created
    /// </summary>
    public class VAS_297_RecentNewCustomersWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_297_RecentNewCustomersWidgetController).FullName);

        private const int WidgetPageSize = 7;
        private const int MaxListPageSize = 25;

        /// <summary>A customer is "new" within this many days of C_BPartner.Created.</summary>
        private const int NewCustomerWindowDays = 45;

        // Profile completion % (0..100). Matches VAS_136.ProfileCompletionExpr
        // exactly - correlated to the outer C_BPartner alias "bp"; portable
        // across Oracle and PostgreSQL.
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
        /// Tenant-local "today" as a DATE, dialect-specific. Matches VAS_138's
        /// TodayDateExpr(): returned as its own column so "days since joined" is
        /// computed in C# (DateTime - DateTime), since the SQL date subtraction
        /// yields an interval the provider maps to a System.TimeSpan.
        /// </summary>
        private string TodayDateExpr()
        {
            return DB.IsPostgreSQL()
                ? "CAST(CURRENT_DATE AS DATE)"
                : "TRUNC(SYSDATE)";
        }

        /// <summary>
        /// The date NewCustomerWindowDays ago, dialect-specific. Deliberately NOT
        /// "CAST(CURRENT_DATE AS DATE) - N": the framework rewrites "AS DATE" casts
        /// to "AS TIMESTAMP" for PostgreSQL (ADempiere date columns are uniformly
        /// TIMESTAMP), and Postgres has no "timestamp - integer" operator, only
        /// "date - integer" - confirmed via the live SQLSTATE 42883 error this threw.
        /// Bare CURRENT_DATE is not touched by that rewrite, so "CURRENT_DATE - N"
        /// stays valid date arithmetic.
        /// </summary>
        private string NewSinceExpr()
        {
            return DB.IsPostgreSQL()
                ? "(CURRENT_DATE - " + NewCustomerWindowDays + ")"
                : "(TRUNC(SYSDATE) - " + NewCustomerWindowDays + ")";
        }

        /// <summary>
        /// Paged customer list: customers created in the last 45 days, newest
        /// first, each with its onboarding completion state.
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
                // Customers created within the new-customer window. Plain SELECT
                // (no GROUP BY / ORDER BY) so AddAccessSQL's predicate lands in the
                // WHERE clause where alias "bp" is in scope.
                string customerScopeSql = @"
                    SELECT bp.C_BPartner_ID AS Id,
                           bp.Name AS Customer_Name,
                           bp.SalesRep_ID AS SalesRep_ID,
                           bp.Created AS Created_At,
                           " + TodayDateExpr() + @" AS As_Of_Date,
                           " + ProfileCompletionExpr + @" AS Onb_Progress
                    FROM C_BPartner bp
                    WHERE bp.IsActive = 'Y'
                      AND bp.IsCustomer = 'Y'
                      AND bp.AD_Client_ID = @Client_ID
                      AND bp.Created >= " + NewSinceExpr();
                customerScopeSql = MRole.GetDefault(ctx).AddAccessSQL(customerScopeSql, "bp", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                string sql = @"
                    WITH CustomerScope AS (
                        " + customerScopeSql + @"
                    )
                    SELECT cs.Id,
                           cs.Customer_Name,
                           COALESCE(rep.Name, N'') AS Rep_Name,
                           cs.Onb_Progress,
                           cs.Created_At,
                           cs.As_Of_Date,
                           COUNT(1) OVER () AS Total_Rows
                    FROM CustomerScope cs
                    LEFT OUTER JOIN AD_User rep ON (rep.AD_User_ID=cs.SalesRep_ID AND rep.AD_Client_ID = @Client_ID AND rep.IsActive = 'Y')
                    ORDER BY cs.Created_At DESC,
                             cs.Customer_Name ASC,
                             cs.Id ASC
                    OFFSET " + offset + @" ROWS FETCH NEXT " + limit + @" ROWS ONLY";

                List<object> items = new List<object>();
                int total = 0;

                IDataReader dr = null;
                try
                {
                    dr = DB.ExecuteReader(sql, new SqlParameter[] { new SqlParameter("@Client_ID", ctx.GetAD_Client_ID()) });
                    while (dr != null && dr.Read())
                    {
                        total = Util.GetValueOfInt(dr["Total_Rows"]);

                        DateTime? createdAt = dr["Created_At"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr["Created_At"]);
                        DateTime? asOfDate = dr["As_Of_Date"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr["As_Of_Date"]);
                        // Days since the customer joined. Computed in C# to avoid
                        // the SQL date subtraction returning an interval/TimeSpan
                        // (matches VAS_138/295's overdueDays/unrespDays).
                        int newDays = 0;
                        if (createdAt.HasValue && asOfDate.HasValue)
                        {
                            newDays = (int)(asOfDate.Value.Date - createdAt.Value.Date).TotalDays;
                            if (newDays < 0) { newDays = 0; }
                        }

                        int progress = Util.GetValueOfInt(dr["Onb_Progress"]);
                        items.Add(new
                        {
                            customerId = Util.GetValueOfInt(dr["Id"]),
                            customerName = Util.GetValueOfString(dr["Customer_Name"]),
                            ownerName = Util.GetValueOfString(dr["Rep_Name"]),
                            newDays = newDays,
                            onbDone = progress >= 100,
                            onbProgress = progress,
                            onbStep = OnbStep(progress)
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
                Log.Log(Level.SEVERE, "VAS_297_RecentNewCustomersWidget.GetRows", ex);
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>Presentation label derived from the verified percentage (not a
        /// stored stage). Matches VAS_136.OnbStep().</summary>
        private string OnbStep(int progress)
        {
            if (progress >= 100) { return "Complete"; }
            if (progress >= 75) { return "Final review"; }
            if (progress >= 25) { return "Profile setup"; }
            return "Started";
        }

        private void CloseReader(IDataReader reader)
        {
            if (reader == null) { return; }
            reader.Close();
            reader.Dispose();
        }
    }
}
