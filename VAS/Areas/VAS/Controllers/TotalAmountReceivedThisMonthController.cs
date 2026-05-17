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
    public class TotalAmountReceivedThisMonthController : Controller
    {
        /// <summary>
        /// Returns total AR receipt amount for January, converted to Accounting Schema currency.
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetAmountReceivedThisMonth()
        {
            if (Session["ctx"] == null)
            {
                return Json(new { error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired" }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            DateTime startDate = new DateTime(DateTime.Now.Year, 1, 1);
            DateTime endDate = startDate.AddMonths(1);

            string schemaCurrencySql = @"
                SELECT ClientInfo.AD_Client_ID,
                       AcctSchema.C_Currency_ID AS C_Currency_ID,
                       Currency.StdPrecision
                FROM AD_ClientInfo ClientInfo
                INNER JOIN C_AcctSchema AcctSchema ON (ClientInfo.C_AcctSchema1_ID=AcctSchema.C_AcctSchema_ID)
                INNER JOIN C_Currency Currency ON (AcctSchema.C_Currency_ID=Currency.C_Currency_ID)";

            string receivedJanuarySql = @"
                SELECT SchemaCurrency.C_Currency_ID,
                       SchemaCurrency.StdPrecision,
                       SUM(
                           CASE
                               WHEN Payment.C_Currency_ID=SchemaCurrency.C_Currency_ID THEN COALESCE(Payment.PayAmt, 0)
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
                INNER JOIN SchemaCurrency SchemaCurrency ON (SchemaCurrency.AD_Client_ID=Payment.AD_Client_ID)
                WHERE Payment.IsReceipt='Y'
                AND Payment.IsActive='Y'
                AND Payment.DocStatus IN ('CO', 'CL')
                AND Payment.Posted='Y'
                AND p.DateAcct >= TRUNC(SYSDATE, 'YYYY')
                AND p.DateAcct < ADD_MONTHS(TRUNC(SYSDATE, 'YYYY'), 1)";

            receivedJanuarySql = MRole.GetDefault(ctx).AddAccessSQL(
                receivedJanuarySql,
                "Payment",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            receivedJanuarySql += @"
                GROUP BY SchemaCurrency.C_Currency_ID,
                         SchemaCurrency.StdPrecision";

            string sql = @"
                WITH SchemaCurrency AS (
                    " + schemaCurrencySql + @"
                ),
                ReceivedJanuary AS (
                    " + receivedJanuarySql + @"
                )
                SELECT ReceivedJanuary.C_Currency_ID,
                       ROUND(COALESCE(ReceivedJanuary.TotalAmountReceived, 0), ReceivedJanuary.StdPrecision) AS TotalAmountReceivedJanuary
                FROM ReceivedJanuary";

            decimal totalAmountReceivedJanuary = 0;
            int currencyId = 0;

            IDataReader dr = null;
            try
            { 
                dr = DB.ExecuteReader(sql);

                if (dr != null && dr.Read())
                {
                    currencyId = Util.GetValueOfInt(dr["C_Currency_ID"]);
                    totalAmountReceivedJanuary = Util.GetValueOfDecimal(dr["TotalAmountReceivedJanuary"]);
                }
            }
            finally
            {
                if (dr != null)
                {
                    dr.Close();
                }
            }

            var result = new
            {
                cCurrencyId = currencyId,
                totalAmountReceivedJanuary = totalAmountReceivedJanuary
            };

            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }
    }
}