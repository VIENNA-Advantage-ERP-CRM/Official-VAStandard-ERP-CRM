/************************************************************
 * Module Name    : VAS
 * Purpose        : Widget 03 — Purchase Orders Completed MTD
 *                  KPI card & document drill-down modal counting
 *                  purchase orders reaching terminal states
 *                  (Completed / Closed) in the current MTD window.
 * Author         : Builder Agent 3
 * Date           : 17 August 2026
 ***********************************************************/

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Web.Mvc;
using VAdvantage.Classes;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VIS.Controllers
{
    /// <summary>
    /// Controller for VAS_193_POCompletedMTDWidget.
    /// Provides MTD completed/closed PO summary figures and drill-down document list.
    /// </summary>
    public class VAS_193_POCompletedMTDWidgetController : Controller
    {
        private static readonly VLogger _log = VLogger.GetVLogger(typeof(VAS_193_POCompletedMTDWidgetController).FullName);

        /// <summary>
        /// Gets the MTD completed/closed purchase orders summary and list of records.
        /// Both the KPI card and the drill-down modal consume this single endpoint.
        /// </summary>
        /// <returns>JSON object with KPI metrics and records array.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetPOCompletedMTDData()
        {
            if (Session["ctx"] == null)
            {
                return Json(new
                {
                    error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired"
                }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null)
            {
                return Json(new { error = Msg.GetMsg(Env.GetCtx(), "Error") ?? "Context is null" }, JsonRequestBehavior.AllowGet);
            }

            try
            {
                int clientId = ctx.GetAD_Client_ID();

                // 1. Resolve Functional Accounting Schema Currency
                int schemaCurrencyId = 0;
                string schemaCurSymbol = "";
                string schemaCurIso = "";
                int schemaStdPrecision = 2;

                string curSql = @"
                    SELECT cs.C_Currency_ID, c.CurSymbol, c.StdPrecision, c.ISO_Code
                    FROM C_AcctSchema cs
                    INNER JOIN AD_ClientInfo ci ON (ci.C_AcctSchema1_ID = cs.C_AcctSchema_ID)
                    INNER JOIN C_Currency c ON (cs.C_Currency_ID = c.C_Currency_ID)
                    WHERE ci.AD_Client_ID = @AD_Client_ID
                      AND ci.IsActive = 'Y'
                      AND cs.IsActive = 'Y'
                      AND c.IsActive = 'Y'";

                SqlParameter[] curParams = { new SqlParameter("@AD_Client_ID", clientId) };
                DataSet curDs = DB.ExecuteDataset(curSql, curParams, null);
                if (curDs != null && curDs.Tables.Count > 0 && curDs.Tables[0].Rows.Count > 0)
                {
                    DataRow r = curDs.Tables[0].Rows[0];
                    schemaCurrencyId = Util.GetValueOfInt(r["C_Currency_ID"]);
                    schemaCurSymbol = Util.GetValueOfString(r["CurSymbol"]);
                    schemaCurIso = Util.GetValueOfString(r["ISO_Code"]);
                    schemaStdPrecision = Util.GetValueOfInt(r["StdPrecision"]);
                }

                if (schemaCurrencyId == 0)
                {
                    schemaCurrencyId = ctx.GetContextAsInt("$C_Currency_ID");
                }

                // 2. Compute half-open MTD date bounds: [MonthStart, NextMonthStart)
                DateTime now = DateTime.Now;
                DateTime mtdStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0);
                DateTime mtdEndExclusive = mtdStart.AddMonths(1);

                // 3. Query MTD Completed/Closed Purchase Orders
                string sql = @"
                    SELECT
                        o.C_Order_ID AS purchase_order_id,
                        o.DocumentNo AS purchase_order_number,
                        o.DateOrdered AS order_date,
                        o.DatePromised AS promised_date,
                        o.OrderCompletionDatetime AS completion_datetime,
                        o.DocStatus AS document_status,
                        o.C_Currency_ID AS currency_id,
                        o.C_ConversionType_ID AS conversion_type_id,
                        o.AD_Org_ID AS org_id,
                        bp.Name AS vendor_name,
                        w.Name AS warehouse_name,
                        rep.Name AS representative_name,
                        SUM(COALESCE(ol.QtyOrdered, 0)) AS ordered_qty,
                        SUM(COALESCE(ol.QtyDelivered, 0)) AS delivered_qty,
                        SUM(COALESCE(ol.LineNetAmt, 0)) AS po_value_document_currency
                    FROM C_Order o
                    INNER JOIN C_OrderLine ol
                        ON ol.C_Order_ID = o.C_Order_ID
                       AND ol.IsActive = 'Y'
                    INNER JOIN C_BPartner bp
                        ON bp.C_BPartner_ID = o.C_BPartner_ID
                    LEFT JOIN M_Warehouse w
                        ON w.M_Warehouse_ID = o.M_Warehouse_ID
                    LEFT JOIN AD_User rep
                        ON rep.AD_User_ID = o.SalesRep_ID
                    WHERE o.AD_Client_ID = @P_AD_Client_ID@
                      AND o.IsActive = 'Y'
                      AND o.IsSOTrx = 'N'
                      AND COALESCE(o.IsReturnTrx, 'N') = 'N'
                      AND o.DocStatus IN ('CO', 'CL')
                      AND o.OrderCompletionDatetime >= @P_MTD_START@
                      AND o.OrderCompletionDatetime < @P_MTD_END_EXCLUSIVE@
                    GROUP BY
                        o.C_Order_ID,
                        o.DocumentNo,
                        o.DateOrdered,
                        o.DatePromised,
                        o.OrderCompletionDatetime,
                        o.DocStatus,
                        o.C_Currency_ID,
                        o.C_ConversionType_ID,
                        o.AD_Org_ID,
                        bp.Name,
                        w.Name,
                        rep.Name
                    ORDER BY o.OrderCompletionDatetime DESC, o.DocumentNo DESC";

                sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                SqlParameter[] sqlParams =
                {
                    new SqlParameter("@P_AD_Client_ID", clientId),
                    new SqlParameter("@P_MTD_START", mtdStart),
                    new SqlParameter("@P_MTD_END_EXCLUSIVE", mtdEndExclusive)
                };

                DataSet ds = DB.ExecuteDataset(sql, sqlParams, null);

                int totalCount = 0;
                int completedCount = 0;
                int closedCount = 0;
                int closedShortCount = 0;
                decimal receivedValue = 0m;
                int onTimeEligibleCount = 0;
                int onTimeCount = 0;

                List<PODocumentItem> records = new List<PODocumentItem>();

                if (ds != null && ds.Tables.Count > 0)
                {
                    foreach (DataRow row in ds.Tables[0].Rows)
                    {
                        totalCount++;
                        int poId = Util.GetValueOfInt(row["purchase_order_id"]);
                        string docNo = Util.GetValueOfString(row["purchase_order_number"]);
                        DateTime? orderDate = Util.GetValueOfDateTime(row["order_date"]);
                        DateTime? promisedDate = Util.GetValueOfDateTime(row["promised_date"]);
                        DateTime? completionDt = Util.GetValueOfDateTime(row["completion_datetime"]);
                        string docStatus = Util.GetValueOfString(row["document_status"]);
                        int rowCurId = Util.GetValueOfInt(row["currency_id"]);
                        int convTypeId = Util.GetValueOfInt(row["conversion_type_id"]);
                        int orgId = Util.GetValueOfInt(row["org_id"]);
                        string vendor = Util.GetValueOfString(row["vendor_name"]);
                        string warehouse = Util.GetValueOfString(row["warehouse_name"]);
                        string representative = Util.GetValueOfString(row["representative_name"]);
                        decimal orderedQty = Util.GetValueOfDecimal(row["ordered_qty"]);
                        decimal deliveredQty = Util.GetValueOfDecimal(row["delivered_qty"]);
                        decimal poValueDocCur = Util.GetValueOfDecimal(row["po_value_document_currency"]);

                        // Server-side Currency Conversion to Accounting Currency
                        decimal convertedValue = poValueDocCur;
                        if (rowCurId > 0 && schemaCurrencyId > 0 && rowCurId != schemaCurrencyId)
                        {
                            try
                            {
                                decimal conv = MConversionRate.Convert(
                                    ctx,
                                    poValueDocCur,
                                    rowCurId,
                                    schemaCurrencyId,
                                    orderDate.HasValue ? orderDate.Value : now,
                                    convTypeId,
                                    clientId,
                                    orgId
                                );
                                if (conv > 0m || poValueDocCur == 0m)
                                {
                                    convertedValue = conv;
                                }
                            }
                            catch (Exception ex)
                            {
                                _log.Warning("VAS_193: Currency conversion failed for PO " + docNo + ": " + ex.Message);
                            }
                        }

                        bool isCompleted = (docStatus == "CO");
                        bool isClosed = (docStatus == "CL");
                        bool isClosedShort = isClosed && (orderedQty != deliveredQty);
                        bool isFullyReceived = (orderedQty > 0m && deliveredQty >= orderedQty);

                        if (isCompleted)
                        {
                            completedCount++;
                            if (promisedDate.HasValue && completionDt.HasValue)
                            {
                                onTimeEligibleCount++;
                                if (completionDt.Value.Date <= promisedDate.Value.Date)
                                {
                                    onTimeCount++;
                                }
                            }

                            if (isFullyReceived)
                            {
                                receivedValue += convertedValue;
                            }
                        }
                        else if (isClosed)
                        {
                            closedCount++;
                            if (isClosedShort)
                            {
                                closedShortCount++;
                            }
                        }

                        // Delivery status derivation
                        string deliveryCode = "na";
                        string deliveryText = "Not applicable";
                        string deliveryChip = "chip-neutral";

                        if (docStatus == "CL" || docStatus == "VO")
                        {
                            deliveryCode = "na";
                            deliveryText = Msg.GetMsg(ctx, "VAS_NotApplicable") ?? "Not applicable";
                            deliveryChip = "chip-neutral";
                        }
                        else if (isFullyReceived)
                        {
                            deliveryCode = "full";
                            deliveryText = Msg.GetMsg(ctx, "VAS_FullyDelivered") ?? "Fully delivered";
                            deliveryChip = "chip-ok";
                        }
                        else if (deliveredQty > 0m && deliveredQty < orderedQty)
                        {
                            deliveryCode = "partial";
                            deliveryText = Msg.GetMsg(ctx, "VAS_Partial") ?? "Partial";
                            deliveryChip = "chip-warn";
                        }
                        else
                        {
                            deliveryCode = "pending";
                            deliveryText = Msg.GetMsg(ctx, "VAS_Pending") ?? "Pending";
                            deliveryChip = "chip-neutral";
                        }

                        // Document Status label
                        string docStatusLabel = isCompleted ? (Msg.GetMsg(ctx, "Completed") ?? "Completed") : (Msg.GetMsg(ctx, "Closed") ?? "Closed");

                        PODocumentItem item = new PODocumentItem
                        {
                            PurchaseOrderId = poId,
                            PurchaseOrderNumber = docNo,
                            OrderDate = orderDate.HasValue ? orderDate.Value.ToString("yyyy-MM-dd") : "",
                            OrderDateFormatted = orderDate.HasValue ? orderDate.Value.ToString("dd MMM yyyy") : "",
                            PromisedDate = promisedDate.HasValue ? promisedDate.Value.ToString("yyyy-MM-dd") : "",
                            PromisedDateFormatted = promisedDate.HasValue ? promisedDate.Value.ToString("dd MMM yyyy") : "",
                            CompletionDatetime = completionDt.HasValue ? completionDt.Value.ToString("yyyy-MM-dd HH:mm:ss") : "",
                            DocumentStatus = docStatus,
                            DocStatusText = docStatusLabel,
                            DocStatusChip = "chip-ok",
                            VendorName = string.IsNullOrEmpty(vendor) ? "—" : vendor,
                            WarehouseName = string.IsNullOrEmpty(warehouse) ? "—" : warehouse,
                            RepresentativeName = string.IsNullOrEmpty(representative) ? "—" : representative,
                            OrderedQty = orderedQty,
                            DeliveredQty = deliveredQty,
                            PendingQty = Math.Max(0m, orderedQty - deliveredQty),
                            PoValueDocumentCurrency = poValueDocCur,
                            PoValueConverted = convertedValue,
                            DeliveryCode = deliveryCode,
                            DeliveryText = deliveryText,
                            DeliveryChip = deliveryChip,
                            IsFullyReceived = isFullyReceived,
                            IsClosedShort = isClosedShort
                        };

                        records.Add(item);
                    }
                }

                int? onTimePercent = null;
                if (onTimeEligibleCount > 0)
                {
                    onTimePercent = (int)Math.Round((double)onTimeCount / onTimeEligibleCount * 100.0);
                }

                var result = new
                {
                    totalCount = totalCount,
                    completedCount = completedCount,
                    closedCount = closedCount,
                    closedShortCount = closedShortCount,
                    receivedValue = receivedValue,
                    onTimePercent = onTimePercent,
                    currencySymbol = schemaCurSymbol,
                    currencyIso = schemaCurIso,
                    stdPrecision = schemaStdPrecision,
                    records = records
                };

                return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                _log.Log(Level.SEVERE, "VAS_193_POCompletedMTDWidget.GetPOCompletedMTDData", ex);
                return Json(new { error = Msg.GetMsg(ctx, "Error") ?? "An error occurred while loading data." }, JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Gets the line items for a specific purchase order.
        /// </summary>
        /// <param name="C_Order_ID">Purchase order identifier.</param>
        /// <returns>JSON object with line records and parent summary info.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetPOLines(int C_Order_ID)
        {
            if (Session["ctx"] == null)
            {
                return Json(new
                {
                    error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired"
                }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null || C_Order_ID <= 0)
            {
                return Json(new { error = Msg.GetMsg(Env.GetCtx(), "Error") ?? "Invalid Purchase Order ID" }, JsonRequestBehavior.AllowGet);
            }

            try
            {
                string headerSql = @"
                    SELECT
                        o.C_Order_ID,
                        o.DocumentNo,
                        o.DateOrdered,
                        o.DatePromised,
                        o.DocStatus,
                        o.GrandTotal,
                        bp.Name AS VendorName,
                        w.Name AS WarehouseName,
                        rep.Name AS RepName,
                        cur.CurSymbol,
                        cur.ISO_Code,
                        COALESCE(cur.StdPrecision, 2) AS StdPrecision
                    FROM C_Order o
                    INNER JOIN C_BPartner bp ON bp.C_BPartner_ID = o.C_BPartner_ID
                    LEFT JOIN M_Warehouse w ON w.M_Warehouse_ID = o.M_Warehouse_ID
                    LEFT JOIN AD_User rep ON rep.AD_User_ID = o.SalesRep_ID
                    LEFT JOIN C_Currency cur ON cur.C_Currency_ID = o.C_Currency_ID
                    WHERE o.C_Order_ID = @C_Order_ID
                      AND o.IsActive = 'Y'
                      AND o.IsSOTrx = 'N'";

                headerSql = MRole.GetDefault(ctx).AddAccessSQL(headerSql, "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                SqlParameter[] hParams = { new SqlParameter("@C_Order_ID", C_Order_ID) };
                DataSet hDs = DB.ExecuteDataset(headerSql, hParams, null);

                if (hDs == null || hDs.Tables.Count == 0 || hDs.Tables[0].Rows.Count == 0)
                {
                    return Json(new { error = Msg.GetMsg(ctx, "RecordNotFound") ?? "Record not found" }, JsonRequestBehavior.AllowGet);
                }

                DataRow hr = hDs.Tables[0].Rows[0];
                string docNo = Util.GetValueOfString(hr["DocumentNo"]);
                string vendor = Util.GetValueOfString(hr["VendorName"]);
                DateTime? orderDate = Util.GetValueOfDateTime(hr["DateOrdered"]);
                DateTime? promisedDate = Util.GetValueOfDateTime(hr["DatePromised"]);
                string docStatus = Util.GetValueOfString(hr["DocStatus"]);
                string warehouse = Util.GetValueOfString(hr["WarehouseName"]);
                string repName = Util.GetValueOfString(hr["RepName"]);
                string curSymbol = Util.GetValueOfString(hr["CurSymbol"]);
                string curIso = Util.GetValueOfString(hr["ISO_Code"]);
                int precision = Util.GetValueOfInt(hr["StdPrecision"]);

                // Query Lines
                string linesSql = @"
                    SELECT
                        ol.C_OrderLine_ID,
                        ol.Line,
                        COALESCE(p.Name, ol.Description, '—') AS ProductName,
                        p.Value AS ProductSku,
                        asi.Description AS AttributeDesc,
                        COALESCE(uom.UOMSymbol, uom.Name, '') AS UOMSymbol,
                        COALESCE(ol.QtyOrdered, 0) AS QtyOrdered,
                        COALESCE(ol.QtyDelivered, 0) AS QtyDelivered,
                        COALESCE(ol.PriceActual, 0) AS PriceActual,
                        COALESCE(ol.LineNetAmt, 0) AS LineNetAmt
                    FROM C_OrderLine ol
                    LEFT JOIN M_Product p ON p.M_Product_ID = ol.M_Product_ID
                    LEFT JOIN M_AttributeSetInstance asi ON asi.M_AttributeSetInstance_ID = ol.M_AttributeSetInstance_ID
                    LEFT JOIN C_UOM uom ON uom.C_UOM_ID = ol.C_UOM_ID
                    WHERE ol.C_Order_ID = @C_Order_ID
                      AND ol.IsActive = 'Y'
                    ORDER BY ol.Line ASC, ol.C_OrderLine_ID ASC";

                DataSet lDs = DB.ExecuteDataset(linesSql, hParams, null);

                List<POLineItem> lines = new List<POLineItem>();
                decimal totalOrderedQty = 0m;
                decimal totalDeliveredQty = 0m;
                decimal totalLineNetAmt = 0m;

                if (lDs != null && lDs.Tables.Count > 0)
                {
                    foreach (DataRow lr in lDs.Tables[0].Rows)
                    {
                        int lineId = Util.GetValueOfInt(lr["C_OrderLine_ID"]);
                        int lineNo = Util.GetValueOfInt(lr["Line"]);
                        string prodName = Util.GetValueOfString(lr["ProductName"]);
                        string prodSku = Util.GetValueOfString(lr["ProductSku"]);
                        string attrDesc = Util.GetValueOfString(lr["AttributeDesc"]);
                        string uomSym = Util.GetValueOfString(lr["UOMSymbol"]);
                        decimal ordered = Util.GetValueOfDecimal(lr["QtyOrdered"]);
                        decimal delivered = Util.GetValueOfDecimal(lr["QtyDelivered"]);
                        decimal price = Util.GetValueOfDecimal(lr["PriceActual"]);
                        decimal amount = Util.GetValueOfDecimal(lr["LineNetAmt"]);

                        totalOrderedQty += ordered;
                        totalDeliveredQty += delivered;
                        totalLineNetAmt += amount;

                        decimal pending = Math.Max(0m, ordered - delivered);

                        string lineStatusText;
                        string lineStatusChip;

                        if (docStatus == "DR")
                        {
                            lineStatusText = Msg.GetMsg(ctx, "Drafted") ?? "Drafted";
                            lineStatusChip = "chip-neutral";
                        }
                        else if (docStatus == "VO")
                        {
                            lineStatusText = Msg.GetMsg(ctx, "Voided") ?? "Voided";
                            lineStatusChip = "chip-risk";
                        }
                        else if (ordered > 0m && delivered >= ordered)
                        {
                            lineStatusText = Msg.GetMsg(ctx, "Received") ?? "Received";
                            lineStatusChip = "chip-ok";
                        }
                        else if (delivered > 0m && delivered < ordered)
                        {
                            lineStatusText = Msg.GetMsg(ctx, "PartialReceived") ?? "Partial received";
                            lineStatusChip = "chip-warn";
                        }
                        else
                        {
                            lineStatusText = Msg.GetMsg(ctx, "Pending") ?? "Pending";
                            lineStatusChip = "chip-neutral";
                        }

                        lines.Add(new POLineItem
                        {
                            OrderLineId = lineId,
                            LineNo = lineNo,
                            ProductName = prodName,
                            ProductSku = prodSku,
                            Attribute = string.IsNullOrEmpty(attrDesc) ? "—" : attrDesc,
                            UOM = string.IsNullOrEmpty(uomSym) ? "—" : uomSym,
                            QtyOrdered = ordered,
                            QtyDelivered = delivered,
                            QtyPending = pending,
                            PriceActual = price,
                            LineNetAmt = amount,
                            LineStatus = lineStatusText,
                            LineStatusChip = lineStatusChip
                        });
                    }
                }

                // Delivery Status
                string delivStatus = (docStatus == "CL" || docStatus == "VO")
                    ? (Msg.GetMsg(ctx, "VAS_NotApplicable") ?? "Not applicable")
                    : (totalOrderedQty > 0m && totalDeliveredQty >= totalOrderedQty)
                        ? (Msg.GetMsg(ctx, "VAS_FullyDelivered") ?? "Fully delivered")
                        : (totalDeliveredQty > 0m)
                            ? (Msg.GetMsg(ctx, "VAS_Partial") ?? "Partial")
                            : (Msg.GetMsg(ctx, "VAS_Pending") ?? "Pending");

                var result = new
                {
                    purchaseOrderId = C_Order_ID,
                    purchaseOrderNumber = docNo,
                    vendorName = vendor,
                    orderDateFormatted = orderDate.HasValue ? orderDate.Value.ToString("dd MMM yyyy") : "",
                    promisedDateFormatted = promisedDate.HasValue ? promisedDate.Value.ToString("dd MMM yyyy") : "",
                    warehouseName = warehouse,
                    representativeName = repName,
                    documentStatus = docStatus == "CO" ? (Msg.GetMsg(ctx, "Completed") ?? "Completed") : (Msg.GetMsg(ctx, "Closed") ?? "Closed"),
                    deliveryStatus = delivStatus,
                    currencySymbol = curSymbol,
                    currencyIso = curIso,
                    stdPrecision = precision,
                    totalOrderedQty = totalOrderedQty,
                    totalDeliveredQty = totalDeliveredQty,
                    totalPendingQty = Math.Max(0m, totalOrderedQty - totalDeliveredQty),
                    totalAmount = totalLineNetAmt,
                    lines = lines
                };

                return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                _log.Log(Level.SEVERE, "VAS_193_POCompletedMTDWidget.GetPOLines", ex);
                return Json(new { error = Msg.GetMsg(ctx, "Error") ?? "An error occurred while loading lines." }, JsonRequestBehavior.AllowGet);
            }
        }

        private class PODocumentItem
        {
            public int PurchaseOrderId { get; set; }
            public string PurchaseOrderNumber { get; set; }
            public string OrderDate { get; set; }
            public string OrderDateFormatted { get; set; }
            public string PromisedDate { get; set; }
            public string PromisedDateFormatted { get; set; }
            public string CompletionDatetime { get; set; }
            public string DocumentStatus { get; set; }
            public string DocStatusText { get; set; }
            public string DocStatusChip { get; set; }
            public string VendorName { get; set; }
            public string WarehouseName { get; set; }
            public string RepresentativeName { get; set; }
            public decimal OrderedQty { get; set; }
            public decimal DeliveredQty { get; set; }
            public decimal PendingQty { get; set; }
            public decimal PoValueDocumentCurrency { get; set; }
            public decimal PoValueConverted { get; set; }
            public string DeliveryCode { get; set; }
            public string DeliveryText { get; set; }
            public string DeliveryChip { get; set; }
            public bool IsFullyReceived { get; set; }
            public bool IsClosedShort { get; set; }
        }

        private class POLineItem
        {
            public int OrderLineId { get; set; }
            public int LineNo { get; set; }
            public string ProductName { get; set; }
            public string ProductSku { get; set; }
            public string Attribute { get; set; }
            public string UOM { get; set; }
            public decimal QtyOrdered { get; set; }
            public decimal QtyDelivered { get; set; }
            public decimal QtyPending { get; set; }
            public decimal PriceActual { get; set; }
            public decimal LineNetAmt { get; set; }
            public string LineStatus { get; set; }
            public string LineStatusChip { get; set; }
        }
    }
}
