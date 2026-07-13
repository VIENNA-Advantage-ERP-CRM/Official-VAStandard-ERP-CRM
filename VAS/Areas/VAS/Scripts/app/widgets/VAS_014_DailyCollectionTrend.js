/**
 * Daily Collection Trend Widget (VAS_014)
 * Purpose - Glass 6x2 widget charting daily AR cash inflow over the last 14
 *           days (base/accounting currency) as an inline-SVG line/area chart:
 *           light gridlines, a primary-blue line with soft area fill, a dashed
 *           violet 14-day-average baseline, a highlighted peak dot, and a dated
 *           x-axis (label every other day). Legend shows the peak (amount + date)
 *           and the average per day; an Export action copies the series to CSV.
 * Design  - design.md / PROMPT.md "Widget 11" + image_1. Inline SVG (no chart
 *           lib), redrawn on resize so it stays accurate at any size. Sizes in
 *           `em` per CLAUDE.md (the SVG itself is measured in px and redrawn).
 *           Namespaced vas-dct-*.
 *
 * Backend - VAS_014_DailyCollectionTrend/GetDailyTrend  (business logic in
 *           VASLogic.Models.VAS_014_DailyCollectionTrendModel)
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────
 *  #  | Current Text                  | Message Key                  | Reused?
 * ----+-------------------------------+------------------------------+--------
 *  1  | Daily collection trend        | VAS_014_DailyCollectionTrend | new
 *  2  | Cash inflow · last 14 days     | VAS_014_Subtitle             | new
 *  3  | Daily received                | VAS_014_DailyReceived        | new
 *  4  | peak                          | VAS_014_Peak                 | new
 *  5  | 14-day average                | VAS_014_FourteenDayAverage   | new
 *  6  | / day                         | VAS_014_PerDay               | new
 *  7  | No Data Found                 | VIS_NoDataFound              | reused
 * ─────────────────────────────────────────────────────────────────────
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    /* Per-instance id suffix so multiple widgets' SVG gradient defs never clash. */
    var instanceSeq = 0;

    /* design.md §Widget Header / §Measurement Setup: keep --dash-inline-size on
       :root equal to the dashboard container's current pixel width so the title
       clamp resolves against the dashboard's visible content area, not the
       viewport. A single document-level ResizeObserver serves every widget (the
       var is global); without a marked container — or without ResizeObserver —
       the CSS falls back to 100vw. */
    function ensureDashInlineSizeVar($el) {
        if (window.__vasDashInlineSizeObserver) { return; }
        if (typeof ResizeObserver === 'undefined') { return; }

        var container = $el.closest('.vis-widget-container, [data-dashboard-container]')[0];
        if (!container) { return; }

        var write = function () {
            document.documentElement.style.setProperty('--dash-inline-size', container.clientWidth + 'px');
        };

        window.__vasDashInlineSizeObserver = new ResizeObserver(write);
        window.__vasDashInlineSizeObserver.observe(container);
        write();
    }

    VAS.VAS_014_DailyCollectionTrend = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas-dct-root">');
        var $chartEl;
        var $legendEl;
        var $busy;

        var uid = 'vasdct' + (++instanceSeq);
        var days = 14;
        var trend = null;          /* latest DailyCollectionTrend payload */
        var resizeObserver = null;
        var rafId = 0;

        /* Hover state — refreshed on every draw() (the SVG is rebuilt each time). */
        var $tooltip, $hoverDot, $hoverLine;
        var geom = null;           /* { coords:[{x,y,amount,day}], padL, plotW, n } */

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
                url: VIS.Application.contextUrl + 'VAS_014_DailyCollectionTrend/GetDailyTrend',
                type: 'GET',
                cache: false,
                data: { days: days },
                success: function (res) {
                    var data = typeof res === 'string' ? JSON.parse(res) : res;
                    if (data && typeof data === 'string') { data = JSON.parse(data); }

                    trend = (data && !data.error) ? data : null;
                    renderLegend();
                    draw();
                },
                error: function () { trend = null; renderLegend(); draw(); },
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
            if (value >= 10000000) { return (value / 10000000).toFixed(2).replace(/\.00$/, "") + "Cr"; }
            if (value >= 100000) { return (value / 100000).toFixed(2).replace(/\.00$/, "") + "L"; }
            if (value >= 1000) { return (value / 1000).toFixed(2).replace(/\.00$/, "") + "K"; }
            var prec = (typeof stdPrecision === "number") ? stdPrecision : getStdPrecision();
            return value.toLocaleString(window.navigator.language, {
                minimumFractionDigits: prec, maximumFractionDigits: prec
            });
        }

        function formatAmount(value) {
            var sym = (trend && trend.CurrencySymbol) || "";
            var prec = trend ? Number(trend.StdPrecision) : getStdPrecision();
            var sign = (Number(value) < 0) ? '-' : '';
            return sign + (sym ? sym : "") + formatCompactAmount(Math.abs(Number(value || 0)), prec);
        }

        function formatExactAmount(value) {
            var sym = (trend && trend.CurrencySymbol) || "";
            var prec = trend ? Number(trend.StdPrecision) : getStdPrecision();
            var sign = (Number(value) < 0) ? '-' : '';
            return sign + (sym ? sym : '') + Number(Math.abs(Number(value || 0))).toLocaleString(window.navigator.language, {
                minimumFractionDigits: prec, maximumFractionDigits: prec
            });
        }

        /* "13 May" from a yyyy-MM-dd key (local-time safe — split, don't Date-parse). */
        function formatDay(key) {
            if (!key) { return ""; }
            var parts = String(key).split('-');
            if (parts.length !== 3) { return key; }
            var d = new Date(Number(parts[0]), Number(parts[1]) - 1, Number(parts[2]));
            if (isNaN(d.getTime())) { return key; }
            return d.toLocaleDateString(window.navigator.language, { day: 'numeric', month: 'short' });
        }

        function hasData() {
            return trend && trend.Points && trend.Points.length > 0 && Number(trend.PeakAmount || 0) > 0;
        }

        /* ── Chart rendering ────────────────────────────────────────────── */

        function draw() {
            if (!$chartEl || !$chartEl[0]) { return; }

            var wrap = $chartEl[0];
            var w = wrap.clientWidth;
            var h = wrap.clientHeight;
            if (w <= 0 || h <= 0) { return; }

            if (!hasData()) {
                geom = null;
                $chartEl.html('<div class="vas-dct-empty">' + escapeHtml(lbl("VIS_NoDataFound", "No Data Found")) + '</div>');
                return;
            }

            var points = trend.Points;
            var n = points.length;
            var avg = Number(trend.Average || 0);

            /* Scale chart geometry with the widget's resolved font-size so the chart grows
               to fill tall cells on large dashboards (16px base => s = 1). */
            var s = (parseFloat(window.getComputedStyle($chartEl[0]).fontSize) || 16) / 16;

            /* Plot box (px). Bottom band reserved for the dated x-axis. */
            var padL = 10 * s, padR = 12 * s, padTop = 12 * s, padBottom = 22 * s;
            var plotW = Math.max(1, w - padL - padR);
            var plotH = Math.max(1, h - padTop - padBottom);
            var bottom = padTop + plotH;

            var maxAmt = 0;
            for (var k = 0; k < n; k++) { if (Number(points[k].Amount) > maxAmt) { maxAmt = Number(points[k].Amount); } }
            if (avg > maxAmt) { maxAmt = avg; }
            var maxY = maxAmt > 0 ? maxAmt * 1.12 : 1;   /* headroom above the peak */

            function xOf(i) { return padL + (n === 1 ? plotW / 2 : plotW * (i / (n - 1))); }
            function yOf(v) { return padTop + plotH * (1 - (Number(v) / maxY)); }

            /* Gridlines. */
            var grid = "";
            var gLines = 4;
            for (var g = 0; g <= gLines; g++) {
                var gy = padTop + plotH * (g / gLines);
                grid += '<line x1="' + padL + '" y1="' + gy.toFixed(1) + '" x2="' + (padL + plotW) +
                    '" y2="' + gy.toFixed(1) + '" class="vas-dct-grid"/>';
            }

            /* Line + area paths (and per-point screen coords for hover). */
            var linePts = [];
            var coords = [];
            for (var i = 0; i < n; i++) {
                var cx = xOf(i), cy = yOf(points[i].Amount);
                linePts.push(cx.toFixed(1) + ',' + cy.toFixed(1));
                coords.push({ x: cx, y: cy, amount: points[i].Amount, day: points[i].Day });
            }
            var lineD = 'M' + linePts.join(' L');
            var areaD = lineD + ' L' + xOf(n - 1).toFixed(1) + ',' + bottom.toFixed(1) +
                ' L' + xOf(0).toFixed(1) + ',' + bottom.toFixed(1) + ' Z';

            /* Average baseline. */
            var avgY = yOf(avg).toFixed(1);
            var avgLine = '<line x1="' + padL + '" y1="' + avgY + '" x2="' + (padL + plotW) + '" y2="' + avgY +
                '" class="vas-dct-avg"/>';

            /* Peak dot. */
            var peakIdx = -1;
            for (var p = 0; p < n; p++) { if (points[p].Day === trend.PeakDay) { peakIdx = p; break; } }
            var peakDot = "";
            if (peakIdx >= 0) {
                peakDot = '<circle cx="' + xOf(peakIdx).toFixed(1) + '" cy="' + yOf(points[peakIdx].Amount).toFixed(1) +
                    '" r="' + (4.5 * s).toFixed(1) + '" class="vas-dct-peak-dot"/>';
            }

            /* X-axis date labels (every other day). */
            var labels = "";
            for (var x = 0; x < n; x++) {
                if (x % 2 !== 0) { continue; }
                var anchor = (x === 0) ? 'start' : (x === n - 1 ? 'end' : 'middle');
                labels += '<text x="' + xOf(x).toFixed(1) + '" y="' + (h - 6 * s).toFixed(1) + '" text-anchor="' + anchor +
                    '" class="vas-dct-axis-label">' + escapeHtml(formatDay(points[x].Day)) + '</text>';
            }

            var svg =
                '<svg class="vas-dct-svg" width="' + w + '" height="' + h + '" viewBox="0 0 ' + w + ' ' + h + '" preserveAspectRatio="none" role="img">' +
                '<defs><linearGradient id="' + uid + '-area" x1="0" y1="0" x2="0" y2="1">' +
                '<stop offset="0%" stop-color="#0083DA" stop-opacity="0.20"/>' +
                '<stop offset="100%" stop-color="#0083DA" stop-opacity="0"/>' +
                '</linearGradient></defs>' +
                grid +
                '<path d="' + areaD + '" fill="url(#' + uid + '-area)" stroke="none"/>' +
                avgLine +
                '<path d="' + lineD + '" class="vas-dct-line"/>' +
                peakDot +
                labels +
                /* Hover guide line + dot (shown/positioned on mousemove). */
                '<line class="vas-dct-hover-line" x1="0" y1="' + padTop.toFixed(1) + '" x2="0" y2="' + bottom.toFixed(1) + '" style="display:none"/>' +
                '<circle class="vas-dct-hover-dot" r="' + (4.5 * s).toFixed(1) + '" style="display:none"/>' +
                '</svg>';

            $chartEl.html(svg);
            /* Tooltip is an HTML sibling (re-added after each svg rebuild). */
            $chartEl.append('<div class="vas-dct-tooltip" style="display:none"></div>');

            $hoverLine = $chartEl.find('.vas-dct-hover-line');
            $hoverDot = $chartEl.find('.vas-dct-hover-dot');
            $tooltip = $chartEl.find('.vas-dct-tooltip');
            geom = { coords: coords, padL: padL, plotW: plotW, n: n };
        }

        /* ── Hover interaction: snap to the nearest day, show date + amount ── */

        function onChartMove(e) {
            if (!geom || !geom.coords.length) { return; }

            var rect = $chartEl[0].getBoundingClientRect();
            var mx = e.clientX - rect.left;

            var step = geom.n > 1 ? geom.plotW / (geom.n - 1) : geom.plotW;
            var idx = Math.round((mx - geom.padL) / step);
            if (idx < 0) { idx = 0; }
            if (idx > geom.n - 1) { idx = geom.n - 1; }

            var c = geom.coords[idx];

            if ($hoverDot) { $hoverDot.attr({ cx: c.x.toFixed(1), cy: c.y.toFixed(1) }).css('display', ''); }
            if ($hoverLine) { $hoverLine.attr({ x1: c.x.toFixed(1), x2: c.x.toFixed(1) }).css('display', ''); }

            if ($tooltip) {
                $tooltip.html(
                    '<span class="vas-dct-tt-date">' + escapeHtml(formatDay(c.day)) + '</span>' +
                    '<span class="vas-dct-tt-amt">' + escapeHtml(formatExactAmount(c.amount)) + '</span>'
                );
                /* Clamp horizontally so the centered tooltip never spills past the
                   chart edges. */
                var half = $tooltip.outerWidth() / 2;
                var left = c.x;
                var maxW = $chartEl[0].clientWidth;
                if (left < half) { left = half; }
                if (left > maxW - half) { left = maxW - half; }

                /* Clamp vertically: default position is above the dot (transform
                   pulls it up by its own height + 0.625em gap). If that would push it
                   above the chart top, flip it below the dot instead so the card's
                   overflow:hidden never clips it. */
                var tipH = $tooltip.outerHeight();
                var gapEm = parseFloat(window.getComputedStyle($tooltip[0]).fontSize) * 0.625;
                var topAbove = c.y - tipH - gapEm;
                var topStyle, transformStyle;
                if (topAbove < 0) {
                    /* Flip below: position top just below the dot; no upward transform. */
                    topStyle = (c.y + gapEm) + 'px';
                    transformStyle = 'translateX(-50%)';
                } else {
                    topStyle = c.y + 'px';
                    transformStyle = 'translate(-50%, calc(-100% - 0.625em))';
                }
                $tooltip.css({ display: 'block', left: left + 'px', top: topStyle, transform: transformStyle });
            }
        }

        function onChartLeave() {
            if ($tooltip) { $tooltip.css('display', 'none'); }
            if ($hoverDot) { $hoverDot.css('display', 'none'); }
            if ($hoverLine) { $hoverLine.css('display', 'none'); }
        }

        function scheduleDraw() {
            if (rafId) { window.cancelAnimationFrame(rafId); }
            rafId = window.requestAnimationFrame(function () { rafId = 0; draw(); });
        }

        /* ── Legend ─────────────────────────────────────────────────────── */

        function renderLegend() {
            if (!$legendEl) { return; }

            if (!hasData()) { $legendEl.empty(); return; }

            var peakText = lbl("VAS_014_Peak", "peak") + ' ' + formatAmount(trend.PeakAmount) +
                ' · ' + formatDay(trend.PeakDay);
            var avgText = formatAmount(trend.Average) + ' ' + lbl("VAS_014_PerDay", "/ day");

            $legendEl.html(
                '<span class="vas-dct-legend-item">' +
                '<span class="vas-dct-dot vas-dct-dot-line"></span>' +
                '<span class="vas-dct-legend-label">' + escapeHtml(lbl("VAS_014_DailyReceived", "Daily received")) + '</span>' +
                '<span class="vas-dct-legend-strong">' + escapeHtml(peakText) + '</span>' +
                '</span>' +
                '<span class="vas-dct-legend-item">' +
                '<span class="vas-dct-dot vas-dct-dot-avg"></span>' +
                '<span class="vas-dct-legend-label">' + escapeHtml(lbl("VAS_014_FourteenDayAverage", "14-day average")) + '</span>' +
                '<span class="vas-dct-legend-strong">' + escapeHtml(avgText) + '</span>' +
                '</span>'
            );
        }

        /* ── Export (client-side CSV of the visible series) ─────────────── */

        function exportCsv() {
            if (!hasData()) { return; }

            var sym = (trend.CurrencySymbol) || "";
            var rows = ['Date,Amount' + (sym ? ' (' + sym + ')' : '')];
            for (var i = 0; i < trend.Points.length; i++) {
                rows.push(trend.Points[i].Day + ',' + Number(trend.Points[i].Amount || 0));
            }
            var csv = rows.join('\r\n');

            var blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' });
            var url = window.URL.createObjectURL(blob);
            var a = document.createElement('a');
            a.href = url;
            a.download = 'daily-collection-trend.csv';
            document.body.appendChild(a);
            a.click();
            document.body.removeChild(a);
            window.URL.revokeObjectURL(url);
        }

        function trendIconSvg() {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">' +
                '<polyline points="3 17 9 11 13 15 21 7"></polyline>' +
                '<polyline points="15 7 21 7 21 13"></polyline>' +
                '</svg>';
        }

        function createWidget() {
            var $card = $(
                '<div class="vas-dct-card">' +

                '<div class="vas-dct-head">' +
                '<div class="vas-dct-head-left">' +
                '<div class="vas-dct-icon">' + trendIconSvg() + '</div>' +
                '<div class="vas-dct-title-group">' +
                '<div class="vas-dct-title">' + escapeHtml(lbl("VAS_014_DailyCollectionTrend", "Daily collection trend")) + '</div>' +
                '<div class="vas-dct-subtitle">' + escapeHtml(lbl("VAS_014_Subtitle", "Cash inflow · last 14 days")) + '</div>' +
                '</div>' +
                '</div>' +
                '</div>' +

                '<div class="vas-dct-chart"></div>' +

                '<div class="vas-dct-legend"></div>' +

                '</div>'
            );

            $chartEl = $card.find('.vas-dct-chart');
            $legendEl = $card.find('.vas-dct-legend');

            /* Bound once; the handlers read the latest geom/refs set by draw(). */
            $chartEl.on('mousemove', onChartMove);
            $chartEl.on('mouseleave', onChartLeave);

            $card.find('.vas-dct-export').on('click', function (e) {
                e.stopPropagation();
                exportCsv();
            });

            $root.append($card);

            $busy = $('<div class="vas-dct-busy"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
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

    VAS.VAS_014_DailyCollectionTrend.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        /* Self-wire the dashboard-width CSS variable the title clamp reads. */
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_014_DailyCollectionTrend.prototype.widgetSizeChange = function (height, width) {
        if (this.onResize) { this.onResize(); }
    };

    VAS.VAS_014_DailyCollectionTrend.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_014_DailyCollectionTrend.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
