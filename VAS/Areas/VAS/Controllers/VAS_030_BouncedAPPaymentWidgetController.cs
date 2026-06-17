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
    /// Purpose     : Provides bounced AP payment widget data.
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
        /// Returns the number of bounced or rejected AP cheque payments
        /// during the current financial period.
        /// </summary>
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
                SqlQueryData queryData =
                    BuildBouncedAPPaymentsSql(ctx);

                dr = DB.ExecuteReader(
                    queryData.Sql,
                    queryData.Parameters,
                    null
                );

                int bouncedPaymentCount = 0;
                DateTime? dateFrom = null;
                DateTime? dateTo = null;

                if (dr != null && dr.Read())
                {
                    bouncedPaymentCount = GetInt(
                        dr,
                        "BouncedPaymentCount"
                    );

                    dateFrom = GetNullableDate(
                        dr,
                        "DateFrom"
                    );

                    dateTo = GetNullableDate(
                        dr,
                        "DateTo"
                    );
                }

                return Json(new
                {
                    title = GetMsg(
                        ctx,
                        "VAS_030_MessageBounced",
                        "Bounced"
                    ),

                    badge = GetMsg(
                        ctx,
                        "VAS_030_MessageAction",
                        "Action"
                    ),

                    description = GetMsg(
                        ctx,
                        "VAS_030_MessageNeedReissue",
                        "Need re-issue"
                    ),

                    value = bouncedPaymentCount,
                    bouncedPaymentCount = bouncedPaymentCount,

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
        /// Returns one page of bounced or rejected AP cheque payments.
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetBouncedAPPaymentRows(
            int pageNo = 1,
            int pageSize = 10
        )
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
                SqlQueryData queryData =
                    BuildBouncedAPPaymentRowsSql(
                        ctx,
                        pageNo,
                        pageSize
                    );

                dr = DB.ExecuteReader(
                    queryData.Sql,
                    queryData.Parameters,
                    null
                );

                List<object> rows =
                    new List<object>();

                int totalRecords = 0;

                DateTime? dateFrom = null;
                DateTime? dateTo = null;

                while (dr != null && dr.Read())
                {
                    totalRecords = GetInt(
                        dr,
                        "TotalRecords"
                    );

                    DateTime? paymentDate =
                        GetNullableDate(
                            dr,
                            "PaymentDate"
                        );

                    dateFrom = GetNullableDate(
                        dr,
                        "DateFrom"
                    );

                    dateTo = GetNullableDate(
                        dr,
                        "DateTo"
                    );

                    string vendorName =
                        GetString(
                            dr,
                            "VendorName",
                            string.Empty
                        );

                    string currencyISO =
                        GetString(
                            dr,
                            "CurrencyISO",
                            string.Empty
                        );

                    string currencySymbol =
                        GetString(
                            dr,
                            "CurrencySymbol",
                            currencyISO
                        );

                    string tenderType =
                        GetString(
                            dr,
                            "TenderType",
                            string.Empty
                        );

                    string tenderTypeName =
                        GetString(
                            dr,
                            "TenderTypeName",
                            string.Empty
                        );

                    if (string.IsNullOrWhiteSpace(
                        tenderTypeName
                    ))
                    {
                        tenderTypeName = tenderType;
                    }

                    string paymentMethodName =
                        GetString(
                            dr,
                            "PaymentMethodName",
                            string.Empty
                        );

                    if (string.IsNullOrWhiteSpace(
                        paymentMethodName
                    ))
                    {
                        paymentMethodName =
                            tenderTypeName;
                    }

                    string executionStatus =
                        GetString(
                            dr,
                            "ExecutionStatus",
                            string.Empty
                        );

                    string statusName =
                        GetString(
                            dr,
                            "StatusName",
                            string.Empty
                        );

                    if (string.IsNullOrWhiteSpace(
                        statusName
                    ))
                    {
                        statusName =
                            executionStatus;
                    }

                    string formattedDate =
                        paymentDate.HasValue
                            ? FormatDate(
                                paymentDate.Value
                            )
                            : string.Empty;

                    rows.Add(new
                    {
                        paymentId = GetInt(
                            dr,
                            "PaymentID"
                        ),

                        paymentNo = GetString(
                            dr,
                            "PaymentNo",
                            string.Empty
                        ),

                        documentNo = GetString(
                            dr,
                            "PaymentNo",
                            string.Empty
                        ),

                        paymentDate = formattedDate,
                        date = formattedDate,

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

                        amount = GetDecimal(
                            dr,
                            "Amount",
                            0
                        ),

                        cCurrencyId = GetInt(
                            dr,
                            "C_Currency_ID"
                        ),

                        currency = currencyISO,
                        currencyISO = currencyISO,
                        paymentCurrency = currencyISO,

                        currencySymbol =
                            currencySymbol,

                        paymentCurrencySymbol =
                            currencySymbol,

                        stdPrecision = GetInt(
                            dr,
                            "StdPrecision",
                            2
                        ),

                        tenderType = tenderType,
                        tenderTypeName =
                            tenderTypeName,

                        paymentMethodId = GetInt(
                            dr,
                            "VA009_PaymentMethod_ID"
                        ),

                        paymentMethodName =
                            paymentMethodName,

                        method =
                            paymentMethodName,

                        executionStatus =
                            executionStatus,

                        statusType =
                            executionStatus,

                        statusName =
                            statusName,

                        status =
                            statusName
                    });
                }

                int totalPages =
                    totalRecords == 0
                        ? 0
                        : Convert.ToInt32(
                            Math.Ceiling(
                                (decimal)totalRecords /
                                pageSize
                            )
                        );

                return Json(new
                {
                    title = GetMsg(
                        ctx,
                        "VAS_030_MessageBounced",
                        "Bounced"
                    ),

                    rows = rows,

                    pageNo = pageNo,
                    pageSize = pageSize,
                    totalRecords = totalRecords,
                    totalPages = totalPages,

                    dateFrom =
                        FormatNullableDate(dateFrom),

                    dateTo =
                        FormatNullableDate(dateTo)
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
        /// Builds bounced AP payment count query.
        /// </summary>
        private SqlQueryData BuildBouncedAPPaymentsSql(
            Ctx ctx
        )
        {
            bool hasExecutionStatusColumn =
                HasPaymentExecutionStatusColumn();

            string bouncedStatusFilter =
                hasExecutionStatusColumn
                    ? @"
AND Payment.VA009_ExecutionStatus IN ('"
                        + X_C_Payment
                            .VA009_EXECUTIONSTATUS_Bounced
                        + "', '"
                        + X_C_Payment
                            .VA009_EXECUTIONSTATUS_Rejected
                        + "')"
                    : @"
AND 1 = 2";

            string periodRangeSql = @"
PeriodRange AS
(
SELECT
MIN(Period.StartDate) AS DateFrom,
MAX(Period.EndDate) AS DateTo
FROM AD_ClientInfo ClientInfo
INNER JOIN C_Year YearData ON
(
YearData.C_Calendar_ID =
ClientInfo.C_Calendar_ID
)
INNER JOIN C_Period Period ON
(
Period.C_Year_ID =
YearData.C_Year_ID
)
WHERE ClientInfo.IsActive = 'Y'
AND ClientInfo.AD_Client_ID =
@CountPeriodClientID
AND " + GetCurrentDateSql() + @" >=
Period.StartDate
AND " + GetCurrentDateSql() + @" <
" + GetDateToExclusiveSql(
                "Period.EndDate"
            ) + @"
)";

            string paymentAccessSql = @"
SELECT
Payment.C_Payment_ID,
Payment.DateAcct
FROM C_Payment Payment
WHERE Payment.IsActive = 'Y'
AND Payment.AD_Client_ID =
@CountPaymentClientID
AND Payment.IsReceipt = 'N'
AND Payment.TenderType = 'K'
" + bouncedStatusFilter;

            paymentAccessSql =
                MRole.GetDefault(ctx).AddAccessSQL(
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
WITH
" + periodRangeSql + @",
" + paymentFilteredSql + @"

SELECT
COUNT(
DISTINCT Payment.C_Payment_ID
) AS BouncedPaymentCount,
MIN(
PeriodRange.DateFrom
) AS DateFrom,
MAX(
PeriodRange.DateTo
) AS DateTo
FROM PaymentFiltered Payment
INNER JOIN PeriodRange PeriodRange ON
(
Payment.DateAcct >=
PeriodRange.DateFrom
AND Payment.DateAcct <
" + GetDateToExclusiveSql(
                "PeriodRange.DateTo"
            ) + @"
)";

            SqlParameter[] parameters =
            {
                new SqlParameter(
                    "@CountPeriodClientID",
                    ctx.GetAD_Client_ID()
                ),

                new SqlParameter(
                    "@CountPaymentClientID",
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
        /// Builds bounced AP payment details query.
        /// </summary>
        private SqlQueryData BuildBouncedAPPaymentRowsSql(
            Ctx ctx,
            int pageNo,
            int pageSize
        )
        {
            bool hasExecutionStatusColumn =
                HasPaymentExecutionStatusColumn();

            int offset =
                (pageNo - 1) * pageSize;

            string bouncedStatusFilter =
                hasExecutionStatusColumn
                    ? @"
AND Payment.VA009_ExecutionStatus IN ('"
                        + X_C_Payment
                            .VA009_EXECUTIONSTATUS_Bounced
                        + "', '"
                        + X_C_Payment
                            .VA009_EXECUTIONSTATUS_Rejected
                        + "')"
                    : @"
AND 1 = 2";

            string executionStatusColumn =
                hasExecutionStatusColumn
                    ? CastText(
                        "Payment.VA009_ExecutionStatus"
                    ) + " AS ExecutionStatus"
                    : CastText(
                        "NULL"
                    ) + " AS ExecutionStatus";

            string periodRangeSql = @"
PeriodRange AS
(
SELECT
MIN(Period.StartDate) AS DateFrom,
MAX(Period.EndDate) AS DateTo
FROM AD_ClientInfo ClientInfo
INNER JOIN C_Year YearData ON
(
YearData.C_Calendar_ID =
ClientInfo.C_Calendar_ID
)
INNER JOIN C_Period Period ON
(
Period.C_Year_ID =
YearData.C_Year_ID
)
WHERE ClientInfo.IsActive = 'Y'
AND ClientInfo.AD_Client_ID =
@RowsPeriodClientID
AND " + GetCurrentDateSql() + @" >=
Period.StartDate
AND " + GetCurrentDateSql() + @" <
" + GetDateToExclusiveSql(
                "Period.EndDate"
            ) + @"
)";

            string executionStatusListSql = @"
ExecutionStatusList AS
(
SELECT
" + CastText(
                "RefList.Value"
            ) + @" AS Value,
CASE
WHEN RefListTrl.Name IS NOT NULL
THEN " + CastText(
                "RefListTrl.Name"
            ) + @"
ELSE " + CastText(
                "RefList.Name"
            ) + @"
END AS StatusName
FROM AD_Reference ReferenceInfo
LEFT OUTER JOIN AD_Ref_List RefList ON
(
ReferenceInfo.AD_Reference_ID =
RefList.AD_Reference_ID
)
LEFT OUTER JOIN AD_Ref_List_Trl RefListTrl ON
(
RefList.AD_Ref_List_ID =
RefListTrl.AD_Ref_List_ID
AND RefListTrl.AD_Language =
@ExecutionLanguage
)
WHERE " + CastText(
                "ReferenceInfo.Name"
            ) + @" =
'VA009_ExecutionStatus'
AND ReferenceInfo.IsActive = 'Y'
AND RefList.IsActive = 'Y'
)";

            string tenderTypeListSql = @"
TenderTypeList AS
(
SELECT
" + CastText(
                "RefList.Value"
            ) + @" AS Value,
CASE
WHEN RefListTrl.Name IS NOT NULL
THEN " + CastText(
                "RefListTrl.Name"
            ) + @"
ELSE " + CastText(
                "RefList.Name"
            ) + @"
END AS TenderTypeName
FROM AD_Reference ReferenceInfo
LEFT OUTER JOIN AD_Ref_List RefList ON
(
ReferenceInfo.AD_Reference_ID =
RefList.AD_Reference_ID
)
LEFT OUTER JOIN AD_Ref_List_Trl RefListTrl ON
(
RefList.AD_Ref_List_ID =
RefListTrl.AD_Ref_List_ID
AND RefListTrl.AD_Language =
@TenderLanguage
)
WHERE " + CastText(
                "ReferenceInfo.Name"
            ) + @" =
'C_Payment TenderType'
AND ReferenceInfo.IsActive = 'Y'
AND RefList.IsActive = 'Y'
)";

            string paymentAccessSql = @"
