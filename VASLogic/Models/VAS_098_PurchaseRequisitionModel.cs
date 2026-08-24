/// <summary>
/// Module Name : VASLogic
/// Purpose     : Purchase Requisition overview tab panel data (read side).
///               Returns header identity, requester / preparer, origin +
///               warehouse route, budget-aware stat data, a 6-stage progress
///               model, line items with source-stock availability, a typed
///               activity feed and notes for a selected requisition
///               (M_Requisition) — consumed by the
///               VAS.VAS_098_PurchaseRequisition tab panel.
/// Chronological development:
///   VAI163   2026-07-01  Created. Core header / line queries use only standard
///                        M_Requisition(Line) columns; the custom columns from
///                        the verified mapping (DTD001_MWarehouseSource_ID,
///                        IsBudgetBreach / VAS_BudgetBreach, reserved / ordered /
///                        available-budget line columns) are loaded in guarded
///                        enrichment passes so a missing column degrades the
///                        affected section rather than breaking the panel.
///   VAI163   2026-07-02  Currency now sourced from the requisition's price list
///                        (M_PriceList.C_Currency_ID) instead of M_Requisition.
///   VAI163   2026-07-28  Source warehouse (DTD001_MWarehouseSource_ID) is read in
///                        its own dictionary-guarded query. It previously shared a
///                        SELECT with the budget-breach columns, so a schema
///                        missing either of those failed the whole statement and
///                        the warehouse came back empty — the panel then showed
///                        "External Procurement" and the wrong procurement type.
///   VAI163   2026-07-28  Lines now carry the Attribute Set Instance description
///                        (guarded on M_RequisitionLine.M_AttributeSetInstance_ID,
///                        joined only for a real instance id). Dropped the derived
///                        Contingency total — the panel no longer shows it.
///   VAI163   2026-07-28  - Source stock is now real on-hand stock: M_Storage
///                          .QtyOnHand summed over the locators of the source
///                          warehouse (DTD001_MWarehouseSource_ID), matched on the
///                          line's product and its attribute set instance when it
///                          has one. It used to be M_RequisitionLine reserved qty.
///                        - LoadLineExtras guards each optional column separately.
///                          QtyOrdered is not a standard M_RequisitionLine column,
///                          so the single combined SELECT was failing outright and
///                          silently zeroing reserved / ordered / budget for every
///                          line — which also kept the In Fulfilment stage dark.
///                        - Added the progress milestones (completed / converted /
///                          in-fulfilment / closed), derived from the requisition's
///                          workflow and from the purchase orders reached through
///                          M_RequisitionLine.C_OrderLine_ID.
///   VAI163   2026-07-28  - Activity now carries the downstream lifecycle: the
///                          purchase orders raised from the requisition and the
///                          goods receipts booked against them (created and
///                          completed), each with its document no and timestamp.
///                        - AvailableBudget sums DISTINCT line amounts. Every line
///                          on the same budget control carries that control's
///                          available budget, so adding them multiplied one budget
///                          by the line count.
///   VAI163   2026-08-11  - Activity carries the e-mails sent against the
///                          requisition (LoadEmailActivity, MailAttachment1 by
///                          AD_Table_ID = M_Requisition + Record_ID): recipient
///                          (MailAddress), subject (Title), body (TextMsg), when
///                          (Created) and who sent it (CreatedBy). The body
///                          travels with the row so the panel can reveal it on
///                          click without a second round trip, and an HTML mail is
///                          flattened to plain text (MailBodyToText). The feed is
///                          capped at 200 as a runaway guard — the panel pages it
///                          15 rows at a time, so a cut entry is unreachable
///                          rather than merely further down.
///                        - Added LoadBudgetAmount: when no line carries a
///                          calculated VAS_AvailableBudget — the usual case, since
///                          "Calculate Budget" only runs where budget control is
///                          configured — the Budget figure falls back to the
///                          amount budgeted under the GL budget that applies to
///                          this requisition (org, commitment type 'B', and the
///                          document date inside the budget's period / year), read
///                          from Fact_Acct the way BudgetCheck derives it. It does
///                          NOT narrow by the control's dimensions, which is
///                          per-line and needs the posting record, so it is a
///                          broader figure and the stamped value still wins.
///                          BudgetIsRequisitionLevel tells the panel which of the
///                          two it is showing.
///   VAI163   2026-08-11  - A CHARGE line is left without source-stock data
///                          (LoadSourceStock). A charge is not a product and is
///                          not held anywhere, but it was being given
///                          HasSourceData = true with an on-hand of 0 — so it
///                          drew a 0-of-n bar on the row and was tallied as a
///                          SHORT line against the requisition's source
///                          availability. It now reports neither.
///   VAI163   2026-08-12  - Added LoadDocuments: the documents raised FROM the
///                          requisition, for the panel's new Documents section —
///                          the purchase orders its lines were converted into
///                          (M_RequisitionLine.C_OrderLine_ID), the RFQs issued
///                          against it (C_RfQ.M_Requisition_ID) and the material
///                          transfers fulfilling it
///                          (M_MovementLine.M_RequisitionLine_ID, a DTD001
///                          column and so dictionary-guarded). Each row carries
///                          TableName + RecordId so the panel can open it, and
///                          each source swallows its own failure so one absent
///                          module drops only its own rows. An RFQ and a
///                          movement report a null Amount — neither has a
///                          monetary total, which is not the same as zero.
///                        - Added LoadOrigin: where the requisition came from,
///                          for the panel's new Reference strip — replenishment
///                          (the Replenish Report stamps no id, only the
///                          "Replenishment" description, so the marker is that
///                          message resolved in the caller's own language), the
///                          project its lines were copied from
///                          (M_RequisitionLine.C_Project_ID / C_ProjectLine_ID),
///                          the VA075 field-service work order and the VAMFG
///                          production order. The last two are read with plain
///                          SQL against dictionary-guarded columns on the header
///                          AND the line — neither module is part of this
///                          solution.
///                        - Added GetWindowId (ported from VAS_092), so the
///                          panel can open a record whose screen is not its
///                          table's default zoom target.
///   VAI163   2026-08-12  - Added PostedDate (GetPostedDate: the earliest
///                          Fact_Acct row written for the requisition), which
///                          dates the progress line's new Posted stage.
///                          M_Requisition.Posted says WHETHER it posted; this
///                          says when.
///                        - Every activity row carries its actor in UserName.
///                          The create / status milestones put it in Text — the
///                          row's HEADLINE field — and the PO / GRN rows wrote it
///                          into both, so the panel had to splice the name into
///                          the sentence it builds and a row whose actor was
///                          missing read as a bare action. Text is now only ever
///                          a headline: a comment's body, a mail's subject.
///   VAI163   2026-08-12  - Lines report their quantity and unit price in the
///                          line's SELECTED unit (QtyEntered and PriceActual
///                          scaled by UomRatio, the base-units-per-selected-unit
///                          the line itself was saved with). The panel labels
///                          every row with that unit while being handed the
///                          product's BASE figures, so a line keyed as 2 BOX of
///                          an EA-held product reported 24 at the per-EA price.
///                          LineNetAmt is unchanged by the restatement:
///                          QtyEntered x (PriceActual x ratio) = Qty x
///                          PriceActual.
///                        - LoadSourceStock reads the requisition's own warehouse
///                          (M_Requisition.M_Warehouse_ID) when it has no source
///                          warehouse — where a receipt raised from its PO
///                          delivers — instead of skipping the figure entirely.
///                          An externally procured requisition reported no stock
///                          at all, so the column never reflected the GRN that
///                          satisfied it. StockWarehouseName says which of the two
///                          the figure came from.
///                        - Added LoadReceivedQty: what each line has had booked
///                          in against it on a COMPLETED goods receipt, through
///                          C_OrderLine_ID -> M_InOutLine. Drafted receipts do not
///                          count — they have moved no stock.
///                          Every stock figure is converted onto the selected unit
///                          like the quantities above.
///                        - LoadBudgetAmount falls back to LoadBudgetForPeriod:
///                          the budget covering the requisition's document date
///                          with no commitment-CONTROL requirement. The controlled
///                          read asks for a GL_BudgetControl row, of commitment
///                          type 'B', visible to the requisition's org; a
///                          deployment that budgets without controlling its
///                          requisitions has none, and the Budget figure read N/A
///                          on every record.
///   VAI163   2026-08-12  - The VA075 origins are read through one guarded helper
///                          (LoadVA075Reference): the id column on the header
///                          and / or the lines, and an identifier column on the
///                          referenced table PROBED rather than assumed —
///                          VA075 is not part of this solution and its column
///                          names vary by revision, so requiring DocumentNo
///                          outright meant a schema naming it otherwise drew no
///                          chip at all.
///                        - Added the field service request origin
///                          (M_Requisition.VA075_FieldServiceReq_ID ->
///                          VA075_FieldServiceReq), a separate document to the
///                          work order and so a chip of its own.
///                        - Added GetWindowIdByTable (ported from VAS_102): the
///                          window a TABLE opens in, for a chip whose screen
///                          cannot be named on the client. Both VA075 chips need
///                          it — nothing else can resolve their windows.
///   VAI163   2026-08-12  - Added LoadSalesOrderOrigin: the order whose demand the
///                          requisition serves (M_RequisitionLine.Ref_OrderLine_ID
///                          -> C_OrderLine -> C_Order). IsSOTrx travels with it —
///                          C_Order is dual-purpose, and calling a purchase order
///                          a sales order because of where the link points would
///                          be worse than saying nothing.
///                        - LoadProductionOrderOrigin also reaches the order
///                          through M_RequisitionLine.VAMFG_M_WorkOrderComponent_ID
///                          (-> VAMFG_M_WorkOrderComponent.VAMFG_M_WorkOrder_ID):
///                          a line raised for one COMPONENT of a production order
///                          carries the component, not the order. Its identifier
///                          column is probed rather than assumed, as the VA075
///                          ones are — VAMFG is not part of this solution either.
///   VAI163   2026-08-14  - Activity reports edits FIELD BY FIELD
///                          (LoadChangeActivity / AddChangeRow): one "updated" row
///                          per AD_ChangeLog entry, naming the column that changed,
///                          who changed it and when, for the requisition header AND
///                          its lines — a requisition's substantive edits are its
///                          requested quantities and products, and those live on the
///                          lines. Until this the feed's only account of a change
///                          was the single "status" milestone, derived from
///                          M_Requisition.Updated, which is the LAST save and says
///                          nothing about what it touched. Table names are matched
///                          with UPPER, since an equality on the stored spelling
///                          fails silently and reads exactly like the loader was
///                          never written.
///                        - A chat comment's author now resolves from
///                          CM_ChatEntry.AD_User_ID falling back to CreatedBy. An
///                          entry logged through the platform's own chat plumbing
///                          leaves AD_User_ID null, so asking that column alone
///                          printed the comment and its timestamp with NO NAME
///                          against it — which is most of them. Its AD_Table lookup
///                          became an IN + UPPER for the same reason as the mail
///                          one: as a scalar sub-select a second row RAISES, and
///                          the whole comment feed would vanish with it.
///   VAI163   2026-08-17  Field-level activity carries the OLD and NEW values
///                        (AD_ChangeLog.OldValue / NewValue). Both are normalised
///                        through ChangeValue: the literal "null" the platform
///                        writes for a cleared field reads as empty, not as the
///                        word. A row whose two values are equal is dropped — a
///                        save that rewrote a field with the value it already had
///                        is not an edit, and the platform logs plenty of those.
///                        The trail said WHICH field moved but never what it moved
///                        from or to. Follows VAS_101 / VAS_104.
///   VAI163   2026-08-20  - The line's unit price is restated onto its selected
///                          unit through the product's UOM CONVERSION
///                          (MUOMConversion.GetProductRateFrom, the base units one
///                          selected unit is) rather than through the ratio between
///                          the line's own two quantities. QtyEntered is an
///                          optional column and is not maintained by every path
///                          that writes a line — a replenishment / project /
///                          work-order line carries Qty only — so the derived ratio
///                          collapsed to 1 and the row showed the BASE price under
///                          a "BOX" label. The quantity is derived from the same
///                          rate when the line carries no entered quantity, so the
///                          two still read against each other. The quantity ratio
///                          remains the fallback for a line whose product has no
///                          conversion (a charge line, chiefly).
///                        - LoadBudgetAmount gains its last fallback,
///                          LoadBudgetForYear: the amount budgeted under ANY GL
///                          budget of the requisition's tenant and organisation,
///                          within the fiscal YEAR its document date falls in. The
///                          two reads above it both require GL_Budget
///                          .BudgetControlBasis to be 'P' or 'A' — a column only
///                          meaningful where commitment control is configured, and
///                          left null by a deployment that merely budgets — so a
///                          posted budget existed and the footer still read N/A.
///                          The whole loader no longer returns early when that
///                          column is absent; only the two reads that need it are
///                          skipped.
///                        - The budget overage is recomputed after the fallback
///                          runs. It was derived in RollUpTotals, which is over
///                          before any of these reads, so a breach only ever
///                          reported an amount when a LINE carried the budget.
///                        - LoadMovementDocuments no longer groups by the value
///                          sub-select. Oracle rejects a subquery expression in
///                          GROUP BY outright (ORA-22818), so the statement failed,
///                          the catch swallowed it, and the Documents section
///                          listed NO material transfers at all — the aggregate is
///                          now taken over a derived table and the value read
///                          beside it. The transfer is also always priced, exactly
///                          as VAS_103 prices it, so the row shows the same total
///                          the Material Transfer screen does instead of a dash.
///                        - GetWindowIdByTable validates AD_Table.AD_Window_ID
///                          before returning it: the window is only usable when its
///                          FIRST tab sits on the table, since the panel starts it
///                          with an equal-query on that table's key column. The
///                          MIN(SeqNo) test was added to the fallback query alone,
///                          so a zoom target pointing at a window that carries the
///                          table on a CHILD tab still came back and the framework
///                          raised on screen — which is what a click on the field
///                          service request and the production order chips
///                          reported. Both are now also filtered by the role's
///                          window access, and the resolved id travels with the
///                          payload (LoadOriginWindows) so a chip with no window
///                          renders as plain text rather than as a link that
///                          cannot open.
///   VAI163   2026-08-20  - An Inventory Use issue raised from the requisition now
///                          reports its total value (LoadInternalUseDocuments),
///                          summed the way VAS_102 sums its own: QtyInternalUse x
///                          the line's cost / VA024 unit price. The row reported
///                          no amount at all, on the argument that an issue's
///                          value is a costing question — but the issue's own
///                          screen answers it, and the two should not disagree.
///                          Restructured over a derived table for the same
///                          ORA-22818 reason as the movement query.
///                        - Converted / In Fulfilment now answer for EVERY
///                          document a requisition can be turned into, not only
///                          its purchase orders (LoadConversionMilestones): the
///                          RFQs issued from it, the material transfers and the
///                          inventory-use issues fulfilling it, alongside the
///                          purchase and blanket orders LoadLinkedOrderMilestones
///                          already read. Converted is the EARLIEST of their
///                          creation stamps and In Fulfilment the earliest
///                          completion, so a requisition served by a transfer no
///                          longer sat at "ready to convert" for the life of the
///                          document. IsConverted is raised by any of them too —
///                          it was C_OrderLine_ID alone, which only a purchase
///                          order writes.
///                        - Lines report what has been RECEIVED against them from
///                          every fulfilling document, not only from goods
///                          receipts (LoadReceivedQty): the completed material
///                          transfers (M_MovementLine.MovementQty) and
///                          inventory-use issues (M_InventoryLine.QtyInternalUse)
///                          raised against the line are added to the receipts
///                          reached through C_OrderLine_ID. All three are stored
///                          in the product's base unit and are brought onto the
///                          line's selected unit together.
///                        - Added LoadBlanketOrderOrigin: the blanket order the
///                          requisition was raised against, reached from the line's
///                          Ref_OrderLine_ID either directly (the referenced order
///                          IS the blanket) or through the release it points at
///                          (C_OrderLine.C_OrderLine_Blanket_ID, else
///                          C_Order.C_Order_Blanket — the link is written in two
///                          places by two different code paths, so both are read).
///                          IsSOTrx travels with it, so a blanket SALES order opens
///                          its own screen. LoadSalesOrderOrigin stands down when
///                          the order it found is that same blanket, so the strip
///                          never carries one record twice.
///                        - Window resolution follows VAS_092's order
///                          (ResolveChipWindow): the window NAMED for the record
///                          first, and only then the dictionary's table lookup.
///                          LoadOriginWindows went straight to the table — the last
///                          resort, meant for a screen that cannot be named — and
///                          the production order's screen can be: VAS_102 opens it
///                          as VAMFG_ProductionOrder. Only the two VA075 documents
///                          have no nameable window left.
///                        - GetWindowId filters by the ROLE's window access, as
///                          GetWindowIdByTable does, and walks every candidate
///                          rather than taking the first row. A window the role
///                          cannot see raises when the framework starts it, and the
///                          point of resolving up front is that a chip is drawn as
///                          plain text instead.
///   VAI163   2026-08-21  Activity: an appointment or task now carries the
///                        e-mails sent against IT - MailAttachment1 keyed on
///                        AppointmentsInfo rather than on this panel's own
///                        table - with the recipient (MailAddress), subject
///                        (Title), when (Created) and who sent it (CreatedBy).
///                        The body (TextMsg, flattened) travels with the row so
///                        the panel reveals it on click. Read in one query for
///                        the whole feed through VAS_ActivitySourcesModel.
///   VAI163   2026-08-24  LoadReceivedQty's goods-receipt side now reaches every
///                        order line the requisition line was ordered on, not one:
///                        - It matches the order line BOTH ways —
///                          C_OrderLine.M_RequisitionLine_ID as well as
///                          M_RequisitionLine.C_OrderLine_ID. The forward link is
///                          what the platform itself reads (MOrder's completion
///                          hook, MRequisition.UpdateRequisitionStatus) and is the
///                          only ONE-TO-MANY one: the requisition line holds a
///                          single C_OrderLine_ID, so a line split across two
///                          purchase orders — two vendors, or a top-up after a
///                          short delivery — counted only one order's receipts and
///                          read as permanently short however much had arrived.
///                        - A PO raised from an RFQ is reached at all. RfQCreatePO
///                          builds its order lines from the winning response and
///                          stamps NO requisition reference on them, so a
///                          requisition tendered through an RFQ reported 0 received
///                          forever. The surviving link is the one the RFQ was
///                          built with (C_RfQLine.M_RequisitionLine_ID), so the
///                          chain runs requisition line -> RFQ line -> response
///                          line -> C_RfQResponse.C_Order_ID -> that order's lines,
///                          matched on product because nothing finer exists.
///                          Dictionary-guarded, and it skips a requisition line
///                          whose product sits on more than one line of the same
///                          RFQ: product is the whole match there, so two such
///                          lines would each claim BOTH order lines' receipts.
///                        Both branches sum over a SELECT DISTINCT keyed on
///                        M_InOutLine_ID, so a receipt line reachable by more than
///                        one route is counted once — summing the join directly
///                        would multiply it by the number of routes.
///   VAI163   2026-08-24  Documents: an INVENTORY USE row shows the issue's whole
///                        value again. BuildInventoryRateExpr valued each line at
///                        COALESCE(CurrentCostPrice, VA024_UnitPrice, 0) — both
///                        optional module columns that a great many issues never
///                        carry — so a completed issue whose value lives in
///                        M_CostDetail reported 0, and one where only some lines
///                        carried a price column reported PART of the total, which
///                        is worse because a partial figure does not look wrong.
///                        It now resolves the rate the way VAS_102's own panel
///                        does: the cost booked against the line (newest
///                        M_CostDetail row, Amt / Qty), else the product's latest
///                        cost at the issue's warehouse, else those two columns.
///                        Each cost lookup is NULLIF(..., 0) so a zero-valued row
///                        falls through instead of pinning the rate at zero, which
///                        is the same test VAS_102 makes in C#. The old comment
///                        called itself "VAS_102's arithmetic, column for column" —
///                        true when written, and untrue from the day VAS_102 moved
///                        to M_CostDetail.
///                        The MATERIAL TRANSFER rate is deliberately untouched:
///                        VAS_103 still prices from CurrentCostPrice /
///                        VA024_UnitPrice, so BuildMovementRateExpr already agrees
///                        with the screen it mirrors.
///   VAI163   2026-08-24  Reference strip — two chips that opened the wrong screen
///                        or none:
///                        - BLANKET ORDER. Its window is now resolved on the SERVER
///                          (BlanketOrderWindowId), by name and against the role,
///                          and deliberately WITHOUT the table fallback every other
///                          chip keeps behind it. The client resolved the name for
///                          itself and, when that came back empty, fell through to
///                          C_Order's zoom target — the ordinary order screen, or
///                          for a sales-side record the Sales Order screen. The
///                          click therefore opened a real window filtered to a
///                          record that window does not carry, which is why a
///                          blanket SALES order never reached VAS_BlanketSalesOrder.
///                          A wrong screen is worse than no link, so an unresolved
///                          name leaves the id at 0 and the chip is drawn as plain
///                          text.
///                        - FIELD SERVICE REQUEST. Its screen is NAMED now
///                          (VA075_FieldService) with the dictionary lookup left
///                          behind it, instead of going straight to the table:
///                          AD_Table.AD_Window_ID for VA075_FieldServiceReq
///                          resolves to a window that does not maintain the record,
///                          so the click reported an error rather than showing it.
///                        - Both VA075 origins are now gated on the MODULE
///                          (Env.IsModuleInstalled("VA075_")), not on their columns
///                          alone. A column guard proves a dictionary row exists; it
///                          does not prove the module is installed and its windows
///                          deployed, and a client carrying the rows without the
///                          module passed every ColumnExists and then had no screen
///                          to open.
/// </summary>

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Net;
using System.Text.RegularExpressions;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;

namespace VASLogic.Models
{
    public class VAS_098_PurchaseRequisitionModel
    {
        private static readonly VLogger _log =
            VLogger.GetVLogger(typeof(VAS_098_PurchaseRequisitionModel).FullName);

