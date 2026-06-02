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
    public class VAS_033_PaymentMethodsWidgetController : Controller
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

                string schemaCurrencySql = @"
                    SELECT ClientInfo.AD_Client_ID,
                           AcctSchema.C_Currency_ID AS C_Currency_ID,
                           Currency.StdPrecision,
                           Currency.ISO_Code AS ISO_Code,
                           CASE
                               WHEN Currency.CurSymbol IS NOT NULL THEN Currency.CurSymbol
                               ELSE Currency.ISO_Code
                           END AS Cur_Symbol
                    FROM AD_ClientInfo ClientInfo
                    INNER JOIN C_AcctSchema AcctSchema
                        ON ClientInfo.C_AcctSchema1_ID = AcctSchema.C_AcctSchema_ID
                    INNER JOIN C_Currency Currency
                        ON AcctSchema.C_Currency_ID = Currency.C_Currency_ID";

                string baseSql = @"
                    SELECT
                        " + paymentMethodSelect + @" AS PaymentMethodName,
                        COUNT(1) AS PaymentCount,
                        ROUND(
                            COALESCE(
                                SUM(
                                    CASE
                                        WHEN p.C_Currency_ID = SchemaCurrency.C_Currency_ID THEN COALESCE(p.PayAmt, 0)
                                        ELSE CurrencyConvert(
                                            COALESCE(p.PayAmt, 0),
                                            p.C_Currency_ID,
                                            SchemaCurrency.C_Currency_ID,
                                            p.DateAcct,
                                            p.C_ConversionType_ID,
                                            p.AD_Client_ID,
                                            p.AD_Org_ID
                                        )
                                    END
                                ),
                                0
                            ),
                            MAX(SchemaCurrency.StdPrecision)
                        ) AS PaymentAmount
                    FROM C_Payment p
                    INNER JOIN SchemaCurrency SchemaCurrency
                        ON SchemaCurrency.AD_Client_ID = p.AD_Client_ID
                    " + paymentMethodJoin + @"
                    WHERE p.IsActive = 'Y'
                    AND p.IsReceipt = 'N'
                    AND p.DocStatus IN ('CO', 'CL')
                    "
                    + dateFilter + @"
                ";

                baseSql = MRole.GetDefault(ctx).AddAccessSQL(
                    baseSql,
                    "p",
                    MRole.SQL_FULLYQUALIFIED,
                    MRole.SQL_RO
                );

                string paymentMethodsSql = @"
                    SELECT
                        PaymentMethodName,
                        PaymentCount,
                        PaymentAmount
                    FROM (
                        " + baseSql + @"
                        GROUP BY " + paymentMethodGroupBy + @"
                    ) x
                    ORDER BY PaymentAmount DESC";

                string sql = @"
                    WITH SchemaCurrency AS (
                        " + schemaCurrencySql + @"
                    )
                    " + paymentMethodsSql;

         
                dr = DB.ExecuteReader(sql);

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
