/******************************************************
 * Module Name    : VASLogic
 * Purpose        : Unposted Accounting Entries dashboard widget data
 * chronological  : Development
 * Created Date   : 2026-08-21
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
    /// Module Name : VAS_198_UnPostedAccountEntries
    /// Purpose     : Backs the VAS_198_UnPostedAccountEntriesWidget dashboard widget.
    ///               Every accounting document of ONE open period that has reached a
    ///               completed / closed state but carries no accounting entry
    ///               (COALESCE(Posted,'N')&lt;&gt;'Y'), grouped by transaction type, each
    ///               with a document count, a base-currency value and an openable
    ///               paged record list.
    ///
    ///               NOTHING here is hard-coded to C_Invoice / M_InOut / C_Payment /
    ///               GL_Journal. The transaction types are DISCOVERED from the
    ///               Application Dictionary:
    ///
    ///                 AD_Table -> AD_Column(Posted) -> AD_Field -> AD_Tab -> AD_Window
    ///
    ///               A table qualifies when it is an active, non-view physical table
    ///               carrying an active Posted column and reachable through the MENU -
    ///               it has an active window whose header tab is over it. The menu
    ///               requirement is what keeps purely technical tables with a Posted
    ///               column out of a user-facing card, and it is the same test VAS_195
    ///               applies, so the two agree on what counts as a document.
    ///
    ///               A SCREEN is any menu-reachable window whose HEADER tab is over the
    ///               table. It is deliberately NOT "a window that DISPLAYS the Posted
    ///               field": which columns a window chooses to show has nothing to do
    ///               with which records it holds, and requiring that field silently
    ///               under-split every table whose windows do not all declare one.
    ///               M_Inventory is the case that shows it - Inventory Count and Internal
    ///               Use Inventory are one table separated by IsInternalUse, and losing
    ///               either screen left its documents reported under, and zoomed into, a
    ///               window whose own filter excludes them. A table with no displayed
    ///               header tab at all still falls back to its primary window and
    ///               contributes one unsplit row rather than vanishing from the card.
    ///
    ///               A row of the card is a SCREEN, not a table: one table displayed
    ///               on several windows is several rows, so C_Invoice appears as AP
    ///               Invoice / AR Invoice / Expense Invoice, M_InOut as GRN / Delivery
    ///               Order / the return flavours, C_Order as Purchase Order / Sales
    ///               Order / RMA / Blanket, C_Payment as AP Payment / AR Receipt,
    ///               M_Inventory as Inventory Count / Internal Use. Nothing in that
    ///               list is coded here - the names are the tenant's own windows,
    ///               translated for the session language.
    ///
    ///               What makes two windows over one table different LISTS rather than
    ///               the same list twice is the tab's own WhereClause - the predicate
    ///               the framework itself applies when it opens that window. It is
    ///               added to each screen's query, and because a WhereClause qualifies
    ///               its columns with the TABLE name, every generated statement aliases
    ///               the source table to its own name rather than to something short.
    ///
    ///               Those clauses overwhelmingly separate windows by DOCUMENT TYPE
    ///               rather than by a flag on the document - Delivery Order from Material
    ///               Receipt, Blanket Sales Order from Sales Order - so the statement
    ///               JOINS C_DocType under its own name whenever a screen's filter names
    ///               it, and a clause's own subquery aliases resolve themselves. Neither
    ///               used to be accepted, and the windows they describe were therefore
    ///               inseparable and collapsed into one another's rows.
    ///
    ///               Where a clause still cannot separate the windows - @context@
    ///               variables this widget cannot resolve, a table beyond C_DocType that
    ///               this statement does not join, or no clause at all - the table
    ///               collapses back to ONE row covering all its records. One honest row
    ///               beats several rows each listing the same documents.
    ///
    ///               THE SPLIT IS EXHAUSTIVE. Whatever the screens claim, every record
    ///               of the table lands in at least one row of the card, because the
    ///               filtered screens are always accompanied by a row carrying their
    ///               complement. Four separate ways a record used to escape that
    ///               guarantee are now closed: a table all of whose screens are filtered
    ///               had no window to hang the complement on and so had no complement
    ///               row at all - and, worse, threw the whole split away rather than let
    ///               two rows share a window; the complement was written as NOT(a) AND
    ///               NOT(b), which is NULL - and therefore not true - wherever a sibling
    ///               clause tests a NULLable column; and a clause naming a table outside
    ///               the FROM made the whole query throw, whereupon that screen was
    ///               logged and skipped. Each one silently under-reported unposted
    ///               documents that VAS_195, which applies no tab filter at all, went on
    ///               listing.
    ///
    ///               AND IT IS DISJOINT. Two windows over one table can overlap - a
    ///               Blanket Sales Order is a sales order, so it satisfies the Sales
    ///               Order tab filter as well as its own - and a record matching both
    ///               used to be counted, and valued, under each. Screens are therefore
    ///               ordered NARROWEST FIRST and each one claims only what no narrower
    ///               sibling already has, so a blanket order is reported under Blanket
    ///               Sales Order alone: the window it actually opens in.
    ///
    ///               Zoom therefore needs no rule at all: a row IS a screen, so it
    ///               opens that screen.
    ///
    ///               Dynamic SQL, safely: a bind parameter cannot be a table or column
    ///               identifier, so the physical statement is composed server-side -
    ///               but ONLY from identifiers the dictionary itself returned, each of
    ///               which is re-checked against <see cref="IsSafeIdentifier"/> before
    ///               it is concatenated. No UI value ever becomes an identifier; every
    ///               business filter (client, period bounds, base currency) stays a
    ///               parameter.
    ///
    ///               Valuation is deliberately NOT metadata-driven. Different
    ///               transaction tables store value differently and a column merely
    ///               named ...Amt / ...Total is not evidence of an accounting value,
    ///               so it comes from one of two trusted maps and is then CONFIRMED
    ///               to exist by the same dictionary probe:
    ///
    ///                 header total   <see cref="AmountColumnByTable"/> - GrandTotal,
    ///                                PayAmt, TotalDr - in the document's own
    ///                                currency, converted to base
    ///                 line valued    <see cref="LineValueStrategies"/> - the stored
    ///                                cost the posting process itself uses, times the
    ///                                moved quantity, summed over the lines. WHICH
    ///                                cost column that is differs per table: a receipt
    ///                                or movement line reads PostCurrentCostPrice
    ///                                falling back to CurrentCostPrice, an inventory
    ///                                line reads PriceCost. ALREADY base currency, so
    ///                                never converted again
    ///
    ///               A discovered table in neither map reports its document count and
    ///               no value - that is safer than reporting the wrong accounting
    ///               figure.
    ///
    ///               Period source: the open periods of the tenant's PRIMARY calendar
    ///               (AD_ClientInfo.C_Calendar_ID) - a period qualifies when at least
    ///               one active C_PeriodControl row of it is Open. Documents are
    ///               bounded by DateAcct, or by MovementDate on the inventory tables
    ///               that carry no DateAcct at all - a short explicit fallback, not a
    ///               scan for anything date-shaped. A table with neither column is
    ///               excluded rather than bounded by a guessed one.
    ///
    ///               MRole row-level security is applied to the main physical table of
    ///               every user-facing query: C_Period alias p, AD_Table alias t for
    ///               the dictionary probes, and the DISCOVERED table alias src for
    ///               each transaction query - each source is filtered independently,
    ///               never once over a combined result. GROUP BY, ORDER BY and the
    ///               paging suffix are appended AFTER AddAccessSQL so the FROM-clause
    ///               parser is not confused by a trailing clause. Compatible with
    ///               PostgreSQL and Oracle.
    /// Chronological development:
    ///   VAI145      2026-08-21 Created
    ///   VAI145      2026-08-25 Period end bound made exclusive so a time-stamped
    ///                          document on the period's last day is not dropped
    ///   VAI145      2026-08-25 Screen split made exhaustive, and a table with no
    ///                          Posted-bearing tab falls back to its primary window
    ///                          instead of vanishing - both to agree with VAS_195
    ///   VAI154      2026-08-26 Screens are now every menu-reachable header window over
    ///                          the table, not only the ones displaying Posted; the
    ///                          catch-all row may share a window with a filtered screen
    ///                          instead of collapsing the split; and overlapping screens
    ///                          are made disjoint narrowest-first. Together these are
    ///                          what put a document under its OWN screen - Inventory
    ///                          Count vs Internal Use Inventory, Sales Order vs Blanket
    ///                          Sales Order - and therefore what makes Zoom open the
    ///                          window that actually holds it
    ///   VAI154      2026-08-26 A screen filter may name C_DocType (joined) or its own
    ///                          subquery aliases - which is how most windows over one
    ///                          table are actually told apart; and a colliding row is
    ///                          never qualified with its tab name, so no more
    ///                          "Delivery Order · Delivery Order" or "Sales Order · Order"
    /// </summary>
    public class VAS_198_UnPostedAccountEntriesModel
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_198_UnPostedAccountEntriesModel).FullName);

        /* Error tokens; the client resolves the label. */
        public const string ERROR_INVALID_REQUEST = "INVALID";
        public const string ERROR_NO_PERIOD = "NOPERIOD";

        /* C_PeriodControl.PeriodStatus stored code for an open control row. */
        private const string PERIODSTATUS_Open = "O";

        /* The one AD_Message this class needs: the word that qualifies a catch-all row
           whose name would otherwise repeat a real screen's. Seeded through System
           Messages; unseeded it renders as readable English - see OtherRecordsLabel. */
        private const string MESSAGE_OTHERRECORDS = "VAS_198_OtherRecords";

        /* The dictionary column that marks a table as an accounting source, and the
           document states worth chasing. A document that is still drafted or in
           progress is not an unposted accounting entry - it is unfinished work, and
           belongs to a different widget. */
        private const string COLUMN_POSTED = "Posted";
        private const string DOCSTATUS_ACTIONABLE = "'CO','CL'";

        /* Detail paging guard rails. The client asks for a page size; anything
           outside this band is clamped so a crafted request cannot pull a whole
           table into one response. */
        private const int PAGESIZE_MIN = 1;
        private const int PAGESIZE_MAX = 100;
        private const int PAGESIZE_DEFAULT = 8;

        /* Longest identifier the dictionary may hand us. Both back ends cap well
           below this; the bound exists so a corrupt dictionary row cannot grow the
           generated statement without limit. */
        private const int IDENTIFIER_MAX = 40;

        /* Longest tab filter this widget will paste into its own SQL. Real ones are a
           predicate or two; anything past this is not a tab filter. */
        private const int WHERECLAUSE_MAX = 500;

        /* The same bound for a filter this class COMPOSED rather than read: a complement
           row carries one negated sibling clause per filtered screen, so it is legitimately
           several times the length a single tab filter may be. Still bounded - a table with
           an implausible number of screens collapses to one row instead of growing the
           statement without limit. */
        private const int WHERECLAUSE_COMPOSED_MAX = 8000;

        /// <summary>
        /// The trusted amount strategy: which column carries the accounting value of
        /// a given transaction table. Deliberately a fixed map and NOT a metadata
        /// scan - a column called PayAmt on one table is the document's value, on
        /// another it is a line's share of one, and no dictionary flag distinguishes
        /// them. A table absent from this map shows its count with no value.
        /// Every entry is still confirmed against AD_Column before it is used, so a
        /// map row for a column a given installation does not have degrades to "no
        /// value" instead of failing the query.
        /// </summary>
        private static readonly Dictionary<string, string> AmountColumnByTable =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "C_Invoice", "GrandTotal" },
                { "C_Order", "GrandTotal" },
                { "C_Payment", "PayAmt" },
                { "GL_Journal", "TotalDr" }
            };

        /// <summary>
        /// The second trusted amount strategy, for documents whose value lives on
        /// their LINES rather than in a header total: an inventory movement has no
        /// GrandTotal, and its accounting value is the stored cost of what moved.
        ///
        /// The cost columns are the very ones the posting process uses, so the figure
        /// on the card is the figure that will hit the ledger. They are declared PER
        /// STRATEGY, not shared, because the tables do not agree on which one carries
        /// it: a receipt or a movement line values at its posting cost falling back to
        /// its standing one, while an inventory line values at PriceCost. Those costs
        /// are ALREADY in the tenant's base currency, which is why a line strategy
        /// never goes near currencyConvert: converting a base-currency cost a second
        /// time would silently inflate it.
        ///
        /// Like the header map this is a fixed list, and every column it names -
        /// link, quantity and cost alike - is confirmed against AD_Column before it
        /// reaches the SQL. A strategy whose columns this installation does not have
        /// degrades to "no value" rather than to a wrong number or a broken query.
        /// </summary>
        private static readonly List<LineValueStrategy> LineValueStrategies =
            new List<LineValueStrategy>
            {
                /* A receipt line's quantity is a single column. */
                new LineValueStrategy("M_InOut", "M_InOutLine", "M_InOut_ID",
                    new string[] { "MovementQty" }, "QtyInternalUse",
                    new string[] { "PostCurrentCostPrice", "CurrentCostPrice" }),

                /* Same shape as a receipt line: one quantity that moved, valued at
                   the line's own stored cost. */
                new LineValueStrategy("M_Movement", "M_MovementLine", "M_Movement_ID",
                    new string[] { "MovementQty" }, "",
                    new string[] { "PostCurrentCostPrice", "CurrentCostPrice" }),

                /* A physical inventory posts the DIFFERENCE it found - counted less
                   booked - while an internal-use inventory posts QtyInternalUse and
                   leaves both of those at zero. Summing the two forms is what makes
                   one expression serve both, and it is why the quantity here is a
                   difference rather than a column.

                   PriceCost is what an inventory line values at, and it is already a
                   base-currency figure. The two receipt-style cost columns stay in the
                   list behind it only as a fallback for a schema that has no PriceCost
                   - they are not what this table posts with. */
                new LineValueStrategy("M_Inventory", "M_InventoryLine", "M_Inventory_ID",
                    new string[] { "QtyCount", "QtyBook" }, "QtyInternalUse",
                    new string[] { "PriceCost", "PostCurrentCostPrice", "CurrentCostPrice" })
            };

        /* Every AD-managed table records its author in CreatedBy, which points at
           AD_User. Probed like the line tables so a schema without the split-name
           columns still gets a name rather than a broken query. */
        private const string TABLE_USER = "AD_User";
        private const string COLUMN_CREATEDBY = "CreatedBy";
        private const string COLUMN_NAME = "Name";
        private const string COLUMN_FIRSTNAME = "FirstName";
        private const string COLUMN_LASTNAME = "LastName";

        /* Columns the dictionary probe reports on, beyond the table's own key. Each
           is optional: the generated statement adapts to what the table actually
           has rather than assuming a shape. */
        private const string COLUMN_DOCUMENTNO = "DocumentNo";
        private const string COLUMN_DATEACCT = "DateAcct";
        private const string COLUMN_MOVEMENTDATE = "MovementDate";

        /* The accounting date of a bank statement. C_BankStatement has no DateAcct on
           its header - only its lines do - so without this the table resolves to no
           usable date column and never reaches the card. */
        private const string COLUMN_STATEMENTDATE = "StatementDate";
        private const string COLUMN_DOCSTATUS = "DocStatus";
        private const string COLUMN_DOCTYPE = "C_DocType_ID";

        /* The ONE other table a screen filter may reach for, because the statement joins
           it: a document's type is what separates most windows over one table - Delivery
           Order from Material Receipt, Blanket Sales Order from Sales Order - and those
           tab filters name C_DocType directly. Joined under its own name so a clause
           reading "C_DocType.DocBaseType='MMS'" resolves exactly as it does when the
           framework opens that window. */
        private const string TABLE_DOCTYPE = "C_DocType";

        /* Words that can follow a table name in a FROM / JOIN without being its alias -
           the stop list <see cref="CollectClauseSources"/> reads a clause's own sources
           with. */
        private static readonly List<string> NotAnAlias = new List<string>
        {
            "WHERE", "ON", "AND", "OR", "NOT", "JOIN", "INNER", "LEFT", "RIGHT", "FULL",
            "OUTER", "CROSS", "GROUP", "ORDER", "HAVING", "UNION", "SELECT", "AS", "FROM",
            "IN", "EXISTS", "LIMIT", "FETCH", "OFFSET", "START", "CONNECT"
        };
        private const string COLUMN_CURRENCY = "C_Currency_ID";
        private const string COLUMN_CONVERSIONTYPE = "C_ConversionType_ID";

        // ─────────────────────────────────────────────────────────────────────
        // §1  Period list and bootstrap
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Bootstraps the widget in one round trip: every selectable open period of
        /// the primary calendar, the period to preselect, and that period's unposted
        /// transaction types.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <returns>Populated <see cref="UnPostedBootstrap"/> (never null).</returns>
        public UnPostedBootstrap GetBootstrap(Ctx ctx)
        {
            UnPostedBootstrap result = new UnPostedBootstrap();
            result.Periods = new List<PeriodItem>();
            result.Data = new PeriodData();

            if (ctx == null) { return result; }

            result.Periods = GetOpenPeriods(ctx);
            if (result.Periods.Count == 0) { return result; }

            PeriodItem selected = PickDefaultPeriod(result.Periods, DateTime.Now.Date);
            result.C_Period_ID = selected.C_Period_ID;
            result.PeriodName = selected.Name;
            result.Data = GetPeriodData(ctx, selected.C_Period_ID);

            return result;
        }

        /// <summary>
        /// The open periods of the tenant's PRIMARY calendar, newest first. A period
        /// qualifies when at least one of its active C_PeriodControl rows is Open; it
        /// appears once however many document base types are open for it.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <returns>Open periods, newest StartDate first (never null).</returns>
        public List<PeriodItem> GetOpenPeriods(Ctx ctx)
        {
            List<PeriodItem> items = new List<PeriodItem>();
            if (ctx == null) { return items; }

            /* AD_ClientInfo pins the calendar to the tenant rather than searching all
               calendars, and the open-control test is an EXISTS predicate, not a
               join, so several open base types cannot multiply the period out. */
            string sql = @"
                SELECT p.C_Period_ID AS C_Period_ID,
                       p.Name AS Period_Name,
                       p.StartDate AS Start_Date,
                       p.EndDate AS End_Date,
                       y.C_Year_ID AS C_Year_ID,
                       y.FiscalYear AS Fiscal_Year,
                       ci.C_Calendar_ID AS C_Calendar_ID
                FROM C_Period p
                INNER JOIN C_Year y ON (y.C_Year_ID=p.C_Year_ID)
                INNER JOIN AD_ClientInfo ci ON (ci.C_Calendar_ID=y.C_Calendar_ID AND ci.AD_Client_ID=@AD_Client_ID)
                WHERE p.IsActive='Y'
                  AND y.IsActive='Y'
                  AND EXISTS(SELECT 1 FROM C_PeriodControl pc WHERE pc.C_Period_ID=p.C_Period_ID AND pc.IsActive='Y' AND pc.PeriodStatus=@PeriodStatus)";

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "p", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            sql += " ORDER BY p.StartDate DESC,p.EndDate DESC,p.C_Period_ID DESC";

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
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
                items.Add(item);
            }

            return items;
        }

        /// <summary>
        /// Chooses which open period the widget opens on: the one containing today,
        /// otherwise the most recent one that has already started, otherwise the
        /// first of the list. The list is newest-first, so the first match wins.
        /// </summary>
        /// <param name="periods">Open periods, newest StartDate first.</param>
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
                if (started == null && from <= today) { started = item; }
            }

            return started != null ? started : periods[0];
        }

        /// <summary>
        /// Re-reads one period and confirms it is still active, accessible, open and
        /// on the tenant's primary calendar. The client only ever sends the id; the
        /// date range the queries run against always comes from here.
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
                INNER JOIN AD_ClientInfo ci ON (ci.C_Calendar_ID=y.C_Calendar_ID AND ci.AD_Client_ID=@AD_Client_ID)
                WHERE p.C_Period_ID=@C_Period_ID
                  AND p.IsActive='Y'
                  AND y.IsActive='Y'
                  AND EXISTS(SELECT 1 FROM C_PeriodControl pc WHERE pc.C_Period_ID=p.C_Period_ID AND pc.IsActive='Y' AND pc.PeriodStatus=@PeriodStatus)";

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "p", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
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
        // §2  Source discovery (Application Dictionary)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Every transaction table this widget may read, discovered from the
        /// Application Dictionary and already reduced to the ones it can actually
        /// use. Two flat queries rather than one nested statement: the column probe
        /// and the screen lookup are joined in memory, because a single query
        /// carrying both the per-column aggregation and the three-join screen EXISTS
        /// is exactly the shape the AddAccessSQL parser handles badly.
        ///
        /// A discovered table is dropped when it has no key column (nothing to zoom
        /// to) or no DateAcct column (nothing to bound by the period) - never when it
        /// merely has no amount strategy.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <returns>Usable sources, ordered by display name (never null).</returns>
        private List<SourceItem> DiscoverSources(Ctx ctx)
        {
            List<SourceItem> sources = new List<SourceItem>();
            if (ctx == null) { return sources; }

            Dictionary<int, SourceItem> byTable = ReadSourceColumns(ctx);
            if (byTable.Count == 0) { return sources; }

            ApplyLineStrategies(ctx, byTable);

            Dictionary<int, List<ScreenItem>> screensByTable = ReadSourceScreens(ctx);
            Dictionary<int, ScreenItem> primaryByTable = ReadPrimaryScreens(ctx);

            foreach (SourceItem item in byTable.Values)
            {
                /* No key: the modal could not offer a Zoom, and the row would be a
                   dead end. No accounting date at all - neither DateAcct nor the one
                   trusted alternative - and the period filter this whole widget is
                   about could not be applied, so the source is excluded rather than
                   bounded by a guessed column. */
                if (!IsSafeIdentifier(item.TableName)) { continue; }
                if (!IsSafeIdentifier(item.KeyColumn)) { continue; }
                if (!IsSafeIdentifier(item.DateColumn)) { continue; }

                ScreenItem primary;
                if (!primaryByTable.TryGetValue(item.AD_Table_ID, out primary)) { primary = null; }

                List<ScreenItem> screens;
                if (!screensByTable.TryGetValue(item.AD_Table_ID, out screens) || screens.Count == 0)
                {
                    /* No DISPLAYED header tab - the column is there, the posting is
                       real, but every window over the table hides its header tab, so
                       the screen query returned nothing.

                       Dropping the table here is what used to happen, and it cost the
                       card every record of that table while the VAS_195 checklist -
                       which asks only for a menu-reachable window over the TABLE -
                       listed all of them. A screen is how this card LABELS a row, not
                       what makes a document unposted, so a table with no screen of its
                       own falls back to its primary window and contributes one unsplit
                       row rather than nothing at all. */
                    if (primary == null) { continue; }

                    Log.Log(Level.INFO, "VAS_198: " + item.TableName + " has a Posted column but no"
                        + " displayed header tab; falling back to its primary window as a single unsplit row");

                    screens = new List<ScreenItem>();
                    screens.Add(primary);
                }

                /* One row per SCREEN, which is what the user recognises - Purchase
                   Order and Sales Order are two screens over one C_Order table. */
                List<SourceItem> perScreen = SplitByScreen(item, screens, primary);
                for (int i = 0; i < perScreen.Count; i++)
                {
                    perScreen[i].DisplayName = ResolveDisplayName(perScreen[i]);
                    sources.Add(perScreen[i]);
                }
            }

            sources.Sort(delegate (SourceItem a, SourceItem b)
            {
                int byName = string.Compare(a.DisplayName, b.DisplayName, StringComparison.CurrentCultureIgnoreCase);
                if (byName != 0) { return byName; }

                int byTableId = a.AD_Table_ID.CompareTo(b.AD_Table_ID);
                return byTableId != 0 ? byTableId : a.AD_Window_ID.CompareTo(b.AD_Window_ID);
            });

            DisambiguateDisplayNames(ctx, sources);

            return sources;
        }

        /// <summary>
        /// Probes the dictionary for every active, non-view physical table carrying
        /// an active Posted column, and reports which of the columns this widget can
        /// use each one actually has. One row per table - the MAX(CASE ...) form
        /// collapses the column rows rather than returning one row per column.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <returns>Discovered tables keyed by AD_Table_ID (never null).</returns>
        private Dictionary<int, SourceItem> ReadSourceColumns(Ctx ctx)
        {
            Dictionary<int, SourceItem> map = new Dictionary<int, SourceItem>();

            string sql = @"
                SELECT t.AD_Table_ID AS AD_Table_ID,
                       t.TableName AS Table_Name,
                       t.Name AS Table_Display_Name,
                       MAX(CASE WHEN c.IsKey='Y' THEN c.ColumnName END) AS Key_Column,
                       MAX(CASE WHEN c.ColumnName='" + COLUMN_DOCUMENTNO + @"' THEN c.ColumnName END) AS Document_No_Column,
                       MAX(CASE WHEN c.ColumnName='" + COLUMN_DATEACCT + @"' THEN c.ColumnName END) AS Date_Acct_Column,
                       MAX(CASE WHEN c.ColumnName='" + COLUMN_MOVEMENTDATE + @"' THEN c.ColumnName END) AS Movement_Date_Column,
                       MAX(CASE WHEN c.ColumnName='" + COLUMN_STATEMENTDATE + @"' THEN c.ColumnName END) AS Statement_Date_Column,
                       MAX(CASE WHEN c.ColumnName='" + COLUMN_DOCSTATUS + @"' THEN c.ColumnName END) AS Doc_Status_Column,
                       MAX(CASE WHEN c.ColumnName='" + COLUMN_DOCTYPE + @"' THEN c.ColumnName END) AS Doc_Type_Column,
                       MAX(CASE WHEN c.ColumnName='" + COLUMN_CURRENCY + @"' THEN c.ColumnName END) AS Currency_Column,
                       MAX(CASE WHEN c.ColumnName='" + COLUMN_CONVERSIONTYPE + @"' THEN c.ColumnName END) AS Conversion_Type_Column,
                       MAX(CASE WHEN c.ColumnName=@AmountColumnA THEN c.ColumnName END) AS Amount_Column_A,
                       MAX(CASE WHEN c.ColumnName=@AmountColumnB THEN c.ColumnName END) AS Amount_Column_B,
                       MAX(CASE WHEN c.ColumnName=@AmountColumnC THEN c.ColumnName END) AS Amount_Column_C
                FROM AD_Table t
                INNER JOIN AD_Column c ON (c.AD_Table_ID=t.AD_Table_ID AND c.IsActive='Y')
                WHERE t.IsActive='Y'
                  AND COALESCE(t.IsView,'N')='N'
                  AND EXISTS(SELECT 1 FROM AD_Column pc WHERE pc.AD_Table_ID=t.AD_Table_ID AND pc.ColumnName='" + COLUMN_POSTED + @"' AND pc.IsActive='Y')";

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "t", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            /* GROUP BY / ORDER BY go on AFTER the access SQL. */
            sql += " GROUP BY t.AD_Table_ID,t.TableName,t.Name";
            sql += " ORDER BY t.TableName";

            /* The three distinct amount columns the trusted map names, bound rather
               than inlined - they are the only value-bearing columns the probe looks
               for, and binding them keeps the map the single place they are listed. */
            string[] amountColumns = DistinctAmountColumns();

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@AmountColumnA", amountColumns[0]),
                new SqlParameter("@AmountColumnB", amountColumns[1]),
                new SqlParameter("@AmountColumnC", amountColumns[2])
            };

            DataSet ds = DB.ExecuteDataset(sql, parameters, null);
            if (ds == null || ds.Tables.Count == 0) { return map; }

            DataTable dt = ds.Tables[0];
            for (int i = 0; i < dt.Rows.Count; i++)
            {
                DataRow dr = dt.Rows[i];

                SourceItem item = new SourceItem();
                item.AD_Table_ID = Util.GetValueOfInt(dr["AD_Table_ID"]);
                item.TableName = Util.GetValueOfString(dr["Table_Name"]);
                item.TableDisplayName = Util.GetValueOfString(dr["Table_Display_Name"]);
                item.KeyColumn = Util.GetValueOfString(dr["Key_Column"]);
                item.DocumentNoColumn = Util.GetValueOfString(dr["Document_No_Column"]);
                item.DateAcctColumn = Util.GetValueOfString(dr["Date_Acct_Column"]);
                item.MovementDateColumn = Util.GetValueOfString(dr["Movement_Date_Column"]);
                item.StatementDateColumn = Util.GetValueOfString(dr["Statement_Date_Column"]);
                item.DateColumn = ResolveDateColumn(item);
                item.DocStatusColumn = Util.GetValueOfString(dr["Doc_Status_Column"]);
                item.DocTypeColumn = Util.GetValueOfString(dr["Doc_Type_Column"]);
                item.CurrencyColumn = Util.GetValueOfString(dr["Currency_Column"]);
                item.ConversionTypeColumn = Util.GetValueOfString(dr["Conversion_Type_Column"]);

                /* The amount strategy: the map decides WHICH column carries value,
                   the probe decides whether this installation actually has it. */
                item.AmountColumn = ResolveAmountColumn(item.TableName, dr);

                if (item.AD_Table_ID > 0) { map[item.AD_Table_ID] = item; }
            }

            return map;
        }

        /// <summary>
        /// The distinct column names the trusted amount map refers to, padded to the
        /// three bind slots the probe declares so the statement's shape never varies
        /// with the map's contents. An unused slot binds an empty string, which
        /// matches no column.
        /// </summary>
        /// <returns>Exactly three column names (empty strings for unused slots).</returns>
        private string[] DistinctAmountColumns()
        {
            List<string> distinct = new List<string>();

            foreach (KeyValuePair<string, string> entry in AmountColumnByTable)
            {
                if (!distinct.Contains(entry.Value)) { distinct.Add(entry.Value); }
            }

            string[] slots = new string[] { "", "", "" };
            for (int i = 0; i < slots.Length && i < distinct.Count; i++) { slots[i] = distinct[i]; }

            return slots;
        }

        /// <summary>
        /// Attaches the two SQL fragments that depend on OTHER tables' columns: the
        /// line-level value expression, where a <see cref="LineValueStrategies"/>
        /// entry names the table, and the author name every source shows. One
        /// dictionary read covers every table involved. A source that already has a
        /// header amount column keeps it - a table never carries both.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="byTable">Discovered tables, keyed by AD_Table_ID.</param>
        private void ApplyLineStrategies(Ctx ctx, Dictionary<int, SourceItem> byTable)
        {
            Dictionary<string, List<string>> lineColumns = ReadLineColumns(ctx);
            if (lineColumns.Count == 0) { return; }

            string createdByExpr = BuildCreatedByExpr(lineColumns);

            foreach (SourceItem item in byTable.Values)
            {
                item.CreatedByExpr = createdByExpr;

                if (!string.IsNullOrEmpty(item.AmountColumn)) { continue; }

                LineValueStrategy strategy = FindLineStrategy(item.TableName);
                if (strategy == null) { continue; }

                item.LineValueExpr = BuildLineValueExpr(item, strategy, lineColumns);
            }
        }

        /// <summary>
        /// How to read the name of whoever created a document, over the alias "usr".
        ///
        /// AD_User.Name is the display name and is what nearly every installation
        /// carries, but some store the name split across FirstName / LastName and
        /// leave Name empty - so the split form is used as a fallback, and only when
        /// the dictionary confirms both columns exist. The same expression serves
        /// every source, because CreatedBy is on every AD-managed table.
        /// </summary>
        /// <param name="columns">Confirmed columns per probed table.</param>
        /// <returns>Scalar expression over alias usr, or "" when AD_User is unusable.</returns>
        private string BuildCreatedByExpr(Dictionary<string, List<string>> columns)
        {
            List<string> userColumns;
            if (!columns.TryGetValue(TABLE_USER, out userColumns)) { return ""; }
            if (!HasColumn(userColumns, COLUMN_NAME)) { return ""; }

            string name = "usr." + COLUMN_NAME;

            if (HasColumn(userColumns, COLUMN_FIRSTNAME) && HasColumn(userColumns, COLUMN_LASTNAME))
            {
                /* NULLIF turns a stored empty name into a miss so the split form can
                   take over; || is the concatenation both back ends share. */
                name = "COALESCE(NULLIF(usr." + COLUMN_NAME + ",N''),"
                     + "NULLIF(TRIM(COALESCE(usr." + COLUMN_FIRSTNAME + ",N'') || ' ' || COALESCE(usr."
                     + COLUMN_LASTNAME + ",N'')),N''))";
            }

            return "COALESCE(" + name + ",N'')";
        }

        /// <summary>
        /// The active column names of every line table the strategies refer to.
        /// Read in one statement rather than one per strategy - there are only ever a
        /// handful of line tables, and they do not change between requests.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <returns>Line table name -> its active column names (never null).</returns>
        private Dictionary<string, List<string>> ReadLineColumns(Ctx ctx)
        {
            Dictionary<string, List<string>> map =
                new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            List<SqlParameter> parameters = new List<SqlParameter>();
            StringBuilder inList = new StringBuilder();

            List<string> wanted = new List<string>();
            for (int i = 0; i < LineValueStrategies.Count; i++)
            {
                wanted.Add(LineValueStrategies[i].LineTable);
            }

            /* AD_User rides along: the author name is built from whichever of its name
               columns this installation actually has, and one probe is cheaper than
               two. */
            wanted.Add(TABLE_USER);

            for (int i = 0; i < wanted.Count; i++)
            {
                string bind = "@ProbeTable" + i.ToString(CultureInfo.InvariantCulture);
                if (inList.Length > 0) { inList.Append(","); }
                inList.Append(bind);
                parameters.Add(new SqlParameter(bind, wanted[i]));
            }

            if (inList.Length == 0) { return map; }

            string sql = @"
                SELECT t.TableName AS Table_Name,
                       c.ColumnName AS Column_Name
                FROM AD_Table t
                INNER JOIN AD_Column c ON (c.AD_Table_ID=t.AD_Table_ID AND c.IsActive='Y')
                WHERE t.IsActive='Y'
                  AND t.TableName IN (" + inList.ToString() + ")";

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "t", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            DataSet ds = DB.ExecuteDataset(sql, parameters.ToArray(), null);
            if (ds == null || ds.Tables.Count == 0) { return map; }

            DataTable dt = ds.Tables[0];
            for (int i = 0; i < dt.Rows.Count; i++)
            {
                string table = Util.GetValueOfString(dt.Rows[i]["Table_Name"]);
                string column = Util.GetValueOfString(dt.Rows[i]["Column_Name"]);
                if (string.IsNullOrEmpty(table) || string.IsNullOrEmpty(column)) { continue; }

                if (!map.ContainsKey(table)) { map[table] = new List<string>(); }
                map[table].Add(column);
            }

            return map;
        }

        /// <summary>The strategy declared for one header table, or null.</summary>
        /// <param name="tableName">Discovered physical table name.</param>
        /// <returns>Matching strategy, or null when the table has none.</returns>
        private LineValueStrategy FindLineStrategy(string tableName)
        {
            if (string.IsNullOrEmpty(tableName)) { return null; }

            for (int i = 0; i < LineValueStrategies.Count; i++)
            {
                if (LineValueStrategies[i].TableName.Equals(tableName, StringComparison.OrdinalIgnoreCase))
                {
                    return LineValueStrategies[i];
                }
            }

            return null;
        }

        /// <summary>
        /// The correlated expression that values one document from its lines, or ""
        /// when the installation is missing a column the strategy needs.
        ///
        /// A scalar sub-select rather than a join and a GROUP BY: the SELECT list here
        /// is assembled from whatever columns the table turned out to have, and a
        /// GROUP BY would then have to repeat every one of those expressions exactly -
        /// which the two back ends disagree about often enough to be a real hazard.
        /// The sub-select keeps both the count query and the detail query flat.
        /// </summary>
        /// <param name="item">Discovered source (supplies the header alias' key).</param>
        /// <param name="strategy">Trusted strategy declared for its table.</param>
        /// <param name="lineColumns">Confirmed columns per line table.</param>
        /// <returns>Scalar expression over alias src, or "" when unusable.</returns>
        private string BuildLineValueExpr(SourceItem item, LineValueStrategy strategy,
            Dictionary<string, List<string>> lineColumns)
        {
            List<string> columns;
            if (!lineColumns.TryGetValue(strategy.LineTable, out columns)) { return ""; }
            if (!IsSafeIdentifier(strategy.LineTable)) { return ""; }

            /* The header's key is the join target and has not been validated yet at
               this point in discovery - the caller's own check comes later. */
            if (!IsSafeIdentifier(item.KeyColumn)) { return ""; }

            if (!HasColumn(columns, strategy.LinkColumn)) { return ""; }

            /* Every required quantity column must be present - a partial quantity
               would value the document wrongly rather than not at all. */
            for (int i = 0; i < strategy.QtyColumns.Length; i++)
            {
                if (!HasColumn(columns, strategy.QtyColumns[i])) { return ""; }
            }

            string cost = BuildCostExpr(strategy, columns);
            if (string.IsNullOrEmpty(cost)) { return ""; }

            string qty = strategy.QtyColumns.Length == 2
                ? "(COALESCE(ln." + strategy.QtyColumns[0] + ",0)-COALESCE(ln." + strategy.QtyColumns[1] + ",0))"
                : "COALESCE(ln." + strategy.QtyColumns[0] + ",0)";

            /* The optional quantity is a second document FORM sharing one line table
               (internal use beside a physical count), so it adds to the quantity
               rather than replacing it - the two are never both non-zero on one line. */
            if (!string.IsNullOrEmpty(strategy.OptionalQtyColumn)
                && HasColumn(columns, strategy.OptionalQtyColumn))
            {
                qty = "(" + qty + "+COALESCE(ln." + strategy.OptionalQtyColumn + ",0))";
            }

            /* ABS per LINE, not on the sum: a count that found two products short and
               one over is exposure on all three, not the net of them. */
            return "(SELECT COALESCE(SUM(ABS((" + cost + ")*" + qty + ")),0)"
                 + " FROM " + strategy.LineTable + " ln"
                 + " WHERE ln." + strategy.LinkColumn + "=" + SourceAlias(item) + "." + item.KeyColumn
                 + " AND ln.IsActive='Y')";
        }

        /// <summary>
        /// What one line values at, over the alias "ln".
        ///
        /// The strategy names its cost columns in OVERRIDE order - the first that
        /// carries a value wins - and only the ones this installation actually has
        /// take part. A stored zero counts as "not set" rather than "free", which is
        /// why every candidate but the last is wrapped in NULLIF: a receipt line with
        /// PostCurrentCostPrice 0 falls through to CurrentCostPrice, exactly as the
        /// posting process reads it. The last candidate is taken as it stands, since
        /// there is nothing left to fall through to.
        ///
        /// Returns "" when the line table has none of them - the source then reports
        /// its count with no value rather than a figure nobody can vouch for.
        /// </summary>
        /// <param name="strategy">Trusted strategy declared for the table.</param>
        /// <param name="columns">Confirmed columns of its line table.</param>
        /// <returns>Scalar expression over alias ln, or "" when unusable.</returns>
        private string BuildCostExpr(LineValueStrategy strategy, List<string> columns)
        {
            List<string> present = new List<string>();
            for (int i = 0; i < strategy.CostColumns.Length; i++)
            {
                if (HasColumn(columns, strategy.CostColumns[i])) { present.Add(strategy.CostColumns[i]); }
            }

            if (present.Count == 0) { return ""; }
            if (present.Count == 1) { return "COALESCE(ln." + present[0] + ",0)"; }

            StringBuilder sb = new StringBuilder();
            sb.Append("COALESCE(");

            for (int i = 0; i < present.Count; i++)
            {
                if (i > 0) { sb.Append(","); }

                /* All but the last are candidates to be skipped when zero. */
                if (i < present.Count - 1) { sb.Append("NULLIF(ln.").Append(present[i]).Append(",0)"); }
                else { sb.Append("ln.").Append(present[i]); }
            }

            sb.Append(",0)");
            return sb.ToString();
        }

        /// <summary>
        /// A stored CODE, in the character type the NAME columns beside it are held in.
        ///
        /// On ORACLE this cast is not optional. A dictionary NAME or symbol is NVARCHAR2,
        /// in the national character set; a code column - ISO_Code, DocBaseType,
        /// DocStatus - is a plain CHAR / VARCHAR2 in the database character set. COALESCE
        /// and CASE both require their arms to be convertible to the first one's type, and
        /// Oracle refuses to mix the two character sets: it raises ORA-12704 "character
        /// set mismatch" and the whole statement fails, so the widget shows a load error
        /// rather than a coarser label.
        ///
        /// PostgreSQL has one text type and needs no cast, and NVARCHAR2 is not a type it
        /// knows, so the cast is emitted for Oracle only. 100 characters is comfortably
        /// above anything a code column stores; the length only has to avoid truncating.
        /// Chronological development:
        ///   VAI154      2026-08-26 Created - the same mismatch confirmed on VAS_196's
        ///                          COALESCE(dbt.Name,pc.DocBaseType)
        /// </summary>
        /// <param name="codeExpr">Qualified column holding the stored code.</param>
        /// <returns>The expression, cast where the backend requires it.</returns>
        private string CodeAsText(string codeExpr)
        {
            if (string.IsNullOrEmpty(codeExpr)) { return "N''"; }

            return DB.IsOracle() ? "CAST(" + codeExpr + " AS NVARCHAR2(100))" : codeExpr;
        }

        /// <summary>Case-insensitive membership test over a confirmed column list.</summary>
        /// <param name="columns">Column names the dictionary reported.</param>
        /// <param name="name">Column the strategy needs.</param>
        /// <returns>true when the table carries it.</returns>
        private bool HasColumn(List<string> columns, string name)
        {
            if (columns == null || string.IsNullOrEmpty(name)) { return false; }

            for (int i = 0; i < columns.Count; i++)
            {
                if (name.Equals(columns[i], StringComparison.OrdinalIgnoreCase)) { return true; }
            }

            return false;
        }

        /// <summary>
        /// The column the period bounds are applied to: DateAcct where the table has
        /// one, else MovementDate, else StatementDate.
        ///
        /// This is a SHORT, EXPLICIT fallback list, not a scan for anything that looks
        /// like a date. §19 rules out guessing between DateTrx / DateOrdered /
        /// DateInvoiced precisely because those three mean different things - but it
        /// permits a trusted mapping, and the other two ARE the accounting date of
        /// their own documents, which is how the platform itself treats them:
        ///
        ///   MovementDate    the inventory documents (movements, physical inventories)
        ///                   have no DateAcct at all - for those the movement IS the
        ///                   accounting event.
        ///   StatementDate   C_BankStatement likewise carries no DateAcct on its header,
        ///                   only on its lines, and MBankStatement tests the period with
        ///                   MPeriod.IsOpen(ctx, GetStatementDate(), ...). Without this
        ///                   third fallback the table resolved to no date column and was
        ///                   dropped from discovery in silence.
        ///
        /// Nothing else is accepted; a table with none of the three is still excluded.
        /// Chronological development:
        ///   VAI154      2026-08-24 StatementDate admitted (C_BankStatement)
        /// </summary>
        /// <param name="item">Discovered source carrying the probe results.</param>
        /// <returns>The date column to bound by, or "" when the table has none.</returns>
        private string ResolveDateColumn(SourceItem item)
        {
            if (IsSafeIdentifier(item.DateAcctColumn)) { return item.DateAcctColumn; }
            if (IsSafeIdentifier(item.MovementDateColumn)) { return item.MovementDateColumn; }
            if (IsSafeIdentifier(item.StatementDateColumn)) { return item.StatementDateColumn; }
            return "";
        }

        /// <summary>
        /// The value column of one discovered table: the trusted map's choice, only
        /// when the dictionary probe confirmed the table really carries it.
        /// </summary>
        /// <param name="tableName">Discovered physical table name.</param>
        /// <param name="row">The probe row for that table.</param>
        /// <returns>Confirmed amount column, or "" when the table has no strategy.</returns>
        private string ResolveAmountColumn(string tableName, DataRow row)
        {
            string wanted;
            if (string.IsNullOrEmpty(tableName)
                || !AmountColumnByTable.TryGetValue(tableName, out wanted)) { return ""; }

            string[] found = new string[]
            {
                Util.GetValueOfString(row["Amount_Column_A"]),
                Util.GetValueOfString(row["Amount_Column_B"]),
                Util.GetValueOfString(row["Amount_Column_C"])
            };

            for (int i = 0; i < found.Length; i++)
            {
                if (wanted.Equals(found[i], StringComparison.OrdinalIgnoreCase)) { return found[i]; }
            }

            return "";
        }

        /// <summary>
        /// Every screen each discovered table can be OPENED on - all of them, not one:
        /// the SCREEN is what a card row stands for, so a table reachable through four
        /// windows is four candidate rows.
        ///
        /// A screen is a menu-reachable, active window whose HEADER tab (TabLevel 0) is
        /// over the table - the window that opens the record itself rather than showing
        /// it as somebody else's child tab. That is the same test <see cref="ReadPrimaryScreens"/>
        /// and VAS_195 apply, and it is deliberately NOT "a tab that declares a field
        /// over Posted".
        ///
        /// Requiring the Posted field used to decide the screen set, and it silently
        /// under-split every table whose windows do not all put Posted on their header
        /// tab. M_Inventory is the case that shows it: Inventory Count and Internal Use
        /// Inventory are two windows separated by IsInternalUse, and if only one of them
        /// declares the field, the other's documents were reported under - and zoomed
        /// into - a window whose own filter excludes them. Which columns a window chooses
        /// to DISPLAY has nothing to do with which records it holds; the tab's
        /// WhereClause has, so that is what the screen set is built from.
        ///
        /// The tab's own WhereClause comes back with each: it is what makes two windows
        /// over one table different lists rather than the same list twice.
        /// Chronological development:
        ///   VAI154      2026-08-26 Screen set widened from Posted-bearing tabs to every
        ///                          menu-reachable header window over the table
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <returns>Screens grouped by AD_Table_ID, each list in a stable order.</returns>
        private Dictionary<int, List<ScreenItem>> ReadSourceScreens(Ctx ctx)
        {
            Dictionary<int, List<ScreenItem>> byTable = new Dictionary<int, List<ScreenItem>>();

            /* Screen name: the session language's translation, then the window's
               DisplayName, then its Name. AD_Window.Name is the last resort but it must
               be there - DisplayName is optional and often null, and without this
               fallback such a window resolves to an empty name, falls back to its tab's
               (generic) name and sorts ahead of its siblings. The tab name is
               translated the same way - it is what disambiguates two sources on one
               window. */
            string sql = @"
                SELECT DISTINCT t.AD_Table_ID AS AD_Table_ID,
                       tab.AD_Tab_ID AS AD_Tab_ID,
                       COALESCE(tabtrl.Name,tab.Name,N'') AS Tab_Name,
                       tab.SeqNo AS Tab_Seq_No,
                       COALESCE(tab.WhereClause,N'') AS Tab_Where_Clause,
                       w.AD_Window_ID AS AD_Window_ID,
                       COALESCE(wtrl.Name,w.DisplayName,w.Name,N'') AS Window_Name
                FROM AD_Table t
                INNER JOIN AD_Tab tab ON (tab.AD_Table_ID=t.AD_Table_ID)
                INNER JOIN AD_Window w ON (w.AD_Window_ID=tab.AD_Window_ID)
                INNER JOIN AD_Menu m ON (m.AD_Window_ID=w.AD_Window_ID)
                LEFT OUTER JOIN AD_Window_Trl wtrl ON (wtrl.AD_Window_ID=w.AD_Window_ID AND wtrl.AD_Language=@AD_Language AND wtrl.IsActive='Y')
                LEFT OUTER JOIN AD_Tab_Trl tabtrl ON (tabtrl.AD_Tab_ID=tab.AD_Tab_ID AND tabtrl.AD_Language=@AD_Language AND tabtrl.IsActive='Y')
                WHERE t.IsActive='Y'
                  AND m.IsActive = 'Y'
                  AND COALESCE(t.IsView,'N')='N'
                  AND EXISTS(SELECT 1 FROM AD_Column pc WHERE pc.AD_Table_ID=t.AD_Table_ID AND pc.ColumnName='" + COLUMN_POSTED + @"' AND pc.IsActive='Y')
                  AND tab.IsActive='Y'
                  AND tab.IsDisplayed='Y'
                  /* Header tabs only. COALESCE because TabLevel is NULLable, and a bare
                     comparison would be NULL - never true - on every row that leaves it
                     unset, which is most of them. */
                  AND COALESCE(tab.TabLevel,0)=0
                  AND w.IsActive='Y'";

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "t", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            /* A total order, so the same installation always produces the same rows in
               the same sequence - the first tab of a window is the one whose clause
               represents that window.

               Ordered by the dictionary's own sequence, NOT by window name. The first
               screen of a table is the one a reader would call its main window, and the
               catch-all row is hung on it (see PickComplementCarrier); ordering by name
               made that an alphabetical accident instead - which is how every plain
               sales order came to be reported under, and zoomed into, "Blanket Sales
               Order" merely because B sorts before S. */
            sql += " ORDER BY t.AD_Table_ID,tab.SeqNo,w.AD_Window_ID,tab.AD_Tab_ID";

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@AD_Language", ctx.GetAD_Language())
            };

            DataSet ds = DB.ExecuteDataset(sql, parameters, null);
            if (ds == null || ds.Tables.Count == 0) { return byTable; }

            DataTable dt = ds.Tables[0];
            for (int i = 0; i < dt.Rows.Count; i++)
            {
                DataRow dr = dt.Rows[i];

                int tableId = Util.GetValueOfInt(dr["AD_Table_ID"]);
                if (tableId <= 0) { continue; }

                ScreenItem screen = new ScreenItem();
                screen.AD_Window_ID = Util.GetValueOfInt(dr["AD_Window_ID"]);
                screen.AD_Tab_ID = Util.GetValueOfInt(dr["AD_Tab_ID"]);
                screen.WindowName = Util.GetValueOfString(dr["Window_Name"]);
                screen.TabName = Util.GetValueOfString(dr["Tab_Name"]);
                screen.WhereClause = ResolveWhereClause(ctx, Util.GetValueOfString(dr["Tab_Where_Clause"]));

                if (screen.AD_Window_ID <= 0) { continue; }

                if (!byTable.ContainsKey(tableId)) { byTable[tableId] = new List<ScreenItem>(); }

                /* One window can display the Posted field on more than one tab; the
                   first (lowest SeqNo, by the ORDER BY above) speaks for the window. */
                List<ScreenItem> screens = byTable[tableId];
                bool seen = false;
                for (int s = 0; s < screens.Count; s++)
                {
                    if (screens[s].AD_Window_ID == screen.AD_Window_ID) { seen = true; break; }
                }
                if (!seen) { screens.Add(screen); }
            }

            return byTable;
        }

        /// <summary>
        /// The ONE window a reader would open each Posted-carrying table on, regardless
        /// of whether any tab declares a field over its Posted column.
        ///
        /// This is the safety net under the whole screen mechanism, and it serves two
        /// rows that would otherwise not exist:
        ///
        ///   the fallback row  a table whose Posted column has no AD_Field on a
        ///                     displayed tab returns no screen at all from
        ///                     <see cref="ReadSourceScreens"/>, and used to be dropped
        ///                     from the card in silence - every one of its unposted
        ///                     documents with it.
        ///   the complement    a table whose screens ALL carry a filter has no window
        ///                     to hang "everything the other screens did not claim" on,
        ///                     and the records matching none of those filters appeared
        ///                     nowhere. They hang on this window instead.
        ///
        /// The primary window is the one whose HEADER tab (TabLevel 0) is over the
        /// table - the window that opens the record itself rather than showing it as
        /// somebody else's child tab - lowest tab sequence first, then lowest window id
        /// so the same installation always resolves the same screen. Menu-reachable and
        /// active, exactly as VAS_195 resolves the same thing.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <returns>One primary screen per table, keyed by AD_Table_ID (never null).</returns>
        private Dictionary<int, ScreenItem> ReadPrimaryScreens(Ctx ctx)
        {
            Dictionary<int, ScreenItem> byTable = new Dictionary<int, ScreenItem>();

            /* No WhereClause is read: this screen stands for the whole table by
               definition, and giving it a filter would defeat the point of having it. */
            string sql = @"
                SELECT t.AD_Table_ID AS AD_Table_ID,
                       tab.AD_Tab_ID AS AD_Tab_ID,
                       tab.SeqNo AS Tab_Seq_No,
                       COALESCE(tabtrl.Name,tab.Name,N'') AS Tab_Name,
                       w.AD_Window_ID AS AD_Window_ID,
                       COALESCE(wtrl.Name,w.DisplayName,w.Name,N'') AS Window_Name
                FROM AD_Table t
                INNER JOIN AD_Tab tab ON (tab.AD_Table_ID=t.AD_Table_ID)
                INNER JOIN AD_Window w ON (w.AD_Window_ID=tab.AD_Window_ID)
                INNER JOIN AD_Menu m ON (m.AD_Window_ID=w.AD_Window_ID)
                LEFT OUTER JOIN AD_Window_Trl wtrl ON (wtrl.AD_Window_ID=w.AD_Window_ID AND wtrl.AD_Language=@AD_Language AND wtrl.IsActive='Y')
                LEFT OUTER JOIN AD_Tab_Trl tabtrl ON (tabtrl.AD_Tab_ID=tab.AD_Tab_ID AND tabtrl.AD_Language=@AD_Language AND tabtrl.IsActive='Y')
                WHERE t.IsActive='Y'
                  AND COALESCE(t.IsView,'N')='N'
                  AND EXISTS(SELECT 1 FROM AD_Column pc WHERE pc.AD_Table_ID=t.AD_Table_ID AND pc.ColumnName='" + COLUMN_POSTED + @"' AND pc.IsActive='Y')
                  AND tab.IsActive='Y'
                  /* Header tabs only. COALESCE because TabLevel is NULLable, and a bare
                     comparison would be NULL - never true - on every row that leaves it
                     unset, which is most of them. */
                  AND COALESCE(tab.TabLevel,0)=0
                  AND w.IsActive='Y'
                  AND m.IsActive='Y'";

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "t", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            /* A total order, so the first row of each table is a stable choice. */
            sql += " ORDER BY t.AD_Table_ID,tab.SeqNo,w.AD_Window_ID,tab.AD_Tab_ID";

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@AD_Language", ctx.GetAD_Language())
            };

            DataSet ds = DB.ExecuteDataset(sql, parameters, null);
            if (ds == null || ds.Tables.Count == 0) { return byTable; }

            DataTable dt = ds.Tables[0];
            for (int i = 0; i < dt.Rows.Count; i++)
            {
                DataRow dr = dt.Rows[i];

                int tableId = Util.GetValueOfInt(dr["AD_Table_ID"]);
                int windowId = Util.GetValueOfInt(dr["AD_Window_ID"]);
                if (tableId <= 0 || windowId <= 0) { continue; }

                /* First row wins - the ORDER BY above already decided which. */
                if (byTable.ContainsKey(tableId)) { continue; }

                ScreenItem screen = new ScreenItem();
                screen.AD_Window_ID = windowId;
                screen.AD_Tab_ID = Util.GetValueOfInt(dr["AD_Tab_ID"]);
                screen.WindowName = Util.GetValueOfString(dr["Window_Name"]);
                screen.TabName = Util.GetValueOfString(dr["Tab_Name"]);
                screen.WhereClause = "";

                byTable[tableId] = screen;
            }

            return byTable;
        }

        /// <summary>
        /// Turns one discovered table into the card rows it deserves: one per SCREEN it
        /// can be opened on.
        ///
        /// Two windows over one table are only different rows if their records can be
        /// told apart, and what tells them apart is the tab's own WhereClause - the
        /// very predicate the framework applies when it opens that window. Where the
        /// clause is usable, each window becomes its own row: Purchase Order beside
        /// Sales Order, GRN beside Delivery Order, all over one physical table.
        ///
        /// Where it is not - a clause carrying @context@ variables this widget cannot
        /// resolve, a clause reaching for a table this statement does not join, or no
        /// clause at all - the windows cannot be separated, and the table collapses back
        /// to ONE row covering all of its records. That is the deliberate choice: one
        /// honest row beats several rows each listing the same documents.
        ///
        /// Whatever the split, it is EXHAUSTIVE and DISJOINT - every record of the table
        /// lands in exactly one row of the card. Exhaustive because the filtered screens
        /// are always accompanied by a row carrying their complement (see
        /// <see cref="ComplementClause"/>); without that a document simply disappears
        /// from the card while remaining unposted, which is the one failure this widget
        /// cannot have. Disjoint because screens whose filters overlap - a Blanket Sales
        /// Order satisfies the Sales Order filter too - are taken narrowest first, each
        /// claiming only what no narrower sibling already has (see
        /// <see cref="ExclusiveClause"/>); without that the same document is counted, and
        /// valued, under two screens and opens on whichever the reader happened to click.
        /// </summary>
        /// <param name="item">Discovered table.</param>
        /// <param name="screens">Every screen the table can be opened on.</param>
        /// <param name="primary">The table's primary window - preferred to carry the
        /// catch-all row; may be null.</param>
        /// <returns>One or more sources (never null; empty when there is no screen).</returns>
        private List<SourceItem> SplitByScreen(SourceItem item, List<ScreenItem> screens, ScreenItem primary)
        {
            List<SourceItem> result = new List<SourceItem>();
            if (screens == null || screens.Count == 0) { return result; }

            List<ScreenItem> separable = new List<ScreenItem>();
            List<ScreenItem> unfiltered = new List<ScreenItem>();

            for (int i = 0; i < screens.Count; i++)
            {
                string clause = screens[i].WhereClause;

                if (clause != null && clause.Trim().Length > 0
                    && IsUsableWhereClause(clause) && ReferencesResolvableTables(clause, item))
                {
                    separable.Add(screens[i]);
                }
                else
                {
                    unfiltered.Add(screens[i]);
                }
            }

            /* Nothing separable: one row covering every record of the table, hung on the
               table's main window rather than on whichever screen happened to come first. */
            if (separable.Count == 0)
            {
                SourceItem single = CloneForScreen(item, PickCatchAllScreen(unfiltered, primary, screens));
                single.WhereClause = "";
                result.Add(single);
                return result;
            }

            /* Narrowest screen first, so an overlapping pair is claimed by the screen
               that describes it best - see CompareSpecificity. */
            separable.Sort(CompareSpecificity);

            /* Each screen claims what its own filter matches AND what no NARROWER screen
               has already claimed, which is what makes the rows DISJOINT.

               Without that, overlapping screens double count: Blanket Sales Order is a
               sales order whose document type is a blanket, so it satisfies the Sales
               Order tab filter too, and a blanket order was reported - and valued - under
               both rows. Ordering by specificity and subtracting the earlier claims gives
               it to Blanket Sales Order alone, which is also the window it opens in. */
            List<ScreenItem> claimed = new List<ScreenItem>();

            for (int i = 0; i < separable.Count; i++)
            {
                SourceItem row = CloneForScreen(item, separable[i]);

                if (claimed.Count > 0)
                {
                    string exclusive = ExclusiveClause(item, separable[i], claimed);

                    /* A composition too long to be pasted in leaves this row overlapping
                       its narrower siblings. Double counting is not a wrong screen, so
                       the row still stands on its own filter - the miss is logged rather
                       than costing the table its split. */
                    if (exclusive.Length > 0 && exclusive.Length <= WHERECLAUSE_COMPOSED_MAX)
                    {
                        row.WhereClause = exclusive;
                        row.IsComposedWhereClause = true;
                    }
                    else
                    {
                        Log.Log(Level.WARNING, "VAS_198: " + item.TableName + " screen '"
                            + separable[i].WindowName + "' could not be made exclusive of its"
                            + " narrower siblings; a record matching both is counted twice");
                    }
                }

                claimed.Add(separable[i]);
                result.Add(row);
            }

            /* Whatever the filtered screens did not claim still has to appear somewhere,
               and this is the row that carries it.

               Dropping it - which is what happened before - loses documents outright:
               C_Order's Sales Order tab carries no WhereClause while Purchase Order,
               Quotation and Blanket Order all carry one, so an ordinary sales order
               matched none of the surviving rows and appeared nowhere on the card. Its
               row is therefore the COMPLEMENT of its filtered siblings: everything the
               other screens did not claim. That keeps the split exhaustive without
               double counting, which a plain unfiltered row would not. */
            ScreenItem carrier = PickCatchAllScreen(unfiltered, primary, screens);
            string complement = ComplementClause(item, separable);

            /* The complement could not be composed at all, or is too long to paste in.
               The split is then no longer known to be exhaustive, and a split that might
               lose a record is worse than no split at all - so the table collapses to one
               honest row over all of it. Coarser than intended, never short of documents. */
            if (complement.Length == 0 || complement.Length > WHERECLAUSE_COMPOSED_MAX)
            {
                Log.Log(Level.WARNING, "VAS_198: " + item.TableName + " has " + separable.Count
                    + " filtered screens whose complement cannot be expressed (composed filter"
                    + " too long); reporting the whole table as one row");

                SourceItem whole = CloneForScreen(item, carrier);
                whole.WhereClause = "";

                result.Clear();
                result.Add(whole);
                return result;
            }

            SourceItem rest = CloneForScreen(item, carrier);
            rest.WhereClause = complement;

            /* Server-composed, not dictionary text: it is longer than a tab filter is
               allowed to be and it qualifies its own inner alias, so the raw-clause
               guards must not be applied to it a second time. */
            rest.IsComposedWhereClause = true;

            /* The other half of this row's identity. The carrier may well BE one of the
               filtered screens - when every window of the table carries a filter there is
               no unfiltered one to hang the leftovers on - and two rows sharing a window
               id used to be indistinguishable to a drill-down request, so the whole split
               was thrown away instead. It is the flag, not the window, that tells them
               apart now; the split survives, and each filtered screen keeps its own name
               and its own zoom target. */
            rest.IsComplement = true;
            result.Add(rest);

            if (unfiltered.Count > 1)
            {
                Log.Log(Level.WARNING, "VAS_198: " + item.TableName + " has "
                    + unfiltered.Count + " screens with no usable filter; '"
                    + carrier.WindowName + "' represents them all as the catch-all row");
            }

            return result;
        }

        /// <summary>
        /// Which screen carries the records no filtered screen claims - the table's main
        /// window, as a reader would name it.
        ///
        /// The table's PRIMARY window is that window by definition, so it is preferred
        /// whenever it holds no filter of its own. Failing that, the first screen with no
        /// filter - the screen list is in the dictionary's own tab / window sequence, so
        /// "first" is the lowest-sequenced window over the table and not an alphabetical
        /// accident. Failing even that, the primary window itself: it then shares its
        /// window with a filtered screen, which the complement flag tells apart.
        /// </summary>
        /// <param name="unfiltered">Screens carrying no usable filter, in dictionary order.</param>
        /// <param name="primary">The table's primary window; may be null.</param>
        /// <param name="screens">Every screen of the table, in dictionary order.</param>
        /// <returns>The screen to hang the catch-all row on (never null).</returns>
        private ScreenItem PickCatchAllScreen(List<ScreenItem> unfiltered, ScreenItem primary,
            List<ScreenItem> screens)
        {
            if (primary != null && unfiltered != null)
            {
                for (int i = 0; i < unfiltered.Count; i++)
                {
                    if (unfiltered[i].AD_Window_ID == primary.AD_Window_ID) { return unfiltered[i]; }
                }
            }

            if (unfiltered != null && unfiltered.Count > 0) { return unfiltered[0]; }
            if (primary != null) { return primary; }

            return screens[0];
        }

        /// <summary>
        /// Orders screens NARROWEST FIRST, so that where two screen filters overlap the
        /// record is claimed by the one that describes it most closely.
        ///
        /// Specificity is taken to be the length of the resolved filter, which is a
        /// heuristic and is meant as one: SQL gives no way to ask whether one predicate
        /// implies another, and the dictionary declares no precedence between two windows
        /// over one table. It holds for the shape that actually occurs - a specialised
        /// window repeats its general sibling's predicate and adds to it, as Blanket Sales
        /// Order does to Sales Order - and where it does not hold, the split is still a
        /// partition, just one that assigns a shared record to the other screen.
        /// The window id breaks ties so the same installation always splits the same way.
        /// </summary>
        /// <param name="a">One screen.</param>
        /// <param name="b">The screen to compare it with.</param>
        /// <returns>Negative when a is the narrower screen.</returns>
        private int CompareSpecificity(ScreenItem a, ScreenItem b)
        {
            int byLength = b.WhereClause.Length.CompareTo(a.WhereClause.Length);
            if (byLength != 0) { return byLength; }

            return a.AD_Window_ID.CompareTo(b.AD_Window_ID);
        }

        /// <summary>
        /// One screen's own filter, minus everything a narrower screen has already
        /// claimed - the predicate that makes the card's rows disjoint.
        ///
        /// The subtraction is a NOT EXISTS keyed on the table's own primary key for the
        /// same three-valued-logic reason <see cref="ComplementClause"/> is: a plain
        /// NOT (sibling) is NULL - and therefore not true - wherever the sibling tests a
        /// NULLable column, which would drop the record from this row as well as from the
        /// sibling's. EXISTS is two-valued, so a record the sibling does not genuinely
        /// claim stays here.
        /// </summary>
        /// <param name="item">Discovered table, supplying the table and key names.</param>
        /// <param name="screen">The screen this row stands for.</param>
        /// <param name="claimed">Narrower screens already holding a row.</param>
        /// <returns>Predicate fragment, or "" when it cannot be composed.</returns>
        private string ExclusiveClause(SourceItem item, ScreenItem screen, List<ScreenItem> claimed)
        {
            string complement = ComplementClause(item, claimed);
            if (complement.Length == 0) { return ""; }

            return "(" + screen.WhereClause + ") AND " + complement;
        }

        /// <summary>
        /// The complement of a set of screen filters: every record none of them claims.
        ///
        /// Written as a NOT EXISTS keyed on the table's own primary key rather than as
        /// "NOT (a) AND NOT (b)", and that is the whole point of the method.
        ///
        /// SQL predicates are three-valued. If a sibling clause tests a column that is
        /// NULL on some row, that clause is NULL there - so the row fails the sibling's
        /// own branch (NULL is not true) AND fails NOT(clause) in the complement (NOT
        /// NULL is still NULL). It lands in no row of the card at all and quietly stops
        /// being reported, which on an unposted-entries card is the worst possible way
        /// to be wrong. The earlier code documented this as a caveat it could not fix
        /// portably, because the obvious repair is a boolean COALESCE that Oracle has no
        /// form of.
        ///
        /// EXISTS is two-valued, which is the way out. The correlated sub-select returns
        /// a row only when the outer record genuinely satisfies one of the clauses; when
        /// a clause evaluates to NULL it returns nothing, so NOT EXISTS is TRUE and the
        /// record falls into the complement - exactly where an unclaimed record belongs.
        /// The key equality makes it at most a single primary-key probe per row.
        ///
        /// The clauses inside deliberately still reference the OUTER alias: a tab
        /// WhereClause qualifies its columns with the table name, the outer alias IS the
        /// table name (see <see cref="SourceAlias"/>), and the inner copy is aliased to
        /// something else precisely so those references keep resolving outward.
        /// </summary>
        /// <param name="item">Discovered table, supplying the table and key names.</param>
        /// <param name="screens">Screens carrying resolved, usable clauses.</param>
        /// <returns>Predicate fragment, or "" when there is nothing to complement.</returns>
        private string ComplementClause(SourceItem item, List<ScreenItem> screens)
        {
            if (screens == null || screens.Count == 0) { return ""; }

            StringBuilder claimed = new StringBuilder();

            for (int i = 0; i < screens.Count; i++)
            {
                if (claimed.Length > 0) { claimed.Append(" OR "); }
                claimed.Append("(").Append(screens[i].WhereClause).Append(")");
            }

            /* Both names came from the dictionary and were validated by the caller, but
               they are being concatenated into SQL, so they are checked once more here -
               the same belt-and-braces rule every other generated identifier follows. */
            if (!IsSafeIdentifier(item.TableName) || !IsSafeIdentifier(item.KeyColumn)) { return ""; }

            return "NOT EXISTS(SELECT 1 FROM " + item.TableName + " VAS198Claimed"
                 + " WHERE VAS198Claimed." + item.KeyColumn + "=" + SourceAlias(item) + "." + item.KeyColumn
                 + " AND (" + claimed.ToString() + "))";
        }

        /// <summary>
        /// Whether every table a resolved WhereClause qualifies a column with is one
        /// THIS statement can resolve.
        ///
        /// A tab filter is executed by the framework against a statement that joins
        /// whatever that window joins, so a perfectly valid clause can say
        /// "C_DocType.DocBaseType='MMS'". Pasting a clause naming a table the statement
        /// does not have does not return the wrong rows, it throws; ReadSourceTotal then
        /// logs the failure and returns null, and GetPeriodData skips that source
        /// entirely. The result is a screen's worth of unposted documents missing from
        /// the card with nothing on screen to say so, while VAS_195 - which applies no
        /// tab filter - reports every one of them.
        ///
        /// Three kinds of qualifier resolve, and the third is the one that matters:
        ///
        ///   the source table   aliased to its own name, which is why
        ///                      <see cref="SourceAlias"/> does not shorten it.
        ///   the clause's own   a subquery that brings its own FROM resolves whatever it
        ///                      declares, table name or alias alike - "EXISTS(SELECT 1
        ///                      FROM C_DocType dt WHERE dt.DocBaseType=...)" is
        ///                      self-contained and was being refused for the alias it
        ///                      declares itself.
        ///   C_DocType          JOINED by every query that needs it (see
        ///                      <see cref="NeedsDocTypeJoin"/>). This is what actually
        ///                      separates most windows over one table: Delivery Order
        ///                      from Material Receipt, Blanket Sales Order from Sales
        ///                      Order - the document TYPE, not a flag on the document.
        ///                      Refusing it left those windows inseparable, which is why
        ///                      they collapsed into one another's rows. A source whose
        ///                      table carries no C_DocType_ID column has nothing to join
        ///                      on, so for it the clause is still refused.
        ///
        /// Anything else is refused, and the screen degrades to an unfiltered one. That
        /// is a coarser split, never a lost record: an unfiltered screen either becomes
        /// the catch-all row or collapses the table to a single row, both of which still
        /// cover everything.
        ///
        /// The scan is deliberately literal-aware - a quoted string may contain a dot and
        /// means nothing here - and ignores a dot that is part of a number.
        /// Chronological development:
        ///   VAI154      2026-08-26 C_DocType and the clause's own subquery aliases
        ///                          admitted; was source table only
        /// </summary>
        /// <param name="clause">Resolved WhereClause.</param>
        /// <param name="item">The source the clause would filter - supplies the table
        /// name and whether a C_DocType join is possible at all.</param>
        /// <returns>true when every qualified reference resolves.</returns>
        private bool ReferencesResolvableTables(string clause, SourceItem item)
        {
            if (string.IsNullOrEmpty(clause)) { return false; }
            if (item == null || string.IsNullOrEmpty(item.TableName)) { return false; }

            List<string> declared = CollectClauseSources(clause);
            bool canJoinDocType = IsSafeIdentifier(item.DocTypeColumn);

            bool inLiteral = false;

            for (int i = 0; i < clause.Length; i++)
            {
                char ch = clause[i];

                if (ch == '\'') { inLiteral = !inLiteral; continue; }
                if (inLiteral || ch != '.') { continue; }

                /* A dot only qualifies something when a column name follows it;
                   "1.5" and a trailing dot qualify nothing. */
                if (i + 1 >= clause.Length) { continue; }
                char next = clause[i + 1];
                if (!((next >= 'A' && next <= 'Z') || (next >= 'a' && next <= 'z') || next == '_')) { continue; }

                int end = i;
                int start = i;
                while (start > 0 && IsIdentifierChar(clause[start - 1])) { start--; }

                if (start == end) { continue; }

                string qualifier = clause.Substring(start, end - start);

                /* A numeric prefix is a literal, not a table. */
                char first = qualifier[0];
                if (!((first >= 'A' && first <= 'Z') || (first >= 'a' && first <= 'z') || first == '_')) { continue; }

                if (qualifier.Equals(item.TableName, StringComparison.OrdinalIgnoreCase)) { continue; }
                if (canJoinDocType && qualifier.Equals(TABLE_DOCTYPE, StringComparison.OrdinalIgnoreCase)) { continue; }
                if (HasColumn(declared, qualifier)) { continue; }

                return false;
            }

            return true;
        }

        /// <summary>
        /// The table names and aliases a clause introduces for ITSELF - everything that
        /// follows a FROM or a JOIN inside it, plus the alias declared after each.
        ///
        /// A subquery resolves its own qualifiers wherever it appears, so a clause built
        /// as "EXISTS(SELECT 1 FROM C_DocType dt WHERE dt.C_DocType_ID=...)" is complete
        /// as it stands and needs nothing from the statement around it. Reading those
        /// names out is what lets <see cref="ReferencesResolvableTables"/> accept the
        /// clause instead of refusing it for the alias it declares itself.
        ///
        /// The scan is a tokeniser, not a parser: it skips quoted text and takes the one
        /// or two identifiers after each FROM / JOIN keyword, stopping at a word that is
        /// plainly a keyword rather than an alias. Over-collecting is harmless here - the
        /// result only ever WIDENS what a qualifier may name, and every clause has
        /// already been through <see cref="IsSafeClauseText"/>.
        /// </summary>
        /// <param name="clause">Resolved WhereClause.</param>
        /// <returns>Identifiers the clause resolves on its own (never null).</returns>
        private List<string> CollectClauseSources(string clause)
        {
            List<string> declared = new List<string>();
            if (string.IsNullOrEmpty(clause)) { return declared; }

            bool inLiteral = false;
            int pending = 0;                     // identifiers still expected after FROM / JOIN

            int i = 0;
            while (i < clause.Length)
            {
                char ch = clause[i];

                if (ch == '\'') { inLiteral = !inLiteral; i++; continue; }

                if (inLiteral || !IsIdentifierChar(ch)) { i++; continue; }

                int start = i;
                while (i < clause.Length && IsIdentifierChar(clause[i])) { i++; }
                string word = clause.Substring(start, i - start);

                if (word.Equals("FROM", StringComparison.OrdinalIgnoreCase)
                    || word.Equals("JOIN", StringComparison.OrdinalIgnoreCase))
                {
                    pending = 2;                 // the table, then possibly its alias
                    continue;
                }

                if (pending == 0) { continue; }

                /* A keyword ends the run: what follows FROM is a table and at most one
                   alias, and "FROM C_DocType WHERE" declares no alias at all. */
                if (HasColumn(NotAnAlias, word)) { pending = 0; continue; }

                if (!HasColumn(declared, word)) { declared.Add(word); }
                pending--;
            }

            return declared;
        }

        /// <summary>Whether a character may appear inside an unquoted SQL identifier.</summary>
        /// <param name="ch">Character under test.</param>
        /// <returns>true for a letter, a digit or an underscore.</returns>
        private bool IsIdentifierChar(char ch)
        {
            return (ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z')
                || (ch >= '0' && ch <= '9') || ch == '_';
        }

        /// <summary>
        /// A copy of the discovered table bound to one of its screens. The column
        /// facts are shared; only the screen identity and its filter differ.
        /// </summary>
        /// <param name="item">Discovered table.</param>
        /// <param name="screen">The screen this copy stands for.</param>
        /// <returns>A source ready for the card.</returns>
        private SourceItem CloneForScreen(SourceItem item, ScreenItem screen)
        {
            SourceItem copy = new SourceItem();

            copy.AD_Table_ID = item.AD_Table_ID;
            copy.TableName = item.TableName;
            copy.TableDisplayName = item.TableDisplayName;
            copy.KeyColumn = item.KeyColumn;
            copy.DocumentNoColumn = item.DocumentNoColumn;
            copy.DateAcctColumn = item.DateAcctColumn;
            copy.MovementDateColumn = item.MovementDateColumn;
            copy.StatementDateColumn = item.StatementDateColumn;
            copy.DateColumn = item.DateColumn;
            copy.DocStatusColumn = item.DocStatusColumn;
            copy.DocTypeColumn = item.DocTypeColumn;
            copy.CurrencyColumn = item.CurrencyColumn;
            copy.ConversionTypeColumn = item.ConversionTypeColumn;
            copy.AmountColumn = item.AmountColumn;
            copy.LineValueExpr = item.LineValueExpr;
            copy.CreatedByExpr = item.CreatedByExpr;

            copy.AD_Window_ID = screen.AD_Window_ID;
            copy.AD_Tab_ID = screen.AD_Tab_ID;
            copy.ScreenDisplayName = screen.WindowName;
            copy.TabName = screen.TabName;
            copy.WhereClause = screen.WhereClause;

            return copy;
        }

        /// <summary>
        /// Resolves a tab's WhereClause against the SESSION context and returns the
        /// usable text, or "" when it cannot be used here.
        ///
        /// Most dictionary WhereClauses that carry an @variable@ carry a GLOBAL one -
        /// @#AD_Client_ID@ and friends - which the session context can supply perfectly
        /// well. Rejecting every clause containing an '@' therefore threw away filters
        /// that were entirely resolvable, and with them the screens those filters exist
        /// to tell apart.
        ///
        /// So the clause is PARSED first, with window number 0 - this widget has no
        /// window and no current record, so only global context resolves, which is the
        /// point. What Env.ParseContext cannot fill in it reports by returning empty,
        /// and anything that still carries an '@' afterwards was a window-level
        /// variable this widget genuinely cannot answer for. Those are still refused:
        /// a half-resolved predicate does not fail loudly, it silently returns the
        /// wrong rows.
        ///
        /// The safety check runs on the RESOLVED text, not the raw text, so a context
        /// value cannot smuggle in a terminator or a comment.
        /// </summary>
        /// <param name="ctx">Session context (supplies the global variables).</param>
        /// <param name="clause">AD_Tab.WhereClause, possibly empty.</param>
        /// <returns>The resolved, checked clause, or "" when unusable.</returns>
        private string ResolveWhereClause(Ctx ctx, string clause)
        {
            if (clause == null) { return ""; }

            string text = clause.Trim();
            if (text.Length == 0) { return ""; }

            if (text.IndexOf('@') >= 0)
            {
                try
                {
                    text = Env.ParseContext(ctx, 0, text, false);
                }
                catch (Exception ex)
                {
                    Log.Log(Level.WARNING, "VAS_198: a tab WhereClause could not be resolved "
                        + "against the session context and is ignored", ex);
                    return "";
                }

                if (string.IsNullOrEmpty(text)) { return ""; }
                text = text.Trim();
            }

            return IsUsableWhereClause(text) ? text : "";
        }

        /// <summary>
        /// Whether an ALREADY RESOLVED WhereClause may be pasted into this widget's SQL.
        ///
        /// The text is the framework's own filter for that window rather than anything
        /// a user typed, and it is executed verbatim every time the window opens - but
        /// it IS dictionary data, and dictionary data can be edited, so it is checked
        /// before it is concatenated. Rejected: a surviving @variable@ (see
        /// <see cref="ResolveWhereClause"/> - by this point one the session context
        /// could not answer for), a statement terminator, and either comment form, any
        /// of which could hide what follows.
        /// </summary>
        /// <param name="clause">Resolved WhereClause, possibly empty.</param>
        /// <returns>true when the clause is safe and self-contained.</returns>
        private bool IsUsableWhereClause(string clause)
        {
            if (clause == null) { return false; }
            if (clause.Length > WHERECLAUSE_MAX) { return false; }

            return IsSafeClauseText(clause);
        }

        /// <summary>
        /// The character-level half of the clause guard, without the length bound: no
        /// surviving @variable@, no statement terminator, neither comment form.
        ///
        /// Split out because a COMPOSED clause - a complement row's negation of its
        /// siblings - is legitimately longer than any tab filter is allowed to be, while
        /// still having to satisfy every one of these.
        /// </summary>
        /// <param name="clause">Clause text, raw or composed.</param>
        /// <returns>true when the text hides nothing.</returns>
        private bool IsSafeClauseText(string clause)
        {
            if (clause == null) { return false; }

            if (clause.IndexOf('@') >= 0) { return false; }
            if (clause.IndexOf(';') >= 0) { return false; }
            if (clause.IndexOf("--", StringComparison.Ordinal) >= 0) { return false; }
            if (clause.IndexOf("/*", StringComparison.Ordinal) >= 0) { return false; }

            return true;
        }

        /// <summary>
        /// Whether one source's filter may be pasted into its statement, applying the
        /// guard that matches where the filter came from.
        ///
        /// A clause read from AD_Tab is dictionary data: bounded in length, and allowed
        /// to qualify columns with the source table's name and nothing else. A clause
        /// this class composed is neither - it is longer by design and it names its own
        /// inner alias - so re-running the raw-text guards over it would reject it and,
        /// worse, drop it SILENTLY, turning a complement row into a row over the whole
        /// table that double counts every one of its filtered siblings.
        /// </summary>
        /// <param name="source">Discovered source carrying the filter.</param>
        /// <returns>true when the filter is safe to concatenate.</returns>
        private bool IsUsableClauseFor(SourceItem source)
        {
            if (source == null || string.IsNullOrEmpty(source.WhereClause)) { return false; }

            if (source.IsComposedWhereClause)
            {
                return source.WhereClause.Length <= WHERECLAUSE_COMPOSED_MAX
                    && IsSafeClauseText(source.WhereClause);
            }

            return IsUsableWhereClause(source.WhereClause)
                && ReferencesResolvableTables(source.WhereClause, source);
        }

        /// <summary>
        /// Whether this source's statement has to join C_DocType - because the filter it
        /// is about to run qualifies a column with it.
        ///
        /// Read off the FINAL clause rather than remembered from the tab, so a composed
        /// one counts too: a catch-all row negates its siblings' filters, and if one of
        /// those named C_DocType then so does the negation.
        ///
        /// The clause's own subqueries are the one thing that does NOT need the join -
        /// they declare their own source - but joining anyway costs a single indexed
        /// lookup per row and keeps this a text test rather than a parse.
        /// </summary>
        /// <param name="source">Discovered, already validated source.</param>
        /// <returns>true when the statement must join C_DocType under its own name.</returns>
        private bool NeedsDocTypeJoin(SourceItem source)
        {
            if (!IsUsableClauseFor(source)) { return false; }
            if (!IsSafeIdentifier(source.DocTypeColumn)) { return false; }

            return source.WhereClause.IndexOf(TABLE_DOCTYPE + ".", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// The join that makes "C_DocType.DocBaseType='MMS'" resolve, aliased to the
        /// table's own name for the same reason <see cref="SourceAlias"/> is: a tab
        /// filter qualifies with the TABLE name, because that is how the framework runs
        /// it. LEFT OUTER so a document whose type has been deactivated is still counted
        /// by a filter that does not mention the type at all.
        /// </summary>
        /// <param name="source">Discovered, already validated source.</param>
        /// <returns>Join clause, or "" when the statement does not need it.</returns>
        private string DocTypeJoin(SourceItem source)
        {
            if (!NeedsDocTypeJoin(source)) { return ""; }

            return " LEFT OUTER JOIN " + TABLE_DOCTYPE + " " + TABLE_DOCTYPE
                 + " ON (" + TABLE_DOCTYPE + "." + COLUMN_DOCTYPE + "="
                 + SourceAlias(source) + "." + source.DocTypeColumn + ")";
        }

        /// <summary>
        /// The label a widget row carries, in the preference order the screen the
        /// user would actually navigate to comes first.
        /// </summary>
        /// <param name="item">Discovered source.</param>
        /// <returns>Display name (never null or empty for a usable source).</returns>
        private string ResolveDisplayName(SourceItem item)
        {
            if (!string.IsNullOrEmpty(item.ScreenDisplayName)) { return item.ScreenDisplayName; }
            if (!string.IsNullOrEmpty(item.TabName)) { return item.TabName; }
            if (!string.IsNullOrEmpty(item.TableDisplayName)) { return item.TableDisplayName; }
            return item.TableName;
        }

        /// <summary>
        /// Two tables can be displayed on the SAME window (header and a posted child
        /// tab, for instance), and two rows reading "Material Receipt" would be
        /// indistinguishable. Only the colliding rows are qualified; a name that is
        /// already unique is left alone.
        ///
        /// Never with the TAB NAME. A header tab is named after its own window or after
        /// the entity generically, so appending it produced "Delivery Order · Delivery
        /// Order" and "Sales Order · Order" - qualifiers that distinguish nothing and
        /// read as a mistake. Three steps instead, each one earning its place:
        ///
        ///   the catch-all   qualified FIRST and alone. Where a table's leftovers sit on
        ///                   one of its own screens those two rows are the only pair
        ///                   colliding, and naming the odd one out settles it - the real
        ///                   screen keeps its own plain name.
        ///   the table       what is left is two different TABLES wearing one window
        ///                   name, so each says which table it is - and only if that says
        ///                   something the row's name does not already contain.
        ///   the window id   the last resort, for two windows carrying the same name over
        ///                   the same table. Nobody wants to read it; two rows the reader
        ///                   cannot tell apart are worse.
        /// Chronological development:
        ///   VAI154      2026-08-26 Tab name dropped as a qualifier; catch-all qualified
        ///                          first and alone, then the table, then the window id
        /// </summary>
        /// <param name="ctx">Session context (supplies the message language).</param>
        /// <param name="sources">Discovered sources, already sorted by display name.</param>
        private void DisambiguateDisplayNames(Ctx ctx, List<SourceItem> sources)
        {
            /* The catch-all row goes first, and alone, because it is the row that is
               different. Where a table's leftovers sit on one of its own screens, that
               screen and its catch-all are the only two rows colliding - and qualifying
               BOTH of them produced "Sales Order · Order" beside "Sales Order · Other",
               burdening the real screen with its tab's name to distinguish it from a row
               that had already distinguished itself. Naming the odd one out is enough. */
            Dictionary<string, int> counts = CountDisplayNames(sources);

            for (int i = 0; i < sources.Count; i++)
            {
                SourceItem item = sources[i];
                if (!item.IsComplement || counts[item.DisplayName] <= 1) { continue; }

                item.DisplayName = item.DisplayName + " · " + OtherRecordsLabel(ctx);
            }

            /* What still collides is two genuinely different things wearing one name -
               most often two tables on one window - so each says what it is. */
            counts = CountDisplayNames(sources);

            for (int i = 0; i < sources.Count; i++)
            {
                SourceItem item = sources[i];
                if (counts[item.DisplayName] <= 1) { continue; }

                string qualifier = PickQualifier(item);
                if (qualifier.Length == 0) { continue; }

                item.DisplayName = item.DisplayName + " · " + qualifier;
            }

            /* Whatever is still ambiguous had nothing of its own to say. The window id is
               not a label anybody wants to read, which is exactly why it is the last
               resort - but two rows the reader cannot tell apart are worse. */
            counts = CountDisplayNames(sources);

            for (int i = 0; i < sources.Count; i++)
            {
                SourceItem item = sources[i];
                if (counts[item.DisplayName] <= 1) { continue; }

                item.DisplayName = item.DisplayName + " #"
                    + item.AD_Window_ID.ToString(CultureInfo.InvariantCulture);
            }
        }

        /// <summary>How many sources carry each display name.</summary>
        /// <param name="sources">Discovered sources.</param>
        /// <returns>Display name -> occurrences.</returns>
        private Dictionary<string, int> CountDisplayNames(List<SourceItem> sources)
        {
            Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.CurrentCultureIgnoreCase);

            for (int i = 0; i < sources.Count; i++)
            {
                string name = sources[i].DisplayName;
                counts[name] = counts.ContainsKey(name) ? counts[name] + 1 : 1;
            }

            return counts;
        }

        /// <summary>
        /// What to qualify a colliding row with: what the row IS, which is its table.
        ///
        /// The TAB NAME is deliberately not a candidate. A header tab is named after its
        /// own window or after the entity generically - the Sales Order window's tab is
        /// "Order" - so appending it yields "Sales Order · Order": a qualifier that adds
        /// nothing and reads as noise. Two rows wearing one window name are two different
        /// TABLES, so the table is what separates them and the table is what is shown.
        /// </summary>
        /// <param name="item">The colliding source.</param>
        /// <returns>Qualifier, or "" when the row has nothing distinguishing to add.</returns>
        private string PickQualifier(SourceItem item)
        {
            if (AddsMeaning(item.TableDisplayName, item.DisplayName)) { return item.TableDisplayName; }
            if (AddsMeaning(item.TableName, item.DisplayName)) { return item.TableName; }

            return "";
        }

        /// <summary>
        /// Whether appending a candidate would tell the reader anything: it has to be
        /// there, and it has to be neither the name itself nor a fragment of it. "Order"
        /// against "Sales Order" fails on the last count - the reader already has that
        /// word, and repeating it only looks like a mistake.
        /// </summary>
        /// <param name="candidate">Qualifier under consideration.</param>
        /// <param name="name">The display name it would be appended to.</param>
        /// <returns>true when appending it would tell the reader something.</returns>
        private bool AddsMeaning(string candidate, string name)
        {
            if (string.IsNullOrEmpty(candidate) || string.IsNullOrEmpty(name)) { return false; }

            return name.IndexOf(candidate, StringComparison.CurrentCultureIgnoreCase) < 0
                && candidate.IndexOf(name, StringComparison.CurrentCultureIgnoreCase) < 0;
        }

        /// <summary>
        /// The word that qualifies a catch-all row sharing its name with a real screen.
        /// Seeded as the AD_Message VAS_198_OtherRecords; an installation that has not
        /// seeded it gets readable English rather than the raw key, which is the same
        /// contract the widget's own labels follow.
        /// </summary>
        /// <param name="ctx">Session context (supplies the message language).</param>
        /// <returns>Translated qualifier, or "Other".</returns>
        private string OtherRecordsLabel(Ctx ctx)
        {
            string text = Msg.GetMsg(ctx, MESSAGE_OTHERRECORDS);

            if (string.IsNullOrEmpty(text)
                || text.Equals(MESSAGE_OTHERRECORDS, StringComparison.OrdinalIgnoreCase))
            {
                return "Other";
            }

            return text;
        }

        /// <summary>
        /// Guards every identifier that reaches the generated SQL. The names come
        /// from the Application Dictionary rather than from the client, so this is a
        /// belt-and-braces check - but a dictionary is data, and data can be edited,
        /// so nothing is concatenated that is not a plain unqualified identifier.
        /// </summary>
        /// <param name="identifier">Table or column name returned by the dictionary.</param>
        /// <returns>true when the name is safe to concatenate.</returns>
        private bool IsSafeIdentifier(string identifier)
        {
            if (string.IsNullOrEmpty(identifier)) { return false; }
            if (identifier.Length > IDENTIFIER_MAX) { return false; }

            char first = identifier[0];
            if (!((first >= 'A' && first <= 'Z') || (first >= 'a' && first <= 'z'))) { return false; }

            for (int i = 1; i < identifier.Length; i++)
            {
                char ch = identifier[i];
                bool ok = (ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z')
                       || (ch >= '0' && ch <= '9') || ch == '_';
                if (!ok) { return false; }
            }

            return true;
        }

        // ─────────────────────────────────────────────────────────────────────
        // §3  The transaction-type figures
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The unposted document count and base-currency value of every discovered
        /// transaction type, for one period. Types with nothing outstanding are left
        /// out - the card is a work list, and a row reading zero is not work.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="periodId">C_Period_ID selected by the user.</param>
        /// <returns>Populated <see cref="PeriodData"/> (never null).</returns>
        public PeriodData GetPeriodData(Ctx ctx, int periodId)
        {
            PeriodData result = new PeriodData();
            result.C_Period_ID = periodId;
            result.Sources = new List<SourceTotal>();

            if (ctx == null) { return result; }

            PeriodItem period = GetOpenPeriod(ctx, periodId);
            if (period == null)
            {
                result.ErrorCode = ERROR_NO_PERIOD;
                return result;
            }

            result.PeriodName = period.Name;

            BaseCurrency baseCurrency = GetBaseCurrency(ctx);
            result.BaseCurrencyIso = baseCurrency.Iso;
            result.BaseCurrencySymbol = baseCurrency.Symbol;
            result.BaseCurrencyPrecision = baseCurrency.Precision;

            List<SourceItem> sources = DiscoverSources(ctx);

            /* One aggregate query per discovered table, deliberately. Each source is
               a DIFFERENT physical table, so there is no single pass to share, and
               MRole has to be applied to each one independently (§23) - a combined
               UNION filtered once afterwards would leak rows across roles. A source
               that fails is logged and skipped rather than blanking the whole card:
               one mis-configured dictionary row must not cost the user the other
               twenty transaction types.

               "Source" here is a SCREEN, not a table: a table displayed on four
               windows is four queries, each carrying that window's own tab filter, so
               Purchase Order and Sales Order report their own figures out of one
               C_Order table. */
            for (int i = 0; i < sources.Count; i++)
            {
                SourceTotal total = ReadSourceTotal(ctx, sources[i], period, baseCurrency);
                if (total == null || total.RecordCount <= 0) { continue; }

                result.Sources.Add(total);
                result.TotalRecordCount += total.RecordCount;
            }

            /* Biggest exposure first: the card shows only as many rows as its cell
               fits, so the first page has to be the page worth reading. Types with no
               amount strategy fall to the bottom of the value order and are then
               ordered by count. */
            result.Sources.Sort(delegate (SourceTotal a, SourceTotal b)
            {
                int byValue = b.BaseValue.CompareTo(a.BaseValue);
                if (byValue != 0) { return byValue; }

                int byCount = b.RecordCount.CompareTo(a.RecordCount);
                if (byCount != 0) { return byCount; }

                return string.Compare(a.DisplayName, b.DisplayName, StringComparison.CurrentCultureIgnoreCase);
            });

            return result;
        }

        /// <summary>
        /// Count and base value of the unposted documents behind ONE screen.
        ///
        /// The screen's own tab filter is part of the WHERE clause, so a table shown
        /// on several windows reports a different figure for each: Purchase Order and
        /// Sales Order both read C_Order, and each counts only its own.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="source">Discovered, already validated source.</param>
        /// <param name="period">Validated period carrying the date bounds.</param>
        /// <param name="baseCurrency">Tenant base currency for the conversion.</param>
        /// <returns>Populated <see cref="SourceTotal"/>, or null when the source could
        /// not be read (already logged).</returns>
        private SourceTotal ReadSourceTotal(Ctx ctx, SourceItem source, PeriodItem period,
            BaseCurrency baseCurrency)
        {
            SourceTotal total = new SourceTotal();
            total.AD_Table_ID = source.AD_Table_ID;
            total.AD_Window_ID = source.AD_Window_ID;
            total.IsComplement = source.IsComplement;
            total.TableName = source.TableName;
            total.DisplayName = source.DisplayName;
            total.HasValue = HasValueStrategy(source);

            string alias = SourceAlias(source);

            /* Two levels, deliberately. The INNER select values one document per row
               and carries no aggregate at all; the outer one aggregates those rows.

               Flattening the two would put the value expression inside SUM(), and for
               a line-valued source that expression is a correlated sub-select - an
               aggregate over a correlated sub-select is the kind of construct the two
               back ends disagree about. As a plain select-list expression it is
               unambiguous on both.

               MRole is applied to the INNER select, where the physical table actually
               is; the outer one reads a derived alias and must never be filtered
               again. */
            string rowValue = total.HasValue ? SourceValueExpr(source, baseCurrency) : "0";

            /* The screen's own filter may name the document TYPE - that is what separates
               Delivery Order from Material Receipt - so the statement carries the join
               that makes it resolve. */
            string inner = @"
                SELECT " + rowValue + @" AS Row_Value
                FROM " + source.TableName + " " + alias + DocTypeJoin(source) + @"
                WHERE " + SourceWhere(source);

            inner = MRole.GetDefault(ctx).AddAccessSQL(inner, alias, MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string sql = @"
                SELECT COUNT(1) AS Record_Count,
                       SUM(SourceRows.Row_Value) AS Base_Value
                FROM (" + inner + @") SourceRows";

            /* Appearance order: the value expression binds the base currency, then
               the WHERE clause binds the client and the period bounds. */
            List<SqlParameter> parameters = new List<SqlParameter>();
            if (UsesCurrencyBinds(source, baseCurrency)) { AddBaseCurrencyParameters(parameters, baseCurrency); }
            parameters.AddRange(SourceWhereParameters(ctx, period));

            DataSet ds;
            try
            {
                ds = DB.ExecuteDataset(sql, parameters.ToArray(), null);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_198_UnPostedAccountEntries.ReadSourceTotal " + source.TableName, ex);
                return null;
            }

            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) { return total; }

            DataRow row = ds.Tables[0].Rows[0];
            total.RecordCount = Util.GetValueOfInt(row["Record_Count"]);
            total.BaseValue = Util.GetValueOfDecimal(row["Base_Value"]);

            return total;
        }


        // ─────────────────────────────────────────────────────────────────────
        // §4  Detail (server-side paging)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// One page of the unposted documents of one transaction type, plus the total
        /// row count so the client can page without holding the whole set. The type is
        /// a SCREEN, identified by AD_Table_ID plus AD_Window_ID and re-discovered
        /// here - the client never supplies a table name, a column name or a filter.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="periodId">C_Period_ID selected by the user.</param>
        /// <param name="tableId">AD_Table_ID of the transaction type opened.</param>
        /// <param name="windowId">AD_Window_ID of the screen opened - the second part
        /// of the row's identity, since one table can appear on several.</param>
        /// <param name="isComplement">Whether the row opened is the table's catch-all -
        /// the third part of its identity, because a catch-all may share a window with a
        /// filtered screen.</param>
        /// <param name="pageNo">1-based page number.</param>
        /// <param name="pageSize">Rows per page (clamped server-side).</param>
        /// <returns>Populated <see cref="RecordPage"/> (never null).</returns>
        public RecordPage GetRecords(Ctx ctx, int periodId, int tableId, int windowId,
            bool isComplement, int pageNo, int pageSize)
        {
            RecordPage result = new RecordPage();
            result.Rows = new List<UnPostedRow>();
            result.AD_Table_ID = tableId;
            result.AD_Window_ID = windowId;
            result.IsComplement = isComplement;
            result.C_Period_ID = periodId;

            if (ctx == null) { return result; }

            PeriodItem period = GetOpenPeriod(ctx, periodId);
            if (period == null)
            {
                result.ErrorCode = ERROR_NO_PERIOD;
                return result;
            }

            result.PeriodName = period.Name;

            /* The client's ids are only ever a KEY into what discovery returned - a
               pair the dictionary did not hand out reaches no query at all, and the
               screen filter that comes with it is the server's, never the client's. */
            SourceItem source = FindSource(ctx, tableId, windowId, isComplement);
            if (source == null)
            {
                result.ErrorCode = ERROR_INVALID_REQUEST;
                return result;
            }

            result.TableName = source.TableName;
            result.DisplayName = source.DisplayName;
            result.KeyColumn = source.KeyColumn;
            result.DateColumn = source.DateColumn;
            result.AD_Window_ID = source.AD_Window_ID;
            result.IsComplement = source.IsComplement;
            result.HasValue = HasValueStrategy(source);

            /* A line-valued type reports no document currency even if its table
               happens to carry the column: the figure shown is a sum of stored
               base-currency costs, so labelling it with a document currency would
               attach the wrong unit to a correct number. */
            result.HasCurrency = IsSafeIdentifier(source.CurrencyColumn)
                && string.IsNullOrEmpty(source.LineValueExpr);

            result.HasDocType = IsSafeIdentifier(source.DocTypeColumn);
            result.HasCreatedBy = !string.IsNullOrEmpty(source.CreatedByExpr);

            if (pageSize < PAGESIZE_MIN || pageSize > PAGESIZE_MAX) { pageSize = PAGESIZE_DEFAULT; }
            if (pageNo < 1) { pageNo = 1; }

            result.PageSize = pageSize;

            BaseCurrency baseCurrency = GetBaseCurrency(ctx);
            result.BaseCurrencyIso = baseCurrency.Iso;
            result.BaseCurrencySymbol = baseCurrency.Symbol;
            result.BaseCurrencyPrecision = baseCurrency.Precision;

            /* The screen's total already IS the row count of its list, so the count
               query is the same one the card ran - no second counting shape. */
            SourceTotal total = ReadSourceTotal(ctx, source, period, baseCurrency);
            if (total == null)
            {
                result.ErrorCode = ERROR_INVALID_REQUEST;
                return result;
            }

            result.Total = total.RecordCount;

            if (result.Total == 0)
            {
                result.PageNo = 1;
                return result;
            }

            int totalPages = (result.Total + pageSize - 1) / pageSize;
            if (pageNo > totalPages) { pageNo = totalPages; }
            result.PageNo = pageNo;

            result.Rows = ReadSourceRows(ctx, source, period, baseCurrency, pageNo, pageSize);

            return result;
        }

        /// <summary>
        /// Re-runs discovery and returns the one source the client asked for.
        /// Discovery is a pair of dictionary queries, not a scan of transaction data,
        /// so re-running it per request costs little and keeps the server the only
        /// authority on which identifiers are legal.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="tableId">AD_Table_ID the client selected.</param>
        /// <param name="windowId">AD_Window_ID the client selected - the second part of
        /// a row's identity, since one table can be several rows.</param>
        /// <param name="isComplement">Whether the row selected was the table's catch-all
        /// - the third part, since a catch-all can share a window with a filtered
        /// screen. Matched exactly first, so the two never answer for each other.</param>
        /// <returns>The matching source, or null when it is not a discovered one.</returns>
        private SourceItem FindSource(Ctx ctx, int tableId, int windowId, bool isComplement)
        {
            if (tableId <= 0) { return null; }

            List<SourceItem> sources = DiscoverSources(ctx);

            for (int i = 0; i < sources.Count; i++)
            {
                if (sources[i].AD_Table_ID != tableId) { continue; }
                if (sources[i].AD_Window_ID != windowId) { continue; }
                if (sources[i].IsComplement == isComplement) { return sources[i]; }
            }

            /* The window still identifies the row on its own wherever no catch-all
               shares it, which is the ordinary case. */
            for (int i = 0; i < sources.Count; i++)
            {
                if (sources[i].AD_Table_ID != tableId) { continue; }
                if (sources[i].AD_Window_ID == windowId) { return sources[i]; }
            }

            /* A table that collapsed to one row (its screens could not be separated)
               answers for any of its windows - the client may still be holding an id
               from before the dictionary changed. */
            for (int i = 0; i < sources.Count; i++)
            {
                if (sources[i].AD_Table_ID == tableId) { return sources[i]; }
            }

            return null;
        }

        /// <summary>
        /// One page of unposted documents of a discovered table. Every column beyond
        /// the key and the accounting date is optional, so the SELECT list is built
        /// from what the dictionary confirmed the table has; a missing column becomes
        /// a typed literal rather than an absent result column, so the reader below
        /// can address every column by name unconditionally.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="source">Discovered, already validated source.</param>
        /// <param name="period">Validated period carrying the date bounds.</param>
        /// <param name="baseCurrency">Tenant base currency for the conversion.</param>
        /// <param name="pageNo">1-based page number (already clamped).</param>
        /// <param name="pageSize">Rows per page (already clamped).</param>
        /// <returns>Materialised rows (never null).</returns>
        private List<UnPostedRow> ReadSourceRows(Ctx ctx, SourceItem source, PeriodItem period,
            BaseCurrency baseCurrency, int pageNo, int pageSize)
        {
            List<UnPostedRow> rows = new List<UnPostedRow>();

            string alias = SourceAlias(source);

            bool hasDoc = IsSafeIdentifier(source.DocumentNoColumn);
            bool hasDocType = IsSafeIdentifier(source.DocTypeColumn);
            bool hasCurrency = IsSafeIdentifier(source.CurrencyColumn);
            bool hasCreatedBy = !string.IsNullOrEmpty(source.CreatedByExpr);
            bool hasAmount = HasValueStrategy(source);
            bool isLineValued = !string.IsNullOrEmpty(source.LineValueExpr);

            StringBuilder sb = new StringBuilder();
            sb.Append(" SELECT ").Append(alias).Append(".").Append(source.KeyColumn).Append(" AS Record_ID");
            sb.Append(",").Append(hasDoc ? "COALESCE(" + alias + "." + source.DocumentNoColumn + ",N'')" : "N''")
              .Append(" AS Document_No");
            sb.Append(",").Append(alias).Append(".").Append(source.DateColumn).Append(" AS Date_Acct");

            /* The document type, preferring its translation for the session language.
               One screen can carry several document types - a Purchase Order window
               holds standard orders and blanket orders alike - so this column is what
               tells the rows of one list apart. A table without a C_DocType_ID column
               simply has none to show. */
            sb.Append(",").Append(hasDocType ? "COALESCE(dttrl.Name," + TABLE_DOCTYPE + ".Name,N'')" : "N''")
              .Append(" AS Doc_Type_Name");

            /* The document's own currency, and its own value in it. Without a
               currency column there is nothing to label a document amount with. */
            /* ISO_Code is a plain CHAR column while CurSymbol is national text, so it is
               cast before it stands beside one - see CodeAsText. */
            sb.Append(",").Append(hasCurrency ? "COALESCE(" + CodeAsText("cur.ISO_Code") + ",N'')" : "N''")
              .Append(" AS Currency_Iso");
            sb.Append(",").Append(hasCurrency ? "COALESCE(cur.CurSymbol," + CodeAsText("cur.ISO_Code") + ",N'')" : "N''")
              .Append(" AS Currency_Symbol");
            sb.Append(",").Append(hasCurrency ? "COALESCE(cur.StdPrecision,2)" : "2").Append(" AS Currency_Precision");

            /* Who raised the document - the person to go and ask why it is still
               unposted, which is usually the next thing the reader wants. */
            sb.Append(",").Append(hasCreatedBy ? source.CreatedByExpr : "N''").Append(" AS Created_By_Name");

            /* The document's own value in its own currency - the only amount the
               record list shows. The converted totals belong to the card, which is
               where figures in different currencies are added together; a list of
               records is not, so nothing here is converted and the statement binds no
               currency at all. A line-valued document has no document currency in the
               first place: its stored costs are base-currency figures already. */
            string documentValue = "0";
            if (isLineValued) { documentValue = source.LineValueExpr; }
            else if (hasAmount) { documentValue = "ABS(COALESCE(" + alias + "." + source.AmountColumn + ",0))"; }

            sb.Append(",").Append(documentValue).Append(" AS Document_Value");

            sb.Append(" FROM ").Append(source.TableName).Append(" ").Append(alias);
            if (hasDocType)
            {
                /* Aliased to the table's own name so it serves BOTH purposes at once:
                   naming the type in the list, and resolving a screen filter that says
                   "C_DocType.DocBaseType='MMS'" - the predicate that separates Delivery
                   Order from Material Receipt. One join, because the count query and this
                   one must select from the same shape or they disagree.

                   No IsActive test on the join: a document whose type was deactivated
                   still exists and is still unposted, and nulling the join would drop it
                   from every type-based screen rather than merely leaving it unnamed. */
                sb.Append(" LEFT OUTER JOIN ").Append(TABLE_DOCTYPE).Append(" ").Append(TABLE_DOCTYPE)
                  .Append(" ON (").Append(TABLE_DOCTYPE).Append(".").Append(COLUMN_DOCTYPE).Append("=")
                  .Append(alias).Append(".").Append(source.DocTypeColumn).Append(")");
                sb.Append(" LEFT OUTER JOIN C_DocType_Trl dttrl ON (dttrl.C_DocType_ID=")
                  .Append(TABLE_DOCTYPE).Append(".").Append(COLUMN_DOCTYPE)
                  .Append(" AND dttrl.AD_Language=@AD_Language AND dttrl.IsActive='Y')");
            }
            if (hasCurrency)
            {
                sb.Append(" LEFT OUTER JOIN C_Currency cur ON (cur.C_Currency_ID=").Append(alias).Append(".")
                  .Append(source.CurrencyColumn).Append(")");
            }
            if (hasCreatedBy)
            {
                /* LEFT OUTER, not INNER: a document raised by a user since deactivated
                   must still appear in the list - it is exactly the kind that goes
                   unposted. */
                sb.Append(" LEFT OUTER JOIN ").Append(TABLE_USER).Append(" usr ON (usr.AD_User_ID=")
                  .Append(alias).Append(".").Append(COLUMN_CREATEDBY).Append(")");
            }
            sb.Append(" WHERE ").Append(SourceWhere(source));

            string sql = MRole.GetDefault(ctx).AddAccessSQL(sb.ToString(), alias,
                MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            /* ORDER BY and the paging suffix go on AFTER the access SQL. Newest
               accounting date first, then the key so the order is total and a page
               boundary never repeats or drops a row. */
            sql += " ORDER BY " + alias + "." + source.DateColumn + " DESC,"
                 + alias + "." + source.KeyColumn + " DESC";
            sql += PagingSuffix(pageSize, (pageNo - 1) * pageSize);

            /* Appearance order: the document-type join binds the language, then the
               WHERE clause binds the client and the period bounds. Nothing here
               converts a currency, so there are no currency binds. */
            List<SqlParameter> parameters = new List<SqlParameter>();
            if (hasDocType) { parameters.Add(new SqlParameter("@AD_Language", ctx.GetAD_Language())); }
            parameters.AddRange(SourceWhereParameters(ctx, period));

            DataSet ds;
            try
            {
                ds = DB.ExecuteDataset(sql, parameters.ToArray(), null);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_198_UnPostedAccountEntries.ReadSourceRows " + source.TableName, ex);
                return rows;
            }

            if (ds == null || ds.Tables.Count == 0) { return rows; }

            DataTable dt = ds.Tables[0];
            for (int i = 0; i < dt.Rows.Count; i++)
            {
                DataRow dr = dt.Rows[i];

                UnPostedRow row = new UnPostedRow();
                row.Record_ID = Util.GetValueOfInt(dr["Record_ID"]);
                row.DocumentNo = Util.GetValueOfString(dr["Document_No"]);
                row.DateAcct = Util.GetValueOfDateTime(dr["Date_Acct"]);
                row.DocumentType = Util.GetValueOfString(dr["Doc_Type_Name"]);
                row.CreatedByName = Util.GetValueOfString(dr["Created_By_Name"]);
                row.CurrencyIso = Util.GetValueOfString(dr["Currency_Iso"]);
                row.CurrencySymbol = Util.GetValueOfString(dr["Currency_Symbol"]);
                row.CurrencyPrecision = Util.GetValueOfInt(dr["Currency_Precision"]);
                row.DocumentValue = Util.GetValueOfDecimal(dr["Document_Value"]);

                /* A table without a DocumentNo column still needs something to
                   identify the row by, and its key is what the Zoom uses anyway. */
                if (string.IsNullOrEmpty(row.DocumentNo))
                {
                    row.DocumentNo = row.Record_ID.ToString(CultureInfo.InvariantCulture);
                }

                rows.Add(row);
            }

            return rows;
        }

        // ─────────────────────────────────────────────────────────────────────
        // §5  Query building blocks
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The WHERE body every query against a discovered table shares: the tenant,
        /// the active flag, the unposted test, the actionable document states (only
        /// where the table has a DocStatus column - §9 forbids applying it blindly)
        /// and the selected period's bounds on the accounting date.
        /// </summary>
        /// <param name="source">Discovered, already validated source.</param>
        /// <returns>WHERE body, without the WHERE keyword.</returns>
        private string SourceWhere(SourceItem source)
        {
            string alias = SourceAlias(source);

            StringBuilder sb = new StringBuilder();

            sb.Append(alias).Append(".AD_Client_ID=@AD_Client_ID");
            sb.Append(" AND ").Append(alias).Append(".IsActive='Y'");

            /* Null-safe: a document that has never been through the posting process
               carries NULL rather than 'N' on some sources. */
            sb.Append(" AND COALESCE(").Append(alias).Append(".").Append(COLUMN_POSTED).Append(",'N')<>'Y'");

            if (IsSafeIdentifier(source.DocStatusColumn))
            {
                sb.Append(" AND ").Append(alias).Append(".").Append(source.DocStatusColumn)
                  .Append(" IN (").Append(DOCSTATUS_ACTIONABLE).Append(")");
            }

            /* Inclusive start, EXCLUSIVE end - the end bind is the day after the period,
               so a document stamped with a time on the period's last day still lands
               inside its own period on both backends, without a TRUNC or a cast. It used
               to be an inclusive bound against midnight, which silently dropped every
               such document and was one of the two reasons this card disagreed with the
               VAS_195 close checklist, which has always bounded periods this way. */
            sb.Append(" AND ").Append(alias).Append(".").Append(source.DateColumn).Append(">=@StartDate");
            sb.Append(" AND ").Append(alias).Append(".").Append(source.DateColumn).Append("<@EndDate");

            /* The screen's own filter, last and parenthesised so an OR inside it
               cannot reach past its own brackets and widen everything above. */
            if (IsUsableClauseFor(source))
            {
                sb.Append(" AND (").Append(source.WhereClause).Append(")");
            }

            return sb.ToString();
        }

        /// <summary>
        /// The alias every generated statement gives the source table: the table's own
        /// name, not a short one.
        ///
        /// That is not cosmetic. A tab's WhereClause qualifies its columns with the
        /// TABLE name - "C_Invoice.IsSOTrx='Y'" - because that is how the framework
        /// runs it, so a statement aliasing the table "src" could not resolve the very
        /// filter that makes one screen different from another. Aliasing it to itself
        /// costs nothing and makes both forms work.
        /// </summary>
        /// <param name="source">Discovered, already validated source.</param>
        /// <returns>The alias, which is also what MRole is applied to.</returns>
        private string SourceAlias(SourceItem source)
        {
            return source.TableName;
        }

        /// <summary>
        /// The bind values <see cref="SourceWhere"/> refers to, in appearance order.
        /// </summary>
        /// <param name="ctx">Session context (supplies the tenant).</param>
        /// <param name="period">Validated period carrying the date bounds.</param>
        /// <returns>Bind values for @AD_Client_ID, @StartDate and @EndDate.</returns>
        private SqlParameter[] SourceWhereParameters(Ctx ctx, PeriodItem period)
        {
            DateTime from = period.StartDate.HasValue ? period.StartDate.Value.Date : DateTime.MinValue;

            /* The day AFTER the period's last day - @EndDate is compared with '<'. */
            DateTime to = period.EndDate.HasValue
                ? period.EndDate.Value.Date.AddDays(1)
                : DateTime.MaxValue.Date;

            return new SqlParameter[]
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@StartDate", from),
                new SqlParameter("@EndDate", to)
            };
        }

        /// <summary>
        /// One document's value in the tenant's base currency: its own amount when it
        /// is already in that currency, otherwise converted through the standard
        /// currencyConvert function at the document's own accounting date and
        /// conversion type. Absolute, so a credit document adds exposure rather than
        /// cancelling one out - the card counts work outstanding, not a net balance.
        /// A source with no currency column is taken to be in the base currency
        /// already, which is what an amount stored without one means.
        /// </summary>
        /// <param name="source">Discovered, already validated source.</param>
        /// <param name="baseCurrency">Tenant base currency.</param>
        /// <returns>Scalar expression over the source alias.</returns>
        private string SourceValueExpr(SourceItem source, BaseCurrency baseCurrency)
        {
            /* A line strategy is already a base-currency figure - the stored costs it
               sums are held in the tenant's currency - so it is returned untouched.
               Passing it through currencyConvert would inflate it a second time. */
            if (!string.IsNullOrEmpty(source.LineValueExpr)) { return source.LineValueExpr; }

            string alias = SourceAlias(source);
            string amount = "COALESCE(" + alias + "." + source.AmountColumn + ",0)";

            if (baseCurrency.C_Currency_ID <= 0 || !IsSafeIdentifier(source.CurrencyColumn))
            {
                return "ABS(" + amount + ")";
            }

            string conversionType = IsSafeIdentifier(source.ConversionTypeColumn)
                ? alias + "." + source.ConversionTypeColumn
                : "NULL";

            return "CASE WHEN " + alias + "." + source.CurrencyColumn + "=@BaseCurrencyIdA THEN ABS(" + amount + ")"
                 + " ELSE ABS(COALESCE(currencyConvert(" + amount + "," + alias + "." + source.CurrencyColumn
                 + ",@BaseCurrencyIdB," + alias + "." + source.DateColumn + "," + conversionType
                 + "," + alias + ".AD_Client_ID," + alias + ".AD_Org_ID),0)) END";
        }

        /// <summary>
        /// Whether this source can be valued at all - by a header amount column or by
        /// a line strategy. Neither means the card and the record list both show a
        /// dash instead of a figure nobody can vouch for.
        /// </summary>
        /// <param name="source">Discovered, already validated source.</param>
        /// <returns>true when a trusted value expression exists.</returns>
        private bool HasValueStrategy(SourceItem source)
        {
            return !string.IsNullOrEmpty(source.AmountColumn)
                || !string.IsNullOrEmpty(source.LineValueExpr);
        }

        /// <summary>
        /// Whether the value expression this source produces actually contains the
        /// conversion CASE, and therefore whether the base-currency binds belong in
        /// the parameter list. A line strategy converts nothing, and a source without
        /// a currency column has nothing to convert from - binding a parameter the
        /// statement never mentions would shift every later bind under the positional
        /// binding the backend adapters use.
        /// </summary>
        /// <param name="source">Discovered, already validated source.</param>
        /// <param name="baseCurrency">Tenant base currency.</param>
        /// <returns>true when @BaseCurrencyIdA / B appear in the statement.</returns>
        private bool UsesCurrencyBinds(SourceItem source, BaseCurrency baseCurrency)
        {
            if (!string.IsNullOrEmpty(source.LineValueExpr)) { return false; }
            if (string.IsNullOrEmpty(source.AmountColumn)) { return false; }
            if (baseCurrency.C_Currency_ID <= 0) { return false; }

            return IsSafeIdentifier(source.CurrencyColumn);
        }

        /// <summary>
        /// Binds the currency id behind the conversion expression. The expression is
        /// emitted at most once per statement, so there is one pair of binds.
        /// </summary>
        /// <param name="parameters">List being built.</param>
        /// <param name="baseCurrency">Tenant base currency.</param>
        private void AddBaseCurrencyParameters(List<SqlParameter> parameters, BaseCurrency baseCurrency)
        {
            if (baseCurrency.C_Currency_ID <= 0) { return; }

            parameters.Add(new SqlParameter("@BaseCurrencyIdA", baseCurrency.C_Currency_ID));
            parameters.Add(new SqlParameter("@BaseCurrencyIdB", baseCurrency.C_Currency_ID));
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

            /* The CASE arms are cast to one character type: CurSymbol is national text and
               ISO_Code is not, and Oracle will not decide between them - see CodeAsText. */
            string sql = @"
                SELECT AcctSchema.C_Currency_ID AS Acct_Currency_ID,
                       Currency.StdPrecision AS Std_Precision,
                       CASE WHEN Currency.CurSymbol IS NOT NULL THEN Currency.CurSymbol ELSE " + CodeAsText("Currency.ISO_Code") + @" END AS Currency_Symbol,
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
        // §6  Contracts
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>One selectable open accounting period of the primary calendar.</summary>
        public class PeriodItem
        {
            public int C_Period_ID { get; set; }
            public string Name { get; set; }

            /// <summary>Inclusive lower bound applied to the document's DateAcct.</summary>
            public DateTime? StartDate { get; set; }

            /// <summary>
            /// The period's own last day. The bound the queries apply is the day AFTER
            /// this one, compared exclusively - see <see cref="SourceWhereParameters"/>.
            /// </summary>
            public DateTime? EndDate { get; set; }

            public int C_Year_ID { get; set; }
            public string FiscalYear { get; set; }
            public int C_Calendar_ID { get; set; }
        }

        /// <summary>
        /// One discovered transaction table and everything the generated SQL needs to
        /// know about it. Server-side only - the client never sees a table or column
        /// name it could send back.
        /// </summary>
        private class SourceItem
        {
            public int AD_Table_ID { get; set; }
            public string TableName { get; set; }
            public string TableDisplayName { get; set; }

            /// <summary>The label the widget row carries.</summary>
            public string DisplayName { get; set; }

            /// <summary>IsKey column - the Zoom target and the sort tie-break.</summary>
            public string KeyColumn { get; set; }

            /* Optional columns; "" when the table does not have them. DocStatus is
               a filter only - it decides whether the actionable-state predicate can
               be applied at all, and is never displayed. */
            public string DocumentNoColumn { get; set; }
            public string DateAcctColumn { get; set; }
            public string MovementDateColumn { get; set; }

            /// <summary>StatementDate probe - the bank statement's accounting date.</summary>
            public string StatementDateColumn { get; set; }
            public string DocStatusColumn { get; set; }
            public string DocTypeColumn { get; set; }
            public string CurrencyColumn { get; set; }
            public string ConversionTypeColumn { get; set; }

            /// <summary>
            /// Which of the two date columns is actually used - see
            /// <see cref="ResolveDateColumn"/>. Every query bounds, orders and
            /// converts by THIS, never by either raw probe result.
            /// </summary>
            public string DateColumn { get; set; }

            /// <summary>Confirmed value column from the trusted amount map, or "".</summary>
            public string AmountColumn { get; set; }

            /// <summary>
            /// Correlated line-valuation expression for a table valued from its lines
            /// instead of a header total, or "". Already a base-currency figure, and
            /// never both this and <see cref="AmountColumn"/>.
            /// </summary>
            public string LineValueExpr { get; set; }

            /// <summary>
            /// How to read the author's name over alias usr, or "" when AD_User could
            /// not be probed. The same for every source - CreatedBy is on every
            /// AD-managed table - but carried here so the row builder needs nothing
            /// but the source in front of it.
            /// </summary>
            public string CreatedByExpr { get; set; }

            /* The screen this source stands for - its name, its Zoom target, and the
               tab filter that separates it from the table's other screens. */
            public int AD_Window_ID { get; set; }
            public int AD_Tab_ID { get; set; }
            public string ScreenDisplayName { get; set; }
            public string TabName { get; set; }

            /// <summary>
            /// The tab's own WhereClause, already checked by
            /// <see cref="IsUsableWhereClause"/>, or "" when this source covers the
            /// whole table.
            /// </summary>
            public string WhereClause { get; set; }

            /// <summary>
            /// Whether <see cref="WhereClause"/> was COMPOSED here rather than read from
            /// AD_Tab - true on a complement row and on any screen made exclusive of its
            /// narrower siblings. It decides which guard <see cref="IsUsableClauseFor"/>
            /// applies, because a composed filter is longer than a tab filter may be and
            /// names an alias of its own.
            /// </summary>
            public bool IsComposedWhereClause { get; set; }

            /// <summary>
            /// Whether this row is the table's CATCH-ALL - the records none of its
            /// filtered screens claim. The third component of a row's identity, and the
            /// one that lets the catch-all share a window with a filtered screen: without
            /// it the two are indistinguishable to a drill-down request, which is why the
            /// whole split used to be abandoned whenever every window carried a filter.
            /// </summary>
            public bool IsComplement { get; set; }
        }

        /// <summary>
        /// One screen a discovered table can be opened on. Server-side only - the
        /// WhereClause it carries is never sent anywhere.
        /// </summary>
        private class ScreenItem
        {
            public int AD_Window_ID { get; set; }
            public int AD_Tab_ID { get; set; }
            public string WindowName { get; set; }
            public string TabName { get; set; }
            public string WhereClause { get; set; }
        }

        /// <summary>
        /// One transaction type's headline figures. A "type" is a SCREEN - a table, a
        /// window, and whether it is that table's catch-all - because Purchase Order and
        /// Sales Order are two screens over one C_Order table, so those three are what
        /// identify the row and what the client sends back to open it.
        /// </summary>
        public class SourceTotal
        {
            public int AD_Table_ID { get; set; }

            /// <summary>Physical table - diagnostic only; the client never sends it back.</summary>
            public string TableName { get; set; }

            /// <summary>The screen's name, already disambiguated.</summary>
            public string DisplayName { get; set; }

            /// <summary>Unposted documents of this type in the period.</summary>
            public int RecordCount { get; set; }

            /// <summary>Total in the tenant's base currency; 0 when HasValue is false.</summary>
            public decimal BaseValue { get; set; }

            /// <summary>
            /// false when this table has no trusted amount strategy - the client shows
            /// a dash rather than a figure it cannot vouch for.
            /// </summary>
            public bool HasValue { get; set; }

            /// <summary>
            /// The screen. Part of the row's identity, and the window its records zoom
            /// into - which is the same thing, since the row IS that screen.
            /// </summary>
            public int AD_Window_ID { get; set; }

            /// <summary>
            /// true on the table's catch-all row - the records none of its filtered
            /// screens claim. The rest of the row's identity: a catch-all may share a
            /// window with a filtered screen, so the client sends this back alongside the
            /// table and the window when it opens the row.
            /// </summary>
            public bool IsComplement { get; set; }
        }

        /// <summary>Everything the card shows for one period.</summary>
        public class PeriodData
        {
            public int C_Period_ID { get; set; }
            public string PeriodName { get; set; }

            /// <summary>Types with at least one unposted document, biggest value first.</summary>
            public List<SourceTotal> Sources { get; set; }

            /// <summary>Documents across every type - the footer's headline figure.</summary>
            public int TotalRecordCount { get; set; }

            public string BaseCurrencyIso { get; set; }
            public string BaseCurrencySymbol { get; set; }
            public int BaseCurrencyPrecision { get; set; }

            /// <summary>ERROR_* token when the period no longer qualifies.</summary>
            public string ErrorCode { get; set; }
        }

        /// <summary>Everything the bootstrap round trip returns.</summary>
        public class UnPostedBootstrap
        {
            public List<PeriodItem> Periods { get; set; }
            public int C_Period_ID { get; set; }
            public string PeriodName { get; set; }
            public PeriodData Data { get; set; }
        }

        /// <summary>One unposted document in the detail list.</summary>
        public class UnPostedRow
        {
            /// <summary>Key-column value - the Zoom's record id.</summary>
            public int Record_ID { get; set; }

            /// <summary>DocumentNo where the table has one, else the key as text.</summary>
            public string DocumentNo { get; set; }

            public DateTime? DateAcct { get; set; }

            /// <summary>
            /// C_DocType name, translated for the session language. Empty when the
            /// table carries no C_DocType_ID column.
            /// </summary>
            public string DocumentType { get; set; }

            /// <summary>Display name of whoever raised the document.</summary>
            public string CreatedByName { get; set; }

            /* Document currency. */
            public string CurrencyIso { get; set; }
            public string CurrencySymbol { get; set; }
            public int CurrencyPrecision { get; set; }

            /// <summary>
            /// The document's own value in its own currency; 0 without a strategy.
            /// The only amount the record list carries - converted totals belong to
            /// the card, not to a list of individual records.
            /// </summary>
            public decimal DocumentValue { get; set; }
        }

        /// <summary>One page of detail plus the paging state.</summary>
        public class RecordPage
        {
            public int AD_Table_ID { get; set; }
            public string TableName { get; set; }
            public string DisplayName { get; set; }
            public int C_Period_ID { get; set; }
            public string PeriodName { get; set; }

            public List<UnPostedRow> Rows { get; set; }

            public int Total { get; set; }
            public int PageNo { get; set; }
            public int PageSize { get; set; }

            /// <summary>Key column name - the Zoom's primary column.</summary>
            public string KeyColumn { get; set; }

            /// <summary>
            /// Which date column the rows were bounded and sorted by - DateAcct, or
            /// MovementDate on a table that has no DateAcct. The client captions the
            /// date column from this, so a movement-dated type is not mislabelled
            /// "Account Date".
            /// </summary>
            public string DateColumn { get; set; }

            /// <summary>Window the Zoom opens.</summary>
            public int AD_Window_ID { get; set; }

            /// <summary>
            /// Echo of the catch-all half of the row's identity, so a client holding this
            /// page can page it without having to remember what it asked for.
            /// </summary>
            public bool IsComplement { get; set; }

            /// <summary>
            /// false when the type has no trusted amount strategy - both amount
            /// columns then render as a dash rather than a figure nobody can vouch
            /// for, matching the dash the card row already shows.
            /// </summary>
            public bool HasValue { get; set; }

            /// <summary>
            /// false when the table carries no C_Currency_ID column at all. The
            /// client DROPS the currency column entirely in that case rather than
            /// printing a blank one - there is no document currency to report, and an
            /// empty column reads as missing data instead of as "not applicable".
            /// The document amount is then a base-currency figure.
            /// </summary>
            public bool HasCurrency { get; set; }

            /// <summary>
            /// false when the table carries no C_DocType_ID column. Dropped from the
            /// list for the same reason as the currency column: a column of blanks
            /// says nothing, and this type simply has no document types.
            /// </summary>
            public bool HasDocType { get; set; }

            /// <summary>
            /// false only when AD_User could not be probed at all - CreatedBy itself
            /// is on every AD-managed table, so this is near-always true.
            /// </summary>
            public bool HasCreatedBy { get; set; }

            public string BaseCurrencyIso { get; set; }
            public string BaseCurrencySymbol { get; set; }
            public int BaseCurrencyPrecision { get; set; }

            /// <summary>ERROR_* token when the request could not be served.</summary>
            public string ErrorCode { get; set; }
        }

        /// <summary>
        /// One declared line-valuation strategy. A plain value holder - the column
        /// names it carries are candidates, not facts, until the dictionary confirms
        /// them in <see cref="BuildLineValueExpr"/>.
        /// </summary>
        private class LineValueStrategy
        {
            /// <summary>
            /// Declares a strategy.
            /// </summary>
            /// <param name="tableName">Header table the strategy values.</param>
            /// <param name="lineTable">Line table carrying the quantities and costs.</param>
            /// <param name="linkColumn">Line column pointing back at the header key.</param>
            /// <param name="qtyColumns">One column, or two to be subtracted (first
            /// less second) - all of them required.</param>
            /// <param name="optionalQtyColumn">A further quantity added when the line
            /// table has it; ignored when it does not.</param>
            /// <param name="costColumns">Cost columns in OVERRIDE order - the first
            /// that carries a non-zero value wins. At least one must exist on the line
            /// table or the strategy yields no value.</param>
            public LineValueStrategy(string tableName, string lineTable, string linkColumn,
                string[] qtyColumns, string optionalQtyColumn, string[] costColumns)
            {
                TableName = tableName;
                LineTable = lineTable;
                LinkColumn = linkColumn;
                QtyColumns = qtyColumns;
                OptionalQtyColumn = optionalQtyColumn;
                CostColumns = costColumns;
            }

            public string TableName { get; private set; }
            public string LineTable { get; private set; }
            public string LinkColumn { get; private set; }
            public string[] QtyColumns { get; private set; }
            public string OptionalQtyColumn { get; private set; }
            public string[] CostColumns { get; private set; }
        }

        /// <summary>The tenant's primary accounting schema currency.</summary>
        private class BaseCurrency
        {
            public int C_Currency_ID { get; set; }
            public string Iso { get; set; }
            public string Symbol { get; set; }
            public int Precision { get; set; }
        }
    }
}