        /// <summary>
        /// Returns the full overview payload for the selected requisition.
        /// MRole access filtering is applied only on the main physical table
        /// (M_Requisition alias "r"); child line / activity queries inherit the
        /// parent's authorization.
        /// </summary>
        /// <param name="ctx">User context (client/org/role).</param>
        /// <param name="M_Requisition_ID">Selected requisition id.</param>
        /// <returns>Populated <see cref="RequisitionOverviewData"/>; an empty
        /// instance when the id is invalid or no accessible row is found.</returns>
        public RequisitionOverviewData GetRequisitionOverview(Ctx ctx, int M_Requisition_ID)
        {
            RequisitionOverviewData result = new RequisitionOverviewData();
            if (M_Requisition_ID <= 0) return result;

            if (!LoadHeader(ctx, M_Requisition_ID, result))
                return result;                       // invalid / no access -> empty

            LoadHeaderExtras(M_Requisition_ID, result);   // guarded custom columns

            result.Lines = LoadLines(ctx, M_Requisition_ID);
            LoadLineExtras(M_Requisition_ID, result.Lines); // guarded custom columns
            // Real on-hand stock at the source warehouse (needs the id resolved by
            // LoadHeaderExtras above), falling back to the requisition's own
            // warehouse — where a goods receipt raised from its PO delivers — plus
            // what each line has actually had received against it.
            result.StockWarehouseName = result.SourceWarehouseId > 0
                ? result.SourceWarehouseName : result.RequestWarehouseName;
            LoadSourceStock(M_Requisition_ID, result.SourceWarehouseId,
                            result.RequestWarehouseId, result.Lines);

            RollUpTotals(result);

            // Only when the lines carry no stamped budget of their own — see
            // LoadBudgetAmount.
            if (result.AvailableBudget == 0)
            {
                LoadBudgetAmount(M_Requisition_ID, result);
                // RollUpTotals derives the overage, and it ran before this: a
                // breach whose budget came from the fallback reported no amount at
                // all until the figure was in hand.
                if (result.IsBudgetBreach && result.AvailableBudget > 0 &&
                    result.EstimatedValue > result.AvailableBudget)
                    result.BudgetOverage = result.EstimatedValue - result.AvailableBudget;
            }

            LoadMilestones(M_Requisition_ID, result);

            // Where the requisition came from (Reference chip strip) and what has
            // been raised from it (Documents). Both are entirely optional: every
            // one of their sources is column-guarded and try/caught, so a schema
            // without the module behind it simply contributes no chip and no row.
            LoadOrigin(ctx, M_Requisition_ID, result);
            result.Documents = LoadDocuments(M_Requisition_ID);

            result.Activity = LoadActivity(M_Requisition_ID, result);

            return result;
        }

        // ----------------------------------------------------------------- //
        //  Header                                                            //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// Loads the requisition header using only standard M_Requisition columns
        /// (so identity always renders). Returns false when no accessible row is
        /// found.
        /// </summary>
        private bool LoadHeader(Ctx ctx, int M_Requisition_ID, RequisitionOverviewData d)
        {
            // Posted is a standard M_Requisition column, but this query is the one
            // that must never fail, so it is included only when the dictionary
            // confirms it.
            bool hasPosted = ColumnExists("M_Requisition", "Posted");
            string postedExpr = hasPosted ? "r.Posted" : "'N'";

            string sql = @"SELECT
                              r.M_Requisition_ID,
                              r.DocumentNo,
                              r.DocStatus,
                              r.PriorityRule,
                              r.DateDoc,
                              r.DateRequired,
                              r.Description,
                              r.TotalLines,
                              r.Processed,
                              " + postedExpr + @" AS Posted,
                              r.Created,
                              r.Updated,
                              requester.Name  AS RequesterName,
                              preparer.Name   AS PreparerName,
                              cu.Name         AS CreatedByName,
                              uu.Name         AS UpdatedByName,
                              r.M_Warehouse_ID AS RequestWarehouseId,
                              reqwh.Name      AS RequestWarehouseName,
                              pl.Name         AS PriceListName,
                              cur.CurSymbol   AS CurSymbol,
                              cur.ISO_Code    AS ISO_Code,
                              cur.StdPrecision AS StdPrecision,
                              (SELECT COUNT(*)
                                 FROM M_RequisitionLine rl
                                WHERE rl.M_Requisition_ID = r.M_Requisition_ID
                                  AND rl.IsActive = 'Y'
                                  AND rl.C_OrderLine_ID IS NOT NULL)  AS ConvertedLineCount,
                              TRUNC(CURRENT_DATE) AS SystemDate
                           FROM M_Requisition r
                           LEFT OUTER JOIN C_BPartner requester ON (requester.C_BPartner_ID = r.C_BPartner_ID)
                           LEFT OUTER JOIN AD_User preparer     ON (preparer.AD_User_ID     = r.AD_User_ID)
                           LEFT OUTER JOIN AD_User cu           ON (cu.AD_User_ID           = r.CreatedBy)
                           LEFT OUTER JOIN AD_User uu           ON (uu.AD_User_ID           = r.UpdatedBy)
                           LEFT OUTER JOIN M_Warehouse reqwh    ON (reqwh.M_Warehouse_ID    = r.M_Warehouse_ID)
                           LEFT OUTER JOIN M_PriceList pl       ON (pl.M_PriceList_ID       = r.M_PriceList_ID)
                           -- VAI163 2026-07-02  Currency is taken from the requisition's
                           -- price list (M_PriceList.C_Currency_ID), not M_Requisition.
                           LEFT OUTER JOIN C_Currency cur       ON (cur.C_Currency_ID       = pl.C_Currency_ID)
                           WHERE r.M_Requisition_ID = @M_Requisition_ID
                             AND r.IsActive = 'Y'";

            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql, "r", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            DataSet ds = DB.ExecuteDataset(sql, ReqParam(M_Requisition_ID), null);
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
                return false;

            DataRow r = ds.Tables[0].Rows[0];
            d.M_Requisition_ID    = Util.GetValueOfInt(r["M_Requisition_ID"]);
            d.DocumentNo          = Util.GetValueOfString(r["DocumentNo"]);
            d.StatusCode          = Util.GetValueOfString(r["DocStatus"]);
            d.PriorityCode        = Util.GetValueOfString(r["PriorityRule"]);
            d.DateDoc             = Util.GetValueOfDateTime(r["DateDoc"]);
            d.DateRequired        = Util.GetValueOfDateTime(r["DateRequired"]);
            d.Description         = Util.GetValueOfString(r["Description"]);
            d.EstimatedValue      = Util.GetValueOfDecimal(r["TotalLines"]);
            d.Processed           = Util.GetValueOfString(r["Processed"]) == "Y";
            // Posted is a status list on some schemas ('Y' posted, 'N' not, 'E'
            // error ...); anything other than 'Y' is "not posted" for the badge,
            // and the raw code travels with it so the panel can flag an error.
            d.PostedCode          = Util.GetValueOfString(r["Posted"]);
            d.Posted              = d.PostedCode == "Y";
            d.Created             = Util.GetValueOfDateTime(r["Created"]);
            d.Updated             = Util.GetValueOfDateTime(r["Updated"]);
            d.RequesterName       = Util.GetValueOfString(r["RequesterName"]);
            d.PreparerName        = Util.GetValueOfString(r["PreparerName"]);
            d.CreatedByName       = Util.GetValueOfString(r["CreatedByName"]);
            d.UpdatedByName       = Util.GetValueOfString(r["UpdatedByName"]);
            d.RequestWarehouseId   = Util.GetValueOfInt(r["RequestWarehouseId"]);
            d.RequestWarehouseName = Util.GetValueOfString(r["RequestWarehouseName"]);
            d.PriceListName       = Util.GetValueOfString(r["PriceListName"]);
            d.CurSymbol           = Util.GetValueOfString(r["CurSymbol"]);
            d.ISO_Code            = Util.GetValueOfString(r["ISO_Code"]);
            d.StdPrecision        = Util.GetValueOfInt(r["StdPrecision"]);
            d.ConvertedLineCount  = Util.GetValueOfInt(r["ConvertedLineCount"]);
            d.SystemDate          = Util.GetValueOfDateTime(r["SystemDate"]);
            return true;
        }

        /// <summary>
        /// Loads the header's custom columns (source warehouse + budget-breach
        /// flags).
        ///
        /// These used to share one SELECT, which meant a schema missing ANY of the
        /// three lost all three: the statement failed, the catch swallowed it, and
        /// the source warehouse silently came back empty — so the panel fell
        /// through to its "External Procurement" wording and mislabelled the
        /// procurement type. Each column is now checked against the dictionary and
        /// read independently, so one absent column only drops its own value.
        /// </summary>
        private void LoadHeaderExtras(int M_Requisition_ID, RequisitionOverviewData d)
        {
            LoadSourceWarehouse(M_Requisition_ID, d);
            LoadBudgetBreach(M_Requisition_ID, d);
        }

