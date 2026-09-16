/******************************************************
 * Module Name    : VAS
 * Purpose        : Sales Order dashboard Global Sales Search widget endpoints
 * chronological  : Development
 * Created Date   : 2026-09-11
 * Created by     : VAI052
 ******************************************************/

using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Web.Mvc;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VAS.Controllers
{
    /// <summary>
    /// Module Name : VAS_268_GlobalSalesSearchWidget
    /// Purpose     : Data endpoints for the 9x1 "Global Sales Search" widget - the
    ///               first widget on the Sales Order dashboard. (1) grouped, ranked
    ///               search across nine field families (Sales order / Customer /
    ///               Product / Representative / Warehouse / Quotation /
    ///               Description / Order reference / Location), every match
    ///               resolving to a concrete C_Order_ID, (2) the "recent across
    ///               sales" list (last 6 orders touched by the logged-in user),
    ///               and (3) the shared Sales Order record-preview modal's data
    ///               (header stats + paginated lines with per-line free-stock).
    ///               Sales Order rows are always scoped to IsSOTrx='Y',
    ///               IsReturnTrx='N', IsSalesQuotation='N' (transactional sales
    ///               orders only, per the widget spec) and to the logged-in
    ///               client/role's own record-access rules via MRole - a user can
    ///               never see a result here they could not open from the Sales
    ///               Order list/window itself.
    ///   Per the CTE/MRole rule (Prompt_Instructions.txt "Case 1"), AddAccessSQL is
    ///   applied ONLY to the base_orders CTE's own inner query against the main
    ///   physical table (C_Order "o") - never to the CTE alias, the "matches" CTE,
    ///   or the final combined statement, and never to the secondary
    ///   C_OrderLine/M_Product joins used only to match Product/Description terms.
    ///   Read-only here; no writes.
    /// Chronological development:
    ///   VAI052      2026-09-11 Created
    /// </summary>
    public class VAS_268_GlobalSalesSearchWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_268_GlobalSalesSearchWidgetController).FullName);

        private const int MinSearchLength = 2;
        private const int MaxRankedRows = 50;
        private const int DefaultDropdownRows = 10;
        private const int RecentRowCount = 6;
        private const int MaxLineRows = 200;

        /// <summary>
        /// Grouped, ranked sales-order search. Returns at most <paramref name="max"/>
        /// rows for the dropdown (default 10) plus Total (up to <see cref="MaxRankedRows"/>)
        /// so the client can offer "View all N results" when more exist.
        /// </summary>
        /// <param name="q">Raw search text - minimum 2 characters, matched case-insensitively as a %contains% fragment.</param>
        /// <param name="max">Dropdown row cap (defaults to 10, never more than <see cref="MaxRankedRows"/>).</param>
        /// <returns>JSON { Rows:[...], Total } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult SearchSalesOrders(string q, int max = DefaultDropdownRows)
        {
            if (Session["ctx"] == null) { return Json("", JsonRequestBehavior.AllowGet); }

            Ctx ctx = Session["ctx"] as Ctx;
            string searchText = (q ?? string.Empty).Trim();

            if (max <= 0 || max > MaxRankedRows) { max = DefaultDropdownRows; }
            if (searchText.Length < MinSearchLength)
            {
                return Json(JsonConvert.SerializeObject(new { Rows = new List<SearchRow>(), Total = 0 }), JsonRequestBehavior.AllowGet);
            }

            try
            {
                string json = JsonConvert.SerializeObject(SearchSalesOrdersData(ctx, searchText, max));
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_268_GlobalSalesSearchWidget.SearchSalesOrders", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// The 6 sales orders most recently updated by the logged-in user - shown
        /// when the search box gets focus while empty, still scoped by MRole like
        /// every other query here.
        /// </summary>
        /// <returns>JSON { Rows:[...] } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetRecentSalesOrders()
        {
            if (Session["ctx"] == null) { return Json("", JsonRequestBehavior.AllowGet); }

            Ctx ctx = Session["ctx"] as Ctx;

            try
            {
                string json = JsonConvert.SerializeObject(new { Rows = GetRecentSalesOrdersData(ctx) });
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_268_GlobalSalesSearchWidget.GetRecentSalesOrders", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// Full data for the shared Sales Order record-preview modal: header stats
        /// (customer / dates / value / warehouse / delivery mode / document status /
        /// derived delivery status) and every active line (capped at
        /// <see cref="MaxLineRows"/> - the client paginates these itself, sized to
        /// the space actually available, so the modal body never scrolls).
        /// </summary>
        /// <param name="id">C_Order_ID.</param>
        /// <returns>JSON { Order:{...}, Lines:[...] } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetSalesOrderDetail(int id)
        {
            if (Session["ctx"] == null) { return Json("", JsonRequestBehavior.AllowGet); }

            Ctx ctx = Session["ctx"] as Ctx;

            try
            {
                string json = JsonConvert.SerializeObject(GetSalesOrderDetailData(ctx, id));
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_268_GlobalSalesSearchWidget.GetSalesOrderDetail", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// The base_orders CTE body, WHERE-only text is embedded directly (this is
        /// the one piece of the statement that AddAccessSQL is applied to - see the
        /// class summary). Scope: active, transactional sales orders only
        /// (IsSOTrx='Y', not a return, not a quotation), per the widget spec.
        /// </summary>
        private static string BuildBaseOrdersSql()
        {
            return @"
                SELECT o.C_Order_ID AS Order_Id,
                       o.DocumentNo AS Document_No,
                       o.DateOrdered AS Date_Ordered,
                       o.DatePromised AS Date_Promised,
                       o.DocStatus AS Doc_Status_Code,
                       o.TotalLines AS Order_Value,
                       o.Description AS Description,
                       o.POReference AS Po_Reference,
                       o.C_Order_Quotation AS Quotation_Order_Id,
                       o.C_BPartner_ID AS Bpartner_Id,
                       o.C_BPartner_Location_ID AS Bpartner_Location_Id,
                       o.SalesRep_ID AS Sales_Rep_Id,
                       o.M_Warehouse_ID AS Warehouse_Id,
                       o.DeliveryViaRule AS Delivery_Via_Rule,
                       o.Updated AS Updated,
                       COALESCE(bp.Name, N'') AS Customer_Name,
                       COALESCE(rep.Name, N'') AS Representative_Name,
                       COALESCE(w.Name, N'') AS Warehouse_Name,
                       COALESCE(q.DocumentNo, N'') AS Quotation_No,
                       COALESCE(bpl.Name, N'') AS Location_Name,
                       COALESCE(loc.Address1, N'') AS Location_Address,
                       COALESCE(loc.City, N'') AS Location_City
                  FROM C_Order o
                  INNER JOIN C_BPartner bp ON ( bp.C_BPartner_ID = o.C_BPartner_ID )
                  LEFT OUTER JOIN AD_User rep ON ( rep.AD_User_ID = o.SalesRep_ID )
                  LEFT OUTER JOIN M_Warehouse w ON ( w.M_Warehouse_ID = o.M_Warehouse_ID )
                  LEFT OUTER JOIN C_Order q ON ( q.C_Order_ID = o.C_Order_Quotation )
                  LEFT OUTER JOIN C_BPartner_Location bpl ON ( bpl.C_BPartner_Location_ID = o.C_BPartner_Location_ID )
                  LEFT OUTER JOIN C_Location loc ON ( loc.C_Location_ID = bpl.C_Location_ID )
                 WHERE o.AD_Client_ID = @AD_Client_ID
                   AND o.IsActive = 'Y'
                   AND o.IsSOTrx = 'Y'
                   AND COALESCE(o.IsReturnTrx, 'N') = 'N'
                   AND COALESCE(o.IsSalesQuotation, 'N') = 'N'";
        }

        private SearchResult SearchSalesOrdersData(Ctx ctx, string searchText, int max)
        {
            SearchResult result = new SearchResult { Rows = new List<SearchRow>() };
            if (ctx == null) { return result; }

            string language = GetLanguage(ctx);
            Dictionary<string, string> docStatusMap = GetDecodeMap("C_Order", "DocStatus", language);

            // The one physical-table piece AddAccessSQL is applied to (Prompt_Instructions
            // Case 1) - every branch of "matches" below reads from this CTE, already
            // access-filtered, plus secondary C_OrderLine/M_Product joins used only for
            // matching (never re-scoped, mirroring the C_InvoiceA/C_InvoiceB example).
            string baseOrdersSql = MRole.GetDefault(ctx).AddAccessSQL(BuildBaseOrdersSql(), "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string likeValue = "%" + EscapeLike(searchText.ToUpperInvariant()) + "%";

            // @Search_Pattern is referenced 15 times across the nine branches below -
            // each occurrence gets its OWN uniquely-suffixed parameter (same
            // underlying likeValue) rather than reusing one name, the same rule
            // this codebase's other multi-occurrence queries follow (see VAS_246's
            // dual CurrencyConvert @BaseCurrency_ID_1/_2, VAS_260/261's summary
            // queries) - Oracle's positional bind resolution throws ORA-01008 ("not
            // all variables bound") when one parameter object is asked to satisfy
            // more than one placeholder occurrence.
            string sql = @"
                WITH base_orders AS (" + baseOrdersSql + @"),
                     matches AS (
                       SELECT DISTINCT 1 AS Category_Priority, base.Document_No AS Matched_Text, base.*
                         FROM base_orders base
                        WHERE UPPER(COALESCE(base.Document_No, N'')) LIKE @Search_Pattern_1 ESCAPE '\'

                        UNION ALL

                       SELECT DISTINCT 2, base.Customer_Name, base.*
                         FROM base_orders base
                        WHERE UPPER(base.Customer_Name) LIKE @Search_Pattern_2 ESCAPE '\'

                        UNION ALL

                       SELECT DISTINCT 3, COALESCE(p.Name, p.Value), base.*
                         FROM base_orders base
                        INNER JOIN C_OrderLine ol ON ( ol.C_Order_ID = base.Order_Id AND ol.IsActive = 'Y' )
                        INNER JOIN M_Product p ON ( p.M_Product_ID = ol.M_Product_ID )
                        WHERE UPPER(COALESCE(p.Name, N'')) LIKE @Search_Pattern_3 ESCAPE '\'
                           OR UPPER(COALESCE(p.Value, N'')) LIKE @Search_Pattern_4 ESCAPE '\'
                           OR UPPER(COALESCE(p.SKU, N'')) LIKE @Search_Pattern_5 ESCAPE '\'

                        UNION ALL

                       SELECT DISTINCT 4, base.Representative_Name, base.*
                         FROM base_orders base
                        WHERE UPPER(base.Representative_Name) LIKE @Search_Pattern_6 ESCAPE '\'

                        UNION ALL

                       SELECT DISTINCT 5, base.Warehouse_Name, base.*
                         FROM base_orders base
                        WHERE UPPER(base.Warehouse_Name) LIKE @Search_Pattern_7 ESCAPE '\'

                        UNION ALL

                       SELECT DISTINCT 6, base.Quotation_No, base.*
                         FROM base_orders base
                        WHERE UPPER(base.Quotation_No) LIKE @Search_Pattern_8 ESCAPE '\'

                        UNION ALL

                       SELECT DISTINCT 7,
                              CASE WHEN UPPER(COALESCE(ol.Description, N'')) LIKE @Search_Pattern_9 ESCAPE '\' THEN ol.Description ELSE base.Description END,
                              base.*
                         FROM base_orders base
                         LEFT OUTER JOIN C_OrderLine ol ON ( ol.C_Order_ID = base.Order_Id AND ol.IsActive = 'Y' )
                        WHERE UPPER(COALESCE(base.Description, N'')) LIKE @Search_Pattern_10 ESCAPE '\'
                           OR UPPER(COALESCE(ol.Description, N'')) LIKE @Search_Pattern_11 ESCAPE '\'

                        UNION ALL

                       SELECT DISTINCT 8, base.Po_Reference, base.*
                         FROM base_orders base
                        WHERE UPPER(COALESCE(base.Po_Reference, N'')) LIKE @Search_Pattern_12 ESCAPE '\'

                        UNION ALL

                       SELECT DISTINCT 9,
                              COALESCE(NULLIF(base.Location_Name, N''), NULLIF(base.Location_Address, N''), base.Location_City),
                              base.*
                         FROM base_orders base
                        WHERE UPPER(base.Location_Name) LIKE @Search_Pattern_13 ESCAPE '\'
                           OR UPPER(base.Location_Address) LIKE @Search_Pattern_14 ESCAPE '\'
                           OR UPPER(base.Location_City) LIKE @Search_Pattern_15 ESCAPE '\'
                     )
                SELECT Category_Priority, Matched_Text, Order_Id, Document_No, Date_Ordered,
                       Customer_Name, Doc_Status_Code, Order_Value,
                       COUNT(1) OVER () AS Total_Matches
                  FROM matches
                 ORDER BY Category_Priority, Updated DESC, Document_No
                 OFFSET 0 ROWS FETCH NEXT @Max_Ranked_Rows ROWS ONLY";

            List<SqlParameter> parameters = new List<SqlParameter>
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
            };
            for (int i = 1; i <= 15; i++)
            {
                parameters.Add(new SqlParameter("@Search_Pattern_" + i, SqlDbType.NVarChar) { Value = likeValue });
            }
            parameters.Add(new SqlParameter("@Max_Ranked_Rows", MaxRankedRows));

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, parameters.ToArray());

                int total = 0;
                while (dr != null && dr.Read())
                {
                    total = dr["Total_Matches"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Total_Matches"]);
                    if (result.Rows.Count >= max) { continue; }

                    string docStatusCode = Util.GetValueOfString(dr["Doc_Status_Code"]);
                    DateTime? dateOrdered = dr["Date_Ordered"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr["Date_Ordered"]);

                    result.Rows.Add(new SearchRow
                    {
                        Category = CategoryLabel(Util.GetValueOfInt(dr["Category_Priority"])),
                        MatchedText = Util.GetValueOfString(dr["Matched_Text"]),
                        SalesOrderId = Util.GetValueOfInt(dr["Order_Id"]),
                        SalesOrderNumber = Util.GetValueOfString(dr["Document_No"]),
                        SalesOrderDate = dateOrdered.HasValue ? dateOrdered.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                        CustomerName = Util.GetValueOfString(dr["Customer_Name"]),
                        DocumentStatus = DecodeLabel(docStatusMap, docStatusCode),
                        OrderValue = dr["Order_Value"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Order_Value"])
                    });
                }
                result.Total = total;
            }
            finally
            {
                CloseReader(dr);
            }

            return result;
        }

        /// <summary>The nine fixed search-category labels, by their 1-9 priority (matches the widget's own fixed group order).</summary>
        private static string CategoryLabel(int priority)
        {
            switch (priority)
            {
                case 1: return "Sales order";
                case 2: return "Customer";
                case 3: return "Product";
                case 4: return "Representative";
                case 5: return "Warehouse";
                case 6: return "Quotation";
                case 7: return "Description";
                case 8: return "Order reference";
                case 9: return "Location";
                default: return "";
            }
        }

        private List<RecentRow> GetRecentSalesOrdersData(Ctx ctx)
        {
            List<RecentRow> rows = new List<RecentRow>();
            if (ctx == null) { return rows; }

            string language = GetLanguage(ctx);
            Dictionary<string, string> docStatusMap = GetDecodeMap("C_Order", "DocStatus", language);

            string sql = @"
                SELECT o.C_Order_ID AS Order_Id,
                       o.DocumentNo AS Document_No,
                       o.DateOrdered AS Date_Ordered,
                       o.DocStatus AS Doc_Status_Code,
                       o.TotalLines AS Order_Value,
                       COALESCE(bp.Name, N'') AS Customer_Name
                  FROM C_Order o
                  INNER JOIN C_BPartner bp ON ( bp.C_BPartner_ID = o.C_BPartner_ID )
                 WHERE o.AD_Client_ID = @AD_Client_ID
                   AND o.IsActive = 'Y'
                   AND o.IsSOTrx = 'Y'
                   AND COALESCE(o.IsReturnTrx, 'N') = 'N'
                   AND COALESCE(o.IsSalesQuotation, 'N') = 'N'
                   AND o.UpdatedBy = @AD_User_ID
                 ORDER BY o.Updated DESC
                 OFFSET 0 ROWS FETCH NEXT @Recent_Row_Count ROWS ONLY";

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, new[]
                {
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                    new SqlParameter("@AD_User_ID", ctx.GetAD_User_ID()),
                    new SqlParameter("@Recent_Row_Count", RecentRowCount)
                });

                while (dr != null && dr.Read())
                {
                    string docStatusCode = Util.GetValueOfString(dr["Doc_Status_Code"]);
                    DateTime? dateOrdered = dr["Date_Ordered"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr["Date_Ordered"]);

                    rows.Add(new RecentRow
                    {
                        SalesOrderId = Util.GetValueOfInt(dr["Order_Id"]),
                        SalesOrderNumber = Util.GetValueOfString(dr["Document_No"]),
                        SalesOrderDate = dateOrdered.HasValue ? dateOrdered.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                        CustomerName = Util.GetValueOfString(dr["Customer_Name"]),
                        DocumentStatus = DecodeLabel(docStatusMap, docStatusCode),
                        OrderValue = dr["Order_Value"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Order_Value"])
                    });
                }
            }
            finally
            {
                CloseReader(dr);
            }

            return rows;
        }

        private OrderDetailResult GetSalesOrderDetailData(Ctx ctx, int orderId)
        {
            OrderDetailResult result = new OrderDetailResult { Lines = new List<OrderLineRow>() };
            if (ctx == null || orderId <= 0) { return result; }

            string language = GetLanguage(ctx);
            Dictionary<string, string> docStatusMap = GetDecodeMap("C_Order", "DocStatus", language);

            string headerSql = @"
                SELECT o.C_Order_ID AS Order_Id,
                       o.DocumentNo AS Document_No,
                       o.DateOrdered AS Date_Ordered,
                       o.DatePromised AS Date_Promised,
                       o.DocStatus AS Doc_Status_Code,
                       o.TotalLines AS Order_Value,
                       o.DeliveryViaRule AS Delivery_Via_Rule,
                       o.M_Warehouse_ID AS Warehouse_Id,
                       COALESCE(bp.Name, N'') AS Customer_Name,
                       COALESCE(w.Name, N'') AS Warehouse_Name
                  FROM C_Order o
                  INNER JOIN C_BPartner bp ON ( bp.C_BPartner_ID = o.C_BPartner_ID )
                  LEFT OUTER JOIN M_Warehouse w ON ( w.M_Warehouse_ID = o.M_Warehouse_ID )
                 WHERE o.C_Order_ID = @Order_Id
                   AND o.AD_Client_ID = @AD_Client_ID
                   AND o.IsActive = 'Y'";

            headerSql = MRole.GetDefault(ctx).AddAccessSQL(headerSql, "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string docStatusCode = "";
            string warehouseName = "";
            int warehouseId = 0;

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(headerSql, new[]
                {
                    new SqlParameter("@Order_Id", orderId),
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
                });

                if (dr == null || !dr.Read()) { return result; }

                docStatusCode = Util.GetValueOfString(dr["Doc_Status_Code"]);
                warehouseName = Util.GetValueOfString(dr["Warehouse_Name"]);
                warehouseId = dr["Warehouse_Id"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Warehouse_Id"]);
                DateTime? dateOrdered = dr["Date_Ordered"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr["Date_Ordered"]);
                DateTime? datePromised = dr["Date_Promised"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr["Date_Promised"]);

                result.Order = new OrderHeader
                {
                    SalesOrderId = Util.GetValueOfInt(dr["Order_Id"]),
                    SalesOrderNumber = Util.GetValueOfString(dr["Document_No"]),
                    SalesOrderDate = dateOrdered.HasValue ? dateOrdered.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                    DatePromised = datePromised.HasValue ? datePromised.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                    CustomerName = Util.GetValueOfString(dr["Customer_Name"]),
                    DocumentStatus = DecodeLabel(docStatusMap, docStatusCode),
                    DocumentStatusCode = docStatusCode,
                    OrderValue = dr["Order_Value"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Order_Value"]),
                    WarehouseName = warehouseName,
                    DeliveryMode = DecodeLabel(GetDecodeMap("C_Order", "DeliveryViaRule", language), Util.GetValueOfString(dr["Delivery_Via_Rule"]))
                };
            }
            finally
            {
                CloseReader(dr);
            }

            if (result.Order == null) { return result; }

            LoadOrderLines(ctx, result, orderId, warehouseId);

            bool anyDelivered = false, allDelivered = true, hasLines = result.Lines.Count > 0;
            foreach (OrderLineRow line in result.Lines)
            {
                if (line.QtyDelivered > 0) { anyDelivered = true; }
                if (line.QtyDelivered < line.QtyOrdered) { allDelivered = false; }
            }

            string deliveryStatusKey, deliveryStatusFallback;
            if (docStatusCode == "CL" || docStatusCode == "VO")
            {
                deliveryStatusKey = "VAS_268_DeliveryNA"; deliveryStatusFallback = "Not applicable";
            }
            else if (hasLines && allDelivered)
            {
                deliveryStatusKey = "VAS_268_DeliveryFull"; deliveryStatusFallback = "Fully delivered";
            }
            else if (anyDelivered)
            {
                deliveryStatusKey = "VAS_268_DeliveryPartial"; deliveryStatusFallback = "Partial";
            }
            else
            {
                deliveryStatusKey = "VAS_268_DeliveryPending"; deliveryStatusFallback = "Pending";
            }
            result.Order.DeliveryStatus = Msg.GetMsg(ctx, deliveryStatusKey) ?? deliveryStatusFallback;

            return result;
        }

        /// <summary>
        /// Every active line of the order, each carrying its own free-stock figure
        /// (per <see cref="ResolveLineWarehouseAndFreeStock"/>) and a derived line
        /// status chip mirroring the header DocStatus / delivered-vs-ordered qty.
        /// Capped at <see cref="MaxLineRows"/> - the client paginates client-side,
        /// sized to fit the modal without an inner scrollbar.
        /// </summary>
        private void LoadOrderLines(Ctx ctx, OrderDetailResult result, int orderId, int headerWarehouseId)
        {
            string sql = @"
                SELECT ol.C_OrderLine_ID AS Line_Id,
                       ol.Line AS Line_No,
                       ol.M_Product_ID AS Product_Id,
                       ol.Description AS Description,
                       ol.PrintDescription AS Print_Description,
                       ol.QtyOrdered AS Qty_Ordered,
                       ol.QtyDelivered AS Qty_Delivered,
                       ol.PriceActual AS Price_Actual,
                       ol.LineNetAmt AS Line_Net_Amt,
                       ol.M_AttributeSetInstance_ID AS Asi_Id,
                       ol.C_UOM_ID AS Uom_Id,
                       ol.M_Warehouse_ID AS Line_Warehouse_Id,
                       COALESCE(p.Name, p.Value) AS Product_Name,
                       COALESCE(asi.Description, N'') AS Attribute_Text,
                       COALESCE(u.Name, N'') AS Uom_Name
                  FROM C_OrderLine ol
                  LEFT OUTER JOIN M_Product p ON ( p.M_Product_ID = ol.M_Product_ID )
                  LEFT OUTER JOIN M_AttributeSetInstance asi ON ( asi.M_AttributeSetInstance_ID = ol.M_AttributeSetInstance_ID )
                  LEFT OUTER JOIN C_UOM u ON ( u.C_UOM_ID = ol.C_UOM_ID )
                 WHERE ol.C_Order_ID = @Order_Id
                   AND ol.IsActive = 'Y'
                 ORDER BY ol.Line ASC
                 OFFSET 0 ROWS FETCH NEXT @Max_Line_Rows ROWS ONLY";

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, new[]
                {
                    new SqlParameter("@Order_Id", orderId),
                    new SqlParameter("@Max_Line_Rows", MaxLineRows)
                });

                while (dr != null && dr.Read())
                {
                    decimal qtyOrdered = dr["Qty_Ordered"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Qty_Ordered"]);
                    decimal qtyDelivered = dr["Qty_Delivered"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Qty_Delivered"]);
                    int productId = dr["Product_Id"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Product_Id"]);
                    int lineWarehouseId = dr["Line_Warehouse_Id"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Line_Warehouse_Id"]);
                    int warehouseId = lineWarehouseId > 0 ? lineWarehouseId : headerWarehouseId;

                    string description = Util.GetValueOfString(dr["Description"]);
                    if (string.IsNullOrEmpty(description)) { description = Util.GetValueOfString(dr["Print_Description"]); }

                    result.Lines.Add(new OrderLineRow
                    {
                        LineNo = Util.GetValueOfInt(dr["Line_No"]),
                        ProductName = Util.GetValueOfString(dr["Product_Name"]),
                        Description = description,
                        AttributeText = Util.GetValueOfString(dr["Attribute_Text"]),
                        UomName = Util.GetValueOfString(dr["Uom_Name"]),
                        QtyOrdered = qtyOrdered,
                        QtyDelivered = qtyDelivered,
                        QtyPending = qtyOrdered - qtyDelivered,
                        FreeStock = ResolveFreeStock(ctx, productId, warehouseId),
                        Rate = dr["Price_Actual"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Price_Actual"]),
                        Amount = dr["Line_Net_Amt"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Line_Net_Amt"])
                    });
                }
            }
            finally
            {
                CloseReader(dr);
            }
        }

        /// <summary>
        /// Warehouse-level free stock for one product: QtyOnHand - QtyReserved -
        /// QtyDedicated - QtyAllocated, summed across the warehouse's active
        /// locators (M_Storage joined to M_Locator). A product/warehouse with no
        /// M_Storage rows reads as zero, never null.
        /// </summary>
        private decimal ResolveFreeStock(Ctx ctx, int productId, int warehouseId)
        {
            if (productId <= 0 || warehouseId <= 0) { return 0m; }

            string sql = @"
                SELECT COALESCE(SUM(COALESCE(s.QtyOnHand, 0) - COALESCE(s.QtyReserved, 0)
                              - COALESCE(s.QtyDedicated, 0) - COALESCE(s.QtyAllocated, 0)), 0) AS Free_Stock
                  FROM M_Storage s
                  INNER JOIN M_Locator loc ON ( loc.M_Locator_ID = s.M_Locator_ID )
                 WHERE s.M_Product_ID = @M_Product_ID
                   AND loc.M_Warehouse_ID = @M_Warehouse_ID
                   AND loc.IsActive = 'Y'";

            // "s" (M_Storage) is the main physical table being fetched from here -
            // M_Locator is only joined to scope by warehouse, so AddAccessSQL applies
            // to "s" per the same primary-vs-secondary-alias rule as everywhere else
            // in this codebase (Prompt_Instructions "Case 1").
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "s", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            return Util.GetValueOfDecimal(DB.ExecuteScalar(sql, new[]
            {
                new SqlParameter("@M_Product_ID", productId),
                new SqlParameter("@M_Warehouse_ID", warehouseId)
            }, null));
        }

        /// <summary>Escapes the LIKE metacharacters (and the escape character itself) so user text is matched literally under an explicit ESCAPE '\' clause.</summary>
        private static string EscapeLike(string text)
        {
            if (string.IsNullOrEmpty(text)) { return text; }
            return text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
        }

        /// <summary>The tenant's own label for a stored code, or the code itself when the dictionary carries no entry (never invented English).</summary>
        private static string DecodeLabel(Dictionary<string, string> map, string code)
        {
            if (string.IsNullOrEmpty(code)) { return ""; }
            string label;
            return map != null && map.TryGetValue(code, out label) ? label : code;
        }

        /// <summary>
        /// AD_Ref_List(_Trl) Code -&gt; Label map for one column, cached per
        /// column+language - never a hard-coded code table (Prompt_Instructions
        /// "Fetch Reference List Values for a Column").
        /// </summary>
        private static readonly ConcurrentDictionary<string, Dictionary<string, string>> DecodeMapCache =
            new ConcurrentDictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        private Dictionary<string, string> GetDecodeMap(string tableName, string columnName, string language)
        {
            string cacheKey = tableName + "." + columnName + "|" + language;

            Dictionary<string, string> cached;
            if (DecodeMapCache.TryGetValue(cacheKey, out cached)) { return cached; }

            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int columnId = GetColumnId(tableName, columnName);

            if (columnId > 0)
            {
                // Predicate order matches the parameter array order below
                // (AD_Language first, AD_Column_ID second) so the two line up
                // positionally as well as by name - defensive, in case the
                // underlying DB driver's bind resolution is ever positional.
                string sql = @"
                    SELECT rl.Value AS Code, COALESCE(rlt.Name, rl.Name, rl.Value) AS Label
                      FROM AD_Ref_List rl
                      INNER JOIN AD_Column col ON ( col.AD_Reference_Value_ID = rl.AD_Reference_ID )
                      LEFT OUTER JOIN AD_Ref_List_Trl rlt ON ( rlt.AD_Ref_List_ID = rl.AD_Ref_List_ID AND rlt.AD_Language = @AD_Language )
                     WHERE col.AD_Column_ID = @AD_Column_ID
                       AND rl.IsActive = 'Y'
                       AND col.IsActive = 'Y'
                     ORDER BY rl.Value";

                IDataReader dr = null;
                try
                {
                    dr = DB.ExecuteReader(sql, new[]
                    {
                        new SqlParameter("@AD_Language", SqlDbType.NVarChar) { Value = language },
                        new SqlParameter("@AD_Column_ID", SqlDbType.Int) { Value = columnId }
                    });
                    while (dr != null && dr.Read())
                    {
                        string code = Util.GetValueOfString(dr["Code"]);
                        if (!string.IsNullOrEmpty(code))
                        {
                            map[code] = Util.GetValueOfString(dr["Label"]);
                        }
                    }
                }
                catch (Exception ex)
                {
                    // A decode-map lookup must never take the whole endpoint down -
                    // worst case the raw stored code is shown instead of its label
                    // (DecodeLabel's own fallback), which is far better than a
                    // failed search. See the class log for the underlying cause.
                    Log.Log(Level.SEVERE, "VAS_268_GlobalSalesSearchWidget.GetDecodeMap(" + tableName + "." + columnName + ")", ex);
                }
                finally
                {
                    CloseReader(dr);
                }
            }

            DecodeMapCache[cacheKey] = map;
            return map;
        }

        private static readonly ConcurrentDictionary<string, int> ColumnIdCache = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private static int GetColumnId(string tableName, string columnName)
        {
            string cacheKey = tableName + "." + columnName;

            int cached;
            if (ColumnIdCache.TryGetValue(cacheKey, out cached)) { return cached; }

            int columnId = Util.GetValueOfInt(DB.ExecuteScalar(
                "SELECT AD_Column_ID FROM AD_Column WHERE ColumnName = @Col AND AD_Table_ID = (SELECT AD_Table_ID FROM AD_Table WHERE TableName = @Tbl)",
                new[]
                {
                    new SqlParameter("@Col", SqlDbType.NVarChar) { Value = columnName },
                    new SqlParameter("@Tbl", SqlDbType.NVarChar) { Value = tableName }
                },
                null));

            ColumnIdCache[cacheKey] = columnId;
            return columnId;
        }

        private string GetLanguage(Ctx ctx)
        {
            string language = ctx == null ? string.Empty : ctx.GetAD_Language();
            return string.IsNullOrWhiteSpace(language) ? "en_US" : language;
        }

        private void CloseReader(IDataReader reader)
        {
            if (reader == null) { return; }
            reader.Close();
            reader.Dispose();
        }

        private JsonResult ErrorResult(Ctx ctx)
        {
            string message = Msg.GetMsg(ctx, "Error") ?? "Error";
            string json = JsonConvert.SerializeObject(new { Error = message });
            return Json(json, JsonRequestBehavior.AllowGet);
        }

        private class SearchResult
        {
            public List<SearchRow> Rows { get; set; }
            public int Total { get; set; }
        }

        private class SearchRow
        {
            public string Category { get; set; }
            public string MatchedText { get; set; }
            public int SalesOrderId { get; set; }
            public string SalesOrderNumber { get; set; }
            public string SalesOrderDate { get; set; }
            public string CustomerName { get; set; }
            public string DocumentStatus { get; set; }
            public decimal OrderValue { get; set; }
        }

        private class RecentRow
        {
            public int SalesOrderId { get; set; }
            public string SalesOrderNumber { get; set; }
            public string SalesOrderDate { get; set; }
            public string CustomerName { get; set; }
            public string DocumentStatus { get; set; }
            public decimal OrderValue { get; set; }
        }

        private class OrderDetailResult
        {
            public OrderHeader Order { get; set; }
            public List<OrderLineRow> Lines { get; set; }
        }

        private class OrderHeader
        {
            public int SalesOrderId { get; set; }
            public string SalesOrderNumber { get; set; }
            public string SalesOrderDate { get; set; }
            public string DatePromised { get; set; }
            public string CustomerName { get; set; }
            public string DocumentStatus { get; set; }
            public string DocumentStatusCode { get; set; }
            public decimal OrderValue { get; set; }
            public string WarehouseName { get; set; }
            public string DeliveryMode { get; set; }
            public string DeliveryStatus { get; set; }
        }

        private class OrderLineRow
        {
            public int LineNo { get; set; }
            public string ProductName { get; set; }
            public string Description { get; set; }
            public string AttributeText { get; set; }
            public string UomName { get; set; }
            public decimal QtyOrdered { get; set; }
            public decimal QtyDelivered { get; set; }
            public decimal QtyPending { get; set; }
            public decimal FreeStock { get; set; }
            public decimal Rate { get; set; }
            public decimal Amount { get; set; }
        }
    }
}
