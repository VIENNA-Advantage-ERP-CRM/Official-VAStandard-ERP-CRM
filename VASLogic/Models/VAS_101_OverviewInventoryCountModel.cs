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
            LoadCountMilestones(M_Inventory_ID, docStatus, activity);
            // What was actually edited, field by field. This is what the reader
            // wants from an audit trail — the coarse "updated" milestone
            // LoadCountMilestones used to add could only report the LAST save and
            // never said what it touched, so it is gone.
            LoadFieldChangeActivity(M_Inventory_ID, activity);
            LoadPostingActivity(M_Inventory_ID, activity);
            LoadNoteActivity(M_Inventory_ID, activity);
            LoadEmailActivity(M_Inventory_ID, activity);

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
            int M_Inventory_ID, string docStatus, List<ActivityData> list)
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

                if (docStatus == "CO" || docStatus == "CL")
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
        /// Silently degrades when change logging is off for the table — there are
        /// simply no rows, and the feed keeps its milestones.
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
                                      u.Name AS UserName
                                 FROM AD_ChangeLog cl
                                INNER JOIN AD_Table adt ON (adt.AD_Table_ID = cl.AD_Table_ID)
                                 LEFT OUTER JOIN AD_Column c ON (c.AD_Column_ID = cl.AD_Column_ID)
                                 LEFT OUTER JOIN AD_User  u  ON (u.AD_User_ID   = cl.CreatedBy)
                                WHERE cl.Record_ID = @M_Inventory_ID
                                  AND adt.TableName = 'M_Inventory'
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
                                WHERE adt.TableName = 'M_InventoryLine'
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

            list.Add(new ActivityData
            {
                Type        = "changed",
                FieldName   = field,
                OldValue    = ChangeValue(Util.GetValueOfString(r["OldValue"])),
                NewValue    = ChangeValue(Util.GetValueOfString(r["NewValue"])),
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
                                  AND adt.TableName = 'M_Inventory'
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
                                  AND adt.TableName = 'M_Inventory'
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
                                WHERE ch.AD_Table_ID =
                                      (SELECT t.AD_Table_ID FROM AD_Table t WHERE t.TableName = 'M_Inventory')
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
                              " + rateExpr + @"                       AS UnitRate,
                              NVL(l.QtyCount, 0) * " + rateExpr + @"   AS LineValue
                           FROM M_InventoryLine l
                           LEFT OUTER JOIN M_Product p   ON (p.M_Product_ID   = l.M_Product_ID)
                           LEFT OUTER JOIN C_UOM     u   ON (u.C_UOM_ID        = l.C_UOM_ID)
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
            /// <summary>created | changed | completed | posted | note | email</summary>
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

            // E-mail (MailAttachment1) — the body is revealed on click.
            public string    Body       { get; set; }   // TextMsg (flattened to text)
            public string    MailTo     { get; set; }   // MailAddress
            public string    MailCc     { get; set; }   // MailAddressCc
            public string    MailBcc    { get; set; }   // MailAddressBcc
            public string    MailFrom   { get; set; }   // MailAddressFrom
            public bool      IsMailSent { get; set; }
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

            // Collections
            public List<InventoryCountLineData> Lines    { get; set; }
            public List<NoteData>               Notes    { get; set; }
            public List<ActivityData>           Activity { get; set; }
        }
    }
}