SELECT
Payment.C_Payment_ID AS PaymentID,
Payment.DocumentNo AS PaymentNo,
Payment.DateAcct AS PaymentDate,
Payment.C_BPartner_ID,
Payment.C_BankAccount_ID,
Payment.C_Currency_ID,
Payment.VA009_PaymentMethod_ID,
Payment.PayAmt AS Amount,
" + CastText(
                "Payment.TenderType"
            ) + @" AS TenderType,
" + executionStatusColumn + @"
FROM C_Payment Payment
WHERE Payment.IsActive = 'Y'
AND Payment.AD_Client_ID =
@RowsPaymentClientID
AND Payment.IsReceipt = 'N'
AND Payment.TenderType = 'K'
" + bouncedStatusFilter;

            paymentAccessSql =
                MRole.GetDefault(ctx).AddAccessSQL(
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

            string rowsDataSql = @"
RowsData AS
(
SELECT
Payment.PaymentID,
" + CastText(
                "Payment.PaymentNo"
            ) + @" AS PaymentNo,
Payment.PaymentDate,

" + CastText(
                "BPartner.Name"
            ) + @" AS VendorName,

CASE
WHEN Bank.Name IS NOT NULL
THEN " + CastText(
                "Bank.Name"
            ) + @"
ELSE " + CastText(
                "BankAccount.Name"
            ) + @"
