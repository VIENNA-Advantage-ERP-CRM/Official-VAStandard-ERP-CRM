/******************************************************
 * Module Name    : VASLogic
 * Purpose        : Daily Collection Trend dashboard widget data
 * chronological  : Development
 * Created Date   : 2026-06-02
 * Created by     : VAI154
 ******************************************************/

using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using VAdvantage.DataBase;
using VAdvantage.Model;
using VAdvantage.Utility;

namespace VASLogic.Models
{
    /// <summary>
    /// Module Name : VAS_014_DailyCollectionTrend
    /// Purpose     : Backs the VAS_014_DailyCollectionTrend dashboard widget.
    ///               Returns daily AR receipt cash-inflow (C_Payment.PaymentAmount)
    ///               for the last N days (default 14), converted to the
    ///               accounting-schema (base) currency, plus the N-day average and
    ///               the peak day. The base-currency lookup is scoped to the
    ///               session client; MRole row-level security is applied only on
    ///               the main physical table (C_Payment, alias Payment). The
    ///               calendar fill, average and peak are computed in C# so the SQL
    ///               stays a simple portable GROUP BY (no UNION calendar / window
    ///               functions). Compatible with PostgreSQL and Oracle.
    /// Chronological development:
    ///   VAI154      2026-06-02 Created
    /// </summary>
    public class VAS_014_DailyCollectionTrendModel
    {
        /// <summary>
        /// Returns the daily collection trend for the session client over the
        /// last <paramref name="days"/> days (transaction date), oldest first,
        /// with every calendar day present (missing days = 0). Amounts are in the
        /// base/accounting currency.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="days">Window length in days (default 14).</param>
        /// <returns>Populated <see cref="DailyCollectionTrend"/>.</returns>
        public DailyCollectionTrend GetDailyTrend(Ctx ctx, int days = 14)
        {
            DailyCollectionTrend result = new DailyCollectionTrend();
            result.Points = new List<TrendPoint>();

            if (ctx == null) { return result; }
            if (days <= 0) { days = 14; }

            int clientId = ctx.GetAD_Client_ID();

            /* 1) Base-currency lookup, scoped to the session client (client check
               on the base-currency query). Reads only system/reference tables so
               no MRole predicate is applied. */
            int acctCurrencyId = 0;
            string baseCurrencySql = @"
                SELECT AcctSchema.C_Currency_ID AS Acct_Currency_ID,
                       Currency.StdPrecision AS Std_Precision,
                       CASE WHEN Currency.CurSymbol IS NOT NULL THEN Currency.CurSymbol ELSE Currency.ISO_Code END AS Currency_Symbol,
                       Currency.ISO_Code AS Currency_ISO
                FROM AD_ClientInfo ClientInfo
                INNER JOIN C_AcctSchema AcctSchema ON (AcctSchema.C_AcctSchema_ID=ClientInfo.C_AcctSchema1_ID)
                INNER JOIN C_Currency Currency ON (Currency.C_Currency_ID=AcctSchema.C_Currency_ID)
                WHERE ClientInfo.AD_Client_ID=" + clientId;

            IDataReader cdr = null;
            try
            {
                cdr = DB.ExecuteReader(baseCurrencySql);
                if (cdr != null && cdr.Read())
                {
                    acctCurrencyId = Util.GetValueOfInt(cdr["Acct_Currency_ID"]);
                    result.StdPrecision = Util.GetValueOfInt(cdr["Std_Precision"]);
                    result.CurrencySymbol = Util.GetValueOfString(cdr["Currency_Symbol"]);
                    result.CurrencyIso = Util.GetValueOfString(cdr["Currency_ISO"]);
                }
            }
            finally
            {
                if (cdr != null) { cdr.Close(); cdr.Dispose(); }
            }

            /* No accounting schema currency resolved — return a flat zero-filled
               window so the chart still renders a baseline. */
            if (acctCurrencyId <= 0)
            {
                BuildCalendar(result, days, new Dictionary<string, decimal>());
                return result;
            }

            /* 2) Daily receipt totals, converted to the base currency. Day
               grouping + window are dialect-aware. The (date - integer) form is
               the only well-defined day arithmetic on PostgreSQL (where DateTrx
               behaves as a timestamp); Oracle uses TRUNC. */
            string dayExpr;
            string dateCondition;
            if (DB.IsPostgreSQL())
            {
                dayExpr = "CAST(Payment.DateTrx AS DATE)";
                dateCondition = "Payment.DateTrx >= CURRENT_DATE - " + (days - 1) + " AND Payment.DateTrx < CURRENT_DATE + 1";
            }
            else
            {
                dayExpr = "TRUNC(Payment.DateTrx)";
                dateCondition = "TRUNC(Payment.DateTrx) >= TRUNC(SYSDATE) - " + (days - 1) + " AND TRUNC(Payment.DateTrx) <= TRUNC(SYSDATE)";
            }

            /* acctCurrencyId is a trusted int from the schema lookup above, so it
               is inlined (same style as clientId) rather than parameterized. */
            string dailySql = @"
                SELECT " + dayExpr + @" AS Collection_Date,
                       SUM(
                           CASE
                               WHEN Payment.C_Currency_ID = " + acctCurrencyId + @"
                               THEN COALESCE(Payment.PaymentAmount, 0)
                               ELSE CurrencyConvert(
                                   COALESCE(Payment.PaymentAmount, 0),
                                   Payment.C_Currency_ID,
                                   " + acctCurrencyId + @",
                                   COALESCE(Payment.DateAcct, Payment.DateTrx),
                                   Payment.C_ConversionType_ID,
                                   Payment.AD_Client_ID,
                                   Payment.AD_Org_ID
                               )
                           END
                       ) AS Daily_Amount
                FROM C_Payment Payment
                WHERE Payment.IsReceipt = 'Y'
                  AND Payment.IsActive = 'Y'
                  AND Payment.DocStatus IN ('CO', 'CL')
                  AND Payment.AD_Client_ID = " + clientId + @"
                  AND " + dateCondition;

            /* MRole only on the main physical table (C_Payment / alias Payment).
               GROUP BY / ORDER BY are appended AFTER MRole so the FROM-clause
               parser is not confused by a trailing clause. */
            dailySql = MRole.GetDefault(ctx).AddAccessSQL(
                dailySql,
                "Payment",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            dailySql += @"
                GROUP BY " + dayExpr + @"
                ORDER BY " + dayExpr;

            Dictionary<string, decimal> dailyByDate = new Dictionary<string, decimal>();
            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(dailySql);
                while (dr != null && dr.Read())
                {
                    DateTime? day = Util.GetValueOfDateTime(dr["Collection_Date"]);
                    if (!day.HasValue) { continue; }

                    decimal amount = Math.Round(
                        Util.GetValueOfDecimal(dr["Daily_Amount"]),
                        result.StdPrecision,
                        MidpointRounding.AwayFromZero
                    );

                    dailyByDate[day.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)] = amount;
                }
            }
            finally
            {
                if (dr != null) { dr.Close(); dr.Dispose(); }
            }

