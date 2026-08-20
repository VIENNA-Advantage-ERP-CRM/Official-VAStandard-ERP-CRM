using Newtonsoft.Json;
using System;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Web.Mvc;
using VAdvantage.Classes;
using VAdvantage.DataBase;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VIS.Controllers
{
    /// <summary>
    /// Module Name : Open Transfers KPI Widget (Material Transfer dashboard)
    /// Purpose     : KPI = COUNT(DISTINCT M_Movement_ID) of approved stock transfer documents
    ///               awaiting dispatch (DocStatus IN ('IP', 'WC', 'IN')). Drafts ('DR')
    ///               are excluded per §4 tie-breaker rule.
    /// ID Prefix   : VAS_168_
    /// </summary>
    public class VAS_168_OpenTransfersWidgetController : Controller
    {
        // Excludes 'DR' (Drafted) per spec section 4 tie-breaker rule
        private const string OpenStatusInList = "'IP', 'WC', 'IN'";

// ===== NEW CODE START — currency format (agent C02, 2026-08-19) =====
        /// <summary>
        /// Gets organization currency information (ISO code and Symbol)
        /// </summary>
        /// <param name="ctx">Context</param>
        /// <returns>Object { iso, symbol }</returns>
        private object GetCurrencyInfo(Ctx ctx)
        {
            string iso = "";
            string symbol = "";
            int currencyId = ctx != null ? ctx.GetContextAsInt("$C_Currency_ID") : 0;
            IDataReader cdr = null;

            try
            {
                if (currencyId > 0)
                {
                    string sql = "SELECT ISO_Code, CurSymbol FROM C_Currency WHERE C_Currency_ID = @Cur";
                    SqlParameter[] param = new SqlParameter[] { new SqlParameter("@Cur", currencyId) };
                    cdr = DB.ExecuteReader(sql, param);
                    if (cdr != null && cdr.Read())
                    {
                        iso = Util.GetValueOfString(cdr["ISO_Code"]);
                        symbol = Util.GetValueOfString(cdr["CurSymbol"]);
                    }
                }
                else
                {
                    int clientId = ctx != null ? ctx.GetAD_Client_ID() : 0;
                    string sql = @"SELECT c.ISO_Code, c.CurSymbol 
                                   FROM C_AcctSchema acs 
                                   JOIN C_Currency c ON c.C_Currency_ID = acs.C_Currency_ID 
                                   WHERE acs.AD_Client_ID = @Client AND acs.IsActive = 'Y'";
                    SqlParameter[] param = new SqlParameter[] { new SqlParameter("@Client", clientId) };
                    cdr = DB.ExecuteReader(sql, param);
                    if (cdr != null && cdr.Read())
                    {
                        iso = Util.GetValueOfString(cdr["ISO_Code"]);
                        symbol = Util.GetValueOfString(cdr["CurSymbol"]);
                    }
                }
            }
            catch
            {
                // Fallback handled gracefully
            }
            finally
            {
                if (cdr != null)
                {
                    cdr.Close();
                    cdr = null;
                }
            }

            return new { iso = iso, symbol = symbol };
        }

        /// <summary>
        /// Gets count of open outbound stock transfers awaiting dispatch.
        /// </summary>
        /// <returns>JSON { count, asOf, currency }</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetOpenTransfersData()
        {
            if (Session["ctx"] == null)
            {
                return Json(new
                {
                    error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired"
                }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;
            DateTime today = DateTime.Today;

            string sql = @"
                SELECT COUNT(DISTINCT MMovement.M_Movement_ID) AS Open_Transfer_Count
                FROM M_Movement MMovement
                WHERE MMovement.IsActive = 'Y'
                  AND MMovement.DocStatus IN (" + OpenStatusInList + @")";

            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql,
                "MMovement",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            IDataReader dr = null;

            try
            {
                int count = 0;

                dr = DB.ExecuteReader(sql);
                if (dr != null && dr.Read())
                {
                    count = Util.GetValueOfInt(dr["Open_Transfer_Count"]);
                }

                var result = new
                {
                    count = count,
                    asOf = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    currency = GetCurrencyInfo(ctx)
                };

                return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    error = ex.Message
                }, JsonRequestBehavior.AllowGet);
            }
            finally
            {
                if (dr != null)
                {
                    dr.Close();
                    dr = null;
                }
            }
        }
// ===== NEW CODE END — currency format =====

// ----- OLD CODE (kept for rollback, do not delete) -----
//        /// <summary>
//        /// Gets count of open outbound stock transfers awaiting dispatch.
//        /// </summary>
//        /// <returns>JSON { count, asOf }</returns>
//        [AjaxAuthorizeAttribute]
//        [AjaxSessionFilterAttribute]
//        public JsonResult GetOpenTransfersData()
//        {
//            if (Session["ctx"] == null)
//            {
//                return Json(new
//                {
//                    error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired"
//                }, JsonRequestBehavior.AllowGet);
//            }
//
//            Ctx ctx = Session["ctx"] as Ctx;
//            DateTime today = DateTime.Today;
//
//            string sql = @"
//                SELECT COUNT(DISTINCT MMovement.M_Movement_ID) AS Open_Transfer_Count
//                FROM M_Movement MMovement
//                WHERE MMovement.IsActive = 'Y'
//                  AND MMovement.DocStatus IN (" + OpenStatusInList + @")";
//
//            sql = MRole.GetDefault(ctx).AddAccessSQL(
//                sql,
//                "MMovement",
//                MRole.SQL_FULLYQUALIFIED,
//                MRole.SQL_RO
//            );
//
//            IDataReader dr = null;
//
//            try
//            {
//                int count = 0;
//
//                dr = DB.ExecuteReader(sql);
//                if (dr != null && dr.Read())
//                {
//                    count = Util.GetValueOfInt(dr["Open_Transfer_Count"]);
//                }
//
//                var result = new
//                {
//                    count = count,
//                    asOf = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
//                };
//
//                return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
//            }
//            catch (Exception ex)
//            {
//                return Json(new
//                {
//                    error = ex.Message
//                }, JsonRequestBehavior.AllowGet);
//            }
//            finally
//            {
//                if (dr != null)
//                {
//                    dr.Close();
//                    dr = null;
//                }
//            }
//        }
// ----- END OLD CODE -----
    }
}
