
using CoreLibrary.DataBase;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Web.Mvc;
using VAdvantage.Acct;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Process;
using VAdvantage.Utility;
using VIS.Filters;

namespace VAS.Controllers
{
    /// <summary>
    /// Controller for GL Journal Recent Entries widget.
    /// Displays recent GL journals and supports approval and posting.
    /// </summary>
    public class VAS_044_GLJournalRecentWidgetController : Controller
    {
        private class SchemaInfo
        {
            public int AcctSchemaId { get; set; }

            public string AcctSchemaName { get; set; }

            public string CurSymbol { get; set; }

            public string ISOCode { get; set; }

            public int StdPrecision { get; set; }
        }

        private SchemaInfo GetPrimarySchema(Ctx ctx)
        {
            SchemaInfo info = new SchemaInfo
            {
                AcctSchemaId = 0,
                AcctSchemaName = string.Empty,
                CurSymbol = string.Empty,
                ISOCode = string.Empty,
                StdPrecision = 2
            };

            string schemaSql = @"
SELECT
    C_AcctSchema.C_AcctSchema_ID,
    C_AcctSchema.Name,
    C_Currency.CurSymbol,
    C_Currency.ISO_Code,
    C_Currency.StdPrecision
FROM C_AcctSchema
INNER JOIN C_Currency ON
(
    C_AcctSchema.C_Currency_ID =
    C_Currency.C_Currency_ID
)
WHERE C_AcctSchema.IsActive = 'Y'
AND C_AcctSchema.AD_Client_ID = @ClientID
ORDER BY C_AcctSchema.C_AcctSchema_ID";

            SqlParameter[] schemaParameters =
            {
                new SqlParameter(
                    "@ClientID",
                    ctx.GetAD_Client_ID()
                )
            };

            DataSet schemaDataSet = DB.ExecuteDataset(
                schemaSql,
                schemaParameters,
                null
            );

            if (
                schemaDataSet != null &&
                schemaDataSet.Tables.Count > 0 &&
                schemaDataSet.Tables[0].Rows.Count > 0
            )
            {
                DataRow row =
                    schemaDataSet.Tables[0].Rows[0];

                info.AcctSchemaId =
                    Util.GetValueOfInt(
                        row["C_AcctSchema_ID"]
                    );

                info.AcctSchemaName =
                    Util.GetValueOfString(
                        row["Name"]
                    );

                info.CurSymbol =
                    Util.GetValueOfString(
                        row["CurSymbol"]
                    );

                info.ISOCode =
                    Util.GetValueOfString(
                        row["ISO_Code"]
                    );

                info.StdPrecision =
                    NormalizePrecision(
                        Util.GetValueOfInt(
                            row["StdPrecision"]
                        )
                    );
            }

            return info;
        }

