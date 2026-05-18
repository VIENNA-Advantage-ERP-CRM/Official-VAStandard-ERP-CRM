using Newtonsoft.Json;
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
    public class ExpectedThisWeekController : Controller
    {
        /// <summary>
        /// Returns expected AR invoice amount due in the next 7 days,
        /// converted to Accounting Schema currency.
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetExpectedThisWeek()
        {
            if (Session["ctx"] == null)
            {
                return Json(new { error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired" }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            string schemaCurrencySql = @"
                SELECT ClientInfo.AD_Client_ID,
                       AcctSchema.C_Currency_ID AS C_Currency_ID,
                       Currency.StdPrecision
                FROM AD_ClientInfo ClientInfo
                INNER JOIN C_AcctSchema AcctSchema ON (ClientInfo.C_AcctSchema1_ID=AcctSchema.C_AcctSchema_ID)
                INNER JOIN C_Currency Currency ON (AcctSchema.C_Currency_ID=Currency.C_Currency_ID)";

            string expectedThisWeekSql = @"
                SELECT SchemaCurrency.C_Currency_ID,
                       SchemaCurrency.StdPrecision,
                       SUM(
                           CASE
                               WHEN Invoice.C_Currency_ID=SchemaCurrency.C_Currency_ID THEN COALESCE(InvoicePaySchedule.DueAmt, 0)
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
                       ) AS ExpectedAmount
                FROM C_InvoicePaySchedule InvoicePaySchedule
                INNER JOIN C_Invoice Invoice ON (InvoicePaySchedule.C_Invoice_ID=Invoice.C_Invoice_ID)
                INNER JOIN SchemaCurrency SchemaCurrency ON (SchemaCurrency.AD_Client_ID=Invoice.AD_Client_ID)
                WHERE Invoice.IsSoTrx='Y'
                AND Invoice.IsActive='Y'
                AND Invoice.DocStatus IN ('CO', 'CL')
                AND InvoicePaySchedule.VA009_IsPaid='N'
                AND InvoicePaySchedule.DueDate>=TRUNC(SYSDATE)
                AND InvoicePaySchedule.DueDate<TRUNC(SYSDATE)+7";

            expectedThisWeekSql = MRole.GetDefault(ctx).AddAccessSQL(
                expectedThisWeekSql,
                "Invoice",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            expectedThisWeekSql += @"
                GROUP BY SchemaCurrency.C_Currency_ID,
                         SchemaCurrency.StdPrecision";

            string sql = @"
                WITH SchemaCurrency AS (
                    " + schemaCurrencySql + @"
                ),
                ExpectedThisWeek AS (
                    " + expectedThisWeekSql + @"
                )
                SELECT ExpectedThisWeek.C_Currency_ID,
                       ROUND(COALESCE(ExpectedThisWeek.ExpectedAmount, 0), ExpectedThisWeek.StdPrecision) AS ExpectedAmountThisWeek
                FROM ExpectedThisWeek";

            decimal expectedAmountThisWeek = 0;
            int currencyId = 0;

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql);

                if (dr != null && dr.Read())
                {
                    currencyId = Util.GetValueOfInt(dr["C_Currency_ID"]);
                    expectedAmountThisWeek = Util.GetValueOfDecimal(dr["ExpectedAmountThisWeek"]);
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
                expectedAmountThisWeek = expectedAmountThisWeek
            };

            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }
    }
}