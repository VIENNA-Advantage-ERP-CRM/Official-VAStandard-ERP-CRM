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
        public JsonResult GetRecentEntries(
            int pageNo = 1,
            int pageSize = 5)
        {
            Ctx ctx = GetContext();

            if (ctx == null)
            {
                return GetSessionExpiredResult();
            }

            try
            {
                pageNo =
                    Math.Max(
                        1,
                        pageNo
                    );

                pageSize =
                    Math.Max(
                        1,
                        Math.Min(
                            pageSize,
                            25
                        )
                    );

                int rowStart =
                    ((pageNo - 1) * pageSize) + 1;

                int rowEnd =
                    pageNo * pageSize;

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
@RecentClientID
AND GL_Journal.Created >= @RecentCreatedFrom";

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
        ProtectedJournal.Created,

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
        ProtectedJournal.Created,
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
                JournalTotals.Created DESC,
                JournalTotals.GL_Journal_ID DESC
        ) AS RowNumber,

        COUNT(1) OVER
        (
        ) AS TotalCount

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
    OrderedJournals.StdPrecision,
    OrderedJournals.TotalCount

FROM OrderedJournals OrderedJournals

WHERE OrderedJournals.RowNumber BETWEEN
    @PageRowStart
    AND
    @PageRowEnd

ORDER BY
    OrderedJournals.RowNumber";

                /*
                 * Parameter order matches the placeholder order.
                 * Unique parameter names prevent Oracle binding issues.
                 */
                /* Recent = created within the last 15 days (by GL_Journal.Created). */
                DateTime recentCreatedFrom =
                    DateTime.Now.AddDays(-15);

                SqlParameter[] parameters =
                {
                    new SqlParameter(
                        "@RecentClientID",
                        ctx.GetAD_Client_ID()
                    ),

                    new SqlParameter(
                        "@RecentCreatedFrom",
                        recentCreatedFrom
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
                    ),

                    new SqlParameter(
                        "@PageRowStart",
                        rowStart
                    ),

                    new SqlParameter(
                        "@PageRowEnd",
                        rowEnd
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

                int totalCount =
                    0;

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

                        if (totalCount <= 0)
                        {
                            totalCount =
                                Util.GetValueOfInt(
                                    row["TotalCount"]
                                );
                        }

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
                            totalCount,

                        PageNo =
                            pageNo,

                        PageSize =
                            pageSize,

                        TotalPages =
                            pageSize <= 0
                                ? 0
                                : (int)Math.Ceiling(
                                    totalCount /
                                    (decimal)pageSize
                                ),

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
                    errorKey = "SessionExpired",
                    messageKey = "SessionExpired",
                    error = "SessionExpired",
                    errorText = "SessionExpired"
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
    }
}
