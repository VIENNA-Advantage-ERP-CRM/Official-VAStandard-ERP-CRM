using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Web.Mvc;
using VAdvantage.Acct;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Process;
using VAdvantage.Utility;

/*
 * GL Journal Pending Action Queue Controller
 *
 * -- Labels / Message Keys --------------------------------------------
 *  #  | Current Text                         | Message Key
 * ----+--------------------------------------+--------------------------------
 *  1  | Approval                             | VAS_045_Approval
 *  2  | Post                                 | VAS_045_Post
 *  3  | Resubmit                             | VAS_045_Resubmit
 *  4  | Draft                                | VAS_045_Draft
 *  5  | Load Pending Queue Failed            | VAS_045_LoadPendingQueueFailed
 *  6  | Journal Details Not Found            | VAS_045_JournalDetailsNotFound
 *  7  | Journal Already Posted               | VAS_045_JournalAlreadyPosted
 *  8  | Journal Already Approved             | VAS_045_JournalAlreadyApproved
 *  9  | Invalid Approval Status              | VAS_045_InvalidApprovalStatus
 * 10  | Journal Approved Successfully        | VAS_045_JournalApprovedSuccessfully
 * 11  | Journal Must Be Approved             | VAS_045_JournalMustBeApproved
 * 12  | Journal Posted Successfully          | VAS_045_JournalPostedSuccessfully
 * 13  | No Accounting Schema                 | VAS_045_NoAccountingSchema
 * 14  | Journal Posting Not Completed        | VAS_045_JournalPostingNotCompleted
 * 15  | Journal Not Balanced                 | VAS_045_JournalNotBalanced
 * 16  | Journal Not Convertible              | VAS_045_JournalNotConvertible
 * 17  | Journal Period Closed                | VAS_045_JournalPeriodClosed
 * 18  | Journal Invalid Account              | VAS_045_JournalInvalidAccount
 * 19  | Journal Process Failed               | VAS_045_JournalProcessFailed
 * 20  | Journal Inactive                     | VAS_045_JournalInactive
 * 21  | Could Not Save Journal               | VAS_045_CouldNotSaveJournal
 * ---------------------------------------------------------------------
 */

namespace VAS.Controllers
{
    /// <summary>
    /// Controller for the Pending Action Queue widget — GL_Journal.
    /// Returns GL journals that require user action: Draft, In-Progress (awaiting approval),
    /// Approved (awaiting posting), or Not-Approved (returned for correction).
    /// Linked widgets:
    ///   1. GLJournalPendingWidget — action queue list with urgency markers and zoom-to-record.
    /// Amounts are displayed in the primary accounting schema base currency.
    /// MRole is applied on GL_Journal before ORDER BY.
    /// Age and urgency marker are computed in C# from GL_Journal.Created timestamp.
    /// </summary>
    public class VAS_045_GLJournalPendingWidgetController : Controller
    {

      

