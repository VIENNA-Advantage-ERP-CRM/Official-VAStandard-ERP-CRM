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
    public class CustomerPaidMethodController : Controller
    {
        /// <summary>
        /// Returns AR receipt payment method distribution by paid amount.
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetCustomerPaidMethod()
        {
            if (Session["ctx"] == null)
            {
                return Json(new
                {
                    error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired"
                }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            string paymentMethodSql = @"
                SELECT CASE
                           WHEN PaymentMethod.VA009_Name IS NOT NULL THEN PaymentMethod.VA009_Name
                           WHEN Payment.TenderType = 'K' THEN TO_NCHAR('Cheque')
                           WHEN Payment.TenderType = 'C' THEN TO_NCHAR('Card')
                           WHEN Payment.TenderType = 'A' THEN TO_NCHAR('ACH')
                           WHEN Payment.TenderType = 'D' THEN TO_NCHAR('Direct Debit')
                           WHEN Payment.TenderType = 'T' THEN TO_NCHAR('Bank Transfer')
                           ELSE TO_NCHAR('Other')
                       END AS PaymentMethodName,
                       SUM(COALESCE(Payment.PayAmt, 0)) AS MethodAmount
                FROM C_Payment Payment
                LEFT OUTER JOIN VA009_PaymentMethod PaymentMethod
                    ON Payment.VA009_PaymentMethod_ID = PaymentMethod.VA009_PaymentMethod_ID
                WHERE Payment.IsReceipt = 'Y'
                  AND Payment.IsActive = 'Y'
                  AND Payment.DocStatus IN ('CO', 'CL')
                  AND Payment.Posted = 'Y'";

            paymentMethodSql = MRole.GetDefault(ctx).AddAccessSQL(
                paymentMethodSql,
                "Payment",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            paymentMethodSql += @"
                  AND (
                      PaymentMethod.VA009_PaymentMethod_ID IS NULL
                      OR PaymentMethod.VA009_PaymentMethod_ID NOT IN (
                          SELECT Record_ID
                          FROM AD_Private_Access
                          WHERE AD_Table_ID = 1000613
                            AND AD_User_ID <> " + ctx.GetAD_User_ID() + @"
                            AND IsActive = 'Y'
                      )
                  )
                GROUP BY CASE
                             WHEN PaymentMethod.VA009_Name IS NOT NULL THEN PaymentMethod.VA009_Name
                             WHEN Payment.TenderType = 'K' THEN TO_NCHAR('Cheque')
                             WHEN Payment.TenderType = 'C' THEN TO_NCHAR('Card')
                             WHEN Payment.TenderType = 'A' THEN TO_NCHAR('ACH')
                             WHEN Payment.TenderType = 'D' THEN TO_NCHAR('Direct Debit')
                             WHEN Payment.TenderType = 'T' THEN TO_NCHAR('Bank Transfer')
                             ELSE TO_NCHAR('Other')
                         END";

            string sql = @"
                WITH PaymentMethodData AS (
                    " + paymentMethodSql + @"
                ),
                TotalData AS (
                    SELECT SUM(MethodAmount) AS TotalAmount
                    FROM PaymentMethodData
                )
                SELECT PaymentMethodData.PaymentMethodName,
                       PaymentMethodData.MethodAmount,
                       CASE
                           WHEN COALESCE(TotalData.TotalAmount, 0) = 0 THEN 0
                           ELSE ROUND((PaymentMethodData.MethodAmount * 100.0) / TotalData.TotalAmount, 0)
                       END AS PaymentMethodPercent
                FROM PaymentMethodData
                CROSS JOIN TotalData
                ORDER BY PaymentMethodPercent DESC";

            var rows = new List<object>();

            IDataReader dr = null;

            try
            {
                dr = DB.ExecuteReader(sql);

                while (dr != null && dr.Read())
                {
                    rows.Add(new
                    {
                        paymentMethodName = dr["PaymentMethodName"] == null ? "" : dr["PaymentMethodName"].ToString(),
                        methodAmount = Util.GetValueOfDecimal(dr["MethodAmount"]),
                        paymentMethodPercent = Util.GetValueOfDecimal(dr["PaymentMethodPercent"])
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
    }
}