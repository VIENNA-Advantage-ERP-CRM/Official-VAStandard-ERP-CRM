using System.Data.SqlClient;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Web.Mvc;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VIS.Controllers
{
    /// <summary>
    /// Module Name : VAS_183_MaterialConsumptionWidget
    /// Purpose     : Supplies locator-wise material consumption summary and item-level detail modal data.
    /// Chronological development:
    ///   AI-Dev      2026-08-02 Created
    /// </summary>
    public class VAS_183_MaterialConsumptionWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_183_MaterialConsumptionWidgetController).FullName);

        /// <summary>Returns list of accessible warehouses for dropdown.</summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetWarehouses()
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null) { return Json("", JsonRequestBehavior.AllowGet); }

            try
            {
                var list = new List<object>();

                string sql = @"
                    SELECT M_Warehouse_ID, Value, Name
                    FROM M_Warehouse
                    WHERE IsActive = 'Y'
                    ORDER BY Name ASC";

                sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "M_Warehouse", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                using (IDataReader dr = DB.ExecuteReader(sql, null, null))
                {
                    while (dr != null && dr.Read())
                    {
                        list.Add(new
                        {
                            warehouseId = Util.GetValueOfInt(dr["M_Warehouse_ID"]),
                            shortName = Util.GetValueOfString(dr["Value"]),
                            fullName = Util.GetValueOfString(dr["Name"])
                        });
                    }
                }

                string json = JsonConvert.SerializeObject(new { warehouses = list, success = true });
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_183_MaterialConsumptionWidget.GetWarehouses", ex);
                return Json(JsonConvert.SerializeObject(new { error = Msg.GetMsg(ctx, "Error") ?? "Error" }), JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>Endpoint A: Locator summary grouped by locator for given warehouse and period.</summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetLocatorSummary(int warehouseId, int month, int year)
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null) { return Json("", JsonRequestBehavior.AllowGet); }

            var rows = new List<object>();
            DateTime monthStart = new DateTime(year > 0 ? year : DateTime.Now.Year, month > 0 ? month : DateTime.Now.Month, 1);

            try
            {
                DateTime nextMonthStart = monthStart.AddMonths(1);
                string msl = ToSqlDate(monthStart);
                string nmsl = ToSqlDate(nextMonthStart);

                // AddAccessSQL appends its predicate at the end of the statement, so it is applied
                // to the plain transaction SELECT rather than this aggregate (which would break).
                string trxAccessSql = BuildAccessibleTransactionSql(ctx, msl, nmsl);

                // Cost precedence: the ISSUE LINE's own cost first, the product-level M_Cost
                // maximum only as a last resort. MAX() over M_Cost spans every cost element, org
                // and costing method for that product, so it is not the cost of this issue - on
                // FSMTesting6 '4 mm screws' issues at a line cost of 100 while its M_Cost max is
                // 4,213.56 across 9 cost rows. The original COALESCE listed cc first, so that
                // maximum silently overrode every real line cost and inflated consumption value.
                string sql = @"
                    SELECT
                      loc.M_Locator_ID,
                      loc.Value AS LocatorCode,
                      CASE WHEN loc.LocatorCombination IS NULL OR TRIM(loc.LocatorCombination) = '' THEN loc.Value ELSE loc.LocatorCombination END AS LocatorName,
                      SUM(ABS(mt.MovementQty)) AS TotalQty,
                      SUM(ABS(mt.MovementQty) * COALESCE(il.CurrentCostPrice, il.PriceCost, il.VA024_CostPrice, cc.CurrentCostPrice, 0)) AS TotalValue
                    FROM (" + trxAccessSql + @") mt
                    INNER JOIN M_Locator loc ON loc.M_Locator_ID = mt.M_Locator_ID AND loc.IsActive = 'Y'
                    LEFT JOIN M_InventoryLine il ON il.M_InventoryLine_ID = mt.M_InventoryLine_ID AND il.IsActive = 'Y'
                    LEFT JOIN (SELECT M_Product_ID, MAX(CurrentCostPrice) AS CurrentCostPrice
                               FROM M_Cost WHERE IsActive = 'Y' AND CurrentCostPrice IS NOT NULL
                               GROUP BY M_Product_ID) cc ON cc.M_Product_ID = mt.M_Product_ID";

                if (warehouseId > 0)
                {
                    sql += " WHERE loc.M_Warehouse_ID = " + warehouseId;
                }

                sql += @"
                    GROUP BY loc.M_Locator_ID, loc.Value,
                      CASE WHEN loc.LocatorCombination IS NULL OR TRIM(loc.LocatorCombination) = '' THEN loc.Value ELSE loc.LocatorCombination END
                    ORDER BY SUM(ABS(mt.MovementQty)) DESC";

                using (IDataReader dr = DB.ExecuteReader(sql, null, null))
                {
                    while (dr != null && dr.Read())
                    {
                        rows.Add(new
                        {
                            locatorId = Util.GetValueOfInt(dr["M_Locator_ID"]),
                            locatorCode = Util.GetValueOfString(dr["LocatorCode"]),
                            locatorName = Util.GetValueOfString(dr["LocatorName"]),
                            totalQty = Util.GetValueOfDecimal(dr["TotalQty"]),
                            totalValue = Util.GetValueOfDecimal(dr["TotalValue"])
                        });
                    }
                }

            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_183_MaterialConsumptionWidget.GetLocatorSummary", ex);
                return Json(JsonConvert.SerializeObject(new { error = Msg.GetMsg(ctx, "Error") ?? "Error" }), JsonRequestBehavior.AllowGet);
            }
            // ===== NEW CODE START — currency format (agent A05, 2026-08-19) =====
            return Json(JsonConvert.SerializeObject(new { locators = rows, currency = GetCurrencyInfo(ctx), success = true }), JsonRequestBehavior.AllowGet);
            // ===== NEW CODE END — currency format =====
            // ----- OLD CODE (kept for rollback, do not delete) -----
            // return Json(JsonConvert.SerializeObject(new { locators = rows, success = true }), JsonRequestBehavior.AllowGet);
            // ----- END OLD CODE -----
        }

        /// <summary>Endpoint B: Item-level consumption details for a locator modal.</summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetLocatorDetails(int warehouseId, int locatorId, int month, int year)
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null) { return Json("", JsonRequestBehavior.AllowGet); }

            DateTime monthStart = new DateTime(year > 0 ? year : DateTime.Now.Year, month > 0 ? month : DateTime.Now.Month, 1);
            var items = new List<object>();
            decimal totalQty = 0;
            decimal totalValue = 0;
            HashSet<int> distinctItems = new HashSet<int>();

            try
            {
                DateTime nextMonthStart = monthStart.AddMonths(1);
                string msl = ToSqlDate(monthStart);
                string nmsl = ToSqlDate(nextMonthStart);

                string trxAccessSql = BuildAccessibleTransactionSql(ctx, msl, nmsl);

                string sql = @"
                    SELECT
                      p.M_Product_ID,
                      p.Name AS ProductName,
                      uom.Name AS UomName,
                      asi.Description AS Attributes,
                      SUM(ABS(mt.MovementQty)) AS ConsumedQty,
                      SUM(ABS(mt.MovementQty) * COALESCE(il.CurrentCostPrice, il.PriceCost, il.VA024_CostPrice, cc.CurrentCostPrice, 0)) AS LineValue
                    FROM (" + trxAccessSql + @") mt
                    INNER JOIN M_Product p ON p.M_Product_ID = mt.M_Product_ID
                    LEFT JOIN C_UOM uom ON uom.C_UOM_ID = p.C_UOM_ID
                    LEFT JOIN M_AttributeSetInstance asi ON asi.M_AttributeSetInstance_ID = mt.M_AttributeSetInstance_ID
                    LEFT JOIN M_InventoryLine il ON il.M_InventoryLine_ID = mt.M_InventoryLine_ID AND il.IsActive = 'Y'
                    LEFT JOIN (SELECT M_Product_ID, MAX(CurrentCostPrice) AS CurrentCostPrice
                               FROM M_Cost WHERE IsActive = 'Y' AND CurrentCostPrice IS NOT NULL
                               GROUP BY M_Product_ID) cc ON cc.M_Product_ID = mt.M_Product_ID
                    WHERE mt.M_Locator_ID = " + locatorId + @"
                    GROUP BY p.M_Product_ID, p.Name, uom.Name, asi.Description
                    ORDER BY SUM(ABS(mt.MovementQty)) DESC";

                using (IDataReader dr = DB.ExecuteReader(sql, null, null))
                {
                    while (dr != null && dr.Read())
                    {
                        int pId = Util.GetValueOfInt(dr["M_Product_ID"]);
                        decimal qty = Util.GetValueOfDecimal(dr["ConsumedQty"]);
                        decimal val = Util.GetValueOfDecimal(dr["LineValue"]);

                        distinctItems.Add(pId);
                        totalQty += qty;
                        totalValue += val;

                        items.Add(new
                        {
                            productId = pId,
                            productName = Util.GetValueOfString(dr["ProductName"]),
                            uomName = Util.GetValueOfString(dr["UomName"]),
                            attributes = NormalizeAttributes(Util.GetValueOfString(dr["Attributes"])),
                            consumedQty = qty,
                            lineValue = val
                        });
                    }
                }

            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_183_MaterialConsumptionWidget.GetLocatorDetails", ex);
                return Json(JsonConvert.SerializeObject(new { error = Msg.GetMsg(ctx, "Error") ?? "Error" }), JsonRequestBehavior.AllowGet);
            }

            // ===== NEW CODE START — currency format (agent A05, 2026-08-19) =====
            return Json(JsonConvert.SerializeObject(new
            {
                totalQty = totalQty,
                totalValue = totalValue,
                distinctItemCount = distinctItems.Count,
                items = items,
                currency = GetCurrencyInfo(ctx),
                success = true
            }), JsonRequestBehavior.AllowGet);
            // ===== NEW CODE END — currency format =====
            // ----- OLD CODE (kept for rollback, do not delete) -----
            /*
            return Json(JsonConvert.SerializeObject(new
            {
                totalQty = totalQty,
                totalValue = totalValue,
                distinctItemCount = distinctItems.Count,
                items = items,
                success = true
            }), JsonRequestBehavior.AllowGet);
            */
            // ----- END OLD CODE -----
        }

        // ===== NEW CODE START — currency format (agent A05, 2026-08-19) =====
        /// <summary>Returns currency ISO code and symbol for context org/client.</summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetCurrencyInfo()
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null) { return Json("", JsonRequestBehavior.AllowGet); }
            object curr = GetCurrencyInfo(ctx);
            return Json(JsonConvert.SerializeObject(curr), JsonRequestBehavior.AllowGet);
        }

        private static object GetCurrencyInfo(Ctx ctx)
        {
            string iso = "INR";
            string symbol = "₹";

            if (ctx == null)
            {
                return new { iso = iso, symbol = symbol };
            }

            int currencyId = ctx.GetContextAsInt("$C_Currency_ID");
            if (currencyId <= 0)
            {
                currencyId = ctx.GetContextAsInt("#C_Currency_ID");
            }

            IDataReader dr = null;
            try
            {
                if (currencyId > 0)
                {
                    string sql = "SELECT ISO_Code, CurSymbol FROM C_Currency WHERE C_Currency_ID = " + currencyId + " AND IsActive = 'Y'";
                    dr = DB.ExecuteReader(sql, null, null);
                    if (dr != null && dr.Read())
                    {
                        iso = Util.GetValueOfString(dr["ISO_Code"]);
                        symbol = Util.GetValueOfString(dr["CurSymbol"]);
                    }
                }
                else
                {
                    string sql = @"
                        SELECT c.ISO_Code, c.CurSymbol
                        FROM AD_ClientInfo ci
                        INNER JOIN C_AcctSchema a ON (a.C_AcctSchema_ID = ci.C_AcctSchema1_ID AND a.IsActive = 'Y')
                        INNER JOIN C_Currency c ON (c.C_Currency_ID = a.C_Currency_ID AND c.IsActive = 'Y')
                        WHERE ci.AD_Client_ID = " + ctx.GetAD_Client_ID() + " AND ci.IsActive = 'Y'";
                    dr = DB.ExecuteReader(sql, null, null);
                    if (dr != null && dr.Read())
                    {
                        iso = Util.GetValueOfString(dr["ISO_Code"]);
                        symbol = Util.GetValueOfString(dr["CurSymbol"]);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_183_MaterialConsumptionWidget.GetCurrencyInfo", ex);
            }
            finally
            {
                if (dr != null)
                {
                    dr.Close();
                    dr.Dispose();
                }
            }

            if (string.IsNullOrEmpty(symbol))
            {
                symbol = iso;
            }

            return new { iso = iso, symbol = symbol };
        }
        // ===== NEW CODE END — currency format =====

        /// <summary>
        /// Role-filtered consumption transactions for the period. Kept as a plain SELECT with no
        /// GROUP BY / ORDER BY: AddAccessSQL appends its predicate to the end of the statement, so
        /// it is only safe on a query whose alias is still in scope there.
        /// </summary>
        private static string BuildAccessibleTransactionSql(Ctx ctx, string periodStart, string periodEnd)
        {
            // Consumption = stock issued for internal use / production. Movement types:
            //   'P-' production issue and 'W-' work order issue are consumption by definition.
            //   'I-' is "Inventory Out", which covers BOTH internal-use issues AND physical
            //        inventory count adjustments that write stock down. Only the former is
            //        consumption, so an 'I-' row is accepted only when its source document is an
            //        internal-use document (M_Inventory.IsInternalUse = 'Y').
            // Without that restriction physical inventory adjustments dominated the figures on
            // FSMTesting6: 20,732 of the 23,411 reported units (~89%) came from count documents.
            string sql = @"
                    SELECT mt.M_Transaction_ID, mt.M_Product_ID, mt.M_Locator_ID,
                           mt.M_AttributeSetInstance_ID, mt.MovementQty, mt.M_InventoryLine_ID
                    FROM M_Transaction mt
                    WHERE mt.IsActive = 'Y'
                      AND COALESCE(mt.IsReversed, 'N') = 'N'
                      AND (
                            mt.MovementType IN ('P-', 'W-')
                            OR (mt.MovementType = 'I-' AND EXISTS (
                                  SELECT 1
                                  FROM M_InventoryLine iline
                                  INNER JOIN M_Inventory ihdr ON ihdr.M_Inventory_ID = iline.M_Inventory_ID
                                  WHERE iline.M_InventoryLine_ID = mt.M_InventoryLine_ID
                                    AND iline.IsActive = 'Y'
                                    AND ihdr.IsActive = 'Y'
                                    AND COALESCE(ihdr.IsInternalUse, 'N') = 'Y'))
                          )
                      AND mt.MovementDate >= " + periodStart + @"
                      AND mt.MovementDate < " + periodEnd;

            return MRole.GetDefault(ctx).AddAccessSQL(sql, "mt", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
        }

        /// <summary>
        /// An attribute set instance with no attributes stores a dash placeholder in Description
        /// (e.g. "---"), which would render literally in the modal's Attributes column. The spec
        /// asks for a single "-" when a line has no attributes, and the widget already falls back
        /// to "-" on an empty string, so collapse any dash/whitespace-only value to empty here.
        /// </summary>
        private static string NormalizeAttributes(string description)
        {
            if (string.IsNullOrWhiteSpace(description)) { return ""; }

            string trimmed = description.Trim();
            foreach (char c in trimmed)
            {
                if (c != '-' && c != '_' && c != '.' && !char.IsWhiteSpace(c))
                {
                    return trimmed;
                }
            }
            return "";
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

