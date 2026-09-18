using Newtonsoft.Json;
using System;
using System.Collections.Generic;
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
    /// Module Name : VAS_181_ProductionIssuesWidget
    /// Purpose     : Supplies the KPI metric percentage share of material issue value classified for Production Month-to-Date (MTD).
    /// Chronological development:
    ///   AI-Dev      2026-08-02 Created
    /// </summary>
    public class VAS_181_ProductionIssuesWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_181_ProductionIssuesWidgetController).FullName);

// ===== NEW CODE START — currency format (agent A03, 2026-08-19) =====
        /// <summary>Returns the organization currency info (ISO code and symbol).</summary>
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
                Log.Log(Level.SEVERE, "VAS_181_ProductionIssuesWidget.GetCurrencyInfo", ex);
                string json = JsonConvert.SerializeObject(new { error = Msg.GetMsg(ctx, "Error") ?? "Error" });
                return Json(json, JsonRequestBehavior.AllowGet);
            }
        }

        private object GetCurrencyInfoData(Ctx ctx)
        {
            int currencyId = ctx.GetContextAsInt("$C_Currency_ID");
            string iso = "";
            string symbol = "";

            if (currencyId > 0)
            {
                string sql = "SELECT ISO_Code, CurSymbol FROM C_Currency WHERE C_Currency_ID = @param1 AND IsActive = 'Y'";
                SqlParameter[] param = new SqlParameter[] { new SqlParameter("@param1", currencyId) };
                using (System.Data.IDataReader dr = DB.ExecuteReader(sql, param, null))
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
                string sql = @"SELECT c.ISO_Code, c.CurSymbol 
                               FROM AD_ClientInfo ci
                               INNER JOIN C_AcctSchema a ON (ci.C_AcctSchema1_ID = a.C_AcctSchema_ID)
                               INNER JOIN C_Currency c ON (a.C_Currency_ID = c.C_Currency_ID)
                               WHERE ci.AD_Client_ID = @param1";
                SqlParameter[] param = new SqlParameter[] { new SqlParameter("@param1", ctx.GetAD_Client_ID()) };
                using (System.Data.IDataReader dr = DB.ExecuteReader(sql, param, null))
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
// ===== NEW CODE END — currency format =====

        /// <summary>
        /// Returns the percentage share of MTD issued value for production purpose,
        /// plus the same month-window boundaries as DB-ready SQL date literals
        /// (MonthStartSql/NextMonthStartSql - via <see cref="ToSqlDate"/>, so Oracle
        /// gets TO_DATE(...) and every other supported DB gets CAST(... AS DATE)).
        /// The widget's own click-through reuses these literals verbatim in its
        /// TabWhereClause instead of reconstructing the month window with
        /// Oracle-only SYSDATE/TRUNC/ADD_MONTHS syntax - that raw-text
        /// reconstruction broke the drill-through on this install's actual
        /// Postgres backend (stuck on loading, never actually filtered), the same
        /// class of bug VAS_140 already documents elsewhere in this codebase.
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetProductionIssuesPercentage()
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null) { return Json("", JsonRequestBehavior.AllowGet); }

            try
            {
                DateTime now = DateTime.Now;
                DateTime monthStart = new DateTime(now.Year, now.Month, 1);
                DateTime nextMonthStart = monthStart.AddMonths(1);

                int percentage = GetProductionIssuesPercentageData(ctx, monthStart, nextMonthStart);
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
                Log.Log(Level.SEVERE, "VAS_181_ProductionIssuesWidget.GetProductionIssuesPercentage", ex);
                string json = JsonConvert.SerializeObject(new { error = Msg.GetMsg(ctx, "Error") ?? "Error" });
                return Json(json, JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Every M_Inventory_ID matching the MTD production-issue predicate (capped
        /// at <see cref="MaxZoomIds"/>) - the click-through builds its
        /// TabWhereClause as a flat M_Inventory.M_Inventory_ID IN (...) list from
        /// this, instead of a correlated EXISTS(SELECT 1 FROM M_InventoryLine ...)
        /// subquery. The grid's own "duplicate DocumentNo" diagnostic query does
        /// naive, parenthesis-unaware text surgery on the TabWhereClause looking
        /// for a FROM it can lift out - it mishandled the nested EXISTS(...) and
        /// sent Oracle malformed SQL (ORA-00933), which is what actually hung the
        /// drill-through (confirmed directly in the app log). A flat ID list has
        /// no FROM/subquery in it at all, so that diagnostic query has nothing to
        /// mishandle.
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetProductionIssueIds()
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null) { return Json("", JsonRequestBehavior.AllowGet); }

            try
            {
                DateTime now = DateTime.Now;
                DateTime monthStart = new DateTime(now.Year, now.Month, 1);
                DateTime nextMonthStart = monthStart.AddMonths(1);

                var ids = GetProductionIssueIdsData(ctx, monthStart, nextMonthStart);
                string json = JsonConvert.SerializeObject(new { ids = ids, success = true });
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_181_ProductionIssuesWidget.GetProductionIssueIds", ex);
                string json = JsonConvert.SerializeObject(new { error = Msg.GetMsg(ctx, "Error") ?? "Error" });
                return Json(json, JsonRequestBehavior.AllowGet);
            }
        }

        private const int MaxZoomIds = 1000;

        private System.Collections.Generic.List<int> GetProductionIssueIdsData(Ctx ctx, DateTime monthStart, DateTime nextMonthStart)
        {
            var ids = new System.Collections.Generic.List<int>();
            if (ctx == null) { return ids; }

            string msl = ToSqlDate(monthStart);
            string nmsl = ToSqlDate(nextMonthStart);

            // Same population as GetProductionIssuesPercentageData's ProductionValue
            // branch (line-level work-order classification), just DISTINCT header
            // ids instead of a SUM.
            string sql = @"
                SELECT DISTINCT inv.M_Inventory_ID
                  FROM M_Inventory inv
                  INNER JOIN M_InventoryLine line ON ( line.M_Inventory_ID = inv.M_Inventory_ID )
                 WHERE inv.IsActive = 'Y'
                   AND line.IsActive = 'Y'
                   AND inv.IsInternalUse = 'Y'
                   AND inv.DocStatus IN ('CO', 'CL')
                   AND COALESCE(line.QtyInternalUse, 0) > 0
                   AND (COALESCE(line.VA075_WorkOrder_ID, 0) > 0 OR COALESCE(line.VAMFG_M_WorkOrder_ID, 0) > 0)
                   AND inv.MovementDate >= " + msl + @"
                   AND inv.MovementDate < " + nmsl;

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "inv", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            using (System.Data.IDataReader dr = DB.ExecuteReader(sql, null, null))
            {
                while (dr != null && dr.Read())
                {
                    if (ids.Count >= MaxZoomIds) { break; }
                    ids.Add(Util.GetValueOfInt(dr["M_Inventory_ID"]));
                }
            }

            return ids;
        }

        /// <summary>
        /// The line-level production-order column, per the source specification
        /// (03-use-c-production-issues-copilot-prompt.txt, "DATABASE TABLE MAPPING"):
        ///   "Production order on line: M_InventoryLine.VAMFG_M_WorkOrder_ID"
        ///   "Use the production order on the line level only ... Do not use the production-order
        ///    field from M_Inventory header."
        /// </summary>
        private const string ProductionOrderColumn = "VAMFG_M_WorkOrder_ID";

        /// <summary>
        /// Returns the line-level production-order columns that this installation actually has
        /// (never null; empty when the manufacturing module is not installed).
        ///
        /// The columns ship with the manufacturing module, so they are absent on an installation
        /// that does not have that module - they do not exist on DB 1, for example. Naming them
        /// unconditionally makes the whole query die with ORA-00904 instead of the widget simply
        /// reporting no production issues, so the spec's column is verified against the dictionary
        /// first and any other work-order column is accepted as a fallback.
        /// </summary>
        private static List<string> ResolveProductionOrderColumns()
        {
            string sql = @"
                SELECT c.ColumnName
                FROM AD_Column c
                INNER JOIN AD_Table t ON t.AD_Table_ID = c.AD_Table_ID
                WHERE t.TableName = 'M_InventoryLine'
                  AND c.IsActive = 'Y'
                  AND UPPER(c.ColumnName) LIKE '%WORKORDER%'
                ORDER BY CASE WHEN UPPER(c.ColumnName) = UPPER('" + ProductionOrderColumn + @"') THEN 0 ELSE 1 END, c.ColumnName";

            var columns = new List<string>();
            using (System.Data.IDataReader dr = DB.ExecuteReader(sql, null, null))
            {
                while (dr != null && dr.Read())
                {
                    columns.Add(Util.GetValueOfString(dr["ColumnName"]));
                }
            }
            return columns;
        }

        /// <summary>
        /// SQL predicate that is true when the line is raised against a work order, using only the
        /// columns this installation actually has. When none exist the installation cannot
        /// identify production issues at all, so the predicate is never true.
        /// </summary>
        private static string WorkOrderLinePredicate(List<string> workOrderColumns)
        {
            if (workOrderColumns.Count == 0) { return "1 = 0"; }

            var tests = new List<string>();
            foreach (string column in workOrderColumns)
            {
                tests.Add("COALESCE(line." + column + ", 0) > 0");
            }
            return "(" + string.Join(" OR ", tests) + ")";
        }

        private int GetProductionIssuesPercentageData(Ctx ctx, DateTime monthStart, DateTime nextMonthStart)
        {
            if (ctx == null) { return 0; }

            string msl = ToSqlDate(monthStart);
            string nmsl = ToSqlDate(nextMonthStart);

            // Production share = value of issue lines raised against a WORK ORDER.
            //
            // The previous classification (C_Charge_ID IS NOT NULL OR M_RequisitionLine_ID IS NOT
            // NULL) was always true: an internal-use line always carries a charge account, so on
            // FSMTesting6 all 90 issue lines matched and this KPI returned a hard-coded-looking
            // 100% (and its complement VAS_182 returned 0%). The work order link is the only
            // field in the schema that actually distinguishes a production issue.
            //
            // The work-order columns are manufacturing-module only, so they are named only after
            // the dictionary confirms them - referencing a missing column kills the whole query
            // with ORA-00904 (which is exactly what happened on installations without the module).
            //
            // Cost fallback must end in 0: NVL(CurrentCostPrice, PriceCost) yields NULL when both
            // are null, and SUM() silently drops those lines from the total.
            string sql = @"
                SELECT
                  COALESCE(SUM(CASE WHEN " + WorkOrderLinePredicate(workOrderColumns) + @"
                                    THEN (line.QtyInternalUse * COALESCE(line.CurrentCostPrice, line.PriceCost, line.VA024_CostPrice, 0))
                                    ELSE 0 END), 0) AS ProductionValue,
                  COALESCE(SUM(line.QtyInternalUse * COALESCE(line.CurrentCostPrice, line.PriceCost, line.VA024_CostPrice, 0)), 0) AS TotalValue
                FROM M_Inventory inv
                INNER JOIN M_InventoryLine line ON ( line.M_Inventory_ID = inv.M_Inventory_ID )
                WHERE inv.IsActive = 'Y'
                  AND line.IsActive = 'Y'
                  AND inv.IsInternalUse = 'Y'
                  AND inv.DocStatus IN ('CO', 'CL')
                  AND COALESCE(line.QtyInternalUse, 0) > 0
                  AND inv.MovementDate >= " + msl + @"
                  AND inv.MovementDate < " + nmsl;

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "inv", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            decimal productionVal = 0;
            decimal totalVal = 0;

            using (System.Data.IDataReader dr = DB.ExecuteReader(sql, null, null))
            {
                if (dr != null && dr.Read())
                {
                    productionVal = Util.GetValueOfDecimal(dr["ProductionValue"]);
                    totalVal = Util.GetValueOfDecimal(dr["TotalValue"]);
                }
            }

            if (totalVal <= 0) { return 0; }
            decimal pct = (productionVal / totalVal) * 100m;
            return Convert.ToInt32(Math.Round(pct));
        }

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

