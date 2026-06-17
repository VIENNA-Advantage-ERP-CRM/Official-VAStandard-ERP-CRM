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

            string dayDiffSql = @"(EXTRACT(YEAR FROM ah.DateAcct) - EXTRACT(YEAR FROM inv.DateInvoiced)) * 365
                                + (EXTRACT(MONTH FROM ah.DateAcct) - EXTRACT(MONTH FROM inv.DateInvoiced)) * 30
                                + (EXTRACT(DAY FROM ah.DateAcct) - EXTRACT(DAY FROM inv.DateInvoiced))";

            // Target days — same dynamic, weighted contracted payment period the KPI uses.
            strQuery = @"WITH RecentAllocated AS (
    SELECT inv.C_Invoice_ID,
           inv.DateInvoiced,
           SUM(al.Amount) AS AllocatedAmount
      FROM (" + baseInvoiceQuery + @") inv
     INNER JOIN C_AllocationLine al ON (al.C_Invoice_ID = inv.C_Invoice_ID)
     INNER JOIN C_AllocationHdr ah ON (al.C_AllocationHdr_ID = ah.C_AllocationHdr_ID)
     INNER JOIN C_Payment p ON (p.C_Payment_ID = al.C_Payment_ID)
     WHERE al.IsActive = 'Y'
       AND ah.IsActive = 'Y'
       AND ah.DocStatus IN ('CO', 'CL')
       AND ah.AD_Client_ID = @ClientID
       AND CAST(ah.DateAcct AS DATE) >= " + recentPaymentStartSql + @"
       AND CAST(ah.DateAcct AS DATE) <= " + recentPaymentEndSql + @"
       AND p.IsReceipt = 'N'
       AND p.DocStatus IN ('CO', 'CL')
       AND p.IsActive = 'Y'
       AND al.C_Invoice_ID IS NOT NULL
       AND al.C_Payment_ID IS NOT NULL
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
                    INNER JOIN C_AllocationHdr ah ON (al.C_AllocationHdr_ID = ah.C_AllocationHdr_ID)
                   WHERE al.IsActive = 'Y'
                     AND ah.IsActive = 'Y'
                     AND ah.DocStatus IN ('CO', 'CL')
                     AND ah.AD_Client_ID = @ClientID
                     AND CAST(ah.DateAcct AS DATE) BETWEEN DATE '" + w.MonthStart + "' AND DATE '" + w.MonthEnd + @"' ";

            DataSet dsCur = DB.ExecuteDataset(strQuery, dataParams, null);
            if (dsCur != null && dsCur.Tables.Count > 0 && dsCur.Tables[0].Rows.Count > 0)
            {
                result.CurrentDpo = Util.GetValueOfInt(dsCur.Tables[0].Rows[0]["CurrentMonthDpo"]);
            }

            // Last month DPO.
            strQuery = @"SELECT ROUND(AVG(" + dayDiffSql + @")) AS LastMonthDpo
                    FROM (" + baseInvoiceQuery + @") inv
                    INNER JOIN C_AllocationLine al ON (al.C_Invoice_ID = inv.C_Invoice_ID)
                    INNER JOIN C_AllocationHdr ah ON (al.C_AllocationHdr_ID = ah.C_AllocationHdr_ID)
                   WHERE al.IsActive = 'Y'
                     AND ah.IsActive = 'Y'
                     AND ah.DocStatus IN ('CO', 'CL')
                     AND ah.AD_Client_ID = @ClientID
                     AND CAST(ah.DateAcct AS DATE) BETWEEN DATE '" + w.PrevMonthStart + "' AND DATE '" + w.PrevMonthEnd + @"' ";

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
     INNER JOIN C_AllocationHdr ah ON (al.C_AllocationHdr_ID = ah.C_AllocationHdr_ID)
     WHERE al.IsActive = 'Y'
       AND ah.IsActive = 'Y'
       AND ah.DocStatus IN ('CO', 'CL')
       AND ah.AD_Client_ID = @ClientID
       AND CAST(ah.DateAcct AS DATE) >= DATE '" + win12Start + @"'
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
     INNER JOIN C_AllocationHdr ah ON (al.C_AllocationHdr_ID = ah.C_AllocationHdr_ID)
     INNER JOIN C_Payment p ON (p.C_Payment_ID = al.C_Payment_ID)
     WHERE al.IsActive = 'Y'
       AND ah.IsActive = 'Y'
       AND ah.DocStatus IN ('CO', 'CL')
       AND ah.AD_Client_ID = @ClientID
       AND CAST(ah.DateAcct AS DATE) >= TRUNC(CURRENT_DATE) - 30
       AND CAST(ah.DateAcct AS DATE) <= TRUNC(CURRENT_DATE)
       AND p.IsReceipt = 'N'
       AND p.DocStatus IN ('CO', 'CL')
       AND p.IsActive = 'Y'
       AND al.C_Invoice_ID IS NOT NULL
       AND al.C_Payment_ID IS NOT NULL
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
            strQuery = @"SELECT ROUND(AVG(
                             (EXTRACT(YEAR FROM ah.DateAcct) - EXTRACT(YEAR FROM inv.DateInvoiced)) * 365
                           + (EXTRACT(MONTH FROM ah.DateAcct) - EXTRACT(MONTH FROM inv.DateInvoiced)) * 30
                           + (EXTRACT(DAY FROM ah.DateAcct) - EXTRACT(DAY FROM inv.DateInvoiced))
                         )) AS CurrentMonthDpo
                    FROM (" + baseInvoiceQuery + @") inv
                    INNER JOIN C_AllocationLine al ON (al.C_Invoice_ID = inv.C_Invoice_ID)
                    INNER JOIN C_AllocationHdr ah ON (al.C_AllocationHdr_ID = ah.C_AllocationHdr_ID)
                   WHERE al.IsActive = 'Y'
                     AND ah.IsActive = 'Y'
                     AND ah.DocStatus IN ('CO', 'CL')
                     AND ah.AD_Client_ID = @ClientID
                     AND EXTRACT(YEAR FROM ah.DateAcct) = " + currentYear + @"
                     AND EXTRACT(MONTH FROM ah.DateAcct) = " + currentMonth + @" ";

            DataSet ds = DB.ExecuteDataset(strQuery, dataParams, null);
            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                result.CurrentMonthDpo = Util.GetValueOfInt(ds.Tables[0].Rows[0]["CurrentMonthDpo"]);
            }

            // Step 1b — Last month DPO.
            strQuery = @"SELECT ROUND(AVG(
                             (EXTRACT(YEAR FROM ah.DateAcct) - EXTRACT(YEAR FROM inv.DateInvoiced)) * 365
                           + (EXTRACT(MONTH FROM ah.DateAcct) - EXTRACT(MONTH FROM inv.DateInvoiced)) * 30
                           + (EXTRACT(DAY FROM ah.DateAcct) - EXTRACT(DAY FROM inv.DateInvoiced))
                         )) AS LastMonthDpo
                    FROM (" + baseInvoiceQuery + @") inv
                    INNER JOIN C_AllocationLine al ON (al.C_Invoice_ID = inv.C_Invoice_ID)
                    INNER JOIN C_AllocationHdr ah ON (al.C_AllocationHdr_ID = ah.C_AllocationHdr_ID)
                   WHERE al.IsActive = 'Y'
                     AND ah.IsActive = 'Y'
                     AND ah.DocStatus IN ('CO', 'CL')
                     AND ah.AD_Client_ID = @ClientID
                     AND EXTRACT(YEAR FROM ah.DateAcct) = " + lastMonthYear + @"
                     AND EXTRACT(MONTH FROM ah.DateAcct) = " + lastMonthNum + @" ";

            DataSet dsLast = DB.ExecuteDataset(strQuery, dataParams, null);
            if (dsLast != null && dsLast.Tables.Count > 0 && dsLast.Tables[0].Rows.Count > 0)
            {
                result.LastMonthDpo = Util.GetValueOfInt(dsLast.Tables[0].Rows[0]["LastMonthDpo"]);
            }

            result.GapDays = result.CurrentMonthDpo - result.TargetDpo;

            // Step 2 — Sparkline: monthly avg DPO for last 7 months (grouped by allocation month).
            // Reuses baseInvoiceQuery — MRole already applied in Step 2; not called again here.
            DateTime sevenMonthsAgo = now.AddMonths(-6);
            int sparkYear = sevenMonthsAgo.Year;
            int sparkMonth = sevenMonthsAgo.Month;
            strQuery = @"SELECT EXTRACT(YEAR FROM ah.DateAcct) AS PaidYear,
                         EXTRACT(MONTH FROM ah.DateAcct) AS PaidMonth,
                         ROUND(AVG(
                             (EXTRACT(YEAR FROM ah.DateAcct) - EXTRACT(YEAR FROM inv.DateInvoiced)) * 365
                           + (EXTRACT(MONTH FROM ah.DateAcct) - EXTRACT(MONTH FROM inv.DateInvoiced)) * 30
                           + (EXTRACT(DAY FROM ah.DateAcct) - EXTRACT(DAY FROM inv.DateInvoiced))
                         )) AS MonthlyDpo
                    FROM (" + baseInvoiceQuery + @") inv
                    INNER JOIN C_AllocationLine al ON (al.C_Invoice_ID = inv.C_Invoice_ID)
                    INNER JOIN C_AllocationHdr ah ON (al.C_AllocationHdr_ID = ah.C_AllocationHdr_ID)
                   WHERE al.IsActive = 'Y'
                     AND ah.IsActive = 'Y'
                     AND ah.DocStatus = 'CO'
                     AND ah.AD_Client_ID = @ClientID
                     AND (EXTRACT(YEAR FROM ah.DateAcct) * 12 + EXTRACT(MONTH FROM ah.DateAcct))
                         >= (" + sparkYear + @" * 12 + " + sparkMonth + @")
                   GROUP BY EXTRACT(YEAR FROM ah.DateAcct), EXTRACT(MONTH FROM ah.DateAcct)
                   ORDER BY EXTRACT(YEAR FROM ah.DateAcct), EXTRACT(MONTH FROM ah.DateAcct)";

            DataSet sparkDs = DB.ExecuteDataset(strQuery, dataParams, null);
            if (sparkDs != null && sparkDs.Tables.Count > 0 && sparkDs.Tables[0].Rows.Count > 0)
            {
                for (int i = 0; i < sparkDs.Tables[0].Rows.Count; i++)
                {
                    result.SparklineData.Add(Util.GetValueOfDecimal(sparkDs.Tables[0].Rows[i]["MonthlyDpo"]));
                }
            }

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
