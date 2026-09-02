/**
 * VAS_201_UnMatchedGRNInvoiceWidget
 * 3x2 grid widget for the Period Control dashboard.
 *
 * The three-way-match exceptions of ONE open accounting period, each with the
 * number of documents behind it and their value in the tenant's base currency:
 *
 *   Exception                 Count   Value
 *   --------------------------------------------
 *   GRNs not invoiced            12   54,300
 *   Invoices without GRN          7   29,800
 *   Qty / price mismatches        4    8,150
 *
 * Clicking a row opens a paged record list; each list has its own columns, and
 * the document number is a link that opens that receipt or invoice in its own
 * standard window (a mismatch row links to both sides).
 *
 * The card values are base currency throughout, so rows in different currencies
 * can be added together. The mismatch LIST is the exception: its prices and its
 * amount difference are shown in the invoice's own currency, symbol included -
 * those are figures the reader is meant to check against the document in hand,
 * and a converted price would not match anything printed on it.
 *
 * Period source: the OPEN periods of the tenant's primary calendar - a period
 * qualifies when at least one active C_PeriodControl row of it is Open. Nothing
 * is derived from the calendar month. GRN exceptions are bounded by
 * M_InOut.DateAcct, invoice exceptions by C_Invoice.DateAcct.
 *
 * The footer deliberately does NOT add the three counts up: the mismatch
 * category overlaps the other two document populations, so a total would be
 * misleading. It says what to do instead.
 *
 * Sizing follows design.md -> dashboard-widgets.md: the card carries the widget
 * root anchor clamp, the header reads --dash-inline-size (populated by
 * ensureDashInlineSizeVar), the exception grid shares one column template between
 * its header and its rows, and nothing scrolls. Narrow cells are handled by
 * container queries, the body-level modal and picker by media queries (see the
 * stylesheet).
 *
 * Summary Message Table
 * Rows marked (reuse) already exist in the project under another key and are
 * NOT duplicated here. Column captions prefer the framework's own translated
 * element name (VIS.translatedTexts[<ColumnName>]) and only fall back to the key.
 *  # | Current Text                             | Message Key
 * ---+------------------------------------------+---------------------------------
 *  1 | Unmatched GRNs & Invoices                | VAS_201_UnmatchedGRNInvoices
 *  2 | 3-way match · click for records          | VAS_201_ThreeWayMatchHint
 *  3 | Exception                                | VAS_201_Exception
 *  4 | Count                                    | VAS_201_Count
 *  5 | Value                                    | VAS_201_Value
 *  6 | GRNs not invoiced                        | VAS_201_GRNsNotInvoiced
 *  7 | Invoices without GRN                     | VAS_201_InvoicesWithoutGRN
 *  8 | Qty / Price mismatches                   | VAS_201_QtyPriceMismatches
 *  9 | Click an exception to review matching details | VAS_201_FooterHint
 * 10 | No open accounting period                | VAS_201_NoOpenPeriod
 * 11 | Dashboard period                         | VAS_201_DashboardPeriod
 * 12 | No records in this category              | VAS_201_NoRecords
 * 13 | Receipt Date                             | VAS_201_ReceiptDate
 * 14 | Lines                                    | VAS_201_Lines
 * 15 | Converted Amount                         | VAS_201_ConvertedAmount
 * 16 | Amount in Base Currency        (tooltip) | VAS_201_AmountInBaseCurrency
 * 17 | Invoice Amount                           | VAS_201_InvoiceAmount
 * 18 | GRN                                      | VAS_201_GRN
 * 19 | Invoice                                  | VAS_201_Invoice
 * 20 | Product                                  | VAS_201_Product
 * 21 | Qty Variance                             | VAS_201_QtyVariance
 * 22 | Price Variance                           | VAS_201_PriceVariance
 * 23 | Amount Difference                        | VAS_201_AmountDifference
 * 24 | GRN Qty                        (tooltip) | VAS_201_GrnQty
 * 25 | Invoice Qty                    (tooltip) | VAS_201_InvoiceQty
 * 26 | Order Price                    (tooltip) | VAS_201_OrderPrice
 * 27 | Invoice Price                  (tooltip) | VAS_201_InvoicePrice
 * 28 | Document No                              | DocumentNo                (reuse)
 * 29 | Invoice Date                             | DateInvoiced              (reuse)
 * 30 | Business Partner                         | C_BPartner_ID             (reuse)
 * 31 | Currency                                 | C_Currency_ID             (reuse)
 * 32 | Close                                    | VAS_018_Close             (reuse)
 * 33 | Couldn't load                            | VAS_192_CouldntLoad       (reuse)
 * 34 | Showing                                  | VAS_026_Showing           (reuse)
 * 35 | of                                       | VAS_026_Of                (reuse)
 * 36 | Previous                                 | VAS_026_Prev              (reuse)
 * 37 | Next                                     | VAS_026_Next              (reuse)
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    /* Keep --dash-inline-size on :root equal to the dashboard container's current
       pixel width so the header clamps resolve against the dashboard's visible
       width, not the viewport. One document-level observer serves every widget. */
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

    /* The three exceptions, in display order. `code` is the server-side category
       token and `tone` the colour of its count. Kept in lock-step with
       VASLogic.Models.VAS_201_UnMatchedGRNInvoiceModel. */
    var EXCEPTIONS = [
        { code: 'GRNNOTINV', key: 'VAS_201_GRNsNotInvoiced', text: 'GRNs not invoiced', tone: 'warn' },
        { code: 'INVNOGRN', key: 'VAS_201_InvoicesWithoutGRN', text: 'Invoices without GRN', tone: 'warn' },
        { code: 'MISMATCH', key: 'VAS_201_QtyPriceMismatches', text: 'Qty / Price mismatches', tone: 'fail' }
    ];

    /* Standard windows the document links open. Resolved by NAME at runtime by
       VAS.ZoomUtil - an AD_Window_ID differs per environment and is never
       hard-coded. */
    var ZOOM_WINDOW_RECEIPT = 'VAS_MaterialReceipt';
    var ZOOM_WINDOW_RECEIPT_OLD = 'Material Receipt';
    var ZOOM_WINDOW_INVOICE = 'VAS_APInvoice';
    var ZOOM_WINDOW_INVOICE_OLD = 'Invoice (Vendor)';

    var MODAL_PAGE_SIZE = 8;

    /* Starting estimate for the detail grid's row / header heights, in px. It only
       has to hold until the first page is painted: the real heights are then
       measured and the body grown to fit a full page exactly, because the painted
       height depends on the host's font scale. */
    var MODAL_ROW_H = 58;
    var MODAL_HEAD_H = 52;
    var MODAL_BODY_SLACK = 4;

    VAS.VAS_201_UnMatchedGRNInvoiceWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas-201-root">');
        var $card;
        var $list;
        var $foot;
        var $periodBtn;
        var $busy;

        /* Both overlays live on <body>, so they are NOT inside $root - always
           address them through these references, never through $root.find. The card
           clips its own overflow, so an in-card popover would be cut off. */
        var $picker = null;
        var $overlay = null;
        var $modalBody = null;
        var $modalPager = null;
        var $modalBusy = null;

        /* Per-instance event namespace - a dashboard can hold two of this widget,
           and each must unbind only its own document/window handlers. */
        var _ns = '.vas201_' + (VAS.VAS_201_UnMatchedGRNInvoiceWidget._seq =
            (VAS.VAS_201_UnMatchedGRNInvoiceWidget._seq || 0) + 1);

        /* Current selection and the data painted from it. */
        var _periods = [];
        var _periodId = 0;
        var _periodName = '';
        var _data = null;

        /* Detail modal state. _detailSeq drops the response of a page the user has
           already navigated away from (or of a period they have already changed). */
        var _category = '';
        var _page = 1;
        var _detailSeq = 0;
        var _bodyH = 0;
        var _pickerOpen = false;
        var _modalOpen = false;
        var _busyCount = 0;

        // ── Small helpers ────────────────────────────────────────────────────

        function label(key, fallback) {
            var translated = VIS.Msg.getMsg(key);
            return (translated && translated.charAt(0) !== '[' && translated !== key) ? translated : fallback;
        }

        /* Column captions: prefer the framework's own translated element name so a
           column is captioned exactly as it is everywhere else in the product. */
        function colLabel(columnName, key, fallback) {
            if (VIS.translatedTexts && VIS.translatedTexts[columnName]) {
                return VIS.translatedTexts[columnName];
            }
            return label(key, fallback);
        }

        /* A caption that needs a longer gloss than fits in the column. The header
           prints the short form and hovers the long one: "Converted Amount" says
           what the figure is, the tooltip says what it was converted into. */
        function tip(text, title) {
            return { text: text, title: title };
        }

        function captionText(caption) {
            return (caption && caption.text != null) ? caption.text : caption;
        }

        function captionTitle(caption) {
            return (caption && caption.title != null) ? caption.title : captionText(caption);
        }

        function escapeHtml(value) {
            return String(value == null ? "" : value)
                .replace(/&/g, "&amp;")
                .replace(/</g, "&lt;")
                .replace(/>/g, "&gt;")
                .replace(/"/g, "&quot;")
                .replace(/'/g, "&#039;");
        }

        function parseResponse(res) {
            var data = res;
            if (typeof data === 'string') { data = JSON.parse(data); }
            if (typeof data === 'string') { data = JSON.parse(data); }
            return data;
        }

        /* Reference-counted so overlapping requests can't unhide the overlay early. */
        function showBusy(show) {
            _busyCount = Math.max(0, _busyCount + (show ? 1 : -1));
            if (!$busy || !$busy[0]) { return; }
            $busy.toggleClass('vas-201-hidden', _busyCount === 0);
        }

        function showError(text) {
            if (VIS && VIS.ADialog && VIS.ADialog.error) { VIS.ADialog.error("", "", text); }
        }

        /* Counts are never blank: an empty exception reads as 0, not as nothing. */
        function formatCount(value) {
            var n = Number(value || 0);
            if (!isFinite(n)) { n = 0; }
            return Math.round(n).toLocaleString(window.navigator.language);
        }

        /* Card money: the shared compact formatter, so a seven-figure exception does
           not overrun the row and the numbering system follows the currency (lakh /
           crore vs K / M). The symbol is composed here, never hard-coded. */
        function formatCompact(value, symbol, iso, precision) {
            var n = Number(value || 0);
            if (!isFinite(n)) { n = 0; }
            var p = Number(precision);
            if (!isFinite(p) || p < 0) { p = 2; }

            var sign = n < 0 ? '-' : '';
            var magnitude = (VIS.Util && VIS.Util.formatCompactAmount)
                ? VIS.Util.formatCompactAmount(n, iso, p)
                : Math.abs(n).toLocaleString(window.navigator.language, {
                    minimumFractionDigits: p, maximumFractionDigits: p
                });

            return sign + (symbol ? symbol : '') + magnitude;
        }

        /* Modal money: exact, at the given precision - a record list is not a KPI, so
           nothing is compacted. */
        function formatAmount(value, symbol, precision) {
            var n = Number(value || 0);
            if (!isFinite(n)) { n = 0; }
            var p = Number(precision);
            if (!isFinite(p) || p < 0) { p = 2; }

            var sign = n < 0 ? '-' : '';
            var text = Math.abs(n).toLocaleString(window.navigator.language, {
                minimumFractionDigits: p, maximumFractionDigits: p
            });
            return sign + (symbol ? symbol : '') + text;
        }

        /* Quantities carry their own decimals, not a currency precision. */
        function formatQty(value) {
            var n = Number(value || 0);
            if (!isFinite(n)) { n = 0; }
            return n.toLocaleString(window.navigator.language, { maximumFractionDigits: 4 });
        }

        /* A unit price: the quantity formatter's decimals - a price can carry more of
           them than the currency's standard precision, and rounding it to two would
           hide the very difference the row exists to show - with the document currency
           in front of it. */
        function formatPrice(value, symbol) {
            var n = Number(value || 0);
            if (!isFinite(n)) { n = 0; }
            return (n < 0 ? '-' : '') + (symbol ? symbol : '') + formatQty(Math.abs(n));
        }

        /* Server dates arrive as ISO strings without a zone marker, so they parse as
           local time and no day can shift. */
        function formatDate(value) {
            if (!value) { return ''; }
            var d = new Date(value);
            if (isNaN(d.getTime())) { return String(value); }
            return d.toLocaleDateString(window.navigator.language);
        }

        function icon(name) {
            if (name === 'chevR') { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="9 6 15 12 9 18"/></svg>'; }
            if (name === 'chevL') { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><polyline points="15 18 9 12 15 6"/></svg>'; }
            if (name === 'chevNext') { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><polyline points="9 18 15 12 9 6"/></svg>'; }
            if (name === 'chevDown') { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><polyline points="6 9 12 15 18 9"/></svg>'; }
            if (name === 'calendar') { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="4" width="18" height="17" rx="2"/><line x1="3" y1="9" x2="21" y2="9"/><line x1="8" y1="2" x2="8" y2="6"/><line x1="16" y1="2" x2="16" y2="6"/></svg>'; }
            if (name === 'close') { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><line x1="6" y1="6" x2="18" y2="18"/><line x1="18" y1="6" x2="6" y2="18"/></svg>'; }
            if (name === 'tick') { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round"><polyline points="20 6 9 17 4 12"/></svg>'; }
            /* Match: the widget's own glyph, from the reference design - two
               documents joined by an arrow. */
            if (name === 'match') { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M21 8V6a2 2 0 0 0-2-2h-6"/><path d="M3 16v2a2 2 0 0 0 2 2h6"/><path d="M3 8V6a2 2 0 0 1 2-2h2"/><path d="M17 20h2a2 2 0 0 0 2-2v-2"/><path d="M7 12h10"/><path d="M14 9l3 3-3 3"/></svg>'; }
            return '';
        }

        // ── Build ────────────────────────────────────────────────────────────

        this.Initalize = function () {
            createWidget();
            setupResizeObserver();
            loadBootstrap();
        };

        function createWidget() {
            var title = label('VAS_201_UnmatchedGRNInvoices', 'Unmatched GRNs & Invoices');
            var subtitle = label('VAS_201_ThreeWayMatchHint', '3-way match · click for records');

            $card = $(
                '<div class="vas-201-card vas-widget-bg" role="group" aria-label="' + escapeHtml(title) + '">' +
                    '<div class="vas-201-header">' +
                        '<span class="vas-201-icon">' + icon('match') + '</span>' +
                        '<div class="vas-201-head-text">' +
                            '<div class="vas-201-title" title="' + escapeHtml(title) + '">' + escapeHtml(title) + '</div>' +
                            '<div class="vas-201-subtitle" title="' + escapeHtml(subtitle) + '">' + escapeHtml(subtitle) + '</div>' +
                        '</div>' +
                        /* Period chip: the widget's only filter. It names the period
                           every figure on the card belongs to, and opens the picker. */
                        '<button type="button" class="vas-201-periodchip" aria-haspopup="listbox">' +
                            icon('calendar') +
                            '<span class="vas-201-periodchip-label"></span>' +
                            icon('chevDown') +
                        '</button>' +
                    '</div>' +
                    '<div class="vas-201-body">' +
                        /* Column header and rows share ONE grid template, so the
                           count and value columns line up down the card. */
                        '<div class="vas-201-thead">' +
                            '<span class="vas-201-cell" title="' + escapeHtml(label('VAS_201_Exception', 'Exception')) + '">' +
                                escapeHtml(label('VAS_201_Exception', 'Exception')) + '</span>' +
                            '<span class="vas-201-cell vas-201-cell-num" title="' + escapeHtml(label('VAS_201_Count', 'Count')) + '">' +
                                escapeHtml(label('VAS_201_Count', 'Count')) + '</span>' +
                            '<span class="vas-201-cell vas-201-cell-num" title="' + escapeHtml(label('VAS_201_Value', 'Value')) + '">' +
                                escapeHtml(label('VAS_201_Value', 'Value')) + '</span>' +
                            '<span class="vas-201-cell-chev"></span>' +
                        '</div>' +
                        '<div class="vas-201-list"></div>' +
                        '<div class="vas-201-foot"></div>' +
                    '</div>' +
                '</div>'
            );

            $list = $card.find('.vas-201-list');
            $foot = $card.find('.vas-201-foot');
            $periodBtn = $card.find('.vas-201-periodchip');

            $periodBtn.on('click', function (e) {
                e.stopPropagation();
                togglePicker();
            });

            /* Delegated so the handlers survive every repaint of the list. */
            $list.on('click', '.vas-201-row', function () {
                openModal($(this).attr('data-category'));
            });
            $list.on('keydown', '.vas-201-row', function (e) {
                if (e.key === 'Enter' || e.keyCode === 13 || e.key === ' ' || e.keyCode === 32) {
                    e.preventDefault();
                    $(this).trigger('click');
                }
            });

            $root.append($card);

            $busy = $('<div class="vas-201-busy vas-201-hidden"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $root.append($busy);
        }

        /* Publishes the widget's own measured width so the card anchor can scale on
           the widget instead of the whole dashboard when the cell is unusual. */
        function setupResizeObserver() {
            if (typeof ResizeObserver === 'undefined') { return; }
            try {
                var ro = new ResizeObserver(function (entries) {
                    for (var i = 0; i < entries.length; i++) {
                        var width = entries[i].contentRect.width;
                        if (width > 0 && $root[0]) {
                            $root[0].style.setProperty('--widget-inline-size', width + 'px');
                        }
                    }
                });
                ro.observe($root[0]);
            } catch (e) { }
        }

        // ── Loads ────────────────────────────────────────────────────────────

        function loadBootstrap() {
            showBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_201_UnMatchedGRNInvoiceWidget/GetBootstrap',
                type: 'GET',
                cache: false,
                success: function (res) {
                    var data = null;
                    try { data = parseResponse(res); } catch (e) { }
                    if (!data || data.error) { renderState(label('VAS_192_CouldntLoad', "Couldn't load"), true); return; }

                    _periods = data.Periods || [];
                    _periodId = data.C_Period_ID || 0;
                    _periodName = data.PeriodName || '';

                    paintPeriod();

                    if (_periods.length === 0 || _periodId <= 0) {
                        renderState(label('VAS_201_NoOpenPeriod', 'No open accounting period.'), false);
                        return;
                    }

                    applyData(data.Data);
                },
                error: function () { renderState(label('VAS_192_CouldntLoad', "Couldn't load"), true); },
                complete: function () { showBusy(false); }
            });
        }

        function loadPeriodData(periodId) {
            showBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_201_UnMatchedGRNInvoiceWidget/GetPeriodData',
                type: 'GET',
                cache: false,
                data: { periodId: periodId },
                success: function (res) {
                    var data = null;
                    try { data = parseResponse(res); } catch (e) { }
                    if (!data || data.error) { renderState(label('VAS_192_CouldntLoad', "Couldn't load"), true); return; }

                    /* A late response for a period the user has already moved away
                       from must not overwrite the current card. */
                    if (periodId !== _periodId) { return; }

                    if (data.ErrorCode) {
                        /* The period stopped qualifying (closed elsewhere while the
                           dashboard was open) - re-read the whole selector. */
                        loadBootstrap();
                        return;
                    }

                    applyData(data);
                },
                error: function () { renderState(label('VAS_192_CouldntLoad', "Couldn't load"), true); },
                complete: function () { showBusy(false); }
            });
        }

        function applyData(data) {
            _data = data || null;
            paintList();
        }

        // ── Card rendering ───────────────────────────────────────────────────

        function paintPeriod() {
            var text = _periodName || '—';
            $periodBtn.find('.vas-201-periodchip-label').text(text);
            $periodBtn.attr('title', text);
            /* Nothing to pick when the tenant has no open period at all. */
            $periodBtn.toggleClass('vas-201-hidden', _periods.length === 0);
        }

        function renderState(text, isError) {
            if (!$list) { return; }
            $list.html('<div class="vas-201-state' + (isError ? ' vas-201-state-error' : '') + '">' +
                escapeHtml(text) + '</div>');
            $foot.empty();
        }

        function paintList() {
            if (!$list) { return; }

            var totals = (_data && _data.Exceptions) ? _data.Exceptions : [];
            var symbol = _data ? _data.BaseCurrencySymbol : '';
            var iso = _data ? _data.BaseCurrencyIso : '';
            var precision = _data ? _data.BaseCurrencyPrecision : 2;
            var html = '';

            for (var i = 0; i < EXCEPTIONS.length; i++) {
                var def = EXCEPTIONS[i];
                var total = findTotal(totals, def.code) || { RecordCount: 0, BaseValue: 0 };

                var name = label(def.key, def.text);
                var count = formatCount(total.RecordCount);
                var value = formatCompact(total.BaseValue, symbol, iso, precision);

                html += '<div class="vas-201-row" role="button" tabindex="0" data-category="' + def.code +
                        '" aria-label="' + escapeHtml(name) + '">' +
                    '<span class="vas-201-cell vas-201-cell-name" title="' + escapeHtml(name) + '">' +
                        escapeHtml(name) + '</span>' +
                    '<span class="vas-201-cell vas-201-cell-num vas-201-tone-' + def.tone +
                        '" title="' + escapeHtml(count) + '">' + escapeHtml(count) + '</span>' +
                    '<span class="vas-201-cell vas-201-cell-num vas-201-cell-value" title="' +
                        escapeHtml(value) + '">' + escapeHtml(value) + '</span>' +
                    '<span class="vas-201-cell-chev">' + icon('chevR') + '</span>' +
                '</div>';
            }

            $list.html(html);

            /* Deliberately not a sum of the three counts: the mismatch category
               overlaps the other two document populations, so a total would double
               count. The footer points at the action instead. */
            var hint = label('VAS_201_FooterHint', 'Click an exception to review matching details');
            $foot.html('<span class="vas-201-foot-info" title="' + escapeHtml(hint) + '">' +
                escapeHtml(hint) + '</span>');
        }

        function findTotal(totals, code) {
            for (var i = 0; i < totals.length; i++) {
                if (totals[i].Category === code) { return totals[i]; }
            }
            return null;
        }

        // ── Period picker ────────────────────────────────────────────────────

        /* Anchored under the chip and appended to <body>. Non-modal, and unlike the
           detail modal it closes on an outside click - it is a menu, not a dialog. */
        function buildPicker() {
            $picker = $('<div class="vas-201-pp vas-201-hidden" role="listbox" aria-label="' +
                escapeHtml(label('VAS_201_DashboardPeriod', 'Dashboard period')) + '">');
            $('body').append($picker);

            $picker.on('click', '.vas-201-pp-opt', function () {
                var id = parseInt($(this).attr('data-id'), 10) || 0;
                closePicker();
                selectPeriod(id);
            });
        }

        function fillPicker() {
            var html = '<div class="vas-201-pp-h">' +
                escapeHtml(label('VAS_201_DashboardPeriod', 'Dashboard period')) + '</div>';

            for (var i = 0; i < _periods.length; i++) {
                var p = _periods[i];
                var selected = p.C_Period_ID === _periodId;
                var meta = p.FiscalYear ? String(p.FiscalYear) : '';

                html += '<button type="button" class="vas-201-pp-opt" role="option" data-id="' + p.C_Period_ID +
                        '" aria-selected="' + (selected ? 'true' : 'false') + '">' +
                    '<span class="vas-201-pp-name" title="' + escapeHtml(p.Name || '') + '">' +
                        escapeHtml(p.Name || '') + '</span>' +
                    (meta ? '<span class="vas-201-pp-meta">' + escapeHtml(meta) + '</span>' : '') +
                    '<span class="vas-201-pp-tick">' + icon('tick') + '</span>' +
                '</button>';
            }

            $picker.html(html);
        }

        function positionPicker() {
            if (!$picker || !$periodBtn || !$periodBtn[0]) { return; }

            var rect = $periodBtn[0].getBoundingClientRect();
            var pw = $picker.outerWidth();
            var ph = $picker.outerHeight();
            var gap = 6;

            var left = Math.min(rect.left, window.innerWidth - pw - 8);
            left = Math.max(8, left);

            var top = rect.bottom + gap;
            if (top + ph > window.innerHeight - 8) {
                var above = rect.top - ph - gap;
                top = above >= 8 ? above : Math.max(8, window.innerHeight - ph - 8);
            }

            $picker.css({ left: Math.round(left) + 'px', top: Math.round(top) + 'px' });
        }

        function openPicker() {
            if (_periods.length === 0) { return; }
            if (!$picker) { buildPicker(); }

            fillPicker();
            $picker.removeClass('vas-201-hidden');
            positionPicker();
            _pickerOpen = true;

            $(document).on('click' + _ns, onDocumentClick);
            $(document).on('keydown' + _ns, onPickerKeyDown);
            $(window).on('resize' + _ns + ' scroll' + _ns, closePicker);
        }

        function closePicker() {
            if (!_pickerOpen) { return; }
            _pickerOpen = false;
            if ($picker) { $picker.addClass('vas-201-hidden'); }

            $(document).off('click' + _ns);
            $(document).off('keydown' + _ns);
            $(window).off('resize' + _ns + ' scroll' + _ns);
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

            /* Never leave records of the previous period on screen. */
            closeModal();

            paintPeriod();
            loadPeriodData(periodId);
        }

        // ── Detail modal ─────────────────────────────────────────────────────

        function buildModal() {
            $overlay = $(
                '<div class="vas-201-overlay vas-201-hidden">' +
                    '<div class="vas-201-modal" role="dialog" aria-modal="true">' +
                        '<div class="vas-201-modal-head">' +
                            '<span class="vas-201-modal-ico">' + icon('match') + '</span>' +
                            '<div class="vas-201-modal-heads">' +
                                '<div class="vas-201-modal-title"></div>' +
                                '<div class="vas-201-modal-sub"></div>' +
                            '</div>' +
                            '<button type="button" class="vas-201-modal-close" aria-label="' +
                                escapeHtml(label('VAS_018_Close', 'Close')) + '">' + icon('close') + '</button>' +
                        '</div>' +
                        '<div class="vas-201-modal-body"></div>' +
                        '<div class="vas-201-modal-foot"></div>' +
                        /* Sits over the panel, not over the body, so the header and
                           the pager stay readable while a page is in flight. */
                        '<div class="vas-201-modal-busy vas-201-hidden">' +
                            '<div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div>' +
                        '</div>' +
                    '</div>' +
                '</div>'
            );

            $('body').append($overlay);
            $modalBody = $overlay.find('.vas-201-modal-body');
            $modalPager = $overlay.find('.vas-201-modal-foot');
            $modalBusy = $overlay.find('.vas-201-modal-busy');

            /* The body holds exactly one page, so its height is FIXED - not merely
               floored. syncModalHeight() grows it to the measured height of a real
               full page on the first paint. */
            _bodyH = (MODAL_ROW_H * MODAL_PAGE_SIZE) + MODAL_HEAD_H + MODAL_BODY_SLACK;
            $modalBody.css('height', _bodyH + 'px');

            /* The close button, and Escape - deliberately NOT a click on the scrim. This
               dialog is a work list a reader pages through, so a stray click landing off
               the panel is far more likely to be a missed target than a decision to leave,
               and losing the page to it has nothing to undo it with. */
            $overlay.find('.vas-201-modal-close').on('click', closeModal);

            $modalPager.on('click', '.vas-201-pgbtn', function () {
                var $btn = $(this);
                if ($btn.prop('disabled')) { return; }
                _page = $btn.attr('data-dir') === 'next' ? _page + 1 : Math.max(1, _page - 1);
                loadModalPage();
            });

            /* The document numbers ARE the zoom affordance - a receipt link opens the
               material receipt, an invoice link the vendor invoice. */
            $modalBody.on('click', '.vas-201-doclink', function (e) {
                e.stopPropagation();
                var $link = $(this);
                zoomTo($link.attr('data-target'), parseInt($link.attr('data-id'), 10) || 0);
            });
        }

        function openModal(category) {
            if (!category || _periodId <= 0) { return; }
            if (!$overlay) { buildModal(); }

            _category = category;
            _page = 1;

            $overlay.find('.vas-201-modal-title').text(modalTitle());
            $overlay.find('.vas-201-modal-sub').text(_periodName || '');

            /* Nothing of the previous category may show through: the body opens empty
               (at its pinned height) with the indicator over it. */
            $modalBody.empty();
            $modalPager.empty();

            $overlay.removeClass('vas-201-hidden');
            _modalOpen = true;
            closePicker();

            $(document).on('keydown' + _ns + 'm', onModalKeyDown);

            loadModalPage();
        }

        function modalTitle() {
            for (var i = 0; i < EXCEPTIONS.length; i++) {
                if (EXCEPTIONS[i].code === _category) {
                    return label(EXCEPTIONS[i].key, EXCEPTIONS[i].text);
                }
            }
            return '';
        }

        function closeModal() {
            if (!_modalOpen) { return; }
            _modalOpen = false;
            _detailSeq++;                       // drop any page still in flight
            showModalBusy(false);
            if ($overlay) { $overlay.addClass('vas-201-hidden'); }
            $(document).off('keydown' + _ns + 'm');
        }

        function onModalKeyDown(e) {
            if (e.key === 'Escape' || e.keyCode === 27) { closeModal(); }
        }

        function showModalBusy(show) {
            if (!$modalBusy || !$modalBusy[0]) { return; }
            $modalBusy.toggleClass('vas-201-hidden', !show);
        }

        /* One page at a time from the server - the modal never receives the whole
           set. The previous page stays painted underneath the busy indicator, so the
           panel neither blanks nor resizes while the next one arrives. */
        function loadModalPage() {
            var mySeq = ++_detailSeq;
            var periodId = _periodId;

            showModalBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_201_UnMatchedGRNInvoiceWidget/GetRecords',
                type: 'GET',
                cache: false,
                data: {
                    periodId: periodId,
                    category: _category,
                    pageNo: _page,
                    pageSize: MODAL_PAGE_SIZE
                },
                success: function (res) {
                    /* Stale response: another page, another category, another period,
                       or the modal has been closed since. */
                    if (mySeq !== _detailSeq || periodId !== _periodId) { return; }

                    var data = null;
                    try { data = parseResponse(res); } catch (e) { }

                    if (!data || data.error || data.ErrorCode) {
                        renderModalState(label('VAS_192_CouldntLoad', "Couldn't load"), true);
                        return;
                    }

                    _page = data.PageNo || 1;
                    renderModalRows(data);
                },
                error: function () {
                    if (mySeq !== _detailSeq) { return; }
                    renderModalState(label('VAS_192_CouldntLoad', "Couldn't load"), true);
                },
                complete: function () {
                    /* Only the newest request may clear the indicator - an overtaken
                       response must not unhide the panel while its successor runs. */
                    if (mySeq === _detailSeq) { showModalBusy(false); }
                }
            });
        }

        /* Empty / error takeover of the body. The footer keeps its pager row (with
           the controls disabled) rather than collapsing, so the dialog holds the same
           height in every state. */
        function renderModalState(text, isError) {
            $modalBody.html('<div class="vas-201-modal-state' + (isError ? ' vas-201-modal-state-error' : '') +
                '">' + escapeHtml(text) + '</div>');
            renderModalPager({ Total: 0, PageSize: MODAL_PAGE_SIZE, PageNo: 1, Rows: [] });
        }

        /* Each exception has its own shape, so each has its own column set - but all
           three are SIX columns, so header and rows share one grid template and the
           stylesheet has one place to tune. */
        function columnsFor(category) {
            /* Both document lists value their rows in the tenant base currency, not in
               the document's own - so the caption says converted, and the tooltip says
               what it was converted into. */
            var converted = tip(label('VAS_201_ConvertedAmount', 'Converted Amount'),
                label('VAS_201_AmountInBaseCurrency', 'Amount in Base Currency'));

            if (category === 'GRNNOTINV') {
                /* Five columns: the receipt's own date is the one that matters here,
                   and every row already belongs to the period named in the header. */
                return [
                    colLabel('DocumentNo', 'VAS_201_DocumentNo', 'Document No'),
                    label('VAS_201_ReceiptDate', 'Receipt Date'),
                    colLabel('C_BPartner_ID', 'VAS_201_BusinessPartner', 'Business Partner'),
                    label('VAS_201_Lines', 'Lines'),
                    converted
                ];
            }
            if (category === 'INVNOGRN') {
                return [
                    colLabel('DocumentNo', 'VAS_201_DocumentNo', 'Document No'),
                    colLabel('DateInvoiced', 'VAS_201_DateInvoiced', 'Invoice Date'),
                    colLabel('C_BPartner_ID', 'VAS_201_BusinessPartner', 'Business Partner'),
                    colLabel('C_Currency_ID', 'VAS_201_Currency', 'Currency'),
                    label('VAS_201_InvoiceAmount', 'Invoice Amount'),
                    converted
                ];
            }
            return [
                label('VAS_201_GRN', 'GRN'),
                label('VAS_201_Invoice', 'Invoice'),
                label('VAS_201_Product', 'Product'),
                label('VAS_201_QtyVariance', 'Qty Variance'),
                label('VAS_201_PriceVariance', 'Price Variance'),
                label('VAS_201_AmountDifference', 'Amount Difference')
            ];
        }

        /* Which columns are numeric, per category - they right-align. */
        function numericColumns(category) {
            if (category === 'GRNNOTINV') { return { 3: true, 4: true }; }
            if (category === 'INVNOGRN') { return { 4: true, 5: true }; }
            return { 3: true, 4: true, 5: true };
        }

        /* Grid variant: the three lists do not share a column count, and where they
           do, position N does not mean the same thing - the mismatch list carries a
           second document link where the others carry a date. The modifier gives the
           stylesheet one template per shape and tells it what it may fold away. */
        function gridVariant(category) {
            if (category === 'GRNNOTINV') { return 'vas-201-dgrid-grn'; }
            if (category === 'INVNOGRN') { return 'vas-201-dgrid-inv'; }
            return 'vas-201-dgrid-pair';
        }

        function renderModalRows(data) {
            var rows = data.Rows || [];

            if (rows.length === 0) {
                renderModalState(label('VAS_201_NoRecords', 'No records in this category.'), false);
                return;
            }

            var captions = columnsFor(_category);
            var numeric = numericColumns(_category);

            var head = '<div class="vas-201-dhead">';
            for (var c = 0; c < captions.length; c++) {
                head += headCell(captions[c], numeric[c] ? 'vas-201-dcell-num' : '');
            }
            head += '</div>';

            var body = '';
            for (var i = 0; i < rows.length; i++) {
                body += buildDetailRow(rows[i], data, numeric);
            }

            $modalBody.html('<div class="vas-201-dgrid ' + gridVariant(_category) + '">' + head + body + '</div>');
            $modalBody.scrollTop(0);

            syncModalHeight(rows.length);
            renderModalPager(data);
        }

        function cell(text, extraClass) {
            return '<span class="vas-201-dcell' + (extraClass ? ' ' + extraClass : '') +
                '" title="' + escapeHtml(text) + '">' + escapeHtml(text) + '</span>';
        }

        /* A header caption. Unlike a body cell its tooltip is not the text repeated:
           a caption that carries a gloss shows the gloss instead. */
        function headCell(caption, extraClass) {
            return '<span class="vas-201-dcell' + (extraClass ? ' ' + extraClass : '') +
                '" title="' + escapeHtml(captionTitle(caption)) + '">' +
                escapeHtml(captionText(caption)) + '</span>';
        }

        /* A variance cell: two figures separated by a slash. Each half carries its
           OWN tooltip naming which side it is, because "12 / 10" on its own does not
           say which number came from the receipt and which from the invoice - and the
           column caption ("Qty Variance") cannot say it either. The cell itself takes
           no title, or it would shadow both halves on hover. */
        function pairCell(aText, aLabel, bText, bLabel, extraClass) {
            return '<span class="vas-201-dcell' + (extraClass ? ' ' + extraClass : '') + '">' +
                '<span title="' + escapeHtml(aLabel + ': ' + aText) + '">' + escapeHtml(aText) + '</span>' +
                '<span class="vas-201-dpair-sep"> / </span>' +
                '<span title="' + escapeHtml(bLabel + ': ' + bText) + '">' + escapeHtml(bText) + '</span>' +
            '</span>';
        }

        /* A document number rendered as a link. A button, not an anchor: there is no
           URL to follow - the framework opens the window. Without an id the number
           stays plain text rather than offering a dead link. */
        function docCell(documentNo, target, id) {
            var text = documentNo || '';
            if (!(id > 0)) { return cell(text, 'vas-201-dcell-b'); }

            return '<span class="vas-201-dcell vas-201-dcell-doc">' +
                '<button type="button" class="vas-201-doclink" data-target="' + target +
                    '" data-id="' + id + '" title="' + escapeHtml(text) + '">' +
                    escapeHtml(text) + '</button>' +
            '</span>';
        }

        function buildDetailRow(row, data, numeric) {
            var baseValue = formatAmount(row.BaseValue, data.BaseCurrencySymbol, data.BaseCurrencyPrecision);
            var html = '<div class="vas-201-drow">';

            if (_category === 'GRNNOTINV') {
                html += docCell(row.DocumentNo, 'grn', row.M_InOut_ID) +
                    cell(formatDate(row.DateOther)) +
                    cell(row.BusinessPartnerName || '') +
                    cell(formatCount(row.LineCount), 'vas-201-dcell-num') +
                    cell(baseValue, 'vas-201-dcell-num');
            } else if (_category === 'INVNOGRN') {
                html += docCell(row.DocumentNo, 'invoice', row.C_Invoice_ID) +
                    cell(formatDate(row.DateOther)) +
                    cell(row.BusinessPartnerName || '') +
                    cell(row.CurrencyIso || '') +
                    cell(formatAmount(row.DocumentValue, row.CurrencySymbol, row.CurrencyPrecision), 'vas-201-dcell-num') +
                    cell(baseValue, 'vas-201-dcell-num');
            } else {
                /* Both sides of the match are links, and each variance keeps its two
                   figures side by side (received / invoiced, ordered / invoiced) so
                   the disagreement is visible, not just asserted.

                   Colour says which side is off:
                     quantity  - red whenever the two disagree at all;
                     price     - green when the invoice came in UNDER the order price
                                 (the difference is in our favour), red when over.
                   A side that matches stays in the ordinary text colour. */
                var qtyOff = row.VarianceType === 'QTY' || row.VarianceType === 'BOTH';
                var priceOff = row.VarianceType === 'PRICE' || row.VarianceType === 'BOTH';

                var qtyClass = 'vas-201-dcell-num' + (qtyOff ? ' vas-201-dcell-bad' : '');
                var priceClass = 'vas-201-dcell-num';
                if (priceOff) {
                    priceClass += Number(row.OrderPrice) > Number(row.InvoicePrice)
                        ? ' vas-201-dcell-good'
                        : ' vas-201-dcell-bad';
                }

                /* Prices and the difference are shown in the INVOICE's own currency -
                   the figures the document itself carries. The other two lists still
                   value their totals in the tenant base currency, which is why the
                   symbol comes off the row here and off the payload there. */
                var sym = row.CurrencySymbol || row.CurrencyIso || '';

                html += docCell(row.DocumentNo, 'grn', row.M_InOut_ID) +
                    docCell(row.SecondaryDocumentNo, 'invoice', row.C_Invoice_ID) +
                    cell(row.ProductName || '') +
                    pairCell(formatQty(row.GrnQty), label('VAS_201_GrnQty', 'GRN Qty'),
                             formatQty(row.InvoiceQty), label('VAS_201_InvoiceQty', 'Invoice Qty'),
                             qtyClass) +
                    pairCell(formatPrice(row.OrderPrice, sym), label('VAS_201_OrderPrice', 'Order Price'),
                             formatPrice(row.InvoicePrice, sym), label('VAS_201_InvoicePrice', 'Invoice Price'),
                             priceClass) +
                    cell(formatAmount(row.DocumentValue, sym, row.CurrencyPrecision), 'vas-201-dcell-num');
            }

            return html + '</div>';
        }

        /* Grows the fixed body to whatever a FULL page actually measures, so the last
           page never introduces a scrollbar the first page did not have. Only ever
           grows: shrinking back on a short page is the fluctuation this prevents. */
        function syncModalHeight(rowCount) {
            if (!$modalBody || !$modalBody[0] || rowCount <= 0) { return; }

            var headEl = $modalBody[0].querySelector('.vas-201-dhead');
            var rowEls = $modalBody[0].querySelectorAll('.vas-201-drow');
            if (!rowEls.length) { return; }

            var rowH = 0;
            for (var i = 0; i < rowEls.length; i++) {
                if (rowEls[i].offsetHeight > rowH) { rowH = rowEls[i].offsetHeight; }
            }
            if (rowH <= 0) { return; }

            var headH = headEl ? headEl.offsetHeight : MODAL_HEAD_H;
            var needed = headH + (rowH * MODAL_PAGE_SIZE) + MODAL_BODY_SLACK;

            if (needed > _bodyH) {
                _bodyH = needed;
                $modalBody.css('height', _bodyH + 'px');
            }
        }

        /* Canonical Widget Footer Pager (design.md): "Showing a–b of N" left,
           compact prev / "n of m" / next right. */
        function renderModalPager(data) {
            var total = Number(data.Total || 0);
            var pageSize = Number(data.PageSize || MODAL_PAGE_SIZE);
            var pageNo = Number(data.PageNo || 1);
            var rows = (data.Rows || []).length;

            var totalPages = Math.max(1, Math.ceil(total / pageSize));
            var from = rows > 0 ? ((pageNo - 1) * pageSize) + 1 : 0;
            var to = rows > 0 ? from + rows - 1 : 0;

            var ofTxt = label('VAS_026_Of', 'of');
            var showing = label('VAS_026_Showing', 'Showing') + ' ' + from + '–' + to + ' ' +
                ofTxt + ' ' + formatCount(total);

            var prevDis = pageNo <= 1 ? ' disabled' : '';
            var nextDis = (rows === 0 || pageNo >= totalPages) ? ' disabled' : '';

            $modalPager.html(
                '<span class="vas-201-pager-info" title="' + escapeHtml(showing) + '">' +
                    escapeHtml(showing) + '</span>' +
                '<span class="vas-201-pager-nav">' +
                    '<button type="button" class="vas-201-pgbtn" data-dir="prev" aria-label="' +
                        escapeHtml(label('VAS_026_Prev', 'Previous')) + '"' + prevDis + '>' + icon('chevL') + '</button>' +
                    '<span class="vas-201-pager-label">' + pageNo + ' ' + escapeHtml(ofTxt) + ' ' + totalPages + '</span>' +
                    '<button type="button" class="vas-201-pgbtn" data-dir="next" aria-label="' +
                        escapeHtml(label('VAS_026_Next', 'Next')) + '"' + nextDis + '>' + icon('chevNext') + '</button>' +
                '</span>'
            );
        }

        /* Opens a receipt or an invoice in its own standard window. The dialog closes
           first: the record opens in its own window, and leaving the modal over it
           would hide what was just opened. Degrades silently - a click can never
           throw. */
        function zoomTo(target, id) {
            if (!target || id <= 0 || !VAS.ZoomUtil) { return; }

            closeModal();

            try {
                if (target === 'grn') {
                    VAS.ZoomUtil.zoomToRecord('M_InOut_ID', id, 0,
                        ZOOM_WINDOW_RECEIPT, ZOOM_WINDOW_RECEIPT_OLD);
                } else {
                    VAS.ZoomUtil.zoomToRecord('C_Invoice_ID', id, 0,
                        ZOOM_WINDOW_INVOICE, ZOOM_WINDOW_INVOICE_OLD);
                }
            } catch (e) {
                if (window.console) { console.log(e); }
                showError(label('VAS_192_CouldntLoad', "Couldn't load"));
            }
        }

        // ── Framework contract ───────────────────────────────────────────────

        this.refreshWidget = function () {
            /* Refresh means "start clean": drop the open panels and re-read the
               period list, because a period may have been opened or closed in the
               Period Control widgets since this card last loaded. */
            closePicker();
            closeModal();

            _periods = [];
            _periodId = 0;
            _periodName = '';
            _data = null;

            loadBootstrap();
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            if ($list) { $list.off(); }
            if ($periodBtn) { $periodBtn.off(); }

            /* Both overlays were appended to <body>, so removing $root would leave
               them behind - close each (which unbinds the document/window handlers
               under this instance's namespace) and tear them down explicitly. */
            closePicker();
            if ($picker) {
                $picker.off();
                $picker.remove();
                $picker = null;
            }

            closeModal();
            if ($overlay) {
                $overlay.off();
                $overlay.find('*').off();
                $overlay.remove();
                $overlay = null;
                $modalBody = null;
                $modalPager = null;
                $modalBusy = null;
            }

            $root.remove();
        };
    };

    VAS.VAS_201_UnMatchedGRNInvoiceWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_201_UnMatchedGRNInvoiceWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_201_UnMatchedGRNInvoiceWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_201_UnMatchedGRNInvoiceWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_201_UnMatchedGRNInvoiceWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
