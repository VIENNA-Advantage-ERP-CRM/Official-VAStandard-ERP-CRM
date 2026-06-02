using System;
using System.Data;
using System.Web.Mvc;
using VAdvantage.Classes;
using VAdvantage.DataBase;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VIS.Controllers
{
    public class VAS_027_ClearedAPPaymentWidgetController : Controller
    {
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetClearedAPPayment()
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

                DateTime dateFrom = new DateTime(today.Year, today.Month, 1).AddMonths(-1);
                DateTime dateTo = new DateTime(today.Year, today.Month, 1);

                string dateFilter = GetDateFilter("p.DateTrx", dateFrom, dateTo);

                string clearedPaymentSql = @"
                    SELECT
                        COUNT(1) AS TotalPayments,
                        SUM(CASE WHEN p.IsReconciled = 'Y' THEN 1 ELSE 0 END) AS ClearedPayments
                    FROM C_Payment p
                    WHERE p.IsActive = 'Y'
                    AND p.IsReceipt = 'N'
                    AND p.DocStatus IN ('CO', 'CL')
                " + dateFilter;

                clearedPaymentSql = MRole.GetDefault(ctx).AddAccessSQL(
                    clearedPaymentSql,
                    "p",
                    MRole.SQL_FULLYQUALIFIED,
                    MRole.SQL_RO
                );

                dr = DB.ExecuteReader(clearedPaymentSql);

                int totalPayments = 0;
                int clearedPayments = 0;
                decimal clearedPercentage = 0;

                if (dr.Read())
                {
                    totalPayments = Util.GetValueOfInt(dr["TotalPayments"]);
                    clearedPayments = Util.GetValueOfInt(dr["ClearedPayments"]);
                }

                if (totalPayments > 0)
                {
                    clearedPercentage = decimal.Round((clearedPayments * 100M) / totalPayments, 2);
                }

                return Json(new
                {
                    title = GetMsg(ctx, "VAS_Cleared", "Cleared"),
                    badge = GetMsg(ctx, "VAS_Why", "WHY"),
                    description = GetMsg(ctx, "VAS_APPaymentClearedWhy", "Of last month's AP payments reconciled"),

                    value = clearedPercentage,
                    clearedPercentage = clearedPercentage,

                    totalPayments = totalPayments,
                    clearedPayments = clearedPayments,

                    precision = 2,
                    dateFrom = FormatDate(dateFrom),
                    dateTo = FormatDate(dateTo.AddDays(-1))
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    error = ex.Message,
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

        private string GetDateFilter(string columnName, DateTime dateFrom, DateTime dateTo)
        {
            string dateFromText = FormatDate(dateFrom);
            string dateToText = FormatDate(dateTo);

            if (DB.IsOracle())
            {
                return @"
                    AND " + columnName + @" >= TO_DATE('" + dateFromText + @"', 'YYYY-MM-DD')
                    AND " + columnName + @" < TO_DATE('" + dateToText + @"', 'YYYY-MM-DD')
                ";
            }

            return @"
                AND " + columnName + @" >= '" + dateFromText + @"'
                AND " + columnName + @" < '" + dateToText + @"'
            ";
        }

        private string FormatDate(DateTime date)
        {
            return date.ToString("yyyy-MM-dd");
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
