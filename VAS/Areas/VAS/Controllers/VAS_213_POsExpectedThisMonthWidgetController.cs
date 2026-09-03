﻿﻿﻿﻿/************************************************************
 * Module Name    : VAS
 * Purpose        : Controller for Widget 11: POs Expected This Month
 *                  (VAS_213_POsExpectedThisMonthWidget)
 *                  Shows open purchase orders whose promised delivery date
 *                  falls in the current/selected month, order value, count
 *                  due within 7 days, and nearest promised orders.
 * Chronological Development:
 *   2026-08-17   : Created
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
    /// Controller for Widget 11: POs Expected This Month (VAS_213_POsExpectedThisMonthWidget)
    /// </summary>
    public class VAS_213_POsExpectedThisMonthWidgetController : Controller
    {
        private static readonly VLogger _log = VLogger.GetVLogger(typeof(VAS_213_POsExpectedThisMonthWidgetController).FullName);

        [Authorize]
        public ActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// Retrieves the summary metrics and list of POs expected to be delivered in the specified month/year.
        /// </summary>
        /// <param name="year">Optional 4-digit calendar year (defaults to current year)</param>
        /// <param name="month">Optional 1-based month index 1..12 (defaults to current month)</param>
        /// <returns>JSON object with KPI stats, currency info, and PO records array.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetPOsExpectedThisMonth(int? year = null, int? month = null)
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null)
            {
                return Json(JsonConvert.SerializeObject(new { error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired", success = false }), JsonRequestBehavior.AllowGet);
            }

            try
            {
                int clientId = ctx.GetAD_Client_ID();

                // Step 1: Resolve Client Functional Accounting Schema Currency
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

                // Step 2: Compute Half-Open Date Boundaries for Target Month: [monthStart, monthEndExclusive)
                DateTime today = DateTime.Today;
                int targetYear = (year.HasValue && year.Value >= 2000 && year.Value <= 2100) ? year.Value : today.Year;
                int targetMonth = (month.HasValue && month.Value >= 1 && month.Value <= 12) ? month.Value : today.Month;

                DateTime monthStart = new DateTime(targetYear, targetMonth, 1, 0, 0, 0);
                DateTime monthEndExclusive = monthStart.AddMonths(1);

                DateTime todayPlus7 = today.AddDays(7);

                // Step 3: Query Open POs Expected in the target month window
                string sql = @"
                    SELECT
                        o.C_Order_ID AS purchase_order_id,
                        o.DocumentNo AS purchase_order_number,
                        o.DateOrdered AS order_date,
                        o.DatePromised AS promised_date,
                        o.DocStatus AS document_status,
                        o.C_Currency_ID AS currency_id,
                        o.C_ConversionType_ID AS conversion_type_id,
                        o.AD_Org_ID AS org_id,
                        bp.Name AS vendor_name,
                        w.Name AS warehouse_name,
                        rep.Name AS representative_name,
                        c.CurSymbol AS doc_cur_symbol,
                        c.ISO_Code AS doc_cur_iso,
                        COALESCE(o.GrandTotal, 0) AS po_value,
                        q.ordered_qty,
                        q.delivered_qty
                    FROM C_Order o
                    INNER JOIN C_BPartner bp
                        ON bp.C_BPartner_ID = o.C_BPartner_ID
                    LEFT JOIN M_Warehouse w
                        ON w.M_Warehouse_ID = o.M_Warehouse_ID
                    LEFT JOIN AD_User rep
                        ON rep.AD_User_ID = o.SalesRep_ID
                    LEFT JOIN C_Currency c
                        ON c.C_Currency_ID = o.C_Currency_ID
                    INNER JOIN (
                        SELECT
                            C_Order_ID,
                            SUM(COALESCE(QtyOrdered, 0)) AS ordered_qty,
                            SUM(COALESCE(QtyDelivered, 0)) AS delivered_qty
                        FROM C_OrderLine
                        WHERE IsActive = 'Y'
                        GROUP BY C_Order_ID
                    ) q
                        ON q.C_Order_ID = o.C_Order_ID
                    WHERE o.AD_Client_ID = @P_AD_Client_ID@
                      AND o.IsActive = 'Y'
                      AND o.IsSOTrx = 'N'
                      AND COALESCE(o.IsReturnTrx, 'N') = 'N'
                      AND o.DocStatus NOT IN ('CO', 'CL', 'VO', 'RE')
                      AND o.DatePromised >= @P_MONTH_START@
                      AND o.DatePromised < @P_MONTH_END_EXCLUSIVE@
                      AND q.ordered_qty > q.delivered_qty
                      AND o.C_Order_ID IN (@P_ORDER_ACCESS@)
                    ORDER BY o.DatePromised ASC, o.DocumentNo ASC";

                // MRole.AddAccessSQL cannot parse this statement: the derived table makes
                // AccessSqlParser report "More than one FROM clause", after which it appends
                // the access predicates in the wrong place (ORA-00933) and the widget returns
                // nothing. Apply the same role access through a simple, parseable sub-query.
                string orderAccessSql = MRole.GetDefault(ctx).AddAccessSQL(
                    "SELECT accessOrd.C_Order_ID FROM C_Order accessOrd WHERE accessOrd.AD_Client_ID = " + clientId,
                    "accessOrd", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                sql = sql.Replace("@P_ORDER_ACCESS@", orderAccessSql);

                SqlParameter[] sqlParams =
                {
                    new SqlParameter("@P_AD_Client_ID", clientId),
                    new SqlParameter("@P_MONTH_START", monthStart),
                    new SqlParameter("@P_MONTH_END_EXCLUSIVE", monthEndExclusive)
                };

                DataSet ds = DB.ExecuteDataset(sql, sqlParams, null);

                var poRecords = new List<object>();
                int expectedCount = 0;
                decimal expectedTotalValueConverted = 0m;
                int dueIn7DaysCount = 0;

                if (ds != null && ds.Tables.Count > 0)
                {
                    foreach (DataRow row in ds.Tables[0].Rows)
                    {
                        int poId = Util.GetValueOfInt(row["purchase_order_id"]);
                        string poNo = Util.GetValueOfString(row["purchase_order_number"]);
                        DateTime? orderDate = Util.GetValueOfDateTime(row["order_date"]);
                        DateTime? promisedDate = Util.GetValueOfDateTime(row["promised_date"]);
                        string docStatus = Util.GetValueOfString(row["document_status"]);
                        int orderCurId = Util.GetValueOfInt(row["currency_id"]);
                        int convTypeId = Util.GetValueOfInt(row["conversion_type_id"]);
                        int orgId = Util.GetValueOfInt(row["org_id"]);
                        string vendorName = Util.GetValueOfString(row["vendor_name"]);
                        string warehouseName = Util.GetValueOfString(row["warehouse_name"]);
                        string repName = Util.GetValueOfString(row["representative_name"]);
                        string docCurSymbol = Util.GetValueOfString(row["doc_cur_symbol"]);
                        string docCurIso = Util.GetValueOfString(row["doc_cur_iso"]);
                        decimal poValue = Util.GetValueOfDecimal(row["po_value"]);
                        decimal orderedQty = Util.GetValueOfDecimal(row["ordered_qty"]);
                        decimal deliveredQty = Util.GetValueOfDecimal(row["delivered_qty"]);
                        decimal pendingQty = Math.Max(0m, orderedQty - deliveredQty);

                        // Currency conversion to schema currency
                        decimal convertedValue = poValue;
                        if (orderCurId != schemaCurrencyId && schemaCurrencyId > 0 && poValue != 0m)
                        {
                            try
                            {
                                decimal conv = MConversionRate.Convert(
                                    ctx,
                                    poValue,
                                    orderCurId,
                                    schemaCurrencyId,
                                    orderDate.HasValue ? orderDate.Value : today,
                                    convTypeId,
                                    clientId,
                                    orgId);
                                if (conv != 0m)
                                {
                                    convertedValue = conv;
                                }
                            }
                            catch (Exception exConv)
                            {
                                _log.Log(Level.WARNING, "VAS_213: Currency conversion failed for PO " + poNo, exConv);
                            }
                        }

                        // Due in next 7 days check
                        bool isDue7Days = promisedDate.HasValue && promisedDate.Value.Date >= today && promisedDate.Value.Date <= todayPlus7;
                        if (isDue7Days)
                        {
                            dueIn7DaysCount++;
                        }

                        expectedCount++;
                        expectedTotalValueConverted += convertedValue;

                        // Short vendor name for 2x2 widget list display
                        string shortVendor = vendorName;
                        if (!string.IsNullOrEmpty(vendorName))
                        {
                            int spaceIdx = vendorName.IndexOf(' ');
                            if (spaceIdx > 0)
                            {
                                shortVendor = vendorName.Substring(0, spaceIdx);
                            }
                        }

                        // Delivery status derivation
                        string delivStatus = deliveredQty > 0m ? (Msg.GetMsg(ctx, "VAS_Partial") ?? "Partial") : (Msg.GetMsg(ctx, "VAS_Pending") ?? "Pending");
                        string delivStatusChip = deliveredQty > 0m ? "chip-warn" : "chip-neutral";

                        // Document status display label
                        string docStatusText = docStatus == "DR" ? (Msg.GetMsg(ctx, "Drafted") ?? "Drafted")
                            : docStatus == "IP" ? (Msg.GetMsg(ctx, "InProcess") ?? "In process")
                            : (Msg.GetMsg(ctx, "DocStatus_" + docStatus) ?? docStatus);
                        string docStatusChip = docStatus == "IP" ? "chip-prop" : "chip-neutral";

                        poRecords.Add(new
                        {
                            PurchaseOrderID = poId,
                            PurchaseOrderNo = poNo,
                            OrderDate = orderDate.HasValue ? orderDate.Value.ToString("yyyy-MM-dd") : "",
                            OrderDateDisplay = orderDate.HasValue ? orderDate.Value.ToString("dd MMM yyyy") : "",
                            PromisedDate = promisedDate.HasValue ? promisedDate.Value.ToString("yyyy-MM-dd") : "",
                            PromisedDateDisplay = promisedDate.HasValue ? promisedDate.Value.ToString("dd MMM yyyy") : "",
                            PromisedDateShort = promisedDate.HasValue ? promisedDate.Value.ToString("dd MMM") : "",
                            VendorName = string.IsNullOrEmpty(vendorName) ? "—" : vendorName,
                            ShortVendorName = string.IsNullOrEmpty(shortVendor) ? "—" : shortVendor,
                            WarehouseName = string.IsNullOrEmpty(warehouseName) ? "—" : warehouseName,
                            RepresentativeName = string.IsNullOrEmpty(repName) ? "—" : repName,
                            POValue = poValue,
                            POValueConverted = convertedValue,
                            CurrencyID = orderCurId,
                            CurrencySymbol = docCurSymbol,
                            CurrencyISO = docCurIso,
                            OrderedQty = orderedQty,
                            DeliveredQty = deliveredQty,
                            PendingQty = pendingQty,
                            DocumentStatus = docStatus,
                            DocStatusText = docStatusText,
                            DocStatusChip = docStatusChip,
                            DeliveryStatus = delivStatus,
                            DeliveryStatusChip = delivStatusChip,
                            IsDueNext7Days = isDue7Days
                        });
                    }
                }

                // Step 4: Query Total Open Pending PO count across all dates for the "Of open POs: X pending" stat
                int totalOpenPendingCount = 0;
                string totalOpenSql = @"
                    SELECT COUNT(DISTINCT o.C_Order_ID) AS open_count
                    FROM C_Order o
                    INNER JOIN (
                        SELECT C_Order_ID,
                               SUM(COALESCE(QtyOrdered, 0)) AS ordered_qty,
                               SUM(COALESCE(QtyDelivered, 0)) AS delivered_qty
                        FROM C_OrderLine
                        WHERE IsActive = 'Y'
                        GROUP BY C_Order_ID
                    ) q ON q.C_Order_ID = o.C_Order_ID
                    WHERE o.AD_Client_ID = " + clientId + @"
                      AND o.IsActive = 'Y'
                      AND o.IsSOTrx = 'N'
                      AND COALESCE(o.IsReturnTrx, 'N') = 'N'
                      AND o.DocStatus NOT IN ('CL', 'VO', 'RE')
                      AND q.ordered_qty > q.delivered_qty
                      AND o.C_Order_ID IN (@P_ORDER_ACCESS@)";

                totalOpenSql = totalOpenSql.Replace("@P_ORDER_ACCESS@", orderAccessSql);

                IDataReader totalOpenDr = null;
                try
                {
                    totalOpenDr = DB.ExecuteReader(totalOpenSql);
                    if (totalOpenDr != null && totalOpenDr.Read())
                    {
                        totalOpenPendingCount = Util.GetValueOfInt(totalOpenDr["open_count"]);
                    }
                }
                finally
                {
                    if (totalOpenDr != null) { totalOpenDr.Close(); totalOpenDr.Dispose(); }
                }

                var result = new
                {
                    success = true,
                    expectedPOs = expectedCount,
                    expectedValue = expectedTotalValueConverted,
                    dueIn7Days = dueIn7DaysCount,
                    totalOpenPendingPOs = totalOpenPendingCount,
                    targetMonth = targetMonth,
                    targetYear = targetYear,
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
                _log.Log(Level.SEVERE, "VAS_213_POsExpectedThisMonthWidget.GetPOsExpectedThisMonth", ex);
                return Json(JsonConvert.SerializeObject(new { error = Msg.GetMsg(ctx, "Error") ?? "An error occurred while loading data.", success = false }), JsonRequestBehavior.AllowGet);
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

            if (C_Order_ID <= 0)
            {
                return Json(JsonConvert.SerializeObject(new { error = Msg.GetMsg(ctx, "Error") ?? "Invalid Purchase Order ID", success = false }), JsonRequestBehavior.AllowGet);
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
                    WHERE o.C_Order_ID = " + C_Order_ID;

                headerSql = MRole.GetDefault(ctx).AddAccessSQL(headerSql, "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                IDataReader hDr = null;
                string docNo = "";
                string vendor = "";
                DateTime? orderDate = null;
                DateTime? promisedDate = null;
                string docStatus = "";
                string warehouse = "";
                string repName = "";
                string curSymbol = "";
                string curIso = "";
                int precision = 2;
                decimal grandTotal = 0m;

                try
                {
                    hDr = DB.ExecuteReader(headerSql);
                    if (hDr != null && hDr.Read())
                    {
                        docNo = Util.GetValueOfString(hDr["DocumentNo"]);
                        vendor = Util.GetValueOfString(hDr["VendorName"]);
                        orderDate = Util.GetValueOfDateTime(hDr["DateOrdered"]);
                        promisedDate = Util.GetValueOfDateTime(hDr["DatePromised"]);
                        docStatus = Util.GetValueOfString(hDr["DocStatus"]);
                        warehouse = Util.GetValueOfString(hDr["WarehouseName"]);
                        repName = Util.GetValueOfString(hDr["RepName"]);
                        curSymbol = Util.GetValueOfString(hDr["CurSymbol"]);
                        curIso = Util.GetValueOfString(hDr["ISO_Code"]);
                        precision = Util.GetValueOfInt(hDr["StdPrecision"]);
                        grandTotal = Util.GetValueOfDecimal(hDr["GrandTotal"]);
                    }
                    else
                    {
                        return Json(JsonConvert.SerializeObject(new { error = Msg.GetMsg(ctx, "RecordNotFound") ?? "Record not found", success = false }), JsonRequestBehavior.AllowGet);
                    }
                }
                finally
                {
                    if (hDr != null) { hDr.Close(); hDr.Dispose(); }
                }

                // Query Lines
                string linesSql = @"
                    SELECT
                        ol.C_OrderLine_ID AS line_id,
                        ol.Line AS line_no,
                        -- A charge line, or a product that is not of Item type, carries no
                        -- stock movement: the widget shows its name, UOM, ordered, rate and
                        -- amount, and dashes for received / pending / line status.
                        CASE WHEN COALESCE(ol.C_Charge_ID, 0) > 0
                             THEN COALESCE(ch.Name, N'')
                             ELSE COALESCE(p.Name, ol.Description, N'—') END AS product_name,
                        CASE WHEN COALESCE(ol.C_Charge_ID, 0) > 0 THEN 'Y'
                             WHEN ol.M_Product_ID IS NOT NULL AND COALESCE(p.ProductType, 'I') <> 'I' THEN 'Y'
                             ELSE 'N' END AS IsNonStock,
                        p.Value AS product_sku,
                        CASE WHEN COALESCE(ol.M_AttributeSetInstance_ID, 0) > 0
                             THEN COALESCE(asi.Description, N'')
                             ELSE N'' END AS attribute_desc,
                        COALESCE(u.UOMSymbol, u.Name, N'') AS uom_name,
                        COALESCE(ol.QtyOrdered, 0) AS ordered_qty,
                        -- QtyEntered is expressed in the line's own C_UOM_ID (the UOM the buyer
                        -- picked); QtyOrdered / QtyDelivered are in the product's base UOM. The
                        -- widget shows the selected UOM, so quantities are scaled to it.
                        COALESCE(ol.QtyEntered, ol.QtyOrdered, 0) AS QtyEntered,
                        COALESCE(ol.QtyDelivered, 0) AS delivered_qty,
                        COALESCE(ol.PriceActual, 0) AS price_actual,
                        COALESCE(ol.LineNetAmt, COALESCE(ol.QtyOrdered, 0) * COALESCE(ol.PriceActual, 0)) AS line_net_amt
                    FROM C_OrderLine ol
                    LEFT JOIN M_Product p ON p.M_Product_ID = ol.M_Product_ID
                    LEFT JOIN C_Charge ch ON (ch.C_Charge_ID = ol.C_Charge_ID)
                    LEFT JOIN M_AttributeSetInstance asi ON asi.M_AttributeSetInstance_ID = ol.M_AttributeSetInstance_ID
                    LEFT JOIN C_UOM u ON u.C_UOM_ID = ol.C_UOM_ID
                    WHERE ol.C_Order_ID = " + C_Order_ID + @"
                      AND ol.IsActive = 'Y'
                    ORDER BY ol.Line ASC, ol.C_OrderLine_ID ASC";

                linesSql = MRole.GetDefault(ctx).AddAccessSQL(linesSql, "ol", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                var lines = new List<object>();
                decimal totalOrderedQty = 0m;
                decimal totalDeliveredQty = 0m;
                decimal totalLineNetAmt = 0m;

                IDataReader lDr = null;
                try
                {
                    lDr = DB.ExecuteReader(linesSql);
                    while (lDr != null && lDr.Read())
                    {
                        decimal ordered = Util.GetValueOfDecimal(lDr["ordered_qty"]);
                        decimal delivered = Util.GetValueOfDecimal(lDr["delivered_qty"]);
                        decimal price = Util.GetValueOfDecimal(lDr["price_actual"]);
                        decimal amount = Util.GetValueOfDecimal(lDr["line_net_amt"]);
                        decimal pending = Math.Max(0m, ordered - delivered);

                        totalOrderedQty += ordered;
                        totalDeliveredQty += delivered;
                        totalLineNetAmt += amount;

                        // Quantities are shown in the UOM the line was entered in. QtyEntered is in the
                        // line's own C_UOM_ID; QtyOrdered / QtyDelivered are in the product's base UOM,
                        // so delivered is scaled by this line's own entered/ordered ratio. Header
                        // roll-ups above stay in the base UOM - summing mixed UOMs is meaningless.
                        decimal enteredQtyUom = Util.GetValueOfDecimal(lDr["QtyEntered"]);
                        decimal uomRatio = (ordered != 0) ? (enteredQtyUom / ordered) : 1m;
                        ordered = enteredQtyUom;
                        delivered = delivered * uomRatio;
                        // Pending follows the converted figures, not the base-UOM value.
                        pending = Math.Max(0m, ordered - delivered);

                        string lineStatus = "Pending";
                        string lineStatusKey = "VAS_LineStatusPending";
                        string lineStatusChip = "chip-neutral";

                        if (docStatus == "DR")
                        {
                            lineStatus = Msg.GetMsg(ctx, "Drafted") ?? "Drafted";
                            lineStatusKey = "Drafted";
                            lineStatusChip = "chip-neutral";
                        }
                        else if (docStatus == "VO")
                        {
                            lineStatus = Msg.GetMsg(ctx, "Voided") ?? "Voided";
                            lineStatusKey = "Voided";
                            lineStatusChip = "chip-risk";
                        }
                        else if (ordered > 0m && delivered >= ordered)
                        {
                            lineStatus = Msg.GetMsg(ctx, "Received") ?? "Received";
                            lineStatusKey = "VAS_LineStatusReceived";
                            lineStatusChip = "chip-ok";
                        }
                        else if (delivered > 0m && delivered < ordered)
                        {
                            lineStatus = Msg.GetMsg(ctx, "PartialReceived") ?? "Partial received";
                            lineStatusKey = "VAS_LineStatusPartialReceived";
                            lineStatusChip = "chip-warn";
                        }
                        else
                        {
                            lineStatus = Msg.GetMsg(ctx, "Pending") ?? "Pending";
                            lineStatusKey = "VAS_LineStatusPending";
                            lineStatusChip = "chip-neutral";
                        }

                        lines.Add(new
                        {
                            LineID = Util.GetValueOfInt(lDr["line_id"]),
                            LineNo = Util.GetValueOfInt(lDr["line_no"]),
                            ProductName = Util.GetValueOfString(lDr["product_name"]),
                            ProductSKU = Util.GetValueOfString(lDr["product_sku"]),
                            Attribute = Util.GetValueOfString(lDr["attribute_desc"]),
                            UOM = Util.GetValueOfString(lDr["uom_name"]),
                            OrderedQty = ordered,
                            DeliveredQty = delivered,
                            PendingQty = pending,
                            PriceActual = price,
                            LineNetAmt = amount,
                            // Charge / non-Item lines are never received - the client renders dashes
                            // for received, pending and line status.
                            isNonStock = Util.GetValueOfString(lDr["IsNonStock"]) == "Y",
                            LineStatus = lineStatus,
                            LineStatusKey = lineStatusKey,
                            LineStatusChip = lineStatusChip
                        });
                    }
                }
                finally
                {
                    if (lDr != null) { lDr.Close(); lDr.Dispose(); }
                }

                // Delivery Status
                string delivStatus = (docStatus == "CL" || docStatus == "VO")
                    ? (Msg.GetMsg(ctx, "VAS_NotApplicable") ?? "Not applicable")
                    : (totalOrderedQty > 0m && totalDeliveredQty >= totalOrderedQty)
                        ? (Msg.GetMsg(ctx, "VAS_FullyDelivered") ?? "Fully delivered")
                        : (totalDeliveredQty > 0m)
                            ? (Msg.GetMsg(ctx, "VAS_Partial") ?? "Partial")
                            : (Msg.GetMsg(ctx, "VAS_Pending") ?? "Pending");

                // Doc Status Text
                string docStatusText = docStatus == "DR" ? (Msg.GetMsg(ctx, "Drafted") ?? "Drafted")
                    : docStatus == "IP" ? (Msg.GetMsg(ctx, "InProcess") ?? "In process")
                    : docStatus == "CO" ? (Msg.GetMsg(ctx, "Completed") ?? "Completed")
                    : docStatus == "CL" ? (Msg.GetMsg(ctx, "Closed") ?? "Closed")
                    : (Msg.GetMsg(ctx, "DocStatus_" + docStatus) ?? docStatus);

                var result = new
                {
                    success = true,
                    purchaseOrderId = C_Order_ID,
                    purchaseOrderNumber = docNo,
                    vendorName = vendor,
                    orderDateFormatted = orderDate.HasValue ? orderDate.Value.ToString("dd MMM yyyy") : "",
                    promisedDateFormatted = promisedDate.HasValue ? promisedDate.Value.ToString("dd MMM yyyy") : "",
                    warehouseName = warehouse,
                    representativeName = repName,
                    documentStatus = docStatusText,
                    deliveryStatus = delivStatus,
                    currencySymbol = curSymbol,
                    currencyIso = curIso,
                    stdPrecision = precision,
                    totalOrderedQty = totalOrderedQty,
                    totalDeliveredQty = totalDeliveredQty,
                    totalPendingQty = Math.Max(0m, totalOrderedQty - totalDeliveredQty),
                    totalAmount = (grandTotal > 0m) ? grandTotal : totalLineNetAmt,
                    lines = lines
                };

                return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                _log.Log(Level.SEVERE, "VAS_213_POsExpectedThisMonthWidget.GetPOLines", ex);
                return Json(JsonConvert.SerializeObject(new { error = Msg.GetMsg(ctx, "Error") ?? "An error occurred while loading lines.", success = false }), JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Retrieves full Purchase Order header and lines detail for the single record modal.
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
                        o.GrandTotal AS grand_total,
                        bp.Name AS vendor_name,
                        w.Name AS warehouse_name,
                        u.Name AS created_by_name,
                        o.Created AS created_on,
                        pt.Name AS payment_term_name,
                        c.CurSymbol AS cur_symbol,
                        c.ISO_Code AS cur_iso,
                        COALESCE(c.StdPrecision, 2) AS std_precision
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
                        string rawDocStatus = Util.GetValueOfString(hdrDr["doc_status"]);

                        string docStatusDisplay = rawDocStatus == "DR" ? (Msg.GetMsg(ctx, "Drafted") ?? "Drafted")
                            : rawDocStatus == "IP" ? (Msg.GetMsg(ctx, "InProcess") ?? "In process")
                            : rawDocStatus == "CO" ? (Msg.GetMsg(ctx, "Completed") ?? "Completed")
                            : rawDocStatus == "CL" ? (Msg.GetMsg(ctx, "Closed") ?? "Closed")
                            : (Msg.GetMsg(ctx, "DocStatus_" + rawDocStatus) ?? rawDocStatus);

                        headerObj = new
                        {
                            PurchaseOrderID = Util.GetValueOfInt(hdrDr["purchase_order_id"]),
                            PurchaseOrderNo = Util.GetValueOfString(hdrDr["purchase_order_number"]),
                            OrderDate = orderDate.HasValue ? orderDate.Value.ToString("yyyy-MM-dd") : "",
                            OrderDateDisplay = orderDate.HasValue ? orderDate.Value.ToString("dd MMM yyyy") : "",
                            PromisedDate = promisedDate.HasValue ? promisedDate.Value.ToString("yyyy-MM-dd") : "",
                            PromisedDateDisplay = promisedDate.HasValue ? promisedDate.Value.ToString("dd MMM yyyy") : "",
                            DocStatus = rawDocStatus,
                            DocStatusDisplay = docStatusDisplay,
                            GrandTotal = Util.GetValueOfDecimal(hdrDr["grand_total"]),
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
                _log.Log(Level.SEVERE, "VAS_213_POsExpectedThisMonthWidget.GetPODetail", ex);
                return Json(JsonConvert.SerializeObject(new { error = Msg.GetMsg(ctx, "Error") ?? "Error", success = false }), JsonRequestBehavior.AllowGet);
            }
        }
    }
}
