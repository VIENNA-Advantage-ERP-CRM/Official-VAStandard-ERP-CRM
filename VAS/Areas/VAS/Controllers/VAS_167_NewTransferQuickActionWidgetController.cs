using Newtonsoft.Json;
using System;
using System.Data;
using System.Data.SqlClient;
using System.Web.Mvc;
using VAdvantage.Classes;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Utility;
using VIS.Filters;

namespace VIS.Controllers
{
    /// <summary>
    /// Module Name : New Transfer Quick Action Widget (Material Transfer dashboard)
    /// Purpose     : Quick Action 1x1 entry point tile launching the New Stock Transfer creation flow.
    /// ID Prefix   : VAS_167_
    /// </summary>
    public class VAS_167_NewTransferQuickActionWidgetController : Controller
    {
        // ===== NEW CODE START — currency format (agent C01, 2026-08-19) =====
        /// <summary>
        /// Retrieves currency information (ISO code and symbol) based on context currency or default accounting schema.
        /// </summary>
        /// <param name="ctx">Context</param>
        /// <returns>Object with iso and symbol</returns>
        private object GetCurrencyInfo(Ctx ctx)
        {
            string iso = "";
            string symbol = "";
            int currencyId = ctx.GetContextAsInt("$C_Currency_ID");
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
                catch (Exception ex)
                {
                    VLogger.Get().Severe("Error fetching currency info by C_Currency_ID: " + ex.Message);
                }
                finally
                {
                    if (cdr != null) { cdr.Close(); cdr.Dispose(); }
                }
            }

            if (string.IsNullOrEmpty(iso))
            {
                IDataReader cdr = null;
                try
                {
                    cdr = DB.ExecuteReader(
                        "SELECT c.ISO_Code, c.CurSymbol FROM C_AcctSchema acs JOIN C_Currency c ON (c.C_Currency_ID = acs.C_Currency_ID) WHERE acs.AD_Client_ID = @Client AND acs.IsActive = 'Y'",
                        new SqlParameter[] { new SqlParameter("@Client", ctx.GetAD_Client_ID()) });
                    if (cdr != null && cdr.Read())
                    {
                        iso = Util.GetValueOfString(cdr["ISO_Code"]);
                        symbol = Util.GetValueOfString(cdr["CurSymbol"]);
                    }
                }
                catch (Exception ex)
                {
                    VLogger.Get().Severe("Error fetching currency info by AcctSchema: " + ex.Message);
                }
                finally
                {
                    if (cdr != null) { cdr.Close(); cdr.Dispose(); }
                }
            }

            return new { iso = iso, symbol = symbol };
        }

        /// <summary>
        /// Endpoint payload to fetch standalone currency information.
        /// </summary>
        /// <returns>JSON result</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetCurrencyData()
        {
            if (Session["ctx"] == null)
            {
                return Json(new { error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired" }, JsonRequestBehavior.AllowGet);
            }
            Ctx ctx = Session["ctx"] as Ctx;
            return Json(JsonConvert.SerializeObject(GetCurrencyInfo(ctx)), JsonRequestBehavior.AllowGet);
        }
        // ===== NEW CODE END — currency format =====

        /// <summary>
        /// Gets default creation context (window ID for Material Transfer / M_Movement).
        /// </summary>
        /// <returns>JSON { windowId, currency }</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetWindowContext()
        {
            if (Session["ctx"] == null)
            {
                return Json(new
                {
                    error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired"
                }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            // Fetch Material Transfer AD_Window_ID (default 256 or lookup by window name)
            int windowId = Util.GetValueOfInt(DB.ExecuteScalar("SELECT AD_Window_ID FROM AD_Window WHERE (Name = 'Material Transfer' OR Name = 'M_Movement' OR Name = 'Movements') AND IsActive = 'Y' ORDER BY AD_Window_ID ASC", null, null));

            // ===== NEW CODE START — currency format (agent C01, 2026-08-19) =====
            var result = new
            {
                windowId = windowId > 0 ? windowId : 256,
                currency = GetCurrencyInfo(ctx)
            };
            // ===== NEW CODE END — currency format =====

            // ----- OLD CODE (kept for rollback, do not delete) -----
            // var result = new
            // {
            //     windowId = windowId > 0 ? windowId : 256
            // };
            // ----- END OLD CODE -----

            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }
    }
}
