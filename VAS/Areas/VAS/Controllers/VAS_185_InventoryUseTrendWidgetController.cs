using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Web.Mvc;
using System.Data.SqlClient;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VIS.Controllers
{
    /// <summary>
    /// Module Name : VAS_185_InventoryUseTrendWidget
    /// Purpose     : Supplies monthly quantity, value, and document count buckets for 3M/6M/12M trend analysis.
    /// Chronological development:
    ///   AI-Dev      2026-08-02 Created
    ///   Claude      2026-09-24 Quantity (and the value calc's quantity multiplier) briefly
    ///                          switched to M_InventoryLine.QtyEntered, then reverted back to
    ///                          QtyInternalUse per explicit instruction - both TotalQty and
    ///                          TotalValue's multiplier and the WHERE filter all read
    ///                          QtyInternalUse again. Price simplified per a separate explicit
    ///                          instruction: plain COALESCE(line.CurrentCostPrice, 0), no
    ///                          NULLIF and no PriceCost/VA024_CostPrice/product-cost fallback
    ///                          chain - removed the now-unused ProductCurrentCostSql derived
    ///                          table and its join along with it.
    ///   Claude      2026-09-24 TotalValue is now explicitly run through CurrencyConvert into
    ///                          the tenant's base (primary accounting schema) currency, dated
    ///                          on each row's own M_Inventory.MovementDate - per explicit
    ///                          instruction. M_InventoryLine carries no C_Currency_ID of its
    ///                          own (cost values are schema-currency by ADempiere convention,
    ///                          same as every M_Cost-derived figure), so both the FROM and TO
    ///                          currency are the tenant's own accounting-schema currency -a
    ///                          safe identity conversion on the common single-schema install,
    ///                          but now structurally correct and consistently dated rather
    ///                          than an unconverted raw sum. GetCurrencyInfo no longer prefers
    ///                          the session's $C_Currency_ID: it always resolves the same
    ///                          accounting-schema currency the value is now guaranteed to be
    ///                          in, so the displayed symbol can never disagree with the figure.
    /// </summary>
    public class VAS_185_InventoryUseTrendWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_185_InventoryUseTrendWidgetController).FullName);

        /// <summary>Single-row tenant accounting (base) currency - id, precision, ISO, symbol.</summary>
        private const string SchemaCurrencySql = @"
            SELECT ci.AD_Client_ID AS AD_Client_ID,
                   cs.C_Currency_ID AS Acct_Currency_ID,
                   cur.StdPrecision AS Std_Precision,
                   cur.ISO_Code AS ISO_Code,
                   CASE WHEN cur.CurSymbol IS NOT NULL THEN cur.CurSymbol ELSE cur.ISO_Code END AS Cur_Symbol
            FROM AD_ClientInfo ci
            INNER JOIN C_AcctSchema cs ON (cs.C_AcctSchema_ID=ci.C_AcctSchema1_ID AND cs.IsActive = 'Y')
            INNER JOIN C_Currency cur ON (cur.C_Currency_ID=cs.C_Currency_ID AND cur.IsActive = 'Y')
            WHERE ci.IsActive = 'Y'
              AND ci.AD_Client_ID = @Client_ID";

        private class MonthBucket
        {
            public decimal qty;
            public decimal val;
            public int docs;
        }


        /// <summary>
        /// Three-letter month names for the chart axis.
        /// Deliberately NOT message keys: no VAS widget translates month names through AD_Message.
        /// Nine sibling widgets (VAS_161, VAS_165, VAS_183, VAS_184, VAS_186, VAS_188 among them)
        /// carry the same hardcoded array in JS, and AD_Message holds no month-name keys for VAS at
        /// all - only phrases like "This Month". Keeping the array matches that.
        /// </summary>
        private static readonly string[] MonthShortNames = new string[]
        {
            "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"
        };

        private static string GetMonthShortName(int month)
        {
            if (month < 1 || month > 12) { return ""; }
            return MonthShortNames[month - 1];
        }

        /// <summary>Returns monthly trend series for the specified rolling window (3, 6, or 12 months).</summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetTrendData(int months)
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null) { return Json("", JsonRequestBehavior.AllowGet); }

            int windowMonths = (months == 3 || months == 12) ? months : 6;
            DateTime now = DateTime.Now;
            DateTime endMonthStart = new DateTime(now.Year, now.Month, 1).AddMonths(1);
            DateTime startMonthStart = new DateTime(now.Year, now.Month, 1).AddMonths(-(windowMonths - 1));
            var dbDict = new Dictionary<string, MonthBucket>();

            try
            {
                string smsl = ToSqlDate(startMonthStart);
                string emsl = ToSqlDate(endMonthStart);

                // AddAccessSQL appends its predicate at the end of the statement, so it must be
                // applied to a plain SELECT (no GROUP BY / ORDER BY) where the alias is in scope.
                // AD_Org_ID is carried through for the CurrencyConvert call below.
                string invAccessSql = @"
                    SELECT inv.M_Inventory_ID, inv.MovementDate, inv.AD_Client_ID, inv.AD_Org_ID
                    FROM M_Inventory inv
                    WHERE inv.IsActive = 'Y'
                      AND inv.DocStatus IN ('CO', 'CL')
                      AND COALESCE(inv.IsInternalUse, 'N') = 'Y'
                      AND inv.MovementDate >= " + smsl + @"
                      AND inv.MovementDate < " + emsl;

                invAccessSql = MRole.GetDefault(ctx).AddAccessSQL(invAccessSql, "inv", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                // Price is M_InventoryLine.CurrentCostPrice directly (no NULLIF/fallback chain,
                // no product-cost lookup, per explicit instruction), run through CurrencyConvert
                // into the tenant's base accounting-schema currency, dated on each row's own
                // MovementDate rather than "today" - per explicit instruction.
                string sql = @"
                    WITH schema_currency AS (
                        " + SchemaCurrencySql + @"
                    )
                    SELECT
                      TO_CHAR(ai.MovementDate, 'YYYY-MM') AS MonthBucket,
                      SUM(line.QtyInternalUse) AS TotalQty,
                      SUM(CurrencyConvert(line.QtyInternalUse * COALESCE(line.CurrentCostPrice, 0), sc.Acct_Currency_ID, sc.Acct_Currency_ID, ai.MovementDate, 0, ai.AD_Client_ID, ai.AD_Org_ID)) AS TotalValue,
                      COUNT(DISTINCT ai.M_Inventory_ID) AS DocCount
                    FROM M_InventoryLine line
                    INNER JOIN (" + invAccessSql + @") ai ON ai.M_Inventory_ID = line.M_Inventory_ID
                    CROSS JOIN schema_currency sc
                    WHERE line.IsActive = 'Y'
                      AND COALESCE(line.QtyInternalUse, 0) > 0
                    GROUP BY TO_CHAR(ai.MovementDate, 'YYYY-MM')
                    ORDER BY MonthBucket ASC";

                using (IDataReader dr = DB.ExecuteReader(sql, new SqlParameter[] { new SqlParameter("@Client_ID", ctx.GetAD_Client_ID()) }, null))
                {
                    while (dr != null && dr.Read())
                    {
                        string bucket = Util.GetValueOfString(dr["MonthBucket"]);
                        dbDict[bucket] = new MonthBucket
                        {
                            qty = Util.GetValueOfDecimal(dr["TotalQty"]),
                            val = Util.GetValueOfDecimal(dr["TotalValue"]),
                            docs = Util.GetValueOfInt(dr["DocCount"])
                        };
                    }
                }

            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_185_InventoryUseTrendWidget.GetTrendData", ex);
                return Json(JsonConvert.SerializeObject(new { error = Msg.GetMsg(ctx, "Error") ?? "Error" }), JsonRequestBehavior.AllowGet);
            }

            {
                var series = new List<object>();

                for (int i = 0; i < windowMonths; i++)
                {
                    DateTime dt = startMonthStart.AddMonths(i);
                    string key = dt.ToString("yyyy-MM");
                    string labelName = GetMonthShortName(dt.Month) + (windowMonths == 12 ? (" '" + dt.ToString("yy")) : "");

                    decimal qty = 0;
                    decimal val = 0;
                    int docs = 0;

                    if (dbDict.ContainsKey(key))
                    {
                        qty = dbDict[key].qty;
                        val = dbDict[key].val;
                        docs = dbDict[key].docs;
                    }

                    series.Add(new
                    {
                        key = key,
                        label = labelName,
                        fullMonth = GetMonthShortName(dt.Month) + " " + dt.Year,
                        qty = qty,
                        val = val,
                        docs = docs
                    });
                }

// ===== NEW CODE START — currency format (agent A07, 2026-08-19) =====
                return Json(JsonConvert.SerializeObject(new { series = series, currency = GetCurrencyInfo(ctx), success = true }), JsonRequestBehavior.AllowGet);
// ===== NEW CODE END — currency format =====
// ----- OLD CODE (kept for rollback, do not delete) -----
//              return Json(JsonConvert.SerializeObject(new { series = series, success = true }), JsonRequestBehavior.AllowGet);
// ----- END OLD CODE -----
            }
        }

