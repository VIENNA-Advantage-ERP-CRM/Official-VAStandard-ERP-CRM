/************************************************************
 * Module Name    : VAS
 * Purpose        : Controller for Total Purchases (MTD) KPI Widget
 * chronological  : Development
 * Created Date   : 12 May 2026
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
    public class VAS_025_TotalPurchasesWidgetController : Controller
    {
        string strQuery = "";

        [Authorize]
        public ActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// Returns MTD/YTD purchase totals, invoice count, trend vs last month,
        /// currency info from accounting schema, and 7-month sparkline data.
        /// </summary>
        public JsonResult GetTotalPurchasesKpi()
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                PurchasesKpiResult result = BuildKpiResult(ctx);
                retJSON = JsonConvert.SerializeObject(result);
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        private PurchasesKpiResult BuildKpiResult(Ctx ctx)
        {
            PurchasesKpiResult result = new PurchasesKpiResult();
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
            // AD_Client_ID and AD_Org_ID added explicitly; AddAccessSQL covers role-level access.
            strQuery = @"SELECT SUM(CASE WHEN EXTRACT(YEAR FROM i.DateInvoiced) = " + currentYear + @"
                                    AND EXTRACT(MONTH FROM i.DateInvoiced) = " + currentMonth + @"
                               THEN COALESCE(currencyConvert(i.GrandTotal, i.C_Currency_ID, " + schemaCurrencyId + @", i.DateAcct, i.C_ConversionType_ID, i.AD_Client_ID, i.AD_Org_ID), 0)
                                    * CASE WHEN i.IsReturnTrx = 'Y' THEN -1 ELSE 1 END
                               ELSE 0 END) AS MtdTotal,
                         SUM(CASE WHEN EXTRACT(YEAR FROM i.DateInvoiced) = " + currentYear + @"
                               THEN COALESCE(currencyConvert(i.GrandTotal, i.C_Currency_ID, " + schemaCurrencyId + @", i.DateAcct, i.C_ConversionType_ID, i.AD_Client_ID, i.AD_Org_ID), 0)
                                    * CASE WHEN i.IsReturnTrx = 'Y' THEN -1 ELSE 1 END
                               ELSE 0 END) AS YtdTotal,
                         COUNT(CASE WHEN EXTRACT(YEAR FROM i.DateInvoiced) = " + currentYear + @"
                                    AND EXTRACT(MONTH FROM i.DateInvoiced) = " + currentMonth + @"
                               THEN 1 ELSE NULL END) AS InvoiceCount,
                         SUM(CASE WHEN EXTRACT(YEAR FROM i.DateInvoiced) = " + lastMonthYear + @"
                                    AND EXTRACT(MONTH FROM i.DateInvoiced) = " + lastMonthNum + @"
                               THEN COALESCE(currencyConvert(i.GrandTotal, i.C_Currency_ID, " + schemaCurrencyId + @", i.DateAcct, i.C_ConversionType_ID, i.AD_Client_ID, i.AD_Org_ID), 0)
                                    * CASE WHEN i.IsReturnTrx = 'Y' THEN -1 ELSE 1 END
                               ELSE 0 END) AS LastMonthTotal
                    FROM C_Invoice i
                   WHERE i.IsSOTrx = 'N'
                     AND i.IsExpenseInvoice = 'N'
                     AND i.DocStatus IN ('CO', 'CL')
                     AND i.IsActive = 'Y'
                     AND i.AD_Client_ID = @ClientID";

            strQuery = MRole.GetDefault(ctx).AddAccessSQL(strQuery, "i", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            DataSet ds = DB.ExecuteDataset(strQuery, dataParams, null);
            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                DataRow row = ds.Tables[0].Rows[0];
                result.MtdTotal = Util.GetValueOfDecimal(row["MtdTotal"]);
                result.YtdTotal = Util.GetValueOfDecimal(row["YtdTotal"]);
                result.InvoiceCount = Util.GetValueOfInt(row["InvoiceCount"]);
                result.LastMonthTotal = Util.GetValueOfDecimal(row["LastMonthTotal"]);
            }

            // Step 3 — Sparkline: monthly totals for last 7 months.
            // Arithmetic on EXTRACT avoids date literals (Oracle + Postgres safe).
            DateTime sevenMonthsAgo = now.AddMonths(-6);
            int sparkYear = sevenMonthsAgo.Year;
            int sparkMonth = sevenMonthsAgo.Month;

            strQuery = @"SELECT EXTRACT(YEAR FROM i.DateInvoiced) AS InvYear,
                         EXTRACT(MONTH FROM i.DateInvoiced) AS InvMonth,
                         SUM(COALESCE(currencyConvert(i.GrandTotal, i.C_Currency_ID, " + schemaCurrencyId + @", i.DateAcct, i.C_ConversionType_ID, i.AD_Client_ID, i.AD_Org_ID), 0)
                             * CASE WHEN i.IsReturnTrx = 'Y' THEN -1 ELSE 1 END) AS MonthlyTotal
                    FROM C_Invoice i
                   WHERE i.IsSOTrx = 'N'
                     AND i.IsExpenseInvoice = 'N'
                     AND i.DocStatus IN ('CO', 'CL')
                     AND i.IsActive = 'Y'
                     AND i.AD_Client_ID = @ClientID
                     AND (EXTRACT(YEAR FROM i.DateInvoiced) * 12 + EXTRACT(MONTH FROM i.DateInvoiced))
                         >= (" + sparkYear + " * 12 + " + sparkMonth + @") ";

            strQuery = MRole.GetDefault(ctx).AddAccessSQL(strQuery, "i", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            strQuery += @" GROUP BY EXTRACT(YEAR FROM i.DateInvoiced), EXTRACT(MONTH FROM i.DateInvoiced)
                           ORDER BY EXTRACT(YEAR FROM i.DateInvoiced), EXTRACT(MONTH FROM i.DateInvoiced)";

            DataSet sparkDs = DB.ExecuteDataset(strQuery, dataParams, null);
            if (sparkDs != null && sparkDs.Tables.Count > 0 && sparkDs.Tables[0].Rows.Count > 0)
            {
                for (int i = 0; i < sparkDs.Tables[0].Rows.Count; i++)
                {
                    result.SparklineData.Add(Util.GetValueOfDecimal(sparkDs.Tables[0].Rows[i]["MonthlyTotal"]));
                }
            }

            return result;
        }

        public class PurchasesKpiResult
        {
            public decimal MtdTotal { get; set; }
            public decimal YtdTotal { get; set; }
            public int InvoiceCount { get; set; }
            public decimal LastMonthTotal { get; set; }
            public string CurSymbol { get; set; }
            public int StdPrecision { get; set; }
            public List<decimal> SparklineData { get; set; }
        }
    }
}
