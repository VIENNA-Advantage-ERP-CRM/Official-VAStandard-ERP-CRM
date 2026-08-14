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
    /// Module Name : VAS_180_IssuedMTDWidget
    /// Purpose     : Supplies the KPI metric count of material issue lines posted Month-to-Date (MTD).
    /// Chronological development:
    ///   AI-Dev      2026-08-02 Created
    /// </summary>
    public class VAS_180_IssuedMTDWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_180_IssuedMTDWidgetController).FullName);

        /// <summary>Returns the aggregate count of material issue lines posted month-to-date.</summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetIssuedMTDCount()
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null) { return Json("", JsonRequestBehavior.AllowGet); }

            try
            {
                int count = GetIssuedMTDCountData(ctx);
                string json = JsonConvert.SerializeObject(new
                {
                    count = count,
                    success = true
                });
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_180_IssuedMTDWidget.GetIssuedMTDCount", ex);
                string json = JsonConvert.SerializeObject(new { error = Msg.GetMsg(ctx, "Error") ?? "Error" });
                return Json(json, JsonRequestBehavior.AllowGet);
            }
        }

        private int GetIssuedMTDCountData(Ctx ctx)
        {
            if (ctx == null) { return 0; }

            DateTime now = DateTime.Now;
            DateTime monthStart = new DateTime(now.Year, now.Month, 1);
            DateTime nextMonthStart = monthStart.AddMonths(1);
            string msl = ToSqlDate(monthStart);
            string nmsl = ToSqlDate(nextMonthStart);

            // An "issue line" is a line on an INTERNAL USE document carrying an internal-use
            // quantity. M_Inventory also backs Physical Inventory, and M_InventoryLine also
            // carries count lines (QtyCount/QtyBook), so all three predicates below are needed
            // to match the definition used by VAS_185_InventoryUseTrendWidget. Without the
            // IsInternalUse filter this KPI counted 34 lines for the current month instead of 12.
            string sql = @"
                SELECT COUNT(line.M_InventoryLine_ID)
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

            return Util.GetValueOfInt(DB.ExecuteScalar(sql, null, null));
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
