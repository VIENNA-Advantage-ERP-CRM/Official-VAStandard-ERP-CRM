/************************************************************
 * Module Name    : VAS
 * Purpose        : Top Five Vendors by Spend Widget
 * Created Date   : 14 May 2026
 * Created by     : Humam Yousif
 ***********************************************************/
using CoreLibrary.DataBase;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Web.Mvc;
using VAdvantage.Model;
using VAdvantage.Utility;

namespace VAS.Areas.VAS.Controllers
{
    public class VAS_023_TopFiveVendorsWidgetController : Controller
    {
        string strQuery = "";

        [Authorize]
        public ActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// Returns top 5 vendors by purchase spend for the current fiscal year (taken from the
        /// client's configured accounting calendar), with prior fiscal-year amounts for YoY.
        /// Uses 3 DB round-trips: 1 currency + 1 fiscal-year bounds + 1 vendor aggregation.
        /// MRole applied only to the join-free C_Invoice base query.
        /// C_BPartner and C_BP_Group joins are in the outer query outside MRole scope.
        /// </summary>
        public JsonResult GetTopFiveVendors()
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                TopFiveVendorsResult result = BuildResult(ctx);
                retJSON = JsonConvert.SerializeObject(result);
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        private TopFiveVendorsResult BuildResult(Ctx ctx)
        {
            var result = new TopFiveVendorsResult
            {
                Vendors = new List<VendorItem>()
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

            // Round-trip 2 — current & previous FISCAL YEAR date ranges from the client's
            // configured accounting calendar (AD_ClientInfo -> C_Calendar -> C_Year ->
            // C_Period). Each fiscal year's span is MIN(StartDate)..MAX(EndDate) over its
            // standard periods; the year whose span contains today is the current FY and the
            // one immediately before it is the prior FY. This honours whatever calendar the
            // client uses (Apr–Mar, Jan–Dec, Jul–Jun, …) instead of assuming Apr–Mar.
            // Not MRole-wrapped: setup/reference tables, already scoped by AD_Client_ID
            // (mirrors AvgDaysToPayController's fiscal-period query).
            DateTime today = DateTime.Today;
            DateTime curStart = today, curEnd = today, prevStart = today, prevEnd = today;
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
                // If today is outside every defined year (gap/future), use the latest started year.
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
                    curEnd   = Util.GetValueOfDateTime(rows[curIdx]["YrEnd"]).Value.Date;
                    result.FyLabel = "FY " + Util.GetValueOfString(rows[curIdx]["FiscalYear"]);
                    if (curIdx > 0)
                    {
                        prevStart = Util.GetValueOfDateTime(rows[curIdx - 1]["YrStart"]).Value.Date;
                        prevEnd   = Util.GetValueOfDateTime(rows[curIdx - 1]["YrEnd"]).Value.Date;
                    }
                    else
                    {
                        // No earlier fiscal year defined — use the 1-year window before current.
                        prevStart = curStart.AddYears(-1);
                        prevEnd   = curStart.AddDays(-1);
                    }
                    fyResolved = true;
                }
            }

            if (!fyResolved)
            {
                // Fallback — Apr 1 to Mar 31 Indian fiscal year.
                int fyStartYear = today.Month >= 4 ? today.Year : today.Year - 1;
                curStart  = new DateTime(fyStartYear, 4, 1);
                curEnd    = new DateTime(fyStartYear + 1, 3, 31);
                prevStart = new DateTime(fyStartYear - 1, 4, 1);
                prevEnd   = new DateTime(fyStartYear, 3, 31);
                result.FyLabel = "FY " + fyStartYear + "–" + (fyStartYear + 1).ToString().Substring(2);
            }

            string curStartLit  = curStart.ToString("yyyy-MM-dd");
            string curEndLit    = curEnd.ToString("yyyy-MM-dd");
            string prevStartLit = prevStart.ToString("yyyy-MM-dd");
            string prevEndLit   = prevEnd.ToString("yyyy-MM-dd");

