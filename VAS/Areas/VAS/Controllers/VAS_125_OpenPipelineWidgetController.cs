/******************************************************
 * Module Name    : VAS
 * Purpose        : Customers module Open Pipeline KPI widget endpoint
 * chronological  : Development
 * Created Date   : 2026-07-21
 * Created by     : VAI052
 ******************************************************/

using Newtonsoft.Json;
using System;
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
    /// Module Name : VAS_125_OpenPipelineWidget
    /// Purpose     : Static 2x1 KPI tile - the total UNWEIGHTED value of active, open
    ///               CRM opportunities (VAS_Opportunity) and the count of distinct
    ///               customers with at least one, in the logged-in tenant and the
    ///               organizations the role may access. Each opportunity's PlannedAmt
    ///               is converted to the tenant accounting currency via CurrencyConvert
    ///               (mixed currencies summed into one base total, never weighted by
    ///               Probability). A missing exchange rate on a non-zero amount is
    ///               flagged (data-quality) so the UI can show a neutral "rate
    ///               unavailable" state instead of a silent partial total.
    ///
    ///               Open = the stage rule from VA061_056_OpenOpportunitiesModel:
    ///               VAS_OppStage IN ('10','11','12','13','15') OR VAS_OppStage IS NULL
    ///               (10 Prospecting, 11 Discovery/Design, 12 Product Evaluation,
    ///               13 Proposal, 15 Negotiation; 16 Won and 17 Lost/Archived are
    ///               terminal; a NULL stage is Open/Unassigned and is counted, not
    ///               dropped), further restricted to opportunities not yet converted to
    ///               an order (Ref_Order_ID IS NULL AND C_Order_ID IS NULL).
    ///
    ///               The customer is COALESCE(Ref_BPartner_ID, C_BPartner_ID):
    ///               VAS_Opportunity carries the account on Ref_BPartner_ID, and
    ///               C_BPartner_ID is only sporadically populated. MRole (tenant + org
    ///               + record access) is applied to the main physical table
    ///               VAS_Opportunity only.
    /// Chronological development:
    ///   VAI052      2026-07-21 Created
    ///   VAI052      2026-08-10 Source switched from C_Project (VAS_ProjectStatus
    ///                          'DR'/'IP') to VAS_Opportunity, per the open-stage rule
    ///                          in VA061_056_OpenOpportunitiesModel
    /// </summary>
    public class VAS_125_OpenPipelineWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_125_OpenPipelineWidgetController).FullName);

        /// <summary>
        /// Non-terminal opportunity stages, plus a NULL stage (Open / Unassigned - an
        /// opportunity with no stage yet is still in play and must be counted, not
        /// dropped). Mirrors OPEN_STAGE_PREDICATE in VA061_056_OpenOpportunitiesModel.
        /// Deliberately the stage-only form: that model additionally widens its headline
        /// count with "OR C_EnquiryRdate IS NULL OR VAS_DecisionDate IS NULL", a flat OR
        /// its own comment flags as a caveat because it re-admits Won/Lost rows that
        /// merely lack a date. A pipeline VALUE must not include Won or Lost, so the
        /// widened form is not used here.
        /// </summary>
        private const string OpenStagePredicate =
            "(o.VAS_OppStage IN ('10','11','12','13','15') OR o.VAS_OppStage IS NULL)";

        /// <summary>
        /// The account an opportunity belongs to. VAS_Opportunity carries it on
        /// Ref_BPartner_ID; C_BPartner_ID exists but is only sporadically populated, so
        /// it is a fallback rather than the primary link.
        /// </summary>
        private const string CustomerIdExpr = "COALESCE(o.Ref_BPartner_ID, o.C_BPartner_ID)";

        /// <summary>
        /// KPI tile data: base-currency open-pipeline total, distinct customer and
        /// opportunity counts, currency info, and a data-completeness flag.
        /// </summary>
        /// <returns>
        /// JSON { pipeline_value, customer_count, opportunity_count, currency_symbol,
        /// currency_iso, std_precision, data_complete } or { error }.
        /// </returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetOpenPipeline()
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
                // Convert as of the current business date. Server-computed, not user
                // input, and dialect-correct for Oracle vs PostgreSQL.
                string conversionDate = DB.IsPostgreSQL() ? "CURRENT_DATE" : "TRUNC(SYSDATE)";

                // VAS_Opportunity carries no conversion type, so resolve the tenant's
                // DEFAULT conversion type. CurrencyConvert returns NULL when the
                // conversion-type argument is NULL (every other codebase caller
                // passes a real type), which previously flagged every foreign-
                // currency opportunity as a missing rate.
                int conversionTypeId = MConversionType.GetDefault(ctx.GetAD_Client_ID());

                // Per-opportunity amount converted to the tenant accounting currency;
                // NULL only when a genuine exchange rate is missing for the pair/date.
                string convertedAmount = @"CurrencyConvert(COALESCE(o.PlannedAmt, 0), o.C_Currency_ID, sc.Acct_Currency_ID, "
                    + conversionDate + @", @ConversionType_ID, o.AD_Client_ID, o.AD_Org_ID)";

                // Single-row tenant accounting currency (reporting currency).
                string schemaCurrencySql = @"
                    SELECT ci.AD_Client_ID AS AD_Client_ID,
                           cs.C_Currency_ID AS Acct_Currency_ID,
                           cur.StdPrecision AS Std_Precision,
                           cur.ISO_Code AS ISO_Code,
                           CASE WHEN cur.CurSymbol IS NOT NULL THEN cur.CurSymbol ELSE cur.ISO_Code END AS Cur_Symbol
                    FROM AD_ClientInfo ci
                    INNER JOIN C_AcctSchema cs ON (cs.C_AcctSchema_ID=ci.C_AcctSchema1_ID AND cs.IsActive = 'Y')
                    INNER JOIN C_Currency cur ON (cur.C_Currency_ID=cs.C_Currency_ID AND cur.IsActive = 'Y')
                    WHERE ci.IsActive = 'Y'
                      AND ci.AD_Client_ID = @Client_ID";

                // Aggregate with no GROUP BY always yields exactly one row (SUM/COUNT
                // = 0 when nothing matches). Missing_Rate_Count flags non-zero
                // amounts whose conversion returned NULL, so a partial total is never
                // shown as complete.
                // An opportunity with no currency is already in the tenant's own
                // currency (the column is optional on VAS_Opportunity and is left
                // empty on most rows), so it short-circuits alongside the
                // same-currency case rather than being fed to CurrencyConvert -
                // which returns NULL for a NULL source currency and would otherwise
                // report every such row as a missing rate.
                string pipelineSql = @"
                    SELECT COALESCE(SUM(CASE
                               WHEN o.C_Currency_ID IS NULL OR o.C_Currency_ID = sc.Acct_Currency_ID THEN COALESCE(o.PlannedAmt, 0)
                               ELSE " + convertedAmount + @"
                           END), 0) AS Pipeline_Value,
                           COUNT(DISTINCT CASE WHEN bp.C_BPartner_ID IS NOT NULL
                                               THEN 'B' || bp.C_BPartner_ID
                                               ELSE 'L' || ld.C_Lead_ID END) AS Customer_Count,
                           COUNT(DISTINCT o.VAS_Opportunity_ID) AS Opportunity_Count,
                           COALESCE(SUM(CASE
                               WHEN o.C_Currency_ID IS NOT NULL
                                    AND o.C_Currency_ID <> sc.Acct_Currency_ID
                                    AND COALESCE(o.PlannedAmt, 0) <> 0
                                    AND " + convertedAmount + @" IS NULL
                               THEN 1 ELSE 0
                           END), 0) AS Missing_Rate_Count
                    FROM VAS_Opportunity o
                    INNER JOIN schema_currency sc ON (sc.AD_Client_ID=o.AD_Client_ID)
                    LEFT OUTER JOIN C_BPartner bp ON (bp.C_BPartner_ID=" + CustomerIdExpr + @" AND bp.AD_Client_ID=o.AD_Client_ID AND bp.IsActive = 'Y')
                    LEFT OUTER JOIN C_Lead ld ON (ld.C_Lead_ID=o.C_Lead_ID AND ld.AD_Client_ID=o.AD_Client_ID AND ld.IsActive = 'Y')
                    WHERE o.IsActive = 'Y'
                      AND o.AD_Client_ID = @Client_ID
                      AND " + OpenStagePredicate + @"
                      AND o.Ref_Order_ID IS NULL
                      AND o.C_Order_ID IS NULL
                      AND (bp.C_BPartner_ID IS NOT NULL OR ld.C_Lead_ID IS NOT NULL)";

                // MRole supplies tenant + organization + record access on the main
                // physical table alias "o" (CTE rule: main data source only, not the
                // secondary currency CTE join).
                pipelineSql = MRole.GetDefault(ctx).AddAccessSQL(
                    pipelineSql,
                    "o",
                    MRole.SQL_FULLYQUALIFIED,
                    MRole.SQL_RO
                );

                string sql = @"
                    WITH schema_currency AS (
                        " + schemaCurrencySql + @"
                    ),
                    pipeline AS (
                        " + pipelineSql + @"
                    )
                    SELECT ROUND(pl.Pipeline_Value, sc.Std_Precision) AS Pipeline_Value,
                           pl.Customer_Count AS Customer_Count,
                           pl.Opportunity_Count AS Opportunity_Count,
                           pl.Missing_Rate_Count AS Missing_Rate_Count,
                           sc.Cur_Symbol AS Cur_Symbol,
                           sc.ISO_Code AS ISO_Code,
                           sc.Std_Precision AS Std_Precision
                    FROM schema_currency sc
                    CROSS JOIN pipeline pl";

                decimal pipelineValue = 0;
                int customerCount = 0;
                int opportunityCount = 0;
                int missingRateCount = 0;
                string currencySymbol = "";
                string isoCode = "";
                int stdPrecision = 2;

                IDataReader dr = null;
                try
                {
                    dr = DB.ExecuteReader(
                        sql,
                        new SqlParameter[]
                        {
                            new SqlParameter("@Client_ID", ctx.GetAD_Client_ID()),
                            new SqlParameter("@ConversionType_ID", conversionTypeId)
                        }
                    );

                    if (dr != null && dr.Read())
                    {
                        pipelineValue = Util.GetValueOfDecimal(dr["Pipeline_Value"]);
                        customerCount = Util.GetValueOfInt(dr["Customer_Count"]);
                        opportunityCount = Util.GetValueOfInt(dr["Opportunity_Count"]);
                        missingRateCount = Util.GetValueOfInt(dr["Missing_Rate_Count"]);
                        currencySymbol = Util.GetValueOfString(dr["Cur_Symbol"]);
                        isoCode = Util.GetValueOfString(dr["ISO_Code"]);
                        if (dr["Std_Precision"] != null && dr["Std_Precision"] != DBNull.Value)
                        {
                            stdPrecision = Util.GetValueOfInt(dr["Std_Precision"]);
                        }
                    }
                }
                finally
                {
                    if (dr != null)
                    {
                        dr.Close();
                        dr.Dispose();
                    }
                }

                var result = new
                {
                    pipeline_value = pipelineValue,
                    customer_count = customerCount,
                    opportunity_count = opportunityCount,
                    currency_symbol = currencySymbol,
                    currency_iso = isoCode,
                    std_precision = stdPrecision,
                    // A missing rate on any non-zero amount makes the total partial;
                    // the client then shows a neutral "rate unavailable" state.
                    data_complete = missingRateCount == 0
                };

                return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_125_OpenPipelineWidget.GetOpenPipeline", ex);
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }
    }
}
