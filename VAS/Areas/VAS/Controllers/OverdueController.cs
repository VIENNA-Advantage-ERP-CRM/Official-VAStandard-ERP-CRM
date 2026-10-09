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

            string cteSchemaCurrency = @"SELECT ci.AD_Client_ID, cs.C_Currency_ID AS Acct_Currency_ID, cur.StdPrecision, cur.ISO_Code AS ISO_Code,
                    CASE WHEN cur.CurSymbol IS NOT NULL THEN cur.CurSymbol ELSE cur.ISO_Code END AS Cur_Symbol
                    FROM AD_ClientInfo ci
                    INNER JOIN C_AcctSchema cs ON (cs.C_AcctSchema_ID=ci.C_AcctSchema1_ID)
                    INNER JOIN C_Currency cur ON (cur.C_Currency_ID=cs.C_Currency_ID)
                    WHERE ci.AD_Client_ID=" + ctx.GetAD_Client_ID();

            /* Aggregate the overdue figures in their own subquery. An aggregate
               with no GROUP BY always yields exactly one row (SUM/COUNT = 0 when
               nothing matches), so CROSS JOINing it with the single-row currency
               CTE below always carries the currency — even at zero overdue. */
            string overdueQuery = @"SELECT COALESCE(SUM(CASE
                    WHEN i.IsReturnTrx='N' THEN
                     CurrencyConvert(COALESCE(ips.DueAmt, 0), i.C_Currency_ID, sc.Acct_Currency_ID, i.DateAcct, i.C_ConversionType_ID, i.AD_Client_ID, i.AD_Org_ID)
                    WHEN i.IsReturnTrx='Y' THEN
                     -CurrencyConvert(COALESCE(ips.DueAmt, 0), i.C_Currency_ID, sc.Acct_Currency_ID, i.DateAcct, i.C_ConversionType_ID, i.AD_Client_ID, i.AD_Org_ID)
                    ELSE 0 END), 0) AS Total_Raw, COUNT(DISTINCT i.C_Invoice_ID) AS Overdue_Invoice_Count
                    FROM C_InvoicePaySchedule ips
                    INNER JOIN C_Invoice i ON (ips.C_Invoice_ID=i.C_Invoice_ID)
                    INNER JOIN schema_currency sc ON (sc.AD_Client_ID=i.AD_Client_ID)
                    WHERE ips.VA009_IsPaid='N'" + overdueDateCondition + " AND i.DocStatus IN ('CO', 'CL') AND i.IsSOTrx='Y' AND i.AD_Client_ID=" + ctx.GetAD_Client_ID();

            overdueQuery = MRole.GetDefault(ctx).AddAccessSQL(
                overdueQuery,
                "C_InvoicePaySchedule",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            string sql = "WITH schema_currency AS (" + cteSchemaCurrency + "), overdue AS (" + overdueQuery + ") "
                    + @"SELECT ROUND(o.Total_Raw, sc.StdPrecision) AS Total_Overdue_Outstanding_Amount,
                    o.Overdue_Invoice_Count AS Overdue_Invoice_Count,
                    sc.Cur_Symbol AS Cur_Symbol, sc.ISO_Code AS ISO_Code, sc.StdPrecision AS Std_Precision
                    FROM schema_currency sc CROSS JOIN overdue o";

            decimal totalOverdue = 0;
            int invoiceCount = 0;
            /* Base-currency symbol (accounting schema currency); amounts above are
               already converted to this currency. The CROSS JOIN with the
               schema_currency CTE keeps these populated even at zero overdue. */
            string currencySymbol = "";
            string isoCode = "";
            int stdPrecision = 2;

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql);
                if (dr != null && dr.Read())
                {
                    totalOverdue = Util.GetValueOfDecimal(dr["Total_Overdue_Outstanding_Amount"]);
                    invoiceCount = Util.GetValueOfInt(dr["Overdue_Invoice_Count"]);
                    currencySymbol = Util.GetValueOfString(dr["Cur_Symbol"]);
                    isoCode = Util.GetValueOfString(dr["ISO_Code"]);
                    /* CROSS JOIN always carries the schema precision; guard anyway. */
                    if (dr["Std_Precision"] != null && dr["Std_Precision"] != System.DBNull.Value)
                    {
                        stdPrecision = Util.GetValueOfInt(dr["Std_Precision"]);
                    }
                }
            }
            finally
            {
                dr?.Close();
            }

            var result = new
            {
                totalOverdue = totalOverdue,
                invoiceCount = invoiceCount,
                symbol = currencySymbol,
                isoCode = isoCode,
                stdPrecision = stdPrecision
            };

            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }
    }
}
