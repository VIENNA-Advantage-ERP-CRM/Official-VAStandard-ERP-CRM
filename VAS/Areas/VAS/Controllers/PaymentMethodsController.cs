using System;
using System.Collections.Generic;
using System.Data;
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
                bool hasPaymentMethodName = hasPaymentMethod && HasPaymentMethodNameColumn();
                bool hasPaymentMethodValue = hasPaymentMethod && HasPaymentMethodValueColumn();

                string paymentMethodSelect = "p.PaymentRule";
                string paymentMethodGroupBy = "p.PaymentRule";
                string paymentMethodJoin = string.Empty;

                if (hasPaymentMethod)
                {
                    paymentMethodJoin = @"
                    LEFT OUTER JOIN VA009_PaymentMethod pm ON (p.VA009_PaymentMethod_ID = pm.VA009_PaymentMethod_ID)";

                    if (hasPaymentMethodName)
                    {
                        paymentMethodSelect = "pm.Name";
                        paymentMethodGroupBy = "pm.Name";
                    }
                    else if (hasPaymentMethodValue)
                    {
                        paymentMethodSelect = "pm.Value";
                        paymentMethodGroupBy = "pm.Value";
                    }
                    else
                    {
                        paymentMethodSelect = "p.PaymentRule";
                        paymentMethodGroupBy = "p.PaymentRule";
                        paymentMethodJoin = string.Empty;
                    }
                }

                string dateFilter = GetDateFilter("p.DateAcct", dateFrom, dateTo);

                string baseSql = @"
                    SELECT
                        " + paymentMethodSelect + @" AS PaymentMethodName,
                        COUNT(1) AS PaymentCount,
                        SUM(p.PayAmt) AS PaymentAmount
                    FROM C_Payment p
                    " + paymentMethodJoin + @"
                    WHERE p.IsActive = 'Y'
                    AND p.IsReceipt = @IsReceipt
                    AND p.DocStatus IN ('CO', 'CL')
                    "
                    + dateFilter + @"
                ";

                baseSql = MRole.GetDefault(ctx).AddAccessSQL(baseSql, "p", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                string sql = @"
                    SELECT
                        PaymentMethodName,
                        PaymentCount,
                        PaymentAmount
                    FROM (
                        " + baseSql + @"
                        GROUP BY " + paymentMethodGroupBy + @"
                    ) x
                    ORDER BY PaymentAmount DESC
                ";

                List<System.Data.SqlClient.SqlParameter> parameters = new List<System.Data.SqlClient.SqlParameter>
                {
                    new System.Data.SqlClient.SqlParameter("@IsReceipt", "N")
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
                    dateFrom = FormatDate(dateFrom),
                    dateTo = FormatDate(dateTo.AddDays(-1)),
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
                INNER JOIN AD_Column c ON (t.AD_Table_ID = c.AD_Table_ID)
                WHERE t.TableName = 'C_Payment'
                AND c.ColumnName = 'VA009_PaymentMethod_ID'
            ";

            return Util.GetValueOfInt(DB.ExecuteScalar(sql)) > 0;
        }

        private bool HasPaymentMethodNameColumn()
        {
            string sql = @"
                SELECT COUNT(1)
                FROM AD_Table t
                INNER JOIN AD_Column c ON (t.AD_Table_ID = c.AD_Table_ID)
                WHERE t.TableName = 'VA009_PaymentMethod'
                AND c.ColumnName = 'Name'
            ";

            return Util.GetValueOfInt(DB.ExecuteScalar(sql)) > 0;
        }

        private bool HasPaymentMethodValueColumn()
        {
            string sql = @"
                SELECT COUNT(1)
                FROM AD_Table t
                INNER JOIN AD_Column c ON (t.AD_Table_ID = c.AD_Table_ID)
                WHERE t.TableName = 'VA009_PaymentMethod'
                AND c.ColumnName = 'Value'
            ";

            return Util.GetValueOfInt(DB.ExecuteScalar(sql)) > 0;
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
                AND " + columnName + @" >= DATE '" + dateFromText + @"'
                AND " + columnName + @" < DATE '" + dateToText + @"'
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

        private class PaymentMethodSummary
        {
            public string PaymentMethodName { get; set; }
            public int PaymentCount { get; set; }
            public decimal PaymentAmount { get; set; }
        }
    }
}