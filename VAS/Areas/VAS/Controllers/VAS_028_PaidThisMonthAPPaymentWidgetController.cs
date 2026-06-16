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

        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetPaidThisMonthRows(int pageNo = 1, int pageSize = 10)
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

            if (pageNo <= 0)
            {
                pageNo = 1;
            }

            if (pageSize <= 0)
            {
                pageSize = 10;
            }

            IDataReader dr = null;

            try
            {
                SqlQueryData queryData = BuildPaidThisMonthRowsSql(ctx, pageNo, pageSize);
                dr = DB.ExecuteReader(queryData.Sql, queryData.Parameters, null);

                List<object> rows = new List<object>();
                int totalRecords = 0;
                decimal totalPaid = 0;
                decimal largestPayment = 0;
                int precision = 2;
                int cCurrencyId = 0;
                string currencyISO = string.Empty;
                string currencySymbol = string.Empty;
                DateTime? dateFrom = null;
                DateTime? dateTo = null;

                while (dr != null && dr.Read())
                {
                    totalRecords = GetInt(dr, "TotalRecords", totalRecords);
                    totalPaid = GetDecimal(dr, "TotalPaid", totalPaid);
                    largestPayment = GetDecimal(dr, "LargestPayment", largestPayment);
                    precision = GetInt(dr, "StdPrecision", precision);
                    cCurrencyId = GetInt(dr, "C_Currency_ID", cCurrencyId);
                    currencyISO = GetString(dr, "CurrencyISO", currencyISO);
                    currencySymbol = GetString(dr, "CurrencySymbol", currencySymbol);

                    if (!dateFrom.HasValue && dr["DateFrom"] != DBNull.Value)
                    {
                        dateFrom = Util.GetValueOfDateTime(dr["DateFrom"]);
                    }

                    if (!dateTo.HasValue && dr["DateTo"] != DBNull.Value)
                    {
                        dateTo = Util.GetValueOfDateTime(dr["DateTo"]);
                    }

                    string statusType = GetStatusType(GetString(dr, "IsReconciled", string.Empty), GetString(dr, "VA009_ExecutionStatus", string.Empty));
                    string statusName = GetStatusMessage(ctx, statusType, GetString(dr, "ExecutionStatusName", string.Empty));

                    rows.Add(new
                    {
                        paymentId = GetInt(dr, "C_Payment_ID"),
                        documentNo = GetString(dr, "DocumentNo", string.Empty),
                        paymentDate = FormatNullableDate(Util.GetValueOfDateTime(dr["PaymentDate"])),
                        vendorName = GetString(dr, "VendorName", string.Empty),
                        bankName = GetString(dr, "BankName", string.Empty),
                        accountNo = GetString(dr, "AccountNo", string.Empty),
                        paymentMethodName = GetPaymentMethodName(ctx, GetString(dr, "PaymentMethodName", string.Empty)),
                        docStatus = GetString(dr, "DocStatus", string.Empty),
                        statusType = statusType,
                        statusName = statusName,
                        amount = GetDecimal(dr, "Amount", 0),
                        cCurrencyId = cCurrencyId,
                        currencyISO = currencyISO,
                        currencySymbol = currencySymbol,
                        stdPrecision = precision
                    });
                }

                return Json(new
                {
                    error = false,
                    title = GetMsg(ctx, "VAS_028_MessagePaidThisMonth", "Paid this month"),
                    subtitle = GetPaidThisMonthSubtitle(ctx, dateFrom, dateTo, totalRecords, totalPaid, currencySymbol, currencyISO, precision),
                    totalPaid = totalPaid,
                    paymentCount = totalRecords,
                    avgTicket = totalRecords > 0 ? decimal.Round(totalPaid / totalRecords, precision) : 0,
                    largestPayment = largestPayment,
                    cCurrencyId = cCurrencyId,
                    currencyISO = currencyISO,
                    currencySymbol = currencySymbol,
                    precision = precision,
                    dateFrom = dateFrom.HasValue ? FormatDate(dateFrom.Value) : "",
                    dateTo = dateTo.HasValue ? FormatDate(dateTo.Value) : "",
                    rows = rows,
                    pageNo = pageNo,
                    pageSize = pageSize,
                    totalRecords = totalRecords,
                    totalPages = pageSize == 0 ? 0 : Convert.ToInt32(Math.Ceiling((decimal)totalRecords / pageSize))
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
            DateTime monthStart = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            DateTime nextMonthStart = monthStart.AddMonths(1);

            string executionStatusFilter = HasPaymentExecutionStatusColumn()
                ? @"
AND COALESCE(Payment.VA009_ExecutionStatus, 'R') NOT IN ('B', 'C')"
                : string.Empty;

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
Payment.C_Payment_ID,
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
AND Payment.DocStatus IN ('CO', 'CL')
AND " + TruncColumn("Payment.DateAcct") + @" >= " + ToSqlDate(monthStart) + @"
AND " + TruncColumn("Payment.DateAcct") + @" < " + ToSqlDate(nextMonthStart) + executionStatusFilter;

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
Payment.C_Payment_ID,
Payment.AD_Client_ID,
Payment.C_BPartner_ID,
CASE
WHEN Payment.C_Currency_ID = SchemaCurrency.C_Currency_ID
THEN COALESCE(Payment.PayAmt, 0)
ELSE CurrencyConvert(
COALESCE(Payment.PayAmt, 0),
Payment.C_Currency_ID,
SchemaCurrency.C_Currency_ID,
Payment.DateAcct,
Payment.C_ConversionType_ID,
Payment.AD_Client_ID,
Payment.AD_Org_ID
)
END AS PaidAmount
FROM PaymentFiltered Payment
INNER JOIN SchemaCurrency SchemaCurrency ON (SchemaCurrency.AD_Client_ID = Payment.AD_Client_ID)
)";

            string sql = @"
WITH " + schemaCurrencySql + @",
" + paymentFilteredSql + @",
" + paidThisMonthDataSql + @"
SELECT
ROUND(CAST(COALESCE(SUM(PaidThisMonthData.PaidAmount), 0) AS NUMERIC), CAST(MAX(SchemaCurrency.StdPrecision) AS INTEGER)) AS PaidThisMonth,
COUNT(DISTINCT PaidThisMonthData.C_BPartner_ID) AS VendorCount,
COUNT(PaidThisMonthData.C_Payment_ID) AS PaymentCount,
MAX(SchemaCurrency.C_Currency_ID) AS C_Currency_ID,
MAX(SchemaCurrency.ISO_Code) AS CurrencyISO,
MAX(SchemaCurrency.Cur_Symbol) AS CurrencySymbol,
MAX(SchemaCurrency.StdPrecision) AS StdPrecision,
" + ToSqlDate(monthStart) + @" AS DateFrom,
" + ToSqlDate(nextMonthStart.AddDays(-1)) + @" AS DateTo
FROM SchemaCurrency SchemaCurrency
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


        private string ToSqlDate(DateTime date)
        {
            DateTime day = date.Date;

            if (DB.IsOracle())
            {
                return "TO_DATE('"
                    + day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    + "','YYYY-MM-DD')";
            }

            return DB.TO_DATE(day, true);
        }

        private string TruncColumn(string columnExpression)
        {
            if (DB.IsOracle())
            {
                return "TRUNC(" + columnExpression + ")";
            }

            return columnExpression;
        }


        private SqlQueryData BuildPaidThisMonthRowsSql(Ctx ctx, int pageNo, int pageSize)
        {
            string currentDateSql = GetCurrentDateSql();
            string dateToExclusiveSql = GetDateToExclusiveSql("PeriodRange.DateTo");
            string language = ctx.GetAD_Language();
            int offset = (pageNo - 1) * pageSize;

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
Payment.C_Payment_ID,
Payment.AD_Client_ID,
Payment.AD_Org_ID,
Payment.C_BPartner_ID,
Payment.C_BankAccount_ID,
Payment.C_Currency_ID,
Payment.C_ConversionType_ID,
Payment.DateAcct,
Payment.DocumentNo,
Payment.DocStatus,
Payment.IsReconciled,
Payment.PayAmt,
Payment.VA009_ExecutionStatus,
Payment.VA009_PaymentMethod_ID
FROM C_Payment Payment
WHERE Payment.IsActive = 'Y'
AND Payment.IsReceipt = 'N'
AND Payment.DocStatus IN ('CO', 'CL')" + executionStatusFilter;

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
),
StatusList AS
(
SELECT
RefList.Value,
COALESCE(RefListTrl.Name, RefList.Name) AS Name
FROM AD_Ref_List RefList
INNER JOIN AD_Reference ReferenceInfo ON (RefList.AD_Reference_ID = ReferenceInfo.AD_Reference_ID)
LEFT OUTER JOIN AD_Ref_List_Trl RefListTrl ON (RefList.AD_Ref_List_ID = RefListTrl.AD_Ref_List_ID AND RefListTrl.AD_Language = @AD_Language)
WHERE ReferenceInfo.Name = 'VA009_ExecutionStatus'
),
PaidRows AS
(
SELECT
Payment.C_Payment_ID,
Payment.DateAcct AS PaymentDate,
Payment.DocumentNo,
BPartner.Name AS VendorName,
Bank.Name AS BankName,
BankAccount.AccountNo,
PaymentMethod.VA009_Name AS PaymentMethodName,
Payment.DocStatus,
Payment.IsReconciled,
Payment.VA009_ExecutionStatus,
StatusList.Name AS ExecutionStatusName,
ROUND(CAST(COALESCE(CASE WHEN Payment.C_Currency_ID = SchemaCurrency.C_Currency_ID THEN COALESCE(Payment.PayAmt, 0) ELSE CurrencyConvert(COALESCE(Payment.PayAmt, 0), Payment.C_Currency_ID, SchemaCurrency.C_Currency_ID, Payment.DateAcct, Payment.C_ConversionType_ID, Payment.AD_Client_ID, Payment.AD_Org_ID) END, 0) AS NUMERIC), CAST(SchemaCurrency.StdPrecision AS INTEGER)) AS Amount,
SchemaCurrency.C_Currency_ID,
SchemaCurrency.StdPrecision,
SchemaCurrency.ISO_Code AS CurrencyISO,
SchemaCurrency.Cur_Symbol AS CurrencySymbol,
PeriodRange.DateFrom,
PeriodRange.DateTo
FROM PaymentFiltered Payment
INNER JOIN SchemaCurrency SchemaCurrency ON (SchemaCurrency.AD_Client_ID = Payment.AD_Client_ID)
INNER JOIN PeriodRange PeriodRange ON (Payment.DateAcct >= PeriodRange.DateFrom AND Payment.DateAcct < " + dateToExclusiveSql + @")
LEFT OUTER JOIN C_BPartner BPartner ON (Payment.C_BPartner_ID = BPartner.C_BPartner_ID)
LEFT OUTER JOIN C_BankAccount BankAccount ON (Payment.C_BankAccount_ID = BankAccount.C_BankAccount_ID)
LEFT OUTER JOIN C_Bank Bank ON (BankAccount.C_Bank_ID = Bank.C_Bank_ID)
LEFT OUTER JOIN VA009_PaymentMethod PaymentMethod ON (Payment.VA009_PaymentMethod_ID = PaymentMethod.VA009_PaymentMethod_ID)
LEFT OUTER JOIN StatusList StatusList ON (Payment.VA009_ExecutionStatus = StatusList.Value)
),
PaidRowsWithCount AS
(
SELECT
PaidRows.*,
COUNT(1) OVER () AS TotalRecords,
SUM(PaidRows.Amount) OVER () AS TotalPaid,
MAX(PaidRows.Amount) OVER () AS LargestPayment
FROM PaidRows PaidRows
)";

            string sql = @"
WITH " + periodRangeSql + @",
" + schemaCurrencySql + @",
" + paymentFilteredSql + @"
SELECT
PaidRowsWithCount.*
FROM PaidRowsWithCount PaidRowsWithCount
ORDER BY PaidRowsWithCount.PaymentDate DESC, PaidRowsWithCount.C_Payment_ID DESC
OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@AD_Language", language),
                new SqlParameter("@Offset", offset),
                new SqlParameter("@PageSize", pageSize)
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

        private string GetPaidThisMonthSubtitle(Ctx ctx, DateTime? dateFrom, DateTime? dateTo, int paymentCount, decimal totalPaid, string currencySymbol, string currencyISO, int precision)
        {
            string periodText = dateFrom.HasValue ? dateFrom.Value.ToString("MMM yyyy") : GetMsg(ctx, "VAS_028_MessageThisMonth", "This month");
            string paymentLabel = paymentCount == 1
                ? GetMsg(ctx, "VAS_028_MessagePayment", "payment")
                : GetMsg(ctx, "VAS_028_MessagePayments", "payments");

            return periodText +
                " · " +
                paymentCount.ToString() +
                " " +
                paymentLabel +
                " · Mtd " +
                FormatDisplayAmount(totalPaid, currencySymbol, currencyISO, precision);
        }

        private string FormatDisplayAmount(decimal value, string currencySymbol, string currencyISO, int precision)
        {
            string currency = !string.IsNullOrWhiteSpace(currencySymbol) ? currencySymbol : currencyISO;
            return currency + value.ToString("N" + precision);
        }

        private string GetStatusType(string isReconciled, string executionStatus)
        {
            if (isReconciled == "Y")
            {
                return "cleared";
            }

            if (!string.IsNullOrWhiteSpace(executionStatus))
            {
                return executionStatus;
            }

            return "intransit";
        }

        private string GetStatusMessage(Ctx ctx, string statusType, string executionStatusName)
        {
            if (statusType == "cleared")
            {
                return GetMsg(ctx, "VAS_032_MessageCleared", "Cleared");
            }

            if (!string.IsNullOrWhiteSpace(executionStatusName))
            {
                return executionStatusName;
            }

            return GetMsg(ctx, "VAS_032_MessageInTransit", "In transit");
        }

        private string GetPaymentMethodName(Ctx ctx, string paymentMethodName)
        {
            if (string.IsNullOrWhiteSpace(paymentMethodName))
            {
                return GetMsg(ctx, "VAS_032_MessageNotSpecified", "Not Specified");
            }

            return paymentMethodName;
        }

        private int GetInt(IDataReader reader, string columnName, int fallback = 0)
        {
            object value = reader[columnName];
            return value == null || value == DBNull.Value ? fallback : Util.GetValueOfInt(value);
        }

        private decimal GetDecimal(IDataReader reader, string columnName, decimal fallback)
        {
            object value = reader[columnName];
            return value == null || value == DBNull.Value ? fallback : Util.GetValueOfDecimal(value);
        }

        private string GetString(IDataReader reader, string columnName, string fallback)
        {
            object value = reader[columnName];
            return value == null || value == DBNull.Value ? fallback : Util.GetValueOfString(value);
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

        private string FormatNullableDate(DateTime? date)
        {
            return date.HasValue ? FormatDate(date.Value) : string.Empty;
        }

        private class SqlQueryData
        {
            public string Sql { get; set; }
            public SqlParameter[] Parameters { get; set; }
        }
    }
}
