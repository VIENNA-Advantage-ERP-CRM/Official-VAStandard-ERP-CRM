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
    ///   VAS163      2026-11-20 Sr 254 dispatch value / SO value / line amounts WITHOUT tax;
    ///                          Sr 255 mix, share and Dispatch value card in the base currency;
    ///                          Sr 248-253 SO lines: charge name, item-only qty/status, line UOM.
    /// </summary>
    [System.Web.Mvc.SessionState(System.Web.SessionState.SessionStateBehavior.ReadOnly)]
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
                DeliveryModeMixResult data = GetDeliveryModeMixData(ctx, month, year);
                FillBaseCurrency(ctx, data);
                string json = JsonConvert.SerializeObject(data);
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
        // VAS163 2026-11-20: Sr 254 the value is now WITHOUT tax (ex-tax rule of every SO widget): the
        // old "* (1 + rate / 100)" added the tax on top - twice on a tax-inclusive order, whose
        // PriceActual already carries it. Now MovementQty x PriceActual, and on a tax-inclusive order
        // (same totals-based rule as VAS_271: GrandTotal - TotalLines below half the C_OrderTax total)
        // the tax is taken out with the line's rate (x 100 / (100 + rate)). ROUND keeps the
        // PostgreSQL quotient inside System.Decimal.
        // The tax-inclusive test is made once per shipment on its order (io.C_Order_ID), outside the
        // SUM, so no scalar sub-select sits inside an aggregate.
        private const string DispatchValueSubquery = @"
            (CASE WHEN (SELECT 2 * (COALESCE(so.GrandTotal, 0) - COALESCE(so.TotalLines, 0)) FROM C_Order so WHERE so.C_Order_ID = io.C_Order_ID)
                       < (SELECT COALESCE(SUM(COALESCE(sot.TaxAmt, 0)), 0) FROM C_OrderTax sot WHERE sot.C_Order_ID = io.C_Order_ID AND sot.IsActive = 'Y')
                  THEN (SELECT COALESCE(ROUND(SUM(iol.MovementQty * COALESCE(ol.PriceActual, 0) * 100 / (100 + COALESCE(t.Rate, 0))), 6), 0)
                          FROM M_InOutLine iol
                          JOIN C_OrderLine ol ON ol.C_OrderLine_ID = iol.C_OrderLine_ID
                          LEFT JOIN C_Tax t ON t.C_Tax_ID = ol.C_Tax_ID
                         WHERE iol.M_InOut_ID = io.M_InOut_ID AND iol.IsActive = 'Y')
                  ELSE (SELECT COALESCE(ROUND(SUM(iol.MovementQty * COALESCE(ol.PriceActual, 0)), 6), 0)
                          FROM M_InOutLine iol
                          JOIN C_OrderLine ol ON ol.C_OrderLine_ID = iol.C_OrderLine_ID
                         WHERE iol.M_InOut_ID = io.M_InOut_ID AND iol.IsActive = 'Y')
             END)";

        // VAS163 2026-11-20: Sr 254 order tax-inclusive / ex-tax order value (alias "o"), as VAS_271 / VAS_286.
        private const string TaxIncludedSql =
            "(CASE WHEN 2 * (COALESCE(o.GrandTotal, 0) - COALESCE(o.TotalLines, 0)) < " +
            "(SELECT COALESCE(SUM(COALESCE(ott.TaxAmt, 0)), 0) FROM C_OrderTax ott WHERE ott.C_Order_ID = o.C_Order_ID AND ott.IsActive = 'Y') " +
            "THEN 'Y' ELSE 'N' END)";

        private const string OrderValueExclTaxSql =
            "CASE WHEN " + TaxIncludedSql + " = 'Y' " +
            "THEN COALESCE(o.TotalLines, 0) - (SELECT COALESCE(SUM(COALESCE(ot.TaxAmt, 0)), 0) FROM C_OrderTax ot WHERE ot.C_Order_ID = o.C_Order_ID AND ot.IsActive = 'Y') " +
            "ELSE COALESCE(o.TotalLines, 0) END";

        /// <summary>
        /// VAS163 2026-11-20: Sr 255 the dispatches with their value converted to the base
        /// (accounting-schema) currency - each dispatch is priced from its order lines, so it is in
        /// the order currency and is converted with the configured rates (currencyConvert, order
        /// DateAcct / DateOrdered and conversion type, as VAS_271 / VAS_287) BEFORE any summing.
        /// Placed right after the base_dispatches CTE; the base currency id is an int read from
        /// the DB, inlined like VAS_271. 0 = not resolvable -> unconverted.
        /// </summary>
        /// <param name="baseCurrencyId">Base currency id or 0.</param>
        /// <returns>The "valued" CTE text (with a leading comma).</returns>
        private static string BuildValuedCteSql(int baseCurrencyId)
        {
            string baseValueSql = baseCurrencyId > 0
                ? "CASE WHEN vo.C_Currency_ID IS NULL OR vo.C_Currency_ID = " + baseCurrencyId + " THEN COALESCE(bd.Dispatch_Value, 0) " +
                  "ELSE COALESCE(currencyConvert(COALESCE(bd.Dispatch_Value, 0), vo.C_Currency_ID, " + baseCurrencyId + ", COALESCE(vo.DateAcct, vo.DateOrdered), vo.C_ConversionType_ID, vo.AD_Client_ID, vo.AD_Org_ID), 0) END"
                : "COALESCE(bd.Dispatch_Value, 0)";
            return @",
                valued AS (
                    SELECT bd.M_InOut_ID AS M_InOut_ID,
                           bd.C_Order_ID AS C_Order_ID,
                           bd.Mode_Code AS Mode_Code,
                           ROUND(" + baseValueSql + @", 6) AS Base_Value
                      FROM base_dispatches bd
                      LEFT OUTER JOIN C_Order vo ON ( vo.C_Order_ID = bd.C_Order_ID )
                )";
        }

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

            // VAS163 2026-11-20: Sr 255 the mix and its total are summed in the base currency.
            string sql = @"
                WITH base_dispatches AS (" + baseDispatchesSql + @")" + BuildValuedCteSql(GetBaseCurrencyId(ctx)) + @"
                SELECT
                    Mode_Code,
                    SUM(COALESCE(Base_Value, 0)) AS Dispatch_Value,
                    COUNT(DISTINCT C_Order_ID) AS Sales_Order_Count,
                    COUNT(DISTINCT M_InOut_ID) AS Dispatch_Count
                  FROM valued
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
            // VAS163 2026-11-20: Sr 255 the Dispatch value card (and the share total) in the base currency.
            string summarySql = @"
                WITH base_dispatches AS (" + baseDispatchesSql1 + @")" + BuildValuedCteSql(GetBaseCurrencyId(ctx)) + @",
                mode_flag AS (
                    SELECT
                        C_Order_ID,
                        M_InOut_ID,
                        Base_Value AS Dispatch_Value,
                        CASE WHEN Mode_Code = @Mode_Code THEN 1 ELSE 0 END AS Is_This_Mode
                      FROM valued
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
                    cur.CurSymbol AS Currency_Symbol,
                    cur.ISO_Code AS Currency_Code,
                    cur.StdPrecision AS Currency_Precision,
                    COUNT(1) OVER () AS Total_Rows
                  FROM base_dispatches bd
                  LEFT OUTER JOIN C_Order o ON ( o.C_Order_ID = bd.C_Order_ID )
                  LEFT OUTER JOIN C_BPartner bp ON ( bp.C_BPartner_ID = o.C_BPartner_ID )
                  LEFT OUTER JOIN M_Warehouse wh ON ( wh.M_Warehouse_ID = bd.M_Warehouse_ID )
                  LEFT OUTER JOIN C_Currency cur ON ( cur.C_Currency_ID = o.C_Currency_ID )
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
                    // VAS163 2026-11-11: Row 93 - the dispatch value is priced from the order lines,
                    // so it is in the order's document (price list) currency (C_Order.C_Currency_ID).
                    string rowCurSymbol = Util.GetValueOfString(dr2["Currency_Symbol"]);
                    if (string.IsNullOrWhiteSpace(rowCurSymbol)) { rowCurSymbol = Util.GetValueOfString(dr2["Currency_Code"]); }

                    result.Rows.Add(new DispatchRow
                    {
                        CurrencySymbol = (rowCurSymbol ?? "").Trim(),
                        CurrencyIso = (Util.GetValueOfString(dr2["Currency_Code"]) ?? "").Trim(),
                        CurrencyPrecision = dr2["Currency_Precision"] == DBNull.Value ? 2 : Util.GetValueOfInt(dr2["Currency_Precision"]),
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
                       " + OrderValueExclTaxSql + @" AS Order_Value,
                       " + TaxIncludedSql + @" AS Tax_Included,
                       o.DeliveryViaRule AS Delivery_Via_Rule,
                       o.M_Warehouse_ID AS Warehouse_Id,
                       COALESCE(bp.Name, N'') AS Customer_Name,
                       COALESCE(w.Name, N'') AS Warehouse_Name,
                       (SELECT hc.ISO_Code FROM C_Currency hc WHERE hc.C_Currency_ID = o.C_Currency_ID) AS Header_Currency_Code,
                       (SELECT hc.CurSymbol FROM C_Currency hc WHERE hc.C_Currency_ID = o.C_Currency_ID) AS Header_Currency_Symbol,
                       (SELECT hc.StdPrecision FROM C_Currency hc WHERE hc.C_Currency_ID = o.C_Currency_ID) AS Header_Currency_Precision
                  FROM C_Order o
                  INNER JOIN C_BPartner bp ON ( bp.C_BPartner_ID = o.C_BPartner_ID )
                  LEFT OUTER JOIN M_Warehouse w ON ( w.M_Warehouse_ID = o.M_Warehouse_ID )
                 WHERE o.C_Order_ID = @Order_Id
                   AND o.AD_Client_ID = @AD_Client_ID
                   AND o.IsActive = 'Y'";

            headerSql = MRole.GetDefault(ctx).AddAccessSQL(headerSql, "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            int warehouseId = 0;
            bool taxIncluded = false; // VAS163 2026-11-20: Sr 254 SO value / line amounts without tax

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
                taxIncluded = string.Equals((Util.GetValueOfString(dr["Tax_Included"]) ?? "").Trim(), "Y", StringComparison.OrdinalIgnoreCase);
                DateTime? dateOrdered = dr["Date_Ordered"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr["Date_Ordered"]);
                DateTime? datePromised = dr["Date_Promised"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr["Date_Promised"]);

                result.Order = new OrderHeader
                {
                    // VAS163 2026-11-12: order (document) currency of OrderValue / line amounts.
                    CurrencyIso = (Util.GetValueOfString(dr["Header_Currency_Code"]) ?? "").Trim(),
                    CurrencySymbol = (Util.GetValueOfString(dr["Header_Currency_Symbol"]) ?? "").Trim(),
                    CurrencyPrecision = dr["Header_Currency_Precision"] == DBNull.Value ? 2 : Util.GetValueOfInt(dr["Header_Currency_Precision"]),
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

            LoadOrderLines(ctx, result, orderId, warehouseId, taxIncluded, result.Order.CurrencyPrecision);

            // VAS163 2026-11-20: Sr 249 / 253 the delivery status counts Item-type product lines only.
            bool anyDelivered = false, allDelivered = true;
            bool hasLines = false;
            foreach (OrderLineRow line in result.Lines)
            {
                if (!line.IsItem) { continue; }
                hasLines = true;
                if (line.QtyDelivered > 0) { anyDelivered = true; }
                if (line.QtyDelivered < line.QtyOrdered) { allDelivered = false; }
            }

            string deliveryStatusKey, deliveryStatusFallback;
            if (hasLines && allDelivered) { deliveryStatusKey = "VAS_282_DeliveryFull"; deliveryStatusFallback = "Delivered"; }
            else if (anyDelivered) { deliveryStatusKey = "VAS_282_DeliveryPartial"; deliveryStatusFallback = "Partially delivered"; }
            else { deliveryStatusKey = "VAS_282_DeliveryNone"; deliveryStatusFallback = "Not delivered"; }
            result.Order.DeliveryStatus = Msg.GetMsg(ctx, deliveryStatusKey, true) ?? deliveryStatusFallback; // VAS163 2026-11-11: text-only label (3-arg GetMsg, no tip appended)

            return result;
        }

        /// <summary>Every active line of the order, each carrying its own free-stock figure.</summary>
        /// <param name="ctx">Login context.</param>
        /// <param name="result">Detail payload the lines are added to.</param>
        /// <param name="orderId">C_Order_ID.</param>
        /// <param name="headerWarehouseId">Order warehouse (fallback for the line warehouse).</param>
        /// <param name="taxIncluded">VAS163 2026-11-20: Sr 254 order prices include tax - amounts restated without tax.</param>
        /// <param name="stdPrecision">Order currency precision.</param>
        private void LoadOrderLines(Ctx ctx, OrderDetailResult result, int orderId, int headerWarehouseId, bool taxIncluded, int stdPrecision)
        {
            // VAS163 2026-11-20: Sr 248-252 (as VAS_271): charge name for charge lines, product type,
            // QtyEntered (selected UOM), base UOM name, tax data. ProductType (CHAR) and the names are
            // read raw and resolved in C# (no COALESCE against a literal).
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
                       COALESCE(u.Name, N'') AS Uom_Name,
                       COALESCE(bu.Name, N'') AS Base_Uom_Name
                  FROM C_OrderLine ol
                  LEFT OUTER JOIN M_Product p ON ( p.M_Product_ID = ol.M_Product_ID )
                  LEFT OUTER JOIN C_Charge ch ON ( ch.C_Charge_ID = ol.C_Charge_ID )
                  LEFT OUTER JOIN C_Tax tx ON ( tx.C_Tax_ID = ol.C_Tax_ID )
                  LEFT OUTER JOIN M_AttributeSetInstance asi ON ( asi.M_AttributeSetInstance_ID = ol.M_AttributeSetInstance_ID )
                  LEFT OUTER JOIN C_UOM u ON ( u.C_UOM_ID = ol.C_UOM_ID )
                  LEFT OUTER JOIN C_UOM bu ON ( bu.C_UOM_ID = p.C_UOM_ID )
                 WHERE ol.C_Order_ID = @Order_Id
                   AND ol.IsActive = 'Y'
                 ORDER BY ol.Line ASC
                 OFFSET 0 ROWS FETCH NEXT @Max_Line_Rows ROWS ONLY";

            Dictionary<int, decimal> netRatioByTax = taxIncluded ? GetOrderTaxNetRatios(orderId) : new Dictionary<int, decimal>();

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
                    decimal qtyOrderedBase = dr["Qty_Ordered"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Qty_Ordered"]);
                    decimal qtyEntered = dr["Qty_Entered"] == DBNull.Value ? qtyOrderedBase : Util.GetValueOfDecimal(dr["Qty_Entered"]);
                    decimal qtyDeliveredBase = dr["Qty_Delivered"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Qty_Delivered"]);
                    int productId = dr["Product_Id"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Product_Id"]);
                    int lineWarehouseId = dr["Line_Warehouse_Id"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Line_Warehouse_Id"]);
                    int warehouseId = lineWarehouseId > 0 ? lineWarehouseId : headerWarehouseId;

                    // VAS163 2026-11-20: Sr 251 Ordered / Delivered / Pending in the line's selected UOM
                    // (QtyEntered; base quantities scaled by QtyEntered / QtyOrdered).
                    decimal uomRatio = qtyOrderedBase != 0m ? (qtyEntered / qtyOrderedBase) : 1m;
                    decimal qtyOrdered = qtyEntered;
                    decimal qtyDelivered = Math.Round(qtyDeliveredBase * uomRatio, 6, MidpointRounding.AwayFromZero);

                    // VAS163 2026-11-20: Sr 249 delivered / pending / stock / line status only for Item-type products.
                    bool isItem = productId > 0
                        && string.Equals((Util.GetValueOfString(dr["Product_Type"]) ?? "").Trim(), "I", StringComparison.OrdinalIgnoreCase);

                    // VAS163 2026-11-20: Sr 248 a charge line shows the charge's name.
                    string productName = Util.GetValueOfString(dr["Product_Name"]);
                    if (string.IsNullOrEmpty(productName)) { productName = Util.GetValueOfString(dr["Charge_Name"]); }
                    if (string.IsNullOrEmpty(productName)) { productName = Util.GetValueOfString(dr["Description"]); }

                    // VAS163 2026-11-20: Sr 254 line amount without tax (as VAS_271): on a tax-inclusive order
                    // LineNetAmt is scaled by its tax's net ratio from C_OrderTax, else line TaxAmt / rate is taken off.
                    decimal lineGross = dr["Line_Net_Amt"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Line_Net_Amt"]);
                    decimal lineAmt = lineGross;
                    int lineTaxId = dr["Line_Tax_Id"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Line_Tax_Id"]);
                    if (taxIncluded && netRatioByTax.ContainsKey(lineTaxId))
                    {
                        lineAmt = Math.Round(lineGross * netRatioByTax[lineTaxId], stdPrecision, MidpointRounding.AwayFromZero);
                    }
                    else if (taxIncluded)
                    {
                        decimal lineTax = dr["Line_Tax_Amt"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Line_Tax_Amt"]);
                        decimal taxRate = dr["Tax_Rate"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Tax_Rate"]);
                        if (lineTax == 0m && taxRate > 0m)
                        {
                            lineTax = Math.Round(lineGross * taxRate / (100m + taxRate), stdPrecision, MidpointRounding.AwayFromZero);
                        }
                        lineAmt = lineGross - lineTax;
                    }

                    // Rate per selected UOM (PriceEntered, fallback PriceActual), restated without tax with the same proportion.
                    decimal rate = dr["Price_Entered"] == DBNull.Value
                        ? (dr["Price_Actual"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Price_Actual"]))
                        : Util.GetValueOfDecimal(dr["Price_Entered"]);
                    if (taxIncluded && lineGross != 0m)
                    {
                        rate = Math.Round(rate * lineAmt / lineGross, 6, MidpointRounding.AwayFromZero);
                    }

                    // VAS163 2026-11-20: Sr 252 In stock stays in the product's base UOM (name sent with it);
                    // the shortage is judged in the line's UOM. Stock is only looked up for item lines.
                    decimal onHandBase = isItem ? Math.Max(0m, ResolveFreeStock(ctx, productId, warehouseId)) : 0m;
                    decimal qtyPending = isItem ? Math.Max(0m, qtyOrdered - qtyDelivered) : 0m;

                    result.Lines.Add(new OrderLineRow
                    {
                        LineNo = Util.GetValueOfInt(dr["Line_No"]),
                        ProductName = productName,
                        AttributeText = Util.GetValueOfString(dr["Attribute_Text"]),
                        UomName = Util.GetValueOfString(dr["Uom_Name"]),
                        BaseUomName = Util.GetValueOfString(dr["Base_Uom_Name"]),
                        IsItem = isItem,
                        HasAttribute = dr["Asi_Id"] != DBNull.Value && Util.GetValueOfInt(dr["Asi_Id"]) > 0,
                        QtyOrdered = qtyOrdered,
                        QtyDelivered = isItem ? qtyDelivered : 0m,
                        QtyPending = qtyPending,
                        // VAS163 2026-11-11: row 58 In stock never negative - show 0.
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
        /// Warehouse-level free stock for one product: QtyOnHand - QtyReserved -
        /// QtyDedicated - QtyAllocated, summed across the warehouse's active locators.
        /// </summary>
        private decimal ResolveFreeStock(Ctx ctx, int productId, int warehouseId)
        {
            if (productId <= 0 || warehouseId <= 0) { return 0m; }

            // VAS163 2026-11-13: rows 1029/1064 one In-stock definition in every SO lines pop-up (same as VAS_270/271/272):
            // on-hand of the order's warehouse in the base UOM (the reservation already holds this order), clamped at 0 by the caller.
            string sql = @"
                SELECT COALESCE(SUM(COALESCE(s.QtyOnHand, 0)), 0) AS Free_Stock
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

        /// <summary>
        /// VAS163 2026-11-20: Sr 255 C_Currency_ID of the login client's primary accounting schema
        /// (same lookup as FillBaseCurrency / VAS_271); 0 when it cannot be resolved.
        /// </summary>
        /// <param name="ctx">Login context.</param>
        /// <returns>Base currency id or 0.</returns>
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
                Log.Log(Level.WARNING, "VAS_282_DeliveryModeWidget.GetBaseCurrencyId", ex);
                return 0;
            }
        }

        /// <summary>
        /// VAS163 2026-11-20: Sr 254 (copied from VAS_271). Tax-inclusive order only: per C_Tax_ID,
        /// the share of the lines' gross (SUM(LineNetAmt)) that is NOT tax, from the order's own tax
        /// summary (C_OrderTax.TaxAmt). A tax without a plausible summary row is left out and the
        /// caller falls back to the line's own TaxAmt / rate.
        /// </summary>
        /// <param name="orderId">C_Order_ID.</param>
        /// <returns>C_Tax_ID -> net ratio.</returns>
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
                Log.Log(Level.SEVERE, "VAS_282_DeliveryModeWidget.GetOrderTaxNetRatios", ex);
            }
            return ratios;
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

        private class DeliveryModeMixResult : CurrencyPayload
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
            /// <summary>VAS163 2026-11-11: Row 93 - document (price list) currency of DispatchValue.</summary>
            public string CurrencySymbol { get; set; }
            public string CurrencyIso { get; set; }
            public int CurrencyPrecision { get; set; }
        }

        private class OrderDetailResult
        {
            public OrderHeader Order { get; set; }
            public List<OrderLineRow> Lines { get; set; }
        }

        private class OrderHeader
        {
            // VAS163 2026-11-12: order (document) currency.
            public string CurrencyIso { get; set; }
            public string CurrencySymbol { get; set; }
            public int CurrencyPrecision { get; set; }
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
            /// <summary>VAS163 2026-11-20: Sr 249-252 product base UOM, Item-type flag, ASI present, shortage in line UOM.</summary>
            public string BaseUomName { get; set; }
            public bool IsItem { get; set; }
            public bool HasAttribute { get; set; }
            public decimal QtyShort { get; set; }
            public decimal QtyOrdered { get; set; }
            public decimal QtyDelivered { get; set; }
            public decimal QtyPending { get; set; }
            public decimal FreeStock { get; set; }
            public decimal Rate { get; set; }
            public decimal Amount { get; set; }
        }
    }
}
