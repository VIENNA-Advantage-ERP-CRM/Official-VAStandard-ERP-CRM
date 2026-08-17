/************************************************************
 * Module Name    : VAS
 * Purpose        : Controller for POs Pending Delivery Widget (Widget 04)
 *                  Operational Purchase Order chase list: Completed Purchase Orders
 *                  (DocStatus = 'CO') with remaining undelivered quantity (QtyOrdered > QtyDelivered).
 * Chronological Development:
 *   2026-08-17   : Created
 ***********************************************************/

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Web.Mvc;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VIS.Controllers
{
    /// <summary>
    /// Controller for Widget 04: POs Pending Delivery (VAS_194_POsPendingDeliveryWidget)
    /// </summary>
    public class VAS_194_POsPendingDeliveryWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_194_POsPendingDeliveryWidgetController).FullName);

        [Authorize]
        public ActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// Retrieves the KPI metrics and all open completed purchase orders with pending delivery quantities.
        /// </summary>
        /// <returns>JSON object containing summary stats, currency schema info, and PO records.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetPOsPendingDelivery()
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null)
            {
                return Json(JsonConvert.SerializeObject(new { error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired", success = false }), JsonRequestBehavior.AllowGet);
            }

            try
            {
                int clientId = ctx.GetAD_Client_ID();

                // Step 1: Resolve Client Accounting Schema Currency
                int schemaCurrencyId = 0;
                string schemaCurSymbol = "";
                string schemaCurIso = "";
                int schemaStdPrecision = 2;

                string curSql = @"SELECT cs.C_Currency_ID, c.CurSymbol, c.ISO_Code, c.StdPrecision
                                  FROM C_AcctSchema cs
                                  INNER JOIN AD_ClientInfo ci ON (ci.C_AcctSchema1_ID = cs.C_AcctSchema_ID)
                                  INNER JOIN C_Currency c ON (cs.C_Currency_ID = c.C_Currency_ID)
                                  WHERE ci.AD_Client_ID = " + clientId + @"
                                    AND ci.IsActive = 'Y'
                                    AND cs.IsActive = 'Y'
                                    AND c.IsActive = 'Y'";

                curSql = MRole.GetDefault(ctx).AddAccessSQL(curSql, "cs", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                IDataReader curDr = null;
                try
                {
                    curDr = DB.ExecuteReader(curSql);
                    if (curDr != null && curDr.Read())
                    {
                        schemaCurrencyId = Util.GetValueOfInt(curDr["C_Currency_ID"]);
                        schemaCurSymbol = Util.GetValueOfString(curDr["CurSymbol"]);
                        schemaCurIso = Util.GetValueOfString(curDr["ISO_Code"]);
                        schemaStdPrecision = Util.GetValueOfInt(curDr["StdPrecision"]);
                    }
                }
                finally
                {
                    if (curDr != null) { curDr.Close(); curDr.Dispose(); }
                }

                if (schemaCurrencyId == 0)
                {
                    schemaCurrencyId = ctx.GetContextAsInt("$C_Currency_ID");
                }

                // Step 2: Query Operational Completed POs with Pending Delivery Lines
                DateTime today = DateTime.Today;

                string sql = @"
                    SELECT
                        o.C_Order_ID AS purchase_order_id,
                        o.DocumentNo AS purchase_order_number,
                        o.DateOrdered AS order_date,
                        o.DatePromised AS promised_date,
                        o.C_Currency_ID AS currency_id,
                        o.C_ConversionType_ID AS conversion_type_id,
                        o.AD_Org_ID AS org_id,
                        bp.Name AS vendor_name,
                        w.Name AS warehouse_name,
                        c.CurSymbol AS doc_cur_symbol,
                        c.ISO_Code AS doc_cur_iso,
                        SUM(COALESCE(ol.QtyOrdered, 0)) AS ordered_qty,
                        SUM(COALESCE(ol.QtyDelivered, 0)) AS delivered_qty,
                        SUM(
                            CASE
                                WHEN COALESCE(ol.QtyOrdered, 0) > COALESCE(ol.QtyDelivered, 0)
                                THEN COALESCE(ol.QtyOrdered, 0) - COALESCE(ol.QtyDelivered, 0)
                                ELSE 0
                            END
                        ) AS pending_qty,
                        SUM(
                            CASE
                                WHEN COALESCE(ol.QtyOrdered, 0) > COALESCE(ol.QtyDelivered, 0)
                                THEN (COALESCE(ol.QtyOrdered, 0) - COALESCE(ol.QtyDelivered, 0)) * COALESCE(ol.PriceActual, 0)
                                ELSE 0
                            END
                        ) AS pending_value_document_currency,
                        SUM(COALESCE(ol.LineNetAmt, COALESCE(ol.QtyOrdered, 0) * COALESCE(ol.PriceActual, 0))) AS total_order_value,
                        COUNT(ol.C_OrderLine_ID) AS line_count
                    FROM C_Order o
                    INNER JOIN C_OrderLine ol
                        ON ol.C_Order_ID = o.C_Order_ID
                       AND ol.IsActive = 'Y'
                    INNER JOIN C_BPartner bp
                        ON bp.C_BPartner_ID = o.C_BPartner_ID
                    LEFT JOIN M_Warehouse w
                        ON w.M_Warehouse_ID = o.M_Warehouse_ID
                    LEFT JOIN C_Currency c
                        ON c.C_Currency_ID = o.C_Currency_ID
                    WHERE o.AD_Client_ID = " + clientId + @"
                      AND o.IsActive = 'Y'
                      AND o.IsSOTrx = 'N'
                      AND COALESCE(o.IsReturnTrx, 'N') = 'N'
                      AND o.DocStatus = 'CO'
                    GROUP BY
                        o.C_Order_ID,
                        o.DocumentNo,
                        o.DateOrdered,
                        o.DatePromised,
                        o.C_Currency_ID,
                        o.C_ConversionType_ID,
                        o.AD_Org_ID,
                        bp.Name,
                        w.Name,
                        c.CurSymbol,
                        c.ISO_Code
                    HAVING SUM(
                        CASE
                            WHEN COALESCE(ol.QtyOrdered, 0) > COALESCE(ol.QtyDelivered, 0)
                            THEN COALESCE(ol.QtyOrdered, 0) - COALESCE(ol.QtyDelivered, 0)
                            ELSE 0
                        END
                    ) > 0
                    ORDER BY o.DatePromised ASC, o.DateOrdered ASC, o.DocumentNo ASC";

                sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                var poRecords = new List<object>();
                int openPOsCount = 0;
                decimal itemsPendingCount = 0;
                decimal totalUndeliveredValueConverted = 0;
                int pastDueCount = 0;

                IDataReader dr = null;
                try
                {
                    dr = DB.ExecuteReader(sql);
                    while (dr != null && dr.Read())
                    {
                        int poId = Util.GetValueOfInt(dr["purchase_order_id"]);
                        string poNo = Util.GetValueOfString(dr["purchase_order_number"]);
                        DateTime? orderDate = Util.GetValueOfDateTime(dr["order_date"]);
                        DateTime? promisedDate = Util.GetValueOfDateTime(dr["promised_date"]);
                        int orderCurId = Util.GetValueOfInt(dr["currency_id"]);
                        int convTypeId = Util.GetValueOfInt(dr["conversion_type_id"]);
                        int orgId = Util.GetValueOfInt(dr["org_id"]);
                        string vendorName = Util.GetValueOfString(dr["vendor_name"]);
                        string warehouseName = Util.GetValueOfString(dr["warehouse_name"]);
                        string docCurSymbol = Util.GetValueOfString(dr["doc_cur_symbol"]);
                        string docCurIso = Util.GetValueOfString(dr["doc_cur_iso"]);

                        decimal orderedQty = Util.GetValueOfDecimal(dr["ordered_qty"]);
                        decimal deliveredQty = Util.GetValueOfDecimal(dr["delivered_qty"]);
                        decimal pendingQty = Util.GetValueOfDecimal(dr["pending_qty"]);
                        decimal pendingValueDoc = Util.GetValueOfDecimal(dr["pending_value_document_currency"]);
                        decimal totalOrderVal = Util.GetValueOfDecimal(dr["total_order_value"]);
                        int lineCount = Util.GetValueOfInt(dr["line_count"]);

                        // Convert pending value to schema currency
                        decimal convertedPendingValue = pendingValueDoc;
                        if (orderCurId != schemaCurrencyId && schemaCurrencyId > 0 && pendingValueDoc != 0)
                        {
                            try
                            {
                                decimal conv = MCurrency.CurrencyConvert(
                                    pendingValueDoc,
                                    orderCurId,
                                    schemaCurrencyId,
                                    orderDate.HasValue ? orderDate.Value : today,
                                    convTypeId,
                                    clientId,
                                    orgId);
                                if (conv != 0)
                                {
                                    convertedPendingValue = conv;
                                }
                            }
                            catch (Exception exConv)
                            {
                                Log.Log(Level.WARNING, "Currency conversion failed for PO " + poNo, exConv);
                            }
                        }

                        bool isPastDue = promisedDate.HasValue && promisedDate.Value.Date < today && pendingQty > 0;

                        string deliveryStatus = deliveredQty > 0 ? "Partial" : "Pending";
                        string deliveryStatusKey = deliveredQty > 0 ? "VAS_DeliveryStatusPartial" : "VAS_DeliveryStatusPending";
                        string deliveryStatusChip = deliveredQty > 0 ? "chip-warn" : "chip-neutral";

                        openPOsCount++;
                        itemsPendingCount += pendingQty;
                        totalUndeliveredValueConverted += convertedPendingValue;
                        if (isPastDue)
                        {
                            pastDueCount++;
                        }

                        poRecords.Add(new
                        {
                            PurchaseOrderID = poId,
                            PurchaseOrderNo = poNo,
                            OrderDate = orderDate.HasValue ? orderDate.Value.ToString("yyyy-MM-dd") : "",
                            OrderDateDisplay = orderDate.HasValue ? orderDate.Value.ToString("dd MMM yyyy") : "",
                            PromisedDate = promisedDate.HasValue ? promisedDate.Value.ToString("yyyy-MM-dd") : "",
                            PromisedDateDisplay = promisedDate.HasValue ? promisedDate.Value.ToString("dd MMM yyyy") : "",
                            VendorName = vendorName,
                            WarehouseName = warehouseName,
                            OrderedQty = orderedQty,
                            DeliveredQty = deliveredQty,
                            PendingQty = pendingQty,
                            PendingValue = pendingValueDoc,
                            PendingValueConverted = convertedPendingValue,
                            TotalOrderValue = totalOrderVal,
                            CurrencyID = orderCurId,
                            CurrencySymbol = docCurSymbol,
                            CurrencyISO = docCurIso,
                            IsPastDue = isPastDue,
                            LineCount = lineCount,
                            DeliveryStatus = deliveryStatus,
                            DeliveryStatusKey = deliveryStatusKey,
                            DeliveryStatusChip = deliveryStatusChip,
                            DocStatus = "Completed",
                            DocStatusKey = "VAS_DocStatusCompleted",
                            DocStatusChip = "chip-ok"
                        });
                    }
                }
                finally
                {
                    if (dr != null) { dr.Close(); dr.Dispose(); }
                }

                var result = new
                {
                    success = true,
                    openPOs = openPOsCount,
                    itemsPending = itemsPendingCount,
                    undeliveredValue = totalUndeliveredValueConverted,
                    pastDue = pastDueCount,
                    baseCurrency = new
                    {
                        CurrencyID = schemaCurrencyId,
                        CurSymbol = schemaCurSymbol,
                        ISO_Code = schemaCurIso,
                        StdPrecision = schemaStdPrecision
                    },
                    records = poRecords
                };

                return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_194_POsPendingDeliveryWidget.GetPOsPendingDelivery", ex);
                return Json(JsonConvert.SerializeObject(new { error = Msg.GetMsg(ctx, "Error") ?? "Error", success = false }), JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Retrieves the line items for a specific Purchase Order drill-down.
        /// </summary>
        /// <param name="C_Order_ID">Purchase Order primary key.</param>
        /// <returns>JSON list of line details.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetPOLines(int C_Order_ID)
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null)
            {
                return Json(JsonConvert.SerializeObject(new { error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired", success = false }), JsonRequestBehavior.AllowGet);
            }

            try
            {
                string sql = @"
                    SELECT
                        ol.C_OrderLine_ID AS line_id,
                        ol.Line AS line_no,
                        p.Name AS product_name,
                        p.Value AS product_sku,
                        asi.Description AS attribute_desc,
                        COALESCE(u.UOMSymbol, u.Name) AS uom_name,
                        COALESCE(ol.QtyOrdered, 0) AS ordered_qty,
                        COALESCE(ol.QtyDelivered, 0) AS delivered_qty,
                        CASE
                            WHEN COALESCE(ol.QtyOrdered, 0) > COALESCE(ol.QtyDelivered, 0)
                            THEN COALESCE(ol.QtyOrdered, 0) - COALESCE(ol.QtyDelivered, 0)
                            ELSE 0
                        END AS pending_qty,
                        COALESCE(ol.PriceActual, 0) AS price_actual,
                        COALESCE(ol.LineNetAmt, COALESCE(ol.QtyOrdered, 0) * COALESCE(ol.PriceActual, 0)) AS line_net_amt,
                        c.CurSymbol AS cur_symbol,
                        c.ISO_Code AS cur_iso,
                        c.StdPrecision AS std_precision
                    FROM C_OrderLine ol
                    INNER JOIN C_Order o ON o.C_Order_ID = ol.C_Order_ID
                    LEFT JOIN M_Product p ON p.M_Product_ID = ol.M_Product_ID
                    LEFT JOIN C_UOM u ON u.C_UOM_ID = ol.C_UOM_ID
                    LEFT JOIN M_AttributeSetInstance asi ON asi.M_AttributeSetInstance_ID = ol.M_AttributeSetInstance_ID
                    LEFT JOIN C_Currency c ON c.C_Currency_ID = o.C_Currency_ID
                    WHERE ol.C_Order_ID = " + C_Order_ID + @"
                      AND ol.IsActive = 'Y'
                    ORDER BY ol.Line ASC, ol.C_OrderLine_ID ASC";

                sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "ol", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                var lines = new List<object>();
                IDataReader dr = null;
                try
                {
                    dr = DB.ExecuteReader(sql);
                    while (dr != null && dr.Read())
                    {
                        decimal orderedQty = Util.GetValueOfDecimal(dr["ordered_qty"]);
                        decimal deliveredQty = Util.GetValueOfDecimal(dr["delivered_qty"]);
                        decimal pendingQty = Util.GetValueOfDecimal(dr["pending_qty"]);
                        decimal priceActual = Util.GetValueOfDecimal(dr["price_actual"]);
                        decimal lineNetAmt = Util.GetValueOfDecimal(dr["line_net_amt"]);

                        string lineStatus = "Pending";
                        string lineStatusKey = "VAS_LineStatusPending";
                        string lineStatusChip = "chip-neutral";

                        if (deliveredQty >= orderedQty && orderedQty > 0)
                        {
                            lineStatus = "Received";
                            lineStatusKey = "VAS_LineStatusReceived";
                            lineStatusChip = "chip-ok";
                        }
                        else if (deliveredQty > 0 && deliveredQty < orderedQty)
                        {
                            lineStatus = "Partial received";
                            lineStatusKey = "VAS_LineStatusPartialReceived";
                            lineStatusChip = "chip-warn";
                        }

                        lines.Add(new
                        {
                            LineID = Util.GetValueOfInt(dr["line_id"]),
                            LineNo = Util.GetValueOfInt(dr["line_no"]),
                            ProductName = Util.GetValueOfString(dr["product_name"]),
                            ProductSKU = Util.GetValueOfString(dr["product_sku"]),
                            Attribute = Util.GetValueOfString(dr["attribute_desc"]),
                            UOM = Util.GetValueOfString(dr["uom_name"]),
                            OrderedQty = orderedQty,
                            DeliveredQty = deliveredQty,
                            PendingQty = pendingQty,
                            PriceActual = priceActual,
                            LineNetAmt = lineNetAmt,
                            LineStatus = lineStatus,
                            LineStatusKey = lineStatusKey,
                            LineStatusChip = lineStatusChip,
                            CurrencySymbol = Util.GetValueOfString(dr["cur_symbol"]),
                            CurrencyISO = Util.GetValueOfString(dr["cur_iso"]),
                            StdPrecision = Util.GetValueOfInt(dr["std_precision"])
                        });
                    }
                }
                finally
                {
                    if (dr != null) { dr.Close(); dr.Dispose(); }
                }

                return Json(JsonConvert.SerializeObject(new { success = true, lines = lines }), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_194_POsPendingDeliveryWidget.GetPOLines", ex);
                return Json(JsonConvert.SerializeObject(new { error = Msg.GetMsg(ctx, "Error") ?? "Error", success = false }), JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Retrieves full Purchase Order header and lines detail for the record modal.
        /// </summary>
        /// <param name="C_Order_ID">Purchase Order primary key.</param>
        /// <returns>JSON object with header info and lines.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetPODetail(int C_Order_ID)
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null)
            {
                return Json(JsonConvert.SerializeObject(new { error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired", success = false }), JsonRequestBehavior.AllowGet);
            }

            try
            {
                string headerSql = @"
                    SELECT
                        o.C_Order_ID AS purchase_order_id,
                        o.DocumentNo AS purchase_order_number,
                        o.DateOrdered AS order_date,
                        o.DatePromised AS promised_date,
                        o.DocStatus AS doc_status,
                        bp.Name AS vendor_name,
                        w.Name AS warehouse_name,
                        u.Name AS created_by_name,
                        o.Created AS created_on,
                        pt.Name AS payment_term_name,
                        c.CurSymbol AS cur_symbol,
                        c.ISO_Code AS cur_iso,
                        c.StdPrecision AS std_precision
                    FROM C_Order o
                    INNER JOIN C_BPartner bp ON bp.C_BPartner_ID = o.C_BPartner_ID
                    LEFT JOIN M_Warehouse w ON w.M_Warehouse_ID = o.M_Warehouse_ID
                    LEFT JOIN AD_User u ON u.AD_User_ID = o.CreatedBy
                    LEFT JOIN C_PaymentTerm pt ON pt.C_PaymentTerm_ID = o.C_PaymentTerm_ID
                    LEFT JOIN C_Currency c ON c.C_Currency_ID = o.C_Currency_ID
                    WHERE o.C_Order_ID = " + C_Order_ID;

                headerSql = MRole.GetDefault(ctx).AddAccessSQL(headerSql, "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                object headerObj = null;
                IDataReader hdrDr = null;
                try
                {
                    hdrDr = DB.ExecuteReader(headerSql);
                    if (hdrDr != null && hdrDr.Read())
                    {
                        DateTime? orderDate = Util.GetValueOfDateTime(hdrDr["order_date"]);
                        DateTime? promisedDate = Util.GetValueOfDateTime(hdrDr["promised_date"]);
                        DateTime? createdOn = Util.GetValueOfDateTime(hdrDr["created_on"]);

                        headerObj = new
                        {
                            PurchaseOrderID = Util.GetValueOfInt(hdrDr["purchase_order_id"]),
                            PurchaseOrderNo = Util.GetValueOfString(hdrDr["purchase_order_number"]),
                            OrderDate = orderDate.HasValue ? orderDate.Value.ToString("yyyy-MM-dd") : "",
                            OrderDateDisplay = orderDate.HasValue ? orderDate.Value.ToString("dd MMM yyyy") : "",
                            PromisedDate = promisedDate.HasValue ? promisedDate.Value.ToString("yyyy-MM-dd") : "",
                            PromisedDateDisplay = promisedDate.HasValue ? promisedDate.Value.ToString("dd MMM yyyy") : "",
                            DocStatus = Util.GetValueOfString(hdrDr["doc_status"]),
                            VendorName = Util.GetValueOfString(hdrDr["vendor_name"]),
                            WarehouseName = Util.GetValueOfString(hdrDr["warehouse_name"]),
                            CreatedBy = Util.GetValueOfString(hdrDr["created_by_name"]),
                            CreatedOn = createdOn.HasValue ? createdOn.Value.ToString("dd MMM yyyy") : "",
                            PaymentTerm = Util.GetValueOfString(hdrDr["payment_term_name"]),
                            CurrencySymbol = Util.GetValueOfString(hdrDr["cur_symbol"]),
                            CurrencyISO = Util.GetValueOfString(hdrDr["cur_iso"]),
                            StdPrecision = Util.GetValueOfInt(hdrDr["std_precision"])
                        };
                    }
                }
                finally
                {
                    if (hdrDr != null) { hdrDr.Close(); hdrDr.Dispose(); }
                }

                return Json(JsonConvert.SerializeObject(new { success = true, header = headerObj }), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_194_POsPendingDeliveryWidget.GetPODetail", ex);
                return Json(JsonConvert.SerializeObject(new { error = Msg.GetMsg(ctx, "Error") ?? "Error", success = false }), JsonRequestBehavior.AllowGet);
            }
        }
    }
}
