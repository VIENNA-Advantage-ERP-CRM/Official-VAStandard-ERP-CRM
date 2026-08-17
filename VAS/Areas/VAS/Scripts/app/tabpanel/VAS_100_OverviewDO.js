/************************************************************
 * Module Name    : VAS
 * Purpose        : Delivery Order (DO) Overview tab panel. Renders a
 *                  review-oriented overview of the selected delivery order
 *                  (M_InOut, IsSOTrx = 'Y'): header identity + customer /
 *                  dispatch details card, a four-card KPI snapshot (delivery
 *                  value, lines, delivered qty, linked sales order), a compact
 *                  fulfilment lifecycle strip (Drafted -> Picked/Packed ->
 *                  In Transit -> Completed) driven by DocStatus + Processed,
 *                  the delivery line table and visual action buttons
 *                  (Print / Complete Delivery Order / Raise Invoice — the last
 *                  disabled until the DO is completed). Data is fetched from
 *                  VAS_100_OverviewDO/GetDOOverview. All on-screen strings are
 *                  resolved through VIS.Msg.getMsg("VAS_100_...").
 * Chronological development:
 *   VAI163   2026-07-06  Created
 *   VAI163   2026-08-05  Class prefix renamed MPC-vasdo- -> vas_100- so the panel's
 *                          styles cannot collide with another panel's.
 *   VAI163   2026-08-05  New Record / Copy Record now empty the panel instead
 *                        of leaving the previously selected record on screen.
 *                        Both refreshPanelData and a new data-status listener
 *                        ask isTabInserting(), which reads GridTab.gridTable
 *                        .getIsInserting() — the flag GridTable.dataNew() raises
 *                        for both actions. The record id cannot answer it: a
 *                        copied row carries the source record's key until saved.
 *                        Ported from VAS_092.
 *   VAI163   2026-08-07  Emits the vas_100-prefixed modifier classes the
 *                        stylesheet now uses, the runtime-built ones included
 *                        ("vas_100-tone-" + tone).
 *   VAI163   2026-08-13  Header details card reorganised: the left column is now
 *                        purely the CUSTOMER block (name, location, first name,
 *                        e-mail, address) and every dispatch field — packages,
 *                        transport doc, vehicle, tracking and the new shipping
 *                        method / shipper — sits in the right column. Low and
 *                        Minor priorities now render a green (success) badge.
 *                        New labels resolve through msg(key, fallback) so a
 *                        tenant without the AD_Message rows shows English text
 *                        instead of the raw key.
 *   VAI163   2026-08-13  - New Reference section (renderReference), the VAS_092
 *                          "Generated From" chip strip applied to a delivery
 *                          order: the sales order it was raised from, its
 *                          project, the DO it reverses and the RMA, each chip
 *                          opening the source record through openRecord().
 *                          "Manual" shows when the DO has no origin at all.
 *                        - Sales Order and Movement Date dropped from the header
 *                          details card, and Movement Date from the title strip
 *                          subtitle: the sales order is now a Reference chip and
 *                          the movement date already leads the subtitle's
 *                          replacement.
 *                        - Lifecycle section retitled "Delivery Order Timeline",
 *                          and its Drafted stage reports the record's creation
 *                          date instead of a bare "Done".
 *                        - Print / Complete DO / Raise Invoice buttons removed —
 *                          they were presentational only and duplicated the host
 *                          window's toolbar.
 *   VAI163   2026-08-13  - New Notes section carrying the delivery order's own
 *                          entered description (M_InOut.Description). It renders
 *                          only when there is something to show.
 *                        - Line sub-line: the "SKU" caption in front of the
 *                          product code is gone, the attribute set instance the
 *                          line was delivered against is shown, and the product
 *                          name and locator each carry their full text as a
 *                          tooltip (both clip to one line).
 *                        - The lines table pages at 25 rows (ROWS_PER_PAGE),
 *                          reusing the VAS_106 pager. The totals footer still
 *                          covers the WHOLE delivery, never the page.
 *                        - "Line Items" / "items" now read "Lines" / "lines",
 *                          and the first column header "Item" reads "Line".
 *   VAI163   2026-08-14  Lines:
 *                        - The Product / Service cell reads down in FOUR lines —
 *                          product name, its attributes (only when it has any),
 *                          product code + locator, and Drop Shipment: Yes / No
 *                          (model side). They were one run-together sub-line
 *                          ("code · attribute · locator"), which put three
 *                          unrelated facts behind two middle dots and truncated
 *                          the lot out of sight on any line long enough to matter.
 *                        - Delivery value is the sum of the LINE values (model
 *                          side), and its snapshot card is drawn only once the
 *                          delivery has lines: the figure is derived from them,
 *                          so on a delivery with none it can only be zero, and
 *                          "0.00" reads as a priced document rather than as one
 *                          not yet entered.
 *                        New sections:
 *                        - Documents — the customer invoices raised from this
 *                          delivery, the shipment confirmations recorded for it
 *                          and the customer receipts allocated to those invoices,
 *                          each row opening its document through openRecord().
 *                          The GRN overview's section, same layout and behaviour.
 *                        - Activity — the VAS_092 Purchase Order feed applied to
 *                          a delivery order: created, the document lifecycle one
 *                          row per completed workflow node, one "updated" row per
 *                          FIELD edited on the header AND its lines (headlined
 *                          "Updated <field>", with the line beneath it), the chat
 *                          notes, and the e-mails with every To / Cc / Bcc
 *                          address in full and the body on click. Pages at 15
 *                          (ACTIVITY_PER_PAGE) like every other panel.
 *                        - Activity timestamps render in the viewer's own zone
 *                          (parseStamp / formatDateTime): the DB stores them in
 *                          UTC and the server emits no designator, so the browser
 *                          would otherwise print the stored UTC clock.
 *   VAI163   2026-08-14  Quality confirmation, ported from the VAS_099 GRN panel:
 *                        - The snapshot gains a Confirmation Check card, reading
 *                          Applicable / Non-Applicable off the document type's
 *                          IsShipConfirm flag with the count of lines carrying QA
 *                          parameters beneath it, and — only on a delivery that
 *                          gets confirmed at all — Accepted Quantity
 *                          (M_InOutLineConfirm.ConfirmedQty) and Difference
 *                          Quantity (DifferenceQty) with Scrapped Quantity
 *                          (ScrappedQty) under the latter. With those two in play
 *                          the grid runs three-up so the set forms even rows.
 *                        - A Quality column marks each line with a tick or a
 *                          cross, drawn only when at least one line of the
 *                          delivery has QA parameters: on a delivery where none
 *                          does, a column of crosses is a statement about nothing.
 *                          The word travels as the cell's tooltip and aria-label.
 *                        - A line whose product HAS parameters carries an expand
 *                          caret beside the product name and opens them in a
 *                          drawer below the row (Parameter, To Verify, Acceptable,
 *                          Actual Value, QA Date, Status), collapsed by default; a
 *                          line without parameters carries no caret. Open drawers
 *                          are keyed by M_InOutLine_ID so they survive a pager
 *                          repaint, and reset with the record.
 *   VAI163   2026-08-14  Header and shipment:
 *                        - New Shipment Details section under the lines, in the
 *                          header card's own layout: Shipper, Transport Document
 *                          No., Tracking No., Vehicle Name, Vehicle Registration
 *                          Number and Packages. Drawn only when the delivery
 *                          records at least one of them, and each field is left
 *                          out rather than printed against a dash — a card of six
 *                          dashes says nothing. Packages counts as absent at zero:
 *                          an unpacked delivery should not report "0 packages".
 *                        - The header's right column drops all of those fields
 *                          (they crowded the customer's card with a different
 *                          subject) and keeps Warehouse, Shipping Method and the
 *                          customer's Location.
 *                        - The customer block follows the Sales Order overview:
 *                          the name, then Bill to and Ship to each on their OWN
 *                          labelled line, then the contact as a row of icon-led
 *                          bits (name / phone / e-mail). It used to show one
 *                          unlabelled address — which never said WHICH address it
 *                          was — and split the contact across three labelled
 *                          field boxes, reading as three facts rather than as one
 *                          person.
 *                        - A Posted badge, shown ONLY once the document has been
 *                          posted (M_InOut.Posted). An unposted delivery carries
 *                          no badge rather than one reading "Not Posted".
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

    VAS.VAS_100_OverviewDO = function () {
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

        // The delivery lines page client-side (the whole set arrives in one
        // payload). 25 rows a page, matching the other overview panels: the pager
        // only appears once the table actually exceeds that. Reset with the
        // record, so a new selection always opens on the first page.
        var ROWS_PER_PAGE = 25;
        var linesPage = 0;
        // Which delivery lines have their quality-parameter drawer open, keyed by
        // M_InOutLine_ID. Survives a pager repaint — paging away from a line and
        // back keeps what the reader opened — and is cleared per record.
        var lineQpOpen = {};

        this.init = function () {
            $root = $('<div class="vas_100-root"></div>');
            $body = $('<div class="vas_100-body"></div>');
            $emptyState = $('<div class="vas_100-empty" style="display:none;"></div>');
            $emptyState.text(VIS.Msg.getMsg("VAS_100_NoData"));
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

        this.fetchData = function (recordID) {
            // A different record starts at the top of its own line list and its
            // own activity feed, with every quality drawer shut.
            linesPage = 0;
            activityPage = 0;
            lineQpOpen = {};
            showBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + "VAS_100_OverviewDO/GetDOOverview",
                type: "GET",
                dataType: "json",
                data: { M_InOut_ID: recordID },
                success: function (raw) {
                    var parsed = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                    data = parsed;
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
            lineQpOpen = {};
            render();
        };

        function render() {
            $body.empty();

            if (!data || !data.M_InOut_ID) {
                $body.hide();
                $emptyState.show();
                return;
            }

            $emptyState.hide();
            $body.show();

            // Body is a flat stack of self-contained sections.
            renderHeader();
            renderReference();
            renderSnapshot();
            renderLifecycle();
            renderLines();
            renderShipmentDetails();
            renderDocuments();
            renderNotes();
            // Activity comes LAST — it is the longest section and it pages, so
            // anything under it would be pushed off the bottom of the panel.
            renderActivity();
        }

        // ----------------------------------------------------------------- //
        //  Small builders                                                    //
        // ----------------------------------------------------------------- //

        function section(title, opts, $parent) {
            opts = opts || {};
            var $sec = $('<section class="vas_100-sec"></section>');
            var $head = $('<div class="vas_100-secHead"></div>');
            $head.append($('<h2 class="vas_100-secTitle"></h2>').text(title));
            if (opts.summary) {
                $head.append($('<span class="vas_100-secSummary"></span>').text(opts.summary));
            }
            $sec.append($head);
            ($parent || $body).append($sec);
            return $sec;
        }

        // VIS.Msg.getMsg hands back the key itself when the tenant has no
        // AD_Message row for it. The keys added after the panel shipped are not
        // seeded everywhere, so they go through here and fall back to English
        // rather than painting "VAS_100_ShippingMethod" on the card.
        function msg(key, fallback) {
            try {
                var m = VIS.Msg.getMsg(key);
                if (m && m !== key) return m;
            } catch (e) { }
            return fallback;
        }

        // Returns "N/A" for blank values so the layout never shows an empty cell.
        function na(value) {
            return (value === null || value === undefined || String(value).trim() === "")
                ? VIS.Msg.getMsg("VAS_100_NA")
                : value;
        }

        // The currency token: prefer the linked order's symbol / ISO, else INR.
        function currencyToken() {
            return (data && (data.CurSymbol || data.ISO_Code)) || "₹";
        }

        // True once the delivery has lines entered against it. The delivery VALUE
        // is derived from those lines, so everything that reports it is gated on
        // this rather than on the amount being non-zero — a delivery can legitimately
        // total zero, and that is a different statement from "not entered yet".
        function hasLines() {
            return !!(data && data.Lines && data.Lines.length);
        }

        // ---------- Status / priority maps (codes -> label + tone) ---------- //

        // DocStatus code -> { key, tone }. tone drives the pill colour: Drafted
        // grey, Completed/Closed green, In Progress/Waiting amber, Voided/
        // Reversed/Invalid red.
        var STATUS_MAP = {
            "DR": { key: "VAS_100_Drafted",             tone: "neutral" },
            "IP": { key: "VAS_100_InProgress",          tone: "warning" },
            "AP": { key: "VAS_100_Approved",            tone: "info" },
            "CO": { key: "VAS_100_Completed",           tone: "success" },
            "CL": { key: "VAS_100_Closed",              tone: "success" },
            "VO": { key: "VAS_100_Voided",              tone: "risk" },
            "RE": { key: "VAS_100_Reversed",            tone: "risk" },
            "WC": { key: "VAS_100_WaitingConfirmation", tone: "warning" },
            "WP": { key: "VAS_100_WaitingPayment",      tone: "warning" },
            "IN": { key: "VAS_100_Invalid",             tone: "risk" },
            "NA": { key: "VAS_100_NotApproved",         tone: "risk" }
        };

        function statusMeta(code) {
            var m = STATUS_MAP[code];
            if (m) return { label: VIS.Msg.getMsg(m.key), tone: m.tone };
            return { label: na(code), tone: "neutral" };
        }

        // PriorityRule code -> { key, tone }. The tone is what colours the badge,
        // so the low end of the scale carries "success": Low and Minor both read
        // as green ("nothing pressing here"), the same way Completed does.
        var PRIORITY_MAP = {
            "1": { key: "VAS_100_Urgent", tone: "risk" },
            "3": { key: "VAS_100_High",   tone: "warning" },
            "5": { key: "VAS_100_Medium", tone: "info" },
            "7": { key: "VAS_100_Low",    tone: "success" },
            "9": { key: "VAS_100_Minor",  tone: "success" }
        };

        function priorityMeta(code) {
            var m = PRIORITY_MAP[code];
            if (!m) return null;
            return { label: VIS.Msg.getMsg(m.key), tone: m.tone };
        }

        // DeliveryViaRule code -> label key + English fallback. The shipping
        // method is a plain field, so no tone is needed.
        var SHIPVIA_MAP = {
            "D": { key: "VAS_100_ShipViaDelivery", text: "Delivery" },
            "P": { key: "VAS_100_ShipViaPickup",   text: "Pickup" },
            "S": { key: "VAS_100_ShipViaShipper",  text: "Shipper" }
        };

        // Reads the shipping method as text; an unmapped / empty code yields ""
        // so the caller can fall back to N/A.
        function shippingMethodLabel(code) {
            var m = SHIPVIA_MAP[code];
            return m ? msg(m.key, m.text) : "";
        }

        // ---------- Header (title strip + details card) ---------- //

        function renderHeader() {
            var st = statusMeta(data.StatusCode);
            var pm = priorityMeta(data.PriorityCode);

            // --- Title strip: title + subtitle (left), priority + status (right) ---
            var $strip = $('<section class="vas_100-hdr"></section>');
            var $top = $('<div class="vas_100-hdrTop"></div>');

            var $tl = $('<div class="vas_100-hdrTitleWrap"></div>');
            $tl.append($('<div class="vas_100-hdrTitle"></div>').text(
                VIS.Msg.getMsg("VAS_100_DeliveryOrder") +
                (data.DocumentNo ? " — " + data.DocumentNo : "")));

            var subBits = [];
            if (data.OwnerName) subBits.push(VIS.Msg.getMsg("VAS_100_Owner") + " " + data.OwnerName);
            if (subBits.length) {
                $tl.append($('<div class="vas_100-hdrSub"></div>').text(subBits.join(" · ")));
            }
            $top.append($tl);

            var $pills = $('<div class="vas_100-hdrPills"></div>');
            if (pm) $pills.append(headerPill(pm.label, pm.tone, "chevUp", false));
            // Posted badge — shown ONLY once the document has actually been posted
            // to the ledger (M_InOut.Posted = 'Y'). An unposted delivery carries no
            // badge at all rather than one reading "Not Posted": the absence is the
            // statement, and a permanent badge that flips its own wording makes the
            // posted case harder to spot, not easier.
            if (data.Posted) {
                $pills.append(headerPill(VIS.Msg.getMsg("VAS_100_Posted"), "success", "check", false));
            }
            $pills.append(headerPill(st.label, st.tone, null, true));
            $top.append($pills);

            $strip.append($top);
            $body.append($strip);

            // --- Details card: customer identity (left) + dispatch fields (right) ---
            var $card = $('<section class="vas_100-hdrCard"></section>');

            // Left column is the CUSTOMER block, built the way the Sales Order
            // overview builds one: the name, then bill-to and ship-to each on
            // their OWN line, then the contact as a row of icon-led bits.
            //
            // It used to read as a name, one unlabelled address, and Location /
            // First Name / Email as labelled fields. That said less with more:
            // the single address never told you WHICH address it was, and a
            // contact split across three field boxes reads as three facts rather
            // than as one person.
            var $left = $('<div class="vas_100-hdrColL"></div>');
            $left.append($('<div class="vas_100-fLabel"></div>').text(VIS.Msg.getMsg("VAS_100_Customer")));
            $left.append($('<div class="vas_100-vendName"></div>').text(na(data.CustomerName)));

            // Bill to and Ship to are frequently different places, which is the
            // whole reason both are here. Each is dropped when the delivery has no
            // such address — a delivery raised without a sales order has no
            // bill-to at all. The ship-to falls back to the address the panel
            // already built from the delivery's own location.
            appendAddressLine($left, "VAS_100_BillTo", "Bill to", data.BillToAddress);
            appendAddressLine($left, "VAS_100_ShipTo", "Ship to",
                data.ShipToAddress || data.CustomerAddress);

            var $contact = $('<div class="vas_100-custContact"></div>');
            appendContactBit($contact, "user",  data.ContactName || data.CustomerFirstName);
            appendContactBit($contact, "phone", data.ContactPhone);
            appendContactBit($contact, "mail",  data.ContactEmail || data.CustomerEmail);
            if ($contact.children().length) $left.append($contact);
            $card.append($left);

            // Right column: where the goods left from and how they travel. Every
            // other dispatch field — shipper, transport doc, tracking, packages,
            // vehicle — moved to the Shipment Details section under the lines,
            // where they read together as one subject instead of crowding the
            // customer's card. The sales order lives in the Reference strip below,
            // where it is clickable.
            var $right = $('<div class="vas_100-hdrColR"></div>');
            $right.append(headerField(VIS.Msg.getMsg("VAS_100_Warehouse"), na(data.WarehouseName), false));
            $right.append(headerField(msg("VAS_100_ShippingMethod", "Shipping Method"),
                na(shippingMethodLabel(data.DeliveryViaRule)), false));
            $right.append(headerField(msg("VAS_100_CustomerLocation", "Location"),
                na(data.CustomerLocationName), false));
            $card.append($right);

            $body.append($card);
        }

        // One address line: the pin, its label, then the address. The label is
        // rendered rather than prefixed into the text so it can be styled apart
        // from the address it introduces, and an address the cell cannot fit is
        // recoverable from the line's tooltip. Ported from VAS_106.
        function appendAddressLine($left, key, fallback, value) {
            if (!value) return;
            var label = msg(key, fallback);
            var $addr = $('<div class="vas_100-vendAddr"></div>').attr("title", label + ": " + value);
            $addr.append(svgIcon("pin"));
            $addr.append($('<span class="vas_100-addrLabel"></span>').text(label));
            $addr.append($('<span class="vas_100-addrVal"></span>').text(value));
            $left.append($addr);
        }

        function appendContactBit($container, icon, value) {
            if (!value) return;
            var $bit = $('<span class="vas_100-contactBit"></span>');
            $bit.append(svgIcon(icon));
            $bit.append($('<span></span>').text(value));
            $container.append($bit);
        }

        function headerPill(label, tone, icon, withDot) {
            var $p = $('<span class="vas_100-hdrPill"></span>')
                .addClass("vas_100-tone-" + (tone || "neutral"));
            if (icon) $p.append(svgIcon(icon));
            if (withDot) $p.append($('<span class="vas_100-hdrDot"></span>'));
            $p.append($('<span></span>').text(label));
            return $p;
        }

        function headerField(label, value, link) {
            var $f = $('<div class="vas_100-hdrField"></div>');
            $f.append($('<div class="vas_100-fLabel"></div>').text(label));
            var $v = $('<div class="vas_100-fVal"></div>').text(value);
            if (link) $v.addClass("vas_100-is-link");
            $f.append($v);
            return $f;
        }

        // ---------- Reference (Generated From chip strip) ---------- //

        // Shows only origins that actually exist — the Sales Order the delivery
        // was raised from (C_Order_ID), its Project (C_Project_ID), the delivery
        // order this one reverses (Ref_InOut_ID) and the RMA (M_RMA_ID, absent
        // wherever the returns module is not installed) — each a clickable chip
        // that opens the source record. "Manual" is the fallback for a delivery
        // order with no origin at all, so it only shows when every one of those
        // came back empty. Same strip, chips and open path as VAS_092.
        function renderReference() {
            var $strip = $('<section class="vas_100-genfrom"></section>');
            $strip.append($('<span class="vas_100-gfLabel"></span>')
                .text(msg("VAS_100_Reference", "Reference")));

            var $chips = $('<div class="vas_100-gfChips"></div>');
            var any = false;

            // Sales Order (origin) — opened as a sales transaction so the
            // framework resolves the Sales Order window rather than the Purchase
            // Order window (both live in C_Order).
            if (data.C_Order_ID > 0) {
                $chips.append(originChip("doc", VIS.Msg.getMsg("VAS_100_SalesOrder"),
                    data.SONo || ("#" + data.C_Order_ID),
                    pill(msg("VAS_100_Origin", "Origin"), "info"),
                    "info", "C_Order", data.C_Order_ID, true));
                any = true;
            }

            // Project the delivery was made against.
            if (data.C_Project_ID > 0) {
                $chips.append(originChip("doc", msg("VAS_100_Project", "Project"),
                    data.ProjectNo || data.ProjectName || ("#" + data.C_Project_ID),
                    null, "success", "C_Project", data.C_Project_ID));
                any = true;
            }

            // The delivery order this one reverses / counters (Ref_InOut_ID).
            if (data.Ref_InOut_ID > 0) {
                $chips.append(originChip("truck", msg("VAS_100_ReversalOf", "Reversal Of"),
                    data.RefInOutDocNo || ("#" + data.Ref_InOut_ID),
                    null, "warning", "M_InOut", data.Ref_InOut_ID, true));
                any = true;
            }

            // RMA — only ever non-zero where the returns module is installed.
            if (data.M_RMA_ID > 0) {
                $chips.append(originChip("clipboardCheck", msg("VAS_100_Rma", "RMA"),
                    data.RmaDocNo || ("#" + data.M_RMA_ID),
                    null, "purple", "M_RMA", data.M_RMA_ID));
                any = true;
            }

            if (!any) {
                $chips.append(originChip("pencil", msg("VAS_100_Manual", "Manual"),
                    null, null, "info", null, 0));
            }

            $strip.append($chips);
            $body.append($strip);
        }

        // Origin chip: leading icon (tinted by iconTone) + grey label + dark
        // value, with an optional trailing status pill. When a table + record id
        // is supplied the chip becomes a link that opens that record.
        function originChip(icon, label, value, $statusPill, iconTone, tableName, recordId, isSOTrx) {
            var $chip = $('<span class="vas_100-chip"></span>')
                .addClass("vas_100-ic-" + (iconTone || "muted"));
            var isLink = tableName && recordId && +recordId > 0;
            if (isLink) {
                $chip.addClass("vas_100-is-link")
                    .attr("data-open-table", tableName)
                    .attr("data-open-id", recordId);
                // Sales-transaction records (the originating sales order, and a
                // delivery order itself) must open in their SO window.
                if (isSOTrx) $chip.attr("data-open-sotrx", "Y");
            }
            // The chip caps at the strip's width and its value truncates inside
            // it, so one long document number cannot run off the panel — the
            // untruncated text stays readable on the chip's own tooltip.
            $chip.attr("title", value ? label + ": " + value : label);
            $chip.append(svgIcon(icon));
            $chip.append($('<span class="vas_100-chipLabel"></span>').text(label));
            if (value) $chip.append($('<span class="vas_100-chipVal"></span>').text(value));
            if ($statusPill) $chip.append($statusPill);
            if (isLink) $chip.append(svgIcon("arrowUpRight"));
            return $chip;
        }

        function pill(label, tone) {
            return $('<span class="vas_100-pill"></span>')
                .addClass("vas_100-tone-" + (tone || "neutral"))
                .text(label);
        }

        // ---------- Snapshot (metric grid) ---------- //

        function renderSnapshot() {
            var confirms = confirmationApplicable();

            var $snap = $('<section class="vas_100-snap"></section>');
            // With the two confirmation cards in play the grid runs three-up, so
            // the cards form even rows instead of leaving one alone at the end.
            if (confirms) $snap.addClass("vas_100-has-qc");
            var cur = currencyToken();

            // Delivery value — only once there are lines to derive it FROM. The
            // figure is Σ (delivered qty x rate) over the lines (model side), so on
            // a delivery with none it can only ever be zero, and "₹ 0.00" against
            // an empty delivery reads as a priced document rather than as one not
            // yet entered. The card returns the moment a line does.
            if (hasLines()) {
                $snap.append(metricCard("total", "coins", VIS.Msg.getMsg("VAS_100_DeliveryValue"),
                    formatAmount(+data.DeliveryValue || 0, cur, data.StdPrecision),
                    data.ISO_Code || ""));
            }

            // Line count.
            $snap.append(metricCard("lines", "box", VIS.Msg.getMsg("VAS_100_Lines"),
                (data.LineCount || 0) + "",
                VIS.Msg.getMsg("VAS_100_LinesDelivered")));

            // Delivered quantity (units).
            $snap.append(metricCard("received", "truck", VIS.Msg.getMsg("VAS_100_DeliveredQty"),
                formatNumber(+data.DeliveredQty || 0, 0),
                VIS.Msg.getMsg("VAS_100_Units")));

            // Linked sales order.
            $snap.append(metricCard("order", "doc", VIS.Msg.getMsg("VAS_100_SalesOrder"),
                data.SONo || VIS.Msg.getMsg("VAS_100_NotLinked"),
                formatDate(data.SODateOrdered) || ""));

            // Confirmation Check — whether this delivery's document type asks for a
            // shipment confirmation (C_DocType.IsShipConfirm), with the number of
            // its lines that carry QA parameters underneath. The two answer
            // different questions: a delivery can be confirmable with no QA
            // parameters on any product, and a drafted one can have parameters
            // waiting with no confirmation yet. Shown either way — "Non-Applicable"
            // is an answer, and without the card the reader cannot tell a delivery
            // that needs no confirmation from one whose card failed to draw.
            $snap.append(metricCard("quality", "clipboardCheck",
                msg("VAS_100_ConfirmationCheck", "Confirmation Check"),
                confirms ? msg("VAS_100_Applicable", "Applicable")
                         : msg("VAS_100_NonApplicable", "Non-Applicable"),
                msg("VAS_100_ProductQAParameters", "Product QA Parameters") + ": " +
                    (data.QaParamLineCount || 0)));

            // The confirmation quantities, straight from the delivery's
            // confirmation lines. Only when the delivery is one that gets confirmed
            // at all — on any other document type they are structurally zero and
            // would read as a finding rather than as an absence.
            if (confirms) {
                $snap.append(metricCard("accepted", "checkCircle",
                    msg("VAS_100_AcceptedQty", "Accepted Quantity"),
                    formatNumber(+data.AcceptedQty || 0, 0),
                    msg("VAS_100_UnitsConfirmed", "units confirmed")));

                // Difference is target less confirmed — what did not go out as
                // expected. Scrapped rides underneath it: it is the part of that
                // gap the confirmation explicitly wrote off, so the two read
                // together.
                $snap.append(metricCard("rejected", "xCircle",
                    msg("VAS_100_DifferenceQty", "Difference Quantity"),
                    formatNumber(+data.DifferenceQty || 0, 0),
                    msg("VAS_100_ScrappedQty", "Scrapped Quantity") + ": " +
                        formatNumber(+data.ScrappedQty || 0, 0)));
            }

            $body.append($snap);
        }

        // True when this delivery's document type raises a shipment confirmation
        // (C_DocType.IsShipConfirm) — "this delivery is going to be confirmed",
        // which is what makes a confirmed / difference / scrapped quantity a
        // meaningful thing to report.
        //
        // NOT the same question as "does a quality check apply to a line", which
        // drives the Quality column and the per-line parameters below: a delivery
        // can be confirmable with no QA parameters anywhere, and a drafted one can
        // have parameters waiting with no confirmation raised yet.
        function confirmationApplicable() {
            return !!(data && data.IsShipConfirmDocType);
        }

        function metricCard(tone, icon, label, value, sub) {
            var $c = $('<div class="vas_100-metric"></div>').addClass("vas_100-tone-" + tone);

            var $head = $('<div class="vas_100-mHead"></div>');
            $head.append(svgIcon(icon));
            $head.append($('<span class="vas_100-mLabel"></span>').text(label));
            $c.append($head);

            $c.append($('<div class="vas_100-mVal"></div>').text(value));
            if (sub) $c.append($('<div class="vas_100-mSub"></div>').text(sub));
            return $c;
        }

        // ---------- Fulfilment lifecycle (horizontal stepper) ---------- //

        // Four stages driven by DocStatus + Processed:
        //   Completed/Closed        -> all four done (active = Completed)
        //   Processed / IP / AP     -> Drafted + Picked done, In Transit active
        //   Waiting confirmation/pay-> Drafted done, Picked/Packed active
        //   otherwise (Drafted)     -> Drafted active
        // Voided / Reversed collapse to Drafted (no forward progress).
        function lifecycleReachedIndex() {
            var s = data.StatusCode;
            if (s === "CO" || s === "CL") return 3;
            if (s === "VO" || s === "RE" || s === "IN" || s === "NA") return 0;
            if (data.Processed || s === "IP" || s === "AP") return 2;
            if (s === "WC" || s === "WP") return 1;
            return 0;
        }

        function renderLifecycle() {
            var stageKeys = [
                "VAS_100_StageDrafted",
                "VAS_100_StagePickedPacked",
                "VAS_100_StageInTransit",
                "VAS_100_StageCompleted"
            ];
            var reached = lifecycleReachedIndex();
            var completed = data.StatusCode === "CO" || data.StatusCode === "CL";

            var $sec = section(msg("VAS_100_Timeline", "Delivery Order Timeline"), null);

            // The Drafted stage is dated with the record's creation stamp — the
            // moment the delivery order came into being — rather than reading a
            // bare "Done" / "Current" like the stages after it.
            var createdOn = formatDate(data.Created);

            var $tl = $('<div class="vas_100-stepper"></div>');
            for (var i = 0; i < stageKeys.length; i++) {
                var stateCls, metaText, done;
                if (i < reached) {
                    stateCls = "vas_100-is-done"; done = true;
                    metaText = VIS.Msg.getMsg("VAS_100_Done");
                } else if (i === reached) {
                    // The final stage, once reached on a completed DO, reads done.
                    if (completed && i === stageKeys.length - 1) {
                        stateCls = "vas_100-is-done"; done = true;
                        metaText = formatDate(data.MovementDate) || VIS.Msg.getMsg("VAS_100_Done");
                    } else {
                        stateCls = "vas_100-is-active"; done = false;
                        metaText = VIS.Msg.getMsg("VAS_100_Current");
                    }
                } else {
                    stateCls = "is-pending"; done = false;
                    metaText = VIS.Msg.getMsg("VAS_100_Pending");
                }
                // Drafted always reports WHEN the record was created; the state
                // wording above only stands in when there is no creation stamp.
                if (i === 0 && createdOn) metaText = createdOn;
                $tl.append(stepEntry(i + 1, VIS.Msg.getMsg(stageKeys[i]), metaText, done, stateCls));
            }
            $sec.append($tl);
        }

        function stepEntry(num, title, meta, done, stateCls) {
            var $entry = $('<div class="vas_100-step"></div>').addClass(stateCls || "");

            var $rail = $('<div class="vas_100-stepRail"></div>');
            $rail.append($('<span class="vas_100-stepLine vas_100-stepLine-l"></span>'));
            var $dot = $('<span class="vas_100-stepDot"></span>');
            if (done) {
                $dot.append(svgIcon("check"));
            } else {
                $dot.text(num);
            }
            $rail.append($dot);
            $rail.append($('<span class="vas_100-stepLine vas_100-stepLine-r"></span>'));
            $entry.append($rail);

            var $lbl = $('<div class="vas_100-stepLabel"></div>');
            $lbl.append($('<div class="vas_100-stepTitle"></div>').text(title));
            if (meta) $lbl.append($('<div class="vas_100-stepMeta"></div>').text(meta));
            $entry.append($lbl);

            return $entry;
        }

        // ---------- Delivery lines (table) ---------- //

        function renderLines() {
            var lines = (data && data.Lines) || [];
            if (!lines.length) return;

            var cur = currencyToken();

            var $sec = section(msg("VAS_100_LinesSection", "Lines"), {
                summary: (data.LineCount || 0) + " " + msg("VAS_100_LinesLower", "lines") + " · " +
                    formatNumber(+data.DeliveredQty || 0, 0) + " " + VIS.Msg.getMsg("VAS_100_Units")
            });

            // The delivery's quality parameters, grouped by the line they belong
            // to, so each line can open its own drawer. Rebuilt per render.
            indexQualityParams();

            // The Quality column exists only when at least one line on this
            // delivery has QA parameters defined. On a delivery where none does, a
            // column of crosses is a statement about nothing.
            var showQuality = anyQualityLine();

            var $tbl = $('<div class="vas_100-table"></div>');
            if (showQuality) $tbl.addClass("vas_100-has-q");

            // Header row
            var $head = $('<div class="vas_100-tRow vas_100-tHead"></div>');
            $head.append($('<span></span>').text(msg("VAS_100_Line", "Line")));
            $head.append($('<span></span>').text(VIS.Msg.getMsg("VAS_100_UOM")));
            $head.append($('<span class="vas_100-ta-r"></span>').text(VIS.Msg.getMsg("VAS_100_Ordered")));
            $head.append($('<span class="vas_100-ta-r"></span>').text(VIS.Msg.getMsg("VAS_100_Delivered")));
            $head.append($('<span class="vas_100-ta-r"></span>').text(VIS.Msg.getMsg("VAS_100_LineValue")));
            $head.append($('<span class="vas_100-ta-c"></span>').text(VIS.Msg.getMsg("VAS_100_Status")));
            if (showQuality) {
                $head.append($('<span class="vas_100-ta-c"></span>')
                    .text(msg("VAS_100_Quality", "Quality")));
            }
            $tbl.append($head);

            // Totals footer — always the WHOLE delivery, never just the page.
            var $foot = $('<div class="vas_100-tFoot"></div>');
            var $bit = $('<span class="vas_100-tf vas_100-is-grand"></span>');
            $bit.append(document.createTextNode(VIS.Msg.getMsg("VAS_100_TotalDeliveryValue")));
            $bit.append($('<b></b>').text(formatAmount(+data.DeliveryValue || 0, cur, data.StdPrecision)));
            $foot.append($bit);
            $tbl.append($foot);

            $sec.append($tbl);

            // The pager sits OUTSIDE the table: the table takes its own horizontal
            // scroll on a narrow panel, and the controls must not scroll away with
            // the columns.
            var $pager = $('<div class="vas_100-pager"></div>');
            if (lines.length > ROWS_PER_PAGE) $sec.append($pager);

            // Rows are replaced in place, ahead of the totals footer, so the
            // table's structure and its CSS grid stay exactly as they were.
            function paintPage() {
                var pageCount = Math.max(1, Math.ceil(lines.length / ROWS_PER_PAGE));
                if (linesPage >= pageCount) linesPage = pageCount - 1;
                if (linesPage < 0) linesPage = 0;

                var start = linesPage * ROWS_PER_PAGE;
                var end = Math.min(lines.length, start + ROWS_PER_PAGE);

                // The quality drawers go with the rows they belong to — each is a
                // SIBLING of its line row (so it can span the table's full width
                // instead of living inside one grid cell), so both are cleared.
                $tbl.find(".vas_100-tBody, .vas_100-qpDrawer").remove();
                for (var i = start; i < end; i++) {
                    $foot.before(buildLineRow(lines[i], cur, showQuality));
                    var $drawer = buildQualityDrawer(lines[i]);
                    if ($drawer) $foot.before($drawer);
                }

                buildPager($pager, linesPage, pageCount, lines.length, start, end,
                    function (p) { linesPage = p; paintPage(); });
            }

            paintPage();
        }

        // Range caption on the left, Previous / page-of / Next on the right.
        // Rebuilt on every page change so the disabled states stay accurate.
        // `page` is 0-based and `onGo` is handed the page to move to, so the
        // caller owns its own page state. Nothing is drawn for a single-page
        // list, so a short table shows no controls at all.
        function buildPager($pager, page, pageCount, total, start, end, onGo) {
            $pager.empty();
            if (pageCount <= 1) return;

            $pager.append($('<span class="vas_100-pgRange"></span>').text(
                msg("VAS_100_Showing", "Showing") + " " + (start + 1) + "-" + end + " " +
                msg("VAS_100_Of", "of") + " " + total));

            var $ctrls = $('<span class="vas_100-pgCtrls"></span>');
            $ctrls.append(pagerButton(msg("VAS_100_Previous", "Previous"), "chevLeft",
                page <= 0, function () { onGo(page - 1); }));
            $ctrls.append($('<span class="vas_100-pgPos"></span>').text(
                msg("VAS_100_Page", "Page") + " " + (page + 1) + " " +
                msg("VAS_100_Of", "of") + " " + pageCount));
            $ctrls.append(pagerButton(msg("VAS_100_Next", "Next"), "chevRight",
                page >= pageCount - 1, function () { onGo(page + 1); }));
            $pager.append($ctrls);
        }

        function pagerButton(label, icon, disabled, handler) {
            var $b = $('<span class="vas_100-pgBtn"></span>');
            if (icon === "chevLeft") $b.append(svgIcon(icon));
            $b.append($('<span></span>').text(label));
            if (icon === "chevRight") $b.append(svgIcon(icon));
            if (disabled) $b.addClass("vas_100-is-disabled");
            else $b.on("click", handler);
            return $b;
        }

        // ---------- Shipment Details ---------- //

        // How the goods actually travelled: the shipper, the transport document,
        // the tracking reference, the vehicle and the package count. These sat in
        // the header card's right column, where they crowded the customer's own
        // details with a different subject; read together under the lines they
        // answer one question.
        //
        // Drawn ONLY when the delivery records at least one of them — a card of
        // six dashes says nothing, and on the many deliveries that carry no
        // dispatch information at all the section is simply absent.
        function renderShipmentDetails() {
            var fields = [
                { key: "VAS_100_Shipper",       text: "Shipper",                    value: data.ShipperName },
                { key: "VAS_100_TransportDoc",  text: "Transport Document No.",     value: data.TransportDoc },
                { key: "VAS_100_TrackingNo",    text: "Tracking No.",               value: data.TrackingNo },
                { key: "VAS_100_VehicleName",   text: "Vehicle Name",               value: data.VehicleName },
                { key: "VAS_100_VehicleNo",     text: "Vehicle Registration Number", value: data.VehicleNo },
                // Packages is a COUNT, so zero is an absence rather than a value —
                // an unpacked delivery should not report "0 packages".
                { key: "VAS_100_Packages",      text: "Packages",
                  value: (+data.PackageCount > 0) ? (data.PackageCount + "") : "" }
            ];

            var present = [];
            for (var i = 0; i < fields.length; i++) {
                var v = fields[i].value;
                if (v !== null && v !== undefined && String(v).trim() !== "") present.push(fields[i]);
            }
            if (!present.length) return;

            var $sec = section(msg("VAS_100_ShipmentDetails", "Shipment Details"), null);

            // The header card's own layout, so the section reads as part of the
            // same panel: the identity block on the left, the fields two-across on
            // the right, collapsing to one column on a narrow panel.
            var $card = $('<section class="vas_100-hdrCard vas_100-shipCard"></section>');

            var $left = $('<div class="vas_100-hdrColL"></div>');
            $left.append($('<div class="vas_100-fLabel"></div>')
                .text(msg("VAS_100_ShippedVia", "Shipped Via")));
            // The shipper names the carrier; without one the shipping method is
            // what the delivery can say about how it travelled.
            var via = (data.ShipperName || "").trim() ||
                      shippingMethodLabel(data.DeliveryViaRule) ||
                      msg("VAS_100_ShipmentDetails", "Shipment Details");
            $left.append($('<div class="vas_100-vendName"></div>').text(via));

            var $bits = $('<div class="vas_100-custContact"></div>');
            appendContactBit($bits, "truck", data.VehicleName || data.VehicleNo);
            appendContactBit($bits, "box",
                (+data.PackageCount > 0)
                    ? data.PackageCount + " " + msg("VAS_100_PackagesLower", "packages") : "");
            if ($bits.children().length) $left.append($bits);
            $card.append($left);

            // Every field the delivery actually records, in the order asked for.
            // A field with no value is left out rather than printed against a
            // dash: this section exists to report what IS known.
            var $right = $('<div class="vas_100-hdrColR"></div>');
            for (var j = 0; j < present.length; j++) {
                $right.append(headerField(msg(present[j].key, present[j].text),
                    String(present[j].value).trim(), false));
            }
            $card.append($right);

            $sec.append($card);
        }

        // ---------- Documents raised against the delivery ---------- //

        // The customer invoices raised from this delivery, the shipment
        // confirmations recorded for it and the customer receipts allocated to
        // those invoices — each row opening its document through the shared
        // openRecord() zoom path. The GRN overview's section, with the sales /
        // purchase polarity flipped.
        //
        // Drawn only when there IS something to list: a delivery nothing has been
        // raised against shows no section rather than an empty frame.
        var DOC_TYPES = {
            invoice:      { icon: "doc",            labelKey: "VAS_100_CustomerInvoice", labelText: "Customer Invoice" },
            confirmation: { icon: "clipboardCheck", labelKey: "VAS_100_Confirmation",    labelText: "Shipment Confirmation" },
            payment:      { icon: "coins",          labelKey: "VAS_100_Receipt",         labelText: "Customer Receipt" }
        };

        function renderDocuments() {
            var rows = (data && data.Documents) || [];
            if (!rows.length) return;

            var counts = { invoice: 0, confirmation: 0, payment: 0 };
            for (var c = 0; c < rows.length; c++) {
                if (counts.hasOwnProperty(rows[c].Type)) counts[rows[c].Type]++;
            }
            var summaryBits = [];
            if (counts.invoice) {
                summaryBits.push(counts.invoice + " " + msg("VAS_100_InvoicesCount", "invoices"));
            }
            if (counts.confirmation) {
                summaryBits.push(counts.confirmation + " " + msg("VAS_100_ConfirmationsCount", "confirmations"));
            }
            if (counts.payment) {
                summaryBits.push(counts.payment + " " + msg("VAS_100_ReceiptsCount", "receipts"));
            }

            var $sec = section(msg("VAS_100_Documents", "Documents"), {
                summary: summaryBits.join(" · ")
            });

            var $tbl = $('<div class="vas_100-table vas_100-docTable"></div>');

            var $head = $('<div class="vas_100-docRow vas_100-tHead"></div>');
            $head.append($('<span></span>').text(msg("VAS_100_Document", "Document")));
            $head.append($('<span></span>').text(msg("VAS_100_DocDate", "Date")));
            $head.append($('<span class="vas_100-ta-c"></span>').text(msg("VAS_100_DocStatus", "Status")));
            $head.append($('<span class="vas_100-ta-r"></span>').text(msg("VAS_100_Amount", "Amount")));
            $tbl.append($head);

            for (var i = 0; i < rows.length; i++) $tbl.append(buildDocumentRow(rows[i]));
            $sec.append($tbl);
        }

        function buildDocumentRow(d) {
            var meta = DOC_TYPES[d.Type] || DOC_TYPES.invoice;
            var $tr = $('<div class="vas_100-docRow vas_100-tBody"></div>');

            var canOpen = d.TableName && +d.RecordId > 0;
            if (canOpen) {
                $tr.addClass("vas_100-is-link")
                    .attr("data-open-table", d.TableName)
                    .attr("data-open-id", d.RecordId);
            }

            // Document: icon, number, and beneath it what kind of document it is —
            // plus, where the row has one, the detail that qualifies it (a
            // confirmation's line count, a receipt's discount).
            var $item = $('<span class="vas_100-docItem"></span>');
            $item.append(svgIcon(meta.icon));

            var $txt = $('<span class="vas_100-docTxt"></span>');
            var docNo = (d.DocumentNo || "").trim() || ("#" + d.RecordId);
            $txt.append($('<div class="vas_100-itName"></div>').attr("title", docNo).text(docNo));

            var subBits = [msg(meta.labelKey, meta.labelText)];
            if (d.Type === "confirmation" && d.LineCount > 0) {
                subBits.push(d.LineCount + " " + msg("VAS_100_LinesLower", "lines"));
            }
            if (d.Type === "payment" && +d.DiscountAmt) {
                subBits.push(msg("VAS_100_DiscountedAmount", "Discounted Amount") + ": " +
                    formatAmount(+d.DiscountAmt, currencyToken(), data.StdPrecision));
            }
            if (d.Type === "invoice" && d.IsPaid) {
                subBits.push(msg("VAS_100_Paid", "Paid"));
            }
            var sub = subBits.join(" · ");
            $txt.append($('<div class="vas_100-itSku"></div>').attr("title", sub).text(sub));
            $item.append($txt);
            if (canOpen) $item.append(svgIcon("arrowUpRight"));
            $tr.append($item);

            // Date
            $tr.append($('<span></span>').text(formatDate(d.DocDate)));

            // Status — the same pill vocabulary the header uses.
            var st = statusMeta(d.DocStatus);
            var $st = $('<span class="vas_100-ta-c"></span>');
            $st.append($('<span class="vas_100-tag"></span>')
                .addClass("vas_100-tone-" + (st.tone || "neutral")).text(st.label));
            $tr.append($st);

            // Amount. A confirmation has none of its own, and a cell with no value
            // is left blank rather than filled with a placeholder.
            var $amt = $('<span class="vas_100-ta-r"></span>');
            if (d.Amount !== null && d.Amount !== undefined) {
                $amt.text(formatAmount(+d.Amount || 0, currencyToken(), data.StdPrecision));
            }
            $tr.append($amt);

            return $tr;
        }

        // ---------- Notes (the description entered on the delivery order) ---------- //

        function renderNotes() {
            var text = (data && data.Description) || "";
            if (!String(text).trim()) return;

            var $sec = section(msg("VAS_100_Notes", "Notes"), null);
            $sec.append($('<div class="vas_100-noteCard"></div>').text(text));
        }

        // ---------- Activity (audit trail) ---------- //

        // The same type set VAS_092 tags, so the two panels read alike: the
        // document's whole lifecycle (one row per completed workflow node) plus
        // the field-level edits, notes and e-mails.
        //
        // A type with no titleKey headlines with its OWN text — for a lifecycle
        // row that is the workflow node's name, so a tenant that renamed its nodes
        // reads the trail in its own words; for a note the comment, for an e-mail
        // the subject.
        var ACT_TYPES = {
            created:     { tone: "neutral", icon: "doc",    tagKey: "VAS_100_TagCreated",     tagText: "Created",      titleKey: "VAS_100_ActCreated", titleText: "Delivery order created" },
            prepared:    { tone: "neutral", icon: "doc",    tagKey: "VAS_100_TagPrepared",    tagText: "Prepared",     titleKey: null, titleText: "" },
            completed:   { tone: "success", icon: "check",  tagKey: "VAS_100_TagCompleted",   tagText: "Completed",    titleKey: "VAS_100_ActCompleted", titleText: "Delivery order completed" },
            reactivated: { tone: "warning", icon: "pencil", tagKey: "VAS_100_TagReactivated", tagText: "Re-activated", titleKey: null, titleText: "" },
            rejected:    { tone: "risk",    icon: "alert",  tagKey: "VAS_100_TagRejected",    tagText: "Rejected",     titleKey: null, titleText: "" },
            approval:    { tone: "purple",  icon: "check",  tagKey: "VAS_100_TagApproval",    tagText: "Approved",     titleKey: null, titleText: "" },
            voided:      { tone: "risk",    icon: "alert",  tagKey: "VAS_100_TagVoided",      tagText: "Voided",       titleKey: null, titleText: "" },
            reversed:    { tone: "risk",    icon: "alert",  tagKey: "VAS_100_TagReversed",    tagText: "Reversed",     titleKey: null, titleText: "" },
            closed:      { tone: "neutral", icon: "check",  tagKey: "VAS_100_TagClosed",      tagText: "Closed",       titleKey: null, titleText: "" },
            invalidated: { tone: "warning", icon: "alert",  tagKey: "VAS_100_TagInvalidated", tagText: "Invalid",      titleKey: null, titleText: "" },
            // One row per FIELD that changed, not one per save.
            updated:     { tone: "info",    icon: "pencil", tagKey: "VAS_100_TagUpdated",     tagText: "Updated",      titleKey: "VAS_100_ActUpdated", titleText: "Delivery order updated" },
            note:        { tone: "neutral", icon: "note",   tagKey: "VAS_100_TagNote",        tagText: "Note",         titleKey: null, titleText: "" },
            email:       { tone: "purple",  icon: "mail",   tagKey: "VAS_100_TagEmail",       tagText: "Email",        titleKey: null, titleText: "" }
        };

        // Maximum activity rows shown per page; the feed paginates beyond this.
        // A long-running delivery accumulates every mail, status change and edit,
        // and an unpaged feed made the panel scroll past everything below it. The
        // section summary still counts the WHOLE feed, not the page.
        var ACTIVITY_PER_PAGE = 15;
        var activityPage = 0;   // current Activity page (0-based, like linesPage)

        function renderActivity() {
            var rows = (data && data.Activity) || [];
            if (!rows.length) return;

            var $sec = section(msg("VAS_100_Activity", "Activity"), {
                summary: rows.length + " " + msg("VAS_100_Updates", "updates")
            });

            var $list = $('<div class="vas_100-actList"></div>');
            $sec.append($list);

            // The pager is a sibling of the list card, so the controls keep their
            // place while the card's rows are replaced underneath them.
            var $pager = $('<div class="vas_100-pager"></div>');
            if (rows.length > ACTIVITY_PER_PAGE) $sec.append($pager);

            function paintPage() {
                var pageCount = Math.max(1, Math.ceil(rows.length / ACTIVITY_PER_PAGE));
                if (activityPage >= pageCount) activityPage = pageCount - 1;
                if (activityPage < 0) activityPage = 0;

                var start = activityPage * ACTIVITY_PER_PAGE;
                var end = Math.min(rows.length, start + ACTIVITY_PER_PAGE);

                $list.empty();
                for (var i = start; i < end; i++) {
                    $list.append(activityRow(rows[i]));
                    // An e-mail's body is heavy — it stays collapsed under its row
                    // and opens only when the reader asks for it.
                    var $mail = activityBody(rows[i]);
                    if ($mail) $list.append($mail);
                }

                buildPager($pager, activityPage, pageCount, rows.length, start, end,
                    function (p) { activityPage = p; paintPage(); });
            }

            paintPage();
        }

        function activityRow(a) {
            var meta = ACT_TYPES[a.Type] || ACT_TYPES.note;

            var $row = $('<div class="vas_100-actRow"></div>');

            var $tag = $('<span class="vas_100-actTag"></span>').addClass("vas_100-tone-" + meta.tone);
            $tag.append(svgIcon(meta.icon));
            $tag.append($('<span></span>').text(msg(meta.tagKey, meta.tagText)));
            $row.append($tag);

            var title = activityTitle(a, meta);
            var $title = $('<span class="vas_100-actTitle"></span>');
            var $lead = $('<span class="vas_100-actLead"></span>').text(title).attr("title", title);
            // A note's headline IS the comment, so it wraps rather than clipping
            // after one line — that text is what the reader came for.
            if (a.Type === "note") $lead.addClass("vas_100-multiline");
            $title.append($lead);

            // An e-mail names its recipients under the subject — every address on
            // the To, Cc and Bcc lists, in full. The line wraps, so a long list is
            // read on the row itself rather than hidden behind a count.
            if (a.Type === "email") {
                var to = recipientSummary(a);
                if (to) $title.append($('<small class="vas_100-actSub"></small>').text(to));
            }

            // A field edit names the line it landed on. Dropped entirely for a
            // header edit, which has no line to name.
            if (a.Type === "updated" && a.ChangeScope) {
                $title.append($('<small class="vas_100-actSub"></small>')
                    .text(a.ChangeScope).attr("title", a.ChangeScope));
            }
            $row.append($title);

            // "when · by whom" — the audit trail's whole point, in the same place
            // on every row. For an e-mail that is when it went out and who sent it.
            var when = formatDateTime(a.Created);
            if (a.UserName) {
                when = when ? when + " · " + msg("VAS_100_By", "by") + " " + a.UserName
                            : msg("VAS_100_By", "by") + " " + a.UserName;
            }
            $row.append($('<span class="vas_100-actWhen"></span>').text(when).attr("title", when));

            // Rows carrying a body are clickable; the caret shows the state.
            if (hasActivityBody(a)) {
                $row.addClass("vas_100-is-openable");
                $row.attr("title", msg("VAS_100_ShowMailBody", "Click to read the message"));
                $row.append($('<span class="vas_100-actCaret"></span>').append(svgIcon("chevRight")));
                $row.on("click", function () {
                    var $panel = $row.next(".vas_100-actBody");
                    if (!$panel.length) return;
                    var nowOpen = !$row.hasClass("vas_100-is-open");
                    $row.toggleClass("vas_100-is-open", nowOpen)
                        .attr("title", nowOpen ? msg("VAS_100_HideMailBody", "Click to hide the message")
                                               : msg("VAS_100_ShowMailBody", "Click to read the message"));
                    $panel.toggle(nowOpen);
                });
            }

            return $row;
        }

        // Follows VAS_092's rule exactly.
        function activityTitle(a, meta) {
            if (a.Type === "email") {
                return (a.Text || "").trim() || msg("VAS_100_NoSubject", "(no subject)");
            }
            // Free-text types (note, and every workflow lifecycle row) headline
            // with their own text; an untitled one falls back to what its tag says.
            if (!meta.titleKey) return (a.Text || "").trim() || msg(meta.tagKey, meta.tagText);

            // A field-level edit headlines with the FIELD that changed — the row's
            // tag already says "Updated", and the field is what tells one edit
            // apart from the next. Rows with no field (change logging off) keep
            // the generic wording.
            if (a.Type === "updated" && a.FieldName) {
                return msg("VAS_100_ActFieldUpdated", "Updated") + " " + a.FieldName;
            }
            return msg(meta.titleKey, meta.titleText);
        }

        // Only an e-mail carries a body worth opening; a mail stored without one
        // stays a plain, non-clickable row.
        function hasActivityBody(a) {
            return !!(a && a.Type === "email" && a.Body && String(a.Body).trim());
        }

        // The e-mail body, collapsed beneath its activity row. The full recipient
        // set (From / To / Cc / Bcc) heads it, so every address the mail went to is
        // on screen once the reader opens the message.
        function activityBody(a) {
            if (!hasActivityBody(a)) return null;

            var $panel = $('<div class="vas_100-actBody" style="display:none;"></div>');
            appendMailMeta($panel, "VAS_100_MailFrom", "From:", a.MailFrom);
            appendMailMeta($panel, "VAS_100_MailTo",   "To:",   a.MailTo);
            appendMailMeta($panel, "VAS_100_MailCc",   "Cc:",   a.MailCc);
            appendMailMeta($panel, "VAS_100_MailBcc",  "Bcc:",  a.MailBcc);
            $panel.append($('<p></p>').text(String(a.Body).trim()));
            return $panel;
        }

        function appendMailMeta($panel, key, fallback, value) {
            if (!value || !String(value).trim()) return;
            $panel.append($('<div class="vas_100-actMeta"></div>')
                .text(msg(key, fallback) + " " + String(value).trim()));
        }

        // Row sub-line: every address the mail went to, written out in full — To,
        // then Cc, then Bcc, each behind its own label. A label with nothing behind
        // it is left out entirely: this line lists recipients, and an empty Cc is
        // not one.
        function recipientSummary(a) {
            var bits = [];
            appendAddressBit(bits, "VAS_100_MailTo",  "To:",  a.MailTo);
            appendAddressBit(bits, "VAS_100_MailCc",  "Cc:",  a.MailCc);
            appendAddressBit(bits, "VAS_100_MailBcc", "Bcc:", a.MailBcc);
            return bits.join(" · ");
        }

        function appendAddressBit(bits, key, fallback, value) {
            var text = (value === null || value === undefined) ? "" : String(value).trim();
            if (!text) return;
            bits.push(msg(key, fallback) + " " + text);
        }

        function buildLineRow(ln, cur, showQuality) {
            var $tr = $('<div class="vas_100-tRow vas_100-tBody"></div>');

            // Item (name + code / attribute / locator). The product code leads the
            // sub-line bare — the "SKU" caption in front of it was noise.
            var $item = $('<span class="vas_100-itItem"></span>');
            // Both the name and the locator clip to one line, so each carries its
            // own full text as a tooltip: hovering the product reads out the whole
            // product name, hovering the locator the whole locator.
            var productName = na(ln.ProductName);
            var $name = $('<div class="vas_100-itName"></div>').attr("title", productName);

            // Expander, only on a product that actually has parameters to show — a
            // caret against a line with nothing under it is a promise the click
            // cannot keep. Its state is held per line id, so a pager repaint (or a
            // move to another page and back) keeps what the reader opened.
            var qParams = qualityParamsFor(ln);
            if (qParams.length) {
                var $toggle = $('<span class="vas_100-qpToggle"></span>')
                    .append(svgIcon("chevRight"));
                setQpToggleState($toggle, !!lineQpOpen[ln.M_InOutLine_ID]);
                $toggle.on("click", function (e) {
                    e.stopPropagation();
                    var nowOpen = !lineQpOpen[ln.M_InOutLine_ID];
                    lineQpOpen[ln.M_InOutLine_ID] = nowOpen;
                    setQpToggleState($toggle, nowOpen);
                    $tr.next(".vas_100-qpDrawer").toggle(nowOpen);
                });
                $name.append($toggle);
            }

            $name.append($('<span></span>').text(productName));
            $item.append($name);

            // The cell reads down in four lines, each answering one question:
            //   1  the product name (above)
            //   2  its ATTRIBUTES — lot / serial / attribute set — when it has any
            //   3  the product code and the locator it went out of
            //   4  whether the line is drop-shipped
            // They were one run-together sub-line ("code · attribute · locator"),
            // which put three unrelated facts behind two middle dots and truncated
            // the lot out of sight on any line long enough to matter.

            // 2 — attributes, only for a product that carries them.
            var attr = (ln.AttributeName || "").trim();
            if (attr && attr !== "--" && attr !== "-") {
                $item.append($('<div class="vas_100-itAttr"></div>')
                    .attr("title", attr).text(attr));
            }

            // 3 — product code and locator.
            var $meta = $('<div class="vas_100-itSku"></div>');
            var wrote = false;
            function appendMetaBit(text, title) {
                if (!text) return;
                if (wrote) $meta.append(document.createTextNode(" · "));
                var $bit = $('<span></span>').text(text);
                if (title) $bit.attr("title", title);
                $meta.append($bit);
                wrote = true;
            }
            appendMetaBit(ln.ProductCode, ln.ProductCode);
            // The locator's own tooltip is the FULL locator (its combination),
            // which is what the reader loses when the sub-line truncates.
            if (ln.LocatorName) {
                appendMetaBit(VIS.Msg.getMsg("VAS_100_Locator") + " " + ln.LocatorName,
                    ln.LocatorName);
            }
            if (wrote) {
                $item.append($meta);
            } else if (ln.Description) {
                $item.append($('<div class="vas_100-itSku"></div>')
                    .attr("title", ln.Description).text(ln.Description));
            }

            // 4 — drop shipment. Always drawn, Yes or No: "No" is as much of an
            // answer as "Yes", and a line that reads nothing here would leave the
            // reader unable to tell an ordinary line from one the panel simply
            // failed to flag.
            var dropYes = !!ln.IsDropShip;
            var dropTxt = msg("VAS_100_DropShipment", "Drop Shipment") + ": " +
                (dropYes ? msg("VAS_100_Yes", "Yes") : msg("VAS_100_No", "No"));
            $item.append($('<div class="vas_100-itDrop"></div>')
                .toggleClass("vas_100-is-drop", dropYes)
                .attr("title", dropTxt).text(dropTxt));

            $tr.append($item);

            var prec = +ln.UOMPrecision || 0;

            // UOM
            $tr.append($('<span></span>').text(na(ln.UOMName)));

            // Ordered
            $tr.append($('<span class="vas_100-ta-r"></span>').text(formatNumber(+ln.OrderedQty || 0, prec)));

            // Delivered (mini bar + delivered qty)
            var ordered = +ln.OrderedQty || 0;
            var delivered = +ln.DeliveredQty || 0;
            var pct = ordered > 0 ? Math.min(100, Math.round((delivered / ordered) * 100)) : (delivered > 0 ? 100 : 0);
            var recvState = ordered > 0 && delivered >= ordered ? "vas_100-full" : (delivered > 0 ? "vas_100-part" : "vas_100-none");
            var $recv = $('<span class="vas_100-recv vas_100-ta-r"></span>').addClass(recvState);
            var $bar = $('<span class="vas_100-recvBar"><i></i></span>');
            $bar.find("i").css("width", Math.max(0, Math.min(100, pct)) + "%");
            $recv.append($bar);
            $recv.append(document.createTextNode(formatNumber(delivered, prec)));
            $tr.append($recv);

            // Line value
            $tr.append($('<span class="vas_100-ta-r"></span>').text(
                formatAmount(+ln.LineValue || 0, cur, data.StdPrecision)));

            // Status tag: Full / Partial / Short
            var $q = $('<span class="vas_100-ta-c"></span>');
            var tagCls, tagKey;
            if (ordered > 0 && delivered >= ordered) { tagCls = "vas_100-s-full"; tagKey = "VAS_100_Full"; }
            else if (delivered > 0)                  { tagCls = "vas_100-s-part"; tagKey = "VAS_100_Partial"; }
            else                                     { tagCls = "vas_100-s-short"; tagKey = "VAS_100_Short"; }
            $q.append($('<span class="vas_100-tag"></span>').addClass(tagCls)
                .text(VIS.Msg.getMsg(tagKey)));
            $tr.append($q);

            // Quality marker — omitted entirely when no line on this delivery has
            // QA parameters (the column itself is not rendered then).
            //
            // A tick or a cross rather than the words: the column is one of seven
            // in a side panel, and "Applicable" / "Non-Applicable" are long enough
            // that they ellipsise to the same few characters as each other. The
            // word still travels as the cell's tooltip and aria-label, so the mark
            // is never the only statement of what it means.
            if (showQuality) {
                var qOn = !!ln.QualityApplicable;
                var qWord = qOn ? msg("VAS_100_Applicable", "Applicable")
                                : msg("VAS_100_NonApplicable", "Non-Applicable");
                var $qm = $('<span class="vas_100-ta-c"></span>').attr("title", qWord);
                $qm.append($('<span class="vas_100-qMark"></span>')
                    .addClass(qOn ? "vas_100-q-on" : "vas_100-q-off")
                    .attr("aria-label", qWord)
                    .append(svgIcon(qOn ? "check" : "cross")));
                $tr.append($qm);
            }

            return $tr;
        }

        // ---------- Quality Product (VA010 inspection parameters) ---------- //

        // QC result code -> { key, fallback, tone }. "N" is a parameter that has
        // not been inspected yet, not a failure.
        var QC_STATUS_MAP = {
            "P": { key: "VAS_100_Passed",  fallback: "Passed",  tone: "vas_100-q-pass" },
            "F": { key: "VAS_100_Failed",  fallback: "Failed",  tone: "vas_100-q-fail" },
            "N": { key: "VAS_100_Pending", fallback: "Pending", tone: "vas_100-q-wait" }
        };

        // The delivery's quality parameters grouped by the LINE they belong to, so
        // each line can open its own. Built once per render.
        var qpByLine = {};

        function indexQualityParams() {
            qpByLine = {};
            var rows = (data && data.QualityParams) || [];
            for (var i = 0; i < rows.length; i++) {
                var key = rows[i].LineNo || 0;
                if (!qpByLine[key]) qpByLine[key] = [];
                qpByLine[key].push(rows[i]);
            }
        }

        // The parameters defined against one delivery line (empty when none — that
        // line then carries no expander).
        function qualityParamsFor(ln) {
            return (ln && qpByLine[ln.Line]) || [];
        }

        // True when ANY line of this delivery has QA parameters, which is what
        // decides whether the Quality column is drawn at all.
        function anyQualityLine() {
            var lines = (data && data.Lines) || [];
            for (var i = 0; i < lines.length; i++) {
                if (lines[i].QualityApplicable) return true;
            }
            return false;
        }

        function setQpToggleState($toggle, open) {
            $toggle.toggleClass("vas_100-is-open", !!open)
                .attr("title", open ? msg("VAS_100_HideQuality", "Hide quality parameters")
                                    : msg("VAS_100_ShowQuality", "Show quality parameters"));
        }

        // The quality parameters defined against ONE delivery line's product —
        // colour, size, grade or whatever the quality plan names — with the
        // acceptable value, the inspected value and the resulting verdict. Opens
        // under the line itself, collapsed until asked for, so the parameters and
        // the quantity they apply to are read together.
        //
        // Returns null for a line with no parameters — that line carries no
        // expander either.
        function buildQualityDrawer(ln) {
            var rows = qualityParamsFor(ln);
            if (!rows.length) return null;

            var open = !!lineQpOpen[ln.M_InOutLine_ID];
            var $drawer = $('<div class="vas_100-qpDrawer"></div>');
            if (!open) $drawer.hide();

            // Nothing has been inspected yet — these are the checks the
            // confirmation is going to raise, read from the plan. Said once, above
            // the table, rather than left for the reader to infer from a column of
            // Pending tags.
            var planned = true;
            for (var p = 0; p < rows.length; p++) {
                if (!rows[p].IsPlanned) { planned = false; break; }
            }
            if (planned) {
                $drawer.append($('<div class="vas_100-qpNote"></div>')
                    .text(msg("VAS_100_QualityExpected",
                              "Expected on confirmation — nothing inspected yet.")));
            }

            var $tbl = $('<div class="vas_100-qpMini"></div>');

            var $head = $('<div class="vas_100-qpMiniRow vas_100-qpMiniHead"></div>');
            $head.append($('<span></span>').text(msg("VAS_100_Parameter", "Parameter")));
            // Right-aligned: it is a quantity, and it reads against the line's own
            // Ordered / Delivered figures directly above it.
            $head.append($('<span class="vas_100-ta-r"></span>')
                .text(msg("VAS_100_ToVerify", "To Verify")));
            $head.append($('<span></span>').text(msg("VAS_100_AcceptableValue", "Acceptable")));
            $head.append($('<span></span>').text(msg("VAS_100_ActualValue", "Actual Value")));
            $head.append($('<span></span>').text(msg("VAS_100_QADate", "QA Date")));
            $head.append($('<span class="vas_100-ta-c"></span>').text(msg("VAS_100_Status", "Status")));
            $tbl.append($head);

            for (var i = 0; i < rows.length; i++) $tbl.append(buildQualityRow(rows[i]));

            $drawer.append($tbl);
            return $drawer;
        }

        function buildQualityRow(q) {
            var $tr = $('<div class="vas_100-qpMiniRow"></div>');

            var st = QC_STATUS_MAP[q.StatusCode] || QC_STATUS_MAP["N"];
            var statusText = msg(st.key, st.fallback);

            // Six columns inside a side panel's table: every cell here ellipsises,
            // and a parameter name or a value-list entry is exactly the kind of
            // text that runs past it. The whole row therefore carries the full set
            // as its tooltip, and the cells most likely to be clipped repeat their
            // own value on top of it, so a pointer resting on one column answers
            // for that column first.
            $tr.attr("title", qualityRowTooltip(q, statusText));

            // Parameter (Colour / Size / Grade ...), with the QA remark beneath it
            // when one was entered.
            var $param = $('<span class="vas_100-itItem"></span>');
            var paramName = na(q.ParameterName);
            $param.append($('<div class="vas_100-qpName"></div>')
                .text(paramName).attr("title", paramName));
            var remark = (q.Remark || "").trim();
            if (remark) {
                $param.append($('<div class="vas_100-itSku"></div>')
                    .text(remark).attr("title", remark));
            }
            $tr.append($param);

            // Quantity to verify.
            var toVerify = formatNumber(+q.QuantityToVerify || 0, 0);
            $tr.append($('<span class="vas_100-ta-r"></span>')
                .text(toVerify).attr("title", toVerify));

            // Acceptable / actual value.
            $tr.append($('<span></span>')
                .text(na(q.AcceptableValue)).attr("title", na(q.AcceptableValue)));
            $tr.append($('<span></span>')
                .text(na(q.ActualValue)).attr("title", na(q.ActualValue)));

            // QA / QC date.
            var qaDate = na(formatDate(q.QAQCDate));
            $tr.append($('<span></span>').text(qaDate).attr("title", qaDate));

            // Verdict.
            var $status = $('<span class="vas_100-ta-c"></span>');
            $status.append($('<span class="vas_100-tag"></span>')
                .addClass(st.tone).text(statusText));
            $tr.append($status);

            return $tr;
        }

        // The row's whole inspection, label by label, for its hover tooltip. Blank
        // fields are left out rather than printed as "Label: N/A" — a tooltip that
        // exists to recover clipped text should not be padded with the absence of
        // it.
        function qualityRowTooltip(q, statusText) {
            var bits = [];
            appendTipLine(bits, msg("VAS_100_Parameter", "Parameter"), q.ParameterName);
            appendTipLine(bits, msg("VAS_100_ToVerify", "To Verify"),
                formatNumber(+q.QuantityToVerify || 0, 0));
            appendTipLine(bits, msg("VAS_100_AcceptableValue", "Acceptable"), q.AcceptableValue);
            appendTipLine(bits, msg("VAS_100_ActualValue", "Actual Value"), q.ActualValue);
            appendTipLine(bits, msg("VAS_100_QADate", "QA Date"), formatDate(q.QAQCDate));
            appendTipLine(bits, msg("VAS_100_Status", "Status"), statusText);
            appendTipLine(bits, msg("VAS_100_Remark", "Remark"), q.Remark);
            return bits.join("\n");
        }

        function appendTipLine(bits, label, value) {
            var text = (value === null || value === undefined) ? "" : String(value).trim();
            if (!text) return;
            bits.push(label + ": " + text);
        }

        // ----------------------------------------------------------------- //
        //  Opening a referenced record                                       //
        // ----------------------------------------------------------------- //

        // Tables whose record does NOT open in the table's default zoom target,
        // mapped to the name of the window it does open. Anything not named here
        // falls back to VIS.ZoomTarget.
        var WINDOW_NAME_BY_TABLE = {
            "C_Project": "VAS_Project",
            // The Documents section's rows. Neither of these resolves through the
            // client's zoom target, so both would end at the "cannot open"
            // fallback on every click.
            "M_InOutConfirm": "VAS_ShipReceiptConfirm",
            "C_Payment":      "VAS_ARReceipt"
        };

        // The same map for records opened as a SALES transaction. C_Order and
        // M_InOut each serve both sides, and every one this panel opens is on
        // the sales side — the originating sales order and the reversed
        // delivery order — so both are named here.
        var WINDOW_NAME_BY_TABLE_SOTRX = {
            "C_Order": "VAS_SalesOrder",
            "M_InOut": "VAS_DeliveryOrder"
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
                    "VAS_100_OverviewDO/GetWindow_ID", windowName);
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
                var windowName = (isSOTrx && WINDOW_NAME_BY_TABLE_SOTRX[tableName])
                    ? WINDOW_NAME_BY_TABLE_SOTRX[tableName]
                    : WINDOW_NAME_BY_TABLE[tableName];
                var windowId = resolveWindowIdByName(windowName);

                if (windowId <= 0 &&
                    VIS.ZoomTarget && typeof VIS.ZoomTarget.getZoomAD_Window_ID === "function") {
                    // The 4th arg (IsSOTrx) picks the sales vs purchase window for
                    // dual-purpose tables like C_Order / M_InOut.
                    windowId = VIS.ZoomTarget.getZoomAD_Window_ID(tableName, 0, null, !!isSOTrx) || 0;
                }
                if (windowId > 0 && VIS.viewManager && typeof VIS.viewManager.startWindow === "function") {
                    var zoomQuery = VIS.Query.prototype.getEqualQuery(tableName + "_ID", +recordId);
                    VIS.viewManager.startWindow(windowId, zoomQuery);
                    return;
                }
            } catch (e) { console.log(e); }
            toast(msg("VAS_100_OpenRecord", "Open") + " " + tableName + " #" + recordId, false);
        }

        function toast(message, isError) {
            var $t = $('<div class="vas_100-toast"></div>')
                .addClass(isError ? "vas_100-err" : "vas_100-ok").text(message);
            $root.append($t);
            setTimeout(function () { $t.addClass("vas_100-show"); }, 10);
            setTimeout(function () {
                $t.removeClass("vas_100-show");
                setTimeout(function () { $t.remove(); }, 300);
            }, 3200);
        }

        // Delegated once on the root, so chips and rows rebuilt by render() stay
        // live. Both the Reference chips and the Documents rows carry the same
        // data-open-* attributes, so one handler serves them.
        function bindEvents() {
            $root.on("click", ".vas_100-chip.vas_100-is-link, .vas_100-is-link[data-open-table]",
                function (e) {
                    e.preventDefault();
                    openRecord($(this).attr("data-open-table"), $(this).attr("data-open-id"),
                        $(this).attr("data-open-sotrx") === "Y");
                });
        }

        // ----------------------------------------------------------------- //
        //  Icon helpers                                                      //
        // ----------------------------------------------------------------- //

        var SVG_ICONS = {
            pin:      '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M20 10c0 6-8 12-8 12s-8-6-8-12a8 8 0 0 1 16 0Z"/><circle cx="12" cy="10" r="3"/></svg>',
            truck:    '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M10 17h4V5H2v12h3"/><path d="M14 8h4l4 4v5h-3"/><circle cx="7.5" cy="17.5" r="1.5"/><circle cx="17.5" cy="17.5" r="1.5"/></svg>',
            box:      '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M21 8 12 3 3 8v8l9 5 9-5Z"/><path d="m3 8 9 5 9-5"/><path d="M12 13v8"/></svg>',
            coins:    '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><circle cx="8" cy="8" r="5"/><path d="M14.5 4.2a5 5 0 0 1 0 15.6"/><path d="M7 18.7a5 5 0 0 0 6 0"/></svg>',
            check:    '<svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"><path d="M20 6 9 17l-5-5"/></svg>',
            chevUp:   '<svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round"><path d="m18 15-6-6-6 6"/></svg>',
            doc:      '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8Z"/><path d="M14 2v6h6"/><path d="M8 13h8"/><path d="M8 17h8"/></svg>',
            clipboardCheck: '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><rect x="8" y="2" width="8" height="4" rx="1"/><path d="M16 4h2a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2h2"/><path d="m9 14 2 2 4-4"/></svg>',
            pencil:   '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M12 20h9"/><path d="M16.5 3.5a2.12 2.12 0 0 1 3 3L7 19l-4 1 1-4Z"/></svg>',
            arrowUpRight: '<svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M7 17 17 7M7 7h10v10"/></svg>',
            // The customer contact's phone bit.
            phone:    '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M22 16.92v3a2 2 0 0 1-2.18 2 19.79 19.79 0 0 1-8.63-3.07 19.5 19.5 0 0 1-6-6A19.79 19.79 0 0 1 2.12 4.18 2 2 0 0 1 4.11 2h3a2 2 0 0 1 2 1.72c.13.96.36 1.9.7 2.81a2 2 0 0 1-.45 2.11L8.09 9.91a16 16 0 0 0 6 6l1.27-1.27a2 2 0 0 1 2.11-.45c.9.34 1.85.57 2.81.7A2 2 0 0 1 22 16.92Z"/></svg>',
            // Quality: the line marker (tick / cross) and the two snapshot cards.
            cross:       '<svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"><path d="M18 6 6 18M6 6l12 12"/></svg>',
            checkCircle: '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="9"/><path d="m8.5 12.5 2.5 2.5 4.5-5"/></svg>',
            xCircle:     '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="9"/><path d="m15 9-6 6M9 9l6 6"/></svg>',
            // Activity rows.
            note:     '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8Z"/><path d="M14 2v6h6"/><path d="M8 13h8"/><path d="M8 17h6"/></svg>',
            mail:     '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="5" width="18" height="14" rx="2"/><path d="m3 7 9 6 9-6"/></svg>',
            alert:    '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M10.29 3.86 1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0Z"/><path d="M12 9v4"/><path d="M12 17h.01"/></svg>',
            chevLeft: '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round"><polyline points="15 18 9 12 15 6"/></svg>',
            chevRight: '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round"><polyline points="9 18 15 12 9 6"/></svg>'
        };

        function svgIcon(name) {
            var $wrap = $('<span class="vas_100-ic"></span>');
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

        function formatAmount(value, cur, precision) {
            var sign = value < 0 ? "-" : "";
            var abs = Math.abs(value);
            var p = (precision >= 0) ? precision : 2;
            var formatted = abs.toLocaleString(window.navigator.language, {
                minimumFractionDigits: p,
                maximumFractionDigits: p
            });
            return sign + (cur ? cur + " " : "") + formatted;
        }

        function formatDate(value) {
            if (!value) return "";
            var d = (value instanceof Date) ? value : new Date(value);
            if (isNaN(d.getTime())) return "";
            try {
                return d.toLocaleDateString(window.navigator.language, {
                    year: "numeric", month: "short", day: "2-digit"
                });
            } catch (e) {
                return d.toDateString();
            }
        }

        // Parses a genuine TIMESTAMP (the activity feed's moments) into a Date in
        // the viewer's own zone. The DB stores these in UTC and the server emits
        // no timezone designator, so the browser would read them as local and the
        // feed would print the stored UTC clock — a mail sent late in the evening
        // dating to the next morning. Tagging it "Z" renders it where the reader
        // is. A string already carrying "Z" or a ±hh:mm offset is left untouched.
        //
        // Date-only fields keep using formatDate above, which parses them as they
        // stand so their calendar day can never roll over.
        function parseStamp(value) {
            if (!value) return null;
            if (value instanceof Date) return isNaN(value.getTime()) ? null : value;
            var s = String(value);
            var hasTz = /(z|[+-]\d{2}:?\d{2})$/i.test(s);
            if (/^\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}/.test(s) && !hasTz) {
                s = s.replace(" ", "T") + "Z";
            }
            var d = new Date(s);
            return isNaN(d.getTime()) ? null : d;
        }

        function formatDateTime(value) {
            var d = parseStamp(value);
            if (!d) return "";
            try {
                var dp = d.toLocaleDateString(window.navigator.language, { month: "short", day: "2-digit" });
                var tp = d.toLocaleTimeString(window.navigator.language, { hour: "2-digit", minute: "2-digit" });
                return dp + ", " + tp;
            } catch (e) { return d.toString(); }
        }

        this.getRoot = function () {
            return $root;
        };
    };

    VAS.VAS_100_OverviewDO.prototype.startPanel = function (windowNo, curTab) {
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
    VAS.VAS_100_OverviewDO.prototype.refreshPanelData = function (recordID, selectedRow) {
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
    VAS.VAS_100_OverviewDO.prototype.sizeChanged = function (width) {
        this.panelWidth = width;
    };

    /* Release variables from memory */
    VAS.VAS_100_OverviewDO.prototype.dispose = function () {
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
