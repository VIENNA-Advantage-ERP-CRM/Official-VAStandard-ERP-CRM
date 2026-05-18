using Newtonsoft.Json;
using System.Data;
using System.Web.Mvc;
using VAdvantage.Classes;
using VAdvantage.DataBase;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VIS.Controllers
{
    public class OutstandingSalesOrderController : Controller
    {
        /// <summary>
        /// Returns the total outstanding unpaid amount across all sales invoices,
        /// converted to the client's accounting schema currency.
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetOutstanding()
        {
            if (Session["ctx"] == null)
            {
                return Json(new { error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired" }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            string schemaCurrencySql = @"
                SELECT AD_ClientInfo.AD_Client_ID,
                       C_AcctSchema.C_Currency_ID AS Acct_Currency_ID,
                       C_Currency.StdPrecision
                FROM AD_ClientInfo AD_ClientInfo
                INNER JOIN C_AcctSchema C_AcctSchema ON (C_AcctSchema.C_AcctSchema_ID=AD_ClientInfo.C_AcctSchema1_ID)
                INNER JOIN C_Currency C_Currency ON (C_Currency.C_Currency_ID=C_AcctSchema.C_Currency_ID)
                WHERE AD_ClientInfo.AD_Client_ID=" + ctx.GetAD_Client_ID();

            string outstandingDataSql = @"
                SELECT C_Invoice.AD_Client_ID,
                       C_Invoice.AD_Org_ID,
                       CASE
                           WHEN C_Invoice.IsSOTrx='Y' AND C_Invoice.IsReturnTrx='N'
                               THEN CurrencyConvert(
                                   COALESCE(C_InvoicePaySchedule.DueAmt, 0),
                                   C_Invoice.C_Currency_ID,
                                   SchemaCurrency.Acct_Currency_ID,
                                   C_Invoice.DateAcct,
                                   C_Invoice.C_ConversionType_ID,
                                   C_Invoice.AD_Client_ID,
                                   C_Invoice.AD_Org_ID
                               )
                           WHEN C_Invoice.IsSOTrx='Y' AND C_Invoice.IsReturnTrx='Y'
                               THEN -CurrencyConvert(
                                   COALESCE(C_InvoicePaySchedule.DueAmt, 0),
                                   C_Invoice.C_Currency_ID,
                                   SchemaCurrency.Acct_Currency_ID,
                                   C_Invoice.DateAcct,
                                   C_Invoice.C_ConversionType_ID,
                                   C_Invoice.AD_Client_ID,
                                   C_Invoice.AD_Org_ID
                               )
                           ELSE 0
                       END AS OutstandingAmount,
                       SchemaCurrency.StdPrecision
                FROM C_InvoicePaySchedule C_InvoicePaySchedule
                INNER JOIN C_Invoice C_Invoice ON (C_InvoicePaySchedule.C_Invoice_ID=C_Invoice.C_Invoice_ID)
                INNER JOIN SchemaCurrency SchemaCurrency ON (SchemaCurrency.AD_Client_ID=C_Invoice.AD_Client_ID)
                WHERE C_InvoicePaySchedule.VA009_IsPaid='N'
                AND C_Invoice.DocStatus IN ('CO','CL')
                AND C_Invoice.IsSOTrx='Y'";

            /*
             * Correct MRole handling for CTE query:
             *
             * Apply MRole only on the CTE body where the main physical table exists.
             *
             * Main physical table: C_Invoice
             * Primary alias/name used here: C_Invoice
             *
             * Do not apply MRole on:
             * 1. Final combined WITH query
             * 2. CTE alias SchemaCurrency
             * 3. CTE alias OutstandingData
             * 4. Secondary table C_InvoicePaySchedule
             */
            outstandingDataSql = MRole.GetDefault(ctx).AddAccessSQL(
                outstandingDataSql,
                "C_Invoice",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            string sql = @"
                WITH SchemaCurrency AS (
                    " + schemaCurrencySql + @"
                ),
                OutstandingData AS (
                    " + outstandingDataSql + @"
                )
                SELECT ROUND(COALESCE(SUM(OutstandingData.OutstandingAmount), 0), MAX(OutstandingData.StdPrecision)) AS TotalOutstanding
                FROM OutstandingData OutstandingData";

            decimal totalOutstanding = 0;

            IDataReader dr = null;

            try
            {
                dr = DB.ExecuteReader(sql);

                if (dr != null && dr.Read())
                {
                    totalOutstanding = Util.GetValueOfDecimal(dr["TotalOutstanding"]);
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
                totalOutstanding = totalOutstanding
            };

            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }
    }
}