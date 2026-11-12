/******************************************************
 * Module Name    : VAS
 * Purpose        : Sales Order dashboard "Warehouse Wise SO · Document Status" cross-tab widget endpoints
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
    /// Module Name : VAS_283_WarehouseWiseSODocumentStatusWidget
    /// Purpose     : Data endpoints for the 4x2 "Warehouse Wise SO · Document Status"
    ///               cross-tab widget on the Sales Order dashboard - for a selected
    ///               Month/Year, how many Sales Orders sit in each of four MUTUALLY
    ///               EXCLUSIVE display buckets, per ship-from warehouse. (1) the
    ///               paginated warehouse rows plus a grand total across the COMPLETE
    ///               result (not just the visible page), and (2) a warehouse
    ///               drill-down - a stat strip that reconciles exactly with the row,
    ///               plus that warehouse's Sales Orders for the period, and (3) the
    ///               shared Sales Order record-preview data (header stats +
    ///               paginated lines) for record/lines drill-through inside that
    ///               order table.
    ///
    ///   Business definition per
    ///   16_Warehouse_Wise_SO_Document_Status_Claude_Development_Prompt.txt - the
    ///   four buckets are a DISPLAY classification only and never alter the stored
    ///   DocStatus (a CO order is still Completed everywhere else in the system):
    ///     - Drafted: DocStatus='DR'.
    ///     - In process: DocStatus='IP'.
    ///     - Partly delivered: DocStatus='CO' AND total delivered qty &gt; 0 AND total
    ///       delivered qty &lt; total ordered qty - the same deterministic derivation
    ///       used by the pending-delivery KPIs (VAS_271/VAS_274/VAS_279), just
    ///       expressed as a bucket rather than a count.
    ///     - Completed: every remaining DocStatus='CO' order (fully delivered, or not
    ///       yet delivered at all - "Completed" here is a document-status reading, not
    ///       a delivery-status one).
    ///     - CL, VO, RE, and every other status are excluded entirely from this
    ///       widget - a row's Total always equals the sum of its four visible bucket
    ///       counts, with nothing silently folded in or dropped.
    ///     - Only warehouses with at least one qualifying order for the period are
    ///       returned (no synthesized zero rows for the whole set) - matching the
    ///       prompt's explicit "do not complicate the query with zero-row warehouse
    ///       generation".
    ///
    ///   Per the CTE/MRole rule (Prompt_Instructions.txt "Case 1"): every query here
    ///   isolates a single "FROM C_Order o" WHERE-only fragment (a LEFT OUTER JOIN to
    ///   its own lines, no GROUP BY) and calls AddAccessSQL ONLY on that isolated
    ///   fragment - never on a fragment that already carries its own GROUP BY. All
    ///   GROUP BY/JOIN/classification aggregation happens in the outer CTEs built
    ///   around the already-filtered text. Per this session's confirmed finding that
    ///   this DB layer binds parameters POSITIONALLY (matching left-to-right textual
    ///   occurrence, not by name), every parameter array below is built in the exact
    ///   order its placeholder appears in the final assembled SQL text.
    ///   Read-only here; no writes.
    /// Chronological development:
    ///   VAI052      2026-09-14 Created
    /// </summary>
    [System.Web.Mvc.SessionState(System.Web.SessionState.SessionStateBehavior.ReadOnly)]
    public class VAS_283_WarehouseWiseSODocumentStatusWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_283_WarehouseWiseSODocumentStatusWidgetController).FullName);

        private const int DefaultPageSize = 7;
        private const int MaxPageSize = 10;
        private const int OrdersPageSize = 10;
        private const int MaxOrdersPageSize = 10;
        private const int MaxLineRows = 200;

        /// <summary>
        /// One page of warehouse rows, ranked by total open SO count descending, plus
        /// the grand total across the COMPLETE result (a window aggregate computed
        /// before OFFSET/FETCH trims to the page, so it is never just the page total).
        /// </summary>
        /// <param name="month">Zero-based month (0=Jan..11=Dec).</param>
        /// <param name="year">Calendar year.</param>
        /// <param name="page">Zero-based page index.</param>
        /// <param name="size">Rows per page (capped at <see cref="MaxPageSize"/>).</param>
        /// <returns>JSON { GrandTotal, Total, Rows:[...] } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetWarehouseStatus(int month, int year, int page = 0, int size = DefaultPageSize)
        {
            if (Session["ctx"] == null) { return Json("", JsonRequestBehavior.AllowGet); }

            Ctx ctx = Session["ctx"] as Ctx;
            if (page < 0) { page = 0; }
            if (size <= 0 || size > MaxPageSize) { size = DefaultPageSize; }

            try
            {
                WarehouseStatusResult data = GetWarehouseStatusData(ctx, month, year, page, size);
                FillBaseCurrency(ctx, data);
                string json = JsonConvert.SerializeObject(data);
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_283_WarehouseWiseSODocumentStatusWidget.GetWarehouseStatus", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// The row-click drill-down for one warehouse: the 6-card stat strip
        /// (reconciling exactly with the row) plus one page of that warehouse's
        /// Sales Orders in the same four buckets.
        /// </summary>
        /// <param name="warehouseId">M_Warehouse_ID.</param>
        /// <param name="month">Zero-based month (0=Jan..11=Dec).</param>
        /// <param name="year">Calendar year.</param>
        /// <param name="page">Zero-based page index.</param>
        /// <param name="size">Rows per page (capped at <see cref="MaxOrdersPageSize"/>).</param>
        /// <returns>JSON { Summary:{...}, Total, Rows:[...] } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetWarehouseOrders(int warehouseId, int month, int year, int page = 0, int size = OrdersPageSize)
        {
            if (Session["ctx"] == null) { return Json("", JsonRequestBehavior.AllowGet); }

            Ctx ctx = Session["ctx"] as Ctx;
            if (page < 0) { page = 0; }
            if (size <= 0 || size > MaxOrdersPageSize) { size = OrdersPageSize; }

            try
            {
                string json = JsonConvert.SerializeObject(GetWarehouseOrdersData(ctx, warehouseId, month, year, page, size));
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_283_WarehouseWiseSODocumentStatusWidget.GetWarehouseOrders", ex);
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
                Log.Log(Level.SEVERE, "VAS_283_WarehouseWiseSODocumentStatusWidget.GetSalesOrderDetail", ex);
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
        /// The single physical-table fragment - one "FROM C_Order o" with a LEFT
        /// OUTER JOIN to its own lines (an order with zero active lines still needs to
        /// be counted/classified), WHERE-only, no GROUP BY (Prompt_Instructions.txt
        /// "Case 1"). The ONLY thing AddAccessSQL is ever applied to for this widget.
        /// The optional warehouse filter is appended at the very END of the WHERE
        /// clause - kept there deliberately so its placeholder's textual position
        /// matches this session's confirmed positional-binding requirement.
        /// </summary>
        /// <summary>
        /// VAS163 2026-11-11: Row 124 - 'Y' when the order's prices include tax (the order's
        /// own IsTaxIncluded flag or its price list's flag) - same expression as VAS_270 / VAS_274.
        /// </summary>
        private const string TaxIncludedSql =
            "(CASE WHEN COALESCE(o.IsTaxIncluded, 'N') = 'Y' " +
            "OR COALESCE((SELECT pl.IsTaxIncluded FROM M_PriceList pl WHERE pl.M_PriceList_ID = o.M_PriceList_ID), 'N') = 'Y' " +
            "THEN 'Y' ELSE 'N' END)";

        /// <summary>
        /// VAS163 2026-11-11: Row 124 - SO value WITHOUT taxes (same expression as VAS_270 /
        /// VAS_274): on a tax-inclusive order TotalLines holds the gross, so the order's
        /// extracted tax (C_OrderTax.TaxAmt) is taken off.
        /// </summary>
        private const string OrderValueExclTaxSql =
            "CASE WHEN " + TaxIncludedSql + " = 'Y' " +
            "THEN COALESCE(o.TotalLines, 0) - (SELECT COALESCE(SUM(COALESCE(ot.TaxAmt, 0)), 0) FROM C_OrderTax ot WHERE ot.C_Order_ID = o.C_Order_ID AND ot.IsActive = 'Y') " +
            "ELSE COALESCE(o.TotalLines, 0) END";

        /// <summary>
        /// VAS163 2026-11-11: Row 120 - 'Y' for an Item-type product line (no charge); only
        /// those lines count in the ordered / delivered figures (delivery classification).
        /// </summary>
        private const string ItemLineSql =
            "(CASE WHEN COALESCE(ol.C_Charge_ID, 0) = 0 " +
            "AND (SELECT p.ProductType FROM M_Product p WHERE p.M_Product_ID = ol.M_Product_ID) = 'I' " +
            "THEN 'Y' ELSE 'N' END)";

        private static string BuildBaseOrderLinesSql(bool filterWarehouse)
        {
            string filter = filterWarehouse ? " AND o.M_Warehouse_ID = @Warehouse_ID" : "";

            // VAS163 2026-11-11: Rows 93/123/124 - order value ex-tax and the document currency;
            // Row 120 - ordered / delivered quantities of Item-type product lines only.
            return @"
                SELECT o.C_Order_ID AS C_Order_ID,
                       o.DocumentNo AS Document_No,
                       o.DateOrdered AS Date_Ordered,
                       " + OrderValueExclTaxSql + @" AS Order_Value,
                       o.C_Currency_ID AS Currency_Id,
                       o.DocStatus AS Doc_Status_Code,
                       o.C_BPartner_ID AS Bpartner_Id,
                       o.M_Warehouse_ID AS Warehouse_Id,
                       o.SalesRep_ID AS Sales_Rep_Id,
                       CASE WHEN " + ItemLineSql + @" = 'Y' THEN ol.QtyOrdered ELSE 0 END AS Qty_Ordered,
                       CASE WHEN " + ItemLineSql + @" = 'Y' THEN ol.QtyDelivered ELSE 0 END AS Qty_Delivered
                  FROM C_Order o
                  LEFT OUTER JOIN C_OrderLine ol ON ( ol.C_Order_ID = o.C_Order_ID AND ol.IsActive = 'Y' )
                 WHERE o.AD_Client_ID = @AD_Client_ID
                   AND o.IsActive = 'Y'
                   AND o.IsSOTrx = 'Y'
                   AND EXISTS (SELECT 1 FROM C_DocType dt WHERE dt.C_DocType_ID = o.C_DocTypeTarget_ID AND dt.DocSubTypeSO IN ('SO', 'WR', 'BO'))
                   AND COALESCE(o.IsReturnTrx, 'N') = 'N'
                   AND o.DocStatus IN ('DR', 'IP', 'CO')
                   AND o.DateOrdered >= @Period_Start
                   AND o.DateOrdered < @Period_End" + filter;
        }

        /// <summary>
        /// The shared per-order aggregation and bucket classification, reused by all
        /// three queries below. Callers prepend "WITH " and append their own final
        /// SELECT against "classified" - never nested as a second WITH, and never
        /// re-run through AddAccessSQL.
        /// </summary>
        private string BuildClassifiedCtesSql(Ctx ctx, bool filterWarehouse)
        {
            string baseLinesSql = MRole.GetDefault(ctx).AddAccessSQL(BuildBaseOrderLinesSql(filterWarehouse), "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            return @"
                base_lines AS (" + baseLinesSql + @"),
                order_qty AS (
                    SELECT
                        C_Order_ID, Document_No, Date_Ordered, Order_Value, Currency_Id, Doc_Status_Code,
                        Bpartner_Id, Warehouse_Id, Sales_Rep_Id,
                        SUM(COALESCE(Qty_Ordered, 0)) AS Qty_Ordered,
                        SUM(COALESCE(Qty_Delivered, 0)) AS Qty_Delivered
                      FROM base_lines
                     GROUP BY C_Order_ID, Document_No, Date_Ordered, Order_Value, Currency_Id, Doc_Status_Code,
                              Bpartner_Id, Warehouse_Id, Sales_Rep_Id
                ),
                classified AS (
                    SELECT
                        C_Order_ID, Document_No, Date_Ordered, Order_Value, Currency_Id, Doc_Status_Code,
                        Bpartner_Id, Warehouse_Id, Sales_Rep_Id, Qty_Ordered, Qty_Delivered,
                        CASE
                            WHEN Doc_Status_Code = 'DR' THEN 'DR'
                            WHEN Doc_Status_Code = 'IP' THEN 'IP'
                            WHEN Doc_Status_Code = 'CO' AND Qty_Delivered > 0 AND Qty_Delivered < Qty_Ordered THEN 'PD'
                            ELSE 'CO'
                        END AS Bucket
                      FROM order_qty
                )";
        }

        private WarehouseStatusResult GetWarehouseStatusData(Ctx ctx, int month, int year, int page, int size)
        {
            WarehouseStatusResult result = new WarehouseStatusResult { Rows = new List<WarehouseStatusRow>() };
            if (ctx == null) { return result; }

            DateTime periodStart, periodEnd;
            ResolvePeriod(month, year, out periodStart, out periodEnd);

            string sql = @"
                WITH " + BuildClassifiedCtesSql(ctx, false) + @",
                warehouse_totals AS (
                    SELECT
                        c.Warehouse_Id AS Warehouse_Id,
                        w.Name AS Warehouse_Name,
                        SUM(CASE WHEN c.Bucket = 'DR' THEN 1 ELSE 0 END) AS Drafted_Count,
                        SUM(CASE WHEN c.Bucket = 'IP' THEN 1 ELSE 0 END) AS In_Process_Count,
                        SUM(CASE WHEN c.Bucket = 'PD' THEN 1 ELSE 0 END) AS Partly_Delivered_Count,
                        SUM(CASE WHEN c.Bucket = 'CO' THEN 1 ELSE 0 END) AS Completed_Count,
                        COUNT(1) AS Total_Count
                      FROM classified c
                      INNER JOIN M_Warehouse w ON ( w.M_Warehouse_ID = c.Warehouse_Id )
                     GROUP BY c.Warehouse_Id, w.Name
                )
                SELECT
                    Warehouse_Id, Warehouse_Name, Drafted_Count, In_Process_Count,
                    Partly_Delivered_Count, Completed_Count, Total_Count,
                    COUNT(1) OVER () AS Total_Warehouses,
                    SUM(Total_Count) OVER () AS Grand_Total
                  FROM warehouse_totals
                 ORDER BY Total_Count DESC, Warehouse_Name ASC
                 OFFSET @Row_Offset ROWS FETCH NEXT @Page_Size ROWS ONLY";

            IDataReader dr = null;
            try
            {
                // Positional order matches the text exactly: base_lines' WHERE clause
                // (AD_Client_ID, Period_Start, Period_End) then the final OFFSET/FETCH.
                dr = DB.ExecuteReader(sql, new[]
                {
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                    new SqlParameter("@Period_Start", periodStart),
                    new SqlParameter("@Period_End", periodEnd),
                    new SqlParameter("@Row_Offset", page * size),
                    new SqlParameter("@Page_Size", size)
                });

                int total = 0;
                int grandTotal = 0;
                while (dr != null && dr.Read())
                {
                    total = dr["Total_Warehouses"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Total_Warehouses"]);
                    grandTotal = dr["Grand_Total"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Grand_Total"]);

                    result.Rows.Add(new WarehouseStatusRow
                    {
                        WarehouseId = Util.GetValueOfInt(dr["Warehouse_Id"]),
                        WarehouseName = Util.GetValueOfString(dr["Warehouse_Name"]),
                        DraftedCount = dr["Drafted_Count"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Drafted_Count"]),
                        InProcessCount = dr["In_Process_Count"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["In_Process_Count"]),
                        PartlyDeliveredCount = dr["Partly_Delivered_Count"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Partly_Delivered_Count"]),
                        CompletedCount = dr["Completed_Count"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Completed_Count"]),
                        TotalCount = dr["Total_Count"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Total_Count"])
                    });
                }
                result.Total = total;
                result.GrandTotal = grandTotal;
            }
            finally
            {
                CloseReader(dr);
            }

            return result;
        }

        private WarehouseOrdersResult GetWarehouseOrdersData(Ctx ctx, int warehouseId, int month, int year, int page, int size)
        {
            WarehouseOrdersResult result = new WarehouseOrdersResult { Rows = new List<OrderRow>() };
            if (ctx == null || warehouseId <= 0) { return result; }

            DateTime periodStart, periodEnd;
            ResolvePeriod(month, year, out periodStart, out periodEnd);

            // Positional order matches the text exactly: base_lines' WHERE clause is
            // AD_Client_ID, Period_Start, Period_End, then Warehouse_ID (the filter
            // clause is appended at the end of BuildBaseOrderLinesSql).
            SqlParameter[] periodAndWarehouseParams = new[]
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@Period_Start", periodStart),
                new SqlParameter("@Period_End", periodEnd),
                new SqlParameter("@Warehouse_ID", warehouseId)
            };

            // Query 1 of 2 - the stat-strip summary for this exact warehouse, kept
            // small and purpose-specific rather than one giant statement.
            string summarySql = @"
                WITH " + BuildClassifiedCtesSql(ctx, true) + @"
                SELECT
                    SUM(CASE WHEN Bucket = 'DR' THEN 1 ELSE 0 END) AS Drafted_Count,
                    SUM(CASE WHEN Bucket = 'IP' THEN 1 ELSE 0 END) AS In_Process_Count,
                    SUM(CASE WHEN Bucket = 'PD' THEN 1 ELSE 0 END) AS Partly_Delivered_Count,
                    SUM(CASE WHEN Bucket = 'CO' THEN 1 ELSE 0 END) AS Completed_Count,
                    COUNT(1) AS Total_Count
                  FROM classified";

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(summarySql, periodAndWarehouseParams);
                if (dr != null && dr.Read())
                {
                    result.Summary = new WarehouseSummary
                    {
                        DraftedCount = dr["Drafted_Count"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Drafted_Count"]),
                        InProcessCount = dr["In_Process_Count"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["In_Process_Count"]),
                        PartlyDeliveredCount = dr["Partly_Delivered_Count"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Partly_Delivered_Count"]),
                        CompletedCount = dr["Completed_Count"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Completed_Count"]),
                        TotalCount = dr["Total_Count"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Total_Count"])
                    };
                }
            }
            finally
            {
                CloseReader(dr);
            }

            if (result.Summary == null) { return result; }

            // Query 2 of 2 - a fresh, warehouse-filtered AddAccessSQL pass for the
            // paginated order list.
            string language = GetLanguage(ctx);
            Dictionary<string, string> docStatusMap = GetDecodeMap("C_Order", "DocStatus", language);

            string listSql = @"
                WITH " + BuildClassifiedCtesSql(ctx, true) + @"
                SELECT
                    c.C_Order_ID AS Order_Id,
                    c.Document_No AS Document_No,
                    c.Date_Ordered AS Date_Ordered,
                    c.Order_Value AS Order_Value,
                    c.Doc_Status_Code AS Doc_Status_Code,
                    c.Qty_Ordered AS Qty_Ordered,
                    c.Qty_Delivered AS Qty_Delivered,
                    COALESCE(bp.Name, N'') AS Customer_Name,
                    COALESCE(wh.Name, N'') AS Warehouse_Name,
                    COALESCE(rep.Name, N'') AS Representative_Name,
                    curr.CurSymbol AS Currency_Symbol,
                    curr.ISO_Code AS Currency_Code,
                    curr.StdPrecision AS Currency_Precision,
                    COUNT(1) OVER () AS Total_Rows
                  FROM classified c
                  INNER JOIN C_BPartner bp ON ( bp.C_BPartner_ID = c.Bpartner_Id )
                  LEFT OUTER JOIN M_Warehouse wh ON ( wh.M_Warehouse_ID = c.Warehouse_Id )
                  LEFT OUTER JOIN AD_User rep ON ( rep.AD_User_ID = c.Sales_Rep_Id )
                  LEFT OUTER JOIN C_Currency curr ON ( curr.C_Currency_ID = c.Currency_Id )
                 ORDER BY c.Date_Ordered DESC, c.Document_No DESC
                 OFFSET @Row_Offset ROWS FETCH NEXT @Page_Size ROWS ONLY";

            List<SqlParameter> listParams = new List<SqlParameter>(periodAndWarehouseParams)
            {
                new SqlParameter("@Row_Offset", page * size),
                new SqlParameter("@Page_Size", size)
            };

            IDataReader dr2 = null;
            try
            {
                dr2 = DB.ExecuteReader(listSql, listParams.ToArray());

                int total = 0;
                while (dr2 != null && dr2.Read())
                {
                    total = dr2["Total_Rows"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr2["Total_Rows"]);
                    DateTime? dateOrdered = dr2["Date_Ordered"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr2["Date_Ordered"]);
                    string docStatusCode = Util.GetValueOfString(dr2["Doc_Status_Code"]);
                    decimal qtyOrdered = dr2["Qty_Ordered"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr2["Qty_Ordered"]);
                    decimal qtyDelivered = dr2["Qty_Delivered"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr2["Qty_Delivered"]);

                    string deliveryCode, deliveryFallback, deliveryStatusCode;
                    if (qtyOrdered > 0 && qtyDelivered >= qtyOrdered) { deliveryCode = "VAS_283_DeliveryFull"; deliveryFallback = "Fully delivered"; deliveryStatusCode = "FULL"; }
                    else if (qtyDelivered > 0) { deliveryCode = "VAS_283_DeliveryPartial"; deliveryFallback = "Partial"; deliveryStatusCode = "PARTIAL"; }
                    else { deliveryCode = "VAS_283_DeliveryPending"; deliveryFallback = "Pending"; deliveryStatusCode = "PENDING"; }

                    // VAS163 2026-11-11: Row 93 - document (price list) currency of the order value.
                    string rowCurrencySymbol = (Util.GetValueOfString(dr2["Currency_Symbol"]) ?? "").Trim();
                    if (string.IsNullOrEmpty(rowCurrencySymbol)) { rowCurrencySymbol = (Util.GetValueOfString(dr2["Currency_Code"]) ?? "").Trim(); }

                    result.Rows.Add(new OrderRow
                    {
                        SalesOrderId = Util.GetValueOfInt(dr2["Order_Id"]),
                        SalesOrderNumber = Util.GetValueOfString(dr2["Document_No"]),
                        SalesOrderDate = dateOrdered.HasValue ? dateOrdered.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                        CustomerName = Util.GetValueOfString(dr2["Customer_Name"]),
                        WarehouseName = Util.GetValueOfString(dr2["Warehouse_Name"]),
                        RepresentativeName = Util.GetValueOfString(dr2["Representative_Name"]),
                        OrderValue = dr2["Order_Value"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr2["Order_Value"]),
                        CurrencySymbol = rowCurrencySymbol,
                        CurrencyPrecision = dr2["Currency_Precision"] == DBNull.Value ? 2 : Util.GetValueOfInt(dr2["Currency_Precision"]),
                        DocumentStatus = DecodeLabel(docStatusMap, docStatusCode),
                        // VAS163 2026-11-11: Row 116 - raw DocStatus so the client colours DR / IP orange.
                        DocumentStatusCode = docStatusCode,
                        // VAS163 2026-11-11: text-only overload (the 2-argument one appends the tip).
                        DeliveryStatus = Msg.GetMsg(ctx, deliveryCode, true) ?? deliveryFallback,
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

            int warehouseId = 0;
            // VAS163 2026-11-11: Rows 123/124 - tax-inclusive flag and document currency for the lines.
            bool priceListTaxIncluded = false;
            int stdPrecision = 2;
            string currencySymbol = "";

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
                priceListTaxIncluded = Util.GetValueOfString(dr["Price_List_Tax_Included"]) == "Y";
                stdPrecision = dr["Currency_Precision"] == DBNull.Value ? 2 : Util.GetValueOfInt(dr["Currency_Precision"]);
                currencySymbol = Util.GetValueOfString(dr["Currency_Symbol"]);
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
                    OrderValue = dr["Order_Value"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Order_Value"]),
                    // VAS163 2026-11-11: Row 123 - document (price list) currency of the SO value / lines.
                    CurrencySymbol = (currencySymbol ?? "").Trim(),
                    CurrencyPrecision = stdPrecision,
                    // VAS163 2026-11-11: Row 116 - raw DocStatus so the client colours DR / IP orange.
                    DocumentStatusCode = docStatusCode,
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

            // VAS163 2026-11-11: Row 120 - delivery status from Item-type product lines only.
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
            if (hasLines && allDelivered) { deliveryStatusKey = "VAS_283_DeliveryFull"; deliveryStatusFallback = "Fully delivered"; }
            else if (anyDelivered) { deliveryStatusKey = "VAS_283_DeliveryPartial"; deliveryStatusFallback = "Partial"; }
            else { deliveryStatusKey = "VAS_283_DeliveryPending"; deliveryStatusFallback = "Pending"; }
            // VAS163 2026-11-11: text-only overload (the 2-argument one appends the tip).
            result.Order.DeliveryStatus = Msg.GetMsg(ctx, deliveryStatusKey, true) ?? deliveryStatusFallback;

            return result;
        }

        /// <summary>Every active line of the order, each carrying its own free-stock figure.</summary>
        private void LoadOrderLines(Ctx ctx, OrderDetailResult result, int orderId, int headerWarehouseId, bool priceListTaxIncluded, int stdPrecision)
        {
            // VAS163 2026-11-11: Rows 117/118/120/124 - charge, product type, QtyEntered,
            // PriceEntered, line tax and the product's base UOM added (same columns as VAS_274).
            string sql = @"
                SELECT ol.C_OrderLine_ID AS Line_Id,
                       ol.Line AS Line_No,
                       ol.M_Product_ID AS Product_Id,
                       ol.C_Charge_ID AS Charge_Id,
                       ol.Description AS Description,
                       p.ProductType AS Product_Type,
                       ch.Name AS Charge_Name,
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

            // VAS163 2026-11-11: Row 124 - on a tax-inclusive order each line is restated net of
            // tax with the order's own tax summary (C_OrderTax), as in VAS_270 / VAS_274.
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
                    int chargeId = dr["Charge_Id"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Charge_Id"]);
                    int lineWarehouseId = dr["Line_Warehouse_Id"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Line_Warehouse_Id"]);
                    int warehouseId = lineWarehouseId > 0 ? lineWarehouseId : headerWarehouseId;

                    // VAS163 2026-11-11: Row 117 - quantities in the line's selected UOM: QtyEntered
                    // is in ol.C_UOM_ID, QtyOrdered / QtyDelivered in the base UOM, so delivered is
                    // scaled by QtyEntered / QtyOrdered (division guarded).
                    decimal uomRatio = qtyOrderedBase != 0m ? (qtyEntered / qtyOrderedBase) : 1m;
                    decimal qtyOrdered = qtyEntered;
                    decimal qtyDelivered = qtyDeliveredBase * uomRatio;

                    // VAS163 2026-11-11: Row 120 - only Item-type product lines (never a charge)
                    // carry ordered / delivered / pending / stock / line status and count in totals.
                    bool isItem = productId > 0 && chargeId <= 0
                        && string.Equals((Util.GetValueOfString(dr["Product_Type"]) ?? "").Trim(), "I", StringComparison.OrdinalIgnoreCase);

                    // VAS163 2026-11-11: Row 118 - a charge line shows the charge's name as the product.
                    string productName = chargeId > 0 ? Util.GetValueOfString(dr["Charge_Name"]) : null;
                    if (string.IsNullOrEmpty(productName)) { productName = Util.GetValueOfString(dr["Product_Name"]); }
                    if (string.IsNullOrEmpty(productName)) { productName = Util.GetValueOfString(dr["Charge_Name"]); }
                    if (string.IsNullOrEmpty(productName)) { productName = Util.GetValueOfString(dr["Description"]); }

                    // VAS163 2026-11-11: Row 124 - line amount without tax (VAS_270 approach): on a
                    // tax-inclusive order LineNetAmt is gross and is scaled by its tax's net/gross
                    // ratio from C_OrderTax; fallback the line's TaxAmt / rate.
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

                    // VAS163 2026-11-11: Rows 117/124 - rate per selected UOM (PriceEntered, else
                    // PriceActual), restated ex-tax with the line's net/gross proportion.
                    decimal rate = dr["Price_Entered"] == DBNull.Value
                        ? (dr["Price_Actual"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Price_Actual"]))
                        : Util.GetValueOfDecimal(dr["Price_Entered"]);
                    if (priceListTaxIncluded && lineGross != 0m)
                    {
                        rate = Math.Round(rate * lineAmt / lineGross, 6, MidpointRounding.AwayFromZero);
                    }

                    // VAS163 2026-11-11: row 58 In stock (base UOM) never negative - show 0; the
                    // shortage is judged in the line's UOM.
                    decimal freeStockBase = isItem ? Math.Max(0m, ResolveFreeStock(ctx, productId, warehouseId)) : 0m;
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
                        FreeStock = freeStockBase,
                        QtyShort = isItem ? Math.Max(0m, qtyPending - freeStockBase * uomRatio) : 0m,
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
        /// VAS163 2026-11-11: Row 124 - tax-inclusive order only: per C_Tax_ID, the share of the
        /// lines' gross (SUM(LineNetAmt)) that is NOT tax, from the order's own tax summary
        /// (C_OrderTax.TaxAmt) - copied from VAS_274 / VAS_270, so the lines add up to the SO value.
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
                Log.Log(Level.SEVERE, "VAS_283_WarehouseWiseSODocumentStatusWidget.GetOrderTaxNetRatios", ex);
            }
            return ratios;
        }

        /// <summary>
        /// Warehouse-level free stock for one product: QtyOnHand - QtyReserved -
        /// QtyDedicated - QtyAllocated, summed across the warehouse's active locators.
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
                    Log.Log(Level.SEVERE, "VAS_283_WarehouseWiseSODocumentStatusWidget.GetDecodeMap(" + tableName + "." + columnName + ")", ex);
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
            string message = Msg.GetMsg(ctx, "Error") ?? "Error";
            string json = JsonConvert.SerializeObject(new { Error = message });
            return Json(json, JsonRequestBehavior.AllowGet);
        }

        private class WarehouseStatusResult : CurrencyPayload
        {
            public int GrandTotal { get; set; }
            public int Total { get; set; }
            public List<WarehouseStatusRow> Rows { get; set; }
        }

        private class WarehouseStatusRow
        {
            public int WarehouseId { get; set; }
            public string WarehouseName { get; set; }
            public int DraftedCount { get; set; }
            public int InProcessCount { get; set; }
            public int PartlyDeliveredCount { get; set; }
            public int CompletedCount { get; set; }
            public int TotalCount { get; set; }
        }

        private class WarehouseOrdersResult
        {
            public WarehouseSummary Summary { get; set; }
            public int Total { get; set; }
            public List<OrderRow> Rows { get; set; }
        }

        private class WarehouseSummary
        {
            public int DraftedCount { get; set; }
            public int InProcessCount { get; set; }
            public int PartlyDeliveredCount { get; set; }
            public int CompletedCount { get; set; }
            public int TotalCount { get; set; }
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
            /// <summary>VAS163 2026-11-11: Row 93 - document (price list) currency of OrderValue.</summary>
            public string CurrencySymbol { get; set; }
            public int CurrencyPrecision { get; set; }
            public string DocumentStatus { get; set; }
            /// <summary>VAS163 2026-11-11: Row 116 - raw C_Order.DocStatus (DR / IP / CO).</summary>
            public string DocumentStatusCode { get; set; }
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
            /// <summary>VAS163 2026-11-11: Row 123 - document (price list) currency of the SO value / lines.</summary>
            public string CurrencySymbol { get; set; }
            public int CurrencyPrecision { get; set; }
            /// <summary>VAS163 2026-11-11: Row 116 - raw C_Order.DocStatus (DR / IP / CO).</summary>
            public string DocumentStatusCode { get; set; }
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
            /// <summary>VAS163 2026-11-11: product's base UOM (In stock is in it).</summary>
            public string BaseUomName { get; set; }
            /// <summary>VAS163 2026-11-11: Row 120 - Item-type product line (not a charge).</summary>
            public bool IsItem { get; set; }
            public decimal QtyOrdered { get; set; }
            public decimal QtyDelivered { get; set; }
            public decimal QtyPending { get; set; }
            public decimal FreeStock { get; set; }
            /// <summary>VAS163 2026-11-11: pending minus stock, in the line's UOM, never negative.</summary>
            public decimal QtyShort { get; set; }
            public decimal Rate { get; set; }
            public decimal Amount { get; set; }
        }
    }
}
