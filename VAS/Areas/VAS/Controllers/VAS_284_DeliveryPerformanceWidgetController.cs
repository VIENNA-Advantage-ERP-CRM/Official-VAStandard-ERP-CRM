/******************************************************
 * Module Name    : VAS
 * Purpose        : Sales Order dashboard "Delivery Performance" headline + bucket widget endpoints
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
    /// Module Name : VAS_284_DeliveryPerformanceWidget
    /// Purpose     : Data endpoints for the 3x2 "Delivery Performance" widget on the
    ///               Sales Order dashboard - an on-time headline percentage plus a
    ///               four-way proportional breakdown of how Sales Orders promised in a
    ///               selected Month/Year actually landed against their promise.
    ///
    ///   Business definition per 17_Delivery_Performance_Claude_Development_Prompt.txt
    ///   (CONFIRMED - overrides the paired HTML mock/.md's "1-3 day" / "over 3 day"
    ///   bucket language, which is superseded by the 1-7 / over-7 day boundaries below):
    ///     - Cohort = Sales Orders (IsSOTrx='Y', IsSalesQuotation='N', IsReturnTrx='N',
    ///       DocStatus='CO') whose C_Order.DatePromised falls in the selected period.
    ///       This is the promise being measured, so the denominator is orders PROMISED
    ///       in the period, never orders delivered in the period.
    ///     - Full-delivery status comes from active C_OrderLine totals (QtyOrdered vs
    ///       QtyDelivered) for the order.
    ///     - For a fully delivered order the actual completion date is the LATEST
    ///       completed outbound M_InOut.MovementDate linked to that C_Order_ID (the
    ///       completing delivery, not the first - an order is only "delivered" when
    ///       every line is).
    ///     - Four buckets, always in this severity order:
    ///         1. Delivered on time    - completion date &lt;= promised date
    ///         2. Delayed 1-7 Days     - completion date is 1-7 calendar days after
    ///         3. Delayed over 7 days  - completion date is more than 7 calendar days after
    ///         4. Awaiting dispatch    - not fully delivered yet (zero or partial
    ///            delivery) so every promised order lands in exactly one bucket.
    ///     - Headline on-time % = Delivered-on-time count / all qualifying orders * 100.
    ///     - The movement chip compares the identical calculation over the previous
    ///       calendar month, expressed in PERCENTAGE POINTS, never as a percent-of-a-
    ///       percent. Omitted entirely when the previous period has no qualifying orders.
    ///     - Zero-denominator (no orders promised this period) renders as "no data",
    ///       never a 0% on-time rate - a 0% score against an empty denominator is not
    ///       a real measurement.
    ///     - Per the prompt's own CROSS-DATABASE guidance: day differences are NEVER
    ///       computed in SQL (Oracle/PostgreSQL date arithmetic differs) - the SQL
    ///       fetches only the minimal cohort (promised date, aggregate order-line
    ///       quantities, completion date) and this controller classifies the calendar-
    ///       day difference in C#.
    ///
    ///   Per the CTE/MRole rule (Prompt_Instructions.txt "Case 1"): the query isolates
    ///   a single "FROM C_Order o" WHERE-only fragment (no GROUP BY) and calls
    ///   AddAccessSQL ONLY on that isolated fragment - never on a fragment that already
    ///   carries its own GROUP BY. The qty/ship aggregates are separate CTEs joined
    ///   back to the already-filtered base_orders fragment, so access control is
    ///   inherited through that join rather than re-applied to them.
    ///   Read-only here; no writes.
    /// Chronological development:
    ///   VAI052      2026-09-14 Created
    /// </summary>
    public class VAS_284_DeliveryPerformanceWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_284_DeliveryPerformanceWidgetController).FullName);

        private const int DefaultPageSize = 10;
        private const int MaxPageSize = 10;
        private const int MaxLineRows = 200;

        // Fixed severity order - always returned/rendered in this order regardless of counts.
        private static readonly string[] BucketOrder = { "ontime", "late1_7", "late_over7", "await" };

        private static readonly Dictionary<string, string> BucketFallbackLabels = new Dictionary<string, string>
        {
            { "ontime", "Delivered on time" },
            { "late1_7", "Delayed 1-7 Days" },
            { "late_over7", "Delayed over 7 days" },
            { "await", "Awaiting dispatch" }
        };

        private static readonly Dictionary<string, string> BucketDefinitions = new Dictionary<string, string>
        {
            { "ontime", "Delivered on or before the date promised on the sales order." },
            { "late1_7", "Delivered after the promised date but within a seven-day slip." },
            { "late_over7", "Delivered more than seven days after the promised date." },
            { "await", "Order lines are not yet fully delivered, so there is no completion date to compare against the promise." }
        };

        /// <summary>
        /// The on-time headline, its movement against the previous period, and the
        /// four-bucket breakdown for the selected month.
        /// </summary>
        /// <param name="month">Zero-based month (0=Jan..11=Dec).</param>
        /// <param name="year">Calendar year.</param>
        /// <returns>JSON { TotalOrders, OnTimePercent, ChangePoints, Buckets:[ { Key, Label, OrderCount, Percent } ] } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetDeliveryPerformance(int month, int year)
        {
            if (Session["ctx"] == null) { return Json("", JsonRequestBehavior.AllowGet); }

            Ctx ctx = Session["ctx"] as Ctx;

            try
            {
                string json = JsonConvert.SerializeObject(GetDeliveryPerformanceData(ctx, month, year));
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_284_DeliveryPerformanceWidget.GetDeliveryPerformance", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// The row-click drill-down for one bucket: the stat-strip summary, that
        /// bucket's own definition sentence, and one page of its qualifying orders.
        /// </summary>
        /// <param name="bucketKey">ontime | late1_7 | late_over7 | await.</param>
        /// <param name="month">Zero-based month (0=Jan..11=Dec).</param>
        /// <param name="year">Calendar year.</param>
        /// <param name="page">Zero-based page index.</param>
        /// <param name="size">Rows per page (capped at <see cref="MaxPageSize"/>).</param>
        /// <returns>JSON { Summary:{...}, Definition, Total, Rows:[...] } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetBucketOrders(string bucketKey, int month, int year, int page = 0, int size = DefaultPageSize)
        {
            if (Session["ctx"] == null) { return Json("", JsonRequestBehavior.AllowGet); }

            Ctx ctx = Session["ctx"] as Ctx;
            if (page < 0) { page = 0; }
            if (size <= 0 || size > MaxPageSize) { size = DefaultPageSize; }

            if (string.IsNullOrEmpty(bucketKey) || !BucketFallbackLabels.ContainsKey(bucketKey)) { return ErrorResult(ctx); }

            try
            {
                string json = JsonConvert.SerializeObject(GetBucketOrdersData(ctx, bucketKey, month, year, page, size));
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_284_DeliveryPerformanceWidget.GetBucketOrders", ex);
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
                Log.Log(Level.SEVERE, "VAS_284_DeliveryPerformanceWidget.GetSalesOrderDetail", ex);
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

        /// <summary>
        /// The single physical-table fragment - one "FROM C_Order o", WHERE-only, no
        /// GROUP BY (Prompt_Instructions.txt "Case 1"). The ONLY thing AddAccessSQL is
        /// ever applied to for this widget. Cohort = firm Sales Orders promised in the
        /// given half-open period.
        /// </summary>
        private static string BuildBaseOrdersSql()
        {
            return @"
                SELECT o.C_Order_ID AS C_Order_ID,
                       o.DocumentNo AS Document_No,
                       o.DatePromised AS Date_Promised,
                       o.C_BPartner_ID AS BPartner_ID,
                       o.M_Warehouse_ID AS Warehouse_ID,
                       o.TotalLines AS Order_Value
                  FROM C_Order o
                 WHERE o.AD_Client_ID = @AD_Client_ID
                   AND o.IsActive = 'Y'
                   AND o.IsSOTrx = 'Y'
                   AND COALESCE(o.IsSalesQuotation, 'N') = 'N'
                   AND COALESCE(o.IsReturnTrx, 'N') = 'N'
                   AND o.DocStatus = 'CO'
                   AND o.DatePromised >= @Period_Start
                   AND o.DatePromised < @Period_End";
        }

        /// <summary>
        /// Fetches the minimal cohort for one period: promised date, active-line qty
        /// totals, and the completing delivery's MovementDate (if any). No day-
        /// difference math happens here - see <see cref="Classify"/>.
        /// </summary>
        private List<CohortRow> FetchCohort(Ctx ctx, DateTime periodStart, DateTime periodEnd)
        {
            List<CohortRow> rows = new List<CohortRow>();

            string baseOrdersSql = MRole.GetDefault(ctx).AddAccessSQL(BuildBaseOrdersSql(), "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string sql = @"
                WITH base_orders AS (" + baseOrdersSql + @"),
                qty AS (
                    SELECT ol.C_Order_ID AS C_Order_ID,
                           SUM(COALESCE(ol.QtyOrdered, 0)) AS Qty_Ordered,
                           SUM(COALESCE(ol.QtyDelivered, 0)) AS Qty_Delivered
                      FROM C_OrderLine ol
                      INNER JOIN base_orders bo ON ( bo.C_Order_ID = ol.C_Order_ID )
                     WHERE ol.IsActive = 'Y'
                     GROUP BY ol.C_Order_ID
                ),
                ship AS (
                    SELECT io.C_Order_ID AS C_Order_ID,
                           MAX(io.MovementDate) AS Completion_Date
                      FROM M_InOut io
                      INNER JOIN base_orders bo ON ( bo.C_Order_ID = io.C_Order_ID )
                     WHERE io.IsActive = 'Y'
                       AND io.DocStatus = 'CO'
                       AND io.IsSOTrx = 'Y'
                       AND COALESCE(io.IsReturnTrx, 'N') = 'N'
                     GROUP BY io.C_Order_ID
                )
                SELECT bo.C_Order_ID AS C_Order_ID,
                       bo.Document_No AS Document_No,
                       bo.Date_Promised AS Date_Promised,
                       bo.BPartner_ID AS BPartner_ID,
                       bo.Warehouse_ID AS Warehouse_ID,
                       bo.Order_Value AS Order_Value,
                       COALESCE(q.Qty_Ordered, 0) AS Qty_Ordered,
                       COALESCE(q.Qty_Delivered, 0) AS Qty_Delivered,
                       s.Completion_Date AS Completion_Date
                  FROM base_orders bo
                  LEFT OUTER JOIN qty q ON ( q.C_Order_ID = bo.C_Order_ID )
                  LEFT OUTER JOIN ship s ON ( s.C_Order_ID = bo.C_Order_ID )";

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
                    rows.Add(new CohortRow
                    {
                        OrderId = Util.GetValueOfInt(dr["C_Order_ID"]),
                        DocumentNo = Util.GetValueOfString(dr["Document_No"]),
                        DatePromised = dr["Date_Promised"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr["Date_Promised"]),
                        BPartnerId = dr["BPartner_ID"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["BPartner_ID"]),
                        WarehouseId = dr["Warehouse_ID"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Warehouse_ID"]),
                        OrderValue = dr["Order_Value"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Order_Value"]),
                        QtyOrdered = dr["Qty_Ordered"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Qty_Ordered"]),
                        QtyDelivered = dr["Qty_Delivered"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Qty_Delivered"]),
                        CompletionDate = dr["Completion_Date"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr["Completion_Date"])
                    });
                }
            }
            finally
            {
                CloseReader(dr);
            }

            return rows;
        }

        /// <summary>
        /// Classifies one cohort row per the CONFIRMED logic - all day-difference math
        /// happens here in C#, never in SQL (cross-database rule). Returns the bucket
        /// key and, when it exists, the calendar-day difference (completion - promised).
        /// </summary>
        private static void Classify(CohortRow row, out string bucketKey, out int? dayDiff)
        {
            if (row.QtyDelivered < row.QtyOrdered || !row.CompletionDate.HasValue)
            {
                bucketKey = "await";
                dayDiff = null;
                return;
            }

            DateTime promised = (row.DatePromised ?? row.CompletionDate.Value).Date;
            DateTime completed = row.CompletionDate.Value.Date;
            int diff = (completed - promised).Days;
            dayDiff = diff;

            if (diff <= 0) { bucketKey = "ontime"; }
            else if (diff <= 7) { bucketKey = "late1_7"; }
            else { bucketKey = "late_over7"; }
        }

        private DeliveryPerformanceResult GetDeliveryPerformanceData(Ctx ctx, int month, int year)
        {
            DeliveryPerformanceResult result = new DeliveryPerformanceResult { Buckets = new List<BucketSummary>() };
            if (ctx == null) { return result; }

            DateTime periodStart, periodEnd;
            ResolvePeriod(month, year, out periodStart, out periodEnd);
            DateTime prevPeriodStart = periodStart.AddMonths(-1);
            DateTime prevPeriodEnd = periodStart;

            List<CohortRow> current = FetchCohort(ctx, periodStart, periodEnd);
            List<CohortRow> previous = FetchCohort(ctx, prevPeriodStart, prevPeriodEnd);

            Dictionary<string, int> counts = BucketOrder.ToDictionary(k => k, k => 0);
            int ontimeCount = 0;
            foreach (CohortRow row in current)
            {
                string bucketKey;
                int? dayDiff;
                Classify(row, out bucketKey, out dayDiff);
                counts[bucketKey]++;
                if (bucketKey == "ontime") { ontimeCount++; }
            }

            int total = current.Count;
            result.TotalOrders = total;
            result.OnTimePercent = total > 0 ? (decimal?)Math.Round(ontimeCount * 100m / total, 1) : null;

            foreach (string key in BucketOrder)
            {
                int count = counts[key];
                decimal percent = total > 0 ? Math.Round(count * 100m / total, 1) : 0m;
                result.Buckets.Add(new BucketSummary
                {
                    Key = key,
                    Label = BucketFallbackLabels[key],
                    OrderCount = count,
                    Percent = percent
                });
            }

            int prevOntimeCount = 0;
            foreach (CohortRow row in previous)
            {
                string bucketKey;
                int? dayDiff;
                Classify(row, out bucketKey, out dayDiff);
                if (bucketKey == "ontime") { prevOntimeCount++; }
            }

            int prevTotal = previous.Count;
            decimal? prevOnTimePercent = prevTotal > 0 ? (decimal?)Math.Round(prevOntimeCount * 100m / prevTotal, 1) : null;

            result.ChangePoints = (result.OnTimePercent.HasValue && prevOnTimePercent.HasValue)
                ? (decimal?)Math.Round(result.OnTimePercent.Value - prevOnTimePercent.Value, 1)
                : null;

            return result;
        }

        private BucketOrdersResult GetBucketOrdersData(Ctx ctx, string bucketKey, int month, int year, int page, int size)
        {
            BucketOrdersResult result = new BucketOrdersResult { Rows = new List<BucketOrderRow>() };
            if (ctx == null) { return result; }

            DateTime periodStart, periodEnd;
            ResolvePeriod(month, year, out periodStart, out periodEnd);

            List<CohortRow> cohort = FetchCohort(ctx, periodStart, periodEnd);

            int total = cohort.Count;
            int ontimeCount = 0;
            List<ClassifiedRow> bucketRows = new List<ClassifiedRow>();

            foreach (CohortRow row in cohort)
            {
                string key;
                int? dayDiff;
                Classify(row, out key, out dayDiff);
                if (key == "ontime") { ontimeCount++; }
                if (key == bucketKey) { bucketRows.Add(new ClassifiedRow { Row = row, DayDiff = dayDiff }); }
            }

            int bucketCount = bucketRows.Count;

            result.Summary = new BucketOrdersSummary
            {
                OrderCount = bucketCount,
                SharePercent = total > 0 ? Math.Round(bucketCount * 100m / total, 1) : 0m,
                OnTimeOverallPercent = total > 0 ? (decimal?)Math.Round(ontimeCount * 100m / total, 1) : null
            };
            result.Definition = BucketDefinitions[bucketKey];
            result.Total = bucketCount;

            List<ClassifiedRow> sorted = bucketRows
                .OrderByDescending(r => r.Row.DatePromised ?? DateTime.MinValue)
                .ThenByDescending(r => r.Row.OrderId)
                .ToList();

            List<ClassifiedRow> slice = sorted.Skip(page * size).Take(size).ToList();

            Dictionary<int, string> bpartnerNames = ResolveNames("C_BPartner", slice.Select(r => r.Row.BPartnerId));
            Dictionary<int, string> warehouseNames = ResolveNames("M_Warehouse", slice.Select(r => r.Row.WarehouseId));

            foreach (ClassifiedRow cr in slice)
            {
                CohortRow row = cr.Row;
                string statusLabel;
                string statusChipClass;
                BuildStatusChip(bucketKey, cr.DayDiff, out statusLabel, out statusChipClass);

                result.Rows.Add(new BucketOrderRow
                {
                    SalesOrderId = row.OrderId,
                    SalesOrderNumber = row.DocumentNo,
                    CustomerName = bpartnerNames.ContainsKey(row.BPartnerId) ? bpartnerNames[row.BPartnerId] : "",
                    WarehouseName = warehouseNames.ContainsKey(row.WarehouseId) ? warehouseNames[row.WarehouseId] : "",
                    PromisedDate = row.DatePromised.HasValue ? row.DatePromised.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                    CompletionDate = row.CompletionDate.HasValue ? row.CompletionDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                    OrderValue = row.OrderValue,
                    StatusLabel = statusLabel,
                    StatusChipClass = statusChipClass
                });
            }

            return result;
        }

        private static void BuildStatusChip(string bucketKey, int? dayDiff, out string label, out string chipClass)
        {
            switch (bucketKey)
            {
                case "ontime":
                    label = "On time";
                    chipClass = "ok";
                    return;
                case "late1_7":
                    label = FormatDaysLate(dayDiff);
                    chipClass = "warn";
                    return;
                case "late_over7":
                    label = FormatDaysLate(dayDiff);
                    chipClass = "risk";
                    return;
                default:
                    label = "Awaiting dispatch";
                    chipClass = "neutral";
                    return;
            }
        }

        private static string FormatDaysLate(int? dayDiff)
        {
            int d = dayDiff ?? 0;
            return d == 1 ? "1 day late" : d + " days late";
        }

        /// <summary>Small purpose-specific name lookup for exactly the ids on the current result page (never the full cohort).</summary>
        private Dictionary<int, string> ResolveNames(string tableName, IEnumerable<int> ids)
        {
            Dictionary<int, string> map = new Dictionary<int, string>();
            List<int> distinctIds = ids.Where(id => id > 0).Distinct().ToList();
            if (distinctIds.Count == 0) { return map; }

            List<string> placeholders = new List<string>();
            List<SqlParameter> parameters = new List<SqlParameter>();
            for (int i = 0; i < distinctIds.Count; i++)
            {
                string pName = "@Id" + i;
                placeholders.Add(pName);
                parameters.Add(new SqlParameter(pName, distinctIds[i]));
            }

            string idColumn = tableName + "_ID";
            string sql = "SELECT " + idColumn + " AS Id, Name AS Name FROM " + tableName + " WHERE " + idColumn + " IN (" + string.Join(", ", placeholders) + ")";

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, parameters.ToArray());
                while (dr != null && dr.Read())
                {
                    map[Util.GetValueOfInt(dr["Id"])] = Util.GetValueOfString(dr["Name"]);
                }
            }
            finally
            {
                CloseReader(dr);
            }

            return map;
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
            if (hasLines && allDelivered) { deliveryStatusKey = "VAS_284_DeliveryFull"; deliveryStatusFallback = "Delivered"; }
            else if (anyDelivered) { deliveryStatusKey = "VAS_284_DeliveryPartial"; deliveryStatusFallback = "Partially delivered"; }
            else { deliveryStatusKey = "VAS_284_DeliveryNone"; deliveryStatusFallback = "Not delivered"; }
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
                    Log.Log(Level.SEVERE, "VAS_284_DeliveryPerformanceWidget.GetDecodeMap(" + tableName + "." + columnName + ")", ex);
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

        private class CohortRow
        {
            public int OrderId { get; set; }
            public string DocumentNo { get; set; }
            public DateTime? DatePromised { get; set; }
            public int BPartnerId { get; set; }
            public int WarehouseId { get; set; }
            public decimal OrderValue { get; set; }
            public decimal QtyOrdered { get; set; }
            public decimal QtyDelivered { get; set; }
            public DateTime? CompletionDate { get; set; }
        }

        private class ClassifiedRow
        {
            public CohortRow Row { get; set; }
            public int? DayDiff { get; set; }
        }

        private class DeliveryPerformanceResult
        {
            public int TotalOrders { get; set; }
            public decimal? OnTimePercent { get; set; }
            public decimal? ChangePoints { get; set; }
            public List<BucketSummary> Buckets { get; set; }
        }

        private class BucketSummary
        {
            public string Key { get; set; }
            public string Label { get; set; }
            public int OrderCount { get; set; }
            public decimal Percent { get; set; }
        }

        private class BucketOrdersResult
        {
            public BucketOrdersSummary Summary { get; set; }
            public string Definition { get; set; }
            public int Total { get; set; }
            public List<BucketOrderRow> Rows { get; set; }
        }

        private class BucketOrdersSummary
        {
            public int OrderCount { get; set; }
            public decimal SharePercent { get; set; }
            public decimal? OnTimeOverallPercent { get; set; }
        }

        private class BucketOrderRow
        {
            public int SalesOrderId { get; set; }
            public string SalesOrderNumber { get; set; }
            public string CustomerName { get; set; }
            public string WarehouseName { get; set; }
            public string PromisedDate { get; set; }
            public string CompletionDate { get; set; }
            public decimal OrderValue { get; set; }
            public string StatusLabel { get; set; }
            public string StatusChipClass { get; set; }
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
