/******************************************************
 * Module Name    : VAS
 * Purpose        : Sales Order dashboard "Short Supply Blocking Dispatch" table widget endpoints
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
    /// Module Name : VAS_280_ShortSupplyBlockingDispatchWidget
    /// Purpose     : Data endpoints for the 4x2 "Short Supply Blocking Dispatch" table
    ///               widget on the Sales Order dashboard - Product+ship-from-Warehouse
    ///               combinations where committed (DocStatus='CO') Sales Order demand
    ///               exceeds free stock, ranked by the VALUE of business being held up
    ///               (not by shortfall quantity - a small shortfall on an expensive
    ///               item outranks a large shortfall on a cheap one). (1) the ranked
    ///               shortage list plus a header chip totalling valueBlocked across
    ///               the COMPLETE result (not just the visible page), and (2) a
    ///               product+warehouse drill-down (stat strip + the Sales Orders whose
    ///               active lines contribute pending demand for that exact
    ///               product/warehouse), and (3) the shared Sales Order record-preview
    ///               data (header stats + paginated lines) for record/lines
    ///               drill-through inside that SO table.
    ///
    ///   Business definition per
    ///   13_Short_Supply_Blocking_Dispatch_Claude_Development_Prompt.txt:
    ///     - Demand = pending quantity (max(QtyOrdered - QtyDelivered, 0)) on active
    ///       lines of firm Sales Orders (DocStatus='CO' only - closed/draft/in-process/
    ///       void/reversed create no current dispatch demand here), grouped by
    ///       Product + the order's ship-from Warehouse (C_Order.M_Warehouse_ID).
    ///     - Free stock = QtyOnHand - QtyReserved - QtyDedicated - QtyAllocated,
    ///       summed at Product+Warehouse through M_Storage -&gt; M_Locator - the exact
    ///       same formula as every other widget on this dashboard (VAS_271/274/279).
    ///       No open PO, expected receipt, or in-transit inventory ever offsets this -
    ///       that would require a separate "expected in" concept, not a quieter free
    ///       stock number.
    ///     - shortQty = max(demandQty - freeStock, 0); rows with shortQty = 0 (fully
    ///       covered) never appear.
    ///     - Rate = the pending-quantity-weighted average of C_OrderLine.PriceActual
    ///       for that product/warehouse's demand (SUM(pendingQty*rate)/SUM(pendingQty)) -
    ///       the value at risk is the value of the actual commitment, never the item
    ///       master's standard price.
    ///     - valueBlocked = shortQty * weightedRate - the sort key, descending.
    ///
    ///   Per the CTE/MRole rule (Prompt_Instructions.txt "Case 1"): the demand and
    ///   stock aggregates each isolate their own single-FROM, WHERE-only fragment
    ///   (BuildBaseOrderLinesSql / BuildBaseStockSql) and AddAccessSQL is applied ONLY
    ///   to those isolated fragments - never to a fragment that already carries its own
    ///   GROUP BY (the VAS_270/VAS_277 ORA-00933/ORA-00904 lesson: appending the access
    ///   predicate after a trailing GROUP BY produces invalid SQL). All GROUP BY/JOIN
    ///   aggregation happens in the outer CTEs built around the already-filtered text.
    ///   Read-only here; no writes.
    /// Chronological development:
    ///   VAI052      2026-09-14 Created
    /// </summary>
    public class VAS_280_ShortSupplyBlockingDispatchWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_280_ShortSupplyBlockingDispatchWidgetController).FullName);

        private const int DefaultPageSize = 7;
        private const int MaxPageSize = 10;
        private const int MaxLineRows = 200;

        /// <summary>
        /// One page of the shortage list, ranked by value blocked descending, plus the
        /// total value blocked across the COMPLETE result (a window aggregate computed
        /// before OFFSET/FETCH trims to the page, so it is never just the page total).
        /// </summary>
        /// <param name="page">Zero-based page index.</param>
        /// <param name="size">Rows per page (capped at <see cref="MaxPageSize"/>).</param>
        /// <returns>JSON { TotalValueBlocked, Total, Rows:[...] } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetShortSupply(int page = 0, int size = DefaultPageSize)
        {
            if (Session["ctx"] == null) { return Json("", JsonRequestBehavior.AllowGet); }

            Ctx ctx = Session["ctx"] as Ctx;
            if (page < 0) { page = 0; }
            if (size <= 0 || size > MaxPageSize) { size = DefaultPageSize; }

            try
            {
                string json = JsonConvert.SerializeObject(GetShortSupplyData(ctx, page, size));
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_280_ShortSupplyBlockingDispatchWidget.GetShortSupply", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// The row-click drill-down for one Product+Warehouse: the 8-card stat strip
        /// plus one page of the Sales Orders whose active lines contribute pending
        /// demand for that exact product/warehouse.
        /// </summary>
        /// <param name="productId">M_Product_ID.</param>
        /// <param name="warehouseId">M_Warehouse_ID.</param>
        /// <param name="page">Zero-based page index.</param>
        /// <param name="size">Rows per page (capped at <see cref="MaxPageSize"/>).</param>
        /// <returns>JSON { Summary:{...}, Total, Rows:[...] } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetProductWarehouseDetail(int productId, int warehouseId, int page = 0, int size = DefaultPageSize)
        {
            if (Session["ctx"] == null) { return Json("", JsonRequestBehavior.AllowGet); }

            Ctx ctx = Session["ctx"] as Ctx;
            if (page < 0) { page = 0; }
            if (size <= 0 || size > MaxPageSize) { size = MaxPageSize; }

            try
            {
                string json = JsonConvert.SerializeObject(GetProductWarehouseDetailData(ctx, productId, warehouseId, page, size));
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_280_ShortSupplyBlockingDispatchWidget.GetProductWarehouseDetail", ex);
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
                Log.Log(Level.SEVERE, "VAS_280_ShortSupplyBlockingDispatchWidget.GetSalesOrderDetail", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// The demand fragment - one row per active order line of a firm ('CO') Sales
        /// Order, with NO aggregation of its own (Prompt_Instructions.txt "Case 1": a
        /// single "FROM C_Order o" with one JOIN to its own lines, WHERE-only, no
        /// GROUP BY). This is the ONLY thing AddAccessSQL is ever applied to for the
        /// demand side - the outer "demand" CTE does the SUM/GROUP BY once this text is
        /// already access-filtered and embedded.
        /// </summary>
        /// <summary>
        /// "suffix" gives this fragment's own bind names (@AD_Client_ID&lt;suffix&gt;
        /// etc.) - required because BuildShortageCalcCtesSql concatenates this
        /// fragment alongside a second, independently access-filtered fragment
        /// (BuildBaseStockSql) into ONE combined SQL statement. Oracle's driver binds
        /// each textual placeholder OCCURRENCE separately even when two occurrences
        /// share the same name, so reusing "@AD_Client_ID" verbatim in both fragments
        /// left one occurrence unbound (ORA-01008) - distinct names per fragment make
        /// every occurrence unambiguous regardless of the driver's binding mode.
        /// </summary>
        private static string BuildBaseOrderLinesSql(bool filterProductWarehouse, string suffix)
        {
            string filter = filterProductWarehouse
                ? " AND ol.M_Product_ID = @M_Product_ID" + suffix + " AND o.M_Warehouse_ID = @M_Warehouse_ID" + suffix
                : "";

            return @"
                SELECT o.C_Order_ID AS C_Order_ID,
                       ol.M_Product_ID AS M_Product_ID,
                       o.M_Warehouse_ID AS M_Warehouse_ID,
                       ol.QtyOrdered AS Qty_Ordered,
                       ol.QtyDelivered AS Qty_Delivered,
                       ol.PriceActual AS Price_Actual
                  FROM C_Order o
                  INNER JOIN C_OrderLine ol ON ( ol.C_Order_ID = o.C_Order_ID AND ol.IsActive = 'Y' )
                 WHERE o.AD_Client_ID = @AD_Client_ID" + suffix + @"
                   AND o.IsActive = 'Y'
                   AND o.IsSOTrx = 'Y'
                   AND COALESCE(o.IsSalesQuotation, 'N') = 'N'
                   AND COALESCE(o.IsReturnTrx, 'N') = 'N'
                   AND o.DocStatus = 'CO'" + filter;
        }

        /// <summary>
        /// The stock fragment - one row per active M_Storage record, with NO
        /// aggregation of its own (single "FROM M_Storage s", WHERE-only, no GROUP BY).
        /// The ONLY thing AddAccessSQL is ever applied to for the stock side - the
        /// outer "stock" CTE joins to M_Locator and does the SUM/GROUP BY once this
        /// text is already access-filtered and embedded.
        /// </summary>
        private static string BuildBaseStockSql(bool filterProduct, string suffix)
        {
            string filter = filterProduct ? " AND s.M_Product_ID = @M_Product_ID" + suffix : "";

            return @"
                SELECT s.M_Product_ID AS M_Product_ID,
                       s.M_Locator_ID AS M_Locator_ID,
                       s.QtyOnHand AS Qty_On_Hand,
                       s.QtyReserved AS Qty_Reserved,
                       s.QtyDedicated AS Qty_Dedicated,
                       s.QtyAllocated AS Qty_Allocated
                  FROM M_Storage s
                 WHERE s.AD_Client_ID = @AD_Client_ID" + suffix + @"
                   AND s.IsActive = 'Y'" + filter;
        }

        /// <summary>
        /// The full demand/stock/short-qty/weighted-rate calc, shared by the ranked
        /// list and the single-row drill-down summary. Callers prepend "WITH " and
        /// append their own final SELECT against "calc" - never nested as a second
        /// WITH, and never re-run through AddAccessSQL.
        /// </summary>
        private string BuildShortageCalcCtesSql(Ctx ctx, bool filterProductWarehouse)
        {
            string baseLinesSql = MRole.GetDefault(ctx).AddAccessSQL(BuildBaseOrderLinesSql(filterProductWarehouse, "L"), "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            string baseStockSql = MRole.GetDefault(ctx).AddAccessSQL(BuildBaseStockSql(filterProductWarehouse, "S"), "s", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string warehouseJoinFilter = filterProductWarehouse ? " AND l.M_Warehouse_ID = @M_Warehouse_IDS" : "";

            return @"
                base_lines AS (" + baseLinesSql + @"),
                demand AS (
                    SELECT
                        bl.M_Product_ID AS M_Product_ID,
                        bl.M_Warehouse_ID AS M_Warehouse_ID,
                        SUM(
                            CASE WHEN COALESCE(bl.Qty_Ordered, 0) > COALESCE(bl.Qty_Delivered, 0)
                                 THEN COALESCE(bl.Qty_Ordered, 0) - COALESCE(bl.Qty_Delivered, 0)
                                 ELSE 0
                            END
                        ) AS Demand_Qty,
                        SUM(
                            CASE WHEN COALESCE(bl.Qty_Ordered, 0) > COALESCE(bl.Qty_Delivered, 0)
                                 THEN (COALESCE(bl.Qty_Ordered, 0) - COALESCE(bl.Qty_Delivered, 0)) * COALESCE(bl.Price_Actual, 0)
                                 ELSE 0
                            END
                        ) AS Pending_Value,
                        COUNT(DISTINCT bl.C_Order_ID) AS Affected_Order_Count
                      FROM base_lines bl
                     GROUP BY bl.M_Product_ID, bl.M_Warehouse_ID
                ),
                base_stock AS (" + baseStockSql + @"),
                stock AS (
                    SELECT
                        bs.M_Product_ID AS M_Product_ID,
                        l.M_Warehouse_ID AS M_Warehouse_ID,
                        SUM(
                            COALESCE(bs.Qty_On_Hand, 0) - COALESCE(bs.Qty_Reserved, 0)
                          - COALESCE(bs.Qty_Dedicated, 0) - COALESCE(bs.Qty_Allocated, 0)
                        ) AS Free_Stock
                      FROM base_stock bs
                      INNER JOIN M_Locator l ON ( l.M_Locator_ID = bs.M_Locator_ID AND l.IsActive = 'Y'" + warehouseJoinFilter + @" )
                     GROUP BY bs.M_Product_ID, l.M_Warehouse_ID
                ),
                calc AS (
                    SELECT
                        d.M_Product_ID AS M_Product_ID,
                        d.M_Warehouse_ID AS M_Warehouse_ID,
                        d.Demand_Qty AS Demand_Qty,
                        COALESCE(st.Free_Stock, 0) AS Free_Stock,
                        CASE WHEN d.Demand_Qty > COALESCE(st.Free_Stock, 0)
                             THEN d.Demand_Qty - COALESCE(st.Free_Stock, 0)
                             ELSE 0
                        END AS Short_Qty,
                        CASE WHEN d.Demand_Qty <> 0 THEN ROUND(d.Pending_Value / d.Demand_Qty, 2) ELSE 0 END AS Weighted_Rate,
                        d.Affected_Order_Count AS Affected_Order_Count
                      FROM demand d
                      LEFT OUTER JOIN stock st
                        ON ( st.M_Product_ID = d.M_Product_ID AND st.M_Warehouse_ID = d.M_Warehouse_ID )
                )";
        }

        private ShortSupplyResult GetShortSupplyData(Ctx ctx, int page, int size)
        {
            ShortSupplyResult result = new ShortSupplyResult { Rows = new List<ShortSupplyRow>() };
            if (ctx == null) { return result; }

            string language = GetLanguage(ctx);
            Dictionary<string, string> uomCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            string sql = @"
                WITH " + BuildShortageCalcCtesSql(ctx, false) + @"
                SELECT
                    c.M_Product_ID AS Product_Id,
                    p.Name AS Product_Name,
                    COALESCE(uom.Name, N'') AS Uom_Name,
                    c.M_Warehouse_ID AS Warehouse_Id,
                    w.Name AS Warehouse_Name,
                    c.Demand_Qty AS Demand_Qty,
                    c.Free_Stock AS Free_Stock,
                    c.Short_Qty AS Short_Qty,
                    c.Weighted_Rate AS Weighted_Rate,
                    (c.Short_Qty * c.Weighted_Rate) AS Value_Blocked,
                    c.Affected_Order_Count AS Affected_Order_Count,
                    COUNT(1) OVER () AS Total_Rows,
                    SUM(c.Short_Qty * c.Weighted_Rate) OVER () AS Total_Value_Blocked
                  FROM calc c
                  INNER JOIN M_Product p ON ( p.M_Product_ID = c.M_Product_ID )
                  LEFT OUTER JOIN C_UOM uom ON ( uom.C_UOM_ID = p.C_UOM_ID )
                  INNER JOIN M_Warehouse w ON ( w.M_Warehouse_ID = c.M_Warehouse_ID )
                 WHERE c.Short_Qty > 0
                 ORDER BY (c.Short_Qty * c.Weighted_Rate) DESC, p.Name ASC
                 OFFSET @Row_Offset ROWS FETCH NEXT @Page_Size ROWS ONLY";

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, new[]
                {
                    new SqlParameter("@AD_Client_IDL", ctx.GetAD_Client_ID()),
                    new SqlParameter("@AD_Client_IDS", ctx.GetAD_Client_ID()),
                    new SqlParameter("@Row_Offset", page * size),
                    new SqlParameter("@Page_Size", size)
                });

                int total = 0;
                decimal totalValueBlocked = 0m;
                while (dr != null && dr.Read())
                {
                    total = dr["Total_Rows"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Total_Rows"]);
                    totalValueBlocked = dr["Total_Value_Blocked"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Total_Value_Blocked"]);

                    result.Rows.Add(new ShortSupplyRow
                    {
                        ProductId = Util.GetValueOfInt(dr["Product_Id"]),
                        ProductName = Util.GetValueOfString(dr["Product_Name"]),
                        UomName = Util.GetValueOfString(dr["Uom_Name"]),
                        WarehouseId = Util.GetValueOfInt(dr["Warehouse_Id"]),
                        WarehouseName = Util.GetValueOfString(dr["Warehouse_Name"]),
                        DemandQty = dr["Demand_Qty"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Demand_Qty"]),
                        FreeStock = dr["Free_Stock"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Free_Stock"]),
                        ShortQty = dr["Short_Qty"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Short_Qty"]),
                        ValueBlocked = dr["Value_Blocked"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Value_Blocked"]),
                        AffectedOrderCount = dr["Affected_Order_Count"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Affected_Order_Count"])
                    });
                }
                result.Total = total;
                result.TotalValueBlocked = totalValueBlocked;
            }
            finally
            {
                CloseReader(dr);
            }

            return result;
        }

        private ProductWarehouseDetailResult GetProductWarehouseDetailData(Ctx ctx, int productId, int warehouseId, int page, int size)
        {
            ProductWarehouseDetailResult result = new ProductWarehouseDetailResult { Rows = new List<OrderRow>() };
            if (ctx == null || productId <= 0 || warehouseId <= 0) { return result; }

            // Both the "L" (base_lines) and "S" (base_stock) suffixed names are needed -
            // see BuildBaseOrderLinesSql's remark: BuildShortageCalcCtesSql(true)
            // concatenates two independently access-filtered fragments into one
            // statement, each with its own copy of these placeholders.
            SqlParameter[] filterParams = new[]
            {
                new SqlParameter("@AD_Client_IDL", ctx.GetAD_Client_ID()),
                new SqlParameter("@M_Product_IDL", productId),
                new SqlParameter("@M_Warehouse_IDL", warehouseId),
                new SqlParameter("@AD_Client_IDS", ctx.GetAD_Client_ID()),
                new SqlParameter("@M_Product_IDS", productId),
                new SqlParameter("@M_Warehouse_IDS", warehouseId)
            };

            // Query 1 of 2 - the stat-strip summary for this exact product/warehouse.
            string summarySql = @"
                WITH " + BuildShortageCalcCtesSql(ctx, true) + @"
                SELECT
                    c.Demand_Qty AS Demand_Qty,
                    c.Free_Stock AS Free_Stock,
                    c.Short_Qty AS Short_Qty,
                    c.Weighted_Rate AS Weighted_Rate,
                    (c.Short_Qty * c.Weighted_Rate) AS Value_Blocked,
                    c.Affected_Order_Count AS Affected_Order_Count,
                    p.Name AS Product_Name,
                    COALESCE(uom.Name, N'') AS Uom_Name,
                    w.Name AS Warehouse_Name
                  FROM calc c
                  INNER JOIN M_Product p ON ( p.M_Product_ID = c.M_Product_ID )
                  LEFT OUTER JOIN C_UOM uom ON ( uom.C_UOM_ID = p.C_UOM_ID )
                  INNER JOIN M_Warehouse w ON ( w.M_Warehouse_ID = c.M_Warehouse_ID )";

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(summarySql, filterParams);
                if (dr != null && dr.Read())
                {
                    result.Summary = new ProductWarehouseSummary
                    {
                        ProductName = Util.GetValueOfString(dr["Product_Name"]),
                        UomName = Util.GetValueOfString(dr["Uom_Name"]),
                        WarehouseName = Util.GetValueOfString(dr["Warehouse_Name"]),
                        DemandQty = dr["Demand_Qty"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Demand_Qty"]),
                        FreeStock = dr["Free_Stock"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Free_Stock"]),
                        ShortQty = dr["Short_Qty"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Short_Qty"]),
                        Rate = dr["Weighted_Rate"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Weighted_Rate"]),
                        ValueBlocked = dr["Value_Blocked"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Value_Blocked"]),
                        AffectedOrderCount = dr["Affected_Order_Count"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Affected_Order_Count"])
                    };
                }
            }
            finally
            {
                CloseReader(dr);
            }

            if (result.Summary == null) { return result; }

            // Query 2 of 2 - the Sales Orders whose active lines contribute pending
            // demand for this exact product/warehouse. Mirrors VAS_279's
            // base_orders/line_pending/pending_orders shape: AddAccessSQL applies only
            // to the isolated base_orders body, and the product-specific pending test
            // happens in the outer line_pending CTE.
            string baseOrdersSql = MRole.GetDefault(ctx).AddAccessSQL(BuildBaseOrdersForWarehouseSql(), "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string listSql = @"
                WITH base_orders AS (" + baseOrdersSql + @"),
                line_pending AS (
                    SELECT
                        bo.C_Order_ID AS C_Order_ID,
                        SUM(COALESCE(ol.QtyOrdered, 0)) AS Qty_Ordered,
                        SUM(COALESCE(ol.QtyDelivered, 0)) AS Qty_Delivered,
                        SUM(
                            CASE WHEN ol.M_Product_ID = @M_Product_ID
                                  AND COALESCE(ol.QtyOrdered, 0) > COALESCE(ol.QtyDelivered, 0)
                                 THEN COALESCE(ol.QtyOrdered, 0) - COALESCE(ol.QtyDelivered, 0)
                                 ELSE 0
                            END
                        ) AS Product_Pending_Qty
                      FROM base_orders bo
                      INNER JOIN C_OrderLine ol ON ( ol.C_Order_ID = bo.C_Order_ID AND ol.IsActive = 'Y' )
                     GROUP BY bo.C_Order_ID
                ),
                pending_orders AS (
                    SELECT bo.C_Order_ID AS C_Order_ID, bo.Document_No AS Document_No, bo.Date_Ordered AS Date_Ordered,
                           bo.Order_Value AS Order_Value, bo.Doc_Status_Code AS Doc_Status_Code,
                           bo.Bpartner_Id AS Bpartner_Id, bo.Warehouse_Id AS Warehouse_Id, bo.Sales_Rep_Id AS Sales_Rep_Id,
                           lp.Qty_Ordered AS Qty_Ordered, lp.Qty_Delivered AS Qty_Delivered
                      FROM base_orders bo
                      INNER JOIN line_pending lp ON ( lp.C_Order_ID = bo.C_Order_ID )
                     WHERE lp.Product_Pending_Qty > 0
                )
                SELECT
                    po.C_Order_ID AS Order_Id,
                    po.Document_No AS Document_No,
                    po.Date_Ordered AS Date_Ordered,
                    po.Order_Value AS Order_Value,
                    po.Doc_Status_Code AS Doc_Status_Code,
                    po.Qty_Ordered AS Qty_Ordered,
                    po.Qty_Delivered AS Qty_Delivered,
                    COALESCE(bp.Name, N'') AS Customer_Name,
                    COALESCE(wh.Name, N'') AS Warehouse_Name,
                    COALESCE(rep.Name, N'') AS Representative_Name,
                    COUNT(1) OVER () AS Total_Rows
                  FROM pending_orders po
                  INNER JOIN C_BPartner bp ON ( bp.C_BPartner_ID = po.Bpartner_Id )
                  LEFT OUTER JOIN M_Warehouse wh ON ( wh.M_Warehouse_ID = po.Warehouse_Id )
                  LEFT OUTER JOIN AD_User rep ON ( rep.AD_User_ID = po.Sales_Rep_Id )
                 ORDER BY po.Date_Ordered DESC, po.Document_No DESC
                 OFFSET @Row_Offset ROWS FETCH NEXT @Page_Size ROWS ONLY";

            string language = GetLanguage(ctx);
            Dictionary<string, string> docStatusMap = GetDecodeMap("C_Order", "DocStatus", language);

            IDataReader dr2 = null;
            try
            {
                dr2 = DB.ExecuteReader(listSql, new[]
                {
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                    new SqlParameter("@M_Warehouse_ID", warehouseId),
                    new SqlParameter("@M_Product_ID", productId),
                    new SqlParameter("@Row_Offset", page * size),
                    new SqlParameter("@Page_Size", size)
                });

                int total = 0;
                while (dr2 != null && dr2.Read())
                {
                    total = dr2["Total_Rows"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr2["Total_Rows"]);
                    DateTime? dateOrdered = dr2["Date_Ordered"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr2["Date_Ordered"]);
                    string docStatusCode = Util.GetValueOfString(dr2["Doc_Status_Code"]);
                    decimal qtyOrdered = dr2["Qty_Ordered"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr2["Qty_Ordered"]);
                    decimal qtyDelivered = dr2["Qty_Delivered"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr2["Qty_Delivered"]);

                    string deliveryCode, deliveryFallback, deliveryStatusCode;
                    if (qtyOrdered > 0 && qtyDelivered >= qtyOrdered) { deliveryCode = "VAS_280_DeliveryFull"; deliveryFallback = "Delivered"; deliveryStatusCode = "FULL"; }
                    else if (qtyDelivered > 0) { deliveryCode = "VAS_280_DeliveryPartial"; deliveryFallback = "Partially delivered"; deliveryStatusCode = "PARTIAL"; }
                    else { deliveryCode = "VAS_280_DeliveryNone"; deliveryFallback = "Not delivered"; deliveryStatusCode = "PENDING"; }

                    result.Rows.Add(new OrderRow
                    {
                        SalesOrderId = Util.GetValueOfInt(dr2["Order_Id"]),
                        SalesOrderNumber = Util.GetValueOfString(dr2["Document_No"]),
                        SalesOrderDate = dateOrdered.HasValue ? dateOrdered.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                        CustomerName = Util.GetValueOfString(dr2["Customer_Name"]),
                        WarehouseName = Util.GetValueOfString(dr2["Warehouse_Name"]),
                        RepresentativeName = Util.GetValueOfString(dr2["Representative_Name"]),
                        OrderValue = dr2["Order_Value"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr2["Order_Value"]),
                        DocumentStatus = DecodeLabel(docStatusMap, docStatusCode),
                        DeliveryStatus = Msg.GetMsg(ctx, deliveryCode) ?? deliveryFallback,
                        DeliveryStatusCode = deliveryStatusCode
                    });
                }
                result.Total = total;
            }
            finally
            {
                CloseReader(dr2);
            }

            return result;
        }

        /// <summary>Same shape as BuildBaseOrderLinesSql's parent order, but projects header columns for the SO list display, scoped to one ship-from warehouse.</summary>
        private static string BuildBaseOrdersForWarehouseSql()
        {
            return @"
                SELECT o.C_Order_ID AS C_Order_ID,
                       o.DocumentNo AS Document_No,
                       o.DateOrdered AS Date_Ordered,
                       o.TotalLines AS Order_Value,
                       o.DocStatus AS Doc_Status_Code,
                       o.C_BPartner_ID AS Bpartner_Id,
                       o.M_Warehouse_ID AS Warehouse_Id,
                       o.SalesRep_ID AS Sales_Rep_Id
                  FROM C_Order o
                 WHERE o.AD_Client_ID = @AD_Client_ID
                   AND o.IsActive = 'Y'
                   AND o.IsSOTrx = 'Y'
                   AND COALESCE(o.IsSalesQuotation, 'N') = 'N'
                   AND COALESCE(o.IsReturnTrx, 'N') = 'N'
                   AND o.DocStatus = 'CO'
                   AND o.M_Warehouse_ID = @M_Warehouse_ID";
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

                string docStatusCode = Util.GetValueOfString(dr["Doc_Status_Code"]);
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
            if (hasLines && allDelivered) { deliveryStatusKey = "VAS_280_DeliveryFull"; deliveryStatusFallback = "Delivered"; }
            else if (anyDelivered) { deliveryStatusKey = "VAS_280_DeliveryPartial"; deliveryStatusFallback = "Partially delivered"; }
            else { deliveryStatusKey = "VAS_280_DeliveryNone"; deliveryStatusFallback = "Not delivered"; }
            result.Order.DeliveryStatus = Msg.GetMsg(ctx, deliveryStatusKey) ?? deliveryStatusFallback;

            return result;
        }

        /// <summary>Every active line of the order, each carrying its own free-stock figure.</summary>
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
        /// QtyDedicated - QtyAllocated, summed across the warehouse's active locators -
        /// the exact same formula used by the main shortage aggregate above.
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

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "s", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            return Util.GetValueOfDecimal(DB.ExecuteScalar(sql, new[]
            {
                new SqlParameter("@M_Product_ID", productId),
                new SqlParameter("@M_Warehouse_ID", warehouseId)
            }, null));
        }

        private static string DecodeLabel(Dictionary<string, string> map, string code)
        {
            if (string.IsNullOrEmpty(code)) { return ""; }
            string label;
            return map != null && map.TryGetValue(code, out label) ? label : code;
        }

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
                    Log.Log(Level.SEVERE, "VAS_280_ShortSupplyBlockingDispatchWidget.GetDecodeMap(" + tableName + "." + columnName + ")", ex);
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

        private class ShortSupplyResult
        {
            public decimal TotalValueBlocked { get; set; }
            public int Total { get; set; }
            public List<ShortSupplyRow> Rows { get; set; }
        }

        private class ShortSupplyRow
        {
            public int ProductId { get; set; }
            public string ProductName { get; set; }
            public string UomName { get; set; }
            public int WarehouseId { get; set; }
            public string WarehouseName { get; set; }
            public decimal DemandQty { get; set; }
            public decimal FreeStock { get; set; }
            public decimal ShortQty { get; set; }
            public decimal ValueBlocked { get; set; }
            public int AffectedOrderCount { get; set; }
        }

        private class ProductWarehouseDetailResult
        {
            public ProductWarehouseSummary Summary { get; set; }
            public int Total { get; set; }
            public List<OrderRow> Rows { get; set; }
        }

        private class ProductWarehouseSummary
        {
            public string ProductName { get; set; }
            public string UomName { get; set; }
            public string WarehouseName { get; set; }
            public decimal DemandQty { get; set; }
            public decimal FreeStock { get; set; }
            public decimal ShortQty { get; set; }
            public decimal Rate { get; set; }
            public decimal ValueBlocked { get; set; }
            public int AffectedOrderCount { get; set; }
        }

        private class OrderRow
        {
            public int SalesOrderId { get; set; }
            public string SalesOrderNumber { get; set; }
            public string SalesOrderDate { get; set; }
            public string CustomerName { get; set; }
            public string WarehouseName { get; set; }
            public string RepresentativeName { get; set; }
            public decimal OrderValue { get; set; }
            public string DocumentStatus { get; set; }
            public string DeliveryStatus { get; set; }
            public string DeliveryStatusCode { get; set; }
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
