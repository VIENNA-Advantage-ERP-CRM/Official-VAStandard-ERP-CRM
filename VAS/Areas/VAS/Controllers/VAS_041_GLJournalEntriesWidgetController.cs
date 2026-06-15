using CoreLibrary.DataBase;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Web.Mvc;
using VAdvantage.Model;
using VAdvantage.Utility;

namespace VAS.Controllers
{
    public class VAS_041_GLJournalEntriesWidgetController : Controller
    {
        public JsonResult GetMonthlyEntryCount()
        {
            if (Session["ctx"] == null)
            {
                return Json("", JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            string sql = @"
WITH SchemaCurrency AS (
" + BuildSchemaCurrencySql() + @"
),
PeriodRange AS (
" + BuildPeriodRangeSql() + @"
),
ProtectedJournal AS (
" + BuildProtectedJournalSql(ctx, "GL_Journal.PostingType = 'A'") + @"
)
SELECT COUNT(ProtectedJournal.GL_Journal_ID) AS EntryCount,
       MAX(PeriodRange.MonthDateFrom) AS MonthDateFrom
FROM PeriodRange PeriodRange
INNER JOIN SchemaCurrency SchemaCurrency
    ON (SchemaCurrency.AD_Client_ID = PeriodRange.AD_Client_ID)
LEFT OUTER JOIN ProtectedJournal ProtectedJournal
    ON (ProtectedJournal.AD_Client_ID = SchemaCurrency.AD_Client_ID
    AND ProtectedJournal.C_AcctSchema_ID = SchemaCurrency.C_AcctSchema_ID
    AND ProtectedJournal.DateAcct >= PeriodRange.MonthDateFrom
    AND ProtectedJournal.DateAcct < " + GetDateToExclusiveExpression("PeriodRange.MonthDateTo") + @")";

            SqlParameter[] parameters =
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
            };

            int entryCount = 0;
            DateTime? monthDateFrom = null;

            DataSet ds = DB.ExecuteDataset(sql, parameters, null);

            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                DataRow row = ds.Tables[0].Rows[0];
                entryCount = Util.GetValueOfInt(row["EntryCount"]);
                monthDateFrom = Util.GetValueOfDateTime(row["MonthDateFrom"]);
            }

            DateTime displayDate = monthDateFrom.HasValue ? monthDateFrom.Value : DateTime.Now;

            return Json(JsonConvert.SerializeObject(new
            {
                EntryCount = entryCount,
                MonthName = displayDate.ToString("MMMM"),
                MonthAbbr = displayDate.ToString("MMM"),
                Year = displayDate.Year
            }), JsonRequestBehavior.AllowGet);
        }

