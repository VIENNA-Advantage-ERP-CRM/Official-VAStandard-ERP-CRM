using Newtonsoft.Json;
using System;
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

        /// <summary>Returns the percentage share of MTD issued value for production purpose.</summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetProductionIssuesPercentage()
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null) { return Json("", JsonRequestBehavior.AllowGet); }

            try
            {
                int percentage = GetProductionIssuesPercentageData(ctx);
                string json = JsonConvert.SerializeObject(new
                {
                    percentage = percentage,
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
        /// The line-level production-order column, per the source specification
        /// (03-use-c-production-issues-copilot-prompt.txt, "DATABASE TABLE MAPPING"):
        ///   "Production order on line: M_InventoryLine.VAMFG_M_WorkOrder_ID"
        ///   "Use the production order on the line level only ... Do not use the production-order
        ///    field from M_Inventory header."
        /// </summary>
        private const string ProductionOrderColumn = "VAMFG_M_WorkOrder_ID";

        /// <summary>
        /// Returns the line-level production-order column if this installation actually has it,
        /// otherwise null.
        ///
        /// The column ships with the manufacturing module, so it is absent on an installation that
        /// does not have that module - it does not exist on DB 1, for example. Naming it
        /// unconditionally makes the whole query die with ORA-00904 instead of the widget simply
        /// reporting no production issues, so the spec's column is verified against the dictionary
        /// first and any other work-order column is accepted as a fallback.
        /// </summary>
        private static string ResolveProductionOrderColumn()
        {
            string sql = @"
                SELECT MAX(c.ColumnName) KEEP (DENSE_RANK FIRST ORDER BY CASE WHEN UPPER(c.ColumnName) = UPPER('" + ProductionOrderColumn + @"') THEN 0 ELSE 1 END, c.ColumnName)
                FROM AD_Column c
                INNER JOIN AD_Table t ON t.AD_Table_ID = c.AD_Table_ID
                WHERE t.TableName = 'M_InventoryLine'
                  AND c.IsActive = 'Y'
                  AND UPPER(c.ColumnName) LIKE '%WORKORDER%'";

            string column = Util.GetValueOfString(DB.ExecuteScalar(sql, null, null));
            return string.IsNullOrEmpty(column) ? null : column;
        }

        private int GetProductionIssuesPercentageData(Ctx ctx)
        {
            if (ctx == null) { return 0; }

            DateTime now = DateTime.Now;
            DateTime monthStart = new DateTime(now.Year, now.Month, 1);
            DateTime nextMonthStart = monthStart.AddMonths(1);
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
            // Cost fallback must end in 0: NVL(CurrentCostPrice, PriceCost) yields NULL when both
            // are null, and SUM() silently drops those lines from the total.
            string sql = @"
                SELECT
                  COALESCE(SUM(CASE WHEN COALESCE(line.VA075_WorkOrder_ID, 0) > 0
                                      OR COALESCE(line.VAMFG_M_WorkOrder_ID, 0) > 0
                                    THEN (line.QtyInternalUse * COALESCE(line.CurrentCostPrice, line.PriceCost, line.VA024_CostPrice, 0))
                                    ELSE 0 END), 0) AS ProductionValue,
                  COALESCE(SUM(line.QtyInternalUse * COALESCE(line.CurrentCostPrice, line.PriceCost, line.VA024_CostPrice, 0)), 0) AS TotalValue
                FROM M_InventoryLine line
                INNER JOIN M_Inventory inv ON inv.M_Inventory_ID = line.M_Inventory_ID
                WHERE inv.IsActive = 'Y'
                  AND line.IsActive = 'Y'
                  AND inv.IsInternalUse = 'Y'
                  AND inv.DocStatus IN ('CO', 'CL')
                  AND COALESCE(inv.IsInternalUse, 'N') = 'Y'
                  AND line.IsActive = 'Y'
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

