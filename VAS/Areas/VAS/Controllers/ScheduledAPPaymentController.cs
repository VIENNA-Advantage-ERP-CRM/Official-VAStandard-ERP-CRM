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

                bool hasPaymentMethod = HasInvoicePaymentMethodColumn();

                string paymentMethodSelect = hasPaymentMethod
                    ? @"
                        inv.VA009_PaymentMethod_ID AS PaymentMethod_ID,
                        pm.Name AS PaymentMethodName,"
                    : @"
                        0 AS PaymentMethod_ID,
                        inv.PaymentRule AS PaymentMethodName,";

                string paymentMethodJoin = hasPaymentMethod
                    ? @"
                    LEFT OUTER JOIN VA009_PaymentMethod pm ON (inv.VA009_PaymentMethod_ID=pm.VA009_PaymentMethod_ID)"
                    : string.Empty;

                string invoiceBody = @"
                    SELECT
                        inv.C_Invoice_ID,
                        inv.C_Currency_ID,
                        cur.ISO_Code AS CurrencyISO,
                        cur.CurSymbol AS CurrencySymbol,"
                        + paymentMethodSelect + @"
                        CASE
                            WHEN (inv.GrandTotal-COALESCE(alloc.AllocatedAmt,0)) <= 0 THEN 0
                            WHEN ips.C_InvoicePaySchedule_ID IS NOT NULL
                                AND COALESCE(ips.DueAmt,0) > 0
                                AND ips.DueAmt < (inv.GrandTotal-COALESCE(alloc.AllocatedAmt,0)) THEN ips.DueAmt
                            ELSE (inv.GrandTotal-COALESCE(alloc.AllocatedAmt,0))
                        END AS ScheduledAmount
                    FROM C_Invoice inv
                    LEFT OUTER JOIN C_InvoicePaySchedule ips ON (inv.C_Invoice_ID=ips.C_Invoice_ID AND ips.IsActive='Y')
                    LEFT OUTER JOIN (
                        SELECT
                            al.C_Invoice_ID,
                            SUM(COALESCE(al.Amount,0)+COALESCE(al.DiscountAmt,0)+COALESCE(al.WriteOffAmt,0)) AS AllocatedAmt
                        FROM C_AllocationLine al
                        INNER JOIN C_AllocationHdr ah ON (al.C_AllocationHdr_ID=ah.C_AllocationHdr_ID)
                        WHERE ah.IsActive='Y'
                        AND ah.DocStatus IN ('CO', 'CL')
                        GROUP BY al.C_Invoice_ID
                    ) alloc ON (inv.C_Invoice_ID=alloc.C_Invoice_ID)
                    LEFT OUTER JOIN C_Currency cur ON (inv.C_Currency_ID=cur.C_Currency_ID)"
                    + paymentMethodJoin + @"
                    WHERE inv.IsActive='Y'
                    AND inv.IsSOTrx='N'
                    AND inv.DocStatus IN ('CO', 'CL')
                    AND COALESCE(ips.DueDate, inv.DateAcct)>=@WeekFrom
                    AND COALESCE(ips.DueDate, inv.DateAcct)<@WeekTo
                    AND (inv.GrandTotal-COALESCE(alloc.AllocatedAmt,0)) > 0
                ";

                invoiceBody = MRole.GetDefault(ctx).AddAccessSQL(invoiceBody, "inv", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                string sql = @"
                    WITH ScheduledInvoices AS (
                        " + invoiceBody + @"
                    )
                    SELECT
                        PaymentMethod_ID,
                        PaymentMethodName,
                        C_Currency_ID,
                        CurrencyISO,
                        CurrencySymbol,
                        SUM(ScheduledAmount) AS ScheduledAmount
                    FROM ScheduledInvoices
                    WHERE ScheduledAmount > 0
                    GROUP BY
                        PaymentMethod_ID,
                        PaymentMethodName,
                        C_Currency_ID,
                        CurrencyISO,
                        CurrencySymbol
                    ORDER BY SUM(ScheduledAmount) DESC
                ";

                List<SqlParameter> parameters = new List<SqlParameter>
                {
                    new SqlParameter("@WeekFrom", weekFrom),
                    new SqlParameter("@WeekTo", weekTo)
                };

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
                    weekFrom = weekFrom,
                    weekTo = weekTo.AddDays(-1),
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
                INNER JOIN AD_Column c ON (t.AD_Table_ID=c.AD_Table_ID)
                WHERE t.TableName='C_Invoice'
                AND c.ColumnName='VA009_PaymentMethod_ID'
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