using System.Data.SqlClient;
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
    /// Module Name : VAS_179_OpenMaterialIssuesWidget
    /// Purpose     : Supplies the KPI metric count of open/unfulfilled material issue documents.
    /// Chronological development:
    ///   AI-Dev      2026-08-02 Created
    /// </summary>
    public class VAS_179_OpenMaterialIssuesWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_179_OpenMaterialIssuesWidgetController).FullName);

// ===== NEW CODE START — currency format (agent A01, 2026-08-19) =====
        /// <summary>Returns the aggregate count of open material issue documents along with system currency info.</summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetOpenMaterialIssuesCount()
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null) { return Json("", JsonRequestBehavior.AllowGet); }

            try
            {
                int count = GetOpenMaterialIssuesCountData(ctx);
                string json = JsonConvert.SerializeObject(new
                {
                    count = count,
                    currency = GetCurrencyInfo(ctx),
                    success = true
                });
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_179_OpenMaterialIssuesWidget.GetOpenMaterialIssuesCount", ex);
                string json = JsonConvert.SerializeObject(new { error = Msg.GetMsg(ctx, "Error") ?? "Error" });
                return Json(json, JsonRequestBehavior.AllowGet);
            }
        }

        private object GetCurrencyInfo(Ctx ctx)
        {
            string iso = "";
            string symbol = "";
            int currencyId = ctx.GetContextAsInt("$C_Currency_ID");
            if (currencyId <= 0)
            {
                int client = ctx.GetAD_Client_ID();
                object val = DB.ExecuteScalar(
                    "SELECT C_Currency_ID FROM C_AcctSchema WHERE AD_Client_ID = @Client AND IsActive = 'Y' ORDER BY C_AcctSchema_ID ASC",
                    new SqlParameter[] { new SqlParameter("@Client", client) }, null);
                currencyId = Util.GetValueOfInt(val);
            }
            if (currencyId > 0)
            {
                IDataReader cdr = null;
                try
                {
                    cdr = DB.ExecuteReader(
                        "SELECT ISO_Code, CurSymbol FROM C_Currency WHERE C_Currency_ID = @Cur",
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
//        /// <summary>Returns the aggregate count of open material issue documents.</summary>
//        [AjaxAuthorizeAttribute]
//        [AjaxSessionFilterAttribute]
//        public JsonResult GetOpenMaterialIssuesCount()
//        {
//            Ctx ctx = Session["ctx"] as Ctx;
//            if (ctx == null) { return Json("", JsonRequestBehavior.AllowGet); }
//
//            try
//            {
//                int count = GetOpenMaterialIssuesCountData(ctx);
//                string json = JsonConvert.SerializeObject(new
//                {
//                    count = count,
//                    success = true
//                });
//                return Json(json, JsonRequestBehavior.AllowGet);
//            }
//            catch (Exception ex)
//            {
//                Log.Log(Level.SEVERE, "VAS_179_OpenMaterialIssuesWidget.GetOpenMaterialIssuesCount", ex);
//                string json = JsonConvert.SerializeObject(new { error = Msg.GetMsg(ctx, "Error") ?? "Error" });
//                return Json(json, JsonRequestBehavior.AllowGet);
//            }
//        }
// ----- END OLD CODE -----

        private int GetOpenMaterialIssuesCountData(Ctx ctx)
        {
            if (ctx == null) { return 0; }

            // Query M_Inventory (Physical Inventory / Internal Use / Material Issue) for open documents
            string sql = $@"
                SELECT COUNT(inv.M_Inventory_ID)
                FROM M_Inventory inv
                WHERE inv.IsActive = 'Y'
                  AND inv.DocStatus IN ('DR', 'IP', 'WC', 'IN')";

            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql,
                "inv",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            return Util.GetValueOfInt(DB.ExecuteScalar(sql, null, null));
        }
    }
}