        /// <summary>
        /// Returns journal header and journal lines.
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetJournalEntryDetail(
            int journalId)
        {
            Ctx ctx = GetContext();

            if (ctx == null)
            {
                return GetSessionExpiredResult();
            }

            if (journalId <= 0)
            {
                return Json(
                    JsonConvert.SerializeObject(
                        new
                        {
                            error = true,
                            errorText =
                                "Invalid journal ID."
                        }
                    ),
                    JsonRequestBehavior.AllowGet
                );
            }

            SchemaInfo schema =
                GetPrimarySchema(ctx);

            if (schema.AcctSchemaId <= 0)
            {
                return Json(
                    JsonConvert.SerializeObject(
                        new
                        {
                            error = true,
                            errorText =
                                "Accounting schema was not found."
                        }
                    ),
                    JsonRequestBehavior.AllowGet
                );
            }

            string language =
                ctx.GetAD_Language();

            if (string.IsNullOrWhiteSpace(
                language
            ))
            {
                language = "ar_IQ";
            }

            string headerBase = @"
SELECT
    GL_Journal.GL_Journal_ID,
    GL_Journal.DocumentNo,
    GL_Journal.DateAcct,
    GL_Journal.Description,
    GL_Journal.DocStatus,
    GL_Journal.Posted,
    GL_Journal.Processed,
    GL_Journal.Created,
    DocStatusReference.Name AS DocStatusName,
    AD_User.Name AS CreatedByName,
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
    ) AS TotalCredit
FROM GL_Journal GL_Journal
LEFT OUTER JOIN GL_JournalLine GL_JournalLine ON
(
    GL_Journal.GL_Journal_ID =
    GL_JournalLine.GL_Journal_ID
    AND GL_JournalLine.IsActive = 'Y'
)
LEFT OUTER JOIN
(
    SELECT
        AD_Ref_List.Value,
        AD_Ref_List_Trl.Name
    FROM AD_Reference AD_Reference
    INNER JOIN AD_Ref_List AD_Ref_List ON
    (
        AD_Reference.AD_Reference_ID =
        AD_Ref_List.AD_Reference_ID
    )
    INNER JOIN AD_Ref_List_Trl AD_Ref_List_Trl ON
    (
        AD_Ref_List.AD_Ref_List_ID =
        AD_Ref_List_Trl.AD_Ref_List_ID
        AND AD_Ref_List_Trl.AD_Language =
        @AD_Language
    )
    WHERE AD_Reference.Name = 'DocStatus'
    AND AD_Reference.IsActive = 'Y'
    AND AD_Ref_List.IsActive = 'Y'
) DocStatusReference ON
(
    DocStatusReference.Value =
    GL_Journal.DocStatus
)
LEFT OUTER JOIN AD_User AD_User ON
(
    GL_Journal.CreatedBy =
    AD_User.AD_User_ID
)
WHERE GL_Journal.GL_Journal_ID =
@JournalID
AND GL_Journal.IsActive = 'Y'
AND GL_Journal.C_AcctSchema_ID =
@AcctSchemaID";

            headerBase =
                MRole.GetDefault(ctx)
                    .AddAccessSQL(
                        headerBase,
                        "GL_Journal",
                        MRole.SQL_FULLYQUALIFIED,
                        MRole.SQL_RO
                    );

            string headerSql = headerBase + @"
GROUP BY
    GL_Journal.GL_Journal_ID,
    GL_Journal.DocumentNo,
    GL_Journal.DateAcct,
    GL_Journal.Description,
    GL_Journal.DocStatus,
    GL_Journal.Posted,
    GL_Journal.Processed,
    GL_Journal.Created,
    DocStatusReference.Name,
    AD_User.Name";

            SqlParameter[] headerParameters =
            {
        new SqlParameter(
            "@JournalID",
            journalId
        ),

        new SqlParameter(
            "@AcctSchemaID",
            schema.AcctSchemaId
        ),

        new SqlParameter(
            "@AD_Language",
            language
        )
    };

            DataSet headerDataSet =
                DB.ExecuteDataset(
                    headerSql,
                    headerParameters,
                    null
                );

            if (
                headerDataSet == null ||
                headerDataSet.Tables.Count == 0 ||
                headerDataSet.Tables[0]
                    .Rows.Count == 0
            )
            {
                return Json(
                    JsonConvert.SerializeObject(
                        new
                        {
                            error = true,
                            errorText =
                                "Journal details not found."
                        }
                    ),
                    JsonRequestBehavior.AllowGet
                );
            }

            DataRow headerRow =
                headerDataSet.Tables[0].Rows[0];

            string docStatus =
                Util.GetValueOfString(
                    headerRow["DocStatus"]
                );

            string statusName =
                Util.GetValueOfString(
                    headerRow["DocStatusName"]
                );

            if (string.IsNullOrWhiteSpace(
                statusName
            ))
            {
                statusName = docStatus;
            }

            string dateAcctText =
                string.Empty;

            if (
                headerRow["DateAcct"] !=
                DBNull.Value
            )
            {
                dateAcctText =
                    Convert.ToDateTime(
                        headerRow["DateAcct"]
                    ).ToString(
                        "dd MMM yyyy"
                    );
            }

            string createdText =
                string.Empty;

            if (
                headerRow["Created"] !=
                DBNull.Value
            )
            {
                createdText =
                    Convert.ToDateTime(
                        headerRow["Created"]
                    ).ToString(
                        "dd MMM yyyy"
                    );
            }

