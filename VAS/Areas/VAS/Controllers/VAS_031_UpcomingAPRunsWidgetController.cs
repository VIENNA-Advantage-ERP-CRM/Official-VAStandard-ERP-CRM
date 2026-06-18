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
    /*
     * Labels / Message Keys
     * 1 | Upcoming runs | VAS_033_MessageUpcomingRuns
     * 2 | Next 7 days   | VAS_033_MessageNext7Days
     * 3 | payment       | VAS_033_MessagePayment
     * 4 | payments      | VAS_033_MessagePayments
     * 5 | Ytd           | VAS_033_MessageYTD
     * 6 | Month         | VAS_033_MessageMonth
     */
    public class VAS_033_UpcomingAPRunsWidgetController : Controller
    {
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetUpcomingAPRuns()
        {
            /*
             * The widget displays upcoming payments,
             * therefore the default filter is the next seven days.
             */
            string dateFilter = "NEXT7D";

            Ctx ctx = GetContext();

            if (ctx == null)
            {
                return GetSessionExpiredResult();
            }

            IDataReader dr = null;
            string sql = string.Empty;

            try
            {
                sql = BuildUpcomingAPRunsSql(
                    ctx,
                    dateFilter
                );

                dr = DB.ExecuteReader(
                    sql,
                    null,
                    null
                );

                List<object> runs =
                    new List<object>();

                while (
                    dr != null &&
                    dr.Read() &&
                    runs.Count < 30
                )
                {
                    DateTime? runDate =
                        GetNullableDate(
                            dr,
                            "RunDate"
                        );

                    int paymentCount =
                        GetInt(
                            dr,
                            "PaymentCount"
                        );

                    int stdPrecision =
                        NormalizePrecision(
                            GetInt(
                                dr,
                                "StdPrecision",
                                2
                            )
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

                    string paymentMethodName =
                        GetPaymentMethodName(
                            ctx,
                            GetString(
                                dr,
                                "PaymentMethodName",
                                string.Empty
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

                    runs.Add(
                        new
                        {
                            paymentMethodId =
                                GetInt(
                                    dr,
                                    "VA009_PaymentMethod_ID"
                                ),

                            paymentMethodName =
                                paymentMethodName,

                            runDate =
                                FormatDate(runDate),

                            runDateText =
                                FormatRunDate(runDate),

                            paymentCount =
                                paymentCount,

                            paymentCountText =
                                GetPaymentCountText(
                                    ctx,
                                    paymentCount
                                ),

                            amount = amount,
                            totalAmount = amount,

                            cCurrencyId =
                                GetInt(
                                    dr,
                                    "C_Currency_ID"
                                ),

                            currencyISO =
                                currencyISO,

                            currencySymbol =
                                currencySymbol,

                            stdPrecision =
                                stdPrecision
                        }
                    );
                }

                return Json(
                    new
                    {
                        title = GetMsg(
                            ctx,
                            "VAS_033_MessageUpcomingRuns",
                            "Upcoming runs"
                        ),

                        periodText =
                            GetPeriodText(
                                ctx,
                                dateFilter
                            ),

                        runs = runs
                    },
                    JsonRequestBehavior.AllowGet
                );
            }
            catch (Exception ex)
            {
                return Json(
                    new
                    {
                        error = ex.Message,
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

        private string BuildUpcomingAPRunsSql(
            Ctx ctx,
            string dateFilter
        )
        {
            string dateCondition =
                GetDateConditionSql(dateFilter);

            int clientId =
                ctx.GetAD_Client_ID();

            string clientIdSql =
                clientId.ToString(
                    CultureInfo.InvariantCulture
                );

            bool hasPaymentMethod =
                HasPaymentMethodColumn();

            string paymentMethodNameColumn =
                hasPaymentMethod
                    ? GetPaymentMethodNameColumn("pm")
                    : string.Empty;

            bool usePaymentMethodTable =
                hasPaymentMethod &&
                !string.IsNullOrWhiteSpace(
                    paymentMethodNameColumn
                );

            string paymentMethodIdSql =
                hasPaymentMethod
                    ? "COALESCE(p.VA009_PaymentMethod_ID, 0)"
                    : "0";

            string paymentMethodNameSql =
                usePaymentMethodTable
                    ? paymentMethodNameColumn
                    : "NULL";

            string paymentMethodJoin =
                usePaymentMethodTable
                    ? @"
LEFT OUTER JOIN VA009_PaymentMethod pm ON
(
    pm.VA009_PaymentMethod_ID =
    p.VA009_PaymentMethod_ID
)"
                    : string.Empty;

            /*
             * Oracle DATE can contain a time component, so TRUNC is required.
             * PostgreSQL CAST(timestamp AS DATE) removes the time component.
             */
            string runDateSql =
                GetDateOnlySql(
                    "p.DateTrx"
                );

            string schemaCurrencyCte = @"
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

            string paymentAccessSql = @"
SELECT
    p.C_Payment_ID

FROM C_Payment p

WHERE p.IsActive = 'Y'

AND p.IsReceipt = 'N'

AND p.AD_Client_ID =
    " + clientIdSql + @"

" + dateCondition + @"

AND p.DocStatus NOT IN
(
    'VO',
    'RE'
)";

            paymentAccessSql =
                MRole.GetDefault(ctx).AddAccessSQL(
                    paymentAccessSql,
                    "p",
                    MRole.SQL_FULLYQUALIFIED,
                    MRole.SQL_RO
                );

            string finalSql = @"
WITH
" + schemaCurrencyCte + @",

FilteredPayment AS
(
" + paymentAccessSql + @"
)

SELECT
    " + runDateSql + @" AS RunDate,

    " + paymentMethodIdSql + @"
        AS VA009_PaymentMethod_ID,

    " + paymentMethodNameSql + @"
        AS PaymentMethodName,

    COUNT(1) AS PaymentCount,

    ROUND
    (
        COALESCE
        (
            SUM
            (
                CASE
                    WHEN p.C_Currency_ID =
                         SchemaCurrency.C_Currency_ID

                    THEN COALESCE(
                        p.PayAmt,
                        0
                    )

                    ELSE CurrencyConvert
                    (
                        COALESCE(
                            p.PayAmt,
                            0
                        ),
                        p.C_Currency_ID,
                        SchemaCurrency.C_Currency_ID,
                        p.DateTrx,
                        p.C_ConversionType_ID,
                        p.AD_Client_ID,
                        p.AD_Org_ID
                    )
                END
            ),
            0
        ),

        CAST
        (
            COALESCE
            (
                MAX(
                    SchemaCurrency.StdPrecision
                ),
                2
            )
            AS INTEGER
        )
    ) AS Amount,

    MAX(
        SchemaCurrency.C_Currency_ID
    ) AS C_Currency_ID,

    MAX(
        SchemaCurrency.StdPrecision
    ) AS StdPrecision,

    MAX(
        SchemaCurrency.ISO_Code
    ) AS CurrencyISO,

    MAX(
        SchemaCurrency.CurSymbol
    ) AS CurrencySymbol

FROM C_Payment p

INNER JOIN FilteredPayment fp ON
(
    fp.C_Payment_ID =
    p.C_Payment_ID
)

INNER JOIN SchemaCurrency SchemaCurrency ON
(
    SchemaCurrency.AD_Client_ID =
    p.AD_Client_ID
)

" + paymentMethodJoin + @"

GROUP BY
    " + runDateSql + @",

    " + paymentMethodIdSql + @",

    " + paymentMethodNameSql + @"

ORDER BY
    RunDate ASC,
    PaymentMethodName ASC";

            return finalSql;
        }

        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetUpcomingAPRunDetails(
            string runDate,
            int paymentMethodId = 0
        )
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
                return Json(
                    new
                    {
                        error = true,
                        errorText = "Invalid run date"
                    },
                    JsonRequestBehavior.AllowGet
                );
            }

            IDataReader dr = null;
            string sql = string.Empty;

            try
            {
                sql = BuildUpcomingAPRunDetailsSql(
                    ctx,
                    parsedRunDate,
                    paymentMethodId
                );

                dr = DB.ExecuteReader(
                    sql,
                    null,
                    null
                );

                List<object> rows =
                    new List<object>();

                while (
                    dr != null &&
                    dr.Read()
                )
                {
                    DateTime? dateTrx =
                        GetNullableDate(
                            dr,
                            "DateTrx"
                        );

                    string bankName =
                        GetString(
                            dr,
                            "BankName",
                            string.Empty
                        );

                    string bankAccountName =
                        GetString(
                            dr,
                            "BankAccountName",
                            string.Empty
                        );

                    if (
                        string.IsNullOrWhiteSpace(
                            bankName
                        )
                    )
                    {
                        bankName =
                            bankAccountName;
                    }

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

                    int stdPrecision =
                        NormalizePrecision(
                            GetInt(
                                dr,
                                "StdPrecision",
                                2
                            )
                        );

                    decimal amount = Math.Round(
                        GetDecimal(
                            dr,
                            "PayAmt",
                            0
                        ),
                        stdPrecision,
                        MidpointRounding.AwayFromZero
                    );

                    rows.Add(
                        new
                        {
                            paymentId =
                                GetInt(
                                    dr,
                                    "C_Payment_ID"
                                ),

                            documentNo =
                                GetString(
                                    dr,
                                    "DocumentNo",
                                    string.Empty
                                ),

                            organizationId =
                                GetInt(
                                    dr,
                                    "AD_Org_ID"
                                ),

                            organizationName =
                                GetString(
                                    dr,
                                    "OrganizationName",
                                    string.Empty
                                ),

                            vendorId =
                                GetInt(
                                    dr,
                                    "C_BPartner_ID"
                                ),

                            vendorName =
                                GetString(
                                    dr,
                                    "VendorName",
                                    string.Empty
                                ),

                            bankAccountId =
                                GetInt(
                                    dr,
                                    "C_BankAccount_ID"
                                ),

                            bankName =
                                bankName,

                            bankAccountName =
                                bankAccountName,

                            bankAccountNo =
                                GetString(
                                    dr,
                                    "BankAccountNo",
                                    string.Empty
                                ),

                            cCurrencyId =
                                GetInt(
                                    dr,
                                    "C_Currency_ID"
                                ),

                            currencyISO =
                                currencyISO,

                            currencySymbol =
                                currencySymbol,

                            stdPrecision =
                                stdPrecision,

                            conversionTypeId =
                                GetInt(
                                    dr,
                                    "C_ConversionType_ID"
                                ),

                            currencyTypeName =
                                GetString(
                                    dr,
                                    "CurrencyTypeName",
                                    string.Empty
                                ),

                            docTypeId =
                                GetInt(
                                    dr,
                                    "C_DocType_ID"
                                ),

                            docTypeName =
                                GetString(
                                    dr,
                                    "DocTypeName",
                                    string.Empty
                                ),

                            tenderType =
                                GetString(
                                    dr,
                                    "TenderType",
                                    string.Empty
                                ),

                            transactionDate =
                                dateTrx.HasValue
                                    ? dateTrx.Value.ToString(
                                        "yyyy-MM-dd",
                                        CultureInfo.InvariantCulture
                                    )
                                    : string.Empty,

                            amount = amount,

                            paymentMethodId =
                                GetInt(
                                    dr,
                                    "VA009_PaymentMethod_ID"
                                ),

                            paymentMethodName =
                                GetPaymentMethodName(
                                    ctx,
                                    GetString(
                                        dr,
                                        "PaymentMethodName",
                                        string.Empty
                                    )
                                )
                        }
                    );
                }

                return Json(
                    new
                    {
                        rows = rows
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

        private string BuildUpcomingAPRunDetailsSql(
            Ctx ctx,
            DateTime runDate,
            int paymentMethodId
        )
        {
            string clientIdSql =
                ctx.GetAD_Client_ID().ToString(
                    CultureInfo.InvariantCulture
                );

            string paymentMethodIdSql =
                paymentMethodId.ToString(
                    CultureInfo.InvariantCulture
                );

            bool hasPaymentMethod =
                HasPaymentMethodColumn();

            string paymentMethodNameColumn =
                hasPaymentMethod
                    ? GetPaymentMethodNameColumn("pm")
                    : string.Empty;

            bool usePaymentMethodTable =
                hasPaymentMethod &&
                !string.IsNullOrWhiteSpace(
                    paymentMethodNameColumn
                );

            string paymentMethodFilter =
                hasPaymentMethod
                    ? @"
AND COALESCE(
    p.VA009_PaymentMethod_ID,
    0
) =
    " + paymentMethodIdSql
                    : @"
AND 0 =
    " + paymentMethodIdSql;

            string paymentMethodIdColumn =
                hasPaymentMethod
                    ? "p.VA009_PaymentMethod_ID"
                    : "0 AS VA009_PaymentMethod_ID";

            string paymentMethodNameSql =
                usePaymentMethodTable
                    ? paymentMethodNameColumn
                    : "NULL";

            string paymentMethodJoin =
                usePaymentMethodTable
                    ? @"
LEFT OUTER JOIN VA009_PaymentMethod pm ON
(
    pm.VA009_PaymentMethod_ID =
    p.VA009_PaymentMethod_ID
)"
                    : string.Empty;

            string paymentAccessSql = @"
SELECT
    p.C_Payment_ID

FROM C_Payment p

WHERE p.IsActive = 'Y'

AND p.IsReceipt = 'N'

AND p.AD_Client_ID =
    " + clientIdSql + @"

AND p.DocStatus NOT IN
(
    'VO',
    'RE'
)

AND p.DateTrx >=
    " + ToSqlDate(runDate) + @"

AND p.DateTrx <
    " + ToSqlDate(
        runDate.AddDays(1)
    ) + @"

" + paymentMethodFilter;

            paymentAccessSql =
                MRole.GetDefault(ctx).AddAccessSQL(
                    paymentAccessSql,
                    "p",
                    MRole.SQL_FULLYQUALIFIED,
                    MRole.SQL_RO
                );

            string sql = @"
WITH FilteredPayment AS
(
" + paymentAccessSql + @"
)

SELECT
    p.C_Payment_ID,

    p.DocumentNo,

    p.AD_Org_ID,

    org.Name AS OrganizationName,

    p.C_BPartner_ID,

    bp.Name AS VendorName,

    p.C_BankAccount_ID,

    bank.Name AS BankName,

    ba.Name AS BankAccountName,

    ba.AccountNo AS BankAccountNo,

    p.C_Currency_ID,

    cur.ISO_Code AS CurrencyISO,

    cur.CurSymbol AS CurrencySymbol,

    cur.StdPrecision,

    p.C_ConversionType_ID,

    ct.Name AS CurrencyTypeName,

    p.C_DocType_ID,

    dt.Name AS DocTypeName,

    p.TenderType,

    p.DateTrx,

    p.PayAmt,

    " + paymentMethodIdColumn + @",

    " + paymentMethodNameSql + @"
        AS PaymentMethodName

FROM C_Payment p

INNER JOIN FilteredPayment fp ON
(
    fp.C_Payment_ID =
    p.C_Payment_ID
)

LEFT OUTER JOIN AD_Org org ON
(
    org.AD_Org_ID =
    p.AD_Org_ID
)

LEFT OUTER JOIN C_BPartner bp ON
(
    bp.C_BPartner_ID =
    p.C_BPartner_ID
)

LEFT OUTER JOIN C_BankAccount ba ON
(
    ba.C_BankAccount_ID =
    p.C_BankAccount_ID
)

LEFT OUTER JOIN C_Bank bank ON
(
    bank.C_Bank_ID =
    ba.C_Bank_ID
)

LEFT OUTER JOIN C_Currency cur ON
(
    cur.C_Currency_ID =
    p.C_Currency_ID
)

LEFT OUTER JOIN C_ConversionType ct ON
(
    ct.C_ConversionType_ID =
    p.C_ConversionType_ID
)

LEFT OUTER JOIN C_DocType dt ON
(
    dt.C_DocType_ID =
    p.C_DocType_ID
)

" + paymentMethodJoin + @"

ORDER BY
    p.DateTrx ASC,

    p.DocumentNo ASC,

    p.C_Payment_ID ASC";

            return sql;
        }

        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetPaymentPopupLookups()
        {
            Ctx ctx = GetContext();

            if (ctx == null)
            {
                return GetSessionExpiredResult();
            }

            try
            {
                string clientIdSql =
                    ctx.GetAD_Client_ID().ToString(
                        CultureInfo.InvariantCulture
                    );

                List<object> documentTypes =
                    ReadLookupRows(@"
SELECT
    C_DocType_ID AS ID,
    Name

FROM C_DocType

WHERE IsActive = 'Y'

AND AD_Client_ID IN
(
    0,
    " + clientIdSql + @"
)

AND DocBaseType = 'APP'

ORDER BY
    Name");

                return Json(
                    new
                    {
                        organizations =
                            ReadLookupRows(@"
SELECT
    AD_Org_ID AS ID,
    Name

FROM AD_Org

WHERE IsActive = 'Y'

AND AD_Client_ID IN
(
    0,
    " + clientIdSql + @"
)

ORDER BY
    Name"),

                        bankAccounts =
                            ReadLookupRows(@"
SELECT
    ba.C_BankAccount_ID AS ID,

    ba.Name AS Name,

    ba.Name AS AccountName,

    bank.Name AS BankName,

    ba.AccountNo

FROM C_BankAccount ba

LEFT OUTER JOIN C_Bank bank ON
(
    bank.C_Bank_ID =
    ba.C_Bank_ID
)

WHERE ba.IsActive = 'Y'

AND ba.AD_Client_ID IN
(
    0,
    " + clientIdSql + @"
)

ORDER BY
    ba.Name"),

                        vendors =
                            ReadLookupRows(@"
SELECT
    C_BPartner_ID AS ID,
    Name

FROM C_BPartner

WHERE IsActive = 'Y'

AND IsVendor = 'Y'

AND IsSummary = 'N'

AND AD_Client_ID IN
(
    0,
    " + clientIdSql + @"
)

ORDER BY
    Name"),

                        currencies =
                            ReadLookupRows(@"
SELECT
    C_Currency_ID AS ID,

    ISO_Code AS Name

FROM C_Currency

WHERE IsActive = 'Y'

ORDER BY
    ISO_Code"),

                        conversionTypes =
                            ReadLookupRows(@"
SELECT
    C_ConversionType_ID AS ID,

    Name

FROM C_ConversionType

WHERE IsActive = 'Y'

AND AD_Client_ID IN
(
    0,
    " + clientIdSql + @"
)

ORDER BY
    Name"),

                        documentTypes =
                            documentTypes,

                        docTypes =
                            documentTypes,

                        tenderTypes =
                            ReadReferenceLookupRows(
                                "C_Payment TenderType",
                                ctx
                            )
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
        }

        /// <summary>
        /// Creates a new upcoming AP payment as Draft.
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
                return Json(
                    new
                    {
                        success = false,
                        error = GetMsg(Env.GetCtx(), "SessionExpired", "Session Expired")
                    }
                );
            }

            if (adOrgId <= 0)
            {
                return Json(
                    new
                    {
                        success = false,
                        error = GetMsg(ctx, "VAS_031_MessageOrganizationRequired", "Organization is required.")
                    }
                );
            }

            if (bankAccountId <= 0)
            {
                return Json(
                    new
                    {
                        success = false,
                        error = GetMsg(ctx, "VAS_031_MessageBankAccountRequired", "Bank account is required.")
                    }
                );
            }

            if (vendorId <= 0)
            {
                return Json(
                    new
                    {
                        success = false,
                        error = GetMsg(ctx, "VAS_031_MessageVendorRequired", "Vendor is required.")
                    }
                );
            }

            if (currencyId <= 0)
            {
                return Json(
                    new
                    {
                        success = false,
                        error = GetMsg(ctx, "VAS_031_MessageCurrencyRequired", "Currency is required.")
                    }
                );
            }

            if (conversionTypeId <= 0)
            {
                return Json(
                    new
                    {
                        success = false,
                        error = GetMsg(ctx, "VAS_031_MessageConversionTypeRequired", "Currency type is required.")
                    }
                );
            }

            if (docTypeId <= 0)
            {
                return Json(
                    new
                    {
                        success = false,
                        error = GetMsg(ctx, "VAS_031_MessageDocumentTypeRequired", "Document type is required.")
                    }
                );
            }

            if (
                string.IsNullOrWhiteSpace(
                    tenderType
                )
            )
            {
                return Json(
                    new
                    {
                        success = false,
                        error = GetMsg(ctx, "VAS_031_MessageTenderTypeRequired", "Tender type is required.")
                    }
                );
            }

            if (payAmt <= 0)
            {
                return Json(
                    new
                    {
                        success = false,
                        error = GetMsg(ctx, "VAS_031_MessagePaymentAmountRequired", "Payment amount must be greater than zero.")
                    }
                );
            }

            DateTime dateTrx;

            if (!DateTime.TryParseExact(
                transactionDate,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out dateTrx))
            {
                return Json(
                    new
                    {
                        success = false,
                        error = GetMsg(ctx, "VAS_031_MessageTransactionDateInvalid", "Transaction date must be in yyyy-MM-dd format.")
                    }
                );
            }

            string trxN =
                "CreateUpcomingAPPayment_" +
                ctx.GetAD_User_ID() +
                "_" +
                DateTime.Now.Ticks;

            Trx trx =
                Trx.GetTrx(trxN);


            Trx trxName =
                Trx.GetTrx(trxN);

            try
            {
                MOrg organization =
                    new MOrg(
                        ctx,
                        adOrgId,
                        trxName
                    );

                if (
                    organization.GetAD_Org_ID() <= 0
                )
                {
                    throw new Exception(
                        "Organization not found"
                    );
                }

                if (!organization.IsActive())
                {
                    throw new Exception(
                        "Organization is inactive"
                    );
                }

                MBankAccount bankAccount =
                    new MBankAccount(
                        ctx,
                        bankAccountId,
                        trxName
                    );

                if (
                    bankAccount.GetC_BankAccount_ID() <= 0
                )
                {
                    throw new Exception(
                        "Bank account not found"
                    );
                }

                if (!bankAccount.IsActive())
                {
                    throw new Exception(
                        "Bank account is inactive"
                    );
                }

                MBPartner vendor =
                    new MBPartner(
                        ctx,
                        vendorId,
                        trxName
                    );

                if (
                    vendor.GetC_BPartner_ID() <= 0
                )
                {
                    throw new Exception(
                        "Vendor not found"
                    );
                }

                if (!vendor.IsActive())
                {
                    throw new Exception(
                        "Vendor is inactive"
                    );
                }

                if (!vendor.IsVendor())
                {
                    throw new Exception(
                        "Selected business partner is not configured as a vendor"
                    );
                }

                MCurrency currency =
                    new MCurrency(
                        ctx,
                        currencyId,
                        trxName
                    );

                if (
                    currency.GetC_Currency_ID() <= 0
                )
                {
                    throw new Exception(
                        "Currency not found"
                    );
                }

                if (!currency.IsActive())
                {
                    throw new Exception(
                        "Currency is inactive"
                    );
                }

                MConversionType conversionType =
                    new MConversionType(
                        ctx,
                        conversionTypeId,
                        trxName
                    );

                if (
                    conversionType
                        .GetC_ConversionType_ID() <= 0
                )
                {
                    throw new Exception(
                        "Conversion type not found"
                    );
                }

                if (!conversionType.IsActive())
                {
                    throw new Exception(
                        "Conversion type is inactive"
                    );
                }

                MDocType docType =
                    new MDocType(
                        ctx,
                        docTypeId,
                        trxName
                    );

                if (
                    docType.GetC_DocType_ID() <= 0
                )
                {
                    throw new Exception(
                        "Document type not found"
                    );
                }

                if (!docType.IsActive())
                {
                    throw new Exception(
                        "Document type is inactive"
                    );
                }

                MPayment payment =
                    new MPayment(
                        ctx,
                        0,
                        trxName
                    );

                payment.SetAD_Org_ID(adOrgId);

                /*
                 * false = AP Payment
                 * true  = AR Receipt
                 */
                payment.SetIsReceipt(false);

                payment.SetC_DocType_ID(
                    docTypeId
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

                payment.SetDateTrx(dateTrx);
                payment.SetDateAcct(dateTrx);

                payment.SetTenderType(
                    tenderType.Trim()
                );

                payment.SetPayAmt(payAmt);

                /*
                 * Keep the upcoming payment as Draft.
                 */
                payment.SetDocStatus("DR");
                payment.SetDocAction("CO");
                payment.SetProcessed(false);

                if (
                    !string.IsNullOrWhiteSpace(
                        documentNo
                    )
                )
                {
                    payment.SetDocumentNo(
                        documentNo.Trim()
                    );
                }

                if (!payment.Save())
                {
                    string modelError =
                        GetMsg(ctx, "VAS_031_MessageCouldNotSaveAPPayment", "Could not save AP payment");

                    try
                    {
                        var loggerError =
                            VLogger.RetrieveError();

                        if (loggerError != null)
                        {
                            string retrievedError =
                                loggerError.GetName();

                            if (
                                !string.IsNullOrWhiteSpace(
                                    retrievedError
                                )
                            )
                            {
                                modelError =
                                    retrievedError;
                            }
                        }
                    }
                    catch
                    {
                        modelError =
                            GetMsg(ctx, "VAS_031_MessageCouldNotSaveAPPayment", "Could not save AP payment");
                    }

                    throw new Exception(
                        modelError
                    );
                }

                trx.Commit();

                return Json(
                    new
                    {
                        success = true,

                        paymentId =
                            payment.GetC_Payment_ID(),

                        documentNo =
                            payment.GetDocumentNo(),

                        docStatus =
                            payment.GetDocStatus(),

                        message =
                            GetMsg(ctx, "VAS_031_MessagePaymentCreatedSuccessfully", "Upcoming AP payment created successfully")
                    }
                );
            }
            catch (Exception ex)
            {
                if (trx != null)
                {
                    trx.Rollback();
                }

                return Json(
                    new
                    {
                        success = false,
                        error = ex.Message
                    }
                );
            }
            finally
            {
                if (trx != null)
                {
                    trx.Close();
                }
            }
        }

        private List<object> ReadLookupRows(
            string sql
        )
        {
            IDataReader dr = null;

            List<object> rows =
                new List<object>();

            try
            {
                dr = DB.ExecuteReader(
                    sql,
                    null,
                    null
                );

                while (
                    dr != null &&
                    dr.Read()
                )
                {
                    string name =
                        HasReaderColumn(
                            dr,
                            "Name"
                        )
                            ? GetString(
                                dr,
                                "Name",
                                string.Empty
                            )
                            : string.Empty;

                    if (
                        HasReaderColumn(
                            dr,
                            "BankName"
                        ) ||
                        HasReaderColumn(
                            dr,
                            "AccountName"
                        )
                    )
                    {
                        string bankName =
                            HasReaderColumn(
                                dr,
                                "BankName"
                            )
                                ? GetString(
                                    dr,
                                    "BankName",
                                    string.Empty
                                )
                                : string.Empty;

                        string accountName =
                            HasReaderColumn(
                                dr,
                                "AccountName"
                            )
                                ? GetString(
                                    dr,
                                    "AccountName",
                                    string.Empty
                                )
                                : string.Empty;

                        string accountNo =
                            HasReaderColumn(
                                dr,
                                "AccountNo"
                            )
                                ? GetString(
                                    dr,
                                    "AccountNo",
                                    string.Empty
                                )
                                : string.Empty;

                        string accountTail =
                            GetAccountTail(
                                accountNo
                            );

                        name = FirstNotEmpty(
                            bankName,
                            accountName,
                            name
                        );

                        if (
                            !string.IsNullOrWhiteSpace(
                                accountTail
                            )
                        )
                        {
                            name =
                                name +
                                " · ****" +
                                accountTail;
                        }
                    }

                    rows.Add(
                        new
                        {
                            id = GetInt(
                                dr,
                                "ID"
                            ),

                            name = name
                        }
                    );
                }
            }
            finally
            {
                CloseReader(dr);
            }

            return rows;
        }

        private List<object> ReadReferenceLookupRows(
            string referenceName,
            Ctx ctx
        )
        {
            IDataReader dr = null;

            List<object> rows =
                new List<object>();

            string sql = @"
SELECT
    RefList.Value,

    RefList.Name,

    RefListTrl.Name AS TranslatedName

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
        " + ToSqlString(
            ctx.GetAD_Language()
        ) + @"
)

WHERE ReferenceInfo.Name =
    " + ToSqlString(
            referenceName
        ) + @"

AND ReferenceInfo.IsActive = 'Y'

AND RefList.IsActive = 'Y'

ORDER BY
    RefList.Name";

            try
            {
                dr = DB.ExecuteReader(
                    sql,
                    null,
                    null
                );

                while (
                    dr != null &&
                    dr.Read()
                )
                {
                    string value =
                        GetString(
                            dr,
                            "Value",
                            string.Empty
                        );

                    string name =
                        FirstNotEmpty(
                            GetString(
                                dr,
                                "TranslatedName",
                                string.Empty
                            ),

                            GetString(
                                dr,
                                "Name",
                                string.Empty
                            ),

                            value
                        );

                    rows.Add(
                        new
                        {
                            id = value,
                            value = value,
                            name = name
                        }
                    );
                }
            }
            finally
            {
                CloseReader(dr);
            }

            return rows;
        }

        private string GetDateConditionSql(
            string dateFilter
        )
        {
            dateFilter =
                string.IsNullOrWhiteSpace(
                    dateFilter
                )
                    ? "NEXT7D"
                    : dateFilter.ToUpperInvariant();

            if (dateFilter == "YTD")
            {
                return @"
AND p.DateTrx >=
(
    SELECT
        MIN(
            PeriodInYear.StartDate
        )

    FROM AD_ClientInfo ClientInfo

    INNER JOIN C_Year CurrentYear ON
    (
        CurrentYear.C_Calendar_ID =
        ClientInfo.C_Calendar_ID
    )

    INNER JOIN C_Period CurrentPeriod ON
    (
        CurrentPeriod.C_Year_ID =
        CurrentYear.C_Year_ID
    )

    INNER JOIN C_Period PeriodInYear ON
    (
        PeriodInYear.C_Year_ID =
        CurrentPeriod.C_Year_ID
    )

    WHERE ClientInfo.AD_Client_ID =
        p.AD_Client_ID

    AND ClientInfo.IsActive = 'Y'

    AND CurrentPeriod.IsActive = 'Y'

    AND PeriodInYear.IsActive = 'Y'

    AND " + GetCurrentDateSql() + @" >=
        CurrentPeriod.StartDate

    AND " + GetCurrentDateSql() + @" <
        " + GetDateToExclusiveSql(
            "CurrentPeriod.EndDate"
        ) + @"
)

AND p.DateTrx <
    " + GetDateAddDaysSql(
            GetCurrentDateSql(),
            1
        );
            }

            if (dateFilter == "MONTH")
            {
                return @"
AND p.DateTrx >=
(
    SELECT
        MIN(
            CurrentPeriod.StartDate
        )

    FROM AD_ClientInfo ClientInfo

    INNER JOIN C_Year CurrentYear ON
    (
        CurrentYear.C_Calendar_ID =
        ClientInfo.C_Calendar_ID
    )

    INNER JOIN C_Period CurrentPeriod ON
    (
        CurrentPeriod.C_Year_ID =
        CurrentYear.C_Year_ID
    )

    WHERE ClientInfo.AD_Client_ID =
        p.AD_Client_ID

    AND ClientInfo.IsActive = 'Y'

    AND CurrentPeriod.IsActive = 'Y'

    AND " + GetCurrentDateSql() + @" >=
        CurrentPeriod.StartDate

    AND " + GetCurrentDateSql() + @" <
        " + GetDateToExclusiveSql(
            "CurrentPeriod.EndDate"
        ) + @"
)

AND p.DateTrx <
(
    SELECT
        MAX
        (
            " + GetDateToExclusiveSql(
                "CurrentPeriod.EndDate"
            ) + @"
        )

    FROM AD_ClientInfo ClientInfo

    INNER JOIN C_Year CurrentYear ON
    (
        CurrentYear.C_Calendar_ID =
        ClientInfo.C_Calendar_ID
    )

    INNER JOIN C_Period CurrentPeriod ON
    (
        CurrentPeriod.C_Year_ID =
        CurrentYear.C_Year_ID
    )

    WHERE ClientInfo.AD_Client_ID =
        p.AD_Client_ID

    AND ClientInfo.IsActive = 'Y'

    AND CurrentPeriod.IsActive = 'Y'

    AND " + GetCurrentDateSql() + @" >=
        CurrentPeriod.StartDate

    AND " + GetCurrentDateSql() + @" <
        " + GetDateToExclusiveSql(
            "CurrentPeriod.EndDate"
        ) + @"
)";
            }

            return @"
AND p.DateTrx >=
    " + GetCurrentDateSql() + @"

AND p.DateTrx <
    " + GetDateAddDaysSql(
            GetCurrentDateSql(),
            7
        );
        }

        private string GetCurrentDateSql()
        {
            if (DB.IsOracle())
            {
                return "TRUNC(CURRENT_DATE)";
            }

            return "CURRENT_DATE";
        }

        private string GetDateOnlySql(
            string columnExpression
        )
        {
            if (DB.IsOracle())
            {
                return "TRUNC("
                    + columnExpression
                    + ")";
            }

            return "CAST("
                + columnExpression
                + " AS DATE)";
        }

        private string GetDateAddDaysSql(
            string dateExpression,
            int numberOfDays
        )
        {
            return "("
                + dateExpression
                + " + "
                + numberOfDays.ToString(
                    CultureInfo.InvariantCulture
                )
                + ")";
        }

        private string GetDateToExclusiveSql(
            string columnExpression
        )
        {
            if (DB.IsOracle())
            {
                return "TRUNC("
                    + columnExpression
                    + ") + 1";
            }

            return "CAST("
                + columnExpression
                + " AS DATE) + 1";
        }

        private string ToSqlDate(
            DateTime date
        )
        {
            return "DATE '"
                + date.Date.ToString(
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture
                )
                + "'";
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

        private bool HasPaymentMethodColumn()
        {
            return HasColumn(
                "C_Payment",
                "VA009_PaymentMethod_ID"
            );
        }

        private string GetPaymentMethodNameColumn(
            string tableAlias
        )
        {
            if (
                HasColumn(
                    "VA009_PaymentMethod",
                    "VA009_Name"
                )
            )
            {
                return tableAlias +
                    ".VA009_Name";
            }

            if (
                HasColumn(
                    "VA009_PaymentMethod",
                    "Name"
                )
            )
            {
                return tableAlias +
                    ".Name";
            }

            if (
                HasColumn(
                    "VA009_PaymentMethod",
                    "Value"
                )
            )
            {
                return tableAlias +
                    ".Value";
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
            return Json(
                new
                {
                    error = true,
                    errorText = GetMsg(Env.GetCtx(), "SessionExpired", "Session Expired")
                },
                JsonRequestBehavior.AllowGet
            );
        }

        private string GetPeriodText(
            Ctx ctx,
            string dateFilter
        )
        {
            dateFilter =
                string.IsNullOrWhiteSpace(
                    dateFilter
                )
                    ? "NEXT7D"
                    : dateFilter.ToUpperInvariant();

            if (dateFilter == "YTD")
            {
                return GetMsg(
                    ctx,
                    "VAS_033_MessageYTD",
                    "Ytd"
                );
            }

            if (dateFilter == "MONTH")
            {
                return GetMsg(
                    ctx,
                    "VAS_033_MessageMonth",
                    "Month"
                );
            }

            return GetMsg(
                ctx,
                "VAS_033_MessageNext7Days",
                "Next 7 Days"
            );
        }

        private string GetPaymentCountText(
            Ctx ctx,
            int paymentCount
        )
        {
            if (paymentCount == 1)
            {
                return paymentCount
                    + " "
                    + GetMsg(
                        ctx,
                        "VAS_033_MessagePayment",
                        "payment"
                    );
            }

            return paymentCount
                + " "
                + GetMsg(
                    ctx,
                    "VAS_033_MessagePayments",
                    "payments"
                );
        }

        private string GetPaymentMethodName(
            Ctx ctx,
            string paymentMethodName
        )
        {
            if (
                string.IsNullOrWhiteSpace(
                    paymentMethodName
                )
            )
            {
                return GetMsg(
                    ctx,
                    "VAS_032_MessageNotSpecified",
                    "Not Specified"
                );
            }

            return paymentMethodName;
        }

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

            return
                !string.IsNullOrWhiteSpace(
                    message
                ) &&
                message != "[" + key + "]"
                    ? message
                    : fallback;
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
                if (
                    !string.IsNullOrWhiteSpace(
                        values[index]
                    )
                )
                {
                    return values[index];
                }
            }

            return string.Empty;
        }

        private string GetAccountTail(
            string accountNo
        )
        {
            if (
                string.IsNullOrWhiteSpace(
                    accountNo
                )
            )
            {
                return string.Empty;
            }

            return accountNo.Length > 4
                ? accountNo.Substring(
                    accountNo.Length - 4
                )
                : accountNo;
        }

        private bool HasReaderColumn(
            IDataReader reader,
            string columnName
        )
        {
            if (reader == null)
            {
                return false;
            }

            for (
                int index = 0;
                index < reader.FieldCount;
                index++
            )
            {
                if (
                    string.Equals(
                        reader.GetName(index),
                        columnName,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    return true;
                }
            }

            return false;
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

        private string FormatRunDate(
            DateTime? date
        )
        {
            return date.HasValue
                ? date.Value.ToString(
                    "ddd dd MMM",
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