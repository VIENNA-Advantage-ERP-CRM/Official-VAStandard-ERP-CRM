/******************************************************
 * Module Name    : VAS
 * Purpose        : Sales Order dashboard "Category Wise SO" donut widget endpoints
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
    /// Module Name : VAS_281_CategoryWiseSOWidget
    /// Purpose     : Data endpoints for the 3x2 "Category Wise SO" donut + legend
    ///               widget on the Sales Order dashboard - share of booked
    ///               (DocStatus IN ('CO','CL')) Sales Order value by product
    ///               category (M_Product.M_Product_Category_ID, the direct category -
    ///               no parent-hierarchy roll-up), for a selected Month/Year. Value is
    ///               attributed at LINE level (C_OrderLine.LineNetAmt), so one order
    ///               can legitimately contribute to several categories. (1) the
    ///               already-combined top-4-plus-"Other" segment list plus the period
    ///               total, and (2) a category (or "Other" bucket) drill-down - stat
    ///               strip plus the Sales Orders whose lines fall in that category
    ///               set, and (3) the shared Sales Order record-preview data (header
    ///               stats + paginated lines) for record/lines drill-through inside
    ///               that SO table.
    ///
    ///   Business definition per
    ///   14_Category_Wise_SO_Claude_Development_Prompt.txt: category is the DIRECT
    ///   product category only (M_Product -&gt; M_Product_Category), sorted
    ///   descending by value in service code, top 4 real categories shown
    ///   individually and every remaining category combined into one explicit
    ///   "Other" segment (never more than 5 segments total). Percent = category
    ///   value / total value, rounded for display with the rounding residue
    ///   assigned to the largest segment so the legend always sums to exactly 100.
    ///   "Other" carries the underlying M_Product_Category_ID set so its drill-down
    ///   never uses a fake/hard-coded category id.
    ///
    ///   Per the CTE/MRole rule (Prompt_Instructions.txt "Case 1"): every query here
    ///   isolates a single-FROM ("FROM C_Order o" with simple JOINs to lookup
    ///   tables), WHERE-only fragment and calls AddAccessSQL ONLY on that isolated
    ///   fragment - never on a fragment that already carries its own GROUP BY (the
    ///   VAS_270/VAS_277 lesson: appending the access predicate after a trailing
    ///   GROUP BY produces invalid SQL). All GROUP BY/JOIN aggregation happens in the
    ///   outer CTEs built around the already-filtered text.
    ///   Read-only here; no writes.
    /// Chronological development:
    ///   VAI052      2026-09-14 Created
    /// </summary>
    public class VAS_281_CategoryWiseSOWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_281_CategoryWiseSOWidgetController).FullName);

        private const int TopCategoryCount = 4;
        private const int DefaultPageSize = 10;
        private const int MaxPageSize = 10;
        private const int MaxLineRows = 200;

        /// <summary>
        /// The already-combined top-4-plus-"Other" category mix for the selected
        /// month, plus the period's total booked line value (the centre-label figure).
        /// </summary>
        /// <param name="month">Zero-based month (0=Jan..11=Dec).</param>
        /// <param name="year">Calendar year.</param>
        /// <returns>JSON { TotalValue, Categories:[ { CategoryIds, CategoryName, Value, Percent, IsOther } ] } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetCategoryMix(int month, int year)
        {
            if (Session["ctx"] == null) { return Json("", JsonRequestBehavior.AllowGet); }

            Ctx ctx = Session["ctx"] as Ctx;

            try
            {
                string json = JsonConvert.SerializeObject(GetCategoryMixData(ctx, month, year));
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_281_CategoryWiseSOWidget.GetCategoryMix", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// The drill-down for a segment (a real category, or the combined "Other"
        /// bucket - both are just a set of M_Product_Category_ID values): the 4-card
        /// stat strip plus one page of the qualifying Sales Orders.
        /// </summary>
        /// <param name="categoryIds">Comma-separated M_Product_Category_ID list (one id for a real category, several for "Other").</param>
        /// <param name="month">Zero-based month (0=Jan..11=Dec).</param>
        /// <param name="year">Calendar year.</param>
        /// <param name="page">Zero-based page index.</param>
        /// <param name="size">Rows per page (capped at <see cref="MaxPageSize"/>).</param>
        /// <returns>JSON { Summary:{...}, Total, Rows:[...] } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetCategoryOrders(string categoryIds, int month, int year, int page = 0, int size = DefaultPageSize)
        {
            if (Session["ctx"] == null) { return Json("", JsonRequestBehavior.AllowGet); }

            Ctx ctx = Session["ctx"] as Ctx;
            if (page < 0) { page = 0; }
            if (size <= 0 || size > MaxPageSize) { size = DefaultPageSize; }

            List<int> ids = ParseCategoryIds(categoryIds);
            if (ids.Count == 0) { return ErrorResult(ctx); }

            try
            {
                string json = JsonConvert.SerializeObject(GetCategoryOrdersData(ctx, ids, month, year, page, size));
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_281_CategoryWiseSOWidget.GetCategoryOrders", ex);
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
                Log.Log(Level.SEVERE, "VAS_281_CategoryWiseSOWidget.GetSalesOrderDetail", ex);
                return ErrorResult(ctx);
            }
        }

        private static List<int> ParseCategoryIds(string categoryIds)
        {
            List<int> ids = new List<int>();
            if (string.IsNullOrWhiteSpace(categoryIds)) { return ids; }

            foreach (string part in categoryIds.Split(','))
            {
                int id;
                if (int.TryParse(part.Trim(), out id) && id > 0) { ids.Add(id); }
            }
            return ids;
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
        /// The single physical-table fragment for the category-mix aggregate - one
        /// "FROM C_Order o" with simple JOINs to its own lines, WHERE-only, no
        /// GROUP BY (Prompt_Instructions.txt "Case 1"). The ONLY thing AddAccessSQL is
        /// ever applied to for this query.
        /// </summary>
        private static string BuildBaseAllLinesSql()
        {
            return @"
                SELECT ol.M_Product_ID AS M_Product_ID,
                       ol.LineNetAmt AS Line_Net_Amt
                  FROM C_Order o
                  INNER JOIN C_OrderLine ol ON ( ol.C_Order_ID = o.C_Order_ID AND ol.IsActive = 'Y' )
                 WHERE o.AD_Client_ID = @AD_Client_ID
                   AND o.IsActive = 'Y'
                   AND o.IsSOTrx = 'Y'
                   AND COALESCE(o.IsSalesQuotation, 'N') = 'N'
                   AND COALESCE(o.IsReturnTrx, 'N') = 'N'
                   AND o.DocStatus IN ('CO', 'CL')
                   AND o.DateOrdered >= @Period_Start
                   AND o.DateOrdered < @Period_End";
        }

        private CategoryMixResult GetCategoryMixData(Ctx ctx, int month, int year)
        {
            CategoryMixResult result = new CategoryMixResult { Categories = new List<CategorySegment>() };
            if (ctx == null) { return result; }

            DateTime periodStart, periodEnd;
            ResolvePeriod(month, year, out periodStart, out periodEnd);

            SqlParameter[] periodParams = new[]
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@Period_Start", periodStart),
                new SqlParameter("@Period_End", periodEnd)
            };

            string baseLinesSql = MRole.GetDefault(ctx).AddAccessSQL(BuildBaseAllLinesSql(), "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string sql = @"
                WITH base_lines AS (" + baseLinesSql + @")
                SELECT
                    p.M_Product_Category_ID AS Category_Id,
                    pc.Name AS Category_Name,
                    SUM(COALESCE(bl.Line_Net_Amt, 0)) AS Category_Value
                  FROM base_lines bl
                  INNER JOIN M_Product p ON ( p.M_Product_ID = bl.M_Product_ID )
                  INNER JOIN M_Product_Category pc ON ( pc.M_Product_Category_ID = p.M_Product_Category_ID )
                 GROUP BY p.M_Product_Category_ID, pc.Name
                 ORDER BY SUM(COALESCE(bl.Line_Net_Amt, 0)) DESC";

            List<RawCategory> raw = new List<RawCategory>();
            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, periodParams);
                while (dr != null && dr.Read())
                {
                    raw.Add(new RawCategory
                    {
                        CategoryId = Util.GetValueOfInt(dr["Category_Id"]),
                        CategoryName = Util.GetValueOfString(dr["Category_Name"]),
                        Value = dr["Category_Value"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Category_Value"])
                    });
                }
            }
            finally
            {
                CloseReader(dr);
            }

            decimal totalValue = raw.Sum(r => r.Value);
            result.TotalValue = totalValue;

            // Top 4 real categories individually, everything else combined into one
            // explicit "Other" segment carrying its own underlying category id set -
            // never a hard-coded fake id.
            List<RawCategory> top = raw.Take(TopCategoryCount).ToList();
            List<RawCategory> rest = raw.Skip(TopCategoryCount).ToList();

            foreach (RawCategory r in top)
            {
                result.Categories.Add(new CategorySegment
                {
                    CategoryIds = new List<int> { r.CategoryId },
                    CategoryName = r.CategoryName,
                    Value = r.Value,
                    IsOther = false
                });
            }

            if (rest.Count > 0)
            {
                result.Categories.Add(new CategorySegment
                {
                    CategoryIds = rest.Select(r => r.CategoryId).ToList(),
                    CategoryName = Msg.GetMsg(ctx, "VAS_281_OtherCategory") ?? "Other",
                    Value = rest.Sum(r => r.Value),
                    IsOther = true
                });
            }

            ApplyRoundedPercentages(result.Categories, totalValue);

            return result;
        }

        /// <summary>
        /// Rounds each segment's share to a whole percent and assigns the rounding
        /// residue to the largest segment so the displayed percentages always sum to
        /// exactly 100 (Prompt_Instructions.txt rule 8) - never left to drift.
        /// </summary>
        private static void ApplyRoundedPercentages(List<CategorySegment> segments, decimal totalValue)
        {
            if (segments.Count == 0) { return; }
            if (totalValue <= 0)
            {
                foreach (CategorySegment s in segments) { s.Percent = 0; }
                return;
            }

            int sum = 0;
            int largestIndex = 0;
            decimal largestValue = -1m;
            for (int i = 0; i < segments.Count; i++)
            {
                int pct = (int)Math.Round((segments[i].Value / totalValue) * 100m, MidpointRounding.AwayFromZero);
                segments[i].Percent = pct;
                sum += pct;
                if (segments[i].Value > largestValue) { largestValue = segments[i].Value; largestIndex = i; }
            }

            int residue = 100 - sum;
            if (residue != 0) { segments[largestIndex].Percent += residue; }
        }

        private CategoryOrdersResult GetCategoryOrdersData(Ctx ctx, List<int> categoryIds, int month, int year, int page, int size)
        {
            CategoryOrdersResult result = new CategoryOrdersResult { Rows = new List<OrderRow>() };
            if (ctx == null) { return result; }

            DateTime periodStart, periodEnd;
            ResolvePeriod(month, year, out periodStart, out periodEnd);

            SqlParameter[] categoryIdParams = new SqlParameter[categoryIds.Count];
            string[] categoryIdNames = new string[categoryIds.Count];
            for (int i = 0; i < categoryIds.Count; i++)
            {
                categoryIdNames[i] = "@Cat" + i;
                categoryIdParams[i] = new SqlParameter(categoryIdNames[i], categoryIds[i]);
            }
            string categoryInClause = string.Join(", ", categoryIdNames);

            // The single physical-table fragment for this drill-down - one
            // "FROM C_Order o" with simple equi-JOINs only (no filter inside a JOIN's
            // ON clause - keeping the category test in WHERE, textually AFTER
            // @AD_Client_ID/@Period_Start/@Period_End, matters here: this DB layer
            // binds parameters POSITIONALLY, matching left-to-right occurrence in the
            // SQL text rather than by name, so the bind array below must list
            // @AD_Client_ID/@Period_Start/@Period_End before the @Cat* entries or
            // every slot after the mismatch binds the wrong value/type - exactly how
            // an earlier version of this query surfaced as
            // "ORA-00932: inconsistent datatypes: expected DATE got NUMBER"). WHERE-
            // only, no GROUP BY. The ONLY thing AddAccessSQL is ever applied to for
            // either query below (a fresh call per statement).
            Func<string> buildBaseCategoryLinesSql = () => @"
                SELECT o.C_Order_ID AS C_Order_ID,
                       o.DocumentNo AS Document_No,
                       o.DateOrdered AS Date_Ordered,
                       o.TotalLines AS Order_Value,
                       o.DocStatus AS Doc_Status_Code,
                       o.C_BPartner_ID AS Bpartner_Id,
                       o.M_Warehouse_ID AS Warehouse_Id,
                       o.SalesRep_ID AS Sales_Rep_Id,
                       ol.LineNetAmt AS Line_Net_Amt
                  FROM C_Order o
                  INNER JOIN C_OrderLine ol ON ( ol.C_Order_ID = o.C_Order_ID AND ol.IsActive = 'Y' )
                  INNER JOIN M_Product p ON ( p.M_Product_ID = ol.M_Product_ID )
                 WHERE o.AD_Client_ID = @AD_Client_ID
                   AND o.IsActive = 'Y'
                   AND o.IsSOTrx = 'Y'
                   AND COALESCE(o.IsSalesQuotation, 'N') = 'N'
                   AND COALESCE(o.IsReturnTrx, 'N') = 'N'
                   AND o.DocStatus IN ('CO', 'CL')
                   AND o.DateOrdered >= @Period_Start
                   AND o.DateOrdered < @Period_End
                   AND p.M_Product_Category_ID IN (" + categoryInClause + @")";

            List<SqlParameter> periodAndCategoryParams = new List<SqlParameter>
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@Period_Start", periodStart),
                new SqlParameter("@Period_End", periodEnd)
            };
            periodAndCategoryParams.AddRange(categoryIdParams);

            // Query 1 of 2 - the 4-card stat-strip summary (value/order/customer
            // counts) plus the period's total value (for the Share % card), kept
            // small and purpose-specific rather than one giant statement.
            string baseLinesSql1 = MRole.GetDefault(ctx).AddAccessSQL(buildBaseCategoryLinesSql(), "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            string summarySql = @"
                WITH base_lines AS (" + baseLinesSql1 + @")
                SELECT
                    SUM(COALESCE(Line_Net_Amt, 0)) AS Category_Value,
                    COUNT(DISTINCT C_Order_ID) AS Order_Count,
                    COUNT(DISTINCT Bpartner_Id) AS Customer_Count
                  FROM base_lines";

            decimal categoryValue = 0m;
            int orderCount = 0, customerCount = 0;

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(summarySql, periodAndCategoryParams.ToArray());
                if (dr != null && dr.Read())
                {
                    categoryValue = dr["Category_Value"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Category_Value"]);
                    orderCount = dr["Order_Count"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Order_Count"]);
                    customerCount = dr["Customer_Count"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Customer_Count"]);
                }
            }
            finally
            {
                CloseReader(dr);
            }

            decimal totalValue = GetTotalLineValue(ctx, periodStart, periodEnd);
            decimal percent = totalValue > 0 ? Math.Round((categoryValue / totalValue) * 100m, MidpointRounding.AwayFromZero) : 0m;

            result.Summary = new CategorySummary
            {
                Value = categoryValue,
                Percent = percent,
                OrderCount = orderCount,
                CustomerCount = customerCount
            };

            // Query 2 of 2 - the paginated Sales Orders list, a fresh AddAccessSQL
            // pass (fresh base_lines text) matching every other widget's per-query
            // pattern. line_stats sums ALL active lines of the order (not just the
            // category-matching ones) for the standard delivery-status derivation,
            // consistent with every other widget's SO table.
            string baseLinesSql2 = MRole.GetDefault(ctx).AddAccessSQL(buildBaseCategoryLinesSql(), "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string listSql = @"
                WITH base_lines AS (" + baseLinesSql2 + @"),
                orders AS (
                    SELECT C_Order_ID, Document_No, Date_Ordered, Order_Value, Doc_Status_Code, Bpartner_Id, Warehouse_Id, Sales_Rep_Id
                      FROM base_lines
                     GROUP BY C_Order_ID, Document_No, Date_Ordered, Order_Value, Doc_Status_Code, Bpartner_Id, Warehouse_Id, Sales_Rep_Id
                ),
                line_stats AS (
                    SELECT
                        ol.C_Order_ID AS C_Order_ID,
                        SUM(COALESCE(ol.QtyOrdered, 0)) AS Qty_Ordered,
                        SUM(COALESCE(ol.QtyDelivered, 0)) AS Qty_Delivered
                      FROM C_OrderLine ol
                     WHERE ol.IsActive = 'Y'
                     GROUP BY ol.C_Order_ID
                )
                SELECT
                    o.C_Order_ID AS Order_Id,
                    o.Document_No AS Document_No,
                    o.Date_Ordered AS Date_Ordered,
                    o.Order_Value AS Order_Value,
                    o.Doc_Status_Code AS Doc_Status_Code,
                    COALESCE(bp.Name, N'') AS Customer_Name,
                    COALESCE(wh.Name, N'') AS Warehouse_Name,
                    COALESCE(rep.Name, N'') AS Representative_Name,
                    COALESCE(ls.Qty_Ordered, 0) AS Qty_Ordered,
                    COALESCE(ls.Qty_Delivered, 0) AS Qty_Delivered,
                    COUNT(1) OVER () AS Total_Rows
                  FROM orders o
                  INNER JOIN C_BPartner bp ON ( bp.C_BPartner_ID = o.Bpartner_Id )
                  LEFT OUTER JOIN M_Warehouse wh ON ( wh.M_Warehouse_ID = o.Warehouse_Id )
                  LEFT OUTER JOIN AD_User rep ON ( rep.AD_User_ID = o.Sales_Rep_Id )
                  LEFT OUTER JOIN line_stats ls ON ( ls.C_Order_ID = o.C_Order_ID )
                 ORDER BY o.Date_Ordered DESC, o.Document_No DESC
                 OFFSET @Row_Offset ROWS FETCH NEXT @Page_Size ROWS ONLY";

            List<SqlParameter> listParams = new List<SqlParameter>(periodAndCategoryParams)
            {
                new SqlParameter("@Row_Offset", page * size),
                new SqlParameter("@Page_Size", size)
            };

            string language = GetLanguage(ctx);
            Dictionary<string, string> docStatusMap = GetDecodeMap("C_Order", "DocStatus", language);

            IDataReader dr2 = null;
            try
            {
                dr2 = DB.ExecuteReader(listSql, listParams.ToArray());

                int total = 0;
                while (dr2 != null && dr2.Read())
                {
                    total = dr2["Total_Rows"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr2["Total_Rows"]);
                    DateTime? dateOrdered = dr2["Date_Ordered"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr2["Date_Ordered"]);
                    string docStatusCode = Util.GetValueOfString(dr2["Doc_Status_Code"]);
                    decimal qtyOrdered = dr2["Qty_Ordered"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr2["Qty_Ordered"]);
                    decimal qtyDelivered = dr2["Qty_Delivered"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr2["Qty_Delivered"]);

                    string deliveryCode, deliveryFallback, deliveryStatusCode;
                    if (qtyOrdered > 0 && qtyDelivered >= qtyOrdered) { deliveryCode = "VAS_281_DeliveryFull"; deliveryFallback = "Fully delivered"; deliveryStatusCode = "FULL"; }
                    else if (qtyDelivered > 0) { deliveryCode = "VAS_281_DeliveryPartial"; deliveryFallback = "Partial"; deliveryStatusCode = "PARTIAL"; }
                    else { deliveryCode = "VAS_281_DeliveryPending"; deliveryFallback = "Pending"; deliveryStatusCode = "PENDING"; }

                    result.Rows.Add(new OrderRow
                    {
                        SalesOrderId = Util.GetValueOfInt(dr2["Order_Id"]),
                        SalesOrderNumber = Util.GetValueOfString(dr2["Document_No"]),
                        SalesOrderDate = dateOrdered.HasValue ? dateOrdered.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                        CustomerName = Util.GetValueOfString(dr2["Customer_Name"]),
                        WarehouseName = Util.GetValueOfString(dr2["Warehouse_Name"]),
                        RepresentativeName = Util.GetValueOfString(dr2["Representative_Name"]),
                        OrderValue = dr2["Order_Value"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr2["Order_Value"]),
                        DocumentStatus = DecodeLabel(docStatusMap, docStatusCode),
                        DeliveryStatus = Msg.GetMsg(ctx, deliveryCode) ?? deliveryFallback,
                        DeliveryStatusCode = deliveryStatusCode
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

        /// <summary>The period's total booked line value across every category - the same total as GetCategoryMixData's centre-label figure, recomputed here so the Share % on this drill-down is always internally consistent.</summary>
        private decimal GetTotalLineValue(Ctx ctx, DateTime periodStart, DateTime periodEnd)
        {
            string baseLinesSql = MRole.GetDefault(ctx).AddAccessSQL(BuildBaseAllLinesSql(), "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string sql = @"
                WITH base_lines AS (" + baseLinesSql + @")
                SELECT SUM(COALESCE(Line_Net_Amt, 0)) AS Total_Value FROM base_lines";

            return Util.GetValueOfDecimal(DB.ExecuteScalar(sql, new[]
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@Period_Start", periodStart),
                new SqlParameter("@Period_End", periodEnd)
            }, null));
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
            if (hasLines && allDelivered) { deliveryStatusKey = "VAS_281_DeliveryFull"; deliveryStatusFallback = "Fully delivered"; }
            else if (anyDelivered) { deliveryStatusKey = "VAS_281_DeliveryPartial"; deliveryStatusFallback = "Partial"; }
            else { deliveryStatusKey = "VAS_281_DeliveryPending"; deliveryStatusFallback = "Pending"; }
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
                    Log.Log(Level.SEVERE, "VAS_281_CategoryWiseSOWidget.GetDecodeMap(" + tableName + "." + columnName + ")", ex);
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

        private class RawCategory
        {
            public int CategoryId { get; set; }
            public string CategoryName { get; set; }
            public decimal Value { get; set; }
        }

        private class CategoryMixResult
        {
            public decimal TotalValue { get; set; }
            public List<CategorySegment> Categories { get; set; }
        }

        private class CategorySegment
        {
            public List<int> CategoryIds { get; set; }
            public string CategoryName { get; set; }
            public decimal Value { get; set; }
            public int Percent { get; set; }
            public bool IsOther { get; set; }
        }

        private class CategoryOrdersResult
        {
            public CategorySummary Summary { get; set; }
            public int Total { get; set; }
            public List<OrderRow> Rows { get; set; }
        }

        private class CategorySummary
        {
            public decimal Value { get; set; }
            public decimal Percent { get; set; }
            public int OrderCount { get; set; }
            public int CustomerCount { get; set; }
        }

        private class OrderRow
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
