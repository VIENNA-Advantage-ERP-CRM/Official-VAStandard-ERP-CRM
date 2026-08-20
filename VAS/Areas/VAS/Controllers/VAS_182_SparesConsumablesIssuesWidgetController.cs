using System;
using System.Data;
using System.Web.Mvc;
using System.Data.SqlClient;
using Newtonsoft.Json;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VIS.Controllers
{
    /// <summary>
    /// Module Name : VAS_182_SparesConsumablesIssuesWidget
    /// Purpose     : Supplies the KPI metric percentage share of material issue value classified for Spares / Consumables Month-to-Date (MTD).
    /// Chronological development:
    ///   AI-Dev      2026-08-02 Created
    ///   Agent A04   2026-08-19 Added GetCurrencyInfo endpoint & currency formatting support
    /// </summary>
    public class VAS_182_SparesConsumablesIssuesWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_182_SparesConsumablesIssuesWidgetController).FullName);

// ===== NEW CODE START — currency format (agent A04, 2026-08-19) =====
        /// <summary>Returns currency info (iso code and currency symbol) for the current context.</summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetCurrencyInfo()
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null) { return Json("", JsonRequestBehavior.AllowGet); }

            try
            {
                var currencyInfo = GetCurrencyInfoData(ctx);
                string json = JsonConvert.SerializeObject(currencyInfo);
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_182_SparesConsumablesIssuesWidget.GetCurrencyInfo", ex);
                string json = JsonConvert.SerializeObject(new { iso = "", symbol = "" });
                return Json(json, JsonRequestBehavior.AllowGet);
            }
        }

        private dynamic GetCurrencyInfoData(Ctx ctx)
        {
            int currencyId = ctx.GetContextAsInt("$C_Currency_ID");
            if (currencyId <= 0)
            {
                currencyId = ctx.GetContextAsInt("#C_Currency_ID");
            }

            string iso = "";
            string symbol = "";

            if (currencyId > 0)
            {
                string sql = "SELECT ISO_Code, COALESCE(CurSymbol, ISO_Code) AS CurSymbol FROM C_Currency WHERE C_Currency_ID = @p1 AND IsActive = 'Y'";
                SqlParameter[] param = new SqlParameter[] { new SqlParameter("@p1", currencyId) };
                using (IDataReader dr = DB.ExecuteReader(sql, param, null))
                {
                    if (dr != null && dr.Read())
                    {
                        iso = Util.GetValueOfString(dr["ISO_Code"]);
                        symbol = Util.GetValueOfString(dr["CurSymbol"]);
                    }
                }
            }

            if (string.IsNullOrEmpty(iso))
            {
                string sql = @"SELECT c.ISO_Code, COALESCE(c.CurSymbol, c.ISO_Code) AS CurSymbol 
                               FROM C_AcctSchema a 
                               INNER JOIN C_Currency c ON (c.C_Currency_ID = a.C_Currency_ID) 
                               WHERE a.AD_Client_ID = @p1 AND a.IsActive = 'Y'";
                SqlParameter[] param = new SqlParameter[] { new SqlParameter("@p1", ctx.GetAD_Client_ID()) };
                using (IDataReader dr = DB.ExecuteReader(sql, param, null))
                {
                    if (dr != null && dr.Read())
                    {
                        iso = Util.GetValueOfString(dr["ISO_Code"]);
                        symbol = Util.GetValueOfString(dr["CurSymbol"]);
                    }
                }
            }

            return new { iso = iso, symbol = symbol };
        }

        /// <summary>Returns the percentage share of MTD issued value for spares/consumables purpose.</summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetSparesConsumablesPercentage()
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null) { return Json("", JsonRequestBehavior.AllowGet); }

            try
            {
                int percentage = GetSparesConsumablesPercentageData(ctx);
                string json = JsonConvert.SerializeObject(new
                {
                    percentage = percentage,
                    success = true
                });
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_182_SparesConsumablesIssuesWidget.GetSparesConsumablesPercentage", ex);
                string json = JsonConvert.SerializeObject(new { error = Msg.GetMsg(ctx, "Error") ?? "Error" });
                return Json(json, JsonRequestBehavior.AllowGet);
            }
        }

        private int GetSparesConsumablesPercentageData(Ctx ctx)
        {
            if (ctx == null) { return 0; }

            DateTime now = DateTime.Now;
            DateTime monthStart = new DateTime(now.Year, now.Month, 1);
            DateTime nextMonthStart = monthStart.AddMonths(1);
            string msl = ToSqlDate(monthStart);
            string nmsl = ToSqlDate(nextMonthStart);

            // Spares / consumables share = value of issue lines NOT raised against a work order.
            // Exact complement of VAS_181_ProductionIssuesWidget, so the two KPIs sum to 100%
            // (as the source spec intends: 61% production + 39% spares).
            //
            // The previous classification (C_Charge_ID IS NULL AND M_RequisitionLine_ID IS NULL)
            // could never be true: an internal-use line always carries a charge account, so on
            // FSMTesting6 this KPI returned a hard 0% for every period.
            //
            // Cost fallback must end in 0: NVL(CurrentCostPrice, PriceCost) yields NULL when both
            // are null, and SUM() silently drops those lines from the total.
            string sql = @"
                SELECT
                  COALESCE(SUM(CASE WHEN COALESCE(line.VA075_WorkOrder_ID, 0) = 0
                                     AND COALESCE(line.VAMFG_M_WorkOrder_ID, 0) = 0
                                    THEN (line.QtyInternalUse * COALESCE(line.CurrentCostPrice, line.PriceCost, line.VA024_CostPrice, 0))
                                    ELSE 0 END), 0) AS SparesValue,
                  COALESCE(SUM(line.QtyInternalUse * COALESCE(line.CurrentCostPrice, line.PriceCost, line.VA024_CostPrice, 0)), 0) AS TotalValue
                FROM M_InventoryLine line
                INNER JOIN M_Inventory inv ON inv.M_Inventory_ID = line.M_Inventory_ID
                WHERE inv.IsActive = 'Y'
                  AND inv.DocStatus IN ('CO', 'CL')
                  AND COALESCE(inv.IsInternalUse, 'N') = 'Y'
                  AND line.IsActive = 'Y'
                  AND COALESCE(line.QtyInternalUse, 0) > 0
                  AND inv.MovementDate >= " + msl + @"
                  AND inv.MovementDate < " + nmsl;

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "inv", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            decimal sparesVal = 0;
            decimal totalVal = 0;

            using (System.Data.IDataReader dr = DB.ExecuteReader(sql, null, null))
            {
                if (dr != null && dr.Read())
                {
                    sparesVal = Util.GetValueOfDecimal(dr["SparesValue"]);
                    totalVal = Util.GetValueOfDecimal(dr["TotalValue"]);
                }
            }

            if (totalVal <= 0) { return 0; }
            decimal pct = (sparesVal / totalVal) * 100m;
            return Convert.ToInt32(Math.Round(pct));
        }
// ----- END OLD CODE -----
    
        /// <summary>Date literal for the target DB. Merged in from upstream/beta, which
        /// introduced the msl/nmsl date-literal style this controller now uses.</summary>
        private static string ToSqlDate(DateTime date)
        {
            if (DB.IsOracle())
            {
                return "TO_DATE('" + date.ToString("yyyy-MM-dd") + "', 'YYYY-MM-DD')";
            }
            return "CAST('" + date.ToString("yyyy-MM-dd") + "' AS DATE)";
        }
}
}
