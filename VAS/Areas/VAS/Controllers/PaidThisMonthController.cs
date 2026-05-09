using Newtonsoft.Json;
using System.Data;
using System.Web.Mvc;
using VAdvantage.Classes;
using VAdvantage.DataBase;
using VAdvantage.Utility;
using VIS.Filters;

namespace VIS.Controllers
{
    public class PaidThisMonthController : Controller
    {
        /// <summary>
        /// Returns the total payment amount received from customers in the current calendar month,
        /// converted to the client's accounting schema currency, plus the count of distinct paying customers.
        /// Uses C_AllocationHdr/C_AllocationLine joined to C_InvoicePaySchedule and C_Invoice,
        /// filtered to VA009_IsPaid = 'Y', DocStatus IN ('CO','CL'), IsSOTrx = 'Y',
        /// and DateAcct between the start of the current month and today.
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetPaidThisMonth()
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
                            CurrencyConvert(
                                al.Amount,
                                i.C_Currency_ID,
                                sc.acct_currency_id,
                                ah.DateAcct,
                                i.C_ConversionType_ID,
                                i.ad_client_id,
                                i.ad_org_id
                            )
                        ), 0),
                        MAX(sc.StdPrecision)
                    ) AS Total_Paid_Amount_This_Month,
                    COUNT(DISTINCT i.C_BPartner_ID) AS Customers_Paid_This_Month
                FROM   C_AllocationHdr ah
                JOIN   C_AllocationLine        al  ON ah.C_AllocationHdr_ID      = al.C_AllocationHdr_ID
                JOIN   C_InvoicePaySchedule    ips ON al.C_InvoicePaySchedule_ID = ips.C_InvoicePaySchedule_ID
                JOIN   C_Invoice               i   ON ips.C_Invoice_ID           = i.C_Invoice_ID
                JOIN   schema_currency         sc  ON sc.ad_client_id            = i.ad_client_id
                WHERE  ips.VA009_IsPaid = 'Y'
                  AND  i.DocStatus IN ('CO', 'CL')
                  AND  i.IsSoTrx = 'Y'
                  AND  ah.DateAcct >= TRUNC(CURRENT_DATE, 'MM')
                  AND  ah.DateAcct <= CURRENT_DATE
                  AND  i.AD_Client_ID = " + ctx.GetAD_Client_ID();

            decimal totalPaidAmount = 0;
            int customerCount = 0;

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql);
                if (dr != null && dr.Read())
                {
                    totalPaidAmount = Util.GetValueOfDecimal(dr["Total_Paid_Amount_This_Month"]);
                    customerCount   = Util.GetValueOfInt(dr["Customers_Paid_This_Month"]);
                }
            }
            finally
            {
                dr?.Close();
            }

            var result = new
            {
                totalPaidAmount = totalPaidAmount,
                customerCount   = customerCount
            };

            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }
    }
}
