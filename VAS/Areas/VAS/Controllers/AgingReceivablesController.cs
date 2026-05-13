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
                return Json(new { error = "Session Expired" }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            string schemaCurrencySql = @"
                SELECT ci.AD_Client_ID,
                       cs.C_Currency_ID AS Acct_Currency_ID,
                       cur.StdPrecision
                FROM AD_ClientInfo ci
                INNER JOIN C_AcctSchema cs ON (cs.C_AcctSchema_ID=ci.C_AcctSchema1_ID)
                INNER JOIN C_Currency cur ON (cur.C_Currency_ID=cs.C_Currency_ID)";

            string bucketedSql = @"
                SELECT i.AD_Client_ID,
                       CASE
                           WHEN CAST(ips.DueDate AS DATE) >= CAST(CURRENT_DATE AS DATE) THEN 'Not_Due'
                           WHEN CAST(CURRENT_DATE AS DATE) - CAST(ips.DueDate AS DATE) BETWEEN 1 AND 30 THEN 'Days_1_30'
                           WHEN CAST(CURRENT_DATE AS DATE) - CAST(ips.DueDate AS DATE) BETWEEN 31 AND 60 THEN 'Days_31_60'
                           WHEN CAST(CURRENT_DATE AS DATE) - CAST(ips.DueDate AS DATE) BETWEEN 61 AND 90 THEN 'Days_61_90'
                           WHEN CAST(CURRENT_DATE AS DATE) - CAST(ips.DueDate AS DATE) BETWEEN 91 AND 120 THEN 'Days_91_120'
                           WHEN CAST(CURRENT_DATE AS DATE) - CAST(ips.DueDate AS DATE) > 120 THEN 'Days_Over_120'
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
                SELECT ROUND(COALESCE(SUM(CASE WHEN b.Bucket='Not_Due' THEN b.Amt END), 0), MAX(sc.StdPrecision)) AS Not_Due_Amount,
                       ROUND(COALESCE(SUM(CASE WHEN b.Bucket='Days_1_30' THEN b.Amt END), 0), MAX(sc.StdPrecision)) AS Days_1_30_Amount,
                       ROUND(COALESCE(SUM(CASE WHEN b.Bucket='Days_31_60' THEN b.Amt END), 0), MAX(sc.StdPrecision)) AS Days_31_60_Amount,
                       ROUND(COALESCE(SUM(CASE WHEN b.Bucket='Days_61_90' THEN b.Amt END), 0), MAX(sc.StdPrecision)) AS Days_61_90_Amount,
                       ROUND(COALESCE(SUM(CASE WHEN b.Bucket='Days_91_120' THEN b.Amt END), 0), MAX(sc.StdPrecision)) AS Days_91_120_Amount,
                       ROUND(COALESCE(SUM(CASE WHEN b.Bucket='Days_Over_120' THEN b.Amt END), 0), MAX(sc.StdPrecision)) AS Days_Over_120_Amount
                FROM bucketed b
                INNER JOIN schema_currency sc ON (sc.AD_Client_ID=b.AD_Client_ID)";

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
                        daysOver120Amount = Util.GetValueOfDecimal(dr["Days_Over_120_Amount"])
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