        public JsonResult GetMonthlyEntries()
        {
            if (Session["ctx"] == null)
            {
                return Json("", JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            string sql = @"
WITH SchemaCurrency AS (
" + BuildSchemaCurrencySql() + @"
),
PeriodRange AS (
" + BuildPeriodRangeSql() + @"
),
ProtectedJournal AS (
" + BuildProtectedJournalSql(ctx, "GL_Journal.PostingType = 'A'") + @"
),
JournalData AS (
    SELECT ProtectedJournal.GL_Journal_ID,
           ProtectedJournal.DocumentNo,
           ProtectedJournal.DateAcct,
           ProtectedJournal.Description,
           ProtectedJournal.DocStatus,
           ProtectedJournal.AD_Client_ID,
           ProtectedJournal.C_AcctSchema_ID
    FROM PeriodRange PeriodRange
    INNER JOIN SchemaCurrency SchemaCurrency
        ON (SchemaCurrency.AD_Client_ID = PeriodRange.AD_Client_ID)
    INNER JOIN ProtectedJournal ProtectedJournal
        ON (ProtectedJournal.AD_Client_ID = SchemaCurrency.AD_Client_ID
        AND ProtectedJournal.C_AcctSchema_ID = SchemaCurrency.C_AcctSchema_ID
        AND ProtectedJournal.DateAcct >= PeriodRange.MonthDateFrom
        AND ProtectedJournal.DateAcct < " + GetDateToExclusiveExpression("PeriodRange.MonthDateTo") + @")
),
JournalTotals AS (
    SELECT JournalData.GL_Journal_ID,
           JournalData.DocumentNo,
           JournalData.DateAcct,
           JournalData.Description,
           JournalData.DocStatus,
           AD_Ref_List.Name AS DocStatusName,
           ROUND(COALESCE(SUM(COALESCE(GL_JournalLine.AmtAcctDr, 0)), 0), COALESCE(MAX(SchemaCurrency.StdPrecision), 2)) AS TotalDebit,
           ROUND(COALESCE(SUM(COALESCE(GL_JournalLine.AmtAcctCr, 0)), 0), COALESCE(MAX(SchemaCurrency.StdPrecision), 2)) AS TotalCredit,
           MAX(SchemaCurrency.Cur_Symbol) AS CurSymbol,
           MAX(SchemaCurrency.ISO_Code) AS ISOCode,
           COALESCE(MAX(SchemaCurrency.StdPrecision), 2) AS StdPrecision
    FROM JournalData JournalData
    INNER JOIN SchemaCurrency SchemaCurrency
        ON (SchemaCurrency.AD_Client_ID = JournalData.AD_Client_ID
        AND SchemaCurrency.C_AcctSchema_ID = JournalData.C_AcctSchema_ID)
    LEFT OUTER JOIN GL_JournalLine GL_JournalLine
        ON (JournalData.GL_Journal_ID = GL_JournalLine.GL_Journal_ID
        AND GL_JournalLine.IsActive = 'Y')
    LEFT OUTER JOIN AD_Ref_List AD_Ref_List
        ON (AD_Ref_List.AD_Reference_ID = 131
        AND AD_Ref_List.Value = JournalData.DocStatus
        AND AD_Ref_List.IsActive = 'Y')
    GROUP BY JournalData.GL_Journal_ID,
             JournalData.DocumentNo,
             JournalData.DateAcct,
             JournalData.Description,
             JournalData.DocStatus,
             AD_Ref_List.Name
)
SELECT JournalTotals.GL_Journal_ID,
       JournalTotals.DocumentNo,
       JournalTotals.DateAcct,
       JournalTotals.Description,
       JournalTotals.DocStatus,
       JournalTotals.DocStatusName,
       JournalTotals.TotalDebit,
       JournalTotals.TotalCredit,
       JournalTotals.CurSymbol,
       JournalTotals.ISOCode,
       JournalTotals.StdPrecision
FROM JournalTotals JournalTotals
ORDER BY JournalTotals.DateAcct DESC,
         JournalTotals.GL_Journal_ID DESC
FETCH FIRST 100 ROWS ONLY";

            SqlParameter[] parameters =
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
            };

            DataSet ds = DB.ExecuteDataset(sql, parameters, null);
            List<object> entries = new List<object>();
            decimal totalDebit = 0m;
            decimal totalCredit = 0m;
            string curSymbol = "";
            string isoCode = "";
            int stdPrecision = 2;
            DateTime? monthDate = null;

            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                foreach (DataRow row in ds.Tables[0].Rows)
                {
                    stdPrecision = Util.GetValueOfInt(row["StdPrecision"]);

                    if (stdPrecision < 0)
                    {
                        stdPrecision = 2;
                    }

                    decimal debit = Util.GetValueOfDecimal(row["TotalDebit"]);
                    decimal credit = Util.GetValueOfDecimal(row["TotalCredit"]);

                    totalDebit += debit;
                    totalCredit += credit;

                    string statusName = Util.GetValueOfString(row["DocStatusName"]);
                    string docStatus = Util.GetValueOfString(row["DocStatus"]);

                    if (string.IsNullOrEmpty(statusName))
                    {
                        statusName = docStatus;
                    }

                    string dateAcctText = "";
                    DateTime? dateAcct = Util.GetValueOfDateTime(row["DateAcct"]);

                    if (dateAcct.HasValue)
                    {
                        dateAcctText = dateAcct.Value.ToString("dd MMM yyyy");

                        if (!monthDate.HasValue)
                        {
                            monthDate = dateAcct.Value;
                        }
                    }

                    curSymbol = Util.GetValueOfString(row["CurSymbol"]);
                    isoCode = Util.GetValueOfString(row["ISOCode"]);

                    entries.Add(new
                    {
                        GL_Journal_ID = Util.GetValueOfInt(row["GL_Journal_ID"]),
                        DocumentNo = Util.GetValueOfString(row["DocumentNo"]),
                        DateAcct = dateAcctText,
                        Description = Util.GetValueOfString(row["Description"]),
                        DocStatus = docStatus,
                        StatusName = statusName,
                        TotalDebit = debit,
                        TotalCredit = credit
                    });
                }
            }

            DateTime displayDate = monthDate.HasValue ? monthDate.Value : DateTime.Now;

            return Json(JsonConvert.SerializeObject(new
            {
                Entries = entries,
                TotalCount = entries.Count,
                TotalDebit = Decimal.Round(totalDebit, stdPrecision, MidpointRounding.AwayFromZero),
                TotalCredit = Decimal.Round(totalCredit, stdPrecision, MidpointRounding.AwayFromZero),
                MonthName = displayDate.ToString("MMMM"),
                MonthAbbr = displayDate.ToString("MMM"),
                Year = displayDate.Year,
                CurSymbol = curSymbol,
                ISOCode = isoCode,
                StdPrecision = stdPrecision
            }), JsonRequestBehavior.AllowGet);
        }

