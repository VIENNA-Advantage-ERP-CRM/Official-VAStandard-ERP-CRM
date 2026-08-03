
using System;
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
        /*
         * Keep Reference names in one place.
         * If the database uses a different reference name,
         * change only these constants.
         */
        private const string TenderTypeReferenceName =
            "C_Payment Tender Type";

        private const string ExecutionStatusReferenceName =
            "VA009_ExecutionStatus";

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

            IDataReader reader = null;
            string sql = string.Empty;

            try
            {
                sql = BuildBouncedAPPaymentsSql(
                    ctx
                );

                reader = DB.ExecuteReader(
                    sql,
                    null,
                    null
                );

                int bouncedPaymentCount = 0;
                DateTime? dateFrom = null;
                DateTime? dateTo = null;

                if (
                    reader != null &&
                    reader.Read()
                )
                {
                    bouncedPaymentCount = GetInt(
                        reader,
                        "BouncedPaymentCount"
                    );

                    dateFrom = GetNullableDate(
                        reader,
                        "DateFrom"
                    );

                    dateTo = GetNullableDate(
                        reader,
                        "DateTo"
                    );
                }

                return Json(
                    new
                    {
                        success = true,
                        error = string.Empty,

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
                            FormatNullableDate(
                                dateFrom
                            ),

                        dateTo =
                            FormatNullableDate(
                                dateTo
                            ),

                        hasData =
                            bouncedPaymentCount > 0
                    },
                    JsonRequestBehavior.AllowGet
                );
            }
            catch (Exception)
            {
                string errorMessage = GetMsg(
                    ctx,
                    "VAS_ErrorLoading",
                    "Could not load data"
                );

                return Json(
                    new
                    {
                        success = false,
                        error = errorMessage,
                        errorText = errorMessage,
                        hasData = false
                    },
                    JsonRequestBehavior.AllowGet
                );
            }
            finally
            {
                CloseReader(
                    reader
                );
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

            IDataReader reader = null;
            string sql = string.Empty;

            try
            {
                sql = BuildBouncedAPPaymentRowsSql(
                    ctx,
                    pageNo,
                    pageSize
                );

                reader = DB.ExecuteReader(
                    sql,
                    null,
                    null
                );

                List<object> rows =
                    new List<object>();

                int totalRecords = 0;

                while (
                    reader != null &&
                    reader.Read()
                )
                {
                    totalRecords = GetInt(
                        reader,
                        "TotalRecords"
                    );

                    DateTime? paymentDate =
                        GetNullableDate(
                            reader,
                            "PaymentDate"
                        );

                    string vendorName = GetString(
                        reader,
                        "VendorName",
                        string.Empty
                    );

                    string bankName = GetString(
                        reader,
                        "BankName",
                        string.Empty
                    );

                    string bankAccountName = GetString(
                        reader,
                        "BankAccountName",
                        string.Empty
                    );

                    if (string.IsNullOrWhiteSpace(
                        bankName
                    ))
                    {
                        bankName =
                            bankAccountName;
                    }

                    string currencyISO = GetString(
                        reader,
                        "CurrencyISO",
                        string.Empty
                    );

                    string currencySymbol = GetString(
                        reader,
                        "CurrencySymbol",
                        string.Empty
                    );

                    if (string.IsNullOrWhiteSpace(
                        currencySymbol
                    ))
                    {
                        currencySymbol =
                            currencyISO;
                    }

                    int stdPrecision =
                        NormalizePrecision(
                            GetInt(
                                reader,
                                "StdPrecision",
                                2
                            )
                        );

                    decimal amount = Math.Round(
                        GetDecimal(
                            reader,
                            "Amount",
                            0
                        ),
                        stdPrecision,
                        MidpointRounding.AwayFromZero
                    );

                    string tenderType = GetString(
                        reader,
                        "TenderType",
                        string.Empty
                    );

                    string translatedTenderTypeName =
                        GetString(
                            reader,
                            "TranslatedTenderTypeName",
                            string.Empty
                        );

                    string baseTenderTypeName =
                        GetString(
                            reader,
                            "TenderTypeName",
                            string.Empty
                        );

                    /*
                     * Method display order:
                     *
                     * 1. Current-language translated TenderType name.
                     * 2. Base TenderType reference name.
                     * 3. VA009 Payment Method name.
                     * 4. Raw stored code only as the final fallback.
                     */
                    string tenderTypeName =
                        FirstNotEmpty(
                            translatedTenderTypeName,
                            baseTenderTypeName,
                            tenderType
                        );

                    string paymentMethodName =
                        FirstNotEmpty(
                            translatedTenderTypeName,
                            baseTenderTypeName,

                            GetString(
                                reader,
                                "PaymentMethodName",
                                string.Empty
                            ),

                            tenderType,

                            GetMsg(
                                ctx,
                                "VAS_030_MessageNotSpecified",
                                "Not Specified"
                            )
                        );

                    string executionStatus =
                        GetString(
                            reader,
                            "ExecutionStatus",
                            string.Empty
                        );

                    string translatedStatusName =
                        GetString(
                            reader,
                            "TranslatedStatusName",
                            string.Empty
                        );

                    string statusName =
                        FirstNotEmpty(
                            translatedStatusName,

                            GetString(
                                reader,
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
                        reader,
                        "PaymentID"
                    );

                    string paymentNo = GetString(
                        reader,
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
                                reader,
                                "AccountNo",
                                string.Empty
                            ),

                            amount = amount,

                            cCurrencyId = GetInt(
                                reader,
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

                            /*
                             * Return both stored value and display name.
                             */
                            tenderType = tenderType,

                            tenderTypeName =
                                tenderTypeName,

                            paymentMethodId = GetInt(
                                reader,
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
                        success = true,
                        error = string.Empty,

                        title = GetMsg(
                            ctx,
                            "VAS_030_MessageBounced",
                            "Bounced"
                        ),

                        rows = rows,

                        pageNo = pageNo,
                        pageSize = pageSize,

                        totalRecords =
                            totalRecords,

                        totalPages =
                            totalPages,

                        hasData =
                            rows.Count > 0
                    },
                    JsonRequestBehavior.AllowGet
                );
            }
            catch (Exception)
            {
                string errorMessage = GetMsg(
                    ctx,
                    "VAS_ErrorLoading",
                    "Could not load data"
                );

                return Json(
                    new
                    {
                        success = false,
                        error = errorMessage,
                        errorText = errorMessage,
                        hasData = false,
                        rows = new List<object>()
                    },
                    JsonRequestBehavior.AllowGet
                );
            }
            finally
            {
                CloseReader(
                    reader
                );
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

            string clientIdSql =
                ctx.GetAD_Client_ID()
                    .ToString(
                        CultureInfo.InvariantCulture
                    );

            string bouncedStatusFilter =
                BuildBouncedStatusFilter(
                    hasExecutionStatusColumn
                );

            string paymentAccessSql = @"
SELECT
    Payment.C_Payment_ID,
    Payment.DateAcct

FROM C_Payment Payment

WHERE Payment.IsActive = 'Y'

AND Payment.AD_Client_ID =
    " + clientIdSql + @"

AND Payment.IsReceipt = 'N'

AND Payment.DocStatus IN
(
    'CO',
    'CL'
)

AND Payment.TenderType = 'K'

" + bouncedStatusFilter;

            paymentAccessSql =
                MRole.GetDefault(ctx)
                    .AddAccessSQL(
                        paymentAccessSql,
                        "Payment",
                        MRole.SQL_FULLYQUALIFIED,
                        MRole.SQL_RO
                    );

            /*
             * Bounced payments are counted across the whole financial year
             * that today falls in, not just the current period. The range
             * is anchored on FinancialYearRange with a LEFT JOIN, so
             * DateFrom/DateTo still come back as the year's edges even when
             * no payment bounced in it.
             */
            return @"
WITH
" + BuildFinancialYearRangeSql(
    clientIdSql
) + @",

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

    MIN
    (
        FinancialYearRange.DateFrom
    ) AS DateFrom,

    MAX
    (
        FinancialYearRange.DateTo
    ) AS DateTo

FROM FinancialYearRange FinancialYearRange

LEFT OUTER JOIN PaymentFiltered Payment ON
(
    Payment.DateAcct >=
        FinancialYearRange.DateFrom

    AND Payment.DateAcct <
        " + GetDateToExclusiveSql(
            "FinancialYearRange.DateTo"
        ) + @"
)";
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

            string clientIdSql =
                ctx.GetAD_Client_ID()
                    .ToString(
                        CultureInfo.InvariantCulture
                    );

            string languageSql =
                ToSqlString(
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
                    ? CastText(
                        paymentMethodDisplayColumn
                    ) + " AS PaymentMethodName"
                    : CastText(
                        "NULL"
                    ) + " AS PaymentMethodName";

            string paymentMethodJoin =
                hasPaymentMethodDisplay
                    ? @"
LEFT OUTER JOIN VA009_PaymentMethod PaymentMethod ON
(
    PaymentMethod.VA009_PaymentMethod_ID =
        Payment.VA009_PaymentMethod_ID
)"
                    : string.Empty;

            /*
             * Execution Status List Reference.
             */
            string executionStatusListSql = @"
ExecutionStatusList AS
(
    SELECT DISTINCT
        " + CastText(
            "RefList.Value"
        ) + @" AS ReferenceValue,

        " + CastText(
            "RefList.Name"
        ) + @" AS StatusName,

        " + CastText(
            "RefListTrl.Name"
        ) + @" AS TranslatedStatusName

    FROM AD_Reference ReferenceInfo

    INNER JOIN AD_Ref_List RefList ON
    (
        ReferenceInfo.AD_Reference_ID =
        RefList.AD_Reference_ID
    )

    LEFT OUTER JOIN AD_Ref_List_Trl RefListTrl ON
    (
        RefList.AD_Ref_List_ID =
        RefListTrl.AD_Ref_List_ID

        AND RefListTrl.AD_Language =
        " + languageSql + @"
    )

    WHERE ReferenceInfo.Name =
        " + ToSqlString(
            ExecutionStatusReferenceName
        ) + @"

    AND ReferenceInfo.IsActive = 'Y'

    AND RefList.IsActive = 'Y'
)";

            /*
             * Tender Type List Reference.
             *
             * Stored value:
             * K
             *
             * Display value:
             * TranslatedTenderTypeName or TenderTypeName.
             */
            string tenderTypeListSql = @"
