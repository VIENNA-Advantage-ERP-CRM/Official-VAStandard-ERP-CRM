using System;
/*
 * Bounced AP Payment Widget Controller
 *
 * Labels / Message Keys
 * #  | Current Text                         | Message Key
 * ---+--------------------------------------+--------------------------------
 * 1  | Bounced                              | VAS_030_MessageBounced
 * 2  | Need re-issue                        | VAS_030_MessageNeedReissue
 * 3  | Bounced AP payments                  | VAS_030_MessageBouncedAPPayments
 * 4  | Could not load data                  | VAS_ErrorLoading
 * 5  | Session Expired                      | SessionExpired
 * 6  | Not Specified                        | VAS_030_MessageNotSpecified
 */

using System.Collections.Generic;
using System.Data;
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
    ///
    /// Supported databases:
    /// - Oracle
    /// - PostgreSQL
    /// </summary>
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
            Ctx ctx = GetContext();

            if (ctx == null)
            {
                return GetSessionExpiredResult();
            }

            IDataReader dr = null;
            string sql = string.Empty;

            try
            {
                sql = BuildBouncedAPPaymentsSql(ctx);

                dr = DB.ExecuteReader(
                    sql,
                    null,
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

                return Json(
                    new
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

                        bouncedPaymentCount =
                            bouncedPaymentCount,

                        dateFrom =
                            FormatNullableDate(dateFrom),

                        dateTo =
                            FormatNullableDate(dateTo)
                    },
                    JsonRequestBehavior.AllowGet
                );
            }
            catch (Exception ex)
            {
                return Json(
                    new
                    {
                        error = true,
                        errorText = ex.Message,
                        sql = sql
                    },
                    JsonRequestBehavior.AllowGet
                );
            }
            finally
            {
                CloseReader(dr);
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
            Ctx ctx = GetContext();

            if (ctx == null)
            {
                return GetSessionExpiredResult();
            }

            if (pageNo <= 0)
            {
                pageNo = 1;
            }

            if (pageSize <= 0)
            {
                pageSize = 10;
            }

            if (pageSize > 100)
            {
                pageSize = 100;
            }

            IDataReader dr = null;
            string sql = string.Empty;

            try
            {
                sql = BuildBouncedAPPaymentRowsSql(
                    ctx,
                    pageNo,
                    pageSize
                );

                dr = DB.ExecuteReader(
                    sql,
                    null,
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

                    if (
                        !dateFrom.HasValue &&
                        dr["DateFrom"] != DBNull.Value
                    )
                    {
                        dateFrom = GetNullableDate(
                            dr,
                            "DateFrom"
                        );
                    }

                    if (
                        !dateTo.HasValue &&
                        dr["DateTo"] != DBNull.Value
                    )
                    {
                        dateTo = GetNullableDate(
                            dr,
                            "DateTo"
                        );
                    }

                    string vendorName = GetString(
                        dr,
                        "VendorName",
                        string.Empty
                    );

                    string bankName = GetString(
                        dr,
                        "BankName",
                        string.Empty
                    );

                    string bankAccountName = GetString(
                        dr,
                        "BankAccountName",
                        string.Empty
                    );

                    /*
                     * Perform the fallback in C# instead of SQL CASE.
                     * This avoids Oracle character-set compatibility errors.
                     */
                    if (string.IsNullOrWhiteSpace(bankName))
                    {
                        bankName = bankAccountName;
                    }

                    string currencyISO = GetString(
                        dr,
                        "CurrencyISO",
                        string.Empty
                    );

                    string currencySymbol = GetString(
                        dr,
                        "CurrencySymbol",
                        string.Empty
                    );

                    if (
                        string.IsNullOrWhiteSpace(
                            currencySymbol
                        )
                    )
                    {
                        currencySymbol = currencyISO;
                    }

                    int stdPrecision = NormalizePrecision(
                        GetInt(
                            dr,
                            "StdPrecision",
                            2
                        )
                    );

                    decimal amount = Math.Round(
                        GetDecimal(
                            dr,
                            "Amount",
                            0
                        ),
                        stdPrecision,
                        MidpointRounding.AwayFromZero
                    );

                    string tenderType = GetString(
                        dr,
                        "TenderType",
                        string.Empty
                    );

                    string translatedTenderTypeName =
                        GetString(
                            dr,
                            "TranslatedTenderTypeName",
                            string.Empty
                        );

                    string tenderTypeName = FirstNotEmpty(
                        translatedTenderTypeName,

                        GetString(
                            dr,
                            "TenderTypeName",
                            string.Empty
                        ),

                        tenderType
                    );

                    string paymentMethodName =
                        FirstNotEmpty(
                            GetString(
                                dr,
                                "PaymentMethodName",
                                string.Empty
                            ),

                            tenderTypeName
                        );

                    string executionStatus = GetString(
                        dr,
                        "ExecutionStatus",
                        string.Empty
                    );

                    string translatedStatusName =
                        GetString(
                            dr,
                            "TranslatedStatusName",
                            string.Empty
                        );

                    string statusName = FirstNotEmpty(
                        translatedStatusName,

                        GetString(
                            dr,
                            "StatusName",
                            string.Empty
                        ),

                        executionStatus,

                        GetMsg(
                            ctx,
                            "VAS_030_MessageBounced",
                            "Bounced"
                        )
                    );

                    string formattedDate =
                        paymentDate.HasValue
                            ? FormatDate(
                                paymentDate.Value
                            )
                            : string.Empty;

                    int paymentId = GetInt(
                        dr,
                        "PaymentID"
                    );

                    string paymentNo = GetString(
                        dr,
                        "PaymentNo",
                        string.Empty
                    );

                    rows.Add(
                        new
                        {
                            paymentId = paymentId,

                            paymentNo = paymentNo,
                            documentNo = paymentNo,

                            paymentDate = formattedDate,
                            date = formattedDate,

                            vendorName = vendorName,
                            supplier = vendorName,

                            bankName = bankName,

                            accountNo = GetString(
                                dr,
                                "AccountNo",
                                string.Empty
                            ),

                            amount = amount,

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

                            stdPrecision =
                                stdPrecision,

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

                            statusType = "bounced",

                            statusName =
                                statusName,

                            status =
                                statusName
                        }
                    );
                }

                int totalPages =
                    totalRecords > 0
                        ? Convert.ToInt32(
                            Math.Ceiling(
                                (decimal)totalRecords /
                                pageSize
                            )
                        )
                        : 0;

                return Json(
                    new
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
                    },
                    JsonRequestBehavior.AllowGet
                );
            }
            catch (Exception ex)
            {
                return Json(
                    new
                    {
                        error = true,
                        errorText = ex.Message,
                        sql = sql
                    },
                    JsonRequestBehavior.AllowGet
                );
            }
            finally
            {
                CloseReader(dr);
            }
        }

        /// <summary>
        /// Builds bounced AP payment count query.
        /// </summary>
        private string BuildBouncedAPPaymentsSql(
            Ctx ctx
        )
        {
            bool hasExecutionStatusColumn =
                HasPaymentExecutionStatusColumn();

            string clientIdSql = ctx
                .GetAD_Client_ID()
                .ToString(
                    CultureInfo.InvariantCulture
                );

            string bouncedStatusFilter =
                BuildBouncedStatusFilter(
                    hasExecutionStatusColumn
                );

            string periodRangeSql = @"
PeriodRange AS
(
    SELECT
        MIN(
            Period.StartDate
        ) AS DateFrom,

        MAX(
            Period.EndDate
        ) AS DateTo

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
        " + clientIdSql + @"

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
    " + clientIdSql + @"

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

            string sql = @"
WITH
" + periodRangeSql + @",

PaymentFiltered AS
(
" + paymentAccessSql + @"
)

SELECT
    COUNT
    (
        DISTINCT
        Payment.C_Payment_ID
    ) AS BouncedPaymentCount,

    MIN(
        PeriodRange.DateFrom
    ) AS DateFrom,

    MAX(
        PeriodRange.DateTo
    ) AS DateTo

FROM PeriodRange PeriodRange

LEFT OUTER JOIN PaymentFiltered Payment ON
(
    Payment.DateAcct >=
        PeriodRange.DateFrom

    AND Payment.DateAcct <
        " + GetDateToExclusiveSql(
            "PeriodRange.DateTo"
        ) + @"
)";

            return sql;
        }

        /// <summary>
        /// Builds bounced AP payment details query.
        /// </summary>
        private string BuildBouncedAPPaymentRowsSql(
            Ctx ctx,
            int pageNo,
            int pageSize
        )
        {
            bool hasExecutionStatusColumn =
                HasPaymentExecutionStatusColumn();

            bool hasPaymentMethodColumn =
                HasPaymentMethodColumn();

            string paymentMethodDisplayColumn =
                GetPaymentMethodDisplayColumn(
                    hasPaymentMethodColumn
                );

            bool hasPaymentMethodDisplay =
                !string.IsNullOrWhiteSpace(
                    paymentMethodDisplayColumn
                );

            string clientIdSql = ctx
                .GetAD_Client_ID()
                .ToString(
                    CultureInfo.InvariantCulture
                );

            string languageSql = ToSqlString(
                ctx.GetAD_Language()
            );

            int startRow =
                ((pageNo - 1) * pageSize) + 1;

            int endRow =
                pageNo * pageSize;

            string startRowSql =
                startRow.ToString(
                    CultureInfo.InvariantCulture
                );

            string endRowSql =
                endRow.ToString(
                    CultureInfo.InvariantCulture
                );

            string bouncedStatusFilter =
                BuildBouncedStatusFilter(
                    hasExecutionStatusColumn
                );

            string executionStatusColumn =
                hasExecutionStatusColumn
                    ? CastText(
                        "Payment.VA009_ExecutionStatus"
                    ) + " AS ExecutionStatus"
                    : CastText(
                        "NULL"
                    ) + " AS ExecutionStatus";

            string paymentMethodIdColumn =
                hasPaymentMethodColumn
                    ? "Payment.VA009_PaymentMethod_ID"
                    : "0 AS VA009_PaymentMethod_ID";

            string paymentMethodNameColumn =
                hasPaymentMethodDisplay
                    ? paymentMethodDisplayColumn +
                      " AS PaymentMethodName"
                    : "NULL AS PaymentMethodName";

            string paymentMethodJoin =
                hasPaymentMethodDisplay
                    ? @"
LEFT OUTER JOIN VA009_PaymentMethod PaymentMethod ON
(
    PaymentMethod.VA009_PaymentMethod_ID =
    Payment.VA009_PaymentMethod_ID
)"
                    : string.Empty;

            string periodRangeSql = @"
PeriodRange AS
(
    SELECT
        MIN(
            Period.StartDate
        ) AS DateFrom,

        MAX(
            Period.EndDate
        ) AS DateTo

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
        " + clientIdSql + @"

    AND " + GetCurrentDateSql() + @" >=
        Period.StartDate

    AND " + GetCurrentDateSql() + @" <
        " + GetDateToExclusiveSql(
            "Period.EndDate"
        ) + @"
)";

            /*
             * Translated and base names are returned separately.
             * The fallback is handled in C# to prevent ORA-12704.
             */
            string executionStatusListSql = @"
