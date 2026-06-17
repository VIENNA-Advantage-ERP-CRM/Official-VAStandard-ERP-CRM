using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
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
     * 1 | Upcoming runs | VAS_033_MessageUpcomingRuns
     * 2 | Next 7 days   | VAS_033_MessageNext7Days
     * 3 | payment       | VAS_033_MessagePayment
     * 4 | payments      | VAS_033_MessagePayments
     * 5 | Ytd           | VAS_033_MessageYTD
     * 6 | Month         | VAS_033_MessageMonth
     */
    public class VAS_033_UpcomingAPRunsWidgetController : Controller
    {
        public JsonResult GetUpcomingAPRuns()
        {
            string dateFilter = "YTD";

            if (Session["ctx"] == null)
            {
                return Json(new
                {
                    error = "Session Expired",
                    errorText = "Session Expired"
                }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            try
            {
                string sql = BuildUpcomingAPRunsSql(ctx, dateFilter);

                using (IDataReader dr = DB.ExecuteReader(sql))
                {
                    List<object> runs = new List<object>();

                    while (dr.Read() && runs.Count < 30)
                    {
                        DateTime? runDate = Util.GetValueOfDateTime(dr["RunDate"]);
                        int paymentCount = Util.GetValueOfInt(dr["PaymentCount"]);

                        runs.Add(new
                        {
                            paymentMethodId = Util.GetValueOfInt(dr["VA009_PaymentMethod_ID"]),
                            paymentMethodName = GetPaymentMethodName(ctx, Util.GetValueOfString(dr["PaymentMethodName"])),
                            runDate = FormatDate(runDate),
                            runDateText = FormatRunDate(runDate),
                            paymentCount = paymentCount,
                            paymentCountText = GetPaymentCountText(ctx, paymentCount),
                            amount = Util.GetValueOfDecimal(dr["Amount"]),
                            cCurrencyId = Util.GetValueOfInt(dr["C_Currency_ID"]),
                            currencyISO = Util.GetValueOfString(dr["CurrencyISO"]),
                            currencySymbol = Util.GetValueOfString(dr["CurrencySymbol"]),
                            stdPrecision = Util.GetValueOfInt(dr["StdPrecision"])
                        });
                    }

                    return Json(new
                    {
                        title = GetMsg(ctx, "VAS_033_MessageUpcomingRuns", "Upcoming runs"),
                        periodText = GetPeriodText(ctx, dateFilter),
                        runs = runs
                    }, JsonRequestBehavior.AllowGet);
                }
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    error = ex.Message,
                    errorText = ex.Message
                }, JsonRequestBehavior.AllowGet);
            }
        }

        private string BuildUpcomingAPRunsSql(Ctx ctx, string dateFilter)
        {
            string dateCondition = GetDateConditionSql(dateFilter);

            string schemaCurrencyCte = @"
SchemaCurrency AS (
    SELECT 
        ClientInfo.AD_Client_ID,
        AcctSchema.C_Currency_ID,
        Currency.StdPrecision,
        Currency.ISO_Code,
        COALESCE(Currency.CurSymbol, Currency.ISO_Code) AS Cur_Symbol
    FROM AD_ClientInfo ClientInfo
    INNER JOIN C_AcctSchema AcctSchema 
        ON ClientInfo.C_AcctSchema1_ID = AcctSchema.C_AcctSchema_ID
    INNER JOIN C_Currency Currency 
        ON AcctSchema.C_Currency_ID = Currency.C_Currency_ID
)";

            string paymentAccessSql = @"
SELECT p.C_Payment_ID
FROM C_Payment p
WHERE p.IsActive = 'Y'
AND p.IsReceipt = 'N'
" + dateCondition + @"
AND p.DocStatus NOT IN ('VO', 'RE')";

            /*
             * MRole must be applied only on the main physical table C_Payment p.
             * Do not apply MRole on the final WITH query.
             * Do not apply MRole on CTE aliases.
             * Do not apply MRole on joined aliases.
             */
            paymentAccessSql = MRole.GetDefault(ctx).AddAccessSQL(
                paymentAccessSql,
                "p",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            string filteredPaymentCte = @"
FilteredPayment AS (
" + paymentAccessSql + @"
)";

            string finalSql = @"
WITH " + schemaCurrencyCte + @",
" + filteredPaymentCte + @"
SELECT 
                    p.DateTrx AS RunDate,
                    COALESCE(p.VA009_PaymentMethod_ID, 0) AS VA009_PaymentMethod_ID,
                    pm.VA009_Name AS PaymentMethodName,
                    COUNT(1) AS PaymentCount,

    ROUND(
        CAST(
            COALESCE(
                SUM(
                    CASE 
                        WHEN p.C_Currency_ID = SchemaCurrency.C_Currency_ID 
                        THEN COALESCE(p.PayAmt, 0)
                        ELSE CurrencyConvert(
                            COALESCE(p.PayAmt, 0),
                            p.C_Currency_ID,
                            SchemaCurrency.C_Currency_ID,
                            p.DateTrx,
                            p.C_ConversionType_ID,
                            p.AD_Client_ID,
                            p.AD_Org_ID
                        )
                    END
                ), 
            0) AS NUMERIC
        ),
        CAST(MAX(SchemaCurrency.StdPrecision) AS INTEGER)
    ) AS Amount,

    MAX(SchemaCurrency.C_Currency_ID) AS C_Currency_ID,
    MAX(SchemaCurrency.StdPrecision) AS StdPrecision,
    MAX(SchemaCurrency.ISO_Code) AS CurrencyISO,
    MAX(SchemaCurrency.Cur_Symbol) AS CurrencySymbol

FROM C_Payment p
INNER JOIN FilteredPayment fp 
    ON p.C_Payment_ID = fp.C_Payment_ID
INNER JOIN SchemaCurrency SchemaCurrency 
    ON SchemaCurrency.AD_Client_ID = p.AD_Client_ID
LEFT OUTER JOIN VA009_PaymentMethod pm 
    ON p.VA009_PaymentMethod_ID = pm.VA009_PaymentMethod_ID

GROUP BY 
    p.DateTrx,
    COALESCE(p.VA009_PaymentMethod_ID, 0),
    pm.VA009_Name

ORDER BY 
    p.DateTrx ASC,
    pm.VA009_Name ASC";

            return finalSql;
        }

        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetUpcomingAPRunDetails(string runDate, int paymentMethodId = 0)
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
            DateTime parsedRunDate;

            if (!DateTime.TryParse(runDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedRunDate))
            {
                return Json(new
                {
                    error = true,
                    errorText = "Invalid run date"
                }, JsonRequestBehavior.AllowGet);
            }

            IDataReader dr = null;

            try
            {
                string paymentAccessSql = @"
SELECT p.C_Payment_ID
FROM C_Payment p
WHERE p.IsActive = 'Y'
AND p.IsReceipt = 'N'
AND p.DocStatus NOT IN ('VO', 'RE')
AND " + TruncColumn("p.DateTrx") + " = " + GetDateValue(parsedRunDate) + @"
AND COALESCE(p.VA009_PaymentMethod_ID, 0) = @PaymentMethod_ID";

                paymentAccessSql = MRole.GetDefault(ctx).AddAccessSQL(
                    paymentAccessSql,
                    "p",
                    MRole.SQL_FULLYQUALIFIED,
                    MRole.SQL_RO
                );

                string sql = @"
WITH FilteredPayment AS (
" + paymentAccessSql + @"
)
SELECT
    p.C_Payment_ID,
    p.DocumentNo,
    p.AD_Org_ID,
    org.Name AS OrganizationName,
    p.C_BPartner_ID,
    bp.Name AS VendorName,
    p.C_BankAccount_ID,
    COALESCE(bank.Name, ba.Name) AS BankName,
    ba.Name AS BankAccountName,
    ba.AccountNo AS BankAccountNo,
    p.C_Currency_ID,
    cur.ISO_Code AS CurrencyISO,
    COALESCE(cur.CurSymbol, cur.ISO_Code) AS CurrencySymbol,
    cur.StdPrecision,
    p.C_ConversionType_ID,
    ct.Name AS CurrencyTypeName,
    p.DateTrx,
    p.PayAmt,
    p.VA009_PaymentMethod_ID,
    pm.VA009_Name AS PaymentMethodName
FROM C_Payment p
INNER JOIN FilteredPayment fp ON p.C_Payment_ID = fp.C_Payment_ID
LEFT OUTER JOIN AD_Org org ON p.AD_Org_ID = org.AD_Org_ID
LEFT OUTER JOIN C_BPartner bp ON p.C_BPartner_ID = bp.C_BPartner_ID
LEFT OUTER JOIN C_BankAccount ba ON p.C_BankAccount_ID = ba.C_BankAccount_ID
LEFT OUTER JOIN C_Bank bank ON ba.C_Bank_ID = bank.C_Bank_ID
LEFT OUTER JOIN C_Currency cur ON p.C_Currency_ID = cur.C_Currency_ID
LEFT OUTER JOIN C_ConversionType ct ON p.C_ConversionType_ID = ct.C_ConversionType_ID
LEFT OUTER JOIN VA009_PaymentMethod pm ON p.VA009_PaymentMethod_ID = pm.VA009_PaymentMethod_ID
ORDER BY p.DateTrx ASC, p.DocumentNo ASC, p.C_Payment_ID ASC";

                dr = DB.ExecuteReader(sql, new[]
                {
                    new SqlParameter("@PaymentMethod_ID", paymentMethodId)
                }, null);

                List<object> rows = new List<object>();

                while (dr != null && dr.Read())
                {
                    DateTime? dateTrx = Util.GetValueOfDateTime(dr["DateTrx"]);

                    rows.Add(new
                    {
                        paymentId = Util.GetValueOfInt(dr["C_Payment_ID"]),
                        documentNo = Util.GetValueOfString(dr["DocumentNo"]),
                        organizationId = Util.GetValueOfInt(dr["AD_Org_ID"]),
                        organizationName = Util.GetValueOfString(dr["OrganizationName"]),
                        vendorId = Util.GetValueOfInt(dr["C_BPartner_ID"]),
                        vendorName = Util.GetValueOfString(dr["VendorName"]),
                        bankAccountId = Util.GetValueOfInt(dr["C_BankAccount_ID"]),
                        bankName = Util.GetValueOfString(dr["BankName"]),
                        bankAccountName = Util.GetValueOfString(dr["BankAccountName"]),
                        bankAccountNo = Util.GetValueOfString(dr["BankAccountNo"]),
                        cCurrencyId = Util.GetValueOfInt(dr["C_Currency_ID"]),
                        currencyISO = Util.GetValueOfString(dr["CurrencyISO"]),
                        currencySymbol = Util.GetValueOfString(dr["CurrencySymbol"]),
                        stdPrecision = Util.GetValueOfInt(dr["StdPrecision"]),
                        conversionTypeId = Util.GetValueOfInt(dr["C_ConversionType_ID"]),
                        currencyTypeName = Util.GetValueOfString(dr["CurrencyTypeName"]),
                        transactionDate = dateTrx.HasValue ? dateTrx.Value.ToString("yyyy-MM-dd") : string.Empty,
                        amount = Util.GetValueOfDecimal(dr["PayAmt"]),
                        paymentMethodId = Util.GetValueOfInt(dr["VA009_PaymentMethod_ID"]),
                        paymentMethodName = GetPaymentMethodName(ctx, Util.GetValueOfString(dr["PaymentMethodName"]))
                    });
                }

                return Json(new
                {
                    rows = rows
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

        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetPaymentPopupLookups()
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

            try
            {
                return Json(new
                {
                    organizations = ReadLookupRows(@"
SELECT AD_Org_ID AS ID, Name
FROM AD_Org
WHERE IsActive = 'Y'
AND AD_Client_ID IN (0, @AD_Client_ID)
ORDER BY Name", ctx),
                    bankAccounts = ReadLookupRows(@"
SELECT ba.C_BankAccount_ID AS ID,
       COALESCE(bank.Name, ba.Name) AS Name
FROM C_BankAccount ba
LEFT OUTER JOIN C_Bank bank ON ba.C_Bank_ID = bank.C_Bank_ID
WHERE ba.IsActive = 'Y'
AND ba.AD_Client_ID IN (0, @AD_Client_ID)
ORDER BY Name", ctx),
                    vendors = ReadLookupRows(@"
SELECT C_BPartner_ID AS ID, Name
FROM C_BPartner
WHERE IsActive = 'Y'
AND IsVendor = 'Y'
AND IsSummary = 'N'
AND AD_Client_ID IN (0, @AD_Client_ID)
ORDER BY Name", ctx),
                    currencies = ReadLookupRows(@"
SELECT C_Currency_ID AS ID, ISO_Code AS Name
FROM C_Currency
WHERE IsActive = 'Y'
ORDER BY ISO_Code", ctx),
                    conversionTypes = ReadLookupRows(@"
SELECT C_ConversionType_ID AS ID, Name
FROM C_ConversionType
WHERE IsActive = 'Y'
AND AD_Client_ID IN (0, @AD_Client_ID)
ORDER BY Name", ctx)
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
        }

        [HttpPost]
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult CreateUpcomingAPPayment(
            int paymentId,
            int adOrgId,
            int bankAccountId,
            int vendorId,
            int currencyId,
            int conversionTypeId,
            string transactionDate,
            string documentNo,
            decimal payAmt)
        {
            if (Session["ctx"] == null)
            {
                return Json(new
                {
                    success = false,
                    error = "Session Expired"
                });
            }

            Ctx ctx = Session["ctx"] as Ctx;
            DateTime dateTrx;

            if (paymentId <= 0)
            {
                return Json(new { success = false, error = "Payment is required" });
            }

            if (!DateTime.TryParse(transactionDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out dateTrx))
            {
                return Json(new { success = false, error = "Transaction date is required" });
            }

            if (payAmt <= 0)
            {
                return Json(new { success = false, error = "Payment amount must be greater than zero" });
            }

            try
            {
                MPayment payment = new MPayment(ctx, paymentId, null);

                if (payment.GetC_Payment_ID() <= 0)
                {
                    return Json(new { success = false, error = "Payment not found" });
                }

                payment.SetAD_Org_ID(adOrgId);
                payment.SetC_BankAccount_ID(bankAccountId);
                payment.SetC_BPartner_ID(vendorId);
                payment.SetC_Currency_ID(currencyId);
                payment.SetC_ConversionType_ID(conversionTypeId);
                payment.SetDateTrx(dateTrx);
                payment.SetDateAcct(dateTrx);
                payment.SetDocumentNo(documentNo);
                payment.SetPayAmt(payAmt);

                if (!payment.Save())
                {
                    return Json(new { success = false, error = "Could not save payment" });
                }

                return Json(new
                {
                    success = true,
                    paymentId = payment.GetC_Payment_ID()
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    error = ex.Message
                });
            }
        }

        private List<object> ReadLookupRows(string sql, Ctx ctx)
        {
            IDataReader dr = null;
            List<object> rows = new List<object>();

            try
            {
                SqlParameter[] parameters = sql.IndexOf("@AD_Client_ID", StringComparison.OrdinalIgnoreCase) >= 0
                    ? new[] { new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()) }
                    : null;

                dr = DB.ExecuteReader(sql, parameters, null);

                while (dr != null && dr.Read())
                {
                    rows.Add(new
                    {
                        id = Util.GetValueOfInt(dr["ID"]),
                        name = Util.GetValueOfString(dr["Name"])
                    });
                }
            }
            finally
            {
                if (dr != null)
                {
                    dr.Close();
                    dr.Dispose();
                }
            }

            return rows;
        }

        private string TruncColumn(string columnExpression)
        {
            if (DB.IsOracle())
            {
                return "TRUNC(" + columnExpression + ")";
            }

            return columnExpression;
        }

        private string GetDateConditionSql(string dateFilter)
        {
            dateFilter = string.IsNullOrEmpty(dateFilter)
                ? "NEXT7D"
                : dateFilter.ToUpperInvariant();

            if (dateFilter == "YTD")
            {
                return @"
AND p.DateTrx >= (
    SELECT MIN(pr.StartDate)
    FROM AD_ClientInfo ci
    INNER JOIN C_Year yr 
        ON yr.C_Calendar_ID = ci.C_Calendar_ID
    INNER JOIN C_Period cur 
        ON cur.C_Year_ID = yr.C_Year_ID
    INNER JOIN C_Period pr 
        ON pr.C_Year_ID = cur.C_Year_ID
    WHERE ci.AD_Client_ID = p.AD_Client_ID
    AND ci.IsActive = 'Y'
    AND cur.IsActive = 'Y'
    AND pr.IsActive = 'Y'
    AND " + GetCurrentDateSql() + @" BETWEEN cur.StartDate AND cur.EndDate
)
AND p.DateTrx < " + GetTomorrowDateSql();
            }

            if (dateFilter == "MONTH")
            {
                return @"
AND p.DateTrx >= (
    SELECT cur.StartDate
    FROM AD_ClientInfo ci
    INNER JOIN C_Year yr 
        ON yr.C_Calendar_ID = ci.C_Calendar_ID
    INNER JOIN C_Period cur 
        ON cur.C_Year_ID = yr.C_Year_ID
    WHERE ci.AD_Client_ID = p.AD_Client_ID
    AND ci.IsActive = 'Y'
    AND cur.IsActive = 'Y'
    AND " + GetCurrentDateSql() + @" BETWEEN cur.StartDate AND cur.EndDate
)
AND p.DateTrx <= (
    SELECT cur.EndDate
    FROM AD_ClientInfo ci
    INNER JOIN C_Year yr 
        ON yr.C_Calendar_ID = ci.C_Calendar_ID
    INNER JOIN C_Period cur 
        ON cur.C_Year_ID = yr.C_Year_ID
    WHERE ci.AD_Client_ID = p.AD_Client_ID
    AND ci.IsActive = 'Y'
    AND cur.IsActive = 'Y'
    AND " + GetCurrentDateSql() + @" BETWEEN cur.StartDate AND cur.EndDate
)";
            }

            return @"
AND p.DateTrx >= " + GetCurrentDateSql() + @"
AND p.DateTrx < " + GetNext7DateSql();
        }

        private string GetCurrentDateSql()
        {
            return GetDateValue(DateTime.Today);
        }

        private string GetTomorrowDateSql()
        {
            return GetDateValue(DateTime.Today.AddDays(1));
        }

        private string GetNext7DateSql()
        {
            return GetDateValue(DateTime.Today.AddDays(7));
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

        private string GetPeriodText(Ctx ctx, string dateFilter)
        {
            dateFilter = string.IsNullOrEmpty(dateFilter)
                ? "NEXT7D"
                : dateFilter.ToUpperInvariant();

            if (dateFilter == "YTD")
            {
                return GetMsg(ctx, "VAS_033_MessageYTD", "Ytd");
            }

            if (dateFilter == "MONTH")
            {
                return GetMsg(ctx, "VAS_033_MessageMonth", "Month");
            }

            return GetMsg(ctx, "VAS_033_MessageNext7Days", "Next 7 Days");
        }

        private string GetPaymentCountText(Ctx ctx, int paymentCount)
        {
            if (paymentCount == 1)
            {
                return paymentCount + " " + GetMsg(ctx, "VAS_033_MessagePayment", "payment");
            }

            return paymentCount + " " + GetMsg(ctx, "VAS_033_MessagePayments", "payments");
        }

        private string GetPaymentMethodName(Ctx ctx, string paymentMethodName)
        {
            if (string.IsNullOrEmpty(paymentMethodName))
            {
                return GetMsg(ctx, "VAS_032_MessageNotSpecified", "Not Specified");
            }

            return paymentMethodName;
        }

        private string FormatDate(DateTime? date)
        {
            if (date == null)
            {
                return string.Empty;
            }

            return date.Value.ToString("yyyy-MM-dd");
        }

        private string FormatRunDate(DateTime? date)
        {
            if (date == null)
            {
                return string.Empty;
            }

            return date.Value.ToString("ddd dd MMM", CultureInfo.InvariantCulture);
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
