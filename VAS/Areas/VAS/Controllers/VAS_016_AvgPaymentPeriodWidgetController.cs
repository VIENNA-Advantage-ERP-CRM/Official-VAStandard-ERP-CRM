/************************************************************
 * Module Name    : VAS
 * Purpose        : Controller for Avg Payment Period (DPO) KPI Widget
 * chronological  : Development
 * Created Date   : 13 May 2026
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
    public class VAS_016_AvgPaymentPeriodWidgetController : Controller
    {
        string strQuery = "";


        [Authorize]
        public ActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// Returns average days payable outstanding (DPO) for current and last month,
        /// target vs gap, currency info from accounting schema, and 7-month sparkline data.
        /// DPO is computed from C_AllocationHdr.DateAcct minus C_Invoice.DateInvoiced
        /// for paid AP invoices, joined through C_AllocationLine.
        /// </summary>
        public JsonResult GetAvgPaymentPeriodKpi()
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                AvgPaymentPeriodKpiResult result = BuildKpiResult(ctx);
                retJSON = JsonConvert.SerializeObject(result);
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Returns the DPO analysis drill-down: current / target / last-month DPO, the gap,
        /// and average DPO broken down by product category — all from the same allocation
        /// (payment-date) logic the KPI uses.
        /// </summary>
        public JsonResult GetDpoDrilldown()
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                DpoDrilldownResult result = BuildDpoDrilldown(ctx);
                retJSON = JsonConvert.SerializeObject(result);
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        private sealed class ReportWindows
        {
            public string MonthStart, MonthEnd, PrevMonthStart, PrevMonthEnd;
        }

        private static string FmtDate(DateTime d) { return d.ToString("yyyy-MM-dd"); }

        private ReportWindows GetReportWindows(Ctx ctx)
        {
            DateTime monthStart = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            return new ReportWindows
            {
                MonthStart = FmtDate(monthStart),
                MonthEnd = FmtDate(monthStart.AddMonths(1).AddDays(-1)),
                PrevMonthStart = FmtDate(monthStart.AddMonths(-1)),
                PrevMonthEnd = FmtDate(monthStart.AddDays(-1))
            };
        }

        /// <summary>
        /// Current fiscal-year START date from the client's configured accounting calendar
        /// (AD_ClientInfo -> C_Calendar -> C_Year -> C_Period): the year whose span contains
        /// today. Falls back to Apr 1 (Indian FY) when no calendar/periods are configured.
        /// Mirrors the fiscal-year resolution used in VAS_023.
        /// </summary>
        private DateTime GetCurrentFyStart(Ctx ctx)
        {
            DateTime today = DateTime.Today;
            string sql = @"SELECT MIN(p.StartDate) AS YrStart, MAX(p.EndDate) AS YrEnd
                   FROM AD_ClientInfo ci
                  INNER JOIN C_Calendar cal ON (ci.C_Calendar_ID = cal.C_Calendar_ID)
                  INNER JOIN C_Year yr      ON (yr.C_Calendar_ID = cal.C_Calendar_ID)
                  INNER JOIN C_Period p     ON (p.C_Year_ID = yr.C_Year_ID)
                  WHERE ci.AD_Client_ID = @ClientID
                    AND ci.IsActive = 'Y'
                    AND yr.IsActive = 'Y'
                    AND p.IsActive = 'Y'
                    AND p.PeriodType = 'S'
                  GROUP BY yr.C_Year_ID
                  ORDER BY MIN(p.StartDate)";
            SqlParameter[] prm = { new SqlParameter("@ClientID", ctx.GetAD_Client_ID()) };

            DataSet ds = DB.ExecuteDataset(sql, prm, null);
            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                DataRowCollection rows = ds.Tables[0].Rows;
                int curIdx = -1;
                for (int i = 0; i < rows.Count; i++)
                {
                    DateTime s = Util.GetValueOfDateTime(rows[i]["YrStart"]).Value  .Date;
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
                    return Util.GetValueOfDateTime(rows[curIdx]["YrStart"]).Value.Date;
                }
            }

            // Fallback — Apr 1 Indian fiscal year.
            int y = today.Month >= 4 ? today.Year : today.Year - 1;
            return new DateTime(y, 4, 1);
        }

        private DpoDrilldownResult BuildDpoDrilldown(Ctx ctx)
        {
            var result = new DpoDrilldownResult
            {
                Categories = new List<DpoCategoryRow>()
            };
            result.TargetDpo = 30;

            int clientId = ctx.GetAD_Client_ID();
            SqlParameter[] dataParams = { new SqlParameter("@ClientID", clientId) };
            ReportWindows w = GetReportWindows(ctx);

            // Rolling 12-month window start for the per-category DPO breakdown
            // (trailing analytical window, emitted as an ANSI DATE literal - rule 4).
            DateTime drillToday = DateTime.Today;
            string win12Start = FmtDate(new DateTime(drillToday.Year, drillToday.Month, 1).AddMonths(-11));

            // rule 4: PostgreSQL date subtraction can yield an INTERVAL, so compute the
            // contracted-term day gap with date_part('epoch', ...); Oracle keeps date - date.
            string termDaysSql = DB.IsPostgreSQL()
                ? "CAST(date_part('epoch', (CAST(ips.DueDate AS TIMESTAMP) - CAST(ra.DateInvoiced AS TIMESTAMP))) / 86400 AS NUMERIC)"
                : "(CAST(ips.DueDate AS DATE) - CAST(ra.DateInvoiced AS DATE))";
            string recentPaymentStartSql = DB.IsPostgreSQL()
                ? "CURRENT_DATE - 30"
                : "TRUNC(CURRENT_DATE) - 30";
            string recentPaymentEndSql = DB.IsPostgreSQL()
                ? "CURRENT_DATE"
                : "TRUNC(CURRENT_DATE)";

            // Secured base invoice view — MRole applied ONLY on C_Invoice (primary table).
            // Allocation/line/category tables are joined OUTSIDE MRole scope (matches the KPI).
            string baseInvoiceQuery = @"SELECT i.C_Invoice_ID, i.DateInvoiced
                    FROM C_Invoice i
                   WHERE i.IsSOTrx = 'N'
                     AND i.IsReturnTrx = 'N'
                     AND i.IsExpenseInvoice = 'N'
                     AND i.DocStatus IN ('CO', 'CL')
                     AND i.IsActive = 'Y'
                     AND i.AD_Client_ID = @ClientID";

            baseInvoiceQuery = MRole.GetDefault(ctx).AddAccessSQL(baseInvoiceQuery, "i", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            // Settlement instrument = an AP payment (C_Payment) OR a cash-journal line
            // (C_CashLine -> C_Cash), mirroring AvgDaysToPayController. The settlement date
            // is the payment's DateAcct, or the parent cash journal's DateAcct for cash
            // settlements. Both are LEFT-joined so an invoice settled either way contributes;
            // receipt-side / non-completed payments and voided cash journals are excluded in
            // the ON, so such rows yield NULL and are dropped by the "settlement date IS NOT
            // NULL" guard. (This ADDS the non-voided cash rows to the payment-only behaviour.)
            string settleJoins = @"
     LEFT JOIN C_Payment pay ON (pay.C_Payment_ID = al.C_Payment_ID AND pay.IsActive = 'Y' AND pay.IsReceipt = 'N' AND pay.DocStatus IN ('CO', 'CL'))
     LEFT JOIN C_CashLine cl ON (cl.C_CashLine_ID = al.C_CashLine_ID AND cl.IsActive = 'Y')
     LEFT JOIN C_Cash csh ON (csh.C_Cash_ID = cl.C_Cash_ID AND csh.IsActive = 'Y' AND csh.DocStatus NOT IN ('VO'))";
            string settleDate = "COALESCE(pay.DateAcct, csh.DateAcct)";
            string settleGuard = " AND (al.C_Payment_ID IS NOT NULL OR al.C_CashLine_ID IS NOT NULL) AND " + settleDate + " IS NOT NULL ";

            string dayDiffSql = DB.IsPostgreSQL()
                ? "CAST(date_part('epoch', (CAST(" + settleDate + " AS TIMESTAMP) - CAST(inv.DateInvoiced AS TIMESTAMP))) / 86400 AS NUMERIC)"
                : "(CAST(" + settleDate + " AS DATE) - CAST(inv.DateInvoiced AS DATE))";

            // Target days — same dynamic, weighted contracted payment period the KPI uses.
            strQuery = @"WITH RecentAllocated AS (
    SELECT inv.C_Invoice_ID,
           inv.DateInvoiced,
           SUM(al.Amount) AS AllocatedAmount
      FROM (" + baseInvoiceQuery + @") inv
     INNER JOIN C_AllocationLine al ON (al.C_Invoice_ID = inv.C_Invoice_ID)
     INNER JOIN C_AllocationHdr ah ON (al.C_AllocationHdr_ID = ah.C_AllocationHdr_ID)" + settleJoins + @"
     WHERE al.IsActive = 'Y'
       AND ah.IsActive = 'Y'
       AND ah.DocStatus IN ('CO', 'CL')
       AND ah.AD_Client_ID = @ClientID
       AND CAST(" + settleDate + @" AS DATE) >= " + recentPaymentStartSql + @"
       AND CAST(" + settleDate + @" AS DATE) <= " + recentPaymentEndSql + @"
       AND al.C_Invoice_ID IS NOT NULL" + settleGuard + @"
     GROUP BY inv.C_Invoice_ID, inv.DateInvoiced
),
InvoiceTarget AS (
    SELECT ra.C_Invoice_ID,
           ra.AllocatedAmount,
           ROUND(
               SUM(ips.DueAmt * " + termDaysSql + @")
               / NULLIF(SUM(ips.DueAmt), 0)
           , 0) AS InvoiceTargetDays
      FROM RecentAllocated ra
     INNER JOIN C_InvoicePaySchedule ips ON (ips.C_Invoice_ID = ra.C_Invoice_ID)
     WHERE ips.IsActive = 'Y'
       AND ips.DueAmt > 0
     GROUP BY ra.C_Invoice_ID, ra.AllocatedAmount
)
SELECT ROUND(
           SUM(AllocatedAmount * InvoiceTargetDays)
           / NULLIF(SUM(AllocatedAmount), 0)
       , 0) AS TargetDays
  FROM InvoiceTarget";

            DataSet dsTarget = DB.ExecuteDataset(strQuery, dataParams, null);
            if (dsTarget != null && dsTarget.Tables.Count > 0 && dsTarget.Tables[0].Rows.Count > 0
                && dsTarget.Tables[0].Rows[0]["TargetDays"] != DBNull.Value)
            {
                int dbTarget = Util.GetValueOfInt(dsTarget.Tables[0].Rows[0]["TargetDays"]);
                if (dbTarget > 0) { result.TargetDpo = dbTarget; }
            }

            // Current month DPO.
            strQuery = @"SELECT ROUND(AVG(" + dayDiffSql + @")) AS CurrentMonthDpo
                    FROM (" + baseInvoiceQuery + @") inv
                    INNER JOIN C_AllocationLine al ON (al.C_Invoice_ID = inv.C_Invoice_ID)
                    INNER JOIN C_AllocationHdr ah ON (al.C_AllocationHdr_ID = ah.C_AllocationHdr_ID)" + settleJoins + @"
                   WHERE al.IsActive = 'Y'
                     AND ah.IsActive = 'Y'
                     AND ah.DocStatus IN ('CO', 'CL')
                     AND ah.AD_Client_ID = @ClientID
                     AND CAST(" + settleDate + @" AS DATE) BETWEEN DATE '" + w.MonthStart + "' AND DATE '" + w.MonthEnd + @"' " + settleGuard;

            DataSet dsCur = DB.ExecuteDataset(strQuery, dataParams, null);
            if (dsCur != null && dsCur.Tables.Count > 0 && dsCur.Tables[0].Rows.Count > 0)
            {
                result.CurrentDpo = Util.GetValueOfInt(dsCur.Tables[0].Rows[0]["CurrentMonthDpo"]);
            }

            // Last month DPO.
            strQuery = @"SELECT ROUND(AVG(" + dayDiffSql + @")) AS LastMonthDpo
                    FROM (" + baseInvoiceQuery + @") inv
                    INNER JOIN C_AllocationLine al ON (al.C_Invoice_ID = inv.C_Invoice_ID)
                    INNER JOIN C_AllocationHdr ah ON (al.C_AllocationHdr_ID = ah.C_AllocationHdr_ID)" + settleJoins + @"
                   WHERE al.IsActive = 'Y'
                     AND ah.IsActive = 'Y'
                     AND ah.DocStatus IN ('CO', 'CL')
                     AND ah.AD_Client_ID = @ClientID
                     AND CAST(" + settleDate + @" AS DATE) BETWEEN DATE '" + w.PrevMonthStart + "' AND DATE '" + w.PrevMonthEnd + @"' " + settleGuard;

            DataSet dsLast = DB.ExecuteDataset(strQuery, dataParams, null);
            if (dsLast != null && dsLast.Tables.Count > 0 && dsLast.Tables[0].Rows.Count > 0)
            {
                result.LastMonthDpo = Util.GetValueOfInt(dsLast.Tables[0].Rows[0]["LastMonthDpo"]);
            }

            result.GapDays = result.CurrentDpo - result.TargetDpo;

            // DPO by product category over the last 12 months.
            // Per-invoice DPO is averaged first (CTE) so multi-line invoices are not
            // double-weighted, then averaged per category.
            strQuery = @"WITH InvoiceDpo AS (
    SELECT inv.C_Invoice_ID,
           AVG(" + dayDiffSql + @") AS DpoDays
      FROM (" + baseInvoiceQuery + @") inv
     INNER JOIN C_AllocationLine al ON (al.C_Invoice_ID = inv.C_Invoice_ID)
     INNER JOIN C_AllocationHdr ah ON (al.C_AllocationHdr_ID = ah.C_AllocationHdr_ID)" + settleJoins + @"
     WHERE al.IsActive = 'Y'
       AND ah.IsActive = 'Y'
       AND ah.DocStatus IN ('CO', 'CL')
       AND ah.AD_Client_ID = @ClientID
       AND CAST(" + settleDate + @" AS DATE) >= DATE '" + win12Start + @"'" + settleGuard + @"
     GROUP BY inv.C_Invoice_ID
)
SELECT pc.Name AS CategoryName,
       ROUND(AVG(idp.DpoDays)) AS DpoDays,
       COUNT(DISTINCT idp.C_Invoice_ID) AS InvoiceCount
  FROM InvoiceDpo idp
 INNER JOIN C_InvoiceLine il ON (il.C_Invoice_ID = idp.C_Invoice_ID)
 INNER JOIN M_Product p ON (il.M_Product_ID = p.M_Product_ID)
 INNER JOIN M_Product_Category pc ON (p.M_Product_Category_ID = pc.M_Product_Category_ID)
 WHERE il.IsActive = 'Y'
   AND p.IsActive = 'Y'
   AND pc.IsActive = 'Y'
 GROUP BY pc.Name
HAVING COUNT(DISTINCT idp.C_Invoice_ID) > 0
 ORDER BY DpoDays DESC";

            DataSet dsCat = DB.ExecuteDataset(strQuery, dataParams, null);
            if (dsCat != null && dsCat.Tables.Count > 0)
            {
                int rowCount = 0;
                foreach (DataRow row in dsCat.Tables[0].Rows)
                {
                    if (rowCount >= 6) { break; }

                    result.Categories.Add(new DpoCategoryRow
                    {
                        Name = Util.GetValueOfString(row["CategoryName"]),
                        DpoDays = Util.GetValueOfInt(row["DpoDays"]),
                        InvoiceCount = Util.GetValueOfInt(row["InvoiceCount"])
                    });
                    rowCount++;
                }
            }

            return result;
        }

        private AvgPaymentPeriodKpiResult BuildKpiResult(Ctx ctx)
        {
            AvgPaymentPeriodKpiResult result = new AvgPaymentPeriodKpiResult();
            result.SparklineData = new List<decimal>();
            result.TargetDpo    = 30;

            int clientId = ctx.GetAD_Client_ID();
            SqlParameter[] dataParams = { new SqlParameter("@ClientID", clientId) };
            DateTime now = DateTime.Now;
            int currentYear = now.Year;
            int currentMonth = now.Month;
            DateTime prevMonthDate = now.AddMonths(-1);
            int lastMonthYear = prevMonthDate.Year;
            int lastMonthNum = prevMonthDate.Month;

            // Step 1 — Build a simple C_Invoice-only base query and apply MRole to it.
            // MRole.AddAccessSQL is called only on this short, join-free query to avoid
            // the AccessSqlParser OOM that occurs when the string contains multiple INNER JOINs
            // with EXTRACT arithmetic expressions. The secured inline view is then joined to
            // C_AllocationLine and C_AllocationHdr in the outer queries — outside MRole scope.
            string baseInvoiceQuery = @"SELECT i.C_Invoice_ID, i.DateInvoiced
                    FROM C_Invoice i
                   WHERE i.IsSOTrx = 'N'
                     AND i.IsReturnTrx = 'N'
                     AND i.IsExpenseInvoice = 'N'
                     AND i.DocStatus IN ('CO', 'CL')
                     AND i.IsActive = 'Y'
                     AND i.AD_Client_ID = @ClientID";

            baseInvoiceQuery = MRole.GetDefault(ctx).AddAccessSQL(baseInvoiceQuery, "i", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            // Settlement instrument = an AP payment (C_Payment) OR a cash-journal line
            // (C_CashLine -> C_Cash), mirroring AvgDaysToPayController. Settlement date is the
            // payment's DateAcct, or the parent cash journal's DateAcct for cash settlements.
            // LEFT-joined so an invoice settled either way contributes; receipt-side /
            // non-completed payments and voided cash journals fall out via the guard.
            string settleJoins = @"
                    LEFT JOIN C_Payment pay ON (pay.C_Payment_ID = al.C_Payment_ID AND pay.IsActive = 'Y' AND pay.IsReceipt = 'N' AND pay.DocStatus IN ('CO', 'CL'))
                    LEFT JOIN C_CashLine cl ON (cl.C_CashLine_ID = al.C_CashLine_ID AND cl.IsActive = 'Y')
                    LEFT JOIN C_Cash csh ON (csh.C_Cash_ID = cl.C_Cash_ID AND csh.IsActive = 'Y' AND csh.DocStatus NOT IN ('VO'))";
            string settleDate = "COALESCE(pay.DateAcct, csh.DateAcct)";
            string settleGuard = " AND (al.C_Payment_ID IS NOT NULL OR al.C_CashLine_ID IS NOT NULL) AND " + settleDate + " IS NOT NULL ";
            // Y/M/D day-diff (settlement date - invoice date) reused by the month/sparkline queries.
            string extractDayDiff =
                  "(EXTRACT(YEAR FROM " + settleDate + ") - EXTRACT(YEAR FROM inv.DateInvoiced)) * 365"
                + " + (EXTRACT(MONTH FROM " + settleDate + ") - EXTRACT(MONTH FROM inv.DateInvoiced)) * 30"
                + " + (EXTRACT(DAY FROM " + settleDate + ") - EXTRACT(DAY FROM inv.DateInvoiced))";

            // Step 0 — Dynamic target days: weighted-average contracted payment period
            // for AP invoices actually paid in the last 30 days.
            // Formula: SUM(DueAmt × (DueDate − DateInvoiced)) / SUM(DueAmt) per invoice,
            //          then weighted by AllocatedAmount across all invoices.
            // MRole applied only to C_Invoice inside baseInvoiceQuery (CTE aliases are not physical tables).
            // Falls back to 30 days when no recent allocation data exists.
            strQuery = @"WITH RecentAllocated AS (
    SELECT inv.C_Invoice_ID,
           inv.DateInvoiced,
           SUM(al.Amount) AS AllocatedAmount
      FROM (" + baseInvoiceQuery + @") inv
     INNER JOIN C_AllocationLine al ON (al.C_Invoice_ID = inv.C_Invoice_ID)
     INNER JOIN C_AllocationHdr ah ON (al.C_AllocationHdr_ID = ah.C_AllocationHdr_ID)" + settleJoins + @"
     WHERE al.IsActive = 'Y'
       AND ah.IsActive = 'Y'
       AND ah.DocStatus IN ('CO', 'CL')
       AND ah.AD_Client_ID = @ClientID
       AND CAST(" + settleDate + @" AS DATE) >= TRUNC(CURRENT_DATE) - 30
       AND CAST(" + settleDate + @" AS DATE) <= TRUNC(CURRENT_DATE)
       AND al.C_Invoice_ID IS NOT NULL" + settleGuard + @"
     GROUP BY inv.C_Invoice_ID, inv.DateInvoiced
),
InvoiceTarget AS (
    SELECT ra.C_Invoice_ID,
           ra.AllocatedAmount,
           ROUND(
               SUM(ips.DueAmt * (CAST(ips.DueDate AS DATE) - CAST(ra.DateInvoiced AS DATE)))
               / NULLIF(SUM(ips.DueAmt), 0)
           , 0) AS InvoiceTargetDays
      FROM RecentAllocated ra
     INNER JOIN C_InvoicePaySchedule ips ON (ips.C_Invoice_ID = ra.C_Invoice_ID)
     WHERE ips.IsActive = 'Y'
       AND ips.DueAmt > 0
     GROUP BY ra.C_Invoice_ID, ra.AllocatedAmount
)
SELECT ROUND(
           SUM(AllocatedAmount * InvoiceTargetDays)
           / NULLIF(SUM(AllocatedAmount), 0)
       , 0) AS TargetDays
  FROM InvoiceTarget";

            DataSet dsTarget = DB.ExecuteDataset(strQuery, dataParams, null);
            if (dsTarget != null && dsTarget.Tables.Count > 0 && dsTarget.Tables[0].Rows.Count > 0
                && dsTarget.Tables[0].Rows[0]["TargetDays"] != DBNull.Value)
            {
                int dbTarget = Util.GetValueOfInt(dsTarget.Tables[0].Rows[0]["TargetDays"]);
                if (dbTarget > 0) { result.TargetDpo = dbTarget; }
            }

            // Step 1a — Current month DPO.
            // Wraps the secured inline view; allocation tables joined outside MRole scope.
            strQuery = @"SELECT ROUND(AVG(" + extractDayDiff + @")) AS CurrentMonthDpo
                    FROM (" + baseInvoiceQuery + @") inv
                    INNER JOIN C_AllocationLine al ON (al.C_Invoice_ID = inv.C_Invoice_ID)
                    INNER JOIN C_AllocationHdr ah ON (al.C_AllocationHdr_ID = ah.C_AllocationHdr_ID)" + settleJoins + @"
                   WHERE al.IsActive = 'Y'
                     AND ah.IsActive = 'Y'
                     AND ah.DocStatus IN ('CO', 'CL')
                     AND ah.AD_Client_ID = @ClientID
                     AND EXTRACT(YEAR FROM " + settleDate + @") = " + currentYear + @"
                     AND EXTRACT(MONTH FROM " + settleDate + @") = " + currentMonth + @" " + settleGuard;

            DataSet ds = DB.ExecuteDataset(strQuery, dataParams, null);
            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                result.CurrentMonthDpo = Util.GetValueOfInt(ds.Tables[0].Rows[0]["CurrentMonthDpo"]);
            }

            // Step 1b — Last month DPO.
            strQuery = @"SELECT ROUND(AVG(" + extractDayDiff + @")) AS LastMonthDpo
                    FROM (" + baseInvoiceQuery + @") inv
                    INNER JOIN C_AllocationLine al ON (al.C_Invoice_ID = inv.C_Invoice_ID)
                    INNER JOIN C_AllocationHdr ah ON (al.C_AllocationHdr_ID = ah.C_AllocationHdr_ID)" + settleJoins + @"
                   WHERE al.IsActive = 'Y'
                     AND ah.IsActive = 'Y'
                     AND ah.DocStatus IN ('CO', 'CL')
                     AND ah.AD_Client_ID = @ClientID
                     AND EXTRACT(YEAR FROM " + settleDate + @") = " + lastMonthYear + @"
                     AND EXTRACT(MONTH FROM " + settleDate + @") = " + lastMonthNum + @" " + settleGuard;

            DataSet dsLast = DB.ExecuteDataset(strQuery, dataParams, null);
            if (dsLast != null && dsLast.Tables.Count > 0 && dsLast.Tables[0].Rows.Count > 0)
            {
                result.LastMonthDpo = Util.GetValueOfInt(dsLast.Tables[0].Rows[0]["LastMonthDpo"]);
            }

            result.GapDays = result.CurrentMonthDpo - result.TargetDpo;

            // Step 2 — Sparkline: monthly avg DPO for the CURRENT FISCAL YEAR (12 months from
            // the FY start taken from the client's accounting calendar — same logic as VAS_023),
            // aligned to FY month order (index 0 = FY start month) with 0 for months that have
            // no settled invoices. Reuses baseInvoiceQuery (MRole already applied above).
            DateTime fyStart = GetCurrentFyStart(ctx);
            int fyStartAbs = fyStart.Year * 12 + fyStart.Month;

            strQuery = @"SELECT (EXTRACT(YEAR FROM " + settleDate + @") * 12 + EXTRACT(MONTH FROM " + settleDate + @")) AS MonAbs,
                         ROUND(AVG(" + extractDayDiff + @")) AS MonthlyDpo
                    FROM (" + baseInvoiceQuery + @") inv
                    INNER JOIN C_AllocationLine al ON (al.C_Invoice_ID = inv.C_Invoice_ID)
                    INNER JOIN C_AllocationHdr ah ON (al.C_AllocationHdr_ID = ah.C_AllocationHdr_ID)" + settleJoins + @"
                   WHERE al.IsActive = 'Y'
                     AND ah.IsActive = 'Y'
                     AND ah.DocStatus = 'CO'
                     AND ah.AD_Client_ID = @ClientID
                     AND (EXTRACT(YEAR FROM " + settleDate + @") * 12 + EXTRACT(MONTH FROM " + settleDate + @"))
                         BETWEEN " + fyStartAbs + @" AND " + (fyStartAbs + 11) + @"" + settleGuard + @"
                   GROUP BY (EXTRACT(YEAR FROM " + settleDate + @") * 12 + EXTRACT(MONTH FROM " + settleDate + @"))
                   ORDER BY MonAbs";

            decimal[] spark = new decimal[12];
            DataSet sparkDs = DB.ExecuteDataset(strQuery, dataParams, null);
            if (sparkDs != null && sparkDs.Tables.Count > 0)
            {
                foreach (DataRow row in sparkDs.Tables[0].Rows)
                {
                    int idx = Util.GetValueOfInt(row["MonAbs"]) - fyStartAbs;
                    if (idx >= 0 && idx < 12)
                    {
                        spark[idx] = Util.GetValueOfDecimal(row["MonthlyDpo"]);
                    }
                }
            }
            for (int i = 0; i < 12; i++) { result.SparklineData.Add(spark[i]); }

            return result;
        }

        public class AvgPaymentPeriodKpiResult
        {
            public int CurrentMonthDpo { get; set; }
            public int LastMonthDpo { get; set; }
            public int TargetDpo { get; set; }
            public int GapDays { get; set; }
            public List<decimal> SparklineData { get; set; }
        }
        public class DpoDrilldownResult
        {
            public int CurrentDpo { get; set; }
            public int LastMonthDpo { get; set; }
            public int TargetDpo { get; set; }
            public int GapDays { get; set; }
            public List<DpoCategoryRow> Categories { get; set; }
        }

        public class DpoCategoryRow
        {
            public string Name { get; set; }
            public int DpoDays { get; set; }
            public int InvoiceCount { get; set; }
        }
    }
}
