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
    [System.Web.Mvc.SessionState(System.Web.SessionState.SessionStateBehavior.ReadOnly)]
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
                CategoryMixResult data = GetCategoryMixData(ctx, month, year);
                FillBaseCurrency(ctx, data);
                string json = JsonConvert.SerializeObject(data);
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
        /// VAS163 2026-11-25: Sr 275 the order's tax total (C_OrderTax.TaxAmt) - alias "o" = C_Order.
        /// </summary>
        private const string OrderTaxSumSql =
            "(SELECT COALESCE(SUM(COALESCE(ot.TaxAmt, 0)), 0) FROM C_OrderTax ot WHERE ot.C_Order_ID = o.C_Order_ID AND ot.IsActive = 'Y')";

        /// <summary>
        /// VAS163 2026-11-25: Sr 275 SO value WITHOUT tax - totals-based tax-inclusive rule of VAS_271 /
        /// VAS_288 (the tax is inside TotalLines when 2 * (GrandTotal - TotalLines) &lt; SUM(C_OrderTax.TaxAmt)).
        /// </summary>
        private const string OrderValueExclTaxSql =
            "CASE WHEN 2 * (COALESCE(o.GrandTotal, 0) - COALESCE(o.TotalLines, 0)) < " + OrderTaxSumSql +
            " THEN COALESCE(o.TotalLines, 0) - " + OrderTaxSumSql + " ELSE COALESCE(o.TotalLines, 0) END";

        /// <summary>
        /// VAS163 2026-11-25: Sr 267 / 275 header columns every base-lines fragment carries (alias "o"):
        /// totals and tax for the ex-tax rule, currency / date / conversion type / client / org for
        /// currencyConvert. Plain columns and one scalar sub query - no JOIN, so AddAccessSQL is unaffected.
        /// </summary>
        private const string OrderValuationColumnsSql = @"
                       o.TotalLines AS Total_Lines,
                       o.GrandTotal AS Grand_Total,
                       " + OrderTaxSumSql + @" AS Order_Tax,
                       o.C_Currency_ID AS Line_Currency_Id,
                       o.C_ConversionType_ID AS Conversion_Type_Id,
                       COALESCE(o.DateAcct, o.DateOrdered) AS Date_Acct,
                       o.AD_Client_ID AS Client_Id,
                       o.AD_Org_ID AS Org_Id";

        /// <summary>
        /// VAS163 2026-11-25: Sr 267 / 275 a base-lines row's line amount without tax, in the base
        /// (accounting-schema) currency. On a tax-inclusive order (rule of OrderValueExclTaxSql) the
        /// line's share of the order tax is taken off pro rata (LineNetAmt * (TotalLines - tax) / TotalLines),
        /// so the lines of an order add up to its ex-tax SO value; then the amount is converted with the
        /// configured rates (currencyConvert, arguments as VAS_282 / VAS_271) - never summed across
        /// currencies. The base currency id is an int read from the DB, inlined like VAS_282
        /// (this DB layer binds parameters positionally). 0 = not resolvable -> unconverted.
        /// </summary>
        /// <param name="baseCurrencyId">Base currency id or 0.</param>
        /// <returns>SQL expression on alias "bl" (a base_lines CTE row).</returns>
        private static string BaseLineAmountSql(int baseCurrencyId)
        {
            string exTax = "(CASE WHEN COALESCE(bl.Total_Lines, 0) <> 0 AND 2 * (COALESCE(bl.Grand_Total, 0) - COALESCE(bl.Total_Lines, 0)) < COALESCE(bl.Order_Tax, 0)" +
                " THEN COALESCE(bl.Line_Net_Amt, 0) * (bl.Total_Lines - bl.Order_Tax) / bl.Total_Lines ELSE COALESCE(bl.Line_Net_Amt, 0) END)";
            if (baseCurrencyId <= 0) { return "ROUND(" + exTax + ", 6)"; }
            return "ROUND(CASE WHEN bl.Line_Currency_Id IS NULL OR bl.Line_Currency_Id = " + baseCurrencyId + " THEN " + exTax +
                " ELSE COALESCE(currencyConvert(" + exTax + ", bl.Line_Currency_Id, " + baseCurrencyId + ", bl.Date_Acct, bl.Conversion_Type_Id, bl.Client_Id, bl.Org_Id), 0) END, 6)";
        }

        /// <summary>
        /// VAS163 2026-11-25: Sr 267 base (accounting-schema) currency id of the login client
        /// (AD_ClientInfo.C_AcctSchema1_ID -> C_AcctSchema.C_Currency_ID, as VAS_282). 0 when not resolvable.
        /// </summary>
        /// <param name="ctx">Context.</param>
        /// <returns>C_Currency_ID or 0.</returns>
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
                Log.Log(Level.WARNING, "VAS_281_CategoryWiseSOWidget.GetBaseCurrencyId", ex);
                return 0;
            }
        }

        /// <summary>
        /// The single physical-table fragment for the category-mix aggregate - one
        /// "FROM C_Order o" with simple JOINs to its own lines, WHERE-only, no
        /// GROUP BY (Prompt_Instructions.txt "Case 1"). The ONLY thing AddAccessSQL is
        /// ever applied to for this query.
        /// </summary>
        private static string BuildBaseAllLinesSql()
        {
            // VAS163 2026-11-25: Sr 267 / 275 the order's currency, conversion data and tax totals are
            // carried so BaseLineAmountSql can take the tax off and convert to the base currency.
            return @"
                SELECT ol.M_Product_ID AS M_Product_ID,
                       ol.LineNetAmt AS Line_Net_Amt," + OrderValuationColumnsSql + @"
                  FROM C_Order o
                  INNER JOIN C_OrderLine ol ON ( ol.C_Order_ID = o.C_Order_ID AND ol.IsActive = 'Y' )
                 WHERE o.AD_Client_ID = @AD_Client_ID
                   AND o.IsActive = 'Y'
                   AND o.IsSOTrx = 'Y'
                   AND EXISTS (SELECT 1 FROM C_DocType dt WHERE dt.C_DocType_ID = o.C_DocTypeTarget_ID AND dt.DocSubTypeSO IN ('SO', 'WR', 'BO'))
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

            // VAS163 2026-11-25: Sr 266 / 267 / 275 category value = ex-tax line amounts in the base currency.
            string lineAmountSql = BaseLineAmountSql(GetBaseCurrencyId(ctx));
            string sql = @"
                WITH base_lines AS (" + baseLinesSql + @")
                SELECT
                    p.M_Product_Category_ID AS Category_Id,
                    pc.Name AS Category_Name,
                    SUM(" + lineAmountSql + @") AS Category_Value
                  FROM base_lines bl
                  INNER JOIN M_Product p ON ( p.M_Product_ID = bl.M_Product_ID )
                  INNER JOIN M_Product_Category pc ON ( pc.M_Product_Category_ID = p.M_Product_Category_ID )
                 GROUP BY p.M_Product_Category_ID, pc.Name
                 ORDER BY SUM(" + lineAmountSql + @") DESC";

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
                    CategoryName = Msg.GetMsg(ctx, "VAS_281_OtherCategory", true) ?? "Other", // VAS163 2026-11-11: text-only label (3-arg GetMsg, no tip appended)
                    Value = rest.Sum(r => r.Value),
                    IsOther = true
                });
            }

            ApplyRoundedPercentages(result.Categories, totalValue);

            return result;
        }

        /// <summary>
        /// Rounds each segment's share and assigns the rounding residue to the largest
        /// segment so the displayed percentages always sum to exactly 100
        /// (Prompt_Instructions.txt rule 8) - never left to drift.
        /// VAS163 2026-11-25: Sr 289 shares to ONE decimal (residue in tenths), not whole percents.
        /// </summary>
        private static void ApplyRoundedPercentages(List<CategorySegment> segments, decimal totalValue)
        {
            if (segments.Count == 0) { return; }
            if (totalValue <= 0)
            {
                foreach (CategorySegment s in segments) { s.Percent = 0m; }
                return;
            }

            decimal sum = 0m;
            int largestIndex = 0;
            decimal largestValue = -1m;
            for (int i = 0; i < segments.Count; i++)
            {
                decimal pct = Math.Round((segments[i].Value / totalValue) * 100m, 1, MidpointRounding.AwayFromZero);
                segments[i].Percent = pct;
                sum += pct;
                if (segments[i].Value > largestValue) { largestValue = segments[i].Value; largestIndex = i; }
            }

            decimal residue = 100m - sum;
            if (residue != 0m) { segments[largestIndex].Percent += residue; }
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
                       " + OrderValueExclTaxSql + @" AS Order_Value,
                       o.DocStatus AS Doc_Status_Code,
                       o.C_BPartner_ID AS Bpartner_Id,
                       o.M_Warehouse_ID AS Warehouse_Id,
                       o.SalesRep_ID AS Sales_Rep_Id,
                       o.C_Currency_ID AS Currency_Id,
                       ol.LineNetAmt AS Line_Net_Amt," + OrderValuationColumnsSql + @"
                  FROM C_Order o
                  INNER JOIN C_OrderLine ol ON ( ol.C_Order_ID = o.C_Order_ID AND ol.IsActive = 'Y' )
                  INNER JOIN M_Product p ON ( p.M_Product_ID = ol.M_Product_ID )
                 WHERE o.AD_Client_ID = @AD_Client_ID
                   AND o.IsActive = 'Y'
                   AND o.IsSOTrx = 'Y'
                   AND EXISTS (SELECT 1 FROM C_DocType dt WHERE dt.C_DocType_ID = o.C_DocTypeTarget_ID AND dt.DocSubTypeSO IN ('SO', 'WR', 'BO'))
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
            // VAS163 2026-11-25: Sr 267 / 275 Category value = ex-tax line amounts in the base currency
            // (was a plain SUM(LineNetAmt) adding up different currencies, tax included).
            int baseCurrencyId = GetBaseCurrencyId(ctx);
            string summarySql = @"
                WITH base_lines AS (" + baseLinesSql1 + @")
                SELECT
                    SUM(" + BaseLineAmountSql(baseCurrencyId) + @") AS Category_Value,
                    COUNT(DISTINCT bl.C_Order_ID) AS Order_Count,
                    COUNT(DISTINCT bl.Bpartner_Id) AS Customer_Count
                  FROM base_lines bl";

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

            decimal totalValue = GetTotalLineValue(ctx, periodStart, periodEnd, baseCurrencyId);
            // VAS163 2026-11-25: Sr 289 share with one decimal.
            decimal percent = totalValue > 0 ? Math.Round((categoryValue / totalValue) * 100m, 1, MidpointRounding.AwayFromZero) : 0m;

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

            // VAS163 2026-11-11: Row 93 - Currency_Id (document / price list currency) carried
            // through to the list so the Value column shows the order's own currency.
            // VAS163 2026-11-25: Sr 270 / 275 Order_Value is the ex-tax SO value; the delivery status
            // counts Item-type product lines only (charges / services left out), as the lines pop-up.
            string listSql = @"
                WITH base_lines AS (" + baseLinesSql2 + @"),
                orders AS (
                    SELECT C_Order_ID, Document_No, Date_Ordered, Order_Value, Doc_Status_Code, Bpartner_Id, Warehouse_Id, Sales_Rep_Id, Currency_Id
                      FROM base_lines
                     GROUP BY C_Order_ID, Document_No, Date_Ordered, Order_Value, Doc_Status_Code, Bpartner_Id, Warehouse_Id, Sales_Rep_Id, Currency_Id
                ),
                line_stats AS (
                    SELECT
                        ol.C_Order_ID AS C_Order_ID,
                        SUM(COALESCE(ol.QtyOrdered, 0)) AS Qty_Ordered,
                        SUM(COALESCE(ol.QtyDelivered, 0)) AS Qty_Delivered
                      FROM C_OrderLine ol
                      INNER JOIN M_Product lp ON ( lp.M_Product_ID = ol.M_Product_ID )
                     WHERE ol.IsActive = 'Y'
                       AND lp.ProductType = 'I'
                       AND ol.C_Order_ID IN (SELECT C_Order_ID FROM orders)
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
                    cur.CurSymbol AS Currency_Symbol,
                    cur.ISO_Code AS Currency_Code,
                    cur.StdPrecision AS Currency_Precision,
                    COUNT(1) OVER () AS Total_Rows
                  FROM orders o
                  INNER JOIN C_BPartner bp ON ( bp.C_BPartner_ID = o.Bpartner_Id )
                  LEFT OUTER JOIN M_Warehouse wh ON ( wh.M_Warehouse_ID = o.Warehouse_Id )
                  LEFT OUTER JOIN AD_User rep ON ( rep.AD_User_ID = o.Sales_Rep_Id )
                  LEFT OUTER JOIN line_stats ls ON ( ls.C_Order_ID = o.C_Order_ID )
                  LEFT OUTER JOIN C_Currency cur ON ( cur.C_Currency_ID = o.Currency_Id )
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

                    // VAS163 2026-11-11: Row 93 - Order value in the document (price list) currency.
                    string rowCurSymbol = Util.GetValueOfString(dr2["Currency_Symbol"]);
                    if (string.IsNullOrWhiteSpace(rowCurSymbol)) { rowCurSymbol = Util.GetValueOfString(dr2["Currency_Code"]); }

                    result.Rows.Add(new OrderRow
                    {
                        CurrencySymbol = (rowCurSymbol ?? "").Trim(),
                        CurrencyIso = (Util.GetValueOfString(dr2["Currency_Code"]) ?? "").Trim(),
                        CurrencyPrecision = dr2["Currency_Precision"] == DBNull.Value ? 2 : Util.GetValueOfInt(dr2["Currency_Precision"]),
                        SalesOrderId = Util.GetValueOfInt(dr2["Order_Id"]),
                        SalesOrderNumber = Util.GetValueOfString(dr2["Document_No"]),
                        SalesOrderDate = dateOrdered.HasValue ? dateOrdered.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                        CustomerName = Util.GetValueOfString(dr2["Customer_Name"]),
                        WarehouseName = Util.GetValueOfString(dr2["Warehouse_Name"]),
                        RepresentativeName = Util.GetValueOfString(dr2["Representative_Name"]),
                        OrderValue = dr2["Order_Value"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr2["Order_Value"]),
                        DocumentStatus = DecodeLabel(docStatusMap, docStatusCode),
                        DeliveryStatus = Msg.GetMsg(ctx, deliveryCode, true) ?? deliveryFallback, // VAS163 2026-11-11: text-only label (3-arg GetMsg, no tip appended)
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
        /// <param name="baseCurrencyId">VAS163 2026-11-25: Sr 267 base currency id (0 = unconverted).</param>
        private decimal GetTotalLineValue(Ctx ctx, DateTime periodStart, DateTime periodEnd, int baseCurrencyId)
        {
            string baseLinesSql = MRole.GetDefault(ctx).AddAccessSQL(BuildBaseAllLinesSql(), "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            // VAS163 2026-11-25: Sr 267 / 275 same valuation as the category mix (ex-tax, base currency).
            string sql = @"
                WITH base_lines AS (" + baseLinesSql + @")
                SELECT SUM(" + BaseLineAmountSql(baseCurrencyId) + @") AS Total_Value FROM base_lines bl";

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
                       o.GrandTotal AS Grand_Total,
                       " + OrderTaxSumSql + @" AS Order_Tax_Amt,
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
            bool taxIncluded = false;
            int currencyPrecision = 2;
            decimal orderValueExTax = 0m;

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

                // VAS163 2026-11-25: Sr 275 SO value without tax - totals-based tax-inclusive rule of
                // VAS_271 / VAS_288 (tax is inside TotalLines when 2 * (GrandTotal - TotalLines) < SUM(C_OrderTax.TaxAmt)).
                decimal totalLines = dr["Order_Value"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Order_Value"]);
                decimal grandTotal = dr["Grand_Total"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Grand_Total"]);
                decimal orderTax = dr["Order_Tax_Amt"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Order_Tax_Amt"]);
                taxIncluded = orderTax > 0m && 2m * (grandTotal - totalLines) < orderTax;
                orderValueExTax = taxIncluded ? totalLines - orderTax : totalLines;
                currencyPrecision = dr["Header_Currency_Precision"] == DBNull.Value ? 2 : Util.GetValueOfInt(dr["Header_Currency_Precision"]);

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
                    OrderValue = orderValueExTax, // VAS163 2026-11-25: Sr 275 ex-tax
                    WarehouseName = Util.GetValueOfString(dr["Warehouse_Name"]),
                    DeliveryMode = DecodeLabel(GetDecodeMap("C_Order", "DeliveryViaRule", language), Util.GetValueOfString(dr["Delivery_Via_Rule"]))
                };
            }
            finally
            {
                CloseReader(dr);
            }

            if (result.Order == null) { return result; }

            LoadOrderLines(ctx, result, orderId, warehouseId, taxIncluded, currencyPrecision);

            // VAS163 2026-11-25: Sr 270 delivery status from Item-type product lines only (charges and
            // other product types are left out) - as VAS_288.
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
            if (hasLines && allDelivered) { deliveryStatusKey = "VAS_281_DeliveryFull"; deliveryStatusFallback = "Fully delivered"; }
            else if (anyDelivered) { deliveryStatusKey = "VAS_281_DeliveryPartial"; deliveryStatusFallback = "Partial"; }
            else { deliveryStatusKey = "VAS_281_DeliveryPending"; deliveryStatusFallback = "Pending"; }
            result.Order.DeliveryStatus = Msg.GetMsg(ctx, deliveryStatusKey, true) ?? deliveryStatusFallback; // VAS163 2026-11-11: text-only label (3-arg GetMsg, no tip appended)

            return result;
        }

        /// <summary>
        /// Every active line of the order, each carrying its own free-stock figure.
        /// VAS163 2026-11-25: Sr 269-275 (same rules as VAS_288 / VAS_271's lines pop-up):
        ///  - charge lines show the charge name (C_Charge.Name);
        ///  - Ordered / Delivered / Pending / In stock / Line status only for Item-type products
        ///    (M_Product.ProductType = 'I'), flagged with IsItem;
        ///  - quantities in the line's selected UOM: QtyEntered, delivered scaled by QtyEntered / QtyOrdered;
        ///    In stock stays in the product's base UOM (its name is sent as BaseUomName);
        ///  - Attribute only for lines with an attribute set instance (HasAttribute);
        ///  - on a tax-inclusive order rate and amount are restated without tax (C_OrderTax ratio).
        /// </summary>
        /// <param name="ctx">Context.</param>
        /// <param name="result">Payload the lines are added to.</param>
        /// <param name="orderId">C_Order_ID.</param>
        /// <param name="headerWarehouseId">Order warehouse (used when the line has none).</param>
        /// <param name="taxIncluded">True when the order's line amounts include tax.</param>
        /// <param name="stdPrecision">Order currency precision.</param>
        private void LoadOrderLines(Ctx ctx, OrderDetailResult result, int orderId, int headerWarehouseId, bool taxIncluded, int stdPrecision)
        {
            // ProductType is a CHAR column and the charge / product names are NVARCHAR - selected raw
            // and resolved in C# (no COALESCE against a literal, see VAS_271).
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
                       u.StdPrecision AS Uom_Precision,
                       COALESCE(bu.Name, N'') AS Base_Uom_Name,
                       bu.StdPrecision AS Base_Uom_Precision
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
                    int uomPrecision = dr["Uom_Precision"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Uom_Precision"]);

                    // Sr 272: line UOM quantities (QtyOrdered / QtyDelivered / storage are base UOM).
                    decimal uomRatio = qtyOrderedBase != 0m ? (qtyEntered / qtyOrderedBase) : 1m;
                    decimal qtyOrdered = qtyEntered;
                    decimal qtyDelivered = Math.Round(qtyDeliveredBase * uomRatio, Math.Max(0, Math.Min(uomPrecision, 12)), MidpointRounding.AwayFromZero);

                    // Sr 270: quantities / stock / status only for Item-type products.
                    bool isItem = productId > 0
                        && string.Equals((Util.GetValueOfString(dr["Product_Type"]) ?? "").Trim(), "I", StringComparison.OrdinalIgnoreCase);

                    // Sr 269: a charge line shows the charge's name.
                    string productName = Util.GetValueOfString(dr["Product_Name"]);
                    if (string.IsNullOrEmpty(productName)) { productName = Util.GetValueOfString(dr["Charge_Name"]); }
                    if (string.IsNullOrEmpty(productName)) { productName = Util.GetValueOfString(dr["Description"]); }

                    // Sr 275: amount / rate without tax on a tax-inclusive order (VAS_271 Row 20 rule).
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

                    // Rate per selected UOM (PriceEntered), falling back to PriceActual.
                    decimal rate = dr["Price_Entered"] == DBNull.Value
                        ? (dr["Price_Actual"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Price_Actual"]))
                        : Util.GetValueOfDecimal(dr["Price_Entered"]);
                    if (taxIncluded && lineGross != 0m)
                    {
                        rate = Math.Round(rate * lineAmt / lineGross, 6, MidpointRounding.AwayFromZero);
                    }

                    int asiId = dr["Asi_Id"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Asi_Id"]);
                    // VAS163 2026-11-11: row 58 In stock never negative - show 0. Base UOM (Sr 273).
                    decimal stockBase = isItem ? Math.Max(0m, ResolveFreeStock(ctx, productId, warehouseId)) : 0m;
                    decimal qtyPending = isItem ? Math.Max(0m, qtyOrdered - qtyDelivered) : 0m;

                    result.Lines.Add(new OrderLineRow
                    {
                        LineNo = Util.GetValueOfInt(dr["Line_No"]),
                        ProductName = productName,
                        HasAttribute = asiId > 0,
                        AttributeText = asiId > 0 ? Util.GetValueOfString(dr["Attribute_Text"]) : string.Empty,
                        UomName = Util.GetValueOfString(dr["Uom_Name"]),
                        UomPrecision = uomPrecision,
                        BaseUomName = Util.GetValueOfString(dr["Base_Uom_Name"]),
                        BaseUomPrecision = dr["Base_Uom_Precision"] == DBNull.Value ? uomPrecision : Util.GetValueOfInt(dr["Base_Uom_Precision"]),
                        IsItem = isItem,
                        QtyOrdered = isItem ? qtyOrdered : 0m,
                        QtyDelivered = isItem ? qtyDelivered : 0m,
                        QtyPending = qtyPending,
                        FreeStock = stockBase,
                        // Shortage judged in the line UOM (stock is base UOM).
                        QtyShort = isItem ? Math.Max(0m, qtyPending - stockBase * uomRatio) : 0m,
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
        /// VAS163 2026-11-25: Sr 275 (copied from VAS_288 / VAS_271 Row 20). Tax-inclusive order only: per
        /// C_Tax_ID, the share of the lines' gross (SUM(LineNetAmt)) that is not tax, from the order's
        /// own tax summary (C_OrderTax.TaxAmt). A tax without a plausible summary row is left out and
        /// the caller falls back to the line's own TaxAmt / rate.
        /// </summary>
        /// <param name="orderId">C_Order_ID.</param>
        /// <returns>C_Tax_ID -&gt; net / gross ratio.</returns>
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
                Log.Log(Level.SEVERE, "VAS_281_CategoryWiseSOWidget.GetOrderTaxNetRatios", ex);
            }
            return ratios;
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

        private class RawCategory
        {
            public int CategoryId { get; set; }
            public string CategoryName { get; set; }
            public decimal Value { get; set; }
        }

        private class CategoryMixResult : CurrencyPayload
        {
            public decimal TotalValue { get; set; }
            public List<CategorySegment> Categories { get; set; }
        }

        private class CategorySegment
        {
            public List<int> CategoryIds { get; set; }
            public string CategoryName { get; set; }
            public decimal Value { get; set; }
            /// <summary>VAS163 2026-11-25: Sr 289 one decimal (was a whole percent).</summary>
            public decimal Percent { get; set; }
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
            /// <summary>VAS163 2026-11-11: Row 93 - document (price list) currency of OrderValue.</summary>
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
            public decimal QtyOrdered { get; set; }
            public decimal QtyDelivered { get; set; }
            public decimal QtyPending { get; set; }
            public decimal FreeStock { get; set; }
            public decimal Rate { get; set; }
            public decimal Amount { get; set; }
            /// <summary>VAS163 2026-11-25: Sr 270-273 line flags / UOM data (as VAS_288).</summary>
            public bool HasAttribute { get; set; }
            public bool IsItem { get; set; }
            public int UomPrecision { get; set; }
            public string BaseUomName { get; set; }
            public int BaseUomPrecision { get; set; }
            public decimal QtyShort { get; set; }
        }
    }
}
