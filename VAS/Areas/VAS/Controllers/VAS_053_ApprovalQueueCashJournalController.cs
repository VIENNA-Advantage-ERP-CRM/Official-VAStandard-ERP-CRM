
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Web.Mvc;
using VAdvantage.DataBase;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VAS.Controllers
{
    /// <summary>
    /// Module Name : Cash Journal
    /// Purpose     : Provides in-progress cash journal approval queue entries.
    /// </summary>
    public class VAS_053_ApprovalQueueCashJournalController : Controller
    {
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetApprovalQueue(
            int pageNo = 1,
            int pageSize = 2
        )
        {
            Ctx ctx =
                Session["ctx"] as Ctx;

            if (ctx == null)
            {
                return Json(
                    new
                    {
                        success = false,
                        error = "Session Expired",
                        hasData = false
                    },
                    JsonRequestBehavior.AllowGet
                );
            }

            IDataReader reader =
                null;

            try
            {
                /*
                 * Latest workflow process for every C_Cash record.
                 *
                 * This query uses only ANSI SQL supported by
                 * Oracle and PostgreSQL.
                 */
                string workflowSql = @"
SELECT
    WorkflowProcess.Record_ID,
    WorkflowProcess.TextMsg AS WorkflowMessage,
    WorkflowProcess.Priority,
    WorkflowProcess.Created AS WorkflowCreated,
    WorkflowProcess.AD_WF_Process_ID

FROM AD_WF_Process WorkflowProcess

INNER JOIN AD_Table TableInfo ON
(
    TableInfo.AD_Table_ID =
    WorkflowProcess.AD_Table_ID
)

WHERE WorkflowProcess.IsActive = 'Y'

AND TableInfo.IsActive = 'Y'

AND TableInfo.TableName = 'C_Cash'

AND NOT EXISTS
(
    SELECT
        1

    FROM AD_WF_Process WorkflowProcess2

    WHERE WorkflowProcess2.IsActive = 'Y'

    AND WorkflowProcess2.AD_Table_ID =
        WorkflowProcess.AD_Table_ID

    AND WorkflowProcess2.Record_ID =
        WorkflowProcess.Record_ID

    AND
    (
        WorkflowProcess2.Created >
        WorkflowProcess.Created

        OR
        (
            WorkflowProcess2.Created =
            WorkflowProcess.Created

            AND WorkflowProcess2.AD_WF_Process_ID >
            WorkflowProcess.AD_WF_Process_ID
        )
    )
)";

                /*
                 * Apply MRole only to the main physical table query.
                 *
                 * Do not apply MRole to:
                 * - the final WITH query
                 * - the ProtectedCash CTE alias
                 * - LatestWorkflow
                 * - joined aliases
                 */
                string protectedCashSql = @"
SELECT
    CashHeader.C_Cash_ID,
    CashHeader.AD_Client_ID,
    CashHeader.AD_Org_ID,
    CashHeader.C_CashBook_ID,
    CashHeader.DocumentNo,
    CashHeader.Created,
    CashHeader.CreatedBy,
    CashHeader.StatementDate,
    CashHeader.EndingBalance,
    CashHeader.DocStatus

FROM C_Cash CashHeader

WHERE CashHeader.IsActive = 'Y'

AND CashHeader.DocStatus = 'IP'";

                protectedCashSql =
                    MRole.GetDefault(ctx)
                        .AddAccessSQL(
                            protectedCashSql,
                            "CashHeader",
                            MRole.SQL_FULLYQUALIFIED,
                            MRole.SQL_RO
                        );

                /*
                 * Join only after C_Cash has already been
                 * protected by MRole.
                 */
                string approvalQueueSql = @"
ApprovalQueue AS
(
    SELECT
        ProtectedCash.C_Cash_ID,
        ProtectedCash.DocumentNo,
        ProtectedCash.Created,
        ProtectedCash.CreatedBy,
        ProtectedCash.StatementDate,
        ProtectedCash.EndingBalance,
        ProtectedCash.DocStatus,

        CashBook.Name AS CashBookName,

        CreatedByUser.Name AS CreatedByName,

        Currency.ISO_Code AS CurrencyISO,

        CASE
            WHEN Currency.CurSymbol IS NOT NULL
            THEN Currency.CurSymbol
            ELSE Currency.ISO_Code
        END AS CurrencySymbol,

        Currency.StdPrecision,

        LatestWorkflow.WorkflowMessage,
        LatestWorkflow.Priority

    FROM ProtectedCash ProtectedCash

    INNER JOIN C_CashBook CashBook ON
    (
        ProtectedCash.C_CashBook_ID =
        CashBook.C_CashBook_ID
    )

    INNER JOIN C_Currency Currency ON
    (
        CashBook.C_Currency_ID =
        Currency.C_Currency_ID
    )

    LEFT OUTER JOIN AD_User CreatedByUser ON
    (
        ProtectedCash.CreatedBy =
        CreatedByUser.AD_User_ID
    )

    LEFT OUTER JOIN LatestWorkflow LatestWorkflow ON
    (
        LatestWorkflow.Record_ID =
        ProtectedCash.C_Cash_ID
    )

    WHERE CashBook.IsActive = 'Y'

    AND Currency.IsActive = 'Y'
)";

                string sql = @"
WITH LatestWorkflow AS
(
" + workflowSql + @"
),
ProtectedCash AS
(
" + protectedCashSql + @"
),
" + approvalQueueSql + @"
SELECT
    ApprovalQueue.C_Cash_ID,
    ApprovalQueue.DocumentNo,
    ApprovalQueue.Created,
    ApprovalQueue.CreatedBy,
    ApprovalQueue.StatementDate,
    ApprovalQueue.EndingBalance,
    ApprovalQueue.DocStatus,
    ApprovalQueue.CashBookName,
    ApprovalQueue.CreatedByName,
    ApprovalQueue.CurrencyISO,
    ApprovalQueue.CurrencySymbol,
    ApprovalQueue.StdPrecision,
    ApprovalQueue.WorkflowMessage,
    ApprovalQueue.Priority

FROM ApprovalQueue ApprovalQueue

ORDER BY
    ApprovalQueue.Created DESC,
    ApprovalQueue.C_Cash_ID DESC";

                List<object> items =
                    new List<object>();

                int pendingCount =
                    0;

                reader =
                    DB.ExecuteReader(
                        sql,
                        null,
                        null
                    );

                while (
                    reader != null &&
                    reader.Read()
                )
                {
                    pendingCount++;

                    int priority =
                        GetInt(
                            reader,
                            "Priority"
                        );

                    string workflowMessage =
                        GetString(
                            reader,
                            "WorkflowMessage"
                        );

                    workflowMessage =
                        ShortText(
                            workflowMessage,
                            90
                        );

                    items.Add(
                        new
                        {
                            cCashId =
                                GetInt(
                                    reader,
                                    "C_Cash_ID"
                                ),

                            documentNo =
                                GetString(
                                    reader,
                                    "DocumentNo"
                                ),

                            title =
                                GetTitle(
                                    ctx,
                                    reader
                                ),

                            cashBookName =
                                GetString(
                                    reader,
                                    "CashBookName"
                                ),

                            createdByName =
                                GetString(
                                    reader,
                                    "CreatedByName"
                                ),

                            created =
                                FormatDbDateTime(
                                    reader["Created"]
                                ),

                            relativeTime =
                                GetRelativeTime(
                                    ctx,
                                    reader["Created"]
                                ),

                            amount =
                                GetDecimal(
                                    reader,
                                    "EndingBalance"
                                ),

                            currencyISO =
                                GetString(
                                    reader,
                                    "CurrencyISO"
                                ),

                            currencySymbol =
                                GetString(
                                    reader,
                                    "CurrencySymbol"
                                ),

                            stdPrecision =
                                GetInt(
                                    reader,
                                    "StdPrecision",
                                    2
                                ),

                            workflowMessage =
                                workflowMessage,

                            priority =
                                priority,

                            priorityText =
                                GetPriorityText(
                                    ctx,
                                    priority
                                ),

                            priorityClass =
                                GetPriorityClass(
                                    priority
                                )
                        }
                    );
                }

                pageSize =
                    Math.Max(
                        1,
                        Math.Min(
                            pageSize,
                            50
                        )
                    );

                int totalPages =
                    pendingCount > 0
                        ? (int)Math.Ceiling(
                            (decimal)pendingCount /
                            pageSize
                        )
                        : 0;

                pageNo =
                    Math.Max(
                        1,
                        pageNo
                    );

                if (
                    totalPages > 0 &&
                    pageNo > totalPages
                )
                {
                    pageNo =
                        totalPages;
                }

                List<object> pagedItems =
                    items
                        .Skip(
                            (pageNo - 1) *
                            pageSize
                        )
                        .Take(
                            pageSize
                        )
                        .ToList();

                return Json(
                    new
                    {
                        success = true,
                        error = string.Empty,

                        title = GetMsg(
                            ctx,
                            "VAS_053_ApprovalQueue",
                            "Approval Queue"
                        ),

                        pendingText = GetMsg(
                            ctx,
                            "VAS_053_Pending",
                            "Pending"
                        ),

                        viewAllText = GetMsg(
                            ctx,
                            "VAS_053_ViewAll",
                            "View all ->"
                        ),

                        submittedByText = GetMsg(
                            ctx,
                            "VAS_053_SubmittedBy",
                            "Submitted by"
                        ),

                        noDataText = GetMsg(
                            ctx,
                            "VAS_053_NoData",
                            "No in-progress cash journals"
                        ),

                        pendingCount =
                            pendingCount,

                        pageNo =
                            pageNo,

                        pageSize =
                            pageSize,

                        totalPages =
                            totalPages,

                        items =
                            pagedItems,

                        hasData =
                            pendingCount > 0
                    },
                    JsonRequestBehavior.AllowGet
                );
            }
            catch (Exception)
            {
                return Json(
                    new
                    {
                        success = false,

                        error = GetMsg(
                            ctx,
                            "VAS_053_LoadError",
                            "Unable to load approval queue"
                        ),

                        hasData = false
                    },
                    JsonRequestBehavior.AllowGet
                );
            }
            finally
            {
                if (reader != null)
                {
                    reader.Close();
                    reader.Dispose();
                }
            }
        }

        private string GetTitle(
            Ctx ctx,
            IDataReader reader
        )
        {
            string documentNo =
                GetString(
                    reader,
                    "DocumentNo"
                );

            string cashBookName =
                GetString(
                    reader,
                    "CashBookName"
                );

            if (
                !string.IsNullOrWhiteSpace(
                    documentNo
                ) &&
                !string.IsNullOrWhiteSpace(
                    cashBookName
                )
            )
            {
                return
                    documentNo +
                    " - " +
                    cashBookName;
            }

            if (
                !string.IsNullOrWhiteSpace(
                    documentNo
                )
            )
            {
                return documentNo;
            }

            if (
                !string.IsNullOrWhiteSpace(
                    cashBookName
                )
            )
            {
                return cashBookName;
            }

            return GetMsg(
                ctx,
                "VAS_053_CashJournal",
                "Cash Journal"
            );
        }

        private string GetPriorityText(
            Ctx ctx,
            int priority
        )
        {
            if (priority >= 8)
            {
                return GetMsg(
                    ctx,
                    "VAS_053_High",
                    "High"
                );
            }

            if (priority >= 4)
            {
                return GetMsg(
                    ctx,
                    "VAS_053_Medium",
                    "Med"
                );
            }

            return GetMsg(
                ctx,
                "VAS_053_Low",
                "Low"
            );
        }

        private string GetPriorityClass(
            int priority
        )
        {
            if (priority >= 8)
            {
                return "high";
            }

            if (priority >= 4)
            {
                return "med";
            }

            return "low";
        }

        private string GetRelativeTime(
            Ctx ctx,
            object value
        )
        {
            if (
                value == null ||
                value == DBNull.Value
            )
            {
                return string.Empty;
            }

            DateTime created;

            if (
                !DateTime.TryParse(
                    value.ToString(),
                    out created
                )
            )
            {
                return string.Empty;
            }

            TimeSpan elapsed =
                DateTime.Now -
                created;

            if (elapsed.TotalMinutes < 1)
            {
                return GetMsg(
                    ctx,
                    "VAS_053_JustNow",
                    "just now"
                );
            }

            if (elapsed.TotalHours < 1)
            {
                return
                    Math.Max(
                        1,
                        Convert.ToInt32(
                            Math.Floor(
                                elapsed.TotalMinutes
                            )
                        )
                    ) +
                    "m " +
                    GetMsg(
                        ctx,
                        "VAS_053_Ago",
                        "ago"
                    );
            }

            if (elapsed.TotalDays < 1)
            {
                return
                    Math.Max(
                        1,
                        Convert.ToInt32(
                            Math.Floor(
                                elapsed.TotalHours
                            )
                        )
                    ) +
                    "h " +
                    GetMsg(
                        ctx,
                        "VAS_053_Ago",
                        "ago"
                    );
            }

            if (elapsed.TotalDays < 2)
            {
                return GetMsg(
                    ctx,
                    "VAS_053_Yesterday",
                    "yesterday"
                );
            }

            return
                Math.Max(
                    1,
                    Convert.ToInt32(
                        Math.Floor(
                            elapsed.TotalDays
                        )
                    )
                ) +
                "d " +
                GetMsg(
                    ctx,
                    "VAS_053_Ago",
                    "ago"
                );
        }

        private string GetMsg(
            Ctx ctx,
            string key,
            string fallback
        )
        {
            string msg =
                Msg.GetMsg(
                    ctx,
                    key
                );

            if (
                string.IsNullOrEmpty(
                    msg
                ) ||
                msg == key ||
                msg == "[" + key + "]"
            )
            {
                return fallback;
            }

            return msg;
        }

        private string FormatDbDateTime(
            object value
        )
        {
            if (
                value == null ||
                value == DBNull.Value
            )
            {
                return string.Empty;
            }

            DateTime dateValue;

            if (
                DateTime.TryParse(
                    value.ToString(),
                    out dateValue
                )
            )
            {
                return dateValue.ToString(
                    "yyyy-MM-dd HH:mm"
                );
            }

            return string.Empty;
        }

        private string ShortText(
            string text,
            int maxLength
        )
        {
            if (
                string.IsNullOrWhiteSpace(
                    text
                )
            )
            {
                return string.Empty;
            }

            text =
                text
                    .Replace(
                        "\r",
                        " "
                    )
                    .Replace(
                        "\n",
                        " "
                    )
                    .Trim();

            while (
                text.Contains(
                    "  "
                )
            )
            {
                text =
                    text.Replace(
                        "  ",
                        " "
                    );
            }

            if (
                text.Length <=
                maxLength
            )
            {
                return text;
            }

            return
                text.Substring(
                    0,
                    maxLength
                ) +
                "...";
        }

        private decimal GetDecimal(
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
                return 0;
            }

            decimal result;

            return decimal.TryParse(
                value.ToString(),
                out result
            )
                ? result
                : 0;
        }

        private int GetInt(
            IDataReader reader,
            string columnName,
            int fallback = 0
        )
        {
            object value =
                reader[columnName];

            if (
                value == null ||
                value == DBNull.Value
            )
            {
                return fallback;
            }

            int result;

            return int.TryParse(
                value.ToString(),
                out result
            )
                ? result
                : fallback;
        }

        private string GetString(
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
                return string.Empty;
            }

            return value.ToString();
        }
    }
}

