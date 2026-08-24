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
    ///               UNION CHECKS. Checks 01, 02, 04, 11 and 15 span several physical
    ///               tables. Each branch is built and secured on its OWN main alias and
    ///               only then combined with UNION ALL; the combined statement is never
    ///               re-secured, and the framework's count runner wraps it as a derived
    ///               table rather than applying MRole a second time.
    /// Chronological development:
    ///   VAI154      2026-08-24 Created
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

        /// <summary>
        /// The document registry. Server-controlled and never influenced by the browser:
        /// checks 01, 02 and 15 iterate THIS list, so a table can only be examined
        /// because it is named here.
        ///
        /// Two flags decide which checks see an entry, and they are independent:
        ///   ExpectsAccounting  the document is expected to reach the ledger, so check
        ///                      02 may ask whether it posted. An entry without it is
        ///                      visible to check 01 only.
        ///   IsInventory        the document moves stock, so check 15 examines it.
        ///
        /// C_Order is the one entry with NEITHER: an order is real work that a period
        /// close should surface while it is still open, but it creates no Fact_Acct row
        /// and carries no Posted column, so asking whether it posted would be a question
        /// with no answer. It therefore appears under check 01 and nowhere else.
        /// </summary>
        /// <returns>Registered documents (never null).</returns>
        private List<DocDef> PostingDocuments()
        {
            List<DocDef> docs = new List<DocDef>();

            docs.Add(NewDoc("C_Order", "C_Order_ID", "DateAcct", "GrandTotal", false, false));
            docs.Add(NewDoc("C_Invoice", "C_Invoice_ID", "DateAcct", "GrandTotal", true, false));
            docs.Add(NewDoc("C_Payment", "C_Payment_ID", "DateAcct", "PayAmt", true, false));

            /* Cash journal. Its amount is StatementDifference - the net movement of the
               journal - because a cash journal has no GrandTotal: its beginning and
               ending balances are positions, not a document value, and only the
               difference between them is what this journal did. DateAcct rather than
               StatementDate, even though MCash keeps the two equal, because every other
               entry here is bounded by its accounting date and the period filter must
               mean the same thing for all of them. */
            docs.Add(NewDoc("C_Cash", "C_Cash_ID", "DateAcct", "StatementDifference", true, false));

            docs.Add(NewDoc("GL_Journal", "GL_Journal_ID", "DateAcct", "ControlAmt", true, false));
            docs.Add(NewDoc("M_InOut", "M_InOut_ID", "DateAcct", "", true, true));
            docs.Add(NewDoc("M_Inventory", "M_Inventory_ID", "MovementDate", "", true, true));
            docs.Add(NewDoc("M_Movement", "M_Movement_ID", "MovementDate", "", true, true));

            /* Module documents - present only where the module is installed. */
            docs.Add(NewDoc("VAFAM_AssetDepreciation", "VAFAM_AssetDepreciation_ID", "DateAcct",
                "VAFAM_DepreciatedAmt", true, false));
            docs.Add(NewDoc("M_InventoryRevaluation", "M_InventoryRevaluation_ID", "DateAcct",
                "TotalDifference", true, false));

            return docs;
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

        /// <summary>Builds one posting-document registry entry.</summary>
        /// <param name="tableName">Physical table name.</param>
        /// <param name="keyColumn">Primary key column.</param>
        /// <param name="dateColumn">Effective/accounting date column.</param>
        /// <param name="amountColumn">Document amount column, or "" when it has none.</param>
        /// <param name="expectsAccounting">Whether the document is expected to post.</param>
        /// <param name="isInventory">Whether it is an inventory movement document.</param>
        /// <returns>Populated <see cref="DocDef"/>.</returns>
        private DocDef NewDoc(string tableName, string keyColumn, string dateColumn,
            string amountColumn, bool expectsAccounting, bool isInventory)
        {
            DocDef def = new DocDef();
            def.TableName = tableName;
            def.KeyColumn = keyColumn;
            def.DateColumn = dateColumn;
            def.AmountColumn = amountColumn;
            def.ExpectsAccounting = expectsAccounting;
            def.IsInventory = isInventory;
            return def;
        }

        /// <summary>
        /// The registry entries this installation can actually query: table present,
        /// key/date columns present, and the document columns the checks rely on
        /// present. Everything else is skipped silently - an uninstalled module is not
        /// an error.
        /// </summary>
        /// <param name="inventoryOnly">Restrict to inventory movement documents.</param>
        /// <param name="postingOnly">Restrict to documents expected to post.</param>
        /// <returns>Usable registry entries (never null).</returns>
        private List<DocDef> UsableDocuments(bool inventoryOnly, bool postingOnly)
        {
            List<DocDef> usable = new List<DocDef>();
            List<DocDef> all = PostingDocuments();

            for (int i = 0; i < all.Count; i++)
            {
                DocDef def = all[i];

                if (inventoryOnly && !def.IsInventory) { continue; }
                if (postingOnly && !def.ExpectsAccounting) { continue; }

                if (!TableExists(def.TableName)) { continue; }
                if (!ColumnExists(def.TableName, def.KeyColumn)) { continue; }
                if (!ColumnExists(def.TableName, def.DateColumn)) { continue; }
                if (!ColumnExists(def.TableName, "DocStatus")) { continue; }

                def.HasPosted = ColumnExists(def.TableName, "Posted");
                def.HasProcessed = ColumnExists(def.TableName, "Processed");
                def.HasDocumentNo = ColumnExists(def.TableName, "DocumentNo");
                def.HasBPartner = ColumnExists(def.TableName, "C_BPartner_ID");
                def.HasCurrency = ColumnExists(def.TableName, "C_Currency_ID");
                def.DocTypeKey = DocTypeKeyExpr("doc",
                    ColumnExists(def.TableName, "C_DocType_ID"),
                    ColumnExists(def.TableName, "C_DocTypeTarget_ID"));
                def.HasAmount = !string.IsNullOrEmpty(def.AmountColumn)
                    && ColumnExists(def.TableName, def.AmountColumn);
                def.AD_Table_ID = TableId(def.TableName);

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
                case "MPC_CLOSE_19": return Spec19(c);
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
            List<DocDef> docs = UsableDocuments(false, false);
            if (docs.Count == 0) { return null; }

            List<SqlParameter> parameters = Binds();
            StringBuilder sql = new StringBuilder();

            for (int i = 0; i < docs.Count; i++)
            {
                DocDef d = docs[i];
                string suffix = (i + 1).ToString(CultureInfo.InvariantCulture);

                string pending = d.HasProcessed
                    ? "(doc.DocStatus IN (" + DOCSTATUS_OpenList + ") OR COALESCE(doc.Processed,'N')='N')"
                    : "doc.DocStatus IN (" + DOCSTATUS_OpenList + ")";

                string where = "doc.IsActive='Y' AND doc.DocStatus NOT IN (" + DOCSTATUS_DeadList + ")"
                    + " AND " + pending
                    + " AND " + PeriodWhere(c, "doc", d.DateColumn, suffix, parameters);

                string branch = DocBranchSelect(d, suffix, parameters) + " WHERE " + where;

                if (i > 0) { sql.Append(" UNION ALL "); }
                sql.Append(SecureBranch(c, branch, "doc"));
            }

            return Spec(sql.ToString(), "", "Doc_Date DESC,Doc_Number DESC", parameters, DocColumns());
        }

        /// <summary>
        /// The normalised SELECT/FROM every posting-document branch shares, so the
        /// branches of a UNION really do line up column for column. Columns a given
        /// table does not carry are emitted as typed literals rather than omitted.
        /// </summary>
        /// <param name="d">Registry entry for this branch.</param>
        /// <param name="suffix">Unique bind suffix for this branch.</param>
        /// <param name="parameters">Bind list being built, in appearance order.</param>
        /// <returns>SELECT ... FROM fragment aliased `doc`.</returns>
        private string DocBranchSelect(DocDef d, string suffix, List<SqlParameter> parameters)
        {
            StringBuilder sql = new StringBuilder();

            sql.Append("SELECT ").Append(d.AD_Table_ID).Append(" AS ").Append(TECH_TABLE)
               .Append(",doc.").Append(d.KeyColumn).Append(" AS ").Append(TECH_RECORD)
               .Append(",0 AS ").Append(TECH_WINDOW)
               .Append(",").Append(d.HasDocumentNo ? "COALESCE(doc.DocumentNo,N'')" : "N''").Append(" AS Doc_Number")
               .Append(",doc.").Append(d.DateColumn).Append(" AS Doc_Date")
               .Append(",doc.DocStatus AS Doc_Status")
               .Append(",").Append(string.IsNullOrEmpty(d.DocTypeKey) ? "N''" : "COALESCE(dt.Name,N'')").Append(" AS Doc_Type")
               .Append(",COALESCE(org.Name,N'') AS Org_Name")
               .Append(",").Append(d.HasBPartner ? "COALESCE(bp.Name,N'')" : "N''").Append(" AS Partner_Name")
               .Append(",").Append(d.HasCurrency ? "COALESCE(cur.ISO_Code,N'')" : "N''").Append(" AS Currency_Iso")
               .Append(",").Append(d.HasAmount ? "COALESCE(doc." + d.AmountColumn + ",0)" : "0").Append(" AS Doc_Amount")
               .Append(" FROM ").Append(d.TableName).Append(" doc")
               .Append(" LEFT OUTER JOIN AD_Org org ON (org.AD_Org_ID=doc.AD_Org_ID)");

            if (!string.IsNullOrEmpty(d.DocTypeKey))
            {
                sql.Append(" LEFT OUTER JOIN C_DocType dt ON (dt.C_DocType_ID=").Append(d.DocTypeKey).Append(")");
            }
            if (d.HasBPartner)
            {
                sql.Append(" LEFT OUTER JOIN C_BPartner bp ON (bp.C_BPartner_ID=doc.C_BPartner_ID)");
            }
            if (d.HasCurrency)
            {
                sql.Append(" LEFT OUTER JOIN C_Currency cur ON (cur.C_Currency_ID=doc.C_Currency_ID)");
            }

            return sql.ToString();
        }

        /// <summary>The shared column set of the posting-document checks.</summary>
        /// <returns>Declared columns.</returns>
        private List<ColumnDef> DocColumns()
        {
            List<ColumnDef> columns = new List<ColumnDef>();
            columns.Add(Col("Screen", "VAS_195_Screen", "Screen", COLTYPE_SCREEN, 1.2m));
            columns.Add(Col("Doc_Number", "DocumentNo", "Document No", COLTYPE_DOC, 1.2m));
            columns.Add(Col("Doc_Date", "VAS_195_AccountDate", "Account Date", COLTYPE_DATE, 0.9m));
            columns.Add(Col("Doc_Type", "VAS_195_DocumentType", "Document Type", COLTYPE_TEXT, 1.1m));
            columns.Add(Col("Doc_Status", "VAS_195_DocStatus", "Status", COLTYPE_BADGE, 0.8m));
            columns.Add(Col("Org_Name", "VAS_195_Organization", "Organization", COLTYPE_TEXT, 1.0m));
            columns.Add(Col("Partner_Name", "VAS_195_BusinessPartner", "Business Partner", COLTYPE_TEXT, 1.3m));
            columns.Add(Col("Currency_Iso", "VAS_195_Currency", "Currency", COLTYPE_TEXT, 0.5m));
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
        /// One secured branch per posting table that both expects accounting AND carries
        /// a Posted column - a table without one cannot be judged unposted, and guessing
        /// would manufacture failures.
        /// </summary>
        /// <param name="c">Shared evaluation context.</param>
        /// <returns>Populated <see cref="DetailSpec"/>, or null when no table qualifies.</returns>
        private DetailSpec Spec02(CheckContext c)
        {
            List<DocDef> all = UsableDocuments(false, true);
            List<DocDef> docs = new List<DocDef>();
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].HasPosted) { docs.Add(all[i]); }
            }
            if (docs.Count == 0) { return null; }

            List<SqlParameter> parameters = Binds();
            StringBuilder sql = new StringBuilder();

            for (int i = 0; i < docs.Count; i++)
            {
                DocDef d = docs[i];
                string suffix = (i + 1).ToString(CultureInfo.InvariantCulture);

                string where = "doc.IsActive='Y' AND doc.DocStatus IN (" + DOCSTATUS_FinalList + ")"
                    + " AND COALESCE(doc.Posted,'N')<>'Y'"
                    + " AND " + PeriodWhere(c, "doc", d.DateColumn, suffix, parameters);

                string branch = DocBranchSelect(d, suffix, parameters) + " WHERE " + where;

                if (i > 0) { sql.Append(" UNION ALL "); }
                sql.Append(SecureBranch(c, branch, "doc"));
            }

            return Spec(sql.ToString(), "", "Doc_Date DESC,Doc_Number DESC", parameters, DocColumns());
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
        /// Completed payments in the period whose allocated total falls short of the
        /// payment by more than the currency tolerance.
        ///
        /// Allocation is summed from active lines of completed allocation headers, in
        /// the ALLOCATION's own currency conversion back to the payment currency where
        /// the two differ - comparing raw amounts across currencies would mark a fully
        /// allocated foreign payment as outstanding.
        /// </summary>
        /// <param name="c">Shared evaluation context.</param>
        /// <returns>Populated <see cref="DetailSpec"/>.</returns>
        private DetailSpec Spec03(CheckContext c)
        {
            List<SqlParameter> parameters = Binds();
            bool hasPrepayment = ColumnExists("C_Payment", "IsPrepayment");

            /* Target type first - see DocTypeKeyExpr. A payment carries both. */
            string docTypeKey = DocTypeKeyExpr("p",
                ColumnExists("C_Payment", "C_DocType_ID"),
                ColumnExists("C_Payment", "C_DocTypeTarget_ID"));

            /* Allocated, in the payment's currency. currencyConvert is a no-op when the
               two currencies already match, so the CASE keeps the common path cheap. */
            string allocated = @"COALESCE((SELECT SUM(CASE WHEN ah.C_Currency_ID=p.C_Currency_ID THEN al.Amount
                        ELSE COALESCE(currencyConvert(al.Amount,ah.C_Currency_ID,p.C_Currency_ID,ah.DateAcct,ah.C_ConversionType_ID,ah.AD_Client_ID,ah.AD_Org_ID),0) END)
                    FROM C_AllocationLine al
                    INNER JOIN C_AllocationHdr ah ON (ah.C_AllocationHdr_ID=al.C_AllocationHdr_ID)
                    WHERE al.C_Payment_ID=p.C_Payment_ID
                      AND al.IsActive='Y'
                      AND ah.IsActive='Y'
                      AND ah.DocStatus IN (" + DOCSTATUS_FinalList + ")),0)";

            string category = "CASE WHEN COALESCE(p.IsAllocated,'N')<>'Y' AND " + allocated + @"=0 THEN 'NOTALLOCATED'
                    WHEN " + allocated + "<ABS(COALESCE(p.PayAmt,0)) THEN 'PARTIAL' ELSE 'ONACCOUNT' END";

            string sql = @"
                SELECT " + TableId("C_Payment") + " AS " + TECH_TABLE + @",
                       p.C_Payment_ID AS " + TECH_RECORD + @",
                       0 AS " + TECH_WINDOW + @",
                       COALESCE(p.DocumentNo,N'') AS Doc_Number,
                       p.DateAcct AS Doc_Date,
                       " + (string.IsNullOrEmpty(docTypeKey) ? "N''" : "COALESCE(dt.Name,N'')") + @" AS Doc_Type,
                       COALESCE(bp.Name,N'') AS Partner_Name,
                       COALESCE(cur.ISO_Code,N'') AS Currency_Iso,
                       COALESCE(p.PayAmt,0) AS Pay_Amount,
                       " + allocated + @" AS Allocated_Amount,
                       (ABS(COALESCE(p.PayAmt,0))-" + allocated + @") AS Unallocated_Amount,
                       " + category + @" AS Alloc_Category,
                       " + (hasPrepayment ? "COALESCE(p.IsPrepayment,'N')" : "'N'") + @" AS Is_Prepayment
                FROM C_Payment p
                " + (string.IsNullOrEmpty(docTypeKey)
                        ? ""
                        : "LEFT OUTER JOIN C_DocType dt ON (dt.C_DocType_ID=" + docTypeKey + ")") + @"
                LEFT OUTER JOIN C_BPartner bp ON (bp.C_BPartner_ID=p.C_BPartner_ID)
                LEFT OUTER JOIN C_Currency cur ON (cur.C_Currency_ID=p.C_Currency_ID)
                WHERE p.IsActive='Y'
                  AND p.DocStatus IN (" + DOCSTATUS_FinalList + @")
                  AND COALESCE(p.IsReversal,'N')='N'
                  AND " + PeriodWhere(c, "p", "DateAcct", "P", parameters) + @"
                  AND (ABS(COALESCE(p.PayAmt,0))-" + allocated + ")>@Tolerance";

            parameters.Add(new SqlParameter("@Tolerance", c.Tolerance));

            List<ColumnDef> columns = new List<ColumnDef>();
            columns.Add(Col("Doc_Number", "VAS_195_PaymentNo", "Payment No", COLTYPE_DOC, 1.1m));
            columns.Add(Col("Doc_Date", "VAS_195_AccountDate", "Account Date", COLTYPE_DATE, 0.9m));
            columns.Add(Col("Doc_Type", "VAS_195_PaymentType", "Payment Type", COLTYPE_TEXT, 1.1m));
            columns.Add(Col("Partner_Name", "VAS_195_BusinessPartner", "Business Partner", COLTYPE_TEXT, 1.3m));
            columns.Add(Col("Currency_Iso", "VAS_195_Currency", "Currency", COLTYPE_TEXT, 0.5m));
            columns.Add(Col("Pay_Amount", "VAS_195_PaymentAmount", "Payment Amount", COLTYPE_DOCAMOUNT, 1.0m));
            columns.Add(Col("Allocated_Amount", "VAS_195_AllocatedAmount", "Allocated", COLTYPE_DOCAMOUNT, 1.0m));
            columns.Add(Col("Unallocated_Amount", "VAS_195_UnallocatedAmount", "Unallocated", COLTYPE_DOCAMOUNT, 1.0m));
            columns.Add(Col("Alloc_Category", "VAS_195_AllocCategory", "Category", COLTYPE_BADGE, 0.9m));

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
        /// Account-level summary of every configured suspense account: opening balance,
        /// period debit and credit, movement and closing balance.
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

            return AccountBalanceSpec(c, ids, "VAS_195_SuspenseAccount", "Suspense Account");
        }

        /// <summary>
        /// The shared account-balance statement used by both the suspense check and the
        /// clearing check: one row per natural account, with the balance split into what
        /// was carried in, what moved, and what remains.
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
            if (ColumnExists("C_AcctSchema_GL", "FRPT_RoundingOff_Acct")) { settings.Add("FRPT_RoundingOff_Acct"); }

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
        /// Active recurring setups whose next run fell due on or before period end and
        /// which have no run recorded inside the period.
        ///
        /// The due date comes from C_Recurring's own DateNextRun - the same field the
        /// recurring process advances - rather than from month arithmetic over
        /// FrequencyType. For a HISTORICAL period DateNextRun may already have moved
        /// past the period, which is exactly why the exception test is "no run landed in
        /// this period" rather than "DateNextRun is old".
        /// </summary>
        /// <param name="c">Shared evaluation context.</param>
        /// <returns>Populated <see cref="DetailSpec"/>, or null when the feature is absent.</returns>
        private DetailSpec Spec12(CheckContext c)
        {
            if (!TableExists("C_Recurring")) { return null; }

            List<SqlParameter> parameters = Binds();
            bool hasRuns = TableExists("C_Recurring_Run");
            bool hasRunsRemaining = ColumnExists("C_Recurring", "RunsRemaining");
            bool hasRecurringType = ColumnExists("C_Recurring", "RecurringType");
            bool hasDateLastRun = ColumnExists("C_Recurring", "DateLastRun");

            parameters.Add(new SqlParameter("@PeriodEndR", c.PeriodEndExclusive));

            string noRun = hasRuns
                ? @" AND NOT EXISTS(SELECT 1 FROM C_Recurring_Run rr
                        WHERE rr.C_Recurring_ID=r.C_Recurring_ID
                          AND rr.IsActive='Y'
                          AND rr.DateDoc>=@PeriodStartR AND rr.DateDoc<@PeriodEndR2)"
                : "";

            if (hasRuns)
            {
                parameters.Add(new SqlParameter("@PeriodStartR", c.PeriodStart));
                parameters.Add(new SqlParameter("@PeriodEndR2", c.PeriodEndExclusive));
            }

            string sql = @"
                SELECT " + TableId("C_Recurring") + " AS " + TECH_TABLE + @",
                       r.C_Recurring_ID AS " + TECH_RECORD + @",
                       0 AS " + TECH_WINDOW + @",
                       COALESCE(r.Name,N'') AS Recurring_Name,
                       " + (hasRecurringType ? "COALESCE(r.RecurringType,N'')" : "N''") + @" AS Recurring_Type,
                       COALESCE(r.FrequencyType,N'') AS Frequency_Type,
                       COALESCE(r.Frequency,0) AS Frequency,
                       " + (hasDateLastRun ? "r.DateLastRun" : "CAST(NULL AS DATE)") + @" AS Last_Run,
                       r.DateNextRun AS Next_Run,
                       " + (hasRunsRemaining ? "COALESCE(r.RunsRemaining,0)" : "0") + @" AS Runs_Remaining
                FROM C_Recurring r
                WHERE r.IsActive='Y'
                  AND r.DateNextRun IS NOT NULL
                  AND r.DateNextRun<@PeriodEndR" + noRun;

            List<ColumnDef> columns = new List<ColumnDef>();
            columns.Add(Col("Recurring_Name", "VAS_195_RecurringName", "Recurring", COLTYPE_DOC, 1.6m));
            columns.Add(Col("Recurring_Type", "VAS_195_RecurringType", "Type", COLTYPE_BADGE, 0.9m));
            columns.Add(Col("Frequency_Type", "VAS_195_Frequency", "Frequency", COLTYPE_TEXT, 0.9m));
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
            List<DocDef> docs = UsableDocuments(true, false);
            if (docs.Count == 0) { return null; }

            List<SqlParameter> parameters = Binds();
            StringBuilder sql = new StringBuilder();

            for (int i = 0; i < docs.Count; i++)
            {
                DocDef d = docs[i];
                string suffix = "V" + (i + 1).ToString(CultureInfo.InvariantCulture);

                string pending = d.HasProcessed
                    ? "(doc.DocStatus IN (" + DOCSTATUS_OpenList + ") OR COALESCE(doc.Processed,'N')='N')"
                    : "doc.DocStatus IN (" + DOCSTATUS_OpenList + ")";

                string where = "doc.IsActive='Y' AND doc.DocStatus NOT IN (" + DOCSTATUS_DeadList + ")"
                    + " AND " + pending
                    + " AND " + PeriodWhere(c, "doc", d.DateColumn, suffix, parameters);

                string branch = DocBranchSelect(d, suffix, parameters) + " WHERE " + where;

                if (i > 0) { sql.Append(" UNION ALL "); }
                sql.Append(SecureBranch(c, branch, "doc"));
            }

            return Spec(sql.ToString(), "", "Doc_Date DESC,Doc_Number DESC", parameters, DocColumns());
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
        // 19  Foreign currency revaluation not run                     BLOCKER
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Whether period-end foreign-currency revaluation has been run and posted.
        ///
        /// Applicability first: the check only bites when an account is actually flagged
        /// for revaluation. Failing a tenant that has no foreign-currency exposure would
        /// block a close for a process it never needed to run.
        /// </summary>
        /// <param name="c">Shared evaluation context.</param>
        /// <param name="def">Registry entry.</param>
        /// <returns>Populated <see cref="CheckResult"/>.</returns>
        private CheckResult Eval19(CheckContext c, CheckDef def)
        {
            if (!ColumnExists("C_ElementValue", "FRPT_IsForexRevaluation"))
            {
                return NotApplicable(def, "VAS_195_Na19", "Foreign currency revaluation is not configured");
            }

            int enabled = RevaluationAccountCount(c);
            if (enabled == 0)
            {
                return NotApplicable(def, "VAS_195_Na19b", "No revaluation-enabled account carries a balance");
            }

            /* Evidence: a completed, posted journal that the revaluation stamped for this
               period. Staging output alone is not evidence - it says the calculation ran,
               not that the ledger received it. */
            if (RevaluationJournalPosted(c))
            {
                CheckResult passed = NewResult(def);
                passed.Status = STATUS_PASS;
                passed.SummaryKey = "VAS_195_Clr19";
                passed.SummaryText = "Foreign currency revaluation is completed and posted";
                passed.DetailAvailable = true;
                passed.DocumentCount = enabled;
                return passed;
            }

            CheckResult result = Counted(def, enabled,
                "VAS_195_Sum19", "revaluation-enabled accounts have no posted revaluation for this period",
                "VAS_195_Clr19", "Foreign currency revaluation is completed and posted");
            result.DetailAvailable = true;
            return result;
        }

        /// <summary>The revaluation-enabled accounts and their period-end balances.</summary>
        /// <param name="c">Shared evaluation context.</param>
        /// <returns>Populated <see cref="DetailSpec"/>, or null when not configured.</returns>
        private DetailSpec Spec19(CheckContext c)
        {
            if (!ColumnExists("C_ElementValue", "FRPT_IsForexRevaluation")) { return null; }

            List<SqlParameter> parameters = Binds();
            parameters.Add(new SqlParameter("@AD_Client_ID", c.Ctx.GetAD_Client_ID()));
            parameters.Add(new SqlParameter("@C_AcctSchema_ID", c.Acct.C_AcctSchema_ID));
            parameters.Add(new SqlParameter("@PostingType", POSTINGTYPE_Actual));
            parameters.Add(new SqlParameter("@PeriodEndF", c.PeriodEndExclusive));

            string sql = @"
                SELECT 0 AS " + TECH_TABLE + @",
                       fa.Account_ID AS " + TECH_RECORD + @",
                       0 AS " + TECH_WINDOW + @",
                       COALESCE(ev.Value,N'') AS Account_Value,
                       COALESCE(ev.Name,N'') AS Account_Name,
                       COALESCE(cur.ISO_Code,N'') AS Currency_Iso,
                       SUM(COALESCE(fa.AmtAcctDr,0)-COALESCE(fa.AmtAcctCr,0)) AS Closing_Balance
                FROM Fact_Acct fa
                INNER JOIN C_ElementValue ev ON (ev.C_ElementValue_ID=fa.Account_ID AND ev.IsActive='Y' AND COALESCE(ev.FRPT_IsForexRevaluation,'N')='Y')
                LEFT OUTER JOIN C_Currency cur ON (cur.C_Currency_ID=fa.C_Currency_ID)
                WHERE fa.AD_Client_ID=@AD_Client_ID
                  AND fa.C_AcctSchema_ID=@C_AcctSchema_ID
                  AND fa.PostingType=@PostingType
                  AND fa.IsActive='Y'
                  AND fa.DateAcct<@PeriodEndF";

            DetailSpec spec = new DetailSpec();
            spec.Sql = sql;
            spec.MainAlias = "fa";
            spec.GroupBy = "fa.Account_ID,COALESCE(ev.Value,N''),COALESCE(ev.Name,N''),COALESCE(cur.ISO_Code,N'')";
            spec.OrderBy = "Account_Value";
            spec.Params = parameters;

            List<ColumnDef> columns = new List<ColumnDef>();
            columns.Add(Col("Account_Value", "VAS_195_Account", "Account", COLTYPE_TEXT, 0.9m));
            columns.Add(Col("Account_Name", "VAS_195_AccountName", "Account Name", COLTYPE_TEXT, 2.0m));
            columns.Add(Col("Currency_Iso", "VAS_195_Currency", "Currency", COLTYPE_TEXT, 0.6m));
            columns.Add(Col("Closing_Balance", "VAS_195_ClosingBalance", "Closing Balance", COLTYPE_AMOUNT, 1.2m));
            spec.Columns = columns;

            return spec;
        }

        /// <summary>How many revaluation-enabled accounts carry a period-end balance.</summary>
        /// <param name="c">Shared evaluation context.</param>
        /// <returns>Account count.</returns>
        private int RevaluationAccountCount(CheckContext c)
        {
            DetailSpec spec = Spec19(c);
            return spec == null ? 0 : CountOf(c.Ctx, spec);
        }

        /// <summary>
        /// Whether a completed, posted revaluation journal exists for the period.
        /// Falls back to the journal's accounting date when the FRPT stamp column is not
        /// present in this installation.
        /// </summary>
        /// <param name="c">Shared evaluation context.</param>
        /// <returns>true when posted evidence exists.</returns>
        private bool RevaluationJournalPosted(CheckContext c)
        {
            if (!TableExists("GL_Journal")) { return false; }

            bool hasStamp = ColumnExists("GL_Journal", "FRPT_RevaluationDate");
            string dateColumn = hasStamp ? "FRPT_RevaluationDate" : "DateAcct";

            List<SqlParameter> parameters = Binds();

            string sql = @"
                SELECT COUNT(1) AS Journal_Count
                FROM GL_Journal j
                WHERE j.IsActive='Y'
                  AND j.DocStatus IN (" + DOCSTATUS_FinalList + @")
                  AND COALESCE(j.Posted,'N')='Y'"
                  + (hasStamp ? " AND j.FRPT_RevaluationDate IS NOT NULL" : "")
                  + " AND " + PeriodWhere(c, "j", dateColumn, "J", parameters);

            sql = MRole.GetDefault(c.Ctx).AddAccessSQL(sql, "j", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            DataSet ds = DB.ExecuteDataset(sql, parameters.ToArray(), null);
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) { return false; }

            return Util.GetValueOfInt(ds.Tables[0].Rows[0]["Journal_Count"]) > 0;
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
            string language = c.Ctx.GetAD_Language();
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
            public bool ExpectsAccounting { get; set; }
            public bool IsInventory { get; set; }

            public int AD_Table_ID { get; set; }
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