            decimal totalDebit =
                Decimal.Round(
                    Util.GetValueOfDecimal(
                        headerRow["TotalDebit"]
                    ),
                    schema.StdPrecision,
                    MidpointRounding.AwayFromZero
                );

            decimal totalCredit =
                Decimal.Round(
                    Util.GetValueOfDecimal(
                        headerRow["TotalCredit"]
                    ),
                    schema.StdPrecision,
                    MidpointRounding.AwayFromZero
                );

            /*
             * GL_JournalLine does not retrieve the account
             * directly from Account_ID.
             *
             * The correct relation is:
             * GL_JournalLine.C_ValidCombination_ID
             * -> C_ValidCombination.Account_ID
             * -> C_ElementValue.C_ElementValue_ID
             */
            string linesSql = @"
SELECT
    GL_JournalLine.GL_JournalLine_ID,
    GL_JournalLine.Line,
    GL_JournalLine.C_ValidCombination_ID,
    ValidCombination.Account_ID,
    AccountValue.Value AS AccountCode,
    AccountValue.Name AS AccountName,
    GL_JournalLine.AmtAcctDr,
    GL_JournalLine.AmtAcctCr,
    CostCenterValue.Value AS CostCenterCode,
    CostCenterValue.Name AS CostCenterName,
    BusinessPartner.Name AS BPartnerName,
    Product.Name AS ProductName,
    Project.Name AS ProjectName
FROM GL_JournalLine GL_JournalLine
LEFT OUTER JOIN C_ValidCombination ValidCombination ON
(
    GL_JournalLine.C_ValidCombination_ID =
    ValidCombination.C_ValidCombination_ID
)
LEFT OUTER JOIN C_ElementValue AccountValue ON
(
    ValidCombination.Account_ID =
    AccountValue.C_ElementValue_ID
)
LEFT OUTER JOIN C_ElementValue CostCenterValue ON
(
    ValidCombination.User1_ID =
    CostCenterValue.C_ElementValue_ID
)
LEFT OUTER JOIN C_BPartner BusinessPartner ON
(
    ValidCombination.C_BPartner_ID =
    BusinessPartner.C_BPartner_ID
)
LEFT OUTER JOIN M_Product Product ON
(
    ValidCombination.M_Product_ID =
    Product.M_Product_ID
)
LEFT OUTER JOIN C_Project Project ON
(
    ValidCombination.C_Project_ID =
    Project.C_Project_ID
)
WHERE GL_JournalLine.GL_Journal_ID =
@JournalID
AND GL_JournalLine.IsActive = 'Y'
ORDER BY
    GL_JournalLine.Line,
    GL_JournalLine.GL_JournalLine_ID";

            SqlParameter[] lineParameters =
            {
        new SqlParameter(
            "@JournalID",
            journalId
        )
    };

            DataSet linesDataSet =
                DB.ExecuteDataset(
                    linesSql,
                    lineParameters,
                    null
                );

            List<object> lines =
                new List<object>();

