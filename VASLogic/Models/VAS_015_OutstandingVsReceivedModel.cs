/******************************************************
 * Module Name    : VASLogic
 * Purpose        : Outstanding vs Received dashboard widget data
 * chronological  : Development
 * Created Date   : 2026-06-02
 * Created by     : VAI145
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
    /// Module Name : VAS_015_OutstandingVsReceived
    /// Purpose     : Backs the VAS_015_OutstandingVsReceived dashboard widget.
    ///               Compares, per month over the last 6 calendar months, the open
    ///               sales receivable balance (bars, SUM(C_InvoicePaySchedule.DueAmt)
    ///               of unpaid sales schedules by due month) against actual customer
    ///               collections (line, by month). The collections line sums BOTH AR
    ///               receipts (SUM(C_Payment.PaymentAmount) by trx month) and
    ///               cash-journal collections (SUM(C_CashLine.Amount) of Receipt /
    ///               ReceiptReturn lines by statement month). All series are
    ///               converted to the accounting-schema (base) currency; the
    ///               base-currency lookup is scoped to the session client (client
    ///               check). MRole row-level security is applied only on the main
    ///               physical table of each query (C_Payment and C_Cash for received,
    ///               C_Invoice for outstanding); the secondary joined tables
    ///               (C_InvoicePaySchedule, C_CashLine) source amounts only and are
    ///               not given an access predicate. The 6-month calendar fill is
    ///               computed in C# so each SQL stays a simple portable GROUP BY (no
    ///               CTE / window functions). Compatible with PostgreSQL and Oracle.
    /// Chronological development:
    ///   VAI145      2026-06-02 Created
    ///   VAI145      2026-07-14 Add cash-journal Receipt / ReceiptReturn collections
    ///                          (C_Cash / C_CashLine) to the received series.
    /// </summary>
    public class VAS_015_OutstandingVsReceivedModel
    {
        /// <summary>
        /// Returns the per-month outstanding-vs-received series for the session
        /// client over the last <paramref name="months"/> months, oldest first,
        /// with every month present (missing months = 0). Amounts are in the
        /// base/accounting currency.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="months">Window length in months (default 6).</param>
        /// <returns>Populated <see cref="OutstandingVsReceived"/>.</returns>
        public OutstandingVsReceived GetSeries(Ctx ctx, int months = 6)
        {
            OutstandingVsReceived result = new OutstandingVsReceived();
            result.Months = new List<MonthPoint>();

            if (ctx == null) { return result; }
            if (months <= 0) { months = 6; }

            int clientId = ctx.GetAD_Client_ID();

            /* Window: first day of (current month - (months-1)) .. first day of the
               month AFTER the current month (exclusive). Boundaries are computed in
               C# and inlined as dialect-aware date literals — trusted values, no
               user input, so no injection risk. */
            DateTime firstOfCurrent = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            DateTime startMonth = firstOfCurrent.AddMonths(-(months - 1));
            DateTime endExclusive = firstOfCurrent.AddMonths(1);

            /* 1) Base-currency lookup, scoped to the session client (client check on
               the base-currency query). Reads only system/reference tables so no
               MRole predicate is applied. */
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
                BuildMonths(result, months, startMonth,
                    new Dictionary<string, decimal>(), new Dictionary<string, decimal>());
                return result;
            }

            string startLit = DateLiteral(startMonth);
            string endLit = DateLiteral(endExclusive);

            Dictionary<string, decimal> receivedByMonth =
                LoadReceived(ctx, clientId, acctCurrencyId, startLit, endLit, result.StdPrecision);
            Dictionary<string, decimal> outstandingByMonth =
                LoadOutstanding(ctx, clientId, acctCurrencyId, startLit, endLit, result.StdPrecision);

            BuildMonths(result, months, startMonth, outstandingByMonth, receivedByMonth);
            return result;
        }

        /// <summary>
        /// Monthly customer-collection totals converted to the base currency, keyed
        /// by yyyy-MM. Sums two sources by month: AR receipts
        /// (C_Payment.PaymentAmount of completed/closed receipts, by transaction
        /// month) and cash-journal collections (C_CashLine.Amount of completed/closed
        /// lines whose VSS_PAYMENTTYPE is a customer Receipt or ReceiptReturn, by
        /// statement month). MRole is applied independently to each main physical
        /// table (C_Payment; C_Cash — the joined C_CashLine is a child detail).
        /// </summary>
        private Dictionary<string, decimal> LoadReceived(Ctx ctx, int clientId, int acctCurrencyId,
            string startLit, string endLit, int stdPrecision)
        {
            /* Month grouping is dialect-aware (date_trunc on PostgreSQL, TRUNC on
               Oracle); both yield the first-of-month DATE used as the bucket key. */
            string monthExpr = DB.IsPostgreSQL()
                ? "CAST(date_trunc('month', Payment.DateTrx) AS DATE)"
                : "TRUNC(Payment.DateTrx, 'MM')";

            /* acctCurrencyId is a trusted int from the schema lookup, so it is
               inlined (same style as clientId) rather than parameterized. PaymentAmount is
               the net receipt amount (not PaymentAmount2). */
            string sql = @"
                SELECT " + monthExpr + @" AS Bucket_Month,
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
                       ) AS Received_Amount
                FROM C_Payment Payment
                WHERE Payment.IsReceipt = 'Y'
                  AND Payment.IsActive = 'Y'
                  AND Payment.DocStatus IN ('CO', 'CL')
                  AND Payment.AD_Client_ID = " + clientId + @"
                  AND Payment.DateTrx >= " + startLit + @"
                  AND Payment.DateTrx < " + endLit;

            /* MRole only on the main physical table (C_Payment / alias Payment).
               GROUP BY is appended AFTER MRole so the FROM-clause parser is not
               confused by a trailing clause. */
            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql, "Payment", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            sql += @"
                GROUP BY " + monthExpr;

            Dictionary<string, decimal> receivedByMonth =
                ReadBuckets(sql, "Received_Amount", stdPrecision);

            /* Cash-journal collections recorded directly in the cash book also
               count as money received. Include completed/closed C_CashLine rows
               whose VSS_PAYMENTTYPE is a customer Receipt or ReceiptReturn (the
               return leg is signed by its own Amount), bucket them by statement
               month and ADD them into the receipt totals. Same source, currency
               conversion and payment-type rule as the Daily Collection Trend
               widget (VAS_014). */
            string cashMonthExpr = DB.IsPostgreSQL()
                ? "CAST(date_trunc('month', Cash.StatementDate) AS DATE)"
                : "TRUNC(Cash.StatementDate, 'MM')";

            /* VSS_PAYMENTTYPE list values rendered as SQL literals via the same
               framework helper used elsewhere (e.g. VAS_014). */
            string cashReceiptTypes =
                GlobalVariable.TO_STRING(MCashLine.VSS_PAYMENTTYPE_Receipt) + ", " +
                GlobalVariable.TO_STRING(MCashLine.VSS_PAYMENTTYPE_ReceiptReturn);

            string cashSql = @"
                SELECT " + cashMonthExpr + @" AS Bucket_Month,
                       SUM(
                           CASE
                               WHEN Cash.C_Currency_ID = " + acctCurrencyId + @"
                               THEN COALESCE(CashLine.Amount, 0)
                               ELSE CurrencyConvert(
                                   COALESCE(CashLine.Amount, 0),
                                   CashLine.C_Currency_ID,
                                   " + acctCurrencyId + @",
                                   COALESCE(Cash.DateAcct, Cash.StatementDate),
                                   COALESCE(CashLine.C_ConversionType_ID, 0),
                                   CashLine.AD_Client_ID,
                                   CashLine.AD_Org_ID
                               )
                           END
                       ) AS Received_Amount
                FROM C_Cash Cash
                INNER JOIN C_CashLine CashLine ON (CashLine.C_Cash_ID = Cash.C_Cash_ID)
                WHERE Cash.IsActive = 'Y'
                  AND CashLine.IsActive = 'Y'
                  AND CashLine.VSS_PAYMENTTYPE IN (" + cashReceiptTypes + @")
                  AND Cash.DocStatus IN ('CO', 'CL')
                  AND Cash.AD_Client_ID = " + clientId + @"
                  AND Cash.StatementDate >= " + startLit + @"
                  AND Cash.StatementDate < " + endLit;

            /* MRole only on the main physical table (C_Cash / alias Cash); the
               joined C_CashLine is a child of the access-controlled header. GROUP
               BY is appended AFTER MRole so the FROM-clause parser is not confused
               by a trailing clause. */
            cashSql = MRole.GetDefault(ctx).AddAccessSQL(
                cashSql, "Cash", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            cashSql += @"
                GROUP BY " + cashMonthExpr;

            Dictionary<string, decimal> cashByMonth =
                ReadBuckets(cashSql, "Received_Amount", stdPrecision);

            foreach (KeyValuePair<string, decimal> entry in cashByMonth)
            {
                if (receivedByMonth.ContainsKey(entry.Key))
                {
                    receivedByMonth[entry.Key] = receivedByMonth[entry.Key] + entry.Value;
                }
                else
                {
                    receivedByMonth[entry.Key] = entry.Value;
                }
            }

            return receivedByMonth;
        }

        /// <summary>
        /// Monthly open sales-receivable totals (SUM(C_InvoicePaySchedule.DueAmt) of
        /// unpaid sales schedules, by due month) converted to the base currency,
        /// keyed by yyyy-MM. Return/credit-note documents are signed negative.
        /// MRole is applied only on the main physical table C_Invoice; the joined
        /// C_InvoicePaySchedule is a secondary detail table (no access predicate).
        /// </summary>
        private Dictionary<string, decimal> LoadOutstanding(Ctx ctx, int clientId, int acctCurrencyId,
            string startLit, string endLit, int stdPrecision)
        {
            string monthExpr = DB.IsPostgreSQL()
                ? "CAST(date_trunc('month', ips.DueDate) AS DATE)"
                : "TRUNC(ips.DueDate, 'MM')";

            string sql = @"
                SELECT " + monthExpr + @" AS Bucket_Month,
                       SUM(
                           CASE WHEN i.IsReturnTrx = 'Y' THEN -1 ELSE 1 END *
                           CASE
                               WHEN i.C_Currency_ID = " + acctCurrencyId + @"
                               THEN COALESCE(ips.DueAmt, 0)
                               ELSE CurrencyConvert(
                                   COALESCE(ips.DueAmt, 0),
                                   i.C_Currency_ID,
                                   " + acctCurrencyId + @",
                                   COALESCE(i.DateAcct, i.DateInvoiced),
                                   i.C_ConversionType_ID,
                                   i.AD_Client_ID,
                                   i.AD_Org_ID
                               )
                           END
                       ) AS Outstanding_Amount
                FROM C_Invoice i
                INNER JOIN C_InvoicePaySchedule ips ON (ips.C_Invoice_ID=i.C_Invoice_ID)
                WHERE i.IsSOTrx = 'Y'
                  AND i.IsActive = 'Y'
                  AND i.DocStatus IN ('CO', 'CL')
                  AND i.AD_Client_ID = " + clientId + @"
                  AND COALESCE(ips.IsActive, 'Y') = 'Y'
                  AND COALESCE(ips.VA009_IsPaid, 'N') = 'N'
                  AND ips.DueDate >= " + startLit + @"
                  AND ips.DueDate < " + endLit;

            /* MRole only on the main physical table (C_Invoice / alias i). The
               joined C_InvoicePaySchedule is used solely to source DueAmt, so it
               receives no access predicate (per the CTE/self-join rule). GROUP BY is
               appended AFTER MRole. */
            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql, "i", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            sql += @"
                GROUP BY " + monthExpr;

            return ReadBuckets(sql, "Outstanding_Amount", stdPrecision);
        }

        /// <summary>
        /// Executes a bucket query and returns the rounded amount per yyyy-MM key.
        /// </summary>
        /// <param name="sql">Bucket SQL returning Bucket_Month + an amount column.</param>
        /// <param name="amountColumn">Name of the amount column to read.</param>
        /// <param name="stdPrecision">Base-currency rounding precision.</param>
        /// <returns>Amount keyed by yyyy-MM.</returns>
        private Dictionary<string, decimal> ReadBuckets(string sql, string amountColumn, int stdPrecision)
        {
            Dictionary<string, decimal> byMonth = new Dictionary<string, decimal>();
            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql);
                while (dr != null && dr.Read())
                {
                    DateTime? bucket = Util.GetValueOfDateTime(dr["Bucket_Month"]);
                    if (!bucket.HasValue) { continue; }

                    decimal amount = Math.Round(
                        Util.GetValueOfDecimal(dr[amountColumn]),
                        stdPrecision,
                        MidpointRounding.AwayFromZero
                    );

                    byMonth[bucket.Value.ToString("yyyy-MM", CultureInfo.InvariantCulture)] = amount;
                }
            }
            finally
            {
                if (dr != null) { dr.Close(); dr.Dispose(); }
            }
            return byMonth;
        }

        /// <summary>
        /// Fills a contiguous oldest→newest month series (missing months = 0) from
        /// the per-month outstanding and received totals.
        /// </summary>
        /// <param name="result">Result being populated (Months set here).</param>
        /// <param name="months">Window length in months.</param>
        /// <param name="startMonth">First month (first-of-month date) of the window.</param>
        /// <param name="outstandingByMonth">Outstanding totals keyed by yyyy-MM.</param>
        /// <param name="receivedByMonth">Received totals keyed by yyyy-MM.</param>
        private void BuildMonths(OutstandingVsReceived result, int months, DateTime startMonth,
            Dictionary<string, decimal> outstandingByMonth, Dictionary<string, decimal> receivedByMonth)
        {
            for (int i = 0; i < months; i++)
            {
                DateTime month = startMonth.AddMonths(i);
                string key = month.ToString("yyyy-MM", CultureInfo.InvariantCulture);

                decimal outstanding = 0;
                if (outstandingByMonth.ContainsKey(key)) { outstanding = outstandingByMonth[key]; }

                decimal received = 0;
                if (receivedByMonth.ContainsKey(key)) { received = receivedByMonth[key]; }

                result.Months.Add(new MonthPoint
                {
                    Month = key,
                    Outstanding = outstanding,
                    Received = received
                });
            }
        }

        /// <summary>
        /// Builds a dialect-aware DATE literal for a trusted (code-computed) date.
        /// PostgreSQL uses the ANSI <c>DATE 'yyyy-MM-dd'</c> form; Oracle uses
        /// <c>TO_DATE</c> with an explicit format mask.
        /// </summary>
        /// <param name="value">Date to render.</param>
        /// <returns>SQL date-literal fragment.</returns>
        private static string DateLiteral(DateTime value)
        {
            string iso = value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return DB.IsPostgreSQL()
                ? "DATE '" + iso + "'"
                : "TO_DATE('" + iso + "','YYYY-MM-DD')";
        }

        /// <summary>
        /// Result envelope: shared base-currency descriptors plus the dated
        /// month series (outstanding bars + received line).
        /// </summary>
        public class OutstandingVsReceived
        {
            public string CurrencySymbol { get; set; }
            public string CurrencyIso { get; set; }
            public int StdPrecision { get; set; }
            public List<MonthPoint> Months { get; set; }
        }

        /// <summary>One month bucket: open receivable (bar) and collected (line).</summary>
        public class MonthPoint
        {
            public string Month { get; set; }       /* yyyy-MM */
            public decimal Outstanding { get; set; }
            public decimal Received { get; set; }
        }
    }
}
