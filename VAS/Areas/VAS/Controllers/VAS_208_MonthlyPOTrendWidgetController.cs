/************************************************************
 * Module Name    : VAS
 * Purpose        : Controller for Widget 06: Monthly Purchase Order Trend (VAS_208_MonthlyPOTrendWidget)
 * Created Date   : 17 Aug 2026
 * Created by     : AI-Dev (Builder Agent 6)
 ***********************************************************/
using CoreLibrary.DataBase;
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

namespace VAS.Areas.VAS.Controllers
{
    public class VAS_208_MonthlyPOTrendWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_208_MonthlyPOTrendWidgetController).FullName);

        private class CurrencyInfo
        {
            public int CurrencyId { get; set; }
            public string Symbol { get; set; }
            public string IsoCode { get; set; }
            public int StdPrecision { get; set; }
        }

        private class MonthBucket
        {
            public decimal TotalValue { get; set; }
            public HashSet<int> OrderIds { get; set; } = new HashSet<int>();
            public HashSet<int> VendorIds { get; set; } = new HashSet<int>();
        }

        [Authorize]
        public ActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// Retrieves the client's functional / accounting currency info.
        /// </summary>
        private CurrencyInfo GetBaseCurrencyInfo(Ctx ctx)
        {
            var curInfo = new CurrencyInfo
            {
                CurrencyId = 0,
                Symbol = "$",
                IsoCode = "USD",
                StdPrecision = 2
            };

            int clientId = ctx.GetAD_Client_ID();
            SqlParameter[] dataParams = { new SqlParameter("@ClientID", clientId) };

            string sql = @"SELECT cs.C_Currency_ID, c.CurSymbol, c.ISO_Code, c.StdPrecision
                           FROM C_AcctSchema cs
                           INNER JOIN AD_ClientInfo ci ON (ci.C_AcctSchema1_ID = cs.C_AcctSchema_ID)
                           INNER JOIN C_Currency c ON (cs.C_Currency_ID = c.C_Currency_ID)
                           WHERE ci.AD_Client_ID = @ClientID
                             AND ci.IsActive = 'Y'
                             AND cs.IsActive = 'Y'
                             AND c.IsActive = 'Y'";

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "cs", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            DataSet ds = DB.ExecuteDataset(sql, dataParams, null);
            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                DataRow dr = ds.Tables[0].Rows[0];
                curInfo.CurrencyId = Util.GetValueOfInt(dr["C_Currency_ID"]);
                curInfo.Symbol = Util.GetValueOfString(dr["CurSymbol"]);
                curInfo.IsoCode = Util.GetValueOfString(dr["ISO_Code"]);
                curInfo.StdPrecision = Util.GetValueOfInt(dr["StdPrecision"]);
            }

            return curInfo;
        }

        /// <summary>
        /// Returns monthly PO trend series for the selected From/To range (up to 12 months).
        /// Converted to functional accounting currency server-side.
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetTrendData(int? fromYear, int? fromMonth, int? toYear, int? toMonth)
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null)
            {
                return Json(JsonConvert.SerializeObject(new { error = "Session expired" }), JsonRequestBehavior.AllowGet);
            }

            DateTime now = DateTime.Now;
            int tYear = toYear ?? now.Year;
            int tMonth = toMonth ?? now.Month;

            // Default from: 11 months prior to toYear/toMonth (making 12 months total)
            DateTime toDt = new DateTime(tYear, tMonth, 1);
            DateTime fromDt;
            if (fromYear.HasValue && fromMonth.HasValue)
            {
                fromDt = new DateTime(fromYear.Value, fromMonth.Value, 1);
            }
            else
            {
                fromDt = toDt.AddMonths(-11);
            }

            // Clamping sanity check: if fromDt > toDt, swap or clamp
            if (fromDt > toDt)
            {
                fromDt = toDt;
            }

            // Cap at 12 months
            int totalMonthsSpan = ((toDt.Year - fromDt.Year) * 12) + toDt.Month - fromDt.Month + 1;
            if (totalMonthsSpan > 12)
            {
                fromDt = toDt.AddMonths(-11);
                totalMonthsSpan = 12;
            }
            if (totalMonthsSpan < 1)
            {
                totalMonthsSpan = 1;
            }

            DateTime fromDate = fromDt;
            DateTime toDateExclusive = toDt.AddMonths(1);

            CurrencyInfo baseCur = GetBaseCurrencyInfo(ctx);
            var monthDict = new Dictionary<string, MonthBucket>();

            try
            {
                string sqlFromDate = ToSqlDate(fromDate);
                string sqlToDate = ToSqlDate(toDateExclusive);
                int clientId = ctx.GetAD_Client_ID();

                // Standard Authoritative Query for Widget 06
                string orderAccessSql = @"
                    SELECT o.C_Order_ID, o.DateOrdered, o.C_BPartner_ID, o.C_Currency_ID, o.C_ConversionType_ID, o.AD_Client_ID, o.AD_Org_ID
                    FROM C_Order o
                    WHERE o.AD_Client_ID = " + clientId + @"
                      AND o.IsActive = 'Y'
                      AND o.IsSOTrx = 'N'
                      AND COALESCE(o.IsReturnTrx, 'N') = 'N'
                      AND o.DocStatus <> 'VO'
                      AND o.DateOrdered >= " + sqlFromDate + @"
                      AND o.DateOrdered < " + sqlToDate;

                orderAccessSql = MRole.GetDefault(ctx).AddAccessSQL(orderAccessSql, "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                string sql = @"
                    SELECT
                        base_o.C_Order_ID,
                        base_o.DateOrdered,
                        base_o.C_BPartner_ID,
                        base_o.C_Currency_ID,
                        base_o.C_ConversionType_ID,
                        base_o.AD_Client_ID,
                        base_o.AD_Org_ID,
                        SUM(COALESCE(ol.LineNetAmt, 0)) AS po_value_document_currency
                    FROM (" + orderAccessSql + @") base_o
                    INNER JOIN C_OrderLine ol
                        ON ol.C_Order_ID = base_o.C_Order_ID
                       AND ol.IsActive = 'Y'
                    GROUP BY
                        base_o.C_Order_ID,
                        base_o.DateOrdered,
                        base_o.C_BPartner_ID,
                        base_o.C_Currency_ID,
                        base_o.C_ConversionType_ID,
                        base_o.AD_Client_ID,
                        base_o.AD_Org_ID
                    ORDER BY base_o.DateOrdered, base_o.C_Order_ID";

                using (IDataReader dr = DB.ExecuteReader(sql, null, null))
                {
                    while (dr != null && dr.Read())
                    {
                        int orderId = Util.GetValueOfInt(dr["C_Order_ID"]);
                        DateTime? orderDate = Util.GetValueOfDateTime(dr["DateOrdered"]);
                        int vendorId = Util.GetValueOfInt(dr["C_BPartner_ID"]);
                        int orderCurrencyId = Util.GetValueOfInt(dr["C_Currency_ID"]);
                        int conversionTypeId = Util.GetValueOfInt(dr["C_ConversionType_ID"]);
                        int rowClientId = Util.GetValueOfInt(dr["AD_Client_ID"]);
                        int rowOrgId = Util.GetValueOfInt(dr["AD_Org_ID"]);
                        decimal lineNetTotal = Util.GetValueOfDecimal(dr["po_value_document_currency"]);

                        if (!orderDate.HasValue) { continue; }

                        // Convert money server-side to functional accounting currency
                        decimal convertedAmt = lineNetTotal;
                        if (orderCurrencyId != baseCur.CurrencyId && baseCur.CurrencyId > 0 && orderCurrencyId > 0)
                        {
                            convertedAmt = MConversionRate.Convert(ctx, lineNetTotal, orderCurrencyId, baseCur.CurrencyId,
                                orderDate.Value, conversionTypeId, rowClientId, rowOrgId);
                        }

                        string monthKey = orderDate.Value.ToString("yyyy-MM");
                        if (!monthDict.ContainsKey(monthKey))
                        {
                            monthDict[monthKey] = new MonthBucket();
                        }

                        monthDict[monthKey].TotalValue += convertedAmt;
                        monthDict[monthKey].OrderIds.Add(orderId);
                        if (vendorId > 0)
                        {
                            monthDict[monthKey].VendorIds.Add(vendorId);
                        }
                    }
                }

                // Build full contiguous calendar series from fromDate to toDt
                string[] monthShortNames = { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };
                var series = new List<object>();
                decimal overallTotal = 0;
                int overallPOCount = 0;

                for (int i = 0; i < totalMonthsSpan; i++)
                {
                    DateTime curM = fromDate.AddMonths(i);
                    string key = curM.ToString("yyyy-MM");
                    int mIdx = curM.Month - 1;
                    string shortName = monthShortNames[mIdx];
                    string fullLabel = shortName + " " + curM.Year;

                    decimal val = 0;
                    int poCount = 0;
                    int vendorCount = 0;
                    decimal avgPoVal = 0;

                    if (monthDict.ContainsKey(key))
                    {
                        val = monthDict[key].TotalValue;
                        poCount = monthDict[key].OrderIds.Count;
                        vendorCount = monthDict[key].VendorIds.Count;
                        avgPoVal = poCount > 0 ? (val / poCount) : 0;
                    }

                    overallTotal += val;
                    overallPOCount += poCount;

                    series.Add(new
                    {
                        key = key,
                        year = curM.Year,
                        month = curM.Month,
                        monthIdx = curM.Year * 12 + (curM.Month - 1),
                        shortName = shortName,
                        label = fullLabel,
                        value = val,
                        poCount = poCount,
                        vendorCount = vendorCount,
                        avgPoValue = avgPoVal
                    });
                }

                string fromLabel = monthShortNames[fromDate.Month - 1] + " " + fromDate.Year;
                string toLabel = monthShortNames[toDt.Month - 1] + " " + toDt.Year;

                return Json(JsonConvert.SerializeObject(new
                {
                    success = true,
                    series = series,
                    currency = new
                    {
                        id = baseCur.CurrencyId,
                        symbol = baseCur.Symbol,
                        iso = baseCur.IsoCode,
                        precision = baseCur.StdPrecision
                    },
                    totalValue = overallTotal,
                    totalPOCount = overallPOCount,
                    fromLabel = fromLabel,
                    toLabel = toLabel,
                    monthCount = totalMonthsSpan
                }), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_208_MonthlyPOTrendWidget.GetTrendData", ex);
                return Json(JsonConvert.SerializeObject(new { error = Msg.GetMsg(ctx, "Error") ?? "Error", message = ex.Message }), JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Retrieves the list of Purchase Orders for a specific month drill-down.
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetMonthPODrilldown(int year, int month)
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null)
            {
                return Json(JsonConvert.SerializeObject(new { error = "Session expired" }), JsonRequestBehavior.AllowGet);
            }

            if (year < 2000 || year > 2100 || month < 1 || month > 12)
            {
                return Json(JsonConvert.SerializeObject(new { error = "Invalid date range" }), JsonRequestBehavior.AllowGet);
            }

            DateTime fromDate = new DateTime(year, month, 1);
            DateTime toDateExclusive = fromDate.AddMonths(1);
            CurrencyInfo baseCur = GetBaseCurrencyInfo(ctx);

            try
            {
                string sqlFromDate = ToSqlDate(fromDate);
                string sqlToDate = ToSqlDate(toDateExclusive);
                int clientId = ctx.GetAD_Client_ID();

                string orderAccessSql = @"
                    SELECT o.C_Order_ID, o.DocumentNo, o.DateOrdered, o.DocStatus,
                           o.C_BPartner_ID, o.M_Warehouse_ID, o.SalesRep_ID,
                           o.C_Currency_ID, o.C_ConversionType_ID, o.AD_Client_ID, o.AD_Org_ID
                    FROM C_Order o
                    WHERE o.AD_Client_ID = " + clientId + @"
                      AND o.IsActive = 'Y'
                      AND o.IsSOTrx = 'N'
                      AND COALESCE(o.IsReturnTrx, 'N') = 'N'
                      AND o.DocStatus <> 'VO'
                      AND o.DateOrdered >= " + sqlFromDate + @"
                      AND o.DateOrdered < " + sqlToDate;

                orderAccessSql = MRole.GetDefault(ctx).AddAccessSQL(orderAccessSql, "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                string sql = @"
                    SELECT
                        base_o.C_Order_ID,
                        base_o.DocumentNo,
                        base_o.DateOrdered,
                        base_o.DocStatus,
                        bp.Name AS VendorName,
                        wh.Name AS WarehouseName,
                        usr.Name AS SalesRepName,
                        base_o.C_Currency_ID,
                        base_o.C_ConversionType_ID,
                        base_o.AD_Client_ID,
                        base_o.AD_Org_ID,
                        SUM(COALESCE(ol.LineNetAmt, 0)) AS LineNetTotal,
                        SUM(COALESCE(ol.QtyOrdered, 0)) AS TotalQtyOrdered,
                        SUM(COALESCE(ol.QtyDelivered, 0)) AS TotalQtyDelivered,
                        COUNT(ol.C_OrderLine_ID) AS LineCount
                    FROM (" + orderAccessSql + @") base_o
                    INNER JOIN C_OrderLine ol
                        ON ol.C_Order_ID = base_o.C_Order_ID
                       AND ol.IsActive = 'Y'
                    LEFT JOIN C_BPartner bp ON bp.C_BPartner_ID = base_o.C_BPartner_ID
                    LEFT JOIN M_Warehouse wh ON wh.M_Warehouse_ID = base_o.M_Warehouse_ID
                    LEFT JOIN AD_User usr ON usr.AD_User_ID = base_o.SalesRep_ID
                    GROUP BY
                        base_o.C_Order_ID,
                        base_o.DocumentNo,
                        base_o.DateOrdered,
                        base_o.DocStatus,
                        bp.Name,
                        wh.Name,
                        usr.Name,
                        base_o.C_Currency_ID,
                        base_o.C_ConversionType_ID,
                        base_o.AD_Client_ID,
                        base_o.AD_Org_ID
                    ORDER BY base_o.DateOrdered DESC, base_o.C_Order_ID DESC";

                var records = new List<object>();
                decimal monthValue = 0;
                var vendorSet = new HashSet<string>();

                using (IDataReader dr = DB.ExecuteReader(sql, null, null))
                {
                    while (dr != null && dr.Read())
                    {
                        int orderId = Util.GetValueOfInt(dr["C_Order_ID"]);
                        string docNo = Util.GetValueOfString(dr["DocumentNo"]);
                        DateTime? orderDate = Util.GetValueOfDateTime(dr["DateOrdered"]);
                        string rawDocStatus = Util.GetValueOfString(dr["DocStatus"]);
                        string vendor = Util.GetValueOfString(dr["VendorName"]);
                        string warehouse = Util.GetValueOfString(dr["WarehouseName"]);
                        string rep = Util.GetValueOfString(dr["SalesRepName"]);
                        int orderCurrencyId = Util.GetValueOfInt(dr["C_Currency_ID"]);
                        int conversionTypeId = Util.GetValueOfInt(dr["C_ConversionType_ID"]);
                        int rowClientId = Util.GetValueOfInt(dr["AD_Client_ID"]);
                        int rowOrgId = Util.GetValueOfInt(dr["AD_Org_ID"]);
                        decimal lineNetTotal = Util.GetValueOfDecimal(dr["LineNetTotal"]);
                        decimal qtyOrdered = Util.GetValueOfDecimal(dr["TotalQtyOrdered"]);
                        decimal qtyDelivered = Util.GetValueOfDecimal(dr["TotalQtyDelivered"]);
                        int lineCount = Util.GetValueOfInt(dr["LineCount"]);

                        // Convert money server-side
                        decimal convertedAmt = lineNetTotal;
                        if (orderCurrencyId != baseCur.CurrencyId && baseCur.CurrencyId > 0 && orderCurrencyId > 0 && orderDate.HasValue)
                        {
                            convertedAmt = MConversionRate.Convert(ctx, lineNetTotal, orderCurrencyId, baseCur.CurrencyId,
                                orderDate.Value, conversionTypeId, rowClientId, rowOrgId);
                        }

                        monthValue += convertedAmt;
                        if (!string.IsNullOrEmpty(vendor))
                        {
                            vendorSet.Add(vendor);
                        }

                        // Derive delivery status as per Authoritative Prompt rules
                        string deliveryStatus;
                        string deliveryChip;
                        if (rawDocStatus == "CL" || rawDocStatus == "VO")
                        {
                            deliveryStatus = "Not applicable";
                            deliveryChip = "chip-neutral";
                        }
                        else if (qtyDelivered >= qtyOrdered && qtyOrdered > 0)
                        {
                            deliveryStatus = "Fully delivered";
                            deliveryChip = "chip-ok";
                        }
                        else if (qtyDelivered > 0)
                        {
                            deliveryStatus = "Partial";
                            deliveryChip = "chip-warn";
                        }
                        else
                        {
                            deliveryStatus = "Pending";
                            deliveryChip = "chip-neutral";
                        }

                        // Map DocStatus
                        string statusLabel = "Drafted";
                        string statusChip = "chip-neutral";
                        if (rawDocStatus == "DR")
                        {
                            statusLabel = "Drafted";
                            statusChip = "chip-neutral";
                        }
                        else if (rawDocStatus == "IP")
                        {
                            statusLabel = "In process";
                            statusChip = "chip-prop";
                        }
                        else if (rawDocStatus == "CO")
                        {
                            statusLabel = "Completed";
                            statusChip = "chip-ok";
                        }
                        else if (rawDocStatus == "CL")
                        {
                            statusLabel = "Closed";
                            statusChip = "chip-ok";
                        }
                        else if (rawDocStatus == "VO")
                        {
                            statusLabel = "Voided";
                            statusChip = "chip-risk";
                        }

                        records.Add(new
                        {
                            orderId = orderId,
                            poNo = docNo,
                            orderDate = orderDate.HasValue ? orderDate.Value.ToString("yyyy-MM-dd") : "",
                            orderDateFormatted = orderDate.HasValue ? orderDate.Value.ToString("dd MMM yyyy") : "—",
                            vendor = string.IsNullOrEmpty(vendor) ? "—" : vendor,
                            warehouse = string.IsNullOrEmpty(warehouse) ? "—" : warehouse,
                            rep = string.IsNullOrEmpty(rep) ? "—" : rep,
                            value = convertedAmt,
                            qtyOrdered = qtyOrdered,
                            qtyDelivered = qtyDelivered,
                            qtyPending = Math.Max(0, qtyOrdered - qtyDelivered),
                            lineCount = lineCount,
                            deliveryStatus = deliveryStatus,
                            deliveryChip = deliveryChip,
                            docStatus = rawDocStatus,
                            statusLabel = statusLabel,
                            statusChip = statusChip
                        });
                    }
                }

                int poCount = records.Count;
                int vendorCount = vendorSet.Count;
                decimal avgPoVal = poCount > 0 ? (monthValue / poCount) : 0;

                return Json(JsonConvert.SerializeObject(new
                {
                    success = true,
                    records = records,
                    poCount = poCount,
                    poValue = monthValue,
                    vendorCount = vendorCount,
                    avgPoValue = avgPoVal,
                    currency = new
                    {
                        symbol = baseCur.Symbol,
                        iso = baseCur.IsoCode,
                        precision = baseCur.StdPrecision
                    }
                }), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_208_MonthlyPOTrendWidget.GetMonthPODrilldown", ex);
                return Json(JsonConvert.SerializeObject(new { error = Msg.GetMsg(ctx, "Error") ?? "Error", message = ex.Message }), JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Retrieves the line items for a specific purchase order.
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetPOLineDetails(int orderId)
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null)
            {
                return Json(JsonConvert.SerializeObject(new { error = "Session expired" }), JsonRequestBehavior.AllowGet);
            }

            if (orderId <= 0)
            {
                return Json(JsonConvert.SerializeObject(new { error = "Invalid Order ID" }), JsonRequestBehavior.AllowGet);
            }

            try
            {
                string sql = @"
                    SELECT
                        ol.C_OrderLine_ID,
                        ol.Line,
                        p.Name AS ProductName,
                        p.Value AS ProductCode,
                        asi.Description AS AttributeDesc,
                        COALESCE(uom.UOMSymbol, uom.Name) AS UOMSymbol,
                        ol.QtyOrdered,
                        ol.QtyDelivered,
                        ol.PriceActual,
                        ol.LineNetAmt,
                        o.DocStatus
                    FROM C_OrderLine ol
                    INNER JOIN C_Order o ON o.C_Order_ID = ol.C_Order_ID
                    LEFT JOIN M_Product p ON p.M_Product_ID = ol.M_Product_ID
                    LEFT JOIN M_AttributeSetInstance asi ON asi.M_AttributeSetInstance_ID = ol.M_AttributeSetInstance_ID
                    LEFT JOIN C_UOM uom ON uom.C_UOM_ID = ol.C_UOM_ID
                    WHERE ol.C_Order_ID = " + orderId + @"
                      AND ol.IsActive = 'Y'
                    ORDER BY ol.Line ASC, ol.C_OrderLine_ID ASC";

                var lines = new List<object>();

                using (IDataReader dr = DB.ExecuteReader(sql, null, null))
                {
                    int lineIdx = 1;
                    while (dr != null && dr.Read())
                    {
                        int lineId = Util.GetValueOfInt(dr["C_OrderLine_ID"]);
                        string prodName = Util.GetValueOfString(dr["ProductName"]);
                        string attrDesc = Util.GetValueOfString(dr["AttributeDesc"]);
                        string uom = Util.GetValueOfString(dr["UOMSymbol"]);
                        decimal qtyOrdered = Util.GetValueOfDecimal(dr["QtyOrdered"]);
                        decimal qtyDelivered = Util.GetValueOfDecimal(dr["QtyDelivered"]);
                        decimal rate = Util.GetValueOfDecimal(dr["PriceActual"]);
                        decimal amount = Util.GetValueOfDecimal(dr["LineNetAmt"]);
                        string parentDocStatus = Util.GetValueOfString(dr["DocStatus"]);

                        decimal qtyPending = Math.Max(0, qtyOrdered - qtyDelivered);

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
                        else if (qtyDelivered >= qtyOrdered && qtyOrdered > 0)
                        {
                            lineStatus = "Received";
                            lineChip = "chip-ok";
                        }
                        else if (qtyDelivered > 0)
                        {
                            lineStatus = "Partial received";
                            lineChip = "chip-warn";
                        }
                        else
                        {
                            lineStatus = "Pending";
                            lineChip = "chip-neutral";
                        }

                        lines.Add(new
                        {
                            lineNo = lineIdx++,
                            lineId = lineId,
                            product = string.IsNullOrEmpty(prodName) ? "—" : prodName,
                            attribute = string.IsNullOrEmpty(attrDesc) ? "" : attrDesc,
                            uom = string.IsNullOrEmpty(uom) ? "—" : uom,
                            qtyOrdered = qtyOrdered,
                            qtyDelivered = qtyDelivered,
                            qtyPending = qtyPending,
                            rate = rate,
                            amount = amount,
                            status = lineStatus,
                            statusChip = lineChip
                        });
                    }
                }

                return Json(JsonConvert.SerializeObject(new { success = true, lines = lines }), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_208_MonthlyPOTrendWidget.GetPOLineDetails", ex);
                return Json(JsonConvert.SerializeObject(new { error = Msg.GetMsg(ctx, "Error") ?? "Error", message = ex.Message }), JsonRequestBehavior.AllowGet);
            }
        }

        private static string ToSqlDate(DateTime date)
        {
            if (DB.IsOracle())
            {
                return "TO_DATE('" + date.ToString("yyyy-MM-dd") + "', 'YYYY-MM-DD')";
            }
            return "CAST('" + date.ToString("yyyy-MM-dd") + "' AS DATE)";
        }
    }
}
