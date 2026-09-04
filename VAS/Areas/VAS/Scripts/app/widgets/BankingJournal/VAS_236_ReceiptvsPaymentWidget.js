/************************************************************
 * Module Name    : VAS
 * Purpose        : Receipts vs Payments Trend - a 5x2 composite chart for the
 *                  Banking dashboard.
 *
 *                  Money in against money out over time, as clustered bars, with the
 *                  net of the two drawn over them as a line:
 *
 *                    [bars] Receipts vs Payments Trend    [ Daily · Last 14d v ]
 *                           ₹1.92Cr in · ₹1.54Cr out · +₹38.6L net
 *
 *                    ■ Receipts  ■ Payments  ▬ Net
 *                    ┊     ▄█            ▄█   ╱▔╲
 *                    ┊  ▄█ ██  ▄█ ▄█ ▄█  ██ ╱     ╲
 *                    ┊  ██ ██  ██ ██ ██  ██
 *                       8  9  10 11 12  13 14 ...
 *
 *                  ONE CHART, TWO SCALES, ON PURPOSE. The bars share a scale anchored
 *                  at zero, because their heights are meant to be compared with one
 *                  another. The net line has its OWN scale centred on a mid-line,
 *                  because net is a signed quantity an order of magnitude smaller than
 *                  the gross flows - drawn against the bar scale it would sit flat on
 *                  the floor and say nothing. The mid-line IS the zero of the net
 *                  scale, so a line above it means the period took in more than it
 *                  paid out.
 *
 *                  DRAWN AT MEASURED PIXEL SIZE, not stretched. The specification's
 *                  preserveAspectRatio="none" would let one fixed viewBox fill any
 *                  cell, but it scales the two axes by different factors: the 3px net
 *                  markers become ellipses and the 2.4px line is thicker horizontally
 *                  than vertically. Instead the plot is measured and the geometry is
 *                  recomputed at 1:1, so strokes and markers stay true at every widget
 *                  size. A ResizeObserver redraws on resize, dashboard re-layout and
 *                  browser zoom alike, from data already in hand - no round trip.
 *
 *                  EVERY BUCKET IS PLOTTED, including the empty ones - the server
 *                  returns them, so a quiet day is a zero on the axis rather than a
 *                  gap that would misplace the line.
 *
 *                  The pill sets grain and range together. Both are KEYS the server
 *                  whitelists; the client never sends a date, an interval or an
 *                  expression.
 *
 *                  Amounts arrive already converted into the tenant's base
 *                  (accounting-schema) currency and are formatted through the shared
 *                  VIS.Util.formatCompactAmount helper
 *                  (Scripts/app/util/CurrencyFormat.js).
 *
 *                  Design: design.md -> dashboard-widgets.md (Glass Widget, Widget
 *                  Header, Content Fit Budget, No Inner Scrollbars) supplies the shell
 *                  and the header typography; the widget specification supplies the
 *                  chart anatomy and its named series colours - receipts #20A464,
 *                  payments #D14545, net #0083DA, the same tokens the Aging card uses
 *                  so the colours are learned once.
 *
 *                  Summary Message Table
 *                  Rows marked (reuse) already exist under another key and are NOT
 *                  duplicated here.
 *                   # | Current Text                  | Message Key
 *                  ---+-------------------------------+-----------------------------
 *                   1 | Receipts vs Payments Trend    | VAS_236_ReceiptvsPayment
 *                   2 | in                            | VAS_236_In
 *                   3 | out                           | VAS_236_Out
 *                   4 | net                           | VAS_236_NetLower
 *                   5 | Receipts                      | VAS_236_Receipts
 *                   6 | Payments                      | VAS_236_Payments
 *                   7 | Net                           | VAS_236_Net
 *                   8 | Daily                         | VAS_236_Daily
 *                   9 | Weekly                        | VAS_236_Weekly
 *                  10 | Monthly                       | VAS_236_Monthly
 *                  11 | Last 7d                       | VAS_236_Last7
 *                  12 | Last 14d                      | VAS_236_Last14
 *                  13 | Last 30d                      | VAS_236_Last30
 *                  14 | Last 90d                      | VAS_236_Last90
 *                  15 | Grain                         | VAS_236_Grain
 *                  16 | Range                         | VAS_236_Range
 *                  17 | No movement in this period    | VAS_236_NoMovement
 *                  18 | Couldn't load                 | VAS_192_CouldntLoad (reuse)
 *
 * Chronological development:
 *   VAI154         Created  Date 2026-09-03
 ***********************************************************/
; VAS = window.VAS || {};