TenderTypeList AS
(
    SELECT DISTINCT
        " + CastText(
            "RefList.Value"
        ) + @" AS ReferenceValue,

        " + CastText(
            "RefList.Name"
        ) + @" AS TenderTypeName,

        " + CastText(
            "RefListTrl.Name"
        ) + @" AS TranslatedTenderTypeName

    FROM AD_Reference ReferenceInfo

    INNER JOIN AD_Ref_List RefList ON
    (
        ReferenceInfo.AD_Reference_ID =
        RefList.AD_Reference_ID
    )

    LEFT OUTER JOIN AD_Ref_List_Trl RefListTrl ON
    (
        RefList.AD_Ref_List_ID =
        RefListTrl.AD_Ref_List_ID

        AND RefListTrl.AD_Language =
        " + languageSql + @"
    )

    WHERE ReferenceInfo.Name =
        " + ToSqlString(
            TenderTypeReferenceName
        ) + @"

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

AND Payment.DocStatus IN
(
    'CO',
    'CL'
)

AND Payment.TenderType = 'K'

" + bouncedStatusFilter;

            paymentAccessSql =
                MRole.GetDefault(ctx)
                    .AddAccessSQL(
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

        COALESCE
        (
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

        ExecutionStatusList.TranslatedStatusName

    FROM PaymentFiltered Payment

    /* Same financial-year window as the count on the card, so the list
       and the figure it drills into always agree. */
    INNER JOIN FinancialYearRange FinancialYearRange ON
    (
        Payment.PaymentDate >=
            FinancialYearRange.DateFrom

        AND Payment.PaymentDate <
            " + GetDateToExclusiveSql(
                "FinancialYearRange.DateTo"
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
        " + CastText(
            "Payment.ExecutionStatus"
        ) + @"
    )

    LEFT OUTER JOIN TenderTypeList TenderTypeList ON
    (
        TenderTypeList.ReferenceValue =
        " + CastText(
            "Payment.TenderType"
        ) + @"
    )
)";

            return @"
