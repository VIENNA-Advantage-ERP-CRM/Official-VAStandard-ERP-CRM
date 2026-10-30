using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Web.Mvc;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VAS.Controllers
{
    /// <summary>
    /// Module Name : VAS_203_POGlobalSearchWidget (Purchase Order Dashboard - Widget 01)
    /// Purpose     : Controller for the 9x1 Global Search widget.
    ///               Performs full-width type-ahead search across purchase orders, vendors,
    ///               products, warehouses, representatives, requisitions, order references,
    ///               descriptions, and locations.
    /// 
    /// TABLE & FIELD MAPPING:
    /// - Purchase Order Header: C_Order o (C_Order_ID, DocumentNo, DateOrdered, DocStatus, GrandTotal,
    ///                           C_BPartner_ID, M_Warehouse_ID, SalesRep_ID, C_BPartner_Location_ID,
    ///                           POReference, Description, C_Currency_ID, IsSOTrx='N', IsReturnTrx='N', IsActive='Y')
    /// - Vendor: C_BPartner bp (C_BPartner_ID, Name)
    /// - Warehouse: M_Warehouse w (M_Warehouse_ID, Name)
    /// - Representative: AD_User rep (AD_User_ID, Name)
    /// - Location: C_BPartner_Location bpl (C_BPartner_Location_ID, Name)
    /// - Purchase Order Line: C_OrderLine ol (C_OrderLine_ID, C_Order_ID, M_Product_ID, M_RequisitionLine_ID, IsActive='Y')
    /// - Product: M_Product p (M_Product_ID, Name, Value, SKU)
    /// - Requisition Line: M_RequisitionLine rl (M_RequisitionLine_ID, M_Requisition_ID, IsActive='Y')
    /// - Requisition Header: M_Requisition r (M_Requisition_ID, DocumentNo, IsActive='Y')
    /// - Currency: C_Currency curr (C_Currency_ID, ISO_Code, CurSymbol, StdPrecision)
    /// 
    /// DATABASE PORTABILITY:
    /// - ANSI SQL standard joins (INNER JOIN, LEFT JOIN)
    /// - COALESCE used for NULL coalescing (Oracle & PostgreSQL portable)
    /// - Parameterized SQL statements with MRole tenant/org access enforcement
    /// - Zero provider-specific SQL functions (no DECODE, NVL, TRUNC, etc.)
    ///
    /// Chronological development:
    ///   Claude 2026-10-16 Widget_Bug_Fix Sr 148-154, GetPurchaseOrderDetail (PO detail pop-up):
    ///     UOM shown by full name ("Each", not the "Ea" symbol); a charge line shows the selected
    ///     charge name instead of "Standard Product"; ordered / received / pending are scaled to the
    ///     line's selected UOM (QtyEntered, as VAS_206 does); pending counts Item-type products only
    ///     (charges and other product types are excluded).
    ///   Claude 2026-10-16 Widget_Bug_Fix Sr 154 (re-done): tax-inclusive is judged by the order's PRICE LIST
    ///     (M_PriceList.IsTaxIncluded), as the Purchase Order window does (VAS_092 / VAS_303), not by
    ///     C_Order.IsTaxIncluded, which is a copy taken when the list was assigned and can be stale. On a
    ///     tax-inclusive list each line shows LineNetAmt less its tax (TaxAmt + SurchargeAmt, or derived from
    ///     the tax rate when the line's TaxAmt is 0), and subTotal = SUM(LineNetAmt) - SUM(C_OrderTax.TaxAmt).
    ///   Claude 2026-10-16 Sr 154 (final, user rule: "if the price list IsTaxIncluded = Y reduce that tax amount
    ///     and then show the value; if N show it directly; also applies to the total amount"): the pop-up shows
    ///     amounts WITHOUT tax. Inclusive list: line = LineNetAmt less its tax, totalAmount = SUM(LineNetAmt) -
    ///     SUM(C_OrderTax.TaxAmt). Exclusive list: LineNetAmt as stored. This supersedes the with-tax variant above.
    ///   Claude 2026-10-16 Search results paging: the search no longer stops at the first 8 matches.
    ///     SearchPurchaseOrders / Search take maxRows (default 25) and offset and return hasMore; the
    ///     widget requests the next page as the results list is scrolled, until every match is shown.
    ///   Claude 2026-10-16 Widget_Bug_Fix Sr 154 (re-done again, user: "it didnt show amount with tax"): the
    ///     pop-up now shows amounts WITH tax. Line amount = LineNetAmt on a tax-inclusive price list (already
    ///     gross), LineNetAmt + line tax on a tax-exclusive one; totalAmount (replaces subTotal) = the window's
    ///     Grand Total (inclusive: SUM(LineNetAmt); exclusive: lines + SUM(C_OrderTax.TaxAmt)).
    /// </summary>
    [AjaxAuthorize]
    [AjaxSessionFilter]
    public class VAS_203_POGlobalSearchWidgetController : Controller
    {
        private static readonly VLogger _log = VLogger.GetVLogger(typeof(VAS_203_POGlobalSearchWidgetController));
        private const int SEARCH_PAGE_SIZE = 25;   // default rows per page; the widget loads further pages as the list is scrolled
        private const int MAX_RECENT_RESULTS = 6;

        public ActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// Global search across purchase order entities, or recent POs if query is empty.
        /// </summary>
        /// <param name="query">Search text</param>
        /// <param name="q">Alternative parameter name for search text</param>
        [HttpGet]
        public JsonResult SearchPurchaseOrders(string query = null, string q = null, int maxRows = SEARCH_PAGE_SIZE, int offset = 0)
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null)
            {
                return Fail(Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired");
            }

            string searchTerm = !string.IsNullOrEmpty(query) ? query : q;
            searchTerm = (searchTerm ?? "").Trim();

            if (string.IsNullOrEmpty(searchTerm))
            {
                return GetRecentPurchaseOrders(ctx);
            }

            if (maxRows < 1) { maxRows = 1; }
            if (maxRows > 50) { maxRows = 50; }
            if (offset < 0) { offset = 0; }
            if (offset > 100000) { offset = 100000; }

            return ExecuteSearch(ctx, searchTerm, maxRows, offset);
        }

        /// <summary>
        /// Generic search endpoint alias matching Onfinity convention.
        /// </summary>
        [HttpGet]
        public JsonResult Search(string query = null, string q = null, int maxRows = SEARCH_PAGE_SIZE, int offset = 0)
        {
            return SearchPurchaseOrders(query, q, maxRows, offset);
        }

        /// <summary>
        /// Retrieves 6 recent accessible purchase orders when the search box is focused with empty input.
        /// </summary>
        private JsonResult GetRecentPurchaseOrders(Ctx ctx)
        {
            List<object> results = new List<object>();
            IDataReader dr = null;

            try
            {
                string sql = @"
                    SELECT
                        o.C_Order_ID AS purchase_order_id,
                        o.DocumentNo AS purchase_order_number,
                        o.DateOrdered AS order_date,
                        o.DocStatus AS document_status,
                        bp.Name AS vendor_name,
                        CASE WHEN COALESCE((SELECT pl.IsTaxIncluded FROM M_PriceList pl WHERE pl.M_PriceList_ID = o.M_PriceList_ID), 'N') = 'Y' THEN COALESCE(o.TotalLines, 0) - (SELECT COALESCE(SUM(COALESCE(tl.TaxAmt, 0)), 0) FROM C_OrderTax tl WHERE tl.C_Order_ID = o.C_Order_ID AND tl.IsActive = 'Y') ELSE COALESCE(o.TotalLines, 0) END AS total_amount, -- Sub total (excl. taxes; TotalLines already holds the tax on a tax-inclusive price list, so the extracted tax of the order (C_OrderTax) is taken off - the line TaxAmt is not maintained on every line)
                        curr.ISO_Code AS currency_code,
                        curr.CurSymbol AS currency_symbol,
                        curr.StdPrecision AS currency_precision
                    FROM C_Order o
                    INNER JOIN C_BPartner bp ON bp.C_BPartner_ID = o.C_BPartner_ID
                    LEFT JOIN C_Currency curr ON curr.C_Currency_ID = o.C_Currency_ID
                    WHERE o.AD_Client_ID = @AD_Client_ID
                      AND o.IsActive = 'Y'
                      AND o.IsSOTrx = 'N'
                      AND COALESCE(o.IsReturnTrx, 'N') = 'N'
                      AND COALESCE(o.IsBlanketTrx, 'N') = 'N'
                    ORDER BY o.DateOrdered DESC, o.DocumentNo DESC";

                sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                SqlParameter[] parameters = new SqlParameter[]
                {
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
                };

                dr = DB.ExecuteReader(sql, parameters);
                int count = 0;

                while (dr != null && dr.Read() && count < MAX_RECENT_RESULTS)
                {
                    int orderId = Util.GetValueOfInt(dr["purchase_order_id"]);
                    string docNo = Util.GetValueOfString(dr["purchase_order_number"]);
                    DateTime? orderDate = Util.GetValueOfDateTime(dr["order_date"]);
                    string docStatus = Util.GetValueOfString(dr["document_status"]);
                    string vendorName = Util.GetValueOfString(dr["vendor_name"]);
                    decimal totalAmount = Util.GetValueOfDecimal(dr["total_amount"]);
                    string currencyCode = Util.GetValueOfString(dr["currency_code"]);
                    string currencySymbol = Util.GetValueOfString(dr["currency_symbol"]);
                    int currencyPrecision = dr["currency_precision"] == DBNull.Value ? 2 : Util.GetValueOfInt(dr["currency_precision"]);

                    string statusLabel = GetDocStatusLabel(ctx, docStatus);
                    string dateFormatted = orderDate.HasValue ? orderDate.Value.ToString("dd MMM yyyy", CultureInfo.InvariantCulture) : "";
                    string subtitle = $"{docNo} · {dateFormatted} · {vendorName} · {statusLabel}";
                    string formattedValue = FormatAmount(totalAmount, currencySymbol, currencyCode, currencyPrecision);

                    results.Add(new
                    {
                        orderId = orderId,
                        orderLineId = (int?)null,
                        documentNo = docNo,
                        group = Msg.GetMsg(ctx, "VAS_203_RecentAcrossPurchase") ?? "Recent across purchase",
                        groupKey = "Recent",
                        title = docNo,
                        subtitle = subtitle,
                        value = formattedValue,
                        amount = totalAmount,
                        currencySymbol = currencySymbol,
                        currencyCode = currencyCode,
                        docStatus = docStatus,
                        statusLabel = statusLabel,
                        orderDate = orderDate.HasValue ? orderDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : ""
                    });

                    count++;
                }

                return Ok(new
                {
                    totalCount = results.Count,
                    results = results
                });
            }
            catch (Exception ex)
            {
                _log.Severe("VAS_203_POGlobalSearchWidgetController.GetRecentPurchaseOrders: " + ex.Message);
                return Fail(Msg.GetMsg(ctx, "VAS_203_SearchError") ?? "Unable to search purchase orders. Please try again.");
            }
            finally
            {
                if (dr != null)
                {
                    dr.Close();
                    dr.Dispose();
                }
            }
        }

        /// <summary>
        /// Executes full multi-entity search and ranks / deduplicates results server-side.
        /// </summary>
        private JsonResult ExecuteSearch(Ctx ctx, string searchTerm, int maxRows, int offset)
        {
            string likePattern = "%" + searchTerm.ToLowerInvariant() + "%";
            List<SearchResultCandidate> candidates = new List<SearchResultCandidate>();
            HashSet<string> seenKeys = new HashSet<string>();
            IDataReader dr = null;

            try
            {
                string sql = @"
                    SELECT
                        o.C_Order_ID AS purchase_order_id,
                        o.DocumentNo AS purchase_order_number,
                        o.DateOrdered AS order_date,
                        o.DocStatus AS document_status,
                        bp.Name AS vendor_name,
                        w.Name AS warehouse_name,
                        rep.Name AS representative_name,
                        o.POReference AS order_reference,
                        o.Description AS order_description,
                        bpl.Name AS location_name,
                        ol.C_OrderLine_ID AS purchase_order_line_id,
                        p.Name AS product_name,
                        p.Value AS product_code,
                        p.SKU AS product_sku,
                        r.DocumentNo AS requisition_number,
                        CASE WHEN COALESCE((SELECT pl.IsTaxIncluded FROM M_PriceList pl WHERE pl.M_PriceList_ID = o.M_PriceList_ID), 'N') = 'Y' THEN COALESCE(o.TotalLines, 0) - (SELECT COALESCE(SUM(COALESCE(tl.TaxAmt, 0)), 0) FROM C_OrderTax tl WHERE tl.C_Order_ID = o.C_Order_ID AND tl.IsActive = 'Y') ELSE COALESCE(o.TotalLines, 0) END AS total_amount, -- Sub total (excl. taxes; TotalLines already holds the tax on a tax-inclusive price list, so the extracted tax of the order (C_OrderTax) is taken off - the line TaxAmt is not maintained on every line)
                        curr.ISO_Code AS currency_code,
                        curr.CurSymbol AS currency_symbol,
                        curr.StdPrecision AS currency_precision
                    FROM C_Order o
                    INNER JOIN C_BPartner bp ON bp.C_BPartner_ID = o.C_BPartner_ID
                    LEFT JOIN M_Warehouse w ON w.M_Warehouse_ID = o.M_Warehouse_ID
                    LEFT JOIN AD_User rep ON rep.AD_User_ID = o.SalesRep_ID
                    LEFT JOIN C_BPartner_Location bpl ON bpl.C_BPartner_Location_ID = o.C_BPartner_Location_ID
                    LEFT JOIN C_OrderLine ol ON ol.C_Order_ID = o.C_Order_ID AND ol.IsActive = 'Y'
                    LEFT JOIN M_Product p ON p.M_Product_ID = ol.M_Product_ID
                    LEFT JOIN M_RequisitionLine rl ON rl.M_RequisitionLine_ID = ol.M_RequisitionLine_ID AND rl.IsActive = 'Y'
                    LEFT JOIN M_Requisition r ON r.M_Requisition_ID = rl.M_Requisition_ID AND r.IsActive = 'Y'
                    LEFT JOIN C_Currency curr ON curr.C_Currency_ID = o.C_Currency_ID
                    WHERE o.AD_Client_ID = @AD_Client_ID
                      AND o.IsActive = 'Y'
                      AND o.IsSOTrx = 'N'
                      AND COALESCE(o.IsReturnTrx, 'N') = 'N'
                      AND COALESCE(o.IsBlanketTrx, 'N') = 'N'
                      AND (
                           LOWER(o.DocumentNo) LIKE @SearchLike
                           OR LOWER(COALESCE(bp.Name, N'')) LIKE @SearchLike
                           OR LOWER(COALESCE(p.Name, N'')) LIKE @SearchLike
                           OR LOWER(COALESCE(p.Value, N'')) LIKE @SearchLike
                           OR LOWER(COALESCE(p.SKU, N'')) LIKE @SearchLike
                           OR LOWER(COALESCE(w.Name, N'')) LIKE @SearchLike
                           OR LOWER(COALESCE(rep.Name, N'')) LIKE @SearchLike
                           OR LOWER(COALESCE(o.POReference, N'')) LIKE @SearchLike
                           OR LOWER(COALESCE(o.Description, N'')) LIKE @SearchLike
                           OR LOWER(COALESCE(r.DocumentNo, N'')) LIKE @SearchLike
                           OR LOWER(COALESCE(bpl.Name, N'')) LIKE @SearchLike
                      )
                    ORDER BY o.DateOrdered DESC, o.DocumentNo DESC";

                sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                SqlParameter[] parameters = new SqlParameter[]
                {
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                    new SqlParameter("@SearchLike", likePattern)
                };

                dr = DB.ExecuteReader(sql, parameters);
                string qLower = searchTerm.ToLowerInvariant();

                while (dr != null && dr.Read())
                {
                    int orderId = Util.GetValueOfInt(dr["purchase_order_id"]);
                    string docNo = Util.GetValueOfString(dr["purchase_order_number"]);
                    DateTime? orderDate = Util.GetValueOfDateTime(dr["order_date"]);
                    string docStatus = Util.GetValueOfString(dr["document_status"]);
                    string vendorName = Util.GetValueOfString(dr["vendor_name"]);
                    string warehouseName = Util.GetValueOfString(dr["warehouse_name"]);
                    string repName = Util.GetValueOfString(dr["representative_name"]);
                    string poRef = Util.GetValueOfString(dr["order_reference"]);
                    string description = Util.GetValueOfString(dr["order_description"]);
                    string locName = Util.GetValueOfString(dr["location_name"]);
                    int orderLineId = Util.GetValueOfInt(dr["purchase_order_line_id"]);
                    string productName = Util.GetValueOfString(dr["product_name"]);
                    string productCode = Util.GetValueOfString(dr["product_code"]);
                    string productSku = Util.GetValueOfString(dr["product_sku"]);
                    string reqNo = Util.GetValueOfString(dr["requisition_number"]);
                    decimal totalAmount = Util.GetValueOfDecimal(dr["total_amount"]);
                    string currencyCode = Util.GetValueOfString(dr["currency_code"]);
                    string currencySymbol = Util.GetValueOfString(dr["currency_symbol"]);
                    int currencyPrecision = dr["currency_precision"] == DBNull.Value ? 2 : Util.GetValueOfInt(dr["currency_precision"]);

                    string statusLabel = GetDocStatusLabel(ctx, docStatus);
                    string dateFormatted = orderDate.HasValue ? orderDate.Value.ToString("dd MMM yyyy", CultureInfo.InvariantCulture) : "";
                    string subtitle = $"{docNo} · {dateFormatted} · {vendorName} · {statusLabel}";
                    string formattedValue = FormatAmount(totalAmount, currencySymbol, currencyCode, currencyPrecision);

                    // 1. Purchase Order match
                    if (!string.IsNullOrEmpty(docNo) && docNo.ToLowerInvariant().Contains(qLower))
                    {
                        bool isExact = docNo.Equals(searchTerm, StringComparison.OrdinalIgnoreCase);
                        AddCandidate(candidates, seenKeys, new SearchResultCandidate
                        {
                            Rank = isExact ? 0 : 1,
                            OrderId = orderId,
                            OrderLineId = null,
                            DocumentNo = docNo,
                            Group = Msg.GetMsg(ctx, "VAS_203_PurchaseOrder") ?? "Purchase order",
                            GroupKey = "Purchase order",
                            Title = docNo,
                            Subtitle = subtitle,
                            Value = formattedValue,
                            Amount = totalAmount,
                            CurrencySymbol = currencySymbol,
                            CurrencyCode = currencyCode,
                            DocStatus = docStatus,
                            StatusLabel = statusLabel,
                            OrderDate = orderDate,
                            OrderDateIso = orderDate.HasValue ? orderDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : ""
                        });
                    }

                    // 2. Vendor match
                    if (!string.IsNullOrEmpty(vendorName) && vendorName.ToLowerInvariant().Contains(qLower))
                    {
                        AddCandidate(candidates, seenKeys, new SearchResultCandidate
                        {
                            Rank = 2,
                            OrderId = orderId,
                            OrderLineId = null,
                            DocumentNo = docNo,
                            Group = Msg.GetMsg(ctx, "VAS_203_Vendor") ?? "Vendor",
                            GroupKey = "Vendor",
                            Title = vendorName,
                            Subtitle = subtitle,
                            Value = formattedValue,
                            Amount = totalAmount,
                            CurrencySymbol = currencySymbol,
                            CurrencyCode = currencyCode,
                            DocStatus = docStatus,
                            StatusLabel = statusLabel,
                            OrderDate = orderDate,
                            OrderDateIso = orderDate.HasValue ? orderDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : ""
                        });
                    }

                    // 3. Product match
                    bool matchProdName = !string.IsNullOrEmpty(productName) && productName.ToLowerInvariant().Contains(qLower);
                    bool matchProdCode = !string.IsNullOrEmpty(productCode) && productCode.ToLowerInvariant().Contains(qLower);
                    bool matchProdSku = !string.IsNullOrEmpty(productSku) && productSku.ToLowerInvariant().Contains(qLower);
                    if (matchProdName || matchProdCode || matchProdSku)
                    {
                        string prodTitle = !string.IsNullOrEmpty(productName) ? productName : productCode;
                        AddCandidate(candidates, seenKeys, new SearchResultCandidate
                        {
                            Rank = 3,
                            OrderId = orderId,
                            OrderLineId = orderLineId > 0 ? (int?)orderLineId : null,
                            DocumentNo = docNo,
                            Group = Msg.GetMsg(ctx, "VAS_203_Product") ?? "Product",
                            GroupKey = "Product",
                            Title = prodTitle,
                            Subtitle = subtitle,
                            Value = formattedValue,
                            Amount = totalAmount,
                            CurrencySymbol = currencySymbol,
                            CurrencyCode = currencyCode,
                            DocStatus = docStatus,
                            StatusLabel = statusLabel,
                            OrderDate = orderDate,
                            OrderDateIso = orderDate.HasValue ? orderDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : ""
                        });
                    }

                    // 4. Representative match
                    if (!string.IsNullOrEmpty(repName) && repName.ToLowerInvariant().Contains(qLower))
                    {
                        AddCandidate(candidates, seenKeys, new SearchResultCandidate
                        {
                            Rank = 4,
                            OrderId = orderId,
                            OrderLineId = null,
                            DocumentNo = docNo,
                            Group = Msg.GetMsg(ctx, "VAS_203_Representative") ?? "Representative",
                            GroupKey = "Representative",
                            Title = repName,
                            Subtitle = subtitle,
                            Value = formattedValue,
                            Amount = totalAmount,
                            CurrencySymbol = currencySymbol,
                            CurrencyCode = currencyCode,
                            DocStatus = docStatus,
                            StatusLabel = statusLabel,
                            OrderDate = orderDate,
                            OrderDateIso = orderDate.HasValue ? orderDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : ""
                        });
                    }

                    // 5. Warehouse match
                    if (!string.IsNullOrEmpty(warehouseName) && warehouseName.ToLowerInvariant().Contains(qLower))
                    {
                        AddCandidate(candidates, seenKeys, new SearchResultCandidate
                        {
                            Rank = 5,
                            OrderId = orderId,
                            OrderLineId = null,
                            DocumentNo = docNo,
                            Group = Msg.GetMsg(ctx, "VAS_203_Warehouse") ?? "Warehouse",
                            GroupKey = "Warehouse",
                            Title = warehouseName,
                            Subtitle = subtitle,
                            Value = formattedValue,
                            Amount = totalAmount,
                            CurrencySymbol = currencySymbol,
                            CurrencyCode = currencyCode,
                            DocStatus = docStatus,
                            StatusLabel = statusLabel,
                            OrderDate = orderDate,
                            OrderDateIso = orderDate.HasValue ? orderDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : ""
                        });
                    }

                    // 6. Requisition match
                    if (!string.IsNullOrEmpty(reqNo) && reqNo.ToLowerInvariant().Contains(qLower))
                    {
                        AddCandidate(candidates, seenKeys, new SearchResultCandidate
                        {
                            Rank = 6,
                            OrderId = orderId,
                            OrderLineId = orderLineId > 0 ? (int?)orderLineId : null,
                            DocumentNo = docNo,
                            Group = Msg.GetMsg(ctx, "VAS_203_Requisition") ?? "Requisition",
                            GroupKey = "Requisition",
                            Title = reqNo,
                            Subtitle = subtitle,
                            Value = formattedValue,
                            Amount = totalAmount,
                            CurrencySymbol = currencySymbol,
                            CurrencyCode = currencyCode,
                            DocStatus = docStatus,
                            StatusLabel = statusLabel,
                            OrderDate = orderDate,
                            OrderDateIso = orderDate.HasValue ? orderDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : ""
                        });
                    }

                    // 7. Order Reference match
                    if (!string.IsNullOrEmpty(poRef) && poRef.ToLowerInvariant().Contains(qLower))
                    {
                        AddCandidate(candidates, seenKeys, new SearchResultCandidate
                        {
                            Rank = 7,
                            OrderId = orderId,
                            OrderLineId = null,
                            DocumentNo = docNo,
                            Group = Msg.GetMsg(ctx, "VAS_203_OrderReference") ?? "Order reference",
                            GroupKey = "Order reference",
                            Title = poRef,
                            Subtitle = subtitle,
                            Value = formattedValue,
                            Amount = totalAmount,
                            CurrencySymbol = currencySymbol,
                            CurrencyCode = currencyCode,
                            DocStatus = docStatus,
                            StatusLabel = statusLabel,
                            OrderDate = orderDate,
                            OrderDateIso = orderDate.HasValue ? orderDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : ""
                        });
                    }

                    // 8. Description match
                    if (!string.IsNullOrEmpty(description) && description.ToLowerInvariant().Contains(qLower))
                    {
                        string descTitle = description.Length > 60 ? description.Substring(0, 57) + "..." : description;
                        AddCandidate(candidates, seenKeys, new SearchResultCandidate
                        {
                            Rank = 8,
                            OrderId = orderId,
                            OrderLineId = null,
                            DocumentNo = docNo,
                            Group = Msg.GetMsg(ctx, "VAS_203_Description") ?? "Description",
                            GroupKey = "Description",
                            Title = descTitle,
                            FullTitle = description,
                            Subtitle = subtitle,
                            Value = formattedValue,
                            Amount = totalAmount,
                            CurrencySymbol = currencySymbol,
                            CurrencyCode = currencyCode,
                            DocStatus = docStatus,
                            StatusLabel = statusLabel,
                            OrderDate = orderDate,
                            OrderDateIso = orderDate.HasValue ? orderDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : ""
                        });
                    }

                    // 9. Location match
                    if (!string.IsNullOrEmpty(locName) && locName.ToLowerInvariant().Contains(qLower))
                    {
                        AddCandidate(candidates, seenKeys, new SearchResultCandidate
                        {
                            Rank = 9,
                            OrderId = orderId,
                            OrderLineId = null,
                            DocumentNo = docNo,
                            Group = Msg.GetMsg(ctx, "VAS_203_Location") ?? "Location",
                            GroupKey = "Location",
                            Title = locName,
                            Subtitle = subtitle,
                            Value = formattedValue,
                            Amount = totalAmount,
                            CurrencySymbol = currencySymbol,
                            CurrencyCode = currencyCode,
                            DocStatus = docStatus,
                            StatusLabel = statusLabel,
                            OrderDate = orderDate,
                            OrderDateIso = orderDate.HasValue ? orderDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : ""
                        });
                    }
                }

                // Sorting: Exact match first -> Priority rank -> OrderDate DESC -> DocumentNo DESC
                var sorted = candidates
                    .OrderBy(c => c.Rank)
                    .ThenByDescending(c => c.OrderDate ?? DateTime.MinValue)
                    .ThenByDescending(c => c.DocumentNo)
                    .ToList();

                int totalCount = sorted.Count;
                // One page of the ranked list: rows [offset, offset + maxRows). The search is stateless, so each
                // page re-runs it; hasMore says whether rows remain beyond this page.
                bool hasMore = totalCount > offset + maxRows;
                var topResults = sorted.Skip(offset).Take(maxRows).Select(c => new
                {
                    orderId = c.OrderId,
                    orderLineId = c.OrderLineId,
                    documentNo = c.DocumentNo,
                    group = c.Group,
                    groupKey = c.GroupKey,
                    title = c.Title,
                    fullTitle = c.FullTitle ?? c.Title,
                    subtitle = c.Subtitle,
                    value = c.Value,
                    amount = c.Amount,
                    currencySymbol = c.CurrencySymbol,
                    currencyCode = c.CurrencyCode,
                    docStatus = c.DocStatus,
                    statusLabel = c.StatusLabel,
                    orderDate = c.OrderDateIso
                }).ToList();

                return Ok(new
                {
                    totalCount = totalCount,
                    hasMore = hasMore,
                    results = topResults
                });
            }
            catch (Exception ex)
            {
                _log.Severe("VAS_203_POGlobalSearchWidgetController.ExecuteSearch: " + ex.Message);
                return Fail(Msg.GetMsg(ctx, "VAS_203_SearchError") ?? "Unable to search purchase orders. Please try again.");
            }
            finally
            {
                if (dr != null)
                {
                    dr.Close();
                    dr.Dispose();
                }
            }
        }

        /// <summary>
        /// Detail for one purchase order (header + lines), shown in the widget's
        /// own detail modal. Per specification, clicking a search result opens
        /// THIS detail view instead of navigating to the Purchase Order screen.
        /// </summary>
        [HttpGet]
        public JsonResult GetPurchaseOrderDetail(int orderId)
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null)
            {
                return Fail(Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired");
            }

            if (orderId <= 0)
            {
                return Fail("Invalid purchase order");
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
                        o.POReference,
                        bp.Name AS VendorName,
                        wh.Name AS WarehouseName,
                        rep.Name AS SalesRepName,
                        curr.CurSymbol,
                        curr.ISO_Code,
                        COALESCE(curr.StdPrecision, 2) AS StdPrecision,
                        COALESCE(pl.IsTaxIncluded, 'N') AS PriceListTaxIncluded
                    FROM C_Order o
                    INNER JOIN C_BPartner bp ON bp.C_BPartner_ID = o.C_BPartner_ID
                    LEFT JOIN M_Warehouse wh ON wh.M_Warehouse_ID = o.M_Warehouse_ID
                    LEFT JOIN AD_User rep ON rep.AD_User_ID = o.SalesRep_ID
                    LEFT JOIN C_Currency curr ON curr.C_Currency_ID = o.C_Currency_ID
                    LEFT JOIN M_PriceList pl ON pl.M_PriceList_ID = o.M_PriceList_ID
                    WHERE o.C_Order_ID = @OrderID
                      AND o.AD_Client_ID = @ClientID
                      AND o.IsActive = 'Y'
                      AND o.IsSOTrx = 'N'";

                headerSql = MRole.GetDefault(ctx).AddAccessSQL(headerSql, "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                SqlParameter[] headParams = new SqlParameter[]
                {
                    new SqlParameter("@OrderID", orderId),
                    new SqlParameter("@ClientID", ctx.GetAD_Client_ID())
                };

                object header = null;
                // Whether the order's PRICE LIST includes tax - the flag the Purchase Order window judges by.
                bool plTaxIncluded = false;
                int stdPrecision = 2;
                IDataReader dr = null;
                try
                {
                    dr = DB.ExecuteReader(headerSql, headParams, null);
                    if (dr != null && dr.Read())
                    {
                        plTaxIncluded = Util.GetValueOfString(dr["PriceListTaxIncluded"]) == "Y";
                        stdPrecision = Util.GetValueOfInt(dr["StdPrecision"]);
                        DateTime? dateOrdered = Util.GetValueOfDateTime(dr["DateOrdered"]);
                        DateTime? datePromised = Util.GetValueOfDateTime(dr["DatePromised"]);
                        header = new
                        {
                            orderId = Util.GetValueOfInt(dr["C_Order_ID"]),
                            documentNo = Util.GetValueOfString(dr["DocumentNo"]),
                            vendorName = Util.GetValueOfString(dr["VendorName"]),
                            warehouseName = Util.GetValueOfString(dr["WarehouseName"]),
                            salesRepName = Util.GetValueOfString(dr["SalesRepName"]),
                            poReference = Util.GetValueOfString(dr["POReference"]),
                            docStatus = Util.GetValueOfString(dr["DocStatus"]),
                            docStatusLabel = GetDocStatusLabel(ctx, Util.GetValueOfString(dr["DocStatus"])),
                            grandTotal = Util.GetValueOfDecimal(dr["GrandTotal"]),
                            orderDate = dateOrdered.HasValue ? dateOrdered.Value.ToString("yyyy-MM-dd") : "",
                            orderDateDisplay = dateOrdered.HasValue ? dateOrdered.Value.ToString("dd MMM yyyy") : "",
                            promisedDateDisplay = datePromised.HasValue ? datePromised.Value.ToString("dd MMM yyyy") : "",
                            currencySymbol = Util.GetValueOfString(dr["CurSymbol"]),
                            currencyIso = Util.GetValueOfString(dr["ISO_Code"]),
                            stdPrecision = Util.GetValueOfInt(dr["StdPrecision"])
                        };
                    }
                }
                finally
                {
                    if (dr != null) { dr.Close(); dr.Dispose(); }
                }

                if (header == null)
                {
                    return Fail(Msg.GetMsg(ctx, "RecordNotFound") ?? "Record not found");
                }

                // p.Name / p.Value / asi.Description / uom.UOMSymbol / uom.Name / ol.Description are
                // NVARCHAR2 columns. COALESCE'ing any of them against a string literal (''/'Standard
                // Product') raises ORA-12704 (character set mismatch) on Oracle - selected raw here
                // (or COALESCE'd only against another column, never a literal) and defaulted in C#
                // instead. Same bug fixed the same way in VAS_161/163/165/164/215/209.
                // ProductType is a CHAR column, so it is selected raw and compared in C# (never
                // COALESCE'd against a literal in SQL). The charge name is likewise selected raw.
                // LineNetAmt is selected as stored; it is restated net of tax in C# when the price list
                // includes tax (see below). Surcharge and the tax rate are read for that restatement.
                string surchargeExpr = HasColumn("C_OrderLine", "SurchargeAmt") ? "COALESCE(ol.SurchargeAmt, 0)" : "0";
                string linesSql = @"
                    SELECT
                        ol.C_OrderLine_ID,
                        ol.Line,
                        ol.M_Product_ID,
                        ol.C_Charge_ID,
                        p.Name AS ProductNameRaw,
                        ch.Name AS ChargeNameRaw,
                        ol.Description AS OrderLineDescRaw,
                        p.Value AS ProductCode,
                        p.ProductType AS ProductType,
                        asi.Description AS AttributeDesc,
                        COALESCE(uom.Name, uom.UOMSymbol) AS UomName,
                        COALESCE(ol.QtyOrdered, 0) AS QtyOrdered,
                        COALESCE(ol.QtyEntered, ol.QtyOrdered, 0) AS QtyEntered,
                        COALESCE(ol.QtyDelivered, 0) AS QtyDelivered,
                        COALESCE(ol.PriceActual, 0) AS PriceActual,
                        COALESCE(ol.LineNetAmt, 0) AS LineNetAmt,
                        COALESCE(ol.TaxAmt, 0) AS LineTaxAmt,
                        " + surchargeExpr + @" AS LineSurchargeAmt,
                        COALESCE(tx.Rate, 0) AS TaxRate,
                        ol.C_Tax_ID AS LineTaxId
                    FROM C_OrderLine ol
                    LEFT JOIN C_Tax tx ON tx.C_Tax_ID = ol.C_Tax_ID
                    LEFT JOIN M_Product p ON p.M_Product_ID = ol.M_Product_ID
                    LEFT JOIN C_Charge ch ON ch.C_Charge_ID = ol.C_Charge_ID
                    LEFT JOIN C_UOM uom ON uom.C_UOM_ID = ol.C_UOM_ID
                    LEFT JOIN M_AttributeSetInstance asi ON asi.M_AttributeSetInstance_ID = ol.M_AttributeSetInstance_ID
                    WHERE ol.C_Order_ID = @OrderID
                      AND ol.IsActive = 'Y'
                    ORDER BY ol.Line ASC, ol.C_OrderLine_ID ASC";

                // linesSql only references @OrderID (no @ClientID predicate) - reusing headParams
                // here passed a @ClientID bind value with no matching placeholder in the SQL text,
                // which Oracle rejects with ORA-01006 "bind variable does not exist". A dedicated,
                // single-parameter array keeps the bound parameters in step with the SQL text.
                SqlParameter[] lineParams = new SqlParameter[]
                {
                    new SqlParameter("@OrderID", orderId)
                };

                // Tax-inclusive price list: the order's own tax summary (C_OrderTax) states, per tax, the net
                // base and the extracted tax of the lines carrying it. A line's amount without tax is its gross
                // scaled by that tax's base / (base + tax), so the lines add up exactly to the total below. The
                // line's own TaxAmt is not maintained on every line (often 0, or computed on the gross), which
                // left the gross - or a wrong net - in the amount.
                Dictionary<int, decimal> netRatioByTax = plTaxIncluded ? GetOrderTaxNetRatios(orderId) : new Dictionary<int, decimal>();

                var lines = new List<object>();
                decimal linesNet = 0m;       // line amounts as shown (without tax)
                decimal linesGross = 0m;     // SUM(LineNetAmt) as stored
                dr = null;
                try
                {
                    dr = DB.ExecuteReader(linesSql, lineParams, null);
                    while (dr != null && dr.Read())
                    {
                        // The price list decides (M_PriceList.IsTaxIncluded). Tax-INCLUSIVE ('Y'): LineNetAmt is the
                        // gross (it contains the line tax and surcharge), so that tax is taken off and the amount
                        // shown is without tax; where the line's TaxAmt is 0 / not maintained the tax is derived from
                        // the line's tax rate, so the gross is never left in. Tax-exclusive ('N'): LineNetAmt is
                        // shown directly.
                        decimal lineAmt = Util.GetValueOfDecimal(dr["LineNetAmt"]);
                        decimal lineNet = lineAmt;
                        int lineTaxId = Util.GetValueOfInt(dr["LineTaxId"]);
                        if (plTaxIncluded && netRatioByTax.ContainsKey(lineTaxId))
                        {
                            lineNet = Math.Round(lineAmt * netRatioByTax[lineTaxId], stdPrecision, MidpointRounding.AwayFromZero);
                        }
                        else if (plTaxIncluded)
                        {
                            decimal lineTax = Util.GetValueOfDecimal(dr["LineTaxAmt"]) + Util.GetValueOfDecimal(dr["LineSurchargeAmt"]);
                            decimal taxRate = Util.GetValueOfDecimal(dr["TaxRate"]);
                            if (lineTax == 0m && taxRate > 0m)
                            {
                                lineTax = Math.Round(lineAmt * taxRate / (100m + taxRate), stdPrecision, MidpointRounding.AwayFromZero);
                            }
                            lineNet = lineAmt - lineTax;
                        }
                        linesGross += lineAmt;
                        linesNet += lineNet;

                        // QtyEntered is in the line's own C_UOM_ID (the UOM the buyer picked);
                        // QtyOrdered / QtyDelivered are in the product's base UOM. The dialog shows
                        // the selected UOM beside the figures, so ordered is QtyEntered and received
                        // is scaled by this line's own entered/ordered ratio (same as VAS_206).
                        decimal qtyOrderedBase = Util.GetValueOfDecimal(dr["QtyOrdered"]);
                        decimal qtyEntered = Util.GetValueOfDecimal(dr["QtyEntered"]);
                        decimal uomRatio = (qtyOrderedBase != 0m) ? (qtyEntered / qtyOrderedBase) : 1m;
                        decimal qtyOrdered = qtyEntered;
                        decimal qtyDelivered = Util.GetValueOfDecimal(dr["QtyDelivered"]) * uomRatio;

                        // Pending is a stock figure: only Item-type products carry one. A charge line,
                        // a line without a product and every other product type are excluded.
                        bool isItem = Util.GetValueOfInt(dr["M_Product_ID"]) > 0
                            && string.Equals((Util.GetValueOfString(dr["ProductType"]) ?? "").Trim(), "I", StringComparison.OrdinalIgnoreCase);

                        // A charge line shows the selected charge's name, not "Standard Product".
                        string productName = Util.GetValueOfString(dr["ProductNameRaw"]);
                        if (string.IsNullOrEmpty(productName)) { productName = Util.GetValueOfString(dr["ChargeNameRaw"]); }
                        if (string.IsNullOrEmpty(productName)) { productName = Util.GetValueOfString(dr["OrderLineDescRaw"]); }
                        if (string.IsNullOrEmpty(productName)) { productName = "Standard Product"; }
                        lines.Add(new
                        {
                            lineId = Util.GetValueOfInt(dr["C_OrderLine_ID"]),
                            lineNo = Util.GetValueOfInt(dr["Line"]),
                            productName = productName,
                            productCode = Util.GetValueOfString(dr["ProductCode"]),
                            attribute = Util.GetValueOfString(dr["AttributeDesc"]),
                            uom = Util.GetValueOfString(dr["UomName"]),
                            isItem = isItem,
                            qtyOrdered = qtyOrdered,
                            qtyDelivered = qtyDelivered,
                            qtyPending = isItem ? Math.Max(0m, qtyOrdered - qtyDelivered) : 0m,
                            priceActual = Util.GetValueOfDecimal(dr["PriceActual"]),
                            lineNetAmt = lineNet
                        });
                    }
                }
                finally
                {
                    if (dr != null) { dr.Close(); dr.Dispose(); }
                }

                // Total amount, without tax, stated the way the Purchase Order window states its Sub Total
                // (VAS_092 / VAS_303): on a tax-inclusive price list SUM(LineNetAmt) is the gross, and the total is
                // that gross less the order's extracted tax SUM(C_OrderTax.TaxAmt); on a tax-exclusive one it is
                // SUM(LineNetAmt) directly. When an inclusive order carries no C_OrderTax rows the line-by-line
                // net above is used.
                decimal totalAmount = linesNet;
                if (plTaxIncluded)
                {
                    decimal orderTax = GetOrderTaxAmt(orderId);
                    if (orderTax > 0m) { totalAmount = linesGross - orderTax; }
                }

                return Ok(new { header = header, lines = lines, totalAmount = totalAmount });
            }
            catch (Exception ex)
            {
                _log.Log(Level.SEVERE, "VAS_203_POGlobalSearchWidget.GetPurchaseOrderDetail", ex);
                return Fail(Msg.GetMsg(ctx, "Error") ?? "Error");
            }
        }

        /// <summary>
        /// The order's total tax = SUM(C_OrderTax.TaxAmt) - the extracted tax the platform stores for both
        /// tax-exclusive and tax-inclusive price lists (same query as VAS_092_OverviewPurchaseOrderModel).
        /// </summary>
        private static decimal GetOrderTaxAmt(int orderId)
        {
            string sql = @"SELECT COALESCE(SUM(ot.TaxAmt), 0) AS TaxAmt
                             FROM C_OrderTax ot
                            WHERE ot.C_Order_ID = @OrderID
                              AND ot.IsActive = 'Y'";
            return Util.GetValueOfDecimal(DB.ExecuteScalar(sql, new SqlParameter[] { new SqlParameter("@OrderID", orderId) }, null));
        }

        /// <summary>
        /// Per tax of a tax-inclusive order, the share of a gross line amount that is net of tax:
        /// C_OrderTax.TaxBaseAmt (on an inclusive price list MOrderTax stores the base with the tax - and any
        /// surcharge - taken out) over SUM(LineNetAmt) of the order's lines carrying that tax (the gross).
        /// Taxes without a usable row are left out (the caller falls back to the line tax / rate).
        /// </summary>
        /// <param name="orderId">C_Order_ID</param>
        /// <returns>C_Tax_ID -> net ratio</returns>
        private static Dictionary<int, decimal> GetOrderTaxNetRatios(int orderId)
        {
            Dictionary<int, decimal> ratios = new Dictionary<int, decimal>();
            try
            {
                SqlParameter[] orderParam = new SqlParameter[] { new SqlParameter("@OrderID", orderId) };
                Dictionary<int, decimal> grossByTax = new Dictionary<int, decimal>();
                DataSet grossDs = DB.ExecuteDataset(@"SELECT ol.C_Tax_ID, COALESCE(SUM(ol.LineNetAmt), 0) AS Gross
                                                        FROM C_OrderLine ol
                                                       WHERE ol.C_Order_ID = @OrderID
                                                         AND ol.IsActive = 'Y'
                                                       GROUP BY ol.C_Tax_ID", orderParam, null);
                if (grossDs != null && grossDs.Tables.Count > 0)
                {
                    foreach (DataRow row in grossDs.Tables[0].Rows)
                    {
                        grossByTax[Util.GetValueOfInt(row["C_Tax_ID"])] = Util.GetValueOfDecimal(row["Gross"]);
                    }
                }

                DataSet ds = DB.ExecuteDataset(@"SELECT ot.C_Tax_ID, COALESCE(SUM(ot.TaxBaseAmt), 0) AS TaxBaseAmt
                                                   FROM C_OrderTax ot
                                                  WHERE ot.C_Order_ID = @OrderID
                                                    AND ot.IsActive = 'Y'
                                                  GROUP BY ot.C_Tax_ID",
                    new SqlParameter[] { new SqlParameter("@OrderID", orderId) }, null);
                if (ds != null && ds.Tables.Count > 0)
                {
                    foreach (DataRow row in ds.Tables[0].Rows)
                    {
                        int taxId = Util.GetValueOfInt(row["C_Tax_ID"]);
                        decimal taxBase = Util.GetValueOfDecimal(row["TaxBaseAmt"]);
                        decimal gross;
                        if (taxBase > 0m && grossByTax.TryGetValue(taxId, out gross) && gross > 0m && taxBase <= gross)
                        {
                            ratios[taxId] = taxBase / gross;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Log(Level.SEVERE, "VAS_203_POGlobalSearchWidget.GetOrderTaxNetRatios", ex);
            }
            return ratios;
        }

        private static bool HasColumn(string tableName, string columnName)
        {
            string sql;
            if (DB.IsPostgreSQL())
            {
                sql = @"
                    SELECT COUNT(1)
                    FROM information_schema.columns
                    WHERE UPPER(table_name)=UPPER(@TableName)
                      AND UPPER(column_name)=UPPER(@ColumnName)";
            }
            else
            {
                sql = @"
                    SELECT COUNT(1)
                    FROM USER_TAB_COLUMNS
                    WHERE TABLE_NAME=UPPER(@TableName)
                      AND COLUMN_NAME=UPPER(@ColumnName)";
            }

            return Util.GetValueOfInt(DB.ExecuteScalar(sql, new SqlParameter[]
            {
                new SqlParameter("@TableName", tableName),
                new SqlParameter("@ColumnName", columnName)
            }, null)) > 0;
        }

        private static void AddCandidate(List<SearchResultCandidate> list, HashSet<string> seen, SearchResultCandidate candidate)
        {
            string key = candidate.GroupKey + "_" + candidate.OrderId + (candidate.OrderLineId.HasValue ? "_" + candidate.OrderLineId.Value : "");
            if (seen.Add(key))
            {
                list.Add(candidate);
            }
        }

        /// <summary>
        /// Resolves a purchase order's document-status code (C_Order.DocStatus) to its
        /// human-readable name from the system's own DocStatus reference list
        /// (AD_Reference_ID 131) - the same source MOrder.GetDocStatusName uses - instead
        /// of an AD_Message lookup keyed by the raw code, which has no matching message
        /// for several codes (e.g. "Drafted", "InProgress") and rendered as "[Drafted]" /
        /// "[InProgress]" in the search results. Falls back to the raw code if the list
        /// name cannot be resolved.
        /// </summary>
        private string GetDocStatusLabel(Ctx ctx, string docStatus)
        {
            if (string.IsNullOrEmpty(docStatus)) return "";
            try
            {
                string name = MRefList.GetListName(ctx, 131, docStatus);
                return string.IsNullOrEmpty(name) ? docStatus : name;
            }
            catch
            {
                return docStatus;
            }
        }

        private string FormatAmount(decimal amount, string currencySymbol, string currencyCode, int precision)
        {
            int prec = precision >= 0 ? precision : 2;
            string symbol = !string.IsNullOrEmpty(currencySymbol) ? currencySymbol : (!string.IsNullOrEmpty(currencyCode) ? currencyCode : "");
            string formattedNum = amount.ToString("N" + prec, CultureInfo.InvariantCulture);
            return !string.IsNullOrEmpty(symbol) ? symbol + " " + formattedNum : formattedNum;
        }

        private JsonResult Ok(object data)
        {
            return Json(JsonConvert.SerializeObject(new
            {
                success = true,
                data = data
            }), JsonRequestBehavior.AllowGet);
        }

        private JsonResult Fail(string message)
        {
            return Json(JsonConvert.SerializeObject(new
            {
                success = false,
                error = message,
                message = message
            }), JsonRequestBehavior.AllowGet);
        }

        private class SearchResultCandidate
        {
            public int Rank { get; set; }
            public int OrderId { get; set; }
            public int? OrderLineId { get; set; }
            public string DocumentNo { get; set; }
            public string Group { get; set; }
            public string GroupKey { get; set; }
            public string Title { get; set; }
            // Untruncated text of the title (the Description group shortens Title to 60 characters);
            // the widget shows it as a tooltip. Null means Title is already complete.
            public string FullTitle { get; set; }
            public string Subtitle { get; set; }
            public string Value { get; set; }
            public decimal Amount { get; set; }
            public string CurrencySymbol { get; set; }
            public string CurrencyCode { get; set; }
            public string DocStatus { get; set; }
            public string StatusLabel { get; set; }
            public DateTime? OrderDate { get; set; }
            public string OrderDateIso { get; set; }
        }
    }
}
