
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
    public class VAS_041_GLJournalEntriesWidgetController : Controller
    {
        /// <summary>
        /// Returns the number of Actual GL Journals
        /// created within the current accounting month.
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetMonthlyEntryCount()
        {
            Ctx ctx = GetContext();

            if (ctx == null)
            {
                return GetSessionExpiredResult();
            }

            string sql = @"
WITH SchemaCurrency AS
(
" + BuildSchemaCurrencySql() + @"
),
PeriodRange AS
(
" + BuildPeriodRangeSql() + @"
),
ProtectedJournal AS
(
" + BuildProtectedJournalSql(
                    ctx,
                    "GL_Journal.PostingType = 'A'"
                ) + @"
)
SELECT
    COUNT(
        ProtectedJournal.GL_Journal_ID
    ) AS EntryCount,
    MAX(
        PeriodRange.MonthDateFrom
    ) AS MonthDateFrom
FROM PeriodRange PeriodRange
INNER JOIN SchemaCurrency SchemaCurrency ON
(
    SchemaCurrency.AD_Client_ID =
    PeriodRange.AD_Client_ID
)
LEFT OUTER JOIN ProtectedJournal ProtectedJournal ON
(
    ProtectedJournal.AD_Client_ID =
    SchemaCurrency.AD_Client_ID
    AND ProtectedJournal.C_AcctSchema_ID =
    SchemaCurrency.C_AcctSchema_ID
    AND ProtectedJournal.DateAcct >=
    PeriodRange.MonthDateFrom
    AND ProtectedJournal.DateAcct <
" + GetDateToExclusiveExpression(
                    "PeriodRange.MonthDateTo"
                ) + @"
)";

            SqlParameter[] parameters =
            {
                new SqlParameter(
                    "@AD_Client_ID",
                    ctx.GetAD_Client_ID()
                )
            };

            int entryCount = 0;
            DateTime? monthDateFrom = null;

            DataSet dataSet =
                DB.ExecuteDataset(
                    sql,
                    parameters,
                    null
                );

            if (
                dataSet != null &&
                dataSet.Tables.Count > 0 &&
                dataSet.Tables[0].Rows.Count > 0
            )
            {
                DataRow row =
                    dataSet.Tables[0].Rows[0];

                entryCount =
                    Util.GetValueOfInt(
                        row["EntryCount"]
                    );

                monthDateFrom =
                    Util.GetValueOfDateTime(
                        row["MonthDateFrom"]
                    );
            }

            DateTime displayDate =
                monthDateFrom.HasValue
                    ? monthDateFrom.Value
                    : DateTime.Now;

            return Json(
                JsonConvert.SerializeObject(
                    new
                    {
                        EntryCount =
                            entryCount,

                        MonthName =
                            displayDate.ToString(
                                "MMMM"
                            ),

                        MonthAbbr =
                            displayDate.ToString(
                                "MMM"
                            ),

                        Year =
                            displayDate.Year
                    }
                ),
                JsonRequestBehavior.AllowGet
            );
        }

        /// <summary>
        /// Returns all Actual GL Journals created
        /// in the current accounting month.
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetMonthlyEntries()
        {
            Ctx ctx = GetContext();

            if (ctx == null)
            {
                return GetSessionExpiredResult();
            }

            string language =
                GetLanguage(ctx);

            string sql = @"
WITH SchemaCurrency AS
(
" + BuildSchemaCurrencySql() + @"
),
PeriodRange AS
(
" + BuildPeriodRangeSql() + @"
),
ProtectedJournal AS
(
" + BuildProtectedJournalSql(
                    ctx,
                    "GL_Journal.PostingType = 'A'"
                ) + @"
),
DocumentStatusReference AS
(
" + BuildDocumentStatusReferenceSql() + @"
),
JournalData AS
(
    SELECT
        ProtectedJournal.GL_Journal_ID,
        ProtectedJournal.DocumentNo,
        ProtectedJournal.DateAcct,
        ProtectedJournal.Description,
        ProtectedJournal.DocStatus,
        ProtectedJournal.Posted,
        ProtectedJournal.Processed,
        ProtectedJournal.AD_Client_ID,
        ProtectedJournal.C_AcctSchema_ID
    FROM PeriodRange PeriodRange
    INNER JOIN SchemaCurrency SchemaCurrency ON
    (
        SchemaCurrency.AD_Client_ID =
        PeriodRange.AD_Client_ID
    )
    INNER JOIN ProtectedJournal ProtectedJournal ON
    (
        ProtectedJournal.AD_Client_ID =
        SchemaCurrency.AD_Client_ID
        AND ProtectedJournal.C_AcctSchema_ID =
        SchemaCurrency.C_AcctSchema_ID
        AND ProtectedJournal.DateAcct >=
        PeriodRange.MonthDateFrom
        AND ProtectedJournal.DateAcct <
" + GetDateToExclusiveExpression(
                    "PeriodRange.MonthDateTo"
                ) + @"
    )
),
JournalTotals AS
(
    SELECT
        JournalData.GL_Journal_ID,
        JournalData.DocumentNo,
        JournalData.DateAcct,
        JournalData.Description,
        JournalData.DocStatus,
        JournalData.Posted,
        JournalData.Processed,
        DocumentStatusReference.BaseName AS DocStatusBaseName,
        DocumentStatusReference.TranslatedName AS DocStatusTranslatedName,
        ROUND(
            COALESCE(
                SUM(
                    COALESCE(
                        GL_JournalLine.AmtAcctDr,
                        0
                    )
                ),
                0
            ),
            COALESCE(
                MAX(
                    SchemaCurrency.StdPrecision
                ),
                2
            )
        ) AS TotalDebit,
        ROUND(
            COALESCE(
                SUM(
                    COALESCE(
                        GL_JournalLine.AmtAcctCr,
                        0
                    )
                ),
                0
            ),
            COALESCE(
                MAX(
                    SchemaCurrency.StdPrecision
                ),
                2
            )
        ) AS TotalCredit,
        MAX(
            SchemaCurrency.Cur_Symbol
        ) AS CurSymbol,
        MAX(
            SchemaCurrency.ISO_Code
        ) AS ISOCode,
        COALESCE(
            MAX(
                SchemaCurrency.StdPrecision
            ),
            2
        ) AS StdPrecision
    FROM JournalData JournalData
    INNER JOIN SchemaCurrency SchemaCurrency ON
    (
        SchemaCurrency.AD_Client_ID =
        JournalData.AD_Client_ID
        AND SchemaCurrency.C_AcctSchema_ID =
        JournalData.C_AcctSchema_ID
    )
    LEFT OUTER JOIN GL_JournalLine GL_JournalLine ON
    (
        JournalData.GL_Journal_ID =
        GL_JournalLine.GL_Journal_ID
        AND GL_JournalLine.IsActive = 'Y'
    )
    LEFT OUTER JOIN DocumentStatusReference DocumentStatusReference ON
    (
        DocumentStatusReference.Value =
        JournalData.DocStatus
    )
    GROUP BY
        JournalData.GL_Journal_ID,
        JournalData.DocumentNo,
        JournalData.DateAcct,
        JournalData.Description,
        JournalData.DocStatus,
        JournalData.Posted,
        JournalData.Processed,
        DocumentStatusReference.BaseName,
        DocumentStatusReference.TranslatedName
)
SELECT
    JournalTotals.GL_Journal_ID,
    JournalTotals.DocumentNo,
    JournalTotals.DateAcct,
    JournalTotals.Description,
    JournalTotals.DocStatus,
    JournalTotals.Posted,
    JournalTotals.Processed,
    JournalTotals.DocStatusBaseName,
    JournalTotals.DocStatusTranslatedName,
    JournalTotals.TotalDebit,
    JournalTotals.TotalCredit,
    JournalTotals.CurSymbol,
    JournalTotals.ISOCode,
    JournalTotals.StdPrecision
