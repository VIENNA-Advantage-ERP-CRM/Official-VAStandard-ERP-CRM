/******************************************************
 * Module Name    : VAS
 * Purpose        : Customers module Revenue at a Glance widget endpoints
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
    /// Module Name : VAS_299_RevenueAtAGlanceWidget
    /// Purpose     : 3x2 tile - the four money figures that matter, each drilling
    ///               into its own paged list:
    ///                 pipeline   - open VAS_Opportunity value (VAS_OppStage IN
    ///                              ('10','11','12','13','15') OR NULL, not yet
    ///                              converted to an order), business-partner-backed
    ///                              only. Same open-stage rule as VAS_125/139;
    ///                              narrower than VAS_139 in one respect - a
    ///                              lead-only opportunity (no Ref_BPartner_ID) is
    ///                              not counted, since this tile summarises
    ///                              CUSTOMER revenue, not the lead pipeline.
    ///                 proposals  - open C_Order sales proposals: IsSOTrx='Y' AND
    ///                              (IsSalesQuotation='Y' OR (C_DocType.DocBaseType
    ///                              ='SOO' AND DocSubTypeSO IN ('OB','ON','QT')))
    ///                              AND DocStatus IN ('DR','IN','CO') - identical
    ///                              population to VAS_296.
    ///                 contracts  - Accounts Receivable contracts (VAS_ContractMaster
    ///                              UNION ALL C_Contract, per VAS_140's eligibility
    ///                              rules) ending within the next 90 days.
    ///                 overdue    - unpaid C_InvoicePaySchedule rows past due on a
    ///                              completed customer sales invoice - identical
    ///                              population to VAS_138.
    ///               Every amount is converted to the tenant accounting currency via
    ///               CurrencyConvert so the four figures share one comparable base.
    ///               MRole (tenant + org + record access) is applied to each query's
    ///               own main physical table (VAS_Opportunity / C_Order /
    ///               VAS_ContractMaster+C_Contract / C_InvoicePaySchedule) - never to
    ///               a CTE alias or a secondary join, per the CTE rule.
    /// Chronological development:
    ///   VAI052      2026-09-23 Created
    /// </summary>
    public class VAS_299_RevenueAtAGlanceWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_299_RevenueAtAGlanceWidgetController).FullName);

        private const int WidgetPageSize = 7;
        private const int MaxListPageSize = 25;

        /// <summary>Contracts due within this many days count toward the tile.</summary>
        private const int ContractWindowDays = 90;

        /// <summary>Only Accounts Receivable contracts are in scope. Matches VAS_140.</summary>
        private const string ContractTypeFilter = "ASR";

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

        /// <summary>
        /// An int as an SQL literal. Every value inlined by this controller is either
        /// server-derived (tenant id, default conversion type) or an already-validated
        /// request value, so none can carry SQL text. Same fix VAS_139/140 needed for
        /// the ORA-01008 repeated-placeholder defect: these statements combine several
        /// CTEs, so a bound @Client_ID would occur more times than the parameter list -
        /// inlining avoids that entirely.
        /// </summary>
        private static string IntSql(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>A whole-day date literal in the connected dialect. Matches VAS_140.ToSqlDate.</summary>
        private static string ToSqlDate(DateTime date)
        {
            DateTime day = date.Date;
            if (DB.IsOracle())
            {
                return "TO_DATE('" + day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "','YYYY-MM-DD')";
            }
            return DB.TO_DATE(day, true);
        }

        private class Literals
        {
            public string ClientId;
            public string ConversionTypeId;
            public string Today;
            public string PlusContractWindow;
        }

        private static Literals BuildLiterals(Ctx ctx)
        {
            DateTime today = DateTime.Today;
            return new Literals
            {
                ClientId = IntSql(ctx.GetAD_Client_ID()),
                ConversionTypeId = IntSql(MConversionType.GetDefault(ctx.GetAD_Client_ID())),
                Today = ToSqlDate(today),
                PlusContractWindow = ToSqlDate(today.AddDays(ContractWindowDays + 1))
            };
        }

        /// <summary>
        /// The four tile totals in one payload: value + a client/contract count for
        /// each, plus the shared display currency.
        /// </summary>
        /// <returns>JSON { pipelineValue, pipelineClients, proposalValue, proposalClients,
        /// contractValue, contractCount, overdueValue, overdueClients, currency_* } or { error }.</returns>
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

                decimal pipelineValue; int pipelineClients;
                ReadTotal(PipelineTotalSql(ctx, lit), stdPrecision, out pipelineValue, out pipelineClients);

                decimal proposalValue; int proposalClients;
                ReadTotal(ProposalTotalSql(ctx, lit), stdPrecision, out proposalValue, out proposalClients);

                decimal contractValue; int contractCount;
                ReadTotal(ContractTotalSql(ctx, lit), stdPrecision, out contractValue, out contractCount);

                decimal overdueValue; int overdueClients;
                ReadTotal(OverdueTotalSql(ctx, lit), stdPrecision, out overdueValue, out overdueClients);

                var result = new
                {
                    pipelineValue = pipelineValue,
                    pipelineClients = pipelineClients,
                    proposalValue = proposalValue,
                    proposalClients = proposalClients,
                    contractValue = contractValue,
                    contractCount = contractCount,
                    overdueValue = overdueValue,
                    overdueClients = overdueClients,
                    currency_symbol = currencySymbol,
                    currency_iso = isoCode,
                    std_precision = stdPrecision
                };
                return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_299_RevenueAtAGlanceWidget.GetSummary", ex);
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>Reads a single { Value, Cnt } aggregate row and rounds Value to the display precision.</summary>
        private void ReadTotal(string sql, int stdPrecision, out decimal value, out int count)
        {
            value = 0; count = 0;
            IDataReader dr = null;
            try
            {
                // No parameters: every value in these statements is inlined (ORA-01008 - see IntSql).
                dr = DB.ExecuteReader(sql);
                if (dr != null && dr.Read())
                {
                    value = Math.Round(Util.GetValueOfDecimal(dr["Value"]), stdPrecision);
                    count = Util.GetValueOfInt(dr["Cnt"]);
                }
            }
            finally { CloseReader(dr); }
        }

        // ----------------------------------------------------------------- //
        //  Pipeline - open VAS_Opportunity, business-partner-backed only.     //
        // ----------------------------------------------------------------- //

        private const string OpenStagePredicate =
            "(o.VAS_OppStage IN ('10','11','12','13','15') OR o.VAS_OppStage IS NULL)";

        private string PipelineConvertedAmtExpr(Literals lit)
        {
            return @"CASE WHEN o.C_Currency_ID IS NULL OR o.C_Currency_ID = sc.Acct_Currency_ID THEN COALESCE(o.PlannedAmt, 0)
                          ELSE CurrencyConvert(COALESCE(o.PlannedAmt, 0), o.C_Currency_ID, sc.Acct_Currency_ID, "
                          + lit.Today + @", " + lit.ConversionTypeId + @", o.AD_Client_ID, o.AD_Org_ID) END";
        }

        private string PipelineFromWhere(Literals lit)
        {
            return @"
                FROM VAS_Opportunity o
                INNER JOIN schema_currency sc ON (sc.AD_Client_ID=o.AD_Client_ID)
                INNER JOIN C_BPartner bp ON (bp.C_BPartner_ID=o.Ref_BPartner_ID AND bp.AD_Client_ID=o.AD_Client_ID AND bp.IsActive = 'Y')
                WHERE o.IsActive = 'Y'
                  AND o.AD_Client_ID = " + lit.ClientId + @"
                  AND " + OpenStagePredicate + @"
                  AND o.Ref_Order_ID IS NULL
                  AND o.C_Order_ID IS NULL";
        }

        private string PipelineTotalSql(Ctx ctx, Literals lit)
        {
            string body = @"
                SELECT bp.C_BPartner_ID AS Bp_Id,
                       " + PipelineConvertedAmtExpr(lit) + @" AS Converted_Amt
                " + PipelineFromWhere(lit);
            body = MRole.GetDefault(ctx).AddAccessSQL(body, "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            return @"
                WITH schema_currency AS (
                    " + SchemaCurrencySql(lit.ClientId) + @"
                ),
                pipeline_rows AS (
                    " + body + @"
                )
                SELECT COALESCE(SUM(pr.Converted_Amt), 0) AS Value,
                       COUNT(DISTINCT pr.Bp_Id) AS Cnt
                FROM pipeline_rows pr";
        }

        // ----------------------------------------------------------------- //
        //  Proposals - open C_Order sales proposals. Identical population   //
        //  to VAS_296.                                                      //
        // ----------------------------------------------------------------- //

        private string ProposalConvertedAmtExpr(Literals lit)
        {
            return @"CASE WHEN o.C_Currency_ID = sc.Acct_Currency_ID THEN COALESCE(o.GrandTotal, 0)
                          ELSE CurrencyConvert(COALESCE(o.GrandTotal, 0), o.C_Currency_ID, sc.Acct_Currency_ID, o.DateOrdered, "
                          + lit.ConversionTypeId + @", o.AD_Client_ID, o.AD_Org_ID) END";
        }

        private string ProposalFromWhere(Literals lit)
        {
            return @"
                FROM C_Order o
                INNER JOIN C_DocType dt ON (dt.C_DocType_ID=o.C_DocType_ID AND dt.IsActive = 'Y')
                INNER JOIN C_BPartner bp ON (bp.C_BPartner_ID=o.C_BPartner_ID AND bp.AD_Client_ID=o.AD_Client_ID AND bp.IsActive = 'Y' AND bp.IsCustomer = 'Y')
                INNER JOIN schema_currency sc ON (sc.AD_Client_ID=o.AD_Client_ID)
                WHERE o.IsActive = 'Y'
                  AND o.AD_Client_ID = " + lit.ClientId + @"
                  AND COALESCE(o.IsSOTrx, 'N') = 'Y'
                  AND (COALESCE(o.IsSalesQuotation, 'N') = 'Y'
                       OR (dt.DocBaseType = 'SOO' AND COALESCE(dt.DocSubTypeSO, ' ') IN ('OB', 'ON', 'QT')))
                  AND o.DocStatus IN ('DR', 'IN', 'CO')";
        }

        private string ProposalTotalSql(Ctx ctx, Literals lit)
        {
            string body = @"
                SELECT bp.C_BPartner_ID AS Bp_Id,
                       " + ProposalConvertedAmtExpr(lit) + @" AS Converted_Amt
                " + ProposalFromWhere(lit);
            body = MRole.GetDefault(ctx).AddAccessSQL(body, "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            return @"
                WITH schema_currency AS (
                    " + SchemaCurrencySql(lit.ClientId) + @"
                ),
                proposal_rows AS (
                    " + body + @"
                )
                SELECT COALESCE(SUM(pr.Converted_Amt), 0) AS Value,
                       COUNT(DISTINCT pr.Bp_Id) AS Cnt
                FROM proposal_rows pr";
        }

        // ----------------------------------------------------------------- //
        //  Contracts - Accounts Receivable, ending within the window.       //
        //  VAS_ContractMaster UNION ALL C_Contract. Identical eligibility   //
        //  rules to VAS_140 (minus the LE_30/D31_60/D61_90 bucketing this   //
        //  tile has no use for).                                            //
        // ----------------------------------------------------------------- //

        private string VasContractSql(Literals lit)
        {
            string valueConv = @"CASE WHEN c.C_Currency_ID = sc.Acct_Currency_ID THEN COALESCE(c.VAS_ContractAmount, 0)
                                      ELSE CurrencyConvert(COALESCE(c.VAS_ContractAmount, 0), c.C_Currency_ID, sc.Acct_Currency_ID, c.StartDate, "
                                      + lit.ConversionTypeId + @", c.AD_Client_ID, c.AD_Org_ID) END";
            return @"
                SELECT c.VAS_ContractMaster_ID AS Contract_Id,
                       c.C_BPartner_ID AS Bp_Id,
                       " + valueConv + @" AS Value_Conv
                FROM VAS_ContractMaster c
                INNER JOIN C_BPartner bp ON (bp.C_BPartner_ID=c.C_BPartner_ID AND bp.AD_Client_ID=c.AD_Client_ID AND bp.IsActive = 'Y')
                INNER JOIN schema_currency sc ON (sc.AD_Client_ID=c.AD_Client_ID)
                WHERE c.IsActive = 'Y'
                  AND c.VAS_Status = 'ARD'
                  AND c.ContractType = '" + ContractTypeFilter + @"'
                  AND COALESCE(c.VAS_IsApproved, 'N') = 'Y'
                  AND COALESCE(c.IsExpiredContracts, 'N') <> 'Y'
                  AND COALESCE(c.VAS_Terminate, 'N') <> 'Y'
                  AND c.C_BPartner_ID IS NOT NULL
                  AND c.EndDate IS NOT NULL
                  AND c.AD_Client_ID = " + lit.ClientId + @"
                  AND c.EndDate >= " + lit.Today + @"
                  AND c.EndDate < " + lit.PlusContractWindow;
        }

        private string CoreContractSql(Literals lit)
        {
            string valueConv = @"CASE WHEN c.C_Currency_ID = sc.Acct_Currency_ID THEN COALESCE(c.GrandTotal, 0)
                                      ELSE CurrencyConvert(COALESCE(c.GrandTotal, 0), c.C_Currency_ID, sc.Acct_Currency_ID, c.StartDate,
                                              CASE WHEN COALESCE(c.C_ConversionType_ID, 0) = 0 THEN " + lit.ConversionTypeId + @" ELSE c.C_ConversionType_ID END,
                                              c.AD_Client_ID, c.AD_Org_ID) END";
            return @"
                SELECT c.C_Contract_ID AS Contract_Id,
                       c.C_BPartner_ID AS Bp_Id,
                       " + valueConv + @" AS Value_Conv
                FROM C_Contract c
                INNER JOIN C_BPartner bp ON (bp.C_BPartner_ID=c.C_BPartner_ID AND bp.AD_Client_ID=c.AD_Client_ID AND bp.IsActive = 'Y')
                INNER JOIN schema_currency sc ON (sc.AD_Client_ID=c.AD_Client_ID)
                WHERE c.IsActive = 'Y'
                  AND COALESCE(c.Processed, 'N') = 'Y'
                  AND c.ContractType = '" + ContractTypeFilter + @"'
                  AND c.C_BPartner_ID IS NOT NULL
                  AND c.EndDate IS NOT NULL
                  AND c.AD_Client_ID = " + lit.ClientId + @"
                  AND c.EndDate >= " + lit.Today + @"
                  AND c.EndDate < " + lit.PlusContractWindow;
        }

        private string ContractTotalSql(Ctx ctx, Literals lit)
        {
            string vas = MRole.GetDefault(ctx).AddAccessSQL(VasContractSql(lit), "c", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            string core = MRole.GetDefault(ctx).AddAccessSQL(CoreContractSql(lit), "c", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            return @"
                WITH schema_currency AS (
                    " + SchemaCurrencySql(lit.ClientId) + @"
                ),
                contract_rows AS (
                    " + vas + @"
                    UNION ALL
                    " + core + @"
                )
                SELECT COALESCE(SUM(cr.Value_Conv), 0) AS Value,
                       COUNT(*) AS Cnt
                FROM contract_rows cr";
        }

        // ----------------------------------------------------------------- //
        //  Overdue A/R - unpaid, past-due C_InvoicePaySchedule. Identical   //
        //  population to VAS_138.                                           //
        // ----------------------------------------------------------------- //

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

        private string OverdueConvertedAmtExpr(Literals lit)
        {
            return "CurrencyConvert(" + OverdueOpenAmtExpr() + ", i.C_Currency_ID, sc.Acct_Currency_ID, i.DateAcct, "
                 + lit.ConversionTypeId + ", i.AD_Client_ID, i.AD_Org_ID)";
        }

        private string OverdueFromWhere(Literals lit)
        {
            return @"
                FROM C_InvoicePaySchedule ips
                INNER JOIN C_Invoice i ON (ips.C_Invoice_ID=i.C_Invoice_ID AND i.IsActive = 'Y')
                INNER JOIN C_BPartner bp ON (bp.C_BPartner_ID=i.C_BPartner_ID AND bp.AD_Client_ID=i.AD_Client_ID AND bp.IsActive = 'Y' AND bp.IsCustomer = 'Y')
                INNER JOIN schema_currency sc ON (sc.AD_Client_ID=i.AD_Client_ID)
                WHERE ips.IsActive = 'Y'
                  AND ips.VA009_IsPaid = 'N'" + OverdueDateCondition() + @"
                  AND i.DocStatus IN ('CO', 'CL')
                  AND i.IsSOTrx = 'Y'
                  AND i.IsReturnTrx = 'N'
                  AND i.AD_Client_ID = " + lit.ClientId;
        }

        private string OverdueTotalSql(Ctx ctx, Literals lit)
        {
            string body = @"
                SELECT i.C_BPartner_ID AS Bp_Id,
                       " + OverdueConvertedAmtExpr(lit) + @" AS Converted_Amt
                " + OverdueFromWhere(lit) + @"
                  AND ROUND(" + OverdueOpenAmtExpr() + @", sc.Std_Precision) > 0";
            body = MRole.GetDefault(ctx).AddAccessSQL(body, "ips", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            return @"
                WITH schema_currency AS (
                    " + SchemaCurrencySql(lit.ClientId) + @"
                ),
                overdue_rows AS (
                    " + body + @"
                )
                SELECT COALESCE(SUM(orr.Converted_Amt), 0) AS Value,
                       COUNT(DISTINCT orr.Bp_Id) AS Cnt
                FROM overdue_rows orr";
        }

        // ----------------------------------------------------------------- //
        //  Drill-down list for one tile.                                    //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// Paged drill-down for one tile. Flag is validated against the allow-list.
        /// Rows share one generic shape (customerId, customerName, amount, count) so
        /// the client renders all four with one row template; "count" means the
        /// figure named in each flag's own comment below.
        /// </summary>
        /// <param name="flag">pipeline / proposals / contracts / overdue.</param>
        /// <param name="offset">Zero-based paging offset.</param>
        /// <param name="limit">Page size (7 for the widget, up to 25 for the full list).</param>
        /// <returns>JSON { items:[...], total, offset, limit, currency_* } or { error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetList(string flag, int offset = 0, int limit = WidgetPageSize)
        {
            if (Session["ctx"] == null)
            {
                return Json(new { error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired" }, JsonRequestBehavior.AllowGet);
            }

            string key = (flag ?? "").Trim();
            if (key != "pipeline" && key != "proposals" && key != "contracts" && key != "overdue")
            {
                return Json(new { error = "Invalid flag" }, JsonRequestBehavior.AllowGet);
            }

            if (offset < 0) { offset = 0; }
            if (limit <= 0 || limit > MaxListPageSize) { limit = WidgetPageSize; }

            Ctx ctx = Session["ctx"] as Ctx;

            try
            {
                switch (key)
                {
                    case "pipeline": return PipelineList(ctx, offset, limit);
                    case "proposals": return ProposalList(ctx, offset, limit);
                    case "contracts": return ContractList(ctx, offset, limit);
                    default: return OverdueList(ctx, offset, limit);
                }
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_299_RevenueAtAGlanceWidget.GetList." + key, ex);
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>Ranked by pipeline value desc. "count" = open opportunity count.</summary>
        private JsonResult PipelineList(Ctx ctx, int offset, int limit)
        {
            Literals lit = BuildLiterals(ctx);

            string body = @"
                SELECT bp.C_BPartner_ID AS Bp_Id,
                       bp.Name AS Bp_Name,
                       o.VAS_Opportunity_ID AS Opp_Id,
                       " + PipelineConvertedAmtExpr(lit) + @" AS Converted_Amt
                " + PipelineFromWhere(lit);
            body = MRole.GetDefault(ctx).AddAccessSQL(body, "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string sql = @"
                WITH schema_currency AS (
                    " + SchemaCurrencySql(lit.ClientId) + @"
                ),
                pipeline_rows AS (
                    " + body + @"
                ),
                by_customer AS (
                    SELECT pr.Bp_Id AS Bp_Id,
                           pr.Bp_Name AS Bp_Name,
                           COUNT(DISTINCT pr.Opp_Id) AS Item_Count,
                           SUM(pr.Converted_Amt) AS Amount
                    FROM pipeline_rows pr
                    GROUP BY pr.Bp_Id, pr.Bp_Name
                )
                SELECT c.Bp_Id, c.Bp_Name, c.Item_Count, ROUND(c.Amount, sc.Std_Precision) AS Amount,
                       sc.Cur_Symbol, sc.ISO_Code, sc.Std_Precision,
                       COUNT(1) OVER () AS Total_Rows
                FROM by_customer c
                CROSS JOIN schema_currency sc
                ORDER BY c.Amount DESC, c.Bp_Name ASC, c.Bp_Id ASC
                OFFSET " + offset + @" ROWS FETCH NEXT " + limit + @" ROWS ONLY";

            return RunGenericList(sql, offset, limit);
        }

        /// <summary>Ranked by proposal value desc. "count" = proposal count.</summary>
        private JsonResult ProposalList(Ctx ctx, int offset, int limit)
        {
            Literals lit = BuildLiterals(ctx);

            string body = @"
                SELECT bp.C_BPartner_ID AS Bp_Id,
                       bp.Name AS Bp_Name,
                       o.C_Order_ID AS Order_Id,
                       " + ProposalConvertedAmtExpr(lit) + @" AS Converted_Amt
                " + ProposalFromWhere(lit);
            body = MRole.GetDefault(ctx).AddAccessSQL(body, "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string sql = @"
                WITH schema_currency AS (
                    " + SchemaCurrencySql(lit.ClientId) + @"
                ),
                proposal_rows AS (
                    " + body + @"
                ),
                by_customer AS (
                    SELECT pr.Bp_Id AS Bp_Id,
                           pr.Bp_Name AS Bp_Name,
                           COUNT(DISTINCT pr.Order_Id) AS Item_Count,
                           SUM(pr.Converted_Amt) AS Amount
                    FROM proposal_rows pr
                    GROUP BY pr.Bp_Id, pr.Bp_Name
                )
                SELECT c.Bp_Id, c.Bp_Name, c.Item_Count, ROUND(c.Amount, sc.Std_Precision) AS Amount,
                       sc.Cur_Symbol, sc.ISO_Code, sc.Std_Precision,
                       COUNT(1) OVER () AS Total_Rows
                FROM by_customer c
                CROSS JOIN schema_currency sc
                ORDER BY c.Amount DESC, c.Bp_Name ASC, c.Bp_Id ASC
                OFFSET " + offset + @" ROWS FETCH NEXT " + limit + @" ROWS ONLY";

            return RunGenericList(sql, offset, limit);
        }

        /// <summary>Ranked by ending soonest. "count" = live contract count for that customer.</summary>
        private JsonResult ContractList(Ctx ctx, int offset, int limit)
        {
            Literals lit = BuildLiterals(ctx);

            string vasBody = @"
                SELECT c.C_BPartner_ID AS Bp_Id, bp.Name AS Bp_Name, c.VAS_ContractMaster_ID AS Contract_Id, c.EndDate AS End_Date,
                       " + VasContractValueExpr(lit) + @" AS Converted_Amt
                FROM VAS_ContractMaster c
                INNER JOIN C_BPartner bp ON (bp.C_BPartner_ID=c.C_BPartner_ID AND bp.AD_Client_ID=c.AD_Client_ID AND bp.IsActive = 'Y')
                INNER JOIN schema_currency sc ON (sc.AD_Client_ID=c.AD_Client_ID)
                WHERE c.IsActive = 'Y'
                  AND c.VAS_Status = 'ARD'
                  AND c.ContractType = '" + ContractTypeFilter + @"'
                  AND COALESCE(c.VAS_IsApproved, 'N') = 'Y'
                  AND COALESCE(c.IsExpiredContracts, 'N') <> 'Y'
                  AND COALESCE(c.VAS_Terminate, 'N') <> 'Y'
                  AND c.C_BPartner_ID IS NOT NULL
                  AND c.EndDate IS NOT NULL
                  AND c.AD_Client_ID = " + lit.ClientId + @"
                  AND c.EndDate >= " + lit.Today + @"
                  AND c.EndDate < " + lit.PlusContractWindow;
            vasBody = MRole.GetDefault(ctx).AddAccessSQL(vasBody, "c", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string coreBody = @"
                SELECT c.C_BPartner_ID AS Bp_Id, bp.Name AS Bp_Name, c.C_Contract_ID AS Contract_Id, c.EndDate AS End_Date,
                       " + CoreContractValueExpr(lit) + @" AS Converted_Amt
                FROM C_Contract c
                INNER JOIN C_BPartner bp ON (bp.C_BPartner_ID=c.C_BPartner_ID AND bp.AD_Client_ID=c.AD_Client_ID AND bp.IsActive = 'Y')
                INNER JOIN schema_currency sc ON (sc.AD_Client_ID=c.AD_Client_ID)
                WHERE c.IsActive = 'Y'
                  AND COALESCE(c.Processed, 'N') = 'Y'
                  AND c.ContractType = '" + ContractTypeFilter + @"'
                  AND c.C_BPartner_ID IS NOT NULL
                  AND c.EndDate IS NOT NULL
                  AND c.AD_Client_ID = " + lit.ClientId + @"
                  AND c.EndDate >= " + lit.Today + @"
                  AND c.EndDate < " + lit.PlusContractWindow;
            coreBody = MRole.GetDefault(ctx).AddAccessSQL(coreBody, "c", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string sql = @"
                WITH schema_currency AS (
                    " + SchemaCurrencySql(lit.ClientId) + @"
                ),
                contract_rows AS (
                    " + vasBody + @"
                    UNION ALL
                    " + coreBody + @"
                ),
                by_customer AS (
                    SELECT cr.Bp_Id AS Bp_Id,
                           cr.Bp_Name AS Bp_Name,
                           COUNT(*) AS Item_Count,
                           MIN(cr.End_Date) AS Soonest_End,
                           SUM(cr.Converted_Amt) AS Amount
                    FROM contract_rows cr
                    GROUP BY cr.Bp_Id, cr.Bp_Name
                )
                SELECT c.Bp_Id, c.Bp_Name, c.Item_Count, ROUND(c.Amount, sc.Std_Precision) AS Amount,
                       c.Soonest_End,
                       sc.Cur_Symbol, sc.ISO_Code, sc.Std_Precision,
                       COUNT(1) OVER () AS Total_Rows
                FROM by_customer c
                CROSS JOIN schema_currency sc
                ORDER BY c.Soonest_End ASC, c.Bp_Name ASC, c.Bp_Id ASC
                OFFSET " + offset + @" ROWS FETCH NEXT " + limit + @" ROWS ONLY";

            return RunGenericList(sql, offset, limit, includeEndDate: true);
        }

        private string VasContractValueExpr(Literals lit)
        {
            return @"CASE WHEN c.C_Currency_ID = sc.Acct_Currency_ID THEN COALESCE(c.VAS_ContractAmount, 0)
                          ELSE CurrencyConvert(COALESCE(c.VAS_ContractAmount, 0), c.C_Currency_ID, sc.Acct_Currency_ID, c.StartDate, "
                          + lit.ConversionTypeId + @", c.AD_Client_ID, c.AD_Org_ID) END";
        }

        private string CoreContractValueExpr(Literals lit)
        {
            return @"CASE WHEN c.C_Currency_ID = sc.Acct_Currency_ID THEN COALESCE(c.GrandTotal, 0)
                          ELSE CurrencyConvert(COALESCE(c.GrandTotal, 0), c.C_Currency_ID, sc.Acct_Currency_ID, c.StartDate,
                                  CASE WHEN COALESCE(c.C_ConversionType_ID, 0) = 0 THEN " + lit.ConversionTypeId + @" ELSE c.C_ConversionType_ID END,
                                  c.AD_Client_ID, c.AD_Org_ID) END";
        }

        /// <summary>Ranked by overdue amount desc. "count" = overdue invoice count.</summary>
        private JsonResult OverdueList(Ctx ctx, int offset, int limit)
        {
            Literals lit = BuildLiterals(ctx);

            string body = @"
                SELECT i.C_BPartner_ID AS Bp_Id,
                       bp.Name AS Bp_Name,
                       i.C_Invoice_ID AS Invoice_Id,
                       " + OverdueConvertedAmtExpr(lit) + @" AS Converted_Amt
                " + OverdueFromWhere(lit) + @"
                  AND ROUND(" + OverdueOpenAmtExpr() + @", sc.Std_Precision) > 0";
            body = MRole.GetDefault(ctx).AddAccessSQL(body, "ips", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string sql = @"
                WITH schema_currency AS (
                    " + SchemaCurrencySql(lit.ClientId) + @"
                ),
                overdue_rows AS (
                    " + body + @"
                ),
                by_customer AS (
                    SELECT orr.Bp_Id AS Bp_Id,
                           orr.Bp_Name AS Bp_Name,
                           COUNT(DISTINCT orr.Invoice_Id) AS Item_Count,
                           SUM(orr.Converted_Amt) AS Amount
                    FROM overdue_rows orr
                    GROUP BY orr.Bp_Id, orr.Bp_Name
                )
                SELECT c.Bp_Id, c.Bp_Name, c.Item_Count, ROUND(c.Amount, sc.Std_Precision) AS Amount,
                       sc.Cur_Symbol, sc.ISO_Code, sc.Std_Precision,
                       COUNT(1) OVER () AS Total_Rows
                FROM by_customer c
                CROSS JOIN schema_currency sc
                ORDER BY c.Amount DESC, c.Bp_Name ASC, c.Bp_Id ASC
                OFFSET " + offset + @" ROWS FETCH NEXT " + limit + @" ROWS ONLY";

            return RunGenericList(sql, offset, limit);
        }

        /// <summary>
        /// Runs one of the four by_customer list statements and shapes the common
        /// result. includeEndDate adds the contract tile's "soonest end" column.
        /// </summary>
        private JsonResult RunGenericList(string sql, int offset, int limit, bool includeEndDate = false)
        {
            List<object> items = new List<object>();
            int total = 0;
            string currencySymbol = "", isoCode = "";
            int stdPrecision = 2;

            IDataReader dr = null;
            try
            {
                // No parameters: every value in this statement is inlined (ORA-01008 - see IntSql).
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

                    string endDate = "";
                    if (includeEndDate)
                    {
                        DateTime? end = dr["Soonest_End"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr["Soonest_End"]);
                        endDate = end.HasValue ? end.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "";
                    }

                    items.Add(new
                    {
                        customerId = Util.GetValueOfInt(dr["Bp_Id"]),
                        customerName = Util.GetValueOfString(dr["Bp_Name"]),
                        count = Util.GetValueOfInt(dr["Item_Count"]),
                        amount = Util.GetValueOfDecimal(dr["Amount"]),
                        endDate = endDate
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

        private void CloseReader(IDataReader reader)
        {
            if (reader == null) { return; }
            reader.Close();
            reader.Dispose();
        }
    }
}