ExecutionStatusList AS
(
    SELECT
        " + CastText(
            "RefList.Value"
        ) + @" AS ReferenceValue,

        RefList.Name AS StatusName,

        RefListTrl.Name AS TranslatedStatusName

    FROM AD_Reference ReferenceInfo

    INNER JOIN AD_Ref_List RefList ON
    (
        RefList.AD_Reference_ID =
        ReferenceInfo.AD_Reference_ID
    )

    LEFT OUTER JOIN AD_Ref_List_Trl RefListTrl ON
    (
        RefListTrl.AD_Ref_List_ID =
        RefList.AD_Ref_List_ID

        AND RefListTrl.AD_Language =
        " + languageSql + @"
    )

    WHERE ReferenceInfo.Name =
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
        ) + @" AS ReferenceValue,

        RefList.Name AS TenderTypeName,

        RefListTrl.Name AS TranslatedTenderTypeName

    FROM AD_Reference ReferenceInfo

    INNER JOIN AD_Ref_List RefList ON
    (
        RefList.AD_Reference_ID =
        ReferenceInfo.AD_Reference_ID
    )

    LEFT OUTER JOIN AD_Ref_List_Trl RefListTrl ON
    (
        RefListTrl.AD_Ref_List_ID =
        RefList.AD_Ref_List_ID

        AND RefListTrl.AD_Language =
        " + languageSql + @"
    )

    WHERE ReferenceInfo.Name =
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

    " + paymentMethodIdColumn + @",

    Payment.PayAmt AS Amount,

    " + CastText(
        "Payment.TenderType"
    ) + @" AS TenderType,

    " + executionStatusColumn + @"

