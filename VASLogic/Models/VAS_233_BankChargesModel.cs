/******************************************************
 * Module Name    : VASLogic
 * Purpose        : Bank Charges dashboard widget data
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
    /// Module Name : VAS_233_BankCharges
    /// Purpose     : Backs the VAS_233_BankChargesWidget dashboard widget - what the bank
    ///               cost the tenant in ONE accounting period, from the two places
    ///               Onfinity actually records a bank charge:
    ///
    ///                 Source 1  C_Payment with a NON-advance C_Charge_ID
    ///                           ("expense posted straight through the payment screen
    ///                           without a purchase invoice"). SUM(PayAmt).
    ///                 Source 2  C_BankStatementLine.ChargeAmt - the fee the bank
    ///                           deducted on the statement itself, on a completed or
    ///                           closed statement.
    ///
    ///               The two never overlap: a statement line's ChargeAmt is the bank's
    ///               own fee on that line, not the payment amount the line reconciles,
    ///               so summing both cannot double-count a charge.
    ///
    ///               A third source - GL journal lines posted to the "Bank Charges"
    ///               natural account - is deliberately NOT read. It needs the
    ///               environment's Bank Charges C_ElementValue_ID, and this product has
    ///               no standard column that holds it (AD_ClientInfo does not carry one).
    ///               Guessing an account, or hard-coding one, would silently report the
    ///               wrong number on every other tenant. When the account gets a
    ///               configured home, add a third Read* method here on the same shape as
    ///               the two below - nothing else has to change.
    ///
    ///               Signs: PayAmt on a payment is always a positive magnitude.
    ///               ChargeAmt on a statement line is stored as the STATEMENT-side
    ///               difference and is therefore normally negative for a fee, so it is
    ///               read through ABS(...): this widget reports the SIZE of the cost, and
    ///               a fee must add to the total rather than cancel one recorded the
    ///               other way round.
    ///
    ///               Period list: EVERY active period of the CURRENT fiscal year on the
    ///               tenant's primary calendar (AD_ClientInfo.C_Calendar_ID), newest
    ///               first, with periods that have not started yet left out - a period
    ///               whose StartDate is in the future can only ever read zero. The
    ///               current fiscal year is the year of the period containing today,
    ///               falling back to the year of the most recently started period; it is
    ///               NOT the calendar year, because a fiscal year need not start in
    ///               January. Identical to VAS_231_NetMovementModel, deliberately: the
    ///               two cards sit on the same Banking dashboard and must offer the same
    ///               periods.
    ///
    ///               Delta: against the period immediately BEFORE the selected one on the
    ///               same calendar - which may belong to the previous fiscal year, so it
    ///               is resolved by StartDate rather than by PeriodNo. Both periods are
    ///               summed in ONE pass per source (a CASE per date window over one
    ///               widened range), so the whole card costs two reads, not four.
    ///
    ///               Amounts: every amount is stated in its OWN record's currency, so
    ///               every SUM goes through the currencyConvert(...) DB function into the
    ///               tenant's primary accounting-schema currency
    ///               (AD_ClientInfo.C_AcctSchema1_ID -> C_AcctSchema.C_Currency_ID),
    ///               dated on the record's own DateAcct. The client formats the result
    ///               with VIS.Util.formatCompactAmount against the ISO code and precision
    ///               this model returns - no currency is ever hard-coded.
    ///
    ///               Only settled money counts: DocStatus IN ('CO','CL'). A drafted or
    ///               voided payment or statement has cost nothing yet.
    ///
    ///               MRole row-level security is applied to the main physical table of
    ///               every user-facing query - C_Period alias p for the period reads,
    ///               C_Payment alias p and C_BankStatementLine alias bsl for the figures -
    ///               and never to a joined reference table (C_Year, AD_ClientInfo,
    ///               C_Charge, C_BankStatement) or to a combined statement. ORDER BY is
    ///               appended AFTER AddAccessSQL so its FROM-clause parser never meets a
    ///               trailing clause, and every join ON is a plain equality so the parser
    ///               never meets a function call either. Compatible with PostgreSQL and
    ///               Oracle.
    /// Chronological development:
    ///   VAI154      2026-09-03 Created
    /// </summary>
    public class VAS_233_BankChargesModel
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_233_BankChargesModel).FullName);

        /* DocStatus codes that mean the document actually settled. Stored codes -
           compared bare, never with an N prefix. */
        private const string DOCSTATUS_Completed = "CO";
        private const string DOCSTATUS_Closed = "CL";

        /* C_Charge.IsAdvanceCharge stored code. An advance charge is a prepayment, not a
           cost that has been incurred, so it is excluded. */
        private const string ISADVANCECHARGE_Yes = "Y";

        // ─────────────────────────────────────────────────────────────────────
        // §1  Bootstrap
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Bootstraps the widget in one round trip: the base currency, the current
        /// fiscal year's periods, the period to preselect and that period's figures.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <returns>Populated <see cref="BankChargesBootstrap"/> (never null). Data.Loaded
        /// is false only when there is no context or no usable period - a tenant with no
        /// charges in the period returns Loaded=true and zeros, because "the bank cost you
        /// nothing" is a real answer rather than an error.</returns>
        public BankChargesBootstrap GetBootstrap(Ctx ctx)
        {
            BankChargesBootstrap result = new BankChargesBootstrap();
            result.Periods = new List<PeriodItem>();
            result.Currency = new BaseCurrency();
            result.Data = new BankChargesData();

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
               meet a trailing clause. Newest period first. */
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
                Log.Log(Level.WARNING, "VAS_233_BankCharges: no started period on the primary calendar for AD_Client_ID="
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

        /// <summary>
        /// The period immediately BEFORE the given one on the tenant's primary calendar -
        /// the comparison base for the delta.
        ///
        /// Resolved by StartDate, not by PeriodNo: the period before January of a fiscal
        /// year is December of the PREVIOUS year, where PeriodNo restarts at 1. Unlike the
        /// selectable list this is not restricted to the current fiscal year, for exactly
        /// that reason.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="period">The selected period whose predecessor is wanted.</param>
        /// <returns>Populated <see cref="PeriodItem"/>, or null when the selected period is
        /// the first the calendar defines.</returns>
        private PeriodItem GetPreviousPeriod(Ctx ctx, PeriodItem period)
        {
            if (ctx == null || period == null || !period.StartDate.HasValue) { return null; }

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
                  AND p.StartDate<@Selected_Start";

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "p", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            /* Newest of the earlier periods = the immediate predecessor. */
            sql += " ORDER BY p.StartDate DESC,p.EndDate DESC,p.C_Period_ID DESC";

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@AD_Client_ID_Period", ctx.GetAD_Client_ID()),
                new SqlParameter("@Selected_Start", period.StartDate.Value.Date)
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
        /// Total bank charges for ONE period, in the tenant's base currency, with the
        /// entry count behind the figure and the movement against the preceding period.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="periodId">C_Period_ID selected by the user.</param>
        /// <returns>Populated <see cref="BankChargesData"/> (never null). Loaded is false
        /// only when the period no longer qualifies or the tenant has no accounting
        /// schema; a period with no charges returns Loaded=true and zeros.</returns>
        public BankChargesData GetPeriodData(Ctx ctx, int periodId)
        {
            BankChargesData result = new BankChargesData();
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
                Log.Log(Level.WARNING, "VAS_233_BankCharges: AD_ClientInfo.C_AcctSchema1_ID not configured for AD_Client_ID="
                    + ctx.GetAD_Client_ID());
                return result;
            }

            /* The predecessor may be missing (the calendar's very first period) or may
               belong to the previous fiscal year - both are handled by the window helper
               below, which collapses "no predecessor" into an empty date range that can
               only sum to zero. */
            PeriodItem prior = GetPreviousPeriod(ctx, period);
            result.PriorPeriodName = prior != null ? prior.Name : "";

            ChargeWindow window = new ChargeWindow(period, prior);

            SourceTotals payments = ReadChargePayments(ctx, window, currency.C_Currency_ID);
            SourceTotals statements = ReadStatementCharges(ctx, window, currency.C_Currency_ID);

            result.PaymentChargesAmt = payments.CurrentAmt;
            result.PaymentChargeCount = payments.CurrentCount;
            result.StatementChargesAmt = statements.CurrentAmt;
            result.StatementChargeCount = statements.CurrentCount;

            result.ChargesAmt = payments.CurrentAmt + statements.CurrentAmt;
            result.ChargeCount = payments.CurrentCount + statements.CurrentCount;

            decimal priorAmt = payments.PriorAmt + statements.PriorAmt;
            result.PriorChargesAmt = priorAmt;

            /* A delta against zero is not a percentage - "up from nothing" is infinite, not
               a number. The client hides the badge when HasDelta is false rather than
               printing a misleading figure. */
            if (window.HasPrior && priorAmt != 0)
            {
                result.HasDelta = true;
                result.DeltaPct = (result.ChargesAmt - priorAmt) / Math.Abs(priorAmt) * 100m;
            }

            result.Loaded = true;
            return result;
        }

        /// <summary>
        /// Source 1 - charges posted through the payment screen: C_Payment rows carrying a
        /// C_Charge_ID that is NOT an advance charge.
        ///
        /// PayAmt is stated in the payment's OWN currency, so it is converted with
        /// currencyConvert(...) into the accounting-schema currency, dated on the payment's
        /// DateAcct and using its own conversion type - the same call the standard reports
        /// use, so the widget cannot disagree with them.
        ///
        /// The selected period and its predecessor are summed in ONE pass: the WHERE spans
        /// the widened range covering both, and a CASE per date window splits the rows
        /// between them.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="window">Validated current / prior date windows.</param>
        /// <param name="acctCurrencyId">Primary accounting-schema C_Currency_ID.</param>
        /// <returns>Current and prior totals for this source (never null).</returns>
        private SourceTotals ReadChargePayments(Ctx ctx, ChargeWindow window, int acctCurrencyId)
        {
            /* The target currency is a server-resolved id, never client input, so it is
               inlined rather than bound: the conversion call appears TWICE and the
               provider binds by POSITION, which would need two separately named binds
               for one and the same value. */
            string convert = "currencyConvert(p.PayAmt,p.C_Currency_ID," + acctCurrencyId
                + ",p.DateAcct,p.C_ConversionType_ID,p.AD_Client_ID,p.AD_Org_ID)";

            StringBuilder sql = new StringBuilder();
            sql.Append(@"
                SELECT COALESCE(SUM(CASE WHEN p.DateAcct>=@Cur_From_Amt AND p.DateAcct<@Cur_To_Amt THEN ").Append(convert).Append(@" ELSE 0 END),0) AS Current_Amt,
                       COALESCE(SUM(CASE WHEN p.DateAcct>=@Cur_From_Cnt AND p.DateAcct<@Cur_To_Cnt THEN 1 ELSE 0 END),0) AS Current_Cnt,
                       COALESCE(SUM(CASE WHEN p.DateAcct>=@Prv_From_Amt AND p.DateAcct<@Prv_To_Amt THEN ").Append(convert).Append(@" ELSE 0 END),0) AS Prior_Amt
                FROM C_Payment p
                INNER JOIN C_Charge c ON (c.C_Charge_ID=p.C_Charge_ID)
                WHERE p.IsActive='Y'
                  AND c.IsActive='Y'
                  AND p.AD_Client_ID=@AD_Client_ID
                  AND p.DocStatus IN ('").Append(DOCSTATUS_Completed).Append("','").Append(DOCSTATUS_Closed).Append(@"')
                  AND p.C_Charge_ID IS NOT NULL
                  AND COALESCE(c.IsAdvanceCharge,'N')<>'").Append(ISADVANCECHARGE_Yes).Append(@"'
                  AND p.DateAcct>=@Range_From
                  AND p.DateAcct<@Range_To");

            /* C_Payment p is the main physical table; C_Charge c is a reference lookup and
               takes no access clause of its own. MRole supplies the organisation access,
               so no AD_Org_ID predicate is written by hand - the explicit tenant filter is
               a second, independent guard rather than the only one. Flat SUM(CASE ...)
               aggregation, never nested selects, so the access parser has one simple FROM
               clause to read. */
            string finalSql = MRole.GetDefault(ctx).AddAccessSQL(sql.ToString(), "p",
                MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            return ReadTotals(finalSql, window, ctx.GetAD_Client_ID());
        }

        /// <summary>
        /// Source 2 - the fee the bank deducted on the statement itself:
        /// C_BankStatementLine.ChargeAmt on a completed or closed statement.
        ///
        /// ChargeAmt is stored as the statement-side difference and is normally NEGATIVE
        /// for a fee, so it is read through ABS(...): the card reports the size of the
        /// cost, and a fee must add to the total rather than cancel one recorded with the
        /// opposite sign.
        ///
        /// The line's own C_Currency_ID and DateAcct drive the conversion. A statement
        /// line carries no C_ConversionType_ID, so NULL is passed and currencyConvert falls
        /// back to the default (spot) rate type.
        ///
        /// Period membership is taken from bsl.DateAcct, not from bs.StatementDate: an
        /// accounting period is defined by the accounting date, and reading the header date
        /// instead would put a line in a different period from the payment it reconciles.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="window">Validated current / prior date windows.</param>
        /// <param name="acctCurrencyId">Primary accounting-schema C_Currency_ID.</param>
        /// <returns>Current and prior totals for this source (never null).</returns>
        private SourceTotals ReadStatementCharges(Ctx ctx, ChargeWindow window, int acctCurrencyId)
        {
            string convert = "currencyConvert(ABS(bsl.ChargeAmt),bsl.C_Currency_ID," + acctCurrencyId
                + ",bsl.DateAcct,NULL,bsl.AD_Client_ID,bsl.AD_Org_ID)";

            StringBuilder sql = new StringBuilder();
            sql.Append(@"
                SELECT COALESCE(SUM(CASE WHEN bsl.DateAcct>=@Cur_From_Amt AND bsl.DateAcct<@Cur_To_Amt THEN ").Append(convert).Append(@" ELSE 0 END),0) AS Current_Amt,
                       COALESCE(SUM(CASE WHEN bsl.DateAcct>=@Cur_From_Cnt AND bsl.DateAcct<@Cur_To_Cnt THEN 1 ELSE 0 END),0) AS Current_Cnt,
                       COALESCE(SUM(CASE WHEN bsl.DateAcct>=@Prv_From_Amt AND bsl.DateAcct<@Prv_To_Amt THEN ").Append(convert).Append(@" ELSE 0 END),0) AS Prior_Amt
                FROM C_BankStatementLine bsl
                INNER JOIN C_BankStatement bs ON (bs.C_BankStatement_ID=bsl.C_BankStatement_ID)
                WHERE bsl.IsActive='Y'
                  AND bs.IsActive='Y'
                  AND bsl.AD_Client_ID=@AD_Client_ID
                  AND bs.DocStatus IN ('").Append(DOCSTATUS_Completed).Append("','").Append(DOCSTATUS_Closed).Append(@"')
                  AND COALESCE(bsl.ChargeAmt,0)<>0
                  AND bsl.DateAcct>=@Range_From
                  AND bsl.DateAcct<@Range_To");

            /* C_BankStatementLine bsl is the main physical table the user is reading from;
               C_BankStatement bs is its header and takes no access clause of its own. */
            string finalSql = MRole.GetDefault(ctx).AddAccessSQL(sql.ToString(), "bsl",
                MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            return ReadTotals(finalSql, window, ctx.GetAD_Client_ID());
        }

        /// <summary>
        /// Executes one prepared source query and materialises its single result row.
        /// Both source queries expose the same three aliases and the same nine bind
        /// placeholders in the same order, so they share this executor.
        /// </summary>
        /// <param name="sql">Access-filtered SQL carrying the standard placeholders.</param>
        /// <param name="window">Date windows supplying the bind values.</param>
        /// <param name="clientId">Session AD_Client_ID.</param>
        /// <returns>Populated <see cref="SourceTotals"/>; zeros when the query returns
        /// nothing (never null).</returns>
        private SourceTotals ReadTotals(string sql, ChargeWindow window, int clientId)
        {
            SourceTotals totals = new SourceTotals();

            /* The provider binds POSITIONALLY, so every occurrence carries its own name and
               the array is built in the order the placeholders appear in the SQL text:
               three CASE windows first (current amount, current count, prior amount), then
               the WHERE tenant filter and the widened range. */
            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@Cur_From_Amt", window.CurrentFrom),
                new SqlParameter("@Cur_To_Amt", window.CurrentToExclusive),
                new SqlParameter("@Cur_From_Cnt", window.CurrentFrom),
                new SqlParameter("@Cur_To_Cnt", window.CurrentToExclusive),
                new SqlParameter("@Prv_From_Amt", window.PriorFrom),
                new SqlParameter("@Prv_To_Amt", window.PriorToExclusive),
                new SqlParameter("@AD_Client_ID", clientId),
                new SqlParameter("@Range_From", window.RangeFrom),
                new SqlParameter("@Range_To", window.RangeToExclusive)
            };

            DataSet ds = DB.ExecuteDataset(sql, parameters, null);
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) { return totals; }

            DataRow row = ds.Tables[0].Rows[0];
            totals.CurrentAmt = Util.GetValueOfDecimal(row["Current_Amt"]);
            totals.CurrentCount = Util.GetValueOfInt(row["Current_Cnt"]);
            totals.PriorAmt = Util.GetValueOfDecimal(row["Prior_Amt"]);

            return totals;
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
        // §5  Internal helpers
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The two date windows one source query covers - the selected period and its
        /// predecessor - plus the single widened range the WHERE clause scans.
        ///
        /// Upper bounds are half-open (EndDate + 1 day) so a document stamped on the last
        /// day of a period at 14:30 is still inside it. When there is no predecessor the
        /// prior window collapses onto the current period's start date, which is an empty
        /// range that can only sum to zero - no branch in the SQL and no NULL bind.
        /// </summary>
        private class ChargeWindow
        {
            /// <summary>Selected period's inclusive first day.</summary>
            public DateTime CurrentFrom { get; private set; }

            /// <summary>Selected period's exclusive upper bound (EndDate + 1 day).</summary>
            public DateTime CurrentToExclusive { get; private set; }

            /// <summary>Prior period's inclusive first day; equals PriorToExclusive when
            /// there is no prior period.</summary>
            public DateTime PriorFrom { get; private set; }

            /// <summary>Prior period's exclusive upper bound.</summary>
            public DateTime PriorToExclusive { get; private set; }

            /// <summary>Earliest day either window touches - the WHERE lower bound.</summary>
            public DateTime RangeFrom { get; private set; }

            /// <summary>Exclusive upper bound of both windows - the WHERE upper bound.</summary>
            public DateTime RangeToExclusive { get; private set; }

            /// <summary>False when the selected period is the first the calendar defines,
            /// in which case no delta can be reported.</summary>
            public bool HasPrior { get; private set; }

            /// <summary>Builds both windows from a validated period and its predecessor.</summary>
            /// <param name="period">Selected period (StartDate / EndDate already checked).</param>
            /// <param name="prior">Preceding period, or null when there is none.</param>
            public ChargeWindow(PeriodItem period, PeriodItem prior)
            {
                CurrentFrom = period.StartDate.Value.Date;
                CurrentToExclusive = period.EndDate.Value.Date.AddDays(1);

                HasPrior = prior != null && prior.StartDate.HasValue && prior.EndDate.HasValue;

                if (HasPrior)
                {
                    PriorFrom = prior.StartDate.Value.Date;
                    PriorToExclusive = prior.EndDate.Value.Date.AddDays(1);
                }
                else
                {
                    /* Empty range - sums to zero without a special case in the SQL. */
                    PriorFrom = CurrentFrom;
                    PriorToExclusive = CurrentFrom;
                }

                RangeFrom = PriorFrom < CurrentFrom ? PriorFrom : CurrentFrom;
                RangeToExclusive = PriorToExclusive > CurrentToExclusive ? PriorToExclusive : CurrentToExclusive;
            }
        }

        /// <summary>One source's contribution: its current-period amount and count, and its
        /// prior-period amount.</summary>
        private class SourceTotals
        {
            /// <summary>Converted charge total inside the selected period.</summary>
            public decimal CurrentAmt { get; set; }

            /// <summary>Number of charge entries behind CurrentAmt.</summary>
            public int CurrentCount { get; set; }

            /// <summary>Converted charge total inside the preceding period.</summary>
            public decimal PriorAmt { get; set; }
        }

        // ─────────────────────────────────────────────────────────────────────
        // §6  Transfer objects
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>Everything the widget needs on first paint.</summary>
        public class BankChargesBootstrap
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
            public BankChargesData Data { get; set; }
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
        public class BankChargesData
        {
            /// <summary>Period the figures belong to.</summary>
            public int C_Period_ID { get; set; }

            /// <summary>Period display name.</summary>
            public string PeriodName { get; set; }

            /// <summary>Total bank charges - source 1 plus source 2, in base currency.</summary>
            public decimal ChargesAmt { get; set; }

            /// <summary>Source 1 alone: charges posted through the payment screen.</summary>
            public decimal PaymentChargesAmt { get; set; }

            /// <summary>Source 2 alone: fees deducted on a bank statement line.</summary>
            public decimal StatementChargesAmt { get; set; }

            /// <summary>Total number of charge entries behind ChargesAmt.</summary>
            public int ChargeCount { get; set; }

            /// <summary>Number of charge payments behind PaymentChargesAmt.</summary>
            public int PaymentChargeCount { get; set; }

            /// <summary>Number of statement lines behind StatementChargesAmt.</summary>
            public int StatementChargeCount { get; set; }

            /// <summary>Total charges of the preceding period - the delta's base.</summary>
            public decimal PriorChargesAmt { get; set; }

            /// <summary>Display name of the preceding period; empty when there is none.</summary>
            public string PriorPeriodName { get; set; }

            /// <summary>Movement against the preceding period, in percent. Only meaningful
            /// when HasDelta is true.</summary>
            public decimal DeltaPct { get; set; }

            /// <summary>False when there is no preceding period, or it had no charges at
            /// all - a percentage against zero is not a number.</summary>
            public bool HasDelta { get; set; }

            /// <summary>False only on a failure or a period that no longer qualifies -
            /// a period with no charges is Loaded=true with zeros.</summary>
            public bool Loaded { get; set; }
        }
    }
}
