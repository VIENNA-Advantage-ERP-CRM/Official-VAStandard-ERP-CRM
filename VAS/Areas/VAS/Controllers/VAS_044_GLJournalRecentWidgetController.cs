
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
        /// Returns recent GL journals.
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

            SchemaInfo schema =
                GetPrimarySchema(ctx);

            if (schema.AcctSchemaId <= 0)
            {
                return Json(
                    JsonConvert.SerializeObject(
                        new
                        {
                            success = false,
                            error = "Accounting schema was not found.",
                            Entries = new List<object>()
                        }
                    ),
                    JsonRequestBehavior.AllowGet
                );
            }

            string sqlBase = @"
SELECT
    GL_Journal.GL_Journal_ID,
    GL_Journal.DocumentNo,
    GL_Journal.DateAcct,
    GL_Journal.Description,
    GL_Journal.DocStatus,
    GL_Journal.Posted,
    AD_Ref_List.Name AS DocStatusName,
    COALESCE(
        SUM(GL_JournalLine.AmtAcctDr),
        0
    ) AS TotalDebit,
    COALESCE(
        SUM(GL_JournalLine.AmtAcctCr),
        0
    ) AS TotalCredit
FROM GL_Journal
INNER JOIN GL_JournalLine ON
(
    GL_Journal.GL_Journal_ID =
    GL_JournalLine.GL_Journal_ID
    AND GL_JournalLine.IsActive = 'Y'
)
LEFT OUTER JOIN AD_Ref_List ON
(
    AD_Ref_List.AD_Reference_ID = 131
    AND AD_Ref_List.Value =
    GL_Journal.DocStatus
    AND AD_Ref_List.IsActive = 'Y'
)
WHERE GL_Journal.IsActive = 'Y'
AND GL_Journal.C_AcctSchema_ID =
@AcctSchemaID";

            sqlBase =
                MRole.GetDefault(ctx).AddAccessSQL(
                    sqlBase,
                    "GL_Journal",
                    MRole.SQL_FULLYQUALIFIED,
                    MRole.SQL_RO
                );

            string sql = sqlBase + @"
GROUP BY
    GL_Journal.GL_Journal_ID,
    GL_Journal.DocumentNo,
    GL_Journal.DateAcct,
    GL_Journal.Description,
    GL_Journal.DocStatus,
    GL_Journal.Posted,
    AD_Ref_List.Name
ORDER BY
    GL_Journal.DateAcct DESC,
    GL_Journal.GL_Journal_ID DESC
