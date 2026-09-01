/******************************************************
 * Module Name    : VASLogic
 * Purpose        : Recurring search widget data
 * chronological  : Development
 * Created Date   : 2026-09-01
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
    /// Module Name : VAS_227_SearchRecurring
    /// Purpose     : Backs the VAS_227_SearchRecurringWidget dashboard search bar
    ///               (Recurring module, full-width 1x9). Answers "which recurring
    ///               setup is this?" from a single free-text box: the setup name,
    ///               its description, the document number of whichever source
    ///               document it copies (invoice / order / payment / GL journal /
    ///               journal batch), the project value or name, and the business
    ///               partner behind that document.
    ///
    ///               The term can also name a RECURRING TYPE ("invoice", "project")
    ///               or a FREQUENCY ("monthly", "quarterly"); those keywords are
    ///               resolved to their stored codes in C# and OR-ed onto the text
    ///               match, so a user can search the way they speak about the
    ///               setups rather than by document number alone.
    ///
    ///               Amounts come from whichever source document the setup copies
    ///               (invoice / order / payment) and are reported UNCONVERTED, in
    ///               that document's own currency, so each row carries its currency
    ///               ISO, symbol and standard precision. GL and project setups have
    ///               no schema-backed amount of their own and correctly fall
    ///               through to 0.
    ///
    ///               No display text is produced by the query: the type and the
    ///               frequency are resolved to localized labels by the client from
    ///               the stored codes returned here. A GL setup simply has no
    ///               business partner, and the client leaves that line out.
    ///
    ///               MRole row-level security is applied to the main physical table
    ///               only (C_Recurring, alias r) - a join-free base sub-select, so
    ///               the access-SQL parser never sees the lookup joins. The
    ///               document, journal, project, partner and currency joins are
    ///               lookups that inherit that filter. There is no CTE, so no CTE
    ///               alias is passed to MRole. ORDER BY and the paging suffix are
    ///               appended AFTER AddAccessSQL so the FROM-clause parser is not
    ///               confused by a trailing clause.
    ///
    ///               The tenant always comes from the authenticated context; the
    ///               client never supplies AD_Client_ID. The free-text term is
    ///               always bound, never concatenated. Compatible with PostgreSQL
    ///               and Oracle.
    /// Chronological development:
    ///   VAI154      2026-09-01 Created
    /// </summary>
    public class VAS_227_SearchRecurringModel
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_227_SearchRecurringModel).FullName);

        /// <summary>Rows per request when the client asks for none.</summary>
        public const int MAXROWS_DEFAULT = 25;

        /// <summary>Largest accepted page size - the dropdown pages by scrolling, so
        /// anything beyond this is not a real request.</summary>
        public const int MAXROWS_MAX = 50;

        /// <summary>Shortest term that is worth a round trip. Below this the widget
        /// shows its "type to search" hint instead.</summary>
        public const int TERM_MIN_LENGTH = 2;

        /// <summary>Guard on the scroll offset so a hostile value cannot walk the
        /// whole table one page at a time.</summary>
        private const int OFFSET_MAX = 100000;

        /* C_Recurring.RecurringType stored codes (list reference), shared with the
           sibling Recurring widgets. Returned raw; the client resolves the labels. */
        public const string RECURRINGTYPE_GLJournal = "B";
        public const string RECURRINGTYPE_GLJournalBatch = "G";
        public const string RECURRINGTYPE_Invoice = "I";
        public const string RECURRINGTYPE_Project = "J";
        public const string RECURRINGTYPE_Order = "O";
        public const string RECURRINGTYPE_Payment = "P";

        /* C_Recurring.FrequencyType stored codes (list reference). */
        public const string FREQUENCYTYPE_Daily = "D";
        public const string FREQUENCYTYPE_Weekly = "W";
        public const string FREQUENCYTYPE_Monthly = "M";
        public const string FREQUENCYTYPE_Quarterly = "Q";

        // ─────────────────────────────────────────────────────────────────────
        // §1  The search
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Searches the tenant's active recurring setups for the given free-text term
        /// and returns one page of the most relevant hits, best match first.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="query">Free-text term as typed. Shorter than
        /// <see cref="TERM_MIN_LENGTH"/> returns an empty page rather than the whole
        /// table.</param>
        /// <param name="maxRows">Rows to return; clamped to [1, MAXROWS_MAX].</param>
        /// <param name="offset">Rows to skip - the dropdown's scroll paging.</param>
        /// <returns>Populated <see cref="RecurringSearchPage"/> (never null). Loaded
        /// is false only when the context is missing or the query failed; a term that
        /// matches nothing returns Loaded=true and an empty list.</returns>
        public RecurringSearchPage Search(Ctx ctx, string query, int maxRows, int offset)
        {
            RecurringSearchPage result = new RecurringSearchPage();
            result.Loaded = false;
            result.Rows = new List<RecurringSearchRow>();

            if (ctx == null) { return result; }

            /* Paging inputs arrive from the client, so they are clamped rather than
               trusted - an unbounded page size would defeat the point of paging. */
            if (maxRows <= 0) { maxRows = MAXROWS_DEFAULT; }
            if (maxRows > MAXROWS_MAX) { maxRows = MAXROWS_MAX; }
            if (offset < 0) { offset = 0; }
            if (offset > OFFSET_MAX) { offset = OFFSET_MAX; }

            result.MaxRows = maxRows;
            result.Offset = offset;

            string term = (query ?? "").Trim();
            if (term.Length < TERM_MIN_LENGTH)
            {
                /* Not a failure - there is simply nothing to search for yet. */
                result.Loaded = true;
                return result;
            }

            try
            {
                LoadRows(ctx, term, maxRows, offset, result);
                result.Loaded = true;
            }
            catch (Exception ex)
            {
                /* Caught here so the caller can tell "query failed" apart from "no
                   match" - the widget renders those two cases differently. */
                Log.Log(Level.SEVERE, "VAS_227_SearchRecurring.Search AD_Client_ID="
                    + ctx.GetAD_Client_ID() + " Offset=" + offset, ex);
                result.Loaded = false;
                result.Rows.Clear();
            }

            return result;
        }

        /// <summary>
        /// Reads one page of matches and folds each setup into a display-ready row.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="term">Trimmed search term, already known to be long enough.</param>
        /// <param name="maxRows">Rows to return.</param>
        /// <param name="offset">Rows to skip.</param>
        /// <param name="result">Page envelope whose Rows list is filled in.</param>
        /// <returns>void</returns>
        private void LoadRows(Ctx ctx, string term, int maxRows, int offset, RecurringSearchPage result)
        {
            bool isOracle = DB.IsOracle();

            /* Concatenation separator / empty-string literal, spelled the way each
               backend wants it. Oracle treats '' as NULL, and concatenating NULL
               there is a no-op, so the searchable string stays well formed either
               way. */
            string sep = isOracle ? "N' '" : "' '";
            string empty = isOracle ? "N''" : "''";

            /* The source document number, written once and reused by the relevance
               ranking, the searchable text and the SELECT list. */
            const string sourceDocumentExpression =
                "COALESCE(inv.DocumentNo,ord.DocumentNo,pay.DocumentNo,glj.DocumentNo,glb.DocumentNo,prj.Value)";

            /* The amount is reported in the source document's OWN currency - no
               conversion. GL and project setups have no schema-backed amount of their
               own and correctly fall through to 0. */
            const string amountExpression = "COALESCE(inv.GrandTotal,ord.GrandTotal,pay.PayAmt,0)";

            const string documentCurrencyExpression =
                "COALESCE(inv.C_Currency_ID,ord.C_Currency_ID,pay.C_Currency_ID)";

            /* MRole goes on the main physical table only, so the base select is kept
               join-free - the access-SQL parser sees one table and one WHERE. The
               lookup joins are added around it below. No org predicate is written by
               hand: AddAccessSQL supplies the organisation access clause itself. */
            StringBuilder baseSql = new StringBuilder();
            baseSql.Append(@"
                SELECT r.C_Recurring_ID, r.Name, r.Description, r.RecurringType, r.FrequencyType,
                       r.Frequency, r.RunsMax, r.RunsRemaining, r.DateNextRun, r.DateLastRun,
                       r.C_Invoice_ID, r.C_Order_ID, r.C_Payment_ID, r.C_Project_ID,
                       r.GL_Journal_ID, r.GL_JournalBatch_ID, r.AD_Client_ID, r.AD_Org_ID
                FROM C_Recurring r
                WHERE r.IsActive='Y'
                  AND r.AD_Client_ID IN (@AD_Client_ID)");

            string accessSql = MRole.GetDefault(ctx).AddAccessSQL(
                baseSql.ToString(), "r", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            /* Beyond the free-text match, the term can also name a recurring TYPE
               ("invoice", "project") or a FREQUENCY ("monthly"). Those resolve to
               stored codes here, in C#, and are OR-ed onto the text match - the codes
               are fixed literals, so nothing the user typed reaches the statement. */
            string matchClause = BuildMatchClause(term, sep, empty, sourceDocumentExpression);

            StringBuilder sql = new StringBuilder();
            sql.Append(@"
                SELECT b.C_Recurring_ID AS Recurring_ID,
                       b.Name AS Recurring_Name,
                       b.Description AS Recurring_Description,
                       b.RecurringType AS Recurring_Type,
                       b.FrequencyType AS Frequency_Type,
                       b.Frequency AS Frequency_Value,
                       b.RunsMax AS Runs_Max,
                       b.RunsRemaining AS Runs_Remaining,
                       b.DateNextRun AS Date_Next_Run,
                       b.DateLastRun AS Date_Last_Run,
                       bp.Name AS BPartner_Name,
                       ").Append(sourceDocumentExpression).Append(@" AS Source_Document_No,
                       ").Append(amountExpression).Append(@" AS Amount_Document,
                       doccur.ISO_Code AS Amount_Currency_Iso,
                       doccur.CurSymbol AS Amount_Currency_Symbol,
                       doccur.StdPrecision AS Amount_Currency_Precision,
                       CASE WHEN LOWER(b.Name)=LOWER(@QExact) THEN 4
                            WHEN LOWER(b.Name) LIKE LOWER(@QStart) THEN 3
                            WHEN LOWER(COALESCE(").Append(sourceDocumentExpression).Append(",").Append(empty).Append(@")) LIKE LOWER(@QDoc) THEN 2
                            ELSE 1 END AS Relevance
                FROM (").Append(accessSql).Append(@") b
                LEFT OUTER JOIN C_Invoice inv ON (inv.C_Invoice_ID=b.C_Invoice_ID)
                LEFT OUTER JOIN C_Order ord ON (ord.C_Order_ID=b.C_Order_ID)
                LEFT OUTER JOIN C_Payment pay ON (pay.C_Payment_ID=b.C_Payment_ID)
                LEFT OUTER JOIN C_Project prj ON (prj.C_Project_ID=b.C_Project_ID)
                LEFT OUTER JOIN GL_Journal glj ON (glj.GL_Journal_ID=b.GL_Journal_ID)
                LEFT OUTER JOIN GL_JournalBatch glb ON (glb.GL_JournalBatch_ID=b.GL_JournalBatch_ID)
                LEFT OUTER JOIN C_BPartner bp ON (bp.C_BPartner_ID=COALESCE(inv.C_BPartner_ID,ord.C_BPartner_ID,pay.C_BPartner_ID,prj.C_BPartner_ID))
                LEFT OUTER JOIN C_Currency doccur ON (doccur.C_Currency_ID=").Append(documentCurrencyExpression).Append(@")
                WHERE ").Append(matchClause);

            /* Best match first, then the soonest run, with the surrogate key breaking
               a tie so scroll paging is stable and a row can never appear on two
               pages. A setup with no next run sorts last on both backends, where ASC
               puts NULLs last by default. Appended after AddAccessSQL by design, and
               the paging suffix after that. */
            sql.Append(@"
                ORDER BY Relevance DESC, Date_Next_Run, Recurring_Name, Recurring_ID");

            /* One row MORE than asked for: its presence is what tells the client
               another page exists, without paying for a second COUNT query. It is
               trimmed off below. */
            int fetch = maxRows + 1;
            sql.Append(PagingSuffix(fetch, offset));

            /* Binds in the order their placeholders appear in the statement - the
               provider binds POSITIONALLY, so this list and the SQL text must be
               built from the same reading order: the relevance CASE first (SELECT
               list), then the tenant inside the base sub-select (FROM), then the
               searchable-text term (WHERE). */
            string like = "%" + term + "%";
            List<SqlParameter> parameters = new List<SqlParameter>();
            parameters.Add(new SqlParameter("@QExact", term));
            parameters.Add(new SqlParameter("@QStart", term + "%"));
            parameters.Add(new SqlParameter("@QDoc", like));
            parameters.Add(new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()));
            parameters.Add(new SqlParameter("@SearchText", like));

            DataSet ds = DB.ExecuteDataset(sql.ToString(), parameters.ToArray(), null);
            if (ds == null || ds.Tables.Count == 0) { return; }

            DataTable dt = ds.Tables[0];
            for (int i = 0; i < dt.Rows.Count; i++)
            {
                DataRow dr = dt.Rows[i];

                DateTime? nextRun = Util.GetValueOfDateTime(dr["Date_Next_Run"]);
                DateTime? lastRun = Util.GetValueOfDateTime(dr["Date_Last_Run"]);

                /* Standard precision of the row's OWN currency, so a zero-decimal
                   currency is not printed with two. Defaults to 2 when the setup has
                   no source document to take a currency from. */
                int precision = Util.GetValueOfInt(dr["Amount_Currency_Precision"]);
                if (precision < 0 || precision > 6) { precision = 2; }

                RecurringSearchRow row = new RecurringSearchRow();
                row.C_Recurring_ID = Util.GetValueOfInt(dr["Recurring_ID"]);
                row.RecurringName = Util.GetValueOfString(dr["Recurring_Name"]);
                row.Description = Util.GetValueOfString(dr["Recurring_Description"]);
                row.RecurringType = Util.GetValueOfString(dr["Recurring_Type"]);
                row.FrequencyType = Util.GetValueOfString(dr["Frequency_Type"]);
                row.Frequency = Util.GetValueOfInt(dr["Frequency_Value"]);
                row.RunsMax = Util.GetValueOfInt(dr["Runs_Max"]);
                row.RunsRemaining = Util.GetValueOfInt(dr["Runs_Remaining"]);
                row.DateNextRun = nextRun.HasValue ? nextRun.Value.ToString("yyyy-MM-dd") : "";
                row.DateLastRun = lastRun.HasValue ? lastRun.Value.ToString("yyyy-MM-dd") : "";
                row.BPartnerName = Util.GetValueOfString(dr["BPartner_Name"]);
                row.SourceDocumentNo = Util.GetValueOfString(dr["Source_Document_No"]);
                row.Amount = Util.GetValueOfDecimal(dr["Amount_Document"]);
                row.AmountCurrencyIso = Util.GetValueOfString(dr["Amount_Currency_Iso"]);
                row.AmountCurrencySymbol = Util.GetValueOfString(dr["Amount_Currency_Symbol"]);
                row.AmountPrecision = precision;

                result.Rows.Add(row);
            }

            /* Trim the sentinel row; its presence signals another page exists. */
            result.HasMore = result.Rows.Count > maxRows;
            if (result.HasMore) { result.Rows.RemoveAt(result.Rows.Count - 1); }
        }

        // ─────────────────────────────────────────────────────────────────────
        // §2  Shared query fragments
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Builds the WHERE predicate that decides whether a setup matches the term.
        ///
        /// The searchable text is ONE concatenated string so the term is bound once
        /// (@SearchText) instead of repeated per column - which also keeps the
        /// positional bind list short and unambiguous. Both sides are wrapped in
        /// LOWER() so the match is case-insensitive using the database's own
        /// collation rather than C#'s ToLower, which can disagree on some locales.
        ///
        /// Keyword hits on the recurring type and the frequency are OR-ed on: those
        /// are stored codes, and a user searching "monthly invoice" is naming them,
        /// not a document number. Only fixed literals are inlined for them - the
        /// typed text itself never reaches the statement.
        /// </summary>
        /// <param name="term">Trimmed search term as typed.</param>
        /// <param name="sep">Backend-specific separator literal.</param>
        /// <param name="empty">Backend-specific empty-string literal.</param>
        /// <param name="sourceDocumentExpression">Source document number expression.</param>
        /// <returns>WHERE clause fragment (no leading WHERE).</returns>
        private string BuildMatchClause(string term, string sep, string empty, string sourceDocumentExpression)
        {
            string textMatch =
                "LOWER(b.Name || " + sep +
                " || COALESCE(b.Description, " + empty + ") || " + sep +
                " || COALESCE(" + sourceDocumentExpression + ", " + empty + ") || " + sep +
                " || COALESCE(prj.Name, " + empty + ") || " + sep +
                " || COALESCE(bp.Name, " + empty + ") || " + sep +
                " || COALESCE(bp.Value, " + empty + ")) LIKE LOWER(@SearchText)";

            string keyword = term.ToLowerInvariant();

            List<string> typeCodes = new List<string>();
            if (keyword.Contains("invoice")) { typeCodes.Add("'" + RECURRINGTYPE_Invoice + "'"); }
            if (keyword.Contains("order")) { typeCodes.Add("'" + RECURRINGTYPE_Order + "'"); }
            if (keyword.Contains("payment")) { typeCodes.Add("'" + RECURRINGTYPE_Payment + "'"); }
            if (keyword.Contains("project")) { typeCodes.Add("'" + RECURRINGTYPE_Project + "'"); }
            /* "batch" is checked before the broader "journal" so "journal batch"
               narrows to the batch code instead of matching both. */
            if (keyword.Contains("batch")) { typeCodes.Add("'" + RECURRINGTYPE_GLJournalBatch + "'"); }
            else if (keyword.Contains("journal"))
            {
                typeCodes.Add("'" + RECURRINGTYPE_GLJournal + "'");
                typeCodes.Add("'" + RECURRINGTYPE_GLJournalBatch + "'");
            }

            List<string> frequencyCodes = new List<string>();
            if (keyword.Contains("dail")) { frequencyCodes.Add("'" + FREQUENCYTYPE_Daily + "'"); }
            if (keyword.Contains("week")) { frequencyCodes.Add("'" + FREQUENCYTYPE_Weekly + "'"); }
            if (keyword.Contains("month")) { frequencyCodes.Add("'" + FREQUENCYTYPE_Monthly + "'"); }
            if (keyword.Contains("quarter")) { frequencyCodes.Add("'" + FREQUENCYTYPE_Quarterly + "'"); }

            List<string> alternatives = new List<string>();
            alternatives.Add(textMatch);
            if (typeCodes.Count > 0)
            {
                alternatives.Add("b.RecurringType IN (" + string.Join(",", typeCodes.ToArray()) + ")");
            }
            if (frequencyCodes.Count > 0)
            {
                alternatives.Add("b.FrequencyType IN (" + string.Join(",", frequencyCodes.ToArray()) + ")");
            }

            if (alternatives.Count == 1) { return textMatch; }
            return "(" + string.Join(" OR ", alternatives.ToArray()) + ")";
        }

        /// <summary>
        /// Database-specific paging suffix: OFFSET / FETCH on Oracle, LIMIT / OFFSET
        /// elsewhere. Both values are server-clamped integers, never client text.
        /// </summary>
        /// <param name="fetchRows">Rows to fetch.</param>
        /// <param name="offset">Rows to skip.</param>
        /// <returns>Paging clause.</returns>
        private string PagingSuffix(int fetchRows, int offset)
        {
            if (fetchRows <= 0) { fetchRows = MAXROWS_DEFAULT; }
            if (offset < 0) { offset = 0; }

            if (DB.IsOracle())
            {
                return " OFFSET " + offset + " ROWS FETCH NEXT " + fetchRows + " ROWS ONLY";
            }
            return " LIMIT " + fetchRows + " OFFSET " + offset;
        }

        // ─────────────────────────────────────────────────────────────────────
        // §3  Contracts
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>One page of recurring search hits.</summary>
        public class RecurringSearchPage
        {
            /// <summary>False only when the data could not be read. A term that
            /// matches nothing is Loaded=true with an empty list.</summary>
            public bool Loaded { get; set; }

            /// <summary>Rows actually requested, after clamping.</summary>
            public int MaxRows { get; set; }

            /// <summary>Rows skipped, after clamping.</summary>
            public int Offset { get; set; }

            /// <summary>True when at least one more page exists behind this one.</summary>
            public bool HasMore { get; set; }

            public List<RecurringSearchRow> Rows { get; set; }
        }

        /// <summary>One matching recurring setup.</summary>
        public class RecurringSearchRow
        {
            public int C_Recurring_ID { get; set; }
            public string RecurringName { get; set; }
            public string Description { get; set; }

            /// <summary>C_Recurring.RecurringType stored code (B/G/I/J/O/P).</summary>
            public string RecurringType { get; set; }

            /// <summary>C_Recurring.FrequencyType stored code (D/W/M/Q).</summary>
            public string FrequencyType { get; set; }

            public int Frequency { get; set; }
            public int RunsMax { get; set; }
            public int RunsRemaining { get; set; }

            /// <summary>yyyy-MM-dd, empty when unset. Date formatting for display is
            /// done client-side.</summary>
            public string DateNextRun { get; set; }

            public string DateLastRun { get; set; }

            /// <summary>Partner of the source document; empty for GL setups, whose
            /// partner line the client omits.</summary>
            public string BPartnerName { get; set; }

            /// <summary>DocumentNo of the source document, or the project value;
            /// empty when the setup copies none of them.</summary>
            public string SourceDocumentNo { get; set; }

            /// <summary>Source-document amount, untouched and in its own currency -
            /// nothing is converted.</summary>
            public decimal Amount { get; set; }

            public string AmountCurrencyIso { get; set; }
            public string AmountCurrencySymbol { get; set; }
            public int AmountPrecision { get; set; }
        }
    }
}
