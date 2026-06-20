using CoreLibrary.DataBase;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Web.Mvc;
using VAdvantage.Acct;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Process;
using VAdvantage.Utility;
using VIS.Filters;

/*
 * GL Journal Recent Entries Controller
 *
 * -- Labels / Message Keys --------------------------------------------
 *  #  | Current Text                         | Message Key
 * ----+--------------------------------------+--------------------------------
 *  1  | Could Not Load Recent Entries        | VAS_044_LoadFailed
 *  2  | Invalid Journal ID                   | VAS_044_InvalidJournalID
 *  3  | Details Not Available                | VAS_044_DetailsNotAvailable
 *  4  | Details Load Failed                  | VAS_044_DetailsLoadFailed
 *  5  | Already Posted                       | VAS_044_AlreadyPosted
 *  6  | Already Approved                     | VAS_044_AlreadyApproved
 *  7  | Prepare Failed                       | VAS_044_PrepareFailed
 *  8  | Invalid Approval Status              | VAS_044_ApproveInvalidStatus
 *  9  | Approve Failed                       | VAS_044_ApproveFailed
 * 10  | Approved Successfully                | VAS_044_ApprovedSuccessfully
 * 11  | Complete Failed                      | VAS_044_CompleteFailed
 * 12  | Must Approve Before Post             | VAS_044_MustApproveBeforePost
 * 13  | Not Processed                        | VAS_044_NotProcessed
 * 14  | No Accounting Schema                 | VAS_044_NoAccountingSchema
 * 15  | Post Verification Failed             | VAS_044_PostVerificationFailed
 * 16  | Posted Successfully                  | VAS_044_PostedSuccessfully
 * 17  | Post Failed                          | VAS_044_PostFailed
 * 18  | No Permission                        | VAS_044_NoPermission
 * 19  | Inactive Journal                     | VAS_044_InactiveJournal
 * 20  | Not Balanced                         | VAS_044_NotBalanced
 * 21  | Not Convertible                      | VAS_044_NotConvertible
 * 22  | Period Closed                        | VAS_044_PeriodClosed
 * 23  | Invalid Account                      | VAS_044_InvalidAccount
 * ---------------------------------------------------------------------
 */

namespace VAS.Controllers
{
    /// <summary>
    /// Controller for the GL Journal Recent Entries widget.
    /// Returns recent journals, journal details, and journal actions.
    /// </summary>
    public class VAS_044_GLJournalRecentWidgetController : Controller
    {
        /*
         * Keep the reference name in one place.
         * Change this value only if the DocStatus reference
         * has another Name in AD_Reference.
         */
        private const string DocumentStatusReferenceName =
            "_Document Status";