            BuildCalendar(result, days, dailyByDate);
            return result;
        }

        /// <summary>
        /// Fills a contiguous oldest→newest day series (missing days = 0) from the
        /// per-day totals, then computes the average (sum / days) and the peak day.
        /// </summary>
        /// <param name="result">Result being populated (Points/Average/Peak set here).</param>
        /// <param name="days">Window length in days.</param>
        /// <param name="dailyByDate">Per-day totals keyed by yyyy-MM-dd.</param>
        private void BuildCalendar(DailyCollectionTrend result, int days, Dictionary<string, decimal> dailyByDate)
        {
            DateTime today = DateTime.Today;
            DateTime start = today.AddDays(-(days - 1));

            decimal total = 0;
            decimal peakAmount = 0;
            string peakDay = "";

            for (int i = 0; i < days; i++)
            {
                DateTime day = start.AddDays(i);
                string key = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

                decimal amount = 0;
                if (dailyByDate.ContainsKey(key)) { amount = dailyByDate[key]; }

                total += amount;

                /* First-wins on ties keeps a single, stable peak callout. */
                if (amount > peakAmount)
                {
                    peakAmount = amount;
                    peakDay = key;
                }

                result.Points.Add(new TrendPoint { Day = key, Amount = amount });
            }

            /* If everything is zero, anchor the peak callout to the latest day so
               the legend still has a date to show. */
            if (peakDay.Length == 0 && result.Points.Count > 0)
            {
                peakDay = result.Points[result.Points.Count - 1].Day;
            }

            result.Average = Math.Round(total / days, result.StdPrecision, MidpointRounding.AwayFromZero);
            result.PeakAmount = peakAmount;
            result.PeakDay = peakDay;
        }

        /// <summary>
        /// Result envelope: shared base-currency descriptors, the dated point
        /// series, the window average and the peak day.
        /// </summary>
        public class DailyCollectionTrend
        {
            public string CurrencySymbol { get; set; }
            public string CurrencyIso { get; set; }
            public int StdPrecision { get; set; }
            public decimal Average { get; set; }
            public decimal PeakAmount { get; set; }
            public string PeakDay { get; set; }
            public List<TrendPoint> Points { get; set; }
        }

        /// <summary>One calendar day and its total collection amount (base currency).</summary>
        public class TrendPoint
        {
            public string Day { get; set; }
            public decimal Amount { get; set; }
        }
    }
}
