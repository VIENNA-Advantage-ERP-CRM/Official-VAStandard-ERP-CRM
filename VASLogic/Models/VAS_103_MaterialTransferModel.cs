/// <summary>
/// Module Name : VASLogic
/// Purpose     : Material Transfer Overview tab panel data (read side). Returns
///               header identity, source / destination warehouses, the derived
///               transfer type (inter- vs intra-warehouse), KPI aggregates (line
///               count / transfer quantity / transfer value / confirmation
///               count), the derived origin flags (requisition / production
///               order) and the transfer lines for a selected stock movement
///               (M_Movement). Per-line value is derived from the movement
///               quantity and item valuation.
/// Chronological development:
///   VAI163   2026-07-07  Created. Optional module columns (CurrentCostPrice,
///                        VA024_UnitPrice, VAMFG_M_WorkOrder_ID,
///                        DTD001_MWarehouseSource_ID) and the optional
///                        M_MovementConfirm table are guarded through the AD
///                        dictionary so the panel works whether or not those
///                        modules are installed.
///   VAI163   2026-08-12  - RequestedBy becomes CreatedByName, and the record's
///                          CREATED stamp travels with it (CreatedOn). The field
///                          was always M_Movement.CreatedBy — who raised the
///                          document — and calling it "Requested by" named a role
///                          the movement does not record.
///                        - Added IncotermName (M_Movement.C_IncoTerm_ID ->
///                          C_IncoTerm), dictionary-guarded on both the column and
///                          the lookup table's display column: the incoterm is a
///                          module column that not every schema carries.
///                        - Added IsConfirmationDocType (C_DocType.IsInTransit) —
///                          the flag that makes the platform raise a movement
///                          confirmation. It decides whether the Confirmations
///                          card and the confirmation lifecycle stages exist at
///                          all, so a plain transfer stops reporting stages it can
///                          never reach.
///                        - Generated From now NAMES its origin instead of
///                          reporting a bare yes/no: the requisition behind a line
///                          (M_MovementLine.M_RequisitionLine_ID ->
///                          M_RequisitionLine -> M_Requisition) and the production
///                          order (VAMFG_M_WorkOrder_ID), each with the id the
///                          panel needs to open it. VAMFG is not part of this
///                          solution, so it is reached by plain SQL under column
///                          guards and its identifier column is probed for.
///                        - Lines carry their Attribute Set Instance, joined only
///                          for a REAL instance (id > 0) so a line with no
///                          attributes cannot pick up the zero-record's "--".
///                        - Added Confirmations (LoadConfirmations) and the
///                          per-line confirmation figures (AttachLineConfirms:
///                          M_MovementLineConfirm's confirmed / difference /
///                          scrapped quantities with the locator the scrap lands
///                          in — VAS_ReceivingLocator_ID where the schema carries
///                          it, else the movement line's own M_LocatorTo_ID, which
///                          is what MMovementConfirm actually moves scrap to).
///                        - Lifecycle dates are read rather than all reported as
///                          MovementDate: CreatedOn (drafted), CompletedDate (the
///                          workflow DocComplete stamp), ConfirmedDate (the
///                          confirmation's own completion — which is also when the
///                          goods were received) and PostedDate (the Created stamp
///                          of the movement's Fact_Acct rows).
///                        - Added Notes (LoadNotes) and Activity (LoadActivity).
///   VAI163   2026-08-14  - Added DocTypeName (M_Movement.C_DocType_ID ->
///                          C_DocType.Name) for the header's right column.
///                          C_DocType was already joined for IsInTransit, so
///                          naming it costs nothing.
///                        - Activity reports edits FIELD BY FIELD
///                          (LoadChangeActivity / AddChangeRow): one "Updated" row
///                          per AD_ChangeLog entry, naming the column that
///                          changed, who changed it and when, for the transfer
///                          header AND its lines — a transfer's substantive edits
///                          are its moved quantities and locators, and those live
///                          on the lines. Until this the feed carried no edit
///                          trail at all: the nearest thing was the "Completed"
///                          milestone, dated from M_Movement.Updated, which is
///                          only ever the LAST save and says nothing about what it
///                          touched. The table name is matched with UPPER, since
///                          an equality on the stored spelling fails silently and
///                          reads exactly like the loader was never written.
///   VAI163   2026-08-17  Field-level activity carries the OLD and NEW values
///                        (AD_ChangeLog.OldValue / NewValue). Both are normalised
///                        through ChangeValue: the literal "null" the platform
///                        writes for a cleared field reads as empty, not as the
///                        word. A row whose two values are equal is dropped — a
///                        save that rewrote a field with the value it already had
///                        is not an edit, and the platform logs plenty of those.
///                        The trail said WHICH field moved but never what it moved
///                        from or to. Follows VAS_101 / VAS_104.
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
using System.Text;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;

namespace VASLogic.Models
{
    public class VAS_103_MaterialTransferModel
    {
        private static readonly VLogger _log =
            VLogger.GetVLogger(typeof(VAS_103_MaterialTransferModel).FullName);

        /// <summary>
        /// Returns the full overview payload for the selected material transfer.
        /// MRole access filtering is applied ONLY on the main physical table
        /// (M_Movement alias "m"); the child line query inherits the parent's
        /// authorization and is not separately role-filtered.
        /// </summary>
        /// <param name="ctx">User context (client/org/role).</param>
        /// <param name="M_Movement_ID">Selected stock movement id.</param>
        /// <returns>Populated <see cref="MaterialTransferOverviewData"/>; an empty
        /// instance when the id is invalid or no accessible row is found.</returns>
        public MaterialTransferOverviewData GetMaterialTransferOverview(Ctx ctx, int M_Movement_ID)
        {
            MaterialTransferOverviewData result = new MaterialTransferOverviewData();
            if (M_Movement_ID <= 0) return result;

            // Optional module columns / table — resolved once so the SQL below
            // only references objects that actually exist in this schema.
            bool hasCurrentCost = ColumnExists("M_MovementLine", "CurrentCostPrice");
            bool hasUnitPrice   = ColumnExists("M_MovementLine", "VA024_UnitPrice");
            bool hasWorkOrder   = ColumnExists("M_MovementLine", "VAMFG_M_WorkOrder_ID");
            bool hasSourceWh    = ColumnExists("M_Movement", "DTD001_MWarehouseSource_ID");
            bool hasConfirm     = ColumnExists("M_MovementConfirm", "M_Movement_ID");
            // The incoterm is a module column, and its lookup table names itself
            // differently across revisions — both are probed for.
            string incotermNameCol = ColumnExists("M_Movement", "C_IncoTerm_ID")
                ? FirstExistingColumn("C_IncoTerm", new string[] { "Name", "Value", "Description" })
                : "";
            // C_DocType.IsInTransit is what makes the platform raise a movement
            // confirmation, so it decides whether this transfer HAS a confirmation
            // stage at all.
            bool hasInTransit = ColumnExists("C_DocType", "IsInTransit");

            // COALESCE([l.CurrentCostPrice], [l.VA024_UnitPrice], 0)
            string rateExpr = BuildRateExpr(hasCurrentCost, hasUnitPrice);
            // Manufacturing work-order id (schema-aware) used to classify origin.
            string woExpr = hasWorkOrder ? "l.VAMFG_M_WorkOrder_ID" : "NULL";
            // Source warehouse id (schema-aware); absent module => no source split.
            string srcWhExpr = hasSourceWh ? "m.DTD001_MWarehouseSource_ID" : "NULL";
            // Confirmation count (schema-aware).
            string confirmExpr = hasConfirm
                ? @"(SELECT COUNT(*) FROM M_MovementConfirm c
                      WHERE c.M_Movement_ID = m.M_Movement_ID
                        AND c.IsActive      = 'Y')"
                : "0";

            // The incoterm's name, and the join that reaches it. Absent on a schema
            // without the column, where both collapse to nothing.
            string incotermExpr = !string.IsNullOrEmpty(incotermNameCol)
                ? "inco." + incotermNameCol : "CAST(NULL AS VARCHAR(255))";
            string incotermJoin = !string.IsNullOrEmpty(incotermNameCol)
                ? "LEFT OUTER JOIN C_IncoTerm inco ON (inco.C_IncoTerm_ID = m.C_IncoTerm_ID)" : "";
            string inTransitExpr = hasInTransit ? "COALESCE(dt.IsInTransit, 'N')" : "'N'";

