/// <summary>
/// Module Name : VASLogic
/// Purpose     : Inventory Count Overview tab panel data (read side). Returns
///               header identity, warehouse, KPI aggregates (counted value /
///               net variance qty / variance-line count / total lines), the
///               matched / short / excess roll-ups and the count lines for a
///               selected physical inventory count (M_Inventory where
///               IsInternalUse is not 'Y'). Book-vs-counted variance is derived
///               from the line quantities.
/// Chronological development:
///   VAI163   2026-07-06  Created. Optional module columns (CurrentCostPrice,
///                        VA024_UnitPrice) are guarded through AD_Column so the
///                        panel works whether or not those modules are installed.
///   VAI163   2026-08-12  - Variance is COUNTED minus SYSTEM, always.
///                          M_InventoryLine.DifferenceQty is no longer preferred:
///                          the platform writes it the other way round
///                          (DBFunctionCollection derives QtyCount = QtyBook -
///                          DifferenceQty, so it is BOOK minus COUNT), which
///                          inverted every line that carried one — a short count
///                          was tagged Excess and signed +, and the Short / Excess
///                          roll-ups were swapped with it.
///                        - A line is valued by the DIRECTION of its variance
///                          (BuildRateExpr): PriceCost where counted exceeds
///                          system — the price the found stock comes in at,
///                          falling back to CurrentCostPrice when it is zero —
///                          and CurrentCostPrice otherwise. PriceCost is
///                          dictionary-guarded like the others.
///                        - Lines carry their Attribute Set Instance, joined only
///                          for a REAL instance so a line with no attributes
///                          cannot pick up the zero-record's description.
///                        - Added LoadProjectRef (M_Inventory.C_Project_ID,
///                          guarded) for the panel's Related Documents section,
///                          LoadNotes (the header description plus each line's,
///                          labelled with its line no and product) and
///                          LoadActivity (created / updated / completed / posted
///                          milestones plus chat notes, newest-first, capped at
///                          200 — the panel pages at 15). Each source is
///                          separately guarded so one DB problem degrades to a
///                          partial trail.
///                        - Added GetWindowId (ported from VAS_092) for the
///                          panel's record-open path.
///   VAI163   2026-08-12  Activity gains the e-mails sent against the count
///                        (LoadEmailActivity, MailAttachment1 by AD_Table_ID +
///                        Record_ID): recipients, subject, body, when and who sent
///                        it. The body travels with the row so the panel can reveal
///                        it on click without a second round trip, and an HTML mail
///                        is flattened to readable text first (MailBodyToText) — no
///                        markup is ever handed to the panel. Ported from VAS_102,
///                        which reads the same table.
///   VAI163   2026-08-12  - Added VarianceValue: what the count's variance is
///                          WORTH — Σ (variance qty x the line's rate), using the
///                          same direction-aware rate the lines are valued at
///                          (BuildRateExpr), so a short line is valued at
///                          CurrentCostPrice and an over line at PriceCost falling
///                          back to CurrentCostPrice. Signed like the quantity it
///                          derives from: negative is stock the count could not
///                          find.
///                        - Added DocTypeName (M_Inventory.C_DocType_ID ->
///                          C_DocType), dictionary-guarded, for the header.
///                        - Activity reports WHICH FIELDS changed and when
///                          (LoadFieldChangeActivity, AD_ChangeLog): one row per
///                          changed column carrying its label, its old value and
///                          its new one, for the count header AND its lines — a
///                          count's edits are its counted quantities, and those
///                          live on the lines. It replaces the single "updated"
///                          milestone derived from M_Inventory.Updated, which could
///                          only ever report the LAST save and never said what it
///                          touched.
///   VAI163   2026-08-14  - Added the VA075 work order origin
///                          (LoadVA075WorkOrder): the maintenance work order the
///                          count's lines were raised against
///                          (M_InventoryLine.VA075_WorkOrder_ID, falling back to
///                          the work order behind VA075_WorkOrderComponent_ID for
///                          rows saved before that column was stamped). The panel
///                          lists it in Related Documents and opens the work order
///                          screen from it; a count carrying one used to name no
///                          source at all. Ported from VAS_102, including its
///                          lesson: the statements are ATTEMPTED rather than gated
///                          on ColumnExists, because VA075 is not part of this
///                          solution and the dictionary guard answers "absent" for
///                          a table it cannot find. A once-per-process flag keeps
///                          an install genuinely without VA075 to one log line.
///                        - Lines carry the PRODUCT'S BASE UOM (BaseUOMName /
///                          BaseUOMPrecision, M_Product.C_UOM_ID). On Hand
///                          (M_InventoryLine.QtyBook) is written straight from
///                          M_Storage.QtyOnHand, so it is always in that unit —
///                          but the table labelled it with the LINE's C_UOM_ID,
///                          which need not be the same one. The panel now names
///                          the base unit beside the figure.
///                        - Added GetWindowIdByTable (ported from VAS_102): the
///                          record-open path's last resort, for a table whose
///                          screen cannot be named on the client. The work order
///                          needs it — VA075 ships its own window and the
///                          browser-side zoom lookup only knows tables the client
///                          has cached.
///   VAI163   2026-08-14  Every AD_Table lookup in the activity loaders matches the
///                        name with UPPER (and the chat note's scalar sub-select
///                        became an IN). An equality on the stored spelling was the
///                        single point at which the field-level trail could return
///                        nothing while AD_ChangeLog was full of good rows, and the
///                        chat sub-select would RAISE rather than answer where the
///                        dictionary holds more than one row for the name — both
///                        failing silently, which reads exactly like the loader was
///                        never written. Same treatment LoadEmailActivity already
///                        carried for this table in VAS_102.
///   VAI163   2026-08-14  Activity follows VAS_092. Added LoadCountWorkflowActivity
///                        (+ WorkflowActivityType): the count's document lifecycle,
///                        one row per completed workflow node — prepare, complete,
///                        re-activate, void, close, approve / reject — carrying the
///                        node's own name, who ran it and when. The single derived
///                        "completed" milestone could not report a re-activation at
///                        all and showed only the LAST completion, so it now stands
///                        in ONLY where the workflow named nothing
///                        (LoadCountMilestones' addCompletion). Field-level edits
///                        emit Type "updated" rather than "changed", the type
///                        VAS_092 uses, so both panels tag and headline the row the
///                        same way; ChangeScope and the old / new values ride along
///                        as extra sub-lines the client drops when empty.
///   VAI163   2026-08-21  Activity: an appointment or task now carries the
///                        e-mails sent against IT - MailAttachment1 keyed on
///                        AppointmentsInfo rather than on this panel's own
///                        table - with the recipient (MailAddress), subject
///                        (Title), when (Created) and who sent it (CreatedBy).
///                        The body (TextMsg, flattened) travels with the row so
///                        the panel reveals it on click. Read in one query for
///                        the whole feed through VAS_ActivitySourcesModel.
/// </summary>

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;

namespace VASLogic.Models
{
    public class VAS_101_OverviewInventoryCountModel
    {
        private static readonly VLogger _log =
            VLogger.GetVLogger(typeof(VAS_101_OverviewInventoryCountModel).FullName);

