
using System;

/*
 * Scheduled AP Payment Widget Controller
 *
 * Labels / Message Keys
 * #  | Current Text                         | Message Key
 * ---+--------------------------------------+-----------------------------------------------
 * 1  | Due This Week                        | VAS_029_MessageScheduled
 * 2  | Queued for {0} run this week         | VAS_029_MessageQueuedForPaymentMethodRunThisWeek
 * 3  | Scheduled for payment this week      | VAS_029_ScheduledForPaymentThisWeek
 * 4  | No Data                              | VAS_ErrorLoading
 * 5  | Session Expired                      | SessionExpired
 * 6  | Not Specified                        | VAS_029_MessageNotSpecified
 * 7  | invoices                             | VAS_029_MessageInvoices
 * 8  | Payments due this week               | VAS_029_MessagePaymentsDueThisWeek
 * 9  | Total                                | VAS_029_MessageTotal
 */

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
    /// Purpose     : Provides scheduled AP payment KPI widget data
    ///               returned as one total amount.
    ///
    /// Compatible with:
    /// - Oracle
    /// - PostgreSQL
    /// </summary>
    public class VAS_029_ScheduledAPPaymentWidgetController : Controller
    {
        /// <summary>
        /// Returns AP invoice schedule amounts due during
        /// the next seven days, returned as one total amount.
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetScheduledAPPaymentThisWeek()
        {
            Ctx ctx = GetContext();

            if (ctx == null)
            {
                return GetSessionExpiredResult();
            }

            IDataReader dr = null;

            try
            {
                SqlQueryData queryData =
                    BuildScheduledAPPaymentThisWeekSql(
                        ctx
                    );

                dr = DB.ExecuteReader(
                    queryData.Sql,
                    queryData.Parameters,
                    null
                );

                decimal scheduledAmountThisWeek = 0;
                int cCurrencyId = 0;
                int precision = 2;

                string currencyISO = string.Empty;
                string currencySymbol = string.Empty;
                string dateFrom = string.Empty;
                string dateTo = string.Empty;

                if (
                    dr != null &&
                    dr.Read()
                )
                {
                    precision =
                        NormalizePrecision(
                            GetInt(
                                dr,
                                "StdPrecision",
                                2
                            )
                        );

                    scheduledAmountThisWeek =
                        Math.Round(
                            GetDecimal(
                                dr,
                                "ScheduledAmount",
                                0
                            ),
                            precision,
                            MidpointRounding.AwayFromZero
                        );

                    cCurrencyId =
                        GetInt(
                            dr,
                            "C_Currency_ID"
                        );

                    currencyISO =
                        GetString(
                            dr,
                            "CurrencyISO",
                            string.Empty
                        );

                    currencySymbol =
                        GetString(
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
                        currencySymbol =
                            currencyISO;
                    }

                    if (
                        dr["DateFrom"] !=
                        DBNull.Value
                    )
                    {
                        dateFrom =
                            FormatDate(
                                Util.GetValueOfDateTime(
                                    dr["DateFrom"]
                                )
                            );
                    }

                    if (
                        dr["DateTo"] !=
                        DBNull.Value
                    )
                    {
                        dateTo =
                            FormatDate(
                                Util.GetValueOfDateTime(
                                    dr["DateTo"]
                                )
                            );
                    }
                }

                return Json(
                    new
                    {
                        title = GetMsg(
                            ctx,
                            "VAS_029_MessageScheduled",
                            "Scheduled"
                        ),

                        description = GetMsg(
                            ctx,
                            "VAS_029_ScheduledForPaymentThisWeek",
                            "Scheduled for payment this week"
                        ),

                        value =
                            scheduledAmountThisWeek,

                        scheduledAmountThisWeek =
                            scheduledAmountThisWeek,

                        cCurrencyId =
                            cCurrencyId,

                        currencyISO =
                            currencyISO,

                        currencySymbol =
                            currencySymbol,

                        symbol =
                            currencySymbol,

                        precision =
                            precision,

                        dateFrom =
                            dateFrom,

                        dateTo =
                            dateTo
                    },
                    JsonRequestBehavior.AllowGet
                );
            }
            catch (Exception)
            {
                return Json(
                    new
                    {
                        error = true,
                        errorText = GetMsg(
                            ctx,
                            "VAS_ErrorLoading",
                            "No Data"
                        )
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
        /// Returns paginated AP invoice schedule rows.
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetScheduledAPPaymentRows(
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

            try
            {
                SqlQueryData queryData =
                    BuildScheduledAPPaymentRowsSql(
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
                int vendorCount = 0;
                int paymentMethodCount = 0;

                string dateFrom = string.Empty;
                string dateTo = string.Empty;

                while (
                    dr != null &&
                    dr.Read()
                )
                {
                    totalRecords =
                        GetInt(
                            dr,
                            "TotalRecords"
                        );

                    vendorCount =
                        GetInt(
                            dr,
                            "VendorCount"
                        );

                    paymentMethodCount =
                        GetInt(
                            dr,
                            "PaymentMethodCount"
                        );

                    if (
                        string.IsNullOrWhiteSpace(
                            dateFrom
                        ) &&
                        dr["DateFrom"] !=
                        DBNull.Value
                    )
                    {
                        dateFrom =
                            FormatDate(
                                Util.GetValueOfDateTime(
                                    dr["DateFrom"]
                                )
                            );
                    }

                    if (
                        string.IsNullOrWhiteSpace(
                            dateTo
                        ) &&
                        dr["DateTo"] !=
                        DBNull.Value
                    )
                    {
                        dateTo =
                            FormatDate(
                                Util.GetValueOfDateTime(
                                    dr["DateTo"]
                                )
                            );
                    }

                    DateTime? invoiceDate =
                        null;

                    DateTime? dueDate =
                        null;

                    if (
                        dr["InvoiceDate"] !=
                        DBNull.Value
                    )
                    {
                        invoiceDate =
                            Util.GetValueOfDateTime(
                                dr["InvoiceDate"]
                            );
                    }

                    if (
                        dr["DueDate"] !=
                        DBNull.Value
                    )
                    {
                        dueDate =
                            Util.GetValueOfDateTime(
                                dr["DueDate"]
                            );
                    }

                    string paymentMethodDisplay =
                        GetString(
                            dr,
                            "PaymentMethodDisplay",
                            string.Empty
                        );

                    string paymentRule =
                        GetString(
                            dr,
                            "PaymentRule",
                            string.Empty
                        );

                    string paymentMethodName =
                        FirstNotEmpty(
                            paymentMethodDisplay,
                            paymentRule,
                            GetMsg(
                                ctx,
                                "VAS_029_MessageNotSpecified",
                                "Not Specified"
                            )
                        );

                    string invoiceCurrency =
                        GetString(
                            dr,
                            "InvoiceCurrency",
                            string.Empty
                        );

                    string invoiceCurrencySymbol =
                        GetString(
                            dr,
                            "InvoiceCurrencySymbol",
                            string.Empty
                        );

                    if (
                        string.IsNullOrWhiteSpace(
                            invoiceCurrencySymbol
                        )
                    )
                    {
                        invoiceCurrencySymbol =
                            invoiceCurrency;
                    }

                    int rowPrecision =
                        NormalizePrecision(
                            GetInt(
                                dr,
                                "StdPrecision",
                                2
                            )
                        );

                    decimal dueAmount =
                        Math.Round(
                            GetDecimal(
                                dr,
                                "DueAmount",
                                0
                            ),
                            rowPrecision,
                            MidpointRounding.AwayFromZero
                        );

                    rows.Add(
                        new
                        {
                            invoiceId =
                                GetInt(
                                    dr,
                                    "C_Invoice_ID"
                                ),

                            documentNo =
                                GetString(
                                    dr,
                                    "DocumentNo",
                                    string.Empty
                                ),

                            invoiceDate =
                                invoiceDate.HasValue
                                    ? invoiceDate.Value.ToString(
                                        "yyyy-MM-dd",
                                        CultureInfo.InvariantCulture
                                    )
                                    : string.Empty,

                            dueDate =
                                dueDate.HasValue
                                    ? dueDate.Value.ToString(
                                        "yyyy-MM-dd",
                                        CultureInfo.InvariantCulture
                                    )
                                    : string.Empty,

                            vendor =
                                GetString(
                                    dr,
                                    "VendorName",
                                    string.Empty
                                ),

                            invoiceCurrency =
                                invoiceCurrency,

                            invoiceCurrencySymbol =
                                invoiceCurrencySymbol,

                            amount =
                                dueAmount,

                            precision =
                                rowPrecision,

                            paymentMethodName =
                                paymentMethodName
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
                        rows =
                            rows,

                        pageNo =
                            pageNo,

                        pageSize =
                            pageSize,

                        totalRecords =
                            totalRecords,

                        totalPages =
                            totalPages,

                        vendorCount =
                            vendorCount,

                        paymentMethodCount =
                            paymentMethodCount,

                        dateFrom =
                            dateFrom,

                        dateTo =
                            dateTo
                    },
                    JsonRequestBehavior.AllowGet
                );
            }
            catch (Exception)
            {
                return Json(
                    new
                    {
                        error = true,
                        errorText = GetMsg(
                            ctx,
                            "VAS_ErrorLoading",
                            "Could not load data"
                        )
                    },
                    JsonRequestBehavior.AllowGet
                );
            }
            finally
            {
                CloseReader(dr);
            }
        }

        private SqlQueryData BuildScheduledAPPaymentThisWeekSql(
            Ctx ctx
        )
        {
            string queryParametersFrom =
                DB.IsOracle()
                    ? " FROM DUAL"
                    : string.Empty;

            string numericType =
                DB.IsOracle()
                    ? "NUMBER"
                    : "NUMERIC";

            string queryParametersSql = @"
QueryParameters AS
(
    SELECT
        @AD_Client_ID AS AD_Client_ID"
        + queryParametersFrom + @"
)";

            string weekRangeSql = @"
WeekRange AS
(
    SELECT
        " + GetWeekStartSql() + @" AS DateFrom,

        " + GetWeekEndExclusiveSql() + @" AS DateToExclusive,

        " + GetWeekEndDisplaySql() + @" AS DateTo

    FROM QueryParameters QueryParameters
)";

            string schemaCurrencySql = @"
SchemaCurrency AS
(
    SELECT
        ClientInfo.AD_Client_ID,
        AcctSchema.C_Currency_ID,
        Currency.StdPrecision,
        Currency.ISO_Code,
        Currency.CurSymbol

    FROM AD_ClientInfo ClientInfo

    INNER JOIN C_AcctSchema AcctSchema ON
    (
        AcctSchema.C_AcctSchema_ID =
        ClientInfo.C_AcctSchema1_ID
    )

    INNER JOIN C_Currency Currency ON
    (
        Currency.C_Currency_ID =
        AcctSchema.C_Currency_ID
    )

    WHERE ClientInfo.IsActive = 'Y'

    AND ClientInfo.AD_Client_ID =
    (
        SELECT
            QueryParameters.AD_Client_ID

        FROM QueryParameters QueryParameters
    )
)";

            string invoiceAccessSql = @"
SELECT
    Invoice.C_Invoice_ID,
    Invoice.AD_Client_ID,
    Invoice.AD_Org_ID,
    Invoice.C_BPartner_ID,
    Invoice.C_Currency_ID,
    Invoice.DateAcct,
    Invoice.C_ConversionType_ID,
    Invoice.IsReturnTrx

FROM C_Invoice Invoice

WHERE Invoice.IsActive = 'Y'

AND Invoice.AD_Client_ID =
(
    SELECT
        QueryParameters.AD_Client_ID

    FROM QueryParameters QueryParameters
)

AND Invoice.IsSOTrx = 'N'

AND Invoice.DocStatus IN
(
    'CO',
    'CL'
)";

            invoiceAccessSql =
                MRole.GetDefault(ctx)
                    .AddAccessSQL(
                        invoiceAccessSql,
                        "Invoice",
                        MRole.SQL_FULLYQUALIFIED,
                        MRole.SQL_RO
                    );

            string convertedAmountExpression = @"
CASE
    WHEN Invoice.C_Currency_ID =
         SchemaCurrency.C_Currency_ID

    THEN COALESCE
    (
        InvoicePaySchedule.DueAmt,
        0
    )

    ELSE CurrencyConvert
    (
        COALESCE
        (
            InvoicePaySchedule.DueAmt,
            0
        ),
        Invoice.C_Currency_ID,
        SchemaCurrency.C_Currency_ID,
        Invoice.DateAcct,
        Invoice.C_ConversionType_ID,
        Invoice.AD_Client_ID,
        Invoice.AD_Org_ID
    )
END";

            string sql = @"
WITH
" + queryParametersSql + @",
" + weekRangeSql + @",
" + schemaCurrencySql + @",
InvoiceFiltered AS
(
" + invoiceAccessSql + @"
),
ScheduledData AS
(
    SELECT
        SchemaCurrency.C_Currency_ID,

        SchemaCurrency.ISO_Code
            AS CurrencyISO,

        CASE
            WHEN SchemaCurrency.CurSymbol IS NOT NULL
            THEN SchemaCurrency.CurSymbol
            ELSE SchemaCurrency.ISO_Code
        END AS CurrencySymbol,

        SchemaCurrency.StdPrecision,

        CAST
        (
            CASE
                WHEN COALESCE
                (
                    Invoice.IsReturnTrx,
                    'N'
                ) = 'Y'

                THEN
                    0 -
                    (
                        " + convertedAmountExpression + @"
                    )

                ELSE
                    " + convertedAmountExpression + @"
            END
            AS " + numericType + @"
        ) AS ScheduledAmount,

        WeekRange.DateFrom,
        WeekRange.DateTo

    FROM InvoiceFiltered Invoice

    INNER JOIN C_InvoicePaySchedule InvoicePaySchedule ON
    (
        InvoicePaySchedule.C_Invoice_ID =
        Invoice.C_Invoice_ID
    )

    INNER JOIN SchemaCurrency SchemaCurrency ON
    (
        SchemaCurrency.AD_Client_ID =
        Invoice.AD_Client_ID
    )

    INNER JOIN WeekRange WeekRange ON
    (
        InvoicePaySchedule.DueDate >=
        WeekRange.DateFrom

        AND InvoicePaySchedule.DueDate <
        WeekRange.DateToExclusive
    )

    WHERE InvoicePaySchedule.IsActive = 'Y'

    AND COALESCE
    (
        InvoicePaySchedule.VA009_IsPaid,
        'N'
    ) <> 'Y'

    AND COALESCE
    (
        InvoicePaySchedule.DueAmt,
        0
    ) <> 0
)
SELECT
    MAX
    (
        ScheduledData.C_Currency_ID
    ) AS C_Currency_ID,

    MAX
    (
        ScheduledData.CurrencyISO
    ) AS CurrencyISO,

    MAX
    (
        ScheduledData.CurrencySymbol
    ) AS CurrencySymbol,

    COALESCE
    (
        MAX
        (
            ScheduledData.StdPrecision
        ),
        2
    ) AS StdPrecision,

    ROUND
    (
        CAST
        (
            COALESCE
            (
                SUM
                (
                    ScheduledData.ScheduledAmount
                ),
                0
            )
            AS " + numericType + @"
        ),

        CAST
        (
            COALESCE
            (
                MAX
                (
                    ScheduledData.StdPrecision
                ),
                2
            )
            AS INTEGER
        )
    ) AS ScheduledAmount,

    MIN
    (
        ScheduledData.DateFrom
    ) AS DateFrom,

    MAX
    (
        ScheduledData.DateTo
    ) AS DateTo

FROM ScheduledData ScheduledData";

            SqlParameter[] parameters =
                new SqlParameter[]
                {
                    new SqlParameter(
                        "@AD_Client_ID",
                        ctx.GetAD_Client_ID()
                    )
                };

            return new SqlQueryData
            {
                Sql =
                    sql,

                Parameters =
                    parameters
            };
        }

        private SqlQueryData BuildScheduledAPPaymentRowsSql(
            Ctx ctx,
            int pageNo,
            int pageSize
        )
        {
            bool hasPaymentMethod =
                HasInvoicePaymentMethodColumn();

            string paymentMethodDisplayColumn =
                GetPaymentMethodDisplayColumn(
                    hasPaymentMethod
                );

            bool hasPaymentMethodDisplayColumn =
                !string.IsNullOrWhiteSpace(
                    paymentMethodDisplayColumn
                );

            string queryParametersFrom =
                DB.IsOracle()
                    ? " FROM DUAL"
                    : string.Empty;

            string numericType =
                DB.IsOracle()
                    ? "NUMBER"
                    : "NUMERIC";

            int startRow =
                ((pageNo - 1) * pageSize) + 1;

            int endRow =
                pageNo * pageSize;

            string paymentMethodIdSelect =
                hasPaymentMethod
                    ? "COALESCE(Invoice.VA009_PaymentMethod_ID, 0)"
                    : "0";

            string paymentMethodDisplaySelect =
                hasPaymentMethodDisplayColumn
                    ? paymentMethodDisplayColumn
                    : "NULL";

            string paymentMethodJoin =
                hasPaymentMethodDisplayColumn
                    ? @"
LEFT OUTER JOIN VA009_PaymentMethod PaymentMethod ON
(
    PaymentMethod.VA009_PaymentMethod_ID =
    Invoice.VA009_PaymentMethod_ID
)"
                    : string.Empty;

            string queryParametersSql = @"
QueryParameters AS
(
    SELECT
        @AD_Client_ID AS AD_Client_ID,
        @StartRow AS StartRow,
        @EndRow AS EndRow"
        + queryParametersFrom + @"
)";

            string weekRangeSql = @"
WeekRange AS
(
    SELECT
        " + GetWeekStartSql() + @" AS DateFrom,

        " + GetWeekEndExclusiveSql() + @" AS DateToExclusive,

        " + GetWeekEndDisplaySql() + @" AS DateTo

    FROM QueryParameters QueryParameters
)";

            string invoiceAccessSql = @"
SELECT
    Invoice.C_Invoice_ID,
    Invoice.AD_Client_ID,
    Invoice.AD_Org_ID,
    Invoice.C_BPartner_ID,
    Invoice.C_Currency_ID,
    Invoice.DateAcct,
    Invoice.DateInvoiced,
    Invoice.DocumentNo,
    Invoice.C_ConversionType_ID,
    Invoice.IsReturnTrx,
    Invoice.PaymentRule" +
                (
                    hasPaymentMethod
                        ? @",
    Invoice.VA009_PaymentMethod_ID"
                        : string.Empty
                ) + @"

FROM C_Invoice Invoice

WHERE Invoice.IsActive = 'Y'

AND Invoice.AD_Client_ID =
(
    SELECT
        QueryParameters.AD_Client_ID

    FROM QueryParameters QueryParameters
)

AND Invoice.IsSOTrx = 'N'

AND Invoice.DocStatus IN
(
    'CO',
    'CL'
)";

            invoiceAccessSql =
                MRole.GetDefault(ctx)
                    .AddAccessSQL(
                        invoiceAccessSql,
                        "Invoice",
                        MRole.SQL_FULLYQUALIFIED,
                        MRole.SQL_RO
                    );

            string dueAmountExpression = @"
CASE
    WHEN COALESCE
    (
        Invoice.IsReturnTrx,
        'N'
    ) = 'Y'

    THEN
        0 -
        COALESCE
        (
            InvoicePaySchedule.DueAmt,
            0
        )

    ELSE
        COALESCE
        (
            InvoicePaySchedule.DueAmt,
            0
        )
END";

            string sql = @"
WITH
" + queryParametersSql + @",
" + weekRangeSql + @",
InvoiceFiltered AS
(
" + invoiceAccessSql + @"
),
ScheduledRowsData AS
(
    SELECT
        Invoice.C_Invoice_ID,
        Invoice.C_BPartner_ID,
        Invoice.DocumentNo,

        Invoice.DateInvoiced
            AS InvoiceDate,

        InvoicePaySchedule.DueDate,

        BPartner.Name
            AS VendorName,

        Currency.ISO_Code
            AS InvoiceCurrency,

        CASE
            WHEN Currency.CurSymbol IS NOT NULL
            THEN Currency.CurSymbol
            ELSE Currency.ISO_Code
        END AS InvoiceCurrencySymbol,

        COALESCE
        (
            Currency.StdPrecision,
            2
        ) AS StdPrecision,

        " + paymentMethodIdSelect + @"
            AS PaymentMethod_ID,

        " + paymentMethodDisplaySelect + @"
            AS PaymentMethodDisplay,

        Invoice.PaymentRule,

        ROUND
        (
            CAST
            (
                " + dueAmountExpression + @"
                AS " + numericType + @"
            ),

            CAST
            (
                COALESCE
                (
                    Currency.StdPrecision,
                    2
                )
                AS INTEGER
            )
        ) AS DueAmount,

        WeekRange.DateFrom,
        WeekRange.DateTo

    FROM InvoiceFiltered Invoice

    INNER JOIN C_InvoicePaySchedule InvoicePaySchedule ON
    (
        InvoicePaySchedule.C_Invoice_ID =
        Invoice.C_Invoice_ID
    )

    INNER JOIN WeekRange WeekRange ON
    (
        InvoicePaySchedule.DueDate >=
        WeekRange.DateFrom

        AND InvoicePaySchedule.DueDate <
        WeekRange.DateToExclusive
    )

    LEFT OUTER JOIN C_BPartner BPartner ON
    (
        BPartner.C_BPartner_ID =
        Invoice.C_BPartner_ID
    )

    LEFT OUTER JOIN C_Currency Currency ON
    (
        Currency.C_Currency_ID =
        Invoice.C_Currency_ID
    )
"
    + paymentMethodJoin + @"

    WHERE InvoicePaySchedule.IsActive = 'Y'

    AND COALESCE
    (
        InvoicePaySchedule.VA009_IsPaid,
        'N'
    ) <> 'Y'

    AND COALESCE
    (
        InvoicePaySchedule.DueAmt,
        0
    ) <> 0
),
SummaryData AS
(
    SELECT
        COUNT(1)
            AS TotalRecords,

        COUNT
        (
            DISTINCT ScheduledRowsData.C_BPartner_ID
        ) AS VendorCount,

        COUNT
        (
            DISTINCT ScheduledRowsData.PaymentMethod_ID
        ) AS PaymentMethodCount

    FROM ScheduledRowsData ScheduledRowsData
),
NumberedRows AS
(
    SELECT
        ScheduledRowsData.C_Invoice_ID,
        ScheduledRowsData.C_BPartner_ID,
        ScheduledRowsData.DocumentNo,
        ScheduledRowsData.InvoiceDate,
        ScheduledRowsData.DueDate,
        ScheduledRowsData.VendorName,
        ScheduledRowsData.InvoiceCurrency,
        ScheduledRowsData.InvoiceCurrencySymbol,
        ScheduledRowsData.StdPrecision,
        ScheduledRowsData.PaymentMethod_ID,
        ScheduledRowsData.PaymentMethodDisplay,
        ScheduledRowsData.PaymentRule,
        ScheduledRowsData.DueAmount,
        ScheduledRowsData.DateFrom,
        ScheduledRowsData.DateTo,

        ROW_NUMBER() OVER
        (
            ORDER BY
                ScheduledRowsData.DueDate,
                ScheduledRowsData.DocumentNo,
                ScheduledRowsData.C_Invoice_ID
        ) AS RowNumber

    FROM ScheduledRowsData ScheduledRowsData
)
SELECT
    NumberedRows.C_Invoice_ID,
    NumberedRows.C_BPartner_ID,
    NumberedRows.DocumentNo,
    NumberedRows.InvoiceDate,
    NumberedRows.DueDate,
    NumberedRows.VendorName,
    NumberedRows.InvoiceCurrency,
    NumberedRows.InvoiceCurrencySymbol,
    NumberedRows.StdPrecision,
    NumberedRows.PaymentMethod_ID,
    NumberedRows.PaymentMethodDisplay,
    NumberedRows.PaymentRule,
    NumberedRows.DueAmount,
    NumberedRows.DateFrom,
    NumberedRows.DateTo,
    SummaryData.TotalRecords,
    SummaryData.VendorCount,
    SummaryData.PaymentMethodCount

FROM NumberedRows NumberedRows

INNER JOIN SummaryData SummaryData ON
(
    1 = 1
)

WHERE NumberedRows.RowNumber >=
(
    SELECT
        QueryParameters.StartRow

    FROM QueryParameters QueryParameters
)

AND NumberedRows.RowNumber <=
(
    SELECT
        QueryParameters.EndRow

    FROM QueryParameters QueryParameters
)

ORDER BY
    NumberedRows.RowNumber";

            SqlParameter[] parameters =
                new SqlParameter[]
                {
                    new SqlParameter(
                        "@AD_Client_ID",
                        ctx.GetAD_Client_ID()
                    ),

                    new SqlParameter(
                        "@StartRow",
                        startRow
                    ),

                    new SqlParameter(
                        "@EndRow",
                        endRow
                    )
                };

            return new SqlQueryData
            {
                Sql =
                    sql,

                Parameters =
                    parameters
            };
        }

        private string GetWeekStartSql()
        {
            if (DB.IsOracle())
            {
                return
                    "TRUNC(CURRENT_DATE)";
            }

            return
                "CURRENT_DATE";
        }

        private string GetWeekEndExclusiveSql()
        {
            if (DB.IsOracle())
            {
                return
                    "TRUNC(CURRENT_DATE) + 7";
            }

            return
                "CURRENT_DATE + INTERVAL '7 DAY'";
        }

        private string GetWeekEndDisplaySql()
        {
            if (DB.IsOracle())
            {
                return
                    "TRUNC(CURRENT_DATE) + 6";
            }

            return
                "CURRENT_DATE + INTERVAL '6 DAY'";
        }

        private string GetPaymentMethodDisplayColumn(
            bool hasPaymentMethod
        )
        {
            if (!hasPaymentMethod)
            {
                return string.Empty;
            }

            if (HasPaymentMethodVA009NameColumn())
            {
                return
                    "PaymentMethod.VA009_Name";
            }

            if (HasPaymentMethodNameColumn())
            {
                return
                    "PaymentMethod.Name";
            }

            if (HasPaymentMethodValueColumn())
            {
                return
                    "PaymentMethod.Value";
            }

            return string.Empty;
        }

        private bool HasInvoicePaymentMethodColumn()
        {
            return HasColumn(
                "C_Invoice",
                "VA009_PaymentMethod_ID"
            );
        }

        private bool HasPaymentMethodVA009NameColumn()
        {
            return HasColumn(
                "VA009_PaymentMethod",
                "VA009_Name"
            );
        }

        private bool HasPaymentMethodNameColumn()
        {
            return HasColumn(
                "VA009_PaymentMethod",
                "Name"
            );
        }

        private bool HasPaymentMethodValueColumn()
        {
            return HasColumn(
                "VA009_PaymentMethod",
                "Value"
            );
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

WHERE TableData.TableName = "
                + ToSqlString(tableName) + @"

AND ColumnData.ColumnName = "
                + ToSqlString(columnName);

            return Util.GetValueOfInt(
                DB.ExecuteScalar(sql)
            ) > 0;
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
            Ctx ctx =
                Env.GetCtx();

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
            string msg =
                Msg.GetMsg(
                    ctx,
                    key
                );

            return
                !string.IsNullOrWhiteSpace(
                    msg
                ) &&
                msg != "[" + key + "]"
                    ? msg
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

            for (
                int i = 0;
                i < values.Length;
                i++
            )
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

        private string FormatDate(
            DateTime? date
        )
        {
            return
                date.HasValue
                    ? date.Value.ToString(
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture
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

        private class SqlQueryData
        {
            public string Sql
            {
                get;
                set;
            }

            public SqlParameter[] Parameters
            {
                get;
                set;
            }
        }
    }
}

