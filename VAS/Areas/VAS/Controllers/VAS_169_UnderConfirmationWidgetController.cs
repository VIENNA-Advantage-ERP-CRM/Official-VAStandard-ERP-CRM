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
    /// Module Name : Under Confirmation KPI Widget (Material Transfer dashboard)
    /// Purpose     : KPI = COUNT(DISTINCT M_Movement_ID) of stock transfer documents
    ///               dispatched and awaiting destination confirmation (DocStatus IN ('DP', 'UC')).
    /// ID Prefix   : VAS_169_
    /// </summary>
    public class VAS_169_UnderConfirmationWidgetController : Controller
    {
        private const string UnderConfirmationStatusInList = "'DP', 'UC'";

// ===== NEW CODE START — currency format (agent C03, 2026-08-19) =====
        /// <summary>
        /// Gets count of dispatched stock transfers awaiting receipt confirmation along with system currency info.
        /// </summary>
        /// <returns>JSON { count, currency, asOf }</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetUnderConfirmationData()
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
                SELECT COUNT(DISTINCT MMovement.M_Movement_ID) AS Under_Confirmation_Count
                FROM M_Movement MMovement
                WHERE MMovement.IsActive = 'Y'
                  AND MMovement.DocStatus IN (" + UnderConfirmationStatusInList + @")";

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
                    count = Util.GetValueOfInt(dr["Under_Confirmation_Count"]);
                }

                var result = new
                {
                    count = count,
                    currency = GetCurrencyInfo(ctx),
                    asOf = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
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
//        /// <summary>
//        /// Gets count of dispatched stock transfers awaiting receipt confirmation.
//        /// </summary>
//        /// <returns>JSON { count, asOf }</returns>
//        [AjaxAuthorizeAttribute]
//        [AjaxSessionFilterAttribute]
//        public JsonResult GetUnderConfirmationData_OLD()
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
//                SELECT COUNT(DISTINCT MMovement.M_Movement_ID) AS Under_Confirmation_Count
//                FROM M_Movement MMovement
//                WHERE MMovement.IsActive = 'Y'
//                  AND MMovement.DocStatus IN (" + UnderConfirmationStatusInList + @")";
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
//                    count = Util.GetValueOfInt(dr["Under_Confirmation_Count"]);
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