        public JsonResult GetJournalEntryDetail(int journalId)
        {
            if (Session["ctx"] == null)
            {
                return Json("", JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            string headerSql = @"
WITH ProtectedJournal AS (
" + BuildProtectedJournalSql(ctx, "GL_Journal.GL_Journal_ID = @JournalID") + @"
),
SchemaCurrency AS (
    SELECT AcctSchema.AD_Client_ID,
           AcctSchema.C_AcctSchema_ID,
           AcctSchema.Name AS AcctSchemaName,
           Currency.StdPrecision,
           Currency.ISO_Code AS ISO_Code,
           CASE
               WHEN Currency.CurSymbol IS NOT NULL THEN Currency.CurSymbol
               ELSE Currency.ISO_Code
           END AS Cur_Symbol
    FROM C_AcctSchema AcctSchema
    INNER JOIN C_Currency Currency
        ON (AcctSchema.C_Currency_ID = Currency.C_Currency_ID)
    WHERE AcctSchema.IsActive = 'Y'
    AND AcctSchema.AD_Client_ID = @AD_Client_ID
)
SELECT ProtectedJournal.GL_Journal_ID,
       ProtectedJournal.DocumentNo,
       ProtectedJournal.DateAcct,
       ProtectedJournal.Description,
       ProtectedJournal.DocStatus,
       ProtectedJournal.Created,
       AD_Ref_List.Name AS DocStatusName,
       AD_User.Name AS CreatedByName,
       ROUND(COALESCE(SUM(COALESCE(GL_JournalLine.AmtAcctDr, 0)), 0), COALESCE(MAX(SchemaCurrency.StdPrecision), 2)) AS TotalDebit,
       ROUND(COALESCE(SUM(COALESCE(GL_JournalLine.AmtAcctCr, 0)), 0), COALESCE(MAX(SchemaCurrency.StdPrecision), 2)) AS TotalCredit,
       MAX(SchemaCurrency.Cur_Symbol) AS CurSymbol,
       MAX(SchemaCurrency.ISO_Code) AS ISOCode,
       COALESCE(MAX(SchemaCurrency.StdPrecision), 2) AS StdPrecision,
       MAX(SchemaCurrency.AcctSchemaName) AS AccountingBook
FROM ProtectedJournal ProtectedJournal
INNER JOIN SchemaCurrency SchemaCurrency
    ON (SchemaCurrency.AD_Client_ID = ProtectedJournal.AD_Client_ID
    AND SchemaCurrency.C_AcctSchema_ID = ProtectedJournal.C_AcctSchema_ID)
LEFT OUTER JOIN GL_JournalLine GL_JournalLine
    ON (ProtectedJournal.GL_Journal_ID = GL_JournalLine.GL_Journal_ID
    AND GL_JournalLine.IsActive = 'Y')
LEFT OUTER JOIN AD_Ref_List AD_Ref_List
    ON (AD_Ref_List.AD_Reference_ID = 131
    AND AD_Ref_List.Value = ProtectedJournal.DocStatus
    AND AD_Ref_List.IsActive = 'Y')
LEFT OUTER JOIN AD_User AD_User
    ON (ProtectedJournal.CreatedBy = AD_User.AD_User_ID)
GROUP BY ProtectedJournal.GL_Journal_ID,
         ProtectedJournal.DocumentNo,
         ProtectedJournal.DateAcct,
         ProtectedJournal.Description,
         ProtectedJournal.DocStatus,
         ProtectedJournal.Created,
         AD_Ref_List.Name,
         AD_User.Name";

            SqlParameter[] parameters =
            {
        new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
        new SqlParameter("@JournalID", journalId)
    };

            DataSet headerDs = DB.ExecuteDataset(headerSql, parameters, null);

            if (headerDs == null || headerDs.Tables.Count == 0 || headerDs.Tables[0].Rows.Count == 0)
            {
                return Json(JsonConvert.SerializeObject(new
                {
                    error = true,
                    errorText = "Journal details not found"
                }), JsonRequestBehavior.AllowGet);
            }

            DataRow headerRow = headerDs.Tables[0].Rows[0];

            int stdPrecision = Util.GetValueOfInt(headerRow["StdPrecision"]);

            if (stdPrecision < 0)
            {
                stdPrecision = 2;
            }

            string linesSql = @"
WITH ProtectedJournal AS (
" + BuildProtectedJournalSql(ctx, "GL_Journal.GL_Journal_ID = @JournalID") + @"
)
SELECT GL_JournalLine.GL_JournalLine_ID,
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
INNER JOIN GL_JournalLine GL_JournalLine
    ON (ProtectedJournal.GL_Journal_ID = GL_JournalLine.GL_Journal_ID)
LEFT OUTER JOIN C_ValidCombination ValidCombination
    ON (GL_JournalLine.C_ValidCombination_ID = ValidCombination.C_ValidCombination_ID)
LEFT OUTER JOIN C_ElementValue AccountValue
    ON (ValidCombination.Account_ID = AccountValue.C_ElementValue_ID)
LEFT OUTER JOIN C_ElementValue CostCenterValue
    ON (ValidCombination.User1_ID = CostCenterValue.C_ElementValue_ID)
LEFT OUTER JOIN C_BPartner BPartner
    ON (ValidCombination.C_BPartner_ID = BPartner.C_BPartner_ID)
LEFT OUTER JOIN M_Product Product
    ON (ValidCombination.M_Product_ID = Product.M_Product_ID)
LEFT OUTER JOIN C_Project Project
    ON (ValidCombination.C_Project_ID = Project.C_Project_ID)
WHERE GL_JournalLine.IsActive = 'Y'
ORDER BY GL_JournalLine.Line,
         GL_JournalLine.GL_JournalLine_ID";

            DataSet lineDs = DB.ExecuteDataset(linesSql, parameters, null);
            List<object> lines = new List<object>();

            if (lineDs != null && lineDs.Tables.Count > 0 && lineDs.Tables[0].Rows.Count > 0)
            {
                foreach (DataRow row in lineDs.Tables[0].Rows)
                {
                    string accountCode = Util.GetValueOfString(row["AccountCode"]);
                    string accountName = Util.GetValueOfString(row["AccountName"]);

                    if (string.IsNullOrEmpty(accountCode))
                    {
                        accountCode = Util.GetValueOfString(row["Account_ID"]);
                    }

                    if (string.IsNullOrEmpty(accountCode))
                    {
                        accountCode = Util.GetValueOfString(row["C_ValidCombination_ID"]);
                    }

                    if (string.IsNullOrEmpty(accountName))
                    {
                        accountName = "-";
                    }

                    string costCenter = "-";
                    string costCenterCode = Util.GetValueOfString(row["CostCenterCode"]);
                    string costCenterName = Util.GetValueOfString(row["CostCenterName"]);

                    if (!string.IsNullOrEmpty(costCenterCode) && !string.IsNullOrEmpty(costCenterName))
                    {
                        costCenter = costCenterCode + " · " + costCenterName;
                    }
                    else if (!string.IsNullOrEmpty(costCenterCode))
                    {
                        costCenter = costCenterCode;
                    }
                    else if (!string.IsNullOrEmpty(costCenterName))
                    {
                        costCenter = costCenterName;
                    }

                    string bPartner = Util.GetValueOfString(row["BPartnerName"]);
                    string product = Util.GetValueOfString(row["ProductName"]);
                    string project = Util.GetValueOfString(row["ProjectName"]);

                    lines.Add(new
                    {
                        AccountCode = accountCode,
                        AccountName = accountName,
                        Debit = Decimal.Round(
                            Util.GetValueOfDecimal(row["AmtAcctDr"]),
                            stdPrecision,
                            MidpointRounding.AwayFromZero
                        ),
                        Credit = Decimal.Round(
                            Util.GetValueOfDecimal(row["AmtAcctCr"]),
                            stdPrecision,
                            MidpointRounding.AwayFromZero
                        ),
                        CostCenter = string.IsNullOrEmpty(costCenter) ? "-" : costCenter,
                        BPartner = string.IsNullOrEmpty(bPartner) ? "-" : bPartner,
                        Product = string.IsNullOrEmpty(product) ? "-" : product,
                        Project = string.IsNullOrEmpty(project) ? "-" : project
                    });
                }
            }

            string docStatus = Util.GetValueOfString(headerRow["DocStatus"]);
            string statusName = Util.GetValueOfString(headerRow["DocStatusName"]);

            if (string.IsNullOrEmpty(statusName))
            {
                statusName = docStatus;
            }

            DateTime? dateAcct = Util.GetValueOfDateTime(headerRow["DateAcct"]);
            DateTime? created = Util.GetValueOfDateTime(headerRow["Created"]);

            return Json(JsonConvert.SerializeObject(new
            {
                Journal = new
                {
                    GL_Journal_ID = Util.GetValueOfInt(headerRow["GL_Journal_ID"]),
                    DocumentNo = Util.GetValueOfString(headerRow["DocumentNo"]),
                    DateAcct = dateAcct.HasValue ? dateAcct.Value.ToString("dd MMM yyyy") : "",
                    Description = Util.GetValueOfString(headerRow["Description"]),
                    DocStatus = docStatus,
                    StatusName = statusName,
                    TotalDebit = Decimal.Round(
                        Util.GetValueOfDecimal(headerRow["TotalDebit"]),
                        stdPrecision,
                        MidpointRounding.AwayFromZero
                    ),
                    TotalCredit = Decimal.Round(
                        Util.GetValueOfDecimal(headerRow["TotalCredit"]),
                        stdPrecision,
                        MidpointRounding.AwayFromZero
                    ),
                    AccountingBook = string.IsNullOrEmpty(Util.GetValueOfString(headerRow["AccountingBook"]))
                        ? "Primary"
                        : Util.GetValueOfString(headerRow["AccountingBook"]),
                    CreatedByName = Util.GetValueOfString(headerRow["CreatedByName"]),
                    CreatedDate = created.HasValue ? created.Value.ToString("dd MMM yyyy") : ""
                },
                Lines = lines,
                LineCount = lines.Count,
                CurSymbol = Util.GetValueOfString(headerRow["CurSymbol"]),
                ISOCode = Util.GetValueOfString(headerRow["ISOCode"]),
                StdPrecision = stdPrecision
            }), JsonRequestBehavior.AllowGet);
        }

        public JsonResult GetUnpostedCount()
        {
            if (Session["ctx"] == null)
            {
                return Json("", JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            string sql = @"
WITH ProtectedJournal AS (
" + BuildProtectedJournalSql(ctx, "GL_Journal.Posted = 'N' AND GL_Journal.DocStatus NOT IN ('VO')") + @"
)
SELECT COUNT(ProtectedJournal.GL_Journal_ID) AS UnpostedCount
FROM ProtectedJournal ProtectedJournal";

            SqlParameter[] parameters =
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
            };

            DataSet ds = DB.ExecuteDataset(sql, parameters, null);
            int unpostedCount = 0;

            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                unpostedCount = Util.GetValueOfInt(ds.Tables[0].Rows[0]["UnpostedCount"]);
            }

            return Json(JsonConvert.SerializeObject(new { UnpostedCount = unpostedCount }), JsonRequestBehavior.AllowGet);
        }

        public JsonResult GetUnpostedEntries()
        {
            if (Session["ctx"] == null)
            {
                return Json("", JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            string sql = @"
WITH SchemaCurrency AS (
" + BuildSchemaCurrencySql() + @"
),
ProtectedJournal AS (
" + BuildProtectedJournalSql(ctx, "GL_Journal.Posted = 'N' AND GL_Journal.DocStatus NOT IN ('VO')") + @"
),
JournalTotals AS (
    SELECT ProtectedJournal.GL_Journal_ID,
           ProtectedJournal.DocumentNo,
           ProtectedJournal.DateAcct,
           ProtectedJournal.Description,
           ProtectedJournal.DocStatus,
           AD_Ref_List.Name AS DocStatusName,
           ROUND(COALESCE(SUM(COALESCE(GL_JournalLine.AmtAcctDr, 0)), 0), COALESCE(MAX(SchemaCurrency.StdPrecision), 2)) AS TotalDebit,
           ROUND(COALESCE(SUM(COALESCE(GL_JournalLine.AmtAcctCr, 0)), 0), COALESCE(MAX(SchemaCurrency.StdPrecision), 2)) AS TotalCredit,
           MAX(SchemaCurrency.Cur_Symbol) AS CurSymbol,
           MAX(SchemaCurrency.ISO_Code) AS ISOCode,
           COALESCE(MAX(SchemaCurrency.StdPrecision), 2) AS StdPrecision
    FROM ProtectedJournal ProtectedJournal
    INNER JOIN SchemaCurrency SchemaCurrency
        ON (SchemaCurrency.AD_Client_ID = ProtectedJournal.AD_Client_ID
        AND SchemaCurrency.C_AcctSchema_ID = ProtectedJournal.C_AcctSchema_ID)
    LEFT OUTER JOIN GL_JournalLine GL_JournalLine
        ON (ProtectedJournal.GL_Journal_ID = GL_JournalLine.GL_Journal_ID
        AND GL_JournalLine.IsActive = 'Y')
    LEFT OUTER JOIN AD_Ref_List AD_Ref_List
        ON (AD_Ref_List.AD_Reference_ID = 131
        AND AD_Ref_List.Value = ProtectedJournal.DocStatus
        AND AD_Ref_List.IsActive = 'Y')
    GROUP BY ProtectedJournal.GL_Journal_ID,
             ProtectedJournal.DocumentNo,
             ProtectedJournal.DateAcct,
             ProtectedJournal.Description,
             ProtectedJournal.DocStatus,
             AD_Ref_List.Name
)
SELECT JournalTotals.GL_Journal_ID,
       JournalTotals.DocumentNo,
       JournalTotals.DateAcct,
       JournalTotals.Description,
       JournalTotals.DocStatus,
       JournalTotals.DocStatusName,
       JournalTotals.TotalDebit,
       JournalTotals.TotalCredit,
       JournalTotals.CurSymbol,
       JournalTotals.ISOCode,
       JournalTotals.StdPrecision
FROM JournalTotals JournalTotals
ORDER BY JournalTotals.DateAcct DESC,
         JournalTotals.GL_Journal_ID DESC
FETCH FIRST 100 ROWS ONLY";

            SqlParameter[] parameters =
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
            };

            DataSet ds = DB.ExecuteDataset(sql, parameters, null);
            List<object> entries = new List<object>();
            decimal totalDebit = 0m;
            decimal totalCredit = 0m;
            string curSymbol = "";
            string isoCode = "";
            int stdPrecision = 2;

            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                foreach (DataRow row in ds.Tables[0].Rows)
                {
                    stdPrecision = Util.GetValueOfInt(row["StdPrecision"]);

                    if (stdPrecision < 0)
                    {
                        stdPrecision = 2;
                    }

                    decimal debit = Util.GetValueOfDecimal(row["TotalDebit"]);
                    decimal credit = Util.GetValueOfDecimal(row["TotalCredit"]);

                    totalDebit += debit;
                    totalCredit += credit;

                    string statusName = Util.GetValueOfString(row["DocStatusName"]);
                    string docStatus = Util.GetValueOfString(row["DocStatus"]);

                    if (string.IsNullOrEmpty(statusName))
                    {
                        statusName = docStatus;
                    }

                    DateTime? dateAcct = Util.GetValueOfDateTime(row["DateAcct"]);

                    curSymbol = Util.GetValueOfString(row["CurSymbol"]);
                    isoCode = Util.GetValueOfString(row["ISOCode"]);

                    entries.Add(new
                    {
                        GL_Journal_ID = Util.GetValueOfInt(row["GL_Journal_ID"]),
                        DocumentNo = Util.GetValueOfString(row["DocumentNo"]),
                        DateAcct = dateAcct.HasValue ? dateAcct.Value.ToString("dd MMM yyyy") : "",
                        Description = Util.GetValueOfString(row["Description"]),
                        DocStatus = docStatus,
                        StatusName = statusName,
                        TotalDebit = debit,
                        TotalCredit = credit
                    });
                }
            }

            return Json(JsonConvert.SerializeObject(new
            {
                Entries = entries,
                TotalCount = entries.Count,
                TotalDebit = Decimal.Round(totalDebit, stdPrecision, MidpointRounding.AwayFromZero),
                TotalCredit = Decimal.Round(totalCredit, stdPrecision, MidpointRounding.AwayFromZero),
                CurSymbol = curSymbol,
                ISOCode = isoCode,
                StdPrecision = stdPrecision
            }), JsonRequestBehavior.AllowGet);
        }

        public JsonResult GetPostedPercentage()
        {
            if (Session["ctx"] == null)
            {
                return Json("", JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            string sql = @"
WITH ProtectedJournal AS (
" + BuildProtectedJournalSql(ctx, "1 = 1") + @"
)
SELECT SUM(CASE WHEN ProtectedJournal.Posted = 'Y' THEN 1 ELSE 0 END) AS PostedCount,
       COUNT(1) AS TotalCount
FROM ProtectedJournal ProtectedJournal";

            SqlParameter[] parameters =
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
            };

            DataSet ds = DB.ExecuteDataset(sql, parameters, null);
            int postedCount = 0;
            int totalCount = 0;

            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                postedCount = Util.GetValueOfInt(ds.Tables[0].Rows[0]["PostedCount"]);
                totalCount = Util.GetValueOfInt(ds.Tables[0].Rows[0]["TotalCount"]);
            }

            int percentage = totalCount > 0
                ? (int)Math.Round((double)postedCount / totalCount * 100)
                : 0;

            return Json(JsonConvert.SerializeObject(new
            {
                PostedCount = postedCount,
                TotalCount = totalCount,
                Percentage = percentage
            }), JsonRequestBehavior.AllowGet);
        }

        private string BuildSchemaCurrencySql()
        {
            return @"
SELECT ClientInfo.AD_Client_ID,
       AcctSchema.C_AcctSchema_ID,
       AcctSchema.Name AS AcctSchemaName,
       AcctSchema.C_Currency_ID AS C_Currency_ID,
       Currency.StdPrecision,
       Currency.ISO_Code AS ISO_Code,
       CASE
           WHEN Currency.CurSymbol IS NOT NULL THEN Currency.CurSymbol
           ELSE Currency.ISO_Code
       END AS Cur_Symbol
FROM AD_ClientInfo ClientInfo
INNER JOIN C_AcctSchema AcctSchema
    ON (ClientInfo.C_AcctSchema1_ID = AcctSchema.C_AcctSchema_ID)
INNER JOIN C_Currency Currency
    ON (AcctSchema.C_Currency_ID = Currency.C_Currency_ID)
WHERE ClientInfo.IsActive = 'Y'
AND AcctSchema.IsActive = 'Y'
AND ClientInfo.AD_Client_ID = @AD_Client_ID";
        }

        private string BuildPeriodRangeSql()
        {
            return @"
SELECT ClientInfo.AD_Client_ID,
       CurrentPeriod.StartDate AS MonthDateFrom,
       CurrentPeriod.EndDate AS MonthDateTo,
       (SELECT MIN(PeriodYTD.StartDate) FROM C_Period PeriodYTD WHERE PeriodYTD.C_Year_ID = CurrentPeriod.C_Year_ID AND PeriodYTD.IsActive = 'Y') AS YTDDateFrom,
       CurrentPeriod.EndDate AS YTDDateTo
FROM AD_ClientInfo ClientInfo
INNER JOIN C_Year YearData
    ON (YearData.C_Calendar_ID = ClientInfo.C_Calendar_ID)
INNER JOIN C_Period CurrentPeriod
    ON (CurrentPeriod.C_Year_ID = YearData.C_Year_ID)
WHERE ClientInfo.IsActive = 'Y'
AND CurrentPeriod.IsActive = 'Y'
AND ClientInfo.AD_Client_ID = @AD_Client_ID
AND CURRENT_DATE >= CurrentPeriod.StartDate
AND CURRENT_DATE < " + GetDateToExclusiveExpression("CurrentPeriod.EndDate");
        }

        private string BuildProtectedJournalSql(Ctx ctx, string extraWhere)
        {
            string sql = @"
SELECT GL_Journal.GL_Journal_ID,
       GL_Journal.AD_Client_ID,
       GL_Journal.AD_Org_ID,
       GL_Journal.C_AcctSchema_ID,
       GL_Journal.DocumentNo,
       GL_Journal.DateAcct,
       GL_Journal.Description,
       GL_Journal.DocStatus,
       GL_Journal.Posted,
       GL_Journal.Created,
       GL_Journal.CreatedBy
FROM GL_Journal GL_Journal
WHERE GL_Journal.IsActive = 'Y'
AND GL_Journal.AD_Client_ID = @AD_Client_ID
AND " + extraWhere;

            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql,
                "GL_Journal",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            return sql;
        }

        private string GetDateToExclusiveExpression(string dateColumn)
        {
            if (DB.IsOracle())
            {
                return dateColumn + " + 1";
            }

            return dateColumn + " + INTERVAL '1 day'";
        }
    }
}