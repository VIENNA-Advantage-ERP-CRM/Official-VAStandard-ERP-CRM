using CoreLibrary.DataBase;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Web.Mvc;
using VAdvantage.Model;
using VAdvantage.Utility;

namespace VAS.Controllers
{
    /// <summary>
    /// Controller for GL Journal amount KPI widgets.
    ///
    /// Messages:
    /// VAS_037_InvalidColumn
    /// VAS_037_InvalidAlias
    /// VAS_037_SessionExpired
    /// VAS_037_ErrorLoadingData
    /// VAS_037_PeriodNotFound
    /// VAS_037_TotalDebitTitle
    /// VAS_037_TotalCreditTitle
    /// VAS_037_NetDifferenceTitle
    /// VAS_037_MonthBadge
    /// VAS_037_YTDBadge
    /// VAS_037_JournalCountDescription
    /// </summary>
    public class VAS_037_GLJournalTotalDebitWidgetController : Controller
    {
        public JsonResult GetTotalDebit(string period)
        {
            if (Session["ctx"] == null)
            {
                return Json(SessionExpired(), JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            return Json(
                GetJournalTotal(ctx, "AmtAcctDr", "TotalDebit", period),
                JsonRequestBehavior.AllowGet
            );
        }

        public JsonResult GetTotalCredit(string period)
        {
            if (Session["ctx"] == null)
            {
                return Json(SessionExpired(), JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            return Json(
                GetJournalTotal(ctx, "AmtAcctCr", "TotalCredit", period),
                JsonRequestBehavior.AllowGet
            );
        }

        public JsonResult GetNetDifference(string period)
        {
            if (Session["ctx"] == null)
            {
                return Json(SessionExpired(), JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            return Json(
                GetNetDifferenceData(ctx, period),
                JsonRequestBehavior.AllowGet
            );
        }

        private object GetJournalTotal(Ctx ctx, string amountColumn, string resultAlias, string period)
        {
            if (amountColumn != "AmtAcctDr" && amountColumn != "AmtAcctCr")
            {
                return Error(GetMsg(ctx, "VAS_037_InvalidColumn", "Invalid Column"));
            }

            if (resultAlias != "TotalDebit" && resultAlias != "TotalCredit")
            {
                return Error(GetMsg(ctx, "VAS_037_InvalidAlias", "Invalid Alias"));
            }

            try
            {
                bool isYTD = IsYTD(period);

                string titleKey = resultAlias == "TotalDebit"
                    ? "VAS_037_TotalDebitTitle"
                    : "VAS_037_TotalCreditTitle";

                string titleFallback = resultAlias == "TotalDebit"
                    ? "Total Debit"
                    : "Total Credit";

                string schemaCurrencySql = BuildSchemaCurrencySql();
                string periodRangeSql = BuildPeriodRangeSql();
                string protectedJournalSql = BuildProtectedJournalSql(ctx);
                string journalDataSql = BuildJournalDataSql();

                string dateFromColumn = isYTD
                    ? "PeriodRange.YTDDateFrom"
                    : "PeriodRange.MonthDateFrom";

                string dateToColumn = isYTD
                    ? "PeriodRange.YTDDateTo"
                    : "PeriodRange.MonthDateTo";

                string dateToExclusiveExpression = GetDateToExclusiveExpression(dateToColumn);

                string sql = @"
WITH SchemaCurrency AS (
" + schemaCurrencySql + @"
),
PeriodRange AS (
" + periodRangeSql + @"
),
ProtectedJournal AS (
" + protectedJournalSql + @"
),
JournalData AS (
" + journalDataSql + @"
)
SELECT ROUND(
           COALESCE(SUM(COALESCE(JournalData." + amountColumn + @", 0)), 0),
           COALESCE(MAX(SchemaCurrency.StdPrecision), 2)
       ) AS " + resultAlias + @",
       COUNT(DISTINCT JournalData.GL_Journal_ID) AS JournalCount,
       MAX(SchemaCurrency.Cur_Symbol) AS CurSymbol,
       MAX(SchemaCurrency.ISO_Code) AS ISOCode,
       COALESCE(MAX(SchemaCurrency.StdPrecision), 2) AS StdPrecision,
       MAX(" + dateFromColumn + @") AS DateFrom,
       MAX(" + dateToColumn + @") AS DateTo
FROM PeriodRange PeriodRange
INNER JOIN SchemaCurrency SchemaCurrency
    ON (SchemaCurrency.AD_Client_ID = PeriodRange.AD_Client_ID)
LEFT OUTER JOIN JournalData JournalData
    ON (JournalData.AD_Client_ID = SchemaCurrency.AD_Client_ID
    AND JournalData.C_AcctSchema_ID = SchemaCurrency.C_AcctSchema_ID
    AND JournalData.DateAcct >= " + dateFromColumn + @"
    AND JournalData.DateAcct < " + dateToExclusiveExpression + @")";

                List<SqlParameter> parameters = new List<SqlParameter>
                {
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
                };

                decimal total = 0;
                int journalCount = 0;
                string curSymbol = "";
                string isoCode = "";
                int stdPrecision = 2;
                DateTime? dateFromValue = null;
                DateTime? dateToValue = null;

                using (IDataReader dr = DB.ExecuteReader(sql, parameters.ToArray(), null))
                {
                    if (dr != null && dr.Read())
                    {
                        total = Util.GetValueOfDecimal(dr[resultAlias]);
                        journalCount = Util.GetValueOfInt(dr["JournalCount"]);
                        curSymbol = Util.GetValueOfString(dr["CurSymbol"]);
                        isoCode = Util.GetValueOfString(dr["ISOCode"]);
                        stdPrecision = Util.GetValueOfInt(dr["StdPrecision"]);
                        dateFromValue = Util.GetValueOfDateTime(dr["DateFrom"]);
                        dateToValue = Util.GetValueOfDateTime(dr["DateTo"]);
                    }
                }

                if (!dateFromValue.HasValue || !dateToValue.HasValue)
                {
                    return Error(GetMsg(ctx, "VAS_037_PeriodNotFound", "Current period was not found"));
                }

                if (stdPrecision < 0)
                {
                    stdPrecision = 2;
                }

                bool hasData = journalCount > 0;

                string description = string.Format(
                    GetMsg(ctx, "VAS_037_JournalCountDescription", "{0} journal(s)"),
                    journalCount
                );

                return new
                {
                    success = true,
                    error = "",
                    title = GetMsg(ctx, titleKey, titleFallback),
                    mainMetric = total,
                    mainMetricText = total.ToString(),
                    description = description,
                    badgeText = GetPeriodBadgeText(ctx, isYTD),
                    dateFrom = FormatDate(dateFromValue.Value),
                    dateTo = FormatDate(dateToValue.Value),
                    currencyISO = isoCode,
                    currencySymbol = curSymbol,
                    stdPrecision = stdPrecision,
                    hasData = hasData,

                    Total = total,
                    JournalCount = journalCount,
                    CurSymbol = curSymbol,
                    ISOCode = isoCode,
                    StdPrecision = stdPrecision,
                    MonthAbbr = DateTime.Now.ToString("MMM")
                };
            }
            catch (Exception ex)
            {
                Trace.TraceError(ex.ToString());
                return Error(GetMsg(ctx, "VAS_037_ErrorLoadingData", "Could not load data"));
            }
        }

        private object GetNetDifferenceData(Ctx ctx, string period)
        {
            try
            {
                bool isYTD = IsYTD(period);

                string schemaCurrencySql = BuildSchemaCurrencySql();
                string periodRangeSql = BuildPeriodRangeSql();
                string protectedJournalSql = BuildProtectedJournalSql(ctx);
                string journalDataSql = BuildJournalDataSql();

                string dateFromColumn = isYTD
                    ? "PeriodRange.YTDDateFrom"
                    : "PeriodRange.MonthDateFrom";

                string dateToColumn = isYTD
                    ? "PeriodRange.YTDDateTo"
                    : "PeriodRange.MonthDateTo";

                string dateToExclusiveExpression = GetDateToExclusiveExpression(dateToColumn);

                string sql = @"
WITH SchemaCurrency AS (
" + schemaCurrencySql + @"
),
PeriodRange AS (
" + periodRangeSql + @"
),
ProtectedJournal AS (
" + protectedJournalSql + @"
),
JournalData AS (
" + journalDataSql + @"
)
SELECT ROUND(
           COALESCE(SUM(COALESCE(JournalData.AmtAcctDr, 0) - COALESCE(JournalData.AmtAcctCr, 0)), 0),
           COALESCE(MAX(SchemaCurrency.StdPrecision), 2)
       ) AS NetDiff,
       COUNT(DISTINCT JournalData.GL_Journal_ID) AS JournalCount,
       MAX(SchemaCurrency.Cur_Symbol) AS CurSymbol,
       MAX(SchemaCurrency.ISO_Code) AS ISOCode,
       COALESCE(MAX(SchemaCurrency.StdPrecision), 2) AS StdPrecision,
       MAX(" + dateFromColumn + @") AS DateFrom,
       MAX(" + dateToColumn + @") AS DateTo
FROM PeriodRange PeriodRange
INNER JOIN SchemaCurrency SchemaCurrency
    ON (SchemaCurrency.AD_Client_ID = PeriodRange.AD_Client_ID)
LEFT OUTER JOIN JournalData JournalData
    ON (JournalData.AD_Client_ID = SchemaCurrency.AD_Client_ID
    AND JournalData.C_AcctSchema_ID = SchemaCurrency.C_AcctSchema_ID
    AND JournalData.DateAcct >= " + dateFromColumn + @"
    AND JournalData.DateAcct < " + dateToExclusiveExpression + @")";

                List<SqlParameter> parameters = new List<SqlParameter>
                {
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
                };

                decimal netDiff = 0;
                int journalCount = 0;
                string curSymbol = "";
                string isoCode = "";
                int stdPrecision = 2;
                DateTime? dateFromValue = null;
                DateTime? dateToValue = null;

                using (IDataReader dr = DB.ExecuteReader(sql, parameters.ToArray(), null))
                {
                    if (dr != null && dr.Read())
                    {
                        netDiff = Util.GetValueOfDecimal(dr["NetDiff"]);
                        journalCount = Util.GetValueOfInt(dr["JournalCount"]);
                        curSymbol = Util.GetValueOfString(dr["CurSymbol"]);
                        isoCode = Util.GetValueOfString(dr["ISOCode"]);
                        stdPrecision = Util.GetValueOfInt(dr["StdPrecision"]);
                        dateFromValue = Util.GetValueOfDateTime(dr["DateFrom"]);
                        dateToValue = Util.GetValueOfDateTime(dr["DateTo"]);
                    }
                }

                if (!dateFromValue.HasValue || !dateToValue.HasValue)
                {
                    return Error(GetMsg(ctx, "VAS_037_PeriodNotFound", "Current period was not found"));
                }

                if (stdPrecision < 0)
                {
                    stdPrecision = 2;
                }

                bool hasData = journalCount > 0;

                string description = string.Format(
                    GetMsg(ctx, "VAS_037_JournalCountDescription", "{0} journal(s)"),
                    journalCount
                );

                return new
                {
                    success = true,
                    error = "",
                    title = GetMsg(ctx, "VAS_037_NetDifferenceTitle", "Net Difference"),
                    mainMetric = netDiff,
                    mainMetricText = netDiff.ToString(),
                    description = description,
                    badgeText = GetPeriodBadgeText(ctx, isYTD),
                    dateFrom = FormatDate(dateFromValue.Value),
                    dateTo = FormatDate(dateToValue.Value),
                    currencyISO = isoCode,
                    currencySymbol = curSymbol,
                    stdPrecision = stdPrecision,
                    hasData = hasData,

                    NetDiff = netDiff,
                    IsBalanced = (netDiff == 0),
                    JournalCount = journalCount,
                    CurSymbol = curSymbol,
                    ISOCode = isoCode,
                    StdPrecision = stdPrecision,
                    MonthAbbr = DateTime.Now.ToString("MMM")
                };
            }
            catch (Exception ex)
            {
                Trace.TraceError(ex.ToString());
                return Error(GetMsg(ctx, "VAS_037_ErrorLoadingData", "Could not load data"));
            }
        }

        private string BuildProtectedJournalSql(Ctx ctx)
        {
            string protectedJournalSql = @"
SELECT GL_Journal.GL_Journal_ID,
       GL_Journal.AD_Client_ID,
       GL_Journal.AD_Org_ID,
       GL_Journal.C_AcctSchema_ID,
       GL_Journal.DateAcct
FROM GL_Journal GL_Journal
WHERE GL_Journal.PostingType = 'A'
AND GL_Journal.IsActive = 'Y'
AND GL_Journal.AD_Client_ID = @AD_Client_ID";

            protectedJournalSql = MRole.GetDefault(ctx).AddAccessSQL(
                protectedJournalSql,
                "GL_Journal",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            return protectedJournalSql;
        }

        private string BuildJournalDataSql()
        {
            return @"
SELECT ProtectedJournal.GL_Journal_ID,
       ProtectedJournal.AD_Client_ID,
       ProtectedJournal.AD_Org_ID,
       ProtectedJournal.C_AcctSchema_ID,
       ProtectedJournal.DateAcct,
       GL_JournalLine.AmtAcctDr,
       GL_JournalLine.AmtAcctCr
FROM ProtectedJournal ProtectedJournal
INNER JOIN GL_JournalLine GL_JournalLine
    ON (ProtectedJournal.GL_Journal_ID = GL_JournalLine.GL_Journal_ID)
WHERE GL_JournalLine.IsActive = 'Y'";
        }

        private string BuildSchemaCurrencySql()
        {
            return @"
SELECT ClientInfo.AD_Client_ID,
       AcctSchema.C_AcctSchema_ID,
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
            DateTime today = DateTime.Today;
            string todayLiteral = GetDateLiteral(today);

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
AND CurrentPeriod.StartDate <= " + todayLiteral + @"
AND CurrentPeriod.EndDate >= " + todayLiteral;
        }

        private string GetDateToExclusiveExpression(string dateColumn)
        {
            if (DB.IsOracle())
            {
                return dateColumn + " + 1";
            }

            return dateColumn + " + INTERVAL '1 day'";
        }

        private string GetDateLiteral(DateTime date)
        {
            string dateText = FormatDate(date);

            if (DB.IsOracle())
            {
                return "TO_DATE('" + dateText + "', 'YYYY-MM-DD')";
            }

            return "DATE '" + dateText + "'";
        }

        private string FormatDate(DateTime date)
        {
            return date.ToString("yyyy-MM-dd");
        }

        private bool IsYTD(string period)
        {
            return !string.IsNullOrEmpty(period)
                && string.Compare(period, "ytd", StringComparison.OrdinalIgnoreCase) == 0;
        }

        private string GetPeriodBadgeText(Ctx ctx, bool isYTD)
        {
            if (isYTD)
            {
                return GetMsg(ctx, "VAS_037_YTDBadge", "YTD");
            }

            return GetMsg(ctx, "VAS_037_MonthBadge", "Month");
        }

        private string GetMsg(Ctx ctx, string key, string fallback)
        {
            string msg = Msg.GetMsg(ctx, key);

            if (string.IsNullOrEmpty(msg) || msg == key)
            {
                return fallback;
            }

            return msg;
        }

        private object SessionExpired()
        {
            return new
            {
                success = false,
                error = "Session Expired",
                hasData = false
            };
        }

        private object Error(string message)
        {
            return new
            {
                success = false,
                error = message,
                hasData = false
            };
        }
    }
}
