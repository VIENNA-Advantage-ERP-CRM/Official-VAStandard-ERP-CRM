/************************************************************
 * Module Name    : VAS
 * Purpose        : Expected Landed Cost tab panel for the Purchase Order
 *                  window (C_Order, IsSOTrx = 'N'). A title strip (title, PO
 *                  Number · Ordered date, state pill) above a body that opens
 *                  with the order's details card (vendor + buyer | document
 *                  type, currency, order lines, the origins it was raised from)
 *                  and then holds the cost element table, the generated
 *                  distribution lines of each entry and — while the order is
 *                  drafted — the add / edit form. The panel scrolls as one
 *                  piece: nothing is pinned.
 *
 *                  Draft (DocStatus = 'DR') is editable; completed
 *                  (DocStatus = 'CO') is strictly read-only, with each
 *                  entry's generated lines expanded by default. The server
 *                  enforces that rule independently.
 *
 *                  Everything shown comes from
 *                  VAS_167_PurchaseOrderLandedCost/GetLandedCostPanel:
 *                  cost elements (M_CostElement), currencies (C_Currency),
 *                  currency rate types (C_ConversionType), the entries
 *                  (C_ExpectedCost) with their amount converted server-side
 *                  into the document currency, and the generated lines
 *                  (C_ExpectedCostDistribution). No exchange rate, cost
 *                  element list or allocation is computed in the browser,
 *                  and this panel carries no Complete Purchase Order action
 *                  (completion is owned by the window's own document action).
 * Chronological development:
 *   VAI163   2026-07-30  Created
 *   VAI163   2026-07-31  Header redesigned to the VAS_092 Purchase Order
 *                        Overview pattern — title strip with state pills over a
 *                        two-column details card. Vendor, buyer, currency and
 *                        order-line count are now surfaced; document type moved
 *                        off the sub-line into the card.
 *   VAI163   2026-08-04  - An order with no expected landed cost shows only the
 *                          message: the Cost Elements caption and the empty
 *                          table frame are not rendered at all, in draft as well
 *                          as once the order is completed. The draft form stays,
 *                          since it is how the first entry is added.
 *                        - Cost Distribution drops its "Select type" placeholder
 *                          (the list is the fixed set of types, so it stands on
 *                          the first one) and its options are indented.
 *   VAI163   2026-08-04  - Cost Element, Currency and Currency Rate Type drop
 *                          their "select ..." placeholders too, and every option
 *                          list is indented. Currency comes up on the order's own
 *                          (pricelist) currency and the rate type on Spot, which
 *                          the server resolves from the tenant's own
 *                          C_ConversionType rows (DefaultConversionTypeId).
 *                        - The Amount field accepts a number only: anything that
 *                          could not be part of one is dropped as it is typed,
 *                          pasted or dragged in (sanitizeAmount).
 *   VAI163   2026-08-04  - The entries stay editable until the order is COMPLETED
 *                          (data.IsEditable), not only while it is drafted, so an
 *                          In Progress purchase order can still be worked on.
 *                          Every edit affordance, the badge and the caption hint
 *                          now read that flag; the server enforces the same rule.
 *                        - The trailing hint row under the form is gone (both the
 *                          draft and the completed variants), and with it the
 *                          hintRow builder. The notice strip above the section
 *                          stays, reworded to the new rule (VAS_167_EditableNotice).
 *                        - Generated Lines drawers start collapsed and open on
 *                          the entry's own Lines button.
 *                        - A generated line names the order line's Attribute Set
 *                          Instance after the product.
 *   VAI163   2026-08-04  - The header card names the origins the order was raised
 *                          from — the contract (C_Order.VAS_ContractMaster_ID),
 *                          the RFQ (C_RfQResponse.C_Order_ID), the project
 *                          (C_ProjectLine.C_OrderPO_ID) and the requisition
 *                          (M_RequisitionLine.C_OrderLine_ID, or through the RFQ)
 *                          — and each value opens that record: openRecord()
 *                          resolves the window by NAME (WINDOW_NAME_BY_TABLE ->
 *                          VAS_ContractMaster / VAS_RFQ / VAS_Project /
 *                          VAS_Requisition) through
 *                          VAS_167_PurchaseOrderLandedCost/GetWindow_ID,
 *                          remembering the id per name, and falls back to the
 *                          table's zoom target when a name cannot be resolved —
 *                          the VAS_092 record-open pattern.
 *   VAI163   2026-08-05  The details card names the sales order the PO was
 *                        raised against (C_Order.Ref_Order_ID), clickable like
 *                        the other origins. Both sides of that reference live in
 *                        C_Order, so the link carries an IsSOTrx flag
 *                        (WINDOW_NAME_BY_TABLE_SOTRX -> VAS_SalesOrder) and
 *                        opens the Sales Order window rather than the Purchase
 *                        Order one.
 *   VAI163   2026-08-05  Class prefix renamed MPC-vaselc- -> vas_167- so the panel's
 *                          styles cannot collide with another panel's.
 *   VAI163   2026-08-05  New Record / Copy Record now empty the panel instead
 *                        of leaving the previously selected record on screen.
 *                        Both refreshPanelData and a new data-status listener
 *                        ask isTabInserting(), which reads GridTab.gridTable
 *                        .getIsInserting() — the flag GridTable.dataNew() raises
 *                        for both actions. The record id cannot answer it: a
 *                        copied row carries the source record's key until saved.
 *                        Ported from VAS_092.
 *   VAI163   2026-08-06  The cost-elements table paginates at 15 rows a page
 *                        (COSTS_PER_PAGE), with buildPager() / pagerButton() and
 *                        the chevLeft / chevRight icons added here — the panel had
 *                        no pager of its own. A table that fits on one page shows
 *                        no controls, the totals footer keeps covering the whole
 *                        order rather than the page, and a page's rows are replaced
 *                        in place so the card, header and footer are untouched.
 *                        Repainting rows is safe because every row control is
 *                        reached through a delegated [data-elc-*] handler on $root.
 *                        The page resets to the first alongside editId / linesOpen
 *                        whenever the payload is re-read — a record change and a
 *                        save / remove reload alike.
 *   VAI163   2026-08-07  Emits the vas_167-prefixed modifier classes the
 *                        stylesheet now uses, the runtime-built ones included
 *                        ("vas_167-tone-" + tone).
 *   VAI163   2026-08-07  The details card names the MRP plan the order was
 *                        generated by (VAMRP_PlanRun_ID), clickable like every
 *                        other origin. Several plan runs can feed one order when
 *                        the id sits on the lines, so the first is named and the
 *                        rest hinted with "+n". The plan is NOT in
 *                        WINDOW_NAME_BY_TABLE — its window belongs to an optional
 *                        module, so the link resolves through the table's own
 *                        zoom target, as it does in VAS_092.
 *   VAI163   2026-08-07  The title strip is no longer frozen above the body: the
 *                        panel scrolls as one piece (the scroll moved to
 *                        .vas_167-root in the stylesheet), so the selection reset
 *                        scrolls $root rather than $body and showBusy() anchors
 *                        the overlay to the current offset — absolutely
 *                        positioned in a scroll container, it would otherwise
 *                        scroll out of sight with the content behind it.
 *   VAI163   2026-08-07  The details card names the blanket purchase order this
 *                        one was released against (C_Order.C_Order_Blanket),
 *                        clickable like every other origin. C_Order now appears
 *                        in BOTH window maps — the blanket takes the purchase
 *                        side (VAS_BlanketPurchaseOrder) and the sales-order
 *                        reference keeps the sales side through its IsSOTrx flag.
 *   VAI163   2026-08-19  The blanket order field appears for a release order
 *                        whose link is recorded on its LINES rather than on the
 *                        header — the server reads both now — and a release
 *                        drawing on several blankets names the first and tallies
 *                        the rest ("+n more"), as the plan run beside it does.
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

    VAS.VAS_167_PurchaseOrderLandedCost = function () {
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
                // New (unsaved) record — nothing to show against it.
                if ($self.record_ID) {
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
        var $header;
        var $headSub;
        var $headPills;
        var $headBadge;
        var $body;
        var $emptyState;
        var data = null;
        // C_ExpectedCost_ID currently being edited; null while adding.
        var editId = null;
        // Generated-lines drawer state, keyed by C_ExpectedCost_ID. Absent means
        // collapsed — the allocation opens on the entry's own Lines button.
        var linesOpen = {};
        // True while a create / update / delete is in flight, so the form cannot
        // be submitted twice.
        var saving = false;
        // Maximum cost-element rows shown per page; the table paginates beyond
        // this. An order carrying many expected costs otherwise pushed the Add /
        // Edit form far below the fold.
        var COSTS_PER_PAGE = 15;
        var costsPage = 0;   // 0-based

        // Some AD_Message keys may not be seeded yet; fall back to a readable
        // English default so the panel never renders raw keys.
        var MSG_DEFAULTS = {
            VAS_167_NoData: "No purchase order selected",
            VAS_167_Title: "Expected Landed Cost",
            VAS_167_BadgeExpected: "Expected",
            VAS_167_BadgeGenerated: "Lines Generated",
            VAS_167_EditableNotice: "Expected landed cost is editable until the purchase order is completed. Lines are generated against each distribution type on completion.",
            VAS_167_NoLinesNotice: "This purchase order has no product lines, so there is nothing to allocate expected landed cost against.",
            VAS_167_NoRateNotice: "At least one entry has no exchange rate for its currency rate type, so it is left out of the converted total.",
            VAS_167_CostElements: "Cost Elements",
            VAS_167_EditableUntilCompleted: "Editable until completed",
            VAS_167_Locked: "Locked",
            VAS_167_ColElement: "Cost Element / Distribution",
            VAS_167_ColAmount: "Amount",
            VAS_167_Empty: "No expected landed cost defined yet — add the first cost element below.",
            VAS_167_EmptyLocked: "No expected landed cost was defined on this purchase order.",
            VAS_167_NoRate: "No exchange rate",
            // Header details card
            VAS_167_Vendor: "Vendor",
            VAS_167_Buyer: "Buyer",
            VAS_167_DocType: "Document Type",
            VAS_167_Ordered: "Ordered",
            VAS_167_OrderLines: "Order Lines",
            VAS_167_Contract: "Contract",
            VAS_167_Rfq: "RFQ",
            VAS_167_Project: "Project",
            VAS_167_Requisition: "Requisition",
            VAS_167_SalesOrder: "Sales Order",
            VAS_167_BlanketOrder: "Blanket Order",
            VAS_167_Plan: "Plan",
            VAS_167_More: "more",
            VAS_167_OpenRecord: "Open",
            VAS_167_NoVendor: "—",
            VAS_167_POTotal: "PO Total",
            VAS_167_ExpectedTotal: "Expected Landed Cost",
            // Row actions
            VAS_167_Edit: "Edit",
            VAS_167_Remove: "Remove",
            VAS_167_Lines: "Lines",
            VAS_167_ShowLines: "Show generated lines",
            VAS_167_HideLines: "Hide generated lines",
            // Generated lines
            VAS_167_GeneratedLines: "Generated Lines",
            VAS_167_ColOrderLine: "Order Line (Product)",
            VAS_167_ColBase: "Base",
            VAS_167_ColQty: "Qty",
            VAS_167_Code: "Code",
            VAS_167_BaseQty: "Qty",
            VAS_167_BaseValue: "Value",
            VAS_167_BaseEqual: "Equal",
            VAS_167_BaseVolume: "Volume",
            VAS_167_BaseWeight: "Weight",
            VAS_167_Of: "of",
            // Cost-table pager
            VAS_167_Showing: "Showing",
            VAS_167_Page: "Page",
            VAS_167_Previous: "Previous",
            VAS_167_Next: "Next",
            VAS_167_Distributed: "Distributed",
            VAS_167_NotReconciled: "Does not add up to the entry amount",
            VAS_167_NoGeneratedLines: "No generated lines for this cost element.",
            // Form
            VAS_167_AddCaption: "Add Expected Landed Cost",
            VAS_167_EditCaption: "Edit Expected Landed Cost",
            VAS_167_FldDistribution: "Cost Distribution",
            VAS_167_FldCostElement: "Cost Element",
            VAS_167_FldAmount: "Amount",
            VAS_167_FldCurrency: "Currency",
            VAS_167_FldRateType: "Currency Rate Type",
            VAS_167_BtnAdd: "Add Expected Cost",
            VAS_167_BtnUpdate: "Update",
            VAS_167_BtnCancel: "Cancel",
            VAS_167_NoteIncomplete: "Fill the required fields (*)",
            VAS_167_NoteReadyAdd: "Ready — click Add",
            VAS_167_NoteReadyUpdate: "Ready — click Update",
            VAS_167_Saving: "Saving …",
            VAS_167_ConfirmRemove: "Remove this expected landed cost entry?",
            VAS_167_SaveFailed: "The expected landed cost could not be saved.",
            VAS_167_DeleteFailed: "The expected landed cost could not be removed."
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
            $root = $('<div class="vas_167-root"></div>');

            // ---- Title strip ----
            // Title + sub-line on the left, state pills on the right; the order's
            // own details live in the card at the top of the body below. It
            // scrolls with that body — the panel scrolls as one piece.
            $header = $('<div class="vas_167-head"></div>');
            var $htext = $('<div class="vas_167-headText"></div>');
            $htext.append($('<div class="vas_167-headTitle"></div>').text(getMsg("VAS_167_Title")));
            $headSub = $('<div class="vas_167-headSub"></div>');
            $htext.append($headSub);
            $headPills = $('<div class="vas_167-headPills"></div>');
            $headBadge = $('<span class="vas_167-badge"></span>');
            $headPills.append($headBadge);
            $header.append($htext).append($headPills);

            // ---- Scrolling body ----
            $body = $('<div class="vas_167-body"></div>');
            $emptyState = $('<div class="vas_167-noRecord"></div>').text(getMsg("VAS_167_NoData"));
            $emptyState.hide();

            $root.append($header).append($body).append($emptyState);
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
            if (show) {
                // $root is the scroll container now, and an absolutely positioned
                // overlay is placed in its CONTENT, so at top:0 it would sit above
                // the visible area whenever the reader has scrolled — a save from
                // the form near the bottom would flash no spinner at all.
                // Anchoring it to the current offset keeps the veil, and the
                // spinner centred in it, over what is actually on screen.
                $busy.css("top", ($root.scrollTop() || 0) + "px");
            }
            $busy[0].style.visibility = show ? "visible" : "hidden";
        }

        // ----------------------------------------------------------------- //
        //  Data                                                             //
        // ----------------------------------------------------------------- //

        this.fetchData = function (recordID) {
            showBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + "VAS_167_PurchaseOrderLandedCost/GetLandedCostPanel",
                type: "GET",
                dataType: "json",
                data: { C_Order_ID: recordID },
                success: function (raw) {
                    data = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                    // A newly selected order starts in add mode with every
                    // generated-lines drawer collapsed, on the first page of the
                    // cost table. This runs after a save / remove too, which is
                    // deliberate and matches the drawer state beside it: the list
                    // has just changed under the reader, so the table returns to
                    // the top exactly as the panel's scroll position does below.
                    editId = null;
                    linesOpen = {};
                    costsPage = 0;
                    saving = false;
                    render();
                    // Selection always returns the reader to the top of the panel.
                    // The whole panel scrolls now, title strip included, so the
                    // scroll to reset is the root's and not the body's.
                    $root.scrollTop(0);
                    showBusy(false);
                },
                error: function (err) {
                    console.log(err);
                    showBusy(false);
                }
            });
        };

        this.clear = function () {
            data = null;
            editId = null;
            linesOpen = {};
            costsPage = 0;
            render();
        };

        // ----------------------------------------------------------------- //
        //  Render                                                           //
        // ----------------------------------------------------------------- //

        function render() {
            $body.empty();

            if (!data || !data.PurchaseOrderId) {
                $header.hide();
                $body.hide();
                $emptyState.show();
                return;
            }

            $emptyState.hide();
            $header.show();
            $body.show();

            renderHeaderMeta();

            var $wrap = $('<div class="vas_167-stack"></div>');

            $wrap.append(renderHeaderCard());

            // Editable-state notice. A locked order shows no equivalent strip —
            // the badge and the absent controls already say it is frozen.
            if (data.IsEditable) {
                //$wrap.append(noticeStrip("pencil", "warn", getMsg("VAS_167_EditableNotice")));
                if (!data.EligibleLineCount) {
                    $wrap.append(noticeStrip("alert", "risk", getMsg("VAS_167_NoLinesNotice")));
                }
            }
            if (data.HasMissingConversion) {
                $wrap.append(noticeStrip("alert", "risk", getMsg("VAS_167_NoRateNotice")));
            }

            // With no expected landed cost on the order there is nothing for the
            // Cost Elements section to show: its caption and the table frame would
            // be an empty scaffold, so only the message renders — in draft too,
            // where the form below it is how the first entry is added.
            if ((data.ExpectedCosts || []).length) {
                $wrap.append(renderSubCaption());
                $wrap.append(renderTable());
            } else {
                $wrap.append(renderEmptyNotice());
            }

            // The form, and nothing under it: the trailing hint row is gone.
            if (data.IsEditable) {
                $wrap.append(renderForm());
            }

            $body.append($wrap);
            validateForm();
        }

        // Title strip: PO Number · Ordered date on the sub-line, state pill on
        // the right. Re-rendered on every selection so the reader always sees
        // which record they are on. The remaining order facts belong to the
        // details card, not to this line.
        function renderHeaderMeta() {
            $headSub.empty();
            $headSub.append($('<b></b>').text(data.PurchaseOrderNumber || ""));
            var ordered = formatDate(data.OrderDate);
            if (ordered) {
                $headSub.append(document.createTextNode(
                    " · " + getMsg("VAS_167_Ordered") + " " + ordered));
            }

            $headBadge.removeClass("vas_167-is-generated");
            if (data.IsEditable) {
                $headBadge.text(getMsg("VAS_167_BadgeExpected"));
            } else {
                $headBadge.text(getMsg("VAS_167_BadgeGenerated")).addClass("vas_167-is-generated");
            }
        }

        // Details card: vendor identity (left, behind a divider) beside the
        // order's own fields (right, two across) — the Purchase Order Overview
        // header card, carrying what this panel already knows about the order.
        function renderHeaderCard() {
            var $card = $('<section class="vas_167-hdrCard"></section>');

            var $left = $('<div class="vas_167-hdrColL"></div>');
            $left.append($('<div class="vas_167-fLabel"></div>').text(getMsg("VAS_167_Vendor")));
            $left.append($('<div class="vas_167-vendName"></div>')
                .text(data.VendorName || getMsg("VAS_167_NoVendor")));
            if (data.BuyerName) {
                var $contact = $('<div class="vas_167-vendContact"></div>');
                var $bit = $('<span class="vas_167-contactBit"></span>');
                $bit.append(svgIcon("user"));
                $bit.append($('<span></span>').text(data.BuyerName));
                $contact.append($bit);
                $left.append($contact);
            }
            $card.append($left);

            var $right = $('<div class="vas_167-hdrColR"></div>');
            if (data.DocumentTypeName) {
                $right.append(headerField(getMsg("VAS_167_DocType"), data.DocumentTypeName));
            }
            var cur = (data.DocumentCurrencyCode || "") +
                      (data.DocumentCurrencySymbol ? " (" + data.DocumentCurrencySymbol + ")" : "");
            if ($.trim(cur)) {
                $right.append(headerField(getMsg("VAS_167_FldCurrency"), cur));
            }
            // PO Total is deliberately absent — it lives in the table footer,
            // beside the expected total it is there to be compared against.
            $right.append(headerField(getMsg("VAS_167_OrderLines"),
                String(data.EligibleLineCount || 0)));
            // Origins the order was raised from — each shown only when it exists,
            // and clickable: the value opens that record.
            if (data.ContractMasterId > 0) {
                $right.append(headerField(getMsg("VAS_167_Contract"),
                    data.ContractMasterNo || ("#" + data.ContractMasterId),
                    "VAS_ContractMaster", data.ContractMasterId));
            }
            if (data.RfqId > 0) {
                $right.append(headerField(getMsg("VAS_167_Rfq"),
                    data.RfqNo || ("#" + data.RfqId), "C_RfQ", data.RfqId));
            }
            if (data.ProjectId > 0) {
                $right.append(headerField(getMsg("VAS_167_Project"),
                    data.ProjectNo || ("#" + data.ProjectId), "C_Project", data.ProjectId));
            }
            if (data.RequisitionId > 0) {
                $right.append(headerField(getMsg("VAS_167_Requisition"),
                    data.RequisitionNo || ("#" + data.RequisitionId),
                    "M_Requisition", data.RequisitionId));
            }
            // Sales order the PO was raised against (C_Order.Ref_Order_ID).
            // Opened as a sales transaction so the framework resolves the Sales
            // Order window — both sides of the reference live in C_Order, and
            // without the flag the link would open the Purchase Order window.
            if (data.SalesOrderId > 0) {
                $right.append(headerField(getMsg("VAS_167_SalesOrder"),
                    data.SalesOrderNo || ("#" + data.SalesOrderId),
                    "C_Order", data.SalesOrderId, true));
            }
            // Blanket purchase order this one was released against. Both sides
            // live in C_Order like the sales order above, but this one is a
            // PURCHASE transaction — it carries no IsSOTrx flag, so it takes the
            // plain mapping and opens the Blanket Purchase Order window.
            //
            // The server reaches it through the order header OR its lines, and a
            // release whose lines draw on several blankets names the first and
            // tallies the rest, the way the plan run below already does.
            if (data.BlanketOrderId > 0) {
                var blanketVal = data.BlanketOrderNo || ("#" + data.BlanketOrderId);
                if (data.BlanketOrderCount > 1) {
                    blanketVal += " +" + (data.BlanketOrderCount - 1) + " " + getMsg("VAS_167_More");
                }
                $right.append(headerField(getMsg("VAS_167_BlanketOrder"), blanketVal,
                    "C_Order", data.BlanketOrderId));
            }
            // MRP plan run the order was generated by (VAMRP_PlanRun_ID). The
            // id can sit on the lines rather than the header, so several runs
            // can feed one order: the first is named and opened, the rest are
            // hinted with "+n". VAMRP is optional — the server sends no plan at
            // all where the module is not deployed, and this row is skipped.
            if (data.PlanRunId > 0) {
                var planVal = data.PlanRunNo || ("#" + data.PlanRunId);
                if (data.PlanRunCount > 1) {
                    planVal += " +" + (data.PlanRunCount - 1) + " " + getMsg("VAS_167_More");
                }
                $right.append(headerField(getMsg("VAS_167_Plan"), planVal,
                    "VAMRP_PlanRun", data.PlanRunId));
            }
            $card.append($right);

            return $card;
        }

        // A labelled header field. Supplying a table + record id makes the value a
        // link that opens that record.
        function headerField(label, value, openTable, openId, isSOTrx) {
            var $f = $('<div class="vas_167-hdrField"></div>');
            $f.append($('<div class="vas_167-fLabel"></div>').text(label));
            var $v = $('<div class="vas_167-fVal"></div>').text(value);
            if (openTable && +openId > 0) {
                $v.addClass("vas_167-is-link")
                    .attr("data-elc-open-table", openTable)
                    .attr("data-elc-open-id", openId)
                    .attr("title", getMsg("VAS_167_OpenRecord"));
                // Dual-purpose tables (C_Order is both sides) need to say which
                // window they mean.
                if (isSOTrx) $v.attr("data-elc-open-sotrx", "Y");
            }
            $f.append($v);
            return $f;
        }

        function noticeStrip(icon, tone, text) {
            var $s = $('<div class="vas_167-strip"></div>').addClass("vas_167-tone-" + tone);
            $s.append(svgIcon(icon));
            $s.append($('<span></span>').text(text));
            return $s;
        }

        function renderSubCaption() {
            var $row = $('<div class="vas_167-subcap"></div>');
            var $cap = $('<span class="vas_167-cap"></span>');
            $cap.append(svgIcon("coins"));
            $cap.append($('<span></span>').text(
                getMsg("VAS_167_CostElements") + " (" + (data.ExpectedCostCount || 0) + ")"));
            $row.append($cap);
            $row.append($('<span class="vas_167-capHint"></span>').text(
                data.IsEditable ? getMsg("VAS_167_EditableUntilCompleted")
                                : getMsg("VAS_167_Locked")));
            return $row;
        }

        // ---------- Cost elements table ---------- //

        // Stands in for the whole Cost Elements section when the order carries no
        // expected landed cost: the message alone, on its own card, with no
        // caption and no empty table above it. A drafted order is told the entry
        // is added below; a completed one simply that none was defined.
        function renderEmptyNotice() {
            return $('<div class="vas_167-empty vas_167-is-standalone"></div>').text(
                data.IsEditable ? getMsg("VAS_167_Empty") : getMsg("VAS_167_EmptyLocked"));
        }

        // Only ever called with at least one entry — see render().
        function renderTable() {
            var $tbl = $('<div class="vas_167-table"></div>');

            var $head = $('<div class="vas_167-row vas_167-rowHead"></div>');
            $head.append($('<span></span>').text(getMsg("VAS_167_ColElement")));
            $head.append($('<span class="vas_167-ta-r"></span>').text(getMsg("VAS_167_ColAmount")));
            $head.append($('<span></span>'));
            $tbl.append($head);

            var costs = data.ExpectedCosts || [];

            // The totals footer always covers the whole order, never the page, so
            // it is built once and the page's rows are inserted ahead of it.
            var $foot = buildTableFooter();
            $tbl.append($foot);

            // The pager closes the table card, under the footer. A table that fits
            // on one page carries no controls at all.
            var $pager = $('<div class="vas_167-pager"></div>');
            if (costs.length > COSTS_PER_PAGE) $tbl.append($pager);

            // Rows are replaced in place, so the card, its header and its footer
            // stay exactly as they were. Safe to repaint: every row control is
            // reached through a delegated [data-elc-*] handler bound on $root, not
            // a handler bound to the row itself.
            function paintPage() {
                var pageCount = Math.max(1, Math.ceil(costs.length / COSTS_PER_PAGE));
                if (costsPage >= pageCount) costsPage = pageCount - 1;
                if (costsPage < 0) costsPage = 0;

                var start = costsPage * COSTS_PER_PAGE;
                var end = Math.min(costs.length, start + COSTS_PER_PAGE);

                $tbl.find(".vas_167-rowBody, .vas_167-linesWrap").remove();
                for (var i = start; i < end; i++) {
                    $foot.before(buildCostRow(costs[i]));
                    // Generated lines belong to a completed order only — an order
                    // that is still editable has nothing generated yet.
                    if (!data.IsEditable) $foot.before(buildLinesBlock(costs[i]));
                }

                buildPager($pager, costsPage, pageCount, costs.length, start, end,
                    function (p) { costsPage = p; paintPage(); });
            }

            paintPage();
            return $tbl;
        }

        // Range caption on the left, Previous / page-of / Next on the right.
        // Rebuilt on every page change so the disabled states stay accurate.
        // `page` is 0-based and `onGo` is handed the page to move to, so the
        // caller owns its page state. Nothing is drawn for a single-page table.
        function buildPager($pager, page, pageCount, total, start, end, onGo) {
            $pager.empty();
            if (pageCount <= 1) return;

            $pager.append($('<span class="vas_167-pgRange"></span>').text(
                getMsg("VAS_167_Showing") + " " + (start + 1) + "-" + end + " " +
                getMsg("VAS_167_Of") + " " + total));

            var $ctrls = $('<span class="vas_167-pgCtrls"></span>');
            $ctrls.append(pagerButton(getMsg("VAS_167_Previous"), "chevLeft",
                page <= 0, function () { onGo(page - 1); }));
            $ctrls.append($('<span class="vas_167-pgPos"></span>').text(
                getMsg("VAS_167_Page") + " " + (page + 1) + " " +
                getMsg("VAS_167_Of") + " " + pageCount));
            $ctrls.append(pagerButton(getMsg("VAS_167_Next"), "chevRight",
                page >= pageCount - 1, function () { onGo(page + 1); }));
            $pager.append($ctrls);
        }

        function pagerButton(label, icon, disabled, handler) {
            var $b = $('<span class="vas_167-pgBtn"></span>');
            if (icon === "chevLeft") $b.append(svgIcon(icon));
            $b.append($('<span></span>').text(label));
            if (icon === "chevRight") $b.append(svgIcon(icon));
            if (disabled) $b.addClass("vas_167-is-disabled");
            else $b.on("click", handler);
            return $b;
        }

        function buildCostRow(c) {
            var $row = $('<div class="vas_167-row vas_167-rowBody"></div>');
            if (editId === c.ExpectedCostId) $row.addClass("vas_167-is-editing");

            // ---- Identity: element name, distribution chip, meta sub-line ----
            var $id = $('<span></span>');
            $id.append($('<div class="vas_167-name"></div>')
                .attr("title", c.CostElementName || "")
                .text(c.CostElementName || ""));

            var $chipWrap = $('<div class="vas_167-chipWrap"></div>');
            var $chip = $('<span class="vas_167-distChip"></span>')
                .addClass("vas_167-dist-" + distTone(c.DistributionCode));
            $chip.append($('<span class="vas_167-dot"></span>'));
            $chip.append($('<span></span>').text(c.DistributionLabel || c.DistributionCode || ""));
            $chipWrap.append($chip);
            $id.append($chipWrap);

            // Meta = the real C_ExpectedCost_ID and the currency rate type; no
            // position-derived identifier is invented.
            var meta = "#" + c.ExpectedCostId;
            if (c.ConversionTypeName) meta += " · " + c.ConversionTypeName;
            $id.append($('<div class="vas_167-sub"></div>').text(meta));
            if (c.Description) {
                $id.append($('<div class="vas_167-sub"></div>')
                    .attr("title", c.Description).text(c.Description));
            }
            $row.append($id);

            // ---- Amount: entered amount + converted document-currency line ----
            var $amt = $('<span class="vas_167-amt"></span>');
            var $v = $('<div class="vas_167-amtV"></div>');
            $v.append(document.createTextNode(formatNumber(c.EnteredAmount, enteredPrecision(c))));
            $v.append($('<span></span>').text(c.EnteredCurrencyCode || ""));
            $amt.append($v);
            if (!c.IsSameCurrency) {
                if (c.IsConversionAvailable) {
                    $amt.append($('<div class="vas_167-sub"></div>').text(
                        "≈ " + formatNumber(c.ConvertedAmount, docPrecision()) +
                        " " + (data.DocumentCurrencyCode || "")));
                } else {
                    $amt.append($('<div class="vas_167-sub vas_167-is-warn"></div>')
                        .text(getMsg("VAS_167_NoRate")));
                }
            }
            $row.append($amt);

            // ---- Actions: edit / remove in draft, a Lines toggle when completed ----
            var $act = $('<span class="vas_167-acts"></span>');
            if (data.IsEditable) {
                $act.append(iconButton("pencil", "edit", getMsg("VAS_167_Edit"))
                    .attr("data-elc-edit", c.ExpectedCostId));
                $act.append(iconButton("trash", "rm", getMsg("VAS_167_Remove"))
                    .attr("data-elc-remove", c.ExpectedCostId));
            } else {
                var open = isLinesOpen(c.ExpectedCostId);
                var $tg = $('<button type="button" class="vas_167-linesBtn"></button>')
                    .attr("data-elc-lines", c.ExpectedCostId)
                    .attr("aria-expanded", open ? "true" : "false")
                    .attr("aria-label", open ? getMsg("VAS_167_HideLines") : getMsg("VAS_167_ShowLines"))
                    .attr("title", open ? getMsg("VAS_167_HideLines") : getMsg("VAS_167_ShowLines"));
                $tg.append($('<span></span>').text(getMsg("VAS_167_Lines")));
                $tg.append(svgIcon("chevDown"));
                if (open) $tg.addClass("vas_167-is-open");
                $act.append($tg);
            }
            $row.append($act);

            return $row;
        }

        // Generated (server-side) distribution lines for one entry. Read straight
        // from C_ExpectedCostDistribution — never recalculated here.
        function buildLinesBlock(c) {
            var $wrap = $('<div class="vas_167-linesWrap"></div>')
                .attr("data-elc-linesfor", c.ExpectedCostId);
            if (!isLinesOpen(c.ExpectedCostId)) $wrap.addClass("vas_167-is-closed");

            var $cap = $('<div class="vas_167-linesCap"></div>');
            $cap.append(svgIcon("checkCircle"));
            $cap.append($('<span></span>').text(
                getMsg("VAS_167_GeneratedLines") + " · " +
                (c.DistributionLabel || c.DistributionCode || "")));
            $wrap.append($cap);

            var lines = c.GeneratedLines || [];
            if (!lines.length) {
                $wrap.append($('<div class="vas_167-linesEmpty"></div>')
                    .text(getMsg("VAS_167_NoGeneratedLines")));
                return $wrap;
            }

            var $tbl = $('<div class="vas_167-lTable"></div>');
            var $lh = $('<div class="vas_167-lRow vas_167-lHead"></div>');
            $lh.append($('<span></span>').text(getMsg("VAS_167_ColOrderLine")));
            $lh.append($('<span></span>').text(getMsg("VAS_167_ColBase")));
            $lh.append($('<span class="vas_167-ta-c"></span>').text(getMsg("VAS_167_ColQty")));
            $lh.append($('<span class="vas_167-ta-r"></span>').text(getMsg("VAS_167_ColAmount")));
            $tbl.append($lh);

            for (var i = 0; i < lines.length; i++) {
                $tbl.append(buildLineRow(c, lines[i], lines.length));
            }

            // Footer reports the distributed total in the currency the lines are
            // actually stored in, and — when that differs from the document
            // currency — the document-currency equivalent of the parent amount
            // beside it. The two are never conflated.
            var $foot = $('<div class="vas_167-lFoot"></div>');
            var $tf = $('<span class="vas_167-tf"></span>');
            $tf.append(document.createTextNode(getMsg("VAS_167_Distributed")));
            $tf.append($('<b></b>').text(
                formatNumber(c.DistributedAmount, enteredPrecision(c)) +
                " " + (c.EnteredCurrencyCode || "")));
            $foot.append($tf);

            if (!c.IsSameCurrency && c.IsConversionAvailable) {
                var $conv = $('<span class="vas_167-tf"></span>');
                $conv.append(document.createTextNode("≈"));
                $conv.append($('<b></b>').text(
                    formatNumber(c.ConvertedAmount, docPrecision()) +
                    " " + (data.DocumentCurrencyCode || "")));
                $foot.append($conv);
            }
            if (c.IsReconciled === false) {
                $foot.append($('<span class="vas_167-tf vas_167-is-warn"></span>')
                    .text(getMsg("VAS_167_NotReconciled")));
            }
            $tbl.append($foot);

            $wrap.append($tbl);
            return $wrap;
        }

        function buildLineRow(c, g, lineCount) {
            var $row = $('<div class="vas_167-lRow vas_167-lBody"></div>');

            // Product name, with the line's Attribute Set Instance (size / lot /
            // serial ...) after it — only when the line carries a real instance;
            // a blank or "--" / "-" placeholder is not an attribute.
            var $item = $('<span></span>');
            var $name = $('<div class="vas_167-name"></div>');
            $name.append($('<span></span>').text(g.ProductName || ""));
            var asi = $.trim(g.AttributeSetInstance || "");
            var hasAsi = (asi && asi !== "--" && asi !== "-");
            if (hasAsi) {
                $name.append($('<span class="vas_167-attr"></span>').text(asi));
            }
            $name.attr("title", (g.ProductName || "") + (hasAsi ? " — " + asi : ""));
            $item.append($name);
            $item.append($('<div class="vas_167-sub"></div>')
                .text(getMsg("VAS_167_Code") + " " + (g.ProductCode || "")));
            $row.append($item);

            $row.append($('<span class="vas_167-base"></span>').text(baseLabel(c, g, lineCount)));
            $row.append($('<span class="vas_167-ta-c"></span>').text(formatNumber(g.LineQuantity, 0)));

            // Stored in the entry's entered currency, so shown in it — the platform
            // converts these amounts to the accounting / invoice currency later,
            // on the accounting date (see the model's currency convention note).
            var $amt = $('<span class="vas_167-ta-r"></span>');
            $amt.append($('<b></b>').text(formatNumber(g.AllocatedAmount, enteredPrecision(c))));
            $row.append($amt);

            return $row;
        }

        // The audit basis behind each slice, built from the stored Base value and
        // the parent's distribution code.
        function baseLabel(c, g, lineCount) {
            var of = " " + getMsg("VAS_167_Of") + " ";
            switch (c.DistributionCode) {
                case "Q":
                    return getMsg("VAS_167_BaseQty") + " " + formatNumber(g.AllocationBase, 0) +
                           of + formatNumber(g.TotalAllocationBase, 0);
                // C = Costs, I = Import Value (legacy rows) — both value bases.
                case "C":
                case "I":
                    return getMsg("VAS_167_BaseValue") + " " + formatNumber(g.AllocationBase, 2) +
                           of + formatNumber(g.TotalAllocationBase, 2);
                case "V":
                    return getMsg("VAS_167_BaseVolume") + " " + formatNumber(g.AllocationBase, 2) +
                           of + formatNumber(g.TotalAllocationBase, 2);
                case "W":
                    return getMsg("VAS_167_BaseWeight") + " " + formatNumber(g.AllocationBase, 2) +
                           of + formatNumber(g.TotalAllocationBase, 2);
                case "L":
                    return getMsg("VAS_167_BaseEqual") + " 1" + of + lineCount;
                default:
                    return formatNumber(g.AllocationBase, 2);
            }
        }

        // PO total beside the expected landed cost total — both in the document
        // currency, computed server-side from converted amounts.
        function buildTableFooter() {
            var $foot = $('<div class="vas_167-foot"></div>');

            var $po = $('<span class="vas_167-tf"></span>');
            $po.append(document.createTextNode(getMsg("VAS_167_POTotal")));
            $po.append($('<b></b>').text(money(data.PurchaseOrderTotal)));
            $foot.append($po);

            var $grand = $('<span class="vas_167-grand"></span>');
            $grand.append(document.createTextNode(getMsg("VAS_167_ExpectedTotal")));
            $grand.append($('<b></b>').text(money(data.ExpectedCostTotalConverted)));
            $foot.append($grand);

            return $foot;
        }

        // ---------- Add / edit form (drafted orders only) ---------- //

        function renderForm() {
            var editing = editId !== null;
            var c = editing ? findCost(editId) : null;
            if (editing && !c) { editId = null; editing = false; }

            var $form = $('<div class="vas_167-form"></div>');

            var $cap = $('<div class="vas_167-formCap"></div>');
            $cap.append(svgIcon(editing ? "pencil" : "plus"));
            $cap.append($('<span></span>').text(editing
                ? getMsg("VAS_167_EditCaption") + " · #" + c.ExpectedCostId
                : getMsg("VAS_167_AddCaption")));
            $form.append($cap);

            var $grid = $('<div class="vas_167-formGrid"></div>');
            // Cost Distribution carries no "select" placeholder: the list is the
            // fixed set of distribution types, so the first one stands selected
            // and the reader changes it. Its options are indented (see
            // selectField) so they read as a set under the closed control.
            $grid.append(selectField("elcDist", getMsg("VAS_167_FldDistribution"),
                data.Distributions, "Code", c ? c.DistributionCode : "",
                null, true));
            // Cost Element: no placeholder either — the list is what can be
            // booked against, so it stands on its first element.
            $grid.append(selectField("elcElem", getMsg("VAS_167_FldCostElement"),
                data.CostElements, "Id", c ? c.CostElementId : "",
                null, true));
            $grid.append(amountField(c ? c.EnteredAmount : ""));
            // Currency comes up on the order's own (pricelist) currency.
            $grid.append(selectField("elcCur", getMsg("VAS_167_FldCurrency"),
                data.Currencies, "Id", c ? c.EnteredCurrencyId : data.DocumentCurrencyId,
                null, true));
            // Rate type comes up on Spot — resolved on the server against the
            // tenant's own C_ConversionType rows, not assumed here.
            $grid.append(selectField("elcRate", getMsg("VAS_167_FldRateType"),
                data.ConversionTypes, "Id",
                c ? c.ConversionTypeId : data.DefaultConversionTypeId,
                null, true));
            $form.append($grid);

            var $foot = $('<div class="vas_167-formFoot"></div>');
            if (editing) {
                $foot.append($('<button type="button" class="vas_167-btn vas_167-is-ghost"></button>')
                    .attr("data-elc-cancel", "1").text(getMsg("VAS_167_BtnCancel")));
            }
            $foot.append($('<span class="vas_167-formNote"></span>'));
            var $submit = $('<button type="button" class="vas_167-btn vas_167-is-primary"></button>')
                .attr("data-elc-save", "1").prop("disabled", true);
            $submit.append(svgIcon(editing ? "check" : "plus"));
            $submit.append($('<span></span>').text(
                editing ? getMsg("VAS_167_BtnUpdate") : getMsg("VAS_167_BtnAdd")));
            $foot.append($submit);
            $form.append($foot);

            var $err = $('<div class="vas_167-formErr"></div>').hide();
            $form.append($err);

            return $form;
        }

        // Shared underline-only field: label above value, transparent background.
        function fieldShell(label) {
            var $ff = $('<div class="vas_167-ff"></div>');
            var $content = $('<div class="vas_167-ffBody"></div>');
            var $label = $('<label></label>');
            $label.append(document.createTextNode(label));
            $label.append($('<span class="vas_167-req"></span>').text("*"));
            $content.append($label);
            $ff.append($content);
            return { $ff: $ff, $content: $content };
        }

        // A placeholder is only added when one is passed — pass null for a list
        // that should stand on its first entry instead of on "select ...".
        // `indent` prefixes each option's label with a fixed space, so the values
        // sit slightly in from the edge of the open list.
        function selectField(name, label, items, valueKey, selectedValue, placeholder, indent) {
            var shell = fieldShell(label);
            shell.$content.addClass("vas_167-is-select");

            var $sel = $('<select class="vas_167-input"></select>').attr("data-elc-field", name);
            if (placeholder) {
                $sel.append($('<option value=""></option>').text(placeholder));
            }

            // Non-breaking: a leading plain space is collapsed away in an option.
            var pad = indent ? "  " : "";
            var list = items || [];
            var selected = (selectedValue === null || selectedValue === undefined)
                ? "" : String(selectedValue);
            for (var i = 0; i < list.length; i++) {
                var val = String(list[i][valueKey]);
                var $op = $('<option></option>').attr("value", val)
                    .text(pad + (list[i].Name || val));
                if (val === selected) $op.prop("selected", true);
                $sel.append($op);
            }
            shell.$content.append($sel);
            return shell.$ff;
        }

        function amountField(value) {
            var shell = fieldShell(getMsg("VAS_167_FldAmount"));
            var $inp = $('<input type="text" class="vas_167-input vas_167-is-num" inputmode="decimal" />')
                .attr("data-elc-field", "elcAmt")
                .attr("placeholder", "0.00")
                .val(value === "" || value === null || value === undefined ? "" : String(value));
            shell.$content.append($inp);
            return shell.$ff;
        }

        // ----------------------------------------------------------------- //
        //  Form state / validation                                           //
        // ----------------------------------------------------------------- //

        function formField(name) {
            return $body.find('[data-elc-field="' + name + '"]');
        }

        function readForm() {
            return {
                distributionCode: $.trim(formField("elcDist").val() || ""),
                costElementId:    parseInt(formField("elcElem").val() || "0", 10) || 0,
                amountText:       $.trim(formField("elcAmt").val() || ""),
                currencyId:       parseInt(formField("elcCur").val() || "0", 10) || 0,
                conversionTypeId: parseInt(formField("elcRate").val() || "0", 10) || 0
            };
        }

        // Strips everything that could never be part of an amount — letters,
        // symbols, spaces, a sign — leaving digits and the two separators the
        // parser below understands. Which of those is the decimal point and which
        // is grouping stays parseAmountInput's decision, so "1,234.50" can still
        // be typed; this only keeps the field free of characters that would make
        // it unreadable.
        function sanitizeAmount(text) {
            return String(text === null || text === undefined ? "" : text)
                .replace(/[^\d.,]/g, "");
        }

        // Reads a typed amount without assuming a decimal separator: "1,234.50"
        // and "1.234,50" both mean the same number, so whichever separator comes
        // last is the decimal point and the other is grouping. The result is
        // always sent to the server as an invariant "1234.5" string, so no
        // server-side culture setting can reinterpret it.
        function parseAmountInput(text) {
            var s = String(text || "").replace(/\s/g, "");
            if (!s) return NaN;
            var lastDot = s.lastIndexOf("."), lastComma = s.lastIndexOf(",");
            if (lastDot >= 0 && lastComma >= 0) {
                var dec = lastDot > lastComma ? "." : ",";
                var grp = dec === "." ? /,/g : /\./g;
                s = s.replace(grp, "");
                if (dec === ",") s = s.replace(",", ".");
            } else if (lastComma >= 0) {
                // A single comma is the decimal separator here; a grouping-only
                // value ("1,234") is unusual in an amount field and reading it as
                // 1.234 would understate the cost, so it is rejected instead.
                s = /^-?\d{1,3}(,\d{3})+$/.test(s) ? "NaN" : s.replace(",", ".");
            }
            if (!/^-?\d*(\.\d*)?$/.test(s)) return NaN;
            return parseFloat(s);
        }

        // All five fields are required and the amount must parse to a number
        // greater than zero; the submit button stays inert until then.
        function validateForm() {
            var $btn = $body.find("[data-elc-save]");
            if (!$btn.length) return;

            var v = readForm();
            var amount = parseAmountInput(v.amountText);
            var ok = !!v.distributionCode && v.costElementId > 0 && v.currencyId > 0 &&
                     v.conversionTypeId > 0 && !isNaN(amount) && amount > 0;

            $btn.prop("disabled", !ok || saving);
            var $note = $body.find(".vas_167-formNote");
            if (saving) {
                $note.text(getMsg("VAS_167_Saving"));
            } else {
                $note.text(ok
                    ? (editId !== null ? getMsg("VAS_167_NoteReadyUpdate") : getMsg("VAS_167_NoteReadyAdd"))
                    : getMsg("VAS_167_NoteIncomplete"));
            }
            return ok;
        }

        function showFormError(message) {
            var $err = $body.find(".vas_167-formErr");
            if (!$err.length) { toast(message, true); return; }
            $err.text(message).show();
        }

        // ----------------------------------------------------------------- //
        //  Mutations (always server-side)                                    //
        // ----------------------------------------------------------------- //

        function saveCost() {
            if (saving || !validateForm()) return;
            var v = readForm();

            saving = true;
            validateForm();
            showBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + "VAS_167_PurchaseOrderLandedCost/SaveExpectedCost",
                type: "POST",
                dataType: "json",
                data: {
                    C_ExpectedCost_ID: editId === null ? 0 : editId,
                    C_Order_ID: data.PurchaseOrderId,
                    LandedCostDistribution: v.distributionCode,
                    M_CostElement_ID: v.costElementId,
                    Description: "",
                    Amt: String(parseAmountInput(v.amountText)),
                    C_Currency_ID: v.currencyId,
                    C_ConversionType_ID: v.conversionTypeId
                },
                success: function (raw) {
                    var resp = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                    saving = false;
                    if (resp && resp.Success) {
                        // Re-read so the count, footer and totals stay in step
                        // with what the server actually stored.
                        editId = null;
                        $self.fetchData($self.record_ID);
                        return;
                    }
                    showBusy(false);
                    validateForm();
                    showFormError((resp && resp.Message) ? resp.Message : getMsg("VAS_167_SaveFailed"));
                },
                error: function (err) {
                    console.log(err);
                    saving = false;
                    showBusy(false);
                    validateForm();
                    showFormError(getMsg("VAS_167_SaveFailed"));
                }
            });
        }

        function removeCost(id) {
            if (saving) return;
            if (!window.confirm(getMsg("VAS_167_ConfirmRemove"))) return;

            saving = true;
            showBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + "VAS_167_PurchaseOrderLandedCost/DeleteExpectedCost",
                type: "POST",
                dataType: "json",
                data: { C_ExpectedCost_ID: id },
                success: function (raw) {
                    var resp = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                    saving = false;
                    if (resp && resp.Success) {
                        // The removed entry may have been the one under edit.
                        if (editId === id) editId = null;
                        $self.fetchData($self.record_ID);
                        return;
                    }
                    showBusy(false);
                    toast((resp && resp.Message) ? resp.Message : getMsg("VAS_167_DeleteFailed"), true);
                },
                error: function (err) {
                    console.log(err);
                    saving = false;
                    showBusy(false);
                    toast(getMsg("VAS_167_DeleteFailed"), true);
                }
            });
        }

        // ----------------------------------------------------------------- //
        //  Events                                                           //
        // ----------------------------------------------------------------- //

        // ----------------------------------------------------------------- //
        //  Record navigation                                                 //
        // ----------------------------------------------------------------- //

        // Tables whose record opens in a NAMED window rather than the table's
        // default zoom target — the contract reference opens VAS_ContractMaster
        // and the RFQ opens VAS_RFQ. Any further screen that needs naming belongs
        // here; nothing else has to change.
        // VAMRP_PlanRun is deliberately NOT here: the plan's window belongs to an
        // optional module whose name this panel has no business fixing, so the
        // plan link falls through to the table's own zoom target below — the same
        // way the Purchase Order Overview opens it.
        var WINDOW_NAME_BY_TABLE = {
            "VAS_ContractMaster": "VAS_ContractMaster",
            "C_RfQ":              "VAS_RFQ",
            "C_Project":          "VAS_Project",
            "M_Requisition":      "VAS_Requisition",
            "C_Order":            "VAS_BlanketPurchaseOrder"
        };

        // The same map for records opened as a SALES transaction. C_Order serves
        // both sides — the sales-order reference opens it with IsSOTrx, the
        // blanket order without — so each side names its own window and this map
        // wins when the flag is set. Without it the sales order would open in the
        // Blanket Purchase Order window named above.
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
                    "VAS_167_PurchaseOrderLandedCost/GetWindow_ID", windowName);
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
        // table when it has one, else the table's default zoom target. Either way
        // the window is started with an equal-query on the table's key column
        // (TableName_ID). Degrades to a toast so a click never throws.
        function openRecord(tableName, recordId, isSOTrx) {
            if (!tableName || !recordId || +recordId <= 0 || !window.VIS) return;
            try {
                // A sales-transaction record takes its own window name where the
                // table has one; everything else takes the plain mapping.
                var windowName = (isSOTrx && WINDOW_NAME_BY_TABLE_SOTRX[tableName])
                    ? WINDOW_NAME_BY_TABLE_SOTRX[tableName]
                    : WINDOW_NAME_BY_TABLE[tableName];
                var windowId = resolveWindowIdByName(windowName);

                if (windowId <= 0 &&
                    VIS.ZoomTarget && typeof VIS.ZoomTarget.getZoomAD_Window_ID === "function") {
                    // The 4th arg (IsSOTrx) picks the sales vs purchase window for
                    // a dual-purpose table like C_Order.
                    windowId = VIS.ZoomTarget.getZoomAD_Window_ID(tableName, 0, null, !!isSOTrx) || 0;
                }
                if (windowId > 0 && VIS.viewManager &&
                    typeof VIS.viewManager.startWindow === "function") {
                    var zoomQuery = VIS.Query.prototype.getEqualQuery(tableName + "_ID", +recordId);
                    VIS.viewManager.startWindow(windowId, zoomQuery);
                    return;
                }
            } catch (e) { console.log(e); }
            toast(getMsg("VAS_167_OpenRecord") + " " + tableName + " #" + recordId, true);
        }

        function bindEvents() {
            // Open a linked record (the contract reference) from its header field.
            $root.on("click", "[data-elc-open-table]", function () {
                openRecord($(this).attr("data-elc-open-table"),
                           $(this).attr("data-elc-open-id"),
                           $(this).attr("data-elc-open-sotrx") === "Y");
            });

            // The amount takes digits and a decimal separator and nothing else —
            // anything typed, pasted or dragged into it that could not be part of
            // a number is dropped as it arrives, before validation sees it.
            $root.on("input", '[data-elc-field="elcAmt"]', function () {
                var before = this.value;
                var clean = sanitizeAmount(before);
                if (clean === before) return;
                // Keep the caret where the reader left it, minus whatever was
                // dropped ahead of it.
                var pos = this.selectionStart;
                var head = before.slice(0, pos);
                var shift = head.length - sanitizeAmount(head).length;
                this.value = clean;
                try { this.setSelectionRange(pos - shift, pos - shift); } catch (e) { }
            });

            // Live validation while the reader fills the form.
            $root.on("input", "[data-elc-field]", validateForm);
            $root.on("change", "[data-elc-field]", validateForm);

            $root.on("click", "[data-elc-save]", function () { saveCost(); });

            $root.on("click", "[data-elc-cancel]", function () {
                editId = null;
                render();
            });

            // Editing loads the entry into the same form — no separate dialog.
            $root.on("click", "[data-elc-edit]", function () {
                editId = parseInt($(this).attr("data-elc-edit"), 10);
                render();
                var $first = $body.find('[data-elc-field="elcDist"]');
                if ($first.length) $first.focus();
            });

            $root.on("click", "[data-elc-remove]", function () {
                removeCost(parseInt($(this).attr("data-elc-remove"), 10));
            });

            // Generated-lines drawer, one per entry.
            $root.on("click", "[data-elc-lines]", function () {
                var id = parseInt($(this).attr("data-elc-lines"), 10);
                var open = !isLinesOpen(id);
                linesOpen[id] = open;
                var $btn = $(this);
                $btn.toggleClass("vas_167-is-open", open)
                    .attr("aria-expanded", open ? "true" : "false")
                    .attr("aria-label", open ? getMsg("VAS_167_HideLines") : getMsg("VAS_167_ShowLines"))
                    .attr("title", open ? getMsg("VAS_167_HideLines") : getMsg("VAS_167_ShowLines"));
                $body.find('[data-elc-linesfor="' + id + '"]').toggleClass("vas_167-is-closed", !open);
            });
        }

        function toast(message, isError) {
            var $t = $('<div class="vas_167-toast"></div>')
                .addClass(isError ? "vas_167-err" : "vas_167-ok").text(message);
            $root.append($t);
            setTimeout(function () { $t.addClass("vas_167-show"); }, 10);
            setTimeout(function () {
                $t.removeClass("vas_167-show");
                setTimeout(function () { $t.remove(); }, 300);
            }, 3600);
        }

        // ----------------------------------------------------------------- //
        //  Helpers                                                          //
        // ----------------------------------------------------------------- //

        function findCost(id) {
            var costs = data.ExpectedCosts || [];
            for (var i = 0; i < costs.length; i++) {
                if (costs[i].ExpectedCostId === id) return costs[i];
            }
            return null;
        }

        // Closed until asked for: the drawer opens only once the reader clicks the
        // entry's Lines button, which is what shows that entry's distribution
        // detail for its own distribution method.
        function isLinesOpen(id) {
            return linesOpen[id] === true;
        }

        // Chip colour per C_LandedCostDistribution code. I (Import Value) is no
        // longer offered but still appears on entries created elsewhere.
        function distTone(code) {
            switch (code) {
                case "Q": return "qty";
                case "C":
                case "I": return "value";
                case "L": return "equal";
                case "V": return "volume";
                case "W": return "weight";
                default:  return "other";
            }
        }

        function docPrecision() {
            var p = data && data.DocumentCurrencyPrecision;
            return (p >= 0) ? p : 2;
        }

        // Precision of the currency the entry — and therefore its generated
        // lines — is denominated in.
        function enteredPrecision(c) {
            var p = c && c.EnteredCurrencyPrecision;
            return (p >= 0) ? p : 2;
        }

        function iconButton(icon, kind, label) {
            var $b = $('<button type="button" class="vas_167-icBtn"></button>')
                .addClass("vas_167-is-" + kind)
                .attr("title", label)
                .attr("aria-label", label);
            $b.append(svgIcon(icon));
            return $b;
        }

        var SVG_ICONS = {
            pencil: '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 20h9"/><path d="M16.5 3.5a2.1 2.1 0 0 1 3 3L7 19l-4 1 1-4Z"/></svg>',
            trash: '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M3 6h18M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2m3 0v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6"/></svg>',
            plus: '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 5v14M5 12h14"/></svg>',
            check: '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><polyline points="20 6 9 17 4 12"/></svg>',
            checkCircle: '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="9"/><path d="M9 12l2 2 4-4"/></svg>',
            chevDown: '<svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><polyline points="6 9 12 15 18 9"/></svg>',
            chevLeft: '<svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><polyline points="15 18 9 12 15 6"/></svg>',
            chevRight: '<svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><polyline points="9 18 15 12 9 6"/></svg>',
            info: '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"/><path d="M12 16v-4M12 8h.01"/></svg>',
            alert: '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M10.29 3.86 1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0Z"/><path d="M12 9v4M12 17h.01"/></svg>',
            coins: '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="8" cy="8" r="5"/><path d="M14.5 4.2a5 5 0 0 1 0 15.6"/><path d="M7 18.7a5 5 0 0 0 6 0"/></svg>',
            user: '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"/><circle cx="12" cy="7" r="4"/></svg>'
        };

        function svgIcon(name) {
            var $wrap = $('<span class="vas_167-ic"></span>');
            $wrap[0].innerHTML = SVG_ICONS[name] || "";
            return $wrap;
        }

        function formatNumber(value, precision) {
            var p = (precision >= 0) ? precision : 2;
            return (+value || 0).toLocaleString(window.navigator.language, {
                minimumFractionDigits: p,
                maximumFractionDigits: p
            });
        }

        // Document-currency amount: symbol when the currency has one, else the
        // ISO code, so nothing is ever labelled with a hard-coded currency.
        function money(value) {
            var cur = (data && (data.DocumentCurrencySymbol || data.DocumentCurrencyCode)) || "";
            var formatted = formatNumber(value, docPrecision());
            return cur ? cur + " " + formatted : formatted;
        }

        // Date-only value: keep the stored calendar day, never shift it by zone.
        function formatDate(value) {
            if (!value) return "";
            var s = String(value);
            if (/^\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}/.test(s)) {
                s = s.replace(" ", "T").replace(/(z|[+-]\d{2}:?\d{2})$/i, "");
            }
            var d = new Date(s);
            if (isNaN(d.getTime())) return "";
            try {
                return d.toLocaleDateString(window.navigator.language, {
                    year: "numeric", month: "short", day: "2-digit"
                });
            } catch (e) {
                return d.toDateString();
            }
        }

        this.getRoot = function () {
            return $root;
        };
    };

    VAS.VAS_167_PurchaseOrderLandedCost.prototype.startPanel = function (windowNo, curTab) {
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
    VAS.VAS_167_PurchaseOrderLandedCost.prototype.refreshPanelData = function (recordID, selectedRow) {
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
        this.fetchData(recordID);
    };

    /* Set width as per window width */
    VAS.VAS_167_PurchaseOrderLandedCost.prototype.sizeChanged = function (width) {
        this.panelWidth = width;
    };

    /* Release variables from memory */
    VAS.VAS_167_PurchaseOrderLandedCost.prototype.dispose = function () {
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