        /// <summary>
        /// Returns pending GL journals with translated document status.
        /// Status is returned as Value and Name.
        /// </summary>
        public JsonResult GetPendingQueue(
            int pageNo = 1,
            int pageSize = 2)
        {
            if (Session["ctx"] == null)
            {
                return Json(
                    JsonConvert.SerializeObject(
                        new
                        {
                            success = false,
                    errorKey = "SessionExpired",
                    messageKey = "SessionExpired",
                    error = "Session Expired",
                    errorText = "Session Expired",
                            Queue = new List<object>(),
                            TotalCount = 0
                        }
                    ),
                    JsonRequestBehavior.AllowGet
                );
            }

            Ctx ctx =
                Session["ctx"] as Ctx;

            if (ctx == null)
            {
                return Json(
                    JsonConvert.SerializeObject(
                        new
                        {
                            success = false,
                    errorKey = "SessionExpired",
                    messageKey = "SessionExpired",
                    error = "Session Expired",
                    errorText = "Session Expired",
                            Queue = new List<object>(),
                            TotalCount = 0
                        }
                    ),
                    JsonRequestBehavior.AllowGet
                );
            }

            IDataReader reader =
                null;

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
                    ctx.GetAD_Language();

                if (
                    string.IsNullOrWhiteSpace(
                        language
                    )
                )
                {
                    language =
                        "en_US";
                }

                /*
                 * Prevent Oracle ORA-12704.
                 *
                 * Oracle:
                 * VARCHAR2(4000)
                 *
                 * PostgreSQL:
                 * VARCHAR(4000)
                 */
                string textCastType =
                    CoreLibrary.DataBase.DB.IsOracle()
                        ? "VARCHAR2(4000)"
                        : "VARCHAR(4000)";

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
    GL_Journal.Description,
    GL_Journal.DocStatus,
    GL_Journal.Posted,
    GL_Journal.Processed,
    GL_Journal.Created,
    GL_Journal.CreatedBy
FROM GL_Journal GL_Journal
WHERE GL_Journal.IsActive = 'Y'
AND GL_Journal.AD_Client_ID =
@PendingClientID
AND GL_Journal.DocStatus IN
(
    'DR',
    'IP',
    'AP',
    'NA'
)";

                protectedJournalSql =
                    MRole.GetDefault(ctx)
                        .AddAccessSQL(
                            protectedJournalSql,
                            "GL_Journal",
                            MRole.SQL_FULLYQUALIFIED,
                            MRole.SQL_RO
                        );

                /*
                 * One query provides:
                 *
                 * 1. Primary accounting schema and currency.
                 * 2. Protected GL journal records.
                 * 3. Journal debit totals.
                 * 4. Translated DocStatus reference.
                 * 5. Total pending count.
                 * 6. First 25 pending rows.
                 */
                string sql = @"
WITH SchemaCurrency AS
(
    SELECT
        ClientInfo.AD_Client_ID,
        AcctSchema.C_AcctSchema_ID,
        Currency.CurSymbol,
        Currency.ISO_Code,
        Currency.StdPrecision
    FROM AD_ClientInfo ClientInfo
    INNER JOIN C_AcctSchema AcctSchema ON
    (
        ClientInfo.C_AcctSchema1_ID =
        AcctSchema.C_AcctSchema_ID
    )
    INNER JOIN C_Currency Currency ON
    (
        AcctSchema.C_Currency_ID =
        Currency.C_Currency_ID
    )
    WHERE ClientInfo.IsActive = 'Y'
    AND AcctSchema.IsActive = 'Y'
    AND Currency.IsActive = 'Y'
    AND ClientInfo.AD_Client_ID =
    @SchemaClientID
),
ProtectedJournal AS
(
" + protectedJournalSql + @"
),
JournalLineTotals AS
(
    SELECT
        GL_JournalLine.GL_Journal_ID,

        COALESCE(
            SUM(
                COALESCE(
                    GL_JournalLine.AmtAcctDr,
                    0
                )
            ),
            0
        ) AS TotalDebit

    FROM GL_JournalLine GL_JournalLine

    WHERE GL_JournalLine.IsActive = 'Y'

    GROUP BY
        GL_JournalLine.GL_Journal_ID
),
DocumentStatusReferenceSource AS
(
    SELECT
        CAST
        (
            RefList.Value AS " + textCastType + @"
        ) AS StatusValue,

        CAST
        (
            RefList.Name AS " + textCastType + @"
        ) AS StatusBaseName,

        CAST
        (
            RefListTrl.Name AS " + textCastType + @"
        ) AS StatusTranslatedName,

        ROW_NUMBER() OVER
        (
            PARTITION BY
                CAST
                (
                    RefList.Value AS " + textCastType + @"
                )

            ORDER BY
                CASE
                    WHEN RefListTrl.Name IS NOT NULL
                    THEN 0
                    ELSE 1
                END,
                RefList.AD_Ref_List_ID
        ) AS ReferenceRowNumber

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
        @StatusLanguage
    )

    WHERE TableInfo.TableName =
        'GL_Journal'

    AND ColumnInfo.ColumnName =
        'DocStatus'

    AND TableInfo.IsActive = 'Y'
    AND ColumnInfo.IsActive = 'Y'
    AND ReferenceInfo.IsActive = 'Y'
    AND RefList.IsActive = 'Y'
),
DocumentStatusReference AS
(
    SELECT
        DocumentStatusReferenceSource.StatusValue,
        DocumentStatusReferenceSource.StatusBaseName,
        DocumentStatusReferenceSource.StatusTranslatedName

    FROM DocumentStatusReferenceSource DocumentStatusReferenceSource

    WHERE DocumentStatusReferenceSource.ReferenceRowNumber = 1
),
PendingRows AS
(
    SELECT
        ProtectedJournal.GL_Journal_ID,
        ProtectedJournal.DocumentNo,
        ProtectedJournal.Description,
        ProtectedJournal.DocStatus,
        ProtectedJournal.Posted,
        ProtectedJournal.Processed,
        ProtectedJournal.Created,

        StatusReference.StatusBaseName,
        StatusReference.StatusTranslatedName,

        CreatedUser.Name AS UserName,

        COALESCE(
            JournalLineTotals.TotalDebit,
            0
        ) AS TotalDebit,

        SchemaCurrency.CurSymbol,
        SchemaCurrency.ISO_Code,
        SchemaCurrency.StdPrecision,

        COUNT(1) OVER
        (
        ) AS TotalCount,

        ROW_NUMBER() OVER
        (
            ORDER BY
                ProtectedJournal.Created ASC,
                ProtectedJournal.GL_Journal_ID ASC
        ) AS RowNumber

    FROM ProtectedJournal ProtectedJournal

    INNER JOIN SchemaCurrency SchemaCurrency ON
    (
        SchemaCurrency.AD_Client_ID =
        ProtectedJournal.AD_Client_ID

        AND SchemaCurrency.C_AcctSchema_ID =
        ProtectedJournal.C_AcctSchema_ID
    )

    LEFT OUTER JOIN JournalLineTotals JournalLineTotals ON
    (
        ProtectedJournal.GL_Journal_ID =
        JournalLineTotals.GL_Journal_ID
    )

    LEFT OUTER JOIN AD_User CreatedUser ON
    (
        ProtectedJournal.CreatedBy =
        CreatedUser.AD_User_ID

        AND CreatedUser.IsActive = 'Y'
    )

    LEFT OUTER JOIN DocumentStatusReference StatusReference ON
    (
        CAST(
            ProtectedJournal.DocStatus AS " + textCastType + @"
        ) =
        StatusReference.StatusValue
    )
)
SELECT
    PendingRows.GL_Journal_ID,
    PendingRows.DocumentNo,
    PendingRows.Description,
    PendingRows.DocStatus,
    PendingRows.Posted,
    PendingRows.Processed,
    PendingRows.Created,
    PendingRows.StatusBaseName,
    PendingRows.StatusTranslatedName,
    PendingRows.UserName,
    PendingRows.TotalDebit,
    PendingRows.CurSymbol,
    PendingRows.ISO_Code,
    PendingRows.StdPrecision,
    PendingRows.TotalCount,
    PendingRows.RowNumber

FROM PendingRows PendingRows

WHERE PendingRows.RowNumber BETWEEN
    @PageRowStart
    AND
    @PageRowEnd

ORDER BY
    PendingRows.RowNumber";

