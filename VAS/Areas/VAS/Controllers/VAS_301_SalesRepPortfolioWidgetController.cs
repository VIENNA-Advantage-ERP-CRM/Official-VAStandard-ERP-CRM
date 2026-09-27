/******************************************************
 * Module Name    : VAS
 * Purpose        : Customers module Sales Rep Portfolio widget endpoints
 * chronological  : Development
 * Created Date   : 2026-09-23
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
    /// Module Name : VAS_301_SalesRepPortfolioWidget
    /// Purpose     : 3x2 leaderboard - sales reps ranked by owned ARR (the same
    ///               C_BPartner.ActualLifeTimeValue figure VAS_126/138/295-300
    ///               label "ARR"), each with its account count, an "open"
    ///               account count (at least one open VAS_Opportunity, the
    ///               identical stage rule VAS_125/139/299 use) and an
    ///               "at-risk" account count (an overdue receivable - VAS_138's
    ///               rule - OR an open support ticket - VAS_126's rule). A rep
    ///               reads "Healthy" only when its risk count is zero. MRole
    ///               (tenant + org + record access) is applied independently to
    ///               each physical table this reads - C_BPartner (the owned
    ///               book), VAS_Opportunity, C_InvoicePaySchedule and R_Request -
    ///               never to AD_User (a reference lookup, not record data) or a
    ///               CTE alias.
    /// Chronological development:
    ///   VAI052      2026-09-23 Created
    /// </summary>
    public class VAS_301_SalesRepPortfolioWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_301_SalesRepPortfolioWidgetController).FullName);

        private const int WidgetPageSize = 7;
        private const int MaxListPageSize = 25;

        private static string SchemaCurrencySql(string clientIdSql)
        {
            return @"
            SELECT ci.AD_Client_ID AS AD_Client_ID,
                   cs.C_Currency_ID AS Acct_Currency_ID,
                   cur.StdPrecision AS Std_Precision,
                   cur.ISO_Code AS ISO_Code,
                   CASE WHEN cur.CurSymbol IS NOT NULL THEN cur.CurSymbol ELSE cur.ISO_Code END AS Cur_Symbol
            FROM AD_ClientInfo ci
            INNER JOIN C_AcctSchema cs ON (cs.C_AcctSchema_ID=ci.C_AcctSchema1_ID AND cs.IsActive = 'Y')
            INNER JOIN C_Currency cur ON (cur.C_Currency_ID=cs.C_Currency_ID AND cur.IsActive = 'Y')
            WHERE ci.IsActive = 'Y'
              AND ci.AD_Client_ID = " + clientIdSql;
        }

        private static string IntSql(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Open (not yet converted) VAS_Opportunity stages. Matches VAS_125/139/299.</summary>
        private const string OpenStagePredicate =
            "(o.VAS_OppStage IN ('10','11','12','13','15') OR o.VAS_OppStage IS NULL)";

        /// <summary>Overdue = due strictly before today (dialect-correct truncation). Matches VAS_138/299.</summary>
        private string OverdueDateCondition()
        {
            return DB.IsPostgreSQL()
                ? " AND CAST(ips.DueDate AS DATE) < CAST(CURRENT_DATE AS DATE)"
                : " AND TRUNC(ips.DueDate) < TRUNC(SYSDATE)";
        }

        private string OverdueOpenAmtExpr()
        {
            return "(COALESCE(ips.DueAmt, 0) - COALESCE(ips.VA009_PaidAmntInvce, 0) - COALESCE(ips.VA009_Variance, 0))";
        }

        private class Literals
        {
            public string ClientId;
        }

        private static Literals BuildLiterals(Ctx ctx)
        {
            return new Literals { ClientId = IntSql(ctx.GetAD_Client_ID()) };
        }

        /// <summary>Active customers with an owner, plus their ARR. Alias "bp" is the MRole physical table.</summary>
        private string CustomerBaseSql(Literals lit)
        {
            return @"
                SELECT bp.C_BPartner_ID AS Bp_Id,
                       bp.SalesRep_ID AS Rep_Id,
                       COALESCE(bp.ActualLifeTimeValue, 0) AS Arr
                FROM C_BPartner bp
                WHERE bp.IsActive = 'Y'
                  AND bp.IsCustomer = 'Y'
                  AND bp.AD_Client_ID = " + lit.ClientId + @"
                  AND bp.SalesRep_ID IS NOT NULL";
        }

        /// <summary>Customers with at least one open opportunity. Alias "o" is the MRole physical table.</summary>
        private string OpenOppSql(Literals lit)
        {
            return @"
                SELECT DISTINCT o.Ref_BPartner_ID AS Bp_Id
                FROM VAS_Opportunity o
                WHERE o.IsActive = 'Y'
                  AND o.AD_Client_ID = " + lit.ClientId + @"
                  AND " + OpenStagePredicate + @"
                  AND o.Ref_Order_ID IS NULL
                  AND o.C_Order_ID IS NULL
                  AND o.Ref_BPartner_ID IS NOT NULL";
        }

        /// <summary>Customers with an overdue receivable. Alias "ips" is the MRole physical table. Matches VAS_138/299.</summary>
        private string OverdueCustomerSql(Literals lit)
        {
            return @"
                SELECT DISTINCT i.C_BPartner_ID AS Bp_Id
                FROM C_InvoicePaySchedule ips
                INNER JOIN C_Invoice i ON (ips.C_Invoice_ID=i.C_Invoice_ID AND i.IsActive = 'Y')
                WHERE ips.IsActive = 'Y'
                  AND ips.VA009_IsPaid = 'N'" + OverdueDateCondition() + @"
                  AND i.DocStatus IN ('CO', 'CL')
                  AND i.IsSOTrx = 'Y'
                  AND i.IsReturnTrx = 'N'
                  AND i.AD_Client_ID = " + lit.ClientId + @"
                  AND ROUND(" + OverdueOpenAmtExpr() + @", 2) > 0";
        }

        /// <summary>Customers with an open support ticket. Alias "r" is the MRole physical table. Matches VAS_126.CountOpenTickets.</summary>
        private string OpenTicketCustomerSql(Literals lit)
        {
            return @"
                SELECT DISTINCT r.C_BPartner_ID AS Bp_Id
                FROM R_Request r
                INNER JOIN R_Status s ON (s.R_Status_ID=r.R_Status_ID AND s.AD_Client_ID=r.AD_Client_ID AND s.IsActive = 'Y')
                WHERE r.IsActive = 'Y'
                  AND r.AD_Client_ID = " + lit.ClientId + @"
                  AND r.C_BPartner_ID IS NOT NULL
                  AND s.IsOpen = 'Y'
                  AND COALESCE(s.IsClosed, 'N') = 'N'";
        }

        /// <summary>
        /// Reps ranked by owned ARR desc, each with account/open/at-risk counts
        /// and a bar percentage relative to the top rep.
        /// </summary>
        /// <param name="offset">Zero-based paging offset.</param>
        /// <param name="limit">Page size (7 for the widget, up to 25 for the full list).</param>
        /// <returns>JSON { items:[...], total, totalReps, totalArr, offset, limit, currency_* } or { error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetLeaderboard(int offset = 0, int limit = WidgetPageSize)
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
                Literals lit = BuildLiterals(ctx);

                string currencySymbol = "", isoCode = "";
                int stdPrecision = 2;
                IDataReader cdr = null;
                try
                {
                    cdr = DB.ExecuteReader(SchemaCurrencySql("@Client_ID"), new SqlParameter[] { new SqlParameter("@Client_ID", ctx.GetAD_Client_ID()) });
                    if (cdr != null && cdr.Read())
                    {
                        currencySymbol = Util.GetValueOfString(cdr["Cur_Symbol"]);
                        isoCode = Util.GetValueOfString(cdr["ISO_Code"]);
                        if (cdr["Std_Precision"] != null && cdr["Std_Precision"] != DBNull.Value)
                        {
                            stdPrecision = Util.GetValueOfInt(cdr["Std_Precision"]);
                        }
                    }
                }
                finally { CloseReader(cdr); }

                string customerBase = MRole.GetDefault(ctx).AddAccessSQL(CustomerBaseSql(lit), "bp", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                string openOpp = MRole.GetDefault(ctx).AddAccessSQL(OpenOppSql(lit), "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                string overdueCust = MRole.GetDefault(ctx).AddAccessSQL(OverdueCustomerSql(lit), "ips", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                string openTicketCust = MRole.GetDefault(ctx).AddAccessSQL(OpenTicketCustomerSql(lit), "r", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                string sql = @"
                    WITH schema_currency AS (
                        " + SchemaCurrencySql(lit.ClientId) + @"
                    ),
                    customer_base AS (
                        " + customerBase + @"
                    ),
                    open_opp AS (
                        " + openOpp + @"
                    ),
                    overdue_cust AS (
                        " + overdueCust + @"
                    ),
                    open_ticket_cust AS (
                        " + openTicketCust + @"
                    ),
                    by_rep AS (
                        SELECT rep.AD_User_ID AS Rep_Id,
                               rep.Name AS Rep_Name,
                               COUNT(DISTINCT cb.Bp_Id) AS Cust_Count,
                               COALESCE(SUM(cb.Arr), 0) AS Total_Arr,
                               COUNT(DISTINCT oo.Bp_Id) AS Open_Count,
                               COUNT(DISTINCT CASE WHEN od.Bp_Id IS NOT NULL OR ot.Bp_Id IS NOT NULL THEN cb.Bp_Id END) AS Risk_Count
                        FROM AD_User rep
                        INNER JOIN customer_base cb ON (cb.Rep_Id=rep.AD_User_ID)
                        LEFT OUTER JOIN open_opp oo ON (oo.Bp_Id=cb.Bp_Id)
                        LEFT OUTER JOIN overdue_cust od ON (od.Bp_Id=cb.Bp_Id)
                        LEFT OUTER JOIN open_ticket_cust ot ON (ot.Bp_Id=cb.Bp_Id)
                        WHERE rep.IsActive = 'Y'
                          AND rep.AD_Client_ID = " + lit.ClientId + @"
                        GROUP BY rep.AD_User_ID, rep.Name
                    )
                    SELECT r.Rep_Id, r.Rep_Name, r.Cust_Count, ROUND(r.Total_Arr, sc.Std_Precision) AS Total_Arr,
                           r.Open_Count, r.Risk_Count,
                           COUNT(1) OVER () AS Total_Reps,
                           SUM(r.Total_Arr) OVER () AS All_Reps_Arr,
                           MAX(r.Total_Arr) OVER () AS Max_Arr,
                           sc.Cur_Symbol, sc.ISO_Code, sc.Std_Precision
                    FROM by_rep r
                    CROSS JOIN schema_currency sc
                    ORDER BY r.Total_Arr DESC, r.Rep_Name ASC, r.Rep_Id ASC
                    OFFSET " + offset + @" ROWS FETCH NEXT " + limit + @" ROWS ONLY";

                List<object> items = new List<object>();
                int totalReps = 0;
                decimal totalArr = 0;

                IDataReader dr = null;
                try
                {
                    dr = DB.ExecuteReader(sql);
                    while (dr != null && dr.Read())
                    {
                        totalReps = Util.GetValueOfInt(dr["Total_Reps"]);
                        totalArr = Util.GetValueOfDecimal(dr["All_Reps_Arr"]);
                        currencySymbol = Util.GetValueOfString(dr["Cur_Symbol"]);
                        isoCode = Util.GetValueOfString(dr["ISO_Code"]);
                        if (dr["Std_Precision"] != null && dr["Std_Precision"] != DBNull.Value)
                        {
                            stdPrecision = Util.GetValueOfInt(dr["Std_Precision"]);
                        }

                        decimal arr = Util.GetValueOfDecimal(dr["Total_Arr"]);
                        decimal maxArr = Util.GetValueOfDecimal(dr["Max_Arr"]);
                        int barPercent = maxArr > 0 ? (int)Math.Round(arr * 100m / maxArr, 0) : 0;

                        items.Add(new
                        {
                            repId = Util.GetValueOfInt(dr["Rep_Id"]),
                            repName = Util.GetValueOfString(dr["Rep_Name"]),
                            count = Util.GetValueOfInt(dr["Cust_Count"]),
                            arr = arr,
                            openCount = Util.GetValueOfInt(dr["Open_Count"]),
                            riskCount = Util.GetValueOfInt(dr["Risk_Count"]),
                            barPercent = barPercent
                        });
                    }
                }
                finally { CloseReader(dr); }

                var result = new
                {
                    items = items,
                    total = totalReps,
                    totalReps = totalReps,
                    totalArr = totalArr,
                    offset = offset,
                    limit = limit,
                    currency_symbol = currencySymbol,
                    currency_iso = isoCode,
                    std_precision = stdPrecision
                };
                return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_301_SalesRepPortfolioWidget.GetLeaderboard", ex);
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>One rep's owned customers, ranked by ARR desc.</summary>
        /// <param name="repId">AD_User_ID selected from the leaderboard.</param>
        /// <param name="offset">Zero-based paging offset.</param>
        /// <param name="limit">Page size (7 for the widget, up to 25 for the full list).</param>
        /// <returns>JSON { items:[...], total, offset, limit, currency_* } or { error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetRepList(int repId, int offset = 0, int limit = WidgetPageSize)
        {
            if (Session["ctx"] == null)
            {
                return Json(new { error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired" }, JsonRequestBehavior.AllowGet);
            }

            if (repId <= 0)
            {
                return Json(new { error = "Invalid rep" }, JsonRequestBehavior.AllowGet);
            }

            if (offset < 0) { offset = 0; }
            if (limit <= 0 || limit > MaxListPageSize) { limit = WidgetPageSize; }

            Ctx ctx = Session["ctx"] as Ctx;

            try
            {
                Literals lit = BuildLiterals(ctx);

                string body = @"
                    SELECT bp.C_BPartner_ID AS Bp_Id,
                           bp.Name AS Bp_Name,
                           COALESCE(bp.ActualLifeTimeValue, 0) AS Arr
                    FROM C_BPartner bp
                    WHERE bp.IsActive = 'Y'
                      AND bp.IsCustomer = 'Y'
                      AND bp.AD_Client_ID = " + lit.ClientId + @"
                      AND bp.SalesRep_ID = " + IntSql(repId);
                body = MRole.GetDefault(ctx).AddAccessSQL(body, "bp", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                string sql = @"
                    WITH schema_currency AS (
                        " + SchemaCurrencySql(lit.ClientId) + @"
                    ),
                    customer_rows AS (
                        " + body + @"
                    )
                    SELECT cr.Bp_Id, cr.Bp_Name, ROUND(cr.Arr, sc.Std_Precision) AS Arr,
                           sc.Cur_Symbol, sc.ISO_Code, sc.Std_Precision,
                           COUNT(1) OVER () AS Total_Rows
                    FROM customer_rows cr
                    CROSS JOIN schema_currency sc
                    ORDER BY cr.Arr DESC, cr.Bp_Name ASC, cr.Bp_Id ASC
                    OFFSET " + offset + @" ROWS FETCH NEXT " + limit + @" ROWS ONLY";

                List<object> items = new List<object>();
                int total = 0;
                string currencySymbol = "", isoCode = "";
                int stdPrecision = 2;

                IDataReader dr = null;
                try
                {
                    dr = DB.ExecuteReader(sql);
                    while (dr != null && dr.Read())
                    {
                        total = Util.GetValueOfInt(dr["Total_Rows"]);
                        currencySymbol = Util.GetValueOfString(dr["Cur_Symbol"]);
                        isoCode = Util.GetValueOfString(dr["ISO_Code"]);
                        if (dr["Std_Precision"] != null && dr["Std_Precision"] != DBNull.Value)
                        {
                            stdPrecision = Util.GetValueOfInt(dr["Std_Precision"]);
                        }
                        items.Add(new
                        {
                            customerId = Util.GetValueOfInt(dr["Bp_Id"]),
                            customerName = Util.GetValueOfString(dr["Bp_Name"]),
                            arr = Util.GetValueOfDecimal(dr["Arr"])
                        });
                    }
                }
                finally { CloseReader(dr); }

                var result = new
                {
                    items = items,
                    total = total,
                    offset = offset,
                    limit = limit,
                    currency_symbol = currencySymbol,
                    currency_iso = isoCode,
                    std_precision = stdPrecision
                };
                return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_301_SalesRepPortfolioWidget.GetRepList", ex);
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
