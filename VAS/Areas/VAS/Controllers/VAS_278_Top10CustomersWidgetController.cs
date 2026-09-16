/******************************************************
 * Module Name    : VAS
 * Purpose        : Sales Order dashboard "Top 10 Customers" ranked bar-list widget endpoints
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
    /// Module Name : VAS_278_Top10CustomersWidget
    /// Purpose     : Data endpoints for the 3x2 "Top 10 Customers" ranked horizontal
    ///               bar-list widget on the Sales Order dashboard - the ten INDIVIDUAL
    ///               C_BPartner customers with the highest booked Sales Order value for
    ///               a selected Month/Year, ranked descending, five rows per widget
    ///               page (the widget holds all ten rows and pages client-side without
    ///               refetching). A row click drills into that customer's period stats
    ///               (SO value, Open SOs, Pending delivery, On-time %) and their Sales
    ///               Orders for the period, reusing the same shared record/lines child
    ///               modals as every other widget on this dashboard.
    ///
    ///   Business definition per
    ///   11_Top_10_Customers_Claude_Development_Prompt.txt: rank is by individual
    ///   customer (C_Order.C_BPartner_ID), never a customer group/parent hierarchy -
    ///   this schema carries no such hierarchy concept for C_BPartner, so no
    ///   group-level rollup applies. Booked Sales Order = active real Sales Order,
    ///   IsSOTrx='Y', non-quotation, non-return, DocStatus IN ('CO','CL'). SO value =
    ///   C_Order.TotalLines (pre-tax), period = C_Order.DateOrdered within the
    ///   selected calendar month. Open SOs = DocStatus='CO' with at least one active
    ///   line where QtyOrdered &gt; QtyDelivered, for the selected period. Pending
    ///   delivery = count of that customer's booked orders (CO or CL) in the period
    ///   with at least one active line where QtyOrdered &gt; QtyDelivered - same
    ///   deterministic pending-delivery rule as VAS_271, counting orders not lines.
    ///   On-time % = among the customer's fully-delivered orders in the period, the
    ///   share whose last completed outbound M_InOut.MovementDate is on or before
    ///   C_Order.DatePromised; null (no fully-delivered orders to judge) when the
    ///   denominator is zero.
    ///
    ///   month is zero-based (0=Jan..11=Dec), matching the widget's own Month select
    ///   values - never converted to a 1-based value at the API boundary.
    ///
    ///   Per the CTE/MRole rule (Prompt_Instructions.txt "Case 1"): every query here
    ///   isolates a single "FROM C_Order o" fragment (BuildBaseOrdersSql), calls
    ///   AddAccessSQL ONLY on that isolated fragment, then embeds the already-filtered
    ///   text into a larger multi-CTE statement - AddAccessSQL is never called again on
    ///   the combined statement itself (the VAS_270 ORA-00904 fix, applied consistently
    ///   across every widget this session). Read-only here; no writes.
    /// Chronological development:
    ///   VAI052      2026-09-14 Created
    /// </summary>
    public class VAS_278_Top10CustomersWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_278_Top10CustomersWidgetController).FullName);

        private const int TopCustomerCount = 10;
        private const int DefaultPageSize = 10;
        private const int MaxPageSize = 10;
        private const int MaxLineRows = 200;

        /// <summary>
        /// The ten individual customers with the highest booked SO value for the
        /// selected month, ranked descending. Rank is assigned here in service code
        /// after the query, never in SQL.
        /// </summary>
        /// <param name="month">Zero-based month (0=Jan..11=Dec).</param>
        /// <param name="year">Calendar year.</param>
        /// <returns>JSON { Customers:[ { CustomerId, CustomerName, OrderValue, Rank } ] } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetTopCustomers(int month, int year)
        {
            if (Session["ctx"] == null) { return Json("", JsonRequestBehavior.AllowGet); }

            Ctx ctx = Session["ctx"] as Ctx;

            try
            {
                string json = JsonConvert.SerializeObject(GetTopCustomersData(ctx, month, year));
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_278_Top10CustomersWidget.GetTopCustomers", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// The row-click drill-down stat strip for one customer in the selected month:
        /// SO value, Open SOs, Pending delivery, On-time %.
        /// </summary>
        /// <param name="customerId">C_BPartner_ID.</param>
        /// <param name="month">Zero-based month (0=Jan..11=Dec).</param>
        /// <param name="year">Calendar year.</param>
        /// <returns>JSON { OrderValue, OpenOrders, PendingDelivery, OnTimePercent } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetCustomerSummary(int customerId, int month, int year)
        {
            if (Session["ctx"] == null) { return Json("", JsonRequestBehavior.AllowGet); }

            Ctx ctx = Session["ctx"] as Ctx;

            try
            {
                string json = JsonConvert.SerializeObject(GetCustomerSummaryData(ctx, customerId, month, year));
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_278_Top10CustomersWidget.GetCustomerSummary", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// One page of the customer's booked Sales Orders for the selected month, newest first.
        /// </summary>
        /// <param name="customerId">C_BPartner_ID.</param>
        /// <param name="month">Zero-based month (0=Jan..11=Dec).</param>
        /// <param name="year">Calendar year.</param>
        /// <param name="page">Zero-based page index.</param>
        /// <param name="size">Rows per page (capped at <see cref="MaxPageSize"/>).</param>
        /// <returns>JSON { Total, Rows:[...] } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetCustomerOrders(int customerId, int month, int year, int page = 0, int size = DefaultPageSize)
        {
            if (Session["ctx"] == null) { return Json("", JsonRequestBehavior.AllowGet); }

            Ctx ctx = Session["ctx"] as Ctx;
            if (page < 0) { page = 0; }
            if (size <= 0 || size > MaxPageSize) { size = DefaultPageSize; }

            try
            {
                string json = JsonConvert.SerializeObject(GetCustomerOrdersData(ctx, customerId, month, year, page, size));
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_278_Top10CustomersWidget.GetCustomerOrders", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// Full data for the shared Sales Order record-preview modal (record view and
        /// lines view both render from this one payload).
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
                Log.Log(Level.SEVERE, "VAS_278_Top10CustomersWidget.GetSalesOrderDetail", ex);
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
        /// text is embedded.
        /// </summary>
        private static string BuildBaseOrdersSql(bool filterByCustomer)
        {
            string customerFilter = filterByCustomer ? " AND o.C_BPartner_ID = @C_BPartner_ID" : "";

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
                   AND COALESCE(o.IsReturnTrx, 'N') = 'N'
                   AND COALESCE(o.IsSalesQuotation, 'N') = 'N'
                   AND o.DocStatus IN ('CO', 'CL')
                   AND o.DateOrdered >= @Month_Start
                   AND o.DateOrdered < @Month_End" + customerFilter;
        }

        private TopCustomersResult GetTopCustomersData(Ctx ctx, int month, int year)
        {
            TopCustomersResult result = new TopCustomersResult { Customers = new List<CustomerRankRow>() };
            if (ctx == null) { return result; }

            DateTime monthStart, monthEnd;
            ResolvePeriod(month, year, out monthStart, out monthEnd);

            // The only AddAccessSQL call for this statement - applied to the isolated
            // single-WHERE base_orders body BEFORE it is embedded below. No customer
            // filter here - ranking spans every customer in the period.
            string baseOrdersSql = MRole.GetDefault(ctx).AddAccessSQL(BuildBaseOrdersSql(false), "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string sql = @"
                WITH base_orders AS (" + baseOrdersSql + @")
                SELECT
                    bo.Bpartner_Id AS Customer_Id,
                    bp.Name AS Customer_Name,
                    SUM(bo.Order_Value) AS Order_Value
                  FROM base_orders bo
                  INNER JOIN C_BPartner bp ON ( bp.C_BPartner_ID = bo.Bpartner_Id )
                 GROUP BY bo.Bpartner_Id, bp.Name
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
                    new SqlParameter("@Top_Count", TopCustomerCount)
                });

                int rank = 0;
                while (dr != null && dr.Read())
                {
                    rank++;
                    result.Customers.Add(new CustomerRankRow
                    {
                        CustomerId = Util.GetValueOfInt(dr["Customer_Id"]),
                        CustomerName = Util.GetValueOfString(dr["Customer_Name"]),
                        OrderValue = dr["Order_Value"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Order_Value"]),
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

        private CustomerSummaryResult GetCustomerSummaryData(Ctx ctx, int customerId, int month, int year)
        {
            CustomerSummaryResult result = new CustomerSummaryResult();
            if (ctx == null || customerId <= 0) { return result; }

            DateTime monthStart, monthEnd;
            ResolvePeriod(month, year, out monthStart, out monthEnd);

            SqlParameter[] periodParams = new[]
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@Month_Start", monthStart),
                new SqlParameter("@Month_End", monthEnd),
                new SqlParameter("@C_BPartner_ID", customerId)
            };

            // Query 1 of 3 - kept small and purpose-specific rather than one giant
            // statement (Prompt_Instructions.txt): the customer's booked SO value.
            string baseOrdersSql1 = MRole.GetDefault(ctx).AddAccessSQL(BuildBaseOrdersSql(true), "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            string valueSql = @"
                WITH base_orders AS (" + baseOrdersSql1 + @")
                SELECT SUM(Order_Value) AS Order_Value FROM base_orders";

            result.OrderValue = Util.GetValueOfDecimal(DB.ExecuteScalar(valueSql, periodParams, null));

            // Query 2 of 3 - Open SOs / Pending delivery, via the same deterministic
            // "at least one active line has QtyOrdered > QtyDelivered" rule as VAS_271
            // (a per-order SUM+GROUP BY, never an EXISTS subquery against the base
            // fragment, so the isolated base_orders text stays a clean single FROM).
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
                    SUM(CASE WHEN Pending_Qty > 0 AND Doc_Status_Code = 'CO' THEN 1 ELSE 0 END) AS Open_Orders,
                    SUM(CASE WHEN Pending_Qty > 0 THEN 1 ELSE 0 END) AS Pending_Delivery
                  FROM line_stats";

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(pendingSql, periodParams);
                if (dr != null && dr.Read())
                {
                    result.OpenOrders = dr["Open_Orders"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Open_Orders"]);
                    result.PendingDelivery = dr["Pending_Delivery"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Pending_Delivery"]);
                }
            }
            finally
            {
                CloseReader(dr);
            }

            // Query 3 of 3 - On-time %: among the customer's fully-delivered orders in
            // the period, compare the last completed outbound M_InOut.MovementDate
            // with C_Order.DatePromised (date-only, computed here in service code -
            // never an Oracle-only or PostgreSQL-only date function).
            string baseOrdersSql3 = MRole.GetDefault(ctx).AddAccessSQL(BuildBaseOrdersSql(true), "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            string onTimeSql = @"
                WITH base_orders AS (" + baseOrdersSql3 + @"),
                line_stats AS (
                    SELECT bo.C_Order_ID AS C_Order_ID,
                           bo.Date_Promised AS Date_Promised,
                           SUM(COALESCE(ol.QtyOrdered, 0)) AS Qty_Ordered,
                           SUM(COALESCE(ol.QtyDelivered, 0)) AS Qty_Delivered
                      FROM base_orders bo
                      INNER JOIN C_OrderLine ol ON ( ol.C_Order_ID = bo.C_Order_ID AND ol.IsActive = 'Y' )
                     GROUP BY bo.C_Order_ID, bo.Date_Promised
                ),
                fully_delivered AS (
                    SELECT ls.C_Order_ID AS C_Order_ID,
                           ls.Date_Promised AS Date_Promised,
                           (
                               SELECT MAX(io.MovementDate)
                                 FROM M_InOut io
                                WHERE io.C_Order_ID = ls.C_Order_ID
                                  AND io.IsActive = 'Y'
                                  AND io.IsSOTrx = 'Y'
                                  AND COALESCE(io.IsReturnTrx, 'N') = 'N'
                                  AND io.DocStatus = 'CO'
                           ) AS Last_Movement_Date
                      FROM line_stats ls
                     WHERE ls.Qty_Ordered > 0
                       AND ls.Qty_Delivered >= ls.Qty_Ordered
                )
                SELECT
                    COUNT(1) AS Fully_Delivered_Count,
                    SUM(CASE WHEN Last_Movement_Date IS NOT NULL AND Last_Movement_Date <= Date_Promised THEN 1 ELSE 0 END) AS On_Time_Count
                  FROM fully_delivered";

            IDataReader dr2 = null;
            try
            {
                dr2 = DB.ExecuteReader(onTimeSql, periodParams);
                if (dr2 != null && dr2.Read())
                {
                    int fullyDeliveredCount = dr2["Fully_Delivered_Count"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr2["Fully_Delivered_Count"]);
                    int onTimeCount = dr2["On_Time_Count"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr2["On_Time_Count"]);

                    // Null (never 0%) when there is nothing fully delivered yet to
                    // judge - the concept does not apply, per the formatting rule for
                    // an empty numeric cell.
                    result.OnTimePercent = fullyDeliveredCount > 0
                        ? (decimal?)Math.Round((onTimeCount * 100m) / fullyDeliveredCount, 1)
                        : null;
                }
            }
            finally
            {
                CloseReader(dr2);
            }

            return result;
        }

        private CustomerOrdersResult GetCustomerOrdersData(Ctx ctx, int customerId, int month, int year, int page, int size)
        {
            CustomerOrdersResult result = new CustomerOrdersResult { Rows = new List<CustomerOrderRow>() };
            if (ctx == null || customerId <= 0) { return result; }

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
                    new SqlParameter("@C_BPartner_ID", customerId),
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
                    if (qtyOrdered > 0 && qtyDelivered >= qtyOrdered) { deliveryCode = "VAS_278_DeliveryFull"; deliveryFallback = "Fully delivered"; deliveryStatusCode = "FULL"; }
                    else if (qtyDelivered > 0) { deliveryCode = "VAS_278_DeliveryPartial"; deliveryFallback = "Partial"; deliveryStatusCode = "PARTIAL"; }
                    else { deliveryCode = "VAS_278_DeliveryPending"; deliveryFallback = "Pending"; deliveryStatusCode = "PENDING"; }

                    result.Rows.Add(new CustomerOrderRow
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
            if (hasLines && allDelivered) { deliveryStatusKey = "VAS_278_DeliveryFull"; deliveryStatusFallback = "Fully delivered"; }
            else if (anyDelivered) { deliveryStatusKey = "VAS_278_DeliveryPartial"; deliveryStatusFallback = "Partial"; }
            else { deliveryStatusKey = "VAS_278_DeliveryPending"; deliveryStatusFallback = "Pending"; }
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
                    Log.Log(Level.SEVERE, "VAS_278_Top10CustomersWidget.GetDecodeMap(" + tableName + "." + columnName + ")", ex);
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

        private class TopCustomersResult
        {
            public List<CustomerRankRow> Customers { get; set; }
        }

        private class CustomerRankRow
        {
            public int CustomerId { get; set; }
            public string CustomerName { get; set; }
            public decimal OrderValue { get; set; }
            public int Rank { get; set; }
        }

        private class CustomerSummaryResult
        {
            public decimal OrderValue { get; set; }
            public int OpenOrders { get; set; }
            public int PendingDelivery { get; set; }
            public decimal? OnTimePercent { get; set; }
        }

        private class CustomerOrdersResult
        {
            public int Total { get; set; }
            public List<CustomerOrderRow> Rows { get; set; }
        }

        private class CustomerOrderRow
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
