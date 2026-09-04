/// <summary>
/// Module Name : VASLogic
/// Purpose     : Internal Use / Material Issue Overview tab panel data (read
///               side). Returns header identity, warehouse, KPI aggregates
///               (issued value / quantity issued / not-fully-issued count /
///               total lines), the derived origin (production order /
///               requisition / manual issue) and the issue lines for a selected
///               internal-use material issue (M_Inventory where IsInternalUse is
///               'Y'). Per-line issued value and available stock are derived from
///               line quantities, item valuation and current storage.
/// Chronological development:
///   VAI163   2026-07-07  Created. Optional module columns (CurrentCostPrice,
///                        VA024_UnitPrice, VAMFG_M_WorkOrder_ID) are guarded
///                        through AD_Column so the panel works whether or not
///                        those modules are installed.
///   VAI163   2026-07-29  - Requested quantity now reads from the linked
///                          requisition line (M_RequisitionLine.Qty) whenever the
///                          issue line was created from a requisition, falling
///                          back to M_InventoryLine.QtyEntered for a manual issue.
///                          The header RequestedQty / NotFullCount aggregates use
///                          the same expression.
///                        - Added NotFullQty (the requested-minus-issued shortfall
///                          across the issue) for the panel's quantity cards.
///                        - Added the reference documents the panel's References
///                          section needs: the linked requisitions (number, doc /
///                          required dates, description, preparer) and, when the
///                          manufacturing module is installed, the work-order
///                          document no.
///   VAI163   2026-07-29  - Per-line unit rate now resolves from M_CostDetail:
///                          the cost written for the issue line itself, else the
///                          product's latest cost at the issue's warehouse, else
///                          the previous CurrentCostPrice / VA024_UnitPrice
///                          columns. TotalValue is recomputed from the lines so
///                          the KPI, footer and rows always agree.
///                        - Added CreatedDate / UpdatedDate, CompletedDate (the
///                          workflow DocComplete stamp, falling back to Updated)
///                          and PostedDate (earliest Fact_Acct row) for the
///                          panel's timeline.
///                        - Added Activity: the issue's audit trail (created,
///                          updated, completed, posted, chat notes) merged
///                          newest-first. Each source is separately guarded so one
///                          DB problem degrades to a partial trail.
///   VAI163   2026-07-29  - Work order origin now reads the VA075 service module:
///                          M_InventoryLine.VA075_WorkOrder_ID (falling back to the
///                          work order behind VA075_WorkOrderComponent_ID for rows
///                          saved before that column was stamped). The panel gets
///                          the work order's document no and reference, and the
///                          origin reads WORKORDER instead of MANUAL. All of it is
///                          AD_Column-guarded, so a schema without VA075 behaves
///                          exactly as before.
///                        - Added the line's Attribute Set Instance description.
///   VAI163   2026-08-03  - Activity now includes the e-mails sent against the
///                          issue (MailAttachment1 by AD_Table_ID = M_Inventory +
///                          Record_ID): recipient (MailAddress), subject (Title),
///                          body (TextMsg), when (Created) and who sent it
///                          (CreatedBy). The body travels with the row so the
///                          panel can reveal it on click without a second round
///                          trip, and an HTML mail is flattened to plain text
///                          (MailBodyToText) so the panel never has to render
///                          markup. The feed cap rose to 50 so a well-mailed
///                          issue does not push its own milestones off the trail.
///   VAI163   2026-08-03  - The e-mail lookup no longer filters on
///                          MailAttachment1.AttachmentType. That column's value
///                          varies between installations, and requiring 'M' hid
///                          mails that were really there. A row now counts as an
///                          e-mail when it carries a recipient address, which is
///                          what makes it one; the table id is matched with
///                          IN + UPPER so a differently-cased or duplicated
///                          dictionary entry still resolves.
///   VAI163   2026-08-05  - Added Notes (LoadNotes / NoteData, mirroring the
///                          Purchase Order overview): the issue header's
///                          description followed by the description entered on
///                          each line's child tab (M_InventoryLine.Description),
///                          labelled with the line no and product / charge. The
///                          header note was previously the only one the panel
///                          could show, so a note typed against a line was
///                          invisible.
///                        - Chat-note activity resolves its author from
///                          CM_ChatEntry.AD_User_ID falling back to CreatedBy —
///                          a note logged through the platform's own chat
///                          plumbing leaves AD_User_ID null, which printed a
///                          comment with a timestamp but no commenter name.
///   VAI163   2026-08-05  - Line quantities and rates are reported in the line's
///                          SELECTED UOM (C_UOM_ID). QtyEntered is already on
///                          that scale; QtyInternalUse, the requisition Qty and
///                          the M_Storage on-hand are in the product's BASE UOM,
///                          so the panel was labelling base figures with the
///                          selected unit's name. Each line converts with its
///                          own QtyEntered / QtyInternalUse ratio, and the unit
///                          rate is restated per selected unit so the line VALUE
///                          is unchanged.
///                        - The KPI aggregates (LineCount / RequestedQty /
///                          IssuedQty / NotFullCount / NotFullQty / TotalValue)
///                          are now summed from the loaded lines instead of read
///                          from the header SQL, which still counts in base UOM
///                          — the cards and the rows have to agree.
///                        - The manufacturing production order
///                          (VAMFG_M_WorkOrder_ID) travels in its own fields
///                          (ProductionOrderNo / VAMFG_M_WorkOrder_ID /
///                          ProductionOrderCount) via LoadProductionOrder. It
///                          used to be written into WorkOrderNo, which made the
///                          panel label a production order "Work Order".
///   VAI163   2026-08-11  - The e-mail lookup's "has a recipient" filter tested
///                          COALESCE(TRIM(addr), '') &lt;&gt; '', which on Oracle compares
///                          against NULL (the empty string IS NULL there) and is
///                          UNKNOWN for every row — so the Activity feed showed
///                          no e-mails at all on an Oracle deployment. It now
///                          tests against a space, which is non-null on Oracle
///                          and still blank-equal to '' on SQL Server.
///                        - The activity cap rose from 50 to 200. The panel pages
///                          the feed 15 rows at a time, so entries beyond the cap
///                          are unreachable rather than just further down.
///   VAI163   2026-08-11  - Requested quantity on a line raised from a VA075 work
///                          order now reads the work order's own spare part /
///                          service row (VA075_WorkOrderComponent.Quantity, via
///                          M_InventoryLine.VA075_WorkOrderComponent_ID) instead
///                          of the requisition / keyed fallback, which on such a
///                          line mirrors the ISSUED quantity — the row read
///                          "requested == issued" however little was issued.
///                          LoadWorkOrderComponentQtys, AD_Column-guarded.
///                        - Added the project the issue was raised for
///                          (M_Inventory.C_Project_ID → C_Project value / name).
///                          An issue linked to a project and nothing else was
///                          classified MANUAL and read "Manual Issue"; the origin
///                          now falls to PROJECT before MANUAL and the panel draws
///                          a Project chip that opens the project record. The
///                          column is guarded, so an install without C_Project_ID
///                          on M_Inventory behaves exactly as before.
///                        - GetWindowId matches AD_Window.Name case-insensitively.
///                          An exact match turned a differently-cased dictionary
///                          entry into a silent "no such window", which the panel
///                          can only report as a "Cannot open" toast.
///   VAI163   2026-08-13  - The VA075 work-order lookup no longer hides behind
///                          ColumnExists. VA075 is NOT part of this solution, so
///                          its AD_Table / AD_Column rows are whatever the
///                          module's own install left behind — and the guard
///                          answers "absent" for a table it cannot find, or
///                          raises (which the catch turns into "absent") when
///                          AD_Table holds more than one row for the name. Either
///                          way the panel reported no work order and the
///                          Reference section read "Manual Issue" even though
///                          M_InventoryLine.VA075_WorkOrder_ID held a good id.
///                          The two lookups (direct, then the older
///                          component-based one) are now ATTEMPTED, each with its
///                          own once-per-process flag, so an install genuinely
///                          without VA075 reports it once.
///                        - On hand is reported in the PRODUCT'S BASE UOM — the
///                          unit M_Storage.QtyOnHand is stored in — instead of
///                          being restated into the line's entered uom, which
///                          made the figure disagree with every other place the
///                          product's stock is shown. The base unit's name and
///                          precision travel with the line so the column can
///                          name the scale it is on.
///                        - Activity reports header edits FIELD BY FIELD
///                          (LoadChangeActivity): one "updated" row per
///                          AD_ChangeLog entry, naming the column that changed,
///                          who changed it and when. The generic header-stamp
///                          "updated" row survives only where change logging is
///                          off for M_Inventory.
///   VAI163   2026-08-14  Those field-by-field rows now cover the LINES as well as
///                        the header (LoadChangeActivity reads AD_ChangeLog for
///                        M_InventoryLine too, joined to this issue's lines). An
///                        issue's substantive edits are its ISSUED QUANTITIES, and
///                        those live on the lines — reading only M_Inventory
///                        reported nothing at all for the change a reader most
///                        wants to trace, so a corrected quantity left no record.
///                        Each row carries the line it landed on (ChangeScope:
///                        line number + product). Both passes share AddChangeRow,
///                        which keeps the unnamed-column exclusion in one place.
///                        Matches VAS_099 / VAS_101, which read the same pair.
///   VAI163   2026-08-14  The VA075 work order's IDENTIFIER is probed rather than
///                        assumed (RunWorkOrderLookup): DocumentNo was selected
///                        outright, so on a revision naming it otherwise the whole
///                        statement failed, the catch recorded the module as
///                        unusable for the rest of the process, and the panel read
///                        "Manual Issue" however good the id on the line was. The
///                        REFERENCE column beside it was already probed for exactly
///                        this reason; the identifier had been left assumed. The
///                        lookup now also succeeds on the ID ALONE — a work order
///                        that cannot be named is still a work order, and the panel
///                        labels it "#<id>".
///   VAI163   2026-08-17  - The Issue Timeline's Posted stage captions with a DATE
///                          instead of the word "Posted", which only repeated the
///                          stage's own title. GetPostedDate resolves the issue's
///                          fact rows by AD_Table_ID IN + UPPER rather than joining
///                          on TableName = 'M_Inventory': a dictionary that spells
///                          the name in another case, or carries more than one row
///                          for it, matched nothing — the same failure this panel's
///                          e-mail lookup was already fixed for, and one that shows
///                          up per DEPLOYMENT (it was reported on PostgreSQL, where
///                          the stage showed the posting STATUS), not per statement.
///                          It then falls back to the fact rows' accounting date
///                          and, for a record the document itself reports as posted
///                          but whose facts answer nothing, to the record's own last
///                          change — the stamp posting leaves behind. Null only for
///                          an issue that was never posted, which is the one case
///                          the stage has no date to show.
///                        - LoadPostingActivity resolves the table id the same way:
///                          the trail lost its posting entry wherever the timeline
///                          lost its posting date, and for the same reason.
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
///   VAI163   2026-09-02  Activity timestamps render in the VIEWER's zone on
///                        PostgreSQL too. Every date and timestamp handed to
///                        the client now goes through Stamp(), which drops the
///                        DateTimeKind the provider tagged the value with -
///                        Oracle says Unspecified, Npgsql says Utc or Local,
///                        and Newtonsoft writes a zone designator for the
///                        latter two but not the first. The panel parses the
///                        bare Oracle form, so the designator made it read the
///                        value as already-zoned and skip its own conversion,
///                        printing the stored clock. Same JSON on either engine
///                        now. No-op on Oracle.
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
    public class VAS_102_OverviewInternalUseModel
    {
        private static readonly VLogger _log =
            VLogger.GetVLogger(typeof(VAS_102_OverviewInternalUseModel).FullName);

        /// <summary>
        /// Returns the full overview payload for the selected internal-use issue.
        /// MRole access filtering is applied ONLY on the main physical table
        /// (M_Inventory alias "inv"); the child line query inherits the parent's
        /// authorization and is not separately role-filtered.
        /// </summary>
        /// <param name="ctx">User context (client/org/role).</param>
        /// <param name="M_Inventory_ID">Selected internal-use issue id.</param>
        /// <returns>Populated <see cref="InternalUseOverviewData"/>; an empty
        /// instance when the id is invalid or no accessible row is found.</returns>
        public InternalUseOverviewData GetInternalUseOverview(Ctx ctx, int M_Inventory_ID)
        {
            InternalUseOverviewData result = new InternalUseOverviewData();
            if (M_Inventory_ID <= 0) return result;

            // Optional module columns — resolved once so the SQL below only
            // references columns that actually exist in this schema.
            bool hasCurrentCost = ColumnExists("M_InventoryLine", "CurrentCostPrice");
            bool hasUnitPrice   = ColumnExists("M_InventoryLine", "VA024_UnitPrice");
            bool hasWorkOrder   = ColumnExists("M_InventoryLine", "VAMFG_M_WorkOrder_ID");
            bool hasAsi         = ColumnExists("M_InventoryLine", "M_AttributeSetInstance_ID");
            // The project the issue was raised for. Guarded like every other
            // optional column: an install without it simply never gets a Project
            // origin, exactly as before.
            bool hasProject     = ColumnExists("M_Inventory", "C_Project_ID");

            // COALESCE([l.CurrentCostPrice], [l.VA024_UnitPrice], 0)
            string rateExpr = BuildRateExpr(hasCurrentCost, hasUnitPrice);
            // Manufacturing work-order id (schema-aware) used to classify origin.
            string woExpr = hasWorkOrder ? "l.VAMFG_M_WorkOrder_ID" : "NULL";

            // Project columns, only referenced when M_Inventory actually carries
            // C_Project_ID — the join itself has to disappear with the column.
            string projIdExpr    = hasProject ? "inv.C_Project_ID" : "NULL";
            string projValueExpr = hasProject ? "pj.Value"         : "CAST(NULL AS VARCHAR(255))";
            string projNameExpr  = hasProject ? "pj.Name"          : "CAST(NULL AS VARCHAR(255))";
            string projJoin      = hasProject
                ? "LEFT OUTER JOIN C_Project pj ON (pj.C_Project_ID = inv.C_Project_ID)"
                : "";

            // Requested quantity: what was asked for on the requisition the line
            // came from — not what the issue line itself carries. QtyEntered on an
            // internal-use line mirrors the issued quantity, so a partially issued
            // line would otherwise report "requested == issued". Lines with no
            // requisition (manual issue) keep QtyEntered.
            const string REQ_JOIN =
                "LEFT OUTER JOIN M_RequisitionLine rql ON (rql.M_RequisitionLine_ID = l.M_RequisitionLine_ID)";
            const string REQ_QTY = "COALESCE(rql.Qty, COALESCE(l.QtyEntered, 0))";

            string sql = @"SELECT
                              inv.M_Inventory_ID,
                              inv.DocumentNo,
                              inv.DocStatus,
                              inv.Processed,
                              inv.Posted,
                              inv.MovementDate,
                              inv.Description,
                              inv.M_Warehouse_ID,
                              inv.Created      AS CreatedDate,
                              inv.Updated      AS UpdatedDate,
                              wh.Name          AS WarehouseName,
                              creator.Name     AS IssuedBy,
                              " + projIdExpr    + @" AS ProjectID,
                              " + projValueExpr + @" AS ProjectValue,
                              " + projNameExpr  + @" AS ProjectName,
                              (SELECT COUNT(*)
                                 FROM M_InventoryLine l
                                WHERE l.M_Inventory_ID = inv.M_Inventory_ID
                                  AND l.IsActive       = 'Y')                   AS LineCount,
                              (SELECT COALESCE(SUM(" + REQ_QTY + @"), 0)
                                 FROM M_InventoryLine l " + REQ_JOIN + @"
                                WHERE l.M_Inventory_ID = inv.M_Inventory_ID
                                  AND l.IsActive       = 'Y')                   AS RequestedQty,
                              (SELECT COALESCE(SUM(COALESCE(l.QtyInternalUse, 0)), 0)
                                 FROM M_InventoryLine l
                                WHERE l.M_Inventory_ID = inv.M_Inventory_ID
                                  AND l.IsActive       = 'Y')                   AS IssuedQty,
                              (SELECT COALESCE(SUM(COALESCE(l.QtyInternalUse, 0) * " + rateExpr + @"), 0)
                                 FROM M_InventoryLine l
                                WHERE l.M_Inventory_ID = inv.M_Inventory_ID
                                  AND l.IsActive       = 'Y')                   AS TotalValue,
                              (SELECT COALESCE(SUM(CASE WHEN COALESCE(l.QtyInternalUse, 0) < " + REQ_QTY + @"
                                                   THEN 1 ELSE 0 END), 0)
                                 FROM M_InventoryLine l " + REQ_JOIN + @"
                                WHERE l.M_Inventory_ID = inv.M_Inventory_ID
                                  AND l.IsActive       = 'Y')                   AS NotFullCount,
                              (SELECT COALESCE(SUM(CASE WHEN " + REQ_QTY + @" > COALESCE(l.QtyInternalUse, 0)
                                                   THEN " + REQ_QTY + @" - COALESCE(l.QtyInternalUse, 0)
                                                   ELSE 0 END), 0)
                                 FROM M_InventoryLine l " + REQ_JOIN + @"
                                WHERE l.M_Inventory_ID = inv.M_Inventory_ID
                                  AND l.IsActive       = 'Y')                   AS NotFullQty,
                              (SELECT MAX(l.M_RequisitionLine_ID)
                                 FROM M_InventoryLine l
                                WHERE l.M_Inventory_ID = inv.M_Inventory_ID
                                  AND l.IsActive       = 'Y')                   AS SampleRequisitionLineID,
                              (SELECT MAX(" + woExpr + @")
                                 FROM M_InventoryLine l
                                WHERE l.M_Inventory_ID = inv.M_Inventory_ID
                                  AND l.IsActive       = 'Y')                   AS SampleWorkOrderID
                            FROM M_Inventory inv
                            LEFT OUTER JOIN M_Warehouse wh   ON (inv.M_Warehouse_ID = wh.M_Warehouse_ID)
                            LEFT OUTER JOIN AD_User creator  ON (inv.CreatedBy      = creator.AD_User_ID)
                            " + projJoin + @"
                            WHERE inv.M_Inventory_ID = @M_Inventory_ID
                              AND inv.IsActive       = 'Y'
                              AND COALESCE(inv.IsInternalUse, 'N') = 'Y'";

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
            result.MovementDate   = Stamp(r["MovementDate"]);
            result.Description    = Util.GetValueOfString(r["Description"]);
            result.WarehouseName  = Util.GetValueOfString(r["WarehouseName"]);
            result.IssuedBy       = Util.GetValueOfString(r["IssuedBy"]);
            result.CreatedDate    = Stamp(r["CreatedDate"]);
            result.UpdatedDate    = Stamp(r["UpdatedDate"]);
            int M_Warehouse_ID    = Util.GetValueOfInt(r["M_Warehouse_ID"]);

            // ----- KPI aggregates -----
            result.LineCount    = Util.GetValueOfInt(r["LineCount"]);
            result.RequestedQty = Util.GetValueOfDecimal(r["RequestedQty"]);
            result.IssuedQty    = Util.GetValueOfDecimal(r["IssuedQty"]);
            result.TotalValue   = Util.GetValueOfDecimal(r["TotalValue"]);
            result.NotFullCount = Util.GetValueOfInt(r["NotFullCount"]);
            result.NotFullQty   = Util.GetValueOfDecimal(r["NotFullQty"]);

            // ----- Project (M_Inventory.C_Project_ID) -----
            // Read before the origin is classified: an issue raised against a
            // project but against no other document used to read "Manual Issue".
            // The project's search key identifies it on the chip; its name rides
            // along for the tooltip.
            result.C_Project_ID = Util.GetValueOfInt(r["ProjectID"]);
            result.ProjectNo    = Util.GetValueOfString(r["ProjectValue"]);
            result.ProjectName  = Util.GetValueOfString(r["ProjectName"]);
            result.HasProject   = result.C_Project_ID > 0;

            // ----- Origin: derived from the linked source ids on the lines -----
            // A VA075 service work order wins, then a manufacturing work order,
            // then a requisition, then the project the issue was raised for; only
            // an issue linked to nothing at all is manual.
            LoadVA075WorkOrder(M_Inventory_ID, result);
            result.HasWorkOrder   = !string.IsNullOrEmpty(result.WorkOrderNo)
                                    || Util.GetValueOfInt(r["SampleWorkOrderID"]) > 0;
            result.HasRequisition = Util.GetValueOfInt(r["SampleRequisitionLineID"]) > 0;
            result.OriginCode     = !string.IsNullOrEmpty(result.WorkOrderNo) ? "WORKORDER"
                                  : (Util.GetValueOfInt(r["SampleWorkOrderID"]) > 0 ? "PRODUCTION"
                                  : (result.HasRequisition ? "REQUISITION"
                                  : (result.HasProject ? "PROJECT" : "MANUAL")));

            // Issued value is expressed in the accounting currency; the panel
            // renders INR (₹) with standard 2-dp precision.
            result.StdPrecision = 2;

            // ----- Reference documents (References section) -----
            result.Requisitions = LoadRequisitions(M_Inventory_ID);
            if (result.Requisitions.Count > 0)
            {
                RequisitionRefData first = result.Requisitions[0];
                // The id the panel's Reference chip opens the requisition with.
                result.M_Requisition_ID = first.M_Requisition_ID;
                result.RequisitionNo    = first.DocumentNo;
                result.RequisitionDate  = first.DateDoc;
                result.DateRequired     = first.DateRequired;
                result.RequestedBy      = first.PreparerName;
                result.RequisitionNote  = first.Description;
                // More than one requisition can feed a single issue; the panel
                // shows the first and a "+n" hint from this count.
                result.RequisitionCount = result.Requisitions.Count;
            }
            // The manufacturing production order (M_InventoryLine.
            // VAMFG_M_WorkOrder_ID). It is a DIFFERENT document to the VA075
            // service work order and travels in its own fields: it used to be
            // written into WorkOrderNo, which made the panel label a production
            // order "Work Order".
            if (hasWorkOrder)
                LoadProductionOrder(M_Inventory_ID, result);

            // ----- Timeline stamps -----
            // Created is the record's own stamp (not the movement date) and Issued
            // is the completion moment; both are what the panel's timeline reads.
            result.CompletedDate = GetCompletedDate(M_Inventory_ID);
            result.CompletedBy   = _lastCompletedByName;
            if (!result.CompletedDate.HasValue &&
                (result.StatusCode == "CO" || result.StatusCode == "CL"))
            {
                // Completed outside the workflow engine — the last change is the
                // closest stamp we have.
                result.CompletedDate = result.UpdatedDate;
            }
            // The Posted flag licenses the last-change fallback inside, so the
            // timeline's Posted stage always captions with a DATE on a posted issue
            // instead of repeating the word "Posted".
            result.PostedDate = GetPostedDate(M_Inventory_ID, result.Posted);

            // ----- Issue lines -----
            result.Lines = LoadLines(M_Inventory_ID, rateExpr, woExpr, hasAsi, M_Warehouse_ID);

            // Every KPI aggregate is re-derived from the loaded lines, replacing
            // the SQL sums read above.
            //
            // Two reasons the header query cannot produce these. Line values may
            // have been re-rated from M_CostDetail, and — since the lines are now
            // reported in each line's SELECTED UOM — the SQL sums are on the
            // product's BASE scale, so a line keyed in millilitres against a
            // litre-based product had the cards reading a thousandth of what its
            // own row showed. Summing the lines is what keeps the KPI cards, the
            // table rows and the totals footer telling the same story.
            //
            // Quantities across lines with different UOMs are still only as
            // meaningful as the units allow — the same caveat the issue's own
            // UOM mix carries — but they now at least match the rows they
            // summarise.
            decimal total = 0, requested = 0, issued = 0, notFullQty = 0;
            int notFullCount = 0;
            for (int i = 0; i < result.Lines.Count; i++)
            {
                InternalUseLineData ln = result.Lines[i];
                total     += ln.LineValue;
                requested += ln.RequestedQty;
                issued    += ln.IssuedQty;
                if (ln.IssuedQty < ln.RequestedQty)
                {
                    notFullCount++;
                    notFullQty += ln.RequestedQty - ln.IssuedQty;
                }
            }
            result.TotalValue   = total;
            result.RequestedQty = requested;
            result.IssuedQty    = issued;
            result.NotFullCount = notFullCount;
            result.NotFullQty   = notFullQty;
            result.LineCount    = result.Lines.Count;

            // ----- Notes (issue header + each line's own description) -----
            result.Notes = LoadNotes(M_Inventory_ID);

            // ----- Audit trail -----
            result.Activity = LoadActivity(M_Inventory_ID, result.StatusCode);

            return result;
        }

        /// <summary>
        /// Loads notes for the Notes section, mirroring the Purchase Order
        /// overview: the issue header note (M_Inventory.Description) followed by
        /// each line's own note (M_InventoryLine.Description), the description
        /// entered on the child tab. Composed in C# so the SQL stays portable (no
        /// DB-specific string functions).
        ///
        /// LEFT OUTER JOIN to M_InventoryLine (with IsActive in the join, not the
        /// WHERE) so the M_Inventory row — and thus the header note — is still
        /// returned for an issue with no active lines.
        /// </summary>
        /// <param name="M_Inventory_ID">Selected internal-use issue id.</param>
        /// <returns>Header note first, then one entry per line note (may be empty).</returns>
        private List<NoteData> LoadNotes(int M_Inventory_ID)
        {
            List<NoteData> notes = new List<NoteData>();
            try
            {
                string sql = @"SELECT inv.Description AS HeaderNote,
                                      l.Line          AS LineNo,
                                      l.Description   AS LineDescription,
                                      p.Name          AS ProductName,
                                      ch.Name         AS ChargeName
                                 FROM M_Inventory inv
                                 LEFT OUTER JOIN M_InventoryLine l ON (l.M_Inventory_ID = inv.M_Inventory_ID
                                                                       AND l.IsActive    = 'Y')
                                 LEFT OUTER JOIN M_Product p  ON (p.M_Product_ID = l.M_Product_ID)
                                 LEFT OUTER JOIN C_Charge  ch ON (ch.C_Charge_ID = l.C_Charge_ID)
                                WHERE inv.M_Inventory_ID = @M_Inventory_ID
                                  AND inv.IsActive       = 'Y'
                                ORDER BY l.Line";
                DataSet ds = DB.ExecuteDataset(sql, InventoryParam(M_Inventory_ID), null);
                if (ds == null || ds.Tables.Count == 0) return notes;

                bool headerAdded = false;
                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    if (!headerAdded)
                    {
                        string headerNote = Util.GetValueOfString(r["HeaderNote"]);
                        if (!string.IsNullOrEmpty(headerNote.Trim()))
                            notes.Add(new NoteData { NoteType = "header", Text = headerNote.Trim() });
                        headerAdded = true;
                    }

                    // Per-line note = the description entered on M_InventoryLine.
                    string lineDesc = Util.GetValueOfString(r["LineDescription"]);
                    if (string.IsNullOrEmpty(lineDesc.Trim())) continue;

                    string prod = Util.GetValueOfString(r["ProductName"]);
                    if (string.IsNullOrEmpty(prod)) prod = Util.GetValueOfString(r["ChargeName"]);

                    // Prefix with the line number (and product / charge, when
                    // present) so the line description is attributable on sight.
                    int lineNo = Util.GetValueOfInt(r["LineNo"]);
                    string label = lineNo > 0 ? "#" + lineNo : "";
                    if (!string.IsNullOrEmpty(prod))
                        label = string.IsNullOrEmpty(label) ? prod.Trim() : label + " " + prod.Trim();

                    string text = string.IsNullOrEmpty(label)
                        ? lineDesc.Trim()
                        : label + " — " + lineDesc.Trim();
                    notes.Add(new NoteData { NoteType = "line", Text = text });
                }
            }
            catch (Exception ex)
            {
                // Non-fatal: the panel simply shows no Notes section.
                _log.Severe("LoadNotes (M_Inventory_ID=" + M_Inventory_ID + "): " + ex.Message);
            }
            return notes;
        }

        /// <summary>
        /// Resolves a window's AD_Window_ID from its name (AD_Window.Name) for the
        /// panel's record-open path, so a Reference chip can open its source
        /// document on a named screen rather than whatever the table's default
        /// zoom target resolves to.
        ///
        /// Restricted to windows this tenant can see (AD_Client_ID 0 or its own),
        /// preferring the tenant's own row over the system one. Whether the ROLE
        /// may open it is the platform's call, made when the window is started.
        /// </summary>
        /// <param name="ctx">User context (client).</param>
        /// <param name="windowName">Window name to resolve.</param>
        /// <returns>The window id, or 0 when the name is unknown to this client.</returns>
        public int GetWindowId(Ctx ctx, string windowName)
        {
            if (string.IsNullOrEmpty(windowName)) return 0;
            try
            {
                // UPPER on both sides: the name is a dictionary entry keyed in by
                // hand, and a case difference must not read as "no such window" —
                // that failure is silent, and the panel's chip degrades to a
                // "Cannot open" toast rather than saying why.
                string sql = @"SELECT w.AD_Window_ID
                                 FROM AD_Window w
                                WHERE UPPER(w.Name)  = UPPER(@Name)
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
        /// This is the panel's last resort for a chip whose screen cannot be named
        /// in the client's map — a module that ships its own window and is not
        /// part of this solution (VA075) has no name that can be hard-coded, and
        /// the browser-side zoom lookup only knows tables the client has cached.
        /// Reading the dictionary here works for any installed module.
        ///
        /// Each statement carries a single bind name, occurring once: positional
        /// binding gives a repeated name a second, unfilled placeholder.
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
        /// Builds the unit-rate SQL expression from whichever optional cost
        /// columns exist on M_InventoryLine, ending at 0.
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
        /// Loads M_InventoryLine rows for the issue with product, locator and UOM
        /// metadata, requested (the linked requisition line's Qty, else
        /// QtyEntered) / issued (QtyInternalUse) quantities, available stock at the
        /// locator (summed from M_Storage) and a unit rate / line value. Child of
        /// an already authorized issue, so no separate MRole filter is applied
        /// here.
        /// </summary>
        /// <param name="M_Inventory_ID">Owning internal-use issue id.</param>
        /// <param name="rateExpr">Unit-rate SQL expression (schema-aware).</param>
        /// <param name="woExpr">Work-order-id SQL expression (schema-aware).</param>
        /// <param name="hasAsi">True when M_InventoryLine carries an attribute set instance.</param>
        /// <param name="M_Warehouse_ID">The issue's warehouse, for the costing lookup.</param>
        /// <returns>Ordered list of line rows (may be empty).</returns>
        private List<InternalUseLineData> LoadLines(
            int M_Inventory_ID, string rateExpr, string woExpr, bool hasAsi, int M_Warehouse_ID)
        {
            List<InternalUseLineData> lines = new List<InternalUseLineData>();

            // Attribute set instance (lot / serial / attributes) — only referenced
            // when the column is actually there.
            string asiExpr = hasAsi ? "asi.Description" : "CAST(NULL AS VARCHAR(255))";
            string asiJoin = hasAsi
                ? @"LEFT OUTER JOIN M_AttributeSetInstance asi
                           ON (asi.M_AttributeSetInstance_ID = l.M_AttributeSetInstance_ID
                               AND l.M_AttributeSetInstance_ID > 0)"
                : "";

            string sql = @"SELECT
                              l.M_InventoryLine_ID,
                              l.Line,
                              l.Description     AS LineDescription,
                              l.M_Product_ID,
                              -- Quantities travel on two scales and are reconciled
                              -- in C# below: QtyEntered is in the line's SELECTED
                              -- UOM (C_UOM_ID), while QtyInternalUse, the
                              -- requisition's Qty and M_Storage's on-hand are all
                              -- in the product's BASE UOM.
                              rql.Qty                  AS ReqQtyBase,
                              COALESCE(l.QtyEntered, 0)     AS QtyEnteredUOM,
                              COALESCE(l.QtyInternalUse, 0) AS QtyBase,
                              COALESCE(st.AvailableQty, 0)  AS AvailableQtyBase,
                              p.Value           AS ProductCode,
                              p.Name            AS ProductName,
                              loc.Value         AS LocatorCode,
                              COALESCE(loc.LocatorCombination, loc.Bin, loc.Value) AS LocatorName,
                              u.Name            AS UOMName,
                              COALESCE(u.StdPrecision, 0) AS UOMPrecision,
                              -- The PRODUCT's own unit. On-hand is stored in it
                              -- (M_Storage.QtyOnHand) and is reported in it, so
                              -- the column has to be able to name it.
                              bu.Name           AS BaseUOMName,
                              COALESCE(bu.StdPrecision, 0) AS BaseUOMPrecision,
                              " + rateExpr + @"                        AS UnitRate,
                              COALESCE(l.QtyInternalUse, 0) * " + rateExpr + @" AS LineValue,
                              l.M_RequisitionLine_ID AS RequisitionLineID,
                              rq.DocumentNo     AS RequisitionNo,
                              " + asiExpr + @"  AS AttributeSetInstance,
                              " + woExpr + @"                          AS WorkOrderID
                           FROM M_InventoryLine l
                           LEFT OUTER JOIN M_Product p   ON (p.M_Product_ID   = l.M_Product_ID)
                           LEFT OUTER JOIN C_UOM     u   ON (u.C_UOM_ID        = l.C_UOM_ID)
                           LEFT OUTER JOIN C_UOM     bu  ON (bu.C_UOM_ID       = p.C_UOM_ID)
                           LEFT OUTER JOIN M_Locator loc ON (loc.M_Locator_ID  = l.M_Locator_ID)
                           LEFT OUTER JOIN M_RequisitionLine rql ON (rql.M_RequisitionLine_ID = l.M_RequisitionLine_ID)
                           LEFT OUTER JOIN M_Requisition     rq  ON (rq.M_Requisition_ID      = rql.M_Requisition_ID)
                           " + asiJoin + @"
                           LEFT OUTER JOIN (SELECT s.M_Product_ID,
                                                   s.M_Locator_ID,
                                                   COALESCE(SUM(COALESCE(s.QtyOnHand, 0)), 0) AS AvailableQty
                                              FROM M_Storage s
                                             WHERE s.IsActive = 'Y'
                                             GROUP BY s.M_Product_ID, s.M_Locator_ID) st
                                  ON (st.M_Product_ID = l.M_Product_ID
                                  AND st.M_Locator_ID = l.M_Locator_ID)
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

            // Costing: the rate booked for the line itself wins, then the
            // product's latest cost at this warehouse; the line's own cost columns
            // remain the last resort.
            Dictionary<int, decimal> lineCosts = LoadLineCosts(M_Inventory_ID);
            Dictionary<int, decimal> whCosts   = LoadWarehouseCosts(M_Inventory_ID, M_Warehouse_ID);

            // What the VA075 work order asked for, per line — the quantity on the
            // spare part / service row the line came from. It outranks both other
            // sources below: an issue raised from a work order was requested THERE.
            Dictionary<int, decimal> woReqQtys = LoadWorkOrderComponentQtys(M_Inventory_ID);

            foreach (DataRow r in ds.Tables[0].Rows)
            {
                InternalUseLineData ln = new InternalUseLineData();
                ln.M_InventoryLine_ID = Util.GetValueOfInt(r["M_InventoryLine_ID"]);
                ln.Line               = Util.GetValueOfInt(r["Line"]);
                ln.Description        = Util.GetValueOfString(r["LineDescription"]);
                ln.M_Product_ID       = Util.GetValueOfInt(r["M_Product_ID"]);

                // ---- Put every quantity on the line's SELECTED UOM scale ----
                //
                // The panel labels each row with the selected UOM (C_UOM_ID), so
                // every figure on the row has to be expressed in it. QtyEntered
                // already is; QtyInternalUse, the requisition Qty and the storage
                // on-hand are in the product's base UOM.
                //
                // The line carries its own exact conversion: QtyEntered and
                // QtyInternalUse are the SAME physical quantity on the two scales
                // (MInventoryLine.BeforeSave converts one into the other whenever
                // the quantity or the UOM changes), so their ratio converts base
                // into selected without a conversion-table lookup — and it stays
                // right for a product whose rate is defined per line.
                decimal qtyEntered = Util.GetValueOfDecimal(r["QtyEnteredUOM"]);
                decimal qtyBase    = Util.GetValueOfDecimal(r["QtyBase"]);
                decimal perBase    = (qtyBase != 0 && qtyEntered != 0) ? (qtyEntered / qtyBase) : 1;

                ln.IssuedQty    = qtyEntered != 0 ? qtyEntered : qtyBase;

                // On hand is reported in the PRODUCT'S BASE UOM, always — the unit
                // M_Storage.QtyOnHand is actually stored in. It used to be restated
                // into the line's entered UOM (* perBase), which made the figure
                // disagree with every other place the product's stock is shown
                // (the product screen, the storage tab, a stock report) for any
                // line keyed in a non-base unit. The column names the base unit
                // beside the figure so the different scale to Issued is explicit.
                ln.AvailableQty       = Util.GetValueOfDecimal(r["AvailableQtyBase"]);
                ln.BaseUOMName        = Util.GetValueOfString(r["BaseUOMName"]);
                ln.BaseUOMPrecision   = Util.GetValueOfInt(r["BaseUOMPrecision"]);

                // Requested, in order of authority:
                //
                //  1. The VA075 work order's spare part / service row, when the
                //     line was raised from one. That row IS the request, so it
                //     wins — the issue line's own QtyEntered mirrors what was
                //     issued and would report "requested == issued". It is already
                //     on the selected-UOM scale, so it is taken as it stands.
                //  2. The requisition line, which asked in base UOM and so
                //     converts like the rest.
                //  3. What was keyed on the line (a manual issue), already in the
                //     selected UOM.
                decimal woReqQty;
                if (woReqQtys.TryGetValue(ln.M_InventoryLine_ID, out woReqQty))
                    ln.RequestedQty = woReqQty;
                else if (r["ReqQtyBase"] != DBNull.Value)
                    ln.RequestedQty = Util.GetValueOfDecimal(r["ReqQtyBase"]) * perBase;
                else
                    ln.RequestedQty = ln.IssuedQty;

                ln.ProductCode        = Util.GetValueOfString(r["ProductCode"]);
                ln.ProductName        = Util.GetValueOfString(r["ProductName"]);
                ln.LocatorCode        = Util.GetValueOfString(r["LocatorCode"]);
                ln.LocatorName        = Util.GetValueOfString(r["LocatorName"]);
                ln.UOMName            = Util.GetValueOfString(r["UOMName"]);
                ln.UOMPrecision       = Util.GetValueOfInt(r["UOMPrecision"]);
                ln.UnitRate           = Util.GetValueOfDecimal(r["UnitRate"]);
                ln.LineValue          = Util.GetValueOfDecimal(r["LineValue"]);
                ln.RequisitionLineID  = Util.GetValueOfInt(r["RequisitionLineID"]);
                ln.RequisitionNo      = Util.GetValueOfString(r["RequisitionNo"]);
                ln.AttributeSetInstance = Util.GetValueOfString(r["AttributeSetInstance"]);
                ln.WorkOrderID        = Util.GetValueOfInt(r["WorkOrderID"]);

                decimal costRate;
                if (lineCosts.TryGetValue(ln.M_InventoryLine_ID, out costRate) && costRate != 0)
                {
                    ln.UnitRate   = costRate;
                    ln.CostSource = "LINE";
                }
                else if (whCosts.TryGetValue(ln.M_Product_ID, out costRate) && costRate != 0)
                {
                    ln.UnitRate   = costRate;
                    ln.CostSource = "WAREHOUSE";
                }
                else
                {
                    ln.CostSource = "PRICE";
                }

                // Every rate resolved above is per BASE unit (M_CostDetail books
                // Amt / Qty in base, and the line's own cost columns are base
                // too), so it is restated per SELECTED unit to match the quantity
                // beside it — a rate per litre shown against a millilitre
                // quantity would read a thousand times high.
                //
                // The line's VALUE is unchanged by any of this: issued × rate is
                // the same money on either scale, which is what keeps the row, the
                // KPI card and the totals footer agreeing.
                if (perBase != 0 && perBase != 1) ln.UnitRate = ln.UnitRate / perBase;
                ln.LineValue = ln.IssuedQty * ln.UnitRate;

                lines.Add(ln);
            }
            return lines;
        }

        /// <summary>
        /// Loads the distinct requisitions the issue's lines were raised against,
        /// lowest document no first. Empty for a manual issue or one raised from a
        /// production order.
        /// </summary>
        /// <param name="M_Inventory_ID">Owning internal-use issue id.</param>
        /// <returns>Ordered list of linked requisitions (may be empty).</returns>
        private List<RequisitionRefData> LoadRequisitions(int M_Inventory_ID)
        {
            List<RequisitionRefData> refs = new List<RequisitionRefData>();

            string sql = @"SELECT DISTINCT
                              rq.M_Requisition_ID,
                              rq.DocumentNo,
                              rq.DateDoc,
                              rq.DateRequired,
                              rq.Description,
                              preparer.Name AS PreparerName
                           FROM M_InventoryLine l
                           INNER JOIN M_RequisitionLine rql ON (rql.M_RequisitionLine_ID = l.M_RequisitionLine_ID)
                           INNER JOIN M_Requisition     rq  ON (rq.M_Requisition_ID      = rql.M_Requisition_ID)
                           LEFT OUTER JOIN AD_User preparer ON (preparer.AD_User_ID      = rq.AD_User_ID)
                          WHERE l.M_Inventory_ID = @M_Inventory_ID
                            AND l.IsActive       = 'Y'
                          ORDER BY rq.DocumentNo";

            SqlParameter[] param = new SqlParameter[]
            {
                new SqlParameter("@M_Inventory_ID", M_Inventory_ID)
            };

            DataSet ds = DB.ExecuteDataset(sql, param, null);
            if (ds == null || ds.Tables.Count == 0)
                return refs;

            foreach (DataRow r in ds.Tables[0].Rows)
            {
                RequisitionRefData rf = new RequisitionRefData();
                rf.M_Requisition_ID = Util.GetValueOfInt(r["M_Requisition_ID"]);
                rf.DocumentNo       = Util.GetValueOfString(r["DocumentNo"]);
                rf.DateDoc          = Stamp(r["DateDoc"]);
                rf.DateRequired     = Stamp(r["DateRequired"]);
                rf.Description      = Util.GetValueOfString(r["Description"]);
                rf.PreparerName     = Util.GetValueOfString(r["PreparerName"]);
                refs.Add(rf);
            }
            return refs;
        }

        /// <summary>
        /// Fills ProductionOrderNo / VAMFG_M_WorkOrder_ID / ProductionOrderCount
        /// from the manufacturing production order(s) the issue's lines were
        /// raised against (M_InventoryLine.VAMFG_M_WorkOrder_ID).
        ///
        /// This is a production order, NOT the VA075 service work order — the two
        /// are separate documents on separate tables, and each gets its own chip
        /// in the panel's Reference strip so neither is labelled as the other.
        /// The id travels with the document no so the chip can open the record.
        ///
        /// Only called when M_InventoryLine.VAMFG_M_WorkOrder_ID exists; a DB
        /// issue degrades to nothing loaded, so a missing / renamed VAMFG table
        /// never breaks the overview.
        /// </summary>
        /// <param name="M_Inventory_ID">Owning internal-use issue id.</param>
        /// <param name="result">Overview payload being populated.</param>
        private void LoadProductionOrder(int M_Inventory_ID, InternalUseOverviewData result)
        {
            try
            {
                string sql = @"SELECT DISTINCT wo.VAMFG_M_WorkOrder_ID, wo.DocumentNo
                                 FROM M_InventoryLine l
                                INNER JOIN VAMFG_M_WorkOrder wo
                                   ON (wo.VAMFG_M_WorkOrder_ID = l.VAMFG_M_WorkOrder_ID)
                                WHERE l.M_Inventory_ID = @M_Inventory_ID
                                  AND l.IsActive       = 'Y'
                                ORDER BY wo.DocumentNo";
                SqlParameter[] param = new SqlParameter[]
                {
                    new SqlParameter("@M_Inventory_ID", M_Inventory_ID)
                };
                DataSet ds = DB.ExecuteDataset(sql, param, null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return;

                DataRow r = ds.Tables[0].Rows[0];
                result.VAMFG_M_WorkOrder_ID = Util.GetValueOfInt(r["VAMFG_M_WorkOrder_ID"]);
                result.ProductionOrderNo    = Util.GetValueOfString(r["DocumentNo"]);
                // More than one production order can feed a single issue; the
                // panel names the first and hints the rest with "+n".
                result.ProductionOrderCount = ds.Tables[0].Rows.Count;
            }
            catch (Exception ex)
            {
                _log.Severe("LoadProductionOrder (" + M_Inventory_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// Fills WorkOrderNo / WorkOrderRef / VA075_WorkOrder_ID from the VA075
        /// service work order the issue was raised against.
        ///
        /// The link is M_InventoryLine.VA075_WorkOrder_ID. Rows saved before that
        /// column was stamped only carry VA075_WorkOrderComponent_ID, so the
        /// component's own work order is accepted as a fallback — that is what
        /// keeps existing issues from reading "Manual Issue". Everything is
        /// AD_Column-guarded and try/caught: without the VA075 module this is a
        /// no-op.
        /// </summary>
        /// <param name="M_Inventory_ID">Selected internal-use issue id.</param>
        /// <param name="result">Overview payload being populated.</param>
        /// <summary>
        /// Remembers whether each VA075 lookup is usable against this schema, so a
        /// database without the module reports it once per process rather than on
        /// every issue the panel opens.
        /// Null = not tried, false = the statement failed, true = it ran.
        /// </summary>
        private static bool? _va075DirectUsable;
        private static bool? _va075ComponentUsable;

        private void LoadVA075WorkOrder(int M_Inventory_ID, InternalUseOverviewData result)
        {
            // "Work order reference" is named differently across VA075 revisions;
            // take the first one this schema actually has. A dictionary that names
            // none simply yields no reference pill — the chip itself does not
            // depend on it.
            string refCol = FirstExistingColumn("VA075_WorkOrder", new string[]
            {
                "VA075_ReferenceNo", "Reference", "POReference", "Description", "Name"
            });
            string refExpr = string.IsNullOrEmpty(refCol)
                ? "CAST(NULL AS VARCHAR(255))" : "wo." + refCol;

            // The work order stamped straight onto the issue line — the link the
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
            // stands in. This is what keeps existing issues off "Manual Issue".
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
        /// The statement is ATTEMPTED rather than gated on ColumnExists. That
        /// dictionary guard was the single point at which the Work Order origin
        /// failed: VA075 is NOT part of this solution, so its AD_Table / AD_Column
        /// rows are whatever the module's own install left behind — and the guard
        /// answers "absent" for a table it cannot find, or raises (which the catch
        /// turns into "absent") when AD_Table holds more than one row for the name.
        /// Either way the panel reported no work order and the Reference section
        /// fell through to "Manual Issue" even though M_InventoryLine
        /// .VA075_WorkOrder_ID held a perfectly good id. Running the query and
        /// letting a genuinely absent module throw once is both more accurate and
        /// cheaper than asking the dictionary to describe a schema it does not own.
        ///
        /// The issue id is inlined rather than bound: the caller's sub-select and
        /// this statement would otherwise need the same bind name twice, which
        /// Oracle's positional binding does not allow. It is an int, so nothing
        /// can be injected.
        /// </summary>
        /// <returns>True when a work order was found and the payload filled.</returns>
        private bool RunWorkOrderLookup(string innerSql, string refExpr,
                                        InternalUseOverviewData result,
                                        ref bool? usable, string which, int M_Inventory_ID)
        {
            try
            {
                // The work order's IDENTIFIER is probed, not assumed. DocumentNo
                // was selected outright — on a VA075 revision that names it
                // otherwise the whole statement failed, the catch below recorded
                // the module as unusable, and the panel fell through to "Manual
                // Issue" however good the id on the line was. The reference column
                // a few lines up was already probed for exactly this reason; the
                // identifier had been left assumed.
                //
                // Selected as a stable alias so the reader below does not care
                // which column answered.
                string noCol = FirstExistingColumn("VA075_WorkOrder", new string[]
                {
                    "DocumentNo", "Name", "Value", "VA075_SRNo"
                });
                string noExpr = string.IsNullOrEmpty(noCol)
                    ? "CAST(NULL AS VARCHAR(255))" : "wo." + noCol;
                string orderBy = string.IsNullOrEmpty(noCol)
                    ? "wo.VA075_WorkOrder_ID" : "wo." + noCol;

                string sql = @"SELECT wo.VA075_WorkOrder_ID,
                                      " + noExpr + @"  AS WorkOrderNo,
                                      " + refExpr + @" AS WorkOrderRef
                                 FROM VA075_WorkOrder wo
                                WHERE wo.VA075_WorkOrder_ID IN (" + innerSql + @")
                                ORDER BY " + orderBy;
                DataSet ds = DB.ExecuteDataset(sql, null, null);
                usable = true;
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
                    return false;

                DataRow r = ds.Tables[0].Rows[0];
                result.VA075_WorkOrder_ID = Util.GetValueOfInt(r["VA075_WorkOrder_ID"]);
                result.WorkOrderNo        = Util.GetValueOfString(r["WorkOrderNo"]);
                result.WorkOrderRef       = Util.GetValueOfString(r["WorkOrderRef"]);
                result.WorkOrderCount     = ds.Tables[0].Rows.Count;
                // The ID is what makes this an origin — a work order the panel can
                // name is better, but one it cannot name is still a work order.
                return result.VA075_WorkOrder_ID > 0;
            }
            catch (Exception ex)
            {
                // Almost certainly "no such table / column" on an install without
                // VA075. Recorded so the next issue skips the attempt.
                usable = false;
                _log.Severe("LoadVA075WorkOrder/" + which +
                            " (M_Inventory_ID=" + M_Inventory_ID + "): " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// The quantity the VA075 work order asked for, per issue line: the
        /// Quantity on the spare part / service row the line was raised from
        /// (work order > task details > spare parts / services tab), keyed by
        /// M_InventoryLine_ID.
        ///
        /// The link is M_InventoryLine.VA075_WorkOrderComponent_ID — the spare
        /// part row itself, not just the work order — so a line that only carries
        /// VA075_WorkOrder_ID is absent from the result and keeps the requisition
        /// / keyed-quantity fallback.
        ///
        /// No UOM conversion is applied. The component quantity is on the same
        /// scale as M_InventoryLine.QtyEntered (the issue line is generated from
        /// the component, and MInventory writes QtyEntered straight back into
        /// VA075_WorkOrderComponent.Quantity on completion), which is the SELECTED
        /// UOM the panel reports every line quantity in.
        ///
        /// AD_Column-guarded and try/caught: without the VA075 module this is a
        /// pair of dictionary reads and an empty result.
        /// </summary>
        /// <param name="M_Inventory_ID">Owning internal-use issue id.</param>
        /// <returns>M_InventoryLine_ID -> requested quantity (may be empty).</returns>
        private Dictionary<int, decimal> LoadWorkOrderComponentQtys(int M_Inventory_ID)
        {
            Dictionary<int, decimal> qtys = new Dictionary<int, decimal>();
            if (!ColumnExists("M_InventoryLine", "VA075_WorkOrderComponent_ID")) return qtys;
            if (!ColumnExists("VA075_WorkOrderComponent", "Quantity")) return qtys;

            try
            {
                string sql = @"SELECT l.M_InventoryLine_ID,
                                      COALESCE(c.Quantity, 0) AS ReqQty
                                 FROM M_InventoryLine l
                                INNER JOIN VA075_WorkOrderComponent c
                                        ON (c.VA075_WorkOrderComponent_ID = l.VA075_WorkOrderComponent_ID)
                                WHERE l.M_Inventory_ID = @M_Inventory_ID
                                  AND l.IsActive       = 'Y'
                                  AND COALESCE(l.VA075_WorkOrderComponent_ID, 0) > 0";
                DataSet ds = DB.ExecuteDataset(sql, InventoryParam(M_Inventory_ID), null);
                if (ds == null || ds.Tables.Count == 0) return qtys;

                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    int lineId = Util.GetValueOfInt(r["M_InventoryLine_ID"]);
                    if (lineId > 0) qtys[lineId] = Util.GetValueOfDecimal(r["ReqQty"]);
                }
            }
            catch (Exception ex)
            {
                // Non-fatal: the lines keep the requisition / keyed quantity.
                _log.Severe("LoadWorkOrderComponentQtys (M_Inventory_ID="
                            + M_Inventory_ID + "): " + ex.Message);
            }
            return qtys;
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

        // ----------------------------------------------------------------- //
        //  Costing (M_CostDetail)                                            //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// Unit cost actually booked for each line of this issue, keyed by
        /// M_InventoryLine_ID: Amt / Qty of the newest M_CostDetail row written
        /// against the line. Empty for an unprocessed issue (no cost detail yet)
        /// or when the lookup fails.
        /// </summary>
        /// <param name="M_Inventory_ID">Owning internal-use issue id.</param>
        private Dictionary<int, decimal> LoadLineCosts(int M_Inventory_ID)
        {
            Dictionary<int, decimal> costs = new Dictionary<int, decimal>();
            if (!ColumnExists("M_CostDetail", "M_InventoryLine_ID")) return costs;

            try
            {
                // One row per line — the newest cost detail — rather than a SUM,
                // which would double-count across accounting schemas and cost
                // elements.
                string sql = @"SELECT cd.M_InventoryLine_ID, cd.Amt, cd.Qty
                                 FROM M_CostDetail cd
                                WHERE cd.M_CostDetail_ID IN
                                      (SELECT MAX(cd2.M_CostDetail_ID)
                                         FROM M_CostDetail cd2
                                        INNER JOIN M_InventoryLine l2
                                                ON (l2.M_InventoryLine_ID = cd2.M_InventoryLine_ID)
                                        WHERE l2.M_Inventory_ID = @M_Inventory_ID
                                          AND l2.IsActive       = 'Y'
                                          AND cd2.IsActive      = 'Y'
                                          AND COALESCE(cd2.Qty, 0)  <> 0
                                        GROUP BY cd2.M_InventoryLine_ID)";

                DataSet ds = DB.ExecuteDataset(sql, InventoryParam(M_Inventory_ID), null);
                if (ds == null || ds.Tables.Count == 0) return costs;

                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    decimal qty = Util.GetValueOfDecimal(r["Qty"]);
                    if (qty == 0) continue;
                    costs[Util.GetValueOfInt(r["M_InventoryLine_ID"])] =
                        Math.Abs(Util.GetValueOfDecimal(r["Amt"]) / qty);
                }
            }
            catch (Exception ex)
            {
                // Non-fatal: the line falls back to the warehouse cost, then to
                // its own cost columns.
                _log.Severe("LoadLineCosts (M_Inventory_ID=" + M_Inventory_ID + "): " + ex.Message);
            }
            return costs;
        }

        /// <summary>
        /// Latest unit cost per product AT THE ISSUE'S WAREHOUSE, keyed by
        /// M_Product_ID — Amt / Qty of the newest M_CostDetail row for that
        /// product and warehouse. Used for lines that carry no cost detail of
        /// their own (a draft issue). Empty when the schema keeps no warehouse on
        /// M_CostDetail (costing level below warehouse) or the lookup fails.
        /// </summary>
        /// <param name="M_Inventory_ID">Owning internal-use issue id.</param>
        /// <param name="M_Warehouse_ID">The issue's warehouse.</param>
        private Dictionary<int, decimal> LoadWarehouseCosts(int M_Inventory_ID, int M_Warehouse_ID)
        {
            Dictionary<int, decimal> costs = new Dictionary<int, decimal>();
            if (M_Warehouse_ID <= 0) return costs;
            if (!ColumnExists("M_CostDetail", "M_Warehouse_ID")) return costs;

            try
            {
                string sql = @"SELECT cd.M_Product_ID, cd.Amt, cd.Qty
                                 FROM M_CostDetail cd
                                WHERE cd.M_CostDetail_ID IN
                                      (SELECT MAX(cd2.M_CostDetail_ID)
                                         FROM M_CostDetail cd2
                                        WHERE cd2.IsActive        = 'Y'
                                          AND COALESCE(cd2.Qty, 0)    <> 0
                                          AND cd2.M_Warehouse_ID  = @M_Warehouse_ID
                                          AND cd2.M_Product_ID IN
                                              (SELECT l.M_Product_ID
                                                 FROM M_InventoryLine l
                                                WHERE l.M_Inventory_ID = @M_Inventory_ID
                                                  AND l.IsActive       = 'Y')
                                        GROUP BY cd2.M_Product_ID)";
                SqlParameter[] param = new SqlParameter[]
                {
                    new SqlParameter("@M_Warehouse_ID", M_Warehouse_ID),
                    new SqlParameter("@M_Inventory_ID", M_Inventory_ID)
                };

                DataSet ds = DB.ExecuteDataset(sql, param, null);
                if (ds == null || ds.Tables.Count == 0) return costs;

                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    decimal qty = Util.GetValueOfDecimal(r["Qty"]);
                    if (qty == 0) continue;
                    costs[Util.GetValueOfInt(r["M_Product_ID"])] =
                        Math.Abs(Util.GetValueOfDecimal(r["Amt"]) / qty);
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadWarehouseCosts (M_Inventory_ID=" + M_Inventory_ID + "): " + ex.Message);
            }
            return costs;
        }

        // ----------------------------------------------------------------- //
        //  Timeline stamps + audit trail                                     //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// Set by <see cref="GetCompletedDate"/> alongside its return value, so the
        /// caller gets both the moment and the actor from one query.
        /// </summary>
        private string _lastCompletedByName;

        /// <summary>
        /// Returns the moment the issue was completed — the Created stamp of its
        /// workflow DocComplete activity — or null when it has no such node (an
        /// issue completed outside the workflow engine); the caller then falls back
        /// to the last-updated stamp.
        ///
        /// Deliberately a standalone query rather than a subselect in the main
        /// SELECT, which MRole.AddAccessSQL rewrites with SQL_FULLYQUALIFIED: that
        /// parser walks every FROM/JOIN it can read (including inside subselects)
        /// and appends its filters to the OUTER WHERE, so aliases living only in a
        /// subselect break the whole statement.
        /// </summary>
        /// <param name="M_Inventory_ID">Selected internal-use issue id.</param>
        private DateTime? GetCompletedDate(int M_Inventory_ID)
        {
            _lastCompletedByName = "";
            try
            {
                string sql = @"SELECT wfa.Created, u.Name AS UserName
                                 FROM AD_WF_Process wfp
                                INNER JOIN AD_WF_Activity wfa
                                        ON (wfa.AD_WF_Process_ID = wfp.AD_WF_Process_ID)
                                INNER JOIN AD_WF_Node wfn
                                        ON (wfn.AD_WF_Node_ID = wfa.AD_WF_Node_ID)
                                INNER JOIN AD_Table adt
                                        ON (adt.AD_Table_ID = wfp.AD_Table_ID)
                                 LEFT OUTER JOIN AD_User u ON (wfa.CreatedBy = u.AD_User_ID)
                                WHERE wfp.Record_ID = @M_Inventory_ID
                                  AND adt.TableName = 'M_Inventory'
                                  AND wfp.IsActive  = 'Y'
                                  AND wfa.IsActive  = 'Y'
                                  AND wfn.IsActive  = 'Y'
                                  AND wfa.WFState   = 'CC'
                                  AND UPPER(TRIM(wfn.Value)) IN ('DOCCOMPLETE', 'COMPLETE', '(DOCCOMPLETE)')
                                ORDER BY wfa.Created DESC";
                DataSet ds = DB.ExecuteDataset(sql, InventoryParam(M_Inventory_ID), null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
                    return null;

                DataRow r = ds.Tables[0].Rows[0];
                _lastCompletedByName = Util.GetValueOfString(r["UserName"]);
                return Stamp(r["Created"]);
            }
            catch (Exception ex)
            {
                _log.Severe("GetCompletedDate (M_Inventory_ID=" + M_Inventory_ID + "): " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Returns the date the issue was posted, for the timeline's Posted stage:
        /// the earliest Created stamp across the Fact_Acct rows written for it,
        /// falling back to those rows' accounting date and finally — for a record
        /// the document itself reports as posted but whose facts cannot be read —
        /// to the record's own last change. Null only when the issue has never been
        /// posted at all.
        ///
        /// The stage is meant to CAPTION WITH A DATE; with nothing to show it prints
        /// the word "Posted", which only repeats the stage's own title. Hence the
        /// chain: on a posted record, one of the three always answers.
        ///
        /// The table id is resolved with IN + UPPER over AD_Table rather than by
        /// joining on TableName = 'M_Inventory'. A dictionary that spells the name
        /// in another case, or carries more than one row for it, made the join
        /// match nothing — and the panel then reported an obviously posted issue as
        /// having no posting date. It is the same fix this panel's e-mail lookup
        /// already carries, and the difference shows up per DEPLOYMENT (it was
        /// reported on PostgreSQL), not per statement.
        ///
        /// Standalone queries for the same MRole reason documented on
        /// <see cref="GetCompletedDate"/>. Every statement is portable: no NVL, no
        /// TRUNC, one bind name per statement occurring exactly once.
        /// </summary>
        /// <param name="M_Inventory_ID">Selected internal-use issue id.</param>
        /// <param name="posted">The record's own Posted flag — what licenses the
        /// last-change fallback. Without it an unposted issue would be given a
        /// posting date.</param>
        private DateTime? GetPostedDate(int M_Inventory_ID, bool posted)
        {
            try
            {
                // Both stamps in one read: Created is when posting RAN, DateAcct the
                // date it was booked to. Created is the milestone the timeline wants;
                // DateAcct answers for a deployment whose fact rows carry no usable
                // create stamp.
                string sql = @"SELECT MIN(fa.Created)  AS PostedOn,
                                      MIN(fa.DateAcct) AS PostedAcct
                                 FROM Fact_Acct fa
                                WHERE fa.Record_ID = @M_Inventory_ID
                                  AND fa.AD_Table_ID IN
                                      (SELECT t.AD_Table_ID FROM AD_Table t
                                        WHERE UPPER(t.TableName) = 'M_INVENTORY')";
                DataSet ds = DB.ExecuteDataset(sql, InventoryParam(M_Inventory_ID), null);
                if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                {
                    DataRow r = ds.Tables[0].Rows[0];
                    DateTime? postedOn = Stamp(r["PostedOn"]);
                    if (postedOn.HasValue) return postedOn;
                    DateTime? postedAcct = Stamp(r["PostedAcct"]);
                    if (postedAcct.HasValue) return postedAcct;
                }
            }
            catch (Exception ex)
            {
                // Non-fatal: fall through to the record's own stamp below.
                _log.Severe("GetPostedDate/facts (M_Inventory_ID=" + M_Inventory_ID + "): " + ex.Message);
            }

            // The document says it is posted, but its accounting facts answered
            // nothing. Posting is what last wrote to the record — it stamps Posted
            // and nothing else touches a posted issue — so its last change is the
            // closest thing to the posting moment there is, and a date is what this
            // stage exists to show. Never reached for an unposted issue.
            if (!posted) return null;
            try
            {
                string sql = @"SELECT i.Updated
                                 FROM M_Inventory i
                                WHERE i.M_Inventory_ID = @M_Inventory_ID";
                DataSet ds = DB.ExecuteDataset(sql, InventoryParam(M_Inventory_ID), null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
                    return null;
                return Stamp(ds.Tables[0].Rows[0]["Updated"]);
            }
            catch (Exception ex)
            {
                _log.Severe("GetPostedDate/record (M_Inventory_ID=" + M_Inventory_ID + "): " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Builds the issue's audit trail: who created it, who last changed it and
        /// when, when it was completed and by whom, when it was posted, plus any
        /// chat notes and e-mails logged against it — merged newest-first and
        /// capped.
        ///
        /// Each source runs under its own guard so a DB-level problem with one
        /// degrades to a partial trail (logged) rather than breaking the overview.
        /// </summary>
        /// <param name="M_Inventory_ID">Selected internal-use issue id.</param>
        /// <param name="docStatus">The issue's DocStatus, for the completion entry.</param>
        /// <returns>Activity rows ordered newest-first (may be empty).</returns>
        private List<InternalUseActivityData> LoadActivity(int M_Inventory_ID, string docStatus)
        {
            // Purely a runaway guard, not a headline count: the panel pages the
            // feed 15 rows at a time, so anything the cap cuts is silently
            // unreachable rather than merely further down. It sits high enough
            // that a normally mailed issue never reaches it.
            const int MAX_ENTRIES = 200;

            List<InternalUseActivityData> activity = new List<InternalUseActivityData>();
            LoadIssueMilestones(M_Inventory_ID, docStatus, activity);

            // One row per FIELD the user changed.
            int fieldChanges = LoadChangeActivity(M_Inventory_ID, activity);

            // Appointments, tasks, calls and letters filed against the issue.
            LoadSharedSourceActivity(M_Inventory_ID, activity);

            // The milestone above adds a single generic "the issue was edited" row
            // from the header's own stamp. That is a stand-in for exactly the
            // detail this now carries, so it goes as soon as the change log has
            // named the fields — otherwise the same edit is reported twice, once
            // vaguely and once per field.
            if (fieldChanges > 0)
            {
                activity.RemoveAll(delegate (InternalUseActivityData a)
                {
                    return a.Type == "updated" && string.IsNullOrEmpty(a.FieldName);
                });
            }

            LoadPostingActivity(M_Inventory_ID, activity);
            LoadNoteActivity(M_Inventory_ID, activity);
            LoadEmailActivity(M_Inventory_ID, activity);

            // Newest first; entries with no timestamp sink to the bottom.
            activity.Sort(delegate (InternalUseActivityData a, InternalUseActivityData b)
            {
                return b.Created.GetValueOrDefault(DateTime.MinValue)
                        .CompareTo(a.Created.GetValueOrDefault(DateTime.MinValue));
            });

            if (activity.Count > MAX_ENTRIES)
                activity = activity.GetRange(0, MAX_ENTRIES);
            return activity;
        }

        /// <summary>
        /// One "updated" row per FIELD the user changed, read from the platform's
        /// change log (AD_ChangeLog). Each row names the field (the dictionary's
        /// display name for the column, falling back to the raw column name), who
        /// changed it and when — so the trail says which field moved rather than
        /// only that something did.
        ///
        /// Both the header (M_Inventory) and its LINES (M_InventoryLine) are read.
        /// An issue's substantive edits are its ISSUED QUANTITIES, and those live
        /// on the lines — a header-only trail reported nothing at all for the
        /// change a reader most wants to trace, so a corrected quantity left no
        /// record. A line row is labelled with its line number and product so the
        /// reader can tell which row moved.
        ///
        /// Silently degrades to no rows when change logging is off for the table,
        /// in which case the caller keeps its single header-stamp "updated" row.
        /// </summary>
        /// <param name="M_Inventory_ID">Selected internal-use issue id.</param>
        /// <param name="list">Feed being built; rows are appended.</param>
        /// <returns>How many field-level rows were added.</returns>
        private int LoadChangeActivity(int M_Inventory_ID, List<InternalUseActivityData> list)
        {
            int added = 0;

            // ----- Header edits (M_Inventory) -----
            try
            {
                // AD_Column is LEFT joined so a log row whose column has since been
                // removed from the dictionary still reports its change.
                string sql = @"SELECT cl.Created,
                                      cl.OldValue,
                                      cl.NewValue,
                                      u.Name         AS UserName,
                                      col.Name       AS FieldLabel,
                                      col.ColumnName AS FieldColumn,
                                      col.AD_Reference_ID       AS RefType,
                                      col.AD_Reference_Value_ID AS RefValueId
                                 FROM AD_ChangeLog cl
                                INNER JOIN AD_Table adt
                                        ON (adt.AD_Table_ID = cl.AD_Table_ID)
                                 LEFT OUTER JOIN AD_Column col
                                        ON (col.AD_Column_ID = cl.AD_Column_ID)
                                 LEFT OUTER JOIN AD_User u
                                        ON (u.AD_User_ID = cl.CreatedBy)
                                WHERE cl.Record_ID = @M_Inventory_ID
                                  AND adt.TableName = 'M_Inventory'
                                  AND COALESCE(cl.IsActive, 'Y') = 'Y'
                                ORDER BY cl.Created DESC";
                DataSet ds = DB.ExecuteDataset(sql, InventoryParam(M_Inventory_ID), null);
                if (ds != null && ds.Tables.Count > 0)
                {
                    foreach (DataRow r in ds.Tables[0].Rows)
                        if (AddChangeRow(r, "", list)) added++;
                }
            }
            catch (Exception ex)
            {
                // Change logging is optional; a schema without it just shows no
                // per-field rows.
                _log.Severe("LoadChangeActivity/header (M_Inventory_ID=" + M_Inventory_ID + "): " + ex.Message);
            }

            // ----- Line edits (M_InventoryLine) -----
            //
            // The line ids are reached through a join rather than a sub-select on
            // the same parameter, so the statement carries its bind name exactly
            // once: Oracle binds positionally, and a repeated name becomes a
            // second, unfilled placeholder.
            try
            {
                string sql = @"SELECT cl.Created,
                                      cl.OldValue,
                                      cl.NewValue,
                                      u.Name         AS UserName,
                                      col.Name       AS FieldLabel,
                                      col.ColumnName AS FieldColumn,
                                      col.AD_Reference_ID       AS RefType,
                                      col.AD_Reference_Value_ID AS RefValueId,
                                      l.Line  AS LineNo,
                                      p.Name  AS ProductName
                                 FROM AD_ChangeLog cl
                                INNER JOIN AD_Table adt
                                        ON (adt.AD_Table_ID = cl.AD_Table_ID)
                                INNER JOIN M_InventoryLine l
                                        ON (l.M_InventoryLine_ID = cl.Record_ID)
                                 LEFT OUTER JOIN M_Product p
                                        ON (p.M_Product_ID  = l.M_Product_ID)
                                 LEFT OUTER JOIN AD_Column col
                                        ON (col.AD_Column_ID = cl.AD_Column_ID)
                                 LEFT OUTER JOIN AD_User u
                                        ON (u.AD_User_ID = cl.CreatedBy)
                                WHERE adt.TableName = 'M_InventoryLine'
                                  AND COALESCE(cl.IsActive, 'Y') = 'Y'
                                  AND l.M_Inventory_ID = @M_Inventory_ID
                                ORDER BY cl.Created DESC";
                DataSet ds = DB.ExecuteDataset(sql, InventoryParam(M_Inventory_ID), null);
                if (ds != null && ds.Tables.Count > 0)
                {
                    foreach (DataRow r in ds.Tables[0].Rows)
                    {
                        // "#10 Steel Bolt M8" — the line number identifies the row,
                        // the product says what it is without a second lookup.
                        string scope = "#" + Util.GetValueOfInt(r["LineNo"]);
                        string prod  = Util.GetValueOfString(r["ProductName"]);
                        if (!string.IsNullOrEmpty(prod)) scope += " " + prod.Trim();
                        if (AddChangeRow(r, scope, list)) added++;
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadChangeActivity/lines (M_Inventory_ID=" + M_Inventory_ID + "): " + ex.Message);
            }

            return added;
        }

        /// <summary>
        /// Turns one AD_ChangeLog row into an "updated" activity entry. A change
        /// whose column the dictionary cannot name is skipped: it would render as
        /// a bare "Updated" identifying nothing, which is what naming the field
        /// exists to stop.
        /// </summary>
        /// <param name="r">Change-log row (Created / UserName / FieldLabel /
        /// FieldColumn).</param>
        /// <param name="scope">Which record the edit landed on: "" for the issue
        /// header, else the line's number and product.</param>
        /// <param name="list">Feed being built; the row is appended.</param>
        /// <returns>True when an entry was added.</returns>
        private bool AddChangeRow(DataRow r, string scope, List<InternalUseActivityData> list)
        {
            DateTime? at = Stamp(r["Created"]);
            if (!at.HasValue) return false;

            string field = Util.GetValueOfString(r["FieldLabel"]);
            if (string.IsNullOrEmpty(field))
                field = Util.GetValueOfString(r["FieldColumn"]);
            if (string.IsNullOrEmpty(field)) return false;

            // The move itself. A save that rewrites a field with the value it
            // already had is not an edit, and the platform logs plenty of those.
            // Compared on the RAW values, before either is resolved: two records
            // can share a name, and dropping such a row would hide a real edit.
            string oldValue = ChangeValue(Util.GetValueOfString(r["OldValue"]));
            string newValue = ChangeValue(Util.GetValueOfString(r["NewValue"]));
            if (string.Equals(oldValue, newValue, StringComparison.Ordinal)) return false;

            // ... and then reported as the field SHOWS them, not as the log stored
            // them: a reference reads as the referenced record's identifier, a list
            // value as its label, a date as the date alone.
            string column  = Util.GetValueOfString(r["FieldColumn"]);
            int refType    = Util.GetValueOfInt(r["RefType"]);
            int refValueId = Util.GetValueOfInt(r["RefValueId"]);

            list.Add(new InternalUseActivityData
            {
                Type        = "updated",
                FieldName   = field,
                OldValue    = _changeValues.Display(oldValue, column, refType, refValueId),
                NewValue    = _changeValues.Display(newValue, column, refType, refValueId),
                ChangeScope = scope,
                UserName    = Util.GetValueOfString(r["UserName"]),
                Created     = at
            });
            return true;
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
        /// Every date and timestamp this panel hands the client is read through
        /// here rather than through Util.GetValueOfDateTime directly, so the
        /// DateTimeKind the PROVIDER tagged the value with cannot reach the JSON.
        /// Oracle tags Unspecified and Npgsql tags Utc or Local; Newtonsoft writes
        /// a zone designator for the latter two and none for the first, and the
        /// panel's parseDbDate reads the two shapes differently - which is why the
        /// Activity feed's times were hours out on PostgreSQL. A no-op for a value
        /// that is already Unspecified, so the Oracle path is untouched. See
        /// VAS_ActivitySourcesModel.Stamp for the full account.
        /// </summary>
        private static DateTime? Stamp(object value)
        {
            return VAS_ActivitySourcesModel.Stamp(value);
        }

        /// <summary>
        /// The correspondence and engagement sources shared with every other
        /// overview panel: appointments and tasks (AppointmentsInfo, split on
        /// IsTask), calls (VA048_CallDetails) and letters (MailAttachment1,
        /// AttachmentType 'I'), each pinned to the issue by AD_Table_ID +
        /// Record_ID.
        ///
        /// Mails stay with LoadEmailActivity, which carries the recipient and body
        /// detail the mail drawer needs and now excludes letters so the two kinds
        /// cannot both claim the same row.
        /// </summary>
        private void LoadSharedSourceActivity(int M_Inventory_ID, List<InternalUseActivityData> list)
        {
            List<VAS_ActivitySourceRow> rows =
                _activitySources.Load("M_Inventory", M_Inventory_ID, false);
            foreach (VAS_ActivitySourceRow s in rows)
            {
                list.Add(new InternalUseActivityData
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

        /// <summary>
        /// The issue's own milestones: "created" (Created / CreatedBy), "updated"
        /// (Updated / UpdatedBy, only when it differs from the create stamp) and,
        /// for a completed or closed issue, "completed" — the workflow stamp when
        /// there is one, else the last change.
        /// </summary>
        private void LoadIssueMilestones(
            int M_Inventory_ID, string docStatus, List<InternalUseActivityData> list)
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
                DateTime? created = Stamp(r["Created"]);
                DateTime? updated = Stamp(r["Updated"]);
                string updatedBy  = Util.GetValueOfString(r["UpdatedByName"]);

                list.Add(new InternalUseActivityData
                {
                    Type     = "created",
                    UserName = Util.GetValueOfString(r["CreatedByName"]),
                    Created  = created
                });

                // An issue saved once has Updated == Created; that is not an edit.
                if (updated.HasValue &&
                    (!created.HasValue || updated.Value > created.Value.AddSeconds(1)))
                {
                    list.Add(new InternalUseActivityData
                    {
                        Type     = "updated",
                        UserName = updatedBy,
                        Created  = updated
                    });
                }

                if (docStatus == "CO" || docStatus == "CL")
                {
                    DateTime? completedAt = GetCompletedDate(M_Inventory_ID);
                    string completedBy    = _lastCompletedByName;

                    list.Add(new InternalUseActivityData
                    {
                        Type     = "completed",
                        UserName = !string.IsNullOrEmpty(completedBy) ? completedBy : updatedBy,
                        Created  = completedAt.HasValue ? completedAt : updated
                    });
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadIssueMilestones (M_Inventory_ID=" + M_Inventory_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// Adds a "posted" entry from the earliest Fact_Acct row written for the
        /// issue, carrying the user who ran the posting.
        ///
        /// The table id is matched with IN + UPPER for the same reason as
        /// <see cref="GetPostedDate"/>: joining on TableName = 'M_Inventory' finds
        /// nothing on a dictionary that spells the name in another case or carries
        /// more than one row for it, and the trail then lost its posting entry on
        /// exactly the deployments where the timeline lost its posting date.
        /// </summary>
        private void LoadPostingActivity(int M_Inventory_ID, List<InternalUseActivityData> list)
        {
            try
            {
                string sql = @"SELECT fa.Created, u.Name AS UserName
                                 FROM Fact_Acct fa
                                 LEFT OUTER JOIN AD_User u ON (fa.CreatedBy = u.AD_User_ID)
                                WHERE fa.Record_ID = @M_Inventory_ID
                                  AND fa.AD_Table_ID IN
                                      (SELECT t.AD_Table_ID FROM AD_Table t
                                        WHERE UPPER(t.TableName) = 'M_INVENTORY')
                                ORDER BY fa.Created";
                DataSet ds = DB.ExecuteDataset(sql, InventoryParam(M_Inventory_ID), null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return;

                DataRow r = ds.Tables[0].Rows[0];
                list.Add(new InternalUseActivityData
                {
                    Type     = "posted",
                    UserName = Util.GetValueOfString(r["UserName"]),
                    Created  = Stamp(r["Created"])
                });
            }
            catch (Exception ex)
            {
                _log.Severe("LoadPostingActivity (M_Inventory_ID=" + M_Inventory_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// Adds free-text chat notes (CM_ChatEntry) logged against this issue —
        /// each carrying the commenter's name, the moment it was posted and the
        /// comment text, which is what the panel prints on the row.
        ///
        /// The author is taken from CM_ChatEntry.AD_User_ID, falling back to
        /// CreatedBy: an entry logged through the platform's own chat plumbing
        /// often leaves AD_User_ID null, which left the activity feed printing a
        /// bare timestamp with no name against the comment.
        /// </summary>
        private void LoadNoteActivity(int M_Inventory_ID, List<InternalUseActivityData> list)
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
                                -- IN + UPPER, like the mail loader below: a scalar
                                -- sub-select RAISES on Oracle where AD_Table holds
                                -- more than one row named M_Inventory, and the
                                -- case-sensitive name matched nothing at all in a
                                -- dictionary that spells it any other way. Either
                                -- way every note vanished from the feed.
                                WHERE ch.AD_Table_ID IN
                                      (SELECT t.AD_Table_ID FROM AD_Table t
                                        WHERE UPPER(t.TableName) = 'M_INVENTORY')
                                  AND ch.Record_ID = @M_Inventory_ID
                                  AND ce.IsActive  = 'Y'";
                DataSet ds = DB.ExecuteDataset(sql, InventoryParam(M_Inventory_ID), null);
                if (ds == null || ds.Tables.Count == 0) return;

                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    list.Add(new InternalUseActivityData
                    {
                        Type     = "note",
                        Text     = Util.GetValueOfString(r["CharacterData"]),
                        UserName = Util.GetValueOfString(r["UserName"]),
                        Created  = Stamp(r["Created"])
                    });
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadNoteActivity (M_Inventory_ID=" + M_Inventory_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// Adds the e-mails sent against this issue (MailAttachment1, joined by
        /// AD_Table_ID = M_Inventory + Record_ID = the issue id) as "email" rows:
        /// recipient (MailAddress), subject (Title), body (TextMsg), when
        /// (Created) and who sent it (CreatedBy).
        ///
        /// The body travels with the row so the panel can reveal it on click
        /// without a second round trip.
        /// </summary>
        private void LoadEmailActivity(int M_Inventory_ID, List<InternalUseActivityData> list)
        {
            try
            {
                // A row is an e-mail when it has somewhere to go — a recipient on
                // any of the address columns. AttachmentType is deliberately NOT
                // filtered on: its value varies between installations, and a mail
                // that carries an address is a mail whatever the column says.
                // Rows with no recipient at all (a stored letter / inbound
                // document) are the ones this leaves out.
                //
                // "Has an address" is tested against a SPACE, not against ''.
                // Oracle stores the empty string as NULL, so COALESCE(TRIM(x), '')
                // yields NULL and `<> ''` compares against NULL — the predicate
                // is UNKNOWN for every row, including the ones that DO carry an
                // address, and the query returned no mails at all. Comparing to
                // ' ' keeps the NVL fallback non-null on Oracle, and SQL Server
                // blank-pads the comparison so an empty address still fails it.
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
                                  AND COALESCE(ma.IsActive, 'Y') = 'Y'
                                  -- Letters ('I') and only letters are filtered
                                  -- out: they are a kind of their own now and
                                  -- LoadSharedSourceActivity reads them, so leaving
                                  -- them here would report each one twice. Every
                                  -- other AttachmentType still counts as a mail.
                                  AND COALESCE(TO_CHAR(ma.AttachmentType), 'M') <> 'I'
                                  AND (COALESCE(TRIM(ma.MailAddress), ' ')     <> ' '
                                    OR COALESCE(TRIM(ma.MailAddressCc), ' ')   <> ' '
                                    OR COALESCE(TRIM(ma.MailAddressBcc), ' ')  <> ' ')
                                ORDER BY ma.Created DESC";
                DataSet ds = DB.ExecuteDataset(sql, InventoryParam(M_Inventory_ID), null);
                if (ds == null || ds.Tables.Count == 0) return;

                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    list.Add(new InternalUseActivityData
                    {
                        Type       = "email",
                        // Text is the row's headline everywhere in this feed; for
                        // an e-mail that is its subject.
                        Text       = Util.GetValueOfString(r["Title"]),
                        // Mails sent as HTML store their markup in TextMsg; the
                        // panel shows a body as text, so it is flattened here.
                        Body       = MailBodyToText(Util.GetValueOfString(r["TextMsg"])),
                        MailTo     = Util.GetValueOfString(r["MailAddress"]),
                        MailCc     = Util.GetValueOfString(r["MailAddressCc"]),
                        MailBcc    = Util.GetValueOfString(r["MailAddressBcc"]),
                        MailFrom   = Util.GetValueOfString(r["MailAddressFrom"]),
                        IsMailSent = Util.GetValueOfString(r["IsMailSent"]) == "Y",
                        UserName   = Util.GetValueOfString(r["UserName"]),
                        Created    = Stamp(r["Created"])
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
        /// A mail sent as HTML stores its markup here, and the panel shows the
        /// body as text — so without this the reader gets tags instead of a
        /// message. Block-level markup becomes line breaks, table cells become
        /// tabs, everything else is dropped and entities are decoded last, so
        /// the browser still receives text it can safely escape: no markup is
        /// ever handed to the panel. A body with no markup is returned as it
        /// was stored.
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
                s = s.Replace('\u00A0', ' ');               // nbsp reads as a space

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

        /// <summary>The single-id parameter every child query takes.</summary>
        private SqlParameter[] InventoryParam(int M_Inventory_ID)
        {
            return new SqlParameter[]
            {
                new SqlParameter("@M_Inventory_ID", M_Inventory_ID)
            };
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

        public class InternalUseLineData
        {
            public int      M_InventoryLine_ID { get; set; }
            public int      Line               { get; set; }
            public string   Description        { get; set; }
            public int      M_Product_ID       { get; set; }
            public string   ProductCode        { get; set; }   // SKU
            public string   ProductName        { get; set; }
            public string   LocatorCode        { get; set; }
            public string   LocatorName        { get; set; }
            public string   UOMName            { get; set; }
            public int      UOMPrecision       { get; set; }
            public decimal  RequestedQty       { get; set; }   // M_RequisitionLine.Qty, else QtyEntered
            public decimal  IssuedQty          { get; set; }   // QtyInternalUse
            public decimal  AvailableQty       { get; set; }   // M_Storage on-hand, in BASE uom
            /// <summary>The product's own unit, which on-hand is reported in.</summary>
            public string   BaseUOMName        { get; set; }
            public int      BaseUOMPrecision   { get; set; }
            public decimal  UnitRate           { get; set; }
            public decimal  LineValue          { get; set; }
            // Where UnitRate came from: LINE (this line's cost detail),
            // WAREHOUSE (product cost at the issue's warehouse) or PRICE (the
            // line's own cost / price columns).
            public string   CostSource         { get; set; }
            public int      RequisitionLineID  { get; set; }
            public string   RequisitionNo      { get; set; }   // parent requisition doc no
            public string   AttributeSetInstance { get; set; } // lot / serial / attributes
            public int      WorkOrderID        { get; set; }
        }

        /// <summary>One entry in the issue's audit trail.</summary>
        public class InternalUseActivityData
        {
            /// <summary>created | updated | completed | posted | note | email</summary>
            public string    Type       { get; set; }
            public string    UserName   { get; set; }   // actor / mail sender
            public DateTime? Created    { get; set; }
            public string    DocumentNo { get; set; }
            public string    Text       { get; set; }   // note body / e-mail subject
            /// <summary>For an "updated" row: the display name of the field that
            /// changed. Empty on the generic header-stamp row that stands in when
            /// change logging is off.</summary>
            public string    FieldName  { get; set; }
            /// <summary>For an "updated" row: which record the edit landed on —
            /// "" for the issue header, else the line's number and product
            /// ("#10 Steel Bolt M8"). An issue's substantive edits are its issued
            /// quantities, and those live on the lines.</summary>
            public string    ChangeScope { get; set; }
            // The move itself, for an "updated" row: what the field held
            // before the edit and what it holds after. Either side is empty
            // where the log recorded no value — a field cleared, or filled
            // for the first time.
            public string    OldValue    { get; set; }
            public string    NewValue    { get; set; }

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
            /// are correspondence about the ISSUE. Empty on every other type; the
            /// bodies travel with the row so the panel reveals them on click
            /// without a second round trip.</summary>
            public List<VAS_ActivityMailRow> Mails { get; set; }
        }

        /// <summary>
        /// One note shown in the Notes section: the issue header's description, or
        /// the description entered on one of its lines (the child tab).
        /// </summary>
        public class NoteData
        {
            public string NoteType { get; set; }   // header | line
            public string Text     { get; set; }
        }

        /// <summary>A requisition the issue's lines were raised against.</summary>
        public class RequisitionRefData
        {
            public int       M_Requisition_ID { get; set; }
            public string    DocumentNo       { get; set; }
            public DateTime? DateDoc          { get; set; }
            public DateTime? DateRequired     { get; set; }
            public string    Description      { get; set; }
            public string    PreparerName     { get; set; }
        }

        public class InternalUseOverviewData
        {
            // Header / identity
            public int       M_Inventory_ID { get; set; }
            public string    DocumentNo     { get; set; }
            public string    StatusCode     { get; set; }   // DocStatus code
            public bool      Processed      { get; set; }
            public bool      Posted         { get; set; }
            public DateTime? MovementDate   { get; set; }
            public string    Description    { get; set; }
            public string    WarehouseName  { get; set; }
            public string    IssuedBy       { get; set; }

            // Timeline stamps: when the record was created, completed and posted
            // — none of them the movement date.
            public DateTime? CreatedDate    { get; set; }
            public DateTime? UpdatedDate    { get; set; }
            public DateTime? CompletedDate  { get; set; }
            public string    CompletedBy    { get; set; }
            public DateTime? PostedDate     { get; set; }

            // Origin (derived from the linked source ids on the lines)
            // WORKORDER | PRODUCTION | REQUISITION | PROJECT | MANUAL
            public string    OriginCode     { get; set; }
            public bool      HasWorkOrder   { get; set; }
            public bool      HasRequisition { get; set; }
            public bool      HasProject     { get; set; }

            // Reference documents (References section)
            public int       M_Requisition_ID { get; set; }  // first linked requisition (chip target)
            public string    RequisitionNo    { get; set; }  // first linked requisition
            public DateTime? RequisitionDate  { get; set; }
            public DateTime? DateRequired     { get; set; }
            public string    RequestedBy      { get; set; }
            public string    RequisitionNote  { get; set; }
            public int       RequisitionCount { get; set; }
            // VA075 service work order
            public string    WorkOrderNo      { get; set; }
            public string    WorkOrderRef     { get; set; }   // the work order's own reference
            public int       VA075_WorkOrder_ID { get; set; }
            public int       WorkOrderCount   { get; set; }

            // VAMFG manufacturing production order — a separate document to the
            // service work order above, and labelled as one in the panel.
            public string    ProductionOrderNo    { get; set; }
            public int       VAMFG_M_WorkOrder_ID { get; set; }
            public int       ProductionOrderCount { get; set; }

            // Project (M_Inventory.C_Project_ID) — the chip's target, its search
            // key as the chip value and its name for the tooltip.
            public int       C_Project_ID { get; set; }
            public string    ProjectNo    { get; set; }   // C_Project.Value
            public string    ProjectName  { get; set; }   // C_Project.Name

            // Currency
            public int       StdPrecision   { get; set; }

            // KPI aggregates
            public int       LineCount    { get; set; }
            public decimal   RequestedQty { get; set; }
            public decimal   IssuedQty    { get; set; }
            public decimal   TotalValue   { get; set; }
            public int       NotFullCount { get; set; }
            // Requested minus issued across every not-fully-issued line.
            public decimal   NotFullQty   { get; set; }

            // Collections
            public List<InternalUseLineData>     Lines        { get; set; }
            public List<RequisitionRefData>      Requisitions { get; set; }
            public List<InternalUseActivityData> Activity     { get; set; }
            // Header description + each line's own description (child tab).
            public List<NoteData>                Notes        { get; set; }
        }
    }
}
