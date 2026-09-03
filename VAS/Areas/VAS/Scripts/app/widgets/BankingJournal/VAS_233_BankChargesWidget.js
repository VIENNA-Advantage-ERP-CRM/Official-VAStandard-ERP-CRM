/************************************************************
 * Module Name    : VAS
 * Purpose        : Bank Charges - a 2x1 KPI card for the Banking dashboard.
 *
 *                  What the bank cost the tenant in ONE accounting period, with
 *                  the movement against the preceding period and the number of
 *                  entries behind the figure:
 *
 *                      Bank Charges                        [ Jul-2026 v ]
 *                      Charges posted through banking journals
 *                      ₹84,200
 *                      ▲ 12%   23 charge entries
 *
 *                  Period chip: EVERY period of the CURRENT fiscal year, newest
 *                  first, exactly as VAS_231_NetMovementWidget presents it (a chip
 *                  naming the selection, a body-anchored popover listing the rest).
 *                  Periods that have not started yet are not offered - the server
 *                  leaves them out, because a future period can only read zero. The
 *                  list is NOT restricted to open periods: a closed month still has
 *                  charges worth reading. The two cards share a dashboard, so they
 *                  deliberately share the same period contract.
 *
 *                  Amounts arrive already converted into the tenant's base
 *                  (accounting-schema) currency and are formatted through the
 *                  shared VIS.Util.formatCompactAmount helper
 *                  (Scripts/app/util/CurrencyFormat.js), so the compact scale
 *                  follows the base currency - K/L/Cr for Indian-numbering
 *                  currencies, K/M/B otherwise - and the decimals follow its
 *                  StdPrecision. Nothing is parsed or rounded by hand here.
 *
 *                  Tone lives in the NUMBER, never in the card. Bank charges are a
 *                  cost, so the value and the delta both take the warning-deep
 *                  amber (#9A6500) - amber reads as a cost without the alarm of red,
 *                  and the delta does NOT turn success-green when charges fall,
 *                  because a bank-charge delta is not good/bad news, it is just a
 *                  direction. The widget surface stays the standard glass gradient,
 *                  per dashboard-widgets.md §KPI And Summary Widget ("Do not use
 *                  full tinted or semantic gradient backgrounds just to indicate
 *                  success, warning, loss, or status") - which is why this card does
 *                  NOT carry the preview mock's amber `tint-warning` surface.
 *
 *                  Design: design.md -> dashboard-widgets.md (Glass Widget, Widget
 *                  Header, KPI And Summary Widget, Widget Stat Values, Content Fit
 *                  Budget). Title Roboto Medium on the shared --dash-inline-size
 *                  clamp, KPI value 1.75em SemiBold, meta at the xs token, and the
 *                  whole card sized off the widget root anchor
 *                  clamp(16px, 1.2cqi, 20px). Nothing scrolls inside the cell.
 *
 *                  Summary Message Table
 *                  Rows marked (reuse) already exist under another key and are NOT
 *                  duplicated here.
 *                   # | Current Text                  | Message Key
 *                  ---+-------------------------------+-----------------------------
 *                   1 | Bank Charges                  | VAS_233_BankCharges
 *                   2 | Charges through               | VAS_233_BankChargesHint
 *                     |   banking journals            |
 *                   3 | charge entries                | VAS_233_ChargeEntries
 *                   4 | Charge payments               | VAS_233_ChargePayments
 *                   5 | Statement charges             | VAS_233_StatementCharges
 *                   6 | vs                            | VAS_233_Vs
 *                   7 | No accounting period          | VAS_231_NoPeriod        (reuse)
 *                   8 | Dashboard period              | VAS_201_DashboardPeriod (reuse)
 *                   9 | Couldn't load                 | VAS_192_CouldntLoad     (reuse)
 *
 * Chronological development:
 *   VAI154         Created  Date 2026-09-03
 ***********************************************************/
; VAS = window.VAS || {};

