/************************************************************
 * Module Name    : VAS
 * Purpose        : Controller for Total Outstanding KPI Widget
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
    public class VAS_024_TotalOutstandingWidgetController : Controller
    {
        string strQuery = "";

        [Authorize]
        public ActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// Returns total outstanding AP balance, invoice count, vendor count,
        /// month-over-month trend data, currency info from accounting schema,
        /// and 7-month sparkline data.
        /// </summary>
        public JsonResult GetTotalOutstandingKpi()
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                OutstandingKpiResult result = BuildKpiResult(ctx);
                retJSON = JsonConvert.SerializeObject(result);
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        private OutstandingKpiResult BuildKpiResult(Ctx ctx)
        {
            OutstandingKpiResult result = new OutstandingKpiResult();
            result.SparklineData = new List<decimal>();

            int clientId = ctx.GetAD_Client_ID();
            SqlParameter[] dataParams   = { new SqlParameter("@ClientID", clientId) };
            DateTime now = DateTime.Now;
            int currentYear = now.Year;
            int currentMonth = now.Month;
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
                   WHERE ci.AD_Client_ID = @ClientID
                     AND ci.IsActive = 'Y'
                     AND cs.IsActive = 'Y'
                     AND c.IsActive = 'Y' ";

            strQuery = MRole.GetDefault(ctx).AddAccessSQL(strQuery, "cs", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            DataSet cDs = DB.ExecuteDataset(strQuery, dataParams, null);
            if (cDs != null && cDs.Tables.Count > 0 && cDs.Tables[0].Rows.Count > 0)
            {
                schemaCurrencyId = Util.GetValueOfInt(cDs.Tables[0].Rows[0]["C_Currency_ID"]);
                result.CurSymbol = Util.GetValueOfString(cDs.Tables[0].Rows[0]["CurSymbol"]);
                result.StdPrecision = Util.GetValueOfInt(cDs.Tables[0].Rows[0]["StdPrecision"]);
            }

            if (schemaCurrencyId == 0) { return result; }

            // Step 2 — KPI aggregation.
            // Flat CASE/WHEN with EXTRACT avoids nested subqueries that cause MRole parser
            // out-of-memory errors. EXTRACT(YEAR/MONTH FROM col) works in Oracle and Postgres.
            // OpenAmt > 0 identifies invoices with a remaining unpaid balance.
            // AD_Client_ID and AD_Org_ID added explicitly; AddAccessSQL covers role-level access.
            // Base query: join-free C_Invoice for MRole; C_InvoicePaySchedule joined in outer query.
            string baseOutstanding = @"SELECT i.C_Invoice_ID, i.C_Currency_ID, i.DateAcct, i.C_ConversionType_ID, i.AD_Client_ID, i.AD_Org_ID, i.DateInvoiced, i.C_BPartner_ID, i.IsReturnTrx
                    FROM C_Invoice i
                   WHERE i.IsSOTrx = 'N'
                     AND i.IsExpenseInvoice = 'N'
                     AND i.DocStatus IN ('CO', 'CL')
                     AND i.IsActive = 'Y'
                     AND i.AD_Client_ID = @ClientID";
            baseOutstanding = MRole.GetDefault(ctx).AddAccessSQL(baseOutstanding, "i", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            strQuery = @"SELECT SUM(CASE WHEN b.IsReturnTrx = 'N'
                                    THEN COALESCE(currencyConvert(ips.DueAmt, b.C_Currency_ID, " + schemaCurrencyId + @", b.DateAcct, b.C_ConversionType_ID, b.AD_Client_ID, b.AD_Org_ID), 0)
                                    ELSE -COALESCE(currencyConvert(ips.DueAmt, b.C_Currency_ID, " + schemaCurrencyId + @", b.DateAcct, b.C_ConversionType_ID, b.AD_Client_ID, b.AD_Org_ID), 0)
                                    END) AS OutstandingTotal,
                         SUM(CASE WHEN EXTRACT(YEAR FROM b.DateInvoiced) = " + currentYear + @"
                                   AND EXTRACT(MONTH FROM b.DateInvoiced) = " + currentMonth + @"
                              THEN CASE WHEN b.IsReturnTrx = 'N'
                                        THEN COALESCE(currencyConvert(ips.DueAmt, b.C_Currency_ID, " + schemaCurrencyId + @", b.DateAcct, b.C_ConversionType_ID, b.AD_Client_ID, b.AD_Org_ID), 0)
                                        ELSE -COALESCE(currencyConvert(ips.DueAmt, b.C_Currency_ID, " + schemaCurrencyId + @", b.DateAcct, b.C_ConversionType_ID, b.AD_Client_ID, b.AD_Org_ID), 0)
                                        END
                              ELSE 0 END) AS CurrentMonthOutstanding,
                         SUM(CASE WHEN EXTRACT(YEAR FROM b.DateInvoiced) = " + lastMonthYear + @"
                                   AND EXTRACT(MONTH FROM b.DateInvoiced) = " + lastMonthNum + @"
                              THEN CASE WHEN b.IsReturnTrx = 'N'
                                        THEN COALESCE(currencyConvert(ips.DueAmt, b.C_Currency_ID, " + schemaCurrencyId + @", b.DateAcct, b.C_ConversionType_ID, b.AD_Client_ID, b.AD_Org_ID), 0)
                                        ELSE -COALESCE(currencyConvert(ips.DueAmt, b.C_Currency_ID, " + schemaCurrencyId + @", b.DateAcct, b.C_ConversionType_ID, b.AD_Client_ID, b.AD_Org_ID), 0)
                                        END
                              ELSE 0 END) AS LastMonthOutstanding,
                         COUNT(DISTINCT b.C_Invoice_ID) AS InvoiceCount,
                         COUNT(DISTINCT b.C_BPartner_ID) AS VendorCount
                    FROM (" + baseOutstanding + @") b
                   INNER JOIN C_InvoicePaySchedule ips ON (ips.C_Invoice_ID = b.C_Invoice_ID)
                   WHERE ips.IsActive = 'Y'
                     AND ips.VA009_IsPaid = 'N'
                     AND ips.DueAmt > 0";

            DataSet ds = DB.ExecuteDataset(strQuery, dataParams, null);
            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                DataRow row = ds.Tables[0].Rows[0];
                result.OutstandingTotal = Util.GetValueOfDecimal(row["OutstandingTotal"]);
                result.CurrentMonthOutstanding = Util.GetValueOfDecimal(row["CurrentMonthOutstanding"]);
                result.LastMonthOutstanding = Util.GetValueOfDecimal(row["LastMonthOutstanding"]);
                result.InvoiceCount = Util.GetValueOfInt(row["InvoiceCount"]);
                result.VendorCount = Util.GetValueOfInt(row["VendorCount"]);
            }

            // Step 3 — Sparkline: monthly outstanding balances for last 7 months.
            // Uses EXTRACT arithmetic to avoid date literals (Oracle + Postgres safe).
            DateTime sevenMonthsAgo = now.AddMonths(-6);
            int sparkYear = sevenMonthsAgo.Year;
            int sparkMonth = sevenMonthsAgo.Month;

            strQuery = @"SELECT EXTRACT(YEAR FROM b.DateInvoiced) AS InvYear,
                         EXTRACT(MONTH FROM b.DateInvoiced) AS InvMonth,
                         SUM(CASE WHEN b.IsReturnTrx = 'N'
                                  THEN COALESCE(currencyConvert(ips.DueAmt, b.C_Currency_ID, " + schemaCurrencyId + @", b.DateAcct, b.C_ConversionType_ID, b.AD_Client_ID, b.AD_Org_ID), 0)
                                  ELSE -COALESCE(currencyConvert(ips.DueAmt, b.C_Currency_ID, " + schemaCurrencyId + @", b.DateAcct, b.C_ConversionType_ID, b.AD_Client_ID, b.AD_Org_ID), 0)
                                  END) AS MonthlyOutstanding
                    FROM (" + baseOutstanding + @") b
                   INNER JOIN C_InvoicePaySchedule ips ON (ips.C_Invoice_ID = b.C_Invoice_ID)
                   WHERE ips.IsActive = 'Y'
                     AND ips.VA009_IsPaid = 'N'
                     AND ips.DueAmt > 0
                     AND (EXTRACT(YEAR FROM b.DateInvoiced) * 12 + EXTRACT(MONTH FROM b.DateInvoiced))
                         >= (" + sparkYear + " * 12 + " + sparkMonth + @")
                   GROUP BY EXTRACT(YEAR FROM b.DateInvoiced), EXTRACT(MONTH FROM b.DateInvoiced)
                   ORDER BY EXTRACT(YEAR FROM b.DateInvoiced), EXTRACT(MONTH FROM b.DateInvoiced)";

            DataSet sparkDs = DB.ExecuteDataset(strQuery, dataParams, null);
            if (sparkDs != null && sparkDs.Tables.Count > 0 && sparkDs.Tables[0].Rows.Count > 0)
            {
                for (int i = 0; i < sparkDs.Tables[0].Rows.Count; i++)
                {
                    result.SparklineData.Add(Util.GetValueOfDecimal(sparkDs.Tables[0].Rows[i]["MonthlyOutstanding"]));
                }
            }

            return result;
        }

        public class OutstandingKpiResult
        {
            public decimal OutstandingTotal { get; set; }
            public decimal CurrentMonthOutstanding { get; set; }
            public decimal LastMonthOutstanding { get; set; }
            public int InvoiceCount { get; set; }
            public int VendorCount { get; set; }
            public string CurSymbol { get; set; }
            public int StdPrecision { get; set; }
            public List<decimal> SparklineData { get; set; }
        }
    }
}
