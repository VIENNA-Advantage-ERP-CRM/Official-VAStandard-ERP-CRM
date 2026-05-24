using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Web.Mvc;
using VAdvantage.Classes;
using VAdvantage.DataBase;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VIS.Controllers
{
    public class BouncedAPPaymentController : Controller
    {
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetBouncedAPPayments()
        {
            if (Session["ctx"] == null)
            {
                return Json(new
                {
                    error = "Session Expired",
                    errorText = "Session Expired"
                }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;
            IDataReader dr = null;

            try
            {
                DateTime today = DateTime.Today;
                DateTime dateFrom = new DateTime(today.Year, today.Month, 1);
                DateTime dateTo = dateFrom.AddMonths(1);

                string sql = @"
                    SELECT
                        COUNT(1) AS BouncedPaymentCount
                    FROM C_Payment p
                    WHERE p.IsActive='Y'
                    AND p.IsReceipt=@IsReceipt
                    AND p.DocStatus=@DocStatus
                    AND p.DateAcct>=@DateFrom
                    AND p.DateAcct<@DateTo
                ";

                sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "p", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                List<SqlParameter> parameters = new List<SqlParameter>
                {
                    new SqlParameter("@IsReceipt", "N"),
                    new SqlParameter("@DocStatus", "RE"),
                    new SqlParameter("@DateFrom", dateFrom),
                    new SqlParameter("@DateTo", dateTo)
                };

                dr = DB.ExecuteReader(sql, parameters.ToArray());

                int bouncedPaymentCount = 0;

                if (dr.Read())
                {
                    bouncedPaymentCount = Util.GetValueOfInt(dr["BouncedPaymentCount"]);
                }

                return Json(new
                {
                    title = GetMsg(ctx, "VAS_Bounced", "Bounced"),
                    badge = GetMsg(ctx, "VAS_Action", "Action"),
                    description = GetMsg(ctx, "VAS_NeedReissue", "Need re-issue"),
                    bouncedPaymentCount = bouncedPaymentCount,
                    dateFrom = dateFrom,
                    dateTo = dateTo.AddDays(-1)
                }, JsonRequestBehavior.AllowGet);
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
                    dr.Dispose();
                }
            }
        }

        private string GetMsg(Ctx ctx, string key, string fallback)
        {
            string msg = Msg.GetMsg(ctx, key);
            return !string.IsNullOrEmpty(msg) && msg != "[" + key + "]"
                ? msg
                : fallback;
        }
    }
}