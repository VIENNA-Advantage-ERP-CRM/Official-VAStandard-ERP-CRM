/************************************************************
 * Module Name    : VAS
 * Purpose        : Internal Use / Material Issue Overview tab panel. Renders a
 *                  review-oriented overview of the selected internal-use material
 *                  issue (M_Inventory, IsInternalUse = 'Y'): header identity +
 *                  warehouse / issue details card, a four-card KPI snapshot
 *                  (issued value, quantity issued, not-fully-issued lines, total
 *                  lines), Full / Partial / Short status cards, a compact issue
 *                  timeline (Created -> Issued -> Posting), an issue-lines table
 *                  with per-line requested / issued / available / value and a
 *                  segmented All-lines / Pending-only filter, and visual action
 *                  buttons (Issue Stock / Post Inventory — the latter disabled
 *                  until the stock is issued). Data is fetched from
 *                  VAS_102_OverviewInternalUse/GetInternalUseOverview. All
 *                  on-screen strings are resolved through
 *                  VIS.Msg.getMsg("VAS_102_...").
 * Chronological development:
 *   VAI163   2026-07-07  Created
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {

    VAS.VAS_102_OverviewInternalUse = function () {
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
        var pendingOnly = false;   // Issue-lines filter state.

        this.init = function () {
            $root = $('<div class="MPC-vasiu-root"></div>');
            $body = $('<div class="MPC-vasiu-body"></div>');
            $emptyState = $('<div class="MPC-vasiu-empty" style="display:none;"></div>');
            $emptyState.text(VIS.Msg.getMsg("VAS_102_NoData"));
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
                url: VIS.Application.contextUrl + "VAS_102_OverviewInternalUse/GetInternalUseOverview",
                type: "GET",
                dataType: "json",
                data: { M_Inventory_ID: recordID },
                success: function (raw) {
                    var parsed = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                    data = parsed;
                    pendingOnly = false;
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

            if (!data || !data.M_Inventory_ID) {
                $body.hide();
                $emptyState.show();
                return;
            }

            $emptyState.hide();
            $body.show();

            renderHeader();
            renderSnapshot();
            renderStatusCards();
            renderTimeline();
            renderLines();
            renderActions();
        }

        // ----------------------------------------------------------------- //
        //  Small builders                                                    //
        // ----------------------------------------------------------------- //

        function section(title, opts, $parent) {
            opts = opts || {};
            var $sec = $('<section class="MPC-vasiu-sec"></section>');
            var $head = $('<div class="MPC-vasiu-secHead"></div>');
            $head.append($('<h2 class="MPC-vasiu-secTitle"></h2>').text(title));
            if (opts.summary) {
                $head.append($('<span class="MPC-vasiu-secSummary"></span>').text(opts.summary));
            }
            if (opts.$right) $head.append(opts.$right);
            $sec.append($head);
            ($parent || $body).append($sec);
            return $sec;
        }

        function na(value) {
            return (value === null || value === undefined || String(value).trim() === "")
                ? VIS.Msg.getMsg("VAS_102_NA")
                : value;
        }

        function currencyToken() {
            return (data && (data.CurSymbol || data.ISO_Code)) || "₹";
        }

        // ---------- Status map (DocStatus code -> label + tone) ---------- //

        var STATUS_MAP = {
            "DR": { key: "VAS_102_Drafted",             tone: "neutral" },
            "IP": { key: "VAS_102_InProgress",          tone: "info" },
            "AP": { key: "VAS_102_Approved",            tone: "info" },
            "CO": { key: "VAS_102_Completed",           tone: "info" },
            "CL": { key: "VAS_102_Closed",              tone: "success" },
            "VO": { key: "VAS_102_Voided",              tone: "risk" },
            "RE": { key: "VAS_102_Reversed",            tone: "risk" },
            "WC": { key: "VAS_102_WaitingConfirmation", tone: "warning" },
            "WP": { key: "VAS_102_WaitingPayment",      tone: "warning" },
            "IN": { key: "VAS_102_Invalid",             tone: "risk" },
            "NA": { key: "VAS_102_NotApproved",         tone: "risk" }
        };

        function statusMeta(code) {
            var m = STATUS_MAP[code];
            if (m) return { label: VIS.Msg.getMsg(m.key), tone: m.tone };
            return { label: na(code), tone: "neutral" };
        }

        // ---------- Origin map (code -> label) ---------- //

        var ORIGIN_MAP = {
            "PRODUCTION":  "VAS_102_ProductionOrder",
            "REQUISITION": "VAS_102_Requisition",
            "MANUAL":      "VAS_102_ManualIssue"
        };

        function originLabel() {
            var k = ORIGIN_MAP[data.OriginCode] || "VAS_102_ManualIssue";
            return VIS.Msg.getMsg(k);
        }

        // Line status derived from requested vs issued.
        // Full: issued >= requested; Short: issued <= 0; Partial: otherwise.
        function lineStatus(requested, issued) {
            if (issued >= requested) return "full";
            if (issued <= 0) return "short";
            return "partial";
        }

        // ---------- Header (title strip + details card) ---------- //

        function renderHeader() {
            var st = statusMeta(data.StatusCode);

            var $strip = $('<section class="MPC-vasiu-hdr"></section>');
            var $top = $('<div class="MPC-vasiu-hdrTop"></div>');

            var $tl = $('<div class="MPC-vasiu-hdrTitleWrap"></div>');
            $tl.append($('<div class="MPC-vasiu-hdrTitle"></div>').text(
                VIS.Msg.getMsg("VAS_102_InternalUse") +
                (data.DocumentNo ? " — " + data.DocumentNo : "")));

            var subBits = [];
            var moved = formatDate(data.MovementDate);
            if (moved) subBits.push(VIS.Msg.getMsg("VAS_102_MovementDate") + " " + moved);
            if (data.IssuedBy) subBits.push(VIS.Msg.getMsg("VAS_102_IssuedBy") + " " + data.IssuedBy);
            if (subBits.length) {
                $tl.append($('<div class="MPC-vasiu-hdrSub"></div>').text(subBits.join(" · ")));
            }
            $top.append($tl);

            var $pills = $('<div class="MPC-vasiu-hdrPills"></div>');
            $pills.append(headerPill(originLabel(), "info", "layers", false));
            if (data.Posted) {
                $pills.append(headerPill(VIS.Msg.getMsg("VAS_102_Posted"), "success", "check", false));
            }
            $pills.append(headerPill(st.label, st.tone, null, true));
            $top.append($pills);

            $strip.append($top);
            $body.append($strip);

            // --- Details card: warehouse identity (left) + document fields (right) ---
            var $card = $('<section class="MPC-vasiu-hdrCard"></section>');

            var $left = $('<div class="MPC-vasiu-hdrColL"></div>');
            $left.append($('<div class="MPC-vasiu-fLabel"></div>').text(VIS.Msg.getMsg("VAS_102_IssuedFrom")));
            $left.append($('<div class="MPC-vasiu-vendName"></div>').text(na(data.WarehouseName)));

            var $contact = $('<div class="MPC-vasiu-vendContact"></div>');
            appendContactBit($contact, "user", data.IssuedBy);
            appendContactBit($contact, "calendar", formatDate(data.MovementDate));
            if ($contact.children().length) $left.append($contact);
            $card.append($left);

            var $right = $('<div class="MPC-vasiu-hdrColR"></div>');
            $right.append(headerField(VIS.Msg.getMsg("VAS_102_InternalUseNo"), na(data.DocumentNo), false));
            $right.append(headerField(VIS.Msg.getMsg("VAS_102_Origin"), originLabel(), false));
            $right.append(headerField(VIS.Msg.getMsg("VAS_102_Reference"), na(data.Description), false));
            $right.append(headerField(VIS.Msg.getMsg("VAS_102_Posted"),
                data.Posted ? VIS.Msg.getMsg("VAS_102_Posted")
                            : VIS.Msg.getMsg("VAS_102_NotPosted"), false));
            $card.append($right);

            $body.append($card);
        }

        function headerPill(label, tone, icon, withDot) {
            var $p = $('<span class="MPC-vasiu-hdrPill"></span>')
                .addClass("tone-" + (tone || "neutral"));
            if (icon) $p.append(svgIcon(icon));
            if (withDot) $p.append($('<span class="MPC-vasiu-hdrDot"></span>'));
            $p.append($('<span></span>').text(label));
            return $p;
        }

        function headerField(label, value, link) {
            var $f = $('<div class="MPC-vasiu-hdrField"></div>');
            $f.append($('<div class="MPC-vasiu-fLabel"></div>').text(label));
            var $v = $('<div class="MPC-vasiu-fVal"></div>').text(value);
            if (link) $v.addClass("is-link");
            $f.append($v);
            return $f;
        }

        function appendContactBit($container, icon, value) {
            if (!value) return;
            var $bit = $('<span class="MPC-vasiu-contactBit"></span>');
            $bit.append(svgIcon(icon));
            $bit.append($('<span></span>').text(value));
            $container.append($bit);
        }

        // ---------- Snapshot (KPI metric grid) ---------- //

        function renderSnapshot() {
            var $snap = $('<section class="MPC-vasiu-snap"></section>');
            var cur = currencyToken();

            // Total issued value.
            $snap.append(metricCard("total", "coins", VIS.Msg.getMsg("VAS_102_TotalValue"),
                formatAmount(+data.TotalValue || 0, cur, data.StdPrecision),
                VIS.Msg.getMsg("VAS_102_IssuedValue")));

            // Quantity issued (across N lines).
            $snap.append(metricCard("issued", "box", VIS.Msg.getMsg("VAS_102_QuantityIssued"),
                formatNumber(+data.IssuedQty || 0, 0),
                VIS.Msg.getMsg("VAS_102_Across") + " " + (data.LineCount || 0) + " " + VIS.Msg.getMsg("VAS_102_LinesWord")));

            // Total lines.
            $snap.append(metricCard("lines", "layers", VIS.Msg.getMsg("VAS_102_Lines"),
                (data.LineCount || 0) + "", VIS.Msg.getMsg("VAS_102_OnThisIssue")));

            // Not fully issued.
            $snap.append(metricCard("pending", "alert", VIS.Msg.getMsg("VAS_102_NotFullyIssued"),
                (data.NotFullCount || 0) + "", VIS.Msg.getMsg("VAS_102_ShortOfRequest")));

            $body.append($snap);
        }

        function metricCard(tone, icon, label, value, sub) {
            var $c = $('<div class="MPC-vasiu-metric"></div>').addClass("tone-" + tone);

            var $head = $('<div class="MPC-vasiu-mHead"></div>');
            $head.append(svgIcon(icon));
            $head.append($('<span class="MPC-vasiu-mLabel"></span>').text(label));
            $c.append($head);

            $c.append($('<div class="MPC-vasiu-mVal"></div>').text(value));
            if (sub) $c.append($('<div class="MPC-vasiu-mSub"></div>').text(sub));
            return $c;
        }

        // ---------- Status summary cards (Full / Partial / Short / Lines) ---------- //

        function renderStatusCards() {
            var counts = countLineStatuses();
            var $row = $('<section class="MPC-vasiu-status"></section>');
            $row.append(statusCard(VIS.Msg.getMsg("VAS_102_Full"),    counts.full,    "full"));
            $row.append(statusCard(VIS.Msg.getMsg("VAS_102_Partial"), counts.partial, "partial"));
            $row.append(statusCard(VIS.Msg.getMsg("VAS_102_Short"),   counts.short,   "short"));
            $row.append(statusCard(VIS.Msg.getMsg("VAS_102_Lines"),   data.LineCount || 0, "neutral"));
            $body.append($row);
        }

        function countLineStatuses() {
            var out = { full: 0, partial: 0, short: 0 };
            var lines = (data && data.Lines) || [];
            for (var i = 0; i < lines.length; i++) {
                var s = lineStatus(+lines[i].RequestedQty || 0, +lines[i].IssuedQty || 0);
                out[s]++;
            }
            return out;
        }

        function statusCard(label, value, tone) {
            var $c = $('<div class="MPC-vasiu-statCard"></div>').addClass("tone-" + tone);
            $c.append($('<div class="MPC-vasiu-statVal"></div>').text(value + ""));
            $c.append($('<div class="MPC-vasiu-statLbl"></div>').text(label));
            return $c;
        }

        // ---------- Issue timeline (3-node stepper) ---------- //

        function renderTimeline() {
            var issued = data.Processed || data.StatusCode === "CO" || data.StatusCode === "CL";
            var stages = [
                { key: "VAS_102_Created", done: true,        date: data.MovementDate },
                { key: "VAS_102_Issued",  done: issued,      date: data.MovementDate },
                { key: "VAS_102_Posting", done: data.Posted, date: data.MovementDate }
            ];

            var activeIdx = -1;
            for (var k = 0; k < stages.length; k++) { if (stages[k].done) activeIdx = k; }

            var $sec = section(VIS.Msg.getMsg("VAS_102_IssueTimeline"), null);

            var $tl = $('<div class="MPC-vasiu-stepper"></div>');
            for (var i = 0; i < stages.length; i++) {
                var s = stages[i];
                var stateCls, metaText;
                if (s.done) {
                    stateCls = "is-done";
                    metaText = (i === 2)
                        ? VIS.Msg.getMsg("VAS_102_Posted")
                        : (formatDate(s.date) || VIS.Msg.getMsg("VAS_102_Done"));
                } else if (i === activeIdx + 1) {
                    stateCls = "is-active";
                    metaText = VIS.Msg.getMsg("VAS_102_Pending");
                } else {
                    stateCls = "is-pending";
                    metaText = VIS.Msg.getMsg("VAS_102_Pending");
                }
                $tl.append(stepEntry(i + 1, VIS.Msg.getMsg(s.key), metaText, s.done, stateCls));
            }
            $sec.append($tl);
        }

        function stepEntry(num, title, meta, done, stateCls) {
            var $entry = $('<div class="MPC-vasiu-step"></div>').addClass(stateCls || "");

            var $rail = $('<div class="MPC-vasiu-stepRail"></div>');
            $rail.append($('<span class="MPC-vasiu-stepLine MPC-vasiu-stepLine-l"></span>'));
            var $dot = $('<span class="MPC-vasiu-stepDot"></span>');
            if (done) { $dot.append(svgIcon("check")); } else { $dot.text(num); }
            $rail.append($dot);
            $rail.append($('<span class="MPC-vasiu-stepLine MPC-vasiu-stepLine-r"></span>'));
            $entry.append($rail);

            var $lbl = $('<div class="MPC-vasiu-stepLabel"></div>');
            $lbl.append($('<div class="MPC-vasiu-stepTitle"></div>').text(title));
            if (meta) $lbl.append($('<div class="MPC-vasiu-stepMeta"></div>').text(meta));
            $entry.append($lbl);

            return $entry;
        }

        // ---------- Issue lines (table + filter toggle) ---------- //

        var $linesTable = null;   // rebuilt in place when the filter toggles.

        function renderLines() {
            var lines = (data && data.Lines) || [];
            if (!lines.length) return;

            // Segmented filter: All lines | Pending only.
            var $seg = $('<span class="MPC-vasiu-seg"></span>');
            var $btnAll = $('<button type="button"></button>')
                .text(VIS.Msg.getMsg("VAS_102_AllLines")).toggleClass("on", !pendingOnly);
            var $btnPend = $('<button type="button"></button>')
                .text(VIS.Msg.getMsg("VAS_102_PendingOnly")).toggleClass("on", pendingOnly);
            $btnAll.on("click", function () { if (pendingOnly) { pendingOnly = false; refreshTable($btnAll, $btnPend); } });
            $btnPend.on("click", function () { if (!pendingOnly) { pendingOnly = true; refreshTable($btnAll, $btnPend); } });
            $seg.append($btnAll).append($btnPend);

            var $sec = section(VIS.Msg.getMsg("VAS_102_IssueLines"), {
                summary: (data.LineCount || 0) + " " + VIS.Msg.getMsg("VAS_102_Items"),
                $right: $seg
            });

            $linesTable = buildLinesTable();
            $sec.append($linesTable);
        }

        function refreshTable($btnAll, $btnPend) {
            $btnAll.toggleClass("on", !pendingOnly);
            $btnPend.toggleClass("on", pendingOnly);
            if (!$linesTable) return;
            var $fresh = buildLinesTable();
            $linesTable.replaceWith($fresh);
            $linesTable = $fresh;
        }

        function buildLinesTable() {
            var lines = (data && data.Lines) || [];
            var cur = currencyToken();

            var $tbl = $('<div class="MPC-vasiu-table"></div>');

            var $head = $('<div class="MPC-vasiu-tRow MPC-vasiu-tHead"></div>');
            $head.append($('<span></span>').text(VIS.Msg.getMsg("VAS_102_Item")));
            $head.append($('<span></span>').text(VIS.Msg.getMsg("VAS_102_Locator")));
            $head.append($('<span></span>').text(VIS.Msg.getMsg("VAS_102_UOM")));
            $head.append($('<span class="ta-r"></span>').text(VIS.Msg.getMsg("VAS_102_Requested")));
            $head.append($('<span class="ta-r"></span>').text(VIS.Msg.getMsg("VAS_102_Issued")));
            $head.append($('<span class="ta-r"></span>').text(VIS.Msg.getMsg("VAS_102_Available")));
            $head.append($('<span class="ta-r"></span>').text(VIS.Msg.getMsg("VAS_102_Value")));
            $head.append($('<span class="ta-c"></span>').text(VIS.Msg.getMsg("VAS_102_Status")));
            $tbl.append($head);

            var shown = 0;
            for (var i = 0; i < lines.length; i++) {
                var ln = lines[i];
                var st = lineStatus(+ln.RequestedQty || 0, +ln.IssuedQty || 0);
                // Pending only hides fully-issued lines.
                if (pendingOnly && st === "full") continue;
                $tbl.append(buildLineRow(ln, st, cur));
                shown++;
            }

            if (shown === 0) {
                $tbl.append($('<div class="MPC-vasiu-tEmpty"></div>')
                    .text(VIS.Msg.getMsg("VAS_102_NoPendingLines")));
            }

            // Totals footer
            var $foot = $('<div class="MPC-vasiu-tFoot"></div>');
            var $bit = $('<span class="MPC-vasiu-tf is-grand"></span>');
            $bit.append(document.createTextNode(VIS.Msg.getMsg("VAS_102_TotalIssuedValue")));
            $bit.append($('<b></b>').text(formatAmount(+data.TotalValue || 0, cur, data.StdPrecision)));
            $foot.append($bit);
            $tbl.append($foot);

            return $tbl;
        }

        function buildLineRow(ln, st, cur) {
            var $tr = $('<div class="MPC-vasiu-tRow MPC-vasiu-tBody"></div>');

            // Item (name + SKU)
            var $item = $('<span class="MPC-vasiu-itItem"></span>');
            $item.append($('<div class="MPC-vasiu-itName"></div>').text(na(ln.ProductName)));
            if (ln.ProductCode) {
                $item.append($('<div class="MPC-vasiu-itSku"></div>')
                    .text(VIS.Msg.getMsg("VAS_102_SKU") + " " + ln.ProductCode));
            } else if (ln.Description) {
                $item.append($('<div class="MPC-vasiu-itSku"></div>').text(ln.Description));
            }
            $tr.append($item);

            var prec = +ln.UOMPrecision || 0;

            // Locator
            $tr.append($('<span></span>').text(na(ln.LocatorName)));

            // UOM
            $tr.append($('<span></span>').text(na(ln.UOMName)));

            // Requested
            $tr.append($('<span class="ta-r"></span>').text(formatNumber(+ln.RequestedQty || 0, prec)));

            // Issued
            $tr.append($('<span class="ta-r"></span>').text(formatNumber(+ln.IssuedQty || 0, prec)));

            // Available
            $tr.append($('<span class="ta-r"></span>').text(formatNumber(+ln.AvailableQty || 0, prec)));

            // Value
            $tr.append($('<span class="ta-r"></span>').text(
                formatAmount(+ln.LineValue || 0, cur, data.StdPrecision)));

            // Status tag
            var tagKey = st === "full" ? "VAS_102_Full"
                       : (st === "partial" ? "VAS_102_Partial" : "VAS_102_Short");
            var $q = $('<span class="ta-c"></span>');
            $q.append($('<span class="MPC-vasiu-tag"></span>').addClass("s-" + st)
                .text(VIS.Msg.getMsg(tagKey)));
            $tr.append($q);

            return $tr;
        }

        // ---------- Actions (visual buttons) ---------- //

        // Presentational only. Issue Stock is disabled once the issue is already
        // issued/posted; Post Inventory is disabled until it is issued.
        function renderActions() {
            var issued = data.Processed || data.StatusCode === "CO" || data.StatusCode === "CL";
            var posted = data.Posted;

            var $bar = $('<section class="MPC-vasiu-actions"></section>');
            $bar.append(actionButton("box", VIS.Msg.getMsg("VAS_102_IssueStock"), "pri", issued));
            $bar.append(actionButton("layers", VIS.Msg.getMsg("VAS_102_PostInventory"), "sec", !issued || posted));
            $body.append($bar);
        }

        function actionButton(icon, label, kind, disabled) {
            var $b = $('<span class="MPC-vasiu-btn"></span>').addClass("btn-" + (kind || "sec"));
            if (disabled) $b.addClass("is-disabled");
            $b.append(svgIcon(icon));
            $b.append($('<span></span>').text(label));
            return $b;
        }

        // ----------------------------------------------------------------- //
        //  Icon helpers                                                      //
        // ----------------------------------------------------------------- //

        var SVG_ICONS = {
            user:     '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"/><circle cx="12" cy="7" r="4"/></svg>',
            calendar: '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="4" width="18" height="18" rx="2"/><path d="M16 2v4"/><path d="M8 2v4"/><path d="M3 10h18"/></svg>',
            box:      '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M21 8 12 3 3 8v8l9 5 9-5Z"/><path d="m3 8 9 5 9-5"/><path d="M12 13v8"/></svg>',
            coins:    '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><circle cx="8" cy="8" r="5"/><path d="M14.5 4.2a5 5 0 0 1 0 15.6"/><path d="M7 18.7a5 5 0 0 0 6 0"/></svg>',
            alert:    '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M10.29 3.86 1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0Z"/><path d="M12 9v4"/><path d="M12 17h.01"/></svg>',
            layers:   '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="m12 2 9 5-9 5-9-5 9-5Z"/><path d="m3 12 9 5 9-5"/><path d="m3 17 9 5 9-5"/></svg>',
            check:    '<svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"><path d="M20 6 9 17l-5-5"/></svg>'
        };

        function svgIcon(name) {
            var $wrap = $('<span class="MPC-vasiu-ic"></span>');
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

    VAS.VAS_102_OverviewInternalUse.prototype.startPanel = function (windowNo, curTab) {
        this.windowNo = windowNo;
        this.curTab = curTab;
        if (curTab && typeof curTab.getAD_Table_ID === "function") {
            this.table_ID = curTab.getAD_Table_ID();
        }
        this.init();
    };

    /* Update tab panel based on selected record */
    VAS.VAS_102_OverviewInternalUse.prototype.refreshPanelData = function (recordID, selectedRow) {
        if (selectedRow == undefined || recordID <= 0) {
            this.clear();
            return;
        }
        this.record_ID = recordID;
        this.selectedRow = selectedRow;
        this.fetchData(recordID);
    };

    /* Set width as per window width */
    VAS.VAS_102_OverviewInternalUse.prototype.sizeChanged = function (width) {
        this.panelWidth = width;
    };

    /* Release variables from memory */
    VAS.VAS_102_OverviewInternalUse.prototype.dispose = function () {
        this.record_ID = 0;
        this.table_ID = 0;
        this.windowNo = 0;
        this.curTab = null;
        this.selectedRow = null;
        this.panelWidth = null;
    };

})(VAS, jQuery);
