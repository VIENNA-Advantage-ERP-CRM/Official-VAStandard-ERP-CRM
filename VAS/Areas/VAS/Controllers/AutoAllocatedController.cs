using Newtonsoft.Json;
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
    public class AutoAllocatedController : Controller
    {
        /// <summary>
        /// Returns AR receipts allocated to invoices for the current month.
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetAutoAllocated()
        {
            if (Session["ctx"] == null)
            {
                return Json(new
                {
                    error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired"
                }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            DateTime monthStart = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            DateTime nextMonthStart = monthStart.AddMonths(1);



            /*
             * IMPORTANT:
             * This SQL body is only the CTE body.
             * MRole must be applied here, on the main physical table alias: Payment.
             * Do NOT apply MRole to the final WITH query.
             * Do NOT apply MRole to the CTE alias: AllocatedReceiptsThisMonth.
             */
            string allocatedReceiptsCteBodySql = @"
                SELECT Payment.C_Payment_ID,
                       SchemaCurrency.C_Currency_ID,
                       SchemaCurrency.StdPrecision,

                       CASE
                           WHEN Payment.C_Currency_ID = SchemaCurrency.C_Currency_ID
                           THEN COALESCE(Payment.PayAmt, 0)
                           ELSE CurrencyConvert(
                               COALESCE(Payment.PayAmt, 0),
                               Payment.C_Currency_ID,
                               SchemaCurrency.C_Currency_ID,
                               Payment.DateAcct,
                               Payment.C_ConversionType_ID,
                               Payment.AD_Client_ID,
                               Payment.AD_Org_ID
                           )
                       END AS ReceiptAmount

                FROM C_Payment Payment
                INNER JOIN SchemaCurrency SchemaCurrency
                    ON SchemaCurrency.AD_Client_ID = Payment.AD_Client_ID

                WHERE Payment.IsReceipt = 'Y'
                  AND Payment.IsActive = 'Y'
                  AND Payment.DocStatus IN ('CO', 'CL')

                  AND " + WidgetDateSqlHelper.TruncColumn("Payment.Created") + @" >= " + WidgetDateSqlHelper.ToSqlDate(monthStart) + @"
                  AND " + WidgetDateSqlHelper.TruncColumn("Payment.Created") + @" < " + WidgetDateSqlHelper.ToSqlDate(nextMonthStart) + @"

                  AND Payment.C_Invoice_ID IS NOT NULL ";

            allocatedReceiptsCteBodySql = MRole.GetDefault(ctx).AddAccessSQL(
                allocatedReceiptsCteBodySql,
                "Payment",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            string sql = @"
                WITH SchemaCurrency AS (
                    SELECT ClientInfo.AD_Client_ID,
                           AcctSchema.C_Currency_ID AS C_Currency_ID,
                           Currency.StdPrecision
                    FROM AD_ClientInfo ClientInfo
                    INNER JOIN C_AcctSchema AcctSchema
                        ON ClientInfo.C_AcctSchema1_ID = AcctSchema.C_AcctSchema_ID
                    INNER JOIN C_Currency Currency
                        ON AcctSchema.C_Currency_ID = Currency.C_Currency_ID
                ),

                AllocatedReceiptsThisMonth AS (
                    " + allocatedReceiptsCteBodySql + @"
                )

                SELECT AllocatedReceiptsThisMonth.C_Currency_ID,

                       COUNT(*) AS AllocatedReceiptCount,

                       ROUND(
                           SUM(AllocatedReceiptsThisMonth.ReceiptAmount),
                           AllocatedReceiptsThisMonth.StdPrecision
                       ) AS AllocatedReceiptAmount

                FROM AllocatedReceiptsThisMonth
                GROUP BY AllocatedReceiptsThisMonth.C_Currency_ID,
                         AllocatedReceiptsThisMonth.StdPrecision";

            IDataReader dr = null;

            try
            {
                dr = DB.ExecuteReader(sql);

                List<object> receipts = new List<object>();

                if (dr != null)
                {
                    while (dr.Read())
                    {
                        receipts.Add(new
                        {
                            cCurrencyId = Util.GetValueOfInt(dr["C_Currency_ID"]),
                            allocatedReceiptCount = Util.GetValueOfInt(dr["AllocatedReceiptCount"]),
                            allocatedReceiptAmount = Util.GetValueOfDecimal(dr["AllocatedReceiptAmount"])
                        });
                    }
                }

                var result = new
                {
                    monthStart = monthStart.ToString("yyyy-MM-dd"),
                    nextMonthStart = nextMonthStart.ToString("yyyy-MM-dd"),
                    allocatedReceipts = receipts
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
                    dr.Dispose();
                }
            }
        }
    }
}