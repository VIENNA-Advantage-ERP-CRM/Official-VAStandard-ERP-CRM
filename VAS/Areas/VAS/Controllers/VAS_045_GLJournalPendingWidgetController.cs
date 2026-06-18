using CoreLibrary.DataBase;
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
        /// Returns pending GL journals (DocStatus IN DR, IP, AP, NA) ordered oldest-first,
        /// capped at 15 display rows. TotalCount reflects all pending records.
        /// </summary>
        public JsonResult GetPendingQueue()
        {
            if (Session["ctx"] == null) { return Json("", JsonRequestBehavior.AllowGet); }
            Ctx ctx = Session["ctx"] as Ctx;

            // ── Step 1: Resolve primary accounting schema ─────────────────────
            int acctSchemaId = 0;
            string curSymbol = "";
            string isoCode = "";
            int stdPrecision = 2;

            string schemaSql = "SELECT C_AcctSchema.C_AcctSchema_ID,"
                             + " C_Currency.CurSymbol, C_Currency.ISO_Code, C_Currency.StdPrecision"
                             + " FROM C_AcctSchema"
                             + " INNER JOIN C_Currency ON (C_AcctSchema.C_Currency_ID=C_Currency.C_Currency_ID)"
                             + " WHERE C_AcctSchema.IsActive='Y'"
                             + " AND C_AcctSchema.AD_Client_ID=@ClientID";

            SqlParameter[] schemaParams = { new SqlParameter("@ClientID", ctx.GetAD_Client_ID()) };
            DataSet schemaDs = CoreLibrary.DataBase.DB.ExecuteDataset(schemaSql, schemaParams, null);
            if (schemaDs != null && schemaDs.Tables[0].Rows.Count > 0)
            {
                acctSchemaId = Util.GetValueOfInt(schemaDs.Tables[0].Rows[0]["C_AcctSchema_ID"]);
                curSymbol = Util.GetValueOfString(schemaDs.Tables[0].Rows[0]["CurSymbol"]);
                isoCode = Util.GetValueOfString(schemaDs.Tables[0].Rows[0]["ISO_Code"]);
                stdPrecision = Util.GetValueOfInt(schemaDs.Tables[0].Rows[0]["StdPrecision"]);
            }

            // ── Step 3: Count all pending journals for header badge ───────────
            // Uses same WHERE conditions + MRole so the count respects row-level security.
            string countBase = "SELECT COUNT(1) FROM GL_Journal"
                             + " WHERE GL_Journal.DocStatus IN ('DR','IP','AP','NA')"
                             + " AND GL_Journal.IsActive='Y'"
                             + " AND GL_Journal.C_AcctSchema_ID=@AcctSchemaID";

            countBase = MRole.GetDefault(ctx).AddAccessSQL(
                countBase, "GL_Journal", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            SqlParameter[] countParams = { new SqlParameter("@AcctSchemaID", acctSchemaId) };
            int totalCount = 0;
            DataSet countDs = CoreLibrary.DataBase.DB.ExecuteDataset(countBase, countParams, null);
            if (countDs != null && countDs.Tables[0].Rows.Count > 0)
            {
                totalCount = Util.GetValueOfInt(countDs.Tables[0].Rows[0][0]);
            }

            // ── Step 4: Query pending journals — no GROUP BY needed ───────────
            // Correlated subquery fetches TotalDebit per journal without GROUP BY,
            // allowing MRole to be applied cleanly before ORDER BY.
            // FETCH FIRST 25 ROWS ONLY caps at the database level — works on Oracle 12c+ and PostgreSQL 8.4+.
            string sqlBase = "SELECT GL_Journal.GL_Journal_ID,"
                           + " GL_Journal.DocumentNo,"
                           + " GL_Journal.Description,"
                           + " GL_Journal.DocStatus,"
                           + " AD_Ref_List.Name AS DocStatusName,"
                           + " GL_Journal.Created,"
                           + " (SELECT COALESCE(SUM(jl.AmtAcctDr),0) FROM GL_JournalLine jl"
                           + " WHERE jl.GL_Journal_ID=GL_Journal.GL_Journal_ID AND jl.IsActive='Y') AS TotalDebit,"
                           + " AD_User.Name AS UserName"
                           + " FROM GL_Journal"
                           + " INNER JOIN AD_User ON (GL_Journal.CreatedBy=AD_User.AD_User_ID)"
                           + " LEFT OUTER JOIN AD_Ref_List ON (AD_Ref_List.AD_Reference_ID=131"
                           + " AND AD_Ref_List.Value=GL_Journal.DocStatus"
                           + " AND AD_Ref_List.IsActive='Y')"
                           + " WHERE GL_Journal.DocStatus IN ('DR','IP','AP','NA')"
                           + " AND GL_Journal.IsActive='Y'"
                           + " AND GL_Journal.C_AcctSchema_ID=@AcctSchemaID";

            // Apply MRole before ORDER BY
            sqlBase = MRole.GetDefault(ctx).AddAccessSQL(
                sqlBase, "GL_Journal", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string sql = sqlBase + " ORDER BY GL_Journal.Created ASC FETCH FIRST 25 ROWS ONLY";

            SqlParameter[] mainParams = { new SqlParameter("@AcctSchemaID", acctSchemaId) };
            DataSet ds = CoreLibrary.DataBase.DB.ExecuteDataset(sql, mainParams, null);

            // ── Step 5: Build result — SQL already capped at 25 rows ─────────
            var queue = new List<object>();
            DateTime now = DateTime.Now;

            if (ds != null && ds.Tables[0].Rows.Count > 0)
            {
                for (int i = 0; i < ds.Tables[0].Rows.Count; i++)
                {
                    DataRow row = ds.Tables[0].Rows[i];

                    int journalId = Util.GetValueOfInt(row["GL_Journal_ID"]);
                    string docNo = Util.GetValueOfString(row["DocumentNo"]);
                    string description = Util.GetValueOfString(row["Description"]);
                    string docStatus = Util.GetValueOfString(row["DocStatus"]);
                    string statusName = Util.GetValueOfString(row["DocStatusName"]);
                    string userName = Util.GetValueOfString(row["UserName"]);
                    decimal totalDebit = Decimal.Round(
                        Util.GetValueOfDecimal(row["TotalDebit"]), stdPrecision, MidpointRounding.AwayFromZero);

                    // Age — calculated from Created timestamp
                    DateTime created = row["Created"] != DBNull.Value
                        ? Convert.ToDateTime(row["Created"])
                        : now;
                    double totalHours = (now - created).TotalHours;

                    string ageStr;
                    if (totalHours < 1)
                        ageStr = "< 1h";
                    else if (totalHours < 24)
                        ageStr = ((int)totalHours) + "h";
                    else if (totalHours < 48)
                        ageStr = "1d";
                    else
                        ageStr = ((int)(totalHours / 24)) + "d";

                    // Urgency marker
                    string markerType;
                    if (totalHours >= 48)
                        markerType = "danger";
                    else if (totalHours >= 24)
                        markerType = "warn";
                    else
                        markerType = "info";

                    // Action label based on DocStatus
                    string actionLabel;
                    switch (docStatus)
                    {
                        case "IP":
                            actionLabel = "Approval";
                            break;
                        case "AP":
                            actionLabel = "Post";
                            break;
                        case "NA":
                            actionLabel = "Resubmit";
                            break;
                        default:
                            actionLabel = "Draft";
                            break;
                    }

                    queue.Add(new
                    {
                        GL_Journal_ID = journalId,
                        DocumentNo = docNo,
                        Description = description,
                        DocStatus = docStatus,
                        StatusName = statusName,
                        ActionLabel = actionLabel,
                        MarkerType = markerType,
                        AgeStr = ageStr,
                        IsOverdue = totalHours >= 48,
                        TotalDebit = totalDebit,
                        UserName = userName
                    });
                }
            }

            return Json(JsonConvert.SerializeObject(new
            {
                Queue = queue,
                TotalCount = totalCount,
                CurSymbol = curSymbol,
                ISOCode = isoCode,
                StdPrecision = stdPrecision
            }), JsonRequestBehavior.AllowGet);
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

        /// <summary>
        /// Completes and posts the selected approved GL Journal.
        /// If immediate accounting is enabled, completion may post it automatically.
        /// </summary>
        [HttpPost]
        public JsonResult PostJournal(int journalId)
        {
            Ctx ctx = Session["ctx"] as Ctx;

            if (ctx == null)
            {
                return Json(new
                {
                    success = false,
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
                    "VAS045PostJournal"
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
                    trx.Commit();

                    return Json(new
                    {
                        success = true,
                        journalId = journal.Get_ID(),
                        documentNo = journal.GetDocumentNo(),
                        docStatus = journal.GetDocStatus(),
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

                /*
                 * Posting directly from Approved is not a valid document action.
                 * Complete the journal first, then post it.
                 */
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

                    /*
                     * Reload the journal because immediate accounting may
                     * have posted it during completion.
                     */
                    journal = new MJournal(
                        ctx,
                        journalId,
                        trx
                    );

                    docStatus =
                        journal.GetDocStatus();
                }

                if (
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
                 * The Complete action may already post the journal when
                 * immediate accounting is enabled.
                 */
                if (!IsJournalPosted(journal))
                {
                    string postingError =
                        PostJournalAccounting(
                            ctx,
                            journalId,
                            journalTableId,
                            trx
                        );

                    if (!string.IsNullOrWhiteSpace(
                        postingError
                    ))
                    {
                        throw new InvalidOperationException(
                            postingError
                        );
                    }

                    journal = new MJournal(
                        ctx,
                        journalId,
                        trx
                    );
                }

                if (!IsJournalPosted(journal))
                {
                    throw new InvalidOperationException(
                        GetMsg(
                            ctx,
                            "VAS_045_JournalPostingNotCompleted",
                            "The journal process finished, but the journal was not posted."
                        )
                    );
                }

                trx.Commit();

                return Json(new
                {
                    success = true,
                    journalId = journal.Get_ID(),
                    documentNo = journal.GetDocumentNo(),
                    docStatus = journal.GetDocStatus(),
                    posted = true,
                    message = GetMsg(
                        ctx,
                        "VAS_045_JournalPostedSuccessfully",
                        "Journal posted successfully."
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

        private string PostJournalAccounting(
            Ctx ctx,
            int journalId,
            int journalTableId,
            Trx trx)
        {
            MAcctSchema[] acctSchemas =
                MAcctSchema.GetClientAcctSchema(
                    ctx,
                    ctx.GetAD_Client_ID()
                );

            if (
                acctSchemas == null ||
                acctSchemas.Length == 0
            )
            {
                return GetMsg(
                    ctx,
                    "VAS_045_NoAccountingSchema",
                    "No accounting schema was found for this client."
                );
            }

            string postingResult =
                Doc.PostImmediate(
                    acctSchemas,
                    journalTableId,
                    journalId,
                    false,
                    trx
                );

            if (string.IsNullOrWhiteSpace(
                postingResult
            ))
            {
                return string.Empty;
            }

            if (string.Equals(
                postingResult,
                Doc.STATUS_Posted,
                StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            if (string.Equals(
                postingResult,
                "AlreadyPosted",
                StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            return GetPostingResultMessage(
                ctx,
                postingResult
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