; (function (VAS, $) {

    /* CSS lives in VAS/Areas/VAS/Content/VAS_236_ReceiptvsPaymentWidget.css. All classes
       are namespaced `vas-236-` so they never collide with sibling widgets. */

    /* design.md §Widget Header / §Measurement Setup: keep --dash-inline-size on :root
       equal to the dashboard container's current pixel width so the header clamps
       resolve against the dashboard's visible content area, not the viewport. One
       document-level observer serves every widget. */
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

    /* Inline SVG, not an icon-font class - the host shell does not always load an icon
       font and a missing glyph leaves an empty box. Explicit width/height as well as a
       viewBox: an SVG with only a viewBox falls back to 300x150px if a stylesheet is
       stale, which would sprawl across the header. */
    var ICONS = {
        bars: '<svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" ' +
            'stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
            '<line x1="18" y1="20" x2="18" y2="10"></line>' +
            '<line x1="12" y1="20" x2="12" y2="4"></line>' +
            '<line x1="6" y1="20" x2="6" y2="14"></line></svg>',
        chevron: '<svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" ' +
            'stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
            '<polyline points="6 9 12 15 18 9"></polyline></svg>',
        tick: '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" ' +
            'stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
            '<polyline points="20 6 9 17 4 12"></polyline></svg>'
    };

    /* Series colours - the specification's, and the same tokens the Aging card uses so a
       reader learns receipts-green and payments-red once for the whole dashboard. */
    var COLOR_RECEIPT = '#20A464';
    var COLOR_PAYMENT = '#D14545';
    var COLOR_NET = '#0083DA';
    var COLOR_GRID = 'rgba(15,61,97,0.08)';

    /* Grain and range whitelists, mirroring the server's constants. The control sends one
       of these KEYS and nothing else - no dates, no intervals, no expressions. */
    var GRAINS = [
        { key: 'day', msg: 'VAS_236_Daily', text: 'Daily' },
        { key: 'week', msg: 'VAS_236_Weekly', text: 'Weekly' },
        { key: 'month', msg: 'VAS_236_Monthly', text: 'Monthly' }
    ];

    var RANGES = [
        { days: 7, msg: 'VAS_236_Last7', text: 'Last 7d' },
        { days: 14, msg: 'VAS_236_Last14', text: 'Last 14d' },
        { days: 30, msg: 'VAS_236_Last30', text: 'Last 30d' },
        { days: 90, msg: 'VAS_236_Last90', text: 'Last 90d' }
    ];

    /* Plot geometry, in CSS pixels at the measured size. */
    var PAD_L = 8, PAD_R = 8, PAD_T = 14, PAD_B = 8;
    var BAR_GROUP_FRACTION = 0.30;   /* each bar is this share of its group's width */
    var BAR_GAP_FRACTION = 0.08;     /* gap between the two bars of a group */
    var BAR_MIN_WIDTH = 1.5;         /* a 90-day range still has to draw something */
    var HEADROOM = 1.12;             /* the tallest bar stops short of the ceiling */
    var NET_MID_FRACTION = 0.55;     /* the net scale's zero, down the plot */
    var NET_AMPLITUDE = 0.40;        /* how far the net line may swing either way */
    var AXIS_MAX_LABELS = 14;        /* beyond this the axis thins itself out */

    VAS.VAS_236_ReceiptvsPaymentWidget = function () {
        this.frame;
        this.windowNo;
        this.widgetInfo;

        var $self = this;
        var $root;
        var $card;
        var $subtitle;
        var $periodBtn;
        var $plot;
        var $axis;
        var $state;
        var $busy;
        var $picker;

        var widgetID = 0;

        /* Unique event namespace per instance - a widget can sit twice on one dashboard,
           and the picker binds document-level handlers. */
        var _ns = '';

        var _points = [];
        var _currency = null;
        var _grain = 'day';
        var _range = 14;
        var _totalReceipts = 0;
        var _totalPayments = 0;
        var _totalNet = 0;

        var _pickerOpen = false;
        var _disposed = false;
        var _rootObserver = null;
        var _plotObserver = null;
        var _redrawQueued = false;

        /* ------------------------------------------------------------ */
        /* Lifecycle                                                    */
        /* ------------------------------------------------------------ */
        this.initalize = function () {
            widgetID = (VIS.Utility && VIS.Utility.Util
                ? VIS.Utility.Util.getValueOfInt($self.widgetInfo.AD_UserHomeWidgetID)
                : 0);
            if (widgetID === 0) { widgetID = $self.windowNo; }
            _ns = '.vas236_' + widgetID;

            buildSkeleton();
            createBusyIndicator();
            setupRootObserver();
            setupPlotObserver();
        };

        /* The framework's own widget loader, overlaid on the whole card while a read is in
           flight - the same treatment every sibling VAS widget gives its loads. It covers
           EVERY read: the initial load, the Refresh button and a grain or range change all
           replace the chart, and each deserves to say so. Created visible so it is already
           up from the moment the widget mounts. */
        function createBusyIndicator() {
            $busy = $('<div class="vis-busyindicatorouterwrap vas-widget-busy-wrap">' +
                '<div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div>' +
            '</div>');
            $busy[0].style.visibility = 'visible';
            $root.append($busy);
        }

        function showBusyIndicator() {
            if ($busy && $busy[0]) { $busy[0].style.visibility = 'visible'; }
        }

        function hideBusyIndicator() {
            if ($busy && $busy[0]) { $busy[0].style.visibility = 'hidden'; }
        }

        /* Publishes THIS widget's own pixel width as --widget-inline-size on its root,
           which is the first variable the card's font-size clamp reads (the dashboard
           width is only the fallback). Every sibling widget does exactly this - without it
           the card measures itself against the whole dashboard and renders a size larger
           than its neighbours. */
        function setupRootObserver() {
            if (typeof ResizeObserver === 'undefined') { return; }
            try {
                _rootObserver = new ResizeObserver(function (entries) {
                    if (!$root || !$root[0]) { return; }

                    for (var i = 0; i < entries.length; i++) {
                        var width = entries[i].contentRect.width;
                        if (width > 0) {
                            $root[0].style.setProperty('--widget-inline-size', width + 'px');
                        }
                    }
                });
                _rootObserver.observe($root[0]);
            } catch (e) { /* the clamp falls back to --dash-inline-size */ }
        }

        /* The chart is drawn at the plot's MEASURED size, so it has to be redrawn whenever
           that size changes - a widget resize, a dashboard re-layout or a browser zoom,
           all of which move the element's CSS-pixel box and so fire here. The redraw uses
           data already in hand; it never costs a request. */
        function setupPlotObserver() {
            if (typeof ResizeObserver === 'undefined') { return; }
            try {
                _plotObserver = new ResizeObserver(function () {
                    if (_disposed) { return; }
                    scheduleRedraw();
                });
                _plotObserver.observe($plot[0]);
            } catch (e) { /* the chart still draws once, on load */ }
        }

        /* Coalesced to one draw per frame: a drag fires the observer continuously, and
           rebuilding the SVG string on every one of those callbacks would be wasteful. */
        function scheduleRedraw() {
            if (_redrawQueued) { return; }
            _redrawQueued = true;

            var raf = window.requestAnimationFrame || function (cb) { return window.setTimeout(cb, 16); };
            raf(function () {
                _redrawQueued = false;
                if (_disposed) { return; }
                drawChart();
            });
        }

        this.intialLoad = function () {
            loadData();
        };

        /* The dashboard's Refresh button calls this. The chosen grain and range are kept -
           they are a view the user set, not a position in the data. */
        this.refreshWidget = function () {
            loadData();
        };

        /* ------------------------------------------------------------ */
        /* DOM skeleton                                                 */
        /* ------------------------------------------------------------ */
        function buildSkeleton() {
            $root = $('<div class="vas-236-root" id="vas-236-root-' + widgetID + '"></div>');

            var title = label('VAS_236_ReceiptvsPayment', 'Receipts vs Payments Trend');

            $card = $(
                '<div class="vas-236-card">' +
                    '<div class="vas-236-header">' +
                        '<span class="vas-236-icon">' + ICONS.bars + '</span>' +
                        '<div class="vas-236-head-text">' +
                            '<div class="vas-236-title"></div>' +
                            '<div class="vas-236-subtitle"></div>' +
                        '</div>' +
                        '<button type="button" class="vas-236-period" aria-haspopup="listbox">' +
                            '<span class="vas-236-period-label"></span>' +
                            ICONS.chevron +
                        '</button>' +
                    '</div>' +
                    '<div class="vas-236-body">' +
                        '<div class="vas-236-legend"></div>' +
                        '<div class="vas-236-plot"></div>' +
                        '<div class="vas-236-axis"></div>' +
                    '</div>' +
                    '<div class="vas-236-state vas-236-hidden"></div>' +
                '</div>'
            );

            $card.find('.vas-236-title').text(title).attr('title', title);

            $subtitle = $card.find('.vas-236-subtitle');
            $periodBtn = $card.find('.vas-236-period');
            $plot = $card.find('.vas-236-plot');
            $axis = $card.find('.vas-236-axis');
            $state = $card.find('.vas-236-state');

            paintLegend();
            paintPeriodLabel();

            $periodBtn.on('click' + _ns, function (e) {
                e.preventDefault();
                e.stopPropagation();
                togglePicker();
            });

            $root.append($card);
        }

        /* ------------------------------------------------------------ */
        /* Data                                                         */
        /* ------------------------------------------------------------ */
        function loadData() {
            showBusyIndicator();

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_236_ReceiptvsPaymentWidget/GetTrend',
                type: 'GET',
                dataType: 'json',
                /* Asynchronous, always - nothing here justifies blocking the UI thread. */
                async: true,
                data: { grain: _grain, rangeDays: _range },
                success: function (raw) {
                    if (_disposed) { return; }
                    hideBusyIndicator();

                    var data = parseResponse(raw);
                    if (!data || data.error || !data.Loaded) {
                        renderState(label('VAS_192_CouldntLoad', "Couldn't load"));
                        return;
                    }

                    _points = data.Points || [];
                    _currency = data.Currency || null;
                    _grain = data.Grain || _grain;
                    _range = Number(data.RangeDays) || _range;
                    _totalReceipts = Number(data.TotalReceipts) || 0;
                    _totalPayments = Number(data.TotalPayments) || 0;
                    _totalNet = Number(data.TotalNet) || 0;

                    $state.addClass('vas-236-hidden');
                    $card.find('.vas-236-body').removeClass('vas-236-hidden');

                    paintSubtitle();
                    paintPeriodLabel();
                    drawChart();
                },
                error: function () {
                    if (_disposed) { return; }
                    /* The overlay comes down on failure too - a spinner left running over
                       an error the user cannot see is the worst of both. */
                    hideBusyIndicator();
                    renderState(label('VAS_192_CouldntLoad', "Couldn't load"));
                }
            });
        }

        /* The controller returns a JSON string inside a JSON response. */
        function parseResponse(raw) {
            try {
                return (typeof raw === 'string') ? (raw ? JSON.parse(raw) : null) : raw;
            }
            catch (e) { if (window.console) { console.log(e); } return null; }
        }

        /* ------------------------------------------------------------ */
        /* Render - header                                              */
        /* ------------------------------------------------------------ */

        /* A load failure takes the card over. A range with no movement does NOT - that is
           said inside the plot, so the header and its (zero) totals stay put. */
        function renderState(text) {
            $card.find('.vas-236-body').addClass('vas-236-hidden');
            $state.removeClass('vas-236-hidden').text(text);
        }

        /* "₹1.92Cr in · ₹1.54Cr out · +₹38.6L net" - the whole range in one line, so the
           card answers "how much, overall" without the reader summing the bars. */
        function paintSubtitle() {
            var text = money(_totalReceipts) + ' ' + label('VAS_236_In', 'in') +
                ' · ' + money(_totalPayments) + ' ' + label('VAS_236_Out', 'out') +
                ' · ' + signedMoney(_totalNet) + ' ' + label('VAS_236_NetLower', 'net');

            $subtitle.text(text).attr('title', text);
        }

        /* The legend names the three series ONCE, above the plot - so no bar or marker
           has to carry a label of its own. The net swatch is a RULE rather than a square,
           because that is what it is on the chart. */
        function paintLegend() {
            $card.find('.vas-236-legend').html(
                '<span class="vas-236-lg">' +
                    '<span class="vas-236-sw vas-236-sw-r"></span>' +
                    escapeHtml(label('VAS_236_Receipts', 'Receipts')) +
                '</span>' +
                '<span class="vas-236-lg">' +
                    '<span class="vas-236-sw vas-236-sw-p"></span>' +
                    escapeHtml(label('VAS_236_Payments', 'Payments')) +
                '</span>' +
                '<span class="vas-236-lg">' +
                    '<span class="vas-236-sw vas-236-sw-n"></span>' +
                    escapeHtml(label('VAS_236_Net', 'Net')) +
                '</span>'
            );
        }

        function paintPeriodLabel() {
            var text = grainText(_grain) + ' · ' + rangeText(_range);
            $periodBtn.find('.vas-236-period-label').text(text);
            $periodBtn.attr('title', text);
        }

        function grainText(key) {
            for (var i = 0; i < GRAINS.length; i++) {
                if (GRAINS[i].key === key) { return label(GRAINS[i].msg, GRAINS[i].text); }
            }
            return label(GRAINS[0].msg, GRAINS[0].text);
        }

        function rangeText(days) {
            for (var i = 0; i < RANGES.length; i++) {
                if (RANGES[i].days === days) { return label(RANGES[i].msg, RANGES[i].text); }
            }
            return label(RANGES[1].msg, RANGES[1].text);
        }

        /* ------------------------------------------------------------ */
        /* Render - the chart                                           */
        /* ------------------------------------------------------------ */

        /* Drawn at the plot's MEASURED pixel size rather than through a fixed viewBox with
           preserveAspectRatio="none". Stretching one viewBox to fill the cell scales the
           two axes by different factors, which turns the round net markers into ellipses
           and makes the line thicker across than down. Recomputing the geometry at 1:1
           costs one pass over an array the widget already holds. */
        function drawChart() {
            if (!$plot || !$plot[0]) { return; }

            var w = $plot[0].clientWidth;
            var h = $plot[0].clientHeight;

            /* The card may not have been laid out yet - try again on the next frame rather
               than drawing into a zero-sized box. */
            if (w <= 0 || h <= 0) { return; }

            if (!_points || _points.length === 0) {
                $plot.html('<div class="vas-236-empty">' +
                    escapeHtml(label('VAS_236_NoMovement', 'No movement in this period')) + '</div>');
                $axis.empty();
                return;
            }

            var plotW = w - PAD_L - PAD_R;
            var plotH = h - PAD_T - PAD_B;
            if (plotW <= 0 || plotH <= 0) { return; }

            var svg = '';

            /* ---- grid: three rules at 25 / 50 / 75% of the plot ---- */
            for (var g = 1; g <= 3; g++) {
                var gy = PAD_T + (plotH / 4) * g;
                svg += '<line x1="' + PAD_L + '" y1="' + gy.toFixed(1) + '" x2="' + (w - PAD_R) +
                    '" y2="' + gy.toFixed(1) + '" stroke="' + COLOR_GRID + '" stroke-width="1"></line>';
            }

            /* ---- bar scale: anchored at zero, with headroom so the tallest bar does not
                   touch the ceiling. An all-zero range still needs a non-zero divisor. ---- */
            var maxBar = 0;
            for (var i = 0; i < _points.length; i++) {
                var r = Math.abs(Number(_points[i].Receipts) || 0);
                var p = Math.abs(Number(_points[i].Payments) || 0);
                if (r > maxBar) { maxBar = r; }
                if (p > maxBar) { maxBar = p; }
            }
            var barScale = (maxBar > 0 ? maxBar : 1) * HEADROOM;

            var n = _points.length;
            var group = plotW / n;
            var barW = Math.max(BAR_MIN_WIDTH, group * BAR_GROUP_FRACTION);
            var gap = group * BAR_GAP_FRACTION;

            var barY = function (v) { return PAD_T + plotH - (Math.abs(v) / barScale) * plotH; };
            var groupX = function (idx) { return PAD_L + idx * group + group / 2; };

            /* ---- the clustered bars ---- */
            for (var b = 0; b < n; b++) {
                var pt = _points[b];
                var rv = Math.abs(Number(pt.Receipts) || 0);
                var pv = Math.abs(Number(pt.Payments) || 0);

                var cx = groupX(b);
                var rx = cx - barW - gap / 2;
                var px = cx + gap / 2;

                if (rv > 0) {
                    svg += bar(rx, barY(rv), barW, PAD_T + plotH - barY(rv), COLOR_RECEIPT, '0.92');
                }
                if (pv > 0) {
                    svg += bar(px, barY(pv), barW, PAD_T + plotH - barY(pv), COLOR_PAYMENT, '0.88');
                }
            }

            /* ---- the net line, on its OWN scale ----
               Net is a signed quantity an order of magnitude smaller than the gross flows;
               on the bar scale it would lie flat along the floor. Its own scale is centred
               on a mid-line - which is the net zero - so the line reads above or below it. */
            var netMax = 0;
            for (var m = 0; m < n; m++) {
                var nv = Math.abs(Number(_points[m].Net) || 0);
                if (nv > netMax) { netMax = nv; }
            }
            netMax = (netMax > 0 ? netMax : 1) * 1.2;

            var midY = PAD_T + plotH * NET_MID_FRACTION;
            var netY = function (v) { return midY - ((Number(v) || 0) / netMax) * (plotH * NET_AMPLITUDE); };

            var path = '';
            for (var l = 0; l < n; l++) {
                path += (l ? 'L' : 'M') + groupX(l).toFixed(1) + ' ' + netY(_points[l].Net).toFixed(1) + ' ';
            }

            svg += '<path d="' + path + '" fill="none" stroke="' + COLOR_NET +
                '" stroke-width="2.4" stroke-linejoin="round" stroke-linecap="round"></path>';

            /* Markers only while they can be told apart - at 90 daily points they would
               merge into a caterpillar and hide the line they are meant to punctuate. */
            if (group >= 14) {
                for (var k = 0; k < n; k++) {
                    svg += '<circle cx="' + groupX(k).toFixed(1) + '" cy="' + netY(_points[k].Net).toFixed(1) +
                        '" r="3" fill="#FFFFFF" stroke="' + COLOR_NET + '" stroke-width="2"></circle>';
                }
            }

            /* ---- one transparent hit area per group, carrying the numbers ----
               A native <title> is the whole tooltip: it needs no positioning code, it
               cannot escape the widget, and it reaches assistive tech. */
            for (var t = 0; t < n; t++) {
                var hx = PAD_L + t * group;
                svg += '<rect class="vas-236-hit" x="' + hx.toFixed(1) + '" y="' + PAD_T +
                    '" width="' + group.toFixed(1) + '" height="' + plotH.toFixed(1) + '" fill="transparent">' +
                    '<title>' + escapeHtml(pointTooltip(_points[t])) + '</title>' +
                '</rect>';
            }

            $plot.html('<svg class="vas-236-svg" viewBox="0 0 ' + w + ' ' + h + '" width="' + w +
                '" height="' + h + '" role="img" aria-label="' +
                escapeHtml(label('VAS_236_ReceiptvsPayment', 'Receipts vs Payments Trend')) + '">' +
                svg + '</svg>');

            paintAxis(n);
        }

        function bar(x, y, width, height, fill, opacity) {
            /* A radius larger than half the bar width renders as a lozenge, so it is capped
               - at a 90-day range the bars are only a couple of pixels wide. */
            var rx = Math.min(3, width / 2);

            return '<rect x="' + x.toFixed(1) + '" y="' + y.toFixed(1) +
                '" width="' + width.toFixed(1) + '" height="' + Math.max(0, height).toFixed(1) +
                '" rx="' + rx.toFixed(1) + '" fill="' + fill + '" opacity="' + opacity + '"></rect>';
        }

        /* The exact figures behind a group that is only ever drawn as a proportion. */
        function pointTooltip(point) {
            var lines = [formatBucket(point.BucketStart, true)];

            lines.push(label('VAS_236_Receipts', 'Receipts') + ': ' +
                amountText(point.Receipts) + countSuffix(point.ReceiptCount));
            lines.push(label('VAS_236_Payments', 'Payments') + ': ' +
                amountText(point.Payments) + countSuffix(point.PaymentCount));
            lines.push(label('VAS_236_Net', 'Net') + ': ' + signedAmountText(point.Net));

            return lines.join('\n');
        }

        /* The axis thins itself rather than overlapping: at 90 points every label cannot
           fit, so every Nth is drawn and the rest are spacers that keep the alignment. */
        function paintAxis(n) {
            var step = Math.ceil(n / AXIS_MAX_LABELS);
            var html = '';

            for (var i = 0; i < n; i++) {
                var show = (i % step === 0) || (i === n - 1);
                html += '<span class="vas-236-tick">' +
                    (show ? escapeHtml(formatBucket(_points[i].BucketStart, false)) : '') +
                '</span>';
            }

            $axis.html(html);
        }

        /* ------------------------------------------------------------ */
        /* Grain / range picker - anchored under the pill, on <body>     */
        /* ------------------------------------------------------------ */
        function buildPicker() {
            $picker = $('<div class="vas-236-pp vas-236-hidden" role="listbox"></div>');
            $('body').append($picker);

            $picker.on('click', '.vas-236-pp-opt', function () {
                var $opt = $(this);
                var grain = $opt.attr('data-grain');
                var days = parseInt($opt.attr('data-range'), 10) || 0;

                closePicker();
                if (grain) { selectGrain(grain); }
                else if (days > 0) { selectRange(days); }
            });
        }

        function fillPicker() {
            var html = '<div class="vas-236-pp-h">' +
                escapeHtml(label('VAS_236_Grain', 'Grain')) + '</div>';

            for (var i = 0; i < GRAINS.length; i++) {
                html += optionHtml('data-grain="' + GRAINS[i].key + '"',
                    label(GRAINS[i].msg, GRAINS[i].text), GRAINS[i].key === _grain);
            }

            html += '<div class="vas-236-pp-sep"></div>' +
                '<div class="vas-236-pp-h">' + escapeHtml(label('VAS_236_Range', 'Range')) + '</div>';

            for (var r = 0; r < RANGES.length; r++) {
                html += optionHtml('data-range="' + RANGES[r].days + '"',
                    label(RANGES[r].msg, RANGES[r].text), RANGES[r].days === _range);
            }

            $picker.html(html);
        }

        function optionHtml(attr, text, selected) {
            return '<button type="button" class="vas-236-pp-opt" role="option" ' + attr +
                    ' aria-selected="' + (selected ? 'true' : 'false') + '">' +
                '<span class="vas-236-pp-name">' + escapeHtml(text) + '</span>' +
                '<span class="vas-236-pp-tick">' + ICONS.tick + '</span>' +
            '</button>';
        }

        /* The panel is fixed and lives on <body>, so it only stays glued to the pill if
           something re-anchors it. The dashboard scrolls in its own container, not the
           window, and scroll events do not bubble - a CAPTURE listener on document is the
           only one that sees every scroll. Scrolling is not a dismissal: the panel travels
           with the pill and closes only on a pick, an outside click or Escape. */
        var _pickerW = 0;
        var _pickerH = 0;

        function measurePicker() {
            $picker.css('max-height', '');
            _pickerW = $picker.outerWidth();
            _pickerH = $picker.outerHeight();
        }

        function positionPicker() {
            if (!$picker || !$periodBtn || !$periodBtn[0]) { return; }

            var rect = $periodBtn[0].getBoundingClientRect();
            var gap = 6;
            var edge = 8;

            var roomBelow = window.innerHeight - rect.bottom - gap - edge;
            var roomAbove = rect.top - gap - edge;

            var below = _pickerH <= roomBelow || roomBelow >= roomAbove;
            var room = below ? roomBelow : roomAbove;

            var ph = _pickerH;
            if (ph > room) {
                ph = Math.max(160, room);
                $picker.css('max-height', ph + 'px');
            } else {
                $picker.css('max-height', '');
            }

            var top = below ? rect.bottom + gap : rect.top - ph - gap;
            /* Right-aligned to the pill: it sits at the card's trailing edge, so a
               left-aligned panel would hang off the dashboard. */
            var left = Math.min(rect.right - _pickerW, window.innerWidth - _pickerW - edge);
            left = Math.max(edge, left);

            $picker.css({ left: Math.round(left) + 'px', top: Math.round(top) + 'px' });
        }

        function onAnchorScroll() {
            if (_pickerOpen) { positionPicker(); }
        }

        function openPicker() {
            if (!$picker) { buildPicker(); }

            fillPicker();
            $picker.removeClass('vas-236-hidden');
            _pickerOpen = true;
            measurePicker();

            $(document).on('click' + _ns, onDocumentClick);
            $(document).on('keydown' + _ns, onPickerKeyDown);
            $(window).on('resize' + _ns, positionPicker);
            document.addEventListener('scroll', onAnchorScroll, true);

            positionPicker();
        }

        function closePicker() {
            if (!_pickerOpen) { return; }
            _pickerOpen = false;
            if ($picker) { $picker.addClass('vas-236-hidden'); }

            $(document).off('click' + _ns);
            $(document).off('keydown' + _ns);
            $(window).off('resize' + _ns);
            document.removeEventListener('scroll', onAnchorScroll, true);
        }

        function togglePicker() {
            if (_pickerOpen) { closePicker(); } else { openPicker(); }
        }

        function onDocumentClick(e) {
            if (!$picker) { return; }
            if ($picker[0].contains(e.target)) { return; }
            if ($periodBtn[0] && $periodBtn[0].contains(e.target)) { return; }
            closePicker();
        }

        function onPickerKeyDown(e) {
            if (e.key === 'Escape' || e.keyCode === 27) { closePicker(); }
        }

        /* Both re-read: unlike a display toggle, grain and range change WHICH rows are
           aggregated, so the figures have to come from the server. */
        function selectGrain(key) {
            if (!key || key === _grain) { return; }
            _grain = key;
            paintPeriodLabel();
            loadData();
        }

        function selectRange(days) {
            if (!days || days === _range) { return; }
            _range = days;
            paintPeriodLabel();
            loadData();
        }

        /* ------------------------------------------------------------ */
        /* Formatting                                                   */
        /* ------------------------------------------------------------ */
        function symbol() { return (_currency && _currency.Symbol) ? _currency.Symbol : ''; }
        function iso() { return (_currency && _currency.Iso) ? _currency.Iso : ''; }
        function precision() {
            var p = _currency ? Number(_currency.Precision) : NaN;
            return (isNaN(p) || p < 0 || p > 6) ? 2 : p;
        }

        function money(value) {
            return symbol() + compact(value);
        }

        function signedMoney(value) {
            var v = Number(value) || 0;
            /* An explicit sign on the net - the sign IS the reading, never left to colour
               alone. A true minus sign, not a hyphen. */
            return (v < 0 ? '−' : '+') + symbol() + compact(v);
        }

        function compact(value) {
            try {
                if (VIS.Util && typeof VIS.Util.formatCompactAmount === 'function') {
                    return VIS.Util.formatCompactAmount(value, iso(), precision());
                }
            }
            catch (e) { if (window.console) { console.log(e); } }
            return String(Math.abs(Number(value) || 0));
        }

        /* Full, non-compact amount for the tooltips: the exact figure behind a bar that is
           only ever drawn as a proportion. */
        function amountText(value) {
            var abs = Math.abs(Number(value) || 0);
            var p = precision();
            return symbol() + abs.toLocaleString(window.navigator.language,
                { minimumFractionDigits: p, maximumFractionDigits: p });
        }

        function signedAmountText(value) {
            var v = Number(value) || 0;
            return (v < 0 ? '−' : '+') + amountText(v);
        }

        function countSuffix(count) {
            var n = Number(count) || 0;
            return n > 0 ? ' (' + n + ')' : '';
        }

        /* Bucket dates arrive as yyyy-MM-dd and are formatted HERE, in the reader's locale
           and to a length the grain deserves - a daily axis needs only the day number,
           where a monthly one needs the month. Parsed part by part rather than through
           Date(string), which reads a bare ISO date as UTC and shifts it a day back for
           anyone west of Greenwich. */
        function formatBucket(isoDate, full) {
            if (!isoDate) { return ''; }

            var parts = String(isoDate).split('-');
            if (parts.length !== 3) { return String(isoDate); }

            var d = new Date(parseInt(parts[0], 10), parseInt(parts[1], 10) - 1, parseInt(parts[2], 10));
            if (isNaN(d.getTime())) { return String(isoDate); }

            try {
                if (full) {
                    return d.toLocaleDateString(window.navigator.language,
                        { day: '2-digit', month: 'short', year: 'numeric' });
                }
                if (_grain === 'month') {
                    return d.toLocaleDateString(window.navigator.language, { month: 'short' });
                }
                if (_grain === 'week') {
                    return d.toLocaleDateString(window.navigator.language, { day: 'numeric', month: 'short' });
                }
                return String(d.getDate());
            }
            catch (e) { return String(isoDate); }
        }

        /* ------------------------------------------------------------ */
        /* Helpers                                                      */
        /* ------------------------------------------------------------ */

        /* Everything that reaches the DOM - including the SVG <title> text - goes through
           here. An unescaped '<' inside a <title> would close it and inject markup. */
        function escapeHtml(s) {
            var v = (s === null || s === undefined) ? '' : String(s);
            return v.replace(/&/g, '&amp;').replace(/"/g, '&quot;')
                .replace(/'/g, '&#39;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
        }

        /* Every user-facing string goes through AD_Message; the fallback keeps the card
           readable when a key has not been seeded yet. */
        function label(key, fallback) {
            try {
                if (VIS.Msg && typeof VIS.Msg.getMsg === 'function') {
                    var v = VIS.Msg.getMsg(key);
                    if (v && v !== key && v.charAt(0) !== '[') { return v; }
                }
            }
            catch (e) { /* ignore */ }
            return fallback;
        }

        this.getRoot = function () { return $root; };

        /* Release everything that outlives the card: the body-mounted picker, the document
           and window listeners it registers, and both observers - a ResizeObserver left
           running keeps the whole subtree alive. */
        this.releasePanel = function () {
            _disposed = true;
            closePicker();

            if (_rootObserver) {
                try { _rootObserver.disconnect(); } catch (e) { /* ignore */ }
                _rootObserver = null;
            }
            if (_plotObserver) {
                try { _plotObserver.disconnect(); } catch (e) { /* ignore */ }
                _plotObserver = null;
            }

            if ($picker) { $picker.off(); $picker.remove(); $picker = null; }
            if ($busy) { $busy.remove(); $busy = null; }
            if ($periodBtn) { $periodBtn.off(_ns); }

            _points = [];
        };
    };

    /* ---------------------------------------------------------------- */
    /* Required prototype hooks (same surface as other VAS widgets)     */
    /* ---------------------------------------------------------------- */
    VAS.VAS_236_ReceiptvsPaymentWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.widgetInfo = frame.widgetInfo;
        this.windowNo = windowNo;
        this.initalize();
        this.frame.getContentGrid().append(this.getRoot());

        /* Self-wire the dashboard-width CSS variable the header clamps read. */
        ensureDashInlineSizeVar(this.getRoot());

        this.intialLoad();
    };

    /* No prototype refreshWidget: the constructor already defines the instance method,
       which shadows anything on the prototype. A prototype version calling
       this.refreshWidget() would be unreachable at best and infinite recursion the day
       the instance one is removed. */

    VAS.VAS_236_ReceiptvsPaymentWidget.prototype.widgetSizeChange = function (widget) {
        this.widgetInfo = widget;
    };

    VAS.VAS_236_ReceiptvsPaymentWidget.prototype.dispose = function () {
        try { this.releasePanel(); } catch (e) { /* ignore */ }
        if (this.frame && typeof this.frame.dispose === 'function') {
            try { this.frame.dispose(); } catch (e) { /* ignore */ }
        }
        this.frame = null;
        this.windowNo = null;
    };

})(VAS, jQuery);
