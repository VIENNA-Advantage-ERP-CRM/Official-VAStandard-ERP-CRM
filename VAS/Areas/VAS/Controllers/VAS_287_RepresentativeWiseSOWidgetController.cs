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
    ///       the completing outbound M_InOut.MovementDate, averaged ONLY across that
    ///       representative's period orders that are fully delivered (every active
    ///       line's QtyDelivered &gt;= QtyOrdered). Orders not yet fully delivered are
    ///       excluded from the average, never counted as zero. The day-difference
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
                string json = JsonConvert.SerializeObject(GetTopRepresentativesData(ctx, month, year));
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
                       o.TotalLines AS Order_Value,
                       o.DocStatus AS Doc_Status_Code
                  FROM C_Order o
                 WHERE o.AD_Client_ID = @AD_Client_ID
                   AND o.IsActive = 'Y'
                   AND o.IsSOTrx = 'Y'
                   AND COALESCE(o.IsSalesQuotation, 'N') = 'N'
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
                    SUM(bo.Order_Value) AS Order_Value,
                    COUNT(*) AS Order_Count
                  FROM base_orders bo
                  INNER JOIN AD_User u ON ( u.AD_User_ID = bo.Sales_Rep_Id )
                 GROUP BY bo.Sales_Rep_Id, u.Name
                 ORDER BY SUM(bo.Order_Value) DESC
                 OFFSET 0 ROWS FETCH NEXT @Top_Count ROWS ONLY";

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, new[]
                {
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

            SqlParameter[] periodParams = new[]
            {
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
                SELECT SUM(Order_Value) AS Order_Value, COUNT(*) AS Order_Count FROM base_orders";

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
            string baseOrdersSql2 = MRole.GetDefault(ctx).AddAccessSQL(BuildBaseOrdersSql(true), "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            string pendingSql = @"
                WITH base_orders AS (" + baseOrdersSql2 + @"),
                line_stats AS (
                    SELECT bo.C_Order_ID AS C_Order_ID,
                           bo.Doc_Status_Code AS Doc_Status_Code,
                           SUM(
                               CASE WHEN COALESCE(ol.QtyOrdered, 0) > COALESCE(ol.QtyDelivered, 0)
                                    THEN COALESCE(ol.QtyOrdered, 0) - COALESCE(ol.QtyDelivered, 0)
                                    ELSE 0
                               END
                           ) AS Pending_Qty
                      FROM base_orders bo
                      INNER JOIN C_OrderLine ol ON ( ol.C_Order_ID = bo.C_Order_ID AND ol.IsActive = 'Y' )
                     GROUP BY bo.C_Order_ID, bo.Doc_Status_Code
                )
                SELECT
                    SUM(CASE WHEN Pending_Qty > 0 AND Doc_Status_Code = 'CO' THEN 1 ELSE 0 END) AS Pending_Delivery
                  FROM line_stats";

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

            // Query 3 of 3 - Avg cycle: fetch (DateOrdered, CompletionDate) pairs for
            // this representative's FULLY delivered period orders only, then compute
            // the calendar-day average here in C# (cross-database rule - never
            // Oracle-only or PostgreSQL-only date arithmetic in the SQL itself).
            string baseOrdersSql3 = MRole.GetDefault(ctx).AddAccessSQL(BuildBaseOrdersSql(true), "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            string cycleSql = @"
                WITH base_orders AS (" + baseOrdersSql3 + @"),
                line_stats AS (
                    SELECT bo.C_Order_ID AS C_Order_ID,
                           bo.Date_Ordered AS Date_Ordered,
                           SUM(COALESCE(ol.QtyOrdered, 0)) AS Qty_Ordered,
                           SUM(COALESCE(ol.QtyDelivered, 0)) AS Qty_Delivered
                      FROM base_orders bo
                      INNER JOIN C_OrderLine ol ON ( ol.C_Order_ID = bo.C_Order_ID AND ol.IsActive = 'Y' )
                     GROUP BY bo.C_Order_ID, bo.Date_Ordered
                )
                SELECT
                    ls.Date_Ordered AS Date_Ordered,
                    (
                        SELECT MAX(io.MovementDate)
                          FROM M_InOut io
                         WHERE io.C_Order_ID = ls.C_Order_ID
                           AND io.IsActive = 'Y'
                           AND io.IsSOTrx = 'Y'
                           AND COALESCE(io.IsReturnTrx, 'N') = 'N'
                           AND io.DocStatus = 'CO'
                    ) AS Completion_Date
                  FROM line_stats ls
                 WHERE ls.Qty_Ordered > 0
                   AND ls.Qty_Delivered >= ls.Qty_Ordered";

            IDataReader dr2 = null;
            try
            {
                dr2 = DB.ExecuteReader(cycleSql, periodParams);

                int cycleCount = 0;
                double cycleDaysTotal = 0;
                while (dr2 != null && dr2.Read())
                {
                    DateTime? dateOrdered = dr2["Date_Ordered"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr2["Date_Ordered"]);
                    DateTime? completionDate = dr2["Completion_Date"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr2["Completion_Date"]);

                    if (!dateOrdered.HasValue || !completionDate.HasValue) { continue; }

                    cycleDaysTotal += (completionDate.Value.Date - dateOrdered.Value.Date).Days;
                    cycleCount++;
                }

                result.AvgCycleDays = cycleCount > 0 ? (decimal?)Math.Round(cycleDaysTotal / cycleCount, 1) : null;
            }
            finally
            {
                CloseReader(dr2);
            }

            return result;
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
                     WHERE ol.IsActive = 'Y'
                     GROUP BY ol.C_Order_ID
                )
                SELECT
                    bo.C_Order_ID AS Order_Id,
                    bo.Document_No AS Document_No,
                    bo.Date_Ordered AS Date_Ordered,
                    bo.Order_Value AS Order_Value,
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
                 ORDER BY bo.Date_Ordered DESC, bo.Document_No DESC
                 OFFSET @Row_Offset ROWS FETCH NEXT @Page_Size ROWS ONLY";

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, new[]
                {
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

                    result.Rows.Add(new RepOrderRow
                    {
                        SalesOrderId = Util.GetValueOfInt(dr["Order_Id"]),
                        SalesOrderNumber = Util.GetValueOfString(dr["Document_No"]),
                        SalesOrderDate = dateOrdered.HasValue ? dateOrdered.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                        CustomerName = Util.GetValueOfString(dr["Customer_Name"]),
                        WarehouseName = Util.GetValueOfString(dr["Warehouse_Name"]),
                        RepresentativeName = Util.GetValueOfString(dr["Representative_Name"]),
                        OrderValue = dr["Order_Value"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Order_Value"]),
                        DocumentStatus = DecodeLabel(docStatusMap, docStatusCode),
                        DeliveryStatus = Msg.GetMsg(ctx, deliveryCode) ?? deliveryFallback,
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
            if (hasLines && allDelivered) { deliveryStatusKey = "VAS_287_DeliveryFull"; deliveryStatusFallback = "Fully delivered"; }
            else if (anyDelivered) { deliveryStatusKey = "VAS_287_DeliveryPartial"; deliveryStatusFallback = "Partial"; }
            else { deliveryStatusKey = "VAS_287_DeliveryPending"; deliveryStatusFallback = "Pending"; }
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

        private JsonResult ErrorResult(Ctx ctx)
        {
            string message = Msg.GetMsg(ctx, "Error") ?? "Error";
            string json = JsonConvert.SerializeObject(new { Error = message });
            return Json(json, JsonRequestBehavior.AllowGet);
        }

        private class TopRepresentativesResult
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
