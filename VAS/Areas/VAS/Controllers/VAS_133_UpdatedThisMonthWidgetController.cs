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
using VASLogic.Models;
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
        /// Number of numbered placeholder sets (AD_Client_ID/MonthStart/
        /// NextMonthStart) the branches consume: the five tracked tables, then the
        /// "no field log" product fallback (6) and its NOT EXISTS probe (7).
        /// </summary>
        private const int BranchSets = 7;

        /// <summary>One change-logged table feeding the widget.</summary>
        private class Tracked
        {
            public string SourceType;
            public string TableName;
            public string Alias;
            public string KeyColumn;
            /// <summary>Joins that bring in the owning product "p" (empty for M_Product itself).</summary>
            public string ProductJoin;
            public string ProductIdExpr;
            public string FallbackTab;
        }

        private static readonly Tracked[] TrackedTables = new Tracked[]
        {
            new Tracked { SourceType = "PRODUCT", TableName = "M_Product", Alias = "p", KeyColumn = "M_Product_ID",
                ProductJoin = "", ProductIdExpr = "p.M_Product_ID", FallbackTab = "Product" },
            new Tracked { SourceType = "PRICE", TableName = "M_ProductPrice", Alias = "pp", KeyColumn = "M_ProductPrice_ID",
                ProductJoin = "JOIN M_Product p ON (p.M_Product_ID = pp.M_Product_ID AND p.AD_Client_ID = pp.AD_Client_ID)",
                ProductIdExpr = "pp.M_Product_ID", FallbackTab = "Price" },
            new Tracked { SourceType = "BOM", TableName = "M_BOM", Alias = "b", KeyColumn = "M_BOM_ID",
                ProductJoin = "JOIN M_Product p ON (p.M_Product_ID = b.M_Product_ID AND p.AD_Client_ID = b.AD_Client_ID)",
                ProductIdExpr = "b.M_Product_ID", FallbackTab = "BOM" },
            new Tracked { SourceType = "BOMPRODUCT", TableName = "M_BOMProduct", Alias = "bp", KeyColumn = "M_BOMProduct_ID",
                ProductJoin = "JOIN M_BOM b ON (b.M_BOM_ID = bp.M_BOM_ID AND b.AD_Client_ID = bp.AD_Client_ID) " +
                              "JOIN M_Product p ON (p.M_Product_ID = b.M_Product_ID AND p.AD_Client_ID = b.AD_Client_ID)",
                ProductIdExpr = "b.M_Product_ID", FallbackTab = "BOM Line" },
            new Tracked { SourceType = "PRODUCTPO", TableName = "M_Product_PO", Alias = "po", KeyColumn = "M_Product_PO_ID",
                ProductJoin = "JOIN M_Product p ON (p.M_Product_ID = po.M_Product_ID AND p.AD_Client_ID = po.AD_Client_ID)",
                ProductIdExpr = "po.M_Product_ID", FallbackTab = "Purchasing" }
        };

        /// <summary>
        /// Returns the current-month update count, distinct updater count,
        /// today's update count, and distinct product count - all from one
        /// query pass so they stay consistent with the modal's totals.
        /// </summary>
        /// <param name="clientTzOffsetMinutes">Browser getTimezoneOffset() value
        /// ((UTC - local) in minutes); when sent, "this month" / "today" are
        /// resolved in the USER'S local time instead of the server clock.</param>
        /// <returns>JSON { updateCount, userCount, todayCount, productCount, monthStart }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetSummary(int? clientTzOffsetMinutes = null)
        {
            if (Session["ctx"] == null)
            {
                return Fail(Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired");
            }

            Ctx ctx = Session["ctx"] as Ctx;

            try
            {
                DateBounds bounds = ResolveDateBounds(clientTzOffsetMinutes);

                string sql = @"
                    WITH updated_records AS (
                        " + BuildUnion(ctx, false) + @"
                    )
                    SELECT
                        COUNT(*) AS UpdateCount,
                        COUNT(DISTINCT updated_by_id) AS UserCount,
                        COALESCE(SUM(CASE WHEN updated_at >= @TodayStart AND updated_at < @TomorrowStart THEN 1 ELSE 0 END), 0) AS TodayCount,
                        COUNT(DISTINCT product_id) AS ProductCount
                    FROM updated_records";

                int updateCount = 0, userCount = 0, todayCount = 0, productCount = 0;

                IDataReader dr = null;
                try
                {
                    dr = DB.ExecuteReader(sql, BuildDateParams(ctx, bounds, true));
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
                    monthStart = bounds.ClientMonthStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                });
            }
            catch (Exception)
            {
                return Fail(Msg.GetMsg(ctx, "Error") ?? "Error");
            }
        }

        /// <summary>
        /// Returns one page (2-8 rows) of this month's field-level changes to the
        /// product and its child tabs, newest first, with the total row count for
        /// pagination.
        /// </summary>
        /// <param name="offset">Zero-based result offset.</param>
        /// <param name="pageSize">Requested page size; clamped to 2-8.</param>
        /// <param name="clientTzOffsetMinutes">Browser getTimezoneOffset() value
        /// ((UTC - local) in minutes); when sent, the month window follows the
        /// USER'S local time and displayed timestamps are shifted to it.</param>
        /// <returns>JSON { total, offset, pageSize, rows[] }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetPagedUpdates(int offset = 0, int pageSize = MaxPageSize, int? clientTzOffsetMinutes = null)
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
                DateBounds bounds = ResolveDateBounds(clientTzOffsetMinutes);

                string sql = @"
                    WITH updated_records AS (
                        " + BuildUnion(ctx, true) + @"
                    ),
                    ranked_updates AS (
                        SELECT
                            ur.source_type, ur.source_record_id, ur.product_id, ur.product_name,
                            ur.field_name, ur.column_name, ur.ref_id, ur.ref_value_id,
                            ur.old_value, ur.new_value, ur.tab_name, ur.updated_by_id,
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
                        field_name, column_name, ref_id, ref_value_id, old_value, new_value, tab_name,
                        updated_by_id, updated_by_name, updated_at, total_count
                    FROM ranked_updates
                    WHERE row_num > @Offset1
                      AND row_num <= (@Offset2 + @PageSize)
                    ORDER BY row_num";

                List<SqlParameter> parms = new List<SqlParameter>(BuildDateParams(ctx, bounds, false));
                parms.Add(new SqlParameter("@Offset1", offset));
                parms.Add(new SqlParameter("@Offset2", offset));
                parms.Add(new SqlParameter("@PageSize", pageSize));

                List<object> rows = new List<object>();
                int total = 0;
                // "x ago" is measured on the DATABASE clock (see GetDbNow), not the web server's or the browser's.
                DateTime? dbNow = GetDbNow();
                VAS_ChangeLogValueModel resolver = new VAS_ChangeLogValueModel();

                IDataReader dr = null;
                try
                {
                    dr = DB.ExecuteReader(sql, parms.ToArray());
                    while (dr != null && dr.Read())
                    {
                        total = Util.GetValueOfInt(dr["total_count"]);
                        DateTime? updatedAt = Util.GetValueOfDateTime(dr["updated_at"]);
                        int? ageMinutes = (updatedAt.HasValue && dbNow.HasValue)
                            ? (int?)Math.Max(0, (int)Math.Floor((dbNow.Value - updatedAt.Value).TotalMinutes))
                            : null;
                        string updatedByName = Util.GetValueOfString(dr["updated_by_name"]);
                        // Shift the stored (server-clock) timestamp onto the user's
                        // local clock before display.
                        updatedAt = updatedAt.HasValue
                            ? updatedAt.Value.AddMinutes(bounds.DisplayShiftMinutes)
                            : updatedAt;

                        // AD_ChangeLog keeps the raw stored value (an id for a
                        // reference, a code for a list); show what the field shows.
                        string columnName = Util.GetValueOfString(dr["column_name"]);
                        int refId = Util.GetValueOfInt(dr["ref_id"]);
                        int refValueId = Util.GetValueOfInt(dr["ref_value_id"]);
                        string newValue = resolver.Display(
                            VAS_ChangeLogValueModel.Normalise(Util.GetValueOfString(dr["new_value"])),
                            columnName, refId, refValueId);
                        string oldValue = resolver.Display(
                            VAS_ChangeLogValueModel.Normalise(Util.GetValueOfString(dr["old_value"])),
                            columnName, refId, refValueId);

                        rows.Add(new
                        {
                            sourceType = Util.GetValueOfString(dr["source_type"]),
                            sourceRecordId = Util.GetValueOfInt(dr["source_record_id"]),
                            productId = Util.GetValueOfInt(dr["product_id"]),
                            productName = Util.GetValueOfString(dr["product_name"]),
                            fieldName = Util.GetValueOfString(dr["field_name"]),
                            newValue = newValue,
                            oldValue = oldValue,
                            category = Util.GetValueOfString(dr["tab_name"]),
                            updatedById = Util.GetValueOfInt(dr["updated_by_id"]),
                            updatedByName = updatedByName,
                            updatedAt = updatedAt.HasValue ? updatedAt.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : "",
                            updatedAgeMinutes = ageMinutes
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
        /// UNION ALL of every tracked table's change-log branch plus the product
        /// fallback. Both queries (summary and list) share it so their counts agree.
        /// Branch n consumes placeholder set n (see BuildDateParams), in textual
        /// order, because DB.ExecuteReader binds positionally.
        /// </summary>
        private string BuildUnion(Ctx ctx, bool full)
        {
            List<string> branches = new List<string>();
            for (int i = 0; i < TrackedTables.Length; i++)
            {
                branches.Add(ChangeBranch(ctx, TrackedTables[i], i + 1, full));
            }
            branches.Add(FallbackBranch(ctx, TrackedTables.Length + 1, full));
            return string.Join(" UNION ALL ", branches);
        }

        /// <summary>
        /// One row per logged field change on the tracked table (AD_ChangeLog),
        /// carrying the field, its new value, the tab the field sits on, who
        /// changed it and when. The tracked table is the FIRST table in the FROM so
        /// MRole is applied to it, as before; a save that wrote the same value back
        /// is not a change and is dropped.
        /// </summary>
        private string ChangeBranch(Ctx ctx, Tracked t, int n, bool full)
        {
            string nul = NLiteral(ctx, "~");
            string select = full
                ? @"SELECT
                        " + NLiteral(ctx, t.SourceType) + @" AS source_type,
                        cl.AD_ChangeLog_ID AS source_record_id,
                        " + t.ProductIdExpr + @" AS product_id,
                        p.Name AS product_name,
                        COALESCE(col.Name, col.ColumnName) AS field_name,
                        col.ColumnName AS column_name,
                        col.AD_Reference_ID AS ref_id,
                        col.AD_Reference_Value_ID AS ref_value_id,
                        cl.OldValue AS old_value,
                        cl.NewValue AS new_value,
                        COALESCE((SELECT MIN(tb.Name)
                                    FROM AD_Field f
                                    JOIN AD_Tab tb ON (tb.AD_Tab_ID = f.AD_Tab_ID)
                                   WHERE f.AD_Column_ID = cl.AD_Column_ID
                                     AND tb.AD_Table_ID = cl.AD_Table_ID
                                     AND f.IsActive = 'Y'
                                     AND tb.IsActive = 'Y'), " + NLiteral(ctx, t.FallbackTab) + @") AS tab_name,
                        cl.CreatedBy AS updated_by_id,
                        cl.Created AS updated_at"
                : @"SELECT " + t.ProductIdExpr + @" AS product_id, cl.CreatedBy AS updated_by_id, cl.Created AS updated_at";

            string sql = select + @"
                    FROM " + t.TableName + " " + t.Alias + @"
                    " + t.ProductJoin + @"
                    JOIN AD_ChangeLog cl ON (cl.Record_ID = " + t.Alias + "." + t.KeyColumn + @")
                    JOIN AD_Table tbl ON (tbl.AD_Table_ID = cl.AD_Table_ID AND tbl.TableName = '" + t.TableName + @"')
                    " + (full ? "LEFT JOIN AD_Column col ON (col.AD_Column_ID = cl.AD_Column_ID)" : "") + @"
                    WHERE " + t.Alias + @".AD_Client_ID = @AD_Client_ID" + n + @"
                      AND cl.Created >= @MonthStart" + n + @"
                      AND cl.Created < @NextMonthStart" + n + @"
                      AND COALESCE(cl.IsActive, 'Y') = 'Y'
                      AND COALESCE(cl.OldValue, " + nul + ") <> COALESCE(cl.NewValue, " + nul + ")";
            return MRole.GetDefault(ctx).AddAccessSQL(sql, t.Alias, MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
        }

        /// <summary>
        /// Products saved this month that left NO field-level log (a column whose
        /// change log is switched off). They stay in the feed as a record-level
        /// entry rather than vanishing; the NOT EXISTS keeps them from doubling the
        /// logged ones. Uses placeholder sets n (product) and n+1 (probe).
        /// </summary>
        private string FallbackBranch(Ctx ctx, int n, bool full)
        {
            string select = full
                ? @"SELECT
                        " + NLiteral(ctx, "PRODUCT") + @" AS source_type,
                        p.M_Product_ID AS source_record_id,
                        p.M_Product_ID AS product_id,
                        p.Name AS product_name,
                        " + NLiteral(ctx, "Product details updated") + @" AS field_name,
                        NULL AS column_name,
                        0 AS ref_id,
                        0 AS ref_value_id,
                        NULL AS old_value,
                        NULL AS new_value,
                        " + NLiteral(ctx, "Product") + @" AS tab_name,
                        p.UpdatedBy AS updated_by_id,
                        p.Updated AS updated_at"
                : @"SELECT p.M_Product_ID AS product_id, p.UpdatedBy AS updated_by_id, p.Updated AS updated_at";

            string sql = select + @"
                    FROM M_Product p
                    WHERE p.AD_Client_ID = @AD_Client_ID" + n + @"
                      AND p.Updated >= @MonthStart" + n + @"
                      AND p.Updated < @NextMonthStart" + n;
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "p", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            // Appended after the role filter so that filter lands on the outer WHERE.
            return sql + @"
                      AND NOT EXISTS (SELECT 1
                                        FROM AD_ChangeLog c2
                                        JOIN AD_Table t2 ON (t2.AD_Table_ID = c2.AD_Table_ID AND t2.TableName = 'M_Product')
                                       WHERE c2.Record_ID = p.M_Product_ID
                                         AND c2.Created >= @MonthStart" + (n + 1) + @"
                                         AND c2.Created < @NextMonthStart" + (n + 1) + @")";
        }

        /// <summary>
        /// Positional parameter set: DB.ExecuteReader matches this array to the
        /// left-to-right order the @placeholders appear in the final SQL, so every
        /// numbered set below is supplied in the order BuildUnion emits it. The
        /// fallback's NOT EXISTS probe (set 7) carries no client id.
        /// </summary>
        private SqlParameter[] BuildDateParams(Ctx ctx, DateBounds bounds, bool withToday)
        {
            List<SqlParameter> list = new List<SqlParameter>();
            for (int n = 1; n <= BranchSets; n++)
            {
                if (n != BranchSets)
                {
                    list.Add(new SqlParameter("@AD_Client_ID" + n, ctx.GetAD_Client_ID()));
                }
                list.Add(new SqlParameter("@MonthStart" + n, SqlDbType.DateTime) { Value = bounds.MonthStart });
                list.Add(new SqlParameter("@NextMonthStart" + n, SqlDbType.DateTime) { Value = bounds.NextMonthStart });
            }
            if (withToday)
            {
                list.Add(new SqlParameter("@TodayStart", SqlDbType.DateTime) { Value = bounds.TodayStart });
                list.Add(new SqlParameter("@TomorrowStart", SqlDbType.DateTime) { Value = bounds.TomorrowStart });
            }
            return list.ToArray();
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

        /// <summary>
        /// Resolves current-month and today half-open date boundaries. When the
        /// browser offset is supplied, "this month" / "today" are what THEY mean
        /// on the user's local clock - not on the server clock, which can sit in
        /// a different timezone (or even a different month near month end).
        ///
        /// Sign conventions: the browser sends getTimezoneOffset() = (UTC - local)
        /// in minutes (IST = -330); the server's own offset is (local - UTC)
        /// (IST server = +330). Their sum is how many minutes the server wall
        /// clock runs AHEAD of the user's wall clock.
        ///
        /// The DB stores Updated on the SERVER clock, so the user-local
        /// boundaries are shifted back onto the server clock for the SQL
        /// comparison, and DisplayShiftMinutes moves stored timestamps onto the
        /// user's clock for display. Still no DB-specific date function - the
        /// boundaries remain bind parameters.
        /// </summary>
        private DateBounds ResolveDateBounds(int? clientTzOffsetMinutes)
        {
            int serverOffset = (int)Math.Round((DateTime.Now - DateTime.UtcNow).TotalMinutes);
            int delta = clientTzOffsetMinutes.HasValue ? serverOffset + clientTzOffsetMinutes.Value : 0;

            // "now" on the user's wall clock
            DateTime clientNow = DateTime.Now.AddMinutes(-delta);
            DateTime clientMonthStart = new DateTime(clientNow.Year, clientNow.Month, 1);

            return new DateBounds
            {
                // user-local boundaries, expressed back on the server clock for SQL
                MonthStart = clientMonthStart.AddMinutes(delta),
                NextMonthStart = clientMonthStart.AddMonths(1).AddMinutes(delta),
                TodayStart = clientNow.Date.AddMinutes(delta),
                TomorrowStart = clientNow.Date.AddDays(1).AddMinutes(delta),
                // server clock -> user clock shift for displayed timestamps
                DisplayShiftMinutes = -delta,
                ClientMonthStart = clientMonthStart
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

        /// <summary>Resolved current-month and today half-open date boundaries
        /// (MonthStart..TomorrowStart are on the server clock for SQL comparison;
        /// ClientMonthStart is the user-local month start for display).</summary>
        private class DateBounds
        {
            public DateTime MonthStart;
            public DateTime NextMonthStart;
            public DateTime TodayStart;
            public DateTime TomorrowStart;
            public int DisplayShiftMinutes;
            public DateTime ClientMonthStart;
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