        /// <summary>
        /// Returns the full overview payload for the selected inventory count.
        /// MRole access filtering is applied ONLY on the main physical table
        /// (M_Inventory alias "inv"); the child line query inherits the parent's
        /// authorization and is not separately role-filtered.
        /// </summary>
        /// <param name="ctx">User context (client/org/role).</param>
        /// <param name="M_Inventory_ID">Selected inventory count id.</param>
        /// <returns>Populated <see cref="InventoryCountOverviewData"/>; an empty
        /// instance when the id is invalid or no accessible row is found.</returns>
        public InventoryCountOverviewData GetInventoryCountOverview(Ctx ctx, int M_Inventory_ID)
        {
            InventoryCountOverviewData result = new InventoryCountOverviewData();
            if (M_Inventory_ID <= 0) return result;

            // Optional module columns — resolved once so the SQL below only
            // references columns that actually exist in this schema.
            bool hasCurrentCost = ColumnExists("M_InventoryLine", "CurrentCostPrice");
            bool hasUnitPrice   = ColumnExists("M_InventoryLine", "VA024_UnitPrice");
            bool hasPriceCost   = ColumnExists("M_InventoryLine", "PriceCost");

            // The rate a line is valued at depends on WHICH WAY it varies:
            //
            //   system > counted (stock is going out) -> CurrentCostPrice, what the
            //       stock on hand is already carried at;
            //   counted > system (stock is coming in) -> PriceCost, what the found
            //       stock is being brought in at, falling back to CurrentCostPrice
            //       when PriceCost is zero or the column is absent.
            //
            // A matched line varies by nothing, so either rate values it the same.
            string rateExpr = BuildRateExpr(hasCurrentCost, hasUnitPrice, hasPriceCost);

            // Variance per line = COUNTED minus SYSTEM, so a count that came up
            // short of the book reads negative and one that came up over reads
            // positive — which is what the panel's Short / Excess tags and their
            // ( - ) / ( + ) signs mean.
            //
            // M_InventoryLine.DifferenceQty is deliberately NOT used: the platform
            // writes it the other way round (DBFunctionCollection derives
            // QtyCount = QtyBook - DifferenceQty, so DifferenceQty is BOOK minus
            // COUNT). Preferring it inverted every line that carried one — a short
            // count was tagged Excess and signed +, and the Short / Excess counts
            // above the table were swapped with it. Deriving the figure from the
            // two quantities the panel puts either side of it also means the column
            // can never disagree with them.
            const string varExpr =
                "(COALESCE(l.QtyCount, 0) - COALESCE(l.QtyBook, 0))";

            // The document type is what the count screen names the document by, so
            // the panel shows it. C_DocType_ID is dictionary-guarded: it is not on
            // every schema's M_Inventory, and its absence must not cost the whole
            // query its rows.
            bool hasDocType = ColumnExists("M_Inventory", "C_DocType_ID");
            string docTypeExpr = hasDocType ? "dt.Name" : "CAST(NULL AS VARCHAR(255))";
            string docTypeJoin = hasDocType
                ? "LEFT OUTER JOIN C_DocType dt ON (dt.C_DocType_ID = inv.C_DocType_ID)" : "";

            string sql = @"SELECT
                              inv.M_Inventory_ID,
                              inv.DocumentNo,
                              inv.DocStatus,
                              inv.Processed,
                              inv.Posted,
                              inv.MovementDate,
                              inv.Description,
                              wh.Name          AS WarehouseName,
                              creator.Name     AS CountedBy,
                              " + docTypeExpr + @" AS DocTypeName,
                              (SELECT COUNT(*)
                                 FROM M_InventoryLine l
                                WHERE l.M_Inventory_ID = inv.M_Inventory_ID
                                  AND l.IsActive       = 'Y')                   AS LineCount,
                              (SELECT NVL(SUM(NVL(l.QtyCount, 0) * " + rateExpr + @"), 0)
                                 FROM M_InventoryLine l
                                WHERE l.M_Inventory_ID = inv.M_Inventory_ID
                                  AND l.IsActive       = 'Y')                   AS TotalValue,
                              (SELECT NVL(SUM(" + varExpr + @"), 0)
                                 FROM M_InventoryLine l
                                WHERE l.M_Inventory_ID = inv.M_Inventory_ID
                                  AND l.IsActive       = 'Y')                   AS NetVarianceQty,
                              -- What that variance is WORTH: each line's variance
                              -- at the line's own rate, which is chosen by the
                              -- DIRECTION it varies in (see BuildRateExpr). Signed
                              -- like the quantity, so stock the count could not
                              -- find reads negative.
                              (SELECT NVL(SUM(" + varExpr + @" * " + rateExpr + @"), 0)
                                 FROM M_InventoryLine l
                                WHERE l.M_Inventory_ID = inv.M_Inventory_ID
                                  AND l.IsActive       = 'Y')                   AS VarianceValue,
                              (SELECT NVL(SUM(CASE WHEN " + varExpr + @" = 0 THEN 1 ELSE 0 END), 0)
                                 FROM M_InventoryLine l
                                WHERE l.M_Inventory_ID = inv.M_Inventory_ID
                                  AND l.IsActive       = 'Y')                   AS MatchedCount,
                              (SELECT NVL(SUM(CASE WHEN " + varExpr + @" < 0 THEN 1 ELSE 0 END), 0)
                                 FROM M_InventoryLine l
                                WHERE l.M_Inventory_ID = inv.M_Inventory_ID
                                  AND l.IsActive       = 'Y')                   AS ShortCount,
                              (SELECT NVL(SUM(CASE WHEN " + varExpr + @" > 0 THEN 1 ELSE 0 END), 0)
                                 FROM M_InventoryLine l
                                WHERE l.M_Inventory_ID = inv.M_Inventory_ID
                                  AND l.IsActive       = 'Y')                   AS ExcessCount
                            FROM M_Inventory inv
                            LEFT OUTER JOIN M_Warehouse wh   ON (inv.M_Warehouse_ID = wh.M_Warehouse_ID)
                            LEFT OUTER JOIN AD_User creator  ON (inv.CreatedBy      = creator.AD_User_ID)
                            " + docTypeJoin + @"
                            WHERE inv.M_Inventory_ID = @M_Inventory_ID
                              AND inv.IsActive       = 'Y'
                              AND COALESCE(inv.IsInternalUse, 'N') <> 'Y'";

            // MRole access only on the primary physical table the user is fetching.
            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql, "inv", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            SqlParameter[] param = new SqlParameter[]
            {
                new SqlParameter("@M_Inventory_ID", M_Inventory_ID)
            };

            DataSet ds = DB.ExecuteDataset(sql, param, null);
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
                return result;

            DataRow r = ds.Tables[0].Rows[0];

            // ----- Header / identity -----
            result.M_Inventory_ID = Util.GetValueOfInt(r["M_Inventory_ID"]);
            result.DocumentNo     = Util.GetValueOfString(r["DocumentNo"]);
            result.StatusCode     = Util.GetValueOfString(r["DocStatus"]);
            result.Processed      = Util.GetValueOfString(r["Processed"]) == "Y";
            result.Posted         = Util.GetValueOfString(r["Posted"]) == "Y";
            result.CountDate      = Util.GetValueOfDateTime(r["MovementDate"]);
            result.Description    = Util.GetValueOfString(r["Description"]);
            result.WarehouseName  = Util.GetValueOfString(r["WarehouseName"]);
            result.CountedBy      = Util.GetValueOfString(r["CountedBy"]);
            result.DocTypeName    = Util.GetValueOfString(r["DocTypeName"]);

            // ----- KPI aggregates -----
            result.LineCount      = Util.GetValueOfInt(r["LineCount"]);
            result.TotalValue     = Util.GetValueOfDecimal(r["TotalValue"]);
            result.NetVarianceQty = Util.GetValueOfDecimal(r["NetVarianceQty"]);
            result.VarianceValue  = Util.GetValueOfDecimal(r["VarianceValue"]);
            result.MatchedCount   = Util.GetValueOfInt(r["MatchedCount"]);
            result.ShortCount     = Util.GetValueOfInt(r["ShortCount"]);
            result.ExcessCount    = Util.GetValueOfInt(r["ExcessCount"]);
            result.VarianceLineCount = result.ShortCount + result.ExcessCount;

            // Physical count value is expressed in the accounting currency; the
            // panel renders INR (₹) with standard 2-dp precision.
            result.StdPrecision = 2;

            // ----- Count lines -----
            result.Lines = LoadLines(M_Inventory_ID, rateExpr, varExpr);

            // ----- Related documents, notes and the audit trail -----
            LoadProjectRef(M_Inventory_ID, result);
            // The maintenance work order the count's lines were raised against.
            // Silent on an install without VA075 (see LoadVA075WorkOrder).
            LoadVA075WorkOrder(M_Inventory_ID, result);
            result.Notes    = LoadNotes(M_Inventory_ID, result.Description);
            result.Activity = LoadActivity(M_Inventory_ID, result.StatusCode);

            return result;
        }

