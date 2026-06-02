using System;
using System.Collections.Generic;
using System.Data;
using System.Web.Mvc;
using VAdvantage.Classes;
using VAdvantage.DataBase;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VIS.Controllers
{
    public class VAS_031_UpcomingAPRunsWidgetController : Controller
    {
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetUpcomingAPRuns()
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
                DateTime dateFrom = DateTime.Today;
                DateTime dateTo = dateFrom.AddDays(7);

                bool hasPaymentMethod = HasInvoicePaymentMethodColumn();
                bool hasPaymentMethodName = hasPaymentMethod && HasPaymentMethodNameColumn();

                string paymentMethodDisplayColumn = hasPaymentMethodName ? "pm.Name" : "pm.Value";

                string paymentMethodSelect = hasPaymentMethod
                    ? @"
                        inv.VA009_PaymentMethod_ID AS PaymentMethod_ID,
                        " + paymentMethodDisplayColumn + @" AS PaymentMethodName,"
                    : @"
                        0 AS PaymentMethod_ID,
                        inv.PaymentRule AS PaymentMethodName,";

                string paymentMethodJoin = hasPaymentMethod
                    ? @"
                    LEFT OUTER JOIN VA009_PaymentMethod pm ON (inv.VA009_PaymentMethod_ID = pm.VA009_PaymentMethod_ID)"
                    : string.Empty;

                string dateFilter = GetDateFilter("COALESCE(ips.DueDate, inv.DateAcct)", dateFrom, dateTo);

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

                string invoiceBody = @"
                    SELECT
                        inv.C_Invoice_ID,
                        inv.C_BPartner_ID,
                        bp.Name AS VendorName,
                        COALESCE(ips.DueDate, inv.DateAcct) AS DueDate,
                        SchemaCurrency.C_Currency_ID,
                        SchemaCurrency.ISO_Code AS CurrencyISO,
                        SchemaCurrency.Cur_Symbol AS CurrencySymbol,
                        SchemaCurrency.StdPrecision,"
                        + paymentMethodSelect + @"
                        CASE
                            WHEN (inv.GrandTotal - COALESCE(alloc.AllocatedAmt, 0)) <= 0 THEN 0

                            WHEN ips.C_InvoicePaySchedule_ID IS NOT NULL
                                AND COALESCE(ips.DueAmt, 0) > 0
                                AND ips.DueAmt < (inv.GrandTotal - COALESCE(alloc.AllocatedAmt, 0))
                            THEN
                                CASE
                                    WHEN inv.IsSOTrx = 'N' AND inv.IsReturnTrx = 'N'
                                    THEN CurrencyConvert(
                                        COALESCE(ips.DueAmt, 0),
                                        inv.C_Currency_ID,
                                        SchemaCurrency.C_Currency_ID,
                                        inv.DateAcct,
                                        inv.C_ConversionType_ID,
                                        inv.AD_Client_ID,
                                        inv.AD_Org_ID
                                    )

                                    WHEN inv.IsSOTrx = 'N' AND inv.IsReturnTrx = 'Y'
                                    THEN -CurrencyConvert(
                                        COALESCE(ips.DueAmt, 0),
                                        inv.C_Currency_ID,
                                        SchemaCurrency.C_Currency_ID,
                                        inv.DateAcct,
                                        inv.C_ConversionType_ID,
                                        inv.AD_Client_ID,
                                        inv.AD_Org_ID
                                    )

                                    ELSE 0
                                END

                            ELSE
                                CASE
                                    WHEN inv.IsSOTrx = 'N' AND inv.IsReturnTrx = 'N'
                                    THEN CurrencyConvert(
                                        COALESCE(inv.GrandTotal - COALESCE(alloc.AllocatedAmt, 0), 0),
                                        inv.C_Currency_ID,
                                        SchemaCurrency.C_Currency_ID,
                                        inv.DateAcct,
                                        inv.C_ConversionType_ID,
                                        inv.AD_Client_ID,
                                        inv.AD_Org_ID
                                    )

                                    WHEN inv.IsSOTrx = 'N' AND inv.IsReturnTrx = 'Y'
                                    THEN -CurrencyConvert(
                                        COALESCE(inv.GrandTotal - COALESCE(alloc.AllocatedAmt, 0), 0),
                                        inv.C_Currency_ID,
                                        SchemaCurrency.C_Currency_ID,
                                        inv.DateAcct,
                                        inv.C_ConversionType_ID,
                                        inv.AD_Client_ID,
                                        inv.AD_Org_ID
                                    )

                                    ELSE 0
                                END
                        END AS OpenAmount
                    FROM C_Invoice inv
                    INNER JOIN C_BPartner bp ON (inv.C_BPartner_ID = bp.C_BPartner_ID)
                    INNER JOIN SchemaCurrency SchemaCurrency
                        ON SchemaCurrency.AD_Client_ID = inv.AD_Client_ID
                    LEFT OUTER JOIN C_InvoicePaySchedule ips
                        ON (inv.C_Invoice_ID = ips.C_Invoice_ID AND ips.IsActive = 'Y')
                    LEFT OUTER JOIN (
                        SELECT
                            al.C_Invoice_ID,
                            SUM(COALESCE(al.Amount, 0) + COALESCE(al.DiscountAmt, 0) + COALESCE(al.WriteOffAmt, 0)) AS AllocatedAmt
                        FROM C_AllocationLine al
                        WHERE al.IsActive = 'Y'
                        AND al.C_Invoice_ID IS NOT NULL
                        GROUP BY al.C_Invoice_ID
                    ) alloc ON (inv.C_Invoice_ID = alloc.C_Invoice_ID)"
                    + paymentMethodJoin + @"
                    WHERE inv.IsActive = 'Y'
                    AND inv.IsSOTrx = 'N'
                    AND inv.DocStatus IN ('CO', 'CL')
                    "
                    + dateFilter + @"
                    AND (inv.GrandTotal - COALESCE(alloc.AllocatedAmt, 0)) > 0
                ";

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
                    UpcomingInvoices AS (
                        " + invoiceBody + @"
                    )
                    SELECT
                        DueDate,
                        PaymentMethod_ID,
                        PaymentMethodName,
                        C_Currency_ID,
                        CurrencyISO,
                        CurrencySymbol,
                        COUNT(1) AS PaymentCount,
                        MIN(VendorName) AS VendorName,
                        ROUND(
                            COALESCE(SUM(OpenAmount), 0),
                            MAX(StdPrecision)
                        ) AS TotalAmount
                    FROM UpcomingInvoices
                    WHERE OpenAmount > 0
                    GROUP BY
                        DueDate,
                        PaymentMethod_ID,
                        PaymentMethodName,
                        C_Currency_ID,
                        CurrencyISO,
                        CurrencySymbol
                    ORDER BY DueDate ASC, SUM(OpenAmount) DESC
                ";

                dr = DB.ExecuteReader(sql);

                List<object> runs = new List<object>();

                while (dr.Read())
                {
                    string paymentMethodName = Util.GetValueOfString(dr["PaymentMethodName"]);

                    if (string.IsNullOrEmpty(paymentMethodName))
                    {
                        paymentMethodName = GetMsg(ctx, "VAS_NotSpecified", "Not Specified");
                    }

                    runs.Add(new
                    {
                        dueDate = Util.GetValueOfDateTime(dr["DueDate"]),
                        paymentMethodId = Util.GetValueOfInt(dr["PaymentMethod_ID"]),
                        paymentMethodName = paymentMethodName,
                        paymentCount = Util.GetValueOfInt(dr["PaymentCount"]),
                        vendorName = Util.GetValueOfString(dr["VendorName"]),
                        totalAmount = Util.GetValueOfDecimal(dr["TotalAmount"]),
                        cCurrencyId = Util.GetValueOfInt(dr["C_Currency_ID"]),
                        currencyISO = Util.GetValueOfString(dr["CurrencyISO"]),
                        currencySymbol = Util.GetValueOfString(dr["CurrencySymbol"])
                    });
                }

                return Json(new
                {
                    title = GetMsg(ctx, "VAS_UpcomingRuns", "Upcoming runs"),
                    subTitle = GetMsg(ctx, "VAS_Next7Days", "Next 7 days"),
                    dateFrom = FormatDate(dateFrom),
                    dateTo = FormatDate(dateTo.AddDays(-1)),
                    runs = runs
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
