/******************************************************
 * Module Name    : VASLogic
 * Purpose        : Receipts vs Payments Trend dashboard widget data
 * chronological  : Development
 * Created Date   : 2026-09-03
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
    /// Module Name : VAS_236_ReceiptvsPayment
    /// Purpose     : Backs the VAS_236_ReceiptvsPaymentWidget dashboard widget - money in
    ///               against money out over time, as one point per period:
    ///
    ///                 Receipts  SUM(ABS(PayAmt)) WHERE IsReceipt='Y'
    ///                 Payments  SUM(ABS(PayAmt)) WHERE IsReceipt='N'
    ///                 Net       Receipts - Payments
    ///
    ///               Direction comes from C_Payment.IsReceipt, never from the stored sign,
    ///               which is why both sides are summed through ABS(): a negative amount in
    ///               the data must not flip a receipt into a payment. Only settled money
    ///               counts - DocStatus IN ('CO','CL').
    ///
    ///               NO DATE FUNCTIONS IN SQL. Bucketing by day / week / month is the one
    ///               place the two backends diverge hardest - DATE_TRUNC('week', ...)
    ///               against TRUNC(d,'IW'), with a different week origin - and any of them
    ///               inside a GROUP BY also has to survive MRole's parser twice over. So
    ///               the query groups by the raw DateAcct, which is a plain date column on
    ///               both, and the DAY ROWS ARE ROLLED UP INTO BUCKETS HERE. One query
    ///               shape, no dialect variants, and the week origin is decided by code
    ///               rather than by whichever backend the tenant happens to run.
    ///
    ///               The row count that reaches C# is bounded by the range: at most one row
    ///               per distinct accounting date, so 90 rows for the widest range - far
    ///               cheaper to roll up in memory than to make the database do it twice in
    ///               two dialects.
    ///
    ///               EVERY BUCKET IS RETURNED, including the empty ones. A day nothing
    ///               happened on is a zero on the chart, not a gap in the axis - the series
    ///               has to stay aligned to the date scale or the line lies about when
    ///               things moved.
    ///
    ///               GRAIN AND RANGE ARE WHITELISTS. The client sends a key from a fixed
    ///               list, never an expression or a number that reaches SQL: an unknown
    ///               grain or range falls back to the default rather than being trusted.
    ///
    ///               Amounts are converted with the currencyConvert(...) DB function into
    ///               the tenant's primary accounting-schema currency
    ///               (AD_ClientInfo.C_AcctSchema1_ID -> C_AcctSchema.C_Currency_ID), dated
    ///               on each payment's own DateAcct and using its own conversion type - a
    ///               single bar cannot be built from added-up different currencies.
    ///
    ///               MRole row-level security is applied to C_Payment p, the main and only
    ///               physical table. GROUP BY and ORDER BY are appended AFTER AddAccessSQL
    ///               so its FROM-clause parser never meets a trailing clause. Compatible
    ///               with PostgreSQL and Oracle.
    /// Chronological development:
    ///   VAI154      2026-09-03 Created
    /// </summary>
    public class VAS_236_ReceiptvsPaymentModel
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_236_ReceiptvsPaymentModel).FullName);

        /* C_Payment.DocStatus codes that mean the money actually moved. Stored codes -
           compared bare, never with an N prefix. */
        private const string DOCSTATUS_Completed = "CO";
        private const string DOCSTATUS_Closed = "CL";

        /* C_Payment.IsReceipt stored codes. */
        private const string ISRECEIPT_Yes = "Y";
        private const string ISRECEIPT_No = "N";

        /* Grain whitelist. The client sends one of these and NOTHING else ever influences
           how the rows are bucketed. */
        public const string GRAIN_Day = "day";
        public const string GRAIN_Week = "week";
        public const string GRAIN_Month = "month";

        /* Range whitelist, in days. A number outside this set is refused rather than
           trusted - it is the one value that decides how much of the ledger is scanned. */
        public const int RANGE_Default = 14;
        private static readonly int[] RANGE_Allowed = new int[] { 7, 14, 30, 90 };

        /* A week starts on Monday, decided here rather than by the backend: PostgreSQL's
           DATE_TRUNC('week') is ISO (Monday) while Oracle's TRUNC(d,'IW') is also ISO but
           TRUNC(d,'W') is not, and a tenant should not see a different chart because of
           which database it runs. */
        private const DayOfWeek WEEK_FirstDay = DayOfWeek.Monday;

        // ─────────────────────────────────────────────────────────────────────
        // §1  Entry point
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The receipts / payments / net series for the requested grain and range, one
        /// point per period with the empty periods included.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="grain">"day", "week" or "month"; anything else falls back to day.</param>
        /// <param name="rangeDays">7, 14, 30 or 90; anything else falls back to 14.</param>
        /// <returns>Populated <see cref="TrendResult"/> (never null). Loaded is false only
        /// when there is no context or no accounting schema; a tenant with no payments in
        /// the range returns Loaded=true and a series of zeros, because a flat chart is a
        /// real answer rather than an error.</returns>
        public TrendResult GetTrend(Ctx ctx, string grain, int rangeDays)
        {
            TrendResult result = new TrendResult();
            result.Points = new List<TrendPoint>();
            result.Currency = new BaseCurrency();
            result.Grain = NormalizeGrain(grain);
            result.RangeDays = NormalizeRange(rangeDays);

            if (ctx == null) { return result; }

            result.Currency = GetBaseCurrency(ctx);
            if (result.Currency.C_Currency_ID <= 0)
            {
                Log.Log(Level.WARNING, "VAS_236_ReceiptvsPayment: AD_ClientInfo.C_AcctSchema1_ID not configured for AD_Client_ID="
                    + ctx.GetAD_Client_ID());
                return result;
            }

            DateTime today = DateTime.Now.Date;
            /* Inclusive first day, exclusive upper bound - a payment stamped today at 14:30
               is inside the window and nothing future-dated leaks in. */
            DateTime from = today.AddDays(-(result.RangeDays - 1));
            DateTime toExclusive = today.AddDays(1);

            /* The axis is built FIRST, from the calendar alone, so it exists whether or not
               a single payment is found - an empty range still draws a proper date scale. */
            result.Points = BuildBuckets(result.Grain, from, today);

            Dictionary<string, TrendPoint> byKey = IndexByKey(result.Points);
            ReadDailyTotals(ctx, from, toExclusive, result.Currency.C_Currency_ID, result.Grain, byKey);

            for (int i = 0; i < result.Points.Count; i++)
            {
                TrendPoint point = result.Points[i];

                point.Net = point.Receipts - point.Payments;

                result.TotalReceipts += point.Receipts;
                result.TotalPayments += point.Payments;
            }

            result.TotalNet = result.TotalReceipts - result.TotalPayments;
            result.Loaded = true;

            return result;
        }

        // ─────────────────────────────────────────────────────────────────────
        // §2  Whitelists
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Maps a client-supplied grain onto the whitelist. Anything unrecognised -
        /// including null, empty and any attempt at SQL - becomes the default.
        /// </summary>
        /// <param name="grain">Raw key from the request.</param>
        /// <returns>One of the GRAIN_* constants.</returns>
        private string NormalizeGrain(string grain)
        {
            if (String.IsNullOrEmpty(grain)) { return GRAIN_Day; }

            if (String.Equals(grain, GRAIN_Week, StringComparison.OrdinalIgnoreCase)) { return GRAIN_Week; }
            if (String.Equals(grain, GRAIN_Month, StringComparison.OrdinalIgnoreCase)) { return GRAIN_Month; }

            return GRAIN_Day;
        }

        /// <summary>
        /// Maps a client-supplied range onto the whitelist. This is the value that decides
        /// how much of the ledger is scanned, so an arbitrary number is refused rather than
        /// merely clamped.
        /// </summary>
        /// <param name="rangeDays">Raw value from the request.</param>
        /// <returns>One of the allowed ranges.</returns>
        private int NormalizeRange(int rangeDays)
        {
            for (int i = 0; i < RANGE_Allowed.Length; i++)
            {
                if (RANGE_Allowed[i] == rangeDays) { return rangeDays; }
            }
            return RANGE_Default;
        }

        // ─────────────────────────────────────────────────────────────────────
        // §3  The axis
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Every bucket the range covers, in order, all zero - built from the calendar
        /// alone. The chart draws this whether or not any payment is found, so an empty
        /// range still shows a proper date scale rather than an empty box, and a period
        /// nothing happened in is a zero rather than a gap that would misplace the line.
        /// </summary>
        /// <param name="grain">Normalised grain.</param>
        /// <param name="from">First day of the range (inclusive).</param>
        /// <param name="to">Last day of the range (inclusive).</param>
        /// <returns>Ordered, zeroed points (never null).</returns>
        private List<TrendPoint> BuildBuckets(string grain, DateTime from, DateTime to)
        {
            List<TrendPoint> points = new List<TrendPoint>();

            DateTime cursor = BucketStart(grain, from);
            /* Guard against a runaway loop if a future change makes Advance return the same
               date - the widest legitimate series is 90 daily buckets. */
            int guard = 0;

            while (cursor <= to && guard < 400)
            {
                TrendPoint point = new TrendPoint();
                point.Key = cursor.ToString("yyyy-MM-dd");
                point.BucketStart = point.Key;
                points.Add(point);

                cursor = Advance(grain, cursor);
                guard++;
            }

            return points;
        }

        /// <summary>The first day of the bucket a date belongs to.</summary>
        /// <param name="grain">Normalised grain.</param>
        /// <param name="date">Any date.</param>
        /// <returns>Bucket start date.</returns>
        private DateTime BucketStart(string grain, DateTime date)
        {
            DateTime day = date.Date;

            if (grain == GRAIN_Month) { return new DateTime(day.Year, day.Month, 1); }

            if (grain == GRAIN_Week)
            {
                /* Days to step back to reach WEEK_FirstDay. The +7 and %7 keep it positive
                   whatever the culture's first day of week is - this must not depend on the
                   server's locale. */
                int offset = ((int)day.DayOfWeek - (int)WEEK_FirstDay + 7) % 7;
                return day.AddDays(-offset);
            }

            return day;
        }

        /// <summary>The start of the bucket after this one.</summary>
        /// <param name="grain">Normalised grain.</param>
        /// <param name="bucketStart">Current bucket's first day.</param>
        /// <returns>Next bucket's first day.</returns>
        private DateTime Advance(string grain, DateTime bucketStart)
        {
            if (grain == GRAIN_Month) { return bucketStart.AddMonths(1); }
            if (grain == GRAIN_Week) { return bucketStart.AddDays(7); }
            return bucketStart.AddDays(1);
        }

        /// <summary>Indexes the series by bucket key so day rows can be merged in one
        /// pass instead of a nested search per row.</summary>
        /// <param name="points">The built series.</param>
        /// <returns>Key -> point.</returns>
        private Dictionary<string, TrendPoint> IndexByKey(List<TrendPoint> points)
        {
            Dictionary<string, TrendPoint> byKey = new Dictionary<string, TrendPoint>();
            for (int i = 0; i < points.Count; i++)
            {
                if (!byKey.ContainsKey(points[i].Key)) { byKey.Add(points[i].Key, points[i]); }
            }
            return byKey;
        }

        // ─────────────────────────────────────────────────────────────────────
        // §4  The figures
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Reads one row per accounting DATE and folds each into its bucket.
        ///
        /// The query groups by the raw DateAcct - a plain date column on both backends -
        /// and the day-to-bucket mapping happens here. See the class summary for why the
        /// grouping is not done in SQL.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="from">First day of the range (inclusive).</param>
        /// <param name="toExclusive">Exclusive upper bound (tomorrow).</param>
        /// <param name="acctCurrencyId">Primary accounting-schema C_Currency_ID.</param>
        /// <param name="grain">Normalised grain, for the day-to-bucket mapping.</param>
        /// <param name="byKey">The series to fill, indexed by bucket key.</param>
        private void ReadDailyTotals(Ctx ctx, DateTime from, DateTime toExclusive, int acctCurrencyId,
            string grain, Dictionary<string, TrendPoint> byKey)
        {
            /* The target currency is a server-resolved id, never client input, so it is
               inlined rather than bound - the conversion appears twice and the provider
               binds by POSITION. ABS() because the direction is carried by IsReceipt, never
               by the stored sign. */
            string convert = "currencyConvert(p.PayAmt,p.C_Currency_ID," + acctCurrencyId
                + ",p.DateAcct,p.C_ConversionType_ID,p.AD_Client_ID,p.AD_Org_ID)";

            StringBuilder sql = new StringBuilder();
            sql.Append(@"
                SELECT p.DateAcct AS Trx_Date,
                       COALESCE(SUM(CASE WHEN p.IsReceipt='").Append(ISRECEIPT_Yes).Append("' THEN ").Append(convert).Append(@" ELSE 0 END),0) AS Receipt_Amt,
                       COALESCE(SUM(CASE WHEN p.IsReceipt='").Append(ISRECEIPT_No).Append("' THEN ").Append(convert).Append(@" ELSE 0 END),0) AS Payment_Amt,
                       COALESCE(SUM(CASE WHEN p.IsReceipt='").Append(ISRECEIPT_Yes).Append(@"' THEN 1 ELSE 0 END),0) AS Receipt_Cnt,
                       COALESCE(SUM(CASE WHEN p.IsReceipt='").Append(ISRECEIPT_No).Append(@"' THEN 1 ELSE 0 END),0) AS Payment_Cnt
                FROM C_Payment p
                WHERE p.IsActive='Y'
                  AND p.AD_Client_ID=@AD_Client_ID
                  AND p.DocStatus IN ('").Append(DOCSTATUS_Completed).Append("','").Append(DOCSTATUS_Closed).Append(@"')
                  AND p.DateAcct>=@Date_From
                  AND p.DateAcct<@Date_To");

            /* C_Payment p is the main and only physical table. MRole supplies the
               organisation access clause, so no AD_Org_ID predicate is written by hand -
               the explicit tenant filter is a second, independent guard rather than the
               only one. Flat SUM(CASE ...) aggregation, never nested selects, so the access
               parser has one simple FROM clause to read. */
            string finalSql = MRole.GetDefault(ctx).AddAccessSQL(sql.ToString(), "p",
                MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            /* GROUP BY and ORDER BY go on AFTER the access SQL - its FROM-clause parser
               must not meet a trailing clause. Grouping by the bare date column needs no
               dialect-specific truncation. */
            finalSql += " GROUP BY p.DateAcct ORDER BY p.DateAcct";

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@Date_From", from),
                new SqlParameter("@Date_To", toExclusive)
            };

            DataSet ds = DB.ExecuteDataset(finalSql, parameters, null);
            if (ds == null || ds.Tables.Count == 0) { return; }

            DataTable dt = ds.Tables[0];
            for (int i = 0; i < dt.Rows.Count; i++)
            {
                DataRow row = dt.Rows[i];

                DateTime? date = Util.GetValueOfDateTime(row["Trx_Date"]);
                if (!date.HasValue) { continue; }

                /* Fold the day onto its bucket. A date outside the built axis - which
                   should not happen, but a stray DateAcct is cheaper to skip than to
                   trust - is simply dropped rather than creating a bucket off the scale. */
                string key = BucketStart(grain, date.Value).ToString("yyyy-MM-dd");
                if (!byKey.ContainsKey(key)) { continue; }

                TrendPoint point = byKey[key];
                point.Receipts += Util.GetValueOfDecimal(row["Receipt_Amt"]);
                point.Payments += Util.GetValueOfDecimal(row["Payment_Amt"]);
                point.ReceiptCount += Util.GetValueOfInt(row["Receipt_Cnt"]);
                point.PaymentCount += Util.GetValueOfInt(row["Payment_Cnt"]);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // §5  Base currency
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The tenant's base currency: the currency of the primary accounting schema
        /// (AD_ClientInfo.C_AcctSchema1_ID). Reads only system / reference tables scoped to
        /// the session client, so no MRole predicate is applied - the same treatment the
        /// sibling KPI widgets give this lookup.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <returns>Populated <see cref="BaseCurrency"/>; C_Currency_ID is 0 when the tenant
        /// has no primary accounting schema.</returns>
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
        // §6  Transfer objects
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>Everything the widget needs on one paint.</summary>
        public class TrendResult
        {
            /// <summary>One point per period, oldest first, empty periods included.</summary>
            public List<TrendPoint> Points { get; set; }

            /// <summary>The grain actually applied, echoed back for the control's label.</summary>
            public string Grain { get; set; }

            /// <summary>The range in days actually applied, echoed back likewise.</summary>
            public int RangeDays { get; set; }

            /// <summary>Receipts across the whole range - the subtitle's first figure.</summary>
            public decimal TotalReceipts { get; set; }

            /// <summary>Payments across the whole range.</summary>
            public decimal TotalPayments { get; set; }

            /// <summary>TotalReceipts minus TotalPayments.</summary>
            public decimal TotalNet { get; set; }

            /// <summary>Currency every amount here is stated in.</summary>
            public BaseCurrency Currency { get; set; }

            /// <summary>False only on a failure or a missing accounting schema; a range with
            /// no payments is Loaded=true with a series of zeros.</summary>
            public bool Loaded { get; set; }
        }

        /// <summary>One period on the chart.</summary>
        public class TrendPoint
        {
            /// <summary>Bucket start as yyyy-MM-dd - the series key.</summary>
            public string Key { get; set; }

            /// <summary>The same date, for the client to format into an axis label per
            /// grain. The LABEL is never built here: a date's presentation belongs to the
            /// reader's locale, not to a query.</summary>
            public string BucketStart { get; set; }

            /// <summary>Money in during this period, in base currency.</summary>
            public decimal Receipts { get; set; }

            /// <summary>Money out during this period, in base currency.</summary>
            public decimal Payments { get; set; }

            /// <summary>Receipts minus Payments - the net line's value.</summary>
            public decimal Net { get; set; }

            /// <summary>Number of receipts behind Receipts.</summary>
            public int ReceiptCount { get; set; }

            /// <summary>Number of payments behind Payments.</summary>
            public int PaymentCount { get; set; }
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
    }
}