            // Round-trip 3 — vendor spend aggregation for current FY and prior FY.
            // MRole on join-free C_Invoice base; C_BPartner + C_BP_Group joins in outer query.
            string baseVendor = @"SELECT i.C_Invoice_ID,
                       CASE WHEN i.IsReturnTrx = 'N'
                            THEN COALESCE(currencyConvert(i.GrandTotal, i.C_Currency_ID, " + schemaCurrencyId + @", i.DateAcct, i.C_ConversionType_ID, i.AD_Client_ID, i.AD_Org_ID), 0)
                            ELSE -COALESCE(currencyConvert(i.GrandTotal, i.C_Currency_ID, " + schemaCurrencyId + @", i.DateAcct, i.C_ConversionType_ID, i.AD_Client_ID, i.AD_Org_ID), 0)
                            END AS GrandTotal,
                       i.C_BPartner_ID, i.DateInvoiced
                  FROM C_Invoice i
                 WHERE i.IsSOTrx = 'N'
                   AND i.IsExpenseInvoice = 'N'
                   AND i.DocStatus IN ('CO', 'CL')
                   AND i.IsActive = 'Y'
                   AND i.AD_Client_ID = @ClientID";
            baseVendor = MRole.GetDefault(ctx).AddAccessSQL(baseVendor, "i", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            strQuery = @"SELECT bp.Name AS VendorName,
                       bpg.Name AS Category,
                       SUM(CASE WHEN CAST(v.DateInvoiced AS DATE) BETWEEN DATE '" + curStartLit + @"' AND DATE '" + curEndLit + @"'
                                THEN v.GrandTotal ELSE 0 END) AS CurrAmt,
                       SUM(CASE WHEN CAST(v.DateInvoiced AS DATE) BETWEEN DATE '" + prevStartLit + @"' AND DATE '" + prevEndLit + @"'
                                THEN v.GrandTotal ELSE 0 END) AS PrevAmt
                  FROM (" + baseVendor + @") v
                 INNER JOIN C_BPartner bp ON (v.C_BPartner_ID = bp.C_BPartner_ID)
                  LEFT OUTER JOIN C_BP_Group bpg ON (bp.C_BP_Group_ID = bpg.C_BP_Group_ID AND bpg.IsActive = 'Y')
                 WHERE CAST(v.DateInvoiced AS DATE) BETWEEN DATE '" + prevStartLit + @"' AND DATE '" + curEndLit + @"'
                   AND bp.IsActive = 'Y'
                 GROUP BY bp.Name, bpg.Name
                 ORDER BY CurrAmt DESC";

            DataSet dsVendors = DB.ExecuteDataset(strQuery, dataParams, null);
            if (dsVendors != null && dsVendors.Tables.Count > 0)
            {
                int rank = 1;
                foreach (DataRow row in dsVendors.Tables[0].Rows)
                {
                    if (rank > 5) { break; }
                    string  vendorName = Util.GetValueOfString(row["VendorName"]);
                    string  category   = Util.GetValueOfString(row["Category"]);
                    decimal currAmt    = Util.GetValueOfDecimal(row["CurrAmt"]);
                    decimal prevAmt    = Util.GetValueOfDecimal(row["PrevAmt"]);
                    decimal yoyPct     = prevAmt != 0
                        ? Math.Round((currAmt - prevAmt) / prevAmt * 100, 1)
                        : 0;
                    result.Vendors.Add(new VendorItem
                    {
                        Rank     = rank,
                        Name     = vendorName,
                        Category = string.IsNullOrEmpty(category) ? "—" : category,
                        Initials = GetInitials(vendorName),
                        CurrAmt  = currAmt,
                        PrevAmt  = prevAmt,
                        YoyPct   = yoyPct
                    });
                    rank++;
                }
            }

            return result;
        }

        private static string GetInitials(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) { return "?"; }
            var parts = name.Trim().Split(new char[] { ' ', '-', '.' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                return (parts[0][0].ToString() + parts[1][0].ToString()).ToUpper();
            }
            return name.Substring(0, Math.Min(2, name.Length)).ToUpper();
        }

        public class TopFiveVendorsResult
        {
            public string           CurSymbol    { get; set; }
            public int              StdPrecision { get; set; }
            public string           CurIso       { get; set; }
            public string           FyLabel      { get; set; }
            public List<VendorItem> Vendors      { get; set; }
        }

        public class VendorItem
        {
            public int     Rank     { get; set; }
            public string  Name     { get; set; }
            public string  Category { get; set; }
            public string  Initials { get; set; }
            public decimal CurrAmt  { get; set; }
            public decimal PrevAmt  { get; set; }
            public decimal YoyPct   { get; set; }
        }
    }
}
