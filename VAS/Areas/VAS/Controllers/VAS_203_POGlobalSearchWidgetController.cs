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
    /// </summary>
    [AjaxAuthorize]
    [AjaxSessionFilter]
    public class VAS_203_POGlobalSearchWidgetController : Controller
    {
        private static readonly VLogger _log = VLogger.GetVLogger(typeof(VAS_203_POGlobalSearchWidgetController));
        private const int MAX_SEARCH_RESULTS = 8;
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
        public JsonResult SearchPurchaseOrders(string query = null, string q = null)
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

            return ExecuteSearch(ctx, searchTerm);
        }

        /// <summary>
        /// Generic search endpoint alias matching Onfinity convention.
        /// </summary>
        [HttpGet]
        public JsonResult Search(string query = null, string q = null)
        {
            return SearchPurchaseOrders(query, q);
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
                        o.GrandTotal AS total_amount,
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
        private JsonResult ExecuteSearch(Ctx ctx, string searchTerm)
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
                        o.GrandTotal AS total_amount,
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
                var topResults = sorted.Take(MAX_SEARCH_RESULTS).Select(c => new
                {
                    orderId = c.OrderId,
                    orderLineId = c.OrderLineId,
                    documentNo = c.DocumentNo,
                    group = c.Group,
                    groupKey = c.GroupKey,
                    title = c.Title,
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

        private static void AddCandidate(List<SearchResultCandidate> list, HashSet<string> seen, SearchResultCandidate candidate)
        {
            string key = candidate.GroupKey + "_" + candidate.OrderId + (candidate.OrderLineId.HasValue ? "_" + candidate.OrderLineId.Value : "");
            if (seen.Add(key))
            {
                list.Add(candidate);
            }
        }

        private string GetDocStatusLabel(Ctx ctx, string docStatus)
        {
            if (string.IsNullOrEmpty(docStatus)) return "";
            switch (docStatus.ToUpperInvariant())
            {
                case "DR": return Msg.GetMsg(ctx, "Drafted") ?? "Drafted";
                case "IP": return Msg.GetMsg(ctx, "InProgress") ?? "In Progress";
                case "CO": return Msg.GetMsg(ctx, "Completed") ?? "Completed";
                case "CL": return Msg.GetMsg(ctx, "Closed") ?? "Closed";
                case "VO": return Msg.GetMsg(ctx, "Voided") ?? "Voided";
                default: return docStatus;
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
