using System;
using System.Data;
using System.Data.SqlClient;
using System.Web.Mvc;
using VAdvantage.Classes;
using VAdvantage.DataBase;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VAS.Controllers
{
    /*
     * Labels / Message Keys
     * 1 | Cleared                                | VAS_027_messageCleared
     * 2 | WHY                                    | VAS_027_messageWhy
     * 3 | Of last month's AP payments reconciled | VAS_027_messageAPPaymentClearedWhy
     */
    public class VAS_027_ClearedAPPaymentWidgetController : Controller
    {
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetClearedAPPayment()
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

            if (ctx == null)
            {
                return Json(new
                {
                    error = "Session Expired",
                    errorText = "Session Expired"
                }, JsonRequestBehavior.AllowGet);
            }

            IDataReader dr = null;

            try
            {
                SqlQueryData queryData = BuildClearedAPPaymentSql(ctx);

                dr = DB.ExecuteReader(queryData.Sql, queryData.Parameters, null);

                int totalPayments = 0;
                int clearedPayments = 0;
                decimal clearedPercentage = 0;
                DateTime? dateFrom = null;
                DateTime? dateTo = null;

                if (dr != null && dr.Read())
                {
                    totalPayments = Util.GetValueOfInt(dr["TotalPayments"]);
                    clearedPayments = Util.GetValueOfInt(dr["ClearedPayments"]);

                    if (dr["DateFrom"] != DBNull.Value)
                    {
                        dateFrom = Util.GetValueOfDateTime(dr["DateFrom"]);
                    }

                    if (dr["DateTo"] != DBNull.Value)
                    {
                        dateTo = Util.GetValueOfDateTime(dr["DateTo"]);
                    }
                }

                if (totalPayments > 0)
                {
                    clearedPercentage = decimal.Round((clearedPayments * 100M) / totalPayments, 2);
                }

                return Json(new
                {
                    title = GetMsg(ctx, "VAS_027_messageCleared", "Cleared"),
                    badge = GetMsg(ctx, "VAS_027_messageWhy", "WHY"),
                    description = GetMsg(ctx, "VAS_027_messageAPPaymentClearedWhy", "Of last month's AP payments reconciled"),
                    value = clearedPercentage,
                    clearedPercentage = clearedPercentage,
                    totalPayments = totalPayments,
                    clearedPayments = clearedPayments,
                    precision = 2,
                    dateFrom = dateFrom.HasValue ? FormatDate(dateFrom.Value) : "",
                    dateTo = dateTo.HasValue ? FormatDate(dateTo.Value) : ""
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    error = ex.Message,
                    errorText = ex.Message
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

        private SqlQueryData BuildClearedAPPaymentSql(Ctx ctx)
        {
            string currentDateSql = GetCurrentDateSql();
            string dateToExclusiveSql = GetDateToExclusiveSql("PeriodRange.DateTo");

            string currentPeriodSql = @"
CurrentPeriod AS
(
SELECT
YearData.C_Calendar_ID,
Period.StartDate,
Period.EndDate
FROM AD_ClientInfo ClientInfo
INNER JOIN C_Year YearData ON (YearData.C_Calendar_ID = ClientInfo.C_Calendar_ID)
INNER JOIN C_Period Period ON (Period.C_Year_ID = YearData.C_Year_ID)
WHERE ClientInfo.IsActive = 'Y'
AND ClientInfo.AD_Client_ID = @AD_Client_ID
AND " + currentDateSql + @" BETWEEN Period.StartDate AND Period.EndDate
)";

            string periodRangeSql = @"
PeriodRange AS
(
SELECT
PreviousPeriod.StartDate AS DateFrom,
PreviousPeriod.EndDate AS DateTo
FROM CurrentPeriod CurrentPeriod
INNER JOIN C_Year PreviousYear ON (PreviousYear.C_Calendar_ID = CurrentPeriod.C_Calendar_ID)
INNER JOIN C_Period PreviousPeriod ON (PreviousPeriod.C_Year_ID = PreviousYear.C_Year_ID)
WHERE PreviousPeriod.EndDate < CurrentPeriod.StartDate
AND NOT EXISTS (SELECT 1 FROM C_Year LookupYear INNER JOIN C_Period LookupPeriod ON (LookupPeriod.C_Year_ID = LookupYear.C_Year_ID) WHERE LookupYear.C_Calendar_ID = CurrentPeriod.C_Calendar_ID AND LookupPeriod.EndDate < CurrentPeriod.StartDate AND LookupPeriod.EndDate > PreviousPeriod.EndDate)
)";

            string paymentAccessSql = @"
SELECT
Payment.C_Payment_ID,
Payment.DateTrx,
Payment.IsReconciled
FROM C_Payment Payment
WHERE Payment.IsActive = 'Y'
AND Payment.IsReceipt = 'N'
AND Payment.DocStatus IN ('CO', 'CL')";

            paymentAccessSql = MRole.GetDefault(ctx).AddAccessSQL(
                paymentAccessSql,
                "Payment",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            string paymentFilteredSql = @"
PaymentFiltered AS
(
" + paymentAccessSql + @"
)";

            string sql = @"
WITH " + currentPeriodSql + @",
" + periodRangeSql + @",
" + paymentFilteredSql + @"
SELECT
COUNT(Payment.C_Payment_ID) AS TotalPayments,
COALESCE(SUM(CASE WHEN Payment.IsReconciled = 'Y' THEN 1 ELSE 0 END), 0) AS ClearedPayments,
MIN(PeriodRange.DateFrom) AS DateFrom,
MAX(PeriodRange.DateTo) AS DateTo
FROM PaymentFiltered Payment
INNER JOIN PeriodRange PeriodRange ON (Payment.DateTrx >= PeriodRange.DateFrom AND Payment.DateTrx < " + dateToExclusiveSql + @")";

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
            };

            return new SqlQueryData
            {
                Sql = sql,
                Parameters = parameters
            };
        }

        private string GetCurrentDateSql()
        {
            if (DB.IsOracle())
            {
                return "TRUNC(CURRENT_DATE)";
            }

            return "CURRENT_DATE";
        }

        private string GetDateToExclusiveSql(string columnName)
        {
            return "CAST(" + columnName + " AS DATE) + 1";
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

        private class SqlQueryData
        {
            public string Sql { get; set; }
            public SqlParameter[] Parameters { get; set; }
        }
    }
}