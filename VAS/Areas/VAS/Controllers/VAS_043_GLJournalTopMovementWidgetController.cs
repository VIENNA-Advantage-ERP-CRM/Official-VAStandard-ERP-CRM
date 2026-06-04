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
    /// Controller for the Top Ledger Movement widget — GL_Journal / GL_JournalLine / C_ElementValue.
    /// Returns the top accounts ranked by absolute net movement (DR − CR) for posted journals.
    /// Supports month / YTD period toggle.
    /// Linked widgets:
    ///   1. GLJournalTopMovementWidget — horizontal bar chart of top accounts by net movement.
    /// Amounts are displayed in the primary accounting schema base currency.
    /// MRole is applied on GL_Journal (the primary access-controlled table).
    /// GROUP BY / ORDER BY are appended after MRole so the access filters
    /// remain inside the WHERE clause and do not corrupt the query structure.
    /// Date range filtering uses integer YYYYMMDD comparisons for Oracle/PostgreSQL compatibility.
    /// </summary>
    public class VAS_043_GLJournalTopMovementWidgetController : Controller
    {
        /// <summary>
        /// Returns the top 10 accounts by absolute net movement for posted GL journals
        /// in the requested period.
        /// period = 'month' (1st of current month to today, default) | 'ytd' (Jan 1 to today)
        /// </summary>
        public JsonResult GetTopMovement(string period)
        {
            if (Session["ctx"] == null) { return Json("", JsonRequestBehavior.AllowGet); }
            Ctx ctx = Session["ctx"] as Ctx;
            bool isYtd = string.Compare(period, "ytd", StringComparison.OrdinalIgnoreCase) == 0;

            // ── Step 1: Resolve primary accounting schema ─────────────────────
            int    acctSchemaId = 0;
            string curSymbol    = "";
            string isoCode      = "";
            int    stdPrecision = 2;

            string schemaSql = "SELECT C_AcctSchema.C_AcctSchema_ID,"
                             + " C_Currency.CurSymbol, C_Currency.ISO_Code, C_Currency.StdPrecision"
                             + " FROM C_AcctSchema"
                             + " INNER JOIN C_Currency ON (C_AcctSchema.C_Currency_ID=C_Currency.C_Currency_ID)"
                             + " WHERE C_AcctSchema.IsActive='Y'"
                             + " AND C_AcctSchema.AD_Client_ID=@ClientID";

            SqlParameter[] schemaParams = { new SqlParameter("@ClientID", ctx.GetAD_Client_ID()) };
            DataSet schemaDs = DB.ExecuteDataset(schemaSql, schemaParams, null);
            if (schemaDs != null && schemaDs.Tables[0].Rows.Count > 0)
            {
                acctSchemaId = Util.GetValueOfInt(schemaDs.Tables[0].Rows[0]["C_AcctSchema_ID"]);
                curSymbol    = Util.GetValueOfString(schemaDs.Tables[0].Rows[0]["CurSymbol"]);
                isoCode      = Util.GetValueOfString(schemaDs.Tables[0].Rows[0]["ISO_Code"]);
                stdPrecision = Util.GetValueOfInt(schemaDs.Tables[0].Rows[0]["StdPrecision"]);
            }

            // ── Step 2: Calculate period boundaries ───────────────────────────
            DateTime today        = DateTime.Now.Date;
            DateTime periodStart  = isYtd
                ? new DateTime(today.Year, 1, 1)
                : new DateTime(today.Year, today.Month, 1);
            DateTime periodEnd    = today;

            // YYYYMMDD integer comparisons — cross-DB safe, injection-safe (server-side ints only)
            int periodStartYmd = periodStart.Year * 10000 + periodStart.Month * 100 + periodStart.Day;
            int periodEndYmd   = periodEnd.Year   * 10000 + periodEnd.Month   * 100 + periodEnd.Day;

            string dateExpr = "(EXTRACT(YEAR FROM GL_Journal.DateAcct) * 10000"
                            + " + EXTRACT(MONTH FROM GL_Journal.DateAcct) * 100"
                            + " + EXTRACT(DAY FROM GL_Journal.DateAcct))";

            // ── Step 3: Build base query and apply MRole before GROUP BY ──────
            // MRole must be applied while the SQL ends with the WHERE clause.
            // GROUP BY and ORDER BY are appended afterwards so the access
            // predicates stay inside WHERE and do not break query structure.
            string sqlBase = "SELECT C_ElementValue.Value AS AccountCode,"
                           + " C_ElementValue.Name AS AccountName,"
                           + " COALESCE(SUM(GL_JournalLine.AmtAcctDr), 0) AS TotalDebit,"
                           + " COALESCE(SUM(GL_JournalLine.AmtAcctCr), 0) AS TotalCredit"
                           + " FROM GL_Journal"
                           + " INNER JOIN GL_JournalLine ON (GL_Journal.GL_Journal_ID=GL_JournalLine.GL_Journal_ID"
                           + " AND GL_JournalLine.IsActive='Y')"
                           + " INNER JOIN C_ElementValue ON (GL_JournalLine.Account_ID=C_ElementValue.C_ElementValue_ID"
                           + " AND C_ElementValue.IsActive='Y')"
                           + " WHERE GL_Journal.DocStatus='CO'"
                           + " AND GL_Journal.IsActive='Y'"
                           + " AND GL_Journal.C_AcctSchema_ID=@AcctSchemaID"
                           + " AND " + dateExpr + " >= @PeriodStart"
                           + " AND " + dateExpr + " <= @PeriodEnd";

            // Apply MRole before GROUP BY / ORDER BY
            sqlBase = MRole.GetDefault(ctx).AddAccessSQL(
                sqlBase, "GL_Journal", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            // Append GROUP BY and ORDER BY after MRole
            string sql = sqlBase
                       + " GROUP BY C_ElementValue.Value, C_ElementValue.Name"
                       + " ORDER BY ABS(COALESCE(SUM(GL_JournalLine.AmtAcctDr),0)"
                       + " - COALESCE(SUM(GL_JournalLine.AmtAcctCr),0)) DESC";

            SqlParameter[] mainParams =
            {
                new SqlParameter("@AcctSchemaID", acctSchemaId),
                new SqlParameter("@PeriodStart",  periodStartYmd),
                new SqlParameter("@PeriodEnd",    periodEndYmd)
            };
            DataSet ds = DB.ExecuteDataset(sql, mainParams, null);

            // ── Step 4: Build result — top 10 accounts ────────────────────────
            var accounts = new List<object>();

            if (ds != null && ds.Tables[0].Rows.Count > 0)
            {
                // First pass: find the maximum absolute net for bar scaling
                decimal maxAbs = 0m;
                int     maxRows = Math.Min(10, ds.Tables[0].Rows.Count);

                for (int i = 0; i < maxRows; i++)
                {
                    DataRow row = ds.Tables[0].Rows[i];
                    decimal dr  = Util.GetValueOfDecimal(row["TotalDebit"]);
                    decimal cr  = Util.GetValueOfDecimal(row["TotalCredit"]);
                    decimal abs = Math.Abs(dr - cr);
                    if (abs > maxAbs) { maxAbs = abs; }
                }

                // Second pass: build result with bar percentages
                for (int i = 0; i < maxRows; i++)
                {
                    DataRow row = ds.Tables[0].Rows[i];
                    decimal dr  = Decimal.Round(
                        Util.GetValueOfDecimal(row["TotalDebit"]),  stdPrecision, MidpointRounding.AwayFromZero);
                    decimal cr  = Decimal.Round(
                        Util.GetValueOfDecimal(row["TotalCredit"]), stdPrecision, MidpointRounding.AwayFromZero);
                    decimal net    = dr - cr;
                    decimal absNet = Math.Abs(net);
                    int     barPct = maxAbs > 0m
                        ? (int)Math.Round(absNet / maxAbs * 100m, MidpointRounding.AwayFromZero)
                        : 0;

                    accounts.Add(new
                    {
                        AccountCode = Util.GetValueOfString(row["AccountCode"]),
                        AccountName = Util.GetValueOfString(row["AccountName"]),
                        TotalDebit  = dr,
                        TotalCredit = cr,
                        NetMovement = absNet,
                        IsCredit    = net < 0m,   // CR > DR → credit-heavy → green bar
                        BarPct      = barPct
                    });
                }
            }

            string periodLabel = isYtd
                ? "YTD " + today.Year
                : today.ToString("MMM yyyy").ToUpper();

            return Json(JsonConvert.SerializeObject(new
            {
                Accounts     = accounts,
                CurSymbol    = curSymbol,
                ISOCode      = isoCode,
                StdPrecision = stdPrecision,
                PeriodLabel  = periodLabel
            }), JsonRequestBehavior.AllowGet);
        }
    }
}
