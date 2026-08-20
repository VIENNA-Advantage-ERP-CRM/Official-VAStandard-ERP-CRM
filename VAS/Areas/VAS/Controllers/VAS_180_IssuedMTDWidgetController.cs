using Newtonsoft.Json;
using System;
using System.Data;
using System.Web.Mvc;
using System.Data.SqlClient;
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
    ///   A02         2026-08-19 Added GetCurrencyInfo, parameterized dates & business rule filters
    /// </summary>
    public class VAS_180_IssuedMTDWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_180_IssuedMTDWidgetController).FullName);

// ===== NEW CODE START — currency format (agent A02, 2026-08-19) =====
        /// <summary>Returns the aggregate count of material issue lines posted month-to-date along with currency info.</summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetIssuedMTDCount()
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null) { return Json("", JsonRequestBehavior.AllowGet); }

            try
            {
                int count = GetIssuedMTDCountData(ctx);
                object currencyInfo = GetCurrencyInfo(ctx);
                string json = JsonConvert.SerializeObject(new
                {
                    count = count,
                    currency = currencyInfo,
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

        /// <summary>Standalone endpoint for retrieving organizational currency info.</summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetCurrencyInfoEndpoint()
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null) { return Json("", JsonRequestBehavior.AllowGet); }
            try
            {
                object currencyInfo = GetCurrencyInfo(ctx);
                string json = JsonConvert.SerializeObject(currencyInfo);
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_180_IssuedMTDWidget.GetCurrencyInfoEndpoint", ex);
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
                  AND line.IsActive = 'Y'
                  AND inv.IsInternalUse = 'Y'
                  AND inv.DocStatus IN ('CO', 'CL')
                  AND COALESCE(inv.IsInternalUse, 'N') = 'Y'
                  AND line.IsActive = 'Y'
                  AND COALESCE(line.QtyInternalUse, 0) > 0
                  AND inv.MovementDate >= " + msl + @"
                  AND inv.MovementDate < " + nmsl;

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "inv", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            SqlParameter[] param = new SqlParameter[]
            {
                new SqlParameter("@MonthStart", SqlDbType.DateTime) { Value = monthStart },
                new SqlParameter("@NextMonthStart", SqlDbType.DateTime) { Value = nextMonthStart }
            };

            return Util.GetValueOfInt(DB.ExecuteScalar(sql, param, null));
        }

        /// <summary>
        /// Retrieves currency information (ISO code and symbol) based on $C_Currency_ID or C_AcctSchema fallback.
        /// </summary>
        private object GetCurrencyInfo(Ctx ctx)
        {
            string iso = "";
            string symbol = "";
            int currencyId = ctx.GetContextAsInt("$C_Currency_ID");
            if (currencyId <= 0)
            {
                string fallbackSql = @"SELECT AcctSchema.C_Currency_ID
                                       FROM AD_ClientInfo ClientInfo
                                       INNER JOIN C_AcctSchema AcctSchema ON (AcctSchema.C_AcctSchema_ID = ClientInfo.C_AcctSchema1_ID AND AcctSchema.IsActive = 'Y')
                                       WHERE ClientInfo.AD_Client_ID = @AD_Client_ID";
                currencyId = Util.GetValueOfInt(DB.ExecuteScalar(fallbackSql, new SqlParameter[] { new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()) }, null));
            }

            if (currencyId > 0)
            {
                IDataReader cdr = null;
                try
                {
                    cdr = DB.ExecuteReader(
                        "SELECT ISO_Code, CurSymbol FROM C_Currency WHERE C_Currency_ID = @Cur AND IsActive = 'Y'",
                        new SqlParameter[] { new SqlParameter("@Cur", currencyId) });
                    if (cdr != null && cdr.Read())
                    {
                        iso = Util.GetValueOfString(cdr["ISO_Code"]);
                        symbol = Util.GetValueOfString(cdr["CurSymbol"]);
                    }
                }
                finally { if (cdr != null) { cdr.Close(); cdr.Dispose(); } }
            }
            return new { iso = iso, symbol = symbol };
        }
// ===== NEW CODE END — currency format =====
// ----- OLD CODE (kept for rollback, do not delete) -----
//        /// <summary>Returns the aggregate count of material issue lines posted month-to-date.</summary>
//        [AjaxAuthorizeAttribute]
//        [AjaxSessionFilterAttribute]
//        public JsonResult GetIssuedMTDCount()
//        {
//            Ctx ctx = Session["ctx"] as Ctx;
//            if (ctx == null) { return Json("", JsonRequestBehavior.AllowGet); }
//
//            try
//            {
//                int count = GetIssuedMTDCountData(ctx);
//                string json = JsonConvert.SerializeObject(new
//                {
//                    count = count,
//                    success = true
//                });
//                return Json(json, JsonRequestBehavior.AllowGet);
//            }
//            catch (Exception ex)
//            {
//                Log.Log(Level.SEVERE, "VAS_180_IssuedMTDWidget.GetIssuedMTDCount", ex);
//                string json = JsonConvert.SerializeObject(new { error = Msg.GetMsg(ctx, "Error") ?? "Error" });
//                return Json(json, JsonRequestBehavior.AllowGet);
//            }
//        }
//
//        private int GetIssuedMTDCountData(Ctx ctx)
//        {
//            if (ctx == null) { return 0; }
//
//            DateTime now = DateTime.Now;
//            DateTime monthStart = new DateTime(now.Year, now.Month, 1);
//            DateTime nextMonthStart = monthStart.AddMonths(1);
//            string msl = ToSqlDate(monthStart);
//            string nmsl = ToSqlDate(nextMonthStart);
//
//            string sql = @"
//                SELECT COUNT(line.M_InventoryLine_ID)
//                FROM M_InventoryLine line
//                INNER JOIN M_Inventory inv ON inv.M_Inventory_ID = line.M_Inventory_ID
//                WHERE inv.IsActive = 'Y'
//                  AND inv.DocStatus IN ('CO', 'CL')
//                  AND inv.MovementDate >= " + msl + @"
//                  AND inv.MovementDate < " + nmsl;
//
//            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "inv", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
//
//            return Util.GetValueOfInt(DB.ExecuteScalar(sql, null, null));
//        }
//
//        private static string ToSqlDate(DateTime date)
//        {
//            if (DB.IsOracle())
//            {
//                return "TO_DATE('" + date.ToString("yyyy-MM-dd") + "', 'YYYY-MM-DD')";
//            }
//            return "CAST('" + date.ToString("yyyy-MM-dd") + "' AS DATE)";
//        }
// ----- END OLD CODE -----
    }
}