            if (
                linesDataSet != null &&
                linesDataSet.Tables.Count > 0 &&
                linesDataSet.Tables[0]
                    .Rows.Count > 0
            )
            {
                foreach (
                    DataRow row in
                    linesDataSet.Tables[0].Rows
                )
                {
                    string accountCode =
                        Util.GetValueOfString(
                            row["AccountCode"]
                        );

                    string accountName =
                        Util.GetValueOfString(
                            row["AccountName"]
                        );

                    if (string.IsNullOrWhiteSpace(
                        accountCode
                    ))
                    {
                        accountCode =
                            Util.GetValueOfString(
                                row["Account_ID"]
                            );
                    }

                    if (string.IsNullOrWhiteSpace(
                        accountCode
                    ))
                    {
                        accountCode =
                            Util.GetValueOfString(
                                row[
                                    "C_ValidCombination_ID"
                                ]
                            );
                    }

                    if (string.IsNullOrWhiteSpace(
                        accountName
                    ))
                    {
                        accountName = "-";
                    }

                    string costCenterCode =
                        Util.GetValueOfString(
                            row["CostCenterCode"]
                        );

                    string costCenterName =
                        Util.GetValueOfString(
                            row["CostCenterName"]
                        );

                    string costCenter =
                        string.Empty;

                    if (
                        !string.IsNullOrWhiteSpace(
                            costCenterCode
                        ) &&
                        !string.IsNullOrWhiteSpace(
                            costCenterName
                        )
                    )
                    {
                        costCenter =
                            costCenterCode +
                            " · " +
                            costCenterName;
                    }
                    else if (
                        !string.IsNullOrWhiteSpace(
                            costCenterCode
                        )
                    )
                    {
                        costCenter =
                            costCenterCode;
                    }
                    else if (
                        !string.IsNullOrWhiteSpace(
                            costCenterName
                        )
                    )
                    {
                        costCenter =
                            costCenterName;
                    }

                    string businessPartner =
                        Util.GetValueOfString(
                            row["BPartnerName"]
                        );

                    string product =
                        Util.GetValueOfString(
                            row["ProductName"]
                        );

                    string project =
                        Util.GetValueOfString(
                            row["ProjectName"]
                        );

                    lines.Add(
                        new
                        {
                            AccountCode =
                                accountCode,

                            AccountName =
                                accountName,

                            Debit =
                                Decimal.Round(
                                    Util.GetValueOfDecimal(
                                        row["AmtAcctDr"]
                                    ),
                                    schema.StdPrecision,
                                    MidpointRounding
                                        .AwayFromZero
                                ),

                            Credit =
                                Decimal.Round(
                                    Util.GetValueOfDecimal(
                                        row["AmtAcctCr"]
                                    ),
                                    schema.StdPrecision,
                                    MidpointRounding
                                        .AwayFromZero
                                ),

                            CostCenter =
                                string.IsNullOrWhiteSpace(
                                    costCenter
                                )
                                    ? "-"
                                    : costCenter,

                            BPartner =
                                string.IsNullOrWhiteSpace(
                                    businessPartner
                                )
                                    ? "-"
                                    : businessPartner,

                            Product =
                                string.IsNullOrWhiteSpace(
                                    product
                                )
                                    ? "-"
                                    : product,

                            Project =
                                string.IsNullOrWhiteSpace(
                                    project
                                )
                                    ? "-"
                                    : project
                        }
                    );
                }
            }

            return Json(
                JsonConvert.SerializeObject(
                    new
                    {
                        success = true,

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
                                dateAcctText,

                            Description =
                                Util.GetValueOfString(
                                    headerRow[
                                        "Description"
                                    ]
                                ),

                            DocStatus =
                                docStatus,

                            StatusName =
                                statusName,

                            Posted =
                                Util.GetValueOfString(
                                    headerRow[
                                        "Posted"
                                    ]
                                ),

                            Processed =
                                Util.GetValueOfString(
                                    headerRow[
                                        "Processed"
                                    ]
                                ),

                            TotalDebit =
                                totalDebit,

                            TotalCredit =
                                totalCredit,

                            AccountingBook =
                                string.IsNullOrWhiteSpace(
                                    schema.AcctSchemaName
                                )
                                    ? "Primary"
                                    : schema.AcctSchemaName,

                            CreatedByName =
                                Util.GetValueOfString(
                                    headerRow[
                                        "CreatedByName"
                                    ]
                                ),

                            CreatedDate =
                                createdText
                        },

                        Lines =
                            lines,

                        LineCount =
                            lines.Count,

                        CurSymbol =
                            string.IsNullOrWhiteSpace(
                                schema.CurSymbol
                            )
                                ? schema.ISOCode
                                : schema.CurSymbol,

                        ISOCode =
                            schema.ISOCode,

                        StdPrecision =
                            schema.StdPrecision
                    }
                ),
                JsonRequestBehavior.AllowGet
            );
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
            Ctx ctx = GetContext();

