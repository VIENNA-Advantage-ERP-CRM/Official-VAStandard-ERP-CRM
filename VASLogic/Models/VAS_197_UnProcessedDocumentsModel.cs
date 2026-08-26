/******************************************************
 * Module Name    : VASLogic
 * Purpose        : Open / Unprocessed Documents dashboard widget data
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
    /// Module Name : VAS_197_UnProcessedDocuments
    /// Purpose     : Backs the VAS_197_UnProcessedDocumentsWidget dashboard widget.
    ///               Every document of ONE open accounting period that has NOT reached
    ///               a settled state - anything whose DocStatus is not Completed,
    ///               Closed, Reversed or Voided - grouped by the screen it belongs to,
    ///               each with a document count, the age of its oldest item, a
    ///               base-currency value and an openable paged record list.
    ///
    ///               The sibling of VAS_198_UnPostedAccountEntries, and deliberately
    ///               its mirror image: that widget chases documents that are finished
    ///               but not yet in the ledger, this one chases documents that are not
    ///               finished at all. Same discovery, same period chip, same paging,
    ///               same Zoom - a different question.
    ///
    ///               NOTHING here is hard-coded to C_Invoice / C_Order / C_Payment /
    ///               GL_Journal. The document types are DISCOVERED from the
    ///               Application Dictionary: a table qualifies when it is an active,
    ///               non-view physical table carrying an active DocStatus column - the
    ///               column IS the definition of a document that can be open.
    ///
    ///               A row of the card is a SCREEN, not a table: one table shown on
    ///               several windows is several rows, so C_Invoice appears as AP
    ///               Invoice / AR Invoice / Expense Invoice and C_Order as Purchase
    ///               Order / Sales Order / RMA. What makes two windows over one table
    ///               different LISTS rather than the same list twice is the tab's own
    ///               WhereClause - the predicate the framework itself applies when it
    ///               opens that window. Because a WhereClause qualifies its columns
    ///               with the TABLE name, every generated statement aliases the source
    ///               table to its own name rather than to something short.
    ///
    ///               A clause carrying @variables@ is RESOLVED against the session
    ///               context first (Env.ParseContext, window 0), so the global ones -
    ///               @#AD_Client_ID@ and its kind - work normally. Only a window-level
    ///               variable, which needs a current record this widget does not have,
    ///               is refused.
    ///
    ///               Where the clauses still cannot separate the windows - an
    ///               unresolvable variable, or no clause at all - the table collapses
    ///               back to ONE row covering all its records, and says so in the log.
    ///               One honest row beats several rows each listing the same documents.
    ///
    ///               Dynamic SQL, safely: a bind parameter cannot be a table or column
    ///               identifier, so the physical statement is composed server-side -
    ///               but ONLY from identifiers the dictionary itself returned, each of
    ///               which is re-checked against <see cref="IsSafeIdentifier"/> before
    ///               it is concatenated. No UI value ever becomes an identifier; every
    ///               business filter (client, period bounds, base currency) stays a
    ///               parameter.
    ///
    ///               Valuation is deliberately NOT metadata-driven. Different tables
    ///               store value differently and a column merely named ...Amt is not
    ///               evidence of an accounting value, so it comes from one of two
    ///               trusted maps and is then CONFIRMED to exist by the same probe:
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
    ///               no value - safer than reporting the wrong accounting figure.
    ///
    ///               Period source: the open periods of the tenant's PRIMARY calendar
    ///               (AD_ClientInfo.C_Calendar_ID) - a period qualifies when at least
    ///               one active C_PeriodControl row of it is Open. Documents are
    ///               bounded by DateAcct, or by MovementDate on the inventory tables
    ///               that carry no DateAcct at all - a short explicit fallback, not a
    ///               scan for anything date-shaped. A table with neither is excluded.
    ///
    ///               MRole row-level security is applied to the main physical table of
    ///               every user-facing query: C_Period alias p, AD_Table alias t for
    ///               the dictionary probes, and the DISCOVERED table for each screen -
    ///               each filtered independently, never once over a combined result.
    ///               GROUP BY, ORDER BY and the paging suffix are appended AFTER
    ///               AddAccessSQL so the FROM-clause parser is not confused by a
    ///               trailing clause. Compatible with PostgreSQL and Oracle.
    /// Chronological development:
    ///   VAI145      2026-08-21 Created
    ///   VAI145      2026-08-24 Tab WhereClause resolved against the session context
    ///                          instead of being discarded whenever it carried an '@';
    ///                          window name falls back to AD_Window.Name
    /// </summary>
    public class VAS_197_UnProcessedDocumentsModel
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_197_UnProcessedDocumentsModel).FullName);

        /* Error tokens; the client resolves the label. */
        public const string ERROR_INVALID_REQUEST = "INVALID";
        public const string ERROR_NO_PERIOD = "NOPERIOD";

        /* C_PeriodControl.PeriodStatus stored code for an open control row. */
        private const string PERIODSTATUS_Open = "O";

        /* The dictionary column that marks a table as holding documents, and the
           states that mean a document is FINISHED. Anything else - drafted, in
           progress, invalid, waiting on approval, whatever the tenant has configured
           - is work still open, which is what this widget lists.

           Completed, Closed, Reversed, Voided. Stored codes compared against a
           column, so no N prefix (see the SQL coding standards). */
        private const string COLUMN_DOCSTATUS = "DocStatus";
        private const string DOCSTATUS_SETTLED = "'CO','CL','RE','VO'";

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

        /// <summary>
        /// The trusted amount strategy: which column carries the value of a given
        /// document table. Deliberately a fixed map and NOT a metadata scan - a column
        /// called PayAmt on one table is the document's value, on another it is a
        /// line's share of one, and no dictionary flag distinguishes them. A table
        /// absent from this map shows its count with no value. Every entry is still
        /// confirmed against AD_Column before it is used, so a map row for a column a
        /// given installation does not have degrades to "no value" instead of failing
        /// the query.
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
        /// GrandTotal, and its value is the stored cost of what moved.
        ///
        /// Those costs are ALREADY in the tenant's base currency, which is why a line
        /// strategy never goes near currencyConvert. Like the header map this is a
        /// fixed list, and every column it names - link, quantity and cost alike - is
        /// confirmed against AD_Column before it reaches the SQL.
        ///
        /// The cost columns are per strategy, not shared, because the tables do not
        /// agree on which one carries the figure: a receipt or a movement line values
        /// at its posting cost falling back to its standing one, while an inventory
        /// line carries PriceCost.
        /// </summary>
        private static readonly List<LineValueStrategy> LineValueStrategies =
            new List<LineValueStrategy>
            {
                new LineValueStrategy("M_InOut", "M_InOutLine", "M_InOut_ID",
                    new string[] { "MovementQty" }, "QtyInternalUse",
                    new string[] { "PostCurrentCostPrice", "CurrentCostPrice" }),

                new LineValueStrategy("M_Movement", "M_MovementLine", "M_Movement_ID",
                    new string[] { "MovementQty" }, "",
                    new string[] { "PostCurrentCostPrice", "CurrentCostPrice" }),

                /* A physical inventory posts the DIFFERENCE it found - counted less
                   booked - while an internal-use inventory posts QtyInternalUse and
                   leaves both of those at zero. Summing the two forms is what makes
                   one expression serve both.

                   PriceCost is what an inventory line values at, and it is already a
                   base-currency figure. The two receipt-style cost columns stay in the
                   list behind it only as a fallback for a schema that has no PriceCost
                   - they are not what this table posts with. */
                new LineValueStrategy("M_Inventory", "M_InventoryLine", "M_Inventory_ID",
                    new string[] { "QtyCount", "QtyBook" }, "QtyInternalUse",
                    new string[] { "PriceCost", "PostCurrentCostPrice", "CurrentCostPrice" })
            };

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
        private const string COLUMN_DOCTYPE = "C_DocType_ID";
        private const string COLUMN_DOCTYPETARGET = "C_DocTypeTarget_ID";

        /// <summary>
        /// Which document-type column actually says what a document IS, per table.
        ///
        /// An order or an invoice carries two: C_DocType_ID, which holds the base
        /// type, and C_DocTypeTarget_ID, which holds the one the user chose. The two
        /// are only reconciled when the document COMPLETES - and nothing in this
        /// widget has completed, by definition. Reading C_DocType_ID here would label
        /// every drafted order with the generic base type instead of "Purchase Order"
        /// or "Blanket Order".
        ///
        /// A fixed map rather than a rule like "prefer Target wherever it exists":
        /// which of the two is authoritative is a fact about the document's lifecycle,
        /// not something a column's presence can be read to imply. Every entry is
        /// still confirmed against AD_Column before it is used, so a table that turns
        /// out not to have the target column falls back to C_DocType_ID rather than
        /// failing the query. Tables absent from the map use C_DocType_ID, which is
        /// the only one they have.
        /// </summary>
        private static readonly Dictionary<string, string> DocTypeColumnByTable =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "C_Order", COLUMN_DOCTYPETARGET },
                { "C_Invoice", COLUMN_DOCTYPETARGET }
            };
        private const string COLUMN_BPARTNER = "C_BPartner_ID";
        private const string COLUMN_CURRENCY = "C_Currency_ID";
        private const string COLUMN_CONVERSIONTYPE = "C_ConversionType_ID";

        // ─────────────────────────────────────────────────────────────────────
        // §1  Period list and bootstrap
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Bootstraps the widget in one round trip: every selectable open period of
        /// the primary calendar, the period to preselect, and that period's open
        /// document types.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <returns>Populated <see cref="UnProcessedBootstrap"/> (never null).</returns>
        public UnProcessedBootstrap GetBootstrap(Ctx ctx)
        {
            UnProcessedBootstrap result = new UnProcessedBootstrap();
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
        /// Every screen this widget may read, discovered from the Application
        /// Dictionary and already reduced to the ones it can actually use.
        ///
        /// A discovered table is dropped when it has no key column (nothing to zoom
        /// to), no usable accounting date (nothing to bound by the period), or no
        /// screen at all - never when it merely has no amount strategy.
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

            foreach (SourceItem item in byTable.Values)
            {
                if (!IsSafeIdentifier(item.TableName)) { continue; }
                if (!IsSafeIdentifier(item.KeyColumn)) { continue; }
                if (!IsSafeIdentifier(item.DateColumn)) { continue; }
                if (!IsSafeIdentifier(item.DocStatusColumn)) { continue; }

                List<ScreenItem> screens;
                if (!screensByTable.TryGetValue(item.AD_Table_ID, out screens)) { continue; }

                /* One row per SCREEN, which is what the user recognises - Purchase
                   Order and Sales Order are two screens over one C_Order table. */
                List<SourceItem> perScreen = SplitByScreen(item, screens);
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

            DisambiguateDisplayNames(sources);

            return sources;
        }

        /// <summary>
        /// Probes the dictionary for every active, non-view physical table carrying an
        /// active DocStatus column, and reports which of the columns this widget can
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
                       MAX(CASE WHEN c.ColumnName='" + COLUMN_DOCSTATUS + @"' THEN c.ColumnName END) AS Doc_Status_Column,
                       MAX(CASE WHEN c.ColumnName='" + COLUMN_DOCSTATUS + @"' THEN c.AD_Reference_Value_ID END) AS Doc_Status_Reference_ID,
                       MAX(CASE WHEN c.ColumnName='" + COLUMN_DOCUMENTNO + @"' THEN c.ColumnName END) AS Document_No_Column,
                       MAX(CASE WHEN c.ColumnName='" + COLUMN_DATEACCT + @"' THEN c.ColumnName END) AS Date_Acct_Column,
                       MAX(CASE WHEN c.ColumnName='" + COLUMN_MOVEMENTDATE + @"' THEN c.ColumnName END) AS Movement_Date_Column,
                       MAX(CASE WHEN c.ColumnName='" + COLUMN_STATEMENTDATE + @"' THEN c.ColumnName END) AS Statement_Date_Column,
                       MAX(CASE WHEN c.ColumnName='" + COLUMN_DOCTYPE + @"' THEN c.ColumnName END) AS Doc_Type_Column,
                       MAX(CASE WHEN c.ColumnName='" + COLUMN_DOCTYPETARGET + @"' THEN c.ColumnName END) AS Doc_Type_Target_Column,
                       MAX(CASE WHEN c.ColumnName='" + COLUMN_BPARTNER + @"' THEN c.ColumnName END) AS BPartner_Column,
                       MAX(CASE WHEN c.ColumnName='" + COLUMN_CURRENCY + @"' THEN c.ColumnName END) AS Currency_Column,
                       MAX(CASE WHEN c.ColumnName='" + COLUMN_CONVERSIONTYPE + @"' THEN c.ColumnName END) AS Conversion_Type_Column,
                       MAX(CASE WHEN c.ColumnName=@AmountColumnA THEN c.ColumnName END) AS Amount_Column_A,
                       MAX(CASE WHEN c.ColumnName=@AmountColumnB THEN c.ColumnName END) AS Amount_Column_B,
                       MAX(CASE WHEN c.ColumnName=@AmountColumnC THEN c.ColumnName END) AS Amount_Column_C
                FROM AD_Table t
                INNER JOIN AD_Column c ON (c.AD_Table_ID=t.AD_Table_ID AND c.IsActive='Y')
                WHERE t.IsActive='Y'
                  AND COALESCE(t.IsView,'N')='N'
                  AND EXISTS(SELECT 1 FROM AD_Column sc WHERE sc.AD_Table_ID=t.AD_Table_ID AND sc.ColumnName='" + COLUMN_DOCSTATUS + @"' AND sc.IsActive='Y')";

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "t", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            /* GROUP BY / ORDER BY go on AFTER the access SQL. */
            sql += " GROUP BY t.AD_Table_ID,t.TableName,t.Name";
            sql += " ORDER BY t.TableName";

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
                item.DocStatusColumn = Util.GetValueOfString(dr["Doc_Status_Column"]);
                item.DocStatusReferenceId = Util.GetValueOfInt(dr["Doc_Status_Reference_ID"]);
                item.DocumentNoColumn = Util.GetValueOfString(dr["Document_No_Column"]);
                item.DateAcctColumn = Util.GetValueOfString(dr["Date_Acct_Column"]);
                item.MovementDateColumn = Util.GetValueOfString(dr["Movement_Date_Column"]);
                item.StatementDateColumn = Util.GetValueOfString(dr["Statement_Date_Column"]);
                item.DateColumn = ResolveDateColumn(item);
                item.DocTypeColumn = ResolveDocTypeColumn(item.TableName, dr);
                item.BPartnerColumn = Util.GetValueOfString(dr["BPartner_Column"]);
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
        /// Which column this table's document type is read from - see
        /// <see cref="DocTypeColumnByTable"/> for why an order and an invoice are read
        /// from the TARGET type while everything else is read from the plain one.
        /// </summary>
        /// <param name="tableName">Discovered physical table name.</param>
        /// <param name="row">The probe row for that table.</param>
        /// <returns>Confirmed document-type column, or "" when the table has none.</returns>
        private string ResolveDocTypeColumn(string tableName, DataRow row)
        {
            string plain = Util.GetValueOfString(row["Doc_Type_Column"]);
            string target = Util.GetValueOfString(row["Doc_Type_Target_Column"]);

            string preferred;
            if (!string.IsNullOrEmpty(tableName)
                && DocTypeColumnByTable.TryGetValue(tableName, out preferred)
                && COLUMN_DOCTYPETARGET.Equals(preferred, StringComparison.OrdinalIgnoreCase)
                && IsSafeIdentifier(target))
            {
                return target;
            }

            return plain;
        }

        /// <summary>
        /// The column the period bounds are applied to: DateAcct where the table has
        /// one, else MovementDate, else StatementDate.
        ///
        /// A SHORT, EXPLICIT fallback list, not a scan for anything date-shaped.
        /// Guessing between DateTrx / DateOrdered / DateInvoiced is ruled out because
        /// those three mean different things. The other two are admitted because for
        /// their documents they ARE the accounting date, and the platform itself treats
        /// them that way:
        ///
        ///   MovementDate    the inventory documents carry no DateAcct at all - for
        ///                   those the movement is the accounting event.
        ///   StatementDate   C_BankStatement likewise has no DateAcct on the header
        ///                   (only its lines do), and MBankStatement tests the period
        ///                   with MPeriod.IsOpen(ctx, GetStatementDate(), ...). Without
        ///                   this third fallback the whole table resolved to no date
        ///                   column and was dropped from discovery in silence.
        ///
        /// Nothing else is accepted; a table with none of the three is still excluded.
        /// Chronological development:
        ///   VAI145      2026-08-24 StatementDate admitted (C_BankStatement)
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
        /// Attaches the two SQL fragments that depend on OTHER tables' columns: the
        /// line-level value expression, where a <see cref="LineValueStrategies"/>
        /// entry names the table, and the business-partner name. One dictionary read
        /// covers every table involved.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="byTable">Discovered tables, keyed by AD_Table_ID.</param>
        private void ApplyLineStrategies(Ctx ctx, Dictionary<int, SourceItem> byTable)
        {
            Dictionary<string, List<string>> lineColumns = ReadLineColumns(ctx);
            if (lineColumns.Count == 0) { return; }

            foreach (SourceItem item in byTable.Values)
            {
                if (!string.IsNullOrEmpty(item.AmountColumn)) { continue; }

                LineValueStrategy strategy = FindLineStrategy(item.TableName);
                if (strategy == null) { continue; }

                item.LineValueExpr = BuildLineValueExpr(item, strategy, lineColumns);
            }
        }

        /// <summary>
        /// The active column names of every line table the strategies refer to. Read
        /// in one statement rather than one per strategy - there are only ever a
        /// handful, and they do not change between requests.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <returns>Line table name -> its active column names (never null).</returns>
        private Dictionary<string, List<string>> ReadLineColumns(Ctx ctx)
        {
            Dictionary<string, List<string>> map =
                new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            List<SqlParameter> parameters = new List<SqlParameter>();
            StringBuilder inList = new StringBuilder();

            for (int i = 0; i < LineValueStrategies.Count; i++)
            {
                string bind = "@ProbeTable" + i.ToString(CultureInfo.InvariantCulture);
                if (inList.Length > 0) { inList.Append(","); }
                inList.Append(bind);
                parameters.Add(new SqlParameter(bind, LineValueStrategies[i].LineTable));
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
        /// </summary>
        /// <param name="item">Discovered source (supplies the header alias' key).</param>
        /// <param name="strategy">Trusted strategy declared for its table.</param>
        /// <param name="lineColumns">Confirmed columns per line table.</param>
        /// <returns>Scalar expression over the source alias, or "" when unusable.</returns>
        private string BuildLineValueExpr(SourceItem item, LineValueStrategy strategy,
            Dictionary<string, List<string>> lineColumns)
        {
            List<string> columns;
            if (!lineColumns.TryGetValue(strategy.LineTable, out columns)) { return ""; }
            if (!IsSafeIdentifier(strategy.LineTable)) { return ""; }
            if (!IsSafeIdentifier(item.KeyColumn)) { return ""; }

            if (!HasColumn(columns, strategy.LinkColumn)) { return ""; }

            for (int i = 0; i < strategy.QtyColumns.Length; i++)
            {
                if (!HasColumn(columns, strategy.QtyColumns[i])) { return ""; }
            }

            string cost = BuildCostExpr(strategy, columns);
            if (string.IsNullOrEmpty(cost)) { return ""; }

            string qty = strategy.QtyColumns.Length == 2
                ? "(COALESCE(ln." + strategy.QtyColumns[0] + ",0)-COALESCE(ln." + strategy.QtyColumns[1] + ",0))"
                : "COALESCE(ln." + strategy.QtyColumns[0] + ",0)";

            if (!string.IsNullOrEmpty(strategy.OptionalQtyColumn)
                && HasColumn(columns, strategy.OptionalQtyColumn))
            {
                qty = "(" + qty + "+COALESCE(ln." + strategy.OptionalQtyColumn + ",0))";
            }

            /* ABS per LINE, not on the sum: a document that moved two products one way
               and one the other is exposure on all three, not the net of them. */
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
        /// Every screen each discovered table appears on - all of them, not one: the
        /// SCREEN is what a card row stands for, so a table shown on four windows is
        /// four candidate rows.
        ///
        /// AD_Tab points at its table directly, so a window is "over" a table when it
        /// has an active, displayed tab for it - no field-level detour, and no
        /// dependence on any particular column being on screen.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <returns>Screens grouped by AD_Table_ID, each list in a stable order.</returns>
        private Dictionary<int, List<ScreenItem>> ReadSourceScreens(Ctx ctx)
        {
            Dictionary<int, List<ScreenItem>> byTable = new Dictionary<int, List<ScreenItem>>();

            /* AD_Window carries no DisplayName column in this schema, so the screen
               name is AD_Window.Name, preferring its translation for the session
               language. The tab name is translated the same way - it is what
               disambiguates two sources on one window. */
            string sql = @"
                SELECT DISTINCT tab.AD_Table_ID AS AD_Table_ID,
                       tab.AD_Tab_ID AS AD_Tab_ID,
                       COALESCE(tabtrl.Name,tab.Name,N'') AS Tab_Name,
                       tab.SeqNo AS Tab_Seq_No,
                       COALESCE(tab.WhereClause,N'') AS Tab_Where_Clause,
                       w.AD_Window_ID AS AD_Window_ID,
                       COALESCE(wtrl.Name,w.DisplayName,N'') AS Window_Name
                FROM AD_Table t
                INNER JOIN AD_Column c ON (c.AD_Table_ID=t.AD_Table_ID)
                INNER JOIN AD_Field f ON (f.AD_Column_ID=c.AD_Column_ID)
                INNER JOIN AD_Tab tab ON (tab.AD_Tab_ID=f.AD_Tab_ID)
                INNER JOIN AD_Window w ON (w.AD_Window_ID=tab.AD_Window_ID)
                INNER JOIN AD_Menu m ON (m.AD_Window_ID=w.AD_Window_ID)
                LEFT OUTER JOIN AD_Window_Trl wtrl ON (wtrl.AD_Window_ID=w.AD_Window_ID AND wtrl.AD_Language=@AD_Language AND wtrl.IsActive='Y')
                LEFT OUTER JOIN AD_Tab_Trl tabtrl ON (tabtrl.AD_Tab_ID=tab.AD_Tab_ID AND tabtrl.AD_Language=@AD_Language AND tabtrl.IsActive='Y')
                WHERE tab.IsActive='Y'
                  AND m.IsActive = 'Y'
                  AND tab.IsDisplayed='Y'
                  AND c.ColumnName='" + COLUMN_DOCSTATUS+ @"'
                  AND w.IsActive='Y'";

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "t", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            /* A total order, so the same installation always produces the same rows in
               the same sequence - the first tab of a window speaks for that window. */
            sql += " ORDER BY tab.AD_Table_ID,Window_Name,tab.SeqNo,w.AD_Window_ID,tab.AD_Tab_ID";

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

                /* One window can show the same table on more than one tab; the first
                   (lowest SeqNo, by the ORDER BY above) speaks for the window. */
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
        /// Turns one discovered table into the card rows it deserves: one per SCREEN.
        ///
        /// Two windows over one table are only different rows if their records can be
        /// told apart, and what tells them apart is the tab's own WhereClause - the
        /// very predicate the framework applies when it opens that window. Where the
        /// clause is usable, each window becomes its own row: Purchase Order beside
        /// Sales Order, both over C_Order.
        ///
        /// Where it is not - a clause carrying @context@ variables this widget cannot
        /// resolve, or no clause at all - the windows cannot be separated, and the
        /// table collapses back to ONE row covering all of its records.
        /// </summary>
        /// <param name="item">Discovered table.</param>
        /// <param name="screens">Every screen it appears on.</param>
        /// <returns>One or more sources (never null; empty when there is no screen).</returns>
        private List<SourceItem> SplitByScreen(SourceItem item, List<ScreenItem> screens)
        {
            List<SourceItem> result = new List<SourceItem>();
            if (screens == null || screens.Count == 0) { return result; }

            List<ScreenItem> separable = new List<ScreenItem>();
            List<ScreenItem> unfiltered = new List<ScreenItem>();

            for (int i = 0; i < screens.Count; i++)
            {
                string clause = screens[i].WhereClause;

                if (clause != null && clause.Trim().Length > 0 && IsUsableWhereClause(clause))
                {
                    separable.Add(screens[i]);
                }
                else
                {
                    unfiltered.Add(screens[i]);
                }
            }

            /* Nothing separable: one row covering every record of the table. */
            if (separable.Count == 0)
            {
                SourceItem single = CloneForScreen(item, screens[0]);
                single.WhereClause = "";
                result.Add(single);
                return result;
            }

            for (int i = 0; i < separable.Count; i++)
            {
                result.Add(CloneForScreen(item, separable[i]));
            }

            /* A window with NO filter of its own is that table's catch-all, and it must
               still get a row.

               Dropping it - which is what happened before - loses documents outright:
               C_Order's Sales Order tab carries no WhereClause while Purchase Order,
               Quotation and Blanket Order all carry one, so an ordinary sales order
               matched none of the surviving rows and appeared nowhere on the card. Its
               row is therefore the COMPLEMENT of its filtered siblings: everything the
               other screens did not claim. That keeps the split exhaustive without
               double counting, which a plain unfiltered row would not.

               One caveat, deliberately left: if a sibling clause tests a NULLable
               column, NOT(clause) is NULL for those rows and they still fall outside
               every row. Making that airtight needs boolean COALESCE, which Oracle has
               no portable form of. */
            if (unfiltered.Count > 0)
            {
                SourceItem rest = CloneForScreen(item, unfiltered[0]);
                rest.WhereClause = NegateClauses(separable);
                result.Add(rest);

                if (unfiltered.Count > 1)
                {
                    Log.Log(Level.WARNING, "VAS_197: " + item.TableName + " has "
                        + unfiltered.Count + " screens with no usable filter; '"
                        + unfiltered[0].WindowName + "' represents them all as the catch-all row");
                }
            }

            return result;
        }

        /// <summary>
        /// The complement of a set of screen filters: everything none of them claims.
        /// </summary>
        /// <param name="screens">Screens carrying resolved, usable clauses.</param>
        /// <returns>Predicate fragment, or "" when there is nothing to negate.</returns>
        private string NegateClauses(List<ScreenItem> screens)
        {
            StringBuilder sb = new StringBuilder();

            for (int i = 0; i < screens.Count; i++)
            {
                if (sb.Length > 0) { sb.Append(" AND "); }
                sb.Append("NOT (").Append(screens[i].WhereClause).Append(")");
            }

            return sb.ToString();
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
            copy.DocStatusColumn = item.DocStatusColumn;
            copy.DocStatusReferenceId = item.DocStatusReferenceId;
            copy.DocumentNoColumn = item.DocumentNoColumn;
            copy.DateAcctColumn = item.DateAcctColumn;
            copy.MovementDateColumn = item.MovementDateColumn;
            copy.StatementDateColumn = item.StatementDateColumn;
            copy.DateColumn = item.DateColumn;
            copy.DocTypeColumn = item.DocTypeColumn;
            copy.BPartnerColumn = item.BPartnerColumn;
            copy.CurrencyColumn = item.CurrencyColumn;
            copy.ConversionTypeColumn = item.ConversionTypeColumn;
            copy.AmountColumn = item.AmountColumn;
            copy.LineValueExpr = item.LineValueExpr;

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
        /// to tell apart: a window whose clause was discarded stops being separable, and
        /// once fewer than two remain, every window over that table collapses into one
        /// row. That is what made Blanket Sales Order and Sales Quotation disappear.
        ///
        /// So the clause is PARSED first, with window number 0 - this widget has no
        /// window and no current record, so only global context resolves, which is the
        /// point. What Env.ParseContext cannot fill in it reports by returning empty,
        /// and anything that still carries an '@' afterwards was a window-level
        /// variable this widget genuinely cannot answer for. Those are still refused:
        /// a half-resolved predicate does not fail loudly, it silently returns the
        /// wrong rows, which on a close checklist is the worse outcome.
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
                    Log.Log(Level.WARNING, "VAS_197: a tab WhereClause could not be resolved "
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
        /// The text is the framework's own filter for that window rather than anything a
        /// user typed, and it is executed verbatim every time the window opens - but it
        /// IS dictionary data, and dictionary data can be edited, so it is checked
        /// before it is concatenated. Rejected: a surviving @variable@ (see
        /// <see cref="ResolveWhereClause"/> - by this point it is one the session
        /// context could not answer for), a statement terminator, and either comment
        /// form, any of which could hide what follows.
        /// </summary>
        /// <param name="clause">Resolved WhereClause, possibly empty.</param>
        /// <returns>true when the clause is safe and self-contained.</returns>
        private bool IsUsableWhereClause(string clause)
        {
            if (clause == null) { return false; }
            if (clause.Length > WHERECLAUSE_MAX) { return false; }

            if (clause.IndexOf('@') >= 0) { return false; }
            if (clause.IndexOf(';') >= 0) { return false; }
            if (clause.IndexOf("--", StringComparison.Ordinal) >= 0) { return false; }
            if (clause.IndexOf("/*", StringComparison.Ordinal) >= 0) { return false; }

            return true;
        }

        /// <summary>
        /// The label a widget row carries, in the preference order the screen the user
        /// would actually navigate to comes first.
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
        /// Two tables can be displayed on the SAME window (a header and a child tab),
        /// and two rows reading "Material Receipt" would be indistinguishable. Only
        /// the colliding rows are qualified with their tab name.
        /// </summary>
        /// <param name="sources">Discovered sources, already sorted by display name.</param>
        private void DisambiguateDisplayNames(List<SourceItem> sources)
        {
            Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.CurrentCultureIgnoreCase);

            for (int i = 0; i < sources.Count; i++)
            {
                string name = sources[i].DisplayName;
                counts[name] = counts.ContainsKey(name) ? counts[name] + 1 : 1;
            }

            for (int i = 0; i < sources.Count; i++)
            {
                SourceItem item = sources[i];
                if (counts[item.DisplayName] <= 1) { continue; }

                string qualifier = !string.IsNullOrEmpty(item.TabName) ? item.TabName : item.TableName;
                item.DisplayName = item.DisplayName + " · " + qualifier;
            }
        }

        /// <summary>
        /// Guards every identifier that reaches the generated SQL. The names come from
        /// the Application Dictionary rather than from the client, so this is a
        /// belt-and-braces check - but a dictionary is data, and data can be edited, so
        /// nothing is concatenated that is not a plain unqualified identifier.
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
        // §3  The screen figures
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The open document count, oldest date and base-currency value of every
        /// discovered screen, for one period. Screens with nothing outstanding are
        /// left out - the card is a work list, and a row reading zero is not work.
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

            /* One aggregate query per discovered screen, deliberately. Each source is
               a DIFFERENT physical table (or a differently filtered view of one), so
               there is no single pass to share, and MRole has to be applied to each
               independently - a combined UNION filtered once afterwards would leak
               rows across roles. A source that fails is logged and skipped rather than
               blanking the whole card: one mis-configured dictionary row must not cost
               the user the other twenty screens. */
            for (int i = 0; i < sources.Count; i++)
            {
                SourceTotal total = ReadSourceTotal(ctx, sources[i], period, baseCurrency);
                if (total == null || total.RecordCount <= 0) { continue; }

                result.Sources.Add(total);
                result.TotalRecordCount += total.RecordCount;
            }

            /* Biggest exposure first: the card shows only as many rows as its cell
               fits, so the first page has to be the page worth reading. Screens with no
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
        /// Count, oldest date and base value of the open documents behind ONE screen.
        ///
        /// The screen's own tab filter is part of the WHERE clause, so a table shown
        /// on several windows reports a different figure for each.
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

            string inner = @"
                SELECT " + rowValue + @" AS Row_Value,
                       " + alias + "." + source.DateColumn + @" AS Row_Date
                FROM " + source.TableName + " " + alias + @"
                WHERE " + SourceWhere(source);

            inner = MRole.GetDefault(ctx).AddAccessSQL(inner, alias, MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            /* The oldest date is what the card's "oldest 03 Apr" reads from - a count
               says how much is outstanding, the age says how badly. */
            string sql = @"
                SELECT COUNT(1) AS Record_Count,
                       SUM(SourceRows.Row_Value) AS Base_Value,
                       MIN(SourceRows.Row_Date) AS Oldest_Date
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
                Log.Log(Level.SEVERE, "VAS_197_UnProcessedDocuments.ReadSourceTotal " + source.TableName, ex);
                return null;
            }

            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) { return total; }

            DataRow row = ds.Tables[0].Rows[0];
            total.RecordCount = Util.GetValueOfInt(row["Record_Count"]);
            total.BaseValue = Util.GetValueOfDecimal(row["Base_Value"]);
            total.OldestDate = Util.GetValueOfDateTime(row["Oldest_Date"]);

            return total;
        }

        // ─────────────────────────────────────────────────────────────────────
        // §4  Detail (server-side paging)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// One page of the open documents of one screen, plus the total row count so
        /// the client can page without holding the whole set. The screen is identified
        /// by AD_Table_ID plus AD_Window_ID and re-discovered here - the client never
        /// supplies a table name, a column name or a filter.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="periodId">C_Period_ID selected by the user.</param>
        /// <param name="tableId">AD_Table_ID of the screen opened.</param>
        /// <param name="windowId">AD_Window_ID of the screen opened.</param>
        /// <param name="pageNo">1-based page number.</param>
        /// <param name="pageSize">Rows per page (clamped server-side).</param>
        /// <returns>Populated <see cref="RecordPage"/> (never null).</returns>
        public RecordPage GetRecords(Ctx ctx, int periodId, int tableId, int windowId,
            int pageNo, int pageSize)
        {
            RecordPage result = new RecordPage();
            result.Rows = new List<UnProcessedRow>();
            result.AD_Table_ID = tableId;
            result.AD_Window_ID = windowId;
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
            SourceItem source = FindSource(ctx, tableId, windowId);
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
            result.HasValue = HasValueStrategy(source);
            result.HasDocType = IsSafeIdentifier(source.DocTypeColumn);
            result.HasBPartner = IsSafeIdentifier(source.BPartnerColumn);

            /* A line-valued screen reports no document currency even if its table
               happens to carry the column: the figure shown is a sum of stored
               base-currency costs, so labelling it with a document currency would
               attach the wrong unit to a correct number. */
            result.HasCurrency = IsSafeIdentifier(source.CurrencyColumn)
                && string.IsNullOrEmpty(source.LineValueExpr);

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

            /* DocStatus is a List reference: the stored codes are not human-readable
               and must never be printed raw. One lookup per request, not per row. */
            result.DocStatusNames = GetRefListNames(ctx, source.DocStatusReferenceId);

            return result;
        }

        /// <summary>
        /// Re-runs discovery and returns the one source the client asked for.
        /// Discovery is a handful of dictionary queries, not a scan of transaction
        /// data, so re-running it per request costs little and keeps the server the
        /// only authority on which identifiers are legal.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="tableId">AD_Table_ID the client selected.</param>
        /// <param name="windowId">AD_Window_ID the client selected.</param>
        /// <returns>The matching source, or null when it is not a discovered one.</returns>
        private SourceItem FindSource(Ctx ctx, int tableId, int windowId)
        {
            if (tableId <= 0) { return null; }

            List<SourceItem> sources = DiscoverSources(ctx);

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
        /// One page of open documents of a discovered screen. Every column beyond the
        /// key, the date and the status is optional, so the SELECT list is built from
        /// what the dictionary confirmed the table has; a missing column becomes a
        /// typed literal rather than an absent result column, so the reader below can
        /// address every column by name unconditionally.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="source">Discovered, already validated source.</param>
        /// <param name="period">Validated period carrying the date bounds.</param>
        /// <param name="baseCurrency">Tenant base currency for the conversion.</param>
        /// <param name="pageNo">1-based page number (already clamped).</param>
        /// <param name="pageSize">Rows per page (already clamped).</param>
        /// <returns>Materialised rows (never null).</returns>
        private List<UnProcessedRow> ReadSourceRows(Ctx ctx, SourceItem source, PeriodItem period,
            BaseCurrency baseCurrency, int pageNo, int pageSize)
        {
            List<UnProcessedRow> rows = new List<UnProcessedRow>();

            string alias = SourceAlias(source);

            bool hasDoc = IsSafeIdentifier(source.DocumentNoColumn);
            bool hasDocType = IsSafeIdentifier(source.DocTypeColumn);
            bool hasBPartner = IsSafeIdentifier(source.BPartnerColumn);
            bool hasCurrency = IsSafeIdentifier(source.CurrencyColumn);
            bool hasAmount = HasValueStrategy(source);
            bool isLineValued = !string.IsNullOrEmpty(source.LineValueExpr);

            StringBuilder sb = new StringBuilder();
            sb.Append(" SELECT ").Append(alias).Append(".").Append(source.KeyColumn).Append(" AS Record_ID");
            sb.Append(",").Append(hasDoc ? "COALESCE(" + alias + "." + source.DocumentNoColumn + ",N'')" : "N''")
              .Append(" AS Document_No");
            sb.Append(",").Append(alias).Append(".").Append(source.DateColumn).Append(" AS Date_Acct");

            /* The stored status code; the client resolves it against DocStatusNames.
               Always present - it is what made this table a source in the first
               place. */
            sb.Append(",COALESCE(").Append(alias).Append(".").Append(source.DocStatusColumn)
              .Append(",N'') AS Doc_Status");

            /* The document type, preferring its translation for the session language.
               One screen can carry several types - a Purchase Order window holds
               standard and blanket orders alike - so this is what tells the rows of
               one list apart. The join target is whichever column ResolveDocTypeColumn
               chose; both are foreign keys into C_DocType, so the join is the same
               either way. */
            sb.Append(",").Append(hasDocType ? "COALESCE(dttrl.Name,dt.Name,N'')" : "N''")
              .Append(" AS Doc_Type_Name");

            /* Who the document is with - the other party to chase. */
            sb.Append(",").Append(hasBPartner ? "COALESCE(bp.Name,N'')" : "N''")
              .Append(" AS Business_Partner_Name");

            sb.Append(",").Append(hasCurrency ? "COALESCE(cur.ISO_Code,N'')" : "N''").Append(" AS Currency_Iso");
            sb.Append(",").Append(hasCurrency ? "COALESCE(cur.CurSymbol,cur.ISO_Code,N'')" : "N''")
              .Append(" AS Currency_Symbol");
            sb.Append(",").Append(hasCurrency ? "COALESCE(cur.StdPrecision,2)" : "2").Append(" AS Currency_Precision");

            /* The document's value in its own currency - the only amount the record
               list shows. The converted totals belong to the card, which is where
               figures in different currencies are added together; a list of records is
               not, so nothing here is converted and the statement binds no currency. */
            string documentValue = "0";
            if (isLineValued) { documentValue = source.LineValueExpr; }
            else if (hasAmount) { documentValue = "ABS(COALESCE(" + alias + "." + source.AmountColumn + ",0))"; }

            sb.Append(",").Append(documentValue).Append(" AS Document_Value");

            sb.Append(" FROM ").Append(source.TableName).Append(" ").Append(alias);
            if (hasDocType)
            {
                sb.Append(" LEFT OUTER JOIN C_DocType dt ON (dt.C_DocType_ID=").Append(alias).Append(".")
                  .Append(source.DocTypeColumn).Append(" AND dt.IsActive='Y')");
                sb.Append(" LEFT OUTER JOIN C_DocType_Trl dttrl ON (dttrl.C_DocType_ID=dt.C_DocType_ID AND dttrl.AD_Language=@AD_Language AND dttrl.IsActive='Y')");
            }
            if (hasBPartner)
            {
                /* LEFT OUTER: a document raised against a partner since deactivated
                   must still appear - it is exactly the kind that stays open. */
                sb.Append(" LEFT OUTER JOIN C_BPartner bp ON (bp.C_BPartner_ID=").Append(alias).Append(".")
                  .Append(source.BPartnerColumn).Append(")");
            }
            if (hasCurrency)
            {
                sb.Append(" LEFT OUTER JOIN C_Currency cur ON (cur.C_Currency_ID=").Append(alias).Append(".")
                  .Append(source.CurrencyColumn).Append(")");
            }
            sb.Append(" WHERE ").Append(SourceWhere(source));

            string sql = MRole.GetDefault(ctx).AddAccessSQL(sb.ToString(), alias,
                MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            /* ORDER BY and the paging suffix go on AFTER the access SQL. OLDEST first
               here, unlike the sibling widget: an open document is a queue, and the
               one that has waited longest is the one to deal with. The key breaks
               ties, so the order is total and a page boundary never repeats or drops
               a row. */
            sql += " ORDER BY " + alias + "." + source.DateColumn + " ASC,"
                 + alias + "." + source.KeyColumn + " ASC";
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
                Log.Log(Level.SEVERE, "VAS_197_UnProcessedDocuments.ReadSourceRows " + source.TableName, ex);
                return rows;
            }

            if (ds == null || ds.Tables.Count == 0) { return rows; }

            DataTable dt = ds.Tables[0];
            for (int i = 0; i < dt.Rows.Count; i++)
            {
                DataRow dr = dt.Rows[i];

                UnProcessedRow row = new UnProcessedRow();
                row.Record_ID = Util.GetValueOfInt(dr["Record_ID"]);
                row.DocumentNo = Util.GetValueOfString(dr["Document_No"]);
                row.DateAcct = Util.GetValueOfDateTime(dr["Date_Acct"]);
                row.BusinessPartnerName = Util.GetValueOfString(dr["Business_Partner_Name"]);
                row.DocumentType = Util.GetValueOfString(dr["Doc_Type_Name"]);
                row.DocStatus = Util.GetValueOfString(dr["Doc_Status"]);
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
        /// The WHERE body every query against a discovered screen shares: the tenant,
        /// the active flag, the not-yet-settled test, the selected period's bounds on
        /// the accounting date, and the screen's own tab filter.
        /// </summary>
        /// <param name="source">Discovered, already validated source.</param>
        /// <returns>WHERE body, without the WHERE keyword.</returns>
        private string SourceWhere(SourceItem source)
        {
            string alias = SourceAlias(source);

            StringBuilder sb = new StringBuilder();

            sb.Append(alias).Append(".AD_Client_ID=@AD_Client_ID");
            sb.Append(" AND ").Append(alias).Append(".IsActive='Y'");

            /* The heart of the widget: everything that has NOT settled. Spelled with
               an explicit IS NULL rather than left to COALESCE, because NOT IN against
               a NULL yields NULL - which would silently drop every document whose
               status has never been set, and those are the most open of all. */
            sb.Append(" AND (").Append(alias).Append(".").Append(source.DocStatusColumn).Append(" IS NULL")
              .Append(" OR ").Append(alias).Append(".").Append(source.DocStatusColumn)
              .Append(" NOT IN (").Append(DOCSTATUS_SETTLED).Append("))");

            sb.Append(" AND ").Append(alias).Append(".").Append(source.DateColumn).Append(">=@StartDate");
            sb.Append(" AND ").Append(alias).Append(".").Append(source.DateColumn).Append("<=@EndDate");

            /* The screen's own filter, last and parenthesised so an OR inside it
               cannot reach past its own brackets and widen everything above. */
            if (!string.IsNullOrEmpty(source.WhereClause) && IsUsableWhereClause(source.WhereClause))
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
            DateTime to = period.EndDate.HasValue ? period.EndDate.Value.Date : DateTime.MaxValue.Date;

            return new SqlParameter[]
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@StartDate", from),
                new SqlParameter("@EndDate", to)
            };
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
        /// One document's value in the tenant's base currency: its own amount when it
        /// is already in that currency, otherwise converted through the standard
        /// currencyConvert function at the document's own date and conversion type.
        /// Absolute, so a credit document adds exposure rather than cancelling one out
        /// - the card counts work outstanding, not a net balance.
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
        /// Every value of one List reference, translated for the session language, as
        /// code -> display name. Read once per detail request rather than once per
        /// row, so a page of eight documents still costs one lookup.
        /// </summary>
        /// <param name="ctx">Session context (supplies the UI language).</param>
        /// <param name="referenceId">AD_Column.AD_Reference_Value_ID of the list column.</param>
        /// <returns>Code -> display name (never null; empty when the column is not
        /// list-based).</returns>
        private Dictionary<string, string> GetRefListNames(Ctx ctx, int referenceId)
        {
            Dictionary<string, string> names = new Dictionary<string, string>();
            if (ctx == null || referenceId <= 0) { return names; }

            /* The translation is a LEFT OUTER JOIN so an untranslated value still
               returns its base name, and the COALESCE falls back to the raw code
               rather than to nothing. */
            string sql = @"
                SELECT rl.Value AS Ref_Value,
                       COALESCE(rlt.Name,rl.Name,rl.Value) AS Display_Name
                FROM AD_Ref_List rl
                LEFT OUTER JOIN AD_Ref_List_Trl rlt ON (rlt.AD_Ref_List_ID=rl.AD_Ref_List_ID AND rlt.AD_Language=@AD_Language AND rlt.IsActive='Y')
                WHERE rl.AD_Reference_ID=@AD_Reference_ID
                  AND rl.IsActive='Y'";

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@AD_Language", ctx.GetAD_Language()),
                new SqlParameter("@AD_Reference_ID", referenceId)
            };

            DataSet ds = DB.ExecuteDataset(sql, parameters, null);
            if (ds == null || ds.Tables.Count == 0) { return names; }

            DataTable dt = ds.Tables[0];
            for (int i = 0; i < dt.Rows.Count; i++)
            {
                string code = Util.GetValueOfString(dt.Rows[i]["Ref_Value"]);
                if (string.IsNullOrEmpty(code)) { continue; }
                names[code] = Util.GetValueOfString(dt.Rows[i]["Display_Name"]);
            }

            return names;
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
        // §6  Contracts
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>One selectable open accounting period of the primary calendar.</summary>
        public class PeriodItem
        {
            public int C_Period_ID { get; set; }
            public string Name { get; set; }

            /// <summary>Inclusive lower bound applied to the document's date.</summary>
            public DateTime? StartDate { get; set; }

            /// <summary>Inclusive upper bound applied to the document's date.</summary>
            public DateTime? EndDate { get; set; }

            public int C_Year_ID { get; set; }
            public string FiscalYear { get; set; }
            public int C_Calendar_ID { get; set; }
        }

        /// <summary>
        /// One discovered screen and everything the generated SQL needs to know about
        /// it. Server-side only - the client never sees a table name, a column name or
        /// a WhereClause it could send back.
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

            /// <summary>
            /// The column that made this table a source. Always present, and always
            /// both filtered on and displayed.
            /// </summary>
            public string DocStatusColumn { get; set; }

            /// <summary>List reference behind DocStatus, for the code -> name map.</summary>
            public int DocStatusReferenceId { get; set; }

            /* Optional columns; "" when the table does not have them. */
            public string DocumentNoColumn { get; set; }
            public string DateAcctColumn { get; set; }
            public string MovementDateColumn { get; set; }

            /// <summary>StatementDate probe - the bank statement's accounting date.</summary>
            public string StatementDateColumn { get; set; }
            public string BPartnerColumn { get; set; }

            /// <summary>
            /// Which column the document type is read from - already resolved by
            /// <see cref="ResolveDocTypeColumn"/>, so it is C_DocTypeTarget_ID on an
            /// order or an invoice and C_DocType_ID elsewhere. Everything downstream
            /// uses THIS and never either raw probe result.
            /// </summary>
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
        }

        /// <summary>
        /// One screen a discovered table appears on. Server-side only - the
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
        /// One screen's headline figures. A row is a table AND a window, because
        /// Purchase Order and Sales Order are two screens over one C_Order table, so
        /// that pair is what identifies the row and what the client sends back.
        /// </summary>
        public class SourceTotal
        {
            public int AD_Table_ID { get; set; }

            /// <summary>Physical table - diagnostic only; the client never sends it back.</summary>
            public string TableName { get; set; }

            /// <summary>The screen's name, already disambiguated.</summary>
            public string DisplayName { get; set; }

            /// <summary>Open documents of this screen in the period.</summary>
            public int RecordCount { get; set; }

            /// <summary>
            /// The date of the oldest one - what the card's "oldest 03 Apr" reads. A
            /// count says how much is outstanding; this says how badly.
            /// </summary>
            public DateTime? OldestDate { get; set; }

            /// <summary>Total in the tenant's base currency; 0 when HasValue is false.</summary>
            public decimal BaseValue { get; set; }

            /// <summary>
            /// false when this screen has no trusted amount strategy - the client shows
            /// a dash rather than a figure it cannot vouch for.
            /// </summary>
            public bool HasValue { get; set; }

            /// <summary>
            /// The screen. Half of the row's identity, and the window its records zoom
            /// into - which is the same thing, since the row IS that screen.
            /// </summary>
            public int AD_Window_ID { get; set; }
        }

        /// <summary>Everything the card shows for one period.</summary>
        public class PeriodData
        {
            public int C_Period_ID { get; set; }
            public string PeriodName { get; set; }

            /// <summary>Screens with at least one open document, biggest value first.</summary>
            public List<SourceTotal> Sources { get; set; }

            /// <summary>Documents across every screen - the footer's headline figure.</summary>
            public int TotalRecordCount { get; set; }

            public string BaseCurrencyIso { get; set; }
            public string BaseCurrencySymbol { get; set; }
            public int BaseCurrencyPrecision { get; set; }

            /// <summary>ERROR_* token when the period no longer qualifies.</summary>
            public string ErrorCode { get; set; }
        }

        /// <summary>Everything the bootstrap round trip returns.</summary>
        public class UnProcessedBootstrap
        {
            public List<PeriodItem> Periods { get; set; }
            public int C_Period_ID { get; set; }
            public string PeriodName { get; set; }
            public PeriodData Data { get; set; }
        }

        /// <summary>One open document in the detail list.</summary>
        public class UnProcessedRow
        {
            /// <summary>Key-column value - the Zoom's record id.</summary>
            public int Record_ID { get; set; }

            /// <summary>DocumentNo where the table has one, else the key as text.</summary>
            public string DocumentNo { get; set; }

            public DateTime? DateAcct { get; set; }

            /// <summary>
            /// Stored DocStatus code; the client resolves it against DocStatusNames
            /// and reads the pill's tone off the CODE, so the tone survives every
            /// language.
            /// </summary>
            public string DocStatus { get; set; }

            /// <summary>
            /// C_DocType name, translated. Read from the TARGET type on an order or
            /// an invoice, because the plain one is not reconciled until the document
            /// completes and nothing in this list has. Empty when the table carries no
            /// document-type column at all.
            /// </summary>
            public string DocumentType { get; set; }

            /// <summary>Who the document is with. Empty without a C_BPartner_ID column.</summary>
            public string BusinessPartnerName { get; set; }

            /* Document currency. */
            public string CurrencyIso { get; set; }
            public string CurrencySymbol { get; set; }
            public int CurrencyPrecision { get; set; }

            /// <summary>
            /// The document's own value in its own currency; 0 without a strategy. The
            /// only amount the record list carries - converted totals belong to the
            /// card, not to a list of individual records.
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

            public List<UnProcessedRow> Rows { get; set; }

            public int Total { get; set; }
            public int PageNo { get; set; }
            public int PageSize { get; set; }

            /// <summary>Key column name - the Zoom's primary column.</summary>
            public string KeyColumn { get; set; }

            /// <summary>
            /// Which date column the rows were bounded and sorted by - DateAcct, or
            /// MovementDate on a table that has no DateAcct. The client captions the
            /// date column from this, so a movement-dated screen is not mislabelled
            /// "Account Date".
            /// </summary>
            public string DateColumn { get; set; }

            /// <summary>
            /// The screen. Half of the row's identity, and the window the Zoom opens.
            /// </summary>
            public int AD_Window_ID { get; set; }

            /// <summary>
            /// false when the screen has no trusted amount strategy - the amount
            /// renders as a dash rather than a figure nobody can vouch for.
            /// </summary>
            public bool HasValue { get; set; }

            /* Which optional columns the list should show at all. A column with
               nothing behind it is DROPPED rather than printed blank: a column of
               empty cells reads as missing data instead of as "not applicable". */
            public bool HasDocType { get; set; }
            public bool HasBPartner { get; set; }
            public bool HasCurrency { get; set; }

            /// <summary>Stored DocStatus code -> translated display name.</summary>
            public Dictionary<string, string> DocStatusNames { get; set; }

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
            /// <summary>Declares a strategy.</summary>
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
