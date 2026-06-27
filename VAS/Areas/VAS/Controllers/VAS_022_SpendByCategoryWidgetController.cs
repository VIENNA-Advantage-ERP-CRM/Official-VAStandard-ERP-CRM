/************************************************************
 * Module Name    : VAS
 * Purpose        : Spend by Category (MTD) Widget
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
    public class VAS_022_SpendByCategoryWidgetController : Controller
    {
        string strQuery = "";

        [Authorize]
        public ActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// Returns purchase spend totals grouped by product category for the current month (MTD).
        /// Uses 2 DB round-trips: currency lookup + join-based category aggregation.
        /// MRole is applied only to the join-free C_Invoice base query (prevents AccessSqlParser OOM).
        /// The outer query joins C_InvoiceLine, M_Product, and M_Product_Category outside MRole scope.
        /// Top 12 categories by spend are returned.
        /// </summary>
        public JsonResult GetSpendByCategory()
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                SpendByCategoryResult result = BuildResult(ctx);
                retJSON = JsonConvert.SerializeObject(result);
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        private SpendByCategoryResult BuildResult(Ctx ctx)
        {
            var result = new SpendByCategoryResult
            {
                Categories = new List<CategorySpend>()
            };

            int clientId = ctx.GetAD_Client_ID();
            SqlParameter[] dataParams   = { new SqlParameter("@ClientID", clientId) };
            DateTime now = DateTime.Now;
            int currentYear  = now.Year;
            int currentMonth = now.Month;

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

            // Round-trip 2 — category aggregation for current month.
            // MRole applied only to C_Invoice base query (join-free) to avoid OOM in AccessSqlParser.
            // C_InvoiceLine, M_Product, M_Product_Category joins are done outside MRole scope.
            string baseQuery = @"SELECT i.C_Invoice_ID, i.C_Currency_ID, i.DateAcct, i.C_ConversionType_ID, i.AD_Client_ID, i.AD_Org_ID, i.IsReturnTrx
                    FROM C_Invoice i
                   WHERE i.IsSOTrx = 'N'
                     AND i.IsExpenseInvoice = 'N'
                     AND i.DocStatus IN ('CO', 'CL')
                     AND i.IsActive = 'Y'
                     AND i.AD_Client_ID = @ClientID
                     AND EXTRACT(YEAR FROM i.DateAcct) = " + currentYear + @"
                     AND EXTRACT(MONTH FROM i.DateAcct) = " + currentMonth;

            baseQuery = MRole.GetDefault(ctx).AddAccessSQL(baseQuery, "i", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            strQuery = @"SELECT pc.Name AS CategoryName,
                       SUM(CASE WHEN base_i.IsReturnTrx = 'N'
                                THEN COALESCE(currencyConvert(il.LineNetAmt, base_i.C_Currency_ID, " + schemaCurrencyId + @", base_i.DateAcct, base_i.C_ConversionType_ID, base_i.AD_Client_ID, base_i.AD_Org_ID), 0)
                                ELSE -COALESCE(currencyConvert(il.LineNetAmt, base_i.C_Currency_ID, " + schemaCurrencyId + @", base_i.DateAcct, base_i.C_ConversionType_ID, base_i.AD_Client_ID, base_i.AD_Org_ID), 0)
                                END) AS TotalAmount
                  FROM (" + baseQuery + @") base_i
                 INNER JOIN C_InvoiceLine il ON (il.C_Invoice_ID = base_i.C_Invoice_ID)
                 INNER JOIN M_Product p ON (il.M_Product_ID = p.M_Product_ID)
                 INNER JOIN M_Product_Category pc ON (p.M_Product_Category_ID = pc.M_Product_Category_ID)
                 WHERE il.IsActive = 'Y'
                   AND p.IsActive = 'Y'
                   AND pc.IsActive = 'Y'
                 GROUP BY pc.Name
                HAVING SUM(CASE WHEN base_i.IsReturnTrx = 'N'
                                THEN COALESCE(currencyConvert(il.LineNetAmt, base_i.C_Currency_ID, " + schemaCurrencyId + @", base_i.DateAcct, base_i.C_ConversionType_ID, base_i.AD_Client_ID, base_i.AD_Org_ID), 0)
                                ELSE -COALESCE(currencyConvert(il.LineNetAmt, base_i.C_Currency_ID, " + schemaCurrencyId + @", base_i.DateAcct, base_i.C_ConversionType_ID, base_i.AD_Client_ID, base_i.AD_Org_ID), 0)
                                END) > 0
                 ORDER BY TotalAmount DESC";

            DataSet ds = DB.ExecuteDataset(strQuery, dataParams, null);
            if (ds != null && ds.Tables.Count > 0)
            {
                int rowCount = 0;
                foreach (DataRow row in ds.Tables[0].Rows)
                {
                    if (rowCount >= 12) { break; }
                    result.Categories.Add(new CategorySpend
                    {
                        Name   = Util.GetValueOfString(row["CategoryName"]),
                        Amount = Util.GetValueOfDecimal(row["TotalAmount"])
                    });
                    rowCount++;
                }
            }

            return result;
        }

        public class SpendByCategoryResult
        {
            public string            CurSymbol    { get; set; }
            public int               StdPrecision { get; set; }
            public List<CategorySpend> Categories { get; set; }
        }

        public class CategorySpend
        {
            public string  Name   { get; set; }
            public decimal Amount { get; set; }
        }
    }
}
