using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Web.Mvc;
using VAdvantage.Classes;
using VAdvantage.DataBase;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VIS.Controllers
{
    /// <summary>
    /// Module Name : VAS Dashboard
    /// Purpose     : Provides paid-this-month AP payment KPI widget data.
    /// Chronological development:
    ///   <EmployeeCode>   Created Date
    /// </summary>
    public class PaidThisMonthAPPaymentController : Controller
    {
        /// <summary>
        /// Gets total AP payments posted in the current calendar month.
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
                DateTime today = DateTime.Today;
                DateTime dateFrom = new DateTime(today.Year, today.Month, 1);
                DateTime dateTo = dateFrom.AddMonths(1);

                string schemaCurrencySql = @"
SELECT
    ClientInfo.AD_Client_ID,
    AcctSchema.C_Currency_ID AS C_Currency_ID,
    Currency.StdPrecision,
    Currency.ISO_Code AS ISO_Code,
    CASE
        WHEN Currency.CurSymbol IS NOT NULL THEN Currency.CurSymbol
        ELSE Currency.ISO_Code
    END AS Cur_Symbol
FROM AD_ClientInfo ClientInfo
INNER JOIN C_AcctSchema AcctSchema ON (ClientInfo.C_AcctSchema1_ID=AcctSchema.C_AcctSchema_ID)
INNER JOIN C_Currency Currency ON (AcctSchema.C_Currency_ID=Currency.C_Currency_ID)
WHERE ClientInfo.IsActive='Y'
AND ClientInfo.AD_Client_ID=@AD_Client_ID";

                string paidThisMonthSql = @"
SELECT
    p.AD_Client_ID,
    p.C_BPartner_ID,
    CASE
        WHEN p.C_Currency_ID=SchemaCurrency.C_Currency_ID THEN COALESCE(p.PayAmt, 0)
        ELSE CurrencyConvert(
            COALESCE(p.PayAmt, 0),
            p.C_Currency_ID,
            SchemaCurrency.C_Currency_ID,
            p.DateAcct,
            p.C_ConversionType_ID,
            p.AD_Client_ID,
            p.AD_Org_ID
        )
    END AS PaidAmount
FROM C_Payment p
INNER JOIN SchemaCurrency SchemaCurrency ON (SchemaCurrency.AD_Client_ID=p.AD_Client_ID)
WHERE p.IsActive='Y'
AND p.IsReceipt=@IsReceipt
AND p.DocStatus IN ('CO','CL')
AND p.DateAcct>=@DateFrom
AND p.DateAcct<@DateTo
AND COALESCE(p.VA009_ExecutionStatus,'R') NOT IN ('B','C')";

                paidThisMonthSql = MRole.GetDefault(ctx).AddAccessSQL(
                    paidThisMonthSql,
                    "p",
                    MRole.SQL_FULLYQUALIFIED,
                    MRole.SQL_RO
                );

                string sql = @"
WITH SchemaCurrency AS (
    " + schemaCurrencySql + @"
),
PaidThisMonthData AS (
    " + paidThisMonthSql + @"
)
SELECT
    COALESCE(SUM(PaidThisMonthData.PaidAmount), 0) AS PaidThisMonth,
    COUNT(DISTINCT PaidThisMonthData.C_BPartner_ID) AS VendorCount,
    MAX(SchemaCurrency.C_Currency_ID) AS C_Currency_ID,
    MAX(SchemaCurrency.ISO_Code) AS CurrencyISO,
    MAX(SchemaCurrency.Cur_Symbol) AS CurrencySymbol,
    MAX(SchemaCurrency.StdPrecision) AS StdPrecision
FROM SchemaCurrency SchemaCurrency
LEFT OUTER JOIN PaidThisMonthData PaidThisMonthData ON (PaidThisMonthData.AD_Client_ID=SchemaCurrency.AD_Client_ID)";

                List<SqlParameter> parameters = new List<SqlParameter>
                {
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                    new SqlParameter("@IsReceipt", "N"),
                    new SqlParameter("@DateFrom", dateFrom),
                    new SqlParameter("@DateTo", dateTo)
                };

                dr = DB.ExecuteReader(sql, parameters.ToArray());

                decimal paidThisMonth = 0;
                int vendorCount = 0;
                int cCurrencyId = 0;
                int precision = 2;
                string currencyISO = string.Empty;
                string currencySymbol = string.Empty;

                if (dr != null && dr.Read())
                {
                    paidThisMonth = Util.GetValueOfDecimal(dr["PaidThisMonth"]);
                    vendorCount = Util.GetValueOfInt(dr["VendorCount"]);
                    cCurrencyId = Util.GetValueOfInt(dr["C_Currency_ID"]);
                    precision = Util.GetValueOfInt(dr["StdPrecision"]);
                    currencyISO = Util.GetValueOfString(dr["CurrencyISO"]);
                    currencySymbol = Util.GetValueOfString(dr["CurrencySymbol"]);
                }

                paidThisMonth = decimal.Round(paidThisMonth, precision);

                return Json(new
                {
                    title = GetMsg(ctx, "VAS_PaidThisMonth", "Paid this month"),
                    subtitle = GetMsg(ctx, "VAS_CashPaid", "Cash paid"),
                    badge = GetMsg(ctx, "VAS_Why", "WHY"),
                    description = GetMsg(ctx, "VAS_OutgoingPaymentsPostedSoFar", "Outgoing payments posted so far"),
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
                    dateFrom = FormatDate(dateFrom),
                    dateTo = FormatDate(dateTo.AddDays(-1))
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
    }
}