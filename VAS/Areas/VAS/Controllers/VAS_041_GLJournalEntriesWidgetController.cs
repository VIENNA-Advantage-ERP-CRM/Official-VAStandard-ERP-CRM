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
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetMonthlyEntries(
            int pageNo = 1,
            int pageSize = 10)
        {
            return GetJournalEntries(
                false,
                pageNo,
                pageSize
            );
        }

        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetUnpostedEntries(
            int pageNo = 1,
            int pageSize = 10)
        {
            return GetJournalEntries(
                true,
                pageNo,
                pageSize
            );
        }

        private JsonResult GetJournalEntries(
            bool onlyUnposted,
            int pageNo,
            int pageSize)
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

                string language = GetLanguage(ctx);

                string schemaParameter =
                    onlyUnposted
                        ? "@UnpostedSchemaClientID"
                        : "@MonthlySchemaClientID";

                string periodParameter =
                    "@MonthlyPeriodClientID";

                string journalClientParameter =
                    onlyUnposted
                        ? "@UnpostedJournalClientID"
                        : "@MonthlyJournalClientID";

                string languageParameter =
                    onlyUnposted
                        ? "@UnpostedLanguage"
                        : "@MonthlyLanguage";

                string journalCondition =
                    onlyUnposted
                        ? @"
COALESCE(
    GL_Journal.Posted,
    'N'
) <> 'Y'
AND GL_Journal.DocStatus <> 'VO'"
                        : @"
GL_Journal.PostingType = 'A'";

                string periodCte = string.Empty;
                string periodJoin = string.Empty;

                if (!onlyUnposted)
                {
                    periodCte = @",
PeriodRange AS
(
" + BuildCurrentPeriodSql(periodParameter) + @"
)";

                    periodJoin = @"
INNER JOIN PeriodRange PeriodRange ON
(
    PeriodRange.AD_Client_ID =
    ProtectedJournal.AD_Client_ID
    AND ProtectedJournal.DateAcct >=
    PeriodRange.MonthDateFrom
    AND ProtectedJournal.DateAcct <
" + GetDateToExclusiveExpression(
                        "PeriodRange.MonthDateTo"
                    ) + @"
)";
                }

                string sql = @"
WITH SchemaCurrency AS
(
" + BuildSchemaCurrencySql(
                    schemaParameter
                ) + @"
)" +
periodCte + @",
ProtectedJournal AS
(
" + BuildProtectedJournalSql(
                    ctx,
                    journalClientParameter,
                    journalCondition
                ) + @"
),
DocumentStatusReference AS
(
" + BuildDocumentStatusReferenceSql(
                    languageParameter
                ) + @"
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
            AS DocStatusBaseName,

        DocumentStatusReference.TranslatedName
            AS DocStatusTranslatedName,

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

" + periodJoin + @"

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
        JournalTotals.DocStatusBaseName,
        JournalTotals.DocStatusTranslatedName,
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
        ) AS RowNumber,

        COUNT(1) OVER
        (
        ) AS TotalCount,

        SUM(JournalTotals.TotalDebit) OVER
        (
        ) AS GrandTotalDebit,

        SUM(JournalTotals.TotalCredit) OVER
        (
        ) AS GrandTotalCredit

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
    OrderedJournals.DocStatusBaseName,
    OrderedJournals.DocStatusTranslatedName,
    OrderedJournals.TotalDebit,
    OrderedJournals.TotalCredit,
    OrderedJournals.CurSymbol,
    OrderedJournals.ISOCode,
    OrderedJournals.StdPrecision,
    OrderedJournals.TotalCount,
    OrderedJournals.GrandTotalDebit,
    OrderedJournals.GrandTotalCredit

FROM OrderedJournals OrderedJournals

WHERE OrderedJournals.RowNumber BETWEEN
    @PageRowStart
    AND
    @PageRowEnd

