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
    /// <summary>
    /// Module Name : VAS Dashboard
    /// Purpose     : Provides bounced AP payment KPI widget data.
    /// Chronological development:
    ///   <EmployeeCode>   Created Date
    /// </summary>
    public class BouncedAPPaymentController : Controller
    {
        /// <summary>
        /// Gets outgoing AP payments marked as bounced during the current calendar month.
        /// </summary>
        /// <returns>Bounced AP payment count and reporting date range.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetBouncedAPPayments()
        {
            if (Session["ctx"] == null)
            {
                return Json(new
                {
                    error = true,
                    errorText = "Session Expired"
                }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            if (ctx == null)
            {
                return Json(new
                {
                    error = true,
                    errorText = "Session Expired"
                }, JsonRequestBehavior.AllowGet);
            }

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
AND p.AD_Client_ID=@AD_Client_ID
AND p.IsReceipt=@IsReceipt
AND p.DateAcct>=@DateFrom
AND p.DateAcct<@DateTo
AND COALESCE(p.VA009_ExecutionStatus,'')=@BouncedStatus";

                sql = MRole.GetDefault(ctx).AddAccessSQL(
                    sql,
                    "p",
                    MRole.SQL_FULLYQUALIFIED,
                    MRole.SQL_RO
                );

                List<SqlParameter> parameters = new List<SqlParameter>
                {
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                    new SqlParameter("@IsReceipt", "N"),
                    new SqlParameter("@DateFrom", dateFrom),
                    new SqlParameter("@DateTo", dateTo),
                    new SqlParameter("@BouncedStatus", "B")
                };

                dr = DB.ExecuteReader(sql, parameters.ToArray());

                int bouncedPaymentCount = 0;

                if (dr != null && dr.Read())
                {
                    bouncedPaymentCount = Util.GetValueOfInt(dr["BouncedPaymentCount"]);
                }

                return Json(new
                {
                    title = GetMsg(ctx, "VAS_Bounced", "Bounced"),
                    badge = GetMsg(ctx, "VAS_Action", "Action"),
                    description = GetMsg(ctx, "VAS_NeedReissue", "Need re-issue"),
                    value = bouncedPaymentCount,
                    bouncedPaymentCount = bouncedPaymentCount,
                    dateFrom = FormatDate(dateFrom),
                    dateTo = FormatDate(dateTo.AddDays(-1))
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    error = true,
                    errorText = ex.Message
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

        /// <summary>
        /// Gets translated message text by key with fallback.
        /// </summary>
        /// <param name="ctx">User context.</param>
        /// <param name="key">Message key.</param>
        /// <param name="fallback">Fallback text.</param>
        /// <returns>Translated or fallback message text.</returns>
        private string GetMsg(Ctx ctx, string key, string fallback)
        {
            string msg = Msg.GetMsg(ctx, key);

            return !string.IsNullOrEmpty(msg) && msg != "[" + key + "]"
                ? msg
                : fallback;
        }

        /// <summary>
        /// Formats date values returned to the widget.
        /// </summary>
        /// <param name="date">Date value.</param>
        /// <returns>Date formatted as yyyy-MM-dd.</returns>
        private string FormatDate(DateTime date)
        {
            return date.ToString("yyyy-MM-dd");
        }
    }
}