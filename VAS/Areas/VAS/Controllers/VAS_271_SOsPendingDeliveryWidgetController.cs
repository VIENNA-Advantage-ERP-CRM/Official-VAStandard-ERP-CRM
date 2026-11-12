/******************************************************
 * Module Name    : VAS
 * Purpose        : Sales Order dashboard "SOs Pending Delivery" KPI widget endpoints
 * chronological  : Development
 * Created Date   : 2026-09-14
 * Created by     : VAI052
 ******************************************************/

using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Web.Mvc;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VAS.Controllers
{
    /// <summary>
    /// Module Name : VAS_271_SOsPendingDeliveryWidget
    /// Purpose     : Data endpoints for the 3x1 "SOs Pending Delivery" KPI tile on the
    ///               Sales Order dashboard. (1) the tile/summary figures - open count,
    ///               pending items, undelivered value and past-due count, all-time (no
    ///               month scoping), (2) the drill-down modal's paginated documents list
    ///               with a per-order stock-shortfall chip, and (3) the shared Sales
    ///               Order record-preview data (header stats + paginated lines with
    ///               per-line free stock) reused for both the "record" and "lines" child
    ///               modals.
    ///
    ///   Business definition per 04_SOs_Pending_Delivery_Claude_Development_Prompt.txt:
    ///     - A Sales Order is pending delivery when DocStatus = 'CO' (Drafted, In
    ///       Process, Closed, Voided, Reversed and quotations are all excluded),
    ///       IsSOTrx = 'Y', not a return, not a quotation, DateOrdered &lt;= today, and at
    ///       least one active line has QtyOrdered &gt; QtyDelivered. This is an all-time
    ///       operational backlog, not scoped to the current month.
    ///     - Pending quantity per line = QtyOrdered - QtyDelivered when positive, else 0;
    ///       pending value = pending quantity * PriceActual (never a proportional share of
    ///       the order total).
    ///     - Past due = pending order where C_Order.DatePromised &lt; today (strictly
    ///       less than - the promised, not requested, date is the commitment).
    ///     - Stock/shortfall chip is computed per order independently (not an
    ///       allocation-aware simulation across orders): for every product/warehouse the
    ///       order has a pending quantity for, compare against that warehouse's free
    ///       stock (SUM(QtyOnHand - QtyReserved - QtyDedicated - QtyAllocated) over
    ///       active M_Storage rows joined to active M_Locator rows in that warehouse; no
    ///       stock row = 0; no cost tables, no reorder points). All-coverable -&gt;
    ///       "Coverable"; otherwise -&gt; "Short &lt;summed shortfall&gt;".
    ///
    ///   Per the CTE/MRole rule (Prompt_Instructions.txt "Case 1"), AddAccessSQL is
    ///   applied ONLY to the isolated "base_orders" CTE body (BuildBaseOrdersSql) - a
    ///   single SELECT against the one main physical table (C_Order "o") with its own
    ///   single WHERE clause - before it is embedded into the combined multi-CTE
    ///   statement. It is never applied to that combined statement itself, nor to the
    ///   free_stock/product_pending/order_stats/pending_by_order CTE aliases, which are
    ///   secondary aggregates over C_OrderLine/M_Locator/M_Storage joined only to enrich
    ///   each row (mirroring the C_InvoiceA/C_InvoiceB example - primary table only, and
    ///   the exact fix already applied to VAS_270 after that widget hit ORA-00904 from
    ///   applying AddAccessSQL to a multi-FROM-clause statement).
    ///   Read-only here; no writes.
    /// Chronological development:
    ///   VAI052      2026-09-14 Created
    ///   Claude      2026-11-05 Row 26: line pop-up In stock in the product's base UOM (+ its name).
    /// </summary>
    [System.Web.Mvc.SessionState(System.Web.SessionState.SessionStateBehavior.ReadOnly)]
    public class VAS_271_SOsPendingDeliveryWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_271_SOsPendingDeliveryWidgetController).FullName);

        private const int DefaultPageSize = 10;
        private const int MaxPageSize = 10;
        private const int MaxLineRows = 200;

        /// <summary>
        /// All-time summary figures for the tile and the modal's stat strip.
        /// </summary>
        /// <returns>JSON SummaryResult or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetSummary()
        {
            if (Session["ctx"] == null) { return Json("", JsonRequestBehavior.AllowGet); }

            Ctx ctx = Session["ctx"] as Ctx;

            try
            {
                SummaryResult data = GetSummaryData(ctx);
                FillBaseCurrency(ctx, data);
                string json = JsonConvert.SerializeObject(data);
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_271_SOsPendingDeliveryWidget.GetSummary", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// One page of the underlying pending-delivery sales orders for the modal's
        /// table, sorted earliest promised date first. Falls back to a stock-free
        /// variant (ShortfallQty null, StockUnavailable true) if the free-stock join
        /// itself fails, so one bad column never costs the user the whole backlog list.
        /// </summary>
        /// <param name="page">Zero-based page index.</param>
        /// <param name="size">Rows per page (capped at <see cref="MaxPageSize"/>).</param>
        /// <returns>JSON { Total, StockUnavailable, Rows:[...] } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetOrders(int page = 0, int size = DefaultPageSize)
        {
            if (Session["ctx"] == null) { return Json("", JsonRequestBehavior.AllowGet); }

            Ctx ctx = Session["ctx"] as Ctx;
            if (page < 0) { page = 0; }
            if (size <= 0 || size > MaxPageSize) { size = DefaultPageSize; }

            try
            {
                string json = JsonConvert.SerializeObject(GetOrdersData(ctx, page, size));
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_271_SOsPendingDeliveryWidget.GetOrders", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// Full data for the shared Sales Order record-preview modal (record view and
        /// lines view both render from this one payload): header stats and every active
        /// line (capped at <see cref="MaxLineRows"/> - the client paginates client-side,
        /// sized to the space actually available, so the modal body never scrolls).
        /// </summary>
        /// <param name="id">C_Order_ID.</param>
        /// <returns>JSON { Order:{...}, Lines:[...] } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetSalesOrderDetail(int id)
        {
            if (Session["ctx"] == null) { return Json("", JsonRequestBehavior.AllowGet); }

            Ctx ctx = Session["ctx"] as Ctx;

            try
            {
                string json = JsonConvert.SerializeObject(GetSalesOrderDetailData(ctx, id));
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_271_SOsPendingDeliveryWidget.GetSalesOrderDetail", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// The base_orders CTE body - the ONLY fragment AddAccessSQL is ever applied to
        /// (Prompt_Instructions.txt "Case 1"): a single SELECT against the one main
        /// physical table (C_Order "o") with its own single WHERE clause, scoped to the
        /// all-time pending-delivery cohort definition (DocStatus 'CO', DateOrdered up to
        /// today - the QtyOrdered &gt; QtyDelivered test happens one level up, against
        /// C_OrderLine, once this fragment is embedded). GetSummaryData/GetOrdersData
        /// each embed this ALREADY-ACCESS-FILTERED text as their own "base_orders" CTE
        /// and must never call AddAccessSQL again on the combined multi-CTE statement
        /// built around it.
        /// </summary>
        private static string BuildBaseOrdersSql()
        {
            // VAS163 2026-11-11: Row 20 Tax_Included also honours the order's own IsTaxIncluded flag (VAS_270);
            // Rows 26/28 currency, accounting date, conversion type, client and org carried for the
            // document-currency column and the base-currency conversion of the summary card.
            return @"
                SELECT o.C_Order_ID AS C_Order_ID,
                       o.DocumentNo AS Document_No,
                       o.DateOrdered AS Date_Ordered,
                       o.DatePromised AS Date_Promised,
                       o.C_BPartner_ID AS Bpartner_Id,
                       o.M_Warehouse_ID AS Warehouse_Id,
                       " + TaxIncludedSql + @" AS Tax_Included,
                       o.C_Currency_ID AS Currency_Id,
                       COALESCE(o.DateAcct, o.DateOrdered) AS Date_Acct,
                       o.C_ConversionType_ID AS Conversion_Type_Id,
                       o.AD_Client_ID AS Client_Id,
                       o.AD_Org_ID AS Org_Id
                  FROM C_Order o
                 WHERE o.AD_Client_ID = @AD_Client_ID
                   AND o.IsActive = 'Y'
                   AND o.IsSOTrx = 'Y'
                   AND COALESCE(o.IsReturnTrx, 'N') = 'N'
                   AND EXISTS (SELECT 1 FROM C_DocType dt WHERE dt.C_DocType_ID = o.C_DocTypeTarget_ID AND dt.DocSubTypeSO IN ('SO', 'WR', 'BO'))
                   AND o.DocStatus = 'CO'
                   AND o.DateOrdered <= @Today";
        }

        /// <summary>
        /// SO value WITHOUT taxes: on a tax-inclusive price list C_Order.TotalLines
        /// holds the gross, so the lines' tax is taken off (same expression as
        /// VAS_203 / VAS_268). Scalar sub-selects in the SELECT list leave the main
        /// WHERE untouched for AddAccessSQL.
        /// </summary>
        /// <summary>
        /// VAS163 2026-11-11: Row 20 'Y' when the order's prices include tax - the order's own
        /// IsTaxIncluded flag or its price list's flag (same expression as VAS_270).
        /// </summary>
        private const string TaxIncludedSql =
            "(CASE WHEN COALESCE(o.IsTaxIncluded, 'N') = 'Y' " +
            "OR COALESCE((SELECT pl.IsTaxIncluded FROM M_PriceList pl WHERE pl.M_PriceList_ID = o.M_PriceList_ID), 'N') = 'Y' " +
            "THEN 'Y' ELSE 'N' END)";

        // VAS163 2026-11-11: Row 20 the tax is taken from the order's tax summary (C_OrderTax.TaxAmt)
        // instead of C_OrderLine.TaxAmt, which is not maintained on every line (VAS_270 5-Nov Row 45).
        private const string OrderValueExclTaxSql =
            "CASE WHEN " + TaxIncludedSql + " = 'Y' " +
            "THEN COALESCE(o.TotalLines, 0) - (SELECT COALESCE(SUM(COALESCE(ot.TaxAmt, 0)), 0) FROM C_OrderTax ot WHERE ot.C_Order_ID = o.C_Order_ID AND ot.IsActive = 'Y') " +
            "ELSE COALESCE(o.TotalLines, 0) END";

        /* Per-line fragments shared by the summary and the list (alias "ol" = C_OrderLine,
           "bo" = base_orders). Sr 8.15 / 8.21: only Item-type product lines take part
           (ItemOnlyJoinSql) - charges, services and other product types never make an
           order "pending delivery" nor add to its pending value. */
        private const string ItemOnlyJoinSql =
            "INNER JOIN M_Product p ON ( p.M_Product_ID = ol.M_Product_ID AND p.ProductType = 'I' )";

        // Undelivered quantity in the product's base UOM (compared with storage).
        private const string PendingBaseQtySql =
            "CASE WHEN COALESCE(ol.QtyOrdered, 0) > COALESCE(ol.QtyDelivered, 0) " +
            "THEN COALESCE(ol.QtyOrdered, 0) - COALESCE(ol.QtyDelivered, 0) ELSE 0 END";

        // Sr 8.13: undelivered quantity in the line's selected UOM (scaled by QtyEntered / QtyOrdered).
        // ROUND: on PostgreSQL a numeric division carries a scale too large for System.Decimal
        // ("Numeric value does not fit in a System.Decimal"), so the quotient is rounded in SQL.
        private const string PendingEnteredQtySql =
            "CASE WHEN COALESCE(ol.QtyOrdered, 0) > COALESCE(ol.QtyDelivered, 0) AND COALESCE(ol.QtyOrdered, 0) <> 0 " +
            "THEN ROUND((COALESCE(ol.QtyOrdered, 0) - COALESCE(ol.QtyDelivered, 0)) * COALESCE(ol.QtyEntered, ol.QtyOrdered) / ol.QtyOrdered, 6) ELSE 0 END";

        // Sr 8.21: value of the undelivered quantity WITHOUT tax - the undelivered share
        // of the line's net amount (so discounts are respected), with the line tax taken
        // off on a tax-inclusive price list. Previously (QtyOrdered - QtyDelivered) *
        // PriceActual, which carried the tax on tax-inclusive lists and counted charges.
        // VAS163 2026-11-11: Row 20 on a tax-inclusive order the tax is extracted with the line's
        // tax rate (gross * 100 / (100 + rate), alias "tx" = C_Tax via TaxJoinSql) - the same
        // approach as VAS_270's delivered value; C_OrderLine.TaxAmt is often 0, so the gross stayed in.
        private const string PendingValueSql =
            "CASE WHEN COALESCE(ol.QtyOrdered, 0) > COALESCE(ol.QtyDelivered, 0) AND COALESCE(ol.QtyOrdered, 0) <> 0 " +
            "THEN ROUND((COALESCE(ol.QtyOrdered, 0) - COALESCE(ol.QtyDelivered, 0)) * COALESCE(ol.LineNetAmt, 0) / ol.QtyOrdered " +
            "* (CASE WHEN bo.Tax_Included = 'Y' THEN 100 / (100 + COALESCE(tx.Rate, 0)) ELSE 1 END), 6) ELSE 0 END";

        // VAS163 2026-11-11: Row 20 line tax rate for PendingValueSql.
        private const string TaxJoinSql =
            "LEFT OUTER JOIN C_Tax tx ON ( tx.C_Tax_ID = ol.C_Tax_ID )";

        private SummaryResult GetSummaryData(Ctx ctx)
        {
            SummaryResult result = new SummaryResult();
            if (ctx == null) { return result; }

            DateTime today = DateTime.Today;

            // The only AddAccessSQL call for this statement - applied to the isolated
            // single-WHERE base_orders body BEFORE it is embedded below.
            string baseOrdersSql = MRole.GetDefault(ctx).AddAccessSQL(BuildBaseOrdersSql(), "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            // VAS163 2026-11-11: Row 28 the Undelivered value card is always in the base
            // (accounting-schema) currency: each order's pending value in another currency is
            // converted with the configured rates (currencyConvert, same argument order as
            // VAS_020) before summing. The id is an int read from the DB, inlined like VAS_020.
            int baseCurrencyId = GetBaseCurrencyId(ctx);
            string undeliveredValueSql = baseCurrencyId > 0
                ? "ROUND(SUM(CASE WHEN Currency_Id = " + baseCurrencyId + " THEN Pending_Value " +
                  "ELSE COALESCE(currencyConvert(Pending_Value, Currency_Id, " + baseCurrencyId + ", Date_Acct, Conversion_Type_Id, Client_Id, Org_Id), 0) END), 6)"
                : "SUM(Pending_Value)";

            string sql = @"
                WITH base_orders AS (" + baseOrdersSql + @"),
                pending_by_order AS (
                    SELECT
                        bo.C_Order_ID AS C_Order_ID,
                        bo.Date_Promised AS Date_Promised,
                        bo.Currency_Id AS Currency_Id,
                        bo.Date_Acct AS Date_Acct,
                        bo.Conversion_Type_Id AS Conversion_Type_Id,
                        bo.Client_Id AS Client_Id,
                        bo.Org_Id AS Org_Id,
                        SUM(" + PendingBaseQtySql + @") AS Pending_Qty,
                        SUM(" + PendingEnteredQtySql + @") AS Pending_Qty_Entered,
                        SUM(" + PendingValueSql + @") AS Pending_Value
                      FROM base_orders bo
                      INNER JOIN C_OrderLine ol ON ( ol.C_Order_ID = bo.C_Order_ID AND ol.IsActive = 'Y' )
                      " + ItemOnlyJoinSql + @"
                      " + TaxJoinSql + @"
                     GROUP BY bo.C_Order_ID, bo.Date_Promised, bo.Currency_Id, bo.Date_Acct, bo.Conversion_Type_Id, bo.Client_Id, bo.Org_Id
                )
                SELECT
                    COUNT(1) AS Open_Count,
                    SUM(Pending_Qty_Entered) AS Pending_Items,
                    " + undeliveredValueSql + @" AS Undelivered_Value,
                    SUM(CASE WHEN Date_Promised < @Today THEN 1 ELSE 0 END) AS Past_Due_Count
                  FROM pending_by_order
                 WHERE Pending_Qty > 0";

            IDataReader dr = null;
            try
            {
                // "@Today" occurs twice in the assembled text - once inside
                // BuildBaseOrdersSql()'s WHERE clause, once again in the outer
                // Past_Due_Count CASE. This DB layer binds parameters POSITIONALLY
                // (matching left-to-right textual occurrence, not by name), so both
                // occurrences need their own array entry, in order.
                dr = DB.ExecuteReader(sql, new[]
                {
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                    new SqlParameter("@Today", today),
                    new SqlParameter("@Today", today)
                });

                if (dr != null && dr.Read())
                {
                    result.OpenCount = dr["Open_Count"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Open_Count"]);
                    result.PendingItems = dr["Pending_Items"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Pending_Items"]);
                    result.UndeliveredValue = dr["Undelivered_Value"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Undelivered_Value"]);
                    result.PastDueCount = dr["Past_Due_Count"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Past_Due_Count"]);
                }
            }
            finally
            {
                CloseReader(dr);
            }

            return result;
        }

        private OrdersResult GetOrdersData(Ctx ctx, int page, int size)
        {
            OrdersResult result = new OrdersResult { Rows = new List<OrderRow>() };
            if (ctx == null) { return result; }

            try
            {
                LoadOrders(ctx, result, page, size, includeStock: true);
            }
            catch (Exception ex)
            {
                // The free-stock join (M_Locator/M_Storage) is the one part of this
                // query genuinely prone to failure independent of the rest of the
                // backlog list (spec section 14 "Stock service unavailable") - retry
                // without it rather than losing the whole list over one column.
                Log.Log(Level.WARNING, "VAS_271_SOsPendingDeliveryWidget.GetOrdersData - retrying without stock shortfall", ex);
                result.Rows.Clear();
                result.StockUnavailable = true;
                LoadOrders(ctx, result, page, size, includeStock: false);
            }

            return result;
        }

        private void LoadOrders(Ctx ctx, OrdersResult result, int page, int size, bool includeStock)
        {
            string baseOrdersSql = MRole.GetDefault(ctx).AddAccessSQL(BuildBaseOrdersSql(), "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            // VAS163 2026-11-11: Row 20 C_Tax joined for the ex-tax pending value; Row 26 the
            // order's own currency returned for the Pending value column; Row 25 order_stats
            // sums Ordered over ALL Item lines (fully delivered products included - previously
            // filtered out by "WHERE pp.Pending_Qty > 0", which made a partly delivered order
            // look untouched) and keeps only orders with something pending via HAVING.
            string sql;
            if (includeStock)
            {
                sql = @"
                    WITH base_orders AS (" + baseOrdersSql + @"),
                    free_stock AS (
                        SELECT
                            l.M_Warehouse_ID AS M_Warehouse_ID,
                            s.M_Product_ID AS M_Product_ID,
                            SUM(
                                COALESCE(s.QtyOnHand, 0) - COALESCE(s.QtyReserved, 0)
                              - COALESCE(s.QtyDedicated, 0) - COALESCE(s.QtyAllocated, 0)
                            ) AS Free_Qty
                          FROM M_Locator l
                          INNER JOIN M_Storage s ON ( s.M_Locator_ID = l.M_Locator_ID AND s.IsActive = 'Y' )
                         WHERE l.IsActive = 'Y'
                         GROUP BY l.M_Warehouse_ID, s.M_Product_ID
                    ),
                    product_pending AS (
                        SELECT
                            bo.C_Order_ID AS C_Order_ID,
                            COALESCE(ol.M_Warehouse_ID, bo.Warehouse_Id) AS M_Warehouse_ID,
                            ol.M_Product_ID AS M_Product_ID,
                            SUM(" + PendingBaseQtySql + @") AS Pending_Qty,
                            SUM(" + PendingEnteredQtySql + @") AS Pending_Qty_Entered,
                            SUM(COALESCE(ol.QtyEntered, ol.QtyOrdered, 0)) AS Ordered_Qty,
                            SUM(" + PendingValueSql + @") AS Pending_Value
                          FROM base_orders bo
                          INNER JOIN C_OrderLine ol ON ( ol.C_Order_ID = bo.C_Order_ID AND ol.IsActive = 'Y' )
                          " + ItemOnlyJoinSql + @"
                          " + TaxJoinSql + @"
                         GROUP BY bo.C_Order_ID, COALESCE(ol.M_Warehouse_ID, bo.Warehouse_Id), ol.M_Product_ID
                    ),
                    order_stats AS (
                        SELECT
                            pp.C_Order_ID AS C_Order_ID,
                            SUM(pp.Ordered_Qty) AS Ordered_Qty,
                            SUM(pp.Pending_Qty_Entered) AS Pending_Qty,
                            SUM(pp.Pending_Value) AS Pending_Value,
                            SUM(
                                CASE
                                    WHEN pp.Pending_Qty > 0 AND pp.Pending_Qty > COALESCE(fs.Free_Qty, 0)
                                    THEN pp.Pending_Qty - COALESCE(fs.Free_Qty, 0)
                                    ELSE 0
                                END
                            ) AS Shortfall_Qty
                          FROM product_pending pp
                          LEFT OUTER JOIN free_stock fs
                            ON ( fs.M_Warehouse_ID = pp.M_Warehouse_ID AND fs.M_Product_ID = pp.M_Product_ID )
                         GROUP BY pp.C_Order_ID
                        HAVING SUM(pp.Pending_Qty) > 0
                    )
                    SELECT
                        bo.C_Order_ID AS Order_Id,
                        bo.Document_No AS Document_No,
                        bo.Date_Ordered AS Date_Ordered,
                        bo.Date_Promised AS Date_Promised,
                        COALESCE(bp.Name, N'') AS Customer_Name,
                        COALESCE(w.Name, N'') AS Warehouse_Name,
                        os.Ordered_Qty AS Ordered_Qty,
                        os.Pending_Qty AS Pending_Qty,
                        os.Pending_Value AS Pending_Value,
                        os.Shortfall_Qty AS Shortfall_Qty,
                        curr.CurSymbol AS Currency_Symbol,
                        curr.ISO_Code AS Currency_Code,
                        curr.StdPrecision AS Currency_Precision,
                        COUNT(1) OVER () AS Total_Rows
                      FROM order_stats os
                      INNER JOIN base_orders bo ON ( bo.C_Order_ID = os.C_Order_ID )
                      INNER JOIN C_BPartner bp ON ( bp.C_BPartner_ID = bo.Bpartner_Id )
                      LEFT OUTER JOIN M_Warehouse w ON ( w.M_Warehouse_ID = bo.Warehouse_Id )
                      LEFT OUTER JOIN C_Currency curr ON ( curr.C_Currency_ID = bo.Currency_Id )
                     ORDER BY bo.Date_Promised ASC, bo.Date_Ordered ASC, bo.Document_No ASC
                     OFFSET @Row_Offset ROWS FETCH NEXT @Page_Size ROWS ONLY";
            }
            else
            {
                sql = @"
                    WITH base_orders AS (" + baseOrdersSql + @"),
                    product_pending AS (
                        SELECT
                            bo.C_Order_ID AS C_Order_ID,
                            SUM(" + PendingBaseQtySql + @") AS Pending_Qty,
                            SUM(" + PendingEnteredQtySql + @") AS Pending_Qty_Entered,
                            SUM(COALESCE(ol.QtyEntered, ol.QtyOrdered, 0)) AS Ordered_Qty,
                            SUM(" + PendingValueSql + @") AS Pending_Value
                          FROM base_orders bo
                          INNER JOIN C_OrderLine ol ON ( ol.C_Order_ID = bo.C_Order_ID AND ol.IsActive = 'Y' )
                          " + ItemOnlyJoinSql + @"
                          " + TaxJoinSql + @"
                         GROUP BY bo.C_Order_ID
                    )
                    SELECT
                        bo.C_Order_ID AS Order_Id,
                        bo.Document_No AS Document_No,
                        bo.Date_Ordered AS Date_Ordered,
                        bo.Date_Promised AS Date_Promised,
                        COALESCE(bp.Name, N'') AS Customer_Name,
                        COALESCE(w.Name, N'') AS Warehouse_Name,
                        pp.Ordered_Qty AS Ordered_Qty,
                        pp.Pending_Qty_Entered AS Pending_Qty,
                        pp.Pending_Value AS Pending_Value,
                        curr.CurSymbol AS Currency_Symbol,
                        curr.ISO_Code AS Currency_Code,
                        curr.StdPrecision AS Currency_Precision,
                        COUNT(1) OVER () AS Total_Rows
                      FROM product_pending pp
                      INNER JOIN base_orders bo ON ( bo.C_Order_ID = pp.C_Order_ID )
                      INNER JOIN C_BPartner bp ON ( bp.C_BPartner_ID = bo.Bpartner_Id )
                      LEFT OUTER JOIN M_Warehouse w ON ( w.M_Warehouse_ID = bo.Warehouse_Id )
                      LEFT OUTER JOIN C_Currency curr ON ( curr.C_Currency_ID = bo.Currency_Id )
                     WHERE pp.Pending_Qty > 0
                     ORDER BY bo.Date_Promised ASC, bo.Date_Ordered ASC, bo.Document_No ASC
                     OFFSET @Row_Offset ROWS FETCH NEXT @Page_Size ROWS ONLY";
            }

            // Per the confirmed SQL's CASE WHEN pending_qty <= 0 THEN 'Delivered' ELSE
            // 'Pending' END - every row returned here already satisfies pending_qty > 0
            // (both query variants filter on it), so the label is deterministically
            // "Pending" for this list. Resolved once via message key rather than a
            // literal in the SQL text (Prompt_Instructions "no static text in query").
            string pendingLabel = Msg.GetMsg(ctx, "VAS_271_DeliveryPendingChip", true) ?? "Pending"; // VAS163 2026-11-11: text-only label (3-arg GetMsg, no tip appended)
            // VAS163 2026-11-11: Row 25 "Partial" when part of the ordered quantity is already delivered.
            string partialLabel = Msg.GetMsg(ctx, "VAS_271_DeliveryPartialChip", true); // VAS163 2026-11-11: text-only label (3-arg GetMsg, no tip appended)
            if (string.IsNullOrEmpty(partialLabel) || partialLabel == "VAS_271_DeliveryPartialChip") { partialLabel = "Partial"; }

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, new[]
                {
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                    new SqlParameter("@Today", DateTime.Today),
                    new SqlParameter("@Row_Offset", page * size),
                    new SqlParameter("@Page_Size", size)
                });

                int total = 0;
                while (dr != null && dr.Read())
                {
                    total = dr["Total_Rows"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Total_Rows"]);
                    DateTime? dateOrdered = dr["Date_Ordered"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr["Date_Ordered"]);
                    DateTime? datePromised = dr["Date_Promised"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr["Date_Promised"]);

                    bool hasShortfallColumn = includeStock;
                    decimal? shortfallQty = hasShortfallColumn
                        ? (dr["Shortfall_Qty"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Shortfall_Qty"]))
                        : (decimal?)null;

                    // VAS163 2026-11-11: Row 25 Pending when ordered == pending, Partial when ordered > pending (> 0).
                    decimal orderedQty = dr["Ordered_Qty"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Ordered_Qty"]);
                    decimal pendingQty = dr["Pending_Qty"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Pending_Qty"]);
                    bool isPartial = pendingQty > 0m && Math.Round(orderedQty, 4) > Math.Round(pendingQty, 4);

                    result.Rows.Add(new OrderRow
                    {
                        SalesOrderId = Util.GetValueOfInt(dr["Order_Id"]),
                        SalesOrderNumber = Util.GetValueOfString(dr["Document_No"]),
                        SalesOrderDate = dateOrdered.HasValue ? dateOrdered.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                        PromisedDate = datePromised.HasValue ? datePromised.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                        CustomerName = Util.GetValueOfString(dr["Customer_Name"]),
                        WarehouseName = Util.GetValueOfString(dr["Warehouse_Name"]),
                        OrderedQty = orderedQty,
                        PendingQty = pendingQty,
                        PendingValue = dr["Pending_Value"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Pending_Value"]),
                        // VAS163 2026-11-11: Row 26 document (price list) currency of PendingValue.
                        CurrencyIso = (Util.GetValueOfString(dr["Currency_Code"]) ?? string.Empty).Trim(),
                        CurrencySymbol = (Util.GetValueOfString(dr["Currency_Symbol"]) ?? string.Empty).Trim(),
                        CurrencyPrecision = dr["Currency_Precision"] == DBNull.Value ? 2 : Util.GetValueOfInt(dr["Currency_Precision"]),
                        ShortfallQty = shortfallQty,
                        DeliveryStatus = isPartial ? partialLabel : pendingLabel,
                        IsPartial = isPartial
                    });
                }
                result.Total = total;
            }
            finally
            {
                CloseReader(dr);
            }
        }

        private OrderDetailResult GetSalesOrderDetailData(Ctx ctx, int orderId)
        {
            OrderDetailResult result = new OrderDetailResult { Lines = new List<OrderLineRow>() };
            if (ctx == null || orderId <= 0) { return result; }

            string language = GetLanguage(ctx);
            Dictionary<string, string> docStatusMap = GetDecodeMap("C_Order", "DocStatus", language);

            string headerSql = @"
                SELECT o.C_Order_ID AS Order_Id,
                       o.DocumentNo AS Document_No,
                       o.DateOrdered AS Date_Ordered,
                       o.DatePromised AS Date_Promised,
                       o.DocStatus AS Doc_Status_Code,
                       " + OrderValueExclTaxSql + @" AS Order_Value,
                       " + TaxIncludedSql + @" AS Price_List_Tax_Included,
                       curr.CurSymbol AS Currency_Symbol,
                       curr.ISO_Code AS Currency_Code,
                       curr.StdPrecision AS Currency_Precision,
                       o.DeliveryViaRule AS Delivery_Via_Rule,
                       o.M_Warehouse_ID AS Warehouse_Id,
                       COALESCE(bp.Name, N'') AS Customer_Name,
                       COALESCE(w.Name, N'') AS Warehouse_Name
                  FROM C_Order o
                  INNER JOIN C_BPartner bp ON ( bp.C_BPartner_ID = o.C_BPartner_ID )
                  LEFT OUTER JOIN M_Warehouse w ON ( w.M_Warehouse_ID = o.M_Warehouse_ID )
                  LEFT OUTER JOIN C_Currency curr ON ( curr.C_Currency_ID = o.C_Currency_ID )
                 WHERE o.C_Order_ID = @Order_Id
                   AND o.AD_Client_ID = @AD_Client_ID
                   AND o.IsActive = 'Y'";

            headerSql = MRole.GetDefault(ctx).AddAccessSQL(headerSql, "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string docStatusCode = "";
            int warehouseId = 0;
            bool priceListTaxIncluded = false;
            int stdPrecision = 2;

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(headerSql, new[]
                {
                    new SqlParameter("@Order_Id", orderId),
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
                });

                if (dr == null || !dr.Read()) { return result; }

                docStatusCode = Util.GetValueOfString(dr["Doc_Status_Code"]);
                warehouseId = dr["Warehouse_Id"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Warehouse_Id"]);
                priceListTaxIncluded = Util.GetValueOfString(dr["Price_List_Tax_Included"]) == "Y";
                stdPrecision = dr["Currency_Precision"] == DBNull.Value ? 2 : Util.GetValueOfInt(dr["Currency_Precision"]);
                string currencySymbol = Util.GetValueOfString(dr["Currency_Symbol"]);
                if (string.IsNullOrWhiteSpace(currencySymbol)) { currencySymbol = Util.GetValueOfString(dr["Currency_Code"]); }
                DateTime? dateOrdered = dr["Date_Ordered"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr["Date_Ordered"]);
                DateTime? datePromised = dr["Date_Promised"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr["Date_Promised"]);

                result.Order = new OrderHeader
                {
                    SalesOrderId = Util.GetValueOfInt(dr["Order_Id"]),
                    SalesOrderNumber = Util.GetValueOfString(dr["Document_No"]),
                    SalesOrderDate = dateOrdered.HasValue ? dateOrdered.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                    DatePromised = datePromised.HasValue ? datePromised.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                    CustomerName = Util.GetValueOfString(dr["Customer_Name"]),
                    DocumentStatus = DecodeLabel(docStatusMap, docStatusCode),
                    DocumentStatusCode = docStatusCode,
                    OrderValue = dr["Order_Value"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Order_Value"]),
                    CurrencySymbol = (currencySymbol ?? "").Trim(),
                    CurrencyPrecision = stdPrecision,
                    WarehouseName = Util.GetValueOfString(dr["Warehouse_Name"]),
                    DeliveryMode = DecodeLabel(GetDecodeMap("C_Order", "DeliveryViaRule", language), Util.GetValueOfString(dr["Delivery_Via_Rule"]))
                };
            }
            finally
            {
                CloseReader(dr);
            }

            if (result.Order == null) { return result; }

            LoadOrderLines(ctx, result, orderId, warehouseId, priceListTaxIncluded, stdPrecision);

            // Delivery state counts Item-type product lines only (Sr 8.15).
            bool anyDelivered = false, allDelivered = true;
            bool hasLines = false;
            foreach (OrderLineRow line in result.Lines)
            {
                if (!line.IsItem) { continue; }
                hasLines = true;
                if (line.QtyDelivered > 0) { anyDelivered = true; }
                if (line.QtyDelivered < line.QtyOrdered) { allDelivered = false; }
            }

            string deliveryStatusKey, deliveryStatusFallback;
            if (hasLines && allDelivered)
            {
                deliveryStatusKey = "VAS_271_DeliveryFull"; deliveryStatusFallback = "Delivered";
            }
            else if (anyDelivered)
            {
                deliveryStatusKey = "VAS_271_DeliveryPartial"; deliveryStatusFallback = "Partially delivered";
            }
            else
            {
                deliveryStatusKey = "VAS_271_DeliveryNone"; deliveryStatusFallback = "Not delivered";
            }
            result.Order.DeliveryStatus = Msg.GetMsg(ctx, deliveryStatusKey, true) ?? deliveryStatusFallback; // VAS163 2026-11-11: text-only label (3-arg GetMsg, no tip appended)

            return result;
        }

        /// <summary>
        /// Every active line of the order, each carrying its own free-stock figure
        /// (per <see cref="ResolveOnHand"/>) and a derived line-status chip. This
        /// widget's cohort is always DocStatus='CO', so line status only ever
        /// distinguishes fully/partly delivered from still-in-process - no
        /// drafted/voided branch is needed here.
        /// </summary>
        private void LoadOrderLines(Ctx ctx, OrderDetailResult result, int orderId, int headerWarehouseId, bool priceListTaxIncluded, int stdPrecision)
        {
            // ProductType is a CHAR column and the charge / product names are NVARCHAR -
            // all selected raw and resolved in C# (no COALESCE against a literal, see VAS_203).
            string sql = @"
                SELECT ol.C_OrderLine_ID AS Line_Id,
                       ol.Line AS Line_No,
                       ol.M_Product_ID AS Product_Id,
                       ol.C_Charge_ID AS Charge_Id,
                       ol.Description AS Description,
                       ol.QtyOrdered AS Qty_Ordered,
                       ol.QtyEntered AS Qty_Entered,
                       ol.QtyDelivered AS Qty_Delivered,
                       ol.PriceActual AS Price_Actual,
                       ol.PriceEntered AS Price_Entered,
                       ol.LineNetAmt AS Line_Net_Amt,
                       ol.TaxAmt AS Line_Tax_Amt,
                       ol.C_Tax_ID AS Line_Tax_Id,
                       tx.Rate AS Tax_Rate,
                       ol.M_AttributeSetInstance_ID AS Asi_Id,
                       ol.C_UOM_ID AS Uom_Id,
                       ol.M_Warehouse_ID AS Line_Warehouse_Id,
                       COALESCE(p.Name, p.Value) AS Product_Name,
                       p.ProductType AS Product_Type,
                       ch.Name AS Charge_Name,
                       COALESCE(asi.Description, N'') AS Attribute_Text,
                       COALESCE(u.Name, N'') AS Uom_Name,
                       COALESCE(bu.Name, N'') AS Base_Uom_Name
                  FROM C_OrderLine ol
                  LEFT OUTER JOIN M_Product p ON ( p.M_Product_ID = ol.M_Product_ID )
                  LEFT OUTER JOIN C_Charge ch ON ( ch.C_Charge_ID = ol.C_Charge_ID )
                  LEFT OUTER JOIN C_Tax tx ON ( tx.C_Tax_ID = ol.C_Tax_ID )
                  LEFT OUTER JOIN M_AttributeSetInstance asi ON ( asi.M_AttributeSetInstance_ID = ol.M_AttributeSetInstance_ID )
                  LEFT OUTER JOIN C_UOM u ON ( u.C_UOM_ID = ol.C_UOM_ID )
                  LEFT OUTER JOIN C_UOM bu ON ( bu.C_UOM_ID = p.C_UOM_ID )
                 WHERE ol.C_Order_ID = @Order_Id
                   AND ol.IsActive = 'Y'
                 ORDER BY ol.Line ASC
                 OFFSET 0 ROWS FETCH NEXT @Max_Line_Rows ROWS ONLY";

            // VAS163 2026-11-11: Row 20 on a tax-inclusive order each line is restated net of tax
            // with the order's own tax summary (C_OrderTax), as VAS_270 does.
            Dictionary<int, decimal> netRatioByTax = priceListTaxIncluded ? GetOrderTaxNetRatios(orderId) : new Dictionary<int, decimal>();

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, new[]
                {
                    new SqlParameter("@Order_Id", orderId),
                    new SqlParameter("@Max_Line_Rows", MaxLineRows)
                });

                while (dr != null && dr.Read())
                {
                    decimal qtyOrderedBase = dr["Qty_Ordered"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Qty_Ordered"]);
                    decimal qtyEntered = dr["Qty_Entered"] == DBNull.Value ? qtyOrderedBase : Util.GetValueOfDecimal(dr["Qty_Entered"]);
                    decimal qtyDeliveredBase = dr["Qty_Delivered"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Qty_Delivered"]);
                    int productId = dr["Product_Id"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Product_Id"]);
                    int lineWarehouseId = dr["Line_Warehouse_Id"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Line_Warehouse_Id"]);
                    int warehouseId = lineWarehouseId > 0 ? lineWarehouseId : headerWarehouseId;

                    // Sr 8.13: quantities in the line's selected UOM. QtyEntered is in the
                    // line's C_UOM_ID; QtyOrdered / QtyDelivered / storage are in the base
                    // UOM, so they are scaled by the line's entered/ordered ratio (VAS_203).
                    decimal uomRatio = qtyOrderedBase != 0m ? (qtyEntered / qtyOrderedBase) : 1m;
                    decimal qtyOrdered = qtyEntered;
                    decimal qtyDelivered = qtyDeliveredBase * uomRatio;

                    // Sr 8.15: pending / delivered / stock only for Item-type products.
                    bool isItem = productId > 0
                        && string.Equals((Util.GetValueOfString(dr["Product_Type"]) ?? "").Trim(), "I", StringComparison.OrdinalIgnoreCase);

                    // Sr 8.16: a charge line shows the charge's name instead of a blank.
                    string productName = Util.GetValueOfString(dr["Product_Name"]);
                    if (string.IsNullOrEmpty(productName)) { productName = Util.GetValueOfString(dr["Charge_Name"]); }
                    if (string.IsNullOrEmpty(productName)) { productName = Util.GetValueOfString(dr["Description"]); }

                    // Line amount without tax. VAS163 2026-11-11: Row 20 on a tax-inclusive order
                    // LineNetAmt is the gross: scaled by its tax's net/gross ratio from C_OrderTax
                    // (so the lines add up to the header SO value); only when the order has no tax
                    // summary for that tax is the line's own TaxAmt (or rate-derived tax) taken off.
                    decimal lineGross = dr["Line_Net_Amt"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Line_Net_Amt"]);
                    decimal lineAmt = lineGross;
                    int lineTaxId = dr["Line_Tax_Id"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Line_Tax_Id"]);
                    if (priceListTaxIncluded && netRatioByTax.ContainsKey(lineTaxId))
                    {
                        lineAmt = Math.Round(lineGross * netRatioByTax[lineTaxId], stdPrecision, MidpointRounding.AwayFromZero);
                    }
                    else if (priceListTaxIncluded)
                    {
                        decimal lineTax = dr["Line_Tax_Amt"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Line_Tax_Amt"]);
                        decimal taxRate = dr["Tax_Rate"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Tax_Rate"]);
                        if (lineTax == 0m && taxRate > 0m)
                        {
                            lineTax = Math.Round(lineGross * taxRate / (100m + taxRate), stdPrecision, MidpointRounding.AwayFromZero);
                        }
                        lineAmt = lineGross - lineTax;
                    }

                    // Sr 8.14: the rate per selected UOM (PriceEntered), falling back to PriceActual.
                    decimal rate = dr["Price_Entered"] == DBNull.Value
                        ? (dr["Price_Actual"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Price_Actual"]))
                        : Util.GetValueOfDecimal(dr["Price_Entered"]);
                    // VAS163 2026-11-11: Row 20 the tax-inclusive price is restated net with the same proportion.
                    if (priceListTaxIncluded && lineGross != 0m)
                    {
                        rate = Math.Round(rate * lineAmt / lineGross, 6, MidpointRounding.AwayFromZero);
                    }

                    // 5-Nov Row 26: In stock is ALWAYS in the product's base UOM (product
                    // master UOM), never scaled to the line's UOM - the base UOM name is sent
                    // with it. On-hand, not free stock (its reservation already holds this
                    // order). The shortage is judged in the line's UOM.
                    decimal onHandBase = isItem ? ResolveOnHand(ctx, productId, warehouseId) : 0m;
                    decimal qtyPending = isItem ? Math.Max(0m, qtyOrdered - qtyDelivered) : 0m;

                    result.Lines.Add(new OrderLineRow
                    {
                        LineNo = Util.GetValueOfInt(dr["Line_No"]),
                        ProductName = productName,
                        AttributeText = Util.GetValueOfString(dr["Attribute_Text"]),
                        UomName = Util.GetValueOfString(dr["Uom_Name"]),
                        BaseUomName = Util.GetValueOfString(dr["Base_Uom_Name"]),
                        IsItem = isItem,
                        QtyOrdered = qtyOrdered,
                        QtyDelivered = qtyDelivered,
                        QtyPending = qtyPending,
                        // VAS163 2026-11-11: Row 58 In stock never negative - 0 when the warehouse has no stock.
                        FreeStock = Math.Max(0m, onHandBase),
                        QtyShort = isItem ? Math.Max(0m, qtyPending - Math.Max(0m, onHandBase) * uomRatio) : 0m,
                        Rate = rate,
                        Amount = lineAmt
                    });
                }
            }
            finally
            {
                CloseReader(dr);
            }
        }

        /// <summary>
        /// Warehouse-level on-hand stock for one product (base UOM), summed across the
        /// warehouse's active locators (M_Storage joined to M_Locator). Reserved qty is
        /// not subtracted - it already contains the order being shown. A
        /// product/warehouse with no M_Storage rows reads as zero, never null.
        /// </summary>
        private decimal ResolveOnHand(Ctx ctx, int productId, int warehouseId)
        {
            if (productId <= 0 || warehouseId <= 0) { return 0m; }

            string sql = @"
                SELECT COALESCE(SUM(COALESCE(s.QtyOnHand, 0)), 0) AS On_Hand
                  FROM M_Storage s
                  INNER JOIN M_Locator loc ON ( loc.M_Locator_ID = s.M_Locator_ID )
                 WHERE s.M_Product_ID = @M_Product_ID
                   AND loc.M_Warehouse_ID = @M_Warehouse_ID
                   AND loc.IsActive = 'Y'";

            // "s" (M_Storage) is the main physical table here - M_Locator is only joined
            // to scope by warehouse (Prompt_Instructions "Case 1").
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "s", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            return Util.GetValueOfDecimal(DB.ExecuteScalar(sql, new[]
            {
                new SqlParameter("@M_Product_ID", productId),
                new SqlParameter("@M_Warehouse_ID", warehouseId)
            }, null));
        }

        /// <summary>
        /// VAS163 2026-11-11: Row 20 (copied from VAS_270, 5-Nov Row 45). Tax-inclusive order
        /// only: per C_Tax_ID, the share of the lines' gross (SUM(LineNetAmt)) that is NOT tax,
        /// taken from the order's own tax summary (C_OrderTax.TaxAmt) - the same tax the header
        /// SO value takes off. A tax with no (or an implausible) summary row is left out and the
        /// caller falls back to the line's own TaxAmt / rate.
        /// </summary>
        private static Dictionary<int, decimal> GetOrderTaxNetRatios(int orderId)
        {
            Dictionary<int, decimal> ratios = new Dictionary<int, decimal>();
            try
            {
                Dictionary<int, decimal> grossByTax = new Dictionary<int, decimal>();
                DataSet grossDs = DB.ExecuteDataset(@"
                    SELECT ol.C_Tax_ID AS Tax_Id, COALESCE(SUM(ol.LineNetAmt), 0) AS Gross
                      FROM C_OrderLine ol
                     WHERE ol.C_Order_ID = @Order_Id
                       AND ol.IsActive = 'Y'
                     GROUP BY ol.C_Tax_ID", new[] { new SqlParameter("@Order_Id", orderId) }, null);
                if (grossDs != null && grossDs.Tables.Count > 0)
                {
                    foreach (DataRow row in grossDs.Tables[0].Rows)
                    {
                        grossByTax[Util.GetValueOfInt(row["Tax_Id"])] = Util.GetValueOfDecimal(row["Gross"]);
                    }
                }

                DataSet taxDs = DB.ExecuteDataset(@"
                    SELECT ot.C_Tax_ID AS Tax_Id, COALESCE(SUM(ot.TaxAmt), 0) AS Tax_Amt
                      FROM C_OrderTax ot
                     WHERE ot.C_Order_ID = @Order_Id
                       AND ot.IsActive = 'Y'
                     GROUP BY ot.C_Tax_ID", new[] { new SqlParameter("@Order_Id", orderId) }, null);
                if (taxDs != null && taxDs.Tables.Count > 0)
                {
                    foreach (DataRow row in taxDs.Tables[0].Rows)
                    {
                        int taxId = Util.GetValueOfInt(row["Tax_Id"]);
                        decimal taxAmt = Util.GetValueOfDecimal(row["Tax_Amt"]);
                        decimal gross;
                        if (grossByTax.TryGetValue(taxId, out gross) && gross > 0m && taxAmt >= 0m && taxAmt < gross)
                        {
                            ratios[taxId] = (gross - taxAmt) / gross;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_271_SOsPendingDeliveryWidget.GetOrderTaxNetRatios", ex);
            }
            return ratios;
        }

        /// <summary>
        /// VAS163 2026-11-11: Row 28 C_Currency_ID of the login client's primary accounting
        /// schema (AD_ClientInfo.C_AcctSchema1_ID -> C_AcctSchema.C_Currency_ID, same lookup as
        /// FillBaseCurrency); 0 when it cannot be resolved (the card then sums unconverted).
        /// </summary>
        private static int GetBaseCurrencyId(Ctx ctx)
        {
            if (ctx == null) { return 0; }
            try
            {
                return Util.GetValueOfInt(DB.ExecuteScalar(@"
                    SELECT cs.C_Currency_ID
                      FROM AD_ClientInfo ci
                     INNER JOIN C_AcctSchema cs ON ( cs.C_AcctSchema_ID = ci.C_AcctSchema1_ID )
                     WHERE ci.AD_Client_ID = @AD_Client_ID",
                    new[] { new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()) }, null));
            }
            catch (Exception ex)
            {
                Log.Log(Level.WARNING, "VAS_271_SOsPendingDeliveryWidget.GetBaseCurrencyId", ex);
                return 0;
            }
        }

        /// <summary>The tenant's own label for a stored code, or the code itself when the dictionary carries no entry (never invented English).</summary>
        private static string DecodeLabel(Dictionary<string, string> map, string code)
        {
            if (string.IsNullOrEmpty(code)) { return ""; }
            string label;
            return map != null && map.TryGetValue(code, out label) ? label : code;
        }

        /// <summary>
        /// AD_Ref_List(_Trl) Code -&gt; Label map for one column, cached per
        /// column+language - never a hard-coded code table (Prompt_Instructions
        /// "Fetch Reference List Values for a Column").
        /// </summary>
        private static readonly ConcurrentDictionary<string, Dictionary<string, string>> DecodeMapCache =
            new ConcurrentDictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        private Dictionary<string, string> GetDecodeMap(string tableName, string columnName, string language)
        {
            string cacheKey = tableName + "." + columnName + "|" + language;

            Dictionary<string, string> cached;
            if (DecodeMapCache.TryGetValue(cacheKey, out cached)) { return cached; }

            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int columnId = GetColumnId(tableName, columnName);

            if (columnId > 0)
            {
                string sql = @"
                    SELECT rl.Value AS Code, COALESCE(rlt.Name, rl.Name, rl.Value) AS Label
                      FROM AD_Ref_List rl
                      INNER JOIN AD_Column col ON ( col.AD_Reference_Value_ID = rl.AD_Reference_ID )
                      LEFT OUTER JOIN AD_Ref_List_Trl rlt ON ( rlt.AD_Ref_List_ID = rl.AD_Ref_List_ID AND rlt.AD_Language = @AD_Language )
                     WHERE col.AD_Column_ID = @AD_Column_ID
                       AND rl.IsActive = 'Y'
                       AND col.IsActive = 'Y'
                     ORDER BY rl.Value";

                IDataReader dr = null;
                try
                {
                    dr = DB.ExecuteReader(sql, new[]
                    {
                        new SqlParameter("@AD_Language", SqlDbType.NVarChar) { Value = language },
                        new SqlParameter("@AD_Column_ID", SqlDbType.Int) { Value = columnId }
                    });
                    while (dr != null && dr.Read())
                    {
                        string code = Util.GetValueOfString(dr["Code"]);
                        if (!string.IsNullOrEmpty(code))
                        {
                            map[code] = Util.GetValueOfString(dr["Label"]);
                        }
                    }
                }
                catch (Exception ex)
                {
                    // A decode-map lookup must never take the whole endpoint down - worst
                    // case the raw stored code is shown instead of its label
                    // (DecodeLabel's own fallback).
                    Log.Log(Level.SEVERE, "VAS_271_SOsPendingDeliveryWidget.GetDecodeMap(" + tableName + "." + columnName + ")", ex);
                }
                finally
                {
                    CloseReader(dr);
                }
            }

            DecodeMapCache[cacheKey] = map;
            return map;
        }

        private static readonly ConcurrentDictionary<string, int> ColumnIdCache = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private static int GetColumnId(string tableName, string columnName)
        {
            string cacheKey = tableName + "." + columnName;

            int cached;
            if (ColumnIdCache.TryGetValue(cacheKey, out cached)) { return cached; }

            int columnId = Util.GetValueOfInt(DB.ExecuteScalar(
                "SELECT AD_Column_ID FROM AD_Column WHERE ColumnName = @Col AND AD_Table_ID = (SELECT AD_Table_ID FROM AD_Table WHERE TableName = @Tbl)",
                new[]
                {
                    new SqlParameter("@Col", SqlDbType.NVarChar) { Value = columnName },
                    new SqlParameter("@Tbl", SqlDbType.NVarChar) { Value = tableName }
                },
                null));

            ColumnIdCache[cacheKey] = columnId;
            return columnId;
        }

        private string GetLanguage(Ctx ctx)
        {
            string language = ctx == null ? string.Empty : ctx.GetAD_Language();
            return string.IsNullOrWhiteSpace(language) ? "en_US" : language;
        }

        private void CloseReader(IDataReader reader)
        {
            if (reader == null) { return; }
            reader.Close();
            reader.Dispose();
        }

        /// <summary>
        /// VAS163 2026-11-10: base currency of the login client's primary accounting schema
        /// (AD_ClientInfo.C_AcctSchema1_ID -> C_AcctSchema.C_Currency_ID, same lookup as
        /// VAS_205) so the widget labels its amounts with it instead of a hard-coded rupee.
        /// Left empty (client shows no symbol) when it cannot be resolved.
        /// </summary>
        private static void FillBaseCurrency(Ctx ctx, CurrencyPayload target)
        {
            if (ctx == null || target == null) { return; }
            try
            {
                DataSet ds = DB.ExecuteDataset(@"
                    SELECT c.ISO_Code AS Currency_Code, c.CurSymbol AS Currency_Symbol, c.StdPrecision AS Currency_Precision
                      FROM AD_ClientInfo ci
                     INNER JOIN C_AcctSchema cs ON ( cs.C_AcctSchema_ID = ci.C_AcctSchema1_ID )
                     INNER JOIN C_Currency c ON ( c.C_Currency_ID = cs.C_Currency_ID )
                     WHERE ci.AD_Client_ID = @AD_Client_ID",
                    new[] { new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()) }, null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) { return; }
                DataRow r = ds.Tables[0].Rows[0];
                target.CurrencyIso = (Util.GetValueOfString(r["Currency_Code"]) ?? string.Empty).Trim();
                target.CurrencySymbol = (Util.GetValueOfString(r["Currency_Symbol"]) ?? string.Empty).Trim();
                if (r["Currency_Precision"] != DBNull.Value) { target.CurrencyPrecision = Util.GetValueOfInt(r["Currency_Precision"]); }
            }
            catch (Exception ex)
            {
                Log.Log(Level.WARNING, "FillBaseCurrency", ex);
            }
        }

        /// <summary>VAS163 2026-11-10: base (accounting-schema) currency carried by the widget's first payload.</summary>
        private class CurrencyPayload
        {
            public string CurrencyIso { get; set; } = string.Empty;
            public string CurrencySymbol { get; set; } = string.Empty;
            public int? CurrencyPrecision { get; set; }
        }

        private JsonResult ErrorResult(Ctx ctx)
        {
            string message = Msg.GetMsg(ctx, "Error", true) ?? "Error"; // VAS163 2026-11-11: text-only label (3-arg GetMsg, no tip appended)
            string json = JsonConvert.SerializeObject(new { Error = message });
            return Json(json, JsonRequestBehavior.AllowGet);
        }

        private class SummaryResult : CurrencyPayload
        {
            public int OpenCount { get; set; }
            public decimal PendingItems { get; set; }
            public decimal UndeliveredValue { get; set; }
            public int PastDueCount { get; set; }
        }

        private class OrdersResult
        {
            public int Total { get; set; }
            public bool StockUnavailable { get; set; }
            public List<OrderRow> Rows { get; set; }
        }

        private class OrderRow
        {
            public int SalesOrderId { get; set; }
            public string SalesOrderNumber { get; set; }
            public string SalesOrderDate { get; set; }
            public string PromisedDate { get; set; }
            public string CustomerName { get; set; }
            public string WarehouseName { get; set; }
            public decimal OrderedQty { get; set; }
            public decimal PendingQty { get; set; }
            public decimal PendingValue { get; set; }
            // VAS163 2026-11-11: Row 26 document (price list) currency of PendingValue.
            public string CurrencyIso { get; set; }
            public string CurrencySymbol { get; set; }
            public int CurrencyPrecision { get; set; }
            public decimal? ShortfallQty { get; set; }
            public string DeliveryStatus { get; set; }
            // VAS163 2026-11-11: Row 25 true when part of the order is already delivered (chip tone).
            public bool IsPartial { get; set; }
        }

        private class OrderDetailResult
        {
            public OrderHeader Order { get; set; }
            public List<OrderLineRow> Lines { get; set; }
        }

        private class OrderHeader
        {
            public int SalesOrderId { get; set; }
            public string SalesOrderNumber { get; set; }
            public string SalesOrderDate { get; set; }
            public string DatePromised { get; set; }
            public string CustomerName { get; set; }
            public string DocumentStatus { get; set; }
            public string DocumentStatusCode { get; set; }
            public decimal OrderValue { get; set; }
            public string CurrencySymbol { get; set; }
            public int CurrencyPrecision { get; set; }
            public string WarehouseName { get; set; }
            public string DeliveryMode { get; set; }
            public string DeliveryStatus { get; set; }
        }

        private class OrderLineRow
        {
            public int LineNo { get; set; }
            public string ProductName { get; set; }
            public string AttributeText { get; set; }
            public string UomName { get; set; }
            /// <summary>The product's base (master) UOM name - the unit FreeStock is stated in (5-Nov Row 26).</summary>
            public string BaseUomName { get; set; }
            public bool IsItem { get; set; }
            public decimal QtyOrdered { get; set; }
            public decimal QtyDelivered { get; set; }
            public decimal QtyPending { get; set; }
            /// <summary>On-hand stock in the product's BASE UOM (5-Nov Row 26).</summary>
            public decimal FreeStock { get; set; }
            /// <summary>Pending quantity not covered by on-hand stock, in the line's UOM.</summary>
            public decimal QtyShort { get; set; }
            public decimal Rate { get; set; }
            public decimal Amount { get; set; }
        }
    }
}