FETCH FIRST 25 ROWS ONLY";

            SqlParameter[] parameters =
            {
                new SqlParameter(
                    "@AcctSchemaID",
                    schema.AcctSchemaId
                )
            };

            DataSet dataSet = DB.ExecuteDataset(
                sql,
                parameters,
                null
            );

            List<object> entries =
                new List<object>();

            object unbalancedEntry = null;

            if (
                dataSet != null &&
                dataSet.Tables.Count > 0 &&
                dataSet.Tables[0].Rows.Count > 0
            )
            {
                foreach (
                    DataRow row in
                    dataSet.Tables[0].Rows
                )
                {
                    int journalId =
                        Util.GetValueOfInt(
                            row["GL_Journal_ID"]
                        );

                    string documentNo =
                        Util.GetValueOfString(
                            row["DocumentNo"]
                        );

                    string description =
                        Util.GetValueOfString(
                            row["Description"]
                        );

                    string docStatus =
                        Util.GetValueOfString(
                            row["DocStatus"]
                        );

                    string statusName =
                        Util.GetValueOfString(
                            row["DocStatusName"]
                        );

                    string posted =
                        Util.GetValueOfString(
                            row["Posted"]
                        );

                    if (string.IsNullOrEmpty(
                        statusName
                    ))
                    {
                        statusName = docStatus;
                    }

                    string dateAcctText =
                        string.Empty;

                    if (
                        row["DateAcct"] !=
                        DBNull.Value
                    )
                    {
                        dateAcctText =
                            Convert.ToDateTime(
                                row["DateAcct"]
                            ).ToString("MMM dd");
                    }

                    decimal totalDebit =
                        Decimal.Round(
                            Util.GetValueOfDecimal(
                                row["TotalDebit"]
                            ),
                            schema.StdPrecision,
                            MidpointRounding
                                .AwayFromZero
                        );

                    decimal totalCredit =
                        Decimal.Round(
                            Util.GetValueOfDecimal(
                                row["TotalCredit"]
                            ),
                            schema.StdPrecision,
                            MidpointRounding
                                .AwayFromZero
                        );

                    bool isUnbalanced =
                        Math.Abs(
                            totalDebit -
                            totalCredit
                        ) > 0m;

                    entries.Add(
                        new
                        {
                            GL_Journal_ID =
                                journalId,

                            DocumentNo =
                                documentNo,

                            DateAcct =
                                dateAcctText,

                            Description =
                                description,

                            DocStatus =
                                docStatus,

                            Posted =
                                posted,

                            StatusName =
                                statusName,

                            TotalDebit =
                                totalDebit,

                            TotalCredit =
                                totalCredit,

                            IsUnbalanced =
                                isUnbalanced
                        }
                    );

                    if (
                        isUnbalanced &&
                        unbalancedEntry == null
                    )
                    {
                        decimal difference =
                            Decimal.Round(
                                Math.Abs(
                                    totalDebit -
                                    totalCredit
                                ),
                                schema.StdPrecision,
                                MidpointRounding
                                    .AwayFromZero
                            );

                        unbalancedEntry =
                            new
                            {
                                GL_Journal_ID =
                                    journalId,

                                DocumentNo =
                                    documentNo,

                                Difference =
                                    difference
                            };
                    }
                }
            }

            return Json(
                JsonConvert.SerializeObject(
                    new
                    {
                        success = true,
                        Entries = entries,
                        UnbalancedEntry =
                            unbalancedEntry,
                        CurSymbol =
                            schema.CurSymbol,
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
        /// Returns journal header and lines.
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
    AD_Ref_List.Name AS DocStatusName,
    AD_User.Name AS CreatedByName,
    COALESCE(
        SUM(GL_JournalLine.AmtAcctDr),
        0
    ) AS TotalDebit,
    COALESCE(
        SUM(GL_JournalLine.AmtAcctCr),
        0
    ) AS TotalCredit
FROM GL_Journal
LEFT OUTER JOIN GL_JournalLine ON
(
    GL_Journal.GL_Journal_ID =
    GL_JournalLine.GL_Journal_ID
    AND GL_JournalLine.IsActive = 'Y'
)
LEFT OUTER JOIN AD_Ref_List ON
(
    AD_Ref_List.AD_Reference_ID = 131
    AND AD_Ref_List.Value =
    GL_Journal.DocStatus
    AND AD_Ref_List.IsActive = 'Y'
)
LEFT OUTER JOIN AD_User ON
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
                MRole.GetDefault(ctx).AddAccessSQL(
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
    AD_Ref_List.Name,
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

            if (string.IsNullOrEmpty(
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
                    MidpointRounding
                        .AwayFromZero
                );

            decimal totalCredit =
                Decimal.Round(
                    Util.GetValueOfDecimal(
                        headerRow["TotalCredit"]
                    ),
                    schema.StdPrecision,
                    MidpointRounding
                        .AwayFromZero
                );

            string linesSql = @"
SELECT
    GL_JournalLine.GL_JournalLine_ID,
    C_ElementValue.Value AS AccountCode,
    C_ElementValue.Name AS AccountName,
    GL_JournalLine.AmtAcctDr,
    GL_JournalLine.AmtAcctCr
FROM GL_JournalLine
INNER JOIN C_ElementValue ON
(
    GL_JournalLine.Account_ID =
    C_ElementValue.C_ElementValue_ID
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
                    lines.Add(
                        new
                        {
                            AccountCode =
                                Util.GetValueOfString(
                                    row["AccountCode"]
                                ),

                            AccountName =
                                Util.GetValueOfString(
                                    row["AccountName"]
                                ),

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

                            CostCenter = "-",
                            BPartner = "-",
                            Product = "-",
                            Project = "-"
                        }
                    );
                }
            }

            return Json(
                JsonConvert.SerializeObject(
                    new
                    {
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

                            StatusName =
                                statusName,

                            TotalDebit =
                                totalDebit,

                            TotalCredit =
                                totalCredit,

                            AccountingBook =
                                string.IsNullOrEmpty(
                                    schema
                                        .AcctSchemaName
                                )
                                    ? "Primary"
                                    : schema
                                        .AcctSchemaName,

                            CreatedByName =
                                Util.GetValueOfString(
                                    headerRow[
                                        "CreatedByName"
                                    ]
                                ),

                            CreatedDate =
                                createdText
                        },

                        Lines = lines,

                        CurSymbol =
                            schema.CurSymbol,

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


