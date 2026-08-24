/************************************************************
 * Module Name    : VAS
 * Purpose        : Purchase Order Overview tab panel. Renders a
 *                  review-oriented overview of the selected purchase
 *                  order (C_Order, IsSoTrx = 'N'): identity, linked
 *                  origin docs, stat strip, 7-stage progress, line
 *                  items with received progress, line change history,
 *                  landed cost (per component + per-line distribution),
 *                  and a stacked Notes / Activity area. Data is fetched
 *                  from VAS_092_OverviewPurchaseOrder/GetPurchaseOrderOverview.
 * Chronological development:
 *   VAI163   2026-06-10  Created
 *   VAI163   2026-06-15  Added inline icons (address, contact, linked
 *                        chips, activity badge) and a Production Order
 *                        linked chip to match the reference design.
 *   VAI163   2026-06-17  Terms & Notes description value now read from
 *                        C_Order.POReference instead of C_Order.Description.
 *   VAI163   2026-06-17  Reworked the landed-cost section into a per-component
 *                        view: component name + source, distribution-method
 *                        tag, expected, actual (invoiced / awaiting), variance,
 *                        a totals footer and a methodology note.
 *   VAI163   2026-06-22  Redesigned to the canonical windows-and-panels.md
 *                        Right Panel Body language.
 *   VAI163   2026-07-01  Header reworked to a soft-gradient title strip above a
 *                        white two-column details card (vendor | terms).
 *   VAI163   2026-07-01  Snapshot metric grid + Generated From chip strip.
 *   VAI163   2026-07-01  Two-column Terms & Notes | Recent Activity footer.
 *   VAI163   2026-07-08  Generated From "Manual" fallback chip.
 *   VAI163   2026-07-17  - getMsg() fallbacks so missing AD_Message keys never
 *                          render raw (English defaults in MSG_DEFAULTS).
 *                        - Header mirrors the Sales Order overview: vendor left,
 *                          terms right (adds Pricelist); priority from the real
 *                          C_Order.PriorityRule field.
 *                        - Generated From now shows only origins that exist
 *                          (Sales Order / Requisition / Contract) as clickable
 *                          chips that open the source record.
 *                        - Order Total caption changed to "Inclusive Taxes".
 *                        - Stepper no longer shows "Partially Received" once the
 *                          order is fully received.
 *                        - New Line History section (C_OrderLineHistory).
 *                        - Landed cost now shows the per-line distribution
 *                          breakdown under each component.
 *                        - Notes & Activity stacked (Notes then Activity); Notes
 *                          sourced like the Sales Order overview.
 *   VAI163   2026-07-17  Completed stage now shows OrderCompletedDate (the real
 *                        DocComplete timestamp) instead of DateOrdered.
 *                      - New Documents section: the GRNs / vendor invoices
 *                        prepared from this PO, each row opening the underlying
 *                        document via the shared openRecord() zoom path.
 *   VAI163   2026-07-24  "Received: X of Y lines" now counts only deliverable
 *                        (stockable) lines, matching the corrected server-side
 *                        fully-received logic, so a PO with a freight / landed-
 *                        cost charge line no longer stays "Partially Received".
 *   VAI163   2026-07-27  - Line items drop the "SKU" prefix before the product
 *                          search key and show the unit of measure (UOM) next to
 *                          the ordered quantity.
 *                        - Line items show the Attribute Set Instance details
 *                          (size / lot / serial ...) when the product carries one.
 *                        - Timestamps (Created / activity / history / completion)
 *                          are parsed as UTC and rendered in the browser's local
 *                          zone; date-only fields keep their stored calendar day
 *                          without any zone shift (parseDbDate asUtc flag).
 *                        - Comments/chat notes render commenter name + timestamp
 *                          above the comment text.
 *                        - Priority Low (7) and Minor (9) now show a green badge,
 *                          driven straight from C_Order.PriorityRule.
 *                        - Order Progress "With Vendor" stage now shows the PO
 *                          completion date instead of the ordered date.
 *   VAI163   2026-07-27  - Line-item and line-history quantities now show the
 *                          entered-UOM quantity (QtyEntered) instead of the
 *                          base-UOM QtyOrdered.
 *                        - Long product / component names carry a title tooltip
 *                          so the full name is readable on hover when truncated.
 *   VAI163   2026-07-27  - Documents section now shows the GRN amount (total
 *                          received value), not just invoice amounts.
 *                        - Order Progress: a reached milestone renders green even
 *                          when it is the current stage, so Invoice Raised and
 *                          Payment Done turn green once the invoice / payment
 *                          exists (was staying orange / pending).
 *   VAI163   2026-07-27  - Activity feed: notes/chat now use the same symmetric
 *                          row layout as every other activity (tag | title |
 *                          right-aligned timestamp · author) instead of a
 *                          bespoke multi-line block.
 *                        - Order Progress Payment stage shows "Payment Completed"
 *                          when every invoice is fully paid, else "Pending Amount".
 *   VAI163   2026-07-27  - Documents section now lists linked AP Payments — coins
 *                          icon, "AP Payment · Discounted Amount: <DiscountAmt>"
 *                          sub-label, DateTrx / DocStatus / PayAmt, clicking the
 *                          number opens the AP Payment record. The activity feed's
 *                          payment entry now reads "AP Payment Created".
 *                        - A "Posted" badge renders beside the priority / status
 *                          pills when the document is posted (hidden otherwise).
 *                        - Line-items Subtotal now shows the net-of-tax amount
 *                          (SubTotal = GrandTotal − Tax) so a tax-inclusive price
 *                          list shows the correct subtotal and extracted tax.
 *   VAI163   2026-07-27  - The Sales Order origin chip now opens the Sales Order
 *                          window (openRecord passes IsSOTrx = true) instead of
 *                          wrongly opening the Purchase Order window.
 *                        - Line Items table paginates at 25 rows per page with a
 *                          prev / next footer pager.
 *                        - Line History is collapsed by default with a Show /
 *                          Hide Details toggle.
 *                        - New Budget section + header "Budget Breach" badge
 *                          surfacing the GL budget check (IsBudgetViolated,
 *                          MaxBudgetViolationAmount, per-line BudgetViolationAmount).
 *                        - Received card quantity now item-only (server side).
 *   VAI163   2026-07-27  - Line Attribute Set Instance sub-line is hidden when the
 *                          line has no real instance (blank / "--" placeholder).
 *   VAI163   2026-07-29  - A line's change history now sits under that line: an
 *                          edited line carries a History (n) chip that opens a
 *                          drawer directly beneath it, instead of the reader
 *                          matching line numbers against a separate table. The
 *                          drawer state survives paging and resets per record.
 *                          The standalone section is kept only for history whose
 *                          line was removed from the order ("Removed lines").
 *   VAI163   2026-07-29  - The history toggle moved to a trailing action column at
 *                          the right-hand end of the line row and is now icon-only,
 *                          with a tooltip / aria-label carrying the action and the
 *                          number of changes ("Show history (3)").
 *   VAI163   2026-07-29  - Recent Activity shows the e-mails sent against the
 *                          order (MailAttachment1): subject as the headline,
 *                          recipient beneath it, sender and time on the right, and
 *                          the message body revealed on click.
 *                        - Landed Cost columns explain themselves on hover; the
 *                          Actual cell names the invoice its figure came from and
 *                          the Variance cell spells out the direction.
 *   VAI163   2026-07-29  - E-mails also get their own Emails section above Recent
 *                          Activity: the complete correspondence, newest first,
 *                          where the activity feed only shows what survives its
 *                          cap. Same row shape and click-to-open body.
 *                        - Emails page client-side at 10 per page, with the pager
 *                          as the list card's footer. Which messages are open is
 *                          remembered per mail id, so paging away and back does
 *                          not fold one the reader had opened.
 *   VAI163   2026-07-30  - Removed the standalone Emails section: Recent Activity
 *                          already lists the same e-mails (type "email", same
 *                          subject / recipient row and click-to-open body), so the
 *                          panel was showing every message twice. E-mails stay in
 *                          the activity feed only; the server still loads them
 *                          (LoadEmails feeds the feed).
 *                        - Landed Cost now pages client-side at 10 components per
 *                          page, reusing the line-items pager. The totals footer
 *                          and the methodology note keep reporting every
 *                          component, not just the visible page.
 *                        - A line's history drawer drops the Received column and
 *                          shows Updated By instead (C_OrderLineHistory.UpdatedBy
 *                          resolved to AD_User.Name): who changed the line is what
 *                          the drawer is read for, and the received quantity is
 *                          a property of the line today, not of a past version.
 *                        - The Removed lines table also shows Updated By, as its
 *                          last column so both history views carry it in the same
 *                          position.
 *   VAI163   2026-07-31  Line items: Expected Delivery and Received are shown
 *                        only for Item-type product lines (isDeliverableLine) —
 *                        a charge line or a Service / Resource / Expense product
 *                        is never goods-received, so both cells now read "—".
 *                        Received shows Σ M_InOutLine.QtyEntered against
 *                        C_OrderLine.QtyEntered, both in the entered UOM and
 *                        labelled with it.
 *                        Generated From: adds RFQ and Project chips, and marks a
 *                        requisition reached through the RFQ with a "via RFQ"
 *                        pill. "Manual" now only shows when no origin exists.
 *                        Blanket Order chip added (C_Order.C_Order_Blanket).
 *                        Activity feed shows the full document lifecycle (new
 *                        prepared / completed / reactivated / voided / closed /
 *                        rejected / updated types), e-mail rows summarise the
 *                        To list with a Cc+Bcc count and the opened body lists
 *                        From / To / Cc / Bcc verbatim, and the history drawer's
 *                        Updated By is right-aligned under the Received column.
 *   VAI163   2026-08-04  - Landed Cost shows the distribution method the server
 *                          resolved from the dictionary for the value stored on
 *                          C_ExpectedCost.LandedCostDistribution
 *                          (DistributionName); the client's own I/Q/W/V/L/C
 *                          labels are now only a fallback.
 *                        - Quantities read in the entered (selected) UOM
 *                          throughout: the snapshot cards and the Line Items
 *                          summary now show the order's own unit and quantity
 *                          scale instead of the converted base-UOM totals, and
 *                          the Removed lines table names its unit like every
 *                          other quantity.
 *                        - New Record no longer leaves the previously selected
 *                          order on screen: the panel listens to the tab's
 *                          data-status events (the framework's New Record path
 *                          never calls refreshPanelData) and empties itself for a
 *                          row that has no key yet.
 *                        - Line items: numeric columns right-aligned as one, and
 *                          the Received cell redrawn as the VAS_099 GRN Received
 *                          column — the figure on its own line with the progress
 *                          bar stacked beneath it.
 *   VAI163   2026-08-04  - A cell with no value is left blank: the placeholder
 *                          dash is gone from the line items, both history views,
 *                          the Documents table and the landed-cost actual /
 *                          variance cells.
 *                        - The line's Attribute Set Instance follows the product
 *                          name on the same line (VAS_099 treatment) instead of
 *                          sitting on a sub-line of its own.
 *   VAI163   2026-08-04  Opening a record can now name the window it opens rather
 *                        than relying on the table's default zoom target:
 *                        WINDOW_NAME_BY_TABLE maps the table to a window name and
 *                        resolveWindowIdByName() turns that into an AD_Window_ID
 *                        through VAS_092_OverviewPurchaseOrder/GetWindow_ID
 *                        (resolved once per name and remembered). The RFQ chip
 *                        opens the VAS_RFQ screen this way, the Project chip
 *                        VAS_Project, the Requisition chip VAS_Requisition, a
 *                        Documents GRN row VAS_MaterialReceipt, a vendor invoice
 *                        row VAS_APInvoice, an AP payment row VAS_APPayment, the
 *                        Contract chip VAS_ContractMaster and the Blanket Order
 *                        chip VAS_BlanketPurchaseOrder. C_Order is named on both
 *                        sides — the blanket in the table map, the Sales Order
 *                        chip in WINDOW_NAME_BY_TABLE_SOTRX, which wins when
 *                        IsSOTrx is set. Any record with no name keeps the zoom
 *                        target, which is also the fallback when a name cannot be
 *                        resolved.
 *   VAI163   2026-08-05  - New Record and Copy Record now empty the panel
 *                          instead of leaving the previously selected order on
 *                          screen. Both entry points (refreshPanelData and the
 *                          data-status listener) ask isTabInserting() rather
 *                          than trusting the record id, which on a copied row
 *                          is the id of the order copied FROM.
 *                          isTabInserting reads GridTab.gridTable
 *                          .getIsInserting() — the flag GridTable.dataNew()
 *                          raises for both actions. Its first version asked the
 *                          GridTab directly, which exposes no such method, so
 *                          the guard silently never fired.
 *   VAI163   2026-08-05  - Generated From shows the MRP plan the order was
 *                          generated by (VAMRP_PlanRun_ID) as its own clickable
 *                          chip. A planned PO named no origin at all, so it
 *                          rendered the "Manual" fallback.
 *   VAI163   2026-08-05  Class prefix renamed MPC-vaspo- -> vas_092- so the panel's
 *                          styles cannot collide with another panel's.
 *   VAI163   2026-08-06  - Snapshot cards carry their full text as a tooltip. Every
 *                          line inside a card clips to one line, so a long figure —
 *                          the Received pair above all — read as ellipsised with no
 *                          way to see it. The title sits on the card, so hovering
 *                          anywhere over it reads the label, value and caption out.
 *                        - Generated From chips likewise, since a long document
 *                          number now truncates inside the chip rather than running
 *                          the strip off the panel edge.
 *   VAI163   2026-08-06  Recent Activity paginates at 15 rows a page
 *                          (ACTIVITY_PER_PAGE), reusing the line-items pager
 *                          control. A feed that fits on one page shows no controls
 *                          at all, and the section summary keeps counting the whole
 *                          feed rather than the page. The page resets with the rest
 *                          of the per-record view state on a record change / clear.
 *   VAI163   2026-08-07  Emits the vas_092-prefixed modifier classes the
 *                        stylesheet now uses, the runtime-built ones included
 *                        ("vas_092-tone-" + tone).
 *   VAI163   2026-08-07  New Record could still leave the previous order on the
 *                        panel, for two reasons the 2026-08-05 insert guard does
 *                        not cover. refreshPanelData can run BEFORE GridTable
 *                        raises its insert flag, so isTabInserting() asked at
 *                        that instant answers "no" and the panel loads the row
 *                        just left; and the reply of a fetch already on the wire
 *                        landed AFTER the clear and repainted it (visible mainly
 *                        the first time, when that fetch is the slow one).
 *                        refreshPanelData now goes through scheduleFetch, which
 *                        holds REFRESH_DELAY_MS and re-asks isTabInserting(),
 *                        and every fetch carries a token (fetchToken) that a
 *                        clear or a newer fetch invalidates — a reply holding a
 *                        stale token is dropped instead of rendering.
 *                        Ported from VAS_106.
 *   VAI163   2026-08-11  Order Progress dates its receipt stage from when the
 *                        GRN was CREATED (model side) rather than the movement
 *                        date, which a user can back-date. A stage whose date is
 *                        a stored timestamp is marked stamp:true and rendered
 *                        with formatStampDate, so the calendar day shown is the
 *                        viewer's own — the stored value is UTC and carries no
 *                        zone designator, so a receipt entered late in the
 *                        evening used to date to the next morning. Drafted
 *                        carries the same marker; it always read C_Order.Created.
 *   VAI163   2026-08-13  - Activity reports edits FIELD BY FIELD: an "updated"
 *                          row carries the name of the column that changed
 *                          (a.FieldName) and headlines with it, so the trail
 *                          reads "Updated <field> · <when> · by <who>". The old
 *                          "Order updated · 5 fields changed" wording (and its
 *                          VAS_092_FieldsChanged key) went with it — a count said
 *                          something moved but never what.
 *                        - An e-mail's recipient line lists every address on the
 *                          mail (To, Cc and Bcc, each labelled) in full instead
 *                          of naming the To list and counting the rest as
 *                          "+n more". The count could only be resolved by opening
 *                          the message, which a mail stored without a body cannot
 *                          do. allRecipients / countAddresses went with it, as did
 *                          the sub-line's tooltip. Ported from VAS_099.
 *   VAI163   2026-08-14  - The header's terms column carries a Drop Shipment
 *                          field (C_Order.IsDropShip), reading Yes or No like the
 *                          GRN overview's. It sits under Warehouse, which on a
 *                          drop-shipped order names a place the goods never reach
 *                          — the vendor delivers straight to the customer — and
 *                          nothing on the panel said so.
 *   VAI163   2026-08-14  - Drop Shipment reads as a TICK or a CROSS rather than as
 *                          "Yes" / "No" (headerFlagField). Neither word says more
 *                          than the mark does, and each took a whole field's line
 *                          to say it in a column that runs two fields wide. The
 *                          word travels as the field's tooltip and the mark's
 *                          aria-label, so the glyph is never the only statement of
 *                          what it means.
 *                        - The vendor contact's e-mail no longer prints OVER the
 *                          terms column beside it (stylesheet). A contact bit is a
 *                          flex item, and a flex item's default min-width is the
 *                          widest its content can be — so a long address refused
 *                          to narrow and overflowed the 40% vendor column. It
 *                          shows in the single-record view because that is where
 *                          the panel is narrow enough for the address to exceed
 *                          its column.
 *   VAI163   2026-08-17  Activity's field-level rows carry the MOVE: "was X →
 *                        now Y" under the field's name (changeDelta), the old
 *                        value struck through and a value the log recorded as
 *                        empty shown as an em dash, so a cleared field is
 *                        visibly cleared rather than looking like a rendering
 *                        gap. A row said WHICH field moved but never what it
 *                        moved from or to.
 *                        A field edit made on a LINE also names the line it
 *                        landed on (a.ChangeScope — line number + item), on the
 *                        sub-line the e-mail recipients use. Both follow
 *                        VAS_101 / VAS_104.
 *   VAI163   2026-08-18  Drop Shipment reads as the WORD "Yes" / "No" again
 *                        (plain headerField), not as a tick / cross. A mark
 *                        has to be decoded before it answers, and it only
 *                        answered in words to a reader who found the tooltip
 *                        or ran a screen reader. headerFlagField and the
 *                        "cross" icon went with it — nothing else drew one.
 *   VAI163   2026-08-20  - The Order Total card leads with the NET amount
 *                          (data.SubTotal, which is GrandTotal − Tax and so stays
 *                          net on a tax-inclusive price list) over "<ISO> ·
 *                          Exclusive Taxes". It led with the grand total under
 *                          "Inclusive Taxes". The card's name, tone, shape and
 *                          sub-line format are unchanged, so it still reads as one
 *                          of the four. VAS_092_ExclTaxFreight is the new key;
 *                          VAS_092_InclTaxFreight is no longer used.
 *                        - An "updated" activity row shows the field's DISPLAY
 *                          value rather than the raw one the change log stores:
 *                          a reference reads as the record's name, a list value as
 *                          its label, and a date as the date alone (model side,
 *                          DisplayChangeValue). changeValueText renders a
 *                          yyyy-MM-dd value in the reader's locale, building the
 *                          Date from local parts so a bare ISO date cannot roll
 *                          back a day west of Greenwich.
 *   VAI163   2026-08-21  Activity: a Task or Appointment row now says how many
 *                        e-mails were sent against it, and opens on click onto
 *                        each one - who it went to, its subject, when it went
 *                        and who sent it, then the message itself. The body is
 *                        shown ONLY once the row is opened.
 *   VAI163   2026-08-24  - A Send Invoice button in the header strip
 *                          (.vas_092-actions) opens the Preview and Share
 *                          Document form on this order through the shared
 *                          VAS_SentEmailDoc form, with the recipient seeded from
 *                          the VENDOR - its name and e-mail address. It runs off
 *                          the tab's own print process, so it is disabled (not
 *                          hidden) on a window that carries none; a blank address
 *                          is not a failure, VAS_SentEmailDoc resolves the
 *                          recipient on the server from AD_Table_ID + RecordID.
 *                          Follows VAS_189_ARInvoiceDetailPanel.
 *                        - Order Progress: the "With Vendor" stage reads
 *                          "Email Sent" or "Pending" (C_Order.VAS_IsEmailSent)
 *                          instead of repeating the completion date the Completed
 *                          stage above it already carries - a date that said the
 *                          order was finished, never that it had reached the
 *                          vendor. A stage may now carry its own sub-line (meta),
 *                          which holds in every state.
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {

    // True when the tab is sitting on a row that has not been saved yet —
    // whether it came from New Record or from Copy Record.
    //
    // The authority is the GRID TABLE's insert flag. VIS.GridTable.dataNew()
    // sets it for both New and Copy and clears it again on save, refresh or
    // undo, and GridTable.getIsInserting() reads it. GridTab does NOT expose
    // that method — it only holds the table as .gridTable — so asking the tab
    // itself always answered "no" and this guard never fired.
    //
    // Why the record id alone cannot answer this: on Copy Record the new row
    // carries the SOURCE record's field values, its key among them, so the id
    // handed to the panel is the order that was copied FROM. On New Record the
    // key is empty, but the framework can still re-issue the previously
    // selected id. Either way the panel would show a saved order's totals,
    // lines and activity beside an unsaved record.
    //
    // The direct read is tried first, then a few name variants for builds that
    // surface the flag elsewhere. When nothing answers, the result is "no" and
    // behaviour is exactly as it was.
    function isTabInserting(curTab) {
        if (!curTab) return false;
        try {
            if (curTab.gridTable && typeof curTab.gridTable.getIsInserting === "function"
                && curTab.gridTable.getIsInserting()) {
                return true;
            }
        } catch (e) { }

        var probes = ["getIsInserting", "isInserting", "getIsNew", "isNew"];
        for (var i = 0; i < probes.length; i++) {
            try {
                if (typeof curTab[probes[i]] === "function" && curTab[probes[i]]()) return true;
            } catch (e2) { }
        }
        return false;
    }

    VAS.VAS_092_OverviewPurchaseOrder = function () {
        this.record_ID = 0;
        this.table_ID = 0;
        this.windowNo = 0;
        this.AD_Window_ID = 0;
        this.curTab = null;
        this.selectedRow = null;
        this.panelWidth;

        var $self = this;
        var $root;
        var $busy;
        var $body;
        var $emptyState;
        var data = null;
        var linesPage = 1;      // current line-items page (1-based)
        var historyOpen = false; // Removed-lines history section expanded?
        // Which line items have their history drawer open, keyed by
        // C_OrderLine_ID. Survives a pager repaint; cleared per record.
        var lineHistOpen = {};
        var lcPage = 1;          // current Landed Cost page (1-based)
        var activityPage = 1;    // current Recent Activity page (1-based)
        // The C_Order_ID the panel is currently showing (or loading). 0 = nothing
        // on screen. Used to tell a real record change from the stream of
        // data-status events the tab fires while a record is being edited.
        var shownRecordId = 0;

        // How long refreshPanelData holds before it actually fetches.
        // On New Record / Copy Record the framework can call refreshPanelData
        // BEFORE GridTable raises its insert flag, so isTabInserting() asked at
        // that instant still answers "no" and the panel would load the record
        // the user has just moved off. Asking again after this pause gets the
        // truth. It also collapses a burst of arrow-key row changes into one
        // request instead of one per row.
        var REFRESH_DELAY_MS = 150;
        // Raised by every fetch, every scheduled fetch and every clear. A reply
        // carrying a token that is no longer the current one belongs to a record
        // the panel has already moved off, so it is dropped instead of painting.
        // This is what stops a slow FIRST response from landing on top of the
        // empty panel that New Record had already cleared — the delay above
        // cannot do it, because the response can arrive at any time.
        var fetchToken = 0;
        var pendingFetch = null;    // timer handle of a scheduled fetch, if any

        // Some AD_Message keys may not be seeded yet; fall back to a readable
        // English default so the panel never renders raw keys.
        var MSG_DEFAULTS = {
            VAS_092_NoData: "No purchase order selected",
            VAS_092_PurchaseOrder: "Purchase Order",
            VAS_092_Created: "Created",
            VAS_092_Ordered: "Ordered",
            VAS_092_SupplierRef: "Vendor Ref",
            VAS_092_Buyer: "Buyer",
            VAS_092_Vendor: "Vendor",
            VAS_092_BillTo: "Bill To",
            VAS_092_ShipTo: "Warehouse",
            VAS_092_PaymentTerms: "Payment Term",
            VAS_092_Pricelist: "Pricelist",
            VAS_092_Currency: "Currency",
            VAS_092_DropShipment: "Drop Shipment",
            VAS_092_Yes: "Yes",
            VAS_092_No: "No",
            // Priority (C_Order.PriorityRule)
            VAS_092_UrgentPriority: "Urgent priority",
            VAS_092_HighPriority: "High priority",
            VAS_092_MediumPriority: "Medium priority",
            VAS_092_LowPriority: "Low priority",
            VAS_092_MinorPriority: "Minor priority",
            VAS_092_NormalPriority: "Normal priority",
            // Delivery / status
            VAS_092_PaymentDone: "Payment Done",
            VAS_092_PaymentCompleted: "Payment Completed",
            VAS_092_PendingAmount: "Pending Amount",
            VAS_092_Completed: "Completed",
            VAS_092_PartialDelivered: "Partially Received",
            VAS_092_FullyReceived: "Received",
            VAS_092_WithVendor: "With Vendor",
            VAS_092_Drafted: "Drafted",
            // Header action bar
            VAS_092_SendInvoice: "Send Invoice",
            VAS_092_ActionFailed: "The action could not be completed.",
            // Generated From
            VAS_092_GeneratedFrom: "Generated From",
            VAS_092_Manual: "Manual",
            VAS_092_SalesOrder: "Sales Order",
            VAS_092_Origin: "Origin",
            VAS_092_Requisition: "Requisition",
            VAS_092_Rfq: "RFQ",
            VAS_092_ViaRfq: "via RFQ",
            VAS_092_Project: "Project",
            VAS_092_BlanketOrder: "Blanket Order",
            VAS_092_Contract: "Contract",
            VAS_092_Plan: "Plan",
            VAS_092_More: "more",
            // Snapshot
            // VAS_092_InclTaxFreight is unused: the Order Total card leads with
            // the NET amount now, so its sub-line reads Exclusive Taxes. Kept as a
            // default in case a tenant still has the key seeded.
            VAS_092_InclTaxFreight: "Inclusive Taxes",
            VAS_092_ExclTaxFreight: "Exclusive Taxes",
            VAS_092_OrderTotal: "Order Total",
            VAS_092_ExpectedDelivery: "Expected Delivery",
            VAS_092_LineItems: "Line Items",
            VAS_092_Lines: "lines",
            VAS_092_OrderedLower: "ordered",
            VAS_092_Of: "of",
            VAS_092_Received: "Received",
            // Progress
            VAS_092_InvoiceRaised: "Invoice Raised",
            VAS_092_OrderProgress: "Order Progress",
            VAS_092_Stage: "Stage",
            VAS_092_InProgress: "In progress",
            VAS_092_Pending: "Pending",
            VAS_092_EmailSent: "Email Sent",
            VAS_092_Required: "Required",
            // Line items
            VAS_092_Items: "items",
            VAS_092_Units: "units",
            VAS_092_Item: "Item",
            VAS_092_UnitPrice: "Unit price",
            VAS_092_Qty: "Qty",
            VAS_092_ExpDelivery: "Exp. delivery",
            VAS_092_LineTotal: "Line Amount",
            VAS_092_Subtotal: "Subtotal",
            VAS_092_Tax: "Tax",
            VAS_092_GrandTotal: "Grand Total",
            VAS_092_SKU: "SKU",
            VAS_092_Delivered: "Received",
            VAS_092_Partial: "Partial",
            VAS_092_Awaiting: "Awaiting",
            // History
            VAS_092_History: "Line History",
            VAS_092_Changes: "changes",
            VAS_092_ChangedOn: "Changed On",
            VAS_092_UpdatedBy: "Updated By",
            VAS_092_ShowDetails: "Show Details",
            VAS_092_HideDetails: "Hide Details",
            VAS_092_ShowHistory: "Show history",
            VAS_092_HideHistory: "Hide history",
            VAS_092_RemovedLines: "Removed lines",
            // Pagination
            VAS_092_Showing: "Showing",
            VAS_092_Prev: "Previous",
            VAS_092_Next: "Next",
            // Budget (GL budget control / breach)
            VAS_092_Budget: "Budget",
            VAS_092_BudgetBreach: "Budget Breach",
            VAS_092_WithinBudget: "Within Budget",
            VAS_092_OverBudgetBy: "Over budget by",
            VAS_092_MaxLineBreach: "Highest line breach",
            VAS_092_LineBreaches: "Line breaches",
            VAS_092_NoBudgetBreach: "This order is within its allocated budget.",
            VAS_092_BudgetNote: "Budget breach amounts are shown in the accounting currency the GL budget is maintained in.",
            // Documents (GRNs / invoices raised from this PO)
            VAS_092_Documents: "Documents",
            VAS_092_Document: "Document",
            VAS_092_DocDate: "Date",
            VAS_092_DocStatus: "Status",
            VAS_092_Amount: "Amount",
            VAS_092_GoodsReceipt: "Goods Receipt",
            VAS_092_VendorInvoice: "Vendor Invoice",
            VAS_092_APPayment: "AP Payment",
            VAS_092_DiscountedAmount: "Discounted Amount",
            VAS_092_GRNsCount: "GRNs",
            VAS_092_InvoicesCount: "invoices",
            VAS_092_PaymentsCount: "payments",
            VAS_092_LinesCount: "lines",
            VAS_092_Paid: "Paid",
            VAS_092_Posted: "Posted",
            VAS_092_StDrafted: "Drafted",
            VAS_092_StInProgress: "In Progress",
            VAS_092_StCompleted: "Completed",
            VAS_092_StClosed: "Closed",
            VAS_092_StApproved: "Approved",
            VAS_092_StNotApproved: "Not Approved",
            VAS_092_StInvalid: "Invalid",
            VAS_092_StWaiting: "Waiting",
            VAS_092_StUnknown: "Unknown",
            // Landed cost
            VAS_092_ByValue: "By value",
            VAS_092_ByQuantity: "By quantity",
            VAS_092_ByWeight: "By weight",
            VAS_092_ByVolume: "By volume",
            VAS_092_Equally: "Equally",
            VAS_092_ByCosts: "By costs",
            VAS_092_NotSet: "Not set",
            VAS_092_LandedCost: "Landed Cost",
            VAS_092_CostComponent: "Cost Component",
            VAS_092_DistributionMethod: "Distribution Method",
            VAS_092_Expected: "Expected",
            VAS_092_Actual: "Actual",
            VAS_092_Variance: "Variance",
            VAS_092_Components: "components",
            VAS_092_Basis: "basis",
            VAS_092_MixedBasis: "mixed basis",
            VAS_092_Invoiced: "Invoiced",
            VAS_092_AwaitingInvoice: "Awaiting invoice",
            VAS_092_OnBudget: "On budget",
            // Landed cost column tooltips
            VAS_092_TipComponent: "The cost element charged on top of the goods (freight, duty, insurance …), with the document it came from underneath.",
            VAS_092_TipMethod: "How this cost is spread across the order lines — by invoice value, quantity, weight, volume, equally per line or by costs.",
            VAS_092_TipExpected: "Landed cost planned on this order, before any vendor invoice.",
            VAS_092_TipActual: "Landed cost actually charged, taken from completed vendor invoices allocated to this order's receipts.",
            VAS_092_TipAwaiting: "No vendor invoice has charged this cost yet, so there is nothing actual to show.",
            VAS_092_TipVariance: "Actual minus expected.",
            VAS_092_TipOver: "Actual is over expected by",
            VAS_092_TipUnder: "Actual is under expected by",
            VAS_092_TipOnBudget: "Actual matches what was expected.",
            VAS_092_ExpectedLandedCost: "Expected Landed Cost",
            VAS_092_ActualToDate: "Actual to Date",
            VAS_092_OpenNotInvoiced: "Open (not invoiced)",
            VAS_092_LandedValue: "Landed Value",
            VAS_092_LandedMethodology: "Actuals replace estimates as vendor charge invoices are completed —",
            VAS_092_ComponentsInvoiced: "components invoiced",
            VAS_092_DistributedAcross: "Distributed across lines",
            VAS_092_Line: "Line",
            // Notes / Activity
            VAS_092_Notes: "Notes",
            VAS_092_NotesCount: "notes",
            VAS_092_TagNote: "Note",
            VAS_092_TagEmail: "Email",
            VAS_092_MailTo: "To",
            VAS_092_MailCc: "Cc",
            VAS_092_MailBcc: "Bcc",
            VAS_092_MailFrom: "From",
            VAS_092_ShowMailBody: "Show message",
            VAS_092_HideMailBody: "Hide message",
            // E-mails sent against a meeting or task, opened from its row.
            VAS_092_MailSubject: "Subject",
            VAS_092_NoSubject: "(no subject)",
            VAS_092_Email: "email",
            VAS_092_Emails: "emails",
            VAS_092_ShowMails: "Show e-mails",
            VAS_092_HideMails: "Hide e-mails",
            VAS_092_TagGRN: "GRN",
            VAS_092_ActGRN: "Goods received",
            VAS_092_TagInvoice: "Invoice",
            VAS_092_ActInvoice: "Vendor invoice",
            VAS_092_TagPayment: "AP Payment",
            VAS_092_ActPayment: "AP Payment Created",
            VAS_092_TagApproval: "Approved",
            VAS_092_ActApproval: "Order approved",
            VAS_092_TagCreated: "Created",
            VAS_092_ActCreated: "Order created",
            // Document lifecycle (workflow nodes + header edits)
            VAS_092_TagPrepared: "Prepared",
            VAS_092_TagCompleted: "Completed",
            VAS_092_TagReactivated: "Re-activated",
            VAS_092_TagRejected: "Rejected",
            VAS_092_TagVoided: "Voided",
            VAS_092_TagReversed: "Reversed",
            VAS_092_TagClosed: "Closed",
            VAS_092_TagInvalidated: "Invalid",
            VAS_092_TagUpdated: "Updated",
            // The shared correspondence / engagement sources.
            VAS_092_TagAppointment: "Meeting",
            VAS_092_TagTask: "Task",
            VAS_092_TagCall: "Call",
            VAS_092_TagLetter: "Letter",
            VAS_092_ActCancelled: "Cancelled",
            VAS_092_ActCompleted: "Completed",
            VAS_092_ActUpdated: "Order updated",
            VAS_092_ActFieldUpdated: "Updated",
            VAS_092_RecentActivity: "Activity",
            VAS_092_Updates: "updates",
            VAS_092_OpenRecord: "Open"
        };

        // Prefer the seeded AD_Message; else the English default; else the key.
        function getMsg(key) {
            try {
                var m = VIS.Msg.getMsg(key);
                if (m && m !== key) return m;
            } catch (e) { }
            return MSG_DEFAULTS.hasOwnProperty(key) ? MSG_DEFAULTS[key] : key;
        }

        this.init = function () {
            $root = $('<div class="vas_092-root"></div>');
            $body = $('<div class="vas_092-body"></div>');
            $emptyState = $('<div class="vas_092-empty" style="display:none;"></div>');
            $emptyState.text(getMsg("VAS_092_NoData"));
            $root.append($body).append($emptyState);
            createBusyIndicator();
            bindEvents();
        };

        function createBusyIndicator() {
            $busy = $('<div class="vis-apanel-busy">' +
                      '<div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div>' +
                      '</div>');
            $busy.css({
                "position": "absolute", "width": "100%", "height": "100%",
                "text-align": "center", "z-index": "999"
            });
            $busy[0].style.visibility = "hidden";
            $root.append($busy);
        }

        function showBusy(show) {
            if (!$busy || !$busy[0]) return;
            $busy[0].style.visibility = show ? "visible" : "hidden";
        }

        // Drops whatever the panel was loading: cancels a fetch still waiting on
        // its delay and invalidates the token of one already on the wire, so
        // neither can paint over what the caller is about to put on screen.
        function invalidateFetch() {
            fetchToken++;
            if (pendingFetch) {
                clearTimeout(pendingFetch);
                pendingFetch = null;
            }
        }

        // Same thing, reachable from dispose so a timer cannot outlive the panel.
        this.abortPendingFetch = invalidateFetch;

        // Waits REFRESH_DELAY_MS, re-asks the tab whether it is inserting, and
        // only then fetches. See REFRESH_DELAY_MS for why the wait is needed.
        this.scheduleFetch = function (recordID) {
            invalidateFetch();
            var token = fetchToken;
            // Claim the record now, not when the timer fires: shownRecordId means
            // "showing or loading", and leaving it stale through the wait would
            // let the data-status listener fire a second fetch for the same row.
            shownRecordId = +recordID || 0;
            // Feedback while we hold — clear()/fetchData() own it from here.
            showBusy(true);
            pendingFetch = setTimeout(function () {
                pendingFetch = null;
                if (token !== fetchToken) return;   // superseded while waiting
                // The insert flag may only have been raised during the wait.
                if (isTabInserting($self.curTab)) {
                    $self.record_ID = 0;
                    $self.clear();
                    return;
                }
                $self.fetchData(recordID);
            }, REFRESH_DELAY_MS);
        };

        this.fetchData = function (recordID) {
            invalidateFetch();
            var token = fetchToken;
            shownRecordId = +recordID || 0;
            showBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + "VAS_092_OverviewPurchaseOrder/GetPurchaseOrderOverview",
                type: "GET",
                dataType: "json",
                data: { C_Order_ID: recordID },
                success: function (raw) {
                    // Reply for a record the panel has already left (a New
                    // Record cleared it, or a newer row was selected). Whoever
                    // superseded us owns the busy indicator now, so leave it be.
                    if (token !== fetchToken) return;
                    var parsed = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                    data = parsed;
                    // Reset per-record view state so a newly selected order starts
                    // on the first line page with History collapsed.
                    linesPage = 1;
                    historyOpen = false;
                    lineHistOpen = {};
                    lcPage = 1;
                    activityPage = 1;
                    render();
                    showBusy(false);
                },
                error: function (err) {
                    if (token !== fetchToken) return;
                    console.log(err);
                    showBusy(false);
                }
            });
        };

        // Empties the panel back to its "no purchase order selected" state. The
        // per-record view state goes with it, so the next order never inherits the
        // page, the open history drawers or the expanded sections of the last one.
        this.clear = function () {
            invalidateFetch();
            data = null;
            shownRecordId = 0;
            linesPage = 1;
            historyOpen = false;
            lineHistOpen = {};
            lcPage = 1;
            activityPage = 1;
            render();
            showBusy(false);
        };

        // The framework notifies a tab panel when the selected record changes
        // (refreshPanelData) but NOT when the user starts a new one:
        // GridController.dataNew() never reaches the tab panel. The panel would
        // therefore keep showing the previously selected order beside an empty new
        // record. Listening to the tab's own data-status events closes that gap —
        // a current row carrying no key yet (an unsaved new record) empties the
        // panel, and a key other than the one on screen loads it.
        function onTabDataStatus(e) {
            // Two signals for "a new record is on screen": the event says the tab
            // is inserting, and the current row carries no key yet.
            var inserting = false;
            try {
                inserting = !!(e && typeof e.getIsInserting === "function" && e.getIsInserting());
            } catch (ex) {
                inserting = false;
            }
            // The event does not report insert state on every build, and a
            // COPIED row still carries the source record's key — so ask the tab
            // as well before trusting the id below.
            if (!inserting) inserting = isTabInserting($self.curTab);

            var rid = 0;
            try {
                if ($self.curTab && typeof $self.curTab.getRecord_ID === "function") {
                    rid = +$self.curTab.getRecord_ID() || 0;
                }
            } catch (ex2) {
                rid = 0;
            }

            if (inserting || rid <= 0) {
                // New (unsaved) record — nothing to show against it.
                if (shownRecordId || data) {
                    $self.record_ID = 0;
                    $self.clear();
                }
                return;
            }
            if (rid !== shownRecordId) {
                $self.record_ID = rid;
                $self.fetchData(rid);
            }
        }

        // Registered on the tab in startPanel, removed in dispose. Kept as an
        // object because the framework calls listener.dataStatusChanged(event).
        this.tabDataListener = { dataStatusChanged: function (e) { onTabDataStatus(e); } };

        function render() {
            $body.empty();

            if (!data || !data.C_Order_ID) {
                $body.hide();
                $emptyState.show();
                return;
            }

            $emptyState.hide();
            $body.show();

            // Body is a flat stack of self-contained sections.
            renderHeader();
            renderLinked();
            renderSnapshot();
            renderProgress();
            renderBudget();
            renderLines();
            renderDocuments();
            renderHistory();
            renderLandedCost();
            renderBottom();
        }

        // ----------------------------------------------------------------- //
        //  Section / primitive builders                                      //
        // ----------------------------------------------------------------- //

        function section(title, opts, $parent) {
            opts = opts || {};
            var $sec = $('<section class="vas_092-sec"></section>');
            var $head = $('<div class="vas_092-secHead"></div>');
            $head.append($('<h2 class="vas_092-secTitle"></h2>').text(title));

            var $right = $('<div class="vas_092-secRight"></div>');
            if (opts.summary) {
                $right.append($('<span class="vas_092-secSummary"></span>').text(opts.summary));
            }
            if (opts.action) {
                $right.append($('<a class="vas_092-secAction"></a>').text(opts.action));
            }
            if (opts.summary || opts.action) $head.append($right);

            $sec.append($head);
            ($parent || $body).append($sec);
            return $sec;
        }

        // Status pill (tinted). tone: info | success | warning | risk | neutral | purple
        function pill(label, tone) {
            return $('<span class="vas_092-pill"></span>')
                .addClass("vas_092-tone-" + (tone || "neutral"))
                .text(label);
        }

        // ---------- Header (title strip + vendor / terms card) ---------- //

        // Maps the order's delivery / progress state to a semantic tone + label.
        // Fully received is checked before partial so a completed order never
        // reports "Partially Received".
        function statusTone(d) {
            if (d.IsPaymentDone)
                return { tone: "success", label: getMsg("VAS_092_PaymentDone") };
            if (d.IsFullyDelivered)
                return { tone: "success", label: getMsg("VAS_092_Completed") };
            if (d.IsPartialDelivered)
                return { tone: "warning", label: getMsg("VAS_092_PartialDelivered") };
            if (d.IsWithVendor)
                return { tone: "info", label: getMsg("VAS_092_WithVendor") };
            return { tone: "neutral", label: getMsg("VAS_092_Drafted") };
        }

        // Priority pill descriptor from the real C_Order.PriorityRule field
        // (1 Urgent / 3 High / 5 Medium / 7 Low), mirroring the Sales Order
        // overview, so a changed priority is reflected in the panel.
        function priorityMeta() {
            // Tone/label are driven by the real C_Order.PriorityRule value so the
            // panel always matches the record screen. Low (7) and Minor (9) both
            // render a green (success) badge; anything unmapped stays neutral.
            switch (data.PriorityRule) {
                case "1": return { tone: "risk",    icon: "chevUp", label: getMsg("VAS_092_UrgentPriority") };
                case "3": return { tone: "warning", icon: "chevUp", label: getMsg("VAS_092_HighPriority") };
                case "5": return { tone: "info",    icon: null,     label: getMsg("VAS_092_MediumPriority") };
                case "7": return { tone: "success", icon: null,     label: getMsg("VAS_092_LowPriority") };
                case "9": return { tone: "success", icon: null,     label: getMsg("VAS_092_MinorPriority") };
                default:  return { tone: "neutral", icon: null,     label: getMsg("VAS_092_NormalPriority") };
            }
        }

        // Header: soft-gradient title strip (title + subtitle, priority + status
        // pills) above a white two-column details card — vendor identity on the
        // left, payment / pricelist / currency / ship-to fields on the right,
        // mirroring the Sales Order overview.
        function renderHeader() {
            var st = statusTone(data);
            var pm = priorityMeta();

            var $strip = $('<section class="vas_092-hdr"></section>');
            var $top = $('<div class="vas_092-hdrTop"></div>');

            var $tl = $('<div class="vas_092-hdrTitleWrap"></div>');
            $tl.append($('<div class="vas_092-hdrTitle"></div>').text(
                getMsg("VAS_092_PurchaseOrder") +
                (data.DocumentNo ? " — " + data.DocumentNo : "")));

            var subBits = [];
            if (data.POReference) subBits.push(getMsg("VAS_092_SupplierRef") + " " + data.POReference);
            var ordered = formatDate(data.DateOrdered);
            if (ordered) subBits.push(getMsg("VAS_092_Ordered") + " " + ordered);
            if (subBits.length) {
                $tl.append($('<div class="vas_092-hdrSub"></div>').text(subBits.join(" · ")));
            }
            $top.append($tl);

            var $pills = $('<div class="vas_092-hdrPills"></div>');
            $pills.append(headerPill(pm.label, pm.tone, pm.icon, false));
            $pills.append(headerPill(st.label, st.tone, null, true));
            // Posted badge — shown only when the document has been posted to the
            // ledger (C_Order.Posted = 'Y'); hidden for unposted documents.
            if (data.Posted) {
                $pills.append(headerPill(getMsg("VAS_092_Posted"), "success", "check", false));
            }
            // Budget Breach badge — shown when the platform's budget check flagged
            // this order as over its GL budget (C_Order.IsBudgetViolated = 'Y').
            if (data.IsBudgetViolated) {
                $pills.append(headerPill(getMsg("VAS_092_BudgetBreach"), "risk", "alert", false));
            }
            $top.append($pills);

            $strip.append($top);
            renderActions($strip);
            $body.append($strip);

            // --- Details card: vendor identity (left) + terms fields (right) ---
            if (!data.VendorName && !data.VendorAddress &&
                !data.ContactName && !data.ContactPhone && !data.ContactEmail &&
                !data.PaymentTermName && !data.PriceListName && !data.WarehouseName &&
                !data.OrgName && !data.ISO_Code && !data.IsDropShip) {
                return;
            }

            var $card = $('<section class="vas_092-hdrCard"></section>');

            // Left column: vendor name + address + contact bits + bill to.
            var $left = $('<div class="vas_092-hdrColL"></div>');
            $left.append($('<div class="vas_092-fLabel"></div>').text(getMsg("VAS_092_Vendor")));
            $left.append($('<div class="vas_092-vendName"></div>').text(data.VendorName || ""));

            if (data.VendorAddress) {
                var $addr = $('<div class="vas_092-vendAddr"></div>');
                $addr.append(svgIcon("pin"));
                $addr.append($('<span></span>').text(data.VendorAddress));
                $left.append($addr);
            }

            var $contact = $('<div class="vas_092-vendContact"></div>');
            appendContactBit($contact, "user",  data.ContactName);
            appendContactBit($contact, "phone", data.ContactPhone);
            appendContactBit($contact, "mail",  data.ContactEmail);
            if ($contact.children().length) $left.append($contact);           
            $card.append($left);

            // Right column: labelled term fields (mirrors SO field set).
            var $right = $('<div class="vas_092-hdrColR"></div>');
            if (data.BuyerName)       $right.append(headerField(getMsg("VAS_092_Buyer"), data.BuyerName));
            if (data.PaymentTermName) $right.append(headerField(getMsg("VAS_092_PaymentTerms"), data.PaymentTermName));
            if (data.PriceListName)   $right.append(headerField(getMsg("VAS_092_Pricelist"), data.PriceListName));
            var cur = (data.ISO_Code || "") + (data.CurSymbol ? " (" + data.CurSymbol + ")" : "");
            if (cur.trim())           $right.append(headerField(getMsg("VAS_092_Currency"), cur));
            if (data.WarehouseName) $right.append(headerField(getMsg("VAS_092_ShipTo"), data.WarehouseName));
            // Drop Shipment (C_Order.IsDropShip) — always shown, Yes or No: "No"
            // is as much of an answer as "Yes" here, and the reader is looking at
            // a Warehouse right above it that a drop-shipped order never reaches.
            // Reads as the WORD, like every other field in this column, so the
            // answer needs no glyph to be decoded first.
            $right.append(headerField(getMsg("VAS_092_DropShipment"),
                data.IsDropShip ? getMsg("VAS_092_Yes") : getMsg("VAS_092_No")));
            //if (data.OrgName) $right.append(headerField(getMsg("VAS_092_BillTo"), data.OrgName));
            if ($right.children().length) $card.append($right);

            $body.append($card);
        }

        // ---------- Header action bar ---------- //

        // Send Invoice runs off the TAB's print process (the same one the
        // framework's own print button uses), so the control is disabled — not
        // hidden — when the window carries none: an absent button reads as a
        // missing feature, a disabled one as a document that cannot be printed.
        function renderActions($parent) {
            var hasPrintProcess = $self.curTab
                && typeof $self.curTab.getAD_Process_ID === "function"
                && +$self.curTab.getAD_Process_ID() > 0;

            var $a = $('<div class="vas_092-actions"></div>');
            var $send = $('<button type="button" class="vas_092-btn"></button>');
            $send.append(svgIcon("send"));
            $send.append($('<span></span>').text(getMsg("VAS_092_SendInvoice")));
            $send.prop("disabled", !hasPrintProcess);
            $send.on("click", function () { if (hasPrintProcess) sendInvoiceEmail(); });
            $a.append($send);
            $parent.append($a);
        }

        // AD_Process_ID / AD_Table_ID / AD_Window_ID for the share flow, read off
        // the current grid tab — the same values the framework's print button
        // works from. Mirrors VAS_189_ARInvoiceDetailPanel.
        function printContext() {
            var tab = $self.curTab;
            return {
                AD_Process_ID: (tab && typeof tab.getAD_Process_ID === "function") ? tab.getAD_Process_ID() : 0,
                AD_Table_ID: (tab && typeof tab.getAD_Table_ID === "function") ? tab.getAD_Table_ID() : ($self.table_ID || 0),
                AD_Window_ID: (tab && typeof tab.getAD_Window_ID === "function") ? tab.getAD_Window_ID() : ($self.AD_Window_ID || 0),
                RecordID: $self.record_ID,
                ToName: (data && data.VendorName) ? data.VendorName : "",
                ToEmail: (data && data.VendorEmail) ? data.VendorEmail : ""
            };
        }

        // Open the Preview and Share Document form (the shared VA112 share/e-mail
        // panel) on this purchase order, with the recipient seeded from the
        // VENDOR — its name and e-mail address. When the address is blank
        // VAS_SentEmailDoc resolves it on the server from AD_Table_ID + RecordID,
        // so a vendor with no address on the order still reaches its contact.
        function sendInvoiceEmail() {
            if (!$self.record_ID || !$self.curTab) return;
            if (!VAS.VAS_SentEmailDoc || typeof VAS.VAS_SentEmailDoc.sendEmail !== "function") {
                toast(getMsg("VAS_092_ActionFailed"), true);
                return;
            }

            var ctxRes = printContext();
            if (!ctxRes.AD_Process_ID || !ctxRes.AD_Table_ID || !ctxRes.AD_Window_ID) {
                toast(getMsg("VAS_092_ActionFailed"), true);
                return;
            }

            // Called as a plain static, not with `new`: it returns nothing and
            // instantiates the form itself.
            VAS.VAS_SentEmailDoc.sendEmail({
                windowNo: $self.windowNo,
                AD_Process_ID: ctxRes.AD_Process_ID,
                AD_Table_ID: ctxRes.AD_Table_ID,
                RecordID: ctxRes.RecordID,
                AD_Window_ID: ctxRes.AD_Window_ID,
                Name: ctxRes.ToName,
                EMailID: ctxRes.ToEmail
            });
        }

        function headerPill(label, tone, icon, withDot) {
            var $p = $('<span class="vas_092-hdrPill"></span>')
                .addClass("vas_092-tone-" + (tone || "neutral"));
            if (icon) $p.append(svgIcon(icon));
            if (withDot) $p.append($('<span class="vas_092-hdrDot"></span>'));
            $p.append($('<span></span>').text(label));
            return $p;
        }

        function headerField(label, value) {
            var $f = $('<div class="vas_092-hdrField"></div>');
            $f.append($('<div class="vas_092-fLabel"></div>').text(label));
            $f.append($('<div class="vas_092-fVal"></div>').text(value));
            return $f;
        }

        function appendContactBit($container, icon, value) {
            if (!value) return;
            var $bit = $('<span class="vas_092-contactBit"></span>');
            $bit.append(svgIcon(icon));
            $bit.append($('<span></span>').text(value));
            $container.append($bit);
        }

        // ---------- Generated From (chip strip) ---------- //

        // Shows only origins that actually exist — Sales Order (Ref_Order_ID),
        // Requisition (M_Requisition), RFQ (C_RfQResponse.C_Order_ID), Project
        // (C_ProjectLine.C_OrderPO_ID), Blanket Order (C_Order.C_Order_Blanket)
        // and Contract reference (VAS_ContractMaster) — each a clickable chip
        // that opens the source
        // record. "Manual" is the fallback for a PO with no origin at all, so it
        // only shows when every one of those came back empty.
        function renderLinked() {
            var $strip = $('<section class="vas_092-genfrom"></section>');
            $strip.append($('<span class="vas_092-gfLabel"></span>')
                .text(getMsg("VAS_092_GeneratedFrom")));

            var $chips = $('<div class="vas_092-gfChips"></div>');
            var any = false;

            // Sales Order (origin) — from Ref_Order_ID. Opened as a sales
            // transaction (isSOTrx = true) so the framework resolves the Sales
            // Order window rather than the Purchase Order window (both live in
            // C_Order).
            if (data.RefOrderDocNo) {
                $chips.append(originChip("doc", getMsg("VAS_092_SalesOrder"), data.RefOrderDocNo,
                    pill(getMsg("VAS_092_Origin"), "info"), "info", "C_Order", data.RefOrderId, true));
                any = true;
            }

            // Requisition — the requisition(s) this PO was generated from, whether
            // raised into the PO directly or reached through the RFQ. A chain
            // origin is marked so the reader can tell the two apart.
            if (data.RequisitionDocNo) {
                var reqVal = data.RequisitionDocNo;
                if (data.RequisitionCount > 1)
                    reqVal += " +" + (data.RequisitionCount - 1) + " " + getMsg("VAS_092_More");
                $chips.append(originChip("clipboardCheck", getMsg("VAS_092_Requisition"), reqVal,
                    data.IsRequisitionViaRfq ? pill(getMsg("VAS_092_ViaRfq"), "neutral") : null,
                    "success", "M_Requisition", data.RequisitionId));
                any = true;
            }

            // RFQ — the request for quotation the PO was raised from
            // (C_RfQResponse.C_Order_ID).
            if (data.RfqId > 0) {
                // No "#id" fallback on any chip below: an internal key is not a
                // document number, and printing one tells the reader nothing they
                // can act on. Where the model could not resolve an identifier the
                // chip carries its LABEL alone and still opens the record.
                var rfqVal = data.RfqNo || "";
                if (data.RfqCount > 1)
                    rfqVal += " +" + (data.RfqCount - 1) + " " + getMsg("VAS_092_More");
                $chips.append(originChip("clipboardCheck", getMsg("VAS_092_Rfq"), rfqVal,
                    null, "warning", "C_RfQ", data.RfqId));
                any = true;
            }

            // Project — the project line this PO was generated for
            // (C_ProjectLine.C_OrderPO_ID).
            if (data.ProjectId > 0) {
                var projVal = data.ProjectNo || data.ProjectName || "";
                if (data.ProjectCount > 1)
                    projVal += " +" + (data.ProjectCount - 1) + " " + getMsg("VAS_092_More");
                $chips.append(originChip("doc", getMsg("VAS_092_Project"), projVal,
                    null, "info", "C_Project", data.ProjectId));
                any = true;
            }

            // Blanket Order — the blanket this PO was released against
            // (C_Order.C_Order_Blanket). Opened on the purchase side: the blanket
            // is itself a C_Order, so isSOTrx stays false.
            if (data.BlanketOrderId > 0) {
                var blanketVal = data.BlanketOrderNo || "";
                // A release can draw on more than one blanket when the link is
                // read from the lines; the first is named and the rest counted.
                if (data.BlanketOrderCount > 1) {
                    blanketVal += " +" + (data.BlanketOrderCount - 1) + " " + getMsg("VAS_092_More");
                }
                $chips.append(originChip("calendar", getMsg("VAS_092_BlanketOrder"),
                    blanketVal, null, "success", "C_Order", data.BlanketOrderId, false));
                any = true;
            }

            // Contract reference — C_Order.VAS_ContractMaster_ID.
            if (data.ContractMasterId > 0) {
                $chips.append(originChip("doc", getMsg("VAS_092_Contract"),
                    data.ContractMasterNo || "",
                    null, "purple", "VAS_ContractMaster", data.ContractMasterId));
                any = true;
            }

            // Plan — the MRP plan run that generated this order
            // (VAMRP_PlanRun_ID). A planned PO is not a manual one, and used to
            // fall through to the "Manual" chip because nothing read the plan.
            if (data.PlanRunId > 0) {
                var planVal = data.PlanRunNo || "";
                if (data.PlanRunCount > 1)
                    planVal += " +" + (data.PlanRunCount - 1) + " " + getMsg("VAS_092_More");
                $chips.append(originChip("factory", getMsg("VAS_092_Plan"), planVal,
                    null, "warning", "VAMRP_PlanRun", data.PlanRunId));
                any = true;
            }

            if (!any) {
                $chips.append(originChip("pencil", getMsg("VAS_092_Manual"), null, null, "info", null, 0));
            }

            $strip.append($chips);
            $body.append($strip);
        }

        // Origin chip: leading icon (tinted by iconTone) + grey label + dark
        // value, with an optional trailing status pill. When a table + record id
        // is supplied the chip becomes a link that opens that record.
        function originChip(icon, label, value, $statusPill, iconTone, tableName, recordId, isSOTrx) {
            var $chip = $('<span class="vas_092-chip"></span>').addClass("vas_092-ic-" + (iconTone || "muted"));
            var isLink = tableName && recordId && +recordId > 0;
            if (isLink) {
                $chip.addClass("vas_092-is-link")
                    .attr("data-open-table", tableName)
                    .attr("data-open-id", recordId);
                // Sales-transaction records (e.g. the originating Sales Order in
                // C_Order) must open in their SO window, not the PO window.
                if (isSOTrx) $chip.attr("data-open-sotrx", "Y");
            }
            // The chip caps at the strip's width and its value truncates inside
            // it, so one long document number cannot run off the panel — the
            // untruncated text stays readable on the chip's own tooltip.
            $chip.attr("title", value ? label + ": " + value : label);
            $chip.append(svgIcon(icon));
            $chip.append($('<span class="vas_092-chipLabel"></span>').text(label));
            if (value) $chip.append($('<span class="vas_092-chipVal"></span>').text(value));
            if ($statusPill) $chip.append($statusPill);
            if (isLink) $chip.append(svgIcon("arrowUpRight"));
            return $chip;
        }

        // ---------- Snapshot (metric grid) ---------- //

        function renderSnapshot() {
            var $snap = $('<section class="vas_092-snap"></section>');

            // Order Total — the card keeps its name; the FIGURE is the NET amount,
            // the order's subtotal before tax.
            //
            // SubTotal, not TotalLines: it is GrandTotal − TaxAmt (model side), so
            // on a tax-INCLUSIVE price list — where C_Order.TotalLines carries the
            // gross — it is still the net figure, and it is the same number the
            // lines table foots with.
            //
            // The sub-line is what distinguishes the two totals, so it says
            // outright which one this is rather than leaving the reader to infer it
            // from the label. Same shape as before (ISO · basis), so the card sits
            // beside the other three unchanged.
            var totalSub = (data.ISO_Code || "");
            var excl = getMsg("VAS_092_ExclTaxFreight");
            totalSub = totalSub ? totalSub + " · " + excl : excl;
            $snap.append(metricCard("total", "coins", getMsg("VAS_092_OrderTotal"),
                formatAmount(+data.SubTotal || 0, data.CurSymbol, data.ISO_Code, data.StdPrecision),
                totalSub, null));

            // Expected Delivery — promised date + delivery-status caption.
            var st = statusTone(data);
            $snap.append(metricCard("delivery", "calendar", getMsg("VAS_092_ExpectedDelivery"),
                formatDate(data.DatePromised), st.label, null));

            // Line Items — line count + total quantity ordered, in the unit the
            // order was keyed in.
            $snap.append(metricCard("lines", "box", getMsg("VAS_092_LineItems"),
                (data.LineCount || 0) + " " + getMsg("VAS_092_Lines"),
                qtyWithUnit(+data.TotalQtyOrdered || 0) + " " + getMsg("VAS_092_OrderedLower"),
                null));

            // Received — delivered/ordered + percent and fully-received line count.
            // Both figures are in the entered (selected) UOM, on the same scale as
            // the line rows, with the unit named once after the pair.
            var ordered = +data.TotalQtyOrdered || 0;
            var delivered = +data.TotalQtyDelivered || 0;
            var pct = ordered > 0 ? Math.round((delivered / ordered) * 100) : 0;
            // Denominator is the deliverable (stockable) line count, not the raw
            // line count — charge / service lines are never received, so counting
            // them would keep a fully received order below 100%.
            var recvSub = pct + "% · " + (data.FullyReceivedLineCount || 0) + " " +
                getMsg("VAS_092_Of") + " " + (data.DeliverableLineCount || 0) + " " +
                getMsg("VAS_092_Lines");
            $snap.append(metricCard("received", "inbox", getMsg("VAS_092_Received"),
                formatQty(delivered) + " / " + qtyWithUnit(ordered), recvSub, pct));

            $body.append($snap);
        }

        function metricCard(tone, icon, label, value, sub, pct) {
            var $c = $('<div class="vas_092-metric"></div>').addClass("vas_092-tone-" + tone);

            // Label, value and caption each clip to a single line inside the card,
            // so a long figure (the Received pair "delivered / ordered UOM" above
            // all) shows ellipsised. The whole card carries the untruncated text as
            // a tooltip — a title on the card serves every cell inside it, so
            // hovering anywhere over the card reads out the full value.
            var tip = label;
            if (value) tip += ": " + value;
            if (sub) tip += " · " + sub;
            $c.attr("title", tip);

            var $head = $('<div class="vas_092-mHead"></div>');
            $head.append(svgIcon(icon));
            $head.append($('<span class="vas_092-mLabel"></span>').text(label));
            $c.append($head);

            $c.append($('<div class="vas_092-mVal"></div>').text(value));
            if (sub) $c.append($('<div class="vas_092-mSub"></div>').text(sub));

            if (pct != null) {
                var $bar = $('<div class="vas_092-mBar"><i></i></div>');
                $bar.find("i").css("width", Math.max(0, Math.min(100, pct)) + "%");
                $c.append($bar);
            }
            return $c;
        }

        // ---------- Order Progress (Timeline) ---------- //

        // 7-stage progress. The Partial Received stage relabels to "Received"
        // once the order is fully received so it never reads as partial.
        function progressStages() {
            var recvLabel = data.IsFullyDelivered
                ? getMsg("VAS_092_FullyReceived")
                : getMsg("VAS_092_PartialDelivered");
            // Payment stage: "Payment Completed" (green) once every invoice is
            // fully paid; otherwise "Pending Amount".
            var paymentLabel = data.IsPaymentDone
                ? getMsg("VAS_092_PaymentCompleted")
                : getMsg("VAS_092_PendingAmount");
            // With Vendor reports whether the order has actually gone OUT to the
            // vendor (C_Order.VAS_IsEmailSent) — "Email Sent" or "Pending". It
            // used to repeat the completion date the Completed stage above it
            // already carries, which said nothing about the vendor having it.
            var withVendorMeta = data.IsEmailSent
                ? getMsg("VAS_092_EmailSent")
                : getMsg("VAS_092_Pending");
            return [
                // stamp: true marks a date that is a stored TIMESTAMP (UTC, no
                // zone designator) rather than a document date field, so it is
                // rendered in the viewer's own zone — otherwise a record created
                // late in the local evening reports the following UTC day.
                { key: "VAS_092_Drafted",          done: true,                     active: data.CurrentStage === 1, date: data.Created || data.DateOrdered, stamp: true },
                { key: "VAS_092_Completed",        done: data.IsCompleted,         active: data.CurrentStage === 2, date: data.OrderCompletedDate || data.DateOrdered },
                { key: "VAS_092_WithVendor",       done: data.IsWithVendor,        active: data.CurrentStage === 3, date: data.OrderCompletedDate || data.DateOrdered, meta: withVendorMeta },
                { key: "VAS_092_ExpectedDelivery", done: data.IsExpectedDelivery,  active: data.CurrentStage === 4, date: data.DatePromised, required: true },
                // The receipt stage dates from when the GRN was CREATED (model
                // side), which is a stamp — not the movement date it used to
                // show, which a user can back-date.
                { key: "VAS_092_PartialDelivered", label: recvLabel, done: data.IsPartialDelivered, active: data.CurrentStage === 5, date: data.LastReceiptDate, stamp: true },
                { key: "VAS_092_InvoiceRaised",    done: data.IsInvoiceRaised,     active: data.CurrentStage === 6, date: data.LastInvoiceDate },
                { key: "VAS_092_PaymentDone",      label: paymentLabel, done: data.IsPaymentDone, active: data.CurrentStage === 7, date: data.LastPaymentDate }
            ];
        }

        function renderProgress() {
            var stages = progressStages();
            var st = statusTone(data);

            var $sec = section(getMsg("VAS_092_OrderProgress"), {
                summary: getMsg("VAS_092_Stage") + " " + (data.CurrentStage || 1) +
                    " " + getMsg("VAS_092_Of") + " " + stages.length + " · " + st.label
            });

            var $tl = $('<div class="vas_092-stepper"></div>');
            for (var i = 0; i < stages.length; i++) {
                var s = stages[i];

                // A reached milestone is always green (done), even when it is the
                // current stage — so Invoice Raised / Payment Done turn green as
                // soon as the invoice / payment exists rather than staying orange.
                // The orange "in progress" state is reserved for a current stage
                // that has not yet been reached.
                var stateCls, statusText;
                if (s.done) {
                    stateCls = "vas_092-is-done"; statusText = getMsg("VAS_092_Completed");
                } else if (s.active) {
                    stateCls = "vas_092-is-active"; statusText = getMsg("VAS_092_InProgress");
                } else {
                    stateCls = "vas_092-is-pending"; statusText = getMsg("VAS_092_Pending");
                }

                var dateText = s.stamp ? formatStampDate(s.date) : formatDate(s.date);
                var metaText = statusText;
                if (s.done && dateText) {
                    metaText = s.required
                        ? getMsg("VAS_092_Required") + " " + dateText
                        : dateText;
                }
                // A stage that states its own sub-line (With Vendor) keeps it in
                // every state — the sentence is the point, not the date.
                if (s.meta) metaText = s.meta;

                $tl.append(stepEntry(i + 1, s.label || getMsg(s.key), metaText, s.done, stateCls));
            }
            $sec.append($tl);
        }

        function stepEntry(num, title, meta, done, stateCls) {
            var $entry = $('<div class="vas_092-step"></div>').addClass(stateCls || "");

            var $rail = $('<div class="vas_092-stepRail"></div>');
            $rail.append($('<span class="vas_092-stepLine vas_092-stepLine-l"></span>'));
            var $dot = $('<span class="vas_092-stepDot"></span>');
            if (done) {
                $dot.append(svgIcon("check"));
            } else {
                $dot.text(num);
            }
            $rail.append($dot);
            $rail.append($('<span class="vas_092-stepLine vas_092-stepLine-r"></span>'));
            $entry.append($rail);

            var $lbl = $('<div class="vas_092-stepLabel"></div>');
            $lbl.append($('<div class="vas_092-stepTitle"></div>').text(title));
            if (meta) $lbl.append($('<div class="vas_092-stepMeta"></div>').text(meta));
            $entry.append($lbl);

            return $entry;
        }

        // ---------- Budget (GL budget control / breach) ---------- //

        // Surfaces the platform's budget check result. The section renders only
        // when there is something to flag — the order is marked over budget
        // (C_Order.IsBudgetViolated) or at least one line carries a breach amount
        // (C_OrderLine.BudgetViolationAmount). It shows how far over budget the
        // order is and a per-line breakdown of which lines breached and by how
        // much. Amounts are in the accounting currency the GL budget is kept in.
        function renderBudget() {
            var lines = (data && data.Lines) || [];
            var lineBreaches = [];
            for (var i = 0; i < lines.length; i++) {
                if ((+lines[i].BudgetViolationAmount || 0) > 0) lineBreaches.push(lines[i]);
            }
            var maxBreach = +data.MaxBudgetViolationAmount || 0;
            var breached = data.IsBudgetViolated || maxBreach > 0 || lineBreaches.length > 0;
            if (!breached) return;   // within budget → nothing to surface

            var $sec = section(getMsg("VAS_092_Budget"), {
                summary: getMsg("VAS_092_BudgetBreach")
            });

            // Breach banner: alert icon + "Over budget by <amount>".
            var $card = $('<div class="vas_092-budget is-breach"></div>');
            var $head = $('<div class="vas_092-budgetHead"></div>');
            $head.append(svgIcon("alert"));
            $head.append($('<span class="vas_092-budgetTitle"></span>')
                .text(getMsg("VAS_092_BudgetBreach")));
            $card.append($head);

            if (maxBreach > 0) {
                var $amt = $('<div class="vas_092-budgetAmt"></div>');
                $amt.append($('<span class="vas_092-budgetLbl"></span>')
                    .text(getMsg("VAS_092_OverBudgetBy")));
                $amt.append($('<b></b>').text(formatAmount(maxBreach, data.CurSymbol,
                    data.ISO_Code, data.StdPrecision)));
                $card.append($amt);
            }
            $sec.append($card);

            // Per-line breakdown of the lines that breached budget.
            if (lineBreaches.length) {
                var $tbl = $('<div class="vas_092-table vas_092-budgetTable"></div>');
                var $h = $('<div class="vas_092-tRow vas_092-tHead"></div>');
                $h.append($('<span></span>').text(getMsg("VAS_092_Item")));
                $h.append($('<span class="vas_092-ta-r"></span>').text(getMsg("VAS_092_OverBudgetBy")));
                $tbl.append($h);

                for (var j = 0; j < lineBreaches.length; j++) {
                    var ln = lineBreaches[j];
                    var $tr = $('<div class="vas_092-tRow vas_092-tBody"></div>');

                    var $item = $('<span class="vas_092-itItem"></span>');
                    $item.append($('<div class="vas_092-itName"></div>')
                        .text(ln.ProductName || "").attr("title", ln.ProductName || ""));
                    $item.append($('<div class="vas_092-itSku"></div>')
                        .text(getMsg("VAS_092_Line") + " " + ln.Line));
                    $tr.append($item);

                    $tr.append($('<span class="vas_092-ta-r vas_092-budgetOver"></span>').text(
                        formatAmount(+ln.BudgetViolationAmount || 0, data.CurSymbol,
                            data.ISO_Code, data.StdPrecision)));
                    $tbl.append($tr);
                }
                $sec.append($tbl);
            }

            // Currency clarification note.
            var $note = $('<div class="vas_092-note"></div>');
            $note.append(svgIcon("info"));
            $note.append($('<span></span>').text(getMsg("VAS_092_BudgetNote")));
            $sec.append($note);
        }

        // ---------- Line Items (table) ---------- //

        // Maximum line-item rows shown per page; the table paginates beyond this.
        var LINES_PER_PAGE = 25;

        function renderLines() {
            var lines = (data && data.Lines) || [];
            if (!lines.length) return;

            // Quantities in the entered (selected) UOM, so the summary reads on the
            // same scale as the rows below it.
            var $sec = section(getMsg("VAS_092_LineItems"), {
                summary: (data.LineCount || 0) + " " + getMsg("VAS_092_Items") + " · " +
                    qtyWithUnit(+data.TotalQtyOrdered || 0) + " · " +
                    qtyWithUnit(+data.TotalQtyDelivered || 0) + " " + getMsg("VAS_092_Received")
            });

            var $tbl = $('<div class="vas_092-table vas_092-itTable"></div>');
            $sec.append($tbl);
            paintLinesTable($tbl, lines);
        }

        // (Re)paints the line-items table for the current page. Kept separate from
        // renderLines so the pager can repaint just the table without rebuilding
        // the whole panel. The totals footer always reflects the full order, not
        // the visible page.
        function paintLinesTable($tbl, lines) {
            $tbl.empty();

            var totalPages = Math.ceil(lines.length / LINES_PER_PAGE);
            if (linesPage < 1) linesPage = 1;
            if (linesPage > totalPages) linesPage = totalPages;
            var start = (linesPage - 1) * LINES_PER_PAGE;
            var end = Math.min(start + LINES_PER_PAGE, lines.length);

            // Every numeric column (unit price, quantity, line amount, received)
            // is right-aligned, so the figures line up on one edge down the table
            // and against the totals footer beneath them.
            var $head = $('<div class="vas_092-tRow vas_092-tHead"></div>');
            $head.append($('<span></span>').text(getMsg("VAS_092_Item")));
            $head.append($('<span class="vas_092-ta-r"></span>').text(getMsg("VAS_092_UnitPrice")));
            $head.append($('<span class="vas_092-ta-r"></span>').text(getMsg("VAS_092_Qty")));
            $head.append($('<span></span>').text(getMsg("VAS_092_ExpDelivery")));
            $head.append($('<span class="vas_092-ta-r"></span>').text(getMsg("VAS_092_LineTotal")));
            $head.append($('<span class="vas_092-ta-r"></span>').text(getMsg("VAS_092_Received")));
            // Trailing action column (per-line history toggle) — deliberately
            // unlabelled; the button carries its own tooltip.
            $head.append($('<span></span>'));
            $tbl.append($head);

            // A line's change history is drawn directly beneath that line, in a
            // drawer the row's own History button opens — not in a separate table
            // further down the panel where the reader has to match line numbers up
            // by hand.
            var histByLine = historyByLine();
            for (var i = start; i < end; i++) {
                var ln = lines[i];
                var hist = histByLine[ln.C_OrderLine_ID] || [];
                $tbl.append(buildLineRow(ln, hist));
                if (hist.length) $tbl.append(buildLineHistory(ln, hist));
            }

            // Subtotal is the net-of-tax product amount (GrandTotal − TaxAmt). For
            // a tax-inclusive price list this differs from C_Order.TotalLines
            // (which carries the gross), so the Subtotal + Tax always sum to the
            // grand total for both inclusive and exclusive price lists.
            var $foot = $('<div class="vas_092-tFoot"></div>');
            $foot.append(buildTotalBit(getMsg("VAS_092_Subtotal"),
                formatAmount(+data.SubTotal || 0, data.CurSymbol, data.ISO_Code, data.StdPrecision), false));
            $foot.append(buildTotalBit(getMsg("VAS_092_Tax"),
                formatAmount(+data.TaxAmt || 0, data.CurSymbol, data.ISO_Code, data.StdPrecision), false));
            $foot.append(buildTotalBit(getMsg("VAS_092_GrandTotal"),
                formatAmount(+data.GrandTotal || 0, data.CurSymbol, data.ISO_Code, data.StdPrecision), true));
            $tbl.append($foot);

            if (totalPages > 1) {
                $tbl.append(buildLinesPager($tbl, lines, start, end, totalPages));
            }
        }

        // "Showing a–b of N" on the left, prev / "page of pages" / next on the
        // right. Prev/next repaint the table in place (paintLinesTable).
        function buildLinesPager($tbl, lines, start, end, totalPages) {
            var $pager = $('<div class="vas_092-pager"></div>');

            $pager.append($('<span class="vas_092-pagerInfo"></span>').text(
                getMsg("VAS_092_Showing") + " " + (start + 1) + "–" + end + " " +
                getMsg("VAS_092_Of") + " " + lines.length));

            var $nav = $('<div class="vas_092-pagerNav"></div>');

            var $prev = $('<button type="button" class="vas_092-pgBtn"></button>')
                .attr("aria-label", getMsg("VAS_092_Prev"));
            $prev.append(svgIcon("chevLeft"));
            if (linesPage <= 1) $prev.prop("disabled", true);
            $prev.on("click", function () {
                if (linesPage > 1) { linesPage--; paintLinesTable($tbl, lines); }
            });

            var $next = $('<button type="button" class="vas_092-pgBtn"></button>')
                .attr("aria-label", getMsg("VAS_092_Next"));
            $next.append(svgIcon("chevRight"));
            if (linesPage >= totalPages) $next.prop("disabled", true);
            $next.on("click", function () {
                if (linesPage < totalPages) { linesPage++; paintLinesTable($tbl, lines); }
            });

            $nav.append($prev);
            $nav.append($('<span class="vas_092-pagerLabel"></span>').text(
                linesPage + " " + getMsg("VAS_092_Of") + " " + totalPages));
            $nav.append($next);

            $pager.append($nav);
            return $pager;
        }

        // History rows grouped by the line they belong to (newest first, as the
        // model already ordered them).
        function historyByLine() {
            var map = {};
            var rows = (data && data.History) || [];
            for (var i = 0; i < rows.length; i++) {
                var id = rows[i].C_OrderLine_ID;
                if (!id) continue;
                if (!map[id]) map[id] = [];
                map[id].push(rows[i]);
            }
            return map;
        }

        function buildLineRow(ln, hist) {
            var $tr = $('<div class="vas_092-tRow vas_092-tBody"></div>');

            var $item = $('<span class="vas_092-itItem"></span>');

            // Product name, with the Attribute Set Instance (size / colour / lot /
            // serial ...) following it on the same line — the attribute reads as
            // part of what the product IS, so it sits after the name rather than
            // on a sub-line of its own. Only a real instance is shown: a blank or
            // "--" / "-" placeholder (no M_AttributeSetInstance_ID) is not an
            // attribute. The full text goes on a tooltip so a truncated
            // (ellipsised) name stays readable on hover.
            var $name = $('<div class="vas_092-itName"></div>');
            $name.append($('<span></span>').text(ln.ProductName || ""));
            var asi = (ln.AttributeSetInstance || "").trim();
            var hasAsi = (asi && asi !== "--" && asi !== "-");
            if (hasAsi) {
                $name.append($('<span class="vas_092-itAttr"></span>').text(asi));
            }
            var nameTip = (ln.ProductName || "") + (hasAsi ? " — " + asi : "");
            if (nameTip) $name.attr("title", nameTip);
            $item.append($name);

            // Product search key (no "SKU" prefix) or, failing that, the line note.
            if (ln.ProductValue) {
                $item.append($('<div class="vas_092-itSku"></div>').text(ln.ProductValue));
            } else if (ln.Description) {
                $item.append($('<div class="vas_092-itSku"></div>').text(ln.Description));
            }
            $tr.append($item);

            $tr.append($('<span class="vas_092-ta-r"></span>').text(formatAmount(
                +ln.PriceActual || 0, data.CurSymbol, data.ISO_Code,
                ln.PricePrecision != null ? ln.PricePrecision : data.StdPrecision)));

            // Ordered quantity in the line's entered (selected) UOM
            // (C_OrderLine.QtyEntered), labelled with that unit.
            var qtyText = formatNumber(+ln.QtyEntered || 0, +ln.UOMPrecision || 0);
            if (ln.UOMSymbol) qtyText += " " + ln.UOMSymbol;
            $tr.append($('<span class="vas_092-ta-r"></span>').text(qtyText));

            // Delivery and receipt only mean something for a stockable item. A
            // charge line, or a Service / Resource / Expense product, is never
            // received — both cells stay empty rather than claiming "not received".
            var deliverable = isDeliverableLine(ln);

            // A cell with nothing to report is left blank — no placeholder dash.
            var $exp = $('<span class="vas_092-expDate"></span>');
            if (deliverable) {
                $exp.append(document.createTextNode(formatDate(ln.DatePromised)));
                $exp.append($('<small></small>').text(recvLabel(ln.RecvState)));
            }
            $tr.append($exp);

            $tr.append($('<span class="vas_092-ta-r"></span>').text(formatAmount(
                +ln.LineNetAmt || 0, data.CurSymbol, data.ISO_Code, data.StdPrecision)));

            // Received — the figure on its own line with the progress bar stacked
            // beneath it (the VAS_099 GRN Received column), so the number shares a
            // baseline and right edge with the Qty column instead of being pushed
            // inward by an inline bar. It is the quantity received in the line's
            // ENTERED UOM, the same scale and unit the Qty cell shows; the tooltip
            // spells the pair out in full.
            var $recv = $('<span class="vas_092-recv vas_092-ta-r"></span>');
            if (deliverable) {
                var ordered  = +ln.QtyEntered || 0;
                var received = +ln.QtyReceivedEntered || 0;
                var prec = +ln.UOMPrecision || 0;
                var pct = ordered > 0 ? Math.round((received / ordered) * 100) : 0;
                $recv.addClass("vas_092-" + (ln.RecvState || "none"));
                $recv.append($('<span class="vas_092-recvVal"></span>')
                    .text(formatNumber(received, prec)));
                var $bar = $('<span class="vas_092-recvBar"><i></i></span>');
                $bar.find("i").css("width", Math.max(0, Math.min(100, pct)) + "%");
                $recv.append($bar);
                var recvTip = formatNumber(received, prec) + " " + getMsg("VAS_092_Of") +
                              " " + formatNumber(ordered, prec);
                if (ln.UOMSymbol) recvTip += " " + ln.UOMSymbol;
                $recv.attr("title", recvTip);
            }
            $tr.append($recv);

            // Trailing action column, right-hand edge of the row. Only a line that
            // was actually edited carries the toggle — an untouched order shows an
            // empty cell there, so every row keeps the same grid.
            var $act = $('<span class="vas_092-itAct"></span>');
            if (hist && hist.length) $act.append(buildHistToggle(ln, hist));
            $tr.append($act);

            return $tr;
        }

        // The per-line affordance: an icon button at the right-hand end of the row
        // that opens the drawer sitting immediately beneath it. Icon-only keeps the
        // action column narrow, so the tooltip (and aria-label) carries the meaning
        // and the change count.
        function buildHistToggle(ln, hist) {
            var open = !!lineHistOpen[ln.C_OrderLine_ID];

            var $b = $('<button type="button" class="vas_092-histBtn"></button>')
                .attr("aria-expanded", open ? "true" : "false")
                .attr("title", histToggleLabel(open, hist.length))
                .attr("aria-label", histToggleLabel(open, hist.length));
            $b.append(svgIcon("history"));
            if (open) $b.addClass("vas_092-is-open");

            $b.on("click", function (e) {
                e.preventDefault();
                e.stopPropagation();
                var nowOpen = !lineHistOpen[ln.C_OrderLine_ID];
                lineHistOpen[ln.C_OrderLine_ID] = nowOpen;
                $b.toggleClass("vas_092-is-open", nowOpen)
                  .attr("aria-expanded", nowOpen ? "true" : "false")
                  .attr("title", histToggleLabel(nowOpen, hist.length))
                  .attr("aria-label", histToggleLabel(nowOpen, hist.length));
                // The drawer is this row's next sibling — no id plumbing needed.
                $b.closest(".vas_092-tBody").next(".vas_092-lineHist").toggle(nowOpen);
            });

            return $b;
        }

        function histToggleLabel(open, count) {
            return open ? getMsg("VAS_092_HideHistory")
                        : getMsg("VAS_092_ShowHistory") + " (" + count + ")";
        }

        // The drawer itself: the prior versions of this one line, newest first.
        // Rendered collapsed unless this line was left open.
        //
        // It reuses the line-items table's own row classes, so every version sits
        // on the SAME six columns in the SAME order as the line above it — the
        // first cell carries the change timestamp in place of the item (the item
        // is the line it hangs under), then Unit Price, Qty, Exp. delivery, Line
        // Amount and Received exactly as the line renders them.
        function buildLineHistory(ln, rows) {
            var $wrap = $('<div class="vas_092-lineHist"></div>');
            if (!lineHistOpen[ln.C_OrderLine_ID]) $wrap.hide();

            var $tbl = $('<div class="vas_092-table vas_092-itTable vas_092-lhTable"></div>');

            var $h = $('<div class="vas_092-tRow vas_092-tHead"></div>');
            $h.append($('<span></span>').text(getMsg("VAS_092_ChangedOn")));
            $h.append($('<span class="vas_092-ta-r"></span>').text(getMsg("VAS_092_UnitPrice")));
            $h.append($('<span class="vas_092-ta-r"></span>').text(getMsg("VAS_092_Qty")));
            $h.append($('<span></span>').text(getMsg("VAS_092_ExpDelivery")));
            $h.append($('<span class="vas_092-ta-r"></span>').text(getMsg("VAS_092_LineTotal")));
            // Who made the change, in the track the line's Received bar occupies —
            // right-aligned like that column so the two sit directly in line.
            $h.append($('<span class="vas_092-ta-r"></span>').text(getMsg("VAS_092_UpdatedBy")));
            $tbl.append($h);

            for (var i = 0; i < rows.length; i++) {
                $tbl.append(buildLineHistoryRow(ln, rows[i]));
            }

            $wrap.append($tbl);
            return $wrap;
        }

        function buildLineHistoryRow(ln, h) {
            var $r = $('<div class="vas_092-tRow vas_092-tBody"></div>');

            // Local system time (formatDateTime converts the UTC-stored value),
            // plus the note that version carried when it differs from now.
            var $when = $('<span class="vas_092-lhWhen"></span>');
            $when.append(document.createTextNode(formatDateTime(h.ChangedOn)));
            if (h.Description && h.Description !== ln.Description) {
                $when.append($('<small></small>').text(h.Description).attr("title", h.Description));
            }
            $r.append($when);

            $r.append($('<span class="vas_092-ta-r"></span>').text(formatAmount(
                +h.PriceActual || 0, data.CurSymbol, data.ISO_Code, h.StdPrecision)));

            var qty = formatNumber(+h.QtyEntered || 0, +h.UOMPrecision || 0);
            if (h.UOMSymbol) qty += " " + h.UOMSymbol;
            $r.append($('<span class="vas_092-ta-r"></span>').text(qty));

            // Same gate as the line above it: a charge / service line is never
            // goods-received, so a promised date is meaningless on any version.
            $r.append($('<span></span>').text(
                isDeliverableLine(ln) ? formatDate(h.DatePromised) : ""));

            $r.append($('<span class="vas_092-ta-r"></span>').text(formatAmount(
                +h.LineNetAmt || 0, data.CurSymbol, data.ISO_Code, h.StdPrecision)));

            // Who changed the line (C_OrderLineHistory.UpdatedBy). A snapshot
            // written by a background/platform process can carry no resolvable
            // user; the cell is then left blank.
            var by = h.UpdatedByName || "";
            $r.append($('<span class="vas_092-lhBy vas_092-ta-r"></span>')
                .text(by).attr("title", by));

            return $r;
        }

        function recvLabel(state) {
            if (state === "full") return getMsg("VAS_092_Delivered");
            if (state === "part") return getMsg("VAS_092_Partial");
            return getMsg("VAS_092_Awaiting");
        }

        // A line is deliverable only when it carries an Item-type product
        // (M_Product.ProductType = 'I'). A charge line (C_Charge_ID, no product)
        // and a Service / Resource / Expense product are never goods-received, so
        // an expected-delivery date or a received quantity would be meaningless.
        function isDeliverableLine(ln) {
            if (!ln || !ln.M_Product_ID || ln.C_Charge_ID) return false;
            return ln.ProductType === "I";
        }

        function buildTotalBit(label, value, isGrand) {
            var $bit = $('<span class="vas_092-tf"></span>');
            if (isGrand) $bit.addClass("vas_092-is-grand");
            $bit.append(document.createTextNode(label));
            $bit.append($('<b></b>').text(value));
            return $bit;
        }

        // ---------- Line History (C_OrderLineHistory) ---------- //

        // Prior versions of the order lines the platform snapshots on
        // re-activate / edit. A live line's history is drawn inline, in the drawer
        // under that line — this section only carries what has nowhere to sit:
        // history belonging to lines that were since removed from the order.
        //
        // It stays collapsed by default (secondary, audit-style view). A "Show
        // Details" link in the section header expands it; the open/closed state is
        // remembered per record (historyOpen) until another order is selected.
        function renderHistory() {
            var all = (data && data.History) || [];
            if (!all.length) return;

            var lines = (data && data.Lines) || [];
            var live = {};
            for (var l = 0; l < lines.length; l++) live[lines[l].C_OrderLine_ID] = true;

            var rows = [];
            for (var i = 0; i < all.length; i++) {
                if (!all[i].C_OrderLine_ID || !live[all[i].C_OrderLine_ID]) rows.push(all[i]);
            }
            if (!rows.length) return;

            var $sec = section(getMsg("VAS_092_RemovedLines"), {
                summary: rows.length + " " + getMsg("VAS_092_Changes")
            });

            var $tbl = $('<div class="vas_092-table vas_092-histTable"></div>');

            var $h = $('<div class="vas_092-tRow vas_092-tHead"></div>');
            $h.append($('<span></span>').text(getMsg("VAS_092_Item")));
            $h.append($('<span></span>').text(getMsg("VAS_092_ChangedOn")));
            $h.append($('<span class="vas_092-ta-r"></span>').text(getMsg("VAS_092_Qty")));
            $h.append($('<span class="vas_092-ta-r"></span>').text(getMsg("VAS_092_UnitPrice")));
            $h.append($('<span class="vas_092-ta-r"></span>').text(getMsg("VAS_092_LineTotal")));
            // Last column, same position the per-line history drawer puts it in.
            $h.append($('<span></span>').text(getMsg("VAS_092_UpdatedBy")));
            $tbl.append($h);

            for (var i = 0; i < rows.length; i++) {
                $tbl.append(buildHistoryRow(rows[i]));
            }
            $sec.append($tbl);

            // Collapse toggle in the section header (right side).
            var $toggle = $('<a href="#" class="vas_092-showDetails"></a>');
            var $right = $sec.find(".vas_092-secRight").first();
            if (!$right.length) {
                $right = $('<div class="vas_092-secRight"></div>');
                $sec.find(".vas_092-secHead").first().append($right);
            }
            $right.append($toggle);

            function paintHistory() {
                $tbl.toggle(historyOpen);
                $toggle.text(historyOpen
                    ? getMsg("VAS_092_HideDetails")
                    : getMsg("VAS_092_ShowDetails"));
            }
            $toggle.on("click", function (e) {
                e.preventDefault();
                historyOpen = !historyOpen;
                paintHistory();
            });
            paintHistory();
        }

        function buildHistoryRow(h) {
            var $tr = $('<div class="vas_092-tRow vas_092-tBody"></div>');

            var $item = $('<span class="vas_092-itItem"></span>');
            $item.append($('<div class="vas_092-itName"></div>')
                .text(h.ProductName || "").attr("title", h.ProductName || ""));
            var sub = getMsg("VAS_092_Line") + " " + h.LineNo;
            if (h.Description) sub += " · " + h.Description;
            $item.append($('<div class="vas_092-itSku"></div>').text(sub));
            $tr.append($item);

            // Local system time (formatDateTime converts the UTC-stored value).
            $tr.append($('<span></span>').text(formatDateTime(h.ChangedOn)));
            // Entered-UOM quantity snapshot (C_OrderLineHistory.QtyEntered), named
            // with its unit exactly as the line rows and the drawer show it.
            var histQty = formatNumber(+h.QtyEntered || 0, +h.UOMPrecision || 0);
            if (h.UOMSymbol) histQty += " " + h.UOMSymbol;
            $tr.append($('<span class="vas_092-ta-r"></span>').text(histQty));
            $tr.append($('<span class="vas_092-ta-r"></span>').text(formatAmount(
                +h.PriceActual || 0, data.CurSymbol, data.ISO_Code, h.StdPrecision)));
            $tr.append($('<span class="vas_092-ta-r"></span>').text(formatAmount(
                +h.LineNetAmt || 0, data.CurSymbol, data.ISO_Code, h.StdPrecision)));
            // Who changed the line; a platform/background snapshot leaves no
            // resolvable user, and the cell is then blank.
            var by = h.UpdatedByName || "";
            $tr.append($('<span class="vas_092-lhBy"></span>')
                .text(by).attr("title", by));

            return $tr;
        }

        // ---------- Documents (GRNs / invoices raised from this PO) ---------- //

        // DocStatus code -> label + tone. Codes the platform can return on a
        // receipt / invoice; anything unmapped falls back to a neutral chip.
        var DOC_STATUS = {
            "DR": { key: "VAS_092_StDrafted",     tone: "neutral" },
            "IP": { key: "VAS_092_StInProgress",  tone: "info"    },
            "CO": { key: "VAS_092_StCompleted",   tone: "success" },
            "CL": { key: "VAS_092_StClosed",      tone: "success" },
            "AP": { key: "VAS_092_StApproved",    tone: "success" },
            "NA": { key: "VAS_092_StNotApproved", tone: "warning" },
            "IN": { key: "VAS_092_StInvalid",     tone: "risk"    },
            "WC": { key: "VAS_092_StWaiting",     tone: "info"    },
            "WP": { key: "VAS_092_StWaiting",     tone: "info"    }
        };

        function docStatusPill(code) {
            var s = DOC_STATUS[code];
            return s ? pill(getMsg(s.key), s.tone)
                     : pill(code || getMsg("VAS_092_StUnknown"), "neutral");
        }

        // The GRNs and vendor invoices prepared from this PO. Each row opens the
        // underlying document through the shared openRecord() zoom path.
        function renderDocuments() {
            var rows = (data && data.Documents) || [];
            if (!rows.length) return;

            var $sec = section(getMsg("VAS_092_Documents"), {
                summary: buildDocumentsSummary(rows)
            });

            var $tbl = $('<div class="vas_092-table vas_092-docTable"></div>');

            var $h = $('<div class="vas_092-tRow vas_092-tHead"></div>');
            $h.append($('<span></span>').text(getMsg("VAS_092_Document")));
            $h.append($('<span></span>').text(getMsg("VAS_092_DocDate")));
            $h.append($('<span></span>').text(getMsg("VAS_092_DocStatus")));
            $h.append($('<span class="vas_092-ta-r"></span>').text(getMsg("VAS_092_Amount")));
            $tbl.append($h);

            for (var i = 0; i < rows.length; i++) {
                $tbl.append(buildDocumentRow(rows[i]));
            }

            $sec.append($tbl);
        }

        // "2 GRNs · 1 invoices · 1 payments" — only the kinds actually present
        // are counted.
        function buildDocumentsSummary(rows) {
            var grn = 0, inv = 0, pay = 0;
            for (var i = 0; i < rows.length; i++) {
                if (rows[i].Type === "grn") grn++;
                else if (rows[i].Type === "invoice") inv++;
                else if (rows[i].Type === "payment") pay++;
            }
            var bits = [];
            if (grn) bits.push(grn + " " + getMsg("VAS_092_GRNsCount"));
            if (inv) bits.push(inv + " " + getMsg("VAS_092_InvoicesCount"));
            if (pay) bits.push(pay + " " + getMsg("VAS_092_PaymentsCount"));
            return bits.join(" · ");
        }

        function buildDocumentRow(d) {
            var $tr = $('<div class="vas_092-tRow vas_092-tBody"></div>');

            var canOpen = d.TableName && +d.RecordId > 0;
            if (canOpen) {
                $tr.addClass("vas_092-is-link")
                    .attr("data-open-table", d.TableName)
                    .attr("data-open-id", d.RecordId);
            }

            // Identity: doc number + kind, with the open affordance on the right.
            var $item = $('<span class="vas_092-itItem vas_092-docItem"></span>');
            var docIcon = d.Type === "grn" ? "inbox"
                        : (d.Type === "payment" ? "coins" : "doc");
            $item.append(svgIcon(docIcon));
            var $txt = $('<span class="vas_092-docTxt"></span>');
            $txt.append($('<div class="vas_092-itName"></div>').text(d.DocumentNo || ""));
            var sub;
            if (d.Type === "grn") {
                sub = getMsg("VAS_092_GoodsReceipt");
                if (d.LineCount)
                    sub += " · " + d.LineCount + " " + getMsg("VAS_092_LinesCount");
            } else if (d.Type === "payment") {
                // "AP Payment · Discounted Amount: <DiscountAmt>" — the discount
                // taken on the payment (C_Payment.DiscountAmt).
                sub = getMsg("VAS_092_APPayment") + " · " +
                    getMsg("VAS_092_DiscountedAmount") + ": " +
                    formatAmount(+d.DiscountAmt || 0, data.CurSymbol,
                        data.ISO_Code, data.StdPrecision);
            } else {
                sub = getMsg("VAS_092_VendorInvoice");
                if (d.IsPaid) sub += " · " + getMsg("VAS_092_Paid");
            }
            $txt.append($('<div class="vas_092-itSku"></div>').text(sub));
            $item.append($txt);
            if (canOpen) $item.append(svgIcon("arrowUpRight"));
            $tr.append($item);

            $tr.append($('<span></span>').text(formatDate(d.DocDate)));
            $tr.append($('<span></span>').append(docStatusPill(d.DocStatus)));

            // Amount: invoices show the grand total; GRNs show the total
            // received value (received qty × order-line price).
            var $amt = $('<span class="vas_092-ta-r"></span>');
            if (d.Amount !== null && d.Amount !== undefined) {
                $amt.text(formatAmount(+d.Amount || 0, data.CurSymbol,
                    data.ISO_Code, data.StdPrecision));
            } else {
                $amt.text("");
            }
            $tr.append($amt);

            return $tr;
        }

        // ---------- Landed Cost (table) ---------- //

        var LC_METHODS = {
            "I": { key: "VAS_092_ByValue",    tone: "info"    },
            "Q": { key: "VAS_092_ByQuantity", tone: "success" },
            "W": { key: "VAS_092_ByWeight",   tone: "purple"  },
            "V": { key: "VAS_092_ByVolume",   tone: "warning" },
            "L": { key: "VAS_092_Equally",    tone: "neutral" },
            "C": { key: "VAS_092_ByCosts",    tone: "neutral" }
        };

        // The distribution method's label for one component. The server sends the
        // dictionary's own name for the value stored on the row
        // (C_ExpectedCost.LandedCostDistribution), so a renamed, translated or
        // newly added method reads exactly as it does on the record screen.
        // LC_METHODS is only the fallback for a deployment whose reference list
        // could not be read; the raw code is the last resort.
        function methodLabel(c) {
            if (c && c.DistributionName) return c.DistributionName;
            var code = c ? c.DistributionCode : null;
            var m = LC_METHODS[code];
            if (m) return getMsg(m.key);
            return code ? code : getMsg("VAS_092_NotSet");
        }

        function methodTone(code) {
            var m = LC_METHODS[code];
            return m ? m.tone : "neutral";
        }

        var LC_PER_PAGE = 10;

        // One row per cost component, followed (when available) by the per-line
        // distribution breakdown (C_ExpectedCostDistribution) showing how much of
        // that component was distributed onto each order line.
        function renderLandedCost() {
            var comps = (data && data.LandedCostComponents) || [];
            if (!comps.length) return;

            var $sec = section(getMsg("VAS_092_LandedCost"), {
                summary: buildLandedSummary(comps)
            });

            var $tbl = $('<div class="vas_092-table vas_092-ldTable"></div>');
            $sec.append($tbl);
            paintLandedTable($tbl, comps);

            $sec.append(buildLandedNote(comps));
        }

        // (Re)paints the landed-cost table for the current page. Kept separate from
        // renderLandedCost so the pager can repaint just this table without
        // rebuilding the panel. The totals footer always reflects every component,
        // not the visible page — same rule as the line-items table.
        function paintLandedTable($tbl, comps) {
            $tbl.empty();

            var totalPages = Math.max(1, Math.ceil(comps.length / LC_PER_PAGE));
            if (lcPage < 1) lcPage = 1;
            if (lcPage > totalPages) lcPage = totalPages;
            var start = (lcPage - 1) * LC_PER_PAGE;
            var end = Math.min(start + LC_PER_PAGE, comps.length);

            var $h = $('<div class="vas_092-tRow vas_092-tHead"></div>');
            // Each column explains itself on hover — "expected vs actual vs
            // variance" is the part of this table people read differently.
            $h.append($('<span></span>').text(getMsg("VAS_092_CostComponent"))
                .attr("title", getMsg("VAS_092_TipComponent")));
            $h.append($('<span></span>').text(getMsg("VAS_092_DistributionMethod")));
                //.attr("title", getMsg("VAS_092_TipMethod")));
            $h.append($('<span class="vas_092-ta-r"></span>').text(getMsg("VAS_092_Expected"))
                .attr("title", getMsg("VAS_092_TipExpected")));
            $h.append($('<span class="vas_092-ta-r"></span>').text(getMsg("VAS_092_Actual"))
                .attr("title", getMsg("VAS_092_TipActual")));
            $h.append($('<span class="vas_092-ta-r"></span>').text(getMsg("VAS_092_Variance"))
                .attr("title", getMsg("VAS_092_TipVariance")));
            $tbl.append($h);

            for (var i = start; i < end; i++) {
                $tbl.append(buildComponentRow(comps[i]));
                var $dist = buildDistRows(comps[i]);
                if ($dist) $tbl.append($dist);
            }

            $tbl.append(buildLandedFooter());

            if (totalPages > 1) {
                $tbl.append(buildLandedPager($tbl, comps, start, end, totalPages));
            }
        }

        // "Showing a–b of N" on the left, prev / "page of pages" / next on the
        // right. Prev/next repaint the table in place (paintLandedTable), the same
        // control the line-items table uses.
        function buildLandedPager($tbl, comps, start, end, totalPages) {
            var $pager = $('<div class="vas_092-pager"></div>');

            $pager.append($('<span class="vas_092-pagerInfo"></span>').text(
                getMsg("VAS_092_Showing") + " " + (start + 1) + "–" + end + " " +
                getMsg("VAS_092_Of") + " " + comps.length));

            var $nav = $('<div class="vas_092-pagerNav"></div>');

            var $prev = $('<button type="button" class="vas_092-pgBtn"></button>')
                .attr("aria-label", getMsg("VAS_092_Prev"))
                .attr("title", getMsg("VAS_092_Prev"));
            $prev.append(svgIcon("chevLeft"));
            if (lcPage <= 1) $prev.prop("disabled", true);
            $prev.on("click", function () {
                if (lcPage > 1) { lcPage--; paintLandedTable($tbl, comps); }
            });

            var $next = $('<button type="button" class="vas_092-pgBtn"></button>')
                .attr("aria-label", getMsg("VAS_092_Next"))
                .attr("title", getMsg("VAS_092_Next"));
            $next.append(svgIcon("chevRight"));
            if (lcPage >= totalPages) $next.prop("disabled", true);
            $next.on("click", function () {
                if (lcPage < totalPages) { lcPage++; paintLandedTable($tbl, comps); }
            });

            $nav.append($prev);
            $nav.append($('<span class="vas_092-pagerLabel"></span>').text(
                lcPage + " " + getMsg("VAS_092_Of") + " " + totalPages));
            $nav.append($next);

            $pager.append($nav);
            return $pager;
        }

        function buildLandedSummary(comps) {
            if (!comps.length) return "";
            var seen = {};
            for (var i = 0; i < comps.length; i++) {
                seen[methodLabel(comps[i])] = true;
            }
            var methods = [];
            for (var k in seen) { if (seen.hasOwnProperty(k)) methods.push(k); }

            var count = comps.length + " " + getMsg("VAS_092_Components");
            if (methods.length === 1) {
                return count + " · " + getMsg("VAS_092_Basis") + ": " + methods[0];
            }
            return count + " · " + getMsg("VAS_092_MixedBasis");
        }

        function buildComponentRow(c) {
            var $tr = $('<div class="vas_092-tRow vas_092-tBody"></div>');

            var $name = $('<span class="vas_092-itItem"></span>');
            var compName = c.ComponentName || getMsg("VAS_092_LandedCost");
            $name.append($('<div class="vas_092-itName"></div>')
                .text(compName).attr("title", compName));
            if (c.SourceLabel) {
                $name.append($('<div class="vas_092-itSku"></div>').text(c.SourceLabel));
            }
            $tr.append($name);

            // The method name comes from the dictionary, so it can be longer than
            // the two-word labels this column used to carry — the pill truncates
            // inside its track and the cell's tooltip carries the full name.
            var method = methodLabel(c);
            $tr.append($('<span class="vas_092-ldMethod"></span>')
              //  .attr("title", method + " — " + getMsg("VAS_092_TipMethod"))
                .append(pill(method, methodTone(c.DistributionCode))));

            $tr.append($('<span class="vas_092-ta-r vas_092-ldExp"></span>')
                .attr("title", getMsg("VAS_092_TipExpected"))
                .text(formatAmount(+c.ExpectedAmt || 0, data.CurSymbol, data.ISO_Code, data.StdPrecision)));

            var $act = $('<span class="vas_092-ta-r vas_092-ldAct"></span>');
            if (c.IsInvoiced) {
                // The tooltip names the invoice the actual came from, so the figure
                // can be traced without leaving the panel.
                var src = [];
                if (c.InvoiceNo) src.push(c.InvoiceNo);
                if (c.InvoiceReference) src.push(c.InvoiceReference);
                var invoiced = formatDate(c.LatestInvoiceDate);
                if (invoiced) src.push(invoiced);
                $act.attr("title", src.length
                    ? getMsg("VAS_092_Invoiced") + ": " + src.join(" · ")
                    : getMsg("VAS_092_TipActual"));
                $act.append($('<span class="vas_092-ldAmt"></span>').text(
                    formatAmount(+c.ActualAmt || 0, data.CurSymbol, data.ISO_Code, data.StdPrecision)));
                $act.append($('<span class="vas_092-ldFlag vas_092-inv"></span>')
                    .text(getMsg("VAS_092_Invoiced")));
            } else {
                $act.addClass("vas_092-is-pending");
                $act.attr("title", getMsg("VAS_092_TipAwaiting"));
                $act.append($('<span class="vas_092-ldAmt"></span>').text(""));
                $act.append($('<span class="vas_092-ldFlag vas_092-wait"></span>')
                    .text(getMsg("VAS_092_AwaitingInvoice")));
            }
            $tr.append($act);

            $tr.append(buildVarianceCell(c));
            return $tr;
        }

        // Per-line distribution breakdown for a component: a full-width block of
        // "Line N · label → distributed amount" rows.
        function buildDistRows(c) {
            var lines = (c && c.DistributionLines) || [];
            if (!lines.length) return null;

            var $wrap = $('<div class="vas_092-ldDist"></div>');
            $wrap.append($('<div class="vas_092-ldDistCap"></div>').text(getMsg("VAS_092_DistributedAcross")));
            for (var i = 0; i < lines.length; i++) {
                var l = lines[i];
                var $row = $('<div class="vas_092-ldDistRow"></div>');
                $row.append($('<span class="vas_092-ldDistItem"></span>')
                    .text(getMsg("VAS_092_Line") + " " + l.LineNo + " · " + (l.LineLabel || "")));
                $row.append($('<span class="vas_092-ldDistAmt"></span>').text(
                    formatAmount(+l.Amt || 0, data.CurSymbol, data.ISO_Code, data.StdPrecision)));
                $wrap.append($row);
            }
            return $wrap;
        }

        function buildVarianceCell(c) {
            var $v = $('<span class="vas_092-ta-r vas_092-ldVar"></span>');
            var amt = formatAmount(Math.abs(+c.VarianceAmt || 0),
                data.CurSymbol, data.ISO_Code, data.StdPrecision);
            // The sign is easy to misread, so the tooltip spells the direction out.
            if (c.VarianceStatus === "over") {
                $v.addClass("vas_092-over").text("+" + amt)
                  .attr("title", getMsg("VAS_092_TipOver") + " " + amt);
            } else if (c.VarianceStatus === "under") {
                $v.addClass("vas_092-under").text("−" + amt)
                  .attr("title", getMsg("VAS_092_TipUnder") + " " + amt);
            } else if (c.VarianceStatus === "on_budget") {
                $v.addClass("vas_092-flat").text(getMsg("VAS_092_OnBudget"))
                  .attr("title", getMsg("VAS_092_TipOnBudget"));
            } else {
                $v.addClass("vas_092-flat").text("")
                  .attr("title", getMsg("VAS_092_TipVariance"));
            }
            return $v;
        }

        function buildLandedFooter() {
            var $foot = $('<div class="vas_092-tFoot vas_092-ldFoot"></div>');
            $foot.append(buildLandedTotal(getMsg("VAS_092_ExpectedLandedCost"),
                formatAmount(+data.ExpectedLandedCost || 0, data.CurSymbol, data.ISO_Code, data.StdPrecision),
                false, false));
            $foot.append(buildLandedTotal(getMsg("VAS_092_ActualToDate"),
                formatAmount(+data.ActualToDate || 0, data.CurSymbol, data.ISO_Code, data.StdPrecision),
                false, false));
            $foot.append(buildLandedTotal(getMsg("VAS_092_OpenNotInvoiced"),
                formatAmount(+data.OpenNotInvoiced || 0, data.CurSymbol, data.ISO_Code, data.StdPrecision),
                false, true));
            $foot.append(buildLandedTotal(getMsg("VAS_092_LandedValue"),
                formatAmount(+data.LandedValue || 0, data.CurSymbol, data.ISO_Code, data.StdPrecision),
                true, false));
            return $foot;
        }

        function buildLandedTotal(label, value, isGrand, isWarn) {
            var $bit = $('<span class="vas_092-tf vas_092-lf"></span>');
            if (isGrand) $bit.addClass("vas_092-is-grand");
            $bit.append(document.createTextNode(label));
            var $b = $('<b></b>').text(value);
            if (isWarn) $b.addClass("vas_092-warn");
            $bit.append($b);
            return $bit;
        }

        function buildLandedNote(comps) {
            var $note = $('<div class="vas_092-note"></div>');
            $note.append(svgIcon("info"));

            var invoiced = data.InvoicedComponentCount || 0;
            var total = data.LandedComponentCount || comps.length;
            var text = getMsg("VAS_092_LandedMethodology") + " " +
                invoiced + " " + getMsg("VAS_092_Of") + " " + total + " " +
                getMsg("VAS_092_ComponentsInvoiced") + ".";
            $note.append($('<span></span>').text(text));
            return $note;
        }

        // ---------- Bottom (Notes stacked above Activity) ---------- //

        // Notes and Activity render one below the other (Notes then Activity),
        // each full width.
        function renderBottom() {
            var notes = (data && data.Notes) || [];
            var activity = (data && data.Activity) || [];
            if (!notes.length && !activity.length) return;

            var $stack = $('<div class="vas_092-bottom"></div>');
            $body.append($stack);

            if (notes.length) renderNotes($stack, notes);
            // E-mails are not given a section of their own: the activity feed
            // already lists them (type "email", with the same subject / recipient
            // row and click-to-open body), so a separate Emails section repeated
            // the same records twice in the panel.
            if (activity.length) renderActivity($stack, activity);
        }

        // ---------- Notes ---------- //

        // Order header note + per-line notes, mirroring the Sales Order overview.
        function renderNotes($parent, notes) {
            var $sec = section(getMsg("VAS_092_Notes"), {
                summary: notes.length + " " + getMsg("VAS_092_NotesCount")
            }, $parent);
            var $card = $('<div class="vas_092-textCard"></div>');
            for (var i = 0; i < notes.length; i++) {
                var t = (notes[i].Text || "").trim();
                if (t) $card.append($('<p></p>').text(t));
            }
            $sec.append($card);
        }

        // ---------- Recent Activity (typed feed) ---------- //

        // Document-lifecycle types (prepared / completed / reactivated / ... )
        // headline with the workflow node's own name, so titleKey stays null and
        // activityTitle falls through to a.Text.
        var ACT_TYPES = {
            note:        { tone: "info",    icon: "mail",   tagKey: "VAS_092_TagNote",        titleKey: null },
            email:       { tone: "purple",  icon: "mail",   tagKey: "VAS_092_TagEmail",       titleKey: null },
            grn:         { tone: "success", icon: "inbox",  tagKey: "VAS_092_TagGRN",         titleKey: "VAS_092_ActGRN" },
            invoice:     { tone: "info",    icon: "doc",    tagKey: "VAS_092_TagInvoice",     titleKey: "VAS_092_ActInvoice" },
            payment:     { tone: "success", icon: "coins",  tagKey: "VAS_092_TagPayment",     titleKey: "VAS_092_ActPayment" },
            approval:    { tone: "purple",  icon: "check",  tagKey: "VAS_092_TagApproval",    titleKey: "VAS_092_ActApproval" },
            created:     { tone: "neutral", icon: "doc",    tagKey: "VAS_092_TagCreated",     titleKey: "VAS_092_ActCreated" },
            prepared:    { tone: "neutral", icon: "doc",    tagKey: "VAS_092_TagPrepared",    titleKey: null },
            completed:   { tone: "success", icon: "check",  tagKey: "VAS_092_TagCompleted",   titleKey: null },
            reactivated: { tone: "warning", icon: "pencil", tagKey: "VAS_092_TagReactivated", titleKey: null },
            rejected:    { tone: "risk",    icon: "alert",  tagKey: "VAS_092_TagRejected",    titleKey: null },
            voided:      { tone: "risk",    icon: "alert",  tagKey: "VAS_092_TagVoided",      titleKey: null },
            reversed:    { tone: "risk",    icon: "alert",  tagKey: "VAS_092_TagReversed",    titleKey: null },
            closed:      { tone: "neutral", icon: "check",  tagKey: "VAS_092_TagClosed",      titleKey: null },
            invalidated: { tone: "warning", icon: "alert",  tagKey: "VAS_092_TagInvalidated", titleKey: null },
            updated:     { tone: "info",    icon: "pencil", tagKey: "VAS_092_TagUpdated",     titleKey: "VAS_092_ActUpdated" },
            // The correspondence and engagement sources shared with every other
            // overview panel (model side, VAS_ActivitySourcesModel): meetings and
            // tasks from AppointmentsInfo, calls from VA048_CallDetails, and the
            // inbound letters MailAttachment1 files under AttachmentType 'I'.
            //
            // titleKey null on all four: each headlines with its OWN subject, note
            // or title, falling back to what its tag says it is — a call with no
            // note reads "Call".
            appointment: { tone: "info",    icon: "calendar", tagKey: "VAS_092_TagAppointment", titleKey: null },
            task:        { tone: "warning", icon: "check",    tagKey: "VAS_092_TagTask",        titleKey: null },
            call:        { tone: "success", icon: "phone",    tagKey: "VAS_092_TagCall",        titleKey: null },
            letter:      { tone: "purple",  icon: "mail",     tagKey: "VAS_092_TagLetter",      titleKey: null }
        };

        // Maximum activity rows shown per page; the feed paginates beyond this.
        // A long-running order accumulates every mail, receipt, invoice and status
        // change, and an unpaged feed made the panel scroll past everything below
        // it. The section summary still counts the WHOLE feed, not the page.
        var ACTIVITY_PER_PAGE = 15;

        function renderActivity($parent, activity) {
            var $sec = section(getMsg("VAS_092_RecentActivity"), {
                summary: activity.length + " " + getMsg("VAS_092_Updates")
            }, $parent);

            var $card = $('<div class="vas_092-actList"></div>');
            $sec.append($card);
            paintActivityList($card, activity);
        }

        // (Re)paints the activity feed for the current page. Kept separate from
        // renderActivity so the pager can repaint just the list, the same split
        // the line-items and landed-cost tables use.
        function paintActivityList($card, activity) {
            $card.empty();

            var totalPages = Math.max(1, Math.ceil(activity.length / ACTIVITY_PER_PAGE));
            if (activityPage < 1) activityPage = 1;
            if (activityPage > totalPages) activityPage = totalPages;
            var start = (activityPage - 1) * ACTIVITY_PER_PAGE;
            var end = Math.min(start + ACTIVITY_PER_PAGE, activity.length);

            for (var i = start; i < end; i++) {
                var a = activity[i];
                $card.append(activityRow(a));
                // An e-mail's body is heavy — it stays collapsed under its row and
                // opens only when the reader asks for it.
                var $mail = activityBody(a);
                if ($mail) $card.append($mail);
            }

            // A feed that fits on one page carries no controls at all.
            if (totalPages > 1) {
                $card.append(buildActivityPager($card, activity, start, end, totalPages));
            }
        }

        // "Showing a–b of N" on the left, prev / "page of pages" / next on the
        // right. Prev/next repaint the list in place (paintActivityList), the same
        // control the line-items and landed-cost tables use.
        function buildActivityPager($card, activity, start, end, totalPages) {
            var $pager = $('<div class="vas_092-pager"></div>');

            $pager.append($('<span class="vas_092-pagerInfo"></span>').text(
                getMsg("VAS_092_Showing") + " " + (start + 1) + "–" + end + " " +
                getMsg("VAS_092_Of") + " " + activity.length));

            var $nav = $('<div class="vas_092-pagerNav"></div>');

            var $prev = $('<button type="button" class="vas_092-pgBtn"></button>')
                .attr("aria-label", getMsg("VAS_092_Prev"))
                .attr("title", getMsg("VAS_092_Prev"));
            $prev.append(svgIcon("chevLeft"));
            if (activityPage <= 1) $prev.prop("disabled", true);
            $prev.on("click", function () {
                if (activityPage > 1) { activityPage--; paintActivityList($card, activity); }
            });

            var $next = $('<button type="button" class="vas_092-pgBtn"></button>')
                .attr("aria-label", getMsg("VAS_092_Next"))
                .attr("title", getMsg("VAS_092_Next"));
            $next.append(svgIcon("chevRight"));
            if (activityPage >= totalPages) $next.prop("disabled", true);
            $next.on("click", function () {
                if (activityPage < totalPages) { activityPage++; paintActivityList($card, activity); }
            });

            $nav.append($prev);
            $nav.append($('<span class="vas_092-pagerLabel"></span>').text(
                activityPage + " " + getMsg("VAS_092_Of") + " " + totalPages));
            $nav.append($next);

            $pager.append($nav);
            return $pager;
        }

        // "was X → now Y" under the field's name, for a field-level edit. A value
        // the log recorded as empty reads as an em dash rather than as a blank, so
        // a cleared field is visibly cleared instead of looking like a rendering
        // gap. Follows VAS_101 / VAS_104.
        // A changed DATE arrives as yyyy-MM-dd (model side, which drops the time
        // the change log stores against a date field). It is rendered in the
        // reader's own locale here, like every other date on the panel.
        //
        // The parts are read out and handed to the Date constructor as LOCAL
        // numbers rather than parsed from the string: JavaScript reads a bare
        // "2026-08-20" as UTC midnight, which renders as the 19th anywhere west of
        // Greenwich — the day-rollover this panel's date handling exists to avoid.
        var ISO_DATE = /^(\d{4})-(\d{2})-(\d{2})$/;
        function changeValueText(value) {
            var s = (value === null || value === undefined) ? "" : String(value);
            var m = ISO_DATE.exec(s);
            if (!m) return s;
            var d = new Date(+m[1], +m[2] - 1, +m[3]);
            if (isNaN(d.getTime())) return s;
            try {
                return d.toLocaleDateString(window.navigator.language,
                    { year: "numeric", month: "short", day: "2-digit" });
            } catch (e) { return s; }
        }

        function changeDelta(a) {
            var $d = $('<small class="vas_092-actSub vas_092-actDelta"></small>');
            var blank = "—";
            var oldTxt = changeValueText(a.OldValue) || blank;
            var newTxt = changeValueText(a.NewValue) || blank;
            $d.append($('<span class="vas_092-cvOld"></span>').text(oldTxt));
            $d.append($('<span class="vas_092-cvArrow"></span>').text("→"));
            $d.append($('<span class="vas_092-cvNew"></span>').text(newTxt));
            $d.attr("title", oldTxt + " → " + newTxt);
            return $d;
        }

        function activityRow(a) {
            var meta = ACT_TYPES[a.Type] || ACT_TYPES.note;

            var $row = $('<div class="vas_092-actRow"></div>');
            $row.append(activityTag(meta));

            // Every activity row shares one symmetric layout: tag | title |
            // right-aligned timestamp · author. For a note/chat the title is the
            // comment text; a title tooltip keeps a long comment fully readable.
            var title = activityTitle(a, meta);
            var $title = $('<span class="vas_092-actTitle"></span>');
            $title.append($('<span class="vas_092-actLead"></span>')
                .text(title).attr("title", title));

            // An e-mail names its recipients under the subject — every address on
            // the To, Cc and Bcc lists, in full. No tooltip: the line is no
            // longer an abridgement of something the reader has to hover to see.
            // ... and so does a LETTER, which is the same record filed under a
            // different attachment type.
            if (a.Type === "email" || a.Type === "letter") {
                var to = recipientSummary(a);
                if (to) {
                    $title.append($('<small class="vas_092-actSub"></small>').text(to));
                }
            }

            // A call names the number it reached, where one was recorded — the
            // same slot answering the same question.
            if (a.Type === "call" && a.MailTo) {
                $title.append($('<small class="vas_092-actSub"></small>')
                    .text(a.MailTo).attr("title", a.MailTo));
            }

            // A meeting or task names where it is and whether it has been dealt
            // with; a cancelled one says so rather than reading as still open.
            if (a.Type === "appointment" || a.Type === "task") {
                var apptBits = [];
                if (a.Location) apptBits.push(a.Location);
                if (a.IsCancelled) apptBits.push(getMsg("VAS_092_ActCancelled"));
                else if (a.IsClosed) apptBits.push(getMsg("VAS_092_ActCompleted"));
                // What was e-mailed about this meeting or task. The count only —
                // the addresses, subjects and bodies are in the drawer, and a
                // meeting that generated several notices would otherwise push
                // everything else off the sub-line.
                var apptMails = activityMails(a);
                if (apptMails.length) apptBits.push(mailCountLabel(apptMails.length));
                if (apptBits.length) {
                    var apptSub = apptBits.join(" · ");
                    $title.append($('<small class="vas_092-actSub"></small>')
                        .text(apptSub).attr("title", apptSub));
                }
            }

            // A field edit names the record it landed on — a LINE edit says which
            // line, on the sub-line the e-mail recipients use — and then the move
            // itself. The headline stays "Updated <field>": which field moved is the
            // question, and both of these qualify it rather than competing with it
            // for the one line that clips.
            if (a.Type === "updated") {
                if (a.ChangeScope) {
                    $title.append($('<small class="vas_092-actSub"></small>')
                        .text(a.ChangeScope).attr("title", a.ChangeScope));
                }
                if (a.OldValue || a.NewValue) $title.append(changeDelta(a));
            }
            $row.append($title);

            var when = formatDateTime(a.Created);
            if (a.UserName) when += " · " + a.UserName;
            $row.append($('<span class="vas_092-actWhen"></span>').text(when));

            // Rows carrying a body are clickable; the caret shows the state.
            if (hasActivityBody(a)) {
                // A meeting or task opens onto the e-mails sent about it; every
                // other openable row onto its own message.
                var isAppt = (a.Type === "appointment" || a.Type === "task");
                var showHint = getMsg(isAppt ? "VAS_092_ShowMails" : "VAS_092_ShowMailBody");
                var hideHint = getMsg(isAppt ? "VAS_092_HideMails" : "VAS_092_HideMailBody");

                $row.addClass("vas_092-is-openable");
                $row.attr("title", showHint);
                var $caret = $('<span class="vas_092-actCaret"></span>').append(svgIcon("chevRight"));
                $row.append($caret);
                $row.on("click", function () {
                    var $panel = $row.next(".vas_092-actBody");
                    if (!$panel.length) return;
                    var nowOpen = !$row.hasClass("vas_092-is-open");
                    $row.toggleClass("vas_092-is-open", nowOpen)
                        .attr("title", nowOpen ? hideHint : showHint);
                    $panel.toggle(nowOpen);
                });
            }
            return $row;
        }

        // What opens on click. A letter opens like a mail: it is the same record
        // in the same table, filed under a different attachment type, with the
        // same body and the same addresses on it. A meeting or task opens onto
        // the e-mails sent against it instead.
        function hasActivityBody(a) {
            if (!a) return false;
            if (a.Type === "email" || a.Type === "letter") {
                return !!(a.Body && String(a.Body).trim());
            }
            if (a.Type === "appointment" || a.Type === "task") {
                return activityMails(a).length > 0;
            }
            return false;
        }

        // The e-mails sent against an appointment or task (MailAttachment1 keyed
        // on AppointmentsInfo). Always an array, so callers can count and loop
        // without guarding.
        function activityMails(a) {
            return (a && a.Mails && a.Mails.length) ? a.Mails : [];
        }

        function mailCountLabel(n) {
            return n + " " + (n === 1 ? getMsg("VAS_092_Email")
                                      : getMsg("VAS_092_Emails"));
        }

        // The e-mail body, collapsed beneath its activity row. The full recipient
        // set (From / To / Cc / Bcc) heads it, so every address the mail went to
        // is on screen once the reader opens the message.
        function activityBody(a) {
            if (!hasActivityBody(a)) return null;

            var $panel = $('<div class="vas_092-actBody" style="display:none;"></div>');

            // An appointment or task opens onto the e-mails sent about it, each
            // with its own recipient, subject, moment and sender. They are listed
            // newest first (model order).
            if (a.Type === "appointment" || a.Type === "task") {
                var mails = activityMails(a);
                for (var i = 0; i < mails.length; i++) {
                    $panel.append(activityMailEntry(mails[i], i > 0));
                }
                return $panel;
            }

            appendMailMeta($panel, "VAS_092_MailFrom", a.MailFrom);
            appendMailMeta($panel, "VAS_092_MailTo",   a.MailTo);
            appendMailMeta($panel, "VAS_092_MailCc",   a.MailCc);
            appendMailMeta($panel, "VAS_092_MailBcc",  a.MailBcc);
            $panel.append($('<p></p>').text(String(a.Body).trim()));
            return $panel;
        }

        // One e-mail inside an appointment's or task's drawer: who it went to and
        // what it was about, then when and by whom, then the message. Separated
        // from the one before it so several notices do not read as one.
        function activityMailEntry(m, separated) {
            var $wrap = $('<div class="vas_092-actMailItem"></div>');
            if (separated) $wrap.addClass("vas_092-actMailSplit");

            appendMailMeta($wrap, "VAS_092_MailTo", m.MailTo);
            appendMailMeta($wrap, "VAS_092_MailSubject",
                (m.Subject && String(m.Subject).trim())
                    ? m.Subject : getMsg("VAS_092_NoSubject"));

            // "when · who", the same two parts in the same order as the row above
            // it.
            var when = formatDateTime(m.SentOn);
            if (m.SentBy) when = when ? when + " · " + m.SentBy : m.SentBy;
            if (when) $wrap.append($('<div class="vas_092-actMeta"></div>').text(when));

            // The body is the thing the click was for; a mail filed without one
            // still shows its envelope rather than an empty gap.
            if (m.Body && String(m.Body).trim()) {
                $wrap.append($('<p></p>').text(String(m.Body).trim()));
            }
            return $wrap;
        }

        function appendMailMeta($panel, key, value) {
            if (!value || !String(value).trim()) return;
            $panel.append($('<div class="vas_092-actMeta"></div>')
                .text(getMsg(key) + " " + String(value).trim()));
        }

        // Row sub-line: the To list, plus "+n more" covering the Cc / Bcc
        // addresses. Counting by comma / semicolon is enough for a summary — the
        // body lists the addresses verbatim.
        // Every address the mail went to, written out in full and labelled: To,
        // then Cc, then Bcc. It used to name the To list and count the rest as
        // "+n more", which could only be resolved by opening the message — and a
        // mail stored without a body cannot be opened at all. Ported from VAS_099.
        function recipientSummary(a) {
            var bits = [];
            appendAddressBit(bits, "VAS_092_MailTo",  a.MailTo);
            appendAddressBit(bits, "VAS_092_MailCc",  a.MailCc);
            appendAddressBit(bits, "VAS_092_MailBcc", a.MailBcc);
            return bits.join(" · ");
        }

        function appendAddressBit(bits, key, value) {
            var text = (value === null || value === undefined) ? "" : String(value).trim();
            if (!text) return;
            bits.push(getMsg(key) + " " + text);
        }

        // allRecipients (the row's hover tooltip) and countAddresses (the "+n
        // more" tally) are gone with the abridged sub-line they served: the row
        // now writes every address out, so there is nothing left to count or to
        // recover on hover.

        function activityTag(meta) {
            var $t = $('<span class="vas_092-actTag"></span>').addClass("vas_092-tone-" + meta.tone);
            if (meta.icon) $t.append(svgIcon(meta.icon));
            $t.append($('<span></span>').text(getMsg(meta.tagKey)));
            return $t;
        }

        function activityTitle(a, meta) {
            // Free-text types (note / e-mail) headline with their own text; an
            // untitled one falls back to what its tag says it is.
            if (!meta.titleKey) return a.Text || getMsg(meta.tagKey);

            // A field-level edit headlines with the FIELD that changed — the row's
            // tag already says "Updated", and the field is what tells one edit
            // apart from the next. Rows with no field (change logging off) keep
            // the generic wording.
            if (a.Type === "updated" && a.FieldName) {
                return getMsg("VAS_092_ActFieldUpdated") + " " + a.FieldName;
            }

            var s = getMsg(meta.titleKey);
            if (a.Type === "grn" && a.Count > 0) {
                s += " · " + a.Count + " " + getMsg("VAS_092_Lines");
            }
            if (a.DocumentNo) s += " (" + a.DocumentNo + ")";
            return s;
        }

        // ----------------------------------------------------------------- //
        //  Events / record navigation                                        //
        // ----------------------------------------------------------------- //

        function bindEvents() {
            // Open a linked origin record from a Generated From chip.
            $root.on("click", ".vas_092-chip.vas_092-is-link, .vas_092-is-link[data-open-table]", function (e) {
                e.preventDefault();
                openRecord($(this).attr("data-open-table"), $(this).attr("data-open-id"),
                    $(this).attr("data-open-sotrx") === "Y");
            });
        }

        // Tables whose record does NOT open in the table's default zoom window,
        // mapped to the name of the window it does open — the RFQ, Project and
        // Requisition chips each open a named screen that is not what their
        // table's zoom target resolves to, so the id comes from this name
        // instead. Any further screen that needs naming belongs here — nothing
        // else has to change.
        // M_InOut, C_Invoice and C_Payment are dual-purpose (shipment / receipt,
        // AR / AP, receipt / payment), but every one of them this panel opens is
        // a goods receipt, a vendor invoice or an AP payment against this
        // purchase order, so all three can be named outright.
        // C_Order appears here on its PURCHASE side only, which in this panel is
        // the Blanket Order chip — the sales side is named separately below.
        var WINDOW_NAME_BY_TABLE = {
            "C_RfQ":              "VAS_RFQ",
            "C_Project":          "VAS_Project",
            "M_Requisition":      "VAS_Requisition",
            "M_InOut":            "VAS_MaterialReceipt",
            "C_Invoice":          "VAS_APInvoice",
            "C_Payment":          "VAS_APPayment",
            "VAS_ContractMaster": "VAS_ContractMaster",
            "C_Order":            "VAS_BlanketPurchaseOrder"
        };

        // The same map for records opened as a SALES transaction. C_Order serves
        // both sides — the Sales Order origin chip opens it with IsSOTrx, the
        // Blanket Order chip without — so each side names its own window and this
        // one wins when the flag is set.
        var WINDOW_NAME_BY_TABLE_SOTRX = {
            "C_Order": "VAS_SalesOrder"
        };

        // Window name -> AD_Window_ID, resolved once per name and remembered for
        // the life of the panel. A name the dictionary does not know is cached as
        // -1 so a failed lookup is not repeated on every click.
        var windowIdByName = {};

        // Resolves a window id from its name through the panel's own endpoint.
        // Returns 0 when it cannot be resolved, which leaves openRecord() to fall
        // back to the table's zoom target.
        function resolveWindowIdByName(windowName) {
            if (!windowName) return 0;
            if (windowIdByName.hasOwnProperty(windowName)) {
                return windowIdByName[windowName] > 0 ? windowIdByName[windowName] : 0;
            }
            try {
                if (!(window.VIS && VIS.dataContext &&
                      typeof VIS.dataContext.getJSONRecord === "function")) {
                    return 0;
                }
                var id = VIS.dataContext.getJSONRecord(
                    "VAS_092_OverviewPurchaseOrder/GetWindow_ID", windowName);
                id = parseInt(id, 10);
                if (isNaN(id) || id <= 0) {
                    windowIdByName[windowName] = -1;
                    console.log("resolveWindowIdByName: no window named " + windowName);
                    return 0;
                }
                windowIdByName[windowName] = id;
                return id;
            } catch (e) {
                windowIdByName[windowName] = -1;
                console.log(e);
                return 0;
            }
        }

        // Open the record's window filtered to that row: the window named for this
        // table when it has one, else the table's default zoom target (the
        // VAS_105_AccountRightPanel pattern). Either way the window is started
        // with an equal-query on the table's key column (TableName_ID). Degrades
        // to a toast so a click never throws.
        function openRecord(tableName, recordId, isSOTrx) {
            if (!tableName || !recordId || +recordId <= 0 || !window.VIS) return;
            try {
                // A sales-transaction record takes its own name where the table
                // has one; everything else takes the plain mapping.
                var windowName = (isSOTrx && WINDOW_NAME_BY_TABLE_SOTRX[tableName])
                    ? WINDOW_NAME_BY_TABLE_SOTRX[tableName]
                    : WINDOW_NAME_BY_TABLE[tableName];
                var windowId = resolveWindowIdByName(windowName);

                if (windowId <= 0 &&
                    VIS.ZoomTarget && typeof VIS.ZoomTarget.getZoomAD_Window_ID === "function") {
                    // The 4th arg (IsSOTrx) picks the sales vs purchase window for
                    // dual-purpose tables like C_Order — true opens the Sales Order
                    // window, false the Purchase Order window.
                    windowId = VIS.ZoomTarget.getZoomAD_Window_ID(tableName, 0, null, !!isSOTrx) || 0;
                }
                if (windowId > 0 && VIS.viewManager && typeof VIS.viewManager.startWindow === "function") {
                    var zoomQuery = VIS.Query.prototype.getEqualQuery(tableName + "_ID", +recordId);
                    VIS.viewManager.startWindow(windowId, zoomQuery);
                    return;
                }
            } catch (e) { console.log(e); }
            toast(getMsg("VAS_092_OpenRecord") + " " + tableName + " #" + recordId, false);
        }

        function toast(message, isError) {
            var $t = $('<div class="vas_092-toast"></div>').addClass(isError ? "vas_092-err" : "vas_092-ok").text(message);
            $root.append($t);
            setTimeout(function () { $t.addClass("vas_092-show"); }, 10);
            setTimeout(function () { $t.removeClass("vas_092-show"); setTimeout(function () { $t.remove(); }, 300); }, 3200);
        }

        // ----------------------------------------------------------------- //
        //  Icon helpers                                                      //
        // ----------------------------------------------------------------- //

        var SVG_ICONS = {
            pin:      '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M20 10c0 6-8 12-8 12s-8-6-8-12a8 8 0 0 1 16 0Z"/><circle cx="12" cy="10" r="3"/></svg>',
            user:     '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"/><circle cx="12" cy="7" r="4"/></svg>',
            phone:    '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M22 16.92v3a2 2 0 0 1-2.18 2 19.79 19.79 0 0 1-8.63-3.07 19.5 19.5 0 0 1-6-6 19.79 19.79 0 0 1-3.07-8.67A2 2 0 0 1 4.11 2h3a2 2 0 0 1 2 1.72c.13.96.36 1.9.7 2.81a2 2 0 0 1-.45 2.11L8.09 9.91a16 16 0 0 0 6 6l1.27-1.27a2 2 0 0 1 2.11-.45c.91.34 1.85.57 2.81.7A2 2 0 0 1 22 16.92Z"/></svg>',
            mail:     '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="5" width="18" height="14" rx="2"/><path d="m3 7 9 6 9-6"/></svg>',
            box:      '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M21 8 12 3 3 8v8l9 5 9-5Z"/><path d="m3 8 9 5 9-5"/><path d="M12 13v8"/></svg>',
            coins:    '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><circle cx="8" cy="8" r="5"/><path d="M14.5 4.2a5 5 0 0 1 0 15.6"/><path d="M7 18.7a5 5 0 0 0 6 0"/></svg>',
            info:     '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"/><path d="M12 16v-4"/><path d="M12 8h.01"/></svg>',
            check:    '<svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"><path d="M20 6 9 17l-5-5"/></svg>',
            chevUp:   '<svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round"><path d="m18 15-6-6-6 6"/></svg>',
            doc:      '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8Z"/><path d="M14 2v6h6"/><path d="M8 13h8"/><path d="M8 17h8"/></svg>',
            clipboardCheck: '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><rect x="8" y="2" width="8" height="4" rx="1"/><path d="M16 4h2a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2h2"/><path d="m9 14 2 2 4-4"/></svg>',
            factory:  '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M2 20V9l6 4V9l6 4V9l6 4v7Z"/><path d="M2 20h20"/><path d="M7 20v-4"/><path d="M12 20v-4"/><path d="M17 20v-4"/></svg>',
            pencil:   '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M12 20h9"/><path d="M16.5 3.5a2.12 2.12 0 0 1 3 3L7 19l-4 1 1-4Z"/></svg>',
            calendar: '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="4" width="18" height="18" rx="2"/><path d="M16 2v4"/><path d="M8 2v4"/><path d="M3 10h18"/></svg>',
            inbox:    '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M22 12h-6l-2 3h-4l-2-3H2"/><path d="M5.45 5.11 2 12v6a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-6l-3.45-6.89A2 2 0 0 0 16.76 4H7.24a2 2 0 0 0-1.79 1.11Z"/></svg>',
            arrowUpRight: '<svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M7 17 17 7M7 7h10v10"/></svg>',
            chevLeft: '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round"><polyline points="15 18 9 12 15 6"/></svg>',
            chevRight: '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round"><polyline points="9 18 15 12 9 6"/></svg>',
            alert: '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M10.29 3.86 1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0Z"/><path d="M12 9v4"/><path d="M12 17h.01"/></svg>',
            send:     '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M22 2 11 13"/><path d="M22 2l-7 20-4-9-9-4Z"/></svg>',
            history: '<svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M3 12a9 9 0 1 0 3-6.7L3 8"/><path d="M3 3v5h5"/><path d="M12 7v5l3 2"/></svg>'
        };

        function svgIcon(name) {
            var $wrap = $('<span class="vas_092-ic"></span>');
            $wrap[0].innerHTML = SVG_ICONS[name] || "";
            return $wrap;
        }

        // ----------------------------------------------------------------- //
        //  Formatting helpers                                                //
        // ----------------------------------------------------------------- //

        function formatNumber(value, precision) {
            var p = (precision >= 0) ? precision : 0;
            return (+value || 0).toLocaleString(window.navigator.language, {
                minimumFractionDigits: p,
                maximumFractionDigits: p
            });
        }

        // An order-level quantity, in the entered (selected) UOM the server
        // reports the totals in. Decimals are shown only when the unit has them
        // and the value needs them — a whole number is never padded with zeros.
        function formatQty(value) {
            var p = (data && +data.QtyPrecision > 0) ? +data.QtyPrecision : 0;
            return (+value || 0).toLocaleString(window.navigator.language, {
                minimumFractionDigits: 0,
                maximumFractionDigits: p
            });
        }

        // The same quantity, named with its unit: the order's own UOM symbol when
        // every item line shares one, else the generic "units" — a mixed-UOM order
        // must not label a summed figure with one line's unit.
        function qtyWithUnit(value) {
            return formatQty(value) + " " +
                ((data && data.QtyUOMSymbol) ? data.QtyUOMSymbol : getMsg("VAS_092_Units"));
        }

        function formatAmount(value, symbol, iso, precision) {
            var sign = value < 0 ? "-" : "";
            var abs = Math.abs(value);
            var cur = symbol || iso || "";
            var p = (precision >= 0) ? precision : 2;
            var formatted = abs.toLocaleString(window.navigator.language, {
                minimumFractionDigits: p,
                maximumFractionDigits: p
            });
            return sign + (cur ? cur + " " : "") + formatted;
        }

        // Parses a .NET/Newtonsoft DB value into a Date.
        //
        // asUtc = true  → for genuine *timestamps* (Created, activity, history,
        //   completion time). The DB stores these in UTC and Newtonsoft emits no
        //   timezone designator (e.g. "2026-07-01T10:00:00"), which the browser
        //   would otherwise read as local. We tag it "Z" so toLocale* renders it
        //   in the viewer's own zone.
        // asUtc = false → for *date-only* fields (Ordered / Promised / Invoice /
        //   receipt dates). These carry no meaningful time-of-day, so we parse the
        //   wall-clock value as-is and never shift it — the calendar day shown
        //   always matches the day stored, regardless of the viewer's zone.
        // Strings that already carry a "Z" or ±hh:mm offset are left untouched.
        function parseDbDate(value, asUtc) {
            if (!value) return null;
            if (value instanceof Date) return isNaN(value.getTime()) ? null : value;
            var s = String(value);
            var hasTz = /(z|[+-]\d{2}:?\d{2})$/i.test(s);
            var isDateTime = /^\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}/.test(s);
            if (asUtc && isDateTime && !hasTz) {
                s = s.replace(" ", "T") + "Z";
            } else if (!asUtc && isDateTime) {
                // Keep the calendar date: drop any timezone marker and parse the
                // date/time as local so no zone conversion can roll the day over.
                s = s.replace(" ", "T").replace(/(z|[+-]\d{2}:?\d{2})$/i, "");
            }
            var d = new Date(s);
            return isNaN(d.getTime()) ? null : d;
        }

        function formatDate(value) {
            var d = parseDbDate(value, false);
            if (!d) return "";
            try {
                return d.toLocaleDateString(window.navigator.language, {
                    year: "numeric", month: "short", day: "2-digit"
                });
            } catch (e) {
                return d.toDateString();
            }
        }

        // The calendar day of a stored TIMESTAMP, in the viewer's own zone. The
        // DB keeps these in UTC and the server emits no zone designator, so
        // reading one with formatDate (which deliberately does not shift a
        // date-only field) would print the UTC day — a receipt entered at 9pm
        // local would date to the following morning.
        function formatStampDate(value) {
            var d = parseDbDate(value, true);
            if (!d) return "";
            try {
                return d.toLocaleDateString(window.navigator.language, {
                    year: "numeric", month: "short", day: "2-digit"
                });
            } catch (e) {
                return d.toDateString();
            }
        }

        function formatDateTime(value) {
            var d = parseDbDate(value, true);
            if (!d) return "";
            try {
                var datePart = d.toLocaleDateString(window.navigator.language, {
                    month: "short", day: "2-digit"
                });
                var timePart = d.toLocaleTimeString(window.navigator.language, {
                    hour: "2-digit", minute: "2-digit"
                });
                return datePart + ", " + timePart;
            } catch (e) {
                return d.toString();
            }
        }

        this.getRoot = function () {
            return $root;
        };
    };

    VAS.VAS_092_OverviewPurchaseOrder.prototype.startPanel = function (windowNo, curTab) {
        this.windowNo = windowNo;
        this.curTab = curTab;
        if (curTab && typeof curTab.getAD_Table_ID === "function") {
            this.table_ID = curTab.getAD_Table_ID();
        }
        // Cached for the share flow's fallback, so printContext() still resolves
        // the window on a build whose tab does not expose the getter.
        if (curTab && typeof curTab.getAD_Window_ID === "function") {
            this.AD_Window_ID = curTab.getAD_Window_ID();
        }
        this.init();
        // Watch the tab itself so New Record (which never calls refreshPanelData)
        // still empties the panel.
        if (curTab && typeof curTab.addDataStatusListener === "function") {
            try { curTab.addDataStatusListener(this.tabDataListener); } catch (e) { }
        }
    };

    /* Update tab panel based on selected record */
    VAS.VAS_092_OverviewPurchaseOrder.prototype.refreshPanelData = function (recordID, selectedRow) {
        // The insert check is what makes Copy Record behave: the id we are handed
        // for a copied row is the SOURCE order's, so without it the panel would
        // reload the copied-from record here even after the data-status listener
        // had just cleared it.
        if (selectedRow == undefined || recordID <= 0 || isTabInserting(this.curTab)) {
            this.record_ID = 0;
            this.clear();
            return;
        }
        this.record_ID = recordID;
        this.selectedRow = selectedRow;
        // Held rather than fetched outright: the insert flag is not always up
        // yet when we get here, so scheduleFetch asks once more before loading.
        this.scheduleFetch(recordID);
    };

    /* Set width as per window width */
    VAS.VAS_092_OverviewPurchaseOrder.prototype.sizeChanged = function (width) {
        this.panelWidth = width;
    };

    /* Release variables from memory */
    VAS.VAS_092_OverviewPurchaseOrder.prototype.dispose = function () {
        // Kill any held fetch first — its timer would otherwise fire against a
        // panel whose curTab has just been nulled out below.
        if (typeof this.abortPendingFetch === "function") {
            try { this.abortPendingFetch(); } catch (e) { }
        }
        if (this.curTab && typeof this.curTab.removeDataStatusListener === "function") {
            try { this.curTab.removeDataStatusListener(this.tabDataListener); } catch (e) { }
        }
        this.tabDataListener = null;
        this.record_ID = 0;
        this.table_ID = 0;
        this.windowNo = 0;
        this.curTab = null;
        this.selectedRow = null;
        this.panelWidth = null;
    };

})(VAS, jQuery);
