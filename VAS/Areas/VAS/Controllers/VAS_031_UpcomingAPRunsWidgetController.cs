using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Web.Mvc;
using VAdvantage.Classes;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Process;
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
    ///     Due Date + Payment Method + Currency + Business Partner.
    ///
    /// Create:
    ///     Creates and completes C_Payment linked to C_Invoice.
    /// </summary>
    public class VAS_031_UpcomingAPRunsWidgetController : Controller
    {
        private const int MaximumRows = 30;
        private const int PaymentTableId = 335;
        private const int PaymentProcessId = 149;

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

                        cBPartnerId = GetInt(
                            reader,
                            "C_BPartner_ID"
                        ),

                        vendorId = GetInt(
                            reader,
                            "C_BPartner_ID"
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
                : "CURRENT_DATE + INTERVAL '7 DAY'";

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
    Invoice.Description,
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

ScheduleAllocation AS
(
    SELECT
        AllocationLine.C_InvoicePaySchedule_ID,
        SUM
        (
            COALESCE(AllocationLine.Amount, 0)
            + COALESCE(AllocationLine.DiscountAmt, 0)
            + COALESCE(AllocationLine.WriteOffAmt, 0)
        ) AS AllocatedAmt
    FROM C_AllocationLine AllocationLine
    WHERE AllocationLine.IsActive = 'Y'
    AND AllocationLine.C_InvoicePaySchedule_ID IS NOT NULL
    GROUP BY AllocationLine.C_InvoicePaySchedule_ID
),

InvoiceAllocation AS
(
    SELECT
        AllocationLine.C_Invoice_ID,
        SUM
        (
            COALESCE(AllocationLine.Amount, 0)
            + COALESCE(AllocationLine.DiscountAmt, 0)
            + COALESCE(AllocationLine.WriteOffAmt, 0)
        ) AS AllocatedAmt
    FROM C_AllocationLine AllocationLine
    WHERE AllocationLine.IsActive = 'Y'
    AND AllocationLine.C_Invoice_ID IS NOT NULL
    GROUP BY AllocationLine.C_Invoice_ID
),

InvoiceOpenBalance AS
(
    SELECT
        Invoice.C_Invoice_ID,
        CASE
            WHEN
            (
                ABS(COALESCE(Invoice.GrandTotal, 0))
                - ABS(COALESCE(InvoiceAllocation.AllocatedAmt, 0))
            ) > 0
            THEN
            SIGN(COALESCE(Invoice.GrandTotal, 0)) *
            (
                ABS(COALESCE(Invoice.GrandTotal, 0))
                - ABS(COALESCE(InvoiceAllocation.AllocatedAmt, 0))
            )
            ELSE 0
        END AS InvoiceOpenAmount
    FROM SecuredInvoice Invoice
    LEFT OUTER JOIN InvoiceAllocation InvoiceAllocation ON
    (
        InvoiceAllocation.C_Invoice_ID = Invoice.C_Invoice_ID
    )
),

UnpaidSchedule AS
(
    SELECT
        InvoicePaySchedule.C_InvoicePaySchedule_ID,
        InvoicePaySchedule.C_Invoice_ID,
        InvoicePaySchedule.DueDate,
        COALESCE(InvoicePaySchedule.DueAmt, 0) AS DueAmt,
        COALESCE(ScheduleAllocation.AllocatedAmt, 0) AS AllocatedAmt,
        CASE
            WHEN
            (
                ABS(COALESCE(InvoicePaySchedule.DueAmt, 0))
                - ABS(COALESCE(ScheduleAllocation.AllocatedAmt, 0))
            ) > 0
            THEN
            SIGN(COALESCE(InvoicePaySchedule.DueAmt, 0)) *
            (
                ABS(COALESCE(InvoicePaySchedule.DueAmt, 0))
                - ABS(COALESCE(ScheduleAllocation.AllocatedAmt, 0))
            )
            ELSE 0
        END AS ScheduleOpenAmount
    FROM C_InvoicePaySchedule InvoicePaySchedule
    LEFT OUTER JOIN ScheduleAllocation ScheduleAllocation ON
    (
        ScheduleAllocation.C_InvoicePaySchedule_ID =
            InvoicePaySchedule.C_InvoicePaySchedule_ID
    )
    WHERE InvoicePaySchedule.IsActive = 'Y'
    AND COALESCE(InvoicePaySchedule.VA009_IsPaid, 'N') = 'N'
    AND COALESCE(InvoicePaySchedule.C_Payment_ID, 0) = 0
    AND COALESCE(InvoicePaySchedule.C_CashLine_ID, 0) = 0
    AND COALESCE(InvoicePaySchedule.DueAmt, 0) <> 0
    AND NOT EXISTS
    (
        SELECT 1
        FROM C_Payment ExistingPayment
        WHERE ExistingPayment.IsActive = 'Y'
        AND ExistingPayment.C_Invoice_ID = InvoicePaySchedule.C_Invoice_ID
        AND ExistingPayment.C_InvoicePaySchedule_ID =
            InvoicePaySchedule.C_InvoicePaySchedule_ID
        AND ExistingPayment.DocStatus NOT IN ('VO', 'RE')
    )
),

UpcomingInvoices AS
(
    SELECT
        Invoice.C_Invoice_ID,
        Invoice.C_BPartner_ID,
        Invoice.C_BPartner_Location_ID,
        UnpaidSchedule.C_InvoicePaySchedule_ID,
        BusinessPartner.Name AS VendorName,
        COALESCE(UnpaidSchedule.DueDate, Invoice.DateAcct) AS DueDate,
        Invoice.C_Currency_ID,
        Currency.ISO_Code AS CurrencyISO,
        Currency.CurSymbol AS CurrencySymbol,
        Currency.StdPrecision,
        Invoice.VA009_PaymentMethod_ID,
        PaymentMethod.VA009_Name AS PaymentMethodName,
        UnpaidSchedule.DueAmt AS ScheduleDueAmount,
        UnpaidSchedule.AllocatedAmt AS ScheduleAllocatedAmount,
        UnpaidSchedule.ScheduleOpenAmount,
        InvoiceOpenBalance.InvoiceOpenAmount,
        CASE
            WHEN ABS(UnpaidSchedule.ScheduleOpenAmount) <
             ABS(InvoiceOpenBalance.InvoiceOpenAmount)
        THEN UnpaidSchedule.ScheduleOpenAmount
        ELSE InvoiceOpenBalance.InvoiceOpenAmount
        END AS OpenAmount
    FROM SecuredInvoice Invoice
    INNER JOIN C_BPartner BusinessPartner ON
    (
        BusinessPartner.C_BPartner_ID = Invoice.C_BPartner_ID
    )
    INNER JOIN UnpaidSchedule UnpaidSchedule ON
    (
        UnpaidSchedule.C_Invoice_ID = Invoice.C_Invoice_ID
        AND UnpaidSchedule.ScheduleOpenAmount <> 0
    )
    INNER JOIN InvoiceOpenBalance InvoiceOpenBalance ON
    (
        InvoiceOpenBalance.C_Invoice_ID = Invoice.C_Invoice_ID
        AND InvoiceOpenBalance.InvoiceOpenAmount <> 0
    )
    LEFT OUTER JOIN C_Currency Currency ON
    (
        Currency.C_Currency_ID = Invoice.C_Currency_ID
    )
    LEFT OUTER JOIN VA009_PaymentMethod PaymentMethod ON
    (
        PaymentMethod.VA009_PaymentMethod_ID =
            Invoice.VA009_PaymentMethod_ID
    )
    WHERE BusinessPartner.IsActive = 'Y'
    AND BusinessPartner.IsVendor = 'Y'
    AND COALESCE(UnpaidSchedule.DueDate, Invoice.DateAcct) >= " + currentDateSql + @"
    AND COALESCE(UnpaidSchedule.DueDate, Invoice.DateAcct) < " + dateToSql + @"
)

SELECT
    DueDate AS RunDate,
    C_BPartner_ID,
    VendorName,
    COALESCE(VA009_PaymentMethod_ID, 0) AS VA009_PaymentMethod_ID,
    PaymentMethodName,
    C_Currency_ID,
    CurrencyISO,
    CurrencySymbol,
    MAX(COALESCE(StdPrecision, 2)) AS StdPrecision,
    COUNT(1) AS PaymentCount,
    SUM(OpenAmount) AS Amount
FROM UpcomingInvoices
WHERE OpenAmount <> 0
GROUP BY
    DueDate,
    C_BPartner_ID,
    VendorName,
    COALESCE(VA009_PaymentMethod_ID, 0),
    PaymentMethodName,
    C_Currency_ID,
    CurrencyISO,
    CurrencySymbol
ORDER BY
    RunDate,
    Amount DESC,
    VendorName";

            return sql;
        }
        #endregion



        #region Upcoming Invoice Details


        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetUpcomingAPRunDetails(
            string runDate,
            int paymentMethodId = 0,
            int currencyId = 0,
            int cBPartnerId = 0)
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
                    "VAS_031_TransactionDateInvalid",
                    "Invalid due date."
                );

                return Json(new
                {
                    success = false,
                    error = invalidDateMessage,
                    errorText = invalidDateMessage,
                    hasData = false,
                    rows = new List<object>()
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
                    errorText = currencyMessage,
                    hasData = false,
                    rows = new List<object>()
                }, JsonRequestBehavior.AllowGet);
            }

            if (cBPartnerId <= 0)
            {
                string businessPartnerMessage = GetMsg(
                    ctx,
                    "VAS_031_BusinessPartnerRequired",
                    "Business partner is required."
                );

                return Json(new
                {
                    success = false,
                    error = businessPartnerMessage,
                    errorText = businessPartnerMessage,
                    hasData = false,
                    rows = new List<object>()
                }, JsonRequestBehavior.AllowGet);
            }

            IDataReader reader = null;

            try
            {
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

                string cBPartnerIdSql =
                    cBPartnerId.ToString(
                        CultureInfo.InvariantCulture
                    );

                string runDateText =
                    parsedRunDate.ToString(
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture
                    );

                string runDateSql =
                    DB.IsOracle()
                        ? "TO_DATE('" +
                          runDateText +
                          "', 'YYYY-MM-DD')"
                        : "DATE '" +
                          runDateText +
                          "'";

                /*
                 * Exclusive end date for the selected widget group.
                 *
                 * Oracle:
                 *     selected date + 1 day
                 *
                 * PostgreSQL:
                 *     selected date + INTERVAL '1 DAY'
                 */
                string runDateEndSql =
                    DB.IsOracle()
                        ? runDateSql + " + 1"
                        : runDateSql + " + INTERVAL '1 DAY'";

                string periodEndExclusiveSql =
                    DB.IsOracle()
                        ? "Period.EndDate + 1"
                        : "Period.EndDate + INTERVAL '1 DAY'";

                /*
                 * Apply MRole only to the main physical table query
                 * before placing it inside the SecuredInvoice CTE.
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
    Invoice.Description,
    Invoice.DateInvoiced,
    Invoice.DateAcct,
    Invoice.GrandTotal,
    Invoice.VA009_PaymentMethod_ID

FROM C_Invoice Invoice

WHERE Invoice.IsActive = 'Y'

AND Invoice.AD_Client_ID =
    " + clientIdSql + @"

AND Invoice.IsSOTrx = 'N'

AND Invoice.DocStatus IN
(
    'CO',
    'CL'
)

AND EXISTS
(
    SELECT
        1

    FROM C_DocType InvoiceDocumentType

    WHERE InvoiceDocumentType.C_DocType_ID =
        Invoice.C_DocType_ID

    AND InvoiceDocumentType.IsActive = 'Y'

    AND InvoiceDocumentType.DocBaseType = 'API'

    AND InvoiceDocumentType.IsSOTrx = 'N'
)

AND Invoice.C_Currency_ID =
    " + currencyIdSql + @"

AND Invoice.C_BPartner_ID =
    " + cBPartnerIdSql + @"

AND COALESCE
(
    Invoice.VA009_PaymentMethod_ID,
    0
) = " + paymentMethodIdSql;

                invoiceAccessSql =
                    MRole.GetDefault(ctx).AddAccessSQL(
                        invoiceAccessSql,
                        "Invoice",
                        MRole.SQL_FULLYQUALIFIED,
                        MRole.SQL_RO
                    );

                string sql = @"
WITH SelectedPeriodSource AS
(
    SELECT
        Period.C_Period_ID,
        Period.StartDate AS DateFrom,
        Period.EndDate AS DateTo,

        ROW_NUMBER() OVER
        (
            ORDER BY
                Period.StartDate DESC,
                Period.C_Period_ID DESC
        ) AS PeriodRowNumber

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

    AND " + runDateSql + @" >=
        Period.StartDate

    AND " + runDateSql + @" <
        " + periodEndExclusiveSql + @"
),

SelectedPeriod AS
(
    SELECT
        SelectedPeriodSource.C_Period_ID,
        SelectedPeriodSource.DateFrom,
        SelectedPeriodSource.DateTo

    FROM SelectedPeriodSource SelectedPeriodSource

    WHERE SelectedPeriodSource.PeriodRowNumber = 1
),

SecuredInvoice AS
(
" + invoiceAccessSql + @"
),

ScheduleAllocation AS
(
    SELECT
        AllocationLine.C_InvoicePaySchedule_ID,

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

    AND AllocationLine.C_InvoicePaySchedule_ID IS NOT NULL

    GROUP BY
        AllocationLine.C_InvoicePaySchedule_ID
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

InvoiceOpenBalance AS
(
    SELECT
        Invoice.C_Invoice_ID,

        CASE
            WHEN
            (
                ABS(COALESCE(Invoice.GrandTotal, 0))
                - ABS(COALESCE(InvoiceAllocation.AllocatedAmt, 0))
            ) > 0
            THEN
            SIGN(COALESCE(Invoice.GrandTotal, 0)) *
            (
                ABS(COALESCE(Invoice.GrandTotal, 0))
                - ABS(COALESCE(InvoiceAllocation.AllocatedAmt, 0))
            )
            ELSE 0
        END AS InvoiceOpenAmount

    FROM SecuredInvoice Invoice

    LEFT OUTER JOIN InvoiceAllocation InvoiceAllocation ON
    (
        InvoiceAllocation.C_Invoice_ID =
            Invoice.C_Invoice_ID
    )
),

UnpaidSchedule AS
(
    SELECT
        InvoicePaySchedule.C_InvoicePaySchedule_ID,
        InvoicePaySchedule.C_Invoice_ID,
        InvoicePaySchedule.DueDate,

        COALESCE
        (
            InvoicePaySchedule.DueAmt,
            0
        ) AS DueAmt,

        COALESCE
        (
            InvoicePaySchedule.DiscountAmt,
            0
        ) AS DiscountAmt,

        InvoicePaySchedule.DiscountDate,

        InvoicePaySchedule.DiscountDays2,

        COALESCE
        (
            InvoicePaySchedule.Discount2,
            0
        ) AS Discount2,

        0 AS WriteOffAmt,

        COALESCE
        (
            ScheduleAllocation.AllocatedAmt,
            0
        ) AS AllocatedAmt,

        CASE
            WHEN
            (
                ABS(COALESCE(InvoicePaySchedule.DueAmt, 0))
                - ABS(COALESCE(ScheduleAllocation.AllocatedAmt, 0))
            ) > 0
            THEN
            SIGN(COALESCE(InvoicePaySchedule.DueAmt, 0)) *
            (
                ABS(COALESCE(InvoicePaySchedule.DueAmt, 0))
                - ABS(COALESCE(ScheduleAllocation.AllocatedAmt, 0))
            )
            ELSE 0
        END AS ScheduleOpenAmount

    FROM C_InvoicePaySchedule InvoicePaySchedule

    LEFT OUTER JOIN ScheduleAllocation ScheduleAllocation ON
    (
        ScheduleAllocation.C_InvoicePaySchedule_ID =
            InvoicePaySchedule.C_InvoicePaySchedule_ID
    )

    WHERE InvoicePaySchedule.IsActive = 'Y'

    AND COALESCE
    (
        InvoicePaySchedule.VA009_IsPaid,
        'N'
    ) = 'N'

    AND COALESCE
    (
        InvoicePaySchedule.DueAmt,
        0
    ) <> 0

    AND COALESCE
    (
        InvoicePaySchedule.C_Payment_ID,
        0
    ) = 0

    AND COALESCE
    (
        InvoicePaySchedule.C_CashLine_ID,
        0
    ) = 0

    AND NOT EXISTS
    (
        SELECT
            1

        FROM C_Payment ExistingPayment

        WHERE ExistingPayment.IsActive = 'Y'

        AND ExistingPayment.C_Invoice_ID =
            InvoicePaySchedule.C_Invoice_ID

        AND ExistingPayment.C_InvoicePaySchedule_ID =
            InvoicePaySchedule.C_InvoicePaySchedule_ID

        AND ExistingPayment.DocStatus NOT IN
        (
            'VO',
            'RE'
        )
    )
)

SELECT
    Invoice.C_Invoice_ID,
    Invoice.DocumentNo,
    Invoice.Description,
    Invoice.AD_Org_ID,

    Organization.Name AS OrganizationName,

    Invoice.C_BPartner_ID,
    Invoice.C_BPartner_Location_ID,

    UnpaidSchedule.C_InvoicePaySchedule_ID,

    UnpaidSchedule.DueAmt AS ScheduleDueAmount,

    UnpaidSchedule.DiscountAmt AS DiscountAmt,

    UnpaidSchedule.DiscountDate,

    UnpaidSchedule.DiscountDays2,

    UnpaidSchedule.Discount2,

    UnpaidSchedule.WriteOffAmt AS WriteOffAmt,

    UnpaidSchedule.AllocatedAmt AS ScheduleAllocatedAmount,

    CASE
        WHEN ABS(UnpaidSchedule.ScheduleOpenAmount) <
             ABS(InvoiceOpenBalance.InvoiceOpenAmount)
        THEN UnpaidSchedule.ScheduleOpenAmount
        ELSE InvoiceOpenBalance.InvoiceOpenAmount
    END AS ScheduleOpenAmount,

    COALESCE
    (
        invoiceOpen
        (
            Invoice.C_Invoice_ID,
            UnpaidSchedule.C_InvoicePaySchedule_ID
        ),
        0
    ) AS InitialOpenAmount,

    COALESCE
    (
        invoiceDiscount
        (
            Invoice.C_Invoice_ID,
            " + runDateSql + @",
            UnpaidSchedule.C_InvoicePaySchedule_ID
        ),
        0
    ) AS InitialDiscountAmount,

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
        UnpaidSchedule.DueDate,
        Invoice.DateAcct
    ) AS DueDate,

    Invoice.GrandTotal,

    CASE
        WHEN ABS(UnpaidSchedule.ScheduleOpenAmount) <
             ABS(InvoiceOpenBalance.InvoiceOpenAmount)
        THEN UnpaidSchedule.ScheduleOpenAmount
        ELSE InvoiceOpenBalance.InvoiceOpenAmount
    END AS OpenAmount,

    SelectedPeriod.C_Period_ID,

    SelectedPeriod.DateFrom AS PeriodDateFrom,

    SelectedPeriod.DateTo AS PeriodDateTo

FROM SecuredInvoice Invoice

INNER JOIN C_BPartner BusinessPartner ON
(
    BusinessPartner.C_BPartner_ID =
        Invoice.C_BPartner_ID
)

INNER JOIN UnpaidSchedule UnpaidSchedule ON
(
    UnpaidSchedule.C_Invoice_ID =
        Invoice.C_Invoice_ID

    AND UnpaidSchedule.ScheduleOpenAmount <> 0
)

INNER JOIN InvoiceOpenBalance InvoiceOpenBalance ON
(
    InvoiceOpenBalance.C_Invoice_ID =
        Invoice.C_Invoice_ID

    AND InvoiceOpenBalance.InvoiceOpenAmount <> 0
)

INNER JOIN SelectedPeriod SelectedPeriod ON
(
    COALESCE
    (
        UnpaidSchedule.DueDate,
        Invoice.DateAcct
    ) >= SelectedPeriod.DateFrom

    AND COALESCE
    (
        UnpaidSchedule.DueDate,
        Invoice.DateAcct
    ) <
        " +
                (
                    DB.IsOracle()
                        ? "SelectedPeriod.DateTo + 1"
                        : "SelectedPeriod.DateTo + INTERVAL '1 DAY'"
                ) + @"
)

LEFT OUTER JOIN AD_Org Organization ON
(
    Organization.AD_Org_ID =
        Invoice.AD_Org_ID
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

/*
 * The main widget groups records by:
 *
 * 1. Due Date
 * 2. Payment Method
 * 3. Currency
 * 4. Business Partner
 *
 * Payment Method, Currency, and Business Partner are already filtered
 * inside SecuredInvoice.
 *
 * This date filter ensures that the popup returns only
 * invoices belonging to the selected widget group.
 */
AND COALESCE
(
    UnpaidSchedule.DueDate,
    Invoice.DateAcct
) >= " + runDateSql + @"

AND COALESCE
(
    UnpaidSchedule.DueDate,
    Invoice.DateAcct
) < " + runDateEndSql + @"

ORDER BY
    DueDate,
    Invoice.DocumentNo,
    UnpaidSchedule.C_InvoicePaySchedule_ID,
    Invoice.C_Invoice_ID";

                reader = DB.ExecuteReader(
                    sql,
                    null,
                    null
                );

                List<object> rows =
                    new List<object>();

                DateTime? periodDateFrom = null;
                DateTime? periodDateTo = null;

                int periodId = 0;

                while (
                    reader != null &&
                    reader.Read()
                )
                {
                    DateTime? dueDate =
                        GetNullableDateTime(
                            reader,
                            "DueDate"
                        );

                    DateTime? dateInvoiced =
                        GetNullableDateTime(
                            reader,
                            "DateInvoiced"
                        );

                    if (!periodDateFrom.HasValue)
                    {
                        periodDateFrom =
                            GetNullableDateTime(
                                reader,
                                "PeriodDateFrom"
                            );
                    }

                    if (!periodDateTo.HasValue)
                    {
                        periodDateTo =
                            GetNullableDateTime(
                                reader,
                                "PeriodDateTo"
                            );
                    }

                    if (periodId <= 0)
                    {
                        periodId = GetInt(
                            reader,
                            "C_Period_ID"
                        );
                    }

                    string currencyISO =
                        GetString(
                            reader,
                            "CurrencyISO"
                        );

                    string currencySymbol =
                        GetString(
                            reader,
                            "CurrencySymbol"
                        );

                    if (string.IsNullOrWhiteSpace(
                        currencySymbol
                    ))
                    {
                        currencySymbol =
                            currencyISO;
                    }

                    int invoiceId =
                        GetInt(
                            reader,
                            "C_Invoice_ID"
                        );

                    int vendorId =
                        GetInt(
                            reader,
                            "C_BPartner_ID"
                        );

                    int invoicePayScheduleId =
                        GetInt(
                            reader,
                            "C_InvoicePaySchedule_ID"
                        );

                    decimal scheduleDueAmount =
                        GetDecimal(
                            reader,
                            "ScheduleDueAmount"
                        );

                    decimal scheduleAllocatedAmount =
                        GetDecimal(
                            reader,
                            "ScheduleAllocatedAmount"
                        );

                    decimal scheduleDiscountAmt =
                        GetDecimal(
                            reader,
                            "DiscountAmt"
                        );

                    decimal scheduleDiscountAmt2 =
                        GetDecimal(
                            reader,
                            "Discount2"
                        );

                    decimal writeOffAmt =
                        GetDecimal(
                            reader,
                            "WriteOffAmt"
                        );

                    decimal scheduleOpenAmount =
                        GetDecimal(
                            reader,
                            "ScheduleOpenAmount"
                        );

                    decimal initialOpenAmount =
                        GetDecimal(
                            reader,
                            "InitialOpenAmount"
                        );

                    decimal appliedDiscountAmt =
                        GetDecimal(
                            reader,
                            "InitialDiscountAmount"
                        );

                    if (initialOpenAmount <= 0)
                    {
                        initialOpenAmount =
                            scheduleOpenAmount;
                    }

                    if (appliedDiscountAmt < 0)
                    {
                        appliedDiscountAmt = 0;
                    }

                    if (
                        appliedDiscountAmt >
                        initialOpenAmount
                    )
                    {
                        appliedDiscountAmt =
                            initialOpenAmount;
                    }

                    decimal schedulePayAmt =
                        initialOpenAmount -
                        appliedDiscountAmt;

                    if (schedulePayAmt < 0)
                    {
                        schedulePayAmt = 0;
                    }

                    if (
                        invoicePayScheduleId <= 0 ||
                        initialOpenAmount <= 0
                    )
                    {
                        continue;
                    }

                    rows.Add(new
                    {
                        invoiceId = invoiceId,
                        sourceInvoiceId = invoiceId,
                        cInvoiceId = invoiceId,

                        invoicePayScheduleId =
                            invoicePayScheduleId,

                        cInvoicePayScheduleId =
                            invoicePayScheduleId,

                        invoiceDocumentNo =
                            GetString(
                                reader,
                                "DocumentNo"
                            ),

                        documentNo =
                            GetString(
                                reader,
                                "DocumentNo"
                            ),

                        description =
                            GetString(
                                reader,
                                "Description"
                            ),

                        invoiceDescription =
                            GetString(
                                reader,
                                "Description"
                            ),

                        organizationId =
                            GetInt(
                                reader,
                                "AD_Org_ID"
                            ),

                        adOrgId =
                            GetInt(
                                reader,
                                "AD_Org_ID"
                            ),

                        organizationName =
                            GetString(
                                reader,
                                "OrganizationName"
                            ),

                        vendorId = vendorId,
                        cBPartnerId = vendorId,

                        bPartnerLocationId =
                            GetInt(
                                reader,
                                "C_BPartner_Location_ID"
                            ),

                        cBPartnerLocationId =
                            GetInt(
                                reader,
                                "C_BPartner_Location_ID"
                            ),

                        vendorName =
                            GetString(
                                reader,
                                "VendorName"
                            ),

                        isVendor =
                            string.Equals(
                                GetString(
                                    reader,
                                    "IsVendor"
                                ),
                                "Y",
                                StringComparison.OrdinalIgnoreCase
                            ),

                        cCurrencyId =
                            GetInt(
                                reader,
                                "C_Currency_ID"
                            ),

                        currencyId =
                            GetInt(
                                reader,
                                "C_Currency_ID"
                            ),

                        currencyISO =
                            currencyISO,

                        currencySymbol =
                            currencySymbol,

                        stdPrecision =
                            NormalizePrecision(
                                GetInt(
                                    reader,
                                    "StdPrecision",
                                    2
                                )
                            ),

                        conversionTypeId =
                            GetInt(
                                reader,
                                "C_ConversionType_ID"
                            ),

                        cConversionTypeId =
                            GetInt(
                                reader,
                                "C_ConversionType_ID"
                            ),

                        currencyTypeName =
                            GetString(
                                reader,
                                "CurrencyTypeName"
                            ),

                        dateInvoiced =
                            FormatDate(
                                dateInvoiced
                            ),

                        dueDate =
                            FormatDate(
                                dueDate
                            ),

                        transactionDate =
                            FormatDate(
                                parsedRunDate
                            ),

                        dateTrx =
                            FormatDate(
                                parsedRunDate
                            ),

                        grandTotal =
                            GetDecimal(
                                reader,
                                "GrandTotal"
                            ),

                        scheduleDueAmount =
                            scheduleDueAmount,

                        scheduleAllocatedAmount =
                            scheduleAllocatedAmount,

                        discountAmt =
                            appliedDiscountAmt,

                        discountAmount =
                            appliedDiscountAmt,

                        writeOffAmt =
                            writeOffAmt,

                        writeOffAmount =
                            writeOffAmt,

                        scheduleOpenAmount =
                            scheduleOpenAmount,

                        openAmount =
                            initialOpenAmount,

                        amount =
                            schedulePayAmt,

                        payAmt =
                            schedulePayAmt,

                        paymentMethodId =
                            GetInt(
                                reader,
                                "VA009_PaymentMethod_ID"
                            ),

                        paymentMethodName =
                            GetPaymentMethodName(
                                ctx,
                                GetString(
                                    reader,
                                    "PaymentMethodName"
                                )
                            ),

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

                    hasData =
                        rows.Count > 0,

                    periodId =
                        periodId,

                    dateFrom =
                        FormatDate(
                            periodDateFrom
                        ),

                    dateTo =
                        FormatDate(
                            periodDateTo
                        ),

                    rows =
                        rows
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
                    errorText = message,
                    hasData = false,
                    rows = new List<object>()
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
        public JsonResult GetPaymentPopupLookups(
            int currencyId = 0)
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
                        clientId,
                        currencyId
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

WHERE Currency.IsActive = 'Y' and Currency.IsMyCurrency = 'Y'

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
            int clientId,
            int currencyId = 0)
        {
            List<object> rows = new List<object>();

            IDataReader reader = null;

            string sql = @"
SELECT
    BankAccount.C_BankAccount_ID AS ID,

    Bank.Name AS BankName,

    BankAccount.Name AS BankAccountName,

    BankAccount.AccountNo,

    Currency.ISO_Code AS CurrencyISO

FROM C_BankAccount BankAccount

LEFT OUTER JOIN C_Bank Bank ON
(
    Bank.C_Bank_ID =
    BankAccount.C_Bank_ID
)

LEFT OUTER JOIN C_Currency Currency ON
(
    Currency.C_Currency_ID =
    BankAccount.C_Currency_ID
)

WHERE BankAccount.IsActive = 'Y'

AND BankAccount.AD_Client_ID = " +
                clientId.ToString(
                    CultureInfo.InvariantCulture
                ) + @"
" +
                (
                    currencyId > 0
                        ? @"
AND BankAccount.C_Currency_ID = " +
                          currencyId.ToString(
                              CultureInfo.InvariantCulture
                          )
                        : string.Empty
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

                    string currencyISO = GetString(
                        reader,
                        "CurrencyISO"
                    );

                    /*
                     * Display format: "BankName - AccountNo - Currency"
                     * e.g. "Iraqi National Bank - 123456789 - IQD".
                     * The bank name is shown first, followed by the
                     * account number, falling back to the account name when
                     * the bank has no name.
                     */
                    List<string> displayNameParts =
                        new List<string>();

                    string primaryName = FirstNotEmpty(
                        bankName,
                        accountName
                    );

                    if (!string.IsNullOrWhiteSpace(primaryName))
                    {
                        displayNameParts.Add(primaryName);
                    }

                    if (!string.IsNullOrWhiteSpace(accountNo))
                    {
                        displayNameParts.Add(accountNo);
                    }

                    string displayName = string.Join(
                        " - ",
                        displayNameParts
                    );

                    if (!string.IsNullOrWhiteSpace(currencyISO))
                    {
                        displayName =
                            displayName +
                            " - " +
                            currencyISO;
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
) AND  PaymentMethod.VA009_PAYMENTBASETYPE NOT IN ('B','C','P') 
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
            string paymentDescription,
            decimal discountAmt,
            decimal writeOffAmt,
            decimal payAmt)
        {
            Ctx ctx = GetContext();

            if (ctx == null)
            {
                return GetSessionExpiredResult();
            }

            if (invoiceId <= 0)
            {
                return GetValidationError(
                    ctx,
                    "VAS_031_SourceInvoiceRequired",
                    "Source invoice is required."
                );
            }

            if (invoicePayScheduleId <= 0)
            {
                return GetValidationError(
                    ctx,
                    "VAS_031_InvoicePayScheduleRequired",
                    "Invoice payment schedule is required."
                );
            }

            if (paymentMethodId <= 0)
            {
                return GetValidationError(
                    ctx,
                    "VAS_031_PaymentMethodRequired",
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
                    "VAS_031_TransactionDateInvalid",
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
                            "VAS_031_SourceInvoiceNotFound",
                            "The source invoice was not found."
                        )
                    );
                }

                if (invoice.GetAD_Client_ID() != ctx.GetAD_Client_ID())
                {
                    throw new InvalidOperationException(
                        GetMsg(
                            ctx,
                            "VAS_031_InvalidSourceInvoice",
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
                        GetMsg(
                            ctx,
                            "VAS_031_InvalidAPInvoiceDocBaseType",
                            "The selected document is not a normal AP Invoice. Invoice DocBaseType must be API, but it is {0}."
                        ).Replace(
                            "{0}",
                            invoiceDocBaseType
                        )
                    );
                }

                /*
                 * Prevent changing invoice-related information
                 * from the UI.
                 */
                if (invoice.GetAD_Org_ID() != adOrgId)
                {
                    throw new InvalidOperationException(
                        GetMsg(
                            ctx,
                            "VAS_031_OrganizationInvoiceMismatch",
                            "Selected organization does not match the invoice."
                        )
                    );
                }

                if (invoice.GetC_BPartner_ID() != vendorId)
                {
                    throw new InvalidOperationException(
                        GetMsg(
                            ctx,
                            "VAS_031_VendorInvoiceMismatch",
                            "Selected vendor does not match the invoice."
                        )
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
                        GetMsg(
                            ctx,
                            "VAS_031_InvalidInvoicePaySchedule",
                            "The selected invoice payment schedule is invalid, inactive, paid, or does not belong to the selected invoice."
                        )
                    );
                }

                if (HasExistingPaymentForSchedule(
                    invoiceId,
                    validatedInvoicePayScheduleId,
                    trx
                ))
                {
                    throw new InvalidOperationException(
                        GetMsg(
                            ctx,
                            "VAS_031_ScheduleAlreadyHasPayment",
                            "Invoice schedule already has an active payment."
                        )
                    );
                }

                /*
                 * Read the current invoice open amount before creating payment.
                 */
                decimal openAmountBeforePayment =
                    GetInvoicePayScheduleOpenAmount(
                        invoiceId,
                        validatedInvoicePayScheduleId,
                        trx
                    );

                if (openAmountBeforePayment <= 0)
                {
                    throw new InvalidOperationException(
                        GetMsg(
                            ctx,
                            "VAS_031_MessageInvoiceAlreadyPaid",
                            "The invoice payment schedule has no open amount."
                        )
                    );
                }

                if (discountAmt < 0)
                {
                    throw new InvalidOperationException(
                        GetMsg(
                            ctx,
                            "VAS_031_MessageDiscountInvalid",
                            "Discount amount must be zero or greater."
                        )
                    );
                }

                if (writeOffAmt < 0)
                {
                    throw new InvalidOperationException(
                        GetMsg(
                            ctx,
                            "VAS_031_MessageWriteOffInvalid",
                            "Write off amount must be zero or greater."
                        )
                    );
                }

                if (
                    invoice.GetC_Currency_ID() == currencyId &&
                    (
                        payAmt > openAmountBeforePayment ||
                        (payAmt + discountAmt + writeOffAmt) > openAmountBeforePayment
                    )
                )
                {
                    throw new InvalidOperationException(
                        GetMsg(
                            ctx,
                            "VAS_031_PaymentExceedsOpenAmount",
                            "Payment amount and discount exceed the invoice open amount."
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
                        GetMsg(
                            ctx,
                            "VAS_031_OrganizationInactive",
                            "Organization was not found or is inactive."
                        )
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
                        GetMsg(
                            ctx,
                            "VAS_031_MessageBankAccountInactive",
                            "Bank account was not found or is inactive."
                        )
                    );
                }

                if (bankAccount.GetAD_Client_ID() != ctx.GetAD_Client_ID())
                {
                    throw new InvalidOperationException(
                        GetMsg(
                            ctx,
                            "VAS_031_BankAccountInvalidClient",
                            "Bank account does not belong to the current client."
                        )
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
                        GetMsg(
                            ctx,
                            "VAS_031_MessageInactiveVendor",
                            "Selected business partner is not an active vendor."
                        )
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
                        GetMsg(
                            ctx,
                            "VAS_031_VendorLocationNotFound",
                            "No active business partner location was found for the selected vendor."
                        )
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
                        GetMsg(
                            ctx,
                            "VAS_031_MessageCurrencyInactive",
                            "Currency was not found or is inactive."
                        )
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
                        GetMsg(
                            ctx,
                            "VAS_031_ConversionTypeInactive",
                            "Conversion type was not found or is inactive."
                        )
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
                        GetMsg(
                            ctx,
                            "VAS_031_APPaymentDocTypeNotFound",
                            "AP Payment document type was not found."
                        )
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
                        GetMsg(
                            ctx,
                            "VAS_031_InvalidAPPaymentDocType",
                            "The selected document type is not an AP Payment document type."
                        )
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
                        GetMsg(
                            ctx,
                            "VAS_031_MessageInvalidTenderType",
                            "The selected tender type is not valid."
                        )
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
                            "VAS_031_InvalidPaymentMethod",
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

                decimal scheduleOpenAmountInPaymentCurrency =
                    openAmountBeforePayment;

                if (invoice.GetC_Currency_ID() != currencyId)
                {
                    scheduleOpenAmountInPaymentCurrency =
                        MConversionRate.Convert(
                            ctx,
                            openAmountBeforePayment,
                            invoice.GetC_Currency_ID(),
                            currencyId,
                            dateTrx,
                            conversionTypeId,
                            invoice.GetAD_Client_ID(),
                            invoice.GetAD_Org_ID()
                        );

                    if (scheduleOpenAmountInPaymentCurrency <= 0)
                    {
                        throw new InvalidOperationException(
                            GetMsg(
                                ctx,
                                "VAS_031_NoCurrencyConversion",
                                "No currency conversion found for the selected currency."
                            )
                        );
                    }
                }

                if (
                    payAmt + discountAmt + writeOffAmt >
                    scheduleOpenAmountInPaymentCurrency
                )
                {
                    throw new InvalidOperationException(
                        GetMsg(
                            ctx,
                            "VAS_031_PaymentExceedsOpenAmount",
                            "Payment amount and discount exceed the invoice open amount."
                        )
                    );
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
                    currencyId
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

                payment.SetDateTrx(DateTime.Now);
                payment.SetDateAcct(DateTime.Now);

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

                if (!string.IsNullOrWhiteSpace(
                    paymentDescription
                ))
                {
                    payment.Set_Value(
                        "Description",
                        paymentDescription.Trim()
                    );
                }

                payment.SetPayAmt(payAmt);

                if (discountAmt > 0)
                {
                    payment.SetDiscountAmt(discountAmt);
                }

                if (
                    writeOffAmt > 0 &&
                    payment.Get_ColumnIndex(
                        "WriteOffAmt"
                    ) >= 0
                )
                {
                    payment.SetWriteOffAmt(writeOffAmt);
                }

                decimal overUnderAmt =
                    scheduleOpenAmountInPaymentCurrency -
                    payAmt -
                    discountAmt -
                    writeOffAmt;

                payment.SetOverUnderAmt(overUnderAmt);
                payment.SetIsOverUnderPayment(
                    overUnderAmt != 0
                );

                /*
                 * Save the payment in progress first, then complete
                 * it through the standard workflow process below.
                 */
                payment.SetDocStatus(
                    MPayment.DOCSTATUS_InProgress
                );
                payment.SetDocAction(
                    MPayment.DOCACTION_Complete
                );
                payment.SetProcessed(false);

                if (!string.IsNullOrWhiteSpace(documentNo))
                {
                    payment.SetDocumentNo(
                        documentNo.Trim()
                    );
                }

                if (!payment.Save(trx))
                {
                    throw new InvalidOperationException(
                        GetLastModelError(
                            ctx,
                            "VAS_031_CouldNotSaveAPPayment",
                            "Could not save AP payment."
                        )
                    );
                }

                int createdPaymentId =
                    payment.GetC_Payment_ID();

                /*
                 * Complete through the standard workflow process.
                 * CompleteOrReverse runs outside this transaction,
                 * so commit the saved payment first just like the
                 * existing shipment/payment workflow callers do.
                 */
                trx.Commit();
                trx.Close();
                trx = null;

                string completionMessage =
                    DocumentEngine.CompleteOrReverse(
                        ctx,
                        "C_Payment",
                        PaymentTableId,
                        createdPaymentId,
                        PaymentProcessId,
                        MPayment.DOCACTION_Complete
                    );

                if (!string.IsNullOrWhiteSpace(completionMessage))
                {
                    throw new InvalidOperationException(
                        completionMessage
                    );
                }

                /*
                 * Reload changes made by the workflow completion.
                 */
                payment = new MPayment(
                    ctx,
                    createdPaymentId,
                    null
                );

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
                            GetMsg(
                                ctx,
                                "VAS_031_PaymentSavedNotCompleted",
                                "Payment was saved but was not completed. Current status: {0}"
                            ).Replace(
                                "{0}",
                                completedDocStatus
                            );
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
                    null
                );

                bool invoiceFullyPaid =
                    remainingOpenAmount <= 0;

                string createdPaymentDocumentNo =
                    payment.GetDocumentNo();

                string msg = (
                    GetMsg(
                        ctx,
                        "VAS_031_PaymentCreatedSuccessfully",
                        "AP payment created and completed successfully:"
                    ) + " " + createdPaymentDocumentNo
                )
                    .Replace("[", string.Empty)
                    .Replace("]", string.Empty)
                    .Trim();


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
                        createdPaymentDocumentNo,

                    paymentDocumentNo =
                        createdPaymentDocumentNo,

                    paymentNo =
                        createdPaymentDocumentNo,

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

                    message = msg
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
                    "VAS_031_CouldNotSaveAPPayment",
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
            if (invoiceId <= 0 ||
                invoicePayScheduleId <= 0)
            {
                return 0;
            }

            string sql = @"
SELECT
    InvoicePaySchedule.C_InvoicePaySchedule_ID

FROM C_InvoicePaySchedule InvoicePaySchedule

WHERE InvoicePaySchedule.IsActive = 'Y'

AND COALESCE
(
    InvoicePaySchedule.VA009_IsPaid,
    'N'
) = 'N'

AND COALESCE
(
    InvoicePaySchedule.C_Payment_ID,
    0
) = 0

AND COALESCE
(
    InvoicePaySchedule.C_CashLine_ID,
    0
) = 0

AND InvoicePaySchedule.C_Invoice_ID = " +
                invoiceId.ToString(
                    CultureInfo.InvariantCulture
                ) + @"

AND InvoicePaySchedule.C_InvoicePaySchedule_ID = " +
                invoicePayScheduleId.ToString(
                    CultureInfo.InvariantCulture
                );

            return Util.GetValueOfInt(
                DB.ExecuteScalar(
                    sql,
                    null,
                    trx
                )
            );
        }

        private bool IsInvoicePaySchedulePaid(
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
    COALESCE
    (
        InvoicePaySchedule.VA009_IsPaid,
        'N'
    )

FROM C_InvoicePaySchedule InvoicePaySchedule

WHERE InvoicePaySchedule.IsActive = 'Y'

AND InvoicePaySchedule.C_Invoice_ID = " +
                invoiceId.ToString(
                    CultureInfo.InvariantCulture
                ) + @"

AND InvoicePaySchedule.C_InvoicePaySchedule_ID = " +
                invoicePayScheduleId.ToString(
                    CultureInfo.InvariantCulture
                );

            string isPaid = Util.GetValueOfString(
                DB.ExecuteScalar(
                    sql,
                    null,
                    trx
                )
            );

            return string.Equals(
                isPaid,
                "Y",
                StringComparison.OrdinalIgnoreCase
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

        private decimal GetInvoicePayScheduleOpenAmount(
            int invoiceId,
            int invoicePayScheduleId,
            Trx trx)
        {
            if (invoiceId <= 0 || invoicePayScheduleId <= 0)
            {
                return 0;
            }

            string invoiceIdSql = invoiceId.ToString(
                CultureInfo.InvariantCulture
            );

            string scheduleIdSql = invoicePayScheduleId.ToString(
                CultureInfo.InvariantCulture
            );

            string sql = @"
SELECT
    CASE
        WHEN ABS(ScheduleBalance.ScheduleOpenAmount) <
             ABS(InvoiceBalance.InvoiceOpenAmount)
        THEN ScheduleBalance.ScheduleOpenAmount
        ELSE InvoiceBalance.InvoiceOpenAmount
    END AS OpenAmount
FROM
(
    SELECT
        CASE
            WHEN
            (
                ABS(COALESCE(InvoicePaySchedule.DueAmt, 0))
                - ABS(COALESCE(ScheduleAllocation.AllocatedAmt, 0))
            ) > 0
            THEN
            SIGN(COALESCE(InvoicePaySchedule.DueAmt, 0)) *
            (
                ABS(COALESCE(InvoicePaySchedule.DueAmt, 0))
                - ABS(COALESCE(ScheduleAllocation.AllocatedAmt, 0))
            )
            ELSE 0
        END AS ScheduleOpenAmount
    FROM C_InvoicePaySchedule InvoicePaySchedule
    LEFT OUTER JOIN
    (
        SELECT
            AllocationLine.C_InvoicePaySchedule_ID,
            SUM
            (
                COALESCE(AllocationLine.Amount, 0)
                + COALESCE(AllocationLine.DiscountAmt, 0)
                + COALESCE(AllocationLine.WriteOffAmt, 0)
            ) AS AllocatedAmt
        FROM C_AllocationLine AllocationLine
        WHERE AllocationLine.IsActive = 'Y'
        AND AllocationLine.C_InvoicePaySchedule_ID = " + scheduleIdSql + @"
        GROUP BY AllocationLine.C_InvoicePaySchedule_ID
    ) ScheduleAllocation ON
    (
        ScheduleAllocation.C_InvoicePaySchedule_ID =
            InvoicePaySchedule.C_InvoicePaySchedule_ID
    )
    WHERE InvoicePaySchedule.IsActive = 'Y'
    AND COALESCE(InvoicePaySchedule.VA009_IsPaid, 'N') = 'N'
    AND COALESCE(InvoicePaySchedule.C_Payment_ID, 0) = 0
    AND COALESCE(InvoicePaySchedule.C_CashLine_ID, 0) = 0
    AND InvoicePaySchedule.C_Invoice_ID = " + invoiceIdSql + @"
    AND InvoicePaySchedule.C_InvoicePaySchedule_ID = " + scheduleIdSql + @"
) ScheduleBalance
CROSS JOIN
(
    SELECT
        CASE
            WHEN
            (
                ABS(COALESCE(Invoice.GrandTotal, 0))
                - ABS(COALESCE(InvoiceAllocation.AllocatedAmt, 0))
            ) > 0
            THEN
            SIGN(COALESCE(Invoice.GrandTotal, 0)) *
            (
                ABS(COALESCE(Invoice.GrandTotal, 0))
                - ABS(COALESCE(InvoiceAllocation.AllocatedAmt, 0))
            )
            ELSE 0
        END AS InvoiceOpenAmount
    FROM C_Invoice Invoice
    LEFT OUTER JOIN
    (
        SELECT
            AllocationLine.C_Invoice_ID,
            SUM
            (
                COALESCE(AllocationLine.Amount, 0)
                + COALESCE(AllocationLine.DiscountAmt, 0)
                + COALESCE(AllocationLine.WriteOffAmt, 0)
            ) AS AllocatedAmt
        FROM C_AllocationLine AllocationLine
        WHERE AllocationLine.IsActive = 'Y'
        AND AllocationLine.C_Invoice_ID = " + invoiceIdSql + @"
        GROUP BY AllocationLine.C_Invoice_ID
    ) InvoiceAllocation ON
    (
        InvoiceAllocation.C_Invoice_ID = Invoice.C_Invoice_ID
    )
    WHERE Invoice.C_Invoice_ID = " + invoiceIdSql + @"
) InvoiceBalance";

            object result = DB.ExecuteScalar(sql, null, trx);

            if (result == null || result == DBNull.Value)
            {
                return 0;
            }

            return Util.GetValueOfDecimal(result);
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
                ABS(COALESCE(Invoice.GrandTotal, 0))
                - ABS(COALESCE(Allocation.AllocatedAmt, 0))
            ) > 0
            THEN
            SIGN(COALESCE(Invoice.GrandTotal, 0)) *
            (
                ABS(COALESCE(Invoice.GrandTotal, 0))
                - ABS(COALESCE(Allocation.AllocatedAmt, 0))
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
                    "VAS_031_OrganizationRequired",
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
                    "VAS_031_ConversionTypeRequired",
                    "Currency type is required."
                );
            }

            if (docTypeId <= 0)
            {
                return GetValidationError(
                    ctx,
                    "VAS_031_DocumentTypeRequired",
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
                    "VAS_031_TransactionDateRequired",
                    "Transaction date is required."
                );
            }

            if (payAmt <= 0)
            {
                return GetValidationError(
                    ctx,
                    "VAS_031_PaymentAmountRequired",
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
                    "VAS_031_TransactionDateInvalid",
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
                errorKey = messageKey,
                messageKey = messageKey,
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
            const string messageKey = "SessionExpired";
            const string message = "Session Expired";

            return Json(new
            {
                success = false,
                errorKey = messageKey,
                messageKey = messageKey,
                error = message,
                errorText = message,
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
