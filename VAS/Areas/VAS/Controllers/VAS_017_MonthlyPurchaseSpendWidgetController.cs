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
        /// fiscal year (April–March cycle). Uses 2 DB round-trips: currency + CTE aggregation.
        /// MRole is applied only to the join-free C_Invoice base query inside the CTE.
        /// FY month mapping: Apr=1, May=2, … Dec=9, Jan=10, Feb=11, Mar=12.
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
            DateTime now = DateTime.Now;

            // Fiscal year start year: if current month >= April the FY started this calendar year.
            int fyStartYear = now.Month >= 4 ? now.Year : now.Year - 1;
            result.FyLabel = "FY " + fyStartYear + "–" + (fyStartYear + 1).ToString().Substring(2);

            // Round-trip 1 — functional currency from accounting schema
            int schemaCurrencyId = 0;
            strQuery = @"SELECT cs.C_Currency_ID, c.CurSymbol, c.StdPrecision
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
            }

            if (schemaCurrencyId == 0) { return result; }

            // Round-trip 2 — CTE aggregation for current and previous FY monthly totals.
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

            int fyPrev = fyStartYear - 1;
            int fyNext = fyStartYear + 1;

            // WHERE covers both FYs:
            //   Previous FY: Apr(fyPrev)–Mar(fyStartYear)
            //   Current  FY: Apr(fyStartYear)–Mar(fyNext)
            // FyType 'C' = current FY, 'P' = previous FY.
            // FyMonth = calendar month mapped to fiscal month (Apr=1 … Mar=12).
            strQuery = @"WITH MonthlyData AS (
                    SELECT inv.GrandTotal,
                           CASE WHEN (EXTRACT(YEAR FROM inv.DateAcct) = " + fyStartYear + @" AND EXTRACT(MONTH FROM inv.DateAcct) >= 4)
                                  OR (EXTRACT(YEAR FROM inv.DateAcct) = " + fyNext + @" AND EXTRACT(MONTH FROM inv.DateAcct) <= 3)
                                THEN 'C' ELSE 'P' END AS FyType,
                           CASE WHEN EXTRACT(MONTH FROM inv.DateAcct) >= 4
                                THEN EXTRACT(MONTH FROM inv.DateAcct) - 3
                                ELSE EXTRACT(MONTH FROM inv.DateAcct) + 9 END AS FyMonth
                      FROM (" + baseQuery + @") inv
                     WHERE ((EXTRACT(YEAR FROM inv.DateAcct) = " + fyPrev + @" AND EXTRACT(MONTH FROM inv.DateAcct) >= 4)
                         OR (EXTRACT(YEAR FROM inv.DateAcct) = " + fyStartYear + @")
                         OR (EXTRACT(YEAR FROM inv.DateAcct) = " + fyNext + @" AND EXTRACT(MONTH FROM inv.DateAcct) <= 3))
                )
                SELECT FyType, FyMonth, SUM(GrandTotal) AS TotalAmount
                  FROM MonthlyData
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
            public string    FyLabel      { get; set; }
            public decimal[] CurrentYear  { get; set; }
            public decimal[] LastYear     { get; set; }
        }
    }
}
