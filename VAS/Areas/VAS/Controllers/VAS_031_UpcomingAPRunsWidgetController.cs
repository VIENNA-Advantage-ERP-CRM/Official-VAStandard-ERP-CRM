using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Web.Mvc;
using VAdvantage.Classes;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VAS.Controllers
{
    /// <summary>
    /// Upcoming AP invoices due during the next seven days.
    ///
    /// Main widget:
    ///     Reads AP invoices from C_Invoice.
    ///
    /// Details popup:
    ///     Reads individual invoices for the selected:
    ///     Due Date + Payment Method + Currency.
    ///
    /// Create:
    ///     Creates and completes C_Payment linked to C_Invoice.
    /// </summary>
    public class VAS_031_UpcomingAPRunsWidgetController : Controller
    {
        private const int MaximumRows = 30;

        #region Upcoming AP Runs

        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetUpcomingAPRuns()
        {
            Ctx ctx = GetContext();

            if (ctx == null)
            {
                return GetSessionExpiredResult();
            }

            IDataReader reader = null;

            try
            {
                string sql = BuildUpcomingAPRunsSql(ctx);

                reader = DB.ExecuteReader(
                    sql,
                    null,
                    null
                );

                List<object> runs = new List<object>();

                while (
                    reader != null &&
                    reader.Read() &&
                    runs.Count < MaximumRows
                )
                {
                    DateTime? runDate = GetNullableDateTime(
                        reader,
                        "RunDate"
                    );

                    int paymentCount = GetInt(
                        reader,
                        "PaymentCount"
                    );

                    string currencyISO = GetString(
                        reader,
                        "CurrencyISO"
                    );

                    string currencySymbol = GetString(
                        reader,
                        "CurrencySymbol"
                    );

                    if (string.IsNullOrWhiteSpace(currencySymbol))
                    {
                        currencySymbol = currencyISO;
                    }

                    runs.Add(new
                    {
                        paymentMethodId = GetInt(
                            reader,
                            "VA009_PaymentMethod_ID"
                        ),

                        paymentMethodName = GetPaymentMethodName(
                            ctx,
                            GetString(
                                reader,
                                "PaymentMethodName"
                            )
                        ),

                        runDate = FormatDate(
                            runDate
                        ),

                        dueDate = FormatDate(
                            runDate
                        ),

                        runDateText = FormatRunDate(
                            runDate
                        ),

                        dueDateText = FormatRunDate(
                            runDate
                        ),

                        paymentCount = paymentCount,

                        paymentCountText = GetPaymentCountText(
                            ctx,
                            paymentCount
                        ),

                        amount = GetDecimal(
                            reader,
                            "Amount"
                        ),

                        totalAmount = GetDecimal(
                            reader,
                            "Amount"
                        ),

                        cCurrencyId = GetInt(
                            reader,
                            "C_Currency_ID"
                        ),

                        currencyId = GetInt(
                            reader,
                            "C_Currency_ID"
                        ),

                        currencyISO = currencyISO,

                        currencySymbol = currencySymbol,

                        stdPrecision = NormalizePrecision(
                            GetInt(
                                reader,
                                "StdPrecision",
                                2
                            )
                        ),

                        vendorName = GetString(
                            reader,
                            "VendorName"
                        )
                    });
                }

                return Json(new
                {
                    success = true,
                    error = string.Empty,

                    title = GetMsg(
                        ctx,
                        "VAS_031_MessageUpcomingRuns",
                        "Upcoming runs"
                    ),

                    periodText = GetMsg(
                        ctx,
                        "VAS_031_MessageNext7Days",
                        "Next 7 days"
                    ),

                    hasData = runs.Count > 0,
                    runs = runs
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                VLogger.Get().SaveError(
                    "VAS_031_GetUpcomingAPRuns",
                    ex
                );

                string message = GetMsg(
                    ctx,
                    "VAS_ErrorLoading",
                    "Could not load data"
                );

                return Json(new
                {
                    success = false,
                    error = message,
                    errorText = message,
                    hasData = false
                }, JsonRequestBehavior.AllowGet);
            }
            finally
            {
                CloseReader(reader);
            }
        }

        private string BuildUpcomingAPRunsSql(Ctx ctx)
        {
            int clientId = ctx.GetAD_Client_ID();

            string clientIdSql = clientId.ToString(
                CultureInfo.InvariantCulture
            );

            string currentDateSql = DB.IsOracle()
                ? "TRUNC(CURRENT_DATE)"
                : "CURRENT_DATE";

            string dateToSql = DB.IsOracle()
                ? "TRUNC(CURRENT_DATE) + 7"
                : "CURRENT_DATE + 7";

            /*
             * MRole is applied only to the physical C_Invoice table.
             */
            string invoiceAccessSql = @"
SELECT
    Invoice.C_Invoice_ID,
    Invoice.AD_Client_ID,
    Invoice.AD_Org_ID,
    Invoice.C_BPartner_ID,
    Invoice.C_BPartner_Location_ID,
    Invoice.C_DocType_ID,
    Invoice.C_DocTypeTarget_ID,
    Invoice.C_Currency_ID,
    Invoice.C_ConversionType_ID,
    Invoice.DocumentNo,
    Invoice.DateInvoiced,
    Invoice.DateAcct,
    Invoice.GrandTotal,
    Invoice.VA009_PaymentMethod_ID
FROM C_Invoice Invoice
WHERE Invoice.IsActive = 'Y'
AND Invoice.AD_Client_ID = " + clientIdSql + @"
AND Invoice.IsSOTrx = 'N'
AND Invoice.DocStatus IN ('CO', 'CL')
AND EXISTS
(
    SELECT 1
    FROM C_DocType InvoiceDocumentType
    WHERE InvoiceDocumentType.C_DocType_ID = Invoice.C_DocType_ID
    AND InvoiceDocumentType.IsActive = 'Y'
    AND InvoiceDocumentType.DocBaseType = 'API'
    AND InvoiceDocumentType.IsSOTrx = 'N'
)";

            invoiceAccessSql = MRole.GetDefault(ctx).AddAccessSQL(
                invoiceAccessSql,
                "Invoice",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            string sql = @"
WITH SecuredInvoice AS
(
" + invoiceAccessSql + @"
),
InvoiceAllocation AS
(
    SELECT
        AllocationLine.C_Invoice_ID,

        SUM
        (
            COALESCE
            (
                AllocationLine.Amount,
                0
            )
            +
            COALESCE
            (
                AllocationLine.DiscountAmt,
                0
            )
            +
            COALESCE
            (
                AllocationLine.WriteOffAmt,
                0
            )
        ) AS AllocatedAmt

    FROM C_AllocationLine AllocationLine

    WHERE AllocationLine.IsActive = 'Y'

    AND AllocationLine.C_Invoice_ID IS NOT NULL

    GROUP BY
        AllocationLine.C_Invoice_ID
),
UpcomingInvoices AS
(
    SELECT
        Invoice.C_Invoice_ID,

        Invoice.C_BPartner_ID,

        Invoice.C_BPartner_Location_ID,

        InvoicePaySchedule.C_InvoicePaySchedule_ID,

        BusinessPartner.Name AS VendorName,

        COALESCE
        (
            InvoicePaySchedule.DueDate,
            Invoice.DateAcct
        ) AS DueDate,

        Invoice.C_Currency_ID,

        Currency.ISO_Code AS CurrencyISO,

        Currency.CurSymbol AS CurrencySymbol,

        Currency.StdPrecision,

        Invoice.VA009_PaymentMethod_ID,

        PaymentMethod.VA009_Name AS PaymentMethodName,

        CASE
            WHEN
            (
                Invoice.GrandTotal -
                COALESCE
                (
                    InvoiceAllocation.AllocatedAmt,
                    0
                )
            ) <= 0
            THEN 0

            WHEN InvoicePaySchedule.C_InvoicePaySchedule_ID
                 IS NOT NULL

            AND COALESCE
            (
                InvoicePaySchedule.DueAmt,
                0
            ) > 0

            AND InvoicePaySchedule.DueAmt <
            (
                Invoice.GrandTotal -
                COALESCE
                (
                    InvoiceAllocation.AllocatedAmt,
                    0
                )
            )
            THEN InvoicePaySchedule.DueAmt

            ELSE
            (
                Invoice.GrandTotal -
                COALESCE
                (
                    InvoiceAllocation.AllocatedAmt,
                    0
                )
            )
        END AS OpenAmount

    FROM SecuredInvoice Invoice

    INNER JOIN C_BPartner BusinessPartner ON
    (
        BusinessPartner.C_BPartner_ID =
        Invoice.C_BPartner_ID
    )

    INNER JOIN C_InvoicePaySchedule InvoicePaySchedule ON
    (
        InvoicePaySchedule.C_Invoice_ID =
        Invoice.C_Invoice_ID

        AND InvoicePaySchedule.IsActive = 'Y'

        AND COALESCE
        (
            InvoicePaySchedule.DueAmt,
            0
        ) > 0

        AND NOT EXISTS
        (
            SELECT 1

            FROM C_Payment ExistingPayment

            WHERE ExistingPayment.IsActive = 'Y'

            AND ExistingPayment.C_Invoice_ID =
            Invoice.C_Invoice_ID

            AND ExistingPayment.C_InvoicePaySchedule_ID =
            InvoicePaySchedule.C_InvoicePaySchedule_ID

            AND ExistingPayment.DocStatus NOT IN
            (
                'VO',
                'RE'
            )
        )
    )

    LEFT OUTER JOIN InvoiceAllocation InvoiceAllocation ON
    (
        InvoiceAllocation.C_Invoice_ID =
        Invoice.C_Invoice_ID
    )

    LEFT OUTER JOIN C_Currency Currency ON
    (
        Currency.C_Currency_ID =
        Invoice.C_Currency_ID
    )

    LEFT OUTER JOIN VA009_PaymentMethod PaymentMethod ON
    (
        PaymentMethod.VA009_PaymentMethod_ID =
        Invoice.VA009_PaymentMethod_ID
    )

    WHERE BusinessPartner.IsActive = 'Y'

    AND BusinessPartner.IsVendor = 'Y'

    AND COALESCE
    (
        InvoicePaySchedule.DueDate,
        Invoice.DateAcct
    ) >= " + currentDateSql + @"

    AND COALESCE
    (
        InvoicePaySchedule.DueDate,
        Invoice.DateAcct
    ) < " + dateToSql + @"
)
SELECT
    DueDate AS RunDate,

    COALESCE
    (
        VA009_PaymentMethod_ID,
        0
    ) AS VA009_PaymentMethod_ID,

    PaymentMethodName,

    C_Currency_ID,

    CurrencyISO,

    CurrencySymbol,

    MAX
    (
        COALESCE
        (
            StdPrecision,
            2
        )
    ) AS StdPrecision,

    COUNT(1) AS PaymentCount,

    MIN(VendorName) AS VendorName,

    SUM(OpenAmount) AS Amount

FROM UpcomingInvoices

WHERE OpenAmount > 0

GROUP BY
    DueDate,

    COALESCE
    (
        VA009_PaymentMethod_ID,
        0
    ),

    PaymentMethodName,

    C_Currency_ID,

    CurrencyISO,

    CurrencySymbol

ORDER BY
    RunDate,

    Amount DESC";

            return sql;
        }

        #endregion

        #region Upcoming Invoice Details

        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetUpcomingAPRunDetails(
            string runDate,
            int paymentMethodId = 0,
            int currencyId = 0)
        {
            Ctx ctx = GetContext();

            if (ctx == null)
            {
                return GetSessionExpiredResult();
            }

            DateTime parsedRunDate;

            if (!DateTime.TryParseExact(
                runDate,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out parsedRunDate))
            {
                string invalidDateMessage = GetMsg(
                    ctx,
                    "VAS_031_MessageTransactionDateInvalid",
                    "Invalid due date."
                );

                return Json(new
                {
                    success = false,
                    error = invalidDateMessage,
                    errorText = invalidDateMessage
                }, JsonRequestBehavior.AllowGet);
            }

            if (currencyId <= 0)
            {
                string currencyMessage = GetMsg(
                    ctx,
                    "VAS_031_MessageCurrencyRequired",
                    "Currency is required."
                );

                return Json(new
                {
                    success = false,
                    error = currencyMessage,
                    errorText = currencyMessage
                }, JsonRequestBehavior.AllowGet);
            }

            IDataReader reader = null;

            try
            {
                DateTime dateFrom = parsedRunDate.Date;
                DateTime dateTo = dateFrom.AddDays(1);

                string clientIdSql =
                    ctx.GetAD_Client_ID().ToString(
                        CultureInfo.InvariantCulture
                    );

                string paymentMethodIdSql =
                    Math.Max(
                        0,
                        paymentMethodId
                    ).ToString(
                        CultureInfo.InvariantCulture
                    );

                string currencyIdSql =
                    currencyId.ToString(
                        CultureInfo.InvariantCulture
                    );

                string invoiceAccessSql = @"
SELECT
    Invoice.C_Invoice_ID,
    Invoice.AD_Client_ID,
    Invoice.AD_Org_ID,
    Invoice.C_BPartner_ID,
    Invoice.C_BPartner_Location_ID,
    Invoice.C_DocType_ID,
    Invoice.C_DocTypeTarget_ID,
    Invoice.C_Currency_ID,
    Invoice.C_ConversionType_ID,
    Invoice.DocumentNo,
    Invoice.DateInvoiced,
    Invoice.DateAcct,
    Invoice.GrandTotal,
    Invoice.VA009_PaymentMethod_ID
FROM C_Invoice Invoice
WHERE Invoice.IsActive = 'Y'
AND Invoice.AD_Client_ID = " + clientIdSql + @"
AND Invoice.IsSOTrx = 'N'
AND Invoice.DocStatus IN ('CO', 'CL')
AND EXISTS
(
    SELECT 1
    FROM C_DocType InvoiceDocumentType
    WHERE InvoiceDocumentType.C_DocType_ID = Invoice.C_DocType_ID
    AND InvoiceDocumentType.IsActive = 'Y'
    AND InvoiceDocumentType.DocBaseType = 'API'
    AND InvoiceDocumentType.IsSOTrx = 'N'
)
AND Invoice.C_Currency_ID = " + currencyIdSql + @"
AND COALESCE
(
    Invoice.VA009_PaymentMethod_ID,
    0
) = " + paymentMethodIdSql;

                invoiceAccessSql = MRole.GetDefault(ctx).AddAccessSQL(
                    invoiceAccessSql,
                    "Invoice",
                    MRole.SQL_FULLYQUALIFIED,
                    MRole.SQL_RO
                );

                string sql = @"
WITH SecuredInvoice AS
(
" + invoiceAccessSql + @"
),
InvoiceAllocation AS
(
    SELECT
        AllocationLine.C_Invoice_ID,

        SUM
        (
            COALESCE
            (
                AllocationLine.Amount,
                0
            )
            +
            COALESCE
            (
                AllocationLine.DiscountAmt,
                0
            )
            +
            COALESCE
            (
                AllocationLine.WriteOffAmt,
                0
            )
        ) AS AllocatedAmt

    FROM C_AllocationLine AllocationLine

    WHERE AllocationLine.IsActive = 'Y'

    AND AllocationLine.C_Invoice_ID IS NOT NULL

    GROUP BY
        AllocationLine.C_Invoice_ID
)
SELECT
    Invoice.C_Invoice_ID,

    Invoice.DocumentNo,

    Invoice.AD_Org_ID,

    Organization.Name AS OrganizationName,

    Invoice.C_BPartner_ID,

    Invoice.C_BPartner_Location_ID,

    InvoicePaySchedule.C_InvoicePaySchedule_ID,

    BusinessPartner.Name AS VendorName,

    BusinessPartner.IsVendor,

    Invoice.C_Currency_ID,

    Currency.ISO_Code AS CurrencyISO,

    Currency.CurSymbol AS CurrencySymbol,

    Currency.StdPrecision,

    Invoice.C_ConversionType_ID,

    ConversionType.Name AS CurrencyTypeName,

    Invoice.VA009_PaymentMethod_ID,

    PaymentMethod.VA009_Name AS PaymentMethodName,

    Invoice.DateInvoiced,

    Invoice.DateAcct,

    COALESCE
    (
        InvoicePaySchedule.DueDate,
        Invoice.DateAcct
    ) AS DueDate,

    Invoice.GrandTotal,

    CASE
        WHEN
        (
            Invoice.GrandTotal -
            COALESCE
            (
                InvoiceAllocation.AllocatedAmt,
                0
            )
        ) <= 0
        THEN 0

        WHEN InvoicePaySchedule.C_InvoicePaySchedule_ID
             IS NOT NULL

        AND COALESCE
        (
            InvoicePaySchedule.DueAmt,
            0
        ) > 0

        AND InvoicePaySchedule.DueAmt <
        (
            Invoice.GrandTotal -
            COALESCE
            (
                InvoiceAllocation.AllocatedAmt,
                0
            )
        )
        THEN InvoicePaySchedule.DueAmt

        ELSE
        (
            Invoice.GrandTotal -
            COALESCE
            (
                InvoiceAllocation.AllocatedAmt,
                0
            )
        )
    END AS OpenAmount

FROM SecuredInvoice Invoice

INNER JOIN C_BPartner BusinessPartner ON
(
    BusinessPartner.C_BPartner_ID =
    Invoice.C_BPartner_ID
)

LEFT OUTER JOIN AD_Org Organization ON
(
    Organization.AD_Org_ID =
    Invoice.AD_Org_ID
)

INNER JOIN C_InvoicePaySchedule InvoicePaySchedule ON
(
    InvoicePaySchedule.C_Invoice_ID =
    Invoice.C_Invoice_ID

    AND InvoicePaySchedule.IsActive = 'Y'

    AND COALESCE
    (
        InvoicePaySchedule.DueAmt,
        0
    ) > 0

    AND NOT EXISTS
    (
        SELECT 1

        FROM C_Payment ExistingPayment

        WHERE ExistingPayment.IsActive = 'Y'

        AND ExistingPayment.C_Invoice_ID =
        Invoice.C_Invoice_ID

        AND ExistingPayment.C_InvoicePaySchedule_ID =
        InvoicePaySchedule.C_InvoicePaySchedule_ID

        AND ExistingPayment.DocStatus NOT IN
        (
            'VO',
            'RE'
        )
    )
)

LEFT OUTER JOIN InvoiceAllocation InvoiceAllocation ON
(
    InvoiceAllocation.C_Invoice_ID =
    Invoice.C_Invoice_ID
)

LEFT OUTER JOIN C_Currency Currency ON
(
    Currency.C_Currency_ID =
    Invoice.C_Currency_ID
)

LEFT OUTER JOIN C_ConversionType ConversionType ON
(
    ConversionType.C_ConversionType_ID =
    Invoice.C_ConversionType_ID
)

LEFT OUTER JOIN VA009_PaymentMethod PaymentMethod ON
(
    PaymentMethod.VA009_PaymentMethod_ID =
    Invoice.VA009_PaymentMethod_ID
)

WHERE BusinessPartner.IsActive = 'Y'

AND BusinessPartner.IsVendor = 'Y'
"
+ GetDateFilter(
                    @"COALESCE
(
    InvoicePaySchedule.DueDate,
    Invoice.DateAcct
)",
                    dateFrom,
                    dateTo
                ) + @"

AND
(
    Invoice.GrandTotal -
    COALESCE
    (
        InvoiceAllocation.AllocatedAmt,
        0
    )
) > 0

ORDER BY
    DueDate,
    Invoice.DocumentNo,
    Invoice.C_Invoice_ID";

                reader = DB.ExecuteReader(
                    sql,
                    null,
                    null
                );

                List<object> rows = new List<object>();

                while (
                    reader != null &&
                    reader.Read()
                )
                {
                    DateTime? dueDate = GetNullableDateTime(
                        reader,
                        "DueDate"
                    );

                    DateTime? dateInvoiced = GetNullableDateTime(
                        reader,
                        "DateInvoiced"
                    );

                    string currencyISO = GetString(
                        reader,
                        "CurrencyISO"
                    );

                    string currencySymbol = GetString(
                        reader,
                        "CurrencySymbol"
                    );

                    if (string.IsNullOrWhiteSpace(currencySymbol))
                    {
                        currencySymbol = currencyISO;
                    }

                    int invoiceId = GetInt(
                        reader,
                        "C_Invoice_ID"
                    );

                    int vendorId = GetInt(
                        reader,
                        "C_BPartner_ID"
                    );

                    decimal openAmount = GetDecimal(
                        reader,
                        "OpenAmount"
                    );

                    rows.Add(new
                    {
                        invoiceId = invoiceId,

                        sourceInvoiceId = invoiceId,

                        cInvoiceId = invoiceId,

                        invoiceDocumentNo = GetString(
                            reader,
                            "DocumentNo"
                        ),

                        documentNo = GetString(
                            reader,
                            "DocumentNo"
                        ),

                        organizationId = GetInt(
                            reader,
                            "AD_Org_ID"
                        ),

                        adOrgId = GetInt(
                            reader,
                            "AD_Org_ID"
                        ),

                        organizationName = GetString(
                            reader,
                            "OrganizationName"
                        ),

                        vendorId = vendorId,

                        cBPartnerId = vendorId,

                        bPartnerLocationId = GetInt(
                            reader,
                            "C_BPartner_Location_ID"
                        ),

                        cBPartnerLocationId = GetInt(
                            reader,
                            "C_BPartner_Location_ID"
                        ),

                        invoicePayScheduleId = GetInt(
                            reader,
                            "C_InvoicePaySchedule_ID"
                        ),

                        cInvoicePayScheduleId = GetInt(
                            reader,
                            "C_InvoicePaySchedule_ID"
                        ),

                        vendorName = GetString(
                            reader,
                            "VendorName"
                        ),

                        isVendor = string.Equals(
                            GetString(
                                reader,
                                "IsVendor"
                            ),
                            "Y",
                            StringComparison.OrdinalIgnoreCase
                        ),

                        cCurrencyId = GetInt(
                            reader,
                            "C_Currency_ID"
                        ),

                        currencyId = GetInt(
                            reader,
                            "C_Currency_ID"
                        ),

                        currencyISO = currencyISO,

                        currencySymbol = currencySymbol,

                        stdPrecision = NormalizePrecision(
                            GetInt(
                                reader,
                                "StdPrecision",
                                2
                            )
                        ),

                        conversionTypeId = GetInt(
                            reader,
                            "C_ConversionType_ID"
                        ),

                        cConversionTypeId = GetInt(
                            reader,
                            "C_ConversionType_ID"
                        ),

                        currencyTypeName = GetString(
                            reader,
                            "CurrencyTypeName"
                        ),

                        dateInvoiced = FormatDate(
                            dateInvoiced
                        ),

                        dueDate = FormatDate(
                            dueDate
                        ),

                        transactionDate = FormatDate(
                            dueDate
                        ),

                        dateTrx = FormatDate(
                            dueDate
                        ),

                        grandTotal = GetDecimal(
                            reader,
                            "GrandTotal"
                        ),

                        openAmount = openAmount,

                        amount = openAmount,

                        payAmt = openAmount,

                        paymentMethodId = GetInt(
                            reader,
                            "VA009_PaymentMethod_ID"
                        ),

                        paymentMethodName = GetPaymentMethodName(
                            ctx,
                            GetString(
                                reader,
                                "PaymentMethodName"
                            )
                        ),

                        /*
                         * These values are selected from popup lookups.
                         */
                        bankAccountId = 0,
                        docTypeId = 0,
                        cDocTypeId = 0,
                        tenderType = string.Empty,
                        paymentDocumentNo = string.Empty
                    });
                }

                return Json(new
                {
                    success = true,
                    error = string.Empty,
                    rows = rows
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                VLogger.Get().SaveError(
                    "VAS_031_GetUpcomingAPRunDetails",
                    ex
                );

                string message = GetMsg(
                    ctx,
                    "VAS_ErrorLoading",
                    "Could not load invoice details."
                );

                return Json(new
                {
                    success = false,
                    error = message,
                    errorText = message
                }, JsonRequestBehavior.AllowGet);
            }
            finally
            {
                CloseReader(reader);
            }
        }

        #endregion

        #region Popup Lookups

        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetPaymentPopupLookups()
        {
            Ctx ctx = GetContext();

            if (ctx == null)
            {
                return GetSessionExpiredResult();
            }

            string currentLookup = string.Empty;

            try
            {
                int clientId = ctx.GetAD_Client_ID();

                string clientIdSql = clientId.ToString(
                    CultureInfo.InvariantCulture
                );

                currentLookup = "organizations";

                List<object> organizations = ReadLookupRows(@"
SELECT
    Organization.AD_Org_ID AS ID,
    Organization.Name AS Name

FROM AD_Org Organization

WHERE Organization.IsActive = 'Y'

AND Organization.AD_Client_ID = " + clientIdSql + @"

AND Organization.AD_Org_ID > 0

ORDER BY
    Organization.Name");

                currentLookup = "bankAccounts";

                List<object> bankAccounts =
                    ReadBankAccountLookupRows(
                        clientId
                    );

                currentLookup = "vendors";

                List<object> vendors = ReadLookupRows(@"
SELECT
    BusinessPartner.C_BPartner_ID AS ID,
    BusinessPartner.Name AS Name

FROM C_BPartner BusinessPartner

WHERE BusinessPartner.IsActive = 'Y'

AND BusinessPartner.IsVendor = 'Y'

AND BusinessPartner.IsSummary = 'N'

AND BusinessPartner.AD_Client_ID = " + clientIdSql + @"

ORDER BY
    BusinessPartner.Name");

                currentLookup = "currencies";

                List<object> currencies = ReadLookupRows(@"
SELECT
    Currency.C_Currency_ID AS ID,
    Currency.ISO_Code AS Name

FROM C_Currency Currency

WHERE Currency.IsActive = 'Y'

ORDER BY
    Currency.ISO_Code");

                currentLookup = "conversionTypes";

                List<object> conversionTypes = ReadLookupRows(@"
SELECT
    ConversionType.C_ConversionType_ID AS ID,
    ConversionType.Name AS Name

FROM C_ConversionType ConversionType

WHERE ConversionType.IsActive = 'Y'

AND ConversionType.AD_Client_ID IN
(
    0,
    " + clientIdSql + @"
)

ORDER BY
    ConversionType.Name");

                currentLookup = "documentTypes";

                List<object> documentTypes =
                    ReadDocTypeLookupRows(
                        ctx
                    );

                currentLookup = "tenderTypes";

                List<object> tenderTypes =
                    ReadTenderTypeLookupRows(
                        ctx
                    );

                currentLookup = "paymentMethods";

                List<object> paymentMethods =
                    ReadPaymentMethodLookupRows(
                        ctx
                    );

                return Json(new
                {
                    success = true,
                    error = string.Empty,

                    organizations = organizations,
                    bankAccounts = bankAccounts,
                    vendors = vendors,
                    currencies = currencies,
                    conversionTypes = conversionTypes,
                    documentTypes = documentTypes,
                    docTypes = documentTypes,
                    tenderTypes = tenderTypes,
                    paymentMethods = paymentMethods
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                VLogger.Get().SaveError(
                    "VAS_031_GetPaymentPopupLookups_" +
                    currentLookup,
                    ex
                );

                string message = GetMsg(
                    ctx,
                    "VAS_ErrorLoading",
                    "Could not load lookup data."
                );

                return Json(new
                {
                    success = false,
                    error = message,
                    errorText = message,
                    lookup = currentLookup
                }, JsonRequestBehavior.AllowGet);
            }
        }

        private List<object> ReadLookupRows(
            string sql)
        {
            List<object> rows = new List<object>();

            IDataReader reader = null;

            try
            {
                reader = DB.ExecuteReader(
                    sql,
                    null,
                    null
                );

                while (
                    reader != null &&
                    reader.Read()
                )
                {
                    int id = GetInt(
                        reader,
                        "ID"
                    );

                    string name = GetString(
                        reader,
                        "Name"
                    );

                    rows.Add(new
                    {
                        id = id,
                        value = id,
                        name = name,
                        text = name,
                        label = name
                    });
                }
            }
            finally
            {
                CloseReader(reader);
            }

            return rows;
        }

        private List<object> ReadBankAccountLookupRows(
            int clientId)
        {
            List<object> rows = new List<object>();

            IDataReader reader = null;

            string sql = @"
SELECT
    BankAccount.C_BankAccount_ID AS ID,

    Bank.Name AS BankName,

    BankAccount.Name AS BankAccountName,

    BankAccount.AccountNo

FROM C_BankAccount BankAccount

LEFT OUTER JOIN C_Bank Bank ON
(
    Bank.C_Bank_ID =
    BankAccount.C_Bank_ID
)

WHERE BankAccount.IsActive = 'Y'

AND BankAccount.AD_Client_ID = " +
                clientId.ToString(
                    CultureInfo.InvariantCulture
                ) + @"

ORDER BY
    BankAccount.Name";

            try
            {
                reader = DB.ExecuteReader(
                    sql,
                    null,
                    null
                );

                while (
                    reader != null &&
                    reader.Read()
                )
                {
                    int id = GetInt(
                        reader,
                        "ID"
                    );

                    string bankName = GetString(
                        reader,
                        "BankName"
                    );

                    string accountName = GetString(
                        reader,
                        "BankAccountName"
                    );

                    string accountNo = GetString(
                        reader,
                        "AccountNo"
                    );

                    string displayName = FirstNotEmpty(
                        bankName,
                        accountName,
                        accountNo
                    );

                    if (
                        !string.IsNullOrWhiteSpace(accountNo) &&
                        displayName != accountNo
                    )
                    {
                        displayName =
                            displayName +
                            " · " +
                            accountNo;
                    }

                    rows.Add(new
                    {
                        id = id,
                        value = id,
                        name = displayName,
                        text = displayName,
                        label = displayName,

                        bankName = bankName,
                        bankAccountName = accountName,
                        accountNo = accountNo
                    });
                }
            }
            finally
            {
                CloseReader(reader);
            }

            return rows;
        }

        private List<object> ReadDocTypeLookupRows(
            Ctx ctx)
        {
            int clientId = ctx.GetAD_Client_ID();

            string clientIdSql = clientId.ToString(
                CultureInfo.InvariantCulture
            );

            string sql = @"
SELECT
    DocumentType.C_DocType_ID AS ID,

    DocumentType.Name AS Name

FROM C_DocType DocumentType

WHERE DocumentType.IsActive = 'Y'

AND DocumentType.DocBaseType = 'APP'

AND DocumentType.IsSOTrx = 'N'

AND DocumentType.AD_Client_ID IN
(
    0,
    " + clientIdSql + @"
)

ORDER BY
    DocumentType.Name";

            return ReadLookupRows(
                sql
            );
        }

        private List<object> ReadTenderTypeLookupRows(
            Ctx ctx)
        {
            List<object> rows = new List<object>();

            IDataReader reader = null;

            string languageSql = ToSqlString(
                ctx.GetAD_Language()
            );

            string valueSql = GetTextCastSql(
                "RefList.Value"
            );

            string baseNameSql = GetTextCastSql(
                "RefList.Name"
            );

            string translatedNameSql = GetTextCastSql(
                "RefListTrl.Name"
            );

            string sql = @"
SELECT
    " + valueSql + @" AS TenderValue,

    " + baseNameSql + @" AS BaseName,

    " + translatedNameSql + @" AS TranslatedName

FROM AD_Table TableInfo

INNER JOIN AD_Column ColumnInfo ON
(
    ColumnInfo.AD_Table_ID =
    TableInfo.AD_Table_ID
)

INNER JOIN AD_Reference ReferenceInfo ON
(
    ReferenceInfo.AD_Reference_ID =
    ColumnInfo.AD_Reference_Value_ID
)

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

WHERE TableInfo.TableName = 'C_Payment'

AND ColumnInfo.ColumnName = 'TenderType'

AND TableInfo.IsActive = 'Y'

AND ColumnInfo.IsActive = 'Y'

AND ReferenceInfo.IsActive = 'Y'

AND RefList.IsActive = 'Y'

ORDER BY
    RefList.Name";

            try
            {
                reader = DB.ExecuteReader(
                    sql,
                    null,
                    null
                );

                while (
                    reader != null &&
                    reader.Read()
                )
                {
                    string value = GetString(
                        reader,
                        "TenderValue"
                    );

                    string displayName = FirstNotEmpty(
                        GetString(
                            reader,
                            "TranslatedName"
                        ),

                        GetString(
                            reader,
                            "BaseName"
                        ),

                        value
                    );

                    rows.Add(new
                    {
                        id = value,
                        value = value,
                        code = value,
                        name = displayName,
                        text = displayName,
                        label = displayName
                    });
                }
            }
            finally
            {
                CloseReader(reader);
            }

            return rows;
        }

        private List<object> ReadPaymentMethodLookupRows(
            Ctx ctx)
        {
            List<object> rows = new List<object>();
            IDataReader reader = null;

            string clientIdSql = ctx.GetAD_Client_ID().ToString(
                CultureInfo.InvariantCulture
            );

            string sql = @"
SELECT
    PaymentMethod.VA009_PaymentMethod_ID AS ID,
    PaymentMethod.VA009_Name AS Name
FROM VA009_PaymentMethod PaymentMethod
WHERE PaymentMethod.IsActive = 'Y'
AND PaymentMethod.AD_Client_ID IN
(
    0,
    " + clientIdSql + @"
)
ORDER BY
    PaymentMethod.VA009_Name";

            try
            {
                reader = DB.ExecuteReader(
                    sql,
                    null,
                    null
                );

                while (
                    reader != null &&
                    reader.Read()
                )
                {
                    int id = GetInt(reader, "ID");
                    string name = GetString(reader, "Name");

                    rows.Add(new
                    {
                        id = id,
                        value = id,
                        name = name,
                        text = name,
                        label = name,
                        isCheck = IsCheckPaymentMethod(name)
                    });
                }
            }
            finally
            {
                CloseReader(reader);
            }

            return rows;
        }

        private bool IsCheckPaymentMethod(
            string paymentMethodName)
        {
            string value = (paymentMethodName ?? string.Empty)
                .Trim()
                .ToLowerInvariant();

            return
                value.Contains("cheque") ||
                value.Contains("check") ||
                value.Contains("chq") ||
                value.Contains("صك");
        }

        #endregion

        #region Create AP Payment From Invoice

        [HttpPost]
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult CreateUpcomingAPPayment(
            int invoiceId,
            int invoicePayScheduleId,
            int adOrgId,
            int bankAccountId,
            int vendorId,
            int currencyId,
            int conversionTypeId,
            int docTypeId,
            string tenderType,
            int paymentMethodId,
            string checkNo,
            string checkDate,
            string transactionDate,
            string documentNo,
            decimal payAmt)
        {
            Ctx ctx = GetContext();

            if (ctx == null)
            {
                return Json(new
                {
                    success = false,
                    error = "Session Expired",
                    errorText = "Session Expired"
                });
            }

            if (invoiceId <= 0)
            {
                return GetValidationError(
                    ctx,
                    "VAS_031_MessageSourceInvoiceRequired",
                    "Source invoice is required."
                );
            }

            if (invoicePayScheduleId <= 0)
            {
                return GetValidationError(
                    ctx,
                    "VAS_031_MessageInvoicePayScheduleRequired",
                    "Invoice payment schedule is required."
                );
            }

            if (paymentMethodId <= 0)
            {
                return GetValidationError(
                    ctx,
                    "VAS_031_MessagePaymentMethodRequired",
                    "Payment method is required."
                );
            }

            JsonResult validationResult = ValidateCreateRequest(
                ctx,
                adOrgId,
                bankAccountId,
                vendorId,
                currencyId,
                conversionTypeId,
                docTypeId,
                tenderType,
                transactionDate,
                payAmt
            );

            if (validationResult != null)
            {
                return validationResult;
            }

            DateTime dateTrx;

            if (!DateTime.TryParseExact(
                transactionDate,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out dateTrx))
            {
                return GetValidationError(
                    ctx,
                    "VAS_031_MessageTransactionDateInvalid",
                    "Transaction date must be in yyyy-MM-dd format."
                );
            }

            string trxName =
                "VAS031_CreateInvoicePayment_" +
                ctx.GetAD_User_ID() +
                "_" +
                DateTime.Now.Ticks;

            Trx trx = Trx.GetTrx(trxName);

            try
            {
                /*
                 * Read and validate source invoice.
                 */
                MInvoice invoice = new MInvoice(
                    ctx,
                    invoiceId,
                    trx
                );

                if (invoice.GetC_Invoice_ID() <= 0)
                {
                    throw new InvalidOperationException(
                        GetMsg(
                            ctx,
                            "VAS_031_MessageSourceInvoiceNotFound",
                            "The source invoice was not found."
                        )
                    );
                }

                if (invoice.GetAD_Client_ID() != ctx.GetAD_Client_ID())
                {
                    throw new InvalidOperationException(
                        GetMsg(
                            ctx,
                            "VAS_031_MessageInvalidSourceInvoice",
                            "The invoice does not belong to the current client."
                        )
                    );
                }

                if (invoice.IsSOTrx())
                {
                    throw new InvalidOperationException(
                        GetMsg(
                            ctx,
                            "VAS_031_MessageInvalidAPInvoice",
                            "The selected invoice is not an AP invoice."
                        )
                    );
                }

                string invoiceDocStatus = invoice.GetDocStatus();

                if (!string.Equals(
                        invoiceDocStatus,
                        "CO",
                        StringComparison.OrdinalIgnoreCase
                    ) &&
                    !string.Equals(
                        invoiceDocStatus,
                        "CL",
                        StringComparison.OrdinalIgnoreCase
                    ))
                {
                    throw new InvalidOperationException(
                        GetMsg(
                            ctx,
                            "VAS_031_MessageInvoiceNotCompleted",
                            "The selected invoice is not completed."
                        )
                    );
                }

                int invoiceDocTypeId = invoice.GetC_DocType_ID();

                if (invoiceDocTypeId <= 0)
                {
                    invoiceDocTypeId = invoice.GetC_DocTypeTarget_ID();
                }

                string invoiceDocBaseType = GetInvoiceDocBaseType(
                    invoiceDocTypeId,
                    trx
                );

                if (!string.Equals(
                    invoiceDocBaseType,
                    "API",
                    StringComparison.OrdinalIgnoreCase
                ))
                {
                    throw new InvalidOperationException(
                        "The selected document is not a normal AP Invoice. " +
                        "Invoice DocBaseType must be API, but it is " +
                        invoiceDocBaseType + "."
                    );
                }

                /*
                 * Prevent changing invoice-related information
                 * from the UI.
                 */
                if (invoice.GetAD_Org_ID() != adOrgId)
                {
                    throw new InvalidOperationException(
                        "Selected organization does not match the invoice."
                    );
                }

                if (invoice.GetC_BPartner_ID() != vendorId)
                {
                    throw new InvalidOperationException(
                        "Selected vendor does not match the invoice."
                    );
                }

                if (invoice.GetC_Currency_ID() != currencyId)
                {
                    throw new InvalidOperationException(
                        "Selected currency does not match the invoice."
                    );
                }

                int validatedInvoicePayScheduleId =
                    ValidateInvoicePaySchedule(
                        invoiceId,
                        invoicePayScheduleId,
                        trx
                    );

                if (validatedInvoicePayScheduleId <= 0)
                {
                    throw new InvalidOperationException(
                        "The selected invoice payment schedule is invalid, inactive, " +
                        "or does not belong to the selected invoice."
                    );
                }

                if (HasExistingPaymentForSchedule(
                    invoiceId,
                    validatedInvoicePayScheduleId,
                    trx
                ))
                {
                    throw new InvalidOperationException(
                        "Invoice schedule already has an active payment."
                    );
                }

                /*
                 * Read the current invoice open amount before creating payment.
                 */
                decimal openAmountBeforePayment = GetInvoiceOpenAmount(
                    invoiceId,
                    trx
                );

                if (openAmountBeforePayment <= 0)
                {
                    throw new InvalidOperationException(
                        GetMsg(
                            ctx,
                            "VAS_031_MessageInvoiceAlreadyPaid",
                            "The invoice has no open amount."
                        )
                    );
                }

                if (payAmt > openAmountBeforePayment)
                {
                    throw new InvalidOperationException(
                        GetMsg(
                            ctx,
                            "VAS_031_MessagePaymentExceedsOpenAmount",
                            "Payment amount exceeds the invoice open amount."
                        )
                    );
                }

                /*
                 * Validate organization.
                 */
                MOrg organization = new MOrg(
                    ctx,
                    adOrgId,
                    trx
                );

                if (organization.GetAD_Org_ID() <= 0 ||
                    !organization.IsActive())
                {
                    throw new InvalidOperationException(
                        "Organization was not found or is inactive."
                    );
                }

                /*
                 * Validate bank account.
                 */
                MBankAccount bankAccount = new MBankAccount(
                    ctx,
                    bankAccountId,
                    trx
                );

                if (bankAccount.GetC_BankAccount_ID() <= 0 ||
                    !bankAccount.IsActive())
                {
                    throw new InvalidOperationException(
                        "Bank account was not found or is inactive."
                    );
                }

                if (bankAccount.GetAD_Client_ID() != ctx.GetAD_Client_ID())
                {
                    throw new InvalidOperationException(
                        "Bank account does not belong to the current client."
                    );
                }

                /*
                 * Validate vendor.
                 */
                MBPartner vendor = new MBPartner(
                    ctx,
                    vendorId,
                    trx
                );

                if (vendor.GetC_BPartner_ID() <= 0 ||
                    !vendor.IsActive() ||
                    !vendor.IsVendor())
                {
                    throw new InvalidOperationException(
                        "Selected business partner is not an active vendor."
                    );
                }

                int bPartnerLocationId = ResolveBPartnerLocationId(
                    invoice,
                    vendorId,
                    trx
                );

                if (bPartnerLocationId <= 0)
                {
                    throw new InvalidOperationException(
                        "No active business partner location was found for the selected vendor."
                    );
                }

                /*
                 * Validate currency.
                 */
                MCurrency currency = new MCurrency(
                    ctx,
                    currencyId,
                    trx
                );

                if (currency.GetC_Currency_ID() <= 0 ||
                    !currency.IsActive())
                {
                    throw new InvalidOperationException(
                        "Currency was not found or is inactive."
                    );
                }

                /*
                 * Validate conversion type.
                 */
                MConversionType conversionType = new MConversionType(
                    ctx,
                    conversionTypeId,
                    trx
                );

                if (conversionType.GetC_ConversionType_ID() <= 0 ||
                    !conversionType.IsActive())
                {
                    throw new InvalidOperationException(
                        "Conversion type was not found or is inactive."
                    );
                }

                /*
                 * Resolve AP Payment document type.
                 */
                int resolvedDocTypeId = ResolveAPPaymentDocTypeId(
                    ctx,
                    docTypeId,
                    adOrgId,
                    trx
                );

                if (resolvedDocTypeId <= 0)
                {
                    throw new InvalidOperationException(
                        "AP Payment document type was not found."
                    );
                }

                MDocType documentType = new MDocType(
                    ctx,
                    resolvedDocTypeId,
                    trx
                );

                if (documentType.GetC_DocType_ID() <= 0 ||
                    !documentType.IsActive() ||
                    !string.Equals(
                        documentType.GetDocBaseType(),
                        "APP",
                        StringComparison.OrdinalIgnoreCase
                    ))
                {
                    throw new InvalidOperationException(
                        "The selected document type is not an AP Payment document type."
                    );
                }

                /*
                 * Validate tender type.
                 */
                string normalizedTenderType =
                    (tenderType ?? string.Empty).Trim();

                if (!IsValidTenderType(
                    ctx,
                    normalizedTenderType
                ))
                {
                    throw new InvalidOperationException(
                        "The selected tender type is not valid."
                    );
                }

                /*
                 * Validate payment method.
                 */
                string paymentMethodName = Util.GetValueOfString(
                    DB.ExecuteScalar(
                        @"
SELECT
    PaymentMethod.VA009_Name

FROM VA009_PaymentMethod PaymentMethod

WHERE PaymentMethod.IsActive = 'Y'

AND PaymentMethod.VA009_PaymentMethod_ID = " +
                        paymentMethodId.ToString(
                            CultureInfo.InvariantCulture
                        ) + @"

AND PaymentMethod.AD_Client_ID IN
(
    0,
    " +
                        ctx.GetAD_Client_ID().ToString(
                            CultureInfo.InvariantCulture
                        ) +
                        @"
)",
                        null,
                        trx
                    )
                );

                if (string.IsNullOrWhiteSpace(paymentMethodName))
                {
                    throw new InvalidOperationException(
                        GetMsg(
                            ctx,
                            "VAS_031_MessageInvalidPaymentMethod",
                            "The selected payment method is invalid or inactive."
                        )
                    );
                }

                bool isCheckPayment = IsCheckPaymentMethod(
                    paymentMethodName
                );

                DateTime parsedCheckDate = DateTime.MinValue;

                if (isCheckPayment)
                {
                    if (string.IsNullOrWhiteSpace(checkNo))
                    {
                        throw new InvalidOperationException(
                            GetMsg(
                                ctx,
                                "VAS_031_MessageCheckNoRequired",
                                "Check number is required."
                            )
                        );
                    }

                    if (string.IsNullOrWhiteSpace(checkDate) ||
                        !DateTime.TryParseExact(
                            checkDate,
                            "yyyy-MM-dd",
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.None,
                            out parsedCheckDate
                        ))
                    {
                        throw new InvalidOperationException(
                            GetMsg(
                                ctx,
                                "VAS_031_MessageCheckDateInvalid",
                                "Check date is required and must be in yyyy-MM-dd format."
                            )
                        );
                    }
                }

                /*
                 * Create AP payment.
                 */
                MPayment payment = new MPayment(
                    ctx,
                    0,
                    trx
                );

                payment.SetAD_Org_ID(
                    invoice.GetAD_Org_ID()
                );

                payment.SetIsReceipt(false);

                payment.SetC_DocType_ID(
                    resolvedDocTypeId
                );

                payment.SetC_BankAccount_ID(
                    bankAccountId
                );

                payment.SetC_BPartner_ID(
                    invoice.GetC_BPartner_ID()
                );

                payment.SetC_BPartner_Location_ID(
                    bPartnerLocationId
                );

                payment.SetC_Currency_ID(
                    invoice.GetC_Currency_ID()
                );

                payment.SetC_ConversionType_ID(
                    conversionTypeId
                );

                /*
                 * Linking C_Invoice_ID is necessary so the completion
                 * process can allocate the payment against this invoice.
                 */
                payment.SetC_Invoice_ID(
                    invoice.GetC_Invoice_ID()
                );

                payment.Set_Value(
                    "C_InvoicePaySchedule_ID",
                    validatedInvoicePayScheduleId
                );

                payment.SetDateTrx(dateTrx);
                payment.SetDateAcct(dateTrx);

                payment.SetTenderType(
                    normalizedTenderType
                );

                payment.Set_Value(
                    "VA009_PaymentMethod_ID",
                    paymentMethodId
                );

                if (isCheckPayment)
                {
                    payment.Set_Value(
                        "CheckNo",
                        checkNo.Trim()
                    );

                    payment.Set_Value(
                        "CheckDate",
                        parsedCheckDate
                    );
                }

                payment.SetPayAmt(payAmt);

                /*
                 * Initially save as Draft.
                 */
                payment.SetDocStatus("DR");
                payment.SetDocAction("CO");
                payment.SetProcessed(false);

                if (!string.IsNullOrWhiteSpace(documentNo))
                {
                    payment.SetDocumentNo(
                        documentNo.Trim()
                    );
                }

                if (!payment.Save())
                {
                    throw new InvalidOperationException(
                        GetLastModelError(
                            ctx,
                            "VAS_031_MessageCouldNotSaveAPPayment",
                            "Could not save AP payment."
                        )
                    );
                }

                /*
                 * Important:
                 * Complete the payment so Vienna Advantage creates
                 * the accounting/allocation records.
                 */
                bool completed = payment.ProcessIt("CO");

                if (!completed)
                {
                    string processMessage = payment.GetProcessMsg();

                    if (string.IsNullOrWhiteSpace(processMessage))
                    {
                        processMessage = GetMsg(
                            ctx,
                            "VAS_031_MessageCouldNotCompleteAPPayment",
                            "Could not complete AP payment."
                        );
                    }

                    throw new InvalidOperationException(
                        processMessage
                    );
                }

                /*
                 * Save changes made by ProcessIt.
                 */
                if (!payment.Save())
                {
                    throw new InvalidOperationException(
                        GetLastModelError(
                            ctx,
                            "VAS_031_MessageCouldNotCompleteAPPayment",
                            "Could not save the completed AP payment."
                        )
                    );
                }

                /*
                 * Confirm that payment is really completed.
                 */
                string completedDocStatus = payment.GetDocStatus();

                bool isCompleted =
                    string.Equals(
                        completedDocStatus,
                        "CO",
                        StringComparison.OrdinalIgnoreCase
                    ) ||
                    string.Equals(
                        completedDocStatus,
                        "CL",
                        StringComparison.OrdinalIgnoreCase
                    );

                if (!isCompleted)
                {
                    string processMessage = payment.GetProcessMsg();

                    if (string.IsNullOrWhiteSpace(processMessage))
                    {
                        processMessage =
                            "Payment was saved but was not completed. " +
                            "Current status: " +
                            completedDocStatus;
                    }

                    throw new InvalidOperationException(
                        processMessage
                    );
                }

                /*
                 * Recalculate invoice open amount in the same transaction.
                 *
                 * Full payment:
                 * remainingOpenAmount = 0
                 * invoice must disappear from widget.
                 *
                 * Partial payment:
                 * remainingOpenAmount > 0
                 * invoice remains with the remaining value.
                 */
                decimal remainingOpenAmount = GetInvoiceOpenAmount(
                    invoiceId,
                    trx
                );

                bool invoiceFullyPaid =
                    remainingOpenAmount <= 0;

                /*
                 * Commit payment and allocation together.
                 */
                trx.Commit();

                return Json(new
                {
                    success = true,
                    error = string.Empty,

                    invoiceId = invoice.GetC_Invoice_ID(),

                    invoiceDocumentNo =
                        invoice.GetDocumentNo(),

                    invoicePayScheduleId =
                        validatedInvoicePayScheduleId,

                    bPartnerLocationId =
                        bPartnerLocationId,

                    paymentId =
                        payment.GetC_Payment_ID(),

                    documentNo =
                        payment.GetDocumentNo(),

                    docStatus =
                        payment.GetDocStatus(),

                    docAction =
                        payment.GetDocAction(),

                    processed =
                        payment.IsProcessed(),

                    docTypeId =
                        resolvedDocTypeId,

                    docTypeName =
                        documentType.GetName(),

                    paidAmount =
                        payAmt,

                    openAmountBeforePayment =
                        openAmountBeforePayment,

                    remainingOpenAmount =
                        remainingOpenAmount,

                    invoiceFullyPaid =
                        invoiceFullyPaid,

                    removeInvoiceFromWidget =
                        invoiceFullyPaid,

                    refreshUpcomingAPRuns =
                        true,

                    message = GetMsg(
                        ctx,
                        "VAS_031_MessagePaymentCreatedSuccessfully",
                        "AP payment created and completed successfully."
                    )
                });
            }
            catch (InvalidOperationException ex)
            {
                RollbackTransaction(trx);

                return Json(new
                {
                    success = false,
                    error = ex.Message,
                    errorText = ex.Message
                });
            }
            catch (Exception ex)
            {
                RollbackTransaction(trx);

                VLogger.Get().SaveError(
                    "VAS_031_CreateUpcomingAPPayment",
                    ex
                );

                string message = GetMsg(
                    ctx,
                    "VAS_031_MessageCouldNotSaveAPPayment",
                    "Could not save or complete AP payment."
                );

                return Json(new
                {
                    success = false,
                    error = message,
                    errorText = message
                });
            }
            finally
            {
                if (trx != null)
                {
                    trx.Close();
                }
            }
        }

        #endregion


        #region Create AP Payment From Invoice



        private string GetInvoiceDocBaseType(
            int invoiceDocTypeId,
            Trx trx)
        {
            if (invoiceDocTypeId <= 0)
            {
                return string.Empty;
            }

            string sql = @"
SELECT
    DocumentType.DocBaseType
FROM C_DocType DocumentType
WHERE DocumentType.C_DocType_ID = " +
                invoiceDocTypeId.ToString(CultureInfo.InvariantCulture);

            return Util.GetValueOfString(
                DB.ExecuteScalar(sql, null, trx)
            );
        }

        private int ValidateInvoicePaySchedule(
            int invoiceId,
            int invoicePayScheduleId,
            Trx trx)
        {
            if (invoiceId <= 0 || invoicePayScheduleId <= 0)
            {
                return 0;
            }

            string sql = @"
SELECT
    InvoicePaySchedule.C_InvoicePaySchedule_ID
FROM C_InvoicePaySchedule InvoicePaySchedule
WHERE InvoicePaySchedule.IsActive = 'Y'
AND InvoicePaySchedule.C_Invoice_ID = " +
                invoiceId.ToString(CultureInfo.InvariantCulture) + @"
AND InvoicePaySchedule.C_InvoicePaySchedule_ID = " +
                invoicePayScheduleId.ToString(CultureInfo.InvariantCulture);

            return Util.GetValueOfInt(
                DB.ExecuteScalar(sql, null, trx)
            );
        }

        private bool HasExistingPaymentForSchedule(
            int invoiceId,
            int invoicePayScheduleId,
            Trx trx)
        {
            if (invoiceId <= 0 ||
                invoicePayScheduleId <= 0)
            {
                return true;
            }

            string sql = @"
SELECT
    COUNT(1)

FROM C_Payment ExistingPayment

WHERE ExistingPayment.IsActive = 'Y'

AND ExistingPayment.C_Invoice_ID = " +
                invoiceId.ToString(
                    CultureInfo.InvariantCulture
                ) + @"

AND ExistingPayment.C_InvoicePaySchedule_ID = " +
                invoicePayScheduleId.ToString(
                    CultureInfo.InvariantCulture
                ) + @"

AND ExistingPayment.DocStatus NOT IN
(
    'VO',
    'RE'
)";

            int paymentCount = Util.GetValueOfInt(
                DB.ExecuteScalar(
                    sql,
                    null,
                    trx
                )
            );

            return paymentCount > 0;
        }

        private int ResolveBPartnerLocationId(
            MInvoice invoice,
            int vendorId,
            Trx trx)
        {
            if (invoice == null || vendorId <= 0)
            {
                return 0;
            }

            int invoiceLocationId =
                invoice.GetC_BPartner_Location_ID();

            if (invoiceLocationId > 0)
            {
                string validationSql = @"
SELECT
    BPartnerLocation.C_BPartner_Location_ID
FROM C_BPartner_Location BPartnerLocation
WHERE BPartnerLocation.IsActive = 'Y'
AND BPartnerLocation.C_BPartner_Location_ID = " +
                    invoiceLocationId.ToString(CultureInfo.InvariantCulture) + @"
AND BPartnerLocation.C_BPartner_ID = " +
                    vendorId.ToString(CultureInfo.InvariantCulture);

                int validLocationId = Util.GetValueOfInt(
                    DB.ExecuteScalar(validationSql, null, trx)
                );

                if (validLocationId > 0)
                {
                    return validLocationId;
                }
            }

            string fallbackSql;

            if (DB.IsOracle())
            {
                fallbackSql = @"
SELECT LocationResult.C_BPartner_Location_ID
FROM
(
    SELECT BPartnerLocation.C_BPartner_Location_ID
    FROM C_BPartner_Location BPartnerLocation
    WHERE BPartnerLocation.IsActive = 'Y'
    AND BPartnerLocation.C_BPartner_ID = " +
                    vendorId.ToString(CultureInfo.InvariantCulture) + @"
    ORDER BY
        CASE WHEN BPartnerLocation.IsBillTo = 'Y' THEN 0 ELSE 1 END,
        BPartnerLocation.C_BPartner_Location_ID
) LocationResult
WHERE ROWNUM = 1";
            }
            else
            {
                fallbackSql = @"
SELECT BPartnerLocation.C_BPartner_Location_ID
FROM C_BPartner_Location BPartnerLocation
WHERE BPartnerLocation.IsActive = 'Y'
AND BPartnerLocation.C_BPartner_ID = " +
                    vendorId.ToString(CultureInfo.InvariantCulture) + @"
ORDER BY
    CASE WHEN BPartnerLocation.IsBillTo = 'Y' THEN 0 ELSE 1 END,
    BPartnerLocation.C_BPartner_Location_ID
LIMIT 1";
            }

            return Util.GetValueOfInt(
                DB.ExecuteScalar(fallbackSql, null, trx)
            );
        }

        private decimal GetInvoiceOpenAmount(
            int invoiceId,
            Trx trx)
        {
            string invoiceIdSql =
                invoiceId.ToString(
                    CultureInfo.InvariantCulture
                );

            string sql = @"
SELECT
    CASE
        WHEN
        (
            Invoice.GrandTotal -
            COALESCE
            (
                Allocation.AllocatedAmt,
                0
            )
        ) > 0
        THEN
        (
            Invoice.GrandTotal -
            COALESCE
            (
                Allocation.AllocatedAmt,
                0
            )
        )
        ELSE 0
    END AS OpenAmount

FROM C_Invoice Invoice

LEFT OUTER JOIN
(
    SELECT
        AllocationLine.C_Invoice_ID,

        SUM
        (
            COALESCE
            (
                AllocationLine.Amount,
                0
            )
            +
            COALESCE
            (
                AllocationLine.DiscountAmt,
                0
            )
            +
            COALESCE
            (
                AllocationLine.WriteOffAmt,
                0
            )
        ) AS AllocatedAmt

    FROM C_AllocationLine AllocationLine

    WHERE AllocationLine.IsActive = 'Y'

    AND AllocationLine.C_Invoice_ID =
        " + invoiceIdSql + @"

    GROUP BY
        AllocationLine.C_Invoice_ID
) Allocation ON
(
    Allocation.C_Invoice_ID =
    Invoice.C_Invoice_ID
)

WHERE Invoice.C_Invoice_ID =
    " + invoiceIdSql;

            IDataReader reader = null;

            try
            {
                reader = DB.ExecuteReader(
                    sql,
                    null,
                    trx
                );

                if (
                    reader != null &&
                    reader.Read()
                )
                {
                    return GetDecimal(
                        reader,
                        "OpenAmount"
                    );
                }
            }
            finally
            {
                CloseReader(reader);
            }

            return 0;
        }

        private JsonResult ValidateCreateRequest(
            Ctx ctx,
            int adOrgId,
            int bankAccountId,
            int vendorId,
            int currencyId,
            int conversionTypeId,
            int docTypeId,
            string tenderType,
            string transactionDate,
            decimal payAmt)
        {
            if (adOrgId <= 0)
            {
                return GetValidationError(
                    ctx,
                    "VAS_031_MessageOrganizationRequired",
                    "Organization is required."
                );
            }

            if (bankAccountId <= 0)
            {
                return GetValidationError(
                    ctx,
                    "VAS_031_MessageBankAccountRequired",
                    "Bank account is required."
                );
            }

            if (vendorId <= 0)
            {
                return GetValidationError(
                    ctx,
                    "VAS_031_MessageVendorRequired",
                    "Vendor is required."
                );
            }

            if (currencyId <= 0)
            {
                return GetValidationError(
                    ctx,
                    "VAS_031_MessageCurrencyRequired",
                    "Currency is required."
                );
            }

            if (conversionTypeId <= 0)
            {
                return GetValidationError(
                    ctx,
                    "VAS_031_MessageConversionTypeRequired",
                    "Currency type is required."
                );
            }

            if (docTypeId <= 0)
            {
                return GetValidationError(
                    ctx,
                    "VAS_031_MessageDocumentTypeRequired",
                    "Document type is required."
                );
            }

            if (string.IsNullOrWhiteSpace(tenderType))
            {
                return GetValidationError(
                    ctx,
                    "VAS_031_MessageTenderTypeRequired",
                    "Tender type is required."
                );
            }

            if (string.IsNullOrWhiteSpace(transactionDate))
            {
                return GetValidationError(
                    ctx,
                    "VAS_031_MessageTransactionDateRequired",
                    "Transaction date is required."
                );
            }

            if (payAmt <= 0)
            {
                return GetValidationError(
                    ctx,
                    "VAS_031_MessagePaymentAmountRequired",
                    "Payment amount must be greater than zero."
                );
            }

            DateTime parsedDate;

            if (!DateTime.TryParseExact(
                transactionDate,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out parsedDate))
            {
                return GetValidationError(
                    ctx,
                    "VAS_031_MessageTransactionDateInvalid",
                    "Transaction date must be in yyyy-MM-dd format."
                );
            }

            return null;
        }

        private JsonResult GetValidationError(
            Ctx ctx,
            string messageKey,
            string fallback)
        {
            string message = GetMsg(
                ctx,
                messageKey,
                fallback
            );

            return Json(new
            {
                success = false,
                error = message,
                errorText = message
            });
        }

        private int ResolveAPPaymentDocTypeId(
            Ctx ctx,
            int requestedDocTypeId,
            int adOrgId,
            Trx trx)
        {
            if (ctx == null ||
                requestedDocTypeId <= 0 ||
                adOrgId <= 0)
            {
                return 0;
            }

            string sql = @"
SELECT
    DocumentType.C_DocType_ID
FROM C_DocType DocumentType
WHERE DocumentType.C_DocType_ID = " +
                requestedDocTypeId.ToString(CultureInfo.InvariantCulture) + @"
AND DocumentType.IsActive = 'Y'
AND DocumentType.DocBaseType = 'APP'
AND DocumentType.IsSOTrx = 'N'
AND DocumentType.AD_Client_ID IN
(
    0,
    " + ctx.GetAD_Client_ID().ToString(CultureInfo.InvariantCulture) + @"
)
AND DocumentType.AD_Org_ID IN
(
    0,
    " + adOrgId.ToString(CultureInfo.InvariantCulture) + @"
)";

            return Util.GetValueOfInt(
                DB.ExecuteScalar(sql, null, trx)
            );
        }

        private bool IsValidTenderType(
            Ctx ctx,
            string tenderType)
        {
            if (string.IsNullOrWhiteSpace(
                tenderType
            ))
            {
                return false;
            }

            IDataReader reader = null;

            string tenderTypeSql = ToSqlString(
                tenderType.Trim()
            );

            string sql = @"
SELECT
    RefList.Value

FROM AD_Table TableInfo

INNER JOIN AD_Column ColumnInfo ON
(
    ColumnInfo.AD_Table_ID =
    TableInfo.AD_Table_ID
)

INNER JOIN AD_Reference ReferenceInfo ON
(
    ReferenceInfo.AD_Reference_ID =
    ColumnInfo.AD_Reference_Value_ID
)

INNER JOIN AD_Ref_List RefList ON
(
    RefList.AD_Reference_ID =
    ReferenceInfo.AD_Reference_ID
)

WHERE TableInfo.TableName = 'C_Payment'

AND ColumnInfo.ColumnName = 'TenderType'

AND TableInfo.IsActive = 'Y'

AND ColumnInfo.IsActive = 'Y'

AND ReferenceInfo.IsActive = 'Y'

AND RefList.IsActive = 'Y'

AND " + GetTextCastSql(
                "RefList.Value"
            ) + @" = " + tenderTypeSql;

            try
            {
                reader = DB.ExecuteReader(
                    sql,
                    null,
                    null
                );

                return (
                    reader != null &&
                    reader.Read()
                );
            }
            finally
            {
                CloseReader(reader);
            }
        }

        #endregion

        #region SQL Helpers

        private string GetDateFilter(
            string columnName,
            DateTime dateFrom,
            DateTime dateTo)
        {
            string dateFromText =
                dateFrom.ToString(
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture
                );

            string dateToText =
                dateTo.ToString(
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture
                );

            if (DB.IsOracle())
            {
                return @"
AND " + columnName + @" >= TO_DATE('" +
                    dateFromText +
                    @"', 'YYYY-MM-DD')

AND " + columnName + @" < TO_DATE('" +
                    dateToText +
                    @"', 'YYYY-MM-DD')";
            }

            return @"
AND " + columnName + @" >= DATE '" +
                dateFromText + @"'

AND " + columnName + @" < DATE '" +
                dateToText + @"'";
        }

        private string GetTextCastSql(
            string expression)
        {
            return DB.IsOracle()
                ? "CAST(" +
                  expression +
                  " AS VARCHAR2(4000))"
                : "CAST(" +
                  expression +
                  " AS VARCHAR(4000))";
        }

        private string ToSqlString(
            string value)
        {
            return "'" +
                (
                    value ??
                    string.Empty
                ).Replace(
                    "'",
                    "''"
                ) +
                "'";
        }

        #endregion

        #region General Helpers

        private Ctx GetContext()
        {
            return Session["ctx"] as Ctx;
        }

        private JsonResult GetSessionExpiredResult()
        {
            return Json(new
            {
                success = false,
                error = "Session Expired",
                errorText = "Session Expired",
                hasData = false
            }, JsonRequestBehavior.AllowGet);
        }

        private object GetReaderValue(
            IDataRecord record,
            string columnName)
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
                    StringComparison.OrdinalIgnoreCase))
                {
                    return record.GetValue(index);
                }
            }

            return DBNull.Value;
        }

        private int GetInt(
            IDataRecord record,
            string columnName,
            int fallback = 0)
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

            return Util.GetValueOfInt(value);
        }

        private decimal GetDecimal(
            IDataRecord record,
            string columnName,
            decimal fallback = 0)
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

            return Util.GetValueOfDecimal(value);
        }

        private string GetString(
            IDataRecord record,
            string columnName,
            string fallback = "")
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

            return Util.GetValueOfString(value);
        }

        private DateTime? GetNullableDateTime(
            IDataRecord record,
            string columnName)
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

            return Util.GetValueOfDateTime(value);
        }

        private string FirstNotEmpty(
            params string[] values)
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
            int precision)
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

        private string FormatDate(
            DateTime? date)
        {
            return date.HasValue
                ? date.Value.ToString(
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture
                )
                : string.Empty;
        }

        private string FormatRunDate(
            DateTime? date)
        {
            if (!date.HasValue)
            {
                return string.Empty;
            }

            return date.Value.ToString(
                "ddd, dd MMM",
                CultureInfo.InvariantCulture
            );
        }

        private string GetPaymentCountText(
            Ctx ctx,
            int count)
        {
            return count == 1
                ? GetMsg(
                    ctx,
                    "VAS_031_MessagePayment",
                    "invoice"
                )
                : GetMsg(
                    ctx,
                    "VAS_031_MessagePayments",
                    "invoices"
                );
        }

        private string GetPaymentMethodName(
            Ctx ctx,
            string name)
        {
            return string.IsNullOrWhiteSpace(name)
                ? GetMsg(
                    ctx,
                    "VAS_031_MessageNotSpecified",
                    "Not Specified"
                )
                : name;
        }

        private string GetLastModelError(
            Ctx ctx,
            string messageKey,
            string fallback)
        {
            string modelError = GetMsg(
                ctx,
                messageKey,
                fallback
            );

            try
            {
                ValueNamePair loggerError =
                    VLogger.RetrieveError();

                if (
                    loggerError != null &&
                    !string.IsNullOrWhiteSpace(
                        loggerError.GetName()
                    )
                )
                {
                    modelError = loggerError.GetName();
                }
            }
            catch
            {
                modelError = fallback;
            }

            return modelError;
        }

        private string GetMsg(
            Ctx ctx,
            string key,
            string fallback)
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
                string.IsNullOrWhiteSpace(message) ||
                message == key ||
                message == "[" + key + "]"
            )
            {
                return fallback;
            }

            return message;
        }

        private void RollbackTransaction(
            Trx trx)
        {
            if (trx != null)
            {
                trx.Rollback();
            }
        }

        private void CloseReader(
            IDataReader reader)
        {
            if (reader == null)
            {
                return;
            }

            reader.Close();
            reader.Dispose();
        }

        #endregion
    }
}