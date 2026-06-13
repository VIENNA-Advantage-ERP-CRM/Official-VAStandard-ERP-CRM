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
                SqlQueryData queryData = BuildScheduledAPPaymentThisWeekSql(ctx);

                dr = DB.ExecuteReader(queryData.Sql, queryData.Parameters, null);

                decimal scheduledAmountThisWeek = 0;
                int cCurrencyId = 0;
                int precision = 2;
                string currencyISO = string.Empty;
                string currencySymbol = string.Empty;
                string dateFrom = string.Empty;
                string dateTo = string.Empty;
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

                    if (string.IsNullOrEmpty(dateFrom) && dr["DateFrom"] != DBNull.Value)
                    {
                        dateFrom = FormatDate(Util.GetValueOfDateTime(dr["DateFrom"]));
                    }

                    if (string.IsNullOrEmpty(dateTo) && dr["DateTo"] != DBNull.Value)
                    {
                        dateTo = FormatDate(Util.GetValueOfDateTime(dr["DateTo"]));
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
                    description = GetMsg(ctx, "VAS_029_MessageScheduledForPaymentThisWeek", "Scheduled for payment this week"),
                    value = scheduledAmountThisWeek,
                    scheduledAmountThisWeek = scheduledAmountThisWeek,
                    groups = groups,
                    cCurrencyId = cCurrencyId,
                    currencyISO = currencyISO,
                    currencySymbol = currencySymbol,
                    symbol = currencySymbol,
                    precision = precision,
                    dateFrom = dateFrom,
                    dateTo = dateTo
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

        private SqlQueryData BuildScheduledAPPaymentThisWeekSql(Ctx ctx)
        {
            bool hasPaymentMethod = HasInvoicePaymentMethodColumn();
            bool hasPaymentMethodVA009Name = hasPaymentMethod && HasPaymentMethodVA009NameColumn();
            bool hasPaymentMethodName = hasPaymentMethod && HasPaymentMethodNameColumn();
            bool hasPaymentMethodValue = hasPaymentMethod && HasPaymentMethodValueColumn();

            string weekStartSql = GetWeekStartSql();
            string weekEndExclusiveSql = GetWeekEndExclusiveSql();
            string weekEndDisplaySql = GetWeekEndDisplaySql();

            string paymentMethodDisplayColumn = string.Empty;
            string paymentMethodDisplayCondition = string.Empty;

            if (hasPaymentMethodVA009Name)
            {
                paymentMethodDisplayColumn = GetTextSql("PaymentMethod.VA009_Name");
                paymentMethodDisplayCondition = "PaymentMethod.VA009_Name IS NOT NULL";
            }
            else if (hasPaymentMethodName)
            {
                paymentMethodDisplayColumn = GetTextSql("PaymentMethod.Name");
                paymentMethodDisplayCondition = "PaymentMethod.Name IS NOT NULL";
            }
            else if (hasPaymentMethodValue)
            {
                paymentMethodDisplayColumn = GetTextSql("PaymentMethod.Value");
                paymentMethodDisplayCondition = "PaymentMethod.Value IS NOT NULL";
            }

            string invoicePaymentRuleSql = GetTextSql("Invoice.PaymentRule");
            string emptyTextSql = GetEmptyTextSql();

            string paymentMethodIdSelect = hasPaymentMethod
                ? "COALESCE(Invoice.VA009_PaymentMethod_ID, 0)"
                : "0";

            string paymentMethodNameSelect = hasPaymentMethod && !string.IsNullOrEmpty(paymentMethodDisplayColumn)
                ? @"CASE WHEN " + paymentMethodDisplayCondition + @" THEN " + paymentMethodDisplayColumn + @" WHEN Invoice.PaymentRule IS NOT NULL THEN " + invoicePaymentRuleSql + @" ELSE " + emptyTextSql + @" END"
                : @"CASE WHEN Invoice.PaymentRule IS NOT NULL THEN " + invoicePaymentRuleSql + @" ELSE " + emptyTextSql + @" END";

            string paymentMethodJoin = hasPaymentMethod && !string.IsNullOrEmpty(paymentMethodDisplayColumn)
                ? @"
LEFT OUTER JOIN VA009_PaymentMethod PaymentMethod ON (Invoice.VA009_PaymentMethod_ID = PaymentMethod.VA009_PaymentMethod_ID)"
                : string.Empty;

            string weekRangeSql = @"
WeekRange AS
(
SELECT
" + weekStartSql + @" AS DateFrom,
" + weekEndExclusiveSql + @" AS DateToExclusive,
" + weekEndDisplaySql + @" AS DateTo
FROM AD_ClientInfo ClientInfo
WHERE ClientInfo.IsActive = 'Y'
AND ClientInfo.AD_Client_ID = @AD_Client_ID
)";

            string schemaCurrencySql = @"
SchemaCurrency AS
(
SELECT
ClientInfo.AD_Client_ID,
AcctSchema.C_Currency_ID AS C_Currency_ID,
Currency.StdPrecision,
" + GetTextSql("Currency.ISO_Code") + @" AS ISO_Code,
CASE WHEN Currency.CurSymbol IS NOT NULL THEN " + GetTextSql("Currency.CurSymbol") + @" ELSE " + GetTextSql("Currency.ISO_Code") + @" END AS Cur_Symbol
FROM AD_ClientInfo ClientInfo
INNER JOIN C_AcctSchema AcctSchema ON (ClientInfo.C_AcctSchema1_ID = AcctSchema.C_AcctSchema_ID)
INNER JOIN C_Currency Currency ON (AcctSchema.C_Currency_ID = Currency.C_Currency_ID)
WHERE ClientInfo.IsActive = 'Y'
AND ClientInfo.AD_Client_ID = @AD_Client_ID
)";

            string invoiceAccessSql = @"
SELECT
Invoice.C_Invoice_ID,
Invoice.AD_Client_ID,
Invoice.AD_Org_ID,
Invoice.C_BPartner_ID,
Invoice.C_Currency_ID,
Invoice.DateAcct,
Invoice.C_ConversionType_ID,
Invoice.IsReturnTrx,
Invoice.PaymentRule" + (hasPaymentMethod ? @",
Invoice.VA009_PaymentMethod_ID" : string.Empty) + @"
FROM C_Invoice Invoice
WHERE Invoice.IsActive = 'Y'
AND Invoice.AD_Client_ID = @AD_Client_ID
AND Invoice.IsSOTrx = 'N'
AND Invoice.DocStatus IN ('CO', 'CL')";

            /*
             * MRole Handling:
             * Apply MRole only on the main physical table C_Invoice Invoice.
             * Do not apply MRole on the final WITH query.
             * Do not apply MRole on CTE aliases.
             * Do not apply MRole on joined aliases.
             */
            invoiceAccessSql = MRole.GetDefault(ctx).AddAccessSQL(
                invoiceAccessSql,
                "Invoice",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            string invoiceFilteredSql = @"
InvoiceFiltered AS
(
" + invoiceAccessSql + @"
)";

            string scheduledDataSql = @"
ScheduledData AS
(
SELECT
Invoice.C_Invoice_ID,
Invoice.C_BPartner_ID,
SchemaCurrency.C_Currency_ID,
SchemaCurrency.ISO_Code AS CurrencyISO,
SchemaCurrency.Cur_Symbol AS CurrencySymbol,
SchemaCurrency.StdPrecision,
" + paymentMethodIdSelect + @" AS PaymentMethod_ID,
" + paymentMethodNameSelect + @" AS PaymentMethodName,
CASE WHEN COALESCE(Invoice.IsReturnTrx, 'N') = 'Y' THEN -CurrencyConvert(COALESCE(InvoicePaySchedule.DueAmt, 0), Invoice.C_Currency_ID, SchemaCurrency.C_Currency_ID, Invoice.DateAcct, Invoice.C_ConversionType_ID, Invoice.AD_Client_ID, Invoice.AD_Org_ID) ELSE CurrencyConvert(COALESCE(InvoicePaySchedule.DueAmt, 0), Invoice.C_Currency_ID, SchemaCurrency.C_Currency_ID, Invoice.DateAcct, Invoice.C_ConversionType_ID, Invoice.AD_Client_ID, Invoice.AD_Org_ID) END AS ScheduledAmount,
WeekRange.DateFrom,
WeekRange.DateTo
FROM InvoiceFiltered Invoice
INNER JOIN C_InvoicePaySchedule InvoicePaySchedule ON (Invoice.C_Invoice_ID = InvoicePaySchedule.C_Invoice_ID)
INNER JOIN SchemaCurrency SchemaCurrency ON (SchemaCurrency.AD_Client_ID = Invoice.AD_Client_ID)
INNER JOIN WeekRange WeekRange ON (InvoicePaySchedule.DueDate >= WeekRange.DateFrom AND InvoicePaySchedule.DueDate < WeekRange.DateToExclusive)" + paymentMethodJoin + @"
WHERE InvoicePaySchedule.IsActive = 'Y'
AND COALESCE(InvoicePaySchedule.VA009_IsPaid, 'N') <> 'Y'
AND COALESCE(InvoicePaySchedule.DueAmt, 0) > 0
)";

            string sql = @"
WITH " + weekRangeSql + @",
" + schemaCurrencySql + @",
" + invoiceFilteredSql + @",
" + scheduledDataSql + @"
SELECT
ScheduledData.PaymentMethod_ID,
ScheduledData.PaymentMethodName,
ScheduledData.C_Currency_ID,
ScheduledData.CurrencyISO,
ScheduledData.CurrencySymbol,
MAX(ScheduledData.StdPrecision) AS StdPrecision,
ROUND(COALESCE(SUM(ScheduledData.ScheduledAmount), 0), MAX(ScheduledData.StdPrecision)) AS ScheduledAmount,
MIN(ScheduledData.DateFrom) AS DateFrom,
MAX(ScheduledData.DateTo) AS DateTo
FROM ScheduledData ScheduledData
GROUP BY
ScheduledData.PaymentMethod_ID,
ScheduledData.PaymentMethodName,
ScheduledData.C_Currency_ID,
ScheduledData.CurrencyISO,
ScheduledData.CurrencySymbol
HAVING SUM(ScheduledData.ScheduledAmount) > 0
ORDER BY ScheduledAmount DESC";

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

        private string GetWeekStartSql()
        {
            if (DB.IsOracle())
            {
                return "TRUNC(CURRENT_DATE, 'IW')";
            }

            return "DATE_TRUNC('week', CURRENT_DATE)";
        }

        private string GetWeekEndExclusiveSql()
        {
            if (DB.IsOracle())
            {
                return "TRUNC(CURRENT_DATE, 'IW') + 7";
            }

            return "DATE_TRUNC('week', CURRENT_DATE) + INTERVAL '7 days'";
        }

        private string GetWeekEndDisplaySql()
        {
            if (DB.IsOracle())
            {
                return "TRUNC(CURRENT_DATE, 'IW') + 6";
            }

            return "DATE_TRUNC('week', CURRENT_DATE) + INTERVAL '6 days'";
        }

        private string GetTextSql(string columnName)
        {
            return "TRIM(CAST(" + columnName + " AS CHAR(255)))";
        }

        private string GetEmptyTextSql()
        {
            return "CAST(NULL AS CHAR(1))";
        }

        private bool HasInvoicePaymentMethodColumn()
        {
            string sql = @"
SELECT
COUNT(1)
FROM AD_Table TableData
INNER JOIN AD_Column ColumnData ON (TableData.AD_Table_ID = ColumnData.AD_Table_ID)
WHERE TableData.TableName = 'C_Invoice'
AND ColumnData.ColumnName = 'VA009_PaymentMethod_ID'";

            return Util.GetValueOfInt(DB.ExecuteScalar(sql)) > 0;
        }

        private bool HasPaymentMethodVA009NameColumn()
        {
            string sql = @"
SELECT
COUNT(1)
FROM AD_Table TableData
INNER JOIN AD_Column ColumnData ON (TableData.AD_Table_ID = ColumnData.AD_Table_ID)
WHERE TableData.TableName = 'VA009_PaymentMethod'
AND ColumnData.ColumnName = 'VA009_Name'";

            return Util.GetValueOfInt(DB.ExecuteScalar(sql)) > 0;
        }

        private bool HasPaymentMethodNameColumn()
        {
            string sql = @"
SELECT
COUNT(1)
FROM AD_Table TableData
INNER JOIN AD_Column ColumnData ON (TableData.AD_Table_ID = ColumnData.AD_Table_ID)
WHERE TableData.TableName = 'VA009_PaymentMethod'
AND ColumnData.ColumnName = 'Name'";

            return Util.GetValueOfInt(DB.ExecuteScalar(sql)) > 0;
        }

        private bool HasPaymentMethodValueColumn()
        {
            string sql = @"
SELECT
COUNT(1)
FROM AD_Table TableData
INNER JOIN AD_Column ColumnData ON (TableData.AD_Table_ID = ColumnData.AD_Table_ID)
WHERE TableData.TableName = 'VA009_PaymentMethod'
AND ColumnData.ColumnName = 'Value'";

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
        private string FormatDate(DateTime? date)
        {
            return date.HasValue ? date.Value.ToString("yyyy-MM-dd") : "";
        }

        private class SqlQueryData
        {
            public string Sql { get; set; }
            public SqlParameter[] Parameters { get; set; }
        }
    }
}
