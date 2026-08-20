/************************************************************
 * Module Name    : VAS
 * Purpose        : Controller for PO Pending for Payment Widget (Widget 12)
 *                  Operational Purchase Order queue of received Purchase Orders
 *                  whose payment has not yet been fully released (balance_due > 0).
 *
 * Confirmed Database Mapping:
 *  - C_Order          : C_Order_ID, DocumentNo, DateOrdered, C_BPartner_ID, M_Warehouse_ID,
 *                       C_PaymentTerm_ID, C_Currency_ID, C_ConversionType_ID, AD_Org_ID,
 *                       DocStatus, IsSOTrx, IsReturnTrx, IsActive, AD_Client_ID
 *  - C_BPartner       : Name (Vendor name)
 *  - M_Warehouse      : Name (Warehouse name)
 *  - C_PaymentTerm    : NetDays, Name
 *  - M_InOut          : C_Order_ID, DateReceived, MovementDate, IsActive, IsSOTrx, MovementType, DocStatus
 *  - M_InOutLine      : M_InOut_ID, C_OrderLine_ID, MovementQty, IsActive
 *  - C_OrderLine      : C_OrderLine_ID, PriceActual, QtyOrdered, QtyDelivered, LineNetAmt, Line, C_UOM_ID, M_Product_ID, M_AttributeSetInstance_ID
 *  - C_Invoice        : C_Order_ID, GrandTotal, VA009_PaidAmount, VA009_OpenAmount, DueDate, IsActive, IsSOTrx, DocStatus
 *
 * Chronological Development:
 *   2026-08-17   : Created
 ***********************************************************/

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Web.Mvc;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VIS.Controllers
{
    /// <summary>
    /// Controller for Widget 12: PO Pending for Payment (VAS_214_POPendingForPaymentWidget)
    /// </summary>
    public class VAS_214_POPendingForPaymentWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_214_POPendingForPaymentWidgetController).FullName);

        [Authorize]
        public ActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// Retrieves the list of received Purchase Orders that have outstanding payable balance.
        /// Aggregates goods receipts and AP invoices separately before joining to C_Order.
        /// </summary>
        /// <returns>JSON object containing queue records, summary total due, and accounting currency info.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetPOPendingForPayment()
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

                // Step 2: Query Received POs with Outstanding Balance
                DateTime today = DateTime.Today;

                string sql = @"
                    SELECT
                        o.C_Order_ID AS purchase_order_id,
                        o.DocumentNo AS purchase_order_number,
                        o.DateOrdered AS order_date,
                        o.DatePromised AS promised_date,
                        o.DocStatus AS doc_status,
                        o.C_Currency_ID AS currency_id,
                        o.C_ConversionType_ID AS conversion_type_id,
                        o.AD_Org_ID AS org_id,
                        bp.Name AS vendor_name,
                        w.Name AS warehouse_name,
                        pt.NetDays AS net_days,
                        pt.Name AS payment_term_name,
                        c.CurSymbol AS doc_cur_symbol,
                        c.ISO_Code AS doc_cur_iso,
                        c.StdPrecision AS doc_std_precision,
                        r.received_on,
                        r.received_value,
                        i.payment_due AS invoice_payment_due,
                        CASE
                            WHEN i.C_Order_ID IS NOT NULL THEN i.invoice_total
                            ELSE r.received_value
                        END AS total_payable,
                        CASE
                            WHEN i.C_Order_ID IS NOT NULL THEN i.paid_amount
                            ELSE 0
                        END AS paid_amount,
                        CASE
                            WHEN i.C_Order_ID IS NOT NULL THEN i.open_amount
                            ELSE r.received_value
                        END AS balance_due
                    FROM C_Order o
                    INNER JOIN C_BPartner bp
                        ON bp.C_BPartner_ID = o.C_BPartner_ID
                    LEFT JOIN M_Warehouse w
                        ON w.M_Warehouse_ID = o.M_Warehouse_ID
                    LEFT JOIN C_PaymentTerm pt
                        ON pt.C_PaymentTerm_ID = o.C_PaymentTerm_ID
                    LEFT JOIN C_Currency c
                        ON c.C_Currency_ID = o.C_Currency_ID
                    INNER JOIN (
                        SELECT
                            io.C_Order_ID,
                            MAX(COALESCE(io.DateReceived, io.MovementDate)) AS received_on,
                            SUM(COALESCE(iol.MovementQty, 0) * COALESCE(ol.PriceActual, 0)) AS received_value
                        FROM M_InOut io
                        INNER JOIN M_InOutLine iol
                            ON iol.M_InOut_ID = io.M_InOut_ID
                           AND iol.IsActive = 'Y'
                        LEFT JOIN C_OrderLine ol
                            ON ol.C_OrderLine_ID = iol.C_OrderLine_ID
                        WHERE io.IsActive = 'Y'
                          AND io.IsSOTrx = 'N'
                          AND io.MovementType = 'V+'
                          AND io.DocStatus IN ('CO', 'CL')
                          AND io.C_Order_ID IS NOT NULL
                        GROUP BY io.C_Order_ID
                    ) r
                        ON r.C_Order_ID = o.C_Order_ID
                    LEFT JOIN (
                        SELECT
                            inv.C_Order_ID,
                            SUM(COALESCE(inv.GrandTotal, 0)) AS invoice_total,
                            SUM(COALESCE(inv.VA009_PaidAmount, 0)) AS paid_amount,
                            SUM(COALESCE(inv.VA009_OpenAmount,
                                         COALESCE(inv.GrandTotal, 0) - COALESCE(inv.VA009_PaidAmount, 0))) AS open_amount,
                            MIN(
                                CASE
                                    WHEN COALESCE(inv.VA009_OpenAmount,
                                                  COALESCE(inv.GrandTotal, 0) - COALESCE(inv.VA009_PaidAmount, 0)) > 0
                                    THEN inv.DueDate
                                    ELSE NULL
                                END
                            ) AS payment_due
                        FROM C_Invoice inv
                        WHERE inv.IsActive = 'Y'
                          AND inv.IsSOTrx = 'N'
                          AND inv.DocStatus IN ('CO', 'CL')
                          AND inv.C_Order_ID IS NOT NULL
                        GROUP BY inv.C_Order_ID
                    ) i
                        ON i.C_Order_ID = o.C_Order_ID
                    WHERE o.AD_Client_ID = " + clientId + @"
                      AND o.IsActive = 'Y'
                      AND o.IsSOTrx = 'N'
                      AND COALESCE(o.IsReturnTrx, 'N') = 'N'
                      AND (
                            CASE
                                WHEN i.C_Order_ID IS NOT NULL THEN i.open_amount
                                ELSE r.received_value
                            END
                          ) > 0
                    ORDER BY
                        CASE
                            WHEN i.C_Order_ID IS NOT NULL THEN i.payment_due
                            ELSE r.received_on + COALESCE(pt.NetDays, 0)
                        END ASC,
                        o.DocumentNo ASC";

                sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                var records = new List<object>();
                decimal totalDueConvertedAcrossQueue = 0;

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
                        DateTime? receivedOn = Util.GetValueOfDateTime(dr["received_on"]);
                        DateTime? invoicePaymentDue = Util.GetValueOfDateTime(dr["invoice_payment_due"]);
                        int netDays = Util.GetValueOfInt(dr["net_days"]);
                        string paymentTermName = Util.GetValueOfString(dr["payment_term_name"]);
                        string docStatus = Util.GetValueOfString(dr["doc_status"]);

                        int orderCurId = Util.GetValueOfInt(dr["currency_id"]);
                        int convTypeId = Util.GetValueOfInt(dr["conversion_type_id"]);
                        int orgId = Util.GetValueOfInt(dr["org_id"]);
                        string vendorName = Util.GetValueOfString(dr["vendor_name"]);
                        string warehouseName = Util.GetValueOfString(dr["warehouse_name"]);
                        string docCurSymbol = Util.GetValueOfString(dr["doc_cur_symbol"]);
                        string docCurIso = Util.GetValueOfString(dr["doc_cur_iso"]);
                        int docPrecision = Util.GetValueOfInt(dr["doc_std_precision"]);

                        decimal totalPayable = Util.GetValueOfDecimal(dr["total_payable"]);
                        decimal paidAmount = Util.GetValueOfDecimal(dr["paid_amount"]);
                        decimal balanceDue = Util.GetValueOfDecimal(dr["balance_due"]);

                        // Resolve effective payment due date: Invoice DueDate or (ReceivedOn + NetDays)
                        DateTime? paymentDue = invoicePaymentDue;
                        if (!paymentDue.HasValue && receivedOn.HasValue)
                        {
                            paymentDue = receivedOn.Value.Date.AddDays(netDays);
                        }

                        // Determine Overdue status (computed in C# server code for 100% portability)
                        bool isOverdue = false;
                        int overdueDays = 0;
                        if (paymentDue.HasValue && paymentDue.Value.Date < today)
                        {
                            isOverdue = true;
                            overdueDays = (today - paymentDue.Value.Date).Days;
                        }

                        // Currency conversion for total queue due calculation
                        decimal convertedBalanceDue = balanceDue;
                        if (orderCurId != schemaCurrencyId && schemaCurrencyId > 0 && balanceDue != 0)
                        {
                            try
                            {
                                decimal conv = MCurrency.CurrencyConvert(
                                    balanceDue,
                                    orderCurId,
                                    schemaCurrencyId,
                                    orderDate.HasValue ? orderDate.Value : today,
                                    convTypeId,
                                    clientId,
                                    orgId);
                                if (conv != 0)
                                {
                                    convertedBalanceDue = conv;
                                }
                            }
                            catch (Exception exConv)
                            {
                                Log.Log(Level.WARNING, "Currency conversion failed for PO " + poNo, exConv);
                            }
                        }

                        totalDueConvertedAcrossQueue += convertedBalanceDue;

                        records.Add(new
                        {
                            PurchaseOrderID = poId,
                            PurchaseOrderNo = poNo,
                            OrderDate = orderDate.HasValue ? orderDate.Value.ToString("yyyy-MM-dd") : "",
                            OrderDateDisplay = orderDate.HasValue ? orderDate.Value.ToString("dd MMM yyyy") : "",
                            OrderDateShort = orderDate.HasValue ? orderDate.Value.ToString("dd MMM") : "",
                            PromisedDate = promisedDate.HasValue ? promisedDate.Value.ToString("yyyy-MM-dd") : "",
                            ReceivedOn = receivedOn.HasValue ? receivedOn.Value.ToString("yyyy-MM-dd") : "",
                            ReceivedOnDisplay = receivedOn.HasValue ? receivedOn.Value.ToString("dd MMM yyyy") : "",
                            ReceivedOnShort = receivedOn.HasValue ? receivedOn.Value.ToString("dd MMM") : "",
                            PaymentDue = paymentDue.HasValue ? paymentDue.Value.ToString("yyyy-MM-dd") : "",
                            PaymentDueDisplay = paymentDue.HasValue ? paymentDue.Value.ToString("dd MMM yyyy") : "",
                            PaymentDueShort = paymentDue.HasValue ? paymentDue.Value.ToString("dd MMM") : "",
                            IsOverdue = isOverdue,
                            OverdueDays = overdueDays,
                            TotalPayable = totalPayable,
                            PaidAmount = paidAmount,
                            BalanceDue = balanceDue,
                            BalanceDueConverted = convertedBalanceDue,
                            VendorName = vendorName,
                            WarehouseName = warehouseName,
                            PaymentTermName = paymentTermName,
                            DocStatus = docStatus,
                            CurrencyID = orderCurId,
                            CurrencySymbol = docCurSymbol,
                            CurrencyISO = docCurIso,
                            StdPrecision = docPrecision
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
                    totalDue = totalDueConvertedAcrossQueue,
                    totalCount = records.Count,
                    baseCurrency = new
                    {
                        CurrencyID = schemaCurrencyId,
                        CurSymbol = schemaCurSymbol,
                        ISO_Code = schemaCurIso,
                        StdPrecision = schemaStdPrecision
                    },
                    records = records
                };

                return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_214_POPendingForPaymentWidget.GetPOPendingForPayment", ex);
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
                Log.Log(Level.SEVERE, "VAS_214_POPendingForPaymentWidget.GetPOLines", ex);
                return Json(JsonConvert.SerializeObject(new { error = Msg.GetMsg(ctx, "Error") ?? "Error", success = false }), JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Retrieves full Purchase Order header details for the record modal.
        /// </summary>
        /// <param name="C_Order_ID">Purchase Order primary key.</param>
        /// <returns>JSON object with header info.</returns>
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
                Log.Log(Level.SEVERE, "VAS_214_POPendingForPaymentWidget.GetPODetail", ex);
                return Json(JsonConvert.SerializeObject(new { error = Msg.GetMsg(ctx, "Error") ?? "Error", success = false }), JsonRequestBehavior.AllowGet);
            }
        }
    }
}
