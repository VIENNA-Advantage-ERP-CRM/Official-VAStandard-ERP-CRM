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
    /// Module Name : VAS_181_ProductionIssuesWidget
    /// Purpose     : Supplies the KPI metric percentage share of material issue value classified for Production Month-to-Date (MTD).
    /// Chronological development:
    ///   AI-Dev      2026-08-02 Created
    /// </summary>
    public class VAS_181_ProductionIssuesWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_181_ProductionIssuesWidgetController).FullName);

        /// <summary>Returns the percentage share of MTD issued value for production purpose.</summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetProductionIssuesPercentage()
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null) { return Json("", JsonRequestBehavior.AllowGet); }

            try
            {
                int percentage = GetProductionIssuesPercentageData(ctx);
                string json = JsonConvert.SerializeObject(new
                {
                    percentage = percentage,
                    success = true
                });
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_181_ProductionIssuesWidget.GetProductionIssuesPercentage", ex);
                string json = JsonConvert.SerializeObject(new { error = Msg.GetMsg(ctx, "Error") ?? "Error" });
                return Json(json, JsonRequestBehavior.AllowGet);
            }
        }

        private int GetProductionIssuesPercentageData(Ctx ctx)
        {
            if (ctx == null) { return 0; }

            DateTime now = DateTime.Now;
            DateTime monthStart = new DateTime(now.Year, now.Month, 1);
            DateTime nextMonthStart = monthStart.AddMonths(1);
            string msl = ToSqlDate(monthStart);
            string nmsl = ToSqlDate(nextMonthStart);

            string sql = @"
                SELECT 
                  COALESCE(SUM(CASE WHEN line.C_CHARGE_ID IS NOT NULL OR line.M_REQUISITIONLINE_ID IS NOT NULL THEN (line.QtyInternalUse * NVL(line.CurrentCostPrice, line.PriceCost)) ELSE 0 END), 0) AS ProductionValue,
                  COALESCE(SUM(line.QtyInternalUse * NVL(line.CurrentCostPrice, line.PriceCost)), 0) AS TotalValue
                FROM M_InventoryLine line
                INNER JOIN M_Inventory inv ON inv.M_Inventory_ID = line.M_Inventory_ID
                WHERE inv.IsActive = 'Y'
                  AND inv.DocStatus IN ('CO', 'CL')
                  AND inv.MovementDate >= " + msl + @"
                  AND inv.MovementDate < " + nmsl;

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "inv", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            decimal productionVal = 0;
            decimal totalVal = 0;

            using (System.Data.IDataReader dr = DB.ExecuteReader(sql, null, null))
            {
                if (dr != null && dr.Read())
                {
                    productionVal = Util.GetValueOfDecimal(dr["ProductionValue"]);
                    totalVal = Util.GetValueOfDecimal(dr["TotalValue"]);
                }
            }

            if (totalVal <= 0) { return 0; }
            decimal pct = (productionVal / totalVal) * 100m;
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
