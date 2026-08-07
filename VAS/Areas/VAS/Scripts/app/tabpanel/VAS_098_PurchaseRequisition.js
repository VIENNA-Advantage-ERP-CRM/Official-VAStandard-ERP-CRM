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
        var $body;
        var $emptyState;
        var data = null;

        // Line items page client-side (the whole set arrives in one payload); the
        // page resets whenever a different record is loaded.
        var LINES_PER_PAGE = 10;
        var linesPage = 0;
        var activityPage = 0;   // current Activity page (0-based, like linesPage)

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
        // Tones match the colours the requisition window itself uses for the
        // PriorityRule field: urgent red, high orange, medium blue, low green,
        // minor grey. Urgent and High used to share one tone, and Low shared grey
        // with Minor, so the badge disagreed with the screen.
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

        this.fetchData = function (recordID) {
            showBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + "VAS_098_PurchaseRequisition/GetRequisitionOverview",
                type: "GET",
                dataType: "json",
                data: { M_Requisition_ID: recordID },
                success: function (raw) {
                    var parsed = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                    data = parsed;
                    linesPage = 0;
                    activityPage = 0;
                    render();
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
            linesPage = 0;
            activityPage = 0;
            render();
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
            renderConvert();
            renderStats();
            renderProgress();
            renderLower();
        }

        // ----------------------------------------------------------------- //
        //  Helpers                                                           //
        // ----------------------------------------------------------------- //

        // Localised label lookup. All on-screen text is seeded in AD_Message as
        // VAS_098_<key>. VIS.Msg returns the key itself when it is not seeded, so
        // an optional English fallback keeps a raw "VAS_098_Foo" off the screen
        // until the message is added to the dictionary.
        function msg(key, fallback) {
            var full = "VAS_098_" + key;
            try {
                var m = VIS.Msg.getMsg(full);
                if (m && m !== full) return m;
            } catch (e) { }
            return (fallback !== null && fallback !== undefined) ? fallback : full;
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
            if (data.SourceWarehouseName) {
                // origin / procurement-type chip (design shows a chip here)
                $pills.append(tag(procurementType(), "vas_098-mwo"));
            }
            $pills.append(tag(st.label, st.tone));
            // Posting status of the record, beside the document status.
            var pst = postedMeta();
            $pills.append(tag(pst.label, pst.tone));
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
            // Reference — the purchase order raised from this requisition. N/A
            // until it has been converted.
            $r.append(headerField(msg("Reference", "Reference"), referenceText()));
           
           
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

        // The purchase order this requisition produced. When it produced more than
        // one, the first is named and the rest counted.
        function referenceText() {
            if (!data.OrderDocumentNo) return msg("NA");
            var count = +data.OrderCount || 0;
            return count > 1
                ? data.OrderDocumentNo + " +" + (count - 1) + " " + msg("More", "more")
                : data.OrderDocumentNo;
        }

        // Labelled field block (uppercase caption + value) for the right column.
        function headerField(label, value) {
            var $f = $('<div class="vas_098-hdrField"></div>');
            $f.append($('<div class="vas_098-fLabel"></div>').text(label));
            $f.append($('<div class="vas_098-fVal"></div>').text(value));
            return $f;
        }

        // ------------------------ Convert strip -------------------------- //

        function renderConvert() {
            var canConvert = data.StatusCode === "CO" && !data.IsConverted;

            var $strip = $('<div class="vas_098-convert"></div>');

            var noteText, noteOk = false;
            if (data.IsConverted) {
                // "Converted" — the note used to read "Already converted".
                noteText = msg("Converted");
                noteOk = true;
            } else if (canConvert) {
                noteText = msg("ReadyToConvertNote");
                noteOk = true;
            } else {
                noteText = msg("ConversionAvailable");
            }

            var $note = $('<span class="vas_098-cvnote"></span>');
            if (noteOk) $note.append($('<span class="vas_098-ok"></span>').append(svgIcon("check")));
            $note.append(document.createTextNode(noteText));
            $strip.append($note);

            // Document actions, each run through the platform's process engine via
            // the controller. Preconditions are per-action rather than one shared
            // flag: raising an RFQ is still legitimate after the requisition has
            // been converted to a PO, so it no longer greys out with the others.
            var isCompleted = data.StatusCode === "CO" || data.StatusCode === "CL";
            var isClosedOff = data.StatusCode === "VO" || data.StatusCode === "RE";
            var canAct = isCompleted && !isClosedOff;

            // Button labels name the target document only — the "Convert to" /
            // "Create" verb has been dropped from each. New message keys are used
            // so the old seeded ConvertTo* / CreateRFQ text does not resurface.
            var $actions = $('<div class="vas_098-cvactions"></div>');
            $actions.append(convertBtn(msg("MaterialTransfer", "Material Transfer"), "transfer", "vas_098-primary",
                canConvert, "ConvertToMaterialTransfer",
                msg("ConfirmMaterialTransfer",
                    "Create a material transfer from this requisition?")));
            $actions.append(convertBtn(msg("RFQ", "RFQ"), "rfq", "vas_098-secondary",
                canAct, "CreateRFQ",
                msg("ConfirmCreateRFQ",
                    "Create an RFQ from this requisition?")));
            $actions.append(convertBtn(msg("PurchaseOrder", "Purchase Order"), "external", "vas_098-secondary",
                canConvert, "ConvertToPurchaseOrder",
                msg("ConfirmConvertToPO",
                    "Create the purchase order(s) for this requisition?")));
            $strip.append($actions);

            $body.append($strip);
        }

        // Action button: confirms, POSTs to the controller, then refreshes the
        // panel so the status, progress stepper and button states reflect the
        // result. Disabled buttons carry a reason as a tooltip.
        function convertBtn(label, icon, variant, enabled, endpoint, confirmText) {
            var $b = $('<button type="button" class="vas_098-btn"></button>').addClass(variant);
            $b.append(svgIcon(icon));
            $b.append(document.createTextNode(label));

            if (!enabled) {
                $b.prop("disabled", true);
                $b.attr("title", data.IsConverted
                    ? msg("AlreadyConverted")
                    : msg("ConversionAvailable"));
                return $b;
            }

            $b.on("click", function () {
                if (!confirm(confirmText)) return;
                runAction($b, endpoint, label);
            });
            return $b;
        }

        // POSTs a convert action and refreshes on success. Guards against
        // double-clicks while the request is in flight.
        function runAction($btn, endpoint, label) {
            if ($btn.prop("disabled")) return;
            $btn.prop("disabled", true);
            showBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + "VAS_098_PurchaseRequisition/" + endpoint,
                type: "POST",
                dataType: "json",
                data: { M_Requisition_ID: $self.record_ID },
                success: function (raw) {
                    var res = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                    if (res && res.success) {
                        toast(res.message || label, false);
                        // Re-fetch: conversion changes the progress stepper, the
                        // convert note and which actions still apply.
                        $self.fetchData($self.record_ID);
                    } else {
                        $btn.prop("disabled", false);
                        showBusy(false);
                        toast((res && (res.error || res.message)) ||
                              msg("ActionFailed", "The action could not be completed."), true);
                    }
                },
                error: function () {
                    $btn.prop("disabled", false);
                    showBusy(false);
                    toast(msg("ActionFailed", "The action could not be completed."), true);
                }
            });
        }

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

            // Source availability — on-hand stock at the source warehouse. Shows a
            // real quantity (0 included) whenever a source warehouse is configured;
            // only external procurement, which has no source warehouse, reads N/A.
            var $s4 = statCard("vas_098-a-amber", msg("SourceAvailability"));
            if (data.HasSourceData) {
                $s4.append($('<div class="vas_098-sval"></div>')
                    .text(formatNumber(data.SourceStockOnHand || 0)));
                $s4.append(statSub(
                    msg("OnHandAtSource", "on hand") +
                    (data.SourceWarehouseName ? " · " + data.SourceWarehouseName : "") +
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
            var submitted  = data.Processed || s === "IP" || s === "AP" || s === "CO" || s === "CL";
            var completed  = s === "CO" || s === "CL" || data.IsConverted;
            var converted  = data.IsConverted;
            // In fulfilment once a purchase order raised from this requisition has
            // been completed — not from a per-line ordered quantity.
            var fulfilment = data.HasOrdered;
            // Closed when the requisition itself is closed, or when every purchase
            // order raised from it has closed.
            var closed     = data.IsClosed;

            return [
                { key: "vas_098-c1", label: msg("Drafted"),      done: true,       sub: formatDateShort(data.Created) },
                { key: "vas_098-c2", label: msg("Submitted"),    done: submitted,  sub: formatDateShort(data.CompletedDate) },
                { key: "vas_098-c3", label: msg("Completed"),    done: completed,  sub: formatDateShort(data.CompletedDate) },
                { key: "vas_098-c4", label: msg("Converted"),    done: converted,  sub: formatDateShort(data.ConvertedDate) },
                { key: "vas_098-c5", label: msg("InFulfilment"), done: fulfilment, sub: formatDateShort(data.FulfilmentDate) },
                { key: "vas_098-c6", label: msg("Closed"),       done: closed,     sub: formatDateShort(data.ClosedDate) }
            ];
        }

        function renderProgress() {
            var stages = progressStages();

            // Monotonic reach: a stage is "reached" if it or any later stage is done.
            var reached = [];
            for (var i = 0; i < stages.length; i++) {
                var any = false;
                for (var j = i; j < stages.length; j++) { if (stages[j].done) { any = true; break; } }
                reached.push(any);
            }
            var current = 1;
            for (var k = 0; k < reached.length; k++) { if (reached[k]) current = k + 1; }

            var st = statusMeta();
            var $sh = $('<div class="vas_098-sechead"></div>');
            $sh.append($('<h2></h2>').text(msg("RequisitionProgress")));
            $sh.append($('<span class="vas_098-secright"></span>').text(
                msg("Stage") + " " + current + " " + msg("Of") + " " + stages.length + " · " + st.label));
            $body.append($sh);

            var $stepper = $('<div class="vas_098-stepper"></div>');
            for (var s = 0; s < stages.length; s++) {
                var stg = stages[s];
                var stateCls, sub, showCheck;
                if (s + 1 < current) { stateCls = "vas_098-done";    showCheck = true;  sub = stg.sub || ""; }
                else if (s + 1 === current) { stateCls = "vas_098-active"; showCheck = false; sub = activeSub(stg, current); }
                else { stateCls = "vas_098-pending"; showCheck = false; sub = msg("Pending"); }
                $stepper.append(stepEntry(s + 1, stg, stateCls, showCheck, sub));
            }
            $body.append($stepper);
        }

        function activeSub(stg, current) {
            if (stg.key === "vas_098-c3") return msg("ReadyToConvert");
            if (stg.sub) return stg.sub;
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

        // Items, then Activity, then Notes — each a headed section stacked down the
        // panel. These used to be three tabs; Activity and Notes now sit at the
        // bottom where they are visible without a click.
        function renderLower() {
            var lines = (data.Lines) || [];
            var activity = (data.Activity) || [];

            sectionHead(msg("Items"), lines.length + " " + msg("Lines"));
            $body.append(renderItemsPanel(lines));

            sectionHead(msg("Activity"), activity.length + " " + msg("Updates", "updates"));
            $body.append(renderActivityPanel(activity));

            sectionHead(msg("Notes"), "");
            $body.append(renderNotesPanel());
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
            $head.append($('<span class="vas_098-ta-r"></span>').text(msg("SourceStock")));
            $head.append($('<span class="vas_098-ta-r"></span>').text(msg("UnitCost")));
            $head.append($('<span class="vas_098-ta-r"></span>').text(msg("EstTotal")));
            $items.append($head);

            if (!lines.length) {
                $items.append($('<div class="vas_098-itempty"></div>').text(msg("NoLineItems")));
                $panel.append($items);
                return $panel;
            }

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
            // Product search key, shown without the former "SKU" prefix.
            if (ln.ProductValue) {
                $item.append($('<div class="vas_098-itsku"></div>').text(ln.ProductValue));
            } else if (ln.Description) {
                $item.append($('<div class="vas_098-itsku"></div>').text(ln.Description));
            }
            // Attribute Set Instance (lot / serial / size ...), only when the line
            // carries a real instance — a blank or "--" placeholder is not shown.
            var asi = (ln.AttributeSetInstance || "").trim();
            if (asi && asi !== "--" && asi !== "-") {
                $item.append($('<div class="vas_098-itattr"></div>').text(asi).attr("title", asi));
            }
            $r.append($item);

            // Unit of measure (replaced the product category column).
            var $uom = $('<span></span>');
            if (ln.UOMName) $uom.append($('<span class="vas_098-uom"></span>').text(ln.UOMName));
            else $uom.append($('<span class="vas_098-na"></span>').text(msg("NA")));
            $r.append($uom);

            $r.append($('<span class="vas_098-ta-c"></span>').text(formatNumber(ln.RequestedQty, ln.UOMPrecision)));

            $r.append(sourceCell(ln));

            $r.append($('<span class="vas_098-ta-r"></span>').text(money(ln.UnitPrice)));
            $r.append($('<span class="vas_098-ta-r"></span>').text(money(ln.LineAmount)));
            return $r;
        }

        // On-hand stock for this line's product at the source warehouse, against
        // the requested quantity. N/A only when the requisition has no source
        // warehouse at all — with one configured, no stock reads as 0, not N/A.
        function sourceCell(ln) {
            var $c = $('<span class="vas_098-ta-r"></span>');
            if (!ln.HasSourceData) {
                $c.append($('<span class="vas_098-na"></span>').text(msg("NA")));
                return $c;
            }
            var req = +ln.RequestedQty || 0;
            var onHand = +ln.SourceQtyOnHand || 0;
            var pct = req > 0 ? Math.round((onHand / req) * 100) : (onHand > 0 ? 100 : 0);
            var cls = (req > 0 && onHand >= req) ? "vas_098-full" : "vas_098-short";
            var $src = $('<span class="vas_098-src"></span>').addClass(cls);
            $src.attr("title", msg("OnHandAtSource", "on hand") +
                (data.SourceWarehouseName ? " · " + data.SourceWarehouseName : ""));
            var $bar = $('<span class="vas_098-bar"><i></i></span>');
            $bar.find("i").css("width", Math.max(0, Math.min(100, pct)) + "%");
            $src.append($bar);
            $src.append(document.createTextNode(
                formatNumber(onHand, ln.UOMPrecision) + "/" + formatNumber(req, ln.UOMPrecision)));
            $c.append($src);
            return $c;
        }

        function itemsFooter() {
            var $f = $('<div class="vas_098-itfoot"></div>');
            // Budget set for the requisition (VAS_AvailableBudget, written by the
            // "Calculate Budget" process) sits where the subtotal used to, so the
            // estimate can be read straight against it. N/A when no budget has been
            // calculated for this requisition.
            $f.append(footBit(msg("Budget", "Budget"),
                (+data.AvailableBudget || 0) > 0 ? money(data.AvailableBudget) : msg("NA"), false));
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
            // Downstream lifecycle documents.
            po:          { cls: "vas_098-po",  key: "ActPO",          fallback: "PO"  },
            grn:         { cls: "vas_098-grn", key: "ActGRN",         fallback: "GRN" },
            grncomplete: { cls: "vas_098-grn", key: "ActGRNComplete", fallback: "GRN" }
        };

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
                for (var i = start; i < end; i++) $card.append(activityRow(activity[i]));

                buildPager($pager, activityPage, pageCount, activity.length, start, end,
                    function (p) { activityPage = p; paintPage(); });
            }

            paintPage();
            return $panel;
        }

        function activityRow(a) {
            var meta = ACT_BADGE[a.Type] || ACT_BADGE.comment;
            var $row = $('<div class="vas_098-actrow"></div>');

            $row.append($('<span class="vas_098-actbadge"></span>')
                .addClass(meta.cls).text(msg(meta.key, meta.fallback)));

            var $main = $('<div class="vas_098-actmain"></div>');
            var $wrap = $('<div class="vas_098-atwrap"></div>');
            $wrap.append($('<span class="vas_098-at"></span>').text(activityText(a)));
            $wrap.append($('<span class="vas_098-attime"></span>').text(formatDateTime(a.Created)));
            $main.append($wrap);
            $row.append($main);
            return $row;
        }

        function activityText(a) {
            if (a.Type === "create")
                return msg("RequisitionCreated") + (a.Text ? " " + msg("By") + " " + a.Text : "");
            if (a.Type === "status")
                return msg("RequisitionMarked") + " " + statusMeta().label + (a.Text ? " " + msg("By") + " " + a.Text : "");

            // Downstream documents name themselves, so the row reads
            // "PO Created — PO-000123 by <user>".
            if (a.Type === "po" || a.Type === "grn" || a.Type === "grncomplete") {
                var label;
                if (a.Type === "po") label = msg("POCreated", "PO Created");
                else if (a.Type === "grn") label = msg("GRNCreated", "GRN Created");
                else label = msg("GRNCompleted", "GRN Completed");

                if (a.DocumentNo) label += " — " + a.DocumentNo;
                if (a.Text) label += " " + msg("By") + " " + a.Text;
                return label;
            }

            return a.Text || msg("ActComment");
        }

        // ---- Notes ---- //

        function renderNotesPanel() {
            var $panel = $('<div class="vas_098-lowersec"></div>');
            var $card = $('<div class="vas_098-panelcard vas_098-notescard"></div>');

            var $notes = $('<div class="vas_098-notesbody"></div>');
            var text = data.Description;
            if (text) {
                var paras = String(text).split(/\r?\n+/);
                for (var i = 0; i < paras.length; i++) {
                    var t = paras[i].trim();
                    if (t) $notes.append($('<p></p>').text(t));
                }
            }
            if (!$notes.children().length) $notes.append($('<p class="vas_098-na"></p>').text(msg("NoNotes")));
            $card.append($notes);

            $panel.append($card);
            return $panel;
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
            note:      '<svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><path d="M14 2v6h6M9 13h6M9 17h4"/></svg>'
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

        function formatNumber(value, precision) {
            var p = (precision >= 0) ? precision : 0;
            return (+value || 0).toLocaleString(window.navigator.language, {
                minimumFractionDigits: p, maximumFractionDigits: p
            });
        }

        function stripTime(d) {
            return new Date(d.getFullYear(), d.getMonth(), d.getDate()).getTime();
        }

        // Parses a timestamp as it came off the server, as WALL-CLOCK time.
        //
        // Created / Updated are stored in server local time. Depending on how the
        // DateTime is tagged on the way out, the JSON can carry a "Z" or an
        // offset, and `new Date(...)` then converts it into the browser's timezone
        // — so the panel showed a creation time hours away from the one the
        // requisition window shows. Reading the date and time components straight
        // out of the string and building a local Date keeps the two in agreement
        // regardless of how the value was tagged.
        function parseServerDate(value) {
            if (!value) return null;
            if (value instanceof Date) return isNaN(value.getTime()) ? null : value;

            var m = /^(\d{4})-(\d{2})-(\d{2})(?:[T ](\d{2}):(\d{2})(?::(\d{2}))?)?/
                .exec(String(value));
            if (m) {
                return new Date(+m[1], +m[2] - 1, +m[3],
                                +(m[4] || 0), +(m[5] || 0), +(m[6] || 0));
            }

            // Anything else (e.g. an epoch value) falls back to native parsing.
            var d = new Date(value);
            return isNaN(d.getTime()) ? null : d;
        }

        function formatDate(value) {
            var d = parseServerDate(value);
            if (!d) return "";
            try {
                return d.toLocaleDateString(window.navigator.language,
                    { year: "numeric", month: "short", day: "numeric" });
            } catch (e) { return d.toDateString(); }
        }

        function formatDateShort(value) {
            var d = parseServerDate(value);
            if (!d) return "";
            try {
                return d.toLocaleDateString(window.navigator.language, { month: "short", day: "numeric" });
            } catch (e) { return ""; }
        }

        function formatDateTime(value) {
            var d = parseServerDate(value);
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
        this.fetchData(recordID);
    };

    /* Set width as per window width */
    VAS.VAS_098_PurchaseRequisition.prototype.sizeChanged = function (width) {
        this.panelWidth = width;
    };

    /* Release variables from memory */
    VAS.VAS_098_PurchaseRequisition.prototype.dispose = function () {
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
