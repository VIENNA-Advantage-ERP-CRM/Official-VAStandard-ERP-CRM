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

        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetUnreconciledAPPayments(int pageNo = 1, int pageSize = 5)
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

            if (pageNo <= 0) { pageNo = 1; }
            if (pageSize <= 0) { pageSize = 5; }

            int offset = (pageNo - 1) * pageSize;
            string currentDateSql = GetCurrentDateSql();

            string paymentsBaseSql = @"
SELECT
Payment.C_Payment_ID AS Payment_ID,
Payment.DateTrx AS Trx_Date,
Payment.DocumentNo AS Document_No,
BPartner.Name AS Vendor_Name,
COALESCE(Bank.Name, BankAccount.Name) AS Bank_Name,
COALESCE(BankAccount.AccountNo, '') AS Account_No,
Payment.PayAmt AS Pay_Amount,
Currency.ISO_Code AS Payment_Currency,
CASE WHEN Currency.CurSymbol IS NOT NULL THEN Currency.CurSymbol ELSE Currency.ISO_Code END AS Payment_Currency_Symbol,
Currency.StdPrecision AS Std_Precision,
PaymentMethod.VA009_Name AS Payment_Method,
CASE
    WHEN Payment.C_BankAccount_ID > 0 AND Payment.DocumentNo IS NOT NULL THEN 1
    ELSE 0
