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
    /// Controller for the GL Journal Volume by Day chart widget.
    /// Linked widgets:
    ///   1. GLJournalVolumeWidget — bar chart of journal count per day
    ///      with a net value trend line overlay.
    ///      Supports week / month period toggle.
    /// Tables   : GL_Journal, GL_JournalLine, C_AcctSchema, C_Currency
    /// MRole is applied on GL_Journal before GROUP BY / ORDER BY to ensure
    /// the access predicates stay inside the WHERE clause.
    /// Date range filtering uses integer YYYYMMDD comparisons so the query
    /// works on both Oracle and PostgreSQL without date-literal syntax.
    /// </summary>
    public class VAS_GLJournalVolumeController : Controller
    {
        /// <summary>
        /// Returns one entry per calendar day in the requested period,
        /// including days with zero journals (filled in C#).
        /// period = 'week' (current Mon–Sun) | 'month' (1st of month to today, default)
        /// </summary>
        public JsonResult GetVolumeByDay(string period)
        {
            if (Session["ctx"] == null) { return Json("", JsonRequestBehavior.AllowGet); }
            Ctx ctx    = Session["ctx"] as Ctx;
            bool isWeek = string.Compare(period, "week", StringComparison.OrdinalIgnoreCase) == 0;

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

            // ── Step 2: Calculate period and week boundaries ──────────────────
            DateTime today = DateTime.Now.Date;

            // Current week: Monday → Sunday
            int daysFromMon = (int)today.DayOfWeek - (int)DayOfWeek.Monday;
            if (daysFromMon < 0) { daysFromMon += 7; }
            DateTime weekStart = today.AddDays(-daysFromMon);
            DateTime weekEnd   = weekStart.AddDays(6);

            DateTime periodStart = isWeek ? weekStart : new DateTime(today.Year, today.Month, 1);
            DateTime periodEnd   = isWeek ? weekEnd   : today;

            // Convert boundaries to YYYYMMDD integers — cross-DB safe, injection-safe
            int periodStartYmd = periodStart.Year * 10000 + periodStart.Month * 100 + periodStart.Day;
            int periodEndYmd   = periodEnd.Year   * 10000 + periodEnd.Month   * 100 + periodEnd.Day;
            int weekStartYmd   = weekStart.Year   * 10000 + weekStart.Month   * 100 + weekStart.Day;
            int weekEndYmd     = weekEnd.Year     * 10000 + weekEnd.Month     * 100 + weekEnd.Day;

            // ── Step 3: Build base query and apply MRole before GROUP BY ──────
            string dateExpr = "(EXTRACT(YEAR FROM GL_Journal.DateAcct) * 10000"
                            + " + EXTRACT(MONTH FROM GL_Journal.DateAcct) * 100"
                            + " + EXTRACT(DAY FROM GL_Journal.DateAcct))";

            string sqlBase = "SELECT GL_Journal.DateAcct,"
                           + " COUNT(DISTINCT GL_Journal.GL_Journal_ID) AS JournalCount,"
                           + " COALESCE(SUM(GL_JournalLine.AmtAcctDr),0)"
                           + " - COALESCE(SUM(GL_JournalLine.AmtAcctCr),0) AS NetValue"
                           + " FROM GL_Journal"
                           + " LEFT OUTER JOIN GL_JournalLine ON (GL_Journal.GL_Journal_ID=GL_JournalLine.GL_Journal_ID"
                           + " AND GL_JournalLine.IsActive='Y')"
                           + " WHERE GL_Journal.IsActive='Y'"
                           + " AND GL_Journal.C_AcctSchema_ID=@AcctSchemaID"
                           + " AND " + dateExpr + " >= @PeriodStart"
                           + " AND " + dateExpr + " <= @PeriodEnd";

            // Apply MRole before GROUP BY / ORDER BY
            sqlBase = MRole.GetDefault(ctx).AddAccessSQL(
                sqlBase, "GL_Journal", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string sql = sqlBase
                       + " GROUP BY GL_Journal.DateAcct"
                       + " ORDER BY GL_Journal.DateAcct ASC";

            SqlParameter[] mainParams =
            {
                new SqlParameter("@AcctSchemaID", acctSchemaId),
                new SqlParameter("@PeriodStart",  periodStartYmd),
                new SqlParameter("@PeriodEnd",    periodEndYmd)
            };
            DataSet ds = DB.ExecuteDataset(sql, mainParams, null);

            // ── Step 4: Map query results by YYYYMMDD key ─────────────────────
            var countMap = new Dictionary<int, int>();
            var netMap   = new Dictionary<int, decimal>();

            if (ds != null)
            {
                foreach (DataRow row in ds.Tables[0].Rows)
                {
                    if (row["DateAcct"] == DBNull.Value) { continue; }
                    DateTime d   = Convert.ToDateTime(row["DateAcct"]);
                    int      ymd = d.Year * 10000 + d.Month * 100 + d.Day;
                    countMap[ymd] = Util.GetValueOfInt(row["JournalCount"]);
                    netMap[ymd]   = Decimal.Round(
                        Util.GetValueOfDecimal(row["NetValue"]), stdPrecision, MidpointRounding.AwayFromZero);
                }
            }

            // ── Step 5: Build complete day array including zero-count days ────
            var days = new List<object>();
            for (DateTime d = periodStart; d <= periodEnd; d = d.AddDays(1))
            {
                int     ymd          = d.Year * 10000 + d.Month * 100 + d.Day;
                int     journalCount = countMap.ContainsKey(ymd) ? countMap[ymd] : 0;
                decimal netValue     = netMap.ContainsKey(ymd) ? netMap[ymd] : 0m;
                bool    isWeekendDay = (d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday);
                bool    isThisWeek   = (ymd >= weekStartYmd && ymd <= weekEndYmd);

                days.Add(new
                {
                    DayNum       = d.Day,
                    DateStr      = d.ToString("MMM dd"),
                    DayOfWeek    = (int)d.DayOfWeek,
                    JournalCount = journalCount,
                    NetValue     = netValue,
                    IsWeekend    = isWeekendDay,
                    IsThisWeek   = isThisWeek
                });
            }

            // ── Step 6: Build period label ─────────────────────────────────────
            bool sameMonth   = (periodStart.Month == periodEnd.Month && periodStart.Year == periodEnd.Year);
            string endFormat = sameMonth ? "d" : "MMM d";
            string periodLabel = periodStart.ToString("MMM d") + " – " + periodEnd.ToString(endFormat);

            return Json(JsonConvert.SerializeObject(new
            {
                Days         = days,
                PeriodLabel  = periodLabel,
                Period       = isWeek ? "week" : "month",
                CurSymbol    = curSymbol,
                ISOCode      = isoCode,
                StdPrecision = stdPrecision
            }), JsonRequestBehavior.AllowGet);
        }
    }
}
