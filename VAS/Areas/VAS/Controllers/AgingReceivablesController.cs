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
    public class AgingReceivablesController : Controller
    {
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetAgingReceivables()
        {
            if (Session["ctx"] == null)
            {
                return Json(new { error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired" }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            /* Base (accounting-schema) currency for the client. Cur_Symbol falls back to the
               ISO code when no display symbol is configured. All bucket amounts below are
               converted into this currency, so the symbol applies to every figure shown. */
            string schemaCurrencySql = @"
                SELECT ci.AD_Client_ID,
                       cs.C_Currency_ID AS Acct_Currency_ID,
                       cur.StdPrecision,
                       cur.ISO_Code AS ISO_Code,
                       CASE WHEN cur.CurSymbol IS NOT NULL THEN cur.CurSymbol ELSE cur.ISO_Code END AS Cur_Symbol
                FROM AD_ClientInfo ci
                INNER JOIN C_AcctSchema cs ON (cs.C_AcctSchema_ID=ci.C_AcctSchema1_ID)
                INNER JOIN C_Currency cur ON (cur.C_Currency_ID=cs.C_Currency_ID)
                WHERE ci.AD_Client_ID=" + ctx.GetAD_Client_ID();

            /* Days overdue = today − DueDate, built per database.
               PostgreSQL: CAST(CURRENT_DATE AS DATE) − CAST(ips.DueDate AS DATE) was resolving
               to an INTERVAL (operator does not exist: interval >= integer), so the bucket
               BETWEEN comparisons failed. Convert the TIMESTAMP difference into a whole-day
               NUMERIC via date_part('epoch', ...) / 86400 instead. DueDate is truncated to DATE
               first so the aging buckets stay whole-day based regardless of any time component.
               date_part(...) is used in place of EXTRACT(EPOCH FROM ...) so the FROM keyword
               does not interfere with MRole.AddAccessSQL parsing of the main FROM clause.
               Oracle: DATE − DATE already yields whole days; TRUNC strips any time part. */
            string daysOverdueExpr;
            if (DB.IsPostgreSQL())
            {
                daysOverdueExpr = "CAST(date_part('epoch', (CAST(CURRENT_DATE AS TIMESTAMP) - CAST(CAST(ips.DueDate AS DATE) AS TIMESTAMP))) / 86400 AS NUMERIC)";
            }
            else
            {
                daysOverdueExpr = "(TRUNC(SYSDATE) - TRUNC(ips.DueDate))";
            }

            string bucketedSql = @"
                SELECT i.AD_Client_ID,
                       CASE
                           WHEN " + daysOverdueExpr + @" <= 0 THEN 'Not_Due'
                           WHEN " + daysOverdueExpr + @" BETWEEN 1 AND 30 THEN 'Days_1_30'
                           WHEN " + daysOverdueExpr + @" BETWEEN 31 AND 60 THEN 'Days_31_60'
                           WHEN " + daysOverdueExpr + @" BETWEEN 61 AND 90 THEN 'Days_61_90'
                           WHEN " + daysOverdueExpr + @" BETWEEN 91 AND 120 THEN 'Days_91_120'
                           WHEN " + daysOverdueExpr + @" > 120 THEN 'Days_Over_120'
                       END AS Bucket,
                       CASE
                           WHEN i.IsSoTrx='Y' AND i.IsReturnTrx='N'
                               THEN CurrencyConvert(
                                       ips.DueAmt,
                                       i.C_Currency_ID,
                                       sc.Acct_Currency_ID,
                                       i.DateAcct,
                                       i.C_ConversionType_ID,
                                       i.AD_Client_ID,
                                       i.AD_Org_ID
                                    )
                           WHEN i.IsSoTrx='Y' AND i.IsReturnTrx='Y'
                               THEN -CurrencyConvert(
                                       ips.DueAmt,
                                       i.C_Currency_ID,
                                       sc.Acct_Currency_ID,
                                       i.DateAcct,
                                       i.C_ConversionType_ID,
                                       i.AD_Client_ID,
                                       i.AD_Org_ID
                                    )
                           ELSE 0
                       END AS Amt
                FROM C_InvoicePaySchedule ips
                INNER JOIN C_Invoice i ON (ips.C_Invoice_ID=i.C_Invoice_ID)
                INNER JOIN schema_currency sc ON (sc.AD_Client_ID=i.AD_Client_ID)
                WHERE ips.VA009_IsPaid='N'
                AND i.DocStatus IN ('CO','CL')
                AND i.IsSoTrx='Y'";

            /*
             * Important:
             * Apply MRole only on the physical main table C_Invoice.
             * Do not apply MRole on:
             * 1. Final WITH query
             * 2. CTE alias bucketed
             * 3. CTE alias schema_currency
             * 4. Secondary/helper tables
             */
            bucketedSql = MRole.GetDefault(ctx).AddAccessSQL(
                bucketedSql,
                "i",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            string sql = @"
                WITH schema_currency AS (
                    " + schemaCurrencySql + @"
                ),
                bucketed AS (
                    " + bucketedSql + @"
                )
                SELECT ROUND(COALESCE(SUM(CASE WHEN b.Bucket='Not_Due' THEN b.Amt END), 0), sc.StdPrecision) AS Not_Due_Amount,
                       ROUND(COALESCE(SUM(CASE WHEN b.Bucket='Days_1_30' THEN b.Amt END), 0), sc.StdPrecision) AS Days_1_30_Amount,
                       ROUND(COALESCE(SUM(CASE WHEN b.Bucket='Days_31_60' THEN b.Amt END), 0), sc.StdPrecision) AS Days_31_60_Amount,
                       ROUND(COALESCE(SUM(CASE WHEN b.Bucket='Days_61_90' THEN b.Amt END), 0), sc.StdPrecision) AS Days_61_90_Amount,
                       ROUND(COALESCE(SUM(CASE WHEN b.Bucket='Days_91_120' THEN b.Amt END), 0), sc.StdPrecision) AS Days_91_120_Amount,
                       ROUND(COALESCE(SUM(CASE WHEN b.Bucket='Days_Over_120' THEN b.Amt END), 0), sc.StdPrecision) AS Days_Over_120_Amount,
                       sc.Cur_Symbol AS Currency_Symbol,
                       sc.ISO_Code AS ISO_Code,
                       sc.StdPrecision AS Std_Precision
                /* Drive FROM the single-row schema_currency CTE and LEFT JOIN the buckets so the
                   base-currency symbol/precision are always returned - even with zero unpaid
                   invoices (the buckets are empty but the currency row survives). Mirrors
                   OutstandingSalesOrderController. */
                FROM schema_currency sc
                LEFT OUTER JOIN bucketed b ON (b.AD_Client_ID=sc.AD_Client_ID)
                GROUP BY sc.Cur_Symbol, sc.ISO_Code, sc.StdPrecision";

            object result = null;
            IDataReader dr = null;

            try
            {
                dr = DB.ExecuteReader(sql);

                if (dr != null && dr.Read())
                {
                    result = new
                    {
                        notDueAmount = Util.GetValueOfDecimal(dr["Not_Due_Amount"]),
                        days1To30Amount = Util.GetValueOfDecimal(dr["Days_1_30_Amount"]),
                        days31To60Amount = Util.GetValueOfDecimal(dr["Days_31_60_Amount"]),
                        days61To90Amount = Util.GetValueOfDecimal(dr["Days_61_90_Amount"]),
                        days91To120Amount = Util.GetValueOfDecimal(dr["Days_91_120_Amount"]),
                        daysOver120Amount = Util.GetValueOfDecimal(dr["Days_Over_120_Amount"]),
                        symbol = Util.GetValueOfString(dr["Currency_Symbol"]),
                        isoCode = Util.GetValueOfString(dr["ISO_Code"]),
                        stdPrecision = (dr["Std_Precision"] != null && dr["Std_Precision"] != System.DBNull.Value)
                            ? Util.GetValueOfInt(dr["Std_Precision"])
                            : 2
                    };
                }
            }
            finally
            {
                if (dr != null)
                {
                    dr.Close();
                }
            }

            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }
    }
}