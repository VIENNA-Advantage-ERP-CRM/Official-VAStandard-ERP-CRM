/******************************************************
 * Module Name    : VASLogic
 * Purpose        : Unmatched GRNs & Invoices dashboard widget data
 * chronological  : Development
 * Created Date   : 2026-08-20
 * Created by     : VAI145
 ******************************************************/

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Text;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;

namespace VASLogic.Models
{
    /// <summary>
    /// Module Name : VAS_201_UnMatchedGRNInvoice
    /// Purpose     : Backs the VAS_201_UnMatchedGRNInvoiceWidget dashboard widget.
    ///               The three-way-match exceptions of ONE open accounting period,
    ///               each with a document count and a value in the tenant's base
    ///               currency, and each openable as a paged record list:
    ///
    ///                 GRNs not invoiced      a completed receipt no purchase
    ///                                        invoice line points at yet
    ///                 Invoices without GRN   a completed purchase invoice none of
    ///                                        whose MATCHABLE lines points at a receipt
    ///                                        line. Charge lines are not matchable - a
    ///                                        freight or duty charge is never received
    ///                                        into stock - so they are excluded, and an
    ///                                        invoice made up only of charges is not an
    ///                                        exception at all
    ///                 Qty / price mismatches receipt and invoice ARE matched, but
    ///                                        the quantities or the prices differ
    ///
    ///               Period source: the open periods of the tenant's PRIMARY
    ///               calendar (AD_ClientInfo.C_Calendar_ID) - a period qualifies when
    ///               at least one active C_PeriodControl row of it is Open. Nothing
    ///               is derived from the calendar month. GRN exceptions are bounded
    ///               by M_InOut.DateAcct, invoice exceptions by C_Invoice.DateAcct.
    ///
    ///               Valuation:
    ///                 GRN line with an order line   (LineNetAmt / QtyOrdered) *
    ///                                               MovementQty, converted from the
    ///                                               ORDER's currency at its DateAcct
    ///                 GRN line without one          COALESCE(PostCurrentCostPrice,
    ///                                               CurrentCostPrice) * MovementQty,
    ///                                               already a base-currency cost, so
    ///                                               deliberately NOT converted again
    ///                 Invoice without GRN           GrandTotal, converted
    ///                 Mismatch                      ABS(MovementQty * order price -
    ///                                               QtyInvoiced * invoice price);
    ///                                               kept in the invoice currency for
    ///                                               the list, converted for the card
    ///
    ///               A receipt line can be invoiced across several invoice lines, so
    ///               the mismatch query aggregates the invoiced quantity per receipt
    ///               line FIRST (window functions in a second CTE) and then compares
    ///               once - never once per invoice line, which would multiply the
    ///               same variance out. The window functions sit in their OWN CTE
    ///               because AddAccessSQL cannot be applied to a SELECT whose OVER()
    ///               clause carries an ORDER BY.
    ///
    ///               MRole row-level security is applied to the main physical table
    ///               of each user-facing query: C_Period alias p, M_InOut alias io,
    ///               C_Invoice alias i - and for the mismatch query, to C_Invoice
    ///               INSIDE the CTE body, never to the CTE alias or to the combined
    ///               statement. Joined C_Order / C_OrderLine / C_BPartner /
    ///               C_Currency / M_Product rows are lookups and inherit that filter.
    ///               GROUP BY, ORDER BY and the paging suffix are appended AFTER
    ///               AddAccessSQL so the FROM-clause parser is not confused by a
    ///               trailing clause. Compatible with PostgreSQL and Oracle.
    /// Chronological development:
    ///   VAI145      2026-08-20 Created
    ///   VAI145      2026-08-24 Invoice-without-GRN check excludes charge lines
    /// </summary>
    public class VAS_201_UnMatchedGRNInvoiceModel
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_201_UnMatchedGRNInvoiceModel).FullName);

        /* Exception tokens exchanged with the client. The client maps each to a
           localized AD_Message label, so no display text is produced here. */
        public const string CATEGORY_GRN_NOT_INVOICED = "GRNNOTINV";
        public const string CATEGORY_INVOICE_NO_GRN = "INVNOGRN";
        public const string CATEGORY_MISMATCH = "MISMATCH";

        /* Variance tokens for the mismatch list. */
        public const string VARIANCE_QTY = "QTY";
        public const string VARIANCE_PRICE = "PRICE";
        public const string VARIANCE_BOTH = "BOTH";

        /* Error tokens; the client resolves the label. */
        public const string ERROR_INVALID_REQUEST = "INVALID";
        public const string ERROR_NO_PERIOD = "NOPERIOD";

        /* C_PeriodControl.PeriodStatus stored code for an open control row. */
        private const string PERIODSTATUS_Open = "O";

        /* Match tolerances. Zero unless the business configures otherwise - they are
           compile-time constants, never client input, and are inlined into the SQL
           for that reason. Changing a tolerance is one edit here. */
        private const string QTY_TOLERANCE = "0";
        private const string PRICE_TOLERANCE = "0";

        /* Detail paging guard rails. The client asks for a page size; anything
           outside this band is clamped so a crafted request cannot pull a whole
           table into one response. */
        private const int PAGESIZE_MIN = 1;
        private const int PAGESIZE_MAX = 100;
        private const int PAGESIZE_DEFAULT = 8;

        // ─────────────────────────────────────────────────────────────────────
        // §1  Period list and bootstrap
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Bootstraps the widget in one round trip: every selectable open period of
        /// the primary calendar, the period to preselect, and that period's three
        /// exception figures.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <returns>Populated <see cref="MatchBootstrap"/> (never null).</returns>
        public MatchBootstrap GetBootstrap(Ctx ctx)
        {
            MatchBootstrap result = new MatchBootstrap();
            result.Periods = new List<PeriodItem>();
            result.Data = new PeriodData();

            if (ctx == null) { return result; }

            result.Periods = GetOpenPeriods(ctx);
            if (result.Periods.Count == 0) { return result; }

            PeriodItem selected = PickDefaultPeriod(result.Periods, DateTime.Now.Date);
            result.C_Period_ID = selected.C_Period_ID;
            result.PeriodName = selected.Name;
            result.Data = GetPeriodData(ctx, selected.C_Period_ID);

            return result;
        }

        /// <summary>
        /// The open periods of the tenant's PRIMARY calendar, newest first. A period
        /// qualifies when at least one of its active C_PeriodControl rows is Open; it
        /// appears once however many document base types are open for it.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <returns>Open periods, newest StartDate first (never null).</returns>
        public List<PeriodItem> GetOpenPeriods(Ctx ctx)
        {
            List<PeriodItem> items = new List<PeriodItem>();
            if (ctx == null) { return items; }

            /* AD_ClientInfo pins the calendar to the tenant rather than searching all
               calendars, and the open-control test is an EXISTS predicate, not a
               join, so several open base types cannot multiply the period out. */
            string sql = @"
                SELECT p.C_Period_ID AS C_Period_ID,
                       p.Name AS Period_Name,
                       p.StartDate AS Start_Date,
                       p.EndDate AS End_Date,
                       y.C_Year_ID AS C_Year_ID,
                       y.FiscalYear AS Fiscal_Year,
                       ci.C_Calendar_ID AS C_Calendar_ID
                FROM C_Period p
                INNER JOIN C_Year y ON (y.C_Year_ID=p.C_Year_ID)
                INNER JOIN AD_ClientInfo ci ON (ci.C_Calendar_ID=y.C_Calendar_ID AND ci.AD_Client_ID=@AD_Client_ID)
                WHERE p.IsActive='Y'
                  AND y.IsActive='Y'
                  AND EXISTS(SELECT 1 FROM C_PeriodControl pc WHERE pc.C_Period_ID=p.C_Period_ID AND pc.IsActive='Y' AND pc.PeriodStatus=@PeriodStatus)";

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "p", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            sql += " ORDER BY p.StartDate DESC,p.EndDate DESC,p.C_Period_ID DESC";

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@PeriodStatus", PERIODSTATUS_Open)
            };

            DataSet ds = DB.ExecuteDataset(sql, parameters, null);
            if (ds == null || ds.Tables.Count == 0) { return items; }

            DataTable dt = ds.Tables[0];
            for (int i = 0; i < dt.Rows.Count; i++)
            {
                PeriodItem item = new PeriodItem();
                item.C_Period_ID = Util.GetValueOfInt(dt.Rows[i]["C_Period_ID"]);
                item.Name = Util.GetValueOfString(dt.Rows[i]["Period_Name"]);
                item.StartDate = Util.GetValueOfDateTime(dt.Rows[i]["Start_Date"]);
                item.EndDate = Util.GetValueOfDateTime(dt.Rows[i]["End_Date"]);
                item.C_Year_ID = Util.GetValueOfInt(dt.Rows[i]["C_Year_ID"]);
                item.FiscalYear = Util.GetValueOfString(dt.Rows[i]["Fiscal_Year"]);
                item.C_Calendar_ID = Util.GetValueOfInt(dt.Rows[i]["C_Calendar_ID"]);
                items.Add(item);
            }

            return items;
        }

        /// <summary>
        /// Chooses which open period the widget opens on: the one containing today,
        /// otherwise the most recent one that has already started, otherwise the
        /// first of the list. The list is newest-first, so the first match wins.
        /// </summary>
        /// <param name="periods">Open periods, newest StartDate first.</param>
        /// <param name="today">Current application date (date part only).</param>
        /// <returns>The period to preselect (never null when the list is filled).</returns>
        private PeriodItem PickDefaultPeriod(List<PeriodItem> periods, DateTime today)
        {
            PeriodItem started = null;

            for (int i = 0; i < periods.Count; i++)
            {
                PeriodItem item = periods[i];
                if (!item.StartDate.HasValue || !item.EndDate.HasValue) { continue; }

                DateTime from = item.StartDate.Value.Date;
                DateTime to = item.EndDate.Value.Date;

                if (from <= today && to >= today) { return item; }
                if (started == null && from <= today) { started = item; }
            }

            return started != null ? started : periods[0];
        }

        /// <summary>
        /// Re-reads one period and confirms it is still active, accessible, open and
        /// on the tenant's primary calendar. The client only ever sends the id; the
        /// date range the queries run against always comes from here.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="periodId">C_Period_ID the client selected.</param>
        /// <returns>Populated <see cref="PeriodItem"/>, or null when it no longer qualifies.</returns>
        private PeriodItem GetOpenPeriod(Ctx ctx, int periodId)
        {
            if (ctx == null || periodId <= 0) { return null; }

            string sql = @"
                SELECT p.C_Period_ID AS C_Period_ID,
                       p.Name AS Period_Name,
                       p.StartDate AS Start_Date,
                       p.EndDate AS End_Date
                FROM C_Period p
                INNER JOIN C_Year y ON (y.C_Year_ID=p.C_Year_ID)
                INNER JOIN AD_ClientInfo ci ON (ci.C_Calendar_ID=y.C_Calendar_ID AND ci.AD_Client_ID=@AD_Client_ID)
                WHERE p.C_Period_ID=@C_Period_ID
                  AND p.IsActive='Y'
                  AND y.IsActive='Y'
                  AND EXISTS(SELECT 1 FROM C_PeriodControl pc WHERE pc.C_Period_ID=p.C_Period_ID AND pc.IsActive='Y' AND pc.PeriodStatus=@PeriodStatus)";

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "p", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@C_Period_ID", periodId),
                new SqlParameter("@PeriodStatus", PERIODSTATUS_Open)
            };

            DataSet ds = DB.ExecuteDataset(sql, parameters, null);
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) { return null; }

            DataRow row = ds.Tables[0].Rows[0];

            PeriodItem item = new PeriodItem();
            item.C_Period_ID = Util.GetValueOfInt(row["C_Period_ID"]);
            item.Name = Util.GetValueOfString(row["Period_Name"]);
            item.StartDate = Util.GetValueOfDateTime(row["Start_Date"]);
            item.EndDate = Util.GetValueOfDateTime(row["End_Date"]);

            return item;
        }

        // ─────────────────────────────────────────────────────────────────────
        // §2  The three exception figures
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The three exception counts and base-currency values of one period. Three
        /// queries, one per exception - they read different tables, so there is no
        /// single pass to share - and never one query per document.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="periodId">C_Period_ID selected by the user.</param>
        /// <returns>Populated <see cref="PeriodData"/> (never null).</returns>
        public PeriodData GetPeriodData(Ctx ctx, int periodId)
        {
            PeriodData result = new PeriodData();
            result.C_Period_ID = periodId;
            result.Exceptions = new List<ExceptionTotal>();

            if (ctx == null) { return result; }

            PeriodItem period = GetOpenPeriod(ctx, periodId);
            if (period == null)
            {
                result.ErrorCode = ERROR_NO_PERIOD;
                return result;
            }

            result.PeriodName = period.Name;

            BaseCurrency baseCurrency = GetBaseCurrency(ctx);
            result.BaseCurrencyIso = baseCurrency.Iso;
            result.BaseCurrencySymbol = baseCurrency.Symbol;
            result.BaseCurrencyPrecision = baseCurrency.Precision;

            result.Exceptions.Add(ReadTotal(ctx, period, baseCurrency, CATEGORY_GRN_NOT_INVOICED));
            result.Exceptions.Add(ReadTotal(ctx, period, baseCurrency, CATEGORY_INVOICE_NO_GRN));
            result.Exceptions.Add(ReadTotal(ctx, period, baseCurrency, CATEGORY_MISMATCH));

            return result;
        }

        /// <summary>
        /// Count and base value of one exception category.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="period">Validated period carrying the date bounds.</param>
        /// <param name="baseCurrency">Tenant base currency for the conversions.</param>
        /// <param name="category">One of the CATEGORY_* tokens.</param>
        /// <returns>Populated <see cref="ExceptionTotal"/> (never null).</returns>
        private ExceptionTotal ReadTotal(Ctx ctx, PeriodItem period, BaseCurrency baseCurrency,
            string category)
        {
            ExceptionTotal total = new ExceptionTotal();
            total.Category = category;

            string sql;
            List<SqlParameter> parameters = new List<SqlParameter>();

            if (CATEGORY_GRN_NOT_INVOICED.Equals(category))
            {
                /* COUNT(DISTINCT document) with the value summed over its lines:
                   the widget counts GRN documents, not receipt lines. */
                sql = @"
                SELECT COUNT(DISTINCT io.M_InOut_ID) AS Record_Count,
                       SUM(" + GrnLineValueExpr(baseCurrency, 1) + @") AS Base_Value
                " + GrnNotInvoicedFrom() + " WHERE " + GrnNotInvoicedWhere();

                sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "io", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                AddBaseCurrencyParameters(parameters, baseCurrency, 1);
                parameters.AddRange(PeriodBoundParameters(period));
            }
            else if (CATEGORY_INVOICE_NO_GRN.Equals(category))
            {
                sql = @"
                SELECT COUNT(1) AS Record_Count,
                       SUM(" + InvoiceValueExpr(baseCurrency, 1) + @") AS Base_Value
                FROM C_Invoice i
                INNER JOIN C_Invoiceline il ON (i.C_Invoice_ID=il.C_Invoice_ID)
                WHERE " + InvoiceNoGrnWhere();

                sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "i", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                AddBaseCurrencyParameters(parameters, baseCurrency, 1);
                parameters.AddRange(PeriodBoundParameters(period));
            }
            else
            {
                /* The mismatch set is one row per RECEIPT LINE (see MismatchCore),
                   so the document count is the distinct GRNs behind those lines. */
                sql = MismatchCore(ctx, period, baseCurrency, parameters,
                    "SELECT COUNT(DISTINCT MismatchLines.M_InOut_ID) AS Record_Count,"
                    + "SUM(MismatchLines.Mismatch_Value) AS Base_Value FROM (",
                    ") MismatchLines");
            }

            DataSet ds = DB.ExecuteDataset(sql, parameters.ToArray(), null);
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) { return total; }

            DataRow row = ds.Tables[0].Rows[0];
            total.RecordCount = Util.GetValueOfInt(row["Record_Count"]);
            total.BaseValue = Util.GetValueOfDecimal(row["Base_Value"]);

            return total;
        }

        // ─────────────────────────────────────────────────────────────────────
        // §3  Detail (server-side paging)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// One page of the documents behind an exception, plus the total row count so
        /// the client can page without holding the whole set. Detail rows are only
        /// ever read through this method - the card itself never loads them.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="periodId">C_Period_ID selected by the user.</param>
        /// <param name="category">One of the CATEGORY_* tokens.</param>
        /// <param name="pageNo">1-based page number.</param>
        /// <param name="pageSize">Rows per page (clamped server-side).</param>
        /// <returns>Populated <see cref="RecordPage"/> (never null).</returns>
        public RecordPage GetRecords(Ctx ctx, int periodId, string category, int pageNo, int pageSize)
        {
            RecordPage result = new RecordPage();
            result.Rows = new List<ExceptionRow>();
            result.Category = category;
            result.C_Period_ID = periodId;

            if (ctx == null) { return result; }

            if (!CATEGORY_GRN_NOT_INVOICED.Equals(category)
                && !CATEGORY_INVOICE_NO_GRN.Equals(category)
                && !CATEGORY_MISMATCH.Equals(category))
            {
                result.ErrorCode = ERROR_INVALID_REQUEST;
                return result;
            }

            PeriodItem period = GetOpenPeriod(ctx, periodId);
            if (period == null)
            {
                result.ErrorCode = ERROR_NO_PERIOD;
                return result;
            }

            result.PeriodName = period.Name;

            if (pageSize < PAGESIZE_MIN || pageSize > PAGESIZE_MAX) { pageSize = PAGESIZE_DEFAULT; }
            if (pageNo < 1) { pageNo = 1; }

            result.PageSize = pageSize;

            BaseCurrency baseCurrency = GetBaseCurrency(ctx);
            result.BaseCurrencyIso = baseCurrency.Iso;
            result.BaseCurrencySymbol = baseCurrency.Symbol;
            result.BaseCurrencyPrecision = baseCurrency.Precision;

            /* The category total already IS the row count of its list, so the count
               query is the same one the card ran - no second counting shape. */
            ExceptionTotal total = ReadTotal(ctx, period, baseCurrency, category);
            result.Total = total.RecordCount;

            if (result.Total == 0)
            {
                result.PageNo = 1;
                return result;
            }

            int totalPages = (result.Total + pageSize - 1) / pageSize;
            if (pageNo > totalPages) { pageNo = totalPages; }
            result.PageNo = pageNo;

            if (CATEGORY_GRN_NOT_INVOICED.Equals(category))
            {
                result.Rows = ReadGrnRows(ctx, period, baseCurrency, pageNo, pageSize);
            }
            else if (CATEGORY_INVOICE_NO_GRN.Equals(category))
            {
                result.Rows = ReadInvoiceRows(ctx, period, baseCurrency, pageNo, pageSize);
            }
            else
            {
                result.Rows = ReadMismatchRows(ctx, period, baseCurrency, pageNo, pageSize);
            }

            return result;
        }

        /// <summary>
        /// One page of receipts with no purchase invoice line against them, valued
        /// line by line and aggregated to the document.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="period">Validated period carrying the date bounds.</param>
        /// <param name="baseCurrency">Tenant base currency for the conversion.</param>
        /// <param name="pageNo">1-based page number (already clamped).</param>
        /// <param name="pageSize">Rows per page (already clamped).</param>
        /// <returns>Materialised rows (never null).</returns>
        private List<ExceptionRow> ReadGrnRows(Ctx ctx, PeriodItem period, BaseCurrency baseCurrency,
            int pageNo, int pageSize)
        {
            List<ExceptionRow> rows = new List<ExceptionRow>();

            string sql = @"
                SELECT io.M_InOut_ID AS M_InOut_ID,
                       io.DocumentNo AS Document_No,
                       io.DateAcct AS Date_Acct,
                       io.MovementDate AS Movement_Date,
                       io.C_BPartner_ID AS C_BPartner_ID,
                       COALESCE(bp.Name,N'') AS Business_Partner_Name,
                       COUNT(iol.M_InOutLine_ID) AS Line_Count,
                       SUM(" + GrnLineValueExpr(baseCurrency, 1) + @") AS Base_Value
                " + GrnNotInvoicedFrom() + " WHERE " + GrnNotInvoicedWhere();

            string accessSql = MRole.GetDefault(ctx).AddAccessSQL(sql, "io",
                MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            /* GROUP BY / ORDER BY / paging go on AFTER the access SQL. */
            accessSql += " GROUP BY io.M_InOut_ID,io.DocumentNo,io.DateAcct,io.MovementDate,io.C_BPartner_ID,COALESCE(bp.Name,N'')";
            accessSql += " ORDER BY io.DateAcct DESC,io.DocumentNo DESC,io.M_InOut_ID DESC";
            accessSql += PagingSuffix(pageSize, (pageNo - 1) * pageSize);

            List<SqlParameter> parameters = new List<SqlParameter>();
            AddBaseCurrencyParameters(parameters, baseCurrency, 1);
            parameters.AddRange(PeriodBoundParameters(period));

            DataSet ds = DB.ExecuteDataset(accessSql, parameters.ToArray(), null);
            if (ds == null || ds.Tables.Count == 0) { return rows; }

            DataTable dt = ds.Tables[0];
            for (int i = 0; i < dt.Rows.Count; i++)
            {
                DataRow dr = dt.Rows[i];

                ExceptionRow row = new ExceptionRow();
                row.Category = CATEGORY_GRN_NOT_INVOICED;
                row.M_InOut_ID = Util.GetValueOfInt(dr["M_InOut_ID"]);
                row.DocumentNo = Util.GetValueOfString(dr["Document_No"]);
                row.DateAcct = Util.GetValueOfDateTime(dr["Date_Acct"]);
                row.DateOther = Util.GetValueOfDateTime(dr["Movement_Date"]);
                row.C_BPartner_ID = Util.GetValueOfInt(dr["C_BPartner_ID"]);
                row.BusinessPartnerName = Util.GetValueOfString(dr["Business_Partner_Name"]);
                row.LineCount = Util.GetValueOfInt(dr["Line_Count"]);
                row.BaseValue = Util.GetValueOfDecimal(dr["Base_Value"]);

                rows.Add(row);
            }

            return rows;
        }

        /// <summary>
        /// One page of purchase invoices none of whose lines points at a receipt line.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="period">Validated period carrying the date bounds.</param>
        /// <param name="baseCurrency">Tenant base currency for the conversion.</param>
        /// <param name="pageNo">1-based page number (already clamped).</param>
        /// <param name="pageSize">Rows per page (already clamped).</param>
        /// <returns>Materialised rows (never null).</returns>
        private List<ExceptionRow> ReadInvoiceRows(Ctx ctx, PeriodItem period, BaseCurrency baseCurrency,
            int pageNo, int pageSize)
        {
            List<ExceptionRow> rows = new List<ExceptionRow>();

            string sql = @"
                SELECT i.C_Invoice_ID AS C_Invoice_ID,
                       i.DocumentNo AS Document_No,
                       i.DateInvoiced AS Date_Invoiced,
                       i.DateAcct AS Date_Acct,
                       i.C_BPartner_ID AS C_BPartner_ID,
                       COALESCE(bp.Name,N'') AS Business_Partner_Name,
                       i.C_Currency_ID AS C_Currency_ID,
                       cur.ISO_Code AS Currency_Iso,
                       COALESCE(cur.CurSymbol,cur.ISO_Code) AS Currency_Symbol,
                       cur.StdPrecision AS Currency_Precision,
                       COALESCE(i.GrandTotal,0) AS Document_Value,
                       " + InvoiceValueExpr(baseCurrency, 1) + @" AS Base_Value
                FROM C_Invoice i
                INNER JOIN C_Invoiceline il ON (i.C_Invoice_ID=il.C_Invoice_ID)
                INNER JOIN C_Currency cur ON (cur.C_Currency_ID=i.C_Currency_ID)
                LEFT OUTER JOIN C_BPartner bp ON (bp.C_BPartner_ID=i.C_BPartner_ID)
                WHERE " + InvoiceNoGrnWhere();

            string accessSql = MRole.GetDefault(ctx).AddAccessSQL(sql, "i",
                MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            accessSql += " ORDER BY i.DateAcct DESC,i.DocumentNo DESC,i.C_Invoice_ID DESC";
            accessSql += PagingSuffix(pageSize, (pageNo - 1) * pageSize);

            List<SqlParameter> parameters = new List<SqlParameter>();
            AddBaseCurrencyParameters(parameters, baseCurrency, 1);
            parameters.AddRange(PeriodBoundParameters(period));

            DataSet ds = DB.ExecuteDataset(accessSql, parameters.ToArray(), null);
            if (ds == null || ds.Tables.Count == 0) { return rows; }

            DataTable dt = ds.Tables[0];
            for (int i = 0; i < dt.Rows.Count; i++)
            {
                DataRow dr = dt.Rows[i];

                ExceptionRow row = new ExceptionRow();
                row.Category = CATEGORY_INVOICE_NO_GRN;
                row.C_Invoice_ID = Util.GetValueOfInt(dr["C_Invoice_ID"]);
                row.DocumentNo = Util.GetValueOfString(dr["Document_No"]);
                row.DateOther = Util.GetValueOfDateTime(dr["Date_Invoiced"]);
                row.DateAcct = Util.GetValueOfDateTime(dr["Date_Acct"]);
                row.C_BPartner_ID = Util.GetValueOfInt(dr["C_BPartner_ID"]);
                row.BusinessPartnerName = Util.GetValueOfString(dr["Business_Partner_Name"]);
                row.C_Currency_ID = Util.GetValueOfInt(dr["C_Currency_ID"]);
                row.CurrencyIso = Util.GetValueOfString(dr["Currency_Iso"]);
                row.CurrencySymbol = Util.GetValueOfString(dr["Currency_Symbol"]);
                row.CurrencyPrecision = Util.GetValueOfInt(dr["Currency_Precision"]);
                row.DocumentValue = Util.GetValueOfDecimal(dr["Document_Value"]);
                row.BaseValue = Util.GetValueOfDecimal(dr["Base_Value"]);

                rows.Add(row);
            }

            return rows;
        }

        /// <summary>
        /// One page of matched receipt lines whose quantity or price disagrees with
        /// the invoice. One row per RECEIPT LINE, never per invoice line.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="period">Validated period carrying the date bounds.</param>
        /// <param name="baseCurrency">Tenant base currency for the conversion.</param>
        /// <param name="pageNo">1-based page number (already clamped).</param>
        /// <param name="pageSize">Rows per page (already clamped).</param>
        /// <returns>Materialised rows (never null).</returns>
        private List<ExceptionRow> ReadMismatchRows(Ctx ctx, PeriodItem period, BaseCurrency baseCurrency,
            int pageNo, int pageSize)
        {
            List<ExceptionRow> rows = new List<ExceptionRow>();

            List<SqlParameter> parameters = new List<SqlParameter>();
            string sql = MismatchCore(ctx, period, baseCurrency, parameters, "", "");

            sql += " ORDER BY Invoice_Date_Acct DESC,Grn_Document_No DESC,M_InOutLine_ID DESC";
            sql += PagingSuffix(pageSize, (pageNo - 1) * pageSize);

            DataSet ds = DB.ExecuteDataset(sql, parameters.ToArray(), null);
            if (ds == null || ds.Tables.Count == 0) { return rows; }

            DataTable dt = ds.Tables[0];
            for (int i = 0; i < dt.Rows.Count; i++)
            {
                DataRow dr = dt.Rows[i];

                ExceptionRow row = new ExceptionRow();
                row.Category = CATEGORY_MISMATCH;
                row.M_InOut_ID = Util.GetValueOfInt(dr["M_InOut_ID"]);
                row.DocumentNo = Util.GetValueOfString(dr["Grn_Document_No"]);
                row.C_Invoice_ID = Util.GetValueOfInt(dr["C_Invoice_ID"]);
                row.SecondaryDocumentNo = Util.GetValueOfString(dr["Invoice_Document_No"]);
                row.DateAcct = Util.GetValueOfDateTime(dr["Invoice_Date_Acct"]);
                row.BusinessPartnerName = Util.GetValueOfString(dr["Business_Partner_Name"]);
                row.ProductName = Util.GetValueOfString(dr["Product_Name"]);
                row.GrnQty = Util.GetValueOfDecimal(dr["Movement_Qty"]);
                row.InvoiceQty = Util.GetValueOfDecimal(dr["Qty_Invoiced"]);
                row.OrderPrice = Util.GetValueOfDecimal(dr["Order_Price"]);
                row.InvoicePrice = Util.GetValueOfDecimal(dr["Invoice_Price"]);

                /* Two figures, deliberately: DocumentValue is the difference in the
                   invoice's OWN currency (what the buyer sees on the document, and the
                   currency the two price figures are quoted in), BaseValue the same
                   difference converted to the tenant base currency so the card total
                   can add rows in different currencies together. */
                row.C_Currency_ID = Util.GetValueOfInt(dr["C_Currency_ID"]);
                row.CurrencyIso = Util.GetValueOfString(dr["Currency_Iso"]);
                row.CurrencySymbol = Util.GetValueOfString(dr["Currency_Symbol"]);
                row.CurrencyPrecision = Util.GetValueOfInt(dr["Currency_Precision"]);
                row.DocumentValue = Util.GetValueOfDecimal(dr["Mismatch_Value_Doc"]);
                row.BaseValue = Util.GetValueOfDecimal(dr["Mismatch_Value"]);

                bool qtyOff = "Y".Equals(Util.GetValueOfString(dr["Is_Qty_Mismatch"]));
                bool priceOff = "Y".Equals(Util.GetValueOfString(dr["Is_Price_Mismatch"]));
                row.VarianceType = qtyOff && priceOff ? VARIANCE_BOTH : (qtyOff ? VARIANCE_QTY : VARIANCE_PRICE);

                rows.Add(row);
            }

            return rows;
        }

        // ─────────────────────────────────────────────────────────────────────
        // §4  Query building blocks
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// FROM clause shared by the GRN-not-invoiced count and list.
        /// </summary>
        /// <returns>FROM / JOIN fragment.</returns>
        private string GrnNotInvoicedFrom()
        {
            return @"
                FROM M_InOut io
                INNER JOIN M_InOutLine iol ON (iol.M_InOut_ID=io.M_InOut_ID)
                LEFT OUTER JOIN C_OrderLine ol ON (ol.C_OrderLine_ID=iol.C_OrderLine_ID)
                LEFT OUTER JOIN C_Order o ON (o.C_Order_ID=ol.C_Order_ID)
                LEFT OUTER JOIN C_BPartner bp ON (bp.C_BPartner_ID=io.C_BPartner_ID)";
        }

        /// <summary>
        /// WHERE clause shared by the GRN-not-invoiced count and list: a completed
        /// vendor receipt, inside the period by DateAcct, with no purchase invoice
        /// line against any of its receipt lines. NOT EXISTS rather than an anti-join
        /// so a receipt line invoiced twice cannot multiply the receipt out.
        /// </summary>
        /// <returns>WHERE fragment binding @StartDate and @EndDate.</returns>
        private string GrnNotInvoicedWhere()
        {
            return @"io.IsActive='Y'
                  AND iol.IsActive='Y'
                  AND io.IsSOTrx='N'
                  AND COALESCE(io.IsReturnTrx,'N')='N'
                  AND io.DocStatus IN ('CO','CL')
                  AND " + DateWhere("io") + @"
                  AND NOT EXISTS(SELECT 1 FROM C_InvoiceLine il INNER JOIN C_Invoice i ON (i.C_Invoice_ID=il.C_Invoice_ID) WHERE il.M_InOutLine_ID=iol.M_InOutLine_ID AND il.IsActive='Y' AND " + PurchaseInvoiceScope("i") + ")";
        }

        /// <summary>
        /// WHERE clause shared by the invoice-without-GRN count and list: a completed
        /// purchase invoice, inside the period by DateAcct, none of whose active
        /// MATCHABLE lines carries a receipt-line reference.
        ///
        /// CHARGE LINES ARE NOT MATCHABLE and are excluded from both halves of the test.
        /// A charge - freight, duty, a handling fee - is never received into stock, so
        /// it can never carry an M_InOutLine_ID and its absence proves nothing.
        ///
        /// Both halves matter, and the EXISTS is not optional: excluding charges from
        /// the NOT EXISTS alone would make an invoice consisting ENTIRELY of charge
        /// lines look worse, not better - it has no non-charge line pointing at a
        /// receipt, so it would satisfy the test and be reported as an unmatched
        /// invoice. The EXISTS is what says "this invoice has something that could have
        /// been received in the first place".
        ///
        /// COALESCE rather than IS NULL because an unset FK is stored as 0 here.
        /// Chronological development:
        ///   VAI154      2026-08-24 Charge lines excluded
        /// </summary>
        /// <returns>WHERE fragment binding @StartDate and @EndDate.</returns>
        private string InvoiceNoGrnWhere()
        {
            return PurchaseInvoiceScope("i") + @"
                  AND " + DateWhere("i") + @"
                  AND il.M_Product_ID IS NOT NULL 
                  AND EXISTS(SELECT 1 FROM C_InvoiceLine ilm WHERE ilm.C_Invoice_ID=i.C_Invoice_ID AND ilm.IsActive='Y' AND COALESCE(ilm.C_Charge_ID,0)=0)
                  AND (ABS(COALESCE(il.QtyInvoiced,0))-COALESCE((SELECT SUM(ABS(COALESCE(mi.Qty,0))) FROM M_MatchInv mi
                    WHERE mi.C_InvoiceLine_ID=il.C_InvoiceLine_ID
                      AND mi.IsActive='Y' AND COALESCE(mi.Processed,'Y')='Y'),0))>0
                  /*AND NOT EXISTS(SELECT 1 FROM C_InvoiceLine il WHERE il.C_Invoice_ID=i.C_Invoice_ID AND il.IsActive='Y' AND COALESCE(il.C_Charge_ID,0)=0 AND il.M_InOutLine_ID IS NOT NULL)*/";
        }

        /// <summary>
        /// What counts as a purchase invoice here: a vendor invoice that is neither an
        /// expense invoice nor a return, completed or closed.
        /// </summary>
        /// <param name="alias">C_Invoice alias.</param>
        /// <returns>Predicate fragment.</returns>
        private string PurchaseInvoiceScope(string alias)
        {
            return alias + ".IsActive='Y' AND " + alias + ".IsSOTrx='N'"
                + " AND COALESCE(" + alias + ".IsExpenseInvoice,'N')='N'"
                + " AND COALESCE(" + alias + ".IsReturnTrx,'N')='N'"
                + " AND " + alias + ".DocStatus IN ('CO','CL')";
        }

        /// <summary>
        /// The period bounds, applied to a document's accounting date.
        /// </summary>
        /// <param name="alias">Document alias carrying DateAcct.</param>
        /// <returns>Predicate fragment binding @StartDate and @EndDate.</returns>
        private string DateWhere(string alias)
        {
            /* Compare date values only. Oracle DATE always carries a time part
               (TRUNC drops it); PostgreSQL needs an explicit CAST when the column is
               materialised as a timestamp. Only the COLUMN side is normalised - the
               bind values are already midnight, and casting a bind variable leaves
               its type undetermined on PostgreSQL. */
            return DB.IsOracle()
                ? "TRUNC(" + alias + ".DateAcct)>=@StartDate AND TRUNC(" + alias + ".DateAcct)<=@EndDate"
                : "CAST(" + alias + ".DateAcct AS DATE)>=@StartDate AND CAST(" + alias + ".DateAcct AS DATE)<=@EndDate";
        }

        /// <summary>
        /// The base-currency value of ONE receipt line.
        ///
        /// With an order line, the purchase-order line value is proportioned by the
        /// received quantity - (LineNetAmt / QtyOrdered) * MovementQty - and converted
        /// from the ORDER's currency at the order's accounting date. Without one, the
        /// receipt line's own stored cost is used, and deliberately NOT converted: it
        /// is already a base-currency cost.
        /// </summary>
        /// <param name="baseCurrency">Tenant base currency.</param>
        /// <param name="ordinal">1-based occurrence number - each occurrence carries
        /// its own parameter name, because a repeated name is ambiguous under the
        /// positional binding the backend adapters use.</param>
        /// <returns>SQL expression.</returns>
        private string GrnLineValueExpr(BaseCurrency baseCurrency, int ordinal)
        {
            string costValue = "ABS(COALESCE(NULLIF(iol.PostCurrentCostPrice, 0),iol.CurrentCostPrice,0)*COALESCE(iol.MovementQty,0))";

            if (baseCurrency.C_Currency_ID <= 0) { return costValue; }

            string p1 = ParamName(ordinal, "A");
            string p2 = ParamName(ordinal, "B");
            string orderValue = "((COALESCE(ol.LineNetAmt,0)/NULLIF(ol.QtyOrdered,0))*COALESCE(iol.MovementQty,0))";

            return "CASE WHEN iol.C_OrderLine_ID IS NOT NULL THEN"
                 + " CASE WHEN o.C_Currency_ID=" + p1 + " THEN ABS(" + orderValue + ")"
                 + " ELSE ABS(COALESCE(currencyConvert(" + orderValue + ",o.C_Currency_ID," + p2
                 + ",o.DateAcct,o.C_ConversionType_ID,o.AD_Client_ID,o.AD_Org_ID),0)) END"
                 + " ELSE " + costValue + " END";
        }

        /// <summary>
        /// The base-currency value of one purchase invoice: its GrandTotal, converted
        /// at the invoice's accounting date.
        /// </summary>
        /// <param name="baseCurrency">Tenant base currency.</param>
        /// <param name="ordinal">1-based occurrence number.</param>
        /// <returns>SQL expression.</returns>
        private string InvoiceValueExpr(BaseCurrency baseCurrency, int ordinal)
        {
            if (baseCurrency.C_Currency_ID <= 0) { return "ABS(COALESCE(i.GrandTotal,0))"; }

            string p1 = ParamName(ordinal, "A");
            string p2 = ParamName(ordinal, "B");

            return "CASE WHEN i.C_Currency_ID=" + p1 + " THEN ABS(COALESCE(i.GrandTotal,0))"
                 + " ELSE ABS(COALESCE(currencyConvert(COALESCE(i.GrandTotal,0),i.C_Currency_ID," + p2
                 + ",i.DateAcct,i.C_ConversionType_ID,i.AD_Client_ID,i.AD_Org_ID),0)) END";
        }

        /// <summary>
        /// The mismatch statement, one row per RECEIPT LINE.
        ///
        /// A receipt line can be invoiced across several invoice lines, so the
        /// invoiced quantity is aggregated per receipt line with a window function
        /// and only the first invoice line of each is kept - the outer query
        /// therefore compares ONCE per receipt line and cannot multiply the same
        /// variance out. The representative invoice (the lowest invoice line)
        /// supplies the document shown and the currency the variance is converted
        /// from.
        ///
        /// MRole is applied to C_Invoice inside the FIRST CTE body, which carries no
        /// window function and therefore no ORDER BY for the access-SQL parser to
        /// trip over - never to a CTE alias and never to the combined statement.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="period">Validated period carrying the date bounds.</param>
        /// <param name="baseCurrency">Tenant base currency for the conversion.</param>
        /// <param name="parameters">Bind list being built, in appearance order.</param>
        /// <param name="prefix">Optional wrapper opening (for the aggregate form).</param>
        /// <param name="suffix">Optional wrapper closing.</param>
        /// <returns>Complete statement.</returns>
        private string MismatchCore(Ctx ctx, PeriodItem period, BaseCurrency baseCurrency,
            List<SqlParameter> parameters, string prefix, string suffix)
        {
            /* TWO CTEs, deliberately.

               The access filter has to be applied to a SELECT that contains no
               ORDER BY at all - and a window function carries one INSIDE its OVER()
               clause, which the AddAccessSQL parser reads as the statement's own
               trailing ORDER BY and mishandles. Dropping the OVER(... ORDER BY ...)
               would "fix" the parse at the cost of correctness: without it
               ROW_NUMBER has no defined order, so which invoice line ranks first -
               and therefore which invoice document and currency represent the match
               - would vary between runs.

               So the base CTE is the plain, MRole-filtered row set (main physical
               table C_Invoice, alias i), and the window functions are computed in a
               second CTE over it. MRole is never applied to that second body: it
               reads a derived alias, not a physical table. */
            string baseBody = @"
                SELECT il.M_InOutLine_ID AS M_InOutLine_ID,
                       il.C_InvoiceLine_ID AS C_InvoiceLine_ID,
                       COALESCE(il.QtyInvoiced,0) AS Qty_Invoiced_Line,
                       COALESCE((il.QtyEntered / il.QtyInvoiced) * il.PriceActual,0) AS Price_Actual_Line,
                       i.C_Invoice_ID AS C_Invoice_ID,
                       i.DocumentNo AS Invoice_Document_No,
                       i.DateAcct AS Invoice_Date_Acct,
                       i.C_Currency_ID AS C_Currency_ID,
                       i.C_ConversionType_ID AS C_ConversionType_ID,
                       i.AD_Client_ID AS AD_Client_ID,
                       i.AD_Org_ID AS AD_Org_ID,
                       i.C_BPartner_ID AS C_BPartner_ID
                FROM C_Invoice i
                INNER JOIN C_InvoiceLine il ON (il.C_Invoice_ID=i.C_Invoice_ID)
                WHERE il.IsActive='Y'
                  AND il.M_InOutLine_ID IS NOT NULL
                  AND " + PurchaseInvoiceScope("i") + @"
                  AND " + DateWhere("i");

            baseBody = MRole.GetDefault(ctx).AddAccessSQL(baseBody, "i",
                MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            /* Second CTE: the receipt line's total invoiced quantity and highest
               invoice price, and a deterministic rank so exactly one invoice line
               per receipt line survives. Columns are listed rather than SELECT *. */
            string rankedBody = @"
                SELECT b.M_InOutLine_ID AS M_InOutLine_ID,
                       b.C_Invoice_ID AS C_Invoice_ID,
                       b.Invoice_Document_No AS Invoice_Document_No,
                       b.Invoice_Date_Acct AS Invoice_Date_Acct,
                       b.C_Currency_ID AS C_Currency_ID,
                       b.C_ConversionType_ID AS C_ConversionType_ID,
                       b.AD_Client_ID AS AD_Client_ID,
                       b.AD_Org_ID AS AD_Org_ID,
                       b.C_BPartner_ID AS C_BPartner_ID,
                       SUM(b.Qty_Invoiced_Line) OVER (PARTITION BY b.M_InOutLine_ID) AS Qty_Invoiced,
                       MAX(b.Price_Actual_Line) OVER (PARTITION BY b.M_InOutLine_ID) AS Invoice_Price,
                       ROW_NUMBER() OVER (PARTITION BY b.M_InOutLine_ID ORDER BY b.C_InvoiceLine_ID) AS Line_Seq
                FROM MismatchBase b";

            string grnQty = "COALESCE(iol.MovementQty,0)";
            string invoiceQty = "COALESCE(m.Qty_Invoiced,0)";
            string invoicePrice = "COALESCE(m.Invoice_Price,0)";
            string orderPrice = "COALESCE((ol.QtyEntered / ol.QtyOrdered) * ol.PriceActual,0)";

            string qtyVariance = "ABS(" + invoiceQty + "-" + grnQty + ")";
            string priceVariance = "ABS(" + invoicePrice + "-" + orderPrice + ")";

            /* The two sides valued independently and subtracted: what the receipt is
               worth at the ORDER price against what the invoice actually charges. The
               difference is the money at stake in this line, whichever of the two
               figures is off.

               Without an order line there is no order price - COALESCE would make it
               zero and the whole invoiced value would read as a difference - so that
               row values the receipt at the invoice price instead, leaving the
               quantity gap as the difference. Such a row can only be a quantity
               mismatch anyway: the price test requires an order line.

               Signed here; both use sites wrap it in ABS. */
            string receiptPrice = "CASE WHEN iol.C_OrderLine_ID IS NOT NULL THEN " + orderPrice
                + " ELSE " + invoicePrice + " END";
            string variance = "((" + grnQty + "*(" + receiptPrice + "))-(" + invoiceQty + "*" + invoicePrice + "))";

            string varianceBase;
            if (baseCurrency.C_Currency_ID <= 0)
            {
                varianceBase = "ABS(" + variance + ")";
            }
            else
            {
                string p1 = ParamName(1, "A");
                string p2 = ParamName(1, "B");
                varianceBase = "CASE WHEN m.C_Currency_ID=" + p1 + " THEN ABS(" + variance + ")"
                    + " ELSE ABS(COALESCE(currencyConvert(" + variance + ",m.C_Currency_ID," + p2
                    + ",m.Invoice_Date_Acct,m.C_ConversionType_ID,m.AD_Client_ID,m.AD_Org_ID),0)) END";
            }

            string qtyMismatch = qtyVariance + ">" + QTY_TOLERANCE;
            string priceMismatch = "iol.C_OrderLine_ID IS NOT NULL AND " + priceVariance + ">" + PRICE_TOLERANCE;

            string sql = "WITH MismatchBase AS (" + baseBody + @"
                ),
                MismatchMatch AS (" + rankedBody + @"
                )
                " + prefix + @"
                SELECT io.M_InOut_ID AS M_InOut_ID,
                       io.DocumentNo AS Grn_Document_No,
                       iol.M_InOutLine_ID AS M_InOutLine_ID,
                       m.C_Invoice_ID AS C_Invoice_ID,
                       m.Invoice_Document_No AS Invoice_Document_No,
                       m.Invoice_Date_Acct AS Invoice_Date_Acct,
                       COALESCE(bp.Name,N'') AS Business_Partner_Name,
                       COALESCE(mp.Name,N'') AS Product_Name,
                       COALESCE(iol.MovementQty,0) AS Movement_Qty,
                       COALESCE(m.Qty_Invoiced,0) AS Qty_Invoiced,
                       COALESCE((ol.QtyEntered / ol.QtyOrdered) * ol.PriceActual,0) AS Order_Price,
                       COALESCE(m.Invoice_Price,0) AS Invoice_Price,
                       CASE WHEN " + qtyMismatch + @" THEN 'Y' ELSE 'N' END AS Is_Qty_Mismatch,
                       CASE WHEN " + priceMismatch + @" THEN 'Y' ELSE 'N' END AS Is_Price_Mismatch,
                       m.C_Currency_ID AS C_Currency_ID,
                       cur.ISO_Code AS Currency_Iso,
                       COALESCE(cur.CurSymbol,cur.ISO_Code) AS Currency_Symbol,
                       cur.StdPrecision AS Currency_Precision,
                       ABS(" + variance + @") AS Mismatch_Value_Doc,
                       " + varianceBase + @" AS Mismatch_Value
                FROM MismatchMatch m
                INNER JOIN M_InOutLine iol ON (iol.M_InOutLine_ID=m.M_InOutLine_ID)
                INNER JOIN M_InOut io ON (io.M_InOut_ID=iol.M_InOut_ID)
                INNER JOIN C_Currency cur ON (cur.C_Currency_ID=m.C_Currency_ID)
                LEFT OUTER JOIN C_OrderLine ol ON (ol.C_OrderLine_ID=iol.C_OrderLine_ID)
                LEFT OUTER JOIN C_BPartner bp ON (bp.C_BPartner_ID=m.C_BPartner_ID)
                LEFT OUTER JOIN M_Product mp ON (mp.M_Product_ID=iol.M_Product_ID)
                WHERE m.Line_Seq=1
                  AND iol.IsActive='Y'
                  AND io.IsActive='Y'
                  AND io.IsSOTrx='N'
                  AND COALESCE(io.IsReturnTrx,'N')='N'
                  AND io.DocStatus IN ('CO','CL')
                  AND ((" + qtyMismatch + ") OR (" + priceMismatch + "))" + suffix;

            /* Appearance order: the CTE body binds the period dates, then the outer
               SELECT binds the conversion currency. */
            parameters.AddRange(PeriodBoundParameters(period));
            AddBaseCurrencyParameters(parameters, baseCurrency, 1);

            return sql;
        }

        /// <summary>Parameter name for one occurrence of a conversion expression.</summary>
        /// <param name="ordinal">1-based occurrence number.</param>
        /// <param name="slot">"A" for the equality test, "B" for the conversion.</param>
        /// <returns>Bind name.</returns>
        private string ParamName(int ordinal, string slot)
        {
            return "@BaseCurrencyId" + ordinal.ToString(CultureInfo.InvariantCulture) + slot;
        }

        /// <summary>
        /// Binds the currency id behind every conversion occurrence, in the order the
        /// occurrences appear in the statement.
        /// </summary>
        /// <param name="parameters">List being built.</param>
        /// <param name="baseCurrency">Tenant base currency.</param>
        /// <param name="occurrences">How many times the expression was emitted.</param>
        private void AddBaseCurrencyParameters(List<SqlParameter> parameters, BaseCurrency baseCurrency,
            int occurrences)
        {
            if (baseCurrency.C_Currency_ID <= 0) { return; }

            for (int i = 1; i <= occurrences; i++)
            {
                parameters.Add(new SqlParameter(ParamName(i, "A"), baseCurrency.C_Currency_ID));
                parameters.Add(new SqlParameter(ParamName(i, "B"), baseCurrency.C_Currency_ID));
            }
        }

        /// <summary>
        /// The period bounds every exception query binds. Date parts only, so a
        /// document stamped with a time still falls inside its period.
        /// </summary>
        /// <param name="period">Validated period.</param>
        /// <returns>Bind values for @StartDate and @EndDate.</returns>
        private SqlParameter[] PeriodBoundParameters(PeriodItem period)
        {
            DateTime from = period.StartDate.HasValue ? period.StartDate.Value.Date : DateTime.MinValue;
            DateTime to = period.EndDate.HasValue ? period.EndDate.Value.Date : DateTime.MaxValue.Date;

            return new SqlParameter[]
            {
                new SqlParameter("@StartDate", from),
                new SqlParameter("@EndDate", to)
            };
        }

        /// <summary>
        /// Database-specific paging suffix: OFFSET / FETCH on Oracle, LIMIT / OFFSET
        /// elsewhere. Both values are server-clamped integers, never client text.
        /// </summary>
        /// <param name="pageSize">Rows to fetch.</param>
        /// <param name="offset">Rows to skip.</param>
        /// <returns>Paging clause.</returns>
        private string PagingSuffix(int pageSize, int offset)
        {
            if (pageSize <= 0) { pageSize = PAGESIZE_DEFAULT; }
            if (offset < 0) { offset = 0; }

            if (DB.IsOracle())
            {
                return " OFFSET " + offset + " ROWS FETCH NEXT " + pageSize + " ROWS ONLY";
            }
            return " LIMIT " + pageSize + " OFFSET " + offset;
        }

        /// <summary>
        /// The tenant's base currency: the currency of the primary accounting schema
        /// (AD_ClientInfo.C_AcctSchema1_ID). Reads only system / reference tables
        /// scoped to the session client, so no MRole predicate is applied - the same
        /// treatment the sibling KPI widgets give this lookup.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <returns>Populated <see cref="BaseCurrency"/>; C_Currency_ID is 0 when the
        /// tenant has no primary accounting schema.</returns>
        private BaseCurrency GetBaseCurrency(Ctx ctx)
        {
            BaseCurrency result = new BaseCurrency();
            result.Precision = 2;

            string sql = @"
                SELECT AcctSchema.C_Currency_ID AS Acct_Currency_ID,
                       Currency.StdPrecision AS Std_Precision,
                       CASE WHEN Currency.CurSymbol IS NOT NULL THEN Currency.CurSymbol ELSE Currency.ISO_Code END AS Currency_Symbol,
                       Currency.ISO_Code AS Currency_Iso
                FROM AD_ClientInfo ClientInfo
                INNER JOIN C_AcctSchema AcctSchema ON (AcctSchema.C_AcctSchema_ID=ClientInfo.C_AcctSchema1_ID)
                INNER JOIN C_Currency Currency ON (Currency.C_Currency_ID=AcctSchema.C_Currency_ID)
                WHERE ClientInfo.AD_Client_ID=@AD_Client_ID";

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
            };

            DataSet ds = DB.ExecuteDataset(sql, parameters, null);
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) { return result; }

            DataRow row = ds.Tables[0].Rows[0];
            result.C_Currency_ID = Util.GetValueOfInt(row["Acct_Currency_ID"]);
            result.Precision = Util.GetValueOfInt(row["Std_Precision"]);
            result.Symbol = Util.GetValueOfString(row["Currency_Symbol"]);
            result.Iso = Util.GetValueOfString(row["Currency_Iso"]);

            return result;
        }

        // ─────────────────────────────────────────────────────────────────────
        // §5  Contracts
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>One selectable open accounting period of the primary calendar.</summary>
        public class PeriodItem
        {
            public int C_Period_ID { get; set; }
            public string Name { get; set; }

            /// <summary>Inclusive lower bound applied to the document's DateAcct.</summary>
            public DateTime? StartDate { get; set; }

            /// <summary>Inclusive upper bound applied to the document's DateAcct.</summary>
            public DateTime? EndDate { get; set; }

            public int C_Year_ID { get; set; }
            public string FiscalYear { get; set; }
            public int C_Calendar_ID { get; set; }
        }

        /// <summary>One exception category's headline figures.</summary>
        public class ExceptionTotal
        {
            /// <summary>CATEGORY_* token; the client resolves the label.</summary>
            public string Category { get; set; }

            /// <summary>Documents behind the exception - never lines.</summary>
            public int RecordCount { get; set; }

            /// <summary>Total in the tenant's base currency.</summary>
            public decimal BaseValue { get; set; }
        }

        /// <summary>Everything the card shows for one period.</summary>
        public class PeriodData
        {
            public int C_Period_ID { get; set; }
            public string PeriodName { get; set; }

            public List<ExceptionTotal> Exceptions { get; set; }

            public string BaseCurrencyIso { get; set; }
            public string BaseCurrencySymbol { get; set; }
            public int BaseCurrencyPrecision { get; set; }

            /// <summary>One of the ERROR_* tokens; the client resolves the label.</summary>
            public string ErrorCode { get; set; }
        }

        /// <summary>Period list, default selection and its data, in one payload.</summary>
        public class MatchBootstrap
        {
            public List<PeriodItem> Periods { get; set; }

            public int C_Period_ID { get; set; }
            public string PeriodName { get; set; }

            public PeriodData Data { get; set; }
        }

        /// <summary>
        /// One record behind an exception. The three lists have different shapes, so
        /// each fills only the fields its own columns need - the client renders a
        /// different column set per category.
        /// </summary>
        public class ExceptionRow
        {
            /// <summary>CATEGORY_* token this row belongs to.</summary>
            public string Category { get; set; }

            /// <summary>Receipt, where the row has one (GRN and mismatch lists).</summary>
            public int M_InOut_ID { get; set; }

            /// <summary>Invoice, where the row has one (invoice and mismatch lists).</summary>
            public int C_Invoice_ID { get; set; }

            /// <summary>The row's primary document number.</summary>
            public string DocumentNo { get; set; }

            /// <summary>The matched invoice's number, on a mismatch row.</summary>
            public string SecondaryDocumentNo { get; set; }

            public DateTime? DateAcct { get; set; }

            /// <summary>MovementDate on a GRN row, DateInvoiced on an invoice row.</summary>
            public DateTime? DateOther { get; set; }

            public int C_BPartner_ID { get; set; }
            public string BusinessPartnerName { get; set; }

            /// <summary>Receipt lines behind a GRN row.</summary>
            public int LineCount { get; set; }

            /* Mismatch detail. */
            public string ProductName { get; set; }
            public decimal GrnQty { get; set; }
            public decimal InvoiceQty { get; set; }
            public decimal OrderPrice { get; set; }
            public decimal InvoicePrice { get; set; }

            /// <summary>VARIANCE_* token: what disagrees.</summary>
            public string VarianceType { get; set; }

            /* Invoice document currency (invoice and mismatch lists). */
            public int C_Currency_ID { get; set; }
            public string CurrencyIso { get; set; }
            public string CurrencySymbol { get; set; }
            public int CurrencyPrecision { get; set; }

            /// <summary>
            /// The document's own value in its own currency - the invoice total on an
            /// invoice row, the unconverted variance on a mismatch row.
            /// </summary>
            public decimal DocumentValue { get; set; }

            /// <summary>The exception's value in the tenant's base currency.</summary>
            public decimal BaseValue { get; set; }
        }

        /// <summary>One page of detail plus the paging state.</summary>
        public class RecordPage
        {
            public string Category { get; set; }
            public int C_Period_ID { get; set; }
            public string PeriodName { get; set; }

            public List<ExceptionRow> Rows { get; set; }

            public int Total { get; set; }
            public int PageNo { get; set; }
            public int PageSize { get; set; }

            public string BaseCurrencyIso { get; set; }
            public string BaseCurrencySymbol { get; set; }
            public int BaseCurrencyPrecision { get; set; }

            /// <summary>One of the ERROR_* tokens; the client resolves the label.</summary>
            public string ErrorCode { get; set; }
        }

        /// <summary>The tenant's primary accounting-schema currency.</summary>
        private class BaseCurrency
        {
            public int C_Currency_ID { get; set; }
            public string Iso { get; set; }
            public string Symbol { get; set; }
            public int Precision { get; set; }
        }
    }
}
