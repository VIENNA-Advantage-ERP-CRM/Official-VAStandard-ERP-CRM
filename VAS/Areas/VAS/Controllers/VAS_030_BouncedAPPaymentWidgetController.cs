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
    /// Purpose     : Provides bounced AP payment KPI widget data.
    /// Chronological development:
    ///   <EmployeeCode>   Created Date
    /// </summary>
    /*
     * Labels / Message Keys
     * 1 | Bounced       | VAS_030_MessageBounced
     * 2 | Action        | VAS_030_MessageAction
     * 3 | Need re-issue | VAS_030_MessageNeedReissue
     */
    public class VAS_030_BouncedAPPaymentWidgetController : Controller
    {
        /// <summary>
        /// Gets outgoing AP payments marked as bounced or rejected during the current financial period.
        /// Period is based on C_Period calendar linked with AD_ClientInfo.
        /// </summary>
        /// <returns>Bounced AP payment count and reporting date range.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetBouncedAPPayments()
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
                SqlQueryData queryData = BuildBouncedAPPaymentsSql(ctx);

                dr = DB.ExecuteReader(queryData.Sql, queryData.Parameters, null);

                int bouncedPaymentCount = 0;
                DateTime? dateFrom = null;
                DateTime? dateTo = null;

                if (dr != null && dr.Read())
                {
                    bouncedPaymentCount = Util.GetValueOfInt(dr["BouncedPaymentCount"]);

                    if (dr["DateFrom"] != DBNull.Value)
                    {
                        dateFrom = Util.GetValueOfDateTime(dr["DateFrom"]);
                    }

                    if (dr["DateTo"] != DBNull.Value)
                    {
                        dateTo = Util.GetValueOfDateTime(dr["DateTo"]);
                    }
                }

                return Json(new
                {
                    title = GetMsg(ctx, "VAS_030_MessageBounced", "Bounced"),
                    badge = GetMsg(ctx, "VAS_030_MessageAction", "Action"),
                    description = GetMsg(ctx, "VAS_030_MessageNeedReissue", "Need re-issue"),
                    value = bouncedPaymentCount,
                    bouncedPaymentCount = bouncedPaymentCount,
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
        public JsonResult GetBouncedAPPaymentRows(int pageNo = 1, int pageSize = 10)
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

            if (pageNo <= 0) { pageNo = 1; }
            if (pageSize <= 0) { pageSize = 10; }

            IDataReader dr = null;

            try
            {
                SqlQueryData queryData = BuildBouncedAPPaymentRowsSql(ctx, pageNo, pageSize);
                dr = DB.ExecuteReader(queryData.Sql, queryData.Parameters, null);

                List<object> rows = new List<object>();
                int totalRecords = 0;

                while (dr != null && dr.Read())
                {
                    totalRecords = Util.GetValueOfInt(dr["TotalRecords"]);

                    DateTime? paymentDate = Util.GetValueOfDateTime(dr["PaymentDate"]);

                    rows.Add(new
                    {
                        paymentId = Util.GetValueOfInt(dr["PaymentID"]),
                        paymentNo = Util.GetValueOfString(dr["PaymentNo"]),
                        paymentDate = paymentDate.HasValue ? paymentDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                        vendorName = Util.GetValueOfString(dr["VendorName"]),
                        bankName = Util.GetValueOfString(dr["BankName"]),
                        accountNo = Util.GetValueOfString(dr["AccountNo"]),
                        currency = Util.GetValueOfString(dr["CurrencyISO"]),
                        currencySymbol = Util.GetValueOfString(dr["CurrencySymbol"]),
                        amount = Util.GetValueOfDecimal(dr["Amount"]),
                        method = GetTenderTypeName(ctx, Util.GetValueOfString(dr["TenderType"])),
                        status = GetExecutionStatusName(ctx, Util.GetValueOfString(dr["ExecutionStatus"]))
                    });
                }

                return Json(new
                {
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
        private SqlQueryData BuildBouncedAPPaymentsSql(Ctx ctx)
        {
            string currentDateSql = GetCurrentDateSql();
            string dateToExclusiveSql = GetDateToExclusiveSql("PeriodRange.DateTo");

            string bouncedStatusFilter = HasPaymentExecutionStatusColumn()
                ? "AND Payment.VA009_ExecutionStatus IN ('" + X_C_Payment.VA009_EXECUTIONSTATUS_Bounced + "', '" + X_C_Payment.VA009_EXECUTIONSTATUS_Rejected + "')"
                : "AND 1 = 2";

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
AND " + currentDateSql + @" >= Period.StartDate
AND " + currentDateSql + @" < " + GetDateToExclusiveSql("Period.EndDate") + @"
)";

            string paymentAccessSql = @"
SELECT
Payment.C_Payment_ID,
Payment.DateAcct
FROM C_Payment Payment
WHERE Payment.IsActive = 'Y'
AND Payment.IsReceipt = 'N'
AND Payment.TenderType = 'K'
" + bouncedStatusFilter;

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
WITH " + periodRangeSql + @",
" + paymentFilteredSql + @"
SELECT
COUNT(DISTINCT Payment.C_Payment_ID) AS BouncedPaymentCount,
MIN(PeriodRange.DateFrom) AS DateFrom,
MAX(PeriodRange.DateTo) AS DateTo
FROM PaymentFiltered Payment
INNER JOIN PeriodRange PeriodRange ON
(
    Payment.DateAcct >= PeriodRange.DateFrom
    AND Payment.DateAcct < " + dateToExclusiveSql + @"
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

            return "CURRENT_DATE";
        }

        private string GetDateToExclusiveSql(string columnName)
        {
            return "CAST(" + columnName + " AS DATE) + 1";
        }

        private SqlQueryData BuildBouncedAPPaymentRowsSql(Ctx ctx, int pageNo, int pageSize)
        {
            bool hasExecutionStatusColumn = HasPaymentExecutionStatusColumn();
            string bouncedStatusFilter = hasExecutionStatusColumn
                ? "AND Payment.VA009_ExecutionStatus IN ('" + X_C_Payment.VA009_EXECUTIONSTATUS_Bounced + "', '" + X_C_Payment.VA009_EXECUTIONSTATUS_Rejected + "')"
                : "AND 1 = 2";
            string executionStatusSelect = hasExecutionStatusColumn
                ? "Payment.VA009_ExecutionStatus"
                : "NULL";

            int offset = (pageNo - 1) * pageSize;

            string periodRangeSql = @"
PeriodRange AS
(
SELECT
MIN(Period.StartDate) AS DateFrom,
MAX(Period.EndDate) AS DateTo,
MAX(CAST(Period.EndDate AS TIMESTAMP) + INTERVAL '1' DAY) AS DateToExclusive
FROM AD_ClientInfo ClientInfo
INNER JOIN C_Year YearData ON (YearData.C_Calendar_ID = ClientInfo.C_Calendar_ID)
INNER JOIN C_Period Period ON (Period.C_Year_ID = YearData.C_Year_ID)
WHERE ClientInfo.IsActive = 'Y'
AND ClientInfo.AD_Client_ID = @AD_Client_ID
AND CAST(CURRENT_TIMESTAMP AS TIMESTAMP) >= CAST(Period.StartDate AS TIMESTAMP)
AND CAST(CURRENT_TIMESTAMP AS TIMESTAMP) < CAST(Period.EndDate AS TIMESTAMP) + INTERVAL '1' DAY
)";

            string paymentAccessSql = @"
SELECT
Payment.C_Payment_ID AS PaymentID,
Payment.DocumentNo AS PaymentNo,
Payment.DateAcct AS PaymentDate,
BPartner.Name AS VendorName,
COALESCE(Bank.Name, BankAccount.Name) AS BankName,
COALESCE(BankAccount.AccountNo, N'') AS AccountNo,
Currency.ISO_Code AS CurrencyISO,
CASE WHEN Currency.CurSymbol IS NOT NULL THEN Currency.CurSymbol ELSE Currency.ISO_Code END AS CurrencySymbol,
Payment.PaymentAmount AS Amount,
Payment.TenderType,
" + executionStatusSelect + @" AS ExecutionStatus
FROM C_Payment Payment
INNER JOIN C_BankAccount BankAccount ON (Payment.C_BankAccount_ID = BankAccount.C_BankAccount_ID)
INNER JOIN C_Bank Bank ON (BankAccount.C_Bank_ID = Bank.C_Bank_ID)
INNER JOIN C_Currency Currency ON (Payment.C_Currency_ID = Currency.C_Currency_ID)
LEFT JOIN C_BPartner BPartner ON (Payment.C_BPartner_ID = BPartner.C_BPartner_ID)
WHERE Payment.IsActive = 'Y'
AND Payment.IsReceipt = 'N'
" + bouncedStatusFilter;

            paymentAccessSql = MRole.GetDefault(ctx).AddAccessSQL(
                paymentAccessSql,
                "Payment",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            string sql = @"
WITH " + periodRangeSql + @",
PaymentFiltered AS
(
" + paymentAccessSql + @"
),
RowsData AS
(
SELECT PaymentFiltered.*
FROM PaymentFiltered
INNER JOIN PeriodRange ON (
    PaymentFiltered.PaymentDate >= PeriodRange.DateFrom
    AND PaymentFiltered.PaymentDate < PeriodRange.DateToExclusive
)
),
CountData AS
(
SELECT COUNT(1) AS TotalRecords
FROM RowsData
)
SELECT RowsData.PaymentID,
RowsData.PaymentNo,
RowsData.PaymentDate,
RowsData.VendorName,
RowsData.BankName,
RowsData.AccountNo,
RowsData.CurrencyISO,
RowsData.CurrencySymbol,
RowsData.Amount,
RowsData.TenderType,
RowsData.ExecutionStatus,
CountData.TotalRecords
FROM RowsData
CROSS JOIN CountData
ORDER BY RowsData.PaymentDate DESC, RowsData.PaymentNo DESC
OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@Offset", offset),
                new SqlParameter("@PageSize", pageSize)
            };

            return new SqlQueryData
            {
                Sql = sql,
                Parameters = parameters
            };
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

        private string GetTenderTypeName(Ctx ctx, string tenderType)
        {
            switch (tenderType)
            {
                case "K":
                    return GetMsg(ctx, "Cheque", "Cheque");
                case "C":
                    return GetMsg(ctx, "Card", "Card");
                case "A":
                    return GetMsg(ctx, "ACH", "ACH");
                case "D":
                    return GetMsg(ctx, "DirectDebit", "Direct Debit");
                case "T":
                    return GetMsg(ctx, "BankTransfer", "Bank Transfer");
                default:
                    return GetMsg(ctx, "Other", "Other");
            }
        }

        private string GetExecutionStatusName(Ctx ctx, string executionStatus)
        {
            if (executionStatus == X_C_Payment.VA009_EXECUTIONSTATUS_Bounced)
            {
                return GetMsg(ctx, "VAS_030_MessageBounced", "Bounced");
            }

            if (executionStatus == X_C_Payment.VA009_EXECUTIONSTATUS_Rejected)
            {
                return GetMsg(ctx, "VAS_030_MessageRejected", "Rejected");
            }

            return GetMsg(ctx, "VAS_030_MessageNeedReissue", "Need re-issue");
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

        private class SqlQueryData
        {
            public string Sql { get; set; }
            public SqlParameter[] Parameters { get; set; }
        }
    }
}
