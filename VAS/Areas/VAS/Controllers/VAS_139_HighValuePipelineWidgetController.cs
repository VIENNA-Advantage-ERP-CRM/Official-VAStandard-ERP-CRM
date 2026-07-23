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
    ///               status, converted value and expected close, plus a reconciled
    ///               total. Open opportunity = C_Project.VAS_ProjectStatus IN
    ///               ('DR','IP') (the deployment's opportunity/pipeline stages; the
    ///               CRM columns IsOpportunity / VA051_Stage / Probability /
    ///               ExpectedSalesDate / IsSummary are not present in this schema, so
    ///               they are not used - the same definition the VAS_125 KPI uses, so
    ///               the widget total reconciles with that tile). PlannedAmt is
    ///               converted to the tenant accounting currency via CurrencyConvert
    ///               (default conversion type, as-of the current business date) so
    ///               mixed currencies rank on one comparable base. MRole (tenant +
    ///               org + record access) is applied to the main physical table
    ///               C_Project only.
    /// Chronological development:
    ///   VAI052      2026-07-22 Created
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
        /// Per-opportunity PlannedAmt converted to the tenant accounting currency.
        /// Same-currency rows short-circuit; foreign currencies use CurrencyConvert
        /// as-of the current business date with the tenant default conversion type
        /// (C_Project carries no conversion type). Mirrors the VAS_125 KPI so totals
        /// reconcile.
        /// </summary>
        private string ConvertedAmtExpr()
        {
            string conversionDate = DB.IsPostgreSQL() ? "CURRENT_DATE" : "TRUNC(SYSDATE)";
            return @"CASE WHEN p.C_Currency_ID = sc.Acct_Currency_ID THEN COALESCE(p.PlannedAmt, 0)
                         ELSE CurrencyConvert(COALESCE(p.PlannedAmt, 0), p.C_Currency_ID, sc.Acct_Currency_ID, "
                         + conversionDate + @", @ConversionType_ID, p.AD_Client_ID, p.AD_Org_ID) END";
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

                // Open opportunities with converted amount. MRole on the main physical
                // table alias "p" (CTE rule: main data source only, not the currency CTE).
                string openOppSql = @"
                    SELECT p.C_BPartner_ID AS C_BPartner_ID,
                           " + ConvertedAmtExpr() + @" AS Converted_Amt
                    FROM C_Project p
                    INNER JOIN schema_currency sc ON (sc.AD_Client_ID=p.AD_Client_ID)
                    WHERE p.IsActive = 'Y'
                      AND p.C_BPartner_ID IS NOT NULL
                      AND p.AD_Client_ID = @Client_ID
                      AND p.VAS_ProjectStatus IN ('DR', 'IP')";
                openOppSql = MRole.GetDefault(ctx).AddAccessSQL(openOppSql, "p", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                string sql = @"
                    WITH schema_currency AS (
                        " + SchemaCurrencySql + @"
                    ),
                    OpenOpportunity AS (
                        " + openOppSql + @"
                    ),
                    CustomerPipeline AS (
                        SELECT o.C_BPartner_ID AS C_BPartner_ID,
                               bp.Name AS Customer_Name,
                               COUNT(*) AS Open_Opp_Count,
                               COALESCE(SUM(o.Converted_Amt), 0) AS Pipeline_Value
                        FROM OpenOpportunity o
                        INNER JOIN C_BPartner bp ON (bp.C_BPartner_ID=o.C_BPartner_ID AND bp.AD_Client_ID = @Client_ID AND bp.IsActive = 'Y' AND bp.IsCustomer = 'Y')
                        GROUP BY o.C_BPartner_ID, bp.Name
                    ),
                    Ranked AS (
                        SELECT cp.C_BPartner_ID AS C_BPartner_ID,
                               cp.Customer_Name AS Customer_Name,
                               cp.Open_Opp_Count AS Open_Opp_Count,
                               cp.Pipeline_Value AS Pipeline_Value,
                               COUNT(*) OVER () AS Total_Customers,
                               SUM(cp.Pipeline_Value) OVER () AS Total_Pipeline,
                               MAX(cp.Pipeline_Value) OVER () AS Max_Pipeline
                        FROM CustomerPipeline cp
                    )
                    SELECT r.C_BPartner_ID AS Customer_Id,
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
                             r.C_BPartner_ID ASC
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
                            customerId = Util.GetValueOfInt(dr["Customer_Id"]),
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
        /// <param name="C_BPartner_ID">Customer selected from a row.</param>
        /// <returns>JSON { opportunities:[...], currency_* } or { error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetOpportunities(int C_BPartner_ID)
        {
            if (Session["ctx"] == null)
            {
                return Json(new { error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired" }, JsonRequestBehavior.AllowGet);
            }

            if (C_BPartner_ID <= 0)
            {
                return Json(new { error = "Invalid customer" }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            try
            {
                int conversionTypeId = MConversionType.GetDefault(ctx.GetAD_Client_ID());

                // Per-opportunity projection for this customer. MRole on "p".
                string oppRowsSql = @"
                    SELECT p.C_Project_ID AS Opp_Id,
                           p.Value AS Search_Key,
                           p.Name AS Opp_Name,
                           COALESCE(owner.Name, N'') AS Owner_Name,
                           p.VAS_ProjectStatus AS Status_Code,
                           ROUND(" + ConvertedAmtExpr() + @", sc.Std_Precision) AS Value_Conv,
                           p.DateFinish AS Close_Date,
                           sc.Cur_Symbol AS Cur_Symbol,
                           sc.ISO_Code AS ISO_Code,
                           sc.Std_Precision AS Std_Precision
                    FROM C_Project p
                    INNER JOIN schema_currency sc ON (sc.AD_Client_ID=p.AD_Client_ID)
                    LEFT OUTER JOIN AD_User owner ON (owner.AD_User_ID=p.SalesRep_ID AND owner.AD_Client_ID = @Client_ID AND owner.IsActive = 'Y')
                    WHERE p.IsActive = 'Y'
                      AND p.C_BPartner_ID = @BP_ID
                      AND p.AD_Client_ID = @Client_ID
                      AND p.VAS_ProjectStatus IN ('DR', 'IP')";
                oppRowsSql = MRole.GetDefault(ctx).AddAccessSQL(oppRowsSql, "p", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

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
                        new SqlParameter("@BP_ID", C_BPartner_ID),
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
