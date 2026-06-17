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
    public class VAS_028_PaidThisMonthAPPaymentWidgetController : Controller
    {
        /// <summary>
        /// Gets total AP payments posted in the current calendar month.
        /// The KPI total is converted to the accounting schema currency.
        /// </summary>
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

                dr = DB.ExecuteReader(
                    queryData.Sql,
                    queryData.Parameters,
                    null
                );

                decimal paidThisMonth = 0;

                int vendorCount = 0;
                int paymentCount = 0;
                int cCurrencyId = 0;
                int precision = 2;

                string currencyISO = string.Empty;
                string currencySymbol = string.Empty;

                DateTime? dateFrom = null;
                DateTime? dateTo = null;

                if (dr != null && dr.Read())
                {
                    paidThisMonth = GetDecimal(
                        dr,
                        "PaidThisMonth",
                        0
                    );

                    vendorCount = GetInt(
                        dr,
                        "VendorCount"
                    );

                    paymentCount = GetInt(
                        dr,
                        "PaymentCount"
                    );

                    cCurrencyId = GetInt(
                        dr,
                        "C_Currency_ID"
                    );

                    precision = GetInt(
                        dr,
                        "StdPrecision",
                        2
                    );

                    currencyISO = GetString(
                        dr,
                        "CurrencyISO",
                        string.Empty
                    );

                    currencySymbol = GetString(
                        dr,
                        "CurrencySymbol",
                        currencyISO
                    );

                    if (dr["DateFrom"] != DBNull.Value)
                    {
                        dateFrom = Util.GetValueOfDateTime(
                            dr["DateFrom"]
                        );
                    }

                    if (dr["DateTo"] != DBNull.Value)
                    {
                        dateTo = Util.GetValueOfDateTime(
                            dr["DateTo"]
                        );
                    }
                }

                paidThisMonth = decimal.Round(
                    paidThisMonth,
                    precision
                );

                return Json(new
                {
                    title = GetMsg(
                        ctx,
                        "VAS_028_MessagePaidThisMonth",
                        "Paid this month"
                    ),

                    subtitle = GetPaidThisMonthSubtitle(
                        ctx,
                        dateFrom,
                        paymentCount,
                        paidThisMonth,
                        currencySymbol,
                        currencyISO,
                        precision
                    ),

                    description = GetMsg(
                        ctx,
                        "VAS_028_MessageOutgoingPaymentsPostedSoFar",
                        "Outgoing payments posted so far"
                    ),

                    value = paidThisMonth,
                    paidThisMonth = paidThisMonth,
                    totalPaidAmount = paidThisMonth,

                    vendorCount = vendorCount,
                    paymentCount = paymentCount,

                    cCurrencyId = cCurrencyId,
                    currencyISO = currencyISO,
                    currencySymbol = currencySymbol,
                    symbol = currencySymbol,
                    precision = precision,

                    dateFrom = FormatNullableDate(dateFrom),
                    dateTo = FormatNullableDate(dateTo)
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
        /// Returns one page of AP payments.
        /// Each row is returned using its original payment currency.
        /// No currency conversion is applied to the rows.
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetPaidThisMonthRows(
            int pageNo = 1,
            int pageSize = 10
        )
        {
            if (Session["ctx"] == null)
            {
                return Json(new
                {
                    error = true,
                    errorText = Msg.GetMsg(
                        Env.GetCtx(),
                        "SessionExpired"
                    ) ?? "Session Expired"
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
                SqlQueryData queryData = BuildPaidThisMonthRowsSql(
                    ctx,
                    pageNo,
                    pageSize
                );

                dr = DB.ExecuteReader(
                    queryData.Sql,
                    queryData.Parameters,
                    null
                );

                List<object> rows = new List<object>();

                int totalRecords = 0;

                DateTime? dateFrom = null;
                DateTime? dateTo = null;

                while (dr != null && dr.Read())
                {
                    totalRecords = GetInt(
                        dr,
                        "TotalRecords"
                    );

                    DateTime? paymentDate = null;

                    if (dr["PaymentDate"] != DBNull.Value)
                    {
                        paymentDate = Util.GetValueOfDateTime(
                            dr["PaymentDate"]
                        );
                    }

                    if (dr["DateFrom"] != DBNull.Value)
                    {
                        dateFrom = Util.GetValueOfDateTime(
                            dr["DateFrom"]
                        );
                    }

                    if (dr["DateTo"] != DBNull.Value)
                    {
                        dateTo = Util.GetValueOfDateTime(
                            dr["DateTo"]
                        );
                    }

                    int rowCurrencyId = GetInt(
                        dr,
                        "C_Currency_ID"
                    );

                    int rowPrecision = GetInt(
                        dr,
                        "StdPrecision",
                        2
                    );

                    string rowCurrencyISO = GetString(
                        dr,
                        "CurrencyISO",
                        string.Empty
                    );

                    string rowCurrencySymbol = GetString(
                        dr,
                        "CurrencySymbol",
                        rowCurrencyISO
                    );

                    string isReconciled = GetString(
                        dr,
                        "IsReconciled",
                        "N"
                    );

                    string executionStatus = GetString(
                        dr,
                        "VA009_ExecutionStatus",
                        string.Empty
                    );

                    string executionStatusName = GetString(
                        dr,
                        "ExecutionStatusName",
                        string.Empty
                    );

                    string statusType = GetStatusType(
                        isReconciled,
                        executionStatus
                    );

                    string paymentMethodName = GetPaymentMethodName(
                        ctx,
                        GetString(
                            dr,
                            "PaymentMethodName",
                            string.Empty
                        )
                    );

                    string vendorName = GetString(
                        dr,
                        "VendorName",
                        string.Empty
                    );

                    string formattedDate = paymentDate.HasValue
                        ? FormatDate(paymentDate.Value)
                        : string.Empty;

                    rows.Add(new
                    {
                        paymentId = GetInt(
                            dr,
                            "C_Payment_ID"
                        ),

                        paymentDate = formattedDate,
                        date = formattedDate,

                        documentNo = GetString(
                            dr,
                            "DocumentNo",
                            string.Empty
                        ),

                        vendorName = vendorName,
                        supplier = vendorName,

                        bankName = GetString(
                            dr,
                            "BankName",
                            string.Empty
                        ),

                        accountNo = GetString(
                            dr,
                            "AccountNo",
                            string.Empty
                        ),

                        /*
                         * Original payment amount.
                         * The value comes directly from C_Payment.PayAmt.
                         */
                        amount = GetDecimal(
                            dr,
                            "Amount",
                            0
                        ),

                        /*
                         * Original currency of this specific payment.
                         */
                        cCurrencyId = rowCurrencyId,
                        currencyISO = rowCurrencyISO,
                        currencySymbol = rowCurrencySymbol,
                        stdPrecision = rowPrecision,

                        paymentCurrency = rowCurrencyISO,
                        paymentCurrencySymbol = rowCurrencySymbol,

                        paymentMethodId = GetInt(
                            dr,
                            "VA009_PaymentMethod_ID"
                        ),

                        paymentMethodName = paymentMethodName,

                        isReconciled = isReconciled == "Y",
                        executionStatus = executionStatus,

                        statusType = statusType,

                        statusName = GetStatusMessage(
                            ctx,
                            statusType,
                            executionStatusName
                        )
                    });
                }

                int totalPages = totalRecords == 0
                    ? 0
                    : Convert.ToInt32(
                        Math.Ceiling(
                            (decimal)totalRecords / pageSize
                        )
                    );

                return Json(new
                {
                    title = GetMsg(
                        ctx,
                        "VAS_028_MessagePaidThisMonth",
                        "Paid this month"
                    ),

                    subtitle = GetRowsSubtitle(
                        ctx,
                        dateFrom,
                        totalRecords
                    ),

                    rows = rows,

                    pageNo = pageNo,
                    pageSize = pageSize,
                    totalRecords = totalRecords,
                    totalPages = totalPages,
                    paymentCount = totalRecords,

                    dateFrom = FormatNullableDate(dateFrom),
                    dateTo = FormatNullableDate(dateTo)
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
        /// Builds the KPI query.
        /// Amounts are converted to accounting schema currency.
        /// </summary>
        private SqlQueryData BuildPaidThisMonthSql(Ctx ctx)
        {
            DateTime monthStart = new DateTime(
                DateTime.Today.Year,
                DateTime.Today.Month,
                1
            );

            DateTime nextMonthStart = monthStart.AddMonths(1);

            string executionStatusFilter =
                HasPaymentExecutionStatusColumn()
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
CASE
WHEN Currency.CurSymbol IS NOT NULL
THEN Currency.CurSymbol
ELSE Currency.ISO_Code
END AS Cur_Symbol
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
AND Payment.AD_Client_ID = @AD_Client_ID
AND " + TruncColumn("Payment.DateAcct") + @" >= " + ToSqlDate(monthStart) + @"
AND " + TruncColumn("Payment.DateAcct") + @" < " + ToSqlDate(nextMonthStart)
                + executionStatusFilter;

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
ROUND(
CAST(COALESCE(SUM(PaidThisMonthData.PaidAmount), 0) AS NUMERIC),
CAST(MAX(SchemaCurrency.StdPrecision) AS INTEGER)
) AS PaidThisMonth,
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

            SqlParameter[] parameters =
            {
                new SqlParameter(
                    "@AD_Client_ID",
                    ctx.GetAD_Client_ID()
                )
            };

            return new SqlQueryData
            {
                Sql = sql,
                Parameters = parameters
            };
        }

        /// <summary>
        /// Builds the rows query.
        /// Each row uses C_Payment.PayAmt and C_Payment.C_Currency_ID.
        /// No CurrencyConvert is used.
        /// </summary>
        private SqlQueryData BuildPaidThisMonthRowsSql(
            Ctx ctx,
            int pageNo,
            int pageSize
        )
        {
            DateTime monthStart = new DateTime(
                DateTime.Today.Year,
                DateTime.Today.Month,
                1
            );

            DateTime nextMonthStart = monthStart.AddMonths(1);

            int offset = (pageNo - 1) * pageSize;

            bool hasExecutionStatus =
                HasPaymentExecutionStatusColumn();

            string executionStatusFilter = hasExecutionStatus
                ? @"
AND COALESCE(Payment.VA009_ExecutionStatus, 'R') NOT IN ('B', 'C')"
                : string.Empty;

            string executionStatusColumn = hasExecutionStatus
                ? "Payment.VA009_ExecutionStatus"
                : "NULL AS VA009_ExecutionStatus";

            string paymentAccessSql = @"
SELECT
Payment.C_Payment_ID,
Payment.AD_Client_ID,
Payment.AD_Org_ID,
Payment.C_BPartner_ID,
Payment.C_BankAccount_ID,
Payment.C_Currency_ID,
Payment.VA009_PaymentMethod_ID,
Payment.DateAcct,
Payment.DocumentNo,
Payment.DocStatus,
Payment.IsReconciled,
Payment.PayAmt,
" + executionStatusColumn + @"
FROM C_Payment Payment
WHERE Payment.IsActive = 'Y'
AND Payment.IsReceipt = 'N'
AND Payment.DocStatus IN ('CO', 'CL')
AND Payment.AD_Client_ID = @AD_Client_ID
AND " + TruncColumn("Payment.DateAcct") + @" >= " + ToSqlDate(monthStart) + @"
AND " + TruncColumn("Payment.DateAcct") + @" < " + ToSqlDate(nextMonthStart)
                + executionStatusFilter;

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

            string statusListSql;

            if (hasExecutionStatus)
            {
                statusListSql = @"
StatusList AS
(
SELECT
RefList.Value,
COALESCE(RefListTrl.Name, RefList.Name) AS Name
FROM AD_Ref_List RefList
INNER JOIN AD_Reference ReferenceInfo ON (RefList.AD_Reference_ID = ReferenceInfo.AD_Reference_ID)
LEFT OUTER JOIN AD_Ref_List_Trl RefListTrl ON (RefList.AD_Ref_List_ID = RefListTrl.AD_Ref_List_ID AND RefListTrl.AD_Language = @AD_Language)
WHERE ReferenceInfo.Name = 'VA009_ExecutionStatus'
)";
            }
            else
            {
                statusListSql = @"
StatusList AS
(
SELECT
NULL AS Value,
NULL AS Name
FROM AD_Reference ReferenceInfo
WHERE 1 = 0
)";
            }

            string paidRowsSql = @"
PaidRows AS
(
SELECT
Payment.C_Payment_ID,
Payment.DateAcct AS PaymentDate,
Payment.DocumentNo,
BPartner.Name AS VendorName,
COALESCE(Bank.Name, BankAccount.Name) AS BankName,
BankAccount.AccountNo,
Payment.VA009_PaymentMethod_ID,
PaymentMethod.VA009_Name AS PaymentMethodName,
Payment.DocStatus,
Payment.IsReconciled,
Payment.VA009_ExecutionStatus,
StatusList.Name AS ExecutionStatusName,
ROUND(
CAST(COALESCE(Payment.PayAmt, 0) AS NUMERIC),
CAST(PaymentCurrency.StdPrecision AS INTEGER)
) AS Amount,
Payment.C_Currency_ID,
PaymentCurrency.StdPrecision,
PaymentCurrency.ISO_Code AS CurrencyISO,
CASE
WHEN PaymentCurrency.CurSymbol IS NOT NULL
THEN PaymentCurrency.CurSymbol
ELSE PaymentCurrency.ISO_Code
END AS CurrencySymbol,
" + ToSqlDate(monthStart) + @" AS DateFrom,
" + ToSqlDate(nextMonthStart.AddDays(-1)) + @" AS DateTo
FROM PaymentFiltered Payment
INNER JOIN C_Currency PaymentCurrency ON (Payment.C_Currency_ID = PaymentCurrency.C_Currency_ID)
LEFT OUTER JOIN C_BPartner BPartner ON (Payment.C_BPartner_ID = BPartner.C_BPartner_ID)
LEFT OUTER JOIN C_BankAccount BankAccount ON (Payment.C_BankAccount_ID = BankAccount.C_BankAccount_ID)
LEFT OUTER JOIN C_Bank Bank ON (BankAccount.C_Bank_ID = Bank.C_Bank_ID)
LEFT OUTER JOIN VA009_PaymentMethod PaymentMethod ON (Payment.VA009_PaymentMethod_ID = PaymentMethod.VA009_PaymentMethod_ID)
LEFT OUTER JOIN StatusList StatusList ON (Payment.VA009_ExecutionStatus = StatusList.Value)
)";

            string paidRowsWithCountSql = @"
PaidRowsWithCount AS
(
SELECT
PaidRows.*,
COUNT(1) OVER () AS TotalRecords
FROM PaidRows PaidRows
)";

            string sql = @"
WITH " + paymentFilteredSql + @",
" + statusListSql + @",
" + paidRowsSql + @",
" + paidRowsWithCountSql + @"
SELECT
PaidRowsWithCount.C_Payment_ID,
PaidRowsWithCount.PaymentDate,
PaidRowsWithCount.DocumentNo,
PaidRowsWithCount.VendorName,
PaidRowsWithCount.BankName,
PaidRowsWithCount.AccountNo,
PaidRowsWithCount.VA009_PaymentMethod_ID,
PaidRowsWithCount.PaymentMethodName,
PaidRowsWithCount.DocStatus,
PaidRowsWithCount.IsReconciled,
PaidRowsWithCount.VA009_ExecutionStatus,
PaidRowsWithCount.ExecutionStatusName,
PaidRowsWithCount.Amount,
PaidRowsWithCount.C_Currency_ID,
PaidRowsWithCount.StdPrecision,
PaidRowsWithCount.CurrencyISO,
PaidRowsWithCount.CurrencySymbol,
PaidRowsWithCount.DateFrom,
PaidRowsWithCount.DateTo,
PaidRowsWithCount.TotalRecords
FROM PaidRowsWithCount PaidRowsWithCount
ORDER BY PaidRowsWithCount.PaymentDate DESC,
PaidRowsWithCount.C_Payment_ID DESC
OFFSET @Offset ROWS
FETCH NEXT @PageSize ROWS ONLY";

            List<SqlParameter> parameters =
                new List<SqlParameter>();

            parameters.Add(
                new SqlParameter(
                    "@AD_Client_ID",
                    ctx.GetAD_Client_ID()
                )
            );

            if (hasExecutionStatus)
            {
                parameters.Add(
                    new SqlParameter(
                        "@AD_Language",
                        ctx.GetAD_Language()
                    )
                );
            }

            parameters.Add(
                new SqlParameter(
                    "@Offset",
                    offset
                )
            );

            parameters.Add(
                new SqlParameter(
                    "@PageSize",
                    pageSize
                )
            );

            return new SqlQueryData
            {
                Sql = sql,
                Parameters = parameters.ToArray()
            };
        }

        private string ToSqlDate(DateTime date)
        {
            DateTime day = date.Date;

            if (DB.IsOracle())
            {
                return "TO_DATE('"
                    + day.ToString(
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture
                    )
                    + "','YYYY-MM-DD')";
            }

            return DB.TO_DATE(
                day,
                true
            );
        }

        private string TruncColumn(string columnExpression)
        {
            if (DB.IsOracle())
            {
                return "TRUNC("
                    + columnExpression
                    + ")";
            }

            return columnExpression;
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

            return Util.GetValueOfInt(
                DB.ExecuteScalar(sql)
            ) > 0;
        }

        private string GetPaidThisMonthSubtitle(
            Ctx ctx,
            DateTime? dateFrom,
            int paymentCount,
            decimal totalPaid,
            string currencySymbol,
            string currencyISO,
            int precision
        )
        {
            string periodText = dateFrom.HasValue
                ? dateFrom.Value.ToString(
                    "MMM yyyy",
                    CultureInfo.InvariantCulture
                )
                : GetMsg(
                    ctx,
                    "VAS_028_MessageThisMonth",
                    "This month"
                );

            string paymentLabel = paymentCount == 1
                ? GetMsg(
                    ctx,
                    "VAS_028_MessagePayment",
                    "payment"
                )
                : GetMsg(
                    ctx,
                    "VAS_028_MessagePayments",
                    "payments"
                );

            return periodText
                + " · "
                + paymentCount
                + " "
                + paymentLabel
                + " · MTD "
                + FormatDisplayAmount(
                    totalPaid,
                    currencySymbol,
                    currencyISO,
                    precision
                );
        }

        private string GetRowsSubtitle(
            Ctx ctx,
            DateTime? dateFrom,
            int paymentCount
        )
        {
            string periodText = dateFrom.HasValue
                ? dateFrom.Value.ToString(
                    "MMM yyyy",
                    CultureInfo.InvariantCulture
                )
                : GetMsg(
                    ctx,
                    "VAS_028_MessageThisMonth",
                    "This month"
                );

            string paymentLabel = paymentCount == 1
                ? GetMsg(
                    ctx,
                    "VAS_028_MessagePayment",
                    "payment"
                )
                : GetMsg(
                    ctx,
                    "VAS_028_MessagePayments",
                    "payments"
                );

            return periodText
                + " · "
                + paymentCount
                + " "
                + paymentLabel;
        }

        private string FormatDisplayAmount(
            decimal value,
            string currencySymbol,
            string currencyISO,
            int precision
        )
        {
            string currency =
                !string.IsNullOrWhiteSpace(currencySymbol)
                    ? currencySymbol
                    : currencyISO;

            return currency
                + value.ToString(
                    "N" + precision,
                    CultureInfo.InvariantCulture
                );
        }

        private string GetStatusType(
            string isReconciled,
            string executionStatus
        )
        {
            if (isReconciled == "Y")
            {
                return "cleared";
            }

            if (executionStatus == "B"
                || executionStatus == "C")
            {
                return "bounced";
            }

            return "intransit";
        }

        private string GetStatusMessage(
            Ctx ctx,
            string statusType,
            string executionStatusName
        )
        {
            if (statusType == "cleared")
            {
                return GetMsg(
                    ctx,
                    "VAS_032_MessageCleared",
                    "Cleared"
                );
            }

            if (statusType == "bounced")
            {
                return !string.IsNullOrWhiteSpace(
                    executionStatusName
                )
                    ? executionStatusName
                    : GetMsg(
                        ctx,
                        "VAS_032_MessageBounced",
                        "Bounced"
                    );
            }

            return GetMsg(
                ctx,
                "VAS_032_MessageInTransit",
                "In transit"
            );
        }

        private string GetPaymentMethodName(
            Ctx ctx,
            string paymentMethodName
        )
        {
            if (string.IsNullOrWhiteSpace(paymentMethodName))
            {
                return GetMsg(
                    ctx,
                    "VAS_032_MessageNotSpecified",
                    "Not Specified"
                );
            }

            return paymentMethodName;
        }

        private int GetInt(
            IDataReader reader,
            string columnName,
            int fallback = 0
        )
        {
            object value = reader[columnName];

            return value == null || value == DBNull.Value
                ? fallback
                : Util.GetValueOfInt(value);
        }

        private decimal GetDecimal(
            IDataReader reader,
            string columnName,
            decimal fallback
        )
        {
            object value = reader[columnName];

            return value == null || value == DBNull.Value
                ? fallback
                : Util.GetValueOfDecimal(value);
        }

        private string GetString(
            IDataReader reader,
            string columnName,
            string fallback
        )
        {
            object value = reader[columnName];

            return value == null || value == DBNull.Value
                ? fallback
                : Util.GetValueOfString(value);
        }

        private string GetMsg(
            Ctx ctx,
            string key,
            string fallback
        )
        {
            string message = Msg.GetMsg(
                ctx,
                key
            );

            return !string.IsNullOrEmpty(message)
                && message != "[" + key + "]"
                    ? message
                    : fallback;
        }

        private string FormatDate(DateTime date)
        {
            return date.ToString(
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture
            );
        }

        private string FormatNullableDate(DateTime? date)
        {
            return date.HasValue
                ? FormatDate(date.Value)
                : string.Empty;
        }

        private class SqlQueryData
        {
            public string Sql { get; set; }

            public SqlParameter[] Parameters { get; set; }
        }
    }
}