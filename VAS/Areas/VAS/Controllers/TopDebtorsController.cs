using Newtonsoft.Json;
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
    public class TopDebtorsController : Controller
    {
        /// <summary>
        /// Returns the top 5 customers with the largest outstanding unpaid invoice balances
        /// (VA009_IsPaid = 'N', DocStatus IN ('CO','CL'), IsSoTrx = 'Y'), converted to the
        /// client's accounting schema currency. Credit notes are subtracted. Includes max days
        /// overdue and a status text label.
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetTopDebtors()
        {
            if (Session["ctx"] == null)
            {
                return Json(new { error = "Session Expired" }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            string schemaCurrencySql = @"
                SELECT ci.AD_Client_ID,
                       acc.C_Currency_ID AS Acct_Currency_ID,
                       cur.StdPrecision
                FROM AD_ClientInfo ci
                INNER JOIN C_AcctSchema acc ON (ci.C_AcctSchema1_ID=acc.C_AcctSchema_ID)
                INNER JOIN C_Currency cur ON (acc.C_Currency_ID=cur.C_Currency_ID)";

            string customerOutstandingSql = @"
                SELECT bp.C_BPartner_ID AS Customer_ID,
                       bp.Name AS Customer_Name,
                       SUM(
                           CASE
                               WHEN i.IsReturnTrx='N' THEN
                                   CASE
                                       WHEN i.C_Currency_ID=sc.Acct_Currency_ID THEN NVL(ips.DueAmt, 0)
                                       ELSE CurrencyConvert(
                                           NVL(ips.DueAmt, 0),
                                           i.C_Currency_ID,
                                           sc.Acct_Currency_ID,
                                           i.DateAcct,
                                           i.C_ConversionType_ID,
                                           i.AD_Client_ID,
                                           i.AD_Org_ID
                                       )
                                   END
                               WHEN i.IsReturnTrx='Y' THEN
                                   -CASE
                                       WHEN i.C_Currency_ID=sc.Acct_Currency_ID THEN NVL(ips.DueAmt, 0)
                                       ELSE CurrencyConvert(
                                           NVL(ips.DueAmt, 0),
                                           i.C_Currency_ID,
                                           sc.Acct_Currency_ID,
                                           i.DateAcct,
                                           i.C_ConversionType_ID,
                                           i.AD_Client_ID,
                                           i.AD_Org_ID
                                       )
                                   END
                               ELSE 0
                           END
                       ) AS Total_Outstanding_Amount,
                       MAX(
                           CASE
                               WHEN TRUNC(SYSDATE) - TRUNC(ips.DueDate) > 0 THEN TRUNC(SYSDATE) - TRUNC(ips.DueDate)
                               ELSE 0
                           END
                       ) AS Max_Days_Overdue
                FROM C_InvoicePaySchedule ips
                INNER JOIN C_Invoice i ON (ips.C_Invoice_ID=i.C_Invoice_ID)
                INNER JOIN C_BPartner bp ON (i.C_BPartner_ID=bp.C_BPartner_ID)
                INNER JOIN schema_currency sc ON (sc.AD_Client_ID=i.AD_Client_ID)
                WHERE i.IsSoTrx='Y'
                AND ips.VA009_IsPaid='N'
                AND i.DocStatus IN ('CO', 'CL')";

            customerOutstandingSql = MRole.GetDefault(ctx).AddAccessSQL(
                customerOutstandingSql,
                "i",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            customerOutstandingSql += @"
                GROUP BY bp.C_BPartner_ID,
                         bp.Name";

            string precisionDataSql = @"
                SELECT MAX(StdPrecision) AS Prec
                FROM schema_currency";

            string sql = @"
                WITH schema_currency AS (
                    " + schemaCurrencySql + @"
                ),
                customer_outstanding AS (
                    " + customerOutstandingSql + @"
                ),
                precision_data AS (
                    " + precisionDataSql + @"
                )
                SELECT co.Customer_ID,
                       co.Customer_Name,
                       ROUND(co.Total_Outstanding_Amount, p.Prec) AS Total_Outstanding_Amount,
                       co.Max_Days_Overdue
                FROM customer_outstanding co
                CROSS JOIN precision_data p
                ORDER BY Total_Outstanding_Amount DESC
                FETCH FIRST 5 ROWS ONLY";

            var rows = new List<object>();

            IDataReader dr = null;
            try
            {
                
                dr = DB.ExecuteReader(sql);
                while (dr != null && dr.Read())
                {
                    rows.Add(new
                    {
                        customerName = dr["Customer_Name"]?.ToString(),
                        unpaidBalance = Util.GetValueOfDecimal(dr["Total_Outstanding_Amount"]),
                        daysOverdue = Util.GetValueOfInt(dr["Max_Days_Overdue"]),
                        statusText = Util.GetValueOfInt(dr["Max_Days_Overdue"]) <= 0 ? "Not yet due" : Util.GetValueOfInt(dr["Max_Days_Overdue"]).ToString() + " days overdue"
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
