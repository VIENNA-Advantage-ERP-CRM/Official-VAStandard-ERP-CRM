/******************************************************
 * Module Name    : VAS
 * Purpose        : Customers module High-Value Pipeline widget endpoints
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
    /// Module Name : VAS_139_HighValuePipelineWidget
    /// Purpose     : 3x2 ranked list - customers with open opportunities ordered by
    ///               total open pipeline value, each with a bar relative to the top
    ///               customer. The row modal lists the customer's opportunities with
    ///               stage, converted value and expected close, plus a reconciled
    ///               total. Open opportunity = VAS_Opportunity with VAS_OppStage IN
    ///               ('10','11','12','13','15') OR VAS_OppStage IS NULL, not yet
    ///               converted to an order (Ref_Order_ID IS NULL AND C_Order_ID IS
    ///               NULL) - the stage rule from VA061_056_OpenOpportunitiesModel, and
    ///               the same definition the VAS_125 KPI uses so the widget total
    ///               reconciles with that tile. The customer is
    ///               COALESCE(Ref_BPartner_ID, C_BPartner_ID), because VAS_Opportunity
    ///               carries the account on Ref_BPartner_ID. PlannedAmt is converted to
    ///               the tenant accounting currency via CurrencyConvert (default
    ///               conversion type, as-of the current business date) so mixed
    ///               currencies rank on one comparable base. MRole (tenant + org +
    ///               record access) is applied to the main physical table
    ///               VAS_Opportunity only.
    /// Chronological development:
    ///   VAI052      2026-07-22 Created
    ///   VAI052      2026-08-10 Source switched from C_Project (VAS_ProjectStatus
    ///                          'DR'/'IP') to VAS_Opportunity, per the open-stage rule
    ///                          in VA061_056_OpenOpportunitiesModel
    /// </summary>
    public class VAS_139_HighValuePipelineWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_139_HighValuePipelineWidgetController).FullName);

        private const int WidgetPageSize = 7;
        private const int MaxListPageSize = 25;

        /// <summary>Single-row tenant accounting (reporting) currency.</summary>
        private const string SchemaCurrencySql = @"
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

        /// <summary>
        /// Non-terminal opportunity stages, plus a NULL stage (Open / Unassigned).
        /// Mirrors OPEN_STAGE_PREDICATE in VA061_056_OpenOpportunitiesModel and the
        /// VAS_125 KPI, so this list reconciles with that tile.
        /// </summary>
        private const string OpenStagePredicate =
            "(o.VAS_OppStage IN ('10','11','12','13','15') OR o.VAS_OppStage IS NULL)";

        /// <summary>
        /// The account an opportunity belongs to. VAS_Opportunity carries it on
        /// Ref_BPartner_ID; C_BPartner_ID is only sporadically populated, so it is a
        /// fallback rather than the primary link.
        /// </summary>
        private const string CustomerIdExpr = "COALESCE(o.Ref_BPartner_ID, o.C_BPartner_ID)";

        /// <summary>
        /// Per-opportunity PlannedAmt converted to the tenant accounting currency.
        /// Same-currency rows short-circuit, as do rows with no currency at all - the
        /// column is optional on VAS_Opportunity and empty on most rows, and
        /// CurrencyConvert returns NULL for a NULL source currency. Foreign currencies
        /// use CurrencyConvert as-of the current business date with the tenant default
        /// conversion type (VAS_Opportunity carries no conversion type). Mirrors the
        /// VAS_125 KPI so totals reconcile.
        /// </summary>
        private string ConvertedAmtExpr()
        {
            string conversionDate = DB.IsPostgreSQL() ? "CURRENT_DATE" : "TRUNC(SYSDATE)";
            return @"CASE WHEN o.C_Currency_ID IS NULL OR o.C_Currency_ID = sc.Acct_Currency_ID THEN COALESCE(o.PlannedAmt, 0)
                         ELSE CurrencyConvert(COALESCE(o.PlannedAmt, 0), o.C_Currency_ID, sc.Acct_Currency_ID, "
                         + conversionDate + @", @ConversionType_ID, o.AD_Client_ID, o.AD_Org_ID) END";
        }

        /// <summary>
        /// Ranked customer rows by converted open-pipeline value (desc), paged, plus
        /// the whole-result totals (customer count, total pipeline) and per-row bar
        /// percentage relative to the top customer.
        /// </summary>
        /// <param name="offset">Zero-based paging offset.</param>
        /// <param name="limit">Page size; 7 for the widget, up to 25 for the full list.</param>
        /// <returns>JSON { items:[...], total, totalPipeline, offset, limit, currency_* } or { error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetRows(int offset = 0, int limit = WidgetPageSize)
        {
            if (Session["ctx"] == null)
            {
                return Json(new { error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired" }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            if (offset < 0) { offset = 0; }
            if (limit <= 0 || limit > MaxListPageSize) { limit = WidgetPageSize; }

            try
            {
                int conversionTypeId = MConversionType.GetDefault(ctx.GetAD_Client_ID());

                // Open opportunities with converted amount, resolved to an account.
                // An opportunity may hang off a business partner OR, before the lead is
                // converted, off C_Lead - and Ref_BPartner_ID can even point at a
                // partner row that no longer exists. Both joins are therefore LEFT, and
                // a row survives when EITHER resolves, so the opportunity count here
                // matches the Open Opportunities KPI (which needs no account at all).
                // MRole on the main physical table alias "o" (CTE rule: main data
                // source only, not the currency CTE or the account lookups).
                string openOppSql = @"
                    SELECT CASE WHEN bp.C_BPartner_ID IS NOT NULL THEN 'BP' ELSE 'LEAD' END AS Account_Type,
                           COALESCE(bp.C_BPartner_ID, 0) AS Customer_Id,
                           -- Lead id only identifies the account when there is no
                           -- partner. A partner-backed opportunity may ALSO carry the
                           -- lead it came from, and those differ per opportunity -
                           -- grouping on it would split one customer into several rows.
                           CASE WHEN bp.C_BPartner_ID IS NOT NULL THEN 0 ELSE COALESCE(ld.C_Lead_ID, 0) END AS Lead_Id,
                           COALESCE(bp.Name, ld.Name) AS Account_Name,
                           " + ConvertedAmtExpr() + @" AS Converted_Amt
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
                openOppSql = MRole.GetDefault(ctx).AddAccessSQL(openOppSql, "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                string sql = @"
                    WITH schema_currency AS (
                        " + SchemaCurrencySql + @"
                    ),
                    OpenOpportunity AS (
                        " + openOppSql + @"
                    ),
                    CustomerPipeline AS (
                        SELECT o.Account_Type AS Account_Type,
                               o.Customer_Id AS Customer_Id,
                               o.Lead_Id AS Lead_Id,
                               o.Account_Name AS Customer_Name,
                               COUNT(*) AS Open_Opp_Count,
                               COALESCE(SUM(o.Converted_Amt), 0) AS Pipeline_Value
                        FROM OpenOpportunity o
                        GROUP BY o.Account_Type, o.Customer_Id, o.Lead_Id, o.Account_Name
                    ),
                    Ranked AS (
                        SELECT cp.Account_Type AS Account_Type,
                               cp.Customer_Id AS Customer_Id,
                               cp.Lead_Id AS Lead_Id,
                               cp.Customer_Name AS Customer_Name,
                               cp.Open_Opp_Count AS Open_Opp_Count,
                               cp.Pipeline_Value AS Pipeline_Value,
                               COUNT(*) OVER () AS Total_Customers,
                               SUM(cp.Pipeline_Value) OVER () AS Total_Pipeline,
                               MAX(cp.Pipeline_Value) OVER () AS Max_Pipeline
                        FROM CustomerPipeline cp
                    )
                    SELECT r.Account_Type AS Account_Type,
                           r.Customer_Id AS Customer_Id,
                           r.Lead_Id AS Lead_Id,
                           r.Customer_Name AS Customer_Name,
                           r.Open_Opp_Count AS Open_Opp_Count,
                           ROUND(r.Pipeline_Value, sc.Std_Precision) AS Pipeline_Value,
                           r.Total_Customers AS Total_Customers,
                           ROUND(r.Total_Pipeline, sc.Std_Precision) AS Total_Pipeline,
                           CASE WHEN r.Max_Pipeline > 0
                                THEN ROUND((r.Pipeline_Value * 100.0) / r.Max_Pipeline, 0)
                                ELSE 0 END AS Bar_Percent,
                           sc.Cur_Symbol AS Cur_Symbol,
                           sc.ISO_Code AS ISO_Code,
                           sc.Std_Precision AS Std_Precision
                    FROM Ranked r
                    CROSS JOIN schema_currency sc
                    ORDER BY r.Pipeline_Value DESC,
                             r.Customer_Name ASC,
                             r.Customer_Id ASC,
                             r.Lead_Id ASC
                    OFFSET " + offset + @" ROWS FETCH NEXT " + limit + @" ROWS ONLY";

                List<object> items = new List<object>();
                int total = 0;
                decimal totalPipeline = 0;
                string currencySymbol = "", isoCode = "";
                int stdPrecision = 2;

                IDataReader dr = null;
                try
                {
                    dr = DB.ExecuteReader(sql, new SqlParameter[]
                    {
                        new SqlParameter("@Client_ID", ctx.GetAD_Client_ID()),
                        new SqlParameter("@ConversionType_ID", conversionTypeId)
                    });
                    while (dr != null && dr.Read())
                    {
                        total = Util.GetValueOfInt(dr["Total_Customers"]);
                        totalPipeline = Util.GetValueOfDecimal(dr["Total_Pipeline"]);
                        currencySymbol = Util.GetValueOfString(dr["Cur_Symbol"]);
                        isoCode = Util.GetValueOfString(dr["ISO_Code"]);
                        if (dr["Std_Precision"] != null && dr["Std_Precision"] != DBNull.Value)
                        {
                            stdPrecision = Util.GetValueOfInt(dr["Std_Precision"]);
                        }
                        items.Add(new
                        {
                            // customerId is 0 for a lead-backed account; the client uses
                            // that to suppress the "open the customer record" action,
                            // which has nothing to open.
                            customerId = Util.GetValueOfInt(dr["Customer_Id"]),
                            leadId = Util.GetValueOfInt(dr["Lead_Id"]),
                            accountType = Util.GetValueOfString(dr["Account_Type"]),
                            customerName = Util.GetValueOfString(dr["Customer_Name"]),
                            openOpps = Util.GetValueOfInt(dr["Open_Opp_Count"]),
                            pipeline = Util.GetValueOfDecimal(dr["Pipeline_Value"]),
                            barPercent = Util.GetValueOfInt(dr["Bar_Percent"])
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
                    totalPipeline = totalPipeline,
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
                Log.Log(Level.SEVERE, "VAS_139_HighValuePipelineWidget.GetRows", ex);
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// One customer's open opportunities for the detail modal - name, status,
        /// converted value and close date - ordered by value. The client sums the
        /// returned converted values into the modal total, which reconciles with the
        /// customer's row pipeline. Tenant/org/record access is reapplied here even
        /// though the id comes from the client.
        /// </summary>
        /// <param name="C_BPartner_ID">Customer selected from a row; 0 for a lead row.</param>
        /// <param name="C_Lead_ID">Lead selected from a row, when the account is a lead
        /// rather than a business partner.</param>
        /// <returns>JSON { opportunities:[...], currency_* } or { error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetOpportunities(int C_BPartner_ID, int C_Lead_ID = 0)
        {
            if (Session["ctx"] == null)
            {
                return Json(new { error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired" }, JsonRequestBehavior.AllowGet);
            }

            // Exactly one of the two identifies the account the row was built from.
            if (C_BPartner_ID <= 0 && C_Lead_ID <= 0)
            {
                return Json(new { error = "Invalid customer" }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            try
            {
                int conversionTypeId = MConversionType.GetDefault(ctx.GetAD_Client_ID());

                // Match the account the row was grouped under. A business partner takes
                // precedence, exactly as in GetRows, so an opportunity that carries both
                // is not returned twice.
                // NOT EXISTS rather than NOT IN: the partner id may be NULL (lead-only
                // opportunity) or dangling, and NOT IN yields UNKNOWN on a NULL left
                // side, which would silently drop the row.
                bool byPartner = C_BPartner_ID > 0;
                string accountFilter = byPartner
                    ? CustomerIdExpr + " = @Account_ID"
                    : @"(o.C_Lead_ID = @Account_ID
                         AND NOT EXISTS (SELECT 1 FROM C_BPartner bp2
                                         WHERE bp2.C_BPartner_ID=" + CustomerIdExpr + @"
                                           AND bp2.AD_Client_ID=o.AD_Client_ID
                                           AND bp2.IsActive = 'Y'))";

                // Per-opportunity projection for this customer. MRole on "o".
                // VAS_DecisionDate is the expected close; VAS_OppStage the stage code.
                string oppRowsSql = @"
                    SELECT o.VAS_Opportunity_ID AS Opp_Id,
                           o.Value AS Search_Key,
                           o.Name AS Opp_Name,
                           COALESCE(owner.Name, '') AS Owner_Name,
                           o.VAS_OppStage AS Status_Code,
                           ROUND(" + ConvertedAmtExpr() + @", sc.Std_Precision) AS Value_Conv,
                           o.VAS_DecisionDate AS Close_Date,
                           sc.Cur_Symbol AS Cur_Symbol,
                           sc.ISO_Code AS ISO_Code,
                           sc.Std_Precision AS Std_Precision
                    FROM VAS_Opportunity o
                    INNER JOIN schema_currency sc ON (sc.AD_Client_ID=o.AD_Client_ID)
                    LEFT OUTER JOIN AD_User owner ON (owner.AD_User_ID=o.SalesRep_ID AND owner.AD_Client_ID = @Client_ID AND owner.IsActive = 'Y')
                    WHERE o.IsActive = 'Y'
                      AND o.AD_Client_ID = @Client_ID
                      AND " + accountFilter + @"
                      AND " + OpenStagePredicate + @"
                      AND o.Ref_Order_ID IS NULL
                      AND o.C_Order_ID IS NULL";
                oppRowsSql = MRole.GetDefault(ctx).AddAccessSQL(oppRowsSql, "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                string sql = @"
                    WITH schema_currency AS (
                        " + SchemaCurrencySql + @"
                    ),
                    opp_rows AS (
                        " + oppRowsSql + @"
                    )
                    SELECT orr.Opp_Id,
                           orr.Search_Key,
                           orr.Opp_Name,
                           orr.Owner_Name,
                           orr.Status_Code,
                           orr.Value_Conv,
                           orr.Close_Date,
                           orr.Cur_Symbol,
                           orr.ISO_Code,
                           orr.Std_Precision
                    FROM opp_rows orr
                    ORDER BY orr.Value_Conv DESC,
                             orr.Opp_Name ASC,
                             orr.Opp_Id ASC";

                List<object> opportunities = new List<object>();
                string currencySymbol = "", isoCode = "";
                int stdPrecision = 2;

                IDataReader dr = null;
                try
                {
                    dr = DB.ExecuteReader(sql, new SqlParameter[]
                    {
                        new SqlParameter("@Client_ID", ctx.GetAD_Client_ID()),
                        new SqlParameter("@Account_ID", byPartner ? C_BPartner_ID : C_Lead_ID),
                        new SqlParameter("@ConversionType_ID", conversionTypeId)
                    });
                    while (dr != null && dr.Read())
                    {
                        currencySymbol = Util.GetValueOfString(dr["Cur_Symbol"]);
                        isoCode = Util.GetValueOfString(dr["ISO_Code"]);
                        if (dr["Std_Precision"] != null && dr["Std_Precision"] != DBNull.Value)
                        {
                            stdPrecision = Util.GetValueOfInt(dr["Std_Precision"]);
                        }
                        DateTime? closeDate = dr["Close_Date"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr["Close_Date"]);
                        opportunities.Add(new
                        {
                            id = Util.GetValueOfInt(dr["Opp_Id"]),
                            searchKey = Util.GetValueOfString(dr["Search_Key"]),
                            name = Util.GetValueOfString(dr["Opp_Name"]),
                            ownerName = Util.GetValueOfString(dr["Owner_Name"]),
                            statusCode = Util.GetValueOfString(dr["Status_Code"]),
                            value = Util.GetValueOfDecimal(dr["Value_Conv"]),
                            closeDate = closeDate.HasValue ? closeDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : ""
                        });
                    }
                }
                finally
                {
                    CloseReader(dr);
                }

                var result = new
                {
                    opportunities = opportunities,
                    currency_symbol = currencySymbol,
                    currency_iso = isoCode,
                    std_precision = stdPrecision
                };
                return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_139_HighValuePipelineWidget.GetOpportunities", ex);
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
