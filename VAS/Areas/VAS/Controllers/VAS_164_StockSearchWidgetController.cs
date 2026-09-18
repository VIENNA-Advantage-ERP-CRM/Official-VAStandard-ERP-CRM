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
    /*
     * TABLE & FIELD MAPPING FOR STOCK SEARCH WIDGET:
     * - Product Master: M_Product (M_Product_ID, Value, Name, ProductType, IsStocked, M_Product_Category_ID, C_UOM_ID, IsActive)
     * - Product Category: M_Product_Category (M_Product_Category_ID, Name)
     * - Unit of Measure: C_UOM (C_UOM_ID, Name, UOMSymbol)
     * - Storage / On-Hand: M_Storage (M_Product_ID, M_Locator_ID, QtyOnHand)
     * - Locator: M_Locator (M_Locator_ID, M_Warehouse_ID, Value, LocatorCombination)
     *   Visible locator value is COALESCE(LocatorCombination, Value) - the source prompt states
     *   this twice ("Use COALESCE(M_Locator.LocatorCombination, M_Locator.Value) as the visible
     *   Locator value" and "Locator display: M_Locator.LocatorCombination, with M_Locator.Value
     *   as fallback"). Value alone is the code/identifier, not the name.
     * - Warehouse: M_Warehouse (M_Warehouse_ID, Value, Name)
     * Cross-Database: COALESCE used for Oracle and PostgreSQL compatibility.
     */

    [AjaxAuthorize]
    [AjaxSessionFilter]
    public class VAS_164_StockSearchWidgetController : Controller
    {
        private static readonly VLogger _log = VLogger.GetVLogger(typeof(VAS_164_StockSearchWidgetController));

        /// <summary>
        /// Search stockable products by name, code, category, or product type.
        /// </summary>
        [HttpGet]
        public JsonResult SearchProducts(string query)
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null)
            {
                return Json(new { error = "Unauthorized context." }, JsonRequestBehavior.AllowGet);
            }

            List<object> list = new List<object>();
            IDataReader dr = null;
            try
            {
                // Locator search (user request 2026-08-29). The source prompt originally scoped
                // search to product name/code/category and said "Do not advertise locator search
                // unless locator search is deliberately added later with a separately approved
                // requirement" - this is that requirement, so the locator predicates are added and
                // the input placeholder that already mentioned locators becomes truthful.
                //
                // LOCATOR MATCH TAKES PRECEDENCE, it is not simply OR'd in. The requirement is
                // "display ONLY the products available in the corresponding locator", and a plain
                // OR cannot deliver that: on DB 1 the locator codes are 1000000..1000006 while
                // product codes run 10000030..10000038, so searching the locator code "1000003"
                // under an OR also dragged in every product whose CODE merely contains it.
                //
                // So: when the term matches any locator the user can see, only storage rows in a
                // matching locator qualify - products with no stock there disappear, and the summed
                // on-hand becomes that locator's quantity. When the term matches no locator, the
                // NOT EXISTS opens up and it behaves as the original product search, with the
                // on-hand still summed across every locator.
                //
                // Bound as parameters, not concatenated. The prompt's implementation rules require
                // "Use parameterized SQL only. Never concatenate search text ... into SQL", and the
                // previous concatenation of user input straight into the LIKE was an injection
                // vector. Each occurrence binds its own name rather than reusing one - VAS_073 is
                // the repo's precedent for binding a separate name per occurrence - and they are
                // added in the order they appear in the statement.
                string searchFilter = "";
                List<SqlParameter> parameters = new List<SqlParameter>();

                if (!string.IsNullOrEmpty(query))
                {
                    string pattern = "%" + query.Trim().ToUpper() + "%";

                    searchFilter = @" AND (UPPER(loc.Value) LIKE @QLocatorCode
                                        OR UPPER(loc.LocatorCombination) LIKE @QLocatorName
                                        OR (NOT EXISTS (SELECT 1
                                                          FROM M_Locator lx
                                                         WHERE lx.IsActive = 'Y'
                                                           AND lx.AD_Client_ID = p.AD_Client_ID
                                                           AND (UPPER(lx.Value) LIKE @QLocatorMatchCode
                                                             OR UPPER(lx.LocatorCombination) LIKE @QLocatorMatchName))
                                            AND (UPPER(p.Value) LIKE @QProductCode
                                              OR UPPER(p.Name) LIKE @QProductName
                                              OR UPPER(pc.Name) LIKE @QCategory)))";

                    parameters.Add(new SqlParameter("@QLocatorCode", pattern));
                    parameters.Add(new SqlParameter("@QLocatorName", pattern));
                    parameters.Add(new SqlParameter("@QLocatorMatchCode", pattern));
                    parameters.Add(new SqlParameter("@QLocatorMatchName", pattern));
                    parameters.Add(new SqlParameter("@QProductCode", pattern));
                    parameters.Add(new SqlParameter("@QProductName", pattern));
                    parameters.Add(new SqlParameter("@QCategory", pattern));
                }

                string sql = @"SELECT p.M_Product_ID, 
                                      p.Value AS ProductCode, 
                                      p.Name AS ProductName, 
                                      p.ProductType, 
                                      COALESCE(pc.Name, N'Standard') AS CategoryName, 
                                      COALESCE(u.UOMSymbol, COALESCE(u.Name, N'Each')) AS UOMName, 
                                      p.IsActive, 
                                      COALESCE(SUM(s.QtyOnHand), 0) AS TotalQtyOnHand 
                               FROM M_Product p 
                               LEFT JOIN M_Product_Category pc ON (p.M_Product_Category_ID = pc.M_Product_Category_ID) 
                               LEFT JOIN C_UOM u ON (p.C_UOM_ID = u.C_UOM_ID) 
                               LEFT JOIN M_Storage s ON (p.M_Product_ID = s.M_Product_ID AND s.IsActive = 'Y') 
                               LEFT JOIN M_Locator loc ON (s.M_Locator_ID = loc.M_Locator_ID AND loc.IsActive = 'Y') 
                               WHERE p.IsActive = 'Y' 
                                 AND p.IsStocked = 'Y' 
                                 AND p.ProductType = 'I'" + searchFilter;

                sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "p", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                sql += " GROUP BY p.M_Product_ID, p.Value, p.Name, p.ProductType, pc.Name, u.UOMSymbol, u.Name, p.IsActive ORDER BY TotalQtyOnHand DESC, p.Name ASC";

                dr = DB.ExecuteReader(sql, parameters.Count > 0 ? parameters.ToArray() : null, null);
                int count = 0;
                while (dr != null && dr.Read() && count < 20)
                {
                    list.Add(new
                    {
                        productId = Util.GetValueOfInt(dr["M_Product_ID"]),
                        code = Util.GetValueOfString(dr["ProductCode"]),
                        name = Util.GetValueOfString(dr["ProductName"]),
                        productType = GetProductTypeLabel(Util.GetValueOfString(dr["ProductType"])),
                        category = Util.GetValueOfString(dr["CategoryName"]),
                        uom = Util.GetValueOfString(dr["UOMName"]),
                        status = Util.GetValueOfString(dr["IsActive"]) == "Y" ? "Active" : "Inactive",
                        onHand = Util.GetValueOfDecimal(dr["TotalQtyOnHand"])
                    });
                    count++;
                }
            }
            catch (Exception ex)
            {
                _log.Severe("VAS_164_StockSearchWidgetController.SearchProducts: " + ex.Message);
            }
            finally
            {
                if (dr != null)
                {
                    dr.Close();
                    dr.Dispose();
                }
            }

            return Json(new { items = list }, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Gets product detail for the 6 form fields and status pill.
        /// </summary>
        [HttpGet]
        public JsonResult GetProductDetail(int productId)
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null)
            {
                return Json(new { error = "Unauthorized context." }, JsonRequestBehavior.AllowGet);
            }

            object detail = null;
            IDataReader dr = null;
            try
            {
                string sql = @"SELECT p.M_Product_ID, 
                                      p.Value AS ProductCode, 
                                      p.Name AS ProductName, 
                                      p.ProductType, 
                                      COALESCE(pc.Name, N'Standard') AS CategoryName, 
                                      COALESCE(u.UOMSymbol, COALESCE(u.Name, N'Each')) AS UOMName, 
                                      p.IsActive, 
                                      COALESCE(SUM(s.QtyOnHand), 0) AS TotalQtyOnHand 
                               FROM M_Product p 
                               LEFT JOIN M_Product_Category pc ON (p.M_Product_Category_ID = pc.M_Product_Category_ID) 
                               LEFT JOIN C_UOM u ON (p.C_UOM_ID = u.C_UOM_ID) 
                               LEFT JOIN M_Storage s ON (p.M_Product_ID = s.M_Product_ID AND s.IsActive = 'Y') 
                               WHERE p.M_Product_ID = " + productId + @" 
                                 AND p.IsStocked = 'Y' 
                                 AND p.ProductType = 'I'";

                sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "p", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                sql += " GROUP BY p.M_Product_ID, p.Value, p.Name, p.ProductType, pc.Name, u.UOMSymbol, u.Name, p.IsActive";

                dr = DB.ExecuteReader(sql, null, null);
                if (dr != null && dr.Read())
                {
                    detail = new
                    {
                        productId = Util.GetValueOfInt(dr["M_Product_ID"]),
                        code = Util.GetValueOfString(dr["ProductCode"]),
                        name = Util.GetValueOfString(dr["ProductName"]),
                        productType = GetProductTypeLabel(Util.GetValueOfString(dr["ProductType"])),
                        category = Util.GetValueOfString(dr["CategoryName"]),
                        uom = Util.GetValueOfString(dr["UOMName"]),
                        status = Util.GetValueOfString(dr["IsActive"]) == "Y" ? "Active" : "Inactive",
                        onHand = Util.GetValueOfDecimal(dr["TotalQtyOnHand"])
                    };
                }
            }
            catch (Exception ex)
            {
                _log.Severe("VAS_164_StockSearchWidgetController.GetProductDetail: " + ex.Message);
            }
            finally
            {
                if (dr != null)
                {
                    dr.Close();
                    dr.Dispose();
                }
            }

            return Json(new { product = detail }, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Gets locator-level stock breakdown for a specific product.
        /// </summary>
        [HttpGet]
        public JsonResult GetProductLocators(int productId)
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null)
            {
                return Json(new { error = "Unauthorized context." }, JsonRequestBehavior.AllowGet);
            }

            List<object> list = new List<object>();
            IDataReader dr = null;
            try
            {
                // Attribute added to the locator breakdown (user request 2026-08-16). Stock is held
                // per attribute set instance, so it belongs in the GROUP BY - without it two
                // different batches in one locator collapse into a single untraceable row.
                // asi.Description is NVARCHAR2: selected raw, never COALESCE'd against a literal
                // (that raises ORA-12704 - see VAS_161 / VAS_163 / VAS_165). Blank when absent.
                string sql = @"SELECT COALESCE(loc.LocatorCombination, loc.Value) AS LocatorCode,
                                      w.Name AS WarehouseName,
                                      asi.Description AS AttributeDesc,
                                      SUM(s.QtyOnHand) AS QtyOnHand
                               FROM M_Storage s
                               JOIN M_Locator loc ON (s.M_Locator_ID = loc.M_Locator_ID)
                               JOIN M_Warehouse w ON (loc.M_Warehouse_ID = w.M_Warehouse_ID)
                               LEFT JOIN M_AttributeSetInstance asi ON (s.M_AttributeSetInstance_ID = asi.M_AttributeSetInstance_ID)
                               WHERE s.IsActive = 'Y' AND s.M_Product_ID = " + productId;

                sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "s", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                sql += " GROUP BY COALESCE(loc.LocatorCombination, loc.Value), w.Name, asi.Description"
                     + " ORDER BY QtyOnHand DESC, COALESCE(loc.LocatorCombination, loc.Value) ASC";

                dr = DB.ExecuteReader(sql, null, null);
                while (dr != null && dr.Read())
                {
                    list.Add(new
                    {
                        locator = Util.GetValueOfString(dr["LocatorCode"]),
                        warehouse = Util.GetValueOfString(dr["WarehouseName"]),
                        attribute = Util.GetValueOfString(dr["AttributeDesc"]),
                        qty = Util.GetValueOfDecimal(dr["QtyOnHand"])
                    });
                }
            }
            catch (Exception ex)
            {
                _log.Severe("VAS_164_StockSearchWidgetController.GetProductLocators: " + ex.Message);
            }
            finally
            {
                if (dr != null)
                {
                    dr.Close();
                    dr.Dispose();
                }
            }

            return Json(new { locators = list, totalLocators = list.Count }, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Previous inventory counts for a product - the modal's second tab. One row per count line,
        /// newest first, carrying the parent M_Inventory_ID so the row can navigate to the
        /// VAS_PhysicalInventory record.
        /// Physical counts only (IsInternalUse = 'N'); internal-use documents are not counts.
        /// </summary>
        [HttpGet]
        public JsonResult GetProductCountHistory(int productId)
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null)
            {
                return Json(new { error = "Unauthorized context." }, JsonRequestBehavior.AllowGet);
            }

            List<object> list = new List<object>();
            IDataReader dr = null;
            try
            {
                // asi.Description selected raw - see the note in GetProductLocators.
                string sql = @"SELECT i.M_Inventory_ID,
                                      i.DocumentNo,
                                      i.MovementDate,
                                      i.DocStatus,
                                      w.Name AS WarehouseName,
                                      COALESCE(loc.LocatorCombination, loc.Value) AS LocatorCode,
                                      asi.Description AS AttributeDesc,
                                      COALESCE(il.QtyBook, 0) AS QtyBook,
                                      COALESCE(il.QtyCount, 0) AS QtyCount
                               FROM M_InventoryLine il
                               JOIN M_Inventory i ON (il.M_Inventory_ID = i.M_Inventory_ID)
                               LEFT JOIN M_Locator loc ON (il.M_Locator_ID = loc.M_Locator_ID)
                               LEFT JOIN M_Warehouse w ON (loc.M_Warehouse_ID = w.M_Warehouse_ID)
                               LEFT JOIN M_AttributeSetInstance asi ON (il.M_AttributeSetInstance_ID = asi.M_AttributeSetInstance_ID)
                               WHERE i.IsActive = 'Y'
                                 AND il.IsActive = 'Y'
                                 AND COALESCE(i.IsInternalUse, 'N') = 'N'
                                 AND il.M_Product_ID = " + productId;

                sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "i", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                sql += " ORDER BY i.MovementDate DESC, i.M_Inventory_ID DESC";

                dr = DB.ExecuteReader(sql, null, null);
                while (dr != null && dr.Read())
                {
                    DateTime? movementDate = Util.GetValueOfDateTime(dr["MovementDate"]);
                    decimal qtyBook = Util.GetValueOfDecimal(dr["QtyBook"]);
                    decimal qtyCount = Util.GetValueOfDecimal(dr["QtyCount"]);

                    list.Add(new
                    {
                        inventoryId = Util.GetValueOfInt(dr["M_Inventory_ID"]),
                        documentNo = Util.GetValueOfString(dr["DocumentNo"]),
                        movementDate = (movementDate.HasValue && movementDate.Value != DateTime.MinValue)
                            ? movementDate.Value.ToString("dd MMM yyyy") : "",
                        docStatus = Util.GetValueOfString(dr["DocStatus"]),
                        warehouse = Util.GetValueOfString(dr["WarehouseName"]),
                        locator = Util.GetValueOfString(dr["LocatorCode"]),
                        attribute = Util.GetValueOfString(dr["AttributeDesc"]),
                        qtyBook = qtyBook,
                        qtyCount = qtyCount,
                        // Computed, never M_InventoryLine.DifferenceQty - that column is
                        // sign-inverted on this data (see VAS_159 / VAS_160).
                        variance = qtyCount - qtyBook
                    });
                }
            }
            catch (Exception ex)
            {
                _log.Severe("VAS_164_StockSearchWidgetController.GetProductCountHistory: " + ex.Message);
            }
            finally
            {
                if (dr != null)
                {
                    dr.Close();
                    dr.Dispose();
                }
            }

            return Json(new { counts = list, totalCounts = list.Count }, JsonRequestBehavior.AllowGet);
        }

        private string GetProductTypeLabel(string code)
        {
            switch (code)
            {
                case "I": return "Item";
                case "A": return "Assembly";
                case "C": return "Component";
                case "F": return "Finished Good";
                case "R": return "Raw Material";
                default: return "Item";
            }
        }
    }
}

