/******************************************************
 * Module Name    : VAS
 * Purpose        : Sales Order dashboard "Monthly Sales Order Trend" column-chart widget endpoints
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
    /// Module Name : VAS_277_MonthlySalesOrderTrendWidget
    /// Purpose     : Data endpoints for the 4x2 "Monthly Sales Order Trend" column-chart
    ///               widget on the Sales Order dashboard - booked Sales Order value by
    ///               month, over a user-controlled but hard-capped 12-month range. (1)
    ///               the monthly series for the chart (missing months filled with an
    ///               explicit zero here in service code, never a SQL calendar-series
    ///               trick), (2) a bar-click month drill-down (summary stat-strip
    ///               figures + paginated order list, same shape as VAS_273's table),
    ///               and (3) the shared Sales Order record-preview data (header stats +
    ///               paginated lines with per-line free stock) reused for both the
    ///               "record" and "lines" child modals opened from a row in that list.
    ///
    ///   Business definition per
    ///   10_Monthly_Sales_Order_Trend_Claude_Development_Prompt.txt: uses the EXACT
    ///   same booked-order/value rule as VAS_273 Order Value Booked MTD - active real
    ///   Sales Order, IsSOTrx='Y', non-return, non-quotation, DocStatus IN ('CO','CL')
    ///   (Drafted/In-Process excluded), value = C_Order.TotalLines pre-tax, month =
    ///   C_Order.DateOrdered's calendar month (grouped via EXTRACT for portable
    ///   year/month keys, never a formatted string). Forward-dated months are
    ///   legitimate and are never filtered out. The 12-month span cap is enforced here
    ///   again server-side - the client's own clamp is never trusted alone.
    ///
    ///   Per the CTE/MRole rule (Prompt_Instructions.txt "Case 1"): GetSeries and
    ///   GetMonthSummary are single "FROM C_Order o" queries with no CTEs at all, so
    ///   AddAccessSQL applies directly to them (the same simple pattern VAS_268's own
    ///   header/recent-order queries already prove safe). GetMonthOrders adds a
    ///   secondary "line_stats" CTE over C_OrderLine, so - matching the fix already
    ///   applied to VAS_270-276 after VAS_270 hit ORA-00904 - AddAccessSQL is applied
    ///   ONLY to the isolated "base_orders" fragment there before it is embedded into
    ///   the combined statement, never to that combined statement itself.
    ///   Read-only here; no writes.
    /// Chronological development:
    ///   VAI052      2026-09-14 Created
    /// </summary>
    public class VAS_277_MonthlySalesOrderTrendWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_277_MonthlySalesOrderTrendWidgetController).FullName);

        private const int DefaultPageSize = 10;
        private const int MaxPageSize = 10;
        private const int MaxLineRows = 200;
        private const int MaxSpanMonths = 12;

        /// <summary>
        /// The monthly booked-value/count series for the chart, over [fromIndex,
        /// toIndex] inclusive (monthIndex = year * 12 + zero-based month). The span is
        /// clamped to <see cref="MaxSpanMonths"/> server-side regardless of what the
        /// client sent. Months with no orders are returned as explicit zero entries -
        /// never silently omitted.
        /// </summary>
        /// <param name="fromIndex">First month's monthIndex (year * 12 + zero-based month).</param>
        /// <param name="toIndex">Last month's monthIndex, inclusive.</param>
        /// <returns>JSON { FromIndex, ToIndex, Months:[ { MonthIndex, Value, OrderCount } ] } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetSeries(int fromIndex, int toIndex)
        {
            if (Session["ctx"] == null) { return Json("", JsonRequestBehavior.AllowGet); }

            Ctx ctx = Session["ctx"] as Ctx;

            try
            {
                string json = JsonConvert.SerializeObject(GetSeriesData(ctx, fromIndex, toIndex));
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_277_MonthlySalesOrderTrendWidget.GetSeries", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// The bar-click drill-down for one month: the 4-card summary plus one page of
        /// that month's booked orders, combined in a single round trip.
        /// </summary>
        /// <param name="monthIndex">year * 12 + zero-based month.</param>
        /// <param name="page">Zero-based page index.</param>
        /// <param name="size">Rows per page (capped at <see cref="MaxPageSize"/>).</param>
        /// <returns>JSON { Summary:{...}, Total, Rows:[...] } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetMonthOrders(int monthIndex, int page = 0, int size = DefaultPageSize)
        {
            if (Session["ctx"] == null) { return Json("", JsonRequestBehavior.AllowGet); }

            Ctx ctx = Session["ctx"] as Ctx;
            if (page < 0) { page = 0; }
            if (size <= 0 || size > MaxPageSize) { size = DefaultPageSize; }

            try
            {
                string json = JsonConvert.SerializeObject(GetMonthOrdersData(ctx, monthIndex, page, size));
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_277_MonthlySalesOrderTrendWidget.GetMonthOrders", ex);
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
                Log.Log(Level.SEVERE, "VAS_277_MonthlySalesOrderTrendWidget.GetSalesOrderDetail", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>monthIndex -&gt; inclusive first-of-month date.</summary>
        private static DateTime MonthIndexToDate(int monthIndex)
        {
            int year = monthIndex / 12;
            int month = monthIndex % 12; // zero-based
            return new DateTime(year, month + 1, 1);
        }

        /// <summary>The exact-same booked Sales Order cohort filter as VAS_273 - a single "FROM C_Order o", no CTEs.</summary>
        private static string BuildBaseOrdersSql()
        {
            return @"
                SELECT o.C_Order_ID AS C_Order_ID,
                       o.DocumentNo AS Document_No,
                       o.DateOrdered AS Date_Ordered,
                       o.SalesRep_ID AS Sales_Rep_Id,
                       o.TotalLines AS Order_Value,
                       o.DocStatus AS Doc_Status_Code,
                       o.C_BPartner_ID AS Bpartner_Id
                  FROM C_Order o
                 WHERE o.AD_Client_ID = @AD_Client_ID
                   AND o.IsActive = 'Y'
                   AND o.IsSOTrx = 'Y'
                   AND COALESCE(o.IsReturnTrx, 'N') = 'N'
                   AND COALESCE(o.IsSalesQuotation, 'N') = 'N'
                   AND o.DocStatus IN ('CO', 'CL')
                   AND o.DateOrdered >= @Month_Start
                   AND o.DateOrdered < @Month_End";
        }

        private SeriesResult GetSeriesData(Ctx ctx, int fromIndex, int toIndex)
        {
            SeriesResult result = new SeriesResult { Months = new List<MonthPoint>() };
            if (ctx == null) { return result; }

            if (toIndex < fromIndex) { toIndex = fromIndex; }
            // The 12-month span cap, enforced again here - the client's own clamp is
            // never trusted alone (Prompt_Instructions "do not trust the client").
            if (toIndex - fromIndex + 1 > MaxSpanMonths) { toIndex = fromIndex + MaxSpanMonths - 1; }

            result.FromIndex = fromIndex;
            result.ToIndex = toIndex;

            DateTime fromDate = MonthIndexToDate(fromIndex);
            DateTime toDate = MonthIndexToDate(toIndex + 1);

            // The isolated single-FROM, WHERE-only fragment AddAccessSQL is applied to
            // (Prompt_Instructions.txt "Case 1"). This must never carry its own
            // trailing GROUP BY: AddAccessSQL appends the access predicate after the
            // fragment's own WHERE conditions, and a GROUP BY sitting after that point
            // pushes the appended predicate past it, producing invalid trailing SQL
            // (ORA-00933 "SQL command not properly ended") - the aggregation always
            // belongs in the outer query built around the already-filtered fragment,
            // exactly like every other widget's base_orders/CTE pattern this session.
            string baseOrdersSql = @"
                SELECT o.DateOrdered AS Date_Ordered,
                       o.TotalLines AS Order_Value
                  FROM C_Order o
                 WHERE o.AD_Client_ID = @AD_Client_ID
                   AND o.IsActive = 'Y'
                   AND o.IsSOTrx = 'Y'
                   AND COALESCE(o.IsReturnTrx, 'N') = 'N'
                   AND COALESCE(o.IsSalesQuotation, 'N') = 'N'
                   AND o.DocStatus IN ('CO', 'CL')
                   AND o.DateOrdered >= @From_Date
                   AND o.DateOrdered < @To_Date";

            baseOrdersSql = MRole.GetDefault(ctx).AddAccessSQL(baseOrdersSql, "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string sql = @"
                WITH base_orders AS (" + baseOrdersSql + @")
                SELECT
                    EXTRACT(YEAR FROM bo.Date_Ordered) AS Year_Num,
                    EXTRACT(MONTH FROM bo.Date_Ordered) AS Month_Num,
                    SUM(COALESCE(bo.Order_Value, 0)) AS Order_Value,
                    COUNT(1) AS Order_Count
                  FROM base_orders bo
                 GROUP BY
                    EXTRACT(YEAR FROM bo.Date_Ordered),
                    EXTRACT(MONTH FROM bo.Date_Ordered)";

            Dictionary<int, MonthPoint> byIndex = new Dictionary<int, MonthPoint>();

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, new[]
                {
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                    new SqlParameter("@From_Date", fromDate),
                    new SqlParameter("@To_Date", toDate)
                });

                while (dr != null && dr.Read())
                {
                    int yearNum = Util.GetValueOfInt(dr["Year_Num"]);
                    int monthNum = Util.GetValueOfInt(dr["Month_Num"]); // 1-based from EXTRACT
                    int monthIndex = yearNum * 12 + (monthNum - 1);

                    byIndex[monthIndex] = new MonthPoint
                    {
                        MonthIndex = monthIndex,
                        Value = dr["Order_Value"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Order_Value"]),
                        OrderCount = dr["Order_Count"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Order_Count"])
                    };
                }
            }
            finally
            {
                CloseReader(dr);
            }

            // Fill every missing month inside the selected range with an explicit zero
            // entry here in service code - never a SQL calendar-series trick
            // (Prompt_Instructions "do not make SQL dialect-specific").
            for (int idx = fromIndex; idx <= toIndex; idx++)
            {
                MonthPoint point;
                if (!byIndex.TryGetValue(idx, out point))
                {
                    point = new MonthPoint { MonthIndex = idx, Value = 0m, OrderCount = 0 };
                }
                result.Months.Add(point);
            }

            return result;
        }

        private MonthOrdersResult GetMonthOrdersData(Ctx ctx, int monthIndex, int page, int size)
        {
            MonthOrdersResult result = new MonthOrdersResult { Summary = new MonthSummary(), Rows = new List<OrderRow>() };
            if (ctx == null) { return result; }

            DateTime monthStart = MonthIndexToDate(monthIndex);
            DateTime monthEnd = MonthIndexToDate(monthIndex + 1);

            string language = GetLanguage(ctx);
            Dictionary<string, string> docStatusMap = GetDecodeMap("C_Order", "DocStatus", language);

            // The only AddAccessSQL call for this statement - applied to the isolated
            // single-WHERE base_orders body BEFORE it is embedded below.
            string baseOrdersSql = MRole.GetDefault(ctx).AddAccessSQL(BuildBaseOrdersSql(), "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string summarySql = @"
                WITH base_orders AS (" + baseOrdersSql + @")
                SELECT
                    COUNT(1) AS Order_Count,
                    SUM(COALESCE(Order_Value, 0)) AS Order_Value,
                    COUNT(DISTINCT Bpartner_Id) AS Customer_Count,
                    ROUND(AVG(COALESCE(Order_Value, 0)), 2) AS Avg_Order_Value
                  FROM base_orders";

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(summarySql, new[]
                {
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                    new SqlParameter("@Month_Start", monthStart),
                    new SqlParameter("@Month_End", monthEnd)
                });

                if (dr != null && dr.Read())
                {
                    result.Summary.OrderCount = dr["Order_Count"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Order_Count"]);
                    result.Summary.OrderValue = dr["Order_Value"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Order_Value"]);
                    result.Summary.CustomerCount = dr["Customer_Count"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Customer_Count"]);
                    result.Summary.AvgOrderValue = dr["Avg_Order_Value"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Avg_Order_Value"]);
                }
            }
            finally
            {
                CloseReader(dr);
            }

            // A second, independent AddAccessSQL pass (fresh base_orders text) for the
            // paginated list query, matching every other widget's per-query pattern.
            string baseOrdersSql2 = MRole.GetDefault(ctx).AddAccessSQL(BuildBaseOrdersSql(), "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string listSql = @"
                WITH base_orders AS (" + baseOrdersSql2 + @"),
                line_stats AS (
                    SELECT
                        ol.C_Order_ID AS C_Order_ID,
                        COUNT(1) AS Line_Count,
                        SUM(COALESCE(ol.QtyOrdered, 0)) AS Order_Qty
                      FROM C_OrderLine ol
                     WHERE ol.IsActive = 'Y'
                     GROUP BY ol.C_Order_ID
                )
                SELECT
                    bo.C_Order_ID AS Order_Id,
                    bo.Document_No AS Document_No,
                    bo.Date_Ordered AS Date_Ordered,
                    COALESCE(bp.Name, N'') AS Customer_Name,
                    COALESCE(rep.Name, N'') AS Representative_Name,
                    COALESCE(ls.Line_Count, 0) AS Line_Count,
                    COALESCE(ls.Order_Qty, 0) AS Order_Qty,
                    bo.Order_Value AS Order_Value,
                    bo.Doc_Status_Code AS Doc_Status_Code,
                    COUNT(1) OVER () AS Total_Rows
                  FROM base_orders bo
                  INNER JOIN C_BPartner bp ON ( bp.C_BPartner_ID = bo.Bpartner_Id )
                  LEFT OUTER JOIN AD_User rep ON ( rep.AD_User_ID = bo.Sales_Rep_Id )
                  LEFT OUTER JOIN line_stats ls ON ( ls.C_Order_ID = bo.C_Order_ID )
                 ORDER BY bo.Date_Ordered DESC, bo.Document_No DESC
                 OFFSET @Row_Offset ROWS FETCH NEXT @Page_Size ROWS ONLY";

            IDataReader dr2 = null;
            try
            {
                dr2 = DB.ExecuteReader(listSql, new[]
                {
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                    new SqlParameter("@Month_Start", monthStart),
                    new SqlParameter("@Month_End", monthEnd),
                    new SqlParameter("@Row_Offset", page * size),
                    new SqlParameter("@Page_Size", size)
                });

                int total = 0;
                while (dr2 != null && dr2.Read())
                {
                    total = dr2["Total_Rows"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr2["Total_Rows"]);
                    DateTime? dateOrdered = dr2["Date_Ordered"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr2["Date_Ordered"]);
                    string docStatusCode = Util.GetValueOfString(dr2["Doc_Status_Code"]);

                    result.Rows.Add(new OrderRow
                    {
                        SalesOrderId = Util.GetValueOfInt(dr2["Order_Id"]),
                        SalesOrderNumber = Util.GetValueOfString(dr2["Document_No"]),
                        SalesOrderDate = dateOrdered.HasValue ? dateOrdered.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                        CustomerName = Util.GetValueOfString(dr2["Customer_Name"]),
                        RepresentativeName = Util.GetValueOfString(dr2["Representative_Name"]),
                        LineCount = dr2["Line_Count"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr2["Line_Count"]),
                        OrderQty = dr2["Order_Qty"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr2["Order_Qty"]),
                        OrderValue = dr2["Order_Value"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr2["Order_Value"]),
                        DocumentStatus = DecodeLabel(docStatusMap, docStatusCode)
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
                deliveryStatusKey = "VAS_277_DeliveryFull"; deliveryStatusFallback = "Delivered";
            }
            else if (anyDelivered)
            {
                deliveryStatusKey = "VAS_277_DeliveryPartial"; deliveryStatusFallback = "Partially delivered";
            }
            else
            {
                deliveryStatusKey = "VAS_277_DeliveryNone"; deliveryStatusFallback = "Not delivered";
            }
            result.Order.DeliveryStatus = Msg.GetMsg(ctx, deliveryStatusKey) ?? deliveryStatusFallback;

            return result;
        }

        /// <summary>
        /// Every active line of the order, each carrying its own free-stock figure
        /// (per <see cref="ResolveFreeStock"/>) and a derived line-status chip.
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
                    Log.Log(Level.SEVERE, "VAS_277_MonthlySalesOrderTrendWidget.GetDecodeMap(" + tableName + "." + columnName + ")", ex);
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

        private class SeriesResult
        {
            public int FromIndex { get; set; }
            public int ToIndex { get; set; }
            public List<MonthPoint> Months { get; set; }
        }

        private class MonthPoint
        {
            public int MonthIndex { get; set; }
            public decimal Value { get; set; }
            public int OrderCount { get; set; }
        }

        private class MonthOrdersResult
        {
            public MonthSummary Summary { get; set; }
            public int Total { get; set; }
            public List<OrderRow> Rows { get; set; }
        }

        private class MonthSummary
        {
            public int OrderCount { get; set; }
            public decimal OrderValue { get; set; }
            public int CustomerCount { get; set; }
            public decimal AvgOrderValue { get; set; }
        }

        private class OrderRow
        {
            public int SalesOrderId { get; set; }
            public string SalesOrderNumber { get; set; }
            public string SalesOrderDate { get; set; }
            public string CustomerName { get; set; }
            public string RepresentativeName { get; set; }
            public int LineCount { get; set; }
            public decimal OrderQty { get; set; }
            public decimal OrderValue { get; set; }
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
