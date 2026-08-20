/******************************************************
 * Module Name    : VASLogic
 * Purpose        : Payment Allocation Status dashboard widget data
 * chronological  : Development
 * Created Date   : 2026-08-20
 * Created by     : VAI154
 ******************************************************/

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Text;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;

namespace VASLogic.Models
{
    /// <summary>
    /// Module Name : VAS_199_PaymentAllocationStatus
    /// Purpose     : Backs the VAS_199_PaymentAllocationStatusWidget dashboard
    ///               widget. Classifies the completed / closed payments of ONE
    ///               accounting period into three mutually exclusive buckets and
    ///               serves the records behind each bucket a page at a time.
    ///
    ///               Period source: the selectable periods are the active periods
    ///               that carry at least one active C_PeriodControl row with
    ///               PeriodStatus 'O'. Nothing is derived from the calendar month -
    ///               the accounting period is whatever C_PeriodControl says is open,
    ///               and the selected period's own StartDate / EndDate bound
    ///               C_Payment.DateAcct (never DateTrx, never a month function).
    ///
    ///               Classification priority (a payment appears in exactly one
    ///               bucket, in this order):
    ///                 IsAllocated='Y'                      -> Allocated (CO / CL)
    ///                 IsPrepayment='Y' or advance charge   -> Advances / prepayments
    ///                 otherwise                            -> Settlement, not allocated
    ///               C_Payment.IsAllocated is the authoritative completeness flag;
    ///               allocation lines are never summed to re-derive it.
    ///
    ///               The three counts come from ONE aggregated query - never one
    ///               query per row - and the detail rows are not touched until the
    ///               user opens a bucket, which then pages server-side (COUNT first,
    ///               then one page of rows).
    ///
    ///               MRole row-level security is applied to the main physical table
    ///               of every query (C_Period alias p, C_Payment alias p); the joined
    ///               C_Year / C_Calendar / C_DocType / C_Charge / C_BPartner /
    ///               C_Currency rows are lookup tables and inherit that filter, and
    ///               the C_PeriodControl EXISTS check is a child-table predicate.
    ///               ORDER BY and the paging suffix are appended AFTER AddAccessSQL
    ///               so the FROM-clause parser is not confused by a trailing clause.
    ///               Compatible with PostgreSQL and Oracle.
    /// Chronological development:
    ///   VAI154      2026-08-20 Created
    /// </summary>
    public class VAS_199_PaymentAllocationStatusModel
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_199_PaymentAllocationStatusModel).FullName);

        /* Category tokens exchanged with the client. The client maps each to a
           localized AD_Message label, so no display text is produced here. */
        public const string CATEGORY_SETTLEMENT = "SETTLE";
        public const string CATEGORY_ADVANCE = "ADVANCE";
        public const string CATEGORY_ALLOCATED = "ALLOC";

        /* Error tokens; the client resolves the label. */
        public const string ERROR_INVALID_REQUEST = "INVALID";
        public const string ERROR_NO_PERIOD = "NOPERIOD";

        /* C_PeriodControl.PeriodStatus stored code for an open control row. */
        private const string PERIODSTATUS_Open = "O";

        /* The standard Allocation form the card's action button opens. Resolved from
           AD_Form.ClassName at runtime - the numeric AD_Form_ID differs per
           environment - and memoised per app domain. 0 = not looked up yet,
           -1 = looked up and not present, so a missing form is not re-queried. */
        private const string ALLOCATION_FORM_CLASSNAME = "VAdvantage.Apps.AForms.VAllocation";
        private static int _allocationFormId = 0;

        /* Detail paging guard rails. The client asks for a page size; anything
           outside this band is clamped so a crafted request cannot pull the whole
           payment table into one response. */
        private const int PAGESIZE_MIN = 1;
        private const int PAGESIZE_MAX = 100;
        private const int PAGESIZE_DEFAULT = 8;

        // ─────────────────────────────────────────────────────────────────────
        // §1  Period list and bootstrap
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Bootstraps the widget in one round trip: every selectable open period,
        /// the period to preselect, and the three counts of that period.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <returns>Populated <see cref="StatusBootstrap"/> (never null).</returns>
        public StatusBootstrap GetBootstrap(Ctx ctx)
        {
            StatusBootstrap result = new StatusBootstrap();
            result.Periods = new List<PeriodItem>();
            result.Counts = new CategoryCounts();

            if (ctx == null) { return result; }

            result.Periods = GetOpenPeriods(ctx);
            if (result.Periods.Count == 0) { return result; }

            PeriodItem selected = PickDefaultPeriod(result.Periods, DateTime.Now.Date);
            result.C_Period_ID = selected.C_Period_ID;
            result.PeriodName = selected.Name;
            result.Counts = GetCounts(ctx, selected.C_Period_ID);

            return result;
        }

        /// <summary>
        /// Every active period the role may read that has at least one active
        /// C_PeriodControl row in status Open. A period appears once however many
        /// document base types are open for it, and the list is chronological.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <returns>Open periods in StartDate order (never null).</returns>
        public List<PeriodItem> GetOpenPeriods(Ctx ctx)
        {
            List<PeriodItem> items = new List<PeriodItem>();
            if (ctx == null) { return items; }

            /* One row per period: the open-control test is an EXISTS predicate, not
               a join, so several open base types cannot multiply the period out. */
            string sql = @"
                SELECT p.C_Period_ID AS C_Period_ID,
                       p.Name AS Period_Name,
                       p.StartDate AS Start_Date,
                       p.EndDate AS End_Date,
                       y.C_Year_ID AS C_Year_ID,
                       y.FiscalYear AS Fiscal_Year,
                       cal.C_Calendar_ID AS C_Calendar_ID,
                       cal.Name AS Calendar_Name
                FROM C_Period p
                INNER JOIN C_Year y ON (y.C_Year_ID=p.C_Year_ID)
                INNER JOIN C_Calendar cal ON (cal.C_Calendar_ID=y.C_Calendar_ID)
                INNER JOIN AD_ClientInfo cinfo ON (cinfo.C_Calendar_ID = cal.C_Calendar_ID)
                WHERE p.IsActive='Y'
                  AND y.IsActive='Y'
                  AND cal.IsActive='Y'
                  AND EXISTS(SELECT 1 FROM C_PeriodControl pc WHERE pc.C_Period_ID=p.C_Period_ID AND pc.IsActive='Y' AND pc.PeriodStatus=@PeriodStatus)";

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "p", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            sql += " ORDER BY p.StartDate DESC,p.EndDate DESC,p.C_Period_ID DESC";

            SqlParameter[] parameters = new SqlParameter[]
            {
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
                item.CalendarName = Util.GetValueOfString(dt.Rows[i]["Calendar_Name"]);
                items.Add(item);
            }

            return items;
        }

        /// <summary>
        /// Chooses which open period the widget opens on: the one containing today,
        /// otherwise the most recent one that has already started, otherwise the
        /// first of the list. Never assumes the current calendar month.
        /// </summary>
        /// <param name="periods">Open periods in StartDate order.</param>
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
                if (from <= today) { started = item; }        // list is ordered, so the last hit wins
            }

            return started != null ? started : periods[0];
        }

        /// <summary>
        /// Re-reads one period and confirms it is still active, accessible and open.
        /// The client only ever sends the id; the date range the queries run against
        /// always comes from here, never from the browser.
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
                INNER JOIN C_Calendar cal ON (cal.C_Calendar_ID=y.C_Calendar_ID)
                WHERE p.C_Period_ID=@C_Period_ID
                  AND p.IsActive='Y'
                  AND y.IsActive='Y'
                  AND cal.IsActive='Y'
                  AND EXISTS(SELECT 1 FROM C_PeriodControl pc WHERE pc.C_Period_ID=p.C_Period_ID AND pc.IsActive='Y' AND pc.PeriodStatus=@PeriodStatus)";

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "p", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            SqlParameter[] parameters = new SqlParameter[]
            {
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
        // §2  Category counts
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The three category counts of one period plus the two headline figures of
        /// what is still unallocated - the age of the oldest one and their total
        /// value in the tenant's base currency - from a single aggregated pass over
        /// C_Payment. One query, not one per row and not one per figure.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="periodId">C_Period_ID selected by the user.</param>
        /// <returns>Populated <see cref="CategoryCounts"/> (never null; zeros when
        /// the period is no longer open or holds no payments).</returns>
        public CategoryCounts GetCounts(Ctx ctx, int periodId)
        {
            CategoryCounts result = new CategoryCounts();
            result.C_Period_ID = periodId;
            result.OldestUnallocatedDays = -1;      // -1 = nothing unallocated

            if (ctx == null) { return result; }

            PeriodItem period = GetOpenPeriod(ctx, periodId);
            if (period == null)
            {
                result.ErrorCode = ERROR_NO_PERIOD;
                return result;
            }

            result.PeriodName = period.Name;
            result.AllocationFormId = GetAllocationFormId();

            /* The unallocated total is reported in ONE currency, so payments in
               foreign currencies are converted through the standard DB function to
               the tenant's primary accounting-schema currency - never summed raw
               and never against a hard-coded currency. */
            BaseCurrency baseCurrency = GetBaseCurrency(ctx);
            result.BaseCurrencyIso = baseCurrency.Iso;
            result.BaseCurrencySymbol = baseCurrency.Symbol;
            result.BaseCurrencyPrecision = baseCurrency.Precision;

            string amountExpr = baseCurrency.C_Currency_ID > 0
                ? @"SUM(CASE WHEN COALESCE(p.IsAllocated,'N')<>'Y' THEN COALESCE(currencyConvert(p.PayAmt,p.C_Currency_ID,@AcctCurrencyId,p.DateAcct,p.C_ConversionType_ID,p.AD_Client_ID,p.AD_Org_ID),0) ELSE 0 END)"
                : "0";      // no accounting schema resolved - report nothing rather than a raw mixed-currency sum

            /* Flat SUM(CASE ...) aggregation rather than nested counting subqueries:
               nested selects can exhaust the AddAccessSQL parser, and one pass is
               cheaper than several. The oldest date is returned as a DATE and the
               day count is worked out in C# - date arithmetic differs between the
               two backends and there is no reason to make the query carry it. */
            StringBuilder sql = new StringBuilder();
            sql.Append(@"
                SELECT SUM(CASE WHEN COALESCE(p.IsAllocated,'N')<>'Y' AND COALESCE(p.IsPrepayment,'N')<>'Y' AND COALESCE(ch.IsAdvanceCharge,'N')<>'Y' THEN 1 ELSE 0 END) AS Settlement_Count,
                       SUM(CASE WHEN COALESCE(p.IsAllocated,'N')<>'Y' AND (p.IsPrepayment='Y' OR COALESCE(ch.IsAdvanceCharge,'N')='Y') THEN 1 ELSE 0 END) AS Advance_Count,
                       SUM(CASE WHEN p.IsAllocated='Y' THEN 1 ELSE 0 END) AS Allocated_Count,
                       MIN(CASE WHEN COALESCE(p.IsAllocated,'N')<>'Y' THEN p.DateAcct ELSE NULL END) AS Oldest_Unallocated_Date,
                       ");
            sql.Append(amountExpr).Append(@" AS Unallocated_Amount
                FROM C_Payment p
                LEFT OUTER JOIN C_Charge ch ON (ch.C_Charge_ID=p.C_Charge_ID)
                WHERE ");
            sql.Append(CommonPaymentWhere());

            string accessSql = MRole.GetDefault(ctx).AddAccessSQL(sql.ToString(), "p",
                MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            /* Bind values in the order they APPEAR: the conversion sits in the SELECT
               list, ahead of the period bounds in the WHERE. */
            List<SqlParameter> parameters = new List<SqlParameter>();
            if (baseCurrency.C_Currency_ID > 0)
            {
                parameters.Add(new SqlParameter("@AcctCurrencyId", baseCurrency.C_Currency_ID));
            }
            parameters.AddRange(PeriodBoundParameters(period));

            DataSet ds = DB.ExecuteDataset(accessSql, parameters.ToArray(), null);
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) { return result; }

            DataRow row = ds.Tables[0].Rows[0];
            result.SettlementCount = Util.GetValueOfInt(row["Settlement_Count"]);
            result.AdvanceCount = Util.GetValueOfInt(row["Advance_Count"]);
            result.AllocatedCount = Util.GetValueOfInt(row["Allocated_Count"]);
            result.UnallocatedAmount = Util.GetValueOfDecimal(row["Unallocated_Amount"]);

            /* Age of the oldest unallocated payment, in whole days from its
               accounting date to today. A payment accounted in the future counts as
               0 days rather than a negative age. */
            DateTime? oldest = Util.GetValueOfDateTime(row["Oldest_Unallocated_Date"]);
            if (oldest.HasValue)
            {
                double days = (DateTime.Now.Date - oldest.Value.Date).TotalDays;
                result.OldestUnallocatedDays = days > 0 ? (int)Math.Floor(days) : 0;
                result.OldestUnallocatedDate = oldest;
            }

            return result;
        }

        /// <summary>
        /// AD_Form_ID of the standard Allocation form, resolved from its ClassName so
        /// no numeric id is ever hard-coded. Dictionary metadata does not change at
        /// runtime, so the answer is memoised per app domain.
        /// </summary>
        /// <returns>AD_Form_ID, or 0 when the form is not present in this environment
        /// (the client then hides the action button rather than offering a dead one).</returns>
        private int GetAllocationFormId()
        {
            if (_allocationFormId > 0) { return _allocationFormId; }
            if (_allocationFormId < 0) { return 0; }        // looked up, not present

            string sql = @"
                SELECT Form.AD_Form_ID AS Form_ID
                FROM AD_Form Form
                WHERE Form.ClassName=@ClassName
                  AND Form.IsActive='Y'";

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@ClassName", ALLOCATION_FORM_CLASSNAME)
            };

            int formId = Util.GetValueOfInt(DB.ExecuteScalar(sql, parameters, null));
            _allocationFormId = formId > 0 ? formId : -1;

            return formId;
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
        // §3  Category detail (server-side paging)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// One page of the payments behind a category, plus the total row count so
        /// the client can page without holding the whole set. Detail rows are only
        /// ever read through this method - the widget itself never loads them.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="periodId">C_Period_ID selected by the user.</param>
        /// <param name="category">One of the CATEGORY_* tokens.</param>
        /// <param name="pageNo">1-based page number.</param>
        /// <param name="pageSize">Rows per page (clamped server-side).</param>
        /// <returns>Populated <see cref="PaymentPage"/> (never null).</returns>
        public PaymentPage GetPayments(Ctx ctx, int periodId, string category, int pageNo, int pageSize)
        {
            PaymentPage result = new PaymentPage();
            result.Rows = new List<PaymentRow>();
            result.Category = category;
            result.C_Period_ID = periodId;

            if (ctx == null) { return result; }

            string categoryWhere = CategoryWhere(category);
            if (categoryWhere == null)
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

            /* COUNT first: an out-of-range page (the set shrank since the user last
               paged) is clamped before the row query runs, so the modal can never
               land on an empty page while records still exist. */
            result.Total = CountPayments(ctx, period, categoryWhere);
            if (result.Total == 0)
            {
                result.PageNo = 1;
                return result;
            }

            int totalPages = (result.Total + pageSize - 1) / pageSize;
            if (pageNo > totalPages) { pageNo = totalPages; }
            result.PageNo = pageNo;

            result.Rows = ReadPayments(ctx, period, categoryWhere, pageNo, pageSize);

            return result;
        }

        /// <summary>
        /// Total number of payments in one category of one period.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="period">Validated period carrying the date bounds.</param>
        /// <param name="categoryWhere">Category predicate from <see cref="CategoryWhere"/>.</param>
        /// <returns>Row count, 0 when nothing matches.</returns>
        private int CountPayments(Ctx ctx, PeriodItem period, string categoryWhere)
        {
            StringBuilder sql = new StringBuilder();
            sql.Append(@"
                SELECT COUNT(1) AS Record_Count
                FROM C_Payment p
                LEFT OUTER JOIN C_Charge ch ON (ch.C_Charge_ID=p.C_Charge_ID)
                WHERE ");
            sql.Append(CommonPaymentWhere());
            sql.Append(" AND ").Append(categoryWhere);

            string accessSql = MRole.GetDefault(ctx).AddAccessSQL(sql.ToString(), "p",
                MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            DataSet ds = DB.ExecuteDataset(accessSql, PeriodBoundParameters(period), null);
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) { return 0; }

            return Util.GetValueOfInt(ds.Tables[0].Rows[0]["Record_Count"]);
        }

        /// <summary>
        /// One page of payment rows, newest accounting date first.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="period">Validated period carrying the date bounds.</param>
        /// <param name="categoryWhere">Category predicate from <see cref="CategoryWhere"/>.</param>
        /// <param name="pageNo">1-based page number (already clamped).</param>
        /// <param name="pageSize">Rows per page (already clamped).</param>
        /// <returns>Materialised rows (never null).</returns>
        private List<PaymentRow> ReadPayments(Ctx ctx, PeriodItem period, string categoryWhere,
            int pageNo, int pageSize)
        {
            List<PaymentRow> rows = new List<PaymentRow>();

            StringBuilder sql = new StringBuilder();
            sql.Append(@"
                SELECT p.C_Payment_ID AS C_Payment_ID,
                       p.DocumentNo AS Document_No,
                       p.DateTrx AS Date_Trx,
                       p.DateAcct AS Date_Acct,
                       p.IsReceipt AS Is_Receipt,
                       p.C_DocType_ID AS C_DocType_ID,
                       COALESCE(dt.Name,N'') AS Doc_Type_Name,
                       p.C_BPartner_ID AS C_BPartner_ID,
                       COALESCE(bp.Name,N'') AS Business_Partner_Name,
                       p.C_Charge_ID AS C_Charge_ID,
                       COALESCE(ch.Name,N'') AS Charge_Name,
                       p.C_Currency_ID AS C_Currency_ID,
                       cur.ISO_Code AS Currency_Iso,
                       COALESCE(cur.CurSymbol,cur.ISO_Code) AS Currency_Symbol,
                       cur.StdPrecision AS Currency_Precision,
                       COALESCE(p.PayAmt,0) AS Pay_Amt
                FROM C_Payment p
                INNER JOIN C_Currency cur ON (cur.C_Currency_ID=p.C_Currency_ID)
                LEFT OUTER JOIN C_DocType dt ON (dt.C_DocType_ID=p.C_DocType_ID)
                LEFT OUTER JOIN C_Charge ch ON (ch.C_Charge_ID=p.C_Charge_ID)
                LEFT OUTER JOIN C_BPartner bp ON (bp.C_BPartner_ID=p.C_BPartner_ID)
                WHERE ");
            sql.Append(CommonPaymentWhere());
            sql.Append(" AND ").Append(categoryWhere);

            string accessSql = MRole.GetDefault(ctx).AddAccessSQL(sql.ToString(), "p",
                MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            /* ORDER BY and the paging suffix go on AFTER the access SQL. */
            accessSql += " ORDER BY p.DateAcct DESC,p.DocumentNo DESC,p.C_Payment_ID DESC";
            accessSql += PagingSuffix(pageSize, (pageNo - 1) * pageSize);

            DataSet ds = DB.ExecuteDataset(accessSql, PeriodBoundParameters(period), null);
            if (ds == null || ds.Tables.Count == 0) { return rows; }

            DataTable dt = ds.Tables[0];
            for (int i = 0; i < dt.Rows.Count; i++)
            {
                DataRow dr = dt.Rows[i];

                PaymentRow row = new PaymentRow();
                row.C_Payment_ID = Util.GetValueOfInt(dr["C_Payment_ID"]);
                row.DocumentNo = Util.GetValueOfString(dr["Document_No"]);
                row.DateTrx = Util.GetValueOfDateTime(dr["Date_Trx"]);
                row.DateAcct = Util.GetValueOfDateTime(dr["Date_Acct"]);
                row.IsReceipt = "Y".Equals(Util.GetValueOfString(dr["Is_Receipt"]));
                row.C_DocType_ID = Util.GetValueOfInt(dr["C_DocType_ID"]);
                row.DocTypeName = Util.GetValueOfString(dr["Doc_Type_Name"]);
                row.C_BPartner_ID = Util.GetValueOfInt(dr["C_BPartner_ID"]);
                row.BusinessPartnerName = Util.GetValueOfString(dr["Business_Partner_Name"]);
                row.C_Charge_ID = Util.GetValueOfInt(dr["C_Charge_ID"]);
                row.ChargeName = Util.GetValueOfString(dr["Charge_Name"]);
                row.C_Currency_ID = Util.GetValueOfInt(dr["C_Currency_ID"]);
                row.CurrencyIso = Util.GetValueOfString(dr["Currency_Iso"]);
                row.CurrencySymbol = Util.GetValueOfString(dr["Currency_Symbol"]);
                row.CurrencyPrecision = Util.GetValueOfInt(dr["Currency_Precision"]);
                row.PayAmt = Util.GetValueOfDecimal(dr["Pay_Amt"]);

                rows.Add(row);
            }

            return rows;
        }

        // ─────────────────────────────────────────────────────────────────────
        // §4  Query building blocks
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The predicate every category shares: active, completed or closed, and
        /// accounted inside the selected period. DateAcct - not DateTrx - because
        /// this is a period-control dashboard, and the bounds are the period's own
        /// dates rather than any month or year function.
        /// </summary>
        /// <returns>WHERE fragment binding @StartDate and @EndDate.</returns>
        private string CommonPaymentWhere()
        {
            /* Compare date values only. Oracle DATE always carries a time part
               (TRUNC drops it); PostgreSQL needs an explicit CAST when the column is
               materialised as a timestamp. Only the COLUMN side is normalised - the
               bind values are already midnight, and casting a bind variable leaves
               its type undetermined on PostgreSQL. */
            string dateCondition = DB.IsOracle()
                ? "TRUNC(p.DateAcct)>=@StartDate AND TRUNC(p.DateAcct)<=@EndDate"
                : "CAST(p.DateAcct AS DATE)>=@StartDate AND CAST(p.DateAcct AS DATE)<=@EndDate";

            return "p.IsActive='Y' AND p.DocStatus IN ('CO','CL') AND " + dateCondition;
        }

        /// <summary>
        /// The predicate that isolates one category. The three are mutually
        /// exclusive by construction: allocated wins over advance, advance wins over
        /// settlement, so an unallocated prepayment can never also be counted as a
        /// settlement payment.
        /// </summary>
        /// <param name="category">One of the CATEGORY_* tokens.</param>
        /// <returns>WHERE fragment, or null when the token is not recognised.</returns>
        private string CategoryWhere(string category)
        {
            if (CATEGORY_SETTLEMENT.Equals(category))
            {
                return "COALESCE(p.IsAllocated,'N')<>'Y' AND COALESCE(p.IsPrepayment,'N')<>'Y' AND COALESCE(ch.IsAdvanceCharge,'N')<>'Y'";
            }

            if (CATEGORY_ADVANCE.Equals(category))
            {
                return "COALESCE(p.IsAllocated,'N')<>'Y' AND (p.IsPrepayment='Y' OR COALESCE(ch.IsAdvanceCharge,'N')='Y')";
            }

            if (CATEGORY_ALLOCATED.Equals(category))
            {
                return "p.IsAllocated='Y'";
            }

            return null;
        }

        /// <summary>
        /// The period bounds every payment query binds. Date parts only, so a
        /// payment stamped with a time still falls inside its period.
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

        // ─────────────────────────────────────────────────────────────────────
        // §5  Contracts
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>One selectable open accounting period.</summary>
        public class PeriodItem
        {
            public int C_Period_ID { get; set; }
            public string Name { get; set; }

            /// <summary>Inclusive lower bound applied to C_Payment.DateAcct.</summary>
            public DateTime? StartDate { get; set; }

            /// <summary>Inclusive upper bound applied to C_Payment.DateAcct.</summary>
            public DateTime? EndDate { get; set; }

            public int C_Year_ID { get; set; }
            public string FiscalYear { get; set; }
            public int C_Calendar_ID { get; set; }
            public string CalendarName { get; set; }
        }

        /// <summary>The three category counts of one period, plus the unallocated
        /// headline figures shown under them.</summary>
        public class CategoryCounts
        {
            public int C_Period_ID { get; set; }
            public string PeriodName { get; set; }

            public int SettlementCount { get; set; }
            public int AdvanceCount { get; set; }
            public int AllocatedCount { get; set; }

            /// <summary>Whole days from the oldest unallocated payment's accounting
            /// date to today; -1 when nothing is unallocated in this period.</summary>
            public int OldestUnallocatedDays { get; set; }

            /// <summary>Accounting date behind OldestUnallocatedDays, for the tooltip.</summary>
            public DateTime? OldestUnallocatedDate { get; set; }

            /// <summary>Total of every unallocated payment, converted to the base
            /// currency below.</summary>
            public decimal UnallocatedAmount { get; set; }

            public string BaseCurrencyIso { get; set; }
            public string BaseCurrencySymbol { get; set; }
            public int BaseCurrencyPrecision { get; set; }

            /// <summary>AD_Form_ID of the standard Allocation form; 0 hides the
            /// action button rather than offering a dead one.</summary>
            public int AllocationFormId { get; set; }

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

        /// <summary>Period list, default selection and its counts, in one payload.</summary>
        public class StatusBootstrap
        {
            public List<PeriodItem> Periods { get; set; }

            public int C_Period_ID { get; set; }
            public string PeriodName { get; set; }

            public CategoryCounts Counts { get; set; }
        }

        /// <summary>One payment behind a category.</summary>
        public class PaymentRow
        {
            public int C_Payment_ID { get; set; }
            public string DocumentNo { get; set; }
            public DateTime? DateTrx { get; set; }
            public DateTime? DateAcct { get; set; }

            /// <summary>Decides which standard window the client zooms to: a receipt
            /// and a vendor payment are different screens.</summary>
            public bool IsReceipt { get; set; }

            public int C_DocType_ID { get; set; }

            /// <summary>C_DocType.Name - the document type shown in the Type column.</summary>
            public string DocTypeName { get; set; }

            public int C_BPartner_ID { get; set; }
            public string BusinessPartnerName { get; set; }

            public int C_Charge_ID { get; set; }
            public string ChargeName { get; set; }

            public int C_Currency_ID { get; set; }
            public string CurrencyIso { get; set; }
            public string CurrencySymbol { get; set; }
            public int CurrencyPrecision { get; set; }

            /// <summary>C_Payment.PayAmt - the displayed payment amount.</summary>
            public decimal PayAmt { get; set; }
        }

        /// <summary>One page of category detail plus the paging state.</summary>
        public class PaymentPage
        {
            public string Category { get; set; }
            public int C_Period_ID { get; set; }
            public string PeriodName { get; set; }

            public List<PaymentRow> Rows { get; set; }

            public int Total { get; set; }
            public int PageNo { get; set; }
            public int PageSize { get; set; }

            /// <summary>One of the ERROR_* tokens; the client resolves the label.</summary>
            public string ErrorCode { get; set; }
        }
    }
}
