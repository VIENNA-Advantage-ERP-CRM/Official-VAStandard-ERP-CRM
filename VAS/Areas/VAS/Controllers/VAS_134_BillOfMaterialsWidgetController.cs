using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Web.Mvc;
using VAdvantage.DataBase;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VAS.Controllers
{
    /// <summary>
    /// Module Name : VAS_134_BillOfMaterialsWidget (Product Master dashboard)
    /// Purpose     : Data endpoints for the 2x1 "Bill of Materials" dual-stat
    ///               KPI tile and its two drill-down modals - the
    ///               manufacturing BOM model only (M_BOM header,
    ///               M_BOMProduct component lines). A parent/output product
    ///               belongs in this widget only when M_Product.IsBOM='Y';
    ///               verification state, revision, and the visible
    ///               update-actor/time all come from that SAME parent
    ///               M_Product row (IsVerified / VersionNo / UpdatedBy /
    ///               Updated) - never AD_ChangeLog, never a separate
    ///               submitted-by/verified-by/approval-event source (the
    ///               widget's confirmed business definition explicitly omits
    ///               those). Left stat = distinct active component-product
    ///               count across active BOMs; right stat = count of active
    ///               BOMs whose parent product is not verified (any value
    ///               other than 'Y' collapses to "not verified", no
    ///               intermediate state). MRole is applied to each block's
    ///               own primary table (M_BOM / M_BOMProduct); all input is
    ///               parameterized; the SQL uses only COALESCE / CASE /
    ///               window functions (no NVL, DECODE, LIMIT/OFFSET/FETCH,
    ///               DB date formatting or DB-specific upsert), so it runs
    ///               unchanged on Oracle and PostgreSQL. DB.ExecuteReader
    ///               binds parameters POSITIONALLY (matches the array to the
    ///               left-to-right order @placeholders appear in the final
    ///               SQL text, not by name) - a value needed in two places
    ///               gets two distinct placeholder names, each supplied once,
    ///               in that exact order (see the project convention in
    ///               InvoicesController's @Like1/@Like2 pattern). Both paged
    ///               endpoints are server-paginated (ROW_NUMBER + COUNT()
    ///               OVER(), 2-8 rows, validated server-side).
    /// Widget size : 2 columns x 1 row.
    /// Widget number 134.
    /// Chronological development:
    ///   &lt;EmpCode&gt;   2026-07-21 Created
    ///   Claude       2026-10-15 Widget_Bug_Fix Sr 36: "updated Nh ago" was wrong by the DB/browser
    ///                           timezone gap (Updated is stamped by the database clock, which can run in
    ///                           UTC) - each row now carries updatedAgeMinutes, measured on the database
    ///                           clock, and the widget shows that instead of comparing with the browser's
    ///                           clock. Sr 37: "Products in BOM" list ordering - most
    ///                           recently added BOM component first (see GetProductsInBom).
    ///   VAI163       2026-10-29 BOM Verification: "latest updated" now covers the BOM header and its
    ///                           lines as well as the parent product, so editing a BOM brings it to the
    ///                           top; verified and not-verified stay interleaved.
    /// </summary>
    public class VAS_134_BillOfMaterialsWidgetController : Controller
    {
        private const int MinPageSize = 2;
        private const int MaxPageSize = 8;

        /// <summary>
        /// Returns the distinct active-component-product count and the
        /// not-verified active-BOM count for the tile's two stats.
        /// </summary>
        /// <returns>JSON { productsInBom, pendingVerify }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetSummary()
        {
            if (Session["ctx"] == null)
            {
                return Fail(Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired");
            }

            Ctx ctx = Session["ctx"] as Ctx;

            try
            {
                string activeBoms = @"
                    SELECT b.M_BOM_ID, b.M_Product_ID, p.IsVerified
                    FROM M_BOM b
                    JOIN M_Product p ON (p.M_Product_ID = b.M_Product_ID AND p.AD_Client_ID = b.AD_Client_ID)
                    WHERE b.AD_Client_ID = @AD_Client_ID1
                      AND b.IsActive = 'Y'
                      AND p.IsActive = 'Y'
                      AND p.IsBOM = 'Y'";
                activeBoms = MRole.GetDefault(ctx).AddAccessSQL(activeBoms, "b", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                string componentProducts = @"
                    SELECT DISTINCT bp.M_ProductBOM_ID AS product_id
                    FROM active_boms ab
                    JOIN M_BOMProduct bp ON (bp.M_BOM_ID = ab.M_BOM_ID AND bp.IsActive = 'Y')
                    JOIN M_Product cp ON (cp.M_Product_ID = bp.M_ProductBOM_ID AND cp.IsActive = 'Y')
                    WHERE bp.AD_Client_ID = @AD_Client_ID2
                      AND cp.AD_Client_ID = @AD_Client_ID3";
                componentProducts = MRole.GetDefault(ctx).AddAccessSQL(componentProducts, "bp", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                string sql = @"
                    WITH active_boms AS (
                        " + activeBoms + @"
                    ),
                    component_products AS (
                        " + componentProducts + @"
                    ),
                    component_total AS (
                        SELECT COUNT(*) AS products_in_bom FROM component_products
                    ),
                    pending_total AS (
                        SELECT COUNT(*) AS pending_verify FROM active_boms WHERE COALESCE(IsVerified, 'N') <> 'Y'
                    )
                    SELECT ct.products_in_bom, pt.pending_verify
                    FROM component_total ct CROSS JOIN pending_total pt";

                SqlParameter[] parms = new SqlParameter[]
                {
                    new SqlParameter("@AD_Client_ID1", ctx.GetAD_Client_ID()),
                    new SqlParameter("@AD_Client_ID2", ctx.GetAD_Client_ID()),
                    new SqlParameter("@AD_Client_ID3", ctx.GetAD_Client_ID())
                };

                int productsInBom = 0, pendingVerify = 0;

                IDataReader dr = null;
                try
                {
                    dr = DB.ExecuteReader(sql, parms);
                    if (dr != null && dr.Read())
                    {
                        productsInBom = Util.GetValueOfInt(dr["products_in_bom"]);
                        pendingVerify = Util.GetValueOfInt(dr["pending_verify"]);
                    }
                }
                finally
                {
                    if (dr != null) { dr.Close(); dr.Dispose(); }
                }

                return Ok(new { productsInBom = productsInBom, pendingVerify = pendingVerify });
            }
            catch (Exception)
            {
                return Fail(Msg.GetMsg(ctx, "Error") ?? "Error");
            }
        }

        /// <summary>
        /// Returns one page (2-8 rows) of distinct products used as active
        /// BOM components, the most recently added component first (MAX of its
        /// M_BOMProduct.Created), then distinct-BOM usage count descending.
        /// </summary>
        /// <param name="offset">Zero-based result offset.</param>
        /// <param name="pageSize">Requested page size; clamped to 2-8.</param>
        /// <returns>JSON { total, offset, pageSize, rows[] }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetProductsInBom(int offset = 0, int pageSize = MaxPageSize)
        {
            if (Session["ctx"] == null)
            {
                return Fail(Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired");
            }

            Ctx ctx = Session["ctx"] as Ctx;

            if (offset < 0) { offset = 0; }
            if (pageSize < MinPageSize) { pageSize = MinPageSize; }
            if (pageSize > MaxPageSize) { pageSize = MaxPageSize; }

            try
            {
                string productUsage = @"
                    SELECT cp.M_Product_ID AS product_id, cp.Name AS product_name, cp.Value AS product_code,
                           pc.Name AS product_category, COUNT(DISTINCT b.M_BOM_ID) AS bom_count,
                           MAX(bp.Created) AS last_used
                    FROM M_BOMProduct bp
                    JOIN M_BOM b ON (b.M_BOM_ID = bp.M_BOM_ID AND b.AD_Client_ID = bp.AD_Client_ID AND b.IsActive = 'Y')
                    JOIN M_Product assembly ON (assembly.M_Product_ID = b.M_Product_ID AND assembly.AD_Client_ID = b.AD_Client_ID AND assembly.IsActive = 'Y' AND assembly.IsBOM = 'Y')
                    JOIN M_Product cp ON (cp.M_Product_ID = bp.M_ProductBOM_ID AND cp.AD_Client_ID = bp.AD_Client_ID AND cp.IsActive = 'Y')
                    LEFT JOIN M_Product_Category pc ON (pc.M_Product_Category_ID = cp.M_Product_Category_ID AND pc.AD_Client_ID = cp.AD_Client_ID)
                    WHERE bp.AD_Client_ID = @AD_Client_ID1
                      AND bp.IsActive = 'Y'";
                productUsage = MRole.GetDefault(ctx).AddAccessSQL(productUsage, "bp", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                productUsage += @"
                    GROUP BY cp.M_Product_ID, cp.Name, cp.Value, pc.Name";

                string sql = @"
                    WITH product_usage AS (
                        " + productUsage + @"
                    ),
                    ranked_products AS (
                        SELECT pu.product_id, pu.product_name, pu.product_code, pu.product_category, pu.bom_count,
                            COUNT(*) OVER () AS total_count,
                            ROW_NUMBER() OVER (
                                ORDER BY pu.last_used DESC, pu.bom_count DESC, pu.product_name ASC, pu.product_id ASC
                            ) AS row_num
                        FROM product_usage pu
                    )
                    SELECT product_id, product_name, product_code, product_category, bom_count, total_count
                    FROM ranked_products
                    WHERE row_num > @Offset1
                      AND row_num <= (@Offset2 + @PageSize)
                    ORDER BY row_num";

                SqlParameter[] parms = new SqlParameter[]
                {
                    new SqlParameter("@AD_Client_ID1", ctx.GetAD_Client_ID()),
                    new SqlParameter("@Offset1", offset),
                    new SqlParameter("@Offset2", offset),
                    new SqlParameter("@PageSize", pageSize)
                };

                List<object> rows = new List<object>();
                int total = 0;

                IDataReader dr = null;
                try
                {
                    dr = DB.ExecuteReader(sql, parms);
                    while (dr != null && dr.Read())
                    {
                        total = Util.GetValueOfInt(dr["total_count"]);
                        rows.Add(new
                        {
                            productId = Util.GetValueOfInt(dr["product_id"]),
                            productName = Util.GetValueOfString(dr["product_name"]),
                            productCode = Util.GetValueOfString(dr["product_code"]),
                            productCategory = Util.GetValueOfString(dr["product_category"]),
                            bomCount = Util.GetValueOfInt(dr["bom_count"])
                        });
                    }
                }
                finally
                {
                    if (dr != null) { dr.Close(); dr.Dispose(); }
                }

                return Ok(new { total = total, offset = offset, pageSize = pageSize, rows = rows });
            }
            catch (Exception)
            {
                return Fail(Msg.GetMsg(ctx, "Error") ?? "Error");
            }
        }

        /// <summary>
        /// Returns one page (2-8 rows) of active BOMs with verification
        /// status, most recently updated first - verified and not-verified
        /// rows are interleaved, not grouped.
        /// </summary>
        /// <param name="offset">Zero-based result offset.</param>
        /// <param name="pageSize">Requested page size; clamped to 2-8.</param>
        /// <returns>JSON { total, notVerifiedCount, offset, pageSize, rows[] }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetVerification(int offset = 0, int pageSize = MaxPageSize)
        {
            if (Session["ctx"] == null)
            {
                return Fail(Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired");
            }

            Ctx ctx = Session["ctx"] as Ctx;

            if (offset < 0) { offset = 0; }
            if (pageSize < MinPageSize) { pageSize = MinPageSize; }
            if (pageSize > MaxPageSize) { pageSize = MaxPageSize; }

            try
            {
                string bomRows = @"
                    SELECT b.M_BOM_ID AS bom_id, b.Name AS bom_code, p.M_Product_ID AS assembly_product_id,
                           p.Name AS assembly_name, p.VersionNo AS revision, p.IsVerified AS raw_verified,
                           p.UpdatedBy AS product_updated_by, p.Updated AS product_updated,
                           b.UpdatedBy AS bom_updated_by, b.Updated AS bom_updated
                    FROM M_BOM b
                    JOIN M_Product p ON (p.M_Product_ID = b.M_Product_ID AND p.AD_Client_ID = b.AD_Client_ID)
                    WHERE b.AD_Client_ID = @AD_Client_ID1
                      AND b.IsActive = 'Y'
                      AND p.IsActive = 'Y'
                      AND p.IsBOM = 'Y'";
                bomRows = MRole.GetDefault(ctx).AddAccessSQL(bomRows, "b", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                // "Latest updated" is the newest change to the BOM as a whole -
                // the parent product, the BOM header or any of its lines (a
                // deactivated line is a change too, so all lines count here) -
                // and the user shown is whoever made that change.
                string sql = @"
                    WITH bom_rows AS (
                        " + bomRows + @"
                    ),
                    line_stats AS (
                        SELECT bp.M_BOM_ID AS bom_id,
                            SUM(CASE WHEN bp.IsActive = 'Y' THEN 1 ELSE 0 END) AS component_count,
                            MAX(bp.Updated) AS line_updated
                        FROM M_BOMProduct bp
                        JOIN bom_rows br ON (br.bom_id = bp.M_BOM_ID)
                        WHERE bp.AD_Client_ID = @AD_Client_ID2
                        GROUP BY bp.M_BOM_ID
                    ),
                    line_last AS (
                        SELECT bp.M_BOM_ID AS bom_id, MAX(bp.UpdatedBy) AS line_updated_by
                        FROM M_BOMProduct bp
                        JOIN line_stats ls ON (ls.bom_id = bp.M_BOM_ID AND bp.Updated = ls.line_updated)
                        GROUP BY bp.M_BOM_ID
                    ),
                    latest_change AS (
                        SELECT br.bom_id, br.bom_code, br.assembly_product_id, br.assembly_name, br.revision, br.raw_verified,
                            COALESCE(ls.component_count, 0) AS component_count,
                            CASE WHEN ls.line_updated IS NOT NULL AND ls.line_updated > br.bom_updated
                                      AND ls.line_updated > br.product_updated THEN ls.line_updated
                                 WHEN br.bom_updated > br.product_updated THEN br.bom_updated
                                 ELSE br.product_updated END AS updated_at,
                            CASE WHEN ls.line_updated IS NOT NULL AND ls.line_updated > br.bom_updated
                                      AND ls.line_updated > br.product_updated THEN ll.line_updated_by
                                 WHEN br.bom_updated > br.product_updated THEN br.bom_updated_by
                                 ELSE br.product_updated_by END AS updated_by_id
                        FROM bom_rows br
                        LEFT JOIN line_stats ls ON (ls.bom_id = br.bom_id)
                        LEFT JOIN line_last ll ON (ll.bom_id = br.bom_id)
                    ),
                    normalized_boms AS (
                        SELECT lc.bom_id, lc.bom_code, lc.assembly_product_id, lc.assembly_name, lc.revision, lc.component_count,
                            CASE WHEN COALESCE(lc.raw_verified, 'N') = 'Y' THEN 'Y' ELSE 'N' END AS is_verified,
                            lc.updated_by_id, u.Name AS updated_by_name, lc.updated_at
                        FROM latest_change lc
                        LEFT JOIN AD_User u ON (u.AD_User_ID = lc.updated_by_id)
                    ),
                    ranked_boms AS (
                        SELECT nb.bom_id, nb.bom_code, nb.assembly_product_id, nb.assembly_name, nb.revision, nb.component_count,
                            nb.is_verified, nb.updated_by_id, nb.updated_by_name, nb.updated_at,
                            COUNT(*) OVER () AS total_count,
                            SUM(CASE WHEN nb.is_verified = 'N' THEN 1 ELSE 0 END) OVER () AS not_verified_count,
                            ROW_NUMBER() OVER (
                                ORDER BY nb.updated_at DESC, nb.bom_id DESC
                            ) AS row_num
                        FROM normalized_boms nb
                    )
                    SELECT bom_id, bom_code, assembly_product_id, assembly_name, revision, component_count, is_verified,
                        updated_by_id, updated_by_name, updated_at, total_count, not_verified_count
                    FROM ranked_boms
                    WHERE row_num > @Offset1
                      AND row_num <= (@Offset2 + @PageSize)
                    ORDER BY row_num";

                SqlParameter[] parms = new SqlParameter[]
                {
                    new SqlParameter("@AD_Client_ID1", ctx.GetAD_Client_ID()),
                    new SqlParameter("@AD_Client_ID2", ctx.GetAD_Client_ID()),
                    new SqlParameter("@Offset1", offset),
                    new SqlParameter("@Offset2", offset),
                    new SqlParameter("@PageSize", pageSize)
                };

                List<object> rows = new List<object>();
                int total = 0, notVerifiedCount = 0;
                // "x ago" is measured on the DATABASE clock (see GetDbNow), not the web server's or the browser's.
                DateTime? dbNow = GetDbNow();

                IDataReader dr = null;
                try
                {
                    dr = DB.ExecuteReader(sql, parms);
                    while (dr != null && dr.Read())
                    {
                        total = Util.GetValueOfInt(dr["total_count"]);
                        notVerifiedCount = Util.GetValueOfInt(dr["not_verified_count"]);
                        DateTime? updatedAt = Util.GetValueOfDateTime(dr["updated_at"]);
                        int? ageMinutes = (updatedAt.HasValue && dbNow.HasValue)
                            ? (int?)Math.Max(0, (int)Math.Floor((dbNow.Value - updatedAt.Value).TotalMinutes))
                            : null;
                        rows.Add(new
                        {
                            bomId = Util.GetValueOfInt(dr["bom_id"]),
                            bomCode = Util.GetValueOfString(dr["bom_code"]),
                            assemblyProductId = Util.GetValueOfInt(dr["assembly_product_id"]),
                            assemblyName = Util.GetValueOfString(dr["assembly_name"]),
                            revision = Util.GetValueOfString(dr["revision"]),
                            componentCount = Util.GetValueOfInt(dr["component_count"]),
                            isVerified = "Y".Equals(Util.GetValueOfString(dr["is_verified"]), StringComparison.OrdinalIgnoreCase),
                            updatedById = Util.GetValueOfInt(dr["updated_by_id"]),
                            updatedByName = Util.GetValueOfString(dr["updated_by_name"]),
                            updatedAt = updatedAt.HasValue ? updatedAt.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : "",
                            updatedAgeMinutes = ageMinutes
                        });
                    }
                }
                finally
                {
                    if (dr != null) { dr.Close(); dr.Dispose(); }
                }

                return Ok(new { total = total, notVerifiedCount = notVerifiedCount, offset = offset, pageSize = pageSize, rows = rows });
            }
            catch (Exception)
            {
                return Fail(Msg.GetMsg(ctx, "Error") ?? "Error");
            }
        }

        /// <summary>
        /// Current time on the DATABASE clock, the clock that stamps Created / Updated (the Receipt Queue
        /// widget already documents it: the DB can run in UTC while the web server and the users sit in
        /// other zones). Comparing a stored timestamp with the web server's or the browser's "now" is off by
        /// the zone gap - a record saved moments ago read "5h ago". Age = this value - stored timestamp.
        /// </summary>
        private static DateTime? GetDbNow()
        {
            string sql = DB.IsPostgreSQL() ? "SELECT LOCALTIMESTAMP"
                       : DB.IsOracle() ? "SELECT SYSDATE FROM DUAL"
                       : "SELECT NOW()";
            return Util.GetValueOfDateTime(DB.ExecuteScalar(sql, new SqlParameter[0], null));
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
