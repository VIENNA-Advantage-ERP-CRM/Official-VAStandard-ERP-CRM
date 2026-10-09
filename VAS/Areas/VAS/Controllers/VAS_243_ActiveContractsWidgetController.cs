/******************************************************
 * Module Name    : VAS
 * Purpose        : Service Contracts module Active Contracts KPI widget endpoint
 * chronological  : Development
 * Created Date   : 2026-09-07
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
    /// Module Name : VAS_243_ActiveContractsWidget
    /// Purpose     : Data endpoint for the 2x1 "Active contracts" KPI tile on the
    ///               Service Contracts dashboard - a single read-only aggregate:
    ///               the COUNT of "Active" contracts in the accessible portfolio
    ///               and their combined value converted to the tenant's base
    ///               (accounting-schema) currency. Live is DERIVED, not stored -
    ///               completed (co.Processed='Y' - verified against real data;
    ///               completed service contracts here aren't consistently
    ///               carrying DocStatus='CO', the same reason VAS_244 reads this
    ///               the same way), not cancelled (IsCancel='N') and not yet
    ///               ended (EndDate >= CURRENT_DATE), AND not within the
    ///               ≤90-day expiring window (DAYSBETWEEN(EndDate, CURRENT_DATE)
    ///               &gt; 90) - the same Active-band threshold
    ///               VAS_260_ContractsByStatusWidget's own "Active" band uses, so
    ///               this tile's count reconciles with that widget's Active band
    ///               rather than silently including its Expiring band too. MRole
    ///               is applied to the single physical table alias "co" only.
    ///               Not clickable; no writes.
    /// Chronological development:
    ///   VAI052      2026-09-07 Created
    ///   VAI052      2026-09-07 Live now reads co.Processed='Y' instead of
    ///                          co.DocStatus='CO' (same fix as VAS_244, verified
    ///                          against real data).
    ///   VAI052      2026-09-10 Added the same &gt;90-day DAYSBETWEEN filter
    ///                          VAS_260's Active band uses - this tile previously
    ///                          counted every live contract (Active + Expiring),
    ///                          disagreeing with VAS_260's stricter Active band on
    ///                          the same dashboard.
    /// </summary>
    public class VAS_243_ActiveContractsWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_243_ActiveContractsWidgetController).FullName);

        // Matches VAS_260_ContractsByStatusWidget's own Active-band threshold, so
        // this tile's "Active" count reconciles with that widget's Active band
        // instead of double-counting contracts VAS_260 buckets as Expiring.
        private const int ExpiringWindowDays = 90;

        /// <summary>
        /// Live count + base-currency portfolio value for the Active contracts KPI.
        /// </summary>
        /// <returns>JSON { LiveCount, LiveValueBase, CurrencyIso, CurrencySymbol, CurrencyPrecision } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetActiveContracts()
        {
            if (Session["ctx"] == null) { return Json("", JsonRequestBehavior.AllowGet); }

            Ctx ctx = Session["ctx"] as Ctx;

            try
            {
                string json = JsonConvert.SerializeObject(GetActiveContractsData(ctx));
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_243_ActiveContractsWidget.GetActiveContracts", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// The tenant's accounting-schema (base) currency - same resolution
        /// AD_ClientInfo -> C_AcctSchema1 -> C_Currency the sibling VAS_140
        /// Contracts-expiring widget already uses, so both KPIs report in the
        /// same currency.
        /// </summary>
        private static string SchemaCurrencySql()
        {
            return @"
                SELECT cs.C_Currency_ID AS Acct_Currency_ID,
                       cur.StdPrecision AS Std_Precision,
                       cur.ISO_Code     AS ISO_Code,
                       CASE WHEN cur.CurSymbol IS NOT NULL THEN cur.CurSymbol ELSE cur.ISO_Code END AS Cur_Symbol
                  FROM AD_ClientInfo ci
                  JOIN C_AcctSchema cs ON ( cs.C_AcctSchema_ID = ci.C_AcctSchema1_ID AND cs.IsActive = 'Y' )
                  JOIN C_Currency cur  ON ( cur.C_Currency_ID  = cs.C_Currency_ID    AND cur.IsActive = 'Y' )
                 WHERE ci.IsActive = 'Y'
                   AND ci.AD_Client_ID = @AD_Client_ID";
        }

        /// <summary>
        /// Resolves the count + base-currency value of the live contract set
        /// (kpi-active-contracts.queries.md §D): completed, not cancelled, not
        /// yet ended. GrandTotal is converted per-row to the base currency via
        /// CurrencyConvert before summing - never a raw cross-currency SUM.
        /// </summary>
        private ActiveContractsResult GetActiveContractsData(Ctx ctx)
        {
            ActiveContractsResult result = new ActiveContractsResult();
            if (ctx == null) { return result; }

            int clientId = ctx.GetAD_Client_ID();

            // Base currency (also carried in the response so the client formats
            // the sub-line without guessing).
            int baseCurrencyId = 0;
            IDataReader cdr = null;
            try
            {
                cdr = DB.ExecuteReader(SchemaCurrencySql(), new[] { new SqlParameter("@AD_Client_ID", clientId) });
                if (cdr != null && cdr.Read())
                {
                    baseCurrencyId = Util.GetValueOfInt(cdr["Acct_Currency_ID"]);
                    result.CurrencyIso = Util.GetValueOfString(cdr["ISO_Code"]);
                    result.CurrencySymbol = Util.GetValueOfString(cdr["Cur_Symbol"]);
                    result.CurrencyPrecision = cdr["Std_Precision"] == DBNull.Value ? 2 : Util.GetValueOfInt(cdr["Std_Precision"]);
                }
            }
            finally
            {
                CloseReader(cdr);
            }

            string sql = @"
                SELECT COUNT(co.C_Contract_ID) AS Live_Count,
                       SUM( CurrencyConvert(co.GrandTotal, co.C_Currency_ID, @BaseCurrency_ID,
                              CURRENT_DATE, co.C_ConversionType_ID, co.AD_Client_ID, co.AD_Org_ID) ) AS Live_Value_Base
                  FROM C_Contract co
                 WHERE co.AD_Client_ID = @AD_Client_ID
                   AND co.IsActive = 'Y'
                   AND co.Processed = 'Y'
                   AND co.IsCancel = 'N'
                   AND co.EndDate >= CURRENT_DATE
                   AND DAYSBETWEEN(co.EndDate, CURRENT_DATE) > @ExpiringWindowDays";

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "co", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, new[]
                {
                    new SqlParameter("@BaseCurrency_ID", baseCurrencyId),
                    new SqlParameter("@AD_Client_ID", clientId),
                    new SqlParameter("@ExpiringWindowDays", ExpiringWindowDays)
                });

                if (dr != null && dr.Read())
                {
                    result.LiveCount = Util.GetValueOfInt(dr["Live_Count"]);
                    result.LiveValueBase = dr["Live_Value_Base"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Live_Value_Base"]);
                }
            }
            finally
            {
                CloseReader(dr);
            }

            return result;
        }

        private void CloseReader(IDataReader reader)
        {
            if (reader == null) { return; }
            reader.Close();
            reader.Dispose();
        }

        private JsonResult ErrorResult(Ctx ctx)
        {
            string message = Msg.GetMsg(ctx, "Error") ?? "Error";
            string json = JsonConvert.SerializeObject(new { Error = message });
            return Json(json, JsonRequestBehavior.AllowGet);
        }

        private class ActiveContractsResult
        {
            public int LiveCount { get; set; }
            public decimal LiveValueBase { get; set; }
            public string CurrencyIso { get; set; }
            public string CurrencySymbol { get; set; }
            public int CurrencyPrecision { get; set; } = 2;
        }
    }
}
