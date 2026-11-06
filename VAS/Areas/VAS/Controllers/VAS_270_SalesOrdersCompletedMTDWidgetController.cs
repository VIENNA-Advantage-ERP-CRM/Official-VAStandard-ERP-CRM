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
    ///     - Delivered value (5-Nov Row 43, supersedes Sr 8.12) = SUM over the Item-type
    ///       lines of every cohort order of delivered qty x net price without tax - the
    ///       delivered portion of partly delivered orders counts, the undelivered
    ///       (pending) rest never does. Delivery state counts Item lines only (Sr 8.15).
    ///     - On-time shipment (5-Nov Row 44) over every cohort SO: fully delivered ->
    ///       on time when the last completed outbound M_InOut (IsSOTrx='Y',
    ///       IsReturnTrx='N', DocStatus CO/CL) MovementDate day <= DatePromised day,
    ///       else delayed; not fully delivered with a promised date already passed ->
    ///       delayed; not fully delivered with a promised date today or later, no
    ///       promised date, or no Item line -> left out of the denominator. A zero
    ///       denominator is surfaced as a null percentage (client renders "-").
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
    ///   Claude      2026-11-05 Rows 43/44/45/26: delivered value incl. partial deliveries,
    ///               reworked on-time %, ex-tax values from C_OrderTax, In stock in base UOM.
    /// </summary>
    [System.Web.Mvc.SessionState(System.Web.SessionState.SessionStateBehavior.ReadOnly)]
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
                       " + OrderValueExclTaxSql + @" AS Order_Value,
                       " + TaxIncludedSql + @" AS Tax_Included,
                       o.C_BPartner_ID AS Bpartner_Id,
                       o.M_Warehouse_ID AS Warehouse_Id,
                       o.SalesRep_ID AS Sales_Rep_Id
                  FROM C_Order o
                 WHERE o.AD_Client_ID = @AD_Client_ID
                   AND o.IsActive = 'Y'
                   AND o.IsSOTrx = 'Y'
                   AND COALESCE(o.IsReturnTrx, 'N') = 'N'
                   AND EXISTS (SELECT 1 FROM C_DocType dt WHERE dt.C_DocType_ID = o.C_DocTypeTarget_ID AND dt.DocSubTypeSO IN ('SO', 'WR', 'BO'))
                   AND o.DocStatus = 'CO'
                   AND o.DateOrdered >= @Period_Start
                   AND o.DateOrdered < @Period_End";
        }

        /// <summary>
        /// 'Y' when the order's prices include tax - the order's own IsTaxIncluded flag
        /// (5-Nov Row 45: "SO has IsTaxIncluded = Y") or its price list's flag.
        /// </summary>
        private const string TaxIncludedSql =
            "(CASE WHEN COALESCE(o.IsTaxIncluded, 'N') = 'Y' " +
            "OR COALESCE((SELECT pl.IsTaxIncluded FROM M_PriceList pl WHERE pl.M_PriceList_ID = o.M_PriceList_ID), 'N') = 'Y' " +
            "THEN 'Y' ELSE 'N' END)";

        /// <summary>
        /// SO value WITHOUT taxes: on a tax-inclusive order C_Order.TotalLines holds the
        /// gross, so the order's extracted tax (C_OrderTax.TaxAmt) is taken off (same
        /// expression as VAS_268 / VAS_203). 5-Nov Row 45: the tax was previously read
        /// from C_OrderLine.TaxAmt, which is not maintained on every line (often 0), so the
        /// gross stayed in. Scalar sub-selects in the SELECT list leave the main WHERE
        /// untouched for AddAccessSQL.
        /// </summary>
        private const string OrderValueExclTaxSql =
            "CASE WHEN " + TaxIncludedSql + " = 'Y' " +
            "THEN COALESCE(o.TotalLines, 0) - (SELECT COALESCE(SUM(COALESCE(ot.TaxAmt, 0)), 0) FROM C_OrderTax ot WHERE ot.C_Order_ID = o.C_Order_ID AND ot.IsActive = 'Y') " +
            "ELSE COALESCE(o.TotalLines, 0) END";

        /// <summary>
        /// 5-Nov Row 43: value of the DELIVERED quantity of one line, without tax (alias
        /// "ol" = C_OrderLine, "bo" = base_orders, "tx" = C_Tax): the delivered share
        /// (capped at the ordered quantity) of the line's net amount, so discounts are
        /// respected; on a tax-inclusive order the tax is extracted with the line's tax
        /// rate (gross * 100 / (100 + rate)). ROUND keeps the PostgreSQL numeric
        /// quotient within System.Decimal.
        /// </summary>
        private const string DeliveredValueSql =
            "CASE WHEN COALESCE(ol.QtyOrdered, 0) <> 0 AND COALESCE(ol.QtyDelivered, 0) > 0 " +
            "THEN ROUND((CASE WHEN ol.QtyDelivered > ol.QtyOrdered THEN ol.QtyOrdered ELSE ol.QtyDelivered END) " +
            "* COALESCE(ol.LineNetAmt, 0) / ol.QtyOrdered " +
            "* (CASE WHEN bo.Tax_Included = 'Y' THEN 100 / (100 + COALESCE(tx.Rate, 0)) ELSE 1 END), 6) " +
            "ELSE 0 END";

        private SummaryResult GetSummaryData(Ctx ctx)
        {
            DateTime periodStart = PeriodStart();
            DateTime periodEnd = PeriodEnd();
            DateTime today = DateTime.Today;

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

            // One row per cohort order; the figures are aggregated in C# below so the
            // on-time rule can compare calendar DAYS (MovementDate / DatePromised may carry
            // a time) without database-specific date functions (Oracle and PostgreSQL).
            // Delivery figures count Item-type product lines only (Sr 8.15).
            string sql = @"
                WITH base_orders AS (" + baseOrdersSql + @"),
                line_delivery AS (
                    SELECT
                        ol.C_Order_ID AS C_Order_ID,
                        SUM(COALESCE(ol.QtyOrdered, 0)) AS Ordered_Qty,
                        SUM(COALESCE(ol.QtyDelivered, 0)) AS Delivered_Qty,
                        SUM(" + DeliveredValueSql + @") AS Delivered_Value
                      FROM base_orders bo
                     INNER JOIN C_OrderLine ol ON ( ol.C_Order_ID = bo.C_Order_ID AND ol.IsActive = 'Y' )
                     INNER JOIN M_Product p ON ( p.M_Product_ID = ol.M_Product_ID AND p.ProductType = 'I' )
                      LEFT OUTER JOIN C_Tax tx ON ( tx.C_Tax_ID = ol.C_Tax_ID )
                     GROUP BY ol.C_Order_ID
                ),
                last_shipment AS (
                    SELECT
                        io.C_Order_ID AS C_Order_ID,
                        MAX(io.MovementDate) AS Last_Shipment_Date
                      FROM M_InOut io
                     INNER JOIN base_orders sbo ON ( sbo.C_Order_ID = io.C_Order_ID )
                     WHERE io.IsActive = 'Y'
                       AND io.IsSOTrx = 'Y'
                       AND COALESCE(io.IsReturnTrx, 'N') = 'N'
                       AND io.DocStatus IN ('CO', 'CL')
                     GROUP BY io.C_Order_ID
                )
                SELECT
                    o.C_Order_ID AS Order_Id,
                    o.Date_Promised AS Date_Promised,
                    COALESCE(ld.Ordered_Qty, 0) AS Ordered_Qty,
                    COALESCE(ld.Delivered_Qty, 0) AS Delivered_Qty,
                    COALESCE(ld.Delivered_Value, 0) AS Delivered_Value,
                    ls.Last_Shipment_Date AS Last_Shipment_Date
                  FROM base_orders o
                  LEFT OUTER JOIN line_delivery ld ON ( ld.C_Order_ID = o.C_Order_ID )
                  LEFT OUTER JOIN last_shipment ls ON ( ls.C_Order_ID = o.C_Order_ID )";

            int completedCount = 0, deliveredOrderCount = 0, onTimeCount = 0, onTimeDenominator = 0;
            decimal deliveredValue = 0m;

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, new[]
                {
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                    new SqlParameter("@Period_Start", periodStart),
                    new SqlParameter("@Period_End", periodEnd)
                });

                while (dr != null && dr.Read())
                {
                    completedCount++;

                    decimal orderedQty = dr["Ordered_Qty"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Ordered_Qty"]);
                    decimal deliveredQty = dr["Delivered_Qty"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Delivered_Qty"]);
                    DateTime? datePromised = dr["Date_Promised"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr["Date_Promised"]);
                    DateTime? lastShipment = dr["Last_Shipment_Date"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr["Last_Shipment_Date"]);

                    if (deliveredQty > 0m) { deliveredOrderCount++; }

                    // 5-Nov Row 43: delivered value = delivered qty x net price (ex tax) over
                    // the Item lines of every cohort order - fully AND partially delivered
                    // orders contribute their delivered portion; the undelivered rest never
                    // counts. (Supersedes the earlier "fully delivered orders only" rule.)
                    deliveredValue += dr["Delivered_Value"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Delivered_Value"]);

                    // 5-Nov Row 44: on-time shipment over every SO of this month's cohort.
                    //  - Fully delivered: on time when the last completed DO's movement date
                    //    is on/before the promised date, else delayed.
                    //  - Not fully delivered and the promised date has passed (before today):
                    //    delayed (no complete delivery by the promised date).
                    //  - Not fully delivered and promised today or later: not judged yet,
                    //    left out of the denominator.
                    //  - No promised date, or no Item line (nothing to ship): left out.
                    if (orderedQty <= 0m || !datePromised.HasValue) { continue; }
                    DateTime promisedDay = datePromised.Value.Date;
                    bool fullyDelivered = deliveredQty >= orderedQty;
                    if (fullyDelivered)
                    {
                        if (!lastShipment.HasValue) { continue; }
                        onTimeDenominator++;
                        if (lastShipment.Value.Date <= promisedDay) { onTimeCount++; }
                    }
                    else if (promisedDay < today)
                    {
                        onTimeDenominator++;
                    }
                }
            }
            finally
            {
                CloseReader(dr);
            }

            result.CompletedCount = completedCount;
            result.DeliveredOrderCount = deliveredOrderCount;
            result.DeliveredValue = deliveredValue;
            if (onTimeDenominator > 0)
            {
                result.OnTimePercent = (int)Math.Round(100m * onTimeCount / onTimeDenominator, MidpointRounding.AwayFromZero);
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
                     INNER JOIN M_Product p ON ( p.M_Product_ID = ol.M_Product_ID AND p.ProductType = 'I' )
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

            // 5-Nov Row 45: on a tax-inclusive order each line is restated net of tax with
            // the order's own tax summary (C_OrderTax) - see GetOrderTaxNetRatios.
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

                    // Line amount without tax (5-Nov Row 45). On a tax-inclusive order
                    // LineNetAmt is the gross: it is scaled by its tax's net/gross ratio from
                    // C_OrderTax (so the lines add up to the header SO value); only when the
                    // order has no tax summary for that tax is the line's own TaxAmt (or the
                    // rate-derived tax) taken off.
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
                    // 5-Nov Row 45: on a tax-inclusive order the price is gross too, so it is
                    // restated with the same net/gross proportion as the line amount.
                    decimal rate = dr["Price_Entered"] == DBNull.Value
                        ? (dr["Price_Actual"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Price_Actual"]))
                        : Util.GetValueOfDecimal(dr["Price_Entered"]);
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
                        FreeStock = onHandBase,
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
        /// Tax-inclusive order only (5-Nov Row 45): per C_Tax_ID, the share of the lines'
        /// gross (SUM(LineNetAmt)) that is NOT tax, taken from the order's own tax summary
        /// (C_OrderTax.TaxAmt) - the same tax the header SO value takes off, so the lines
        /// add up to it. A tax with no summary row (or an implausible one) is left out and
        /// the caller falls back to the line's own TaxAmt / rate.
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
                Log.Log(Level.SEVERE, "VAS_270_SalesOrdersCompletedMTDWidget.GetOrderTaxNetRatios", ex);
            }
            return ratios;
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
