/******************************************************
 * Module Name    : VASLogic
 * Purpose        : Schedules Due dashboard widget data
 * chronological  : Development
 * Created Date   : 2026-08-31
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
    /// Module Name : VAS_220_SchedulesDue
    /// Purpose     : Backs the VAS_220_SchedulesDueWidget dashboard widget
    ///               (Recurring module, 2x1 KPI + drill-down modal). Answers
    ///               "how many recurring setups are due to generate inside the
    ///               configured date window, and which ones are they?".
    ///
    ///               One request serves both the KPI and the modal: the row list is
    ///               fetched once and the two headline figures (total due, due
    ///               today) are derived from it in code. That keeps the card and the
    ///               list in lock-step and avoids a second round trip, and it also
    ///               removes the only backend-specific expression the spec needed -
    ///               "due today" no longer has to be written as CURRENT_DATE on
    ///               PostgreSQL and TRUNC(SYSDATE) on Oracle.
    ///
    ///               The date window is expressed half-open
    ///               (DateNextRun >= DateFrom AND DateNextRun < DateToExclusive)
    ///               rather than with BETWEEN over a truncated column: DateNextRun
    ///               can carry a time part on Oracle, and wrapping the column in
    ///               TRUNC / CAST would both hide those rows' real value and stop
    ///               the column being used as an index.
    ///
    ///               Amounts come from whichever source document the setup copies
    ///               (invoice / order / payment) and are reported UNCONVERTED, in
    ///               that document's own currency. Each row therefore carries its
    ///               currency ISO, symbol and standard precision, and the list
    ///               renders a currency column instead of implying one shared unit
    ///               across rows that may not share one.
    ///
    ///               MRole row-level security is applied to the main physical table
    ///               only (C_Recurring, alias r). The document, journal, project,
    ///               partner and currency joins are lookups that inherit that
    ///               filter. There is no CTE, so no CTE alias is passed to MRole.
    ///               ORDER BY is appended AFTER AddAccessSQL so the FROM-clause
    ///               parser is not confused by a trailing clause.
    ///
    ///               The tenant always comes from the authenticated context; the
    ///               client never supplies AD_Client_ID. Compatible with PostgreSQL
    ///               and Oracle.
    /// Chronological development:
    ///   VAI154      2026-08-31 Created
    /// </summary>
    public class VAS_220_SchedulesDueModel
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_220_SchedulesDueModel).FullName);

        /// <summary>Default look-ahead window, in days, matching the widget design.</summary>
        public const int DEFAULT_WINDOW_DAYS = 30;

        /// <summary>Smallest accepted look-ahead window (today only).</summary>
        public const int MIN_WINDOW_DAYS = 1;

        /// <summary>Largest accepted look-ahead window - guards the row volume of a
        /// widget that deliberately returns every matching row.</summary>
        public const int MAX_WINDOW_DAYS = 365;

        /* C_Recurring.RecurringType stored codes (list reference). Returned to the
           client raw; the client resolves each to a localized AD_Message label, so no
           display text is ever produced by the query or by this layer. */
        public const string RECURRINGTYPE_GLJournal = "B";
        public const string RECURRINGTYPE_GLJournalBatch = "G";
        public const string RECURRINGTYPE_Invoice = "I";
        public const string RECURRINGTYPE_Project = "J";
        public const string RECURRINGTYPE_Order = "O";
        public const string RECURRINGTYPE_Payment = "P";

        /// <summary>
        /// Returns the recurring setups due to generate inside the default 30-day
        /// window for the session tenant.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <returns>Populated <see cref="SchedulesDueInfo"/> (never null).</returns>
        public SchedulesDueInfo GetSchedulesDue(Ctx ctx)
        {
            return GetSchedulesDue(ctx, DEFAULT_WINDOW_DAYS);
        }

        /// <summary>
        /// Returns the recurring setups due to generate between today and
        /// today + <paramref name="windowDays"/> (both bounds inclusive as dates).
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="windowDays">Look-ahead window in days; clamped to
        /// [MIN_WINDOW_DAYS, MAX_WINDOW_DAYS].</param>
        /// <returns>Populated <see cref="SchedulesDueInfo"/> (never null). Loaded is
        /// false only when the context is missing or the query failed; a tenant with
        /// nothing due returns Loaded=true with zero counts and an empty row list -
        /// zero is a real answer, not an error state.</returns>
        public SchedulesDueInfo GetSchedulesDue(Ctx ctx, int windowDays)
        {
            SchedulesDueInfo result = new SchedulesDueInfo();
            result.Loaded = false;
            result.Rows = new List<ScheduleDueRow>();

            if (ctx == null) { return result; }

            /* The window is a display/filter choice, not a security boundary, but it
               still arrives from the client, so it is clamped rather than trusted. */
            if (windowDays < MIN_WINDOW_DAYS) { windowDays = MIN_WINDOW_DAYS; }
            if (windowDays > MAX_WINDOW_DAYS) { windowDays = MAX_WINDOW_DAYS; }

            DateTime today = DateTime.Now.Date;
            DateTime dateFrom = today;
            DateTime dateToInclusive = today.AddDays(windowDays);
            /* Half-open upper bound: the day after the last included day, at
               midnight. A row stamped 17 Jul 14:30 is still inside a window that
               ends on 17 Jul. */
            DateTime dateToExclusive = dateToInclusive.AddDays(1);

            result.WindowDays = windowDays;
            result.DateFrom = dateFrom.ToString("yyyy-MM-dd");
            result.DateTo = dateToInclusive.ToString("yyyy-MM-dd");

            int orgId = ctx.GetAD_Org_ID();

            /* The amount is reported in the source document's OWN currency - no
               conversion. Each row therefore carries its currency alongside the
               figure, and the list shows a currency column rather than implying one
               shared unit across rows that may not share one. */
            const string amountExpression = "COALESCE(inv.GrandTotal,ord.GrandTotal,pay.PayAmt,0)";

            StringBuilder sql = new StringBuilder();
            sql.Append(@"
                SELECT r.C_Recurring_ID AS C_Recurring_ID,
                       r.Name AS Recurring_Name,
                       r.DateNextRun AS Date_Next_Run,
                       r.RecurringType AS Recurring_Type,
                       r.FrequencyType AS Frequency_Type,
                       r.Frequency AS Frequency_Value,
                       r.RunsRemaining AS Runs_Remaining,
                       bp.Name AS BPartner_Name,
                       COALESCE(glj.Description,glb.Description) AS Journal_Description,
                       ").Append(amountExpression).Append(@" AS Amount_Document,
                       doccur.ISO_Code AS Amount_Currency_Iso,
                       doccur.CurSymbol AS Amount_Currency_Symbol,
                       doccur.StdPrecision AS Amount_Currency_Precision
                FROM C_Recurring r
                LEFT OUTER JOIN C_Invoice inv ON (inv.C_Invoice_ID=r.C_Invoice_ID)
                LEFT OUTER JOIN C_Order ord ON (ord.C_Order_ID=r.C_Order_ID)
                LEFT OUTER JOIN C_Payment pay ON (pay.C_Payment_ID=r.C_Payment_ID)
                LEFT OUTER JOIN C_Project prj ON (prj.C_Project_ID=r.C_Project_ID)
                LEFT OUTER JOIN GL_Journal glj ON (glj.GL_Journal_ID=r.GL_Journal_ID)
                LEFT OUTER JOIN GL_JournalBatch glb ON (glb.GL_JournalBatch_ID=r.GL_JournalBatch_ID)
                LEFT OUTER JOIN C_BPartner bp ON (bp.C_BPartner_ID=COALESCE(inv.C_BPartner_ID,ord.C_BPartner_ID,pay.C_BPartner_ID,prj.C_BPartner_ID))
                LEFT OUTER JOIN C_Currency doccur ON (doccur.C_Currency_ID=COALESCE(inv.C_Currency_ID,ord.C_Currency_ID,pay.C_Currency_ID))
                WHERE r.IsActive='Y'
                  AND r.AD_Client_ID IN (@AD_Client_ID)
                  AND COALESCE(r.RunsRemaining,0)>0
                  AND r.DateNextRun>=@DateFrom
                  AND r.DateNextRun<@DateToExclusive");

            /* Login org. 0 is the '*' organisation and is a legitimate login value
               meaning "every org this role can reach" - only then is the org
               predicate left to MRole. When a specific org is selected, narrow to
               that org plus the shared (AD_Org_ID=0) setups, which belong to every
               org by definition. Added before AddAccessSQL so the WHERE clause is
               complete when the parser sees it. */
            if (orgId > 0)
            {
                sql.Append(@"
                  AND r.AD_Org_ID IN (0,@AD_Org_ID)");
            }

            string finalSql = sql.ToString();

            /* MRole only on the main physical table (C_Recurring / alias r). It also
               supplies the r.AD_Client_ID / r.AD_Org_ID access predicates, so the
               explicit tenant filter above is a second, independent guard rather
               than the only one. */
            finalSql = MRole.GetDefault(ctx).AddAccessSQL(finalSql, "r", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            /* Soonest first, then by name so two setups falling on the same day keep
               a stable, reproducible order across refreshes and pages. Appended
               after AddAccessSQL by design. */
            finalSql += @"
                ORDER BY r.DateNextRun,r.Name";

            /* The provider binds positionally, so every parameter is added in the
               order its placeholder appears in the statement text: the WHERE clause
               binds, then the optional org bind. Each occurrence carries its own
               unique name. */
            List<SqlParameter> parameters = new List<SqlParameter>();
            parameters.Add(new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()));
            parameters.Add(new SqlParameter("@DateFrom", dateFrom));
            parameters.Add(new SqlParameter("@DateToExclusive", dateToExclusive));
            if (orgId > 0)
            {
                parameters.Add(new SqlParameter("@AD_Org_ID", orgId));
            }

            try
            {
                DataSet ds = DB.ExecuteDataset(finalSql, parameters.ToArray(), null);
                if (ds != null && ds.Tables.Count > 0)
                {
                    DataTable dt = ds.Tables[0];
                    for (int i = 0; i < dt.Rows.Count; i++)
                    {
                        DataRow dr = dt.Rows[i];

                        DateTime? nextRun = Util.GetValueOfDateTime(dr["Date_Next_Run"]);

                        /* Standard precision of the row's OWN currency, so a
                           zero-decimal currency is not printed with two. Defaults to
                           2 when the setup has no source document to take a currency
                           from. */
                        int precision = Util.GetValueOfInt(dr["Amount_Currency_Precision"]);
                        if (precision < 0 || precision > 6) { precision = 2; }

                        ScheduleDueRow row = new ScheduleDueRow();
                        row.C_Recurring_ID = Util.GetValueOfInt(dr["C_Recurring_ID"]);
                        row.RecurringName = Util.GetValueOfString(dr["Recurring_Name"]);
                        row.DateNextRun = nextRun.HasValue ? nextRun.Value.ToString("yyyy-MM-dd") : "";
                        row.IsDueToday = nextRun.HasValue && nextRun.Value.Date == today;
                        row.RecurringType = Util.GetValueOfString(dr["Recurring_Type"]);
                        row.FrequencyType = Util.GetValueOfString(dr["Frequency_Type"]);
                        row.Frequency = Util.GetValueOfInt(dr["Frequency_Value"]);
                        row.RunsRemaining = Util.GetValueOfInt(dr["Runs_Remaining"]);
                        row.BPartnerName = Util.GetValueOfString(dr["BPartner_Name"]);
                        row.JournalDescription = Util.GetValueOfString(dr["Journal_Description"]);
                        row.Amount = Util.GetValueOfDecimal(dr["Amount_Document"]);
                        row.AmountCurrencyIso = Util.GetValueOfString(dr["Amount_Currency_Iso"]);
                        row.AmountCurrencySymbol = Util.GetValueOfString(dr["Amount_Currency_Symbol"]);
                        row.AmountPrecision = precision;

                        result.Rows.Add(row);

                        if (row.IsDueToday) { result.DueToday++; }
                    }
                }

                result.SchedulesDue = result.Rows.Count;
                result.Loaded = true;
            }
            catch (Exception ex)
            {
                /* Caught here so the caller can tell "query failed" apart from
                   "nothing is due" - the widget renders those two cases
                   differently. */
                Log.Log(Level.SEVERE, "VAS_220_SchedulesDue.GetSchedulesDue AD_Client_ID=" + ctx.GetAD_Client_ID()
                    + " WindowDays=" + windowDays, ex);
                result.Loaded = false;
                result.SchedulesDue = 0;
                result.DueToday = 0;
                result.Rows.Clear();
            }

            return result;
        }

        /// <summary>
        /// Result envelope for the widget: the two headline figures, the resolved
        /// window and the full row list the drill-down modal pages through.
        /// </summary>
        public class SchedulesDueInfo
        {
            /// <summary>False only when the data could not be read (no context or
            /// query failure). A tenant with nothing due is Loaded=true with zero
            /// counts.</summary>
            public bool Loaded { get; set; }

            /// <summary>Setups due inside the window.</summary>
            public int SchedulesDue { get; set; }

            /// <summary>Subset of the above whose DateNextRun is today.</summary>
            public int DueToday { get; set; }

            /// <summary>Resolved look-ahead window in days, after clamping.</summary>
            public int WindowDays { get; set; }

            /// <summary>First day of the window, yyyy-MM-dd. Formatted for display client-side.</summary>
            public string DateFrom { get; set; }

            /// <summary>Last day of the window (inclusive), yyyy-MM-dd.</summary>
            public string DateTo { get; set; }

            public List<ScheduleDueRow> Rows { get; set; }
        }

        /// <summary>
        /// One recurring setup due inside the window. Type and frequency are carried
        /// as stored list codes; the client resolves the labels from AD_Message.
        /// </summary>
        public class ScheduleDueRow
        {
            public int C_Recurring_ID { get; set; }
            public string RecurringName { get; set; }

            /// <summary>yyyy-MM-dd. Date formatting for display is done client-side.</summary>
            public string DateNextRun { get; set; }

            public bool IsDueToday { get; set; }

            /// <summary>C_Recurring.RecurringType stored code (B/G/I/J/O/P).</summary>
            public string RecurringType { get; set; }

            /// <summary>C_Recurring.FrequencyType stored code (D/W/M/Q).</summary>
            public string FrequencyType { get; set; }

            public int Frequency { get; set; }
            public int RunsRemaining { get; set; }

            /// <summary>Partner of the source document; empty for GL setups, which
            /// the client labels as internal.</summary>
            public string BPartnerName { get; set; }

            /// <summary>Journal / journal-batch description, shown beside the
            /// internal label for GL setups.</summary>
            public string JournalDescription { get; set; }

            /// <summary>Source-document amount, untouched and in its own currency -
            /// nothing is converted.</summary>
            public decimal Amount { get; set; }

            /// <summary>ISO code of the currency Amount is expressed in; empty when
            /// the setup has no source document to take a currency from.</summary>
            public string AmountCurrencyIso { get; set; }

            /// <summary>Display symbol of the same currency, when one is defined.</summary>
            public string AmountCurrencySymbol { get; set; }

            /// <summary>Standard precision of that currency, so a zero-decimal
            /// currency is not printed with two.</summary>
            public int AmountPrecision { get; set; }
        }
    }
}