WITH
" + BuildFinancialYearRangeSql(
    clientIdSql
) + @",

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

    NumberedRows.TotalRecords

FROM NumberedRows NumberedRows

WHERE NumberedRows.RowNumber >=
    " + startRowSql + @"

AND NumberedRows.RowNumber <=
    " + endRowSql + @"

ORDER BY
    NumberedRows.RowNumber";
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

            string bouncedStatus =
                ToSqlString(
                    X_C_Payment
                        .VA009_EXECUTIONSTATUS_Bounced
                );

            string rejectedStatus =
                ToSqlString(
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
        /// Builds the FinancialYearRange CTE: the first and last dates of
        /// the financial year that today falls in.
        /// </summary>
        /// <remarks>
        /// The year is resolved through period control rather than from the
        /// calendar year of the current date - the inner subquery finds the
        /// C_Period holding today and takes its C_Year_ID, then the outer
        /// query spans every period of that year. A financial year that
        /// straddles the calendar boundary is therefore handled correctly.
        ///
        /// When today falls outside every defined period the subquery yields
        /// NULL, the CTE returns a single NULL range, and the queries using
        /// it match no payments - an unconfigured calendar reads as "nothing
        /// in range" rather than as "everything".
        /// </remarks>
        private string BuildFinancialYearRangeSql(
            string clientIdSql
        )
        {
            return @"
FinancialYearRange AS
(
    SELECT
        MIN
        (
            Period.StartDate
        ) AS DateFrom,

        MAX
        (
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

    AND YearData.IsActive = 'Y'

    AND Period.IsActive = 'Y'

    AND ClientInfo.AD_Client_ID =
        " + clientIdSql + @"

    AND YearData.C_Year_ID =
    (
        SELECT
            MIN
            (
                CurrentYear.C_Year_ID
            )

        FROM AD_ClientInfo CurrentClientInfo

        INNER JOIN C_Year CurrentYear ON
        (
            CurrentYear.C_Calendar_ID =
            CurrentClientInfo.C_Calendar_ID
        )

        INNER JOIN C_Period CurrentPeriod ON
        (
            CurrentPeriod.C_Year_ID =
            CurrentYear.C_Year_ID
        )

        WHERE CurrentClientInfo.IsActive = 'Y'

        AND CurrentYear.IsActive = 'Y'

        AND CurrentPeriod.IsActive = 'Y'

        AND CurrentClientInfo.AD_Client_ID =
            " + clientIdSql + @"

        AND " + GetCurrentDateSql() + @" >=
            CurrentPeriod.StartDate

        AND " + GetCurrentDateSql() + @" <
            " + GetDateToExclusiveSql(
                "CurrentPeriod.EndDate"
            ) + @"
    )
)";
        }

        /// <summary>
        /// Returns the current database date without time.
        /// </summary>
        private string GetCurrentDateSql()
        {
            return DB.IsOracle()
                ? "TRUNC(CURRENT_DATE)"
                : "CURRENT_DATE";
        }

        /// <summary>
        /// Converts an inclusive end date into an exclusive end date.
        /// </summary>
        private string GetDateToExclusiveSql(
            string columnName
        )
        {
            if (DB.IsOracle())
            {
                return "CAST("
                    + columnName
                    + " AS DATE) + 1";
            }

            return "CAST("
                + columnName
                + " AS DATE) + INTERVAL '1 DAY'";
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
    " + ToSqlString(
                tableName
            ) + @"

AND ColumnData.ColumnName =
    " + ToSqlString(
                columnName
            );

            return Util.GetValueOfInt(
                DB.ExecuteScalar(
                    sql
                )
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
                + (
                    value ??
                    string.Empty
                ).Replace(
                    "'",
                    "''"
                )
                + "'";
        }

        private Ctx GetContext()
        {
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
                    success = false,
                    error = sessionExpired,
                    errorText = sessionExpired,
                    hasData = false
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
            if (ctx == null)
            {
                return fallback;
            }

            string message = Msg.GetMsg(
                ctx,
                key
            );

            if (
                string.IsNullOrEmpty(
                    message
                ) ||
                message == key ||
                message == "[" + key + "]"
            )
            {
                return fallback;
            }

            return message;
        }

        private string FirstNotEmpty(
            params string[] values
        )
        {
            if (values == null)
            {
                return string.Empty;
            }

            for (
                int index = 0;
                index < values.Length;
                index++
            )
            {
                if (!string.IsNullOrWhiteSpace(
                    values[index]
                ))
                {
                    return values[index];
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

        private object GetReaderValue(
            IDataRecord record,
            string columnName
        )
        {
            if (record == null)
            {
                return DBNull.Value;
            }

            for (
                int index = 0;
                index < record.FieldCount;
                index++
            )
            {
                if (string.Equals(
                    record.GetName(index),
                    columnName,
                    StringComparison.OrdinalIgnoreCase
                ))
                {
                    return record.GetValue(
                        index
                    );
                }
            }

            return DBNull.Value;
        }

        private int GetInt(
            IDataRecord record,
            string columnName,
            int fallback = 0
        )
        {
            object value = GetReaderValue(
                record,
                columnName
            );

            if (
                value == null ||
                value == DBNull.Value
            )
            {
                return fallback;
            }

            return Util.GetValueOfInt(
                value
            );
        }

        private decimal GetDecimal(
            IDataRecord record,
            string columnName,
            decimal fallback
        )
        {
            object value = GetReaderValue(
                record,
                columnName
            );

            if (
                value == null ||
                value == DBNull.Value
            )
            {
                return fallback;
            }

            return Util.GetValueOfDecimal(
                value
            );
        }

        private string GetString(
            IDataRecord record,
            string columnName,
            string fallback
        )
        {
            object value = GetReaderValue(
                record,
                columnName
            );

            if (
                value == null ||
                value == DBNull.Value
            )
            {
                return fallback;
            }

            return Util.GetValueOfString(
                value
            );
        }

        private DateTime? GetNullableDate(
            IDataRecord record,
            string columnName
        )
        {
            object value = GetReaderValue(
                record,
                columnName
            );

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
                ? FormatDate(
                    date.Value
                )
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
