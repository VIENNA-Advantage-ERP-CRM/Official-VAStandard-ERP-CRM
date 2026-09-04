/************************************************************
 * Module Name    : VAS
 * Purpose        : Net Movement - a 2x1 KPI card for the Banking dashboard.
 *
 *                  Money IN minus money OUT for ONE accounting period, with the
 *                  two gross figures underneath:
 *
 *                      Net Movement                        [ Jul-2026 v ]
 *                      Net difference between inflows and outflows
 *                      +₹38.6L
 *                      ● ₹1.92Cr in     ● ₹1.54Cr out
 *
 *                  Period chip: EVERY period of the CURRENT fiscal year, newest
 *                  first, presented the way VAS_197 presents its period list (a
 *                  chip naming the selection, a body-anchored popover listing the
 *                  rest). Periods that have not started yet are not offered - the
 *                  server leaves them out, because a future period can only read
 *                  zero. Unlike VAS_197 the list is NOT restricted to open
 *                  periods: a closed month still has a net movement worth reading.
 *
 *                  Amounts arrive already converted into the tenant's base
 *                  (accounting-schema) currency and are formatted through the
 *                  shared VIS.Util.formatCompactAmount helper
 *                  (Scripts/app/util/CurrencyFormat.js), so the compact scale
 *                  follows the base currency - K/L/Cr for Indian-numbering
 *                  currencies, K/M/B otherwise - and the decimals follow its
 *                  StdPrecision. Nothing is parsed or rounded by hand here.
 *
 *                  Tone lives in the NUMBER, never in the card. A negative net
 *                  changes the leading sign and the colour of the value; the
 *                  widget surface stays the standard glass gradient, per
 *                  dashboard-widgets.md §KPI And Summary Widget ("Do not use full
 *                  tinted or semantic gradient backgrounds just to indicate
 *                  success, warning, loss, or status").
 *
 *                  Design: design.md -> dashboard-widgets.md (Glass Widget, Widget
 *                  Header, KPI And Summary Widget, Widget Stat Values, Content Fit
 *                  Budget). Title Roboto Regular on the shared --dash-inline-size
 *                  clamp, KPI value 1.75em SemiBold, meta at the xs token, and the
 *                  whole card sized off the widget root anchor
 *                  clamp(16px, 1.2cqi, 20px). Nothing scrolls inside the cell.
 *
 *                  Summary Message Table
 *                  Rows marked (reuse) already exist under another key and are NOT
 *                  duplicated here.
 *                   # | Current Text                  | Message Key
 *                  ---+-------------------------------+-----------------------------
 *                   1 | Net Movement                  | VAS_231_NetMovement
 *                   2 | Net difference between        | VAS_231_NetMovementHint
 *                     |   inflows and outflows        |
 *                   3 | in                            | VAS_231_In
 *                   4 | out                           | VAS_231_Out
 *                   5 | Receipts                      | VAS_231_Receipts
 *                   6 | Payments                      | VAS_231_Payments
 *                   7 | No accounting period          | VAS_231_NoPeriod
 *                   8 | Dashboard period              | VAS_201_DashboardPeriod (reuse)
 *                   9 | Couldn't load                 | VAS_192_CouldntLoad     (reuse)
 *
 * Chronological development:
 *   VAI154         Created  Date 2026-09-02
 ***********************************************************/
; VAS = window.VAS || {};