END AS BankName,

" + CastText(
                "BankAccount.AccountNo"
            ) + @" AS AccountNo,

Payment.C_Currency_ID,

" + CastText(
                "Currency.ISO_Code"
            ) + @" AS CurrencyISO,

CASE
WHEN Currency.CurSymbol IS NOT NULL
THEN " + CastText(
                "Currency.CurSymbol"
            ) + @"
ELSE " + CastText(
                "Currency.ISO_Code"
            ) + @"
END AS CurrencySymbol,

Currency.StdPrecision,
Payment.Amount,

Payment.TenderType,
TenderTypeList.TenderTypeName,

Payment.VA009_PaymentMethod_ID,

" + CastText(
                "PaymentMethod.VA009_Name"
            ) + @" AS PaymentMethodName,

Payment.ExecutionStatus,
ExecutionStatusList.StatusName,

PeriodRange.DateFrom,
PeriodRange.DateTo

FROM PaymentFiltered Payment

INNER JOIN PeriodRange PeriodRange ON
(
Payment.PaymentDate >=
PeriodRange.DateFrom
AND Payment.PaymentDate <
" + GetDateToExclusiveSql(
                "PeriodRange.DateTo"
            ) + @"
)

LEFT OUTER JOIN C_BPartner BPartner ON
(
Payment.C_BPartner_ID =
BPartner.C_BPartner_ID
)

