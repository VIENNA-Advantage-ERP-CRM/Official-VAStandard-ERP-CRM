using System;
using System.Data;
using System.Web.Mvc;
using System.Data.SqlClient;
using Newtonsoft.Json;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VIS.Controllers
{
    /// <summary>
    /// Module Name : VAS_182_SparesConsumablesIssuesWidget
    /// Purpose     : Supplies the KPI metric percentage share of material issue value classified for Spares / Consumables Month-to-Date (MTD).
    /// Chronological development:
    ///   AI-Dev      2026-08-02 Created
    ///   Agent A04   2026-08-19 Added GetCurrencyInfo endpoint & currency formatting support
    ///   Claude      2026-09-18 Reclassified Spares/Consumables via Product Category
    ///                          (M_Product_Category.ProductGroup = 'C') instead of the
    ///                          "not linked to a work order" complement of VAS_181 -- that
    ///                          heuristic pushed this KPI to ~99% on installs where few
    ///                          internal-use lines carry a work order link at all.
    /// </summary>
    public class VAS_182_SparesConsumablesIssuesWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_182_SparesConsumablesIssuesWidgetController).FullName);

// ===== NEW CODE START — currency format (agent A04, 2026-08-19) =====
        /// <summary>Returns currency info (iso code and currency symbol) for the current context.</summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetCurrencyInfo()
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null) { return Json("", JsonRequestBehavior.AllowGet); }

            try
            {
                var currencyInfo = GetCurrencyInfoData(ctx);
                string json = JsonConvert.SerializeObject(currencyInfo);
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_182_SparesConsumablesIssuesWidget.GetCurrencyInfo", ex);
                string json = JsonConvert.SerializeObject(new { iso = "", symbol = "" });
                return Json(json, JsonRequestBehavior.AllowGet);
            }
        }

        private dynamic GetCurrencyInfoData(Ctx ctx)
        {
            int currencyId = ctx.GetContextAsInt("$C_Currency_ID");
            if (currencyId <= 0)
            {
                currencyId = ctx.GetContextAsInt("#C_Currency_ID");
            }

            string iso = "";
            string symbol = "";

            if (currencyId > 0)
            {
                string sql = "SELECT ISO_Code, COALESCE(CurSymbol, ISO_Code) AS CurSymbol FROM C_Currency WHERE C_Currency_ID = @p1 AND IsActive = 'Y'";
                SqlParameter[] param = new SqlParameter[] { new SqlParameter("@p1", currencyId) };
                using (IDataReader dr = DB.ExecuteReader(sql, param, null))
                {
                    if (dr != null && dr.Read())
                    {
                        iso = Util.GetValueOfString(dr["ISO_Code"]);
                        symbol = Util.GetValueOfString(dr["CurSymbol"]);
                    }
                }
            }

            if (string.IsNullOrEmpty(iso))
            {
                string sql = @"SELECT c.ISO_Code, COALESCE(c.CurSymbol, c.ISO_Code) AS CurSymbol 
                               FROM C_AcctSchema a 
                               INNER JOIN C_Currency c ON (c.C_Currency_ID = a.C_Currency_ID) 
                               WHERE a.AD_Client_ID = @p1 AND a.IsActive = 'Y'";
                SqlParameter[] param = new SqlParameter[] { new SqlParameter("@p1", ctx.GetAD_Client_ID()) };
                using (IDataReader dr = DB.ExecuteReader(sql, param, null))
                {
                    if (dr != null && dr.Read())
                    {
                        iso = Util.GetValueOfString(dr["ISO_Code"]);
                        symbol = Util.GetValueOfString(dr["CurSymbol"]);
                    }
                }
            }

            return new { iso = iso, symbol = symbol };
        }

        /// <summary>
        /// Returns the percentage share of MTD issued value for spares/consumables
        /// purpose, plus the same month-window boundaries as DB-ready SQL date
        /// literals (monthStartSql/nextMonthStartSql via <see cref="ToSqlDate"/>) -
        /// the widget's own click-through reuses these verbatim in its
        /// TabWhereClause instead of reconstructing the month window with
        /// Oracle-only SYSDATE/TRUNC/ADD_MONTHS syntax (broke the drill-through on
        /// this install's actual Postgres backend - stuck on loading, never
        /// actually filtered - the same class of bug VAS_140/VAS_181 already hit).
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetSparesConsumablesPercentage()
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null) { return Json("", JsonRequestBehavior.AllowGet); }

            try
            {
                DateTime now = DateTime.Now;
                DateTime monthStart = new DateTime(now.Year, now.Month, 1);
                DateTime nextMonthStart = monthStart.AddMonths(1);

                int percentage = GetSparesConsumablesPercentageData(ctx, monthStart, nextMonthStart);
                string json = JsonConvert.SerializeObject(new
                {
                    percentage = percentage,
                    monthStartSql = ToSqlDate(monthStart),
                    nextMonthStartSql = ToSqlDate(nextMonthStart),
                    success = true
                });
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_182_SparesConsumablesIssuesWidget.GetSparesConsumablesPercentage", ex);
                string json = JsonConvert.SerializeObject(new { error = Msg.GetMsg(ctx, "Error") ?? "Error" });
                return Json(json, JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Every M_Inventory_ID matching the MTD spares/consumables predicate
        /// (capped at <see cref="MaxZoomIds"/>) - the click-through builds its
        /// TabWhereClause as a flat M_Inventory.M_Inventory_ID IN (...) list from
        /// this, instead of a correlated EXISTS(SELECT 1 FROM M_InventoryLine ...)
        /// subquery. The grid's own "duplicate DocumentNo" diagnostic query does
        /// naive, parenthesis-unaware text surgery on the TabWhereClause looking
        /// for a FROM it can lift out - it mishandled the nested EXISTS(...) and
        /// sent Oracle malformed SQL (ORA-00933), the same bug VAS_181 hit and
        /// fixed the same way (confirmed directly in the app log).
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetSparesConsumablesIds()
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null) { return Json("", JsonRequestBehavior.AllowGet); }

            try
            {
                DateTime now = DateTime.Now;
                DateTime monthStart = new DateTime(now.Year, now.Month, 1);
                DateTime nextMonthStart = monthStart.AddMonths(1);

                var ids = GetSparesConsumablesIdsData(ctx, monthStart, nextMonthStart);
                string json = JsonConvert.SerializeObject(new { ids = ids, success = true });
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_182_SparesConsumablesIssuesWidget.GetSparesConsumablesIds", ex);
                string json = JsonConvert.SerializeObject(new { error = Msg.GetMsg(ctx, "Error") ?? "Error" });
                return Json(json, JsonRequestBehavior.AllowGet);
            }
        }

        private const int MaxZoomIds = 1000;

        private System.Collections.Generic.List<int> GetSparesConsumablesIdsData(Ctx ctx, DateTime monthStart, DateTime nextMonthStart)
        {
            var ids = new System.Collections.Generic.List<int>();
            if (ctx == null) { return ids; }

            string msl = ToSqlDate(monthStart);
            string nmsl = ToSqlDate(nextMonthStart);

            // Same population as GetSparesConsumablesPercentageData's SparesValue
            // branch (Product Category group 'C' + line-level NOT-work-order
            // classification), just DISTINCT header ids instead of a SUM.
            string sql = @"
                SELECT DISTINCT inv.M_Inventory_ID
                  FROM M_Inventory inv
                  INNER JOIN M_InventoryLine line ON ( line.M_Inventory_ID = inv.M_Inventory_ID )
                  INNER JOIN M_Product mp ON ( mp.M_Product_ID = line.M_Product_ID )
                  INNER JOIN M_Product_Category mpc ON ( mpc.M_Product_Category_ID = mp.M_Product_Category_ID )
                 WHERE inv.IsActive = 'Y'
                   AND mpc.ProductGroup = 'C'
                   AND line.IsActive = 'Y'
                   AND mp.IsActive = 'Y'
                   AND mpc.IsActive = 'Y'
                   AND COALESCE(inv.IsInternalUse, 'N') = 'Y'
                   AND inv.DocStatus IN ('CO', 'CL')
                   AND COALESCE(line.QtyInternalUse, 0) > 0
                   AND COALESCE(line.VA075_WorkOrder_ID, 0) = 0
                   AND COALESCE(line.VAMFG_M_WorkOrder_ID, 0) = 0
                   AND inv.MovementDate >= " + msl + @"
                   AND inv.MovementDate < " + nmsl;

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "inv", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            using (IDataReader dr = DB.ExecuteReader(sql, null, null))
            {
                while (dr != null && dr.Read())
                {
                    if (ids.Count >= MaxZoomIds) { break; }
                    ids.Add(Util.GetValueOfInt(dr["M_Inventory_ID"]));
                }
            }

            return ids;
        }

        private int GetSparesConsumablesPercentageData(Ctx ctx, DateTime monthStart, DateTime nextMonthStart)
        {
            if (ctx == null) { return 0; }

            string msl = ToSqlDate(monthStart);
            string nmsl = ToSqlDate(nextMonthStart);

            // Spares / consumables share = value of issue lines for products whose Product
            // Category is in the "Consumables/Spares" group (M_Product_Category.ProductGroup
            // = 'C'), as a share of the SAME group's total issued value for the period.
            //
            // Previously this widget classified "spares" as "any line NOT raised against a
            // work order" -- an exact complement of VAS_181_ProductionIssuesWidget with no
            // actual product classification behind it. On installs where few internal-use
            // lines carry a work order link at all, that heuristic pushed this KPI to ~99-100%
            // regardless of what was actually issued. Product Category is the real source of
            // truth for what counts as a spare/consumable part.
            //
            // Cost fallback must end in 0: NVL(CurrentCostPrice, PriceCost) yields NULL when both
            // are null, and SUM() silently drops those lines from the total.
            string sql = @"
                SELECT
                  COALESCE(SUM(CASE WHEN COALESCE(line.VA075_WorkOrder_ID, 0) = 0
                                     AND COALESCE(line.VAMFG_M_WorkOrder_ID, 0) = 0
                                    THEN (line.QtyInternalUse * COALESCE(line.CurrentCostPrice, line.PriceCost, line.VA024_CostPrice, 0))
                                    ELSE 0 END), 0) AS SparesValue,
                  COALESCE(SUM(line.QtyInternalUse * COALESCE(line.CurrentCostPrice, line.PriceCost, line.VA024_CostPrice, 0)), 0) AS TotalValue
                FROM M_Inventory inv
                INNER JOIN M_InventoryLine line ON ( line.M_Inventory_ID = inv.M_Inventory_ID )
                INNER JOIN M_Product mp ON ( mp.M_Product_ID = line.M_Product_ID )
                INNER JOIN M_Product_Category mpc ON ( mpc.M_Product_Category_ID = mp.M_Product_Category_ID )
                WHERE inv.IsActive = 'Y'
                  AND mpc.ProductGroup = 'C'
                  AND inv.DocStatus IN ('CO', 'CL')
                  AND COALESCE(inv.IsInternalUse, 'N') = 'Y'
                  AND line.IsActive = 'Y'
                  AND mp.IsActive = 'Y'
                  AND mpc.IsActive = 'Y'
                  AND COALESCE(line.QtyInternalUse, 0) > 0
                  AND inv.MovementDate >= " + msl + @"
                  AND inv.MovementDate < " + nmsl;

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "inv", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            decimal sparesVal = 0;
            decimal totalVal = 0;

            using (System.Data.IDataReader dr = DB.ExecuteReader(sql, null, null))
            {
                if (dr != null && dr.Read())
                {
                    sparesVal = Util.GetValueOfDecimal(dr["SparesValue"]);
                    totalVal = Util.GetValueOfDecimal(dr["TotalValue"]);
                }
            }

            if (totalVal <= 0) { return 0; }
            decimal pct = (sparesVal / totalVal) * 100m;
            return Convert.ToInt32(Math.Round(pct));
        }
// ----- END OLD CODE -----
    
        /// <summary>Date literal for the target DB. Merged in from upstream/beta, which
        /// introduced the msl/nmsl date-literal style this controller now uses.</summary>
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