        /// <summary>
        /// Returns the six most recent GL journals.
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetRecentEntries()
        {
            Ctx ctx = GetContext();

            if (ctx == null)
            {
                return GetSessionExpiredResult();
            }

            try
            {
                string language =
                    GetLanguage(ctx);

                string castType =
                    GetTextCastType();

                /*
                 * Apply MRole only to the physical GL_Journal query.
                 * Do not apply MRole to the final WITH query.
                 */
                string protectedJournalSql = @"
SELECT
    GL_Journal.GL_Journal_ID,
    GL_Journal.AD_Client_ID,
    GL_Journal.AD_Org_ID,
    GL_Journal.C_AcctSchema_ID,
    GL_Journal.DocumentNo,
    GL_Journal.DateAcct,
    GL_Journal.Description,
    GL_Journal.DocStatus,
    GL_Journal.Posted,
    GL_Journal.Processed,
    GL_Journal.Created,
    GL_Journal.CreatedBy
FROM GL_Journal GL_Journal
WHERE GL_Journal.IsActive = 'Y'
AND GL_Journal.AD_Client_ID =
@RecentClientID";

                protectedJournalSql =
                    MRole.GetDefault(ctx)
                        .AddAccessSQL(
                            protectedJournalSql,
                            "GL_Journal",
                            MRole.SQL_FULLYQUALIFIED,
                            MRole.SQL_RO
                        );

                /*
                 * ANSI SQL query compatible with Oracle and PostgreSQL.
                 *
                 * ROW_NUMBER is used instead of ROWNUM or LIMIT.
                 * COALESCE is supported by both databases.
                 */
                string sql = @"
WITH ProtectedJournal AS
(
" + protectedJournalSql + @"
),
SchemaCurrency AS
(
    SELECT
        AcctSchema.AD_Client_ID,
        AcctSchema.C_AcctSchema_ID,
        AcctSchema.Name AS AcctSchemaName,
        Currency.StdPrecision,
        Currency.ISO_Code AS ISOCode,
        Currency.CurSymbol AS CurSymbol
    FROM C_AcctSchema AcctSchema
    INNER JOIN C_Currency Currency ON
    (
        AcctSchema.C_Currency_ID =
        Currency.C_Currency_ID
    )
    WHERE AcctSchema.IsActive = 'Y'
    AND Currency.IsActive = 'Y'
    AND AcctSchema.AD_Client_ID =
    @RecentSchemaClientID
),
DocumentStatusReference AS
(
    SELECT DISTINCT
        CAST(
            RefList.Value AS " + castType + @"
        ) AS StatusValue,

        CAST(
            RefList.Name AS " + castType + @"
        ) AS BaseName,

        CAST(
            RefListTrl.Name AS " + castType + @"
        ) AS TranslatedName

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
        @RecentLanguage
    )

    WHERE ReferenceInfo.Name =
    @RecentStatusReferenceName

    AND ReferenceInfo.IsActive = 'Y'
    AND RefList.IsActive = 'Y'
),
JournalTotals AS
(
    SELECT
        ProtectedJournal.GL_Journal_ID,
        ProtectedJournal.DocumentNo,
        ProtectedJournal.DateAcct,
        ProtectedJournal.Description,
        ProtectedJournal.DocStatus,
        ProtectedJournal.Posted,
        ProtectedJournal.Processed,

        DocumentStatusReference.BaseName
            AS StatusBaseName,

        DocumentStatusReference.TranslatedName
            AS StatusTranslatedName,

        COALESCE(
            SUM(
                COALESCE(
                    GL_JournalLine.AmtAcctDr,
                    0
                )
            ),
            0
        ) AS TotalDebit,

        COALESCE(
            SUM(
                COALESCE(
                    GL_JournalLine.AmtAcctCr,
                    0
                )
            ),
            0
        ) AS TotalCredit,

        MAX(
            SchemaCurrency.CurSymbol
        ) AS CurSymbol,

        MAX(
            SchemaCurrency.ISOCode
        ) AS ISOCode,

        COALESCE(
            MAX(
                SchemaCurrency.StdPrecision
            ),
            2
        ) AS StdPrecision

    FROM ProtectedJournal ProtectedJournal

    INNER JOIN SchemaCurrency SchemaCurrency ON
    (
        SchemaCurrency.AD_Client_ID =
        ProtectedJournal.AD_Client_ID

        AND SchemaCurrency.C_AcctSchema_ID =
        ProtectedJournal.C_AcctSchema_ID
    )

    LEFT OUTER JOIN GL_JournalLine GL_JournalLine ON
    (
        ProtectedJournal.GL_Journal_ID =
        GL_JournalLine.GL_Journal_ID

        AND GL_JournalLine.IsActive = 'Y'
    )

    LEFT OUTER JOIN DocumentStatusReference
        DocumentStatusReference ON
    (
        CAST(
            ProtectedJournal.DocStatus AS " + castType + @"
        ) =
        DocumentStatusReference.StatusValue
    )

    GROUP BY
        ProtectedJournal.GL_Journal_ID,
        ProtectedJournal.DocumentNo,
        ProtectedJournal.DateAcct,
        ProtectedJournal.Description,
        ProtectedJournal.DocStatus,
        ProtectedJournal.Posted,
        ProtectedJournal.Processed,
        DocumentStatusReference.BaseName,
        DocumentStatusReference.TranslatedName
),
OrderedJournals AS
(
    SELECT
        JournalTotals.GL_Journal_ID,
        JournalTotals.DocumentNo,
        JournalTotals.DateAcct,
        JournalTotals.Description,
        JournalTotals.DocStatus,
        JournalTotals.Posted,
        JournalTotals.Processed,
        JournalTotals.StatusBaseName,
        JournalTotals.StatusTranslatedName,
        JournalTotals.TotalDebit,
        JournalTotals.TotalCredit,
        JournalTotals.CurSymbol,
        JournalTotals.ISOCode,
        JournalTotals.StdPrecision,

        ROW_NUMBER() OVER
        (
            ORDER BY
                JournalTotals.DateAcct DESC,
                JournalTotals.GL_Journal_ID DESC
        ) AS RowNumber

    FROM JournalTotals JournalTotals
)
SELECT
    OrderedJournals.GL_Journal_ID,
    OrderedJournals.DocumentNo,
    OrderedJournals.DateAcct,
    OrderedJournals.Description,
    OrderedJournals.DocStatus,
    OrderedJournals.Posted,
    OrderedJournals.Processed,
    OrderedJournals.StatusBaseName,
    OrderedJournals.StatusTranslatedName,
    OrderedJournals.TotalDebit,
    OrderedJournals.TotalCredit,
    OrderedJournals.CurSymbol,
    OrderedJournals.ISOCode,
    OrderedJournals.StdPrecision

FROM OrderedJournals OrderedJournals

WHERE OrderedJournals.RowNumber <= 6

ORDER BY
    OrderedJournals.RowNumber";

                /*
                 * Parameter order matches the placeholder order.
                 * Unique parameter names prevent Oracle binding issues.
                 */
                SqlParameter[] parameters =
                {
                    new SqlParameter(
                        "@RecentClientID",
                        ctx.GetAD_Client_ID()
                    ),

                    new SqlParameter(
                        "@RecentSchemaClientID",
                        ctx.GetAD_Client_ID()
                    ),

                    new SqlParameter(
                        "@RecentLanguage",
                        language
                    ),

                    new SqlParameter(
                        "@RecentStatusReferenceName",
                        DocumentStatusReferenceName
                    )
                };

                DataSet dataSet =
                    DB.ExecuteDataset(
                        sql,
                        parameters,
                        null
                    );

                List<object> entries =
                    new List<object>();

                string curSymbol =
                    string.Empty;

                string isoCode =
                    string.Empty;

                int stdPrecision =
                    2;

                if (
                    dataSet != null &&
                    dataSet.Tables.Count > 0
                )
                {
                    foreach (
                        DataRow row in
                        dataSet.Tables[0].Rows
                    )
                    {
                        string statusValue =
                            Util.GetValueOfString(
                                row["DocStatus"]
                            );

                        string statusName =
                            GetReferenceDisplayName(
                                row,
                                "StatusTranslatedName",
                                "StatusBaseName",
                                statusValue
                            );

                        DateTime? dateAcct =
                            Util.GetValueOfDateTime(
                                row["DateAcct"]
                            );

                        stdPrecision =
                            NormalizePrecision(
                                Util.GetValueOfInt(
                                    row["StdPrecision"]
                                )
                            );

                        decimal totalDebit =
                            Decimal.Round(
                                Util.GetValueOfDecimal(
                                    row["TotalDebit"]
                                ),
                                stdPrecision,
                                MidpointRounding.AwayFromZero
                            );

                        decimal totalCredit =
                            Decimal.Round(
                                Util.GetValueOfDecimal(
                                    row["TotalCredit"]
                                ),
                                stdPrecision,
                                MidpointRounding.AwayFromZero
                            );

                        curSymbol =
                            Util.GetValueOfString(
                                row["CurSymbol"]
                            );

                        isoCode =
                            Util.GetValueOfString(
                                row["ISOCode"]
                            );

                        if (
                            string.IsNullOrWhiteSpace(
                                curSymbol
                            )
                        )
                        {
                            curSymbol =
                                isoCode;
                        }

                        entries.Add(
                            new
                            {
                                GL_Journal_ID =
                                    Util.GetValueOfInt(
                                        row[
                                            "GL_Journal_ID"
                                        ]
                                    ),

                                DocumentNo =
                                    Util.GetValueOfString(
                                        row["DocumentNo"]
                                    ),

                                DateAcct =
                                    dateAcct.HasValue
                                        ? dateAcct.Value.ToString(
                                            "dd MMM yyyy"
                                        )
                                        : string.Empty,

                                Description =
                                    Util.GetValueOfString(
                                        row["Description"]
                                    ),

                                /*
                                 * Existing compatibility field.
                                 */
                                DocStatus =
                                    statusValue,

                                /*
                                 * Separate status fields.
                                 */
                                StatusValue =
                                    statusValue,

                                StatusName =
                                    statusName,

                                /*
                                 * Requested Status Value / Name object.
                                 */
                                Status = new
                                {
                                    Value =
                                        statusValue,

                                    Name =
                                        statusName
                                },

                                Posted =
                                    Util.GetValueOfString(
                                        row["Posted"]
                                    ),

                                Processed =
                                    Util.GetValueOfString(
                                        row["Processed"]
                                    ),

                                TotalDebit =
                                    totalDebit,

                                TotalCredit =
                                    totalCredit,

                                IsUnbalanced =
                                    totalDebit !=
                                    totalCredit
                            }
                        );
                    }
                }

                return JsonString(
                    new
                    {
                        success = true,
                        error = string.Empty,

                        Entries =
                            entries,

                        TotalCount =
                            entries.Count,

                        CurSymbol =
                            curSymbol,

                        ISOCode =
                            isoCode,

                        StdPrecision =
                            stdPrecision
                    }
                );
            }
            catch (Exception exception)
            {
                LogException(
                    "GetRecentEntries",
                    exception
                );

                string errorMessage =
                    GetMsg(
                        ctx,
                        "VAS_044_LoadFailed",
                        "Error loading journal entries."
                    );

                return JsonString(
                    new
                    {
                        success = false,

                        error =
                            errorMessage,

                        errorText =
                            errorMessage
                    }
                );
            }
        }

