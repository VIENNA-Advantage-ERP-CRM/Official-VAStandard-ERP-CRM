/******************************************************
 * Module Name    : VASLogic
 * Purpose        : Mandatory Close Checklist dashboard widget - the 23 check handlers
 * chronological  : Development
 * Created Date   : 2026-08-24
 * Created by     : VAI154
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
    /// Module Name : VAS_195_MandatoryChecklist (check handlers)
    /// Purpose     : The 23 period-close check handlers. The framework - accounting
    ///               context, period selection, MRole application, paging, document
    ///               resolution and the data contracts - lives in the sibling partial
    ///               VAS_195_MandatoryChecklistModel.cs.
    ///
    ///               ONE STATEMENT PER CHECK. Almost every handler builds a single
    ///               DetailSpec and lets the framework both COUNT it and PAGE it. That
    ///               is deliberate: a checklist whose headline number is produced by a
    ///               different query from its drill-down is a checklist that will
    ///               eventually say "12" and then show 9 rows. Only the handlers whose
    ///               headline is genuinely an aggregate rather than a row count - the
    ///               Trial Balance difference, the suspense closing balances, the bank
    ///               reconciliation ratio - run a second, explicitly aggregate query.
    ///
    ///               OPTIONAL SCHEMA. Handlers that reach into a module (fixed assets,
    ///               FRPT revaluation, VA009 payment execution, bank-statement matching)
    ///               probe AD_Table/AD_Column first and answer NOT_APPLICABLE when the
    ///               module is absent. The queries behind those probes are written to
    ///               the documented column names but have NOT been verified against a
    ///               live installation of those modules - they are guarded precisely so
    ///               that being wrong degrades the row instead of the checklist.
    ///
    ///               DOCUMENT DISCOVERY. Checks 01 and 02 do NOT work from a list of
    ///               tables kept in this file. They ask the Application Dictionary which
    ///               tables behave like documents - a key, a usable date, at least one
    ///               active menu-reachable window, and EITHER a DocStatus column OR a
    ///               Posted column - which is the union of what VAS_197 and VAS_198
    ///               recognise, so the checklist covers everything those widgets cover.
    ///               A hand-kept list was the earlier design and it was wrong in the way
    ///               hand-kept lists always are: whatever nobody remembered to add simply
    ///               never appeared, and nothing said so. The menu requirement is what
    ///               keeps staging and workflow tables out; they carry a DocStatus but no
    ///               user opens one. Check 15 keeps a fixed list, because "does this
    ///               document move stock" is not a question the dictionary answers.
    ///
    ///               DocStatus is deliberately NOT the entry ticket. Requiring it was
    ///               what made check 02 disagree with the VAS_198 widget: the accounting
    ///               tables that post without ever running a document workflow -
    ///               M_MatchInv and M_MatchPO are the obvious ones - carry a Posted
    ///               column and a Posted button on their window but no DocStatus at all,
    ///               so discovery dropped them in silence and the checklist reported
    ///               fewer unposted entries than the widget beside it. Each check now
    ///               states which column it needs (01 and 15 need DocStatus, 02 needs
    ///               Posted) and the branch builder emits a typed literal for whichever
    ///               of the two a given table does not have.
    ///
    ///               UNION CHECKS. Checks 01, 02, 04, 11 and 15 span several physical
    ///               tables. Each branch is built and secured on its OWN main alias and
    ///               only then combined with UNION ALL; the combined statement is never
    ///               re-secured, and the framework's count runner wraps it as a derived
    ///               table rather than applying MRole a second time. With discovery in
    ///               play checks 01 and 02 can reach a few dozen branches, which is the
    ///               cost of covering everything rather than a chosen few.
    /// Chronological development:
    ///   VAI145      2026-08-24 Created
    ///   VAI145      2026-08-25 Discovery admits Posted-only tables so check 02 agrees
    ///                          with the VAS_198 widget
    ///   VAI145      2026-08-25 Check 09 drills into the suspense POSTINGS - ledger,
    ///                          document, dates, side, amount - instead of an account
    ///                          summary; check 10 keeps the summary
    ///   VAI145      2026-08-25 Check 09's posting list bounded by the selected period,
    ///                          like every other check on the card
    ///   VAI145      2026-08-25 Check 12 keyed on DateNextRun alone - the run test it was
    ///                          ANDed with could only hide live exceptions - and its type
    ///                          and frequency read as names rather than stored codes
    ///   VAI145      2026-08-25 Check 15 groups by screen with the screen name under the
    ///                          document number, and drops the Currency column its three
    ///                          tables can never populate
    ///   VAI145      2026-08-25 Check 03 reads C_Payment.IsAllocated and excludes
    ///                          advances, instead of re-deriving allocation from
    ///                          C_AllocationLine - it now agrees with the VAS_199 widget
    ///   VAI145      2026-08-25 Check 19 rewritten to the simplified rule: foreign-currency
    ///                          invoice count, then FRPT_RevaluationDate journal count.
    ///                          Reclassified WARNING, reports PASS / FAIL, no drill-down
    /// </summary>
    public partial class VAS_195_MandatoryChecklistModel
    {
        /* Quantity tolerance for the three-way-match checks. A compile-time policy
           constant, never client input, and inlined for that reason. */
        private const string QTY_TOLERANCE = "0";

        /* Price tolerance for check 08. A compile-time policy constant like the quantity
           one, and inlined for the same reason - but also because its expression appears
           TWICE in that statement (once in the variance-type CASE, once in the WHERE),
           and a bind reused across two occurrences is filled from the wrong slot under
           positional binding. A literal has no slot to get wrong. */
        private const string PRICE_TOLERANCE = "0";

        /* Payment execution states that mean "not settled": In-Progress, Bounced,
           Rejected. VA009 module column; probed before use. */
        private const string COLUMN_EXECUTION_STATUS = "VA009_ExecutionStatus";

        /* Which side of the ledger one Fact_Acct row sits on. Emitted as a stable TOKEN
           rather than as display text: the client badge translates it through
           VAS_195_Debit / VAS_195_Credit, which a string composed in SQL could never be.
           Inlined into the statement because they are compile-time policy constants, and
           because each appears in a CASE the WHERE never binds against. */
        private const string DRCR_DEBIT = "Debit";
        private const string DRCR_CREDIT = "Credit";

        /* The date column a discovered document is bounded by, in preference order.
           DateAcct first because the period filter must mean the same thing for every
           table on the card; MovementDate for the inventory documents that carry no
           accounting date at all, where the movement IS the accounting event. This is a
           short trusted list, not a scan for anything date-shaped - DateOrdered,
           DateInvoiced and DatePromised mean different things and guessing between them
           would silently rebase the period. */
        private static readonly string[] DateColumnPreference =
            new string[] { "DateAcct", "MovementDate", "StatementDate" };

        /* The amount column shown in the detail list, in preference order. First match
           wins; a table with none of them shows no amount rather than a wrong one. */
        private static readonly string[] AmountColumnPreference = new string[]
        {
            "GrandTotal", "PayAmt", "ControlAmt", "StatementDifference",
            "TotalDifference", "VAFAM_DepreciatedAmt", "LineTotalAmt"
        };

        /* Inventory movement documents for check 15. Deliberately a fixed list rather
           than a discovery: "does this document move stock" is not a question the
           dictionary answers, and check 15 goes on to look for M_Transaction rows
           through line references that only these tables have. */
        private static readonly string[] InventoryTables =
            new string[] { "M_InOut", "M_Inventory", "M_Movement" };

        /* Discovered documents, cached per request - checks 01 and 02 both need them. */
        private List<DocDef> _discovered;

        /// <summary>
        /// Every table in this installation that behaves like a document, discovered
        /// from the Application Dictionary rather than from a list maintained here.
        ///
        /// A table qualifies when it has a key column, a usable date column, at least one
        /// active menu-reachable window over it, and EITHER a DocStatus column OR a
        /// Posted column. That is the UNION of what VAS_197 and VAS_198 recognise, so the
        /// checklist covers everything those widgets cover rather than a hand-kept subset
        /// that silently omits whatever nobody remembered to add. The menu requirement is
        /// what keeps staging and workflow tables out: they carry a DocStatus but no user
        /// ever opens one.
        ///
        /// The OR is the whole point of this revision. DocStatus alone used to be the
        /// entry ticket, and it quietly excluded every table that posts to the ledger
        /// without running a document workflow - M_MatchInv and M_MatchPO carry a Posted
        /// column and a Posted button on their window but have no DocStatus whatsoever.
        /// Check 02 therefore reported fewer unposted entries than the VAS_198 widget
        /// sitting on the same dashboard, which is exactly the kind of disagreement a
        /// close checklist cannot afford. Discovery now admits them; the checks
        /// themselves say which of the two columns they require, and the branch builder
        /// emits a typed literal wherever a table lacks one.
        ///
        /// Still server-controlled, and still nothing the browser can influence - the
        /// browser sends a check code, and every table name reaching SQL comes from
        /// AD_Table and is re-validated by IsSafeIdentifier.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <returns>Discovered documents (never null).</returns>
        private List<DocDef> DiscoverDocuments(Ctx ctx)
        {
            if (_discovered != null) { return _discovered; }

            _discovered = new List<DocDef>();

            StringBuilder select = new StringBuilder();
            select.Append(@"
                SELECT t.AD_Table_ID AS AD_Table_ID,
                       t.TableName AS Table_Name,
                       MAX(CASE WHEN c.IsKey='Y' THEN c.ColumnName END) AS Key_Column");

            /* One probed column per name the branch builder may need. Listing them here
               costs one query for the whole card; probing them per table would cost one
               per table per check. */
            AppendProbe(select, "DocumentNo");
            AppendProbe(select, "DocStatus");
            AppendProbe(select, "Posted");
            AppendProbe(select, "Processed");
            AppendProbe(select, "C_BPartner_ID");
            AppendProbe(select, "C_Currency_ID");
            AppendProbe(select, "C_DocType_ID");
            AppendProbe(select, "C_DocTypeTarget_ID");

            for (int i = 0; i < DateColumnPreference.Length; i++) { AppendProbe(select, DateColumnPreference[i]); }
            for (int i = 0; i < AmountColumnPreference.Length; i++) { AppendProbe(select, AmountColumnPreference[i]); }

            select.Append(@"
                FROM AD_Table t
                INNER JOIN AD_Column c ON (c.AD_Table_ID=t.AD_Table_ID AND c.IsActive='Y')
                WHERE t.IsActive='Y'
                  /* COALESCE, not a bare comparison: AD_Table.IsView is NULLable and is
                     unset on most tables, so 't.IsView=''N''' is NULL - never true - and
                     silently discovers nothing at all. VAS_197 and VAS_198 guard it the
                     same way. */
                  AND COALESCE(t.IsView,'N')='N'
                  AND (EXISTS(SELECT 1 FROM AD_Column dc WHERE dc.AD_Table_ID=t.AD_Table_ID AND dc.ColumnName='DocStatus' AND dc.IsActive='Y')
                       OR EXISTS(SELECT 1 FROM AD_Column ac WHERE ac.AD_Table_ID=t.AD_Table_ID AND ac.ColumnName='Posted' AND ac.IsActive='Y'))
                  AND EXISTS(SELECT 1 FROM AD_Tab tab INNER JOIN AD_Window w ON (w.AD_Window_ID=tab.AD_Window_ID) INNER JOIN AD_Menu m ON (m.AD_Window_ID=w.AD_Window_ID) WHERE tab.AD_Table_ID=t.AD_Table_ID AND tab.IsActive='Y' AND w.IsActive='Y' AND m.IsActive='Y')
                GROUP BY t.AD_Table_ID,t.TableName
                ORDER BY t.TableName");

            DataSet ds = DB.ExecuteDataset(select.ToString(), new SqlParameter[] { }, null);
            if (ds == null || ds.Tables.Count == 0) { return _discovered; }

            DataTable dt = ds.Tables[0];
            for (int i = 0; i < dt.Rows.Count; i++)
            {
                DocDef def = ReadDiscoveredDoc(dt.Rows[i]);
                if (def != null) { _discovered.Add(def); }
            }

            Log.Log(Level.INFO, "VAS_195: discovered " + _discovered.Count
                + " document tables for the unprocessed / unposted checks");

            /* The screen split is an ENHANCEMENT over the table branch, never a
               precondition for it. If screen resolution fails - a dictionary the parser
               dislikes, a clause that will not resolve - the checks must still report
               their documents at table granularity rather than reporting nothing at
               all. Losing the split costs a grouping; losing the fallback costs the
               whole check. */
            try
            {
                List<DocDef> byScreen = ExpandByScreen(ctx, _discovered);
                if (byScreen.Count > 0) { _discovered = byScreen; }
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_195: screen split failed; falling back to "
                    + "table-level document branches", ex);
            }

            RankByScreen(_discovered);

            Log.Log(Level.INFO, "VAS_195: " + _discovered.Count + " document branches after the screen split");

            return _discovered;
        }

        /// <summary>
        /// Gives each discovered table its primary SCREEN - the window a reader would
        /// open it on - for labelling, grouping and drill-down navigation. One branch
        /// per table, exactly as before: this does NOT split a table into one branch per
        /// screen.
        ///
        /// It did, briefly, and that was wrong. Splitting C_Order into Sales Order /
        /// Purchase Order / Sales Quotation / Blanket Order needs each branch to carry
        /// its tab's WhereClause, and applying those clauses emptied every branch of
        /// checks 01 and 02 - the checks reported a clean PASS over a period that was
        /// not clean. A filter that silently reports nothing is far worse on a close
        /// checklist than a grouping that is coarser than one would like, so the filter
        /// is gone until its behaviour against this installation's actual AD_Tab data
        /// has been confirmed rather than assumed.
        ///
        /// The PRIMARY screen is chosen by <see cref="PickPrimaryScreen"/>: it is the
        /// right window to open a record on and the right name to group by.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="tables">Discovered tables, labelled in place.</param>
        /// <returns>The same tables, one entry each (never null).</returns>
        private List<DocDef> ExpandByScreen(Ctx ctx, List<DocDef> tables)
        {
            if (tables.Count == 0) { return tables; }

            Dictionary<int, List<ScreenDef>> byTable = ReadScreens(ctx, tables);

            for (int t = 0; t < tables.Count; t++)
            {
                DocDef table = tables[t];

                List<ScreenDef> screens;
                if (!byTable.TryGetValue(table.AD_Table_ID, out screens) || screens.Count == 0)
                {
                    continue;
                }

                ScreenDef primary = PickPrimaryScreen(screens);

                table.AD_Window_ID = primary.AD_Window_ID;
                table.ScreenLabel = primary.WindowName;

                /* Deliberately left empty - see the note above. */
                table.WhereClause = "";
            }

            return tables;
        }

        /// <summary>
        /// Which of a table's screens speaks for it.
        ///
        /// Two properties matter, in this order. First, does the tab actually carry the
        /// POSTED BUTTON: a reader following up an unposted-entry row is going there to
        /// post something, so a window that cannot post is the wrong place to send them -
        /// and it is the same window VAS_198 names for the same record, which is what
        /// makes the checklist row and the widget row agree by sight and not only by
        /// count. Second, and only among equals on the first, does the tab carry NO
        /// filter of its own - that is the window showing the whole table rather than one
        /// slice of it.
        ///
        /// The list arrives ordered by window name, so where neither property separates
        /// the candidates the first one wins and the choice stays stable between requests.
        /// </summary>
        /// <param name="screens">A table's screens, in the query's stable order.</param>
        /// <returns>The screen to label, group and zoom by (never null).</returns>
        private ScreenDef PickPrimaryScreen(List<ScreenDef> screens)
        {
            ScreenDef posted = null;
            ScreenDef unfiltered = null;

            for (int s = 0; s < screens.Count; s++)
            {
                ScreenDef screen = screens[s];
                bool noFilter = string.IsNullOrEmpty(screen.WhereClause);

                if (screen.HasPostedField)
                {
                    if (noFilter) { return screen; }
                    if (posted == null) { posted = screen; }
                }
                else if (noFilter && unfiltered == null) { unfiltered = screen; }
            }

            if (posted != null) { return posted; }
            return unfiltered != null ? unfiltered : screens[0];
        }

        /// <summary>
        /// Every active, menu-reachable screen each discovered table is shown on, with
        /// its tab filter already resolved against the session context.
        ///
        /// One window can show a table on more than one tab; the first (lowest SeqNo)
        /// speaks for the window.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="tables">Discovered tables.</param>
        /// <returns>Screens grouped by AD_Table_ID (never null).</returns>
        private Dictionary<int, List<ScreenDef>> ReadScreens(Ctx ctx, List<DocDef> tables)
        {
            Dictionary<int, List<ScreenDef>> byTable = new Dictionary<int, List<ScreenDef>>();

            List<int> tableIds = new List<int>();
            for (int i = 0; i < tables.Count; i++)
            {
                if (tables[i].AD_Table_ID > 0) { tableIds.Add(tables[i].AD_Table_ID); }
            }
            if (tableIds.Count == 0) { return byTable; }

            List<SqlParameter> parameters = Binds();
            parameters.Add(new SqlParameter("@AD_Language", ctxLanguage(ctx)));
            string inList = BuildIdInList(tableIds, "@AD_Table_ID", parameters);

            string sql = @"
                SELECT DISTINCT tab.AD_Table_ID AS AD_Table_ID,
                       tab.AD_Tab_ID AS AD_Tab_ID,
                       tab.SeqNo AS Tab_Seq_No,
                       COALESCE(tab.WhereClause,N'') AS Tab_Where_Clause,
                       w.AD_Window_ID AS AD_Window_ID,
                       COALESCE(wtrl.Name,w.DisplayName,w.Name,N'') AS Window_Name,
                       (SELECT COUNT(1)
                          FROM AD_Field pf
                          INNER JOIN AD_Column pc ON (pc.AD_Column_ID=pf.AD_Column_ID)
                         WHERE pf.AD_Tab_ID=tab.AD_Tab_ID
                           AND pf.IsActive='Y'
                           AND pc.IsActive='Y'
                           AND pc.ColumnName='Posted') AS Posted_Field_Count
                FROM AD_Tab tab
                INNER JOIN AD_Window w ON (w.AD_Window_ID=tab.AD_Window_ID)
                INNER JOIN AD_Menu m ON (m.AD_Window_ID=w.AD_Window_ID)
                LEFT OUTER JOIN AD_Window_Trl wtrl ON (wtrl.AD_Window_ID=w.AD_Window_ID AND wtrl.AD_Language=@AD_Language AND wtrl.IsActive='Y')
                WHERE tab.IsActive='Y'
                  AND tab.IsDisplayed='Y'
                  AND w.IsActive='Y'
                  AND m.IsActive='Y'
                  AND tab.AD_Table_ID IN (" + inList + @")
                ORDER BY tab.AD_Table_ID,Window_Name,tab.SeqNo,w.AD_Window_ID,tab.AD_Tab_ID";

            DataSet ds = DB.ExecuteDataset(sql, parameters.ToArray(), null);
            if (ds == null || ds.Tables.Count == 0) { return byTable; }

            DataTable dt = ds.Tables[0];
            for (int i = 0; i < dt.Rows.Count; i++)
            {
                DataRow dr = dt.Rows[i];

                int tableId = Util.GetValueOfInt(dr["AD_Table_ID"]);
                int windowId = Util.GetValueOfInt(dr["AD_Window_ID"]);
                if (tableId <= 0 || windowId <= 0) { continue; }

                if (!byTable.ContainsKey(tableId)) { byTable[tableId] = new List<ScreenDef>(); }

                List<ScreenDef> screens = byTable[tableId];

                bool seen = false;
                for (int s = 0; s < screens.Count; s++)
                {
                    if (screens[s].AD_Window_ID == windowId) { seen = true; break; }
                }
                if (seen) { continue; }

                ScreenDef screen = new ScreenDef();
                screen.AD_Window_ID = windowId;
                screen.WindowName = Util.GetValueOfString(dr["Window_Name"]);
                screen.WhereClause = ResolveWhereClause(ctx, Util.GetValueOfString(dr["Tab_Where_Clause"]));
                screen.HasPostedField = Util.GetValueOfInt(dr["Posted_Field_Count"]) > 0;

                screens.Add(screen);
            }

            return byTable;
        }

        /// <summary>
        /// Resolves a tab's WhereClause against the SESSION context and returns the
        /// usable text, or "" when it cannot be used here.
        ///
        /// Parsed with window number 0: this widget has no window and no current record,
        /// so only global context resolves - which is the point. A clause carrying only
        /// @#AD_Client_ID@ and its kind works; one needing a window-level variable does
        /// not, and is refused rather than half-resolved, because a half-resolved
        /// predicate does not fail loudly, it silently returns the wrong rows. The
        /// injection checks run on the RESOLVED text, so a context value cannot smuggle
        /// in a terminator or a comment.
        /// </summary>
        /// <param name="ctx">Session context (supplies the global variables).</param>
        /// <param name="clause">AD_Tab.WhereClause, possibly empty.</param>
        /// <returns>The resolved, checked clause, or "".</returns>
        private string ResolveWhereClause(Ctx ctx, string clause)
        {
            if (clause == null) { return ""; }

            string text = clause.Trim();
            if (text.Length == 0 || text.Length > 2000) { return ""; }

            if (text.IndexOf('@') >= 0)
            {
                try
                {
                    text = Env.ParseContext(ctx, 0, text, false);
                }
                catch (Exception ex)
                {
                    Log.Log(Level.WARNING, "VAS_195: a tab WhereClause could not be resolved "
                        + "against the session context and is ignored", ex);
                    return "";
                }

                if (string.IsNullOrEmpty(text)) { return ""; }
                text = text.Trim();
            }

            if (text.IndexOf('@') >= 0) { return ""; }
            if (text.IndexOf(';') >= 0) { return ""; }
            if (text.IndexOf("--", StringComparison.Ordinal) >= 0) { return ""; }
            if (text.IndexOf("/*", StringComparison.Ordinal) >= 0) { return ""; }

            return text;
        }

        /// <summary>
        /// Gives each discovered SCREEN a sort rank in screen-name order.
        ///
        /// The rank exists because the screen label is resolved AFTER the query - the
        /// framework's document resolver fills it in from AD_Window once the page is
        /// materialised - so there is no screen column in the SQL to sort on. Each UNION
        /// branch instead carries its table's rank as a plain integer, which the ORDER BY
        /// can reach. An integer and not the label itself: a name would have to be
        /// escaped into every branch, and a sort key nobody displays has no business
        /// being a string literal in generated SQL.
        ///
        /// The label itself already arrived with the screen, so this is a sort and a
        /// numbering, not another query. A branch whose label could not be resolved
        /// sorts LAST rather than first, so an unnamed screen never leads the list.
        /// </summary>
        /// <param name="docs">Discovered screens, ranked in place.</param>
        private void RankByScreen(List<DocDef> docs)
        {
            if (docs.Count == 0) { return; }

            docs.Sort(delegate (DocDef a, DocDef b)
            {
                bool aBlank = string.IsNullOrEmpty(a.ScreenLabel);
                bool bBlank = string.IsNullOrEmpty(b.ScreenLabel);

                if (aBlank != bBlank) { return aBlank ? 1 : -1; }
                if (aBlank) { return string.Compare(a.TableName, b.TableName, StringComparison.CurrentCultureIgnoreCase); }

                int byLabel = string.Compare(a.ScreenLabel, b.ScreenLabel, StringComparison.CurrentCultureIgnoreCase);
                return byLabel != 0 ? byLabel
                    : string.Compare(a.TableName, b.TableName, StringComparison.CurrentCultureIgnoreCase);
            });

            for (int i = 0; i < docs.Count; i++) { docs[i].ScreenRank = i + 1; }
        }

        /// <summary>Adds one MAX(CASE ...) probe column to the discovery SELECT.</summary>
        /// <param name="select">Statement being built.</param>
        /// <param name="columnName">Column being probed.</param>
        private void AppendProbe(StringBuilder select, string columnName)
        {
            select.Append(",MAX(CASE WHEN c.ColumnName='").Append(columnName)
                  .Append("' THEN c.ColumnName END) AS Col_").Append(columnName);
        }

        /// <summary>
        /// Turns one discovery row into a usable document, or null when the table cannot
        /// be queried safely: no key to zoom to, no date to bound by, or a name that
        /// fails the identifier guard.
        /// </summary>
        /// <param name="row">Discovery result row.</param>
        /// <returns>Populated <see cref="DocDef"/>, or null.</returns>
        private DocDef ReadDiscoveredDoc(DataRow row)
        {
            DocDef def = new DocDef();
            def.AD_Table_ID = Util.GetValueOfInt(row["AD_Table_ID"]);
            def.TableName = Util.GetValueOfString(row["Table_Name"]);
            def.KeyColumn = Util.GetValueOfString(row["Key_Column"]);

            if (!IsSafeIdentifier(def.TableName) || !IsSafeIdentifier(def.KeyColumn)) { return null; }

            def.DateColumn = FirstProbed(row, DateColumnPreference);
            if (!IsSafeIdentifier(def.DateColumn)) { return null; }

            def.AmountColumn = FirstProbed(row, AmountColumnPreference);
            def.HasAmount = IsSafeIdentifier(def.AmountColumn);

            def.HasDocStatus = Probed(row, "DocStatus");
            def.HasPosted = Probed(row, "Posted");
            def.HasProcessed = Probed(row, "Processed");
            def.HasDocumentNo = Probed(row, "DocumentNo");
            def.HasBPartner = Probed(row, "C_BPartner_ID");
            def.HasCurrency = Probed(row, "C_Currency_ID");
            def.DocTypeKey = DocTypeKeyExpr("doc", Probed(row, "C_DocType_ID"), Probed(row, "C_DocTypeTarget_ID"));

            def.IsInventory = IsInventoryTable(def.TableName);

            return def;
        }

        /// <summary>Whether a probed column came back present.</summary>
        /// <param name="row">Discovery result row.</param>
        /// <param name="columnName">Column probed.</param>
        /// <returns>true when the table carries it.</returns>
        private bool Probed(DataRow row, string columnName)
        {
            string alias = "Col_" + columnName;
            if (!row.Table.Columns.Contains(alias)) { return false; }
            return !string.IsNullOrEmpty(Util.GetValueOfString(row[alias]));
        }

        /// <summary>The first column of a preference list the table actually carries.</summary>
        /// <param name="row">Discovery result row.</param>
        /// <param name="preference">Candidate columns, best first.</param>
        /// <returns>The chosen column name, or "".</returns>
        private string FirstProbed(DataRow row, string[] preference)
        {
            for (int i = 0; i < preference.Length; i++)
            {
                if (Probed(row, preference[i])) { return preference[i]; }
            }
            return "";
        }

        /// <summary>Whether a discovered table is one of the inventory movement documents.</summary>
        /// <param name="tableName">Physical table name.</param>
        /// <returns>true when check 15 should examine it.</returns>
        private bool IsInventoryTable(string tableName)
        {
            for (int i = 0; i < InventoryTables.Length; i++)
            {
                if (InventoryTables[i].Equals(tableName, StringComparison.OrdinalIgnoreCase)) { return true; }
            }
            return false;
        }

        /// <summary>
        /// The document-type key of one table, preferring the TARGET type over the
        /// actual one.
        ///
        /// C_Order, C_Invoice, C_Payment and M_InOut carry both: C_DocTypeTarget_ID is
        /// what the user picked and is populated from the moment the document is
        /// drafted, while C_DocType_ID is only set when it completes. Reading
        /// C_DocType_ID alone would therefore leave the Document Type column blank on
        /// precisely the rows check 01 exists to list - the unprocessed ones.
        ///
        /// NULLIF guards the fallback because an unset FK is stored as 0 here, not as
        /// NULL, and COALESCE would happily hand back the zero.
        /// </summary>
        /// <param name="alias">Table alias carrying the columns.</param>
        /// <param name="hasType">Whether the table carries C_DocType_ID.</param>
        /// <param name="hasTarget">Whether the table carries C_DocTypeTarget_ID.</param>
        /// <returns>Join-key expression, or "" when the table has neither column.</returns>
        private string DocTypeKeyExpr(string alias, bool hasType, bool hasTarget)
        {
            if (hasTarget && hasType)
            {
                return "COALESCE(NULLIF(" + alias + ".C_DocTypeTarget_ID,0),NULLIF(" + alias + ".C_DocType_ID,0))";
            }
            if (hasTarget) { return "NULLIF(" + alias + ".C_DocTypeTarget_ID,0)"; }
            if (hasType) { return "NULLIF(" + alias + ".C_DocType_ID,0)"; }

            return "";
        }

        /// <summary>
        /// The discovered documents one check should examine.
        ///
        /// Check 01 takes everything with a DocStatus - "unprocessed" is a statement
        /// about a document workflow, and a table without one has no workflow to be
        /// unfinished in. A Posted-only table is not a draft, it is a posting artefact.
        /// Check 02 takes only what carries a Posted column: a table without one cannot
        /// be judged unposted, and treating its absence as "not posted" would
        /// manufacture failures out of documents that never post at all. It does NOT
        /// require a DocStatus - that requirement is what used to hide M_MatchInv and
        /// M_MatchPO from it while the VAS_198 widget listed them.
        /// Check 15 takes the inventory movement documents, all of which have both.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="inventoryOnly">Restrict to inventory movement documents.</param>
        /// <param name="postedOnly">Restrict to documents carrying a Posted column.</param>
        /// <param name="docStatusOnly">Restrict to documents carrying a DocStatus column.</param>
        /// <returns>Usable documents (never null).</returns>
        private List<DocDef> UsableDocuments(Ctx ctx, bool inventoryOnly, bool postedOnly, bool docStatusOnly)
        {
            List<DocDef> usable = new List<DocDef>();
            List<DocDef> all = DiscoverDocuments(ctx);

            for (int i = 0; i < all.Count; i++)
            {
                DocDef def = all[i];

                if (inventoryOnly && !def.IsInventory) { continue; }
                if (postedOnly && !def.HasPosted) { continue; }
                if (docStatusOnly && !def.HasDocStatus) { continue; }

                usable.Add(def);
            }

            return usable;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Dispatch
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Evaluates one check. The switch is exhaustive over the registry, so adding a
        /// registry entry without a handler is a compile-visible omission rather than a
        /// silently missing row.
        /// </summary>
        /// <param name="c">Shared evaluation context.</param>
        /// <param name="def">Registry entry being evaluated.</param>
        /// <returns>Populated <see cref="CheckResult"/>, or null when unhandled.</returns>
        private CheckResult Evaluate(CheckContext c, CheckDef def)
        {
            switch (def.CheckCode)
            {
                case "MPC_CLOSE_01": return Eval01(c, def);
                case "MPC_CLOSE_02": return Eval02(c, def);
                case "MPC_CLOSE_03": return Eval03(c, def);
                case "MPC_CLOSE_04": return Eval04(c, def);
                case "MPC_CLOSE_05": return Eval05(c, def);
                case "MPC_CLOSE_06": return Eval06(c, def);
                case "MPC_CLOSE_07": return Eval07(c, def);
                case "MPC_CLOSE_08": return Eval08(c, def);
                case "MPC_CLOSE_09": return Eval09(c, def);
                case "MPC_CLOSE_10": return Eval10(c, def);
                case "MPC_CLOSE_11": return Eval11(c, def);
                case "MPC_CLOSE_12": return Eval12(c, def);
                case "MPC_CLOSE_13": return Eval13(c, def);
                case "MPC_CLOSE_14": return Eval14(c, def);
                case "MPC_CLOSE_15": return Eval15(c, def);
                case "MPC_CLOSE_16": return Eval16(c, def);
                case "MPC_CLOSE_17": return Eval17(c, def);
                case "MPC_CLOSE_18": return Eval18(c, def);
                case "MPC_CLOSE_19": return Eval19(c, def);
                case "MPC_CLOSE_20": return Eval20(c, def);
                case "MPC_CLOSE_21": return Eval21(c, def);
                case "MPC_CLOSE_22": return Eval22(c, def);
                case "MPC_CLOSE_23": return Eval23(c, def);
            }
            return null;
        }

        /// <summary>
        /// Builds the detail statement for one check, or null when the check has nothing
        /// to drill into.
        /// </summary>
        /// <param name="c">Shared evaluation context.</param>
        /// <param name="def">Registry entry being opened.</param>
        /// <returns>Populated <see cref="DetailSpec"/>, or null.</returns>
        private DetailSpec BuildDetail(CheckContext c, CheckDef def)
        {
            switch (def.CheckCode)
            {
                case "MPC_CLOSE_01": return Spec01(c);
                case "MPC_CLOSE_02": return Spec02(c);
                case "MPC_CLOSE_03": return Spec03(c);
                case "MPC_CLOSE_04": return Spec04(c);
                case "MPC_CLOSE_05": return Spec05(c);
                case "MPC_CLOSE_06": return Spec06(c);
                case "MPC_CLOSE_07": return Spec07(c);
                case "MPC_CLOSE_08": return Spec08(c);
                case "MPC_CLOSE_09": return Spec09(c);
                case "MPC_CLOSE_10": return Spec10(c);
                case "MPC_CLOSE_11": return Spec11(c);
                case "MPC_CLOSE_12": return Spec12(c);
                case "MPC_CLOSE_14": return Spec14(c);
                case "MPC_CLOSE_15": return Spec15(c);
                case "MPC_CLOSE_16": return Spec16(c);
                case "MPC_CLOSE_17": return Spec17(c);
                case "MPC_CLOSE_18": return Spec18(c);
                /* 19 deliberately absent - see Eval19. The row reports two counts and
                   exposes no records, so there is nothing to page and the endpoint must
                   have nothing to answer with even when asked directly. */
                case "MPC_CLOSE_20": return Spec20(c);
                case "MPC_CLOSE_21": return Spec21(c);
                case "MPC_CLOSE_22": return Spec22(c);
                case "MPC_CLOSE_23": return Spec23(c);
            }

            /* 13 is configuration-driven and has no records until finance maintains its
               expectations - there is nothing to page. */
            return null;
        }

        /// <summary>
        /// The common shape: build the check's one statement, count it, and let the
        /// classification decide what the count means.
        /// </summary>
        /// <param name="c">Shared evaluation context.</param>
        /// <param name="def">Registry entry.</param>
        /// <param name="spec">The check's statement, or null when nothing is queryable.</param>
        /// <param name="summaryKey">AD_Message key for the non-zero summary.</param>
        /// <param name="summaryText">English fallback for the non-zero summary.</param>
        /// <param name="clearKey">AD_Message key for the zero summary.</param>
        /// <param name="clearText">English fallback for the zero summary.</param>
        /// <returns>Populated <see cref="CheckResult"/>.</returns>
        private CheckResult CountSpec(CheckContext c, CheckDef def, DetailSpec spec,
            string summaryKey, string summaryText, string clearKey, string clearText)
        {
            if (spec == null)
            {
                return NotApplicable(def, "VAS_195_NoSource", "No applicable source data in this installation");
            }

            int count = CountOf(c.Ctx, spec);
            return Counted(def, count, summaryKey, summaryText, clearKey, clearText);
        }

        /// <summary>
        /// Applies MRole to ONE branch of a multi-table statement, on that branch's own
        /// main physical alias, while it is still a plain SELECT. Branches are combined
        /// only after each has been secured, and the combination is never re-secured.
        /// </summary>
        /// <param name="c">Shared evaluation context.</param>
        /// <param name="sql">One branch's plain physical-table SELECT.</param>
        /// <param name="alias">That branch's main physical table alias.</param>
        /// <returns>Secured branch.</returns>
        private string SecureBranch(CheckContext c, string sql, string alias)
        {
            return MRole.GetDefault(c.Ctx).AddAccessSQL(sql, alias, MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
        }

        /// <summary>
        /// The period-bound predicate for one date column, plus its two binds. Inclusive
        /// start, exclusive end - so a document carrying a time still lands in its own
        /// period on both backends without TRUNC or a cast.
        /// </summary>
        /// <param name="c">Shared evaluation context.</param>
        /// <param name="alias">Table alias carrying the date column.</param>
        /// <param name="column">Date column name (already validated).</param>
        /// <param name="suffix">Unique bind suffix for this occurrence.</param>
        /// <param name="parameters">Bind list being built, in appearance order.</param>
        /// <returns>Predicate fragment.</returns>
        private string PeriodWhere(CheckContext c, string alias, string column, string suffix,
            List<SqlParameter> parameters)
        {
            parameters.Add(new SqlParameter("@PeriodStart" + suffix, c.PeriodStart));
            parameters.Add(new SqlParameter("@PeriodEnd" + suffix, c.PeriodEndExclusive));

            return alias + "." + column + ">=@PeriodStart" + suffix
                 + " AND " + alias + "." + column + "<@PeriodEnd" + suffix;
        }

        /// <summary>A new bind list seeded with nothing; kept for readability at call sites.</summary>
        /// <returns>Empty bind list.</returns>
        private List<SqlParameter> Binds()
        {
            return new List<SqlParameter>();
        }

        /// <summary>Assembles a DetailSpec.</summary>
        /// <param name="sql">The statement.</param>
        /// <param name="mainAlias">Main physical alias for MRole, or "" when pre-secured.</param>
        /// <param name="orderBy">Sort clause, appended after securing.</param>
        /// <param name="parameters">Binds in appearance order.</param>
        /// <param name="columns">Declared columns.</param>
        /// <returns>Populated <see cref="DetailSpec"/>.</returns>
        private DetailSpec Spec(string sql, string mainAlias, string orderBy,
            List<SqlParameter> parameters, List<ColumnDef> columns)
        {
            DetailSpec spec = new DetailSpec();
            spec.Sql = sql;
            spec.MainAlias = mainAlias;
            spec.OrderBy = orderBy;
            spec.Params = parameters;
            spec.Columns = columns;
            return spec;
        }

        // ─────────────────────────────────────────────────────────────────────
        // 01  Unprocessed documents                                    BLOCKER
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Draft or in-progress posting documents whose effective date falls in the
        /// period. They can still change the period's numbers, so they must be
        /// completed, voided or moved before it closes.
        /// </summary>
        /// <param name="c">Shared evaluation context.</param>
        /// <param name="def">Registry entry.</param>
        /// <returns>Populated <see cref="CheckResult"/>.</returns>
        private CheckResult Eval01(CheckContext c, CheckDef def)
        {
            return CountSpec(c, def, Spec01(c),
                "VAS_195_Sum01", "unprocessed documents can still affect this period",
                "VAS_195_Clr01", "No unprocessed posting documents found");
        }

        /// <summary>
        /// One UNION ALL branch per registered posting table, each secured on its own
        /// alias. A document qualifies when it is active, not voided or reversed, and
        /// either sits in a working DocStatus or is still unprocessed.
        ///
        /// A completed-but-unposted document is deliberately NOT counted here - that is
        /// check 02, and counting it twice would make the checklist look worse than the
        /// books are.
        /// </summary>
        /// <param name="c">Shared evaluation context.</param>
        /// <returns>Populated <see cref="DetailSpec"/>, or null when no table qualifies.</returns>
        private DetailSpec Spec01(CheckContext c)
        {
            List<DocDef> docs = UsableDocuments(c.Ctx, false, false, true);
            if (docs.Count == 0) { return null; }

            List<SqlParameter> parameters = Binds();
            StringBuilder sql = new StringBuilder();

            for (int i = 0; i < docs.Count; i++)
            {
                DocDef d = docs[i];
                string suffix = (i + 1).ToString(CultureInfo.InvariantCulture);

                string a = Alias(d);

                string pending = d.HasProcessed
                    ? "(" + a + ".DocStatus IN (" + DOCSTATUS_OpenList + ") OR COALESCE(" + a + ".Processed,'N')='N')"
                    : a + ".DocStatus IN (" + DOCSTATUS_OpenList + ")";

                string where = a + ".IsActive='Y' AND " + a + ".DocStatus NOT IN (" + DOCSTATUS_DeadList + ")"
                    + " AND " + pending
                    + " AND " + PeriodWhere(c, a, d.DateColumn, suffix, parameters)
                    + ScreenFilter(d);

                string branch = DocBranchSelect(d, suffix, parameters) + " WHERE " + where;

                if (i > 0) { sql.Append(" UNION ALL "); }
                sql.Append(SecureBranch(c, branch, a));
            }

            /* Grouped by screen, newest first inside each group. Screen_Sort is a
               selected column of every branch, which is what lets a UNION's ORDER BY
               reach it. */
            return Spec(sql.ToString(), "", "Screen_Sort,Doc_Date DESC,Doc_Number DESC",
                parameters, DocColumns(false, true));
        }

        /// <summary>
        /// The normalised SELECT/FROM every posting-document branch shares, so the
        /// branches of a UNION really do line up column for column. Columns a given
        /// table does not carry are emitted as typed literals rather than omitted.
        /// </summary>
        /// <param name="d">Discovered screen for this branch.</param>
        /// <param name="suffix">Unique bind suffix for this branch.</param>
        /// <param name="parameters">Bind list being built, in appearance order.</param>
        /// <returns>SELECT ... FROM fragment aliased to the table's own name.</returns>
        private string DocBranchSelect(DocDef d, string suffix, List<SqlParameter> parameters)
        {
            string a = Alias(d);
            StringBuilder sql = new StringBuilder();

            sql.Append("SELECT ").Append(d.AD_Table_ID).Append(" AS ").Append(TECH_TABLE)
               .Append(",").Append(a).Append(".").Append(d.KeyColumn).Append(" AS ").Append(TECH_RECORD)
               /* The screen's OWN window, so the drill-down opens the record on the
                  screen the row came from rather than on the table's default one. */
               .Append(",").Append(d.AD_Window_ID).Append(" AS ").Append(TECH_WINDOW)
               /* Sort key only - never declared as a column, so the page materialiser
                  ignores it and the reader never sees it. */
               .Append(",").Append(d.ScreenRank).Append(" AS Screen_Sort")
               .Append(",").Append(d.HasDocumentNo ? "COALESCE(" + a + ".DocumentNo,N'')" : "N''").Append(" AS Doc_Number")
               .Append(",").Append(a).Append(".").Append(d.DateColumn).Append(" AS Doc_Date")
               /* A Posted-only table has no workflow state to report; the branch still
                  has to line up column for column with its siblings, so it contributes
                  the same typed literal the other optional columns use. */
               .Append(",").Append(d.HasDocStatus ? a + ".DocStatus" : "N''").Append(" AS Doc_Status")
               .Append(",").Append(string.IsNullOrEmpty(d.DocTypeKey) ? "N''" : "COALESCE(dt.Name,N'')").Append(" AS Doc_Type")
               .Append(",COALESCE(org.Name,N'') AS Org_Name")
               .Append(",").Append(d.HasBPartner ? "COALESCE(bp.Name,N'')" : "N''").Append(" AS Partner_Name")
               .Append(",").Append(d.HasCurrency ? "COALESCE(cur.ISO_Code,N'')" : "N''").Append(" AS Currency_Iso")
               .Append(",").Append(d.HasAmount ? "COALESCE(" + a + "." + d.AmountColumn + ",0)" : "0").Append(" AS Doc_Amount")
               .Append(" FROM ").Append(d.TableName).Append(" ").Append(a)
               .Append(" LEFT OUTER JOIN AD_Org org ON (org.AD_Org_ID=").Append(a).Append(".AD_Org_ID)");

            if (!string.IsNullOrEmpty(d.DocTypeKey))
            {
                sql.Append(" LEFT OUTER JOIN C_DocType dt ON (dt.C_DocType_ID=").Append(d.DocTypeKey).Append(")");
            }
            if (d.HasBPartner)
            {
                sql.Append(" LEFT OUTER JOIN C_BPartner bp ON (bp.C_BPartner_ID=").Append(a).Append(".C_BPartner_ID)");
            }
            if (d.HasCurrency)
            {
                sql.Append(" LEFT OUTER JOIN C_Currency cur ON (cur.C_Currency_ID=").Append(a).Append(".C_Currency_ID)");
            }

            return sql.ToString();
        }

        /// <summary>
        /// The alias every branch gives its source table.
        ///
        /// A short fixed alias, because no branch carries a tab WhereClause any more.
        /// Were one ever reintroduced, this would have to become the table's own name -
        /// a tab clause qualifies its columns with the TABLE name ("C_Order.IsSOTrx"),
        /// which an alias of "doc" cannot resolve. That is why VAS_197 aliases each
        /// table to itself, and why this method exists rather than the string being
        /// written out at each of the three call sites.
        /// </summary>
        /// <param name="d">Discovered document.</param>
        /// <returns>The alias, which is also what MRole is applied to.</returns>
        private string Alias(DocDef d)
        {
            return "doc";
        }

        /// <summary>
        /// The screen's own filter. Always empty today - see the note on
        /// <see cref="ExpandByScreen"/> - and kept as the single place a per-screen
        /// predicate would be reintroduced once its behaviour has been verified against
        /// real AD_Tab data.
        /// </summary>
        /// <param name="d">Discovered document.</param>
        /// <returns>Predicate fragment, or "" when there is no filter.</returns>
        private string ScreenFilter(DocDef d)
        {
            if (string.IsNullOrEmpty(d.WhereClause)) { return ""; }
            return " AND (" + d.WhereClause + ")";
        }

        /// <summary>
        /// The shared column set of the document checks.
        ///
        /// NO check declares the Screen column any more, and all three sort by it: the
        /// rows arrive grouped screen by screen, and a column repeating the same value
        /// down each group earns less than the width it costs. The screen name goes on a
        /// second line inside the document cell instead - see the client's docCell -
        /// which costs no column width at all and still tells a reader landing mid-list
        /// which screen a record belongs to.
        ///
        /// CURRENCY is optional because it is not merely narrow on the inventory check,
        /// it is EMPTY: M_InOut, M_Inventory and M_Movement carry no C_Currency_ID, so
        /// the branch builder emits a literal for every one of their rows and the column
        /// renders blank down its whole length. A column that can never have a value on
        /// the check that declares it is not a thin column, it is a wrong one.
        /// </summary>
        /// <param name="withScreen">Whether to declare the Screen column. Every caller
        /// passes false today; the parameter stays because the client still renders
        /// COLTYPE_SCREEN, so restoring the column on one check is a one-word change
        /// rather than a rendering change.</param>
        /// <param name="withCurrency">Whether the check's tables can carry a currency.</param>
        /// <returns>Declared columns.</returns>
        private List<ColumnDef> DocColumns(bool withScreen, bool withCurrency)
        {
            List<ColumnDef> columns = new List<ColumnDef>();
            if (withScreen) { columns.Add(Col("Screen", "VAS_195_Screen", "Screen", COLTYPE_SCREEN, 1.2m)); }

            /* Wider without a Screen column of its own: the document cell then carries
               the screen name on a second line, and a window name is routinely longer
               than the document number above it. */
            columns.Add(Col("Doc_Number", "DocumentNo", "Document No", COLTYPE_DOC,
                withScreen ? 1.2m : 1.7m));
            columns.Add(Col("Doc_Date", "VAS_195_AccountDate", "Account Date", COLTYPE_DATE, 0.9m));
            columns.Add(Col("Doc_Type", "VAS_195_DocumentType", "Document Type", COLTYPE_TEXT, 1.1m));
            columns.Add(Col("Doc_Status", "VAS_195_DocStatus", "Status", COLTYPE_BADGE, 0.8m));
            columns.Add(Col("Org_Name", "VAS_195_Organization", "Organization", COLTYPE_TEXT, 1.0m));
            columns.Add(Col("Partner_Name", "VAS_195_BusinessPartner", "Business Partner", COLTYPE_TEXT, 1.3m));
            if (withCurrency) { columns.Add(Col("Currency_Iso", "VAS_195_Currency", "Currency", COLTYPE_TEXT, 0.5m)); }
            columns.Add(Col("Doc_Amount", "VAS_195_Amount", "Amount", COLTYPE_DOCAMOUNT, 1.0m));
            return columns;
        }

        // ─────────────────────────────────────────────────────────────────────
        // 02  Unposted accounting entries                              BLOCKER
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Completed or closed documents that never reached the ledger. The period's
        /// numbers are incomplete until they do.
        /// </summary>
        /// <param name="c">Shared evaluation context.</param>
        /// <param name="def">Registry entry.</param>
        /// <returns>Populated <see cref="CheckResult"/>.</returns>
        private CheckResult Eval02(CheckContext c, CheckDef def)
        {
            return CountSpec(c, def, Spec02(c),
                "VAS_195_Sum02", "completed documents are not posted to the primary ledger",
                "VAS_195_Clr02", "All completed accounting documents are posted");
        }

        /// <summary>
        /// One secured branch per posting table that carries a Posted column - a table
        /// without one cannot be judged unposted, and guessing would manufacture
        /// failures.
        ///
        /// A DocStatus is NOT required, and the completed-state test is applied only to
        /// the tables that have one. This is what makes the check agree with the VAS_198
        /// widget, which reads the same way: M_MatchInv and M_MatchPO post to the ledger
        /// and carry a Posted button, but they have no document workflow at all, so
        /// demanding DocStatus IN ('CO','CL') of them excluded every one of their
        /// unposted rows from a checklist whose whole job is to find them. For a table
        /// with no workflow, "exists and is not posted" IS the actionable condition.
        /// </summary>
        /// <param name="c">Shared evaluation context.</param>
        /// <returns>Populated <see cref="DetailSpec"/>, or null when no table qualifies.</returns>
        private DetailSpec Spec02(CheckContext c)
        {
            List<DocDef> docs = UsableDocuments(c.Ctx, false, true, false);
            if (docs.Count == 0) { return null; }

            List<SqlParameter> parameters = Binds();
            StringBuilder sql = new StringBuilder();

            for (int i = 0; i < docs.Count; i++)
            {
                DocDef d = docs[i];
                string suffix = (i + 1).ToString(CultureInfo.InvariantCulture);

                string a = Alias(d);

                string completed = d.HasDocStatus
                    ? a + ".DocStatus IN (" + DOCSTATUS_FinalList + ") AND "
                    : "";

                string where = a + ".IsActive='Y' AND " + completed
                    + "COALESCE(" + a + ".Posted,'N')<>'Y'"
                    + " AND " + PeriodWhere(c, a, d.DateColumn, suffix, parameters)
                    + ScreenFilter(d);

                string branch = DocBranchSelect(d, suffix, parameters) + " WHERE " + where;

                if (i > 0) { sql.Append(" UNION ALL "); }
                sql.Append(SecureBranch(c, branch, a));
            }

            /* Grouped by screen, newest first inside each group. Screen_Sort is a
               selected column of every branch, which is what lets a UNION's ORDER BY
               reach it. */
            return Spec(sql.ToString(), "", "Screen_Sort,Doc_Date DESC,Doc_Number DESC",
                parameters, DocColumns(false, true));
        }

        // ─────────────────────────────────────────────────────────────────────
        // 03  Payment allocation status                                WARNING
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Settled payments still carrying an unallocated amount. Often legitimate -
        /// advances and on-account receipts look exactly like this - which is why it
        /// warns rather than blocks.
        /// </summary>
        /// <param name="c">Shared evaluation context.</param>
        /// <param name="def">Registry entry.</param>
        /// <returns>Populated <see cref="CheckResult"/>.</returns>
        private CheckResult Eval03(CheckContext c, CheckDef def)
        {
            return CountSpec(c, def, Spec03(c),
                "VAS_195_Sum03", "settlement payments are unallocated or partially allocated",
                "VAS_195_Clr03", "All settlement payments are allocated");
        }

        /// <summary>
        /// Completed SETTLEMENT payments in the period that are not allocated - the same
        /// population the VAS_199 widget reports under "Settlement, not allocated", read
        /// the same way.
        ///
        /// IsAllocated IS THE ANSWER. This check used to re-derive allocation by summing
        /// C_AllocationLine.Amount and comparing it against PayAmt, and that is what made
        /// it disagree with VAS_199. An allocation line carries more than Amount -
        /// DiscountAmt, WriteOffAmt and OverUnderAmt are settlement too - so an invoice
        /// paid short by an agreed discount allocates in full, sets IsAllocated='Y', and
        /// still sums to less than PayAmt. The sum-and-compare test flagged exactly those
        /// payments as outstanding when nothing was outstanding at all. C_Payment's own
        /// completeness flag is maintained by the allocation process itself and is the
        /// authoritative answer; VAS_199 says so in its own header, and this check now
        /// follows it rather than second-guessing it.
        ///
        /// ADVANCES ARE NOT SETTLEMENT. A prepayment, and a payment against a charge
        /// flagged IsAdvanceCharge, are unallocated by their nature and forever - they
        /// are money received before there is anything to allocate against. VAS_199 gives
        /// them their own bucket and keeps them out of the settlement figure. This check
        /// never excluded them, so every advance on the books inflated it. Its own summary
        /// line has always said "settlement payments", and the doc note below has always
        /// said advances are why it warns rather than blocks - the predicate simply never
        /// matched the words.
        ///
        /// Two further filters were dropped to match: a currency tolerance, which has no
        /// meaning once the test is a flag rather than a subtraction, and a
        /// COALESCE(IsReversal,'N')='N' exclusion that VAS_199 does not apply.
        ///
        /// The period bounds stay in <see cref="PeriodWhere"/>'s half-open form rather
        /// than VAS_199's TRUNC/CAST form. They select the same rows - both cover the
        /// whole of the last day - and the half-open form leaves an index on DateAcct
        /// usable, which a function on the column does not.
        /// </summary>
        /// <param name="c">Shared evaluation context.</param>
        /// <returns>Populated <see cref="DetailSpec"/>.</returns>
        private DetailSpec Spec03(CheckContext c)
        {
            List<SqlParameter> parameters = Binds();

            bool hasPrepayment = ColumnExists("C_Payment", "IsPrepayment");
            bool hasCharge = ColumnExists("C_Payment", "C_Charge_ID");
            bool hasAdvanceCharge = hasCharge && ColumnExists("C_Charge", "IsAdvanceCharge");

            /* Target type first - see DocTypeKeyExpr. A payment carries both. */
            string docTypeKey = DocTypeKeyExpr("p",
                ColumnExists("C_Payment", "C_DocType_ID"),
                ColumnExists("C_Payment", "C_DocTypeTarget_ID"));

            /* The two advance forms, each applied only where its column exists. A schema
               without them cannot separate advances from settlements, and the check then
               reports the wider population rather than failing - the same degradation
               every optional-column probe in this file makes. */
            string notAdvance = "";
            if (hasPrepayment) { notAdvance += " AND COALESCE(p.IsPrepayment,'N')<>'Y'"; }
            if (hasAdvanceCharge) { notAdvance += " AND COALESCE(ch.IsAdvanceCharge,'N')<>'Y'"; }

            string sql = @"
                SELECT " + TableId("C_Payment") + " AS " + TECH_TABLE + @",
                       p.C_Payment_ID AS " + TECH_RECORD + @",
                       0 AS " + TECH_WINDOW + @",
                       COALESCE(p.DocumentNo,N'') AS Doc_Number,
                       p.DateAcct AS Doc_Date,
                       " + (string.IsNullOrEmpty(docTypeKey) ? "N''" : "COALESCE(dt.Name,N'')") + @" AS Doc_Type,
                       COALESCE(bp.Name,N'') AS Partner_Name,
                       " + (hasCharge ? "COALESCE(ch.Name,N'')" : "N''") + @" AS Charge_Name,
                       COALESCE(cur.ISO_Code,N'') AS Currency_Iso,
                       COALESCE(p.PayAmt,0) AS Pay_Amount
                FROM C_Payment p
                " + (string.IsNullOrEmpty(docTypeKey)
                        ? ""
                        : "INNER JOIN C_DocType dt ON (dt.C_DocType_ID=" + docTypeKey + ")") + @"
                " + (hasCharge ? "LEFT OUTER JOIN C_Charge ch ON (ch.C_Charge_ID=p.C_Charge_ID)" : "") + @"
                LEFT OUTER JOIN C_BPartner bp ON (bp.C_BPartner_ID=p.C_BPartner_ID)
                INNER JOIN C_Currency cur ON (cur.C_Currency_ID=p.C_Currency_ID)
                WHERE p.IsActive='Y'
                  AND p.DocStatus IN (" + DOCSTATUS_FinalList + @")
                  AND " + PeriodWhere(c, "p", "DateAcct", "P", parameters) + @"
                  AND COALESCE(p.IsAllocated,'N')<>'Y'" + notAdvance;

            List<ColumnDef> columns = new List<ColumnDef>();
            columns.Add(Col("Doc_Number", "VAS_195_PaymentNo", "Payment No", COLTYPE_DOC, 1.2m));
            columns.Add(Col("Doc_Date", "VAS_195_AccountDate", "Account Date", COLTYPE_DATE, 0.9m));
            columns.Add(Col("Doc_Type", "VAS_195_PaymentType", "Payment Type", COLTYPE_TEXT, 1.2m));
            columns.Add(Col("Partner_Name", "VAS_195_BusinessPartner", "Business Partner", COLTYPE_TEXT, 1.4m));
            columns.Add(Col("Charge_Name", "VAS_195_Charge", "Charge", COLTYPE_TEXT, 1.1m));
            columns.Add(Col("Currency_Iso", "VAS_195_Currency", "Currency", COLTYPE_TEXT, 0.5m));
            columns.Add(Col("Pay_Amount", "VAS_195_PaymentAmount", "Payment Amount", COLTYPE_DOCAMOUNT, 1.1m));

            return Spec(sql, "p", "p.DateAcct DESC,p.C_Payment_ID DESC", parameters, columns);
        }

        // ─────────────────────────────────────────────────────────────────────
        // 04  Bank reconciliation                                      WARNING
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Bank items still unreconciled at period end. Open items are normal in a live
        /// bank account; what matters is that finance has looked at them.
        /// </summary>
        /// <param name="c">Shared evaluation context.</param>
        /// <param name="def">Registry entry.</param>
        /// <returns>Populated <see cref="CheckResult"/>.</returns>
        private CheckResult Eval04(CheckContext c, CheckDef def)
        {
            return CountSpec(c, def, Spec04(c),
                "VAS_195_Sum04", "bank or statement items remain unreconciled",
                "VAS_195_Clr04", "No unreconciled bank items found");
        }

        /// <summary>
        /// Two secured branches: unreconciled bank payments, and statement lines that
        /// resolve to no document at all.
        ///
        /// The statement branch deliberately requires BOTH references to be null. A line
        /// already pointing at a payment is the SAME reconciliation item as that payment
        /// and would otherwise be counted twice - once here and once in the payment
        /// branch - inflating the number finance is asked to review.
        /// </summary>
        /// <param name="c">Shared evaluation context.</param>
        /// <returns>Populated <see cref="DetailSpec"/>.</returns>
        private DetailSpec Spec04(CheckContext c)
        {
            List<SqlParameter> parameters = Binds();
            StringBuilder sql = new StringBuilder();

            string payments = @"
                SELECT " + TableId("C_Payment") + " AS " + TECH_TABLE + @",
                       p.C_Payment_ID AS " + TECH_RECORD + @",
                       0 AS " + TECH_WINDOW + @",
                       COALESCE(ba.Name,bank.Name,N'') AS Bank_Account,
                       'PAYMENT' AS Source_Type,
                       COALESCE(p.DocumentNo,N'') AS Doc_Number,
                       p.DateTrx AS Trx_Date,
                       p.DateAcct AS Doc_Date,
                       COALESCE(bp.Name,N'') AS Partner_Name,
                       COALESCE(cur.ISO_Code,N'') AS Currency_Iso,
                       COALESCE(p.PayAmt,0) AS Doc_Amount
                FROM C_Payment p
                LEFT OUTER JOIN C_BankAccount ba ON (ba.C_BankAccount_ID=p.C_BankAccount_ID)
                LEFT OUTER JOIN C_Bank bank ON (bank.C_Bank_ID=ba.C_Bank_ID)
                LEFT OUTER JOIN C_BPartner bp ON (bp.C_BPartner_ID=p.C_BPartner_ID)
                LEFT OUTER JOIN C_Currency cur ON (cur.C_Currency_ID=p.C_Currency_ID)
                WHERE p.IsActive='Y'
                  AND p.DocStatus IN (" + DOCSTATUS_FinalList + @")
                  AND COALESCE(p.IsReversal,'N')='N'
                  AND p.C_BankAccount_ID IS NOT NULL
                  AND COALESCE(p.IsReconciled,'N')<>'Y'
                  AND " + PeriodWhere(c, "p", "DateAcct", "P", parameters);

            sql.Append(SecureBranch(c, payments, "p"));

            /* The statement branch only exists where the module's tables do. */
            if (TableExists("C_BankStatementLine") && ColumnExists("C_BankStatementLine", "C_Payment_ID"))
            {
                bool hasInvoice = ColumnExists("C_BankStatementLine", "C_Invoice_ID");

                string lines = @"
                SELECT " + TableId("C_BankStatementLine") + " AS " + TECH_TABLE + @",
                       bsl.C_BankStatementLine_ID AS " + TECH_RECORD + @",
                       0 AS " + TECH_WINDOW + @",
                       COALESCE(ba.Name,N'') AS Bank_Account,
                       'STATEMENT' AS Source_Type,
                       COALESCE(bs.DocumentNo,N'') AS Doc_Number,
                       bsl.StatementLineDate AS Trx_Date,
                       bsl.DateAcct AS Doc_Date,
                       COALESCE(bp.Name,N'') AS Partner_Name,
                       COALESCE(cur.ISO_Code,N'') AS Currency_Iso,
                       COALESCE(bsl.StmtAmt,0) AS Doc_Amount
                FROM C_BankStatementLine bsl
                INNER JOIN C_BankStatement bs ON (bs.C_BankStatement_ID=bsl.C_BankStatement_ID)
                LEFT OUTER JOIN C_BankAccount ba ON (ba.C_BankAccount_ID=bs.C_BankAccount_ID)
                LEFT OUTER JOIN C_BPartner bp ON (bp.C_BPartner_ID=bsl.C_BPartner_ID)
                LEFT OUTER JOIN C_Currency cur ON (cur.C_Currency_ID=bsl.C_Currency_ID)
                WHERE bsl.IsActive='Y'
                  AND bs.IsActive='Y'
                  AND bsl.C_Payment_ID IS NULL"
                  + (hasInvoice ? " AND bsl.C_Invoice_ID IS NULL" : "")
                  + " AND " + PeriodWhere(c, "bsl", "DateAcct", "B", parameters);

                sql.Append(" UNION ALL ").Append(SecureBranch(c, lines, "bsl"));
            }

            List<ColumnDef> columns = new List<ColumnDef>();
            columns.Add(Col("Bank_Account", "VAS_195_BankAccount", "Bank Account", COLTYPE_TEXT, 1.2m));
            columns.Add(Col("Source_Type", "VAS_195_SourceType", "Source", COLTYPE_BADGE, 0.8m));
            columns.Add(Col("Doc_Number", "DocumentNo", "Document No", COLTYPE_DOC, 1.1m));
            columns.Add(Col("Trx_Date", "VAS_195_TransactionDate", "Transaction Date", COLTYPE_DATE, 0.9m));
            columns.Add(Col("Doc_Date", "VAS_195_AccountDate", "Account Date", COLTYPE_DATE, 0.9m));
            columns.Add(Col("Partner_Name", "VAS_195_BusinessPartner", "Business Partner", COLTYPE_TEXT, 1.2m));
            columns.Add(Col("Currency_Iso", "VAS_195_Currency", "Currency", COLTYPE_TEXT, 0.5m));
            columns.Add(Col("Doc_Amount", "VAS_195_Amount", "Amount", COLTYPE_DOCAMOUNT, 1.0m));

            return Spec(sql.ToString(), "", "Doc_Date DESC,Doc_Number DESC", parameters, columns);
        }

        // ─────────────────────────────────────────────────────────────────────
        // 05  In-progress / bounced payments                           WARNING
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Payments stuck mid-execution, or returned by the bank. Operational exceptions
        /// rather than ledger errors, so they warn.
        /// </summary>
        /// <param name="c">Shared evaluation context.</param>
        /// <param name="def">Registry entry.</param>
        /// <returns>Populated <see cref="CheckResult"/>.</returns>
        private CheckResult Eval05(CheckContext c, CheckDef def)
        {
            return CountSpec(c, def, Spec05(c),
                "VAS_195_Sum05", "payments are in progress, bounced, or rejected",
                "VAS_195_Clr05", "No in-progress or bounced payments found");
        }

        /// <summary>
        /// Payments in the period that are either in a working DocStatus or carry a
        /// VA009 execution status of In-Progress, Bounced or Rejected. The execution
        /// column only exists where the VA009 module is installed, so it is probed and
        /// the predicate degrades to the DocStatus test alone.
        /// </summary>
        /// <param name="c">Shared evaluation context.</param>
        /// <returns>Populated <see cref="DetailSpec"/>.</returns>
        private DetailSpec Spec05(CheckContext c)
        {
            List<SqlParameter> parameters = Binds();
            bool hasExec = ColumnExists("C_Payment", COLUMN_EXECUTION_STATUS);

            string execExpr = hasExec ? "COALESCE(p." + COLUMN_EXECUTION_STATUS + ",N'')" : "N''";
            string exception = hasExec
                ? "(p.DocStatus IN ('DR','IP','WP','WC') OR p." + COLUMN_EXECUTION_STATUS + " IN ('I','B','C'))"
                : "p.DocStatus IN ('DR','IP','WP','WC')";

            string sql = @"
                SELECT " + TableId("C_Payment") + " AS " + TECH_TABLE + @",
                       p.C_Payment_ID AS " + TECH_RECORD + @",
                       0 AS " + TECH_WINDOW + @",
                       COALESCE(p.DocumentNo,N'') AS Doc_Number,
                       p.DateAcct AS Doc_Date,
                       COALESCE(bp.Name,N'') AS Partner_Name,
                       COALESCE(p.TenderType,N'') AS Tender_Type,
                       COALESCE(cur.ISO_Code,N'') AS Currency_Iso,
                       COALESCE(p.PayAmt,0) AS Doc_Amount,
                       p.DocStatus AS Doc_Status,
                       " + execExpr + @" AS Exec_Status
                FROM C_Payment p
                LEFT OUTER JOIN C_BPartner bp ON (bp.C_BPartner_ID=p.C_BPartner_ID)
                LEFT OUTER JOIN C_Currency cur ON (cur.C_Currency_ID=p.C_Currency_ID)
                WHERE p.IsActive='Y'
                  AND " + exception + @"
                  AND " + PeriodWhere(c, "p", "DateAcct", "P", parameters);

            List<ColumnDef> columns = new List<ColumnDef>();
            columns.Add(Col("Doc_Number", "VAS_195_PaymentNo", "Payment No", COLTYPE_DOC, 1.1m));
            columns.Add(Col("Doc_Date", "VAS_195_AccountDate", "Account Date", COLTYPE_DATE, 0.9m));
            columns.Add(Col("Partner_Name", "VAS_195_BusinessPartner", "Business Partner", COLTYPE_TEXT, 1.3m));
            columns.Add(Col("Tender_Type", "VAS_195_TenderType", "Tender Type", COLTYPE_TEXT, 0.8m));
            columns.Add(Col("Currency_Iso", "VAS_195_Currency", "Currency", COLTYPE_TEXT, 0.5m));
            columns.Add(Col("Doc_Amount", "VAS_195_Amount", "Amount", COLTYPE_DOCAMOUNT, 1.0m));
            columns.Add(Col("Doc_Status", "VAS_195_DocStatus", "Status", COLTYPE_BADGE, 0.8m));
            columns.Add(Col("Exec_Status", "VAS_195_ExecStatus", "Execution", COLTYPE_BADGE, 0.8m));

            return Spec(sql, "p", "p.DateAcct DESC,p.C_Payment_ID DESC", parameters, columns);
        }

        // ─────────────────────────────────────────────────────────────────────
        // 06  GRNs not invoiced                                        WARNING
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Goods received but not yet matched to a purchase invoice - typically a valid
        /// accrued liability, which is why it warns rather than blocks.
        /// </summary>
        /// <param name="c">Shared evaluation context.</param>
        /// <param name="def">Registry entry.</param>
        /// <returns>Populated <see cref="CheckResult"/>.</returns>
        private CheckResult Eval06(CheckContext c, CheckDef def)
        {
            return CountSpec(c, def, Spec06(c),
                "VAS_195_Sum06", "receipt lines are received but not invoiced",
                "VAS_195_Clr06", "All received goods are matched to invoices");
        }

        /// <summary>
        /// Receipt LINES that no invoice has touched at all.
        ///
        /// NOT "lines with something left to invoice". A partially invoiced line -
        /// received 50, invoiced 45 - is a QUANTITY VARIANCE, and check 08 already
        /// reports it as one. Listing it here too would have the same receipt line
        /// counted under two different findings, inflating both and leaving the reader
        /// to work out that they are the same 5 units. The boundary is therefore drawn
        /// at zero matched quantity: nothing invoiced belongs here, anything partially
        /// invoiced belongs to check 08.
        ///
        /// The test is on the matching records rather than on M_InOutLine.IsInvoiced,
        /// which a partial invoice also sets - a flag-based test could not tell "some"
        /// from "all". Absolute values so a return line cannot net a receipt line away,
        /// and SUM rather than EXISTS so a degenerate zero-quantity match row still
        /// reads as unmatched.
        /// </summary>
        /// <param name="c">Shared evaluation context.</param>
        /// <returns>Populated <see cref="DetailSpec"/>, or null when matching is absent.</returns>
        private DetailSpec Spec06(CheckContext c)
        {
            if (!TableExists("M_MatchInv")) { return null; }

            List<SqlParameter> parameters = Binds();

            string matched = MatchedQtyExpr("iol");

            string sql = @"
                SELECT " + TableId("M_InOut") + " AS " + TECH_TABLE + @",
                       io.M_InOut_ID AS " + TECH_RECORD + @",
                       0 AS " + TECH_WINDOW + @",
                       COALESCE(io.DocumentNo,N'') AS Doc_Number,
                       io.MovementDate AS Trx_Date,
                       io.DateAcct AS Doc_Date,
                       COALESCE(bp.Name,N'') AS Partner_Name,
                       COALESCE(prod.Name,N'') AS Product_Name,
                       COALESCE(uom.UOMSymbol,N'') AS Uom_Symbol,
                       ABS(COALESCE(iol.MovementQty,0)) AS Received_Qty
                FROM M_InOutLine iol
                INNER JOIN M_InOut io ON (io.M_InOut_ID=iol.M_InOut_ID)
                LEFT OUTER JOIN C_BPartner bp ON (bp.C_BPartner_ID=io.C_BPartner_ID)
                LEFT OUTER JOIN M_Product prod ON (prod.M_Product_ID=iol.M_Product_ID)
                LEFT OUTER JOIN C_UOM uom ON (uom.C_UOM_ID=iol.C_UOM_ID)
                WHERE iol.IsActive='Y'
                  AND io.IsActive='Y'
                  AND io.IsSOTrx='N'
                  AND COALESCE(io.IsReturnTrx,'N')='N'
                  AND io.DocStatus IN (" + DOCSTATUS_FinalList + @")
                  AND iol.M_Product_ID IS NOT NULL
                  AND COALESCE(iol.MovementQty,0)<>0
                  AND " + PeriodWhere(c, "io", "DateAcct", "I", parameters) + @"
                  AND " + matched + "<=" + QTY_TOLERANCE;

            /* No Matched / Unmatched columns: on this list matched is zero by
               definition and unmatched is the received quantity, so both would be
               restating the column beside them. */
            List<ColumnDef> columns = new List<ColumnDef>();
            columns.Add(Col("Doc_Number", "VAS_195_GrnNo", "GRN No", COLTYPE_DOC, 1.1m));
            columns.Add(Col("Trx_Date", "VAS_195_ReceiptDate", "Receipt Date", COLTYPE_DATE, 0.9m));
            columns.Add(Col("Doc_Date", "VAS_195_AccountDate", "Account Date", COLTYPE_DATE, 0.9m));
            columns.Add(Col("Partner_Name", "VAS_195_BusinessPartner", "Business Partner", COLTYPE_TEXT, 1.4m));
            columns.Add(Col("Product_Name", "VAS_195_Product", "Product", COLTYPE_TEXT, 1.5m));
            columns.Add(Col("Uom_Symbol", "VAS_195_Uom", "UOM", COLTYPE_TEXT, 0.5m));
            columns.Add(Col("Received_Qty", "VAS_195_ReceivedQty", "Received", COLTYPE_QTY, 0.8m));

            return Spec(sql, "iol", "io.DateAcct DESC,io.DocumentNo DESC,iol.M_InOutLine_ID DESC",
                parameters, columns);
        }

        /// <summary>
        /// Total quantity matched against one receipt line, across every invoice line
        /// that touches it.
        ///
        /// Shared by checks 06 and 08 so the two agree on where "not invoiced" ends and
        /// "invoiced, but not for the full quantity" begins - the boundary between them
        /// is this one number, and two separate expressions for it would eventually
        /// drift and either double-count a line or lose it between the checks.
        /// </summary>
        /// <param name="alias">M_InOutLine alias.</param>
        /// <returns>SQL expression yielding the matched quantity (never null).</returns>
        private string MatchedQtyExpr(string alias)
        {
            bool hasProcessed = ColumnExists("M_MatchInv", "Processed");

            return @"COALESCE((SELECT SUM(ABS(COALESCE(mim.Qty,0))) FROM M_MatchInv mim
                    WHERE mim.M_InOutLine_ID=" + alias + @".M_InOutLine_ID
                      AND mim.IsActive='Y'"
                  + (hasProcessed ? " AND COALESCE(mim.Processed,'Y')='Y'" : "") + "),0)";
        }

        // ─────────────────────────────────────────────────────────────────────
        // 07  Invoices without GRN                                     WARNING
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Purchase invoice lines for goods that no receipt covers. Service and non-stock
        /// lines are excluded, so what remains is genuinely a three-way-match gap.
        /// </summary>
        /// <param name="c">Shared evaluation context.</param>
        /// <param name="def">Registry entry.</param>
        /// <returns>Populated <see cref="CheckResult"/>.</returns>
        private CheckResult Eval07(CheckContext c, CheckDef def)
        {
            return CountSpec(c, def, Spec07(c),
                "VAS_195_Sum07", "invoice lines are not matched to a receipt",
                "VAS_195_Clr07", "All item invoice lines are matched to receipts");
        }

        /// <summary>
        /// Invoice LINES carrying a product whose invoiced quantity exceeds the matched
        /// receipt quantity.
        ///
        /// The product test is what keeps this honest: a service or charge line has no
        /// M_Product_ID and legitimately never has a receipt, so flagging it purely
        /// because M_InOutLine_ID is null would fill the list with non-findings. The
        /// invoice's own MatchRequirementI is reported so the reviewer can see whether
        /// matching was even mandatory for that document.
        /// </summary>
        /// <param name="c">Shared evaluation context.</param>
        /// <returns>Populated <see cref="DetailSpec"/>, or null when matching is absent.</returns>
        private DetailSpec Spec07(CheckContext c)
        {
            if (!TableExists("M_MatchInv")) { return null; }

            List<SqlParameter> parameters = Binds();
            bool hasProcessed = ColumnExists("M_MatchInv", "Processed");
            bool hasMatchReq = ColumnExists("C_Invoice", "MatchRequirementI");

            string matched = @"COALESCE((SELECT SUM(ABS(COALESCE(mi.Qty,0))) FROM M_MatchInv mi
                    WHERE mi.C_InvoiceLine_ID=il.C_InvoiceLine_ID
                      AND mi.IsActive='Y'"
                  + (hasProcessed ? " AND COALESCE(mi.Processed,'Y')='Y'" : "") + "),0)";

            string unmatched = "(ABS(COALESCE(il.QtyInvoiced,0))-" + matched + ")";

            string sql = @"
                SELECT " + TableId("C_Invoice") + " AS " + TECH_TABLE + @",
                       i.C_Invoice_ID AS " + TECH_RECORD + @",
                       0 AS " + TECH_WINDOW + @",
                       COALESCE(i.DocumentNo,N'') AS Doc_Number,
                       i.DateInvoiced AS Trx_Date,
                       i.DateAcct AS Doc_Date,
                       COALESCE(bp.Name,N'') AS Partner_Name,
                       COALESCE(prod.Name,N'') AS Product_Name,
                       ABS(COALESCE(il.QtyInvoiced,0)) AS Invoiced_Qty,
                       " + matched + @" AS Matched_Qty,
                       " + unmatched + @" AS Unmatched_Qty,
                       " + (hasMatchReq ? "COALESCE(i.MatchRequirementI,N'')" : "N''") + @" AS Match_Requirement,
                       COALESCE(cur.ISO_Code,N'') AS Currency_Iso,
                       COALESCE(il.LineNetAmt,0) AS Doc_Amount
                FROM C_InvoiceLine il
                INNER JOIN C_Invoice i ON (i.C_Invoice_ID=il.C_Invoice_ID)
                LEFT OUTER JOIN C_BPartner bp ON (bp.C_BPartner_ID=i.C_BPartner_ID)
                LEFT OUTER JOIN M_Product prod ON (prod.M_Product_ID=il.M_Product_ID)
                LEFT OUTER JOIN C_Currency cur ON (cur.C_Currency_ID=i.C_Currency_ID)
                WHERE il.IsActive='Y'
                  AND i.IsActive='Y'
                  AND i.IsSOTrx='N'
                  AND COALESCE(i.IsReturnTrx,'N')='N'
                  AND i.DocStatus IN (" + DOCSTATUS_FinalList + @")
                  AND il.M_Product_ID IS NOT NULL
                  AND COALESCE(il.QtyInvoiced,0)<>0
                  AND " + PeriodWhere(c, "i", "DateAcct", "I", parameters) + @"
                  AND " + unmatched + ">" + QTY_TOLERANCE;

            List<ColumnDef> columns = new List<ColumnDef>();
            columns.Add(Col("Doc_Number", "VAS_195_InvoiceNo", "Invoice No", COLTYPE_DOC, 1.1m));
            columns.Add(Col("Trx_Date", "DateInvoiced", "Invoice Date", COLTYPE_DATE, 0.9m));
            columns.Add(Col("Doc_Date", "VAS_195_AccountDate", "Account Date", COLTYPE_DATE, 0.9m));
            columns.Add(Col("Partner_Name", "VAS_195_BusinessPartner", "Business Partner", COLTYPE_TEXT, 1.2m));
            columns.Add(Col("Product_Name", "VAS_195_Product", "Product", COLTYPE_TEXT, 1.3m));
            columns.Add(Col("Invoiced_Qty", "VAS_195_InvoicedQty", "Invoiced", COLTYPE_QTY, 0.8m));
            columns.Add(Col("Matched_Qty", "VAS_195_MatchedQty", "Matched", COLTYPE_QTY, 0.8m));
            columns.Add(Col("Unmatched_Qty", "VAS_195_UnmatchedQty", "Unmatched", COLTYPE_QTY, 0.8m));
            columns.Add(Col("Match_Requirement", "VAS_195_MatchRequirement", "Match Req.", COLTYPE_BADGE, 0.8m));
            columns.Add(Col("Doc_Amount", "VAS_195_LineAmount", "Line Amount", COLTYPE_DOCAMOUNT, 1.0m));

            return Spec(sql, "il", "i.DateAcct DESC,i.DocumentNo DESC,il.C_InvoiceLine_ID DESC",
                parameters, columns);
        }

        // ─────────────────────────────────────────────────────────────────────
        // 08  Qty / price mismatches                                   WARNING
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Matched receipt/invoice pairs whose quantities or prices disagree.
        /// </summary>
        /// <param name="c">Shared evaluation context.</param>
        /// <param name="def">Registry entry.</param>
        /// <returns>Populated <see cref="CheckResult"/>.</returns>
        private CheckResult Eval08(CheckContext c, CheckDef def)
        {
            return CountSpec(c, def, Spec08(c),
                "VAS_195_Sum08", "matched lines differ in quantity or price",
                "VAS_195_Clr08", "Matched quantities and prices agree");
        }

        /// <summary>
        /// Matches whose receipt and invoice disagree beyond tolerance.
        ///
        /// The stored PriceDifference* columns this check would prefer do not exist in
        /// this schema (they are module additions), so they are probed and, when absent,
        /// the variance is computed from the documents themselves: the order line's price
        /// against the invoice line's. The variance TYPE is returned as a token rather
        /// than folded into one number, because a quantity gap and a price gap are not
        /// the same finding and must not be summed.
        /// </summary>
        /// <param name="c">Shared evaluation context.</param>
        /// <returns>Populated <see cref="DetailSpec"/>, or null when matching is absent.</returns>
        private DetailSpec Spec08(CheckContext c)
        {
            if (!TableExists("M_MatchInv")) { return null; }

            List<SqlParameter> parameters = Binds();

            /* Unit prices, normalised to the entered UOM so the two sides compare. */
            string orderPrice = "COALESCE((ol.QtyEntered/NULLIF(ol.QtyOrdered,0))*ol.PriceActual,0)";
            string invoicePrice = "COALESCE((il.QtyEntered/NULLIF(il.QtyInvoiced,0))*il.PriceActual,0)";

            /* The invoiced side is the TOTAL matched against this receipt line, not the
               one invoice line this match row happens to point at. A receipt of 50
               invoiced as 30 + 20 is fully invoiced and must not be reported as two
               variances of 20 and 30; and the partial receipts check 06 now defers to
               this check are only measured correctly against the aggregate. */
            string matchedQty = MatchedQtyExpr("iol");
            string qtyVariance = "ABS(ABS(COALESCE(iol.MovementQty,0))-" + matchedQty + ")";
            string priceVariance = "ABS(" + invoicePrice + "-" + orderPrice + ")";

            string qtyOff = qtyVariance + ">" + QTY_TOLERANCE;
            string priceOff = "iol.C_OrderLine_ID IS NOT NULL AND " + priceVariance + ">" + PRICE_TOLERANCE;

            string varianceType = "CASE WHEN (" + qtyOff + ") AND (" + priceOff + ") THEN 'BOTH'"
                + " WHEN " + qtyOff + " THEN 'QTY' ELSE 'PRICE' END";

            string sql = @"
                SELECT " + TableId("C_Invoice") + " AS " + TECH_TABLE + @",
                       i.C_Invoice_ID AS " + TECH_RECORD + @",
                       0 AS " + TECH_WINDOW + @",
                       COALESCE(o.DocumentNo,N'') AS Order_Number,
                       COALESCE(io.DocumentNo,N'') AS Grn_Number,
                       COALESCE(i.DocumentNo,N'') AS Doc_Number,
                       i.DateAcct AS Doc_Date,
                       COALESCE(bp.Name,N'') AS Partner_Name,
                       COALESCE(prod.Name,N'') AS Product_Name,
                       " + varianceType + @" AS Variance_Type,
                       ABS(COALESCE(iol.MovementQty,0)) AS Received_Qty,
                       " + matchedQty + @" AS Invoiced_Qty,
                       " + orderPrice + @" AS Order_Price,
                       " + invoicePrice + @" AS Invoice_Price,
                       COALESCE(cur.ISO_Code,N'') AS Currency_Iso
                FROM M_MatchInv mi
                INNER JOIN C_InvoiceLine il ON (il.C_InvoiceLine_ID=mi.C_InvoiceLine_ID)
                INNER JOIN C_Invoice i ON (i.C_Invoice_ID=il.C_Invoice_ID)
                INNER JOIN M_InOutLine iol ON (iol.M_InOutLine_ID=mi.M_InOutLine_ID)
                INNER JOIN M_InOut io ON (io.M_InOut_ID=iol.M_InOut_ID)
                LEFT OUTER JOIN C_OrderLine ol ON (ol.C_OrderLine_ID=iol.C_OrderLine_ID)
                LEFT OUTER JOIN C_Order o ON (o.C_Order_ID=ol.C_Order_ID)
                LEFT OUTER JOIN C_BPartner bp ON (bp.C_BPartner_ID=i.C_BPartner_ID)
                LEFT OUTER JOIN M_Product prod ON (prod.M_Product_ID=il.M_Product_ID)
                LEFT OUTER JOIN C_Currency cur ON (cur.C_Currency_ID=i.C_Currency_ID)
                WHERE mi.IsActive='Y'
                  AND il.IsActive='Y'
                  AND i.IsActive='Y'
                  AND iol.IsActive='Y'
                  AND i.DocStatus IN (" + DOCSTATUS_FinalList + @")
                  AND " + PeriodWhere(c, "i", "DateAcct", "I", parameters) + @"
                  AND ((" + qtyOff + ") OR (" + priceOff + "))";

            List<ColumnDef> columns = new List<ColumnDef>();
            columns.Add(Col("Order_Number", "VAS_195_OrderNo", "PO No", COLTYPE_TEXT, 1.0m));
            columns.Add(Col("Grn_Number", "VAS_195_GrnNo", "GRN No", COLTYPE_TEXT, 1.0m));
            columns.Add(Col("Doc_Number", "VAS_195_InvoiceNo", "Invoice No", COLTYPE_DOC, 1.0m));
            columns.Add(Col("Doc_Date", "VAS_195_AccountDate", "Account Date", COLTYPE_DATE, 0.9m));
            columns.Add(Col("Partner_Name", "VAS_195_BusinessPartner", "Business Partner", COLTYPE_TEXT, 1.2m));
            columns.Add(Col("Product_Name", "VAS_195_Product", "Product", COLTYPE_TEXT, 1.2m));
            columns.Add(Col("Variance_Type", "VAS_195_VarianceType", "Variance", COLTYPE_BADGE, 0.8m));
            columns.Add(Col("Received_Qty", "VAS_195_ReceivedQty", "Received", COLTYPE_QTY, 0.7m));
            columns.Add(Col("Invoiced_Qty", "VAS_195_InvoicedQty", "Invoiced", COLTYPE_QTY, 0.7m));
            columns.Add(Col("Order_Price", "VAS_195_OrderPrice", "Order Price", COLTYPE_DOCAMOUNT, 0.9m));
            columns.Add(Col("Invoice_Price", "VAS_195_InvoicePrice", "Invoice Price", COLTYPE_DOCAMOUNT, 0.9m));

            return Spec(sql, "mi", "i.DateAcct DESC,i.DocumentNo DESC,mi.M_MatchInv_ID DESC",
                parameters, columns);
        }

        // ─────────────────────────────────────────────────────────────────────
        // 09  Suspense account balances                                BLOCKER
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Material balances left on the configured suspense and rounding accounts.
        ///
        /// The balance tested is the CLOSING balance through period end, not the
        /// period's own movement: a suspense amount posted three months ago and never
        /// cleared is exactly the thing this check exists to catch, and a
        /// movement-only test would report the period as clean.
        /// </summary>
        /// <param name="c">Shared evaluation context.</param>
        /// <param name="def">Registry entry.</param>
        /// <returns>Populated <see cref="CheckResult"/>.</returns>
        private CheckResult Eval09(CheckContext c, CheckDef def)
        {
            List<SuspenseAcct> accounts = SuspenseAccounts(c);

            if (accounts.Count == 0)
            {
                /* Enabled but unresolvable is a configuration error on a BLOCKER; never
                   configured at all is simply not applicable. */
                return SuspenseEnabled(c)
                    ? Configured(def, "VAS_195_Cfg09", "Suspense account is enabled but cannot be resolved")
                    : NotApplicable(def, "VAS_195_Na09", "No suspense accounts are configured");
            }

            int material = 0;
            decimal total = 0;

            for (int i = 0; i < accounts.Count; i++)
            {
                decimal closing = Math.Abs(accounts[i].ClosingBalance);
                if (closing > c.Tolerance) { material++; total += closing; }
            }

            CheckResult result = Counted(def, material,
                "VAS_195_Sum09", "suspense accounts carry material closing balances",
                "VAS_195_Clr09", "Suspense account balances are within tolerance");

            result.Amount = total;
            result.DocumentCount = accounts.Count;
            /* Every configured account stays drillable, cleared or not - "it is zero"
               is an answer worth being able to verify. */
            result.DetailAvailable = true;

            return result;
        }

        /// <summary>
        /// Every posting that makes up the suspense accounts' closing balance, newest
        /// first - the entries themselves, not a summary of them.
        ///
        /// An account-level summary is what this used to open, and it answered the wrong
        /// question. The check BLOCKS the close because a suspense balance is not zero,
        /// and the only useful next step is finding out which documents put it there;
        /// a row reading "opening 4,000, movement 900, closing 4,900" tells the reader
        /// what they already saw on the card. The posting list names the documents and
        /// zooms straight to them.
        ///
        /// The clearing-account check still opens the summary - see
        /// <see cref="AccountBalanceSpec"/>, which is unchanged and now serves check 10
        /// alone.
        /// </summary>
        /// <param name="c">Shared evaluation context.</param>
        /// <returns>Populated <see cref="DetailSpec"/>, or null when none are configured.</returns>
        private DetailSpec Spec09(CheckContext c)
        {
            List<SuspenseAcct> accounts = SuspenseAccounts(c);
            if (accounts.Count == 0) { return null; }

            List<int> ids = new List<int>();
            for (int i = 0; i < accounts.Count; i++)
            {
                if (!ids.Contains(accounts[i].Account_ID)) { ids.Add(accounts[i].Account_ID); }
            }

            return SuspensePostingSpec(c, ids);
        }

        /// <summary>
        /// One row per Fact_Acct entry on the configured suspense accounts, WITHIN the
        /// selected period.
        ///
        /// Bounded at both ends by <see cref="PeriodWhere"/>, like every other check on
        /// this card: the period chip at the top of the widget is the one filter the
        /// whole checklist is read through, and a list that quietly ignored it would be
        /// the odd one out. Newest first, so the entries most likely to be the cause are
        /// on the first page.
        ///
        /// One consequence, and it is intended rather than overlooked: the card's
        /// headline for this check is the CLOSING balance - every posting ever made to
        /// the account through period end - so this list will not sum to it whenever the
        /// account carried a balance into the period. The list answers "what hit suspense
        /// in this period", which is the question the period filter asks; the opening
        /// balance behind the difference is the previous periods' business.
        ///
        /// The LEDGER column carries "&lt;Value&gt; - &lt;Name&gt;" ("79200 - Suspense
        /// balancing") because the check spans every configured suspense account at
        /// once - balancing, error and the optional rounding account - and a posting row
        /// is meaningless without saying which of them it landed on. The CASE guards the
        /// separator: an account with no name renders as its value alone rather than as
        /// a value trailing a dangling dash.
        ///
        /// No Screen column is declared, which is what puts the screen name UNDER the
        /// document number - see the client's docCell. The document number itself is not
        /// selected at all: Fact_Acct stores AD_Table_ID / Record_ID rather than a
        /// number, and the shared resolver fills the display value, the screen label and
        /// the navigability from those three technical aliases.
        /// </summary>
        /// <param name="c">Shared evaluation context.</param>
        /// <param name="accountIds">Validated natural account ids.</param>
        /// <returns>Populated <see cref="DetailSpec"/>.</returns>
        private DetailSpec SuspensePostingSpec(CheckContext c, List<int> accountIds)
        {
            List<SqlParameter> parameters = Binds();

            string sql = @"
                SELECT fa.AD_Table_ID AS " + TECH_TABLE + @",
                       COALESCE(fa.Record_ID,0) AS " + TECH_RECORD + @",
                       COALESCE(fa.AD_Window_ID,0) AS " + TECH_WINDOW + @",
                       CASE WHEN COALESCE(ev.Name,N'')=N'' THEN COALESCE(ev.Value,N'')
                            ELSE COALESCE(ev.Value,N'') || N' - ' || COALESCE(ev.Name,N'') END AS Ledger_Name,
                       fa.DateTrx AS Doc_Date,
                       fa.DateAcct AS Acct_Date,
                       CASE WHEN COALESCE(fa.AmtAcctDr,0)<>0 THEN N'" + DRCR_DEBIT + @"' ELSE N'" + DRCR_CREDIT + @"' END AS Dr_Cr,
                       CASE WHEN COALESCE(fa.AmtAcctDr,0)<>0 THEN COALESCE(fa.AmtAcctDr,0) ELSE COALESCE(fa.AmtAcctCr,0) END AS Posting_Amount
                FROM Fact_Acct fa
                LEFT OUTER JOIN C_ElementValue ev ON (ev.C_ElementValue_ID=fa.Account_ID)
                WHERE fa.AD_Client_ID=@AD_Client_ID
                  AND fa.C_AcctSchema_ID=@C_AcctSchema_ID
                  AND fa.PostingType=@PostingType
                  AND fa.IsActive='Y'
                  AND ";

            /* Appearance order - the adapters bind positionally, so each fragment's binds
               are added at the point its text is appended, never up front. */
            parameters.Add(new SqlParameter("@AD_Client_ID", c.Ctx.GetAD_Client_ID()));
            parameters.Add(new SqlParameter("@C_AcctSchema_ID", c.Acct.C_AcctSchema_ID));
            parameters.Add(new SqlParameter("@PostingType", POSTINGTYPE_Actual));

            /* The period chip's own bounds, through the shared helper every other check
               uses - inclusive start, exclusive end, so a posting stamped with a time on
               the period's last day still falls inside its own period on both backends. */
            sql += PeriodWhere(c, "fa", "DateAcct", "S", parameters);

            sql += " AND fa.Account_ID IN (";
            sql += BuildIdInList(accountIds, "@Account_ID", parameters) + ")";

            /* Doc_Number is DECLARED but never selected: Fact_Acct has no document
               number to give, and the shared resolver fills the display value from the
               technical aliases above. A declared column its statement does not select
               yields an empty cell, which is exactly the hook the DOC renderer falls
               back through. */
            List<ColumnDef> columns = new List<ColumnDef>();
            columns.Add(Col("Ledger_Name", "VAS_195_Ledger", "Ledger", COLTYPE_TEXT, 1.6m));
            columns.Add(Col("Doc_Number", "DocumentNo", "Document No", COLTYPE_DOC, 1.5m));
            columns.Add(Col("Doc_Date", "VAS_195_DocumentDate", "Document Date", COLTYPE_DATE, 0.95m));
            columns.Add(Col("Acct_Date", "VAS_195_AccountDate", "Account Date", COLTYPE_DATE, 0.95m));
            columns.Add(Col("Dr_Cr", "VAS_195_DrCr", "Dr / Cr", COLTYPE_BADGE, 0.7m));
            columns.Add(Col("Posting_Amount", "VAS_195_Amount", "Amount", COLTYPE_AMOUNT, 1.1m));

            /* Physical columns in the sort rather than the select aliases: there is no
               GROUP BY or DISTINCT here, so both back ends can reach them, and
               Fact_Acct_ID breaks the date tie so paging stays stable between requests. */
            return Spec(sql, "fa", "fa.DateAcct DESC,fa.Fact_Acct_ID DESC", parameters, columns);
        }

        /// <summary>
        /// The account-balance statement behind the CLEARING check: one row per natural
        /// account, with the balance split into what was carried in, what moved, and what
        /// remains.
        ///
        /// The suspense check used to share it and no longer does - see
        /// <see cref="SuspensePostingSpec"/> for why. The signature keeps its caption
        /// parameters, so a second summary-style check can still reuse it.
        ///
        /// Opening and closing are bounded by @PeriodEnd only, so they include every
        /// prior posting; the period figures are bounded at both ends. Amounts are
        /// AmtAcctDr/AmtAcctCr, already in the accounting schema's own currency - there
        /// is deliberately no conversion anywhere in this statement.
        /// </summary>
        /// <param name="c">Shared evaluation context.</param>
        /// <param name="accountIds">Validated natural account ids.</param>
        /// <param name="labelKey">AD_Message key for the account column caption.</param>
        /// <param name="labelText">English fallback caption.</param>
        /// <returns>Populated <see cref="DetailSpec"/>.</returns>
        private DetailSpec AccountBalanceSpec(CheckContext c, List<int> accountIds,
            string labelKey, string labelText)
        {
            /* Every occurrence carries its OWN bind name and the binds are added in the
               order those names appear in the finished text. Both matter: the backend
               adapters bind positionally, so a name reused by the three period CASE
               expressions would be filled from the wrong slot - and this statement needs
               the same two dates five times over. The fragments are therefore plain
               strings, and the binds are listed once, below, in reading order. */
            string periodCase = "fa.DateAcct>=@PeriodStart{0} AND fa.DateAcct<@PeriodEnd{0}";

            string sql = @"
                SELECT fa.Account_ID AS " + TECH_RECORD + @",
                       0 AS " + TECH_TABLE + @",
                       0 AS " + TECH_WINDOW + @",
                       COALESCE(ev.Value,N'') AS Account_Value,
                       COALESCE(ev.Name,N'') AS Account_Name,
                       SUM(CASE WHEN fa.DateAcct<@OpeningBefore THEN COALESCE(fa.AmtAcctDr,0)-COALESCE(fa.AmtAcctCr,0) ELSE 0 END) AS Opening_Balance,
                       SUM(CASE WHEN " + string.Format(periodCase, "A") + @" THEN COALESCE(fa.AmtAcctDr,0) ELSE 0 END) AS Period_Debit,
                       SUM(CASE WHEN " + string.Format(periodCase, "B") + @" THEN COALESCE(fa.AmtAcctCr,0) ELSE 0 END) AS Period_Credit,
                       SUM(CASE WHEN " + string.Format(periodCase, "C") + @" THEN COALESCE(fa.AmtAcctDr,0)-COALESCE(fa.AmtAcctCr,0) ELSE 0 END) AS Period_Movement,
                       SUM(CASE WHEN fa.DateAcct<@ClosingBefore THEN COALESCE(fa.AmtAcctDr,0)-COALESCE(fa.AmtAcctCr,0) ELSE 0 END) AS Closing_Balance,
                       SUM(CASE WHEN " + string.Format(periodCase, "D") + @" THEN 1 ELSE 0 END) AS Entry_Count
                FROM Fact_Acct fa
                LEFT OUTER JOIN C_ElementValue ev ON (ev.C_ElementValue_ID=fa.Account_ID)
                WHERE fa.AD_Client_ID=@AD_Client_ID
                  AND fa.C_AcctSchema_ID=@C_AcctSchema_ID
                  AND fa.PostingType=@PostingType
                  AND fa.IsActive='Y'
                  AND fa.Account_ID IN (";

            List<SqlParameter> parameters = Binds();
            parameters.Add(new SqlParameter("@OpeningBefore", c.PeriodStart));
            parameters.Add(new SqlParameter("@PeriodStartA", c.PeriodStart));
            parameters.Add(new SqlParameter("@PeriodEndA", c.PeriodEndExclusive));
            parameters.Add(new SqlParameter("@PeriodStartB", c.PeriodStart));
            parameters.Add(new SqlParameter("@PeriodEndB", c.PeriodEndExclusive));
            parameters.Add(new SqlParameter("@PeriodStartC", c.PeriodStart));
            parameters.Add(new SqlParameter("@PeriodEndC", c.PeriodEndExclusive));
            parameters.Add(new SqlParameter("@ClosingBefore", c.PeriodEndExclusive));
            parameters.Add(new SqlParameter("@PeriodStartD", c.PeriodStart));
            parameters.Add(new SqlParameter("@PeriodEndD", c.PeriodEndExclusive));
            parameters.Add(new SqlParameter("@AD_Client_ID", c.Ctx.GetAD_Client_ID()));
            parameters.Add(new SqlParameter("@C_AcctSchema_ID", c.Acct.C_AcctSchema_ID));
            parameters.Add(new SqlParameter("@PostingType", POSTINGTYPE_Actual));

            sql += BuildIdInList(accountIds, "@Account_ID", parameters) + ")";

            /* Fact_Acct fa is the main physical table; GROUP BY is appended by the
               framework AFTER the access SQL, so it is folded into the statement here
               only because AddAccessSQL has not run yet at this point - the framework
               secures spec.Sql before anything else touches it. */
            DetailSpec spec = new DetailSpec();
            spec.Sql = sql;
            spec.MainAlias = "fa";
            spec.GroupBy = "fa.Account_ID,COALESCE(ev.Value,N''),COALESCE(ev.Name,N'')";
            spec.OrderBy = "Account_Value";
            spec.Params = parameters;

            List<ColumnDef> columns = new List<ColumnDef>();
            columns.Add(Col("Account_Value", labelKey, labelText, COLTYPE_TEXT, 0.8m));
            columns.Add(Col("Account_Name", "VAS_195_AccountName", "Account Name", COLTYPE_TEXT, 1.6m));
            columns.Add(Col("Opening_Balance", "VAS_195_OpeningBalance", "Opening", COLTYPE_AMOUNT, 1.0m));
            columns.Add(Col("Period_Debit", "VAS_195_PeriodDebit", "Debit", COLTYPE_AMOUNT, 1.0m));
            columns.Add(Col("Period_Credit", "VAS_195_PeriodCredit", "Credit", COLTYPE_AMOUNT, 1.0m));
            columns.Add(Col("Period_Movement", "VAS_195_PeriodMovement", "Movement", COLTYPE_AMOUNT, 1.0m));
            columns.Add(Col("Closing_Balance", "VAS_195_ClosingBalance", "Closing", COLTYPE_AMOUNT, 1.0m));
            columns.Add(Col("Entry_Count", "VAS_195_Entries", "Entries", COLTYPE_NUMBER, 0.6m));
            spec.Columns = columns;

            return spec;
        }

        /// <summary>
        /// The configured suspense/rounding accounts of the primary accounting schema,
        /// resolved from C_AcctSchema_GL through C_ValidCombination to the natural
        /// account, with each account's closing balance through period end.
        ///
        /// The three settings hold a C_ValidCombination_ID, NOT a Fact_Acct.Account_ID -
        /// comparing the setting directly against Fact_Acct would match nothing, or the
        /// wrong account. FRPT_RoundingOff_Acct is optional in this schema and is probed
        /// before it is named.
        /// </summary>
        /// <param name="c">Shared evaluation context.</param>
        /// <returns>Resolved accounts with balances (never null).</returns>
        private List<SuspenseAcct> SuspenseAccounts(CheckContext c)
        {
            List<SuspenseAcct> accounts = new List<SuspenseAcct>();

            List<string> settings = new List<string>();
            settings.Add("SuspenseBalancing_Acct");
            settings.Add("SuspenseError_Acct");
            if (ColumnExists("C_AcctSchema_GL", "FRPT_RoundingOff_Acct")) 
            {
                settings.Add("FRPT_RoundingOff_Acct"); 
            }

            StringBuilder select = new StringBuilder("SELECT ");
            for (int i = 0; i < settings.Count; i++)
            {
                if (i > 0) { select.Append(","); }
                select.Append("COALESCE(gl.").Append(settings[i]).Append(",0) AS Setting_")
                      .Append((i + 1).ToString(CultureInfo.InvariantCulture));
            }
            select.Append(@" FROM C_AcctSchema_GL gl
                WHERE gl.AD_Client_ID=@AD_Client_ID
                  AND gl.C_AcctSchema_ID=@C_AcctSchema_ID
                  AND gl.IsActive='Y'");

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@AD_Client_ID", c.Ctx.GetAD_Client_ID()),
                new SqlParameter("@C_AcctSchema_ID", c.Acct.C_AcctSchema_ID)
            };

            DataSet ds = DB.ExecuteDataset(select.ToString(), parameters, null);
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) { return accounts; }

            DataRow row = ds.Tables[0].Rows[0];
            List<int> combinations = new List<int>();
            for (int i = 0; i < settings.Count; i++)
            {
                int id = Util.GetValueOfInt(row["Setting_" + (i + 1).ToString(CultureInfo.InvariantCulture)]);
                if (id > 0 && !combinations.Contains(id)) { combinations.Add(id); }
            }

            if (combinations.Count == 0) { return accounts; }

            List<int> accountIds = ResolveCombinations(c, combinations);
            if (accountIds.Count == 0) { return accounts; }

            return ReadClosingBalances(c, accountIds);
        }

        /// <summary>Whether the schema has any suspense handling switched on.</summary>
        /// <param name="c">Shared evaluation context.</param>
        /// <returns>true when a Use* flag is set.</returns>
        private bool SuspenseEnabled(CheckContext c)
        {
            string sql = @"
                SELECT COUNT(1) AS Enabled_Count
                FROM C_AcctSchema_GL gl
                WHERE gl.AD_Client_ID=@AD_Client_ID
                  AND gl.C_AcctSchema_ID=@C_AcctSchema_ID
                  AND gl.IsActive='Y'
                  AND (COALESCE(gl.UseSuspenseBalancing,'N')='Y' OR COALESCE(gl.UseSuspenseError,'N')='Y')";

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@AD_Client_ID", c.Ctx.GetAD_Client_ID()),
                new SqlParameter("@C_AcctSchema_ID", c.Acct.C_AcctSchema_ID)
            };

            DataSet ds = DB.ExecuteDataset(sql, parameters, null);
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) { return false; }

            return Util.GetValueOfInt(ds.Tables[0].Rows[0]["Enabled_Count"]) > 0;
        }

        /// <summary>Resolves valid combinations to their distinct natural accounts.</summary>
        /// <param name="c">Shared evaluation context.</param>
        /// <param name="combinationIds">Configured C_ValidCombination_IDs.</param>
        /// <returns>Distinct natural account ids (never null).</returns>
        private List<int> ResolveCombinations(CheckContext c, List<int> combinationIds)
        {
            List<int> accountIds = new List<int>();

            List<SqlParameter> parameters = Binds();
            parameters.Add(new SqlParameter("@C_AcctSchema_ID", c.Acct.C_AcctSchema_ID));
            string inList = BuildIdInList(combinationIds, "@C_ValidCombination_ID", parameters);

            string sql = @"
                SELECT vc.Account_ID AS Account_ID
                FROM C_ValidCombination vc
                INNER JOIN C_ElementValue ev ON (ev.C_ElementValue_ID=vc.Account_ID AND ev.IsActive='Y')
                WHERE vc.C_AcctSchema_ID=@C_AcctSchema_ID
                  AND vc.IsActive='Y'
                  AND vc.C_ValidCombination_ID IN (" + inList + ")";

            DataSet ds = DB.ExecuteDataset(sql, parameters.ToArray(), null);
            if (ds == null || ds.Tables.Count == 0) { return accountIds; }

            DataTable dt = ds.Tables[0];
            for (int i = 0; i < dt.Rows.Count; i++)
            {
                int id = Util.GetValueOfInt(dt.Rows[i]["Account_ID"]);
                if (id > 0 && !accountIds.Contains(id)) { accountIds.Add(id); }
            }

            return accountIds;
        }

        /// <summary>
        /// Closing balance through period end for each of the given natural accounts.
        /// </summary>
        /// <param name="c">Shared evaluation context.</param>
        /// <param name="accountIds">Validated natural account ids.</param>
        /// <returns>One entry per account (never null).</returns>
        private List<SuspenseAcct> ReadClosingBalances(CheckContext c, List<int> accountIds)
        {
            List<SuspenseAcct> accounts = new List<SuspenseAcct>();

            List<SqlParameter> parameters = Binds();
            parameters.Add(new SqlParameter("@AD_Client_ID", c.Ctx.GetAD_Client_ID()));
            parameters.Add(new SqlParameter("@C_AcctSchema_ID", c.Acct.C_AcctSchema_ID));
            parameters.Add(new SqlParameter("@PostingType", POSTINGTYPE_Actual));
            parameters.Add(new SqlParameter("@PeriodEnd", c.PeriodEndExclusive));
            string inList = BuildIdInList(accountIds, "@Account_ID", parameters);

            string sql = @"
                SELECT fa.Account_ID AS Account_ID,
                       SUM(COALESCE(fa.AmtAcctDr,0)-COALESCE(fa.AmtAcctCr,0)) AS Closing_Balance
                FROM Fact_Acct fa
                WHERE fa.AD_Client_ID=@AD_Client_ID
                  AND fa.C_AcctSchema_ID=@C_AcctSchema_ID
                  AND fa.PostingType=@PostingType
                  AND fa.IsActive='Y'
                  AND fa.DateAcct<@PeriodEnd
                  AND fa.Account_ID IN (" + inList + ")";

            sql = MRole.GetDefault(c.Ctx).AddAccessSQL(sql, "fa", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            sql += " GROUP BY fa.Account_ID";

            DataSet ds = DB.ExecuteDataset(sql, parameters.ToArray(), null);

            /* Every configured account gets an entry, balance or not - an account with
               no postings has a closing balance of zero, which is a result, not a gap. */
            Dictionary<int, decimal> balances = new Dictionary<int, decimal>();
            if (ds != null && ds.Tables.Count > 0)
            {
                DataTable dt = ds.Tables[0];
                for (int i = 0; i < dt.Rows.Count; i++)
                {
                    balances[Util.GetValueOfInt(dt.Rows[i]["Account_ID"])] =
                        Util.GetValueOfDecimal(dt.Rows[i]["Closing_Balance"]);
                }
            }

            for (int i = 0; i < accountIds.Count; i++)
            {
                SuspenseAcct account = new SuspenseAcct();
                account.Account_ID = accountIds[i];

                decimal balance;
                account.ClosingBalance = balances.TryGetValue(accountIds[i], out balance) ? balance : 0;

                accounts.Add(account);
            }

            return accounts;
        }

        // ─────────────────────────────────────────────────────────────────────
        // 10  Clearing account balances                                WARNING
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Balances left on accounts finance designates as clearing accounts.
        ///
        /// There is no reliable universal rule for identifying a clearing account, and
        /// this installation carries no close-check account mapping, so the check uses
        /// C_ElementValue.IsAllocationRelated as the documented SETUP AID and says so.
        /// When nothing is marked, the answer is NOT_APPLICABLE with a setup message -
        /// never a PASS, because "we found no clearing accounts to look at" is not the
        /// same as "the clearing accounts are clean".
        /// </summary>
        /// <param name="c">Shared evaluation context.</param>
        /// <param name="def">Registry entry.</param>
        /// <returns>Populated <see cref="CheckResult"/>.</returns>
        private CheckResult Eval10(CheckContext c, CheckDef def)
        {
            List<int> accountIds = ClearingAccounts(c);

            if (accountIds.Count == 0)
            {
                return NotApplicable(def, "VAS_195_Na10", "No clearing accounts are configured - setup required to evaluate");
            }

            List<SuspenseAcct> balances = ReadClosingBalances(c, accountIds);

            int material = 0;
            decimal total = 0;
            for (int i = 0; i < balances.Count; i++)
            {
                decimal closing = Math.Abs(balances[i].ClosingBalance);
                if (closing > c.Tolerance) { material++; total += closing; }
            }

            CheckResult result = Counted(def, material,
                "VAS_195_Sum10", "clearing accounts carry balances to review",
                "VAS_195_Clr10", "Clearing account balances are within policy");

            result.Amount = total;
            result.DocumentCount = accountIds.Count;
            result.DetailAvailable = true;

            return result;
        }

        /// <summary>Account-level balances of the designated clearing accounts.</summary>
        /// <param name="c">Shared evaluation context.</param>
        /// <returns>Populated <see cref="DetailSpec"/>, or null when none are designated.</returns>
        private DetailSpec Spec10(CheckContext c)
        {
            List<int> accountIds = ClearingAccounts(c);
            if (accountIds.Count == 0) { return null; }

            return AccountBalanceSpec(c, accountIds, "VAS_195_ClearingAccount", "Clearing Account");
        }

        /// <summary>
        /// The natural accounts treated as clearing accounts. Setup aid only - see the
        /// note on Eval10.
        /// </summary>
        /// <param name="c">Shared evaluation context.</param>
        /// <returns>Distinct account ids (never null).</returns>
        private List<int> ClearingAccounts(CheckContext c)
        {
            List<int> accountIds = new List<int>();
            if (!ColumnExists("C_ElementValue", "IsAllocationRelated")) { return accountIds; }

            string sql = @"
                SELECT ev.C_ElementValue_ID AS Account_ID
                FROM C_ElementValue ev
                WHERE ev.AD_Client_ID=@AD_Client_ID
                  AND ev.IsActive='Y'
                  AND COALESCE(ev.IsAllocationRelated,'N')='Y'";

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@AD_Client_ID", c.Ctx.GetAD_Client_ID())
            };

            DataSet ds = DB.ExecuteDataset(sql, parameters, null);
            if (ds == null || ds.Tables.Count == 0) { return accountIds; }

            DataTable dt = ds.Tables[0];
            for (int i = 0; i < dt.Rows.Count; i++)
            {
                int id = Util.GetValueOfInt(dt.Rows[i]["Account_ID"]);
                if (id > 0 && !accountIds.Contains(id)) { accountIds.Add(id); }
            }

            return accountIds;
        }

        // ─────────────────────────────────────────────────────────────────────
        // 11  Incomplete allocations / settlements                     WARNING
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Settlement WORK left unfinished - as distinct from check 03, which looks at
        /// payments. A draft allocation document and an unallocated payment are two
        /// different problems with two different owners.
        /// </summary>
        /// <param name="c">Shared evaluation context.</param>
        /// <param name="def">Registry entry.</param>
        /// <returns>Populated <see cref="CheckResult"/>.</returns>
        private CheckResult Eval11(CheckContext c, CheckDef def)
        {
            return CountSpec(c, def, Spec11(c),
                "VAS_195_Sum11", "settlement documents are still open",
                "VAS_195_Clr11", "No incomplete settlement activity found");
        }

        /// <summary>
        /// Two secured branches: allocation headers in the period that never reached a
        /// final status, and payment-allocate rows that were prepared but never became
        /// an allocation line.
        /// </summary>
        /// <param name="c">Shared evaluation context.</param>
        /// <returns>Populated <see cref="DetailSpec"/>.</returns>
        private DetailSpec Spec11(CheckContext c)
        {
            List<SqlParameter> parameters = Binds();
            StringBuilder sql = new StringBuilder();

            string headers = @"
                SELECT " + TableId("C_AllocationHdr") + " AS " + TECH_TABLE + @",
                       ah.C_AllocationHdr_ID AS " + TECH_RECORD + @",
                       0 AS " + TECH_WINDOW + @",
                       'ALLOCATION' AS Source_Type,
                       COALESCE(ah.DocumentNo,N'') AS Doc_Number,
                       ah.DateAcct AS Doc_Date,
                       N'' AS Partner_Name,
                       COALESCE(cur.ISO_Code,N'') AS Currency_Iso,
                       COALESCE(ah.ApprovalAmt,0) AS Doc_Amount,
                       ah.DocStatus AS Doc_Status
                FROM C_AllocationHdr ah
                LEFT OUTER JOIN C_Currency cur ON (cur.C_Currency_ID=ah.C_Currency_ID)
                WHERE ah.IsActive='Y'
                  AND ah.DocStatus NOT IN (" + DOCSTATUS_FinalList + @")
                  AND ah.DocStatus NOT IN (" + DOCSTATUS_DeadList + @")
                  AND " + PeriodWhere(c, "ah", "DateAcct", "A", parameters);

            sql.Append(SecureBranch(c, headers, "ah"));

            if (TableExists("C_PaymentAllocate") && ColumnExists("C_PaymentAllocate", "C_AllocationLine_ID"))
            {
                string prepared = @"
                SELECT " + TableId("C_Payment") + " AS " + TECH_TABLE + @",
                       pa.C_Payment_ID AS " + TECH_RECORD + @",
                       0 AS " + TECH_WINDOW + @",
                       'PREPARED' AS Source_Type,
                       COALESCE(p.DocumentNo,N'') AS Doc_Number,
                       p.DateAcct AS Doc_Date,
                       COALESCE(bp.Name,N'') AS Partner_Name,
                       COALESCE(cur.ISO_Code,N'') AS Currency_Iso,
                       COALESCE(pa.Amount,0) AS Doc_Amount,
                       p.DocStatus AS Doc_Status
                FROM C_PaymentAllocate pa
                INNER JOIN C_Payment p ON (p.C_Payment_ID=pa.C_Payment_ID)
                LEFT OUTER JOIN C_BPartner bp ON (bp.C_BPartner_ID=p.C_BPartner_ID)
                LEFT OUTER JOIN C_Currency cur ON (cur.C_Currency_ID=p.C_Currency_ID)
                WHERE pa.IsActive='Y'
                  AND p.IsActive='Y'
                  AND pa.C_AllocationLine_ID IS NULL
                  AND " + PeriodWhere(c, "p", "DateAcct", "PA", parameters) + @"
                  AND ABS(COALESCE(pa.Amount,0))>@ToleranceP";

                /* Tolerance predicate sits AFTER the period one in the text, so its bind
                   is added after the period binds - order is what positional binding
                   goes by, not the name. */
                parameters.Add(new SqlParameter("@ToleranceP", c.Tolerance));

                sql.Append(" UNION ALL ").Append(SecureBranch(c, prepared, "pa"));
            }

            List<ColumnDef> columns = new List<ColumnDef>();
            columns.Add(Col("Source_Type", "VAS_195_SettlementSource", "Source", COLTYPE_BADGE, 0.9m));
            columns.Add(Col("Doc_Number", "DocumentNo", "Document No", COLTYPE_DOC, 1.2m));
            columns.Add(Col("Doc_Date", "VAS_195_AccountDate", "Account Date", COLTYPE_DATE, 0.9m));
            columns.Add(Col("Partner_Name", "VAS_195_BusinessPartner", "Business Partner", COLTYPE_TEXT, 1.3m));
            columns.Add(Col("Currency_Iso", "VAS_195_Currency", "Currency", COLTYPE_TEXT, 0.5m));
            columns.Add(Col("Doc_Amount", "VAS_195_Amount", "Amount", COLTYPE_DOCAMOUNT, 1.0m));
            columns.Add(Col("Doc_Status", "VAS_195_DocStatus", "Status", COLTYPE_BADGE, 0.8m));

            return Spec(sql.ToString(), "", "Doc_Date DESC,Doc_Number DESC", parameters, columns);
        }

        // ─────────────────────────────────────────────────────────────────────
        // 12  Missing recurring entries                                BLOCKER
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Recurring documents that were due by period end and never generated. The
        /// period's income or expense is incomplete without them.
        /// </summary>
        /// <param name="c">Shared evaluation context.</param>
        /// <param name="def">Registry entry.</param>
        /// <returns>Populated <see cref="CheckResult"/>.</returns>
        private CheckResult Eval12(CheckContext c, CheckDef def)
        {
            if (!TableExists("C_Recurring"))
            {
                return NotApplicable(def, "VAS_195_Na12", "The recurring documents feature is not in use");
            }

            return CountSpec(c, def, Spec12(c),
                "VAS_195_Sum12", "recurring entries due by period end have not been generated",
                "VAS_195_Clr12", "All recurring entries due for this period are generated");
        }

        /// <summary>
        /// Active recurring setups whose NEXT RUN falls before the end of the selected
        /// period - that is the whole test, and it is sufficient on its own.
        ///
        /// The due date comes from C_Recurring's own DateNextRun, the same field the
        /// recurring process advances when it generates a document, rather than from
        /// month arithmetic over FrequencyType. Because the process moves that field
        /// forward on every generation, "DateNextRun still sits inside this period" IS
        /// "a run is due and has not happened". Nothing further needs asking.
        ///
        /// A second condition used to be ANDed on: no C_Recurring_Run row dated inside
        /// the period. It has been removed, for two reasons. It could only ever REMOVE
        /// rows, so the historical-period case its comment claimed to handle was never
        /// reachable - a DateNextRun that had advanced past the period already failed the
        /// first test, and no second test can bring a row back. And it wrongly hid live
        /// exceptions: a weekly setup that ran on the 1st and the 8th but still owes the
        /// 15th has both a run inside the period and a due date inside it, and the
        /// NOT EXISTS dropped it from a check whose entire job is to report it.
        ///
        /// Bounded by PeriodEndExclusive, so a setup due ON the period's last day counts
        /// as due for that period - which it is.
        ///
        /// TYPE and FREQUENCY are list columns: they store a one-letter code and the
        /// reader needs the word. Both are now resolved through AD_Ref_List for the
        /// session language, exactly as check 22 resolves DocBaseType, so the columns
        /// read "Invoice" and "Monthly" rather than 'I' and 'M'. The raw codes are no
        /// longer shown - they are internal values, and the name is what the same field
        /// shows on the recurring document's own window.
        /// </summary>
        /// <param name="c">Shared evaluation context.</param>
        /// <returns>Populated <see cref="DetailSpec"/>, or null when the feature is absent.</returns>
        private DetailSpec Spec12(CheckContext c)
        {
            if (!TableExists("C_Recurring")) { return null; }

            List<SqlParameter> parameters = Binds();
            bool hasRunsRemaining = ColumnExists("C_Recurring", "RunsRemaining");
            bool hasRecurringType = ColumnExists("C_Recurring", "RecurringType");
            bool hasDateLastRun = ColumnExists("C_Recurring", "DateLastRun");

            /* The joins are built BEFORE any of their binds are added, because whether a
               join exists decides whether its bind exists at all: a bind with no
               occurrence in the finished text is not ignored under positional binding -
               it is filled into the NEXT occurrence's slot, and every bind after it
               shifts by one. Each occurrence therefore carries its own name, and each
               name is added only where its text really is. */
            string typeJoin = hasRecurringType
                ? ListNameJoin("rt", "rttrl", "r.RecurringType", "C_Recurring", "RecurringType", "@AD_LanguageT")
                : "";
            if (typeJoin.Length > 0) { parameters.Add(new SqlParameter("@AD_LanguageT", ctxLanguage(c))); }

            string freqJoin = ListNameJoin("fr", "frtrl", "r.FrequencyType", "C_Recurring", "FrequencyType", "@AD_LanguageF");
            if (freqJoin.Length > 0) { parameters.Add(new SqlParameter("@AD_LanguageF", ctxLanguage(c))); }

            /* A join that could not be built contributes no alias, so the name expression
               that would have read it falls back to the stored code - a readable 'M'
               beats an empty column. */
            string typeName = typeJoin.Length > 0
                ? "COALESCE(rttrl.Name,rt.Name,r.RecurringType,N'')"
                : (hasRecurringType ? "COALESCE(r.RecurringType,N'')" : "N''");

            string freqName = freqJoin.Length > 0
                ? "COALESCE(frtrl.Name,fr.Name,r.FrequencyType,N'')"
                : "COALESCE(r.FrequencyType,N'')";

            string sql = @"
                SELECT " + TableId("C_Recurring") + " AS " + TECH_TABLE + @",
                       r.C_Recurring_ID AS " + TECH_RECORD + @",
                       0 AS " + TECH_WINDOW + @",
                       COALESCE(r.Name,N'') AS Recurring_Name,
                       " + typeName + @" AS Recurring_Type_Name,
                       " + freqName + @" AS Frequency_Name,
                       COALESCE(r.Frequency,0) AS Frequency,
                       " + (hasDateLastRun ? "r.DateLastRun" : "CAST(NULL AS DATE)") + @" AS Last_Run,
                       r.DateNextRun AS Next_Run,
                       " + (hasRunsRemaining ? "COALESCE(r.RunsRemaining,0)" : "0") + @" AS Runs_Remaining
                FROM C_Recurring r" + typeJoin + freqJoin + @"
                WHERE r.IsActive='Y'
                  AND r.DateNextRun IS NOT NULL
                  AND r.DateNextRun<@PeriodEndR";

            parameters.Add(new SqlParameter("@PeriodEndR", c.PeriodEndExclusive));

            List<ColumnDef> columns = new List<ColumnDef>();
            columns.Add(Col("Recurring_Name", "VAS_195_RecurringName", "Recurring", COLTYPE_DOC, 1.6m));
            columns.Add(Col("Recurring_Type_Name", "VAS_195_RecurringType", "Type", COLTYPE_TEXT, 1.1m));
            columns.Add(Col("Frequency_Name", "VAS_195_Frequency", "Frequency", COLTYPE_TEXT, 1.1m));
            columns.Add(Col("Last_Run", "VAS_195_LastRun", "Last Run", COLTYPE_DATE, 0.9m));
            columns.Add(Col("Next_Run", "VAS_195_NextRun", "Next Run", COLTYPE_DATE, 0.9m));
            columns.Add(Col("Runs_Remaining", "VAS_195_RunsRemaining", "Remaining", COLTYPE_NUMBER, 0.7m));

            return Spec(sql, "r", "r.DateNextRun,r.C_Recurring_ID", parameters, columns);
        }

        // ─────────────────────────────────────────────────────────────────────
        // 13  Missing accruals / provisions                            WARNING
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Period-end accruals and provisions finance expects but has not booked.
        ///
        /// This check is configuration-driven by design and there is no close-requirement
        /// entity in this installation, so it reports NOT_APPLICABLE with a setup
        /// message. The alternative - inferring an accrual from journal description text
        /// - is explicitly ruled out: it would produce confident answers with no basis,
        /// which on a close checklist is worse than no answer.
        /// </summary>
        /// <param name="c">Shared evaluation context.</param>
        /// <param name="def">Registry entry.</param>
        /// <returns>Populated <see cref="CheckResult"/>.</returns>
        private CheckResult Eval13(CheckContext c, CheckDef def)
        {
            return NotApplicable(def, "VAS_195_Na13",
                "No accrual or provision expectations are configured - setup required to evaluate");
        }

        // ─────────────────────────────────────────────────────────────────────
        // 14  Fixed asset depreciation not processed                   BLOCKER
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Asset depreciation schedules due in the period that are not completed and
        /// posted. Requires the fixed-asset module; absent, the check does not apply.
        /// </summary>
        /// <param name="c">Shared evaluation context.</param>
        /// <param name="def">Registry entry.</param>
        /// <returns>Populated <see cref="CheckResult"/>.</returns>
        private CheckResult Eval14(CheckContext c, CheckDef def)
        {
            if (!TableExists("VAFAM_AssetSchedule"))
            {
                return NotApplicable(def, "VAS_195_Na14", "The fixed-asset module is not installed");
            }

            return CountSpec(c, def, Spec14(c),
                "VAS_195_Sum14", "asset schedules due in this period are not posted",
                "VAS_195_Clr14", "Depreciation is fully processed for all due assets");
        }

        /// <summary>
        /// Due schedules with no completed, posted depreciation behind them.
        ///
        /// Written to the module's documented column names and guarded by a column probe
        /// per name: this installation carries no fixed-asset module, so the statement
        /// below is unverified against a live schema. A probe miss returns null and the
        /// check reports NOT_APPLICABLE rather than raising a missing-column error.
        /// </summary>
        /// <param name="c">Shared evaluation context.</param>
        /// <returns>Populated <see cref="DetailSpec"/>, or null when the schema differs.</returns>
        private DetailSpec Spec14(CheckContext c)
        {
            if (!TableExists("VAFAM_AssetSchedule")) { return null; }
            if (!ColumnExists("VAFAM_AssetSchedule", "A_Asset_ID")) { return null; }
            if (!ColumnExists("VAFAM_AssetSchedule", "C_Period_ID")) { return null; }

            bool hasAmount = ColumnExists("VAFAM_AssetSchedule", "VAFAM_DepreciationAmt");
            bool hasLine = TableExists("VAFAM_AssetDepreciationLine")
                && ColumnExists("VAFAM_AssetDepreciationLine", "VAFAM_AssetSchedule_ID");

            List<SqlParameter> parameters = Binds();
            parameters.Add(new SqlParameter("@C_Period_ID", c.Period.C_Period_ID));

            /* Complete means the schedule is carried by a depreciation header that is
               finished AND posted. A draft header is not evidence of anything. */
            string posted = hasLine
                ? @" AND NOT EXISTS(SELECT 1 FROM VAFAM_AssetDepreciationLine adl
                        INNER JOIN VAFAM_AssetDepreciation ad ON (ad.VAFAM_AssetDepreciation_ID=adl.VAFAM_AssetDepreciation_ID)
                        WHERE adl.VAFAM_AssetSchedule_ID=sch.VAFAM_AssetSchedule_ID
                          AND adl.IsActive='Y'
                          AND ad.IsActive='Y'
                          AND ad.DocStatus IN (" + DOCSTATUS_FinalList + @")
                          AND COALESCE(ad.Posted,'N')='Y')"
                : "";

            string sql = @"
                SELECT " + TableId("A_Asset") + " AS " + TECH_TABLE + @",
                       sch.A_Asset_ID AS " + TECH_RECORD + @",
                       0 AS " + TECH_WINDOW + @",
                       COALESCE(a.Value,N'') AS Asset_Value,
                       COALESCE(a.Name,N'') AS Asset_Name,
                       COALESCE(ag.Name,N'') AS Asset_Group,
                       " + (hasAmount ? "COALESCE(sch.VAFAM_DepreciationAmt,0)" : "0") + @" AS Doc_Amount
                FROM VAFAM_AssetSchedule sch
                INNER JOIN A_Asset a ON (a.A_Asset_ID=sch.A_Asset_ID)
                LEFT OUTER JOIN A_Asset_Group ag ON (ag.A_Asset_Group_ID=a.A_Asset_Group_ID)
                WHERE sch.IsActive='Y'
                  AND a.IsActive='Y'
                  AND sch.C_Period_ID=@C_Period_ID" + posted;

            List<ColumnDef> columns = new List<ColumnDef>();
            columns.Add(Col("Asset_Value", "VAS_195_AssetValue", "Asset", COLTYPE_DOC, 1.0m));
            columns.Add(Col("Asset_Name", "VAS_195_AssetName", "Asset Name", COLTYPE_TEXT, 1.8m));
            columns.Add(Col("Asset_Group", "VAS_195_AssetGroup", "Group", COLTYPE_TEXT, 1.2m));
            columns.Add(Col("Doc_Amount", "VAS_195_DepreciationAmount", "Depreciation", COLTYPE_AMOUNT, 1.1m));

            return Spec(sql, "sch", "Asset_Value", parameters, columns);
        }

        // ─────────────────────────────────────────────────────────────────────
        // 15  Pending inventory transactions                           BLOCKER
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Inventory documents left unprocessed in the period. Stock quantities and the
        /// balances derived from them are unreliable until they are finished.
        /// </summary>
        /// <param name="c">Shared evaluation context.</param>
        /// <param name="def">Registry entry.</param>
        /// <returns>Populated <see cref="CheckResult"/>.</returns>
        private CheckResult Eval15(CheckContext c, CheckDef def)
        {
            return CountSpec(c, def, Spec15(c),
                "VAS_195_Sum15", "inventory documents are pending in this period",
                "VAS_195_Clr15", "No pending inventory transactions found");
        }

        /// <summary>
        /// One secured branch per registered INVENTORY document, restricted to those
        /// still in a working state.
        /// </summary>
        /// <param name="c">Shared evaluation context.</param>
        /// <returns>Populated <see cref="DetailSpec"/>, or null when no table qualifies.</returns>
        private DetailSpec Spec15(CheckContext c)
        {
            List<DocDef> docs = UsableDocuments(c.Ctx, true, false, true);
            if (docs.Count == 0) { return null; }

            List<SqlParameter> parameters = Binds();
            StringBuilder sql = new StringBuilder();

            for (int i = 0; i < docs.Count; i++)
            {
                DocDef d = docs[i];
                string suffix = "V" + (i + 1).ToString(CultureInfo.InvariantCulture);

                string a = Alias(d);

                string pending = d.HasProcessed
                    ? "(" + a + ".DocStatus IN (" + DOCSTATUS_OpenList + ") OR COALESCE(" + a + ".Processed,'N')='N')"
                    : a + ".DocStatus IN (" + DOCSTATUS_OpenList + ")";

                string where = a + ".IsActive='Y' AND " + a + ".DocStatus NOT IN (" + DOCSTATUS_DeadList + ")"
                    + " AND " + pending
                    + " AND " + PeriodWhere(c, a, d.DateColumn, suffix, parameters)
                    + ScreenFilter(d);

                string branch = DocBranchSelect(d, suffix, parameters) + " WHERE " + where;

                if (i > 0) { sql.Append(" UNION ALL "); }
                sql.Append(SecureBranch(c, branch, a));
            }

            /* Grouped by screen exactly as checks 01 and 02 are, and for the same reason:
               a reader working this list works one document type at a time - every
               material receipt, then every physical inventory - rather than following
               three of them interleaved by date. Screen_Sort is a selected column of
               every branch, which is what lets a UNION's ORDER BY reach it, and the
               screen name itself rides under each document number.

               No Currency column: none of these three tables has a C_Currency_ID, so it
               could only ever render blank. */
            return Spec(sql.ToString(), "", "Screen_Sort,Doc_Date DESC,Doc_Number DESC",
                parameters, DocColumns(false, false));
        }

        // ─────────────────────────────────────────────────────────────────────
        // 16  Inventory costing not completed                          BLOCKER
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Inventory movements in the period that costing has not finished with.
        /// Valuation and cost of goods sold are wrong until it has.
        /// </summary>
        /// <param name="c">Shared evaluation context.</param>
        /// <param name="def">Registry entry.</param>
        /// <returns>Populated <see cref="CheckResult"/>.</returns>
        private CheckResult Eval16(CheckContext c, CheckDef def)
        {
            if (!TableExists("M_Transaction") || !TableExists("M_CostDetail"))
            {
                return NotApplicable(def, "VAS_195_Na16", "No inventory costing data in this installation");
            }

            return CountSpec(c, def, Spec16(c),
                "VAS_195_Sum16", "inventory transactions have incomplete or errored costing",
                "VAS_195_Clr16", "Inventory costing is complete for the selected period");
        }

        /// <summary>
        /// Period transactions with no processed cost detail in the primary accounting
        /// schema.
        ///
        /// Deliberately transaction-level. A cost-closing header being processed says
        /// the RUN finished, not that every transaction in the period was costed by it -
        /// so the test is per movement, against M_CostDetail for the primary schema.
        /// </summary>
        /// <param name="c">Shared evaluation context.</param>
        /// <returns>Populated <see cref="DetailSpec"/>, or null when costing data is absent.</returns>
        private DetailSpec Spec16(CheckContext c)
        {
            if (!TableExists("M_Transaction") || !TableExists("M_CostDetail")) { return null; }

            bool costByTransaction = ColumnExists("M_CostDetail", "M_Transaction_ID");
            bool hasSchema = ColumnExists("M_CostDetail", "C_AcctSchema_ID");
            bool hasProcessed = ColumnExists("M_CostDetail", "Processed");

            /* Without a transaction reference on M_CostDetail there is no reliable way
               to tie a cost row to a movement, and guessing would report every
               transaction as uncosted. */
            if (!costByTransaction) { return null; }

            List<SqlParameter> parameters = Binds();

            StringBuilder costWhere = new StringBuilder();
            costWhere.Append("cd.M_Transaction_ID=mt.M_Transaction_ID AND cd.IsActive='Y'");
            if (hasProcessed) { costWhere.Append(" AND COALESCE(cd.Processed,'N')='Y'"); }
            if (hasSchema) { costWhere.Append(" AND cd.C_AcctSchema_ID=@C_AcctSchema_ID"); }

            string sql = @"
                SELECT " + TableId("M_Transaction") + " AS " + TECH_TABLE + @",
                       mt.M_Transaction_ID AS " + TECH_RECORD + @",
                       0 AS " + TECH_WINDOW + @",
                       mt.MovementDate AS Doc_Date,
                       COALESCE(mt.MovementType,N'') AS Movement_Type,
                       COALESCE(prod.Name,N'') AS Product_Name,
                       COALESCE(loc.Value,N'') AS Locator_Name,
                       COALESCE(wh.Name,N'') AS Warehouse_Name,
                       COALESCE(mt.MovementQty,0) AS Movement_Qty
                FROM M_Transaction mt
                LEFT OUTER JOIN M_Product prod ON (prod.M_Product_ID=mt.M_Product_ID)
                LEFT OUTER JOIN M_Locator loc ON (loc.M_Locator_ID=mt.M_Locator_ID)
                LEFT OUTER JOIN M_Warehouse wh ON (wh.M_Warehouse_ID=loc.M_Warehouse_ID)
                WHERE mt.IsActive='Y'
                  AND " + PeriodWhere(c, "mt", "MovementDate", "T", parameters) + @"
                  AND NOT EXISTS(SELECT 1 FROM M_CostDetail cd WHERE " + costWhere + ")";

            if (hasSchema) { parameters.Add(new SqlParameter("@C_AcctSchema_ID", c.Acct.C_AcctSchema_ID)); }

            List<ColumnDef> columns = new List<ColumnDef>();
            columns.Add(Col("Doc_Date", "VAS_195_TransactionDate", "Transaction Date", COLTYPE_DATE, 1.0m));
            columns.Add(Col("Movement_Type", "VAS_195_MovementType", "Movement", COLTYPE_BADGE, 0.9m));
            columns.Add(Col("Product_Name", "VAS_195_Product", "Product", COLTYPE_TEXT, 1.8m));
            columns.Add(Col("Warehouse_Name", "VAS_195_Warehouse", "Warehouse", COLTYPE_TEXT, 1.2m));
            columns.Add(Col("Locator_Name", "VAS_195_Locator", "Locator", COLTYPE_TEXT, 1.0m));
            columns.Add(Col("Movement_Qty", "VAS_195_Quantity", "Quantity", COLTYPE_QTY, 0.8m));

            return Spec(sql, "mt", "mt.MovementDate DESC,mt.M_Transaction_ID DESC", parameters, columns);
        }

        // ─────────────────────────────────────────────────────────────────────
        // 17  Physical inventory adjustments pending                   WARNING
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Physical counts with unposted differences. Count variances need a decision
        /// before close, but the decision is finance's - hence a warning.
        /// </summary>
        /// <param name="c">Shared evaluation context.</param>
        /// <param name="def">Registry entry.</param>
        /// <returns>Populated <see cref="CheckResult"/>.</returns>
        private CheckResult Eval17(CheckContext c, CheckDef def)
        {
            if (!TableExists("M_Inventory"))
            {
                return NotApplicable(def, "VAS_195_Na17", "No physical inventory documents in this installation");
            }

            return CountSpec(c, def, Spec17(c),
                "VAS_195_Sum17", "physical count lines have unresolved differences",
                "VAS_195_Clr17", "No pending physical count adjustments");
        }

        /// <summary>
        /// Count LINES with a real difference on a document that is not yet finished.
        ///
        /// A completed-but-unposted count belongs to check 02 and is excluded here, so
        /// one document cannot appear as two separate close problems.
        /// </summary>
        /// <param name="c">Shared evaluation context.</param>
        /// <returns>Populated <see cref="DetailSpec"/>, or null when the tables are absent.</returns>
        private DetailSpec Spec17(CheckContext c)
        {
            if (!TableExists("M_Inventory") || !TableExists("M_InventoryLine")) { return null; }

            bool hasDifference = ColumnExists("M_InventoryLine", "DifferenceQty");
            List<SqlParameter> parameters = Binds();

            /* Prefer the stored difference; fall back to the two quantities it is
               derived from where the column is not present. */
            string difference = hasDifference
                ? "COALESCE(il.DifferenceQty,0)"
                : "(COALESCE(il.QtyCount,0)-COALESCE(il.QtyBook,0))";

            string sql = @"
                SELECT " + TableId("M_Inventory") + " AS " + TECH_TABLE + @",
                       i.M_Inventory_ID AS " + TECH_RECORD + @",
                       0 AS " + TECH_WINDOW + @",
                       COALESCE(i.DocumentNo,N'') AS Doc_Number,
                       i.MovementDate AS Doc_Date,
                       COALESCE(wh.Name,N'') AS Warehouse_Name,
                       COALESCE(loc.Value,N'') AS Locator_Name,
                       COALESCE(prod.Name,N'') AS Product_Name,
                       COALESCE(il.QtyBook,0) AS Book_Qty,
                       COALESCE(il.QtyCount,0) AS Count_Qty,
                       " + difference + @" AS Difference_Qty,
                       i.DocStatus AS Doc_Status
                FROM M_InventoryLine il
                INNER JOIN M_Inventory i ON (i.M_Inventory_ID=il.M_Inventory_ID)
                LEFT OUTER JOIN M_Locator loc ON (loc.M_Locator_ID=il.M_Locator_ID)
                LEFT OUTER JOIN M_Warehouse wh ON (wh.M_Warehouse_ID=loc.M_Warehouse_ID)
                LEFT OUTER JOIN M_Product prod ON (prod.M_Product_ID=il.M_Product_ID)
                WHERE il.IsActive='Y'
                  AND i.IsActive='Y'
                  AND i.DocStatus NOT IN (" + DOCSTATUS_FinalList + @")
                  AND i.DocStatus NOT IN (" + DOCSTATUS_DeadList + @")
                  AND " + difference + @"<>0
                  AND " + PeriodWhere(c, "i", "MovementDate", "N", parameters);

            List<ColumnDef> columns = new List<ColumnDef>();
            columns.Add(Col("Doc_Number", "VAS_195_CountNo", "Count No", COLTYPE_DOC, 1.1m));
            columns.Add(Col("Doc_Date", "VAS_195_MovementDate", "Movement Date", COLTYPE_DATE, 0.9m));
            columns.Add(Col("Warehouse_Name", "VAS_195_Warehouse", "Warehouse", COLTYPE_TEXT, 1.1m));
            columns.Add(Col("Locator_Name", "VAS_195_Locator", "Locator", COLTYPE_TEXT, 0.9m));
            columns.Add(Col("Product_Name", "VAS_195_Product", "Product", COLTYPE_TEXT, 1.5m));
            columns.Add(Col("Book_Qty", "VAS_195_BookQty", "Book", COLTYPE_QTY, 0.7m));
            columns.Add(Col("Count_Qty", "VAS_195_CountQty", "Count", COLTYPE_QTY, 0.7m));
            columns.Add(Col("Difference_Qty", "VAS_195_DifferenceQty", "Difference", COLTYPE_QTY, 0.8m));
            columns.Add(Col("Doc_Status", "VAS_195_DocStatus", "Status", COLTYPE_BADGE, 0.8m));

            return Spec(sql, "il", "i.MovementDate DESC,i.DocumentNo DESC,il.M_InventoryLine_ID DESC",
                parameters, columns);
        }

        // ─────────────────────────────────────────────────────────────────────
        // 18  Tax transactions pending posting                         WARNING
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Tax-bearing invoices that have not reached the ledger. Tax reporting draws on
        /// the same postings, so an unposted tax document is incomplete twice over.
        /// </summary>
        /// <param name="c">Shared evaluation context.</param>
        /// <param name="def">Registry entry.</param>
        /// <returns>Populated <see cref="CheckResult"/>.</returns>
        private CheckResult Eval18(CheckContext c, CheckDef def)
        {
            if (!TableExists("C_InvoiceTax"))
            {
                return NotApplicable(def, "VAS_195_Na18", "No tax transactions in this installation");
            }

            return CountSpec(c, def, Spec18(c),
                "VAS_195_Sum18", "tax-bearing documents are pending posting",
                "VAS_195_Clr18", "All tax-bearing documents are posted");
        }

        /// <summary>
        /// Completed invoices in the period carrying a material tax amount that are not
        /// posted.
        /// </summary>
        /// <param name="c">Shared evaluation context.</param>
        /// <returns>Populated <see cref="DetailSpec"/>, or null when tax data is absent.</returns>
        private DetailSpec Spec18(CheckContext c)
        {
            if (!TableExists("C_InvoiceTax")) { return null; }

            List<SqlParameter> parameters = Binds();
            bool hasTaxBase = ColumnExists("C_InvoiceTax", "TaxBaseAmt");

            string sql = @"
                SELECT " + TableId("C_Invoice") + " AS " + TECH_TABLE + @",
                       i.C_Invoice_ID AS " + TECH_RECORD + @",
                       0 AS " + TECH_WINDOW + @",
                       COALESCE(i.DocumentNo,N'') AS Doc_Number,
                       i.DateAcct AS Doc_Date,
                       COALESCE(bp.Name,N'') AS Partner_Name,
                       COALESCE(tax.Name,N'') AS Tax_Name,
                       COALESCE(tax.Rate,0) AS Tax_Rate,
                       " + (hasTaxBase ? "COALESCE(it.TaxBaseAmt,0)" : "0") + @" AS Tax_Base,
                       COALESCE(it.TaxAmt,0) AS Tax_Amount,
                       COALESCE(cur.ISO_Code,N'') AS Currency_Iso,
                       i.DocStatus AS Doc_Status
                FROM C_InvoiceTax it
                INNER JOIN C_Invoice i ON (i.C_Invoice_ID=it.C_Invoice_ID)
                LEFT OUTER JOIN C_Tax tax ON (tax.C_Tax_ID=it.C_Tax_ID)
                LEFT OUTER JOIN C_BPartner bp ON (bp.C_BPartner_ID=i.C_BPartner_ID)
                LEFT OUTER JOIN C_Currency cur ON (cur.C_Currency_ID=i.C_Currency_ID)
                WHERE it.IsActive='Y'
                  AND i.IsActive='Y'
                  AND i.DocStatus IN (" + DOCSTATUS_FinalList + @")
                  AND COALESCE(i.Posted,'N')<>'Y'
                  AND " + PeriodWhere(c, "i", "DateAcct", "X", parameters) + @"
                  AND ABS(COALESCE(it.TaxAmt,0))>@ToleranceT";

            /* Added after the period binds because it appears after them in the text. */
            parameters.Add(new SqlParameter("@ToleranceT", c.Tolerance));

            List<ColumnDef> columns = new List<ColumnDef>();
            columns.Add(Col("Doc_Number", "DocumentNo", "Document No", COLTYPE_DOC, 1.1m));
            columns.Add(Col("Doc_Date", "VAS_195_AccountDate", "Account Date", COLTYPE_DATE, 0.9m));
            columns.Add(Col("Partner_Name", "VAS_195_BusinessPartner", "Business Partner", COLTYPE_TEXT, 1.3m));
            columns.Add(Col("Tax_Name", "VAS_195_TaxName", "Tax", COLTYPE_TEXT, 1.2m));
            columns.Add(Col("Tax_Rate", "VAS_195_TaxRate", "Rate", COLTYPE_NUMBER, 0.6m));
            columns.Add(Col("Tax_Base", "VAS_195_TaxBase", "Tax Base", COLTYPE_DOCAMOUNT, 1.0m));
            columns.Add(Col("Tax_Amount", "VAS_195_TaxAmount", "Tax Amount", COLTYPE_DOCAMOUNT, 1.0m));
            columns.Add(Col("Currency_Iso", "VAS_195_Currency", "Currency", COLTYPE_TEXT, 0.5m));
            columns.Add(Col("Doc_Status", "VAS_195_DocStatus", "Status", COLTYPE_BADGE, 0.8m));

            return Spec(sql, "it", "i.DateAcct DESC,i.DocumentNo DESC", parameters, columns);
        }

        // ─────────────────────────────────────────────────────────────────────
        // 19  Foreign currency revaluation not run                     WARNING
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Whether the period's foreign-currency exposure has been revalued.
        ///
        /// TWO COUNTS, SHORT-CIRCUITED. Is there anything to revalue - a completed or
        /// closed invoice in the period whose transaction currency is not the primary
        /// schema's? If not, there is nothing to ask about and the row PASSES; the second
        /// query never runs. If there is, does a revaluation journal exist for this period
        /// in the primary schema? That answer is the row's outcome.
        ///
        /// PASS when there is nothing to revalue is deliberate, and it is NOT the same as
        /// NOT_APPLICABLE. Not-applicable means the question could not be asked - a module
        /// absent, a column missing. Here the question was asked and answered: the period
        /// holds no foreign-currency invoice, so nothing is outstanding. That is evidence,
        /// and the row says so.
        ///
        /// FAIL, NOT WARNING, and non-blocking with it. The registry classifies this row
        /// WARNING so it never gates the close, but its rule is stated as a pass/fail
        /// test, so the STATUS it reports is PASS or FAIL rather than the WARNING status
        /// <see cref="Counted"/> would give a warning-class row. Classification and status
        /// are separate fields precisely so a row can be one thing and report the other;
        /// IsBlocking is left false, which is what keeps the close available.
        ///
        /// NO DRILL-DOWN. DetailAvailable stays false and no MPC_CLOSE_19 case exists in
        /// BuildDetail, so the row is not clickable AND the detail endpoint has nothing to
        /// answer with even if it is asked. The summary carries the two counts; no
        /// record-level data leaves the server.
        ///
        /// The evidence is the EXISTENCE of an active journal carrying a
        /// FRPT_RevaluationDate inside the period. Deliberately not Posted='Y' and not a
        /// DocStatus test - the requirement is that the revaluation was run for this
        /// period, and whether its journal has reached the ledger yet is check 02's
        /// question, not this one.
        /// </summary>
        /// <param name="c">Shared evaluation context.</param>
        /// <param name="def">Registry entry.</param>
        /// <returns>Populated <see cref="CheckResult"/>.</returns>
        private CheckResult Eval19(CheckContext c, CheckDef def)
        {
            /* The stamp column is what this check reads its evidence from. Without it the
               question genuinely cannot be asked, which is the one case that IS
               not-applicable rather than a pass. */
            if (!TableExists("GL_Journal") || !ColumnExists("GL_Journal", "FRPT_RevaluationDate"))
            {
                return NotApplicable(def, "VAS_195_Na19", "Foreign currency revaluation is not configured");
            }

            int foreignInvoices = ForeignCurrencyInvoiceCount(c);

            if (foreignInvoices == 0)
            {
                return RevaluationResult(def, STATUS_PASS, 0, 0,
                    "VAS_195_Rev19None", "No foreign-currency invoices were found for the selected period");
            }

            int journals = RevaluationJournalCount(c);

            if (journals > 0)
            {
                return RevaluationResult(def, STATUS_PASS, foreignInvoices, journals,
                    "VAS_195_Rev19Found", "A foreign-currency revaluation journal was found for the selected period");
            }

            /* Lower-cased on purpose: the client leads a non-passing summary with its
               RecordCount, so this reads "3 foreign-currency invoices exist, but ...".
               The two PASS messages above are never prefixed and stay full sentences. */
            return RevaluationResult(def, STATUS_FAIL, foreignInvoices, 0,
                "VAS_195_Rev19Missing",
                "foreign-currency invoices exist, but no revaluation journal was found for the selected period");
        }

        /// <summary>
        /// Assembles check 19's one and only outcome: a status, a summary, the two counts
        /// it was decided from, and no drill-down.
        /// </summary>
        /// <param name="def">Registry entry.</param>
        /// <param name="status">STATUS_PASS or STATUS_FAIL.</param>
        /// <param name="foreignInvoices">Foreign-currency invoices found in the period.</param>
        /// <param name="journals">Revaluation journals found for the period.</param>
        /// <param name="summaryKey">AD_Message key for the summary line.</param>
        /// <param name="summaryText">English fallback for the summary line.</param>
        /// <returns>Populated <see cref="CheckResult"/>.</returns>
        private CheckResult RevaluationResult(CheckDef def, string status,
            int foreignInvoices, int journals, string summaryKey, string summaryText)
        {
            CheckResult result = NewResult(def);

            result.Status = status;
            result.SummaryKey = summaryKey;
            result.SummaryText = summaryText;

            /* The count the row stands for is the exposure it found. A passing row that
               found nothing reports zero, which is the honest figure. */
            result.RecordCount = foreignInvoices;
            result.DocumentCount = journals;

            /* Non-blocking whatever the status, and never openable. */
            result.IsBlocking = false;
            result.DetailAvailable = false;

            return result;
        }

        /// <summary>
        /// Completed or closed invoices in the period whose transaction currency is not
        /// the primary accounting schema's - the exposure that would need revaluing.
        ///
        /// Draft and in-progress invoices are deliberately out of scope: they are check
        /// 01's business, and counting them here would raise a revaluation warning about
        /// documents that may never be completed at all. Both sales and purchase invoices
        /// qualify, and so do credit notes - IsSOTrx is not tested, because exposure is
        /// exposure whichever direction it points.
        ///
        /// MRole is applied to C_Invoice on its own, as the only physical table in the
        /// statement, which is what makes the organization scope the ROLE's rather than
        /// the whole database's.
        /// </summary>
        /// <param name="c">Shared evaluation context.</param>
        /// <returns>Invoice count, 0 when there is no exposure.</returns>
        private int ForeignCurrencyInvoiceCount(CheckContext c)
        {
            if (c.Acct.C_Currency_ID <= 0) { return 0; }

            List<SqlParameter> parameters = Binds();

            string sql = @"
                SELECT COUNT(1) AS Invoice_Count
                FROM C_Invoice i
                WHERE i.IsActive='Y'
                  AND i.DocStatus IN (" + DOCSTATUS_FinalList + @")
                  AND i.C_Currency_ID<>@BaseCurrency_ID
                  AND ";

            parameters.Add(new SqlParameter("@BaseCurrency_ID", c.Acct.C_Currency_ID));
            sql += PeriodWhere(c, "i", "DateAcct", "FX", parameters);

            sql = MRole.GetDefault(c.Ctx).AddAccessSQL(sql, "i", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            DataSet ds = DB.ExecuteDataset(sql, parameters.ToArray(), null);
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) { return 0; }

            return Util.GetValueOfInt(ds.Tables[0].Rows[0]["Invoice_Count"]);
        }

        /// <summary>
        /// Active journals of the primary accounting schema carrying a
        /// FRPT_RevaluationDate inside the period - the evidence that revaluation ran.
        ///
        /// The date tested is the revaluation stamp, never the journal's own DateAcct: a
        /// journal booked in one period can revalue another, and it is the period it
        /// revalued that this check is about. A journal in a different accounting schema
        /// is not evidence for this one, which is why the schema is part of the predicate
        /// rather than assumed.
        ///
        /// MRole is applied to GL_Journal on its own, independently of the invoice count's
        /// filter - two physical tables, two access filters, never one over a combined
        /// result.
        /// </summary>
        /// <param name="c">Shared evaluation context.</param>
        /// <returns>Journal count, 0 when no revaluation is recorded for the period.</returns>
        private int RevaluationJournalCount(CheckContext c)
        {
            List<SqlParameter> parameters = Binds();

            string sql = @"
                SELECT COUNT(1) AS Journal_Count
                FROM GL_Journal j
                WHERE j.IsActive='Y'
                  AND j.C_AcctSchema_ID=@C_AcctSchema_ID
                  AND j.FRPT_RevaluationDate IS NOT NULL
                  AND ";

            parameters.Add(new SqlParameter("@C_AcctSchema_ID", c.Acct.C_AcctSchema_ID));
            sql += PeriodWhere(c, "j", "FRPT_RevaluationDate", "RV", parameters);

            sql = MRole.GetDefault(c.Ctx).AddAccessSQL(sql, "j", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            DataSet ds = DB.ExecuteDataset(sql, parameters.ToArray(), null);
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) { return 0; }

            return Util.GetValueOfInt(ds.Tables[0].Rows[0]["Journal_Count"]);
        }

        // ─────────────────────────────────────────────────────────────────────
        // 20  Trial Balance debit <> credit                            BLOCKER
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Whether the period's postings balance.
        ///
        /// The only check here whose headline is a MEASUREMENT rather than a count, so it
        /// runs its own aggregate: an out-of-balance ledger has a difference, not a
        /// number of rows. The row count behind it is the drill-down's business.
        /// </summary>
        /// <param name="c">Shared evaluation context.</param>
        /// <param name="def">Registry entry.</param>
        /// <returns>Populated <see cref="CheckResult"/>.</returns>
        private CheckResult Eval20(CheckContext c, CheckDef def)
        {
            List<SqlParameter> parameters = Binds();
            parameters.Add(new SqlParameter("@AD_Client_ID", c.Ctx.GetAD_Client_ID()));
            parameters.Add(new SqlParameter("@C_AcctSchema_ID", c.Acct.C_AcctSchema_ID));
            parameters.Add(new SqlParameter("@PostingType", POSTINGTYPE_Actual));

            string sql = @"
                SELECT SUM(COALESCE(fa.AmtAcctDr,0)) AS Total_Debit,
                       SUM(COALESCE(fa.AmtAcctCr,0)) AS Total_Credit,
                       SUM(COALESCE(fa.AmtAcctDr,0)-COALESCE(fa.AmtAcctCr,0)) AS Difference
                FROM Fact_Acct fa
                WHERE fa.AD_Client_ID=@AD_Client_ID
                  AND fa.C_AcctSchema_ID=@C_AcctSchema_ID
                  AND fa.PostingType=@PostingType
                  AND fa.IsActive='Y'
                  AND " + PeriodWhere(c, "fa", "DateAcct", "F", parameters);

            /* Secured on Fact_Acct before anything wraps it, exactly as the paged
               drill-down is. */
            sql = MRole.GetDefault(c.Ctx).AddAccessSQL(sql, "fa", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            decimal difference = 0;
            DataSet ds = DB.ExecuteDataset(sql, parameters.ToArray(), null);
            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                difference = Util.GetValueOfDecimal(ds.Tables[0].Rows[0]["Difference"]);
            }

            /* Rounding is applied to the TOTALS, never to each row before summing -
               rounding a million rows would manufacture the very difference this check
               is looking for. */
            bool unequal = Math.Abs(difference) > c.Tolerance;

            CheckResult result = NewResult(def);
            result.Amount = difference;
            result.DetailAvailable = true;

            if (unequal)
            {
                result.Status = STATUS_FAIL;
                result.IsBlocking = true;
                result.SummaryKey = "VAS_195_Sum20";
                result.SummaryText = "Trial Balance is out of balance";
            }
            else
            {
                result.Status = STATUS_PASS;
                result.SummaryKey = "VAS_195_Clr20";
                result.SummaryText = "Trial Balance debit and credit are equal";
            }

            return result;
        }

        /// <summary>
        /// Organisation and account level movement for the period, so an imbalance can be
        /// traced to where it came from.
        /// </summary>
        /// <param name="c">Shared evaluation context.</param>
        /// <returns>Populated <see cref="DetailSpec"/>.</returns>
        private DetailSpec Spec20(CheckContext c)
        {
            List<SqlParameter> parameters = Binds();
            parameters.Add(new SqlParameter("@AD_Client_ID", c.Ctx.GetAD_Client_ID()));
            parameters.Add(new SqlParameter("@C_AcctSchema_ID", c.Acct.C_AcctSchema_ID));
            parameters.Add(new SqlParameter("@PostingType", POSTINGTYPE_Actual));

            string sql = @"
                SELECT 0 AS " + TECH_TABLE + @",
                       0 AS " + TECH_RECORD + @",
                       0 AS " + TECH_WINDOW + @",
                       COALESCE(org.Name,N'') AS Org_Name,
                       COALESCE(ev.Value,N'') AS Account_Value,
                       COALESCE(ev.Name,N'') AS Account_Name,
                       SUM(COALESCE(fa.AmtAcctDr,0)) AS Period_Debit,
                       SUM(COALESCE(fa.AmtAcctCr,0)) AS Period_Credit,
                       SUM(COALESCE(fa.AmtAcctDr,0)-COALESCE(fa.AmtAcctCr,0)) AS Difference
                FROM Fact_Acct fa
                LEFT OUTER JOIN AD_Org org ON (org.AD_Org_ID=fa.AD_Org_ID)
                LEFT OUTER JOIN C_ElementValue ev ON (ev.C_ElementValue_ID=fa.Account_ID)
                WHERE fa.AD_Client_ID=@AD_Client_ID
                  AND fa.C_AcctSchema_ID=@C_AcctSchema_ID
                  AND fa.PostingType=@PostingType
                  AND fa.IsActive='Y'
                  AND " + PeriodWhere(c, "fa", "DateAcct", "F", parameters);

            DetailSpec spec = new DetailSpec();
            spec.Sql = sql;
            spec.MainAlias = "fa";
            spec.GroupBy = "COALESCE(org.Name,N''),COALESCE(ev.Value,N''),COALESCE(ev.Name,N'')";
            spec.OrderBy = "Org_Name,Account_Value";
            spec.Params = parameters;

            List<ColumnDef> columns = new List<ColumnDef>();
            columns.Add(Col("Org_Name", "VAS_195_Organization", "Organization", COLTYPE_TEXT, 1.1m));
            columns.Add(Col("Account_Value", "VAS_195_Account", "Account", COLTYPE_TEXT, 0.8m));
            columns.Add(Col("Account_Name", "VAS_195_AccountName", "Account Name", COLTYPE_TEXT, 1.8m));
            columns.Add(Col("Period_Debit", "VAS_195_PeriodDebit", "Debit", COLTYPE_AMOUNT, 1.0m));
            columns.Add(Col("Period_Credit", "VAS_195_PeriodCredit", "Credit", COLTYPE_AMOUNT, 1.0m));
            columns.Add(Col("Difference", "VAS_195_Difference", "Difference", COLTYPE_AMOUNT, 1.0m));
            spec.Columns = columns;

            return spec;
        }

        // ─────────────────────────────────────────────────────────────────────
        // 21  Bank accounts fully reconciled                             CHECK
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Positive confirmation that every bank account with period activity has been
        /// reconciled. Reports completeness rather than exceptions - the unresolved items
        /// themselves stay visible under check 04, so nothing is hidden by this being a
        /// CHECK rather than a warning.
        /// </summary>
        /// <param name="c">Shared evaluation context.</param>
        /// <param name="def">Registry entry.</param>
        /// <returns>Populated <see cref="CheckResult"/>.</returns>
        private CheckResult Eval21(CheckContext c, CheckDef def)
        {
            List<BankStat> accounts = BankReconciliation(c);

            if (accounts.Count == 0)
            {
                return NotApplicable(def, "VAS_195_Na21", "No bank activity in the selected period");
            }

            int complete = 0;
            for (int i = 0; i < accounts.Count; i++)
            {
                if (accounts[i].Unreconciled == 0) { complete++; }
            }

            CheckResult result = NewResult(def);
            result.RecordCount = accounts.Count - complete;
            result.DocumentCount = accounts.Count;
            result.DetailAvailable = true;

            if (complete == accounts.Count)
            {
                result.Status = STATUS_COMPLETE;
                result.SummaryKey = "VAS_195_Clr21";
                result.SummaryText = "All active bank accounts are fully reconciled";
            }
            else
            {
                result.Status = STATUS_INCOMPLETE;
                result.SummaryKey = "VAS_195_Sum21";
                result.SummaryText = "bank accounts are not fully reconciled";
            }

            return result;
        }

        /// <summary>
        /// One row per bank account with period activity: total items, reconciled,
        /// unreconciled.
        ///
        /// Built on the SAME payment population check 04 examines, so the two can never
        /// tell different stories about the same account - one counts the accounts, the
        /// other lists the items.
        /// </summary>
        /// <param name="c">Shared evaluation context.</param>
        /// <returns>Populated <see cref="DetailSpec"/>.</returns>
        private DetailSpec Spec21(CheckContext c)
        {
            List<SqlParameter> parameters = Binds();

            string sql = @"
                SELECT 0 AS " + TECH_TABLE + @",
                       0 AS " + TECH_RECORD + @",
                       0 AS " + TECH_WINDOW + @",
                       COALESCE(bank.Name,N'') AS Bank_Name,
                       COALESCE(ba.Name,N'') AS Bank_Account,
                       COALESCE(cur.ISO_Code,N'') AS Currency_Iso,
                       COUNT(1) AS Total_Items,
                       SUM(CASE WHEN COALESCE(p.IsReconciled,'N')='Y' THEN 1 ELSE 0 END) AS Reconciled_Items,
                       SUM(CASE WHEN COALESCE(p.IsReconciled,'N')<>'Y' THEN 1 ELSE 0 END) AS Unreconciled_Items
                FROM C_Payment p
                INNER JOIN C_BankAccount ba ON (ba.C_BankAccount_ID=p.C_BankAccount_ID)
                LEFT OUTER JOIN C_Bank bank ON (bank.C_Bank_ID=ba.C_Bank_ID)
                LEFT OUTER JOIN C_Currency cur ON (cur.C_Currency_ID=ba.C_Currency_ID)
                WHERE p.IsActive='Y'
                  AND ba.IsActive='Y'
                  AND p.DocStatus IN (" + DOCSTATUS_FinalList + @")
                  AND COALESCE(p.IsReversal,'N')='N'
                  AND " + PeriodWhere(c, "p", "DateAcct", "P", parameters);

            DetailSpec spec = new DetailSpec();
            spec.Sql = sql;
            spec.MainAlias = "p";
            spec.GroupBy = "COALESCE(bank.Name,N''),COALESCE(ba.Name,N''),COALESCE(cur.ISO_Code,N'')";
            spec.OrderBy = "Bank_Name,Bank_Account";
            spec.Params = parameters;

            List<ColumnDef> columns = new List<ColumnDef>();
            columns.Add(Col("Bank_Name", "VAS_195_Bank", "Bank", COLTYPE_TEXT, 1.3m));
            columns.Add(Col("Bank_Account", "VAS_195_BankAccount", "Bank Account", COLTYPE_TEXT, 1.5m));
            columns.Add(Col("Currency_Iso", "VAS_195_Currency", "Currency", COLTYPE_TEXT, 0.6m));
            columns.Add(Col("Total_Items", "VAS_195_TotalItems", "Total", COLTYPE_NUMBER, 0.7m));
            columns.Add(Col("Reconciled_Items", "VAS_195_ReconciledItems", "Reconciled", COLTYPE_NUMBER, 0.8m));
            columns.Add(Col("Unreconciled_Items", "VAS_195_UnreconciledItems", "Unreconciled", COLTYPE_NUMBER, 0.9m));
            spec.Columns = columns;

            return spec;
        }

        /// <summary>Per-bank-account reconciliation counts for the period.</summary>
        /// <param name="c">Shared evaluation context.</param>
        /// <returns>One entry per bank account with activity (never null).</returns>
        private List<BankStat> BankReconciliation(CheckContext c)
        {
            List<BankStat> accounts = new List<BankStat>();

            DetailSpec spec = Spec21(c);
            List<DetailRow> rows = PageOf(c.Ctx, spec, 1, PAGESIZE_MAX);

            for (int i = 0; i < rows.Count; i++)
            {
                object value;

                BankStat stat = new BankStat();
                if (rows[i].Cells.TryGetValue("Unreconciled_Items", out value))
                {
                    stat.Unreconciled = Util.GetValueOfInt(value);
                }
                accounts.Add(stat);
            }

            return accounts;
        }

        // ─────────────────────────────────────────────────────────────────────
        // 22  Required document base types still open                  WARNING
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Document base types still open on the selected period - a readiness
        /// indicator, since closing the period is what will shut them.
        /// </summary>
        /// <param name="c">Shared evaluation context.</param>
        /// <param name="def">Registry entry.</param>
        /// <returns>Populated <see cref="CheckResult"/>.</returns>
        private CheckResult Eval22(CheckContext c, CheckDef def)
        {
            return CountSpec(c, def, Spec22(c),
                "VAS_195_Sum22", "document base types are still open",
                "VAS_195_Clr22", "All document base types are closed");
        }

        /// <summary>
        /// The open control rows of the selected period, with the base type resolved to
        /// its translated display name.
        ///
        /// No required/ignored base-type policy exists in this installation, so ALL open
        /// base types are shown - the documented default. The label comes from
        /// AD_Ref_List for the session language, never from a hard-coded map: DocBaseType
        /// stores a two-letter code that means nothing to a reader.
        /// </summary>
        /// <param name="c">Shared evaluation context.</param>
        /// <returns>Populated <see cref="DetailSpec"/>.</returns>
        private DetailSpec Spec22(CheckContext c)
        {
            List<SqlParameter> parameters = Binds();
            parameters.Add(new SqlParameter("@AD_Language", ctxLanguage(c)));
            parameters.Add(new SqlParameter("@C_Period_ID", c.Period.C_Period_ID));
            parameters.Add(new SqlParameter("@PeriodStatus", PERIODSTATUS_Open));

            string sql = @"
                SELECT 0 AS " + TECH_TABLE + @",
                       0 AS " + TECH_RECORD + @",
                       0 AS " + TECH_WINDOW + @",
                       pc.DocBaseType AS Base_Type,
                       COALESCE(rlt.Name,rl.Name,pc.DocBaseType) AS Base_Type_Name,
                       pc.PeriodStatus AS Period_Status,
                       pc.Updated AS Updated_On,
                       COALESCE(u.Name,N'') AS Updated_By
                FROM C_PeriodControl pc
                LEFT OUTER JOIN AD_Ref_List rl ON (rl.Value=pc.DocBaseType AND rl.IsActive='Y' AND rl.AD_Reference_ID=(SELECT c2.AD_Reference_Value_ID FROM AD_Column c2 INNER JOIN AD_Table t2 ON (t2.AD_Table_ID=c2.AD_Table_ID) WHERE t2.TableName='C_PeriodControl' AND c2.ColumnName='DocBaseType' AND c2.IsActive='Y'))
                LEFT OUTER JOIN AD_Ref_List_Trl rlt ON (rlt.AD_Ref_List_ID=rl.AD_Ref_List_ID AND rlt.AD_Language=@AD_Language AND rlt.IsActive='Y')
                LEFT OUTER JOIN AD_User u ON (u.AD_User_ID=pc.UpdatedBy)
                WHERE pc.IsActive='Y'
                  AND pc.C_Period_ID=@C_Period_ID
                  AND pc.PeriodStatus=@PeriodStatus";

            List<ColumnDef> columns = new List<ColumnDef>();
            columns.Add(Col("Base_Type", "VAS_195_BaseTypeCode", "Code", COLTYPE_TEXT, 0.5m));
            columns.Add(Col("Base_Type_Name", "VAS_195_BaseTypeName", "Document Base Type", COLTYPE_TEXT, 2.0m));
            columns.Add(Col("Period_Status", "VAS_195_PeriodStatus", "Status", COLTYPE_BADGE, 0.8m));
            columns.Add(Col("Updated_On", "VAS_195_LastUpdated", "Last Updated", COLTYPE_DATE, 1.0m));
            columns.Add(Col("Updated_By", "VAS_195_UpdatedBy", "Updated By", COLTYPE_TEXT, 1.2m));

            return Spec(sql, "pc", "Base_Type_Name", parameters, columns);
        }

        // ─────────────────────────────────────────────────────────────────────
        // 23  Prior period unexpectedly open                           WARNING
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Earlier periods still carrying open controls - a back-posting risk that
        /// governance should have a view on.
        /// </summary>
        /// <param name="c">Shared evaluation context.</param>
        /// <param name="def">Registry entry.</param>
        /// <returns>Populated <see cref="CheckResult"/>.</returns>
        private CheckResult Eval23(CheckContext c, CheckDef def)
        {
            return CountSpec(c, def, Spec23(c),
                "VAS_195_Sum23", "prior periods have unexpected open controls",
                "VAS_195_Clr23", "No unexpected prior open periods found");
        }

        /// <summary>
        /// Every standard period of the primary calendar that ENDED before the selected
        /// period began and still has an open control.
        ///
        /// All of them, not just the immediately preceding one, and derived from the
        /// periods' actual dates rather than by subtracting a month - a calendar with a
        /// 53-week year or an adjustment period would defeat any arithmetic shortcut.
        /// </summary>
        /// <param name="c">Shared evaluation context.</param>
        /// <returns>Populated <see cref="DetailSpec"/>.</returns>
        private DetailSpec Spec23(CheckContext c)
        {
            /* @PeriodStatus2 is bound FIRST because its occurrence - the open-control
               count in the SELECT list - comes first in the statement text, ahead of
               everything in the WHERE clause. */
            List<SqlParameter> parameters = Binds();
            parameters.Add(new SqlParameter("@PeriodStatus2", PERIODSTATUS_Open));
            parameters.Add(new SqlParameter("@C_Calendar_ID", c.Acct.C_Calendar_ID));
            parameters.Add(new SqlParameter("@PeriodType", PERIODTYPE_Standard));
            parameters.Add(new SqlParameter("@SelectedStart", c.PeriodStart));
            parameters.Add(new SqlParameter("@PeriodStatus", PERIODSTATUS_Open));

            string sql = @"
                SELECT " + TableId("C_Period") + " AS " + TECH_TABLE + @",
                       p.C_Period_ID AS " + TECH_RECORD + @",
                       0 AS " + TECH_WINDOW + @",
                       COALESCE(y.FiscalYear,N'') AS Fiscal_Year,
                       COALESCE(p.Name,N'') AS Period_Name,
                       p.StartDate AS Start_Date,
                       p.EndDate AS End_Date,
                       (SELECT COUNT(1) FROM C_PeriodControl pc2 WHERE pc2.C_Period_ID=p.C_Period_ID AND pc2.IsActive='Y' AND pc2.PeriodStatus=@PeriodStatus2) AS Open_Controls
                FROM C_Period p
                INNER JOIN C_Year y ON (y.C_Year_ID=p.C_Year_ID)
                WHERE p.IsActive='Y'
                  AND y.IsActive='Y'
                  AND y.C_Calendar_ID=@C_Calendar_ID
                  AND p.PeriodType=@PeriodType
                  AND p.EndDate<@SelectedStart
                  AND EXISTS(SELECT 1 FROM C_PeriodControl pc WHERE pc.C_Period_ID=p.C_Period_ID AND pc.IsActive='Y' AND pc.PeriodStatus=@PeriodStatus)";

            List<ColumnDef> columns = new List<ColumnDef>();
            columns.Add(Col("Fiscal_Year", "VAS_195_FiscalYear", "Fiscal Year", COLTYPE_TEXT, 0.9m));
            columns.Add(Col("Period_Name", "VAS_195_PeriodName", "Period", COLTYPE_DOC, 1.4m));
            columns.Add(Col("Start_Date", "VAS_195_StartDate", "Start Date", COLTYPE_DATE, 1.0m));
            columns.Add(Col("End_Date", "VAS_195_EndDate", "End Date", COLTYPE_DATE, 1.0m));
            columns.Add(Col("Open_Controls", "VAS_195_OpenControls", "Open Controls", COLTYPE_NUMBER, 0.9m));

            return Spec(sql, "p", "p.StartDate DESC,p.C_Period_ID DESC", parameters, columns);
        }

        /// <summary>The session language, defaulted defensively.</summary>
        /// <param name="c">Shared evaluation context.</param>
        /// <returns>AD_Language code.</returns>
        private string ctxLanguage(CheckContext c)
        {
            return ctxLanguage(c.Ctx);
        }

        /// <summary>
        /// The session language, defaulted defensively. Overload for the discovery pass,
        /// which runs before a CheckContext exists.
        /// </summary>
        /// <param name="ctx">Session context.</param>
        /// <returns>AD_Language code.</returns>
        private string ctxLanguage(Ctx ctx)
        {
            string language = ctx == null ? "" : ctx.GetAD_Language();
            return string.IsNullOrEmpty(language) ? "en_US" : language;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Local contracts
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// One entry of the server-side posting-document registry. Every flag is filled
        /// from a dictionary probe, never assumed.
        /// </summary>
        private class DocDef
        {
            public string TableName { get; set; }
            public string KeyColumn { get; set; }
            public string DateColumn { get; set; }
            public string AmountColumn { get; set; }
            public bool IsInventory { get; set; }

            /// <summary>The screen this branch stands for; the sort order's basis.</summary>
            public string ScreenLabel { get; set; }

            /// <summary>
            /// The screen's window - what the drill-down opens, so a row lands on the
            /// screen it came from rather than on the table's default one.
            /// </summary>
            public int AD_Window_ID { get; set; }

            /// <summary>
            /// The tab filter that makes this branch a different list from its siblings,
            /// already resolved against the session context; "" when the branch covers
            /// the whole table.
            /// </summary>
            public string WhereClause { get; set; }

            /// <summary>
            /// 1-based position in screen-name order, emitted into each UNION branch as
            /// the sort key the resolved screen label cannot be.
            /// </summary>
            public int ScreenRank { get; set; }

            public int AD_Table_ID { get; set; }

            /// <summary>
            /// Whether the table runs a document workflow. False for the posting
            /// artefacts - M_MatchInv, M_MatchPO - which have a Posted column and a
            /// Posted button but no DocStatus, and which check 02 must still list.
            /// </summary>
            public bool HasDocStatus { get; set; }

            public bool HasPosted { get; set; }
            public bool HasProcessed { get; set; }
            public bool HasDocumentNo { get; set; }
            public bool HasBPartner { get; set; }
            public bool HasCurrency { get; set; }
            public bool HasAmount { get; set; }

            /// <summary>
            /// Join-key expression for C_DocType over the `doc` alias, preferring the
            /// TARGET type; "" when the table carries no document type at all.
            /// </summary>
            public string DocTypeKey { get; set; }
        }

        /// <summary>
        /// One screen a discovered table is shown on: the window to open, its name, and
        /// the tab filter that tells it apart from the table's other screens.
        /// </summary>
        private class ScreenDef
        {
            public int AD_Window_ID { get; set; }
            public string WindowName { get; set; }

            /// <summary>Resolved and checked; "" when the tab carries no usable filter.</summary>
            public string WhereClause { get; set; }

            /// <summary>
            /// Whether the tab displays the Posted button. The window a reader would go
            /// to in order to POST something is the right one to name and to zoom to,
            /// and it is the same window VAS_198 names for the same record.
            /// </summary>
            public bool HasPostedField { get; set; }
        }

        /// <summary>One configured control account and its closing balance.</summary>
        private class SuspenseAcct
        {
            public int Account_ID { get; set; }
            public decimal ClosingBalance { get; set; }
        }

        /// <summary>One bank account's reconciliation counts for the period.</summary>
        private class BankStat
        {
            public int Unreconciled { get; set; }
        }
    }
}
