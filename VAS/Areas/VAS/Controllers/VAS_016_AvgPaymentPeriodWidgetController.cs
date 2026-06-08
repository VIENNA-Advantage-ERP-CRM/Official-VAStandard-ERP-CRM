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
    }
}