            if (ctx == null)
            {
                return Json(
                    new
                    {
                        success = false,
                        error = "Session Expired",
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
                    throw new
                        InvalidOperationException(
                            "The journal is already posted."
                        );
                }

                string docStatus =
                    journal.GetDocStatus();

                if (
                    string.Equals(
                        docStatus,
                        "AP",
                        StringComparison
                            .OrdinalIgnoreCase
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
                                journal
                                    .GetDocumentNo(),
                            docStatus =
                                journal
                                    .GetDocStatus(),
                            posted =
                                IsJournalPosted(
                                    journal
                                ),
                            message =
                                "The journal is already approved."
                        }
                    );
                }

                if (
                    string.Equals(
                        docStatus,
                        "DR",
                        StringComparison
                            .OrdinalIgnoreCase
                    ) ||
                    string.Equals(
                        docStatus,
                        "NA",
                        StringComparison
                            .OrdinalIgnoreCase
                    )
                )
                {
                    journal.SetDocAction(
                        DocActionVariables
                            .ACTION_PREPARE
                    );

                    if (!journal.ProcessIt(
                        DocActionVariables
                            .ACTION_PREPARE))
                    {
                        throw new
                            InvalidOperationException(
                                GetJournalProcessError(
                                    journal,
                                    "The journal could not be prepared."
                                )
                            );
                    }

                    SaveJournal(journal);

                    docStatus =
                        journal.GetDocStatus();
                }

                if (!string.Equals(
                    docStatus,
                    "IP",
                    StringComparison
                        .OrdinalIgnoreCase))
                {
                    throw new
                        InvalidOperationException(
                            "Only Draft, In Progress or Not Approved journals can be approved."
                        );
                }

                journal.SetDocAction(
                    DocActionVariables
                        .ACTION_APPROVE
                );

                if (!journal.ProcessIt(
                    DocActionVariables
                        .ACTION_APPROVE))
                {
                    throw new
                        InvalidOperationException(
                            GetJournalProcessError(
                                journal,
                                "The journal could not be approved."
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
                            "Journal approved successfully."
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


        [HttpPost]
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult PostJournal(
            int journalId)
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
                ValidateActionRequest(
                    ctx,
                    journalId
                );

            if (validationResult != null)
            {
                return validationResult;
            }

            VAdvantage.DataBase.Trx completeTransaction =
                null;

            try
            {
                /*
                 * Step 1:
                 * Complete the Approved journal first.
                 */
                string completeTransactionName =
                    VAdvantage.DataBase.Trx.CreateTrxName(
                        "VAS044CompleteJournal"
                    );

                completeTransaction =
                    VAdvantage.DataBase.Trx.GetTrx(
                        completeTransactionName
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
                        journalId = journal.Get_ID(),
                        documentNo = journal.GetDocumentNo(),
                        docStatus = journal.GetDocStatus(),
                        posted = true,
                        message = "The journal is already posted."
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
                                journal,
                                "The journal could not be completed."
                            )
                        );
                    }

                    SaveJournal(journal);

                    /*
                     * The Complete process must be committed before
                     * the accounting posting engine reads the journal.
                     */
                    completeTransaction.Commit();
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
                    completeTransaction.Commit();
                }
                else
                {
                    throw new InvalidOperationException(
                        "The journal must be approved before it can be posted."
                    );
                }
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

            /*
             * Step 2:
             * Read the journal again after committing Complete.
             */
            JournalPostingState journalState =
                GetJournalPostingState(
                    journalId
                );

            if (journalState == null)
            {
                return Json(new
                {
                    success = false,
                    error = "Journal details not found.",
                    errorText = "Journal details not found."
                });
            }

            if (string.Equals(
                journalState.Posted,
                "Y",
                StringComparison.OrdinalIgnoreCase))
            {
                return Json(new
                {
                    success = true,
                    journalId = journalId,
                    documentNo = journalState.DocumentNo,
                    docStatus = journalState.DocStatus,
                    posted = true,
                    message = "The journal is already posted."
                });
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
                return Json(new
                {
                    success = false,
                    error =
                        "The journal was not completed successfully. " +
                        "Current status: " +
                        journalState.DocStatus,
                    errorText =
                        "The journal was not completed successfully. " +
                        "Current status: " +
                        journalState.DocStatus
                });
            }

            /*
             * Doc.PostImmediate only loads processed documents.
             */
            if (!string.Equals(
                journalState.Processed,
                "Y",
                StringComparison.OrdinalIgnoreCase))
            {
                return Json(new
                {
                    success = false,
                    error =
                        "The journal is completed but Processed is not Y.",
                    errorText =
                        "The journal is completed but Processed is not Y."
                });
            }

            try
            {
                /*
                 * Step 3:
                 * Use Vienna Advantage accounting posting engine.
                 */
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
                    return Json(new
                    {
                        success = false,
                        error =
                            "No accounting schema was found for the client.",
                        errorText =
                            "No accounting schema was found for the client."
                    });
                }

                string postingResult =
                    Doc.PostImmediate(
                        accountingSchemas,
                        journalTableId,
                        journalId,
                        false,
                        null
                    );

                /*
                 * PostImmediate returns null or empty on success.
                 * Otherwise it returns the actual accounting error.
                 */
                if (!string.IsNullOrWhiteSpace(
                    postingResult
                ))
                {
                    return Json(new
                    {
                        success = false,
                        error = postingResult,
                        errorText = postingResult
                    });
                }

                /*
                 * Step 4:
                 * Verify the Posted value after posting.
                 */
                journalState =
                    GetJournalPostingState(
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
                    string postedStatus =
                        journalState == null
                            ? string.Empty
                            : journalState.Posted;

                    string errorMessage =
                        "The accounting engine finished, " +
                        "but the journal was not posted.";

                    if (!string.IsNullOrWhiteSpace(
                        postedStatus
                    ))
                    {
                        errorMessage +=
                            " Posted status: " +
                            postedStatus;
                    }

                    return Json(new
                    {
                        success = false,
                        error = errorMessage,
                        errorText = errorMessage
                    });
                }

                return Json(new
                {
                    success = true,
                    journalId = journalId,
                    documentNo =
                        journalState.DocumentNo,
                    docStatus =
                        journalState.DocStatus,
                    posted = true,
                    message =
                        "Journal posted successfully."
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





        private string GetJournalProcessError(
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
                /*
                 * Keep fallback when no logger error exists.
                 */
            }

            return fallback;
        }


        private JsonResult ValidateActionRequest(
            Ctx ctx,
            int journalId)
        {
            if (journalId <= 0)
            {
                return Json(
                    new
                    {
                        success = false,
                        error =
                            "Journal details not found.",
                        errorText =
                            "Journal details not found."
                    }
                );
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
                return Json(
                    new
                    {
                        success = false,
                        error =
                            "You do not have permission to update this journal.",
                        errorText =
                            "You do not have permission to update this journal."
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
                throw new
                    InvalidOperationException(
                        "Journal details not found."
                    );
            }

            if (!journal.IsActive())
            {
                throw new
                    InvalidOperationException(
                        "The journal is inactive."
                    );
            }

            if (
                journal.GetAD_Client_ID() !=
                ctx.GetAD_Client_ID()
            )
            {
                throw new
                    InvalidOperationException(
                        "You do not have permission to update this journal."
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
                    StringComparison
                        .OrdinalIgnoreCase
                ) ||
                string.Equals(
                    postedText,
                    "TRUE",
                    StringComparison
                        .OrdinalIgnoreCase
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
                errorMessage =
                    "Could not save the journal.";
            }

            throw new
                InvalidOperationException(
                    errorMessage
                );
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
                JsonConvert.SerializeObject(
                    new
                    {
                        success = false,
                        error = "Session Expired",
                        errorText =
                            "Session Expired"
                    }
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

        private class JournalPostingState
        {
            public string DocumentNo { get; set; }

            public string DocStatus { get; set; }

            public string Processed { get; set; }

            public string Posted { get; set; }
        }

        private JournalPostingState GetJournalPostingState(
            int journalId)
        {
            string sql = @"
SELECT
    GL_Journal.DocumentNo,
    GL_Journal.DocStatus,
    GL_Journal.Processed,
    GL_Journal.Posted
FROM GL_Journal
WHERE GL_Journal.GL_Journal_ID = @JournalID";

            SqlParameter[] parameters =
            {
        new SqlParameter(
            "@JournalID",
            journalId
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


    }
}


