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
            // A different record starts at the top of its own line list.
            linesPage = 0;
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
            renderNotes();
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
            $pills.append(headerPill(st.label, st.tone, null, true));
            $top.append($pills);

            $strip.append($top);
            $body.append($strip);

            // --- Details card: customer identity (left) + dispatch fields (right) ---
            var $card = $('<section class="vas_100-hdrCard"></section>');

            // Left column is the CUSTOMER block only — name, location, first
            // name, e-mail, address. The dispatch fields that used to sit here
            // (packages / transport doc / vehicle / tracking) moved to the right
            // column so the two halves each read as one subject.
            var $left = $('<div class="vas_100-hdrColL"></div>');
            $left.append($('<div class="vas_100-fLabel"></div>').text(VIS.Msg.getMsg("VAS_100_Customer")));
            $left.append($('<div class="vas_100-vendName"></div>').text(na(data.CustomerName)));

            if (data.CustomerAddress) {
                var $addr = $('<div class="vas_100-vendAddr"></div>');
                $addr.append(svgIcon("pin"));
                $addr.append($('<span></span>').text(data.CustomerAddress));
                $left.append($addr);
            }

            var $custFields = $('<div class="vas_100-custFields"></div>');
            $custFields.append(headerField(msg("VAS_100_CustomerLocation", "Location"),
                na(data.CustomerLocationName), false));
            $custFields.append(headerField(msg("VAS_100_CustomerFirstName", "First Name"),
                na(data.CustomerFirstName), false));
            $custFields.append(headerField(msg("VAS_100_CustomerEmail", "Email Address"),
                na(data.CustomerEmail), false));
            $left.append($custFields);
            $card.append($left);

            // Right column: labelled dispatch / shipping fields. The sales order
            // lives in the Reference strip below (where it is clickable) and the
            // movement date is not repeated here.
            var $right = $('<div class="vas_100-hdrColR"></div>');
            $right.append(headerField(VIS.Msg.getMsg("VAS_100_Warehouse"), na(data.WarehouseName), false));
            $right.append(headerField(VIS.Msg.getMsg("VAS_100_Packages"),
                (data.PackageCount ? data.PackageCount + "" : VIS.Msg.getMsg("VAS_100_NA")), false));
            $right.append(headerField(VIS.Msg.getMsg("VAS_100_TransportDoc"),
                na(data.TransportDoc), false));
            $right.append(headerField(msg("VAS_100_ShippingMethod", "Shipping Method"),
                na(shippingMethodLabel(data.DeliveryViaRule)), false));
            $right.append(headerField(msg("VAS_100_Shipper", "Shipper"),
                na(data.ShipperName), false));
            $right.append(headerField(msg("VAS_100_VehicleNo", "Vehicle No"),
                na(data.VehicleNo), false));
            $right.append(headerField(msg("VAS_100_TrackingNo", "Tracking No"),
                na(data.TrackingNo), false));
            $card.append($right);

            $body.append($card);
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
            var $snap = $('<section class="vas_100-snap"></section>');
            var cur = currencyToken();

            // Delivery value.
            $snap.append(metricCard("total", "coins", VIS.Msg.getMsg("VAS_100_DeliveryValue"),
                formatAmount(+data.DeliveryValue || 0, cur, data.StdPrecision),
                data.ISO_Code || ""));

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

            $body.append($snap);
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

            var $tbl = $('<div class="vas_100-table"></div>');

            // Header row
            var $head = $('<div class="vas_100-tRow vas_100-tHead"></div>');
            $head.append($('<span></span>').text(msg("VAS_100_Line", "Line")));
            $head.append($('<span></span>').text(VIS.Msg.getMsg("VAS_100_UOM")));
            $head.append($('<span class="vas_100-ta-r"></span>').text(VIS.Msg.getMsg("VAS_100_Ordered")));
            $head.append($('<span class="vas_100-ta-r"></span>').text(VIS.Msg.getMsg("VAS_100_Delivered")));
            $head.append($('<span class="vas_100-ta-r"></span>').text(VIS.Msg.getMsg("VAS_100_LineValue")));
            $head.append($('<span class="vas_100-ta-c"></span>').text(VIS.Msg.getMsg("VAS_100_Status")));
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

                $tbl.find(".vas_100-tBody").remove();
                for (var i = start; i < end; i++) {
                    $foot.before(buildLineRow(lines[i], cur));
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

        // ---------- Notes (the description entered on the delivery order) ---------- //

        function renderNotes() {
            var text = (data && data.Description) || "";
            if (!String(text).trim()) return;

            var $sec = section(msg("VAS_100_Notes", "Notes"), null);
            $sec.append($('<div class="vas_100-noteCard"></div>').text(text));
        }

        function buildLineRow(ln, cur) {
            var $tr = $('<div class="vas_100-tRow vas_100-tBody"></div>');

            // Item (name + code / attribute / locator). The product code leads the
            // sub-line bare — the "SKU" caption in front of it was noise.
            var $item = $('<span class="vas_100-itItem"></span>');
            // Both the name and the locator clip to one line, so each carries its
            // own full text as a tooltip: hovering the product reads out the whole
            // product name, hovering the locator the whole locator.
            var productName = na(ln.ProductName);
            $item.append($('<div class="vas_100-itName"></div>')
                .attr("title", productName).text(productName));

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
            // Lot / serial / attributes the line was delivered against.
            appendMetaBit(ln.AttributeName, ln.AttributeName);
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

            return $tr;
        }

        // ----------------------------------------------------------------- //
        //  Opening a referenced record                                       //
        // ----------------------------------------------------------------- //

        // Tables whose record does NOT open in the table's default zoom target,
        // mapped to the name of the window it does open. Anything not named here
        // falls back to VIS.ZoomTarget.
        var WINDOW_NAME_BY_TABLE = {
            "C_Project": "VAS_Project"
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

        // Delegated once on the root, so chips rebuilt by render() stay live.
        function bindEvents() {
            $root.on("click", ".vas_100-chip.vas_100-is-link", function (e) {
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