                /*
                 * Placeholder order:
                 *
                 * 1. @SchemaClientID
                 * 2. @PendingClientID
                 * 3. @StatusLanguage
                 * Every bind variable appears exactly once.
                 */
                SqlParameter[] parameters =
                {
            new SqlParameter(
                "@SchemaClientID",
                ctx.GetAD_Client_ID()
            ),

            new SqlParameter(
                "@PendingClientID",
                ctx.GetAD_Client_ID()
            ),

            new SqlParameter(
                "@StatusLanguage",
                language
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

                reader =
                    CoreLibrary.DataBase.DB.ExecuteReader(
                        sql,
                        parameters,
                        null
                    );

                List<object> queue =
                    new List<object>();

                int totalCount =
                    0;

                string curSymbol =
                    string.Empty;

                string isoCode =
                    string.Empty;

                int stdPrecision =
                    2;

                DateTime now =
                    DateTime.Now;

                while (reader.Read())
                {
                    int journalId =
                        Util.GetValueOfInt(
                            reader["GL_Journal_ID"]
                        );

                    string documentNo =
                        Util.GetValueOfString(
                            reader["DocumentNo"]
                        );

                    string description =
                        Util.GetValueOfString(
                            reader["Description"]
                        );

                    /*
                     * Original stored reference value.
                     * Example:
                     *
                     * DR
                     * IP
                     * AP
                     * NA
                     */
                    string statusValue =
                        Util.GetValueOfString(
                            reader["DocStatus"]
                        );

                    /*
                     * Translation fallback order:
                     *
                     * 1. AD_Ref_List_Trl.Name
                     * 2. AD_Ref_List.Name
                     * 3. Original DocStatus value
                     */
                    string translatedStatusName =
                        Util.GetValueOfString(
                            reader[
                                "StatusTranslatedName"
                            ]
                        );

                    string baseStatusName =
                        Util.GetValueOfString(
                            reader[
                                "StatusBaseName"
                            ]
                        );

                    string statusName;

                    if (
                        !string.IsNullOrWhiteSpace(
                            translatedStatusName
                        )
                    )
                    {
                        statusName =
                            translatedStatusName;
                    }
                    else if (
                        !string.IsNullOrWhiteSpace(
                            baseStatusName
                        )
                    )
                    {
                        statusName =
                            baseStatusName;
                    }
                    else
                    {
                        statusName =
                            statusValue;
                    }

                    string userName =
                        Util.GetValueOfString(
                            reader["UserName"]
                        );

                    if (
                        string.IsNullOrWhiteSpace(
                            userName
                        )
                    )
                    {
                        userName =
                            "-";
                    }

                    stdPrecision =
                        (
                            Util.GetValueOfInt(
                                reader["StdPrecision"]
                            )
                        );

                    decimal totalDebit =
                        Decimal.Round(
                            Util.GetValueOfDecimal(
                                reader["TotalDebit"]
                            ),
                            stdPrecision,
                            MidpointRounding.AwayFromZero
                        );

                    totalCount =
                        Util.GetValueOfInt(
                            reader["TotalCount"]
                        );

                    curSymbol =
                        Util.GetValueOfString(
                            reader["CurSymbol"]
                        );

                    isoCode =
                        Util.GetValueOfString(
                            reader["ISO_Code"]
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

                    DateTime created =
                        reader["Created"] != DBNull.Value
                            ? Convert.ToDateTime(
                                reader["Created"]
                            )
                            : now;

                    double totalHours =
                        (now - created).TotalHours;

                    if (totalHours < 0)
                    {
                        totalHours =
                            0;
                    }

                    string ageText;

                    if (totalHours < 1)
                    {
                        ageText =
                            "< 1h";
                    }
                    else if (totalHours < 24)
                    {
                        ageText =
                            ((int)totalHours) +
                            "h";
                    }
                    else if (totalHours < 48)
                    {
                        ageText =
                            "1d";
                    }
                    else
                    {
                        ageText =
                            (
                                (int)(
                                    totalHours / 24
                                )
                            ) +
                            "d";
                    }

                    string markerType;

                    if (totalHours >= 48)
                    {
                        markerType =
                            "danger";
                    }
                    else if (totalHours >= 24)
                    {
                        markerType =
                            "warn";
                    }
                    else
                    {
                        markerType =
                            "info";
                    }

                    string actionLabel;

                    switch (
                        statusValue.ToUpperInvariant()
                    )
                    {
                        case "IP":
                            actionLabel =
                                GetMsg(
                                    ctx,
                                    "VAS_045_Approval",
                                    "Approval"
                                );
                            break;

                        case "AP":
                            actionLabel =
                                GetMsg(
                                    ctx,
                                    "VAS_045_Post",
                                    "Post"
                                );
                            break;

                        case "NA":
                            actionLabel =
                                GetMsg(
                                    ctx,
                                    "VAS_045_Resubmit",
                                    "Resubmit"
                                );
                            break;

                        default:
                            actionLabel =
                                GetMsg(
                                    ctx,
                                    "VAS_045_Draft",
                                    "Draft"
                                );
                            break;
                    }

                    queue.Add(
                        new
                        {
                            GL_Journal_ID =
                                journalId,

                            DocumentNo =
                                documentNo,

                            Description =
                                description,

                            /*
                             * Existing field retained for compatibility.
                             */
                            DocStatus =
                                statusValue,

                            /*
                             * Separate value and name fields.
                             */
                            StatusValue =
                                statusValue,

                            StatusName =
                                statusName,

                            /*
                             * Requested reference object.
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
                                    reader["Posted"]
                                ),

                            Processed =
                                Util.GetValueOfString(
                                    reader["Processed"]
                                ),

                            ActionLabel =
                                actionLabel,

                            MarkerType =
                                markerType,

                            AgeStr =
                                ageText,

                            IsOverdue =
                                totalHours >= 48,

                            TotalDebit =
                                totalDebit,

                            UserName =
                                userName
                        }
                    );
                }

                return Json(
                    JsonConvert.SerializeObject(
                        new
                        {
                            success = true,
                            error = string.Empty,

                            Queue =
                                queue,

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
                    ),
                    JsonRequestBehavior.AllowGet
                );
            }
            catch (Exception)
            {
                string errorMessage =
                    GetMsg(
                        ctx,
                        "VAS_045_LoadPendingQueueFailed",
                        "Error loading pending journal queue."
                    );

                return Json(
                    JsonConvert.SerializeObject(
                        new
                        {
                            success = false,

                            error =
                                errorMessage,

                            errorText =
                                errorMessage,

                            Queue =
                                new List<object>(),

                            TotalCount =
                                0
                        }
                    ),
                    JsonRequestBehavior.AllowGet
                );
            }
            finally
            {
                if (reader != null)
                {
                    reader.Close();
                    reader.Dispose();
                    reader = null;
                }
            }
        }

        /// <summary>
        /// Prepares and approves the selected GL Journal.
        /// Draft and Not Approved journals are prepared first.
        /// </summary>
        [HttpPost]
        public JsonResult ApproveJournal(int journalId)
        {
            Ctx ctx = Session["ctx"] as Ctx;

            if (ctx == null)
            {
                return Json(new
                {
                    success = false,
                    errorKey = "SessionExpired",
                    messageKey = "SessionExpired",
                    error = "Session Expired",
                    errorText = "Session Expired"
                });
            }

            if (journalId <= 0)
            {
                return Json(new
                {
                    success = false,
                    error = GetMsg(
                        ctx,
                        "VAS_045_JournalDetailsNotFound",
                        "Journal details not found"
                    )
                });
            }

            int journalTableId =
                MTable.Get_Table_ID("GL_Journal");

            MRole role = MRole.GetDefault(ctx);

            if (!role.IsRecordAccess(
                journalTableId,
                journalId,
                false))
            {
                return Json(new
                {
                    success = false,
                    error = GetMsg(
                        ctx,
                        "AccessTableNoUpdate",
                        "You do not have permission to update this journal."
                    )
                });
            }

            string trxName =
                Trx.CreateTrxName(
                    "VAS045ApproveJournal"
                );

            Trx trx = Trx.GetTrx(trxName);

            try
            {
                MJournal journal = new MJournal(
                    ctx,
                    journalId,
                    trx
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
                            "VAS_045_JournalAlreadyPosted",
                            "The journal is already posted."
                        )
                    );
                }

                string docStatus =
                    journal.GetDocStatus();

                /*
                 * Draft and Not Approved cannot be approved directly.
                 * Prepare first so the model validates:
                 * - accounting period
                 * - journal lines
                 * - active accounts
                 * - debit and credit balance
                 * - control amount
                 */
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

                    if (!journal.ProcessIt(
                        DocActionVariables.ACTION_PREPARE))
                    {
                        throw new InvalidOperationException(
                            GetJournalProcessError(
                                ctx,
                                journal,
                                "The journal could not be prepared."
                            )
                        );
                    }

                    SaveJournal(
                        ctx,
                        journal
                    );

                    docStatus =
                        journal.GetDocStatus();
                }

                if (
                    string.Equals(
                        docStatus,
                        "AP",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    trx.Commit();

                    return Json(new
                    {
                        success = true,
                        journalId = journal.Get_ID(),
                        documentNo = journal.GetDocumentNo(),
                        docStatus = journal.GetDocStatus(),
                        posted = IsJournalPosted(journal),
                        message = GetMsg(
                            ctx,
                            "VAS_045_JournalAlreadyApproved",
                            "The journal is already approved."
                        )
                    });
                }

                if (!string.Equals(
                    docStatus,
                    "IP",
                    StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        GetMsg(
                            ctx,
                            "VAS_045_InvalidApprovalStatus",
                            "Only Draft, In Progress or Not Approved journals can be approved."
                        )
                    );
                }

                journal.SetDocAction(
                    DocActionVariables.ACTION_APPROVE
                );

                if (!journal.ProcessIt(
                    DocActionVariables.ACTION_APPROVE))
                {
                    throw new InvalidOperationException(
                        GetJournalProcessError(
                            ctx,
                            journal,
                            "The journal could not be approved."
                        )
                    );
                }

                SaveJournal(
                    ctx,
                    journal
                );

                trx.Commit();

                return Json(new
                {
                    success = true,
                    journalId = journal.Get_ID(),
                    documentNo = journal.GetDocumentNo(),
                    docStatus = journal.GetDocStatus(),
                    posted = IsJournalPosted(journal),
                    message = GetMsg(
                        ctx,
                        "VAS_045_JournalApprovedSuccessfully",
                        "Journal approved successfully."
                    )
                });
            }
            catch (Exception ex)
            {
                if (trx != null)
                {
                    trx.Rollback();
                }

                return Json(new
                {
                    success = false,
                    error = ex.Message,
                    errorText = ex.Message
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

        [HttpPost]
        public JsonResult PostJournal(
            int journalId)
        {
            Ctx ctx = Session["ctx"] as Ctx;

            if (ctx == null)
            {
                return Json(new
                {
                    success = false,
                    errorKey = "SessionExpired",
                    messageKey = "SessionExpired",
                    error = "Session Expired",
                    errorText = "Session Expired"
                });
            }

            if (journalId <= 0)
            {
                return Json(new
                {
                    success = false,
                    error = GetMsg(
                        ctx,
                        "VAS_045_JournalDetailsNotFound",
                        "Journal details not found."
                    ),
                    errorText = GetMsg(
                        ctx,
                        "VAS_045_JournalDetailsNotFound",
                        "Journal details not found."
                    )
                });
            }

            int journalTableId =
                MTable.Get_Table_ID(
                    "GL_Journal"
                );

            MRole role =
                MRole.GetDefault(ctx);

            if (!role.IsRecordAccess(
                journalTableId,
                journalId,
                false))
            {
                return Json(new
                {
                    success = false,
                    error = GetMsg(
                        ctx,
                        "AccessTableNoUpdate",
                        "You do not have permission to update this journal."
                    ),
                    errorText = GetMsg(
                        ctx,
                        "AccessTableNoUpdate",
                        "You do not have permission to update this journal."
                    )
                });
            }

            Trx completeTransaction = null;

            try
            {
                /*
                 * Step 1:
                 * Complete the approved journal.
                 */
                string transactionName =
                    Trx.CreateTrxName(
                        "VAS045CompleteJournal"
                    );

                completeTransaction =
                    Trx.GetTrx(
                        transactionName
                    );

                MJournal journal =
                    new MJournal(
                        ctx,
                        journalId,
                        completeTransaction
                    );

                ValidateJournal(
                    ctx,
                    journal,
                    journalId
                );

                if (IsJournalPosted(journal))
                {
                    completeTransaction.Commit();

                    return Json(new
                    {
                        success = true,
                        journalId =
                            journal.Get_ID(),

                        documentNo =
                            journal.GetDocumentNo(),

                        docStatus =
                            journal.GetDocStatus(),

                        posted = true,

                        message = GetMsg(
                            ctx,
                            "VAS_045_JournalAlreadyPosted",
                            "The journal is already posted."
                        )
                    });
                }

                string docStatus =
                    journal.GetDocStatus();

                if (string.Equals(
                    docStatus,
                    "AP",
                    StringComparison.OrdinalIgnoreCase))
                {
                    journal.SetDocAction(
                        DocActionVariables.ACTION_COMPLETE
                    );

                    if (!journal.ProcessIt(
                        DocActionVariables.ACTION_COMPLETE))
                    {
                        throw new InvalidOperationException(
                            GetJournalProcessError(
                                ctx,
                                journal,
                                "The journal could not be completed."
                            )
                        );
                    }

                    SaveJournal(
                        ctx,
                        journal
                    );
                }
                else if (
                    !string.Equals(
                        docStatus,
                        "CO",
                        StringComparison.OrdinalIgnoreCase
                    ) &&
                    !string.Equals(
                        docStatus,
                        "CL",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    throw new InvalidOperationException(
                        GetMsg(
                            ctx,
                            "VAS_045_JournalMustBeApproved",
                            "The journal must be approved before it can be posted."
                        )
                    );
                }

                /*
                 * Complete must be committed before accounting posting.
                 */
                completeTransaction.Commit();
            }
            catch (Exception exception)
            {
                if (completeTransaction != null)
                {
                    completeTransaction.Rollback();
                }

                return Json(new
                {
                    success = false,
                    error = exception.Message,
                    errorText = exception.Message
                });
            }
            finally
            {
                if (completeTransaction != null)
                {
                    completeTransaction.Close();
                    completeTransaction = null;
                }
            }

            try
            {
                /*
                 * Step 2:
                 * Reload after committing Complete.
                 */
                MJournal completedJournal =
                    new MJournal(
                        ctx,
                        journalId,
                        null
                    );

                ValidateJournal(
                    ctx,
                    completedJournal,
                    journalId
                );

                if (IsJournalPosted(
                    completedJournal))
                {
                    return Json(new
                    {
                        success = true,

                        journalId =
                            completedJournal.Get_ID(),

                        documentNo =
                            completedJournal.GetDocumentNo(),

                        docStatus =
                            completedJournal.GetDocStatus(),

                        posted = true,

                        message = GetMsg(
                            ctx,
                            "VAS_045_JournalPostedSuccessfully",
                            "Journal posted successfully."
                        )
                    });
                }

                string completedStatus =
                    completedJournal.GetDocStatus();

                if (
                    !string.Equals(
                        completedStatus,
                        "CO",
                        StringComparison.OrdinalIgnoreCase
                    ) &&
                    !string.Equals(
                        completedStatus,
                        "CL",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    throw new InvalidOperationException(
                        GetMsg(
                            ctx,
                            "VAS_045_JournalPostingNotCompletedStatus",
                            "The journal was not completed successfully. Current status: {0}"
                        ).Replace(
                            "{0}",
                            completedStatus
                        )
                    );
                }

                if (!IsJournalProcessed(
                    completedJournal))
                {
                    throw new InvalidOperationException(
                        "The journal was completed, but Processed is not Y."
                    );
                }

                /*
                 * Step 3:
                 * Run the actual Vienna Advantage accounting engine.
                 */
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
                    throw new InvalidOperationException(
                        GetMsg(
                            ctx,
                            "VAS_045_NoAccountingSchema",
                            "No accounting schema was found for this client."
                        )
                    );
                }

                /*
                 * Important:
                 * Do not pass the Complete transaction.
                 * Completion has already been committed.
                 */
                string postingResult =
                    Doc.PostImmediate(
                        accountingSchemas,
                        journalTableId,
                        journalId,
                        false,
                        null
                    );

                if (!IsPostingSuccess(
                    postingResult))
                {
                    throw new InvalidOperationException(
                        GetPostingErrorMessage(
                            ctx,
                            postingResult
                        )
                    );
                }

                /*
                 * Step 4:
                 * Reload from database and verify Posted.
                 */
                MJournal postedJournal =
                    new MJournal(
                        ctx,
                        journalId,
                        null
                    );

                ValidateJournal(
                    ctx,
                    postedJournal,
                    journalId
                );

                if (!IsJournalPosted(
                    postedJournal))
                {
                    throw new InvalidOperationException(
                        GetMsg(
                            ctx,
                            "VAS_045_JournalPostingNotCompleted",
                            "The accounting engine finished, but the journal was not posted."
                        )
                    );
                }

                return Json(new
                {
                    success = true,

                    journalId =
                        postedJournal.Get_ID(),

                    documentNo =
                        postedJournal.GetDocumentNo(),

                    docStatus =
                        postedJournal.GetDocStatus(),

                    posted = true,

                    message = GetMsg(
                        ctx,
                        "VAS_045_JournalPostedSuccessfully",
                        "Journal posted successfully."
                    )
                });
            }
            catch (Exception exception)
            {
                return Json(new
                {
                    success = false,
                    error = exception.Message,
                    errorText = exception.Message
                });
            }
        }


        private bool IsJournalProcessed(
            MJournal journal)
        {
            if (journal == null)
            {
                return false;
            }

            object processedValue =
                journal.Get_Value(
                    "Processed"
                );

            if (
                processedValue == null ||
                processedValue == DBNull.Value
            )
            {
                return false;
            }

            if (processedValue is bool)
            {
                return (bool)processedValue;
            }

            string processedText =
                Util.GetValueOfString(
                    processedValue
                );

            return
                string.Equals(
                    processedText,
                    "Y",
                    StringComparison.OrdinalIgnoreCase
                ) ||
                string.Equals(
                    processedText,
                    "TRUE",
                    StringComparison.OrdinalIgnoreCase
                ) ||
                string.Equals(
                    processedText,
                    "1",
                    StringComparison.OrdinalIgnoreCase
                );
        }

        private bool IsPostingSuccess(
            string postingResult)
        {
            /*
             * Doc.PostImmediate normally returns null or empty
             * when posting succeeds.
             */
            if (string.IsNullOrWhiteSpace(
                postingResult
            ))
            {
                return true;
            }

            if (string.Equals(
                postingResult,
                Doc.STATUS_Posted,
                StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(
                postingResult,
                "AlreadyPosted",
                StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(
                postingResult,
                "OK",
                StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }


        private string GetPostingErrorMessage(
            Ctx ctx,
            string postingResult)
        {
            string result =
                postingResult == null
                    ? string.Empty
                    : postingResult.Trim();

            /*
             * Try to retrieve the detailed model error first.
             */
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
                    if (string.IsNullOrWhiteSpace(
                        result
                    ))
                    {
                        return loggerError.GetName();
                    }

                    return
                        result +
                        " - " +
                        loggerError.GetName();
                }
            }
            catch
            {
                /*
                 * Continue with the posting status.
                 */
            }

            if (string.Equals(
                result,
                Doc.STATUS_NotBalanced,
                StringComparison.OrdinalIgnoreCase))
            {
                return GetMsg(
                    ctx,
                    "VAS_045_JournalNotBalanced",
                    "The journal could not be posted because it is not balanced."
                );
            }

            if (string.Equals(
                result,
                Doc.STATUS_NotConvertible,
                StringComparison.OrdinalIgnoreCase))
            {
                return GetMsg(
                    ctx,
                    "VAS_045_JournalNotConvertible",
                    "The journal could not be posted because currency conversion is missing."
                );
            }

            if (string.Equals(
                result,
                Doc.STATUS_PeriodClosed,
                StringComparison.OrdinalIgnoreCase))
            {
                return GetMsg(
                    ctx,
                    "VAS_045_JournalPeriodClosed",
                    "The journal could not be posted because the accounting period is closed."
                );
            }

            if (string.Equals(
                result,
                Doc.STATUS_InvalidAccount,
                StringComparison.OrdinalIgnoreCase))
            {
                return GetMsg(
                    ctx,
                    "VAS_045_JournalInvalidAccount",
                    "The journal could not be posted because one or more accounts are invalid."
                );
            }

            if (string.Equals(
                result,
                "NoDoc",
                StringComparison.OrdinalIgnoreCase))
            {
                return
                    "The accounting document was not found. " +
                    "Verify that the journal is Completed, Processed = Y, " +
                    "and GL Journal is configured as an accounting document.";
            }

            if (string.IsNullOrWhiteSpace(
                result
            ))
            {
                return GetMsg(
                    ctx,
                    "VAS_045_JournalProcessFailed",
                    "The journal could not be posted."
                );
            }

            /*
             * Return the real result instead of replacing it
             * with a generic error.
             */
            return
                GetMsg(
                    ctx,
                    "VAS_045_AccountingPostingFailed",
                    "Accounting posting failed: {0}"
                ).Replace(
                    "{0}",
                    result
                );
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
                        "VAS_045_JournalDetailsNotFound",
                        "Journal details not found"
                    )
                );
            }

            if (!journal.IsActive())
            {
                throw new InvalidOperationException(
                    GetMsg(
                        ctx,
                        "VAS_045_JournalInactive",
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
                        "AccessTableNoUpdate",
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
                journal.Get_Value("Posted");

            if (postedValue == null ||
                postedValue == DBNull.Value)
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
                );
        }

        private void SaveJournal(
            Ctx ctx,
            MJournal journal)
        {
            if (journal.Save())
            {
                return;
            }

            string errorMessage = GetMsg(
                ctx,
                "VAS_045_CouldNotSaveJournal",
                "Could not save the journal."
            );

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
                errorMessage = GetMsg(
                    ctx,
                    "VAS_045_CouldNotSaveJournal",
                    "Could not save the journal."
                );
            }

            throw new InvalidOperationException(
                errorMessage
            );
        }

        private string GetJournalProcessError(
            Ctx ctx,
            MJournal journal,
            string fallback)
        {
            string processMessage =
                journal == null
                    ? string.Empty
                    : journal.GetProcessMsg();

            if (!string.IsNullOrWhiteSpace(
                processMessage
            ))
            {
                return processMessage;
            }

            return GetMsg(
                ctx,
                "VAS_045_JournalProcessFailed",
                fallback
            );
        }



        private string GetPostingResultMessage(
            Ctx ctx,
            string postingResult)
        {
            if (string.Equals(
                postingResult,
                Doc.STATUS_NotBalanced,
                StringComparison.OrdinalIgnoreCase))
            {
                return GetMsg(
                    ctx,
                    "VAS_045_JournalNotBalanced",
                    "The journal could not be posted because it is not balanced."
                );
            }

            if (string.Equals(
                postingResult,
                Doc.STATUS_NotConvertible,
                StringComparison.OrdinalIgnoreCase))
            {
                return GetMsg(
                    ctx,
                    "VAS_045_JournalNotConvertible",
                    "The journal could not be posted because currency conversion is missing."
                );
            }

            if (string.Equals(
                postingResult,
                Doc.STATUS_PeriodClosed,
                StringComparison.OrdinalIgnoreCase))
            {
                return GetMsg(
                    ctx,
                    "VAS_045_JournalPeriodClosed",
                    "The journal could not be posted because the accounting period is closed."
                );
            }

            if (string.Equals(
                postingResult,
                Doc.STATUS_InvalidAccount,
                StringComparison.OrdinalIgnoreCase))
            {
                return GetMsg(
                    ctx,
                    "VAS_045_JournalInvalidAccount",
                    "The journal could not be posted because one or more accounts are invalid."
                );
            }

            if (string.Equals(
                postingResult,
                Doc.STATUS_Error,
                StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    postingResult,
                    Doc.STATUS_DocumentError,
                    StringComparison.OrdinalIgnoreCase))
            {
                return GetMsg(
                    ctx,
                    "VAS_045_JournalProcessFailed",
                    "The journal could not be posted."
                );
            }

            return GetMsg(
                ctx,
                postingResult,
                postingResult
            );
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

            string message =
                Msg.GetMsg(
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


    }
}
