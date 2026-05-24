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
    public class PaidThisMonthController : Controller
    {
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetPaidThisMonth()
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
                        SUM(p.PayAmt) AS PaidThisMonth,
                        MAX(p.C_Currency_ID) AS C_Currency_ID,
                        MAX(cur.ISO_Code) AS CurrencyISO,
                        MAX(cur.CurSymbol) AS CurrencySymbol
                    FROM C_Payment p
                    LEFT OUTER JOIN C_Currency cur ON (p.C_Currency_ID=cur.C_Currency_ID)
                    WHERE p.IsActive='Y'
                    AND p.IsReceipt=@IsReceipt
                    AND p.DocStatus IN ('CO', 'CL')
                    AND p.DateAcct>=@DateFrom
                    AND p.DateAcct<@DateTo
                ";

                sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "p", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                List<SqlParameter> parameters = new List<SqlParameter>
                {
                    new SqlParameter("@IsReceipt", "N"),
                    new SqlParameter("@DateFrom", dateFrom),
                    new SqlParameter("@DateTo", dateTo)
                };

                dr = DB.ExecuteReader(sql, parameters.ToArray());

                decimal paidThisMonth = 0;
                int cCurrencyId = 0;
                string currencyISO = string.Empty;
                string currencySymbol = string.Empty;

                if (dr.Read())
                {
                    paidThisMonth = Util.GetValueOfDecimal(dr["PaidThisMonth"]);
                    cCurrencyId = Util.GetValueOfInt(dr["C_Currency_ID"]);
                    currencyISO = Util.GetValueOfString(dr["CurrencyISO"]);
                    currencySymbol = Util.GetValueOfString(dr["CurrencySymbol"]);
                }

                return Json(new
                {
                    title = GetMsg(ctx, "VAS_PaidThisMonth", "Paid this month"),
                    badge = GetMsg(ctx, "VAS_Why", "WHY"),
                    description = GetMsg(ctx, "VAS_OutgoingPaymentsPostedSoFar", "Outgoing payments posted so far"),
                    paidThisMonth = paidThisMonth,
                    cCurrencyId = cCurrencyId,
                    currencyISO = currencyISO,
                    currencySymbol = currencySymbol,
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