// ===== NEW CODE START — currency format (agent A07, 2026-08-19) =====
        /// <summary>
        /// Retrieves the tenant's base (primary accounting schema) currency - ISO code and
        /// symbol. Deliberately does NOT prefer the session's $C_Currency_ID: TotalValue is
        /// now always converted into this same accounting-schema currency (see GetTrendData),
        /// so the label shown here must always be that currency too, never a session
        /// preference the figure was never actually converted into.
        /// </summary>
        private object GetCurrencyInfo(Ctx ctx)
        {
            string iso = "";
            string symbol = "";
            if (ctx == null) { return new { iso = iso, symbol = symbol }; }

            int clientId = ctx.GetAD_Client_ID();
            IDataReader cdr = null;
            try
            {
                cdr = DB.ExecuteReader(
                    @"SELECT c.ISO_Code, c.CurSymbol
                      FROM AD_ClientInfo ci
                      INNER JOIN C_AcctSchema ac ON (ac.C_AcctSchema_ID = ci.C_AcctSchema1_ID)
                      INNER JOIN C_Currency c ON (c.C_Currency_ID = ac.C_Currency_ID)
                      WHERE ci.AD_Client_ID = @Client",
                    new SqlParameter[] { new SqlParameter("@Client", clientId) });
                if (cdr != null && cdr.Read())
                {
                    iso = Util.GetValueOfString(cdr["ISO_Code"]);
                    symbol = Util.GetValueOfString(cdr["CurSymbol"]);
                }
            }
            finally { if (cdr != null) { cdr.Close(); cdr.Dispose(); } }

            return new { iso = iso, symbol = symbol };
        }
// ===== NEW CODE END — currency format =====

        private static string ToSqlDate(DateTime date)
        {
            if (DB.IsOracle())
            {
                return "TO_DATE('" + date.ToString("yyyy-MM-dd") + "', 'YYYY-MM-DD')";
            }
            return "CAST('" + date.ToString("yyyy-MM-dd") + "' AS DATE)";
        }
    }
}
