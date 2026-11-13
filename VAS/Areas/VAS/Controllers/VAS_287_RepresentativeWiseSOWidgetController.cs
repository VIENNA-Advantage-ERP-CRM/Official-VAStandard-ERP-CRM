/******************************************************
 * Module Name    : VAS
 * Purpose        : Sales Order dashboard "Representative Wise SO" ranked bar-list widget endpoints
 * chronological  : Development
 * Created Date   : 2026-09-15
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
    /// Module Name : VAS_287_RepresentativeWiseSOWidget
    /// Purpose     : Data endpoints for the 3x2 "Representative Wise SO" ranked
    ///               horizontal bar-list widget on the Sales Order dashboard - the
    ///               ten sales representatives with the highest booked Sales Order
    ///               value for a selected Month/Year, ranked descending, five rows
    ///               per widget page (the widget holds all ten rows and pages
    ///               client-side without refetching). Structurally identical to
    ///               VAS_278 Top 10 Customers per the design spec's own instruction
    ///               ("two ranked lists on the same dashboard must look the same").
    ///
    ///   Business definition per
    ///   20_Representative_Wise_SO_Claude_Development_Prompt.txt (CONFIRMED):
    ///     - Representative is the ORDER-LEVEL representative: C_Order.SalesRep_ID
    ///       -&gt; AD_User.AD_User_ID -&gt; AD_User.Name. Never substituted with
    ///       C_BPartner.SalesRep_ID (the account-owner concept) even when the
    ///       order-level column exists - the order-level rep is who actually booked
    ///       the business.
    ///     - Booked Sales Order = active real Sales Order, IsSOTrx='Y', non-quotation,
    ///       non-return, DocStatus IN ('CO','CL'). Value = SUM(C_Order.TotalLines)
    ///       (pre-tax), period = C_Order.DateOrdered within the selected month.
    ///     - Historical inactive representatives are NOT filtered out: the AD_User
    ///       join carries no IsActive predicate, so someone who has since left the
    ///       company still appears for the months they actually booked in - dropping
    ///       them would make the period's ranking undercount the real total.
    ///     - Pending delivery (drill-down) = count of that representative's booked
    ///       orders (period, DocStatus='CO' only) with at least one active line where
    ///       QtyOrdered &gt; QtyDelivered - the exact same deterministic per-order rule
    ///       as VAS_278's "Open SOs" stat (never a QtyOrdered&gt;QtyDelivered count
    ///       across CL orders, which are already fully settled by definition).
    ///     - Avg cycle (drill-down) = mean calendar days from C_Order.DateOrdered to
    ///       the last completed/closed outbound M_InOut.MovementDate (matched through
    ///       M_InOutLine.C_OrderLine_ID, header C_Order_ID as fallback), averaged ONLY
    ///       across that representative's period orders that are fully delivered
    ///       (order QtyDelivered &gt;= QtyOrdered). Orders not yet fully delivered are
    ///       excluded from the average, never counted as zero; when the month has none,
    ///       the trailing 12 months are used (5-Nov row 30). The day-difference
    ///       itself is computed here in C#, never in SQL (cross-database rule).
    ///     - Access control is inherited entirely through the existing MRole
    ///       AddAccessSQL org-scoping already applied to every widget on this
    ///       dashboard - this schema has no separate "sales rep can see only their
    ///       own bookings" concept to detect or enforce beyond that, so no additional
    ///       security model is invented here per the general implementation rules.
    ///
    ///   Per the CTE/MRole rule (Prompt_Instructions.txt "Case 1"): every query here
    ///   isolates a single "FROM C_Order o" fragment (BuildBaseOrdersSql), calls
    ///   AddAccessSQL ONLY on that isolated fragment, then embeds the already-filtered
    ///   text into a larger multi-CTE statement - AddAccessSQL is never called again
    ///   on the combined statement itself. Read-only here; no writes.
    /// Chronological development:
    ///   VAI052      2026-09-15 Created
    /// </summary>
    [System.Web.Mvc.SessionState(System.Web.SessionState.SessionStateBehavior.ReadOnly)]
    public class VAS_287_RepresentativeWiseSOWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_287_RepresentativeWiseSOWidgetController).FullName);

        private const int TopRepCount = 10;
        private const int DefaultPageSize = 10;
        private const int MaxPageSize = 10;
        private const int MaxLineRows = 200;

        /// <summary>
        /// The ten representatives with the highest booked SO value for the selected
        /// month, ranked descending. Rank is assigned here in service code after the
        /// query, never in SQL.
        /// </summary>
        /// <param name="month">Zero-based month (0=Jan..11=Dec).</param>
        /// <param name="year">Calendar year.</param>
        /// <returns>JSON { Reps:[ { RepId, RepName, OrderValue, OrderCount, Rank } ] } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetTopRepresentatives(int month, int year)
        {
            if (Session["ctx"] == null) { return Json("", JsonRequestBehavior.AllowGet); }

            Ctx ctx = Session["ctx"] as Ctx;

            try
            {
                TopRepresentativesResult data = GetTopRepresentativesData(ctx, month, year);
                FillBaseCurrency(ctx, data);
                string json = JsonConvert.SerializeObject(data);
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_287_RepresentativeWiseSOWidget.GetTopRepresentatives", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// The row-click drill-down stat strip for one representative in the selected
        /// month: SO value, SOs booked, Pending delivery, Avg cycle.
        /// </summary>
        /// <param name="repId">AD_User_ID (C_Order.SalesRep_ID).</param>
        /// <param name="month">Zero-based month (0=Jan..11=Dec).</param>
        /// <param name="year">Calendar year.</param>
        /// <returns>JSON { OrderValue, OrderCount, PendingDelivery, AvgCycleDays } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetRepresentativeSummary(int repId, int month, int year)
        {
            if (Session["ctx"] == null) { return Json("", JsonRequestBehavior.AllowGet); }

            Ctx ctx = Session["ctx"] as Ctx;

            try
            {
                string json = JsonConvert.SerializeObject(GetRepresentativeSummaryData(ctx, repId, month, year));
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_287_RepresentativeWiseSOWidget.GetRepresentativeSummary", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// One page of the representative's booked Sales Orders for the selected month, newest first.
        /// </summary>
        /// <param name="repId">AD_User_ID (C_Order.SalesRep_ID).</param>
        /// <param name="month">Zero-based month (0=Jan..11=Dec).</param>
        /// <param name="year">Calendar year.</param>
        /// <param name="page">Zero-based page index.</param>
        /// <param name="size">Rows per page (capped at <see cref="MaxPageSize"/>).</param>
        /// <returns>JSON { Total, Rows:[...] } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetRepresentativeOrders(int repId, int month, int year, int page = 0, int size = DefaultPageSize)
        {
            if (Session["ctx"] == null) { return Json("", JsonRequestBehavior.AllowGet); }

            Ctx ctx = Session["ctx"] as Ctx;
            if (page < 0) { page = 0; }
            if (size <= 0 || size > MaxPageSize) { size = DefaultPageSize; }

            try
            {
                string json = JsonConvert.SerializeObject(GetRepresentativeOrdersData(ctx, repId, month, year, page, size));
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_287_RepresentativeWiseSOWidget.GetRepresentativeOrders", ex);
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
                Log.Log(Level.SEVERE, "VAS_287_RepresentativeWiseSOWidget.GetSalesOrderDetail", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>Resolves the zero-based month/year filter (defaulting to the current month) into an inclusive/exclusive date pair.</summary>
        private static void ResolvePeriod(int month, int year, out DateTime monthStart, out DateTime monthEnd)
        {
            DateTime today = DateTime.Today;
            int resolvedYear = year > 0 ? year : today.Year;
            int resolvedMonth = (month >= 0 && month <= 11) ? month : (today.Month - 1);

            monthStart = new DateTime(resolvedYear, resolvedMonth + 1, 1);
            monthEnd = monthStart.AddMonths(1);
        }

        /// <summary>
        /// The single physical-table fragment every query here embeds as its own
        /// "base_orders" CTE - one "FROM C_Order o", the ONLY thing AddAccessSQL is
        /// ever applied to (Prompt_Instructions.txt "Case 1"). Returns one row per
        /// order (no aggregation) - grouping/summing happens one level up once this
        /// text is embedded. Every row here has a non-null SalesRep_ID.
        /// </summary>
        /// <summary>
        /// VAS163 2026-11-11: Row 129 - 'Y' when the order's prices include tax (the order's own
        /// IsTaxIncluded flag or its price list's flag) - same expression as VAS_270 / VAS_273.
        /// </summary>
        private const string TaxIncludedSql =
            "(CASE WHEN COALESCE(o.IsTaxIncluded, 'N') = 'Y' " +
            "OR COALESCE((SELECT pl.IsTaxIncluded FROM M_PriceList pl WHERE pl.M_PriceList_ID = o.M_PriceList_ID), 'N') = 'Y' " +
            "THEN 'Y' ELSE 'N' END)";

        /// <summary>
        /// VAS163 2026-11-11: Row 129 - SO value WITHOUT taxes (same expression as VAS_273): on a
        /// tax-inclusive order TotalLines holds the gross, so the order's C_OrderTax.TaxAmt is taken off.
        /// </summary>
        private const string OrderValueExclTaxSql =
            "CASE WHEN " + TaxIncludedSql + " = 'Y' " +
            "THEN COALESCE(o.TotalLines, 0) - (SELECT COALESCE(SUM(COALESCE(ot.TaxAmt, 0)), 0) FROM C_OrderTax ot WHERE ot.C_Order_ID = o.C_Order_ID AND ot.IsActive = 'Y') " +
            "ELSE COALESCE(o.TotalLines, 0) END";

        /// <summary>
        /// VAS163 2026-11-11: Row 127 - the ex-tax order value converted to the base
        /// (accounting-schema) currency with the configured rates (argument order as VAS_273).
        /// Binds @Base_Currency_ID.
        /// </summary>
        private const string OrderValueBaseSql =
            "CurrencyConvert(" + OrderValueExclTaxSql + ", o.C_Currency_ID, @Base_Currency_ID, " +
            "COALESCE(o.DateAcct, o.DateOrdered), o.C_ConversionType_ID, o.AD_Client_ID, o.AD_Org_ID)";

        // VAS163 2026-11-11: Rows 126/127/129 - Order_Value is the ex-tax value in the document
        // (price list) currency (Currency_Id), Base_Order_Value the same converted to the base
        // currency for the summary card / ranking. @Base_Currency_ID now occurs BEFORE
        // @AD_Client_ID (positional binding) - every caller binds it first.
        private static string BuildBaseOrdersSql(bool filterByRep)
        {
            string repFilter = filterByRep ? " AND o.SalesRep_ID = @Sales_Rep_ID" : "";

            return @"
                SELECT o.C_Order_ID AS C_Order_ID,
                       o.DocumentNo AS Document_No,
                       o.DateOrdered AS Date_Ordered,
                       o.DatePromised AS Date_Promised,
                       o.C_BPartner_ID AS Bpartner_Id,
                       o.M_Warehouse_ID AS Warehouse_Id,
                       o.SalesRep_ID AS Sales_Rep_Id,
                       " + OrderValueExclTaxSql + @" AS Order_Value,
                       " + OrderValueBaseSql + @" AS Base_Order_Value,
                       o.C_Currency_ID AS Currency_Id,
                       o.DocStatus AS Doc_Status_Code
                  FROM C_Order o
                 WHERE o.AD_Client_ID = @AD_Client_ID
                   AND o.IsActive = 'Y'
                   AND o.IsSOTrx = 'Y'
                   AND EXISTS (SELECT 1 FROM C_DocType dt WHERE dt.C_DocType_ID = o.C_DocTypeTarget_ID AND dt.DocSubTypeSO IN ('SO', 'WR', 'BO'))
                   AND COALESCE(o.IsReturnTrx, 'N') = 'N'
                   AND o.DocStatus IN ('CO', 'CL')
                   AND o.DateOrdered >= @Month_Start
                   AND o.DateOrdered < @Month_End
                   AND o.SalesRep_ID IS NOT NULL" + repFilter;
        }

        private TopRepresentativesResult GetTopRepresentativesData(Ctx ctx, int month, int year)
        {
            TopRepresentativesResult result = new TopRepresentativesResult { Reps = new List<RepRankRow>() };
            if (ctx == null) { return result; }

            // The only access-breadth signal this framework actually exposes (per
            // Prompt_Instructions.txt CONFIRMED rule 8: show a helper note rather than
            // presenting a permission-filtered ranking as complete). Defensive default
            // to "full access" on any failure so a role-lookup hiccup never surfaces a
            // false "restricted" note.
            try { result.IsFullAccess = MRole.GetDefault(ctx).IsAccessAllOrgs(); }
            catch (Exception ex) { Log.Log(Level.WARNING, "VAS_287_RepresentativeWiseSOWidget.IsAccessAllOrgs", ex); result.IsFullAccess = true; }

            DateTime monthStart, monthEnd;
            ResolvePeriod(month, year, out monthStart, out monthEnd);

            // The only AddAccessSQL call for this statement - applied to the isolated
            // single-WHERE base_orders body BEFORE it is embedded below. No rep filter
            // here - ranking spans every representative in the period.
            string baseOrdersSql = MRole.GetDefault(ctx).AddAccessSQL(BuildBaseOrdersSql(false), "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string sql = @"
                WITH base_orders AS (" + baseOrdersSql + @")
                SELECT
                    bo.Sales_Rep_Id AS Rep_Id,
                    u.Name AS Rep_Name,
                    SUM(COALESCE(bo.Base_Order_Value, 0)) AS Order_Value,
                    COUNT(*) AS Order_Count
                  FROM base_orders bo
                  INNER JOIN AD_User u ON ( u.AD_User_ID = bo.Sales_Rep_Id )
                 GROUP BY bo.Sales_Rep_Id, u.Name
                 ORDER BY SUM(COALESCE(bo.Base_Order_Value, 0)) DESC
                 OFFSET 0 ROWS FETCH NEXT @Top_Count ROWS ONLY";

            IDataReader dr = null;
            try
            {
                // VAS163 2026-11-11: Rows 127/129 - ranked on the same ex-tax base-currency value
                // as the SO value card (labelled with the base currency); @Base_Currency_ID first.
                dr = DB.ExecuteReader(sql, new[]
                {
                    new SqlParameter("@Base_Currency_ID", GetBaseCurrencyId(ctx)),
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                    new SqlParameter("@Month_Start", monthStart),
                    new SqlParameter("@Month_End", monthEnd),
                    new SqlParameter("@Top_Count", TopRepCount)
                });

                int rank = 0;
                while (dr != null && dr.Read())
                {
                    rank++;
                    result.Reps.Add(new RepRankRow
                    {
                        RepId = Util.GetValueOfInt(dr["Rep_Id"]),
                        RepName = Util.GetValueOfString(dr["Rep_Name"]),
                        OrderValue = dr["Order_Value"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Order_Value"]),
                        OrderCount = dr["Order_Count"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Order_Count"]),
                        Rank = rank
                    });
                }
            }
            finally
            {
                CloseReader(dr);
            }

            return result;
        }

        private RepresentativeSummaryResult GetRepresentativeSummaryData(Ctx ctx, int repId, int month, int year)
        {
            RepresentativeSummaryResult result = new RepresentativeSummaryResult();
            if (ctx == null || repId <= 0) { return result; }

            DateTime monthStart, monthEnd;
            ResolvePeriod(month, year, out monthStart, out monthEnd);

            // VAS163 2026-11-11: Row 127 - base currency for the SO value card; bound first.
            int baseCurrencyId = GetBaseCurrencyId(ctx);
            SqlParameter[] periodParams = new[]
            {
                new SqlParameter("@Base_Currency_ID", baseCurrencyId),
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@Month_Start", monthStart),
                new SqlParameter("@Month_End", monthEnd),
                new SqlParameter("@Sales_Rep_ID", repId)
            };

            // Query 1 of 3 - kept small and purpose-specific (Prompt_Instructions.txt):
            // the representative's booked SO value and count.
            string baseOrdersSql1 = MRole.GetDefault(ctx).AddAccessSQL(BuildBaseOrdersSql(true), "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            string valueSql = @"
                WITH base_orders AS (" + baseOrdersSql1 + @")
                SELECT SUM(COALESCE(Base_Order_Value, 0)) AS Order_Value, COUNT(*) AS Order_Count FROM base_orders";
            // VAS163 2026-11-11: Rows 127/129 - SO value card = ex-tax values converted to the
            // base currency with currencyConvert BEFORE summing (Base_Order_Value).

            IDataReader dr0 = null;
            try
            {
                dr0 = DB.ExecuteReader(valueSql, periodParams);
                if (dr0 != null && dr0.Read())
                {
                    result.OrderValue = dr0["Order_Value"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr0["Order_Value"]);
                    result.OrderCount = dr0["Order_Count"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr0["Order_Count"]);
                }
            }
            finally
            {
                CloseReader(dr0);
            }

            // Query 2 of 3 - Pending delivery: firm ('CO') orders with at least one
            // active line where QtyOrdered > QtyDelivered - the exact same
            // deterministic per-order rule as VAS_278's "Open SOs" stat.
            // VAS163 2026-11-11: Row 128 - Pending delivery excludes ONLY fully delivered orders:
            // it used to count only 'CO' orders and judged every line, so a Closed order still
            // short of delivery was left out and an order whose items were all shipped stayed
            // "pending" because of a charge / service line (never shipped). Now every booked
            // order (CO and CL) with at least one shippable Item line (M_Product.ProductType = 'I',
            // no charge) where QtyOrdered > QtyDelivered counts - not delivered AND partly
            // delivered orders. Same Item-only rule as the list's Delivery chip and Avg cycle.
            // VAS163 2026-11-13: row 1102 the card left out booked orders the list shows as
            // "Pending" (e.g. Henry Garrick's Closed SO 50211 with no Item quantity left: 12 counted
            // of 14, expected 13). Now it excludes ONLY fully delivered orders, with exactly the
            // list's Delivery-chip rule (Item lines, no charge: ordered > 0 and delivered >= ordered
            // = fully delivered); every other booked order (pending or partly delivered) counts.
            string baseOrdersSql2 = MRole.GetDefault(ctx).AddAccessSQL(BuildBaseOrdersSql(true), "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            string pendingSql = @"
                WITH base_orders AS (" + baseOrdersSql2 + @"),
                line_stats AS (
                    SELECT bo.C_Order_ID AS C_Order_ID,
                           SUM(COALESCE(ol.QtyOrdered, 0)) AS Qty_Ordered,
                           SUM(COALESCE(ol.QtyDelivered, 0)) AS Qty_Delivered
                      FROM base_orders bo
                      INNER JOIN C_OrderLine ol ON ( ol.C_Order_ID = bo.C_Order_ID AND ol.IsActive = 'Y' )
                      INNER JOIN M_Product p ON ( p.M_Product_ID = ol.M_Product_ID )
                     WHERE p.ProductType = 'I'
                       AND COALESCE(ol.C_Charge_ID, 0) = 0
                     GROUP BY bo.C_Order_ID
                )
                SELECT
                    SUM(CASE WHEN COALESCE(ls.Qty_Ordered, 0) > 0 AND COALESCE(ls.Qty_Delivered, 0) >= COALESCE(ls.Qty_Ordered, 0)
                             THEN 0 ELSE 1 END) AS Pending_Delivery
                  FROM base_orders bo
                  LEFT OUTER JOIN line_stats ls ON ( ls.C_Order_ID = bo.C_Order_ID )";

            IDataReader dr1 = null;
            try
            {
                dr1 = DB.ExecuteReader(pendingSql, periodParams);
                if (dr1 != null && dr1.Read())
                {
                    result.PendingDelivery = dr1["Pending_Delivery"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr1["Pending_Delivery"]);
                }
            }
            finally
            {
                CloseReader(dr1);
            }

            // Query 3 of 3 - Avg cycle (5-Nov row 30). Cycle = calendar days from
            // C_Order.DateOrdered to the LAST completed/closed customer shipment of a
            // FULLY delivered order. The card used to stay "—" because (a) the shipment
            // was matched only through the M_InOut.C_Order_ID header link, which is
            // empty for shipments generated across orders / built from order lines,
            // (b) Closed ('CL') shipments were ignored, and (c) a current month usually
            // has no fully delivered order yet. The shipment is now matched through
            // its lines (M_InOutLine.C_OrderLine_ID) with the header link as fallback,
            // CO and CL both count, and when the selected month has no fully delivered
            // order the average widens to the trailing 12 months (same fallback as the
            // PO dashboard's VAS_215 Avg cycle). Day arithmetic stays in C#.
            int cycleCount;
            double cycleDaysTotal;
            ComputeCycleDays(ctx, repId, monthStart, monthEnd, out cycleCount, out cycleDaysTotal);
            if (cycleCount == 0)
            {
                ComputeCycleDays(ctx, repId, monthStart.AddMonths(-12), monthEnd, out cycleCount, out cycleDaysTotal);
            }
            result.AvgCycleDays = cycleCount > 0 ? (decimal?)Math.Round(cycleDaysTotal / cycleCount, 1) : null;

            return result;
        }

        /// <summary>
        /// Sums the order-to-final-shipment calendar days of the representative's fully
        /// delivered booked orders dated in [fromDate, toDate). Orders without a
        /// completed/closed shipment, or whose shipment predates the order, are skipped.
        /// </summary>
        private void ComputeCycleDays(Ctx ctx, int repId, DateTime fromDate, DateTime toDate, out int cycleCount, out double cycleDaysTotal)
        {
            cycleCount = 0;
            cycleDaysTotal = 0;

            string baseOrdersSql = MRole.GetDefault(ctx).AddAccessSQL(BuildBaseOrdersSql(true), "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            // VAS163 2026-11-11: row 13 Avg cycle stayed empty - the "fully delivered"
            // test summed EVERY line, so an order with a charge / service / resource line
            // (never shipped, QtyDelivered stays 0) could never qualify. The test now
            // uses only shippable Item product lines (M_Product.ProductType = 'I', no
            // charge); a Closed ('CL') order also counts as complete. Still per the
            // selected representative only (@Sales_Rep_ID in base_orders).
            string cycleSql = @"
                WITH base_orders AS (" + baseOrdersSql + @"),
                line_stats AS (
                    SELECT bo.C_Order_ID AS C_Order_ID,
                           bo.Date_Ordered AS Date_Ordered,
                           bo.Doc_Status_Code AS Doc_Status_Code,
                           SUM(COALESCE(ol.QtyOrdered, 0)) AS Qty_Ordered,
                           SUM(COALESCE(ol.QtyDelivered, 0)) AS Qty_Delivered
                      FROM base_orders bo
                      INNER JOIN C_OrderLine ol ON ( ol.C_Order_ID = bo.C_Order_ID AND ol.IsActive = 'Y' )
                      INNER JOIN M_Product p ON ( p.M_Product_ID = ol.M_Product_ID )
                     WHERE p.ProductType = 'I'
                       AND COALESCE(ol.C_Charge_ID, 0) = 0
                     GROUP BY bo.C_Order_ID, bo.Date_Ordered, bo.Doc_Status_Code
                )
                SELECT
                    ls.Date_Ordered AS Date_Ordered,
                    COALESCE(
                        (
                            SELECT MAX(io.MovementDate)
                              FROM M_InOut io
                              INNER JOIN M_InOutLine iol ON ( iol.M_InOut_ID = io.M_InOut_ID )
                              INNER JOIN C_OrderLine sol ON ( sol.C_OrderLine_ID = iol.C_OrderLine_ID )
                             WHERE sol.C_Order_ID = ls.C_Order_ID
                               AND io.IsActive = 'Y'
                               AND io.IsSOTrx = 'Y'
                               AND COALESCE(io.IsReturnTrx, 'N') = 'N'
                               AND io.DocStatus IN ('CO', 'CL')
                        ),
                        (
                            SELECT MAX(io2.MovementDate)
                              FROM M_InOut io2
                             WHERE io2.C_Order_ID = ls.C_Order_ID
                               AND io2.IsActive = 'Y'
                               AND io2.IsSOTrx = 'Y'
                               AND COALESCE(io2.IsReturnTrx, 'N') = 'N'
                               AND io2.DocStatus IN ('CO', 'CL')
                        )
                    ) AS Completion_Date
                  FROM line_stats ls
                 WHERE ( ls.Doc_Status_Code = 'CL' AND ls.Qty_Delivered > 0 )
                    OR ( ls.Qty_Ordered > 0 AND ls.Qty_Delivered >= ls.Qty_Ordered )";

            IDataReader dr = null;
            try
            {
                // Fresh parameter set per execution, in placeholder order.
                // VAS163 2026-11-11: Row 127 - base_orders now binds @Base_Currency_ID first.
                dr = DB.ExecuteReader(cycleSql, new[]
                {
                    new SqlParameter("@Base_Currency_ID", GetBaseCurrencyId(ctx)),
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                    new SqlParameter("@Month_Start", fromDate),
                    new SqlParameter("@Month_End", toDate),
                    new SqlParameter("@Sales_Rep_ID", repId)
                });

                while (dr != null && dr.Read())
                {
                    DateTime? dateOrdered = dr["Date_Ordered"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr["Date_Ordered"]);
                    DateTime? completionDate = dr["Completion_Date"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr["Completion_Date"]);

                    if (!dateOrdered.HasValue || !completionDate.HasValue) { continue; }

                    int days = (completionDate.Value.Date - dateOrdered.Value.Date).Days;
                    if (days < 0) { continue; }

                    cycleDaysTotal += days;
                    cycleCount++;
                }
            }
            finally
            {
                CloseReader(dr);
            }
        }

        private RepresentativeOrdersResult GetRepresentativeOrdersData(Ctx ctx, int repId, int month, int year, int page, int size)
        {
            RepresentativeOrdersResult result = new RepresentativeOrdersResult { Rows = new List<RepOrderRow>() };
            if (ctx == null || repId <= 0) { return result; }

            DateTime monthStart, monthEnd;
            ResolvePeriod(month, year, out monthStart, out monthEnd);

            string language = GetLanguage(ctx);
            Dictionary<string, string> docStatusMap = GetDecodeMap("C_Order", "DocStatus", language);

            string baseOrdersSql = MRole.GetDefault(ctx).AddAccessSQL(BuildBaseOrdersSql(true), "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string sql = @"
                WITH base_orders AS (" + baseOrdersSql + @"),
                line_stats AS (
                    SELECT ol.C_Order_ID AS C_Order_ID,
                           SUM(COALESCE(ol.QtyOrdered, 0)) AS Qty_Ordered,
                           SUM(COALESCE(ol.QtyDelivered, 0)) AS Qty_Delivered
                      FROM C_OrderLine ol
                      INNER JOIN M_Product p ON ( p.M_Product_ID = ol.M_Product_ID )
                     WHERE ol.IsActive = 'Y'
                       AND p.ProductType = 'I'
                       AND COALESCE(ol.C_Charge_ID, 0) = 0
                     GROUP BY ol.C_Order_ID
                )
                SELECT
                    bo.C_Order_ID AS Order_Id,
                    bo.Document_No AS Document_No,
                    bo.Date_Ordered AS Date_Ordered,
                    bo.Order_Value AS Order_Value,
                    cur.CurSymbol AS Currency_Symbol,
                    cur.ISO_Code AS Currency_Code,
                    cur.StdPrecision AS Currency_Precision,
                    bo.Doc_Status_Code AS Doc_Status_Code,
                    COALESCE(bp.Name, N'') AS Customer_Name,
                    COALESCE(wh.Name, N'') AS Warehouse_Name,
                    COALESCE(rep.Name, N'') AS Representative_Name,
                    COALESCE(ls.Qty_Ordered, 0) AS Qty_Ordered,
                    COALESCE(ls.Qty_Delivered, 0) AS Qty_Delivered,
                    COUNT(1) OVER () AS Total_Rows
                  FROM base_orders bo
                  INNER JOIN C_BPartner bp ON ( bp.C_BPartner_ID = bo.Bpartner_Id )
                  LEFT OUTER JOIN M_Warehouse wh ON ( wh.M_Warehouse_ID = bo.Warehouse_Id )
                  LEFT OUTER JOIN AD_User rep ON ( rep.AD_User_ID = bo.Sales_Rep_Id )
                  LEFT OUTER JOIN line_stats ls ON ( ls.C_Order_ID = bo.C_Order_ID )
                  LEFT OUTER JOIN C_Currency cur ON ( cur.C_Currency_ID = bo.Currency_Id )
                 ORDER BY bo.Date_Ordered DESC, bo.Document_No DESC
                 OFFSET @Row_Offset ROWS FETCH NEXT @Page_Size ROWS ONLY";

            IDataReader dr = null;
            try
            {
                // VAS163 2026-11-11: Rows 126/128 - Value in the order's own (price list) currency
                // (C_Currency of C_Order.C_Currency_ID); the Delivery chip judges Item lines only
                // (same rule as the Pending delivery card). base_orders binds @Base_Currency_ID first.
                dr = DB.ExecuteReader(sql, new[]
                {
                    new SqlParameter("@Base_Currency_ID", GetBaseCurrencyId(ctx)),
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                    new SqlParameter("@Month_Start", monthStart),
                    new SqlParameter("@Month_End", monthEnd),
                    new SqlParameter("@Sales_Rep_ID", repId),
                    new SqlParameter("@Row_Offset", page * size),
                    new SqlParameter("@Page_Size", size)
                });

                int total = 0;
                while (dr != null && dr.Read())
                {
                    total = dr["Total_Rows"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Total_Rows"]);
                    DateTime? dateOrdered = dr["Date_Ordered"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr["Date_Ordered"]);
                    string docStatusCode = Util.GetValueOfString(dr["Doc_Status_Code"]);
                    decimal qtyOrdered = dr["Qty_Ordered"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Qty_Ordered"]);
                    decimal qtyDelivered = dr["Qty_Delivered"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Qty_Delivered"]);

                    string deliveryCode, deliveryFallback, deliveryStatusCode;
                    if (qtyOrdered > 0 && qtyDelivered >= qtyOrdered) { deliveryCode = "VAS_287_DeliveryFull"; deliveryFallback = "Fully delivered"; deliveryStatusCode = "FULL"; }
                    else if (qtyDelivered > 0) { deliveryCode = "VAS_287_DeliveryPartial"; deliveryFallback = "Partial"; deliveryStatusCode = "PARTIAL"; }
                    else { deliveryCode = "VAS_287_DeliveryPending"; deliveryFallback = "Pending"; deliveryStatusCode = "PENDING"; }

                    // VAS163 2026-11-11: Row 126 - document currency symbol (ISO code when no symbol).
                    string rowCurSymbol = Util.GetValueOfString(dr["Currency_Symbol"]);
                    if (string.IsNullOrWhiteSpace(rowCurSymbol)) { rowCurSymbol = Util.GetValueOfString(dr["Currency_Code"]); }

                    result.Rows.Add(new RepOrderRow
                    {
                        SalesOrderId = Util.GetValueOfInt(dr["Order_Id"]),
                        SalesOrderNumber = Util.GetValueOfString(dr["Document_No"]),
                        SalesOrderDate = dateOrdered.HasValue ? dateOrdered.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                        CustomerName = Util.GetValueOfString(dr["Customer_Name"]),
                        WarehouseName = Util.GetValueOfString(dr["Warehouse_Name"]),
                        RepresentativeName = Util.GetValueOfString(dr["Representative_Name"]),
                        OrderValue = dr["Order_Value"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Order_Value"]),
                        CurrencySymbol = (rowCurSymbol ?? "").Trim(),
                        CurrencyIso = (Util.GetValueOfString(dr["Currency_Code"]) ?? "").Trim(),
                        CurrencyPrecision = dr["Currency_Precision"] == DBNull.Value ? 2 : Util.GetValueOfInt(dr["Currency_Precision"]),
                        DocumentStatus = DecodeLabel(docStatusMap, docStatusCode),
                        DeliveryStatus = Msg.GetMsg(ctx, deliveryCode, true) ?? deliveryFallback, // VAS163 2026-11-11: text-only label (3-arg GetMsg, no tip appended)
                        DeliveryStatusCode = deliveryStatusCode
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
                       o.DeliveryViaRule AS Delivery_Via_Rule,
                       o.M_Warehouse_ID AS Warehouse_Id,
                       COALESCE(bp.Name, N'') AS Customer_Name,
                       COALESCE(w.Name, N'') AS Warehouse_Name,
                       curr.CurSymbol AS Currency_Symbol,
                       curr.ISO_Code AS Currency_Code,
                       curr.StdPrecision AS Currency_Precision
                  FROM C_Order o
                  INNER JOIN C_BPartner bp ON ( bp.C_BPartner_ID = o.C_BPartner_ID )
                  LEFT OUTER JOIN M_Warehouse w ON ( w.M_Warehouse_ID = o.M_Warehouse_ID )
                  LEFT OUTER JOIN C_Currency curr ON ( curr.C_Currency_ID = o.C_Currency_ID )
                 WHERE o.C_Order_ID = @Order_Id
                   AND o.AD_Client_ID = @AD_Client_ID
                   AND o.IsActive = 'Y'";
            // VAS163 2026-11-11: row 129 - pop-up SO value without tax (OrderValueExclTaxSql, C_OrderTax)
            // and the tax-inclusive flag for the line restatement (same as VAS_278).

            headerSql = MRole.GetDefault(ctx).AddAccessSQL(headerSql, "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

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

                string docStatusCode = Util.GetValueOfString(dr["Doc_Status_Code"]);
                warehouseId = dr["Warehouse_Id"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Warehouse_Id"]);
                // VAS163 2026-11-11: row 129 - tax-inclusive flag and document precision for the lines.
                priceListTaxIncluded = Util.GetValueOfString(dr["Price_List_Tax_Included"]) == "Y";
                stdPrecision = dr["Currency_Precision"] == DBNull.Value ? 2 : Util.GetValueOfInt(dr["Currency_Precision"]);
                DateTime? dateOrdered = dr["Date_Ordered"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr["Date_Ordered"]);
                DateTime? datePromised = dr["Date_Promised"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr["Date_Promised"]);
                // VAS163 2026-11-11: Rows 93/126 - the record / lines pop-ups show the order's
                // amounts with its own (price list) currency, as the list does.
                string headerCurSymbol = Util.GetValueOfString(dr["Currency_Symbol"]);
                if (string.IsNullOrWhiteSpace(headerCurSymbol)) { headerCurSymbol = Util.GetValueOfString(dr["Currency_Code"]); }

                result.Order = new OrderHeader
                {
                    CurrencySymbol = (headerCurSymbol ?? "").Trim(),
                    CurrencyIso = (Util.GetValueOfString(dr["Currency_Code"]) ?? "").Trim(),
                    CurrencyPrecision = dr["Currency_Precision"] == DBNull.Value ? 2 : Util.GetValueOfInt(dr["Currency_Precision"]),
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

            LoadOrderLines(ctx, result, orderId, warehouseId, priceListTaxIncluded, stdPrecision);

            // VAS163 2026-11-11: row 130 - the order's delivery status (pop-up subtitle / foot note)
            // judges Item-type product lines only; charges / other product types are ignored (as VAS_278).
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
            if (hasLines && allDelivered) { deliveryStatusKey = "VAS_287_DeliveryFull"; deliveryStatusFallback = "Fully delivered"; }
            else if (anyDelivered) { deliveryStatusKey = "VAS_287_DeliveryPartial"; deliveryStatusFallback = "Partial"; }
            else { deliveryStatusKey = "VAS_287_DeliveryPending"; deliveryStatusFallback = "Pending"; }
            result.Order.DeliveryStatus = Msg.GetMsg(ctx, deliveryStatusKey, true) ?? deliveryStatusFallback; // VAS163 2026-11-11: text-only label (3-arg GetMsg, no tip appended)

            return result;
        }

        /// <summary>Every active line of the order, each carrying its own free-stock figure.</summary>
        // VAS163 2026-11-11: rows 130-132 - charge name, quantities in the line's UOM and Item-only
        // figures (same implementation as VAS_278 LoadOrderLines).
        private void LoadOrderLines(Ctx ctx, OrderDetailResult result, int orderId, int headerWarehouseId, bool priceListTaxIncluded, int stdPrecision)
        {
            // ProductType is a CHAR column and the charge / product names are NVARCHAR -
            // all selected raw and resolved in C# (no COALESCE against a literal).
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

            // VAS163 2026-11-11: row 129 - on a tax-inclusive order each line is restated net
            // of tax with the order's own tax summary (C_OrderTax), as in VAS_278.
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

                    // VAS163 2026-11-11: row 132 - quantities in the line's selected UOM:
                    // QtyEntered is in ol.C_UOM_ID, QtyOrdered / QtyDelivered in the base UOM,
                    // so delivered = QtyDelivered * QtyEntered / QtyOrdered (division guarded;
                    // multiplied first so a fully delivered line stays exactly equal).
                    decimal uomRatio = qtyOrderedBase != 0m ? (qtyEntered / qtyOrderedBase) : 1m;
                    decimal qtyOrdered = qtyEntered;
                    decimal qtyDelivered = qtyOrderedBase != 0m ? (qtyDeliveredBase * qtyEntered / qtyOrderedBase) : qtyDeliveredBase;

                    // VAS163 2026-11-11: row 130 - only Item-type product lines (never a charge
                    // line) carry ordered / delivered / pending / stock / line status.
                    bool isItem = productId > 0 && chargeId <= 0
                        && string.Equals((Util.GetValueOfString(dr["Product_Type"]) ?? "").Trim(), "I", StringComparison.OrdinalIgnoreCase);

                    // VAS163 2026-11-11: row 131 - a line created with a charge shows the charge's name.
                    string productName = chargeId > 0 ? Util.GetValueOfString(dr["Charge_Name"]) : null;
                    if (string.IsNullOrEmpty(productName)) { productName = Util.GetValueOfString(dr["Product_Name"]); }
                    if (string.IsNullOrEmpty(productName)) { productName = Util.GetValueOfString(dr["Charge_Name"]); }
                    if (string.IsNullOrEmpty(productName)) { productName = Util.GetValueOfString(dr["Description"]); }

                    // VAS163 2026-11-11: row 132 - In stock (base UOM) never negative; the shortage
                    // is judged in the line's UOM; pending never negative.
                    decimal freeStockBase = isItem ? Math.Max(0m, ResolveFreeStock(ctx, productId, warehouseId)) : 0m;
                    decimal qtyPending = isItem ? Math.Max(0m, qtyOrdered - qtyDelivered) : 0m;

                    // VAS163 2026-11-11: row 129 - line amount without tax (VAS_278 approach): on a
                    // tax-inclusive order LineNetAmt is gross and is scaled by its tax's
                    // net/gross ratio from C_OrderTax; fallback the line's TaxAmt / rate.
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

                    // VAS163 2026-11-11: rows 132/129 - rate per selected UOM (PriceEntered, else
                    // PriceActual), restated ex-tax with the line's net/gross proportion.
                    decimal rate = dr["Price_Entered"] == DBNull.Value
                        ? (dr["Price_Actual"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Price_Actual"]))
                        : Util.GetValueOfDecimal(dr["Price_Entered"]);
                    if (priceListTaxIncluded && lineGross != 0m)
                    {
                        rate = Math.Round(rate * lineAmt / lineGross, 6, MidpointRounding.AwayFromZero);
                    }

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
                        // VAS163 2026-11-11: row 58 In stock never negative - show 0.
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

        /// <summary>
        /// VAS163 2026-11-11: Row 127 - C_Currency_ID of the client's primary accounting schema
        /// (AD_ClientInfo.C_AcctSchema1_ID -> C_AcctSchema.C_Currency_ID), the target of the
        /// SO value conversion. Same lookup as FillBaseCurrency / VAS_273.
        /// </summary>
        private static int GetBaseCurrencyId(Ctx ctx)
        {
            if (ctx == null) { return 0; }
            return Util.GetValueOfInt(DB.ExecuteScalar(@"
                SELECT cs.C_Currency_ID
                  FROM AD_ClientInfo ci
                 INNER JOIN C_AcctSchema cs ON ( cs.C_AcctSchema_ID = ci.C_AcctSchema1_ID )
                 WHERE ci.AD_Client_ID = @AD_Client_ID",
                new[] { new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()) }, null));
        }

        /// <summary>
        /// VAS163 2026-11-11: row 129 - tax-inclusive order only: per C_Tax_ID, the share of the
        /// lines' gross (SUM(LineNetAmt)) that is NOT tax, from the order's own tax summary
        /// (C_OrderTax.TaxAmt) - copied from VAS_278 / VAS_273, so the lines add up to the header SO value.
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
                Log.Log(Level.SEVERE, "VAS_287_RepresentativeWiseSOWidget.GetOrderTaxNetRatios", ex);
            }
            return ratios;
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
                    Log.Log(Level.SEVERE, "VAS_287_RepresentativeWiseSOWidget.GetDecodeMap(" + tableName + "." + columnName + ")", ex);
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

        private class TopRepresentativesResult : CurrencyPayload
        {
            public bool IsFullAccess { get; set; }
            public List<RepRankRow> Reps { get; set; }
        }

        private class RepRankRow
        {
            public int RepId { get; set; }
            public string RepName { get; set; }
            public decimal OrderValue { get; set; }
            public int OrderCount { get; set; }
            public int Rank { get; set; }
        }

        private class RepresentativeSummaryResult
        {
            public decimal OrderValue { get; set; }
            public int OrderCount { get; set; }
            public int PendingDelivery { get; set; }
            public decimal? AvgCycleDays { get; set; }
        }

        private class RepresentativeOrdersResult
        {
            public int Total { get; set; }
            public List<RepOrderRow> Rows { get; set; }
        }

        private class RepOrderRow
        {
            public int SalesOrderId { get; set; }
            public string SalesOrderNumber { get; set; }
            public string SalesOrderDate { get; set; }
            public string CustomerName { get; set; }
            public string WarehouseName { get; set; }
            public string RepresentativeName { get; set; }
            public decimal OrderValue { get; set; }
            /// <summary>VAS163 2026-11-11: Row 126 - document (price list) currency of OrderValue.</summary>
            public string CurrencySymbol { get; set; }
            public string CurrencyIso { get; set; }
            public int CurrencyPrecision { get; set; }
            public string DocumentStatus { get; set; }
            public string DeliveryStatus { get; set; }
            public string DeliveryStatusCode { get; set; }
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
            /// <summary>VAS163 2026-11-11: Rows 93/126 - document (price list) currency of the order's amounts.</summary>
            public string CurrencySymbol { get; set; }
            public string CurrencyIso { get; set; }
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
            /// <summary>VAS163 2026-11-11: row 132 - product's base UOM (unit of FreeStock).</summary>
            public string BaseUomName { get; set; }
            /// <summary>VAS163 2026-11-11: row 130 - true only for an Item-type product line (no charge).</summary>
            public bool IsItem { get; set; }
            public decimal QtyOrdered { get; set; }
            public decimal QtyDelivered { get; set; }
            public decimal QtyPending { get; set; }
            public decimal FreeStock { get; set; }
            /// <summary>VAS163 2026-11-11: row 132 - pending qty not covered by stock, in the line's UOM.</summary>
            public decimal QtyShort { get; set; }
            public decimal Rate { get; set; }
            public decimal Amount { get; set; }
        }
    }
}
