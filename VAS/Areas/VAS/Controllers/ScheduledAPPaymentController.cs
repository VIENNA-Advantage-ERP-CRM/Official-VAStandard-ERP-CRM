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
    /// Purpose     : Provides scheduled AP payment KPI widget data grouped by payment method.
    /// Chronological development:
    ///   <EmployeeCode>   Created Date
    /// </summary>
    public class ScheduledAPPaymentController : Controller
    {
        /// <summary>
        /// Gets AP invoice schedule amounts due in the current week, grouped by payment method.
        /// </summary>
        /// <returns>Scheduled AP payment amounts, currency, precision and grouped payment method rows.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetScheduledAPPaymentThisWeek()
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
                int daysFromMonday = ((int)today.DayOfWeek + 6) % 7;
                DateTime weekFrom = today.AddDays(-daysFromMonday);
                DateTime weekTo = weekFrom.AddDays(7);

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

                string invoiceBody = @"
SELECT
    inv.C_Invoice_ID,
    inv.C_BPartner_ID,
    SchemaCurrency.C_Currency_ID,
    SchemaCurrency.ISO_Code AS CurrencyISO,
    SchemaCurrency.Cur_Symbol AS CurrencySymbol,
    SchemaCurrency.StdPrecision,
    CASE
        WHEN inv.VA009_PaymentMethod_ID IS NOT NULL THEN inv.VA009_PaymentMethod_ID
        ELSE 0
    END AS PaymentMethod_ID,
    CASE
        WHEN PaymentMethod.Name IS NOT NULL THEN PaymentMethod.Name
        WHEN inv.PaymentRule IS NOT NULL THEN inv.PaymentRule
        ELSE ''
    END AS PaymentMethodName,
    CASE
        WHEN COALESCE(inv.IsReturnTrx,'N')='Y' THEN -CurrencyConvert(
            COALESCE(ips.DueAmt,0),
            inv.C_Currency_ID,
            SchemaCurrency.C_Currency_ID,
            inv.DateAcct,
            inv.C_ConversionType_ID,
            inv.AD_Client_ID,
            inv.AD_Org_ID
        )
        ELSE CurrencyConvert(
            COALESCE(ips.DueAmt,0),
            inv.C_Currency_ID,
            SchemaCurrency.C_Currency_ID,
            inv.DateAcct,
            inv.C_ConversionType_ID,
            inv.AD_Client_ID,
            inv.AD_Org_ID
        )
    END AS ScheduledAmount
FROM C_Invoice inv
INNER JOIN C_InvoicePaySchedule ips ON (inv.C_Invoice_ID=ips.C_Invoice_ID)
INNER JOIN SchemaCurrency SchemaCurrency ON (SchemaCurrency.AD_Client_ID=inv.AD_Client_ID)
LEFT OUTER JOIN VA009_PaymentMethod PaymentMethod ON (inv.VA009_PaymentMethod_ID=PaymentMethod.VA009_PaymentMethod_ID)
WHERE inv.IsActive='Y'
AND ips.IsActive='Y'
AND inv.AD_Client_ID=@AD_Client_ID
AND inv.IsSOTrx='N'
AND inv.DocStatus IN ('CO','CL')
AND COALESCE(ips.VA009_IsPaid,'N')<>'Y'
AND COALESCE(ips.DueAmt,0)>0
AND ips.DueDate>=@WeekFrom
AND ips.DueDate<@WeekTo";

                invoiceBody = MRole.GetDefault(ctx).AddAccessSQL(
                    invoiceBody,
                    "inv",
                    MRole.SQL_FULLYQUALIFIED,
                    MRole.SQL_RO
                );

                string sql = @"
WITH SchemaCurrency AS (
    " + schemaCurrencySql + @"
),
ScheduledData AS (
    " + invoiceBody + @"
)
SELECT
    ScheduledData.PaymentMethod_ID,
    ScheduledData.PaymentMethodName,
    ScheduledData.C_Currency_ID,
    ScheduledData.CurrencyISO,
    ScheduledData.CurrencySymbol,
    MAX(ScheduledData.StdPrecision) AS StdPrecision,
    ROUND(COALESCE(SUM(ScheduledData.ScheduledAmount),0),MAX(ScheduledData.StdPrecision)) AS ScheduledAmount
FROM ScheduledData ScheduledData
GROUP BY
    ScheduledData.PaymentMethod_ID,
    ScheduledData.PaymentMethodName,
    ScheduledData.C_Currency_ID,
    ScheduledData.CurrencyISO,
    ScheduledData.CurrencySymbol
HAVING SUM(ScheduledData.ScheduledAmount)>0
ORDER BY ScheduledAmount DESC";

                List<SqlParameter> parameters = new List<SqlParameter>
                {
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                    new SqlParameter("@WeekFrom", weekFrom),
                    new SqlParameter("@WeekTo", weekTo)
                };

                dr = DB.ExecuteReader(sql, parameters.ToArray());

                decimal scheduledAmountThisWeek = 0;
                int cCurrencyId = 0;
                int precision = 2;
                string currencyISO = string.Empty;
                string currencySymbol = string.Empty;
                List<object> groups = new List<object>();

                while (dr != null && dr.Read())
                {
                    decimal scheduledAmount = Util.GetValueOfDecimal(dr["ScheduledAmount"]);
                    int groupCurrencyId = Util.GetValueOfInt(dr["C_Currency_ID"]);
                    int groupPrecision = Util.GetValueOfInt(dr["StdPrecision"]);
                    string groupCurrencyISO = Util.GetValueOfString(dr["CurrencyISO"]);
                    string groupCurrencySymbol = Util.GetValueOfString(dr["CurrencySymbol"]);
                    string paymentMethodName = Util.GetValueOfString(dr["PaymentMethodName"]);

                    if (string.IsNullOrEmpty(paymentMethodName))
                    {
                        paymentMethodName = GetMsg(ctx, "VAS_NotSpecified", "Not Specified");
                    }

                    scheduledAmountThisWeek += scheduledAmount;

                    if (cCurrencyId == 0)
                    {
                        cCurrencyId = groupCurrencyId;
                        precision = groupPrecision;
                        currencyISO = groupCurrencyISO;
                        currencySymbol = groupCurrencySymbol;
                    }

                    groups.Add(new
                    {
                        paymentMethodName = paymentMethodName,
                        value = decimal.Round(scheduledAmount, groupPrecision),
                        scheduledAmount = decimal.Round(scheduledAmount, groupPrecision),
                        cCurrencyId = groupCurrencyId,
                        currencyISO = groupCurrencyISO,
                        currencySymbol = groupCurrencySymbol,
                        symbol = groupCurrencySymbol,
                        precision = groupPrecision
                    });
                }

                scheduledAmountThisWeek = decimal.Round(scheduledAmountThisWeek, precision);

                return Json(new
                {
                    title = GetMsg(ctx, "VAS_Scheduled", "Scheduled"),
                    badge = GetMsg(ctx, "VAS_Why", "WHY"),
                    description = GetMsg(ctx, "VAS_ScheduledForPaymentThisWeek", "Scheduled for payment this week"),
                    value = scheduledAmountThisWeek,
                    scheduledAmountThisWeek = scheduledAmountThisWeek,
                    groups = groups,
                    cCurrencyId = cCurrencyId,
                    currencyISO = currencyISO,
                    currencySymbol = currencySymbol,
                    symbol = currencySymbol,
                    precision = precision,
                    dateFrom = FormatDate(weekFrom),
                    dateTo = FormatDate(weekTo.AddDays(-1))
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