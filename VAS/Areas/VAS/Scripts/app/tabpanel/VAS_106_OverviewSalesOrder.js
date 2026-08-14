/************************************************************
 * Module Name    : VAS
 * Purpose        : Sales Order Overview tab panel. Renders a review-and-act
 *                  overview of the selected sales order (C_Order, IsSOTrx = 'Y'):
 *                  header identity, customer + addresses, created-from origin
 *                  documents, KPI strip, a 6-stage order progress stepper and
 *                  collapsible sections — Order Lines (with per-line contract
 *                  flow), Delivery Readiness, Deliveries, Invoices, Notes and
 *                  Activity, each drawn only when it has something to show. Data
 *                  comes from VAS_106_OverviewSalesOrder/GetSalesOrderOverview.
 *
 *                  One write action is wired to the server (never mutate
 *                  documents from the browser): Create Contract from a service /
 *                  charge line (CreateContract).
 * Chronological development:
 *   VAI163   2026-07-08  Created
 *   VAI163   2026-08-05  Class prefix renamed MPC-vaso- -> vas_106- so the panel's
 *                          styles cannot collide with another panel's.
 *   VAI163   2026-08-05  New Record / Copy Record now empty the panel instead
 *                        of leaving the previously selected record on screen.
 *                        Both refreshPanelData and a new data-status listener
 *                        ask isTabInserting(), which reads GridTab.gridTable
 *                        .getIsInserting() — the flag GridTable.dataNew() raises
 *                        for both actions. The record id cannot answer it: a
 *                        copied row carries the source record's key until saved.
 *                        Ported from VAS_092.
 *   VAI163   2026-08-06  Activity paginates at 15 rows a page (ACTIVITY_PER_PAGE).
 *                        The panel had no pager of its own, so buildPager() /
 *                        pagerButton() and the chevLeft / chevRight icons were added
 *                        here, modelled on VAS_099. A feed that fits on one page
 *                        shows no controls, and the section's count badge keeps
 *                        counting the whole feed. Ported from VAS_092.
 *   VAI163   2026-08-07  Emits the vas_106-prefixed modifier classes the
 *                        stylesheet now uses, the runtime-built ones included
 *                        ("vas_106-tone-" + tone).
 *   VAI163   2026-08-07  New Record no longer leaves the previous order on the
 *                        panel. Two causes: refreshPanelData could run before
 *                        GridTable raised its insert flag, so it fetched the row
 *                        the user had just left; and the first (slow) reply for
 *                        that row landed AFTER the clear and repainted it.
 *                        refreshPanelData now goes through scheduleFetch, which
 *                        holds REFRESH_DELAY_MS and re-asks isTabInserting(),
 *                        and every fetch carries a token (fetchToken) that a
 *                        clear or a newer fetch invalidates — a reply that
 *                        arrives holding a stale token is dropped.
 *   VAI163   2026-08-12  - Bill to and Ship to take a line each
 *                          (appendAddressLine). Joined with a "·" into one run of
 *                          text they read as a single address wherever the panel
 *                          was narrow enough to wrap them — and the two are
 *                          frequently different places.
 *                        - Low (7) and Minor (9) priority read GREEN. Grey said
 *                          "no priority set" when what they mean is the lowest
 *                          priority there is, and Minor had no case at all — it
 *                          fell through to "Normal priority".
 *                        - The shipping rule reads the DICTIONARY's name for
 *                          C_Order.DeliveryRule (DeliveryRuleName, model side), so
 *                          it matches the order screen. The built-in map is the
 *                          fallback and gains 'R' (After Receipt), whose absence
 *                          is what showed a bare "R" on an order carrying it.
 *                        - Removed the action bar: Complete Sales Order, Create
 *                          Delivery, Create Invoice, the state note above them,
 *                          hasUndelivered() and handleCompleteSalesOrder(). Those
 *                          actions belong to the sales order window, and two of
 *                          them only ever raised a "available shortly" toast. The
 *                          note repeated the header status pill and the stepper.
 *                        - The Posted badge is drawn only once the order IS
 *                          posted. It read "Not Posted" on every drafted order —
 *                          not news about a document that cannot be posted yet.
 *                        - Order Lines, Delivery Readiness, Deliveries, Invoices,
 *                          Notes and Activity are drawn only when they have rows;
 *                          an empty section (heading included) is not rendered at
 *                          all, where each used to stand as a frame saying it had
 *                          nothing.
 *                        - Activity moved to the BOTTOM, below Notes: it is the
 *                          longest section and it pages, so anything under it was
 *                          pushed off the panel.
 *   VAI163   2026-08-12  - Timestamps render in the viewer's local system zone.
 *                          The DB stores them in UTC and the server emits no zone
 *                          designator, so `new Date(value)` read them as local and
 *                          the Activity feed showed the stored UTC clock. Added
 *                          parseDbDate: it tags a bare TIMESTAMP "Z"
 *                          (formatDateTime) while a DATE-ONLY field is still
 *                          parsed as it stands (formatDate) so its calendar day
 *                          cannot roll over. Ported from VAS_098 / VAS_099.
 *                        - A line's sub-line carries its UNIT OF MEASURE in place
 *                          of the product search key: the key repeats what the
 *                          name identifies, where the unit is what the quantity
 *                          beside it is counted in and was nowhere on the row.
 *                          Same on the Delivery Readiness rows.
 *                        - Lines show the Attribute Set Instance directly under
 *                          the product name (model side, joined only for a real
 *                          instance so the dictionary's "--" row cannot print).
 *                          Same on the Delivery Readiness rows.
 *                        - A product name that ellipsises carries the whole of it
 *                          on a hover tooltip, so nothing is unreadable and the
 *                          row layout is unchanged.
 *                        - The Deliveries and Invoices sections are replaced by
 *                          ONE Documents section, built like the Purchase Order
 *                          overview's: shipments, invoices and customer receipts
 *                          in a single four-column table (Document / Date /
 *                          Status / Amount), a per-kind count in the header, and
 *                          rows that open the record they name. Receipts were not
 *                          listed anywhere before. The order's invoiced / yet-to-
 *                          invoice footer moves onto it — that is a fact about the
 *                          ORDER, not about the invoice rows. The Deliveries and
 *                          Invoices COLLECTIONS stay: the KPI strip and the
 *                          progress stepper are derived from them.
 *   VAI163   2026-08-12  - The progress line's second stage is COMPLETED, dated by
 *                          when the order actually completed (the workflow's
 *                          DocComplete stamp, model side). It read "Confirmed" and
 *                          was dated by DateOrdered — a document field a user can
 *                          back-date.
 *                        - Shipped and Delivered are dated by the LATEST delivery
 *                          raised against the order (lastDeliveryDate); both
 *                          carried no date at all.
 *                        - The Contract column is drawn only when the order has a
 *                          SERVICE line (hasContractLines). A charge cannot carry
 *                          a contract and neither can a stocked item, so an order
 *                          without one drops the column and the table runs six
 *                          tracks (vas_106-no-contract).
 *                        - The contract toggle is READ ONLY and stands alone: no
 *                          "Contract" caption beside it, no document-number chip,
 *                          and no inline create form (buildContractForm, its field
 *                          helpers, validateContractForm, handleCreateContract and
 *                          their handlers are gone, as is the hint that explained
 *                          the flow). Which contract it is stays on its tooltip.
 *                          The panel is read-only now: it opens records, nothing
 *                          else.
 *                        - A cell with no value renders BLANK rather than a dash.
 *                          A dash reads as a figure that could not be worked out;
 *                          blank reads as "does not apply", which is the truth for
 *                          a service line's delivered quantity or a shipment's
 *                          amount.
 *                        - Delivery Readiness reports Ready or SHORT — "Awaited"
 *                          is gone. It was the fallback for an unclassified row
 *                          and read as though the goods were on their way, when
 *                          what it means is the warehouse does not hold enough.
 *                        - A comment names its author: every Activity row now
 *                          carries "when · by whom" in the same place, and a
 *                          note's text wraps to three lines instead of running off
 *                          the row. The author also resolves for platform-logged
 *                          notes (model side).
 *                        - Sections no longer collapse. The chevron and the click
 *                          that drove it are gone and the header is a plain div —
 *                          every section is simply open, always.
 *   VAI163   2026-08-12  Delivery Readiness leads each row with its UNIT, on the
 *                        same line as the product name ("EACH · Bolt M8") rather
 *                        than on a sub-line under it: the quantities are what the
 *                        table is for, so the unit they are counted in reads
 *                        first. Only the name ellipsises; both are on the row's
 *                        tooltip. Order Lines keeps the unit on its sub-line,
 *                        beside the line family it shares that line with.
 *   VAI163   2026-08-12  - A document opens on the SALES side. Every record this
 *                          panel lists belongs to a table that serves both — an AR
 *                          invoice, an AR receipt, a shipment — and the browser's
 *                          zoom lookup resolved the purchase side, so an invoice
 *                          number opened the AP Invoice screen. openRecord now
 *                          names the window (WINDOW_NAME_BY_TABLE, resolved through
 *                          the new GetWindow_ID endpoint) and asks the zoom target
 *                          for IsSOTrx = true when it falls through.
 *                        - The Documents table drops its Invoiced / Yet-to-invoice
 *                          footer: the KPI strip already reports the invoiced
 *                          amount and the share of the order it covers.
 *                        - Order Lines and Delivery Readiness page at 25 rows
 *                          (ROWS_PER_PAGE), each with its own page state, reusing
 *                          the activity pager. The lines table's totals footer
 *                          still covers the WHOLE order, never the page. Activity
 *                          already paged at 15 — but the model capped the feed at
 *                          15 too, so the pager could never appear (model side).
 *                        - Quantities and the unit price read in the line's
 *                          SELECTED UOM (model side): the row was labelled with
 *                          that unit while carrying the product's BASE figures, so
 *                          a line sold as 2 BOX of an EA-held product read as 24 at
 *                          the per-EA price. Delivery Readiness converts its
 *                          pending and on-hand quantities onto the same scale.
 *   VAI163   2026-08-12  - The Quotation chip opens the Sales QUOTATION screen.
 *                          Naming the window by TABLE was not enough: a quotation,
 *                          a blanket and an order are all C_Order records living
 *                          on three different windows, so originChip takes a
 *                          window name of its own (data-open-window) and
 *                          openRecord prefers it.
 *                        - Created From gains the blanket sales order the order
 *                          was released against (model side). An order created
 *                          from a blanket carried no origin and the strip called
 *                          it "Manual".
 *                        - Each line carries its change history in a drawer
 *                          beneath it, opened by a history button on the product
 *                          name — the Purchase Order overview's shape, on this
 *                          panel's own columns (historyByLine / buildHistToggle /
 *                          buildLineHistory). Open drawers are keyed by
 *                          C_OrderLine_ID so a pager repaint keeps them, and reset
 *                          with the record.
 *   VAI163   2026-08-12  - Created From gains a Contract chip: the contract master
 *                          on the header (C_Order.VAS_ContractMaster_ID) or the
 *                          service contract behind one of the lines
 *                          (C_OrderLine.C_Contract_ID), whichever the order
 *                          carries. An order raised against a contract reported no
 *                          origin and the strip called it "Manual".
 *                        - The Project chip names the project by its NUMBER
 *                          (C_Project.Value) with the name on its tooltip — the
 *                          strip identifies documents, and it was showing the name.
 *                          A project origin could also go missing entirely: every
 *                          origin shared ONE query, so a schema without any one of
 *                          the optional columns lost them all and the order read
 *                          "Manual". Each is read under its own guard now (model
 *                          side).
 *   VAI163   2026-08-12  The Contract chip opens the contract's own screen.
 *                        openRecord gains a fourth and final step: when neither a
 *                        named window nor the client's zoom target resolves, the
 *                        server is asked which window the TABLE opens in
 *                        (GetWindowIdByTable). C_Contract and VAS_ContractMaster
 *                        are maintained by module windows whose names cannot be
 *                        hard-coded here, and the browser-side zoom lookup only
 *                        knows tables the client has cached, so the chip fell
 *                        through to the "cannot open" toast. Any future chip gets
 *                        the same safety net. Ported from VAS_102.
 *                        The zoom target is also only asked for the SALES side of
 *                        a table that actually serves both (DUAL_PURPOSE_TABLES) —
 *                        asking it of a single-screen table like C_Contract could
 *                        resolve the wrong window, or none.
 *   VAI163   2026-08-12  - Activity lists the e-mails sent against the order
 *                          (EventType "Email", model side). The row is the subject,
 *                          with the recipient beneath it (.vas_106-actSub) and the
 *                          date / time · sender in the usual place; the MESSAGE
 *                          itself stays collapsed and opens on click
 *                          (.vas_106-actRow.vas_106-is-openable / -actCaret /
 *                          -actBody), because a body is far too heavy to sit in a
 *                          feed. The Note badge takes the "note" icon, which it now
 *                          has to give up "mail" to the e-mail rows.
 *                        - The status pill reads "Completed" in green for an order
 *                          that has reached that state. It read "Confirmed" in blue
 *                          until every line had shipped, which named the same state
 *                          twice and gave a completed order the tone of a notice
 *                          rather than a milestone. A part-shipped order still names
 *                          its own state.
 *                        - The Posted badge joins the header pills, straight after
 *                          that status pill: a document that has reached the ledger
 *                          says so beside the state that got it there, instead of
 *                          only in the Order Progress heading. Still drawn only once
 *                          the order IS posted.
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

    VAS.VAS_106_OverviewSalesOrder = function () {
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
                // New (unsaved) record — nothing to show against it. `data` is
                // tested too: a fetch that has already painted must be cleared
                // even if record_ID was reset by whoever scheduled it.
                if ($self.record_ID || data) {
                    $self.record_ID = 0;
                    $self.clear();
                }
                return;
            }
            if (rid !== $self.record_ID) {
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
        // Maximum activity rows shown per page; the feed paginates beyond this.
        // An order accumulates every status change, delivery, invoice and note,
        // and an unpaged feed made the section scroll past everything below it.
        // The section's own count badge still counts the WHOLE feed, not the page.
        var ACTIVITY_PER_PAGE = 15;
        var activityPage = 0;   // current Activity page (0-based)

        // Order Lines and Delivery Readiness page client-side (the whole set
        // arrives in one payload); both reset to the first page whenever a
        // different record is loaded. 25 rows a page, matching the other overview
        // panels: the pager only appears once a table actually exceeds that.
        var ROWS_PER_PAGE = 25;
        var linesPage = 0;
        var readinessPage = 0;

        // Which lines have their change-history drawer open, keyed by
        // C_OrderLine_ID so a pager repaint (or a move to another page and back)
        // keeps what the reader opened. Reset with the record.
        var lineHistOpen = {};

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

        this.init = function () {
            $root = $('<div class="vas_106-root"></div>');
            $body = $('<div class="vas_106-body"></div>');
            $emptyState = $('<div class="vas_106-empty" style="display:none;"></div>');
            $emptyState.text(getMsg("VAS_106_NoData", "No sales order selected"));
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

        // Some AD_Message keys may not be seeded yet; fall back to a readable
        // English default so the panel never renders raw keys.
        function getMsg(key, fallback) {
            try {
                var m = VIS.Msg.getMsg(key);
                if (m && m !== key) return m;
            } catch (e) { }
            return fallback != null ? fallback : key;
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
            showBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + "VAS_106_OverviewSalesOrder/GetSalesOrderOverview",
                type: "GET",
                dataType: "json",
                data: { C_Order_ID: recordID },
                success: function (raw) {
                    // Reply for a record the panel has already left (a New
                    // Record cleared it, or a newer row was selected). Whoever
                    // superseded us owns the busy indicator now, so leave it be.
                    if (token !== fetchToken) return;
                    data = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                    // A newly selected order starts on the first page of every
                    // paged section.
                    activityPage = 0;
                    linesPage = 0;
                    lineHistOpen = {};
                    readinessPage = 0;
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
            invalidateFetch();
            data = null;
            activityPage = 0;
            linesPage = 0;
            lineHistOpen = {};
            readinessPage = 0;
            render();
            showBusy(false);
        };

        function render() {
            $body.empty();
            if (!data || !data.C_Order_ID) {
                $body.hide();
                $emptyState.show();
                return;
            }
            $emptyState.hide();
            $body.show();

            renderHeader();
            renderCustomerCard();
            renderCreatedFrom();
            renderKpis();
            renderStepper();
            // Each of the sections below draws nothing at all — not even its
            // heading — when it has nothing to list. They used to stand as empty
            // frames saying "No deliveries yet", which on a fresh order was most
            // of the panel spent on saying nothing.
            renderOrderLines();
            renderDeliveryReadiness();
            // One Documents section in place of the separate Deliveries and
            // Invoices ones: shipments, invoices and receipts in a single table.
            renderDocuments();
            renderNotes();
            // Activity comes LAST: it is the longest section (it pages, and grows
            // for the life of the document), so anything under it was pushed off
            // the bottom of the panel.
            renderActivity();
        }

        // ----------------------------------------------------------------- //
        //  Derived-value helpers (single source of truth for the UI)         //
        // ----------------------------------------------------------------- //

        function stockLines() {
            var out = [], lines = data.Lines || [];
            for (var i = 0; i < lines.length; i++) {
                if (lines[i].LineType === "product") out.push(lines[i]);
            }
            return out;
        }

        // Fulfilment: fully-delivered stock lines / total stock lines.
        function fulfilment() {
            var st = stockLines(), full = 0;
            for (var i = 0; i < st.length; i++) {
                if (st[i].QtyOrdered > 0 && st[i].QtyDelivered >= st[i].QtyOrdered) full++;
            }
            return { full: full, total: st.length, pct: st.length ? Math.round(full / st.length * 100) : 0 };
        }

        // Invoiced: Σ invoice grand totals vs order grand total + payment state.
        function invoiced() {
            var inv = data.Invoices || [], amt = 0, allPaid = inv.length > 0;
            for (var i = 0; i < inv.length; i++) {
                amt += (+inv[i].GrandTotal || 0);
                if (!inv[i].IsPaid) allPaid = false;
            }
            var gt = +data.GrandTotal || 0;
            return {
                amount: amt,
                pct: gt > 0 ? Math.round(amt / gt * 100) : 0,
                state: inv.length === 0 ? "" : (allPaid ? getMsg("VAS_106_Paid", "paid") : getMsg("VAS_106_Unpaid", "unpaid")),
                fully: gt > 0 && amt >= gt
            };
        }

        // Delivery readiness: available (min(onhand,pending)) / pending units.
        function readiness() {
            var rows = data.DeliveryReadiness || [], pending = 0, avail = 0, shortCount = 0;
            for (var i = 0; i < rows.length; i++) {
                var p = +rows[i].PendingQty || 0, oh = +rows[i].QtyOnHand || 0;
                if (p <= 0) continue;
                pending += p;
                avail += Math.min(oh, p);
                if (oh < p) shortCount++;
            }
            return {
                pending: pending, available: avail, shortCount: shortCount,
                pct: pending > 0 ? Math.round(avail / pending * 100) : 100
            };
        }

        function isCompleted() { return data.DocStatus === "CO" || data.DocStatus === "CL"; }

        // ----------------------------------------------------------------- //
        //  Header                                                            //
        // ----------------------------------------------------------------- //

        // PriorityRule -> label + tone.
        //
        // Low (7) and Minor (9) read GREEN, not grey. Grey says "no priority set"
        // when what these mean is the lowest priorities there are — nothing is
        // pressing about this order, which is good news and should look like it.
        // Minor had no case of its own at all and fell through to "Normal".
        function priorityMeta() {
            switch (data.PriorityRule) {
                case "1": return { label: getMsg("VAS_106_Urgent", "Urgent priority"), tone: "risk", icon: "chevUp" };
                case "3": return { label: getMsg("VAS_106_High", "High priority"), tone: "warning", icon: "chevUp" };
                case "5": return { label: getMsg("VAS_106_Medium", "Medium priority"), tone: "info", icon: null };
                case "7": return { label: getMsg("VAS_106_Low", "Low priority"), tone: "success", icon: null };
                case "9": return { label: getMsg("VAS_106_Minor", "Minor priority"), tone: "success", icon: null };
                default:  return { label: getMsg("VAS_106_Normal", "Normal priority"), tone: "neutral", icon: null };
            }
        }

        // SOCreditStatus -> label + tone.
        function creditMeta() {
            switch (data.CreditStatus) {
                case "O": return { label: getMsg("VAS_106_CreditOK", "Credit OK"), tone: "success" };
                case "H": return { label: getMsg("VAS_106_CreditHold", "Credit Hold"), tone: "risk" };
                case "S": return { label: getMsg("VAS_106_CreditStop", "Credit Stop"), tone: "risk" };
                case "W": return { label: getMsg("VAS_106_CreditWatch", "Credit Watch"), tone: "warning" };
                case "X": return { label: getMsg("VAS_106_NoCreditCheck", "No Credit Check"), tone: "neutral" };
                default:  return { label: getMsg("VAS_106_CreditUnknown", "Credit Unknown"), tone: "neutral" };
            }
        }

        // Live order-state pill (drives the header + action-bar message).
        function statusMeta() {
            var f = fulfilment();
            if (data.DocStatus === "VO") return { label: getMsg("VAS_106_Voided", "Voided"), tone: "risk" };
            if (data.DocStatus === "CL") return { label: getMsg("VAS_106_Closed", "Closed"), tone: "neutral" };
            if (data.DocStatus === "DR" || data.DocStatus === "IP")
                return { label: getMsg("VAS_106_Draft", "Draft"), tone: "neutral" };
            // Completed family. Reaching Completed is what the pill reports, so it
            // says so — in green. It used to read "Confirmed" in blue until every
            // line had shipped, which named the same state twice and gave an
            // ordinary completed order the tone of a notice rather than of the
            // milestone it is. A part-shipped order still names its own state,
            // which is the one thing here the delivery progress genuinely adds.
            if (f.total > 0 && f.full > 0 && f.full < f.total)
                return { label: getMsg("VAS_106_PartiallyDelivered", "Partially Delivered"), tone: "warning" };
            return { label: getMsg("VAS_106_Completed", "Completed"), tone: "success" };
        }

        function renderHeader() {
            var $strip = $('<section class="vas_106-hdr"></section>');
            var $top = $('<div class="vas_106-hdrTop"></div>');

            var $tl = $('<div class="vas_106-hdrTitleWrap"></div>');
            $tl.append($('<div class="vas_106-hdrTitle"></div>').text(
                getMsg("VAS_106_SalesOrder", "Sales Order") +
                (data.DocumentNo ? " — " + data.DocumentNo : "")));

            var subBits = [];
            if (data.POReference) subBits.push(getMsg("VAS_106_CustomerPO", "Customer PO") + " " + data.POReference);
            var od = formatDate(data.DateOrdered);
            if (od) subBits.push(getMsg("VAS_106_Ordered", "Ordered") + " " + od);
            if (subBits.length)
                $tl.append($('<div class="vas_106-hdrSub"></div>').text(subBits.join(" · ")));
            $top.append($tl);

            var pm = priorityMeta(), cm = creditMeta(), sm = statusMeta();
            var $pills = $('<div class="vas_106-hdrPills"></div>');
            $pills.append(headerPill(pm.label, pm.tone, pm.icon, false));
            $pills.append(headerPill(cm.label, cm.tone, null, true));
            $pills.append(headerPill(sm.label, sm.tone, null, true));
            // Posted comes straight after the status pill: a document that has
            // reached the ledger says so beside the state that got it there. Drawn
            // only once the order IS posted — "Not Posted" is not news about a
            // document that cannot be posted yet, the same rule the Order Progress
            // badge follows.
            if (data.Posted === "Y")
                $pills.append(headerPill(getMsg("VAS_106_Posted", "Posted"), "success", null, true));
            $top.append($pills);

            $strip.append($top);
            $body.append($strip);
        }

        function headerPill(label, tone, icon, withDot) {
            var $p = $('<span class="vas_106-hdrPill"></span>').addClass("vas_106-tone-" + (tone || "neutral"));
            if (icon) $p.append(svgIcon(icon));
            if (withDot) $p.append($('<span class="vas_106-hdrDot"></span>'));
            $p.append($('<span></span>').text(label));
            return $p;
        }

        // ----------------------------------------------------------------- //
        //  Customer identity card                                            //
        // ----------------------------------------------------------------- //

        // The order's shipping rule — C_Order.DeliveryRule.
        //
        // The server sends the DICTIONARY's own name for the value stored on the
        // record (DeliveryRuleName, from the reference list behind the column), so
        // the panel reads exactly what the order screen reads, translations and
        // customer-added values included.
        //
        // The map below is only the fallback for a deployment whose reference list
        // could not be read. It used to be the whole answer and was missing 'R'
        // (After Receipt), so an order carrying that rule showed a bare "R" — the
        // wrong value the panel was reported for.
        function shippingRuleLabel() {
            if (data.DeliveryRuleName) return data.DeliveryRuleName;
            switch (data.DeliveryRule) {
                case "A": return getMsg("VAS_106_ShipAvailability", "Availability");
                case "F": return getMsg("VAS_106_ShipForce", "Force");
                case "L": return getMsg("VAS_106_ShipCompleteLine", "Complete Line");
                case "M": return getMsg("VAS_106_ShipManual", "Manual");
                case "O": return getMsg("VAS_106_ShipCompleteOrder", "Complete Order");
                case "R": return getMsg("VAS_106_ShipAfterReceipt", "After Receipt");
                default:  return data.DeliveryRule || "";
            }
        }

        function renderCustomerCard() {
            var $card = $('<section class="vas_106-hdrCard"></section>');

            // Left: customer identity.
            var $left = $('<div class="vas_106-hdrColL"></div>');
            $left.append($('<div class="vas_106-fLabel"></div>').text(getMsg("VAS_106_Customer", "Customer")));
            $left.append($('<div class="vas_106-custName"></div>').text(data.CustomerName || ""));

            // Bill to and Ship to each take their OWN line. They used to be joined
            // with a "·" into one run of text, which read as a single address on
            // any panel narrow enough to wrap it — and the two are frequently
            // different places, which is the whole reason they are both here.
            appendAddressLine($left, "VAS_106_BillTo", "Bill to", data.BillToAddress);
            appendAddressLine($left, "VAS_106_ShipTo", "Ship to", data.ShipToAddress);

            var $contact = $('<div class="vas_106-custContact"></div>');
            appendContactBit($contact, "user", data.ContactName);
            appendContactBit($contact, "phone", data.ContactPhone);
            appendContactBit($contact, "mail", data.ContactEmail);
            if ($contact.children().length) $left.append($contact);
            $card.append($left);

            // Right: commercial terms.
            var $right = $('<div class="vas_106-hdrColR"></div>');
            if (data.SalesRepName)    $right.append(headerField(getMsg("VAS_106_Salesperson", "Salesperson"), data.SalesRepName));
            if (data.PaymentTermName) $right.append(headerField(getMsg("VAS_106_PaymentTerms", "Payment Terms"), data.PaymentTermName));
            if (data.PriceListName)   $right.append(headerField(getMsg("VAS_106_Pricelist", "Pricelist"), data.PriceListName));
            if (data.WarehouseName)   $right.append(headerField(getMsg("VAS_106_FulfilmentWarehouse", "Fulfilment Warehouse"), data.WarehouseName));
            $right.append(headerField(getMsg("VAS_106_ShippingRule", "Shipping Rule"), shippingRuleLabel()));
            $card.append($right);

            $body.append($card);
        }

        function headerField(label, value) {
            var $f = $('<div class="vas_106-hdrField"></div>');
            $f.append($('<div class="vas_106-fLabel"></div>').text(label));
            $f.append($('<div class="vas_106-fVal"></div>').text(value));
            return $f;
        }

        // One address line: the pin, its label, then the address. The label is
        // rendered rather than prefixed into the text so it can be styled apart
        // from the address it introduces, and an address the cell cannot fit is
        // recoverable from the line's tooltip.
        function appendAddressLine($left, key, fallback, value) {
            if (!value) return;
            var label = getMsg(key, fallback);
            var $addr = $('<div class="vas_106-custAddr"></div>').attr("title", label + ": " + value);
            $addr.append(svgIcon("pin"));
            $addr.append($('<span class="vas_106-addrLabel"></span>').text(label));
            $addr.append($('<span class="vas_106-addrVal"></span>').text(value));
            $left.append($addr);
        }

        function appendContactBit($container, icon, value) {
            if (!value) return;
            var $bit = $('<span class="vas_106-contactBit"></span>');
            $bit.append(svgIcon(icon));
            $bit.append($('<span></span>').text(value));
            $container.append($bit);
        }

        // ----------------------------------------------------------------- //
        //  Created From (chip strip)                                         //
        // ----------------------------------------------------------------- //

        function renderCreatedFrom() {
            var $strip = $('<section class="vas_106-genfrom"></section>');
            $strip.append($('<span class="vas_106-gfLabel"></span>').text(getMsg("VAS_106_CreatedFrom", "Created From")));
            var $chips = $('<div class="vas_106-gfChips"></div>');

            var any = false;
            if (data.QuotationNo) {
                // A quotation is a C_Order, but NOT one the Sales Order window
                // shows: that window is filtered to orders, so the record opens on
                // the Sales Quotation screen instead. Named per CHIP rather than
                // per table, since the blanket chip below is a C_Order too and
                // opens somewhere else again.
                $chips.append(originChip("doc", getMsg("VAS_106_Quotation", "Quotation"), data.QuotationNo,
                    pill(getMsg("VAS_106_Origin", "Origin"), "info"), "info", "C_Order", data.QuotationId,
                    "VAS_SalesQuotation"));
                any = true;
            }
            // The blanket sales order this one was released against. An order
            // created from a blanket carried no origin at all and the strip called
            // it "Manual".
            if (data.BlanketOrderId > 0) {
                $chips.append(originChip("calendar", getMsg("VAS_106_BlanketOrder", "Blanket Sales Order"),
                    data.BlanketOrderNo || ("#" + data.BlanketOrderId), null, "success",
                    "C_Order", data.BlanketOrderId, "VAS_BlanketSalesOrder"));
                any = true;
            }
            if (data.OpportunityId) {
                $chips.append(originChip("target", getMsg("VAS_106_Opportunity", "Opportunity"),
                    data.OpportunityName || ("#" + data.OpportunityId), null, "success",
                    "VAS_Opportunity", data.OpportunityId));
                any = true;
            }
            if (data.ProjectId) {
                $chips.append(originChip("folder", getMsg("VAS_106_Project", "Project"),
                    data.ProjectName || ("#" + data.ProjectId), null, "muted",
                    "C_Project", data.ProjectId));
                any = true;
            }
            if (!any) {
                $chips.append(originChip("pencil", getMsg("VAS_106_Manual", "Manual"), null, null, "info", null, 0));
            }

            $strip.append($chips);
            $body.append($strip);
        }

        // windowName overrides WINDOW_NAME_BY_TABLE for THIS chip. Several origins
        // are C_Order records that live on different screens — a quotation, a
        // blanket, an order — so the table alone cannot say which window to open.
        function originChip(icon, label, value, $statusPill, iconTone, tableName, recordId, windowName) {
            var $chip = $('<span class="vas_106-chip"></span>').addClass("vas_106-ic-" + (iconTone || "muted"));
            if (tableName && recordId) {
                $chip.addClass("vas_106-is-link").attr("data-open-table", tableName).attr("data-open-id", recordId);
                if (windowName) $chip.attr("data-open-window", windowName);
            }
            $chip.append(svgIcon(icon));
            $chip.append($('<span class="vas_106-chipLabel"></span>').text(label));
            if (value) $chip.append($('<span class="vas_106-chipVal"></span>').text(value));
            if ($statusPill) $chip.append($statusPill);
            return $chip;
        }

        // ----------------------------------------------------------------- //
        //  Workflow action bar                                               //
        // ----------------------------------------------------------------- //

        // The action bar is gone: the Complete Sales Order / Create Delivery /
        // Create Invoice buttons, the state note that headed them, and
        // hasUndelivered() which only decided whether one of them was enabled.
        //
        // All three actions belong to the sales order window, where each runs
        // behind the document's own validation — and two of them never did
        // anything here but raise a "will be available shortly" toast. The state
        // the note carried is already on screen twice: in the header's status pill
        // and in the Order Progress stepper.
        //
        // The controller's CompleteSalesOrder endpoint is left in place but is no
        // longer reached from this panel.

        // ----------------------------------------------------------------- //
        //  KPI strip                                                         //
        // ----------------------------------------------------------------- //

        function renderKpis() {
            var $snap = $('<section class="vas_106-snap"></section>');

            // Order Total.
            var totSub = (data.ISO_Code || "") + (data.ISO_Code ? " · " : "") + getMsg("VAS_106_InclTax", "incl. tax");
            $snap.append(metricCard("total", "coins", getMsg("VAS_106_OrderTotal", "Order Total"),
                formatAmount(+data.GrandTotal || 0, data.CurSymbol, data.ISO_Code, data.StdPrecision),
                totSub, null));

            // Fulfilment.
            var f = fulfilment();
            $snap.append(metricCard("fulfil", "box", getMsg("VAS_106_Fulfilment", "Fulfilment"),
                f.pct + "%",
                f.full + " " + getMsg("VAS_106_Of", "of") + " " + f.total + " " + getMsg("VAS_106_StockLines", "stock lines delivered"),
                f.pct));

            // Invoiced. The payment state is appended only when there IS one — an
            // order with no invoice yet has no paid/unpaid position, and the
            // separator alone would trail the caption.
            var iv = invoiced();
            var ivSub = iv.pct + "% " + getMsg("VAS_106_Billed", "billed");
            if (iv.state) ivSub += " · " + iv.state;
            $snap.append(metricCard("invoiced", "fileText", getMsg("VAS_106_Invoiced", "Invoiced"),
                formatAmount(iv.amount, data.CurSymbol, data.ISO_Code, data.StdPrecision),
                ivSub, iv.pct));

            // Delivery Readiness.
            var rd = readiness();
            $snap.append(metricCard("readiness", "cube", getMsg("VAS_106_DeliveryReadiness", "Delivery Readiness"),
                rd.pct + "%",
                rd.available + " " + getMsg("VAS_106_Of", "of") + " " + rd.pending + " " + getMsg("VAS_106_PendingUnits", "pending units in stock"),
                rd.pct));

            $body.append($snap);
        }

        function metricCard(tone, icon, label, value, sub, pct) {
            var $c = $('<div class="vas_106-metric"></div>').addClass("vas_106-tone-" + tone);
            var $head = $('<div class="vas_106-mHead"></div>');
            $head.append(svgIcon(icon));
            $head.append($('<span class="vas_106-mLabel"></span>').text(label));
            $c.append($head);
            $c.append($('<div class="vas_106-mVal"></div>').text(value));
            if (sub) $c.append($('<div class="vas_106-mSub"></div>').text(sub));
            if (pct != null) {
                var $bar = $('<div class="vas_106-mBar"><i></i></div>');
                $bar.find("i").css("width", Math.max(0, Math.min(100, pct)) + "%");
                $c.append($bar);
            }
            return $c;
        }

        // ----------------------------------------------------------------- //
        //  Order Progress stepper                                            //
        // ----------------------------------------------------------------- //

        // The latest delivery raised against this order — the moment the goods
        // actually went out, for the Delivered stage. Taken as the maximum
        // movement date rather than the first row, so the stage does not depend on
        // the order the server happened to return them in.
        function lastDeliveryDate() {
            var dv = data.Deliveries || [], best = null;
            for (var i = 0; i < dv.length; i++) {
                var d = parseDbDate(dv[i].MovementDate, false);
                if (d && (!best || d > best)) best = d;
            }
            return best;
        }

        function progressStages() {
            var f = fulfilment(), iv = invoiced(), inv = data.Invoices || [], dv = data.Deliveries || [];
            var completed = isCompleted();
            var shipped = dv.length > 0;
            var delivered = f.total > 0 ? (f.full >= f.total) : shipped;
            var invd = iv.amount > 0;
            var paid = inv.length > 0 && iv.state === getMsg("VAS_106_Paid", "paid");
            var lastDelivery = lastDeliveryDate();
            return [
                { key: "VAS_106_Drafted",   label: "Drafted",   done: true,      date: data.Created || data.DateOrdered },
                // "Completed", not "Confirmed" — this stage is the document
                // reaching Completed, so it is named for that and dated by when it
                // actually happened (the workflow's DocComplete stamp, model side)
                // rather than by DateOrdered, which is a document field a user can
                // back-date.
                { key: "VAS_106_Completed", label: "Completed", done: completed,
                  date: completed ? (data.CompletedDate || data.DateOrdered) : null },
                { key: "VAS_106_Shipped",   label: "Shipped",   done: shipped,   date: lastDelivery },
                // Dated by the LATEST delivery raised against the order — the
                // delivery that carried it to this state.
                { key: "VAS_106_Delivered", label: "Delivered", done: delivered, date: lastDelivery,
                  meta: (f.total ? (f.full + "/" + f.total) : null) },
                { key: "VAS_106_Invoiced",  label: "Invoiced",  done: invd,      date: null,
                  meta: invd ? (iv.pct + "%") : null },
                { key: "VAS_106_Paid",      label: "Paid",      done: paid,      date: null }
            ];
        }

        function renderStepper() {
            var stages = progressStages();
            // Active = first not-done stage (else the last).
            var active = stages.length;
            for (var a = 0; a < stages.length; a++) { if (!stages[a].done) { active = a; break; } }

            var posted = data.Posted === "Y";
            var $sec = $('<section class="vas_106-sec"></section>');
            var $head = $('<div class="vas_106-secHead"></div>');
            var $title = $('<h2 class="vas_106-secTitle"></h2>').text(getMsg("VAS_106_OrderProgress", "Order Progress"));
            var $posted = postBadge(posted);
            if ($posted) $title.append($posted);
            $head.append($title);
            $head.append($('<div class="vas_106-secRight"></div>').append(
                $('<span class="vas_106-secSummary"></span>').text(
                    getMsg("VAS_106_Stage", "Stage") + " " + Math.min(active + 1, stages.length) + " " +
                    getMsg("VAS_106_Of", "of") + " " + stages.length + " · " + statusMeta().label)));
            $sec.append($head);

            var $tl = $('<div class="vas_106-stepper"></div>');
            for (var i = 0; i < stages.length; i++) {
                var s = stages[i];
                var stateCls, metaText;
                if (i === active && !s.done) { stateCls = "vas_106-is-active"; metaText = getMsg("VAS_106_InProgress", "In progress"); }
                else if (s.done) { stateCls = "vas_106-is-done"; metaText = formatDate(s.date) || s.meta || getMsg("VAS_106_Done", "Done"); }
                else { stateCls = "is-pending"; metaText = s.meta || getMsg("VAS_106_Pending", "Pending"); }
                $tl.append(stepEntry(i + 1, getMsg(s.key, s.label), metaText, s.done, stateCls));
            }
            $sec.append($tl);
            $body.append($sec);
        }

        // The Posted badge, drawn only once the order IS posted. It used to render
        // for every record, reading "Not Posted" on every drafted order — which is
        // not news about a document that cannot be posted yet. It is a milestone
        // badge now: absent until the milestone is reached. Returns null so the
        // caller appends nothing at all.
        function postBadge(posted) {
            if (!posted) return null;
            return $('<span class="vas_106-postBadge vas_106-posted"></span>')
                .text(getMsg("VAS_106_Posted", "Posted"));
        }

        function stepEntry(num, title, meta, done, stateCls) {
            var $entry = $('<div class="vas_106-step"></div>').addClass(stateCls || "");
            var $rail = $('<div class="vas_106-stepRail"></div>');
            $rail.append($('<span class="vas_106-stepLine vas_106-stepLine-l"></span>'));
            var $dot = $('<span class="vas_106-stepDot"></span>');
            if (done) $dot.append(svgIcon("check")); else $dot.text(num);
            $rail.append($dot);
            $rail.append($('<span class="vas_106-stepLine vas_106-stepLine-r"></span>'));
            $entry.append($rail);
            var $lbl = $('<div class="vas_106-stepLabel"></div>');
            $lbl.append($('<div class="vas_106-stepTitle"></div>').text(title));
            if (meta) $lbl.append($('<div class="vas_106-stepMeta"></div>').text(meta));
            $entry.append($lbl);
            return $entry;
        }

        // ----------------------------------------------------------------- //
        //  Section shell                                                     //
        // ----------------------------------------------------------------- //

        // A headed section: icon, title, optional count badge, then its body.
        //
        // It no longer collapses. The chevron and the click that drove it are
        // gone, and the header is a plain div rather than a button — every section
        // is simply open, always. Collapsing was a control the reader had to
        // operate to see what they came for, and a section that has nothing to
        // show is now not drawn at all, so there is nothing left worth folding
        // away. The `id` still lands on the section as data-sec, which is what
        // identifies it in the DOM.
        function collapsible(id, iconName, title, badgeText) {
            var $sec = $('<section class="vas_106-dsec"></section>').attr("data-sec", id);
            var $head = $('<div class="vas_106-dsecHead"></div>');
            $head.append($('<span class="vas_106-tIc"></span>').append(svgIcon(iconName)));
            $head.append($('<span class="vas_106-dsecTitle"></span>').text(title));
            if (badgeText != null && badgeText !== "")
                $head.append($('<span class="vas_106-badge"></span>').text(badgeText));
            var $bodyWrap = $('<div class="vas_106-dsecBody"></div>');
            $sec.append($head).append($bodyWrap);
            $body.append($sec);
            return $bodyWrap;
        }

        // emptyRow() is unused: every section that had a "nothing here" row now
        // simply is not drawn. Kept because it is the shell's own placeholder
        // helper, and a section that ever needs to render an explanation rather
        // than disappear will want it.
        function emptyRow(text) {
            return $('<div class="vas_106-secEmpty"></div>').text(text);
        }

        // ----------------------------------------------------------------- //
        //  Order Lines                                                       //
        // ----------------------------------------------------------------- //

        // True when the order has at least one SERVICE line — the only kind a
        // contract can be raised against. A charge is not a product and cannot
        // carry one, and neither can a stocked item, so an order made up of those
        // drops the Contract column entirely rather than showing a column of
        // blanks. The table's grid follows (vas_106-no-contract, six tracks).
        function hasContractLines() {
            var lines = data.Lines || [];
            for (var i = 0; i < lines.length; i++) {
                if (lines[i].LineType === "service") return true;
            }
            return false;
        }

        function renderOrderLines() {
            var lines = data.Lines || [];
            if (!lines.length) return;
            var $wrap = collapsible("lines", "list", getMsg("VAS_106_OrderLines", "Order Lines"), lines.length);

            var showContract = hasContractLines();
            var $tbl = $('<div class="vas_106-table vas_106-linesTable"></div>');
            if (!showContract) $tbl.addClass("vas_106-no-contract");
            var $h = $('<div class="vas_106-tRow vas_106-tHead"></div>');
            $h.append($('<span></span>').text(getMsg("VAS_106_ProductService", "Product / Service")));
            $h.append($('<span class="vas_106-ta-c"></span>').text(getMsg("VAS_106_Qty", "Qty")));
            $h.append($('<span class="vas_106-ta-r"></span>').text(getMsg("VAS_106_UnitPrice", "Unit price")));
            $h.append($('<span class="vas_106-ta-c"></span>').text(getMsg("VAS_106_Disc", "Disc")));
            $h.append($('<span class="vas_106-ta-r"></span>').text(getMsg("VAS_106_LineTotal", "Line total")));
            $h.append($('<span class="vas_106-ta-r"></span>').text(getMsg("VAS_106_Delivered", "Delivered")));
            if (showContract) {
                $h.append($('<span class="vas_106-ta-r"></span>').text(getMsg("VAS_106_Contract", "Contract")));
            }
            $tbl.append($h);

            // Totals footer — always the whole order, never just the page.
            var $foot = $('<div class="vas_106-tFoot"></div>');
            $foot.append(totalBit(getMsg("VAS_106_Subtotal", "Subtotal"),
                formatAmount(+data.TotalLines || 0, data.CurSymbol, data.ISO_Code, data.StdPrecision), false));
            $foot.append(totalBit(getMsg("VAS_106_Tax", "Tax"),
                formatAmount(+data.TaxAmt || 0, data.CurSymbol, data.ISO_Code, data.StdPrecision), false));
            $foot.append(totalBit(getMsg("VAS_106_GrandTotal", "Grand Total"),
                formatAmount(+data.GrandTotal || 0, data.CurSymbol, data.ISO_Code, data.StdPrecision), true));
            $tbl.append($foot);

            $wrap.append($tbl);

            // The pager sits OUTSIDE the table: the table takes its own horizontal
            // scroll on a narrow panel, and the controls must not scroll away with
            // the columns.
            var $pager = $('<div class="vas_106-pager"></div>');
            if (lines.length > ROWS_PER_PAGE) $wrap.append($pager);

            // Rows are replaced in place, ahead of the totals footer, so the
            // table's structure and its CSS grid stay exactly as they were.
            function paintPage() {
                var pageCount = Math.max(1, Math.ceil(lines.length / ROWS_PER_PAGE));
                if (linesPage >= pageCount) linesPage = pageCount - 1;
                if (linesPage < 0) linesPage = 0;

                var start = linesPage * ROWS_PER_PAGE;
                var end = Math.min(lines.length, start + ROWS_PER_PAGE);

                // A line's change history is drawn directly beneath that line, in a
                // drawer the row's own History button opens — not in a table
                // further down the panel where the reader would have to match line
                // numbers up by hand. Same treatment as the PO overview.
                var histByLine = historyByLine();
                $tbl.find(".vas_106-tBody, .vas_106-lineHist").remove();
                for (var i = start; i < end; i++) {
                    var hist = histByLine[lines[i].C_OrderLine_ID] || [];
                    $foot.before(buildLineRow(lines[i], showContract, hist));
                    if (hist.length) $foot.before(buildLineHistory(lines[i], hist));
                }

                buildPager($pager, linesPage, pageCount, lines.length, start, end,
                    function (p) { linesPage = p; paintPage(); });
            }

            paintPage();

            // The contract hint is gone with the flow it explained: it told the
            // reader to enable a toggle and fill a form that the panel no longer
            // offers.
        }

        // Prior versions of the order lines, grouped by the line they belong to.
        // The server returns them ordered by line then newest change first, so the
        // grouping preserves that order within each line.
        function historyByLine() {
            var byLine = {}, rows = (data && data.History) || [];
            for (var i = 0; i < rows.length; i++) {
                var id = rows[i].C_OrderLine_ID;
                if (!id) continue;
                if (!byLine[id]) byLine[id] = [];
                byLine[id].push(rows[i]);
            }
            return byLine;
        }

        // The per-line affordance: an icon button at the end of the item cell that
        // opens the drawer sitting immediately beneath the row. Icon-only keeps the
        // cell narrow, so the tooltip carries the meaning and the change count.
        function buildHistToggle(ln, hist) {
            var open = !!lineHistOpen[ln.C_OrderLine_ID];

            var $b = $('<button type="button" class="vas_106-histBtn"></button>')
                .attr("aria-expanded", open ? "true" : "false")
                .attr("title", histToggleLabel(open, hist.length))
                .attr("aria-label", histToggleLabel(open, hist.length));
            $b.append(svgIcon("history"));
            if (open) $b.addClass("vas_106-is-open");

            $b.on("click", function (e) {
                e.preventDefault();
                e.stopPropagation();
                var nowOpen = !lineHistOpen[ln.C_OrderLine_ID];
                lineHistOpen[ln.C_OrderLine_ID] = nowOpen;
                $b.toggleClass("vas_106-is-open", nowOpen)
                  .attr("aria-expanded", nowOpen ? "true" : "false")
                  .attr("title", histToggleLabel(nowOpen, hist.length))
                  .attr("aria-label", histToggleLabel(nowOpen, hist.length));
                // The drawer is this row's next sibling — no id plumbing needed.
                $b.closest(".vas_106-tBody").next(".vas_106-lineHist").toggle(nowOpen);
            });

            return $b;
        }

        function histToggleLabel(open, count) {
            return open ? getMsg("VAS_106_HideHistory", "Hide history")
                        : getMsg("VAS_106_ShowHistory", "Show history") + " (" + count + ")";
        }

        // The drawer itself: one row per prior version, on the SAME columns as the
        // line above it and in the same order — the first cell carries the change
        // timestamp in place of the item (the item is the line it hangs under),
        // then Qty, Unit price, Disc and Line total exactly as the line renders
        // them, and finally who made the change in the Delivered column's track.
        function buildLineHistory(ln, rows) {
            var $wrap = $('<div class="vas_106-lineHist"></div>');
            if (!lineHistOpen[ln.C_OrderLine_ID]) $wrap.hide();

            var $tbl = $('<div class="vas_106-table vas_106-lhTable"></div>');

            var $h = $('<div class="vas_106-tRow vas_106-tHead"></div>');
            $h.append($('<span></span>').text(getMsg("VAS_106_ChangedOn", "Changed on")));
            $h.append($('<span class="vas_106-ta-c"></span>').text(getMsg("VAS_106_Qty", "Qty")));
            $h.append($('<span class="vas_106-ta-r"></span>').text(getMsg("VAS_106_UnitPrice", "Unit price")));
            $h.append($('<span class="vas_106-ta-c"></span>').text(getMsg("VAS_106_Disc", "Disc")));
            $h.append($('<span class="vas_106-ta-r"></span>').text(getMsg("VAS_106_LineTotal", "Line total")));
            $h.append($('<span class="vas_106-ta-r"></span>').text(getMsg("VAS_106_UpdatedBy", "Updated by")));
            $tbl.append($h);

            for (var i = 0; i < rows.length; i++) $tbl.append(buildLineHistoryRow(rows[i]));

            $wrap.append($tbl);
            return $wrap;
        }

        function buildLineHistoryRow(h) {
            var $r = $('<div class="vas_106-tRow vas_106-tBody"></div>');

            // Local system time (formatDateTime converts the UTC-stored value),
            // plus the note that version carried when it had one.
            var $when = $('<span class="vas_106-lhWhen"></span>');
            $when.append(document.createTextNode(formatDateTime(h.ChangedOn)));
            var note = (h.Description || "").trim();
            if (note) $when.append($('<small></small>').text(note).attr("title", note));
            $r.append($when);

            var p = +h.UOMPrecision || 0;
            var qty = formatNumber(+h.QtyEntered || 0, p);
            if (h.UOMName) qty += " " + h.UOMName;
            $r.append($('<span class="vas_106-ta-c"></span>').text(qty));

            $r.append($('<span class="vas_106-ta-r"></span>').text(formatAmount(
                +h.PriceEntered || 0, data.CurSymbol, data.ISO_Code, data.StdPrecision)));
            $r.append($('<span class="vas_106-ta-c"></span>').text(
                (+h.Discount ? formatNumber(+h.Discount, 0) + "%" : "")));
            $r.append($('<span class="vas_106-ta-r"></span>').text(formatAmount(
                +h.LineNetAmt || 0, data.CurSymbol, data.ISO_Code, data.StdPrecision)));

            // A snapshot written by a background / platform process can carry no
            // resolvable user; the cell is then left blank.
            var by = h.UpdatedByName || "";
            $r.append($('<span class="vas_106-lhBy vas_106-ta-r"></span>').text(by).attr("title", by));

            return $r;
        }

        function buildLineRow(ln, showContract, hist) {
            var $tr = $('<div class="vas_106-tRow vas_106-tBody"></div>').attr("data-line", ln.C_OrderLine_ID);

            // Product / service identity: name, the attribute set instance it was
            // sold against, then the line family and its unit.
            var $item = $('<span class="vas_106-itItem"></span>');
            // The name cell ellipsises, so the full product name goes on a hover
            // tooltip — the reader gets all of it without the column widening and
            // pushing the row's layout around.
            var pname = ln.ProductName || "";
            var $name = $('<div class="vas_106-itName"></div>').text(pname);
            if (pname) $name.attr("title", pname);
            // The history button rides at the end of the name line, so the drawer
            // it opens is reachable from the line it belongs to without spending a
            // column of the table on an affordance most lines do not carry.
            if (hist && hist.length) $name.append(buildHistToggle(ln, hist));
            $item.append($name);

            // Lot / serial / attributes sit directly under the product name — the
            // attribute qualifies WHICH stock was sold, so it belongs with the name
            // rather than below the line's unit.
            var asi = (ln.AttributeSetInstance || "").trim();
            if (asi && asi !== "--" && asi !== "-") {
                $item.append($('<div class="vas_106-itAttr"></div>').text(asi).attr("title", asi));
            }

            // The sub-line carries the line family and its UNIT OF MEASURE. It used
            // to carry the product's search key ("SKU ..."), which repeats what the
            // name already identifies; the unit is what the quantity beside it is
            // counted in and was nowhere on the row.
            var sub;
            if (ln.LineType === "product") sub = getMsg("VAS_106_ProductTag", "PRODUCT");
            else if (ln.LineType === "service") sub = getMsg("VAS_106_ServiceTag", "SERVICE");
            else if (ln.LineType === "charge") sub = getMsg("VAS_106_ChargeTag", "CHARGE");
            else sub = "";
            var uomLabel = (ln.UOMName || ln.UOMSymbol || "").trim();
            if (uomLabel) sub += (sub ? " · " : "") + uomLabel;
            else if (ln.LineType === "charge" && ln.ChargeName) sub += " · " + ln.ChargeName;
            if (sub) $item.append($('<div class="vas_106-itSku"></div>').text(sub).attr("title", sub));
            $tr.append($item);

            var uomP = +ln.UOMPrecision || 0;
            $tr.append($('<span class="vas_106-ta-c"></span>').text(formatNumber(+ln.QtyOrdered || 0, uomP)));
            $tr.append($('<span class="vas_106-ta-r"></span>').text(formatAmount(+ln.PriceActual || 0, data.CurSymbol, data.ISO_Code, data.StdPrecision)));
            $tr.append($('<span class="vas_106-ta-c"></span>').text((+ln.Discount ? formatNumber(+ln.Discount, 0) + "%" : "")));
            $tr.append($('<span class="vas_106-ta-r"></span>').text(formatAmount(+ln.LineNetAmt || 0, data.CurSymbol, data.ISO_Code, data.StdPrecision)));

            // Delivered — stockable lines only; a service / charge line is not
            // delivered at all, so its cell is left EMPTY rather than dashed. A
            // dash reads as a value that could not be worked out; nothing reads as
            // "this does not apply here", which is the truth.
            if (ln.LineType === "product") {
                var ordered = +ln.QtyOrdered || 0, delivered = +ln.QtyDelivered || 0;
                var pct = ordered > 0 ? Math.round(delivered / ordered * 100) : 0;
                var $prog = $('<span class="vas_106-prog vas_106-ta-r"></span>').addClass("vas_106-" + (ln.DeliveredState || "none"));
                var $bar = $('<span class="vas_106-progBar"><i></i></span>');
                $bar.find("i").css("width", Math.max(0, Math.min(100, pct)) + "%");
                $prog.append($bar);
                $prog.append(document.createTextNode(formatNumber(delivered, uomP) + "/" + formatNumber(ordered, uomP)));
                $tr.append($prog);
            } else {
                $tr.append($('<span class="vas_106-ta-r"></span>'));
            }

            // Contract — only when the order HAS a service line to carry one.
            if (showContract) $tr.append(buildContractCell(ln));
            return $tr;
        }

        // Contract cell — READ ONLY, and only ever on a SERVICE line.
        //
        // The panel shows whether the line carries a contract; it no longer offers
        // to create one. The toggle is a state indicator, not a control: it does
        // not respond to a click, and the inline contract form it used to open
        // (billing frequency, cycles, quantity, dates and a Create Contract
        // button) is gone with it. Raising a contract belongs to the order's own
        // screen, where the document's validation applies.
        //
        // The cell carries the switch ALONE — no "Contract" caption beside it and
        // no document-number chip. The column header already says what it is, and
        // a contract's number on a row whose control cannot be operated was text
        // for its own sake. Which contract it is stays on the switch's tooltip.
        function buildContractCell(ln) {
            var $cell = $('<span class="vas_106-ta-r"></span>');
            // A charge cannot carry a contract, and neither can a stocked item.
            if (ln.LineType !== "service") return $cell;

            var $lc = $('<span class="vas_106-lineContract"></span>');
            var contracted = ln.C_Contract_ID > 0;
            var $sw = $('<span class="vas_106-switch vas_106-locked" role="switch"></span>')
                .attr("aria-checked", (contracted || ln.IsContractFlag) ? "true" : "false")
                .attr("aria-readonly", "true");
            if (contracted || ln.IsContractFlag) $sw.addClass("vas_106-on");
            $sw.attr("title", contracted
                ? getMsg("VAS_106_Contract", "Contract") + ": " + (ln.ContractNo || ("#" + ln.C_Contract_ID))
                : getMsg("VAS_106_NoContract", "No contract"));
            $lc.append($sw);
            $cell.append($lc);
            return $cell;
        }

        // buildContractForm() and its field helpers (fieldWrap / numField /
        // textField) are gone with the create flow they built. The panel's
        // Frequencies payload fed only that form and is no longer requested
        // (model side).


        function totalBit(label, value, isGrand) {
            var $bit = $('<span class="vas_106-tf"></span>');
            if (isGrand) $bit.addClass("vas_106-is-grand");
            $bit.append(document.createTextNode(label));
            $bit.append($('<b></b>').text(value));
            return $bit;
        }

        // ----------------------------------------------------------------- //
        //  Delivery Readiness                                                //
        // ----------------------------------------------------------------- //

        function renderDeliveryReadiness() {
            var rows = data.DeliveryReadiness || [];
            if (!rows.length) return;
            var rd = readiness();
            var badge = rd.shortCount > 0 ? (rd.shortCount + " " + getMsg("VAS_106_Short", "short")) : rows.length;
            var $wrap = collapsible("readiness", "cube", getMsg("VAS_106_DeliveryReadiness", "Delivery Readiness"), badge);

            var $tbl = $('<div class="vas_106-table vas_106-rdTable"></div>');
            var $h = $('<div class="vas_106-tRow vas_106-tHead"></div>');
            $h.append($('<span></span>').text(getMsg("VAS_106_Item", "Item")));
            $h.append($('<span></span>').text(getMsg("VAS_106_Warehouse", "Warehouse")));
            $h.append($('<span class="vas_106-ta-c"></span>').text(getMsg("VAS_106_PendingToDeliver", "Pending to Deliver")));
            $h.append($('<span class="vas_106-ta-c"></span>').text(getMsg("VAS_106_OnHand", "On Hand")));
            $h.append($('<span class="vas_106-ta-r"></span>').text(getMsg("VAS_106_Readiness", "Readiness")));
            $tbl.append($h);

            $wrap.append($tbl);

            // Pager outside the table, for the same reason as the lines table.
            var $pager = $('<div class="vas_106-pager"></div>');
            if (rows.length > ROWS_PER_PAGE) $wrap.append($pager);

            function paintPage() {
                var pageCount = Math.max(1, Math.ceil(rows.length / ROWS_PER_PAGE));
                if (readinessPage >= pageCount) readinessPage = pageCount - 1;
                if (readinessPage < 0) readinessPage = 0;

                var start = readinessPage * ROWS_PER_PAGE;
                var end = Math.min(rows.length, start + ROWS_PER_PAGE);

                $tbl.find(".vas_106-tBody").remove();
                for (var i = start; i < end; i++) $tbl.append(buildReadinessRow(rows[i]));

                buildPager($pager, readinessPage, pageCount, rows.length, start, end,
                    function (p) { readinessPage = p; paintPage(); });
            }

            paintPage();

            var $note = $('<div class="vas_106-note"></div>');
            $note.append(svgIcon("info"));
            $note.append($('<span></span>').text(getMsg("VAS_106_ReadinessNote",
                "On Hand = physical stock in the warehouse right now · Service lines excluded — readiness tracks stockable items against the next delivery")));
            $wrap.append($note);
        }

        function buildReadinessRow(rd) {
            var $tr = $('<div class="vas_106-tRow vas_106-tBody"></div>');
            // The UOM leads the product name on the SAME line — "EACH · Bolt M8" —
            // rather than sitting on a sub-line under it. The quantities in this
            // table are the whole point of the row, so the unit they are counted in
            // reads first, with the product it belongs to after it.
            //
            // Only the NAME ellipsises: the unit is short and holds its width, so a
            // long product name is what gives way. Both are on the line's tooltip.
            var $item = $('<span class="vas_106-itItem"></span>');
            var rname = rd.ProductName || "";
            var ruom = (rd.UOMName || "").trim();
            var $rn = $('<div class="vas_106-itName"></div>');
            if (ruom) $rn.append($('<span class="vas_106-uomTag"></span>').text(ruom));
            $rn.append($('<span class="vas_106-nameTxt"></span>').text(rname));
            $rn.attr("title", ruom ? ruom + " · " + rname : rname);
            $item.append($rn);

            // Lot / serial / attributes stay under the name — they qualify WHICH
            // stock the row is about.
            var rasi = (rd.AttributeSetInstance || "").trim();
            if (rasi && rasi !== "--" && rasi !== "-") {
                $item.append($('<div class="vas_106-itAttr"></div>').text(rasi).attr("title", rasi));
            }
            $tr.append($item);
            $tr.append($('<span></span>').text(rd.WarehouseName || ""));
            $tr.append($('<span class="vas_106-ta-c"></span>').text(formatNumber(+rd.PendingQty || 0, 0)));
            $tr.append($('<span class="vas_106-ta-c"></span>').text(formatNumber(+rd.QtyOnHand || 0, 0)));

            // Ready / Short only. "Awaited" is gone: it was the fallback state for
            // a line the server had not classified, and it read as though the goods
            // were on their way when in fact the warehouse simply does not hold
            // enough of them — which is exactly what SHORT means. Anything not
            // ready is now reported as short, by how much.
            var tag, tone, dot = true;
            var shortBy = (+rd.PendingQty || 0) - (+rd.QtyOnHand || 0);
            if (rd.Readiness === "ready") { tag = getMsg("VAS_106_FullyDelivered", "Fully delivered"); tone = "vas_106-ready"; }
            else if (rd.Readiness === "instock") { tag = getMsg("VAS_106_Ready", "Ready to ship"); tone = "vas_106-ready"; }
            else {
                tag = getMsg("VAS_106_ShortBy", "Short by") + " " +
                      formatNumber(shortBy > 0 ? shortBy : (+rd.PendingQty || 0), 0);
                tone = "vas_106-short";
            }

            var $rt = $('<span class="vas_106-ta-r"></span>');
            var $pill = $('<span class="vas_106-rdTag"></span>').addClass(tone);
            if (dot) $pill.append($('<span class="vas_106-rdDot"></span>'));
            $pill.append($('<span></span>').text(tag));
            $rt.append($pill);
            $tr.append($rt);
            return $tr;
        }

        // ----------------------------------------------------------------- //
        //  Documents                                                         //
        // ----------------------------------------------------------------- //

        // Every document raised FROM this order: the shipments that delivered it,
        // the invoices that billed it and the receipts that paid those invoices.
        //
        // One section, built like the Purchase Order overview's: the same four
        // columns (Document / Date / Status / Amount), a per-kind count in the
        // header badge, and a whole row that opens the record it names. It
        // replaces the separate Deliveries and Invoices sections, which split one
        // question — what has happened against this order — across two tables with
        // different columns, and left receipts out entirely.
        //
        // The Deliveries / Invoices COLLECTIONS are untouched: the KPI strip and
        // the progress stepper are derived from them.
        function renderDocuments() {
            var rows = (data && data.Documents) || [];
            if (!rows.length) return;

            var $wrap = collapsible("documents", "fileText",
                getMsg("VAS_106_Documents", "Documents"), documentsSummary(rows));

            var $tbl = $('<div class="vas_106-table vas_106-docTable"></div>');
            var $h = $('<div class="vas_106-tRow vas_106-tHead"></div>');
            $h.append($('<span></span>').text(getMsg("VAS_106_Document", "Document")));
            $h.append($('<span></span>').text(getMsg("VAS_106_DocDate", "Date")));
            $h.append($('<span></span>').text(getMsg("VAS_106_Status", "Status")));
            $h.append($('<span class="vas_106-ta-r"></span>').text(getMsg("VAS_106_Amount", "Amount")));
            $tbl.append($h);

            for (var i = 0; i < rows.length; i++) $tbl.append(buildDocumentRow(rows[i]));

            // No totals footer. The Invoiced / Yet-to-invoice pair that used to
            // close this table is gone: the KPI strip already reports the invoiced
            // amount and how much of the order it covers, so the footer restated it
            // under a second set of words.

            $wrap.append($tbl);
        }

        // "2 shipments · 1 invoices" — only the kinds actually present count.
        function documentsSummary(rows) {
            var dl = 0, inv = 0, rc = 0;
            for (var i = 0; i < rows.length; i++) {
                if (rows[i].Type === "delivery") dl++;
                else if (rows[i].Type === "invoice") inv++;
                else if (rows[i].Type === "receipt") rc++;
            }
            var bits = [];
            if (dl)  bits.push(dl + " " + getMsg("VAS_106_ShipmentsCount", "shipments"));
            if (inv) bits.push(inv + " " + getMsg("VAS_106_InvoicesCount", "invoices"));
            if (rc)  bits.push(rc + " " + getMsg("VAS_106_ReceiptsCount", "receipts"));
            return bits.join(" · ");
        }

        function buildDocumentRow(d) {
            var $tr = $('<div class="vas_106-tRow vas_106-tBody"></div>');

            var canOpen = d.TableName && +d.RecordId > 0;
            if (canOpen) {
                $tr.addClass("vas_106-is-link")
                    .attr("data-open-table", d.TableName)
                    .attr("data-open-id", d.RecordId);
            }

            // Identity: doc number + kind, with the open affordance on the right.
            var $item = $('<span class="vas_106-itItem vas_106-docItem"></span>');
            var icon = d.Type === "delivery" ? "truck" : (d.Type === "receipt" ? "coins" : "fileText");
            $item.append(svgIcon(icon));

            var $txt = $('<span class="vas_106-docTxt"></span>');
            $txt.append($('<div class="vas_106-itName"></div>').text(d.DocumentNo || ""));

            var sub;
            if (d.Type === "delivery") {
                sub = getMsg("VAS_106_Shipment", "Shipment");
                if (d.LineCount) sub += " · " + d.LineCount + " " + getMsg("VAS_106_Items", "Items");
                if (d.Extra) sub += " · " + d.Extra;
            } else if (d.Type === "receipt") {
                sub = getMsg("VAS_106_CustomerReceipt", "Customer Receipt");
                if (+d.DiscountAmt) {
                    sub += " · " + getMsg("VAS_106_DiscountedAmount", "Discount") + ": " +
                        formatAmount(+d.DiscountAmt || 0, data.CurSymbol, data.ISO_Code, data.StdPrecision);
                }
            } else {
                sub = getMsg("VAS_106_CustomerInvoice", "Customer Invoice");
                if (d.IsPaid) sub += " · " + getMsg("VAS_106_PaidTag", "Paid");
            }
            $txt.append($('<div class="vas_106-itSku"></div>').text(sub).attr("title", sub));

            $item.append($txt);
            if (canOpen) $item.append(svgIcon("arrowUpRight"));
            $tr.append($item);

            $tr.append($('<span></span>').text(formatDate(d.DocDate) || ""));
            $tr.append($('<span></span>').append(docStatusPill(d.DocStatus)));

            // A shipment carries no amount of its own — the model sends null for
            // it, which is not the same as a zero.
            var $amt = $('<span class="vas_106-ta-r"></span>');
            $amt.text((d.Amount === null || d.Amount === undefined)
                ? ""
                : formatAmount(+d.Amount || 0, data.CurSymbol, data.ISO_Code, data.StdPrecision));
            $tr.append($amt);

            return $tr;
        }


        // ----------------------------------------------------------------- //
        //  Activity                                                          //
        // ----------------------------------------------------------------- //

        // A note takes the "note" icon: it had "mail", which now belongs to the
        // e-mail rows, and two badges carrying the same envelope in one feed said
        // the opposite of what the badges are for.
        var ACT_TYPES = {
            Note:     { tone: "info",    icon: "note",  label: "Note" },
            Email:    { tone: "purple",  icon: "mail",  label: "Email" },
            Delivery: { tone: "success", icon: "truck", label: "Delivery" },
            Invoice:  { tone: "warning", icon: "fileText", label: "Invoice" },
            Created:  { tone: "neutral", icon: "plus",  label: "Created" },
            Updated:  { tone: "purple",  icon: "pencil", label: "Updated" }
        };

        function renderActivity() {
            var rows = data.Activity || [];
            if (!rows.length) return;
            var $wrap = collapsible("activity", "clock", getMsg("VAS_106_Activity", "Activity"), rows.length);

            var $card = $('<div class="vas_106-panelcard"></div>');
            $wrap.append($card);

            // The pager is a sibling of the card, so it keeps its place while the
            // card's rows are replaced underneath it.
            var $pager = $('<div class="vas_106-pager"></div>');
            if (rows.length > ACTIVITY_PER_PAGE) $wrap.append($pager);

            function paintPage() {
                var pageCount = Math.max(1, Math.ceil(rows.length / ACTIVITY_PER_PAGE));
                if (activityPage >= pageCount) activityPage = pageCount - 1;
                if (activityPage < 0) activityPage = 0;

                var start = activityPage * ACTIVITY_PER_PAGE;
                var end = Math.min(rows.length, start + ACTIVITY_PER_PAGE);

                $card.empty();
                for (var i = start; i < end; i++) {
                    $card.append(activityRow(rows[i]));
                    // An e-mail's message is far too heavy to sit in a feed: it
                    // stays collapsed under its own row and opens only when the
                    // reader asks for it.
                    var $mail = activityBody(rows[i]);
                    if ($mail) $card.append($mail);
                }

                buildPager($pager, activityPage, pageCount, rows.length, start, end,
                    function (p) { activityPage = p; paintPage(); });
            }

            paintPage();
        }

        // Range caption on the left, Previous / page-of / Next on the right.
        // Rebuilt on every page change so the disabled states stay accurate.
        // `page` is 0-based and `onGo` is handed the page to move to, so the
        // caller owns its own page state. Nothing is drawn for a single-page
        // list, so a short feed shows no controls at all.
        function buildPager($pager, page, pageCount, total, start, end, onGo) {
            $pager.empty();
            if (pageCount <= 1) return;

            $pager.append($('<span class="vas_106-pgRange"></span>').text(
                getMsg("VAS_106_Showing", "Showing") + " " + (start + 1) + "-" + end + " " +
                getMsg("VAS_106_Of", "of") + " " + total));

            var $ctrls = $('<span class="vas_106-pgCtrls"></span>');
            $ctrls.append(pagerButton(getMsg("VAS_106_Previous", "Previous"), "chevLeft",
                page <= 0, function () { onGo(page - 1); }));
            $ctrls.append($('<span class="vas_106-pgPos"></span>').text(
                getMsg("VAS_106_Page", "Page") + " " + (page + 1) + " " +
                getMsg("VAS_106_Of", "of") + " " + pageCount));
            $ctrls.append(pagerButton(getMsg("VAS_106_Next", "Next"), "chevRight",
                page >= pageCount - 1, function () { onGo(page + 1); }));
            $pager.append($ctrls);
        }

        function pagerButton(label, icon, disabled, handler) {
            var $b = $('<span class="vas_106-pgBtn"></span>');
            if (icon === "chevLeft") $b.append(svgIcon(icon));
            $b.append($('<span></span>').text(label));
            if (icon === "chevRight") $b.append(svgIcon(icon));
            if (disabled) $b.addClass("vas_106-is-disabled");
            else $b.on("click", handler);
            return $b;
        }

        function activityRow(a) {
            var meta = ACT_TYPES[a.EventType] || ACT_TYPES.Updated;
            var $row = $('<div class="vas_106-actRow"></div>');
            var $badge = $('<span class="vas_106-actBadge"></span>').addClass("vas_106-tone-" + meta.tone);
            $badge.append(svgIcon(meta.icon));
            $badge.append($('<span></span>').text(getMsg("VAS_106_Act" + a.EventType, meta.label)));
            $row.append($badge);

            var $main = $('<div class="vas_106-actMain"></div>');
            var $wrapT = $('<div class="vas_106-atWrap"></div>');
            // For a NOTE the headline is the comment itself, so it wraps rather
            // than ellipsising after one line — that text is what the reader came
            // for. Every other headline is a short labelled event.
            var title = activityTitle(a, meta);
            var $title = $('<span class="vas_106-at"></span>').text(title).attr("title", title);
            if (a.EventType === "Note") $title.addClass("vas_106-multiline");
            $wrapT.append($title);

            // "when · by whom" — the audit trail's whole point, and the same three
            // parts in the same place on every row: the badge says what kind of
            // event it is, the headline what happened, this when and by whom. The
            // actor used to be appended bare, so a comment read as though its text
            // and a name were two halves of one caption.
            var when = formatDateTime(a.EventTime);
            if (a.ActorName) {
                when = when ? when + " · " + getMsg("VAS_106_By", "by") + " " + a.ActorName
                            : getMsg("VAS_106_By", "by") + " " + a.ActorName;
            }
            $wrapT.append($('<span class="vas_106-actTime"></span>').text(when).attr("title", when));
            $main.append($wrapT);

            // An e-mail names its recipients under the subject: the To list, plus a
            // count of the Cc / Bcc addresses so the reader can see at a glance
            // that others were copied. Every address itself is listed in the body.
            if (a.EventType === "Email") {
                var to = recipientSummary(a);
                if (to) {
                    $main.append($('<div class="vas_106-actSub"></div>')
                        .text(to).attr("title", allRecipients(a) || to));
                }
            }
            $row.append($main);

            // A row carrying a message opens on click; the caret shows the state.
            if (hasActivityBody(a)) {
                $row.addClass("vas_106-is-openable")
                    .attr("title", getMsg("VAS_106_ShowMailBody", "Show message"));
                $row.append($('<span class="vas_106-actCaret"></span>').append(svgIcon("chevRight")));
                $row.on("click", function () {
                    var $panel = $row.next(".vas_106-actBody");
                    if (!$panel.length) return;
                    var nowOpen = !$row.hasClass("vas_106-is-open");
                    $row.toggleClass("vas_106-is-open", nowOpen)
                        .attr("title", nowOpen ? getMsg("VAS_106_HideMailBody", "Hide message")
                                               : getMsg("VAS_106_ShowMailBody", "Show message"));
                    $panel.toggle(nowOpen);
                });
            }
            return $row;
        }

        // Only an e-mail carries a body, and only one that actually has text —
        // an empty message is nothing to open.
        function hasActivityBody(a) {
            return !!(a && a.EventType === "Email" && a.Body && String(a.Body).trim());
        }

        // The e-mail message, collapsed beneath its activity row. The full
        // recipient set (From / To / Cc / Bcc) heads it, so every address the mail
        // went to is on screen once the reader opens it. The body arrives as plain
        // text (the server flattens an HTML mail) and is set with .text(), so
        // nothing in a message can reach the DOM as markup.
        function activityBody(a) {
            if (!hasActivityBody(a)) return null;

            var $panel = $('<div class="vas_106-actBody" style="display:none;"></div>');
            appendMailMeta($panel, "VAS_106_MailFrom", "From", a.MailFrom);
            appendMailMeta($panel, "VAS_106_MailTo",   "To",   a.MailTo);
            appendMailMeta($panel, "VAS_106_MailCc",   "Cc",   a.MailCc);
            appendMailMeta($panel, "VAS_106_MailBcc",  "Bcc",  a.MailBcc);
            $panel.append($('<p></p>').text(String(a.Body).trim()));
            return $panel;
        }

        function appendMailMeta($panel, key, fallback, value) {
            if (!value || !String(value).trim()) return;
            $panel.append($('<div class="vas_106-actMeta"></div>')
                .text(getMsg(key, fallback) + " " + String(value).trim()));
        }

        // Row sub-line: the To list, plus "+n more" covering the Cc / Bcc
        // addresses. Counting by comma / semicolon is enough for a summary — the
        // body lists the addresses verbatim.
        function recipientSummary(a) {
            var to = (a.MailTo || "").trim();
            var extra = countAddresses(a.MailCc) + countAddresses(a.MailBcc);
            if (!to && !extra) return "";
            var s = getMsg("VAS_106_MailTo", "To") + " " + (to || "—");
            if (extra > 0) s += " +" + extra + " " + getMsg("VAS_106_MoreRecipients", "more");
            return s;
        }

        // Every address on the mail, for the row's hover tooltip.
        function allRecipients(a) {
            var bits = [];
            if (a.MailTo)  bits.push(getMsg("VAS_106_MailTo", "To") + " " + a.MailTo);
            if (a.MailCc)  bits.push(getMsg("VAS_106_MailCc", "Cc") + " " + a.MailCc);
            if (a.MailBcc) bits.push(getMsg("VAS_106_MailBcc", "Bcc") + " " + a.MailBcc);
            return bits.join("\n");
        }

        function countAddresses(value) {
            if (!value || !String(value).trim()) return 0;
            var parts = String(value).split(/[;,]/), n = 0;
            for (var i = 0; i < parts.length; i++) {
                if (parts[i].trim()) n++;
            }
            return n;
        }

        function activityTitle(a, meta) {
            if (a.EventType === "Note") return a.Title || getMsg("VAS_106_ActNote", "Note");
            // An e-mail's headline is its subject; a mail sent without one still
            // has to name itself.
            if (a.EventType === "Email")
                return a.Title || getMsg("VAS_106_ActNoSubject", "(No subject)");
            if (a.EventType === "Invoice")
                return getMsg("VAS_106_ActInvoiceTxt", "Invoice") + " " + (a.Title || "") +
                       (a.Amount ? " (" + formatAmount(+a.Amount, data.CurSymbol, data.ISO_Code, data.StdPrecision) + ")" : "");
            if (a.EventType === "Delivery")
                return getMsg("VAS_106_ActDeliveryTxt", "Delivery") + " " + (a.Title || "");
            if (a.EventType === "Created")
                return getMsg("VAS_106_ActCreatedTxt", "Order created") + (a.Title ? " " + a.Title : "");
            if (a.EventType === "Updated")
                return getMsg("VAS_106_ActConfirmedTxt", "Order confirmed") + (a.Title ? " " + a.Title : "");
            return a.Title || "";
        }

        // ----------------------------------------------------------------- //
        //  Notes                                                             //
        // ----------------------------------------------------------------- //

        function renderNotes() {
            var rows = data.Notes || [];
            if (!rows.length) return;
            var $wrap = collapsible("notes", "note", getMsg("VAS_106_Notes", "Notes"), rows.length);

            var $card = $('<div class="vas_106-panelcard vas_106-notesCard"></div>');
            var $notes = $('<div class="vas_106-notesBody"></div>');
            for (var i = 0; i < rows.length; i++) $notes.append($('<p></p>').text(rows[i].Text));
            $card.append($notes);
            $wrap.append($card);
        }

        // ----------------------------------------------------------------- //
        //  Shared pills                                                      //
        // ----------------------------------------------------------------- //

        function pill(label, tone) {
            return $('<span class="vas_106-pill"></span>').addClass("vas_106-tone-" + (tone || "neutral")).text(label);
        }

        function tagPill(label, tone) {
            var $t = $('<span class="vas_106-tag"></span>').addClass(tone);
            $t.append($('<span class="vas_106-dot"></span>'));
            $t.append($('<span></span>').text(label));
            return $t;
        }

        function docStatusPill(docStatus) {
            var map = {
                "CO": { tone: "vas_106-approved", label: getMsg("VAS_106_Completed", "Completed") },
                "CL": { tone: "vas_106-approved", label: getMsg("VAS_106_Closed", "Closed") },
                "DR": { tone: "vas_106-draft",    label: getMsg("VAS_106_Draft", "Draft") },
                "IP": { tone: "vas_106-partial",  label: getMsg("VAS_106_InProgressShort", "In Progress") },
                "IN": { tone: "vas_106-partial",  label: getMsg("VAS_106_InTransit", "Scheduled") }
            };
            var m = map[docStatus] || { tone: "vas_106-draft", label: docStatus || "" };
            return tagPill(m.label, m.tone);
        }

        // ----------------------------------------------------------------- //
        //  Events / actions                                                  //
        // ----------------------------------------------------------------- //

        function bindEvents() {
            // No section-collapse handler: every section is always open, and its
            // header is no longer a control.
            //
            // No contract handlers either — the toggle is a read-only state
            // indicator now, and the inline create form it drove is gone. The
            // action-bar handlers went earlier, with their buttons.

            // Open linked records.
            $root.on("click", ".vas_106-is-link[data-open-table], .vas_106-chip.vas_106-is-link", function (e) {
                e.preventDefault();
                openRecord($(this).attr("data-open-table"), $(this).attr("data-open-id"),
                    $(this).attr("data-open-window"));
            });
        }

        // validateContractForm(), handleCreateContract() and parseResult() are
        // gone with the inline contract form — parseResult read the reply of the
        // only POST this panel still made. The controller's CreateContract
        // endpoint is left in place but is no longer reached from here.
        //
        // handleCompleteSalesOrder() went earlier, with its button. The panel is
        // read-only now: it opens records, and nothing else.

        // Tables whose record does NOT open in the table's default zoom window,
        // mapped to the name of the window it does open.
        //
        // Every document this panel lists is the SALES side of a table that serves
        // both: C_Invoice is an AR invoice here, C_Payment an AR receipt, M_InOut a
        // shipment, C_Order the quotation this order came from. The browser's zoom
        // lookup resolved the purchase side for them, so clicking an invoice number
        // opened the AP Invoice screen. Naming the window settles it.
        //
        // Any further screen that needs naming belongs here; nothing else has to
        // change. Ported from VAS_092.
        var WINDOW_NAME_BY_TABLE = {
            "C_Invoice":  "VAS_ARInvoice",
            "C_Payment":  "VAS_ARReceipt",
            "M_InOut":    "VAS_DeliveryOrder",
            "C_Order":    "VAS_SalesOrder",
            "C_Project":  "VAS_Project"
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
                    "VAS_106_OverviewSalesOrder/GetWindow_ID", windowName);
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

        // Tables that serve BOTH sides of the trade, where the zoom target has to
        // be told which side to resolve. Everything else — a project, a contract —
        // has one screen, and asking for the sales side of it is meaningless.
        var DUAL_PURPOSE_TABLES = {
            "C_Order": true, "C_Invoice": true, "C_Payment": true, "M_InOut": true
        };

        // Table name -> AD_Window_ID, cached like windowIdByName above.
        var windowIdByTable = {};

        // Last resort: ask the SERVER which window the table opens in
        // (AD_Table.AD_Window_ID, else the first window with a tab on it).
        //
        // The Contract chip needs this. C_Contract and VAS_ContractMaster are
        // maintained by module windows whose names cannot be hard-coded here, and
        // the browser-side zoom lookup only knows tables the client has cached — so
        // the chip fell through to the "cannot open" toast. Any future chip gets
        // the same safety net. Ported from VAS_102.
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
                    "VAS_106_OverviewSalesOrder/GetWindowIdByTable", tableName);
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

        // Open the record's window filtered to that row, in four steps: the window
        // the CALLER names for this record, else the one named for its table, else
        // the table's default zoom target, else the window the DICTIONARY says the
        // table opens in. Either way the window is started with an equal-query on
        // the table's key column (TableName_ID). Degrades to a toast so a click
        // never throws.
        //
        // The caller's name wins because a table does not always settle the screen:
        // a quotation, a blanket and an order are all C_Order records living on
        // three different windows.
        function openRecord(tableName, recordId, windowName) {
            if (!tableName || !recordId || +recordId <= 0 || !window.VIS) return;
            try {
                var windowId = resolveWindowIdByName(windowName || WINDOW_NAME_BY_TABLE[tableName]);
                // A named window the dictionary does not know falls back to the
                // table's own mapping before the zoom target gets its turn.
                if (windowId <= 0 && windowName) {
                    windowId = resolveWindowIdByName(WINDOW_NAME_BY_TABLE[tableName]);
                }

                if (windowId <= 0 &&
                    VIS.ZoomTarget && typeof VIS.ZoomTarget.getZoomAD_Window_ID === "function") {
                    // 4th arg is IsSOTrx. It is only meaningful for a table that
                    // serves both sides — every one of those this panel opens is on
                    // the SALES side. Asking it of a single-screen table like
                    // C_Contract could resolve the wrong window, or none.
                    var isSOTrx = !!DUAL_PURPOSE_TABLES[tableName];
                    windowId = VIS.ZoomTarget.getZoomAD_Window_ID(tableName, 0, null, isSOTrx) || 0;
                }
                if (windowId <= 0) windowId = resolveWindowIdByTable(tableName);

                if (windowId > 0 && VIS.viewManager && typeof VIS.viewManager.startWindow === "function") {
                    var zoomQuery = VIS.Query.prototype.getEqualQuery(tableName + "_ID", +recordId);
                    VIS.viewManager.startWindow(windowId, zoomQuery);
                    return;
                }
            } catch (e) { console.log(e); }
            toast(getMsg("VAS_106_OpenRecord", "Open") + " " + tableName + " #" + recordId, false);
        }

        // Lightweight self-contained toast (no dependency on a host toast API).
        function toast(message, isError) {
            var $t = $('<div class="vas_106-toast"></div>').addClass(isError ? "vas_106-err" : "vas_106-ok").text(message);
            $root.append($t);
            setTimeout(function () { $t.addClass("vas_106-show"); }, 10);
            setTimeout(function () { $t.removeClass("vas_106-show"); setTimeout(function () { $t.remove(); }, 300); }, 3200);
        }

        // ----------------------------------------------------------------- //
        //  Icons                                                             //
        // ----------------------------------------------------------------- //

        var SVG_ICONS = {
            pin:      '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M20 10c0 6-8 12-8 12s-8-6-8-12a8 8 0 0 1 16 0Z"/><circle cx="12" cy="10" r="3"/></svg>',
            user:     '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"/><circle cx="12" cy="7" r="4"/></svg>',
            phone:    '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M22 16.92v3a2 2 0 0 1-2.18 2 19.79 19.79 0 0 1-8.63-3.07 19.5 19.5 0 0 1-6-6 19.79 19.79 0 0 1-3.07-8.67A2 2 0 0 1 4.11 2h3a2 2 0 0 1 2 1.72c.13.96.36 1.9.7 2.81a2 2 0 0 1-.45 2.11L8.09 9.91a16 16 0 0 0 6 6l1.27-1.27a2 2 0 0 1 2.11-.45c.91.34 1.85.57 2.81.7A2 2 0 0 1 22 16.92Z"/></svg>',
            mail:     '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="5" width="18" height="14" rx="2"/><path d="m3 7 9 6 9-6"/></svg>',
            coins:    '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><circle cx="8" cy="8" r="5"/><path d="M14.5 4.2a5 5 0 0 1 0 15.6"/><path d="M7 18.7a5 5 0 0 0 6 0"/></svg>',
            box:      '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M21 8 12 3 3 8v8l9 5 9-5Z"/><path d="m3 8 9 5 9-5"/><path d="M12 13v8"/></svg>',
            cube:     '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M21 16V8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16z"/><polyline points="3.27 6.96 12 12.01 20.73 6.96"/><line x1="12" y1="22.08" x2="12" y2="12"/></svg>',
            fileText: '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><path d="M14 2v6h6M12 18v-6M9 15h6"/></svg>',
            truck:    '<svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><rect x="1" y="3" width="15" height="13"/><path d="M16 8h4l3 3v5h-7V8z"/><circle cx="5.5" cy="18.5" r="2.5"/><circle cx="18.5" cy="18.5" r="2.5"/></svg>',
            list:     '<svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><line x1="8" y1="6" x2="21" y2="6"/><line x1="8" y1="12" x2="21" y2="12"/><line x1="8" y1="18" x2="21" y2="18"/><line x1="3" y1="6" x2="3.01" y2="6"/><line x1="3" y1="12" x2="3.01" y2="12"/><line x1="3" y1="18" x2="3.01" y2="18"/></svg>',
            clock:    '<svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="9"/><polyline points="12 7 12 12 15 14"/></svg>',
            note:     '<svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><path d="M14 2v6h6M9 13h6M9 17h4"/></svg>',
            doc:      '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8Z"/><path d="M14 2v6h6"/></svg>',
            target:   '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="9"/><path d="M9 12l2 2 4-4"/></svg>',
            folder:   '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M4 4h5l2 3h9a1 1 0 0 1 1 1v10a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2Z"/></svg>',
            pencil:   '<svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M12 20h9"/><path d="M16.5 3.5a2.12 2.12 0 0 1 3 3L7 19l-4 1 1-4Z"/></svg>',
            info:     '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"/><path d="M12 16v-4"/><path d="M12 8h.01"/></svg>',
            check:    '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"><path d="M20 6 9 17l-5-5"/></svg>',
            checkCircle: '<svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"/><polyline points="22 4 12 14.01 9 11.01"/></svg>',
            chevUp:   '<svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><path d="m18 15-6-6-6 6"/></svg>',
            history:  '<svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M3 12a9 9 0 1 0 3-6.7L3 8"/><path d="M3 3v5h5"/><path d="M12 7v5l3 2"/></svg>',
            chevLeft: '<svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><path d="m15 18-6-6 6-6"/></svg>',
            chevRight:'<svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><path d="m9 18 6-6-6-6"/></svg>',
            plus:     '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 5v14M5 12h14"/></svg>',
            arrowUpRight: '<svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M7 17 17 7M7 7h10v10"/></svg>'
        };

        function svgIcon(name) {
            var $wrap = $('<span class="vas_106-ic"></span>');
            $wrap[0].innerHTML = SVG_ICONS[name] || "";
            return $wrap;
        }

        // ----------------------------------------------------------------- //
        //  Formatting helpers                                                //
        // ----------------------------------------------------------------- //

        function formatNumber(value, precision) {
            var p = (precision >= 0) ? precision : 0;
            return (+value || 0).toLocaleString(window.navigator.language, {
                minimumFractionDigits: p, maximumFractionDigits: p
            });
        }

        function formatAmount(value, symbol, iso, precision) {
            var sign = value < 0 ? "-" : "";
            var abs = Math.abs(+value || 0);
            var cur = symbol || iso || "";
            var p = (precision >= 0) ? precision : 2;
            var formatted = abs.toLocaleString(window.navigator.language, {
                minimumFractionDigits: p, maximumFractionDigits: p
            });
            return sign + (cur ? cur + " " : "") + formatted;
        }

        // Parses a .NET/Newtonsoft DB value into a Date.
        //
        // asUtc = true  → for genuine TIMESTAMPS (activity moments, created /
        //   updated stamps). The DB stores these in UTC and the server emits no
        //   timezone designator (e.g. "2026-08-12T10:00:00"), which the browser
        //   reads as LOCAL — so the panel printed the stored UTC clock and every
        //   activity time read hours out. Tagging it "Z" makes toLocale* render it
        //   in the viewer's own system zone, which is what the feed should show.
        // asUtc = false → for DATE-ONLY fields (ordered / invoiced / movement
        //   dates). These carry no meaningful time of day, so the value is parsed
        //   as it stands and never shifted — the calendar day shown always matches
        //   the day stored, whatever the viewer's zone.
        // Strings already carrying a "Z" or a ±hh:mm offset are left untouched.
        //
        // Ported from VAS_098 / VAS_099 / VAS_102, which all carried this bug.
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

        function formatDate(value) {
            var d = parseDbDate(value, false);
            if (!d) return "";
            try { return d.toLocaleDateString(window.navigator.language, { year: "numeric", month: "short", day: "2-digit" }); }
            catch (e) { return d.toDateString(); }
        }

        function formatDateTime(value) {
            var d = parseDbDate(value, true);
            if (!d) return "";
            try {
                var dp = d.toLocaleDateString(window.navigator.language, { month: "short", day: "2-digit" });
                var tp = d.toLocaleTimeString(window.navigator.language, { hour: "2-digit", minute: "2-digit" });
                return dp + ", " + tp;
            } catch (e) { return d.toString(); }
        }

        this.getRoot = function () { return $root; };
    };

    VAS.VAS_106_OverviewSalesOrder.prototype.startPanel = function (windowNo, curTab) {
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
    VAS.VAS_106_OverviewSalesOrder.prototype.refreshPanelData = function (recordID, selectedRow) {
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
        // Held rather than fetched outright: the insert flag is not always up
        // yet when we get here, so scheduleFetch asks once more before loading.
        this.scheduleFetch(recordID);
    };

    /* Set width as per window width */
    VAS.VAS_106_OverviewSalesOrder.prototype.sizeChanged = function (width) {
        this.panelWidth = width;
    };

    /* Release variables from memory */
    VAS.VAS_106_OverviewSalesOrder.prototype.dispose = function () {
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
