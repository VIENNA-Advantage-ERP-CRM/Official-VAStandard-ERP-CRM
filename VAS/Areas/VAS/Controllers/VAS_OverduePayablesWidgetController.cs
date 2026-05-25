/************************************************************
 * Module Name    : VAS
 * Purpose        : Controller for Overdue Payables KPI Widget
 * chronological  : Development
 * Created Date   : 13 May 2026
 * Created by     : Humam Yousif
 ***********************************************************/
using CoreLibrary.DataBase;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Web.Mvc;
using VAdvantage.Model;
using VAdvantage.Utility;

namespace VAS.Areas.VAS.Controllers
{
    public class VAS_OverduePayablesWidgetController : Controller
    {
        string strQuery = "";

        [Authorize]
        public ActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// Returns total overdue AP balance, invoice count, average days past due,
        /// month-over-month trend data, currency info from accounting schema,
        /// and 7-month sparkline data.
        /// </summary>
        public JsonResult GetOverduePayablesKpi()
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                OverduePayablesKpiResult result = BuildKpiResult(ctx);
                retJSON = JsonConvert.SerializeObject(result);
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        private OverduePayablesKpiResult BuildKpiResult(Ctx ctx)
        {
            OverduePayablesKpiResult result = new OverduePayablesKpiResult();
            result.SparklineData = new List<decimal>();

            int clientId = ctx.GetAD_Client_ID();
            int orgId = ctx.GetAD_Org_ID();
            DateTime now = DateTime.Now;
            int currentYear = now.Year;
            int currentMonth = now.Month;
            int currentDay = now.Day;
            DateTime prevMonthDate = now.AddMonths(-1);
            int lastMonthYear = prevMonthDate.Year;
            int lastMonthNum = prevMonthDate.Month;

            // Step 1 — Get functional currency from the client accounting schema (rule 12).
            // C_AcctSchema1_ID on AD_ClientInfo points to the primary accounting schema,
            // ensuring the widget always uses the correct base currency for this client.
            int schemaCurrencyId = 0;

            strQuery = @"SELECT cs.C_Currency_ID, c.CurSymbol, c.StdPrecision
                    FROM C_AcctSchema cs
                    INNER JOIN AD_ClientInfo ci ON (ci.C_AcctSchema1_ID = cs.C_AcctSchema_ID)
                    INNER JOIN C_Currency c ON (cs.C_Currency_ID = c.C_Currency_ID)
                   WHERE ci.AD_Client_ID = " + clientId + @"
                     AND ci.IsActive = 'Y'
                     AND cs.IsActive = 'Y'
                     AND c.IsActive = 'Y' ";

            strQuery = MRole.GetDefault(ctx).AddAccessSQL(strQuery, "cs", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            DataSet cDs = DB.ExecuteDataset(strQuery, null, null);
            if (cDs != null && cDs.Tables.Count > 0 && cDs.Tables[0].Rows.Count > 0)
            {
                schemaCurrencyId = Util.GetValueOfInt(cDs.Tables[0].Rows[0]["C_Currency_ID"]);
                result.CurSymbol = Util.GetValueOfString(cDs.Tables[0].Rows[0]["CurSymbol"]);
                result.StdPrecision = Util.GetValueOfInt(cDs.Tables[0].Rows[0]["StdPrecision"]);
            }

            if (schemaCurrencyId == 0) { return result; }

            // Step 2 — KPI aggregation.
            // Base query: C_Invoice LEFT OUTER JOIN C_InvoicePaySchedule.
            // When a payment schedule exists, each installment row uses ps.DueDate and ps.DueAmt.
            // When no schedule exists, the invoice's own DueDate and OpenAmt are used (COALESCE).
            // MRole is applied ONLY on C_Invoice alias "i" (primary physical table).
            // C_InvoicePaySchedule ps is a secondary join — MRole is NOT applied on it.
            string baseQuery = @"SELECT i.C_Invoice_ID, i.DateInvoiced,
                            COALESCE(currencyConvert(COALESCE(ps.VA009_OpenAmnt, i.VA009_OpenAmount), i.C_Currency_ID, " + schemaCurrencyId + @", i.DateAcct, i.C_ConversionType_ID, i.AD_Client_ID, i.AD_Org_ID), 0) AS OpenAmt,
                            COALESCE(ps.DueDate, i.DueDate) AS EffectiveDueDate
                       FROM C_Invoice i
                       LEFT OUTER JOIN C_InvoicePaySchedule ps ON (ps.C_Invoice_ID = i.C_Invoice_ID
                                                                    AND ps.IsActive = 'Y'
                                                                    AND ps.IsValid = 'Y')
                      WHERE i.IsSOTrx = 'N'
                        AND i.IsReturnTrx = 'N'
                        AND i.DocStatus IN ('CO', 'CL')
                        AND i.IsActive = 'Y'
                        AND i.AD_Client_ID = " + clientId + @"
                        AND i.AD_Org_ID = " + orgId;

            baseQuery = MRole.GetDefault(ctx).AddAccessSQL(baseQuery, "i", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            // Outer query aggregates from the secured inline view.
            // Overdue = effective due date is in the past and open amount > 0.
            // AvgDpd = average days past due using EXTRACT arithmetic (year*365 + month*30 + day).
            strQuery = @"SELECT SUM(inv.OpenAmt) AS OverdueTotal,
                         SUM(CASE WHEN EXTRACT(YEAR FROM inv.DateInvoiced) = " + currentYear + @"
                                   AND EXTRACT(MONTH FROM inv.DateInvoiced) = " + currentMonth + @"
                              THEN inv.OpenAmt ELSE 0 END) AS CurrentMonthOverdue,
                         SUM(CASE WHEN EXTRACT(YEAR FROM inv.DateInvoiced) = " + lastMonthYear + @"
                                   AND EXTRACT(MONTH FROM inv.DateInvoiced) = " + lastMonthNum + @"
                              THEN inv.OpenAmt ELSE 0 END) AS LastMonthOverdue,
                         COUNT(inv.C_Invoice_ID) AS InvoiceCount,
                         ROUND(AVG(
                             (" + currentYear + @" - EXTRACT(YEAR FROM inv.EffectiveDueDate)) * 365 +
                             (" + currentMonth + @" - EXTRACT(MONTH FROM inv.EffectiveDueDate)) * 30 +
                             (" + currentDay + @" - EXTRACT(DAY FROM inv.EffectiveDueDate))
                         )) AS AvgDpd
                    FROM (" + baseQuery + @") inv
                   WHERE inv.OpenAmt > 0
                     AND inv.EffectiveDueDate IS NOT NULL
                     AND (EXTRACT(YEAR FROM inv.EffectiveDueDate) * 12 + EXTRACT(MONTH FROM inv.EffectiveDueDate)) * 31
                         + EXTRACT(DAY FROM inv.EffectiveDueDate)
                         < ((" + currentYear + @" * 12 + " + currentMonth + @") * 31 + " + currentDay + @")";

            DataSet ds = DB.ExecuteDataset(strQuery, null, null);
            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                DataRow row = ds.Tables[0].Rows[0];
                result.OverdueTotal = Util.GetValueOfDecimal(row["OverdueTotal"]);
                result.CurrentMonthOverdue = Util.GetValueOfDecimal(row["CurrentMonthOverdue"]);
                result.LastMonthOverdue = Util.GetValueOfDecimal(row["LastMonthOverdue"]);
                result.InvoiceCount = Util.GetValueOfInt(row["InvoiceCount"]);
                result.AvgDpd = Util.GetValueOfInt(row["AvgDpd"]);
            }

            // Step 3 — Sparkline: monthly overdue balances for last 7 months.
            // Reuses baseQuery — MRole already applied above; not called again here.
            // Grouped by the effective due date month within the sparkline window.
            DateTime sevenMonthsAgo = now.AddMonths(-6);
            int sparkYear = sevenMonthsAgo.Year;
            int sparkMonth = sevenMonthsAgo.Month;

            strQuery = @"SELECT EXTRACT(YEAR FROM inv.EffectiveDueDate) AS DueYear,
                         EXTRACT(MONTH FROM inv.EffectiveDueDate) AS DueMonth,
                         SUM(inv.OpenAmt) AS MonthlyOverdue
                    FROM (" + baseQuery + @") inv
                   WHERE inv.OpenAmt > 0
                     AND inv.EffectiveDueDate IS NOT NULL
                     AND (EXTRACT(YEAR FROM inv.EffectiveDueDate) * 12 + EXTRACT(MONTH FROM inv.EffectiveDueDate))
                         >= (" + sparkYear + @" * 12 + " + sparkMonth + @")
                   GROUP BY EXTRACT(YEAR FROM inv.EffectiveDueDate), EXTRACT(MONTH FROM inv.EffectiveDueDate)
                   ORDER BY EXTRACT(YEAR FROM inv.EffectiveDueDate), EXTRACT(MONTH FROM inv.EffectiveDueDate)";

            DataSet sparkDs = DB.ExecuteDataset(strQuery, null, null);
            if (sparkDs != null && sparkDs.Tables.Count > 0 && sparkDs.Tables[0].Rows.Count > 0)
            {
                for (int i = 0; i < sparkDs.Tables[0].Rows.Count; i++)
                {
                    result.SparklineData.Add(Util.GetValueOfDecimal(sparkDs.Tables[0].Rows[i]["MonthlyOverdue"]));
                }
            }

            return result;
        }

        public class OverduePayablesKpiResult
        {
            public decimal OverdueTotal { get; set; }
            public decimal CurrentMonthOverdue { get; set; }
            public decimal LastMonthOverdue { get; set; }
            public int InvoiceCount { get; set; }
            public int AvgDpd { get; set; }
            public string CurSymbol { get; set; }
            public int StdPrecision { get; set; }
            public List<decimal> SparklineData { get; set; }
        }
    }
}
