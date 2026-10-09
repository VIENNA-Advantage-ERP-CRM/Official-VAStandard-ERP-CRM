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
    /// </summary>
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
                string json = JsonConvert.SerializeObject(GetSummaryData(ctx));
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
            return @"
                SELECT o.C_Order_ID AS C_Order_ID,
                       o.DocumentNo AS Document_No,
                       o.DateOrdered AS Date_Ordered,
                       o.DatePromised AS Date_Promised,
                       o.C_BPartner_ID AS Bpartner_Id,
                       o.M_Warehouse_ID AS Warehouse_Id
                  FROM C_Order o
                 WHERE o.AD_Client_ID = @AD_Client_ID
                   AND o.IsActive = 'Y'
                   AND o.IsSOTrx = 'Y'
                   AND COALESCE(o.IsReturnTrx, 'N') = 'N'
                   AND COALESCE(o.IsSalesQuotation, 'N') = 'N'
                   AND o.DocStatus = 'CO'
                   AND o.DateOrdered <= @Today";
        }

        private SummaryResult GetSummaryData(Ctx ctx)
        {
            SummaryResult result = new SummaryResult();
            if (ctx == null) { return result; }

            DateTime today = DateTime.Today;

            // The only AddAccessSQL call for this statement - applied to the isolated
            // single-WHERE base_orders body BEFORE it is embedded below.
            string baseOrdersSql = MRole.GetDefault(ctx).AddAccessSQL(BuildBaseOrdersSql(), "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string sql = @"
                WITH base_orders AS (" + baseOrdersSql + @"),
                pending_by_order AS (
                    SELECT
                        bo.C_Order_ID AS C_Order_ID,
                        bo.Date_Promised AS Date_Promised,
                        SUM(
                            CASE
                                WHEN COALESCE(ol.QtyOrdered, 0) > COALESCE(ol.QtyDelivered, 0)
                                THEN COALESCE(ol.QtyOrdered, 0) - COALESCE(ol.QtyDelivered, 0)
                                ELSE 0
                            END
                        ) AS Pending_Qty,
                        SUM(
                            CASE
                                WHEN COALESCE(ol.QtyOrdered, 0) > COALESCE(ol.QtyDelivered, 0)
                                THEN (COALESCE(ol.QtyOrdered, 0) - COALESCE(ol.QtyDelivered, 0)) * COALESCE(ol.PriceActual, 0)
                                ELSE 0
                            END
                        ) AS Pending_Value
                      FROM base_orders bo
                      INNER JOIN C_OrderLine ol ON ( ol.C_Order_ID = bo.C_Order_ID AND ol.IsActive = 'Y' )
                     GROUP BY bo.C_Order_ID, bo.Date_Promised
                )
                SELECT
                    COUNT(1) AS Open_Count,
                    SUM(Pending_Qty) AS Pending_Items,
                    SUM(Pending_Value) AS Undelivered_Value,
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
                            SUM(
                                CASE
                                    WHEN COALESCE(ol.QtyOrdered, 0) > COALESCE(ol.QtyDelivered, 0)
                                    THEN COALESCE(ol.QtyOrdered, 0) - COALESCE(ol.QtyDelivered, 0)
                                    ELSE 0
                                END
                            ) AS Pending_Qty,
                            SUM(COALESCE(ol.QtyOrdered, 0)) AS Ordered_Qty,
                            SUM(
                                CASE
                                    WHEN COALESCE(ol.QtyOrdered, 0) > COALESCE(ol.QtyDelivered, 0)
                                    THEN (COALESCE(ol.QtyOrdered, 0) - COALESCE(ol.QtyDelivered, 0)) * COALESCE(ol.PriceActual, 0)
                                    ELSE 0
                                END
                            ) AS Pending_Value
                          FROM base_orders bo
                          INNER JOIN C_OrderLine ol ON ( ol.C_Order_ID = bo.C_Order_ID AND ol.IsActive = 'Y' )
                         GROUP BY bo.C_Order_ID, COALESCE(ol.M_Warehouse_ID, bo.Warehouse_Id), ol.M_Product_ID
                    ),
                    order_stats AS (
                        SELECT
                            pp.C_Order_ID AS C_Order_ID,
                            SUM(pp.Ordered_Qty) AS Ordered_Qty,
                            SUM(pp.Pending_Qty) AS Pending_Qty,
                            SUM(pp.Pending_Value) AS Pending_Value,
                            SUM(
                                CASE
                                    WHEN pp.Pending_Qty > COALESCE(fs.Free_Qty, 0)
                                    THEN pp.Pending_Qty - COALESCE(fs.Free_Qty, 0)
                                    ELSE 0
                                END
                            ) AS Shortfall_Qty
                          FROM product_pending pp
                          LEFT OUTER JOIN free_stock fs
                            ON ( fs.M_Warehouse_ID = pp.M_Warehouse_ID AND fs.M_Product_ID = pp.M_Product_ID )
                         WHERE pp.Pending_Qty > 0
                         GROUP BY pp.C_Order_ID
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
                        COUNT(1) OVER () AS Total_Rows
                      FROM order_stats os
                      INNER JOIN base_orders bo ON ( bo.C_Order_ID = os.C_Order_ID )
                      INNER JOIN C_BPartner bp ON ( bp.C_BPartner_ID = bo.Bpartner_Id )
                      LEFT OUTER JOIN M_Warehouse w ON ( w.M_Warehouse_ID = bo.Warehouse_Id )
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
                            SUM(
                                CASE
                                    WHEN COALESCE(ol.QtyOrdered, 0) > COALESCE(ol.QtyDelivered, 0)
                                    THEN COALESCE(ol.QtyOrdered, 0) - COALESCE(ol.QtyDelivered, 0)
                                    ELSE 0
                                END
                            ) AS Pending_Qty,
                            SUM(COALESCE(ol.QtyOrdered, 0)) AS Ordered_Qty,
                            SUM(
                                CASE
                                    WHEN COALESCE(ol.QtyOrdered, 0) > COALESCE(ol.QtyDelivered, 0)
                                    THEN (COALESCE(ol.QtyOrdered, 0) - COALESCE(ol.QtyDelivered, 0)) * COALESCE(ol.PriceActual, 0)
                                    ELSE 0
                                END
                            ) AS Pending_Value
                          FROM base_orders bo
                          INNER JOIN C_OrderLine ol ON ( ol.C_Order_ID = bo.C_Order_ID AND ol.IsActive = 'Y' )
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
                        pp.Pending_Qty AS Pending_Qty,
                        pp.Pending_Value AS Pending_Value,
                        COUNT(1) OVER () AS Total_Rows
                      FROM product_pending pp
                      INNER JOIN base_orders bo ON ( bo.C_Order_ID = pp.C_Order_ID )
                      INNER JOIN C_BPartner bp ON ( bp.C_BPartner_ID = bo.Bpartner_Id )
                      LEFT OUTER JOIN M_Warehouse w ON ( w.M_Warehouse_ID = bo.Warehouse_Id )
                     WHERE pp.Pending_Qty > 0
                     ORDER BY bo.Date_Promised ASC, bo.Date_Ordered ASC, bo.Document_No ASC
                     OFFSET @Row_Offset ROWS FETCH NEXT @Page_Size ROWS ONLY";
            }

            // Per the confirmed SQL's CASE WHEN pending_qty <= 0 THEN 'Delivered' ELSE
            // 'Pending' END - every row returned here already satisfies pending_qty > 0
            // (both query variants filter on it), so the label is deterministically
            // "Pending" for this list. Resolved once via message key rather than a
            // literal in the SQL text (Prompt_Instructions "no static text in query").
            string pendingLabel = Msg.GetMsg(ctx, "VAS_271_DeliveryPendingChip") ?? "Pending";

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

                    result.Rows.Add(new OrderRow
                    {
                        SalesOrderId = Util.GetValueOfInt(dr["Order_Id"]),
                        SalesOrderNumber = Util.GetValueOfString(dr["Document_No"]),
                        SalesOrderDate = dateOrdered.HasValue ? dateOrdered.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                        PromisedDate = datePromised.HasValue ? datePromised.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                        CustomerName = Util.GetValueOfString(dr["Customer_Name"]),
                        WarehouseName = Util.GetValueOfString(dr["Warehouse_Name"]),
                        OrderedQty = dr["Ordered_Qty"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Ordered_Qty"]),
                        PendingQty = dr["Pending_Qty"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Pending_Qty"]),
                        PendingValue = dr["Pending_Value"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Pending_Value"]),
                        ShortfallQty = shortfallQty,
                        DeliveryStatus = pendingLabel
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
                       o.TotalLines AS Order_Value,
                       o.DeliveryViaRule AS Delivery_Via_Rule,
                       o.M_Warehouse_ID AS Warehouse_Id,
                       COALESCE(bp.Name, N'') AS Customer_Name,
                       COALESCE(w.Name, N'') AS Warehouse_Name
                  FROM C_Order o
                  INNER JOIN C_BPartner bp ON ( bp.C_BPartner_ID = o.C_BPartner_ID )
                  LEFT OUTER JOIN M_Warehouse w ON ( w.M_Warehouse_ID = o.M_Warehouse_ID )
                 WHERE o.C_Order_ID = @Order_Id
                   AND o.AD_Client_ID = @AD_Client_ID
                   AND o.IsActive = 'Y'";

            headerSql = MRole.GetDefault(ctx).AddAccessSQL(headerSql, "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string docStatusCode = "";
            int warehouseId = 0;

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
                    WarehouseName = Util.GetValueOfString(dr["Warehouse_Name"]),
                    DeliveryMode = DecodeLabel(GetDecodeMap("C_Order", "DeliveryViaRule", language), Util.GetValueOfString(dr["Delivery_Via_Rule"]))
                };
            }
            finally
            {
                CloseReader(dr);
            }

            if (result.Order == null) { return result; }

            LoadOrderLines(ctx, result, orderId, warehouseId);

            bool anyDelivered = false, allDelivered = true;
            bool hasLines = result.Lines.Count > 0;
            foreach (OrderLineRow line in result.Lines)
            {
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
            result.Order.DeliveryStatus = Msg.GetMsg(ctx, deliveryStatusKey) ?? deliveryStatusFallback;

            return result;
        }

        /// <summary>
        /// Every active line of the order, each carrying its own free-stock figure
        /// (per <see cref="ResolveFreeStock"/>) and a derived line-status chip. This
        /// widget's cohort is always DocStatus='CO', so line status only ever
        /// distinguishes fully/partly delivered from still-in-process - no
        /// drafted/voided branch is needed here.
        /// </summary>
        private void LoadOrderLines(Ctx ctx, OrderDetailResult result, int orderId, int headerWarehouseId)
        {
            string sql = @"
                SELECT ol.C_OrderLine_ID AS Line_Id,
                       ol.Line AS Line_No,
                       ol.M_Product_ID AS Product_Id,
                       ol.QtyOrdered AS Qty_Ordered,
                       ol.QtyDelivered AS Qty_Delivered,
                       ol.PriceActual AS Price_Actual,
                       ol.LineNetAmt AS Line_Net_Amt,
                       ol.M_AttributeSetInstance_ID AS Asi_Id,
                       ol.C_UOM_ID AS Uom_Id,
                       ol.M_Warehouse_ID AS Line_Warehouse_Id,
                       COALESCE(p.Name, p.Value) AS Product_Name,
                       COALESCE(asi.Description, N'') AS Attribute_Text,
                       COALESCE(u.Name, N'') AS Uom_Name
                  FROM C_OrderLine ol
                  LEFT OUTER JOIN M_Product p ON ( p.M_Product_ID = ol.M_Product_ID )
                  LEFT OUTER JOIN M_AttributeSetInstance asi ON ( asi.M_AttributeSetInstance_ID = ol.M_AttributeSetInstance_ID )
                  LEFT OUTER JOIN C_UOM u ON ( u.C_UOM_ID = ol.C_UOM_ID )
                 WHERE ol.C_Order_ID = @Order_Id
                   AND ol.IsActive = 'Y'
                 ORDER BY ol.Line ASC
                 OFFSET 0 ROWS FETCH NEXT @Max_Line_Rows ROWS ONLY";

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
                    decimal qtyOrdered = dr["Qty_Ordered"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Qty_Ordered"]);
                    decimal qtyDelivered = dr["Qty_Delivered"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Qty_Delivered"]);
                    int productId = dr["Product_Id"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Product_Id"]);
                    int lineWarehouseId = dr["Line_Warehouse_Id"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Line_Warehouse_Id"]);
                    int warehouseId = lineWarehouseId > 0 ? lineWarehouseId : headerWarehouseId;

                    result.Lines.Add(new OrderLineRow
                    {
                        LineNo = Util.GetValueOfInt(dr["Line_No"]),
                        ProductName = Util.GetValueOfString(dr["Product_Name"]),
                        AttributeText = Util.GetValueOfString(dr["Attribute_Text"]),
                        UomName = Util.GetValueOfString(dr["Uom_Name"]),
                        QtyOrdered = qtyOrdered,
                        QtyDelivered = qtyDelivered,
                        QtyPending = qtyOrdered - qtyDelivered,
                        FreeStock = ResolveFreeStock(ctx, productId, warehouseId),
                        Rate = dr["Price_Actual"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Price_Actual"]),
                        Amount = dr["Line_Net_Amt"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Line_Net_Amt"])
                    });
                }
            }
            finally
            {
                CloseReader(dr);
            }
        }

        /// <summary>
        /// Warehouse-level free stock for one product: QtyOnHand - QtyReserved -
        /// QtyDedicated - QtyAllocated, summed across the warehouse's active
        /// locators (M_Storage joined to M_Locator). A product/warehouse with no
        /// M_Storage rows reads as zero, never null.
        /// </summary>
        private decimal ResolveFreeStock(Ctx ctx, int productId, int warehouseId)
        {
            if (productId <= 0 || warehouseId <= 0) { return 0m; }

            string sql = @"
                SELECT COALESCE(SUM(COALESCE(s.QtyOnHand, 0) - COALESCE(s.QtyReserved, 0)
                              - COALESCE(s.QtyDedicated, 0) - COALESCE(s.QtyAllocated, 0)), 0) AS Free_Stock
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

        private JsonResult ErrorResult(Ctx ctx)
        {
            string message = Msg.GetMsg(ctx, "Error") ?? "Error";
            string json = JsonConvert.SerializeObject(new { Error = message });
            return Json(json, JsonRequestBehavior.AllowGet);
        }

        private class SummaryResult
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
            public decimal? ShortfallQty { get; set; }
            public string DeliveryStatus { get; set; }
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
            public decimal QtyOrdered { get; set; }
            public decimal QtyDelivered { get; set; }
            public decimal QtyPending { get; set; }
            public decimal FreeStock { get; set; }
            public decimal Rate { get; set; }
            public decimal Amount { get; set; }
        }
    }
}
