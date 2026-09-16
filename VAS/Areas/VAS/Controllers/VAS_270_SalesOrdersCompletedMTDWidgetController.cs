/******************************************************
 * Module Name    : VAS
 * Purpose        : Sales Order dashboard "Sales Orders Completed MTD" KPI widget endpoints
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
    /// Module Name : VAS_270_SalesOrdersCompletedMTDWidget
    /// Purpose     : Data endpoints for the 2x1 "Sales Orders Completed MTD" KPI tile on
    ///               the Sales Order dashboard. (1) the tile/summary figures - completed
    ///               count, delivered-order count, delivered value and on-time-shipment
    ///               percentage for month-to-date (DateOrdered from the 1st of the current
    ///               month through today), (2) the drill-down modal's paginated documents
    ///               list, and (3) the shared Sales Order record-preview data (header
    ///               stats + paginated lines with per-line free stock) reused for both the
    ///               "record" and "lines" child modals.
    ///
    ///   Business definition per 03_Sales_Orders_Completed_MTD_Claude_Development_Prompt.txt
    ///   "CONFIRMED BUSINESS DEFINITION - OVERRIDES THE ORIGINAL DRAFT" (the paired .md's
    ///   Completed/Closed split, write-off note and period selector are NOT implemented):
    ///     - Headline = C_Order.DocStatus = 'CO' only (Closed 'CL' excluded), date-filtered
    ///       on DateOrdered, regardless of delivery state.
    ///     - Delivered value = SUM(QtyDelivered * PriceActual) over active lines with
    ///       QtyDelivered > 0, for that same CO/MTD cohort only (never TotalLines).
    ///     - On-time shipment = last completed outbound M_InOut (IsSOTrx='Y',
    ///       IsReturnTrx='N', DocStatus='CO') MovementDate <= C_Order.DatePromised, over
    ///       the subset of the cohort that has both a completed shipment and a
    ///       DatePromised; a zero denominator is surfaced as a null percentage (client
    ///       renders "-").
    ///
    ///   Per the CTE/MRole rule (Prompt_Instructions.txt "Case 1"), AddAccessSQL is
    ///   applied ONLY to the isolated "base_orders" CTE body (BuildBaseOrdersSql) - a
    ///   single SELECT against the one main physical table (C_Order "o") with its own
    ///   single WHERE clause - before it is embedded into the combined multi-CTE
    ///   statement. It is never applied to that combined statement itself, nor to the
    ///   line_delivery/last_shipment/line_stats CTE aliases, which are secondary
    ///   aggregates over C_OrderLine/M_InOut joined only to enrich each row (mirroring
    ///   the C_InvoiceA/C_InvoiceB example - primary table only).
    ///   Read-only here; no writes.
    /// Chronological development:
    ///   VAI052      2026-09-14 Created
    /// </summary>
    public class VAS_270_SalesOrdersCompletedMTDWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_270_SalesOrdersCompletedMTDWidgetController).FullName);

        private const int DefaultPageSize = 10;
        private const int MaxPageSize = 10;
        private const int MaxLineRows = 200;

        /// <summary>
        /// Month-to-date summary figures for the tile and the modal's stat strip.
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
                Log.Log(Level.SEVERE, "VAS_270_SalesOrdersCompletedMTDWidget.GetSummary", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// One page of the underlying MTD-completed sales orders for the modal's
        /// "Documents" table.
        /// </summary>
        /// <param name="page">Zero-based page index.</param>
        /// <param name="size">Rows per page (capped at <see cref="MaxPageSize"/>).</param>
        /// <returns>JSON { Total, Rows:[...] } or { Error }.</returns>
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
                Log.Log(Level.SEVERE, "VAS_270_SalesOrdersCompletedMTDWidget.GetOrders", ex);
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
                Log.Log(Level.SEVERE, "VAS_270_SalesOrdersCompletedMTDWidget.GetSalesOrderDetail", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>Inclusive first day of the current month, for the MTD cohort's lower bound.</summary>
        private static DateTime PeriodStart()
        {
            DateTime today = DateTime.Today;
            return new DateTime(today.Year, today.Month, 1);
        }

        /// <summary>Exclusive upper bound - the day after today - so DateOrdered &lt; this value covers today in full.</summary>
        private static DateTime PeriodEnd()
        {
            return DateTime.Today.AddDays(1);
        }

        /// <summary>
        /// The base_orders CTE body - the ONLY fragment AddAccessSQL is ever applied to
        /// (Prompt_Instructions.txt "Case 1"): a single SELECT against the one main
        /// physical table (C_Order "o") with its own single WHERE clause, scoped to the
        /// MTD-completed cohort. GetSummaryData/GetOrdersData each embed this
        /// ALREADY-ACCESS-FILTERED text as their own "base_orders" CTE and must never
        /// call AddAccessSQL again on the combined multi-CTE statement built around it -
        /// doing so against a string with more than one WHERE clause (line_delivery/
        /// last_shipment/line_stats each have their own) risks the filter landing on the
        /// wrong WHERE.
        /// </summary>
        private static string BuildBaseOrdersSql()
        {
            return @"
                SELECT o.C_Order_ID AS C_Order_ID,
                       o.DocumentNo AS Document_No,
                       o.DateOrdered AS Date_Ordered,
                       o.DatePromised AS Date_Promised,
                       o.DocStatus AS Doc_Status_Code,
                       o.TotalLines AS Order_Value,
                       o.C_BPartner_ID AS Bpartner_Id,
                       o.M_Warehouse_ID AS Warehouse_Id,
                       o.SalesRep_ID AS Sales_Rep_Id
                  FROM C_Order o
                 WHERE o.AD_Client_ID = @AD_Client_ID
                   AND o.IsActive = 'Y'
                   AND o.IsSOTrx = 'Y'
                   AND COALESCE(o.IsReturnTrx, 'N') = 'N'
                   AND COALESCE(o.IsSalesQuotation, 'N') = 'N'
                   AND o.DocStatus = 'CO'
                   AND o.DateOrdered >= @Period_Start
                   AND o.DateOrdered < @Period_End";
        }

        private SummaryResult GetSummaryData(Ctx ctx)
        {
            DateTime periodStart = PeriodStart();
            DateTime periodEnd = PeriodEnd();

            SummaryResult result = new SummaryResult
            {
                PeriodStart = periodStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                PeriodEnd = periodEnd.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            };
            if (ctx == null) { return result; }

            // The only AddAccessSQL call for this statement - applied to the isolated
            // single-WHERE base_orders body BEFORE it is embedded below, per the class
            // summary and BuildBaseOrdersSql's own remarks.
            string baseOrdersSql = MRole.GetDefault(ctx).AddAccessSQL(BuildBaseOrdersSql(), "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string sql = @"
                WITH base_orders AS (" + baseOrdersSql + @"),
                line_delivery AS (
                    SELECT
                        ol.C_Order_ID AS C_Order_ID,
                        SUM(
                            CASE
                                WHEN COALESCE(ol.QtyDelivered, 0) > 0
                                THEN COALESCE(ol.QtyDelivered, 0) * COALESCE(ol.PriceActual, 0)
                                ELSE 0
                            END
                        ) AS Delivered_Value,
                        MAX(
                            CASE WHEN COALESCE(ol.QtyDelivered, 0) > 0 THEN 1 ELSE 0 END
                        ) AS Has_Delivery
                      FROM C_OrderLine ol
                     WHERE ol.IsActive = 'Y'
                     GROUP BY ol.C_Order_ID
                ),
                last_shipment AS (
                    SELECT
                        io.C_Order_ID AS C_Order_ID,
                        MAX(io.MovementDate) AS Last_Shipment_Date
                      FROM M_InOut io
                     WHERE io.IsActive = 'Y'
                       AND io.IsSOTrx = 'Y'
                       AND COALESCE(io.IsReturnTrx, 'N') = 'N'
                       AND io.DocStatus = 'CO'
                     GROUP BY io.C_Order_ID
                )
                SELECT
                    COUNT(1) AS Completed_Count,
                    SUM(COALESCE(ld.Has_Delivery, 0)) AS Delivered_Order_Count,
                    SUM(COALESCE(ld.Delivered_Value, 0)) AS Delivered_Value,
                    SUM(
                        CASE
                            WHEN ls.Last_Shipment_Date IS NOT NULL
                             AND o.Date_Promised IS NOT NULL
                             AND ls.Last_Shipment_Date <= o.Date_Promised
                            THEN 1 ELSE 0
                        END
                    ) AS On_Time_Count,
                    SUM(
                        CASE
                            WHEN ls.Last_Shipment_Date IS NOT NULL
                             AND o.Date_Promised IS NOT NULL
                            THEN 1 ELSE 0
                        END
                    ) AS On_Time_Denominator
                  FROM base_orders o
                  LEFT OUTER JOIN line_delivery ld ON ( ld.C_Order_ID = o.C_Order_ID )
                  LEFT OUTER JOIN last_shipment ls ON ( ls.C_Order_ID = o.C_Order_ID )";

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, new[]
                {
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                    new SqlParameter("@Period_Start", periodStart),
                    new SqlParameter("@Period_End", periodEnd)
                });

                if (dr != null && dr.Read())
                {
                    result.CompletedCount = dr["Completed_Count"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Completed_Count"]);
                    result.DeliveredOrderCount = dr["Delivered_Order_Count"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Delivered_Order_Count"]);
                    result.DeliveredValue = dr["Delivered_Value"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Delivered_Value"]);

                    int onTimeDenominator = dr["On_Time_Denominator"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["On_Time_Denominator"]);
                    if (onTimeDenominator > 0)
                    {
                        int onTimeCount = dr["On_Time_Count"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["On_Time_Count"]);
                        result.OnTimePercent = (int)Math.Round(100m * onTimeCount / onTimeDenominator, MidpointRounding.AwayFromZero);
                    }
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

            string language = GetLanguage(ctx);
            Dictionary<string, string> docStatusMap = GetDecodeMap("C_Order", "DocStatus", language);

            DateTime periodStart = PeriodStart();
            DateTime periodEnd = PeriodEnd();

            // Same isolated-fragment AddAccessSQL call as GetSummaryData - never applied
            // to the combined statement below, which has its own second WHERE inside
            // line_stats.
            string baseOrdersSql = MRole.GetDefault(ctx).AddAccessSQL(BuildBaseOrdersSql(), "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string sql = @"
                WITH base_orders AS (" + baseOrdersSql + @"),
                line_stats AS (
                    SELECT
                        ol.C_Order_ID AS C_Order_ID,
                        SUM(COALESCE(ol.QtyOrdered, 0)) AS Ordered_Qty,
                        SUM(COALESCE(ol.QtyDelivered, 0)) AS Delivered_Qty
                      FROM C_OrderLine ol
                     WHERE ol.IsActive = 'Y'
                     GROUP BY ol.C_Order_ID
                )
                SELECT
                    o.C_Order_ID AS Order_Id,
                    o.Document_No AS Document_No,
                    o.Date_Ordered AS Date_Ordered,
                    o.Doc_Status_Code AS Doc_Status_Code,
                    o.Order_Value AS Order_Value,
                    COALESCE(bp.Name, N'') AS Customer_Name,
                    COALESCE(w.Name, N'') AS Warehouse_Name,
                    COALESCE(rep.Name, N'') AS Representative_Name,
                    COALESCE(ls.Ordered_Qty, 0) AS Ordered_Qty,
                    COALESCE(ls.Delivered_Qty, 0) AS Delivered_Qty,
                    COUNT(1) OVER () AS Total_Rows
                  FROM base_orders o
                  INNER JOIN C_BPartner bp ON ( bp.C_BPartner_ID = o.Bpartner_Id )
                  LEFT OUTER JOIN M_Warehouse w ON ( w.M_Warehouse_ID = o.Warehouse_Id )
                  LEFT OUTER JOIN AD_User rep ON ( rep.AD_User_ID = o.Sales_Rep_Id )
                  LEFT OUTER JOIN line_stats ls ON ( ls.C_Order_ID = o.C_Order_ID )
                 ORDER BY o.Date_Ordered DESC, o.Document_No DESC
                 OFFSET @Row_Offset ROWS FETCH NEXT @Page_Size ROWS ONLY";

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, new[]
                {
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                    new SqlParameter("@Period_Start", periodStart),
                    new SqlParameter("@Period_End", periodEnd),
                    new SqlParameter("@Row_Offset", page * size),
                    new SqlParameter("@Page_Size", size)
                });

                int total = 0;
                while (dr != null && dr.Read())
                {
                    total = dr["Total_Rows"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Total_Rows"]);

                    decimal orderedQty = dr["Ordered_Qty"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Ordered_Qty"]);
                    decimal deliveredQty = dr["Delivered_Qty"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Delivered_Qty"]);
                    DateTime? dateOrdered = dr["Date_Ordered"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr["Date_Ordered"]);

                    string deliveryStatusKey, deliveryStatusFallback;
                    if (deliveredQty <= 0)
                    {
                        deliveryStatusKey = "VAS_270_DeliveryNone"; deliveryStatusFallback = "Not delivered";
                    }
                    else if (deliveredQty >= orderedQty)
                    {
                        deliveryStatusKey = "VAS_270_DeliveryFull"; deliveryStatusFallback = "Delivered";
                    }
                    else
                    {
                        deliveryStatusKey = "VAS_270_DeliveryPartial"; deliveryStatusFallback = "Partially delivered";
                    }

                    result.Rows.Add(new OrderRow
                    {
                        SalesOrderId = Util.GetValueOfInt(dr["Order_Id"]),
                        SalesOrderNumber = Util.GetValueOfString(dr["Document_No"]),
                        SalesOrderDate = dateOrdered.HasValue ? dateOrdered.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                        CustomerName = Util.GetValueOfString(dr["Customer_Name"]),
                        WarehouseName = Util.GetValueOfString(dr["Warehouse_Name"]),
                        RepresentativeName = Util.GetValueOfString(dr["Representative_Name"]),
                        OrderValue = dr["Order_Value"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Order_Value"]),
                        DeliveryStatus = Msg.GetMsg(ctx, deliveryStatusKey) ?? deliveryStatusFallback,
                        DocumentStatus = DecodeLabel(docStatusMap, Util.GetValueOfString(dr["Doc_Status_Code"]))
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
                deliveryStatusKey = "VAS_270_DeliveryFull"; deliveryStatusFallback = "Delivered";
            }
            else if (anyDelivered)
            {
                deliveryStatusKey = "VAS_270_DeliveryPartial"; deliveryStatusFallback = "Partially delivered";
            }
            else
            {
                deliveryStatusKey = "VAS_270_DeliveryNone"; deliveryStatusFallback = "Not delivered";
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
                    Log.Log(Level.SEVERE, "VAS_270_SalesOrdersCompletedMTDWidget.GetDecodeMap(" + tableName + "." + columnName + ")", ex);
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
            public int CompletedCount { get; set; }
            public int DeliveredOrderCount { get; set; }
            public decimal DeliveredValue { get; set; }
            public int? OnTimePercent { get; set; }
            public string PeriodStart { get; set; }
            public string PeriodEnd { get; set; }
        }

        private class OrdersResult
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
            public string DeliveryStatus { get; set; }
            public string DocumentStatus { get; set; }
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
