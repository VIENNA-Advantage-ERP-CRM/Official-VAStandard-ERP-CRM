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
    public class PaidThisMonthController : Controller
    {
        /// <summary>
        /// Returns the total payment amount received from customers in the current calendar month,
        /// converted to the client's accounting schema currency, plus the count of distinct paying customers.
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetPaidThisMonth()
        {
            if (Session["ctx"] == null)
            {
                return Json(new { error = "Session Expired" }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            DateTime today = DateTime.Now.Date;
            DateTime monthStart = new DateTime(today.Year, today.Month, 1);

            string monthStartDate = monthStart.ToString("yyyy-MM-dd");
            string todayDate = today.ToString("yyyy-MM-dd");

            string schemaCurrencySql = @"
                SELECT ci.AD_Client_ID,
                       cs.C_Currency_ID AS Acct_Currency_ID,
                       cur.StdPrecision
                FROM AD_ClientInfo ci
                INNER JOIN C_AcctSchema cs ON (cs.C_AcctSchema_ID=ci.C_AcctSchema1_ID)
                INNER JOIN C_Currency cur ON (cur.C_Currency_ID=cs.C_Currency_ID)";

            string paidThisMonthDataSql = @"
                SELECT i.C_BPartner_ID,
                       CurrencyConvert(
                           al.Amount,
                           i.C_Currency_ID,
                           sc.Acct_Currency_ID,
                           ah.DateAcct,
                           i.C_ConversionType_ID,
                           i.AD_Client_ID,
                           i.AD_Org_ID
                       ) AS PaidAmount,
                       sc.StdPrecision
                FROM C_AllocationHdr ah
                INNER JOIN C_AllocationLine al ON (ah.C_AllocationHdr_ID=al.C_AllocationHdr_ID)
                INNER JOIN C_InvoicePaySchedule ips ON (al.C_InvoicePaySchedule_ID=ips.C_InvoicePaySchedule_ID)
                INNER JOIN C_Invoice i ON (ips.C_Invoice_ID=i.C_Invoice_ID)
                INNER JOIN SchemaCurrency sc ON (sc.AD_Client_ID=i.AD_Client_ID)
                WHERE ips.VA009_IsPaid='Y'
                AND i.DocStatus IN ('CO','CL')
                AND i.IsSoTrx='Y'
                AND CAST(ah.DateAcct AS DATE) >= DATE '" + monthStartDate + @"'
                AND CAST(ah.DateAcct AS DATE) <= DATE '" + todayDate + @"'";

            /*
             * MRole handling for CTE:
             * Apply access SQL only on the CTE body where the main physical table exists.
             *
             * Main physical table: C_Invoice
             * Primary alias: i
             *
             * Do not apply MRole on:
             * 1. Final combined WITH query
             * 2. CTE alias SchemaCurrency
             * 3. CTE alias PaidThisMonthData
             * 4. Secondary joined tables used only for calculation/join logic
             */
            paidThisMonthDataSql = MRole.GetDefault(ctx).AddAccessSQL(
                paidThisMonthDataSql,
                "i",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            string sql = @"
                WITH SchemaCurrency AS (
                    " + schemaCurrencySql + @"
                ),
                PaidThisMonthData AS (
                    " + paidThisMonthDataSql + @"
                )
                SELECT ROUND(COALESCE(SUM(PaidAmount), 0), MAX(StdPrecision)) AS Total_Paid_Amount_This_Month,
                       COUNT(DISTINCT C_BPartner_ID) AS Customers_Paid_This_Month
                FROM PaidThisMonthData";

            decimal totalPaidAmount = 0;
            int customerCount = 0;

            IDataReader dr = null;

            try
            {
                dr = DB.ExecuteReader(sql);

                if (dr != null && dr.Read())
                {
                    totalPaidAmount = Util.GetValueOfDecimal(dr["Total_Paid_Amount_This_Month"]);
                    customerCount = Util.GetValueOfInt(dr["Customers_Paid_This_Month"]);
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
                totalPaidAmount = totalPaidAmount,
                customerCount = customerCount
            };

            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }
    }
}