ORDER BY
    OrderedJournals.RowNumber";

                List<SqlParameter> parameterList =
                    new List<SqlParameter>();

                parameterList.Add(
                    new SqlParameter(
                        schemaParameter,
                        ctx.GetAD_Client_ID()
                    )
                );

                if (!onlyUnposted)
                {
                    parameterList.Add(
                        new SqlParameter(
                            periodParameter,
                            ctx.GetAD_Client_ID()
                        )
                    );
                }

                parameterList.Add(
                    new SqlParameter(
                        journalClientParameter,
                        ctx.GetAD_Client_ID()
                    )
                );

                parameterList.Add(
                    new SqlParameter(
                        languageParameter,
                        language
                    )
                );

                parameterList.Add(
                    new SqlParameter(
                        "@PageRowStart",
                        rowStart
                    )
                );

                parameterList.Add(
                    new SqlParameter(
                        "@PageRowEnd",
                        rowEnd
                    )
                );

                DataSet dataSet =
                    DB.ExecuteDataset(
                        sql,
                        parameterList.ToArray(),
                        null
                    );

                List<object> entries =
                    new List<object>();

                decimal totalDebit = 0m;
                decimal totalCredit = 0m;

                string curSymbol = string.Empty;
                string isoCode = string.Empty;

                int stdPrecision = 2;

                int totalCount = 0;

                decimal grandTotalDebit = 0m;
                decimal grandTotalCredit = 0m;

                DateTime? displayPeriodDate =
                    null;

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
                        stdPrecision =
                            NormalizePrecision(
                                Util.GetValueOfInt(
                                    row["StdPrecision"]
                                )
                            );

                        decimal debit =
                            Decimal.Round(
                                Util.GetValueOfDecimal(
                                    row["TotalDebit"]
                                ),
                                stdPrecision,
                                MidpointRounding.AwayFromZero
                            );

                        decimal credit =
                            Decimal.Round(
                                Util.GetValueOfDecimal(
                                    row["TotalCredit"]
                                ),
                                stdPrecision,
                                MidpointRounding.AwayFromZero
                            );

                        if (totalCount <= 0)
                        {
                            totalCount =
                                Util.GetValueOfInt(
                                    row["TotalCount"]
                                );
                        }

                        if (
                            grandTotalDebit == 0m &&
                            grandTotalCredit == 0m
                        )
                        {
                            grandTotalDebit =
                                Util.GetValueOfDecimal(
                                    row["GrandTotalDebit"]
                                );

                            grandTotalCredit =
                                Util.GetValueOfDecimal(
                                    row["GrandTotalCredit"]
                                );
                        }

                        totalDebit += debit;
                        totalCredit += credit;

                        string docStatus =
                            Util.GetValueOfString(
                                row["DocStatus"]
                            );

                        string statusName =
                            Util.GetValueOfString(
                                row[
                                    "DocStatusTranslatedName"
                                ]
                            );

                        if (
                            string.IsNullOrWhiteSpace(
                                statusName
                            )
                        )
                        {
                            statusName =
                                Util.GetValueOfString(
                                    row[
                                        "DocStatusBaseName"
                                    ]
                                );
                        }

                        if (
                            string.IsNullOrWhiteSpace(
                                statusName
                            )
                        )
                        {
                            statusName =
                                docStatus;
                        }

                        DateTime? dateAcct =
                            Util.GetValueOfDateTime(
                                row["DateAcct"]
                            );

                        if (
                            dateAcct.HasValue &&
                            !displayPeriodDate.HasValue
                        )
                        {
                            displayPeriodDate =
                                dateAcct.Value;
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
                                    debit,

                                TotalCredit =
                                    credit
                            }
                        );
                    }
                }

                DateTime displayDate =
                    displayPeriodDate.HasValue
                        ? displayPeriodDate.Value
                        : DateTime.Now;

                return JsonString(
                    new
                    {
                        success = true,

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

                        TotalDebit =
                            Decimal.Round(
                                grandTotalDebit,
                                stdPrecision,
                                MidpointRounding.AwayFromZero
                            ),

                        TotalCredit =
                            Decimal.Round(
                                grandTotalCredit,
                                stdPrecision,
                                MidpointRounding.AwayFromZero
                            ),

                        PageTotalDebit =
                            Decimal.Round(
                                totalDebit,
                                stdPrecision,
                                MidpointRounding.AwayFromZero
                            ),

                        PageTotalCredit =
                            Decimal.Round(
                                totalCredit,
                                stdPrecision,
                                MidpointRounding.AwayFromZero
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
                );
            }
            catch (Exception exception)
            {
                return JsonString(
                    new
                    {
                        success = false,
                        error = true,
                        errorText =
                            exception.Message
                    }
                );
            }
        }

        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetMonthlyEntryCount()
        {
            Ctx ctx = GetContext();

            if (ctx == null)
            {
                return GetSessionExpiredResult();
            }

            try
            {
                string sql = @"
WITH PeriodRange AS
(
" + BuildCurrentPeriodSql(
                    "@CountPeriodClientID"
                ) + @"
),
ProtectedJournal AS
(
" + BuildProtectedJournalSql(
                    ctx,
                    "@CountJournalClientID",
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

LEFT OUTER JOIN ProtectedJournal ProtectedJournal ON
(
    ProtectedJournal.AD_Client_ID =
    PeriodRange.AD_Client_ID

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
                        "@CountPeriodClientID",
                        ctx.GetAD_Client_ID()
                    ),

                    new SqlParameter(
                        "@CountJournalClientID",
                        ctx.GetAD_Client_ID()
                    )
                };

                DataSet dataSet =
                    DB.ExecuteDataset(
                        sql,
                        parameters,
                        null
                    );

                int entryCount = 0;

                DateTime? monthDateFrom =
                    null;

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

                return JsonString(
                    new
                    {
                        success = true,

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
                );
            }
            catch (Exception exception)
            {
                return JsonString(
                    new
                    {
                        success = false,
                        error = true,
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

            try
            {
                string sql = @"
WITH ProtectedJournal AS
(
" + BuildProtectedJournalSql(
                    ctx,
                    "@UnpostedCountClientID",
                    @"
COALESCE(
    GL_Journal.Posted,
    'N'
) <> 'Y'
AND GL_Journal.DocStatus <> 'VO'"
                ) + @"
)
SELECT
    COUNT(1) AS UnpostedCount

FROM ProtectedJournal ProtectedJournal";

                SqlParameter[] parameters =
                {
                    new SqlParameter(
                        "@UnpostedCountClientID",
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

                return JsonString(
                    new
                    {
                        success = true,

                        UnpostedCount =
                            unpostedCount
                    }
                );
            }
            catch (Exception exception)
            {
                return JsonString(
                    new
                    {
                        success = false,
                        error = true,
                        errorText =
                            exception.Message
                    }
                );
            }
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

            try
            {
                string sql = @"
WITH ProtectedJournal AS
(
" + BuildProtectedJournalSql(
                    ctx,
                    "@PercentageClientID",
                    "1 = 1"
                ) + @"
)
SELECT
    COALESCE(
        SUM(
            CASE
                WHEN ProtectedJournal.Posted = 'Y'
                THEN 1
                ELSE 0
            END
        ),
        0
    ) AS PostedCount,

    COUNT(1) AS TotalCount

FROM ProtectedJournal ProtectedJournal";

                SqlParameter[] parameters =
                {
                    new SqlParameter(
                        "@PercentageClientID",
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
                            (
                                (double)postedCount /
                                totalCount
                            ) * 100
                        )
                        : 0;

                return JsonString(
                    new
                    {
                        success = true,

                        PostedCount =
                            postedCount,

                        TotalCount =
                            totalCount,

                        Percentage =
                            percentage
                    }
                );
            }
            catch (Exception exception)
            {
                return JsonString(
                    new
                    {
                        success = false,
                        error = true,
                        errorText =
                            exception.Message
                    }
                );
            }
        }

        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetJournalEntryDetail(
            int journalId,
            int pageNo = 1,
            int pageSize = 3)
        {
            Ctx ctx = GetContext();

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
                            "Invalid journal ID."
                    }
                );
            }

            try
            {
                pageNo =
                    Math.Max(
                        pageNo,
                        1
                    );

                pageSize =
                    Math.Max(
                        Math.Min(
                            pageSize,
                            25
                        ),
                        1
                    );

                int startRow =
                    ((pageNo - 1) * pageSize) + 1;

                int endRow =
                    pageNo * pageSize;

                string language =
                    GetLanguage(ctx);

                string headerSql = @"
WITH ProtectedJournal AS
(
" + BuildProtectedJournalSql(
                    ctx,
                    "@DetailHeaderClientID",
                    @"
GL_Journal.GL_Journal_ID =
@DetailHeaderJournalID"
                ) + @"
),
SchemaCurrency AS
(
" + BuildSchemaCurrencySql(
                    "@DetailSchemaClientID"
                ) + @"
),
DocumentStatusReference AS
(
" + BuildDocumentStatusReferenceSql(
                    "@DetailLanguage"
                ) + @"
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

    DocumentStatusReference.BaseName
        AS DocStatusBaseName,

    DocumentStatusReference.TranslatedName
        AS DocStatusTranslatedName,

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

LEFT OUTER JOIN DocumentStatusReference
    DocumentStatusReference ON
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
                        "@DetailHeaderClientID",
                        ctx.GetAD_Client_ID()
                    ),

                    new SqlParameter(
                        "@DetailHeaderJournalID",
                        journalId
                    ),

                    new SqlParameter(
                        "@DetailSchemaClientID",
                        ctx.GetAD_Client_ID()
                    ),

                    new SqlParameter(
                        "@DetailLanguage",
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
                    headerDataSet.Tables[0].Rows.Count == 0
                )
                {
                    return JsonString(
                        new
                        {
                            success = false,
                            error = true,
                            errorText =
                                "Journal details not found."
                        }
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
                    "@DetailLineClientID",
                    @"
GL_Journal.GL_Journal_ID =
@DetailLineJournalID"
                ) + @"
),
LineRows AS
(
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
        Project.Name AS ProjectName,

        COUNT(1) OVER
        (
        ) AS TotalLineCount,

        ROW_NUMBER() OVER
        (
            ORDER BY
                GL_JournalLine.Line,
                GL_JournalLine.GL_JournalLine_ID
        ) AS LineRowNumber

    FROM ProtectedJournal ProtectedJournal

    INNER JOIN GL_JournalLine GL_JournalLine ON
    (
        ProtectedJournal.GL_Journal_ID =
        GL_JournalLine.GL_Journal_ID
    )

    LEFT OUTER JOIN C_ValidCombination
        ValidCombination ON
    (
        GL_JournalLine.C_ValidCombination_ID =
        ValidCombination.C_ValidCombination_ID
    )

    LEFT OUTER JOIN C_ElementValue
        AccountValue ON
    (
        ValidCombination.Account_ID =
        AccountValue.C_ElementValue_ID
    )

    LEFT OUTER JOIN C_ElementValue
        CostCenterValue ON
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
)
SELECT
    LineRows.GL_JournalLine_ID,
    LineRows.Line,
    LineRows.C_ValidCombination_ID,
    LineRows.Account_ID,
    LineRows.AccountCode,
    LineRows.AccountName,
    LineRows.AmtAcctDr,
    LineRows.AmtAcctCr,
    LineRows.CostCenterCode,
    LineRows.CostCenterName,
    LineRows.BPartnerName,
    LineRows.ProductName,
    LineRows.ProjectName,
    LineRows.TotalLineCount

