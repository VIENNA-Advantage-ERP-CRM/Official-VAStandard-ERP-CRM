/******************************************************
 * Module Name    : VAS
 * Purpose        : Sales Order dashboard "SO Queue" primary operational work-queue widget endpoints
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
    /// Module Name : VAS_288_SOQueueWidget
    /// Purpose     : Data endpoints for the 6x3 "SO Queue" widget on the Sales Order
    ///               dashboard - the operational list of live Sales Orders for a
    ///               selected promised-delivery Month/Year, sorted by DatePromised
    ///               ascending (the next thing due always leads page 1), seven rows
    ///               per page. Row click opens the shared Sales Order record-preview
    ///               modal directly - there is no intermediate list.
    ///
    ///   Business definition per 21_SO_Queue_Claude_Development_Prompt.txt
    ///   (CONFIRMED - overrides the paired mock's generic "SO No/SO date/Customer/
    ///   Ship from/Quotation/Representative/Promised/Value/Status" labels with exact
    ///   schema terminology, per the prompt's explicit "UI FIELD NAMES" section):
    ///     - Live Sales Order = IsActive='Y', IsSOTrx='Y', non-return, non-quotation,
    ///       DocStatus IN ('DR','IP','CO') only - Closed ('CL'), Voided ('VO') and
    ///       Reversed ('RE') are excluded entirely (this is an operational work
    ///       queue, not a historical value report).
    ///     - Period filters C_Order.DatePromised (never DateOrdered) - the confirmed
    ///       promise, consistent with the overdue and delivery-performance widgets.
    ///     - Document Status is the RAW C_Order.DocStatus vocabulary only (Drafted /
    ///       In Progress / Completed) - never a delivery-progress label like "Partly
    ///       Delivered"/"Delivered". Delivery progress is a different concept that
    ///       still exists inside the shared record modal's own "Delivery status"
    ///       stat card, but the two must never be conflated in this widget's own
    ///       Status column.
    ///     - Quotation No. comes from C_Order.C_Order_Quotation -&gt; the source
    ///       quotation's own DocumentNo (a self-join on C_Order) - never inferred by
    ///       matching customer/date. Renders as null (client shows an em dash) for a
    ///       direct order with no source quotation.
    ///     - Sales Rep is the ORDER-LEVEL C_Order.SalesRep_ID -&gt; AD_User.Name -
    ///       never substituted with the Business Partner's assigned salesperson.
    ///     - SubTotal = C_Order.TotalLines (pre-tax) - GrandTotal is never used for
    ///       this column.
    ///     - C_OrderLine is intentionally NOT joined in this query (the prompt's own
    ///       "Do not add extra visible columns" / "Do not load all order-line data
    ///       during initial dashboard load" rules) - line detail is fetched only when
    ///       a row is opened, via the shared GetSalesOrderDetail endpoint.
    ///
    ///   Per the CTE/MRole rule (Prompt_Instructions.txt "Case 1"): the query isolates
    ///   a single "FROM C_Order o" WHERE-only fragment (no GROUP BY) and calls
    ///   AddAccessSQL ONLY on that isolated fragment; the BPartner/Warehouse/AD_User/
    ///   quotation-self-join joins happen in an outer SELECT built around the
    ///   already-filtered text - kept out of the access-filtered fragment itself so a
    ///   second C_Order reference (the quotation self-join) can never confuse the
    ///   access-SQL parser's table/alias resolution.
    ///   Read-only here; no writes.
    /// Chronological development:
    ///   VAI052      2026-09-15 Created
    /// </summary>
    public class VAS_288_SOQueueWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_288_SOQueueWidgetController).FullName);

        private const int DefaultPageSize = 6;
        private const int MaxPageSize = 10;
        private const int MaxLineRows = 200;

        /// <summary>
        /// One page of live Sales Orders promised in the selected month, sorted by
        /// promised date ascending.
        /// </summary>
        /// <param name="month">Zero-based month (0=Jan..11=Dec).</param>
        /// <param name="year">Calendar year.</param>
        /// <param name="page">Zero-based page index.</param>
        /// <param name="size">Rows per page (capped at <see cref="MaxPageSize"/>).</param>
        /// <returns>JSON { Total, Rows:[...] } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetQueue(int month, int year, int page = 0, int size = DefaultPageSize)
        {
            if (Session["ctx"] == null) { return Json("", JsonRequestBehavior.AllowGet); }

            Ctx ctx = Session["ctx"] as Ctx;
            if (page < 0) { page = 0; }
            if (size <= 0 || size > MaxPageSize) { size = DefaultPageSize; }

            try
            {
                string json = JsonConvert.SerializeObject(GetQueueData(ctx, month, year, page, size));
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_288_SOQueueWidget.GetQueue", ex);
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
                Log.Log(Level.SEVERE, "VAS_288_SOQueueWidget.GetSalesOrderDetail", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>Resolves the zero-based month/year filter (defaulting to the current month) into an inclusive/exclusive date pair.</summary>
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
        /// ever applied to for this widget. Cohort = live Sales Orders (DR/IP/CO)
        /// promised in the given half-open period.
        ///
        /// Quotation_Order_Id is C_Order.C_Order_Quotation - VARCHAR2(22) /
        /// character varying holding the quotation's C_Order_ID as TEXT - so the
        /// outer query's join to the quotation casts the NUMBER side
        /// (18-Sep-2026): compared raw, PostgreSQL rejects the statement
        /// ("operator does not exist: numeric = character varying") and Oracle
        /// converts the text implicitly, raising ORA-01722 on any non-numeric
        /// value. CAST(... AS VARCHAR(22)) reads on both.
        /// </summary>
        private static string BuildBaseOrdersSql()
        {
            return @"
                SELECT o.C_Order_ID AS C_Order_ID,
                       o.DocumentNo AS Document_No,
                       o.DateOrdered AS Date_Ordered,
                       o.C_BPartner_ID AS BPartner_Id,
                       o.M_Warehouse_ID AS Warehouse_Id,
                       o.C_Order_Quotation AS Quotation_Order_Id,
                       o.SalesRep_ID AS Sales_Rep_Id,
                       o.DatePromised AS Date_Promised,
                       o.TotalLines AS Sub_Total,
                       o.DocStatus AS Doc_Status_Code
                  FROM C_Order o
                 WHERE o.AD_Client_ID = @AD_Client_ID
                   AND o.IsActive = 'Y'
                   AND o.IsSOTrx = 'Y'
                   AND COALESCE(o.IsReturnTrx, 'N') = 'N'
                   AND COALESCE(o.IsSalesQuotation, 'N') = 'N'
                   AND o.DocStatus IN ('DR', 'IP', 'CO')
                   AND o.DatePromised >= @Period_Start
                   AND o.DatePromised < @Period_End";
        }

        private QueueResult GetQueueData(Ctx ctx, int month, int year, int page, int size)
        {
            QueueResult result = new QueueResult { Rows = new List<QueueRow>() };
            if (ctx == null) { return result; }

            DateTime periodStart, periodEnd;
            ResolvePeriod(month, year, out periodStart, out periodEnd);

            string baseOrdersSql = MRole.GetDefault(ctx).AddAccessSQL(BuildBaseOrdersSql(), "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string sql = @"
                WITH base_orders AS (" + baseOrdersSql + @")
                SELECT
                    bo.C_Order_ID AS Order_Id,
                    bo.Document_No AS Document_No,
                    bo.Date_Ordered AS Date_Ordered,
                    COALESCE(bp.Name, N'') AS Business_Partner,
                    COALESCE(wh.Name, N'') AS Warehouse,
                    q.DocumentNo AS Quotation_No,
                    COALESCE(rep.Name, N'') AS Sales_Rep,
                    bo.Date_Promised AS Date_Promised,
                    bo.Sub_Total AS Sub_Total,
                    bo.Doc_Status_Code AS Doc_Status_Code,
                    COUNT(1) OVER () AS Total_Rows
                  FROM base_orders bo
                  INNER JOIN C_BPartner bp ON ( bp.C_BPartner_ID = bo.BPartner_Id )
                  LEFT OUTER JOIN M_Warehouse wh ON ( wh.M_Warehouse_ID = bo.Warehouse_Id )
                  LEFT OUTER JOIN AD_User rep ON ( rep.AD_User_ID = bo.Sales_Rep_Id )
                  LEFT OUTER JOIN C_Order q ON ( CAST(q.C_Order_ID AS VARCHAR(22)) = TRIM(bo.Quotation_Order_Id) )
                 ORDER BY bo.Date_Promised ASC, bo.Document_No ASC
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
                    DateTime? dateOrdered = dr["Date_Ordered"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr["Date_Ordered"]);
                    DateTime? datePromised = dr["Date_Promised"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr["Date_Promised"]);
                    string quotationNo = dr["Quotation_No"] == DBNull.Value ? null : Util.GetValueOfString(dr["Quotation_No"]);

                    result.Rows.Add(new QueueRow
                    {
                        SalesOrderId = Util.GetValueOfInt(dr["Order_Id"]),
                        DocumentNo = Util.GetValueOfString(dr["Document_No"]),
                        DateOrdered = dateOrdered.HasValue ? dateOrdered.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                        BusinessPartner = Util.GetValueOfString(dr["Business_Partner"]),
                        Warehouse = Util.GetValueOfString(dr["Warehouse"]),
                        QuotationNo = string.IsNullOrEmpty(quotationNo) ? null : quotationNo,
                        SalesRep = Util.GetValueOfString(dr["Sales_Rep"]),
                        DatePromised = datePromised.HasValue ? datePromised.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                        SubTotal = dr["Sub_Total"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Sub_Total"]),
                        DocumentStatus = Util.GetValueOfString(dr["Doc_Status_Code"])
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
            if (hasLines && allDelivered) { deliveryStatusKey = "VAS_288_DeliveryFull"; deliveryStatusFallback = "Delivered"; }
            else if (anyDelivered) { deliveryStatusKey = "VAS_288_DeliveryPartial"; deliveryStatusFallback = "Partially delivered"; }
            else { deliveryStatusKey = "VAS_288_DeliveryNone"; deliveryStatusFallback = "Not delivered"; }
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
                    Log.Log(Level.SEVERE, "VAS_288_SOQueueWidget.GetDecodeMap(" + tableName + "." + columnName + ")", ex);
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

        private class QueueResult
        {
            public int Total { get; set; }
            public List<QueueRow> Rows { get; set; }
        }

        private class QueueRow
        {
            public int SalesOrderId { get; set; }
            public string DocumentNo { get; set; }
            public string DateOrdered { get; set; }
            public string BusinessPartner { get; set; }
            public string Warehouse { get; set; }
            public string QuotationNo { get; set; }
            public string SalesRep { get; set; }
            public string DatePromised { get; set; }
            public decimal SubTotal { get; set; }
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
