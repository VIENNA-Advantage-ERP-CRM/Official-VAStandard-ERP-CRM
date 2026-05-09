using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Web.Mvc;
using VAdvantage.Classes;
using VAdvantage.DataBase;
using VAdvantage.Utility;
using VIS.Filters;

namespace VIS.Controllers
{
    public class OutstandingSalesOrderController : Controller
    {
        /// <summary>
        /// Returns the total outstanding (unpaid) amount across all sales invoices,
        /// converted to the client's accounting schema currency.
        /// Outstanding = C_InvoicePaySchedule rows where VA009_IsPaid = 'N'
        /// on completed/closed sales invoices (DocStatus IN ('CO','CL'), IsSOTrx = 'Y').
        /// Credit notes are subtracted. Amounts are converted via CurrencyConvert.
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetOutstanding()
        {
            if (Session["ctx"] == null)
                return Json(new { error = "Session Expired" }, JsonRequestBehavior.AllowGet);

            Ctx ctx = Session["ctx"] as Ctx;

            string sql = @"
                WITH schema_currency AS (
                    SELECT ci.ad_client_id,
                           cs.c_currency_id AS acct_currency_id,
                           cur.StdPrecision
                    FROM   ad_clientinfo ci
                    JOIN   c_acctschema  cs  ON cs.c_acctschema_id = ci.c_acctschema1_id
                    JOIN   c_currency    cur ON cur.c_currency_id  = cs.c_currency_id
                )
                SELECT
                    ROUND(
                        COALESCE(SUM(
                            CASE
                                WHEN i.IsSoTrx = 'Y' AND i.IsReturnTrx = 'N'
                                THEN CurrencyConvert(
                                        ips.DueAmt,
                                        i.C_Currency_ID,
                                        sc.acct_currency_id,
                                        i.DateAcct,
                                        i.C_ConversionType_ID,
                                        i.ad_client_id,
                                        i.ad_org_id
                                     )
                                WHEN i.IsSoTrx = 'Y' AND i.IsReturnTrx = 'Y'
                                THEN -CurrencyConvert(
                                        ips.DueAmt,
                                        i.C_Currency_ID,
                                        sc.acct_currency_id,
                                        i.DateAcct,
                                        i.C_ConversionType_ID,
                                        i.ad_client_id,
                                        i.ad_org_id
                                     )
                                ELSE 0
                            END
                        ), 0),
                        MAX(sc.StdPrecision)
                    ) AS TotalOutstanding
                FROM   C_InvoicePaySchedule ips
                JOIN   C_Invoice      i   ON ips.C_Invoice_ID = i.C_Invoice_ID
                JOIN   schema_currency sc  ON sc.ad_client_id  = i.ad_client_id
                WHERE  ips.VA009_IsPaid = 'N'
                  AND  i.DocStatus IN ('CO', 'CL')
                  AND  i.IsSOTrx   = 'Y'
                  AND  i.AD_Client_ID = " + ctx.GetAD_Client_ID();

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
                dr?.Close();
            }

            var result = new
            {
                totalOutstanding = totalOutstanding
            };

            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }
    }
}
