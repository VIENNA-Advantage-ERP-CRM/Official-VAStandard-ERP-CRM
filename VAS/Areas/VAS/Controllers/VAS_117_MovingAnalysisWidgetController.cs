using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Web.Mvc;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VAS.Controllers
{
    /// <summary>
    /// Module Name : VAS_117_MovingAnalysisWidget
    /// Purpose     : Supplies stock-velocity rankings (fast movers / slow movers)
    ///               for the Product section "Moving Analysis" dashboard widget.
    ///               Velocity is measured from issue/sale movements on
    ///               M_Transaction (customer shipments 'C-' and inventory issues
    ///               'I-'); slow movers are stocked items with no such movement in
    ///               the last 30 days. Inactive and discontinued products are
    ///               excluded. Widget number 117 - reassign on hand-off.
    /// Chronological development:
    ///   117         2026-07-15 Created
    /// </summary>
    public class VAS_117_MovingAnalysisWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_117_MovingAnalysisWidgetController).FullName);

        // Movement window in days: 30d drives the fast ranking and the
        // "issued in the last 30 days" slow-mover test; 90d is the longer
        // window the slow view uses to expose near-zero movement.
        private const int FAST_WINDOW_DAYS = 30;
        private const int SLOW_WINDOW_DAYS = 90;
        private const int MAX_ROWS_PER_LIST = 5;

        /// <summary>
        /// Returns the fast-mover and slow-mover velocity lists for the widget.
        /// </summary>
        /// <returns>Serialized moving-analysis data.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetMovingAnalysis()
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null)
            {
                return Json(JsonConvert.SerializeObject(new { error = "Session Expired" }), JsonRequestBehavior.AllowGet);
            }

            try
            {
                MovingAnalysisResult result = GetMovingAnalysisData(ctx);
                return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return ErrorResult(ctx, ex);
            }
        }

        /// <summary>
        /// Aggregates issue movements and on-hand stock per product, then splits
        /// the products into a fast list (highest 30-day issue velocity, desc) and
        /// a slow list (in stock, but nothing issued in the last 30 days - ordered
        /// by 90-day issues asc, then largest idle stock first).
        /// </summary>
        /// <param name="ctx">Current application context.</param>
        /// <returns>Fast and slow velocity lists.</returns>
        private MovingAnalysisResult GetMovingAnalysisData(Ctx ctx)
        {
            MovingAnalysisResult result = new MovingAnalysisResult
            {
                fast = new List<MovingProduct>(),
                slow = new List<MovingProduct>()
            };
            if (ctx == null) { return result; }

            // Window cut-offs are resolved in C# and passed as parameters so the
            // query carries no database-specific date arithmetic (Oracle + PG).
            DateTime today = DateTime.Now.Date;
            DateTime cutoffFast = today.AddDays(-FAST_WINDOW_DAYS);
            DateTime cutoffSlow = today.AddDays(-SLOW_WINDOW_DAYS);

            // Issue/sale outflow per product. MovementQty is signed (negative =
            // stock leaving), so -MovementQty is the issued quantity; only
            // customer shipments ('C-', sold) and inventory issues ('I-', issued)
            // are counted, matching the widget's "issued or sold" definition.
            // MRole.AddAccessSQL appends its access predicate to the END of the
            // string, so each block below is a SELECT ... WHERE only; the GROUP BY
            // is appended AFTER the access predicate (matching VAS_094's stock
            // query). Putting GROUP BY inside the literal breaks the SQL with
            // ORA-00907 because the predicate then lands after the GROUP BY.
            string issuesSql = @"
                SELECT Trans.M_Product_ID,
                       SUM(CASE WHEN Trans.MovementDate>=@Issue_Cutoff_Fast AND Trans.MovementQty<0 AND Trans.MovementType IN (N'C-',N'I-') THEN -Trans.MovementQty ELSE 0 END) AS Issued_Fast,
                       SUM(CASE WHEN Trans.MovementQty<0 AND Trans.MovementType IN (N'C-',N'I-') THEN -Trans.MovementQty ELSE 0 END) AS Issued_Slow
                FROM M_Transaction Trans
                WHERE Trans.IsActive=N'Y'
                  AND Trans.AD_Client_ID=@Issue_Client_ID
                  AND Trans.AD_Org_ID IN (0,COALESCE(NULLIF(@Issue_Org_ID,0),Trans.AD_Org_ID))
                  AND Trans.MovementDate>=@Issue_Cutoff_Slow";

            // Current on-hand stock per product across all locators/warehouses.
            string stockSql = @"
                SELECT Storage.M_Product_ID,
                       SUM(COALESCE(Storage.QtyOnHand,0)) AS On_Hand
                FROM M_Storage Storage
                WHERE Storage.IsActive=N'Y'
                  AND Storage.AD_Client_ID=@Stock_Client_ID
                  AND Storage.AD_Org_ID IN (0,COALESCE(NULLIF(@Stock_Org_ID,0),Storage.AD_Org_ID))";

            // Item master - active, non-discontinued products only.
            string productSql = @"
                SELECT Product.M_Product_ID,
                       Product.Value AS Product_Code,
                       Product.Name AS Product_Name,
                       Product.C_UOM_ID
                FROM M_Product Product
                WHERE Product.IsActive=N'Y'
                  AND Product.AD_Client_ID=@Product_Client_ID
                  AND Product.AD_Org_ID IN (0,COALESCE(NULLIF(@Product_Org_ID,0),Product.AD_Org_ID))
                  AND (Product.Discontinued IS NULL OR Product.Discontinued=N'N')";

            issuesSql = AddAccessSql(ctx, issuesSql, "Trans") + " GROUP BY Trans.M_Product_ID";
            stockSql = AddAccessSql(ctx, stockSql, "Storage") + " GROUP BY Storage.M_Product_ID";
            productSql = AddAccessSql(ctx, productSql, "Product");

            string sql = string.Format(@"
                WITH Issues AS (
                    {0}
                ),
                Stock AS (
                    {1}
                ),
                Products AS (
                    {2}
                )
                SELECT Products.M_Product_ID,
                       Products.Product_Code,
                       Products.Product_Name,
                       UnitOfMeasure.Name AS UOM_Name,
                       COALESCE(Issues.Issued_Fast,0) AS Issued_Fast,
                       COALESCE(Issues.Issued_Slow,0) AS Issued_Slow,
                       COALESCE(Stock.On_Hand,0) AS On_Hand
                FROM Products
                LEFT OUTER JOIN Issues ON (Issues.M_Product_ID=Products.M_Product_ID)
                LEFT OUTER JOIN Stock ON (Stock.M_Product_ID=Products.M_Product_ID)
                LEFT OUTER JOIN C_UOM UnitOfMeasure ON (UnitOfMeasure.C_UOM_ID=Products.C_UOM_ID AND UnitOfMeasure.IsActive=N'Y')
                WHERE COALESCE(Issues.Issued_Fast,0)>0 OR COALESCE(Stock.On_Hand,0)>0",
                issuesSql,
                stockSql,
                productSql
            );

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@Issue_Cutoff_Fast", SqlDbType.DateTime) { Value = cutoffFast },
                new SqlParameter("@Issue_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@Issue_Org_ID", ctx.GetAD_Org_ID()),
                new SqlParameter("@Issue_Cutoff_Slow", SqlDbType.DateTime) { Value = cutoffSlow },
                new SqlParameter("@Stock_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@Stock_Org_ID", ctx.GetAD_Org_ID()),
                new SqlParameter("@Product_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@Product_Org_ID", ctx.GetAD_Org_ID())
            };

            List<MovingProduct> all = new List<MovingProduct>();
            IDataReader reader = null;
            try
            {
                reader = DB.ExecuteReader(sql, parameters);
                while (reader != null && reader.Read())
                {
                    decimal issuedFast = Util.GetValueOfDecimal(reader["Issued_Fast"]);
                    decimal issuedSlow = Util.GetValueOfDecimal(reader["Issued_Slow"]);
                    decimal onHand = Util.GetValueOfDecimal(reader["On_Hand"]);

                    all.Add(new MovingProduct
                    {
                        product_id = Util.GetValueOfInt(reader["M_Product_ID"]),
                        sku = Util.GetValueOfString(reader["Product_Code"]),
                        product_name = Util.GetValueOfString(reader["Product_Name"]),
                        uom_name = Util.GetValueOfString(reader["UOM_Name"]),
                        on_hand = onHand,
                        issued_fast = issuedFast,
                        issued_slow = issuedSlow,
                        per_day = issuedFast / FAST_WINDOW_DAYS,
                        turns_fast = AnnualTurns(issuedFast, FAST_WINDOW_DAYS, onHand),
                        turns_slow = AnnualTurns(issuedSlow, SLOW_WINDOW_DAYS, onHand)
                    });
                }
            }
            finally
            {
                CloseReader(reader);
            }

            // Fast: anything actually issued in the last 30 days, highest first.
            result.fast = all
                .Where(row => row.issued_fast > 0)
                .OrderByDescending(row => row.issued_fast)
                .ThenByDescending(row => row.on_hand)
                .Take(MAX_ROWS_PER_LIST)
                .ToList();

            // Slow: in stock but nothing sold/issued in the last 30 days - the
            // idle "laying in the warehouse" stock. Lowest 90-day movement first,
            // then the largest idle piles, so dead stock surfaces at the top.
            result.slow = all
                .Where(row => row.issued_fast == 0 && row.on_hand > 0)
                .OrderBy(row => row.issued_slow)
                .ThenByDescending(row => row.on_hand)
                .Take(MAX_ROWS_PER_LIST)
                .ToList();

            result.fast_window_days = FAST_WINDOW_DAYS;
            result.slow_window_days = SLOW_WINDOW_DAYS;
            return result;
        }

        /// <summary>
        /// Annualised turnover ratio: issued quantity scaled to a full year over
        /// the measured window, divided by current on-hand. Zero when nothing is
        /// on hand (no meaningful ratio).
        /// </summary>
        /// <param name="issued">Issued quantity in the window.</param>
        /// <param name="windowDays">Length of the measurement window in days.</param>
        /// <param name="onHand">Current on-hand quantity.</param>
        /// <returns>Annualised turnover ratio.</returns>
        private decimal AnnualTurns(decimal issued, int windowDays, decimal onHand)
        {
            if (onHand <= 0 || windowDays <= 0) { return 0m; }
            return issued * (365m / windowDays) / onHand;
        }

        /// <summary>
        /// Adds read-only role access to a query whose named alias is a physical table.
        /// </summary>
        /// <param name="ctx">Current application context.</param>
        /// <param name="sql">Physical-table query.</param>
        /// <param name="tableAlias">Main physical-table alias.</param>
        /// <returns>Role-secured SQL.</returns>
        private string AddAccessSql(Ctx ctx, string sql, string tableAlias)
        {
            return MRole.GetDefault(ctx).AddAccessSQL(
                sql,
                tableAlias,
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );
        }

        /// <summary>
        /// Closes and disposes a database reader.
        /// </summary>
        /// <param name="reader">Reader to close.</param>
        private void CloseReader(IDataReader reader)
        {
            if (reader == null) { return; }
            reader.Close();
            reader.Dispose();
        }

        /// <summary>
        /// Logs a controller error and returns a localized error payload.
        /// </summary>
        /// <param name="ctx">Current application context.</param>
        /// <param name="ex">Unhandled controller exception.</param>
        /// <returns>Localized JSON error.</returns>
        private JsonResult ErrorResult(Ctx ctx, Exception ex)
        {
            Log.Log(Level.SEVERE, "VAS_117_MovingAnalysisWidget.GetMovingAnalysis", ex);
            // The concrete reason (e.g. an ORA-/PG error) is shown in the widget so
            // a data-side fault is diagnosable without reading the server log.
            string message = (Msg.GetMsg(ctx, "Error") ?? "Error") + ": " + ex.Message;
            string json = JsonConvert.SerializeObject(new { error = message });
            return Json(json, JsonRequestBehavior.AllowGet);
        }

        /// <summary>Moving-analysis response: fast and slow velocity lists.</summary>
        private class MovingAnalysisResult
        {
            public int fast_window_days { get; set; }
            public int slow_window_days { get; set; }
            public List<MovingProduct> fast { get; set; }
            public List<MovingProduct> slow { get; set; }
        }

        /// <summary>One product's velocity metrics for the moving-analysis lists.</summary>
        private class MovingProduct
        {
            public int product_id { get; set; }
            public string sku { get; set; }
            public string product_name { get; set; }
            public string uom_name { get; set; }
            public decimal on_hand { get; set; }
            public decimal issued_fast { get; set; }
            public decimal issued_slow { get; set; }
            public decimal per_day { get; set; }
            public decimal turns_fast { get; set; }
            public decimal turns_slow { get; set; }
        }
    }
}
