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
    public class ExpectedThisWeekController : Controller
    {
        /// <summary>
        /// Returns upcoming AR receipt runs due in the next 7 days,
        /// grouped by due date and payment method.
        /// Compatible with Oracle and PostgreSQL.
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetExpectedThisWeek()
        {
            if (Session["ctx"] == null)
            {
                return Json(new
                {
                    error = "Session Expired"
                }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            DateTime startDate = DateTime.Today;
            DateTime endDate = startDate.AddDays(7);

            string startDateSql = DB.TO_DATE(startDate, true);
            string endDateSql = DB.TO_DATE(endDate, true);

            string schemaCurrencySql = @"
                SELECT ClientInfo.AD_Client_ID,
                       AcctSchema.C_Currency_ID AS C_Currency_ID,
                       Currency.StdPrecision
                FROM AD_ClientInfo ClientInfo
                INNER JOIN C_AcctSchema AcctSchema
                    ON ClientInfo.C_AcctSchema1_ID = AcctSchema.C_AcctSchema_ID
                INNER JOIN C_Currency Currency
                    ON AcctSchema.C_Currency_ID = Currency.C_Currency_ID";

            string upcomingRunsSql = @"
                SELECT InvoicePaySchedule.DueDate,
                       SchemaCurrency.C_Currency_ID,
                       SchemaCurrency.StdPrecision,

                       PaymentMethod.VA009_Name AS PaymentMethodName,
                       Invoice.PaymentRule AS PaymentRule,

                       COUNT(DISTINCT InvoicePaySchedule.C_InvoicePaySchedule_ID) AS PaymentCount,

                       SUM(
                           CASE
                               WHEN Invoice.C_Currency_ID = SchemaCurrency.C_Currency_ID
                               THEN COALESCE(InvoicePaySchedule.DueAmt, 0)
                               ELSE CurrencyConvert(
                                   COALESCE(InvoicePaySchedule.DueAmt, 0),
                                   Invoice.C_Currency_ID,
                                   SchemaCurrency.C_Currency_ID,
                                   Invoice.DateAcct,
                                   Invoice.C_ConversionType_ID,
                                   Invoice.AD_Client_ID,
                                   Invoice.AD_Org_ID
                               )
                           END
                       ) AS TotalPayableAmount

                FROM C_InvoicePaySchedule InvoicePaySchedule

                INNER JOIN C_Invoice Invoice
                    ON InvoicePaySchedule.C_Invoice_ID = Invoice.C_Invoice_ID

                INNER JOIN SchemaCurrency SchemaCurrency
                    ON SchemaCurrency.AD_Client_ID = Invoice.AD_Client_ID

                LEFT OUTER JOIN C_Payment Payment
                    ON Payment.C_Invoice_ID = Invoice.C_Invoice_ID
                   AND Payment.IsReceipt = 'Y'
                   AND Payment.IsActive = 'Y'
                   AND Payment.DocStatus IN ('CO', 'CL')

                LEFT OUTER JOIN VA009_PaymentMethod PaymentMethod
                    ON Payment.VA009_PaymentMethod_ID = PaymentMethod.VA009_PaymentMethod_ID
                   AND PaymentMethod.IsActive = 'Y'

                WHERE Invoice.IsSoTrx = 'Y'
                  AND Invoice.IsActive = 'Y'
                  AND Invoice.DocStatus IN ('CO', 'CL')
                  AND InvoicePaySchedule.IsActive = 'Y'
                  AND InvoicePaySchedule.VA009_IsPaid = 'N'

                  AND InvoicePaySchedule.DueDate >= " + startDateSql + @"
                  AND InvoicePaySchedule.DueDate < " + endDateSql;

            upcomingRunsSql = MRole.GetDefault(ctx).AddAccessSQL(
                upcomingRunsSql,
                "Invoice",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            upcomingRunsSql += @"

                  AND (
                      Payment.C_Payment_ID IS NULL
                      OR Payment.C_Payment_ID NOT IN (
                          SELECT PrivateAccess.Record_ID
                          FROM AD_Private_Access PrivateAccess
                          INNER JOIN AD_Table TableInfo
                              ON TableInfo.AD_Table_ID = PrivateAccess.AD_Table_ID
                          WHERE TableInfo.TableName = 'C_Payment'
                            AND PrivateAccess.AD_User_ID <> " + ctx.GetAD_User_ID() + @"
                            AND PrivateAccess.IsActive = 'Y'
                      )
                  )

                  AND (
                      PaymentMethod.VA009_PaymentMethod_ID IS NULL
                      OR PaymentMethod.VA009_PaymentMethod_ID NOT IN (
                          SELECT PrivateAccess.Record_ID
                          FROM AD_Private_Access PrivateAccess
                          INNER JOIN AD_Table TableInfo
                              ON TableInfo.AD_Table_ID = PrivateAccess.AD_Table_ID
                          WHERE TableInfo.TableName = 'VA009_PaymentMethod'
                            AND PrivateAccess.AD_User_ID <> " + ctx.GetAD_User_ID() + @"
                            AND PrivateAccess.IsActive = 'Y'
                      )
                  )

                GROUP BY InvoicePaySchedule.DueDate,
                         SchemaCurrency.C_Currency_ID,
                         SchemaCurrency.StdPrecision,
                         PaymentMethod.VA009_Name,
                         Invoice.PaymentRule";

            string sql = @"
                WITH SchemaCurrency AS (
                    " + schemaCurrencySql + @"
                ),
                UpcomingRuns AS (
                    " + upcomingRunsSql + @"
                )
                SELECT UpcomingRuns.DueDate,
                       UpcomingRuns.PaymentMethodName,
                       UpcomingRuns.PaymentRule,
                       UpcomingRuns.C_Currency_ID,
                       UpcomingRuns.PaymentCount,
                       ROUND(
                           COALESCE(UpcomingRuns.TotalPayableAmount, 0),
                           UpcomingRuns.StdPrecision
                       ) AS TotalPayableAmount
                FROM UpcomingRuns
                ORDER BY UpcomingRuns.DueDate,
                         UpcomingRuns.PaymentMethodName,
                         UpcomingRuns.PaymentRule";

            var rows = new List<object>();

            IDataReader dr = null;

            try
            {
                dr = DB.ExecuteReader(sql);

                while (dr != null && dr.Read())
                {
                    DateTime dueDate = DateTime.MinValue;

                    if (dr["DueDate"] != null && dr["DueDate"] != DBNull.Value)
                    {
                        dueDate = Convert.ToDateTime(dr["DueDate"]);
                    }

                    string paymentMethodName = "";

                    if (dr["PaymentMethodName"] != null && dr["PaymentMethodName"] != DBNull.Value)
                    {
                        paymentMethodName = dr["PaymentMethodName"].ToString();
                    }

                    if (string.IsNullOrEmpty(paymentMethodName))
                    {
                        string paymentRule = "";

                        if (dr["PaymentRule"] != null && dr["PaymentRule"] != DBNull.Value)
                        {
                            paymentRule = dr["PaymentRule"].ToString();
                        }

                        paymentMethodName = GetPaymentRuleName(paymentRule);
                    }

                    rows.Add(new
                    {
                        dueDate = dueDate == DateTime.MinValue ? "" : dueDate.ToString("yyyy-MM-dd"),
                        paymentMethodName = paymentMethodName,
                        c_Currency_ID = Util.GetValueOfInt(dr["C_Currency_ID"]),
                        paymentCount = Util.GetValueOfInt(dr["PaymentCount"]),
                        totalPayableAmount = Util.GetValueOfDecimal(dr["TotalPayableAmount"])
                    });
                }

                return Json(rows, JsonRequestBehavior.AllowGet);
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

        private string GetPaymentRuleName(string paymentRule)
        {
            if (paymentRule == "B")
            {
                return "Direct Debit";
            }

            if (paymentRule == "K")
            {
                return "Cheque";
            }

            if (paymentRule == "S")
            {
                return "Check";
            }

            if (paymentRule == "T")
            {
                return "Bank Transfer";
            }

            if (paymentRule == "P")
            {
                return "On Credit";
            }

            return "Expected";
        }
    }
}