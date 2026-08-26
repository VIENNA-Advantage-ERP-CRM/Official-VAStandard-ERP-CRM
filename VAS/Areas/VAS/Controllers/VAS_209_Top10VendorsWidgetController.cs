/************************************************************
 * Module Name    : VAS
 * Purpose        : Top 10 Vendors Widget (Purchase Order Dashboard - Widget 07)
 * Description    : Ranks top ten vendors by converted Purchase Order line value
 *                  for the selected month, 5 rows per widget page, with drill-down
 *                  vendor purchase orders modal and PO lines inspection.
 * Created Date   : 17 Aug 2026
 * ID Prefix      : VAS_209_
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
    public class VAS_209_Top10VendorsWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_209_Top10VendorsWidgetController).FullName);

        [Authorize]
        public ActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// Endpoint 1: Retrieves Top 10 Vendors by converted Purchase Order spend
        /// for the specified month and year, along with overall monthly totals.
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetTopVendors(int month, int year)
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null)
            {
                return Json(JsonConvert.SerializeObject(new { error = "Session Expired" }), JsonRequestBehavior.AllowGet);
            }

            int clientId = ctx.GetAD_Client_ID();
            int selectedYear = year > 0 ? year : DateTime.Now.Year;
            int selectedMonth = (month >= 1 && month <= 12) ? month : DateTime.Now.Month;
            DateTime monthStart = new DateTime(selectedYear, selectedMonth, 1);
            DateTime monthEnd = monthStart.AddMonths(1);

            try
            {
                // Step 1: Resolve Client Accounting Schema Functional Currency
                CurrencyInfo curInfo = GetClientFunctionalCurrency(ctx, clientId);

                // Step 2: Fetch and aggregate eligible Purchase Orders for the month
                string sql = @"
                    WITH po_data AS (
                        SELECT
                            o.C_Order_ID,
                            o.C_BPartner_ID,
                            o.DateOrdered,
                            o.DatePromised,
                            o.C_Currency_ID,
                            o.C_ConversionType_ID,
                            o.AD_Client_ID,
                            o.AD_Org_ID,
                            SUM(COALESCE(ol.LineNetAmt, 0)) AS po_value_document_currency,
                            SUM(COALESCE(ol.QtyOrdered, 0)) AS ordered_qty,
                            SUM(COALESCE(ol.QtyDelivered, 0)) AS delivered_qty
                        FROM C_Order o
                        INNER JOIN C_OrderLine ol
                            ON ol.C_Order_ID = o.C_Order_ID
                           AND ol.IsActive = 'Y'
                        WHERE o.AD_Client_ID = @ClientID
                          AND o.IsActive = 'Y'
                          AND o.IsSOTrx = 'N'
                          AND COALESCE(o.IsReturnTrx, 'N') = 'N'
                          AND o.DocStatus <> 'VO'
                          AND o.DateOrdered >= @MonthStart
                          AND o.DateOrdered < @MonthEnd
                        GROUP BY
                            o.C_Order_ID,
                            o.C_BPartner_ID,
                            o.DateOrdered,
                            o.DatePromised,
                            o.C_Currency_ID,
                            o.C_ConversionType_ID,
                            o.AD_Client_ID,
                            o.AD_Org_ID
                    )
                    SELECT
                        pd.C_Order_ID AS purchase_order_id,
                        pd.C_BPartner_ID AS vendor_id,
                        bp.Name AS vendor_name,
                        pd.DateOrdered AS order_date,
                        pd.DatePromised AS promised_date,
                        pd.C_Currency_ID AS currency_id,
                        pd.C_ConversionType_ID AS conversion_type_id,
                        pd.AD_Client_ID,
                        pd.AD_Org_ID,
                        pd.po_value_document_currency,
                        pd.ordered_qty,
                        pd.delivered_qty,
                        (
                            SELECT MAX(io.MovementDate)
                            FROM M_InOut io
                            WHERE io.C_Order_ID = pd.C_Order_ID
                              AND io.IsActive = 'Y'
                              AND io.IsSOTrx = 'N'
                              AND COALESCE(io.IsReturnTrx, 'N') = 'N'
                              AND io.DocStatus = 'CO'
                        ) AS last_receipt_date
                    FROM po_data pd
                    INNER JOIN C_BPartner bp
                        ON bp.C_BPartner_ID = pd.C_BPartner_ID
                    ORDER BY bp.Name, pd.DateOrdered, pd.C_Order_ID";

                SqlParameter[] parameters = new SqlParameter[]
                {
                    new SqlParameter("@ClientID", clientId),
                    new SqlParameter("@MonthStart", monthStart),
                    new SqlParameter("@MonthEnd", monthEnd)
                };

                Dictionary<int, VendorAggregate> vendorMap = new Dictionary<int, VendorAggregate>();
                decimal totalMonthConvertedValue = 0m;
                int totalMonthPOCount = 0;
                HashSet<int> distinctMonthPOs = new HashSet<int>();

                using (IDataReader dr = DB.ExecuteReader(sql, parameters, null))
                {
                    while (dr != null && dr.Read())
                    {
                        int orderId = Util.GetValueOfInt(dr["purchase_order_id"]);
                        int vendorId = Util.GetValueOfInt(dr["vendor_id"]);
                        string vendorName = Util.GetValueOfString(dr["vendor_name"]);
                        DateTime? orderDate = Util.GetValueOfDateTime(dr["order_date"]);
                        DateTime? promisedDate = Util.GetValueOfDateTime(dr["promised_date"]);
                        DateTime? lastReceiptDate = Util.GetValueOfDateTime(dr["last_receipt_date"]);
                        int curId = Util.GetValueOfInt(dr["currency_id"]);
                        int convTypeId = Util.GetValueOfInt(dr["conversion_type_id"]);
                        int orgId = Util.GetValueOfInt(dr["AD_Org_ID"]);
                        decimal docValue = Util.GetValueOfDecimal(dr["po_value_document_currency"]);
                        decimal orderedQty = Util.GetValueOfDecimal(dr["ordered_qty"]);
                        decimal deliveredQty = Util.GetValueOfDecimal(dr["delivered_qty"]);

                        // Convert currency server-side to functional accounting currency
                        decimal convertedValue = ConvertAmount(ctx, docValue, curId, curInfo.CurrencyId, orderDate, convTypeId, clientId, orgId);

                        totalMonthConvertedValue += convertedValue;
                        if (!distinctMonthPOs.Contains(orderId))
                        {
                            distinctMonthPOs.Add(orderId);
                            totalMonthPOCount++;
                        }

                        // On-Time Delivery calculation for fully received POs
                        bool isFullyReceived = orderedQty > 0 && deliveredQty >= orderedQty;
                        bool hasPromisedAndReceipt = promisedDate.HasValue && lastReceiptDate.HasValue;
                        bool isOnTime = false;
                        bool isEligibleForOnTime = false;

                        if (isFullyReceived && hasPromisedAndReceipt)
                        {
                            isEligibleForOnTime = true;
                            if (lastReceiptDate.Value.Date <= promisedDate.Value.Date)
                            {
                                isOnTime = true;
                            }
                        }

                        if (!vendorMap.ContainsKey(vendorId))
                        {
                            vendorMap[vendorId] = new VendorAggregate
                            {
                                VendorId = vendorId,
                                VendorName = vendorName,
                                TotalConvertedValue = 0m,
                                DistinctOrders = new HashSet<int>(),
                                EligibleOnTimeCount = 0,
                                OnTimeCount = 0
                            };
                        }

                        VendorAggregate vAgg = vendorMap[vendorId];
                        vAgg.TotalConvertedValue += convertedValue;
                        vAgg.DistinctOrders.Add(orderId);
                        if (isEligibleForOnTime)
                        {
                            vAgg.EligibleOnTimeCount++;
                            if (isOnTime)
                            {
                                vAgg.OnTimeCount++;
                            }
                        }
                    }
                }

                // Rank all vendors by Converted Value DESC, then PO Count DESC, then Vendor Name ASC
                List<VendorAggregate> allVendors = new List<VendorAggregate>(vendorMap.Values);
                allVendors.Sort((a, b) =>
                {
                    int cmp = b.TotalConvertedValue.CompareTo(a.TotalConvertedValue);
                    if (cmp != 0) return cmp;
                    int countCmp = b.DistinctOrders.Count.CompareTo(a.DistinctOrders.Count);
                    if (countCmp != 0) return countCmp;
                    return string.Compare(a.VendorName, b.VendorName, StringComparison.OrdinalIgnoreCase);
                });

                // Top 10 only
                int topCount = Math.Min(10, allVendors.Count);
                decimal topVendorValue = topCount > 0 ? allVendors[0].TotalConvertedValue : 0m;

                List<object> topVendorsList = new List<object>();
                for (int i = 0; i < topCount; i++)
                {
                    VendorAggregate v = allVendors[i];
                    decimal share = totalMonthConvertedValue > 0 ? (v.TotalConvertedValue / totalMonthConvertedValue) : 0m;
                    int pct = topVendorValue > 0 ? (int)Math.Round((v.TotalConvertedValue / topVendorValue) * 100m, MidpointRounding.AwayFromZero) : 0;
                    
                    decimal onTimePct = v.EligibleOnTimeCount > 0
                        ? Math.Round(((decimal)v.OnTimeCount / v.EligibleOnTimeCount) * 100m, 1)
                        : 100.0m;

                    topVendorsList.Add(new
                    {
                        vendorId = v.VendorId,
                        name = v.VendorName,
                        value = v.TotalConvertedValue,
                        pos = v.DistinctOrders.Count,
                        share = Math.Round(share, 4),
                        pct = Math.Max(0, Math.Min(100, pct)),
                        onTimePct = onTimePct
                    });
                }

                var response = new
                {
                    success = true,
                    curSymbol = curInfo.CurSymbol,
                    curIso = curInfo.CurIso,
                    stdPrecision = curInfo.StdPrecision,
                    totalMonthValue = totalMonthConvertedValue,
                    totalMonthPOs = totalMonthPOCount,
                    vendors = topVendorsList
                };

                return Json(JsonConvert.SerializeObject(response), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_209_Top10VendorsWidget.GetTopVendors", ex);
                return Json(JsonConvert.SerializeObject(new { error = Msg.GetMsg(ctx, "Error") ?? "Error", message = ex.Message }), JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Endpoint 2: Retrieves detailed Purchase Orders list for a specific vendor
        /// in the selected month and year for the drill-down modal table.
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetVendorPurchaseOrders(int vendorId, int month, int year)
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null)
            {
                return Json(JsonConvert.SerializeObject(new { error = "Session Expired" }), JsonRequestBehavior.AllowGet);
            }

            int clientId = ctx.GetAD_Client_ID();
            int selectedYear = year > 0 ? year : DateTime.Now.Year;
            int selectedMonth = (month >= 1 && month <= 12) ? month : DateTime.Now.Month;
            DateTime monthStart = new DateTime(selectedYear, selectedMonth, 1);
            DateTime monthEnd = monthStart.AddMonths(1);

            try
            {
                CurrencyInfo curInfo = GetClientFunctionalCurrency(ctx, clientId);

                string sql = @"
                    SELECT
                        o.C_Order_ID,
                        o.DocumentNo,
                        o.DateOrdered,
                        o.DatePromised,
                        o.DocStatus,
                        o.C_Currency_ID,
                        o.C_ConversionType_ID,
                        o.AD_Org_ID,
                        bp.Name AS VendorName,
                        wh.Name AS WarehouseName,
                        usr.Name AS SalesRepName,
                        SUM(COALESCE(ol.LineNetAmt, 0)) AS PoValueDoc,
                        SUM(COALESCE(ol.QtyOrdered, 0)) AS OrderedQty,
                        SUM(COALESCE(ol.QtyDelivered, 0)) AS DeliveredQty,
                        COUNT(ol.C_OrderLine_ID) AS LineCount,
                        (
                            SELECT MAX(io.MovementDate)
                            FROM M_InOut io
                            WHERE io.C_Order_ID = o.C_Order_ID
                              AND io.IsActive = 'Y'
                              AND io.IsSOTrx = 'N'
                              AND COALESCE(io.IsReturnTrx, 'N') = 'N'
                              AND io.DocStatus = 'CO'
                        ) AS LastReceiptDate
                    FROM C_Order o
                    INNER JOIN C_BPartner bp ON bp.C_BPartner_ID = o.C_BPartner_ID
                    LEFT JOIN M_Warehouse wh ON wh.M_Warehouse_ID = o.M_Warehouse_ID
                    LEFT JOIN AD_User usr ON usr.AD_User_ID = o.SalesRep_ID
                    LEFT JOIN C_OrderLine ol ON ol.C_Order_ID = o.C_Order_ID AND ol.IsActive = 'Y'
                    WHERE o.AD_Client_ID = @ClientID
                      AND o.IsActive = 'Y'
                      AND o.IsSOTrx = 'N'
                      AND COALESCE(o.IsReturnTrx, 'N') = 'N'
                      AND o.DocStatus <> 'VO'
                      AND o.C_BPartner_ID = @VendorID
                      AND o.DateOrdered >= @MonthStart
                      AND o.DateOrdered < @MonthEnd
                    GROUP BY
                        o.C_Order_ID,
                        o.DocumentNo,
                        o.DateOrdered,
                        o.DatePromised,
                        o.DocStatus,
                        o.C_Currency_ID,
                        o.C_ConversionType_ID,
                        o.AD_Org_ID,
                        bp.Name,
                        wh.Name,
                        usr.Name
                    ORDER BY o.DateOrdered DESC, o.DocumentNo DESC";

                SqlParameter[] parameters = new SqlParameter[]
                {
                    new SqlParameter("@ClientID", clientId),
                    new SqlParameter("@VendorID", vendorId),
                    new SqlParameter("@MonthStart", monthStart),
                    new SqlParameter("@MonthEnd", monthEnd)
                };

                List<object> poList = new List<object>();

                using (IDataReader dr = DB.ExecuteReader(sql, parameters, null))
                {
                    while (dr != null && dr.Read())
                    {
                        int orderId = Util.GetValueOfInt(dr["C_Order_ID"]);
                        string docNo = Util.GetValueOfString(dr["DocumentNo"]);
                        DateTime? dateOrdered = Util.GetValueOfDateTime(dr["DateOrdered"]);
                        DateTime? datePromised = Util.GetValueOfDateTime(dr["DatePromised"]);
                        string docStatus = Util.GetValueOfString(dr["DocStatus"]);
                        int curId = Util.GetValueOfInt(dr["C_Currency_ID"]);
                        int convTypeId = Util.GetValueOfInt(dr["C_ConversionType_ID"]);
                        int orgId = Util.GetValueOfInt(dr["AD_Org_ID"]);
                        string vendorName = Util.GetValueOfString(dr["VendorName"]);
                        string whName = Util.GetValueOfString(dr["WarehouseName"]);
                        string repName = Util.GetValueOfString(dr["SalesRepName"]);
                        decimal docValue = Util.GetValueOfDecimal(dr["PoValueDoc"]);
                        decimal orderedQty = Util.GetValueOfDecimal(dr["OrderedQty"]);
                        decimal deliveredQty = Util.GetValueOfDecimal(dr["DeliveredQty"]);
                        int lineCount = Util.GetValueOfInt(dr["LineCount"]);
                        DateTime? lastReceipt = Util.GetValueOfDateTime(dr["LastReceiptDate"]);

                        decimal convertedValue = ConvertAmount(ctx, docValue, curId, curInfo.CurrencyId, dateOrdered, convTypeId, clientId, orgId);

                        // Determine Document Status display and chip
                        string docStatusText;
                        string docStatusChip;
                        switch (docStatus)
                        {
                            case "DR":
                                docStatusText = "Drafted";
                                docStatusChip = "chip-neutral";
                                break;
                            case "IP":
                                docStatusText = "In Progress";
                                docStatusChip = "chip-prop";
                                break;
                            case "CO":
                                docStatusText = "Completed";
                                docStatusChip = "chip-ok";
                                break;
                            case "CL":
                                docStatusText = "Closed";
                                docStatusChip = "chip-ok";
                                break;
                            case "VO":
                                docStatusText = "Voided";
                                docStatusChip = "chip-risk";
                                break;
                            default:
                                docStatusText = docStatus;
                                docStatusChip = "chip-neutral";
                                break;
                        }

                        // Determine Delivery Status display and chip
                        string delivStatusText;
                        string delivStatusChip;
                        if (docStatus == "CL" || docStatus == "VO")
                        {
                            delivStatusText = "Not applicable";
                            delivStatusChip = "chip-neutral";
                        }
                        else if (orderedQty > 0 && deliveredQty >= orderedQty)
                        {
                            delivStatusText = "Fully delivered";
                            delivStatusChip = "chip-ok";
                        }
                        else if (deliveredQty > 0 && deliveredQty < orderedQty)
                        {
                            delivStatusText = "Partial";
                            delivStatusChip = "chip-warn";
                        }
                        else
                        {
                            delivStatusText = "Pending";
                            delivStatusChip = "chip-neutral";
                        }

                        poList.Add(new
                        {
                            orderId = orderId,
                            po = docNo,
                            dateShort = dateOrdered.HasValue ? dateOrdered.Value.ToString("dd MMM") : "",
                            dateFull = dateOrdered.HasValue ? dateOrdered.Value.ToString("dd MMM yyyy") : "",
                            dateIso = dateOrdered.HasValue ? dateOrdered.Value.ToString("yyyy-MM-dd") : "",
                            vendor = vendorName,
                            wh = string.IsNullOrEmpty(whName) ? "—" : whName,
                            rep = string.IsNullOrEmpty(repName) ? "—" : repName,
                            valueNum = convertedValue,
                            qtyOrdered = orderedQty,
                            qtyDelivered = deliveredQty,
                            qtyPending = Math.Max(0, orderedQty - deliveredQty),
                            items = lineCount,
                            statusText = docStatusText,
                            statusChip = docStatusChip,
                            deliveryText = delivStatusText,
                            deliveryChip = delivStatusChip,
                            expShort = datePromised.HasValue ? datePromised.Value.ToString("dd MMM") : "—",
                            expFull = datePromised.HasValue ? datePromised.Value.ToString("dd MMM yyyy") : "—"
                        });
                    }
                }

                return Json(JsonConvert.SerializeObject(new
                {
                    success = true,
                    curSymbol = curInfo.CurSymbol,
                    curIso = curInfo.CurIso,
                    stdPrecision = curInfo.StdPrecision,
                    orders = poList
                }), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_209_Top10VendorsWidget.GetVendorPurchaseOrders", ex);
                return Json(JsonConvert.SerializeObject(new { error = Msg.GetMsg(ctx, "Error") ?? "Error", message = ex.Message }), JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Endpoint 3: Retrieves individual lines of a Purchase Order for the PO lines detail modal.
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetPurchaseOrderLines(int orderId)
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null)
            {
                return Json(JsonConvert.SerializeObject(new { error = "Session Expired" }), JsonRequestBehavior.AllowGet);
            }

            int clientId = ctx.GetAD_Client_ID();

            try
            {
                CurrencyInfo curInfo = GetClientFunctionalCurrency(ctx, clientId);

                // Fetch Order Header summary
                string orderSql = @"
                    SELECT
                        o.C_Order_ID,
                        o.DocumentNo,
                        o.DateOrdered,
                        o.DatePromised,
                        o.DocStatus,
                        o.C_Currency_ID,
                        o.C_ConversionType_ID,
                        o.AD_Org_ID,
                        bp.Name AS VendorName,
                        wh.Name AS WarehouseName,
                        usr.Name AS CreatedByName,
                        o.Created AS CreatedOn
                    FROM C_Order o
                    INNER JOIN C_BPartner bp ON bp.C_BPartner_ID = o.C_BPartner_ID
                    LEFT JOIN M_Warehouse wh ON wh.M_Warehouse_ID = o.M_Warehouse_ID
                    LEFT JOIN AD_User usr ON usr.AD_User_ID = o.CreatedBy
                    WHERE o.C_Order_ID = @OrderID
                      AND o.AD_Client_ID = @ClientID";

                SqlParameter[] orderParams = new SqlParameter[]
                {
                    new SqlParameter("@OrderID", orderId),
                    new SqlParameter("@ClientID", clientId)
                };

                object orderHeader = null;
                string parentDocStatus = "DR";

                using (IDataReader dr = DB.ExecuteReader(orderSql, orderParams, null))
                {
                    if (dr != null && dr.Read())
                    {
                        parentDocStatus = Util.GetValueOfString(dr["DocStatus"]);
                        DateTime? dtOrd = Util.GetValueOfDateTime(dr["DateOrdered"]);
                        DateTime? dtProm = Util.GetValueOfDateTime(dr["DatePromised"]);
                        DateTime? dtCreated = Util.GetValueOfDateTime(dr["CreatedOn"]);

                        orderHeader = new
                        {
                            orderId = Util.GetValueOfInt(dr["C_Order_ID"]),
                            po = Util.GetValueOfString(dr["DocumentNo"]),
                            vendor = Util.GetValueOfString(dr["VendorName"]),
                            wh = Util.GetValueOfString(dr["WarehouseName"]),
                            createdBy = Util.GetValueOfString(dr["CreatedByName"]),
                            createdOn = dtCreated.HasValue ? dtCreated.Value.ToString("dd MMM yyyy") : "",
                            dateFull = dtOrd.HasValue ? dtOrd.Value.ToString("dd MMM yyyy") : "",
                            expFull = dtProm.HasValue ? dtProm.Value.ToString("dd MMM yyyy") : "—"
                        };
                    }
                }

                // Fetch Order Lines
                string linesSql = @"
                    SELECT
                        ol.C_OrderLine_ID,
                        ol.Line,
                        p.Value AS ProductCode,
                        p.Name AS ProductName,
                        asi.Description AS AttributeDesc,
                        COALESCE(uom.UOMSymbol, uom.Name) AS UomName,
                        COALESCE(ol.QtyOrdered, 0) AS QtyOrdered,
                        COALESCE(ol.QtyDelivered, 0) AS QtyDelivered,
                        COALESCE(ol.PriceActual, 0) AS PriceActual,
                        COALESCE(ol.LineNetAmt, 0) AS LineNetAmt
                    FROM C_OrderLine ol
                    INNER JOIN M_Product p ON p.M_Product_ID = ol.M_Product_ID
                    LEFT JOIN C_UOM uom ON uom.C_UOM_ID = ol.C_UOM_ID
                    LEFT JOIN M_AttributeSetInstance asi ON asi.M_AttributeSetInstance_ID = ol.M_AttributeSetInstance_ID
                    WHERE ol.C_Order_ID = @OrderID
                      AND ol.IsActive = 'Y'
                    ORDER BY ol.Line ASC, ol.C_OrderLine_ID ASC";

                List<object> linesList = new List<object>();

                using (IDataReader dr = DB.ExecuteReader(linesSql, orderParams, null))
                {
                    while (dr != null && dr.Read())
                    {
                        decimal qtyOrd = Util.GetValueOfDecimal(dr["QtyOrdered"]);
                        decimal qtyDel = Util.GetValueOfDecimal(dr["QtyDelivered"]);
                        decimal qtyPend = Math.Max(0, qtyOrd - qtyDel);
                        decimal rate = Util.GetValueOfDecimal(dr["PriceActual"]);
                        decimal amount = Util.GetValueOfDecimal(dr["LineNetAmt"]);

                        // Line status derivation
                        string lineStatus;
                        string lineChip;
                        if (parentDocStatus == "DR")
                        {
                            lineStatus = "Drafted";
                            lineChip = "chip-neutral";
                        }
                        else if (parentDocStatus == "VO")
                        {
                            lineStatus = "Voided";
                            lineChip = "chip-risk";
                        }
                        else if (qtyOrd > 0 && qtyDel >= qtyOrd)
                        {
                            lineStatus = "Received";
                            lineChip = "chip-ok";
                        }
                        else if (qtyDel > 0 && qtyDel < qtyOrd)
                        {
                            lineStatus = "Partial received";
                            lineChip = "chip-warn";
                        }
                        else
                        {
                            lineStatus = "Pending";
                            lineChip = "chip-neutral";
                        }

                        linesList.Add(new
                        {
                            lineId = Util.GetValueOfInt(dr["C_OrderLine_ID"]),
                            lineNo = Util.GetValueOfInt(dr["Line"]),
                            sku = Util.GetValueOfString(dr["ProductCode"]),
                            name = Util.GetValueOfString(dr["ProductName"]),
                            attr = Util.GetValueOfString(dr["AttributeDesc"]),
                            uom = Util.GetValueOfString(dr["UomName"]),
                            qty = qtyOrd,
                            recd = qtyDel,
                            pend = qtyPend,
                            rate = rate,
                            amount = amount,
                            statusText = lineStatus,
                            statusChip = lineChip
                        });
                    }
                }

                return Json(JsonConvert.SerializeObject(new
                {
                    success = true,
                    header = orderHeader,
                    lines = linesList,
                    curSymbol = curInfo.CurSymbol,
                    curIso = curInfo.CurIso,
                    stdPrecision = curInfo.StdPrecision
                }), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_209_Top10VendorsWidget.GetPurchaseOrderLines", ex);
                return Json(JsonConvert.SerializeObject(new { error = Msg.GetMsg(ctx, "Error") ?? "Error", message = ex.Message }), JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Helper to resolve AD_Window_ID by window name for record navigation.
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetWindow_ID(string fields)
        {
            int windowId = 0;
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx != null && !string.IsNullOrEmpty(fields))
            {
                string sql = "SELECT AD_Window_ID FROM AD_Window WHERE Name = @Name AND IsActive = 'Y'";
                object res = DB.ExecuteScalar(sql, new SqlParameter[] { new SqlParameter("@Name", fields) }, null);
                if (res != null && res != DBNull.Value)
                {
                    windowId = Convert.ToInt32(res);
                }
            }
            return Json(JsonConvert.SerializeObject(windowId), JsonRequestBehavior.AllowGet);
        }

        #region Helpers

        private class CurrencyInfo
        {
            public int CurrencyId { get; set; }
            public string CurSymbol { get; set; }
            public string CurIso { get; set; }
            public int StdPrecision { get; set; }
        }

        private class VendorAggregate
        {
            public int VendorId { get; set; }
            public string VendorName { get; set; }
            public decimal TotalConvertedValue { get; set; }
            public HashSet<int> DistinctOrders { get; set; }
            public int EligibleOnTimeCount { get; set; }
            public int OnTimeCount { get; set; }
        }

        private CurrencyInfo GetClientFunctionalCurrency(Ctx ctx, int clientId)
        {
            CurrencyInfo info = new CurrencyInfo
            {
                CurrencyId = 0,
                CurSymbol = "₹",
                CurIso = "INR",
                StdPrecision = 2
            };

            string sql = @"
                SELECT cs.C_Currency_ID, c.CurSymbol, c.StdPrecision, c.ISO_Code
                FROM C_AcctSchema cs
                INNER JOIN AD_ClientInfo ci ON (ci.C_AcctSchema1_ID = cs.C_AcctSchema_ID)
                INNER JOIN C_Currency c ON (cs.C_Currency_ID = c.C_Currency_ID)
                WHERE ci.AD_Client_ID = @ClientID
                  AND ci.IsActive = 'Y'
                  AND cs.IsActive = 'Y'
                  AND c.IsActive = 'Y'";

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "cs", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            SqlParameter[] parameters = new SqlParameter[] { new SqlParameter("@ClientID", clientId) };

            DataSet ds = DB.ExecuteDataset(sql, parameters, null);
            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                info.CurrencyId = Util.GetValueOfInt(ds.Tables[0].Rows[0]["C_Currency_ID"]);
                info.CurSymbol = Util.GetValueOfString(ds.Tables[0].Rows[0]["CurSymbol"]);
                info.StdPrecision = Util.GetValueOfInt(ds.Tables[0].Rows[0]["StdPrecision"]);
                info.CurIso = Util.GetValueOfString(ds.Tables[0].Rows[0]["ISO_Code"]);
            }

            return info;
        }

        private decimal ConvertAmount(Ctx ctx, decimal docValue, int sourceCurId, int targetCurId, DateTime? convDate, int convTypeId, int clientId, int orgId)
        {
            if (sourceCurId == targetCurId || targetCurId == 0 || sourceCurId == 0 || docValue == 0m)
            {
                return docValue;
            }

            DateTime date = convDate.HasValue ? convDate.Value : DateTime.Now;
            try
            {
                decimal converted = MConversionRate.Convert(ctx, docValue, sourceCurId, targetCurId, date, convTypeId, clientId, orgId);
                if (converted != 0m)
                {
                    return converted;
                }
            }
            catch
            {
                // Fallback to original value if rate conversion failed
            }
            return docValue;
        }

        #endregion
    }
}
