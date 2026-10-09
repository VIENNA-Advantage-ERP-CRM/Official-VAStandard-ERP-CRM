/******************************************************
 * Module Name    : VAS
 * Purpose        : Sales Order dashboard "Top Products" ranked bar-list widget endpoints
 * chronological  : Development
 * Created Date   : 2026-09-15
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
    /// Module Name : VAS_285_TopProductsWidget
    /// Purpose     : Data endpoints for the 2x2 "Top Products" compact ranked bar-list
    ///               widget on the Sales Order dashboard - the four products driving the
    ///               most booked Sales Order value in a selected Month/Year, plus a
    ///               per-product demand/stock drill-down and the shared Sales Order
    ///               record-preview data for record/lines drill-through inside that
    ///               drill-down's order table.
    ///
    ///   Business definition per 18_Top_Products_Claude_Development_Prompt.txt
    ///   (CONFIRMED - the paired HTML mock ships with no period filter at all; this
    ///   prompt's own USER OVERRIDE explicitly adds the same compact Month/Year filter
    ///   used by every other Sales dashboard value widget, defaulted to the current
    ///   month, calling stopPropagation() so it never triggers the row/modal handler):
    ///     - Ranking scope = booked Sales Orders (DocStatus IN ('CO','CL'), standard SO
    ///       filters) whose C_Order.DateOrdered falls in the selected period.
    ///     - Ranked by SUM(C_OrderLine.LineNetAmt) per product, descending, top 4 only -
    ///       this is the same value definition as the category/customer value widgets so
    ///       the ranking reconciles with the category donut for the same period.
    ///     - Product identity: name from M_Product.Name; SKU = COALESCE(M_Product.SKU,
    ///       M_Product.Value) so an item with no SKU still shows something.
    ///     - Drill-down's "Qty on open SO" and "Free stock" are CURRENT operational
    ///       state (never period-filtered) using the EXACT SAME definitions as the
    ///       Short Supply widget (VAS_280): open qty = pending quantity
    ///       (max(QtyOrdered-QtyDelivered,0)) summed over active lines of firm
    ///       (DocStatus='CO') Sales Orders for the product across every warehouse (Top
    ///       Products is not warehouse-scoped); free stock = QtyOnHand-QtyReserved-
    ///       QtyDedicated-QtyAllocated summed over the product's active M_Storage
    ///       through active M_Locator, again across every warehouse. Only the "SO
    ///       value" stat in the drill-down stays period-filtered - it is the same
    ///       period as the widget's own ranking, recomputed for this one product.
    ///     - Coverage = min(100, freeStock/openQty*100); when openQty is zero the
    ///       concept does not apply (nothing is pending) so it renders as "-", never a
    ///       divide-by-zero or a misleading 0%/100%.
    ///
    ///   Per the CTE/MRole rule (Prompt_Instructions.txt "Case 1"): the ranking query
    ///   isolates a single "FROM C_Order o" WHERE-only fragment (no GROUP BY) and calls
    ///   AddAccessSQL ONLY on that isolated fragment; the C_OrderLine/M_Product
    ///   aggregation happens in an outer CTE built around the already-filtered text.
    ///   The single-product scalar queries (period value / open qty / free stock) carry
    ///   no GROUP BY of their own, so AddAccessSQL is applied to each directly (the
    ///   same pattern VAS_280's ResolveFreeStock already uses).
    ///   Read-only here; no writes.
    /// Chronological development:
    ///   VAI052      2026-09-15 Created
    /// </summary>
    public class VAS_285_TopProductsWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_285_TopProductsWidgetController).FullName);

        private const int TopProductCount = 4;
        private const int DefaultPageSize = 10;
        private const int MaxPageSize = 10;
        private const int MaxLineRows = 200;

        /// <summary>
        /// The top-<see cref="TopProductCount"/> products by summed booked-order-line
        /// value in the selected month.
        /// </summary>
        /// <param name="month">Zero-based month (0=Jan..11=Dec).</param>
        /// <param name="year">Calendar year.</param>
        /// <returns>JSON { Products:[ { ProductId, Sku, ProductName, OrderValue, Rank } ] } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetTopProducts(int month, int year)
        {
            if (Session["ctx"] == null) { return Json("", JsonRequestBehavior.AllowGet); }

            Ctx ctx = Session["ctx"] as Ctx;

            try
            {
                string json = JsonConvert.SerializeObject(GetTopProductsData(ctx, month, year));
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_285_TopProductsWidget.GetTopProducts", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// The row-click drill-down for one product: period SO value, current open
        /// quantity, current free stock, and computed coverage.
        /// </summary>
        /// <param name="productId">M_Product_ID.</param>
        /// <param name="month">Zero-based month (0=Jan..11=Dec) - scopes the SO value stat only.</param>
        /// <param name="year">Calendar year.</param>
        /// <returns>JSON { ProductName, Sku, UomName, OrderValue, OpenQty, FreeStock, CoveragePercent } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetProductDemand(int productId, int month, int year)
        {
            if (Session["ctx"] == null) { return Json("", JsonRequestBehavior.AllowGet); }

            Ctx ctx = Session["ctx"] as Ctx;

            try
            {
                string json = JsonConvert.SerializeObject(GetProductDemandData(ctx, productId, month, year));
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_285_TopProductsWidget.GetProductDemand", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// One page of the selected period's booked Sales Orders that carry this
        /// product.
        /// </summary>
        /// <param name="productId">M_Product_ID.</param>
        /// <param name="month">Zero-based month (0=Jan..11=Dec).</param>
        /// <param name="year">Calendar year.</param>
        /// <param name="page">Zero-based page index.</param>
        /// <param name="size">Rows per page (capped at <see cref="MaxPageSize"/>).</param>
        /// <returns>JSON { Total, Rows:[...] } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetProductOrders(int productId, int month, int year, int page = 0, int size = DefaultPageSize)
        {
            if (Session["ctx"] == null) { return Json("", JsonRequestBehavior.AllowGet); }

            Ctx ctx = Session["ctx"] as Ctx;
            if (page < 0) { page = 0; }
            if (size <= 0 || size > MaxPageSize) { size = DefaultPageSize; }

            try
            {
                string json = JsonConvert.SerializeObject(GetProductOrdersData(ctx, productId, month, year, page, size));
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_285_TopProductsWidget.GetProductOrders", ex);
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
                Log.Log(Level.SEVERE, "VAS_285_TopProductsWidget.GetSalesOrderDetail", ex);
                return ErrorResult(ctx);
            }
        }

        private static void ResolvePeriod(int month, int year, out DateTime periodStart, out DateTime periodEnd)
        {
            DateTime today = DateTime.Today;
            int resolvedYear = year > 0 ? year : today.Year;
            int resolvedMonth = (month >= 0 && month <= 11) ? month : (today.Month - 1);

            periodStart = new DateTime(resolvedYear, resolvedMonth + 1, 1);
            periodEnd = periodStart.AddMonths(1);
        }

        /// <summary>
        /// The single physical-table fragment - one "FROM C_Order o", WHERE-only, no
        /// GROUP BY (Prompt_Instructions.txt "Case 1"). The ONLY thing AddAccessSQL is
        /// ever applied to for the ranking/order-list side of this widget. Cohort =
        /// booked Sales Orders (CO/CL) ordered in the given half-open period.
        /// </summary>
        private static string BuildBaseOrdersSql()
        {
            return @"
                SELECT o.C_Order_ID AS C_Order_ID,
                       o.DocumentNo AS Document_No,
                       o.DateOrdered AS Date_Ordered,
                       o.TotalLines AS Order_Value,
                       o.DocStatus AS Doc_Status_Code,
                       o.C_BPartner_ID AS BPartner_Id,
                       o.M_Warehouse_ID AS Warehouse_Id,
                       o.SalesRep_ID AS Sales_Rep_Id
                  FROM C_Order o
                 WHERE o.AD_Client_ID = @AD_Client_ID
                   AND o.IsActive = 'Y'
                   AND o.IsSOTrx = 'Y'
                   AND COALESCE(o.IsSalesQuotation, 'N') = 'N'
                   AND COALESCE(o.IsReturnTrx, 'N') = 'N'
                   AND o.DocStatus IN ('CO', 'CL')
                   AND o.DateOrdered >= @Period_Start
                   AND o.DateOrdered < @Period_End";
        }

        private TopProductsResult GetTopProductsData(Ctx ctx, int month, int year)
        {
            TopProductsResult result = new TopProductsResult { Products = new List<TopProductRow>() };
            if (ctx == null) { return result; }

            DateTime periodStart, periodEnd;
            ResolvePeriod(month, year, out periodStart, out periodEnd);

            string baseOrdersSql = MRole.GetDefault(ctx).AddAccessSQL(BuildBaseOrdersSql(), "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string sql = @"
                WITH base_orders AS (" + baseOrdersSql + @"),
                ranked AS (
                    SELECT p.M_Product_ID AS Product_Id,
                           COALESCE(p.SKU, p.Value) AS Sku,
                           p.Name AS Product_Name,
                           SUM(COALESCE(ol.LineNetAmt, 0)) AS Order_Value
                      FROM C_OrderLine ol
                      INNER JOIN base_orders bo ON ( bo.C_Order_ID = ol.C_Order_ID )
                      INNER JOIN M_Product p ON ( p.M_Product_ID = ol.M_Product_ID )
                     WHERE ol.IsActive = 'Y'
                     GROUP BY p.M_Product_ID, p.SKU, p.Value, p.Name
                )
                SELECT Product_Id, Sku, Product_Name, Order_Value
                  FROM ranked
                 ORDER BY Order_Value DESC, Product_Name ASC
                 FETCH FIRST @Top_Count ROWS ONLY";

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, new[]
                {
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                    new SqlParameter("@Period_Start", periodStart),
                    new SqlParameter("@Period_End", periodEnd),
                    new SqlParameter("@Top_Count", TopProductCount)
                });

                int rank = 0;
                while (dr != null && dr.Read())
                {
                    rank++;
                    result.Products.Add(new TopProductRow
                    {
                        ProductId = Util.GetValueOfInt(dr["Product_Id"]),
                        Sku = Util.GetValueOfString(dr["Sku"]),
                        ProductName = Util.GetValueOfString(dr["Product_Name"]),
                        OrderValue = dr["Order_Value"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Order_Value"]),
                        Rank = rank
                    });
                }
            }
            finally
            {
                CloseReader(dr);
            }

            return result;
        }

        private ProductDemandResult GetProductDemandData(Ctx ctx, int productId, int month, int year)
        {
            ProductDemandResult result = new ProductDemandResult();
            if (ctx == null || productId <= 0) { return result; }

            DateTime periodStart, periodEnd;
            ResolvePeriod(month, year, out periodStart, out periodEnd);

            // Product identity - not access-scoped (the item master lookup used by every
            // other widget's product/line joins is not independently row-filtered).
            string infoSql = @"
                SELECT p.Name AS Product_Name,
                       COALESCE(p.SKU, p.Value) AS Sku,
                       COALESCE(u.Name, N'') AS Uom_Name
                  FROM M_Product p
                  LEFT OUTER JOIN C_UOM u ON ( u.C_UOM_ID = p.C_UOM_ID )
                 WHERE p.M_Product_ID = @M_Product_ID";

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(infoSql, new[] { new SqlParameter("@M_Product_ID", productId) });
                if (dr == null || !dr.Read()) { return result; }

                result.ProductName = Util.GetValueOfString(dr["Product_Name"]);
                result.Sku = Util.GetValueOfString(dr["Sku"]);
                result.UomName = Util.GetValueOfString(dr["Uom_Name"]);
            }
            finally
            {
                CloseReader(dr);
            }

            // Period SO value for this one product - same value definition as the
            // ranking (SUM of active-line LineNetAmt), scoped to the selected period.
            // No GROUP BY on this query, so AddAccessSQL applies directly (matches
            // VAS_280's ResolveFreeStock precedent for a JOIN with a single aggregate row).
            string periodValueSql = @"
                SELECT SUM(COALESCE(ol.LineNetAmt, 0)) AS Order_Value
                  FROM C_Order o
                  INNER JOIN C_OrderLine ol ON ( ol.C_Order_ID = o.C_Order_ID AND ol.IsActive = 'Y' AND ol.M_Product_ID = @M_Product_ID )
                 WHERE o.AD_Client_ID = @AD_Client_ID
                   AND o.IsActive = 'Y'
                   AND o.IsSOTrx = 'Y'
                   AND COALESCE(o.IsSalesQuotation, 'N') = 'N'
                   AND COALESCE(o.IsReturnTrx, 'N') = 'N'
                   AND o.DocStatus IN ('CO', 'CL')
                   AND o.DateOrdered >= @Period_Start
                   AND o.DateOrdered < @Period_End";

            periodValueSql = MRole.GetDefault(ctx).AddAccessSQL(periodValueSql, "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            result.OrderValue = Util.GetValueOfDecimal(DB.ExecuteScalar(periodValueSql, new[]
            {
                new SqlParameter("@M_Product_ID", productId),
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@Period_Start", periodStart),
                new SqlParameter("@Period_End", periodEnd)
            }, null));

            // Current open qty - EXACT same "pending quantity on firm (CO) Sales Orders"
            // definition as VAS_280's demand CTE, aggregated across every warehouse
            // (this widget is not warehouse-scoped). Never period-filtered.
            string openQtySql = @"
                SELECT SUM(
                           CASE WHEN COALESCE(ol.QtyOrdered, 0) > COALESCE(ol.QtyDelivered, 0)
                                THEN COALESCE(ol.QtyOrdered, 0) - COALESCE(ol.QtyDelivered, 0)
                                ELSE 0
                           END
                       ) AS Open_Qty
                  FROM C_Order o
                  INNER JOIN C_OrderLine ol ON ( ol.C_Order_ID = o.C_Order_ID AND ol.IsActive = 'Y' AND ol.M_Product_ID = @M_Product_ID )
                 WHERE o.AD_Client_ID = @AD_Client_ID
                   AND o.IsActive = 'Y'
                   AND o.IsSOTrx = 'Y'
                   AND COALESCE(o.IsSalesQuotation, 'N') = 'N'
                   AND COALESCE(o.IsReturnTrx, 'N') = 'N'
                   AND o.DocStatus = 'CO'";

            openQtySql = MRole.GetDefault(ctx).AddAccessSQL(openQtySql, "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            result.OpenQty = Util.GetValueOfDecimal(DB.ExecuteScalar(openQtySql, new[]
            {
                new SqlParameter("@M_Product_ID", productId),
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
            }, null));

            result.FreeStock = ResolveFreeStock(ctx, productId);

            result.CoveragePercent = result.OpenQty > 0
                ? (decimal?)Math.Min(100m, Math.Round(result.FreeStock * 100m / result.OpenQty, 0))
                : null;

            return result;
        }

        private ProductOrdersResult GetProductOrdersData(Ctx ctx, int productId, int month, int year, int page, int size)
        {
            ProductOrdersResult result = new ProductOrdersResult { Rows = new List<OrderRow>() };
            if (ctx == null || productId <= 0) { return result; }

            DateTime periodStart, periodEnd;
            ResolvePeriod(month, year, out periodStart, out periodEnd);

            string baseOrdersSql = MRole.GetDefault(ctx).AddAccessSQL(BuildBaseOrdersSql(), "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string sql = @"
                WITH base_orders AS (" + baseOrdersSql + @"),
                line_match AS (
                    SELECT bo.C_Order_ID AS C_Order_ID,
                           SUM(COALESCE(ol.QtyOrdered, 0)) AS Qty_Ordered,
                           SUM(COALESCE(ol.QtyDelivered, 0)) AS Qty_Delivered,
                           SUM(CASE WHEN ol.M_Product_ID = @M_Product_ID THEN 1 ELSE 0 END) AS Product_Line_Count
                      FROM base_orders bo
                      INNER JOIN C_OrderLine ol ON ( ol.C_Order_ID = bo.C_Order_ID AND ol.IsActive = 'Y' )
                     GROUP BY bo.C_Order_ID
                ),
                matched_orders AS (
                    SELECT bo.C_Order_ID AS C_Order_ID, bo.Document_No AS Document_No, bo.Date_Ordered AS Date_Ordered,
                           bo.Order_Value AS Order_Value, bo.Doc_Status_Code AS Doc_Status_Code,
                           bo.BPartner_Id AS BPartner_Id, bo.Warehouse_Id AS Warehouse_Id, bo.Sales_Rep_Id AS Sales_Rep_Id,
                           lm.Qty_Ordered AS Qty_Ordered, lm.Qty_Delivered AS Qty_Delivered
                      FROM base_orders bo
                      INNER JOIN line_match lm ON ( lm.C_Order_ID = bo.C_Order_ID )
                     WHERE lm.Product_Line_Count > 0
                )
                SELECT
                    mo.C_Order_ID AS Order_Id,
                    mo.Document_No AS Document_No,
                    mo.Date_Ordered AS Date_Ordered,
                    mo.Order_Value AS Order_Value,
                    mo.Doc_Status_Code AS Doc_Status_Code,
                    mo.Qty_Ordered AS Qty_Ordered,
                    mo.Qty_Delivered AS Qty_Delivered,
                    COALESCE(bp.Name, N'') AS Customer_Name,
                    COALESCE(wh.Name, N'') AS Warehouse_Name,
                    COALESCE(rep.Name, N'') AS Representative_Name,
                    COUNT(1) OVER () AS Total_Rows
                  FROM matched_orders mo
                  INNER JOIN C_BPartner bp ON ( bp.C_BPartner_ID = mo.BPartner_Id )
                  LEFT OUTER JOIN M_Warehouse wh ON ( wh.M_Warehouse_ID = mo.Warehouse_Id )
                  LEFT OUTER JOIN AD_User rep ON ( rep.AD_User_ID = mo.Sales_Rep_Id )
                 ORDER BY mo.Date_Ordered DESC, mo.Document_No DESC
                 OFFSET @Row_Offset ROWS FETCH NEXT @Page_Size ROWS ONLY";

            string language = GetLanguage(ctx);
            Dictionary<string, string> docStatusMap = GetDecodeMap("C_Order", "DocStatus", language);

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, new[]
                {
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                    new SqlParameter("@Period_Start", periodStart),
                    new SqlParameter("@Period_End", periodEnd),
                    new SqlParameter("@M_Product_ID", productId),
                    new SqlParameter("@Row_Offset", page * size),
                    new SqlParameter("@Page_Size", size)
                });

                int total = 0;
                while (dr != null && dr.Read())
                {
                    total = dr["Total_Rows"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Total_Rows"]);
                    DateTime? dateOrdered = dr["Date_Ordered"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr["Date_Ordered"]);
                    string docStatusCode = Util.GetValueOfString(dr["Doc_Status_Code"]);
                    decimal qtyOrdered = dr["Qty_Ordered"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Qty_Ordered"]);
                    decimal qtyDelivered = dr["Qty_Delivered"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Qty_Delivered"]);

                    string deliveryCode, deliveryFallback, deliveryStatusCode;
                    if (qtyOrdered > 0 && qtyDelivered >= qtyOrdered) { deliveryCode = "VAS_285_DeliveryFull"; deliveryFallback = "Delivered"; deliveryStatusCode = "FULL"; }
                    else if (qtyDelivered > 0) { deliveryCode = "VAS_285_DeliveryPartial"; deliveryFallback = "Partially delivered"; deliveryStatusCode = "PARTIAL"; }
                    else { deliveryCode = "VAS_285_DeliveryNone"; deliveryFallback = "Not delivered"; deliveryStatusCode = "PENDING"; }

                    result.Rows.Add(new OrderRow
                    {
                        SalesOrderId = Util.GetValueOfInt(dr["Order_Id"]),
                        SalesOrderNumber = Util.GetValueOfString(dr["Document_No"]),
                        SalesOrderDate = dateOrdered.HasValue ? dateOrdered.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                        CustomerName = Util.GetValueOfString(dr["Customer_Name"]),
                        WarehouseName = Util.GetValueOfString(dr["Warehouse_Name"]),
                        RepresentativeName = Util.GetValueOfString(dr["Representative_Name"]),
                        OrderValue = dr["Order_Value"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Order_Value"]),
                        DocumentStatus = DecodeLabel(docStatusMap, docStatusCode),
                        DeliveryStatus = Msg.GetMsg(ctx, deliveryCode) ?? deliveryFallback,
                        DeliveryStatusCode = deliveryStatusCode
                    });
                }
                result.Total = total;
            }
            finally
            {
                CloseReader(dr);
            }

            return result;
        }

        /// <summary>
        /// Current free stock for one product across every warehouse: QtyOnHand -
        /// QtyReserved - QtyDedicated - QtyAllocated, summed over active locators - the
        /// exact same formula as VAS_280's ResolveFreeStock, just without a warehouse
        /// filter (this widget is not warehouse-scoped).
        /// </summary>
        private decimal ResolveFreeStock(Ctx ctx, int productId)
        {
            if (productId <= 0) { return 0m; }

            string sql = @"
                SELECT COALESCE(SUM(COALESCE(s.QtyOnHand, 0) - COALESCE(s.QtyReserved, 0)
                              - COALESCE(s.QtyDedicated, 0) - COALESCE(s.QtyAllocated, 0)), 0) AS Free_Stock
                  FROM M_Storage s
                  INNER JOIN M_Locator loc ON ( loc.M_Locator_ID = s.M_Locator_ID AND loc.IsActive = 'Y' )
                 WHERE s.M_Product_ID = @M_Product_ID";

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "s", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            return Util.GetValueOfDecimal(DB.ExecuteScalar(sql, new[]
            {
                new SqlParameter("@M_Product_ID", productId)
            }, null));
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
            if (hasLines && allDelivered) { deliveryStatusKey = "VAS_285_DeliveryFull"; deliveryStatusFallback = "Delivered"; }
            else if (anyDelivered) { deliveryStatusKey = "VAS_285_DeliveryPartial"; deliveryStatusFallback = "Partially delivered"; }
            else { deliveryStatusKey = "VAS_285_DeliveryNone"; deliveryStatusFallback = "Not delivered"; }
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
                        FreeStock = ResolveLineFreeStock(ctx, productId, warehouseId),
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
        /// Warehouse-level free stock for one product (used by the shared line table,
        /// which is scoped to the order's ship-from warehouse) - QtyOnHand -
        /// QtyReserved - QtyDedicated - QtyAllocated, summed across the warehouse's
        /// active locators.
        /// </summary>
        private decimal ResolveLineFreeStock(Ctx ctx, int productId, int warehouseId)
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
                    Log.Log(Level.SEVERE, "VAS_285_TopProductsWidget.GetDecodeMap(" + tableName + "." + columnName + ")", ex);
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

        private class TopProductsResult
        {
            public List<TopProductRow> Products { get; set; }
        }

        private class TopProductRow
        {
            public int ProductId { get; set; }
            public string Sku { get; set; }
            public string ProductName { get; set; }
            public decimal OrderValue { get; set; }
            public int Rank { get; set; }
        }

        private class ProductDemandResult
        {
            public string ProductName { get; set; }
            public string Sku { get; set; }
            public string UomName { get; set; }
            public decimal OrderValue { get; set; }
            public decimal OpenQty { get; set; }
            public decimal FreeStock { get; set; }
            public decimal? CoveragePercent { get; set; }
        }

        private class ProductOrdersResult
        {
            public int Total { get; set; }
            public List<OrderRow> Rows { get; set; }
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
