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
    /// Purpose     : Static 2x1 KPI tile - the total UNWEIGHTED value of active,
    ///               open opportunities (C_Project.IsOpportunity='Y') and the count
    ///               of distinct customers with at least one, in the logged-in
    ///               tenant and the organizations the role may access. Each
    ///               opportunity's PlannedAmt is converted to the tenant accounting
    ///               currency via CurrencyConvert (mixed currencies summed into one
    ///               base total, never weighted by Probability). A missing exchange
    ///               rate on a non-zero amount is flagged (data-quality) so the UI
    ///               can show a neutral "rate unavailable" state instead of a silent
    ///               partial total. Open = VAS_ProjectStatus IN ('DR','IP') (plus
    ///               'HD' only when include-hold is on), stage not '07' (Lost), and
    ///               proposal status not 'NI'/'DM'. MRole (tenant + org + record
    ///               access) is applied to the main physical table C_Project only.
    /// Chronological development:
    ///   VAI052      2026-07-21 Created
    /// </summary>
    public class VAS_125_OpenPipelineWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_125_OpenPipelineWidgetController).FullName);

        /// <summary>
        /// KPI tile data: base-currency open-pipeline total, distinct customer and
        /// opportunity counts, currency info, and a data-completeness flag.
        /// </summary>
        /// <param name="includeHold">'Y' to include VAS_ProjectStatus 'HD'; default 'N'.</param>
        /// <returns>
        /// JSON { pipeline_value, customer_count, opportunity_count, currency_symbol,
        /// currency_iso, std_precision, data_complete } or { error }.
        /// </returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetOpenPipeline(string includeHold = "N")
        {
            if (Session["ctx"] == null)
            {
                return Json(new
                {
                    error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired"
                }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            // Only 'Y' turns on Hold; any other value keeps it excluded.
            string includeHoldFlag = string.Equals(includeHold, "Y", StringComparison.OrdinalIgnoreCase) ? "Y" : "N";

            try
            {
                // Convert as of the current business date. Server-computed, not user
                // input, and dialect-correct for Oracle vs PostgreSQL.
                string conversionDate = DB.IsPostgreSQL() ? "CURRENT_DATE" : "TRUNC(SYSDATE)";

                // C_Project carries no conversion type, so resolve the tenant's
                // DEFAULT conversion type. CurrencyConvert returns NULL when the
                // conversion-type argument is NULL (every other codebase caller
                // passes a real type), which previously flagged every foreign-
                // currency opportunity as a missing rate.
                int conversionTypeId = MConversionType.GetDefault(ctx.GetAD_Client_ID());

                // Per-opportunity amount converted to the tenant accounting currency;
                // NULL only when a genuine exchange rate is missing for the pair/date.
                string convertedAmount = @"CurrencyConvert(COALESCE(p.PlannedAmt, 0), p.C_Currency_ID, sc.Acct_Currency_ID, "
                    + conversionDate + @", @ConversionType_ID, p.AD_Client_ID, p.AD_Org_ID)";

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
                string pipelineSql = @"
                    SELECT COALESCE(SUM(CASE
                               WHEN p.C_Currency_ID = sc.Acct_Currency_ID THEN COALESCE(p.PlannedAmt, 0)
                               ELSE " + convertedAmount + @"
                           END), 0) AS Pipeline_Value,
                           COUNT(DISTINCT p.C_BPartner_ID) AS Customer_Count,
                           COUNT(DISTINCT p.C_Project_ID) AS Opportunity_Count,
                           COALESCE(SUM(CASE
                               WHEN p.C_Currency_ID <> sc.Acct_Currency_ID
                                    AND COALESCE(p.PlannedAmt, 0) <> 0
                                    AND " + convertedAmount + @" IS NULL
                               THEN 1 ELSE 0
                           END), 0) AS Missing_Rate_Count
                    FROM C_Project p
                    INNER JOIN schema_currency sc ON (sc.AD_Client_ID=p.AD_Client_ID)
                    WHERE p.IsActive = 'Y'
                      AND p.C_BPartner_ID IS NOT NULL
                      AND p.AD_Client_ID = @Client_ID
                      AND (
                              p.VAS_ProjectStatus IN ('DR', 'IP')
                              OR (@Include_Hold = 'Y' AND p.VAS_ProjectStatus = 'HD')
                          )";

                // MRole supplies tenant + organization + record access on the main
                // physical table alias "p" (CTE rule: main data source only, not the
                // secondary currency CTE join).
                pipelineSql = MRole.GetDefault(ctx).AddAccessSQL(
                    pipelineSql,
                    "p",
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
                            new SqlParameter("@Include_Hold", SqlDbType.VarChar) { Value = includeHoldFlag },
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
