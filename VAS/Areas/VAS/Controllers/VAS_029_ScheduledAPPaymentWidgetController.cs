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
    /// Purpose     : Provides scheduled AP payment KPI widget data
    ///               grouped by payment method.
    ///
    /// Compatible with:
    /// - Oracle
    /// - PostgreSQL
    /// </summary>
    public class VAS_029_ScheduledAPPaymentWidgetController : Controller
    {
        /// <summary>
        /// Returns AP invoice schedule amounts due during
        /// the next seven days, grouped by payment method.
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
            string sql = string.Empty;

            try
            {
                sql = BuildScheduledAPPaymentThisWeekSql(ctx);

                dr = DB.ExecuteReader(
                    sql,
                    null,
                    null
                );

                decimal scheduledAmountThisWeek = 0;

                int cCurrencyId = 0;
                int precision = 2;

                string currencyISO = string.Empty;
                string currencySymbol = string.Empty;
                string dateFrom = string.Empty;
                string dateTo = string.Empty;

                List<object> groups = new List<object>();

                while (dr != null && dr.Read())
                {
                    decimal scheduledAmount = GetDecimal(
                        dr,
                        "ScheduledAmount",
                        0
                    );

                    int groupCurrencyId = GetInt(
                        dr,
                        "C_Currency_ID"
                    );

                    int groupPrecision = NormalizePrecision(
                        GetInt(
                            dr,
                            "StdPrecision",
                            2
                        )
                    );

                    string groupCurrencyISO = GetString(
                        dr,
                        "CurrencyISO",
                        string.Empty
                    );

                    string groupCurrencySymbol = GetString(
                        dr,
                        "CurrencySymbol",
                        string.Empty
                    );

                    /*
                     * The fallback is performed in C# instead of SQL CASE
                     * to avoid Oracle character-set incompatibility errors.
                     */
                    if (string.IsNullOrWhiteSpace(groupCurrencySymbol))
                    {
                        groupCurrencySymbol = groupCurrencyISO;
                    }

                    string paymentMethodDisplay = GetString(
                        dr,
                        "PaymentMethodDisplay",
                        string.Empty
                    );

                    string paymentRule = GetString(
                        dr,
                        "PaymentRule",
                        string.Empty
                    );

                    string paymentMethodName = FirstNotEmpty(
                        paymentMethodDisplay,
                        paymentRule,
                        GetMsg(
                            ctx,
                            "VAS_029_MessageNotSpecified",
                            "Not Specified"
                        )
                    );

                    scheduledAmountThisWeek += scheduledAmount;

                    if (cCurrencyId == 0)
                    {
                        cCurrencyId = groupCurrencyId;
                        precision = groupPrecision;
                        currencyISO = groupCurrencyISO;
                        currencySymbol = groupCurrencySymbol;
                    }

                    if (
                        string.IsNullOrWhiteSpace(dateFrom) &&
                        dr["DateFrom"] != DBNull.Value
                    )
                    {
                        dateFrom = FormatDate(
                            Util.GetValueOfDateTime(
                                dr["DateFrom"]
                            )
                        );
                    }

                    if (
                        string.IsNullOrWhiteSpace(dateTo) &&
                        dr["DateTo"] != DBNull.Value
                    )
                    {
                        dateTo = FormatDate(
                            Util.GetValueOfDateTime(
                                dr["DateTo"]
                            )
                        );
                    }

                    scheduledAmount = Math.Round(
                        scheduledAmount,
                        groupPrecision,
                        MidpointRounding.AwayFromZero
                    );

                    groups.Add(
                        new
                        {
                            paymentMethodName = paymentMethodName,

                            value = scheduledAmount,
                            scheduledAmount = scheduledAmount,

                            cCurrencyId = groupCurrencyId,
                            currencyISO = groupCurrencyISO,
                            currencySymbol = groupCurrencySymbol,
                            symbol = groupCurrencySymbol,
                            precision = groupPrecision
                        }
                    );
                }

                precision = NormalizePrecision(precision);

                scheduledAmountThisWeek = Math.Round(
                    scheduledAmountThisWeek,
                    precision,
                    MidpointRounding.AwayFromZero
                );

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
                            "VAS_029_MessageScheduledForPaymentThisWeek",
                            "Scheduled for payment this week"
                        ),

                        value = scheduledAmountThisWeek,

                        scheduledAmountThisWeek =
                            scheduledAmountThisWeek,

                        groups = groups,

                        cCurrencyId = cCurrencyId,
                        currencyISO = currencyISO,
                        currencySymbol = currencySymbol,
                        symbol = currencySymbol,
                        precision = precision,

                        dateFrom = dateFrom,
                        dateTo = dateTo
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
                        errorText = ex.Message
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
            string sql = string.Empty;

            try
            {
                sql = BuildScheduledAPPaymentRowsSql(
                    ctx,
                    pageNo,
                    pageSize
                );

                dr = DB.ExecuteReader(
                    sql,
                    null,
                    null
                );

                List<object> rows = new List<object>();

                int totalRecords = 0;
                int vendorCount = 0;
                int paymentMethodCount = 0;

                string dateFrom = string.Empty;
                string dateTo = string.Empty;

                while (dr != null && dr.Read())
                {
                    totalRecords = GetInt(
                        dr,
                        "TotalRecords"
                    );

                    vendorCount = GetInt(
                        dr,
                        "VendorCount"
                    );

                    paymentMethodCount = GetInt(
                        dr,
                        "PaymentMethodCount"
                    );

                    if (
                        string.IsNullOrWhiteSpace(dateFrom) &&
                        dr["DateFrom"] != DBNull.Value
                    )
                    {
                        dateFrom = FormatDate(
                            Util.GetValueOfDateTime(
                                dr["DateFrom"]
                            )
                        );
                    }

                    if (
                        string.IsNullOrWhiteSpace(dateTo) &&
                        dr["DateTo"] != DBNull.Value
                    )
                    {
                        dateTo = FormatDate(
                            Util.GetValueOfDateTime(
                                dr["DateTo"]
                            )
                        );
                    }

                    DateTime? invoiceDate = null;
                    DateTime? dueDate = null;

                    if (dr["InvoiceDate"] != DBNull.Value)
                    {
                        invoiceDate =
                            Util.GetValueOfDateTime(
                                dr["InvoiceDate"]
                            );
                    }

                    if (dr["DueDate"] != DBNull.Value)
                    {
                        dueDate =
                            Util.GetValueOfDateTime(
                                dr["DueDate"]
                            );
                    }

                    string paymentMethodDisplay = GetString(
                        dr,
                        "PaymentMethodDisplay",
                        string.Empty
                    );

                    string paymentRule = GetString(
                        dr,
                        "PaymentRule",
                        string.Empty
                    );

                    string paymentMethodName = FirstNotEmpty(
                        paymentMethodDisplay,
                        paymentRule,
                        GetMsg(
                            ctx,
                            "VAS_029_MessageNotSpecified",
                            "Not Specified"
                        )
                    );

                    string invoiceCurrency = GetString(
                        dr,
                        "InvoiceCurrency",
                        string.Empty
                    );

                    string invoiceCurrencySymbol = GetString(
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

                    int rowPrecision = NormalizePrecision(
                        GetInt(
                            dr,
                            "StdPrecision",
                            2
                        )
                    );

                    decimal dueAmount = Math.Round(
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
                            invoiceId = GetInt(
                                dr,
                                "C_Invoice_ID"
                            ),

                            documentNo = GetString(
                                dr,
                                "DocumentNo",
                                string.Empty
                            ),

                            invoiceDate = invoiceDate.HasValue
                                ? invoiceDate.Value.ToString(
                                    "yyyy-MM-dd",
                                    CultureInfo.InvariantCulture
                                )
                                : string.Empty,

                            dueDate = dueDate.HasValue
                                ? dueDate.Value.ToString(
                                    "yyyy-MM-dd",
                                    CultureInfo.InvariantCulture
                                )
                                : string.Empty,

                            vendor = GetString(
                                dr,
                                "VendorName",
                                string.Empty
                            ),

                            invoiceCurrency =
                                invoiceCurrency,

                            invoiceCurrencySymbol =
                                invoiceCurrencySymbol,

                            amount = dueAmount,
                            precision = rowPrecision,

                            paymentMethodName =
                                paymentMethodName
                        }
                    );
                }

                int totalPages = totalRecords > 0
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
                        rows = rows,

                        pageNo = pageNo,
                        pageSize = pageSize,

                        totalRecords = totalRecords,
                        totalPages = totalPages,

                        vendorCount = vendorCount,

                        paymentMethodCount =
                            paymentMethodCount,

                        dateFrom = dateFrom,
                        dateTo = dateTo
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
                        errorText = ex.Message
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
        /// Builds the KPI query grouped by payment method.
        /// </summary>
        private string BuildScheduledAPPaymentThisWeekSql(
            Ctx ctx
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

            string clientIdSql = ctx
                .GetAD_Client_ID()
                .ToString(
                    CultureInfo.InvariantCulture
                );

            string paymentMethodIdSelect =
                hasPaymentMethod
                    ? "COALESCE(Invoice.VA009_PaymentMethod_ID, 0)"
                    : "0";

            /*
             * Do not use CASE between PaymentRule and payment-method name.
             * Oracle can raise ORA-12704 when the character sets differ.
             *
             * Both values are returned separately and the fallback
             * is handled in C#.
             */
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

            string weekRangeSql = @"
WeekRange AS
(
    SELECT
        " + GetWeekStartSql() + @" AS DateFrom,

        " + GetWeekEndExclusiveSql() + @" AS DateToExclusive,

        " + GetWeekEndDisplaySql() + @" AS DateTo

    FROM AD_ClientInfo ClientInfo

    WHERE ClientInfo.IsActive = 'Y'

    AND ClientInfo.AD_Client_ID =
        " + clientIdSql + @"
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
        " + clientIdSql + @"
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
    " + clientIdSql + @"

AND Invoice.IsSOTrx = 'N'

AND Invoice.DocStatus IN ('CO', 'CL')";

            invoiceAccessSql =
                MRole.GetDefault(ctx).AddAccessSQL(
                    invoiceAccessSql,
                    "Invoice",
                    MRole.SQL_FULLYQUALIFIED,
                    MRole.SQL_RO
                );

            string sql = @"
WITH
" + weekRangeSql + @",

" + schemaCurrencySql + @",

InvoiceFiltered AS
(
" + invoiceAccessSql + @"
),

ScheduledData AS
(
    SELECT
        Invoice.C_Invoice_ID,
        Invoice.C_BPartner_ID,

        SchemaCurrency.C_Currency_ID,

        SchemaCurrency.ISO_Code AS CurrencyISO,

        SchemaCurrency.CurSymbol AS CurrencySymbol,

        SchemaCurrency.StdPrecision,

        " + paymentMethodIdSelect + @" AS PaymentMethod_ID,

        " + paymentMethodDisplaySelect + @" AS PaymentMethodDisplay,

        Invoice.PaymentRule,

        CASE
            WHEN COALESCE(
                Invoice.IsReturnTrx,
                'N'
            ) = 'Y'
            THEN
                -CASE
                    WHEN Invoice.C_Currency_ID =
                         SchemaCurrency.C_Currency_ID
                    THEN COALESCE(
                        InvoicePaySchedule.DueAmt,
                        0
                    )

                    ELSE CurrencyConvert
                    (
                        COALESCE(
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
                END

            ELSE
                CASE
                    WHEN Invoice.C_Currency_ID =
                         SchemaCurrency.C_Currency_ID
                    THEN COALESCE(
                        InvoicePaySchedule.DueAmt,
                        0
                    )

                    ELSE CurrencyConvert
                    (
                        COALESCE(
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
                END
        END AS ScheduledAmount,

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

    " + paymentMethodJoin + @"

    WHERE InvoicePaySchedule.IsActive = 'Y'

    AND COALESCE(
        InvoicePaySchedule.VA009_IsPaid,
        'N'
    ) <> 'Y'

    AND COALESCE(
        InvoicePaySchedule.DueAmt,
        0
    ) > 0
)

SELECT
    ScheduledData.PaymentMethod_ID,

    ScheduledData.PaymentMethodDisplay,

    ScheduledData.PaymentRule,

    ScheduledData.C_Currency_ID,

    ScheduledData.CurrencyISO,

    ScheduledData.CurrencySymbol,

    MAX(
        ScheduledData.StdPrecision
    ) AS StdPrecision,

    ROUND
    (
        COALESCE
        (
            SUM(
                ScheduledData.ScheduledAmount
            ),
            0
        ),

        CAST
        (
            COALESCE
            (
                MAX(
                    ScheduledData.StdPrecision
                ),
                2
            ) AS INTEGER
        )
    ) AS ScheduledAmount,

    MIN(
        ScheduledData.DateFrom
    ) AS DateFrom,

    MAX(
        ScheduledData.DateTo
    ) AS DateTo

FROM ScheduledData ScheduledData

GROUP BY
    ScheduledData.PaymentMethod_ID,
    ScheduledData.PaymentMethodDisplay,
    ScheduledData.PaymentRule,
    ScheduledData.C_Currency_ID,
    ScheduledData.CurrencyISO,
    ScheduledData.CurrencySymbol

HAVING SUM(
    ScheduledData.ScheduledAmount
) > 0

ORDER BY
    ScheduledAmount DESC";

            return sql;
        }

        /// <summary>
        /// Builds the paginated scheduled-payment rows query.
        ///
        /// ROW_NUMBER is used because it works on both
        /// Oracle and PostgreSQL.
        ///
        /// DISTINCT aggregate counts are calculated in a regular
        /// aggregate CTE because PostgreSQL does not support:
        /// COUNT(DISTINCT column) OVER().
        /// </summary>
        private string BuildScheduledAPPaymentRowsSql(
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

            string clientIdSql = ctx
                .GetAD_Client_ID()
                .ToString(
                    CultureInfo.InvariantCulture
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

            string weekRangeSql = @"
WeekRange AS
(
    SELECT
        " + GetWeekStartSql() + @" AS DateFrom,

        " + GetWeekEndExclusiveSql() + @" AS DateToExclusive,

        " + GetWeekEndDisplaySql() + @" AS DateTo

    FROM AD_ClientInfo ClientInfo

    WHERE ClientInfo.IsActive = 'Y'

    AND ClientInfo.AD_Client_ID =
        " + clientIdSql + @"
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
    " + clientIdSql + @"

AND Invoice.IsSOTrx = 'N'

AND Invoice.DocStatus IN ('CO', 'CL')";

            invoiceAccessSql =
                MRole.GetDefault(ctx).AddAccessSQL(
                    invoiceAccessSql,
                    "Invoice",
                    MRole.SQL_FULLYQUALIFIED,
                    MRole.SQL_RO
                );

            string sql = @"
WITH
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

        Invoice.DateInvoiced AS InvoiceDate,

        InvoicePaySchedule.DueDate,

        BPartner.Name AS VendorName,

        Currency.ISO_Code AS InvoiceCurrency,

        Currency.CurSymbol AS InvoiceCurrencySymbol,

        COALESCE(
            Currency.StdPrecision,
            2
        ) AS StdPrecision,

        " + paymentMethodIdSelect + @" AS PaymentMethod_ID,

        " + paymentMethodDisplaySelect + @" AS PaymentMethodDisplay,

        Invoice.PaymentRule,

        CASE
            WHEN COALESCE(
                Invoice.IsReturnTrx,
                'N'
            ) = 'Y'
            THEN -COALESCE(
                InvoicePaySchedule.DueAmt,
                0
            )

            ELSE COALESCE(
                InvoicePaySchedule.DueAmt,
                0
            )
        END AS DueAmount,

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

    " + paymentMethodJoin + @"

    WHERE InvoicePaySchedule.IsActive = 'Y'

    AND COALESCE(
        InvoicePaySchedule.VA009_IsPaid,
        'N'
    ) <> 'Y'

    AND COALESCE(
        InvoicePaySchedule.DueAmt,
        0
    ) > 0
),

SummaryData AS
(
    SELECT
        COUNT(1) AS TotalRecords,

        COUNT
        (
            DISTINCT
            ScheduledRowsData.C_BPartner_ID
        ) AS VendorCount,

        COUNT
        (
            DISTINCT
            ScheduledRowsData.PaymentMethod_ID
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
                ScheduledRowsData.DueDate ASC,
                ScheduledRowsData.DocumentNo ASC,
                ScheduledRowsData.C_Invoice_ID ASC
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

CROSS JOIN SummaryData SummaryData

WHERE NumberedRows.RowNumber >=
    " + startRowSql + @"

AND NumberedRows.RowNumber <=
    " + endRowSql + @"

ORDER BY
    NumberedRows.RowNumber";

            return sql;
        }

        /// <summary>
        /// Returns today's date without the time.
        /// </summary>
        private string GetWeekStartSql()
        {
            if (DB.IsOracle())
            {
                return "TRUNC(CURRENT_DATE)";
            }

            /*
             * PostgreSQL CURRENT_DATE has no time component.
             */
            return "CURRENT_DATE";
        }

        /// <summary>
        /// Returns the exclusive end date: today + 7 days.
        /// </summary>
        private string GetWeekEndExclusiveSql()
        {
            if (DB.IsOracle())
            {
                return "TRUNC(CURRENT_DATE) + 7";
            }

            /*
             * PostgreSQL supports DATE + INTEGER.
             * This keeps the result as a DATE.
             */
            return "CURRENT_DATE + 7";
        }

        /// <summary>
        /// Returns the displayed end date: today + 6 days.
        /// </summary>
        private string GetWeekEndDisplaySql()
        {
            if (DB.IsOracle())
            {
                return "TRUNC(CURRENT_DATE) + 6";
            }

            return "CURRENT_DATE + 6";
        }

        /// <summary>
        /// Determines which payment-method description column exists.
        /// </summary>
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
                return "PaymentMethod.VA009_Name";
            }

            if (HasPaymentMethodNameColumn())
            {
                return "PaymentMethod.Name";
            }

            if (HasPaymentMethodValueColumn())
            {
                return "PaymentMethod.Value";
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

WHERE TableData.TableName = " +
                ToSqlString(tableName) + @"

AND ColumnData.ColumnName = " +
                ToSqlString(columnName);

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
            string msg = Msg.GetMsg(
                ctx,
                key
            );

            return
                !string.IsNullOrWhiteSpace(msg) &&
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

            for (int i = 0; i < values.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(values[i]))
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
            if (precision < 0 || precision > 28)
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
            object value = reader[columnName];

            return value == null ||
                   value == DBNull.Value
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

            return value == null ||
                   value == DBNull.Value
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

            return value == null ||
                   value == DBNull.Value
                ? fallback
                : Util.GetValueOfString(value);
        }

        private string FormatDate(
            DateTime? date
        )
        {
            return date.HasValue
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
    }
}