            string sql = @"SELECT
                              m.M_Movement_ID,
                              m.DocumentNo,
                              m.DocStatus,
                              m.Processed,
                              m.Posted,
                              m.MovementDate,
                              m.Created       AS CreatedOn,
                              m.Description,
                              from_wh.Name     AS FromWarehouseName,
                              to_wh.Name       AS ToWarehouseName,
                              creator.Name     AS CreatedByName,
                              " + incotermExpr   + @" AS IncotermName,
                              " + inTransitExpr  + @" AS IsConfirmationDocType,
                              dt.Name          AS DocTypeName,
                              CASE WHEN " + srcWhExpr + @" = m.M_Warehouse_ID
                                   THEN 'INTRA' ELSE 'INTER' END          AS TransferTypeCode,
                              (SELECT COUNT(*)
                                 FROM M_MovementLine l
                                WHERE l.M_Movement_ID = m.M_Movement_ID
                                  AND l.IsActive      = 'Y')              AS LineCount,
                              (SELECT NVL(SUM(NVL(l.MovementQty, 0)), 0)
                                 FROM M_MovementLine l
                                WHERE l.M_Movement_ID = m.M_Movement_ID
                                  AND l.IsActive      = 'Y')              AS TransferQty,
                              (SELECT NVL(SUM(NVL(l.MovementQty, 0) * " + rateExpr + @"), 0)
                                 FROM M_MovementLine l
                                WHERE l.M_Movement_ID = m.M_Movement_ID
                                  AND l.IsActive      = 'Y')              AS TransferValue,
                              " + confirmExpr + @"                        AS ConfirmationCount,
                              (SELECT MAX(l.M_RequisitionLine_ID)
                                 FROM M_MovementLine l
                                WHERE l.M_Movement_ID = m.M_Movement_ID
                                  AND l.IsActive      = 'Y')              AS SampleRequisitionLineID,
                              (SELECT MAX(" + woExpr + @")
                                 FROM M_MovementLine l
                                WHERE l.M_Movement_ID = m.M_Movement_ID
                                  AND l.IsActive      = 'Y')              AS SampleWorkOrderID
                            FROM M_Movement m
                            LEFT OUTER JOIN M_Warehouse from_wh ON (from_wh.M_Warehouse_ID = " + srcWhExpr + @")
                            LEFT OUTER JOIN M_Warehouse to_wh   ON (to_wh.M_Warehouse_ID   = m.M_Warehouse_ID)
                            LEFT OUTER JOIN AD_User creator     ON (creator.AD_User_ID     = m.CreatedBy)
                            LEFT OUTER JOIN C_DocType dt        ON (dt.C_DocType_ID        = m.C_DocType_ID)
                            " + incotermJoin + @"
                            WHERE m.M_Movement_ID = @M_Movement_ID
                              AND m.IsActive      = 'Y'";

            // MRole access only on the primary physical table the user is fetching.
            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql, "m", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            SqlParameter[] param = new SqlParameter[]
            {
                new SqlParameter("@M_Movement_ID", M_Movement_ID)
            };

            DataSet ds = DB.ExecuteDataset(sql, param, null);
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
                return result;

            DataRow r = ds.Tables[0].Rows[0];

            // ----- Header / identity -----
            result.M_Movement_ID    = Util.GetValueOfInt(r["M_Movement_ID"]);
            result.DocumentNo       = Util.GetValueOfString(r["DocumentNo"]);
            result.StatusCode       = Util.GetValueOfString(r["DocStatus"]);
            result.Processed        = Util.GetValueOfString(r["Processed"]) == "Y";
            result.Posted           = Util.GetValueOfString(r["Posted"]) == "Y";
            result.MovementDate     = Util.GetValueOfDateTime(r["MovementDate"]);
            result.CreatedOn        = Util.GetValueOfDateTime(r["CreatedOn"]);
            result.Description      = Util.GetValueOfString(r["Description"]);
            result.FromWarehouseName = Util.GetValueOfString(r["FromWarehouseName"]);
            result.ToWarehouseName   = Util.GetValueOfString(r["ToWarehouseName"]);
            result.CreatedByName    = Util.GetValueOfString(r["CreatedByName"]);
            result.IncotermName     = Util.GetValueOfString(r["IncotermName"]);
            result.IsConfirmationDocType =
                Util.GetValueOfString(r["IsConfirmationDocType"]) == "Y";
            // The document type the transfer was raised on, for the header's right
            // column. C_DocType is already LEFT joined for IsInTransit, so naming
            // it costs nothing and a transfer without one simply shows no value.
            result.DocTypeName      = Util.GetValueOfString(r["DocTypeName"]);
            result.TransferTypeCode = Util.GetValueOfString(r["TransferTypeCode"]);

            // ----- KPI aggregates -----
            result.LineCount         = Util.GetValueOfInt(r["LineCount"]);
            result.TransferQty       = Util.GetValueOfDecimal(r["TransferQty"]);
            result.TransferValue     = Util.GetValueOfDecimal(r["TransferValue"]);
            result.ConfirmationCount = Util.GetValueOfInt(r["ConfirmationCount"]);

            // ----- Origin: derived from the linked source ids on the lines -----
            result.HasRequisition = Util.GetValueOfInt(r["SampleRequisitionLineID"]) > 0;
            result.HasWorkOrder   = Util.GetValueOfInt(r["SampleWorkOrderID"]) > 0;
            // The origin is NAMED, not just flagged: the strip identifies documents
            // and the panel opens them.
            LoadRequisitionOrigin(M_Movement_ID, result);
            LoadWorkOrderOrigin(M_Movement_ID, woExpr, result);

            // Transfer value is expressed in the accounting currency; the panel
            // renders INR (₹) with standard 2-dp precision.
            result.StdPrecision = 2;

            // ----- Transfer lines -----
            result.Lines = LoadLines(M_Movement_ID, rateExpr, woExpr);

            // ----- Confirmations (only ever raised by an in-transit doc type) -----
            if (hasConfirm)
            {
                result.Confirmations = LoadConfirmations(M_Movement_ID);
                AttachLineConfirms(M_Movement_ID, result.Lines);
                result.ConfirmedDate = GetConfirmationCompletedDate(M_Movement_ID);
                foreach (ConfirmationData c in result.Confirmations)
                {
                    if (c.StatusCode == "CO" || c.StatusCode == "CL")
                    {
                        result.IsConfirmationCompleted = true;
                        break;
                    }
                }
            }

            // ----- Lifecycle dates -----
            result.CompletedDate = GetMovementCompletedDate(M_Movement_ID);
            result.PostedDate    = GetPostedDate(M_Movement_ID);

            // ----- Notes + activity -----
            result.Notes    = LoadNotes(M_Movement_ID);
            result.Activity = LoadActivity(M_Movement_ID);

            return result;
        }

        /// <summary>Single-parameter helper for the movement-scoped queries.</summary>
        private SqlParameter[] MovementParam(int M_Movement_ID)
        {
            return new SqlParameter[] { new SqlParameter("@M_Movement_ID", M_Movement_ID) };
        }

        /// <summary>
        /// Returns the first of the candidate columns that exists on the table, or
        /// "" when the table has none of them. Used where an optional module — or
        /// a dictionary table across revisions — names the same concept
        /// differently.
        /// </summary>
        private string FirstExistingColumn(string tableName, string[] candidates)
        {
            for (int i = 0; i < candidates.Length; i++)
            {
                if (ColumnExists(tableName, candidates[i])) return candidates[i];
            }
            return "";
        }

        // ================================================================= //
        //  Generated from                                                   //
        // ================================================================= //

