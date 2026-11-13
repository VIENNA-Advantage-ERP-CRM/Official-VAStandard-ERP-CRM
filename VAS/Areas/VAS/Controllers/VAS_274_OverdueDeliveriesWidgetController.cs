/******************************************************
 * Module Name    : VAS
 * Purpose        : Sales Order dashboard "Overdue Deliveries" KPI widget endpoints
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
    /// Module Name : VAS_274_OverdueDeliveriesWidget
    /// Purpose     : Data endpoints for the 2x1 "Overdue Deliveries" KPI tile on the
    ///               Sales Order dashboard - the dashboard's only risk-toned tile. (1)
    ///               the tile/summary figures - overdue count, pending value, average
    ///               and worst-case days late, and (2) the drill-down modal's paginated
    ///               documents list (most overdue first) with a single-valued blocking
    ///               reason per row, and (3) the shared Sales Order record-preview data
    ///               (header stats + paginated lines with per-line free stock) reused
    ///               for both the "record" and "lines" child modals.
    ///
    ///   Business definition per
    ///   07_Overdue_Deliveries_Claude_Development_Prompt.txt - a strict subset of the
    ///   VAS_271 SOs Pending Delivery cohort:
    ///     - Real active Sales Order, DocStatus = 'CO' only, non-return, non-quotation,
    ///       pending quantity &gt; 0, AND DatePromised &lt; today (the added filter over
    ///       VAS_271's own cohort).
    ///     - Days late is whole CALENDAR days (weekends/holidays included), computed as
    ///       CURRENT_DATE - DatePromised via the codebase's existing portable
    ///       DAYSBETWEEN(a, b) = a - b function (confirmed usage/argument-order per
    ///       VAS_244's own comments) - never a hand-rolled EXTRACT/date_trunc expression.
    ///     - Blocking reason is a priority chain evaluated in this order and is
    ///       single-valued per row: (1) any line short of free stock at the ship-from
    ///       warehouse -&gt; "Stock short", (2) otherwise the customer's EFFECTIVE credit
    ///       status (C_BPartner.CreditStatusSettingOn = 'CL' -&gt; use
    ///       C_BPartner_Location.SOCreditStatus for the order's own
    ///       C_BPartner_Location_ID, else C_BPartner.SOCreditStatus) = 'H' -&gt;
    ///       "Credit hold", (3) otherwise -&gt; "Transport pending". The stock/credit
    ///       comparison values are computed in SQL; the display label is resolved in
    ///       C# via message key (never a literal string in the query).
    ///     - Free stock uses the exact same formula as VAS_271: SUM(QtyOnHand -
    ///       QtyReserved - QtyDedicated - QtyAllocated) over active M_Storage rows
    ///       joined to active M_Locator rows in that warehouse.
    ///
    ///   Per the CTE/MRole rule (Prompt_Instructions.txt "Case 1"), AddAccessSQL is
    ///   applied ONLY to the isolated "base_orders" CTE body (BuildBaseOrdersSql) - a
    ///   single SELECT against the one main physical table (C_Order "o") with its own
    ///   single WHERE clause - before it is embedded (via BuildCommonCtesSql) into the
    ///   combined multi-CTE statements used by both the summary and the modal list.
    ///   AddAccessSQL is never applied to either combined statement itself (mirroring
    ///   the fix already applied to VAS_270/271/272/273 after VAS_270 hit ORA-00904
    ///   from doing exactly that).
    ///   Read-only here; no writes.
    /// Chronological development:
    ///   VAI052      2026-09-14 Created
    /// </summary>
    [System.Web.Mvc.SessionState(System.Web.SessionState.SessionStateBehavior.ReadOnly)]
    public class VAS_274_OverdueDeliveriesWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_274_OverdueDeliveriesWidgetController).FullName);

        private const int DefaultPageSize = 10;
        private const int MaxPageSize = 10;
        private const int MaxLineRows = 200;

        /// <summary>
        /// Summary figures for the tile and the modal's stat strip.
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
                Log.Log(Level.SEVERE, "VAS_274_OverdueDeliveriesWidget.GetSummary", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// One page of the underlying overdue sales orders for the modal's table, most
        /// overdue first.
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
                Log.Log(Level.SEVERE, "VAS_274_OverdueDeliveriesWidget.GetOrders", ex);
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
                Log.Log(Level.SEVERE, "VAS_274_OverdueDeliveriesWidget.GetSalesOrderDetail", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// The base_orders CTE body - the ONLY fragment AddAccessSQL is ever applied to
        /// (Prompt_Instructions.txt "Case 1"): a single SELECT against the one main
        /// physical table (C_Order "o") with its own single WHERE clause, scoped to the
        /// overdue cohort (DocStatus 'CO', DatePromised strictly before today).
        /// </summary>
        /// <summary>
        /// VAS163 2026-11-11: Rows 57/61 - 'Y' when the order's prices include tax (the order's
        /// own IsTaxIncluded flag or its price list's flag) - same expression as VAS_270.
        /// </summary>
        private const string TaxIncludedSql =
            "(CASE WHEN COALESCE(o.IsTaxIncluded, 'N') = 'Y' " +
            "OR COALESCE((SELECT pl.IsTaxIncluded FROM M_PriceList pl WHERE pl.M_PriceList_ID = o.M_PriceList_ID), 'N') = 'Y' " +
            "THEN 'Y' ELSE 'N' END)";

        /// <summary>
        /// VAS163 2026-11-11: Row 57 - SO value WITHOUT taxes (same expression as VAS_270 /
        /// VAS_268): on a tax-inclusive order TotalLines holds the gross, so the order's
        /// extracted tax (C_OrderTax.TaxAmt) is taken off.
        /// </summary>
        private const string OrderValueExclTaxSql =
            "CASE WHEN " + TaxIncludedSql + " = 'Y' " +
            "THEN COALESCE(o.TotalLines, 0) - (SELECT COALESCE(SUM(COALESCE(ot.TaxAmt, 0)), 0) FROM C_OrderTax ot WHERE ot.C_Order_ID = o.C_Order_ID AND ot.IsActive = 'Y') " +
            "ELSE COALESCE(o.TotalLines, 0) END";

        /// <summary>
        /// VAS163 2026-11-11: Row 48 - the cohort is DatePromised strictly before the start
        /// of today (@Today = server DateTime.Today, bound after @AD_Client_ID). It used to
        /// compare with the DB's CURRENT_DATE and derive "days late" as
        /// CAST(DAYSBETWEEN(CURRENT_DATE, DatePromised)), which for a DatePromised carrying a
        /// time of day (or a DB clock in another time zone) yields a fraction below 1 that
        /// casts to 0 - the "0 days late" rows. Days late is now counted in C# on whole
        /// dates, so every listed order is at least 1 day late.
        /// Row 61: Tax_Included added for the ex-tax pending value; Currency_Id for the
        /// document currency of the row amounts.
        /// </summary>
        private static string BuildBaseOrdersSql()
        {
            return @"
                SELECT o.C_Order_ID AS C_Order_ID,
                       o.DocumentNo AS Document_No,
                       o.DatePromised AS Date_Promised,
                       o.C_BPartner_ID AS Bpartner_Id,
                       o.C_BPartner_Location_ID AS Bpartner_Location_Id,
                       o.M_Warehouse_ID AS Warehouse_Id,
                       o.C_Currency_ID AS Currency_Id,
                       " + TaxIncludedSql + @" AS Tax_Included
                  FROM C_Order o
                 WHERE o.AD_Client_ID = @AD_Client_ID
                   AND o.IsActive = 'Y'
                   AND o.IsSOTrx = 'Y'
                   AND COALESCE(o.IsReturnTrx, 'N') = 'N'
                   AND EXISTS (SELECT 1 FROM C_DocType dt WHERE dt.C_DocType_ID = o.C_DocTypeTarget_ID AND dt.DocSubTypeSO IN ('SO', 'WR', 'BO'))
                   AND o.DocStatus = 'CO'
                   AND o.DatePromised < @Today";
        }

        /// <summary>VAS163 2026-11-11: Row 48 - whole calendar days between the promised date and today.</summary>
        private static int ComputeDaysLate(DateTime? datePromised)
        {
            if (!datePromised.HasValue) { return 0; }
            return (DateTime.Today - datePromised.Value.Date).Days;
        }

        /// <summary>
        /// The CTE definitions shared by GetSummaryData and GetOrdersData (everything
        /// but the final SELECT): base_orders (already access-filtered), free_stock,
        /// product_pending and order_pending, exactly mirroring VAS_271's shape with the
        /// added DatePromised cutoff baked into base_orders. Callers prepend "WITH " and
        /// append their own final SELECT - never nested as a second WITH, and never
        /// re-run through AddAccessSQL.
        /// </summary>
        private string BuildCommonCtesSql(Ctx ctx)
        {
            string baseOrdersSql = MRole.GetDefault(ctx).AddAccessSQL(BuildBaseOrdersSql(), "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            // VAS163 2026-11-11: Row 61 - bug: product_pending valued the pending quantity as
            // (QtyOrdered - QtyDelivered) * PriceActual over EVERY active line, so charge lines
            // and non-item products (never shipped) counted their full amount as pending, and on
            // a tax-inclusive price list PriceActual carries the tax - the order's pending value
            // did not match its lines. Now: Item-type product lines only (the lines pop-up shows
            // pending only for them), value = pending share of the line's net amount (discounts
            // kept), tax taken out with the line's tax rate when tax-included (same expression as
            // VAS_270 DeliveredValueSql). Pending items / shortfall are item-only as a result.
            return @"
                base_orders AS (" + baseOrdersSql + @"),
                free_stock AS (
                    SELECT
                        l.M_Warehouse_ID AS M_Warehouse_ID,
                        s.M_Product_ID AS M_Product_ID,
                        SUM(
                            COALESCE(s.QtyOnHand, 0) - COALESCE(s.QtyReserved, 0)
                          - COALESCE(s.QtyDedicated, 0) - COALESCE(s.QtyAllocated, 0)
                        ) AS Free_Qty
                      FROM M_Locator l
                      INNER JOIN M_Storage s ON ( s.M_Locator_ID = l.M_Locator_ID AND s.IsActive = 'Y' )
                     WHERE l.IsActive = 'Y'
                     GROUP BY l.M_Warehouse_ID, s.M_Product_ID
                ),
                product_pending AS (
                    SELECT
                        bo.C_Order_ID AS C_Order_ID,
                        COALESCE(ol.M_Warehouse_ID, bo.Warehouse_Id) AS M_Warehouse_ID,
                        ol.M_Product_ID AS M_Product_ID,
                        SUM(
                            CASE
                                WHEN COALESCE(ol.QtyOrdered, 0) > COALESCE(ol.QtyDelivered, 0)
                                THEN COALESCE(ol.QtyOrdered, 0) - COALESCE(ol.QtyDelivered, 0)
                                ELSE 0
                            END
                        ) AS Pending_Qty,
                        SUM(
                            CASE
                                WHEN COALESCE(ol.QtyOrdered, 0) > COALESCE(ol.QtyDelivered, 0) AND COALESCE(ol.QtyOrdered, 0) <> 0
                                THEN ROUND((COALESCE(ol.QtyOrdered, 0) - COALESCE(ol.QtyDelivered, 0))
                                     * COALESCE(ol.LineNetAmt, 0) / ol.QtyOrdered
                                     * (CASE WHEN bo.Tax_Included = 'Y' THEN 100 / (100 + COALESCE(tx.Rate, 0)) ELSE 1 END), 6)
                                ELSE 0
                            END
                        ) AS Pending_Value
                      FROM base_orders bo
                      INNER JOIN C_OrderLine ol ON ( ol.C_Order_ID = bo.C_Order_ID AND ol.IsActive = 'Y' )
                      INNER JOIN M_Product p ON ( p.M_Product_ID = ol.M_Product_ID AND p.ProductType = 'I' )
                      LEFT OUTER JOIN C_Tax tx ON ( tx.C_Tax_ID = ol.C_Tax_ID )
                     WHERE COALESCE(ol.C_Charge_ID, 0) = 0
                     GROUP BY bo.C_Order_ID, COALESCE(ol.M_Warehouse_ID, bo.Warehouse_Id), ol.M_Product_ID
                ),
                order_pending AS (
                    SELECT
                        pp.C_Order_ID AS C_Order_ID,
                        SUM(pp.Pending_Qty) AS Pending_Qty,
                        SUM(pp.Pending_Value) AS Pending_Value,
                        SUM(
                            CASE
                                WHEN pp.Pending_Qty > COALESCE(fs.Free_Qty, 0)
                                THEN pp.Pending_Qty - COALESCE(fs.Free_Qty, 0)
                                ELSE 0
                            END
                        ) AS Shortfall_Qty
                      FROM product_pending pp
                      LEFT OUTER JOIN free_stock fs
                        ON ( fs.M_Warehouse_ID = pp.M_Warehouse_ID AND fs.M_Product_ID = pp.M_Product_ID )
                     WHERE pp.Pending_Qty > 0
                     GROUP BY pp.C_Order_ID
                )";
        }

        /// <summary>
        /// VAS163 2026-11-11: Row 61 (retest) - the list's pending value was ~0.3% above the
        /// sum of the pending line amounts in the lines pop-up: the SQL took tax out with
        /// 100 / (100 + C_Tax.Rate) while the lines use the order's own C_OrderTax net/gross
        /// ratio (GetOrderTaxNetRatios). This computes every overdue order's pending value
        /// with EXACTLY the lines pop-up arithmetic (LoadOrderLines): per Item line, the ex-tax
        /// line amount (C_OrderTax ratio per tax, fallback line TaxAmt / rate, rounded to the
        /// currency precision) times the pending share (QtyOrdered - QtyDelivered) / QtyOrdered,
        /// rounded to the currency precision, summed per order.
        /// </summary>
        private Dictionary<int, decimal> GetPendingValuesByOrder(Ctx ctx)
        {
            Dictionary<int, decimal> values = new Dictionary<int, decimal>();
            if (ctx == null) { return values; }

            string sql = @"
                WITH " + BuildCommonCtesSql(ctx) + @",
                tax_gross AS (
                    SELECT tgl.C_Order_ID AS C_Order_ID,
                           tgl.C_Tax_ID AS C_Tax_ID,
                           SUM(tgl.LineNetAmt) AS Gross
                      FROM C_OrderLine tgl
                      INNER JOIN base_orders tgb ON ( tgb.C_Order_ID = tgl.C_Order_ID )
                     WHERE tgl.IsActive = 'Y'
                     GROUP BY tgl.C_Order_ID, tgl.C_Tax_ID
                ),
                tax_amt AS (
                    SELECT ot.C_Order_ID AS C_Order_ID,
                           ot.C_Tax_ID AS C_Tax_ID,
                           SUM(ot.TaxAmt) AS Tax_Amt
                      FROM C_OrderTax ot
                      INNER JOIN base_orders tab ON ( tab.C_Order_ID = ot.C_Order_ID )
                     WHERE ot.IsActive = 'Y'
                     GROUP BY ot.C_Order_ID, ot.C_Tax_ID
                )
                SELECT
                    bo.C_Order_ID AS Order_Id,
                    bo.Tax_Included AS Tax_Included,
                    cur.StdPrecision AS Currency_Precision,
                    ol.QtyOrdered AS Qty_Ordered,
                    ol.QtyDelivered AS Qty_Delivered,
                    ol.LineNetAmt AS Line_Net_Amt,
                    ol.TaxAmt AS Line_Tax_Amt,
                    tx.Rate AS Tax_Rate,
                    tg.Gross AS Tax_Gross,
                    ta.Tax_Amt AS Order_Tax_Amt
                  FROM base_orders bo
                  INNER JOIN C_OrderLine ol ON ( ol.C_Order_ID = bo.C_Order_ID AND ol.IsActive = 'Y' )
                  INNER JOIN M_Product p ON ( p.M_Product_ID = ol.M_Product_ID AND p.ProductType = 'I' )
                  LEFT OUTER JOIN C_Tax tx ON ( tx.C_Tax_ID = ol.C_Tax_ID )
                  LEFT OUTER JOIN C_Currency cur ON ( cur.C_Currency_ID = bo.Currency_Id )
                  LEFT OUTER JOIN tax_gross tg ON ( tg.C_Order_ID = ol.C_Order_ID AND tg.C_Tax_ID = ol.C_Tax_ID )
                  LEFT OUTER JOIN tax_amt ta ON ( ta.C_Order_ID = ol.C_Order_ID AND ta.C_Tax_ID = ol.C_Tax_ID )
                 WHERE COALESCE(ol.C_Charge_ID, 0) = 0
                   AND COALESCE(ol.QtyOrdered, 0) > COALESCE(ol.QtyDelivered, 0)";

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, new[]
                {
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                    new SqlParameter("@Today", DateTime.Today)
                });

                while (dr != null && dr.Read())
                {
                    int orderId = Util.GetValueOfInt(dr["Order_Id"]);
                    int stdPrecision = dr["Currency_Precision"] == DBNull.Value ? 2 : Util.GetValueOfInt(dr["Currency_Precision"]);
                    decimal qtyOrdered = dr["Qty_Ordered"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Qty_Ordered"]);
                    decimal qtyDelivered = dr["Qty_Delivered"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Qty_Delivered"]);
                    if (qtyOrdered == 0m || qtyOrdered <= qtyDelivered) { continue; }

                    // Same as LoadOrderLines (Row 57): ex-tax line amount on a tax-inclusive order.
                    decimal lineGross = dr["Line_Net_Amt"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Line_Net_Amt"]);
                    decimal lineAmt = lineGross;
                    if (Util.GetValueOfString(dr["Tax_Included"]) == "Y")
                    {
                        bool hasRatio = dr["Tax_Gross"] != DBNull.Value && dr["Order_Tax_Amt"] != DBNull.Value;
                        decimal taxGross = hasRatio ? Util.GetValueOfDecimal(dr["Tax_Gross"]) : 0m;
                        decimal orderTaxAmt = hasRatio ? Util.GetValueOfDecimal(dr["Order_Tax_Amt"]) : 0m;
                        if (hasRatio && taxGross > 0m && orderTaxAmt >= 0m && orderTaxAmt < taxGross)
                        {
                            decimal ratio = (taxGross - orderTaxAmt) / taxGross;
                            lineAmt = Math.Round(lineGross * ratio, stdPrecision, MidpointRounding.AwayFromZero);
                        }
                        else
                        {
                            decimal lineTax = dr["Line_Tax_Amt"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Line_Tax_Amt"]);
                            decimal taxRate = dr["Tax_Rate"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Tax_Rate"]);
                            if (lineTax == 0m && taxRate > 0m)
                            {
                                lineTax = Math.Round(lineGross * taxRate / (100m + taxRate), stdPrecision, MidpointRounding.AwayFromZero);
                            }
                            lineAmt = lineGross - lineTax;
                        }
                    }

                    decimal pendingAmt = Math.Round(lineAmt * (qtyOrdered - qtyDelivered) / qtyOrdered, stdPrecision, MidpointRounding.AwayFromZero);
                    decimal sum;
                    values.TryGetValue(orderId, out sum);
                    values[orderId] = sum + pendingAmt;
                }
            }
            finally
            {
                CloseReader(dr);
            }

            return values;
        }

        private SummaryResult GetSummaryData(Ctx ctx)
        {
            SummaryResult result = new SummaryResult();
            if (ctx == null) { return result; }

            // VAS163 2026-11-11: Row 61 - pending value per order computed like the lines pop-up.
            Dictionary<int, decimal> pendingByOrder = GetPendingValuesByOrder(ctx);

            // VAS163 2026-11-11: Row 48 - one row per overdue order; count / pending value /
            // average and worst days late are aggregated here on whole dates (see ComputeDaysLate).
            string sql = @"
                WITH " + BuildCommonCtesSql(ctx) + @"
                SELECT
                    op.C_Order_ID AS C_Order_ID,
                    op.Pending_Value AS Pending_Value,
                    bo.Date_Promised AS Date_Promised
                  FROM order_pending op
                  INNER JOIN base_orders bo ON ( bo.C_Order_ID = op.C_Order_ID )";

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, new[]
                {
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                    new SqlParameter("@Today", DateTime.Today)
                });

                int count = 0, maxDays = 0;
                long sumDays = 0;
                decimal pendingValue = 0m;
                while (dr != null && dr.Read())
                {
                    DateTime? datePromised = dr["Date_Promised"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr["Date_Promised"]);
                    int daysLate = ComputeDaysLate(datePromised);
                    if (daysLate <= 0) { continue; }
                    count++;
                    sumDays += daysLate;
                    if (daysLate > maxDays) { maxDays = daysLate; }
                    // VAS163 2026-11-11: Row 61 - same per-line computation as the lines pop-up.
                    decimal orderPending;
                    pendingByOrder.TryGetValue(Util.GetValueOfInt(dr["C_Order_ID"]), out orderPending);
                    pendingValue += orderPending;
                }
                result.OverdueCount = count;
                result.PendingValue = pendingValue;
                result.AvgDaysLate = count > 0 ? Math.Round((decimal)sumDays / count, 2) : 0m;
                result.MaxDaysLate = maxDays;
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

            // VAS163 2026-11-11: Row 61 - pending value per order computed like the lines pop-up.
            Dictionary<int, decimal> pendingByOrder = GetPendingValuesByOrder(ctx);
            decimal orderPending;

            string sql = @"
                WITH " + BuildCommonCtesSql(ctx) + @"
                SELECT
                    bo.C_Order_ID AS Order_Id,
                    bo.Document_No AS Document_No,
                    COALESCE(bp.Name, N'') AS Customer_Name,
                    COALESCE(w.Name, N'') AS Warehouse_Name,
                    bo.Date_Promised AS Date_Promised,
                    op.Pending_Qty AS Pending_Qty,
                    op.Pending_Value AS Pending_Value,
                    cur.CurSymbol AS Currency_Symbol,
                    cur.ISO_Code AS Currency_Code,
                    cur.StdPrecision AS Currency_Precision,
                    op.Shortfall_Qty AS Shortfall_Qty,
                    CASE
                        WHEN bp.CreditStatusSettingOn = 'CL' THEN bpl.SOCreditStatus
                        ELSE bp.SOCreditStatus
                    END AS Effective_Credit_Status,
                    COUNT(1) OVER () AS Total_Rows
                  FROM order_pending op
                  INNER JOIN base_orders bo ON ( bo.C_Order_ID = op.C_Order_ID )
                  INNER JOIN C_BPartner bp ON ( bp.C_BPartner_ID = bo.Bpartner_Id )
                  LEFT OUTER JOIN C_BPartner_Location bpl ON ( bpl.C_BPartner_Location_ID = bo.Bpartner_Location_Id )
                  LEFT OUTER JOIN M_Warehouse w ON ( w.M_Warehouse_ID = bo.Warehouse_Id )
                  LEFT OUTER JOIN C_Currency cur ON ( cur.C_Currency_ID = bo.Currency_Id )
                 ORDER BY bo.Date_Promised ASC, bo.Document_No ASC
                 OFFSET @Row_Offset ROWS FETCH NEXT @Page_Size ROWS ONLY";

            // VAS163 2026-11-11: Row 48 - @Today follows @AD_Client_ID (base_orders WHERE);
            // most overdue first = oldest promised date first.
            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, new[]
                {
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                    new SqlParameter("@Today", DateTime.Today),
                    new SqlParameter("@Row_Offset", page * size),
                    new SqlParameter("@Page_Size", size)
                });

                int total = 0;
                while (dr != null && dr.Read())
                {
                    total = dr["Total_Rows"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Total_Rows"]);
                    DateTime? datePromised = dr["Date_Promised"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr["Date_Promised"]);
                    decimal shortfallQty = dr["Shortfall_Qty"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Shortfall_Qty"]);
                    string effectiveCreditStatus = Util.GetValueOfString(dr["Effective_Credit_Status"]);
                    // VAS163 2026-11-11: Rows 59/61 - the row's pending value is in the document currency.
                    string rowCurSymbol = Util.GetValueOfString(dr["Currency_Symbol"]);
                    if (string.IsNullOrWhiteSpace(rowCurSymbol)) { rowCurSymbol = Util.GetValueOfString(dr["Currency_Code"]); }

                    string blockReasonKey, blockReasonFallback;
                    if (shortfallQty > 0)
                    {
                        blockReasonKey = "VAS_274_ReasonStockShort"; blockReasonFallback = "Stock short";
                    }
                    else if (effectiveCreditStatus == "H")
                    {
                        blockReasonKey = "VAS_274_ReasonCreditHold"; blockReasonFallback = "Credit hold";
                    }
                    else
                    {
                        blockReasonKey = "VAS_274_ReasonTransportPending"; blockReasonFallback = "Transport pending";
                    }

                    result.Rows.Add(new OrderRow
                    {
                        SalesOrderId = Util.GetValueOfInt(dr["Order_Id"]),
                        SalesOrderNumber = Util.GetValueOfString(dr["Document_No"]),
                        CustomerName = Util.GetValueOfString(dr["Customer_Name"]),
                        WarehouseName = Util.GetValueOfString(dr["Warehouse_Name"]),
                        PromisedDate = datePromised.HasValue ? datePromised.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                        DaysLate = ComputeDaysLate(datePromised),
                        PendingQty = dr["Pending_Qty"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Pending_Qty"]),
                        // VAS163 2026-11-11: Row 61 - same per-line computation as the lines pop-up.
                        PendingValue = pendingByOrder.TryGetValue(Util.GetValueOfInt(dr["Order_Id"]), out orderPending) ? orderPending : 0m,
                        CurrencySymbol = (rowCurSymbol ?? "").Trim(),
                        CurrencyIso = (Util.GetValueOfString(dr["Currency_Code"]) ?? "").Trim(), // VAS163 2026-11-12: document currency ISO for client-side Intl formatting
                        CurrencyPrecision = dr["Currency_Precision"] == DBNull.Value ? 2 : Util.GetValueOfInt(dr["Currency_Precision"]),
                        BlockReason = Msg.GetMsg(ctx, blockReasonKey, true) ?? blockReasonFallback, // VAS163 2026-11-11: text-only label (3-arg GetMsg, no tip appended)
                        DeliveryStatus = Msg.GetMsg(ctx, "VAS_274_DeliveryPendingChip", true) ?? "Pending" // VAS163 2026-11-11: text-only label (3-arg GetMsg, no tip appended)
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
            // VAS163 2026-11-11: Rows 57/59 - tax-inclusive flag and document currency for the lines.
            bool priceListTaxIncluded = false;
            int stdPrecision = 2;
            string currencySymbol = "";

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
                currencySymbol = Util.GetValueOfString(dr["Currency_Symbol"]);
                if (string.IsNullOrWhiteSpace(currencySymbol)) { currencySymbol = Util.GetValueOfString(dr["Currency_Code"]); }
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
                    CurrencySymbol = (currencySymbol ?? "").Trim(),
                    CurrencyIso = (Util.GetValueOfString(dr["Currency_Code"]) ?? "").Trim(), // VAS163 2026-11-12: document currency ISO for client-side Intl formatting
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

            LoadOrderLines(ctx, result, orderId, warehouseId, priceListTaxIncluded, stdPrecision);

            bool anyDelivered = false, allDelivered = true;
            bool hasLines = result.Lines.Count > 0;
            foreach (OrderLineRow line in result.Lines)
            {
                if (line.QtyDelivered > 0) { anyDelivered = true; }
                if (line.QtyDelivered < line.QtyOrdered) { allDelivered = false; }
            }

            string deliveryStatusKey, deliveryStatusFallback;
            if (hasLines && allDelivered)
            {
                deliveryStatusKey = "VAS_274_DeliveryFull"; deliveryStatusFallback = "Delivered";
            }
            else if (anyDelivered)
            {
                deliveryStatusKey = "VAS_274_DeliveryPartial"; deliveryStatusFallback = "Partially delivered";
            }
            else
            {
                deliveryStatusKey = "VAS_274_DeliveryNone"; deliveryStatusFallback = "Not delivered";
            }
            result.Order.DeliveryStatus = Msg.GetMsg(ctx, deliveryStatusKey, true) ?? deliveryStatusFallback; // VAS163 2026-11-11: text-only label (3-arg GetMsg, no tip appended)

            return result;
        }

        /// <summary>
        /// Every active line of the order, each carrying its own free-stock figure
        /// (per <see cref="ResolveFreeStock"/>) and a derived line-status chip. This
        /// widget's cohort is always DocStatus='CO', so line status only ever
        /// distinguishes fully/partly delivered from still-in-process.
        /// </summary>
        private void LoadOrderLines(Ctx ctx, OrderDetailResult result, int orderId, int headerWarehouseId, bool priceListTaxIncluded, int stdPrecision)
        {
            // VAS163 2026-11-11: Rows 51/52/55/57 - charge, product type, QtyEntered, PriceEntered,
            // line tax and the product's base UOM added (same columns as VAS_270). ProductType /
            // names selected raw and resolved in C# (no COALESCE against a literal, see VAS_203).
            string sql = @"
                SELECT ol.C_OrderLine_ID AS Line_Id,
                       ol.Line AS Line_No,
                       ol.M_Product_ID AS Product_Id,
                       ol.C_Charge_ID AS Charge_Id,
                       ol.Description AS Description,
                       p.ProductType AS Product_Type,
                       ch.Name AS Charge_Name,
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

            // VAS163 2026-11-11: Row 57 - on a tax-inclusive order each line is restated net of
            // tax with the order's own tax summary (C_OrderTax), as in VAS_270.
            Dictionary<int, decimal> netRatioByTax = priceListTaxIncluded ? GetOrderTaxNetRatios(orderId) : new Dictionary<int, decimal>();

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
                    int chargeId = dr["Charge_Id"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Charge_Id"]);
                    int lineWarehouseId = dr["Line_Warehouse_Id"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Line_Warehouse_Id"]);
                    int warehouseId = lineWarehouseId > 0 ? lineWarehouseId : headerWarehouseId;

                    // VAS163 2026-11-11: Row 52 - quantities in the line's selected UOM: QtyEntered
                    // is in ol.C_UOM_ID, QtyOrdered / QtyDelivered in the base UOM, so delivered is
                    // scaled by QtyEntered / QtyOrdered (division guarded).
                    decimal uomRatio = qtyOrderedBase != 0m ? (qtyEntered / qtyOrderedBase) : 1m;
                    decimal qtyOrdered = qtyEntered;
                    decimal qtyDelivered = qtyDeliveredBase * uomRatio;

                    // VAS163 2026-11-11: Rows 55/56 - only Item-type product lines (never a charge)
                    // carry delivered / pending / stock / line status and count in the cards.
                    bool isItem = productId > 0 && chargeId <= 0
                        && string.Equals((Util.GetValueOfString(dr["Product_Type"]) ?? "").Trim(), "I", StringComparison.OrdinalIgnoreCase);

                    // VAS163 2026-11-11: Row 51 - a charge line shows the charge's name as the product.
                    string productName = chargeId > 0 ? Util.GetValueOfString(dr["Charge_Name"]) : null;
                    if (string.IsNullOrEmpty(productName)) { productName = Util.GetValueOfString(dr["Product_Name"]); }
                    if (string.IsNullOrEmpty(productName)) { productName = Util.GetValueOfString(dr["Charge_Name"]); }
                    if (string.IsNullOrEmpty(productName)) { productName = Util.GetValueOfString(dr["Description"]); }

                    // VAS163 2026-11-11: Row 57 - line amount without tax (VAS_270 approach): on a
                    // tax-inclusive order LineNetAmt is gross and is scaled by its tax's net/gross
                    // ratio from C_OrderTax; fallback the line's TaxAmt / rate.
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

                    // VAS163 2026-11-11: Rows 52/57 - rate per selected UOM (PriceEntered, else
                    // PriceActual), restated ex-tax with the line's net/gross proportion.
                    decimal rate = dr["Price_Entered"] == DBNull.Value
                        ? (dr["Price_Actual"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Price_Actual"]))
                        : Util.GetValueOfDecimal(dr["Price_Entered"]);
                    if (priceListTaxIncluded && lineGross != 0m)
                    {
                        rate = Math.Round(rate * lineAmt / lineGross, 6, MidpointRounding.AwayFromZero);
                    }

                    // VAS163 2026-11-11: Row 58 - In stock (base UOM) never negative; the shortage is
                    // judged in the line's UOM.
                    decimal freeStockBase = isItem ? Math.Max(0m, ResolveFreeStock(ctx, productId, warehouseId)) : 0m;
                    decimal qtyPending = isItem ? Math.Max(0m, qtyOrdered - qtyDelivered) : 0m;

                    result.Lines.Add(new OrderLineRow
                    {
                        LineNo = Util.GetValueOfInt(dr["Line_No"]),
                        ProductName = productName,
                        AttributeText = Util.GetValueOfString(dr["Attribute_Text"]),
                        UomName = Util.GetValueOfString(dr["Uom_Name"]),
                        BaseUomName = Util.GetValueOfString(dr["Base_Uom_Name"]),
                        IsItem = isItem,
                        QtyOrdered = qtyOrdered,
                        QtyDelivered = qtyDelivered,
                        QtyPending = qtyPending,
                        FreeStock = freeStockBase,
                        QtyShort = isItem ? Math.Max(0m, qtyPending - freeStockBase * uomRatio) : 0m,
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
        /// QtyDedicated - QtyAllocated, summed across the warehouse's active
        /// locators (M_Storage joined to M_Locator). A product/warehouse with no
        /// M_Storage rows reads as zero, never null.
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

            // "s" (M_Storage) is the main physical table here - M_Locator is only joined
            // to scope by warehouse (Prompt_Instructions "Case 1").
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "s", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            return Util.GetValueOfDecimal(DB.ExecuteScalar(sql, new[]
            {
                new SqlParameter("@M_Product_ID", productId),
                new SqlParameter("@M_Warehouse_ID", warehouseId)
            }, null));
        }

        /// <summary>
        /// VAS163 2026-11-11: Row 57 - tax-inclusive order only: per C_Tax_ID, the share of the
        /// lines' gross (SUM(LineNetAmt)) that is NOT tax, from the order's own tax summary
        /// (C_OrderTax.TaxAmt) - copied from VAS_270, so the lines add up to the header SO value.
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
                Log.Log(Level.SEVERE, "VAS_274_OverdueDeliveriesWidget.GetOrderTaxNetRatios", ex);
            }
            return ratios;
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
                    Log.Log(Level.SEVERE, "VAS_274_OverdueDeliveriesWidget.GetDecodeMap(" + tableName + "." + columnName + ")", ex);
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
            public int OverdueCount { get; set; }
            public decimal PendingValue { get; set; }
            public decimal AvgDaysLate { get; set; }
            public int MaxDaysLate { get; set; }
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
            public string CustomerName { get; set; }
            public string WarehouseName { get; set; }
            public string PromisedDate { get; set; }
            public int DaysLate { get; set; }
            public decimal PendingQty { get; set; }
            public decimal PendingValue { get; set; }
            /// <summary>VAS163 2026-11-11: Rows 59/61 - document (price list) currency of PendingValue.</summary>
            public string CurrencyIso { get; set; } // VAS163 2026-11-12
            public string CurrencySymbol { get; set; }
            public int CurrencyPrecision { get; set; }
            public string BlockReason { get; set; }
            public string DeliveryStatus { get; set; }
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
            /// <summary>VAS163 2026-11-11: Row 59 - document (price list) currency of the SO value / lines.</summary>
            public string CurrencyIso { get; set; } // VAS163 2026-11-12
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
            /// <summary>VAS163 2026-11-11: product's base UOM (In stock is in it).</summary>
            public string BaseUomName { get; set; }
            /// <summary>VAS163 2026-11-11: Rows 55/56 - Item-type product line (not a charge).</summary>
            public bool IsItem { get; set; }
            public decimal QtyOrdered { get; set; }
            public decimal QtyDelivered { get; set; }
            public decimal QtyPending { get; set; }
            public decimal FreeStock { get; set; }
            /// <summary>VAS163 2026-11-11: pending minus stock, in the line's UOM, never negative.</summary>
            public decimal QtyShort { get; set; }
            public decimal Rate { get; set; }
            public decimal Amount { get; set; }
        }
    }
}
