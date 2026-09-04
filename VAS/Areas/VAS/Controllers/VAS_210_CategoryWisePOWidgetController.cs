/************************************************************
 * Module Name    : VAS
 * Purpose        : Widget 08 - Category Wise PO Widget Controller
 *                  Supplies monthly converted Purchase Order line values grouped by
 *                  M_Product_Category (top 3 + Other), category PO drill-down list,
 *                  and PO line items drill-down.
 * Created Date   : 17 Aug 2026
 * Created by     : Builder Agent 8 (AI-Dev)
 ***********************************************************/
using CoreLibrary.DataBase;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Web.Mvc;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;

namespace VAS.Areas.VAS.Controllers
{
    public class VAS_210_CategoryWisePOWidgetController : Controller
    {
        private static readonly VLogger _log = VLogger.GetVLogger(typeof(VAS_210_CategoryWisePOWidgetController).FullName);

        [Authorize]
        public ActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// Endpoint 1: Retrieves category-wise aggregated PO values for the specified month/year.
        /// Performs server-side currency conversion into the tenant's accounting schema currency.
        /// Top 3 categories by spend are returned individually, with remaining categories folded into "Other".
        /// </summary>
        /// <param name="month">1-based month index (1..12)</param>
        /// <param name="year">4-digit calendar year (e.g. 2026)</param>
        /// <returns>JSON serialized CategoryWisePOResult</returns>
        [HttpGet]
        public JsonResult GetCategoryWisePO(int month, int year)
        {
            if (Session["ctx"] == null)
            {
                return Json(JsonConvert.SerializeObject(new { error = "Session expired" }), JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null)
            {
                return Json(JsonConvert.SerializeObject(new { error = "Context not found" }), JsonRequestBehavior.AllowGet);
            }

            int selMonth = month > 0 && month <= 12 ? month : DateTime.Now.Month;
            int selYear = year > 1900 ? year : DateTime.Now.Year;

            DateTime monthStart = new DateTime(selYear, selMonth, 1);
            DateTime monthEndExclusive = monthStart.AddMonths(1);

            int clientId = ctx.GetAD_Client_ID();

            try
            {
                // 1. Resolve tenant base accounting currency
                CurrencyInfo currency = GetAccountingCurrency(ctx, clientId);

                // 2. Query category PO facts
                string sql = @"
                    SELECT
                        o.C_Order_ID,
                        o.C_BPartner_ID,
                        o.DateOrdered,
                        o.C_Currency_ID,
                        o.C_ConversionType_ID,
                        o.AD_Client_ID,
                        o.AD_Org_ID,
                        COALESCE(pc.M_Product_Category_ID, 0) AS M_Product_Category_ID,
                        COALESCE(pc.Name, N'Uncategorised') AS CategoryName,
                        SUM(COALESCE(ol.LineNetAmt, 0)) AS CategoryLineNetAmt
                    FROM C_Order o
                    INNER JOIN C_OrderLine ol ON (ol.C_Order_ID = o.C_Order_ID AND ol.IsActive = 'Y')
                    LEFT JOIN M_Product p ON (p.M_Product_ID = ol.M_Product_ID)
                    LEFT JOIN M_Product_Category pc ON (pc.M_Product_Category_ID = p.M_Product_Category_ID)
                    WHERE o.AD_Client_ID = " + clientId + @"
                      AND o.IsActive = 'Y'
                      AND o.IsSOTrx = 'N'
                      AND COALESCE(o.IsReturnTrx, 'N') = 'N'
                      AND o.DocStatus <> 'VO'
                      AND o.DateOrdered >= " + ToSqlDate(monthStart) + @"
                      AND o.DateOrdered < " + ToSqlDate(monthEndExclusive) + @"
                    GROUP BY
                        o.C_Order_ID,
                        o.C_BPartner_ID,
                        o.DateOrdered,
                        o.C_Currency_ID,
                        o.C_ConversionType_ID,
                        o.AD_Client_ID,
                        o.AD_Org_ID,
                        pc.M_Product_Category_ID,
                        pc.Name
                    ORDER BY pc.Name, o.C_Order_ID";

                // Group in memory with currency conversion
                Dictionary<int, CategoryBucket> catDict = new Dictionary<int, CategoryBucket>();

                using (IDataReader dr = DB.ExecuteReader(sql, null, null))
                {
                    while (dr != null && dr.Read())
                    {
                        int catId = Util.GetValueOfInt(dr["M_Product_Category_ID"]);
                        string catName = Util.GetValueOfString(dr["CategoryName"]);
                        int orderId = Util.GetValueOfInt(dr["C_Order_ID"]);
                        int vendorId = Util.GetValueOfInt(dr["C_BPartner_ID"]);
                        DateTime orderDate = Convert.ToDateTime(dr["DateOrdered"]);
                        int curFromId = Util.GetValueOfInt(dr["C_Currency_ID"]);
                        int convTypeId = Util.GetValueOfInt(dr["C_ConversionType_ID"]);
                        int orgId = Util.GetValueOfInt(dr["AD_Org_ID"]);
                        decimal lineNetAmt = Util.GetValueOfDecimal(dr["CategoryLineNetAmt"]);

                        // Convert amount to accounting currency
                        decimal convertedAmt = ConvertCurrency(ctx, lineNetAmt, curFromId, currency.CurrencyId, orderDate, convTypeId, clientId, orgId);

                        if (!catDict.TryGetValue(catId, out CategoryBucket bucket))
                        {
                            bucket = new CategoryBucket
                            {
                                CategoryId = catId,
                                CategoryName = catName,
                                TotalConvertedValue = 0,
                                OrderIds = new HashSet<int>(),
                                VendorIds = new HashSet<int>()
                            };
                            catDict[catId] = bucket;
                        }

                        bucket.TotalConvertedValue += convertedAmt;
                        bucket.OrderIds.Add(orderId);
                        bucket.VendorIds.Add(vendorId);
                    }
                }

                // Sort categories descending by spend
                List<CategoryBucket> sortedCats = new List<CategoryBucket>(catDict.Values);
                sortedCats.Sort((a, b) => b.TotalConvertedValue.CompareTo(a.TotalConvertedValue));

                decimal monthTotal = 0;
                foreach (var b in sortedCats)
                {
                    monthTotal += b.TotalConvertedValue;
                }

                // Top 3 + Other rule
                List<CategoryDisplayItem> displayList = new List<CategoryDisplayItem>();
                string[] pastelColors = new string[] { "#A9D2FF", "#A3E0D4", "#FFDCA1", "#CFC9F5" };
                string otherColor = "#D7E3EE";

                if (sortedCats.Count <= 4)
                {
                    for (int i = 0; i < sortedCats.Count; i++)
                    {
                        var b = sortedCats[i];
                        decimal share = monthTotal > 0 ? Math.Round((b.TotalConvertedValue / monthTotal) * 100m, 1) : 0;
                        displayList.Add(new CategoryDisplayItem
                        {
                            CategoryId = b.CategoryId,
                            CategoryIds = new List<int> { b.CategoryId },
                            CategoryName = b.CategoryName,
                            CategoryValue = b.TotalConvertedValue,
                            Share = share,
                            PoCount = b.OrderIds.Count,
                            VendorCount = b.VendorIds.Count,
                            Color = i < pastelColors.Length ? pastelColors[i] : otherColor,
                            IsOther = false
                        });
                    }
                }
                else
                {
                    // Top 3
                    for (int i = 0; i < 3; i++)
                    {
                        var b = sortedCats[i];
                        decimal share = monthTotal > 0 ? Math.Round((b.TotalConvertedValue / monthTotal) * 100m, 1) : 0;
                        displayList.Add(new CategoryDisplayItem
                        {
                            CategoryId = b.CategoryId,
                            CategoryIds = new List<int> { b.CategoryId },
                            CategoryName = b.CategoryName,
                            CategoryValue = b.TotalConvertedValue,
                            Share = share,
                            PoCount = b.OrderIds.Count,
                            VendorCount = b.VendorIds.Count,
                            Color = pastelColors[i],
                            IsOther = false
                        });
                    }

                    // Remainder folded into "Other"
                    decimal otherValue = 0;
                    HashSet<int> otherOrderIds = new HashSet<int>();
                    HashSet<int> otherVendorIds = new HashSet<int>();
                    List<int> otherCatIds = new List<int>();

                    for (int i = 3; i < sortedCats.Count; i++)
                    {
                        var b = sortedCats[i];
                        otherValue += b.TotalConvertedValue;
                        otherCatIds.Add(b.CategoryId);
                        foreach (var o in b.OrderIds) { otherOrderIds.Add(o); }
                        foreach (var v in b.VendorIds) { otherVendorIds.Add(v); }
                    }

                    decimal otherShare = monthTotal > 0 ? Math.Round((otherValue / monthTotal) * 100m, 1) : 0;
                    displayList.Add(new CategoryDisplayItem
                    {
                        CategoryId = 0,
                        CategoryIds = otherCatIds,
                        CategoryName = Msg.GetMsg(ctx, "VAS_Other") ?? "Other",
                        CategoryValue = otherValue,
                        Share = otherShare,
                        PoCount = otherOrderIds.Count,
                        VendorCount = otherVendorIds.Count,
                        Color = otherColor,
                        IsOther = true
                    });
                }

                var result = new
                {
                    categories = displayList,
                    totalValue = monthTotal,
                    curSymbol = currency.CurSymbol,
                    curIso = currency.ISO_Code,
                    stdPrecision = currency.StdPrecision,
                    success = true
                };

                return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                _log.Severe("GetCategoryWisePO: " + ex.Message);
                return Json(JsonConvert.SerializeObject(new { error = ex.Message, success = false }), JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Endpoint 2: Retrieves the Purchase Orders drill-down list for the selected category
        /// (or multiple categories if "Other" was clicked).
        /// </summary>
        [HttpGet]
        public JsonResult GetCategoryPODrillDown(int month, int year, string categoryIds)
        {
            if (Session["ctx"] == null)
            {
                return Json(JsonConvert.SerializeObject(new { error = "Session expired" }), JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null)
            {
                return Json(JsonConvert.SerializeObject(new { error = "Context not found" }), JsonRequestBehavior.AllowGet);
            }

            int selMonth = month > 0 && month <= 12 ? month : DateTime.Now.Month;
            int selYear = year > 1900 ? year : DateTime.Now.Year;

            DateTime monthStart = new DateTime(selYear, selMonth, 1);
            DateTime monthEndExclusive = monthStart.AddMonths(1);

            int clientId = ctx.GetAD_Client_ID();

            try
            {
                CurrencyInfo currency = GetAccountingCurrency(ctx, clientId);

                // Build category filter condition
                string catCondition = "";
                if (!string.IsNullOrEmpty(categoryIds))
                {
                    string[] split = categoryIds.Split(new char[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    List<int> validIds = new List<int>();
                    bool includesUncategorised = false;
                    foreach (var s in split)
                    {
                        if (int.TryParse(s, out int cid))
                        {
                            if (cid == 0) { includesUncategorised = true; }
                            else { validIds.Add(cid); }
                        }
                    }

                    List<string> condParts = new List<string>();
                    if (validIds.Count > 0)
                    {
                        condParts.Add("pc.M_Product_Category_ID IN (" + string.Join(",", validIds) + ")");
                    }
                    if (includesUncategorised)
                    {
                        condParts.Add("(pc.M_Product_Category_ID IS NULL OR pc.M_Product_Category_ID = 0)");
                    }

                    if (condParts.Count > 0)
                    {
                        catCondition = " AND (" + string.Join(" OR ", condParts) + ")";
                    }
                }

                string sql = @"
                    SELECT
                        o.C_Order_ID,
                        o.DocumentNo,
                        o.DateOrdered,
                        b                        -- A charge line, or a product that is not of Item type, carries no
                        -- stock movement: the widget shows its name, UOM, ordered, rate and
                        -- amount, and dashes for received / pending / line status.
CASE WHEN COALESCE(ol.C_Charge_ID, 0) > 0
     THEN COALESCE(ch.Name, N'')
     ELSE p.Name END AS VendorName,
CASE WHEN COALESCE(ol.C_Charge_ID, 0) > 0 THEN 'Y'
     WHEN ol.M_Product_ID IS NOT NULL AND COALESCE(p.ProductType, 'I') <> 'I' THEN 'Y'
     ELSE 'N' END AS IsNonStock,
                        wh.Name AS WarehouseName,
                        sr.Name AS RepName,
                        o.DocStatus,
                        o.C_Currency_ID,
                        o.C_ConversionType_ID,
                        o.AD_Client_ID,
                        o.AD_Org_ID,
                        SUM(COALESCE(ol.LineNetAmt, 0)) AS CategoryLineNetAmt,
                        SUM(COALESCE(ol.QtyOrdered, 0)) AS TotalQtyOrdered,
                        -- QtyEntered is expressed in the line's own C_UOM_ID (the UOM the buyer
                        -- picked); QtyOrdered / QtyDelivered are in the product's base UOM. The
                        -- widget shows the selected UOM, so quantities are scaled to it.
COALESCE(ol.QtyEntered, ol.QtyOrdered, 0) AS QtyEntered,
                        SUM(COALESCE(ol.QtyDelivered, 0)) AS TotalQtyDelivered
                    FROM C_Order o
                    INNER JOIN C_OrderLine ol ON (ol.C_Order_ID = o.C_Order_ID AND ol.IsActive = 'Y')
                    LEFT JOIN M_Product p ON (p.M_Product_ID = ol.M_Product_ID)
                    LEFT JOIN C_Charge ch ON (ch.C_Charge_ID = ol.C_Charge_ID)
                    LEFT JOIN M_Product_Category pc ON (pc.M_Product_Category_ID = p.M_Product_Category_ID)
                    LEFT JOIN C_BPartner bp ON (bp.C_BPartner_ID = o.C_BPartner_ID)
                    LEFT JOIN M_Warehouse wh ON (wh.M_Warehouse_ID = o.M_Warehouse_ID)
                    LEFT JOIN AD_User sr ON (sr.AD_User_ID = o.SalesRep_ID)
                    WHERE o.AD_Client_ID = " + clientId + @"
                      AND o.IsActive = 'Y'
                      AND o.IsSOTrx = 'N'
                      AND COALESCE(o.IsReturnTrx, 'N') = 'N'
                      AND o.DocStatus <> 'VO'
                      AND o.DateOrdered >= " + ToSqlDate(monthStart) + @"
                      AND o.DateOrdered < " + ToSqlDate(monthEndExclusive) + @"
                      " + catCondition + @"
                    GROUP BY
                        o.C_Order_ID,
                        o.DocumentNo,
                        o.DateOrdered,
                        bp.Name,
                        wh.Name,
                        sr.Name,
                        o.DocStatus,
                        o.C_Currency_ID,
                        o.C_ConversionType_ID,
                        o.AD_Client_ID,
                        o.AD_Org_ID
                    ORDER BY o.DateOrdered DESC, o.DocumentNo DESC";

                List<PurchaseOrderRecord> records = new List<PurchaseOrderRecord>();

                using (IDataReader dr = DB.ExecuteReader(sql, null, null))
                {
                    while (dr != null && dr.Read())
                    {
                        int orderId = Util.GetValueOfInt(dr["C_Order_ID"]);
                        string docNo = Util.GetValueOfString(dr["DocumentNo"]);
                        DateTime orderDate = Convert.ToDateTime(dr["DateOrdered"]);
                        string vendor = Util.GetValueOfString(dr["VendorName"]);
                        string wh = Util.GetValueOfString(dr["WarehouseName"]);
                        string rep = Util.GetValueOfString(dr["RepName"]);
                        string docStatus = Util.GetValueOfString(dr["DocStatus"]);
                        int curFromId = Util.GetValueOfInt(dr["C_Currency_ID"]);
                        int convTypeId = Util.GetValueOfInt(dr["C_ConversionType_ID"]);
                        int orgId = Util.GetValueOfInt(dr["AD_Org_ID"]);
                        decimal lineNetAmt = Util.GetValueOfDecimal(dr["CategoryLineNetAmt"]);
                        decimal qtyOrdered = Util.GetValueOfDecimal(dr["TotalQtyOrdered"]);
                        decimal qtyDelivered = Util.GetValueOfDecimal(dr["TotalQtyDelivered"]);

                        decimal convertedValue = ConvertCurrency(ctx, lineNetAmt, curFromId, currency.CurrencyId, orderDate, convTypeId, clientId, orgId);

                        // Delivery status rule (independent from DocStatus):
                        // - Closed/Voided display delivery status as Not applicable
                        // - Fully delivered: total delivered >= ordered and ordered > 0
                        // - Partial: delivered > 0 and delivered < ordered
                        // - Pending: delivered <= 0 and ordered > 0
                        string deliveryStatus = "Pending";
                        string deliveryChip = "chip-neutral";

                        if (docStatus == "CL" || docStatus == "VO")
                        {
                            deliveryStatus = "Not applicable";
                            deliveryChip = "chip-neutral";
                        }
                        else if (qtyDelivered >= qtyOrdered && qtyOrdered > 0)
                        {
                            deliveryStatus = "Fully delivered";
                            deliveryChip = "chip-ok";
                        }
                        else if (qtyDelivered > 0 && qtyDelivered < qtyOrdered)
                        {
                            deliveryStatus = "Partial";
                            deliveryChip = "chip-warn";
                        }
                        else
                        {
                            deliveryStatus = "Pending";
                            deliveryChip = "chip-neutral";
                        }

                        // Document Status label & chip:
                        string statusLabel = docStatus;
                        string statusChip = "chip-neutral";

                        switch (docStatus)
                        {
                            case "DR":
                                statusLabel = "Drafted";
                                statusChip = "chip-neutral";
                                break;
                            case "IP":
                                statusLabel = "In Progress";
                                statusChip = "chip-prop";
                                break;
                            case "CO":
                                statusLabel = "Completed";
                                statusChip = "chip-ok";
                                break;
                            case "CL":
                                statusLabel = "Closed";
                                statusChip = "chip-ok";
                                break;
                            case "VO":
                                statusLabel = "Voided";
                                statusChip = "chip-risk";
                                break;
                            default:
                                statusLabel = docStatus;
                                statusChip = "chip-neutral";
                                break;
                        }

                        records.Add(new PurchaseOrderRecord
                        {
                            OrderId = orderId,
                            DocumentNo = docNo,
                            DateOrdered = orderDate.ToString("yyyy-MM-dd"),
                            DateOrderedFull = orderDate.ToString("dd MMM yyyy"),
                            DateOrderedShort = orderDate.ToString("dd MMM"),
                            Vendor = !string.IsNullOrEmpty(vendor) ? vendor : "—",
                            Warehouse = !string.IsNullOrEmpty(wh) ? wh : "—",
                            Representative = !string.IsNullOrEmpty(rep) ? rep : "—",
                            ValueNum = convertedValue,
                            QtyOrdered = qtyOrdered,
                            QtyDelivered = qtyDelivered,
                            DeliveryStatus = deliveryStatus,
                            DeliveryChip = deliveryChip,
                            DocStatus = docStatus,
                            StatusLabel = statusLabel,
                            StatusChip = statusChip
                        });
                    }
                }

                var result = new
                {
                    records = records,
                    totalRecords = records.Count,
                    curSymbol = currency.CurSymbol,
                    curIso = currency.ISO_Code,
                    stdPrecision = currency.StdPrecision,
                    success = true
                };

                return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                _log.Severe("GetCategoryPODrillDown: " + ex.Message);
                return Json(JsonConvert.SerializeObject(new { error = ex.Message, success = false }), JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Endpoint 3: Retrieves individual lines for a specific Purchase Order document.
        /// </summary>
        [HttpGet]
        public JsonResult GetPOLines(int orderId)
        {
            if (Session["ctx"] == null)
            {
                return Json(JsonConvert.SerializeObject(new { error = "Session expired" }), JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null)
            {
                return Json(JsonConvert.SerializeObject(new { error = "Context not found" }), JsonRequestBehavior.AllowGet);
            }

            int clientId = ctx.GetAD_Client_ID();

            try
            {
                CurrencyInfo currency = GetAccountingCurrency(ctx, clientId);

                // Header query
                string headerSql = @"
                    SELECT
                        o.C_Order_ID,
                        o.DocumentNo,
                        o.DateOrdered,
                        bp.Name AS VendorName,
                        wh.Name AS WarehouseName,
                        u.Name AS CreatedByName,
                        o.Created,
                        o.DocStatus,
                        o.C_Currency_ID,
                        o.C_ConversionType_ID,
                        o.AD_Org_ID,
                        SUM(COALESCE(ol.LineNetAmt, 0)) AS TotalOrderAmt,
                        SUM(COALESCE(ol.QtyOrdered, 0)) AS TotalQtyOrdered,
                        SUM(COALESCE(ol.QtyDelivered, 0)) AS TotalQtyDelivered
                    FROM C_Order o
                    INNER JOIN C_OrderLine ol ON (ol.C_Order_ID = o.C_Order_ID AND ol.IsActive = 'Y')
                    LEFT JOIN C_BPartner bp ON (bp.C_BPartner_ID = o.C_BPartner_ID)
                    LEFT JOIN M_Warehouse wh ON (wh.M_Warehouse_ID = o.M_Warehouse_ID)
                    LEFT JOIN AD_User u ON (u.AD_User_ID = o.CreatedBy)
                    WHERE o.C_Order_ID = " + orderId + @"
                      AND o.AD_Client_ID = " + clientId + @"
                      AND o.IsActive = 'Y'
                    GROUP BY
                        o.C_Order_ID,
                        o.DocumentNo,
                        o.DateOrdered,
                        bp.Name,
                        wh.Name,
                        u.Name,
                        o.Created,
                        o.DocStatus,
                        o.C_Currency_ID,
                        o.C_ConversionType_ID,
                        o.AD_Org_ID";

                POHeaderDetails header = null;

                using (IDataReader dr = DB.ExecuteReader(headerSql, null, null))
                {
                    if (dr != null && dr.Read())
                    {
                        DateTime orderDate = Convert.ToDateTime(dr["DateOrdered"]);
                        DateTime createdDate = Convert.ToDateTime(dr["Created"]);
                        int curFromId = Util.GetValueOfInt(dr["C_Currency_ID"]);
                        int convTypeId = Util.GetValueOfInt(dr["C_ConversionType_ID"]);
                        int orgId = Util.GetValueOfInt(dr["AD_Org_ID"]);
                        decimal totalAmt = Util.GetValueOfDecimal(dr["TotalOrderAmt"]);
                        decimal qtyOrdered = Util.GetValueOfDecimal(dr["TotalQtyOrdered"]);
                        decimal qtyDelivered = Util.GetValueOfDecimal(dr["TotalQtyDelivered"]);
                        string docStatus = Util.GetValueOfString(dr["DocStatus"]);

                        decimal convVal = ConvertCurrency(ctx, totalAmt, curFromId, currency.CurrencyId, orderDate, convTypeId, clientId, orgId);

                        string delivStatus = "Pending";
                        if (docStatus == "CL" || docStatus == "VO") { delivStatus = "Not applicable"; }
                        else if (qtyDelivered >= qtyOrdered && qtyOrdered > 0) { delivStatus = "Fully delivered"; }
                        else if (qtyDelivered > 0 && qtyDelivered < qtyOrdered) { delivStatus = "Partial"; }

                        string docStatusLabel = docStatus == "CO" ? "Completed" : (docStatus == "DR" ? "Drafted" : (docStatus == "CL" ? "Closed" : (docStatus == "IP" ? "In Progress" : docStatus)));

                        header = new POHeaderDetails
                        {
                            OrderId = orderId,
                            DocumentNo = Util.GetValueOfString(dr["DocumentNo"]),
                            DateOrdered = orderDate.ToString("dd MMM yyyy"),
                            Vendor = Util.GetValueOfString(dr["VendorName"]),
                            Warehouse = Util.GetValueOfString(dr["WarehouseName"]),
                            CreatedBy = Util.GetValueOfString(dr["CreatedByName"]),
                            CreatedOn = createdDate.ToString("dd MMM yyyy"),
                            DocStatus = docStatus,
                            DocStatusLabel = docStatusLabel,
                            DeliveryStatus = delivStatus,
                            TotalValue = convVal,
                            TotalQtyOrdered = qtyOrdered,
                            TotalQtyDelivered = qtyDelivered,
                            TotalQtyPending = Math.Max(0, qtyOrdered - qtyDelivered)
                        };
                    }
                }

                // Lines query
                string linesSql = @"
                    SELECT
                        ol.C_OrderLine_ID,
                        ol.Line,
                        p.Name AS ProductName,
                        CASE WHEN COALESCE(ol.M_AttributeSetInstance_ID, 0) > 0
                             THEN COALESCE(asi.Description, N'')
                             ELSE N'' END AS Attribute,
                        COALESCE(uom.UOMSymbol, uom.Name) AS UomName,
                        COALESCE(ol.QtyOrdered, 0) AS QtyOrdered,
                        COALESCE(ol.QtyDelivered, 0) AS QtyDelivered,
                        COALESCE(ol.PriceActual, 0) AS PriceActual,
                        COALESCE(ol.LineNetAmt, 0) AS LineNetAmt,
                        o.DocStatus
                    FROM C_OrderLine ol
                    INNER JOIN C_Order o ON (o.C_Order_ID = ol.C_Order_ID)
                    LEFT JOIN M_Product p ON (p.M_Product_ID = ol.M_Product_ID)
                    LEFT JOIN M_AttributeSetInstance asi ON (asi.M_AttributeSetInstance_ID = ol.M_AttributeSetInstance_ID)
                    LEFT JOIN C_UOM uom ON (uom.C_UOM_ID = ol.C_UOM_ID)
                    WHERE ol.C_Order_ID = " + orderId + @"
                      AND ol.IsActive = 'Y'
                    ORDER BY ol.Line, ol.C_OrderLine_ID";

                List<POLineRecord> lines = new List<POLineRecord>();

                using (IDataReader dr = DB.ExecuteReader(linesSql, null, null))
                {
                    int lineIdx = 1;
                    while (dr != null && dr.Read())
                    {
                        decimal qtyOrdered = Util.GetValueOfDecimal(dr["QtyOrdered"]);
                        decimal qtyDelivered = Util.GetValueOfDecimal(dr["QtyDelivered"]);
                        decimal qtyPending = Math.Max(0, qtyOrdered - qtyDelivered);
                        decimal rate = Util.GetValueOfDecimal(dr["PriceActual"]);
                        decimal amount = Util.GetValueOfDecimal(dr["LineNetAmt"]);
                        string pStatus = Util.GetValueOfString(dr["DocStatus"]);

                        // Line status rule:
                        // - Parent DR -> Drafted
                        // - Parent VO -> Voided
                        // - QtyDelivered >= QtyOrdered and QtyOrdered > 0 -> Received
                        // - 0 < QtyDelivered < QtyOrdered -> Partial received
                        // - Otherwise -> Pending
                        string lineStatus = "Pending";
                        string lineStatusChip = "chip-neutral";

                        if (pStatus == "DR")
                        {
                            lineStatus = "Drafted";
                            lineStatusChip = "chip-neutral";
                        }
                        else if (pStatus == "VO")
                        {
                            lineStatus = "Voided";
                            lineStatusChip = "chip-risk";
                        }
                        else if (qtyDelivered >= qtyOrdered && qtyOrdered > 0)
                        {
                            lineStatus = "Received";
                            lineStatusChip = "chip-ok";
                        }
                        else if (qtyDelivered > 0 && qtyDelivered < qtyOrdered)
                        {
                            lineStatus = "Partial received";
                            lineStatusChip = "chip-warn";
                        }
                        else
                        {
                            lineStatus = "Pending";
                            lineStatusChip = "chip-neutral";
                        }

                        lines.Add(new POLineRecord
                        {
                            LineNo = lineIdx++,
                            ProductName = Util.GetValueOfString(dr["ProductName"]),
                            Attribute = Util.GetValueOfString(dr["Attribute"]),
                            Uom = Util.GetValueOfString(dr["UomName"]),
                            QtyOrdered = qtyOrdered,
                            QtyDelivered = qtyDelivered,
                            QtyPending = qtyPending,
                            Rate = rate,
                            Amount = amount,
                            // Charge / non-Item lines are never received - the client renders dashes
                            // for received, pending and line status.
                            IsNonStock = Util.GetValueOfString(dr["IsNonStock"]) == "Y",
                            LineStatus = lineStatus,
                            LineStatusChip = lineStatusChip
                        });
                    }
                }

                var result = new
                {
                    header = header,
                    lines = lines,
                    totalLines = lines.Count,
                    curSymbol = currency.CurSymbol,
                    curIso = currency.ISO_Code,
                    stdPrecision = currency.StdPrecision,
                    success = true
                };

                return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                _log.Severe("GetPOLines: " + ex.Message);
                return Json(JsonConvert.SerializeObject(new { error = ex.Message, success = false }), JsonRequestBehavior.AllowGet);
            }
        }

        #region Helper Methods

        private CurrencyInfo GetAccountingCurrency(Ctx ctx, int clientId)
        {
            var info = new CurrencyInfo
            {
                CurrencyId = 0,
                CurSymbol = "₹",
                StdPrecision = 2,
                ISO_Code = "INR"
            };

            string sql = @"
                SELECT cs.C_Currency_ID, c.CurSymbol, c.StdPrecision, c.ISO_Code
                FROM C_AcctSchema cs
                INNER JOIN AD_ClientInfo ci ON (ci.C_AcctSchema1_ID = cs.C_AcctSchema_ID)
                INNER JOIN C_Currency c ON (cs.C_Currency_ID = c.C_Currency_ID)
                WHERE ci.AD_Client_ID = " + clientId + @"
                  AND ci.IsActive = 'Y'
                  AND cs.IsActive = 'Y'
                  AND c.IsActive = 'Y'";

            using (IDataReader dr = DB.ExecuteReader(sql, null, null))
            {
                if (dr != null && dr.Read())
                {
                    info.CurrencyId = Util.GetValueOfInt(dr["C_Currency_ID"]);
                    info.CurSymbol = Util.GetValueOfString(dr["CurSymbol"]);
                    info.StdPrecision = Util.GetValueOfInt(dr["StdPrecision"]);
                    info.ISO_Code = Util.GetValueOfString(dr["ISO_Code"]);
                }
            }

            return info;
        }

        private decimal ConvertCurrency(Ctx ctx, decimal amount, int curFromId, int curToId, DateTime convDate, int convTypeId, int clientId, int orgId)
        {
            if (amount == 0 || curFromId == curToId || curToId <= 0 || curFromId <= 0)
            {
                return amount;
            }

            try
            {
                decimal converted = MConversionRate.Convert(ctx, amount, curFromId, curToId, convDate, convTypeId, clientId, orgId);
                if (converted != 0)
                {
                    return converted;
                }

                // Fallback using base conversion
                converted = MConversionRate.ConvertBase(ctx, amount, curFromId, convDate, convTypeId, clientId, orgId);
                if (converted != 0)
                {
                    return converted;
                }
            }
            catch (Exception ex)
            {
                _log.Warning("ConvertCurrency fallback triggered: " + ex.Message);
            }

            return amount;
        }

        private static string ToSqlDate(DateTime date)
        {
            if (DB.IsOracle())
            {
                return "TO_DATE('" + date.ToString("yyyy-MM-dd") + "', 'YYYY-MM-DD')";
            }
            return "CAST('" + date.ToString("yyyy-MM-dd") + "' AS DATE)";
        }

        #endregion

        #region Internal DTO Classes

        private class CurrencyInfo
        {
            public int CurrencyId { get; set; }
            public string CurSymbol { get; set; }
            public int StdPrecision { get; set; }
            public string ISO_Code { get; set; }
        }

        private class CategoryBucket
        {
            public int CategoryId { get; set; }
            public string CategoryName { get; set; }
            public decimal TotalConvertedValue { get; set; }
            public HashSet<int> OrderIds { get; set; }
            public HashSet<int> VendorIds { get; set; }
        }

        public class CategoryDisplayItem
        {
            public int CategoryId { get; set; }
            public List<int> CategoryIds { get; set; }
            public string CategoryName { get; set; }
            public decimal CategoryValue { get; set; }
            public decimal Share { get; set; }
            public int PoCount { get; set; }
            public int VendorCount { get; set; }
            public string Color { get; set; }
            public bool IsOther { get; set; }
        }

        public class PurchaseOrderRecord
        {
            public int OrderId { get; set; }
            public string DocumentNo { get; set; }
            public string DateOrdered { get; set; }
            public string DateOrderedFull { get; set; }
            public string DateOrderedShort { get; set; }
            public string Vendor { get; set; }
            public string Warehouse { get; set; }
            public string Representative { get; set; }
            public decimal ValueNum { get; set; }
            public decimal QtyOrdered { get; set; }
            public decimal QtyDelivered { get; set; }
            public string DeliveryStatus { get; set; }
            public string DeliveryChip { get; set; }
            public string DocStatus { get; set; }
            public string StatusLabel { get; set; }
            public string StatusChip { get; set; }
        }

        public class POHeaderDetails
        {
            public int OrderId { get; set; }
            public string DocumentNo { get; set; }
            public string DateOrdered { get; set; }
            public string Vendor { get; set; }
            public string Warehouse { get; set; }
            public string CreatedBy { get; set; }
            public string CreatedOn { get; set; }
            public string DocStatus { get; set; }
            public string DocStatusLabel { get; set; }
            public string DeliveryStatus { get; set; }
            public decimal TotalValue { get; set; }
            public decimal TotalQtyOrdered { get; set; }
            public decimal TotalQtyDelivered { get; set; }
            public decimal TotalQtyPending { get; set; }
        }

        public class POLineRecord
        {
            public bool IsNonStock { get; set; }
            public int LineNo { get; set; }
            public string ProductName { get; set; }
            public string Attribute { get; set; }
            public string Uom { get; set; }
            public decimal QtyOrdered { get; set; }
            public decimal QtyDelivered { get; set; }
            public decimal QtyPending { get; set; }
            public decimal Rate { get; set; }
            public decimal Amount { get; set; }
            public string LineStatus { get; set; }
            public string LineStatusChip { get; set; }
        }

        #endregion
    }
}
