/************************************************************
 * Module Name    : VAS
 * Purpose        : Monthly Purchase Spend Trend Widget
 * Created Date   : 14 May 2026
 * Created by     : Humam Yousif
 ***********************************************************/
using CoreLibrary.DataBase;
using Newtonsoft.Json;
using System;
using System.Data;
using System.Data.SqlClient;
using System.Web.Mvc;
using VAdvantage.Model;
using VAdvantage.Utility;

namespace VAS.Areas.VAS.Controllers
{
    public class VAS_017_MonthlyPurchaseSpendWidgetController : Controller
    {
        string strQuery = "";

        [Authorize]
        public ActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// Returns monthly GrandTotal totals for purchase invoices for the current and previous
        /// fiscal year, with the fiscal year taken from the client's configured accounting
        /// calendar (not a hardcoded Apr–Mar cycle). Uses 3 DB round-trips: currency +
        /// fiscal-year bounds + CTE aggregation. MRole is applied only to the join-free
        /// C_Invoice base query inside the CTE. FyMonth = 1..12 counted from the FY's first
        /// month; FyStartYear/FyStartMonth let the client render calendar-correct axis labels.
        /// </summary>
        public JsonResult GetMonthlyPurchaseSpend()
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                MonthlyPurchaseSpendResult result = BuildResult(ctx);
                retJSON = JsonConvert.SerializeObject(result);
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        private MonthlyPurchaseSpendResult BuildResult(Ctx ctx)
        {
            var result = new MonthlyPurchaseSpendResult
            {
                CurrentYear = new decimal[12],
                LastYear    = new decimal[12]
            };

            int clientId = ctx.GetAD_Client_ID();
            SqlParameter[] dataParams   = { new SqlParameter("@ClientID", clientId) };

            // Round-trip 1 — functional currency from accounting schema
            int schemaCurrencyId = 0;
            strQuery = @"SELECT cs.C_Currency_ID, c.CurSymbol, c.StdPrecision, c.ISO_Code
                    FROM C_AcctSchema cs
                    INNER JOIN AD_ClientInfo ci ON (ci.C_AcctSchema1_ID = cs.C_AcctSchema_ID)
                    INNER JOIN C_Currency c ON (cs.C_Currency_ID = c.C_Currency_ID)
                   WHERE ci.AD_Client_ID = @ClientID
                     AND ci.IsActive = 'Y'
                     AND cs.IsActive = 'Y'
                     AND c.IsActive = 'Y'";

            strQuery = MRole.GetDefault(ctx).AddAccessSQL(strQuery, "cs", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            DataSet cDs = DB.ExecuteDataset(strQuery, dataParams, null);
            if (cDs != null && cDs.Tables.Count > 0 && cDs.Tables[0].Rows.Count > 0)
            {
                schemaCurrencyId    = Util.GetValueOfInt(cDs.Tables[0].Rows[0]["C_Currency_ID"]);
                result.CurSymbol    = Util.GetValueOfString(cDs.Tables[0].Rows[0]["CurSymbol"]);
                result.StdPrecision = Util.GetValueOfInt(cDs.Tables[0].Rows[0]["StdPrecision"]);
                result.CurIso       = Util.GetValueOfString(cDs.Tables[0].Rows[0]["ISO_Code"]);
            }

            if (schemaCurrencyId == 0) { return result; }

            // Round-trip 2 — current & previous FISCAL YEAR start dates from the client's
            // configured accounting calendar (AD_ClientInfo -> C_Calendar -> C_Year ->
            // C_Period), same logic as VAS_023. The FY whose span contains today is current;
            // the one before it is previous. Falls back to Apr–Mar if no calendar/periods.
            DateTime today = DateTime.Today;
            DateTime curStart = today, prevStart = today;
            bool fyResolved = false;

            strQuery = @"SELECT yr.FiscalYear AS FiscalYear,
                        MIN(p.StartDate) AS YrStart,
                        MAX(p.EndDate)   AS YrEnd
                   FROM AD_ClientInfo ci
                  INNER JOIN C_Calendar cal ON (ci.C_Calendar_ID = cal.C_Calendar_ID)
                  INNER JOIN C_Year yr      ON (yr.C_Calendar_ID = cal.C_Calendar_ID)
                  INNER JOIN C_Period p     ON (p.C_Year_ID = yr.C_Year_ID)
                  WHERE ci.AD_Client_ID = @ClientID
                    AND ci.IsActive = 'Y'
                    AND yr.IsActive = 'Y'
                    AND p.IsActive = 'Y'
                    AND p.PeriodType = 'S'
                  GROUP BY yr.C_Year_ID, yr.FiscalYear
                  ORDER BY MIN(p.StartDate)";

            DataSet fyDs = DB.ExecuteDataset(strQuery, dataParams, null);
            if (fyDs != null && fyDs.Tables.Count > 0 && fyDs.Tables[0].Rows.Count > 0)
            {
                DataRowCollection rows = fyDs.Tables[0].Rows;
                int curIdx = -1;
                for (int i = 0; i < rows.Count; i++)
                {
                    DateTime s = Util.GetValueOfDateTime(rows[i]["YrStart"]).Value.Date;
                    DateTime e = Util.GetValueOfDateTime(rows[i]["YrEnd"]).Value.Date;
                    if (today >= s && today <= e) { curIdx = i; break; }
                }
                if (curIdx < 0)
                {
                    for (int i = rows.Count - 1; i >= 0; i--)
                    {
                        if (today >= Util.GetValueOfDateTime(rows[i]["YrStart"]).Value.Date) { curIdx = i; break; }
                    }
                }
                if (curIdx >= 0)
                {
                    curStart = Util.GetValueOfDateTime(rows[curIdx]["YrStart"]).Value.Date;
                    result.FyLabel = "FY " + Util.GetValueOfString(rows[curIdx]["FiscalYear"]);
                    prevStart = (curIdx > 0)
                        ? Util.GetValueOfDateTime(rows[curIdx - 1]["YrStart"]).Value.Date
                        : curStart.AddYears(-1);
                    fyResolved = true;
                }
            }

            if (!fyResolved)
            {
                // Fallback — Apr 1 Indian fiscal year.
                int fbStartYear = today.Month >= 4 ? today.Year : today.Year - 1;
                curStart  = new DateTime(fbStartYear, 4, 1);
                prevStart = new DateTime(fbStartYear - 1, 4, 1);
                result.FyLabel = "FY " + fbStartYear + "–" + (fbStartYear + 1).ToString().Substring(2);
            }

            result.FyStartYear  = curStart.Year;
            result.FyStartMonth = curStart.Month;

            // Absolute month index (year*12+month) of each FY's first month; FyMonth 1..12 is
            // the offset from it. A standard 12-period calendar makes prevStartAbs = curStartAbs-12.
            int curStartAbs  = curStart.Year * 12 + curStart.Month;
            int prevStartAbs = prevStart.Year * 12 + prevStart.Month;

            // Round-trip 3 — CTE aggregation for current and previous FY monthly totals.
            // MRole applied to C_Invoice base query only (join-free to prevent AccessSqlParser OOM).
            // CTE alias MonthlyData is NOT a physical table — MRole is NOT applied to it.
            string baseQuery = @"SELECT i.C_Invoice_ID,
                           CASE WHEN i.IsReturnTrx = 'N'
                                THEN COALESCE(currencyConvert(i.GrandTotal, i.C_Currency_ID, " + schemaCurrencyId + @", i.DateAcct, i.C_ConversionType_ID, i.AD_Client_ID, i.AD_Org_ID), 0)
                                ELSE -COALESCE(currencyConvert(i.GrandTotal, i.C_Currency_ID, " + schemaCurrencyId + @", i.DateAcct, i.C_ConversionType_ID, i.AD_Client_ID, i.AD_Org_ID), 0)
                                END AS GrandTotal,
                           i.DateAcct
                    FROM C_Invoice i
                   WHERE i.IsSOTrx = 'N'
                     AND i.IsExpenseInvoice = 'N'
                     AND i.DocStatus IN ('CO', 'CL')
                     AND i.IsActive = 'Y'
                     AND i.AD_Client_ID = @ClientID";

            baseQuery = MRole.GetDefault(ctx).AddAccessSQL(baseQuery, "i", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            // Bucket each invoice by absolute month index (year*12+month), then classify as
            // current ('C') or previous ('P') FY and compute FyMonth = 1..12 offset from that
            // FY's first month. Works for any calendar start (Apr, Jan, Jul, …).
            strQuery = @"WITH MonthlyData AS (
                    SELECT inv.GrandTotal,
                           (EXTRACT(YEAR FROM inv.DateAcct) * 12 + EXTRACT(MONTH FROM inv.DateAcct)) AS MonAbs
                      FROM (" + baseQuery + @") inv
                     WHERE (EXTRACT(YEAR FROM inv.DateAcct) * 12 + EXTRACT(MONTH FROM inv.DateAcct))
                           BETWEEN " + prevStartAbs + @" AND " + (curStartAbs + 11) + @"
                ),
                Classified AS (
                    SELECT GrandTotal,
                           CASE WHEN MonAbs >= " + curStartAbs + @" THEN 'C' ELSE 'P' END AS FyType,
                           CASE WHEN MonAbs >= " + curStartAbs + @" THEN MonAbs - " + curStartAbs + @" + 1
                                ELSE MonAbs - " + prevStartAbs + @" + 1 END AS FyMonth
                      FROM MonthlyData
                )
                SELECT FyType, FyMonth, SUM(GrandTotal) AS TotalAmount
                  FROM Classified
                 GROUP BY FyType, FyMonth
                 ORDER BY FyType, FyMonth";

            DataSet ds = DB.ExecuteDataset(strQuery, dataParams, null);
            if (ds != null && ds.Tables.Count > 0)
            {
                foreach (DataRow row in ds.Tables[0].Rows)
                {
                    string fyType  = Util.GetValueOfString(row["FyType"]);
                    int    fyMonth = Util.GetValueOfInt(row["FyMonth"]);
                    decimal amt    = Util.GetValueOfDecimal(row["TotalAmount"]);
                    if (fyMonth >= 1 && fyMonth <= 12)
                    {
                        if (fyType == "C") result.CurrentYear[fyMonth - 1] = amt;
                        else               result.LastYear[fyMonth - 1]    = amt;
                    }
                }
            }

            return result;
        }

        public class MonthlyPurchaseSpendResult
        {
            public string    CurSymbol    { get; set; }
            public int       StdPrecision { get; set; }
            public string    CurIso       { get; set; }
            public string    FyLabel      { get; set; }
            public int       FyStartYear  { get; set; }
            public int       FyStartMonth { get; set; }
            public decimal[] CurrentYear  { get; set; }
            public decimal[] LastYear     { get; set; }
        }
    }
}