FROM C_Payment Payment

WHERE Payment.IsActive = 'Y'

AND Payment.AD_Client_ID =
    " + clientIdSql + @"

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

            string rowsDataSql = @"
RowsData AS
(
    SELECT
        Payment.PaymentID,

        Payment.PaymentNo,

        Payment.PaymentDate,

        BPartner.Name AS VendorName,

        Bank.Name AS BankName,

        BankAccount.Name AS BankAccountName,

        BankAccount.AccountNo,

        Payment.C_Currency_ID,

        Currency.ISO_Code AS CurrencyISO,

        Currency.CurSymbol AS CurrencySymbol,

        COALESCE(
            Currency.StdPrecision,
            2
        ) AS StdPrecision,

        Payment.Amount,

        Payment.TenderType,

        TenderTypeList.TenderTypeName,

        TenderTypeList.TranslatedTenderTypeName,

        Payment.VA009_PaymentMethod_ID,

        " + paymentMethodNameColumn + @",

        Payment.ExecutionStatus,

        ExecutionStatusList.StatusName,

        ExecutionStatusList.TranslatedStatusName,

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
        BPartner.C_BPartner_ID =
        Payment.C_BPartner_ID
    )

    LEFT OUTER JOIN C_BankAccount BankAccount ON
    (
        BankAccount.C_BankAccount_ID =
        Payment.C_BankAccount_ID
    )

    LEFT OUTER JOIN C_Bank Bank ON
    (
        Bank.C_Bank_ID =
        BankAccount.C_Bank_ID
    )

    LEFT OUTER JOIN C_Currency Currency ON
    (
        Currency.C_Currency_ID =
        Payment.C_Currency_ID
    )

    " + paymentMethodJoin + @"

    LEFT OUTER JOIN ExecutionStatusList ExecutionStatusList ON
    (
        ExecutionStatusList.ReferenceValue =
        Payment.ExecutionStatus
    )

    LEFT OUTER JOIN TenderTypeList TenderTypeList ON
    (
        TenderTypeList.ReferenceValue =
        Payment.TenderType
    )
)";

            string sql = @"
