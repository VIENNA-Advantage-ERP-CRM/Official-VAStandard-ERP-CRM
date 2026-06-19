using System;
using System.Collections.Generic;
using System.Data;
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
    /// Purpose     : Provides scheduled AP payment KPI widget data grouped by payment method.
    /// Chronological development:
    ///   <EmployeeCode>   Created Date
    /// </summary>
    /*
     * Labels / Message Keys
     * 1 | Scheduled                       | VAS_029_MessageScheduled
     * 2 | WHY                             | VAS_029_MessageWhy
     * 3 | Scheduled for payment this week | VAS_029_MessageScheduledForPaymentThisWeek
     * 4 | Not Specified                   | VAS_029_MessageNotSpecified
     */
    public class VAS_029_ScheduledAPPaymentWidgetController : Controller
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
                int adClientId = ctx.GetAD_Client_ID();

                bool hasPaymentMethod = HasInvoicePaymentMethodColumn();
                bool hasPaymentMethodVA009Name = hasPaymentMethod && HasPaymentMethodVA009NameColumn();
                bool hasPaymentMethodName = hasPaymentMethod && HasPaymentMethodNameColumn();
                bool hasPaymentMethodValue = hasPaymentMethod && HasPaymentMethodValueColumn();

                string paymentMethodDisplayColumn = string.Empty;

                if (hasPaymentMethodVA009Name)
                {
                    paymentMethodDisplayColumn = "PaymentMethod.VA009_Name";
                }
                else if (hasPaymentMethodName)
                {
                    paymentMethodDisplayColumn = "PaymentMethod.Name";
                }
                else if (hasPaymentMethodValue)
                {
                    paymentMethodDisplayColumn = "PaymentMethod.Value";
                }

                string paymentMethodIdSelect = hasPaymentMethod
                    ? "COALESCE(Invoice.VA009_PaymentMethod_ID,0)"
                    : "0";

                string paymentMethodNameSelect = hasPaymentMethod && !string.IsNullOrEmpty(paymentMethodDisplayColumn)
                    ? @"CASE
        WHEN " + paymentMethodDisplayColumn + @" IS NOT NULL THEN " + paymentMethodDisplayColumn + @"
        WHEN Invoice.PaymentRule IS NOT NULL THEN Invoice.PaymentRule
        ELSE ''
    END"
                    : @"CASE
        WHEN Invoice.PaymentRule IS NOT NULL THEN Invoice.PaymentRule
        ELSE ''
    END";

                string paymentMethodJoin = hasPaymentMethod && !string.IsNullOrEmpty(paymentMethodDisplayColumn)
                    ? @"
LEFT OUTER JOIN VA009_PaymentMethod PaymentMethod ON (Invoice.VA009_PaymentMethod_ID=PaymentMethod.VA009_PaymentMethod_ID)"
                    : string.Empty;

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
AND ClientInfo.AD_Client_ID=" + adClientId;

                string invoiceBaseSql = @"