FROM LineRows LineRows

WHERE LineRows.LineRowNumber >= @DetailLineStartRow
AND LineRows.LineRowNumber <= @DetailLineEndRow

ORDER BY
    LineRows.LineRowNumber";

                SqlParameter[] lineParameters =
                {
                    new SqlParameter(
                        "@DetailLineClientID",
                        ctx.GetAD_Client_ID()
                    ),

                    new SqlParameter(
                        "@DetailLineJournalID",
                        journalId
                    ),

                    new SqlParameter(
                        "@DetailLineStartRow",
                        startRow
                    ),

                    new SqlParameter(
                        "@DetailLineEndRow",
                        endRow
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

                int totalLineCount =
                    0;

                if (
                    lineDataSet != null &&
                    lineDataSet.Tables.Count > 0
                )
                {
                    foreach (
                        DataRow row in
                        lineDataSet.Tables[0].Rows
                    )
                    {
                        if (totalLineCount <= 0)
                        {
                            totalLineCount =
                                Util.GetValueOfInt(
                                    row["TotalLineCount"]
                                );
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

                        lines.Add(
                            new
                            {
                                GL_JournalLine_ID =
                                    Util.GetValueOfInt(
                                        row[
                                            "GL_JournalLine_ID"
                                        ]
                                    ),

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
                                    GetDisplayValue(
                                        row["BPartnerName"]
                                    ),

                                Product =
                                    GetDisplayValue(
                                        row["ProductName"]
                                    ),

                                Project =
                                    GetDisplayValue(
                                        row["ProjectName"]
                                    )
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

                return JsonString(
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
                                GetDefaultValue(
                                    headerRow[
                                        "AccountingBook"
                                    ],
                                    "Primary"
                                ),

                            CreatedByName =
                                GetDisplayValue(
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
                            totalLineCount,

                        LinePageNo =
                            pageNo,

                        LinePageSize =
                            pageSize,

                        LineTotalPages =
                            pageSize <= 0
                                ? 0
                                : Convert.ToInt32(
                                    Math.Ceiling(
                                        totalLineCount /
                                        (decimal)pageSize
                                    )
                                ),

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
                );
            }
            catch (Exception exception)
            {
                return JsonString(
                    new
                    {
                        success = false,
                        error = true,
                        errorText =
                            exception.Message
                    }
                );
            }
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

                    if (
                        !journal.ProcessIt(
                            DocActionVariables
                                .ACTION_PREPARE
                        )
                    )
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

                if (
                    !string.Equals(
                        docStatus,
                        "IP",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    throw new InvalidOperationException(
                        "Only Draft, In Progress or Not Approved journals can be approved."
                    );
                }

                journal.SetDocAction(
                    DocActionVariables
                        .ACTION_APPROVE
                );

                if (
                    !journal.ProcessIt(
                        DocActionVariables
                            .ACTION_APPROVE
                    )
                )
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

        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetJournalPrintInfo(
            int journalId)
        {
            Ctx ctx = GetContext();

            if (ctx == null)
            {
                return GetSessionExpiredResult();
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

            int tableId =
                MTable.Get_Table_ID(
                    "GL_Journal"
                );

            return JsonString(
                new
                {
                    success = true,
                    AD_Process_ID =
                        GetJournalPrintProcessId(
                            tableId
                        ),
                    AD_Table_ID =
                        tableId,
                    Record_ID =
                        journalId
                }
            );
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
                            "VAS041PostJournal"
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
                                "The journal is already posted."
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
                        DocActionVariables
                            .ACTION_COMPLETE
                    );

                    if (
                        !journal.ProcessIt(
                            DocActionVariables
                                .ACTION_COMPLETE
                        )
                    )
                    {
                        throw new InvalidOperationException(
                            GetJournalProcessError(
                                journal,
                                "The journal could not be completed."
                            )
                        );
                    }

                    SaveJournal(journal);
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
                        "The journal must be approved before it can be posted."
                    );
                }

                transaction.Commit();
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
                    transaction = null;
                }
            }

            JournalPostingState journalState =
                GetJournalPostingState(
                    ctx,
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

            if (
                !string.Equals(
                    journalState.Processed,
                    "Y",
                    StringComparison.OrdinalIgnoreCase
                )
            )
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
                int tableId =
                    MTable.Get_Table_ID(
                        "GL_Journal"
                    );

                MAcctSchema[] schemas =
                    MAcctSchema.GetClientAcctSchema(
                        ctx,
                        ctx.GetAD_Client_ID()
                    );

                if (
                    schemas == null ||
                    schemas.Length == 0
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
                        schemas,
                        tableId,
                        journalId,
                        false,
                        null
                    );

                if (!IsPostingSuccess(postingResult))
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
                    return Json(
                        new
                        {
                            success = false,
                            error =
                                "The accounting engine finished, but the journal was not posted.",
                            errorText =
                                "The accounting engine finished, but the journal was not posted."
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

        private string BuildSchemaCurrencySql(
            string clientParameter)
        {
            return @"
SELECT
    AcctSchema.AD_Client_ID,
    AcctSchema.C_AcctSchema_ID,
    AcctSchema.Name AS AcctSchemaName,
    Currency.StdPrecision,
    Currency.ISO_Code AS ISOCode,

    CASE
        WHEN Currency.CurSymbol IS NOT NULL
        THEN Currency.CurSymbol
        ELSE Currency.ISO_Code
    END AS CurSymbol

FROM C_AcctSchema AcctSchema

INNER JOIN C_Currency Currency ON
(
    AcctSchema.C_Currency_ID =
    Currency.C_Currency_ID
)

WHERE AcctSchema.IsActive = 'Y'
AND Currency.IsActive = 'Y'
AND AcctSchema.AD_Client_ID =
" + clientParameter;
        }

        private string BuildCurrentPeriodSql(
            string clientParameter)
        {
            return @"
SELECT
    PeriodCandidates.AD_Client_ID,
    PeriodCandidates.MonthDateFrom,
    PeriodCandidates.MonthDateTo

FROM
(
    SELECT
        ClientInfo.AD_Client_ID,

        CurrentPeriod.StartDate
            AS MonthDateFrom,

        CurrentPeriod.EndDate
            AS MonthDateTo,

        ROW_NUMBER() OVER
        (
            ORDER BY
                CurrentPeriod.StartDate DESC,
                CurrentPeriod.C_Period_ID DESC
        ) AS PeriodRowNumber

    FROM AD_ClientInfo ClientInfo

    INNER JOIN C_Year YearData ON
    (
        YearData.C_Calendar_ID =
        ClientInfo.C_Calendar_ID

        AND YearData.IsActive = 'Y'
    )

    INNER JOIN C_Period CurrentPeriod ON
    (
        CurrentPeriod.C_Year_ID =
        YearData.C_Year_ID

        AND CurrentPeriod.IsActive = 'Y'
    )

    WHERE ClientInfo.IsActive = 'Y'

    AND ClientInfo.AD_Client_ID =
    " + clientParameter + @"

    AND CURRENT_DATE >=
    CurrentPeriod.StartDate

    AND CURRENT_DATE <
    " + GetDateToExclusiveExpression(
                "CurrentPeriod.EndDate"
            ) + @"
) PeriodCandidates

WHERE PeriodCandidates.PeriodRowNumber = 1";
        }

        private string BuildProtectedJournalSql(
            Ctx ctx,
            string clientParameter,
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
" + clientParameter + @"

AND
(
" + extraWhere + @"
)";

            return MRole.GetDefault(ctx)
                .AddAccessSQL(
                    sql,
                    "GL_Journal",
                    MRole.SQL_FULLYQUALIFIED,
                    MRole.SQL_RO
                );
        }

        private string BuildDocumentStatusReferenceSql(
            string languageParameter)
        {
            return @"
SELECT
    AD_Ref_List.Value,
    AD_Ref_List.Name AS BaseName,

    AD_Ref_List_Trl.Name
        AS TranslatedName

FROM AD_Reference AD_Reference

INNER JOIN AD_Ref_List AD_Ref_List ON
(
    AD_Reference.AD_Reference_ID =
    AD_Ref_List.AD_Reference_ID
)

LEFT OUTER JOIN AD_Ref_List_Trl
    AD_Ref_List_Trl ON
(
    AD_Ref_List.AD_Ref_List_ID =
    AD_Ref_List_Trl.AD_Ref_List_ID

    AND AD_Ref_List_Trl.AD_Language =
    " + languageParameter + @"
)

WHERE AD_Reference.Name = '_Document Status'
AND AD_Reference.IsActive = 'Y'
AND AD_Ref_List.IsActive = 'Y'";
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

            int tableId =
                MTable.Get_Table_ID(
                    "GL_Journal"
                );

            MRole role =
                MRole.GetDefault(ctx);

            if (
                !role.IsRecordAccess(
                    tableId,
                    journalId,
                    false
                )
            )
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

        private int GetJournalPrintProcessId(
            int tableId)
        {
            int windowId =
                GetJournalWindowId();

            if (windowId > 0)
            {
                SqlParameter[] windowParameters =
                    new SqlParameter[]
                    {
                        new SqlParameter(
                            "@AD_Table_ID",
                            tableId
                        ),
                        new SqlParameter(
                            "@AD_Window_ID",
                            windowId
                        )
                    };

                int processId =
                    Util.GetValueOfInt(
                        DB.ExecuteScalar(
                            @"
SELECT
    AD_Tab.AD_Process_ID
FROM AD_Tab AD_Tab
INNER JOIN AD_Process AD_Process ON
(
    AD_Tab.AD_Process_ID =
    AD_Process.AD_Process_ID
)
WHERE AD_Tab.AD_Table_ID = @AD_Table_ID
AND AD_Tab.AD_Window_ID = @AD_Window_ID
AND AD_Tab.AD_Process_ID IS NOT NULL
AND AD_Tab.AD_Process_ID > 0
AND AD_Tab.IsActive = 'Y'
AND AD_Process.IsActive = 'Y'
AND AD_Process.IsReport = 'Y'
AND AD_Process.AD_ReportView_ID IS NOT NULL
AND EXISTS
(
    SELECT
        1
    FROM AD_PrintFormat AD_PrintFormat
    WHERE AD_PrintFormat.AD_ReportView_ID =
        AD_Process.AD_ReportView_ID
    AND AD_PrintFormat.AD_Table_ID = @AD_Table_ID
    AND AD_PrintFormat.IsActive = 'Y'
)
ORDER BY
    AD_Tab.SeqNo",
                            windowParameters,
                            null
                        )
                    );

                if (processId > 0)
                {
                    return processId;
                }
            }

            SqlParameter[] parameters =
                new SqlParameter[]
                {
                    new SqlParameter(
                        "@AD_Table_ID",
                        tableId
                    )
                };

            return Util.GetValueOfInt(
                DB.ExecuteScalar(
                    @"
SELECT
    AD_Tab.AD_Process_ID
FROM AD_Tab AD_Tab
INNER JOIN AD_Window AD_Window ON
(
    AD_Tab.AD_Window_ID =
    AD_Window.AD_Window_ID
)
INNER JOIN AD_Process AD_Process ON
(
    AD_Tab.AD_Process_ID =
    AD_Process.AD_Process_ID
)
WHERE AD_Tab.AD_Table_ID = @AD_Table_ID
AND AD_Tab.AD_Process_ID IS NOT NULL
AND AD_Tab.AD_Process_ID > 0
AND AD_Tab.IsActive = 'Y'
AND AD_Window.IsActive = 'Y'
AND AD_Process.IsActive = 'Y'
AND AD_Process.IsReport = 'Y'
AND AD_Process.AD_ReportView_ID IS NOT NULL
AND EXISTS
(
    SELECT
        1
    FROM AD_PrintFormat AD_PrintFormat
    WHERE AD_PrintFormat.AD_ReportView_ID =
        AD_Process.AD_ReportView_ID
    AND AD_PrintFormat.AD_Table_ID = @AD_Table_ID
    AND AD_PrintFormat.IsActive = 'Y'
)
ORDER BY
    CASE
        WHEN AD_Window.Name = 'VAS_GLJournal' THEN 0
        WHEN AD_Window.Name = 'GL Journal' THEN 1
        ELSE 2
    END,
    AD_Tab.SeqNo",
                    parameters,
                    null
                )
            );
        }

        private int GetJournalWindowId()
        {
            int windowId =
                GetWindowIdByName(
                    "VAS_GLJournal"
                );

            if (windowId > 0)
            {
                return windowId;
            }

            windowId =
                GetWindowIdByName(
                    "GL Journal"
                );

            if (windowId > 0)
            {
                return windowId;
            }

            SqlParameter[] parameters =
                new SqlParameter[]
                {
                    new SqlParameter(
                        "@Name",
                        "VAS_GLJournal"
                    )
                };

            return Util.GetValueOfInt(
                DB.ExecuteScalar(
                    @"
SELECT
    AD_Window.AD_Window_ID
FROM VAS_ZoomScreenConfig VAS_ZoomScreenConfig
INNER JOIN AD_Window AD_Window ON
(
    VAS_ZoomScreenConfig.Value =
    AD_Window.Name
)
WHERE VAS_ZoomScreenConfig.Name = @Name
AND VAS_ZoomScreenConfig.IsActive = 'Y'
AND AD_Window.IsActive = 'Y'",
                    parameters,
                    null
                )
            );
        }

        private int GetWindowIdByName(
            string windowName)
        {
            SqlParameter[] parameters =
                new SqlParameter[]
                {
                    new SqlParameter(
                        "@Name",
                        windowName
                    )
                };

            return Util.GetValueOfInt(
                DB.ExecuteScalar(
                    @"
SELECT
    MIN(AD_Window_ID)
FROM AD_Window
WHERE Name = @Name
AND IsActive = 'Y'",
                    parameters,
                    null
                )
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

            string value =
                Util.GetValueOfString(
                    postedValue
                );

            return
                string.Equals(
                    value,
                    "Y",
                    StringComparison.OrdinalIgnoreCase
                ) ||
                string.Equals(
                    value,
                    "TRUE",
                    StringComparison.OrdinalIgnoreCase
                ) ||
                string.Equals(
                    value,
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
                return
                    "The journal could not be posted because it is not balanced.";
            }

            if (
                string.Equals(
                    result,
                    Doc.STATUS_NotConvertible,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return
                    "The journal could not be posted because currency conversion is missing.";
            }

            if (
                string.Equals(
                    result,
                    Doc.STATUS_PeriodClosed,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return
                    "The journal could not be posted because the accounting period is closed.";
            }

            if (
                string.Equals(
                    result,
                    Doc.STATUS_InvalidAccount,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return
                    "The journal could not be posted because one or more accounts are invalid.";
            }

            if (string.IsNullOrWhiteSpace(result))
            {
                return
                    "The journal could not be posted.";
            }

            return
                "Accounting posting failed: " +
                result;
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

        private string GetDisplayValue(
            object value)
        {
            string text =
                Util.GetValueOfString(value);

            return string.IsNullOrWhiteSpace(text)
                ? "-"
                : text;
        }

        private string GetDefaultValue(
            object value,
            string defaultValue)
        {
            string text =
                Util.GetValueOfString(value);

            return string.IsNullOrWhiteSpace(text)
                ? defaultValue
                : text;
        }

        private string GetLanguage(
            Ctx ctx)
        {
            string language =
                ctx == null
                    ? string.Empty
                    : ctx.GetAD_Language();

            return string.IsNullOrWhiteSpace(language)
                ? "en_US"
                : language;
        }

        private Ctx GetContext()
        {
            if (Session["ctx"] == null)
            {
                return null;
            }

            return Session["ctx"] as Ctx;
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

        private JsonResult GetSessionExpiredResult()
        {
            return JsonString(
                new
                {
                    success = false,
                    error = true,
                    errorText =
                        "Session Expired"
                }
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

        private string GetDateToExclusiveExpression(
            string dateColumn)
        {
            if (DB.IsOracle())
            {
                return
                    dateColumn +
                    " + 1";
            }

            return
                dateColumn +
                " + INTERVAL '1 day'";
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