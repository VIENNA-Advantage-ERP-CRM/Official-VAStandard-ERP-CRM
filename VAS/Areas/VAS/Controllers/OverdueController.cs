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
                return Json(new { error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired" }, JsonRequestBehavior.AllowGet);

            Ctx ctx = Session["ctx"] as Ctx;

            string overdueDateCondition = "";
            if (DB.IsPostgreSQL())
            {
                overdueDateCondition = " AND CAST(ips.DueDate AS DATE) < CAST(CURRENT_DATE AS DATE)";
            }
            else
            {
                overdueDateCondition = " AND TRUNC(ips.DueDate) < TRUNC(SYSDATE)";
            }

            string cteSchemaCurrency = "SELECT ci.AD_Client_ID, cs.C_Currency_ID AS Acct_Currency_ID, cur.StdPrecision FROM AD_ClientInfo ci INNER JOIN C_AcctSchema cs ON (cs.C_AcctSchema_ID=ci.C_AcctSchema1_ID) INNER JOIN C_Currency cur ON (cur.C_Currency_ID=cs.C_Currency_ID)";

            string mainQuery = "SELECT ROUND(COALESCE(SUM(CASE WHEN i.IsReturnTrx='N' THEN CurrencyConvert(COALESCE(ips.DueAmt, 0), i.C_Currency_ID, sc.Acct_Currency_ID, i.DateAcct, i.C_ConversionType_ID, i.AD_Client_ID, i.AD_Org_ID) WHEN i.IsReturnTrx='Y' THEN -CurrencyConvert(COALESCE(ips.DueAmt, 0), i.C_Currency_ID, sc.Acct_Currency_ID, i.DateAcct, i.C_ConversionType_ID, i.AD_Client_ID, i.AD_Org_ID) ELSE 0 END), 0), MAX(sc.StdPrecision)) AS Total_Overdue_Outstanding_Amount, COUNT(DISTINCT i.C_Invoice_ID) AS Overdue_Invoice_Count FROM C_InvoicePaySchedule ips INNER JOIN C_Invoice i ON (ips.C_Invoice_ID=i.C_Invoice_ID) INNER JOIN schema_currency sc ON (sc.AD_Client_ID=i.AD_Client_ID) WHERE ips.VA009_IsPaid='N'" + overdueDateCondition + " AND i.DocStatus IN ('CO', 'CL') AND i.IsSOTrx='Y' AND i.AD_Client_ID=" + ctx.GetAD_Client_ID();

            mainQuery = MRole.GetDefault(ctx).AddAccessSQL(
                mainQuery,
                "C_InvoicePaySchedule",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            string sql = "WITH schema_currency AS (" + cteSchemaCurrency + ") " + mainQuery;

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