; (function (VAS, $) {

    /* CSS lives in VAS/Areas/VAS/Content/VAS_233_BankChargesWidget.css. All classes
       are namespaced `vas-233-` so they never collide with sibling widgets. */

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
        /* Coin with a currency stroke - a fee taken off the account. */
        charge: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" ' +
            'stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
            '<circle cx="12" cy="12" r="9"></circle>' +
            '<path d="M16 8h-5a2 2 0 0 0 0 4h2a2 2 0 0 1 0 4H8"></path>' +
            '<line x1="12" y1="6" x2="12" y2="8"></line>' +
            '<line x1="12" y1="16" x2="12" y2="18"></line></svg>',
        chevron: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" ' +
            'stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
            '<polyline points="6 9 12 15 18 9"></polyline></svg>',
        tick: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" ' +
            'stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
            '<polyline points="20 6 9 17 4 12"></polyline></svg>',
        up: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" ' +
            'stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
            '<polyline points="6 14 12 8 18 14"></polyline></svg>',
        down: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" ' +
            'stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
            '<polyline points="6 10 12 16 18 10"></polyline></svg>'
    };

    VAS.VAS_233_BankChargesWidget = function () {
        this.frame;
        this.windowNo;
        this.widgetInfo;

        var $self = this;
        var $root;
        var $card;
        var $periodBtn;
        var $value;
        var $foot;
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
            _ns = '.vas233_' + widgetID;

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
            $root = $('<div class="vas-233-root" id="vas-233-root-' + widgetID + '"></div>');

            var title = label('VAS_233_BankCharges', 'Bank Charges');
            var subtitle = label('VAS_233_BankChargesHint',
                'Charges through banking journals');

            $card = $(
                '<div class="vas-233-card">' +
                    '<div class="vas-233-header">' +
                        '<span class="vas-233-icon">' + ICONS.charge + '</span>' +
                        '<div class="vas-233-head-text">' +
                            '<div class="vas-233-title"></div>' +
                            '<div class="vas-233-subtitle"></div>' +
                        '</div>' +
                        /* Period pill: the widget's only filter. It names the period every
                           figure on the card belongs to, and opens the picker. */
                        '<button type="button" class="vas-233-periodchip vas-233-hidden" aria-haspopup="listbox">' +
                            '<span class="vas-233-periodchip-label"></span>' +
                            ICONS.chevron +
                        '</button>' +
                    '</div>' +
                    /* Header, value and meta are DIRECT children of the card, exactly
                       as in VAS_231_NetMovementWidget: the card itself is the flex column
                       that distributes them (space-between + a gap floor). An inner
                       body wrapper would take the distribution away from the card and
                       is what let the value overflow its cell. */
                    '<div class="vas-233-value"></div>' +
                    '<div class="vas-233-foot"></div>' +
                    '<div class="vas-233-state vas-233-hidden"></div>' +
                '</div>'
            );

            $card.find('.vas-233-title').text(title).attr('title', title);
            /* The subtitle truncates to one line in a 2x1 cell, so the full text
               also goes on the title attribute - the explainer is never lost, it
               just moves to the tooltip on a narrow dashboard. */
            $card.find('.vas-233-subtitle').text(subtitle).attr('title', subtitle);

            $periodBtn = $card.find('.vas-233-periodchip');
            $value = $card.find('.vas-233-value');
            $foot = $card.find('.vas-233-foot');
            $state = $card.find('.vas-233-state');

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
                url: VIS.Application.contextUrl + 'VAS_233_BankChargesWidget/GetBootstrap',
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
                url: VIS.Application.contextUrl + 'VAS_233_BankChargesWidget/GetPeriodData',
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
            $value.addClass('vas-233-is-loading');
        }

        /* A load failure takes the card over; an empty period does NOT - "the bank cost
           you nothing" is a real answer and renders as a zero, per the KPI empty-state
           rule. */
        function renderState(text) {
            $value.removeClass('vas-233-is-loading').addClass('vas-233-hidden');
            $foot.addClass('vas-233-hidden');
            $state.removeClass('vas-233-hidden').text(text);
        }

        function renderFigures() {
            $state.addClass('vas-233-hidden');
            $value.removeClass('vas-233-is-loading vas-233-hidden');
            $foot.removeClass('vas-233-hidden');

            var amount = _data ? Number(_data.ChargesAmt) || 0 : 0;
            var count = _data ? Number(_data.ChargeCount) || 0 : 0;

            /* The value tooltip carries the exact figure AND the split between the two
               places a charge can be recorded, so the operator can tell a payment-screen
               charge from a fee the bank took on the statement without leaving the card. */
            $value
                .attr('title', valueTooltip(amount))
                .html(
                    '<span class="vas-233-cur">' + escapeHtml(symbol()) + '</span>' +
                    escapeHtml(compact(amount))
                );

            $foot.html(deltaHtml() + countHtml(count));
        }

        /* Delta badge: direction from the sign, colour ALWAYS warning-deep amber.
           Rising charges are not "bad news" in the danger sense and falling charges are
           not a success - the metric is a cost, so both directions read in the cost
           colour and only the arrow changes. Hidden entirely when the preceding period
           had no charges: a percentage against zero is not a number. */
        function deltaHtml() {
            if (!_data || !_data.HasDelta) { return ''; }

            var pct = Number(_data.DeltaPct) || 0;
            var up = pct >= 0;
            var text = Math.abs(pct).toFixed(pct !== 0 && Math.abs(pct) < 10 ? 1 : 0) + '%';

            var priorName = _data.PriorPeriodName || '';
            var tip = label('VAS_233_Vs', 'vs') + ' ' +
                (priorName ? priorName + ': ' : '') + amountText(Number(_data.PriorChargesAmt) || 0);

            return '<span class="vas-233-delta" title="' + escapeHtml(tip) + '">' +
                (up ? ICONS.up : ICONS.down) +
                escapeHtml(text) +
            '</span>';
        }

        /* "23 charge entries" - the number of documents behind the headline. The count is
           always shown, zero included, so the card never loses its second line. */
        function countHtml(count) {
            return '<span class="vas-233-count">' +
                escapeHtml(String(count) + ' ' + label('VAS_233_ChargeEntries', 'charge entries')) +
            '</span>';
        }

        function valueTooltip(amount) {
            var head = label('VAS_233_BankCharges', 'Bank Charges') + ': ' + amountText(amount);
            if (!_data) { return head; }

            return head +
                '\n' + label('VAS_233_ChargePayments', 'Charge payments') + ': ' +
                    amountText(Number(_data.PaymentChargesAmt) || 0) +
                    countSuffix(_data.PaymentChargeCount) +
                '\n' + label('VAS_233_StatementCharges', 'Statement charges') + ': ' +
                    amountText(Number(_data.StatementChargesAmt) || 0) +
                    countSuffix(_data.StatementChargeCount);
        }

        function paintPeriod() {
            var text = _periodName || '—';
            $periodBtn.find('.vas-233-periodchip-label').text(text);
            $periodBtn.attr('title', text);
            /* Nothing to pick when the year has no started period at all. */
            $periodBtn.toggleClass('vas-233-hidden', _periods.length === 0);
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
           the magnitude only - the symbol is this widget's own composition. */
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
            $picker = $('<div class="vas-233-pp vas-233-hidden" role="listbox" aria-label="' +
                escapeHtml(label('VAS_201_DashboardPeriod', 'Dashboard period')) + '"></div>');
            $('body').append($picker);

            $picker.on('click', '.vas-233-pp-opt', function () {
                var id = parseInt($(this).attr('data-id'), 10) || 0;
                closePicker();
                selectPeriod(id);
            });
        }

        function fillPicker() {
            var html = '<div class="vas-233-pp-h">' +
                escapeHtml(label('VAS_201_DashboardPeriod', 'Dashboard period')) + '</div>';

            for (var i = 0; i < _periods.length; i++) {
                var p = _periods[i];
                var selected = p.C_Period_ID === _periodId;
                var meta = p.FiscalYear ? String(p.FiscalYear) : '';

                html += '<button type="button" class="vas-233-pp-opt" role="option" data-id="' + p.C_Period_ID +
                        '" aria-selected="' + (selected ? 'true' : 'false') + '">' +
                    '<span class="vas-233-pp-name" title="' + escapeHtml(p.Name || '') + '">' +
                        escapeHtml(p.Name || '') + '</span>' +
                    (meta ? '<span class="vas-233-pp-meta">' + escapeHtml(meta) + '</span>' : '') +
                    '<span class="vas-233-pp-tick">' + ICONS.tick + '</span>' +
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
            $picker.removeClass('vas-233-hidden');
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
            if ($picker) { $picker.addClass('vas-233-hidden'); }

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
    VAS.VAS_233_BankChargesWidget.prototype.init = function (windowNo, frame) {
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
       day the instance one is removed - VAS_231 defines the instance method only,
       and so does this card. */

    VAS.VAS_233_BankChargesWidget.prototype.widgetSizeChange = function (widget) {
        this.widgetInfo = widget;
    };

    VAS.VAS_233_BankChargesWidget.prototype.dispose = function () {
        try { this.releasePanel(); } catch (e) { /* ignore */ }
        if (this.frame && typeof this.frame.dispose === 'function') {
            try { this.frame.dispose(); } catch (e) { /* ignore */ }
        }
        this.frame = null;
        this.windowNo = null;
    };

})(VAS, jQuery);
