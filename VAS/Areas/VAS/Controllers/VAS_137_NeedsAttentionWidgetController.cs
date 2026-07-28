/******************************************************
 * Module Name    : VAS
 * Purpose        : Customers module Needs Attention widget endpoints
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
    /// Module Name : VAS_137_NeedsAttentionWidget
    /// Purpose     : 3x1 entry-tiles - four distinct-customer exception counts and
    ///               their paged drill-downs:
    ///                 overdue      - customers with an overdue open sales receivable
    ///                                (C_InvoicePaySchedule, same rule as VAS_124).
    ///                 unresponded  - customers with an active, unresolved, open
    ///                                R_Request (CloseDate NULL, not resolved/processed).
    ///                 unsegmented  - active customers with C_BP_Group_ID IS NULL.
    ///                 onbIncomplete- active customers whose profile completion &lt; 100,
    ///                                scored 20 points per profile section (same rule
    ///                                as VAS_136; the C_Project onboarding columns
    ///                                VA136_OnboardingType / VAS_ActualEndDate /
    ///                                IsOpportunity are not present in this schema).
    ///               MRole (tenant + org + record access) is applied to each query's
    ///               main physical table.
    /// Chronological development:
    ///   VAI052      2026-07-22 Created
    /// </summary>
    public class VAS_137_NeedsAttentionWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_137_NeedsAttentionWidgetController).FullName);

        private const int MaxPageSize = 7;

        // Profile completion % (0..100): 20 points per profile section that has data.
        // Correlated to the outer C_BPartner alias "bp". Same expression as VAS_136.
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

        /// <summary>Overdue = due strictly before today (dialect-correct truncation).</summary>
        private string OverdueDateCondition()
        {
            return DB.IsPostgreSQL()
                ? " AND CAST(ips.DueDate AS DATE) < CAST(CURRENT_DATE AS DATE)"
                : " AND TRUNC(ips.DueDate) < TRUNC(SYSDATE)";
        }

        /// <summary>
        /// Tile aggregates: the four distinct-customer exception counts.
        /// </summary>
        /// <returns>JSON { overdue, unresponded, unsegmented, onbIncomplete } or { error }.</returns>
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
                var result = new
                {
                    overdue = CountOverdue(ctx),
                    unresponded = CountUnresponded(ctx),
                    unsegmented = CountUnsegmented(ctx),
                    onbIncomplete = CountOnbIncomplete(ctx)
                };
                return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_137_NeedsAttentionWidget.GetSummary", ex);
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        private int CountOverdue(Ctx ctx)
        {
            string sql = @"
                SELECT COUNT(DISTINCT i.C_BPartner_ID) AS Cnt
                FROM C_InvoicePaySchedule ips
                INNER JOIN C_Invoice i ON (ips.C_Invoice_ID=i.C_Invoice_ID AND i.IsActive = 'Y')
                INNER JOIN C_BPartner bp ON (bp.C_BPartner_ID=i.C_BPartner_ID AND bp.AD_Client_ID=i.AD_Client_ID AND bp.IsActive = 'Y' AND bp.IsCustomer = 'Y')
                WHERE ips.IsActive = 'Y'
                  AND ips.VA009_IsPaid = 'N'" + OverdueDateCondition() + @"
                  AND i.DocStatus IN ('CO', 'CL')
                  AND i.IsSOTrx = 'Y'
                  AND i.AD_Client_ID = @Client_ID";
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "ips", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            return ReadScalarInt(ctx, sql);
        }

        private int CountUnresponded(Ctx ctx)
        {
            string sql = @"
                SELECT COUNT(DISTINCT r.C_BPartner_ID) AS Cnt
                FROM R_Request r
                INNER JOIN C_BPartner bp ON (bp.C_BPartner_ID=r.C_BPartner_ID AND bp.AD_Client_ID=r.AD_Client_ID AND bp.IsActive = 'Y' AND bp.IsCustomer = 'Y')
                WHERE r.IsActive = 'Y'
                  AND r.AD_Client_ID = @Client_ID
                  AND r.C_BPartner_ID IS NOT NULL
                  AND r.CloseDate IS NULL
                  AND COALESCE(r.VAS_IsResolved, 'N') = 'N'
                  AND COALESCE(r.Processed, 'N') = 'N'";
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "r", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            return ReadScalarInt(ctx, sql);
        }

        private int CountUnsegmented(Ctx ctx)
        {
            string sql = @"
                SELECT COUNT(DISTINCT bp.C_BPartner_ID) AS Cnt
                FROM C_BPartner bp
                WHERE bp.IsActive = 'Y'
                  AND bp.IsCustomer = 'Y'
                  AND bp.AD_Client_ID = @Client_ID
                  AND bp.C_BP_Group_ID IS NULL";
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "bp", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            return ReadScalarInt(ctx, sql);
        }

        private int CountOnbIncomplete(Ctx ctx)
        {
            string sql = @"
                SELECT SUM(CASE WHEN " + ProfileCompletionExpr + @" < 100 THEN 1 ELSE 0 END) AS Cnt
                FROM C_BPartner bp
                WHERE bp.IsActive = 'Y'
                  AND bp.IsCustomer = 'Y'
                  AND bp.AD_Client_ID = @Client_ID";
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "bp", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            return ReadScalarInt(ctx, sql);
        }

        /// <summary>
        /// Paged drill-down for one tile. Flag is validated against the allow-list.
        /// </summary>
        /// <param name="flag">overdue / unresponded / unsegmented / onbIncomplete.</param>
        /// <param name="offset">Zero-based paging offset.</param>
        /// <param name="limit">Page size; clamped to 1..7.</param>
        /// <returns>JSON { flag, items:[...], total, ...currency } or { error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetList(string flag, int offset = 0, int limit = MaxPageSize)
        {
            if (Session["ctx"] == null)
            {
                return Json(new { error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired" }, JsonRequestBehavior.AllowGet);
            }

            string key = (flag ?? "").Trim();
            if (key != "overdue" && key != "unresponded" && key != "unsegmented" && key != "onbIncomplete")
            {
                return Json(new { error = "Invalid flag" }, JsonRequestBehavior.AllowGet);
            }

            if (offset < 0) { offset = 0; }
            if (limit <= 0 || limit > MaxPageSize) { limit = MaxPageSize; }

            Ctx ctx = Session["ctx"] as Ctx;

            try
            {
                switch (key)
                {
                    case "overdue": return OverdueList(ctx, offset, limit);
                    case "unresponded": return UnrespondedList(ctx, offset, limit);
                    case "unsegmented": return UnsegmentedList(ctx, offset, limit);
                    default: return OnbIncompleteList(ctx, offset, limit);
                }
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_137_NeedsAttentionWidget.GetList." + key, ex);
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        private JsonResult OverdueList(Ctx ctx, int offset, int limit)
        {
            SchemaCurrency currency = GetSchemaCurrency(ctx);

            string overdueSql = @"
                SELECT i.C_BPartner_ID AS Bp_Id,
                       COUNT(DISTINCT i.C_Invoice_ID) AS Inv_Count,
                       COALESCE(SUM(CASE
                           WHEN i.IsReturnTrx = 'N' THEN CurrencyConvert(COALESCE(ips.DueAmt, 0), i.C_Currency_ID, @Ccy_ID, i.DateAcct, i.C_ConversionType_ID, i.AD_Client_ID, i.AD_Org_ID)
                           WHEN i.IsReturnTrx = 'Y' THEN -CurrencyConvert(COALESCE(ips.DueAmt, 0), i.C_Currency_ID, @Ccy_ID, i.DateAcct, i.C_ConversionType_ID, i.AD_Client_ID, i.AD_Org_ID)
                           ELSE 0 END), 0) AS Amt,
                       MIN(ips.DueDate) AS Oldest_Due
                FROM C_InvoicePaySchedule ips
                INNER JOIN C_Invoice i ON (ips.C_Invoice_ID=i.C_Invoice_ID AND i.IsActive = 'Y')
                INNER JOIN C_BPartner bp ON (bp.C_BPartner_ID=i.C_BPartner_ID AND bp.AD_Client_ID=i.AD_Client_ID AND bp.IsActive = 'Y' AND bp.IsCustomer = 'Y')
                WHERE ips.IsActive = 'Y'
                  AND ips.VA009_IsPaid = 'N'" + OverdueDateCondition() + @"
                  AND i.DocStatus IN ('CO', 'CL')
                  AND i.IsSOTrx = 'Y'
                  AND i.AD_Client_ID = @Client_ID";
            overdueSql = MRole.GetDefault(ctx).AddAccessSQL(overdueSql, "ips", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            overdueSql += @"
                GROUP BY i.C_BPartner_ID";

            string sql = @"
                WITH Overdue AS (
                    " + overdueSql + @"
                )
                SELECT o.Bp_Id AS Id,
                       bpn.Name AS Name,
                       o.Inv_Count AS Inv_Count,
                       o.Amt AS Amt,
                       o.Oldest_Due AS Oldest_Due,
                       COUNT(1) OVER () AS Total
                FROM Overdue o
                INNER JOIN C_BPartner bpn ON (bpn.C_BPartner_ID=o.Bp_Id AND bpn.AD_Client_ID = @Client_ID)
                ORDER BY o.Oldest_Due ASC, o.Amt DESC, bpn.Name ASC
                OFFSET " + offset + @" ROWS FETCH NEXT " + limit + @" ROWS ONLY";

            List<object> items = new List<object>();
            int total = 0;
            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, new SqlParameter[]
                {
                    new SqlParameter("@Client_ID", ctx.GetAD_Client_ID()),
                    new SqlParameter("@Ccy_ID", currency.CurrencyId)
                });
                while (dr != null && dr.Read())
                {
                    total = Util.GetValueOfInt(dr["Total"]);
                    DateTime? oldest = Util.GetValueOfDateTime(dr["Oldest_Due"]);
                    int maxDays = oldest.HasValue ? Math.Max(0, (DateTime.Today - oldest.Value.Date).Days) : 0;
                    items.Add(new
                    {
                        id = Util.GetValueOfInt(dr["Id"]),
                        name = Util.GetValueOfString(dr["Name"]),
                        invoiceCount = Util.GetValueOfInt(dr["Inv_Count"]),
                        amount = Math.Round(Util.GetValueOfDecimal(dr["Amt"]), currency.StdPrecision),
                        maxDays = maxDays
                    });
                }
            }
            finally
            {
                CloseReader(dr);
            }

            var result = new
            {
                flag = "overdue",
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

        private JsonResult UnrespondedList(Ctx ctx, int offset, int limit)
        {
            string requestSql = @"
                SELECT r.C_BPartner_ID AS Bp_Id,
                       COUNT(r.R_Request_ID) AS Req_Count,
                       MIN(COALESCE(r.DateLastAction, r.Created)) AS Oldest_Waiting
                FROM R_Request r
                INNER JOIN C_BPartner bp ON (bp.C_BPartner_ID=r.C_BPartner_ID AND bp.AD_Client_ID=r.AD_Client_ID AND bp.IsActive = 'Y' AND bp.IsCustomer = 'Y')
                WHERE r.IsActive = 'Y'
                  AND r.AD_Client_ID = @Client_ID
                  AND r.C_BPartner_ID IS NOT NULL
                  AND r.CloseDate IS NULL
                  AND COALESCE(r.VAS_IsResolved, 'N') = 'N'
                  AND COALESCE(r.Processed, 'N') = 'N'";
            requestSql = MRole.GetDefault(ctx).AddAccessSQL(requestSql, "r", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            requestSql += @"
                GROUP BY r.C_BPartner_ID";

            string sql = @"
                WITH Unresponded AS (
                    " + requestSql + @"
                )
                SELECT u.Bp_Id AS Id,
                       bpn.Name AS Name,
                       u.Req_Count AS Req_Count,
                       u.Oldest_Waiting AS Oldest_Waiting,
                       COUNT(1) OVER () AS Total
                FROM Unresponded u
                INNER JOIN C_BPartner bpn ON (bpn.C_BPartner_ID=u.Bp_Id AND bpn.AD_Client_ID = @Client_ID)
                ORDER BY u.Oldest_Waiting ASC, u.Req_Count DESC, bpn.Name ASC
                OFFSET " + offset + @" ROWS FETCH NEXT " + limit + @" ROWS ONLY";

            List<object> items = new List<object>();
            int total = 0;
            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, new SqlParameter[] { new SqlParameter("@Client_ID", ctx.GetAD_Client_ID()) });
                while (dr != null && dr.Read())
                {
                    total = Util.GetValueOfInt(dr["Total"]);
                    DateTime? oldest = Util.GetValueOfDateTime(dr["Oldest_Waiting"]);
                    int maxWait = oldest.HasValue ? Math.Max(0, (DateTime.Today - oldest.Value.Date).Days) : 0;
                    items.Add(new
                    {
                        id = Util.GetValueOfInt(dr["Id"]),
                        name = Util.GetValueOfString(dr["Name"]),
                        requestCount = Util.GetValueOfInt(dr["Req_Count"]),
                        maxWaitDays = maxWait
                    });
                }
            }
            finally
            {
                CloseReader(dr);
            }

            return Json(JsonConvert.SerializeObject(new { flag = "unresponded", items = items, total = total, offset = offset, limit = limit }), JsonRequestBehavior.AllowGet);
        }

        private JsonResult UnsegmentedList(Ctx ctx, int offset, int limit)
        {
            string sql = @"
                SELECT bp.C_BPartner_ID AS Id,
                       bp.Name AS Name,
                       bp.Value AS Code,
                       COALESCE(rep.Name, N'') AS Rep,
                       COUNT(1) OVER () AS Total
                FROM C_BPartner bp
                LEFT OUTER JOIN AD_User rep ON (rep.AD_User_ID=bp.SalesRep_ID AND rep.AD_Client_ID=bp.AD_Client_ID AND rep.IsActive = 'Y')
                WHERE bp.IsActive = 'Y'
                  AND bp.IsCustomer = 'Y'
                  AND bp.AD_Client_ID = @Client_ID
                  AND bp.C_BP_Group_ID IS NULL";
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "bp", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            sql += @"
                ORDER BY bp.Updated DESC, bp.Name ASC
                OFFSET " + offset + @" ROWS FETCH NEXT " + limit + @" ROWS ONLY";

            List<object> items = new List<object>();
            int total = 0;
            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, new SqlParameter[] { new SqlParameter("@Client_ID", ctx.GetAD_Client_ID()) });
                while (dr != null && dr.Read())
                {
                    total = Util.GetValueOfInt(dr["Total"]);
                    items.Add(new
                    {
                        id = Util.GetValueOfInt(dr["Id"]),
                        name = Util.GetValueOfString(dr["Name"]),
                        code = Util.GetValueOfString(dr["Code"]),
                        rep = Util.GetValueOfString(dr["Rep"])
                    });
                }
            }
            finally
            {
                CloseReader(dr);
            }

            return Json(JsonConvert.SerializeObject(new { flag = "unsegmented", items = items, total = total, offset = offset, limit = limit }), JsonRequestBehavior.AllowGet);
        }

        private JsonResult OnbIncompleteList(Ctx ctx, int offset, int limit)
        {
            string scopeSql = @"
                SELECT bp.C_BPartner_ID AS Id,
                       bp.Name AS Name,
                       bp.Updated AS Updated_At,
                       " + ProfileCompletionExpr + @" AS Progress
                FROM C_BPartner bp
                WHERE bp.IsActive = 'Y'
                  AND bp.IsCustomer = 'Y'
                  AND bp.AD_Client_ID = @Client_ID";
            scopeSql = MRole.GetDefault(ctx).AddAccessSQL(scopeSql, "bp", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string sql = @"
                WITH Scope AS (
                    " + scopeSql + @"
                )
                SELECT s.Id, s.Name, s.Progress, COUNT(1) OVER () AS Total
                FROM Scope s
                WHERE s.Progress < 100
                ORDER BY s.Progress ASC, s.Name ASC, s.Id ASC
                OFFSET " + offset + @" ROWS FETCH NEXT " + limit + @" ROWS ONLY";

            List<object> items = new List<object>();
            int total = 0;
            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, new SqlParameter[] { new SqlParameter("@Client_ID", ctx.GetAD_Client_ID()) });
                while (dr != null && dr.Read())
                {
                    total = Util.GetValueOfInt(dr["Total"]);
                    int progress = Util.GetValueOfInt(dr["Progress"]);
                    items.Add(new
                    {
                        id = Util.GetValueOfInt(dr["Id"]),
                        name = Util.GetValueOfString(dr["Name"]),
                        progress = progress,
                        onbStep = OnbStep(progress)
                    });
                }
            }
            finally
            {
                CloseReader(dr);
            }

            return Json(JsonConvert.SerializeObject(new { flag = "onbIncomplete", items = items, total = total, offset = offset, limit = limit }), JsonRequestBehavior.AllowGet);
        }

        private string OnbStep(int progress)
        {
            if (progress >= 100) { return "Complete"; }
            if (progress >= 75) { return "Final review"; }
            if (progress >= 25) { return "Profile setup"; }
            return "Started";
        }

        private int ReadScalarInt(Ctx ctx, string sql)
        {
            int value = 0;
            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, new SqlParameter[] { new SqlParameter("@Client_ID", ctx.GetAD_Client_ID()) });
                if (dr != null && dr.Read())
                {
                    value = Util.GetValueOfInt(dr["Cnt"]);
                }
            }
            finally
            {
                CloseReader(dr);
            }
            return value;
        }

        private SchemaCurrency GetSchemaCurrency(Ctx ctx)
        {
            SchemaCurrency currency = new SchemaCurrency { StdPrecision = 2 };
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
