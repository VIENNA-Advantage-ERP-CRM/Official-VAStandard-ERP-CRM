/******************************************************
 * Module Name    : VAS
 * Purpose        : Sales Order dashboard "Drafted / In-Process SOs" KPI widget endpoints
 * chronological  : Development
 * Created Date   : 2026-09-14
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
    /// Module Name : VAS_272_DraftedInProcessSOsWidget
    /// Purpose     : Data endpoints for the 3x1 "Drafted / In-Process SOs" KPI tile on
    ///               the Sales Order dashboard. (1) the tile/summary figures - total,
    ///               drafted and in-process counts plus pre-tax total value, all-time
    ///               (not month-scoped), (2) the drill-down modal's paginated documents
    ///               list (newest first) with a stage chip, and (3) the shared Sales
    ///               Order record-preview data (header stats + paginated lines with
    ///               per-line free stock) reused for both the "record" and "lines" child
    ///               modals.
    ///
    ///   Business definition per 05_Drafted_In_Process_SOs_Claude_Development_Prompt.txt:
    ///     - Cohort: IsSOTrx='Y', not a return, not a quotation, active, and
    ///       DocStatus IN ('DR','IP') - Drafted and In Process are confirmed directly
    ///       from C_Order.DocStatus, no derived approval-state table. These two
    ///       document states are pipeline work, not yet a firm commitment, and must
    ///       never appear in any other Sales Order KPI on this dashboard (booked,
    ///       pending delivery, completed).
    ///     - Value = SUM(C_Order.TotalLines), pre-tax, never GrandTotal.
    ///     - Line count in the modal list is ACTIVE lines only.
    ///     - Stage display text ("Drafted" / "In process") never shows the raw DR/IP
    ///       code to the user.
    ///
    ///   Per the CTE/MRole rule (Prompt_Instructions.txt "Case 1"), AddAccessSQL is
    ///   applied ONLY to the isolated "base_orders" CTE body (BuildBaseOrdersSql) - a
    ///   single SELECT against the one main physical table (C_Order "o") with its own
    ///   single WHERE clause - before it is embedded into the combined statement used by
    ///   the modal list (which also has a secondary "line_count" CTE over
    ///   C_OrderLine). The plain aggregate summary query has only one FROM/WHERE to
    ///   begin with, so the same base_orders fragment is reused there directly with no
    ///   further CTEs. AddAccessSQL is never applied to either combined statement itself
    ///   (mirroring the fix already applied to VAS_270/271 after VAS_270 hit ORA-00904
    ///   from applying it to a multi-FROM-clause statement).
    ///   Read-only here; no writes.
    /// Chronological development:
    ///   VAI052      2026-09-14 Created
    /// </summary>
    [System.Web.Mvc.SessionState(System.Web.SessionState.SessionStateBehavior.ReadOnly)]
    public class VAS_272_DraftedInProcessSOsWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_272_DraftedInProcessSOsWidgetController).FullName);

        private const int DefaultPageSize = 10;
        private const int MaxPageSize = 10;
        private const int MaxLineRows = 200;

        /// <summary>
        /// All-time summary figures for the tile and the modal's stat strip.
        /// </summary>
        /// <returns>JSON SummaryResult or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetSummary()
        {
            if (Session["ctx"] == null) { return Json("", JsonRequestBehavior.AllowGet); }

            Ctx ctx = Session["ctx"] as Ctx;

            try
            {
                SummaryResult data = GetSummaryData(ctx);
                FillBaseCurrency(ctx, data);
                string json = JsonConvert.SerializeObject(data);
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_272_DraftedInProcessSOsWidget.GetSummary", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// One page of the underlying drafted/in-process sales orders for the modal's
        /// table, newest first.
        /// </summary>
        /// <param name="page">Zero-based page index.</param>
        /// <param name="size">Rows per page (capped at <see cref="MaxPageSize"/>).</param>
        /// <returns>JSON { Total, Rows:[...] } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetOrders(int page = 0, int size = DefaultPageSize)
        {
            if (Session["ctx"] == null) { return Json("", JsonRequestBehavior.AllowGet); }

            Ctx ctx = Session["ctx"] as Ctx;
            if (page < 0) { page = 0; }
            if (size <= 0 || size > MaxPageSize) { size = DefaultPageSize; }

            try
            {
                string json = JsonConvert.SerializeObject(GetOrdersData(ctx, page, size));
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_272_DraftedInProcessSOsWidget.GetOrders", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// Full data for the shared Sales Order record-preview modal (record view and
        /// lines view both render from this one payload): header stats and every active
        /// line (capped at <see cref="MaxLineRows"/> - the client paginates client-side,
        /// sized to the space actually available, so the modal body never scrolls).
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
                Log.Log(Level.SEVERE, "VAS_272_DraftedInProcessSOsWidget.GetSalesOrderDetail", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// The base_orders CTE body - the ONLY fragment AddAccessSQL is ever applied to
        /// (Prompt_Instructions.txt "Case 1"): a single SELECT against the one main
        /// physical table (C_Order "o") with its own single WHERE clause, scoped to the
        /// Drafted/In-Process cohort. GetSummaryData/GetOrdersData each embed this
        /// ALREADY-ACCESS-FILTERED text and must never call AddAccessSQL again on a
        /// combined multi-FROM statement built around it.
        /// </summary>
        private static string BuildBaseOrdersSql()
        {
            // VAS163 2026-11-11: Row 30 the document-type filter is now exactly the Sales Order
            // window's Order-tab where clause (AD_Tab VAS_1000695 of window VAS_SalesOrder):
            //   C_Order.IsSOTrx='Y' AND C_Order.IsReturnTrx='N' AND C_Order.C_DocTypeTarget_ID IN
            //   (SELECT dt.C_DocType_ID FROM C_DocType dt WHERE dt.DocBaseType IN ('SOO','RSO')
            //    AND dt.DocSubTypeSO NOT IN ('OB','ON') AND dt.IsReturnTrx='N' AND dt.IsBlanketTrx='N'
            //    AND dt.AD_Client_ID = <login client>)
            // The previous filter (DocSubTypeSO IN ('SO','WR','BO'), no DocBaseType / IsBlanketTrx /
            // doc-type IsReturnTrx test) counted e.g. blanket-order doc types the window hides, so
            // the widget showed more Drafted SOs than the window. IsActive and role access are kept.
            // Rows 34/35: tax-inclusive flag from the order or its price list; accounting date,
            // conversion type, client and org carried for the base-currency Value card.
            return @"
                SELECT o.C_Order_ID AS C_Order_ID,
                       o.DocumentNo AS Document_No,
                       o.DateOrdered AS Date_Ordered,
                       o.C_BPartner_ID AS Bpartner_Id,
                       o.SalesRep_ID AS Sales_Rep_Id,
                       " + OrderValueExclTaxSql + @" AS Order_Value,
                       o.C_Currency_ID AS Currency_Id,
                       COALESCE(o.DateAcct, o.DateOrdered) AS Date_Acct,
                       o.C_ConversionType_ID AS Conversion_Type_Id,
                       o.AD_Client_ID AS Client_Id,
                       o.AD_Org_ID AS Org_Id,
                       o.DocStatus AS Doc_Status_Code
                  FROM C_Order o
                 WHERE o.AD_Client_ID = @AD_Client_ID
                   AND o.IsActive = 'Y'
                   AND o.IsSOTrx = 'Y'
                   AND o.IsReturnTrx = 'N'
                   AND EXISTS (SELECT 1 FROM C_DocType dt WHERE dt.C_DocType_ID = o.C_DocTypeTarget_ID
                                  AND dt.DocBaseType IN ('SOO', 'RSO')
                                  AND dt.DocSubTypeSO NOT IN ('OB', 'ON')
                                  AND dt.IsReturnTrx = 'N'
                                  AND dt.IsBlanketTrx = 'N'
                                  AND dt.AD_Client_ID = o.AD_Client_ID)
                   AND o.DocStatus IN ('DR', 'IP')";
        }

        /// <summary>
        /// VAS163 2026-11-11: Row 34 'Y' when the order's prices include tax - the order's own
        /// IsTaxIncluded flag or its price list's flag (same expression as VAS_270).
        /// </summary>
        private const string TaxIncludedSql =
            "(CASE WHEN COALESCE(o.IsTaxIncluded, 'N') = 'Y' " +
            "OR COALESCE((SELECT pl.IsTaxIncluded FROM M_PriceList pl WHERE pl.M_PriceList_ID = o.M_PriceList_ID), 'N') = 'Y' " +
            "THEN 'Y' ELSE 'N' END)";

        /// <summary>
        /// SO value WITHOUT taxes: on a tax-inclusive order C_Order.TotalLines holds the
        /// gross, so the order's extracted tax is taken off. VAS163 2026-11-11: Row 34 the
        /// tax now comes from the order's tax summary (C_OrderTax.TaxAmt) - same expression
        /// as VAS_270 - instead of C_OrderLine.TaxAmt, which is not maintained on every line.
        /// Scalar sub-selects in the SELECT list leave the main WHERE untouched for AddAccessSQL.
        /// </summary>
        private const string OrderValueExclTaxSql =
            "CASE WHEN " + TaxIncludedSql + " = 'Y' " +
            "THEN COALESCE(o.TotalLines, 0) - (SELECT COALESCE(SUM(COALESCE(ot.TaxAmt, 0)), 0) FROM C_OrderTax ot WHERE ot.C_Order_ID = o.C_Order_ID AND ot.IsActive = 'Y') " +
            "ELSE COALESCE(o.TotalLines, 0) END";

        private SummaryResult GetSummaryData(Ctx ctx)
        {
            SummaryResult result = new SummaryResult();
            if (ctx == null) { return result; }

            // The only AddAccessSQL call for this statement - applied to the isolated
            // single-WHERE base_orders body BEFORE it is embedded below.
            string baseOrdersSql = MRole.GetDefault(ctx).AddAccessSQL(BuildBaseOrdersSql(), "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            // VAS163 2026-11-11: Row 35 the Value card is always in the base (accounting-schema)
            // currency: an order in another currency is converted with the configured rates
            // (currencyConvert, same argument order as VAS_020) before summing. The id is an int
            // read from the DB, inlined like VAS_020.
            int baseCurrencyId = GetBaseCurrencyId(ctx);
            string totalValueSql = baseCurrencyId > 0
                ? "ROUND(SUM(CASE WHEN Currency_Id = " + baseCurrencyId + " THEN COALESCE(Order_Value, 0) " +
                  "ELSE COALESCE(currencyConvert(COALESCE(Order_Value, 0), Currency_Id, " + baseCurrencyId + ", Date_Acct, Conversion_Type_Id, Client_Id, Org_Id), 0) END), 6)"
                : "SUM(COALESCE(Order_Value, 0))";

            string sql = @"
                WITH base_orders AS (" + baseOrdersSql + @")
                SELECT
                    COUNT(1) AS Total_Count,
                    SUM(CASE WHEN Doc_Status_Code = 'DR' THEN 1 ELSE 0 END) AS Drafted_Count,
                    SUM(CASE WHEN Doc_Status_Code = 'IP' THEN 1 ELSE 0 END) AS In_Process_Count,
                    " + totalValueSql + @" AS Total_Value
                  FROM base_orders";

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, new[]
                {
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
                });

                if (dr != null && dr.Read())
                {
                    result.TotalCount = dr["Total_Count"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Total_Count"]);
                    result.DraftedCount = dr["Drafted_Count"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Drafted_Count"]);
                    result.InProcessCount = dr["In_Process_Count"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["In_Process_Count"]);
                    result.TotalValue = dr["Total_Value"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Total_Value"]);
                }
            }
            finally
            {
                CloseReader(dr);
            }

            return result;
        }

        private OrdersResult GetOrdersData(Ctx ctx, int page, int size)
        {
            OrdersResult result = new OrdersResult { Rows = new List<OrderRow>() };
            if (ctx == null) { return result; }

            string baseOrdersSql = MRole.GetDefault(ctx).AddAccessSQL(BuildBaseOrdersSql(), "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string sql = @"
                WITH base_orders AS (" + baseOrdersSql + @"),
                line_count AS (
                    SELECT
                        ol.C_Order_ID AS C_Order_ID,
                        COUNT(1) AS Line_Count
                      FROM C_OrderLine ol
                     WHERE ol.IsActive = 'Y'
                     GROUP BY ol.C_Order_ID
                )
                SELECT
                    bo.C_Order_ID AS Order_Id,
                    bo.Document_No AS Document_No,
                    bo.Date_Ordered AS Date_Ordered,
                    COALESCE(bp.Name, N'') AS Customer_Name,
                    COALESCE(rep.Name, N'') AS Representative_Name,
                    COALESCE(lc.Line_Count, 0) AS Line_Count,
                    bo.Order_Value AS Order_Value,
                    curr.CurSymbol AS Currency_Symbol,
                    curr.ISO_Code AS Currency_Code,
                    curr.StdPrecision AS Currency_Precision,
                    bo.Doc_Status_Code AS Doc_Status_Code,
                    COUNT(1) OVER () AS Total_Rows
                  FROM base_orders bo
                  INNER JOIN C_BPartner bp ON ( bp.C_BPartner_ID = bo.Bpartner_Id )
                  LEFT OUTER JOIN C_Currency curr ON ( curr.C_Currency_ID = bo.Currency_Id )
                  LEFT OUTER JOIN AD_User rep ON ( rep.AD_User_ID = bo.Sales_Rep_Id )
                  LEFT OUTER JOIN line_count lc ON ( lc.C_Order_ID = bo.C_Order_ID )
                 ORDER BY bo.Date_Ordered DESC, bo.Document_No DESC
                 OFFSET @Row_Offset ROWS FETCH NEXT @Page_Size ROWS ONLY";

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, new[]
                {
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                    new SqlParameter("@Row_Offset", page * size),
                    new SqlParameter("@Page_Size", size)
                });

                int total = 0;
                while (dr != null && dr.Read())
                {
                    total = dr["Total_Rows"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Total_Rows"]);
                    DateTime? dateOrdered = dr["Date_Ordered"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr["Date_Ordered"]);
                    string docStatusCode = Util.GetValueOfString(dr["Doc_Status_Code"]);

                    string stageKey, stageFallback;
                    if (docStatusCode == "IP")
                    {
                        // Sr 8.22: badge reads "In Progress" (new key; old VAS_272_StageInProcess retired).
                        stageKey = "VAS_272_StageInProgress"; stageFallback = "In Progress";
                    }
                    else
                    {
                        stageKey = "VAS_272_StageDrafted"; stageFallback = "Drafted";
                    }

                    result.Rows.Add(new OrderRow
                    {
                        SalesOrderId = Util.GetValueOfInt(dr["Order_Id"]),
                        SalesOrderNumber = Util.GetValueOfString(dr["Document_No"]),
                        SalesOrderDate = dateOrdered.HasValue ? dateOrdered.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                        CustomerName = Util.GetValueOfString(dr["Customer_Name"]),
                        RepresentativeName = Util.GetValueOfString(dr["Representative_Name"]),
                        LineCount = dr["Line_Count"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Line_Count"]),
                        OrderValue = dr["Order_Value"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Order_Value"]),
                        CurrencySymbol = CurrencyLabel(dr),
                        CurrencyPrecision = dr["Currency_Precision"] == DBNull.Value ? 2 : Util.GetValueOfInt(dr["Currency_Precision"]),
                        StageCode = docStatusCode,
                        // VAS163 2026-11-11: Row 81 - text only (the 2-arg overload appends the
                        // message tip, which rendered "In Progress In Progress").
                        Stage = Msg.GetMsg(ctx, stageKey, true) ?? stageFallback
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
                       " + OrderValueExclTaxSql + @" AS Order_Value,
                       " + TaxIncludedSql + @" AS Price_List_Tax_Included,
                       curr.CurSymbol AS Currency_Symbol,
                       curr.ISO_Code AS Currency_Code,
                       curr.StdPrecision AS Currency_Precision,
                       o.DeliveryViaRule AS Delivery_Via_Rule,
                       o.M_Warehouse_ID AS Warehouse_Id,
                       COALESCE(bp.Name, N'') AS Customer_Name,
                       COALESCE(w.Name, N'') AS Warehouse_Name
                  FROM C_Order o
                  INNER JOIN C_BPartner bp ON ( bp.C_BPartner_ID = o.C_BPartner_ID )
                  LEFT OUTER JOIN M_Warehouse w ON ( w.M_Warehouse_ID = o.M_Warehouse_ID )
                  LEFT OUTER JOIN C_Currency curr ON ( curr.C_Currency_ID = o.C_Currency_ID )
                 WHERE o.C_Order_ID = @Order_Id
                   AND o.AD_Client_ID = @AD_Client_ID
                   AND o.IsActive = 'Y'";

            headerSql = MRole.GetDefault(ctx).AddAccessSQL(headerSql, "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string docStatusCode = "";
            int warehouseId = 0;
            bool priceListTaxIncluded = false;
            int stdPrecision = 2;

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
                warehouseId = dr["Warehouse_Id"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Warehouse_Id"]);
                priceListTaxIncluded = Util.GetValueOfString(dr["Price_List_Tax_Included"]) == "Y";
                stdPrecision = dr["Currency_Precision"] == DBNull.Value ? 2 : Util.GetValueOfInt(dr["Currency_Precision"]);
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
                    CurrencySymbol = CurrencyLabel(dr),
                    CurrencyPrecision = stdPrecision,
                    WarehouseName = Util.GetValueOfString(dr["Warehouse_Name"]),
                    DeliveryMode = DecodeLabel(GetDecodeMap("C_Order", "DeliveryViaRule", language), Util.GetValueOfString(dr["Delivery_Via_Rule"]))
                };
            }
            finally
            {
                CloseReader(dr);
            }

            if (result.Order == null) { return result; }

            LoadOrderLines(ctx, result, orderId, warehouseId, priceListTaxIncluded, stdPrecision, language);

            // Delivery state counts Item-type product lines only (Sr 8.25).
            bool anyDelivered = false, allDelivered = true;
            bool hasLines = false;
            foreach (OrderLineRow line in result.Lines)
            {
                if (!line.IsItem) { continue; }
                hasLines = true;
                if (line.QtyDelivered > 0) { anyDelivered = true; }
                if (line.QtyDelivered < line.QtyOrdered) { allDelivered = false; }
            }

            // Sr 8.27: a Drafted order has no delivery status - shown blank, never the
            // text "Not applicable".
            if (docStatusCode == "DR")
            {
                result.Order.DeliveryStatus = "";
                return result;
            }

            string deliveryStatusKey, deliveryStatusFallback;
            if (hasLines && allDelivered)
            {
                deliveryStatusKey = "VAS_272_DeliveryFull"; deliveryStatusFallback = "Delivered";
            }
            else if (anyDelivered)
            {
                deliveryStatusKey = "VAS_272_DeliveryPartial"; deliveryStatusFallback = "Partially delivered";
            }
            else
            {
                deliveryStatusKey = "VAS_272_DeliveryNone"; deliveryStatusFallback = "Not delivered";
            }
            result.Order.DeliveryStatus = Msg.GetMsg(ctx, deliveryStatusKey, true) ?? deliveryStatusFallback; // VAS163 2026-11-11: text-only label (3-arg GetMsg, no tip appended)

            return result;
        }

        /// <summary>
        /// Every active line of the order, each carrying its own free-stock figure
        /// (per <see cref="ResolveOnHand"/>) and a derived line-status chip. Unlike
        /// VAS_270/271, this widget's cohort (Drafted/In Process) can genuinely include
        /// undelivered-by-definition orders, so the Drafted branch is kept here.
        /// </summary>
        private void LoadOrderLines(Ctx ctx, OrderDetailResult result, int orderId, int headerWarehouseId, bool priceListTaxIncluded, int stdPrecision, string language)
        {
            // ProductType is a CHAR column and the charge / product / UOM names are
            // NVARCHAR - all selected raw and resolved in C# (no COALESCE against a
            // literal, see VAS_203). Sr 8.24: the UOM is shown by its full (translated)
            // name - C_UOM_Trl.Name, then C_UOM.Name - and only falls back to the
            // symbol ("Ea") when no name is maintained.
            string sql = @"
                SELECT ol.C_OrderLine_ID AS Line_Id,
                       ol.Line AS Line_No,
                       ol.M_Product_ID AS Product_Id,
                       ol.C_Charge_ID AS Charge_Id,
                       ol.Description AS Description,
                       ol.QtyOrdered AS Qty_Ordered,
                       ol.QtyEntered AS Qty_Entered,
                       ol.QtyDelivered AS Qty_Delivered,
                       ol.PriceActual AS Price_Actual,
                       ol.PriceEntered AS Price_Entered,
                       ol.LineNetAmt AS Line_Net_Amt,
                       ol.TaxAmt AS Line_Tax_Amt,
                       ol.C_Tax_ID AS Line_Tax_Id,
                       tx.Rate AS Tax_Rate,
                       ol.M_AttributeSetInstance_ID AS Asi_Id,
                       ol.C_UOM_ID AS Uom_Id,
                       ol.M_Warehouse_ID AS Line_Warehouse_Id,
                       COALESCE(p.Name, p.Value) AS Product_Name,
                       p.ProductType AS Product_Type,
                       ch.Name AS Charge_Name,
                       COALESCE(asi.Description, N'') AS Attribute_Text,
                       ut.Name AS Uom_Name_Trl,
                       u.Name AS Uom_Name,
                       u.UOMSymbol AS Uom_Symbol,
                       butrl.Name AS Base_Uom_Name_Trl,
                       bu.Name AS Base_Uom_Name,
                       bu.UOMSymbol AS Base_Uom_Symbol
                  FROM C_OrderLine ol
                  LEFT OUTER JOIN M_Product p ON ( p.M_Product_ID = ol.M_Product_ID )
                  LEFT OUTER JOIN C_Charge ch ON ( ch.C_Charge_ID = ol.C_Charge_ID )
                  LEFT OUTER JOIN C_Tax tx ON ( tx.C_Tax_ID = ol.C_Tax_ID )
                  LEFT OUTER JOIN M_AttributeSetInstance asi ON ( asi.M_AttributeSetInstance_ID = ol.M_AttributeSetInstance_ID )
                  LEFT OUTER JOIN C_UOM u ON ( u.C_UOM_ID = ol.C_UOM_ID )
                  LEFT OUTER JOIN C_UOM_Trl ut ON ( ut.C_UOM_ID = ol.C_UOM_ID AND ut.AD_Language = @AD_Language )
                  LEFT OUTER JOIN C_UOM bu ON ( bu.C_UOM_ID = p.C_UOM_ID )
                  LEFT OUTER JOIN C_UOM_Trl butrl ON ( butrl.C_UOM_ID = p.C_UOM_ID AND butrl.AD_Language = @AD_Language_Base )
                 WHERE ol.C_Order_ID = @Order_Id
                   AND ol.IsActive = 'Y'
                 ORDER BY ol.Line ASC
                 OFFSET 0 ROWS FETCH NEXT @Max_Line_Rows ROWS ONLY";

            // VAS163 2026-11-11: Row 34 on a tax-inclusive order each line is restated net of tax
            // with the order's own tax summary (C_OrderTax), as VAS_270 does.
            Dictionary<int, decimal> netRatioByTax = priceListTaxIncluded ? GetOrderTaxNetRatios(orderId) : new Dictionary<int, decimal>();

            IDataReader dr = null;
            try
            {
                // Parameter order follows textual order (@AD_Language, @AD_Language_Base, @Order_Id,
                // @Max_Line_Rows) for drivers that bind positionally.
                // VAS163 2026-11-11: Row 33 @AD_Language_Base for the product's base UOM name.
                dr = DB.ExecuteReader(sql, new[]
                {
                    new SqlParameter("@AD_Language", SqlDbType.NVarChar) { Value = language },
                    new SqlParameter("@AD_Language_Base", SqlDbType.NVarChar) { Value = language },
                    new SqlParameter("@Order_Id", orderId),
                    new SqlParameter("@Max_Line_Rows", MaxLineRows)
                });

                while (dr != null && dr.Read())
                {
                    decimal qtyOrderedBase = dr["Qty_Ordered"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Qty_Ordered"]);
                    decimal qtyEntered = dr["Qty_Entered"] == DBNull.Value ? qtyOrderedBase : Util.GetValueOfDecimal(dr["Qty_Entered"]);
                    decimal qtyDeliveredBase = dr["Qty_Delivered"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Qty_Delivered"]);
                    int productId = dr["Product_Id"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Product_Id"]);
                    int lineWarehouseId = dr["Line_Warehouse_Id"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Line_Warehouse_Id"]);
                    int warehouseId = lineWarehouseId > 0 ? lineWarehouseId : headerWarehouseId;

                    // Quantities in the line's selected UOM (QtyEntered; delivered scaled by
                    // the line's entered/ordered ratio - same approach as VAS_203 / VAS_207).
                    decimal uomRatio = qtyOrderedBase != 0m ? (qtyEntered / qtyOrderedBase) : 1m;
                    decimal qtyOrdered = qtyEntered;
                    decimal qtyDelivered = qtyDeliveredBase * uomRatio;

                    // Sr 8.25: pending / delivered / stock only for Item-type products.
                    bool isItem = productId > 0
                        && string.Equals((Util.GetValueOfString(dr["Product_Type"]) ?? "").Trim(), "I", StringComparison.OrdinalIgnoreCase);

                    // A charge line shows the charge's name instead of a blank product.
                    string productName = Util.GetValueOfString(dr["Product_Name"]);
                    if (string.IsNullOrEmpty(productName)) { productName = Util.GetValueOfString(dr["Charge_Name"]); }
                    if (string.IsNullOrEmpty(productName)) { productName = Util.GetValueOfString(dr["Description"]); }

                    string uomName = Util.GetValueOfString(dr["Uom_Name_Trl"]);
                    if (string.IsNullOrWhiteSpace(uomName)) { uomName = Util.GetValueOfString(dr["Uom_Name"]); }
                    if (string.IsNullOrWhiteSpace(uomName)) { uomName = Util.GetValueOfString(dr["Uom_Symbol"]); }

                    // VAS163 2026-11-11: Row 33 the product's base (master) UOM name, same
                    // translated-name / name / symbol fallback as the line UOM.
                    string baseUomName = Util.GetValueOfString(dr["Base_Uom_Name_Trl"]);
                    if (string.IsNullOrWhiteSpace(baseUomName)) { baseUomName = Util.GetValueOfString(dr["Base_Uom_Name"]); }
                    if (string.IsNullOrWhiteSpace(baseUomName)) { baseUomName = Util.GetValueOfString(dr["Base_Uom_Symbol"]); }

                    // Line amount without tax. VAS163 2026-11-11: Row 34 on a tax-inclusive order
                    // LineNetAmt is the gross: scaled by its tax's net/gross ratio from C_OrderTax
                    // (so the lines add up to the header SO value); only when the order has no tax
                    // summary for that tax is the line's own TaxAmt (or rate-derived tax) taken off.
                    decimal lineGross = dr["Line_Net_Amt"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Line_Net_Amt"]);
                    decimal lineAmt = lineGross;
                    int lineTaxId = dr["Line_Tax_Id"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Line_Tax_Id"]);
                    if (priceListTaxIncluded && netRatioByTax.ContainsKey(lineTaxId))
                    {
                        lineAmt = Math.Round(lineGross * netRatioByTax[lineTaxId], stdPrecision, MidpointRounding.AwayFromZero);
                    }
                    else if (priceListTaxIncluded)
                    {
                        decimal lineTax = dr["Line_Tax_Amt"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Line_Tax_Amt"]);
                        decimal taxRate = dr["Tax_Rate"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Tax_Rate"]);
                        if (lineTax == 0m && taxRate > 0m)
                        {
                            lineTax = Math.Round(lineGross * taxRate / (100m + taxRate), stdPrecision, MidpointRounding.AwayFromZero);
                        }
                        lineAmt = lineGross - lineTax;
                    }

                    // The rate per selected UOM (PriceEntered), falling back to PriceActual.
                    decimal rate = dr["Price_Entered"] == DBNull.Value
                        ? (dr["Price_Actual"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Price_Actual"]))
                        : Util.GetValueOfDecimal(dr["Price_Entered"]);
                    // VAS163 2026-11-11: Row 34 the tax-inclusive price is restated net with the same proportion.
                    if (priceListTaxIncluded && lineGross != 0m)
                    {
                        rate = Math.Round(rate * lineAmt / lineGross, 6, MidpointRounding.AwayFromZero);
                    }

                    // VAS163 2026-11-11: Rows 33/58 In stock is ALWAYS in the product's base UOM
                    // (never scaled to the line's UOM) and never negative (0 when the warehouse has
                    // no stock). On-hand, not free stock (its reservation already holds this order).
                    // The shortage is judged in the line's UOM.
                    decimal onHandBase = isItem ? Math.Max(0m, ResolveOnHand(ctx, productId, warehouseId)) : 0m;
                    decimal qtyPending = isItem ? Math.Max(0m, qtyOrdered - qtyDelivered) : 0m;

                    result.Lines.Add(new OrderLineRow
                    {
                        LineNo = Util.GetValueOfInt(dr["Line_No"]),
                        ProductName = productName,
                        AttributeText = Util.GetValueOfString(dr["Attribute_Text"]),
                        UomName = (uomName ?? "").Trim(),
                        BaseUomName = (baseUomName ?? "").Trim(),
                        IsItem = isItem,
                        QtyOrdered = qtyOrdered,
                        QtyDelivered = qtyDelivered,
                        QtyPending = qtyPending,
                        FreeStock = onHandBase,
                        QtyShort = isItem ? Math.Max(0m, qtyPending - onHandBase * uomRatio) : 0m,
                        Rate = rate,
                        Amount = lineAmt
                    });
                }
            }
            finally
            {
                CloseReader(dr);
            }
        }

        /// <summary>
        /// VAS163 2026-11-11: Row 34 (copied from VAS_270, 5-Nov Row 45). Tax-inclusive order
        /// only: per C_Tax_ID, the share of the lines' gross (SUM(LineNetAmt)) that is NOT tax,
        /// taken from the order's own tax summary (C_OrderTax.TaxAmt) - the same tax the header
        /// SO value takes off. A tax with no (or an implausible) summary row is left out and the
        /// caller falls back to the line's own TaxAmt / rate.
        /// </summary>
        private static Dictionary<int, decimal> GetOrderTaxNetRatios(int orderId)
        {
            Dictionary<int, decimal> ratios = new Dictionary<int, decimal>();
            try
            {
                Dictionary<int, decimal> grossByTax = new Dictionary<int, decimal>();
                DataSet grossDs = DB.ExecuteDataset(@"
                    SELECT ol.C_Tax_ID AS Tax_Id, COALESCE(SUM(ol.LineNetAmt), 0) AS Gross
                      FROM C_OrderLine ol
                     WHERE ol.C_Order_ID = @Order_Id
                       AND ol.IsActive = 'Y'
                     GROUP BY ol.C_Tax_ID", new[] { new SqlParameter("@Order_Id", orderId) }, null);
                if (grossDs != null && grossDs.Tables.Count > 0)
                {
                    foreach (DataRow row in grossDs.Tables[0].Rows)
                    {
                        grossByTax[Util.GetValueOfInt(row["Tax_Id"])] = Util.GetValueOfDecimal(row["Gross"]);
                    }
                }

                DataSet taxDs = DB.ExecuteDataset(@"
                    SELECT ot.C_Tax_ID AS Tax_Id, COALESCE(SUM(ot.TaxAmt), 0) AS Tax_Amt
                      FROM C_OrderTax ot
                     WHERE ot.C_Order_ID = @Order_Id
                       AND ot.IsActive = 'Y'
                     GROUP BY ot.C_Tax_ID", new[] { new SqlParameter("@Order_Id", orderId) }, null);
                if (taxDs != null && taxDs.Tables.Count > 0)
                {
                    foreach (DataRow row in taxDs.Tables[0].Rows)
                    {
                        int taxId = Util.GetValueOfInt(row["Tax_Id"]);
                        decimal taxAmt = Util.GetValueOfDecimal(row["Tax_Amt"]);
                        decimal gross;
                        if (grossByTax.TryGetValue(taxId, out gross) && gross > 0m && taxAmt >= 0m && taxAmt < gross)
                        {
                            ratios[taxId] = (gross - taxAmt) / gross;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_272_DraftedInProcessSOsWidget.GetOrderTaxNetRatios", ex);
            }
            return ratios;
        }

        /// <summary>
        /// VAS163 2026-11-11: Row 35 C_Currency_ID of the login client's primary accounting
        /// schema (AD_ClientInfo.C_AcctSchema1_ID -> C_AcctSchema.C_Currency_ID, same lookup as
        /// FillBaseCurrency); 0 when it cannot be resolved (the card then sums unconverted).
        /// </summary>
        private static int GetBaseCurrencyId(Ctx ctx)
        {
            if (ctx == null) { return 0; }
            try
            {
                return Util.GetValueOfInt(DB.ExecuteScalar(@"
                    SELECT cs.C_Currency_ID
                      FROM AD_ClientInfo ci
                     INNER JOIN C_AcctSchema cs ON ( cs.C_AcctSchema_ID = ci.C_AcctSchema1_ID )
                     WHERE ci.AD_Client_ID = @AD_Client_ID",
                    new[] { new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()) }, null));
            }
            catch (Exception ex)
            {
                Log.Log(Level.WARNING, "VAS_272_DraftedInProcessSOsWidget.GetBaseCurrencyId", ex);
                return 0;
            }
        }

        /// <summary>The order currency's symbol (falls back to its ISO code) - read from Currency_Symbol / Currency_Code.</summary>
        private static string CurrencyLabel(IDataReader dr)
        {
            string symbol = Util.GetValueOfString(dr["Currency_Symbol"]);
            if (string.IsNullOrWhiteSpace(symbol)) { symbol = Util.GetValueOfString(dr["Currency_Code"]); }
            return (symbol ?? "").Trim();
        }

        /// <summary>
        /// Warehouse-level on-hand stock for one product (base UOM), summed across the
        /// warehouse's active locators (M_Storage joined to M_Locator). Reserved qty is
        /// not subtracted - it already contains the order being shown. A
        /// product/warehouse with no M_Storage rows reads as zero, never null.
        /// </summary>
        private decimal ResolveOnHand(Ctx ctx, int productId, int warehouseId)
        {
            if (productId <= 0 || warehouseId <= 0) { return 0m; }

            string sql = @"
                SELECT COALESCE(SUM(COALESCE(s.QtyOnHand, 0)), 0) AS On_Hand
                  FROM M_Storage s
                  INNER JOIN M_Locator loc ON ( loc.M_Locator_ID = s.M_Locator_ID )
                 WHERE s.M_Product_ID = @M_Product_ID
                   AND loc.M_Warehouse_ID = @M_Warehouse_ID
                   AND loc.IsActive = 'Y'";

            // "s" (M_Storage) is the main physical table here - M_Locator is only joined
            // to scope by warehouse (Prompt_Instructions "Case 1").
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "s", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            return Util.GetValueOfDecimal(DB.ExecuteScalar(sql, new[]
            {
                new SqlParameter("@M_Product_ID", productId),
                new SqlParameter("@M_Warehouse_ID", warehouseId)
            }, null));
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
                    // A decode-map lookup must never take the whole endpoint down - worst
                    // case the raw stored code is shown instead of its label
                    // (DecodeLabel's own fallback).
                    Log.Log(Level.SEVERE, "VAS_272_DraftedInProcessSOsWidget.GetDecodeMap(" + tableName + "." + columnName + ")", ex);
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

        /// <summary>
        /// VAS163 2026-11-10: base currency of the login client's primary accounting schema
        /// (AD_ClientInfo.C_AcctSchema1_ID -> C_AcctSchema.C_Currency_ID, same lookup as
        /// VAS_205) so the widget labels its amounts with it instead of a hard-coded rupee.
        /// Left empty (client shows no symbol) when it cannot be resolved.
        /// </summary>
        private static void FillBaseCurrency(Ctx ctx, CurrencyPayload target)
        {
            if (ctx == null || target == null) { return; }
            try
            {
                DataSet ds = DB.ExecuteDataset(@"
                    SELECT c.ISO_Code AS Currency_Code, c.CurSymbol AS Currency_Symbol, c.StdPrecision AS Currency_Precision
                      FROM AD_ClientInfo ci
                     INNER JOIN C_AcctSchema cs ON ( cs.C_AcctSchema_ID = ci.C_AcctSchema1_ID )
                     INNER JOIN C_Currency c ON ( c.C_Currency_ID = cs.C_Currency_ID )
                     WHERE ci.AD_Client_ID = @AD_Client_ID",
                    new[] { new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()) }, null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) { return; }
                DataRow r = ds.Tables[0].Rows[0];
                target.CurrencyIso = (Util.GetValueOfString(r["Currency_Code"]) ?? string.Empty).Trim();
                target.CurrencySymbol = (Util.GetValueOfString(r["Currency_Symbol"]) ?? string.Empty).Trim();
                if (r["Currency_Precision"] != DBNull.Value) { target.CurrencyPrecision = Util.GetValueOfInt(r["Currency_Precision"]); }
            }
            catch (Exception ex)
            {
                Log.Log(Level.WARNING, "FillBaseCurrency", ex);
            }
        }

        /// <summary>VAS163 2026-11-10: base (accounting-schema) currency carried by the widget's first payload.</summary>
        private class CurrencyPayload
        {
            public string CurrencyIso { get; set; } = string.Empty;
            public string CurrencySymbol { get; set; } = string.Empty;
            public int? CurrencyPrecision { get; set; }
        }

        private JsonResult ErrorResult(Ctx ctx)
        {
            string message = Msg.GetMsg(ctx, "Error", true) ?? "Error"; // VAS163 2026-11-11: text-only label (3-arg GetMsg, no tip appended)
            string json = JsonConvert.SerializeObject(new { Error = message });
            return Json(json, JsonRequestBehavior.AllowGet);
        }

        private class SummaryResult : CurrencyPayload
        {
            public int TotalCount { get; set; }
            public int DraftedCount { get; set; }
            public int InProcessCount { get; set; }
            public decimal TotalValue { get; set; }
        }

        private class OrdersResult
        {
            public int Total { get; set; }
            public List<OrderRow> Rows { get; set; }
        }

        private class OrderRow
        {
            public int SalesOrderId { get; set; }
            public string SalesOrderNumber { get; set; }
            public string SalesOrderDate { get; set; }
            public string CustomerName { get; set; }
            public string RepresentativeName { get; set; }
            public int LineCount { get; set; }
            public decimal OrderValue { get; set; }
            public string CurrencySymbol { get; set; }
            public int CurrencyPrecision { get; set; }
            public string StageCode { get; set; }
            public string Stage { get; set; }
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
            public string CurrencySymbol { get; set; }
            public int CurrencyPrecision { get; set; }
            public string WarehouseName { get; set; }
            public string DeliveryMode { get; set; }
            public string DeliveryStatus { get; set; }
        }

        private class OrderLineRow
        {
            public int LineNo { get; set; }
            public string ProductName { get; set; }
            public string AttributeText { get; set; }
            public string UomName { get; set; }
            /// <summary>VAS163 2026-11-11: Row 33 the product's base (master) UOM name - the unit FreeStock is stated in.</summary>
            public string BaseUomName { get; set; }
            public bool IsItem { get; set; }
            public decimal QtyOrdered { get; set; }
            public decimal QtyDelivered { get; set; }
            public decimal QtyPending { get; set; }
            /// <summary>VAS163 2026-11-11: Rows 33/58 on-hand stock in the product's BASE UOM, never negative.</summary>
            public decimal FreeStock { get; set; }
            /// <summary>VAS163 2026-11-11: Row 33 pending quantity not covered by on-hand stock, in the line's UOM.</summary>
            public decimal QtyShort { get; set; }
            public decimal Rate { get; set; }
            public decimal Amount { get; set; }
        }
    }
}
