using Newtonsoft.Json;
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
                return Json(new { error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired" }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            string paymentMethodSql = @"
                SELECT CASE
                           WHEN PaymentMethod.Name IS NOT NULL THEN PaymentMethod.Name
                           WHEN Payment.TenderType='K' THEN 'Cheque'
                           WHEN Payment.TenderType='C' THEN 'Card'
                           WHEN Payment.TenderType='A' THEN 'ACH'
                           WHEN Payment.TenderType='D' THEN 'Direct Debit'
                           WHEN Payment.TenderType='T' THEN 'Bank Transfer'
                           ELSE 'Other'
                       END AS PaymentMethodName,
                       SUM(COALESCE(Payment.PayAmt, 0)) AS MethodAmount
                FROM C_Payment Payment
                LEFT OUTER JOIN VA009_PaymentMethod PaymentMethod ON (Payment.VA009_PaymentMethod_ID=PaymentMethod.VA009_PaymentMethod_ID)
                WHERE Payment.IsReceipt='Y'
                AND Payment.IsActive='Y'
                AND Payment.DocStatus IN ('CO', 'CL')
                AND Payment.Posted='Y'";

            paymentMethodSql = MRole.GetDefault(ctx).AddAccessSQL(
                paymentMethodSql,
                "Payment",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            paymentMethodSql += @"
                GROUP BY CASE
                             WHEN PaymentMethod.Name IS NOT NULL THEN PaymentMethod.Name
                             WHEN Payment.TenderType='K' THEN 'Cheque'
                             WHEN Payment.TenderType='C' THEN 'Card'
                             WHEN Payment.TenderType='A' THEN 'ACH'
                             WHEN Payment.TenderType='D' THEN 'Direct Debit'
                             WHEN Payment.TenderType='T' THEN 'Bank Transfer'
                             ELSE 'Other'
                         END";

            string sql = @"
                WITH PaymentMethodData AS (
                    " + paymentMethodSql + @"
                ),
                TotalData AS (
                    SELECT SUM(PaymentMethodData.MethodAmount) AS TotalAmount
                    FROM PaymentMethodData
                )
                SELECT PaymentMethodData.PaymentMethodName,
                       PaymentMethodData.MethodAmount,
                       CASE
                           WHEN COALESCE(TotalData.TotalAmount, 0)=0 THEN 0
                           ELSE ROUND((PaymentMethodData.MethodAmount*100.0)/TotalData.TotalAmount, 0)
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
            }
            finally
            {
                if (dr != null)
                {
                    dr.Close();
                }
            }

            return Json(JsonConvert.SerializeObject(rows), JsonRequestBehavior.AllowGet);
        }
    }
}