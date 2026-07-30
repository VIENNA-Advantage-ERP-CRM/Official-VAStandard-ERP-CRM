/************************************************************
 * Module Name    : VAS
 * Purpose        : Inventory Count Overview tab panel. Renders a review-
 *                  oriented overview of the selected physical inventory count
 *                  (M_Inventory, IsInternalUse <> 'N'): header identity +
 *                  warehouse / count details card, a four-card KPI snapshot
 *                  (counted value, net variance qty, variance lines, total
 *                  lines), matched / short / excess status cards, a compact
 *                  count timeline (Count started -> Counted -> Posting), a
 *                  count-lines table with per-line signed variance and a
 *                  segmented All-lines / Variances-only filter, and visual
 *                  action buttons (Complete / Post adjustments — the latter
 *                  disabled until the count is completed). Data is fetched from
 *                  VAS_101_OverviewInventoryCount/GetInventoryCountOverview.
 *                  All on-screen strings are resolved through
 *                  VIS.Msg.getMsg("VAS_101_...").
 * Chronological development:
 *   VAI163   2026-07-06  Created
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {

    VAS.VAS_101_OverviewInventoryCount = function () {
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
        var variancesOnly = false;   // Count-lines filter state.

        this.init = function () {
            $root = $('<div class="MPC-vasic-root"></div>');
            $body = $('<div class="MPC-vasic-body"></div>');
            $emptyState = $('<div class="MPC-vasic-empty" style="display:none;"></div>');
            $emptyState.text(VIS.Msg.getMsg("VAS_101_NoData"));
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
                url: VIS.Application.contextUrl + "VAS_101_OverviewInventoryCount/GetInventoryCountOverview",
                type: "GET",
                dataType: "json",
                data: { M_Inventory_ID: recordID },
                success: function (raw) {
                    var parsed = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                    data = parsed;
                    variancesOnly = false;
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
            var $sec = $('<section class="MPC-vasic-sec"></section>');
            var $head = $('<div class="MPC-vasic-secHead"></div>');
            $head.append($('<h2 class="MPC-vasic-secTitle"></h2>').text(title));
            if (opts.summary) {
                $head.append($('<span class="MPC-vasic-secSummary"></span>').text(opts.summary));
            }
            if (opts.$right) $head.append(opts.$right);
            $sec.append($head);
            ($parent || $body).append($sec);
            return $sec;
        }

        function na(value) {
            return (value === null || value === undefined || String(value).trim() === "")
                ? VIS.Msg.getMsg("VAS_101_NA")
                : value;
        }

        function currencyToken() {
            return (data && (data.CurSymbol || data.ISO_Code)) || "₹";
        }

        // ---------- Status map (code -> label + tone) ---------- //

        var STATUS_MAP = {
            "DR": { key: "VAS_101_Drafted",             tone: "neutral" },
            "IP": { key: "VAS_101_InProgress",          tone: "info" },
            "AP": { key: "VAS_101_Approved",            tone: "info" },
            "CO": { key: "VAS_101_Completed",           tone: "info" },
            "CL": { key: "VAS_101_Closed",              tone: "success" },
            "VO": { key: "VAS_101_Voided",              tone: "risk" },
            "RE": { key: "VAS_101_Reversed",            tone: "risk" },
            "WC": { key: "VAS_101_WaitingConfirmation", tone: "warning" },
            "WP": { key: "VAS_101_WaitingPayment",      tone: "warning" },
            "IN": { key: "VAS_101_Invalid",             tone: "risk" },
            "NA": { key: "VAS_101_NotApproved",         tone: "risk" }
        };

        function statusMeta(code) {
            var m = STATUS_MAP[code];
            if (m) return { label: VIS.Msg.getMsg(m.key), tone: m.tone };
            return { label: na(code), tone: "neutral" };
        }

        // ---------- Header (title strip + details card) ---------- //

        function renderHeader() {
            var st = statusMeta(data.StatusCode);

            var $strip = $('<section class="MPC-vasic-hdr"></section>');
            var $top = $('<div class="MPC-vasic-hdrTop"></div>');

            var $tl = $('<div class="MPC-vasic-hdrTitleWrap"></div>');
            $tl.append($('<div class="MPC-vasic-hdrTitle"></div>').text(
                VIS.Msg.getMsg("VAS_101_InventoryCount") +
                (data.DocumentNo ? " — " + data.DocumentNo : "")));

            var subBits = [];
            var counted = formatDate(data.CountDate);
            if (counted) subBits.push(VIS.Msg.getMsg("VAS_101_CountDate") + " " + counted);
            if (data.CountedBy) subBits.push(VIS.Msg.getMsg("VAS_101_CountedBy") + " " + data.CountedBy);
            if (subBits.length) {
                $tl.append($('<div class="MPC-vasic-hdrSub"></div>').text(subBits.join(" · ")));
            }
            $top.append($tl);

            var $pills = $('<div class="MPC-vasic-hdrPills"></div>');
            if (data.Posted) {
                $pills.append(headerPill(VIS.Msg.getMsg("VAS_101_Posted"), "success", "check", false));
            }
            $pills.append(headerPill(st.label, st.tone, null, true));
            $top.append($pills);

            $strip.append($top);
            $body.append($strip);

            // --- Details card: warehouse identity (left) + document fields (right) ---
            var $card = $('<section class="MPC-vasic-hdrCard"></section>');

            var $left = $('<div class="MPC-vasic-hdrColL"></div>');
            $left.append($('<div class="MPC-vasic-fLabel"></div>').text(VIS.Msg.getMsg("VAS_101_Warehouse")));
            $left.append($('<div class="MPC-vasic-vendName"></div>').text(na(data.WarehouseName)));

            var $contact = $('<div class="MPC-vasic-vendContact"></div>');
            appendContactBit($contact, "user", data.CountedBy);
            appendContactBit($contact, "calendar", formatDate(data.CountDate));
            if ($contact.children().length) $left.append($contact);
            $card.append($left);

            var $right = $('<div class="MPC-vasic-hdrColR"></div>');
            $right.append(headerField(VIS.Msg.getMsg("VAS_101_CountNo"), na(data.DocumentNo), false));
            $right.append(headerField(VIS.Msg.getMsg("VAS_101_Reference"), na(data.Description), false));
            $right.append(headerField(VIS.Msg.getMsg("VAS_101_Posted"),
                data.Posted ? VIS.Msg.getMsg("VAS_101_Posted")
                            : VIS.Msg.getMsg("VAS_101_NotPosted"), false));
            $right.append(headerField(VIS.Msg.getMsg("VAS_101_Lines"), (data.LineCount || 0) + "", false));
            $card.append($right);

            $body.append($card);
        }

        function headerPill(label, tone, icon, withDot) {
            var $p = $('<span class="MPC-vasic-hdrPill"></span>')
                .addClass("tone-" + (tone || "neutral"));
            if (icon) $p.append(svgIcon(icon));
            if (withDot) $p.append($('<span class="MPC-vasic-hdrDot"></span>'));
            $p.append($('<span></span>').text(label));
            return $p;
        }

        function headerField(label, value, link) {
            var $f = $('<div class="MPC-vasic-hdrField"></div>');
            $f.append($('<div class="MPC-vasic-fLabel"></div>').text(label));
            var $v = $('<div class="MPC-vasic-fVal"></div>').text(value);
            if (link) $v.addClass("is-link");
            $f.append($v);
            return $f;
        }

        function appendContactBit($container, icon, value) {
            if (!value) return;
            var $bit = $('<span class="MPC-vasic-contactBit"></span>');
            $bit.append(svgIcon(icon));
            $bit.append($('<span></span>').text(value));
            $container.append($bit);
        }

        // ---------- Snapshot (KPI metric grid) ---------- //

        function renderSnapshot() {
            var $snap = $('<section class="MPC-vasic-snap"></section>');
            var cur = currencyToken();

            // Total counted value.
            $snap.append(metricCard("total", "coins", VIS.Msg.getMsg("VAS_101_TotalValue"),
                formatAmount(+data.TotalValue || 0, cur, data.StdPrecision), ""));

            // Net count variance (signed qty), tinted by direction.
            var net = +data.NetVarianceQty || 0;
            var netTone = net < 0 ? "short" : (net > 0 ? "excess" : "total");
            $snap.append(metricCard(netTone, "delta", VIS.Msg.getMsg("VAS_101_NetVariance"),
                signedNumber(net, 0), VIS.Msg.getMsg("VAS_101_NetQtyDifference")));

            // Variance lines.
            $snap.append(metricCard("variance", "alert", VIS.Msg.getMsg("VAS_101_VarianceLines"),
                (data.VarianceLineCount || 0) + "",
                VIS.Msg.getMsg("VAS_101_Of") + " " + (data.LineCount || 0) + " " + VIS.Msg.getMsg("VAS_101_LinesWord")));

            // Total lines.
            $snap.append(metricCard("lines", "box", VIS.Msg.getMsg("VAS_101_TotalLines"),
                (data.LineCount || 0) + "", ""));

            $body.append($snap);
        }

        function metricCard(tone, icon, label, value, sub) {
            var $c = $('<div class="MPC-vasic-metric"></div>').addClass("tone-" + tone);

            var $head = $('<div class="MPC-vasic-mHead"></div>');
            $head.append(svgIcon(icon));
            $head.append($('<span class="MPC-vasic-mLabel"></span>').text(label));
            $c.append($head);

            $c.append($('<div class="MPC-vasic-mVal"></div>').text(value));
            if (sub) $c.append($('<div class="MPC-vasic-mSub"></div>').text(sub));
            return $c;
        }

        // ---------- Status summary cards (Matched / Short / Excess / Lines) ---------- //

        function renderStatusCards() {
            var $row = $('<section class="MPC-vasic-status"></section>');
            $row.append(statusCard(VIS.Msg.getMsg("VAS_101_Matched"), data.MatchedCount || 0, "match"));
            $row.append(statusCard(VIS.Msg.getMsg("VAS_101_Short"),   data.ShortCount || 0,   "short"));
            $row.append(statusCard(VIS.Msg.getMsg("VAS_101_Excess"),  data.ExcessCount || 0,  "excess"));
            $row.append(statusCard(VIS.Msg.getMsg("VAS_101_Lines"),   data.LineCount || 0,    "neutral"));
            $body.append($row);
        }

        function statusCard(label, value, tone) {
            var $c = $('<div class="MPC-vasic-statCard"></div>').addClass("tone-" + tone);
            $c.append($('<div class="MPC-vasic-statVal"></div>').text(value + ""));
            $c.append($('<div class="MPC-vasic-statLbl"></div>').text(label));
            return $c;
        }

        // ---------- Count timeline (3-node stepper) ---------- //

        function renderTimeline() {
            var completed = data.Processed || data.StatusCode === "CO" || data.StatusCode === "CL";
            var stages = [
                { key: "VAS_101_CountStarted", done: true,          date: data.CountDate },
                { key: "VAS_101_Counted",      done: completed,     date: data.CountDate },
                { key: "VAS_101_Posting",      done: data.Posted,   date: data.CountDate }
            ];

            var activeIdx = -1;
            for (var k = 0; k < stages.length; k++) { if (stages[k].done) activeIdx = k; }

            var $sec = section(VIS.Msg.getMsg("VAS_101_CountTimeline"), null);

            var $tl = $('<div class="MPC-vasic-stepper"></div>');
            for (var i = 0; i < stages.length; i++) {
                var s = stages[i];
                var stateCls, metaText;
                if (s.done) {
                    stateCls = "is-done";
                    metaText = (i === 2)
                        ? VIS.Msg.getMsg("VAS_101_Posted")
                        : (formatDate(s.date) || VIS.Msg.getMsg("VAS_101_Done"));
                } else if (i === activeIdx + 1) {
                    stateCls = "is-active";
                    metaText = VIS.Msg.getMsg("VAS_101_Pending");
                } else {
                    stateCls = "is-pending";
                    metaText = VIS.Msg.getMsg("VAS_101_Pending");
                }
                $tl.append(stepEntry(i + 1, VIS.Msg.getMsg(s.key), metaText, s.done, stateCls));
            }
            $sec.append($tl);
        }

        function stepEntry(num, title, meta, done, stateCls) {
            var $entry = $('<div class="MPC-vasic-step"></div>').addClass(stateCls || "");

            var $rail = $('<div class="MPC-vasic-stepRail"></div>');
            $rail.append($('<span class="MPC-vasic-stepLine MPC-vasic-stepLine-l"></span>'));
            var $dot = $('<span class="MPC-vasic-stepDot"></span>');
            if (done) { $dot.append(svgIcon("check")); } else { $dot.text(num); }
            $rail.append($dot);
            $rail.append($('<span class="MPC-vasic-stepLine MPC-vasic-stepLine-r"></span>'));
            $entry.append($rail);

            var $lbl = $('<div class="MPC-vasic-stepLabel"></div>');
            $lbl.append($('<div class="MPC-vasic-stepTitle"></div>').text(title));
            if (meta) $lbl.append($('<div class="MPC-vasic-stepMeta"></div>').text(meta));
            $entry.append($lbl);

            return $entry;
        }

        // ---------- Count lines (table + filter toggle) ---------- //

        var $linesTable = null;   // rebuilt in place when the filter toggles.

        function renderLines() {
            var lines = (data && data.Lines) || [];
            if (!lines.length) return;

            // Segmented filter: All lines | Variances only.
            var $seg = $('<span class="MPC-vasic-seg"></span>');
            var $btnAll = $('<button type="button"></button>')
                .text(VIS.Msg.getMsg("VAS_101_AllLines")).toggleClass("on", !variancesOnly);
            var $btnVar = $('<button type="button"></button>')
                .text(VIS.Msg.getMsg("VAS_101_VariancesOnly")).toggleClass("on", variancesOnly);
            $btnAll.on("click", function () { if (variancesOnly) { variancesOnly = false; refreshTable($btnAll, $btnVar); } });
            $btnVar.on("click", function () { if (!variancesOnly) { variancesOnly = true; refreshTable($btnAll, $btnVar); } });
            $seg.append($btnAll).append($btnVar);

            var $sec = section(VIS.Msg.getMsg("VAS_101_CountLines"), {
                summary: (data.LineCount || 0) + " " + VIS.Msg.getMsg("VAS_101_Items"),
                $right: $seg
            });

            $linesTable = buildLinesTable();
            $sec.append($linesTable);
        }

        function refreshTable($btnAll, $btnVar) {
            $btnAll.toggleClass("on", !variancesOnly);
            $btnVar.toggleClass("on", variancesOnly);
            if (!$linesTable) return;
            var $fresh = buildLinesTable();
            $linesTable.replaceWith($fresh);
            $linesTable = $fresh;
        }

        function buildLinesTable() {
            var lines = (data && data.Lines) || [];
            var cur = currencyToken();

            var $tbl = $('<div class="MPC-vasic-table"></div>');

            var $head = $('<div class="MPC-vasic-tRow MPC-vasic-tHead"></div>');
            $head.append($('<span></span>').text(VIS.Msg.getMsg("VAS_101_Item")));
            $head.append($('<span></span>').text(VIS.Msg.getMsg("VAS_101_UOM")));
            $head.append($('<span class="ta-r"></span>').text(VIS.Msg.getMsg("VAS_101_SystemQty")));
            $head.append($('<span class="ta-r"></span>').text(VIS.Msg.getMsg("VAS_101_CountedQty")));
            $head.append($('<span class="ta-r"></span>').text(VIS.Msg.getMsg("VAS_101_Variance")));
            $head.append($('<span class="ta-r"></span>').text(VIS.Msg.getMsg("VAS_101_Value")));
            $head.append($('<span class="ta-c"></span>').text(VIS.Msg.getMsg("VAS_101_Status")));
            $tbl.append($head);

            var shown = 0;
            for (var i = 0; i < lines.length; i++) {
                var ln = lines[i];
                var variance = +ln.VarianceQty || 0;
                if (variancesOnly && variance === 0) continue;
                $tbl.append(buildLineRow(ln, variance, cur));
                shown++;
            }

            if (shown === 0) {
                $tbl.append($('<div class="MPC-vasic-tEmpty"></div>')
                    .text(VIS.Msg.getMsg("VAS_101_NoVarianceLines")));
            }

            // Totals footer
            var $foot = $('<div class="MPC-vasic-tFoot"></div>');
            var $bit = $('<span class="MPC-vasic-tf is-grand"></span>');
            $bit.append(document.createTextNode(VIS.Msg.getMsg("VAS_101_TotalCountedValue")));
            $bit.append($('<b></b>').text(formatAmount(+data.TotalValue || 0, cur, data.StdPrecision)));
            $foot.append($bit);
            $tbl.append($foot);

            return $tbl;
        }

        function buildLineRow(ln, variance, cur) {
            var $tr = $('<div class="MPC-vasic-tRow MPC-vasic-tBody"></div>');

            // Item (name + SKU / locator)
            var $item = $('<span class="MPC-vasic-itItem"></span>');
            $item.append($('<div class="MPC-vasic-itName"></div>').text(na(ln.ProductName)));
            var metaBits = [];
            if (ln.ProductCode) metaBits.push(VIS.Msg.getMsg("VAS_101_SKU") + " " + ln.ProductCode);
            if (ln.LocatorName) metaBits.push(VIS.Msg.getMsg("VAS_101_Locator") + " " + ln.LocatorName);
            if (metaBits.length) {
                $item.append($('<div class="MPC-vasic-itSku"></div>').text(metaBits.join(" · ")));
            } else if (ln.Description) {
                $item.append($('<div class="MPC-vasic-itSku"></div>').text(ln.Description));
            }
            $tr.append($item);

            var prec = +ln.UOMPrecision || 0;

            // UOM
            $tr.append($('<span></span>').text(na(ln.UOMName)));

            // System (book) qty
            $tr.append($('<span class="ta-r"></span>').text(formatNumber(+ln.SystemQty || 0, prec)));

            // Counted qty
            $tr.append($('<span class="ta-r"></span>').text(formatNumber(+ln.CountedQty || 0, prec)));

            // Variance (signed, colored)
            var vTone = variance < 0 ? "short" : (variance > 0 ? "excess" : "match");
            $tr.append($('<span class="ta-r MPC-vasic-var"></span>').addClass(vTone)
                .text(signedNumber(variance, prec)));

            // Value
            $tr.append($('<span class="ta-r"></span>').text(
                formatAmount(+ln.LineValue || 0, cur, data.StdPrecision)));

            // Status tag
            var tagKey = variance < 0 ? "VAS_101_Short" : (variance > 0 ? "VAS_101_Excess" : "VAS_101_Match");
            var $q = $('<span class="ta-c"></span>');
            $q.append($('<span class="MPC-vasic-tag"></span>').addClass("s-" + vTone)
                .text(VIS.Msg.getMsg(tagKey)));
            $tr.append($q);

            return $tr;
        }

        // ---------- Actions (visual buttons) ---------- //

        // Presentational only. Complete is disabled once the count is already
        // completed/posted; Post adjustments is disabled until it is completed.
        function renderActions() {
            var completed = data.Processed || data.StatusCode === "CO" || data.StatusCode === "CL";
            var posted = data.Posted;

            var $bar = $('<section class="MPC-vasic-actions"></section>');
            $bar.append(actionButton("check", VIS.Msg.getMsg("VAS_101_Complete"), "pri", completed));
            $bar.append(actionButton("layers", VIS.Msg.getMsg("VAS_101_PostAdjustments"), "sec", !completed || posted));
            $body.append($bar);
        }

        function actionButton(icon, label, kind, disabled) {
            var $b = $('<span class="MPC-vasic-btn"></span>').addClass("btn-" + (kind || "sec"));
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
            delta:    '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M12 4 3 20h18Z"/></svg>',
            alert:    '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M10.29 3.86 1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0Z"/><path d="M12 9v4"/><path d="M12 17h.01"/></svg>',
            layers:   '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="m12 2 9 5-9 5-9-5 9-5Z"/><path d="m3 12 9 5 9-5"/><path d="m3 17 9 5 9-5"/></svg>',
            check:    '<svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"><path d="M20 6 9 17l-5-5"/></svg>'
        };

        function svgIcon(name) {
            var $wrap = $('<span class="MPC-vasic-ic"></span>');
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

    VAS.VAS_101_OverviewInventoryCount.prototype.startPanel = function (windowNo, curTab) {
        this.windowNo = windowNo;
        this.curTab = curTab;
        if (curTab && typeof curTab.getAD_Table_ID === "function") {
            this.table_ID = curTab.getAD_Table_ID();
        }
        this.init();
    };

    /* Update tab panel based on selected record */
    VAS.VAS_101_OverviewInventoryCount.prototype.refreshPanelData = function (recordID, selectedRow) {
        if (selectedRow == undefined || recordID <= 0) {
            this.clear();
            return;
        }
        this.record_ID = recordID;
        this.selectedRow = selectedRow;
        this.fetchData(recordID);
    };

    /* Set width as per window width */
    VAS.VAS_101_OverviewInventoryCount.prototype.sizeChanged = function (width) {
        this.panelWidth = width;
    };

    /* Release variables from memory */
    VAS.VAS_101_OverviewInventoryCount.prototype.dispose = function () {
        this.record_ID = 0;
        this.table_ID = 0;
        this.windowNo = 0;
        this.curTab = null;
        this.selectedRow = null;
        this.panelWidth = null;
    };

})(VAS, jQuery);
