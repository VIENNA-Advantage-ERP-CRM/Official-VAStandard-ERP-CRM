/************************************************************
 * Module Name    : VAS
 * Purpose        : Purchase Requisition overview tab panel. Renders a
 *                  review-oriented overview of the selected requisition
 *                  (M_Requisition): identity, requester / preparer, origin +
 *                  warehouse route, convert actions, a budget-aware stat strip,
 *                  a 6-stage progress stepper, and a tabbed lower region
 *                  (Items / Activity / Notes). Data is fetched from
 *                  VAS_098_PurchaseRequisition/GetRequisitionOverview.
 *                  Design follows the attached requisition-window reference.
 * Chronological development:
 *   VAI163   2026-07-01  Created (modelled on VAS_092_OverviewPurchaseOrder).
 *   VAI163   2026-07-02  Reworked the header to the VAS_092 pattern: a soft-
 *                        gradient title strip (title + subtitle + priority /
 *                        type / status pills) whose tint follows requisition
 *                        progress, above a white two-column details card with
 *                        the Source Warehouse leading the left column. Replaces
 *                        the former identity card + separate route strip.
 *   VAI163   2026-07-02  Added a "Create RFQ" document-action button beside the
 *                        two conversion actions, and moved every on-screen label
 *                        behind VIS.Msg.getMsg("VAS_098_*") (seed in AD_Message).
 *   VAI163   2026-07-28  - Dropped the requester-warehouse route line from the
 *                          details card's left column; it already reads as a
 *                          labelled field in the right column.
 *                        - People now carry a "People" heading and a visible role
 *                          (Requester / Preparer) beside each name.
 *                        - Progress "Drafted" stage captions with the record's
 *                          creation date instead of the document date.
 *                        - Line items show the Attribute Set Instance sub-line and
 *                          drop the "SKU" prefix before the product search key;
 *                          the full product name is a hover tooltip.
 *                        - Removed the Contingency total from the items footer.
 *                        - msg() takes an optional English fallback so an unseeded
 *                          key never renders as raw "VAS_098_*" text.
 *   VAI163   2026-07-28  - Source availability (card + line column) now reports
 *                          real on-hand stock at the source warehouse; 0 is a
 *                          value, and only a requisition without a source
 *                          warehouse reads N/A.
 *                        - The three convert buttons are functional: each
 *                          confirms, POSTs to the controller, shows the process
 *                          result in a toast and refreshes the panel. Create RFQ
 *                          no longer greys out just because the requisition has
 *                          already been converted to a purchase order.
 *                        - Progress stages are captioned with real dates:
 *                          completion under Submitted / Completed, PO creation
 *                          under Converted, PO completion under In Fulfilment and
 *                          the close under Closed.
 *   VAI163   2026-07-28  - The status pill and the progress header now show the
 *                          requisition's own DocStatus; "Converted" is a progress
 *                          state and no longer masks it.
 *                        - Added a posting-status badge (M_Requisition.Posted).
 *                        - Added a Reference field carrying the purchase order
 *                          raised from this requisition.
 *                        - Timestamps are parsed as wall-clock time so the
 *                          creation time matches the requisition window instead of
 *                          being shifted into the browser's timezone.
 *   VAI163   2026-07-28  - Line items show the UOM in place of the product
 *                          category.
 *                        - Items / Activity / Notes are stacked sections down the
 *                          panel instead of tabs, so Activity and Notes sit at the
 *                          bottom and need no click.
 *                        - Action buttons renamed to the document they produce:
 *                          Material Transfer / RFQ / Purchase Order.
 *                        - The converted note reads "Converted".
 *   VAI163   2026-07-28  - Activity carries the full lifecycle: PO Created, GRN
 *                          Created and GRN Completed, each with document no and
 *                          timestamp.
 *                        - Convert strip wraps inside the panel instead of
 *                          overflowing it; line items page at 10 rows.
 *                        - Priority badge colours match the requisition window.
 *                        - The items footer reports the Budget set for the
 *                          requisition in place of the estimated subtotal.
 *   VAI163   2026-08-05  Class prefix renamed MPC-vasrq- -> vas_098- so the panel's
 *                          styles cannot collide with another panel's.
 *   VAI163   2026-08-05  New Record / Copy Record now empty the panel instead
 *                        of leaving the previously selected record on screen.
 *                        Both refreshPanelData and a new data-status listener
 *                        ask isTabInserting(), which reads GridTab.gridTable
 *                        .getIsInserting() — the flag GridTable.dataNew() raises
 *                        for both actions. The record id cannot answer it: a
 *                        copied row carries the source record's key until saved.
 *                        Ported from VAS_092.
 *   VAI163   2026-08-06  Activity paginates at 15 rows a page (ACTIVITY_PER_PAGE),
 *                        reusing the line-items pager. buildPager() is no longer
 *                        tied to linesPage / LINES_PER_PAGE: it takes the 0-based
 *                        page and an onGo(page) callback, so the items table and
 *                        the activity feed page independently. A feed that fits on
 *                        one page shows no controls, and the section header keeps
 *                        counting the whole feed. Ported from VAS_092.
 *   VAI163   2026-08-07  Emits the vas_098-prefixed modifier classes the
 *                        stylesheet now uses.
 *   VAI163   2026-08-11  - Timestamps render in the viewer's local system zone.
 *                          The July change read them as WALL-CLOCK, on the belief
 *                          that Created is stored in server local time; the DB
 *                          actually stores UTC and the server emits no zone
 *                          designator, so the panel printed the stored UTC clock
 *                          and every creation time read hours out. parseDbDate
 *                          tags a bare timestamp "Z"; a date-only field is still
 *                          parsed as it stands so its calendar day cannot roll
 *                          over, and the day-count arithmetic is unaffected.
 *                        - The progress stepper dates its stages with
 *                          formatStampDateShort: every one of them is a stamp,
 *                          and the date-only formatter would print the UTC day.
 *                        - Line items page at 25 rows instead of 10, matching the
 *                          Purchase Order and Internal Use overviews.
 *                        - Activity carries the e-mails sent against the
 *                          requisition (type "email"): the subject headlines the
 *                          row, the recipient runs underneath it and the
 *                          timestamp / sender sit where every other entry carries
 *                          them. The message body opens on click, headed by the
 *                          full From / To / Cc / Bcc set. Follows VAS_092.
 *                        - The items footer's Budget figure falls back to the
 *                          budget the requisition is drawn against (model side)
 *                          when no line carries a calculated one, and its tooltip
 *                          says which of the two is on screen.
 *   VAI163   2026-08-11  - Minor priority reads green, not grey: grey said "no
 *                          priority set" when what it means is the lowest one
 *                          there is. Every priority still takes its tone from its
 *                          own PriorityRule value (PRIORITY_MAP).
 *                        - The details card drops its Reference field. It named
 *                          the purchase order raised from the requisition, so it
 *                          read N/A on every requisition not yet converted — and
 *                          the conversion is already reported by the progress
 *                          stepper and by Activity's "PO Created" entry, both of
 *                          which name the order.
 *                        - Line items show the Attribute Set Instance directly
 *                          under the product name, above the search key: the
 *                          attribute qualifies WHICH stock was asked for.
 *                        - A charge line's Source Stock cell is blank
 *                          (isChargeLine). A charge is not stocked, so neither
 *                          "0" nor "N/A" is a fact about it; the model leaves such
 *                          a line without source data at all, so it also stops
 *                          counting against the requisition's source availability.
 *                        - Activity moved below Notes, to the bottom of the
 *                          panel: it is the longest section and the least often
 *                          read, so anything under it was pushed off the panel.
 *   VAI163   2026-08-12  - The Purchase Order button confirms a PURCHASE ORDER.
 *                          Its text is keyed ConfirmPurchaseOrder now: the older
 *                          ConfirmConvertToPO is seeded with the RFQ sentence on
 *                          existing databases, so the button asked the reader to
 *                          confirm an RFQ. A fresh key cannot inherit the wrong
 *                          text — the same move the button LABELS made when they
 *                          were renamed.
 *                        - msg() treats VIS.Msg's bracketed "[VAS_098_FOO]"
 *                          answer as not-found (isMissingMsg), so an unseeded key
 *                          reaches its English fallback instead of rendering as
 *                          raw bracketed text. It only tested m !== full, which a
 *                          bracketed answer always passes. Ported from VAS_099.
 *                        - Notes carries every description entered against the
 *                          requisition: the header's, then each LINE's
 *                          (M_RequisitionLine.Description), captioned with its
 *                          line no and product. A line note was previously
 *                          unreachable from the panel — the Items table shows the
 *                          product, its attributes and its search key, not the
 *                          description.
 *                        - The RFQ button greys out once EVERY line has been
 *                          converted into a purchase order (isFullyConverted:
 *                          ConvertedLineCount >= LineCount). It stayed live on a
 *                          fully converted requisition, offering to seek quotes
 *                          for lines already ordered. A PARTLY converted one
 *                          keeps it — the outstanding lines are what an RFQ is
 *                          for — which is why data.IsConverted (true from the
 *                          first converted line) is not the test.
 *                        - Added the Documents section, built like the Purchase
 *                          Order overview's: the purchase orders, RFQs and
 *                          material transfers raised from this requisition, with
 *                          a per-kind count in the section header, each row
 *                          carrying its date, status and amount and opening the
 *                          record on click. Drawn only when there is something to
 *                          list.
 *                        - Added the Reference strip (renderReference), the
 *                          mirror of the PO overview's Generated From and in the
 *                          same place — directly under the details card. One chip
 *                          per source document: field-service work order,
 *                          production order, project, replenishment; each opens
 *                          its record except replenishment, which is a process
 *                          and has none. A requisition generated from nothing
 *                          reads "Manual".
 *                        - Added the record-open path both of those need
 *                          (bindEvents / openRecord / WINDOW_NAME_BY_TABLE /
 *                          resolveWindowIdByName, against the new GetWindow_ID
 *                          endpoint), ported from VAS_092.
 *   VAI163   2026-08-12  - Every Activity row carries "when · by whom" in the
 *                          same place, read from UserName (which the model now
 *                          sets on every row). The actor used to be spliced onto
 *                          the end of a milestone's or a document's own sentence,
 *                          sat beside the timestamp on an e-mail, and was missing
 *                          altogether from a COMMENT — a chat entry showed its
 *                          text and a time with no author. activityText() is the
 *                          action alone now.
 *                        - A comment's headline wraps to three lines rather than
 *                          ellipsising after one (vas_098-multiline): it is the
 *                          comment itself, not a label.
 *                        - Notes is drawn only when the requisition carries a
 *                          DESCRIPTION of its own. Without one the section and
 *                          its heading are absent instead of framing a "no notes"
 *                          placeholder — and a requisition with line notes but no
 *                          description of its own shows no Notes section at all,
 *                          which is the intended behaviour: the header
 *                          description is what decides whether the section
 *                          exists, and the line notes are a detail of it.
 *                        - The progress line's last stage is Posted
 *                          (M_Requisition.Posted, dated by PostedDate) in place
 *                          of Closed. Closing is a state most requisitions never
 *                          reach; posting is one every completed requisition
 *                          does.
 *                          Posted is marked from its own flag and does NOT
 *                          back-fill the stages before it (LIFECYCLE_STAGES):
 *                          posting follows completion, so it is routinely true
 *                          while Converted and In Fulfilment are still ahead, and
 *                          the chain rule would have reported purchase orders
 *                          that do not exist. Closed was safely terminal and
 *                          needed none of that. The "Stage n of m" caption counts
 *                          the chain, which is now five.
 *   VAI163   2026-08-12  - Source Stock is now Received On Hand, and reports the
 *                          warehouse a purchased line is RECEIVED into as well as
 *                          the one an internal line is served from (model side).
 *                          An externally procured requisition has no source
 *                          warehouse, so the column read N/A for the life of the
 *                          document and never reflected the receipt that
 *                          satisfied it; it now rises as each GRN completes. The
 *                          tooltip names the warehouse and the quantity received.
 *                        - Quantity and unit price read in the line's SELECTED
 *                          UOM (model side). The row was already labelled with
 *                          that unit while carrying the product's BASE figures, so
 *                          a line keyed as 2 BOX of an EA-held product read as 24
 *                          against a "BOX" label, priced per EA.
 *                        - The unit price shows to the currency's precision
 *                          (moneyPrecise) instead of being rounded to whole
 *                          currency units — a rate of 0.75 an EA printed as 1.
 *                          Line amounts and totals keep money().
 *                        - Items is drawn only when the requisition HAS lines;
 *                          the empty frame and its "no line items" placeholder
 *                          are gone.
 *   VAI163   2026-08-12  - Removed the RFQ and Material Transfer buttons (and
 *                          isFullyConverted, which only gated the first). Neither
 *                          could ever succeed from this panel, so both answered
 *                          every click with an error: the RFQ process selects its
 *                          lines by ORGANISATION and needs a topic, quote type
 *                          and response date that an overview has no business
 *                          inventing, and the material-transfer process ships
 *                          with the DTD001 module, which is not installed here.
 *                          Both actions live on the requisition window, with the
 *                          parameter screen they need. Purchase Order stays and
 *                          becomes the strip's primary action.
 *                        - The Reference strip gains a Field Service Request chip
 *                          (M_Requisition.VA075_FieldServiceReq_ID), beside the
 *                          work order rather than sharing with it: a request can
 *                          exist without a work order, and one work order can
 *                          serve several.
 *                        - openRecord gains a third and final step: when neither a
 *                          named window nor the client's zoom target resolves, the
 *                          server is asked which window the TABLE opens in
 *                          (GetWindowIdByTable). The VA075 work order and field
 *                          service request chips needed it — that module is not
 *                          part of this solution, so its screens cannot be named
 *                          here, and the browser-side zoom lookup only knows
 *                          tables the client has cached, so both fell through to
 *                          the "cannot open" toast on every click. Ported from
 *                          VAS_102.
 *   VAI163   2026-08-12  - The Reference strip gains the order whose demand the
 *                          requisition serves (M_RequisitionLine.Ref_OrderLine_ID
 *                          -> C_OrderLine -> C_Order). C_Order is dual-purpose, so
 *                          the chip reads Sales Order only when the linked order
 *                          actually is one, and openRecord takes an isSOTrx flag
 *                          again (WINDOW_NAME_BY_TABLE_SOTRX) so each side opens
 *                          in its own window.
 *                        - The Production Order chip also resolves from
 *                          M_RequisitionLine.VAMFG_M_WorkOrderComponent_ID (model
 *                          side): a line raised for one COMPONENT of a production
 *                          order carries the component, not the order.
 *                        - Removed the convert strip entirely — the "conversion
 *                          available" note and the Purchase Order button, with
 *                          convertBtn() and runAction(). Those actions belong to
 *                          the requisition window, behind the parameter screen and
 *                          document validation the process expects, and the note
 *                          restated what the progress stepper already shows. The
 *                          controller's ConvertToPurchaseOrder endpoint went with
 *                          it and the controller is read-only again.
 *                        - Removed the procurement-type header badge ("Internal
 *                          Fulfillment" / "Purchase Requisition"). The header
 *                          sub-line opens with the same words and the details card
 *                          carries them as a labelled field.
 *                        - The Posted badge is drawn only once the record IS
 *                          posted (or posting errored, which the reader has to act
 *                          on). It used to read "Not Posted" on every drafted
 *                          requisition — not news about a document that cannot be
 *                          posted yet.
 *                        - New Record / Copy Record reliably empty the panel, by
 *                          the mechanism VAS_099 needed for the same bug: the
 *                          framework can call refreshPanelData BEFORE GridTable
 *                          raises its insert flag, so the panel loaded the row
 *                          just left; and a reply already on the wire landed after
 *                          the clear and repainted it. refreshPanelData now goes
 *                          through scheduleFetch (holds REFRESH_DELAY_MS and
 *                          re-asks isTabInserting), every fetch carries a token a
 *                          clear or newer fetch invalidates, shownRecordId tracks
 *                          "showing or loading", and clear() drops the busy
 *                          indicator a discarded reply used to strand.
 *   VAI163   2026-08-14  - The progress line drops Submitted. It was dated by the
 *                          COMPLETION stamp — the same moment the stage beside it
 *                          reports — and its condition was satisfied by every
 *                          status Completed is, so the two lit up together and
 *                          read as one event told twice. LIFECYCLE_STAGES falls
 *                          to 4 with it.
 *                        - Activity reports edits FIELD BY FIELD: an "updated" row
 *                          per changed column (model side), headlined "Updated
 *                          <field>" with the line it landed on beneath it and
 *                          "when · by whom" where every other row carries it. The
 *                          feed's only account of a change used to be the single
 *                          "status" milestone, derived from M_Requisition.Updated
 *                          — the LAST save, saying nothing about what it touched.
 *                        - An e-mail's recipient line lists every address on the
 *                          mail (To, Cc and Bcc, each labelled) in full instead of
 *                          naming the To list and counting the rest as "+n more".
 *                          That count could only be resolved by opening the
 *                          message, which a mail stored without a body cannot do.
 *                          allRecipients / countAddresses went with it, as did the
 *                          sub-line's tooltip.
 *   VAI163   2026-08-17  Activity's field-level rows carry the MOVE: "was X →
 *                        now Y" under the field's name (changeDelta), the old
 *                        value struck through and a value the log recorded as
 *                        empty shown as an em dash, so a cleared field is
 *                        visibly cleared rather than looking like a rendering
 *                        gap. A row said WHICH field moved but never what it
 *                        moved from or to.
 *   VAI163   2026-08-20  - The Work Order, Field Service Request and Production
 *                          Order chips take the window they open from the PAYLOAD
 *                          (WorkOrderWindowId / FieldServiceReqWindowId /
 *                          ProductionOrderWindowId, resolved and access-checked on
 *                          the server). Those three belong to modules this
 *                          solution does not ship, so their screens cannot be
 *                          named here and the browser's zoom lookup does not know
 *                          their tables — the panel used to click first and find
 *                          out afterwards, and a click reported an error from a
 *                          window opened with a query it could not run. A chip
 *                          whose window did not resolve is now drawn as plain text
 *                          instead of as a link that fails.
 *                        - openRecord takes that id as its first choice, ahead of
 *                          the named window and the zoom target.
 *                        - The items footer shows a NEGATIVE budget rather than
 *                          reading N/A at it. A budget is a figure, not a
 *                          quantity, and only the absence of one is N/A.
 *                        - The progress line's Posted stage reads Pending while
 *                          the document is unposted, like every other stage still
 *                          ahead. "Not Posted" restated the stage's own label as
 *                          though it were news.
 *                        - A stage that has been REACHED renders as done and shows
 *                          the moment it happened. The last reached stage was
 *                          drawn "active" instead, which replaced its date with a
 *                          forward-looking caption — a converted requisition read
 *                          "Ready to Convert" and never showed the date it was
 *                          converted on. The active marker moves to the first
 *                          stage still ahead, which is the one being worked
 *                          towards.
 *                        - Converted / In Fulfilment answer for every document
 *                          raised from the requisition, not only its purchase
 *                          orders: RFQs, material transfers and inventory-use
 *                          issues count too, with the dates to match (model side).
 *                        - The items table's "Received On Hand" column becomes
 *                          "Received": a progress bar over the received / ordered
 *                          ratio ("8/10"), summed across the goods receipts,
 *                          material transfers and inventory-use issues that
 *                          delivered the line. Green once the line has everything
 *                          it asked for, orange while it is short. The old column
 *                          reported the warehouse's stock POSITION, which moves
 *                          for reasons having nothing to do with the requisition.
 *                          sourceCell -> receivedCell.
 *                        - The Reference strip gains the BLANKET order the
 *                          requisition draws on, opening the blanket sales or
 *                          blanket purchase screen as the record's side requires.
 *                          originChip takes a named window for it — a blanket is a
 *                          C_Order like any other, so nothing about the record
 *                          says which screen it belongs to.
 *                        - Window resolution follows VAS_092's order throughout:
 *                          the window NAMED for the record first, then the zoom
 *                          target, and only then the dictionary's table lookup.
 *                          VAMFG_M_WorkOrder joins WINDOW_NAME_BY_TABLE under the
 *                          name VAS_102 opens it by (VAMFG_ProductionOrder) —
 *                          it went straight to the table lookup, which is the last
 *                          resort for a screen that cannot be named, and the
 *                          production order's can. The two VA075 documents still
 *                          fall through to it, since theirs genuinely cannot.
 *   VAI163   2026-08-21  Activity: a Task or Appointment row now says how many
 *                        e-mails were sent against it, and opens on click onto
 *                        each one - who it went to, its subject, when it went
 *                        and who sent it, then the message itself. The body is
 *                        shown ONLY once the row is opened.
 *   VAI163   2026-08-24  The Reference strip's BLANKET ORDER chip takes its window
 *                        from the payload (data.BlanketOrderWindowId) rather than
 *                        resolving the name for itself. The name is still sent and
 *                        still tried, but only second: the client's own lookup
 *                        falls through to the TABLE's zoom target when it comes
 *                        back empty, and C_Order's zoom target is the ordinary
 *                        order screen — so a blanket SALES order opened the Sales
 *                        Order window filtered to a record that window does not
 *                        carry, instead of VAS_BlanketSalesOrder. A window the
 *                        server could not resolve now leaves the chip as plain
 *                        text, still naming the document, rather than opening the
 *                        wrong screen. Same treatment the VA075 / VAMFG chips
 *                        already had.
 *   VAI163   2026-08-26  - Generated From names a referenced BLANKET order as one.
 *                          A requisition raised against a C_Order carrying
 *                          IsBlanketTrx = 'Y' read "Sales Order", which called a
 *                          standing commitment an ordinary order. The chip follows
 *                          data.RefOrderIsBlanket (refOrderLabel) and carries the
 *                          blanket screen's window with it; an ordinary order is
 *                          left on the client's own lookup, since passing a
 *                          server-resolved 0 would take its link away. The model
 *                          fixes the loader that should have claimed the record for
 *                          the Blanket chip in the first place.
 *                        - Progress: the Completed stage reads Pending while the
 *                          document is Drafted or In Progress, where it read
 *                          "In Progress" on both — a caption that claimed work had
 *                          started on a record nobody had submitted, and that said
 *                          the same thing either way. It is marked done by the
 *                          DOCUMENT STATUS alone now (CO / CL), not also by
 *                          IsConverted, and captions with the completion date; a
 *                          converted requisition still lights it through the
 *                          existing reach rule.
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {

    // True when the tab is sitting on a row that has not been saved yet —
    // whether it came from New Record or from Copy Record.
    //
    // The authority is the GRID TABLE's insert flag: VIS.GridTable.dataNew()
    // raises it for both actions and clears it again on save, refresh or undo,
    // and GridTable.getIsInserting() reads it. GridTab does NOT expose that
    // method — it only holds the table as .gridTable — so asking the tab itself
    // always answers "no".
    //
    // The record id cannot answer this on its own: a copied row carries the
    // SOURCE record's field values, its key included, so the id handed to the
    // panel is the record that was copied FROM. Either way the panel would
    // otherwise show a saved record's details beside an unsaved new one.
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

    VAS.VAS_098_PurchaseRequisition = function () {
        this.record_ID = 0;
        this.table_ID = 0;
        this.windowNo = 0;
        this.curTab = null;
        this.selectedRow = null;
        this.panelWidth;

        var $self = this;

        // The framework notifies a tab panel when the selected record changes
        // (refreshPanelData) but NOT when the user starts a new one:
        // GridController.dataNew() never reaches the tab panel, so the panel
        // would keep showing the previously selected record beside an empty new
        // one. Listening to the tab's own data-status events closes that gap.
        function onTabDataStatus(e) {
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
                // New (unsaved) record — nothing to show against it. Asked of
                // shownRecordId rather than record_ID: a fetch still in flight has
                // already claimed the former, so a New Record raised while the
                // first (slow) request is on the wire still clears — and
                // invalidates the reply that would otherwise repaint it.
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
        var $root;
        var $busy;
        var $body;
        var $emptyState;
        var data = null;

        // The record the panel is SHOWING or LOADING. Distinct from record_ID,
        // which the host sets: this one is claimed the moment a fetch is
        // scheduled, so a New Record raised mid-flight can tell there is something
        // to clear.
        var shownRecordId = 0;

        // How long refreshPanelData holds before it actually fetches.
        // On New Record / Copy Record the framework can call refreshPanelData
        // BEFORE GridTable raises its insert flag, so isTabInserting() asked at
        // that instant still answers "no" and the panel would load the record the
        // user has just moved off — which is exactly what left the previous
        // requisition on screen. Asking again after this pause gets the truth. It
        // also collapses a burst of arrow-key row changes into one request.
        var REFRESH_DELAY_MS = 150;

        // Raised by every fetch, every scheduled fetch and every clear. A reply
        // carrying a token that is no longer the current one belongs to a record
        // the panel has already moved off, so it is dropped instead of painting —
        // without this, the reply of a request already on the wire lands AFTER the
        // clear and repaints the record that was just cleared. Ported from VAS_099.
        var fetchToken = 0;
        var pendingFetch = null;

        // Line items page client-side (the whole set arrives in one payload); the
        // page resets whenever a different record is loaded. 25 rows a page,
        // matching the Purchase Order and Internal Use overviews: the pager only
        // appears once a requisition actually exceeds that.
        var LINES_PER_PAGE = 25;
        var linesPage = 0;
        var activityPage = 0;   // current Activity page (0-based, like linesPage)

        // Placeholder for a document field that carries no value — an RFQ has no
        // amount, an unposted transfer no date. A dash, so an empty cell is never
        // mistaken for a zero.
        var DASH = "—";

        // ---- Code maps: status/priority codes -> message key + tone. Labels
        //      are looked up through VIS.Msg (AD_Message VAS_098_*) at render. ---- //
        var STATUS_MAP = {
            "DR": { key: "Draft",       tone: "vas_098-draft"     },
            "IP": { key: "InProgress",  tone: "vas_098-partial"   },
            "AP": { key: "Approved",    tone: "vas_098-approved"  },
            "CO": { key: "Completed",   tone: "vas_098-approved"  },
            "CL": { key: "Closed",      tone: "vas_098-sent"      },
            "VO": { key: "Voided",      tone: "vas_098-cancelled" },
            "RE": { key: "Reversed",    tone: "vas_098-cancelled" },
            "WC": { key: "WaitingConfirmation", tone: "vas_098-partial" },
            "WP": { key: "WaitingPayment",      tone: "vas_098-partial" },
            "IN": { key: "Invalid",     tone: "vas_098-cancelled" },
            "NA": { key: "NotApproved", tone: "vas_098-cancelled" }
        };
        // Tone per PriorityRule value: urgent red, high orange, medium blue, low
        // and minor green. Urgent and High used to share one tone, and Low shared
        // grey with Minor, so the badge disagreed with the requisition window.
        // Minor is green rather than grey — grey read as "no priority set" when
        // what it means is the lowest one there is.
        var PRIORITY_MAP = {
            "1": { key: "UrgentPriority", tone: "vas_098-urgent" },
            "3": { key: "HighPriority",   tone: "vas_098-high"   },
            "5": { key: "MediumPriority", tone: "vas_098-med"    },
            "7": { key: "LowPriority",    tone: "vas_098-low"    },
            "9": { key: "MinorPriority",  tone: "vas_098-minor"  }
        };

        this.init = function () {
            $root = $('<div class="vas_098-root"></div>');
            $body = $('<div class="vas_098-body"></div>');
            $emptyState = $('<div class="vas_098-empty" style="display:none;"></div>');
            $emptyState.text(msg("NoData"));
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

        // Waits REFRESH_DELAY_MS, re-asks the tab whether it is inserting, and only
        // then fetches. See REFRESH_DELAY_MS for why the wait is needed.
        this.scheduleFetch = function (recordID) {
            invalidateFetch();
            var token = fetchToken;
            // Claim the record now, not when the timer fires: shownRecordId means
            // "showing or loading", and leaving it stale through the wait would let
            // the data-status listener fire a second fetch for the same row.
            shownRecordId = +recordID || 0;
            // Feedback while we hold — clear() / fetchData() own it from here.
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
                url: VIS.Application.contextUrl + "VAS_098_PurchaseRequisition/GetRequisitionOverview",
                type: "GET",
                dataType: "json",
                data: { M_Requisition_ID: recordID },
                success: function (raw) {
                    // Reply for a record the panel has already left (a New Record
                    // cleared it, or a newer row was selected). Whoever superseded
                    // us owns the busy indicator now, so leave it be.
                    if (token !== fetchToken) return;
                    var parsed = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                    data = parsed;
                    linesPage = 0;
                    activityPage = 0;
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

        this.clear = function () {
            // Anything in flight or held belongs to the record being cleared.
            invalidateFetch();
            shownRecordId = 0;
            data = null;
            linesPage = 0;
            activityPage = 0;
            render();
            // A discarded reply would otherwise strand the indicator.
            showBusy(false);
        };

        function render() {
            // Nothing to draw into until startPanel() -> init() has built the DOM.
            if (!$body) return;

            $body.empty();

            if (!data || !data.M_Requisition_ID) {
                $body.hide();
                $emptyState.show();
                return;
            }

            $emptyState.hide();
            $body.show();

            renderHead();
            renderDetails();
            // Where the requisition came from, directly under the details card —
            // the placement the Purchase Order overview gives its own strip.
            renderReference();
            renderStats();
            renderProgress();
            renderLower();
        }

        // ----------------------------------------------------------------- //
        //  Helpers                                                           //
        // ----------------------------------------------------------------- //

        // Localised label lookup. All on-screen text is seeded in AD_Message as
        // VAS_098_<key>, with an optional English fallback for a key that has not
        // been added to the dictionary yet.
        //
        // VIS.Msg does NOT answer an unseeded key with the key itself — it answers
        // with the key bracketed and upper-cased ("[VAS_098_CONFIRMPURCHASEORDER]").
        // That is never equal to the key, so the `m !== full` test alone let the
        // bracketed form straight through and the fallback below was unreachable:
        // an unseeded key rendered as raw bracketed text at the user. A bracketed
        // answer now counts as "not found". Ported from VAS_099 / VAS_190.
        function msg(key, fallback) {
            var full = "VAS_098_" + key;
            try {
                var m = VIS.Msg.getMsg(full);
                if (m && m !== full && !isMissingMsg(m)) return m;
            } catch (e) { }
            return (fallback !== null && fallback !== undefined) ? fallback : full;
        }

        // True for VIS.Msg's "key not seeded" answer: the key, bracketed.
        function isMissingMsg(text) {
            var t = String(text);
            return t.length > 1 && t.charAt(0) === "[" && t.charAt(t.length - 1) === "]";
        }

        // The requisition's own document status. "Converted" is a progress state,
        // not a DocStatus, and it used to be substituted here — which meant a
        // completed requisition stopped reporting that it was Completed once a PO
        // had been raised. Conversion is still visible in its own right: the
        // convert strip states it and the progress stepper has a Converted stage.
        function statusMeta() {
            var m = STATUS_MAP[data.StatusCode];
            if (m) return { label: msg(m.key), tone: m.tone };
            return { label: data.StatusCode || msg("NA"), tone: "vas_098-draft" };
        }

        // Posting status of the record (M_Requisition.Posted). Anything other than
        // 'Y' is "not posted"; 'E' is a posting error and is called out as such.
        function postedMeta() {
            if (data.Posted) return { label: msg("Posted", "Posted"), tone: "vas_098-approved" };
            if (data.PostedCode === "E")
                return { label: msg("PostingError", "Posting Error"), tone: "vas_098-cancelled" };
            return { label: msg("NotPosted", "Not Posted"), tone: "vas_098-draft" };
        }

        function priorityMeta() {
            var m = PRIORITY_MAP[data.PriorityCode];
            if (m) return { label: msg(m.key), tone: m.tone };
            return { label: msg("NormalPriority"), tone: "vas_098-med" };
        }

        function procurementType() {
            return data.SourceWarehouseName ? msg("InternalFulfillment") : msg("PurchaseRequisition");
        }

        // isFullyConverted() is gone with the RFQ button it gated: that was the
        // only caller. The convert strip's remaining action (Purchase Order) is
        // gated by canConvert, which reads the document status and data.IsConverted.

        function tag(label, tone) {
            var $t = $('<span class="vas_098-tag"></span>').addClass(tone || "vas_098-draft");
            $t.append($('<span class="vas_098-dot"></span>'));
            $t.append(document.createTextNode(label));
            return $t;
        }

        // ---------------------------- Head ------------------------------- //

        // VAI163 2026-07-02  Reworked to the VAS_092 header pattern: a title strip
        // (title + subtitle, with the priority / type / status pills on the
        // right).
        // VAI163 2026-07-17  Title strip is now untinted, matching VAS_106; the
        // status is still carried by the pills on the right.
        function renderHead() {
            var st = statusMeta();
            var pm = priorityMeta();

            var $head = $('<div class="vas_098-hdr"></div>');
            var $top = $('<div class="vas_098-hdrTop"></div>');

            var $tl = $('<div class="vas_098-hdrTitleWrap"></div>');
            $tl.append($('<div class="vas_098-hdrTitle"></div>').text(
                msg("Requisition") + " — " + (data.DocumentNo || msg("NA"))));
            var subBits = [procurementType()];
            var raised = formatDate(data.DateDoc || data.Created);
            if (raised) subBits.push(msg("Raised") + " " + raised);
            $tl.append($('<div class="vas_098-hdrSub"></div>').text(subBits.join(" · ")));
            $top.append($tl);

            var $pills = $('<div class="vas_098-hdrPills"></div>');
            $pills.append(priorityPill(pm));
            // The procurement-type chip ("Internal Fulfillment" / "Purchase
            // Requisition") is deliberately absent. It repeated the header
            // sub-line, which already opens with the procurement type, and the
            // details card names it again as a labelled field — three copies of one
            // fact across a single screenful.
            $pills.append(tag(st.label, st.tone));
            // Posted, and ONLY when it is posted. The pill used to be drawn for
            // every record — reading "Not Posted" on every drafted requisition,
            // which is not news about a document that cannot be posted yet. It is a
            // milestone badge now: absent until the milestone is reached, like the
            // Posted stage of the progress line. A posting ERROR still shows, since
            // that is something the reader has to act on.
            if (data.Posted || data.PostedCode === "E") {
                $pills.append(tag(postedMeta().label, postedMeta().tone));
            }
            $top.append($pills);

            $head.append($top);
            $body.append($head);
        }

        function priorityPill(pm) {
            var $p = $('<span class="vas_098-prio"></span>').addClass(pm.tone);
            $p.append(svgIcon("chevrons"));
            $p.append(document.createTextNode(pm.label));
            return $p;
        }

        // --------------------- Header details card ----------------------- //

        // VAI163 2026-07-02  Two-column details card (VAS_092 pattern): the goods
        // source — the Source Warehouse — leads the LEFT column (with the transfer
        // route and the requester / preparer), and labelled meta fields fill the
        // right. Replaces the former identity card + separate route strip. Purpose
        // is no longer surfaced here — it already lives in the Notes tab.
        // VAI163 2026-07-28  Dropped the requester-warehouse route line from the
        // left column: the same value is a labelled field in the right column, so
        // it was showing twice in the card.
        function renderDetails() {
            var $card = $('<div class="vas_098-hdrCard"></div>');

            // Left: Source Warehouse (the source of goods) + route + people.
            var $l = $('<div class="vas_098-hdrColL"></div>');
            $l.append($('<div class="vas_098-fLabel"></div>').text(msg("SourceWarehouse")));
            $l.append($('<div class="vas_098-srcName"></div>').text(
                data.SourceWarehouseName || msg("ExternalProcurement")));
            var cur = (data.ISO_Code || "") + (data.CurSymbol ? " (" + data.CurSymbol + ")" : "");
            if (cur.replace(/\s/g, "")) $l.append(headerField(msg("Currency"), cur));
            // The requester warehouse used to repeat here as a route line; it is
            // already a labelled field in the right column, so this column now
            // carries only the source warehouse, currency and the people.

            // People — a heading plus a visible role on each name, so it is clear
            // who the names belong to (the role used to be a hover title only).
            var $people = $('<div class="vas_098-srcPeople"></div>');
            appendPersonBit($people, data.RequesterName, msg("Requester"));
            appendPersonBit($people, data.PreparerName, msg("Preparer"));
            if ($people.children().length) {
                $l.append($('<div class="vas_098-fLabel vas_098-peopleLbl"></div>')
                    .text(msg("People", "People")));
                $l.append($people);
            }
            $card.append($l);

            // Right: labelled meta fields.
            var $r = $('<div class="vas_098-hdrColR"></div>');
            $r.append(headerField(msg("ProcurementType"), procurementType()));
            $r.append(headerField(msg("PriceList"), data.PriceListName || msg("NA")));
            $r.append(headerField(msg("RequestWarehouse"), data.RequestWarehouseName || msg("NA")));
            // The Reference field is deliberately absent. It named the purchase
            // order raised from this requisition, which reads N/A on every
            // requisition that has not been converted — most of them — and the
            // conversion is already reported by the progress stepper and by the
            // "PO Created" entry in Activity, both of which name the order.
            $card.append($r);

            $body.append($card);
        }

        // Person chip: user icon, the role (Requester / Preparer) and the name.
        // The role is rendered, not just a tooltip, so a single name is never
        // ambiguous about which of the two it is.
        function appendPersonBit($container, value, role) {
            if (!value) return;
            var $bit = $('<span class="vas_098-personBit"></span>').attr("title", role + ": " + value);
            $bit.append(svgIcon("user"));
            $bit.append($('<span class="vas_098-personRole"></span>').text(role));
            $bit.append($('<span class="vas_098-personName"></span>').text(value));
            $container.append($bit);
        }

        // referenceText() is gone with the Reference field it filled. The purchase
        // order the requisition produced is still on screen: the Converted stage
        // of the progress stepper dates it, and Activity's "PO Created" entry
        // names it from its own row (ActivityData.DocumentNo). The payload's
        // OrderDocumentNo / OrderCount are no longer read by this panel; they are
        // left on the model, which uses OrderCount for its own milestone logic.

        // Labelled field block (uppercase caption + value) for the right column.
        function headerField(label, value) {
            var $f = $('<div class="vas_098-hdrField"></div>');
            $f.append($('<div class="vas_098-fLabel"></div>').text(label));
            $f.append($('<div class="vas_098-fVal"></div>').text(value));
            return $f;
        }

        // -------------------------- Reference ---------------------------- //

        // The documents this requisition was GENERATED FROM, in the Purchase Order
        // overview's "Generated From" shape: a labelled strip of chips directly
        // under the details card, one per source document, each opening that
        // record on click.
        //
        // Only origins that exist are drawn. A requisition linked to none of them
        // was raised by hand and says so — "Manual" is a chip like any other, not
        // an apology for an empty strip.
        //
        // Replenishment is the one origin with no record behind it: the Replenish
        // Report raises the requisition and stamps only its description, so that
        // chip names the origin without being a link. Everything else opens.
        function renderReference() {
            var $strip = $('<section class="vas_098-genfrom"></section>');
            $strip.append($('<span class="vas_098-gfLabel"></span>')
                .text(msg("GeneratedFrom", "Generated From")));

            var $chips = $('<div class="vas_098-gfChips"></div>');
            var any = false;

            // Maintenance work order first — it is the strongest origin when more
            // than one is present.
            //
            // The window comes from the payload: VA075 is not part of this
            // solution, so its screen cannot be named here and the browser's zoom
            // lookup does not know its table. A chip whose window the server could
            // not resolve is drawn as plain text — it still names the document,
            // and it cannot fail on click.
            //
            // Drawn on the record's ID, not on its number: the model no longer
            // invents "#1000042" for a document whose identifier column it could
            // not read, so a chip keyed on the number would vanish for exactly
            // those records. It is the LINK that matters — the label says what the
            // document is and the click opens it, with or without a number to show.
            if (data.VA075_WorkOrder_ID > 0) {
                $chips.append(originChip("wrench", msg("WorkOrder", "Work Order"),
                    countedValue(data.WorkOrderNo, data.WorkOrderCount),
                    "warning", "VA075_WorkOrder", data.VA075_WorkOrder_ID, "",
                    data.WorkOrderWindowId));
                any = true;
            }

            // The field service request the requisition was raised against
            // (M_Requisition.VA075_FieldServiceReq_ID). A separate VA075 document
            // to the work order above — a request can exist without one, and one
            // work order can serve several — so it gets its own chip rather than
            // sharing.
            if (data.VA075_FieldServiceReq_ID > 0) {
                $chips.append(originChip("clipboard",
                    msg("FieldServiceRequest", "Field Service Request"),
                    countedValue(data.FieldServiceReqNo, data.FieldServiceReqCount),
                    "info", "VA075_FieldServiceReq", data.VA075_FieldServiceReq_ID, "",
                    data.FieldServiceReqWindowId));
                any = true;
            }

            // Production order (VAMFG) — a manufacturing document, NOT the
            // maintenance work order above. Reached from the line's own work order
            // or from the component it was raised for (model side).
            if (data.VAMFG_M_WorkOrder_ID > 0) {
                $chips.append(originChip("factory",
                    msg("ProductionOrder", "Production Order"),
                    countedValue(data.ProductionOrderNo, data.ProductionOrderCount),
                    "warning", "VAMFG_M_WorkOrder", data.VAMFG_M_WorkOrder_ID, "",
                    data.ProductionOrderWindowId));
                any = true;
            }

            // The BLANKET order the requisition draws on — the standing commitment
            // behind the request, reached from the same reference either directly or
            // through the release it points at (model side).
            //
            // Its window comes from the PAYLOAD, resolved on the server by name and
            // against the role. A blanket order is a C_Order like any other, so
            // nothing about the record itself says it opens the blanket screen
            // rather than the ordinary order one — and a blanket SALES order opens a
            // different one again.
            //
            // The name travels too, but only as the second attempt: it is the
            // server-resolved id that matters here, because the client's own name
            // lookup falls through to the TABLE's zoom target when it comes back
            // empty, and C_Order's zoom target is the ordinary order screen. That
            // fall-through is what stopped a blanket sales order opening — the click
            // started the Sales Order window filtered to a record it does not carry.
            // A window the server could not resolve now leaves the chip as plain
            // text, still naming the document, rather than opening the wrong screen.
            if (data.BlanketOrderNo) {
                var $blanket = originChip("doc",
                    data.BlanketOrderIsSOTrx
                        ? msg("BlanketSalesOrder", "Blanket Sales Order")
                        : msg("BlanketOrder", "Blanket Purchase Order"),
                    countedValue(data.BlanketOrderNo, data.BlanketOrderCount),
                    "success", "C_Order", data.BlanketOrderId, "",
                    data.BlanketOrderWindowId,
                    data.BlanketOrderIsSOTrx ? "VAS_BlanketSalesOrder"
                                             : "VAS_BlanketPurchaseOrder");
                if (data.BlanketOrderIsSOTrx) $blanket.attr("data-open-sotrx", "Y");
                $chips.append($blanket);
                any = true;
            }

            // The order whose demand this requisition serves
            // (M_RequisitionLine.Ref_OrderLine_ID). C_Order carries both sides of
            // the trade, so the chip names the side the linked order is actually
            // on rather than assuming a sale, and opens it in that side's window.
            //
            // A referenced order that is itself a BLANKET is named as one
            // (RefOrderIsBlanket): a standing commitment is not an ordinary order,
            // and the chip carries the blanket screen's window with it, since
            // C_Order's own window cannot show a blanket. The model normally hands
            // that record to the chip above and clears this one — this is what keeps
            // the naming right on a schema where none of its routes can run.
            //
            // The model clears this one when it turns out to be the same record as
            // the blanket above, so the strip never lists one document twice.
            if (data.RefOrderNo) {
                var refValue = countedValue(data.RefOrderNo, data.RefOrderCount);
                var $order;
                if (data.RefOrderIsBlanket) {
                    $order = originChip("doc", refOrderLabel(), refValue,
                        "success", "C_Order", data.RefOrderId, "",
                        data.RefOrderWindowId,
                        data.RefOrderIsSOTrx ? "VAS_BlanketSalesOrder"
                                             : "VAS_BlanketPurchaseOrder");
                } else {
                    // No window argument at all: an ordinary order opens fine
                    // through the client's own lookup, and a server-resolved 0
                    // would strip the chip of its link instead.
                    $order = originChip("doc", refOrderLabel(), refValue,
                        "success", "C_Order", data.RefOrderId);
                }
                if (data.RefOrderIsSOTrx) $order.attr("data-open-sotrx", "Y");
                $chips.append($order);
                any = true;
            }

            // The project whose planned lines were copied into this requisition.
            // Its search key identifies it on the chip and its name sits on the
            // tooltip, so the strip lists identifiers only.
            if (data.ProjectId > 0) {
                $chips.append(originChip("folder", msg("Project", "Project"),
                    countedValue(projectLabel(), data.ProjectCount),
                    "info", "C_Project", data.ProjectId, projectTooltip()));
                any = true;
            }

            // Replenishment — no document to open, so the chip is a plain one.
            if (data.IsReplenishment) {
                $chips.append(originChip("refresh",
                    msg("Replenishment", "Replenishment"), null, "success", null, 0));
                any = true;
            }

            if (!any) {
                $chips.append(originChip("pencil", msg("Manual", "Manual"),
                    null, "muted", null, 0));
            }

            $strip.append($chips);
            $body.append($strip);
        }

        // What the referenced order chip calls itself. A blanket is named as one on
        // both sides of the trade: the label is what tells a reader the requisition
        // draws on a standing commitment rather than on a one-off order, and the
        // two open different screens.
        function refOrderLabel() {
            if (data.RefOrderIsBlanket) {
                return data.RefOrderIsSOTrx
                    ? msg("BlanketSalesOrder", "Blanket Sales Order")
                    : msg("BlanketOrder", "Blanket Purchase Order");
            }
            return data.RefOrderIsSOTrx ? msg("SalesOrder", "Sales Order")
                                        : msg("Order", "Order");
        }

        // "REQ-1 +2" — the first document named, the rest counted, for an origin
        // that several documents feed.
        function countedValue(value, count) {
            if (!value) return "";
            var extra = (+count || 0) - 1;
            return extra > 0 ? (value + " +" + extra) : value;
        }

        // The project chip's value: the project's search key, falling back to its
        // name when the key is blank, so the chip is never a bare label.
        function projectLabel() {
            return ((data.ProjectNo || "").trim()) || ((data.ProjectName || "").trim());
        }

        // The project's name, for the chip's tooltip — the strip itself carries
        // the identifier, as it does for every other document.
        function projectTooltip() {
            var name = (data.ProjectName || "").trim();
            if (!name || name === projectLabel()) return "";
            return msg("Project", "Project") + ": " + name;
        }

        // Origin chip: leading (tinted) icon + grey label + dark value. Given a
        // table and a record id it becomes a link that opens that record, marked
        // with a trailing arrow.
        //
        // windowId is for the chips whose screen only the SERVER can name — the
        // two VA075 documents and the VAMFG production order. Passing it (even as
        // 0) says "this chip's window was resolved for me": a real id travels with
        // the chip and is used straight away, and 0 means there is no window the
        // role can open, so the chip stays plain text rather than becoming a link
        // that reports an error when it is clicked. Omitting the argument leaves a
        // chip on the ordinary path, where the client resolves the window itself.
        //
        // namedWindow is the other half of that: a window this side CAN name but
        // the table cannot choose, because two records of the same table open
        // different screens. That is the blanket order — a C_Order like any other,
        // opening the blanket screen rather than the ordinary order one.
        function originChip(icon, label, value, iconTone, tableName, recordId, tooltip,
                            windowId, namedWindow) {
            var $chip = $('<span class="vas_098-chip"></span>')
                .addClass("vas_098-ic-" + (iconTone || "muted"));

            var serverResolved = (windowId !== undefined && windowId !== null);
            var isLink = tableName && recordId && +recordId > 0 &&
                (!serverResolved || +windowId > 0);
            if (isLink) {
                $chip.addClass("vas_098-is-link")
                    .attr("data-open-table", tableName)
                    .attr("data-open-id", recordId);
                if (serverResolved) $chip.attr("data-open-windowid", +windowId);
                if (namedWindow) $chip.attr("data-open-window", namedWindow);
            }

            $chip.append(svgIcon(icon));
            $chip.append($('<span class="vas_098-chipLabel"></span>').text(label));
            if (value) $chip.append($('<span class="vas_098-chipVal"></span>').text(value));
            if (isLink) $chip.append(svgIcon("arrowUpRight"));
            // The chip caps at the strip's width and its value truncates inside it,
            // so one long document number cannot run off the panel — the whole of
            // it stays readable on the chip's own tooltip.
            $chip.attr("title", tooltip || (value ? label + ": " + value : label));
            return $chip;
        }

        // -------------------------- Documents ---------------------------- //

        // The documents raised FROM this requisition: the purchase orders its
        // lines were converted into, the RFQs issued against it and the material
        // transfers fulfilling it. Built like the Purchase Order overview's own
        // Documents section — same columns, same per-kind summary, same clickable
        // rows opening the underlying record through openRecord().
        //
        // A requisition with nothing raised from it yet carries no section at all,
        // rather than an empty frame.
        function renderDocuments() {
            var rows = (data && data.Documents) || [];
            if (!rows.length) return null;

            sectionHead(msg("Documents", "Documents"), documentsSummary(rows));

            var $panel = $('<div class="vas_098-lowersec"></div>');
            var $tbl = $('<div class="vas_098-items vas_098-docTable"></div>');

            var $head = $('<div class="vas_098-docRow vas_098-ithead"></div>');
            $head.append($('<span></span>').text(msg("Document", "Document")));
            $head.append($('<span></span>').text(msg("DocDate", "Date")));
            $head.append($('<span></span>').text(msg("DocStatus", "Status")));
            $head.append($('<span class="vas_098-ta-r"></span>').text(msg("Amount", "Amount")));
            $tbl.append($head);

            for (var i = 0; i < rows.length; i++) $tbl.append(documentRow(rows[i]));

            $panel.append($tbl);
            return $panel;
        }

        // "2 purchase orders · 1 RFQs" — only the kinds actually present count.
        function documentsSummary(rows) {
            var ord = 0, rfq = 0, mov = 0, blk = 0, iuse = 0;
            for (var i = 0; i < rows.length; i++) {
                if (rows[i].Type === "order") ord++;
                else if (rows[i].Type === "blanket") blk++;
                else if (rows[i].Type === "rfq") rfq++;
                else if (rows[i].Type === "movement") mov++;
                else if (rows[i].Type === "internaluse") iuse++;
            }
            var bits = [];
            if (ord) bits.push(ord + " " + msg("PurchaseOrdersCount", "purchase orders"));
            if (blk) bits.push(blk + " " + msg("BlanketOrdersCount", "blanket orders"));
            if (rfq) bits.push(rfq + " " + msg("RFQsCount", "RFQs"));
            if (mov) bits.push(mov + " " + msg("TransfersCount", "material transfers"));
            if (iuse) bits.push(iuse + " " + msg("InternalUseCount", "inventory issues"));
            return bits.join(" · ");
        }

        // Document type -> the icon it leads with, the kind it calls itself, and
        // the window it opens where that is not the table's own. A blanket order
        // shares C_Order with an ordinary purchase order, so only the row's TYPE
        // can tell them apart — and each opens its own screen.
        var DOC_TYPES = {
            order:       { icon: "doc",      key: "PurchaseOrder",   text: "Purchase Order" },
            blanket:     { icon: "doc",      key: "BlanketOrder",    text: "Blanket Purchase Order",
                           window: "VAS_BlanketPurchaseOrder" },
            rfq:         { icon: "rfq",      key: "RFQ",             text: "RFQ" },
            movement:    { icon: "transfer", key: "MaterialTransfer", text: "Material Transfer" },
            internaluse: { icon: "transfer", key: "InternalUse",     text: "Inventory Use",
                           window: "VAS_InternalUseInventory" }
        };

        function documentRow(d) {
            var $r = $('<div class="vas_098-docRow vas_098-itbody"></div>');

            var canOpen = d.TableName && +d.RecordId > 0;
            var meta = DOC_TYPES[d.Type] || DOC_TYPES.order;

            if (canOpen) {
                $r.addClass("vas_098-is-link")
                    .attr("data-open-table", d.TableName)
                    .attr("data-open-id", d.RecordId);
                // A blanket order and an inventory-use issue each open a screen
                // that is NOT their table's default: C_Order's zoom target is the
                // ordinary purchase order window, and M_Inventory's is the physical
                // count. Naming the window on the row is the only way to tell them
                // apart — the table alone cannot.
                if (meta.window) $r.attr("data-open-window", meta.window);
            }

            // Identity: doc number + kind, with the open affordance on the right.
            var $item = $('<span class="vas_098-docItem"></span>');
            $item.append(svgIcon(meta.icon));

            var $txt = $('<span class="vas_098-docTxt"></span>');
            $txt.append($('<div class="vas_098-itname"></div>').text(d.DocumentNo || DASH));

            var sub = msg(meta.key, meta.text);
            if (d.LineCount) sub += " · " + d.LineCount + " " + msg("Lines");
            $txt.append($('<div class="vas_098-itsku"></div>').text(sub));

            $item.append($txt);
            if (canOpen) $item.append(svgIcon("arrowUpRight"));
            $r.append($item);

            $r.append($('<span></span>').text(formatDate(d.DocDate) || DASH));

            var st = STATUS_MAP[d.DocStatus];
            $r.append($('<span></span>').append(st
                ? tag(msg(st.key), st.tone)
                : tag(d.DocStatus || msg("NA"), "vas_098-draft")));

            // A document this schema records no total for sends null rather than a
            // zero — a zero here would read as "this document is worth nothing".
            // A material transfer always carries one now: the same value its own
            // screen shows, summed from its lines (model side).
            var $amt = $('<span class="vas_098-ta-r"></span>');
            $amt.text((d.Amount === null || d.Amount === undefined) ? DASH : money(d.Amount));
            $r.append($amt);

            return $r;
        }

        // ------------------ Events / record navigation ------------------- //

        // Delegated once on the root, so it survives every re-render: a Reference
        // chip or a Documents row opens the record it points at.
        function bindEvents() {
            $root.on("click", ".vas_098-chip.vas_098-is-link, .vas_098-is-link[data-open-table]", function (e) {
                e.preventDefault();
                openRecord($(this).attr("data-open-table"), $(this).attr("data-open-id"),
                    $(this).attr("data-open-sotrx") === "Y",
                    // A row may name the window itself where its TABLE cannot
                    // choose one — a blanket order and an ordinary purchase order
                    // are both C_Order, and open different screens.
                    $(this).attr("data-open-window"),
                    // Or carry the window id outright, for a record whose screen
                    // only the server could resolve (the VA075 / VAMFG chips).
                    $(this).attr("data-open-windowid"));
            });
        }

        // Tables whose record does NOT open in the table's default zoom window,
        // mapped to the name of the window it does open. The VAS_092 Purchase
        // Order overview carries the same map for the same reason: an RFQ opens
        // the VAS_RFQ window, not whatever C_RfQ's zoom target resolves to, and a
        // requisition's purchase orders are purchase-side C_Order records.
        //
        // Any further screen that needs naming belongs here; nothing else has to
        // change.
        // VAMFG_M_WorkOrder is here for the same reason the rest are, and it is the
        // name VAS_102 opens that screen by: the production order's window can be
        // named, so it is named, and the dictionary lookup below is left for the
        // two VA075 documents that genuinely cannot be. Naming it also means a
        // click resolves the same way whether or not the payload carried an id.
        var WINDOW_NAME_BY_TABLE = {
            "C_RfQ":             "VAS_RFQ",
            "C_Project":         "VAS_Project",
            "C_Order":           "VAS_PurchaseOrder",
            "M_Movement":        "VAS_MaterialTransfer",
            "VAMFG_M_WorkOrder": "VAMFG_ProductionOrder"
        };

        // The same map for a record opened as a SALES transaction. C_Order serves
        // both sides — the Documents section lists the purchase orders raised FROM
        // the requisition, while the Reference strip can name the sales order whose
        // demand it serves — so each side names its own window and this one wins
        // when the flag is set. Ported from VAS_092.
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
                    "VAS_098_PurchaseRequisition/GetWindow_ID", windowName);
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

        // Table name -> AD_Window_ID, cached like windowIdByName above.
        var windowIdByTable = {};

        // Last resort: ask the SERVER which window the table opens in
        // (AD_Table.AD_Window_ID, else the first window with a tab on it).
        //
        // The VA075 work order and field service request chips need this. That
        // module is not part of this solution, so its screens cannot be named in
        // WINDOW_NAME_BY_TABLE, and the browser-side zoom lookup only knows tables
        // the client has cached — so both chips fell through to the "cannot open"
        // toast on every click. Any future chip gets the same safety net. Ported
        // from VAS_102.
        function resolveWindowIdByTable(tableName) {
            if (!tableName) return 0;
            if (windowIdByTable.hasOwnProperty(tableName)) {
                return windowIdByTable[tableName] > 0 ? windowIdByTable[tableName] : 0;
            }
            try {
                if (!(window.VIS && VIS.dataContext &&
                      typeof VIS.dataContext.getJSONRecord === "function")) {
                    return 0;
                }
                var id = VIS.dataContext.getJSONRecord(
                    "VAS_098_PurchaseRequisition/GetWindowIdByTable", tableName);
                id = parseInt(id, 10);
                if (isNaN(id) || id <= 0) {
                    windowIdByTable[tableName] = -1;
                    console.log("resolveWindowIdByTable: no window for table " + tableName);
                    return 0;
                }
                windowIdByTable[tableName] = id;
                return id;
            } catch (e) {
                windowIdByTable[tableName] = -1;
                console.log(e);
                return 0;
            }
        }

        // Opens the record's window filtered to that row: the id the row already
        // carries when it has one, else the window named for this table, else the
        // table's default zoom target, else the window the DICTIONARY says the
        // table opens in. Either way the window is started with an equal-query on
        // the table's key column. Degrades to a toast so a click never throws.
        function openRecord(tableName, recordId, isSOTrx, namedWindow, knownWindowId) {
            if (!tableName || !recordId || +recordId <= 0 || !window.VIS) return;
            try {
                // A window the ROW already knows wins outright — the payload
                // resolved it on the server, against the dictionary and the role,
                // for a table whose screen this side cannot name at all.
                var windowId = +knownWindowId || 0;

                // A window named on the ROW wins over both maps: it is the only
                // thing that can tell two records of the same table apart, which is
                // exactly the blanket-order case (C_Order opens either the purchase
                // order window or the blanket one, depending on the record).
                //
                // Failing that, a sales-transaction record takes its own window
                // name where the table has one; everything else takes the plain
                // mapping.
                if (windowId <= 0) {
                    var windowName = namedWindow ||
                        ((isSOTrx && WINDOW_NAME_BY_TABLE_SOTRX[tableName])
                            ? WINDOW_NAME_BY_TABLE_SOTRX[tableName]
                            : WINDOW_NAME_BY_TABLE[tableName]);
                    windowId = resolveWindowIdByName(windowName);
                }

                if (windowId <= 0 &&
                    VIS.ZoomTarget && typeof VIS.ZoomTarget.getZoomAD_Window_ID === "function") {
                    // The 4th arg (IsSOTrx) picks the sales vs purchase window for
                    // a dual-purpose table like C_Order.
                    windowId = VIS.ZoomTarget.getZoomAD_Window_ID(tableName, 0, null, !!isSOTrx) || 0;
                }
                if (windowId <= 0) windowId = resolveWindowIdByTable(tableName);
                if (windowId > 0 && VIS.viewManager && typeof VIS.viewManager.startWindow === "function") {
                    var zoomQuery = VIS.Query.prototype.getEqualQuery(tableName + "_ID", +recordId);
                    VIS.viewManager.startWindow(windowId, zoomQuery);
                    return;
                }
            } catch (e) { console.log(e); }
            toast(msg("OpenRecord", "Open") + " " + tableName + " #" + recordId, false);
        }

        // ------------------------ Convert strip -------------------------- //
        // The convert strip is gone: the "conversion available" note and the
        // Purchase Order / RFQ / Material Transfer buttons with it, along with
        // convertBtn() and runAction() which served only them.
        //
        // Every one of those actions belongs to the requisition window, where the
        // process runs behind its own parameter screen and the document's full
        // validation. Two of the three could never succeed from here at all (the
        // RFQ process selects by organisation and needs commercial terms this
        // panel has no business inventing; the material-transfer process ships
        // with a module that is not installed), and the note that headed them
        // only ever restated what the progress stepper already shows. VAS_099
        // dropped its own action bar for the same reasons.
        //
        // The controller's ConvertToPurchaseOrder endpoint went with them.


        // Lightweight self-contained toast.
        function toast(message, isError) {
            var $t = $('<div class="vas_098-toast"></div>')
                .addClass(isError ? "vas_098-err" : "vas_098-ok").text(message);
            $root.append($t);
            setTimeout(function () { $t.addClass("vas_098-show"); }, 10);
            setTimeout(function () {
                $t.removeClass("vas_098-show");
                setTimeout(function () { $t.remove(); }, 300);
            }, 4200);
        }

        // -------------------------- Stat strip --------------------------- //

        function renderStats() {
            var $strip = $('<div class="vas_098-stats"></div>');

            // Estimated value (breach-aware)
            var $s1 = statCard(data.IsBudgetBreach ? "vas_098-breach" : "vas_098-a-blue", msg("EstimatedValue"));
            var $v1 = $('<div class="vas_098-sval"></div>').text(money(data.EstimatedValue));
            if (data.IsBudgetBreach) {
                $v1.append($('<span class="vas_098-breachic"></span>')
                    .attr("title", msg("BudgetBreached")).append(svgIcon("warn")));
            }
            $s1.append($v1);
            $s1.append(statSub(budgetSubText(), data.IsBudgetBreach ? "vas_098-breach" : ""));
            $strip.append($s1);

            // Required by
            var $s2 = statCard("vas_098-a-violet", msg("RequiredBy"));
            $s2.append($('<div class="vas_098-sval"></div>').text(formatDate(data.DateRequired) || msg("NA")));
            $s2.append(statSub(requiredSubText(), ""));
            $strip.append($s2);

            // Line items
            var $s3 = statCard("vas_098-a-green", msg("LineItems"));
            $s3.append($('<div class="vas_098-sval"></div>').text((data.LineCount || 0) + " " + msg("Lines")));
            $s3.append(statSub(formatNumber(data.RequestedUnits) + " " + msg("UnitsRequested"), ""));
            $strip.append($s3);

            // Stock availability — on-hand at the warehouse the goods are served
            // from or received into, whichever applies (model side). Shows a real
            // quantity (0 included) whenever a warehouse could be resolved; only a
            // requisition naming neither reads N/A. The sub-line names the
            // warehouse the figure was actually read at, so an externally procured
            // requisition is not made to look like an internal one.
            var $s4 = statCard("vas_098-a-amber", msg("SourceAvailability"));
            if (data.HasSourceData) {
                $s4.append($('<div class="vas_098-sval"></div>')
                    .text(formatNumber(data.SourceStockOnHand || 0)));
                var stockWh = data.StockWarehouseName || data.SourceWarehouseName;
                $s4.append(statSub(
                    msg("OnHandAtSource", "on hand") +
                    (stockWh ? " · " + stockWh : "") +
                    " · " + (data.FullyInStockLines || 0) + " / " + (data.LineCount || 0) + " " +
                    msg("LinesFullyInStock"), ""));
            } else {
                $s4.append($('<div class="vas_098-sval"></div>').text(msg("NA")));
                $s4.append(statSub(msg("SourceStockNA"), ""));
            }
            $strip.append($s4);

            $body.append($strip);
        }

        function statCard(accent, cap) {
            var $s = $('<div class="vas_098-stat"></div>').addClass(accent);
            $s.append($('<div class="vas_098-scap"></div>').text(cap));
            return $s;
        }

        function statSub(text, cls) {
            return $('<div class="vas_098-ssub"></div>').addClass(cls || "").text(text);
        }

        function budgetSubText() {
            if (data.IsBudgetBreach) {
                if (data.BudgetBreachNote) return data.BudgetBreachNote;
                if (data.BudgetOverage > 0) return msg("OverBudgetBy") + " " + money(data.BudgetOverage);
                return msg("BudgetBreached");
            }
            return msg("WithinBudget");
        }

        function requiredSubText() {
            if (!data.DateRequired) return msg("NoDateSet");
            // Wall-clock parsing here too: a timezone shift on either date can move
            // the day-count across a midnight and report the wrong days remaining.
            var req = parseServerDate(data.DateRequired);
            var sys = parseServerDate(data.SystemDate) || new Date();
            if (!req) return "";
            var days = Math.round((stripTime(req) - stripTime(sys)) / 86400000);
            if (days > 0) return days + " " + msg("DaysRemaining");
            if (days < 0) return Math.abs(days) + " " + msg("DaysOverdue");
            return msg("DueToday");
        }

        // -------------------------- Progress ----------------------------- //

        // Every stage is captioned with the moment it actually happened, all of
        // which the model derives from the record's workflow and from the purchase
        // orders raised against it (never from the document date, which can be
        // back- or forward-dated).
        function progressStages() {
            var s = data.StatusCode;
            // The DOCUMENT's own status decides this one, and only it: the stage
            // reports whether the requisition has been completed, so it turns on the
            // status turning Completed (or Closed) and shows the moment that
            // happened. IsConverted used to count here too, which asked a second
            // document's existence to answer a question about this one's status —
            // and nothing is lost by dropping it, because a converted requisition
            // already back-fills this stage through the reach rule below.
            var completed  = s === "CO" || s === "CL";
            // Converted once ANYTHING has been raised from the requisition — a
            // purchase or blanket order, an RFQ, a material transfer, an inventory
            // use issue — and in fulfilment once one of those has been completed.
            // Both flags and both dates come from the model, which reads every one
            // of those documents (LoadConversionMilestones); the stepper used to
            // know about purchase orders alone.
            var converted  = data.IsConverted;
            var fulfilment = data.HasOrdered;

            // Every stage below is dated by a stored TIMESTAMP — when the record
            // was created, when the workflow completed it, when the order it
            // became was raised — not by a document date field, so all of them
            // read through formatStampDateShort and land on the viewer's own day.
            //
            // The last stage is Posted (M_Requisition.Posted), replacing Closed.
            // Closing is a state most requisitions never reach — the stage sat
            // pending for the life of the document — where posting is something
            // every completed requisition does, and is the fact a reader is
            // actually looking for at the end of the line.
            // Submitted is gone. It was dated by the COMPLETION stamp — the same
            // moment the stage next to it reports — and its own condition was
            // satisfied by every status Completed is, so the two lit up together
            // and read as one event told twice. Nothing is lost: a requisition that
            // has been submitted is a requisition in progress, which the status
            // pill above already says.
            return [
                { key: "vas_098-c1", label: msg("Drafted"),      done: true,        sub: formatStampDateShort(data.Created) },
                { key: "vas_098-c2", label: msg("Completed"),    done: completed,   sub: formatStampDateShort(data.CompletedDate) },
                { key: "vas_098-c3", label: msg("Converted"),    done: converted,   sub: formatStampDateShort(data.ConvertedDate) },
                { key: "vas_098-c4", label: msg("InFulfilment"), done: fulfilment,  sub: formatStampDateShort(data.FulfilmentDate) },
                { key: "vas_098-c5", label: msg("Posted"),       done: !!data.Posted, sub: formatStampDateShort(data.PostedDate) }
            ];
        }

        // How many of the stages above form the requisition's LIFECYCLE CHAIN —
        // the run in which reaching one stage means every earlier one was reached
        // too. The first four do: a requisition in fulfilment was converted, and a
        // converted one was completed.
        //
        // Posted (the fifth) does not belong to that chain. Posting follows
        // COMPLETION, so it is routinely true while Converted and In Fulfilment
        // are still ahead — and under the chain rule a posted requisition would
        // light up every stage before it, reporting purchase orders that do not
        // exist. It is therefore marked from its own flag alone, and never
        // back-fills the stages before it. (Closed, which it replaced, was safely
        // terminal and needed none of this.)
        var LIFECYCLE_STAGES = 4;

        function renderProgress() {
            var stages = progressStages();

            // Monotonic reach across the LIFECYCLE stages only: one of them is
            // "reached" if it or any later stage in the chain is done. Posted is
            // outside the chain (see LIFECYCLE_STAGES) and is not consulted here.
            var reached = [];
            for (var i = 0; i < LIFECYCLE_STAGES; i++) {
                var any = false;
                for (var j = i; j < LIFECYCLE_STAGES; j++) { if (stages[j].done) { any = true; break; } }
                reached.push(any);
            }
            var current = 1;
            for (var k = 0; k < reached.length; k++) { if (reached[k]) current = k + 1; }

            var st = statusMeta();
            var $sh = $('<div class="vas_098-sechead"></div>');
            $sh.append($('<h2></h2>').text(msg("RequisitionProgress")));
            // Counted over the lifecycle chain, which is what "stage n of m"
            // describes — Posted is a milestone beside it, not the sixth step of it.
            $sh.append($('<span class="vas_098-secright"></span>').text(
                msg("Stage") + " " + current + " " + msg("Of") + " " + LIFECYCLE_STAGES + " · " + st.label));
            $body.append($sh);

            var $stepper = $('<div class="vas_098-stepper"></div>');
            for (var s = 0; s < stages.length; s++) {
                var stg = stages[s];
                var stateCls, sub, showCheck;
                if (s >= LIFECYCLE_STAGES) {
                    // Posted: done or not, on its own flag. It is never the
                    // "active" stage — nobody is working towards it, the posting
                    // engine either has run or has not. Unposted reads Pending like
                    // any other stage still ahead: "Not Posted" restated the stage's
                    // own label as though it were news.
                    if (stg.done) { stateCls = "vas_098-done"; showCheck = true; sub = stg.sub || msg("Posted"); }
                    else { stateCls = "vas_098-pending"; showCheck = false; sub = msg("Pending"); }
                } else if (reached[s]) {
                    // A stage that has been REACHED is done, and shows the moment it
                    // happened. The last reached stage used to be drawn "active"
                    // instead, which replaced its date with a forward-looking caption
                    // — a converted requisition read "Ready to Convert" and never
                    // showed the date it was converted on.
                    stateCls = "vas_098-done"; showCheck = true; sub = stg.sub || "";
                } else if (s === current) {
                    // The first stage still ahead is the one being worked towards.
                    stateCls = "vas_098-active"; showCheck = false; sub = activeSub(stg);
                } else {
                    stateCls = "vas_098-pending"; showCheck = false; sub = msg("Pending");
                }
                $stepper.append(stepEntry(s + 1, stg, stateCls, showCheck, sub));
            }
            $body.append($stepper);
        }

        // Caption for the stage being worked towards — forward-looking, since it
        // has not happened yet and therefore has no date to report.
        function activeSub(stg) {
            // Completed reports the DOCUMENT's own status and nothing else. Until
            // the document completes there is only one thing to say about it, and a
            // requisition sitting in draft and one part-way through its workflow are
            // the same thing here: Pending. It read "In Progress" on a drafted
            // record, which claimed work had started on a document nobody had
            // submitted, and it read the same on one that really was in progress —
            // so the caption never distinguished anything. Once the status turns
            // Completed the stage is reached, and the branch above captions it with
            // the completion date instead of this.
            if (stg.key === "vas_098-c2") return msg("Pending");
            if (stg.key === "vas_098-c3") return msg("ReadyToConvert");
            return msg("InProgressSub");
        }

        function stepEntry(num, stg, stateCls, showCheck, sub) {
            var $step = $('<div class="vas_098-step"></div>').addClass(stateCls).addClass(stg.key);
            var $node = $('<div class="vas_098-node"></div>');
            if (showCheck) $node.append(svgIcon("check")); else $node.text(num);
            $step.append($node);
            $step.append($('<div class="vas_098-slabel"></div>').text(stg.label));
            if (sub) $step.append($('<div class="vas_098-ssub2"></div>').text(sub));
            return $step;
        }

        // -------------------- Lower region (stacked) --------------------- //

        // Items, then Notes, then Activity — each a headed section stacked down
        // the panel. These used to be three tabs; all three now sit down the page
        // where they are visible without a click.
        //
        // Activity comes last deliberately: it is the longest section (it pages,
        // and grows for the life of the document) and the least often read, so
        // anything below it would be pushed off the bottom of the panel. Notes is
        // a short block and reads better directly under the lines it annotates.
        function renderLower() {
            var lines = (data.Lines) || [];
            var activity = (data.Activity) || [];

            // Items only exists when the requisition has lines. An empty frame
            // headed "Items — 0 lines" said nothing the reader could not see, and
            // on a requisition still being keyed it was the first thing on the
            // panel. The heading goes with the table it heads.
            if (lines.length) {
                sectionHead(msg("Items"), lines.length + " " + msg("Lines"));
                $body.append(renderItemsPanel(lines));
            }

            // Documents sits directly under the items it was raised from, and
            // draws nothing (its section header included) when the requisition has
            // produced none yet — hence the null check rather than an empty frame.
            var $docs = renderDocuments();
            if ($docs) $body.append($docs);

            // Notes only exists when the requisition carries a description of its
            // own. Without one, no section: the heading goes with the card it heads.
            var $notes = renderNotesPanel();
            if ($notes) {
                sectionHead(msg("Notes"), "");
                $body.append($notes);
            }

            sectionHead(msg("Activity"), activity.length + " " + msg("Updates", "updates"));
            $body.append(renderActivityPanel(activity));
        }

        // Section header: title on the left, optional summary on the right.
        function sectionHead(title, summary) {
            var $sh = $('<div class="vas_098-sechead vas_098-lowerhead"></div>');
            $sh.append($('<h2></h2>').text(title));
            if (summary) $sh.append($('<span class="vas_098-secright"></span>').text(summary));
            $body.append($sh);
            return $sh;
        }

        // ---- Items ---- //

        function renderItemsPanel(lines) {
            var $panel = $('<div class="vas_098-lowersec"></div>');
            var $items = $('<div class="vas_098-items"></div>');

            var $head = $('<div class="vas_098-itrow vas_098-ithead"></div>');
            $head.append($('<span></span>').text(msg("Item")));
            $head.append($('<span></span>').text(msg("UOM", "UOM")));
            $head.append($('<span class="vas_098-ta-c"></span>').text(msg("Qty")));
            // "Received": how much of what the line asked for has actually been
            // delivered against it, across every document that can deliver it —
            // goods receipts, material transfers and inventory-use issues (model
            // side). It replaces "Received On Hand", which reported the warehouse's
            // stock POSITION: a figure that answers "is there any?" rather than
            // "did this line get what it asked for", and that moves for reasons
            // having nothing to do with this requisition.
            $head.append($('<span class="vas_098-ta-r"></span>')
                .text(msg("Received", "Received")));
            $head.append($('<span class="vas_098-ta-r"></span>').text(msg("UnitCost")));
            $head.append($('<span class="vas_098-ta-r"></span>').text(msg("EstTotal")));
            $items.append($head);

            // renderLower only calls this with lines, so the "no line items"
            // placeholder that used to stand here is gone with the empty section
            // it filled.

            // Totals footer always covers the whole requisition, never the page.
            var $foot = itemsFooter();
            $items.append($foot);
            $panel.append($items);

            // The pager sits outside the items box: that box takes its own
            // horizontal scroll on narrow panels and the controls must not scroll
            // away with the columns.
            var $pager = $('<div class="vas_098-pager"></div>');
            if (lines.length > LINES_PER_PAGE) $panel.append($pager);

            // Rows are replaced in place, ahead of the footer, so the table's
            // structure and its CSS grid stay exactly as they were.
            function paintPage() {
                var pageCount = Math.max(1, Math.ceil(lines.length / LINES_PER_PAGE));
                if (linesPage >= pageCount) linesPage = pageCount - 1;
                if (linesPage < 0) linesPage = 0;

                var start = linesPage * LINES_PER_PAGE;
                var end = Math.min(lines.length, start + LINES_PER_PAGE);

                $items.find(".vas_098-itbody").remove();
                for (var i = start; i < end; i++) $foot.before(itemRow(lines[i]));

                buildPager($pager, linesPage, pageCount, lines.length, start, end,
                    function (p) { linesPage = p; paintPage(); });
            }

            paintPage();
            return $panel;
        }

        // Range caption on the left, Previous / page-of / Next on the right.
        // Rebuilt on every page change so the disabled states stay accurate.
        //
        // `page` is the 0-based page being shown and `onGo` is handed the page to
        // move to, so each paged section owns its own page variable — the line
        // items and the activity feed page independently of one another. Nothing
        // is drawn for a single-page list, so a short section shows no controls.
        function buildPager($pager, page, pageCount, total, start, end, onGo) {
            $pager.empty();
            if (pageCount <= 1) return;

            $pager.append($('<span class="vas_098-pgRange"></span>').text(
                msg("Showing", "Showing") + " " + (start + 1) + "-" + end + " " +
                msg("Of") + " " + total));

            var $ctrls = $('<span class="vas_098-pgCtrls"></span>');
            $ctrls.append(pagerButton(msg("Previous", "Previous"), "chevLeft",
                page <= 0, function () { onGo(page - 1); }));
            $ctrls.append($('<span class="vas_098-pgPos"></span>').text(
                msg("Page", "Page") + " " + (page + 1) + " " +
                msg("Of") + " " + pageCount));
            $ctrls.append(pagerButton(msg("Next", "Next"), "chevRight",
                page >= pageCount - 1, function () { onGo(page + 1); }));
            $pager.append($ctrls);
        }

        function pagerButton(label, icon, disabled, handler) {
            var $b = $('<span class="vas_098-pgBtn"></span>');
            if (icon === "chevLeft") $b.append(svgIcon(icon));
            $b.append($('<span></span>').text(label));
            if (icon === "chevRight") $b.append(svgIcon(icon));
            if (disabled) $b.addClass("vas_098-is-disabled");
            else $b.on("click", handler);
            return $b;
        }

        function itemRow(ln) {
            var $r = $('<div class="vas_098-itrow vas_098-itbody"></div>');

            var $item = $('<span></span>');
            var pname = ln.ProductName || msg("NA");
            $item.append($('<div class="vas_098-itname"></div>').text(pname).attr("title", pname));
            // Attribute Set Instance (lot / serial / size ...) sits directly under
            // the product name — the attribute qualifies WHICH stock was asked
            // for, so it belongs with the name rather than below the search key.
            // Only a real instance is shown; a blank or "--" placeholder is not.
            var asi = (ln.AttributeSetInstance || "").trim();
            if (asi && asi !== "--" && asi !== "-") {
                $item.append($('<div class="vas_098-itattr"></div>').text(asi).attr("title", asi));
            }
            // Product search key, shown without the former "SKU" prefix.
            if (ln.ProductValue) {
                $item.append($('<div class="vas_098-itsku"></div>').text(ln.ProductValue));
            } else if (ln.Description) {
                $item.append($('<div class="vas_098-itsku"></div>').text(ln.Description));
            }
            $r.append($item);

            // Unit of measure (replaced the product category column).
            var $uom = $('<span></span>');
            if (ln.UOMName) $uom.append($('<span class="vas_098-uom"></span>').text(ln.UOMName));
            else $uom.append($('<span class="vas_098-na"></span>').text(msg("NA")));
            $r.append($uom);

            $r.append($('<span class="vas_098-ta-c"></span>').text(formatNumber(ln.RequestedQty, ln.UOMPrecision)));

            $r.append(receivedCell(ln));

            // The unit price is per SELECTED unit (model side) and is shown to the
            // currency's own precision rather than rounded to whole units: a rate
            // of 0.75 an EA is a real price, and money() would have printed it as 1.
            // The line amount keeps the whole-currency treatment the rest of the
            // panel uses for totals.
            var $price = $('<span class="vas_098-ta-r"></span>').text(moneyPrecise(ln.UnitPrice));
            if (ln.UOMName) {
                $price.attr("title", moneyPrecise(ln.UnitPrice) + " / " + ln.UOMName);
            }
            $r.append($price);
            $r.append($('<span class="vas_098-ta-r"></span>').text(money(ln.LineAmount)));
            return $r;
        }

        // What has actually been DELIVERED against this line, against what it asked
        // for: a progress bar over the ratio, "8/10". Both figures are in the line's
        // selected UOM (model side), so they read against each other and against the
        // Qty column beside them.
        //
        // The received side sums every document that can deliver the line — a goods
        // receipt raised from its purchase order, a material transfer, an
        // inventory-use issue — counting only those that completed (model side,
        // LoadReceivedQty). The requested side is the line's own quantity.
        //
        // The bar is green once the line has everything it asked for and orange
        // while it is short, so it turns green of its own accord on the refresh
        // after the last delivery completes.
        //
        // A CHARGE line is left blank. A charge is not delivered — it is a cost
        // added to a document — so it has no received quantity, and "0" would read
        // as an outstanding delivery that is never coming.
        function receivedCell(ln) {
            var $c = $('<span class="vas_098-ta-r"></span>');
            if (isChargeLine(ln)) return $c;

            var req = +ln.RequestedQty || 0;
            var received = +ln.ReceivedQty || 0;
            var full = req > 0 && received >= req;
            var pct = req > 0 ? Math.round((received / req) * 100) : (received > 0 ? 100 : 0);

            var $recv = $('<span class="vas_098-recv"></span>')
                .addClass(full ? "vas_098-full" : "vas_098-short");
            $recv.attr("title", msg("ReceivedOfOrdered", "Received of ordered") + ": " +
                formatNumber(received, ln.UOMPrecision) + " / " +
                formatNumber(req, ln.UOMPrecision) +
                (ln.UOMName ? " " + ln.UOMName : ""));

            var $bar = $('<span class="vas_098-bar"><i></i></span>');
            $bar.find("i").css("width", Math.max(0, Math.min(100, pct)) + "%");
            $recv.append($bar);
            $recv.append($('<span class="vas_098-recvRatio"></span>').text(
                formatNumber(received, ln.UOMPrecision) + "/" + formatNumber(req, ln.UOMPrecision)));
            $c.append($recv);
            return $c;
        }

        // A line raised against a charge rather than a product. The charge id is
        // the authority — a charge line carries no M_Product_ID, and the model
        // sends both — so a product line with an unresolved name is never mistaken
        // for one.
        function isChargeLine(ln) {
            return (+ln.C_Charge_ID || 0) > 0 && (+ln.M_Product_ID || 0) <= 0;
        }

        function itemsFooter() {
            var $f = $('<div class="vas_098-itfoot"></div>');
            // The budget set for the requisition sits where the subtotal used to,
            // so the estimate can be read straight against it. Its source is the
            // line-level VAS_AvailableBudget the "Calculate Budget" process stamps,
            // falling back (model side) to the budget the requisition is drawn
            // against — that process rarely runs, and the field used to read N/A
            // on almost every record. The tooltip says which of the two the figure
            // is, so a whole budget is never mistaken for a remaining balance.
            // Only the ABSENCE of a budget reads N/A. A negative figure is a real
            // one — a budget carried on the credit side nets that way — and
            // hiding it said "no budget" about a record that has one.
            var budget = +data.AvailableBudget || 0;
            var $budget = footBit(msg("Budget", "Budget"),
                budget !== 0 ? money(budget) : msg("NA"), false);
            if (budget !== 0) {
                $budget.attr("title", data.BudgetIsRequisitionLevel
                    ? msg("BudgetForRequisition", "Budget set for this requisition")
                    : msg("BudgetAvailableOnLines", "Budget available to the requisition's lines"));
            }
            $f.append($budget);
            $f.append(footBit(msg("EstimatedTotal"), money(data.EstimatedValue), true));
            return $f;
        }

        function footBit(label, value, grand) {
            var $b = $('<span></span>').addClass(grand ? "vas_098-grand" : "vas_098-tf");
            $b.append(document.createTextNode(label));
            $b.append($('<b></b>').text(value));
            return $b;
        }

        // ---- Activity ---- //

        var ACT_BADGE = {
            create:  { cls: "vas_098-create",  key: "ActCreated" },
            status:  { cls: "vas_098-status",  key: "ActStatus"  },
            submit:  { cls: "vas_098-submit",  key: "ActSubmit"  },
            link:    { cls: "vas_098-link",    key: "ActLinked"  },
            comment: { cls: "vas_098-comment", key: "ActComment" },
            // One row per FIELD that changed, not one per save (model side).
            updated: { cls: "vas_098-status",  key: "ActUpdated", fallback: "Updated" },
            // Downstream lifecycle documents.
            po:          { cls: "vas_098-po",  key: "ActPO",          fallback: "PO"  },
            // Documents created FROM the requisition, each naming itself.
            rfqcreated:         { cls: "vas_098-link", key: "ActRFQ",         fallback: "RFQ" },
            movementcreated:    { cls: "vas_098-link", key: "ActTransfer",    fallback: "Transfer" },
            internalusecreated: { cls: "vas_098-link", key: "ActInternalUse", fallback: "Issue" },
            grn:         { cls: "vas_098-grn", key: "ActGRN",         fallback: "GRN" },
            grncomplete: { cls: "vas_098-grn", key: "ActGRNComplete", fallback: "GRN" },
            // E-mails sent against the requisition (MailAttachment1).
            email:       { cls: "vas_098-email", key: "ActEmail",     fallback: "Email" },
            // The correspondence and engagement sources shared with every other
            // overview panel (model side, VAS_ActivitySourcesModel): meetings and
            // tasks from AppointmentsInfo, calls from VA048_CallDetails, and the
            // inbound letters MailAttachment1 files under AttachmentType 'I'.
            appointment: { cls: "vas_098-appt",   key: "ActAppointment", fallback: "Meeting" },
            task:        { cls: "vas_098-task",   key: "ActTask",        fallback: "Task" },
            call:        { cls: "vas_098-call",   key: "ActCall",        fallback: "Call" },
            letter:      { cls: "vas_098-letter", key: "ActLetter",      fallback: "Letter" }
        };

        // The four types above, for the places that treat them as a family.
        var ACT_SOURCE_TYPES = { appointment: 1, task: 1, call: 1, letter: 1 };

        // Maximum activity rows shown per page; the feed paginates beyond this.
        // A requisition accumulates every status change and every downstream PO /
        // GRN, and an unpaged feed made the panel scroll past everything below it.
        var ACTIVITY_PER_PAGE = 15;

        function renderActivityPanel(activity) {
            var $panel = $('<div class="vas_098-lowersec"></div>');
            var $card = $('<div class="vas_098-panelcard"></div>');

            if (!activity.length) {
                $card.append($('<div class="vas_098-itempty"></div>').text(msg("NoActivity")));
                $panel.append($card);
                return $panel;
            }

            $panel.append($card);

            // The pager is a sibling of the card, exactly as the line-items pager
            // is a sibling of its items box.
            var $pager = $('<div class="vas_098-pager"></div>');
            if (activity.length > ACTIVITY_PER_PAGE) $panel.append($pager);

            function paintPage() {
                var pageCount = Math.max(1, Math.ceil(activity.length / ACTIVITY_PER_PAGE));
                if (activityPage >= pageCount) activityPage = pageCount - 1;
                if (activityPage < 0) activityPage = 0;

                var start = activityPage * ACTIVITY_PER_PAGE;
                var end = Math.min(activity.length, start + ACTIVITY_PER_PAGE);

                $card.empty();
                for (var i = start; i < end; i++) {
                    $card.append(activityRow(activity[i]));
                    // An e-mail's body is heavy — it stays collapsed under its row
                    // and opens only when the reader asks for it.
                    var $mail = activityBody(activity[i]);
                    if ($mail) $card.append($mail);
                }

                buildPager($pager, activityPage, pageCount, activity.length, start, end,
                    function (p) { activityPage = p; paintPage(); });
            }

            paintPage();
            return $panel;
        }

        // "was X → now Y" under the field's name, for a field-level edit. A
        // value the log recorded as empty reads as an em dash rather than as a
        // blank, so a cleared field is visibly cleared instead of looking like a
        // rendering gap. Follows VAS_101 / VAS_104.
        function changeDelta(a) {
            var $d = $('<small class="vas_098-actsub vas_098-actDelta"></small>');
            var blank = "—";
            $d.append($('<span class="vas_098-cvOld"></span>').text(a.OldValue || blank));
            $d.append($('<span class="vas_098-cvArrow"></span>').text("→"));
            $d.append($('<span class="vas_098-cvNew"></span>').text(a.NewValue || blank));
            $d.attr("title", (a.OldValue || blank) + " → " + (a.NewValue || blank));
            return $d;
        }

        function activityRow(a) {
            var meta = ACT_BADGE[a.Type] || ACT_BADGE.comment;
            var $row = $('<div class="vas_098-actrow"></div>');

            $row.append($('<span class="vas_098-actbadge"></span>')
                .addClass(meta.cls).text(msg(meta.key, meta.fallback)));

            var $main = $('<div class="vas_098-actmain"></div>');
            var $wrap = $('<div class="vas_098-atwrap"></div>');
            var text = activityText(a);
            var $text = $('<span class="vas_098-at"></span>').text(text).attr("title", text);
            // A comment's headline IS the comment, so it wraps instead of
            // ellipsising after one line (stylesheet). Every other headline is a
            // short labelled action and reads fine on one.
            if (a.Type === "comment") $text.addClass("vas_098-multiline");
            $wrap.append($text);

            // "when · by whom", on EVERY row — the audit trail's whole point is
            // who did what and when, so the three parts are always in the same
            // three places: the badge says what kind of event it is, the headline
            // says what happened, and this says when and by whom.
            //
            // It used to be built here for an e-mail only, while the milestone and
            // document rows spliced the actor onto the end of their own sentence
            // and a comment named nobody at all — so a chat entry showed its text
            // and a timestamp with no author. The name is read from UserName,
            // which every row now carries (model side).
            var when = formatDateTime(a.Created);
            if (a.UserName) {
                when = when ? when + " · " + msg("By") + " " + a.UserName
                            : msg("By") + " " + a.UserName;
            }
            $wrap.append($('<span class="vas_098-attime"></span>').text(when).attr("title", when));
            $main.append($wrap);

            // An e-mail names its recipients under the subject — every address on
            // the To, Cc and Bcc lists, in full. No tooltip: the line is no longer
            // an abridgement of something the reader has to hover to see.
            // ... and so does a letter, which is the same record filed under a
            // different attachment type.
            if (a.Type === "email" || a.Type === "letter") {
                var to = recipientSummary(a);
                if (to) $main.append($('<div class="vas_098-actsub"></div>').text(to));
            }

            // A call names the number it was placed to, where one was recorded —
            // the same slot, answering the same question of who it reached.
            if (a.Type === "call" && a.MailTo) {
                $main.append($('<div class="vas_098-actsub"></div>')
                    .text(a.MailTo).attr("title", a.MailTo));
            }

            // A meeting or task names where it is and whether it is done.
            if (a.Type === "appointment" || a.Type === "task") {
                var bits = [];
                if (a.Location) bits.push(a.Location);
                if (a.IsCancelled) bits.push(msg("ActCancelled", "Cancelled"));
                else if (a.IsClosed) bits.push(msg("ActCompleted", "Completed"));
                // What was e-mailed about this meeting or task. The count only —
                // the addresses, subjects and bodies are in the drawer, and a
                // meeting that generated several notices would otherwise push
                // everything else off the sub-line.
                var apptMails = activityMails(a);
                if (apptMails.length) bits.push(mailCountLabel(apptMails.length));
                if (bits.length) {
                    var sub = bits.join(" · ");
                    $main.append($('<div class="vas_098-actsub"></div>')
                        .text(sub).attr("title", sub));
                }
            }

            // A line edit names the line it landed on, on the same sub-line. The
            // headline stays "Updated <field>" — which field moved is the question,
            // and the row it moved on qualifies it. Dropped for a header edit,
            // which has no line to name.
            if (a.Type === "updated" && a.ChangeScope) {
                $main.append($('<div class="vas_098-actsub"></div>')
                    .text(a.ChangeScope).attr("title", a.ChangeScope));
            }
            // ...and the move itself: what the field held before the edit and
            // what it holds after, on a sub-line of its own.
            if (a.OldValue || a.NewValue) $main.append(changeDelta(a));
            $row.append($main);

            // Rows carrying a body are clickable; the caret shows the state.
            if (hasActivityBody(a)) {
                // A meeting or task opens onto the e-mails sent about it; every
                // other openable row onto its own message.
                var isAppt = (a.Type === "appointment" || a.Type === "task");
                var showHint = isAppt
                    ? msg("ShowMails", "Click to read the e-mails")
                    : msg("ShowMailBody", "Click to read the message");
                var hideHint = isAppt
                    ? msg("HideMails", "Click to hide the e-mails")
                    : msg("HideMailBody", "Click to hide the message");

                $row.addClass("vas_098-openable").attr("title", showHint);
                $row.on("click", function () {
                    var $panel = $row.next(".vas_098-actbody");
                    if (!$panel.length) return;
                    var nowOpen = !$row.hasClass("vas_098-open");
                    $row.toggleClass("vas_098-open", nowOpen)
                        .attr("title", nowOpen ? hideHint : showHint);
                    $panel.toggle(nowOpen);
                });
            }

            return $row;
        }

        // Only an e-mail carries a body worth opening; a mail stored without one
        // stays a plain, non-clickable row.
        // A letter opens like a mail: it is the same record in the same table,
        // with the same body and the same addresses on it.
        // A meeting or task opens onto the e-mails sent against it instead.
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
            return n + " " + (n === 1 ? msg("Email", "email") : msg("Emails", "emails"));
        }

        // The e-mail body, collapsed beneath its activity row. The full recipient
        // set (From / To / Cc / Bcc) heads it, so every address the mail went to is
        // on screen once the reader opens the message.
        function activityBody(a) {
            if (!hasActivityBody(a)) return null;

            var $panel = $('<div class="vas_098-actbody" style="display:none;"></div>');

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

            appendMailMeta($panel, "MailFrom", "From:", a.MailFrom);
            appendMailMeta($panel, "MailTo",   "To:",   a.MailTo);
            appendMailMeta($panel, "MailCc",   "Cc:",   a.MailCc);
            appendMailMeta($panel, "MailBcc",  "Bcc:",  a.MailBcc);
            $panel.append($('<p></p>').text(String(a.Body).trim()));
            return $panel;
        }

        // One e-mail inside an appointment's or task's drawer: who it went to and
        // what it was about, then when and by whom, then the message. Separated
        // from the one before it so several notices do not read as one.
        function activityMailEntry(m, separated) {
            var $wrap = $('<div class="vas_098-actmailitem"></div>');
            if (separated) $wrap.addClass("vas_098-actmailsplit");

            appendMailMeta($wrap, "MailTo", "To:", m.MailTo);
            appendMailMeta($wrap, "MailSubject", "Subject:",
                (m.Subject && String(m.Subject).trim())
                    ? m.Subject : msg("NoSubject", "(no subject)"));

            // "when · by whom", the same two parts in the same order as the row
            // above it.
            var when = formatDateTime(m.SentOn);
            if (m.SentBy) {
                when = when ? when + " · " + msg("By", "by") + " " + m.SentBy
                            : msg("By", "by") + " " + m.SentBy;
            }
            if (when) $wrap.append($('<div class="vas_098-actmeta"></div>').text(when));

            // The body is the thing the click was for; a mail filed without one
            // still shows its envelope rather than an empty gap.
            if (m.Body && String(m.Body).trim()) {
                $wrap.append($('<p></p>').text(String(m.Body).trim()));
            }
            return $wrap;
        }

        function appendMailMeta($panel, key, fallback, value) {
            if (!value || !String(value).trim()) return;
            $panel.append($('<div class="vas_098-actmeta"></div>')
                .text(msg(key, fallback) + " " + String(value).trim()));
        }

        // Row sub-line: every address the mail went to, written out in full — To,
        // then Cc, then Bcc, each behind its own label.
        //
        // It used to name the To list and count the rest as "+n more". That count
        // could only be resolved by opening the message, and a mail stored without
        // a body cannot be opened at all — so on those rows the Cc and Bcc
        // addresses were unreachable. The sub-line wraps rather than ellipsising
        // (stylesheet), so a long list is read on the row itself.
        //
        // A label with nothing behind it is left out entirely rather than printed
        // against a dash: this line lists recipients, and an empty Cc is not one.
        function recipientSummary(a) {
            var bits = [];
            appendAddressBit(bits, "MailTo",  "To:",  a.MailTo);
            appendAddressBit(bits, "MailCc",  "Cc:",  a.MailCc);
            appendAddressBit(bits, "MailBcc", "Bcc:", a.MailBcc);
            return bits.join(" · ");
        }

        function appendAddressBit(bits, key, fallback, value) {
            var text = (value === null || value === undefined) ? "" : String(value).trim();
            if (!text) return;
            bits.push(msg(key, fallback) + " " + text);
        }

        // allRecipients (the row's hover tooltip) and countAddresses (the "+n more"
        // tally) are gone with the abridged sub-line they served: the row now
        // writes every address out, so there is nothing left to count or to recover
        // on hover.

        // Every "a document was created from this requisition" row, and the
        // sentence it headlines with. Adding a document kind to the feed means
        // adding it here and to ACT_BADGE — nothing else changes.
        var DOC_CREATED_TEXT = {
            po:                 { key: "POCreated",           text: "PO Created" },
            grn:                { key: "GRNCreated",          text: "GRN Created" },
            grncomplete:        { key: "GRNCompleted",        text: "GRN Completed" },
            rfqcreated:         { key: "RFQCreated",          text: "RFQ Created" },
            movementcreated:    { key: "TransferCreated",     text: "Material Transfer Created" },
            internalusecreated: { key: "InternalUseCreated",  text: "Inventory Use Created" }
        };

        // The row's headline: WHAT happened. Who did it and when is written once,
        // by activityRow, in the row's own "when · by whom" slot — so none of the
        // sentences below carry a trailing "by <user>" any more. They used to, and
        // the actor then appeared in a different place on an e-mail row than on
        // every other kind.
        function activityText(a) {
            if (a.Type === "create") return msg("RequisitionCreated");
            if (a.Type === "status")
                return msg("RequisitionMarked") + " " + statusMeta().label;

            // Downstream documents name themselves, so the row reads
            // "PO Created — PO-000123": WHAT was created and WHICH document it is.
            // Who created it and when sit in the row's own "when · by whom" slot,
            // as on every other row.
            if (DOC_CREATED_TEXT.hasOwnProperty(a.Type)) {
                var d = DOC_CREATED_TEXT[a.Type];
                var label = msg(d.key, d.text);
                if (a.DocumentNo) label += " — " + a.DocumentNo;
                return label;
            }

            // An e-mail's headline is its subject; the recipient runs underneath it
            // and the sender sits with the timestamp, as on every other row.
            if (a.Type === "email") {
                return (a.Text || "").trim() || msg("NoSubject", "(no subject)");
            }

            // A field-level edit headlines with the FIELD that changed — the row's
            // badge already says "Updated", and the field is what tells one edit
            // apart from the next. Which record it landed on rides on the sub-line
            // beneath (activityRow).
            if (a.Type === "updated" && a.FieldName) {
                return msg("ActFieldUpdated", "Updated") + " " + a.FieldName;
            }

            // A meeting, task, call or letter headlines with its own subject, note
            // or title. Where it carries none the KIND stands in — a call with no
            // note reads "Call", not the comment wording the default below would
            // otherwise lend it.
            if (ACT_SOURCE_TYPES.hasOwnProperty(a.Type)) {
                var src = ACT_BADGE[a.Type];
                return (a.Text || "").trim() || msg(src.key, src.fallback);
            }

            return a.Text || msg("ActComment");
        }

        // ---- Notes ---- //

        // Every description entered against the requisition: the one typed on the
        // header (M_Requisition.Description) first, then the one typed on each
        // line (M_RequisitionLine.Description).
        //
        // The line descriptions used to be reachable only by reading the Items
        // table row by row — and they are not shown there at all, since the item
        // cell carries the product name, its attribute set and its search key. A
        // note written against a line is a note about the requisition, so it
        // belongs here with the header's.
        //
        // Each line's note is labelled with the line no and the product it was
        // written against, so a reader knows which row it annotates without
        // counting back to the table.
        //
        // The section exists only when the REQUISITION ITSELF carries a
        // description. With none, it is not drawn at all — heading included —
        // rather than standing as an empty card saying so.
        //
        // The header's description is the whole gate, deliberately: a requisition
        // with line notes but no description of its own shows no Notes section,
        // and those line notes are not reachable from the panel. The alternative —
        // opening the section for line notes alone — was considered and rejected;
        // the header description is what decides whether this requisition has
        // anything to say. Line notes remain a detail OF that section, not a
        // reason to raise it.
        function renderNotesPanel() {
            if (!data.Description || !String(data.Description).trim()) return null;

            var $notes = $('<div class="vas_098-notesbody"></div>');
            appendNoteText($notes, data.Description, null);

            var lines = (data.Lines) || [];
            for (var i = 0; i < lines.length; i++) {
                appendNoteText($notes, lines[i].Description, lineNoteLabel(lines[i]));
            }

            var $panel = $('<div class="vas_098-lowersec"></div>');
            var $card = $('<div class="vas_098-panelcard vas_098-notescard"></div>');
            $card.append($notes);
            $panel.append($card);
            return $panel;
        }

        // "Line 10 — Steel Bolt M8", the caption above a line's own note. Falls
        // back to the line no alone when the row names no product (a charge line
        // with nothing keyed), and to nothing at all when it has no line no either.
        function lineNoteLabel(ln) {
            var bits = [];
            if (+ln.Line > 0) bits.push(msg("Line", "Line") + " " + ln.Line);
            var name = (ln.ProductName || "").trim();
            if (name) bits.push(name);
            return bits.length ? bits.join(" — ") : "";
        }

        // Appends one note: its optional caption, then a paragraph per line of the
        // text (the header description is free text and often multi-line). A blank
        // note contributes nothing, caption included.
        function appendNoteText($notes, text, label) {
            if (!text || !String(text).trim()) return;

            var paras = String(text).split(/\r?\n+/);
            var $written = [];
            for (var i = 0; i < paras.length; i++) {
                var t = paras[i].trim();
                if (t) $written.push($('<p></p>').text(t));
            }
            if (!$written.length) return;

            if (label) {
                $notes.append($('<div class="vas_098-notelbl"></div>').text(label));
            }
            for (var j = 0; j < $written.length; j++) $notes.append($written[j]);
        }

        // ----------------------------------------------------------------- //
        //  Icons                                                             //
        // ----------------------------------------------------------------- //
        var SVG_ICONS = {
            user:      '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"/><circle cx="12" cy="7" r="4"/></svg>',
            arrow:     '<svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><line x1="5" y1="12" x2="19" y2="12"/><polyline points="12 5 19 12 12 19"/></svg>',
            check:     '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"><polyline points="20 6 9 17 4 12"/></svg>',
            warn:      '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M10.3 3.9 1.8 18a2 2 0 0 0 1.7 3h17a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0Z"/><line x1="12" y1="9" x2="12" y2="13"/><line x1="12" y1="17" x2="12.01" y2="17"/></svg>',
            chevLeft:  '<svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round"><path d="m15 18-6-6 6-6"/></svg>',
            chevRight: '<svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round"><path d="m9 18 6-6-6-6"/></svg>',
            chevrons:  '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><polyline points="17 11 12 6 7 11"/><polyline points="17 18 12 13 7 18"/></svg>',
            transfer:  '<svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="17 1 21 5 17 9"/><path d="M3 11V9a4 4 0 0 1 4-4h14"/><polyline points="7 23 3 19 7 15"/><path d="M21 13v2a4 4 0 0 1-4 4H3"/></svg>',
            external:  '<svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M16 3h5v5"/><path d="M21 3 9 15"/><path d="M21 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V7a2 2 0 0 1 2-2h6"/></svg>',
            rfq:       '<svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><line x1="22" y1="2" x2="11" y2="13"/><polygon points="22 2 15 22 11 13 2 9 22 2"/></svg>',
            list:      '<svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><line x1="8" y1="6" x2="21" y2="6"/><line x1="8" y1="12" x2="21" y2="12"/><line x1="8" y1="18" x2="21" y2="18"/><line x1="3" y1="6" x2="3.01" y2="6"/><line x1="3" y1="12" x2="3.01" y2="12"/><line x1="3" y1="18" x2="3.01" y2="18"/></svg>',
            clock:     '<svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="9"/><polyline points="12 7 12 12 15 14"/></svg>',
            note:      '<svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><path d="M14 2v6h6M9 13h6M9 17h4"/></svg>',
            // Reference strip + Documents section.
            doc:       '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8Z"/><path d="M14 2v6h6"/><path d="M8 13h8"/><path d="M8 17h8"/></svg>',
            folder:    '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M4 20a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2h5l2 3h7a2 2 0 0 1 2 2v9a2 2 0 0 1-2 2Z"/></svg>',
            factory:   '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M2 20V9l6 4V9l6 4V9l6 4v7Z"/><path d="M2 20h20"/><path d="M7 20v-4"/><path d="M12 20v-4"/><path d="M17 20v-4"/></svg>',
            wrench:    '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M14.7 6.3a4 4 0 0 1 5 5l-9.7 9.7a2.1 2.1 0 0 1-3-3l9.7-9.7Z"/><path d="M14.7 6.3 9 1"/></svg>',
            clipboard: '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><rect x="8" y="2" width="8" height="4" rx="1"/><path d="M16 4h2a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2h2"/><path d="m9 14 2 2 4-4"/></svg>',
            refresh:   '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M3 12a9 9 0 0 1 15-6.7L21 8"/><path d="M21 3v5h-5"/><path d="M21 12a9 9 0 0 1-15 6.7L3 16"/><path d="M3 21v-5h5"/></svg>',
            pencil:    '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M12 20h9"/><path d="M16.5 3.5a2.12 2.12 0 0 1 3 3L7 19l-4 1 1-4Z"/></svg>',
            arrowUpRight: '<svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M7 17 17 7M7 7h10v10"/></svg>'
        };

        function svgIcon(name) {
            var $wrap = $('<span class="vas_098-ic"></span>');
            $wrap[0].innerHTML = SVG_ICONS[name] || "";
            return $wrap;
        }

        // ----------------------------------------------------------------- //
        //  Formatting                                                        //
        // ----------------------------------------------------------------- //

        function currencySymbol() { return data && data.CurSymbol ? data.CurSymbol : "$"; }

        // Whole-currency display to match the reference design.
        function money(value) {
            var v = Math.round(+value || 0);
            return currencySymbol() + " " + v.toLocaleString(window.navigator.language);
        }

        // The same, kept to the currency's own precision — for a per-unit rate,
        // where rounding to whole currency units destroys the figure rather than
        // tidying it (0.75 an EA would print as 1).
        function moneyPrecise(value) {
            var p = (+data.StdPrecision >= 0) ? +data.StdPrecision : 2;
            return currencySymbol() + " " + formatNumber(value, p);
        }

        function formatNumber(value, precision) {
            var p = (precision >= 0) ? precision : 0;
            return (+value || 0).toLocaleString(window.navigator.language, {
                minimumFractionDigits: p, maximumFractionDigits: p
            });
        }

        function stripTime(d) {
            return new Date(d.getFullYear(), d.getMonth(), d.getDate()).getTime();
        }

        // Parses a .NET/Newtonsoft DB value into a Date.
        //
        // asUtc = true  → for genuine TIMESTAMPS (Created, activity, completion
        //   moments). The DB stores these in UTC and the server emits no timezone
        //   designator (e.g. "2026-08-05T10:00:00"), which the browser would
        //   otherwise read as local — so the panel printed the stored UTC clock
        //   and every creation time read hours out. Tagging it "Z" makes
        //   toLocale* render it in the viewer's own system zone.
        // asUtc = false → for DATE-ONLY fields (document / required dates). These
        //   carry no meaningful time-of-day, so the wall-clock value is parsed as
        //   it stands and never shifted — the calendar day shown always matches
        //   the day stored, whatever the viewer's zone.
        // Strings already carrying a "Z" or a ±hh:mm offset are left untouched.
        //
        // This replaces the earlier component-by-component "wall clock" parse,
        // which treated every value as already-local and so displayed a UTC
        // timestamp verbatim.
        function parseDbDate(value, asUtc) {
            if (!value) return null;
            if (value instanceof Date) return isNaN(value.getTime()) ? null : value;
            var s = String(value);
            var hasTz = /(z|[+-]\d{2}:?\d{2})$/i.test(s);
            var isDateTime = /^\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}/.test(s);
            if (asUtc && isDateTime && !hasTz) {
                s = s.replace(" ", "T") + "Z";
            } else if (!asUtc && isDateTime) {
                // Keep the calendar date: drop any zone marker and parse as local
                // so no conversion can roll the day over.
                s = s.replace(" ", "T").replace(/(z|[+-]\d{2}:?\d{2})$/i, "");
            }
            var d = new Date(s);
            return isNaN(d.getTime()) ? null : d;
        }

        // Kept for the day-count arithmetic, which compares two DATE-ONLY fields
        // and must not have either shifted across a midnight.
        function parseServerDate(value) {
            return parseDbDate(value, false);
        }

        function formatDate(value) {
            var d = parseDbDate(value, false);
            if (!d) return "";
            try {
                return d.toLocaleDateString(window.navigator.language,
                    { year: "numeric", month: "short", day: "numeric" });
            } catch (e) { return d.toDateString(); }
        }

        function formatDateShort(value) {
            var d = parseDbDate(value, false);
            if (!d) return "";
            try {
                return d.toLocaleDateString(window.navigator.language, { month: "short", day: "numeric" });
            } catch (e) { return ""; }
        }

        // The calendar day of a stored TIMESTAMP, in the viewer's own zone — for
        // the progress stepper, whose stages are dated by when they happened
        // rather than by a document field. Reading one with formatDateShort would
        // print the UTC day, so a requisition raised late in the evening dated to
        // the following morning.
        function formatStampDateShort(value) {
            var d = parseDbDate(value, true);
            if (!d) return "";
            try {
                return d.toLocaleDateString(window.navigator.language, { month: "short", day: "numeric" });
            } catch (e) { return ""; }
        }

        function formatDateTime(value) {
            var d = parseDbDate(value, true);
            if (!d) return "";
            try {
                var dp = d.toLocaleDateString(window.navigator.language, { month: "short", day: "numeric" });
                var tp = d.toLocaleTimeString(window.navigator.language, { hour: "2-digit", minute: "2-digit" });
                return dp + ", " + tp;
            } catch (e) { return d.toString(); }
        }

        this.getRoot = function () { return $root; };
    };

    VAS.VAS_098_PurchaseRequisition.prototype.startPanel = function (windowNo, curTab) {
        this.windowNo = windowNo;
        this.curTab = curTab;
        if (curTab && typeof curTab.getAD_Table_ID === "function") {
            this.table_ID = curTab.getAD_Table_ID();
        }
        this.init();
        // Watch the tab itself so New Record / Copy Record (neither of which
        // reliably calls refreshPanelData) still empty the panel.
        if (curTab && typeof curTab.addDataStatusListener === "function") {
            try { curTab.addDataStatusListener(this.tabDataListener); } catch (e) { }
        }
    };

    /* Update tab panel based on selected record */
    VAS.VAS_098_PurchaseRequisition.prototype.refreshPanelData = function (recordID, selectedRow) {
        // The insert check is what makes New Record / Copy Record behave:
        // the id handed in for an unsaved row can still be the previously
        // selected (or copied-from) record's, so the tab's own insert state
        // decides, not the id.
        if (selectedRow == undefined || recordID <= 0 || isTabInserting(this.curTab)) {
            this.record_ID = 0;
            this.clear();
            return;
        }
        this.record_ID = recordID;
        this.selectedRow = selectedRow;
        // Held rather than fetched outright: the insert flag is not always up yet
        // when we get here, so scheduleFetch asks once more before loading.
        this.scheduleFetch(recordID);
    };

    /* Set width as per window width */
    VAS.VAS_098_PurchaseRequisition.prototype.sizeChanged = function (width) {
        this.panelWidth = width;
    };

    /* Release variables from memory */
    VAS.VAS_098_PurchaseRequisition.prototype.dispose = function () {
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
