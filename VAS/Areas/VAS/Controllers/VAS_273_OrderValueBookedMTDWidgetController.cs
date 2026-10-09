/******************************************************
 * Module Name    : VAS
 * Purpose        : Sales Order dashboard "Order Value Booked MTD" KPI widget endpoints
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
    /// Module Name : VAS_273_OrderValueBookedMTDWidget
    /// Purpose     : Data endpoints for the 2x1 "Order Value Booked MTD" KPI tile on
    ///               the Sales Order dashboard - the commercial top line / reconciliation
    ///               anchor for every other value widget. (1) the tile/summary figures -
    ///               order value, order count, average order value, a signed
    ///               month-over-month comparison against an EQUIVALENT elapsed window in
    ///               the previous month, customers billed / new, and the
    ///               quotation-vs-direct origin split, and (2) the drill-down modal's
    ///               paginated documents list (newest first), and (3) the shared Sales
    ///               Order record-preview data (header stats + paginated lines with
    ///               per-line free stock) reused for both the "record" and "lines" child
    ///               modals.
    ///
    ///   Business definition per
    ///   06_Order_Value_Booked_MTD_Claude_Development_Prompt.txt:
    ///     - Booked = real Sales Order (not quotation), active, IsSOTrx='Y', non-return,
    ///       DocStatus IN ('CO','CL') - Drafted/In-Process are explicitly excluded and
    ///       stay in the VAS_272 widget only. DateOrdered drives the MTD window.
    ///     - Value = SUM(C_Order.TotalLines), pre-tax - the same definition the Monthly
    ///       Sales Order Trend widget must use so the dashboard reconciles.
    ///     - Month-over-month comparison uses an EQUIVALENT elapsed window in the
    ///       previous month (same number of calendar days from the 1st, clamped to that
    ///       month's length) - never a partial month against a full one. A zero-value
    ///       previous window omits the percentage entirely rather than showing a
    ///       misleading +0%/infinite figure.
    ///     - New customer = C_BPartner.FirstSale falls within the current MTD window.
    ///     - Origin split = at least one active C_OrderLine with C_Quotation_Line_ID NOT
    ///       NULL -&gt; "From quotation", else "Direct order"; never guessed from
    ///       customer/date.
    ///
    ///   Per the CTE/MRole rule (Prompt_Instructions.txt "Case 1"), AddAccessSQL is
    ///   applied ONLY to the isolated "base_orders" CTE body (BuildBaseOrdersSql) - a
    ///   single SELECT against the one main physical table (C_Order "o") with its own
    ///   single WHERE clause - before it is embedded into the combined statements used by
    ///   the summary (which also has a secondary "order_line_stats" CTE over
    ///   C_OrderLine) and the modal list (secondary "line_stats" CTE). The standalone
    ///   previous-month value query has only one FROM/WHERE to begin with, so
    ///   AddAccessSQL is applied directly to it, matching VAS_268's simple-query
    ///   pattern. AddAccessSQL is never applied to either combined multi-FROM statement
    ///   itself (mirroring the fix already applied to VAS_270/271/272 after VAS_270 hit
    ///   ORA-00904 from doing exactly that).
    ///   Read-only here; no writes.
    /// Chronological development:
    ///   VAI052      2026-09-14 Created
    /// </summary>
    public class VAS_273_OrderValueBookedMTDWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_273_OrderValueBookedMTDWidgetController).FullName);

        private const int DefaultPageSize = 10;
        private const int MaxPageSize = 10;
        private const int MaxLineRows = 200;

        /// <summary>
        /// Current-MTD summary figures plus the equivalent-window month-over-month
        /// comparison, for the tile and the modal's 8-card stat strip.
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
                Log.Log(Level.SEVERE, "VAS_273_OrderValueBookedMTDWidget.GetSummary", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// One page of the underlying MTD-booked sales orders for the modal's table,
        /// newest first.
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
                Log.Log(Level.SEVERE, "VAS_273_OrderValueBookedMTDWidget.GetOrders", ex);
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
                Log.Log(Level.SEVERE, "VAS_273_OrderValueBookedMTDWidget.GetSalesOrderDetail", ex);
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
        /// current-MTD booked cohort. GetSummaryData/GetOrdersData each embed this
        /// ALREADY-ACCESS-FILTERED text and must never call AddAccessSQL again on a
        /// combined multi-FROM statement built around it.
        /// </summary>
        private static string BuildBaseOrdersSql()
        {
            return @"
                SELECT o.C_Order_ID AS C_Order_ID,
                       o.DocumentNo AS Document_No,
                       o.DateOrdered AS Date_Ordered,
                       o.C_BPartner_ID AS Bpartner_Id,
                       o.SalesRep_ID AS Sales_Rep_Id,
                       o.TotalLines AS Order_Value,
                       o.DocStatus AS Doc_Status_Code
                  FROM C_Order o
                 WHERE o.AD_Client_ID = @AD_Client_ID
                   AND o.IsActive = 'Y'
                   AND o.IsSOTrx = 'Y'
                   AND COALESCE(o.IsReturnTrx, 'N') = 'N'
                   AND COALESCE(o.IsSalesQuotation, 'N') = 'N'
                   AND o.DocStatus IN ('CO', 'CL')
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

            // The only AddAccessSQL call for the current-MTD statement - applied to the
            // isolated single-WHERE base_orders body BEFORE it is embedded below.
            string baseOrdersSql = MRole.GetDefault(ctx).AddAccessSQL(BuildBaseOrdersSql(), "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string sql = @"
                WITH base_orders AS (" + baseOrdersSql + @"),
                order_line_stats AS (
                    SELECT
                        ol.C_Order_ID AS C_Order_ID,
                        MAX(CASE WHEN ol.C_Quotation_Line_ID IS NOT NULL THEN 1 ELSE 0 END) AS From_Quotation
                      FROM C_OrderLine ol
                     WHERE ol.IsActive = 'Y'
                     GROUP BY ol.C_Order_ID
                )
                SELECT
                    COUNT(1) AS Order_Count,
                    SUM(COALESCE(bo.Order_Value, 0)) AS Order_Value,
                    ROUND(AVG(COALESCE(bo.Order_Value, 0)), 2) AS Avg_Order_Value,
                    COUNT(DISTINCT bo.Bpartner_Id) AS Customers_Billed,
                    COUNT(DISTINCT CASE
                        WHEN bp.FirstSale >= @Period_Start
                         AND bp.FirstSale < @Period_End
                        THEN bo.Bpartner_Id
                        ELSE NULL
                    END) AS New_Customers,
                    SUM(CASE WHEN COALESCE(ols.From_Quotation, 0) = 1 THEN 1 ELSE 0 END) AS From_Quotation_Count,
                    SUM(CASE WHEN COALESCE(ols.From_Quotation, 0) = 0 THEN 1 ELSE 0 END) AS Direct_Order_Count
                  FROM base_orders bo
                  INNER JOIN C_BPartner bp ON ( bp.C_BPartner_ID = bo.Bpartner_Id )
                  LEFT OUTER JOIN order_line_stats ols ON ( ols.C_Order_ID = bo.C_Order_ID )";

            IDataReader dr = null;
            try
            {
                // "@Period_Start"/"@Period_End" each occur TWICE in the assembled text -
                // once inside BuildBaseOrdersSql()'s WHERE clause, once again in the
                // outer New_Customers CASE. This DB layer binds parameters
                // POSITIONALLY (matching left-to-right textual occurrence, not by
                // name), so every occurrence needs its own array entry, in order.
                dr = DB.ExecuteReader(sql, new[]
                {
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                    new SqlParameter("@Period_Start", periodStart),
                    new SqlParameter("@Period_End", periodEnd),
                    new SqlParameter("@Period_Start", periodStart),
                    new SqlParameter("@Period_End", periodEnd)
                });

                if (dr != null && dr.Read())
                {
                    result.OrderCount = dr["Order_Count"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Order_Count"]);
                    result.OrderValue = dr["Order_Value"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Order_Value"]);
                    result.AvgOrderValue = dr["Avg_Order_Value"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Avg_Order_Value"]);
                    result.CustomersBilled = dr["Customers_Billed"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Customers_Billed"]);
                    result.NewCustomers = dr["New_Customers"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["New_Customers"]);
                    result.FromQuotationCount = dr["From_Quotation_Count"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["From_Quotation_Count"]);
                    result.DirectOrderCount = dr["Direct_Order_Count"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Direct_Order_Count"]);
                }
            }
            finally
            {
                CloseReader(dr);
            }

            ApplyMonthOverMonth(ctx, result, periodStart);

            return result;
        }

        /// <summary>
        /// Fetches the previous month's value over an EQUIVALENT elapsed window (same
        /// number of calendar days from the 1st as have elapsed this month so far,
        /// clamped to the previous month's own length) and computes the signed percent
        /// change. A zero (or unavailable) previous value leaves MomPercent null so the
        /// client omits the comparison clause instead of showing a misleading figure.
        /// </summary>
        private void ApplyMonthOverMonth(Ctx ctx, SummaryResult result, DateTime periodStart)
        {
            DateTime today = DateTime.Today;
            int elapsedDays = (today - periodStart).Days + 1;

            DateTime prevMonthStart = periodStart.AddMonths(-1);
            int prevMonthLength = DateTime.DaysInMonth(prevMonthStart.Year, prevMonthStart.Month);
            int clampedElapsed = Math.Min(elapsedDays, prevMonthLength);

            DateTime prevPeriodStart = prevMonthStart;
            DateTime prevPeriodEnd = prevMonthStart.AddDays(clampedElapsed);

            result.ComparisonMonthStart = prevMonthStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            string sql = @"
                SELECT SUM(COALESCE(o.TotalLines, 0)) AS Previous_Order_Value
                  FROM C_Order o
                 WHERE o.AD_Client_ID = @AD_Client_ID
                   AND o.IsActive = 'Y'
                   AND o.IsSOTrx = 'Y'
                   AND COALESCE(o.IsReturnTrx, 'N') = 'N'
                   AND COALESCE(o.IsSalesQuotation, 'N') = 'N'
                   AND o.DocStatus IN ('CO', 'CL')
                   AND o.DateOrdered >= @Prev_Period_Start
                   AND o.DateOrdered < @Prev_Period_End";

            // Single FROM/WHERE, no CTE - AddAccessSQL applies directly here, matching
            // VAS_268's simple-query pattern.
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            decimal previousValue = Util.GetValueOfDecimal(DB.ExecuteScalar(sql, new[]
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@Prev_Period_Start", prevPeriodStart),
                new SqlParameter("@Prev_Period_End", prevPeriodEnd)
            }, null));

            if (previousValue > 0)
            {
                result.MomPercent = Math.Round(((result.OrderValue - previousValue) / previousValue) * 100m, 1);
            }
        }

        private OrdersResult GetOrdersData(Ctx ctx, int page, int size)
        {
            OrdersResult result = new OrdersResult { Rows = new List<OrderRow>() };
            if (ctx == null) { return result; }

            string language = GetLanguage(ctx);
            Dictionary<string, string> docStatusMap = GetDecodeMap("C_Order", "DocStatus", language);

            string baseOrdersSql = MRole.GetDefault(ctx).AddAccessSQL(BuildBaseOrdersSql(), "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string sql = @"
                WITH base_orders AS (" + baseOrdersSql + @"),
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

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, new[]
                {
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                    new SqlParameter("@Period_Start", PeriodStart()),
                    new SqlParameter("@Period_End", PeriodEnd()),
                    new SqlParameter("@Row_Offset", page * size),
                    new SqlParameter("@Page_Size", size)
                });

                int total = 0;
                while (dr != null && dr.Read())
                {
                    total = dr["Total_Rows"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Total_Rows"]);
                    DateTime? dateOrdered = dr["Date_Ordered"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr["Date_Ordered"]);
                    string docStatusCode = Util.GetValueOfString(dr["Doc_Status_Code"]);

                    result.Rows.Add(new OrderRow
                    {
                        SalesOrderId = Util.GetValueOfInt(dr["Order_Id"]),
                        SalesOrderNumber = Util.GetValueOfString(dr["Document_No"]),
                        SalesOrderDate = dateOrdered.HasValue ? dateOrdered.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                        CustomerName = Util.GetValueOfString(dr["Customer_Name"]),
                        RepresentativeName = Util.GetValueOfString(dr["Representative_Name"]),
                        LineCount = dr["Line_Count"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Line_Count"]),
                        OrderQty = dr["Order_Qty"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Order_Qty"]),
                        OrderValue = dr["Order_Value"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Order_Value"]),
                        DocumentStatusCode = docStatusCode,
                        DocumentStatus = DecodeLabel(docStatusMap, docStatusCode)
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
            if (docStatusCode == "CL" && !anyDelivered)
            {
                deliveryStatusKey = "VAS_273_DeliveryNA"; deliveryStatusFallback = "Not applicable";
            }
            else if (hasLines && allDelivered)
            {
                deliveryStatusKey = "VAS_273_DeliveryFull"; deliveryStatusFallback = "Delivered";
            }
            else if (anyDelivered)
            {
                deliveryStatusKey = "VAS_273_DeliveryPartial"; deliveryStatusFallback = "Partially delivered";
            }
            else
            {
                deliveryStatusKey = "VAS_273_DeliveryNone"; deliveryStatusFallback = "Not delivered";
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
                    Log.Log(Level.SEVERE, "VAS_273_OrderValueBookedMTDWidget.GetDecodeMap(" + tableName + "." + columnName + ")", ex);
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
            public int OrderCount { get; set; }
            public decimal OrderValue { get; set; }
            public decimal AvgOrderValue { get; set; }
            public decimal? MomPercent { get; set; }
            public string ComparisonMonthStart { get; set; }
            public int CustomersBilled { get; set; }
            public int NewCustomers { get; set; }
            public int FromQuotationCount { get; set; }
            public int DirectOrderCount { get; set; }
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
            public string RepresentativeName { get; set; }
            public int LineCount { get; set; }
            public decimal OrderQty { get; set; }
            public decimal OrderValue { get; set; }
            public string DocumentStatusCode { get; set; }
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
