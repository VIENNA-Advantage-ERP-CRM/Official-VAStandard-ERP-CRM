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

        /// <summary>
        /// Returns MTD material spend grouped by product category for the drill-down popup.
        /// </summary>
        public JsonResult GetPurchaseCategoryDrilldown()
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                PurchaseCategoryDrilldownResult result = BuildCategoryDrilldown(ctx);
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

        private PurchaseCategoryDrilldownResult BuildCategoryDrilldown(Ctx ctx)
        {
            var result = new PurchaseCategoryDrilldownResult
            {
                Categories = new List<PurchaseCategoryDrilldownRow>()
            };

            int clientId = ctx.GetAD_Client_ID();
            SqlParameter[] dataParams = { new SqlParameter("@ClientID", clientId) };
            ReportWindows w = GetReportWindows(ctx);

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

            string baseQuery = @"SELECT i.C_Invoice_ID, i.C_Currency_ID, i.DateAcct, i.C_ConversionType_ID,
                         i.AD_Client_ID, i.AD_Org_ID, i.IsReturnTrx, i.IsTaxIncluded
                    FROM C_Invoice i
                   WHERE i.IsSOTrx = 'N'
                     AND i.IsExpenseInvoice = 'N'
                     AND i.DocStatus IN ('CO', 'CL')
                     AND i.IsActive = 'Y'
                     AND i.AD_Client_ID = @ClientID
                     AND CAST(i.DateInvoiced AS DATE) BETWEEN DATE '" + w.MonthStart + "' AND DATE '" + w.MonthEnd + "'";

            baseQuery = MRole.GetDefault(ctx).AddAccessSQL(baseQuery, "i", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            // Net (tax-excluded) line value per category. For tax-inclusive invoices
            // (IsTaxIncluded = 'Y') LineNetAmt already contains the tax, so strip the
            // line tax to keep category bars on a true net basis - otherwise tax would
            // be double-counted inside the categories and the "Other" bucket would be
            // understated. (Process guide rule 3.4 / 7.3: do not rely on LineNetAmt as
            // net taxable amount when IsTaxIncluded = 'Y'.) Tax-exclusive lines are
            // unchanged.
            string netLineSql = @"CASE WHEN base_i.IsTaxIncluded = 'Y'
                                THEN (il.LineNetAmt - COALESCE(il.TaxAmt, 0))
                                ELSE il.LineNetAmt END";

            string amountSql = @"CASE WHEN base_i.IsReturnTrx = 'N'
                                THEN COALESCE(currencyConvert(" + netLineSql + @", base_i.C_Currency_ID, " + schemaCurrencyId + @", base_i.DateAcct, base_i.C_ConversionType_ID, base_i.AD_Client_ID, base_i.AD_Org_ID), 0)
                                ELSE -COALESCE(currencyConvert(" + netLineSql + @", base_i.C_Currency_ID, " + schemaCurrencyId + @", base_i.DateAcct, base_i.C_ConversionType_ID, base_i.AD_Client_ID, base_i.AD_Org_ID), 0)
                                END";

            string qtySql = @"CASE WHEN base_i.IsReturnTrx = 'N'
                                THEN COALESCE(il.QtyInvoiced, 0)
                                ELSE -COALESCE(il.QtyInvoiced, 0)
                                END";

            strQuery = @"SELECT pc.Name AS CategoryName,
                         SUM(" + amountSql + @") AS TotalAmount,
                         SUM(" + qtySql + @") AS TotalQty,
                         COUNT(il.C_InvoiceLine_ID) AS LineCount,
                         COUNT(DISTINCT base_i.C_Invoice_ID) AS InvoiceCount
                    FROM (" + baseQuery + @") base_i
                    INNER JOIN C_InvoiceLine il ON (il.C_Invoice_ID = base_i.C_Invoice_ID)
                    INNER JOIN M_Product p ON (il.M_Product_ID = p.M_Product_ID)
                    INNER JOIN M_Product_Category pc ON (p.M_Product_Category_ID = pc.M_Product_Category_ID)
                   WHERE il.IsActive = 'Y'
                     AND p.IsActive = 'Y'
                     AND pc.IsActive = 'Y'
                   GROUP BY pc.Name
                  HAVING SUM(" + amountSql + @") > 0
                   ORDER BY TotalAmount DESC";

            DataSet ds = DB.ExecuteDataset(strQuery, dataParams, null);
            if (ds != null && ds.Tables.Count > 0)
            {
                int rowCount = 0;
                foreach (DataRow row in ds.Tables[0].Rows)
                {
                    decimal amount = Util.GetValueOfDecimal(row["TotalAmount"]);
                    decimal qty = Util.GetValueOfDecimal(row["TotalQty"]);
                    int lineCount = Util.GetValueOfInt(row["LineCount"]);

                    result.TotalAmount += amount;
                    result.TotalQty += qty;
                    result.LineCount += lineCount;
                    result.CategoryCount++;

                    if (rowCount >= 12) { continue; }

                    var category = new PurchaseCategoryDrilldownRow
                    {
                        Name = Util.GetValueOfString(row["CategoryName"]),
                        Amount = amount,
                        Qty = qty,
                        LineCount = lineCount,
                        InvoiceCount = Util.GetValueOfInt(row["InvoiceCount"])
                    };

                    result.Categories.Add(category);
                    rowCount++;
                }
            }

            // Top spender = highest real product category (captured before the
            // reconciliation bucket is added, so the summary line never points at "Other").
            if (result.Categories.Count > 0)
            {
                result.TopName = result.Categories[0].Name;
                result.TopAmount = result.Categories[0].Amount;
            }

            decimal categorizedTotal = result.TotalAmount;

            // Reconcile to the Total Purchases (MTD) KPI card: that card sums invoice
            // GrandTotal (line net + tax + freight/charges) for every AP invoice in the
            // window, whereas the category rows above only cover net amounts of product
            // lines that carry a category. Pull the same GrandTotal-based total and the
            // full purchase-invoice count over the same window, then expose the remainder
            // (tax, charges, non-product / uncategorised lines) as one "Other" bucket so
            // the popup ties out to the card.
            decimal grandTotalMtd = 0;
            strQuery = @"SELECT COALESCE(SUM(
                            COALESCE(currencyConvert(i.GrandTotal, i.C_Currency_ID, " + schemaCurrencyId + @", i.DateAcct, i.C_ConversionType_ID, i.AD_Client_ID, i.AD_Org_ID), 0)
                            * CASE WHEN i.IsReturnTrx = 'Y' THEN -1 ELSE 1 END), 0) AS GrandTotalMtd,
                         COUNT(DISTINCT i.C_Invoice_ID) AS InvoiceCount
                    FROM C_Invoice i
                   WHERE i.IsSOTrx = 'N'
                     AND i.IsExpenseInvoice = 'N'
                     AND i.DocStatus IN ('CO', 'CL')
                     AND i.IsActive = 'Y'
                     AND i.AD_Client_ID = @ClientID
                     AND CAST(i.DateInvoiced AS DATE) BETWEEN DATE '" + w.MonthStart + "' AND DATE '" + w.MonthEnd + @"'";
            strQuery = MRole.GetDefault(ctx).AddAccessSQL(strQuery, "i", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            DataSet gtDs = DB.ExecuteDataset(strQuery, dataParams, null);
            if (gtDs != null && gtDs.Tables.Count > 0 && gtDs.Tables[0].Rows.Count > 0)
            {
                grandTotalMtd = Util.GetValueOfDecimal(gtDs.Tables[0].Rows[0]["GrandTotalMtd"]);
                result.InvoiceCount = Util.GetValueOfInt(gtDs.Tables[0].Rows[0]["InvoiceCount"]);
            }

            // Remainder bucket so the bars + total reconcile to the card.
            decimal otherAmount = grandTotalMtd - categorizedTotal;
            if (otherAmount > 0)
            {
                result.Categories.Add(new PurchaseCategoryDrilldownRow
                {
                    Name = "Other",
                    Amount = otherAmount,
                    IsOther = true
                });
            }

            // Total now equals the card's MTD purchases; order the bars by amount and
            // recompute each share against that reconciled total.
            result.TotalAmount = grandTotalMtd > 0 ? grandTotalMtd : categorizedTotal;
            result.Categories.Sort((a, b) => b.Amount.CompareTo(a.Amount));

            if (result.TotalAmount > 0)
            {
                foreach (PurchaseCategoryDrilldownRow category in result.Categories)
                {
                    category.Percent = Math.Round((category.Amount / result.TotalAmount) * 100, 1);
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
        public class PurchaseCategoryDrilldownResult
        {
            public decimal TotalAmount { get; set; }
            public int CategoryCount { get; set; }
            public int InvoiceCount { get; set; }
            public int LineCount { get; set; }
            public decimal TotalQty { get; set; }
            public string CurSymbol { get; set; }
            public int StdPrecision { get; set; }
            public string TopName { get; set; }
            public decimal TopAmount { get; set; }
            public List<PurchaseCategoryDrilldownRow> Categories { get; set; }
        }

        public class PurchaseCategoryDrilldownRow
        {
            public string Name { get; set; }
            public decimal Amount { get; set; }
            public decimal Percent { get; set; }
            public int InvoiceCount { get; set; }
            public int LineCount { get; set; }
            public decimal Qty { get; set; }
            public bool IsOther { get; set; }
        }
    }
}
