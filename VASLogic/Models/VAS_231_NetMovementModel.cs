/******************************************************
 * Module Name    : VASLogic
 * Purpose        : Net Movement dashboard widget data
 * chronological  : Development
 * Created Date   : 2026-09-02
 * Created by     : VAI145
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
    /// Module Name : VAS_231_NetMovement
    /// Purpose     : Backs the VAS_231_NetMovementWidget dashboard widget - money IN
    ///               minus money OUT for ONE accounting period:
    ///
    ///                 Receipts   SUM(PayAmt) WHERE IsReceipt='Y'
    ///                 Payments   SUM(PayAmt) WHERE IsReceipt='N'
    ///                 Net        Receipts - Payments
    ///
    ///               Period list: EVERY active period of the CURRENT fiscal year on the
    ///               tenant's primary calendar (AD_ClientInfo.C_Calendar_ID), newest
    ///               first, with periods that have not started yet left out - a period
    ///               whose StartDate is in the future can only ever read zero, so
    ///               offering it would be offering an empty answer. The current fiscal
    ///               year is the year of the period containing today, falling back to the
    ///               year of the most recently started period; it is NOT the calendar
    ///               year, because a fiscal year need not start in January.
    ///
    ///               The widget shows the periods the same way VAS_197 does - one chip
    ///               naming the selected period, a popover listing the rest - but its
    ///               list is the current year rather than every open period, and it does
    ///               not test C_PeriodControl: an already-closed month still has a net
    ///               movement worth reading.
    ///
    ///               Amounts: C_Payment.PayAmt is stated in the PAYMENT's own currency,
    ///               so every SUM goes through the currencyConvert(...) DB function into
    ///               the tenant's primary accounting-schema currency
    ///               (AD_ClientInfo.C_AcctSchema1_ID -> C_AcctSchema.C_Currency_ID),
    ///               dated on the payment's own DateAcct. The client formats the result
    ///               with VIS.Util.formatCompactAmount against the ISO code and precision
    ///               this model returns - no currency is ever hard-coded.
    ///
    ///               Only settled money counts: DocStatus IN ('CO','CL'). A drafted or
    ///               voided payment has moved nothing.
    ///
    ///               MRole row-level security is applied to the main physical table of
    ///               every user-facing query - C_Period alias p for the period reads,
    ///               C_Payment alias p for the figures - and never to a joined reference
    ///               table (C_Year, AD_ClientInfo) or to a combined statement. ORDER BY
    ///               is appended AFTER AddAccessSQL so its FROM-clause parser never meets
    ///               a trailing clause. Compatible with PostgreSQL and Oracle.
    /// Chronological development:
    ///   VAI154      2026-09-02 Created
    /// </summary>
    public class VAS_231_NetMovementModel
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_231_NetMovementModel).FullName);

        /* C_Payment.DocStatus codes that mean the money actually moved. Stored codes -
           compared bare, never with an N prefix. */
        private const string DOCSTATUS_Completed = "CO";
        private const string DOCSTATUS_Closed = "CL";

        /* C_Payment.IsReceipt stored codes. */
        private const string ISRECEIPT_Yes = "Y";
        private const string ISRECEIPT_No = "N";

        // ─────────────────────────────────────────────────────────────────────
        // §1  Bootstrap
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Bootstraps the widget in one round trip: the base currency, the current
        /// fiscal year's periods, the period to preselect and that period's figures.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <returns>Populated <see cref="NetMovementBootstrap"/> (never null). Loaded is
        /// false only when there is no context or no usable period - a tenant with no
        /// payments in the period returns Loaded=true and zeros, because zero movement
        /// is a real answer rather than an error.</returns>
        public NetMovementBootstrap GetBootstrap(Ctx ctx)
        {
            NetMovementBootstrap result = new NetMovementBootstrap();
            result.Periods = new List<PeriodItem>();
            result.Currency = new BaseCurrency();
            result.Data = new NetMovementData();

            if (ctx == null) { return result; }

            result.Currency = GetBaseCurrency(ctx);
            result.Periods = GetCurrentYearPeriods(ctx, DateTime.Now.Date);
            if (result.Periods.Count == 0) { return result; }

            /* The list is newest-first and carries no period that has not started, so
               the period containing today is the first entry whose EndDate has not yet
               passed; failing that the newest one is the closest thing to "now". */
            PeriodItem selected = PickDefaultPeriod(result.Periods, DateTime.Now.Date);

            result.C_Period_ID = selected.C_Period_ID;
            result.PeriodName = selected.Name;
            result.Data = GetPeriodData(ctx, selected.C_Period_ID);

            return result;
        }

        // ─────────────────────────────────────────────────────────────────────
        // §2  Period list
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Every active period of the CURRENT fiscal year on the tenant's primary
        /// calendar that has already started, newest first.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="today">Current application date (date part only).</param>
        /// <returns>Periods, newest StartDate first (never null).</returns>
        public List<PeriodItem> GetCurrentYearPeriods(Ctx ctx, DateTime today)
        {
            List<PeriodItem> items = new List<PeriodItem>();
            if (ctx == null) { return items; }

            int yearId = GetCurrentYearId(ctx, today);
            if (yearId <= 0) { return items; }

            /* AD_ClientInfo is not joined again here - the year was already resolved
               against the tenant's primary calendar, so filtering on it is enough. */
            string sql = @"
                SELECT p.C_Period_ID AS C_Period_ID,
                       p.Name AS Period_Name,
                       p.StartDate AS Start_Date,
                       p.EndDate AS End_Date,
                       p.PeriodNo AS Period_No,
                       y.C_Year_ID AS C_Year_ID,
                       y.FiscalYear AS Fiscal_Year
                FROM C_Period p
                INNER JOIN C_Year y ON (y.C_Year_ID=p.C_Year_ID)
                WHERE p.IsActive='Y'
                  AND y.IsActive='Y'
                  AND p.AD_Client_ID=@AD_Client_ID
                  AND p.C_Year_ID=@C_Year_ID
                  AND p.StartDate<=@Today";

            /* C_Period p is the main physical table: the role's organisation access is
               applied HERE, never to the joined C_Year reference row. */
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "p", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            /* ORDER BY goes on AFTER the access SQL - its FROM-clause parser must not
               meet a trailing clause. Newest period first, as asked. */
            sql += " ORDER BY p.StartDate DESC,p.EndDate DESC,p.C_Period_ID DESC";

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@C_Year_ID", yearId),
                new SqlParameter("@Today", today)
            };

            DataSet ds = DB.ExecuteDataset(sql, parameters, null);
            if (ds == null || ds.Tables.Count == 0) { return items; }

            DataTable dt = ds.Tables[0];
            for (int i = 0; i < dt.Rows.Count; i++)
            {
                items.Add(MapPeriod(dt.Rows[i]));
            }

            return items;
        }

        /// <summary>
        /// The CURRENT fiscal year of the tenant's primary calendar: the year of the
        /// period that contains today, or - when today falls in a gap or past the last
        /// defined period - the year of the most recently started period.
        ///
        /// Read from C_Period rather than from C_Year.FiscalYear, because a fiscal year
        /// is not the calendar year and its name is free text.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="today">Current application date (date part only).</param>
        /// <returns>C_Year_ID, or 0 when the tenant has no calendar or no started period.</returns>
        private int GetCurrentYearId(Ctx ctx, DateTime today)
        {
            /* Every join ON here is a plain equality: no function call, no nested
               parenthesis. AccessSqlParser strips the LAST ON at the first ')' it finds,
               so a COALESCE / CAST in the closing join would break the access SQL. */
            string sql = @"
                SELECT p.C_Year_ID AS C_Year_ID
                FROM C_Period p
                INNER JOIN C_Year y ON (y.C_Year_ID=p.C_Year_ID)
                INNER JOIN AD_ClientInfo ci ON (ci.C_Calendar_ID=y.C_Calendar_ID AND ci.AD_Client_ID=@AD_Client_ID)
                WHERE p.IsActive='Y'
                  AND y.IsActive='Y'
                  AND p.AD_Client_ID=@AD_Client_ID_Period
                  AND p.StartDate<=@Today";

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "p", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            /* Newest started period first - its year IS the current fiscal year. */
            sql += " ORDER BY p.StartDate DESC,p.C_Period_ID DESC";

            /* The provider binds POSITIONALLY, so the client id appears under two
               distinct names, added in the order their placeholders appear in the text. */
            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@AD_Client_ID_Period", ctx.GetAD_Client_ID()),
                new SqlParameter("@Today", today)
            };

            DataSet ds = DB.ExecuteDataset(sql, parameters, null);
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
            {
                Log.Log(Level.WARNING, "VAS_231_NetMovement: no started period on the primary calendar for AD_Client_ID="
                    + ctx.GetAD_Client_ID());
                return 0;
            }

            return Util.GetValueOfInt(ds.Tables[0].Rows[0]["C_Year_ID"]);
        }

        /// <summary>
        /// Chooses which period the widget opens on: the one containing today, else the
        /// most recent one. The list is newest-first and already excludes periods that
        /// have not started, so the first match wins.
        /// </summary>
        /// <param name="periods">Current-year periods, newest StartDate first.</param>
        /// <param name="today">Current application date (date part only).</param>
        /// <returns>The period to preselect (never null when the list is filled).</returns>
        private PeriodItem PickDefaultPeriod(List<PeriodItem> periods, DateTime today)
        {
            for (int i = 0; i < periods.Count; i++)
            {
                PeriodItem item = periods[i];
                if (!item.StartDate.HasValue || !item.EndDate.HasValue) { continue; }

                if (item.StartDate.Value.Date <= today && item.EndDate.Value.Date >= today)
                {
                    return item;
                }
            }

            return periods[0];
        }

        /// <summary>
        /// Re-reads one period and confirms it is still active, accessible, started and
        /// on the tenant's primary calendar. The client only ever sends the id; the date
        /// range the figures are read for always comes from here.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="periodId">C_Period_ID the client selected.</param>
        /// <returns>Populated <see cref="PeriodItem"/>, or null when it no longer qualifies.</returns>
        private PeriodItem GetPeriod(Ctx ctx, int periodId)
        {
            if (ctx == null || periodId <= 0) { return null; }

            string sql = @"
                SELECT p.C_Period_ID AS C_Period_ID,
                       p.Name AS Period_Name,
                       p.StartDate AS Start_Date,
                       p.EndDate AS End_Date,
                       p.PeriodNo AS Period_No,
                       y.C_Year_ID AS C_Year_ID,
                       y.FiscalYear AS Fiscal_Year
                FROM C_Period p
                INNER JOIN C_Year y ON (y.C_Year_ID=p.C_Year_ID)
                INNER JOIN AD_ClientInfo ci ON (ci.C_Calendar_ID=y.C_Calendar_ID AND ci.AD_Client_ID=@AD_Client_ID)
                WHERE p.IsActive='Y'
                  AND y.IsActive='Y'
                  AND p.AD_Client_ID=@AD_Client_ID_Period
                  AND p.C_Period_ID=@C_Period_ID
                  AND p.StartDate<=@Today";

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "p", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@AD_Client_ID_Period", ctx.GetAD_Client_ID()),
                new SqlParameter("@C_Period_ID", periodId),
                new SqlParameter("@Today", DateTime.Now.Date)
            };

            DataSet ds = DB.ExecuteDataset(sql, parameters, null);
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) { return null; }

            return MapPeriod(ds.Tables[0].Rows[0]);
        }

        /// <summary>Materialises one period row.</summary>
        /// <param name="row">Row carrying the period aliases.</param>
        /// <returns>Populated <see cref="PeriodItem"/>.</returns>
        private PeriodItem MapPeriod(DataRow row)
        {
            PeriodItem item = new PeriodItem();
            item.C_Period_ID = Util.GetValueOfInt(row["C_Period_ID"]);
            item.Name = Util.GetValueOfString(row["Period_Name"]);
            item.StartDate = Util.GetValueOfDateTime(row["Start_Date"]);
            item.EndDate = Util.GetValueOfDateTime(row["End_Date"]);
            item.PeriodNo = Util.GetValueOfInt(row["Period_No"]);
            item.C_Year_ID = Util.GetValueOfInt(row["C_Year_ID"]);
            item.FiscalYear = Util.GetValueOfString(row["Fiscal_Year"]);
            return item;
        }

        // ─────────────────────────────────────────────────────────────────────
        // §3  The figures
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Receipts, payments and their net for ONE period, in the tenant's base
        /// currency.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="periodId">C_Period_ID selected by the user.</param>
        /// <returns>Populated <see cref="NetMovementData"/> (never null). Loaded is false
        /// only when the period no longer qualifies or the tenant has no accounting
        /// schema; a period with no payments returns Loaded=true and zeros.</returns>
        public NetMovementData GetPeriodData(Ctx ctx, int periodId)
        {
            NetMovementData result = new NetMovementData();
            result.C_Period_ID = periodId;

            if (ctx == null) { return result; }

            PeriodItem period = GetPeriod(ctx, periodId);
            if (period == null || !period.StartDate.HasValue || !period.EndDate.HasValue)
            {
                return result;
            }

            result.C_Period_ID = period.C_Period_ID;
            result.PeriodName = period.Name;

            BaseCurrency currency = GetBaseCurrency(ctx);
            if (currency.C_Currency_ID <= 0)
            {
                Log.Log(Level.WARNING, "VAS_231_NetMovement: AD_ClientInfo.C_AcctSchema1_ID not configured for AD_Client_ID="
                    + ctx.GetAD_Client_ID());
                return result;
            }

            ReadMovement(ctx, period, currency.C_Currency_ID, result);
            result.NetMovement = result.ReceiptsAmt - result.PaymentsAmt;
            result.Loaded = true;

            return result;
        }

        /// <summary>
        /// Fills the receipt / payment totals and counts from C_Payment in ONE grouped
        /// pass.
        ///
        /// PayAmt is stated in the payment's OWN currency, so each side is converted
        /// with currencyConvert(...) into the accounting-schema currency, dated on the
        /// payment's DateAcct and using its own conversion type - the same call the
        /// standard reports use, so the widget cannot disagree with them.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="period">Validated period supplying the date bounds.</param>
        /// <param name="acctCurrencyId">Primary accounting-schema C_Currency_ID.</param>
        /// <param name="result">Row being filled.</param>
        private void ReadMovement(Ctx ctx, PeriodItem period, int acctCurrencyId, NetMovementData result)
        {
            /* The target currency is a server-resolved id, never client input, so it is
               inlined rather than bound: the conversion call appears TWICE and the
               provider binds by POSITION, which would need two separately named binds
               for one and the same value. */
            string convert = "currencyConvert(p.PayAmt,p.C_Currency_ID," + acctCurrencyId
                + ",p.DateAcct,p.C_ConversionType_ID,p.AD_Client_ID,p.AD_Org_ID)";

            StringBuilder sql = new StringBuilder();
            sql.Append(@"
                SELECT COALESCE(SUM(CASE WHEN p.IsReceipt='").Append(ISRECEIPT_Yes).Append("' THEN ").Append(convert).Append(@" ELSE 0 END),0) AS Receipts_Amt,
                       COALESCE(SUM(CASE WHEN p.IsReceipt='").Append(ISRECEIPT_No).Append("' THEN ").Append(convert).Append(@" ELSE 0 END),0) AS Payments_Amt,
                       COALESCE(SUM(CASE WHEN p.IsReceipt='").Append(ISRECEIPT_Yes).Append(@"' THEN 1 ELSE 0 END),0) AS Receipts_Cnt,
                       COALESCE(SUM(CASE WHEN p.IsReceipt='").Append(ISRECEIPT_No).Append(@"' THEN 1 ELSE 0 END),0) AS Payments_Cnt
                FROM C_Payment p
                WHERE p.IsActive='Y'
                  AND p.AD_Client_ID=@AD_Client_ID
                  AND p.DocStatus IN ('").Append(DOCSTATUS_Completed).Append("','").Append(DOCSTATUS_Closed).Append(@"')
                  AND p.DateAcct>=@DateFrom
                  AND p.DateAcct<@DateToExclusive");

            /* C_Payment p is the main physical table. MRole supplies the organisation
               access clause, so no AD_Org_ID predicate is written by hand above - the
               explicit tenant filter is a second, independent guard rather than the
               only one. Flat SUM(CASE ...) aggregation, never nested selects, so the
               access parser has one simple FROM clause to read. */
            string finalSql = MRole.GetDefault(ctx).AddAccessSQL(sql.ToString(), "p",
                MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            /* Half-open upper bound: a payment stamped on the last day of the period at
               14:30 is still inside the period. */
            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@DateFrom", period.StartDate.Value.Date),
                new SqlParameter("@DateToExclusive", period.EndDate.Value.Date.AddDays(1))
            };

            DataSet ds = DB.ExecuteDataset(finalSql, parameters, null);
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) { return; }

            DataRow row = ds.Tables[0].Rows[0];
            result.ReceiptsAmt = Util.GetValueOfDecimal(row["Receipts_Amt"]);
            result.PaymentsAmt = Util.GetValueOfDecimal(row["Payments_Amt"]);
            result.ReceiptsCount = Util.GetValueOfInt(row["Receipts_Cnt"]);
            result.PaymentsCount = Util.GetValueOfInt(row["Payments_Cnt"]);
        }

        // ─────────────────────────────────────────────────────────────────────
        // §4  Base currency
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The tenant's base currency: the currency of the primary accounting schema
        /// (AD_ClientInfo.C_AcctSchema1_ID). Reads only system / reference tables scoped
        /// to the session client, so no MRole predicate is applied - the same treatment
        /// the sibling KPI widgets give this lookup.
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
        // §5  Transfer objects
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>Everything the widget needs on first paint.</summary>
        public class NetMovementBootstrap
        {
            /// <summary>Current fiscal year's started periods, newest first.</summary>
            public List<PeriodItem> Periods { get; set; }

            /// <summary>C_Period_ID to preselect; 0 when there is none.</summary>
            public int C_Period_ID { get; set; }

            /// <summary>Display name of the preselected period.</summary>
            public string PeriodName { get; set; }

            /// <summary>Base currency the figures are stated in.</summary>
            public BaseCurrency Currency { get; set; }

            /// <summary>The preselected period's figures.</summary>
            public NetMovementData Data { get; set; }
        }

        /// <summary>One selectable accounting period.</summary>
        public class PeriodItem
        {
            /// <summary>C_Period.C_Period_ID.</summary>
            public int C_Period_ID { get; set; }

            /// <summary>C_Period.Name as the calendar defines it.</summary>
            public string Name { get; set; }

            /// <summary>C_Period.StartDate.</summary>
            public DateTime? StartDate { get; set; }

            /// <summary>C_Period.EndDate.</summary>
            public DateTime? EndDate { get; set; }

            /// <summary>C_Period.PeriodNo - the period's ordinal in its year.</summary>
            public int PeriodNo { get; set; }

            /// <summary>Owning C_Year.C_Year_ID.</summary>
            public int C_Year_ID { get; set; }

            /// <summary>C_Year.FiscalYear, shown as the popover's row meta.</summary>
            public string FiscalYear { get; set; }
        }

        /// <summary>The accounting-schema currency every figure is stated in.</summary>
        public class BaseCurrency
        {
            /// <summary>C_Currency_ID of the primary accounting schema; 0 when unset.</summary>
            public int C_Currency_ID { get; set; }

            /// <summary>ISO code - drives the client's compact scale (lakh/crore vs million).</summary>
            public string Iso { get; set; }

            /// <summary>Display symbol, falling back to the ISO code.</summary>
            public string Symbol { get; set; }

            /// <summary>C_Currency.StdPrecision.</summary>
            public int Precision { get; set; }
        }

        /// <summary>The card figures for one period.</summary>
        public class NetMovementData
        {
            /// <summary>Period the figures belong to.</summary>
            public int C_Period_ID { get; set; }

            /// <summary>Period display name.</summary>
            public string PeriodName { get; set; }

            /// <summary>Money in - converted SUM(PayAmt) WHERE IsReceipt='Y'.</summary>
            public decimal ReceiptsAmt { get; set; }

            /// <summary>Money out - converted SUM(PayAmt) WHERE IsReceipt='N'.</summary>
            public decimal PaymentsAmt { get; set; }

            /// <summary>Receipts minus payments; negative when more went out than came in.</summary>
            public decimal NetMovement { get; set; }

            /// <summary>Number of receipts behind ReceiptsAmt.</summary>
            public int ReceiptsCount { get; set; }

            /// <summary>Number of payments behind PaymentsAmt.</summary>
            public int PaymentsCount { get; set; }

            /// <summary>False only on a failure or a period that no longer qualifies -
            /// a period with no payments is Loaded=true with zeros.</summary>
            public bool Loaded { get; set; }
        }
    }
}
