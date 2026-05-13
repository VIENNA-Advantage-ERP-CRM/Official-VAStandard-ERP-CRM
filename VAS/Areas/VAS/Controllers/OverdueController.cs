using Newtonsoft.Json;
using System.Data;
using System.Web.Mvc;
using VAdvantage.Classes;
using VAdvantage.DataBase;
using VAdvantage.Utility;
using VIS.Filters;

namespace VIS.Controllers
{
    public class OverdueController : Controller
    {
        /// <summary>
        /// Returns the total overdue (past due date, unpaid) amount across all sales invoices,
        /// converted to the client's accounting schema currency, plus the count of overdue invoices.
        /// Overdue = C_InvoicePaySchedule rows where VA009_IsPaid = 'N' and DueDate < today
        /// on completed/closed sales invoices (DocStatus IN ('CO','CL'), IsSOTrx = 'Y').
        /// Credit notes are subtracted. Amounts are converted via CurrencyConvert.
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetOverdue()
        {
            if (Session["ctx"] == null)
                return Json(new { error = "Session Expired" }, JsonRequestBehavior.AllowGet);

            Ctx ctx = Session["ctx"] as Ctx;

            string overdueDateCondition = "";
            if (DB.IsPostgreSQL())
            {
                overdueDateCondition = " AND CAST(ips.DueDate AS DATE) < CAST(CURRENT_DATE AS DATE) ";
            }
            else
            {
                overdueDateCondition = " AND TRUNC(ips.DueDate) < TRUNC(SYSDATE) ";
            }

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
                                WHEN i.IsReturnTrx = 'N'
                                THEN CurrencyConvert(
                                        COALESCE(ips.DueAmt, 0),
                                        i.C_Currency_ID,
                                        sc.acct_currency_id,
                                        i.DateAcct,
                                        i.C_ConversionType_ID,
                                        i.ad_client_id,
                                        i.ad_org_id
                                     )
                                WHEN i.IsReturnTrx = 'Y'
                                THEN -CurrencyConvert(
                                        COALESCE(ips.DueAmt, 0),
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
                    ) AS Total_Overdue_Outstanding_Amount,
                    COUNT(DISTINCT i.C_Invoice_ID) AS Overdue_Invoice_Count
                FROM   C_InvoicePaySchedule ips
                JOIN   C_Invoice      i   ON ips.C_Invoice_ID = i.C_Invoice_ID
                JOIN   schema_currency sc  ON sc.ad_client_id  = i.ad_client_id
                WHERE  ips.VA009_IsPaid = 'N'
                  " + overdueDateCondition + @"
                  AND  i.DocStatus IN ('CO', 'CL')
                  AND  i.IsSoTrx   = 'Y'
                  AND  i.AD_Client_ID = " + ctx.GetAD_Client_ID();

            decimal totalOverdue = 0;
            int invoiceCount = 0;

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql);
                if (dr != null && dr.Read())
                {
                    totalOverdue = Util.GetValueOfDecimal(dr["Total_Overdue_Outstanding_Amount"]);
                    invoiceCount = Util.GetValueOfInt(dr["Overdue_Invoice_Count"]);
                }
            }
            finally
            {
                dr?.Close();
            }

            var result = new
            {
                totalOverdue = totalOverdue,
                invoiceCount = invoiceCount
            };

            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }
    }
}
