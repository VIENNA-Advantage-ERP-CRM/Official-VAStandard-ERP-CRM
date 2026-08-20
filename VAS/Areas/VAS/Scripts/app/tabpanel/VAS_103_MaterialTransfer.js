/************************************************************
 * Module Name    : VAS
 * Purpose        : Material Transfer Overview tab panel. Renders a review-
 *                  oriented overview of the selected stock movement
 *                  (M_Movement): header identity + transfer type / warehouse
 *                  details card, a From -> To route strip (source warehouse /
 *                  locator to destination warehouse / locator with a movement-
 *                  stage route tag), a "Generated from" origin-chip row
 *                  (Requisition / Production Order, muted when not linked), a
 *                  four-card KPI snapshot (lines, transfer quantity, transfer
 *                  value, confirmations), a five-node lifecycle stepper
 *                  (Drafted -> Confirmed -> Completed -> Received -> Posted) and
 *                  a transfer-lines table with per-line from/to locator,
 *                  quantity, value and source badge, plus visual action buttons
 *                  (Print / Receive Transfer — the latter disabled once posted).
 *                  Data is fetched from
 *                  VAS_103_MaterialTransfer/GetMaterialTransferOverview. All
 *                  on-screen strings are resolved through
 *                  VIS.Msg.getMsg("VAS_103_...").
 * Chronological development:
 *   VAI163   2026-07-07  Created
 *   VAI163   2026-08-05  Class prefix renamed MPC-vasmt- -> vas_103- so the panel's
 *                          styles cannot collide with another panel's.
 *   VAI163   2026-08-05  New Record / Copy Record now empty the panel instead
 *                        of leaving the previously selected record on screen.
 *                        Both refreshPanelData and a new data-status listener
 *                        ask isTabInserting(), which reads GridTab.gridTable
 *                        .getIsInserting() — the flag GridTable.dataNew() raises
 *                        for both actions. The record id cannot answer it: a
 *                        copied row carries the source record's key until saved.
 *                        Ported from VAS_092.
 *   VAI163   2026-08-07  Emits the vas_103-prefixed modifier classes the
 *                        stylesheet now uses, the runtime-built ones included
 *                        ("vas_103-tone-" + tone).
 *   VAI163   2026-08-12  Header:
 *                        - "Requested by" becomes "Created by", with the CREATED
 *                          stamp beside it. The field was always
 *                          M_Movement.CreatedBy, and calling it "Requested by"
 *                          named a role the movement does not record.
 *                        - The details card drops Transfer No (the title strip
 *                          already carries it) and Reference (which was the
 *                          document's Description, and now heads the Notes
 *                          section) for the Incoterm, and absorbs the Route
 *                          section — the From -> To pair is transfer IDENTITY, so
 *                          it belongs in the identity card rather than in a
 *                          standalone strip below it.
 *                        - The Transfer Value KPI card is gone, as is the Total
 *                          Value on the lines footer: a stock movement does not
 *                          change what the stock is worth, so a value total on it
 *                          invited a reading it cannot support.
 *                        - The Confirmations card shows only when the document
 *                          type actually raises confirmations
 *                          (IsConfirmationDocType, model side). It read "0" on
 *                          every ordinary transfer, which is not the same as
 *                          "this document has none to raise".
 *                        - A "Transfer Confirmed" pill once a confirmation has
 *                          been completed.
 *                        Generated from:
 *                        - The Requisition and Production Order chips NAME their
 *                          document and open it (openRecord), instead of reporting
 *                          a bare Linked / Not linked. A transfer raised from
 *                          neither still says so.
 *                        Lines:
 *                        - The product's code stands alone (the "SKU" prefix is
 *                          gone) and the line's ATTRIBUTES travel with it.
 *                        - The Source column is gone, and with it the "From
 *                          Manual (n)" breakdown on the footer: per line it was a
 *                          badge that read "Manual" on almost every row, and the
 *                          Generated From strip above already names the origin.
 *                        - A line with a confirmation opens a drawer of its
 *                          confirmation figures — confirmed, difference and
 *                          scrapped quantities, with the locator the scrap landed
 *                          in shown only when something actually was scrapped.
 *                        Lifecycle + bottom:
 *                        - The stepper is built from the document type and real
 *                          dates rather than five copies of MovementDate:
 *                          Drafted (created), Initiated (in-progress, confirmation
 *                          doc types only), Completed (non-confirmation types),
 *                          Confirmed and Received (confirmation types, both dated
 *                          by the confirmation's completion), Posted. Stages a
 *                          document can never reach are not drawn.
 *                        - Notes and Activity sections at the foot of the panel.
 *                        - The Print / Receive Transfer buttons are gone; both
 *                          were presentational and neither did anything.
 *   VAI163   2026-08-13  Activity pages at 15 rows (ACTIVITY_PER_PAGE), matching
 *                        the other overview panels. The pager is a sibling of
 *                        the list card so the controls keep their place while
 *                        the rows are replaced underneath them, and the
 *                        section's count badge still counts the WHOLE feed.
 *   VAI163   2026-08-14  Header:
 *                        - The route moved from the card's LEFT column to the
 *                          RIGHT, closing it under the document fields and
 *                          spanning both of their tracks, and is drawn a size
 *                          down (stylesheet). The warehouse pair is reference
 *                          detail beside those fields, not a headline of its own.
 *                        - The right column names the DOCUMENT TYPE (model side),
 *                          which the header could not answer before.
 *                        Lines:
 *                        - The confirmation toggle's tick is sized up
 *                          (stylesheet). It is the only mark distinguishing a
 *                          confirmed line and the control that opens its figures,
 *                          and at 0.85em it read as a speck beside the product
 *                          name.
 *                        Activity:
 *                        - Reports edits FIELD BY FIELD: an "Updated" row per
 *                          changed column (model side), headlined "Updated
 *                          <field>" with the line it landed on beneath it and
 *                          "when · by whom" where every other row carries it.
 *                          The feed used to say only that the transfer had been
 *                          created, completed or confirmed — and its "Completed"
 *                          row is dated from M_Movement.Updated, so the nearest
 *                          thing to an edit trail was one row carrying the LAST
 *                          save's timestamp and nothing about what it touched.
 *   VAI163   2026-08-17  Activity's field-level rows carry the MOVE: "was X →
 *                        now Y" under the field's name (changeDelta), the old
 *                        value struck through and a value the log recorded as
 *                        empty shown as an em dash, so a cleared field is
 *                        visibly cleared rather than looking like a rendering
 *                        gap. A row said WHICH field moved but never what it
 *                        moved from or to.
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

    VAS.VAS_103_MaterialTransfer = function () {
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
        // Which lines have their confirmation drawer open, keyed by
        // M_MovementLine_ID. Kept outside render() so a repaint of the same record
        // leaves the reader where they were; reset with the record.
        var openConfirmLines = {};

        this.init = function () {
            $root = $('<div class="vas_103-root"></div>');
            $body = $('<div class="vas_103-body"></div>');
            $emptyState = $('<div class="vas_103-empty" style="display:none;"></div>');
            $emptyState.text(VIS.Msg.getMsg("VAS_103_NoData"));
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
            // Open drawers belong to the record that was on screen; a different
            // transfer's line ids mean nothing here.
            openConfirmLines = {};
            // A different record starts at the top of its own feed.
            activityPage = 0;
            showBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + "VAS_103_MaterialTransfer/GetMaterialTransferOverview",
                type: "GET",
                dataType: "json",
                data: { M_Movement_ID: recordID },
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
            openConfirmLines = {};
            render();
        };

        function render() {
            $body.empty();

            if (!data || !data.M_Movement_ID) {
                $body.hide();
                $emptyState.show();
                return;
            }

            $emptyState.hide();
            $body.show();

            // The Route strip is gone as a section of its own — the From -> To
            // pair is transfer identity and is drawn inside the header card.
            renderHeader();
            renderGeneratedFrom();
            renderSnapshot();
            renderTimeline();
            renderLines();
            // Notes and Activity close the panel: both are commentary on
            // everything above them, so they read last.
            renderNotes();
            renderActivity();
        }

        // ----------------------------------------------------------------- //
        //  Small builders                                                    //
        // ----------------------------------------------------------------- //

        function section(title, opts, $parent) {
            opts = opts || {};
            var $sec = $('<section class="vas_103-sec"></section>');
            var $head = $('<div class="vas_103-secHead"></div>');
            $head.append($('<h2 class="vas_103-secTitle"></h2>').text(title));
            if (opts.summary) {
                $head.append($('<span class="vas_103-secSummary"></span>').text(opts.summary));
            }
            if (opts.$right) $head.append(opts.$right);
            $sec.append($head);
            ($parent || $body).append($sec);
            return $sec;
        }

        function na(value) {
            return (value === null || value === undefined || String(value).trim() === "")
                ? VIS.Msg.getMsg("VAS_103_NA")
                : value;
        }

        // The AD_Message keys this panel gained are not seeded on every
        // deployment; without a fallback they would render as the raw key. The
        // seeded ones keep going through VIS.Msg.getMsg directly.
        function getMsg(key, fallback) {
            try {
                var m = VIS.Msg.getMsg(key);
                if (m && m !== key) return m;
            } catch (e) { }
            return fallback != null ? fallback : key;
        }

        // True when this document type is the kind that raises a movement
        // confirmation (C_DocType.IsInTransit). Everything confirmation-shaped on
        // the panel — the Confirmations card, the Confirmed / Received lifecycle
        // stages — exists only for these.
        function isConfirmType() {
            return !!(data && data.IsConfirmationDocType);
        }

        function currencyToken() {
            return (data && (data.CurSymbol || data.ISO_Code)) || "₹";
        }

        // ---------- Status map (DocStatus code -> label + tone) ---------- //

        var STATUS_MAP = {
            "DR": { key: "VAS_103_Drafted",             tone: "neutral" },
            "IP": { key: "VAS_103_InProgress",          tone: "info" },
            "AP": { key: "VAS_103_Approved",            tone: "info" },
            "CO": { key: "VAS_103_Completed",           tone: "success" },
            "CL": { key: "VAS_103_Closed",              tone: "success" },
            "VO": { key: "VAS_103_Voided",              tone: "risk" },
            "RE": { key: "VAS_103_Reversed",            tone: "risk" },
            "WC": { key: "VAS_103_WaitingConfirmation", tone: "warning" },
            "WP": { key: "VAS_103_WaitingPayment",      tone: "warning" },
            "IN": { key: "VAS_103_Invalid",             tone: "risk" },
            "NA": { key: "VAS_103_NotApproved",         tone: "risk" }
        };

        function statusMeta(code) {
            var m = STATUS_MAP[code];
            if (m) return { label: VIS.Msg.getMsg(m.key), tone: m.tone };
            return { label: na(code), tone: "neutral" };
        }

        function transferTypeLabel() {
            return (data.TransferTypeCode === "INTRA")
                ? VIS.Msg.getMsg("VAS_103_IntraWarehouse")
                : VIS.Msg.getMsg("VAS_103_InterWarehouse");
        }

        // Movement-stage booleans.
        //
        // "confirmed" is the CONFIRMATION document being completed, not the
        // movement leaving Drafted — it used to be `code !== "DR"`, so a transfer
        // that had merely been submitted reported itself confirmed, and "received"
        // was the mere EXISTENCE of a confirmation record, which is raised the
        // moment the transfer completes and says nothing about the goods arriving.
        // Both now come from the confirmation's own completion (model side), and a
        // document type that raises no confirmation reaches neither.
        function stageFlags() {
            var code = data.StatusCode;
            var completed = data.Processed || code === "CO" || code === "CL";
            var confirmed = isConfirmType() && !!data.IsConfirmationCompleted;
            return {
                drafted:   true,
                initiated: completed || code === "IP" || code === "AP" || code === "WC",
                completed: completed,
                confirmed: confirmed,
                // The goods are received when the confirmation that records their
                // arrival is completed — the same event, so the same date.
                received:  confirmed,
                posted:    !!data.Posted
            };
        }

        // Route tag reflecting the current movement stage.
        function routeStageLabel() {
            var f = stageFlags();
            if (data.TransferTypeCode === "INTRA") return VIS.Msg.getMsg("VAS_103_IntraWH");
            if (f.posted)   return VIS.Msg.getMsg("VAS_103_Posted");
            if (f.received) return VIS.Msg.getMsg("VAS_103_Received");
            if (f.completed) return VIS.Msg.getMsg("VAS_103_Dispatched");
            if (f.confirmed) return VIS.Msg.getMsg("VAS_103_Picked");
            return VIS.Msg.getMsg("VAS_103_Drafted");
        }

        // lineOrigin() is gone with the Source column it fed. It classified each
        // line as Requisition / Production Order / Manual, and on almost every
        // transfer that was a column of identical "Manual" badges — while the
        // Generated From strip above already names where the transfer came from.

        // ---------- Header (title strip + details card) ---------- //

        function renderHeader() {
            var st = statusMeta(data.StatusCode);

            var $strip = $('<section class="vas_103-hdr"></section>');
            var $top = $('<div class="vas_103-hdrTop"></div>');

            var $tl = $('<div class="vas_103-hdrTitleWrap"></div>');
            $tl.append($('<div class="vas_103-hdrEyebrow"></div>').text(VIS.Msg.getMsg("VAS_103_MaterialTransfer")));
            $tl.append($('<div class="vas_103-hdrTitle"></div>').text(na(data.DocumentNo)));

            var subBits = [];
            var moved = formatDate(data.MovementDate);
            if (moved) subBits.push(VIS.Msg.getMsg("VAS_103_MovementDate") + " " + moved);
            // Who raised it and when — the field was always M_Movement.CreatedBy,
            // so it is named for what it is.
            if (data.CreatedByName) {
                subBits.push(getMsg("VAS_103_CreatedBy", "Created by") + " " + data.CreatedByName);
            }
            var createdOn = formatDate(data.CreatedOn);
            if (createdOn) subBits.push(getMsg("VAS_103_CreatedOn", "Created on") + " " + createdOn);
            if (subBits.length) {
                $tl.append($('<div class="vas_103-hdrSub"></div>').text(subBits.join(" · ")));
            }
            $top.append($tl);

            var $pills = $('<div class="vas_103-hdrPills"></div>');
            $pills.append(headerPill(transferTypeLabel(), "info", "transfer", false));
            // A completed confirmation is the milestone that says the goods
            // actually arrived, so it earns a pill of its own.
            if (data.IsConfirmationCompleted) {
                $pills.append(headerPill(
                    getMsg("VAS_103_TransferConfirmed", "Transfer Confirmed"), "success", "check", false));
            }
            if (data.Posted) {
                $pills.append(headerPill(VIS.Msg.getMsg("VAS_103_Posted"), "success", "check", false));
            }
            $pills.append(headerPill(st.label, st.tone, null, true));
            $top.append($pills);

            $strip.append($top);
            $body.append($strip);

            // --- Details card: transfer identity (left) + document fields (right) ---
            var $card = $('<section class="vas_103-hdrCard"></section>');

            var $left = $('<div class="vas_103-hdrColL"></div>');
            $left.append($('<div class="vas_103-fLabel"></div>').text(VIS.Msg.getMsg("VAS_103_TransferType")));
            $left.append($('<div class="vas_103-vendName"></div>').text(transferTypeLabel()));

            var $contact = $('<div class="vas_103-vendContact"></div>');
            appendContactBit($contact, "user", data.CreatedByName);
            appendContactBit($contact, "calendar", formatDate(data.MovementDate));
            if ($contact.children().length) $left.append($contact);

            $card.append($left);

            // Transfer No is gone (the title strip above already carries it) and so
            // is Reference, which was the document's Description — that text is a
            // note, and it heads the Notes section at the foot of the panel now.
            var $right = $('<div class="vas_103-hdrColR"></div>');
            // The document type the transfer was raised on — which movement this
            // is, and the one field the header could not answer.
            $right.append(headerField(getMsg("VAS_103_DocumentType", "Document Type"),
                na(data.DocTypeName), false));
            $right.append(headerField(getMsg("VAS_103_Incoterm", "Incoterm"),
                na(data.IncotermName), false));
            $right.append(headerField(VIS.Msg.getMsg("VAS_103_Posted"),
                data.Posted ? VIS.Msg.getMsg("VAS_103_Posted")
                            : VIS.Msg.getMsg("VAS_103_NotPosted"), false));

            // The route closes the right column, spanning both of its field tracks
            // (stylesheet). It sat in the LEFT column beside the transfer type;
            // moved here it reads under the document fields it belongs with, and
            // the left column is left to identity alone.
            $right.append(buildRoute());
            $card.append($right);

            $body.append($card);
        }

        function headerPill(label, tone, icon, withDot) {
            var $p = $('<span class="vas_103-hdrPill"></span>')
                .addClass("vas_103-tone-" + (tone || "neutral"));
            if (icon) $p.append(svgIcon(icon));
            if (withDot) $p.append($('<span class="vas_103-hdrDot"></span>'));
            $p.append($('<span></span>').text(label));
            return $p;
        }

        function headerField(label, value, link) {
            var $f = $('<div class="vas_103-hdrField"></div>');
            $f.append($('<div class="vas_103-fLabel"></div>').text(label));
            var $v = $('<div class="vas_103-fVal"></div>').text(value);
            if (link) $v.addClass("vas_103-is-link");
            $f.append($v);
            return $f;
        }

        function appendContactBit($container, icon, value) {
            if (!value) return;
            var $bit = $('<span class="vas_103-contactBit"></span>');
            $bit.append(svgIcon(icon));
            $bit.append($('<span></span>').text(value));
            $container.append($bit);
        }

        // ---------- Route (From -> To), inside the header card ---------- //

        // Was a section of its own beneath the card, then the card's LEFT column.
        // It now closes the RIGHT column, spanning both field tracks, and is drawn
        // a size down from the standalone strip it began as — the warehouse pair
        // is reference detail beside the document fields, not a headline of its
        // own, and at the old size it dominated a card it merely sits in.
        function buildRoute() {
            var $route = $('<div class="vas_103-route"></div>');
            $route.append(routeNode(VIS.Msg.getMsg("VAS_103_From"), na(data.FromWarehouseName)));

            var $arrow = $('<div class="vas_103-routeArrow"></div>');
            $arrow.append($('<span class="vas_103-routeTag"></span>').text(routeStageLabel()));
            $arrow.append(svgIcon("arrow"));
            $route.append($arrow);

            $route.append(routeNode(VIS.Msg.getMsg("VAS_103_To"), na(data.ToWarehouseName)));
            return $route;
        }

        function routeNode(label, warehouse) {
            var $n = $('<div class="vas_103-routeNode"></div>');
            $n.append($('<div class="vas_103-routeLbl"></div>').text(label));
            $n.append($('<div class="vas_103-routeWh"></div>').text(warehouse));
            return $n;
        }

        // ---------- Generated from (origin chips) ---------- //

        function renderGeneratedFrom() {
            var $sec = section(VIS.Msg.getMsg("VAS_103_GeneratedFrom"), null);
            var $row = $('<div class="vas_103-origins"></div>');

            // A chip is only drawn for an origin the transfer actually has, and it
            // NAMES that document and opens it. Both used to be drawn always, one
            // of them reading "Not linked" — the absence of a link is not news, and
            // "Linked" without a document number could not be acted on.
            var any = false;
            if (data.HasRequisition && data.RequisitionNo) {
                $row.append(originChip(VIS.Msg.getMsg("VAS_103_Requisition"), data.RequisitionNo,
                    data.RequisitionCount, "M_Requisition", data.RequisitionId, "VAS_Requisition"));
                any = true;
            }
            if (data.HasWorkOrder) {
                // The document number needs the VAMFG module to be readable; the
                // chip still opens the record without it.
                $row.append(originChip(VIS.Msg.getMsg("VAS_103_ProductionOrder"),
                    data.WorkOrderNo || ("#" + data.WorkOrderId), data.WorkOrderCount,
                    "VAMFG_M_WorkOrder", data.WorkOrderId, null));
                any = true;
            }
            if (!any) {
                $row.append($('<span class="vas_103-originChip vas_103-is-muted"></span>')
                    .append(svgIcon("link"))
                    .append($('<span class="vas_103-originLbl"></span>')
                        .text(getMsg("VAS_103_ManualTransfer", "Raised manually"))));
            }
            $sec.append($row);
        }

        // label + the document's own number, opening that record on click. `count`
        // above 1 says how many more of the same kind the lines carry.
        function originChip(label, value, count, tableName, recordId, windowName) {
            var $c = $('<span class="vas_103-originChip vas_103-is-linked"></span>');
            $c.append(svgIcon("link"));
            $c.append($('<span class="vas_103-originLbl"></span>').text(label));
            $c.append($('<span class="vas_103-originState"></span>').text(value));
            if (+count > 1) {
                $c.append($('<span class="vas_103-originMore"></span>')
                    .text("+" + (+count - 1)));
            }
            if (tableName && +recordId > 0) {
                $c.addClass("vas_103-is-openable")
                  .attr("title", getMsg("VAS_103_OpenRecord", "Open") + " " + value)
                  .on("click", function () { openRecord(tableName, recordId, windowName); });
            }
            return $c;
        }

        // ---------- Snapshot (KPI metric grid) ---------- //

        function renderSnapshot() {
            var $snap = $('<section class="vas_103-snap"></section>');

            // Lines.
            $snap.append(metricCard("lines", "layers", VIS.Msg.getMsg("VAS_103_Lines"),
                (data.LineCount || 0) + "", VIS.Msg.getMsg("VAS_103_OnThisTransfer")));

            // Transfer quantity.
            $snap.append(metricCard("qty", "box", VIS.Msg.getMsg("VAS_103_TransferQty"),
                formatNumber(+data.TransferQty || 0, 0), VIS.Msg.getMsg("VAS_103_UnitsMoved")));

            // The Transfer Value card is gone. Moving stock between locators does
            // not change what it is worth, so a money total on a transfer invited a
            // reading the document cannot support — and the figure came from
            // whichever optional cost column the schema happened to carry.

            // Confirmations, but only for a document type that actually raises
            // them. It reported "0" on every ordinary transfer, which is not the
            // same as "this document has none to raise".
            if (isConfirmType()) {
                $snap.append(metricCard("confirm", "check", VIS.Msg.getMsg("VAS_103_Confirmations"),
                    (data.ConfirmationCount || 0) + "", VIS.Msg.getMsg("VAS_103_ReceiptRecords")));
            }

            // The grid tracks the cards actually drawn — two or three.
            $snap.addClass(isConfirmType() ? "vas_103-cards-3" : "vas_103-cards-2");
            $body.append($snap);
        }

        function metricCard(tone, icon, label, value, sub) {
            var $c = $('<div class="vas_103-metric"></div>').addClass("vas_103-tone-" + tone);

            var $head = $('<div class="vas_103-mHead"></div>');
            $head.append(svgIcon(icon));
            $head.append($('<span class="vas_103-mLabel"></span>').text(label));
            $c.append($head);

            $c.append($('<div class="vas_103-mVal"></div>').text(value));
            if (sub) $c.append($('<div class="vas_103-mSub"></div>').text(sub));
            return $c;
        }

        // ---------- Lifecycle stepper (5 nodes) ---------- //

        // The stages this document can actually reach, each with the date it
        // actually happened on.
        //
        // Every stage used to be dated by MovementDate — one document field
        // repeated five times, which said nothing about when anything occurred —
        // and every document was given all five stages whether or not it could ever
        // reach them. The shape follows the document type:
        //
        //   Drafted    always, dated by the record's creation
        //   Initiated  confirmation types only, once the document is in progress
        //   Completed  non-confirmation types only, dated by the workflow's
        //              DocComplete stamp (the same rule the PO overview follows)
        //   Confirmed  confirmation types only, dated by the CONFIRMATION's
        //              completion
        //   Received   confirmation types only — the confirmation is what records
        //              the arrival, so it carries the same date
        //   Posted     always, dated by when posting actually ran (Fact_Acct)
        function lifecycleStages() {
            var f = stageFlags();
            var conf = isConfirmType();
            var stages = [];

            stages.push({ key: "VAS_103_Drafted", label: "Drafted",
                          done: f.drafted, date: data.CreatedOn });

            if (conf) {
                stages.push({ key: "VAS_103_Initiated", label: "Initiated",
                              done: f.initiated, date: data.CompletedDate });
            } else {
                stages.push({ key: "VAS_103_Completed", label: "Completed",
                              done: f.completed, date: data.CompletedDate });
            }

            if (conf) {
                stages.push({ key: "VAS_103_Confirmed", label: "Confirmed",
                              done: f.confirmed, date: data.ConfirmedDate });
                stages.push({ key: "VAS_103_Received", label: "Received",
                              done: f.received, date: data.ConfirmedDate });
            }

            stages.push({ key: "VAS_103_Posted", label: "Posted",
                          done: f.posted, date: data.PostedDate });
            return stages;
        }

        function renderTimeline() {
            var stages = lifecycleStages();

            var activeIdx = -1;
            for (var k = 0; k < stages.length; k++) { if (stages[k].done) activeIdx = k; }

            var $sec = section(VIS.Msg.getMsg("VAS_103_Lifecycle"), null);

            var $tl = $('<div class="vas_103-stepper"></div>');
            for (var i = 0; i < stages.length; i++) {
                var s = stages[i];
                var stateCls, metaText;
                if (s.done) {
                    stateCls = "vas_103-is-done";
                    // A stage that happened but whose date cannot be resolved says
                    // "Done" rather than borrowing another stage's date.
                    metaText = formatDate(s.date) || VIS.Msg.getMsg("VAS_103_Done");
                } else if (i === activeIdx + 1) {
                    stateCls = "vas_103-is-active";
                    metaText = VIS.Msg.getMsg("VAS_103_Pending");
                } else {
                    stateCls = "is-pending";
                    metaText = VIS.Msg.getMsg("VAS_103_Pending");
                }
                $tl.append(stepEntry(i + 1, getMsg(s.key, s.label), metaText, s.done, stateCls));
            }
            $sec.append($tl);
        }

        function stepEntry(num, title, meta, done, stateCls) {
            var $entry = $('<div class="vas_103-step"></div>').addClass(stateCls || "");

            var $rail = $('<div class="vas_103-stepRail"></div>');
            $rail.append($('<span class="vas_103-stepLine vas_103-stepLine-l"></span>'));
            var $dot = $('<span class="vas_103-stepDot"></span>');
            if (done) { $dot.append(svgIcon("check")); } else { $dot.text(num); }
            $rail.append($dot);
            $rail.append($('<span class="vas_103-stepLine vas_103-stepLine-r"></span>'));
            $entry.append($rail);

            var $lbl = $('<div class="vas_103-stepLabel"></div>');
            $lbl.append($('<div class="vas_103-stepTitle"></div>').text(title));
            if (meta) $lbl.append($('<div class="vas_103-stepMeta"></div>').text(meta));
            $entry.append($lbl);

            return $entry;
        }

        // ---------- Transfer lines (table) ---------- //

        function renderLines() {
            var lines = (data && data.Lines) || [];
            if (!lines.length) return;

            var $sec = section(VIS.Msg.getMsg("VAS_103_TransferLines"), {
                summary: (data.LineCount || 0) + " " + VIS.Msg.getMsg("VAS_103_Items")
            });

            $sec.append(buildLinesTable());
        }

        function buildLinesTable() {
            var lines = (data && data.Lines) || [];
            var cur = currencyToken();

            var $tbl = $('<div class="vas_103-table"></div>');

            // No Source column: per line it was a badge reading "Manual" on almost
            // every row, and the Generated From strip above already names where the
            // transfer came from.
            var $head = $('<div class="vas_103-tRow vas_103-tHead"></div>');
            $head.append($('<span></span>').text(VIS.Msg.getMsg("VAS_103_Item")));
            $head.append($('<span></span>').text(VIS.Msg.getMsg("VAS_103_FromLocator")));
            $head.append($('<span></span>').text(VIS.Msg.getMsg("VAS_103_ToLocator")));
            $head.append($('<span></span>').text(VIS.Msg.getMsg("VAS_103_UOM")));
            $head.append($('<span class="vas_103-ta-r"></span>').text(VIS.Msg.getMsg("VAS_103_Quantity")));
            $head.append($('<span class="vas_103-ta-r"></span>').text(VIS.Msg.getMsg("VAS_103_Value")));
            $tbl.append($head);

            var totQty = 0;

            for (var i = 0; i < lines.length; i++) {
                var ln = lines[i];
                totQty += (+ln.MovementQty || 0);
                $tbl.append(buildLineRow(ln, cur));
                // The line's confirmation figures, collapsed beneath it. Only a
                // line that HAS a confirmation gets one.
                var $drawer = buildConfirmDrawer(ln);
                if ($drawer) $tbl.append($drawer);
            }

            // Totals footer. The source breakdown is gone with the column it
            // summarised — "From Manual (n)" was a count of lines with nothing
            // linked to them, which is not an origin. So is Total Value: moving
            // stock does not change what it is worth.
            var $foot = $('<div class="vas_103-tFoot"></div>');
            var $qty = $('<span class="vas_103-tf vas_103-is-grand"></span>');
            $qty.append(document.createTextNode(VIS.Msg.getMsg("VAS_103_TotalQty")));
            $qty.append($('<b></b>').text(formatNumber(totQty, 0)));
            $foot.append($qty);
            $tbl.append($foot);

            return $tbl;
        }

        function buildLineRow(ln, cur) {
            var $tr = $('<div class="vas_103-tRow vas_103-tBody"></div>');

            // Item: the product, its code and its attributes.
            var $item = $('<span class="vas_103-itItem"></span>');

            var $nameRow = $('<div class="vas_103-itNameRow"></div>');
            $nameRow.append($('<span class="vas_103-itName"></span>').text(na(ln.ProductName)));
            // The toggle for the line's confirmation figures, on the name itself.
            var $cBtn = buildConfirmToggle(ln);
            if ($cBtn) $nameRow.append($cBtn);
            $item.append($nameRow);

            // The product's code, alone. It was prefixed with the word "SKU",
            // which belongs to a column header, not to every row of the table.
            var subBits = [];
            if (ln.ProductCode) subBits.push(ln.ProductCode);
            // Attributes travel with the product: a lot / serial / attribute set is
            // what distinguishes two rows of the same item from each other.
            if (ln.AttributeSetInstance) subBits.push(ln.AttributeSetInstance);
            if (!subBits.length && ln.Description) subBits.push(ln.Description);
            if (subBits.length) {
                var sub = subBits.join(" · ");
                $item.append($('<div class="vas_103-itSku"></div>').text(sub).attr("title", sub));
            }
            $tr.append($item);

            var prec = +ln.UOMPrecision || 0;

            // From locator
            $tr.append($('<span></span>').text(na(ln.FromLocatorName)));

            // To locator
            $tr.append($('<span></span>').text(na(ln.ToLocatorName)));

            // UOM
            $tr.append($('<span></span>').text(na(ln.UOMName)));

            // Quantity
            $tr.append($('<span class="vas_103-ta-r"></span>').text(formatNumber(+ln.MovementQty || 0, prec)));

            // Value
            $tr.append($('<span class="vas_103-ta-r"></span>').text(
                formatAmount(+ln.LineValue || 0, cur, data.StdPrecision)));

            return $tr;
        }

        // ---------- Per-line confirmation figures ---------- //

        // The toggle that opens a line's confirmation figures, sitting on the
        // product name. Returns null for a line with no confirmation, so an
        // unconfirmed line carries no control at all.
        function buildConfirmToggle(ln) {
            if (!ln.HasConfirm) return null;

            var id = ln.M_MovementLine_ID;
            var open = !!openConfirmLines[id];
            var $b = $('<span class="vas_103-cfBtn"></span>')
                .toggleClass("vas_103-is-open", open)
                .attr("title", getMsg("VAS_103_ConfirmationStatus", "Confirmation status"));
            $b.append(svgIcon("check"));
            $b.on("click", function (e) {
                e.stopPropagation();
                var nowOpen = !openConfirmLines[id];
                if (nowOpen) openConfirmLines[id] = true; else delete openConfirmLines[id];
                $b.toggleClass("vas_103-is-open", nowOpen);
                $b.closest(".vas_103-tBody").next(".vas_103-cfDrawer").toggle(nowOpen);
            });
            return $b;
        }

        // The drawer: what the confirmation actually recorded against this line —
        // confirmed, difference and scrapped quantities, with the locator the scrap
        // landed in shown only when something was scrapped. A sibling of the line
        // row rather than a child, so it can span the table's full width instead of
        // living inside one grid cell.
        function buildConfirmDrawer(ln) {
            if (!ln.HasConfirm) return null;

            var open = !!openConfirmLines[ln.M_MovementLine_ID];
            var prec = +ln.UOMPrecision || 0;

            var $d = $('<div class="vas_103-cfDrawer"></div>');
            if (!open) $d.hide();

            var $grid = $('<div class="vas_103-cfGrid"></div>');
            $grid.append(confirmBit(getMsg("VAS_103_ConfirmTarget", "Target"),
                formatNumber(+ln.ConfirmTargetQty || 0, prec), null));
            $grid.append(confirmBit(getMsg("VAS_103_ConfirmedQty", "Confirmed Qty"),
                formatNumber(+ln.ConfirmedQty || 0, prec), "vas_103-cf-ok"));

            // A difference is only interesting when there IS one, but it is always
            // shown: a zero difference is the reassurance the reader came for.
            var diff = +ln.DifferenceQty || 0;
            $grid.append(confirmBit(getMsg("VAS_103_DifferenceQty", "Difference"),
                signedNumber(diff, prec), diff !== 0 ? "vas_103-cf-warn" : null));

            var scrap = +ln.ScrappedQty || 0;
            var $scrap = confirmBit(getMsg("VAS_103_ScrapQty", "Scrap Qty"),
                formatNumber(scrap, prec), scrap > 0 ? "vas_103-cf-risk" : null);
            // The scrap locator only means something where something was actually
            // scrapped, so it sits under the figure and only then.
            if (scrap > 0 && ln.ScrapLocatorName) {
                $scrap.append($('<span class="vas_103-cfLoc"></span>')
                    .text(ln.ScrapLocatorName)
                    .attr("title", getMsg("VAS_103_ScrapLocator", "Scrap locator") + ": " + ln.ScrapLocatorName));
            }
            $grid.append($scrap);

            $d.append($grid);
            return $d;
        }

        function confirmBit(label, value, toneCls) {
            var $b = $('<div class="vas_103-cfBit"></div>');
            $b.append($('<span class="vas_103-cfLbl"></span>').text(label));
            $b.append($('<span class="vas_103-cfVal"></span>')
                .addClass(toneCls || "").text(value));
            return $b;
        }

        // Signed number: "+N" for positive, "−N" for negative, "0" for zero.
        function signedNumber(value, precision) {
            var v = +value || 0;
            if (v === 0) return formatNumber(0, precision);
            return (v > 0 ? "+" : "−") + formatNumber(Math.abs(v), precision);
        }

        // ---------- Actions ---------- //

        // The Print and Receive Transfer buttons are gone, with actionButton().
        // Both were presentational only: neither was wired to anything, so the
        // panel offered two controls that did nothing on every record. Printing and
        // receiving belong to the transfer window, where each runs behind the
        // document's own validation.

        // ---------- Notes ---------- //

        function renderNotes() {
            var rows = (data && data.Notes) || [];
            if (!rows.length) return;

            var $sec = section(getMsg("VAS_103_Notes", "Notes"), { summary: rows.length + "" });

            var $card = $('<div class="vas_103-panelcard"></div>');
            for (var i = 0; i < rows.length; i++) {
                var n = rows[i];
                var $n = $('<div class="vas_103-noteRow"></div>');
                $n.append($('<span class="vas_103-noteTag"></span>')
                    .addClass(n.NoteType === "header" ? "vas_103-n-header" : "vas_103-n-line")
                    .text(n.NoteType === "header"
                        ? getMsg("VAS_103_NoteHeader", "Document")
                        : getMsg("VAS_103_NoteLine", "Line")));
                $n.append($('<span class="vas_103-noteTxt"></span>').text(n.Text || ""));
                $card.append($n);
            }
            $sec.append($card);
        }

        // ---------- Activity ---------- //

        var ACT_TYPES = {
            Note:         { tone: "info",    icon: "note",   label: "Note" },
            Created:      { tone: "neutral", icon: "plus",   label: "Created" },
            Completed:    { tone: "success", icon: "check",  label: "Completed" },
            Confirmation: { tone: "warning", icon: "clock",  label: "Confirmation" },
            Confirmed:    { tone: "success", icon: "check",  label: "Confirmed" },
            // One row per FIELD that changed, not one per save (model side).
            Updated:      { tone: "info",    icon: "pencil", label: "Updated" }
        };

        // Maximum activity rows shown per page; the feed paginates beyond this.
        // A transfer accumulates every status change, confirmation and note, and
        // an unpaged feed made the section scroll past everything below it. The
        // section's own count badge still counts the WHOLE feed, not the page.
        var ACTIVITY_PER_PAGE = 15;
        var activityPage = 0;   // current Activity page (0-based)

        function renderActivity() {
            var rows = (data && data.Activity) || [];
            if (!rows.length) return;

            var $sec = section(getMsg("VAS_103_Activity", "Activity"), { summary: rows.length + "" });

            var $card = $('<div class="vas_103-panelcard"></div>');
            $sec.append($card);

            // The pager is a sibling of the card, so it keeps its place while the
            // card's rows are replaced underneath it.
            var $pager = $('<div class="vas_103-pager"></div>');
            if (rows.length > ACTIVITY_PER_PAGE) $sec.append($pager);

            function paintPage() {
                var pageCount = Math.max(1, Math.ceil(rows.length / ACTIVITY_PER_PAGE));
                if (activityPage >= pageCount) activityPage = pageCount - 1;
                if (activityPage < 0) activityPage = 0;

                var start = activityPage * ACTIVITY_PER_PAGE;
                var end = Math.min(rows.length, start + ACTIVITY_PER_PAGE);

                $card.empty();
                for (var i = start; i < end; i++) $card.append(activityRow(rows[i]));

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

            $pager.append($('<span class="vas_103-pgRange"></span>').text(
                getMsg("VAS_103_Showing", "Showing") + " " + (start + 1) + "-" + end + " " +
                getMsg("VAS_103_Of", "of") + " " + total));

            var $ctrls = $('<span class="vas_103-pgCtrls"></span>');
            $ctrls.append(pagerButton(getMsg("VAS_103_Previous", "Previous"), "chevLeft",
                page <= 0, function () { onGo(page - 1); }));
            $ctrls.append($('<span class="vas_103-pgPos"></span>').text(
                getMsg("VAS_103_Page", "Page") + " " + (page + 1) + " " +
                getMsg("VAS_103_Of", "of") + " " + pageCount));
            $ctrls.append(pagerButton(getMsg("VAS_103_Next", "Next"), "chevRight",
                page >= pageCount - 1, function () { onGo(page + 1); }));
            $pager.append($ctrls);
        }

        function pagerButton(label, icon, disabled, handler) {
            var $b = $('<span class="vas_103-pgBtn"></span>');
            if (icon === "chevLeft") $b.append(svgIcon(icon));
            $b.append($('<span></span>').text(label));
            if (icon === "chevRight") $b.append(svgIcon(icon));
            if (disabled) $b.addClass("vas_103-is-disabled");
            else $b.on("click", handler);
            return $b;
        }

        // "was X → now Y" under the field's name, for a field-level edit. A
        // value the log recorded as empty reads as an em dash rather than as a
        // blank, so a cleared field is visibly cleared instead of looking like a
        // rendering gap. Follows VAS_101 / VAS_104.
        function changeDelta(a) {
            var $d = $('<small class="vas_103-actSub vas_103-actDelta"></small>');
            var blank = "—";
            $d.append($('<span class="vas_103-cvOld"></span>').text(a.OldValue || blank));
            $d.append($('<span class="vas_103-cvArrow"></span>').text("→"));
            $d.append($('<span class="vas_103-cvNew"></span>').text(a.NewValue || blank));
            $d.attr("title", (a.OldValue || blank) + " → " + (a.NewValue || blank));
            return $d;
        }

        function activityRow(a) {
            var meta = ACT_TYPES[a.EventType] || ACT_TYPES.Note;

            var $row = $('<div class="vas_103-actRow"></div>');
            var $badge = $('<span class="vas_103-actBadge"></span>')
                .addClass("vas_103-tone-" + meta.tone);
            $badge.append(svgIcon(meta.icon));
            $badge.append($('<span></span>').text(getMsg("VAS_103_Act" + a.EventType, meta.label)));
            $row.append($badge);

            var $main = $('<div class="vas_103-actMain"></div>');
            var title = activityTitle(a);
            var $title = $('<span class="vas_103-actTitle"></span>').text(title).attr("title", title);
            // A note's headline IS the comment, so it wraps rather than
            // ellipsising after one line — that text is what the reader came for.
            if (a.EventType === "Note") $title.addClass("vas_103-multiline");
            $main.append($title);

            var when = formatDateTime(a.EventTime);
            if (a.ActorName) {
                when = when ? when + " · " + getMsg("VAS_103_By", "by") + " " + a.ActorName
                            : getMsg("VAS_103_By", "by") + " " + a.ActorName;
            }
            $main.append($('<span class="vas_103-actWhen"></span>').text(when).attr("title", when));

            // A line edit names the line it landed on, BENEATH the headline and its
            // stamp — appended last so it takes a row of its own (stylesheet) and
            // the "when · by whom" stays on the headline's line where every other
            // row carries it. Dropped entirely for a header edit, which has no line
            // to name.
            if (a.EventType === "Updated" && a.ChangeScope) {
                $main.append($('<small class="vas_103-actSub"></small>')
                    .text(a.ChangeScope).attr("title", a.ChangeScope));
            }
            // ...and the move itself: what the field held before the edit and
            // what it holds after, on a sub-line of its own.
            if (a.OldValue || a.NewValue) $main.append(changeDelta(a));
            $row.append($main);
            return $row;
        }

        function activityTitle(a) {
            if (a.EventType === "Note") return a.Title || getMsg("VAS_103_ActNote", "Note");
            if (a.EventType === "Created")
                return getMsg("VAS_103_ActCreatedTxt", "Transfer created") + (a.Title ? " " + a.Title : "");
            if (a.EventType === "Completed")
                return getMsg("VAS_103_ActCompletedTxt", "Transfer completed") + (a.Title ? " " + a.Title : "");
            if (a.EventType === "Confirmation")
                return getMsg("VAS_103_ActConfirmationTxt", "Confirmation raised") + (a.Title ? " " + a.Title : "");
            if (a.EventType === "Confirmed")
                return getMsg("VAS_103_ActConfirmedTxt", "Confirmation completed") + (a.Title ? " " + a.Title : "");
            // A field-level edit headlines with the FIELD that changed — the row's
            // tag already says "Updated", and the field is what tells one edit
            // apart from the next.
            if (a.EventType === "Updated" && a.FieldName)
                return getMsg("VAS_103_ActFieldUpdated", "Updated") + " " + a.FieldName;
            return a.Title || "";
        }

        // ---------- Opening a linked record ---------- //

        // Tables whose record does NOT open in the table's default zoom window,
        // mapped to the name of the window it does. Ported from VAS_106.
        var WINDOW_NAME_BY_TABLE = {
            "M_Requisition": "VAS_Requisition"
        };

        // Window name -> AD_Window_ID, resolved once per name and remembered for
        // the life of the panel. A name the dictionary does not know is cached as
        // -1 so a failed lookup is not repeated on every click.
        var windowIdByName = {};
        var windowIdByTable = {};

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
                    "VAS_103_MaterialTransfer/GetWindow_ID", windowName);
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

        // Last resort: ask the SERVER which window the table opens in. The
        // Production Order chip needs it — VAMFG_M_WorkOrder is maintained by a
        // module window whose name cannot be hard-coded here, and the browser-side
        // zoom lookup only knows tables the client has cached.
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
                    "VAS_103_MaterialTransfer/GetWindowIdByTable", tableName);
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

        // Opens the record's window filtered to that row, in three steps: the window
        // the CALLER names, else the table's own zoom target, else the window the
        // DICTIONARY says the table opens in. Degrades to a toast so a click never
        // throws.
        function openRecord(tableName, recordId, windowName) {
            if (!tableName || !recordId || +recordId <= 0 || !window.VIS) return;
            try {
                var windowId = resolveWindowIdByName(windowName || WINDOW_NAME_BY_TABLE[tableName]);

                if (windowId <= 0 &&
                    VIS.ZoomTarget && typeof VIS.ZoomTarget.getZoomAD_Window_ID === "function") {
                    // Neither table this panel opens serves both sides of the
                    // trade, so no IsSOTrx is asked for.
                    windowId = VIS.ZoomTarget.getZoomAD_Window_ID(tableName, 0, null, false) || 0;
                }
                if (windowId <= 0) windowId = resolveWindowIdByTable(tableName);

                if (windowId > 0 && VIS.viewManager && typeof VIS.viewManager.startWindow === "function") {
                    var zoomQuery = VIS.Query.prototype.getEqualQuery(tableName + "_ID", +recordId);
                    VIS.viewManager.startWindow(windowId, zoomQuery);
                    return;
                }
            } catch (e) { console.log(e); }
            toast(getMsg("VAS_103_OpenRecord", "Open") + " " + tableName + " #" + recordId, true);
        }

        // Lightweight self-contained toast (no dependency on a host toast API).
        function toast(message, isError) {
            var $t = $('<div class="vas_103-toast"></div>')
                .addClass(isError ? "vas_103-err" : "vas_103-ok").text(message);
            $root.append($t);
            setTimeout(function () { $t.addClass("vas_103-show"); }, 10);
            setTimeout(function () {
                $t.removeClass("vas_103-show");
                setTimeout(function () { $t.remove(); }, 300);
            }, 3200);
        }

        // ----------------------------------------------------------------- //
        //  Icon helpers                                                      //
        // ----------------------------------------------------------------- //

        var SVG_ICONS = {
            chevLeft: '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round"><polyline points="15 18 9 12 15 6"/></svg>',
            chevRight: '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round"><polyline points="9 18 15 12 9 6"/></svg>',
            user:     '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"/><circle cx="12" cy="7" r="4"/></svg>',
            calendar: '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="4" width="18" height="18" rx="2"/><path d="M16 2v4"/><path d="M8 2v4"/><path d="M3 10h18"/></svg>',
            box:      '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M21 8 12 3 3 8v8l9 5 9-5Z"/><path d="m3 8 9 5 9-5"/><path d="M12 13v8"/></svg>',
            coins:    '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><circle cx="8" cy="8" r="5"/><path d="M14.5 4.2a5 5 0 0 1 0 15.6"/><path d="M7 18.7a5 5 0 0 0 6 0"/></svg>',
            layers:   '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="m12 2 9 5-9 5-9-5 9-5Z"/><path d="m3 12 9 5 9-5"/><path d="m3 17 9 5 9-5"/></svg>',
            transfer: '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M8 3 4 7l4 4"/><path d="M4 7h16"/><path d="m16 21 4-4-4-4"/><path d="M20 17H4"/></svg>',
            arrow:    '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M5 12h14"/><path d="m13 5 7 7-7 7"/></svg>',
            link:     '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M10 13a5 5 0 0 0 7.54.54l3-3a5 5 0 0 0-7.07-7.07l-1.72 1.71"/><path d="M14 11a5 5 0 0 0-7.54-.54l-3 3a5 5 0 0 0 7.07 7.07l1.71-1.71"/></svg>',
            check:    '<svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"><path d="M20 6 9 17l-5-5"/></svg>',
            // The activity badges. 'print' and 'download' went with the Print and
            // Receive Transfer buttons that were the only things using them.
            note:     '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><path d="M14 2v6h6M9 13h6M9 17h4"/></svg>',
            plus:     '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 5v14M5 12h14"/></svg>',
            clock:    '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="9"/><polyline points="12 7 12 12 15 14"/></svg>',
            // The field-level "Updated" activity rows.
            pencil:   '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M12 20h9"/><path d="M16.5 3.5a2.12 2.12 0 0 1 3 3L7 19l-4 1 1-4Z"/></svg>'
        };

        function svgIcon(name) {
            var $wrap = $('<span class="vas_103-ic"></span>');
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

        // Date AND time, for the activity feed's genuine timestamps.
        //
        // The database holds these in UTC and the server emits no timezone
        // designator (e.g. "2026-08-12T10:00:00"), which the browser reads as
        // LOCAL — so an untagged stamp prints the stored UTC clock and every
        // activity time reads hours out. Tagging it "Z" makes toLocale* render it
        // in the viewer's own zone. A string already carrying a "Z" or a ±hh:mm
        // offset is left alone. Ported from VAS_106.
        function formatDateTime(value) {
            if (!value) return "";
            var d;
            if (value instanceof Date) {
                d = value;
            } else {
                var s = String(value);
                if (!/(Z|[+\-]\d{2}:?\d{2})$/.test(s) && /^\d{4}-\d{2}-\d{2}T/.test(s)) s += "Z";
                d = new Date(s);
            }
            if (isNaN(d.getTime())) return "";
            try {
                return d.toLocaleString(window.navigator.language, {
                    year: "numeric", month: "short", day: "2-digit",
                    hour: "2-digit", minute: "2-digit"
                });
            } catch (e) {
                return d.toString();
            }
        }

        this.getRoot = function () {
            return $root;
        };
    };

    VAS.VAS_103_MaterialTransfer.prototype.startPanel = function (windowNo, curTab) {
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
    VAS.VAS_103_MaterialTransfer.prototype.refreshPanelData = function (recordID, selectedRow) {
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
    VAS.VAS_103_MaterialTransfer.prototype.sizeChanged = function (width) {
        this.panelWidth = width;
    };

    /* Release variables from memory */
    VAS.VAS_103_MaterialTransfer.prototype.dispose = function () {
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
