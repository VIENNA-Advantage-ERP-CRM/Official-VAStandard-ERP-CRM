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
    public class TotalAmountReceivedThisMonthController : Controller
    {
        /// <summary>
        /// Returns total AR receipt amount received in the current month,
        /// converted to Accounting Schema currency.
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetAmountReceivedThisMonth()
        {
            if (Session["ctx"] == null)
            {
                return Json(new
                {
                    error = "Session Expired"
                }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            string schemaCurrencySql = @"
                SELECT ClientInfo.AD_Client_ID,
                       AcctSchema.C_Currency_ID AS C_Currency_ID,
                       Currency.StdPrecision
                FROM AD_ClientInfo ClientInfo
                INNER JOIN C_AcctSchema AcctSchema 
                    ON ClientInfo.C_AcctSchema1_ID = AcctSchema.C_AcctSchema_ID
                INNER JOIN C_Currency Currency 
                    ON AcctSchema.C_Currency_ID = Currency.C_Currency_ID";

            string receivedThisMonthSql = @"
                SELECT SchemaCurrency.C_Currency_ID,
                       SchemaCurrency.StdPrecision,
                       SUM(
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
                           END
                       ) AS TotalAmountReceived
                FROM C_Payment Payment
                INNER JOIN SchemaCurrency SchemaCurrency 
                    ON SchemaCurrency.AD_Client_ID = Payment.AD_Client_ID
                WHERE Payment.IsReceipt = 'Y'
                  AND Payment.IsActive = 'Y'
                  AND Payment.DocStatus IN ('CO', 'CL')
                  AND Payment.DateAcct >= TRUNC(SYSDATE, 'MM')
                  AND Payment.DateAcct < ADD_MONTHS(TRUNC(SYSDATE, 'MM'), 1)";

            receivedThisMonthSql = MRole.GetDefault(ctx).AddAccessSQL(
                receivedThisMonthSql,
                "Payment",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            receivedThisMonthSql += @"
                GROUP BY SchemaCurrency.C_Currency_ID,
                         SchemaCurrency.StdPrecision";

            string sql = @"
                WITH SchemaCurrency AS (
                    " + schemaCurrencySql + @"
                ),
                ReceivedThisMonth AS (
                    " + receivedThisMonthSql + @"
                )
                SELECT ReceivedThisMonth.C_Currency_ID,
                       ROUND(
                           COALESCE(ReceivedThisMonth.TotalAmountReceived, 0),
                           ReceivedThisMonth.StdPrecision
                       ) AS TotalAmountReceivedThisMonth
                FROM ReceivedThisMonth";

            decimal totalAmountReceivedThisMonth = 0;
            int currencyId = 0;

            IDataReader dr = null;

            try
            {
                dr = DB.ExecuteReader(sql);

                if (dr != null && dr.Read())
                {
                    currencyId = Util.GetValueOfInt(dr["C_Currency_ID"]);
                    totalAmountReceivedThisMonth = Util.GetValueOfDecimal(dr["TotalAmountReceivedThisMonth"]);
                }

                return Json(new
                {
                    cCurrencyId = currencyId,
                    totalAmountReceivedThisMonth = totalAmountReceivedThisMonth
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
    }
}