WITH
" + periodRangeSql + @",

" + executionStatusListSql + @",

" + tenderTypeListSql + @",

PaymentFiltered AS
(
" + paymentAccessSql + @"
),

" + rowsDataSql + @",

NumberedRows AS
(
    SELECT
        RowsData.*,

        COUNT(1) OVER () AS TotalRecords,

        ROW_NUMBER() OVER
        (
            ORDER BY
                RowsData.PaymentDate DESC,
                RowsData.PaymentNo DESC,
                RowsData.PaymentID DESC
        ) AS RowNumber

    FROM RowsData RowsData
)

SELECT
    NumberedRows.PaymentID,

    NumberedRows.PaymentNo,

    NumberedRows.PaymentDate,

    NumberedRows.VendorName,

    NumberedRows.BankName,

    NumberedRows.BankAccountName,

    NumberedRows.AccountNo,

    NumberedRows.C_Currency_ID,

    NumberedRows.CurrencyISO,

    NumberedRows.CurrencySymbol,

    NumberedRows.StdPrecision,

    NumberedRows.Amount,

    NumberedRows.TenderType,

    NumberedRows.TenderTypeName,

    NumberedRows.TranslatedTenderTypeName,

    NumberedRows.VA009_PaymentMethod_ID,

    NumberedRows.PaymentMethodName,

    NumberedRows.ExecutionStatus,

    NumberedRows.StatusName,

    NumberedRows.TranslatedStatusName,

    NumberedRows.DateFrom,

    NumberedRows.DateTo,

    NumberedRows.TotalRecords

FROM NumberedRows NumberedRows

WHERE NumberedRows.RowNumber >=
    " + startRowSql + @"

AND NumberedRows.RowNumber <=
    " + endRowSql + @"

