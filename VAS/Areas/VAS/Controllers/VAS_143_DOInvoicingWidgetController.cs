using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Web.Mvc;
using VAdvantage.DataBase;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VAS.Controllers
{
    /// <summary>
    /// Module Name : VAS_143_DOInvoicingWidget (Delivery Order dashboard)
    /// Purpose     : Data endpoints for the 3x2 "DO Invoicing Status" widget -
    ///               period-scoped (Month/Quarter/FY) counts and values of
    ///               customer sales invoices linked to delivery orders (directly,
    ///               through the header C_Invoice_ID, through a line-level
    ///               M_InOutLine_ID, or through the Sales Order at header or line
    ///               level), bucketed Raised/Completed/Open, plus a coverage %
    ///               (invoiced value vs. period DO value) and a trend % vs the
    ///               prior equivalent period. The drill-down modal lists the
    ///               underlying invoices for one category+period, newest first,
    ///               server-paginated. Never loads raw invoice rows to compute the
    ///               summary - both endpoints are pure aggregate SQL. MRole is
    ///               applied to the primary fetched table (C_Invoice) on every
    ///               read; all input is parameterized; the SQL uses only
    ///               COALESCE / CASE / EXISTS / UNION (no NVL, DECODE, TRUNC,
    ///               TO_CHAR/EXTRACT for filtering, LIMIT/ROWNUM - pagination via
    ///               OFFSET/FETCH which both Oracle 12c+ and PostgreSQL support),
    ///               so it runs unchanged on Oracle and PostgreSQL. Every bind
    ///               parameter that repeats in the assembled SQL text uses a
    ///               uniquely-suffixed name with matching array entries in exact
    ///               left-to-right order (DB.ExecuteReader binds positionally, not
    ///               by name - see VAS_133's fix for the same class of bug).
    ///               DO value has no header total (M_InOut has none) so it is
    ///               derived per line from the linked C_OrderLine's per-unit
    ///               value (LineTotalAmt/QtyOrdered, falling back to LineNetAmt/
    ///               QtyOrdered, then PriceActual, then PriceEntered) times the
    ///               delivered MovementQty; a DO line with no linked order line
    ///               contributes 0 (cannot be derived without an order line).
    /// Widget number 143.
    /// Chronological development:
    ///   &lt;EmpCode&gt;   2026-07-21 Created
    /// </summary>
    public class VAS_143_DOInvoicingWidgetController : Controller
    {
        private class PeriodBounds
        {
            public DateTime PeriodStart;
            public DateTime PeriodEnd;
            public DateTime PriorStart;
            public DateTime PriorEnd;
            public string PeriodLabel;
            public string TrendLabel;
        }

        /// <summary>
        /// Resolves the half-open [start, end) window for the requested period and
        /// its immediately-prior equivalent window. FY uses the client's active
        /// calendar (C_Year/C_Period, PeriodType='S') the same way the Top
        /// Vendors/Top Customers widgets resolve fiscal years; falls back to the
        /// calendar year if no calendar data is found for the client.
        /// </summary>
        private PeriodBounds ResolvePeriod(Ctx ctx, string period)
        {
            DateTime today = DateTime.Now.Date;

            if (string.Equals(period, "quarter", StringComparison.OrdinalIgnoreCase))
            {
                int qStartMonth = (((today.Month - 1) / 3) * 3) + 1;
                DateTime start = new DateTime(today.Year, qStartMonth, 1);
                DateTime end = start.AddMonths(3);
                return new PeriodBounds
                {
                    PeriodStart = start,
                    PeriodEnd = end,
                    PriorStart = start.AddMonths(-3),
                    PriorEnd = start,
                    PeriodLabel = "This Quarter",
                    TrendLabel = "vs prior quarter"
                };
            }

            if (string.Equals(period, "fy", StringComparison.OrdinalIgnoreCase))
            {
                PeriodBounds fy = ResolveFiscalYear(ctx, today);
                if (fy != null) { return fy; }
                // No calendar data for this client - fall back to calendar year.
                DateTime cyStart = new DateTime(today.Year, 1, 1);
                return new PeriodBounds
                {
                    PeriodStart = cyStart,
                    PeriodEnd = cyStart.AddYears(1),
                    PriorStart = cyStart.AddYears(-1),
                    PriorEnd = cyStart,
                    PeriodLabel = "FY " + today.Year,
                    TrendLabel = "vs prior year"
                };
            }

            // Default: Month.
            DateTime mStart = new DateTime(today.Year, today.Month, 1);
            return new PeriodBounds
            {
                PeriodStart = mStart,
                PeriodEnd = mStart.AddMonths(1),
                PriorStart = mStart.AddMonths(-1),
                PriorEnd = mStart,
                PeriodLabel = "This Month",
                TrendLabel = "vs prior month"
            };
        }

        /// <summary>Current and immediately-prior fiscal year bounds from the client's active calendar, or null if none configured.</summary>
        private PeriodBounds ResolveFiscalYear(Ctx ctx, DateTime today)
        {
            string sql = @"
                SELECT y.C_Year_ID AS YearId, y.FiscalYear AS FiscalYear,
                       MIN(p.StartDate) AS StartDate, MAX(p.EndDate) AS EndDate
                FROM AD_ClientInfo ci
                JOIN C_Year y ON (y.C_Calendar_ID = ci.C_Calendar_ID AND y.AD_Client_ID = ci.AD_Client_ID)
                JOIN C_Period p ON (p.C_Year_ID = y.C_Year_ID AND p.AD_Client_ID = y.AD_Client_ID)
                WHERE ci.AD_Client_ID = @AD_Client_ID
                  AND y.IsActive = 'Y'
                  AND p.IsActive = 'Y'
                  AND p.PeriodType = 'S'
                GROUP BY y.C_Year_ID, y.FiscalYear
                ORDER BY MIN(p.StartDate) DESC";

            List<Tuple<string, DateTime, DateTime>> years = new List<Tuple<string, DateTime, DateTime>>();
            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, new SqlParameter[]
                {
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
                });
                while (dr != null && dr.Read())
                {
                    DateTime? start = Util.GetValueOfDateTime(dr["StartDate"]);
                    DateTime? end = Util.GetValueOfDateTime(dr["EndDate"]);
                    if (!start.HasValue || !end.HasValue) { continue; }
                    years.Add(Tuple.Create(Util.GetValueOfString(dr["FiscalYear"]), start.Value, end.Value));
                }
            }
            finally
            {
                if (dr != null) { dr.Close(); dr.Dispose(); }
            }

            if (years.Count == 0) { return null; }

            int currentIdx = years.FindIndex(y => today >= y.Item2 && today < y.Item3.AddDays(1));
            if (currentIdx < 0) { currentIdx = 0; } // newest year first; default to most recent if today falls outside all ranges

            Tuple<string, DateTime, DateTime> current = years[currentIdx];
            DateTime priorStart, priorEnd;
            if (currentIdx + 1 < years.Count)
            {
                Tuple<string, DateTime, DateTime> prior = years[currentIdx + 1];
                priorStart = prior.Item2;
                priorEnd = current.Item2;
            }
            else
            {
                // No earlier configured year - use an equal-length window immediately before this one.
                TimeSpan span = current.Item3 - current.Item2;
                priorStart = current.Item2 - span;
                priorEnd = current.Item2;
            }

            return new PeriodBounds
            {
                PeriodStart = current.Item2,
                PeriodEnd = current.Item3.AddDays(1),
                PriorStart = priorStart,
                PriorEnd = priorEnd,
                PeriodLabel = "FY " + current.Item1,
                TrendLabel = "vs prior year"
            };
        }

        /// <summary>
        /// Period summary: Raised / Completed / Open counts + values, DO count,
        /// coverage %, trend % vs the prior equivalent period. Pure aggregate SQL -
        /// never loads raw invoice rows.
        /// </summary>
        /// <param name="period">month | quarter | fy</param>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetSummary(string period = "month")
        {
            if (Session["ctx"] == null)
            {
                return Fail(Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired");
            }

            Ctx ctx = Session["ctx"] as Ctx;
            period = NormalizePeriod(period);
            PeriodBounds bounds = ResolvePeriod(ctx, period);

            // base_invoice and do_line_value each read their primary table (C_Invoice /
            // M_InOut) directly, so MRole is applied to each individually before they
            // are spliced into the combined WITH clause below - AddAccessSQL appends
            // its predicate to the END of the string it's given, so each call happens
            // on a WHERE-only block, with anything that must follow (GROUP BY) appended
            // afterward. Every :period_start / :period_end / :prior_start / :prior_end
            // in the prompt's reference SQL is given a UNIQUE suffixed name here,
            // counted in the exact left-to-right order it appears below, with a
            // matching SqlParameter array (DB.ExecuteReader binds positionally).
            string baseInvoiceSql = @"
                    SELECT i.C_Invoice_ID, i.DocumentNo, i.DateInvoiced, i.GrandTotal,
                           i.VA009_OpenAmount, i.VA009_PaidAmount, i.IsPaid,
                           i.C_Order_ID, i.C_BPartner_ID, i.C_Currency_ID
                    FROM C_Invoice i
                    JOIN C_DocType dt ON (dt.C_DocType_ID = i.C_DocType_ID AND dt.IsActive = 'Y')
                    WHERE i.IsActive = 'Y'
                      AND i.IsSOTrx = 'Y'
                      AND i.DocStatus IN ('CO', 'CL')
                      AND COALESCE(i.IsReturnTrx, 'N') = 'N'
                      AND COALESCE(dt.DocBaseType, 'X') NOT IN ('ARC', 'APC')
                      AND i.AD_Client_ID = @AD_Client_ID1
                      AND i.DateInvoiced >= @PriorStart1
                      AND i.DateInvoiced <  @PeriodEnd1";
            baseInvoiceSql = MRole.GetDefault(ctx).AddAccessSQL(baseInvoiceSql, "i", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string doLineValueSql = @"
                    SELECT io.M_InOut_ID,
                           SUM(
                               COALESCE(iol.MovementQty, 0) *
                               COALESCE(
                                   ol.LineTotalAmt / NULLIF(ol.QtyOrdered, 0),
                                   ol.LineNetAmt / NULLIF(ol.QtyOrdered, 0),
                                   ol.PriceActual,
                                   ol.PriceEntered,
                                   0
                               )
                           ) AS DOValue
                    FROM M_InOut io
                    LEFT JOIN M_InOutLine iol ON (iol.M_InOut_ID = io.M_InOut_ID AND iol.IsActive = 'Y')
                    LEFT JOIN C_OrderLine ol ON (ol.C_OrderLine_ID = iol.C_OrderLine_ID AND ol.IsActive = 'Y')
                    WHERE io.IsActive = 'Y'
                      AND io.IsSOTrx = 'Y'
                      AND io.MovementType = 'C-'
                      AND io.DocStatus IN ('CO', 'CL')
                      AND io.AD_Client_ID = @AD_Client_ID2
                      AND io.MovementDate >= @PeriodStart1
                      AND io.MovementDate <  @PeriodEnd2";
            doLineValueSql = MRole.GetDefault(ctx).AddAccessSQL(doLineValueSql, "io", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            doLineValueSql += @"
                    GROUP BY io.M_InOut_ID";

            string sql = @"
                WITH base_invoice AS (
                " + baseInvoiceSql + @"
                ),
                do_invoice_link AS (
                    SELECT DISTINCT bi.C_Invoice_ID, io.M_InOut_ID, io.C_Order_ID, 'DO' AS LinkType
                    FROM base_invoice bi
                    JOIN M_InOut io ON (io.C_Invoice_ID = bi.C_Invoice_ID AND io.IsActive='Y' AND io.IsSOTrx='Y' AND io.MovementType='C-' AND io.DocStatus IN ('CO','CL'))
                    UNION
                    SELECT DISTINCT bi.C_Invoice_ID, io.M_InOut_ID, io.C_Order_ID, 'DO' AS LinkType
                    FROM base_invoice bi
                    JOIN C_InvoiceLine il ON (il.C_Invoice_ID = bi.C_Invoice_ID AND il.IsActive='Y')
                    JOIN M_InOutLine iol ON (iol.M_InOutLine_ID = il.M_InOutLine_ID AND iol.IsActive='Y')
                    JOIN M_InOut io ON (io.M_InOut_ID = iol.M_InOut_ID AND io.IsActive='Y' AND io.IsSOTrx='Y' AND io.MovementType='C-' AND io.DocStatus IN ('CO','CL'))
                    UNION
                    SELECT DISTINCT bi.C_Invoice_ID, io.M_InOut_ID, io.C_Order_ID, 'SO' AS LinkType
                    FROM base_invoice bi
                    JOIN M_InOut io ON (io.C_Order_ID = bi.C_Order_ID AND io.IsActive='Y' AND io.IsSOTrx='Y' AND io.MovementType='C-' AND io.DocStatus IN ('CO','CL'))
                    WHERE bi.C_Order_ID IS NOT NULL
                    UNION
                    SELECT DISTINCT bi.C_Invoice_ID, io.M_InOut_ID, ol.C_Order_ID, 'SO' AS LinkType
                    FROM base_invoice bi
                    JOIN C_InvoiceLine il ON (il.C_Invoice_ID = bi.C_Invoice_ID AND il.IsActive='Y' AND il.C_OrderLine_ID IS NOT NULL)
                    JOIN C_OrderLine ol ON (ol.C_OrderLine_ID = il.C_OrderLine_ID AND ol.IsActive='Y')
                    JOIN M_InOutLine iol ON (iol.C_OrderLine_ID = il.C_OrderLine_ID AND iol.IsActive='Y')
                    JOIN M_InOut io ON (io.M_InOut_ID = iol.M_InOut_ID AND io.IsActive='Y' AND io.IsSOTrx='Y' AND io.MovementType='C-' AND io.DocStatus IN ('CO','CL'))
                ),
                linked_invoice AS (
                    SELECT DISTINCT bi.C_Invoice_ID, bi.DocumentNo, bi.DateInvoiced, bi.GrandTotal, bi.VA009_OpenAmount, bi.VA009_PaidAmount, bi.IsPaid
                    FROM base_invoice bi
                    JOIN do_invoice_link l ON (l.C_Invoice_ID = bi.C_Invoice_ID)
                ),
                do_line_value AS (
                " + doLineValueSql + @"
                ),
                do_summary AS (
                    SELECT COUNT(*) AS DOCount, COALESCE(SUM(COALESCE(DOValue, 0)), 0) AS TotalDOValue
                    FROM do_line_value
                ),
                invoice_stats AS (
                    SELECT
                        COALESCE(SUM(CASE WHEN DateInvoiced >= @PeriodStart2 AND DateInvoiced < @PeriodEnd3 THEN 1 ELSE 0 END), 0) AS RaisedCount,
                        COALESCE(SUM(CASE WHEN DateInvoiced >= @PeriodStart3 AND DateInvoiced < @PeriodEnd4 THEN COALESCE(GrandTotal, 0) ELSE 0 END), 0) AS RaisedValue,
                        COALESCE(SUM(CASE WHEN DateInvoiced >= @PeriodStart4 AND DateInvoiced < @PeriodEnd5 AND COALESCE(IsPaid, 'N') = 'Y' THEN 1 ELSE 0 END), 0) AS CompletedCount,
                        COALESCE(SUM(CASE WHEN DateInvoiced >= @PeriodStart5 AND DateInvoiced < @PeriodEnd6 AND COALESCE(IsPaid, 'N') = 'Y' THEN COALESCE(GrandTotal, 0) ELSE 0 END), 0) AS CompletedValue,
                        COALESCE(SUM(CASE WHEN DateInvoiced >= @PeriodStart6 AND DateInvoiced < @PeriodEnd7 AND COALESCE(IsPaid, 'N') <> 'Y' THEN 1 ELSE 0 END), 0) AS OpenCount,
                        COALESCE(SUM(CASE WHEN DateInvoiced >= @PeriodStart7 AND DateInvoiced < @PeriodEnd8 AND COALESCE(IsPaid, 'N') <> 'Y'
                            THEN GREATEST(COALESCE(VA009_OpenAmount, GrandTotal - COALESCE(VA009_PaidAmount, 0), GrandTotal, 0), 0)
                            ELSE 0
                        END), 0) AS OpenValue,
                        COALESCE(SUM(CASE WHEN DateInvoiced >= @PriorStart2 AND DateInvoiced < @PriorEnd1 THEN 1 ELSE 0 END), 0) AS PriorRaisedCount
                    FROM linked_invoice
                )
                SELECT
                    s.RaisedCount AS raised_count, s.RaisedValue AS raised_value,
                    s.CompletedCount AS completed_count, s.CompletedValue AS completed_value,
                    s.OpenCount AS open_count, s.OpenValue AS open_value,
                    d.DOCount AS do_count, d.TotalDOValue AS do_value,
                    CASE
                        WHEN d.TotalDOValue <= 0 THEN 0
                        WHEN ROUND((s.RaisedValue * 100.0) / d.TotalDOValue) > 100 THEN 100
                        ELSE ROUND((s.RaisedValue * 100.0) / d.TotalDOValue)
                    END AS coverage_pct,
                    CASE
                        WHEN s.PriorRaisedCount = 0 THEN NULL
                        ELSE ROUND(((s.RaisedCount - s.PriorRaisedCount) * 100.0) / s.PriorRaisedCount)
                    END AS trend_pct
                FROM invoice_stats s
                CROSS JOIN do_summary d";

            List<SqlParameter> parameters = new List<SqlParameter>
            {
                new SqlParameter("@AD_Client_ID1", ctx.GetAD_Client_ID()),
                new SqlParameter("@PriorStart1", bounds.PriorStart),
                new SqlParameter("@PeriodEnd1", bounds.PeriodEnd),
                new SqlParameter("@AD_Client_ID2", ctx.GetAD_Client_ID()),
                new SqlParameter("@PeriodStart1", bounds.PeriodStart),
                new SqlParameter("@PeriodEnd2", bounds.PeriodEnd),
                new SqlParameter("@PeriodStart2", bounds.PeriodStart),
                new SqlParameter("@PeriodEnd3", bounds.PeriodEnd),
                new SqlParameter("@PeriodStart3", bounds.PeriodStart),
                new SqlParameter("@PeriodEnd4", bounds.PeriodEnd),
                new SqlParameter("@PeriodStart4", bounds.PeriodStart),
                new SqlParameter("@PeriodEnd5", bounds.PeriodEnd),
                new SqlParameter("@PeriodStart5", bounds.PeriodStart),
                new SqlParameter("@PeriodEnd6", bounds.PeriodEnd),
                new SqlParameter("@PeriodStart6", bounds.PeriodStart),
                new SqlParameter("@PeriodEnd7", bounds.PeriodEnd),
                new SqlParameter("@PeriodStart7", bounds.PeriodStart),
                new SqlParameter("@PeriodEnd8", bounds.PeriodEnd),
                new SqlParameter("@PriorStart2", bounds.PriorStart),
                new SqlParameter("@PriorEnd1", bounds.PriorEnd)
            };

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, parameters.ToArray());

                int raisedCount = 0, completedCount = 0, openCount = 0, doCount = 0, coveragePct = 0;
                decimal raisedValue = 0, completedValue = 0, openValue = 0, doValue = 0;
                int? trendPct = null;

                if (dr != null && dr.Read())
                {
                    raisedCount = Util.GetValueOfInt(dr["raised_count"]);
                    raisedValue = Util.GetValueOfDecimal(dr["raised_value"]);
                    completedCount = Util.GetValueOfInt(dr["completed_count"]);
                    completedValue = Util.GetValueOfDecimal(dr["completed_value"]);
                    openCount = Util.GetValueOfInt(dr["open_count"]);
                    openValue = Util.GetValueOfDecimal(dr["open_value"]);
                    doCount = Util.GetValueOfInt(dr["do_count"]);
                    doValue = Util.GetValueOfDecimal(dr["do_value"]);
                    coveragePct = Util.GetValueOfInt(dr["coverage_pct"]);
                    object trendRaw = dr["trend_pct"];
                    trendPct = (trendRaw == null || trendRaw == DBNull.Value) ? (int?)null : Util.GetValueOfInt(trendRaw);
                }

                return Ok(new
                {
                    period = period,
                    periodLabel = bounds.PeriodLabel,
                    raisedCount = raisedCount,
                    raisedValue = raisedValue,
                    completedCount = completedCount,
                    completedValue = completedValue,
                    openCount = openCount,
                    openValue = openValue,
                    coveragePct = coveragePct,
                    doCount = doCount,
                    trendPct = trendPct,
                    trendLabel = bounds.TrendLabel,
                    currency = GetCurrencyInfo(ctx)
                });
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
            }
            finally
            {
                if (dr != null) { dr.Close(); dr.Dispose(); }
            }
        }

        /// <summary>
        /// One page of the invoices behind one category+period, newest first.
        /// </summary>
        /// <param name="category">raised | completed | open</param>
        /// <param name="period">month | quarter | fy</param>
        /// <param name="page">0-based page number.</param>
        /// <param name="size">Rows per page (clamped 3-12, matching the widget's adaptive modal sizing).</param>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetDrillDown(string category = "raised", string period = "month", int page = 0, int size = 8)
        {
            if (Session["ctx"] == null)
            {
                return Fail(Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired");
            }

            Ctx ctx = Session["ctx"] as Ctx;
            category = NormalizeCategory(category);
            period = NormalizePeriod(period);
            if (page < 0) { page = 0; }
            if (size < 3) { size = 3; }
            if (size > 12) { size = 12; }

            PeriodBounds bounds = ResolvePeriod(ctx, period);
            int offset = page * size;

            // Same MRole-before-splicing technique as GetSummary (see its comment).
            string baseInvoiceSql = @"
                    SELECT i.C_Invoice_ID, i.DocumentNo, i.DateInvoiced, i.GrandTotal,
                           i.VA009_OpenAmount, i.VA009_PaidAmount, i.IsPaid,
                           i.C_Order_ID, i.C_BPartner_ID, i.C_Currency_ID
                    FROM C_Invoice i
                    JOIN C_DocType dt ON (dt.C_DocType_ID = i.C_DocType_ID AND dt.IsActive = 'Y')
                    WHERE i.IsActive = 'Y'
                      AND i.IsSOTrx = 'Y'
                      AND i.DocStatus IN ('CO', 'CL')
                      AND COALESCE(i.IsReturnTrx, 'N') = 'N'
                      AND COALESCE(dt.DocBaseType, 'X') NOT IN ('ARC', 'APC')
                      AND i.AD_Client_ID = @AD_Client_ID1
                      AND i.DateInvoiced >= @PeriodStart1
                      AND i.DateInvoiced <  @PeriodEnd1
                      AND (
                            @Category1 = 'raised'
                         OR (@Category2 = 'completed' AND COALESCE(i.IsPaid, 'N') = 'Y')
                         OR (@Category3 = 'open' AND COALESCE(i.IsPaid, 'N') <> 'Y')
                      )";
            baseInvoiceSql = MRole.GetDefault(ctx).AddAccessSQL(baseInvoiceSql, "i", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string sql = @"
                WITH base_invoice AS (
                " + baseInvoiceSql + @"
                ),
                do_invoice_link AS (
                    SELECT DISTINCT bi.C_Invoice_ID, io.M_InOut_ID, io.C_Order_ID, 'DO' AS LinkType
                    FROM base_invoice bi
                    JOIN M_InOut io ON (io.C_Invoice_ID = bi.C_Invoice_ID AND io.IsActive='Y' AND io.IsSOTrx='Y' AND io.MovementType='C-' AND io.DocStatus IN ('CO','CL'))
                    UNION
                    SELECT DISTINCT bi.C_Invoice_ID, io.M_InOut_ID, io.C_Order_ID, 'DO' AS LinkType
                    FROM base_invoice bi
                    JOIN C_InvoiceLine il ON (il.C_Invoice_ID = bi.C_Invoice_ID AND il.IsActive='Y')
                    JOIN M_InOutLine iol ON (iol.M_InOutLine_ID = il.M_InOutLine_ID AND iol.IsActive='Y')
                    JOIN M_InOut io ON (io.M_InOut_ID = iol.M_InOut_ID AND io.IsActive='Y' AND io.IsSOTrx='Y' AND io.MovementType='C-' AND io.DocStatus IN ('CO','CL'))
                    UNION
                    SELECT DISTINCT bi.C_Invoice_ID, io.M_InOut_ID, io.C_Order_ID, 'SO' AS LinkType
                    FROM base_invoice bi
                    JOIN M_InOut io ON (io.C_Order_ID = bi.C_Order_ID AND io.IsActive='Y' AND io.IsSOTrx='Y' AND io.MovementType='C-' AND io.DocStatus IN ('CO','CL'))
                    WHERE bi.C_Order_ID IS NOT NULL
                    UNION
                    SELECT DISTINCT bi.C_Invoice_ID, io.M_InOut_ID, ol.C_Order_ID, 'SO' AS LinkType
                    FROM base_invoice bi
                    JOIN C_InvoiceLine il ON (il.C_Invoice_ID = bi.C_Invoice_ID AND il.IsActive='Y' AND il.C_OrderLine_ID IS NOT NULL)
                    JOIN C_OrderLine ol ON (ol.C_OrderLine_ID = il.C_OrderLine_ID AND ol.IsActive='Y')
                    JOIN M_InOutLine iol ON (iol.C_OrderLine_ID = il.C_OrderLine_ID AND iol.IsActive='Y')
                    JOIN M_InOut io ON (io.M_InOut_ID = iol.M_InOut_ID AND io.IsActive='Y' AND io.IsSOTrx='Y' AND io.MovementType='C-' AND io.DocStatus IN ('CO','CL'))
                ),
                ref_choice AS (
                    SELECT C_Invoice_ID,
                           MIN(CASE WHEN LinkType = 'DO' THEN M_InOut_ID END) AS DirectDO_ID,
                           MIN(CASE WHEN LinkType = 'SO' THEN C_Order_ID END) AS SO_ID,
                           MIN(C_Order_ID) AS AnySO_ID
                    FROM do_invoice_link
                    GROUP BY C_Invoice_ID
                ),
                rows_base AS (
                    SELECT
                        bi.C_Invoice_ID,
                        bi.DocumentNo AS InvoiceNo,
                        bi.DateInvoiced AS InvoiceDate,
                        CASE WHEN rc.DirectDO_ID IS NOT NULL THEN dio.DocumentNo ELSE COALESCE(so.DocumentNo, anyso.DocumentNo) END AS RefNo,
                        CASE WHEN rc.DirectDO_ID IS NOT NULL THEN 'DO' ELSE 'SO' END AS RefType,
                        CASE WHEN rc.DirectDO_ID IS NOT NULL THEN dio.MovementDate ELSE COALESCE(so.DateOrdered, anyso.DateOrdered) END AS RefDate,
                        bp.Name AS CustomerName,
                        COALESCE(bi.GrandTotal, 0) AS Amount,
                        CASE
                            WHEN COALESCE(bi.IsPaid, 'N') = 'Y' THEN 'Completed'
                            WHEN COALESCE(bi.VA009_PaidAmount, 0) > 0 THEN 'Partially Paid'
                            ELSE 'Raised'
                        END AS StatusLabel
                    FROM base_invoice bi
                    JOIN ref_choice rc ON (rc.C_Invoice_ID = bi.C_Invoice_ID)
                    LEFT JOIN M_InOut dio ON (dio.M_InOut_ID = rc.DirectDO_ID)
                    LEFT JOIN C_Order so ON (so.C_Order_ID = rc.SO_ID)
                    LEFT JOIN C_Order anyso ON (anyso.C_Order_ID = rc.AnySO_ID)
                    JOIN C_BPartner bp ON (bp.C_BPartner_ID = bi.C_BPartner_ID AND bp.IsActive = 'Y')
                )
                SELECT COUNT(*) OVER() AS total_count,
                       InvoiceNo AS invoice_no, InvoiceDate AS invoice_date,
                       RefNo AS ref_no, RefType AS ref_type, RefDate AS ref_date,
                       CustomerName AS customer_name, Amount AS amount, StatusLabel AS status
                FROM rows_base
                ORDER BY InvoiceDate DESC, InvoiceNo DESC
                OFFSET @Offset1 ROWS FETCH NEXT @Size1 ROWS ONLY";

            List<SqlParameter> parameters = new List<SqlParameter>
            {
                new SqlParameter("@AD_Client_ID1", ctx.GetAD_Client_ID()),
                new SqlParameter("@PeriodStart1", bounds.PeriodStart),
                new SqlParameter("@PeriodEnd1", bounds.PeriodEnd),
                new SqlParameter("@Category1", category),
                new SqlParameter("@Category2", category),
                new SqlParameter("@Category3", category),
                new SqlParameter("@Offset1", offset),
                new SqlParameter("@Size1", size)
            };

            List<object> rows = new List<object>();
            int total = 0;
            IDataReader dr = null;

            try
            {
                dr = DB.ExecuteReader(sql, parameters.ToArray());
                while (dr != null && dr.Read())
                {
                    total = Util.GetValueOfInt(dr["total_count"]);
                    DateTime? invDate = Util.GetValueOfDateTime(dr["invoice_date"]);
                    DateTime? refDate = Util.GetValueOfDateTime(dr["ref_date"]);

                    rows.Add(new
                    {
                        invoiceNo = Util.GetValueOfString(dr["invoice_no"]),
                        invoiceDate = invDate.HasValue ? invDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                        refNo = Util.GetValueOfString(dr["ref_no"]),
                        refType = Util.GetValueOfString(dr["ref_type"]),
                        refDate = refDate.HasValue ? refDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                        customerName = Util.GetValueOfString(dr["customer_name"]),
                        amount = Util.GetValueOfDecimal(dr["amount"]),
                        status = Util.GetValueOfString(dr["status"])
                    });
                }

                return Ok(new { total = total, rows = rows, currency = GetCurrencyInfo(ctx) });
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
            }
            finally
            {
                if (dr != null) { dr.Close(); dr.Dispose(); }
            }
        }

        private string NormalizePeriod(string period)
        {
            if (string.Equals(period, "quarter", StringComparison.OrdinalIgnoreCase)) { return "quarter"; }
            if (string.Equals(period, "fy", StringComparison.OrdinalIgnoreCase)) { return "fy"; }
            return "month";
        }

        private string NormalizeCategory(string category)
        {
            if (string.Equals(category, "completed", StringComparison.OrdinalIgnoreCase)) { return "completed"; }
            if (string.Equals(category, "open", StringComparison.OrdinalIgnoreCase)) { return "open"; }
            return "raised";
        }

        /// <summary>
        /// The system currency (the session's base currency, $C_Currency_ID) as
        /// ISO code + symbol, so the widget shows amounts in the tenant's real
        /// currency instead of a hardcoded '$'. Returns empties if unavailable.
        /// </summary>
        private object GetCurrencyInfo(Ctx ctx)
        {
            string iso = "";
            string symbol = "";
            int currencyId = ctx.GetContextAsInt("$C_Currency_ID");
            if (currencyId > 0)
            {
                IDataReader cdr = null;
                try
                {
                    cdr = DB.ExecuteReader(
                        "SELECT ISO_Code, CurSymbol FROM C_Currency WHERE C_Currency_ID = @Cur",
                        new SqlParameter[] { new SqlParameter("@Cur", currencyId) });
                    if (cdr != null && cdr.Read())
                    {
                        iso = Util.GetValueOfString(cdr["ISO_Code"]);
                        symbol = Util.GetValueOfString(cdr["CurSymbol"]);
                    }
                }
                finally { if (cdr != null) { cdr.Close(); cdr.Dispose(); } }
            }
            return new { iso = iso, symbol = symbol };
        }

        /// <summary>Wraps a success payload as a serialized JSON result.</summary>
        private JsonResult Ok(object result)
        {
            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }

        /// <summary>Wraps a failure message as a serialized JSON result.</summary>
        private JsonResult Fail(string message)
        {
            return Json(JsonConvert.SerializeObject(new
            {
                success = false,
                error = message,
                message = message
            }), JsonRequestBehavior.AllowGet);
        }
    }
}
