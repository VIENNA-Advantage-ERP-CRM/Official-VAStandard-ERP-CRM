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

            string productionOrderColumn = ResolveProductionOrderColumn();
            if (productionOrderColumn == null)
            {
                // Nothing on the issue line records a production order, so no line can honestly be
                // classified as a production issue. Report 0 rather than inventing a proxy - the
                // previous "C_Charge_ID IS NOT NULL OR M_RequisitionLine_ID IS NOT NULL" test was
                // exactly such a proxy and had nothing to do with production.
                Log.Log(Level.WARNING, "VAS_181_ProductionIssuesWidget: M_InventoryLine has no work-order column in this installation; production issues cannot be classified.");
                return 0;
            }

            // Confirmed business rule from the spec, followed exactly:
            //   "Use the production order on the line level only: VAMFG_M_WorkOrder_ID IS NOT NULL"
            //   "Issued line value = COALESCE(QtyInternalUse,0) * COALESCE(CurrentCostPrice,0)"
            //   "Lines with null CurrentCostPrice contribute zero value. Do not substitute another
            //    cost field."  <- which is why there is no PriceCost / VA024_CostPrice fallback here.
            string productionTest = "line." + productionOrderColumn + " IS NOT NULL";
            const string lineValue = "(COALESCE(line.QtyInternalUse, 0) * COALESCE(line.CurrentCostPrice, 0))";

            string sql = @"
                SELECT
                  COALESCE(SUM(CASE WHEN " + productionTest + " THEN " + lineValue + @" ELSE 0 END), 0) AS ProductionValue,
                  COALESCE(SUM(" + lineValue + @"), 0) AS TotalValue
                FROM M_InventoryLine line
                INNER JOIN M_Inventory inv ON inv.M_Inventory_ID = line.M_Inventory_ID
                WHERE inv.IsActive = 'Y'
                  AND line.IsActive = 'Y'
                  AND inv.IsInternalUse = 'Y'
                  AND inv.DocStatus IN ('CO', 'CL')
                  AND line.M_Product_ID IS NOT NULL
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

