/******************************************************
 * Module Name    : VAS
 * Purpose        : Sales Order dashboard "Due for Delivery" KPI + preview widget endpoints
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
    /// Module Name : VAS_279_DueForDeliveryWidget
    /// Purpose     : Data endpoints for the 2x2 "Due for Delivery" KPI + 3-row preview
    ///               widget on the Sales Order dashboard - a near-term commitment
    ///               window: firm (DocStatus='CO') Sales Orders with at least one
    ///               active line still pending delivery, promised for delivery within
    ///               the CURRENT calendar month. No Month/Year filter - always the
    ///               current month, recomputed on every load. (1) the KPI figures -
    ///               due-this-month count/value and a next-7-calendar-day count, and
    ///               (2) the 3 earliest-promised preview rows, and (3) the shared
    ///               Sales Order record-preview data (header stats + paginated lines
    ///               with per-line free stock) opened directly by a preview row click
    ///               - there is no intermediate documents-list modal for this widget.
    ///
    ///   Business definition per
    ///   12_Due_For_Delivery_Claude_Development_Prompt.txt:
    ///     - Real active Sales Order, DocStatus = 'CO' only, non-quotation,
    ///       non-return, DatePromised within [MonthStart, MonthEnd) of the CURRENT
    ///       month, AND at least one active C_OrderLine with QtyOrdered &gt;
    ///       QtyDelivered (the same deterministic pending-delivery rule as VAS_271 /
    ///       VAS_274, so this widget and those two never disagree on what counts as
    ///       "still pending").
    ///     - An order promised earlier in the current month and now overdue REMAINS
    ///       counted here while pending - "promised in this month" is read honestly,
    ///       not narrowed to "promised and not yet overdue". The Overdue Deliveries
    ///       tile (VAS_274) is the dedicated overdue-focused view; this tile is not.
    ///     - dueThisMonthValue = SUM(C_Order.TotalLines) for the qualifying cohort.
    ///     - dueNext7DaysCount = calendar days from Today through Today+7 inclusive
    ///       (exclusive upper bound Today+8), counted only among rows that are not
    ///       already overdue (DatePromised &gt;= Today) - Today/Next7End are computed
    ///       in C# and bound as parameters, never a DB-specific date function.
    ///     - Preview = the 3 rows with the earliest DatePromised (ties broken by
    ///       DocumentNo), giving a genuine "next up" list rather than an arbitrary
    ///       sample. No pager on this widget - the full set lives in the SO Queue
    ///       widget or is reached via a row click.
    ///
    ///   Per the CTE/MRole rule (Prompt_Instructions.txt "Case 1"): both the summary
    ///   and preview queries isolate a single "FROM C_Order o" fragment
    ///   (BuildBaseOrdersSql), call AddAccessSQL ONLY on that isolated fragment (a
    ///   fresh call per statement), then embed the already-filtered text into a
    ///   larger multi-CTE statement - AddAccessSQL is never applied to either
    ///   combined statement itself.
    ///   Read-only here; no writes.
    /// Chronological development:
    ///   VAI052      2026-09-14 Created
    /// </summary>
    public class VAS_279_DueForDeliveryWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_279_DueForDeliveryWidgetController).FullName);

        private const int PreviewCount = 3;
        private const int MaxLineRows = 200;

        /// <summary>
        /// The KPI figures plus the 3-row earliest-promised preview - one call serves
        /// the whole widget (Prompt_Instructions "keep queries small and purpose
        /// specific" is satisfied here by two small internal queries, not one giant
        /// statement, behind this single HTTP round trip).
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
                Log.Log(Level.SEVERE, "VAS_279_DueForDeliveryWidget.GetSummary", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// Full data for the shared Sales Order record-preview modal, opened directly
        /// by a preview row click (header stats and every active line, capped at
        /// <see cref="MaxLineRows"/> - the client paginates client-side, sized to the
        /// space actually available, so the modal body never scrolls).
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
                Log.Log(Level.SEVERE, "VAS_279_DueForDeliveryWidget.GetSalesOrderDetail", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// The base_orders CTE body - the ONLY fragment AddAccessSQL is ever applied to
        /// (Prompt_Instructions.txt "Case 1"): a single SELECT against the one main
        /// physical table (C_Order "o") with its own single WHERE clause, scoped to
        /// firm Sales Orders promised within the current calendar month.
        /// </summary>
        private static string BuildBaseOrdersSql()
        {
            return @"
                SELECT o.C_Order_ID AS C_Order_ID,
                       o.DocumentNo AS Document_No,
                       o.DatePromised AS Date_Promised,
                       o.TotalLines AS Order_Value,
                       o.C_BPartner_ID AS Bpartner_Id
                  FROM C_Order o
                 WHERE o.AD_Client_ID = @AD_Client_ID
                   AND o.IsActive = 'Y'
                   AND o.IsSOTrx = 'Y'
                   AND COALESCE(o.IsReturnTrx, 'N') = 'N'
                   AND COALESCE(o.IsSalesQuotation, 'N') = 'N'
                   AND o.DocStatus = 'CO'
                   AND o.DatePromised >= @Month_Start
                   AND o.DatePromised < @Month_End";
        }

        /// <summary>
        /// The CTE definitions shared by the summary and preview queries: base_orders
        /// (already access-filtered) narrowed to only the still-pending cohort via the
        /// same deterministic "at least one active line has QtyOrdered &gt;
        /// QtyDelivered" rule as VAS_271/VAS_274 (a per-order SUM+GROUP BY, never an
        /// EXISTS against the base fragment, so the isolated base_orders text stays a
        /// clean single FROM). Callers prepend "WITH " and append their own final
        /// SELECT - never nested as a second WITH, and never re-run through
        /// AddAccessSQL.
        /// </summary>
        private string BuildPendingOrdersCtesSql(Ctx ctx)
        {
            string baseOrdersSql = MRole.GetDefault(ctx).AddAccessSQL(BuildBaseOrdersSql(), "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            return @"
                base_orders AS (" + baseOrdersSql + @"),
                line_pending AS (
                    SELECT
                        bo.C_Order_ID AS C_Order_ID,
                        SUM(
                            CASE WHEN COALESCE(ol.QtyOrdered, 0) > COALESCE(ol.QtyDelivered, 0)
                                 THEN COALESCE(ol.QtyOrdered, 0) - COALESCE(ol.QtyDelivered, 0)
                                 ELSE 0
                            END
                        ) AS Pending_Qty
                      FROM base_orders bo
                      INNER JOIN C_OrderLine ol ON ( ol.C_Order_ID = bo.C_Order_ID AND ol.IsActive = 'Y' )
                     GROUP BY bo.C_Order_ID
                ),
                pending_orders AS (
                    SELECT
                        bo.C_Order_ID AS C_Order_ID,
                        bo.Document_No AS Document_No,
                        bo.Date_Promised AS Date_Promised,
                        bo.Order_Value AS Order_Value,
                        bo.Bpartner_Id AS Bpartner_Id
                      FROM base_orders bo
                      INNER JOIN line_pending lp ON ( lp.C_Order_ID = bo.C_Order_ID )
                     WHERE lp.Pending_Qty > 0
                )";
        }

        private SummaryResult GetSummaryData(Ctx ctx)
        {
            SummaryResult result = new SummaryResult { Preview = new List<PreviewRow>() };
            if (ctx == null) { return result; }

            DateTime today = DateTime.Today;
            DateTime monthStart = new DateTime(today.Year, today.Month, 1);
            DateTime monthEnd = monthStart.AddMonths(1);
            // Today through Today+7 inclusive => exclusive upper bound is Today+8.
            DateTime next7EndExclusive = today.AddDays(8);

            SqlParameter[] periodParams = new[]
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@Month_Start", monthStart),
                new SqlParameter("@Month_End", monthEnd),
                new SqlParameter("@Today", today),
                new SqlParameter("@Next7_End", next7EndExclusive)
            };

            string summarySql = @"
                WITH " + BuildPendingOrdersCtesSql(ctx) + @"
                SELECT
                    COUNT(1) AS Due_This_Month_Count,
                    COALESCE(SUM(Order_Value), 0) AS Due_This_Month_Value,
                    COALESCE(SUM(
                        CASE WHEN Date_Promised >= @Today AND Date_Promised < @Next7_End
                             THEN 1 ELSE 0
                        END
                    ), 0) AS Due_Next_7_Days_Count
                  FROM pending_orders";

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(summarySql, periodParams);
                if (dr != null && dr.Read())
                {
                    result.DueThisMonthCount = dr["Due_This_Month_Count"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Due_This_Month_Count"]);
                    result.DueThisMonthValue = dr["Due_This_Month_Value"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Due_This_Month_Value"]);
                    result.DueNext7DaysCount = dr["Due_Next_7_Days_Count"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Due_Next_7_Days_Count"]);
                }
            }
            finally
            {
                CloseReader(dr);
            }

            // A second, independent AddAccessSQL pass (fresh base_orders text) for the
            // preview query, matching every other widget's per-query pattern.
            string previewSql = @"
                WITH " + BuildPendingOrdersCtesSql(ctx) + @"
                SELECT
                    po.C_Order_ID AS Order_Id,
                    po.Document_No AS Document_No,
                    COALESCE(bp.Name, N'') AS Customer_Name,
                    po.Date_Promised AS Date_Promised,
                    po.Order_Value AS Order_Value
                  FROM pending_orders po
                  INNER JOIN C_BPartner bp ON ( bp.C_BPartner_ID = po.Bpartner_Id )
                 ORDER BY po.Date_Promised ASC, po.Document_No ASC
                 OFFSET 0 ROWS FETCH NEXT @Preview_Count ROWS ONLY";

            IDataReader dr2 = null;
            try
            {
                dr2 = DB.ExecuteReader(previewSql, new[]
                {
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                    new SqlParameter("@Month_Start", monthStart),
                    new SqlParameter("@Month_End", monthEnd),
                    new SqlParameter("@Preview_Count", PreviewCount)
                });

                while (dr2 != null && dr2.Read())
                {
                    DateTime? datePromised = dr2["Date_Promised"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr2["Date_Promised"]);

                    result.Preview.Add(new PreviewRow
                    {
                        SalesOrderId = Util.GetValueOfInt(dr2["Order_Id"]),
                        SalesOrderNumber = Util.GetValueOfString(dr2["Document_No"]),
                        CustomerName = Util.GetValueOfString(dr2["Customer_Name"]),
                        PromisedDate = datePromised.HasValue ? datePromised.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                        OrderValue = dr2["Order_Value"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr2["Order_Value"])
                    });
                }
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
            if (hasLines && allDelivered) { deliveryStatusKey = "VAS_279_DeliveryFull"; deliveryStatusFallback = "Delivered"; }
            else if (anyDelivered) { deliveryStatusKey = "VAS_279_DeliveryPartial"; deliveryStatusFallback = "Partially delivered"; }
            else { deliveryStatusKey = "VAS_279_DeliveryNone"; deliveryStatusFallback = "Not delivered"; }
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
                    Log.Log(Level.SEVERE, "VAS_279_DueForDeliveryWidget.GetDecodeMap(" + tableName + "." + columnName + ")", ex);
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
            public int DueThisMonthCount { get; set; }
            public decimal DueThisMonthValue { get; set; }
            public int DueNext7DaysCount { get; set; }
            public List<PreviewRow> Preview { get; set; }
        }

        private class PreviewRow
        {
            public int SalesOrderId { get; set; }
            public string SalesOrderNumber { get; set; }
            public string CustomerName { get; set; }
            public string PromisedDate { get; set; }
            public decimal OrderValue { get; set; }
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