; (function (VAS, $) {

    /* CSS lives in VAS/Areas/VAS/Content/VAS_231_NetMovementWidget.css. All classes
       are namespaced `vas-231-` so they never collide with sibling widgets. */

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

    /* Inline SVG, not an icon-font class - the host shell does not always load an
       icon font and a missing glyph leaves an empty box. */
    var ICONS = {
        /* Arrow out of a corner - money moving. */
        movement: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" ' +
            'stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
            '<path d="M7 7h10v10"></path><path d="M7 17 17 7"></path></svg>',
        chevron: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" ' +
            'stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
            '<polyline points="6 9 12 15 18 9"></polyline></svg>',
        tick: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" ' +
            'stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
            '<polyline points="20 6 9 17 4 12"></polyline></svg>'
    };

    VAS.VAS_231_NetMovementWidget = function () {
        this.frame;
        this.windowNo;
        this.widgetInfo;

        var $self = this;
        var $root;
        var $card;
        var $periodBtn;
        var $value;
        var $split;
        var $state;
        var $picker;

        var widgetID = 0;

        /* Unique event namespace per instance - a widget can sit twice on one
           dashboard, and the picker binds document-level handlers. */
        var _ns = '';

        var _periods = [];
        var _periodId = 0;
        var _periodName = '';
        var _currency = null;
        var _data = null;
        var _pickerOpen = false;
        var _disposed = false;
        var _resizeObserver = null;

        /* ------------------------------------------------------------ */
        /* Lifecycle                                                    */
        /* ------------------------------------------------------------ */
        this.initalize = function () {
            widgetID = (VIS.Utility && VIS.Utility.Util
                ? VIS.Utility.Util.getValueOfInt($self.widgetInfo.AD_UserHomeWidgetID)
                : 0);
            if (widgetID === 0) { widgetID = $self.windowNo; }
            _ns = '.vas231_' + widgetID;

            buildSkeleton();
            setupResizeObserver();
        };

        /* Publishes THIS widget's own pixel width as --widget-inline-size on its root,
           which is the first variable the card's font-size clamp reads (the dashboard
           width is only the fallback). Every sibling KPI card does exactly this - it is
           what makes the clamp land on its 16px floor in a 2x1 cell, so the KPI value
           resolves to 28px and the padding to 13.6px here as it does there. Without it
           the card measures itself against the whole dashboard and renders a size
           larger than its neighbours. */
        function setupResizeObserver() {
            if (typeof ResizeObserver === 'undefined') { return; }
            try {
                _resizeObserver = new ResizeObserver(function (entries) {
                    for (var i = 0; i < entries.length; i++) {
                        var width = entries[i].contentRect.width;
                        if (width > 0 && $root[0]) {
                            $root[0].style.setProperty('--widget-inline-size', width + 'px');
                        }
                    }
                });
                _resizeObserver.observe($root[0]);
            } catch (e) { /* the clamp falls back to --dash-inline-size */ }
        }

        this.intialLoad = function () {
            loadBootstrap();
        };

        /* The dashboard's Refresh button calls this. */
        this.refreshWidget = function () {
            loadBootstrap();
        };

        /* ------------------------------------------------------------ */
        /* DOM skeleton                                                 */
        /* ------------------------------------------------------------ */
        function buildSkeleton() {
            $root = $('<div class="vas-231-root" id="vas-231-root-' + widgetID + '"></div>');

            var title = label('VAS_231_NetMovement', 'Net Movement');
            var subtitle = label('VAS_231_NetMovementHint',
                'Net difference between inflows and outflows');

            $card = $(
                '<div class="vas-231-card">' +
                    '<div class="vas-231-header">' +
                        '<span class="vas-231-icon">' + ICONS.movement + '</span>' +
                        '<div class="vas-231-head-text">' +
                            '<div class="vas-231-title"></div>' +
                            '<div class="vas-231-subtitle"></div>' +
                        '</div>' +
                        /* Period pill: the widget's only filter. It names the period every
                           figure on the card belongs to, and opens the picker. */
                        '<button type="button" class="vas-231-periodchip vas-231-hidden" aria-haspopup="listbox">' +
                            '<span class="vas-231-periodchip-label"></span>' +
                            ICONS.chevron +
                        '</button>' +
                    '</div>' +
                    /* Header, value and meta are DIRECT children of the card, exactly
                       as in VAS_220_SchedulesDue: the card itself is the flex column
                       that distributes them (space-between + a gap floor). An inner
                       body wrapper would take the distribution away from the card and
                       is what let the value overflow its cell. */
                    '<div class="vas-231-value"></div>' +
                    '<div class="vas-231-split"></div>' +
                    '<div class="vas-231-state vas-231-hidden"></div>' +
                '</div>'
            );

            $card.find('.vas-231-title').text(title).attr('title', title);
            /* The subtitle truncates to one line in a 2x1 cell, so the full text
               also goes on the title attribute - the explainer is never lost, it
               just moves to the tooltip on a narrow dashboard. */
            $card.find('.vas-231-subtitle').text(subtitle).attr('title', subtitle);

            $periodBtn = $card.find('.vas-231-periodchip');
            $value = $card.find('.vas-231-value');
            $split = $card.find('.vas-231-split');
            $state = $card.find('.vas-231-state');

            $periodBtn.on('click', function (e) {
                e.preventDefault();
                e.stopPropagation();
                togglePicker();
            });

            $root.append($card);
        }

        /* ------------------------------------------------------------ */
        /* Data                                                         */
        /* ------------------------------------------------------------ */
        function loadBootstrap() {
            renderLoading();

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_231_NetMovementWidget/GetBootstrap',
                type: 'GET',
                dataType: 'json',
                success: function (raw) {
                    if (_disposed) { return; }

                    var data = parseResponse(raw);
                    if (!data || data.error) { renderState(label('VAS_192_CouldntLoad', "Couldn't load")); return; }

                    _periods = data.Periods || [];
                    _currency = data.Currency || null;
                    _periodId = data.C_Period_ID || 0;
                    _periodName = data.PeriodName || '';

                    paintPeriod();

                    if (_periods.length === 0 || _periodId <= 0) {
                        renderState(label('VAS_231_NoPeriod', 'No accounting period.'));
                        return;
                    }

                    _data = data.Data || null;
                    renderFigures();
                },
                error: function () {
                    if (_disposed) { return; }
                    renderState(label('VAS_192_CouldntLoad', "Couldn't load"));
                }
            });
        }

        function loadPeriodData(periodId) {
            renderLoading();

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_231_NetMovementWidget/GetPeriodData',
                type: 'GET',
                dataType: 'json',
                data: { periodId: periodId },
                success: function (raw) {
                    if (_disposed) { return; }
                    /* A late response for a period the user has already moved away from
                       must not overwrite what is on screen. */
                    if (periodId !== _periodId) { return; }

                    var data = parseResponse(raw);
                    if (!data || data.error || !data.Loaded) {
                        renderState(label('VAS_192_CouldntLoad', "Couldn't load"));
                        return;
                    }

                    _data = data;
                    renderFigures();
                },
                error: function () {
                    if (_disposed || periodId !== _periodId) { return; }
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
        /* Render                                                       */
        /* ------------------------------------------------------------ */
        function renderLoading() {
            $value.addClass('vas-231-is-loading');
        }

        /* A load failure takes the card over; an empty period does NOT - zero movement
           is a real answer and renders as a zero, per the KPI empty-state rule. */
        function renderState(text) {
            $value.removeClass('vas-231-is-loading').addClass('vas-231-hidden');
            $split.addClass('vas-231-hidden');
            $state.removeClass('vas-231-hidden').text(text);
        }

        function renderFigures() {
            $state.addClass('vas-231-hidden');
            $value.removeClass('vas-231-is-loading vas-231-hidden');
            $split.removeClass('vas-231-hidden');

            var net = _data ? Number(_data.NetMovement) || 0 : 0;
            var inAmt = _data ? Number(_data.ReceiptsAmt) || 0 : 0;
            var outAmt = _data ? Number(_data.PaymentsAmt) || 0 : 0;
            var negative = net < 0;

            /* The tone lives in the VALUE, not in the card. dashboard-widgets.md
               §KPI And Summary Widget forbids a tinted or semantic gradient surface
               used to signal status - the widget surface stays visually consistent and
               the meaning comes from the text, so all a negative net changes is the
               leading sign and the colour of the number. */
            var sign = negative ? '-' : '+';
            $value
                .toggleClass('vas-231-value--neg', negative)
                .attr('title', label('VAS_231_NetMovement', 'Net Movement') + ': ' + sign + amountText(net))
                .html(
                    '<span class="vas-231-cur">' + escapeHtml(sign + symbol()) + '</span>' +
                    escapeHtml(compact(net))
                );

            var inLabel = label('VAS_231_In', 'in');
            var outLabel = label('VAS_231_Out', 'out');

            $split.html(
                '<span class="vas-231-pos" title="' +
                    escapeHtml(label('VAS_231_Receipts', 'Receipts') + ': ' + amountText(inAmt) +
                        countSuffix(_data ? _data.ReceiptsCount : 0)) + '">' +
                    '<span class="vas-231-dot vas-231-dot--pos"></span>' +
                    escapeHtml(symbol() + compact(inAmt)) +
                    '<span class="vas-231-lbl">' + escapeHtml(inLabel) + '</span>' +
                '</span>' +
                '<span class="vas-231-neg" title="' +
                    escapeHtml(label('VAS_231_Payments', 'Payments') + ': ' + amountText(outAmt) +
                        countSuffix(_data ? _data.PaymentsCount : 0)) + '">' +
                    '<span class="vas-231-dot vas-231-dot--neg"></span>' +
                    escapeHtml(symbol() + compact(outAmt)) +
                    '<span class="vas-231-lbl">' + escapeHtml(outLabel) + '</span>' +
                '</span>'
            );
        }

        function paintPeriod() {
            var text = _periodName || '—';
            $periodBtn.find('.vas-231-periodchip-label').text(text);
            $periodBtn.attr('title', text);
            /* Nothing to pick when the year has no started period at all. */
            $periodBtn.toggleClass('vas-231-hidden', _periods.length === 0);
        }

        /* ------------------------------------------------------------ */
        /* Amount formatting - shared helper only                       */
        /* ------------------------------------------------------------ */
        function symbol() { return (_currency && _currency.Symbol) ? _currency.Symbol : ''; }
        function iso() { return (_currency && _currency.Iso) ? _currency.Iso : ''; }
        function precision() {
            var p = _currency ? Number(_currency.Precision) : NaN;
            return (isNaN(p) || p < 0 || p > 6) ? 2 : p;
        }

        /* Compact magnitude (K / L / Cr, or K / M / B) from the shared util. It returns
           the magnitude only - the sign and the symbol are this widget's own
           composition. */
        function compact(value) {
            try {
                if (VIS.Util && typeof VIS.Util.formatCompactAmount === 'function') {
                    return VIS.Util.formatCompactAmount(value, iso(), precision());
                }
            }
            catch (e) { if (window.console) { console.log(e); } }
            return String(Math.abs(Number(value) || 0));
        }

        /* Full, non-compact amount for the tooltips: the exact figure behind the
           rounded headline. Grouping and the decimal separator come from the browser
           locale, the decimals from the base currency's precision. */
        function amountText(value) {
            var abs = Math.abs(Number(value) || 0);
            var p = precision();
            var text = abs.toLocaleString(window.navigator.language,
                { minimumFractionDigits: p, maximumFractionDigits: p });
            return (Number(value) < 0 ? '-' : '') + symbol() + text;
        }

        function countSuffix(count) {
            var n = Number(count) || 0;
            return n > 0 ? ' (' + n + ')' : '';
        }

        /* ------------------------------------------------------------ */
        /* Period picker - anchored under the chip, appended to <body>   */
        /* ------------------------------------------------------------ */
        function buildPicker() {
            $picker = $('<div class="vas-231-pp vas-231-hidden" role="listbox" aria-label="' +
                escapeHtml(label('VAS_201_DashboardPeriod', 'Dashboard period')) + '"></div>');
            $('body').append($picker);

            $picker.on('click', '.vas-231-pp-opt', function () {
                var id = parseInt($(this).attr('data-id'), 10) || 0;
                closePicker();
                selectPeriod(id);
            });
        }

        function fillPicker() {
            var html = '<div class="vas-231-pp-h">' +
                escapeHtml(label('VAS_201_DashboardPeriod', 'Dashboard period')) + '</div>';

            for (var i = 0; i < _periods.length; i++) {
                var p = _periods[i];
                var selected = p.C_Period_ID === _periodId;
                var meta = p.FiscalYear ? String(p.FiscalYear) : '';

                html += '<button type="button" class="vas-231-pp-opt" role="option" data-id="' + p.C_Period_ID +
                        '" aria-selected="' + (selected ? 'true' : 'false') + '">' +
                    '<span class="vas-231-pp-name" title="' + escapeHtml(p.Name || '') + '">' +
                        escapeHtml(p.Name || '') + '</span>' +
                    (meta ? '<span class="vas-231-pp-meta">' + escapeHtml(meta) + '</span>' : '') +
                    '<span class="vas-231-pp-tick">' + ICONS.tick + '</span>' +
                '</button>';
            }

            $picker.html(html);
        }

        /* The panel is fixed and lives on <body>, so it only stays glued to the chip if
           something re-anchors it. The dashboard scrolls in its own container, not the
           window, and scroll events do not bubble - a CAPTURE listener on document is
           the only one that sees every scroll, whichever container moved. Scrolling is
           not a dismissal: the panel travels with the chip and closes only on a pick, an
           outside click or Escape. Size is measured once at opening; it cannot change
           while the user scrolls. */
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

            /* Hangs below the chip by default and flips above only when the list plainly
               fits better there. Where room is short it is capped and scrolls inside
               itself rather than being pushed off its anchor. */
            var below = _pickerH <= roomBelow || roomBelow >= roomAbove;
            var room = below ? roomBelow : roomAbove;

            var ph = _pickerH;
            if (ph > room) {
                ph = Math.max(140, room);
                $picker.css('max-height', ph + 'px');
            } else {
                $picker.css('max-height', '');
            }

            var top = below ? rect.bottom + gap : rect.top - ph - gap;
            var left = Math.min(rect.left, window.innerWidth - _pickerW - edge);
            left = Math.max(edge, left);

            $picker.css({ left: Math.round(left) + 'px', top: Math.round(top) + 'px' });
        }

        function onAnchorScroll() {
            if (_pickerOpen) { positionPicker(); }
        }

        function openPicker() {
            if (_periods.length === 0) { return; }
            if (!$picker) { buildPicker(); }

            fillPicker();
            $picker.removeClass('vas-231-hidden');
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
            if ($picker) { $picker.addClass('vas-231-hidden'); }

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

        function selectPeriod(periodId) {
            if (periodId <= 0 || periodId === _periodId) { return; }

            _periodId = periodId;
            _periodName = '';
            for (var i = 0; i < _periods.length; i++) {
                if (_periods[i].C_Period_ID === periodId) { _periodName = _periods[i].Name || ''; break; }
            }

            paintPeriod();
            loadPeriodData(periodId);
        }

        /* ------------------------------------------------------------ */
        /* Helpers                                                      */
        /* ------------------------------------------------------------ */
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

        /* Release everything that outlives the card: the body-mounted picker and the
           document / window listeners it registers. */
        this.releasePanel = function () {
            _disposed = true;
            closePicker();
            if (_resizeObserver) {
                try { _resizeObserver.disconnect(); } catch (e) { /* ignore */ }
                _resizeObserver = null;
            }
            if ($picker) { $picker.off(); $picker.remove(); $picker = null; }
            if ($periodBtn) { $periodBtn.off(); }
        };
    };

    /* ---------------------------------------------------------------- */
    /* Required prototype hooks (same surface as other VAS widgets)     */
    /* ---------------------------------------------------------------- */
    VAS.VAS_231_NetMovementWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.widgetInfo = frame.widgetInfo;
        this.windowNo = windowNo;
        this.initalize();
        this.frame.getContentGrid().append(this.getRoot());

        /* Self-wire the dashboard-width CSS variable the header clamps read. */
        ensureDashInlineSizeVar(this.getRoot());

        this.intialLoad();
    };

    /* No prototype refreshWidget: the constructor already defines the instance
       method, which shadows anything on the prototype. A prototype version calling
       this.refreshWidget() would be unreachable at best and infinite recursion the
       day the instance one is removed - VAS_197 defines the instance method only,
       and so does this card. */

    VAS.VAS_231_NetMovementWidget.prototype.widgetSizeChange = function (widget) {
        this.widgetInfo = widget;
    };

    VAS.VAS_231_NetMovementWidget.prototype.dispose = function () {
        try { this.releasePanel(); } catch (e) { /* ignore */ }
        if (this.frame && typeof this.frame.dispose === 'function') {
            try { this.frame.dispose(); } catch (e) { /* ignore */ }
        }
        this.frame = null;
        this.windowNo = null;
    };

})(VAS, jQuery);
