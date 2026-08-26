/**
 * VAS_198_UnPostedAccountEntriesWidget
 * 3x2 grid widget for the Period Control dashboard.
 *
 * Every completed / closed accounting document of ONE open period that carries no
 * accounting entry yet, grouped by transaction type:
 *
 *   Transaction Type          Docs   Value
 *   --------------------------------------------
 *   AR Invoice                   3   28,400
 *   AP Invoice                   2   17,250
 *   GRN                          4    9,600
 *
 * Clicking a type opens a paged record list; the document number is a link that
 * opens that record in its own standard window.
 *
 * The transaction types are NOT hard-coded. The server discovers them from the
 * Application Dictionary - every active physical table with a Posted column, on
 * every menu-reachable window that opens it - and a row is one of those SCREENS,
 * not one of those tables. So C_Invoice arrives as AP Invoice / AR Invoice /
 * Expense Invoice, M_InOut as GRN / Delivery Order / the return flavours, and so
 * on, named as the tenant named its windows. A module that adds a posted document
 * screen appears here without a code change.
 *
 * A row is therefore a table, a window, and a flag saying whether it is that
 * table's catch-all - the records none of its screens claim, which can sit on the
 * same window as one of them. Those three are the whole of what this widget holds
 * and the whole of what it sends back. What separates two screens over one table
 * is that screen's own record filter, which lives on the server and is never sent
 * here. The key column name used to build a Zoom query arrives with the records
 * and is never echoed back - so nothing here can name a table the dictionary did
 * not publish. Zoom needs no rule of its own: a row IS a screen, so it opens that
 * screen.
 *
 * A type is valued either from a header total in its own currency (an invoice's
 * GrandTotal, a payment's PayAmt) or, where it has no header total, from the
 * stored costs on its lines - which is how an inventory movement gets a figure at
 * all, and those are base currency already. A type the server can value neither
 * way shows its document count and a dash. That is deliberate: a count the user
 * can act on beats a figure nobody can vouch for.
 *
 * The card's values are base currency so types in different document currencies
 * can be read against each other; the record list shows both the document's own
 * amount and the converted one.
 *
 * Period source: the OPEN periods of the tenant's primary calendar - a period
 * qualifies when at least one active C_PeriodControl row of it is Open. Nothing is
 * derived from the calendar month. Each type is bounded by its own DateAcct, or by
 * MovementDate where it has no DateAcct - which is why the record list captions its
 * date column from the column the server actually used rather than hard-coding one.
 *
 * The number of types is data, not a constant, so the card pages: it measures its
 * own list and shows as many rows as the cell actually fits, with the canonical
 * footer pager for the rest. Nothing inside the card scrolls.
 *
 * Sizing follows design.md -> dashboard-widgets.md: the card carries the widget
 * root anchor clamp, the header reads --dash-inline-size (populated by
 * ensureDashInlineSizeVar), the type grid shares one column template between its
 * header and its rows, and nothing scrolls. Narrow cells are handled by container
 * queries, the body-level modal and picker by media queries (see the stylesheet).
 *
 * Summary Message Table
 * Rows marked (reuse) already exist in the project under another key and are
 * NOT duplicated here. Column captions prefer the framework's own translated
 * element name (VIS.translatedTexts[<ColumnName>]) and only fall back to the key.
 *  # | Current Text                             | Message Key
 * ---+------------------------------------------+---------------------------------
 *  1 | Unposted Accounting Entries              | VAS_198_UnpostedAccountingEntries
 *  2 | By transaction type · click for documents | VAS_198_ByTransactionTypeHint
 *  3 | Transaction Type                         | VAS_198_TransactionType
 *  4 | Docs                                     | VAS_198_Docs
 *  5 | documents not posted                     | VAS_198_DocumentsNotPosted
 *  6 | Nothing unposted in this period          | VAS_198_NothingUnposted
 *  7 | Document Amount                          | VAS_198_DocumentAmount
 *  8 | Created By                               | VAS_198_CreatedBy
 *  9 | No accounting value for this transaction type (tooltip) | VAS_198_NoAmountStrategy
 * 10 | Value                                    | VAS_Value                 (reuse)
 * 11 | Account Date                             | VAS_200_DateAcct          (reuse)
 * 12 | Document Type                            | VIS_DocumentType          (reuse)
 * 13 | Currency                                 | VAS_201_Currency          (reuse)
 * 14 | No open accounting period                | VAS_201_NoOpenPeriod      (reuse)
 * 15 | Dashboard period                         | VAS_201_DashboardPeriod   (reuse)
 * 16 | No records in this category              | VAS_201_NoRecords         (reuse)
 * 17 | Document No                              | DocumentNo                (reuse)
 * 18 | Created By                    (column)   | CreatedBy                 (reuse)
 * 19 | Close                                    | VAS_018_Close             (reuse)
 * 20 | Couldn't load                            | VAS_192_CouldntLoad       (reuse)
 * 21 | Showing                                  | VAS_026_Showing           (reuse)
 * 22 | of                                       | VAS_026_Of                (reuse)
 * 23 | Previous                                 | VAS_026_Prev              (reuse)
 * 24 | Next                                     | VAS_026_Next              (reuse)
 * 25 | Other                                    | VAS_198_OtherRecords      (server)
 *
 * (server) is resolved in VAS_198_UnPostedAccountEntriesModel, not here: it
 * qualifies a table's catch-all row when that row would otherwise repeat the name
 * of the screen it shares a window with. Unseeded it renders as "Other".
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

    var MODAL_PAGE_SIZE = 8;

    /* Starting estimate for the detail grid's row / header heights, in px. It only
       has to hold until the first page is painted: the real heights are then
       measured and the body grown to fit a full page exactly, because the painted
       height depends on the host's font scale. */
    var MODAL_ROW_H = 58;
    var MODAL_HEAD_H = 52;
    var MODAL_BODY_SLACK = 4;

    /* Card list: how many type rows fit is measured, not assumed. The fallback row
       height only has to survive the very first paint, and the floor keeps the card
       showing something even if the cell collapses to nothing. */
    var CARD_ROW_FALLBACK = 34;
    var CARD_MIN_ROWS = 2;

    VAS.VAS_198_UnPostedAccountEntriesWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas-198-root">');
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
        var _ns = '.vas198_' + (VAS.VAS_198_UnPostedAccountEntriesWidget._seq =
            (VAS.VAS_198_UnPostedAccountEntriesWidget._seq || 0) + 1);

        /* Current selection and the data painted from it. */
        var _periods = [];
        var _periodId = 0;
        var _periodName = '';
        var _data = null;

        /* Card list paging, sized from the measured cell. */
        var _cardPage = 1;
        var _cardPageSize = CARD_MIN_ROWS;
        var _cardRowH = 0;
        var _cardNeedsSync = true;
        var _listObserver = null;

        /* Detail modal state. _detailSeq drops the response of a page the user has
           already navigated away from (or of a period they have already changed). */
        var _tableId = 0;
        var _windowId = 0;

        /* The third part of a row's identity: a table's catch-all row - the records
           none of its screens claim - can sit on the same window as one of those
           screens, and without this the server cannot tell the two apart. */
        var _isComplement = false;
        var _typeName = '';
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
            $busy.toggleClass('vas-198-hidden', _busyCount === 0);
        }

        function showError(text) {
            if (VIS && VIS.ADialog && VIS.ADialog.error) { VIS.ADialog.error("", "", text); }
        }

        /* Counts are never blank: an empty type reads as 0, not as nothing. */
        function formatCount(value) {
            var n = Number(value || 0);
            if (!isFinite(n)) { n = 0; }
            return Math.round(n).toLocaleString(window.navigator.language);
        }

        /* Card money: the shared compact formatter, so a seven-figure type does not
           overrun the row and the numbering system follows the currency (lakh /
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
            /* Posting: the widget's own glyph, from the reference design - an entry
               waiting on an axis it has not been carried across yet. */
            if (name === 'posting') { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 2v6"/><path d="M12 22v-6"/><circle cx="12" cy="12" r="4"/><path d="M4.9 4.9l2.8 2.8"/><path d="M16.3 16.3l2.8 2.8"/></svg>'; }
            return '';
        }

        // ── Build ────────────────────────────────────────────────────────────

        this.Initalize = function () {
            createWidget();
            setupResizeObserver();
            loadBootstrap();
        };

        function createWidget() {
            var title = label('VAS_198_UnpostedAccountingEntries', 'Unposted Accounting Entries');
            var subtitle = label('VAS_198_ByTransactionTypeHint', 'By transaction type · click for documents');
            var typeCap = label('VAS_198_TransactionType', 'Transaction Type');
            var docsCap = label('VAS_198_Docs', 'Docs');
            var valueCap = label('VAS_Value', 'Value');

            $card = $(
                '<div class="vas-198-card vas-widget-bg" role="group" aria-label="' + escapeHtml(title) + '">' +
                    '<div class="vas-198-header">' +
                        '<span class="vas-198-icon">' + icon('posting') + '</span>' +
                        '<div class="vas-198-head-text">' +
                            '<div class="vas-198-title" title="' + escapeHtml(title) + '">' + escapeHtml(title) + '</div>' +
                            '<div class="vas-198-subtitle" title="' + escapeHtml(subtitle) + '">' + escapeHtml(subtitle) + '</div>' +
                        '</div>' +
                        /* Period chip: the widget's only filter. It names the period
                           every figure on the card belongs to, and opens the picker. */
                        '<button type="button" class="vas-198-periodchip" aria-haspopup="listbox">' +
                            icon('calendar') +
                            '<span class="vas-198-periodchip-label"></span>' +
                            icon('chevDown') +
                        '</button>' +
                    '</div>' +
                    '<div class="vas-198-body">' +
                        /* Column header and rows share ONE grid template, so the
                           count and value columns line up down the card. */
                        '<div class="vas-198-thead">' +
                            '<span class="vas-198-cell" title="' + escapeHtml(typeCap) + '">' +
                                escapeHtml(typeCap) + '</span>' +
                            '<span class="vas-198-cell vas-198-cell-num" title="' + escapeHtml(docsCap) + '">' +
                                escapeHtml(docsCap) + '</span>' +
                            '<span class="vas-198-cell vas-198-cell-num" title="' + escapeHtml(valueCap) + '">' +
                                escapeHtml(valueCap) + '</span>' +
                            '<span class="vas-198-cell-chev"></span>' +
                        '</div>' +
                        '<div class="vas-198-list"></div>' +
                        '<div class="vas-198-foot"></div>' +
                    '</div>' +
                '</div>'
            );

            $list = $card.find('.vas-198-list');
            $foot = $card.find('.vas-198-foot');
            $periodBtn = $card.find('.vas-198-periodchip');

            $periodBtn.on('click', function (e) {
                e.stopPropagation();
                togglePicker();
            });

            /* Delegated so the handlers survive every repaint of the list. */
            $list.on('click', '.vas-198-row', function () {
                var $row = $(this);
                openModal(parseInt($row.attr('data-table'), 10) || 0,
                    parseInt($row.attr('data-window'), 10) || 0,
                    $row.attr('data-complement') === '1',
                    $row.attr('data-name') || '');
            });
            $list.on('keydown', '.vas-198-row', function (e) {
                if (e.key === 'Enter' || e.keyCode === 13 || e.key === ' ' || e.keyCode === 32) {
                    e.preventDefault();
                    $(this).trigger('click');
                }
            });

            $foot.on('click', '.vas-198-pgbtn', function () {
                var $btn = $(this);
                if ($btn.prop('disabled')) { return; }
                _cardPage = $btn.attr('data-dir') === 'next' ? _cardPage + 1 : Math.max(1, _cardPage - 1);
                paintList();
            });

            $root.append($card);

            $busy = $('<div class="vas-198-busy vas-198-hidden"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $root.append($busy);

            observeList();
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
                url: VIS.Application.contextUrl + 'VAS_198_UnPostedAccountEntriesWidget/GetBootstrap',
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
                url: VIS.Application.contextUrl + 'VAS_198_UnPostedAccountEntriesWidget/GetPeriodData',
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
            _cardPage = 1;
            _cardNeedsSync = true;
            paintList();
            scheduleSync();
        }

        // ── Card rendering ───────────────────────────────────────────────────

        function paintPeriod() {
            var text = _periodName || '—';
            $periodBtn.find('.vas-198-periodchip-label').text(text);
            $periodBtn.attr('title', text);
            /* Nothing to pick when the tenant has no open period at all. */
            $periodBtn.toggleClass('vas-198-hidden', _periods.length === 0);
        }

        function renderState(text, isError) {
            if (!$list) { return; }
            $list.html('<div class="vas-198-state' + (isError ? ' vas-198-state-error' : '') + '">' +
                escapeHtml(text) + '</div>');
            $foot.empty();
        }

        function sources() {
            return (_data && _data.Sources) ? _data.Sources : [];
        }

        function paintList() {
            if (!$list) { return; }

            var all = sources();
            if (all.length === 0) {
                renderState(label('VAS_198_NothingUnposted', 'Nothing unposted in this period.'), false);
                return;
            }

            var pageSize = Math.max(CARD_MIN_ROWS, _cardPageSize);
            var totalPages = Math.max(1, Math.ceil(all.length / pageSize));
            if (_cardPage > totalPages) { _cardPage = totalPages; }
            if (_cardPage < 1) { _cardPage = 1; }

            var from = (_cardPage - 1) * pageSize;
            var to = Math.min(all.length, from + pageSize);

            var symbol = _data ? _data.BaseCurrencySymbol : '';
            var iso = _data ? _data.BaseCurrencyIso : '';
            var precision = _data ? _data.BaseCurrencyPrecision : 2;
            var noValueTip = label('VAS_198_NoAmountStrategy',
                'No accounting value for this transaction type');

            var html = '';
            for (var i = from; i < to; i++) {
                var src = all[i];
                var name = src.DisplayName || '';
                var count = formatCount(src.RecordCount);

                /* A type with no trusted amount strategy prints a dash, and the
                   tooltip says why - never a zero, which would read as "nothing at
                   stake" when the truth is "not knowable from this table". */
                var value = src.HasValue ? formatCompact(src.BaseValue, symbol, iso, precision) : '—';
                var valueTitle = src.HasValue ? value : noValueTip;

                /* A row is a SCREEN - "Purchase Order", not "C_Order" - so the table
                   and the window both travel with it and both go back when it is
                   opened. One table can be several rows. */
                html += '<div class="vas-198-row" role="button" tabindex="0" data-table="' + src.AD_Table_ID +
                        '" data-window="' + (src.AD_Window_ID || 0) +
                        '" data-complement="' + (src.IsComplement ? '1' : '0') +
                        '" data-name="' + escapeHtml(name) + '" aria-label="' + escapeHtml(name) + '">' +
                    '<span class="vas-198-cell vas-198-cell-name" title="' + escapeHtml(name) + '">' +
                        escapeHtml(name) + '</span>' +
                    '<span class="vas-198-cell vas-198-cell-num vas-198-tone-warn" title="' +
                        escapeHtml(count) + '">' + escapeHtml(count) + '</span>' +
                    '<span class="vas-198-cell vas-198-cell-num vas-198-cell-value" title="' +
                        escapeHtml(valueTitle) + '">' + escapeHtml(value) + '</span>' +
                    '<span class="vas-198-cell-chev">' + icon('chevR') + '</span>' +
                '</div>';
            }

            $list.html(html);
            paintFooter(all.length, totalPages);
        }

        /* Footer: what is outstanding across every type on the left - the figure the
           card exists to surface, and NOT a re-count of the page - and the canonical
           pager on the right when the types do not fit one page. */
        function paintFooter(typeCount, totalPages) {
            var docs = Number(_data && _data.TotalRecordCount ? _data.TotalRecordCount : 0);
            var hint = formatCount(docs) + ' ' + label('VAS_198_DocumentsNotPosted', 'documents not posted');

            var html = '<span class="vas-198-foot-info" title="' + escapeHtml(hint) + '">' +
                escapeHtml(hint) + '</span>';

            if (totalPages > 1) {
                var ofTxt = label('VAS_026_Of', 'of');
                var prevDis = _cardPage <= 1 ? ' disabled' : '';
                var nextDis = _cardPage >= totalPages ? ' disabled' : '';

                html += '<span class="vas-198-pager-nav">' +
                    '<button type="button" class="vas-198-pgbtn" data-dir="prev" aria-label="' +
                        escapeHtml(label('VAS_026_Prev', 'Previous')) + '"' + prevDis + '>' + icon('chevL') + '</button>' +
                    '<span class="vas-198-pager-label">' + _cardPage + ' ' + escapeHtml(ofTxt) + ' ' + totalPages + '</span>' +
                    '<button type="button" class="vas-198-pgbtn" data-dir="next" aria-label="' +
                        escapeHtml(label('VAS_026_Next', 'Next')) + '"' + nextDis + '>' + icon('chevNext') + '</button>' +
                '</span>';
            }

            $foot.html(html);
        }

        // ── Adaptive row count for the type list ─────────────────────────────

        function scheduleSync() {
            var raf = window.requestAnimationFrame || function (cb) { return window.setTimeout(cb, 16); };
            raf(function () { syncCapacity(); });
        }

        /* How many type rows the cell actually fits, measured from the painted rows
           rather than assumed - the row height follows the host's font scale and the
           widget root clamp, so a constant would be wrong on half the dashboards. */
        function syncCapacity() {
            if (!$list || !$list[0]) { return; }
            if (sources().length === 0) { return; }

            var avail = $list[0].clientHeight;
            if (avail <= 0) {
                if (_cardNeedsSync) { scheduleSync(); }     // layout not settled yet - retry
                return;
            }

            var painted = $list[0].querySelectorAll('.vas-198-row');
            var maxH = 0;
            for (var i = 0; i < painted.length; i++) {
                if (painted[i].offsetHeight > maxH) { maxH = painted[i].offsetHeight; }
            }
            if (maxH > 0) { _cardRowH = maxH; }
            var rowH = _cardRowH > 0 ? _cardRowH : CARD_ROW_FALLBACK;

            _cardNeedsSync = false;
            var capacity = Math.max(CARD_MIN_ROWS, Math.floor(avail / rowH));
            if (capacity !== _cardPageSize) {
                _cardPageSize = capacity;
                paintList();
            }
        }

        function observeList() {
            if (typeof ResizeObserver === 'undefined' || !$list || !$list[0]) { return; }
            if (_listObserver) { _listObserver.disconnect(); }
            _listObserver = new ResizeObserver(function () {
                _cardNeedsSync = true;
                syncCapacity();
            });
            _listObserver.observe($list[0]);
        }

        // ── Period picker ────────────────────────────────────────────────────

        /* Anchored under the chip and appended to <body>. Non-modal, and unlike the
           detail modal it closes on an outside click - it is a menu, not a dialog. */
        function buildPicker() {
            $picker = $('<div class="vas-198-pp vas-198-hidden" role="listbox" aria-label="' +
                escapeHtml(label('VAS_201_DashboardPeriod', 'Dashboard period')) + '">');
            $('body').append($picker);

            $picker.on('click', '.vas-198-pp-opt', function () {
                var id = parseInt($(this).attr('data-id'), 10) || 0;
                closePicker();
                selectPeriod(id);
            });
        }

        function fillPicker() {
            var html = '<div class="vas-198-pp-h">' +
                escapeHtml(label('VAS_201_DashboardPeriod', 'Dashboard period')) + '</div>';

            for (var i = 0; i < _periods.length; i++) {
                var p = _periods[i];
                var selected = p.C_Period_ID === _periodId;
                var meta = p.FiscalYear ? String(p.FiscalYear) : '';

                html += '<button type="button" class="vas-198-pp-opt" role="option" data-id="' + p.C_Period_ID +
                        '" aria-selected="' + (selected ? 'true' : 'false') + '">' +
                    '<span class="vas-198-pp-name" title="' + escapeHtml(p.Name || '') + '">' +
                        escapeHtml(p.Name || '') + '</span>' +
                    (meta ? '<span class="vas-198-pp-meta">' + escapeHtml(meta) + '</span>' : '') +
                    '<span class="vas-198-pp-tick">' + icon('tick') + '</span>' +
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
            $picker.removeClass('vas-198-hidden');
            positionPicker();
            _pickerOpen = true;

            $(document).on('click' + _ns, onDocumentClick);
            $(document).on('keydown' + _ns, onPickerKeyDown);
            $(window).on('resize' + _ns + ' scroll' + _ns, closePicker);
        }

        function closePicker() {
            if (!_pickerOpen) { return; }
            _pickerOpen = false;
            if ($picker) { $picker.addClass('vas-198-hidden'); }

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
                '<div class="vas-198-overlay vas-198-hidden">' +
                    '<div class="vas-198-modal" role="dialog" aria-modal="true">' +
                        '<div class="vas-198-modal-head">' +
                            '<span class="vas-198-modal-ico">' + icon('posting') + '</span>' +
                            '<div class="vas-198-modal-heads">' +
                                '<div class="vas-198-modal-title"></div>' +
                                '<div class="vas-198-modal-sub"></div>' +
                            '</div>' +
                            '<button type="button" class="vas-198-modal-close" aria-label="' +
                                escapeHtml(label('VAS_018_Close', 'Close')) + '">' + icon('close') + '</button>' +
                        '</div>' +
                        '<div class="vas-198-modal-body"></div>' +
                        '<div class="vas-198-modal-foot"></div>' +
                        /* Sits over the panel, not over the body, so the header and
                           the pager stay readable while a page is in flight. */
                        '<div class="vas-198-modal-busy vas-198-hidden">' +
                            '<div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div>' +
                        '</div>' +
                    '</div>' +
                '</div>'
            );

            $('body').append($overlay);
            $modalBody = $overlay.find('.vas-198-modal-body');
            $modalPager = $overlay.find('.vas-198-modal-foot');
            $modalBusy = $overlay.find('.vas-198-modal-busy');

            /* The body holds exactly one page, so its height is FIXED - not merely
               floored. syncModalHeight() grows it to the measured height of a real
               full page on the first paint. */
            _bodyH = (MODAL_ROW_H * MODAL_PAGE_SIZE) + MODAL_HEAD_H + MODAL_BODY_SLACK;
            $modalBody.css('height', _bodyH + 'px');

            /* The close button, and Escape - deliberately NOT a click on the scrim. This
               dialog is a work list a reader pages through, so a stray click landing off
               the panel is far more likely to be a missed target than a decision to leave,
               and losing the page to it has nothing to undo it with. */
            $overlay.find('.vas-198-modal-close').on('click', closeModal);

            $modalPager.on('click', '.vas-198-pgbtn', function () {
                var $btn = $(this);
                if ($btn.prop('disabled')) { return; }
                _page = $btn.attr('data-dir') === 'next' ? _page + 1 : Math.max(1, _page - 1);
                loadModalPage();
            });

            /* The document number IS the zoom affordance - it opens that record in
               the very window the Posted field was discovered on. */
            $modalBody.on('click', '.vas-198-doclink', function (e) {
                e.stopPropagation();
                var $link = $(this);
                zoomTo($link.attr('data-column'),
                    parseInt($link.attr('data-id'), 10) || 0,
                    parseInt($link.attr('data-window'), 10) || 0);
            });
        }

        function openModal(tableId, windowId, isComplement, typeName) {
            if (!(tableId > 0) || _periodId <= 0) { return; }
            if (!$overlay) { buildModal(); }

            _tableId = tableId;
            _windowId = windowId || 0;
            _isComplement = !!isComplement;
            _typeName = typeName || '';
            _page = 1;

            $overlay.find('.vas-198-modal-title').text(_typeName);
            $overlay.find('.vas-198-modal-sub').text(_periodName || '');

            /* Nothing of the previous type may show through: the body opens empty
               (at its pinned height) with the indicator over it. */
            $modalBody.empty();
            $modalPager.empty();

            $overlay.removeClass('vas-198-hidden');
            _modalOpen = true;
            closePicker();

            $(document).on('keydown' + _ns + 'm', onModalKeyDown);

            loadModalPage();
        }

        function closeModal() {
            if (!_modalOpen) { return; }
            _modalOpen = false;
            _detailSeq++;                       // drop any page still in flight
            showModalBusy(false);
            if ($overlay) { $overlay.addClass('vas-198-hidden'); }
            $(document).off('keydown' + _ns + 'm');
        }

        function onModalKeyDown(e) {
            if (e.key === 'Escape' || e.keyCode === 27) { closeModal(); }
        }

        function showModalBusy(show) {
            if (!$modalBusy || !$modalBusy[0]) { return; }
            $modalBusy.toggleClass('vas-198-hidden', !show);
        }

        /* One page at a time from the server - the modal never receives the whole
           set. The previous page stays painted underneath the busy indicator, so the
           panel neither blanks nor resizes while the next one arrives. */
        function loadModalPage() {
            var mySeq = ++_detailSeq;
            var periodId = _periodId;

            showModalBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_198_UnPostedAccountEntriesWidget/GetRecords',
                type: 'GET',
                cache: false,
                data: {
                    periodId: periodId,
                    tableId: _tableId,
                    windowId: _windowId,
                    isComplement: _isComplement,
                    pageNo: _page,
                    pageSize: MODAL_PAGE_SIZE
                },
                success: function (res) {
                    /* Stale response: another page, another type, another period, or
                       the modal has been closed since. */
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
            $modalBody.html('<div class="vas-198-modal-state' + (isError ? ' vas-198-modal-state-error' : '') +
                '">' + escapeHtml(text) + '</div>');
            renderModalPager({ Total: 0, PageSize: MODAL_PAGE_SIZE, PageNo: 1, Rows: [] });
        }

        /* One list shape serves every transaction type - that is the whole point of
           normalising them at discovery - except that a column with nothing behind it
           is DROPPED rather than printed blank. An inventory movement has no document
           currency and no document type; a column of empty cells would read as
           missing data instead of as "not applicable".

           The date column is captioned from the column the SERVER actually bounded
           by: most types are dated by DateAcct, but an inventory type that has none
           is dated by MovementDate, and captioning that "Account Date" would be a
           quiet lie about which date the user is reading. */
        function columnsFor(data) {
            var dateColumn = data.DateColumn || 'DateAcct';

            var captions = [
                colLabel('DocumentNo', 'VAS_198_DocumentNo', 'Document No'),
                colLabel(dateColumn, 'VAS_200_DateAcct', 'Account Date')
            ];

            if (data.HasDocType) {
                captions.push(colLabel('C_DocType_ID', 'VIS_DocumentType', 'Document Type'));
            }
            if (data.HasCurrency) {
                captions.push(colLabel('C_Currency_ID', 'VAS_201_Currency', 'Currency'));
            }
            if (data.HasCreatedBy) {
                captions.push(colLabel('CreatedBy', 'VAS_198_CreatedBy', 'Created By'));
            }

            captions.push(label('VAS_198_DocumentAmount', 'Document Amount'));

            return captions;
        }

        /* Which columns are numeric - they right-align. The amount is always last,
           however many optional columns came before it. */
        function numericColumns(captionCount) {
            var numeric = {};
            numeric[captionCount - 1] = true;
            return numeric;
        }

        /* Grid variant. Three optional columns make four possible shapes, but what the
           stylesheet needs is the column COUNT - the proportions that suit five
           columns suit them whichever five they are - so the variant is named for it.
           Four to six; the two mandatory columns and the amount are always there. */
        function gridVariant(captionCount) {
            return 'vas-198-dgrid-c' + captionCount;
        }

        function renderModalRows(data) {
            var rows = data.Rows || [];

            if (rows.length === 0) {
                renderModalState(label('VAS_201_NoRecords', 'No records in this category.'), false);
                return;
            }

            var captions = columnsFor(data);
            var numeric = numericColumns(captions.length);

            var head = '<div class="vas-198-dhead">';
            for (var c = 0; c < captions.length; c++) {
                head += cell(captions[c], numeric[c] ? 'vas-198-dcell-num' : '');
            }
            head += '</div>';

            var body = '';
            for (var i = 0; i < rows.length; i++) {
                body += buildDetailRow(rows[i], data);
            }

            $modalBody.html('<div class="vas-198-dgrid ' + gridVariant(captions.length) + '">' +
                head + body + '</div>');
            $modalBody.scrollTop(0);

            syncModalHeight(rows.length);
            renderModalPager(data);
        }

        function cell(text, extraClass) {
            return '<span class="vas-198-dcell' + (extraClass ? ' ' + extraClass : '') +
                '" title="' + escapeHtml(text) + '">' + escapeHtml(text) + '</span>';
        }

        /* A document number rendered as a link. A button, not an anchor: there is no
           URL to follow - the framework opens the window. Without a window or a key
           the number stays plain text rather than offering a dead link. */
        function docCell(row, data) {
            var text = row.DocumentNo || '';
            var windowId = Number(data.AD_Window_ID || 0);
            var keyColumn = data.KeyColumn || '';

            if (!(row.Record_ID > 0) || !(windowId > 0) || !keyColumn) {
                return cell(text, 'vas-198-dcell-b');
            }

            return '<span class="vas-198-dcell vas-198-dcell-doc">' +
                '<button type="button" class="vas-198-doclink" data-column="' + escapeHtml(keyColumn) +
                    '" data-id="' + row.Record_ID + '" data-window="' + windowId +
                    '" title="' + escapeHtml(text) + '">' +
                    escapeHtml(text) + '</button>' +
            '</span>';
        }

        function buildDetailRow(row, data) {
            /* The document's amount in its OWN currency - the figure printed on the
               document the user is about to open. The card's totals are the converted
               ones; a record list is not where currencies get added together.

               A type with no currency column has no document currency to label it
               with: it is valued from stored costs that are base currency already, so
               it carries the base symbol. That is the truth about such a document,
               not a rounding of it.

               A type with no trusted amount strategy prints a dash, matching the card
               rather than contradicting it with a zero. */
            var symbol = data.HasCurrency
                ? (row.CurrencySymbol || row.CurrencyIso || '')
                : data.BaseCurrencySymbol;
            var precision = data.HasCurrency ? row.CurrencyPrecision : data.BaseCurrencyPrecision;

            var documentAmount = data.HasValue
                ? formatAmount(row.DocumentValue, symbol, precision)
                : '—';

            /* Cells are emitted in exactly the order columnsFor() names them, and each
               optional one is gated on the same flag - the two must never drift, or
               every cell after the gap lands under the wrong caption. */
            var html = '<div class="vas-198-drow">' +
                docCell(row, data) +
                cell(formatDate(row.DateAcct));

            if (data.HasDocType) { html += cell(row.DocumentType || ''); }
            if (data.HasCurrency) { html += cell(row.CurrencyIso || ''); }
            if (data.HasCreatedBy) { html += cell(row.CreatedByName || ''); }

            return html +
                cell(documentAmount, 'vas-198-dcell-num') +
            '</div>';
        }

        /* Grows the fixed body to whatever a FULL page actually measures, so the last
           page never introduces a scrollbar the first page did not have. Only ever
           grows: shrinking back on a short page is the fluctuation this prevents. */
        function syncModalHeight(rowCount) {
            if (!$modalBody || !$modalBody[0] || rowCount <= 0) { return; }

            var headEl = $modalBody[0].querySelector('.vas-198-dhead');
            var rowEls = $modalBody[0].querySelectorAll('.vas-198-drow');
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
                '<span class="vas-198-pager-info" title="' + escapeHtml(showing) + '">' +
                    escapeHtml(showing) + '</span>' +
                '<span class="vas-198-pager-nav">' +
                    '<button type="button" class="vas-198-pgbtn" data-dir="prev" aria-label="' +
                        escapeHtml(label('VAS_026_Prev', 'Previous')) + '"' + prevDis + '>' + icon('chevL') + '</button>' +
                    '<span class="vas-198-pager-label">' + pageNo + ' ' + escapeHtml(ofTxt) + ' ' + totalPages + '</span>' +
                    '<button type="button" class="vas-198-pgbtn" data-dir="next" aria-label="' +
                        escapeHtml(label('VAS_026_Next', 'Next')) + '"' + nextDis + '>' + icon('chevNext') + '</button>' +
                '</span>'
            );
        }

        /* Opens one record in the standard window its Posted field was discovered
           on. The window id came from the server with the page, so there is no name
           lookup and no hard-coded AD_Window_ID anywhere in this widget. The dialog
           closes first: the record opens in its own window, and leaving the modal
           over it would hide what was just opened. Degrades silently - a click can
           never throw. */
        function zoomTo(keyColumn, recordId, windowId) {
            if (!keyColumn || recordId <= 0 || windowId <= 0 || !VAS.ZoomUtil) { return; }

            closeModal();

            try {
                VAS.ZoomUtil.zoomToRecord(keyColumn, recordId, windowId, '', '');
            } catch (e) {
                if (window.console) { console.log(e); }
                showError(label('VAS_192_CouldntLoad', "Couldn't load"));
            }
        }

        // ── Framework contract ───────────────────────────────────────────────

        this.refreshWidget = function () {
            /* Refresh means "start clean": drop the open panels and re-read the
               period list, because a period may have been opened or closed in the
               Period Control widgets since this card last loaded - and a document
               may have been posted, which is the whole point of the card. */
            closePicker();
            closeModal();

            _periods = [];
            _periodId = 0;
            _periodName = '';
            _data = null;
            _cardPage = 1;

            loadBootstrap();
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            if ($list) { $list.off(); }
            if ($foot) { $foot.off(); }
            if ($periodBtn) { $periodBtn.off(); }

            if (_listObserver) {
                _listObserver.disconnect();
                _listObserver = null;
            }

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

    VAS.VAS_198_UnPostedAccountEntriesWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_198_UnPostedAccountEntriesWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_198_UnPostedAccountEntriesWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_198_UnPostedAccountEntriesWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_198_UnPostedAccountEntriesWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
