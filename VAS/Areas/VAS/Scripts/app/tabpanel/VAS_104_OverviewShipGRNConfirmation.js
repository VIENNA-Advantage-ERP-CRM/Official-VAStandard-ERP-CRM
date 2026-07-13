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
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {

    VAS.VAS_104_OverviewShipGRNConfirmation = function () {
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

        this.init = function () {
            $root = $('<div class="MPC-vasgc-root"></div>');
            $body = $('<div class="MPC-vasgc-body"></div>');
            $emptyState = $('<div class="MPC-vasgc-empty" style="display:none;"></div>');
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
            render();
        };

        function qualityMode() {
            return !!(data && data.QualityApplicable);
        }

        function render() {
            $body.empty();
            // Panel-level modifier: hides .qc-only when quality does not apply.
            $root.toggleClass("no-quality", !qualityMode());

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
            renderActions();
        }

        // ----------------------------------------------------------------- //
        //  Small builders                                                    //
        // ----------------------------------------------------------------- //

        function section(title, opts, $parent) {
            opts = opts || {};
            var $sec = $('<section class="MPC-vasgc-sec"></section>');
            var $head = $('<div class="MPC-vasgc-secHead"></div>');
            $head.append($('<h2 class="MPC-vasgc-secTitle"></h2>').text(title));
            if (opts.summary) {
                $head.append($('<span class="MPC-vasgc-secSummary"></span>').text(opts.summary));
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

        function confirmTypeLabel() {
            var k = CONFIRMTYPE_MAP[data.ConfirmTypeCode];
            return k ? VIS.Msg.getMsg(k) : na(data.ConfirmTypeCode);
        }

        function sourceTypeLabel() {
            return (data.SourceTypeCode === "SHP")
                ? VIS.Msg.getMsg("VAS_104_Shipment")
                : VIS.Msg.getMsg("VAS_104_GoodsReceipt");
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

        // Per-line status per the documented rule:
        //  Hold    when difference != 0 or scrapped > 0
        //  Cleared when confirmed = target (and nothing held)
        //  Partial when confirmed > 0
        //  Pending otherwise (nothing confirmed yet)
        function lineStatus(ln) {
            var target = +ln.TargetQty || 0;
            var confirmed = +ln.ConfirmedQty || 0;
            var difference = +ln.DifferenceQty || 0;
            var scrapped = +ln.ScrappedQty || 0;
            if (difference !== 0 || scrapped > 0) return "hold";
            if (confirmed === target) return "cleared";
            if (confirmed > 0) return "partial";
            return "pending";
        }

        // ---------- Header (title strip + details card) ---------- //

        function renderHeader() {
            var st = statusMeta(data.StatusCode);
            var qm = qualityMode();

            var $strip = $('<section class="MPC-vasgc-hdr"></section>');
            var $top = $('<div class="MPC-vasgc-hdrTop"></div>');

            var $tl = $('<div class="MPC-vasgc-hdrTitleWrap"></div>');
            $tl.append($('<div class="MPC-vasgc-hdrEyebrow"></div>').text(
                (data.SourceTypeCode === "SHP")
                    ? VIS.Msg.getMsg("VAS_104_OutboundShipment")
                    : VIS.Msg.getMsg("VAS_104_GoodsReceiptNote")));
            $tl.append($('<div class="MPC-vasgc-hdrTitle"></div>').text(na(data.DocumentNo)));

            var subBits = [];
            if (data.PartyName) subBits.push(data.PartyName);
            var moved = formatDate(data.MovementDate);
            if (moved) subBits.push(VIS.Msg.getMsg("VAS_104_MovementDate") + " " + moved);
            if (subBits.length) {
                $tl.append($('<div class="MPC-vasgc-hdrSub"></div>').text(subBits.join(" · ")));
            }
            $top.append($tl);

            var $pills = $('<div class="MPC-vasgc-hdrPills"></div>');
            $pills.append(headerPill(sourceTypeLabel(), "info", "box", false));

            // QC hold warning (quality mode only) or no-quality marker.
            if (qm) {
                var fails = qcFailCount();
                if (fails > 0) {
                    $pills.append(headerPill(
                        fails + " " + VIS.Msg.getMsg("VAS_104_LinesFailedQC"), "risk", "alert", false));
                }
            } else {
                $pills.append(headerPill(VIS.Msg.getMsg("VAS_104_QualityNotApplicable"), "neutral", null, false));
            }

            $pills.append(headerPill(st.label, st.tone, null, true));
            $top.append($pills);

            $strip.append($top);
            $body.append($strip);

            // --- Source & receipt details card ---
            var $card = $('<section class="MPC-vasgc-hdrCard"></section>');

            var $left = $('<div class="MPC-vasgc-hdrColL"></div>');
            $left.append($('<div class="MPC-vasgc-fLabel"></div>').text(VIS.Msg.getMsg("VAS_104_Party")));
            $left.append($('<div class="MPC-vasgc-vendName"></div>').text(na(data.PartyName)));
            $left.append(headerField(VIS.Msg.getMsg("VAS_104_ConfirmationType"), confirmTypeLabel(), false));

            var $contact = $('<div class="MPC-vasgc-vendContact"></div>');
            appendContactBit($contact, "warehouse", data.WarehouseName);
            appendContactBit($contact, "calendar", formatDate(data.MovementDate));
            if ($contact.children().length) $left.append($contact);
            $card.append($left);

            var $right = $('<div class="MPC-vasgc-hdrColR"></div>');
            $right.append(headerField(VIS.Msg.getMsg("VAS_104_ConfirmationNo"), na(data.DocumentNo), false));
            $right.append(headerField(VIS.Msg.getMsg("VAS_104_SourceDocument"), na(data.SourceDocumentNo), true));
            $right.append(headerField(VIS.Msg.getMsg("VAS_104_Warehouse"), na(data.WarehouseName), false));            
            $card.append($right);

            $body.append($card);
        }

        function headerPill(label, tone, icon, withDot) {
            var $p = $('<span class="MPC-vasgc-hdrPill"></span>')
                .addClass("tone-" + (tone || "neutral"));
            if (icon) $p.append(svgIcon(icon));
            if (withDot) $p.append($('<span class="MPC-vasgc-hdrDot"></span>'));
            $p.append($('<span></span>').text(label));
            return $p;
        }

        function headerField(label, value, link) {
            var $f = $('<div class="MPC-vasgc-hdrField"></div>');
            $f.append($('<div class="MPC-vasgc-fLabel"></div>').text(label));
            var $v = $('<div class="MPC-vasgc-fVal"></div>').text(value);
            if (link && value !== VIS.Msg.getMsg("VAS_104_NA")) $v.addClass("is-link");
            $f.append($v);
            return $f;
        }

        function appendContactBit($container, icon, value) {
            if (!value) return;
            var $bit = $('<span class="MPC-vasgc-contactBit"></span>');
            $bit.append(svgIcon(icon));
            $bit.append($('<span></span>').text(value));
            $container.append($bit);
        }

        // ---------- Snapshot (KPI metric grid) ---------- //

        function renderSnapshot() {
            var $snap = $('<section class="MPC-vasgc-snap"></section>');

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
            var $c = $('<div class="MPC-vasgc-metric"></div>').addClass("tone-" + tone);
            if (qcOnly) $c.addClass("qc-only");

            var $head = $('<div class="MPC-vasgc-mHead"></div>');
            $head.append(svgIcon(icon));
            $head.append($('<span class="MPC-vasgc-mLabel"></span>').text(label));
            $c.append($head);

            $c.append($('<div class="MPC-vasgc-mVal"></div>').text(value));
            if (sub) $c.append($('<div class="MPC-vasgc-mSub"></div>').text(sub));
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

            var $tbl = $('<div class="MPC-vasgc-table"></div>');

            var $head = $('<div class="MPC-vasgc-tRow MPC-vasgc-tHead"></div>');
            $head.append($('<span></span>').text(VIS.Msg.getMsg("VAS_104_Item")));
            $head.append($('<span></span>').text(VIS.Msg.getMsg("VAS_104_Locator")));
            $head.append($('<span></span>').text(VIS.Msg.getMsg("VAS_104_UOM")));
            $head.append($('<span class="ta-r"></span>').text(VIS.Msg.getMsg("VAS_104_Target")));
            $head.append($('<span class="ta-r"></span>').text(VIS.Msg.getMsg("VAS_104_Confirmed")));
            $head.append($('<span class="ta-r"></span>').text(VIS.Msg.getMsg("VAS_104_Difference")));
            $head.append($('<span class="ta-r"></span>').text(VIS.Msg.getMsg("VAS_104_Scrapped")));
            $head.append($('<span class="ta-c qc-only"></span>').text(VIS.Msg.getMsg("VAS_104_QCMark")));
            $head.append($('<span class="ta-c"></span>').text(VIS.Msg.getMsg("VAS_104_Status")));
            $tbl.append($head);

            var totTarget = 0, totConfirmed = 0, totDiff = 0, totScrap = 0;

            for (var i = 0; i < lines.length; i++) {
                var ln = lines[i];
                totTarget    += (+ln.TargetQty || 0);
                totConfirmed += (+ln.ConfirmedQty || 0);
                totDiff      += (+ln.DifferenceQty || 0);
                totScrap     += (+ln.ScrappedQty || 0);
                $tbl.append(buildLineRow(ln));
            }

            // Totals footer
            var $foot = $('<div class="MPC-vasgc-tFoot"></div>');
            $foot.append(footBit(VIS.Msg.getMsg("VAS_104_Target"), formatNumber(totTarget, 0), false));
            $foot.append(footBit(VIS.Msg.getMsg("VAS_104_Confirmed"), formatNumber(totConfirmed, 0), false));
            $foot.append(footBit(VIS.Msg.getMsg("VAS_104_Difference"), signedNumber(totDiff, 0), false));
            $foot.append(footBit(VIS.Msg.getMsg("VAS_104_Scrapped"), formatNumber(totScrap, 0), true));
            $tbl.append($foot);

            return $tbl;
        }

        function footBit(label, value, grand) {
            var $b = $('<span class="MPC-vasgc-tf"></span>');
            if (grand) $b.addClass("is-grand");
            $b.append(document.createTextNode(label));
            $b.append($('<b></b>').text(value));
            return $b;
        }

        function buildLineRow(ln) {
            var $tr = $('<div class="MPC-vasgc-tRow MPC-vasgc-tBody"></div>');

            // Item (name + SKU)
            var $item = $('<span class="MPC-vasgc-itItem"></span>');
            $item.append($('<div class="MPC-vasgc-itName"></div>').text(na(ln.ProductName)));
            if (ln.ProductCode) {
                $item.append($('<div class="MPC-vasgc-itSku"></div>')
                    .text(VIS.Msg.getMsg("VAS_104_SKU") + " " + ln.ProductCode));
            } else if (ln.Description) {
                $item.append($('<div class="MPC-vasgc-itSku"></div>').text(ln.Description));
            }
            $tr.append($item);

            var prec = +ln.UOMPrecision || 0;

            // Locator
            $tr.append($('<span></span>').text(na(ln.LocatorName)));

            // UOM
            $tr.append($('<span></span>').text(na(ln.UOMName)));

            // Target
            $tr.append($('<span class="ta-r"></span>').text(formatNumber(+ln.TargetQty || 0, prec)));

            // Confirmed
            $tr.append($('<span class="ta-r"></span>').text(formatNumber(+ln.ConfirmedQty || 0, prec)));

            // Difference (signed; negative red, zero neutral)
            var diff = +ln.DifferenceQty || 0;
            var diffTone = diff < 0 ? "neg" : (diff > 0 ? "pos" : "zero");
            $tr.append($('<span class="ta-r MPC-vasgc-diff"></span>').addClass(diffTone)
                .text(signedNumber(diff, prec)));

            // Scrapped (red when > 0)
            var scrap = +ln.ScrappedQty || 0;
            $tr.append($('<span class="ta-r MPC-vasgc-scrap"></span>').toggleClass("has", scrap > 0)
                .text(formatNumber(scrap, prec)));

            // QC mark (qc-only) — Pass / Fail chip when quality applies to the line.
            var $qc = $('<span class="ta-c qc-only"></span>');
            if (ln.QualityApplicable) {
                var pass = ln.QcMark === "Y";
                $qc.append($('<span class="MPC-vasgc-tag"></span>')
                    .addClass(pass ? "s-pass" : "s-fail")
                    .text(pass ? VIS.Msg.getMsg("VAS_104_Pass") : VIS.Msg.getMsg("VAS_104_Fail")));
            } else {
                $qc.append($('<span class="MPC-vasgc-dash"></span>').text("—"));
            }
            $tr.append($qc);

            // Status tag
            var st = lineStatus(ln);
            var stKey = st === "cleared" ? "VAS_104_Cleared"
                      : (st === "partial" ? "VAS_104_Partial"
                      : (st === "hold" ? "VAS_104_Hold" : "VAS_104_Pending"));
            var $q = $('<span class="ta-c"></span>');
            $q.append($('<span class="MPC-vasgc-tag"></span>').addClass("s-" + st)
                .text(VIS.Msg.getMsg(stKey)));
            $tr.append($q);

            return $tr;
        }

        // ---------- Actions (visual buttons) ---------- //

        // Presentational only. The confirm button relabels by mode and is
        // disabled once the confirmation has been processed.
        function renderActions() {
            var processed = data.Processed;
            var confirmLabel = qualityMode()
                ? VIS.Msg.getMsg("VAS_104_ClearQualityConfirm")
                : VIS.Msg.getMsg("VAS_104_Confirm");

            var $bar = $('<section class="MPC-vasgc-actions"></section>');
            $bar.append(actionButton("print", VIS.Msg.getMsg("VAS_104_Print"), "sec", false));
            $bar.append(actionButton("check", confirmLabel, "pri", processed));
            $body.append($bar);
        }

        function actionButton(icon, label, kind, disabled) {
            var $b = $('<span class="MPC-vasgc-btn"></span>').addClass("btn-" + (kind || "sec"));
            if (disabled) $b.addClass("is-disabled");
            $b.append(svgIcon(icon));
            $b.append($('<span></span>').text(label));
            return $b;
        }

        // ----------------------------------------------------------------- //
        //  Icon helpers                                                      //
        // ----------------------------------------------------------------- //

        var SVG_ICONS = {
            warehouse: '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M3 21V8l9-5 9 5v13"/><path d="M7 21v-8h10v8"/></svg>',
            calendar:  '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="4" width="18" height="18" rx="2"/><path d="M16 2v4"/><path d="M8 2v4"/><path d="M3 10h18"/></svg>',
            box:       '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M21 8 12 3 3 8v8l9 5 9-5Z"/><path d="m3 8 9 5 9-5"/><path d="M12 13v8"/></svg>',
            layers:    '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="m12 2 9 5-9 5-9-5 9-5Z"/><path d="m3 12 9 5 9-5"/><path d="m3 17 9 5 9-5"/></svg>',
            delta:     '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M12 4 3 20h18Z"/></svg>',
            trash:     '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M3 6h18M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2m3 0v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6"/></svg>',
            shield:    '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10Z"/><path d="m9 12 2 2 4-4"/></svg>',
            alert:     '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M10.29 3.86 1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0Z"/><path d="M12 9v4"/><path d="M12 17h.01"/></svg>',
            check:     '<svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"><path d="M20 6 9 17l-5-5"/></svg>',
            print:     '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M6 9V2h12v7"/><path d="M6 18H4a2 2 0 0 1-2-2v-5a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2v5a2 2 0 0 1-2 2h-2"/><rect x="6" y="14" width="12" height="8"/></svg>'
        };

        function svgIcon(name) {
            var $wrap = $('<span class="MPC-vasgc-ic"></span>');
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
    };

    /* Update tab panel based on selected record */
    VAS.VAS_104_OverviewShipGRNConfirmation.prototype.refreshPanelData = function (recordID, selectedRow) {
        if (selectedRow == undefined || recordID <= 0) {
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
        this.record_ID = 0;
        this.table_ID = 0;
        this.windowNo = 0;
        this.curTab = null;
        this.selectedRow = null;
        this.panelWidth = null;
    };

})(VAS, jQuery);