        /// <summary>
        /// Returns the selected journal header and lines.
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetJournalEntryDetail(
            int journalId)
        {
            Ctx ctx =
                GetContext();

            if (ctx == null)
            {
                return GetSessionExpiredResult();
            }

            if (journalId <= 0)
            {
                return JsonString(
                    new
                    {
                        success = false,
                        error = true,

                        errorText =
                            GetMsg(
                                ctx,
                                "VAS_044_InvalidJournalID",
                                "Invalid journal ID."
                            )
                    }
                );
            }

            try
            {
                string language =
                    GetLanguage(ctx);

                string castType =
                    GetTextCastType();

                /*
                 * The old query used:
                 *
                 * ClientInfo.C_AcctSchema1_ID =
                 * GL_Journal.C_AcctSchema_ID
                 *
                 * That condition removed journals that used another
                 * valid accounting schema.
                 */
                string protectedJournalSql = @"
SELECT
    GL_Journal.GL_Journal_ID,
    GL_Journal.AD_Client_ID,
    GL_Journal.AD_Org_ID,
    GL_Journal.C_AcctSchema_ID,
    GL_Journal.DocumentNo,
    GL_Journal.DateAcct,
    GL_Journal.Description,
    GL_Journal.DocStatus,
    GL_Journal.Posted,
    GL_Journal.Processed,
    GL_Journal.Created,
    GL_Journal.CreatedBy
FROM GL_Journal GL_Journal
WHERE GL_Journal.IsActive = 'Y'
AND GL_Journal.AD_Client_ID =
@DetailClientID
AND GL_Journal.GL_Journal_ID =
@DetailJournalID";

                /*
                 * Apply MRole only to GL_Journal physical table.
                 */
                protectedJournalSql =
                    MRole.GetDefault(ctx)
                        .AddAccessSQL(
                            protectedJournalSql,
                            "GL_Journal",
                            MRole.SQL_FULLYQUALIFIED,
                            MRole.SQL_RO
                        );

                string sql = @"
WITH ProtectedJournal AS
(
" + protectedJournalSql + @"
),
DocumentStatusReference AS
(
    SELECT DISTINCT
        CAST(
            RefList.Value AS " + castType + @"
        ) AS StatusValue,

        CAST(
            RefList.Name AS " + castType + @"
        ) AS BaseName,

        CAST(
            RefListTrl.Name AS " + castType + @"
        ) AS TranslatedName

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
        @DetailLanguage
    )

    WHERE ReferenceInfo.Name =
    @DetailStatusReferenceName

    AND ReferenceInfo.IsActive = 'Y'
    AND RefList.IsActive = 'Y'
)
SELECT
    ProtectedJournal.GL_Journal_ID,
    ProtectedJournal.DocumentNo,
    ProtectedJournal.DateAcct,
    ProtectedJournal.Description,
    ProtectedJournal.DocStatus,
    ProtectedJournal.Posted,
    ProtectedJournal.Processed,
    ProtectedJournal.Created,
    ProtectedJournal.CreatedBy,

    DocumentStatusReference.BaseName
        AS StatusBaseName,

    DocumentStatusReference.TranslatedName
        AS StatusTranslatedName,

    CreatedUser.Name AS CreatedByName,

    AcctSchema.Name AS AccountingBook,
    Currency.CurSymbol,
    Currency.ISO_Code AS ISOCode,
    Currency.StdPrecision,

    GL_JournalLine.GL_JournalLine_ID,
    GL_JournalLine.Line,
    GL_JournalLine.C_ValidCombination_ID,

    ValidCombination.Account_ID,

    AccountValue.Value AS AccountCode,
    AccountValue.Name AS AccountName,

    COALESCE(
        GL_JournalLine.AmtAcctDr,
        0
    ) AS AmtAcctDr,

    COALESCE(
        GL_JournalLine.AmtAcctCr,
        0
    ) AS AmtAcctCr,

    CostCenterValue.Value AS CostCenterCode,
    CostCenterValue.Name AS CostCenterName,

    BusinessPartner.Name AS BPartnerName,
    Product.Name AS ProductName,
    Project.Name AS ProjectName,

    COALESCE(
        SUM(
            COALESCE(
                GL_JournalLine.AmtAcctDr,
                0
            )
        ) OVER
        (
            PARTITION BY
                ProtectedJournal.GL_Journal_ID
        ),
        0
    ) AS TotalDebit,

    COALESCE(
        SUM(
            COALESCE(
                GL_JournalLine.AmtAcctCr,
                0
            )
        ) OVER
        (
            PARTITION BY
                ProtectedJournal.GL_Journal_ID
        ),
        0
    ) AS TotalCredit

FROM ProtectedJournal ProtectedJournal

INNER JOIN C_AcctSchema AcctSchema ON
(
    AcctSchema.C_AcctSchema_ID =
    ProtectedJournal.C_AcctSchema_ID

    AND AcctSchema.AD_Client_ID =
    ProtectedJournal.AD_Client_ID

    AND AcctSchema.IsActive = 'Y'
)

INNER JOIN C_Currency Currency ON
(
    Currency.C_Currency_ID =
    AcctSchema.C_Currency_ID

    AND Currency.IsActive = 'Y'
)

LEFT OUTER JOIN GL_JournalLine GL_JournalLine ON
(
    ProtectedJournal.GL_Journal_ID =
    GL_JournalLine.GL_Journal_ID

    AND GL_JournalLine.IsActive = 'Y'
)

LEFT OUTER JOIN C_ValidCombination
    ValidCombination ON
(
    GL_JournalLine.C_ValidCombination_ID =
    ValidCombination.C_ValidCombination_ID

    AND ValidCombination.IsActive = 'Y'
)

LEFT OUTER JOIN C_ElementValue AccountValue ON
(
    ValidCombination.Account_ID =
    AccountValue.C_ElementValue_ID

    AND AccountValue.IsActive = 'Y'
)

LEFT OUTER JOIN C_ElementValue
    CostCenterValue ON
(
    ValidCombination.User1_ID =
    CostCenterValue.C_ElementValue_ID

    AND CostCenterValue.IsActive = 'Y'
)

LEFT OUTER JOIN C_BPartner
    BusinessPartner ON
(
    ValidCombination.C_BPartner_ID =
    BusinessPartner.C_BPartner_ID

    AND BusinessPartner.IsActive = 'Y'
)

LEFT OUTER JOIN M_Product Product ON
(
    ValidCombination.M_Product_ID =
    Product.M_Product_ID

    AND Product.IsActive = 'Y'
)

LEFT OUTER JOIN C_Project Project ON
(
    ValidCombination.C_Project_ID =
    Project.C_Project_ID

    AND Project.IsActive = 'Y'
)

LEFT OUTER JOIN AD_User CreatedUser ON
(
    ProtectedJournal.CreatedBy =
    CreatedUser.AD_User_ID

    AND CreatedUser.IsActive = 'Y'
)

LEFT OUTER JOIN DocumentStatusReference
    DocumentStatusReference ON
(
    CAST(
        ProtectedJournal.DocStatus AS " + castType + @"
    ) =
    DocumentStatusReference.StatusValue
)

ORDER BY
    GL_JournalLine.Line,
    GL_JournalLine.GL_JournalLine_ID";

                /*
                 * The parameter order follows the placeholder order.
                 * Every placeholder has a unique name.
                 */
                SqlParameter[] parameters =
                {
                    new SqlParameter(
                        "@DetailClientID",
                        ctx.GetAD_Client_ID()
                    ),

                    new SqlParameter(
                        "@DetailJournalID",
                        journalId
                    ),

                    new SqlParameter(
                        "@DetailLanguage",
                        language
                    ),

                    new SqlParameter(
                        "@DetailStatusReferenceName",
                        DocumentStatusReferenceName
                    )
                };

                DataSet dataSet =
                    DB.ExecuteDataset(
                        sql,
                        parameters,
                        null
                    );

                if (
                    dataSet == null ||
                    dataSet.Tables.Count == 0 ||
                    dataSet.Tables[0].Rows.Count == 0
                )
                {
                    return JsonString(
                        new
                        {
                            success = false,
                            error = true,

                            errorText =
                                GetMsg(
                                    ctx,
                                    "VAS_044_DetailsNotAvailable",
                                    "Journal details are not available."
                                )
                        }
                    );
                }

                DataTable resultTable =
                    dataSet.Tables[0];

                DataRow headerRow =
                    resultTable.Rows[0];

                int stdPrecision =
                    NormalizePrecision(
                        Util.GetValueOfInt(
                            headerRow["StdPrecision"]
                        )
                    );

                string statusValue =
                    Util.GetValueOfString(
                        headerRow["DocStatus"]
                    );

                string statusName =
                    GetReferenceDisplayName(
                        headerRow,
                        "StatusTranslatedName",
                        "StatusBaseName",
                        statusValue
                    );

                DateTime? dateAcct =
                    Util.GetValueOfDateTime(
                        headerRow["DateAcct"]
                    );

                DateTime? created =
                    Util.GetValueOfDateTime(
                        headerRow["Created"]
                    );

                decimal totalDebit =
                    Decimal.Round(
                        Util.GetValueOfDecimal(
                            headerRow["TotalDebit"]
                        ),
                        stdPrecision,
                        MidpointRounding.AwayFromZero
                    );

                decimal totalCredit =
                    Decimal.Round(
                        Util.GetValueOfDecimal(
                            headerRow["TotalCredit"]
                        ),
                        stdPrecision,
                        MidpointRounding.AwayFromZero
                    );

                string curSymbol =
                    Util.GetValueOfString(
                        headerRow["CurSymbol"]
                    );

                string isoCode =
                    Util.GetValueOfString(
                        headerRow["ISOCode"]
                    );

                if (
                    string.IsNullOrWhiteSpace(
                        curSymbol
                    )
                )
                {
                    curSymbol =
                        isoCode;
                }

                List<object> lines =
                    new List<object>();

                foreach (
                    DataRow row in
                    resultTable.Rows
                )
                {
                    int journalLineId =
                        Util.GetValueOfInt(
                            row[
                                "GL_JournalLine_ID"
                            ]
                        );

                    /*
                     * LEFT OUTER JOIN returns one header row
                     * when no active journal lines exist.
                     */
                    if (journalLineId <= 0)
                    {
                        continue;
                    }

                    string accountCode =
                        Util.GetValueOfString(
                            row["AccountCode"]
                        );

                    string accountName =
                        Util.GetValueOfString(
                            row["AccountName"]
                        );

                    if (
                        string.IsNullOrWhiteSpace(
                            accountCode
                        )
                    )
                    {
                        accountCode =
                            Util.GetValueOfString(
                                row["Account_ID"]
                            );
                    }

                    if (
                        string.IsNullOrWhiteSpace(
                            accountCode
                        )
                    )
                    {
                        accountCode =
                            Util.GetValueOfString(
                                row[
                                    "C_ValidCombination_ID"
                                ]
                            );
                    }

                    if (
                        string.IsNullOrWhiteSpace(
                            accountName
                        )
                    )
                    {
                        accountName =
                            "-";
                    }

                    string costCenter =
                        BuildCodeName(
                            Util.GetValueOfString(
                                row["CostCenterCode"]
                            ),
                            Util.GetValueOfString(
                                row["CostCenterName"]
                            )
                        );

                    lines.Add(
                        new
                        {
                            GL_JournalLine_ID =
                                journalLineId,

                            Line =
                                Util.GetValueOfInt(
                                    row["Line"]
                                ),

                            AccountCode =
                                accountCode,

                            AccountName =
                                accountName,

                            Debit =
                                Decimal.Round(
                                    Util.GetValueOfDecimal(
                                        row["AmtAcctDr"]
                                    ),
                                    stdPrecision,
                                    MidpointRounding.AwayFromZero
                                ),

                            Credit =
                                Decimal.Round(
                                    Util.GetValueOfDecimal(
                                        row["AmtAcctCr"]
                                    ),
                                    stdPrecision,
                                    MidpointRounding.AwayFromZero
                                ),

                            CostCenter =
                                GetDefaultValue(
                                    costCenter,
                                    "-"
                                ),

                            BPartner =
                                GetDefaultValue(
                                    Util.GetValueOfString(
                                        row["BPartnerName"]
                                    ),
                                    "-"
                                ),

                            Product =
                                GetDefaultValue(
                                    Util.GetValueOfString(
                                        row["ProductName"]
                                    ),
                                    "-"
                                ),

                            Project =
                                GetDefaultValue(
                                    Util.GetValueOfString(
                                        row["ProjectName"]
                                    ),
                                    "-"
                                )
                        }
                    );
                }

                return JsonString(
                    new
                    {
                        success = true,
                        error = string.Empty,

                        Journal = new
                        {
                            GL_Journal_ID =
                                Util.GetValueOfInt(
                                    headerRow[
                                        "GL_Journal_ID"
                                    ]
                                ),

                            DocumentNo =
                                Util.GetValueOfString(
                                    headerRow[
                                        "DocumentNo"
                                    ]
                                ),

                            DateAcct =
                                dateAcct.HasValue
                                    ? dateAcct.Value.ToString(
                                        "dd MMM yyyy"
                                    )
                                    : string.Empty,

                            Description =
                                Util.GetValueOfString(
                                    headerRow[
                                        "Description"
                                    ]
                                ),

                            /*
                             * Existing compatibility field.
                             */
                            DocStatus =
                                statusValue,

                            /*
                             * Separate fields.
                             */
                            StatusValue =
                                statusValue,

                            StatusName =
                                statusName,

                            /*
                             * Requested object.
                             */
                            Status = new
                            {
                                Value =
                                    statusValue,

                                Name =
                                    statusName
                            },

                            Posted =
                                Util.GetValueOfString(
                                    headerRow["Posted"]
                                ),

                            Processed =
                                Util.GetValueOfString(
                                    headerRow["Processed"]
                                ),

                            TotalDebit =
                                totalDebit,

                            TotalCredit =
                                totalCredit,

                            AccountingBook =
                                GetDefaultValue(
                                    Util.GetValueOfString(
                                        headerRow[
                                            "AccountingBook"
                                        ]
                                    ),
                                    "Primary"
                                ),

                            CreatedByName =
                                GetDefaultValue(
                                    Util.GetValueOfString(
                                        headerRow[
                                            "CreatedByName"
                                        ]
                                    ),
                                    "-"
                                ),

                            CreatedDate =
                                created.HasValue
                                    ? created.Value.ToString(
                                        "dd MMM yyyy"
                                    )
                                    : string.Empty
                        },

                        Lines =
                            lines,

                        LineCount =
                            lines.Count,

                        CurSymbol =
                            curSymbol,

                        ISOCode =
                            isoCode,

                        StdPrecision =
                            stdPrecision
                    }
                );
            }
            catch (Exception exception)
            {
                LogException(
                    "GetJournalEntryDetail",
                    exception
                );

                string errorMessage =
                    GetMsg(
                        ctx,
                        "VAS_044_DetailsLoadFailed",
                        "Error loading journal details."
                    );

                return JsonString(
                    new
                    {
                        success = false,
                        error = true,

                        errorText =
                            errorMessage
                    }
                );
            }
        }

        /// <summary>
        /// Prepares and approves a Draft, In Progress,
        /// or Not Approved journal.
        /// </summary>
        [HttpPost]
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult ApproveJournal(
            int journalId)
        {
            Ctx ctx =
                GetContext();

            if (ctx == null)
            {
                return Json(
                    new
                    {
                        success = false,
                        error =
                            "Session Expired",

                        errorText =
                            "Session Expired"
                    }
                );
            }

            JsonResult validationResult =
                ValidateActionRequest(
                    ctx,
                    journalId
                );

            if (validationResult != null)
            {
                return validationResult;
            }

            string transactionName =
                VAdvantage.DataBase.Trx
                    .CreateTrxName(
                        "VAS044ApproveJournal"
                    );

            VAdvantage.DataBase.Trx transaction =
                VAdvantage.DataBase.Trx.GetTrx(
                    transactionName
                );

            try
            {
                MJournal journal =
                    new MJournal(
                        ctx,
                        journalId,
                        transaction
                    );

                ValidateJournal(
                    ctx,
                    journal,
                    journalId
                );

                if (IsJournalPosted(journal))
                {
                    throw new InvalidOperationException(
                        GetMsg(
                            ctx,
                            "VAS_044_AlreadyPosted",
                            "The journal is already posted."
                        )
                    );
                }

                string docStatus =
                    journal.GetDocStatus();

                if (
                    string.Equals(
                        docStatus,
                        "AP",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    transaction.Commit();

                    return Json(
                        new
                        {
                            success = true,

                            journalId =
                                journal.Get_ID(),

                            documentNo =
                                journal.GetDocumentNo(),

                            docStatus =
                                journal.GetDocStatus(),

                            posted =
                                IsJournalPosted(
                                    journal
                                ),

                            message =
                                GetMsg(
                                    ctx,
                                    "VAS_044_AlreadyApproved",
                                    "The journal is already approved."
                                )
                        }
                    );
                }

                if (
                    string.Equals(
                        docStatus,
                        "DR",
                        StringComparison.OrdinalIgnoreCase
                    ) ||
                    string.Equals(
                        docStatus,
                        "NA",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    journal.SetDocAction(
                        DocActionVariables.ACTION_PREPARE
                    );

                    if (
                        !journal.ProcessIt(
                            DocActionVariables.ACTION_PREPARE
                        )
                    )
                    {
                        throw new InvalidOperationException(
                            GetJournalProcessError(
                                journal,
                                GetMsg(
                                    ctx,
                                    "VAS_044_PrepareFailed",
                                    "The journal could not be prepared."
                                )
                            )
                        );
                    }

                    SaveJournal(journal);

                    docStatus =
                        journal.GetDocStatus();
                }

                if (
                    !string.Equals(
                        docStatus,
                        "IP",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    throw new InvalidOperationException(
                        GetMsg(
                            ctx,
                            "VAS_044_ApproveInvalidStatus",
                            "Only Draft, In Progress or Not Approved journals can be approved."
                        )
                    );
                }

                journal.SetDocAction(
                    DocActionVariables.ACTION_APPROVE
                );

                if (
                    !journal.ProcessIt(
                        DocActionVariables.ACTION_APPROVE
                    )
                )
                {
                    throw new InvalidOperationException(
                        GetJournalProcessError(
                            journal,
                            GetMsg(
                                ctx,
                                "VAS_044_ApproveFailed",
                                "The journal could not be approved."
                            )
                        )
                    );
                }

                SaveJournal(journal);

                transaction.Commit();

                return Json(
                    new
                    {
                        success = true,

                        journalId =
                            journal.Get_ID(),

                        documentNo =
                            journal.GetDocumentNo(),

                        docStatus =
                            journal.GetDocStatus(),

                        posted =
                            IsJournalPosted(
                                journal
                            ),

                        message =
                            GetMsg(
                                ctx,
                                "VAS_044_ApprovedSuccessfully",
                                "Journal approved successfully."
                            )
                    }
                );
            }
            catch (Exception exception)
            {
                if (transaction != null)
                {
                    transaction.Rollback();
                }

                return Json(
                    new
                    {
                        success = false,

                        error =
                            exception.Message,

                        errorText =
                            exception.Message
                    }
                );
            }
            finally
            {
                if (transaction != null)
                {
                    transaction.Close();
                }
            }
        }

        /// <summary>
        /// Completes and posts the selected journal.
        /// </summary>
        [HttpPost]
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult PostJournal(
            int journalId)
        {
            Ctx ctx =
                GetContext();

            if (ctx == null)
            {
                return Json(
                    new
                    {
                        success = false,

                        error =
                            "Session Expired",

                        errorText =
                            "Session Expired"
                    }
                );
            }

            JsonResult validationResult =
                ValidateActionRequest(
                    ctx,
                    journalId
                );

            if (validationResult != null)
            {
                return validationResult;
            }

            VAdvantage.DataBase.Trx transaction =
                null;

            try
            {
                string transactionName =
                    VAdvantage.DataBase.Trx
                        .CreateTrxName(
                            "VAS044CompleteJournal"
                        );

                transaction =
                    VAdvantage.DataBase.Trx.GetTrx(
                        transactionName
                    );

                MJournal journal =
                    new MJournal(
                        ctx,
                        journalId,
                        transaction
                    );

                ValidateJournal(
                    ctx,
                    journal,
                    journalId
                );

                if (IsJournalPosted(journal))
                {
                    transaction.Commit();

                    return Json(
                        new
                        {
                            success = true,

                            journalId =
                                journal.Get_ID(),

                            documentNo =
                                journal.GetDocumentNo(),

                            docStatus =
                                journal.GetDocStatus(),

                            posted = true,

                            message =
                                GetMsg(
                                    ctx,
                                    "VAS_044_AlreadyPosted",
                                    "The journal is already posted."
                                )
                        }
                    );
                }

                string docStatus =
                    journal.GetDocStatus();

                if (
                    string.Equals(
                        docStatus,
                        "AP",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    journal.SetDocAction(
                        DocActionVariables.ACTION_COMPLETE
                    );

                    if (
                        !journal.ProcessIt(
                            DocActionVariables.ACTION_COMPLETE
                        )
                    )
                    {
                        throw new InvalidOperationException(
                            GetJournalProcessError(
                                journal,
                                GetMsg(
                                    ctx,
                                    "VAS_044_CompleteFailed",
                                    "The journal could not be completed."
                                )
                            )
                        );
                    }

                    SaveJournal(journal);

                    transaction.Commit();
                }
                else if (
                    string.Equals(
                        docStatus,
                        "CO",
                        StringComparison.OrdinalIgnoreCase
                    ) ||
                    string.Equals(
                        docStatus,
                        "CL",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    transaction.Commit();
                }
                else
                {
                    throw new InvalidOperationException(
                        GetMsg(
                            ctx,
                            "VAS_044_MustApproveBeforePost",
                            "The journal must be approved before it can be posted."
                        )
                    );
                }
            }
            catch (Exception exception)
            {
                if (transaction != null)
                {
                    transaction.Rollback();
                }

                return Json(
                    new
                    {
                        success = false,

                        error =
                            exception.Message,

                        errorText =
                            exception.Message
                    }
                );
            }
            finally
            {
                if (transaction != null)
                {
                    transaction.Close();

                    transaction =
                        null;
                }
            }

            JournalPostingState journalState =
                GetJournalPostingState(
                    ctx,
                    journalId
                );

            if (journalState == null)
            {
                string errorMessage =
                    GetMsg(
                        ctx,
                        "VAS_044_DetailsNotAvailable",
                        "Journal details are not available."
                    );

                return Json(
                    new
                    {
                        success = false,

                        error =
                            errorMessage,

                        errorText =
                            errorMessage
                    }
                );
            }

            if (
                string.Equals(
                    journalState.Posted,
                    "Y",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return Json(
                    new
                    {
                        success = true,

                        journalId =
                            journalId,

                        documentNo =
                            journalState.DocumentNo,

                        docStatus =
                            journalState.DocStatus,

                        posted = true,

                        message =
                            GetMsg(
                                ctx,
                                "VAS_044_AlreadyPosted",
                                "The journal is already posted."
                            )
                    }
                );
            }

            if (
                !string.Equals(
                    journalState.DocStatus,
                    "CO",
                    StringComparison.OrdinalIgnoreCase
                ) &&
                !string.Equals(
                    journalState.DocStatus,
                    "CL",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                string errorMessage =
                    GetMsg(
                        ctx,
                        "VAS_044_CompleteFailed",
                        "The journal was not completed successfully."
                    ) +
                    " " +
                    journalState.DocStatus;

                return Json(
                    new
                    {
                        success = false,

                        error =
                            errorMessage,

                        errorText =
                            errorMessage
                    }
                );
            }

            if (
                !string.Equals(
                    journalState.Processed,
                    "Y",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                string errorMessage =
                    GetMsg(
                        ctx,
                        "VAS_044_NotProcessed",
                        "The journal is completed but Processed is not Y."
                    );

                return Json(
                    new
                    {
                        success = false,

                        error =
                            errorMessage,

                        errorText =
                            errorMessage
                    }
                );
            }

            try
            {
                int journalTableId =
                    MTable.Get_Table_ID(
                        "GL_Journal"
                    );

                MAcctSchema[] accountingSchemas =
                    MAcctSchema.GetClientAcctSchema(
                        ctx,
                        ctx.GetAD_Client_ID()
                    );

                if (
                    accountingSchemas == null ||
                    accountingSchemas.Length == 0
                )
                {
                    string errorMessage =
                        GetMsg(
                            ctx,
                            "VAS_044_NoAccountingSchema",
                            "No accounting schema was found for the client."
                        );

                    return Json(
                        new
                        {
                            success = false,

                            error =
                                errorMessage,

                            errorText =
                                errorMessage
                        }
                    );
                }

                string postingResult =
                    Doc.PostImmediate(
                        accountingSchemas,
                        journalTableId,
                        journalId,
                        false,
                        null
                    );

                if (
                    !IsPostingSuccess(
                        postingResult
                    )
                )
                {
                    string errorMessage =
                        GetPostingErrorMessage(
                            ctx,
                            postingResult
                        );

                    return Json(
                        new
                        {
                            success = false,

                            error =
                                errorMessage,

                            errorText =
                                errorMessage
                        }
                    );
                }

                journalState =
                    GetJournalPostingState(
                        ctx,
                        journalId
                    );

                if (
                    journalState == null ||
                    !string.Equals(
                        journalState.Posted,
                        "Y",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    string errorMessage =
                        GetMsg(
                            ctx,
                            "VAS_044_PostVerificationFailed",
                            "The accounting engine finished, but the journal was not posted."
                        );

                    return Json(
                        new
                        {
                            success = false,

                            error =
                                errorMessage,

                            errorText =
                                errorMessage
                        }
                    );
                }

                return Json(
                    new
                    {
                        success = true,

                        journalId =
                            journalId,

                        documentNo =
                            journalState.DocumentNo,

                        docStatus =
                            journalState.DocStatus,

                        posted = true,

                        message =
                            GetMsg(
                                ctx,
                                "VAS_044_PostedSuccessfully",
                                "Journal posted successfully."
                            )
                    }
                );
            }
            catch (Exception exception)
            {
                LogException(
                    "PostJournal",
                    exception
                );

                string errorMessage =
                    GetMsg(
                        ctx,
                        "VAS_044_PostFailed",
                        "Journal posting failed."
                    );

                return Json(
                    new
                    {
                        success = false,

                        error =
                            errorMessage,

                        errorText =
                            errorMessage
                    }
                );
            }
        }

        private string GetTextCastType()
        {
            if (DB.IsOracle())
            {
                return "VARCHAR2(4000)";
            }

            return "VARCHAR(4000)";
        }

        private string GetLanguage(
            Ctx ctx)
        {
            string language =
                ctx == null
                    ? string.Empty
                    : ctx.GetAD_Language();

            if (
                string.IsNullOrWhiteSpace(
                    language
                )
            )
            {
                return "en_US";
            }

            return language;
        }

        private string GetReferenceDisplayName(
            DataRow row,
            string translatedColumn,
            string baseColumn,
            string fallback)
        {
            string translatedName =
                Util.GetValueOfString(
                    row[translatedColumn]
                );

            if (
                !string.IsNullOrWhiteSpace(
                    translatedName
                )
            )
            {
                return translatedName;
            }

            string baseName =
                Util.GetValueOfString(
                    row[baseColumn]
                );

            if (
                !string.IsNullOrWhiteSpace(
                    baseName
                )
            )
            {
                return baseName;
            }

            return fallback;
        }

        private string BuildCodeName(
            string code,
            string name)
        {
            if (
                !string.IsNullOrWhiteSpace(
                    code
                ) &&
                !string.IsNullOrWhiteSpace(
                    name
                )
            )
            {
                return
                    code +
                    " · " +
                    name;
            }

            if (
                !string.IsNullOrWhiteSpace(
                    code
                )
            )
            {
                return code;
            }

            if (
                !string.IsNullOrWhiteSpace(
                    name
                )
            )
            {
                return name;
            }

            return string.Empty;
        }

        private string GetDefaultValue(
            string value,
            string fallback)
        {
            if (
                string.IsNullOrWhiteSpace(
                    value
                )
            )
            {
                return fallback;
            }

            return value;
        }

        private JsonResult ValidateActionRequest(
            Ctx ctx,
            int journalId)
        {
            if (journalId <= 0)
            {
                string errorMessage =
                    GetMsg(
                        ctx,
                        "VAS_044_DetailsNotAvailable",
                        "Journal details are not available."
                    );

                return Json(
                    new
                    {
                        success = false,

                        error =
                            errorMessage,

                        errorText =
                            errorMessage
                    }
                );
            }

            int journalTableId =
                MTable.Get_Table_ID(
                    "GL_Journal"
                );

            MRole role =
                MRole.GetDefault(ctx);

            if (
                !role.IsRecordAccess(
                    journalTableId,
                    journalId,
                    false
                )
            )
            {
                string errorMessage =
                    GetMsg(
                        ctx,
                        "VAS_044_NoPermission",
                        "You do not have permission to update this journal."
                    );

                return Json(
                    new
                    {
                        success = false,

                        error =
                            errorMessage,

                        errorText =
                            errorMessage
                    }
                );
            }

            return null;
        }

        private void ValidateJournal(
            Ctx ctx,
            MJournal journal,
            int journalId)
        {
            if (
                journal == null ||
                journal.Get_ID() <= 0 ||
                journal.Get_ID() != journalId
            )
            {
                throw new InvalidOperationException(
                    GetMsg(
                        ctx,
                        "VAS_044_DetailsNotAvailable",
                        "Journal details are not available."
                    )
                );
            }

            if (!journal.IsActive())
            {
                throw new InvalidOperationException(
                    GetMsg(
                        ctx,
                        "VAS_044_InactiveJournal",
                        "The journal is inactive."
                    )
                );
            }

            if (
                journal.GetAD_Client_ID() !=
                ctx.GetAD_Client_ID()
            )
            {
                throw new InvalidOperationException(
                    GetMsg(
                        ctx,
                        "VAS_044_NoPermission",
                        "You do not have permission to update this journal."
                    )
                );
            }
        }

        private bool IsJournalPosted(
            MJournal journal)
        {
            if (journal == null)
            {
                return false;
            }

            object postedValue =
                journal.Get_Value(
                    "Posted"
                );

            if (
                postedValue == null ||
                postedValue == DBNull.Value
            )
            {
                return false;
            }

            if (postedValue is bool)
            {
                return (bool)postedValue;
            }

            string postedText =
                Util.GetValueOfString(
                    postedValue
                );

            return
                string.Equals(
                    postedText,
                    "Y",
                    StringComparison.OrdinalIgnoreCase
                ) ||
                string.Equals(
                    postedText,
                    "TRUE",
                    StringComparison.OrdinalIgnoreCase
                ) ||
                string.Equals(
                    postedText,
                    "1",
                    StringComparison.OrdinalIgnoreCase
                );
        }

        private void SaveJournal(
            MJournal journal)
        {
            if (journal.Save())
            {
                return;
            }

            string errorMessage =
                "Could not save the journal.";

            try
            {
                ValueNamePair modelError =
                    VLogger.RetrieveError();

                if (
                    modelError != null &&
                    !string.IsNullOrWhiteSpace(
                        modelError.GetName()
                    )
                )
                {
                    errorMessage =
                        modelError.GetName();
                }
            }
            catch
            {
            }

            throw new InvalidOperationException(
                errorMessage
            );
        }

        private string GetJournalProcessError(
            MJournal journal,
            string fallback)
        {
            string processMessage =
                journal == null
                    ? string.Empty
                    : journal.GetProcessMsg();

            if (
                !string.IsNullOrWhiteSpace(
                    processMessage
                )
            )
            {
                return processMessage;
            }

            try
            {
                ValueNamePair modelError =
                    VLogger.RetrieveError();

                if (
                    modelError != null &&
                    !string.IsNullOrWhiteSpace(
                        modelError.GetName()
                    )
                )
                {
                    return modelError.GetName();
                }
            }
            catch
            {
            }

            return fallback;
        }

        private bool IsPostingSuccess(
            string postingResult)
        {
            if (
                string.IsNullOrWhiteSpace(
                    postingResult
                )
            )
            {
                return true;
            }

            return
                string.Equals(
                    postingResult,
                    Doc.STATUS_Posted,
                    StringComparison.OrdinalIgnoreCase
                ) ||
                string.Equals(
                    postingResult,
                    "AlreadyPosted",
                    StringComparison.OrdinalIgnoreCase
                ) ||
                string.Equals(
                    postingResult,
                    "OK",
                    StringComparison.OrdinalIgnoreCase
                );
        }

        private string GetPostingErrorMessage(
            Ctx ctx,
            string postingResult)
        {
            string result =
                postingResult == null
                    ? string.Empty
                    : postingResult.Trim();

            if (
                string.Equals(
                    result,
                    Doc.STATUS_NotBalanced,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return GetMsg(
                    ctx,
                    "VAS_044_NotBalanced",
                    "The journal could not be posted because it is not balanced."
                );
            }

            if (
                string.Equals(
                    result,
                    Doc.STATUS_NotConvertible,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return GetMsg(
                    ctx,
                    "VAS_044_NotConvertible",
                    "The journal could not be posted because currency conversion is missing."
                );
            }

            if (
                string.Equals(
                    result,
                    Doc.STATUS_PeriodClosed,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return GetMsg(
                    ctx,
                    "VAS_044_PeriodClosed",
                    "The journal could not be posted because the accounting period is closed."
                );
            }

            if (
                string.Equals(
                    result,
                    Doc.STATUS_InvalidAccount,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return GetMsg(
                    ctx,
                    "VAS_044_InvalidAccount",
                    "The journal could not be posted because one or more accounts are invalid."
                );
            }

            return GetMsg(
                ctx,
                "VAS_044_PostFailed",
                "Journal posting failed."
            );
        }

        private JournalPostingState
            GetJournalPostingState(
                Ctx ctx,
                int journalId)
        {
            string sql = @"
SELECT
    GL_Journal.DocumentNo,
    GL_Journal.DocStatus,
    GL_Journal.Processed,
    GL_Journal.Posted
FROM GL_Journal GL_Journal
WHERE GL_Journal.GL_Journal_ID =
@StateJournalID
AND GL_Journal.AD_Client_ID =
@StateClientID";

            SqlParameter[] parameters =
            {
                new SqlParameter(
                    "@StateJournalID",
                    journalId
                ),

                new SqlParameter(
                    "@StateClientID",
                    ctx.GetAD_Client_ID()
                )
            };

            DataSet dataSet =
                DB.ExecuteDataset(
                    sql,
                    parameters,
                    null
                );

            if (
                dataSet == null ||
                dataSet.Tables.Count == 0 ||
                dataSet.Tables[0].Rows.Count == 0
            )
            {
                return null;
            }

            DataRow row =
                dataSet.Tables[0].Rows[0];

            return new JournalPostingState
            {
                DocumentNo =
                    Util.GetValueOfString(
                        row["DocumentNo"]
                    ),

                DocStatus =
                    Util.GetValueOfString(
                        row["DocStatus"]
                    ),

                Processed =
                    Util.GetValueOfString(
                        row["Processed"]
                    ),

                Posted =
                    Util.GetValueOfString(
                        row["Posted"]
                    )
            };
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
            return JsonString(
                new
                {
                    success = false,

                    error =
                        "Session Expired",

                    errorText =
                        "Session Expired"
                }
            );
        }

        private JsonResult JsonString(
            object value)
        {
            return Json(
                JsonConvert.SerializeObject(
                    value
                ),
                JsonRequestBehavior.AllowGet
            );
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

        private string GetMsg(
            Ctx ctx,
            string key,
            string fallback)
        {
            string message =
                Msg.GetMsg(
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

        private void LogException(
            string actionName,
            Exception exception)
        {
            Trace.TraceError(
                "VAS_044_GLJournalRecentWidgetController." +
                actionName +
                ": " +
                exception
            );
        }

        private class JournalPostingState
        {
            public string DocumentNo
            {
                get;
                set;
            }

            public string DocStatus
            {
                get;
                set;
            }

            public string Processed
            {
                get;
                set;
            }

            public string Posted
            {
                get;
                set;
            }
        }
    }
}