LEFT OUTER JOIN C_BankAccount BankAccount ON
(
Payment.C_BankAccount_ID =
BankAccount.C_BankAccount_ID
)

LEFT OUTER JOIN C_Bank Bank ON
(
BankAccount.C_Bank_ID =
Bank.C_Bank_ID
)

LEFT OUTER JOIN C_Currency Currency ON
(
Payment.C_Currency_ID =
Currency.C_Currency_ID
)

LEFT OUTER JOIN VA009_PaymentMethod PaymentMethod ON
(
Payment.VA009_PaymentMethod_ID =
PaymentMethod.VA009_PaymentMethod_ID
)

LEFT OUTER JOIN ExecutionStatusList ExecutionStatusList ON
(
Payment.ExecutionStatus =
ExecutionStatusList.Value
)

LEFT OUTER JOIN TenderTypeList TenderTypeList ON
(
Payment.TenderType =
TenderTypeList.Value
)
)";

            string rowsWithCountSql = @"
RowsWithCount AS
(
SELECT
RowsData.*,
COUNT(1) OVER () AS TotalRecords
FROM RowsData RowsData
)";

            string sql = @"
WITH
" + periodRangeSql + @",
" + executionStatusListSql + @",
" + tenderTypeListSql + @",
" + paymentFilteredSql + @",
" + rowsDataSql + @",
" + rowsWithCountSql + @"

