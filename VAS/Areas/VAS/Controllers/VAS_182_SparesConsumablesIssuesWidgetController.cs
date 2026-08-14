using Newtonsoft.Json;
using System;
using System.Web.Mvc;
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
    /// </summary>
    public class VAS_182_SparesConsumablesIssuesWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_182_SparesConsumablesIssuesWidgetController).FullName);

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