FROM JournalTotals JournalTotals
ORDER BY
    JournalTotals.DateAcct DESC,
    JournalTotals.GL_Journal_ID DESC
FETCH FIRST 100 ROWS ONLY";

            SqlParameter[] parameters =
            {
                new SqlParameter(
                    "@AD_Client_ID",
                    ctx.GetAD_Client_ID()
                ),

                new SqlParameter(
                    "@AD_Language",
                    language
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

            decimal totalDebit = 0m;
            decimal totalCredit = 0m;

            string curSymbol =
                string.Empty;

            string isoCode =
                string.Empty;

            int stdPrecision = 2;

            DateTime? monthDate = null;

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
                    stdPrecision =
                        NormalizePrecision(
                            Util.GetValueOfInt(
                                row["StdPrecision"]
                            )
                        );

                    decimal debit =
                        Util.GetValueOfDecimal(
                            row["TotalDebit"]
                        );

                    decimal credit =
                        Util.GetValueOfDecimal(
                            row["TotalCredit"]
                        );

                    totalDebit += debit;
                    totalCredit += credit;

                    string docStatus =
                        Util.GetValueOfString(
                            row["DocStatus"]
                        );

                    string statusName =
                        GetReferenceName(
                            row,
                            "DocStatusTranslatedName",
                            "DocStatusBaseName",
                            docStatus
                        );

                    string dateAcctText =
                        string.Empty;

                    DateTime? dateAcct =
                        Util.GetValueOfDateTime(
                            row["DateAcct"]
                        );

                    if (dateAcct.HasValue)
                    {
                        dateAcctText =
                            dateAcct.Value.ToString(
                                "dd MMM yyyy"
                            );

                        if (!monthDate.HasValue)
                        {
                            monthDate =
                                dateAcct.Value;
                        }
                    }

                    curSymbol =
                        Util.GetValueOfString(
                            row["CurSymbol"]
                        );

                    isoCode =
                        Util.GetValueOfString(
                            row["ISOCode"]
                        );

                    entries.Add(
                        new
                        {
                            GL_Journal_ID =
                                Util.GetValueOfInt(
                                    row["GL_Journal_ID"]
                                ),

                            DocumentNo =
                                Util.GetValueOfString(
                                    row["DocumentNo"]
                                ),

                            DateAcct =
                                dateAcctText,

                            Description =
                                Util.GetValueOfString(
                                    row["Description"]
                                ),

                            DocStatus =
                                docStatus,

                            StatusName =
                                statusName,

                            Posted =
                                Util.GetValueOfString(
                                    row["Posted"]
                                ),

                            Processed =
                                Util.GetValueOfString(
                                    row["Processed"]
                                ),

                            TotalDebit =
                                Decimal.Round(
                                    debit,
                                    stdPrecision,
                                    MidpointRounding
                                        .AwayFromZero
                                ),

                            TotalCredit =
                                Decimal.Round(
                                    credit,
                                    stdPrecision,
                                    MidpointRounding
                                        .AwayFromZero
                                )
                        }
                    );
                }
            }

            DateTime displayDate =
                monthDate.HasValue
                    ? monthDate.Value
                    : DateTime.Now;

            return Json(
                JsonConvert.SerializeObject(
                    new
                    {
                        success = true,

                        Entries =
                            entries,

                        TotalCount =
                            entries.Count,

                        TotalDebit =
                            Decimal.Round(
                                totalDebit,
                                stdPrecision,
                                MidpointRounding
                                    .AwayFromZero
                            ),

                        TotalCredit =
                            Decimal.Round(
                                totalCredit,
                                stdPrecision,
                                MidpointRounding
                                    .AwayFromZero
                            ),

                        MonthName =
                            displayDate.ToString(
                                "MMMM"
                            ),

                        MonthAbbr =
                            displayDate.ToString(
                                "MMM"
                            ),

                        Year =
                            displayDate.Year,

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

        /// <summary>
        /// Returns the selected Journal header and lines.
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

            string language =
                GetLanguage(ctx);

            string headerSql = @"
WITH ProtectedJournal AS
(
" + BuildProtectedJournalSql(
                    ctx,
                    "GL_Journal.GL_Journal_ID = @JournalID"
                ) + @"
),
SchemaCurrency AS
(
    SELECT
        AcctSchema.AD_Client_ID,
        AcctSchema.C_AcctSchema_ID,
        AcctSchema.Name AS AcctSchemaName,
        Currency.StdPrecision,
        Currency.ISO_Code AS ISO_Code,
        CASE
            WHEN Currency.CurSymbol IS NOT NULL
            THEN Currency.CurSymbol
            ELSE Currency.ISO_Code
        END AS Cur_Symbol
    FROM C_AcctSchema AcctSchema
    INNER JOIN C_Currency Currency ON
    (
        AcctSchema.C_Currency_ID =
        Currency.C_Currency_ID
    )
    WHERE AcctSchema.IsActive = 'Y'
    AND AcctSchema.AD_Client_ID =
    @AD_Client_ID
),
DocumentStatusReference AS
(
" + BuildDocumentStatusReferenceSql() + @"
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
    DocumentStatusReference.BaseName AS DocStatusBaseName,
    DocumentStatusReference.TranslatedName AS DocStatusTranslatedName,
    AD_User.Name AS CreatedByName,
    ROUND(
        COALESCE(
            SUM(
                COALESCE(
                    GL_JournalLine.AmtAcctDr,
                    0
                )
            ),
            0
        ),
        COALESCE(
            MAX(
                SchemaCurrency.StdPrecision
            ),
            2
        )
    ) AS TotalDebit,
    ROUND(
        COALESCE(
            SUM(
                COALESCE(
                    GL_JournalLine.AmtAcctCr,
                    0
                )
            ),
            0
        ),
        COALESCE(
            MAX(
                SchemaCurrency.StdPrecision
            ),
            2
        )
    ) AS TotalCredit,
    MAX(
        SchemaCurrency.Cur_Symbol
    ) AS CurSymbol,
    MAX(
        SchemaCurrency.ISO_Code
    ) AS ISOCode,
    COALESCE(
        MAX(
            SchemaCurrency.StdPrecision
        ),
        2
    ) AS StdPrecision,
    MAX(
        SchemaCurrency.AcctSchemaName
    ) AS AccountingBook
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
LEFT OUTER JOIN DocumentStatusReference DocumentStatusReference ON
(
    DocumentStatusReference.Value =
    ProtectedJournal.DocStatus
)
LEFT OUTER JOIN AD_User AD_User ON
(
    ProtectedJournal.CreatedBy =
    AD_User.AD_User_ID
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
    DocumentStatusReference.TranslatedName,
    AD_User.Name";

            SqlParameter[] headerParameters =
            {
                new SqlParameter(
                    "@AD_Client_ID",
                    ctx.GetAD_Client_ID()
                ),

                new SqlParameter(
                    "@JournalID",
                    journalId
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

            int stdPrecision =
                NormalizePrecision(
                    Util.GetValueOfInt(
                        headerRow["StdPrecision"]
                    )
                );

            string linesSql = @"
WITH ProtectedJournal AS
(
" + BuildProtectedJournalSql(
                    ctx,
                    "GL_Journal.GL_Journal_ID = @JournalID"
                ) + @"
)
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
    BPartner.Name AS BPartnerName,
    Product.Name AS ProductName,
    Project.Name AS ProjectName
FROM ProtectedJournal ProtectedJournal
INNER JOIN GL_JournalLine GL_JournalLine ON
(
    ProtectedJournal.GL_Journal_ID =
    GL_JournalLine.GL_Journal_ID
)
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
LEFT OUTER JOIN C_BPartner BPartner ON
(
    ValidCombination.C_BPartner_ID =
    BPartner.C_BPartner_ID
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
WHERE GL_JournalLine.IsActive = 'Y'
ORDER BY
    GL_JournalLine.Line,
    GL_JournalLine.GL_JournalLine_ID";

            SqlParameter[] lineParameters =
            {
                new SqlParameter(
                    "@AD_Client_ID",
                    ctx.GetAD_Client_ID()
                ),

                new SqlParameter(
                    "@JournalID",
                    journalId
                )
            };

            DataSet lineDataSet =
                DB.ExecuteDataset(
                    linesSql,
                    lineParameters,
                    null
                );

            List<object> lines =
                new List<object>();

            if (
                lineDataSet != null &&
                lineDataSet.Tables.Count > 0 &&
                lineDataSet.Tables[0]
                    .Rows.Count > 0
            )
            {
                foreach (
                    DataRow row in
                    lineDataSet.Tables[0].Rows
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

                    string costCenter =
                        BuildCodeName(
                            Util.GetValueOfString(
                                row["CostCenterCode"]
                            ),
                            Util.GetValueOfString(
                                row["CostCenterName"]
                            )
                        );

                    string bPartner =
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
                                    stdPrecision,
                                    MidpointRounding
                                        .AwayFromZero
                                ),

                            Credit =
                                Decimal.Round(
                                    Util.GetValueOfDecimal(
                                        row["AmtAcctCr"]
                                    ),
                                    stdPrecision,
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
                                    bPartner
                                )
                                    ? "-"
                                    : bPartner,

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

            string docStatus =
                Util.GetValueOfString(
                    headerRow["DocStatus"]
                );

            string statusName =
                GetReferenceName(
                    headerRow,
                    "DocStatusTranslatedName",
                    "DocStatusBaseName",
                    docStatus
                );

            DateTime? dateAcct =
                Util.GetValueOfDateTime(
                    headerRow["DateAcct"]
                );

            DateTime? created =
                Util.GetValueOfDateTime(
                    headerRow["Created"]
                );

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
                                dateAcct.HasValue
                                    ? dateAcct.Value
                                        .ToString(
                                            "dd MMM yyyy"
                                        )
                                    : string.Empty,

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
                                    headerRow["Posted"]
                                ),

                            Processed =
                                Util.GetValueOfString(
                                    headerRow["Processed"]
                                ),

                            TotalDebit =
                                Decimal.Round(
                                    Util.GetValueOfDecimal(
                                        headerRow[
                                            "TotalDebit"
                                        ]
                                    ),
                                    stdPrecision,
                                    MidpointRounding
                                        .AwayFromZero
                                ),

                            TotalCredit =
                                Decimal.Round(
                                    Util.GetValueOfDecimal(
                                        headerRow[
                                            "TotalCredit"
                                        ]
                                    ),
                                    stdPrecision,
                                    MidpointRounding
                                        .AwayFromZero
                                ),

                            AccountingBook =
                                string.IsNullOrWhiteSpace(
                                    Util.GetValueOfString(
                                        headerRow[
                                            "AccountingBook"
                                        ]
                                    )
                                )
                                    ? "Primary"
                                    : Util.GetValueOfString(
                                        headerRow[
                                            "AccountingBook"
                                        ]
                                    ),

                            CreatedByName =
                                Util.GetValueOfString(
                                    headerRow[
                                        "CreatedByName"
                                    ]
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
                            Util.GetValueOfString(
                                headerRow["CurSymbol"]
                            ),

                        ISOCode =
                            Util.GetValueOfString(
                                headerRow["ISOCode"]
                            ),

                        StdPrecision =
                            stdPrecision
                    }
                ),
                JsonRequestBehavior.AllowGet
            );
        }

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
                        "VAS041ApproveJournal"
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
                        "The journal is already posted."
                    );
                }

                string docStatus =
                    journal.GetDocStatus();

                if (string.Equals(
                    docStatus,
                    "AP",
                    StringComparison.OrdinalIgnoreCase))
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
                                "The journal is already approved."
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
                        DocActionVariables
                            .ACTION_PREPARE
                    );

                    if (!journal.ProcessIt(
                        DocActionVariables
                            .ACTION_PREPARE))
                    {
                        throw new InvalidOperationException(
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
                    StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
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
                    throw new InvalidOperationException(
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

            VAdvantage.DataBase.Trx completeTransaction =
                null;

            try
            {
                string transactionName =
                    VAdvantage.DataBase.Trx
                        .CreateTrxName(
                            "VAS041CompleteJournal"
                        );

                completeTransaction =
                    VAdvantage.DataBase.Trx.GetTrx(
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
                                "The journal is already posted."
                        }
                    );
                }

                string docStatus =
                    journal.GetDocStatus();

                if (string.Equals(
                    docStatus,
                    "AP",
                    StringComparison.OrdinalIgnoreCase))
                {
                    journal.SetDocAction(
                        DocActionVariables
                            .ACTION_COMPLETE
                    );

                    if (!journal.ProcessIt(
                        DocActionVariables
                            .ACTION_COMPLETE))
                    {
                        throw new InvalidOperationException(
                            GetJournalProcessError(
                                journal,
                                "The journal could not be completed."
                            )
                        );
                    }

                    SaveJournal(journal);

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
                if (completeTransaction != null)
                {
                    completeTransaction.Close();
                    completeTransaction = null;
                }
            }

            JournalPostingState journalState =
                GetJournalPostingState(
                    journalId
                );

            if (journalState == null)
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

            if (string.Equals(
                journalState.Posted,
                "Y",
                StringComparison.OrdinalIgnoreCase))
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
                            "The journal is already posted."
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
                    "The journal was not completed successfully. " +
                    "Current status: " +
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

            if (!string.Equals(
                journalState.Processed,
                "Y",
                StringComparison.OrdinalIgnoreCase))
            {
                return Json(
                    new
                    {
                        success = false,
                        error =
                            "The journal is completed but Processed is not Y.",
                        errorText =
                            "The journal is completed but Processed is not Y."
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
                    MAcctSchema
                        .GetClientAcctSchema(
                            ctx,
                            ctx.GetAD_Client_ID()
                        );

                if (
                    accountingSchemas == null ||
                    accountingSchemas.Length == 0
                )
                {
                    return Json(
                        new
                        {
                            success = false,
                            error =
                                "No accounting schema was found for the client.",
                            errorText =
                                "No accounting schema was found for the client."
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

                if (!IsPostingSuccess(
                    postingResult
                ))
                {
                    string errorMessage =
                        GetPostingErrorMessage(
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
                            "Journal posted successfully."
                    }
                );
            }
            catch (Exception exception)
            {
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
        }

        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetUnpostedCount()
        {
            Ctx ctx = GetContext();

            if (ctx == null)
            {
                return GetSessionExpiredResult();
            }

            string sql = @"
WITH ProtectedJournal AS
(
" + BuildProtectedJournalSql(
                    ctx,
                    "GL_Journal.Posted = 'N' AND GL_Journal.DocStatus NOT IN ('VO')"
                ) + @"
)
SELECT
    COUNT(
        ProtectedJournal.GL_Journal_ID
    ) AS UnpostedCount
FROM ProtectedJournal ProtectedJournal";

            SqlParameter[] parameters =
            {
                new SqlParameter(
                    "@AD_Client_ID",
                    ctx.GetAD_Client_ID()
                )
            };

            DataSet dataSet =
                DB.ExecuteDataset(
                    sql,
                    parameters,
                    null
                );

            int unpostedCount = 0;

            if (
                dataSet != null &&
                dataSet.Tables.Count > 0 &&
                dataSet.Tables[0].Rows.Count > 0
            )
            {
                unpostedCount =
                    Util.GetValueOfInt(
                        dataSet.Tables[0]
                            .Rows[0]["UnpostedCount"]
                    );
            }

            return Json(
                JsonConvert.SerializeObject(
                    new
                    {
                        UnpostedCount =
                            unpostedCount
                    }
                ),
                JsonRequestBehavior.AllowGet
            );
        }

        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetUnpostedEntries()
        {
            Ctx ctx = GetContext();

            if (ctx == null)
            {
                return GetSessionExpiredResult();
            }

            string language =
                GetLanguage(ctx);

            string sql = @"
WITH SchemaCurrency AS
(
" + BuildSchemaCurrencySql() + @"
),
ProtectedJournal AS
(
" + BuildProtectedJournalSql(
                    ctx,
                    "GL_Journal.Posted = 'N' AND GL_Journal.DocStatus NOT IN ('VO')"
                ) + @"
),
DocumentStatusReference AS
(
" + BuildDocumentStatusReferenceSql() + @"
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
        DocumentStatusReference.BaseName AS DocStatusBaseName,
        DocumentStatusReference.TranslatedName AS DocStatusTranslatedName,
        ROUND(
            COALESCE(
                SUM(
                    COALESCE(
                        GL_JournalLine.AmtAcctDr,
                        0
                    )
                ),
                0
            ),
            COALESCE(
                MAX(
                    SchemaCurrency.StdPrecision
                ),
                2
            )
        ) AS TotalDebit,
        ROUND(
            COALESCE(
                SUM(
                    COALESCE(
                        GL_JournalLine.AmtAcctCr,
                        0
                    )
                ),
                0
            ),
            COALESCE(
                MAX(
                    SchemaCurrency.StdPrecision
                ),
                2
            )
        ) AS TotalCredit,
        MAX(
            SchemaCurrency.Cur_Symbol
        ) AS CurSymbol,
        MAX(
            SchemaCurrency.ISO_Code
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
    LEFT OUTER JOIN DocumentStatusReference DocumentStatusReference ON
    (
        DocumentStatusReference.Value =
        ProtectedJournal.DocStatus
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
)
SELECT
    JournalTotals.GL_Journal_ID,
    JournalTotals.DocumentNo,
    JournalTotals.DateAcct,
    JournalTotals.Description,
    JournalTotals.DocStatus,
    JournalTotals.Posted,
    JournalTotals.Processed,
    JournalTotals.DocStatusBaseName,
    JournalTotals.DocStatusTranslatedName,
    JournalTotals.TotalDebit,
    JournalTotals.TotalCredit,
    JournalTotals.CurSymbol,
    JournalTotals.ISOCode,
    JournalTotals.StdPrecision
FROM JournalTotals JournalTotals
ORDER BY
    JournalTotals.DateAcct DESC,
    JournalTotals.GL_Journal_ID DESC
FETCH FIRST 100 ROWS ONLY";

            SqlParameter[] parameters =
            {
                new SqlParameter(
                    "@AD_Client_ID",
                    ctx.GetAD_Client_ID()
                ),

                new SqlParameter(
                    "@AD_Language",
                    language
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

            decimal totalDebit = 0m;
            decimal totalCredit = 0m;

            string curSymbol =
                string.Empty;

            string isoCode =
                string.Empty;

            int stdPrecision = 2;

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
                    stdPrecision =
                        NormalizePrecision(
                            Util.GetValueOfInt(
                                row["StdPrecision"]
                            )
                        );

                    decimal debit =
                        Util.GetValueOfDecimal(
                            row["TotalDebit"]
                        );

                    decimal credit =
                        Util.GetValueOfDecimal(
                            row["TotalCredit"]
                        );

                    totalDebit += debit;
                    totalCredit += credit;

                    string docStatus =
                        Util.GetValueOfString(
                            row["DocStatus"]
                        );

                    string statusName =
                        GetReferenceName(
                            row,
                            "DocStatusTranslatedName",
                            "DocStatusBaseName",
                            docStatus
                        );

                    DateTime? dateAcct =
                        Util.GetValueOfDateTime(
                            row["DateAcct"]
                        );

                    curSymbol =
                        Util.GetValueOfString(
                            row["CurSymbol"]
                        );

                    isoCode =
                        Util.GetValueOfString(
                            row["ISOCode"]
                        );

                    entries.Add(
                        new
                        {
                            GL_Journal_ID =
                                Util.GetValueOfInt(
                                    row["GL_Journal_ID"]
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

                            DocStatus =
                                docStatus,

                            StatusName =
                                statusName,

                            Posted =
                                Util.GetValueOfString(
                                    row["Posted"]
                                ),

                            Processed =
                                Util.GetValueOfString(
                                    row["Processed"]
                                ),

                            TotalDebit =
                                Decimal.Round(
                                    debit,
                                    stdPrecision,
                                    MidpointRounding
                                        .AwayFromZero
                                ),

                            TotalCredit =
                                Decimal.Round(
                                    credit,
                                    stdPrecision,
                                    MidpointRounding
                                        .AwayFromZero
                                )
                        }
                    );
                }
            }

            return Json(
                JsonConvert.SerializeObject(
                    new
                    {
                        success = true,

                        Entries =
                            entries,

                        TotalCount =
                            entries.Count,

                        TotalDebit =
                            Decimal.Round(
                                totalDebit,
                                stdPrecision,
                                MidpointRounding
                                    .AwayFromZero
                            ),

                        TotalCredit =
                            Decimal.Round(
                                totalCredit,
                                stdPrecision,
                                MidpointRounding
                                    .AwayFromZero
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

        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetPostedPercentage()
        {
            Ctx ctx = GetContext();

            if (ctx == null)
            {
                return GetSessionExpiredResult();
            }

            string sql = @"
WITH ProtectedJournal AS
(
" + BuildProtectedJournalSql(
                    ctx,
                    "1 = 1"
                ) + @"
)
SELECT
    SUM(
        CASE
            WHEN ProtectedJournal.Posted = 'Y'
            THEN 1
            ELSE 0
        END
    ) AS PostedCount,
    COUNT(1) AS TotalCount
FROM ProtectedJournal ProtectedJournal";

            SqlParameter[] parameters =
            {
                new SqlParameter(
                    "@AD_Client_ID",
                    ctx.GetAD_Client_ID()
                )
            };

            DataSet dataSet =
                DB.ExecuteDataset(
                    sql,
                    parameters,
                    null
                );

            int postedCount = 0;
            int totalCount = 0;

            if (
                dataSet != null &&
                dataSet.Tables.Count > 0 &&
                dataSet.Tables[0].Rows.Count > 0
            )
            {
                postedCount =
                    Util.GetValueOfInt(
                        dataSet.Tables[0]
                            .Rows[0]["PostedCount"]
                    );

                totalCount =
                    Util.GetValueOfInt(
                        dataSet.Tables[0]
                            .Rows[0]["TotalCount"]
                    );
            }

            int percentage =
                totalCount > 0
                    ? (int)Math.Round(
                        (double)postedCount /
                        totalCount *
                        100
                    )
                    : 0;

            return Json(
                JsonConvert.SerializeObject(
                    new
                    {
                        PostedCount =
                            postedCount,

                        TotalCount =
                            totalCount,

                        Percentage =
                            percentage
                    }
                ),
                JsonRequestBehavior.AllowGet
            );
        }

        private string BuildSchemaCurrencySql()
        {
            return @"
SELECT
    ClientInfo.AD_Client_ID,
    AcctSchema.C_AcctSchema_ID,
    AcctSchema.Name AS AcctSchemaName,
    AcctSchema.C_Currency_ID,
    Currency.StdPrecision,
    Currency.ISO_Code,
    CASE
        WHEN Currency.CurSymbol IS NOT NULL
        THEN Currency.CurSymbol
        ELSE Currency.ISO_Code
    END AS Cur_Symbol
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
AND ClientInfo.AD_Client_ID =
@AD_Client_ID";
        }

        private string BuildPeriodRangeSql()
        {
            return @"
SELECT
    ClientInfo.AD_Client_ID,
    CurrentPeriod.StartDate AS MonthDateFrom,
    CurrentPeriod.EndDate AS MonthDateTo,
    (
        SELECT
            MIN(
                PeriodYTD.StartDate
            )
        FROM C_Period PeriodYTD
        WHERE PeriodYTD.C_Year_ID =
        CurrentPeriod.C_Year_ID
        AND PeriodYTD.IsActive = 'Y'
    ) AS YTDDateFrom,
    CurrentPeriod.EndDate AS YTDDateTo
FROM AD_ClientInfo ClientInfo
INNER JOIN C_Year YearData ON
(
    YearData.C_Calendar_ID =
    ClientInfo.C_Calendar_ID
)
INNER JOIN C_Period CurrentPeriod ON
(
    CurrentPeriod.C_Year_ID =
    YearData.C_Year_ID
)
WHERE ClientInfo.IsActive = 'Y'
AND CurrentPeriod.IsActive = 'Y'
AND ClientInfo.AD_Client_ID =
@AD_Client_ID
AND CURRENT_DATE >=
CurrentPeriod.StartDate
AND CURRENT_DATE <
" + GetDateToExclusiveExpression(
                "CurrentPeriod.EndDate"
            );
        }


        private string BuildProtectedJournalSql(
            Ctx ctx,
            string extraWhere)
        {
            string sql = @"
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
    GL_Journal.PostingType,
    GL_Journal.Created,
    GL_Journal.CreatedBy
FROM GL_Journal GL_Journal
WHERE GL_Journal.IsActive = 'Y'
AND GL_Journal.AD_Client_ID =
@AD_Client_ID
AND " + extraWhere;

            return MRole.GetDefault(ctx)
                .AddAccessSQL(
                    sql,
                    "GL_Journal",
                    MRole.SQL_FULLYQUALIFIED,
                    MRole.SQL_RO
                );
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
                throw new InvalidOperationException(
                    "Journal details not found."
                );
            }

            if (!journal.IsActive())
            {
                throw new InvalidOperationException(
                    "The journal is inactive."
                );
            }

            if (
                journal.GetAD_Client_ID() !=
                ctx.GetAD_Client_ID()
            )
            {
                throw new InvalidOperationException(
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
                errorMessage =
                    "Could not save the journal.";
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
                 * Keep the fallback message.
                 */
            }

            return fallback;
        }

        private bool IsPostingSuccess(
            string postingResult)
        {
            if (string.IsNullOrWhiteSpace(
                postingResult
            ))
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
            string postingResult)
        {
            string result =
                postingResult == null
                    ? string.Empty
                    : postingResult.Trim();

            if (string.Equals(
                result,
                Doc.STATUS_NotBalanced,
                StringComparison.OrdinalIgnoreCase))
            {
                return
                    "The journal could not be posted because it is not balanced.";
            }

            if (string.Equals(
                result,
                Doc.STATUS_NotConvertible,
                StringComparison.OrdinalIgnoreCase))
            {
                return
                    "The journal could not be posted because currency conversion is missing.";
            }

            if (string.Equals(
                result,
                Doc.STATUS_PeriodClosed,
                StringComparison.OrdinalIgnoreCase))
            {
                return
                    "The journal could not be posted because the accounting period is closed.";
            }

            if (string.Equals(
                result,
                Doc.STATUS_InvalidAccount,
                StringComparison.OrdinalIgnoreCase))
            {
                return
                    "The journal could not be posted because one or more accounts are invalid.";
            }

            if (string.IsNullOrWhiteSpace(
                result
            ))
            {
                return
                    "The journal could not be posted.";
            }

            return
                "Accounting posting failed: " +
                result;
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
WHERE GL_Journal.GL_Journal_ID =
@JournalID";

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

        private string GetReferenceName(
            DataRow row,
            string translatedColumn,
            string baseColumn,
            string fallback)
        {
            string translatedName =
                Util.GetValueOfString(
                    row[translatedColumn]
                );

            if (!string.IsNullOrWhiteSpace(
                translatedName
            ))
            {
                return translatedName;
            }

            string baseName =
                Util.GetValueOfString(
                    row[baseColumn]
                );

            if (!string.IsNullOrWhiteSpace(
                baseName
            ))
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
                !string.IsNullOrWhiteSpace(code) &&
                !string.IsNullOrWhiteSpace(name)
            )
            {
                return code + " · " + name;
            }

            if (!string.IsNullOrWhiteSpace(code))
            {
                return code;
            }

            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }

            return string.Empty;
        }

        private string GetLanguage(
            Ctx ctx)
        {
            string language =
                ctx == null
                    ? string.Empty
                    : ctx.GetAD_Language();

            if (string.IsNullOrWhiteSpace(
                language
            ))
            {
                language = "en_US";
            }

            return language;
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

        private string BuildDocumentStatusReferenceSql()
        {
            return @"
        SELECT
            AD_Ref_List.Value,
            AD_Ref_List.Name AS BaseName,
            AD_Ref_List_Trl.Name AS TranslatedName
        FROM AD_Reference
        INNER JOIN AD_Ref_List ON
        (
            AD_Reference.AD_Reference_ID =
            AD_Ref_List.AD_Reference_ID
        )
        INNER JOIN AD_Ref_List_Trl ON
        (
            AD_Ref_List.AD_Ref_List_ID =
            AD_Ref_List_Trl.AD_Ref_List_ID
            AND AD_Ref_List_Trl.AD_Language =
            @AD_Language
        )
        WHERE AD_Reference.Name = 'DocStatus'
        AND AD_Reference.IsActive = 'Y'
        AND AD_Ref_List.IsActive = 'Y'";
        }


        private string GetDateToExclusiveExpression(
            string dateColumn)
        {
            if (DB.IsOracle())
            {
                return dateColumn + " + 1";
            }

            return
                dateColumn +
                " + INTERVAL '1 day'";
        }

        private class JournalPostingState
        {
            public string DocumentNo { get; set; }

            public string DocStatus { get; set; }

            public string Processed { get; set; }

            public string Posted { get; set; }
        }
    }
}