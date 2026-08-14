/************************************************************
 * Module Name    : VAS
 * Purpose        : Ship / GRN Confirmation Overview tab panel. Renders a review-
 *                  oriented overview of the selected in/out confirmation
 *                  (M_InOutConfirm) in one of two modes driven by the data:
 *                  quality-applicable (incoming QC) and plain confirmation. The
 *                  panel shows a header identity strip (source-type eyebrow,
 *                  confirmation no, type / status / QC-hold / no-quality marker
 *                  chips) + a source & receipt details card, a KPI snapshot
 *                  (lines, target, confirmed, difference, scrapped, and a
 *                  QC-only QC-pass tile), and a confirmation-lines table with a
 *                  QC-only QC-mark column and a per-line status tag
 *                  (Cleared / Partial / Hold / Pending), plus visual action
 *                  buttons (Print / "Clear Quality & Confirm" in quality mode or
 *                  "Confirm" otherwise). All QC-only elements are hidden when
 *                  quality does not apply. Data is fetched from
 *                  VAS_104_OverviewShipGRNConfirmation/GetShipGRNConfirmationOverview.
 *                  All on-screen strings are resolved through
 *                  VIS.Msg.getMsg("VAS_104_...").
 * Chronological development:
 *   VAI163   2026-07-07  Created
 *   VAI163   2026-08-05  Class prefix renamed MPC-vasgc- -> vas_104- so the panel's
 *                          styles cannot collide with another panel's.
 *   VAI163   2026-08-05  New Record / Copy Record now empty the panel instead
 *                        of leaving the previously selected record on screen.
 *                        Both refreshPanelData and a new data-status listener
 *                        ask isTabInserting(), which reads GridTab.gridTable
 *                        .getIsInserting() — the flag GridTable.dataNew() raises
 *                        for both actions. The record id cannot answer it: a
 *                        copied row carries the source record's key until saved.
 *                        Ported from VAS_092.
 *   VAI163   2026-08-07  Emits the vas_104-prefixed modifier classes the
 *                        stylesheet now uses, the runtime-built ones included
 *                        ("vas_104-tone-" + tone).
 *   VAI163   2026-08-12  Header:
 *                        - The eyebrow names the DOCUMENT THIS IS: "GRN
 *                          Confirmation No." over a receipt and "Delivery Order
 *                          Confirmation No." over a shipment. It read "Goods
 *                          Receipt Note" / "Outbound Shipment", which named the
 *                          SOURCE document over the confirmation's own number.
 *                        - The "Quality Not Applicable" pill is gone: it announced
 *                          the absence of something on every ordinary confirmation.
 *                          Quality still hides its own columns when it does not
 *                          apply, which is the same news told once.
 *                        - A Disputed pill when the confirmation is in dispute
 *                          (IsInDispute, model side).
 *                        - The details card drops Confirmation No (the title strip
 *                          already carries it) and Warehouse (the left column
 *                          already does), leaving the source document — which is
 *                          now a LINK that opens the GRN or the delivery order
 *                          (openRecord / WINDOW_NAME_BY_TABLE, resolved through the
 *                          new GetWindow_ID endpoint; M_InOut serves both sides so
 *                          the window has to be named).
 *                        - The confirmation type shows the DICTIONARY's own name
 *                          for it (ConfirmTypeName, model side). The panel carried
 *                          its own map of four codes, so a customer-added type
 *                          showed a bare code.
 *                        Lines:
 *                        - The item cell carries the source document's LINE NUMBER
 *                          and the line's ATTRIBUTES beside the product, and the
 *                          product's Value stands alone — it was prefixed "SKU",
 *                          a word for a column header, not for every row.
 *                        - The Locator column is gone. A locator only means
 *                          something where something was scrapped, so it sits under
 *                          the Scrapped figure, and only when that figure is
 *                          non-zero — as a column it was repeated down the table
 *                          saying nothing.
 *                        - Status is no longer derived from the quantities
 *                          (Cleared / Partial / Hold / Pending, which every line
 *                          had). It is the QUALITY verdict now, from the line's own
 *                          parameters compared actual-against-acceptable, and only
 *                          lines that HAVE quality parameters carry one.
 *                        - Each such line opens a drawer of its quality parameters
 *                          (VA010_ShipConfParameters, model side): QC date, test
 *                          parameter, acceptable value, actual value and quantity.
 *                        Bottom:
 *                        - Notes and Activity sections, in that order, below the
 *                          lines.
 *                        - The Print / Confirm buttons are gone. Both were
 *                          presentational only and neither did anything; the
 *                          actions belong to the confirmation window.
 *   VAI163   2026-08-13  Activity pages at 15 rows (ACTIVITY_PER_PAGE), matching
 *                        the other overview panels. The pager is a sibling of
 *                        the list card so the controls keep their place while
 *                        the rows are replaced underneath them, and the
 *                        section's count badge still counts the WHOLE feed.
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

    VAS.VAS_104_OverviewShipGRNConfirmation = function () {
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
        // Which lines have their quality-parameter drawer open, keyed by
        // M_InOutLineConfirm_ID. Kept outside render() so a repaint of the same
        // record leaves the reader where they were; reset with the record.
        var openQcLines = {};

        this.init = function () {
            $root = $('<div class="vas_104-root"></div>');
            $body = $('<div class="vas_104-body"></div>');
            $emptyState = $('<div class="vas_104-empty" style="display:none;"></div>');
            $emptyState.text(VIS.Msg.getMsg("VAS_104_NoData"));
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
            // confirmation's line ids mean nothing here.
            openQcLines = {};
            // A different record starts at the top of its own feed.
            activityPage = 0;
            showBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + "VAS_104_OverviewShipGRNConfirmation/GetShipGRNConfirmationOverview",
                type: "GET",
                dataType: "json",
                data: { M_InOutConfirm_ID: recordID },
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
            openQcLines = {};
            render();
        };

        function qualityMode() {
            return !!(data && data.QualityApplicable);
        }

        function render() {
            $body.empty();
            // Panel-level modifier: hides .qc-only when quality does not apply.
            $root.toggleClass("vas_104-no-quality", !qualityMode());

            if (!data || !data.M_InOutConfirm_ID) {
                $body.hide();
                $emptyState.show();
                return;
            }

            $emptyState.hide();
            $body.show();

            renderHeader();
            renderSnapshot();
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
            var $sec = $('<section class="vas_104-sec"></section>');
            var $head = $('<div class="vas_104-secHead"></div>');
            $head.append($('<h2 class="vas_104-secTitle"></h2>').text(title));
            if (opts.summary) {
                $head.append($('<span class="vas_104-secSummary"></span>').text(opts.summary));
            }
            if (opts.$right) $head.append(opts.$right);
            $sec.append($head);
            ($parent || $body).append($sec);
            return $sec;
        }

        function na(value) {
            return (value === null || value === undefined || String(value).trim() === "")
                ? VIS.Msg.getMsg("VAS_104_NA")
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

        // ---------- Status map (DocStatus code -> label + tone) ---------- //

        var STATUS_MAP = {
            "DR": { key: "VAS_104_Drafted",             tone: "neutral" },
            "IP": { key: "VAS_104_InProgress",          tone: "info" },
            "AP": { key: "VAS_104_Approved",            tone: "info" },
            "CO": { key: "VAS_104_Completed",           tone: "success" },
            "CL": { key: "VAS_104_Closed",              tone: "success" },
            "VO": { key: "VAS_104_Voided",              tone: "risk" },
            "RE": { key: "VAS_104_Reversed",            tone: "risk" },
            "WC": { key: "VAS_104_WaitingConfirmation", tone: "warning" },
            "WP": { key: "VAS_104_WaitingPayment",      tone: "warning" },
            "IN": { key: "VAS_104_Invalid",             tone: "risk" },
            "NA": { key: "VAS_104_NotApproved",         tone: "risk" }
        };

        function statusMeta(code) {
            var m = STATUS_MAP[code];
            if (m) return { label: VIS.Msg.getMsg(m.key), tone: m.tone };
            return { label: na(code), tone: "neutral" };
        }

        // ---------- Confirmation type map (ConfirmType code -> label) ---------- //

        var CONFIRMTYPE_MAP = {
            "SI": "VAS_104_ShipReceiptConfirm",
            "PI": "VAS_104_PickQAConfirm",
            "CU": "VAS_104_CustomsConfirm",
            "D0": "VAS_104_DropShipConfirm"
        };

        // The confirmation type's FULL name. The server sends the dictionary's own
        // name for the value stored on the record (ConfirmTypeName, from the
        // reference list behind M_InOutConfirm.ConfirmType), so the panel reads
        // what the confirmation screen reads — translations and customer-added
        // types included. The map below is only the fallback for a deployment
        // whose reference list could not be read; it used to be the whole answer,
        // so any type outside those four codes showed a bare code.
        function confirmTypeLabel() {
            if (data.ConfirmTypeName) return data.ConfirmTypeName;
            var k = CONFIRMTYPE_MAP[data.ConfirmTypeCode];
            return k ? VIS.Msg.getMsg(k) : na(data.ConfirmTypeCode);
        }

        function sourceTypeLabel() {
            return (data.SourceTypeCode === "SHP")
                ? VIS.Msg.getMsg("VAS_104_Shipment")
                : VIS.Msg.getMsg("VAS_104_GoodsReceipt");
        }

        // What THIS document is, over its own number. It named the SOURCE document
        // ("Goods Receipt Note" / "Outbound Shipment") above the confirmation's
        // number, which read as though the number belonged to the source.
        function confirmationEyebrow() {
            return (data.SourceTypeCode === "SHP")
                ? getMsg("VAS_104_DOConfirmationNo", "Delivery Order Confirmation No.")
                : getMsg("VAS_104_GRNConfirmationNo", "GRN Confirmation No.");
        }

        // The window each side of M_InOut opens in — a receipt is a Material
        // Receipt, a shipment a Delivery Order. One table, two screens, which the
        // browser's zoom lookup cannot choose between.
        function sourceWindowName() {
            return (data.SourceTypeCode === "SHP") ? "VAS_DeliveryOrder" : "VAS_MaterialReceipt";
        }

        // Number of quality lines that did not pass QC (quality mode only).
        function qcFailCount() {
            var lines = (data && data.Lines) || [];
            var n = 0;
            for (var i = 0; i < lines.length; i++) {
                if (lines[i].QualityApplicable && lines[i].QcMark !== "Y") n++;
            }
            return n;
        }

        // lineStatus() is gone with the column it fed. It derived a Cleared /
        // Partial / Hold / Pending tag from the line's own quantities, so every
        // line carried one — and it only ever restated what the Difference and
        // Scrapped columns beside it already showed. The Status column now reports
        // the QUALITY verdict (qcStatusMeta), which the quantities cannot answer,
        // and only for a line that has quality parameters to derive one from.

        // ---------- Header (title strip + details card) ---------- //

        function renderHeader() {
            var st = statusMeta(data.StatusCode);
            var qm = qualityMode();

            var $strip = $('<section class="vas_104-hdr"></section>');
            var $top = $('<div class="vas_104-hdrTop"></div>');

            var $tl = $('<div class="vas_104-hdrTitleWrap"></div>');
            $tl.append($('<div class="vas_104-hdrEyebrow"></div>').text(confirmationEyebrow()));
            $tl.append($('<div class="vas_104-hdrTitle"></div>').text(na(data.DocumentNo)));

            var subBits = [];
            if (data.PartyName) subBits.push(data.PartyName);
            var moved = formatDate(data.MovementDate);
            if (moved) subBits.push(VIS.Msg.getMsg("VAS_104_MovementDate") + " " + moved);
            if (subBits.length) {
                $tl.append($('<div class="vas_104-hdrSub"></div>').text(subBits.join(" · ")));
            }
            $top.append($tl);

            var $pills = $('<div class="vas_104-hdrPills"></div>');
            $pills.append(headerPill(sourceTypeLabel(), "info", "box", false));

            // QC hold warning, quality mode only. There is deliberately no
            // "Quality Not Applicable" counterpart: it announced the absence of
            // something on every ordinary confirmation, and the panel already says
            // it by hiding the quality columns.
            if (qm) {
                var fails = qcFailCount();
                if (fails > 0) {
                    $pills.append(headerPill(
                        fails + " " + VIS.Msg.getMsg("VAS_104_LinesFailedQC"), "risk", "alert", false));
                }
            }

            // A disputed confirmation says so: the quantities did not agree and
            // the document is being contested, which outranks its status.
            if (data.IsInDispute) {
                $pills.append(headerPill(
                    getMsg("VAS_104_InDispute", "Disputed"), "warning", "alert", false));
            }

            $pills.append(headerPill(st.label, st.tone, null, true));
            $top.append($pills);

            $strip.append($top);
            $body.append($strip);

            // --- Source & receipt details card ---
            var $card = $('<section class="vas_104-hdrCard"></section>');

            var $left = $('<div class="vas_104-hdrColL"></div>');
            $left.append($('<div class="vas_104-fLabel"></div>').text(VIS.Msg.getMsg("VAS_104_Party")));
            $left.append($('<div class="vas_104-vendName"></div>').text(na(data.PartyName)));
            $left.append(headerField(VIS.Msg.getMsg("VAS_104_ConfirmationType"), confirmTypeLabel(), false));

            var $contact = $('<div class="vas_104-vendContact"></div>');
            appendContactBit($contact, "warehouse", data.WarehouseName);
            appendContactBit($contact, "calendar", formatDate(data.MovementDate));
            if ($contact.children().length) $left.append($contact);
            $card.append($left);

            // Confirmation No and Warehouse are gone from this column: the title
            // strip above already carries the confirmation number, and the left
            // column already carries the warehouse. What is left is the one field
            // that appears nowhere else — the document this confirmation was
            // raised against, which OPENS it.
            var $right = $('<div class="vas_104-hdrColR"></div>');
            var $srcLabel = (data.SourceTypeCode === "SHP")
                ? getMsg("VAS_104_SourceDeliveryOrder", "Delivery Order")
                : getMsg("VAS_104_SourceGRN", "Goods Receipt Note");
            $right.append(sourceLinkField($srcLabel));
            $card.append($right);

            $body.append($card);
        }

        // The source document as a link. It carried the link STYLING already but
        // nothing happened on click; it opens the record now — the Material
        // Receipt window for a receipt, the Delivery Order window for a shipment.
        // A confirmation with no readable source degrades to plain text.
        function sourceLinkField(label) {
            var $f = $('<div class="vas_104-hdrField"></div>');
            $f.append($('<div class="vas_104-fLabel"></div>').text(label));

            var docNo = data.SourceDocumentNo;
            var $v = $('<div class="vas_104-fVal"></div>').text(na(docNo));
            if (docNo && +data.SourceInOutID > 0) {
                $v.addClass("vas_104-is-link")
                  .attr("title", getMsg("VAS_104_OpenSource", "Open") + " " + docNo)
                  .on("click", function () {
                      openRecord("M_InOut", data.SourceInOutID, sourceWindowName(),
                                 data.SourceTypeCode === "SHP");
                  });
            }
            $f.append($v);
            return $f;
        }

        function headerPill(label, tone, icon, withDot) {
            var $p = $('<span class="vas_104-hdrPill"></span>')
                .addClass("vas_104-tone-" + (tone || "neutral"));
            if (icon) $p.append(svgIcon(icon));
            if (withDot) $p.append($('<span class="vas_104-hdrDot"></span>'));
            $p.append($('<span></span>').text(label));
            return $p;
        }

        function headerField(label, value, link) {
            var $f = $('<div class="vas_104-hdrField"></div>');
            $f.append($('<div class="vas_104-fLabel"></div>').text(label));
            var $v = $('<div class="vas_104-fVal"></div>').text(value);
            if (link && value !== VIS.Msg.getMsg("VAS_104_NA")) $v.addClass("vas_104-is-link");
            $f.append($v);
            return $f;
        }

        function appendContactBit($container, icon, value) {
            if (!value) return;
            var $bit = $('<span class="vas_104-contactBit"></span>');
            $bit.append(svgIcon(icon));
            $bit.append($('<span></span>').text(value));
            $container.append($bit);
        }

        // ---------- Snapshot (KPI metric grid) ---------- //

        function renderSnapshot() {
            var $snap = $('<section class="vas_104-snap"></section>');

            // Lines.
            $snap.append(metricCard("lines", "layers", VIS.Msg.getMsg("VAS_104_Lines"),
                (data.LineCount || 0) + "", "", false));

            // Target qty.
            $snap.append(metricCard("target", "box", VIS.Msg.getMsg("VAS_104_TargetQty"),
                formatNumber(+data.TargetQty || 0, 0), "", false));

            // Confirmed qty.
            $snap.append(metricCard("confirmed", "check", VIS.Msg.getMsg("VAS_104_Confirmed"),
                formatNumber(+data.ConfirmedQty || 0, 0), "", false));

            // Difference qty.
            $snap.append(metricCard("difference", "delta", VIS.Msg.getMsg("VAS_104_Difference"),
                signedNumber(+data.DifferenceQty || 0, 0), "", false));

            // Scrapped qty.
            $snap.append(metricCard("scrapped", "trash", VIS.Msg.getMsg("VAS_104_Scrapped"),
                formatNumber(+data.ScrappedQty || 0, 0), "", false));

            // QC pass (quality mode only).
            $snap.append(metricCard("qcpass", "shield", VIS.Msg.getMsg("VAS_104_QCPass"),
                (data.QcPassCount || 0) + " / " + (data.LineCount || 0),
                VIS.Msg.getMsg("VAS_104_LinesPassed"), true));

            $body.append($snap);
        }

        function metricCard(tone, icon, label, value, sub, qcOnly) {
            var $c = $('<div class="vas_104-metric"></div>').addClass("vas_104-tone-" + tone);
            if (qcOnly) $c.addClass("vas_104-qc-only");

            var $head = $('<div class="vas_104-mHead"></div>');
            $head.append(svgIcon(icon));
            $head.append($('<span class="vas_104-mLabel"></span>').text(label));
            $c.append($head);

            $c.append($('<div class="vas_104-mVal"></div>').text(value));
            if (sub) $c.append($('<div class="vas_104-mSub"></div>').text(sub));
            return $c;
        }

        // ---------- Confirmation lines (table) ---------- //

        function renderLines() {
            var lines = (data && data.Lines) || [];
            if (!lines.length) return;

            var qm = qualityMode();
            var title = qm ? VIS.Msg.getMsg("VAS_104_LinesAndQuality")
                           : VIS.Msg.getMsg("VAS_104_ConfirmationLines");

            var summary = qm
                ? ((data.LineCount || 0) + " " + VIS.Msg.getMsg("VAS_104_LinesWord") + " · " +
                   (data.QcPassCount || 0) + " " + VIS.Msg.getMsg("VAS_104_Of") + " " +
                   (data.LineCount || 0) + " " + VIS.Msg.getMsg("VAS_104_QCPassed"))
                : ((data.LineCount || 0) + " " + VIS.Msg.getMsg("VAS_104_LinesConfirmed"));

            var $sec = section(title, { summary: summary });
            $sec.append(buildLinesTable());
        }

        function buildLinesTable() {
            var lines = (data && data.Lines) || [];

            var $tbl = $('<div class="vas_104-table"></div>');

            // No Locator column: a locator only means something where something
            // was scrapped, so it moved under the Scrapped figure and shows only
            // when that figure is non-zero.
            var $head = $('<div class="vas_104-tRow vas_104-tHead"></div>');
            $head.append($('<span></span>').text(VIS.Msg.getMsg("VAS_104_Item")));
            $head.append($('<span></span>').text(VIS.Msg.getMsg("VAS_104_UOM")));
            $head.append($('<span class="vas_104-ta-r"></span>').text(VIS.Msg.getMsg("VAS_104_Target")));
            $head.append($('<span class="vas_104-ta-r"></span>').text(VIS.Msg.getMsg("VAS_104_Confirmed")));
            $head.append($('<span class="vas_104-ta-r"></span>').text(VIS.Msg.getMsg("VAS_104_Difference")));
            $head.append($('<span class="vas_104-ta-r"></span>').text(VIS.Msg.getMsg("VAS_104_Scrapped")));
            $head.append($('<span class="vas_104-ta-c vas_104-qc-only"></span>').text(VIS.Msg.getMsg("VAS_104_QCMark")));
            $head.append($('<span class="vas_104-ta-c"></span>').text(VIS.Msg.getMsg("VAS_104_Status")));
            $tbl.append($head);

            var totTarget = 0, totConfirmed = 0, totDiff = 0, totScrap = 0;

            for (var i = 0; i < lines.length; i++) {
                var ln = lines[i];
                totTarget    += (+ln.TargetQty || 0);
                totConfirmed += (+ln.ConfirmedQty || 0);
                totDiff      += (+ln.DifferenceQty || 0);
                totScrap     += (+ln.ScrappedQty || 0);
                $tbl.append(buildLineRow(ln));
                // The line's quality parameters, collapsed beneath it. Only a line
                // that HAS parameters gets one, and it stays shut until asked for.
                var $drawer = buildQcDrawer(ln);
                if ($drawer) $tbl.append($drawer);
            }

            // Totals footer
            var $foot = $('<div class="vas_104-tFoot"></div>');
            $foot.append(footBit(VIS.Msg.getMsg("VAS_104_Target"), formatNumber(totTarget, 0), false));
            $foot.append(footBit(VIS.Msg.getMsg("VAS_104_Confirmed"), formatNumber(totConfirmed, 0), false));
            $foot.append(footBit(VIS.Msg.getMsg("VAS_104_Difference"), signedNumber(totDiff, 0), false));
            $foot.append(footBit(VIS.Msg.getMsg("VAS_104_Scrapped"), formatNumber(totScrap, 0), true));
            $tbl.append($foot);

            return $tbl;
        }

        function footBit(label, value, grand) {
            var $b = $('<span class="vas_104-tf"></span>');
            if (grand) $b.addClass("vas_104-is-grand");
            $b.append(document.createTextNode(label));
            $b.append($('<b></b>').text(value));
            return $b;
        }

        function buildLineRow(ln) {
            var $tr = $('<div class="vas_104-tRow vas_104-tBody"></div>');

            // Item: the source document's line number, the product, its Value and
            // its attributes.
            var $item = $('<span class="vas_104-itItem"></span>');

            var $nameRow = $('<div class="vas_104-itNameRow"></div>');
            // The line number the GRN / shipment knows this row by — the one thing
            // that ties a confirmation row back to the document it confirms.
            if (+ln.Line > 0) {
                $nameRow.append($('<span class="vas_104-lineNo"></span>')
                    .text("#" + ln.Line)
                    .attr("title", getMsg("VAS_104_SourceLineNo", "Line no. on the source document")));
            }
            $nameRow.append($('<span class="vas_104-itName"></span>').text(na(ln.ProductName)));
            // The toggle for the line's quality parameters, on the name itself.
            var $qcBtn = buildQcToggle(ln);
            if ($qcBtn) $nameRow.append($qcBtn);
            $item.append($nameRow);

            // The product's Value, alone. It was prefixed with the word "SKU",
            // which belongs to a column header, not to every row of the table.
            var subBits = [];
            if (ln.ProductCode) subBits.push(ln.ProductCode);
            // Attributes travel with the product: a lot / serial / attribute set is
            // what distinguishes two rows of the same item from each other.
            if (ln.AttributeSetInstance) subBits.push(ln.AttributeSetInstance);
            if (!subBits.length && ln.Description) subBits.push(ln.Description);
            if (subBits.length) {
                var sub = subBits.join(" · ");
                $item.append($('<div class="vas_104-itSku"></div>').text(sub).attr("title", sub));
            }
            $tr.append($item);

            var prec = +ln.UOMPrecision || 0;

            // UOM
            $tr.append($('<span></span>').text(na(ln.UOMName)));

            // Target
            $tr.append($('<span class="vas_104-ta-r"></span>').text(formatNumber(+ln.TargetQty || 0, prec)));

            // Confirmed
            $tr.append($('<span class="vas_104-ta-r"></span>').text(formatNumber(+ln.ConfirmedQty || 0, prec)));

            // Difference (signed; negative red, zero neutral)
            var diff = +ln.DifferenceQty || 0;
            var diffTone = diff < 0 ? "vas_104-neg" : (diff > 0 ? "vas_104-pos" : "vas_104-zero");
            $tr.append($('<span class="vas_104-ta-r vas_104-diff"></span>').addClass(diffTone)
                .text(signedNumber(diff, prec)));

            // Scrapped (red when > 0), with the LOCATOR beneath it. The locator
            // only means something where something was actually scrapped — as its
            // own column it repeated down the table saying nothing — so it appears
            // here and only when the figure above it is non-zero.
            var scrap = +ln.ScrappedQty || 0;
            var $scrapCell = $('<span class="vas_104-ta-r vas_104-scrapCell"></span>');
            $scrapCell.append($('<span class="vas_104-scrap"></span>')
                .toggleClass("vas_104-has", scrap > 0)
                .text(formatNumber(scrap, prec)));
            if (scrap > 0 && ln.LocatorName) {
                $scrapCell.append($('<span class="vas_104-scrapLoc"></span>')
                    .text(ln.LocatorName)
                    .attr("title", getMsg("VAS_104_ScrapLocator", "Scrap locator") + ": " + ln.LocatorName));
            }
            $tr.append($scrapCell);

            // QC mark (qc-only) — Pass / Fail chip when quality applies to the line.
            var $qc = $('<span class="vas_104-ta-c vas_104-qc-only"></span>');
            if (ln.QualityApplicable) {
                var pass = ln.QcMark === "Y";
                $qc.append($('<span class="vas_104-tag"></span>')
                    .addClass(pass ? "vas_104-s-pass" : "vas_104-s-fail")
                    .text(pass ? VIS.Msg.getMsg("VAS_104_Pass") : VIS.Msg.getMsg("VAS_104_Fail")));
            } else {
                $qc.append($('<span class="vas_104-dash"></span>').text("—"));
            }
            $tr.append($qc);

            // Status — the QUALITY verdict, and only for a line that has quality
            // parameters to derive one from. It used to be computed from the
            // quantities for EVERY line (Cleared / Partial / Hold / Pending),
            // which said the same thing the Difference and Scrapped columns
            // already say, one column to the left.
            var $q = $('<span class="vas_104-ta-c"></span>');
            var qs = qcStatusMeta(ln.QcStatusCode);
            if (qs) {
                $q.append($('<span class="vas_104-tag"></span>')
                    .addClass("vas_104-s-" + qs.cls).text(qs.label));
            } else {
                $q.append($('<span class="vas_104-dash"></span>').text("—"));
            }
            $tr.append($q);

            return $tr;
        }

        // A line's quality verdict, derived server-side from its own parameters by
        // comparing each actual value against the acceptable one. Returns null for
        // a line with no parameters, which is what leaves its Status cell empty.
        function qcStatusMeta(code) {
            if (code === "P") return { cls: "pass",    label: getMsg("VAS_104_QcPassed",  "Passed") };
            if (code === "F") return { cls: "fail",    label: getMsg("VAS_104_QcFailed",  "Failed") };
            if (code === "N") return { cls: "pending", label: getMsg("VAS_104_QcPending", "Pending") };
            return null;
        }

        // ---------- Per-line quality parameters (VA010_ShipConfParameters) ---------- //

        // The toggle that opens a line's quality parameters, sitting on the product
        // name. Returns null for a line with none, so an ordinary line carries no
        // control at all.
        function buildQcToggle(ln) {
            var params = ln.QualityParams || [];
            if (!params.length) return null;

            var id = ln.M_InOutLineConfirm_ID;
            var open = !!openQcLines[id];
            var $b = $('<span class="vas_104-qcBtn"></span>')
                .toggleClass("vas_104-is-open", open)
                .attr("title", getMsg("VAS_104_QualityParams", "Quality parameters") + " (" + params.length + ")");
            $b.append(svgIcon("flask"));
            $b.append($('<span></span>').text(params.length + ""));
            $b.on("click", function (e) {
                e.stopPropagation();
                var nowOpen = !openQcLines[id];
                if (nowOpen) openQcLines[id] = true; else delete openQcLines[id];
                $b.toggleClass("vas_104-is-open", nowOpen);
                $b.closest(".vas_104-tBody").next(".vas_104-qcDrawer").toggle(nowOpen);
            });
            return $b;
        }

        // The drawer itself: one row per parameter, carrying the QC date, the test
        // parameter, the acceptable value, the actual value and the quantity to
        // verify. A sibling of the line row rather than a child, so it can span the
        // table's full width instead of living inside one grid cell.
        function buildQcDrawer(ln) {
            var params = ln.QualityParams || [];
            if (!params.length) return null;

            var open = !!openQcLines[ln.M_InOutLineConfirm_ID];
            var $d = $('<div class="vas_104-qcDrawer"></div>');
            if (!open) $d.hide();

            var $tbl = $('<div class="vas_104-qcTable"></div>');

            var $head = $('<div class="vas_104-qcRow vas_104-qcHead"></div>');
            $head.append($('<span></span>').text(getMsg("VAS_104_QcDate", "QC Date")));
            $head.append($('<span></span>').text(getMsg("VAS_104_TestParameter", "Test Parameter")));
            $head.append($('<span></span>').text(getMsg("VAS_104_AcceptableValue", "Acceptable Value")));
            $head.append($('<span></span>').text(getMsg("VAS_104_ActualValue", "Actual Value")));
            $head.append($('<span class="vas_104-ta-r"></span>').text(getMsg("VAS_104_QcQty", "Qty")));
            $head.append($('<span class="vas_104-ta-c"></span>').text(VIS.Msg.getMsg("VAS_104_Status")));
            $tbl.append($head);

            for (var i = 0; i < params.length; i++) {
                $tbl.append(buildQcParamRow(params[i], +ln.UOMPrecision || 0));
            }

            $d.append($tbl);
            return $d;
        }

        function buildQcParamRow(q, prec) {
            var $r = $('<div class="vas_104-qcRow vas_104-qcBody"></div>');

            $r.append($('<span></span>').text(formatDate(q.QAQCDate) || "—"));
            $r.append($('<span></span>').text(na(q.ParameterName)).attr("title", q.ParameterName || ""));
            $r.append($('<span></span>').text(na(q.AcceptableValue)));

            // An actual value that has not been recorded yet is a dash, not a
            // blank: the parameter exists and is waiting to be filled in.
            var actual = q.ActualValue;
            var $actual = $('<span></span>');
            if (actual) {
                $actual.addClass(q.StatusCode === "F" ? "vas_104-qcMiss" : "vas_104-qcHit").text(actual);
            } else {
                $actual.append($('<span class="vas_104-dash"></span>').text("—"));
            }
            $r.append($actual);

            $r.append($('<span class="vas_104-ta-r"></span>')
                .text(formatNumber(+q.QuantityToVerify || 0, prec)));

            var qs = qcStatusMeta(q.StatusCode);
            var $st = $('<span class="vas_104-ta-c"></span>');
            if (qs) {
                $st.append($('<span class="vas_104-tag"></span>')
                    .addClass("vas_104-s-" + qs.cls).text(qs.label));
            }
            $r.append($st);

            if (q.Remark) $r.attr("title", q.Remark);
            return $r;
        }

        // ---------- Actions ---------- //

        // The Print and Confirm buttons are gone, with actionButton(). Both were
        // presentational only: neither was wired to anything, so the panel offered
        // two controls that did nothing on every record. Printing and confirming
        // belong to the confirmation window, where each runs behind the document's
        // own validation.

        // ---------- Notes ---------- //

        function renderNotes() {
            var rows = (data && data.Notes) || [];
            if (!rows.length) return;

            var $sec = section(getMsg("VAS_104_Notes", "Notes"), { summary: rows.length + "" });

            var $card = $('<div class="vas_104-panelcard"></div>');
            for (var i = 0; i < rows.length; i++) {
                var n = rows[i];
                var $n = $('<div class="vas_104-noteRow"></div>');
                $n.append($('<span class="vas_104-noteTag"></span>')
                    .addClass(n.NoteType === "header" ? "vas_104-n-header" : "vas_104-n-line")
                    .text(n.NoteType === "header"
                        ? getMsg("VAS_104_NoteHeader", "Document")
                        : getMsg("VAS_104_NoteLine", "Line")));
                $n.append($('<span class="vas_104-noteTxt"></span>').text(n.Text || ""));
                $card.append($n);
            }
            $sec.append($card);
        }

        // ---------- Activity ---------- //

        var ACT_TYPES = {
            Note:      { tone: "info",    icon: "note",  label: "Note" },
            Created:   { tone: "neutral", icon: "plus",  label: "Created" },
            Completed: { tone: "success", icon: "check", label: "Completed" }
        };

        // Maximum activity rows shown per page; the feed paginates beyond this.
        // A confirmation accumulates every status change and note, and an unpaged
        // feed made the section scroll past everything below it. The section's own
        // count badge still counts the WHOLE feed, not the page.
        var ACTIVITY_PER_PAGE = 15;
        var activityPage = 0;   // current Activity page (0-based)

        function renderActivity() {
            var rows = (data && data.Activity) || [];
            if (!rows.length) return;

            var $sec = section(getMsg("VAS_104_Activity", "Activity"), { summary: rows.length + "" });

            var $card = $('<div class="vas_104-panelcard"></div>');
            $sec.append($card);

            // The pager is a sibling of the card, so it keeps its place while the
            // card's rows are replaced underneath it.
            var $pager = $('<div class="vas_104-pager"></div>');
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

            $pager.append($('<span class="vas_104-pgRange"></span>').text(
                getMsg("VAS_104_Showing", "Showing") + " " + (start + 1) + "-" + end + " " +
                getMsg("VAS_104_Of", "of") + " " + total));

            var $ctrls = $('<span class="vas_104-pgCtrls"></span>');
            $ctrls.append(pagerButton(getMsg("VAS_104_Previous", "Previous"), "chevLeft",
                page <= 0, function () { onGo(page - 1); }));
            $ctrls.append($('<span class="vas_104-pgPos"></span>').text(
                getMsg("VAS_104_Page", "Page") + " " + (page + 1) + " " +
                getMsg("VAS_104_Of", "of") + " " + pageCount));
            $ctrls.append(pagerButton(getMsg("VAS_104_Next", "Next"), "chevRight",
                page >= pageCount - 1, function () { onGo(page + 1); }));
            $pager.append($ctrls);
        }

        function pagerButton(label, icon, disabled, handler) {
            var $b = $('<span class="vas_104-pgBtn"></span>');
            if (icon === "chevLeft") $b.append(svgIcon(icon));
            $b.append($('<span></span>').text(label));
            if (icon === "chevRight") $b.append(svgIcon(icon));
            if (disabled) $b.addClass("vas_104-is-disabled");
            else $b.on("click", handler);
            return $b;
        }

        function activityRow(a) {
            var meta = ACT_TYPES[a.EventType] || ACT_TYPES.Note;

            var $row = $('<div class="vas_104-actRow"></div>');
            var $badge = $('<span class="vas_104-actBadge"></span>')
                .addClass("vas_104-tone-" + meta.tone);
            $badge.append(svgIcon(meta.icon));
            $badge.append($('<span></span>').text(getMsg("VAS_104_Act" + a.EventType, meta.label)));
            $row.append($badge);

            var $main = $('<div class="vas_104-actMain"></div>');
            var title = activityTitle(a);
            var $title = $('<span class="vas_104-actTitle"></span>').text(title).attr("title", title);
            // A note's headline IS the comment, so it wraps rather than
            // ellipsising after one line — that text is what the reader came for.
            if (a.EventType === "Note") $title.addClass("vas_104-multiline");
            $main.append($title);

            // "when · by whom", the same two parts in the same place on every row.
            var when = formatDateTime(a.EventTime);
            if (a.ActorName) {
                when = when ? when + " · " + getMsg("VAS_104_By", "by") + " " + a.ActorName
                            : getMsg("VAS_104_By", "by") + " " + a.ActorName;
            }
            $main.append($('<span class="vas_104-actWhen"></span>').text(when).attr("title", when));
            $row.append($main);
            return $row;
        }

        function activityTitle(a) {
            if (a.EventType === "Note") return a.Title || getMsg("VAS_104_ActNote", "Note");
            if (a.EventType === "Created")
                return getMsg("VAS_104_ActCreatedTxt", "Confirmation created") +
                       (a.Title ? " " + a.Title : "");
            if (a.EventType === "Completed")
                return getMsg("VAS_104_ActCompletedTxt", "Confirmation completed") +
                       (a.Title ? " " + a.Title : "");
            return a.Title || "";
        }

        // ---------- Opening the source document ---------- //

        // Window name -> AD_Window_ID, resolved once per name and remembered for
        // the life of the panel. A name the dictionary does not know is cached as
        // -1 so a failed lookup is not repeated on every click. Ported from
        // VAS_106.
        var windowIdByName = {};

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
                    "VAS_104_OverviewShipGRNConfirmation/GetWindow_ID", windowName);
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

        // Opens the record's window filtered to that row: the window the caller
        // NAMES, else the table's zoom target told which side of the trade to
        // resolve. M_InOut is the case that needs naming — a goods receipt and a
        // delivery order are two screens on one table. Degrades to a toast so a
        // click never throws.
        function openRecord(tableName, recordId, windowName, isSOTrx) {
            if (!tableName || !recordId || +recordId <= 0 || !window.VIS) return;
            try {
                var windowId = resolveWindowIdByName(windowName);

                if (windowId <= 0 &&
                    VIS.ZoomTarget && typeof VIS.ZoomTarget.getZoomAD_Window_ID === "function") {
                    // 4th arg is IsSOTrx — which side of a dual-purpose table to
                    // open. M_InOut is one, and the confirmation knows its side.
                    windowId = VIS.ZoomTarget.getZoomAD_Window_ID(tableName, 0, null, !!isSOTrx) || 0;
                }

                if (windowId > 0 && VIS.viewManager && typeof VIS.viewManager.startWindow === "function") {
                    var zoomQuery = VIS.Query.prototype.getEqualQuery(tableName + "_ID", +recordId);
                    VIS.viewManager.startWindow(windowId, zoomQuery);
                    return;
                }
            } catch (e) { console.log(e); }
            toast(getMsg("VAS_104_OpenSource", "Open") + " " + tableName + " #" + recordId, true);
        }

        // Lightweight self-contained toast (no dependency on a host toast API).
        function toast(message, isError) {
            var $t = $('<div class="vas_104-toast"></div>')
                .addClass(isError ? "vas_104-err" : "vas_104-ok").text(message);
            $root.append($t);
            setTimeout(function () { $t.addClass("vas_104-show"); }, 10);
            setTimeout(function () {
                $t.removeClass("vas_104-show");
                setTimeout(function () { $t.remove(); }, 300);
            }, 3200);
        }

        // ----------------------------------------------------------------- //
        //  Icon helpers                                                      //
        // ----------------------------------------------------------------- //

        var SVG_ICONS = {
            chevLeft: '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round"><polyline points="15 18 9 12 15 6"/></svg>',
            chevRight: '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round"><polyline points="9 18 15 12 9 6"/></svg>',
            warehouse: '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M3 21V8l9-5 9 5v13"/><path d="M7 21v-8h10v8"/></svg>',
            calendar:  '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="4" width="18" height="18" rx="2"/><path d="M16 2v4"/><path d="M8 2v4"/><path d="M3 10h18"/></svg>',
            box:       '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M21 8 12 3 3 8v8l9 5 9-5Z"/><path d="m3 8 9 5 9-5"/><path d="M12 13v8"/></svg>',
            layers:    '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="m12 2 9 5-9 5-9-5 9-5Z"/><path d="m3 12 9 5 9-5"/><path d="m3 17 9 5 9-5"/></svg>',
            delta:     '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M12 4 3 20h18Z"/></svg>',
            trash:     '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M3 6h18M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2m3 0v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6"/></svg>',
            shield:    '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10Z"/><path d="m9 12 2 2 4-4"/></svg>',
            alert:     '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M10.29 3.86 1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0Z"/><path d="M12 9v4"/><path d="M12 17h.01"/></svg>',
            check:     '<svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"><path d="M20 6 9 17l-5-5"/></svg>',
            // The quality-parameter toggle on a line, and the two activity badges.
            // 'print' went with the Print button that was the only thing using it.
            flask:     '<svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M9 2v6.5L3.8 18a2 2 0 0 0 1.7 3h13a2 2 0 0 0 1.7-3L15 8.5V2"/><path d="M8 2h8"/><path d="M6.5 15h11"/></svg>',
            note:      '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><path d="M14 2v6h6M9 13h6M9 17h4"/></svg>',
            plus:      '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 5v14M5 12h14"/></svg>'
        };

        function svgIcon(name) {
            var $wrap = $('<span class="vas_104-ic"></span>');
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

        // Signed number: "+N" for positive, "−N" for negative, "0" for zero.
        function signedNumber(value, precision) {
            var v = +value || 0;
            if (v === 0) return formatNumber(0, precision);
            var sign = v > 0 ? "+" : "−";
            return sign + formatNumber(Math.abs(v), precision);
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
        // in the viewer's own zone, which is what the feed should show. A string
        // already carrying a "Z" or a ±hh:mm offset is left alone. Ported from
        // VAS_106.
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

    VAS.VAS_104_OverviewShipGRNConfirmation.prototype.startPanel = function (windowNo, curTab) {
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
    VAS.VAS_104_OverviewShipGRNConfirmation.prototype.refreshPanelData = function (recordID, selectedRow) {
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
    VAS.VAS_104_OverviewShipGRNConfirmation.prototype.sizeChanged = function (width) {
        this.panelWidth = width;
    };

    /* Release variables from memory */
    VAS.VAS_104_OverviewShipGRNConfirmation.prototype.dispose = function () {
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