SELECT
RowsWithCount.PaymentID,
RowsWithCount.PaymentNo,
RowsWithCount.PaymentDate,
RowsWithCount.VendorName,
RowsWithCount.BankName,
RowsWithCount.AccountNo,
RowsWithCount.C_Currency_ID,
RowsWithCount.CurrencyISO,
RowsWithCount.CurrencySymbol,
RowsWithCount.StdPrecision,
RowsWithCount.Amount,
RowsWithCount.TenderType,
RowsWithCount.TenderTypeName,
RowsWithCount.VA009_PaymentMethod_ID,
RowsWithCount.PaymentMethodName,
RowsWithCount.ExecutionStatus,
RowsWithCount.StatusName,
RowsWithCount.DateFrom,
RowsWithCount.DateTo,
RowsWithCount.TotalRecords
FROM RowsWithCount RowsWithCount
ORDER BY
RowsWithCount.PaymentDate DESC,
RowsWithCount.PaymentNo DESC
OFFSET @RowsOffset ROWS
FETCH NEXT @RowsPageSize ROWS ONLY";

            SqlParameter[] parameters =
            {
                new SqlParameter(
                    "@RowsPeriodClientID",
                    ctx.GetAD_Client_ID()
                ),

                new SqlParameter(
                    "@ExecutionLanguage",
                    ctx.GetAD_Language()
                ),

                new SqlParameter(
                    "@TenderLanguage",
                    ctx.GetAD_Language()
                ),

                new SqlParameter(
                    "@RowsPaymentClientID",
                    ctx.GetAD_Client_ID()
                ),

                new SqlParameter(
                    "@RowsOffset",
                    offset
                ),

                new SqlParameter(
                    "@RowsPageSize",
                    pageSize
                )
            };

            return new SqlQueryData
            {
                Sql = sql,
                Parameters = parameters
            };
        }

        /// <summary>
        /// Returns current date SQL.
        /// </summary>
        private string GetCurrentDateSql()
        {
            if (DB.IsOracle())
            {
                return "CAST(CURRENT_DATE AS DATE)";
            }

            return "CURRENT_DATE";
        }

        /// <summary>
        /// Converts inclusive DateTo to exclusive DateTo.
        /// </summary>
        private string GetDateToExclusiveSql(
            string columnName
        )
        {
            return "CAST("
                + columnName
                + " AS DATE) + 1";
        }

        /// <summary>
        /// Checks whether VA009_ExecutionStatus exists.
        /// </summary>
        private bool HasPaymentExecutionStatusColumn()
        {
            string sql = @"
SELECT
COUNT(1)
FROM AD_Table TableData
INNER JOIN AD_Column ColumnData ON
(
TableData.AD_Table_ID =
ColumnData.AD_Table_ID
)
WHERE TableData.TableName =
'C_Payment'
AND ColumnData.ColumnName =
'VA009_ExecutionStatus'";

            return Util.GetValueOfInt(
                DB.ExecuteScalar(sql)
            ) > 0;
        }

        /// <summary>
        /// Returns translated application message.
        /// </summary>
        private string GetMsg(
            Ctx ctx,
            string key,
            string fallback
        )
        {
            string message =
                Msg.GetMsg(
                    ctx,
                    key
                );

            return !string.IsNullOrEmpty(
                message
            ) &&
            message != "[" + key + "]"
                ? message
                : fallback;
        }

        private int GetInt(
            IDataReader reader,
            string columnName,
            int fallback = 0
        )
        {
            object value =
                reader[columnName];

            return value == null ||
                value == DBNull.Value
                    ? fallback
                    : Util.GetValueOfInt(
                        value
                    );
        }

        private decimal GetDecimal(
            IDataReader reader,
            string columnName,
            decimal fallback
        )
        {
            object value =
                reader[columnName];

            return value == null ||
                value == DBNull.Value
                    ? fallback
                    : Util.GetValueOfDecimal(
                        value
                    );
        }

        private string GetString(
            IDataReader reader,
            string columnName,
            string fallback
        )
        {
            object value =
                reader[columnName];

            return value == null ||
                value == DBNull.Value
                    ? fallback
                    : Util.GetValueOfString(
                        value
                    );
        }

        private DateTime? GetNullableDate(
            IDataReader reader,
            string columnName
        )
        {
            object value =
                reader[columnName];

            if (
                value == null ||
                value == DBNull.Value
            )
            {
                return null;
            }

            return Util.GetValueOfDateTime(
                value
            );
        }

        private string FormatDate(
            DateTime date
        )
        {
            return date.ToString(
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture
            );
        }

        private string FormatNullableDate(
            DateTime? date
        )
        {
            return date.HasValue
                ? FormatDate(date.Value)
                : string.Empty;
        }

        /// <summary>
        /// Casts textual expressions consistently.
        /// </summary>
        private string CastText(
            string expression
        )
        {
            if (DB.IsOracle())
            {
                return "CAST("
                    + expression
                    + " AS VARCHAR2(4000))";
            }

            return "CAST("
                + expression
                + " AS VARCHAR(4000))";
        }

        private class SqlQueryData
        {
            public string Sql { get; set; }

            public SqlParameter[] Parameters
            {
                get;
                set;
            }
        }
    }
}