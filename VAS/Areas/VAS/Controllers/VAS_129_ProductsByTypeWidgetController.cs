using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Web.Mvc;
using VAdvantage.DataBase;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VAS.Controllers
{
    /// <summary>
    /// Module Name : VAS_129_ProductsByTypeWidget (Product Master dashboard)
    /// Purpose     : Data endpoints for the 3x2 "Products by Type" donut widget -
    ///               a catalog-composition view split into the four fixed
    ///               product types (Item / Service / Resource / Expense).
    ///               GetTypeCounts returns the count of active+inactive,
    ///               non-summary products per type (missing types come back as
    ///               0 so the legend always shows all four). GetProductsByType
    ///               returns the drill-down product list for one validated type,
    ///               with category, base UOM, product-level attribute chips
    ///               (M_Product.M_AttributeSetInstance_ID chain), and status;
    ///               attribute rows are grouped into chips per product in C#,
    ///               never aggregated in SQL. MRole is applied to the primary
    ///               fetched table (M_Product) on both queries; all input is
    ///               parameterized; the SQL uses only COALESCE / CASE (no NVL,
    ///               DECODE, LISTAGG, FETCH/LIMIT/OFFSET, DB date formatting or
    ///               DB-specific upsert), so it runs unchanged on Oracle and
    ///               PostgreSQL. Pagination of the drill-down list is done in
    ///               the client (measured page size), not in SQL.
    /// Widget size : 3 columns x 2 rows.
    /// Widget number 129.
    /// Chronological development:
    ///   &lt;EmpCode&gt;   2026-07-20 Created
    /// </summary>
    public class VAS_129_ProductsByTypeWidgetController : Controller
    {
        // Fixed type catalog, in required donut/legend order.
        private static readonly string[] TypeOrder = { "I", "S", "R", "E" };

        /// <summary>
        /// Returns the product count for each of the four fixed product types
        /// (missing types come back as 0) and the grand total.
        /// </summary>
        /// <returns>JSON { counts: { I, S, R, E }, total }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetTypeCounts()
        {
            if (Session["ctx"] == null)
            {
                return Fail(Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired");
            }

            Ctx ctx = Session["ctx"] as Ctx;

            try
            {
                Dictionary<string, int> counts = TypeOrder.ToDictionary(t => t, t => 0);

                string sql = @"
                    SELECT p.ProductType AS ProductType,
                           COUNT(*) AS ProductCount
                    FROM M_Product p
                    WHERE p.AD_Client_ID = @AD_Client_ID
                      AND COALESCE(p.IsSummary, 'N') = 'N'
                      AND p.ProductType IN ('I', 'S', 'R', 'E')";

                // AddAccessSQL appends its predicate to the END of the string, so
                // GROUP BY is appended after the call (widget rule #1).
                sql = MRole.GetDefault(ctx).AddAccessSQL(
                    sql,
                    "p",
                    MRole.SQL_FULLYQUALIFIED,
                    MRole.SQL_RO
                );
                sql += " GROUP BY p.ProductType";

                IDataReader dr = null;
                try
                {
                    dr = DB.ExecuteReader(sql, new SqlParameter[]
                    {
                        new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
                    });

                    while (dr != null && dr.Read())
                    {
                        string type = Util.GetValueOfString(dr["ProductType"]);
                        if (counts.ContainsKey(type))
                        {
                            counts[type] = Util.GetValueOfInt(dr["ProductCount"]);
                        }
                    }
                }
                finally
                {
                    if (dr != null) { dr.Close(); dr.Dispose(); }
                }

                int total = counts.Values.Sum();

                return Ok(new
                {
                    counts = new
                    {
                        I = counts["I"],
                        S = counts["S"],
                        R = counts["R"],
                        E = counts["E"]
                    },
                    total = total
                });
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
            }
        }

        /// <summary>
        /// Returns every active/inactive, non-summary product of one validated
        /// type, with category, base UOM, deduplicated attribute chips, and
        /// status. One row per product+attribute is grouped into a single item
        /// with an attribute list in application code.
        /// </summary>
        /// <param name="productType">One of I, S, R, E.</param>
        /// <returns>JSON { type, items[] }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetProductsByType(string productType = "")
        {
            if (Session["ctx"] == null)
            {
                return Fail(Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired");
            }

            Ctx ctx = Session["ctx"] as Ctx;

            // Validate against the fixed allow-list before executing the query.
            if (Array.IndexOf(TypeOrder, productType) < 0)
            {
                return Fail("Unknown product type.");
            }

            try
            {
                string sql = @"
                    SELECT p.M_Product_ID AS ProductId,
                           p.Name AS ProductName,
                           p.Value AS ProductCode,
                           pc.Name AS CategoryName,
                           u.Name AS UomName,
                           u.UOMSymbol AS UomSymbol,
                           p.IsActive AS IsActive,
                           ai.M_AttributeInstance_ID AS AttributeInstanceId,
                           a.M_Attribute_ID AS AttributeId,
                           a.Name AS AttributeName,
                           av.Name AS AttributeListValue,
                           ai.Value AS AttributeTextValue,
                           ai.ValueNumber AS AttributeNumberValue
                    FROM M_Product p
                    JOIN M_Product_Category pc ON (pc.M_Product_Category_ID = p.M_Product_Category_ID)
                    LEFT JOIN C_UOM u ON (u.C_UOM_ID = p.C_UOM_ID)
                    LEFT JOIN M_AttributeInstance ai ON (ai.M_AttributeSetInstance_ID = p.M_AttributeSetInstance_ID AND ai.IsActive = 'Y')
                    LEFT JOIN M_Attribute a ON (a.M_Attribute_ID = ai.M_Attribute_ID AND a.IsActive = 'Y')
                    LEFT JOIN M_AttributeValue av ON (av.M_AttributeValue_ID = ai.M_AttributeValue_ID AND av.M_Attribute_ID = ai.M_Attribute_ID AND av.IsActive = 'Y')
                    WHERE p.AD_Client_ID = @AD_Client_ID
                      AND COALESCE(p.IsSummary, 'N') = 'N'
                      AND p.ProductType = @ProductType";

                // AddAccessSQL appends its predicate to the END, so ORDER BY is
                // appended after the call (widget rule #1).
                sql = MRole.GetDefault(ctx).AddAccessSQL(
                    sql,
                    "p",
                    MRole.SQL_FULLYQUALIFIED,
                    MRole.SQL_RO
                );
                sql += " ORDER BY p.Name, p.Value, a.Name, av.Name";

                // Preserve first-seen product order while grouping attribute rows.
                List<int> order = new List<int>();
                Dictionary<int, ProductRow> byId = new Dictionary<int, ProductRow>();

                IDataReader dr = null;
                try
                {
                    dr = DB.ExecuteReader(sql, new SqlParameter[]
                    {
                        new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                        new SqlParameter("@ProductType", productType)
                    });

                    while (dr != null && dr.Read())
                    {
                        int productId = Util.GetValueOfInt(dr["ProductId"]);

                        ProductRow row;
                        if (!byId.TryGetValue(productId, out row))
                        {
                            string uomSymbol = Util.GetValueOfString(dr["UomSymbol"]);
                            string uomName = Util.GetValueOfString(dr["UomName"]);
                            row = new ProductRow
                            {
                                productId = productId,
                                name = Util.GetValueOfString(dr["ProductName"]),
                                code = Util.GetValueOfString(dr["ProductCode"]),
                                category = Util.GetValueOfString(dr["CategoryName"]),
                                uom = !string.IsNullOrEmpty(uomName) ? uomName : uomSymbol,
                                isActive = "Y".Equals(Util.GetValueOfString(dr["IsActive"]), StringComparison.OrdinalIgnoreCase),
                                attributes = new List<ProductAttribute>(),
                                seenChips = new HashSet<string>()
                            };
                            byId[productId] = row;
                            order.Add(productId);
                        }

                        // Attribute value priority: list value, then nonblank text,
                        // then number. Rows with no attribute label are skipped.
                        string label = Util.GetValueOfString(dr["AttributeName"]);
                        if (string.IsNullOrEmpty(label)) { continue; }

                        string value = Util.GetValueOfString(dr["AttributeListValue"]);
                        if (string.IsNullOrEmpty(value)) { value = Util.GetValueOfString(dr["AttributeTextValue"]); }
                        if (string.IsNullOrEmpty(value) && dr["AttributeNumberValue"] != DBNull.Value)
                        {
                            value = Util.GetValueOfDecimal(dr["AttributeNumberValue"]).ToString("0.####", CultureInfo.InvariantCulture);
                        }
                        if (string.IsNullOrEmpty(value)) { continue; }

                        string chipKey = label + ":" + value;
                        if (row.seenChips.Contains(chipKey)) { continue; }
                        row.seenChips.Add(chipKey);
                        row.attributes.Add(new ProductAttribute { label = label, value = value });
                    }
                }
                finally
                {
                    if (dr != null) { dr.Close(); dr.Dispose(); }
                }

                List<object> items = order.Select(id =>
                {
                    ProductRow row = byId[id];
                    return (object)new
                    {
                        productId = row.productId,
                        name = row.name,
                        code = row.code,
                        category = row.category,
                        uom = row.uom,
                        attributes = row.attributes,
                        isActive = row.isActive
                    };
                }).ToList();

                return Ok(new { type = productType, items = items });
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
            }
        }

        /// <summary>Intermediate accumulator for one product while grouping attribute rows.</summary>
        private class ProductRow
        {
            public int productId;
            public string name;
            public string code;
            public string category;
            public string uom;
            public bool isActive;
            public List<ProductAttribute> attributes;
            public HashSet<string> seenChips;
        }

        /// <summary>One resolved attribute chip.</summary>
        private class ProductAttribute
        {
            public string label { get; set; }
            public string value { get; set; }
        }

        /// <summary>Wraps a success payload as a serialized JSON result.</summary>
        private JsonResult Ok(object result)
        {
            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }

        /// <summary>Wraps a failure message as a serialized JSON result.</summary>
        private JsonResult Fail(string message)
        {
            return Json(JsonConvert.SerializeObject(new
            {
                success = false,
                error = message,
                message = message
            }), JsonRequestBehavior.AllowGet);
        }
    }
}
