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
    public class PaymentMethodsController : Controller
    {
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetPaymentMethods()
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

                bool hasPaymentMethod = HasPaymentMethodColumn();

                string paymentMethodSelect = hasPaymentMethod
                    ? "pm.Name"
                    : "p.PaymentRule";

                string paymentMethodJoin = hasPaymentMethod
                    ? " LEFT OUTER JOIN VA009_PaymentMethod pm ON (p.VA009_PaymentMethod_ID=pm.VA009_PaymentMethod_ID) "
                    : string.Empty;

                string baseSql = @"
                    SELECT
                        " + paymentMethodSelect + @" AS PaymentMethodName,
                        COUNT(1) AS PaymentCount,
                        SUM(p.PayAmt) AS PaymentAmount
                    FROM C_Payment p
                    " + paymentMethodJoin + @"
                    WHERE p.IsActive='Y'
                    AND p.IsReceipt=@IsReceipt
                    AND p.DocStatus IN ('CO', 'CL')
                    AND p.DateAcct>=@DateFrom
                    AND p.DateAcct<@DateTo
                ";

                baseSql = MRole.GetDefault(ctx).AddAccessSQL(baseSql, "p", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                string sql = @"
                    SELECT
                        PaymentMethodName,
                        PaymentCount,
                        PaymentAmount
                    FROM (
                        " + baseSql + @"
                        GROUP BY " + paymentMethodSelect + @"
                    ) x
                    ORDER BY PaymentAmount DESC
                ";

                List<SqlParameter> parameters = new List<SqlParameter>
                {
                    new SqlParameter("@IsReceipt", "N"),
                    new SqlParameter("@DateFrom", dateFrom),
                    new SqlParameter("@DateTo", dateTo)
                };

                dr = DB.ExecuteReader(sql, parameters.ToArray());

                List<PaymentMethodSummary> rows = new List<PaymentMethodSummary>();
                decimal totalAmount = 0;

                while (dr.Read())
                {
                    decimal paymentAmount = Util.GetValueOfDecimal(dr["PaymentAmount"]);

                    rows.Add(new PaymentMethodSummary
                    {
                        PaymentMethodName = Util.GetValueOfString(dr["PaymentMethodName"]),
                        PaymentCount = Util.GetValueOfInt(dr["PaymentCount"]),
                        PaymentAmount = paymentAmount
                    });

                    totalAmount += paymentAmount;
                }

                List<object> methods = new List<object>();

                foreach (PaymentMethodSummary row in rows)
                {
                    string paymentMethodName = row.PaymentMethodName;

                    if (string.IsNullOrEmpty(paymentMethodName))
                    {
                        paymentMethodName = GetMsg(ctx, "VAS_NotSpecified", "Not Specified");
                    }

                    decimal percentage = 0;

                    if (totalAmount > 0)
                    {
                        percentage = decimal.Round((row.PaymentAmount * 100M) / totalAmount, 2);
                    }

                    methods.Add(new
                    {
                        paymentMethodName = paymentMethodName,
                        paymentCount = row.PaymentCount,
                        paymentAmount = row.PaymentAmount,
                        percentage = percentage
                    });
                }

                return Json(new
                {
                    title = GetMsg(ctx, "VAS_PaymentMethods", "Payment methods"),
                    why = GetMsg(ctx, "VAS_Why", "WHY"),
                    description = GetMsg(ctx, "VAS_PaymentMethodWhy", "UPI is cheapest · shift sub-₹2L payments where possible"),
                    totalAmount = totalAmount,
                    dateFrom = dateFrom,
                    dateTo = dateTo.AddDays(-1),
                    methods = methods
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

        private bool HasPaymentMethodColumn()
        {
            string sql = @"
                SELECT COUNT(1)
                FROM AD_Table t
                INNER JOIN AD_Column c ON (t.AD_Table_ID=c.AD_Table_ID)
                WHERE t.TableName='C_Payment'
                AND c.ColumnName='VA009_PaymentMethod_ID'
            ";

            return Util.GetValueOfInt(DB.ExecuteScalar(sql)) > 0;
        }

        private string GetMsg(Ctx ctx, string key, string fallback)
        {
            string msg = Msg.GetMsg(ctx, key);
            return !string.IsNullOrEmpty(msg) && msg != "[" + key + "]"
                ? msg
                : fallback;
        }

        private class PaymentMethodSummary
        {
            public string PaymentMethodName { get; set; }
            public int PaymentCount { get; set; }
            public decimal PaymentAmount { get; set; }
        }
    }
}