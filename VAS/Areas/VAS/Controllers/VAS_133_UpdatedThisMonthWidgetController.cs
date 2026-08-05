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
    /// Module Name : VAS_133_UpdatedThisMonthWidget (Product Master dashboard)
    /// Purpose     : Data endpoints for the 2x1 "Updated This Month" KPI tile
    ///               and its drill-down modal - a LATEST-RECORD update view
    ///               (not AD_ChangeLog, not a field-level audit trail): one
    ///               row for every M_Product whose own Updated timestamp
    ///               falls in the current calendar month, and one row for
    ///               every M_ProductPrice whose own Updated timestamp falls
    ///               in the current month. M_Product rows display as
    ///               "Attribute"; M_ProductPrice rows display as "Price".
    ///               No New/Review/Deleted categories, no other product
    ///               child table. Inactive records are NOT filtered out (a
    ///               deactivation is itself a valid update). Month/today
    ///               boundaries are computed once in C# (server-local clock)
    ///               and passed as half-open bind parameters - never derived
    ///               with a DB-specific date function - so the same SQL runs
    ///               unchanged on Oracle and PostgreSQL. MRole is applied to
    ///               each UNION branch's own primary table (M_Product /
    ///               M_ProductPrice). The paged endpoint is server-paginated
    ///               (ROW_NUMBER + COUNT() OVER(), 2-8 rows, validated
    ///               server-side) - the client never receives more than one
    ///               page's worth of rows.
    /// Widget size : 2 columns x 1 row.
    /// Widget number 133.
    /// Chronological development:
    ///   &lt;EmpCode&gt;   2026-07-21 Created
    /// </summary>
    public class VAS_133_UpdatedThisMonthWidgetController : Controller
    {
        private const int MinPageSize = 2;
        private const int MaxPageSize = 8;

        /// <summary>
        /// Returns the current-month update count, distinct updater count,
        /// today's update count, and distinct product count - all from one
        /// query pass so they stay consistent with the modal's totals.
        /// </summary>
        /// <returns>JSON { updateCount, userCount, todayCount, productCount, monthStart }.</returns>
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
                DateBounds bounds = ResolveDateBounds();

                string sql = @"
                    WITH updated_records AS (
                        " + ProductBranch(ctx) + @"
                        UNION ALL
                        " + PriceBranch(ctx) + @"
                        UNION ALL
                        " + BomBranch(ctx) + @"
                        UNION ALL
                        " + BomProductBranch(ctx) + @"
                        UNION ALL
                        " + ProductPoBranch(ctx) + @"
                    )
                    SELECT
                        COUNT(*) AS UpdateCount,
                        COUNT(DISTINCT updated_by) AS UserCount,
                        COALESCE(SUM(CASE WHEN updated_at >= @TodayStart AND updated_at < @TomorrowStart THEN 1 ELSE 0 END), 0) AS TodayCount,
                        COUNT(DISTINCT product_id) AS ProductCount
                    FROM updated_records";

                int updateCount = 0, userCount = 0, todayCount = 0, productCount = 0;

                IDataReader dr = null;
                try
                {
                    dr = DB.ExecuteReader(sql, BuildDateParams(ctx, bounds));
                    if (dr != null && dr.Read())
                    {
                        updateCount = Util.GetValueOfInt(dr["UpdateCount"]);
                        userCount = Util.GetValueOfInt(dr["UserCount"]);
                        todayCount = Util.GetValueOfInt(dr["TodayCount"]);
                        productCount = Util.GetValueOfInt(dr["ProductCount"]);
                    }
                }
                finally
                {
                    if (dr != null) { dr.Close(); dr.Dispose(); }
                }

                return Ok(new
                {
                    updateCount = updateCount,
                    userCount = userCount,
                    todayCount = todayCount,
                    productCount = productCount,
                    monthStart = bounds.MonthStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                });
            }
            catch (Exception)
            {
                return Fail(Msg.GetMsg(ctx, "Error") ?? "Error");
            }
        }

        /// <summary>
        /// Returns one page (2-8 rows) of this month's product/price update
        /// records, newest first, with the total row count for pagination.
        /// </summary>
        /// <param name="offset">Zero-based result offset.</param>
        /// <param name="pageSize">Requested page size; clamped to 2-8.</param>
        /// <returns>JSON { total, offset, pageSize, rows[] }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetPagedUpdates(int offset = 0, int pageSize = MaxPageSize)
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
                DateBounds bounds = ResolveDateBounds();

                string productBranch = @"
                    SELECT
                        " + NLiteral(ctx, "PRODUCT") + @" AS source_type,
                        p.M_Product_ID AS source_record_id,
                        p.M_Product_ID AS product_id,
                        p.Name AS product_name,
                        " + NLiteral(ctx, "Product details updated") + @" AS update_description,
                        " + NLiteral(ctx, "Attribute") + @" AS update_category,
                        p.UpdatedBy AS updated_by_id,
                        p.Updated AS updated_at
                    FROM M_Product p
                    WHERE p.AD_Client_ID = @AD_Client_ID1
                      AND p.Updated >= @MonthStart1
                      AND p.Updated < @NextMonthStart1";
                productBranch = MRole.GetDefault(ctx).AddAccessSQL(productBranch, "p", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                string priceBranch = @"
                    SELECT
                        " + NLiteral(ctx, "PRICE") + @" AS source_type,
                        pp.M_ProductPrice_ID AS source_record_id,
                        pp.M_Product_ID AS product_id,
                        p.Name AS product_name,
                        " + NLiteral(ctx, "Product price updated") + @" AS update_description,
                        " + NLiteral(ctx, "Price") + @" AS update_category,
                        pp.UpdatedBy AS updated_by_id,
                        pp.Updated AS updated_at
                    FROM M_ProductPrice pp
                    JOIN M_Product p ON (p.M_Product_ID = pp.M_Product_ID AND p.AD_Client_ID = pp.AD_Client_ID)
                    WHERE pp.AD_Client_ID = @AD_Client_ID2
                      AND pp.Updated >= @MonthStart2
                      AND pp.Updated < @NextMonthStart2";
                priceBranch = MRole.GetDefault(ctx).AddAccessSQL(priceBranch, "pp", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                string bomBranch = @"
                    SELECT
                        " + NLiteral(ctx, "BOM") + @" AS source_type,
                        b.M_BOM_ID AS source_record_id,
                        b.M_Product_ID AS product_id,
                        p.Name AS product_name,
                        " + NLiteral(ctx, "Bill of materials updated") + @" AS update_description,
                        " + NLiteral(ctx, "BOM") + @" AS update_category,
                        b.UpdatedBy AS updated_by_id,
                        b.Updated AS updated_at
                    FROM M_BOM b
                    JOIN M_Product p ON (p.M_Product_ID = b.M_Product_ID AND p.AD_Client_ID = b.AD_Client_ID)
                    WHERE b.AD_Client_ID = @AD_Client_ID3
                      AND b.Updated >= @MonthStart3
                      AND b.Updated < @NextMonthStart3";
                bomBranch = MRole.GetDefault(ctx).AddAccessSQL(bomBranch, "b", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                string bomProductBranch = @"
                    SELECT
                        " + NLiteral(ctx, "BOMPRODUCT") + @" AS source_type,
                        bp.M_BOMProduct_ID AS source_record_id,
                        b.M_Product_ID AS product_id,
                        p.Name AS product_name,
                        " + NLiteral(ctx, "BOM line updated") + @" AS update_description,
                        " + NLiteral(ctx, "BOM Line") + @" AS update_category,
                        bp.UpdatedBy AS updated_by_id,
                        bp.Updated AS updated_at
                    FROM M_BOMProduct bp
                    JOIN M_BOM b ON (b.M_BOM_ID = bp.M_BOM_ID AND b.AD_Client_ID = bp.AD_Client_ID)
                    JOIN M_Product p ON (p.M_Product_ID = b.M_Product_ID AND p.AD_Client_ID = b.AD_Client_ID)
                    WHERE bp.AD_Client_ID = @AD_Client_ID4
                      AND bp.Updated >= @MonthStart4
                      AND bp.Updated < @NextMonthStart4";
                bomProductBranch = MRole.GetDefault(ctx).AddAccessSQL(bomProductBranch, "bp", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                string productPoBranch = @"
                    SELECT
                        " + NLiteral(ctx, "PRODUCTPO") + @" AS source_type,
                        po.M_Product_PO_ID AS source_record_id,
                        po.M_Product_ID AS product_id,
                        p.Name AS product_name,
                        " + NLiteral(ctx, "Purchasing details updated") + @" AS update_description,
                        " + NLiteral(ctx, "Purchasing") + @" AS update_category,
                        po.UpdatedBy AS updated_by_id,
                        po.Updated AS updated_at
                    FROM M_Product_PO po
                    JOIN M_Product p ON (p.M_Product_ID = po.M_Product_ID AND p.AD_Client_ID = po.AD_Client_ID)
                    WHERE po.AD_Client_ID = @AD_Client_ID5
                      AND po.Updated >= @MonthStart5
                      AND po.Updated < @NextMonthStart5";
                productPoBranch = MRole.GetDefault(ctx).AddAccessSQL(productPoBranch, "po", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                string sql = @"
                    WITH updated_records AS (
                        " + productBranch + @"
                        UNION ALL
                        " + priceBranch + @"
                        UNION ALL
                        " + bomBranch + @"
                        UNION ALL
                        " + bomProductBranch + @"
                        UNION ALL
                        " + productPoBranch + @"
                    ),
                    ranked_updates AS (
                        SELECT
                            ur.source_type, ur.source_record_id, ur.product_id, ur.product_name,
                            ur.update_description, ur.update_category, ur.updated_by_id,
                            u.Name AS updated_by_name, ur.updated_at,
                            COUNT(*) OVER () AS total_count,
                            ROW_NUMBER() OVER (
                                ORDER BY ur.updated_at DESC, ur.source_type ASC, ur.source_record_id DESC
                            ) AS row_num
                        FROM updated_records ur
                        LEFT JOIN AD_User u ON (u.AD_User_ID = ur.updated_by_id)
                    )
                    SELECT
                        source_type, source_record_id, product_id, product_name,
                        update_description, update_category, updated_by_id, updated_by_name,
                        updated_at, total_count
                    FROM ranked_updates
                    WHERE row_num > @Offset1
                      AND row_num <= (@Offset2 + @PageSize)
                    ORDER BY row_num";

                List<SqlParameter> parms = new List<SqlParameter>();
                parms.Add(new SqlParameter("@AD_Client_ID1", ctx.GetAD_Client_ID()));
                parms.Add(new SqlParameter("@MonthStart1", SqlDbType.DateTime) { Value = bounds.MonthStart });
                parms.Add(new SqlParameter("@NextMonthStart1", SqlDbType.DateTime) { Value = bounds.NextMonthStart });
                parms.Add(new SqlParameter("@AD_Client_ID2", ctx.GetAD_Client_ID()));
                parms.Add(new SqlParameter("@MonthStart2", SqlDbType.DateTime) { Value = bounds.MonthStart });
                parms.Add(new SqlParameter("@NextMonthStart2", SqlDbType.DateTime) { Value = bounds.NextMonthStart });
                parms.Add(new SqlParameter("@AD_Client_ID3", ctx.GetAD_Client_ID()));
                parms.Add(new SqlParameter("@MonthStart3", SqlDbType.DateTime) { Value = bounds.MonthStart });
                parms.Add(new SqlParameter("@NextMonthStart3", SqlDbType.DateTime) { Value = bounds.NextMonthStart });
                parms.Add(new SqlParameter("@AD_Client_ID4", ctx.GetAD_Client_ID()));
                parms.Add(new SqlParameter("@MonthStart4", SqlDbType.DateTime) { Value = bounds.MonthStart });
                parms.Add(new SqlParameter("@NextMonthStart4", SqlDbType.DateTime) { Value = bounds.NextMonthStart });
                parms.Add(new SqlParameter("@AD_Client_ID5", ctx.GetAD_Client_ID()));
                parms.Add(new SqlParameter("@MonthStart5", SqlDbType.DateTime) { Value = bounds.MonthStart });
                parms.Add(new SqlParameter("@NextMonthStart5", SqlDbType.DateTime) { Value = bounds.NextMonthStart });
                parms.Add(new SqlParameter("@Offset1", offset));
                parms.Add(new SqlParameter("@Offset2", offset));
                parms.Add(new SqlParameter("@PageSize", pageSize));

                List<object> rows = new List<object>();
                int total = 0;

                IDataReader dr = null;
                try
                {
                    dr = DB.ExecuteReader(sql, parms.ToArray());
                    while (dr != null && dr.Read())
                    {
                        total = Util.GetValueOfInt(dr["total_count"]);
                        DateTime? updatedAt = Util.GetValueOfDateTime(dr["updated_at"]);
                        string updatedByName = Util.GetValueOfString(dr["updated_by_name"]);
                        rows.Add(new
                        {
                            sourceType = Util.GetValueOfString(dr["source_type"]),
                            sourceRecordId = Util.GetValueOfInt(dr["source_record_id"]),
                            productId = Util.GetValueOfInt(dr["product_id"]),
                            productName = Util.GetValueOfString(dr["product_name"]),
                            description = Util.GetValueOfString(dr["update_description"]),
                            category = Util.GetValueOfString(dr["update_category"]),
                            updatedById = Util.GetValueOfInt(dr["updated_by_id"]),
                            updatedByName = updatedByName,
                            updatedAt = updatedAt.HasValue ? updatedAt.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : ""
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
        /// M_Product branch of the summary UNION, MRole-secured on "p". Uses
        /// the "1"-suffixed date parameters (see BuildDateParams) since
        /// DB.ExecuteReader binds positionally and this branch's placeholders
        /// are textually distinct from the price branch's.
        /// </summary>
        private string ProductBranch(Ctx ctx)
        {
            string sql = @"
                SELECT p.M_Product_ID AS product_id, p.UpdatedBy AS updated_by, p.Updated AS updated_at
                FROM M_Product p
                WHERE p.AD_Client_ID = @AD_Client_ID1
                  AND p.Updated >= @MonthStart1
                  AND p.Updated < @NextMonthStart1";
            return MRole.GetDefault(ctx).AddAccessSQL(sql, "p", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
        }

        /// <summary>
        /// M_ProductPrice branch of the summary UNION, MRole-secured on "pp".
        /// Uses the "2"-suffixed date parameters (see BuildDateParams).
        /// </summary>
        private string PriceBranch(Ctx ctx)
        {
            string sql = @"
                SELECT pp.M_Product_ID AS product_id, pp.UpdatedBy AS updated_by, pp.Updated AS updated_at
                FROM M_ProductPrice pp
                JOIN M_Product p ON (p.M_Product_ID = pp.M_Product_ID AND p.AD_Client_ID = pp.AD_Client_ID)
                WHERE pp.AD_Client_ID = @AD_Client_ID2
                  AND pp.Updated >= @MonthStart2
                  AND pp.Updated < @NextMonthStart2";
            return MRole.GetDefault(ctx).AddAccessSQL(sql, "pp", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
        }

        /// <summary>
        /// Parameter set for the summary query. DB.ExecuteReader binds
        /// POSITIONALLY (matches this array to the left-to-right order
        /// @placeholders appear in the final assembled SQL text, not by
        /// name) - since ProductBranch and PriceBranch each reference their
        /// own AD_Client_ID/MonthStart/NextMonthStart placeholders (distinct
        /// textual occurrences), each needs its own "1"/"2"-suffixed entry
        /// here, supplied in the exact order the branches appear in the SQL:
        /// product branch first, then price branch, then the final SELECT's
        /// TodayStart/TomorrowStart. See the project convention in
        /// InvoicesController's @Like1/@Like2 pattern.
        /// </summary>
        private string BomBranch(Ctx ctx)
        {
            string sql = @"
                SELECT b.M_Product_ID AS product_id, b.UpdatedBy AS updated_by, b.Updated AS updated_at
                FROM M_BOM b
                JOIN M_Product p ON (p.M_Product_ID = b.M_Product_ID AND p.AD_Client_ID = b.AD_Client_ID)
                WHERE b.AD_Client_ID = @AD_Client_ID3
                  AND b.Updated >= @MonthStart3
                  AND b.Updated < @NextMonthStart3";
            return MRole.GetDefault(ctx).AddAccessSQL(sql, "b", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
        }

        private string BomProductBranch(Ctx ctx)
        {
            string sql = @"
                SELECT b.M_Product_ID AS product_id, bp.UpdatedBy AS updated_by, bp.Updated AS updated_at
                FROM M_BOMProduct bp
                JOIN M_BOM b ON (b.M_BOM_ID = bp.M_BOM_ID AND b.AD_Client_ID = bp.AD_Client_ID)
                JOIN M_Product p ON (p.M_Product_ID = b.M_Product_ID AND p.AD_Client_ID = b.AD_Client_ID)
                WHERE bp.AD_Client_ID = @AD_Client_ID4
                  AND bp.Updated >= @MonthStart4
                  AND bp.Updated < @NextMonthStart4";
            return MRole.GetDefault(ctx).AddAccessSQL(sql, "bp", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
        }

        private string ProductPoBranch(Ctx ctx)
        {
            string sql = @"
                SELECT po.M_Product_ID AS product_id, po.UpdatedBy AS updated_by, po.Updated AS updated_at
                FROM M_Product_PO po
                JOIN M_Product p ON (p.M_Product_ID = po.M_Product_ID AND p.AD_Client_ID = po.AD_Client_ID)
                WHERE po.AD_Client_ID = @AD_Client_ID5
                  AND po.Updated >= @MonthStart5
                  AND po.Updated < @NextMonthStart5";
            return MRole.GetDefault(ctx).AddAccessSQL(sql, "po", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
        }

        private SqlParameter[] BuildDateParams(Ctx ctx, DateBounds bounds)
        {
            return new SqlParameter[]
            {
                new SqlParameter("@AD_Client_ID1", ctx.GetAD_Client_ID()),
                new SqlParameter("@MonthStart1", SqlDbType.DateTime) { Value = bounds.MonthStart },
                new SqlParameter("@NextMonthStart1", SqlDbType.DateTime) { Value = bounds.NextMonthStart },
                new SqlParameter("@AD_Client_ID2", ctx.GetAD_Client_ID()),
                new SqlParameter("@MonthStart2", SqlDbType.DateTime) { Value = bounds.MonthStart },
                new SqlParameter("@NextMonthStart2", SqlDbType.DateTime) { Value = bounds.NextMonthStart },
                new SqlParameter("@AD_Client_ID3", ctx.GetAD_Client_ID()),
                new SqlParameter("@MonthStart3", SqlDbType.DateTime) { Value = bounds.MonthStart },
                new SqlParameter("@NextMonthStart3", SqlDbType.DateTime) { Value = bounds.NextMonthStart },
                new SqlParameter("@AD_Client_ID4", ctx.GetAD_Client_ID()),
                new SqlParameter("@MonthStart4", SqlDbType.DateTime) { Value = bounds.MonthStart },
                new SqlParameter("@NextMonthStart4", SqlDbType.DateTime) { Value = bounds.NextMonthStart },
                new SqlParameter("@AD_Client_ID5", ctx.GetAD_Client_ID()),
                new SqlParameter("@MonthStart5", SqlDbType.DateTime) { Value = bounds.MonthStart },
                new SqlParameter("@NextMonthStart5", SqlDbType.DateTime) { Value = bounds.NextMonthStart },
                new SqlParameter("@TodayStart", SqlDbType.DateTime) { Value = bounds.TodayStart },
                new SqlParameter("@TomorrowStart", SqlDbType.DateTime) { Value = bounds.TomorrowStart }
            };
        }

        /// <summary>
        /// Resolves current-month and today half-open date boundaries from
        /// the server clock in C# (never with a DB-specific date function),
        /// per the widget's portable-SQL requirement.
        /// </summary>
        private DateBounds ResolveDateBounds()
        {
            DateTime now = DateTime.Now;
            DateTime monthStart = new DateTime(now.Year, now.Month, 1);
            DateTime todayStart = now.Date;
            return new DateBounds
            {
                MonthStart = monthStart,
                NextMonthStart = monthStart.AddMonths(1),
                TodayStart = todayStart,
                TomorrowStart = todayStart.AddDays(1)
            };
        }

        /// <summary>
        /// DB-appropriate character literal. On this schema the text columns
        /// are national-character (NVARCHAR2), so a plain literal combined
        /// with them raises ORA-12704 on Oracle; the N'..' prefix fixes it.
        /// PostgreSQL has no N'..' syntax, so it stays a plain quoted literal.
        /// </summary>
        private static string NLiteral(Ctx ctx, string text)
        {
            return DB.IsPostgreSQL() ? "'" + text + "'" : "N'" + text + "'";
        }

        /// <summary>Resolved current-month and today half-open date boundaries.</summary>
        private class DateBounds
        {
            public DateTime MonthStart;
            public DateTime NextMonthStart;
            public DateTime TodayStart;
            public DateTime TomorrowStart;
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