        /// <summary>
        /// The requisition this transfer was raised from, reached through a line's
        /// M_RequisitionLine_ID. Several lines can carry different requisitions;
        /// the first is named and the rest counted, so the chip can say so.
        /// </summary>
        private void LoadRequisitionOrigin(int M_Movement_ID, MaterialTransferOverviewData d)
        {
            try
            {
                string sql = @"SELECT DISTINCT rq.M_Requisition_ID AS RequisitionId,
                                               rq.DocumentNo       AS RequisitionNo
                                 FROM M_MovementLine l
                                INNER JOIN M_RequisitionLine rl
                                        ON (rl.M_RequisitionLine_ID = l.M_RequisitionLine_ID)
                                INNER JOIN M_Requisition rq
                                        ON (rq.M_Requisition_ID = rl.M_Requisition_ID)
                                WHERE l.M_Movement_ID = @M_Movement_ID
                                  AND l.IsActive      = 'Y'
                                ORDER BY rq.DocumentNo";
                DataSet ds = DB.ExecuteDataset(sql, MovementParam(M_Movement_ID), null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return;

                DataRow r = ds.Tables[0].Rows[0];
                d.RequisitionId    = Util.GetValueOfInt(r["RequisitionId"]);
                d.RequisitionNo    = Util.GetValueOfString(r["RequisitionNo"]);
                d.RequisitionCount = ds.Tables[0].Rows.Count;
                d.HasRequisition   = d.RequisitionId > 0;
            }
            catch (Exception ex)
            {
                _log.Severe("LoadRequisitionOrigin (M_Movement_ID=" + M_Movement_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// The production order this transfer was raised for
        /// (M_MovementLine.VAMFG_M_WorkOrder_ID).
        ///
        /// VAMFG is an optional module and is not part of this solution, so its
        /// table is reached by plain SQL under column guards and its identifier
        /// column is probed for — a deployment without the module reports nothing
        /// and the chip simply says the transfer was not raised from one.
        ///
        /// Read in two steps: the ID comes from the movement's own lines, so an
        /// unreadable VAMFG_M_WorkOrder table cannot suppress the chip — it just
        /// renders without a document number.
        /// </summary>
        private void LoadWorkOrderOrigin(int M_Movement_ID, string woExpr, MaterialTransferOverviewData d)
        {
            if (!ColumnExists("M_MovementLine", "VAMFG_M_WorkOrder_ID")) return;

            // --- Step 1: the work order id(s) the lines carry. ---
            try
            {
                string sql = @"SELECT DISTINCT " + woExpr + @" AS WorkOrderId
                                 FROM M_MovementLine l
                                WHERE l.M_Movement_ID = @M_Movement_ID
                                  AND l.IsActive      = 'Y'
                                  AND COALESCE(" + woExpr + @", 0) > 0";
                DataSet ds = DB.ExecuteDataset(sql, MovementParam(M_Movement_ID), null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return;

                d.WorkOrderId    = Util.GetValueOfInt(ds.Tables[0].Rows[0]["WorkOrderId"]);
                d.WorkOrderCount = ds.Tables[0].Rows.Count;
                d.HasWorkOrder   = d.WorkOrderId > 0;
            }
            catch (Exception ex)
            {
                _log.Severe("LoadWorkOrderOrigin/id (M_Movement_ID=" + M_Movement_ID + "): " + ex.Message);
                return;
            }
            if (d.WorkOrderId <= 0) return;

            // --- Step 2: its document number, if the module's table is readable. ---
            string noCol = FirstExistingColumn("VAMFG_M_WorkOrder",
                new string[] { "DocumentNo", "Name", "Value" });
            if (string.IsNullOrEmpty(noCol)) return;

            try
            {
                string sql = @"SELECT wo." + noCol + @" AS WorkOrderNo
                                 FROM VAMFG_M_WorkOrder wo
                                WHERE wo.VAMFG_M_WorkOrder_ID = @VAMFG_M_WorkOrder_ID";
                SqlParameter[] param = new SqlParameter[]
                {
                    new SqlParameter("@VAMFG_M_WorkOrder_ID", d.WorkOrderId)
                };
                DataSet ds = DB.ExecuteDataset(sql, param, null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return;
                d.WorkOrderNo = Util.GetValueOfString(ds.Tables[0].Rows[0]["WorkOrderNo"]);
            }
            catch (Exception ex)
            {
                _log.Severe("LoadWorkOrderOrigin/no (VAMFG_M_WorkOrder_ID=" + d.WorkOrderId + "): " + ex.Message);
            }
        }

        // ================================================================= //
        //  Confirmations                                                    //
        // ================================================================= //

        /// <summary>
        /// The movement confirmations raised against this transfer
        /// (M_MovementConfirm), newest first. Only an in-transit document type
        /// raises any, so an ordinary transfer reports none.
        /// </summary>
        private List<ConfirmationData> LoadConfirmations(int M_Movement_ID)
        {
            List<ConfirmationData> rows = new List<ConfirmationData>();
            try
            {
                string sql = @"SELECT c.M_MovementConfirm_ID,
                                      c.DocumentNo,
                                      c.DocStatus,
                                      c.Created,
                                      c.Updated,
                                      c.Processed
                                 FROM M_MovementConfirm c
                                WHERE c.M_Movement_ID = @M_Movement_ID
                                  AND c.IsActive      = 'Y'
                                ORDER BY c.Created DESC";
                DataSet ds = DB.ExecuteDataset(sql, MovementParam(M_Movement_ID), null);
                if (ds == null || ds.Tables.Count == 0) return rows;

                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    rows.Add(new ConfirmationData
                    {
                        M_MovementConfirm_ID = Util.GetValueOfInt(r["M_MovementConfirm_ID"]),
                        DocumentNo = Util.GetValueOfString(r["DocumentNo"]),
                        StatusCode = Util.GetValueOfString(r["DocStatus"]),
                        Processed  = Util.GetValueOfString(r["Processed"]) == "Y",
                        Created    = Util.GetValueOfDateTime(r["Created"]),
                        Updated    = Util.GetValueOfDateTime(r["Updated"])
                    });
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadConfirmations (M_Movement_ID=" + M_Movement_ID + "): " + ex.Message);
            }
            return rows;
        }

        /// <summary>
        /// Hangs each line's confirmation figures off the line
        /// (M_MovementLineConfirm: target / confirmed / difference / scrapped),
        /// with the locator the scrap lands in.
        ///
        /// That locator is the confirmation's own VAS_ReceivingLocator_ID where the
        /// schema carries it — the column MMovementLineConfirm pushes onto the
        /// movement line's M_LocatorTo_ID — falling back to the line's
        /// M_LocatorTo_ID, which is exactly where MMovementConfirm moves scrap to.
        ///
        /// Read in ONE query for the whole movement and distributed here: a query
        /// per line would cost a round trip per row.
        /// </summary>
        private void AttachLineConfirms(int M_Movement_ID, List<MaterialTransferLineData> lines)
        {
            if (lines == null || lines.Count == 0) return;
            if (!ColumnExists("M_MovementLineConfirm", "M_MovementLine_ID")) return;

            // The receiving locator overrides the line's destination when the
            // schema has it; without it the line's own destination stands.
            bool hasRecvLoc = ColumnExists("M_MovementLineConfirm", "VAS_ReceivingLocator_ID");
            string locIdExpr = hasRecvLoc
                ? "COALESCE(NULLIF(lc.VAS_ReceivingLocator_ID, 0), l.M_LocatorTo_ID)"
                : "l.M_LocatorTo_ID";

            try
            {
                string sql = @"SELECT lc.M_MovementLine_ID,
                                      COALESCE(lc.TargetQty, 0)     AS TargetQty,
                                      COALESCE(lc.ConfirmedQty, 0)  AS ConfirmedQty,
                                      COALESCE(lc.DifferenceQty, 0) AS DifferenceQty,
                                      COALESCE(lc.ScrappedQty, 0)   AS ScrappedQty,
                                      COALESCE(loc.LocatorCombination, loc.Bin, loc.Value) AS ScrapLocatorName
                                 FROM M_MovementLineConfirm lc
                                INNER JOIN M_MovementLine l
                                        ON (l.M_MovementLine_ID = lc.M_MovementLine_ID)
                                 LEFT OUTER JOIN M_Locator loc
                                        ON (loc.M_Locator_ID = " + locIdExpr + @")
                                WHERE l.M_Movement_ID = @M_Movement_ID
                                  AND lc.IsActive     = 'Y'";
                DataSet ds = DB.ExecuteDataset(sql, MovementParam(M_Movement_ID), null);
                if (ds == null || ds.Tables.Count == 0) return;

                Dictionary<int, MaterialTransferLineData> byId =
                    new Dictionary<int, MaterialTransferLineData>();
                foreach (MaterialTransferLineData ln in lines)
                {
                    if (!byId.ContainsKey(ln.M_MovementLine_ID))
                        byId.Add(ln.M_MovementLine_ID, ln);
                }

                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    MaterialTransferLineData ln;
                    if (!byId.TryGetValue(Util.GetValueOfInt(r["M_MovementLine_ID"]), out ln)) continue;

                    // A line can carry more than one confirmation (a split
                    // movement); the figures accumulate, which is what the totals
                    // on the confirmation documents themselves add up to.
                    ln.HasConfirm       = true;
                    ln.ConfirmTargetQty += Util.GetValueOfDecimal(r["TargetQty"]);
                    ln.ConfirmedQty     += Util.GetValueOfDecimal(r["ConfirmedQty"]);
                    ln.DifferenceQty    += Util.GetValueOfDecimal(r["DifferenceQty"]);
                    ln.ScrappedQty      += Util.GetValueOfDecimal(r["ScrappedQty"]);
                    if (string.IsNullOrEmpty(ln.ScrapLocatorName))
                        ln.ScrapLocatorName = Util.GetValueOfString(r["ScrapLocatorName"]);
                }
            }
            catch (Exception ex)
            {
                _log.Severe("AttachLineConfirms (M_Movement_ID=" + M_Movement_ID + "): " + ex.Message);
            }
        }

        // ================================================================= //
        //  Lifecycle dates                                                  //
        // ================================================================= //

        /// <summary>
        /// The moment the movement was completed: the Created stamp of its workflow
        /// DocComplete activity, falling back to the record's own last change for a
        /// movement that reached CO / CL outside the workflow engine.
        ///
        /// A standalone query, never a subselect of the MRole-rewritten header
        /// SELECT — the role filter rewrites that statement and an added subselect
        /// can fail against it. Follows VAS_106.
        /// </summary>
        private DateTime? GetMovementCompletedDate(int M_Movement_ID)
        {
            try
            {
                string sql = @"SELECT MAX(wfa.Created) AS CompletedDate
                                 FROM AD_WF_Process wfp
                                INNER JOIN AD_WF_Activity wfa
                                        ON (wfa.AD_WF_Process_ID = wfp.AD_WF_Process_ID)
                                INNER JOIN AD_Table adt
                                        ON (adt.AD_Table_ID = wfp.AD_Table_ID)
                                WHERE wfp.Record_ID  = @M_Movement_ID
                                  AND adt.TableName  = 'M_Movement'
                                  AND wfp.IsActive   = 'Y'
                                  AND wfa.IsActive   = 'Y'
                                  AND wfa.WFState    = 'CC'";
                DataSet ds = DB.ExecuteDataset(sql, MovementParam(M_Movement_ID), null);
                if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                {
                    DateTime? d = Util.GetValueOfDateTime(ds.Tables[0].Rows[0]["CompletedDate"]);
                    if (d.HasValue) return d;
                }

                string fallback = @"SELECT m.Updated
                                      FROM M_Movement m
                                     WHERE m.M_Movement_ID = @M_Movement_ID
                                       AND m.DocStatus IN ('CO', 'CL')";
                ds = DB.ExecuteDataset(fallback, MovementParam(M_Movement_ID), null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return null;
                return Util.GetValueOfDateTime(ds.Tables[0].Rows[0]["Updated"]);
            }
            catch (Exception ex)
            {
                _log.Severe("GetMovementCompletedDate (M_Movement_ID=" + M_Movement_ID + "): " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// When the transfer's CONFIRMATION was completed — which is also the moment
        /// the goods were received, since the confirmation is what records their
        /// arrival. Taken from the confirmation's own workflow DocComplete stamp,
        /// falling back to its last change.
        ///
        /// The latest across all of the movement's confirmations: a split movement
        /// raises several and it is not received until the last one lands.
        /// </summary>
        private DateTime? GetConfirmationCompletedDate(int M_Movement_ID)
        {
            try
            {
                string sql = @"SELECT MAX(wfa.Created) AS ConfirmedDate
                                 FROM M_MovementConfirm c
                                INNER JOIN AD_WF_Process wfp
                                        ON (wfp.Record_ID = c.M_MovementConfirm_ID)
                                INNER JOIN AD_Table adt
                                        ON (adt.AD_Table_ID = wfp.AD_Table_ID
                                            AND adt.TableName = 'M_MovementConfirm')
                                INNER JOIN AD_WF_Activity wfa
                                        ON (wfa.AD_WF_Process_ID = wfp.AD_WF_Process_ID
                                            AND wfa.WFState = 'CC'
                                            AND wfa.IsActive = 'Y')
                                WHERE c.M_Movement_ID = @M_Movement_ID
                                  AND c.IsActive      = 'Y'
                                  AND wfp.IsActive    = 'Y'";
                DataSet ds = DB.ExecuteDataset(sql, MovementParam(M_Movement_ID), null);
                if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                {
                    DateTime? d = Util.GetValueOfDateTime(ds.Tables[0].Rows[0]["ConfirmedDate"]);
                    if (d.HasValue) return d;
                }

                // Completed outside the workflow engine — the last change to the
                // completed confirmation is the closest stamp there is.
                string fallback = @"SELECT MAX(c.Updated) AS ConfirmedDate
                                      FROM M_MovementConfirm c
                                     WHERE c.M_Movement_ID = @M_Movement_ID
                                       AND c.IsActive      = 'Y'
                                       AND c.DocStatus IN ('CO', 'CL')";
                ds = DB.ExecuteDataset(fallback, MovementParam(M_Movement_ID), null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return null;
                return Util.GetValueOfDateTime(ds.Tables[0].Rows[0]["ConfirmedDate"]);
            }
            catch (Exception ex)
            {
                _log.Severe("GetConfirmationCompletedDate (M_Movement_ID=" + M_Movement_ID + "): " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// When posting actually ran — the earliest Created stamp across the
        /// Fact_Acct rows written for the movement — or null when it has never been
        /// posted. The accounting date is a document field; this is the event.
        /// </summary>
        private DateTime? GetPostedDate(int M_Movement_ID)
        {
            try
            {
                string sql = @"SELECT MIN(fa.Created) AS PostedDate
                                 FROM Fact_Acct fa
                                INNER JOIN AD_Table adt ON (adt.AD_Table_ID = fa.AD_Table_ID)
                                WHERE fa.Record_ID  = @M_Movement_ID
                                  AND adt.TableName = 'M_Movement'";
                DataSet ds = DB.ExecuteDataset(sql, MovementParam(M_Movement_ID), null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return null;
                return Util.GetValueOfDateTime(ds.Tables[0].Rows[0]["PostedDate"]);
            }
            catch (Exception ex)
            {
                _log.Severe("GetPostedDate (M_Movement_ID=" + M_Movement_ID + "): " + ex.Message);
                return null;
            }
        }

        // ================================================================= //
        //  Notes + activity                                                 //
        // ================================================================= //

        /// <summary>
        /// Loads the transfer's notes: the document's own note
        /// (M_Movement.Description) plus each line's note (product name + the
        /// line's description). Composed in C# so the SQL stays portable.
        /// </summary>
        private List<NoteData> LoadNotes(int M_Movement_ID)
        {
            List<NoteData> notes = new List<NoteData>();
            try
            {
                string sql = @"SELECT m.Description AS HeaderNote,
                                      l.Line        AS LineNo,
                                      l.Description AS LineNote,
                                      p.Name        AS ProductName
                                 FROM M_Movement m
                                 LEFT OUTER JOIN M_MovementLine l
                                        ON (l.M_Movement_ID = m.M_Movement_ID AND l.IsActive = 'Y')
                                 LEFT OUTER JOIN M_Product p ON (p.M_Product_ID = l.M_Product_ID)
                                WHERE m.M_Movement_ID = @M_Movement_ID
                                  AND m.IsActive      = 'Y'
                                ORDER BY l.Line";
                DataSet ds = DB.ExecuteDataset(sql, MovementParam(M_Movement_ID), null);
                if (ds == null || ds.Tables.Count == 0) return notes;

                bool headerAdded = false;
                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    if (!headerAdded)
                    {
                        string headerNote = Util.GetValueOfString(r["HeaderNote"]);
                        if (!string.IsNullOrEmpty(headerNote))
                            notes.Add(new NoteData { NoteType = "header", Text = headerNote.Trim() });
                        headerAdded = true;
                    }

                    string lineNote = Util.GetValueOfString(r["LineNote"]);
                    if (string.IsNullOrEmpty(lineNote)) continue;

                    string prod = Util.GetValueOfString(r["ProductName"]);
                    notes.Add(new NoteData
                    {
                        NoteType = "line",
                        Text = string.IsNullOrEmpty(prod)
                            ? lineNote.Trim() : prod.Trim() + " — " + lineNote.Trim()
                    });
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadNotes (M_Movement_ID=" + M_Movement_ID + "): " + ex.Message);
            }
            return notes;
        }

        /// <summary>
        /// Builds the transfer's activity feed: its chat entries (CM_ChatEntry)
        /// merged with the document's create / complete milestones and the
        /// confirmations raised against it, newest first. Each source is guarded so
        /// a DB-level issue with one degrades to a partial feed.
        /// </summary>
        private List<ActivityData> LoadActivity(int M_Movement_ID)
        {
            // A runaway guard, not a headline count.
            const int MAX_ENTRIES = 200;
            List<ActivityData> activity = new List<ActivityData>();

            LoadNoteActivity(M_Movement_ID, activity);
            LoadMilestoneActivity(M_Movement_ID, activity);
            LoadConfirmActivity(M_Movement_ID, activity);
            // What was actually edited, field by field. Until this the feed said
            // only that the transfer had been created, completed or confirmed —
            // the "Completed" milestone above is dated from M_Movement.Updated, so
            // the nearest thing to an edit trail was one row carrying the LAST
            // save's timestamp and nothing about what it touched.
            LoadChangeActivity(M_Movement_ID, activity);
            // Appointments, tasks, calls, letters AND mails filed against the
            // transfer. Mails come from here too: this panel has no mail loader of
            // its own, so without them the correspondence trail would carry the
            // letters and silently drop the e-mails beside them.
            LoadSharedSourceActivity(M_Movement_ID, activity);

            activity.Sort((a, b) =>
                b.EventTime.GetValueOrDefault(DateTime.MinValue)
                 .CompareTo(a.EventTime.GetValueOrDefault(DateTime.MinValue)));

            if (activity.Count > MAX_ENTRIES)
                activity = activity.GetRange(0, MAX_ENTRIES);
            return activity;
        }

        private void LoadNoteActivity(int M_Movement_ID, List<ActivityData> list)
        {
            try
            {
                // The author resolves from CM_ChatEntry.AD_User_ID falling back to
                // CreatedBy: a note logged by the platform leaves AD_User_ID null,
                // and those appeared in the feed with no name against them.
                string sql = @"SELECT ce.CharacterData, ce.Created, u.Name AS UserName
                                 FROM CM_ChatEntry ce
                                INNER JOIN CM_Chat ch ON (ce.CM_Chat_ID = ch.CM_Chat_ID)
                                 LEFT OUTER JOIN AD_User u
                                        ON (u.AD_User_ID = COALESCE(ce.AD_User_ID, ce.CreatedBy))
                                WHERE ch.AD_Table_ID =
                                      (SELECT t.AD_Table_ID FROM AD_Table t
                                        WHERE t.TableName = 'M_Movement')
                                  AND ch.Record_ID = @M_Movement_ID
                                  AND ce.IsActive  = 'Y'";
                DataSet ds = DB.ExecuteDataset(sql, MovementParam(M_Movement_ID), null);
                if (ds == null || ds.Tables.Count == 0) return;
                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    list.Add(new ActivityData
                    {
                        EventType = "Note",
                        Title     = Util.GetValueOfString(r["CharacterData"]),
                        ActorName = Util.GetValueOfString(r["UserName"]),
                        EventTime = Util.GetValueOfDateTime(r["Created"])
                    });
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadNoteActivity (M_Movement_ID=" + M_Movement_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// One "Updated" row per FIELD the user changed, read from the platform's
        /// change log (AD_ChangeLog). Each row names the field (the dictionary's
        /// display name for the column, falling back to the raw column name), who
        /// changed it and when — so the trail says which field moved rather than
        /// only that something did.
        ///
        /// Both the header (M_Movement) and its LINES (M_MovementLine) are read. A
        /// transfer's substantive edits are its MOVED QUANTITIES and its locators,
        /// and those live on the lines — a header-only trail would report almost
        /// nothing. A line row is labelled with its line number and product so the
        /// reader can tell which row moved.
        ///
        /// The table name is matched with UPPER so a dictionary holding it in
        /// another case still resolves: an equality on the stored spelling fails
        /// SILENTLY, which reads exactly like the loader was never written.
        ///
        /// Silently degrades when change logging is off for the ROLE that made the
        /// edit (AD_Role.IsChangeLog) — the platform writes no AD_ChangeLog rows at
        /// all in that case. That is a dictionary setting, not something this can
        /// fix. Follows VAS_092.
        /// </summary>
        /// <param name="M_Movement_ID">Selected material transfer id.</param>
        /// <param name="list">Activity list being populated.</param>
        private void LoadChangeActivity(int M_Movement_ID, List<ActivityData> list)
        {
            // ----- Header edits (M_Movement) -----
            try
            {
                // AD_Column is LEFT joined so a log row whose column has since been
                // removed from the dictionary still reports its change.
                string sql = @"SELECT cl.Created      AS EventOn,
                                      cl.OldValue     AS OldValue,
                                      cl.NewValue     AS NewValue,
                                      u.Name          AS UserName,
                                      col.Name        AS FieldLabel,
                                      col.ColumnName  AS FieldColumn,
                                      col.AD_Reference_ID       AS RefType,
                                      col.AD_Reference_Value_ID AS RefValueId
                                 FROM AD_ChangeLog cl
                                INNER JOIN AD_Table adt
                                        ON (adt.AD_Table_ID = cl.AD_Table_ID)
                                 LEFT OUTER JOIN AD_Column col
                                        ON (col.AD_Column_ID = cl.AD_Column_ID)
                                 LEFT OUTER JOIN AD_User u
                                        ON (u.AD_User_ID = cl.CreatedBy)
                                WHERE cl.Record_ID = @M_Movement_ID
                                  AND UPPER(adt.TableName) = 'M_MOVEMENT'
                                  AND COALESCE(cl.IsActive, 'Y') = 'Y'
                                ORDER BY cl.Created DESC";
                DataSet ds = DB.ExecuteDataset(sql, MovementParam(M_Movement_ID), null);
                if (ds != null && ds.Tables.Count > 0)
                {
                    foreach (DataRow r in ds.Tables[0].Rows) AddChangeRow(r, "", list);
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadChangeActivity/header (M_Movement_ID=" + M_Movement_ID + "): " + ex.Message);
            }

            // ----- Line edits (M_MovementLine) -----
            //
            // The line ids are reached through a join rather than a sub-select on
            // the same parameter, so the statement carries its bind name exactly
            // once: Oracle binds positionally, and a repeated name becomes a
            // second, unfilled placeholder.
            try
            {
                string sql = @"SELECT cl.Created      AS EventOn,
                                      cl.OldValue     AS OldValue,
                                      cl.NewValue     AS NewValue,
                                      u.Name          AS UserName,
                                      col.Name        AS FieldLabel,
                                      col.ColumnName  AS FieldColumn,
                                      col.AD_Reference_ID       AS RefType,
                                      col.AD_Reference_Value_ID AS RefValueId,
                                      l.Line          AS LineNo,
                                      p.Name          AS ProductName
                                 FROM AD_ChangeLog cl
                                INNER JOIN AD_Table adt
                                        ON (adt.AD_Table_ID = cl.AD_Table_ID)
                                INNER JOIN M_MovementLine l
                                        ON (l.M_MovementLine_ID = cl.Record_ID)
                                 LEFT OUTER JOIN M_Product p
                                        ON (p.M_Product_ID = l.M_Product_ID)
                                 LEFT OUTER JOIN AD_Column col
                                        ON (col.AD_Column_ID = cl.AD_Column_ID)
                                 LEFT OUTER JOIN AD_User u
                                        ON (u.AD_User_ID = cl.CreatedBy)
                                WHERE UPPER(adt.TableName) = 'M_MOVEMENTLINE'
                                  AND COALESCE(cl.IsActive, 'Y') = 'Y'
                                  AND l.M_Movement_ID = @M_Movement_ID
                                ORDER BY cl.Created DESC";
                DataSet ds = DB.ExecuteDataset(sql, MovementParam(M_Movement_ID), null);
                if (ds != null && ds.Tables.Count > 0)
                {
                    foreach (DataRow r in ds.Tables[0].Rows)
                    {
                        // "#10 Steel Bolt M8" — the line number identifies the row,
                        // the product says what it is without a second lookup.
                        string scope = "#" + Util.GetValueOfInt(r["LineNo"]);
                        string prod  = Util.GetValueOfString(r["ProductName"]);
                        if (!string.IsNullOrEmpty(prod)) scope += " " + prod.Trim();
                        AddChangeRow(r, scope, list);
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadChangeActivity/lines (M_Movement_ID=" + M_Movement_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// Turns one AD_ChangeLog row into an "Updated" activity entry. A change
        /// whose column the dictionary cannot name is skipped: it would render as
        /// a bare "Updated" identifying nothing, which is what naming the field
        /// exists to stop.
        /// </summary>
        /// <param name="r">Change-log row.</param>
        /// <param name="scope">Which record the edit landed on: "" for the header,
        /// else the line's number and product.</param>
        /// <param name="list">Activity list being populated.</param>
        private void AddChangeRow(DataRow r, string scope, List<ActivityData> list)
        {
            DateTime? at = Util.GetValueOfDateTime(r["EventOn"]);
            if (!at.HasValue) return;

            string field = Util.GetValueOfString(r["FieldLabel"]);
            if (string.IsNullOrEmpty(field))
                field = Util.GetValueOfString(r["FieldColumn"]);
            if (string.IsNullOrEmpty(field)) return;

            // The move itself. A save that rewrites a field with the value it
            // already had is not an edit, and the platform logs plenty of those.
            // Compared on the RAW values, before either is resolved: two records
            // can share a name, and dropping such a row would hide a real edit.
            string oldValue = ChangeValue(Util.GetValueOfString(r["OldValue"]));
            string newValue = ChangeValue(Util.GetValueOfString(r["NewValue"]));
            if (string.Equals(oldValue, newValue, StringComparison.Ordinal)) return;

            // ... and then reported as the field SHOWS them, not as the log stored
            // them: a reference reads as the referenced record's identifier, a list
            // value as its label, a date as the date alone.
            string column  = Util.GetValueOfString(r["FieldColumn"]);
            int refType    = Util.GetValueOfInt(r["RefType"]);
            int refValueId = Util.GetValueOfInt(r["RefValueId"]);

            list.Add(new ActivityData
            {
                EventType   = "Updated",
                FieldName   = field,
                OldValue    = _changeValues.Display(oldValue, column, refType, refValueId),
                NewValue    = _changeValues.Display(newValue, column, refType, refValueId),
                ChangeScope = scope,
                ActorName   = Util.GetValueOfString(r["UserName"]),
                EventTime   = at
            });
        }

        /// <summary>
        /// Resolves a change-log value into the text the field shows — a reference
        /// into the referenced record's identifier, a list code into its label, a
        /// timestamp into the date alone. Shared with the other overview panels
        /// (VAS_ChangeLogValueModel). One per request, so its caches last exactly
        /// as long as the feed being built.
        /// </summary>
        private readonly VAS_ChangeLogValueModel _changeValues = new VAS_ChangeLogValueModel();

        /// <summary>Reads the appointment / task / call / letter / mail sources
        /// every overview panel shares (VAS_ActivitySourcesModel).</summary>
        private readonly VAS_ActivitySourcesModel _activitySources = new VAS_ActivitySourcesModel();

        /// <summary>
        /// The correspondence and engagement sources shared with every other
        /// overview panel: appointments and tasks (AppointmentsInfo, split on
        /// IsTask), calls (VA048_CallDetails), and the letters and mails
        /// MailAttachment1 holds, split on AttachmentType. Each is pinned to the
        /// transfer by AD_Table_ID + Record_ID.
        ///
        /// Mails are read HERE rather than by a loader of this panel's own — it
        /// never had one, so its trail reported no correspondence at all.
        /// </summary>
        private void LoadSharedSourceActivity(int M_Movement_ID, List<ActivityData> list)
        {
            List<VAS_ActivitySourceRow> rows =
                _activitySources.Load("M_Movement", M_Movement_ID, true);
            foreach (VAS_ActivitySourceRow s in rows)
            {
                list.Add(new ActivityData
                {
                    // appointment | task | call | letter | email
                    EventType   = s.Kind,
                    Title       = s.Title,
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
                    ActorName   = s.ActorName,
                    EventTime   = s.EventTime
                });
            }
        }

        /// <summary>
        /// Normalises a logged value for display. The platform writes the literal
        /// "null" into AD_ChangeLog for a cleared field, which would otherwise be
        /// shown to the reader as though it were the text "null". Follows VAS_101.
        /// </summary>
        private static string ChangeValue(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            string v = value.Trim();
            return string.Equals(v, "null", StringComparison.OrdinalIgnoreCase) ? "" : v;
        }

        private void LoadMilestoneActivity(int M_Movement_ID, List<ActivityData> list)
        {
            try
            {
                string sql = @"SELECT m.Created, m.Updated, m.DocStatus, m.DocumentNo,
                                      cu.Name AS CreatedByName, uu.Name AS UpdatedByName
                                 FROM M_Movement m
                                 LEFT OUTER JOIN AD_User cu ON (m.CreatedBy = cu.AD_User_ID)
                                 LEFT OUTER JOIN AD_User uu ON (m.UpdatedBy = uu.AD_User_ID)
                                WHERE m.M_Movement_ID = @M_Movement_ID";
                DataSet ds = DB.ExecuteDataset(sql, MovementParam(M_Movement_ID), null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return;

                DataRow r = ds.Tables[0].Rows[0];
                list.Add(new ActivityData
                {
                    EventType = "Created",
                    Title     = Util.GetValueOfString(r["DocumentNo"]),
                    ActorName = Util.GetValueOfString(r["CreatedByName"]),
                    EventTime = Util.GetValueOfDateTime(r["Created"])
                });

                string docStatus = Util.GetValueOfString(r["DocStatus"]);
                if (docStatus == "CO" || docStatus == "CL")
                {
                    list.Add(new ActivityData
                    {
                        EventType = "Completed",
                        Title     = Util.GetValueOfString(r["DocumentNo"]),
                        ActorName = Util.GetValueOfString(r["UpdatedByName"]),
                        EventTime = Util.GetValueOfDateTime(r["Updated"])
                    });
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadMilestoneActivity (M_Movement_ID=" + M_Movement_ID + "): " + ex.Message);
            }
        }

        private void LoadConfirmActivity(int M_Movement_ID, List<ActivityData> list)
        {
            if (!ColumnExists("M_MovementConfirm", "M_Movement_ID")) return;
            try
            {
                string sql = @"SELECT c.DocumentNo, c.DocStatus, c.Created, c.Updated,
                                      uu.Name AS UpdatedByName
                                 FROM M_MovementConfirm c
                                 LEFT OUTER JOIN AD_User uu ON (c.UpdatedBy = uu.AD_User_ID)
                                WHERE c.M_Movement_ID = @M_Movement_ID
                                  AND c.IsActive      = 'Y'";
                DataSet ds = DB.ExecuteDataset(sql, MovementParam(M_Movement_ID), null);
                if (ds == null || ds.Tables.Count == 0) return;

                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    string status = Util.GetValueOfString(r["DocStatus"]);
                    bool done = status == "CO" || status == "CL";
                    list.Add(new ActivityData
                    {
                        EventType = done ? "Confirmed" : "Confirmation",
                        Title     = Util.GetValueOfString(r["DocumentNo"]),
                        ActorName = Util.GetValueOfString(r["UpdatedByName"]),
                        EventTime = done
                            ? Util.GetValueOfDateTime(r["Updated"])
                            : Util.GetValueOfDateTime(r["Created"])
                    });
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadConfirmActivity (M_Movement_ID=" + M_Movement_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// Resolves a window's AD_Window_ID from its name (AD_Window.Name) for the
        /// panel's record-open path — the Generated From chips open the requisition
        /// and the production order. Restricted to windows this tenant can see,
        /// preferring the tenant's own row over the system one. Ported from VAS_106.
        /// </summary>
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
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return 0;
                return Util.GetValueOfInt(ds.Tables[0].Rows[0]["AD_Window_ID"]);
            }
            catch (Exception ex)
            {
                _log.Severe("GetWindowId (" + windowName + "): " + ex.Message);
                return 0;
            }
        }

        /// <summary>
        /// The window a TABLE's records open in: the table's own zoom target
        /// (AD_Table.AD_Window_ID), falling back to the first window with a tab on
        /// it. The Production Order chip needs this — VAMFG_M_WorkOrder is
        /// maintained by a module window whose name cannot be hard-coded here, and
        /// the browser-side zoom lookup only knows tables the client has cached.
        /// Ported from VAS_106.
        /// </summary>
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
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return 0;
                return Util.GetValueOfInt(ds.Tables[0].Rows[0]["AD_Window_ID"]);
            }
            catch (Exception ex)
            {
                _log.Severe("GetWindowIdByTable (" + name + "): " + ex.Message);
                return 0;
            }
        }

        /// <summary>
        /// Builds the unit-rate SQL expression from whichever optional cost
        /// columns exist on M_MovementLine, ending at 0.
        /// </summary>
        private string BuildRateExpr(bool hasCurrentCost, bool hasUnitPrice)
        {
            List<string> cols = new List<string>();
            if (hasCurrentCost) cols.Add("l.CurrentCostPrice");
            if (hasUnitPrice)   cols.Add("l.VA024_UnitPrice");
            if (cols.Count == 0) return "0";

            StringBuilder sb = new StringBuilder("COALESCE(");
            sb.Append(string.Join(", ", cols));
            sb.Append(", 0)");
            return sb.ToString();
        }

        /// <summary>
        /// Loads M_MovementLine rows for the transfer with product, from/to
        /// locator and UOM metadata, movement quantity, a unit rate / line value
        /// and the linked source ids used to classify per-line origin. Child of an
        /// already authorized movement, so no separate MRole filter is applied.
        /// </summary>
        /// <param name="M_Movement_ID">Owning stock movement id.</param>
        /// <param name="rateExpr">Unit-rate SQL expression (schema-aware).</param>
        /// <param name="woExpr">Work-order-id SQL expression (schema-aware).</param>
        /// <returns>Ordered list of line rows (may be empty).</returns>
        private List<MaterialTransferLineData> LoadLines(
            int M_Movement_ID, string rateExpr, string woExpr)
        {
            List<MaterialTransferLineData> lines = new List<MaterialTransferLineData>();

            string sql = @"SELECT
                              l.M_MovementLine_ID,
                              l.Line,
                              l.Description     AS LineDescription,
                              l.M_Product_ID,
                              p.Value           AS ProductCode,
                              p.Name            AS ProductName,
                              asi.Description   AS AttributeSetInstance,
                              from_loc.Value    AS FromLocatorCode,
                              COALESCE(from_loc.LocatorCombination, from_loc.Bin, from_loc.Value) AS FromLocatorName,
                              to_loc.Value      AS ToLocatorCode,
                              COALESCE(to_loc.LocatorCombination, to_loc.Bin, to_loc.Value)       AS ToLocatorName,
                              u.Name            AS UOMName,
                              NVL(u.StdPrecision, 0) AS UOMPrecision,
                              NVL(l.MovementQty, 0) AS MovementQty,
                              " + rateExpr + @"                        AS UnitRate,
                              NVL(l.MovementQty, 0) * " + rateExpr + @" AS LineValue,
                              l.M_RequisitionLine_ID AS RequisitionLineID,
                              " + woExpr + @"                          AS WorkOrderID
                           FROM M_MovementLine l
                           LEFT OUTER JOIN M_Product p     ON (p.M_Product_ID    = l.M_Product_ID)
                           LEFT OUTER JOIN C_UOM     u     ON (u.C_UOM_ID         = l.C_UOM_ID)
                           LEFT OUTER JOIN M_Locator from_loc ON (from_loc.M_Locator_ID = l.M_Locator_ID)
                           LEFT OUTER JOIN M_Locator to_loc   ON (to_loc.M_Locator_ID   = l.M_LocatorTo_ID)
                           -- Only a REAL instance is joined: id 0 is the
                           -- dictionary's no-attributes row, whose description is a
                           -- bare double dash that would otherwise print against
                           -- every line carrying no attributes at all.
                           LEFT OUTER JOIN M_AttributeSetInstance asi
                                  ON (asi.M_AttributeSetInstance_ID = l.M_AttributeSetInstance_ID
                                      AND l.M_AttributeSetInstance_ID > 0)
                           WHERE l.M_Movement_ID = @M_Movement_ID
                             AND l.IsActive      = 'Y'
                           ORDER BY l.Line";

            SqlParameter[] param = new SqlParameter[]
            {
                new SqlParameter("@M_Movement_ID", M_Movement_ID)
            };

            DataSet ds = DB.ExecuteDataset(sql, param, null);
            if (ds == null || ds.Tables.Count == 0)
                return lines;

            foreach (DataRow r in ds.Tables[0].Rows)
            {
                MaterialTransferLineData ln = new MaterialTransferLineData();
                ln.M_MovementLine_ID = Util.GetValueOfInt(r["M_MovementLine_ID"]);
                ln.Line              = Util.GetValueOfInt(r["Line"]);
                ln.Description       = Util.GetValueOfString(r["LineDescription"]);
                ln.M_Product_ID      = Util.GetValueOfInt(r["M_Product_ID"]);
                ln.ProductCode       = Util.GetValueOfString(r["ProductCode"]);
                ln.ProductName       = Util.GetValueOfString(r["ProductName"]);
                ln.AttributeSetInstance = Util.GetValueOfString(r["AttributeSetInstance"]);
                ln.FromLocatorCode   = Util.GetValueOfString(r["FromLocatorCode"]);
                ln.FromLocatorName   = Util.GetValueOfString(r["FromLocatorName"]);
                ln.ToLocatorCode     = Util.GetValueOfString(r["ToLocatorCode"]);
                ln.ToLocatorName     = Util.GetValueOfString(r["ToLocatorName"]);
                ln.UOMName           = Util.GetValueOfString(r["UOMName"]);
                ln.UOMPrecision      = Util.GetValueOfInt(r["UOMPrecision"]);
                ln.MovementQty       = Util.GetValueOfDecimal(r["MovementQty"]);
                ln.UnitRate          = Util.GetValueOfDecimal(r["UnitRate"]);
                ln.LineValue         = Util.GetValueOfDecimal(r["LineValue"]);
                ln.RequisitionLineID = Util.GetValueOfInt(r["RequisitionLineID"]);
                ln.WorkOrderID       = Util.GetValueOfInt(r["WorkOrderID"]);

                lines.Add(ln);
            }
            return lines;
        }

        /// <summary>
        /// Returns true when the given column exists on the given table, using
        /// the AD_Column dictionary. A DB issue degrades to "absent" (false) so a
        /// lookup failure never breaks the overview query. Also used to probe for
        /// the presence of an optional table (via one of its columns).
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

        public class MaterialTransferLineData
        {
            public int      M_MovementLine_ID { get; set; }
            public int      Line              { get; set; }
            public string   Description       { get; set; }
            public int      M_Product_ID      { get; set; }
            public string   ProductCode       { get; set; }   // M_Product.Value
            public string   ProductName       { get; set; }
            /// <summary>The line's attribute set instance (lot / serial /
            /// attributes), "" when the line carries none.</summary>
            public string   AttributeSetInstance { get; set; }
            public string   FromLocatorCode   { get; set; }
            public string   FromLocatorName   { get; set; }
            public string   ToLocatorCode     { get; set; }
            public string   ToLocatorName     { get; set; }
            public string   UOMName           { get; set; }
            public int      UOMPrecision      { get; set; }
            public decimal  MovementQty       { get; set; }
            public decimal  UnitRate          { get; set; }
            public decimal  LineValue         { get; set; }
            public int      RequisitionLineID { get; set; }
            public int      WorkOrderID       { get; set; }

            // ---- Confirmation figures (M_MovementLineConfirm) ----
            /// <summary>True when a confirmation line exists for this row — which
            /// is the only case the panel shows confirmation figures for.</summary>
            public bool     HasConfirm        { get; set; }
            public decimal  ConfirmTargetQty  { get; set; }
            public decimal  ConfirmedQty      { get; set; }
            public decimal  DifferenceQty     { get; set; }
            public decimal  ScrappedQty       { get; set; }
            /// <summary>Where the scrapped quantity lands. Only meaningful when
            /// something was actually scrapped.</summary>
            public string   ScrapLocatorName  { get; set; }
        }

        /// <summary>One movement confirmation raised against the transfer.</summary>
        public class ConfirmationData
        {
            public int       M_MovementConfirm_ID { get; set; }
            public string    DocumentNo { get; set; }
            public string    StatusCode { get; set; }   // DocStatus code
            public bool      Processed  { get; set; }
            public DateTime? Created    { get; set; }
            public DateTime? Updated    { get; set; }
        }

        /// <summary>One note: the transfer's own, or one of its lines'.</summary>
        public class NoteData
        {
            public string NoteType { get; set; }   // header | line
            public string Text     { get; set; }
        }

        /// <summary>One activity entry. EventType drives the tag + icon the client
        /// renders: Note | Created | Completed | Confirmation | Confirmed.</summary>
        public class ActivityData
        {
            public string    EventType { get; set; }
            public string    Title     { get; set; }
            public string    ActorName { get; set; }
            public DateTime? EventTime { get; set; }
            /// <summary>For an "Updated" row: the dictionary's label for the
            /// column that changed.</summary>
            public string    FieldName   { get; set; }
            /// <summary>For an "Updated" row: which record the edit landed on —
            /// "" for the transfer header, else the line's number and product
            /// ("#10 Steel Bolt M8"). A transfer's substantive edits are its moved
            /// quantities and locators, and those live on the lines.</summary>
            public string    ChangeScope { get; set; }
            // The move itself, for an "updated" row: what the field held
            // before the edit and what it holds after. Either side is empty
            // where the log recorded no value — a field cleared, or filled
            // for the first time.
            public string    OldValue    { get; set; }
            public string    NewValue    { get; set; }

            // The shared correspondence / engagement sources
            // (VAS_ActivitySourcesModel). Empty on every other event type.
            /// <summary>Appointment / task: where the meeting is.</summary>
            public string    Location    { get; set; }
            public bool      IsClosed    { get; set; }
            public bool      IsCancelled { get; set; }
            /// <summary>Who it reached: a mail's or letter's recipient, a call's
            /// number.</summary>
            public string    MailTo      { get; set; }
            // The rest of a mail's / letter's envelope, and its body — carried
            // with the row so the panel can reveal the message on click without a
            // second round trip. The body arrives already flattened to text
            // (VAS_ActivitySourcesModel), since a mail sent as HTML stores markup.
            public string    MailCc      { get; set; }
            public string    MailBcc     { get; set; }
            public string    MailFrom    { get; set; }
            public bool      IsMailSent  { get; set; }
            public string    Body        { get; set; }

            /// <summary>The e-mails sent against an APPOINTMENT or TASK itself
            /// (MailAttachment1 anchored on AppointmentsInfo): recipient, subject,
            /// body, when and by whom. Distinct from the mail fields above, which
            /// are correspondence about the TRANSFER. Empty on every other event
            /// type; the bodies travel with the row so the panel reveals them on
            /// click without a second round trip.</summary>
            public List<VAS_ActivityMailRow> Mails { get; set; }
        }

        public class MaterialTransferOverviewData
        {
            // Header / identity
            public int       M_Movement_ID     { get; set; }
            public string    DocumentNo        { get; set; }
            public string    StatusCode        { get; set; }   // DocStatus code
            public bool      Processed         { get; set; }
            public bool      Posted            { get; set; }
            public DateTime? MovementDate      { get; set; }
            /// <summary>When the transfer was raised — the record's own Created
            /// stamp, which dates the lifecycle's Drafted stage.</summary>
            public DateTime? CreatedOn         { get; set; }
            public string    Description       { get; set; }
            public string    FromWarehouseName { get; set; }
            public string    ToWarehouseName   { get; set; }
            /// <summary>Who raised the document (M_Movement.CreatedBy).</summary>
            public string    CreatedByName     { get; set; }
            /// <summary>The incoterm on the transfer, "" when the schema does not
            /// carry the column or the record has none.</summary>
            public string    IncotermName      { get; set; }
            public string    TransferTypeCode  { get; set; }   // INTER | INTRA

            /// <summary>C_DocType.IsInTransit — this document type raises a
            /// movement confirmation. Without it the transfer has no confirmation
            /// stage at all, and the panel shows neither the card nor the stages.
            /// </summary>
            public bool      IsConfirmationDocType  { get; set; }
            /// <summary>The document type the transfer was raised on
            /// (M_Movement.C_DocType_ID -> C_DocType.Name), for the header's right
            /// column. "" when the movement names none.</summary>
            public string    DocTypeName            { get; set; }
            /// <summary>True once one of the transfer's confirmations has been
            /// completed.</summary>
            public bool      IsConfirmationCompleted { get; set; }

            // Lifecycle dates
            /// <summary>The workflow DocComplete stamp, else the record's last
            /// change for a movement completed outside the engine.</summary>
            public DateTime? CompletedDate  { get; set; }
            /// <summary>When the CONFIRMATION was completed — which is also when
            /// the goods were received.</summary>
            public DateTime? ConfirmedDate  { get; set; }
            /// <summary>When posting actually ran (Fact_Acct), not the accounting
            /// date the document carries.</summary>
            public DateTime? PostedDate     { get; set; }

            // Origin (derived from the linked source ids on the lines)
            public bool      HasRequisition { get; set; }
            public bool      HasWorkOrder   { get; set; }
            /// <summary>The requisition behind one of the lines, named and
            /// openable. RequisitionCount says how many distinct ones there are.
            /// </summary>
            public int       RequisitionId    { get; set; }
            public string    RequisitionNo    { get; set; }
            public int       RequisitionCount { get; set; }
            /// <summary>The production order the transfer was raised for. Empty on
            /// a deployment without the VAMFG module.</summary>
            public int       WorkOrderId    { get; set; }
            public string    WorkOrderNo    { get; set; }
            public int       WorkOrderCount { get; set; }

            // Currency
            public int       StdPrecision   { get; set; }

            // KPI aggregates
            public int       LineCount         { get; set; }
            public decimal   TransferQty       { get; set; }
            public decimal   TransferValue     { get; set; }
            public int       ConfirmationCount { get; set; }

            // Collections
            public List<MaterialTransferLineData> Lines { get; set; }
            public List<ConfirmationData> Confirmations { get; set; }
            public List<NoteData>         Notes         { get; set; }
            public List<ActivityData>     Activity      { get; set; }
        }
    }
}