        /// <summary>
        /// The project the count was raised for (M_Inventory.C_Project_ID), for the
        /// panel's Related Documents section. C_Project_ID is not on every schema's
        /// M_Inventory, so the read is dictionary-guarded: without it the section
        /// simply has nothing to list.
        /// </summary>
        /// <param name="M_Inventory_ID">Selected inventory count id.</param>
        /// <param name="d">Overview payload being populated.</param>
        private void LoadProjectRef(int M_Inventory_ID, InventoryCountOverviewData d)
        {
            if (!ColumnExists("M_Inventory", "C_Project_ID")) return;

            try
            {
                string sql = @"SELECT p.C_Project_ID, p.Value AS ProjectNo, p.Name AS ProjectName
                                 FROM M_Inventory inv
                                INNER JOIN C_Project p ON (p.C_Project_ID = inv.C_Project_ID)
                                WHERE inv.M_Inventory_ID = @M_Inventory_ID";
                DataSet ds = DB.ExecuteDataset(sql, InventoryParam(M_Inventory_ID), null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return;

                DataRow r = ds.Tables[0].Rows[0];
                d.C_Project_ID = Util.GetValueOfInt(r["C_Project_ID"]);
                d.ProjectNo    = Util.GetValueOfString(r["ProjectNo"]);
                d.ProjectName  = Util.GetValueOfString(r["ProjectName"]);
            }
            catch (Exception ex)
            {
                _log.Severe("LoadProjectRef (M_Inventory_ID=" + M_Inventory_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// Remembers whether each VA075 lookup is usable against this schema, so a
        /// database without the module reports it once per process rather than on
        /// every count the panel opens.
        /// Null = not tried, false = the statement failed, true = it ran.
        /// </summary>
        private static bool? _va075DirectUsable;
        private static bool? _va075ComponentUsable;

        /// <summary>
        /// Fills VA075_WorkOrder_ID / WorkOrderNo / WorkOrderRef from the VA075
        /// maintenance work order the count's lines were raised against, for the
        /// panel's Related Documents section.
        ///
        /// The link is M_InventoryLine.VA075_WorkOrder_ID. Rows saved before that
        /// column was stamped carry only VA075_WorkOrderComponent_ID, so the
        /// component's own work order is accepted as a fallback — that is what
        /// keeps existing documents from naming no source at all.
        ///
        /// The statements are ATTEMPTED rather than gated on ColumnExists. VA075 is
        /// NOT part of this solution, so its AD_Table / AD_Column rows are whatever
        /// the module's own install left behind — and the dictionary guard answers
        /// "absent" for a table it cannot find, or raises (which the catch turns
        /// into "absent") when AD_Table holds more than one row for the name. Either
        /// way the panel reported no work order however good the id on the line was.
        /// Ported from VAS_102, where exactly that guard was the fault.
        /// </summary>
        /// <param name="M_Inventory_ID">Selected inventory count id.</param>
        /// <param name="result">Overview payload being populated.</param>
        private void LoadVA075WorkOrder(int M_Inventory_ID, InventoryCountOverviewData result)
        {
            // "Work order reference" is named differently across VA075 revisions;
            // take the first one this schema actually has. A dictionary that names
            // none simply yields no reference caption — the row itself does not
            // depend on it.
            string refCol = FirstExistingColumn("VA075_WorkOrder", new string[]
            {
                "VA075_ReferenceNo", "Reference", "POReference", "Description", "Name"
            });
            string refExpr = string.IsNullOrEmpty(refCol)
                ? "CAST(NULL AS VARCHAR(255))" : "wo." + refCol;

            // The work order stamped straight onto the count line — the link the
            // panel is really about.
            if (_va075DirectUsable != false)
            {
                string inner = @"SELECT l.VA075_WorkOrder_ID AS WO_ID
                                   FROM M_InventoryLine l
                                  WHERE l.M_Inventory_ID = " + M_Inventory_ID + @"
                                    AND l.IsActive       = 'Y'
                                    AND COALESCE(l.VA075_WorkOrder_ID, 0) > 0";
                if (RunWorkOrderLookup(inner, refExpr, result, ref _va075DirectUsable,
                                       "direct", M_Inventory_ID))
                {
                    return;
                }
            }

            // Older rows carry only the spare-part component; its own work order
            // stands in.
            if (_va075ComponentUsable != false)
            {
                string inner = @"SELECT c.VA075_WorkOrder_ID AS WO_ID
                                   FROM M_InventoryLine l
                                  INNER JOIN VA075_WorkOrderComponent c
                                          ON (c.VA075_WorkOrderComponent_ID = l.VA075_WorkOrderComponent_ID)
                                  WHERE l.M_Inventory_ID = " + M_Inventory_ID + @"
                                    AND l.IsActive       = 'Y'
                                    AND COALESCE(l.VA075_WorkOrderComponent_ID, 0) > 0";
                RunWorkOrderLookup(inner, refExpr, result, ref _va075ComponentUsable,
                                   "component", M_Inventory_ID);
            }
        }

        /// <summary>
        /// Runs one VA075 work-order lookup and fills the payload from it.
        ///
        /// The count id is inlined rather than bound: the caller's sub-select and
        /// this statement would otherwise need the same bind name twice, which
        /// Oracle's positional binding does not allow — the second occurrence
        /// becomes an unfilled placeholder. It is an int, so nothing can be
        /// injected.
        /// </summary>
        /// <returns>True when a work order was found and the payload filled.</returns>
        private bool RunWorkOrderLookup(string innerSql, string refExpr,
                                        InventoryCountOverviewData result,
                                        ref bool? usable, string which, int M_Inventory_ID)
        {
            try
            {
                string sql = @"SELECT wo.VA075_WorkOrder_ID,
                                      wo.DocumentNo,
                                      " + refExpr + @" AS WorkOrderRef
                                 FROM VA075_WorkOrder wo
                                WHERE wo.VA075_WorkOrder_ID IN (" + innerSql + @")
                                ORDER BY wo.DocumentNo";
                DataSet ds = DB.ExecuteDataset(sql, null, null);
                usable = true;
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
                    return false;

                DataRow r = ds.Tables[0].Rows[0];
                result.VA075_WorkOrder_ID = Util.GetValueOfInt(r["VA075_WorkOrder_ID"]);
                result.WorkOrderNo        = Util.GetValueOfString(r["DocumentNo"]);
                result.WorkOrderRef       = Util.GetValueOfString(r["WorkOrderRef"]);
                result.WorkOrderCount     = ds.Tables[0].Rows.Count;
                return true;
            }
            catch (Exception ex)
            {
                // Almost certainly "no such table / column" on an install without
                // VA075. Recorded so the next count skips the attempt.
                usable = false;
                _log.Severe("LoadVA075WorkOrder/" + which +
                            " (M_Inventory_ID=" + M_Inventory_ID + "): " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Returns the first of the candidate columns that exists on the table, or
        /// an empty string when the table has none of them.
        /// </summary>
        private string FirstExistingColumn(string tableName, string[] candidates)
        {
            for (int i = 0; i < candidates.Length; i++)
            {
                if (ColumnExists(tableName, candidates[i])) return candidates[i];
            }
            return "";
        }

        /// <summary>
        /// Every description entered against the count: the one typed on the header
        /// (already in hand from the main query) followed by the one typed on each
        /// line, labelled with its line no and product so a reader knows which row
        /// it annotates.
        /// </summary>
        /// <param name="M_Inventory_ID">Selected inventory count id.</param>
        /// <param name="headerNote">M_Inventory.Description, or "" .</param>
        /// <returns>Notes in reading order; never null, may be empty.</returns>
        private List<NoteData> LoadNotes(int M_Inventory_ID, string headerNote)
        {
            List<NoteData> notes = new List<NoteData>();

            if (!string.IsNullOrEmpty(headerNote) && headerNote.Trim().Length > 0)
                notes.Add(new NoteData { NoteType = "header", Text = headerNote.Trim() });

            try
            {
                string sql = @"SELECT l.Line, l.Description, p.Name AS ProductName
                                 FROM M_InventoryLine l
                                 LEFT OUTER JOIN M_Product p ON (p.M_Product_ID = l.M_Product_ID)
                                WHERE l.M_Inventory_ID = @M_Inventory_ID
                                  AND l.IsActive       = 'Y'
                                  AND l.Description IS NOT NULL
                                ORDER BY l.Line";
                DataSet ds = DB.ExecuteDataset(sql, InventoryParam(M_Inventory_ID), null);
                if (ds == null || ds.Tables.Count == 0) return notes;

                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    string text = Util.GetValueOfString(r["Description"]);
                    if (string.IsNullOrEmpty(text) || text.Trim().Length == 0) continue;

                    // "Line 10 — Steel Bolt M8: <note>", so the note names the row
                    // it was written against without the reader counting back to
                    // the table.
                    string label = "";
                    int lineNo = Util.GetValueOfInt(r["Line"]);
                    if (lineNo > 0) label = "Line " + lineNo;
                    string product = Util.GetValueOfString(r["ProductName"]);
                    if (!string.IsNullOrEmpty(product))
                        label = (label.Length > 0 ? label + " — " : "") + product;

                    notes.Add(new NoteData
                    {
                        NoteType = "line",
                        Label    = label,
                        Text     = text.Trim()
                    });
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadNotes (M_Inventory_ID=" + M_Inventory_ID + "): " + ex.Message);
            }
            return notes;
        }

        /// <summary>
        /// The count's audit trail: who created it, who last changed it and when,
        /// when it was completed and posted, plus any chat notes logged against it —
        /// merged newest-first and capped.
        ///
        /// Each source runs under its own guard, so a DB-level problem with one
        /// degrades to a partial trail (logged) rather than breaking the overview.
        /// Modelled on VAS_102, which reads the same table.
        /// </summary>
        /// <param name="M_Inventory_ID">Selected inventory count id.</param>
        /// <param name="docStatus">The count's DocStatus, for the completion entry.</param>
        /// <returns>Activity rows, newest first; never null.</returns>
        private List<ActivityData> LoadActivity(int M_Inventory_ID, string docStatus)
        {
            // A runaway guard, not a headline count: the panel pages the feed 15
            // rows at a time, so it sits high enough that a real count never
            // reaches it.
            const int MAX_ENTRIES = 200;

            List<ActivityData> activity = new List<ActivityData>();

            // The count's document lifecycle, one row per completed workflow node
            // — prepared / completed / re-activated / voided / closed / approved /
            // rejected. Run FIRST so the milestone loader below knows whether the
            // completion has already been reported properly and can skip its own
            // derived stand-in. Follows VAS_092.
            int wfRows = LoadCountWorkflowActivity(M_Inventory_ID, activity);

            LoadCountMilestones(M_Inventory_ID, docStatus, activity, wfRows == 0);
            // What was actually edited, field by field. This is what the reader
            // wants from an audit trail — the coarse "updated" milestone
            // LoadCountMilestones used to add could only report the LAST save and
            // never said what it touched, so it is gone.
            LoadFieldChangeActivity(M_Inventory_ID, activity);
            LoadPostingActivity(M_Inventory_ID, activity);
            LoadNoteActivity(M_Inventory_ID, activity);
            LoadEmailActivity(M_Inventory_ID, activity);
            // Appointments, tasks, calls and letters filed against the count.
            LoadSharedSourceActivity(M_Inventory_ID, activity);

            // Newest first; entries with no timestamp sink to the bottom.
            activity.Sort(delegate (ActivityData a, ActivityData b)
            {
                return b.Created.GetValueOrDefault(DateTime.MinValue)
                        .CompareTo(a.Created.GetValueOrDefault(DateTime.MinValue));
            });

            if (activity.Count > MAX_ENTRIES)
                activity = activity.GetRange(0, MAX_ENTRIES);
            return activity;
        }

        /// <summary>
        /// The count's own milestones: created (Created / CreatedBy), updated
        /// (Updated / UpdatedBy, only when it differs from the create stamp) and,
        /// for a completed or closed count, completed — the workflow's DocComplete
        /// stamp when there is one, else the last change.
        /// </summary>
        private void LoadCountMilestones(
            int M_Inventory_ID, string docStatus, List<ActivityData> list, bool addCompletion)
        {
            try
            {
                string sql = @"SELECT inv.Created,
                                      inv.Updated,
                                      cu.Name AS CreatedByName,
                                      uu.Name AS UpdatedByName
                                 FROM M_Inventory inv
                                 LEFT OUTER JOIN AD_User cu ON (inv.CreatedBy = cu.AD_User_ID)
                                 LEFT OUTER JOIN AD_User uu ON (inv.UpdatedBy = uu.AD_User_ID)
                                WHERE inv.M_Inventory_ID = @M_Inventory_ID";
                DataSet ds = DB.ExecuteDataset(sql, InventoryParam(M_Inventory_ID), null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return;

                DataRow r = ds.Tables[0].Rows[0];
                DateTime? created = Util.GetValueOfDateTime(r["Created"]);
                DateTime? updated = Util.GetValueOfDateTime(r["Updated"]);
                string updatedBy  = Util.GetValueOfString(r["UpdatedByName"]);

                list.Add(new ActivityData
                {
                    Type     = "created",
                    UserName = Util.GetValueOfString(r["CreatedByName"]),
                    Created  = created
                });

                // No "updated" row here any more. It came from M_Inventory.Updated,
                // which is only ever the LAST save — it could not report an earlier
                // edit at all, and it never said what was changed. The change log
                // answers both (LoadFieldChangeActivity).

                // Only when the workflow named no lifecycle node of its own. Where
                // it did, this row would restate a completion the workflow has
                // already reported — with a worse timestamp and, on a count
                // re-activated and re-completed, only the last one of them.
                if (addCompletion && (docStatus == "CO" || docStatus == "CL"))
                {
                    DateTime? completedAt = GetCompletedDate(M_Inventory_ID);
                    list.Add(new ActivityData
                    {
                        Type     = "completed",
                        UserName = !string.IsNullOrEmpty(_lastCompletedByName)
                            ? _lastCompletedByName : updatedBy,
                        Created  = completedAt.HasValue ? completedAt : updated
                    });
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadCountMilestones (M_Inventory_ID=" + M_Inventory_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// The count's document lifecycle, one activity row per completed workflow
        /// node (AD_WF_Process -> AD_WF_Activity -> AD_WF_Node, WFState 'CC')
        /// against this M_Inventory: prepare, complete, re-activate, void, close,
        /// approve / reject — each with the node's own name, the user who ran it
        /// and when.
        ///
        /// This is what the single derived "completed" milestone could not do. That
        /// row came from the LAST DocComplete stamp, so a count re-activated and
        /// re-counted showed one completion and no re-activation at all — the
        /// reader could not see that the document had been reopened.
        ///
        /// The node's own NAME is the headline, so a tenant that renamed its
        /// workflow nodes reads the trail in its own words. Ported from VAS_092.
        /// </summary>
        /// <param name="M_Inventory_ID">Selected inventory count id.</param>
        /// <param name="list">Activity list being populated.</param>
        /// <returns>How many lifecycle rows were added.</returns>
        private int LoadCountWorkflowActivity(int M_Inventory_ID, List<ActivityData> list)
        {
            int added = 0;
            try
            {
                string sql = @"SELECT wfa.Created              AS EventOn,
                                      COALESCE(wfn.Name, wfn.Value) AS NodeName,
                                      UPPER(TRIM(wfn.Value))   AS NodeValue,
                                      u.Name                   AS UserName
                                 FROM AD_WF_Process wfp
                                INNER JOIN AD_WF_Activity wfa
                                        ON (wfa.AD_WF_Process_ID = wfp.AD_WF_Process_ID)
                                INNER JOIN AD_WF_Node wfn
                                        ON (wfn.AD_WF_Node_ID = wfa.AD_WF_Node_ID)
                                INNER JOIN AD_Table adt
                                        ON (adt.AD_Table_ID = wfp.AD_Table_ID)
                                 LEFT OUTER JOIN AD_User u
                                        ON (u.AD_User_ID = wfa.CreatedBy)
                                WHERE wfp.Record_ID = @M_Inventory_ID
                                  AND UPPER(adt.TableName) = 'M_INVENTORY'
                                  AND wfp.IsActive  = 'Y'
                                  AND wfa.IsActive  = 'Y'
                                  AND wfn.IsActive  = 'Y'
                                  AND wfa.WFState   = 'CC'
                                ORDER BY wfa.Created";
                DataSet ds = DB.ExecuteDataset(sql, InventoryParam(M_Inventory_ID), null);
                if (ds == null || ds.Tables.Count == 0) return 0;

                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    string type = WorkflowActivityType(Util.GetValueOfString(r["NodeValue"]));
                    if (type == null) continue;      // routing / non-document node

                    list.Add(new ActivityData
                    {
                        Type     = type,
                        Text     = Util.GetValueOfString(r["NodeName"]),
                        UserName = Util.GetValueOfString(r["UserName"]),
                        Created  = Util.GetValueOfDateTime(r["EventOn"])
                    });
                    added++;
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadCountWorkflowActivity (M_Inventory_ID=" + M_Inventory_ID + "): " + ex.Message);
            }
            return added;
        }

        /// <summary>
        /// Maps a workflow node value to an activity type the client can tag, or
        /// null for nodes that are pure routing (start / end / split) and carry no
        /// meaning for a reader. Ported from VAS_092.
        /// </summary>
        private static string WorkflowActivityType(string nodeValue)
        {
            if (string.IsNullOrEmpty(nodeValue)) return null;
            string v = nodeValue.Replace("(", "").Replace(")", "").Replace("_", "").Trim();

            if (v.Contains("REACTIVATE")) return "reactivated";   // before COMPLETE
            if (v.Contains("COMPLETE"))   return "completed";
            if (v.Contains("REJECT"))     return "rejected";
            if (v.Contains("APPROV"))     return "approval";
            if (v.Contains("VOID"))       return "voided";
            if (v.Contains("REVERSE"))    return "reversed";
            if (v.Contains("CLOSE"))      return "closed";
            if (v.Contains("PREPARE"))    return "prepared";
            if (v.Contains("INVALID"))    return "invalidated";
            return null;
        }

        /// <summary>
        /// The count's field-level edit history, read from the platform's change
        /// log (AD_ChangeLog): one row per changed COLUMN carrying the dictionary's
        /// label for it, the value it held before and the value it holds now, when
        /// the change was saved and who saved it.
        ///
        /// Both the header (M_Inventory) and its LINES (M_InventoryLine) are read.
        /// A count's meaningful edits are its counted quantities, and those live on
        /// the lines — a header-only trail would report almost nothing. A line
        /// change is labelled with its line number and product so the reader can
        /// tell which row moved.
        ///
        /// Rows are NOT collapsed to one per save the way VAS_092 collapses them:
        /// this panel is asked for exactly what changed, so a save touching three
        /// columns is three rows.
        ///
        /// Silently degrades when change logging is off for the ROLE that made the
        /// edit (AD_Role.IsChangeLog) — the platform writes no AD_ChangeLog rows at
        /// all in that case, so there is nothing to report and the feed keeps its
        /// milestones. That is a dictionary setting, not something this can fix.
        ///
        /// The table is matched with UPPER so a dictionary holding the name in
        /// another case still resolves. An equality on the stored spelling is the
        /// single point at which this whole loader returns nothing while the change
        /// log is full of good rows — and it fails silently, which reads exactly
        /// like "the field-level trail was never implemented".
        /// </summary>
        /// <param name="M_Inventory_ID">Selected inventory count id.</param>
        /// <param name="list">Activity list being populated.</param>
        private void LoadFieldChangeActivity(int M_Inventory_ID, List<ActivityData> list)
        {
            // ----- Header edits -----
            try
            {
                string sql = @"SELECT cl.Created,
                                      cl.OldValue,
                                      cl.NewValue,
                                      COALESCE(c.Name, c.ColumnName) AS FieldName,
                                      c.ColumnName               AS FieldColumn,
                                      c.AD_Reference_ID          AS RefType,
                                      c.AD_Reference_Value_ID    AS RefValueId,
                                      u.Name AS UserName
                                 FROM AD_ChangeLog cl
                                INNER JOIN AD_Table adt ON (adt.AD_Table_ID = cl.AD_Table_ID)
                                 LEFT OUTER JOIN AD_Column c ON (c.AD_Column_ID = cl.AD_Column_ID)
                                 LEFT OUTER JOIN AD_User  u  ON (u.AD_User_ID   = cl.CreatedBy)
                                WHERE cl.Record_ID = @M_Inventory_ID
                                  AND UPPER(adt.TableName) = 'M_INVENTORY'
                                  AND COALESCE(cl.IsActive, 'Y') = 'Y'
                                ORDER BY cl.Created DESC";
                AddChangeRows(DB.ExecuteDataset(sql, InventoryParam(M_Inventory_ID), null), "", list);
            }
            catch (Exception ex)
            {
                _log.Severe("LoadFieldChangeActivity/header (M_Inventory_ID=" + M_Inventory_ID + "): " + ex.Message);
            }

            // ----- Line edits -----
            //
            // The line ids are reached through a subselect so the statement carries
            // its bind name exactly once: positional binding gives a repeated name
            // a second, unfilled placeholder.
            try
            {
                string sql = @"SELECT cl.Created,
                                      cl.OldValue,
                                      cl.NewValue,
                                      COALESCE(c.Name, c.ColumnName) AS FieldName,
                                      c.ColumnName               AS FieldColumn,
                                      c.AD_Reference_ID          AS RefType,
                                      c.AD_Reference_Value_ID    AS RefValueId,
                                      u.Name  AS UserName,
                                      l.Line  AS LineNo,
                                      p.Name  AS ProductName
                                 FROM AD_ChangeLog cl
                                INNER JOIN AD_Table adt ON (adt.AD_Table_ID = cl.AD_Table_ID)
                                INNER JOIN M_InventoryLine l
                                        ON (l.M_InventoryLine_ID = cl.Record_ID)
                                 LEFT OUTER JOIN M_Product p ON (p.M_Product_ID  = l.M_Product_ID)
                                 LEFT OUTER JOIN AD_Column c ON (c.AD_Column_ID  = cl.AD_Column_ID)
                                 LEFT OUTER JOIN AD_User  u  ON (u.AD_User_ID    = cl.CreatedBy)
                                WHERE UPPER(adt.TableName) = 'M_INVENTORYLINE'
                                  AND COALESCE(cl.IsActive, 'Y') = 'Y'
                                  AND l.M_Inventory_ID = @M_Inventory_ID
                                ORDER BY cl.Created DESC";
                DataSet ds = DB.ExecuteDataset(sql, InventoryParam(M_Inventory_ID), null);
                if (ds != null && ds.Tables.Count > 0)
                {
                    foreach (DataRow r in ds.Tables[0].Rows)
                    {
                        string scope = "#" + Util.GetValueOfInt(r["LineNo"]);
                        string prod  = Util.GetValueOfString(r["ProductName"]);
                        if (!string.IsNullOrEmpty(prod)) scope += " " + prod.Trim();
                        AddChangeRow(r, scope, list);
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadFieldChangeActivity/lines (M_Inventory_ID=" + M_Inventory_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// Resolves a change-log value into the text the field shows — a reference
        /// into the referenced record's identifier, a list code into its label, a
        /// timestamp into the date alone. Shared with the other overview panels
        /// (VAS_ChangeLogValueModel). One per request, so its caches last exactly
        /// as long as the feed being built.
        /// </summary>
        private readonly VAS_ChangeLogValueModel _changeValues = new VAS_ChangeLogValueModel();

        /// <summary>Reads the appointment / task / call / letter sources every
        /// overview panel shares (VAS_ActivitySourcesModel).</summary>
        private readonly VAS_ActivitySourcesModel _activitySources = new VAS_ActivitySourcesModel();

        /// <summary>
        /// The correspondence and engagement sources shared with every other
        /// overview panel: appointments and tasks (AppointmentsInfo, split on
        /// IsTask), calls (VA048_CallDetails) and letters (MailAttachment1,
        /// AttachmentType 'I'), each pinned to the count by AD_Table_ID +
        /// Record_ID.
        ///
        /// Mails stay with LoadEmailActivity, which carries the recipient and body
        /// detail the mail drawer needs and now excludes letters so the two kinds
        /// cannot both claim the same row.
        /// </summary>
        private void LoadSharedSourceActivity(int M_Inventory_ID, List<ActivityData> list)
        {
            List<VAS_ActivitySourceRow> rows =
                _activitySources.Load("M_Inventory", M_Inventory_ID, false);
            foreach (VAS_ActivitySourceRow s in rows)
            {
                list.Add(new ActivityData
                {
                    Type        = s.Kind,      // appointment | task | call | letter
                    Text        = s.Title,
                    Body        = s.Body,
                    Location    = s.Location,
                    IsClosed    = s.IsClosed,
                    IsCancelled = s.IsCancelled,
                    MailTo      = s.MailTo,
                    MailCc      = s.MailCc,
                    MailBcc     = s.MailBcc,
                    MailFrom    = s.MailFrom,
                    IsMailSent  = s.IsMailSent,
                    // An appointment or task brings the mails sent against it.
                    Mails       = s.Mails,
                    UserName    = s.ActorName,
                    Created     = s.EventTime
                });
            }
        }

        /// <summary>Adds every row of a change-log result under one scope label.</summary>
        private void AddChangeRows(DataSet ds, string scope, List<ActivityData> list)
        {
            if (ds == null || ds.Tables.Count == 0) return;
            foreach (DataRow r in ds.Tables[0].Rows) AddChangeRow(r, scope, list);
        }

        /// <summary>
        /// Turns one AD_ChangeLog row into an activity entry. A change whose column
        /// cannot be resolved through the dictionary is skipped: without a field
        /// name the row says only that "something" changed, which is what this
        /// whole loader exists to stop reporting.
        /// </summary>
        private void AddChangeRow(DataRow r, string scope, List<ActivityData> list)
        {
            string field = Util.GetValueOfString(r["FieldName"]);
            if (string.IsNullOrEmpty(field)) return;

            // Reported as the field SHOWS them, not as the log stored them: a
            // reference reads as the referenced record's identifier, a list value
            // as its label, a date as the date alone.
            string column  = Util.GetValueOfString(r["FieldColumn"]);
            int refType    = Util.GetValueOfInt(r["RefType"]);
            int refValueId = Util.GetValueOfInt(r["RefValueId"]);

            list.Add(new ActivityData
            {
                // "updated", not "changed": the same type VAS_092 emits for a
                // field-level edit, so the two panels tag and headline the row
                // identically ("Updated <field>").
                Type        = "updated",
                FieldName   = field,
                OldValue    = _changeValues.Display(
                                  ChangeValue(Util.GetValueOfString(r["OldValue"])),
                                  column, refType, refValueId),
                NewValue    = _changeValues.Display(
                                  ChangeValue(Util.GetValueOfString(r["NewValue"])),
                                  column, refType, refValueId),
                ChangeScope = scope,
                UserName    = Util.GetValueOfString(r["UserName"]),
                Created     = Util.GetValueOfDateTime(r["Created"])
            });
        }

        /// <summary>
        /// Normalises a logged value for display. The platform writes the literal
        /// "null" into AD_ChangeLog for a cleared field, which would otherwise be
        /// shown to the reader as though it were the text "null".
        /// </summary>
        private static string ChangeValue(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            string v = value.Trim();
            return string.Equals(v, "null", StringComparison.OrdinalIgnoreCase) ? "" : v;
        }

        /// <summary>Who completed the count and when — the workflow's DocComplete
        /// activity stamp, or null when it has none. The actor is left in
        /// <see cref="_lastCompletedByName"/> for the caller.</summary>
        private string _lastCompletedByName;

        private DateTime? GetCompletedDate(int M_Inventory_ID)
        {
            _lastCompletedByName = "";
            try
            {
                string sql = @"SELECT MAX(wfa.Created) AS CompletedOn,
                                      MAX(u.Name)      AS CompletedBy
                                 FROM AD_WF_Process wfp
                                INNER JOIN AD_WF_Activity wfa
                                        ON (wfa.AD_WF_Process_ID = wfp.AD_WF_Process_ID)
                                INNER JOIN AD_Table adt
                                        ON (adt.AD_Table_ID = wfp.AD_Table_ID)
                                 LEFT OUTER JOIN AD_User u ON (u.AD_User_ID = wfa.AD_User_ID)
                                WHERE wfp.Record_ID = @M_Inventory_ID
                                  AND UPPER(adt.TableName) = 'M_INVENTORY'
                                  AND wfp.IsActive  = 'Y'
                                  AND wfa.IsActive  = 'Y'
                                  AND wfa.WFState   = 'CC'";
                DataSet ds = DB.ExecuteDataset(sql, InventoryParam(M_Inventory_ID), null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return null;

                _lastCompletedByName = Util.GetValueOfString(ds.Tables[0].Rows[0]["CompletedBy"]);
                return Util.GetValueOfDateTime(ds.Tables[0].Rows[0]["CompletedOn"]);
            }
            catch (Exception ex)
            {
                _log.Severe("GetCompletedDate (M_Inventory_ID=" + M_Inventory_ID + "): " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Adds a "posted" entry from the earliest Fact_Acct row written for the
        /// count, carrying the user who ran the posting.
        /// </summary>
        private void LoadPostingActivity(int M_Inventory_ID, List<ActivityData> list)
        {
            try
            {
                string sql = @"SELECT fa.Created, u.Name AS UserName
                                 FROM Fact_Acct fa
                                INNER JOIN AD_Table adt ON (adt.AD_Table_ID = fa.AD_Table_ID)
                                 LEFT OUTER JOIN AD_User u ON (fa.CreatedBy = u.AD_User_ID)
                                WHERE fa.Record_ID  = @M_Inventory_ID
                                  AND UPPER(adt.TableName) = 'M_INVENTORY'
                                ORDER BY fa.Created";
                DataSet ds = DB.ExecuteDataset(sql, InventoryParam(M_Inventory_ID), null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return;

                DataRow r = ds.Tables[0].Rows[0];
                list.Add(new ActivityData
                {
                    Type     = "posted",
                    UserName = Util.GetValueOfString(r["UserName"]),
                    Created  = Util.GetValueOfDateTime(r["Created"])
                });
            }
            catch (Exception ex)
            {
                _log.Severe("LoadPostingActivity (M_Inventory_ID=" + M_Inventory_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// Adds the chat notes (CM_ChatEntry) logged against this count — each
        /// carrying the commenter's name, the moment it was posted and the comment
        /// text, which is what the panel prints on the row.
        ///
        /// The author is taken from CM_ChatEntry.AD_User_ID falling back to
        /// CreatedBy: an entry logged through the platform's own chat plumbing often
        /// leaves AD_User_ID null, which would print a comment with no name.
        /// </summary>
        private void LoadNoteActivity(int M_Inventory_ID, List<ActivityData> list)
        {
            try
            {
                string sql = @"SELECT ce.CharacterData,
                                      ce.Created,
                                      COALESCE(u.Name, cu.Name) AS UserName
                                 FROM CM_ChatEntry ce
                                INNER JOIN CM_Chat ch      ON (ce.CM_Chat_ID = ch.CM_Chat_ID)
                                 LEFT OUTER JOIN AD_User u  ON (ce.AD_User_ID = u.AD_User_ID)
                                 LEFT OUTER JOIN AD_User cu ON (ce.CreatedBy  = cu.AD_User_ID)
                                WHERE ch.AD_Table_ID IN
                                      (SELECT t.AD_Table_ID FROM AD_Table t
                                        WHERE UPPER(t.TableName) = 'M_INVENTORY')
                                  AND ch.Record_ID = @M_Inventory_ID
                                  AND ce.IsActive  = 'Y'";
                DataSet ds = DB.ExecuteDataset(sql, InventoryParam(M_Inventory_ID), null);
                if (ds == null || ds.Tables.Count == 0) return;

                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    list.Add(new ActivityData
                    {
                        Type     = "note",
                        Text     = Util.GetValueOfString(r["CharacterData"]),
                        UserName = Util.GetValueOfString(r["UserName"]),
                        Created  = Util.GetValueOfDateTime(r["Created"])
                    });
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadNoteActivity (M_Inventory_ID=" + M_Inventory_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// Adds the e-mails sent against this count (MailAttachment1, joined by
        /// AD_Table_ID = M_Inventory + Record_ID = the count id) as "email" rows:
        /// recipients, subject (Title), body (TextMsg), when (Created) and who sent
        /// it (CreatedBy).
        ///
        /// The body travels with the row so the panel can reveal it on click
        /// without a second round trip. Ported from VAS_102, which reads the same
        /// table.
        /// </summary>
        private void LoadEmailActivity(int M_Inventory_ID, List<ActivityData> list)
        {
            try
            {
                // A row is an e-mail when it has somewhere to go — a recipient on
                // any of the address columns. AttachmentType is deliberately NOT
                // filtered on: its value varies between installations, and a row
                // that carries an address is a mail whatever the column says.
                //
                // "Has an address" is tested against a SPACE, not against ''.
                // Oracle stores the empty string as NULL, so NVL(TRIM(x), '')
                // yields NULL and `<> ''` compares against NULL — UNKNOWN for every
                // row, including the ones that DO carry an address, and the query
                // returned no mails at all. Comparing to ' ' keeps the fallback
                // non-null on Oracle, and SQL Server blank-pads the comparison so
                // an empty address still fails it.
                //
                // The table id is matched with IN + UPPER so a dictionary holding
                // the name in another case, or more than one row for it, resolves
                // instead of failing the statement.
                string sql = @"SELECT ma.MailAddress,
                                      ma.MailAddressCc,
                                      ma.MailAddressBcc,
                                      ma.MailAddressFrom,
                                      ma.Title,
                                      ma.TextMsg,
                                      ma.Created,
                                      ma.IsMailSent,
                                      u.Name AS UserName
                                 FROM MailAttachment1 ma
                                 LEFT OUTER JOIN AD_User u ON (u.AD_User_ID = ma.CreatedBy)
                                WHERE ma.AD_Table_ID IN
                                      (SELECT t.AD_Table_ID FROM AD_Table t
                                        WHERE UPPER(t.TableName) = 'M_INVENTORY')
                                  AND ma.Record_ID          = @M_Inventory_ID
                                  AND NVL(ma.IsActive, 'Y') = 'Y'
                                  -- Letters ('I') and only letters are filtered
                                  -- out: they are a kind of their own now and
                                  -- LoadSharedSourceActivity reads them, so leaving
                                  -- them here would report each one twice. Every
                                  -- other AttachmentType still counts as a mail.
                                  AND COALESCE(ma.AttachmentType, 'M') <> 'I'
                                  AND (NVL(TRIM(ma.MailAddress), ' ')    <> ' '
                                    OR NVL(TRIM(ma.MailAddressCc), ' ')  <> ' '
                                    OR NVL(TRIM(ma.MailAddressBcc), ' ') <> ' ')
                                ORDER BY ma.Created DESC";
                DataSet ds = DB.ExecuteDataset(sql, InventoryParam(M_Inventory_ID), null);
                if (ds == null || ds.Tables.Count == 0) return;

                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    list.Add(new ActivityData
                    {
                        Type       = "email",
                        // Text is the row's headline everywhere in this feed; for
                        // an e-mail that is its subject.
                        Text       = Util.GetValueOfString(r["Title"]),
                        // A mail sent as HTML stores its markup in TextMsg; the
                        // panel shows a body as text, so it is flattened here.
                        Body       = MailBodyToText(Util.GetValueOfString(r["TextMsg"])),
                        MailTo     = Util.GetValueOfString(r["MailAddress"]),
                        MailCc     = Util.GetValueOfString(r["MailAddressCc"]),
                        MailBcc    = Util.GetValueOfString(r["MailAddressBcc"]),
                        MailFrom   = Util.GetValueOfString(r["MailAddressFrom"]),
                        IsMailSent = Util.GetValueOfString(r["IsMailSent"]) == "Y",
                        UserName   = Util.GetValueOfString(r["UserName"]),
                        Created    = Util.GetValueOfDateTime(r["Created"])
                    });
                }
            }
            catch (Exception ex)
            {
                // Non-fatal: a schema without MailAttachment1 just shows no e-mails.
                _log.Severe("LoadEmailActivity (M_Inventory_ID=" + M_Inventory_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// Cheap "is this markup" test — a real tag, not a stray '&lt;' in a
        /// plain-text mail ("qty &lt; 10"), so a plain body is left untouched.
        /// </summary>
        private static readonly Regex HTML_BODY = new Regex(
            @"<\s*/?\s*(html|body|head|br|p|div|table|thead|tbody|tr|td|th|span|a|img|b|i|u"
            + @"|strong|em|ul|ol|li|h[1-6]|font|style|script)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Renders a mail body (MailAttachment1.TextMsg) as readable plain text.
        ///
        /// A mail sent as HTML stores its markup here and the panel shows the body
        /// as text, so without this the reader gets tags instead of a message.
        /// Block-level markup becomes line breaks, table cells become tabs,
        /// everything else is dropped and entities are decoded LAST — so the
        /// browser still receives text it can safely escape and no markup is ever
        /// handed to the panel. A body with no markup is returned as stored.
        /// </summary>
        private static string MailBodyToText(string body)
        {
            if (string.IsNullOrEmpty(body)) return body;
            if (!HTML_BODY.IsMatch(body)) return body;      // plain-text mail

            try
            {
                string s = body;

                // Head matter, styles and scripts carry no reading content.
                s = Regex.Replace(s, @"<\s*(script|style|head)\b[^>]*>.*?<\s*/\s*\1\s*>", " ",
                                  RegexOptions.IgnoreCase | RegexOptions.Singleline);

                // Block boundaries become line breaks so paragraphs survive.
                s = Regex.Replace(s, @"<\s*br\s*/?\s*>", "\n", RegexOptions.IgnoreCase);
                s = Regex.Replace(s, @"<\s*/\s*(p|div|tr|li|h[1-6]|table|blockquote)\s*>", "\n",
                                  RegexOptions.IgnoreCase);
                // Opening tags too, so a <p> with no closing tag still breaks.
                // 'tr' is deliberately absent — </tr> already ends the row, and
                // breaking on both would leave a blank line between every row.
                s = Regex.Replace(s, @"<\s*(p|div|li|h[1-6])\b[^>]*>", "\n",
                                  RegexOptions.IgnoreCase);
                // Cells read better separated than run together.
                s = Regex.Replace(s, @"<\s*/\s*(td|th)\s*>", "\t", RegexOptions.IgnoreCase);

                // Everything left is presentation.
                s = Regex.Replace(s, @"<[^>]*>", string.Empty);

                // Entities last, so an escaped &lt;b&gt; in the text was never
                // treated as a tag above.
                s = WebUtility.HtmlDecode(s);
                s = s.Replace(' ', ' ');               // nbsp reads as a space

                // Normalise the whitespace the markup left behind.
                s = s.Replace("\r\n", "\n").Replace('\r', '\n');
                s = Regex.Replace(s, @"[^\S\n\t]+", " ");   // runs of spaces -> one
                s = Regex.Replace(s, @"\t{2,}", "\t");
                s = Regex.Replace(s, @"[ \t]*\n[ \t]*", "\n");   // incl. the last cell's tab
                s = Regex.Replace(s, @"\n{3,}", "\n\n");    // at most one blank line

                return s.Trim();
            }
            catch (Exception ex)
            {
                // Never lose the mail over a formatting failure — show it raw.
                _log.Severe("MailBodyToText: " + ex.Message);
                return body;
            }
        }

        /// <summary>Single-parameter helper for the count-scoped queries.</summary>
        private SqlParameter[] InventoryParam(int M_Inventory_ID)
        {
            return new SqlParameter[] { new SqlParameter("@M_Inventory_ID", M_Inventory_ID) };
        }

        /// <summary>
        /// Resolves a window's AD_Window_ID from its name (AD_Window.Name) for the
        /// panel's record-open path — a Related Documents row opens the screen named
        /// for its table rather than whatever the table's zoom target resolves to.
        ///
        /// Restricted to windows this tenant can see (AD_Client_ID 0 or its own),
        /// preferring the tenant's own row over the system one. Whether the ROLE may
        /// open it is the platform's call, made when the window is started. Ported
        /// from VAS_092.
        /// </summary>
        /// <param name="ctx">User context (client).</param>
        /// <param name="windowName">Window name to resolve.</param>
        /// <returns>The window id, or 0 when the name resolves to nothing.</returns>
        public int GetWindowId(Ctx ctx, string windowName)
        {
            if (string.IsNullOrEmpty(windowName)) return 0;
            try
            {
                string sql = @"SELECT w.AD_Window_ID
                                 FROM AD_Window w
                                WHERE w.Name         = @Name
                                  AND w.IsActive     = 'Y'
                                  AND w.AD_Client_ID IN (0, @AD_Client_ID)
                                ORDER BY w.AD_Client_ID DESC";
                SqlParameter[] param = new SqlParameter[]
                {
                    new SqlParameter("@Name", windowName.Trim()),
                    new SqlParameter("@AD_Client_ID", ctx == null ? 0 : ctx.GetAD_Client_ID())
                };
                DataSet ds = DB.ExecuteDataset(sql, param, null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
                    return 0;
                return Util.GetValueOfInt(ds.Tables[0].Rows[0]["AD_Window_ID"]);
            }
            catch (Exception ex)
            {
                _log.Severe("GetWindowId (" + windowName + "): " + ex.Message);
                return 0;
            }
        }

        /// <summary>
        /// Resolves the window a TABLE's records open in: the table's own zoom
        /// target (AD_Table.AD_Window_ID), falling back to the first window that
        /// has a tab on the table.
        ///
        /// This is the record-open path's last resort, for a row whose screen
        /// cannot be named in the client's map. The work order needs it: VA075
        /// ships its own window and is not part of this solution, so no name can be
        /// hard-coded for it, and the browser-side zoom lookup only knows tables
        /// the client has already cached. The dictionary knows it either way.
        ///
        /// Each statement carries a single bind name, occurring once: positional
        /// binding gives a repeated name a second, unfilled placeholder.
        /// Ported from VAS_102.
        /// </summary>
        /// <param name="ctx">User context (unused today; kept for symmetry with
        /// <see cref="GetWindowId"/>, which filters by client).</param>
        /// <param name="tableName">Physical table name, e.g. "VA075_WorkOrder".</param>
        /// <returns>The window id, or 0 when the table has no window at all.</returns>
        public int GetWindowIdByTable(Ctx ctx, string tableName)
        {
            if (string.IsNullOrEmpty(tableName)) return 0;
            string name = tableName.Trim();
            try
            {
                string sql = @"SELECT t.AD_Window_ID
                                 FROM AD_Table t
                                WHERE UPPER(t.TableName) = UPPER(@TableName)
                                  AND t.IsActive         = 'Y'
                                  AND COALESCE(t.AD_Window_ID, 0) > 0";
                DataSet ds = DB.ExecuteDataset(
                    sql, new SqlParameter[] { new SqlParameter("@TableName", name) }, null);
                if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                {
                    int id = Util.GetValueOfInt(ds.Tables[0].Rows[0]["AD_Window_ID"]);
                    if (id > 0) return id;
                }

                // No zoom target on the table itself — take the window whose first
                // tab sits on this table, which is the screen that maintains it.
                sql = @"SELECT tb.AD_Window_ID
                          FROM AD_Tab tb
                         INNER JOIN AD_Table t ON (t.AD_Table_ID = tb.AD_Table_ID)
                         WHERE UPPER(t.TableName) = UPPER(@TableName)
                           AND tb.IsActive        = 'Y'
                           AND t.IsActive         = 'Y'
                           AND tb.SeqNo = (SELECT MIN(tb2.SeqNo)
                                             FROM AD_Tab tb2
                                            WHERE tb2.AD_Window_ID = tb.AD_Window_ID
                                              AND tb2.IsActive     = 'Y')
                         ORDER BY tb.SeqNo, tb.AD_Tab_ID";
                ds = DB.ExecuteDataset(
                    sql, new SqlParameter[] { new SqlParameter("@TableName", name) }, null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
                    return 0;
                return Util.GetValueOfInt(ds.Tables[0].Rows[0]["AD_Window_ID"]);
            }
            catch (Exception ex)
            {
                _log.Severe("GetWindowIdByTable (" + name + "): " + ex.Message);
                return 0;
            }
        }

        /// <summary>
        /// Builds the unit-rate SQL expression from whichever optional cost columns
        /// exist on M_InventoryLine, ending at 0.
        ///
        /// The rate follows the DIRECTION of the line's variance:
        ///   * counted greater than system — stock being brought in — is valued at
        ///     PriceCost, the price it comes in at, falling back to
        ///     CurrentCostPrice when PriceCost is zero;
        ///   * otherwise (system greater than or equal to counted) it is valued at
        ///     CurrentCostPrice, what the stock on hand is already carried at.
        ///
        /// VA024_UnitPrice stays the last resort behind both, as it was. A schema
        /// carrying neither cost column values every line at 0, exactly as before.
        /// </summary>
        private string BuildRateExpr(bool hasCurrentCost, bool hasUnitPrice, bool hasPriceCost)
        {
            // What the stock on hand is carried at — the "going out" rate.
            List<string> onHand = new List<string>();
            if (hasCurrentCost) onHand.Add("l.CurrentCostPrice");
            if (hasUnitPrice)   onHand.Add("l.VA024_UnitPrice");
            string onHandExpr = onHand.Count == 0
                ? "0" : "COALESCE(" + string.Join(", ", onHand.ToArray()) + ", 0)";

            if (!hasPriceCost) return onHandExpr;

            // The "coming in" rate: PriceCost, but only when it says something —
            // NULLIF sends a stored zero down to the on-hand rate rather than
            // valuing found stock at nothing.
            string inExpr = "COALESCE(NULLIF(l.PriceCost, 0), " + onHandExpr + ")";

            StringBuilder sb = new StringBuilder();
            sb.Append("(CASE WHEN COALESCE(l.QtyCount, 0) > COALESCE(l.QtyBook, 0) THEN ");
            sb.Append(inExpr);
            sb.Append(" ELSE ");
            sb.Append(onHandExpr);
            sb.Append(" END)");
            return sb.ToString();
        }

        /// <summary>
        /// Loads M_InventoryLine rows for the count with product, locator and UOM
        /// metadata, system (book) / counted quantities, the derived variance and
        /// a unit rate / line value. Child of an already authorized count, so no
        /// separate MRole filter is applied here.
        /// </summary>
        /// <param name="M_Inventory_ID">Owning inventory count id.</param>
        /// <param name="rateExpr">Unit-rate SQL expression (schema-aware).</param>
        /// <param name="varExpr">Variance SQL expression.</param>
        /// <returns>Ordered list of line rows (may be empty).</returns>
        private List<InventoryCountLineData> LoadLines(
            int M_Inventory_ID, string rateExpr, string varExpr)
        {
            List<InventoryCountLineData> lines = new List<InventoryCountLineData>();

            string sql = @"SELECT
                              l.M_InventoryLine_ID,
                              l.Line,
                              l.Description     AS LineDescription,
                              l.M_Product_ID,
                              NVL(l.QtyBook, 0)  AS SystemQty,
                              NVL(l.QtyCount, 0) AS CountedQty,
                              " + varExpr + @"                       AS VarianceQty,
                              p.Value           AS ProductCode,
                              p.Name            AS ProductName,
                              asi.Description   AS AttributeSetInstance,
                              loc.Value         AS LocatorCode,
                              COALESCE(loc.LocatorCombination, loc.Bin, loc.Value) AS LocatorName,
                              u.Name            AS UOMName,
                              NVL(u.StdPrecision, 0) AS UOMPrecision,
                              -- The PRODUCT's own unit. QtyBook is written straight
                              -- from M_Storage.QtyOnHand (MInventoryLine.SetQtyBook),
                              -- so the On Hand figure is always in THIS unit — never
                              -- in the line's C_UOM_ID, which the table used to label
                              -- it with. The panel names it beside the figure.
                              bu.Name           AS BaseUOMName,
                              NVL(bu.StdPrecision, 0) AS BaseUOMPrecision,
                              " + rateExpr + @"                       AS UnitRate,
                              NVL(l.QtyCount, 0) * " + rateExpr + @"   AS LineValue
                           FROM M_InventoryLine l
                           LEFT OUTER JOIN M_Product p   ON (p.M_Product_ID   = l.M_Product_ID)
                           LEFT OUTER JOIN C_UOM     u   ON (u.C_UOM_ID        = l.C_UOM_ID)
                           LEFT OUTER JOIN C_UOM     bu  ON (bu.C_UOM_ID       = p.C_UOM_ID)
                           LEFT OUTER JOIN M_Locator loc ON (loc.M_Locator_ID  = l.M_Locator_ID)
                           -- Only a REAL instance is joined: id 0 is the
                           -- dictionary's no-attributes row, whose description is
                           -- a bare double dash that would otherwise print against
                           -- every line carrying no attributes at all.
                           LEFT OUTER JOIN M_AttributeSetInstance asi
                                  ON (asi.M_AttributeSetInstance_ID = l.M_AttributeSetInstance_ID
                                      AND l.M_AttributeSetInstance_ID > 0)
                           WHERE l.M_Inventory_ID = @M_Inventory_ID
                             AND l.IsActive       = 'Y'
                           ORDER BY l.Line";

            SqlParameter[] param = new SqlParameter[]
            {
                new SqlParameter("@M_Inventory_ID", M_Inventory_ID)
            };

            DataSet ds = DB.ExecuteDataset(sql, param, null);
            if (ds == null || ds.Tables.Count == 0)
                return lines;

            foreach (DataRow r in ds.Tables[0].Rows)
            {
                InventoryCountLineData ln = new InventoryCountLineData();
                ln.M_InventoryLine_ID = Util.GetValueOfInt(r["M_InventoryLine_ID"]);
                ln.Line               = Util.GetValueOfInt(r["Line"]);
                ln.Description        = Util.GetValueOfString(r["LineDescription"]);
                ln.M_Product_ID       = Util.GetValueOfInt(r["M_Product_ID"]);
                ln.SystemQty          = Util.GetValueOfDecimal(r["SystemQty"]);
                ln.CountedQty         = Util.GetValueOfDecimal(r["CountedQty"]);
                ln.VarianceQty        = Util.GetValueOfDecimal(r["VarianceQty"]);
                ln.ProductCode        = Util.GetValueOfString(r["ProductCode"]);
                ln.ProductName        = Util.GetValueOfString(r["ProductName"]);
                ln.AttributeSetInstance = Util.GetValueOfString(r["AttributeSetInstance"]);
                ln.LocatorCode        = Util.GetValueOfString(r["LocatorCode"]);
                ln.LocatorName        = Util.GetValueOfString(r["LocatorName"]);
                ln.UOMName            = Util.GetValueOfString(r["UOMName"]);
                ln.UOMPrecision       = Util.GetValueOfInt(r["UOMPrecision"]);
                ln.BaseUOMName        = Util.GetValueOfString(r["BaseUOMName"]);
                ln.BaseUOMPrecision   = Util.GetValueOfInt(r["BaseUOMPrecision"]);
                ln.UnitRate           = Util.GetValueOfDecimal(r["UnitRate"]);
                ln.LineValue          = Util.GetValueOfDecimal(r["LineValue"]);

                lines.Add(ln);
            }
            return lines;
        }

        /// <summary>
        /// Returns true when the given column exists on the given table, using
        /// the AD_Column dictionary. A DB issue degrades to "absent" (false) so a
        /// lookup failure never breaks the overview query.
        /// </summary>
        private bool ColumnExists(string tableName, string columnName)
        {
            try
            {
                string sql = @"SELECT COUNT(*) FROM AD_Column
                                WHERE UPPER(ColumnName) = UPPER(@ColumnName)
                                  AND AD_Table_ID = (SELECT AD_Table_ID FROM AD_Table
                                                      WHERE UPPER(TableName) = UPPER(@TableName))";
                SqlParameter[] param = new SqlParameter[]
                {
                    new SqlParameter("@ColumnName", columnName),
                    new SqlParameter("@TableName", tableName)
                };
                return Util.GetValueOfInt(DB.ExecuteScalar(sql, param, null)) > 0;
            }
            catch (Exception ex)
            {
                _log.Severe("ColumnExists (" + tableName + "." + columnName + "): " + ex.Message);
                return false;
            }
        }

        // ----------------------------------------------------------------- //
        //  Data carriers                                                     //
        // ----------------------------------------------------------------- //

        /// <summary>One note shown in the Notes section: the count header's
        /// description, or the description entered on one of its lines.</summary>
        public class NoteData
        {
            public string NoteType { get; set; }   // header | line
            public string Label    { get; set; }   // "Line 10 — Steel Bolt M8" (line notes)
            public string Text     { get; set; }
        }

        /// <summary>One entry in the count's audit trail.</summary>
        public class ActivityData
        {
            /// <summary>The lifecycle and event types the client tags, matching
            /// VAS_092's set: created | prepared | completed | reactivated |
            /// rejected | approval | voided | reversed | closed | invalidated |
            /// updated (one per changed field) | posted | note | email.</summary>
            public string    Type       { get; set; }
            public string    UserName   { get; set; }   // actor / mail sender
            public DateTime? Created    { get; set; }   // when
            public string    Text       { get; set; }   // note body / e-mail subject

            // Field-level edit (AD_ChangeLog) — WHICH field changed, from what to
            // what, and on which record.
            /// <summary>The dictionary's label for the changed column.</summary>
            public string    FieldName  { get; set; }
            public string    OldValue   { get; set; }
            public string    NewValue   { get; set; }
            /// <summary>Which record the edit landed on: "" for the count header,
            /// else the line's number and product — a count's real edits are its
            /// counted quantities, and those live on the lines.</summary>
            public string    ChangeScope { get; set; }

            // Appointment / task rows (AppointmentsInfo): where the meeting is and
            // whether it has been dealt with. Empty on every other type.
            public string    Location    { get; set; }
            public bool      IsClosed    { get; set; }
            public bool      IsCancelled { get; set; }

            // E-mail (MailAttachment1) — the body is revealed on click. A LETTER is
            // the same record filed under AttachmentType 'I'.
            public string    Body       { get; set; }   // TextMsg (flattened to text)
            public string    MailTo     { get; set; }   // MailAddress
            public string    MailCc     { get; set; }   // MailAddressCc
            public string    MailBcc    { get; set; }   // MailAddressBcc
            public string    MailFrom   { get; set; }   // MailAddressFrom
            public bool      IsMailSent { get; set; }

            /// <summary>The e-mails sent against an APPOINTMENT or TASK itself
            /// (MailAttachment1 anchored on AppointmentsInfo): recipient, subject,
            /// body, when and by whom. Distinct from the mail fields above, which
            /// are correspondence about the COUNT. Empty on every other type; the
            /// bodies travel with the row so the panel reveals them on click
            /// without a second round trip.</summary>
            public List<VAS_ActivityMailRow> Mails { get; set; }
        }

        public class InventoryCountLineData
        {
            public int      M_InventoryLine_ID { get; set; }
            public int      Line               { get; set; }
            public string   Description        { get; set; }
            public int      M_Product_ID       { get; set; }
            public string   ProductCode        { get; set; }   // product search key
            public string   ProductName        { get; set; }
            // M_AttributeSetInstance.Description — the lot / serial / attributes
            // the line was counted against. Blank when it carries none.
            public string   AttributeSetInstance { get; set; }
            public string   LocatorCode        { get; set; }
            public string   LocatorName        { get; set; }
            public string   UOMName            { get; set; }
            public int      UOMPrecision       { get; set; }
            /// <summary>The PRODUCT's own unit (M_Product.C_UOM_ID), which the On
            /// Hand quantity is stored and reported in — QtyBook is copied from
            /// M_Storage.QtyOnHand, never restated into the line's unit.</summary>
            public string   BaseUOMName        { get; set; }
            public int      BaseUOMPrecision   { get; set; }
            public decimal  SystemQty          { get; set; }   // QtyBook
            public decimal  CountedQty         { get; set; }   // QtyCount
            public decimal  VarianceQty        { get; set; }   // counted - system
            public decimal  UnitRate           { get; set; }
            public decimal  LineValue          { get; set; }
        }

        public class InventoryCountOverviewData
        {
            // Header / identity
            public int       M_Inventory_ID { get; set; }
            public string    DocumentNo     { get; set; }
            public string    StatusCode     { get; set; }   // DocStatus code
            public bool      Processed      { get; set; }
            public bool      Posted         { get; set; }
            public DateTime? CountDate      { get; set; }   // MovementDate
            public string    Description    { get; set; }
            public string    WarehouseName  { get; set; }
            public string    CountedBy      { get; set; }
            /// <summary>The document type the count was raised on
            /// (M_Inventory.C_DocType_ID -> C_DocType.Name). "" when the schema
            /// does not carry the column.</summary>
            public string    DocTypeName    { get; set; }

            // Currency
            public int       StdPrecision   { get; set; }

            // KPI aggregates
            public int       LineCount        { get; set; }
            public decimal   TotalValue       { get; set; }
            public decimal   NetVarianceQty   { get; set; }
            /// <summary>What the variance is WORTH — Σ (variance qty x the line's
            /// direction-aware rate). Signed like the quantity: negative is stock
            /// the count could not find.</summary>
            public decimal   VarianceValue    { get; set; }
            public int       MatchedCount     { get; set; }
            public int       ShortCount       { get; set; }
            public int       ExcessCount      { get; set; }
            public int       VarianceLineCount { get; set; }

            // Related documents — the project the count was raised for
            // (M_Inventory.C_Project_ID), when the schema carries the column and
            // the count names one.
            public int       C_Project_ID   { get; set; }
            public string    ProjectNo      { get; set; }   // C_Project.Value
            public string    ProjectName    { get; set; }

            // Related documents — the VA075 maintenance work order the count's
            // lines were raised against (M_InventoryLine.VA075_WorkOrder_ID, or the
            // work order behind VA075_WorkOrderComponent_ID on older rows). All
            // zero / empty on an install without VA075.
            public int       VA075_WorkOrder_ID { get; set; }
            public string    WorkOrderNo        { get; set; }   // VA075_WorkOrder.DocumentNo
            /// <summary>The work order's own reference, under whichever column name
            /// this VA075 revision carries it. "" when it names none.</summary>
            public string    WorkOrderRef       { get; set; }
            /// <summary>How many distinct work orders the count's lines name; the
            /// panel shows the first and counts the rest.</summary>
            public int       WorkOrderCount     { get; set; }

            // Collections
            public List<InventoryCountLineData> Lines    { get; set; }
            public List<NoteData>               Notes    { get; set; }
            public List<ActivityData>           Activity { get; set; }
        }
    }
}
