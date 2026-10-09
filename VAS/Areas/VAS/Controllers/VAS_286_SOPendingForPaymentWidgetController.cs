/******************************************************
 * Module Name    : VAS
 * Purpose        : Sales Order dashboard "SO Pending for Payment" collection worklist widget endpoints
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
    /// Module Name : VAS_286_SOPendingForPaymentWidget
    /// Purpose     : Data endpoints for the 6x2 "SO Pending for Payment" wide worklist
    ///               widget on the Sales Order dashboard - Sales Orders with a real
    ///               outstanding payment obligation, plus the shared Sales Order
    ///               record-preview data for the record modal opened directly from a
    ///               row click.
    ///
    ///   Business definition per
    ///   19_SO_Pending_For_Payment_Claude_Development_Prompt.txt (CRITICAL USER
    ///   OVERRIDE - the paired HTML mock's "Delivered against SO, payment not yet
    ///   received" framing is NOT the production rule; no shipment/delivery
    ///   condition is required for either path below, so an advance-payment SO can
    ///   appear before any M_InOut exists):
    ///     A. INVOICED OUTSTANDING - one or more completed/closed ('CO'/'CL') sales
    ///        invoices linked to the order have C_Invoice.VA009_OpenAmount > 0. Amount
    ///        = SUM(VA009_OpenAmount) across those open invoices; due date = the
    ///        earliest DueDate among them.
    ///     B. ADVANCE OUTSTANDING - only when path A does not apply (an order with a
    ///        qualifying open invoice never also gets an Advance row - one SO appears
    ///        once only): the order's payment term has C_PaymentTerm.VA009_Advance =
    ///        'Y' and C_Order.GrandTotal minus completed ('CO'/'CL') customer receipts
    ///        (C_Payment.IsReceipt='Y') linked to that order is > 0. Amount = that
    ///        difference (GrandTotal, never TotalLines, because payment can include
    ///        tax/freight). Due date = C_Order.DateOrdered - the schema exposes no
    ///        separate advance-due date, so advance is treated as due from booking.
    ///     - Cohort excludes drafted/voided/reversed/in-process/not-active-related
    ///       document statuses (DocStatus NOT IN ('DR','VO','RE','IN','NA')) and, per
    ///       the general rules, non-quotation/non-return real Sales Orders only.
    ///     - Sorted payment-due-date ascending (oldest/most overdue first); "days
    ///       overdue" is a calendar-day difference computed here in C#, never in SQL
    ///       (cross-database rule - Oracle/PostgreSQL date arithmetic differs).
    ///     - The header "total due" chip sums the amount across the COMPLETE result
    ///       (a window aggregate computed before OFFSET/FETCH trims to the page), so
    ///       it always reconciles with the visible column.
    ///
    ///   Per the CTE/MRole rule (Prompt_Instructions.txt "Case 1"): the query isolates
    ///   a single "FROM C_Order o" WHERE-only fragment (no GROUP BY) and calls
    ///   AddAccessSQL ONLY on that isolated fragment - never on a fragment that already
    ///   carries its own GROUP BY. The invoice_due/received aggregates are separate
    ///   CTEs joined back to the already-filtered base_orders fragment, so access
    ///   control is inherited through that join rather than re-applied to them.
    ///   Read-only here; no writes.
    /// Chronological development:
    ///   VAI052      2026-09-15 Created
    /// </summary>
    public class VAS_286_SOPendingForPaymentWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_286_SOPendingForPaymentWidgetController).FullName);

        private const int DefaultPageSize = 6;
        private const int MaxPageSize = 10;
        private const int MaxLineRows = 200;

        /// <summary>
        /// One page of Sales Orders with a real outstanding payment obligation
        /// (invoiced or advance basis), plus the total due across the complete result.
        /// </summary>
        /// <param name="page">Zero-based page index.</param>
        /// <param name="size">Rows per page (capped at <see cref="MaxPageSize"/>).</param>
        /// <returns>JSON { TotalDue, Total, Rows:[...] } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetPendingPayment(int page = 0, int size = DefaultPageSize)
        {
            if (Session["ctx"] == null) { return Json("", JsonRequestBehavior.AllowGet); }

            Ctx ctx = Session["ctx"] as Ctx;
            if (page < 0) { page = 0; }
            if (size <= 0 || size > MaxPageSize) { size = DefaultPageSize; }

            try
            {
                string json = JsonConvert.SerializeObject(GetPendingPaymentData(ctx, page, size));
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_286_SOPendingForPaymentWidget.GetPendingPayment", ex);
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
                Log.Log(Level.SEVERE, "VAS_286_SOPendingForPaymentWidget.GetSalesOrderDetail", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// The single physical-table fragment - one "FROM C_Order o", WHERE-only, no
        /// GROUP BY (Prompt_Instructions.txt "Case 1"). The ONLY thing AddAccessSQL is
        /// ever applied to for this widget.
        /// </summary>
        private static string BuildBaseOrdersSql()
        {
            return @"
                SELECT o.C_Order_ID AS C_Order_ID,
                       o.DocumentNo AS Document_No,
                       o.DateOrdered AS Date_Ordered,
                       o.C_BPartner_ID AS BPartner_Id,
                       o.M_Warehouse_ID AS Warehouse_Id,
                       o.C_PaymentTerm_ID AS Payment_Term_Id,
                       o.GrandTotal AS Grand_Total
                  FROM C_Order o
                 WHERE o.AD_Client_ID = @AD_Client_ID
                   AND o.IsActive = 'Y'
                   AND o.IsSOTrx = 'Y'
                   AND COALESCE(o.IsSalesQuotation, 'N') = 'N'
                   AND COALESCE(o.IsReturnTrx, 'N') = 'N'
                   AND o.DocStatus NOT IN ('DR', 'VO', 'RE', 'IN', 'NA')";
        }

        private PendingPaymentResult GetPendingPaymentData(Ctx ctx, int page, int size)
        {
            PendingPaymentResult result = new PendingPaymentResult { Rows = new List<PendingPaymentRow>() };
            if (ctx == null) { return result; }

            string baseOrdersSql = MRole.GetDefault(ctx).AddAccessSQL(BuildBaseOrdersSql(), "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string sql = @"
                WITH base_orders AS (" + baseOrdersSql + @"),
                invoice_due AS (
                    SELECT
                        i.C_Order_ID AS C_Order_ID,
                        MIN(i.DueDate) AS Payment_Due_Date,
                        SUM(COALESCE(i.VA009_OpenAmount, 0)) AS Open_Amount
                      FROM C_Invoice i
                      INNER JOIN base_orders bo ON ( bo.C_Order_ID = i.C_Order_ID )
                     WHERE i.IsActive = 'Y'
                       AND i.IsSOTrx = 'Y'
                       AND COALESCE(i.IsReturnTrx, 'N') = 'N'
                       AND i.DocStatus IN ('CO', 'CL')
                       AND COALESCE(i.VA009_OpenAmount, 0) > 0
                     GROUP BY i.C_Order_ID
                ),
                received AS (
                    SELECT
                        p.C_Order_ID AS C_Order_ID,
                        SUM(COALESCE(p.PayAmt, 0)) AS Received_Amount
                      FROM C_Payment p
                      INNER JOIN base_orders bo ON ( bo.C_Order_ID = p.C_Order_ID )
                     WHERE p.IsActive = 'Y'
                       AND p.DocStatus IN ('CO', 'CL')
                       AND p.IsReceipt = 'Y'
                       AND p.IsSOTrx = 'Y'
                     GROUP BY p.C_Order_ID
                ),
                candidate AS (
                    SELECT
                        bo.C_Order_ID AS C_Order_ID,
                        bo.Document_No AS Document_No,
                        bo.Date_Ordered AS Date_Ordered,
                        bo.BPartner_Id AS BPartner_Id,
                        bo.Warehouse_Id AS Warehouse_Id,
                        bo.Grand_Total AS Grand_Total,
                        pt.VA009_Advance AS Advance_Flag,
                        inv.C_Order_ID AS Invoice_Order_Id,
                        inv.Payment_Due_Date AS Invoice_Due_Date,
                        inv.Open_Amount AS Invoice_Open_Amount,
                        COALESCE(r.Received_Amount, 0) AS Received_Amount
                      FROM base_orders bo
                      LEFT OUTER JOIN C_PaymentTerm pt ON ( pt.C_PaymentTerm_ID = bo.Payment_Term_Id )
                      LEFT OUTER JOIN invoice_due inv ON ( inv.C_Order_ID = bo.C_Order_ID )
                      LEFT OUTER JOIN received r ON ( r.C_Order_ID = bo.C_Order_ID )
                )
                SELECT
                    c.C_Order_ID AS Order_Id,
                    c.Document_No AS Document_No,
                    c.Date_Ordered AS Date_Ordered,
                    COALESCE(bp.Name, N'') AS Customer_Name,
                    COALESCE(w.Name, N'') AS Warehouse_Name,
                    CASE WHEN c.Invoice_Order_Id IS NOT NULL THEN 'Invoice' ELSE 'Advance' END AS Payment_Type,
                    CASE WHEN c.Invoice_Order_Id IS NOT NULL THEN c.Invoice_Due_Date ELSE c.Date_Ordered END AS Payment_Due_Date,
                    CASE WHEN c.Invoice_Order_Id IS NOT NULL THEN c.Invoice_Open_Amount
                         ELSE (c.Grand_Total - c.Received_Amount)
                    END AS Amount,
                    COUNT(1) OVER () AS Total_Rows,
                    SUM(
                        CASE WHEN c.Invoice_Order_Id IS NOT NULL THEN c.Invoice_Open_Amount
                             ELSE (c.Grand_Total - c.Received_Amount)
                        END
                    ) OVER () AS Total_Due
                  FROM candidate c
                  INNER JOIN C_BPartner bp ON ( bp.C_BPartner_ID = c.BPartner_Id )
                  LEFT OUTER JOIN M_Warehouse w ON ( w.M_Warehouse_ID = c.Warehouse_Id )
                 WHERE c.Invoice_Open_Amount > 0
                    OR (
                        c.Invoice_Order_Id IS NULL
                        AND COALESCE(c.Advance_Flag, 'N') = 'Y'
                        AND (c.Grand_Total - c.Received_Amount) > 0
                    )
                 ORDER BY
                    CASE WHEN c.Invoice_Order_Id IS NOT NULL THEN c.Invoice_Due_Date ELSE c.Date_Ordered END ASC,
                    c.Document_No ASC
                 OFFSET @Row_Offset ROWS FETCH NEXT @Page_Size ROWS ONLY";

            DateTime today = DateTime.Today;

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, new[]
                {
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                    new SqlParameter("@Row_Offset", page * size),
                    new SqlParameter("@Page_Size", size)
                });

                int total = 0;
                decimal totalDue = 0m;
                while (dr != null && dr.Read())
                {
                    total = dr["Total_Rows"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Total_Rows"]);
                    totalDue = dr["Total_Due"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Total_Due"]);

                    DateTime? dateOrdered = dr["Date_Ordered"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr["Date_Ordered"]);
                    DateTime? paymentDueDate = dr["Payment_Due_Date"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr["Payment_Due_Date"]);

                    bool isOverdue = paymentDueDate.HasValue && paymentDueDate.Value.Date < today;
                    int daysOverdue = isOverdue ? (today - paymentDueDate.Value.Date).Days : 0;

                    result.Rows.Add(new PendingPaymentRow
                    {
                        SalesOrderId = Util.GetValueOfInt(dr["Order_Id"]),
                        SalesOrderNumber = Util.GetValueOfString(dr["Document_No"]),
                        SalesOrderDate = dateOrdered.HasValue ? dateOrdered.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                        CustomerName = Util.GetValueOfString(dr["Customer_Name"]),
                        WarehouseName = Util.GetValueOfString(dr["Warehouse_Name"]),
                        PaymentType = Util.GetValueOfString(dr["Payment_Type"]),
                        PaymentDueDate = paymentDueDate.HasValue ? paymentDueDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                        IsOverdue = isOverdue,
                        DaysOverdue = daysOverdue,
                        Amount = dr["Amount"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Amount"])
                    });
                }
                result.Total = total;
                result.TotalDue = totalDue;
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
            if (hasLines && allDelivered) { deliveryStatusKey = "VAS_286_DeliveryFull"; deliveryStatusFallback = "Delivered"; }
            else if (anyDelivered) { deliveryStatusKey = "VAS_286_DeliveryPartial"; deliveryStatusFallback = "Partially delivered"; }
            else { deliveryStatusKey = "VAS_286_DeliveryNone"; deliveryStatusFallback = "Not delivered"; }
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
                    Log.Log(Level.SEVERE, "VAS_286_SOPendingForPaymentWidget.GetDecodeMap(" + tableName + "." + columnName + ")", ex);
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

        private class PendingPaymentResult
        {
            public decimal TotalDue { get; set; }
            public int Total { get; set; }
            public List<PendingPaymentRow> Rows { get; set; }
        }

        private class PendingPaymentRow
        {
            public int SalesOrderId { get; set; }
            public string SalesOrderNumber { get; set; }
            public string SalesOrderDate { get; set; }
            public string CustomerName { get; set; }
            public string WarehouseName { get; set; }
            public string PaymentType { get; set; }
            public string PaymentDueDate { get; set; }
            public bool IsOverdue { get; set; }
            public int DaysOverdue { get; set; }
            public decimal Amount { get; set; }
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
