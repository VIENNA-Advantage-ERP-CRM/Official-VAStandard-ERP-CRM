using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Web.Mvc;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VAS.Controllers
{
    /// <summary>
    /// Module Name : VAS_115_ABCAnalysisWidget
    /// Purpose     : Warehouse-level ABC / Pareto analysis by current stock value
    ///               for the Product section. Stock value = on-hand qty x current
    ///               cost price (primary accounting schema cost, warehouse/ASI
    ///               fallback). Products are ranked by stock value desc and
    ///               classified A (first 80% of value) / B (next 15%) / C (last 5%)
    ///               using the cumulative value BEFORE each product. Only active,
    ///               stocked item products with on-hand > 0. Same SQL runs on
    ///               Oracle and PostgreSQL. Widget number 115 - reassign on
    ///               hand-off.
    /// Chronological development:
    ///   115         2026-07-16 Created
    /// </summary>
    public class VAS_115_ABCAnalysisWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_115_ABCAnalysisWidgetController).FullName);

        /// <summary>Returns the active warehouses available to the current role/client.</summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetWarehouses()
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null) { return Json("", JsonRequestBehavior.AllowGet); }

            try
            {
                return Json(JsonConvert.SerializeObject(new { rows = GetWarehousesData(ctx) }), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return ErrorResult(ctx, "GetWarehouses", ex);
            }
        }

        /// <summary>Returns the ABC classification for one warehouse.</summary>
        /// <param name="warehouseId">Selected warehouse (mandatory).</param>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetABCAnalysis(int warehouseId)
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null) { return Json("", JsonRequestBehavior.AllowGet); }

            try
            {
                if (warehouseId <= 0)
                {
                    return Json(JsonConvert.SerializeObject(new { error = Msg.GetMsg(ctx, "Error") ?? "Error" }), JsonRequestBehavior.AllowGet);
                }
                return Json(JsonConvert.SerializeObject(GetABCAnalysisData(ctx, warehouseId)), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return ErrorResult(ctx, "GetABCAnalysis", ex);
            }
        }

        private List<Warehouse> GetWarehousesData(Ctx ctx)
        {
            List<Warehouse> warehouses = new List<Warehouse>();
            if (ctx == null) { return warehouses; }

            string sql = @"
                SELECT w.M_Warehouse_ID AS warehouse_id,
                       w.Value AS warehouse_code,
                       w.Name AS warehouse_name
                FROM M_Warehouse w
                WHERE w.IsActive='Y'
                  AND w.AD_Client_ID=@AD_Client_ID";

            sql = AddAccessSql(ctx, sql, "w");
            sql += " ORDER BY w.Name";

            IDataReader reader = null;
            try
            {
                reader = DB.ExecuteReader(sql, new SqlParameter[] { new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()) });
                while (reader != null && reader.Read())
                {
                    warehouses.Add(new Warehouse
                    {
                        warehouse_id = Util.GetValueOfInt(reader["warehouse_id"]),
                        warehouse_code = Util.GetValueOfString(reader["warehouse_code"]),
                        warehouse_name = Util.GetValueOfString(reader["warehouse_name"])
                    });
                }
            }
            finally
            {
                CloseReader(reader);
            }

            return warehouses;
        }

        private ABCResult GetABCAnalysisData(Ctx ctx, int warehouseId)
        {
            ABCResult result = new ABCResult { rows = new List<ABCRow>(), classes = new List<ABCClass>() };
            if (ctx == null) { return result; }

            result.warehouse_id = warehouseId;
            SchemaCurrency currency = GetSchemaCurrency(ctx);
            result.currency_symbol = currency.Symbol;
            result.currency_iso = currency.IsoCode;
            result.std_precision = currency.StdPrecision;

            // storage_cost's physical driver is M_Storage (alias s): MRole is
            // applied to the SELECT ... WHERE and the GROUP BY is appended after,
            // so the access predicate never lands past GROUP BY (ORA-00907). The
            // remaining CTEs read only CTE aliases, so they carry no MRole.
            string storageCost = @"
                SELECT s.M_Storage_ID AS storage_id,
                       l.M_Warehouse_ID AS warehouse_id,
                       p.M_Product_ID AS product_id,
                       COALESCE(p.SKU, p.Value) AS sku,
                       p.Name AS product_name,
                       s.M_AttributeSetInstance_ID AS attribute_set_instance_id,
                       s.QtyOnHand AS qty_on_hand,
                       COALESCE(
                           MAX(CASE WHEN COALESCE(c.M_Warehouse_ID,0)=l.M_Warehouse_ID AND COALESCE(c.M_AttributeSetInstance_ID,0)=COALESCE(s.M_AttributeSetInstance_ID,0) THEN c.CurrentCostPrice END),
                           MAX(CASE WHEN COALESCE(c.M_Warehouse_ID,0)=l.M_Warehouse_ID AND COALESCE(c.M_AttributeSetInstance_ID,0)=0 THEN c.CurrentCostPrice END),
                           MAX(CASE WHEN COALESCE(c.M_Warehouse_ID,0)=0 AND COALESCE(c.M_AttributeSetInstance_ID,0)=COALESCE(s.M_AttributeSetInstance_ID,0) THEN c.CurrentCostPrice END),
                           MAX(CASE WHEN COALESCE(c.M_Warehouse_ID,0)=0 AND COALESCE(c.M_AttributeSetInstance_ID,0)=0 THEN c.CurrentCostPrice END),
                           0
                       ) AS current_cost_price
                FROM M_Storage s
                INNER JOIN M_Locator l ON l.M_Locator_ID=s.M_Locator_ID AND l.IsActive='Y'
                INNER JOIN M_Warehouse w ON w.M_Warehouse_ID=l.M_Warehouse_ID AND w.IsActive='Y'
                INNER JOIN M_Product p ON p.M_Product_ID=s.M_Product_ID AND p.IsActive='Y' AND p.ProductType='I' AND p.IsStocked='Y'
                INNER JOIN AD_ClientInfo ci ON ci.AD_Client_ID=s.AD_Client_ID AND ci.IsActive='Y'
                INNER JOIN C_AcctSchema a ON a.C_AcctSchema_ID=ci.C_AcctSchema1_ID AND a.IsActive='Y'
                LEFT JOIN M_Cost c
                    ON c.AD_Client_ID=s.AD_Client_ID
                   AND c.M_Product_ID=s.M_Product_ID
                   AND c.C_AcctSchema_ID=a.C_AcctSchema_ID
                   AND c.M_CostType_ID=a.M_CostType_ID
                   AND c.M_CostElement_ID=a.M_CostElement_ID
                   AND c.IsActive='Y'
                   AND COALESCE(c.IsThisLevel,'Y')='Y'
                   AND COALESCE(c.A_Asset_ID,0)=0
                   AND COALESCE(c.M_Warehouse_ID,0) IN (0,l.M_Warehouse_ID)
                   AND COALESCE(c.M_AttributeSetInstance_ID,0) IN (0,COALESCE(s.M_AttributeSetInstance_ID,0))
                WHERE s.IsActive='Y'
                  AND s.AD_Client_ID=@AD_Client_ID
                  AND l.M_Warehouse_ID=@M_Warehouse_ID";

            storageCost = AddAccessSql(ctx, storageCost, "s");
            storageCost += @"
                GROUP BY s.M_Storage_ID, l.M_Warehouse_ID, p.M_Product_ID, p.SKU, p.Value, p.Name, s.M_AttributeSetInstance_ID, s.QtyOnHand";

            string sql = @"
                WITH storage_cost AS (
                    " + storageCost + @"
                ),
                product_stock AS (
                    SELECT product_id, sku, product_name,
                           SUM(qty_on_hand) AS qty_on_hand,
                           SUM(qty_on_hand * current_cost_price) AS stock_value
                    FROM storage_cost
                    GROUP BY product_id, sku, product_name
                    HAVING SUM(qty_on_hand) > 0
                ),
                ranked_products AS (
                    SELECT ps.product_id, ps.sku, ps.product_name, ps.qty_on_hand, ps.stock_value,
                           COUNT(*) OVER () AS total_sku_count,
                           SUM(ps.stock_value) OVER () AS total_stock_value,
                           SUM(ps.stock_value) OVER (
                               ORDER BY ps.stock_value DESC, ps.product_id
                               ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW
                           ) AS cumulative_stock_value
                    FROM product_stock ps
                ),
                classified_products AS (
                    SELECT rp.product_id, rp.sku, rp.product_name, rp.qty_on_hand, rp.stock_value,
                           rp.total_sku_count, rp.total_stock_value,
                           CASE
                               WHEN rp.total_stock_value <= 0 THEN 'C'
                               WHEN ((rp.cumulative_stock_value - rp.stock_value) / rp.total_stock_value) < 0.80 THEN 'A'
                               WHEN ((rp.cumulative_stock_value - rp.stock_value) / rp.total_stock_value) < 0.95 THEN 'B'
                               ELSE 'C'
                           END AS abc_class
                    FROM ranked_products rp
                )
                SELECT cp.product_id,
                       cp.sku,
                       cp.product_name,
                       cp.qty_on_hand,
                       cp.stock_value,
                       cp.abc_class,
                       COUNT(*) OVER (PARTITION BY cp.abc_class) AS class_sku_count,
                       ROUND(100.0 * COUNT(*) OVER (PARTITION BY cp.abc_class) / NULLIF(cp.total_sku_count, 0), 4) AS class_sku_percentage,
                       SUM(cp.stock_value) OVER (PARTITION BY cp.abc_class) AS class_stock_value,
                       cp.total_sku_count,
                       cp.total_stock_value
                FROM classified_products cp
                ORDER BY
                    CASE cp.abc_class WHEN 'A' THEN 1 WHEN 'B' THEN 2 ELSE 3 END,
                    cp.stock_value DESC,
                    cp.product_id";

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@M_Warehouse_ID", warehouseId)
            };

            // Per-class aggregates come straight from the SQL window functions
            // (constant within each class), so JS does not re-classify.
            Dictionary<string, ABCClass> classMap = new Dictionary<string, ABCClass>();

            IDataReader reader = null;
            try
            {
                reader = DB.ExecuteReader(sql, parameters);
                while (reader != null && reader.Read())
                {
                    string cls = Util.GetValueOfString(reader["abc_class"]);
                    result.rows.Add(new ABCRow
                    {
                        product_id = SafeInt(reader["product_id"]),
                        sku = Util.GetValueOfString(reader["sku"]),
                        product_name = Util.GetValueOfString(reader["product_name"]),
                        qty_on_hand = SafeDecimal(reader["qty_on_hand"]),
                        stock_value = SafeDecimal(reader["stock_value"]),
                        abc_class = cls
                    });

                    if (!classMap.ContainsKey(cls))
                    {
                        classMap[cls] = new ABCClass
                        {
                            cls = cls,
                            sku_count = SafeInt(reader["class_sku_count"]),
                            sku_pct = SafeDecimal(reader["class_sku_percentage"]),
                            stock_value = SafeDecimal(reader["class_stock_value"])
                        };
                    }

                    if (result.total_sku_count == 0)
                    {
                        result.total_sku_count = SafeInt(reader["total_sku_count"]);
                        result.total_stock_value = SafeDecimal(reader["total_stock_value"]);
                    }
                }
            }
            finally
            {
                CloseReader(reader);
            }

            // Always emit A, B, C in order, with zeros for empty classes.
            foreach (string cls in new[] { "A", "B", "C" })
            {
                result.classes.Add(classMap.ContainsKey(cls)
                    ? classMap[cls]
                    : new ABCClass { cls = cls, sku_count = 0, sku_pct = 0, stock_value = 0 });
            }

            return result;
        }

        /// <summary>Gets the tenant accounting-schema currency and its standard precision.</summary>
        private SchemaCurrency GetSchemaCurrency(Ctx ctx)
        {
            SchemaCurrency currency = new SchemaCurrency();
            string sql = @"
                SELECT AcctSchema.C_Currency_ID,
                       Currency.StdPrecision,
                       CASE WHEN Currency.CurSymbol IS NOT NULL THEN Currency.CurSymbol ELSE Currency.ISO_Code END AS Currency_Symbol,
                       Currency.ISO_Code AS Currency_ISO
                FROM AD_ClientInfo ClientInfo
                INNER JOIN C_AcctSchema AcctSchema ON (AcctSchema.C_AcctSchema_ID=ClientInfo.C_AcctSchema1_ID AND AcctSchema.IsActive='Y')
                INNER JOIN C_Currency Currency ON (Currency.C_Currency_ID=AcctSchema.C_Currency_ID AND Currency.IsActive='Y')
                WHERE ClientInfo.IsActive='Y'
                  AND ClientInfo.AD_Client_ID=@AD_Client_ID";

            sql = AddAccessSql(ctx, sql, "ClientInfo");
            IDataReader reader = null;
            try
            {
                reader = DB.ExecuteReader(sql, new SqlParameter[] { new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()) });
                if (reader == null || !reader.Read()) { return currency; }

                currency.CurrencyId = SafeInt(reader["C_Currency_ID"]);
                currency.Symbol = Util.GetValueOfString(reader["Currency_Symbol"]);
                currency.IsoCode = Util.GetValueOfString(reader["Currency_ISO"]);
                currency.StdPrecision = SafeInt(reader["StdPrecision"]);
                return currency;
            }
            finally
            {
                CloseReader(reader);
            }
        }

        private string AddAccessSql(Ctx ctx, string sql, string tableAlias)
        {
            return MRole.GetDefault(ctx).AddAccessSQL(sql, tableAlias, MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
        }

        // Window-function / computed columns (COUNT() OVER, the 100.0*.../... pct)
        // can come back from the Oracle layer as a boxed numeric type that Util's
        // unboxing cast rejects ("Specified cast is not valid"). Convert.* accepts
        // any IConvertible numeric type and DBNull, so these reads never throw.
        private static decimal SafeDecimal(object value)
        {
            return (value == null || value == DBNull.Value) ? 0m : Convert.ToDecimal(value);
        }

        private static int SafeInt(object value)
        {
            return (value == null || value == DBNull.Value) ? 0 : Convert.ToInt32(Convert.ToDecimal(value));
        }

        private void CloseReader(IDataReader reader)
        {
            if (reader == null) { return; }
            reader.Close();
            reader.Dispose();
        }

        private JsonResult ErrorResult(Ctx ctx, string action, Exception ex)
        {
            Log.Log(Level.SEVERE, "VAS_115_ABCAnalysisWidget." + action, ex);
            string json = JsonConvert.SerializeObject(new { error = Msg.GetMsg(ctx, "Error") ?? "Error" });
            return Json(json, JsonRequestBehavior.AllowGet);
        }

        private class Warehouse
        {
            public int warehouse_id { get; set; }
            public string warehouse_code { get; set; }
            public string warehouse_name { get; set; }
        }

        private class SchemaCurrency
        {
            public int CurrencyId { get; set; }
            public string Symbol { get; set; }
            public string IsoCode { get; set; }
            public int StdPrecision { get; set; }
        }

        private class ABCResult
        {
            public int warehouse_id { get; set; }
            public string currency_symbol { get; set; }
            public string currency_iso { get; set; }
            public int std_precision { get; set; }
            public int total_sku_count { get; set; }
            public decimal total_stock_value { get; set; }
            public List<ABCClass> classes { get; set; }
            public List<ABCRow> rows { get; set; }
        }

        private class ABCClass
        {
            public string cls { get; set; }
            public int sku_count { get; set; }
            public decimal sku_pct { get; set; }
            public decimal stock_value { get; set; }
        }

        private class ABCRow
        {
            public int product_id { get; set; }
            public string sku { get; set; }
            public string product_name { get; set; }
            public decimal qty_on_hand { get; set; }
            public decimal stock_value { get; set; }
            public string abc_class { get; set; }
        }
    }
}
