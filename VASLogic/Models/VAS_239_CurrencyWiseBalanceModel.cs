/******************************************************
 * Module Name    : VASLogic
 * Purpose        : Currency-wise Balance dashboard widget data
 * chronological  : Development
 * Created Date   : 2026-09-04
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
    /// Module Name : VAS_239_CurrencyWiseBalance
    /// Purpose     : Backs the VAS_239_CurrencyWiseBalanceWidget dashboard widget - what the
    ///               tenant holds in each bank-account currency, and what that is worth in
    ///               the primary accounting schema's currency:
    ///
    ///                 Native      SUM of the LATEST C_BankAccountLine.EndingBalance per bank
    ///                             account, grouped by C_BankAccount.C_Currency_ID. Stated in
    ///                             that currency and NEVER converted.
    ///                 Base        the same latest balances converted one account at a time
    ///                             into the base currency and then summed.
    ///                 Total       SUM of the base column, and nothing else. Native amounts in
    ///                             different currencies are never added together.
    ///
    ///               THE BALANCE SOURCE IS C_BankAccountLine.EndingBalance - deliberately not
    ///               C_BankStatement.EndingBalance, not C_BankAccount.CurrentBalance and not
    ///               Fact_Acct. EndingBalance is the balance after the beginning balance has
    ///               been adjusted for the period's payments and disbursements, which is the
    ///               figure this card is defined to report.
    ///
    ///               ONE LINE PER ACCOUNT, THE LATEST ONE. C_BankAccountLine keeps a history
    ///               row per account, so summing the table would add an account's own past
    ///               balances to its present one and inflate every currency. The latest line
    ///               per account is StatementDate DESC then C_BankAccountLine_ID DESC (the id
    ///               breaks a same-date tie deterministically), bounded above by the as-of
    ///               date so a future-dated line is never mistaken for the current balance.
    ///
    ///               NO WINDOW FUNCTION. The obvious shape for "latest per group" is
    ///               ROW_NUMBER() OVER (PARTITION BY ... ORDER BY ...), but that puts an ORDER
    ///               BY inside the SELECT list and MRole's AddAccessSQL parser has to be kept
    ///               clear of clauses of that kind - the sibling cards (VAS_234) hit this
    ///               first. The equivalent portable shape is used instead: ONE date-ordered
    ///               read, oldest first, and a single forward pass that ends up holding the
    ///               newest line per account. That is still set-based - there is no query per
    ///               account anywhere in this model.
    ///
    ///               TWO QUERIES, NOT ONE, AND THAT IS DELIBERATE. Conversion runs in a second
    ///               set-based query restricted to the latest line ids the first pass
    ///               resolved. Folding currencyConvert(...) into the first query would call it
    ///               once per HISTORY row and then discard all but the newest - the cost of
    ///               the whole history rather than of the answer.
    ///
    ///               BASE CURRENCY IS RESOLVED, NEVER ASSUMED. AD_ClientInfo.C_AcctSchema1_ID
    ///               -&gt; C_AcctSchema.C_Currency_ID, with that currency's ISO code, symbol
    ///               and StdPrecision. A tenant with no primary accounting schema gets a
    ///               configuration state, not a fabricated INR column. The conversion type is
    ///               MConversionType.GetDefault(AD_Client_ID) - never a hard-coded id - and
    ///               resolves to SQL NULL when the tenant has no default, which lets the
    ///               database function apply its own.
    ///
    ///               A MISSING RATE IS NOT A ZERO. currencyConvert returns NULL when no rate
    ///               covers the line's StatementDate. Such a line is counted in
    ///               MissingRateCount and left OUT of the base sum; its currency keeps its
    ///               native figure and reports the gap, and the grand total is withheld
    ///               (TotalAvailable=false) rather than quietly under-reported.
    ///
    ///               MRole row-level security is applied to C_BankAccountLine bal, the main
    ///               physical table of both reads - never to C_BankAccount / C_Currency /
    ///               C_AcctSchema / AD_ClientInfo, which are joined only for metadata, and
    ///               never to a derived or grouped result. ORDER BY is appended AFTER
    ///               AddAccessSQL so its FROM-clause parser never meets a trailing clause, and
    ///               every join ON is a plain equality so it never meets a function call
    ///               either. Compatible with PostgreSQL and Oracle.
    /// Chronological development:
    ///   VAI154      2026-09-04 Created
    /// </summary>
    public class VAS_239_CurrencyWiseBalanceModel
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_239_CurrencyWiseBalanceModel).FullName);

        /* Paging. The mock shows five currency rows; a shorter cell may ask for fewer and a
           taller one for more, but never outside these bounds. */
        public const int DEFAULT_PageSize = 5;
        private const int MIN_PageSize = 1;
        private const int MAX_PageSize = 10;

        /* Latest-line ids per conversion query. Oracle refuses an IN list longer than 1000
           expressions, so the ids are sent in batches well inside that limit. One batch
           covers any realistic tenant - the list is one id per bank account. */
        private const int CONVERT_BatchSize = 500;

        // ─────────────────────────────────────────────────────────────────────
        // §1  Entry point
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// One page of currency rows, the base currency they are measured in, and the grand
        /// total behind them.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="pageNo">1-based page; clamped to the available range.</param>
        /// <param name="pageSize">Rows per page; clamped to [1,10].</param>
        /// <returns>Populated <see cref="CurrencyBalanceResult"/> (never null). Loaded is
        /// false only when there is no context or no primary accounting schema; a tenant with
        /// no bank balance lines returns Loaded=true and an empty page, because "no balances
        /// recorded" is a real answer rather than an error.</returns>
        public CurrencyBalanceResult GetCurrencyBalances(Ctx ctx, int pageNo, int pageSize)
        {
            CurrencyBalanceResult result = new CurrencyBalanceResult();
            result.Rows = new List<CurrencyRow>();
            result.PageSize = ClampPageSize(pageSize);
            result.Page = pageNo < 1 ? 1 : pageNo;

            if (ctx == null) { result.Page = 1; return result; }

            /* The Banking dashboard has no shared as-of date context, so the date is resolved
               HERE - once per request, so every row on the card is stated as of the same
               instant - and never accepted from the browser. */
            DateTime asOf = DateTime.Now.Date;
            result.AsOfDate = asOf.ToString("yyyy-MM-dd");

            /* Without a primary accounting schema there is no base currency, and a base column
               is half of what this card says. That is a configuration state of its own, not an
               empty list and not an error. */
            BaseCurrency baseCurrency = GetBaseCurrency(ctx);
            if (baseCurrency == null)
            {
                Log.Log(Level.WARNING, "VAS_239_CurrencyWiseBalance: no primary accounting schema currency for AD_Client_ID="
                    + ctx.GetAD_Client_ID());
                result.NoAcctSchema = true;
                result.Page = 1;
                return result;
            }

            result.BaseCurrency = baseCurrency;

            List<LatestLine> lines = GetLatestLinePerAccount(ctx, asOf);
            if (lines.Count > 0)
            {
                ApplyBaseAmounts(ctx, baseCurrency, lines);
            }

            List<CurrencyRow> rows = GroupByCurrency(lines, baseCurrency);
            SortRows(rows);
            ApplyTotals(result, rows);

            result.TotalRows = rows.Count;
            result.TotalPages = result.PageSize > 0
                ? (int)Math.Ceiling((double)rows.Count / result.PageSize)
                : 0;

            /* Clamp the page AFTER the total is known: a page number the client kept from a
               longer list must land on the last real page, never past the end. */
            int page = result.Page;
            if (result.TotalPages > 0 && page > result.TotalPages) { page = result.TotalPages; }
            if (result.TotalPages == 0) { page = 1; }
            result.Page = page;

            int start = (page - 1) * result.PageSize;
            for (int i = start; i < rows.Count && i < start + result.PageSize; i++)
            {
                result.Rows.Add(rows[i]);
            }

            result.Loaded = true;
            return result;
        }

        /// <summary>Keeps the page size inside the range the design allows.</summary>
        /// <param name="pageSize">Requested size.</param>
        /// <returns>Size within [MIN_PageSize, MAX_PageSize].</returns>
        private int ClampPageSize(int pageSize)
        {
            if (pageSize < MIN_PageSize) { return DEFAULT_PageSize; }
            if (pageSize > MAX_PageSize) { return MAX_PageSize; }
            return pageSize;
        }

        // ─────────────────────────────────────────────────────────────────────
        // §2  Base currency
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The tenant's reporting currency: AD_ClientInfo.C_AcctSchema1_ID ->
        /// C_AcctSchema.C_Currency_ID, with that currency's ISO code, display symbol and
        /// StdPrecision. Nothing here is assumed - a tenant reporting in USD gets a USD
        /// column and a USD total.
        ///
        /// No MRole clause: AD_ClientInfo is the session's OWN tenant configuration row, keyed
        /// by the client id the context carries, not user data that a role filters. The
        /// balances themselves are secured where they are read, in §3.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <returns>Populated <see cref="BaseCurrency"/>, or null when the tenant has no
        /// primary accounting schema.</returns>
        private BaseCurrency GetBaseCurrency(Ctx ctx)
        {
            string sql = @"
                SELECT acs.C_Currency_ID AS C_Currency_ID,
                       cur.ISO_Code AS Iso_Code,
                       cur.StdPrecision AS Std_Precision,
                       CASE WHEN cur.CurSymbol IS NOT NULL THEN cur.CurSymbol ELSE cur.ISO_Code END AS Currency_Symbol
                FROM AD_ClientInfo ci
                INNER JOIN C_AcctSchema acs ON (acs.C_AcctSchema_ID=ci.C_AcctSchema1_ID)
                INNER JOIN C_Currency cur ON (cur.C_Currency_ID=acs.C_Currency_ID)
                WHERE ci.AD_Client_ID=@AD_Client_ID";

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
            };

            DataSet ds = DB.ExecuteDataset(sql, parameters, null);
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) { return null; }

            DataRow row = ds.Tables[0].Rows[0];

            BaseCurrency currency = new BaseCurrency();
            currency.C_Currency_ID = Util.GetValueOfInt(row["C_Currency_ID"]);
            currency.IsoCode = Util.GetValueOfString(row["Iso_Code"]);
            currency.Symbol = Util.GetValueOfString(row["Currency_Symbol"]);
            currency.Precision = Util.GetValueOfInt(row["Std_Precision"]);

            if (currency.C_Currency_ID <= 0) { return null; }
            return currency;
        }

        // ─────────────────────────────────────────────────────────────────────
        // §3  The latest balance line per bank account
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// One line per bank account - the latest C_BankAccountLine dated on or before the
        /// as-of date - carrying its native EndingBalance and the ACCOUNT's currency.
        ///
        /// The read is ordered account, date, id ASCENDING and the forward pass simply
        /// overwrites, so the entry left standing for an account is its newest line. That is
        /// the portable equivalent of ROW_NUMBER() OVER (PARTITION BY ... ORDER BY
        /// StatementDate DESC, C_BankAccountLine_ID DESC) = 1, without putting an ORDER BY
        /// inside the SELECT list where MRole's parser would have to read it.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="asOf">The as-of date; bounds StatementDate exclusively at asOf + 1
        /// day, which keeps a future-dated line from being treated as the current balance
        /// while still admitting one stamped today.</param>
        /// <returns>The latest line per accessible bank account (never null).</returns>
        private List<LatestLine> GetLatestLinePerAccount(Ctx ctx, DateTime asOf)
        {
            List<LatestLine> lines = new List<LatestLine>();

            /* C_BankAccount and C_Currency are display/grouping lookups; C_BankAccountLine bal
               is the physical table the balances come from. Both joins are INNER and safe: a
               balance line always has an account, and an account always has a currency. The
               closing ON is a plain equality so the access parser has nothing to trip on. */
            string sql = @"
                SELECT bal.C_BankAccountLine_ID AS C_BankAccountLine_ID,
                       bal.C_BankAccount_ID AS C_BankAccount_ID,
                       bal.StatementDate AS Statement_Date,
                       bal.EndingBalance AS Ending_Balance,
                       ba.C_Currency_ID AS C_Currency_ID,
                       cur.ISO_Code AS Iso_Code,
                       cur.StdPrecision AS Std_Precision,
                       CASE WHEN cur.CurSymbol IS NOT NULL THEN cur.CurSymbol ELSE cur.ISO_Code END AS Currency_Symbol
                FROM C_BankAccountLine bal
                INNER JOIN C_BankAccount ba ON (ba.C_BankAccount_ID=bal.C_BankAccount_ID)
                INNER JOIN C_Currency cur ON (cur.C_Currency_ID=ba.C_Currency_ID)
                WHERE bal.IsActive='Y'
                  AND ba.IsActive='Y'
                  AND cur.IsActive='Y'
                  AND bal.AD_Client_ID=@AD_Client_ID
                  AND bal.StatementDate<@As_Of_Exclusive";

            /* MRole supplies the organisation access clause, so no AD_Org_ID predicate is
               written by hand - the explicit tenant filter is a second, independent guard
               rather than the only one. */
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "bal", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            /* ORDER BY goes on AFTER the access SQL. Oldest first WITHIN each account, so the
               forward pass below ends holding the newest line per account. */
            sql += " ORDER BY bal.C_BankAccount_ID,bal.StatementDate,bal.C_BankAccountLine_ID";

            /* Exclusive upper bound - asOf + 1 day. A line stamped on the as-of date at
               14:30 is still "as of today", and no time-of-day comparison is ever needed. */
            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@As_Of_Exclusive", asOf.Date.AddDays(1))
            };

            DataSet ds = DB.ExecuteDataset(sql, parameters, null);
            if (ds == null || ds.Tables.Count == 0) { return lines; }

            Dictionary<int, LatestLine> byAccount = new Dictionary<int, LatestLine>();

            DataTable dt = ds.Tables[0];
            for (int i = 0; i < dt.Rows.Count; i++)
            {
                DataRow row = dt.Rows[i];

                LatestLine line = new LatestLine();
                line.C_BankAccountLine_ID = Util.GetValueOfInt(row["C_BankAccountLine_ID"]);
                line.C_BankAccount_ID = Util.GetValueOfInt(row["C_BankAccount_ID"]);
                line.C_Currency_ID = Util.GetValueOfInt(row["C_Currency_ID"]);
                line.IsoCode = Util.GetValueOfString(row["Iso_Code"]);
                line.Symbol = Util.GetValueOfString(row["Currency_Symbol"]);
                line.Precision = Util.GetValueOfInt(row["Std_Precision"]);
                line.NativeBalance = Util.GetValueOfDecimal(row["Ending_Balance"]);

                DateTime? statementDate = Util.GetValueOfDateTime(row["Statement_Date"]);
                line.StatementDate = statementDate.HasValue ? statementDate.Value.Date : asOf;

                /* Ordered oldest-first per account, so the last write wins and that write is
                   the account's newest line. */
                byAccount[line.C_BankAccount_ID] = line;
            }

            foreach (KeyValuePair<int, LatestLine> entry in byAccount)
            {
                lines.Add(entry.Value);
            }

            return lines;
        }

        // ─────────────────────────────────────────────────────────────────────
        // §4  Conversion into the base currency
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Fills each line's base-currency amount.
        ///
        /// A line already IN the base currency is not converted at all - its own EndingBalance
        /// is the base amount, exactly, with no rate lookup to fail. Every other line goes
        /// through the currencyConvert(...) database function ONCE, dated on that line's own
        /// StatementDate, in one set-based query per batch of line ids. There is no query per
        /// account and no conversion of the history rows the forward pass already discarded.
        ///
        /// A NULL result means no rate covers that date: the line is flagged rather than
        /// counted as zero, and its amount stays out of every sum.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="baseCurrency">The resolved reporting currency.</param>
        /// <param name="lines">Latest lines, completed in place.</param>
        private void ApplyBaseAmounts(Ctx ctx, BaseCurrency baseCurrency, List<LatestLine> lines)
        {
            Dictionary<int, LatestLine> byLine = new Dictionary<int, LatestLine>();
            List<int> toConvert = new List<int>();

            for (int i = 0; i < lines.Count; i++)
            {
                LatestLine line = lines[i];

                if (line.C_Currency_ID == baseCurrency.C_Currency_ID)
                {
                    /* Same currency - converting would be a rate lookup that can only ever
                       return the amount it was given, and a missing rate would fail a
                       conversion the card does not need. */
                    line.BaseBalance = line.NativeBalance;
                    line.BaseAvailable = true;
                    continue;
                }

                byLine[line.C_BankAccountLine_ID] = line;
                toConvert.Add(line.C_BankAccountLine_ID);
            }

            if (toConvert.Count == 0) { return; }

            /* The conversion type is resolved from configuration, never hard-coded. When the
               tenant has no default the argument is SQL NULL and the database function applies
               its own - which is what every other caller in the application does. */
            int conversionTypeId = MConversionType.GetDefault(ctx.GetAD_Client_ID());
            string conversionTypeSql = conversionTypeId > 0 ? conversionTypeId.ToString() : "NULL";

            for (int start = 0; start < toConvert.Count; start += CONVERT_BatchSize)
            {
                ReadBaseAmounts(ctx, baseCurrency, conversionTypeSql, byLine,
                    IdList(toConvert, start, CONVERT_BatchSize));
            }
        }

        /// <summary>
        /// Runs one conversion batch and writes each result back onto its line.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="baseCurrency">The resolved reporting currency.</param>
        /// <param name="conversionTypeSql">Server-resolved conversion type id, or "NULL".</param>
        /// <param name="byLine">C_BankAccountLine_ID -> line, for writing results back.</param>
        /// <param name="idList">Comma-separated line ids - server-derived integers only.</param>
        private void ReadBaseAmounts(Ctx ctx, BaseCurrency baseCurrency, string conversionTypeSql,
            Dictionary<int, LatestLine> byLine, string idList)
        {
            if (idList.Length == 0) { return; }

            /* Every argument of currencyConvert is either a column or a server-resolved
               INTEGER composed into the text. Nothing here is bound and nothing here came from
               the browser: the provider binds placeholders POSITIONALLY, and a placeholder
               inside a function call in the SELECT list is the easiest way to mis-order that
               binding - the sibling cards compose these ids for the same reason. */
            StringBuilder sql = new StringBuilder();
            sql.Append(@"
                SELECT bal.C_BankAccountLine_ID AS C_BankAccountLine_ID,
                       currencyConvert(bal.EndingBalance,ba.C_Currency_ID,")
               .Append(baseCurrency.C_Currency_ID)
               .Append(",bal.StatementDate,")
               .Append(conversionTypeSql)
               .Append(@",bal.AD_Client_ID,bal.AD_Org_ID) AS Base_Amt
                FROM C_BankAccountLine bal
                INNER JOIN C_BankAccount ba ON (ba.C_BankAccount_ID=bal.C_BankAccount_ID)
                WHERE bal.IsActive='Y'
                  AND bal.AD_Client_ID=@AD_Client_ID
                  AND bal.C_BankAccountLine_ID IN (").Append(idList).Append(")");

            /* Secured on the same physical table as the first read, so a line the role cannot
               see cannot re-enter the result through this query either. */
            string finalSql = MRole.GetDefault(ctx).AddAccessSQL(sql.ToString(), "bal",
                MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
            };

            DataSet ds = DB.ExecuteDataset(finalSql, parameters, null);
            if (ds == null || ds.Tables.Count == 0) { return; }

            DataTable dt = ds.Tables[0];
            for (int i = 0; i < dt.Rows.Count; i++)
            {
                DataRow row = dt.Rows[i];

                int lineId = Util.GetValueOfInt(row["C_BankAccountLine_ID"]);
                if (!byLine.ContainsKey(lineId)) { continue; }

                LatestLine line = byLine[lineId];

                /* NULL means no rate covers this line's StatementDate. It is NOT zero: a zero
                   would silently understate the currency and the grand total, so the line is
                   flagged and its amount is left out of both. */
                object value = row["Base_Amt"];
                if (value == null || value == DBNull.Value)
                {
                    line.BaseAvailable = false;
                    continue;
                }

                line.BaseBalance = Util.GetValueOfDecimal(value);
                line.BaseAvailable = true;
            }
        }

        /// <summary>
        /// Renders one batch of line ids as a comma-separated list. The values are integers
        /// this model read from the database itself - no browser input reaches this string.
        /// </summary>
        /// <param name="ids">All ids awaiting conversion.</param>
        /// <param name="start">Index of the first id in this batch.</param>
        /// <param name="count">Maximum ids in this batch.</param>
        /// <returns>Comma-separated ids; empty when the range is empty.</returns>
        private string IdList(List<int> ids, int start, int count)
        {
            StringBuilder list = new StringBuilder();

            for (int i = start; i < ids.Count && i < start + count; i++)
            {
                if (list.Length > 0) { list.Append(","); }
                list.Append(ids[i]);
            }

            return list.ToString();
        }

        // ─────────────────────────────────────────────────────────────────────
        // §5  Grouping and totals
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Collapses the per-account lines into one row per currency.
        ///
        /// Native amounts are summed WITHIN a currency only - that is the one place adding
        /// them is meaningful. A line whose rate was missing contributes its native amount to
        /// its own currency and nothing at all to the base column, so the base figure is
        /// always the sum of amounts that were actually convertible.
        /// </summary>
        /// <param name="lines">Latest line per account, with base amounts applied.</param>
        /// <param name="baseCurrency">The resolved reporting currency.</param>
        /// <returns>One row per currency (never null).</returns>
        private List<CurrencyRow> GroupByCurrency(List<LatestLine> lines, BaseCurrency baseCurrency)
        {
            List<CurrencyRow> rows = new List<CurrencyRow>();
            Dictionary<int, CurrencyRow> byCurrency = new Dictionary<int, CurrencyRow>();

            for (int i = 0; i < lines.Count; i++)
            {
                LatestLine line = lines[i];

                CurrencyRow row;
                if (byCurrency.ContainsKey(line.C_Currency_ID))
                {
                    row = byCurrency[line.C_Currency_ID];
                }
                else
                {
                    row = new CurrencyRow();
                    row.C_Currency_ID = line.C_Currency_ID;
                    row.IsoCode = line.IsoCode;
                    row.Symbol = String.IsNullOrEmpty(line.Symbol) ? line.IsoCode : line.Symbol;
                    row.Precision = line.Precision;
                    row.IsBaseCurrency = line.C_Currency_ID == baseCurrency.C_Currency_ID;

                    byCurrency.Add(line.C_Currency_ID, row);
                    rows.Add(row);
                }

                row.NativeBalance += line.NativeBalance;
                row.BankAccountCount++;

                if (line.BaseAvailable) { row.BaseBalance += line.BaseBalance; }
                else { row.MissingRateCount++; }
            }

            /* A currency is only worth reporting in the base column when every one of its
               accounts converted. One missing rate makes the currency's base figure a partial
               sum, and a partial sum presented as a balance is worse than no figure. */
            for (int i = 0; i < rows.Count; i++)
            {
                rows[i].BaseAvailable = rows[i].MissingRateCount == 0;
            }

            return rows;
        }

        /// <summary>
        /// Orders the currencies: the base currency first - it is the yardstick the other rows
        /// are measured against - then by base-currency size, largest holding first, with the
        /// ISO code breaking a tie so two equal rows cannot reshuffle between page requests.
        /// </summary>
        /// <param name="rows">Rows to order in place.</param>
        private void SortRows(List<CurrencyRow> rows)
        {
            rows.Sort(delegate (CurrencyRow a, CurrencyRow b)
            {
                if (a.IsBaseCurrency != b.IsBaseCurrency) { return a.IsBaseCurrency ? -1 : 1; }

                int cmp = Math.Abs(b.BaseBalance).CompareTo(Math.Abs(a.BaseBalance));
                if (cmp != 0) { return cmp; }

                return String.Compare(a.IsoCode, b.IsoCode, StringComparison.OrdinalIgnoreCase);
            });
        }

        /// <summary>
        /// Computes the grand total and the conversion state behind it.
        ///
        /// The total is the sum of the BASE column and nothing else - native amounts in
        /// different currencies are never added together, which is exactly why the Total row's
        /// native cell stays blank on the card. It is withheld entirely when any line failed to
        /// convert: a total that silently omits a currency reads as complete and is not.
        /// </summary>
        /// <param name="result">Result being filled.</param>
        /// <param name="rows">Every currency row, before paging.</param>
        private void ApplyTotals(CurrencyBalanceResult result, List<CurrencyRow> rows)
        {
            decimal total = 0m;
            int missing = 0;

            for (int i = 0; i < rows.Count; i++)
            {
                total += rows[i].BaseBalance;
                missing += rows[i].MissingRateCount;
            }

            result.MissingRateCount = missing;
            result.HasConversionError = missing > 0;
            result.TotalBaseBalance = total;
            result.TotalAvailable = missing == 0;
        }

        // ─────────────────────────────────────────────────────────────────────
        // §6  Transfer objects
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>One bank account's latest balance line. Internal - the client only ever
        /// sees the grouped currency rows.</summary>
        private class LatestLine
        {
            /// <summary>C_BankAccountLine.C_BankAccountLine_ID.</summary>
            public int C_BankAccountLine_ID { get; set; }

            /// <summary>Owning C_BankAccount_ID - what "latest" is resolved per.</summary>
            public int C_BankAccount_ID { get; set; }

            /// <summary>The line's StatementDate - also the conversion date.</summary>
            public DateTime StatementDate { get; set; }

            /// <summary>C_BankAccountLine.EndingBalance, in the account's own currency.</summary>
            public decimal NativeBalance { get; set; }

            /// <summary>C_BankAccount.C_Currency_ID - the account's currency.</summary>
            public int C_Currency_ID { get; set; }

            /// <summary>That currency's ISO code.</summary>
            public string IsoCode { get; set; }

            /// <summary>That currency's display symbol, falling back to its ISO code.</summary>
            public string Symbol { get; set; }

            /// <summary>That currency's C_Currency.StdPrecision.</summary>
            public int Precision { get; set; }

            /// <summary>The balance in the base currency; meaningless unless BaseAvailable.</summary>
            public decimal BaseBalance { get; set; }

            /// <summary>False when no rate covered this line's StatementDate.</summary>
            public bool BaseAvailable { get; set; }
        }

        /// <summary>The tenant's reporting currency, from the primary accounting schema.</summary>
        public class BaseCurrency
        {
            /// <summary>C_AcctSchema.C_Currency_ID of AD_ClientInfo.C_AcctSchema1_ID.</summary>
            public int C_Currency_ID { get; set; }

            /// <summary>Its ISO code - what the base column's header names.</summary>
            public string IsoCode { get; set; }

            /// <summary>Its display symbol, falling back to the ISO code.</summary>
            public string Symbol { get; set; }

            /// <summary>Its C_Currency.StdPrecision.</summary>
            public int Precision { get; set; }
        }

        /// <summary>One page of the widget, plus what the page cannot know by itself.</summary>
        public class CurrencyBalanceResult
        {
            /// <summary>The requested page of currency rows, base currency first.</summary>
            public List<CurrencyRow> Rows { get; set; }

            /// <summary>The currency the base column and the total are stated in.</summary>
            public BaseCurrency BaseCurrency { get; set; }

            /// <summary>The date every balance was taken as of, as yyyy-MM-dd.</summary>
            public string AsOfDate { get; set; }

            /// <summary>1-based page number actually served, after clamping.</summary>
            public int Page { get; set; }

            /// <summary>Rows per page actually used, after clamping.</summary>
            public int PageSize { get; set; }

            /// <summary>Currencies in total, across every page.</summary>
            public int TotalRows { get; set; }

            /// <summary>CEILING(TotalRows / PageSize).</summary>
            public int TotalPages { get; set; }

            /// <summary>Sum of the base column across ALL currencies - never a sum of native
            /// amounts. Only meaningful when TotalAvailable is true.</summary>
            public decimal TotalBaseBalance { get; set; }

            /// <summary>False when at least one balance could not be converted, so the grand
            /// total would be a partial figure presented as a complete one.</summary>
            public bool TotalAvailable { get; set; }

            /// <summary>True when any account's balance had no exchange rate.</summary>
            public bool HasConversionError { get; set; }

            /// <summary>How many accounts could not be converted - never treated as zeros.</summary>
            public int MissingRateCount { get; set; }

            /// <summary>True when the tenant has no primary accounting schema, so there is no
            /// base currency to report in. A configuration state, not an error.</summary>
            public bool NoAcctSchema { get; set; }

            /// <summary>False only on a failure or a missing accounting schema; a tenant with
            /// no balance lines is Loaded=true with an empty page.</summary>
            public bool Loaded { get; set; }
        }

        /// <summary>One currency's holding. Native is in THAT currency; Base is in the
        /// tenant's reporting currency.</summary>
        public class CurrencyRow
        {
            /// <summary>C_BankAccount.C_Currency_ID the accounts were grouped by.</summary>
            public int C_Currency_ID { get; set; }

            /// <summary>The currency's ISO code - the row's label.</summary>
            public string IsoCode { get; set; }

            /// <summary>Its display symbol, falling back to the ISO code.</summary>
            public string Symbol { get; set; }

            /// <summary>Its C_Currency.StdPrecision - the Native cell's decimals.</summary>
            public int Precision { get; set; }

            /// <summary>SUM of the latest EndingBalance of every account in this currency,
            /// stated in this currency and never converted.</summary>
            public decimal NativeBalance { get; set; }

            /// <summary>The same holding in the base currency; meaningless unless
            /// BaseAvailable.</summary>
            public decimal BaseBalance { get; set; }

            /// <summary>Bank accounts behind the two figures.</summary>
            public int BankAccountCount { get; set; }

            /// <summary>Accounts in this currency with no usable exchange rate.</summary>
            public int MissingRateCount { get; set; }

            /// <summary>False when at least one account here could not be converted, so the
            /// base figure would be a partial sum.</summary>
            public bool BaseAvailable { get; set; }

            /// <summary>True for the tenant's own reporting currency - never converted, and
            /// listed first.</summary>
            public bool IsBaseCurrency { get; set; }
        }
    }
}