END AS Auto_Match_Candidate
FROM C_Payment Payment
INNER JOIN C_BPartner BPartner ON (BPartner.C_BPartner_ID = Payment.C_BPartner_ID)
LEFT OUTER JOIN C_BankAccount BankAccount ON (BankAccount.C_BankAccount_ID = Payment.C_BankAccount_ID)
LEFT OUTER JOIN C_Bank Bank ON (Bank.C_Bank_ID = BankAccount.C_Bank_ID)
INNER JOIN C_Currency Currency ON (Currency.C_Currency_ID = Payment.C_Currency_ID)
LEFT OUTER JOIN VA009_PaymentMethod PaymentMethod ON (PaymentMethod.VA009_PaymentMethod_ID = Payment.VA009_PaymentMethod_ID)
WHERE Payment.IsActive = 'Y'
AND Payment.IsReceipt = 'N'
AND Payment.DocStatus IN ('CO', 'CL')
AND COALESCE(Payment.IsReconciled, 'N') = 'N'";

            paymentsBaseSql = MRole.GetDefault(ctx).AddAccessSQL(
                paymentsBaseSql,
                "Payment",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            string sql = @"
WITH PaymentsData AS
(
" + paymentsBaseSql + @"
),
CountData AS
(
SELECT
COUNT(1) AS TotalRecords,
COALESCE(SUM(Pay_Amount), 0) AS TotalAmount,
MIN(Trx_Date) AS OldestDate,
COALESCE(SUM(Auto_Match_Candidate), 0) AS AutoMatchCandidates
FROM PaymentsData
)
SELECT
PaymentsData.Payment_ID,
PaymentsData.Trx_Date,
PaymentsData.Document_No,
PaymentsData.Vendor_Name,
PaymentsData.Bank_Name,
PaymentsData.Account_No,
PaymentsData.Pay_Amount,
PaymentsData.Payment_Currency,
PaymentsData.Payment_Currency_Symbol,
PaymentsData.Std_Precision,
PaymentsData.Payment_Method,
CountData.TotalRecords,
CountData.TotalAmount,
CountData.OldestDate,
CountData.AutoMatchCandidates,
" + currentDateSql + @" AS CurrentDate
FROM PaymentsData
CROSS JOIN CountData
ORDER BY PaymentsData.Trx_Date DESC, PaymentsData.Document_No DESC
OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

            List<SqlParameter> parameters = new List<SqlParameter>
            {
                new SqlParameter("@Offset", offset),
                new SqlParameter("@PageSize", pageSize)
            };

            List<object> rows = new List<object>();
            int totalRecords = 0;
            decimal totalAmount = 0;
            DateTime? oldestDate = null;
            DateTime currentDate = DateTime.Today;
            int autoMatchCandidates = 0;
            string summaryCurrencySymbol = "";
            string summaryCurrencyIso = "";
            int summaryPrecision = 2;

            IDataReader dr = null;

            try
            {
                dr = DB.ExecuteReader(sql, parameters.ToArray(), null);

                while (dr != null && dr.Read())
                {
                    totalRecords = Util.GetValueOfInt(dr["TotalRecords"]);
                    totalAmount = Util.GetValueOfDecimal(dr["TotalAmount"]);
                    autoMatchCandidates = Util.GetValueOfInt(dr["AutoMatchCandidates"]);

                    if (dr["OldestDate"] != DBNull.Value)
                    {
                        oldestDate = Util.GetValueOfDateTime(dr["OldestDate"]);
                    }

                    if (dr["CurrentDate"] != DBNull.Value)
                    {
                        DateTime? now = Util.GetValueOfDateTime(dr["CurrentDate"]);
                        if (now.HasValue)
                        {
                            currentDate = now.Value.Date;
                        }
                    }

                    DateTime? trxDate = Util.GetValueOfDateTime(dr["Trx_Date"]);
                    int rowPrecision = Util.GetValueOfInt(dr["Std_Precision"]);
                    decimal amount = Math.Round(
                        Util.GetValueOfDecimal(dr["Pay_Amount"]),
                        rowPrecision,
                        MidpointRounding.AwayFromZero
                    );

                    string currencyIso = Util.GetValueOfString(dr["Payment_Currency"]);
                    string currencySymbol = Util.GetValueOfString(dr["Payment_Currency_Symbol"]);

                    if (string.IsNullOrEmpty(summaryCurrencySymbol))
                    {
                        summaryCurrencySymbol = currencySymbol;
                        summaryCurrencyIso = currencyIso;
                        summaryPrecision = rowPrecision;
                    }

                    string documentNo = Util.GetValueOfString(dr["Document_No"]);
                    string bankName = Util.GetValueOfString(dr["Bank_Name"]);

                    rows.Add(new
                    {
                        paymentId = Util.GetValueOfInt(dr["Payment_ID"]),
                        date = trxDate.HasValue
                            ? trxDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                            : "",
                        paymentNo = documentNo,
                        vendor = Util.GetValueOfString(dr["Vendor_Name"]),
                        bankName = bankName,
                        accountNo = Util.GetValueOfString(dr["Account_No"]),
                        amount = amount,
                        currencyIso = currencyIso,
                        curSymbol = currencySymbol,
                        stdPrecision = rowPrecision,
                        method = Util.GetValueOfString(dr["Payment_Method"]),
                        whyUnreconciled = GetUnreconciledReason(currentDate, trxDate, documentNo, bankName)
                    });
                }

                int oldestDays = oldestDate.HasValue
                    ? Math.Max(0, Convert.ToInt32((currentDate.Date - oldestDate.Value.Date).TotalDays))
                    : 0;

                int autoMatchRate = totalRecords > 0
                    ? Convert.ToInt32(Math.Round((autoMatchCandidates * 100M) / totalRecords, 0, MidpointRounding.AwayFromZero))
                    : 0;

                return Json(new
                {
                    rows = rows,
                    pageNo = pageNo,
                    pageSize = pageSize,
                    totalRecords = totalRecords,
                    totalPages = pageSize == 0 ? 0 : Convert.ToInt32(Math.Ceiling((decimal)totalRecords / pageSize)),
                    totalAmount = Math.Round(totalAmount, summaryPrecision, MidpointRounding.AwayFromZero),
                    currencyIso = summaryCurrencyIso,
                    curSymbol = summaryCurrencySymbol,
                    stdPrecision = summaryPrecision,
                    oldestDays = oldestDays,
                    autoMatchRate = autoMatchRate
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
            string currentPeriodDateToExclusiveSql = GetDateToExclusiveSql("Period.EndDate");
            string periodRangeDateToExclusiveSql = GetDateToExclusiveSql("PeriodRange.DateTo");

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
AND " + currentDateSql + @" >= Period.StartDate
AND " + currentDateSql + @" < " + currentPeriodDateToExclusiveSql + @"
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
AND NOT EXISTS
(
SELECT 1
FROM C_Year LookupYear
INNER JOIN C_Period LookupPeriod ON (LookupPeriod.C_Year_ID = LookupYear.C_Year_ID)
WHERE LookupYear.C_Calendar_ID = CurrentPeriod.C_Calendar_ID
AND LookupPeriod.EndDate < CurrentPeriod.StartDate
AND LookupPeriod.EndDate > PreviousPeriod.EndDate
)
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
INNER JOIN PeriodRange PeriodRange ON
(
    Payment.DateTrx >= PeriodRange.DateFrom
    AND Payment.DateTrx < " + periodRangeDateToExclusiveSql + @"
)";

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
                return "CAST(CURRENT_DATE AS DATE)";
            }

            return "CAST(GETDATE() AS DATE)";
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

        private string GetUnreconciledReason(DateTime currentDate, DateTime? trxDate, string documentNo, string bankName)
        {
            if (string.IsNullOrWhiteSpace(bankName))
            {
                return "Missing bank account";
            }

            if (string.IsNullOrWhiteSpace(documentNo))
            {
                return "Reference mismatch";
            }

            if (trxDate.HasValue && (currentDate.Date - trxDate.Value.Date).TotalDays <= 3)
            {
                return "Awaiting bank statement";
            }

            return "Statement loaded - partial match";
        }

        private class SqlQueryData
        {
            public string Sql { get; set; }
            public SqlParameter[] Parameters { get; set; }
        }
    }
}
