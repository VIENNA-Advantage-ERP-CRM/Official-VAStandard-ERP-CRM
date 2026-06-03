using System;
using System.Collections.Generic;
using System.Data;
using System.Web.Mvc;
using VAdvantage.Classes;
using VAdvantage.DataBase;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VAS.Controllers
{
    /// <summary>
    /// Module Name : VAS Dashboard
    /// Purpose     : Provides bounced AP payment KPI widget data.
    /// Chronological development:
    ///   <EmployeeCode>   Created Date
    /// </summary>
    /*
     * Labels / Message Keys
     * 1 | Bounced       | VAS_030_MessageBounced
     * 2 | Action        | VAS_030_MessageAction
     * 3 | Need re-issue | VAS_030_MessageNeedReissue
     */
    public class VAS_030_BouncedAPPaymentWidgetController : Controller
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
                int adClientId = ctx.GetAD_Client_ID();
                string executionStatusFilter = HasPaymentExecutionStatusColumn()
                    ? @"
AND COALESCE(p.VA009_ExecutionStatus,'')='B'"
                    : string.Empty;

                string sql = @"
SELECT
    COUNT(1) AS BouncedPaymentCount
FROM C_Payment p
WHERE p.IsActive='Y'
AND p.AD_Client_ID=" + adClientId + @"
AND p.IsReceipt='N'
AND p.DateAcct>=" + GetDateValue(dateFrom) + @"
AND p.DateAcct<" + GetDateValue(dateTo) + @"
" + executionStatusFilter;

                sql = MRole.GetDefault(ctx).AddAccessSQL(
                    sql,
                    "p",
                    MRole.SQL_FULLYQUALIFIED,
                    MRole.SQL_RO
                );

                dr = DB.ExecuteReader(sql);

                int bouncedPaymentCount = 0;

                if (dr != null && dr.Read())
                {
                    bouncedPaymentCount = Util.GetValueOfInt(dr["BouncedPaymentCount"]);
                }

                return Json(new
                {
                    title = GetMsg(ctx, "VAS_030_MessageBounced", "Bounced"),
                    badge = GetMsg(ctx, "VAS_030_MessageAction", "Action"),
                    description = GetMsg(ctx, "VAS_030_MessageNeedReissue", "Need re-issue"),
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

        private string GetDateValue(DateTime date)
        {
            string dateText = date.ToString("yyyy-MM-dd");

            if (DB.IsOracle())
            {
                return "TO_DATE('" + dateText + "', 'YYYY-MM-DD')";
            }

            return "'" + dateText + "'";
        }

        private bool HasPaymentExecutionStatusColumn()
        {
            string sql = @"
                SELECT COUNT(1)
                FROM AD_Table t
                INNER JOIN AD_Column c ON (t.AD_Table_ID = c.AD_Table_ID)
                WHERE t.TableName = 'C_Payment'
                AND c.ColumnName = 'VA009_ExecutionStatus'
            ";

            return Util.GetValueOfInt(DB.ExecuteScalar(sql)) > 0;
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
