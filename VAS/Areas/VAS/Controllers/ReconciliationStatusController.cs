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
    public class ReconciliationStatusController : Controller
    {
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetReconciliationStatus()
        {
            if (Session["ctx"] == null)
            {
                return Json(new
                {
                    error = "Session Expired",
                    errorText = "Session Expired"
                }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;
            IDataReader dr = null;

            try
            {
                DateTime today = DateTime.Today;
                DateTime dateFrom = new DateTime(today.Year, today.Month, 1);
                DateTime dateTo = dateFrom.AddMonths(1);

                string dateFilter = GetDateFilter("p.DateAcct", dateFrom, dateTo);

                string schemaCurrencySql = @"
                    SELECT ClientInfo.AD_Client_ID,
                           AcctSchema.C_Currency_ID AS C_Currency_ID,
                           Currency.StdPrecision,
                           Currency.ISO_Code AS ISO_Code,
                           CASE
                               WHEN Currency.CurSymbol IS NOT NULL THEN Currency.CurSymbol
                               ELSE Currency.ISO_Code
                           END AS Cur_Symbol
                    FROM AD_ClientInfo ClientInfo
                    INNER JOIN C_AcctSchema AcctSchema
                        ON ClientInfo.C_AcctSchema1_ID = AcctSchema.C_AcctSchema_ID
                    INNER JOIN C_Currency Currency
                        ON AcctSchema.C_Currency_ID = Currency.C_Currency_ID";

                string reconciliationSql = @"
                    SELECT
                        COUNT(1) AS TotalPayments,
                        SUM(
                            CASE
                                WHEN COALESCE(p.IsReconciled, 'N') = 'Y'
                                THEN 1
                                ELSE 0
                            END
                        ) AS MatchedPayments
                    FROM C_Payment p
                    WHERE p.IsActive = 'Y'
                    AND p.IsReceipt = 'N'
                    AND p.DocStatus IN ('CO', 'CL')
                    "
                    + dateFilter + @"
                ";

                reconciliationSql = MRole.GetDefault(ctx).AddAccessSQL(
                    reconciliationSql,
                    "p",
                    MRole.SQL_FULLYQUALIFIED,
                    MRole.SQL_RO
                );

                string sql = @"
                    WITH SchemaCurrency AS (
                        " + schemaCurrencySql + @"
                    )
                    " + reconciliationSql;

                dr = DB.ExecuteReader(sql);

                int totalPayments = 0;
                int matchedPayments = 0;
                int manualMatchCount = 0;
                decimal matchedPercentage = 0;

                if (dr.Read())
                {
                    totalPayments = Util.GetValueOfInt(dr["TotalPayments"]);
                    matchedPayments = Util.GetValueOfInt(dr["MatchedPayments"]);
                }

                if (totalPayments > 0)
                {
                    matchedPercentage = decimal.Round((matchedPayments * 100M) / totalPayments, 2);
                    manualMatchCount = totalPayments - matchedPayments;
                }

                return Json(new
                {
                    title = GetMsg(ctx, "VAS_ReconciliationStatus", "Reconciliation status"),
                    subTitle = GetMsg(ctx, "VAS_MatchedToBillsBank", "Matched to bills + bank"),
                    matchedLabel = GetMsg(ctx, "VAS_Matched", "Matched"),
                    matchedPayments = matchedPayments,
                    totalPayments = totalPayments,
                    manualMatchCount = manualMatchCount,
                    matchedPercentage = matchedPercentage,
                    dateFrom = FormatDate(dateFrom),
                    dateTo = FormatDate(dateTo.AddDays(-1))
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    error = ex.Message
                }, JsonRequestBehavior.AllowGet);
            }
            finally
            {
                if (dr != null)
                {
                    dr.Close();
                    dr.Dispose();
                }
            }
        }

        private string GetDateFilter(string columnName, DateTime dateFrom, DateTime dateTo)
        {
            string dateFromText = FormatDate(dateFrom);
            string dateToText = FormatDate(dateTo);

            if (DB.IsOracle())
            {
                return @"
                    AND " + columnName + @" >= TO_DATE('" + dateFromText + @"', 'YYYY-MM-DD')
                    AND " + columnName + @" < TO_DATE('" + dateToText + @"', 'YYYY-MM-DD')
                ";
            }

            return @"
                AND " + columnName + @" >= DATE '" + dateFromText + @"'
                AND " + columnName + @" < DATE '" + dateToText + @"'
            ";
        }

        private string FormatDate(DateTime date)
        {
            return date.ToString("yyyy-MM-dd");
        }

        private string GetMsg(Ctx ctx, string key, string fallback)
        {
            string msg = Msg.GetMsg(ctx, key);
            return !string.IsNullOrEmpty(msg) && msg != "[" + key + "]"
                ? msg
                : fallback;
        }
    }
}