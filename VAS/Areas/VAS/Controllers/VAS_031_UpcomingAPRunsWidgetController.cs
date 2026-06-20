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
    /// Displays AP payments scheduled during the next seven days and creates
    /// a new draft AP payment from the selected upcoming-payment information.
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

                        runDateText = FormatRunDate(
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

                        currencyISO = currencyISO,

                        currencySymbol = currencySymbol,

                        stdPrecision = NormalizePrecision(
                            GetInt(
                                reader,
                                "StdPrecision",
                                2
                            )
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
            catch (Exception)
            {
                string errorMessage = GetMsg(
                    ctx,
                    "VAS_ErrorLoading",
                    "Could not load data"
                );

                return Json(new
                {
                    success = false,
                    error = errorMessage,
                    errorText = errorMessage,
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

            string upcomingDateToSql = DB.IsOracle()
                ? "TRUNC(CURRENT_DATE) + 7"
                : "CURRENT_DATE + INTERVAL '7 day'";

            string periodEndExclusiveSql = DB.IsOracle()
                ? "PeriodRange.PeriodEndDate + 1"
                : "PeriodRange.PeriodEndDate + INTERVAL '1 day'";

            string dateOnlySql = GetDateOnlySql(
                "Payment.DateTrx"
            );

            /*
             * Apply MRole only to the main physical C_Payment table.
             * Period filtering is added later against the secured CTE so
             * MRole never receives a CTE alias or derived table alias.
             */
            string paymentAccessSql = @"
SELECT
    Payment.C_Payment_ID,
    Payment.AD_Client_ID,
    Payment.AD_Org_ID,
    Payment.DateTrx,
    Payment.C_Currency_ID,
    Payment.C_ConversionType_ID,
    Payment.PayAmt,
    Payment.VA009_PaymentMethod_ID
FROM C_Payment Payment
WHERE Payment.IsActive = 'Y'
AND Payment.AD_Client_ID = " + clientIdSql + @"
AND Payment.IsReceipt = 'N'
AND Payment.DocStatus NOT IN ('VO', 'RE')";

            paymentAccessSql = MRole.GetDefault(ctx).AddAccessSQL(
                paymentAccessSql,
                "Payment",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            string amountExpression = @"
COALESCE
(
    SUM
    (
        CASE
            WHEN Payment.C_Currency_ID =
                 SchemaCurrency.C_Currency_ID
            THEN COALESCE(Payment.PayAmt, 0)
            ELSE CurrencyConvert
            (
                COALESCE(Payment.PayAmt, 0),
                Payment.C_Currency_ID,
                SchemaCurrency.C_Currency_ID,
                Payment.DateTrx,
                Payment.C_ConversionType_ID,
                Payment.AD_Client_ID,
                Payment.AD_Org_ID
            )
        END
    ),
    0
)";

            string sql = @"
WITH SchemaCurrency AS
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
    AND ClientInfo.AD_Client_ID = " + clientIdSql + @"
),
CurrentPeriod AS
(
    SELECT
        ClientInfo.AD_Client_ID,
        MIN(Period.StartDate) AS PeriodStartDate,
        MAX(Period.EndDate) AS PeriodEndDate
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
    AND ClientInfo.AD_Client_ID = " + clientIdSql + @"
    AND " + currentDateSql + @" BETWEEN
        Period.StartDate AND Period.EndDate
    GROUP BY
        ClientInfo.AD_Client_ID
),
SecuredPayment AS
(
" + paymentAccessSql + @"
),
FilteredPayment AS
(
    SELECT
        Payment.C_Payment_ID,
        Payment.AD_Client_ID,
        Payment.AD_Org_ID,
        Payment.DateTrx,
        Payment.C_Currency_ID,
        Payment.C_ConversionType_ID,
        Payment.PayAmt,
        Payment.VA009_PaymentMethod_ID
    FROM SecuredPayment Payment
    INNER JOIN CurrentPeriod PeriodRange ON
    (
        PeriodRange.AD_Client_ID =
        Payment.AD_Client_ID
    )
    WHERE Payment.DateTrx >= " + currentDateSql + @"
    AND Payment.DateTrx < " + upcomingDateToSql + @"
    AND Payment.DateTrx >=
        PeriodRange.PeriodStartDate
    AND Payment.DateTrx <
        " + periodEndExclusiveSql + @"
)
SELECT
    " + dateOnlySql + @" AS RunDate,
    COALESCE
    (
        Payment.VA009_PaymentMethod_ID,
        0
    ) AS VA009_PaymentMethod_ID,
    PaymentMethod.VA009_Name AS PaymentMethodName,
    COUNT(1) AS PaymentCount,
    ROUND
    (
        " + CastNumberSql(amountExpression) + @",
        CAST
        (
            COALESCE
            (
                MAX(SchemaCurrency.StdPrecision),
                2
            ) AS INTEGER
        )
    ) AS Amount,
    MAX(SchemaCurrency.C_Currency_ID) AS C_Currency_ID,
    MAX(SchemaCurrency.StdPrecision) AS StdPrecision,
    MAX(SchemaCurrency.ISO_Code) AS CurrencyISO,
    MAX(SchemaCurrency.CurSymbol) AS CurrencySymbol
FROM FilteredPayment Payment
INNER JOIN SchemaCurrency SchemaCurrency ON
(
    SchemaCurrency.AD_Client_ID =
    Payment.AD_Client_ID
)
LEFT OUTER JOIN VA009_PaymentMethod PaymentMethod ON
(
    PaymentMethod.VA009_PaymentMethod_ID =
    Payment.VA009_PaymentMethod_ID
)
GROUP BY
    " + dateOnlySql + @",
    COALESCE
    (
        Payment.VA009_PaymentMethod_ID,
        0
    ),
    PaymentMethod.VA009_Name
ORDER BY
    RunDate,
    PaymentMethod.VA009_Name";

            return sql;
        }

        #endregion

        #region Upcoming AP Run Details

        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetUpcomingAPRunDetails(
            string runDate,
            int paymentMethodId = 0)
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
                    "Invalid run date"
                );

                return Json(new
                {
                    success = false,
                    error = invalidDateMessage,
                    errorText = invalidDateMessage
                }, JsonRequestBehavior.AllowGet);
            }

            IDataReader reader = null;

            try
            {
                string clientIdSql = ctx.GetAD_Client_ID().ToString(
                    CultureInfo.InvariantCulture
                );

                string paymentMethodIdSql = Math.Max(
                    0,
                    paymentMethodId
                ).ToString(
                    CultureInfo.InvariantCulture
                );

                DateTime dateFrom = parsedRunDate.Date;
                DateTime dateTo = dateFrom.AddDays(1);

                /*
                 * Include C_DocType_ID so the Document Type can be
                 * returned to the popup instead of always returning zero.
                 */
                string paymentAccessSql = @"
SELECT
    Payment.C_Payment_ID,
    Payment.DocumentNo,
    Payment.AD_Client_ID,
    Payment.AD_Org_ID,
    Payment.C_BPartner_ID,
    Payment.C_BankAccount_ID,
    Payment.C_Currency_ID,
    Payment.C_ConversionType_ID,
    Payment.C_DocType_ID,
    Payment.DateTrx,
    Payment.PayAmt,
    Payment.TenderType,
    Payment.VA009_PaymentMethod_ID
FROM C_Payment Payment
WHERE Payment.IsActive = 'Y'
AND Payment.AD_Client_ID = " + clientIdSql + @"
AND Payment.IsReceipt = 'N'
AND Payment.DocStatus NOT IN ('VO', 'RE')"
+ GetDateFilter(
                    "Payment.DateTrx",
                    dateFrom,
                    dateTo
                ) + @"
AND COALESCE
(
    Payment.VA009_PaymentMethod_ID,
    0
) = " + paymentMethodIdSql;

                paymentAccessSql = MRole.GetDefault(ctx).AddAccessSQL(
                    paymentAccessSql,
                    "Payment",
                    MRole.SQL_FULLYQUALIFIED,
                    MRole.SQL_RO
                );

                string sql = @"
WITH FilteredPayment AS
(
" + paymentAccessSql + @"
)
SELECT
    Payment.C_Payment_ID,
    Payment.DocumentNo,
    Payment.AD_Org_ID,
    Organization.Name AS OrganizationName,
    Payment.C_BPartner_ID,
    BusinessPartner.Name AS VendorName,
    BusinessPartner.IsVendor,
    Payment.C_BankAccount_ID,
    Bank.Name AS BankName,
    BankAccount.Name AS BankAccountName,
    BankAccount.AccountNo AS BankAccountNo,
    Payment.C_Currency_ID,
    Currency.ISO_Code AS CurrencyISO,
    Currency.CurSymbol AS CurrencySymbol,
    Currency.StdPrecision,
    Payment.C_ConversionType_ID,
    ConversionType.Name AS CurrencyTypeName,
    Payment.C_DocType_ID,
    DocumentType.Name AS DocTypeName,
    Payment.DateTrx,
    Payment.PayAmt,
    Payment.TenderType,
    Payment.VA009_PaymentMethod_ID,
    PaymentMethod.VA009_Name AS PaymentMethodName
FROM FilteredPayment Payment
LEFT OUTER JOIN AD_Org Organization ON
(
    Organization.AD_Org_ID =
    Payment.AD_Org_ID
)
LEFT OUTER JOIN C_BPartner BusinessPartner ON
(
    BusinessPartner.C_BPartner_ID =
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
LEFT OUTER JOIN C_ConversionType ConversionType ON
(
    ConversionType.C_ConversionType_ID =
    Payment.C_ConversionType_ID
)
LEFT OUTER JOIN C_DocType DocumentType ON
(
    DocumentType.C_DocType_ID =
    Payment.C_DocType_ID
)
LEFT OUTER JOIN VA009_PaymentMethod PaymentMethod ON
(
    PaymentMethod.VA009_PaymentMethod_ID =
    Payment.VA009_PaymentMethod_ID
)
ORDER BY
    Payment.DateTrx,
    Payment.DocumentNo,
    Payment.C_Payment_ID";

                reader = DB.ExecuteReader(
                    sql,
                    null,
                    null
                );

                List<object> rows = new List<object>();

                while (reader != null && reader.Read())
                {
                    DateTime? dateTrx = GetNullableDateTime(
                        reader,
                        "DateTrx"
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

                    string bankName = GetString(
                        reader,
                        "BankName"
                    );

                    string bankAccountName = GetString(
                        reader,
                        "BankAccountName"
                    );

                    if (string.IsNullOrWhiteSpace(bankName))
                    {
                        bankName = bankAccountName;
                    }

                    int sourceBusinessPartnerId = GetInt(
                        reader,
                        "C_BPartner_ID"
                    );

                    bool isVendor = string.Equals(
                        GetString(
                            reader,
                            "IsVendor"
                        ),
                        "Y",
                        StringComparison.OrdinalIgnoreCase
                    );

                    /*
                     * Do not prefill a Business Partner that is not a Vendor.
                     * The original ID is still returned separately for review.
                     */
                    int validVendorId = isVendor
                        ? sourceBusinessPartnerId
                        : 0;

                    int documentTypeId = GetInt(
                        reader,
                        "C_DocType_ID"
                    );

                    string tenderType = GetString(
                        reader,
                        "TenderType"
                    );

                    string paymentMethodName = GetPaymentMethodName(
                        ctx,
                        GetString(
                            reader,
                            "PaymentMethodName"
                        )
                    );

                    rows.Add(new
                    {
                        paymentId = GetInt(
                            reader,
                            "C_Payment_ID"
                        ),

                        documentNo = GetString(
                            reader,
                            "DocumentNo"
                        ),

                        paymentDocumentNo = GetString(
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

                        vendorId = validVendorId,

                        cBPartnerId = validVendorId,

                        sourceBusinessPartnerId =
                            sourceBusinessPartnerId,

                        vendorName = GetString(
                            reader,
                            "VendorName"
                        ),

                        isVendor = isVendor,

                        bankAccountId = GetInt(
                            reader,
                            "C_BankAccount_ID"
                        ),

                        bankName = bankName,

                        bankAccountName =
                            bankAccountName,

                        bankAccountNo = GetString(
                            reader,
                            "BankAccountNo"
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

                        docTypeId = documentTypeId,

                        cDocTypeId = documentTypeId,

                        docTypeName = GetString(
                            reader,
                            "DocTypeName"
                        ),

                        transactionDate = FormatDate(
                            dateTrx
                        ),

                        dateTrx = FormatDate(
                            dateTrx
                        ),

                        amount = GetDecimal(
                            reader,
                            "PayAmt"
                        ),

                        payAmt = GetDecimal(
                            reader,
                            "PayAmt"
                        ),

                        tenderType = tenderType,

                        tenderTypeName = tenderType,

                        paymentMethodValue = tenderType,

                        paymentMethodId = GetInt(
                            reader,
                            "VA009_PaymentMethod_ID"
                        ),

                        paymentMethodName =
                            paymentMethodName
                    });
                }

                return Json(new
                {
                    success = true,
                    error = string.Empty,
                    rows = rows
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception)
            {
                string errorMessage = GetMsg(
                    ctx,
                    "VAS_ErrorLoading",
                    "Could not load data"
                );

                return Json(new
                {
                    success = false,
                    error = errorMessage,
                    errorText = errorMessage
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

                List<object> organizations =
                    ReadLookupRows(@"
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

                /*
                 * Only real, active vendors are returned.
                 * A customer/non-vendor cannot be submitted as AP Vendor.
                 */
                List<object> vendors =
                    ReadLookupRows(@"
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

                List<object> currencies =
                    ReadLookupRows(@"
SELECT
    Currency.C_Currency_ID AS ID,
    Currency.ISO_Code AS Name
FROM C_Currency Currency
WHERE Currency.IsActive = 'Y'
ORDER BY
    Currency.ISO_Code");

                currentLookup = "conversionTypes";

                List<object> conversionTypes =
                    ReadLookupRows(@"
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

                    paymentMethods = tenderTypes
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception)
            {
                string errorMessage = GetMsg(
                    ctx,
                    "VAS_ErrorLoading",
                    "Could not load lookup data"
                );

                return Json(new
                {
                    success = false,
                    error = errorMessage,
                    errorText = errorMessage,

                    /*
                     * Helps identify which backend list failed
                     * without exposing the database exception.
                     */
                    lookup = currentLookup
                }, JsonRequestBehavior.AllowGet);
            }
        }

        private List<object> ReadLookupRows(
            string sql)
        {
            List<object> rows =
                new List<object>();

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
            List<object> rows =
                new List<object>();

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

                        bankAccountName =
                            accountName,

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

        /// <summary>
        /// Reference SQL for AP Payment Document Type.
        /// Returns value/name pairs for the JavaScript select.
        /// </summary>
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
            List<object> rows =
                new List<object>();

            IDataReader reader = null;

            string languageSql = ToSqlString(
                ctx.GetAD_Language()
            );

            /*
             * Casting prevents Oracle character-set mismatch errors
             * while remaining compatible with PostgreSQL.
             */
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

        #endregion

        #region Create AP Payment

        /// <summary>
        /// Creates a new draft AP Payment.
        /// IsReceipt = false means AP Payment.
        /// </summary>
        [HttpPost]
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult CreateUpcomingAPPayment(
            int adOrgId,
            int bankAccountId,
            int vendorId,
            int currencyId,
            int conversionTypeId,
            int docTypeId,
            string tenderType,
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

            JsonResult validationResult =
                ValidateCreateRequest(
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

            DateTime.TryParseExact(
                transactionDate,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out dateTrx
            );

            string trxName =
                "CreateUpcomingAPPayment_" +
                ctx.GetAD_User_ID() +
                "_" +
                DateTime.Now.Ticks;

            Trx trx = Trx.GetTrx(
                trxName
            );

            try
            {
                MOrg organization = new MOrg(
                    ctx,
                    adOrgId,
                    trx
                );

                if (
                    organization.GetAD_Org_ID() <= 0
                )
                {
                    throw new InvalidOperationException(
                        GetMsg(
                            ctx,
                            "VAS_031_MessageOrganizationNotFound",
                            "Organization not found."
                        )
                    );
                }

                if (!organization.IsActive())
                {
                    throw new InvalidOperationException(
                        GetMsg(
                            ctx,
                            "VAS_031_MessageOrganizationInactive",
                            "Organization is inactive."
                        )
                    );
                }

                MBankAccount bankAccount =
                    new MBankAccount(
                        ctx,
                        bankAccountId,
                        trx
                    );

                if (
                    bankAccount
                        .GetC_BankAccount_ID() <= 0
                )
                {
                    throw new InvalidOperationException(
                        GetMsg(
                            ctx,
                            "VAS_031_MessageBankAccountNotFound",
                            "Bank account not found."
                        )
                    );
                }

                if (!bankAccount.IsActive())
                {
                    throw new InvalidOperationException(
                        GetMsg(
                            ctx,
                            "VAS_031_MessageBankAccountInactive",
                            "Bank account is inactive."
                        )
                    );
                }

                MBPartner vendor = new MBPartner(
                    ctx,
                    vendorId,
                    trx
                );

                if (
                    vendor.GetC_BPartner_ID() <= 0
                )
                {
                    throw new InvalidOperationException(
                        GetMsg(
                            ctx,
                            "VAS_031_MessageVendorNotFound",
                            "Vendor not found."
                        )
                    );
                }

                if (!vendor.IsActive())
                {
                    throw new InvalidOperationException(
                        GetMsg(
                            ctx,
                            "VAS_031_MessageVendorInactive",
                            "Vendor is inactive."
                        )
                    );
                }

                /*
                 * Keep this validation.
                 * AP Payments must use a Business Partner configured as Vendor.
                 */
                if (!vendor.IsVendor())
                {
                    throw new InvalidOperationException(
                        GetMsg(
                            ctx,
                            "VAS_031_MessageNotVendor",
                            "Selected business partner is not configured as a vendor."
                        )
                    );
                }

                MCurrency currency = new MCurrency(
                    ctx,
                    currencyId,
                    trx
                );

                if (
                    currency.GetC_Currency_ID() <= 0
                )
                {
                    throw new InvalidOperationException(
                        GetMsg(
                            ctx,
                            "VAS_031_MessageCurrencyNotFound",
                            "Currency not found."
                        )
                    );
                }

                if (!currency.IsActive())
                {
                    throw new InvalidOperationException(
                        GetMsg(
                            ctx,
                            "VAS_031_MessageCurrencyInactive",
                            "Currency is inactive."
                        )
                    );
                }

                MConversionType conversionType =
                    new MConversionType(
                        ctx,
                        conversionTypeId,
                        trx
                    );

                if (
                    conversionType
                        .GetC_ConversionType_ID() <= 0
                )
                {
                    throw new InvalidOperationException(
                        GetMsg(
                            ctx,
                            "VAS_031_MessageConversionTypeNotFound",
                            "Conversion type not found."
                        )
                    );
                }

                if (!conversionType.IsActive())
                {
                    throw new InvalidOperationException(
                        GetMsg(
                            ctx,
                            "VAS_031_MessageConversionTypeInactive",
                            "Conversion type is inactive."
                        )
                    );
                }

                int resolvedDocTypeId =
                    ResolveAPPaymentDocTypeId(
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
                            "VAS_031_MessageAPPaymentDocumentTypeNotFound",
                            "AP Payment document type was not found. Configure an active document type with DocBaseType APP."
                        )
                    );
                }

                MDocType documentType = new MDocType(
                    ctx,
                    resolvedDocTypeId,
                    trx
                );

                if (
                    documentType.GetC_DocType_ID() <= 0
                )
                {
                    throw new InvalidOperationException(
                        GetMsg(
                            ctx,
                            "VAS_031_MessageDocumentTypeNotFound",
                            "AP Payment document type was not found."
                        )
                    );
                }

                if (!documentType.IsActive())
                {
                    throw new InvalidOperationException(
                        GetMsg(
                            ctx,
                            "VAS_031_MessageDocumentTypeInactive",
                            "Document type is inactive."
                        )
                    );
                }

                if (!string.Equals(
                    documentType.GetDocBaseType(),
                    "APP",
                    StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        GetMsg(
                            ctx,
                            "VAS_031_MessageInvalidPaymentDocumentType",
                            "The selected document type is not an AP Payment document type."
                        )
                    );
                }

                if (!IsValidTenderType(
                    ctx,
                    tenderType
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

                MPayment payment = new MPayment(
                    ctx,
                    0,
                    trx
                );

                payment.SetAD_Org_ID(
                    adOrgId
                );

                /*
                 * false = AP Payment
                 * true  = AR Receipt
                 */
                payment.SetIsReceipt(
                    false
                );

                payment.SetC_DocType_ID(
                    resolvedDocTypeId
                );

                payment.SetC_BankAccount_ID(
                    bankAccountId
                );

                payment.SetC_BPartner_ID(
                    vendorId
                );

                payment.SetC_Currency_ID(
                    currencyId
                );

                payment.SetC_ConversionType_ID(
                    conversionTypeId
                );

                payment.SetDateTrx(
                    dateTrx
                );

                payment.SetDateAcct(
                    dateTrx
                );

                payment.SetTenderType(
                    tenderType.Trim()
                );

                payment.SetPayAmt(
                    payAmt
                );

                payment.SetDocStatus(
                    "DR"
                );

                payment.SetDocAction(
                    "CO"
                );

                payment.SetProcessed(
                    false
                );

                if (!string.IsNullOrWhiteSpace(
                    documentNo
                ))
                {
                    payment.SetDocumentNo(
                        documentNo.Trim()
                    );
                }

                if (!payment.Save())
                {
                    string modelError =
                        GetLastModelError(
                            ctx,
                            "VAS_031_MessageCouldNotSaveAPPayment",
                            "Could not save AP payment."
                        );

                    throw new InvalidOperationException(
                        modelError
                    );
                }

                trx.Commit();

                return Json(new
                {
                    success = true,
                    error = string.Empty,

                    paymentId =
                        payment.GetC_Payment_ID(),

                    documentNo =
                        payment.GetDocumentNo(),

                    docStatus =
                        payment.GetDocStatus(),

                    docTypeId =
                        resolvedDocTypeId,

                    docTypeName =
                        documentType.GetName(),

                    message = GetMsg(
                        ctx,
                        "VAS_031_MessagePaymentCreatedSuccessfully",
                        "Upcoming AP payment created successfully."
                    )
                });
            }
            catch (InvalidOperationException ex)
            {
                RollbackTransaction(
                    trx
                );

                return Json(new
                {
                    success = false,
                    error = ex.Message,
                    errorText = ex.Message
                });
            }
            catch (Exception)
            {
                RollbackTransaction(
                    trx
                );

                string errorMessage = GetMsg(
                    ctx,
                    "VAS_031_MessageCouldNotSaveAPPayment",
                    "Could not save AP payment."
                );

                return Json(new
                {
                    success = false,
                    error = errorMessage,
                    errorText = errorMessage
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

            if (string.IsNullOrWhiteSpace(
                tenderType
            ))
            {
                return GetValidationError(
                    ctx,
                    "VAS_031_MessageTenderTypeRequired",
                    "Tender type is required."
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
            string errorMessage = GetMsg(
                ctx,
                messageKey,
                fallback
            );

            return Json(new
            {
                success = false,
                error = errorMessage,
                errorText = errorMessage
            });
        }

        private int ResolveAPPaymentDocTypeId(
            Ctx ctx,
            int requestedDocTypeId,
            int adOrgId,
            Trx trx)
        {
            int clientId = ctx.GetAD_Client_ID();

            string clientIdSql = clientId.ToString(
                CultureInfo.InvariantCulture
            );

            string orgIdSql = Math.Max(
                0,
                adOrgId
            ).ToString(
                CultureInfo.InvariantCulture
            );

            string requestedIdSql = Math.Max(
                0,
                requestedDocTypeId
            ).ToString(
                CultureInfo.InvariantCulture
            );

            string sql = @"
SELECT
    DocumentType.C_DocType_ID
FROM C_DocType DocumentType
WHERE DocumentType.IsActive = 'Y'
AND DocumentType.DocBaseType = 'APP'
AND DocumentType.IsSOTrx = 'N'
AND DocumentType.AD_Client_ID IN
(
    0,
    " + clientIdSql + @"
)
AND DocumentType.AD_Org_ID IN
(
    0,
    " + orgIdSql + @"
)
ORDER BY
    CASE
        WHEN DocumentType.C_DocType_ID =
             " + requestedIdSql + @"
        THEN 0
        ELSE 1
    END,
    CASE
        WHEN DocumentType.AD_Org_ID =
             " + orgIdSql + @"
        THEN 0
        ELSE 1
    END,
    DocumentType.Name,
    DocumentType.C_DocType_ID";

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
                    return GetInt(
                        reader,
                        "C_DocType_ID"
                    );
                }
            }
            finally
            {
                CloseReader(reader);
            }

            return 0;
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
            string dateFromText = dateFrom.ToString(
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture
            );

            string dateToText = dateTo.ToString(
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture
            );

            if (DB.IsOracle())
            {
                return @"
AND " + columnName + @" >= TO_DATE('" +
                    dateFromText + @"', 'YYYY-MM-DD')
AND " + columnName + @" < TO_DATE('" +
                    dateToText + @"', 'YYYY-MM-DD')";
            }

            return @"
AND " + columnName + @" >= DATE '" +
                dateFromText + @"'
AND " + columnName + @" < DATE '" +
                dateToText + @"'";
        }

        private string GetDateOnlySql(
            string columnExpression)
        {
            return DB.IsOracle()
                ? "TRUNC(" +
                  columnExpression +
                  ")"
                : "CAST(" +
                  columnExpression +
                  " AS DATE)";
        }

        private string CastNumberSql(
            string expression)
        {
            return DB.IsOracle()
                ? "CAST(" +
                  expression +
                  " AS NUMBER)"
                : "CAST(" +
                  expression +
                  " AS NUMERIC)";
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

            return Util.GetValueOfInt(
                value
            );
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

            return Util.GetValueOfDecimal(
                value
            );
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

            return Util.GetValueOfString(
                value
            );
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

            return Util.GetValueOfDateTime(
                value
            );
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
            int paymentCount)
        {
            return paymentCount == 1
                ? GetMsg(
                    ctx,
                    "VAS_031_MessagePayment",
                    "payment"
                )
                : GetMsg(
                    ctx,
                    "VAS_031_MessagePayments",
                    "payments"
                );
        }

        private string GetPaymentMethodName(
            Ctx ctx,
            string paymentMethodName)
        {
            return string.IsNullOrWhiteSpace(
                paymentMethodName
            )
                ? GetMsg(
                    ctx,
                    "VAS_031_MessageNotSpecified",
                    "Not Specified"
                )
                : paymentMethodName;
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
                    modelError =
                        loggerError.GetName();
                }
            }
            catch
            {
                modelError = GetMsg(
                    ctx,
                    messageKey,
                    fallback
                );
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

            string msg = Msg.GetMsg(
                ctx,
                key
            );

            if (
                string.IsNullOrWhiteSpace(msg) ||
                msg == key ||
                msg == "[" + key + "]"
            )
            {
                return fallback;
            }

            return msg;
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