ORDER BY
    NumberedRows.RowNumber";

            return sql;
        }

        /// <summary>
        /// Creates the bounced/rejected execution-status condition.
        /// </summary>
        private string BuildBouncedStatusFilter(
            bool hasExecutionStatusColumn
        )
        {
            if (!hasExecutionStatusColumn)
            {
                return @"
AND 1 = 2";
            }

            string bouncedStatus = ToSqlString(
                X_C_Payment
                    .VA009_EXECUTIONSTATUS_Bounced
            );

            string rejectedStatus = ToSqlString(
                X_C_Payment
                    .VA009_EXECUTIONSTATUS_Rejected
            );

            return @"
AND Payment.VA009_ExecutionStatus IN
(
    " + bouncedStatus + @",
    " + rejectedStatus + @"
)";
        }

        /// <summary>
        /// Returns the current database date without the time component.
        /// </summary>
        private string GetCurrentDateSql()
        {
            if (DB.IsOracle())
            {
                return "TRUNC(CURRENT_DATE)";
            }

            return "CURRENT_DATE";
        }

        /// <summary>
        /// Converts an inclusive end date to an exclusive end date.
        ///
        /// Oracle:
        /// DATE + INTEGER
        ///
        /// PostgreSQL:
        /// DATE + INTEGER
        /// </summary>
        private string GetDateToExclusiveSql(
            string columnName
        )
        {
            return "CAST("
                + columnName
                + " AS DATE) + 1";
        }

        private bool HasPaymentExecutionStatusColumn()
        {
            return HasColumn(
                "C_Payment",
                "VA009_ExecutionStatus"
            );
        }

        private bool HasPaymentMethodColumn()
        {
            return HasColumn(
                "C_Payment",
                "VA009_PaymentMethod_ID"
            );
        }

        /// <summary>
        /// Returns the first available payment-method display column.
        /// </summary>
        private string GetPaymentMethodDisplayColumn(
            bool hasPaymentMethodColumn
        )
        {
            if (!hasPaymentMethodColumn)
            {
                return string.Empty;
            }

            if (
                HasColumn(
                    "VA009_PaymentMethod",
                    "VA009_Name"
                )
            )
            {
                return "PaymentMethod.VA009_Name";
            }

            if (
                HasColumn(
                    "VA009_PaymentMethod",
                    "Name"
                )
            )
            {
                return "PaymentMethod.Name";
            }

            if (
                HasColumn(
                    "VA009_PaymentMethod",
                    "Value"
                )
            )
            {
                return "PaymentMethod.Value";
            }

            return string.Empty;
        }

        private bool HasColumn(
            string tableName,
            string columnName
        )
        {
            string sql = @"
SELECT
    COUNT(1)

FROM AD_Table TableData

INNER JOIN AD_Column ColumnData ON
(
    ColumnData.AD_Table_ID =
    TableData.AD_Table_ID
)

WHERE TableData.TableName =
    " + ToSqlString(tableName) + @"

AND ColumnData.ColumnName =
    " + ToSqlString(columnName);

            return Util.GetValueOfInt(
                DB.ExecuteScalar(sql)
            ) > 0;
        }

        /// <summary>
        /// Casts text to a database-compatible text type.
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

        private string ToSqlString(
            string value
        )
        {
            return "'"
                + (value ?? string.Empty)
                    .Replace("'", "''")
                + "'";
        }

        private Ctx GetContext()
        {
            if (Session["ctx"] == null)
            {
                return null;
            }

            return Session["ctx"] as Ctx;
        }

        private JsonResult GetSessionExpiredResult()
        {
            Ctx ctx = Env.GetCtx();
            string sessionExpired =
                GetMsg(
                    ctx,
                    "SessionExpired",
                    "Session Expired"
                );

            return Json(
                new
                {
                    error = true,
                    errorText = sessionExpired
                },
                JsonRequestBehavior.AllowGet
            );
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

            return
                !string.IsNullOrWhiteSpace(message) &&
                message != "[" + key + "]"
                    ? message
                    : fallback;
        }

        private string FirstNotEmpty(
            params string[] values
        )
        {
            if (values == null)
            {
                return string.Empty;
            }

            for (int i = 0; i < values.Length; i++)
            {
                if (
                    !string.IsNullOrWhiteSpace(
                        values[i]
                    )
                )
                {
                    return values[i];
                }
            }

            return string.Empty;
        }

        private int NormalizePrecision(
            int precision
        )
        {
            if (
                precision < 0 ||
                precision > 28
            )
            {
                return 2;
            }

            return precision;
        }

        private int GetInt(
            IDataReader reader,
            string columnName,
            int fallback = 0
        )
        {
            object value =
                reader[columnName];

            return
                value == null ||
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

            return
                value == null ||
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

            return
                value == null ||
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

        private void CloseReader(
            IDataReader reader
        )
        {
            if (reader == null)
            {
                return;
            }

            reader.Close();
            reader.Dispose();
        }
    }
}
