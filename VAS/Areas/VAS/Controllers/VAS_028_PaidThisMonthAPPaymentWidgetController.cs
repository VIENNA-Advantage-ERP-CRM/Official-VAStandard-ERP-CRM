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
    /// <summary>
    /// Module Name : VAS Dashboard
    /// Purpose     : Provides paid-this-month AP payment KPI widget data.
    /// </summary>
    /*
     * Labels / Message Keys
     * 1 | Paid this month                 | VAS_028_MessagePaidThisMonth
     * 2 | Cash paid                       | VAS_028_MessageCashPaid
     * 3 | WHY                             | VAS_028_MessageWhy
     * 4 | Outgoing payments posted so far | VAS_028_MessageOutgoingPaymentsPostedSoFar
     */
    public class VAS_028_PaidThisMonthAPPaymentWidgetController : Controller
    {
        /// <summary>
        /// Gets total AP payments posted in the current financial period.
        /// Period is based on C_Period calendar linked with AD_ClientInfo.
        /// </summary>
        /// <returns>Paid AP amount, vendor count, currency symbol and precision.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetPaidThisMonth()
        {
            if (Session["ctx"] == null)
            {
                return Json(new
                {
                    error = true,
                    errorText = "Session Expired"
                }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            if (ctx == null)
            {
                return Json(new
                {
                    error = true,
                    errorText = "Session Expired"
                }, JsonRequestBehavior.AllowGet);
            }

            IDataReader dr = null;

            try
            {
                SqlQueryData queryData = BuildPaidThisMonthSql(ctx);

                dr = DB.ExecuteReader(queryData.Sql, queryData.Parameters, null);

                decimal paidThisMonth = 0;
                int vendorCount = 0;
                int cCurrencyId = 0;
                int precision = 2;
                string currencyISO = string.Empty;
                string currencySymbol = string.Empty;
                DateTime? dateFrom = null;
                DateTime? dateTo = null;

                if (dr != null && dr.Read())
                {
                    paidThisMonth = Util.GetValueOfDecimal(dr["PaidThisMonth"]);
                    vendorCount = Util.GetValueOfInt(dr["VendorCount"]);
                    cCurrencyId = Util.GetValueOfInt(dr["C_Currency_ID"]);
                    precision = Util.GetValueOfInt(dr["StdPrecision"]);
                    currencyISO = Util.GetValueOfString(dr["CurrencyISO"]);
                    currencySymbol = Util.GetValueOfString(dr["CurrencySymbol"]);

                    if (dr["DateFrom"] != DBNull.Value)
                    {
                        dateFrom = Util.GetValueOfDateTime(dr["DateFrom"]);
                    }

                    if (dr["DateTo"] != DBNull.Value)
                    {
                        dateTo = Util.GetValueOfDateTime(dr["DateTo"]);
                    }
                }

                paidThisMonth = decimal.Round(paidThisMonth, precision);

                return Json(new
                {
                    title = GetMsg(ctx, "VAS_028_MessagePaidThisMonth", "Paid this month"),
                    subtitle = GetMsg(ctx, "VAS_028_MessageCashPaid", "Cash paid"),
                    description = GetMsg(ctx, "VAS_028_MessageOutgoingPaymentsPostedSoFar", "Outgoing payments posted so far"),
                    value = paidThisMonth,
                    paidThisMonth = paidThisMonth,
                    totalPaidAmount = paidThisMonth,
                    vendorCount = vendorCount,
                    paymentCount = vendorCount,
                    cCurrencyId = cCurrencyId,
                    currencyISO = currencyISO,
                    currencySymbol = currencySymbol,
                    symbol = currencySymbol,
                    precision = precision,
                    dateFrom = dateFrom.HasValue ? FormatDate(dateFrom.Value) : "",
                    dateTo = dateTo.HasValue ? FormatDate(dateTo.Value) : ""
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    error = true,
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

        private SqlQueryData BuildPaidThisMonthSql(Ctx ctx)
        {
            string currentDateSql = GetCurrentDateSql();
            string dateToExclusiveSql = GetDateToExclusiveSql("PeriodRange.DateTo");

            string executionStatusFilter = HasPaymentExecutionStatusColumn()
                ? @"
AND COALESCE(Payment.VA009_ExecutionStatus, 'R') NOT IN ('B', 'C')"
                : string.Empty;

            string periodRangeSql = @"
PeriodRange AS
(
SELECT
MIN(Period.StartDate) AS DateFrom,
MAX(Period.EndDate) AS DateTo
FROM AD_ClientInfo ClientInfo
INNER JOIN C_Year YearData ON (YearData.C_Calendar_ID = ClientInfo.C_Calendar_ID)
INNER JOIN C_Period Period ON (Period.C_Year_ID = YearData.C_Year_ID)
WHERE ClientInfo.IsActive = 'Y'
AND ClientInfo.AD_Client_ID = @AD_Client_ID
AND " + currentDateSql + @" BETWEEN Period.StartDate AND Period.EndDate
)";

            string schemaCurrencySql = @"
SchemaCurrency AS
(
SELECT
ClientInfo.AD_Client_ID,
AcctSchema.C_Currency_ID AS C_Currency_ID,
Currency.StdPrecision,
Currency.ISO_Code AS ISO_Code,
CASE WHEN Currency.CurSymbol IS NOT NULL THEN Currency.CurSymbol ELSE Currency.ISO_Code END AS Cur_Symbol
FROM AD_ClientInfo ClientInfo
INNER JOIN C_AcctSchema AcctSchema ON (ClientInfo.C_AcctSchema1_ID = AcctSchema.C_AcctSchema_ID)
INNER JOIN C_Currency Currency ON (AcctSchema.C_Currency_ID = Currency.C_Currency_ID)
WHERE ClientInfo.IsActive = 'Y'
AND ClientInfo.AD_Client_ID = @AD_Client_ID
)";

            string paymentAccessSql = @"
SELECT
Payment.AD_Client_ID,
Payment.AD_Org_ID,
Payment.C_BPartner_ID,
Payment.C_Currency_ID,
Payment.C_ConversionType_ID,
Payment.DateAcct,
Payment.PayAmt
FROM C_Payment Payment
WHERE Payment.IsActive = 'Y'
AND Payment.IsReceipt = 'N'
AND Payment.DocStatus IN ('CO', 'CL')" + executionStatusFilter;

            /*
             * MRole Handling:
             * Apply MRole only on the main physical table C_Payment Payment.
             * Do not apply MRole on the final WITH query.
             * Do not apply MRole on CTE aliases.
             * Do not apply MRole on joined aliases.
             */
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

            string paidThisMonthDataSql = @"
PaidThisMonthData AS
(
SELECT
Payment.AD_Client_ID,
Payment.C_BPartner_ID,
CASE WHEN Payment.C_Currency_ID = SchemaCurrency.C_Currency_ID THEN COALESCE(Payment.PayAmt, 0) ELSE CurrencyConvert(COALESCE(Payment.PayAmt, 0), Payment.C_Currency_ID, SchemaCurrency.C_Currency_ID, Payment.DateAcct, Payment.C_ConversionType_ID, Payment.AD_Client_ID, Payment.AD_Org_ID) END AS PaidAmount
FROM PaymentFiltered Payment
INNER JOIN SchemaCurrency SchemaCurrency ON (SchemaCurrency.AD_Client_ID = Payment.AD_Client_ID)
INNER JOIN PeriodRange PeriodRange ON (Payment.DateAcct >= PeriodRange.DateFrom AND Payment.DateAcct < " + dateToExclusiveSql + @")
)";

            string sql = @"
WITH " + periodRangeSql + @",
" + schemaCurrencySql + @",
" + paymentFilteredSql + @",
" + paidThisMonthDataSql + @"
SELECT
ROUND(CAST(COALESCE(SUM(PaidThisMonthData.PaidAmount), 0) AS NUMERIC), CAST(MAX(SchemaCurrency.StdPrecision) AS INTEGER)) AS PaidThisMonth,
COUNT(DISTINCT PaidThisMonthData.C_BPartner_ID) AS VendorCount,
MAX(SchemaCurrency.C_Currency_ID) AS C_Currency_ID,
MAX(SchemaCurrency.ISO_Code) AS CurrencyISO,
MAX(SchemaCurrency.Cur_Symbol) AS CurrencySymbol,
MAX(SchemaCurrency.StdPrecision) AS StdPrecision,
MIN(PeriodRange.DateFrom) AS DateFrom,
MAX(PeriodRange.DateTo) AS DateTo
FROM SchemaCurrency SchemaCurrency
LEFT OUTER JOIN PeriodRange PeriodRange ON (1 = 1)
LEFT OUTER JOIN PaidThisMonthData PaidThisMonthData ON (PaidThisMonthData.AD_Client_ID = SchemaCurrency.AD_Client_ID)";

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

        private bool HasPaymentExecutionStatusColumn()
        {
            string sql = @"
SELECT
COUNT(1)
FROM AD_Table TableData
INNER JOIN AD_Column ColumnData ON (TableData.AD_Table_ID = ColumnData.AD_Table_ID)
WHERE TableData.TableName = 'C_Payment'
AND ColumnData.ColumnName = 'VA009_ExecutionStatus'";

            return Util.GetValueOfInt(DB.ExecuteScalar(sql)) > 0;
        }

        /// <summary>
        /// Gets translated message text by key with fallback.
        /// </summary>
        /// <param name="ctx">User context.</param>
        /// <param name="key">Message key.</param>
        /// <param name="fallback">Fallback text.</param>
        /// <returns>Translated or fallback message text.</returns>
        private string GetMsg(Ctx ctx, string key, string fallback)
        {
            string msg = Msg.GetMsg(ctx, key);

            return !string.IsNullOrEmpty(msg) && msg != "[" + key + "]"
                ? msg
                : fallback;
        }

        /// <summary>
        /// Formats date values returned to the widget.
        /// </summary>
        /// <param name="date">Date value.</param>
        /// <returns>Date formatted as yyyy-MM-dd.</returns>
        private string FormatDate(DateTime date)
        {
            return date.ToString("yyyy-MM-dd");
        }

        private class SqlQueryData
        {
            public string Sql { get; set; }
            public SqlParameter[] Parameters { get; set; }
        }
    }
}