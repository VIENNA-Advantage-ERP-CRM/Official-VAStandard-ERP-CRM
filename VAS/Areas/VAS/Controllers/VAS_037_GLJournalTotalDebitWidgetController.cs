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
    /// <summary>
    /// Controller for GL Journal amount KPI widgets.
    /// Linked widgets:
    ///   1. GLJournalTotalDebitWidget  — displays SUM(AmtAcctDr) via GetTotalDebit()
    ///   2. GLJournalTotalCreditWidget — displays SUM(AmtAcctCr) via GetTotalCredit()
    ///   3. GLJournalNetDiffWidget     — displays SUM(AmtAcctDr) - SUM(AmtAcctCr) via GetNetDifference()
    /// All widgets support a month / YTD period toggle and display amounts
    /// in the primary accounting schema base currency (C_AcctSchema → C_Currency).
    /// </summary>
    public class VAS_037_GLJournalTotalDebitWidgetController : Controller
    {
        // ── Shared helper ─────────────────────────────────────────────────────
        /// <summary>
        /// Builds a period-filtered sum query over the requested GL_JournalLine amount column.
        /// MRole is applied on GL_Journal (the primary access-controlled table).
        /// </summary>
        private object GetJournalTotal(Ctx ctx, string amountColumn, string resultAlias, string period)
        {
            int     currentMonth = DateTime.Now.Month;
            int     currentYear  = DateTime.Now.Year;
            decimal total        = 0;
            int     journalCount = 0;

            // ── Step 1: Resolve the primary accounting schema for this client ──
            // All amount columns (AmtAcctDr / AmtAcctCr) in GL_JournalLine are
            // stored in the base currency of the schema the journal belongs to.
            // Mixing journals from different schemas would sum values in different
            // currencies, producing a meaningless total.  We therefore pin both
            // the aggregation query and the currency display to the same schema.
            int    acctSchemaId  = 0;
            string curSymbol     = "";
            string isoCode       = "";
            int    stdPrecision  = 2;

            string schemaSql = "SELECT C_AcctSchema.C_AcctSchema_ID,"
                             + " C_Currency.CurSymbol, C_Currency.ISO_Code, C_Currency.StdPrecision"
                             + " FROM C_AcctSchema"
                             + " INNER JOIN C_Currency ON (C_AcctSchema.C_Currency_ID = C_Currency.C_Currency_ID)"
                             + " WHERE C_AcctSchema.IsActive = 'Y'"
                             + " AND C_AcctSchema.AD_Client_ID = @ClientID";

            SqlParameter[] schemaParams = { new SqlParameter("@ClientID", ctx.GetAD_Client_ID()) };
            DataSet schemaDs = DB.ExecuteDataset(schemaSql, schemaParams, null);
            if (schemaDs != null && schemaDs.Tables[0].Rows.Count > 0)
            {
                acctSchemaId = Util.GetValueOfInt(schemaDs.Tables[0].Rows[0]["C_AcctSchema_ID"]);
                curSymbol    = Util.GetValueOfString(schemaDs.Tables[0].Rows[0]["CurSymbol"]);
                isoCode      = Util.GetValueOfString(schemaDs.Tables[0].Rows[0]["ISO_Code"]);
                stdPrecision = Util.GetValueOfInt(schemaDs.Tables[0].Rows[0]["StdPrecision"]);
            }

            // ── Step 2: Aggregate only journals that belong to that schema ────
            string sql = "SELECT SUM(GL_JournalLine. @AmountColumn) AS @resultAlias ,"
                       + " COUNT(DISTINCT GL_Journal.GL_Journal_ID) AS JournalCount"
                       + " FROM GL_Journal"
                       + " INNER JOIN GL_JournalLine ON (GL_Journal.GL_Journal_ID = GL_JournalLine.GL_Journal_ID)"
                       + " WHERE GL_Journal.PostingType = 'A'"
                       + " AND GL_Journal.IsActive = 'Y'"
                       + " AND GL_JournalLine.IsActive = 'Y'"
                       + " AND GL_Journal.C_AcctSchema_ID = @AcctSchemaID"
                       + " AND EXTRACT(YEAR FROM GL_Journal.DateAcct) = @CurrentYear";

            var sqlParams = new List<SqlParameter>
            {
                new SqlParameter("@AcctSchemaID", acctSchemaId),
                new SqlParameter("@CurrentYear",  currentYear),
                new SqlParameter("@AmountColumn", amountColumn),
                new SqlParameter("@ResultAlias", resultAlias)
            };

            // month = current month only; ytd = full year (year filter above is sufficient)
            if (string.IsNullOrEmpty(period) || string.Compare(period, "ytd", StringComparison.OrdinalIgnoreCase) != 0)
            {
                sql += " AND EXTRACT(MONTH FROM GL_Journal.DateAcct) = @CurrentMonth";
                sqlParams.Add(new SqlParameter("@CurrentMonth", currentMonth));
            }

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "GL_Journal", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            DataSet ds = DB.ExecuteDataset(sql, sqlParams.ToArray(), null);
            if (ds != null && ds.Tables[0].Rows.Count > 0)
            {
                total        = Util.GetValueOfDecimal(ds.Tables[0].Rows[0][resultAlias]);
                journalCount = Util.GetValueOfInt(ds.Tables[0].Rows[0]["JournalCount"]);
            }

            total = Decimal.Round(total, stdPrecision, MidpointRounding.AwayFromZero);

            return new
            {
                Total        = total,
                JournalCount = journalCount,
                CurSymbol    = curSymbol,
                ISOCode      = isoCode,
                StdPrecision = stdPrecision,
                MonthAbbr    = DateTime.Now.ToString("MMM").ToUpper()
            };
        }

        // ── Actions ───────────────────────────────────────────────────────────

        /// <summary>
        /// Total debit — SUM(AmtAcctDr) from GL_JournalLine.
        /// period = 'month' (default) | 'ytd'
        /// </summary>
        public JsonResult GetTotalDebit(string period)
        {
            if (Session["ctx"] == null) { return Json("", JsonRequestBehavior.AllowGet); }
            return Json(JsonConvert.SerializeObject(
                GetJournalTotal(Session["ctx"] as Ctx, "AmtAcctDr", "TotalDebit", period)
            ), JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Total credit — SUM(AmtAcctCr) from GL_JournalLine.
        /// period = 'month' (default) | 'ytd'
        /// </summary>
        public JsonResult GetTotalCredit(string period)
        {
            if (Session["ctx"] == null) { return Json("", JsonRequestBehavior.AllowGet); }
            return Json(JsonConvert.SerializeObject(
                GetJournalTotal(Session["ctx"] as Ctx, "AmtAcctCr", "TotalCredit", period)
            ), JsonRequestBehavior.AllowGet);
        }

        // ── Net Difference helper ─────────────────────────────────────────────
        /// <summary>
        /// Computes SUM(AmtAcctDr) - SUM(AmtAcctCr) in a single query so both
        /// sides are always evaluated against the same row set.
        /// IsBalanced is true when NetDiff rounds to exactly zero.
        /// </summary>
        private object GetNetDifferenceData(Ctx ctx, string period)
        {
            int     currentMonth = DateTime.Now.Month;
            int     currentYear  = DateTime.Now.Year;
            decimal netDiff      = 0;
            int     journalCount = 0;

            // ── Step 1: Resolve primary accounting schema ─────────────────────
            int    acctSchemaId = 0;
            string curSymbol    = "";
            string isoCode      = "";
            int    stdPrecision = 2;

            string schemaSql = "SELECT C_AcctSchema.C_AcctSchema_ID,"
                             + " C_Currency.CurSymbol, C_Currency.ISO_Code, C_Currency.StdPrecision"
                             + " FROM C_AcctSchema"
                             + " INNER JOIN C_Currency ON (C_AcctSchema.C_Currency_ID = C_Currency.C_Currency_ID)"
                             + " WHERE C_AcctSchema.IsActive = 'Y'"
                             + " AND C_AcctSchema.AD_Client_ID = @ClientID";

            SqlParameter[] schemaParams = { new SqlParameter("@ClientID", ctx.GetAD_Client_ID()) };
            DataSet schemaDs = DB.ExecuteDataset(schemaSql, schemaParams, null);
            if (schemaDs != null && schemaDs.Tables[0].Rows.Count > 0)
            {
                acctSchemaId = Util.GetValueOfInt(schemaDs.Tables[0].Rows[0]["C_AcctSchema_ID"]);
                curSymbol    = Util.GetValueOfString(schemaDs.Tables[0].Rows[0]["CurSymbol"]);
                isoCode      = Util.GetValueOfString(schemaDs.Tables[0].Rows[0]["ISO_Code"]);
                stdPrecision = Util.GetValueOfInt(schemaDs.Tables[0].Rows[0]["StdPrecision"]);
            }

            // ── Step 2: Compute net difference in a single pass ───────────────
            string sql = "SELECT SUM(GL_JournalLine.AmtAcctDr) - SUM(GL_JournalLine.AmtAcctCr) AS NetDiff,"
                       + " COUNT(DISTINCT GL_Journal.GL_Journal_ID) AS JournalCount"
                       + " FROM GL_Journal"
                       + " INNER JOIN GL_JournalLine ON (GL_Journal.GL_Journal_ID = GL_JournalLine.GL_Journal_ID)"
                       + " WHERE GL_Journal.PostingType = 'A'"
                       + " AND GL_Journal.IsActive = 'Y'"
                       + " AND GL_JournalLine.IsActive = 'Y'"
                       + " AND GL_Journal.C_AcctSchema_ID = @AcctSchemaID"
                       + " AND EXTRACT(YEAR FROM GL_Journal.DateAcct) = @CurrentYear";

            var sqlParams = new List<SqlParameter>
            {
                new SqlParameter("@AcctSchemaID", acctSchemaId),
                new SqlParameter("@CurrentYear",  currentYear)
            };

            if (string.IsNullOrEmpty(period) || string.Compare(period, "ytd", StringComparison.OrdinalIgnoreCase) != 0)
            {
                sql += " AND EXTRACT(MONTH FROM GL_Journal.DateAcct) = @CurrentMonth";
                sqlParams.Add(new SqlParameter("@CurrentMonth", currentMonth));
            }

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "GL_Journal", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            DataSet ds = DB.ExecuteDataset(sql, sqlParams.ToArray(), null);
            if (ds != null && ds.Tables[0].Rows.Count > 0)
            {
                netDiff      = Util.GetValueOfDecimal(ds.Tables[0].Rows[0]["NetDiff"]);
                journalCount = Util.GetValueOfInt(ds.Tables[0].Rows[0]["JournalCount"]);
            }

            netDiff = Decimal.Round(netDiff, stdPrecision, MidpointRounding.AwayFromZero);

            return new
            {
                NetDiff      = netDiff,
                IsBalanced   = (netDiff == 0),
                JournalCount = journalCount,
                CurSymbol    = curSymbol,
                ISOCode      = isoCode,
                StdPrecision = stdPrecision,
                MonthAbbr    = DateTime.Now.ToString("MMM").ToUpper()
            };
        }

        /// <summary>
        /// Net difference — SUM(AmtAcctDr) - SUM(AmtAcctCr) from GL_JournalLine.
        /// period = 'month' (default) | 'ytd'
        /// </summary>
        public JsonResult GetNetDifference(string period)
        {
            if (Session["ctx"] == null) { return Json("", JsonRequestBehavior.AllowGet); }
            return Json(JsonConvert.SerializeObject(
                GetNetDifferenceData(Session["ctx"] as Ctx, period)
            ), JsonRequestBehavior.AllowGet);
        }
    }
}
