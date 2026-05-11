using Newtonsoft.Json;
using System.Collections.Generic;
using System.Data;
using System.Web.Mvc;
using VAdvantage.Classes;
using VAdvantage.DataBase;
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
                return Json(new { error = "Session Expired" }, JsonRequestBehavior.AllowGet);

            Ctx ctx = Session["ctx"] as Ctx;

            string sql = @"
                WITH schema_currency AS (
                    SELECT ci.ad_client_id,
                           cs.c_currency_id AS acct_currency_id,
                           cur.StdPrecision
                    FROM ad_clientinfo ci
                    JOIN c_acctschema cs
                      ON cs.c_acctschema_id = ci.c_acctschema1_id
                    JOIN c_currency cur
                      ON cur.c_currency_id = cs.c_currency_id
                ),
                bucketed AS (
                    SELECT
                        i.ad_client_id,
                        CASE
                            WHEN ips.DueDate >= CURRENT_DATE THEN 'Not_Due'
                            WHEN TRUNC(CURRENT_DATE) - TRUNC(ips.DueDate) BETWEEN 1  AND 30  THEN 'Days_1_30'
                            WHEN TRUNC(CURRENT_DATE) - TRUNC(ips.DueDate) BETWEEN 31 AND 60  THEN 'Days_31_60'
                            WHEN TRUNC(CURRENT_DATE) - TRUNC(ips.DueDate) BETWEEN 61 AND 90  THEN 'Days_61_90'
                            WHEN TRUNC(CURRENT_DATE) - TRUNC(ips.DueDate) BETWEEN 91 AND 120 THEN 'Days_91_120'
                            WHEN TRUNC(CURRENT_DATE) - TRUNC(ips.DueDate) > 120              THEN 'Days_Over_120'
                        END AS bucket,
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
                        END AS amt
                    FROM C_InvoicePaySchedule ips
                    JOIN C_Invoice i
                      ON ips.C_Invoice_ID = i.C_Invoice_ID
                    JOIN schema_currency sc
                      ON sc.ad_client_id = i.ad_client_id
                    WHERE ips.VA009_IsPaid = 'N'
                      AND i.DocStatus IN ('CO','CL')
                      AND i.IsSoTrx = 'Y'
                )
                SELECT
                    ROUND(COALESCE(SUM(CASE WHEN bucket = 'Not_Due'       THEN amt END), 0), MAX(sc.StdPrecision)) AS Not_Due_Amount,
                    ROUND(COALESCE(SUM(CASE WHEN bucket = 'Days_1_30'     THEN amt END), 0), MAX(sc.StdPrecision)) AS Days_1_30_Amount,
                    ROUND(COALESCE(SUM(CASE WHEN bucket = 'Days_31_60'    THEN amt END), 0), MAX(sc.StdPrecision)) AS Days_31_60_Amount,
                    ROUND(COALESCE(SUM(CASE WHEN bucket = 'Days_61_90'    THEN amt END), 0), MAX(sc.StdPrecision)) AS Days_61_90_Amount,
                    ROUND(COALESCE(SUM(CASE WHEN bucket = 'Days_91_120'   THEN amt END), 0), MAX(sc.StdPrecision)) AS Days_91_120_Amount,
                    ROUND(COALESCE(SUM(CASE WHEN bucket = 'Days_Over_120' THEN amt END), 0), MAX(sc.StdPrecision)) AS Days_Over_120_Amount
                FROM bucketed b
                JOIN schema_currency sc
                  ON sc.ad_client_id = b.ad_client_id";

            object result = null;

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql);
                if (dr != null && dr.Read())
                {
                    result = new
                    {
                        notDueAmount      = Util.GetValueOfDecimal(dr["Not_Due_Amount"]),
                        days1To30Amount   = Util.GetValueOfDecimal(dr["Days_1_30_Amount"]),
                        days31To60Amount  = Util.GetValueOfDecimal(dr["Days_31_60_Amount"]),
                        days61To90Amount  = Util.GetValueOfDecimal(dr["Days_61_90_Amount"]),
                        days91To120Amount = Util.GetValueOfDecimal(dr["Days_91_120_Amount"]),
                        daysOver120Amount = Util.GetValueOfDecimal(dr["Days_Over_120_Amount"])
                    };
                }
            }
            finally
            {
                dr?.Close();
            }

            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }
    }
}
