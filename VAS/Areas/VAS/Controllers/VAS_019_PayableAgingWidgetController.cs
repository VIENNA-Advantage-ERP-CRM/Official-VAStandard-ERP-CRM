/************************************************************
 * Module Name    : VAS
 * Purpose        : Controller for Payable Aging + Payment Health KPI Widget
 * chronological  : Development
 * Created Date   : 13 May 2026
 * Created by     : Humam Yousif
 ***********************************************************/
using CoreLibrary.DataBase;
using Newtonsoft.Json;
using System.Data;
using System.Data.SqlClient;
using System.Web.Mvc;
using VAdvantage.Model;
using VAdvantage.Utility;

namespace VAS.Areas.VAS.Controllers
{
    public class VAS_019_PayableAgingWidgetController : Controller
    {
        string strQuery = "";

        [Authorize]
        public ActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// Returns AP payable aging buckets (0-30d, 31-60d, 61-90d, 90+d) with invoice counts,
        /// payment health totals (Paid, Pending, Overdue), overdue count, and currency info.
        /// Uses 2 DB round-trips: currency lookup + single CTE aggregation.
        ///
        /// Pattern matches VAS_ARInvoiceWidget (PoReceiptTabPanelModel.GetARInvSchData):
        ///   - INNER JOIN C_InvoicePaySchedule cs — schedule lines are authoritative for due dates/amounts.
        ///   - cs.VA009_IsPaid distinguishes paid ('Y') vs unpaid ('N') installments.
        ///   - TRUNC(CURRENT_DATE) for date arithmetic (same as ARInvoiceWidget).
        ///   - Single CTE + one aggregation SELECT (CASE WHEN across all buckets) — no UNION ALL.
        ///
        /// MRole is applied ONLY to the C_Invoice alias "ci" (primary physical table).
        /// C_InvoicePaySchedule cs is a secondary join — MRole is NOT applied on it per CLAUDE.md CTE rule.
        /// </summary>
        public JsonResult GetPayableAgingKpi()
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                PayableAgingKpiResult result = BuildKpiResult(ctx);
                retJSON = JsonConvert.SerializeObject(result);
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        private PayableAgingKpiResult BuildKpiResult(Ctx ctx)
        {
            PayableAgingKpiResult result = new PayableAgingKpiResult();

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

            // Round-trip 2 — single CTE aggregation (ARInvoiceWidget pattern).
            // CTE base: INNER JOIN C_InvoicePaySchedule so each unpaid installment is a separate row.
            // cs.VA009_IsPaid tracks per-installment payment status ('Y'=paid, 'N'=unpaid).
            // Outer SELECT uses CASE WHEN to fan out all buckets in one pass — no UNION ALL needed.
            // MRole applied ONLY on C_Invoice alias "ci".
            string baseQuery = @"SELECT ci.C_Invoice_ID,
                            cs.C_InvoicePaySchedule_ID,
                            cs.DueDate,
                            CASE WHEN ci.IsReturnTrx = 'N'
                                 THEN COALESCE(currencyConvert(cs.DueAmt, ci.C_Currency_ID, " + schemaCurrencyId + @", ci.DateAcct, ci.C_ConversionType_ID, ci.AD_Client_ID, ci.AD_Org_ID), 0)
                                 ELSE -COALESCE(currencyConvert(cs.DueAmt, ci.C_Currency_ID, " + schemaCurrencyId + @", ci.DateAcct, ci.C_ConversionType_ID, ci.AD_Client_ID, ci.AD_Org_ID), 0)
                                 END AS DueAmt,
                            cs.VA009_IsPaid
                       FROM C_Invoice ci
                      INNER JOIN C_InvoicePaySchedule cs ON (cs.C_Invoice_ID = ci.C_Invoice_ID
                                                              AND cs.IsActive = 'Y'
                                                              AND cs.DueAmt > 0)
                      WHERE ci.IsSOTrx = 'N'
                        AND ci.IsExpenseInvoice = 'N'
                        AND ci.DocStatus IN ('CO', 'CL')
                        AND ci.IsActive = 'Y'
                        AND ci.AD_Client_ID = @ClientID";

            baseQuery = MRole.GetDefault(ctx).AddAccessSQL(baseQuery, "ci", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            // Aging buckets (past-due only, unpaid installments):
            //   0–30d  : DueDate in [TRUNC(CURRENT_DATE)-30 , TRUNC(CURRENT_DATE))
            //   31–60d : DueDate in [TRUNC(CURRENT_DATE)-60 , TRUNC(CURRENT_DATE)-30)
            //   61–90d : DueDate in [TRUNC(CURRENT_DATE)-90 , TRUNC(CURRENT_DATE)-60)
            //   90+d   : DueDate < TRUNC(CURRENT_DATE)-90
            // Payment health:
            //   PaidAmount    : SUM of DueAmt where VA009_IsPaid = 'Y'
            //   PendingAmount : SUM of DueAmt where VA009_IsPaid = 'N' AND DueDate >= today (or NULL)
            //   OverdueAmount : SUM of DueAmt where VA009_IsPaid = 'N' AND DueDate < today
            strQuery = @"WITH InvoiceData AS (
" + baseQuery + @"
)
SELECT COUNT(CASE WHEN id.VA009_IsPaid = 'N'
                       AND id.DueDate IS NOT NULL
                       AND id.DueDate < TRUNC(CURRENT_DATE) THEN 1 END) AS OverdueCount,
       COALESCE(SUM(CASE WHEN id.VA009_IsPaid = 'Y' THEN id.DueAmt ELSE 0 END), 0) AS PaidAmount,
       COALESCE(SUM(CASE WHEN id.VA009_IsPaid = 'N'
                          AND (id.DueDate IS NULL OR id.DueDate >= TRUNC(CURRENT_DATE))
                         THEN id.DueAmt ELSE 0 END), 0) AS PendingAmount,
       COALESCE(SUM(CASE WHEN id.VA009_IsPaid = 'N'
                          AND id.DueDate IS NOT NULL
                          AND id.DueDate < TRUNC(CURRENT_DATE)
                         THEN id.DueAmt ELSE 0 END), 0) AS OverdueAmount,
       COALESCE(SUM(CASE WHEN id.VA009_IsPaid = 'N'
                          AND id.DueDate IS NOT NULL
                          AND id.DueDate >= TRUNC(CURRENT_DATE) - 30
                          AND id.DueDate < TRUNC(CURRENT_DATE)
                         THEN id.DueAmt ELSE 0 END), 0) AS Bucket0to30,
       COUNT(CASE WHEN id.VA009_IsPaid = 'N'
                   AND id.DueDate IS NOT NULL
                   AND id.DueDate >= TRUNC(CURRENT_DATE) - 30
                   AND id.DueDate < TRUNC(CURRENT_DATE) THEN 1 END) AS Count0to30,
       COALESCE(SUM(CASE WHEN id.VA009_IsPaid = 'N'
                          AND id.DueDate IS NOT NULL
                          AND id.DueDate >= TRUNC(CURRENT_DATE) - 60
                          AND id.DueDate < TRUNC(CURRENT_DATE) - 30
                         THEN id.DueAmt ELSE 0 END), 0) AS Bucket31to60,
       COUNT(CASE WHEN id.VA009_IsPaid = 'N'
                   AND id.DueDate IS NOT NULL
                   AND id.DueDate >= TRUNC(CURRENT_DATE) - 60
                   AND id.DueDate < TRUNC(CURRENT_DATE) - 30 THEN 1 END) AS Count31to60,
       COALESCE(SUM(CASE WHEN id.VA009_IsPaid = 'N'
                          AND id.DueDate IS NOT NULL
                          AND id.DueDate >= TRUNC(CURRENT_DATE) - 90
                          AND id.DueDate < TRUNC(CURRENT_DATE) - 60
                         THEN id.DueAmt ELSE 0 END), 0) AS Bucket61to90,
       COUNT(CASE WHEN id.VA009_IsPaid = 'N'
                   AND id.DueDate IS NOT NULL
                   AND id.DueDate >= TRUNC(CURRENT_DATE) - 90
                   AND id.DueDate < TRUNC(CURRENT_DATE) - 60 THEN 1 END) AS Count61to90,
       COALESCE(SUM(CASE WHEN id.VA009_IsPaid = 'N'
                          AND id.DueDate IS NOT NULL
                          AND id.DueDate < TRUNC(CURRENT_DATE) - 90
                         THEN id.DueAmt ELSE 0 END), 0) AS Bucket90Plus,
       COUNT(CASE WHEN id.VA009_IsPaid = 'N'
                   AND id.DueDate IS NOT NULL
                   AND id.DueDate < TRUNC(CURRENT_DATE) - 90 THEN 1 END) AS Count90Plus
  FROM InvoiceData id";

            DataSet ds = DB.ExecuteDataset(strQuery, dataParams, null);
            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                DataRow row = ds.Tables[0].Rows[0];
                result.OverdueCount  = Util.GetValueOfInt(row["OverdueCount"]);
                result.PaidAmount    = Util.GetValueOfDecimal(row["PaidAmount"]);
                result.PendingAmount = Util.GetValueOfDecimal(row["PendingAmount"]);
                result.OverdueAmount = Util.GetValueOfDecimal(row["OverdueAmount"]);
                result.Bucket0to30   = Util.GetValueOfDecimal(row["Bucket0to30"]);
                result.Count0to30    = Util.GetValueOfInt(row["Count0to30"]);
                result.Bucket31to60  = Util.GetValueOfDecimal(row["Bucket31to60"]);
                result.Count31to60   = Util.GetValueOfInt(row["Count31to60"]);
                result.Bucket61to90  = Util.GetValueOfDecimal(row["Bucket61to90"]);
                result.Count61to90   = Util.GetValueOfInt(row["Count61to90"]);
                result.Bucket90Plus  = Util.GetValueOfDecimal(row["Bucket90Plus"]);
                result.Count90Plus   = Util.GetValueOfInt(row["Count90Plus"]);
            }

            return result;
        }

        public class PayableAgingKpiResult
        {
            public int     OverdueCount  { get; set; }
            public decimal PaidAmount    { get; set; }
            public decimal PendingAmount { get; set; }
            public decimal OverdueAmount { get; set; }
            public decimal Bucket0to30   { get; set; }
            public int     Count0to30    { get; set; }
            public decimal Bucket31to60  { get; set; }
            public int     Count31to60   { get; set; }
            public decimal Bucket61to90  { get; set; }
            public int     Count61to90   { get; set; }
            public decimal Bucket90Plus  { get; set; }
            public int     Count90Plus   { get; set; }
            public string  CurSymbol     { get; set; }
            public int     StdPrecision  { get; set; }
            public string  CurIso        { get; set; }
        }
    }
}
