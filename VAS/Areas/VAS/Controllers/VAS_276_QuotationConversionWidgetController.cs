/******************************************************
 * Module Name    : VAS
 * Purpose        : Sales Order dashboard "Quotation Conversion" KPI widget endpoints
 * chronological  : Development
 * Created Date   : 2026-09-14
 * Created by     : VAI052
 ******************************************************/

using Newtonsoft.Json;
using System;
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
    /// Module Name : VAS_276_QuotationConversionWidget
    /// Purpose     : Data endpoints for the 3x1 "Quotation Conversion" KPI split tile
    ///               on the Sales Order dashboard - how much of what was quoted this
    ///               month actually became an order. (1) the tile/summary figures -
    ///               quotations issued, converted count/rate, quoted/converted value,
    ///               average days to convert, and the "expiring in 7 days" /
    ///               "lost or expired" follow-up counts, and (2) the drill-down
    ///               modal's paginated open-quotations list with a per-row conversion
    ///               percentage and status. Unlike VAS_270-275, this widget has NO
    ///               shared Sales Order record/lines child modal - the quotation
    ///               number instead navigates directly to the existing Sales
    ///               Quotation record window (a single-level modal, no back stack).
    ///
    ///   Business definition per
    ///   09_Quotation_Conversion_Claude_Development_Prompt.txt:
    ///     - Denominator cohort: C_Order.IsSalesQuotation='Y', IsSOTrx='Y', non-return,
    ///       active, DocStatus='CO' (issued/accepted quotations only - Drafted/
    ///       In-Process quotations are excluded), DateOrdered in the selected month
    ///       (quotations ISSUED in the period, not conversions occurring in it).
    ///     - Conversion link is C_OrderLine.C_Quotation_Line_ID on the ACTUAL Sales
    ///       Order line, referencing the source quotation line's C_OrderLine_ID -
    ///       never inferred by customer/date. The linked actual order must be a real
    ///       non-quotation Sales Order with DocStatus IN ('CO','CL').
    ///     - A quotation counts as converted for the headline the moment at least one
    ///       qualifying linked line exists, even if only partially ordered; the
    ///       per-row percentage (converted qty / quoted qty) carries that nuance.
    ///     - Value quoted = C_Order.TotalLines (pre-tax) on the quotation header;
    ///       value converted = SUM(LineNetAmt) from the qualifying linked actual
    ///       order lines.
    ///     - Avg days to convert = mean of (first linked order DateOrdered - quotation
    ///       DateOrdered), converted quotations only, via the codebase's confirmed
    ///       portable DAYSBETWEEN(a, b) = a - b function.
    ///     - "Expiring in 7 days" = ValidTillDate in [today, today+8) and not fully
    ///       converted. "Lost / expired" = ValidTillDate &lt; today with zero converted
    ///       quantity (no separate lost/rejected status is invented).
    ///     - Row status vocabulary is fixed and evaluated as a priority chain: Fully
    ///       converted -&gt; Partly Ordered -&gt; Expired -&gt; Open. Resolved to a label in
    ///       C# from SQL-derived quantities, never a literal string in the query.
    ///
    ///   Per the CTE/MRole rule (Prompt_Instructions.txt "Case 1"), AddAccessSQL is
    ///   applied ONLY to the isolated "base_quotations" fragment (BuildBaseQuotationsSql)
    ///   - a single "FROM C_Order q" with plain JOINs to C_BPartner/C_BP_Group (no
    ///   nested CTEs) - before it is embedded into the combined statements used by both
    ///   the summary and the modal list, which each add their own secondary
    ///   "quote_lines" and "conversion" CTEs over C_OrderLine (and the actual-order
    ///   C_Order/C_OrderLine pair) joined afterward, never re-run through AddAccessSQL -
    ///   the same discipline already applied to VAS_270-275 after VAS_270 hit
    ///   ORA-00904 from applying AddAccessSQL to a multi-FROM-clause statement.
    ///   Read-only here; no writes.
    /// Chronological development:
    ///   VAI052      2026-09-14 Created
    /// </summary>
    public class VAS_276_QuotationConversionWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_276_QuotationConversionWidgetController).FullName);

        private const int DefaultPageSize = 10;
        private const int MaxPageSize = 10;

        /// <summary>
        /// Current-month summary figures for the tile and the modal's 8-card stat
        /// strip.
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
                string json = JsonConvert.SerializeObject(GetSummaryData(ctx));
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_276_QuotationConversionWidget.GetSummary", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// One page of the underlying quotations for the modal's table, newest issued
        /// first.
        /// </summary>
        /// <param name="page">Zero-based page index.</param>
        /// <param name="size">Rows per page (capped at <see cref="MaxPageSize"/>).</param>
        /// <returns>JSON { Total, Rows:[...] } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetQuotations(int page = 0, int size = DefaultPageSize)
        {
            if (Session["ctx"] == null) { return Json("", JsonRequestBehavior.AllowGet); }

            Ctx ctx = Session["ctx"] as Ctx;
            if (page < 0) { page = 0; }
            if (size <= 0 || size > MaxPageSize) { size = DefaultPageSize; }

            try
            {
                string json = JsonConvert.SerializeObject(GetQuotationsData(ctx, page, size));
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_276_QuotationConversionWidget.GetQuotations", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>Inclusive first day of the current month.</summary>
        private static DateTime PeriodStart()
        {
            DateTime today = DateTime.Today;
            return new DateTime(today.Year, today.Month, 1);
        }

        /// <summary>Exclusive upper bound - the day after today.</summary>
        private static DateTime PeriodEnd()
        {
            return DateTime.Today.AddDays(1);
        }

        /// <summary>Exclusive upper bound for the "expiring in 7 days" window - today + 8 days.</summary>
        private static DateTime ExpiryEnd()
        {
            return DateTime.Today.AddDays(8);
        }

        /// <summary>
        /// The base_quotations fragment - the ONLY fragment AddAccessSQL is ever
        /// applied to (Prompt_Instructions.txt "Case 1"): a single "FROM C_Order q"
        /// with plain JOINs to C_BPartner/C_BP_Group (no nested CTEs), scoped to the
        /// issued/accepted quotation cohort for the current month.
        /// </summary>
        private static string BuildBaseQuotationsSql()
        {
            return @"
                SELECT q.C_Order_ID AS C_Order_ID,
                       q.DocumentNo AS Document_No,
                       q.DateOrdered AS Date_Ordered,
                       q.ValidTillDate AS Valid_Till,
                       q.TotalLines AS Quoted_Value,
                       COALESCE(bp.Name, N'') AS Customer_Name,
                       COALESCE(bpg.Name, N'') AS Segment_Name
                  FROM C_Order q
                  INNER JOIN C_BPartner bp ON ( bp.C_BPartner_ID = q.C_BPartner_ID )
                  LEFT OUTER JOIN C_BP_Group bpg ON ( bpg.C_BP_Group_ID = bp.C_BP_Group_ID )
                 WHERE q.AD_Client_ID = @AD_Client_ID
                   AND q.IsActive = 'Y'
                   AND q.IsSOTrx = 'Y'
                   AND COALESCE(q.IsReturnTrx, 'N') = 'N'
                   AND q.IsSalesQuotation = 'Y'
                   AND q.DocStatus = 'CO'
                   AND q.DateOrdered >= @Period_Start
                   AND q.DateOrdered < @Period_End";
        }

        /// <summary>
        /// The quote_lines/conversion secondary CTEs shared by GetSummaryData and
        /// GetQuotationsData - global aggregates over C_OrderLine (and, for
        /// conversion, the qualifying actual Sales Order), joined onto the
        /// already-access-filtered base_quotations afterward. Never an AddAccessSQL
        /// target themselves.
        /// </summary>
        private static string BuildConversionCtesSql()
        {
            return @"
                quote_lines AS (
                    SELECT
                        ql.C_Order_ID AS Quotation_Id,
                        COUNT(1) AS Line_Count,
                        SUM(COALESCE(ql.QtyOrdered, 0)) AS Quoted_Qty
                      FROM C_OrderLine ql
                     WHERE ql.IsActive = 'Y'
                     GROUP BY ql.C_Order_ID
                ),
                conversion AS (
                    SELECT
                        ql.C_Order_ID AS Quotation_Id,
                        SUM(COALESCE(sol.QtyOrdered, 0)) AS Converted_Qty,
                        SUM(COALESCE(sol.LineNetAmt, 0)) AS Converted_Value,
                        MIN(so.DateOrdered) AS First_Order_Date
                      FROM C_OrderLine ql
                      INNER JOIN C_OrderLine sol ON ( sol.C_Quotation_Line_ID = ql.C_OrderLine_ID AND sol.IsActive = 'Y' )
                      INNER JOIN C_Order so ON ( so.C_Order_ID = sol.C_Order_ID
                                             AND so.IsActive = 'Y'
                                             AND so.IsSOTrx = 'Y'
                                             AND COALESCE(so.IsReturnTrx, 'N') = 'N'
                                             AND COALESCE(so.IsSalesQuotation, 'N') = 'N'
                                             AND so.DocStatus IN ('CO', 'CL') )
                     WHERE ql.IsActive = 'Y'
                     GROUP BY ql.C_Order_ID
                )";
        }

        private SummaryResult GetSummaryData(Ctx ctx)
        {
            DateTime periodStart = PeriodStart();
            DateTime periodEnd = PeriodEnd();

            SummaryResult result = new SummaryResult
            {
                PeriodStart = periodStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                PeriodEnd = periodEnd.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            };
            if (ctx == null) { return result; }

            // The only AddAccessSQL call for this statement - applied to the isolated
            // single-FROM base_quotations body BEFORE it is embedded below.
            string baseQuotationsSql = MRole.GetDefault(ctx).AddAccessSQL(BuildBaseQuotationsSql(), "q", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string sql = @"
                WITH base_quotations AS (" + baseQuotationsSql + @"),
                " + BuildConversionCtesSql() + @",
                quotation_rows AS (
                    SELECT
                        bq.C_Order_ID AS C_Order_ID,
                        bq.Date_Ordered AS Date_Ordered,
                        bq.Valid_Till AS Valid_Till,
                        bq.Quoted_Value AS Quoted_Value,
                        COALESCE(qls.Quoted_Qty, 0) AS Quoted_Qty,
                        COALESCE(c.Converted_Qty, 0) AS Converted_Qty,
                        COALESCE(c.Converted_Value, 0) AS Converted_Value,
                        c.First_Order_Date AS First_Order_Date
                      FROM base_quotations bq
                      LEFT OUTER JOIN quote_lines qls ON ( qls.Quotation_Id = bq.C_Order_ID )
                      LEFT OUTER JOIN conversion c ON ( c.Quotation_Id = bq.C_Order_ID )
                )
                SELECT
                    COUNT(1) AS Quotations_Issued,
                    SUM(CASE WHEN Converted_Qty > 0 THEN 1 ELSE 0 END) AS Converted,
                    SUM(COALESCE(Quoted_Value, 0)) AS Value_Quoted,
                    SUM(COALESCE(Converted_Value, 0)) AS Value_Converted,
                    ROUND(AVG(
                        CASE
                            WHEN First_Order_Date IS NOT NULL
                            THEN CAST(DAYSBETWEEN(First_Order_Date, Date_Ordered) AS NUMERIC)
                            ELSE NULL
                        END
                    ), 2) AS Avg_Days_To_Convert,
                    SUM(
                        CASE
                            WHEN Valid_Till >= CURRENT_DATE
                             AND Valid_Till < @Expiry_End
                             AND Converted_Qty < Quoted_Qty
                            THEN 1 ELSE 0
                        END
                    ) AS Expiring_In_7_Days,
                    SUM(
                        CASE
                            WHEN Valid_Till < CURRENT_DATE
                             AND Converted_Qty = 0
                            THEN 1 ELSE 0
                        END
                    ) AS Lost_Or_Expired
                  FROM quotation_rows";

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, new[]
                {
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                    new SqlParameter("@Period_Start", periodStart),
                    new SqlParameter("@Period_End", periodEnd),
                    new SqlParameter("@Expiry_End", ExpiryEnd())
                });

                if (dr != null && dr.Read())
                {
                    result.QuotationsIssued = dr["Quotations_Issued"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Quotations_Issued"]);
                    result.Converted = dr["Converted"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Converted"]);
                    result.ValueQuoted = dr["Value_Quoted"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Value_Quoted"]);
                    result.ValueConverted = dr["Value_Converted"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Value_Converted"]);
                    result.AvgDaysToConvert = dr["Avg_Days_To_Convert"] == DBNull.Value ? (decimal?)null : Util.GetValueOfDecimal(dr["Avg_Days_To_Convert"]);
                    result.ExpiringIn7Days = dr["Expiring_In_7_Days"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Expiring_In_7_Days"]);
                    result.LostOrExpired = dr["Lost_Or_Expired"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Lost_Or_Expired"]);
                }
            }
            finally
            {
                CloseReader(dr);
            }

            return result;
        }

        private QuotationsResult GetQuotationsData(Ctx ctx, int page, int size)
        {
            QuotationsResult result = new QuotationsResult { Rows = new List<QuotationRow>() };
            if (ctx == null) { return result; }

            string baseQuotationsSql = MRole.GetDefault(ctx).AddAccessSQL(BuildBaseQuotationsSql(), "q", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string sql = @"
                WITH base_quotations AS (" + baseQuotationsSql + @"),
                " + BuildConversionCtesSql() + @"
                SELECT
                    bq.C_Order_ID AS Quotation_Id,
                    bq.Document_No AS Document_No,
                    bq.Date_Ordered AS Date_Ordered,
                    bq.Valid_Till AS Valid_Till,
                    bq.Customer_Name AS Customer_Name,
                    bq.Segment_Name AS Segment_Name,
                    COALESCE(qls.Line_Count, 0) AS Line_Count,
                    COALESCE(qls.Quoted_Qty, 0) AS Quoted_Qty,
                    COALESCE(c.Converted_Qty, 0) AS Converted_Qty,
                    COUNT(1) OVER () AS Total_Rows
                  FROM base_quotations bq
                  LEFT OUTER JOIN quote_lines qls ON ( qls.Quotation_Id = bq.C_Order_ID )
                  LEFT OUTER JOIN conversion c ON ( c.Quotation_Id = bq.C_Order_ID )
                 ORDER BY bq.Date_Ordered DESC, bq.Document_No ASC
                 OFFSET @Row_Offset ROWS FETCH NEXT @Page_Size ROWS ONLY";

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, new[]
                {
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                    new SqlParameter("@Period_Start", PeriodStart()),
                    new SqlParameter("@Period_End", PeriodEnd()),
                    new SqlParameter("@Row_Offset", page * size),
                    new SqlParameter("@Page_Size", size)
                });

                int total = 0;
                DateTime today = DateTime.Today;
                while (dr != null && dr.Read())
                {
                    total = dr["Total_Rows"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Total_Rows"]);
                    DateTime? dateOrdered = dr["Date_Ordered"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr["Date_Ordered"]);
                    DateTime? validTill = dr["Valid_Till"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr["Valid_Till"]);
                    decimal quotedQty = dr["Quoted_Qty"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Quoted_Qty"]);
                    decimal convertedQty = dr["Converted_Qty"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Converted_Qty"]);

                    int? conversionPercent = quotedQty > 0
                        ? (int?)Math.Round(convertedQty * 100m / quotedQty, MidpointRounding.AwayFromZero)
                        : null;

                    string statusKey, statusFallback;
                    if (quotedQty > 0 && convertedQty >= quotedQty)
                    {
                        statusKey = "VAS_276_StatusFullyConverted"; statusFallback = "Fully converted";
                    }
                    else if (convertedQty > 0)
                    {
                        statusKey = "VAS_276_StatusPartlyOrdered"; statusFallback = "Partly Ordered";
                    }
                    else if (validTill.HasValue && validTill.Value < today)
                    {
                        statusKey = "VAS_276_StatusExpired"; statusFallback = "Expired";
                    }
                    else
                    {
                        statusKey = "VAS_276_StatusOpen"; statusFallback = "Open";
                    }

                    result.Rows.Add(new QuotationRow
                    {
                        QuotationId = Util.GetValueOfInt(dr["Quotation_Id"]),
                        QuotationNo = Util.GetValueOfString(dr["Document_No"]),
                        QuotationDate = dateOrdered.HasValue ? dateOrdered.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                        ValidTill = validTill.HasValue ? validTill.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                        CustomerName = Util.GetValueOfString(dr["Customer_Name"]),
                        SegmentName = Util.GetValueOfString(dr["Segment_Name"]),
                        LineCount = dr["Line_Count"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Line_Count"]),
                        QuotedQty = quotedQty,
                        ConvertedQty = convertedQty,
                        ConversionPercent = conversionPercent,
                        Status = Msg.GetMsg(ctx, statusKey) ?? statusFallback
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

        private class SummaryResult
        {
            public int QuotationsIssued { get; set; }
            public int Converted { get; set; }
            public decimal ValueQuoted { get; set; }
            public decimal ValueConverted { get; set; }
            public decimal? AvgDaysToConvert { get; set; }
            public int ExpiringIn7Days { get; set; }
            public int LostOrExpired { get; set; }
            public string PeriodStart { get; set; }
            public string PeriodEnd { get; set; }
        }

        private class QuotationsResult
        {
            public int Total { get; set; }
            public List<QuotationRow> Rows { get; set; }
        }

        private class QuotationRow
        {
            public int QuotationId { get; set; }
            public string QuotationNo { get; set; }
            public string QuotationDate { get; set; }
            public string ValidTill { get; set; }
            public string CustomerName { get; set; }
            public string SegmentName { get; set; }
            public int LineCount { get; set; }
            public decimal QuotedQty { get; set; }
            public decimal ConvertedQty { get; set; }
            public int? ConversionPercent { get; set; }
            public string Status { get; set; }
        }
    }
}
