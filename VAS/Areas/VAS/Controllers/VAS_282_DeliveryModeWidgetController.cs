/******************************************************
 * Module Name    : VAS
 * Purpose        : Sales Order dashboard "Delivery Mode" proportional bar-list widget endpoints
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
    /// Module Name : VAS_282_DeliveryModeWidget
    /// Purpose     : Data endpoints for the 2x2 "Delivery Mode" proportional bar-list
    ///               widget on the Sales Order dashboard - how a selected Month/Year's
    ///               ACTUAL outbound dispatch value splits across the three known
    ///               shipping methods on M_InOut.DeliveryViaRule (D=Delivery,
    ///               P=Pickup, S=Shipper). (1) the mode mix (one row per mode that
    ///               actually has dispatches - the client zero-fills the other known
    ///               modes so all three always render), and (2) a mode drill-down -
    ///               stat strip plus the completed dispatches for that mode/period,
    ///               and (3) the shared Sales Order record-preview data (header
    ///               stats + paginated lines) for record/lines drill-through inside
    ///               that dispatch table.
    ///
    ///   Business definition per
    ///   15_Delivery_Mode_Claude_Development_Prompt.txt (an explicit override of the
    ///   paired HTML mock's Road/Courier/Rail/Customer-pickup labels and its "Avg
    ///   transit" stat card - neither is supported by the available schema):
    ///     - This is based on ACTUAL outbound M_InOut dispatch documents, never the
    ///       Sales Order's planned shipping method - DocStatus='CO', IsSOTrx='Y',
    ///       non-return, MovementDate within the selected month, DeliveryViaRule IN
    ///       ('D','P','S') only (no invented Road/Courier/Rail subtypes).
    ///     - Dispatch value (revised rule, same finding as VAS_154_ShippingMethodWidget):
    ///       the spec's M_InOut.VA077_TotalSalesAmt is an obsolete column that does not
    ///       exist in this environment. The value is instead COMPUTED from the
    ///       dispatch's own delivered quantity and the Sales Order line unit price incl.
    ///       tax - SUM over the M_InOut's active lines of M_InOutLine.MovementQty *
    ///       COALESCE(C_OrderLine.PriceActual, 0) * (1 + COALESCE(C_Tax.Rate, 0) / 100).
    ///       Share is by this dispatch VALUE, never document count.
    ///     - "Avg transit" is removed entirely (the schema gives no reliable
    ///       dispatch-to-delivery date pair for it) and replaced by "Dispatches" -
    ///       the count of completed M_InOut documents for that mode/period.
    ///     - The three known modes are always kept in the UI even at zero share; per
    ///       the prompt's own instruction this zero-fill/overlay happens in the
    ///       CLIENT (JS), so this API returns only the modes that actually have
    ///       dispatches for the period - never a synthesized zero row.
    ///
    ///   Per the CTE/MRole rule (Prompt_Instructions.txt "Case 1"): every query here
    ///   isolates a single "FROM M_InOut io" WHERE-only fragment (no GROUP BY) and
    ///   calls AddAccessSQL ONLY on that isolated fragment - never on a fragment that
    ///   already carries its own GROUP BY (the VAS_270/VAS_277 lesson). All GROUP
    ///   BY/JOIN aggregation happens in the outer CTE built around the
    ///   already-filtered text.
    ///   Read-only here; no writes.
    /// Chronological development:
    ///   VAI052      2026-09-14 Created
    ///   VAI052      2026-09-14 Fixed ORA-00904 on M_InOut.VA077_TotalSalesAmt (column
    ///                          does not exist in this environment) - dispatch value is
    ///                          now computed from M_InOutLine/C_OrderLine/C_Tax instead.
    /// </summary>
    public class VAS_282_DeliveryModeWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_282_DeliveryModeWidgetController).FullName);

        private const int DefaultPageSize = 10;
        private const int MaxPageSize = 10;
        private const int MaxLineRows = 200;

        private static readonly HashSet<string> KnownModeCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "D", "P", "S" };

        /// <summary>
        /// The mode mix for the selected month: one row per DeliveryViaRule that
        /// actually has completed dispatches in the period (never a synthesized zero
        /// row - the client overlays this onto its own fixed D/P/S scaffold).
        /// </summary>
        /// <param name="month">Zero-based month (0=Jan..11=Dec).</param>
        /// <param name="year">Calendar year.</param>
        /// <returns>JSON { TotalValue, Modes:[ { ModeCode, ModeName, Value, OrderCount, DispatchCount } ] } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetDeliveryModeMix(int month, int year)
        {
            if (Session["ctx"] == null) { return Json("", JsonRequestBehavior.AllowGet); }

            Ctx ctx = Session["ctx"] as Ctx;

            try
            {
                string json = JsonConvert.SerializeObject(GetDeliveryModeMixData(ctx, month, year));
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_282_DeliveryModeWidget.GetDeliveryModeMix", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// The row-click drill-down for one delivery mode: the stat-strip summary
        /// (this mode's value/orders/dispatches plus the period's total value, so the
        /// client can compute Share consistently) and one page of that mode's
        /// completed dispatches.
        /// </summary>
        /// <param name="modeCode">D, P, or S.</param>
        /// <param name="month">Zero-based month (0=Jan..11=Dec).</param>
        /// <param name="year">Calendar year.</param>
        /// <param name="page">Zero-based page index.</param>
        /// <param name="size">Rows per page (capped at <see cref="MaxPageSize"/>).</param>
        /// <returns>JSON { Summary:{...}, Total, Rows:[...] } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetModeDispatches(string modeCode, int month, int year, int page = 0, int size = DefaultPageSize)
        {
            if (Session["ctx"] == null) { return Json("", JsonRequestBehavior.AllowGet); }

            Ctx ctx = Session["ctx"] as Ctx;
            if (page < 0) { page = 0; }
            if (size <= 0 || size > MaxPageSize) { size = DefaultPageSize; }

            if (string.IsNullOrEmpty(modeCode) || !KnownModeCodes.Contains(modeCode)) { return ErrorResult(ctx); }

            try
            {
                string json = JsonConvert.SerializeObject(GetModeDispatchesData(ctx, modeCode.ToUpperInvariant(), month, year, page, size));
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_282_DeliveryModeWidget.GetModeDispatches", ex);
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
                Log.Log(Level.SEVERE, "VAS_282_DeliveryModeWidget.GetSalesOrderDetail", ex);
                return ErrorResult(ctx);
            }
        }

        private static void ResolvePeriod(int month, int year, out DateTime periodStart, out DateTime periodEnd)
        {
            DateTime today = DateTime.Today;
            int resolvedYear = year > 0 ? year : today.Year;
            int resolvedMonth = (month >= 0 && month <= 11) ? month : (today.Month - 1);

            periodStart = new DateTime(resolvedYear, resolvedMonth + 1, 1);
            periodEnd = periodStart.AddMonths(1);
        }

        // Computed dispatch-value expression, correlated to the outer M_InOut alias
        // "io". M_InOut.VA077_TotalSalesAmt is an obsolete column that does not exist
        // in this environment (same finding as VAS_154_ShippingMethodWidget, whose
        // header comment documents the spec owner's override). The value is instead
        // computed from the dispatch's own delivered quantity and the Sales Order
        // line's unit price incl. tax, so PARTIAL dispatches are valued correctly:
        //   Dispatch_Value = SUM over the M_InOut's active lines of
        //     M_InOutLine.MovementQty * COALESCE(C_OrderLine.PriceActual, 0)
        //                              * (1 + COALESCE(C_Tax.Rate, 0) / 100)
        private const string DispatchValueSubquery = @"
            (SELECT COALESCE(SUM(iol.MovementQty * COALESCE(ol.PriceActual, 0)
                                 * (1 + COALESCE(t.Rate, 0) / 100)), 0)
             FROM M_InOutLine iol
             JOIN C_OrderLine ol ON ol.C_OrderLine_ID = iol.C_OrderLine_ID
             LEFT JOIN C_Tax t ON t.C_Tax_ID = ol.C_Tax_ID
             WHERE iol.M_InOut_ID = io.M_InOut_ID AND iol.IsActive = 'Y')";

        /// <summary>
        /// The single physical-table fragment - one "FROM M_InOut io", WHERE-only, no
        /// GROUP BY (Prompt_Instructions.txt "Case 1"). The ONLY thing AddAccessSQL is
        /// ever applied to for this widget.
        /// </summary>
        private static string BuildBaseDispatchesSql(bool filterMode)
        {
            string filter = filterMode ? " AND io.DeliveryViaRule = @Mode_Code" : "";

            return @"
                SELECT io.M_InOut_ID AS M_InOut_ID,
                       io.C_Order_ID AS C_Order_ID,
                       io.M_Warehouse_ID AS M_Warehouse_ID,
                       io.DeliveryViaRule AS Mode_Code,
                       " + DispatchValueSubquery + @" AS Dispatch_Value
                  FROM M_InOut io
                 WHERE io.AD_Client_ID = @AD_Client_ID
                   AND io.IsActive = 'Y'
                   AND io.DocStatus = 'CO'
                   AND io.IsSOTrx = 'Y'
                   AND COALESCE(io.IsReturnTrx, 'N') = 'N'
                   AND io.MovementDate >= @Period_Start
                   AND io.MovementDate < @Period_End
                   AND io.DeliveryViaRule IN ('D', 'P', 'S')" + filter;
        }

        private DeliveryModeMixResult GetDeliveryModeMixData(Ctx ctx, int month, int year)
        {
            DeliveryModeMixResult result = new DeliveryModeMixResult { Modes = new List<DeliveryModeRow>() };
            if (ctx == null) { return result; }

            DateTime periodStart, periodEnd;
            ResolvePeriod(month, year, out periodStart, out periodEnd);

            string language = GetLanguage(ctx);
            Dictionary<string, string> modeMap = GetDecodeMap("M_InOut", "DeliveryViaRule", language);

            string baseDispatchesSql = MRole.GetDefault(ctx).AddAccessSQL(BuildBaseDispatchesSql(false), "io", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string sql = @"
                WITH base_dispatches AS (" + baseDispatchesSql + @")
                SELECT
                    Mode_Code,
                    SUM(COALESCE(Dispatch_Value, 0)) AS Dispatch_Value,
                    COUNT(DISTINCT C_Order_ID) AS Sales_Order_Count,
                    COUNT(DISTINCT M_InOut_ID) AS Dispatch_Count
                  FROM base_dispatches
                 GROUP BY Mode_Code";

            decimal totalValue = 0m;

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, new[]
                {
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                    new SqlParameter("@Period_Start", periodStart),
                    new SqlParameter("@Period_End", periodEnd)
                });

                while (dr != null && dr.Read())
                {
                    string modeCode = Util.GetValueOfString(dr["Mode_Code"]);
                    decimal value = dr["Dispatch_Value"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Dispatch_Value"]);
                    totalValue += value;

                    result.Modes.Add(new DeliveryModeRow
                    {
                        ModeCode = modeCode,
                        ModeName = DecodeModeLabel(modeMap, modeCode),
                        Value = value,
                        OrderCount = dr["Sales_Order_Count"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Sales_Order_Count"]),
                        DispatchCount = dr["Dispatch_Count"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Dispatch_Count"])
                    });
                }
            }
            finally
            {
                CloseReader(dr);
            }

            result.TotalValue = totalValue;
            return result;
        }

        private ModeDispatchesResult GetModeDispatchesData(Ctx ctx, string modeCode, int month, int year, int page, int size)
        {
            ModeDispatchesResult result = new ModeDispatchesResult { Rows = new List<DispatchRow>() };
            if (ctx == null) { return result; }

            DateTime periodStart, periodEnd;
            ResolvePeriod(month, year, out periodStart, out periodEnd);

            SqlParameter[] periodParams = new[]
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@Period_Start", periodStart),
                new SqlParameter("@Period_End", periodEnd)
            };

            // Query 1 of 2 - this mode's value/orders/dispatches plus the period's
            // TOTAL value across every mode (via conditional aggregation over the
            // same unfiltered base), so the client can compute Share consistently
            // with the widget's own mix figures. Kept as one small query rather than
            // two, per Prompt_Instructions "keep queries small and purpose specific".
            // "@Mode_Code" is bound to a single "Is_This_Mode" flag inside mode_flag
            // and never referenced again by name in the outer SELECT - Oracle's
            // driver binds each textual placeholder OCCURRENCE separately (the
            // VAS_280 lesson), so a bind name used four times in one CASE-laden
            // SELECT would need four separate parameters; funnelling it through one
            // CTE column keeps the statement to a single occurrence instead.
            string baseDispatchesSql1 = MRole.GetDefault(ctx).AddAccessSQL(BuildBaseDispatchesSql(false), "io", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            string summarySql = @"
                WITH base_dispatches AS (" + baseDispatchesSql1 + @"),
                mode_flag AS (
                    SELECT
                        C_Order_ID,
                        M_InOut_ID,
                        Dispatch_Value,
                        CASE WHEN Mode_Code = @Mode_Code THEN 1 ELSE 0 END AS Is_This_Mode
                      FROM base_dispatches
                )
                SELECT
                    SUM(CASE WHEN Is_This_Mode = 1 THEN COALESCE(Dispatch_Value, 0) ELSE 0 END) AS Mode_Value,
                    COUNT(DISTINCT CASE WHEN Is_This_Mode = 1 THEN C_Order_ID ELSE NULL END) AS Mode_Order_Count,
                    COUNT(DISTINCT CASE WHEN Is_This_Mode = 1 THEN M_InOut_ID ELSE NULL END) AS Mode_Dispatch_Count,
                    SUM(COALESCE(Dispatch_Value, 0)) AS Total_Value
                  FROM mode_flag";

            List<SqlParameter> summaryParams = new List<SqlParameter>(periodParams) { new SqlParameter("@Mode_Code", modeCode) };

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(summarySql, summaryParams.ToArray());
                if (dr != null && dr.Read())
                {
                    result.Summary = new ModeSummary
                    {
                        ModeValue = dr["Mode_Value"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Mode_Value"]),
                        OrderCount = dr["Mode_Order_Count"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Mode_Order_Count"]),
                        DispatchCount = dr["Mode_Dispatch_Count"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Mode_Dispatch_Count"]),
                        TotalValue = dr["Total_Value"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Total_Value"])
                    };
                }
            }
            finally
            {
                CloseReader(dr);
            }

            if (result.Summary == null) { return result; }

            // Query 2 of 2 - a fresh, mode-filtered AddAccessSQL pass for the
            // paginated dispatch list. C_Order/C_BPartner/M_Warehouse are joined
            // AFTER the access-filtered M_InOut fragment purely for display columns -
            // the row set itself is already access-filtered by the M_InOut side.
            string baseDispatchesSql2 = MRole.GetDefault(ctx).AddAccessSQL(BuildBaseDispatchesSql(true), "io", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            string listSql = @"
                WITH base_dispatches AS (" + baseDispatchesSql2 + @")
                SELECT
                    bd.M_InOut_ID AS InOut_Id,
                    bd.C_Order_ID AS Order_Id,
                    bd.Dispatch_Value AS Dispatch_Value,
                    COALESCE(o.DocumentNo, N'') AS Document_No,
                    o.DatePromised AS Date_Promised,
                    COALESCE(bp.Name, N'') AS Customer_Name,
                    COALESCE(wh.Name, N'') AS Warehouse_Name,
                    COUNT(1) OVER () AS Total_Rows
                  FROM base_dispatches bd
                  LEFT OUTER JOIN C_Order o ON ( o.C_Order_ID = bd.C_Order_ID )
                  LEFT OUTER JOIN C_BPartner bp ON ( bp.C_BPartner_ID = o.C_BPartner_ID )
                  LEFT OUTER JOIN M_Warehouse wh ON ( wh.M_Warehouse_ID = bd.M_Warehouse_ID )
                 ORDER BY o.DatePromised DESC, o.DocumentNo DESC
                 OFFSET @Row_Offset ROWS FETCH NEXT @Page_Size ROWS ONLY";

            List<SqlParameter> listParams = new List<SqlParameter>(periodParams)
            {
                new SqlParameter("@Mode_Code", modeCode),
                new SqlParameter("@Row_Offset", page * size),
                new SqlParameter("@Page_Size", size)
            };

            string language = GetLanguage(ctx);
            Dictionary<string, string> modeMap = GetDecodeMap("M_InOut", "DeliveryViaRule", language);
            string modeLabel = DecodeModeLabel(modeMap, modeCode);

            IDataReader dr2 = null;
            try
            {
                dr2 = DB.ExecuteReader(listSql, listParams.ToArray());

                int total = 0;
                while (dr2 != null && dr2.Read())
                {
                    total = dr2["Total_Rows"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr2["Total_Rows"]);
                    DateTime? datePromised = dr2["Date_Promised"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr2["Date_Promised"]);

                    result.Rows.Add(new DispatchRow
                    {
                        SalesOrderId = Util.GetValueOfInt(dr2["Order_Id"]),
                        SalesOrderNumber = Util.GetValueOfString(dr2["Document_No"]),
                        CustomerName = Util.GetValueOfString(dr2["Customer_Name"]),
                        WarehouseName = Util.GetValueOfString(dr2["Warehouse_Name"]),
                        ModeLabel = modeLabel,
                        PromisedDate = datePromised.HasValue ? datePromised.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                        DispatchValue = dr2["Dispatch_Value"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr2["Dispatch_Value"])
                    });
                }
                result.Total = total;
            }
            finally
            {
                CloseReader(dr2);
            }

            return result;
        }

        /// <summary>The tenant's own label for D/P/S, falling back to the confirmed English mapping (Delivery/Pickup/Shipper) only when the decode map carries no entry.</summary>
        private static string DecodeModeLabel(Dictionary<string, string> map, string code)
        {
            if (string.IsNullOrEmpty(code)) { return ""; }

            string label;
            if (map != null && map.TryGetValue(code, out label)) { return label; }

            switch (code.ToUpperInvariant())
            {
                case "D": return "Delivery";
                case "P": return "Pickup";
                case "S": return "Shipper";
                default: return code;
            }
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

                string docStatusCode = Util.GetValueOfString(dr["Doc_Status_Code"]);
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
                    OrderValue = dr["Order_Value"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Order_Value"]),
                    WarehouseName = Util.GetValueOfString(dr["Warehouse_Name"]),
                    DeliveryMode = DecodeLabel(GetDecodeMap("C_Order", "DeliveryViaRule", language), Util.GetValueOfString(dr["Delivery_Via_Rule"]))
                };
            }
            finally
            {
                CloseReader(dr);
            }

            if (result.Order == null) { return result; }

            LoadOrderLines(ctx, result, orderId, warehouseId);

            bool anyDelivered = false, allDelivered = true;
            bool hasLines = result.Lines.Count > 0;
            foreach (OrderLineRow line in result.Lines)
            {
                if (line.QtyDelivered > 0) { anyDelivered = true; }
                if (line.QtyDelivered < line.QtyOrdered) { allDelivered = false; }
            }

            string deliveryStatusKey, deliveryStatusFallback;
            if (hasLines && allDelivered) { deliveryStatusKey = "VAS_282_DeliveryFull"; deliveryStatusFallback = "Delivered"; }
            else if (anyDelivered) { deliveryStatusKey = "VAS_282_DeliveryPartial"; deliveryStatusFallback = "Partially delivered"; }
            else { deliveryStatusKey = "VAS_282_DeliveryNone"; deliveryStatusFallback = "Not delivered"; }
            result.Order.DeliveryStatus = Msg.GetMsg(ctx, deliveryStatusKey) ?? deliveryStatusFallback;

            return result;
        }

        /// <summary>Every active line of the order, each carrying its own free-stock figure.</summary>
        private void LoadOrderLines(Ctx ctx, OrderDetailResult result, int orderId, int headerWarehouseId)
        {
            string sql = @"
                SELECT ol.C_OrderLine_ID AS Line_Id,
                       ol.Line AS Line_No,
                       ol.M_Product_ID AS Product_Id,
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

                    result.Lines.Add(new OrderLineRow
                    {
                        LineNo = Util.GetValueOfInt(dr["Line_No"]),
                        ProductName = Util.GetValueOfString(dr["Product_Name"]),
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
        /// QtyDedicated - QtyAllocated, summed across the warehouse's active locators.
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

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "s", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            return Util.GetValueOfDecimal(DB.ExecuteScalar(sql, new[]
            {
                new SqlParameter("@M_Product_ID", productId),
                new SqlParameter("@M_Warehouse_ID", warehouseId)
            }, null));
        }

        private static string DecodeLabel(Dictionary<string, string> map, string code)
        {
            if (string.IsNullOrEmpty(code)) { return ""; }
            string label;
            return map != null && map.TryGetValue(code, out label) ? label : code;
        }

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
                    Log.Log(Level.SEVERE, "VAS_282_DeliveryModeWidget.GetDecodeMap(" + tableName + "." + columnName + ")", ex);
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

        private class DeliveryModeMixResult
        {
            public decimal TotalValue { get; set; }
            public List<DeliveryModeRow> Modes { get; set; }
        }

        private class DeliveryModeRow
        {
            public string ModeCode { get; set; }
            public string ModeName { get; set; }
            public decimal Value { get; set; }
            public int OrderCount { get; set; }
            public int DispatchCount { get; set; }
        }

        private class ModeDispatchesResult
        {
            public ModeSummary Summary { get; set; }
            public int Total { get; set; }
            public List<DispatchRow> Rows { get; set; }
        }

        private class ModeSummary
        {
            public decimal ModeValue { get; set; }
            public int OrderCount { get; set; }
            public int DispatchCount { get; set; }
            public decimal TotalValue { get; set; }
        }

        private class DispatchRow
        {
            public int SalesOrderId { get; set; }
            public string SalesOrderNumber { get; set; }
            public string CustomerName { get; set; }
            public string WarehouseName { get; set; }
            public string ModeLabel { get; set; }
            public string PromisedDate { get; set; }
            public decimal DispatchValue { get; set; }
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
            public decimal OrderValue { get; set; }
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
            public decimal QtyOrdered { get; set; }
            public decimal QtyDelivered { get; set; }
            public decimal QtyPending { get; set; }
            public decimal FreeStock { get; set; }
            public decimal Rate { get; set; }
            public decimal Amount { get; set; }
        }
    }
}
