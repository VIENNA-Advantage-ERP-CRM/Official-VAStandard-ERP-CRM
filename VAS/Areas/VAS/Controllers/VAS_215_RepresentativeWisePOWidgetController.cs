/************************************************************
 * Module Name    : VAS
 * Purpose        : Representative Wise PO Widget (Purchase Order Dashboard - Widget 13)
 * Description    : Ranks purchase order spend and count by buying representative (C_Order.SalesRep_ID)
 *                  for the selected month, 5 rows per widget page, with drill-down
 *                  representative purchase orders modal, average cycle time calculation,
 *                  and PO lines inspection.
 * Created Date   : 17 Aug 2026
 * ID Prefix      : VAS_215_
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
    public class VAS_215_RepresentativeWisePOWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_215_RepresentativeWisePOWidgetController).FullName);

        [Authorize]
        public ActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// Endpoint 1: Retrieves Representative Wise PO summary list for the specified month and year,
        /// ranked by converted GrandTotal spend descending, along with overall monthly totals.
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetRepresentativeWisePO(int month, int year)
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

                // Step 2: Fetch eligible Purchase Orders for the month grouped by Representative
                string sql = @"
                    SELECT
                        o.C_Order_ID,
                        o.SalesRep_ID AS representative_id,
                        rep.Name AS representative_name,
                        o.DateOrdered,
                        o.DatePromised,
                        o.OrderCompletionDatetime,
                        o.DocStatus,
                        o.C_Currency_ID,
                        o.C_ConversionType_ID,
                        o.AD_Client_ID,
                        o.AD_Org_ID,
                        COALESCE(o.GrandTotal, 0) AS po_value_doc
                    FROM C_Order o
                    INNER JOIN AD_User rep
                        ON rep.AD_User_ID = o.SalesRep_ID
                    WHERE o.AD_Client_ID = @ClientID
                      AND o.IsActive = 'Y'
                      AND o.IsSOTrx = 'N'
                      AND COALESCE(o.IsReturnTrx, 'N') = 'N'
                      AND o.SalesRep_ID IS NOT NULL
                      AND o.DocStatus NOT IN ('VO', 'RE')
                      AND o.DateOrdered >= @MonthStart
                      AND o.DateOrdered < @MonthEnd
                    ORDER BY rep.Name, o.DateOrdered, o.C_Order_ID";

                SqlParameter[] parameters = new SqlParameter[]
                {
                    new SqlParameter("@ClientID", clientId),
                    new SqlParameter("@MonthStart", monthStart),
                    new SqlParameter("@MonthEnd", monthEnd)
                };

                Dictionary<int, RepresentativeAggregate> repMap = new Dictionary<int, RepresentativeAggregate>();
                decimal totalMonthConvertedValue = 0m;
                int totalMonthPOCount = 0;
                HashSet<int> distinctMonthPOs = new HashSet<int>();

                using (IDataReader dr = DB.ExecuteReader(sql, parameters, null))
                {
                    while (dr != null && dr.Read())
                    {
                        int orderId = Util.GetValueOfInt(dr["C_Order_ID"]);
                        int repId = Util.GetValueOfInt(dr["representative_id"]);
                        string repName = Util.GetValueOfString(dr["representative_name"]);
                        DateTime? orderDate = Util.GetValueOfDateTime(dr["DateOrdered"]);
                        DateTime? completionDate = Util.GetValueOfDateTime(dr["OrderCompletionDatetime"]);
                        int curId = Util.GetValueOfInt(dr["C_Currency_ID"]);
                        int convTypeId = Util.GetValueOfInt(dr["C_ConversionType_ID"]);
                        int orgId = Util.GetValueOfInt(dr["AD_Org_ID"]);
                        decimal docValue = Util.GetValueOfDecimal(dr["po_value_doc"]);

                        // Convert currency server-side to functional accounting currency
                        decimal convertedValue = ConvertAmount(ctx, docValue, curId, curInfo.CurrencyId, orderDate, convTypeId, clientId, orgId);

                        totalMonthConvertedValue += convertedValue;
                        if (!distinctMonthPOs.Contains(orderId))
                        {
                            distinctMonthPOs.Add(orderId);
                            totalMonthPOCount++;
                        }

                        if (!repMap.ContainsKey(repId))
                        {
                            repMap[repId] = new RepresentativeAggregate
                            {
                                RepresentativeId = repId,
                                RepresentativeName = repName,
                                TotalConvertedValue = 0m,
                                DistinctOrders = new HashSet<int>(),
                                CycleDaysList = new List<int>()
                            };
                        }

                        RepresentativeAggregate rAgg = repMap[repId];
                        rAgg.TotalConvertedValue += convertedValue;
                        rAgg.DistinctOrders.Add(orderId);

                        // Cycle time calculation: whole days from DateOrdered to OrderCompletionDatetime
                        if (orderDate.HasValue && completionDate.HasValue)
                        {
                            int days = (int)Math.Round((completionDate.Value.Date - orderDate.Value.Date).TotalDays);
                            if (days >= 0)
                            {
                                rAgg.CycleDaysList.Add(days);
                            }
                        }
                    }
                }

                // Rank all representatives by Converted Value DESC, then PO Count DESC, then Representative Name ASC
                List<RepresentativeAggregate> allReps = new List<RepresentativeAggregate>(repMap.Values);
                allReps.Sort((a, b) =>
                {
                    int cmp = b.TotalConvertedValue.CompareTo(a.TotalConvertedValue);
                    if (cmp != 0) return cmp;
                    int countCmp = b.DistinctOrders.Count.CompareTo(a.DistinctOrders.Count);
                    if (countCmp != 0) return countCmp;
                    return string.Compare(a.RepresentativeName, b.RepresentativeName, StringComparison.OrdinalIgnoreCase);
                });

                decimal topRepValue = allReps.Count > 0 ? allReps[0].TotalConvertedValue : 0m;

                List<object> repList = new List<object>();
                for (int i = 0; i < allReps.Count; i++)
                {
                    RepresentativeAggregate r = allReps[i];
                    decimal share = totalMonthConvertedValue > 0 ? (r.TotalConvertedValue / totalMonthConvertedValue) : 0m;
                    int pct = topRepValue > 0 ? (int)Math.Round((r.TotalConvertedValue / topRepValue) * 100m, MidpointRounding.AwayFromZero) : 0;

                    int? avgCycleDays = null;
                    string avgCycleText = "—";
                    if (r.CycleDaysList.Count > 0)
                    {
                        double sumDays = 0;
                        for (int k = 0; k < r.CycleDaysList.Count; k++)
                        {
                            sumDays += r.CycleDaysList[k];
                        }
                        avgCycleDays = (int)Math.Round(sumDays / r.CycleDaysList.Count);
                        avgCycleText = avgCycleDays.Value == 1 ? "1 day" : avgCycleDays.Value + " days";
                    }

                    repList.Add(new
                    {
                        representativeId = r.RepresentativeId,
                        name = r.RepresentativeName,
                        value = r.TotalConvertedValue,
                        pos = r.DistinctOrders.Count,
                        share = Math.Round(share, 4),
                        pct = Math.Max(0, Math.Min(100, pct)),
                        avgCycle = avgCycleDays,
                        avgCycleText = avgCycleText
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
                    representatives = repList
                };

                return Json(JsonConvert.SerializeObject(response), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_215_RepresentativeWisePOWidget.GetRepresentativeWisePO", ex);
                return Json(JsonConvert.SerializeObject(new { error = Msg.GetMsg(ctx, "Error") ?? "Error", message = ex.Message }), JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Endpoint 2: Retrieves detailed Purchase Orders list for a specific representative
        /// in the selected month and year for the drill-down modal table.
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetRepresentativePurchaseOrders(int representativeId, int month, int year)
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

                // Query purchase orders for the representative
                string sql = @"
                    SELECT
                        o.C_Order_ID,
                        o.DocumentNo,
                        o.DateOrdered,
                        o.DatePromised,
                        o.OrderCompletionDatetime,
                        o.DocStatus,
                        o.C_Currency_ID,
                        o.C_ConversionType_ID,
                        o.AD_Org_ID,
                        bp.Name AS VendorName,
                        wh.Name AS WarehouseName,
                        rep.Name AS SalesRepName,
                        COALESCE(o.GrandTotal, 0) AS PoValueDoc,
                        (SELECT COUNT(ol.C_OrderLine_ID) FROM C_OrderLine ol WHERE ol.C_Order_ID = o.C_Order_ID AND ol.IsActive = 'Y') AS LineCount,
                        (SELECT SUM(COALESCE(ol.QtyOrdered, 0)) FROM C_OrderLine ol WHERE ol.C_Order_ID = o.C_Order_ID AND ol.IsActive = 'Y') AS OrderedQty,
                        (SELECT SUM(COALESCE(ol.QtyDelivered, 0)) FROM C_OrderLine ol WHERE ol.C_Order_ID = o.C_Order_ID AND ol.IsActive = 'Y') AS DeliveredQty
                    FROM C_Order o
                    INNER JOIN C_BPartner bp ON bp.C_BPartner_ID = o.C_BPartner_ID
                    LEFT JOIN M_Warehouse wh ON wh.M_Warehouse_ID = o.M_Warehouse_ID
                    LEFT JOIN AD_User rep ON rep.AD_User_ID = o.SalesRep_ID
                    WHERE o.AD_Client_ID = @ClientID
                      AND o.IsActive = 'Y'
                      AND o.IsSOTrx = 'N'
                      AND COALESCE(o.IsReturnTrx, 'N') = 'N'
                      AND o.DocStatus NOT IN ('VO', 'RE')
                      AND o.SalesRep_ID = @SalesRepID
                      AND o.DateOrdered >= @MonthStart
                      AND o.DateOrdered < @MonthEnd
                    ORDER BY o.DateOrdered DESC, o.DocumentNo DESC";

                SqlParameter[] parameters = new SqlParameter[]
                {
                    new SqlParameter("@ClientID", clientId),
                    new SqlParameter("@SalesRepID", representativeId),
                    new SqlParameter("@MonthStart", monthStart),
                    new SqlParameter("@MonthEnd", monthEnd)
                };

                List<object> poList = new List<object>();
                decimal repTotalValue = 0m;
                List<int> cycleDaysList = new List<int>();

                using (IDataReader dr = DB.ExecuteReader(sql, parameters, null))
                {
                    while (dr != null && dr.Read())
                    {
                        int orderId = Util.GetValueOfInt(dr["C_Order_ID"]);
                        string docNo = Util.GetValueOfString(dr["DocumentNo"]);
                        DateTime? dateOrdered = Util.GetValueOfDateTime(dr["DateOrdered"]);
                        DateTime? datePromised = Util.GetValueOfDateTime(dr["DatePromised"]);
                        DateTime? completionDate = Util.GetValueOfDateTime(dr["OrderCompletionDatetime"]);
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

                        decimal convertedValue = ConvertAmount(ctx, docValue, curId, curInfo.CurrencyId, dateOrdered, convTypeId, clientId, orgId);
                        repTotalValue += convertedValue;

                        if (dateOrdered.HasValue && completionDate.HasValue)
                        {
                            int days = (int)Math.Round((completionDate.Value.Date - dateOrdered.Value.Date).TotalDays);
                            if (days >= 0)
                            {
                                cycleDaysList.Add(days);
                            }
                        }

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
                                docStatusText = "In process";
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
                            case "RE":
                                docStatusText = "Reversed";
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
                        if (docStatus == "CL" || docStatus == "VO" || docStatus == "RE")
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
                        else if (docStatus == "CO" && lineCount == 0)
                        {
                            delivStatusText = "Fully delivered";
                            delivStatusChip = "chip-ok";
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

                string avgCycleText = "—";
                if (cycleDaysList.Count > 0)
                {
                    double sumDays = 0;
                    for (int k = 0; k < cycleDaysList.Count; k++)
                    {
                        sumDays += cycleDaysList[k];
                    }
                    int avgDays = (int)Math.Round(sumDays / cycleDaysList.Count);
                    avgCycleText = avgDays == 1 ? "1 day" : avgDays + " days";
                }

                return Json(JsonConvert.SerializeObject(new
                {
                    success = true,
                    curSymbol = curInfo.CurSymbol,
                    curIso = curInfo.CurIso,
                    stdPrecision = curInfo.StdPrecision,
                    repTotalValue = repTotalValue,
                    posRaised = poList.Count,
                    avgCycleText = avgCycleText,
                    orders = poList
                }), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_215_RepresentativeWisePOWidget.GetRepresentativePurchaseOrders", ex);
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
                Log.Log(Level.SEVERE, "VAS_215_RepresentativeWisePOWidget.GetPurchaseOrderLines", ex);
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

        private class RepresentativeAggregate
        {
            public int RepresentativeId { get; set; }
            public string RepresentativeName { get; set; }
            public decimal TotalConvertedValue { get; set; }
            public HashSet<int> DistinctOrders { get; set; }
            public List<int> CycleDaysList { get; set; }
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