        /// <summary>
        /// Reads the requisition's source warehouse from
        /// M_Requisition.DTD001_MWarehouseSource_ID. Skipped when that custom
        /// column is not present in this schema.
        /// </summary>
        private void LoadSourceWarehouse(int M_Requisition_ID, RequisitionOverviewData d)
        {
            if (!ColumnExists("M_Requisition", "DTD001_MWarehouseSource_ID")) return;

            try
            {
                string sql = @"SELECT r.DTD001_MWarehouseSource_ID AS SourceWarehouseId,
                                      srcwh.Name AS SourceWarehouseName
                                 FROM M_Requisition r
                                 LEFT OUTER JOIN M_Warehouse srcwh
                                        ON (srcwh.M_Warehouse_ID = r.DTD001_MWarehouseSource_ID)
                                WHERE r.M_Requisition_ID = @M_Requisition_ID";
                DataSet ds = DB.ExecuteDataset(sql, ReqParam(M_Requisition_ID), null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return;

                DataRow row = ds.Tables[0].Rows[0];
                d.SourceWarehouseId   = Util.GetValueOfInt(row["SourceWarehouseId"]);
                d.SourceWarehouseName = Util.GetValueOfString(row["SourceWarehouseName"]);
            }
            catch (Exception ex)
            {
                _log.Severe("LoadSourceWarehouse (M_Requisition_ID=" + M_Requisition_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// Reads the budget-breach flag and note, each only when its column exists.
        /// </summary>
        private void LoadBudgetBreach(int M_Requisition_ID, RequisitionOverviewData d)
        {
            bool hasFlag = ColumnExists("M_Requisition", "IsBudgetBreach");
            bool hasNote = ColumnExists("M_Requisition", "VAS_BudgetBreach");
            if (!hasFlag && !hasNote) return;

            try
            {
                List<string> cols = new List<string>();
                cols.Add(hasFlag ? "COALESCE(r.IsBudgetBreach, 'N') AS IsBudgetBreach"
                                 : "'N' AS IsBudgetBreach");
                cols.Add(hasNote ? "r.VAS_BudgetBreach AS BudgetBreachNote"
                                 : "CAST(NULL AS VARCHAR(255)) AS BudgetBreachNote");

                string sql = "SELECT " + string.Join(", ", cols.ToArray()) +
                             @" FROM M_Requisition r
                                WHERE r.M_Requisition_ID = @M_Requisition_ID";
                DataSet ds = DB.ExecuteDataset(sql, ReqParam(M_Requisition_ID), null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return;

                DataRow r = ds.Tables[0].Rows[0];
                d.IsBudgetBreach   = Util.GetValueOfString(r["IsBudgetBreach"]) == "Y";
                d.BudgetBreachNote = Util.GetValueOfString(r["BudgetBreachNote"]);
            }
            catch (Exception ex)
            {
                _log.Severe("LoadBudgetBreach (M_Requisition_ID=" + M_Requisition_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// Returns true when the given column exists on the given table, using the
        /// AD_Column dictionary. A DB issue degrades to "absent" (false), which just
        /// drops the optional value that depends on it.
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
        //  Lines                                                             //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// Loads requisition lines using standard columns only (product +
        /// category + UOM + requested qty + price + line amount). Child of an
        /// already-authorized requisition, so no separate MRole filter.
        /// </summary>
        private List<RequisitionLineData> LoadLines(Ctx ctx, int M_Requisition_ID)
        {
            List<RequisitionLineData> lines = new List<RequisitionLineData>();

            // The attribute set instance (lot / serial / size ...) is only read
            // when the line actually carries the column, and only joined for a real
            // instance id — 0 is "no attributes", not a row to look up.
            bool hasAsi = ColumnExists("M_RequisitionLine", "M_AttributeSetInstance_ID");
            string asiExpr = hasAsi
                ? "asi.Description"
                : "CAST(NULL AS VARCHAR(255))";

            // QtyEntered is the quantity in the line's SELECTED unit (C_UOM_ID);
            // Qty is the same quantity in the product's BASE unit —
            // MRequisitionLine.BeforeSave converts one into the other whenever
            // either changes. The panel labels every row with the selected unit,
            // so it must report the selected-unit figure. A schema without the
            // column falls back to Qty, where the two are necessarily the same.
            string qtyEnteredExpr = ColumnExists("M_RequisitionLine", "QtyEntered")
                ? "rl.QtyEntered" : "rl.Qty";
            string asiJoin = hasAsi
                ? @"LEFT OUTER JOIN M_AttributeSetInstance asi
                           ON (asi.M_AttributeSetInstance_ID = rl.M_AttributeSetInstance_ID
                               AND rl.M_AttributeSetInstance_ID > 0)"
                : "";

            string sql = @"SELECT
                              rl.M_RequisitionLine_ID,
                              rl.Line,
                              rl.Qty,
                              " + qtyEnteredExpr + @" AS QtyEntered,
                              rl.PriceActual,
                              rl.LineNetAmt,
                              rl.Description AS LineDescription,
                              rl.M_Product_ID,
                              rl.C_Charge_ID,
                              rl.C_OrderLine_ID,
                              rl.C_UOM_ID,
                              p.C_UOM_ID AS ProductUOM_ID,
                              p.Name    AS ProductName,
                              p.Value   AS ProductValue,
                              pcat.Name AS CategoryName,
                              ch.Name   AS ChargeName,
                              uom.Name  AS UOMName,
                              uom.StdPrecision AS UOMPrecision,
                              " + asiExpr + @" AS AttributeSetInstance
                           FROM M_RequisitionLine rl
                           LEFT OUTER JOIN M_Product p            ON (p.M_Product_ID = rl.M_Product_ID)
                           LEFT OUTER JOIN M_Product_Category pcat ON (pcat.M_Product_Category_ID = p.M_Product_Category_ID)
                           LEFT OUTER JOIN C_Charge ch            ON (ch.C_Charge_ID = rl.C_Charge_ID)
                           LEFT OUTER JOIN C_UOM uom              ON (uom.C_UOM_ID = rl.C_UOM_ID)
                           " + asiJoin + @"
                           WHERE rl.M_Requisition_ID = @M_Requisition_ID
                             AND rl.IsActive = 'Y'
                           ORDER BY rl.Line";

            DataSet ds = DB.ExecuteDataset(sql, ReqParam(M_Requisition_ID), null);
            if (ds == null || ds.Tables.Count == 0) return lines;

            foreach (DataRow r in ds.Tables[0].Rows)
            {
                RequisitionLineData ln = new RequisitionLineData();
                ln.M_RequisitionLine_ID = Util.GetValueOfInt(r["M_RequisitionLine_ID"]);
                ln.Line          = Util.GetValueOfInt(r["Line"]);

                // The row is reported in the line's SELECTED unit throughout —
                // quantity, unit price and the stock figures alongside them — since
                // that is the unit the row is labelled with (uom.Name, from
                // rl.C_UOM_ID). A line keyed as 2 BOX of a product held in EA read
                // as 24 against a "BOX" label, priced per EA.
                //
                // UomRatio is how many BASE units one SELECTED unit is (12, for a
                // 12-EA box). Everything the database stores in the base unit —
                // PriceActual, M_Storage.QtyOnHand, M_InOutLine.MovementQty — is
                // brought onto the selected scale with it.
                //
                // It comes from the product's own UOM CONVERSION, which is the
                // definition of the rate and is always there to be read. It used to
                // be derived from the line's two quantities (Qty / QtyEntered), and
                // QtyEntered is an optional column that only the requisition WINDOW
                // maintains — a line written by replenishment, by a project copy or
                // by a work order carries Qty alone. The derived ratio then
                // collapsed to 1 and the row showed the product's BASE price under
                // the selected unit's label, which is exactly the price the panel
                // exists to restate. The quantity ratio stays as the fallback for a
                // line whose product has no conversion to read (a charge line,
                // chiefly).
                ln.BaseQty       = Util.GetValueOfDecimal(r["Qty"]);
                ln.RequestedQty  = Util.GetValueOfDecimal(r["QtyEntered"]);
                ln.UomRatio      = GetUomRatio(ctx,
                    Util.GetValueOfInt(r["M_Product_ID"]),
                    Util.GetValueOfInt(r["C_UOM_ID"]),
                    Util.GetValueOfInt(r["ProductUOM_ID"]));
                if (ln.UomRatio <= 0 && ln.RequestedQty != 0 && ln.BaseQty != 0)
                    ln.UomRatio = ln.BaseQty / ln.RequestedQty;
                if (ln.UomRatio <= 0) ln.UomRatio = 1;
                // A line with no entered quantity of its own is stated in the
                // selected unit from the base one, so the figure reads against the
                // unit the row is labelled with.
                if (ln.RequestedQty == 0) ln.RequestedQty = ln.BaseQty / ln.UomRatio;

                // Price per SELECTED unit. PriceActual is per base unit, and
                // MRequisitionLine keeps LineNetAmt = Qty x PriceActual, so scaling
                // by the ratio leaves the line's own total untouched:
                // QtyEntered x (PriceActual x ratio) = Qty x PriceActual.
                ln.UnitPrice     = Util.GetValueOfDecimal(r["PriceActual"]) * ln.UomRatio;
                ln.LineAmount    = Util.GetValueOfDecimal(r["LineNetAmt"]);
                ln.Description   = Util.GetValueOfString(r["LineDescription"]);
                ln.M_Product_ID  = Util.GetValueOfInt(r["M_Product_ID"]);
                ln.C_Charge_ID   = Util.GetValueOfInt(r["C_Charge_ID"]);
                ln.IsConverted   = Util.GetValueOfInt(r["C_OrderLine_ID"]) > 0;
                ln.ProductName   = Util.GetValueOfString(r["ProductName"]);
                ln.ProductValue  = Util.GetValueOfString(r["ProductValue"]);
                ln.CategoryName  = Util.GetValueOfString(r["CategoryName"]);
                ln.ChargeName    = Util.GetValueOfString(r["ChargeName"]);
                ln.UOMName       = Util.GetValueOfString(r["UOMName"]);
                ln.UOMPrecision  = Util.GetValueOfInt(r["UOMPrecision"]);
                ln.AttributeSetInstance = Util.GetValueOfString(r["AttributeSetInstance"]);

                if (string.IsNullOrEmpty(ln.ProductName) && !string.IsNullOrEmpty(ln.ChargeName))
                    ln.ProductName = ln.ChargeName;

                // Line net amount falls back to qty * price when not stored. Both
                // sides are on the selected scale, so the product is the same
                // amount the base-unit pair would have given.
                if (ln.LineAmount == 0 && ln.UnitPrice != 0)
                    ln.LineAmount = ln.RequestedQty * ln.UnitPrice;

                lines.Add(ln);
            }
            return lines;
        }

        /// <summary>
        /// How many BASE units one unit of the line's SELECTED unit is — 12 for a
        /// line keyed in 12-EA boxes of a product held in EA.
        ///
        /// This is the rate the platform itself converts a requisition line with:
        /// MRequisitionLine.BeforeSave writes Qty (base) as
        /// MUOMConversion.ConvertProductFrom(QtyEntered), which multiplies by the
        /// product conversion's DIVIDE rate — the same figure
        /// GetProductRateFrom returns here. Reading the definition rather than
        /// inferring it from the line's two quantities means the rate is right even
        /// where QtyEntered was never written, which is every line raised by a
        /// process rather than typed into the window.
        ///
        /// Returns 0 when there is nothing to read — no product, no selected unit,
        /// or a product with no conversion to that unit — which leaves the caller
        /// on its quantity-derived fallback. A line already keyed in the product's
        /// own unit is 1 by definition and is not looked up at all.
        /// </summary>
        /// <param name="ctx">User context (client, for the conversion cache).</param>
        /// <param name="M_Product_ID">The line's product; 0 for a charge line.</param>
        /// <param name="C_UOM_ID">The line's selected unit.</param>
        /// <param name="productUomId">The product's own (base) unit.</param>
        private decimal GetUomRatio(Ctx ctx, int M_Product_ID, int C_UOM_ID, int productUomId)
        {
            if (M_Product_ID <= 0 || C_UOM_ID <= 0) return 0;
            if (productUomId > 0 && productUomId == C_UOM_ID) return 1;

            try
            {
                decimal? rate = MUOMConversion.GetProductRateFrom(ctx, M_Product_ID, C_UOM_ID);
                if (rate.HasValue && rate.Value > 0) return rate.Value;
            }
            catch (Exception ex)
            {
                _log.Severe("GetUomRatio (M_Product_ID=" + M_Product_ID +
                            ", C_UOM_ID=" + C_UOM_ID + "): " + ex.Message);
            }
            return 0;
        }

        /// <summary>
        /// Enriches the loaded lines with the custom reserved / ordered /
        /// available-budget / breach columns (source-stock + budget), keyed by
        /// line id. Guarded so a missing column simply leaves those unset (the
        /// UI then shows N/A source stock and no per-line breach).
        /// </summary>
        private void LoadLineExtras(int M_Requisition_ID, List<RequisitionLineData> lines)
        {
            if (lines == null || lines.Count == 0) return;
            try
            {
                // Every one of these is optional. They used to sit in a single
                // SELECT, so a schema without (say) M_RequisitionLine.QtyOrdered —
                // which is not a standard column — failed the whole statement and
                // silently zeroed the reserved / ordered / budget figures for every
                // line. Each is now included only when it actually exists.
                string reservedExpr = FirstExistingExpr("M_RequisitionLine",
                    new string[] { "DTD001_ReservedQty", "QtyReserved" }, "rl");
                string orderedExpr = FirstExistingExpr("M_RequisitionLine",
                    new string[] { "QtyOrdered", "DTD001_OrderedQty" }, "rl");
                string budgetExpr = FirstExistingExpr("M_RequisitionLine",
                    new string[] { "VAS_AvailableBudget" }, "rl");
                string breachExpr = ColumnExists("M_RequisitionLine", "IsBudgetBreach")
                    ? "COALESCE(rl.IsBudgetBreach, 'N')" : "'N'";

                string sql = @"SELECT rl.M_RequisitionLine_ID,
                                      " + reservedExpr + @" AS ReservedQty,
                                      " + orderedExpr  + @" AS OrderedQty,
                                      " + budgetExpr   + @" AS AvailableBudget,
                                      " + breachExpr   + @" AS IsBudgetBreach
                                 FROM M_RequisitionLine rl
                                WHERE rl.M_Requisition_ID = @M_Requisition_ID
                                  AND rl.IsActive = 'Y'";
                DataSet ds = DB.ExecuteDataset(sql, ReqParam(M_Requisition_ID), null);
                if (ds == null || ds.Tables.Count == 0) return;

                Dictionary<int, DataRow> byId = new Dictionary<int, DataRow>();
                foreach (DataRow r in ds.Tables[0].Rows)
                    byId[Util.GetValueOfInt(r["M_RequisitionLine_ID"])] = r;

                foreach (RequisitionLineData ln in lines)
                {
                    DataRow r;
                    if (!byId.TryGetValue(ln.M_RequisitionLine_ID, out r)) continue;
                    ln.ReservedQty     = Util.GetValueOfDecimal(r["ReservedQty"]);
                    ln.OrderedQty      = Util.GetValueOfDecimal(r["OrderedQty"]);
                    ln.AvailableBudget = Util.GetValueOfDecimal(r["AvailableBudget"]);
                    ln.IsBudgetBreach  = Util.GetValueOfString(r["IsBudgetBreach"]) == "Y";
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadLineExtras (M_Requisition_ID=" + M_Requisition_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// Returns "COALESCE(alias.Col, 0)" for the first candidate column that
        /// exists on the table, or the literal "0" when none of them do.
        /// </summary>
        private string FirstExistingExpr(string tableName, string[] candidates, string alias)
        {
            for (int i = 0; i < candidates.Length; i++)
            {
                if (ColumnExists(tableName, candidates[i]))
                    return "COALESCE(" + alias + "." + candidates[i] + ", 0)";
            }
            return "0";
        }

        /// <summary>
        /// Fills each line's on-hand quantity at the warehouse the goods are
        /// expected to be held in, and how much has actually been RECEIVED against
        /// the line so far.
        ///
        /// The warehouse is the requisition's SOURCE warehouse
        /// (M_Requisition.DTD001_MWarehouseSource_ID) when it has one — an internal
        /// fulfilment is served out of that stock — and otherwise the requisition's
        /// own request warehouse (M_Requisition.M_Warehouse_ID), which is where a
        /// goods receipt raised from its purchase order delivers.
        ///
        /// That second case is what makes the column live: an externally procured
        /// requisition has no source warehouse, so the figure used to be skipped
        /// altogether and the column read N/A for the life of the document — it
        /// never reflected the receipt that eventually satisfied it. Reading the
        /// request warehouse instead means a completed GRN raises the on-hand and
        /// the panel shows it on the next refresh.
        ///
        /// Stock is summed from M_Storage.QtyOnHand over every locator belonging to
        /// that warehouse, matched on the line's product and — when the line names
        /// one — its attribute set instance. A line with no attribute set instance
        /// sums across all instances of the product. A product with no stock row
        /// yields 0 rather than "unknown".
        ///
        /// Both figures are converted onto the line's SELECTED unit (UomRatio), so
        /// they read against the requested quantity beside them rather than against
        /// the product's base unit.
        /// </summary>
        /// <param name="M_Requisition_ID">Owning requisition id.</param>
        /// <param name="sourceWarehouseId">Source warehouse; 0 when not set.</param>
        /// <param name="requestWarehouseId">The requisition's own warehouse, used
        /// when there is no source warehouse.</param>
        /// <param name="lines">Lines to enrich, keyed by line id.</param>
        private void LoadSourceStock(int M_Requisition_ID, int sourceWarehouseId,
                                     int requestWarehouseId, List<RequisitionLineData> lines)
        {
            if (lines == null || lines.Count == 0) return;

            int warehouseId = sourceWarehouseId > 0 ? sourceWarehouseId : requestWarehouseId;
            // Received quantities stand on their own: a line can have been received
            // even where no warehouse can be resolved for the on-hand figure.
            LoadReceivedQty(M_Requisition_ID, lines);
            if (warehouseId <= 0) return;

            try
            {
                // Match the instance only when the line actually carries one.
                string asiCond = ColumnExists("M_RequisitionLine", "M_AttributeSetInstance_ID")
                    ? @"(rl.M_AttributeSetInstance_ID IS NULL
                         OR rl.M_AttributeSetInstance_ID = 0
                         OR s.M_AttributeSetInstance_ID = rl.M_AttributeSetInstance_ID)"
                    : "1 = 1";

                string sql = @"SELECT rl.M_RequisitionLine_ID,
                                      NVL(SUM(s.QtyOnHand), 0) AS SourceQtyOnHand
                                 FROM M_RequisitionLine rl
                                 LEFT OUTER JOIN M_Storage s
                                        ON (s.M_Product_ID = rl.M_Product_ID
                                            AND s.M_Locator_ID IN (SELECT loc.M_Locator_ID
                                                                     FROM M_Locator loc
                                                                    WHERE loc.M_Warehouse_ID = @M_Warehouse_ID
                                                                      AND loc.IsActive = 'Y')
                                            AND " + asiCond + @")
                                WHERE rl.M_Requisition_ID = @M_Requisition_ID
                                  AND rl.IsActive = 'Y'
                                GROUP BY rl.M_RequisitionLine_ID";

                SqlParameter[] param = new SqlParameter[]
                {
                    new SqlParameter("@M_Requisition_ID", M_Requisition_ID),
                    new SqlParameter("@M_Warehouse_ID", warehouseId)
                };

                DataSet ds = DB.ExecuteDataset(sql, param, null);
                if (ds == null || ds.Tables.Count == 0) return;

                Dictionary<int, decimal> byId = new Dictionary<int, decimal>();
                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    byId[Util.GetValueOfInt(r["M_RequisitionLine_ID"])] =
                        Util.GetValueOfDecimal(r["SourceQtyOnHand"]);
                }

                foreach (RequisitionLineData ln in lines)
                {
                    // A CHARGE line has no stock position: it is not a product, so
                    // it is not held anywhere. Left without source data entirely,
                    // so the panel shows a blank cell rather than a 0-of-n bar, and
                    // so it cannot count against the requisition's source
                    // availability — it used to be tallied as a short line.
                    if (ln.C_Charge_ID > 0 && ln.M_Product_ID <= 0)
                    {
                        ln.HasSourceData   = false;
                        ln.SourceQtyOnHand = 0;
                        ln.SourceState     = "";
                        continue;
                    }

                    decimal onHand;
                    if (!byId.TryGetValue(ln.M_RequisitionLine_ID, out onHand)) onHand = 0;
                    // M_Storage holds the product's BASE unit; the row reports the
                    // selected one.
                    ln.SourceQtyOnHand = ln.UomRatio != 0 ? onHand / ln.UomRatio : onHand;
                    // A warehouse was resolved, so the figure is meaningful even
                    // when it is zero.
                    ln.HasSourceData = true;
                    ln.SourceState = (ln.RequestedQty > 0 && ln.SourceQtyOnHand >= ln.RequestedQty)
                        ? "full" : "short";
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadSourceStock (M_Requisition_ID=" + M_Requisition_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// Fills each line's RECEIVED quantity: how much of what the line asked for
        /// has actually been delivered against it, across every kind of document
        /// that can deliver it.
        ///
        ///   Goods receipt   — the order lines raised for the requisition line, and
        ///                     from them the receipt lines booked against those order
        ///                     lines (M_InOutLine.MovementQty).
        ///
        ///                     An order line is matched BOTH WAYS: forward through
        ///                     C_OrderLine.M_RequisitionLine_ID and back through
        ///                     M_RequisitionLine.C_OrderLine_ID. The forward link is
        ///                     the one the platform itself reads (MOrder's completion
        ///                     hook and MRequisition.UpdateRequisitionStatus both walk
        ///                     C_OrderLine.M_RequisitionLine_ID), and it is the only
        ///                     one that is ONE-TO-MANY: the requisition line holds a
        ///                     single C_OrderLine_ID, so a line ordered across several
        ///                     purchase orders — split between vendors, or topped up
        ///                     after a short delivery — reported only the receipts of
        ///                     whichever order line happened to be stamped on it and
        ///                     read as permanently short. The reverse link is kept as
        ///                     well so a line carrying only that one is still counted.
        ///
        ///   RFQ -> PO       — an RFQ delivers nothing itself, but the purchase order
        ///                     raised from a winning response carries NO requisition
        ///                     reference at all: RfQCreatePO builds its order lines
        ///                     from the response quantities and never stamps
        ///                     M_RequisitionLine_ID (nor writes C_OrderLine_ID back).
        ///                     The link that does survive is the one the RFQ was built
        ///                     with — C_RfQLine.M_RequisitionLine_ID — so the chain is
        ///                     requisition line -> RFQ line -> response line ->
        ///                     C_RfQResponse.C_Order_ID -> that order's lines. Without
        ///                     this branch every requisition tendered through an RFQ
        ///                     read 0 received however much had arrived.
        ///   Material transfer — M_MovementLine.M_RequisitionLine_ID
        ///                     (MovementQty): stock moved in from another warehouse
        ///                     satisfies the request exactly as a purchase does.
        ///   Inventory Use   — M_InventoryLine.M_RequisitionLine_ID
        ///                     (QtyInternalUse): issued straight out of stock.
        ///
        /// A purchase order contributes nothing on its own: ordering is not
        /// receiving. Only what its RECEIPTS booked counts, which is why both order
        /// branches end at M_InOutLine rather than at the order line.
        ///
        /// Only documents that reached Completed or Closed count. A drafted one has
        /// moved no stock, and reporting its quantity as received would claim goods
        /// nobody has.
        ///
        /// All three quantities are stored in the product's BASE unit, so the total
        /// is brought onto the line's selected unit like every other stock figure
        /// here. The two module-owned sources are dictionary-guarded and separately
        /// try/caught, so an absent module drops only its own contribution.
        /// </summary>
        /// <param name="M_Requisition_ID">Owning requisition id.</param>
        /// <param name="lines">Lines to enrich, keyed by line id.</param>
        private void LoadReceivedQty(int M_Requisition_ID, List<RequisitionLineData> lines)
        {
            Dictionary<int, decimal> byId = new Dictionary<int, decimal>();

            // ----- Goods receipts against the order lines raised for the line -----
            //  The order line is matched both ways (see the summary above), which
            //  means one receipt line can be reached TWICE — once forward and once
            //  back. The inner SELECT DISTINCT keys on M_InOutLine_ID, so a receipt
            //  reachable by both routes lands in the sum exactly once; summing the
            //  join directly would double it.
            AddReceivedQty(M_Requisition_ID, byId, "M_InOut", "",  "",
                @"SELECT t.M_RequisitionLine_ID,
                         COALESCE(SUM(t.MovementQty), 0) AS ReceivedQty
                    FROM (SELECT DISTINCT rl.M_RequisitionLine_ID,
                                          iol.M_InOutLine_ID,
                                          iol.MovementQty
                            FROM M_RequisitionLine rl
                           INNER JOIN C_OrderLine ol
                                   ON (ol.M_RequisitionLine_ID = rl.M_RequisitionLine_ID
                                    OR ol.C_OrderLine_ID       = rl.C_OrderLine_ID)
                           INNER JOIN M_InOutLine iol
                                   ON (iol.C_OrderLine_ID = ol.C_OrderLine_ID)
                           INNER JOIN M_InOut io
                                   ON (io.M_InOut_ID = iol.M_InOut_ID)
                           WHERE rl.M_Requisition_ID = @M_Requisition_ID
                             AND rl.IsActive  = 'Y'
                             AND ol.IsActive  = 'Y'
                             AND iol.IsActive = 'Y'
                             AND io.IsActive  = 'Y'
                             AND io.IsSOTrx   = 'N'
                             AND io.DocStatus IN ('CO', 'CL')) t
                   GROUP BY t.M_RequisitionLine_ID");

            // ----- Goods receipts against a PO raised from an RFQ -----
            //  Guarded on C_RfQLine.M_RequisitionLine_ID: without that column the
            //  chain has no starting point and only this contribution is skipped.
            //
            //  The order line carries no requisition reference on this path, so it
            //  is reached through the RESPONSE's order and matched on product. Two
            //  conditions keep that match honest:
            //    * an order line that DOES carry a requisition reference (or that
            //      the requisition line itself names) is excluded — the branch above
            //      already counted its receipts;
            //    * a requisition line whose product sits on more than one line of
            //      the same RFQ is skipped entirely (the NOT EXISTS). Product is all
            //      there is to match on here, so two lines of the same product would
            //      each claim BOTH order lines' receipts and report roughly double
            //      what arrived. Counting nothing understates a ratio that is still
            //      bounded by the requested quantity; counting twice overstates it
            //      past what was asked for, which reads as a completed line.
            AddReceivedQty(M_Requisition_ID, byId, "C_RfQ",
                "C_RfQLine", "M_RequisitionLine_ID",
                @"SELECT t.M_RequisitionLine_ID,
                         COALESCE(SUM(t.MovementQty), 0) AS ReceivedQty
                    FROM (SELECT DISTINCT rl.M_RequisitionLine_ID,
                                          iol.M_InOutLine_ID,
                                          iol.MovementQty
                            FROM M_RequisitionLine rl
                           INNER JOIN C_RfQLine ql
                                   ON (ql.M_RequisitionLine_ID = rl.M_RequisitionLine_ID)
                           INNER JOIN C_RfQResponseLine rspl
                                   ON (rspl.C_RfQLine_ID = ql.C_RfQLine_ID)
                           INNER JOIN C_RfQResponse rsp
                                   ON (rsp.C_RfQResponse_ID = rspl.C_RfQResponse_ID)
                           INNER JOIN C_OrderLine ol
                                   ON (ol.C_Order_ID   = rsp.C_Order_ID
                                   AND ol.M_Product_ID = rl.M_Product_ID)
                           INNER JOIN M_InOutLine iol
                                   ON (iol.C_OrderLine_ID = ol.C_OrderLine_ID)
                           INNER JOIN M_InOut io
                                   ON (io.M_InOut_ID = iol.M_InOut_ID)
                           WHERE rl.M_Requisition_ID = @M_Requisition_ID
                             AND rl.IsActive   = 'Y'
                             AND rl.M_Product_ID > 0
                             AND ql.IsActive   = 'Y'
                             AND rspl.IsActive = 'Y'
                             AND rsp.IsActive  = 'Y'
                             AND ol.IsActive   = 'Y'
                             AND iol.IsActive  = 'Y'
                             AND io.IsActive   = 'Y'
                             AND io.IsSOTrx    = 'N'
                             AND io.DocStatus IN ('CO', 'CL')
                             AND COALESCE(ol.M_RequisitionLine_ID, 0) = 0
                             AND COALESCE(rl.C_OrderLine_ID, 0) <> ol.C_OrderLine_ID
                             AND NOT EXISTS (SELECT 1
                                               FROM C_RfQLine ql2
                                              INNER JOIN M_RequisitionLine rl2
                                                      ON (rl2.M_RequisitionLine_ID = ql2.M_RequisitionLine_ID)
                                              WHERE ql2.C_RfQ_ID     = ql.C_RfQ_ID
                                                AND ql2.C_RfQLine_ID <> ql.C_RfQLine_ID
                                                AND ql2.IsActive      = 'Y'
                                                AND rl2.M_Product_ID  = rl.M_Product_ID)) t
                   GROUP BY t.M_RequisitionLine_ID");

            // ----- Material transfers raised against the line -----
            AddReceivedQty(M_Requisition_ID, byId, "M_Movement",
                "M_MovementLine", "M_RequisitionLine_ID",
                @"SELECT ml.M_RequisitionLine_ID,
                         COALESCE(SUM(ml.MovementQty), 0) AS ReceivedQty
                    FROM M_MovementLine ml
                   INNER JOIN M_Movement m
                           ON (m.M_Movement_ID = ml.M_Movement_ID)
                   INNER JOIN M_RequisitionLine rl
                           ON (rl.M_RequisitionLine_ID = ml.M_RequisitionLine_ID)
                   WHERE rl.M_Requisition_ID = @M_Requisition_ID
                     AND rl.IsActive = 'Y'
                     AND ml.IsActive = 'Y'
                     AND m.IsActive  = 'Y'
                     AND m.DocStatus IN ('CO', 'CL')
                   GROUP BY ml.M_RequisitionLine_ID");

            // ----- Inventory Use issued against the line -----
            AddReceivedQty(M_Requisition_ID, byId, "M_Inventory",
                "M_InventoryLine", "M_RequisitionLine_ID",
                @"SELECT il.M_RequisitionLine_ID,
                         COALESCE(SUM(il.QtyInternalUse), 0) AS ReceivedQty
                    FROM M_InventoryLine il
                   INNER JOIN M_Inventory inv
                           ON (inv.M_Inventory_ID = il.M_Inventory_ID)
                   INNER JOIN M_RequisitionLine rl
                           ON (rl.M_RequisitionLine_ID = il.M_RequisitionLine_ID)
                   WHERE rl.M_Requisition_ID = @M_Requisition_ID
                     AND rl.IsActive  = 'Y'
                     AND il.IsActive  = 'Y'
                     AND inv.IsActive = 'Y'
                     AND COALESCE(inv.IsInternalUse, 'N') = 'Y'
                     AND inv.DocStatus IN ('CO', 'CL')
                   GROUP BY il.M_RequisitionLine_ID");

            if (byId.Count == 0) return;

            foreach (RequisitionLineData ln in lines)
            {
                decimal received;
                if (!byId.TryGetValue(ln.M_RequisitionLine_ID, out received)) continue;
                ln.ReceivedQty = ln.UomRatio != 0 ? received / ln.UomRatio : received;
            }
        }

        /// <summary>
        /// Runs one received-quantity query and ADDS its per-line totals into the
        /// running tally — see <see cref="LoadReceivedQty"/>. Each source is a
        /// separate delivery of the same request, so they sum rather than compete.
        /// </summary>
        /// <param name="M_Requisition_ID">Owning requisition id.</param>
        /// <param name="byId">Running total, keyed by requisition line id.</param>
        /// <param name="what">Document being read, for the log line only.</param>
        /// <param name="guardTable">Table carrying the link column, or "" to skip
        /// the guard.</param>
        /// <param name="guardColumn">The link column that must exist.</param>
        /// <param name="sql">The query. Must select M_RequisitionLine_ID and
        /// ReceivedQty, and bind @M_Requisition_ID exactly once.</param>
        private void AddReceivedQty(int M_Requisition_ID, Dictionary<int, decimal> byId,
                                    string what, string guardTable, string guardColumn,
                                    string sql)
        {
            if (!string.IsNullOrEmpty(guardTable) && !ColumnExists(guardTable, guardColumn))
                return;

            try
            {
                DataSet ds = DB.ExecuteDataset(sql, ReqParam(M_Requisition_ID), null);
                if (ds == null || ds.Tables.Count == 0) return;

                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    int lineId = Util.GetValueOfInt(r["M_RequisitionLine_ID"]);
                    if (lineId <= 0) continue;
                    decimal running;
                    byId.TryGetValue(lineId, out running);
                    byId[lineId] = running + Util.GetValueOfDecimal(r["ReceivedQty"]);
                }
            }
            catch (Exception ex)
            {
                _log.Severe("AddReceivedQty/" + what +
                            " (M_Requisition_ID=" + M_Requisition_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// Rolls the loaded lines up into the header-level stat figures
        /// (line/unit counts, estimated subtotal / contingency, source
        /// availability, reserved-ordered progress, any-line breach).
        /// </summary>
        private void RollUpTotals(RequisitionOverviewData d)
        {
            List<RequisitionLineData> lines = d.Lines ?? new List<RequisitionLineData>();

            decimal subtotal = 0, requestedUnits = 0, reserved = 0, ordered = 0, availableBudget = 0;
            decimal sourceOnHand = 0;
            List<decimal> budgetAmounts = new List<decimal>();
            int fullyInStock = 0;
            bool anyLineHasSource = false;
            bool anyLineBreach = false;

            foreach (RequisitionLineData ln in lines)
            {
                subtotal       += ln.LineAmount;
                requestedUnits += ln.RequestedQty;
                reserved       += ln.ReservedQty;
                ordered        += ln.OrderedQty;
                // VAS_AvailableBudget is the budget available to THAT line's budget
                // control, so every line drawing on the same control carries the
                // same figure. Adding them up would multiply one budget by the line
                // count, so only distinct amounts contribute.
                if (ln.AvailableBudget != 0 && !budgetAmounts.Contains(ln.AvailableBudget))
                {
                    budgetAmounts.Add(ln.AvailableBudget);
                    availableBudget += ln.AvailableBudget;
                }
                sourceOnHand   += ln.SourceQtyOnHand;
                if (ln.HasSourceData)
                {
                    anyLineHasSource = true;
                    if (ln.RequestedQty > 0 && ln.SourceQtyOnHand >= ln.RequestedQty) fullyInStock++;
                }
                if (ln.IsBudgetBreach) anyLineBreach = true;
            }

            d.LineCount        = lines.Count;
            d.RequestedUnits   = requestedUnits;
            d.EstimatedSubtotal = subtotal;

            // The estimated total is the sum of the lines actually shown, not the
            // header's TotalLines. MRequisitionLine.UpdateHeader maintains
            // TotalLines as SUM(LineNetAmt) over EVERY line with no IsActive
            // filter, so a line that was deactivated — which is what happens when a
            // line is re-entered under a different unit of measure — stays in the
            // header figure forever and the total reads high. Deriving it here
            // keeps the KPI card and the footer equal to the rows on screen.
            // TotalLines survives only as the fallback for a requisition with no
            // active lines at all.
            if (lines.Count > 0) d.EstimatedValue = subtotal;
            else if (d.EstimatedValue == 0) d.EstimatedValue = subtotal;

            d.HasSourceData    = anyLineHasSource;
            d.FullyInStockLines = fullyInStock;
            d.SourceStockOnHand = sourceOnHand;
            d.ReservedUnits    = reserved;
            d.OrderedUnits     = ordered;
            d.AvailableBudget  = availableBudget;

            // Budget breach = header flag OR any line flag.
            d.IsBudgetBreach   = d.IsBudgetBreach || anyLineBreach;
            if (d.IsBudgetBreach && availableBudget > 0 && d.EstimatedValue > availableBudget)
                d.BudgetOverage = d.EstimatedValue - availableBudget;

            d.IsConverted      = d.ConvertedLineCount > 0;
            // HasOrdered is set from the linked purchase orders in LoadMilestones —
            // a per-line QtyOrdered column is not standard and cannot be relied on.
        }

        // ----------------------------------------------------------------- //
        //  Progress milestones                                               //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// Fills the requisition-progress dates.
        ///
        ///   CompletedDate  — when the requisition itself was completed: the Created
        ///                    stamp of its workflow DocComplete activity, falling
        ///                    back to Updated for a CO/CL document completed outside
        ///                    the workflow engine.
        ///   ConvertedDate  — when the first purchase order referencing this
        ///                    requisition was created.
        ///   FulfilmentDate — when the first of those orders reached Completed.
        ///   ClosedDate     — the requisition's own close, or the last of its orders
        ///                    reaching Closed.
        ///
        /// Purchase orders are reached through M_RequisitionLine.C_OrderLine_ID,
        /// which is what the conversion process writes back.
        /// </summary>
        private void LoadMilestones(int M_Requisition_ID, RequisitionOverviewData d)
        {
            d.CompletedDate = GetRequisitionCompletedDate(M_Requisition_ID);
            if (!d.CompletedDate.HasValue &&
                (d.StatusCode == "CO" || d.StatusCode == "CL"))
            {
                d.CompletedDate = d.Updated;
            }

            // When posting actually ran, for the stepper's Posted stage.
            d.PostedDate = GetPostedDate(M_Requisition_ID);

            LoadLinkedOrderMilestones(M_Requisition_ID, d);
            // ... and every other document the requisition can be turned into.
            LoadConversionMilestones(M_Requisition_ID, d);

            // The requisition's own close always wins as the closing moment.
            if (d.StatusCode == "CL")
            {
                d.IsClosed   = true;
                d.ClosedDate = d.Updated;
            }
        }

        /// <summary>
        /// Returns the moment the requisition was actually posted — the earliest
        /// Created stamp across the Fact_Acct rows written for it — or null when it
        /// has never been posted.
        ///
        /// M_Requisition.Posted answers WHETHER it posted; this answers WHEN, which
        /// is what the progress stepper's Posted stage is captioned with. Its own
        /// query for the same MRole reason documented on GetRequisitionCompletedDate.
        /// </summary>
        /// <param name="M_Requisition_ID">Selected requisition id.</param>
        private DateTime? GetPostedDate(int M_Requisition_ID)
        {
            try
            {
                string sql = @"SELECT MIN(fa.Created) AS PostedDate
                                 FROM Fact_Acct fa
                                INNER JOIN AD_Table adt ON (adt.AD_Table_ID = fa.AD_Table_ID)
                                WHERE fa.Record_ID  = @M_Requisition_ID
                                  AND adt.TableName = 'M_Requisition'";
                DataSet ds = DB.ExecuteDataset(sql, ReqParam(M_Requisition_ID), null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
                    return null;
                return Util.GetValueOfDateTime(ds.Tables[0].Rows[0]["PostedDate"]);
            }
            catch (Exception ex)
            {
                _log.Severe("GetPostedDate (M_Requisition_ID=" + M_Requisition_ID + "): " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// The moment the requisition's workflow DocComplete activity ran, or null
        /// when it has none. Standalone query — never a subselect of the MRole-
        /// rewritten header SELECT.
        /// </summary>
        private DateTime? GetRequisitionCompletedDate(int M_Requisition_ID)
        {
            try
            {
                string sql = @"SELECT MAX(wfa.Created) AS CompletedDate
                                 FROM AD_WF_Process wfp
                                INNER JOIN AD_WF_Activity wfa
                                        ON (wfa.AD_WF_Process_ID = wfp.AD_WF_Process_ID)
                                INNER JOIN AD_WF_Node wfn
                                        ON (wfn.AD_WF_Node_ID = wfa.AD_WF_Node_ID)
                                INNER JOIN AD_Table adt
                                        ON (adt.AD_Table_ID = wfp.AD_Table_ID)
                                WHERE wfp.Record_ID = @M_Requisition_ID
                                  AND adt.TableName = 'M_Requisition'
                                  AND wfp.IsActive  = 'Y'
                                  AND wfa.IsActive  = 'Y'
                                  AND wfn.IsActive  = 'Y'
                                  AND wfa.WFState   = 'CC'
                                  AND UPPER(TRIM(wfn.Value)) IN ('DOCCOMPLETE', 'COMPLETE', '(DOCCOMPLETE)')";
                DataSet ds = DB.ExecuteDataset(sql, ReqParam(M_Requisition_ID), null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
                    return null;
                return Util.GetValueOfDateTime(ds.Tables[0].Rows[0]["CompletedDate"]);
            }
            catch (Exception ex)
            {
                _log.Severe("GetRequisitionCompletedDate (M_Requisition_ID=" + M_Requisition_ID + "): " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Reads every purchase order raised from this requisition and derives the
        /// converted / in-fulfilment / closed milestones from them.
        /// </summary>
        private void LoadLinkedOrderMilestones(int M_Requisition_ID, RequisitionOverviewData d)
        {
            try
            {
                string sql = @"SELECT o.C_Order_ID,
                                      o.DocumentNo,
                                      o.DocStatus,
                                      o.Created,
                                      o.Updated
                                 FROM C_Order o
                                WHERE o.IsActive = 'Y'
                                  AND EXISTS (SELECT 1
                                                FROM C_OrderLine ol
                                               INNER JOIN M_RequisitionLine rl
                                                       ON (rl.C_OrderLine_ID = ol.C_OrderLine_ID)
                                               WHERE ol.C_Order_ID       = o.C_Order_ID
                                                 AND rl.M_Requisition_ID = @M_Requisition_ID
                                                 AND rl.IsActive         = 'Y')
                                ORDER BY o.Created";
                DataSet ds = DB.ExecuteDataset(sql, ReqParam(M_Requisition_ID), null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return;

                int orderCount = 0, closedCount = 0;
                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    orderCount++;
                    string status     = Util.GetValueOfString(r["DocStatus"]);
                    DateTime? created = Util.GetValueOfDateTime(r["Created"]);
                    DateTime? updated = Util.GetValueOfDateTime(r["Updated"]);

                    // Converted: the earliest linked order's creation.
                    if (!d.ConvertedDate.HasValue ||
                        (created.HasValue && created.Value < d.ConvertedDate.Value))
                        d.ConvertedDate = created;

                    if (d.OrderDocumentNo == null) d.OrderDocumentNo = Util.GetValueOfString(r["DocumentNo"]);

                    if (status == "CO" || status == "CL")
                    {
                        // In fulfilment: the earliest linked order to be completed.
                        d.HasOrdered = true;
                        if (!d.FulfilmentDate.HasValue ||
                            (updated.HasValue && updated.Value < d.FulfilmentDate.Value))
                            d.FulfilmentDate = updated;
                    }

                    if (status == "CL")
                    {
                        closedCount++;
                        // Closed: the last linked order to close.
                        if (!d.ClosedDate.HasValue ||
                            (updated.HasValue && updated.Value > d.ClosedDate.Value))
                            d.ClosedDate = updated;
                    }
                }

                d.OrderCount = orderCount;
                // Every order raised from the requisition is closed -> the
                // requisition's procurement cycle is finished.
                if (orderCount > 0 && closedCount == orderCount) d.IsClosed = true;
                else if (!d.IsClosed) d.ClosedDate = null;
            }
            catch (Exception ex)
            {
                _log.Severe("LoadLinkedOrderMilestones (M_Requisition_ID=" + M_Requisition_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// Carries the Converted / In Fulfilment milestones over EVERY document a
        /// requisition can be turned into, not only its purchase orders.
        ///
        /// A requisition is converted the moment something is raised from it. The
        /// stepper only ever knew about C_Order — reached through
        /// M_RequisitionLine.C_OrderLine_ID, which is what the PO conversion writes
        /// back — so a requisition served by a material transfer, answered by an
        /// RFQ or issued straight out of stock sat at "ready to convert" for the
        /// life of the document, with the Documents section listing the very
        /// document that contradicts it.
        ///
        /// Each source contributes two stamps:
        ///   Converted   — when the document was CREATED. The earliest across all
        ///                 of them wins: conversion happened once, at the first of
        ///                 them, and a later transfer does not un-convert it.
        ///   Fulfilment  — when a document that reached Completed or Closed was
        ///                 last changed. The earliest again: fulfilment began with
        ///                 the first document to complete.
        ///
        /// Purchase and blanket orders are not read here — LoadLinkedOrderMilestones
        /// already has them, and both are C_Order — but their dates are merged
        /// with these, so whichever kind came first is the one the stage reports.
        ///
        /// Every source is column-guarded and independently try/caught: a schema
        /// without the module behind one contributes nothing rather than costing
        /// the stage its other sources.
        /// </summary>
        /// <param name="M_Requisition_ID">Selected requisition id.</param>
        /// <param name="d">Overview payload being populated.</param>
        private void LoadConversionMilestones(int M_Requisition_ID, RequisitionOverviewData d)
        {
            // RFQs issued from the requisition.
            AddConversionMilestone(M_Requisition_ID, d, "C_RfQ", "C_RfQ", "M_Requisition_ID",
                @"SELECT q.Created, q.Updated, q.DocStatus
                    FROM C_RfQ q
                   WHERE q.M_Requisition_ID = @M_Requisition_ID
                     AND q.IsActive = 'Y'");

            // Material transfers fulfilling it.
            AddConversionMilestone(M_Requisition_ID, d, "M_Movement", "M_MovementLine", "M_RequisitionLine_ID",
                @"SELECT DISTINCT m.M_Movement_ID, m.Created, m.Updated, m.DocStatus
                    FROM M_Movement m
                   INNER JOIN M_MovementLine ml ON (ml.M_Movement_ID = m.M_Movement_ID)
                   INNER JOIN M_RequisitionLine rl
                           ON (rl.M_RequisitionLine_ID = ml.M_RequisitionLine_ID)
                   WHERE rl.M_Requisition_ID = @M_Requisition_ID
                     AND m.IsActive  = 'Y'
                     AND ml.IsActive = 'Y'
                     AND rl.IsActive = 'Y'");

            // Inventory Use issues raised against it. Restricted to internal use:
            // a physical count shares the same tables and is not a conversion.
            AddConversionMilestone(M_Requisition_ID, d, "M_Inventory", "M_InventoryLine", "M_RequisitionLine_ID",
                @"SELECT DISTINCT inv.M_Inventory_ID, inv.Created, inv.Updated, inv.DocStatus
                    FROM M_Inventory inv
                   INNER JOIN M_InventoryLine il ON (il.M_Inventory_ID = inv.M_Inventory_ID)
                   INNER JOIN M_RequisitionLine rl
                           ON (rl.M_RequisitionLine_ID = il.M_RequisitionLine_ID)
                   WHERE rl.M_Requisition_ID = @M_Requisition_ID
                     AND inv.IsActive = 'Y'
                     AND il.IsActive  = 'Y'
                     AND rl.IsActive  = 'Y'
                     AND COALESCE(inv.IsInternalUse, 'N') = 'Y'");

            // Anything raised at all IS the conversion. IsConverted used to be
            // ConvertedLineCount > 0 (RollUpTotals), which counts only the lines a
            // PURCHASE ORDER was written back onto.
            if (d.ConvertedDate.HasValue) d.IsConverted = true;
        }

        /// <summary>
        /// Runs one "documents raised from this requisition" query and folds its
        /// rows into the Converted / In Fulfilment stamps — see
        /// <see cref="LoadConversionMilestones"/>.
        /// </summary>
        /// <param name="M_Requisition_ID">Selected requisition id.</param>
        /// <param name="d">Overview payload being populated.</param>
        /// <param name="what">Table being read, for the log line only.</param>
        /// <param name="guardTable">Table carrying the link column.</param>
        /// <param name="guardColumn">The link column that must exist.</param>
        /// <param name="sql">The query. Must select Created, Updated and DocStatus,
        /// and bind @M_Requisition_ID exactly once.</param>
        private void AddConversionMilestone(int M_Requisition_ID, RequisitionOverviewData d,
                                            string what, string guardTable, string guardColumn,
                                            string sql)
        {
            if (!ColumnExists(guardTable, guardColumn)) return;

            try
            {
                DataSet ds = DB.ExecuteDataset(sql, ReqParam(M_Requisition_ID), null);
                if (ds == null || ds.Tables.Count == 0) return;

                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    DateTime? created = Util.GetValueOfDateTime(r["Created"]);
                    DateTime? updated = Util.GetValueOfDateTime(r["Updated"]);
                    string status     = Util.GetValueOfString(r["DocStatus"]);

                    if (created.HasValue &&
                        (!d.ConvertedDate.HasValue || created.Value < d.ConvertedDate.Value))
                        d.ConvertedDate = created;

                    if (status == "CO" || status == "CL")
                    {
                        d.HasOrdered = true;
                        if (updated.HasValue &&
                            (!d.FulfilmentDate.HasValue || updated.Value < d.FulfilmentDate.Value))
                            d.FulfilmentDate = updated;
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Severe("AddConversionMilestone/" + what +
                            " (M_Requisition_ID=" + M_Requisition_ID + "): " + ex.Message);
            }
        }

        // ----------------------------------------------------------------- //
        //  Activity                                                          //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// Builds the activity feed from real events: the create milestone
        /// (M_Requisition.Created), a status milestone when completed / closed
        /// (M_Requisition.Updated), and chat comments (CM_ChatEntry on
        /// M_Requisition), newest-first. Each carries a Type the client maps to
        /// a badge.
        /// </summary>
        private List<ActivityData> LoadActivity(int M_Requisition_ID, RequisitionOverviewData d)
        {
            List<ActivityData> activity = new List<ActivityData>();

            // Milestones from the header we already have.
            //
            // The actor goes in UserName, which is where EVERY row in this feed
            // carries it and where the panel reads it from. These two used to put
            // it in Text — the row's headline field — so the panel had to splice it
            // into the sentence, and any row whose actor was missing simply read as
            // the bare action with no "by" at all. Text is left unset: the headline
            // for a milestone is the action, which the panel words itself.
            activity.Add(new ActivityData
            {
                Type     = "create",
                UserName = d.CreatedByName,
                Created  = d.Created
            });
            if (d.StatusCode == "CO" || d.StatusCode == "CL")
            {
                activity.Add(new ActivityData
                {
                    Type     = "status",
                    UserName = d.UpdatedByName,
                    Created  = d.Updated
                });
            }

            // Downstream documents — the rest of the requisition's lifecycle.
            LoadDocumentActivity(M_Requisition_ID, activity);

            // What was actually edited, field by field. Until this the feed's only
            // account of a change was the single "status" milestone above, derived
            // from M_Requisition.Updated — which is the LAST save and says nothing
            // about what it touched.
            LoadChangeActivity(M_Requisition_ID, activity);

            // Chat comments.
            LoadCommentActivity(M_Requisition_ID, activity);

            // E-mails sent against the requisition.
            LoadEmailActivity(M_Requisition_ID, activity);

            // Appointments, tasks, calls and letters filed against it.
            LoadSharedSourceActivity(M_Requisition_ID, activity);

            activity.Sort((a, b) =>
                b.Created.GetValueOrDefault(DateTime.MinValue)
                 .CompareTo(a.Created.GetValueOrDefault(DateTime.MinValue)));

            // Runaway guard only, not a headline count: the panel pages the feed
            // 15 rows at a time, so anything cut here is unreachable rather than
            // merely further down.
            if (activity.Count > MAX_ACTIVITY)
                activity = activity.GetRange(0, MAX_ACTIVITY);
            return activity;
        }

        /// <summary>Cap on the activity feed — see LoadActivity.</summary>
        private const int MAX_ACTIVITY = 200;

        /// <summary>
        /// Adds the e-mails sent against this requisition (MailAttachment1, joined
        /// by AD_Table_ID = M_Requisition + Record_ID = the requisition id) as
        /// "email" rows: recipient (MailAddress), subject (Title), body (TextMsg),
        /// when (Created) and who sent it (CreatedBy).
        ///
        /// A row counts as an e-mail when it carries a recipient on any address
        /// column, which is what makes it one. AttachmentType is deliberately not
        /// filtered on: its value varies between installations, and requiring a
        /// particular one hides mails that are really there. The table id is
        /// matched with IN + UPPER so a differently-cased or duplicated dictionary
        /// entry still resolves.
        ///
        /// "Has an address" is tested against a SPACE, not against ''. Oracle
        /// stores the empty string as NULL, so NVL(TRIM(x), '') yields NULL and
        /// `&lt;&gt; ''` compares against NULL — UNKNOWN for every row, including the
        /// ones that DO carry an address. Comparing to ' ' keeps the fallback
        /// non-null on Oracle, and SQL Server blank-pads the comparison so an
        /// empty address still fails it.
        /// </summary>
        private void LoadEmailActivity(int M_Requisition_ID, List<ActivityData> list)
        {
            try
            {
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
                                        WHERE UPPER(t.TableName) = 'M_REQUISITION')
                                  AND ma.Record_ID          = @M_Requisition_ID
                                  AND NVL(ma.IsActive, 'Y') = 'Y'
                                  -- Letters ('I') are a kind of their own and are
                                  -- read by LoadSharedSourceActivity; without this
                                  -- they would appear twice, once as an e-mail.
                                  AND COALESCE(ma.AttachmentType, 'M') <> 'I'
                                  AND (NVL(TRIM(ma.MailAddress), ' ')    <> ' '
                                    OR NVL(TRIM(ma.MailAddressCc), ' ')  <> ' '
                                    OR NVL(TRIM(ma.MailAddressBcc), ' ') <> ' ')
                                ORDER BY ma.Created DESC";
                DataSet ds = DB.ExecuteDataset(sql, ReqParam(M_Requisition_ID), null);
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
                _log.Severe("LoadEmailActivity (M_Requisition_ID=" + M_Requisition_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// Cheap "is this markup" test — a real tag, not a stray '&lt;' in a plain
        /// text mail ("qty &lt; 10"), so a plain body is left untouched.
        /// </summary>
        private static readonly Regex HTML_BODY = new Regex(
            @"<\s*/?\s*(html|body|head|br|p|div|table|thead|tbody|tr|td|th|span|a|img|b|i|u"
            + @"|strong|em|ul|ol|li|h[1-6]|font|style|script)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Flattens an HTML mail body to readable plain text: script / style
        /// dropped, block ends turned into line breaks, tags stripped, entities
        /// decoded and runs of blank lines collapsed. A body that is already plain
        /// text is returned unchanged.
        /// </summary>
        private static string MailBodyToText(string body)
        {
            if (string.IsNullOrEmpty(body)) return "";
            if (!HTML_BODY.IsMatch(body)) return body.Trim();

            try
            {
                string s = body;
                s = Regex.Replace(s, @"<\s*(script|style)\b[^>]*>.*?<\s*/\s*\1\s*>",
                                  " ", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                s = Regex.Replace(s, @"<\s*br\s*/?\s*>", "\n", RegexOptions.IgnoreCase);
                s = Regex.Replace(s, @"<\s*/\s*(p|div|tr|li|h[1-6]|table)\s*>", "\n",
                                  RegexOptions.IgnoreCase);
                s = Regex.Replace(s, @"<\s*/\s*(td|th)\s*>", "\t", RegexOptions.IgnoreCase);
                s = Regex.Replace(s, @"<[^>]+>", "");
                s = WebUtility.HtmlDecode(s);
                s = s.Replace("\r\n", "\n").Replace("\r", "\n");
                s = Regex.Replace(s, @"[ \t]+\n", "\n");
                s = Regex.Replace(s, @"\n{3,}", "\n\n");
                return s.Trim();
            }
            catch
            {
                return body;
            }
        }

        /// <summary>
        /// Adds the downstream documents the requisition produced, so the feed
        /// carries the whole lifecycle rather than stopping at the requisition:
        ///
        ///   po          — a purchase order was raised from this requisition.
        ///   grn         — a goods receipt was booked against one of those orders.
        ///   grncomplete — that receipt was completed.
        ///
        /// Purchase orders are reached through M_RequisitionLine.C_OrderLine_ID and
        /// receipts through their own C_OrderLine_ID back to the same order lines,
        /// so only documents genuinely descended from this requisition appear.
        /// </summary>
        private void LoadDocumentActivity(int M_Requisition_ID, List<ActivityData> list)
        {
            // ----- Purchase orders raised from the requisition -----
            try
            {
                string sql = @"SELECT o.DocumentNo,
                                      o.Created,
                                      u.Name AS UserName
                                 FROM C_Order o
                                 LEFT OUTER JOIN AD_User u ON (o.CreatedBy = u.AD_User_ID)
                                WHERE o.IsActive = 'Y'
                                  AND EXISTS (SELECT 1
                                                FROM C_OrderLine ol
                                               INNER JOIN M_RequisitionLine rl
                                                       ON (rl.C_OrderLine_ID = ol.C_OrderLine_ID)
                                               WHERE ol.C_Order_ID       = o.C_Order_ID
                                                 AND rl.M_Requisition_ID = @M_Requisition_ID
                                                 AND rl.IsActive         = 'Y')";
                DataSet ds = DB.ExecuteDataset(sql, ReqParam(M_Requisition_ID), null);
                if (ds != null && ds.Tables.Count > 0)
                {
                    foreach (DataRow r in ds.Tables[0].Rows)
                    {
                        // The actor travels in UserName only. It used to be copied
                        // into Text as well — the headline field — which the panel
                        // then had to splice into the sentence it builds.
                        list.Add(new ActivityData
                        {
                            Type       = "po",
                            DocumentNo = Util.GetValueOfString(r["DocumentNo"]),
                            UserName   = Util.GetValueOfString(r["UserName"]),
                            Created    = Util.GetValueOfDateTime(r["Created"])
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadDocumentActivity/PO (M_Requisition_ID=" + M_Requisition_ID + "): " + ex.Message);
            }

            // ----- Every other document raised from the requisition -----
            //
            // The feed reported the purchase orders and their goods receipts and
            // nothing else, so an RFQ, a material transfer or an inventory-use
            // issue created from this requisition left no trace in the trail at all
            // — even though the Documents section listed it. Each is created from
            // the requisition by its own process, and each is worth a row saying
            // WHAT was created, its document number, who created it and when.
            //
            // Every source is column-guarded and separately try/caught, so a schema
            // without the module behind one simply contributes no rows.
            AddCreationActivity(M_Requisition_ID, list, "rfqcreated", "C_RfQ", null,
                @"SELECT q.DocumentNo, q.Created, u.Name AS UserName
                    FROM C_RfQ q
                    LEFT OUTER JOIN AD_User u ON (q.CreatedBy = u.AD_User_ID)
                   WHERE q.M_Requisition_ID = @M_Requisition_ID
                     AND q.IsActive = 'Y'",
                "C_RfQ", "M_Requisition_ID");

            AddCreationActivity(M_Requisition_ID, list, "movementcreated", "M_Movement", null,
                @"SELECT m.DocumentNo, m.Created, u.Name AS UserName
                    FROM M_Movement m
                    LEFT OUTER JOIN AD_User u ON (m.CreatedBy = u.AD_User_ID)
                   WHERE m.IsActive = 'Y'
                     AND EXISTS (SELECT 1
                                   FROM M_MovementLine ml
                                  INNER JOIN M_RequisitionLine rl
                                          ON (rl.M_RequisitionLine_ID = ml.M_RequisitionLine_ID)
                                  WHERE ml.M_Movement_ID    = m.M_Movement_ID
                                    AND rl.M_Requisition_ID = @M_Requisition_ID
                                    AND ml.IsActive         = 'Y'
                                    AND rl.IsActive         = 'Y')",
                "M_MovementLine", "M_RequisitionLine_ID");

            AddCreationActivity(M_Requisition_ID, list, "internalusecreated", "M_Inventory", null,
                @"SELECT inv.DocumentNo, inv.Created, u.Name AS UserName
                    FROM M_Inventory inv
                    LEFT OUTER JOIN AD_User u ON (inv.CreatedBy = u.AD_User_ID)
                   WHERE inv.IsActive = 'Y'
                     AND COALESCE(inv.IsInternalUse, 'N') = 'Y'
                     AND EXISTS (SELECT 1
                                   FROM M_InventoryLine il
                                  INNER JOIN M_RequisitionLine rl
                                          ON (rl.M_RequisitionLine_ID = il.M_RequisitionLine_ID)
                                  WHERE il.M_Inventory_ID   = inv.M_Inventory_ID
                                    AND rl.M_Requisition_ID = @M_Requisition_ID
                                    AND il.IsActive         = 'Y'
                                    AND rl.IsActive         = 'Y')",
                "M_InventoryLine", "M_RequisitionLine_ID");

            // ----- Goods receipts booked against those orders -----
            try
            {
                string sql = @"SELECT io.DocumentNo,
                                      io.DocStatus,
                                      io.Created,
                                      io.Updated,
                                      u.Name AS UserName
                                 FROM M_InOut io
                                 LEFT OUTER JOIN AD_User u ON (io.CreatedBy = u.AD_User_ID)
                                WHERE io.IsActive = 'Y'
                                  AND io.IsSOTrx  = 'N'
                                  AND EXISTS (SELECT 1
                                                FROM M_InOutLine iol
                                               INNER JOIN M_RequisitionLine rl
                                                       ON (rl.C_OrderLine_ID = iol.C_OrderLine_ID)
                                               WHERE iol.M_InOut_ID      = io.M_InOut_ID
                                                 AND iol.IsActive        = 'Y'
                                                 AND rl.M_Requisition_ID = @M_Requisition_ID
                                                 AND rl.IsActive         = 'Y')";
                DataSet ds = DB.ExecuteDataset(sql, ReqParam(M_Requisition_ID), null);
                if (ds == null || ds.Tables.Count == 0) return;

                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    string docNo   = Util.GetValueOfString(r["DocumentNo"]);
                    string user    = Util.GetValueOfString(r["UserName"]);
                    string status  = Util.GetValueOfString(r["DocStatus"]);

                    list.Add(new ActivityData
                    {
                        Type       = "grn",
                        DocumentNo = docNo,
                        UserName   = user,
                        Created    = Util.GetValueOfDateTime(r["Created"])
                    });

                    // A completed receipt gets its own entry, stamped with the
                    // change that carried it there.
                    if (status == "CO" || status == "CL")
                    {
                        list.Add(new ActivityData
                        {
                            Type       = "grncomplete",
                            DocumentNo = docNo,
                            UserName   = user,
                            Created    = Util.GetValueOfDateTime(r["Updated"])
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadDocumentActivity/GRN (M_Requisition_ID=" + M_Requisition_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// One "updated" row per FIELD the user changed, read from the platform's
        /// change log (AD_ChangeLog). Each row names the field (the dictionary's
        /// display name for the column, falling back to the raw column name), who
        /// changed it and when — so the trail says which field moved rather than
        /// only that the record's status changed at some point.
        ///
        /// Both the header (M_Requisition) and its LINES (M_RequisitionLine) are
        /// read. A requisition's substantive edits are its requested quantities and
        /// products, and those live on the lines — a header-only trail would report
        /// almost nothing. A line row is labelled with its line number and product
        /// so the reader can tell which row moved.
        ///
        /// The table name is matched with UPPER so a dictionary holding it in
        /// another case still resolves: an equality on the stored spelling fails
        /// SILENTLY, which reads exactly like the loader was never written.
        ///
        /// Silently degrades when change logging is off for the ROLE that made the
        /// edit (AD_Role.IsChangeLog) — the platform writes no AD_ChangeLog rows at
        /// all in that case. That is a dictionary setting, not something this can
        /// fix.
        /// </summary>
        /// <param name="M_Requisition_ID">Selected requisition id.</param>
        /// <param name="list">Activity list being populated.</param>
        private void LoadChangeActivity(int M_Requisition_ID, List<ActivityData> list)
        {
            // ----- Header edits (M_Requisition) -----
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
                                WHERE cl.Record_ID = @M_Requisition_ID
                                  AND UPPER(adt.TableName) = 'M_REQUISITION'
                                  AND NVL(cl.IsActive, 'Y') = 'Y'
                                ORDER BY cl.Created DESC";
                DataSet ds = DB.ExecuteDataset(sql, ReqParam(M_Requisition_ID), null);
                if (ds != null && ds.Tables.Count > 0)
                {
                    foreach (DataRow r in ds.Tables[0].Rows) AddChangeRow(r, "", list);
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadChangeActivity/header (M_Requisition_ID=" + M_Requisition_ID + "): " + ex.Message);
            }

            // ----- Line edits (M_RequisitionLine) -----
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
                                INNER JOIN M_RequisitionLine l
                                        ON (l.M_RequisitionLine_ID = cl.Record_ID)
                                 LEFT OUTER JOIN M_Product p
                                        ON (p.M_Product_ID = l.M_Product_ID)
                                 LEFT OUTER JOIN AD_Column col
                                        ON (col.AD_Column_ID = cl.AD_Column_ID)
                                 LEFT OUTER JOIN AD_User u
                                        ON (u.AD_User_ID = cl.CreatedBy)
                                WHERE UPPER(adt.TableName) = 'M_REQUISITIONLINE'
                                  AND NVL(cl.IsActive, 'Y') = 'Y'
                                  AND l.M_Requisition_ID = @M_Requisition_ID
                                ORDER BY cl.Created DESC";
                DataSet ds = DB.ExecuteDataset(sql, ReqParam(M_Requisition_ID), null);
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
                _log.Severe("LoadChangeActivity/lines (M_Requisition_ID=" + M_Requisition_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// Turns one AD_ChangeLog row into an "updated" activity entry. A change
        /// whose column the dictionary cannot name is skipped: it would render as a
        /// bare "Updated" identifying nothing, which is what naming the field
        /// exists to stop.
        /// </summary>
        /// <param name="r">Change-log row.</param>
        /// <param name="scope">Which record the edit landed on: "" for the
        /// requisition header, else the line's number and product.</param>
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
                Type        = "updated",
                FieldName   = field,
                OldValue    = _changeValues.Display(oldValue, column, refType, refValueId),
                NewValue    = _changeValues.Display(newValue, column, refType, refValueId),
                ChangeScope = scope,
                UserName    = Util.GetValueOfString(r["UserName"]),
                Created     = at
            });
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
        /// Runs one "document created from this requisition" query and adds a row
        /// per document: the type the client labels it by, its document number, who
        /// created it and when.
        ///
        /// Guarded on the link column that makes the query possible at all — the
        /// requisition references on M_MovementLine and M_InventoryLine belong to a
        /// module that is not part of this solution — and try/caught on top, so a
        /// schema without it contributes no rows rather than costing the feed its
        /// other sources.
        /// </summary>
        /// <param name="M_Requisition_ID">Selected requisition id.</param>
        /// <param name="list">Activity list being populated.</param>
        /// <param name="type">Activity type the client maps to a badge + sentence.</param>
        /// <param name="what">Table being read, for the log line only.</param>
        /// <param name="unused">Reserved; keeps the call sites aligned.</param>
        /// <param name="sql">The query. Must select DocumentNo, Created, UserName
        /// and bind @M_Requisition_ID exactly once.</param>
        /// <param name="guardTable">Table carrying the link column, or "" to skip
        /// the guard.</param>
        /// <param name="guardColumn">The link column that must exist.</param>
        private void AddCreationActivity(int M_Requisition_ID, List<ActivityData> list,
                                         string type, string what, string unused,
                                         string sql, string guardTable, string guardColumn)
        {
            if (!string.IsNullOrEmpty(guardTable) && !ColumnExists(guardTable, guardColumn))
                return;

            try
            {
                DataSet ds = DB.ExecuteDataset(sql, ReqParam(M_Requisition_ID), null);
                if (ds == null || ds.Tables.Count == 0) return;

                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    list.Add(new ActivityData
                    {
                        Type       = type,
                        DocumentNo = Util.GetValueOfString(r["DocumentNo"]),
                        UserName   = Util.GetValueOfString(r["UserName"]),
                        Created    = Util.GetValueOfDateTime(r["Created"])
                    });
                }
            }
            catch (Exception ex)
            {
                _log.Severe("AddCreationActivity/" + what +
                            " (M_Requisition_ID=" + M_Requisition_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// The correspondence and engagement sources shared with every other
        /// overview panel: appointments and tasks (AppointmentsInfo, split on
        /// IsTask), calls (VA048_CallDetails) and letters (MailAttachment1,
        /// AttachmentType 'I'). Each hangs off the requisition by AD_Table_ID +
        /// Record_ID.
        ///
        /// Mails are NOT taken from here — LoadEmailActivity above reads them with
        /// the recipient and body detail this panel's mail drawer needs, and now
        /// excludes letters so the two do not overlap.
        /// </summary>
        /// <param name="M_Requisition_ID">Selected requisition id.</param>
        /// <param name="list">Activity list being populated.</param>
        private void LoadSharedSourceActivity(int M_Requisition_ID, List<ActivityData> list)
        {
            List<VAS_ActivitySourceRow> rows =
                _activitySources.Load("M_Requisition", M_Requisition_ID, false);
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
                    MailTo     = s.MailTo,
                    MailCc     = s.MailCc,
                    MailBcc    = s.MailBcc,
                    MailFrom   = s.MailFrom,
                    IsMailSent = s.IsMailSent,
                    // An appointment or task brings the mails sent against it.
                    Mails      = s.Mails,
                    UserName   = s.ActorName,
                    Created    = s.EventTime
                });
            }
        }

        /// <summary>Reads the appointment / task / call / letter sources every
        /// overview panel shares (VAS_ActivitySourcesModel).</summary>
        private readonly VAS_ActivitySourcesModel _activitySources = new VAS_ActivitySourcesModel();

        /// <summary>Loads CM_ChatEntry comments logged against the requisition.</summary>
        private void LoadCommentActivity(int M_Requisition_ID, List<ActivityData> list)
        {
            try
            {
                // The author resolves from CM_ChatEntry.AD_User_ID falling back to
                // CreatedBy. An entry logged through the platform's own chat
                // plumbing often leaves AD_User_ID null, and asking that column
                // alone printed the comment and its timestamp with NO NAME against
                // it — which is most of them. CreatedBy is always stamped.
                //
                // The table id is matched with IN + UPPER so a dictionary holding
                // the name in another case, or more than one row for it, resolves
                // instead of failing the statement: as a scalar sub-select a second
                // row RAISES, and the whole comment feed would vanish with it.
                string sql = @"SELECT ce.CharacterData,
                                      ce.Created,
                                      COALESCE(u.Name, cu.Name) AS UserName
                                 FROM CM_ChatEntry ce
                                 INNER JOIN CM_Chat ch      ON (ce.CM_Chat_ID = ch.CM_Chat_ID)
                                 LEFT OUTER JOIN AD_User u  ON (ce.AD_User_ID = u.AD_User_ID)
                                 LEFT OUTER JOIN AD_User cu ON (ce.CreatedBy  = cu.AD_User_ID)
                                WHERE ch.AD_Table_ID IN
                                      (SELECT t.AD_Table_ID FROM AD_Table t
                                        WHERE UPPER(t.TableName) = 'M_REQUISITION')
                                  AND ch.Record_ID = @M_Requisition_ID
                                  AND ce.IsActive  = 'Y'";
                DataSet ds = DB.ExecuteDataset(sql, ReqParam(M_Requisition_ID), null);
                if (ds == null || ds.Tables.Count == 0) return;
                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    list.Add(new ActivityData
                    {
                        Type     = "comment",
                        Text     = Util.GetValueOfString(r["CharacterData"]),
                        UserName = Util.GetValueOfString(r["UserName"]),
                        Created  = Util.GetValueOfDateTime(r["Created"])
                    });
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadCommentActivity (M_Requisition_ID=" + M_Requisition_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// The budgeted amount set for this requisition, for the Budget figure in
        /// the items footer.
        ///
        /// The panel's first choice is M_RequisitionLine.VAS_AvailableBudget, the
        /// figure the "Calculate Budget" process stamps on each line. That process
        /// only runs where budget control is configured AND has been run against
        /// the requisition, so on most records the column is 0 and the footer read
        /// N/A — a budget field that never showed a budget.
        ///
        /// This is the fallback: the amount budgeted under the GL budget that
        /// APPLIES to this requisition, derived the same way CalculateBudget picks
        /// its budget and the same way BudgetCheck derives the controlled amount:
        ///
        ///   * the budget's control is active, commitment type 'B' and visible to
        ///     the requisition's organisation;
        ///   * the budget's basis places the requisition's document date inside it
        ///     — period basis ('P') matching C_Period_ID, annual basis ('A')
        ///     matching C_Year_ID, against the client's own calendar;
        ///   * the amount is SUM(AmtAcctDr - AmtAcctCr) over Fact_Acct rows
        ///     carrying that GL_Budget_ID, within the budget's period or year.
        ///
        /// What it deliberately does NOT do is narrow by the budget control's
        /// dimensions (account, product, project, ...). That narrowing is per LINE
        /// and needs the accounting record BudgetCheck builds during posting; this
        /// is the requisition-level figure — the budget the requisition is drawn
        /// against, not the balance left on one line's control. It is therefore a
        /// broader number than VAS_AvailableBudget, which is why the stamped value
        /// still wins whenever it exists.
        ///
        /// Guarded and try/caught: an install without the budgeting tables, or
        /// with no budget covering the document date, simply leaves the figure at
        /// 0 and the footer reads N/A exactly as it does today.
        /// </summary>
        /// <param name="M_Requisition_ID">Selected requisition id.</param>
        /// <param name="d">Overview payload being populated.</param>
        private void LoadBudgetAmount(int M_Requisition_ID, RequisitionOverviewData d)
        {
            // BudgetControlBasis is what places a budget against the requisition's
            // date in the two reads below, so they are skipped without it. The last
            // read does not need it and runs either way — the whole loader used to
            // return here, and that column is only ever filled where commitment
            // control is configured.
            bool hasBasis = ColumnExists("GL_Budget", "BudgetControlBasis");

            // Narrowed by the budget CONTROL first — the configuration the platform
            // itself checks a requisition against. Only when that yields nothing is
            // the control requirement dropped (see LoadBudgetForPeriod): a budget
            // can exist and cover the requisition's date without anyone having set
            // up a commitment control over it, and the footer read N/A on exactly
            // those installs. The controlled figure still wins where there is one.
            if (hasBasis && ColumnExists("GL_BudgetControl", "CommitmentType"))
                LoadControlledBudgetAmount(M_Requisition_ID, d);
            if (hasBasis && d.AvailableBudget == 0)
                LoadBudgetForPeriod(M_Requisition_ID, d);
            // Neither of those can see a budget whose basis was never set, which is
            // the ordinary state of a budget nobody controls against.
            if (d.AvailableBudget == 0)
                LoadBudgetForYear(M_Requisition_ID, d);
        }

        /// <summary>
        /// The budgeted amount under a GL budget that this requisition's ORG is
        /// commitment-controlled against — the narrow reading, matching
        /// CalculateBudget / BudgetCheck. See LoadBudgetAmount for the full
        /// derivation and its limits.
        /// </summary>
        private void LoadControlledBudgetAmount(int M_Requisition_ID, RequisitionOverviewData d)
        {
            try
            {
                // The requisition id is inlined rather than bound: it appears in
                // two places below and positional binding gives a repeated bind
                // name a second, unfilled placeholder. It is an int, so nothing
                // can be injected.
                string reqId = M_Requisition_ID.ToString();

                string sql = @"SELECT SUM(fa.AmtAcctDr - fa.AmtAcctCr) AS BudgetAmount
                                 FROM Fact_Acct fa
                                INNER JOIN GL_Budget b ON (b.GL_Budget_ID = fa.GL_Budget_ID)
                                WHERE fa.GL_Budget_ID IN (
                                        SELECT b2.GL_Budget_ID
                                          FROM GL_Budget b2
                                         INNER JOIN GL_BudgetControl bc
                                                 ON (bc.GL_Budget_ID = b2.GL_Budget_ID)
                                         INNER JOIN M_Requisition r
                                                 ON (r.M_Requisition_ID = " + reqId + @")
                                         INNER JOIN AD_ClientInfo ci
                                                 ON (ci.AD_Client_ID = b2.AD_Client_ID)
                                         WHERE b2.IsActive = 'Y'
                                           AND bc.IsActive = 'Y'
                                           AND bc.CommitmentType = 'B'
                                           AND bc.AD_Org_ID IN (0, r.AD_Org_ID)
                                           AND ((b2.BudgetControlBasis = 'P'
                                                 AND b2.C_Period_ID = (SELECT p.C_Period_ID
                                                                         FROM C_Period p
                                                                        INNER JOIN C_Year y
                                                                                ON (y.C_Year_ID = p.C_Year_ID)
                                                                        WHERE p.IsActive = 'Y'
                                                                          AND y.C_Calendar_ID = ci.C_Calendar_ID
                                                                          AND r.DateDoc BETWEEN p.StartDate AND p.EndDate))
                                             OR (b2.BudgetControlBasis = 'A'
                                                 AND b2.C_Year_ID = (SELECT p.C_Year_ID
                                                                       FROM C_Period p
                                                                      INNER JOIN C_Year y
                                                                              ON (y.C_Year_ID = p.C_Year_ID)
                                                                      WHERE p.IsActive = 'Y'
                                                                        AND y.C_Calendar_ID = ci.C_Calendar_ID
                                                                        AND r.DateDoc BETWEEN p.StartDate AND p.EndDate))))
                                  AND (fa.C_Period_ID = COALESCE(b.C_Period_ID, 0)
                                    OR fa.C_Period_ID IN (SELECT p3.C_Period_ID
                                                            FROM C_Period p3
                                                           WHERE p3.C_Year_ID = COALESCE(b.C_Year_ID, 0)))";

                DataSet ds = DB.ExecuteDataset(sql, null, null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return;

                decimal amount = Util.GetValueOfDecimal(ds.Tables[0].Rows[0]["BudgetAmount"]);
                if (amount != 0)
                {
                    d.AvailableBudget   = amount;
                    // The panel says where the figure came from, so a reader is
                    // never told a line-level balance when this is the whole
                    // budget the requisition draws against.
                    d.BudgetIsRequisitionLevel = true;
                }
            }
            catch (Exception ex)
            {
                // Non-fatal: the broader read below still gets its turn, and
                // failing that the footer reads N/A exactly as before.
                _log.Severe("LoadControlledBudgetAmount (M_Requisition_ID=" + M_Requisition_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// The budgeted amount under whichever GL budget COVERS the requisition's
        /// document date, with no commitment-control requirement.
        ///
        /// This is the fallback behind LoadControlledBudgetAmount, and it exists
        /// because that one asks for three things at once: a GL_BudgetControl row,
        /// carrying commitment type 'B', visible to the requisition's org. A
        /// deployment that budgets but does not commitment-control its
        /// requisitions has none of them, and the panel's Budget figure read N/A on
        /// every record — a budget field that never showed a budget.
        ///
        /// What is dropped is only the CONTROL. The budget still has to be active,
        /// belong to the requisition's tenant, and place the document date inside
        /// it on its own basis (period 'P' -> C_Period_ID, annual 'A' ->
        /// C_Year_ID), and the amount is still SUM(AmtAcctDr - AmtAcctCr) over the
        /// Fact_Acct rows carrying that GL_Budget_ID within it — the same
        /// arithmetic BudgetCheck uses. So it is the same KIND of number, derived
        /// over a wider set of budgets.
        ///
        /// It is therefore broader than the controlled figure, which is why that
        /// one is asked for first and this only runs when it came back empty.
        /// </summary>
        /// <param name="M_Requisition_ID">Selected requisition id.</param>
        /// <param name="d">Overview payload being populated.</param>
        private void LoadBudgetForPeriod(int M_Requisition_ID, RequisitionOverviewData d)
        {
            try
            {
                // Inlined for the same reason as above: the id appears twice and a
                // repeated bind name gets a second, unfilled placeholder under
                // positional binding. It is an int, so nothing can be injected.
                string reqId = M_Requisition_ID.ToString();

                string sql = @"SELECT SUM(fa.AmtAcctDr - fa.AmtAcctCr) AS BudgetAmount
                                 FROM Fact_Acct fa
                                INNER JOIN GL_Budget b ON (b.GL_Budget_ID = fa.GL_Budget_ID)
                                WHERE fa.GL_Budget_ID IN (
                                        SELECT b2.GL_Budget_ID
                                          FROM GL_Budget b2
                                         INNER JOIN M_Requisition r
                                                 ON (r.M_Requisition_ID = " + reqId + @")
                                         INNER JOIN AD_ClientInfo ci
                                                 ON (ci.AD_Client_ID = b2.AD_Client_ID)
                                         WHERE b2.IsActive     = 'Y'
                                           AND b2.AD_Client_ID = r.AD_Client_ID
                                           AND ((b2.BudgetControlBasis = 'P'
                                                 AND b2.C_Period_ID = (SELECT p.C_Period_ID
                                                                         FROM C_Period p
                                                                        INNER JOIN C_Year y
                                                                                ON (y.C_Year_ID = p.C_Year_ID)
                                                                        WHERE p.IsActive = 'Y'
                                                                          AND y.C_Calendar_ID = ci.C_Calendar_ID
                                                                          AND r.DateDoc BETWEEN p.StartDate AND p.EndDate))
                                             OR (b2.BudgetControlBasis = 'A'
                                                 AND b2.C_Year_ID = (SELECT p.C_Year_ID
                                                                       FROM C_Period p
                                                                      INNER JOIN C_Year y
                                                                              ON (y.C_Year_ID = p.C_Year_ID)
                                                                      WHERE p.IsActive = 'Y'
                                                                        AND y.C_Calendar_ID = ci.C_Calendar_ID
                                                                        AND r.DateDoc BETWEEN p.StartDate AND p.EndDate))))
                                  AND (fa.C_Period_ID = COALESCE(b.C_Period_ID, 0)
                                    OR fa.C_Period_ID IN (SELECT p3.C_Period_ID
                                                            FROM C_Period p3
                                                           WHERE p3.C_Year_ID = COALESCE(b.C_Year_ID, 0)))";

                DataSet ds = DB.ExecuteDataset(sql, null, null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return;

                decimal amount = Util.GetValueOfDecimal(ds.Tables[0].Rows[0]["BudgetAmount"]);
                if (amount != 0)
                {
                    d.AvailableBudget = amount;
                    d.BudgetIsRequisitionLevel = true;
                }
            }
            catch (Exception ex)
            {
                // Non-fatal: the footer keeps reading N/A, exactly as before.
                _log.Severe("LoadBudgetForPeriod (M_Requisition_ID=" + M_Requisition_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// The amount budgeted for the requisition's FISCAL YEAR — the last of the
        /// three budget reads, and the only one that asks nothing of the budget's
        /// configuration.
        ///
        /// The two above it both require GL_Budget.BudgetControlBasis to name where
        /// the budget applies ('P' -> a period, 'A' -> a year). That column is part
        /// of commitment CONTROL, and a deployment that budgets without controlling
        /// its requisitions against the budget leaves it null — so a budget was
        /// posted, the requisition sat inside it, and the footer still read N/A.
        /// That is the case this exists for.
        ///
        /// It is read from the budget side instead: the Fact_Acct rows that CARRY a
        /// GL_Budget_ID (which is what makes a row a budget entry — the platform's
        /// own BudgetCheck identifies them the same way), belonging to the
        /// requisition's tenant, visible to its organisation, and posted into a
        /// period of the fiscal year the requisition's document date falls in. The
        /// amount is SUM(AmtAcctDr - AmtAcctCr), the same arithmetic as the reads
        /// above.
        ///
        /// It is therefore the BROADEST of the three — a whole year, across every
        /// budget and account — which is why it runs last and only when the other
        /// two came back empty. The panel labels it as a requisition-level figure,
        /// so it is never presented as one line's remaining balance.
        /// </summary>
        /// <param name="M_Requisition_ID">Selected requisition id.</param>
        /// <param name="d">Overview payload being populated.</param>
        private void LoadBudgetForYear(int M_Requisition_ID, RequisitionOverviewData d)
        {
            try
            {
                // Inlined for the same reason as the two reads above: the id would
                // otherwise be bound more than once, and positional binding gives a
                // repeated name a second, unfilled placeholder. It is an int, so
                // nothing can be injected.
                string reqId = M_Requisition_ID.ToString();

                string sql = @"SELECT SUM(fa.AmtAcctDr - fa.AmtAcctCr) AS BudgetAmount
                                 FROM Fact_Acct fa
                                INNER JOIN M_Requisition r
                                        ON (r.M_Requisition_ID = " + reqId + @")
                                INNER JOIN AD_ClientInfo ci
                                        ON (ci.AD_Client_ID = fa.AD_Client_ID)
                                WHERE COALESCE(fa.GL_Budget_ID, 0) > 0
                                  AND fa.AD_Client_ID = r.AD_Client_ID
                                  AND fa.AD_Org_ID IN (0, r.AD_Org_ID)
                                  AND fa.C_Period_ID IN (
                                        SELECT p.C_Period_ID
                                          FROM C_Period p
                                         INNER JOIN C_Year y ON (y.C_Year_ID = p.C_Year_ID)
                                         WHERE p.IsActive      = 'Y'
                                           AND y.C_Calendar_ID = ci.C_Calendar_ID
                                           AND p.C_Year_ID     = (SELECT p2.C_Year_ID
                                                                    FROM C_Period p2
                                                                   INNER JOIN C_Year y2
                                                                           ON (y2.C_Year_ID = p2.C_Year_ID)
                                                                   WHERE p2.IsActive      = 'Y'
                                                                     AND y2.C_Calendar_ID = ci.C_Calendar_ID
                                                                     AND r.DateDoc BETWEEN p2.StartDate
                                                                                       AND p2.EndDate))";

                DataSet ds = DB.ExecuteDataset(sql, null, null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return;

                decimal amount = Util.GetValueOfDecimal(ds.Tables[0].Rows[0]["BudgetAmount"]);
                if (amount != 0)
                {
                    d.AvailableBudget = amount;
                    d.BudgetIsRequisitionLevel = true;
                }
            }
            catch (Exception ex)
            {
                // Non-fatal: the footer keeps reading N/A, exactly as before.
                _log.Severe("LoadBudgetForYear (M_Requisition_ID=" + M_Requisition_ID + "): " + ex.Message);
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

        /// <summary>Single-parameter helper for the requisition-scoped queries.</summary>
        private SqlParameter[] ReqParam(int M_Requisition_ID)
        {
            return new SqlParameter[] { new SqlParameter("@M_Requisition_ID", M_Requisition_ID) };
        }

        // ----------------------------------------------------------------- //
        //  Origin (Reference chip strip)                                     //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// Fills the source documents the requisition was generated FROM, for the
        /// panel's Reference strip — the mirror of the Purchase Order overview's
        /// "Generated From".
        ///
        /// A requisition can be raised by hand, by replenishment, from a project's
        /// planned lines, or by a module that consumes stock against its own
        /// document (VA075 field service, VAMFG manufacturing). Only the last two
        /// carry a document the reader can open, and only when their module is
        /// installed, so every probe here is column-guarded and independently
        /// try/caught: a requisition with no origin at all reports none and the
        /// panel says "Manual".
        /// </summary>
        /// <param name="ctx">User context — the replenishment marker is a
        /// translated message, so it is resolved in the caller's language.</param>
        /// <param name="M_Requisition_ID">Selected requisition id.</param>
        /// <param name="d">Overview payload being populated.</param>
        private void LoadOrigin(Ctx ctx, int M_Requisition_ID, RequisitionOverviewData d)
        {
            LoadReplenishmentOrigin(ctx, d);
            LoadProjectOrigin(M_Requisition_ID, d);
            LoadSalesOrderOrigin(M_Requisition_ID, d);
            LoadBlanketOrderOrigin(M_Requisition_ID, d);
            LoadVA075WorkOrderOrigin(M_Requisition_ID, d);
            LoadVA075FieldServiceReqOrigin(M_Requisition_ID, d);
            LoadProductionOrderOrigin(M_Requisition_ID, d);
            LoadOriginWindows(ctx, d);
        }

        /// <summary>
        /// Resolves, for each chip whose screen the CLIENT cannot name, the window
        /// the record actually opens in — and says so in the payload.
        ///
        /// The VA075 work order, the VA075 field service request and the VAMFG
        /// production order all belong to modules this solution does not ship, so
        /// their windows have no name that can be hard-coded on either side and the
        /// browser's zoom lookup only knows tables the client has cached. The panel
        /// used to click first and find out afterwards: it asked the server for a
        /// window at the moment of the click and, failing that, opened whatever the
        /// zoom lookup returned — which is how a click came to report an error
        /// instead of a record.
        ///
        /// Resolving it here instead means the chip KNOWS before it is drawn. A
        /// window that resolves makes the chip a link that opens straight into it;
        /// no window leaves the chip as plain text, still naming the document it
        /// came from. Nothing on screen can fail.
        /// </summary>
        /// <param name="ctx">User context — the role decides what may be opened.</param>
        /// <param name="d">Overview payload being populated.</param>
        private void LoadOriginWindows(Ctx ctx, RequisitionOverviewData d)
        {
            if (d.VA075_WorkOrder_ID > 0)
                d.WorkOrderWindowId = ResolveChipWindow(ctx, "", "VA075_WorkOrder");
            // The field service request's screen HAS a name — VA075_FieldService —
            // so it is asked for by name first and the dictionary's table lookup is
            // left behind it. Going straight to the table was what made this chip
            // fail: AD_Table.AD_Window_ID for VA075_FieldServiceReq resolves to a
            // window that does not maintain the record (or to none the role may
            // open), so the click started a screen the record could not be found
            // in and reported an error instead of showing it.
            if (d.VA075_FieldServiceReq_ID > 0)
                d.FieldServiceReqWindowId =
                    ResolveChipWindow(ctx, "VA075_FieldService", "VA075_FieldServiceReq");
            // The production order's screen HAS a name — VAS_102 opens it by that
            // name — so it is asked for first, the way VAS_092 resolves every window
            // it opens. The two VA075 screens have none that can be hard-coded, and
            // fall straight through to the dictionary.
            if (d.VAMFG_M_WorkOrder_ID > 0)
                d.ProductionOrderWindowId =
                    ResolveChipWindow(ctx, "VAMFG_ProductionOrder", "VAMFG_M_WorkOrder");

            // The blanket order, resolved BY NAME ONLY — deliberately without the
            // table fallback every other chip keeps behind it.
            //
            // C_Order's dictionary window is the ordinary order screen, and a
            // blanket order is not in it: the client used to resolve this name for
            // itself and, when the lookup came back empty, fell through to the
            // table's zoom target — which for a sales-side C_Order is the Sales
            // Order window. The click then opened a real screen filtered to a
            // record that screen does not carry, which is why it did not land on
            // the blanket sales order. A wrong screen is worse than no link, so
            // there is nothing behind the name here: a name that does not resolve
            // leaves the id at 0 and the panel draws the chip as plain text.
            if (d.BlanketOrderId > 0)
            {
                d.BlanketOrderWindowId = GetWindowId(ctx,
                    d.BlanketOrderIsSOTrx ? "VAS_BlanketSalesOrder"
                                          : "VAS_BlanketPurchaseOrder");
            }
        }

        /// <summary>
        /// The window a Reference chip's record opens in, resolved in the order
        /// VAS_092 resolves every window it navigates to:
        ///
        ///   1. the window NAMED for that record, looked up by AD_Window.Name
        ///      (<see cref="GetWindowId"/>) — the name is the authority wherever
        ///      there is one, because a table's zoom target cannot tell two kinds
        ///      of record apart and may not be the screen that maintains it;
        ///   2. failing that, the window the DICTIONARY says the table opens in
        ///      (<see cref="GetWindowIdByTable"/>) — the last resort for a module
        ///      that is not part of this solution and whose screen therefore has no
        ///      name that can be written down here.
        ///
        /// Step 2 is what VAS_092 does not have and does not need: every table it
        /// opens is one it can name. VAS_098 reaches two VA075 documents that it
        /// cannot, so it keeps the dictionary behind the name rather than instead
        /// of it.
        /// </summary>
        /// <param name="ctx">User context (client + role).</param>
        /// <param name="windowName">The window's name, or "" when it has none that
        /// can be named here.</param>
        /// <param name="tableName">The record's physical table.</param>
        /// <returns>The window id, or 0 when the record cannot be opened at all —
        /// which makes the panel draw the chip as plain text.</returns>
        private int ResolveChipWindow(Ctx ctx, string windowName, string tableName)
        {
            if (!string.IsNullOrEmpty(windowName))
            {
                int byName = GetWindowId(ctx, windowName);
                if (byName > 0) return byName;
            }
            return GetWindowIdByTable(ctx, tableName);
        }

        /// <summary>
        /// The order a requisition line was raised to satisfy
        /// (M_RequisitionLine.Ref_OrderLine_ID -> C_OrderLine -> C_Order) — the
        /// demand behind the request, typically a sales order whose stock had to be
        /// bought in.
        ///
        /// C_Order serves both sides of the trade, so IsSOTrx travels with the
        /// order: the panel labels the chip Sales Order or Order accordingly, and
        /// opens it in the matching window. Calling a purchase order a sales order
        /// because of where the link happens to point would be worse than saying
        /// nothing.
        /// </summary>
        private void LoadSalesOrderOrigin(int M_Requisition_ID, RequisitionOverviewData d)
        {
            if (!ColumnExists("M_RequisitionLine", "Ref_OrderLine_ID")) return;

            try
            {
                string sql = @"SELECT DISTINCT o.C_Order_ID, o.DocumentNo, o.IsSOTrx
                                 FROM M_RequisitionLine rl
                                INNER JOIN C_OrderLine ol
                                        ON (ol.C_OrderLine_ID = rl.Ref_OrderLine_ID)
                                INNER JOIN C_Order o
                                        ON (o.C_Order_ID = ol.C_Order_ID)
                                WHERE rl.M_Requisition_ID = @M_Requisition_ID
                                  AND rl.IsActive = 'Y'
                                ORDER BY o.DocumentNo";
                DataSet ds = DB.ExecuteDataset(sql, ReqParam(M_Requisition_ID), null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return;

                DataRow r = ds.Tables[0].Rows[0];
                d.RefOrderId    = Util.GetValueOfInt(r["C_Order_ID"]);
                d.RefOrderNo    = Util.GetValueOfString(r["DocumentNo"]);
                d.RefOrderIsSOTrx = Util.GetValueOfString(r["IsSOTrx"]) == "Y";
                d.RefOrderCount = ds.Tables[0].Rows.Count;
            }
            catch (Exception ex)
            {
                _log.Severe("LoadSalesOrderOrigin (M_Requisition_ID=" + M_Requisition_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// The BLANKET order the requisition was raised against — the standing
        /// commitment behind the request, as opposed to the ordinary order
        /// <see cref="LoadSalesOrderOrigin"/> finds.
        ///
        /// It is reached from the same place, M_RequisitionLine.Ref_OrderLine_ID,
        /// by three routes, because the blanket link is written in more than one
        /// place by more than one piece of code:
        ///
        ///   1. The referenced order IS the blanket (C_Order.IsBlanketTrx = 'Y') —
        ///      a requisition raised directly against the commitment.
        ///   2. It is a RELEASE of one: C_Order.C_Order_Blanket, stamped on the
        ///      release header by the platform (MOrder.SetC_Order_Blanket).
        ///   3. Its LINE is a release of one: C_OrderLine.C_OrderLine_Blanket_ID,
        ///      written by the copy that creates each release line. This is the
        ///      route the header one misses — the two are set by different code and
        ///      a release can carry either.
        ///
        /// IsSOTrx travels with the result: C_Order serves both sides of the trade,
        /// and a blanket SALES order opens a different screen from a blanket
        /// purchase order. Reporting the wrong one would send the reader to a window
        /// that cannot show the record.
        ///
        /// Every column it rests on is optional and dictionary-guarded — the flag,
        /// the header link and the line link each enable their own route — so a
        /// schema carrying none of them simply draws no chip.
        /// </summary>
        /// <param name="M_Requisition_ID">Selected requisition id.</param>
        /// <param name="d">Overview payload being populated.</param>
        private void LoadBlanketOrderOrigin(int M_Requisition_ID, RequisitionOverviewData d)
        {
            if (!ColumnExists("M_RequisitionLine", "Ref_OrderLine_ID")) return;

            bool hasFlag       = ColumnExists("C_Order", "IsBlanketTrx");
            bool hasHeaderLink = ColumnExists("C_Order", "C_Order_Blanket");
            bool hasLineLink   = ColumnExists("C_OrderLine", "C_OrderLine_Blanket_ID");
            if (!hasFlag && !hasHeaderLink && !hasLineLink) return;

            try
            {
                // The id is inlined rather than bound: each UNION branch below would
                // otherwise carry the same bind name again, and positional binding
                // gives a repeated name a second, unfilled placeholder. It is an
                // int, so nothing can be injected.
                string reqId = M_Requisition_ID.ToString();
                const string REF_JOIN =
                    @"FROM M_RequisitionLine rl
                     INNER JOIN C_OrderLine ol ON (ol.C_OrderLine_ID = rl.Ref_OrderLine_ID)";
                string where = @"WHERE rl.M_Requisition_ID = " + reqId + @"
                                   AND rl.IsActive = 'Y'";

                List<string> sources = new List<string>();
                if (hasFlag)
                {
                    sources.Add(@"SELECT o.C_Order_ID AS BLANKET_ID " + REF_JOIN + @"
                                   INNER JOIN C_Order o ON (o.C_Order_ID = ol.C_Order_ID)
                                   " + where + @" AND COALESCE(o.IsBlanketTrx, 'N') = 'Y'");
                }
                if (hasHeaderLink)
                {
                    sources.Add(@"SELECT o.C_Order_Blanket AS BLANKET_ID " + REF_JOIN + @"
                                   INNER JOIN C_Order o ON (o.C_Order_ID = ol.C_Order_ID)
                                   " + where + @" AND COALESCE(o.C_Order_Blanket, 0) > 0");
                }
                if (hasLineLink)
                {
                    sources.Add(@"SELECT bol.C_Order_ID AS BLANKET_ID " + REF_JOIN + @"
                                   INNER JOIN C_OrderLine bol
                                           ON (bol.C_OrderLine_ID = ol.C_OrderLine_Blanket_ID)
                                   " + where + @" AND COALESCE(ol.C_OrderLine_Blanket_ID, 0) > 0");
                }

                string sql = @"SELECT bo.C_Order_ID, bo.DocumentNo, bo.IsSOTrx
                                 FROM C_Order bo
                                WHERE bo.IsActive = 'Y'
                                  AND bo.C_Order_ID IN ("
                                 + string.Join(" UNION ", sources.ToArray()) + @")
                                ORDER BY bo.DocumentNo";
                DataSet ds = DB.ExecuteDataset(sql, null, null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return;

                DataRow r = ds.Tables[0].Rows[0];
                d.BlanketOrderId      = Util.GetValueOfInt(r["C_Order_ID"]);
                d.BlanketOrderNo      = Util.GetValueOfString(r["DocumentNo"]);
                d.BlanketOrderIsSOTrx = Util.GetValueOfString(r["IsSOTrx"]) == "Y";
                d.BlanketOrderCount   = ds.Tables[0].Rows.Count;

                // The plain Order chip stands down when it found this same record —
                // a requisition raised straight against a blanket reaches it through
                // Ref_OrderLine_ID, so both loaders land on it and the strip would
                // carry one document twice, under two different names.
                if (d.RefOrderId > 0 && d.RefOrderId == d.BlanketOrderId)
                {
                    d.RefOrderId    = 0;
                    d.RefOrderNo    = "";
                    d.RefOrderCount = 0;
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadBlanketOrderOrigin (M_Requisition_ID=" + M_Requisition_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// Returns the first of the candidate columns that exists on the table, or
        /// an empty string when it has none of them. For reading a table this
        /// solution does not ship, where the identifier column's name varies
        /// between revisions of the module.
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
        /// Fills one VA075 origin: the document behind an id column carried on the
        /// requisition header and / or its lines.
        ///
        /// The work order and the field service request are read identically — a
        /// guarded id column on M_Requisition / M_RequisitionLine, and a document
        /// identifier on the referenced table whose column name is probed rather
        /// than assumed. VA075 is not part of this solution, so nothing here may
        /// reference its model classes and no column of it may be taken on trust.
        /// Without the module this is a few dictionary reads and no chip.
        /// </summary>
        /// <param name="M_Requisition_ID">Selected requisition id.</param>
        /// <param name="idColumn">The reference column, e.g. "VA075_WorkOrder_ID".</param>
        /// <param name="tableName">The referenced table, e.g. "VA075_WorkOrder".</param>
        /// <param name="labelCandidates">Identifier columns to try, in order.</param>
        /// <param name="recordId">Out: the first referenced record's id, else 0.</param>
        /// <param name="documentNo">Out: that record's identifier, else "".</param>
        /// <param name="count">Out: how many distinct records were referenced.</param>
        private void LoadVA075Reference(int M_Requisition_ID, string idColumn, string tableName,
                                        string[] labelCandidates,
                                        out int recordId, out string documentNo, out int count)
        {
            recordId = 0;
            documentNo = "";
            count = 0;

            // The module itself is asked for FIRST. A column guard proves a column
            // is in the dictionary; it does not prove the module that owns it is
            // installed and its windows deployed — a client that once had VA075, or
            // that took its dictionary rows without the module, answers "yes" to
            // every ColumnExists below and then has no screen to open the record in.
            // That is the state the field service request chip failed from.
            if (!Env.IsModuleInstalled("VA075_")) return;

            // The table's own key column is the proof the table is there at all.
            if (!ColumnExists(tableName, idColumn)) return;

            bool onHeader = ColumnExists("M_Requisition", idColumn);
            bool onLine   = ColumnExists("M_RequisitionLine", idColumn);
            if (!onHeader && !onLine) return;

            // The identifier to show on the chip. A table with none of the
            // candidates still gets a chip, labelled with its id.
            string labelCol = FirstExistingColumn(tableName, labelCandidates);
            string labelExpr = string.IsNullOrEmpty(labelCol)
                ? "CAST(NULL AS VARCHAR(255))" : "src." + labelCol;

            try
            {
                // The ids reachable from the requisition. The id is inlined rather
                // than bound: the two UNION branches would otherwise need the same
                // bind twice, which is not portable across the drivers this
                // platform runs on. It is an int, so nothing can be injected.
                List<string> sources = new List<string>();
                if (onHeader)
                {
                    sources.Add(@"SELECT r." + idColumn + @" AS REF_ID
                                    FROM M_Requisition r
                                   WHERE r.M_Requisition_ID = " + M_Requisition_ID + @"
                                     AND COALESCE(r." + idColumn + @", 0) > 0");
                }
                if (onLine)
                {
                    sources.Add(@"SELECT rl." + idColumn + @" AS REF_ID
                                    FROM M_RequisitionLine rl
                                   WHERE rl.M_Requisition_ID = " + M_Requisition_ID + @"
                                     AND rl.IsActive = 'Y'
                                     AND COALESCE(rl." + idColumn + @", 0) > 0");
                }

                string sql = @"SELECT src." + idColumn + @" AS REF_ID,
                                      " + labelExpr + @" AS DocumentNo
                                 FROM " + tableName + @" src
                                WHERE src." + idColumn + @" IN ("
                                 + string.Join(" UNION ", sources.ToArray()) + @")
                                ORDER BY 2, 1";
                DataSet ds = DB.ExecuteDataset(sql, null, null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return;

                DataRow r2 = ds.Tables[0].Rows[0];
                recordId   = Util.GetValueOfInt(r2["REF_ID"]);
                documentNo = Util.GetValueOfString(r2["DocumentNo"]);
                // No "#id" fallback. An internal key is not a document number, and
                // printing one tells the reader nothing they can quote to anyone.
                // The id still travels — the panel draws the chip on THAT, with its
                // label alone, and the chip still opens the record.
                count      = ds.Tables[0].Rows.Count;
            }
            catch (Exception ex)
            {
                _log.Severe("LoadVA075Reference (" + tableName + ", M_Requisition_ID="
                            + M_Requisition_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// The VA075 field service request the requisition was raised for
        /// (M_Requisition.VA075_FieldServiceReq_ID). Read exactly as the work order
        /// is — see <see cref="LoadVA075Reference"/>.
        /// </summary>
        private void LoadVA075FieldServiceReqOrigin(int M_Requisition_ID, RequisitionOverviewData d)
        {
            int id; string docNo; int count;
            LoadVA075Reference(M_Requisition_ID, "VA075_FieldServiceReq_ID", "VA075_FieldServiceReq",
                new string[] { "DocumentNo", "Name", "Value", "VA075_SRNo" },
                out id, out docNo, out count);

            d.VA075_FieldServiceReq_ID = id;
            d.FieldServiceReqNo        = docNo;
            d.FieldServiceReqCount     = count;
        }

        /// <summary>
        /// Flags a requisition raised by the Replenish Report process.
        ///
        /// That process stamps no id on the requisition — there is no
        /// "replenishment document" to point at — so the only marker it leaves is
        /// the description it writes: Msg.GetMsg(ctx, "Replenishment"). Resolving
        /// the same message here rather than comparing against the English word
        /// keeps the match working on a client running in another language, since
        /// both sides come from the same dictionary entry. The English literal is
        /// still accepted as a fallback for a requisition created before the
        /// message was translated.
        /// </summary>
        private void LoadReplenishmentOrigin(Ctx ctx, RequisitionOverviewData d)
        {
            string description = (d.Description ?? "").Trim();
            if (description.Length == 0) return;

            try
            {
                string marker = Msg.GetMsg(ctx, "Replenishment");
                if (!string.IsNullOrEmpty(marker) &&
                    description.Equals(marker.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    d.IsReplenishment = true;
                    return;
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadReplenishmentOrigin: " + ex.Message);
            }

            if (description.Equals("Replenishment", StringComparison.OrdinalIgnoreCase))
                d.IsReplenishment = true;
        }

        /// <summary>
        /// The project the requisition's lines were copied from (Copy From Project
        /// Line writes M_RequisitionLine.C_Project_ID / C_ProjectLine_ID). Both are
        /// optional columns, so each is checked before it is read; the project is
        /// reached through whichever of the two this schema has.
        ///
        /// Several projects can feed one requisition. The first is named and the
        /// rest counted, exactly as the Purchase Order overview counts its own
        /// multi-origin chips.
        /// </summary>
        private void LoadProjectOrigin(int M_Requisition_ID, RequisitionOverviewData d)
        {
            bool hasProject     = ColumnExists("M_RequisitionLine", "C_Project_ID");
            bool hasProjectLine = ColumnExists("M_RequisitionLine", "C_ProjectLine_ID");
            if (!hasProject && !hasProjectLine) return;

            try
            {
                // The project id, from the line itself when it carries one and
                // otherwise from the project line it was copied from.
                string idExpr;
                string joinExpr = "";
                if (hasProject && hasProjectLine)
                {
                    idExpr = "COALESCE(NULLIF(rl.C_Project_ID, 0), pl.C_Project_ID)";
                    joinExpr = @"LEFT OUTER JOIN C_ProjectLine pl
                                        ON (pl.C_ProjectLine_ID = rl.C_ProjectLine_ID)";
                }
                else if (hasProject)
                {
                    idExpr = "NULLIF(rl.C_Project_ID, 0)";
                }
                else
                {
                    idExpr = "pl.C_Project_ID";
                    joinExpr = @"INNER JOIN C_ProjectLine pl
                                        ON (pl.C_ProjectLine_ID = rl.C_ProjectLine_ID)";
                }

                string sql = @"SELECT DISTINCT p.C_Project_ID, p.Value AS ProjectNo, p.Name AS ProjectName
                                 FROM M_RequisitionLine rl
                                 " + joinExpr + @"
                                INNER JOIN C_Project p ON (p.C_Project_ID = " + idExpr + @")
                                WHERE rl.M_Requisition_ID = @M_Requisition_ID
                                  AND rl.IsActive = 'Y'
                                ORDER BY p.Value";
                DataSet ds = DB.ExecuteDataset(sql, ReqParam(M_Requisition_ID), null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return;

                DataRow r = ds.Tables[0].Rows[0];
                d.ProjectId    = Util.GetValueOfInt(r["C_Project_ID"]);
                d.ProjectNo    = Util.GetValueOfString(r["ProjectNo"]);
                d.ProjectName  = Util.GetValueOfString(r["ProjectName"]);
                d.ProjectCount = ds.Tables[0].Rows.Count;
            }
            catch (Exception ex)
            {
                _log.Severe("LoadProjectOrigin (M_Requisition_ID=" + M_Requisition_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// The VA075 field-service work order the requisition was raised for. The
        /// link is stamped on the header or on the lines depending on how the
        /// module raises it, so both are probed and whichever exists is read.
        ///
        /// VA075 is not part of this solution, so nothing here may reference its
        /// model classes: the table is read with plain SQL, and every column —
        /// including the work order's own identifier — is checked against the
        /// dictionary first. Without the module this is a few dictionary reads and
        /// no chip. Follows VAS_102_OverviewInternalUse.
        /// </summary>
        private void LoadVA075WorkOrderOrigin(int M_Requisition_ID, RequisitionOverviewData d)
        {
            int id; string docNo; int count;
            LoadVA075Reference(M_Requisition_ID, "VA075_WorkOrder_ID", "VA075_WorkOrder",
                new string[] { "DocumentNo", "Name", "Value" },
                out id, out docNo, out count);

            d.VA075_WorkOrder_ID = id;
            d.WorkOrderNo        = docNo;
            d.WorkOrderCount     = count;
        }

        /// <summary>
        /// The manufacturing work order (VAMFG_M_WorkOrder) the requisition was
        /// raised to feed — a DIFFERENT document to the VA075 service work order
        /// above, which is why it gets its own chip rather than sharing one.
        /// Column-guarded on the header and the line the same way.
        /// </summary>
        private void LoadProductionOrderOrigin(int M_Requisition_ID, RequisitionOverviewData d)
        {
            // The table's key column is the proof the module is installed at all.
            if (!ColumnExists("VAMFG_M_WorkOrder", "VAMFG_M_WorkOrder_ID")) return;

            bool onHeader = ColumnExists("M_Requisition", "VAMFG_M_WorkOrder_ID");
            bool onLine   = ColumnExists("M_RequisitionLine", "VAMFG_M_WorkOrder_ID");
            // A line raised for one COMPONENT of a production order carries the
            // component rather than the order; the order is one join further on.
            bool onComponent = ColumnExists("M_RequisitionLine", "VAMFG_M_WorkOrderComponent_ID")
                            && ColumnExists("VAMFG_M_WorkOrderComponent", "VAMFG_M_WorkOrder_ID");
            if (!onHeader && !onLine && !onComponent) return;

            // Probed, not assumed: VAMFG is not part of this solution either.
            string labelCol = FirstExistingColumn("VAMFG_M_WorkOrder",
                new string[] { "DocumentNo", "Name", "Value" });
            string labelExpr = string.IsNullOrEmpty(labelCol)
                ? "CAST(NULL AS VARCHAR(255))" : "wo." + labelCol;

            try
            {
                List<string> sources = new List<string>();
                if (onHeader)
                {
                    sources.Add(@"SELECT r.VAMFG_M_WorkOrder_ID AS WO_ID
                                    FROM M_Requisition r
                                   WHERE r.M_Requisition_ID = " + M_Requisition_ID + @"
                                     AND COALESCE(r.VAMFG_M_WorkOrder_ID, 0) > 0");
                }
                if (onLine)
                {
                    sources.Add(@"SELECT rl.VAMFG_M_WorkOrder_ID AS WO_ID
                                    FROM M_RequisitionLine rl
                                   WHERE rl.M_Requisition_ID = " + M_Requisition_ID + @"
                                     AND rl.IsActive = 'Y'
                                     AND COALESCE(rl.VAMFG_M_WorkOrder_ID, 0) > 0");
                }
                if (onComponent)
                {
                    sources.Add(@"SELECT c.VAMFG_M_WorkOrder_ID AS WO_ID
                                    FROM M_RequisitionLine rl
                                   INNER JOIN VAMFG_M_WorkOrderComponent c
                                           ON (c.VAMFG_M_WorkOrderComponent_ID
                                               = rl.VAMFG_M_WorkOrderComponent_ID)
                                   WHERE rl.M_Requisition_ID = " + M_Requisition_ID + @"
                                     AND rl.IsActive = 'Y'
                                     AND COALESCE(rl.VAMFG_M_WorkOrderComponent_ID, 0) > 0");
                }

                string sql = @"SELECT wo.VAMFG_M_WorkOrder_ID,
                                      " + labelExpr + @" AS DocumentNo
                                 FROM VAMFG_M_WorkOrder wo
                                WHERE wo.VAMFG_M_WorkOrder_ID IN ("
                                 + string.Join(" UNION ", sources.ToArray()) + @")
                                ORDER BY 2, 1";
                DataSet ds = DB.ExecuteDataset(sql, null, null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return;

                DataRow r = ds.Tables[0].Rows[0];
                d.VAMFG_M_WorkOrder_ID   = Util.GetValueOfInt(r["VAMFG_M_WorkOrder_ID"]);
                d.ProductionOrderNo      = Util.GetValueOfString(r["DocumentNo"]);
                // No "#id" fallback — see LoadVA075Reference. The chip is drawn on
                // the id, not on the number.
                d.ProductionOrderCount   = ds.Tables[0].Rows.Count;
            }
            catch (Exception ex)
            {
                _log.Severe("LoadProductionOrderOrigin (M_Requisition_ID=" + M_Requisition_ID + "): " + ex.Message);
            }
        }

        // ----------------------------------------------------------------- //
        //  Documents raised from the requisition                             //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// Every document raised FROM this requisition, for the panel's Documents
        /// section: the purchase orders its lines were converted into, the RFQs
        /// issued against it, and the material transfers that fulfil it. Each row
        /// carries the table + record id the panel opens it with, so the section
        /// navigates exactly as the Purchase Order overview's does.
        ///
        /// Each source is loaded independently and swallows its own failure, so an
        /// absent module (material transfer belongs to DTD001) or a schema without
        /// the linking column drops only its own rows.
        /// </summary>
        /// <param name="M_Requisition_ID">Selected requisition id.</param>
        /// <returns>The documents, oldest first within each kind; never null.</returns>
        private List<RequisitionDocumentData> LoadDocuments(int M_Requisition_ID)
        {
            List<RequisitionDocumentData> docs = new List<RequisitionDocumentData>();
            LoadOrderDocuments(M_Requisition_ID, docs);
            LoadRfqDocuments(M_Requisition_ID, docs);
            LoadMovementDocuments(M_Requisition_ID, docs);
            LoadInternalUseDocuments(M_Requisition_ID, docs);
            return docs;
        }

        /// <summary>
        /// The purchase orders this requisition was converted into, reached the
        /// same way the milestones are: M_RequisitionLine.C_OrderLine_ID, which is
        /// what RequisitionPOCreate writes back. One row per ORDER, carrying how
        /// many of this requisition's lines went onto it.
        /// </summary>
        private void LoadOrderDocuments(int M_Requisition_ID, List<RequisitionDocumentData> docs)
        {
            try
            {
                // A BLANKET order raised from the requisition is reported as one,
                // not as an ordinary purchase order: it commits a quantity over a
                // period rather than ordering goods, and its own screen is a
                // different window. C_Order.IsBlanketTrx is what the platform sets
                // for it, guarded because it is not on every schema's C_Order —
                // where it is absent every row simply reads as a purchase order,
                // exactly as before.
                bool hasBlanket = ColumnExists("C_Order", "IsBlanketTrx");
                string blanketSel = hasBlanket ? "COALESCE(o.IsBlanketTrx, 'N')" : "'N'";

                string sql = @"SELECT o.C_Order_ID,
                                      o.DocumentNo,
                                      o.DateOrdered,
                                      o.DocStatus,
                                      o.GrandTotal,
                                      " + blanketSel + @" AS IsBlanket,
                                      COUNT(rl.M_RequisitionLine_ID) AS LineCount
                                 FROM M_RequisitionLine rl
                                INNER JOIN C_OrderLine ol ON (ol.C_OrderLine_ID = rl.C_OrderLine_ID)
                                INNER JOIN C_Order o      ON (o.C_Order_ID      = ol.C_Order_ID)
                                WHERE rl.M_Requisition_ID = @M_Requisition_ID
                                  AND rl.IsActive = 'Y'
                                GROUP BY o.C_Order_ID, o.DocumentNo, o.DateOrdered,
                                         o.DocStatus, o.GrandTotal, " + blanketSel + @"
                                ORDER BY o.DateOrdered, o.DocumentNo";
                DataSet ds = DB.ExecuteDataset(sql, ReqParam(M_Requisition_ID), null);
                if (ds == null || ds.Tables.Count == 0) return;

                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    bool isBlanket = Util.GetValueOfString(r["IsBlanket"]) == "Y";
                    docs.Add(new RequisitionDocumentData
                    {
                        // The type is what the panel labels the row by and what
                        // names the window it opens, so a blanket carries its own.
                        Type       = isBlanket ? "blanket" : "order",
                        DocumentNo = Util.GetValueOfString(r["DocumentNo"]),
                        DocDate    = Util.GetValueOfDateTime(r["DateOrdered"]),
                        DocStatus  = Util.GetValueOfString(r["DocStatus"]),
                        Amount     = Util.GetValueOfDecimal(r["GrandTotal"]),
                        LineCount  = Util.GetValueOfInt(r["LineCount"]),
                        TableName  = "C_Order",
                        RecordId   = Util.GetValueOfInt(r["C_Order_ID"])
                    });
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadOrderDocuments (M_Requisition_ID=" + M_Requisition_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// The RFQs issued from this requisition (C_RfQ.M_Requisition_ID, written
        /// by CreateRFQFromRequisition), each carrying its own total (C_RfQ.TotalAmt).
        ///
        /// That column is dictionary-guarded: it is not on every C_RfQ revision, and
        /// where it is absent the row reports NO amount rather than a zero — a
        /// blank cell says "this schema does not record one", where "0.00" would be
        /// read as an RFQ worth nothing.
        /// </summary>
        private void LoadRfqDocuments(int M_Requisition_ID, List<RequisitionDocumentData> docs)
        {
            if (!ColumnExists("C_RfQ", "M_Requisition_ID")) return;

            bool hasTotal = ColumnExists("C_RfQ", "TotalAmt");
            string totalSel = hasTotal ? "q.TotalAmt" : "CAST(NULL AS DECIMAL(20,4))";

            try
            {
                string sql = @"SELECT q.C_RfQ_ID,
                                      q.DocumentNo,
                                      q.DateResponse,
                                      q.DocStatus,
                                      " + totalSel + @" AS TotalAmt,
                                      (SELECT COUNT(*)
                                         FROM C_RfQLine ql
                                        WHERE ql.C_RfQ_ID  = q.C_RfQ_ID
                                          AND ql.IsActive  = 'Y') AS LineCount
                                 FROM C_RfQ q
                                WHERE q.M_Requisition_ID = @M_Requisition_ID
                                  AND q.IsActive = 'Y'
                                ORDER BY q.DocumentNo";
                DataSet ds = DB.ExecuteDataset(sql, ReqParam(M_Requisition_ID), null);
                if (ds == null || ds.Tables.Count == 0) return;

                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    docs.Add(new RequisitionDocumentData
                    {
                        Type       = "rfq",
                        DocumentNo = Util.GetValueOfString(r["DocumentNo"]),
                        DocDate    = Util.GetValueOfDateTime(r["DateResponse"]),
                        DocStatus  = Util.GetValueOfString(r["DocStatus"]),
                        Amount     = hasTotal ? (decimal?)Util.GetValueOfDecimal(r["TotalAmt"]) : null,
                        LineCount  = Util.GetValueOfInt(r["LineCount"]),
                        TableName  = "C_RfQ",
                        RecordId   = Util.GetValueOfInt(r["C_RfQ_ID"])
                    });
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadRfqDocuments (M_Requisition_ID=" + M_Requisition_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// The material transfers fulfilling this requisition, through
        /// M_MovementLine.M_RequisitionLine_ID. That column belongs to the DTD001
        /// distribution module, which is not part of this solution, so it is
        /// dictionary-guarded: without it the section simply lists no transfers.
        /// Each row carries the transfer's total value, the same figure the
        /// Material Transfer screen shows for it.
        /// </summary>
        private void LoadMovementDocuments(int M_Requisition_ID, List<RequisitionDocumentData> docs)
        {
            if (!ColumnExists("M_MovementLine", "M_RequisitionLine_ID")) return;

            try
            {
                // A movement has no stored total of its own, so its value is summed
                // from the WHOLE transfer's lines — every line, not only the ones
                // this requisition raised, because the figure names what that
                // document is worth, which is what the transfer screen shows.
                //
                // The rate is exactly the one VAS_103 values a transfer line at:
                // the line's own cost price, falling back to the VA024 unit price,
                // and 0 where the schema carries neither. Reporting the same
                // arithmetic is the point — the two screens are talking about the
                // same document, and this row used to say nothing at all.
                string rateExpr = BuildMovementRateExpr();

                // The value is read OUTSIDE the aggregate rather than beside it.
                // Oracle rejects a subquery expression in GROUP BY (ORA-22818), so
                // grouping by this sub-select failed the statement outright — the
                // catch below swallowed it and the Documents section listed no
                // material transfers at all.
                string sql = @"SELECT x.M_Movement_ID,
                                      x.DocumentNo,
                                      x.MovementDate,
                                      x.DocStatus,
                                      x.LineCount,
                                      (SELECT NVL(SUM(NVL(ml2.MovementQty, 0) * " + rateExpr + @"), 0)
                                         FROM M_MovementLine ml2
                                        WHERE ml2.M_Movement_ID = x.M_Movement_ID
                                          AND ml2.IsActive      = 'Y') AS MovementValue
                                 FROM (SELECT m.M_Movement_ID,
                                              m.DocumentNo,
                                              m.MovementDate,
                                              m.DocStatus,
                                              COUNT(ml.M_MovementLine_ID) AS LineCount
                                         FROM M_MovementLine ml
                                        INNER JOIN M_Movement m
                                                ON (m.M_Movement_ID = ml.M_Movement_ID)
                                        INNER JOIN M_RequisitionLine rl
                                                ON (rl.M_RequisitionLine_ID = ml.M_RequisitionLine_ID)
                                        WHERE rl.M_Requisition_ID = @M_Requisition_ID
                                          AND ml.IsActive = 'Y'
                                          AND rl.IsActive = 'Y'
                                        GROUP BY m.M_Movement_ID, m.DocumentNo,
                                                 m.MovementDate, m.DocStatus) x
                                ORDER BY x.MovementDate, x.DocumentNo";
                DataSet ds = DB.ExecuteDataset(sql, ReqParam(M_Requisition_ID), null);
                if (ds == null || ds.Tables.Count == 0) return;

                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    docs.Add(new RequisitionDocumentData
                    {
                        Type       = "movement",
                        DocumentNo = Util.GetValueOfString(r["DocumentNo"]),
                        DocDate    = Util.GetValueOfDateTime(r["MovementDate"]),
                        DocStatus  = Util.GetValueOfString(r["DocStatus"]),
                        Amount     = Util.GetValueOfDecimal(r["MovementValue"]),
                        LineCount  = Util.GetValueOfInt(r["LineCount"]),
                        TableName  = "M_Movement",
                        RecordId   = Util.GetValueOfInt(r["M_Movement_ID"])
                    });
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadMovementDocuments (M_Requisition_ID=" + M_Requisition_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// The Inventory Use issues raised against this requisition, through
        /// M_InventoryLine.M_RequisitionLine_ID — the column the internal-use
        /// screen writes when a line is picked from a requisition.
        ///
        /// Restricted to INTERNAL USE documents (M_Inventory.IsInternalUse = 'Y').
        /// A physical inventory count shares the same tables and would otherwise be
        /// listed here as though the requisition had produced one.
        ///
        /// The link column belongs to the distribution module, so it is
        /// dictionary-guarded: without it the section simply lists no issues.
        ///
        /// Each row carries the issue's total value, the same figure the Inventory
        /// Use screen shows for it.
        /// </summary>
        private void LoadInternalUseDocuments(int M_Requisition_ID, List<RequisitionDocumentData> docs)
        {
            if (!ColumnExists("M_InventoryLine", "M_RequisitionLine_ID")) return;

            try
            {
                // The issue's value, over the WHOLE document's lines — every line,
                // not only the ones this requisition raised, because the figure
                // names what that document is worth.
                //
                // It is VAS_102's own arithmetic: the issued quantity
                // (QtyInternalUse) at the rate that panel resolves for the line —
                // the cost BOOKED against it, else the product's cost at the
                // issue's warehouse, else its price columns (BuildInventoryRateExpr).
                // The row used to report NOTHING, on the argument that an issue's
                // value is a costing question the document does not answer — but its
                // own screen answers it, and a dash here against a figure there is
                // just the two screens disagreeing.
                //
                // Read outside the aggregate, over a derived table: Oracle rejects a
                // subquery expression in GROUP BY (ORA-22818).
                string rateExpr = BuildInventoryRateExpr();

                string sql = @"SELECT x.M_Inventory_ID,
                                      x.DocumentNo,
                                      x.MovementDate,
                                      x.DocStatus,
                                      x.LineCount,
                                      (SELECT NVL(SUM(NVL(il2.QtyInternalUse, 0) * " + rateExpr + @"), 0)
                                         FROM M_InventoryLine il2
                                        INNER JOIN M_Inventory inv2
                                                ON (inv2.M_Inventory_ID = il2.M_Inventory_ID)
                                        WHERE il2.M_Inventory_ID = x.M_Inventory_ID
                                          AND il2.IsActive       = 'Y') AS IssueValue
                                 FROM (SELECT inv.M_Inventory_ID,
                                              inv.DocumentNo,
                                              inv.MovementDate,
                                              inv.DocStatus,
                                              COUNT(il.M_InventoryLine_ID) AS LineCount
                                         FROM M_InventoryLine il
                                        INNER JOIN M_Inventory inv
                                                ON (inv.M_Inventory_ID = il.M_Inventory_ID)
                                        INNER JOIN M_RequisitionLine rl
                                                ON (rl.M_RequisitionLine_ID = il.M_RequisitionLine_ID)
                                        WHERE rl.M_Requisition_ID = @M_Requisition_ID
                                          AND il.IsActive  = 'Y'
                                          AND rl.IsActive  = 'Y'
                                          AND inv.IsActive = 'Y'
                                          AND COALESCE(inv.IsInternalUse, 'N') = 'Y'
                                        GROUP BY inv.M_Inventory_ID, inv.DocumentNo,
                                                 inv.MovementDate, inv.DocStatus) x
                                ORDER BY x.MovementDate, x.DocumentNo";
                DataSet ds = DB.ExecuteDataset(sql, ReqParam(M_Requisition_ID), null);
                if (ds == null || ds.Tables.Count == 0) return;

                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    docs.Add(new RequisitionDocumentData
                    {
                        Type       = "internaluse",
                        DocumentNo = Util.GetValueOfString(r["DocumentNo"]),
                        DocDate    = Util.GetValueOfDateTime(r["MovementDate"]),
                        DocStatus  = Util.GetValueOfString(r["DocStatus"]),
                        Amount     = Util.GetValueOfDecimal(r["IssueValue"]),
                        LineCount  = Util.GetValueOfInt(r["LineCount"]),
                        TableName  = "M_Inventory",
                        RecordId   = Util.GetValueOfInt(r["M_Inventory_ID"])
                    });
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadInternalUseDocuments (M_Requisition_ID=" + M_Requisition_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// The unit-rate expression an inventory-use line is valued at, over an
        /// M_InventoryLine aliased "il2" whose document is aliased "inv2". Four
        /// sources, in the order VAS_102's own panel resolves them:
        ///
        ///   1. the cost BOOKED against the line — Amt / Qty of the newest
        ///      M_CostDetail row written for it;
        ///   2. else the product's latest cost at the issue's warehouse;
        ///   3. else the line's own CurrentCostPrice;
        ///   4. else the VA024 unit price; else 0.
        ///
        /// Steps 1 and 2 are what this expression was missing, and they are the
        /// only two that answer for a COMPLETED issue. CurrentCostPrice and
        /// VA024_UnitPrice are optional module columns that a great many issues
        /// never carry, so a completed inventory-use document whose value lives
        /// entirely in M_CostDetail reported 0 here while its own screen showed the
        /// full figure — and an issue where only some lines carried a price column
        /// reported part of it, which is worse, because a partial total does not
        /// look wrong. The comment this replaces claimed to be "VAS_102's
        /// arithmetic, column for column"; that was true when it was written, and
        /// stopped being true when VAS_102 moved to M_CostDetail.
        ///
        /// Each cost lookup is wrapped in NULLIF(..., 0) so a zero-valued cost row
        /// falls through to the next source rather than pinning the rate at zero —
        /// VAS_102 makes the same test in C# (costRate != 0).
        ///
        /// Every branch is dictionary-guarded, so a schema missing any of these
        /// columns simply resolves further down the list.
        /// </summary>
        private string BuildInventoryRateExpr()
        {
            List<string> cols = new List<string>();

            // 1. The cost actually booked against this issue line.
            if (ColumnExists("M_CostDetail", "M_InventoryLine_ID"))
            {
                // The newest cost detail for the line, not a SUM: costs are written
                // per accounting schema and cost element, so summing them would
                // multiply the rate by however many of each the client keeps.
                cols.Add(@"NULLIF((SELECT ABS(cd.Amt / cd.Qty)
                                     FROM M_CostDetail cd
                                    WHERE cd.M_CostDetail_ID =
                                          (SELECT MAX(cd2.M_CostDetail_ID)
                                             FROM M_CostDetail cd2
                                            WHERE cd2.M_InventoryLine_ID = il2.M_InventoryLine_ID
                                              AND cd2.IsActive           = 'Y'
                                              AND COALESCE(cd2.Qty, 0)  <> 0)), 0)");

                // 2. Else the product's latest cost at the issue's own warehouse.
                //    This is what values a DRAFT issue, which has booked no cost of
                //    its own yet. Skipped where the schema keeps no warehouse on
                //    M_CostDetail (a costing level below warehouse).
                if (ColumnExists("M_CostDetail", "M_Warehouse_ID"))
                {
                    cols.Add(@"NULLIF((SELECT ABS(cd3.Amt / cd3.Qty)
                                         FROM M_CostDetail cd3
                                        WHERE cd3.M_CostDetail_ID =
                                              (SELECT MAX(cd4.M_CostDetail_ID)
                                                 FROM M_CostDetail cd4
                                                WHERE cd4.M_Product_ID    = il2.M_Product_ID
                                                  AND cd4.M_Warehouse_ID  = inv2.M_Warehouse_ID
                                                  AND cd4.IsActive        = 'Y'
                                                  AND COALESCE(cd4.Qty, 0) <> 0)), 0)");
                }
            }

            // 3 / 4. The line's own price columns, the last resort.
            if (ColumnExists("M_InventoryLine", "CurrentCostPrice")) cols.Add("il2.CurrentCostPrice");
            if (ColumnExists("M_InventoryLine", "VA024_UnitPrice"))  cols.Add("il2.VA024_UnitPrice");
            if (cols.Count == 0) return "0";
            return "COALESCE(" + string.Join(", ", cols.ToArray()) + ", 0)";
        }

        /// <summary>
        /// The unit-rate expression a material-transfer line is valued at, over an
        /// M_MovementLine aliased "ml2": the line's own cost price, else the VA024
        /// unit price. Both are optional module columns, so the expression is built
        /// from whichever the schema has and collapses to a constant 0 when it has
        /// neither.
        ///
        /// This is VAS_103's BuildRateExpr, column for column and fallback for
        /// fallback, so a transfer listed here is worth what the Material Transfer
        /// screen says it is worth. It used to report NOTHING on a schema that
        /// prices neither way — on the argument that a blank cell is honester than
        /// a zero — which left the amount reading as a dash while the transfer's
        /// own screen showed a figure beside it.
        /// </summary>
        private string BuildMovementRateExpr()
        {
            List<string> cols = new List<string>();
            if (ColumnExists("M_MovementLine", "CurrentCostPrice")) cols.Add("ml2.CurrentCostPrice");
            if (ColumnExists("M_MovementLine", "VA024_UnitPrice"))  cols.Add("ml2.VA024_UnitPrice");
            if (cols.Count == 0) return "0";
            return "COALESCE(" + string.Join(", ", cols.ToArray()) + ", 0)";
        }

        /// <summary>
        /// Resolves a window's AD_Window_ID from its name (AD_Window.Name) for the
        /// panel's record-open path. A record whose screen is not the table's
        /// default zoom target is opened by naming its window instead, and the name
        /// is only ever turned into an id here, against the dictionary.
        ///
        /// Restricted to windows this tenant can see (AD_Client_ID 0 or its own),
        /// preferring the tenant's own row over the system one, and to windows the
        /// ROLE may actually open. Ported from VAS_092.
        ///
        /// The role test is the one thing this does that VAS_092's does not. A
        /// window the role cannot see raises when the framework starts it, and a
        /// chip is better drawn as plain text than as a link that reports an error
        /// — which is the whole reason this panel resolves its windows up front.
        /// Every candidate is offered, so a system window the role is shut out of
        /// does not hide the tenant's own copy of it.
        /// </summary>
        /// <param name="ctx">User context (client + role).</param>
        /// <param name="windowName">Window name to resolve.</param>
        /// <returns>The window id, or 0 when the name resolves to nothing the
        /// caller can open.</returns>
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
                if (ds == null || ds.Tables.Count == 0) return 0;

                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    int id = Util.GetValueOfInt(r["AD_Window_ID"]);
                    if (id > 0 && HasWindowAccess(ctx, id)) return id;
                }
                return 0;
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
        /// in the client's map — a module that ships its own window and is not part
        /// of this solution (VA075) has no name that can be hard-coded here, and
        /// the browser-side zoom lookup only knows tables the client has cached. So
        /// the work order and field service request chips resolve through this, or
        /// they do not open at all. Reading the dictionary works for any installed
        /// module. Ported from VAS_102.
        ///
        /// Each statement carries a single bind name, occurring once: positional
        /// binding gives a repeated name a second, unfilled placeholder.
        /// </summary>
        /// <param name="ctx">User context — the role decides whether the window it
        /// resolves may be opened at all.</param>
        /// <param name="tableName">Physical table name, e.g. "VA075_WorkOrder".</param>
        /// <returns>The window id, or 0 when the table has no window the caller can
        /// open the record in.</returns>
        public int GetWindowIdByTable(Ctx ctx, string tableName)
        {
            if (string.IsNullOrEmpty(tableName)) return 0;
            string name = tableName.Trim();
            try
            {
                // The table's own zoom target, ACCEPTED ONLY when that window's
                // first tab sits on this table.
                //
                // The panel starts the window with an equal-query on the table's key
                // column, and the framework runs that query against the FIRST tab —
                // so a zoom target whose first tab is some other table is opened
                // with a column it does not have and raises on screen. That is what
                // a click on the field service request and the production order
                // chips reported: not "cannot open", but an error out of a window
                // asked a question it could not answer. The test below used to guard
                // the fallback query only, which is why the zoom target still got
                // through.
                string sql = @"SELECT t.AD_Window_ID
                                 FROM AD_Table t
                                WHERE UPPER(t.TableName) = UPPER(@TableName)
                                  AND t.IsActive         = 'Y'
                                  AND COALESCE(t.AD_Window_ID, 0) > 0
                                  AND EXISTS (SELECT 1
                                                FROM AD_Tab tb
                                               WHERE tb.AD_Window_ID = t.AD_Window_ID
                                                 AND tb.AD_Table_ID  = t.AD_Table_ID
                                                 AND tb.IsActive     = 'Y'
                                                 AND tb.SeqNo = (SELECT MIN(tb2.SeqNo)
                                                                   FROM AD_Tab tb2
                                                                  WHERE tb2.AD_Window_ID = t.AD_Window_ID
                                                                    AND tb2.IsActive     = 'Y'))";
                DataSet ds = DB.ExecuteDataset(
                    sql, new SqlParameter[] { new SqlParameter("@TableName", name) }, null);
                if (ds != null && ds.Tables.Count > 0)
                {
                    foreach (DataRow r in ds.Tables[0].Rows)
                    {
                        int id = Util.GetValueOfInt(r["AD_Window_ID"]);
                        if (id > 0 && HasWindowAccess(ctx, id)) return id;
                    }
                }

                // No usable zoom target on the table itself — take the window whose
                // FIRST tab sits on this table, which is the screen that maintains
                // it. Every candidate is offered, so one the role cannot open does
                // not shut out the next.
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
                if (ds == null || ds.Tables.Count == 0) return 0;

                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    int id = Util.GetValueOfInt(r["AD_Window_ID"]);
                    if (id > 0 && HasWindowAccess(ctx, id)) return id;
                }
                return 0;
            }
            catch (Exception ex)
            {
                _log.Severe("GetWindowIdByTable (" + name + "): " + ex.Message);
                return 0;
            }
        }

        /// <summary>
        /// Whether the caller's role may open the window. A window the role cannot
        /// see raises when it is started, so a chip pointing at one is better left
        /// as plain text — which is what returning 0 from the resolvers above makes
        /// the panel do.
        ///
        /// A failure to ASK counts as access: this is a display decision, not a
        /// security boundary, and the platform enforces the real one when the
        /// window is started.
        /// </summary>
        /// <param name="ctx">User context (role).</param>
        /// <param name="AD_Window_ID">Window to test.</param>
        private bool HasWindowAccess(Ctx ctx, int AD_Window_ID)
        {
            if (ctx == null || AD_Window_ID <= 0) return false;
            try
            {
                return MRole.GetDefault(ctx, false).GetWindowAccess(AD_Window_ID) ?? false;
            }
            catch (Exception ex)
            {
                _log.Severe("HasWindowAccess (AD_Window_ID=" + AD_Window_ID + "): " + ex.Message);
                return true;
            }
        }

        // ----------------------------------------------------------------- //
        //  Data carriers                                                     //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// One document raised from the requisition — a purchase order, an RFQ or
        /// a material transfer. <see cref="TableName"/> + <see cref="RecordId"/>
        /// are what the panel opens the record with.
        /// </summary>
        public class RequisitionDocumentData
        {
            public string    Type       { get; set; }   // order | rfq | movement
            public string    DocumentNo { get; set; }
            public DateTime? DocDate    { get; set; }
            public string    DocStatus  { get; set; }
            // Null for a document this schema records no total for (an RFQ without
            // C_RfQ.TotalAmt, an inventory issue) — distinct from a genuine zero.
            // A material transfer is always valued, exactly as VAS_103 values it.
            public decimal?  Amount     { get; set; }
            public int       LineCount  { get; set; }
            public string    TableName  { get; set; }
            public int       RecordId   { get; set; }
        }

        public class RequisitionLineData
        {
            public int      M_RequisitionLine_ID { get; set; }
            public int      Line          { get; set; }
            // Quantity and price are both in the line's SELECTED unit (C_UOM_ID),
            // the one the row is labelled with. BaseQty / UomRatio carry the base
            // scale the database stores, for converting anything read from stock.
            public decimal  RequestedQty  { get; set; }
            public decimal  BaseQty       { get; set; }   // M_RequisitionLine.Qty
            public decimal  UomRatio      { get; set; }   // base units per selected unit
            public decimal  UnitPrice     { get; set; }
            public decimal  LineAmount    { get; set; }
            public string   Description   { get; set; }
            public int      M_Product_ID  { get; set; }
            public int      C_Charge_ID   { get; set; }
            public bool     IsConverted   { get; set; }   // line linked to an order line
            public string   ProductName   { get; set; }
            public string   ProductValue  { get; set; }   // product search key
            public string   CategoryName  { get; set; }
            public string   ChargeName    { get; set; }
            public string   UOMName       { get; set; }
            public int      UOMPrecision  { get; set; }
            public string   AttributeSetInstance { get; set; }   // blank when the line carries none

            // From the guarded enrichment pass (custom columns).
            public bool     HasSourceData { get; set; }   // reserved/ordered columns were available
            public decimal  ReservedQty   { get; set; }
            public decimal  OrderedQty    { get; set; }
            public decimal  AvailableBudget { get; set; }
            public bool     IsBudgetBreach { get; set; }
            public string   SourceState   { get; set; }   // full | short (when HasSourceData)
            public decimal  SourceQtyOnHand { get; set; } // M_Storage.QtyOnHand at the source / request warehouse
            // Booked in against this line on a COMPLETED goods receipt, in the
            // selected unit. 0 for a line nothing has been received against.
            public decimal  ReceivedQty   { get; set; }
        }

        public class ActivityData
        {
            // create | status | updated | comment | po | grn | grncomplete | email
            public string    Type       { get; set; }
            public string    Text       { get; set; }   // comment body, mail subject, or actor
            public string    UserName   { get; set; }
            public string    DocumentNo { get; set; }   // related PO / GRN, when any
            public DateTime? Created    { get; set; }

            // Field-level edit (AD_ChangeLog) — WHICH field changed and on which
            // record.
            /// <summary>The dictionary's label for the changed column.</summary>
            public string    FieldName   { get; set; }
            /// <summary>Which record the edit landed on: "" for the requisition
            /// header, else the line's number and product ("#10 Steel Bolt M8").
            /// A requisition's substantive edits are its requested quantities and
            /// products, and those live on the lines.</summary>
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

            // E-mail rows only (MailAttachment1). Body travels with the row so the
            // panel can reveal it on click without a second round trip.
            public string    Body       { get; set; }   // TextMsg (flattened to text)
            public string    MailTo     { get; set; }   // MailAddress
            public string    MailCc     { get; set; }
            public string    MailBcc    { get; set; }
            public string    MailFrom   { get; set; }
            public bool      IsMailSent { get; set; }

            /// <summary>The e-mails sent against an APPOINTMENT or TASK itself
            /// (MailAttachment1 anchored on AppointmentsInfo): recipient, subject,
            /// body, when and by whom. Distinct from the mail fields above, which
            /// are correspondence about the REQUISITION. Empty on every other
            /// type; the bodies travel with the row so the panel reveals them on
            /// click without a second round trip.</summary>
            public List<VAS_ActivityMailRow> Mails { get; set; }
        }

        public class RequisitionOverviewData
        {
            // Identity / header
            public int       M_Requisition_ID { get; set; }
            public string    DocumentNo    { get; set; }
            public string    StatusCode    { get; set; }   // DocStatus (mapped to label in JS)
            public string    PriorityCode  { get; set; }   // PriorityRule (mapped in JS)
            public DateTime? DateDoc       { get; set; }
            public DateTime? DateRequired  { get; set; }
            public string    Description   { get; set; }   // purpose / notes
            public bool      Processed     { get; set; }
            public bool      Posted        { get; set; }   // M_Requisition.Posted = 'Y'
            public string    PostedCode    { get; set; }   // raw posting status code
            public DateTime? Created       { get; set; }
            public DateTime? Updated       { get; set; }
            public DateTime? SystemDate    { get; set; }

            // People / route
            public string    RequesterName { get; set; }
            public string    PreparerName  { get; set; }
            public string    CreatedByName { get; set; }
            public string    UpdatedByName { get; set; }
            public int       RequestWarehouseId   { get; set; }   // M_Requisition.M_Warehouse_ID
            public string    RequestWarehouseName { get; set; }
            public int       SourceWarehouseId    { get; set; }   // DTD001_MWarehouseSource_ID
            public string    SourceWarehouseName  { get; set; }   // custom (guarded)
            // The warehouse the line stock figures were actually read at: the
            // source warehouse when there is one, else the request warehouse. The
            // panel names it on the column's tooltip so the figure is never
            // attributed to the wrong place.
            public string    StockWarehouseName   { get; set; }
            public string    PriceListName { get; set; }

            // Currency
            public string    CurSymbol     { get; set; }
            public string    ISO_Code      { get; set; }
            public int       StdPrecision  { get; set; }

            // Stats / totals
            public decimal   EstimatedValue    { get; set; }   // TotalLines (or Σ lines)
            public decimal   EstimatedSubtotal { get; set; }   // Σ line amounts
            public int       LineCount         { get; set; }
            public decimal   RequestedUnits    { get; set; }
            public decimal   ReservedUnits     { get; set; }
            public decimal   OrderedUnits      { get; set; }
            // Budget set for the requisition — M_RequisitionLine.VAS_AvailableBudget,
            // written by the platform's "Calculate Budget" process. Distinct amounts
            // only, so one budget shared across lines is not counted twice.
            // Falls back to the requisition-level budget (LoadBudgetAmount) when no
            // line carries one, which is the usual case.
            public decimal   AvailableBudget   { get; set; }   // custom (guarded)
            // True when the figure above came from that fallback: the whole budget
            // the requisition draws against, not one line control's balance.
            public bool      BudgetIsRequisitionLevel { get; set; }
            public bool      IsBudgetBreach    { get; set; }
            public decimal   BudgetOverage     { get; set; }   // estimated - available (when breach)
            public string    BudgetBreachNote  { get; set; }   // custom text (guarded)
            public bool      HasSourceData     { get; set; }   // a source warehouse is configured
            public int       FullyInStockLines { get; set; }
            public decimal   SourceStockOnHand { get; set; }   // Σ QtyOnHand at the source warehouse

            // Lifecycle
            public int       ConvertedLineCount { get; set; }
            public bool      IsConverted   { get; set; }
            public bool      HasOrdered    { get; set; }   // a linked PO reached Completed
            public bool      IsClosed      { get; set; }   // requisition CL, or every linked PO closed
            public int       OrderCount    { get; set; }   // purchase orders raised from this requisition
            public string    OrderDocumentNo { get; set; } // first of them, for display

            // Origin — the documents the requisition was generated FROM, for the
            // Reference chip strip. Every one of them is optional; a requisition
            // carrying none of them was raised by hand and the panel says so.
            public bool      IsReplenishment { get; set; }  // raised by Replenish Report
            public int       ProjectId       { get; set; }
            public string    ProjectNo       { get; set; }  // C_Project.Value
            public string    ProjectName     { get; set; }
            public int       ProjectCount    { get; set; }
            // The order whose demand the requisition serves
            // (M_RequisitionLine.Ref_OrderLine_ID). C_Order is dual-purpose, so the
            // side travels with it.
            public int       RefOrderId      { get; set; }
            public string    RefOrderNo      { get; set; }
            public bool      RefOrderIsSOTrx { get; set; }
            public int       RefOrderCount   { get; set; }
            // The BLANKET order behind that reference — the standing commitment the
            // requisition draws on, reached either directly or through the release
            // it points at (LoadBlanketOrderOrigin). Its own chip, and its own
            // screen: a blanket sales order does not open where a blanket purchase
            // order does, hence the side flag.
            public int       BlanketOrderId      { get; set; }
            public string    BlanketOrderNo      { get; set; }
            public bool      BlanketOrderIsSOTrx { get; set; }
            public int       BlanketOrderCount   { get; set; }
            /// <summary>The window the blanket order opens in — the blanket SALES
            /// or blanket PURCHASE screen as its side requires. Resolved by name
            /// only (no table fallback): C_Order's own window is the ordinary order
            /// screen, which does not carry a blanket. 0 means the chip is drawn as
            /// plain text rather than opening the wrong screen.</summary>
            public int       BlanketOrderWindowId { get; set; }
            public int       VA075_WorkOrder_ID { get; set; }
            public string    WorkOrderNo     { get; set; }  // VA075 maintenance work order
            public int       WorkOrderCount  { get; set; }
            public int       VA075_FieldServiceReq_ID { get; set; }
            public string    FieldServiceReqNo    { get; set; }  // VA075 field service request
            public int       FieldServiceReqCount { get; set; }
            public int       VAMFG_M_WorkOrder_ID { get; set; }
            public string    ProductionOrderNo    { get; set; }
            public int       ProductionOrderCount { get; set; }
            // The window each of the three module chips opens in, resolved on the
            // server because their modules are not part of this solution and their
            // screens cannot be named on the client (LoadOriginWindows). 0 means
            // there is none the role can open, and the chip is drawn as plain text
            // rather than as a link that would fail.
            public int       WorkOrderWindowId        { get; set; }
            public int       FieldServiceReqWindowId  { get; set; }
            public int       ProductionOrderWindowId  { get; set; }

            // Progress milestone dates
            public DateTime? PostedDate     { get; set; }   // earliest Fact_Acct row
            public DateTime? CompletedDate  { get; set; }
            public DateTime? ConvertedDate  { get; set; }
            public DateTime? FulfilmentDate { get; set; }
            public DateTime? ClosedDate     { get; set; }

            // Collections
            public List<RequisitionLineData>     Lines     { get; set; }
            public List<RequisitionDocumentData> Documents { get; set; }
            public List<ActivityData>            Activity  { get; set; }
        }
    }
}
