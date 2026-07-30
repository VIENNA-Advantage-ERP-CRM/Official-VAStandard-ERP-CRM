/************************************************************
 * Module Name    : VAS
 * Purpose        : Goods Receipt Note (GRN) Overview tab panel. Renders a
 *                  review-oriented overview of the selected goods receipt
 *                  (M_InOut, IsSOTrx = 'N'): header identity + vendor /
 *                  receipt details card, a four-card KPI snapshot (received
 *                  value, lines, received qty, quality-check applicability),
 *                  a compact receipt timeline (PO date -> expected -> received
 *                  -> posted), the material line table and visual action
 *                  buttons (Print / Complete GRN / Generate Invoice). Data is
 *                  fetched from VAS_099_OverviewGRN/GetGRNOverview.
 *                  All on-screen strings are resolved through
 *                  VIS.Msg.getMsg("VAS_099_...").
 * Chronological development:
 *   VAI163   2026-07-06  Created
 *   VAI163   2026-07-17  Reference Invoice now shows the linked AP invoice doc
 *                        no; added Reference Sales Order (RefOrderDocNo). Both
 *                        fall back to N/A. Supplier renamed to Vendor
 *                        (VAS_099_Vendor msg key, data.Vendor* fields).
 *   VAI163   2026-07-27  - Posted status moved from the vendor details column to a
 *                          pill on the right of the title strip.
 *                        - Purchase Order + new Expected Delivery header fields
 *                          show N/A for a manually-created (PO-less) receipt.
 *                        - Added a Drop Shipment (IsDropShip) header field.
 *                        - Line items show the Attribute Set Instance sub-line and
 *                          drop the "SKU" prefix before the product search key.
 *                        - Removed the Print action button.
 *                        - Complete GRN and Generate Invoice buttons are now
 *                          functional: each confirms, POSTs to the controller
 *                          (CompleteGRN / GenerateInvoice) and refreshes the
 *                          panel; they disable when the action does not apply.
 *   VAI163   2026-07-28  - Line-item Locator now carries the full locator name as
 *                          a hover tooltip, so a truncated sub-line stays readable
 *                          without changing the layout.
 *                        - The material table's Quality column renders only when
 *                          at least one line has a quality check applicable; the
 *                          grid drops to six columns otherwise.
 *                        - Receipt Timeline reworked to the approved document
 *                          status design: Drafted -> In Progress -> Completed ->
 *                          Posted -> Invoiced (dates as captions), replacing the
 *                          previous date-only PO/expected/received/posting stages.
 *                        - Added the References section (Purchase Order, PO Date,
 *                          Vendor Ref, Reference Invoice, Reference Sales Order);
 *                          the two Reference fields moved out of the header card.
 *                        - Added the trailing Notes section, showing the GRN
 *                          header Description.
 *   VAI163   2026-07-28  Quality inspection additions, all gated on the receipt
 *                        having a quality check applicable (QcLineCount > 0):
 *                        - Two further snapshot cards, Accepted Quantity and
 *                          Rejected Quantity, fed by the confirmation lines'
 *                          ConfirmedQty / ScrappedQty. The snapshot grid switches
 *                          to three columns so six cards sit two rows deep.
 *                        - New Quality Product section listing the VA010 quality
 *                          parameters recorded per product (parameter, quantity to
 *                          verify, acceptable vs actual value, QA/QC date, a
 *                          pass / fail / pending tag and a Confirmation column
 *                          that reads Yes for an "MM Receipt with Confirmation"
 *                          document type).
 *   VAI163   2026-07-28  - Timeline's Posting stage now captions with PostedDate
 *                          (when posting actually ran) instead of the accounting
 *                          date, which on most receipts equals the movement date.
 *                        - Received quantity's mini bar moved onto its own line
 *                          beneath the figure, so the Ordered and Received numbers
 *                          share a baseline and right edge.
 *                        - Added the Activity section (audit trail) at the bottom:
 *                          created / updated / completed / posted milestones plus
 *                          confirmations, invoices and chat notes, newest-first.
 *                        - render() no longer runs before startPanel() has built
 *                          the DOM, which previously threw on $body.
 *   VAI163   2026-07-28  - Material lines page client-side at 10 rows, with a
 *                          Previous / Next pager below the table. The totals
 *                          footer and KPI cards always cover the whole receipt,
 *                          never the visible page.
 *                        - Full product name shown as a hover tooltip on the line
 *                          item, matching the locator behaviour.
 *                        - Reference Invoice / Reference Sales Order now come from
 *                          IsSOTrx-resolved orders, so a drop-ship receipt linked
 *                          to the sales order no longer shows the two the wrong
 *                          way round (model side).
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {

    VAS.VAS_099_OverviewGRN = function () {
        this.record_ID = 0;
        this.table_ID = 0;
        this.windowNo = 0;
        this.curTab = null;
        this.selectedRow = null;
        this.panelWidth;

        var $self = this;
        var $root;
        var $busy;
        var $body;
        var $emptyState;
        var data = null;

        // Material lines are paged client-side (the whole set arrives in one
        // payload). Page index resets whenever a different record is loaded.
        var LINES_PER_PAGE = 10;
        var linesPage = 0;

        this.init = function () {
            $root = $('<div class="MPC-vasgrn-root"></div>');
            $body = $('<div class="MPC-vasgrn-body"></div>');
            $emptyState = $('<div class="MPC-vasgrn-empty" style="display:none;"></div>');
            $emptyState.text(VIS.Msg.getMsg("VAS_099_NoData"));
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
                url: VIS.Application.contextUrl + "VAS_099_OverviewGRN/GetGRNOverview",
                type: "GET",
                dataType: "json",
                data: { M_InOut_ID: recordID },
                success: function (raw) {
                    var parsed = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                    data = parsed;
                    linesPage = 0;
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
            render();
        };

        function render() {
            // Nothing to draw into until startPanel() -> init() has run. The host
            // can hand us a record before that, and reaching into an undefined
            // $body threw.
            if (!$body) return;

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
            renderReferences();
            renderSnapshot();
            renderTimeline();
            renderLines();
            renderQualityProducts();
            renderNotes();
            renderActivity();
            renderActions();
        }

        // True when a quality check applies anywhere on this receipt — the two QC
        // snapshot cards and the whole Quality Product section hang off this.
        function qualityApplicable() {
            return (data && (data.QcLineCount || 0) > 0);
        }

        // ----------------------------------------------------------------- //
        //  Small builders                                                    //
        // ----------------------------------------------------------------- //

        // A headered section: Section Header (title + optional summary) followed
        // by a content node. Returns the section element so callers can append.
        function section(title, opts, $parent) {
            opts = opts || {};
            var $sec = $('<section class="MPC-vasgrn-sec"></section>');
            var $head = $('<div class="MPC-vasgrn-secHead"></div>');
            $head.append($('<h2 class="MPC-vasgrn-secTitle"></h2>').text(title));
            if (opts.summary) {
                $head.append($('<span class="MPC-vasgrn-secSummary"></span>').text(opts.summary));
            }
            $sec.append($head);
            ($parent || $body).append($sec);
            return $sec;
        }

        // Returns "N/A" for blank values so the layout never shows an empty cell.
        function na(value) {
            return (value === null || value === undefined || String(value).trim() === "")
                ? VIS.Msg.getMsg("VAS_099_NA")
                : value;
        }

        // Prefer the seeded AD_Message; else a readable English fallback; else the
        // key. Used for message keys that may not be seeded yet.
        function msg(key, fallback) {
            try {
                var m = VIS.Msg.getMsg(key);
                if (m && m !== key) return m;
            } catch (e) { }
            return (fallback !== null && fallback !== undefined) ? fallback : key;
        }

        // The currency token: prefer the linked order's symbol / ISO, else INR.
        function currencyToken() {
            return (data && (data.CurSymbol || data.ISO_Code)) || "₹";
        }

        // ---------- Status / priority maps (codes -> label + tone) ---------- //

        // DocStatus code -> { key, tone }. tone drives the pill colour.
        var STATUS_MAP = {
            "DR": { key: "VAS_099_Drafted",             tone: "neutral" },
            "IP": { key: "VAS_099_InProgress",          tone: "info" },
            "AP": { key: "VAS_099_Approved",            tone: "info" },
            "CO": { key: "VAS_099_Completed",           tone: "success" },
            "CL": { key: "VAS_099_Closed",              tone: "neutral" },
            "VO": { key: "VAS_099_Voided",              tone: "risk" },
            "RE": { key: "VAS_099_Reversed",            tone: "risk" },
            "WC": { key: "VAS_099_WaitingConfirmation", tone: "warning" },
            "WP": { key: "VAS_099_WaitingPayment",      tone: "warning" },
            "IN": { key: "VAS_099_Invalid",             tone: "risk" },
            "NA": { key: "VAS_099_NotApproved",         tone: "risk" }
        };

        function statusMeta(code) {
            var m = STATUS_MAP[code];
            if (m) return { label: VIS.Msg.getMsg(m.key), tone: m.tone };
            return { label: na(code), tone: "neutral" };
        }

        // PriorityRule code -> { key, tone }.
        var PRIORITY_MAP = {
            "1": { key: "VAS_099_Urgent", tone: "risk" },
            "3": { key: "VAS_099_High",   tone: "warning" },
            "5": { key: "VAS_099_Medium", tone: "info" },
            "7": { key: "VAS_099_Low",    tone: "neutral" },
            "9": { key: "VAS_099_Minor",  tone: "neutral" }
        };

        function priorityMeta(code) {
            var m = PRIORITY_MAP[code];
            if (!m) return null;
            return { label: VIS.Msg.getMsg(m.key), tone: m.tone };
        }

        // ---------- Header (title strip + details card) ---------- //

        function renderHeader() {
            var st = statusMeta(data.StatusCode);
            var pm = priorityMeta(data.PriorityCode);

            // --- Title strip: title + subtitle (left), priority + status (right) ---
            var $strip = $('<section class="MPC-vasgrn-hdr"></section>');
            var $top = $('<div class="MPC-vasgrn-hdrTop"></div>');

            var $tl = $('<div class="MPC-vasgrn-hdrTitleWrap"></div>');
            $tl.append($('<div class="MPC-vasgrn-hdrTitle"></div>').text(
                VIS.Msg.getMsg("VAS_099_GoodsReceiptNote") +
                (data.DocumentNo ? " — " + data.DocumentNo : "")));

            var subBits = [];
            var received = formatDate(data.MovementDate);
            if (received) subBits.push(VIS.Msg.getMsg("VAS_099_Received") + " " + received);
            if (data.ReceivedBy) subBits.push(VIS.Msg.getMsg("VAS_099_ReceivedBy") + " " + data.ReceivedBy);
            if (subBits.length) {
                $tl.append($('<div class="MPC-vasgrn-hdrSub"></div>').text(subBits.join(" · ")));
            }
            $top.append($tl);

            var $pills = $('<div class="MPC-vasgrn-hdrPills"></div>');
            if (pm) $pills.append(headerPill(pm.label, pm.tone, "chevUp", false));
            $pills.append(headerPill(st.label, st.tone, null, true));
            // Posted status shown as a pill on the right (moved out of the vendor
            // details column): green when posted, neutral when not.
            $pills.append(headerPill(
                data.Posted ? VIS.Msg.getMsg("VAS_099_Posted") : msg("VAS_099_NotPosted", "Not Posted"),
                data.Posted ? "success" : "neutral", null, false));
            $top.append($pills);

            $strip.append($top);
            $body.append($strip);

            // --- Details card: supplier identity (left) + receipt fields (right) ---
            var $card = $('<section class="MPC-vasgrn-hdrCard"></section>');

            // Left column: vendor name + GSTIN + address + contact bits.
            var $left = $('<div class="MPC-vasgrn-hdrColL"></div>');
            $left.append($('<div class="MPC-vasgrn-fLabel"></div>').text(VIS.Msg.getMsg("VAS_099_Vendor")));
            $left.append($('<div class="MPC-vasgrn-vendName"></div>').text(na(data.VendorName)));

            if (data.VendorTaxID) {
                var $gst = $('<div class="MPC-vasgrn-vendGst"></div>');
                $gst.append($('<span class="MPC-vasgrn-gstLbl"></span>')
                    .text(VIS.Msg.getMsg("VAS_099_GSTIN") + " "));
                $gst.append($('<span></span>').text(data.VendorTaxID));
                $left.append($gst);
            }

            if (data.VendorAddress) {
                var $addr = $('<div class="MPC-vasgrn-vendAddr"></div>');
                $addr.append(svgIcon("pin"));
                $addr.append($('<span></span>').text(data.VendorAddress));
                $left.append($addr);
            }
            var $contact = $('<div class="MPC-vasgrn-vendContact"></div>');
            appendContactBit($contact, "user",  data.ContactName);
            appendContactBit($contact, "phone", data.ContactPhone);
            appendContactBit($contact, "mail",  data.ContactEmail);
            if ($contact.children().length) $left.append($contact);
            $card.append($left);

            // Right column: labelled receipt / reference fields.
            var $right = $('<div class="MPC-vasgrn-hdrColR"></div>');
            // Manually-created receipts have no purchase order — Purchase Order and
            // Expected Delivery both read N/A (na() handles the blank values).
            $right.append(headerField(VIS.Msg.getMsg("VAS_099_AgainstPO"),
                na(data.PONo), !!data.PONo));
            $right.append(headerField(msg("VAS_099_ExpectedDelivery", "Expected Delivery"),
                na(formatDate(data.ExpectedDate)), false));
            $right.append(headerField(VIS.Msg.getMsg("VAS_099_Warehouse"), na(data.WarehouseName), false));
            $right.append(headerField(VIS.Msg.getMsg("VAS_099_ReceivedDate"),
                na(formatDate(data.MovementDate)), false));
            // Drop Shipment flag (M_InOut.IsDropShip).
            $right.append(headerField(msg("VAS_099_DropShipment", "Drop Shipment"),
                data.IsDropShip ? msg("VAS_099_Yes", "Yes") : msg("VAS_099_No", "No"), false));
            // Reference Invoice / Reference Sales Order live in the References
            // section below, not here.
            $card.append($right);

            $body.append($card);
        }

        // ---------- References (PO + linked documents) ---------- //

        // The receipt's reference documents in one block: the purchase order it
        // was raised against (number + order date + the vendor's own reference),
        // the AP invoice raised for that order, and the originating sales order.
        // Every field falls back to N/A so the grid never shows a blank cell.
        function renderReferences() {
            var $sec = section(msg("VAS_099_References", "References"), null);

            var $card = $('<div class="MPC-vasgrn-refCard"></div>');
            $card.append(headerField(VIS.Msg.getMsg("VAS_099_AgainstPO"),
                na(data.PONo), !!data.PONo));
            $card.append(headerField(msg("VAS_099_PODate", "PO Date"),
                na(formatDate(data.PODate)), false));
            $card.append(headerField(msg("VAS_099_VendorRef", "Vendor Ref"),
                na(data.POReference), false));
            // Neither reference is clickable, so they keep the plain value colour;
            // only the PO number carries the link tint, as in the header card.
            $card.append(headerField(VIS.Msg.getMsg("VAS_099_ReferenceInvoice"),
                na(data.ReferenceInvoice), false));
            $card.append(headerField(VIS.Msg.getMsg("VAS_099_ReferenceSalesOrder"),
                na(data.RefOrderDocNo), false));

            $sec.append($card);
        }

        // Header pill: tinted chip with an optional leading chevron (priority) or
        // a leading dot (status).
        function headerPill(label, tone, icon, withDot) {
            var $p = $('<span class="MPC-vasgrn-hdrPill"></span>')
                .addClass("tone-" + (tone || "neutral"));
            if (icon) $p.append(svgIcon(icon));
            if (withDot) $p.append($('<span class="MPC-vasgrn-hdrDot"></span>'));
            $p.append($('<span></span>').text(label));
            return $p;
        }

        // Labelled field block for the details card's right column. When `link`
        // is set the value is rendered in the link colour (e.g. the PO number).
        function headerField(label, value, link) {
            var $f = $('<div class="MPC-vasgrn-hdrField"></div>');
            $f.append($('<div class="MPC-vasgrn-fLabel"></div>').text(label));
            var $v = $('<div class="MPC-vasgrn-fVal"></div>').text(value);
            if (link) $v.addClass("is-link");
            $f.append($v);
            return $f;
        }

        function appendContactBit($container, icon, value) {
            if (!value) return;
            var $bit = $('<span class="MPC-vasgrn-contactBit"></span>');
            $bit.append(svgIcon(icon));
            $bit.append($('<span></span>').text(value));
            $container.append($bit);
        }

        // ---------- Snapshot (metric grid) ---------- //

        function renderSnapshot() {
            var qc = qualityApplicable();

            var $snap = $('<section class="MPC-vasgrn-snap"></section>');
            // With the two quality cards in play the grid runs three-up, so the six
            // cards form two even rows instead of a 4 + 2 orphan.
            if (qc) $snap.addClass("has-qc");
            var cur = currencyToken();

            // Received value.
            $snap.append(metricCard("total", "coins", VIS.Msg.getMsg("VAS_099_ReceivedValue"),
                formatAmount(+data.ReceivedValue || 0, cur, data.StdPrecision),
                data.ISO_Code || ""));

            // Lines received.
            $snap.append(metricCard("lines", "box", VIS.Msg.getMsg("VAS_099_Lines"),
                (data.LineCount || 0) + "",
                VIS.Msg.getMsg("VAS_099_LinesReceived")));

            // Received quantity (units).
            $snap.append(metricCard("received", "inbox", VIS.Msg.getMsg("VAS_099_ReceivedQty"),
                formatNumber(+data.ReceivedQty || 0, 0),
                VIS.Msg.getMsg("VAS_099_Units")));

            // Quality check applicability (Applicable when any QC line exists).
            $snap.append(metricCard("quality", "clipboardCheck", VIS.Msg.getMsg("VAS_099_QualityCheck"),
                qc ? VIS.Msg.getMsg("VAS_099_Applicable") : VIS.Msg.getMsg("VAS_099_NotApplicable"),
                qc ? ((data.QcLineCount || 0) + " " + VIS.Msg.getMsg("VAS_099_QCLines")) : ""));

            // Quality inspection outcome — only meaningful, and only shown, when a
            // quality check applies to this receipt. The quantities come from the
            // receipt confirmation lines: ConfirmedQty passed, ScrappedQty failed.
            if (qc) {
                $snap.append(metricCard("accepted", "checkCircle",
                    msg("VAS_099_AcceptedQty", "Accepted Qty"),
                    formatNumber(+data.AcceptedQty || 0, 0),
                    msg("VAS_099_UnitsPassedQC", "units passed QC")));

                $snap.append(metricCard("rejected", "xCircle",
                    msg("VAS_099_RejectedQty", "Rejected Qty"),
                    formatNumber(+data.RejectedQty || 0, 0),
                    msg("VAS_099_UnitsFailedQC", "units failed QC")));
            }

            $body.append($snap);
        }

        // Metric card: colour-accented left border (via tone class), a header
        // (icon + label), a large value and a caption.
        function metricCard(tone, icon, label, value, sub) {
            var $c = $('<div class="MPC-vasgrn-metric"></div>').addClass("tone-" + tone);

            var $head = $('<div class="MPC-vasgrn-mHead"></div>');
            $head.append(svgIcon(icon));
            $head.append($('<span class="MPC-vasgrn-mLabel"></span>').text(label));
            $c.append($head);

            $c.append($('<div class="MPC-vasgrn-mVal"></div>').text(value));
            if (sub) $c.append($('<div class="MPC-vasgrn-mSub"></div>').text(sub));
            return $c;
        }

        // ---------- Receipt timeline (horizontal stepper) ---------- //

        // The receipt's document lifecycle: Drafted -> In Progress -> Completed ->
        // Posted -> Invoiced. Each stage is driven by document state, not by the
        // presence of a date; the date is only the caption underneath.
        //   Drafted     — the record exists, so always done (captioned with Created).
        //   In Progress — the document has moved past draft (any of IP/AP/WC/WP)
        //                 or reached a terminal state (CO/CL/VO/RE). Invalid (IN)
        //                 and Not Approved (NA) do not count as progressed.
        //   Completed   — DocStatus CO or CL, captioned with the movement date.
        //   Posted      — M_InOut.Posted, captioned with the date posting actually
        //                 ran (PostedDate, from the receipt's Fact_Acct rows). The
        //                 accounting date is only a fallback: it is the date the
        //                 posting was booked to and normally equals the movement
        //                 date, which is not what this stage is reporting.
        //   Invoiced    — an AP invoice exists for the receipt's purchase order.
        var PROGRESSED_STATUS = ["IP", "AP", "WC", "WP", "CO", "CL", "VO", "RE"];

        function timelineStages() {
            var st = data.StatusCode;
            var progressed = PROGRESSED_STATUS.indexOf(st) >= 0;
            var completed  = (st === "CO" || st === "CL");

            return [
                { key: "VAS_099_Drafted",    fallback: "Drafted",     date: data.Created,             done: true },
                { key: "VAS_099_InProgress", fallback: "In Progress", date: progressed ? data.Updated : null, done: progressed },
                { key: "VAS_099_Completed",  fallback: "Completed",   date: completed ? data.MovementDate : null, done: completed },
                { key: "VAS_099_Posted",     fallback: "Posted",      date: data.Posted ? (data.PostedDate || data.PostingDate) : null, done: !!data.Posted },
                { key: "VAS_099_Invoiced",   fallback: "Invoiced",    date: data.ReferenceInvoiceDate, done: !!data.ReferenceInvoice }
            ];
        }

        function renderTimeline() {
            var stages = timelineStages();

            var activeIdx = -1;
            for (var k = 0; k < stages.length; k++) {
                if (stages[k].done) activeIdx = k;
            }

            var $sec = section(VIS.Msg.getMsg("VAS_099_ReceiptTimeline"), null);

            var $tl = $('<div class="MPC-vasgrn-stepper"></div>');
            for (var i = 0; i < stages.length; i++) {
                var s = stages[i];

                var stateCls, metaText;
                if (i === activeIdx) {
                    stateCls = "is-active";
                    metaText = formatDate(s.date) || VIS.Msg.getMsg("VAS_099_Done");
                } else if (s.done) {
                    stateCls = "is-done";
                    metaText = formatDate(s.date) || VIS.Msg.getMsg("VAS_099_Done");
                } else {
                    stateCls = "is-pending";
                    metaText = VIS.Msg.getMsg("VAS_099_Pending");
                }

                $tl.append(stepEntry(i + 1, msg(s.key, s.fallback), metaText, s.done, stateCls));
            }
            $sec.append($tl);
        }

        // Stepper node: connector rail (left line + circle + right line) above a
        // centred label. The circle shows a check when done, else its number.
        function stepEntry(num, title, meta, done, stateCls) {
            var $entry = $('<div class="MPC-vasgrn-step"></div>').addClass(stateCls || "");

            var $rail = $('<div class="MPC-vasgrn-stepRail"></div>');
            $rail.append($('<span class="MPC-vasgrn-stepLine MPC-vasgrn-stepLine-l"></span>'));
            var $dot = $('<span class="MPC-vasgrn-stepDot"></span>');
            if (done) {
                $dot.append(svgIcon("check"));
            } else {
                $dot.text(num);
            }
            $rail.append($dot);
            $rail.append($('<span class="MPC-vasgrn-stepLine MPC-vasgrn-stepLine-r"></span>'));
            $entry.append($rail);

            var $lbl = $('<div class="MPC-vasgrn-stepLabel"></div>');
            $lbl.append($('<div class="MPC-vasgrn-stepTitle"></div>').text(title));
            if (meta) $lbl.append($('<div class="MPC-vasgrn-stepMeta"></div>').text(meta));
            $entry.append($lbl);

            return $entry;
        }

        // ---------- Material lines (table) ---------- //

        function renderLines() {
            var lines = (data && data.Lines) || [];
            if (!lines.length) return;

            var cur = currencyToken();

            var $sec = section(VIS.Msg.getMsg("VAS_099_MaterialLines"), {
                summary: (data.LineCount || 0) + " " + VIS.Msg.getMsg("VAS_099_Items") + " · " +
                    formatNumber(+data.ReceivedQty || 0, 0) + " " + VIS.Msg.getMsg("VAS_099_Units")
            });

            // The Quality column is only meaningful when a quality check actually
            // applies to something on this receipt — with none, the column (header
            // and every cell) is dropped and the grid falls back to six columns.
            var showQuality = false;
            for (var q = 0; q < lines.length; q++) {
                if (lines[q].QualityApplicable) { showQuality = true; break; }
            }

            var $tbl = $('<div class="MPC-vasgrn-table"></div>');
            if (!showQuality) $tbl.addClass("no-quality");

            // Header row
            var $head = $('<div class="MPC-vasgrn-tRow MPC-vasgrn-tHead"></div>');
            $head.append($('<span></span>').text(VIS.Msg.getMsg("VAS_099_Item")));
            $head.append($('<span></span>').text(VIS.Msg.getMsg("VAS_099_UOM")));
            $head.append($('<span class="ta-r"></span>').text(VIS.Msg.getMsg("VAS_099_Ordered")));
            $head.append($('<span class="ta-r"></span>').text(VIS.Msg.getMsg("VAS_099_Received")));
            $head.append($('<span class="ta-r"></span>').text(VIS.Msg.getMsg("VAS_099_Rate")));
            $head.append($('<span class="ta-r"></span>').text(VIS.Msg.getMsg("VAS_099_Amount")));
            if (showQuality) {
                $head.append($('<span class="ta-c"></span>').text(VIS.Msg.getMsg("VAS_099_Quality")));
            }
            $tbl.append($head);

            // Totals footer — always the whole receipt, never just the page.
            var $foot = $('<div class="MPC-vasgrn-tFoot"></div>');
            var $bit = $('<span class="MPC-vasgrn-tf is-grand"></span>');
            $bit.append(document.createTextNode(VIS.Msg.getMsg("VAS_099_TotalReceivedValue")));
            $bit.append($('<b></b>').text(formatAmount(+data.ReceivedValue || 0, cur, data.StdPrecision)));
            $foot.append($bit);
            $tbl.append($foot);

            $sec.append($tbl);

            // The pager sits outside the table: the table gets its own horizontal
            // scroll on narrow panels, and the controls must not scroll away with
            // the columns.
            var $pager = $('<div class="MPC-vasgrn-pager"></div>');
            if (lines.length > LINES_PER_PAGE) $sec.append($pager);

            // Rows are replaced in place, ahead of the totals footer, so the
            // table's existing structure and its CSS grid stay exactly as they
            // were — no wrapper element between the table and its rows.
            function paintPage() {
                var pageCount = Math.max(1, Math.ceil(lines.length / LINES_PER_PAGE));
                if (linesPage >= pageCount) linesPage = pageCount - 1;
                if (linesPage < 0) linesPage = 0;

                var start = linesPage * LINES_PER_PAGE;
                var end = Math.min(lines.length, start + LINES_PER_PAGE);

                $tbl.find(".MPC-vasgrn-tBody").remove();
                for (var i = start; i < end; i++) {
                    $foot.before(buildLineRow(lines[i], cur, showQuality));
                }

                buildPager($pager, lines.length, pageCount, start, end, paintPage);
            }

            paintPage();
        }

        // Renders the pager into $pager: a range caption on the left, Previous /
        // page-of / Next on the right. Rebuilt on every page change so the
        // disabled states stay accurate.
        function buildPager($pager, total, pageCount, start, end, onChange) {
            $pager.empty();
            if (total <= LINES_PER_PAGE) return;

            $pager.append($('<span class="MPC-vasgrn-pgRange"></span>').text(
                msg("VAS_099_Showing", "Showing") + " " + (start + 1) + "-" + end + " " +
                msg("VAS_099_Of", "of") + " " + total));

            var $ctrls = $('<span class="MPC-vasgrn-pgCtrls"></span>');

            $ctrls.append(pagerButton(msg("VAS_099_Previous", "Previous"), "chevLeft",
                linesPage <= 0, function () { linesPage--; onChange(); }));

            $ctrls.append($('<span class="MPC-vasgrn-pgPos"></span>').text(
                msg("VAS_099_Page", "Page") + " " + (linesPage + 1) + " " +
                msg("VAS_099_Of", "of") + " " + pageCount));

            $ctrls.append(pagerButton(msg("VAS_099_Next", "Next"), "chevRight",
                linesPage >= pageCount - 1, function () { linesPage++; onChange(); }));

            $pager.append($ctrls);
        }

        function pagerButton(label, icon, disabled, handler) {
            var $b = $('<span class="MPC-vasgrn-pgBtn"></span>');
            if (icon === "chevLeft") $b.append(svgIcon(icon));
            $b.append($('<span></span>').text(label));
            if (icon === "chevRight") $b.append(svgIcon(icon));
            if (disabled) {
                $b.addClass("is-disabled");
            } else {
                $b.on("click", handler);
            }
            return $b;
        }

        function buildLineRow(ln, cur, showQuality) {
            var $tr = $('<div class="MPC-vasgrn-tRow MPC-vasgrn-tBody"></div>');

            // Item (name + product code / locator). The name cell ellipsises, so
            // the full product name goes on a hover tooltip — same treatment as the
            // locator below, and it leaves the layout untouched.
            var $item = $('<span class="MPC-vasgrn-itItem"></span>');
            var $name = $('<div class="MPC-vasgrn-itName"></div>').text(na(ln.ProductName));
            if (ln.ProductName) $name.attr("title", ln.ProductName);
            $item.append($name);

            // Sub-line: product search key (no "SKU" prefix) · locator. The line
            // ellipsises when the column is narrow, so the locator carries the full
            // name as a hover tooltip — readable without widening the layout.
            var $sku = $('<div class="MPC-vasgrn-itSku"></div>');
            if (ln.ProductCode) {
                $sku.append($('<span></span>').text(ln.ProductCode));
            }
            if (ln.LocatorName) {
                if ($sku.children().length) $sku.append(document.createTextNode(" · "));
                $sku.append($('<span class="MPC-vasgrn-itLoc"></span>')
                    .text(VIS.Msg.getMsg("VAS_099_Locator") + " " + ln.LocatorName)
                    .attr("title", ln.LocatorName));
            }
            if ($sku.children().length) {
                $item.append($sku);
            } else if (ln.Description) {
                $item.append($('<div class="MPC-vasgrn-itSku"></div>').text(ln.Description));
            }
            // Attribute Set Instance details (size / lot / serial ...), only when
            // the line carries a real instance — a blank / "--" placeholder is not
            // shown.
            var asi = (ln.AttributeSetInstance || "").trim();
            if (asi && asi !== "--" && asi !== "-") {
                $item.append($('<div class="MPC-vasgrn-itAttr"></div>').text(asi).attr("title", asi));
            }
            $tr.append($item);

            var prec = +ln.UOMPrecision || 0;

            // UOM
            $tr.append($('<span></span>').text(na(ln.UOMName)));

            // Ordered
            $tr.append($('<span class="ta-r"></span>').text(formatNumber(+ln.OrderedQty || 0, prec)));

            // Received — the figure on its own line so it lands on the same
            // baseline and right edge as Ordered, with the progress bar stacked
            // underneath rather than pushing the number out of the column.
            var ordered = +ln.OrderedQty || 0;
            var received = +ln.ReceivedQty || 0;
            var pct = ordered > 0 ? Math.min(100, Math.round((received / ordered) * 100)) : (received > 0 ? 100 : 0);
            var recvState = ordered > 0 && received >= ordered ? "full" : (received > 0 ? "part" : "none");
            var $recv = $('<span class="MPC-vasgrn-recv ta-r"></span>').addClass(recvState);
            $recv.append($('<span class="MPC-vasgrn-recvVal"></span>').text(formatNumber(received, prec)));
            var $bar = $('<span class="MPC-vasgrn-recvBar"><i></i></span>');
            $bar.find("i").css("width", Math.max(0, Math.min(100, pct)) + "%");
            $recv.append($bar);
            $tr.append($recv);

            // Rate
            $tr.append($('<span class="ta-r"></span>').text(
                formatAmount(+ln.UnitRate || 0, cur, data.StdPrecision)));

            // Amount
            $tr.append($('<span class="ta-r"></span>').text(
                formatAmount(+ln.LineValue || 0, cur, data.StdPrecision)));

            // Quality marker — omitted entirely when no line on this receipt has a
            // quality check applicable (the column itself is not rendered then).
            if (showQuality) {
                var $q = $('<span class="ta-c"></span>');
                if (ln.QualityApplicable) {
                    $q.append($('<span class="MPC-vasgrn-tag q-on"></span>')
                        .text(VIS.Msg.getMsg("VAS_099_Applicable")));
                } else {
                    $q.append($('<span class="MPC-vasgrn-tag q-off"></span>')
                        .text(VIS.Msg.getMsg("VAS_099_NotApplicable")));
                }
                $tr.append($q);
            }

            return $tr;
        }

        // ---------- Quality Product (VA010 inspection parameters) ---------- //

        // QC result code -> { key, fallback, tone }. "N" is a parameter that has
        // not been inspected yet, not a failure.
        var QC_STATUS_MAP = {
            "P": { key: "VAS_099_Passed",  fallback: "Passed",  tone: "q-pass" },
            "F": { key: "VAS_099_Failed",  fallback: "Failed",  tone: "q-fail" },
            "N": { key: "VAS_099_Pending", fallback: "Pending", tone: "q-wait" }
        };

        // The quality parameters configured against each product on the receipt —
        // colour, size, grade or whatever the quality plan defines — with the
        // acceptable value, the inspected value and the resulting verdict. Rendered
        // whenever a quality check applies; when it does not, the section is absent
        // entirely.
        function renderQualityProducts() {
            if (!qualityApplicable()) return;

            var rows = (data && data.QualityParams) || [];

            var $sec = section(msg("VAS_099_QualityProduct", "Quality Product"), {
                summary: rows.length
                    ? rows.length + " " + msg("VAS_099_Parameters", "parameters")
                    : ""
            });

            // A quality check applies but nothing has been recorded yet — say so
            // rather than showing an empty frame.
            if (!rows.length) {
                $sec.append($('<div class="MPC-vasgrn-qpEmpty"></div>')
                    .text(msg("VAS_099_NoQualityParams",
                              "No quality parameters have been recorded for this receipt yet.")));
                return;
            }

            var $tbl = $('<div class="MPC-vasgrn-table MPC-vasgrn-qpTable"></div>');

            var $head = $('<div class="MPC-vasgrn-tRow MPC-vasgrn-tHead"></div>');
            $head.append($('<span></span>').text(msg("VAS_099_Product", "Product")));
            $head.append($('<span></span>').text(msg("VAS_099_Parameter", "Parameter")));
            $head.append($('<span class="ta-r"></span>').text(msg("VAS_099_ToVerify", "To Verify")));
            $head.append($('<span></span>').text(msg("VAS_099_AcceptableValue", "Acceptable")));
            $head.append($('<span></span>').text(msg("VAS_099_ActualValue", "Actual")));
            $head.append($('<span></span>').text(msg("VAS_099_QADate", "QA Date")));
            $head.append($('<span class="ta-c"></span>').text(msg("VAS_099_Status", "Status")));
            $head.append($('<span class="ta-c"></span>').text(msg("VAS_099_Confirmation", "Confirmation")));
            $tbl.append($head);

            for (var i = 0; i < rows.length; i++) {
                $tbl.append(buildQualityRow(rows[i]));
            }

            $sec.append($tbl);
        }

        function buildQualityRow(q) {
            var $tr = $('<div class="MPC-vasgrn-tRow MPC-vasgrn-tBody"></div>');

            // Product (name + search key), with the full name on hover since the
            // cell ellipsises.
            var $prod = $('<span class="MPC-vasgrn-itItem"></span>');
            var pname = na(q.ProductName);
            $prod.append($('<div class="MPC-vasgrn-itName"></div>').text(pname).attr("title", pname));
            if (q.ProductCode) {
                $prod.append($('<div class="MPC-vasgrn-itSku"></div>').text(q.ProductCode));
            }
            $tr.append($prod);

            // Parameter (Colour / Size / Grade ...), with the QA remark beneath it
            // when one was entered.
            var $param = $('<span class="MPC-vasgrn-itItem"></span>');
            $param.append($('<div class="MPC-vasgrn-qpName"></div>').text(na(q.ParameterName)));
            var remark = (q.Remark || "").trim();
            if (remark) {
                $param.append($('<div class="MPC-vasgrn-itSku"></div>')
                    .text(remark).attr("title", remark));
            }
            $tr.append($param);

            // Quantity to verify.
            $tr.append($('<span class="ta-r"></span>').text(formatNumber(+q.QuantityToVerify || 0, 0)));

            // Acceptable / actual value.
            $tr.append($('<span></span>').text(na(q.AcceptableValue)));
            $tr.append($('<span></span>').text(na(q.ActualValue)));

            // QA / QC date.
            $tr.append($('<span></span>').text(na(formatDate(q.QAQCDate))));

            // Verdict.
            var st = QC_STATUS_MAP[q.StatusCode] || QC_STATUS_MAP["N"];
            var $status = $('<span class="ta-c"></span>');
            $status.append($('<span class="MPC-vasgrn-tag"></span>')
                .addClass(st.tone).text(msg(st.key, st.fallback)));
            $tr.append($status);

            // Confirmation — Yes when the receipt uses a document type that asks
            // for a confirmation ("MM Receipt with Confirmation").
            $tr.append($('<span class="ta-c"></span>').text(
                data.IsConfirmationDocType ? msg("VAS_099_Yes", "Yes") : msg("VAS_099_No", "No")));

            return $tr;
        }

        // ---------- Notes (GRN header description) ---------- //

        // The description typed on the goods receipt header. Skipped when blank so
        // an empty card never trails the panel.
        function renderNotes() {
            var text = (data.Description || "").trim();
            if (!text) return;

            var $sec = section(msg("VAS_099_Notes", "Notes"), null);
            var $card = $('<div class="MPC-vasgrn-textCard"></div>');
            $card.append($('<p></p>').text(text));
            $sec.append($card);
        }

        // ---------- Activity (audit trail) ---------- //

        // Activity type -> tag label + tone + icon, and the sentence shown for it.
        var ACT_TYPES = {
            created:      { tone: "neutral", icon: "doc",            tagKey: "VAS_099_TagCreated",      tagText: "Created",      titleKey: "VAS_099_ActCreated",      titleText: "Goods receipt created" },
            updated:      { tone: "info",    icon: "pencil",         tagKey: "VAS_099_TagUpdated",      tagText: "Updated",      titleKey: "VAS_099_ActUpdated",      titleText: "Goods receipt updated" },
            completed:    { tone: "success", icon: "check",          tagKey: "VAS_099_TagCompleted",    tagText: "Completed",    titleKey: "VAS_099_ActCompleted",    titleText: "Goods receipt completed" },
            posted:       { tone: "purple",  icon: "coins",          tagKey: "VAS_099_TagPosted",       tagText: "Posted",       titleKey: "VAS_099_ActPosted",       titleText: "Posted to accounting" },
            confirmation: { tone: "warning", icon: "clipboardCheck", tagKey: "VAS_099_TagConfirmation", tagText: "Confirmation", titleKey: "VAS_099_ActConfirmation", titleText: "Receipt confirmation raised" },
            invoice:      { tone: "info",    icon: "doc",            tagKey: "VAS_099_TagInvoice",      tagText: "Invoice",      titleKey: "VAS_099_ActInvoice",      titleText: "Vendor invoice raised" },
            note:         { tone: "neutral", icon: "mail",           tagKey: "VAS_099_TagNote",         tagText: "Note",         titleKey: null,                      titleText: "" }
        };

        // The receipt's audit trail, newest first: who created it, who changed it
        // and when, when it was completed and posted, plus the confirmations,
        // invoices and notes raised against it.
        function renderActivity() {
            var rows = (data && data.Activity) || [];
            if (!rows.length) return;

            var $sec = section(msg("VAS_099_Activity", "Activity"), {
                summary: rows.length + " " + msg("VAS_099_Updates", "updates")
            });

            var $list = $('<div class="MPC-vasgrn-actList"></div>');
            for (var i = 0; i < rows.length; i++) {
                $list.append(activityRow(rows[i]));
            }
            $sec.append($list);
        }

        function activityRow(a) {
            var meta = ACT_TYPES[a.Type] || ACT_TYPES.note;

            var $row = $('<div class="MPC-vasgrn-actRow"></div>');

            var $tag = $('<span class="MPC-vasgrn-actTag"></span>').addClass("tone-" + meta.tone);
            $tag.append(svgIcon(meta.icon));
            $tag.append($('<span></span>').text(msg(meta.tagKey, meta.tagText)));
            $row.append($tag);

            // For a note the title is the note text itself; for everything else it
            // is the event sentence plus the related document number. A tooltip
            // keeps a long line readable once the cell ellipsises.
            var title = activityTitle(a, meta);
            $row.append($('<span class="MPC-vasgrn-actTitle"></span>')
                .text(title).attr("title", title));

            // "when · by whom" — the audit trail's whole point.
            var when = formatDateTime(a.Created);
            if (a.UserName) {
                when = when
                    ? when + " · " + msg("VAS_099_By", "by") + " " + a.UserName
                    : msg("VAS_099_By", "by") + " " + a.UserName;
            }
            $row.append($('<span class="MPC-vasgrn-actWhen"></span>').text(when).attr("title", when));

            return $row;
        }

        function activityTitle(a, meta) {
            if (a.Type === "note") return (a.Text || "").trim();
            var title = meta.titleKey ? msg(meta.titleKey, meta.titleText) : (meta.titleText || "");
            if (a.DocumentNo) title += " — " + a.DocumentNo;
            return title;
        }

        // ---------- Actions (visual buttons) ---------- //

        // Document actions. Complete GRN runs the receipt's Complete (DocAction
        // "CO"); Generate Invoice creates the AP invoice for the receipt's linked
        // purchase order. Both confirm first, POST to the controller, then refresh
        // the panel. Buttons disable themselves when the action does not apply.
        function renderActions() {
            var $bar = $('<section class="MPC-vasgrn-actions"></section>');

            var isCompleted = (data.StatusCode === "CO" || data.StatusCode === "CL");
            var isClosedOff = (data.StatusCode === "VO" || data.StatusCode === "RE");
            var hasPO = +data.C_Order_ID > 0;

            // Complete GRN — only when the receipt is still open (not completed /
            // voided / reversed).
            var $complete = actionButton("check", VIS.Msg.getMsg("VAS_099_CompleteGRN"), "pri");
            if (isCompleted || isClosedOff) {
                disableBtn($complete);
            } else {
                $complete.on("click", function () {
                    if (!confirm(msg("VAS_099_ConfirmComplete",
                        "Complete this goods receipt? This will process the receipt."))) return;
                    runAction($complete, "CompleteGRN",
                        msg("VAS_099_CompleteFailed", "Could not complete the goods receipt."));
                });
            }
            $bar.append($complete);

            // Generate Invoice — needs a completed receipt with a linked PO. The
            // invoice covers the order's uninvoiced received quantity.
            var $invoice = actionButton("doc", VIS.Msg.getMsg("VAS_099_GenerateInvoice"), "sec");
            if (!isCompleted || !hasPO) {
                disableBtn($invoice);
            } else {
                $invoice.on("click", function () {
                    if (!confirm(msg("VAS_099_ConfirmInvoice",
                        "Generate and complete the AP invoice for this receipt's purchase order?"))) return;
                    runAction($invoice, "GenerateInvoice",
                        msg("VAS_099_InvoiceFailed", "Could not generate the invoice."));
                });
            }
            $bar.append($invoice);

            $body.append($bar);
        }

        function actionButton(icon, label, kind) {
            var $b = $('<span class="MPC-vasgrn-btn"></span>').addClass("btn-" + (kind || "sec"));
            $b.append(svgIcon(icon));
            $b.append($('<span></span>').text(label));
            return $b;
        }

        function disableBtn($b) { $b.addClass("is-disabled"); }

        // POSTs a document action to the controller, then refreshes the panel on
        // success. Guards against double-clicks while the action is in flight.
        function runAction($btn, endpoint, failMsg) {
            if ($btn.hasClass("is-disabled") || $btn.hasClass("is-busy")) return;
            $btn.addClass("is-busy");
            showBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + "VAS_099_OverviewGRN/" + endpoint,
                type: "POST",
                dataType: "json",
                data: { M_InOut_ID: $self.record_ID },
                success: function (raw) {
                    var res = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                    $btn.removeClass("is-busy");
                    if (res && res.success) {
                        toast(res.message || msg("VAS_099_Done", "Done."), false);
                        // Re-fetch so the status pill, timeline and buttons reflect
                        // the new document state.
                        $self.fetchData($self.record_ID);
                    } else {
                        showBusy(false);
                        toast((res && (res.error || res.message)) || failMsg, true);
                    }
                },
                error: function () {
                    $btn.removeClass("is-busy");
                    showBusy(false);
                    toast(failMsg, true);
                }
            });
        }

        // Lightweight self-contained toast.
        function toast(message, isError) {
            var $t = $('<div class="MPC-vasgrn-toast"></div>')
                .addClass(isError ? "err" : "ok").text(message);
            $root.append($t);
            setTimeout(function () { $t.addClass("show"); }, 10);
            setTimeout(function () {
                $t.removeClass("show");
                setTimeout(function () { $t.remove(); }, 300);
            }, 3600);
        }

        // ----------------------------------------------------------------- //
        //  Icon helpers                                                      //
        // ----------------------------------------------------------------- //

        var SVG_ICONS = {
            pin:      '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M20 10c0 6-8 12-8 12s-8-6-8-12a8 8 0 0 1 16 0Z"/><circle cx="12" cy="10" r="3"/></svg>',
            user:     '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"/><circle cx="12" cy="7" r="4"/></svg>',
            phone:    '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M22 16.92v3a2 2 0 0 1-2.18 2 19.79 19.79 0 0 1-8.63-3.07 19.5 19.5 0 0 1-6-6 19.79 19.79 0 0 1-3.07-8.67A2 2 0 0 1 4.11 2h3a2 2 0 0 1 2 1.72c.13.96.36 1.9.7 2.81a2 2 0 0 1-.45 2.11L8.09 9.91a16 16 0 0 0 6 6l1.27-1.27a2 2 0 0 1 2.11-.45c.91.34 1.85.57 2.81.7A2 2 0 0 1 22 16.92Z"/></svg>',
            mail:     '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="5" width="18" height="14" rx="2"/><path d="m3 7 9 6 9-6"/></svg>',
            box:      '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M21 8 12 3 3 8v8l9 5 9-5Z"/><path d="m3 8 9 5 9-5"/><path d="M12 13v8"/></svg>',
            coins:    '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><circle cx="8" cy="8" r="5"/><path d="M14.5 4.2a5 5 0 0 1 0 15.6"/><path d="M7 18.7a5 5 0 0 0 6 0"/></svg>',
            inbox:    '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M22 12h-6l-2 3h-4l-2-3H2"/><path d="M5.45 5.11 2 12v6a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-6l-3.45-6.89A2 2 0 0 0 16.76 4H7.24a2 2 0 0 0-1.79 1.11Z"/></svg>',
            clipboardCheck: '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><rect x="8" y="2" width="8" height="4" rx="1"/><path d="M16 4h2a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2h2"/><path d="m9 14 2 2 4-4"/></svg>',
            check:    '<svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"><path d="M20 6 9 17l-5-5"/></svg>',
            checkCircle: '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="9"/><path d="m8.5 12 2.5 2.5 4.5-5"/></svg>',
            xCircle:  '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="9"/><path d="m15 9-6 6"/><path d="m9 9 6 6"/></svg>',
            pencil:   '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M12 20h9"/><path d="M16.5 3.5a2.12 2.12 0 0 1 3 3L7 19l-4 1 1-4Z"/></svg>',
            chevLeft: '<svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round"><path d="m15 18-6-6 6-6"/></svg>',
            chevRight:'<svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round"><path d="m9 18 6-6-6-6"/></svg>',
            chevUp:   '<svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round"><path d="m18 15-6-6-6 6"/></svg>',
            doc:      '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8Z"/><path d="M14 2v6h6"/><path d="M8 13h8"/><path d="M8 17h8"/></svg>',
            printer:  '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M6 9V2h12v7"/><path d="M6 18H4a2 2 0 0 1-2-2v-5a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2v5a2 2 0 0 1-2 2h-2"/><rect x="6" y="14" width="12" height="8"/></svg>'
        };

        // Returns a span wrapping the named inline SVG (innerHTML so the browser
        // parses the SVG in HTML context — no namespace juggling).
        function svgIcon(name) {
            var $wrap = $('<span class="MPC-vasgrn-ic"></span>');
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

        // Date + time — the audit trail needs the time of day, not just the date.
        function formatDateTime(value) {
            if (!value) return "";
            var d = (value instanceof Date) ? value : new Date(value);
            if (isNaN(d.getTime())) return "";
            try {
                return d.toLocaleDateString(window.navigator.language, {
                    year: "numeric", month: "short", day: "2-digit"
                }) + " " + d.toLocaleTimeString(window.navigator.language, {
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

    VAS.VAS_099_OverviewGRN.prototype.startPanel = function (windowNo, curTab) {
        this.windowNo = windowNo;
        this.curTab = curTab;
        if (curTab && typeof curTab.getAD_Table_ID === "function") {
            this.table_ID = curTab.getAD_Table_ID();
        }
        this.init();
    };

    /* Update tab panel based on selected record */
    VAS.VAS_099_OverviewGRN.prototype.refreshPanelData = function (recordID, selectedRow) {
        if (selectedRow == undefined || recordID <= 0) {
            this.clear();
            return;
        }
        this.record_ID = recordID;
        this.selectedRow = selectedRow;
        this.fetchData(recordID);
    };

    /* Set width as per window width */
    VAS.VAS_099_OverviewGRN.prototype.sizeChanged = function (width) {
        this.panelWidth = width;
    };

    /* Release variables from memory */
    VAS.VAS_099_OverviewGRN.prototype.dispose = function () {
        this.record_ID = 0;
        this.table_ID = 0;
        this.windowNo = 0;
        this.curTab = null;
        this.selectedRow = null;
        this.panelWidth = null;
    };

})(VAS, jQuery);
