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
    public class ScheduledAPPaymentController : Controller
    {
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetScheduledAPPaymentThisWeek()
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
                int daysFromMonday = ((int)today.DayOfWeek + 6) % 7;
                DateTime weekFrom = today.AddDays(-daysFromMonday);
                DateTime weekTo = weekFrom.AddDays(7);

                string weekFromText = weekFrom.ToString("yyyy-MM-dd");
                string weekToText = weekTo.ToString("yyyy-MM-dd");

                bool hasPaymentMethod = HasInvoicePaymentMethodColumn();
                string paymentMethodNameColumn = hasPaymentMethod ? GetPaymentMethodNameColumn() : string.Empty;

                string paymentMethodSelect = string.Empty;
                string paymentMethodJoin = string.Empty;

                if (hasPaymentMethod && !string.IsNullOrEmpty(paymentMethodNameColumn))
                {
                    paymentMethodSelect = @"
                        inv.VA009_PaymentMethod_ID AS PaymentMethod_ID,
                        pm." + paymentMethodNameColumn + @" AS PaymentMethodName,";

                    paymentMethodJoin = @"
                    LEFT OUTER JOIN VA009_PaymentMethod pm 
                        ON (inv.VA009_PaymentMethod_ID = pm.VA009_PaymentMethod_ID)";
                }
                else if (hasPaymentMethod)
                {
                    paymentMethodSelect = @"
                        inv.VA009_PaymentMethod_ID AS PaymentMethod_ID,
                        inv.PaymentRule AS PaymentMethodName,";
                }
                else
                {
                    paymentMethodSelect = @"
                        0 AS PaymentMethod_ID,
                        inv.PaymentRule AS PaymentMethodName,";
                }

                string dateFilter = string.Empty;

                if (DB.IsOracle())
                {
                    dateFilter = @"
                    AND ips.DueDate >= TO_DATE('" + weekFromText + @"', 'YYYY-MM-DD')
                    AND ips.DueDate < TO_DATE('" + weekToText + @"', 'YYYY-MM-DD')
                    ";
                }
                else
                {
                    dateFilter = @"
                    AND ips.DueDate >= DATE '" + weekFromText + @"'
                    AND ips.DueDate < DATE '" + weekToText + @"'
                    ";
                }

                string invoiceBody = @"
                    SELECT
                        inv.C_Invoice_ID,
                        inv.C_Currency_ID,
                        cur.ISO_Code AS CurrencyISO,
                        cur.CurSymbol AS CurrencySymbol,"
                        + paymentMethodSelect + @"
                        COALESCE(ips.DueAmt, 0) AS ScheduledAmount
                    FROM C_Invoice inv
                    INNER JOIN C_InvoicePaySchedule ips 
                        ON (inv.C_Invoice_ID = ips.C_Invoice_ID)
                    LEFT OUTER JOIN C_Currency cur 
                        ON (inv.C_Currency_ID = cur.C_Currency_ID)"
                    + paymentMethodJoin + @"
                    WHERE inv.IsActive = 'Y'
                    AND ips.IsActive = 'Y'
                    AND inv.IsSOTrx = 'N'
                    AND inv.DocStatus IN ('CO', 'CL')
                    AND COALESCE(ips.DueAmt, 0) > 0
                    "
                    + dateFilter;

                invoiceBody = MRole.GetDefault(ctx).AddAccessSQL(
                    invoiceBody,
                    "inv",
                    MRole.SQL_FULLYQUALIFIED,
                    MRole.SQL_RO
                );

                string sql = @"
                    SELECT
                        x.PaymentMethod_ID,
                        x.PaymentMethodName,
                        x.C_Currency_ID,
                        x.CurrencyISO,
                        x.CurrencySymbol,
                        SUM(x.ScheduledAmount) AS ScheduledAmount
                    FROM (
                        " + invoiceBody + @"
                    ) x
                    GROUP BY
                        x.PaymentMethod_ID,
                        x.PaymentMethodName,
                        x.C_Currency_ID,
                        x.CurrencyISO,
                        x.CurrencySymbol
                    HAVING SUM(x.ScheduledAmount) > 0
                    ORDER BY ScheduledAmount DESC
                ";

                List<SqlParameter> parameters = new List<SqlParameter>();

                dr = DB.ExecuteReader(sql, parameters.ToArray());

                decimal scheduledAmountThisWeek = 0;
                int cCurrencyId = 0;
                string currencyISO = string.Empty;
                string currencySymbol = string.Empty;
                List<object> groups = new List<object>();

                while (dr.Read())
                {
                    decimal scheduledAmount = Util.GetValueOfDecimal(dr["ScheduledAmount"]);
                    int groupCurrencyId = Util.GetValueOfInt(dr["C_Currency_ID"]);
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
                        currencyISO = groupCurrencyISO;
                        currencySymbol = groupCurrencySymbol;
                    }

                    groups.Add(new
                    {
                        paymentMethodId = Util.GetValueOfInt(dr["PaymentMethod_ID"]),
                        paymentMethodName = paymentMethodName,
                        scheduledAmount = scheduledAmount,
                        cCurrencyId = groupCurrencyId,
                        currencyISO = groupCurrencyISO,
                        currencySymbol = groupCurrencySymbol
                    });
                }

                return Json(new
                {
                    title = GetMsg(ctx, "VAS_Scheduled", "Scheduled"),
                    badge = GetMsg(ctx, "VAS_Why", "WHY"),
                    description = GetMsg(ctx, "VAS_ScheduledForPaymentThisWeek", "Scheduled for payment this week"),
                    scheduledAmountThisWeek = scheduledAmountThisWeek,
                    cCurrencyId = cCurrencyId,
                    currencyISO = currencyISO,
                    currencySymbol = currencySymbol,
                    weekFrom = weekFromText,
                    weekTo = weekTo.AddDays(-1).ToString("yyyy-MM-dd"),
                    groups = groups
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

        private string GetPaymentMethodNameColumn()
        {
            if (HasColumn("VA009_PaymentMethod", "Name"))
            {
                return "Name";
            }

            if (HasColumn("VA009_PaymentMethod", "Value"))
            {
                return "Value";
            }

            if (HasColumn("VA009_PaymentMethod", "Description"))
            {
                return "Description";
            }

            return string.Empty;
        }

        private bool HasColumn(string tableName, string columnName)
        {
            string sql = @"
                SELECT COUNT(1)
                FROM AD_Table t
                INNER JOIN AD_Column c ON (t.AD_Table_ID = c.AD_Table_ID)
                WHERE t.TableName = '" + tableName + @"'
                AND c.ColumnName = '" + columnName + @"'
            ";

            return Util.GetValueOfInt(DB.ExecuteScalar(sql)) > 0;
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