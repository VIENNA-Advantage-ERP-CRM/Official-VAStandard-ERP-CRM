/**
 * Outstanding vs Received Widget (VAS_015)
 * Purpose - Glass 6x2 widget comparing, per month over the last 6 months, the
 *           open sales receivable balance (pale-blue bars) against actual
 *           collections (green line with point markers), so you can see whether
 *           collections are keeping pace with billing. Inline-SVG combo chart
 *           (light gridlines, bars + line, dated x-axis) with a legend below and
 *           a hover tooltip showing both values for the month.
 * Design  - design.md / PROMPT.md "Widget 13" + image_1. Inline SVG (no chart
 *           lib), redrawn on resize so it stays accurate at any size. Sizes in
 *           `em` per CLAUDE.md (the SVG itself is measured in px and redrawn).
 *           Namespaced vas-ovr-*.
 *
 * Backend - VAS_015_OutstandingVsReceived/GetSeries  (business logic in
 *           VASLogic.Models.VAS_015_OutstandingVsReceivedModel)
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────
 *  #  | Current Text                                  | Message Key                    | Reused?
 * ----+-----------------------------------------------+--------------------------------+--------
 *  1  | Outstanding vs received                       | VAS_015_OutstandingVsReceived  | new
 *  2  | Receivable balance against collections·6 mo   | VAS_015_Subtitle               | new
 *  3  | Outstanding receivable                        | VAS_015_OutstandingReceivable  | new
 *  4  | Received in period                            | VAS_015_ReceivedInPeriod       | new
 *  5  | No Data Found                                 | VIS_NoDataFound                | reused
 * ─────────────────────────────────────────────────────────────────────
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    /* Per-instance id suffix so multiple widgets' SVG gradient defs never clash. */
    var instanceSeq = 0;

    VAS.VAS_015_OutstandingVsReceived = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas-ovr-root">');
        var $chartEl;
        var $legendEl;
        var $busy;

        var uid = 'vasovr' + (++instanceSeq);
        var months = 6;
        var series = null;         /* latest OutstandingVsReceived payload */
        var resizeObserver = null;
        var rafId = 0;

        /* Hover state — refreshed on every draw() (the SVG is rebuilt each time). */
        var $tooltip;
        var bands = null;          /* [{ x, lineX, lineY, outstanding, received, month }] */

        function lbl(key, fallback) {
            var t = VIS.Msg.getMsg(key);
            return (t && t.charAt(0) !== '[') ? t : fallback;
        }

        function showBusy(show) {
            if (!$busy || !$busy[0]) { return; }
            $busy[0].style.visibility = show ? 'visible' : 'hidden';
        }

        function escapeHtml(value) {
            return String(value == null ? "" : value)
                .replace(/&/g, "&amp;")
                .replace(/</g, "&lt;")
                .replace(/>/g, "&gt;")
                .replace(/"/g, "&quot;")
                .replace(/'/g, "&#039;");
        }

        this.Initalize = function () {
            createWidget();
            loadData();
        };

        function loadData() {
            showBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_015_OutstandingVsReceived/GetSeries',
                type: 'GET',
                cache: false,
                data: { months: months },
                success: function (res) {
                    var data = typeof res === 'string' ? JSON.parse(res) : res;
                    if (data && typeof data === 'string') { data = JSON.parse(data); }

                    series = (data && !data.error) ? data : null;
                    renderLegend();
                    draw();
                },
                error: function () { series = null; renderLegend(); draw(); },
                complete: function () { showBusy(false); }
            });
        }

        function getStdPrecision() {
            try {
                if (VIS.Env && VIS.Env.getCtx && VIS.Env.getCtx().getStdPrecision) {
                    return VIS.Env.getCtx().getStdPrecision();
                }
            } catch (e) { /* fall through */ }
            return 2;
        }

        /* Compact-amount formatter: 10M→Cr, 100K→L, 1K→K; below 1000 → locale. */
        function formatCompactAmount(value, stdPrecision) {
            value = Number(value || 0);
            var neg = value < 0;
            var abs = Math.abs(value);
            var out;
            if (abs >= 10000000) { out = (abs / 10000000).toFixed(2).replace(/\.00$/, "") + "Cr"; }
            else if (abs >= 100000) { out = (abs / 100000).toFixed(2).replace(/\.00$/, "") + "L"; }
            else if (abs >= 1000) { out = (abs / 1000).toFixed(2).replace(/\.00$/, "") + "K"; }
            else {
                var prec = (typeof stdPrecision === "number") ? stdPrecision : getStdPrecision();
                out = abs.toLocaleString(window.navigator.language, {
                    minimumFractionDigits: prec, maximumFractionDigits: prec
                });
            }
            return (neg ? "-" : "") + out;
        }

        function formatAmount(value) {
            var sym = (series && series.CurrencySymbol) || "";
            var prec = series ? Number(series.StdPrecision) : getStdPrecision();
            return (sym ? sym : "") + formatCompactAmount(value, prec);
        }

        function formatExactAmount(value) {
            var sym = (series && series.CurrencySymbol) || "";
            var prec = series ? Number(series.StdPrecision) : getStdPrecision();
            return (sym ? ' ' + sym : '') + Number(value || 0).toLocaleString(window.navigator.language, {
                minimumFractionDigits: prec, maximumFractionDigits: prec
            });
        }

        /* "Jan 2026" from a yyyy-MM key (month name + year; local-time safe —
           split, don't Date-parse). */
        function formatMonth(key) {
            if (!key) { return ""; }
            var parts = String(key).split('-');
            if (parts.length !== 2) { return key; }
            var d = new Date(Number(parts[0]), Number(parts[1]) - 1, 1);
            if (isNaN(d.getTime())) { return key; }
            return d.toLocaleDateString(window.navigator.language, { month: 'short', year: 'numeric' });
        }

        function hasData() {
            if (!series || !series.Months || series.Months.length === 0) { return false; }
            for (var i = 0; i < series.Months.length; i++) {
                if (Number(series.Months[i].Outstanding || 0) !== 0 ||
                    Number(series.Months[i].Received || 0) !== 0) { return true; }
            }
            return false;
        }

        /* ── Chart rendering ────────────────────────────────────────────── */

        function draw() {
            if (!$chartEl || !$chartEl[0]) { return; }

            var wrap = $chartEl[0];
            var w = wrap.clientWidth;
            var h = wrap.clientHeight;
            if (w <= 0 || h <= 0) { return; }

            if (!hasData()) {
                bands = null;
                $chartEl.html('<div class="vas-ovr-empty">' + escapeHtml(lbl("VIS_NoDataFound", "No Data Found")) + '</div>');
                return;
            }

            var pts = series.Months;
            var n = pts.length;

            /* Plot box (px). Bottom band reserved for the dated x-axis. Tight side
               insets so the bars/line use the full card width. */
            var padL = 6, padR = 6, padTop = 12, padBottom = 22;
            var plotW = Math.max(1, w - padL - padR);
            var plotH = Math.max(1, h - padTop - padBottom);
            var bottom = padTop + plotH;

            /* Scale headroom: peak of both series (only positive amounts size the
               axis; negative net-outstanding clamps to the baseline). */
            var maxAmt = 0;
            for (var k = 0; k < n; k++) {
                if (Number(pts[k].Outstanding) > maxAmt) { maxAmt = Number(pts[k].Outstanding); }
                if (Number(pts[k].Received) > maxAmt) { maxAmt = Number(pts[k].Received); }
            }
            var maxY = maxAmt > 0 ? maxAmt * 1.12 : 1;   /* headroom above the peak */

            var bandW = plotW / n;
            function centerOf(i) { return padL + bandW * (i + 0.5); }
            function yOf(v) {
                var y = padTop + plotH * (1 - (Number(v) / maxY));
                if (y > bottom) { y = bottom; }
                if (y < padTop) { y = padTop; }
                return y;
            }

            /* Gridlines. */
            var grid = "";
            var gLines = 4;
            for (var g = 0; g <= gLines; g++) {
                var gy = padTop + plotH * (g / gLines);
                grid += '<line x1="' + padL + '" y1="' + gy.toFixed(1) + '" x2="' + (padL + plotW) +
                    '" y2="' + gy.toFixed(1) + '" class="vas-ovr-grid"/>';
            }

            /* Outstanding bars (pale blue), centred in each month band. */
            var barW = Math.max(4, bandW * 0.46);
            var bars = "";
            var bandRefs = [];
            for (var b = 0; b < n; b++) {
                var cx = centerOf(b);
                var barH = Math.max(0, bottom - yOf(pts[b].Outstanding));
                var bx = cx - barW / 2;
                var by = bottom - barH;
                bars += '<rect x="' + bx.toFixed(1) + '" y="' + by.toFixed(1) +
                    '" width="' + barW.toFixed(1) + '" height="' + barH.toFixed(1) +
                    '" rx="3" class="vas-ovr-bar"/>';

                bandRefs.push({
                    x: cx,
                    lineX: cx,
                    lineY: yOf(pts[b].Received),
                    outstanding: pts[b].Outstanding,
                    received: pts[b].Received,
                    month: pts[b].Month
                });
            }

            /* Received line (green) with point markers. */
            var linePts = [];
            var dots = "";
            for (var i = 0; i < n; i++) {
                var lx = centerOf(i), ly = yOf(pts[i].Received);
                linePts.push(lx.toFixed(1) + ',' + ly.toFixed(1));
            }
            var lineD = 'M' + linePts.join(' L');
            for (var d2 = 0; d2 < n; d2++) {
                dots += '<circle cx="' + centerOf(d2).toFixed(1) + '" cy="' + yOf(pts[d2].Received).toFixed(1) +
                    '" r="4" class="vas-ovr-dot"/>';
            }

            /* X-axis month labels (one per band). */
            var labels = "";
            for (var x = 0; x < n; x++) {
                labels += '<text x="' + centerOf(x).toFixed(1) + '" y="' + (h - 6) +
                    '" text-anchor="middle" class="vas-ovr-axis-label">' +
                    escapeHtml(formatMonth(pts[x].Month)) + '</text>';
            }

            var svg =
                '<svg class="vas-ovr-svg" width="' + w + '" height="' + h + '" viewBox="0 0 ' + w + ' ' + h + '" preserveAspectRatio="none" role="img">' +
                grid +
                bars +
                '<path d="' + lineD + '" class="vas-ovr-line"/>' +
                dots +
                labels +
                /* Hover guide line (shown/positioned on mousemove). */
                '<line class="vas-ovr-hover-line" x1="0" y1="' + padTop.toFixed(1) + '" x2="0" y2="' + bottom.toFixed(1) + '" style="display:none"/>' +
                '</svg>';

            $chartEl.html(svg);
            /* Tooltip is an HTML sibling (re-added after each svg rebuild). */
            $chartEl.append('<div class="vas-ovr-tooltip" style="display:none"></div>');

            $tooltip = $chartEl.find('.vas-ovr-tooltip');
            bands = { refs: bandRefs, padL: padL, bandW: bandW, n: n };
        }

        /* ── Hover interaction: snap to the nearest month, show both values ── */

        function onChartMove(e) {
            if (!bands || !bands.refs.length) { return; }

            var rect = $chartEl[0].getBoundingClientRect();
            var mx = e.clientX - rect.left;

            var idx = Math.floor((mx - bands.padL) / bands.bandW);
            if (idx < 0) { idx = 0; }
            if (idx > bands.n - 1) { idx = bands.n - 1; }

            var c = bands.refs[idx];

            var $line = $chartEl.find('.vas-ovr-hover-line');
            if ($line.length) { $line.attr({ x1: c.x.toFixed(1), x2: c.x.toFixed(1) }).css('display', ''); }

            if ($tooltip) {
                $tooltip.html(
                    '<span class="vas-ovr-tt-date">' + escapeHtml(formatMonth(c.month)) + '</span>' +
                    '<span class="vas-ovr-tt-row"><span class="vas-ovr-tt-key vas-ovr-tt-key-out">' +
                    escapeHtml(lbl("VAS_015_OutstandingReceivable", "Outstanding receivable")) + '</span>' +
                    '<span class="vas-ovr-tt-amt">' + escapeHtml(formatExactAmount(c.outstanding)) + '</span></span>' +
                    '<span class="vas-ovr-tt-row"><span class="vas-ovr-tt-key vas-ovr-tt-key-rec">' +
                    escapeHtml(lbl("VAS_015_ReceivedInPeriod", "Received in period")) + '</span>' +
                    '<span class="vas-ovr-tt-amt">' + escapeHtml(formatExactAmount(c.received)) + '</span></span>'
                );
                /* Clamp horizontally so the centered tooltip never spills past the
                   chart edges. */
                var half = $tooltip.outerWidth() / 2;
                var left = c.x;
                var maxW = $chartEl[0].clientWidth;
                if (left < half) { left = half; }
                if (left > maxW - half) { left = maxW - half; }
                $tooltip.css({ display: 'block', left: left + 'px', top: c.lineY + 'px' });
            }
        }

        function onChartLeave() {
            if ($tooltip) { $tooltip.css('display', 'none'); }
            var $line = $chartEl.find('.vas-ovr-hover-line');
            if ($line.length) { $line.css('display', 'none'); }
        }

        function scheduleDraw() {
            if (rafId) { window.cancelAnimationFrame(rafId); }
            rafId = window.requestAnimationFrame(function () { rafId = 0; draw(); });
        }

        /* ── Legend ─────────────────────────────────────────────────────── */

        function renderLegend() {
            if (!$legendEl) { return; }

            if (!hasData()) { $legendEl.empty(); return; }

            $legendEl.html(
                '<span class="vas-ovr-legend-item">' +
                '<span class="vas-ovr-swatch vas-ovr-swatch-bar"></span>' +
                '<span class="vas-ovr-legend-label">' + escapeHtml(lbl("VAS_015_OutstandingReceivable", "Outstanding receivable")) + '</span>' +
                '</span>' +
                '<span class="vas-ovr-legend-item">' +
                '<span class="vas-ovr-swatch vas-ovr-swatch-line"></span>' +
                '<span class="vas-ovr-legend-label">' + escapeHtml(lbl("VAS_015_ReceivedInPeriod", "Received in period")) + '</span>' +
                '</span>'
            );
        }

        function barLineIconSvg() {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">' +
                '<line x1="3" y1="21" x2="21" y2="21"></line>' +
                '<rect x="5" y="11" width="3.2" height="8"></rect>' +
                '<rect x="14" y="7" width="3.2" height="12"></rect>' +
                '<polyline points="4 9 9 12 14 6 20 9"></polyline>' +
                '</svg>';
        }

        function createWidget() {
            var $card = $(
                '<div class="vas-ovr-card">' +

                '<div class="vas-ovr-head">' +
                '<div class="vas-ovr-head-left">' +
                '<div class="vas-ovr-icon">' + barLineIconSvg() + '</div>' +
                '<div class="vas-ovr-title-group">' +
                '<div class="vas-ovr-title">' + escapeHtml(lbl("VAS_015_OutstandingVsReceived", "Outstanding vs received")) + '</div>' +
                '<div class="vas-ovr-subtitle">' + escapeHtml(lbl("VAS_015_Subtitle", "Receivable balance against collections · 6 months")) + '</div>' +
                '</div>' +
                '</div>' +
                '</div>' +

                '<div class="vas-ovr-chart"></div>' +

                '<div class="vas-ovr-legend"></div>' +

                '</div>'
            );

            $chartEl = $card.find('.vas-ovr-chart');
            $legendEl = $card.find('.vas-ovr-legend');

            /* Bound once; the handlers read the latest bands/refs set by draw(). */
            $chartEl.on('mousemove', onChartMove);
            $chartEl.on('mouseleave', onChartLeave);

            $root.append($card);

            $busy = $('<div class="vas-ovr-busy"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $busy[0].style.visibility = 'hidden';
            $root.append($busy);

            /* Redraw whenever the chart box changes size (sidebar toggle, window
               resize, dashboard relayout) so the SVG stays pixel-accurate. */
            if (window.ResizeObserver && $chartEl[0]) {
                resizeObserver = new ResizeObserver(function () { scheduleDraw(); });
                resizeObserver.observe($chartEl[0]);
            }
            $(window).on('resize.' + uid, scheduleDraw);
        }

        this.onResize = function () { scheduleDraw(); };

        this.refreshWidget = function () {
            loadData();
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            if (resizeObserver) { resizeObserver.disconnect(); resizeObserver = null; }
            $(window).off('resize.' + uid);
            if (rafId) { window.cancelAnimationFrame(rafId); rafId = 0; }
            $root.remove();
        };
    };

    VAS.VAS_015_OutstandingVsReceived.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VAS.VAS_015_OutstandingVsReceived.prototype.widgetSizeChange = function (height, width) {
        if (this.onResize) { this.onResize(); }
    };

    VAS.VAS_015_OutstandingVsReceived.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_015_OutstandingVsReceived.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