SELECT
    Invoice.C_Invoice_ID,
    Invoice.AD_Client_ID,
    Invoice.AD_Org_ID,
    Invoice.C_BPartner_ID,
    Invoice.C_Currency_ID,
    Invoice.DateAcct,
    Invoice.C_ConversionType_ID,
    Invoice.IsReturnTrx,
    Invoice.PaymentRule"
    + (hasPaymentMethod ? @",
    Invoice.VA009_PaymentMethod_ID" : string.Empty) + @"
FROM C_Invoice Invoice
WHERE Invoice.IsActive='Y'
AND Invoice.AD_Client_ID=" + adClientId + @"
AND Invoice.IsSOTrx='N'
AND Invoice.DocStatus IN ('CO','CL')";

                invoiceBaseSql = MRole.GetDefault(ctx).AddAccessSQL(
                    invoiceBaseSql,
                    "Invoice",
                    MRole.SQL_FULLYQUALIFIED,
                    MRole.SQL_RO
                );

                string invoiceBody = @"
SELECT
    Invoice.C_Invoice_ID,
    Invoice.C_BPartner_ID,
    SchemaCurrency.C_Currency_ID,
    SchemaCurrency.ISO_Code AS CurrencyISO,
    SchemaCurrency.Cur_Symbol AS CurrencySymbol,
    SchemaCurrency.StdPrecision,
    " + paymentMethodIdSelect + @" AS PaymentMethod_ID,
    " + paymentMethodNameSelect + @" AS PaymentMethodName,
    CASE
        WHEN COALESCE(Invoice.IsReturnTrx,'N')='Y' THEN -CurrencyConvert(
            COALESCE(ips.DueAmt,0),
            Invoice.C_Currency_ID,
            SchemaCurrency.C_Currency_ID,
            Invoice.DateAcct,
            Invoice.C_ConversionType_ID,
            Invoice.AD_Client_ID,
            Invoice.AD_Org_ID
        )
        ELSE CurrencyConvert(
            COALESCE(ips.DueAmt,0),
            Invoice.C_Currency_ID,
            SchemaCurrency.C_Currency_ID,
            Invoice.DateAcct,
            Invoice.C_ConversionType_ID,
            Invoice.AD_Client_ID,
            Invoice.AD_Org_ID
        )
    END AS ScheduledAmount
FROM (
    " + invoiceBaseSql + @"
) Invoice
INNER JOIN C_InvoicePaySchedule ips ON (Invoice.C_Invoice_ID=ips.C_Invoice_ID)
INNER JOIN SchemaCurrency SchemaCurrency ON (SchemaCurrency.AD_Client_ID=Invoice.AD_Client_ID)"
    + paymentMethodJoin + @"
WHERE ips.IsActive='Y'
AND COALESCE(ips.VA009_IsPaid,'N')<>'Y'
AND COALESCE(ips.DueAmt,0)>0
AND ips.DueDate>=" + GetDateValue(weekFrom) + @"
AND ips.DueDate<" + GetDateValue(weekTo);

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

                dr = DB.ExecuteReader(sql);

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
                        paymentMethodName = GetMsg(ctx, "VAS_029_MessageNotSpecified", "Not Specified");
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
                    title = GetMsg(ctx, "VAS_029_MessageScheduled", "Scheduled"),
                    badge = GetMsg(ctx, "VAS_029_MessageWhy", "WHY"),
                    description = GetMsg(ctx, "VAS_029_MessageScheduledForPaymentThisWeek", "Scheduled for payment this week"),
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

        private string GetDateValue(DateTime date)
        {
            string dateText = date.ToString("yyyy-MM-dd");

            if (DB.IsOracle())
            {
                return "TO_DATE('" + dateText + "', 'YYYY-MM-DD')";
            }

            return "'" + dateText + "'";
        }

        private bool HasInvoicePaymentMethodColumn()
        {
            string sql = @"
                SELECT COUNT(1)
                FROM AD_Table t
                INNER JOIN AD_Column c ON (t.AD_Table_ID = c.AD_Table_ID)
                WHERE t.TableName = 'C_Invoice'
                AND c.ColumnName = 'VA009_PaymentMethod_ID'
            ";

            return Util.GetValueOfInt(DB.ExecuteScalar(sql)) > 0;
        }

        private bool HasPaymentMethodVA009NameColumn()
        {
            string sql = @"
                SELECT COUNT(1)
                FROM AD_Table t
                INNER JOIN AD_Column c ON (t.AD_Table_ID = c.AD_Table_ID)
                WHERE t.TableName = 'VA009_PaymentMethod'
                AND c.ColumnName = 'VA009_Name'
            ";

            return Util.GetValueOfInt(DB.ExecuteScalar(sql)) > 0;
        }

        private bool HasPaymentMethodNameColumn()
        {
            string sql = @"
                SELECT COUNT(1)
                FROM AD_Table t
                INNER JOIN AD_Column c ON (t.AD_Table_ID = c.AD_Table_ID)
                WHERE t.TableName = 'VA009_PaymentMethod'
                AND c.ColumnName = 'Name'
            ";

            return Util.GetValueOfInt(DB.ExecuteScalar(sql)) > 0;
        }

        private bool HasPaymentMethodValueColumn()
        {
            string sql = @"
                SELECT COUNT(1)
                FROM AD_Table t
                INNER JOIN AD_Column c ON (t.AD_Table_ID = c.AD_Table_ID)
                WHERE t.TableName = 'VA009_PaymentMethod'
                AND c.ColumnName = 'Value'
            ";

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
    }
}
