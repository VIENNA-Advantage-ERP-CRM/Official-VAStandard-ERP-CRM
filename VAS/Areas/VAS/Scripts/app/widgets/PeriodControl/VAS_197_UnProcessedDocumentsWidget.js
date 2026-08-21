/**
 * VAS_197_UnProcessedDocumentsWidget
 * 3x2 grid widget for the Period Control dashboard.
 *
 * Every document of ONE open period that has NOT reached a settled state - whose
 * DocStatus is not Completed, Closed, Reversed or Voided - grouped by the screen
 * it belongs to:
 *
 *   Sales Invoice                          $48,200
 *   5 docs · oldest 03 Apr
 *   Purchase Invoice                       $31,900
 *   4 docs · oldest 06 Apr
 *
 * The sibling of VAS_198_UnPostedAccountEntries, and deliberately its mirror
 * image: that card chases documents that are finished but not yet in the ledger,
 * this one chases documents that are not finished at all.
 *
 * Clicking a row opens a paged record list; the document number is a link that
 * opens that record in its own standard window.
 *
 * The screens are NOT hard-coded. The server discovers them from the Application
 * Dictionary - every active physical table with a DocStatus column, since that
 * column IS the definition of a document that can be open - and a row is one of
 * the WINDOWS that table appears on, not the table. So C_Invoice arrives as AP
 * Invoice / AR Invoice / Expense Invoice and C_Order as Purchase Order / Sales
 * Order / RMA, named as the tenant named its windows. A module that adds a
 * document screen appears here without a code change.
 *
 * A row is therefore a table AND a window, and that pair is the whole of what this
 * widget holds and the whole of what it sends back. What separates two screens
 * over one table is that screen's own record filter, which lives on the server and
 * is never sent here. Zoom needs no rule of its own: a row IS a screen, so it
 * opens that screen.
 *
 * Each row carries its count AND the age of its oldest item, because a queue of
 * five that has waited a month is a different problem from a queue of five raised
 * this morning. A screen whose value cannot be established from a trusted amount
 * strategy shows a dash rather than a figure nobody can vouch for.
 *
 * Period source: the OPEN periods of the tenant's primary calendar - a period
 * qualifies when at least one active C_PeriodControl row of it is Open. Nothing is
 * derived from the calendar month. Each screen is bounded by its own DateAcct, or
 * by MovementDate where it has none - which is why the record list captions its
 * date column from the column the server actually used.
 *
 * The number of screens is data, not a constant, so the card pages: it measures
 * its own list and shows as many rows as the cell actually fits, with the
 * canonical footer pager for the rest. Nothing inside the card scrolls.
 *
 * Sizing follows design.md -> dashboard-widgets.md: the card carries the widget
 * root anchor clamp, the header reads --dash-inline-size (populated by
 * ensureDashInlineSizeVar), the record grid shares one column template between its
 * header and its rows, and nothing scrolls. Narrow cells are handled by container
 * queries, the body-level modal and picker by media queries (see the stylesheet).
 *
 * Summary Message Table
 * Rows marked (reuse) already exist in the project under another key and are
 * NOT duplicated here. Column captions prefer the framework's own translated
 * element name (VIS.translatedTexts[<ColumnName>]) and only fall back to the key.
 *  # | Current Text                             | Message Key
 * ---+------------------------------------------+---------------------------------
 *  1 | Open / Unprocessed Documents             | VAS_197_OpenUnprocessedDocuments
 *  2 | Not completed · click a row for records  | VAS_197_NotCompletedHint
 *  3 | docs                                     | VAS_197_Docs
 *  4 | oldest                                   | VAS_197_Oldest
 *  5 | documents not completed                  | VAS_197_DocumentsNotCompleted
 *  6 | Nothing open in this period              | VAS_197_NothingOpen
 *  7 | Status                                   | VAS_197_Status
 *  8 | Document Amount                          | VAS_198_DocumentAmount    (reuse)
 *  9 | No accounting value for this transaction type (tooltip) | VAS_198_NoAmountStrategy (reuse)
 * 10 | Account Date                             | VAS_200_DateAcct          (reuse)
 * 11 | Business Partner                         | VAS_201_BusinessPartner   (reuse)
 * 12 | Document Type                            | VIS_DocumentType          (reuse)
 * 13 | No open accounting period                | VAS_201_NoOpenPeriod      (reuse)
 * 14 | Dashboard period                         | VAS_201_DashboardPeriod   (reuse)
 * 15 | No records in this category              | VAS_201_NoRecords         (reuse)
 * 16 | Document No                              | DocumentNo                (reuse)
 * 17 | Close                                    | VAS_018_Close             (reuse)
 * 18 | Couldn't load                            | VAS_192_CouldntLoad       (reuse)
 * 19 | Showing                                  | VAS_026_Showing           (reuse)
 * 20 | of                                       | VAS_026_Of                (reuse)
 * 21 | Previous                                 | VAS_026_Prev              (reuse)
 * 22 | Next                                     | VAS_026_Next              (reuse)
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

    /* Card list: how many screen rows fit is measured, not assumed. The fallback
       row height only has to survive the very first paint, and the floor keeps the
       card showing something even if the cell collapses to nothing. These rows are
       TWO lines, so the fallback is taller than the sibling widget's. */
    var CARD_ROW_FALLBACK = 48;
    var CARD_MIN_ROWS = 2;

    VAS.VAS_197_UnProcessedDocumentsWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas-197-root">');
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
        var _ns = '.vas197_' + (VAS.VAS_197_UnProcessedDocumentsWidget._seq =
            (VAS.VAS_197_UnProcessedDocumentsWidget._seq || 0) + 1);

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
        var _screenName = '';
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
            $busy.toggleClass('vas-197-hidden', _busyCount === 0);
        }

        function showError(text) {
            if (VIS && VIS.ADialog && VIS.ADialog.error) { VIS.ADialog.error("", "", text); }
        }

        /* Counts are never blank: an empty screen reads as 0, not as nothing. */
        function formatCount(value) {
            var n = Number(value || 0);
            if (!isFinite(n)) { n = 0; }
            return Math.round(n).toLocaleString(window.navigator.language);
        }

        /* Card money: the shared compact formatter, so a seven-figure queue does not
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

        /* The card's "oldest 03 Apr": day and month only. A year would be noise -
           every row belongs to the one period named in the header. */
        function formatDayMonth(value) {
            if (!value) { return ''; }
            var d = new Date(value);
            if (isNaN(d.getTime())) { return String(value); }

            try {
                return d.toLocaleDateString(window.navigator.language, { day: 'numeric', month: 'short' });
            } catch (e) {
                return d.toLocaleDateString(window.navigator.language);
            }
        }

        /* A stored list code rendered as its translated name. The map comes from
           AD_Ref_List with the request, so a code is never printed raw unless the
           dictionary has no entry for it at all. */
        function refName(map, code) {
            if (!code) { return ''; }
            if (map && map[code]) { return map[code]; }
            return code;
        }

        function icon(name) {
            if (name === 'chevR') { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="9 6 15 12 9 18"/></svg>'; }
            if (name === 'chevL') { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><polyline points="15 18 9 12 15 6"/></svg>'; }
            if (name === 'chevNext') { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><polyline points="9 18 15 12 9 6"/></svg>'; }
            if (name === 'chevDown') { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><polyline points="6 9 12 15 18 9"/></svg>'; }
            if (name === 'calendar') { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="4" width="18" height="17" rx="2"/><line x1="3" y1="9" x2="21" y2="9"/><line x1="8" y1="2" x2="8" y2="6"/><line x1="16" y1="2" x2="16" y2="6"/></svg>'; }
            if (name === 'close') { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><line x1="6" y1="6" x2="18" y2="18"/><line x1="18" y1="6" x2="6" y2="18"/></svg>'; }
            if (name === 'tick') { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round"><polyline points="20 6 9 17 4 12"/></svg>'; }
            /* Document with a folded corner: the widget's own glyph, from the
               reference design - paperwork still on the desk. */
            if (name === 'doc') { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/></svg>'; }
            return '';
        }

        // ── Build ────────────────────────────────────────────────────────────

        this.Initalize = function () {
            createWidget();
            setupResizeObserver();
            loadBootstrap();
        };

        function createWidget() {
            var title = label('VAS_197_OpenUnprocessedDocuments', 'Open / Unprocessed Documents');
            var subtitle = label('VAS_197_NotCompletedHint', 'Not completed · click a row for records');

            /* No column header row here, unlike the sibling widget: these rows are
               two lines - a name over its own count and age - and a header cannot
               caption a composite cell without lying about one of its halves. */
            $card = $(
                '<div class="vas-197-card vas-widget-bg" role="group" aria-label="' + escapeHtml(title) + '">' +
                    '<div class="vas-197-header">' +
                        '<span class="vas-197-icon">' + icon('doc') + '</span>' +
                        '<div class="vas-197-head-text">' +
                            '<div class="vas-197-title" title="' + escapeHtml(title) + '">' + escapeHtml(title) + '</div>' +
                            '<div class="vas-197-subtitle" title="' + escapeHtml(subtitle) + '">' + escapeHtml(subtitle) + '</div>' +
                        '</div>' +
                        /* Period chip: the widget's only filter. It names the period
                           every figure on the card belongs to, and opens the picker. */
                        '<button type="button" class="vas-197-periodchip" aria-haspopup="listbox">' +
                            icon('calendar') +
                            '<span class="vas-197-periodchip-label"></span>' +
                            icon('chevDown') +
                        '</button>' +
                    '</div>' +
                    '<div class="vas-197-body">' +
                        '<div class="vas-197-list"></div>' +
                        '<div class="vas-197-foot"></div>' +
                    '</div>' +
                '</div>'
            );

            $list = $card.find('.vas-197-list');
            $foot = $card.find('.vas-197-foot');
            $periodBtn = $card.find('.vas-197-periodchip');

            $periodBtn.on('click', function (e) {
                e.stopPropagation();
                togglePicker();
            });

            /* Delegated so the handlers survive every repaint of the list. */
            $list.on('click', '.vas-197-row', function () {
                var $row = $(this);
                openModal(parseInt($row.attr('data-table'), 10) || 0,
                    parseInt($row.attr('data-window'), 10) || 0,
                    $row.attr('data-name') || '');
            });
            $list.on('keydown', '.vas-197-row', function (e) {
                if (e.key === 'Enter' || e.keyCode === 13 || e.key === ' ' || e.keyCode === 32) {
                    e.preventDefault();
                    $(this).trigger('click');
                }
            });

            $foot.on('click', '.vas-197-pgbtn', function () {
                var $btn = $(this);
                if ($btn.prop('disabled')) { return; }
                _cardPage = $btn.attr('data-dir') === 'next' ? _cardPage + 1 : Math.max(1, _cardPage - 1);
                paintList();
            });

            $root.append($card);

            $busy = $('<div class="vas-197-busy vas-197-hidden"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
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
                url: VIS.Application.contextUrl + 'VAS_197_UnProcessedDocumentsWidget/GetBootstrap',
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
                url: VIS.Application.contextUrl + 'VAS_197_UnProcessedDocumentsWidget/GetPeriodData',
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
            $periodBtn.find('.vas-197-periodchip-label').text(text);
            $periodBtn.attr('title', text);
            /* Nothing to pick when the tenant has no open period at all. */
            $periodBtn.toggleClass('vas-197-hidden', _periods.length === 0);
        }

        function renderState(text, isError) {
            if (!$list) { return; }
            $list.html('<div class="vas-197-state' + (isError ? ' vas-197-state-error' : '') + '">' +
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
                renderState(label('VAS_197_NothingOpen', 'Nothing open in this period.'), false);
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

            var docsTxt = label('VAS_197_Docs', 'docs');
            var oldestTxt = label('VAS_197_Oldest', 'oldest');
            var noValueTip = label('VAS_198_NoAmountStrategy',
                'No accounting value for this transaction type');

            var html = '';
            for (var i = from; i < to; i++) {
                var src = all[i];
                var name = src.DisplayName || '';

                /* The second line is the whole reason these rows are two lines: a
                   count says how much is outstanding, the age of the oldest item says
                   how badly. The age is dropped when the server had no date to give,
                   rather than printed as an empty half. */
                var meta = formatCount(src.RecordCount) + ' ' + docsTxt;
                var oldest = formatDayMonth(src.OldestDate);
                if (oldest) { meta += ' · ' + oldestTxt + ' ' + oldest; }

                /* A screen with no trusted amount strategy prints a dash, and the
                   tooltip says why - never a zero, which would read as "nothing at
                   stake" when the truth is "not knowable from this table". */
                var value = src.HasValue ? formatCompact(src.BaseValue, symbol, iso, precision) : '—';
                var valueTitle = src.HasValue ? value : noValueTip;

                /* A row is a SCREEN - "Purchase Order", not "C_Order" - so the table
                   and the window both travel with it and both go back when it is
                   opened. One table can be several rows. */
                html += '<div class="vas-197-row" role="button" tabindex="0" data-table="' + src.AD_Table_ID +
                        '" data-window="' + (src.AD_Window_ID || 0) +
                        '" data-name="' + escapeHtml(name) + '" aria-label="' + escapeHtml(name) + '">' +
                    '<span class="vas-197-cell-main">' +
                        '<span class="vas-197-cell-name" title="' + escapeHtml(name) + '">' +
                            escapeHtml(name) + '</span>' +
                        '<span class="vas-197-cell-meta" title="' + escapeHtml(meta) + '">' +
                            escapeHtml(meta) + '</span>' +
                    '</span>' +
                    '<span class="vas-197-cell-value" title="' + escapeHtml(valueTitle) + '">' +
                        escapeHtml(value) + '</span>' +
                    '<span class="vas-197-cell-chev">' + icon('chevR') + '</span>' +
                '</div>';
            }

            $list.html(html);
            paintFooter(totalPages);
        }

        /* Footer: what is outstanding across every screen on the left - the figure
           the card exists to surface, and NOT a re-count of the page - and the
           canonical pager on the right when the screens do not fit one page. */
        function paintFooter(totalPages) {
            var docs = Number(_data && _data.TotalRecordCount ? _data.TotalRecordCount : 0);
            var hint = formatCount(docs) + ' ' +
                label('VAS_197_DocumentsNotCompleted', 'documents not completed');

            var html = '<span class="vas-197-foot-info" title="' + escapeHtml(hint) + '">' +
                escapeHtml(hint) + '</span>';

            if (totalPages > 1) {
                var ofTxt = label('VAS_026_Of', 'of');
                var prevDis = _cardPage <= 1 ? ' disabled' : '';
                var nextDis = _cardPage >= totalPages ? ' disabled' : '';

                html += '<span class="vas-197-pager-nav">' +
                    '<button type="button" class="vas-197-pgbtn" data-dir="prev" aria-label="' +
                        escapeHtml(label('VAS_026_Prev', 'Previous')) + '"' + prevDis + '>' + icon('chevL') + '</button>' +
                    '<span class="vas-197-pager-label">' + _cardPage + ' ' + escapeHtml(ofTxt) + ' ' + totalPages + '</span>' +
                    '<button type="button" class="vas-197-pgbtn" data-dir="next" aria-label="' +
                        escapeHtml(label('VAS_026_Next', 'Next')) + '"' + nextDis + '>' + icon('chevNext') + '</button>' +
                '</span>';
            }

            $foot.html(html);
        }

        // ── Adaptive row count for the screen list ───────────────────────────

        function scheduleSync() {
            var raf = window.requestAnimationFrame || function (cb) { return window.setTimeout(cb, 16); };
            raf(function () { syncCapacity(); });
        }

        /* How many screen rows the cell actually fits, measured from the painted rows
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

            var painted = $list[0].querySelectorAll('.vas-197-row');
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
            $picker = $('<div class="vas-197-pp vas-197-hidden" role="listbox" aria-label="' +
                escapeHtml(label('VAS_201_DashboardPeriod', 'Dashboard period')) + '">');
            $('body').append($picker);

            $picker.on('click', '.vas-197-pp-opt', function () {
                var id = parseInt($(this).attr('data-id'), 10) || 0;
                closePicker();
                selectPeriod(id);
            });
        }

        function fillPicker() {
            var html = '<div class="vas-197-pp-h">' +
                escapeHtml(label('VAS_201_DashboardPeriod', 'Dashboard period')) + '</div>';

            for (var i = 0; i < _periods.length; i++) {
                var p = _periods[i];
                var selected = p.C_Period_ID === _periodId;
                var meta = p.FiscalYear ? String(p.FiscalYear) : '';

                html += '<button type="button" class="vas-197-pp-opt" role="option" data-id="' + p.C_Period_ID +
                        '" aria-selected="' + (selected ? 'true' : 'false') + '">' +
                    '<span class="vas-197-pp-name" title="' + escapeHtml(p.Name || '') + '">' +
                        escapeHtml(p.Name || '') + '</span>' +
                    (meta ? '<span class="vas-197-pp-meta">' + escapeHtml(meta) + '</span>' : '') +
                    '<span class="vas-197-pp-tick">' + icon('tick') + '</span>' +
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
            $picker.removeClass('vas-197-hidden');
            positionPicker();
            _pickerOpen = true;

            $(document).on('click' + _ns, onDocumentClick);
            $(document).on('keydown' + _ns, onPickerKeyDown);
            $(window).on('resize' + _ns + ' scroll' + _ns, closePicker);
        }

        function closePicker() {
            if (!_pickerOpen) { return; }
            _pickerOpen = false;
            if ($picker) { $picker.addClass('vas-197-hidden'); }

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
                '<div class="vas-197-overlay vas-197-hidden">' +
                    '<div class="vas-197-modal" role="dialog" aria-modal="true">' +
                        '<div class="vas-197-modal-head">' +
                            '<span class="vas-197-modal-ico">' + icon('doc') + '</span>' +
                            '<div class="vas-197-modal-heads">' +
                                '<div class="vas-197-modal-title"></div>' +
                                '<div class="vas-197-modal-sub"></div>' +
                            '</div>' +
                            '<button type="button" class="vas-197-modal-close" aria-label="' +
                                escapeHtml(label('VAS_018_Close', 'Close')) + '">' + icon('close') + '</button>' +
                        '</div>' +
                        '<div class="vas-197-modal-body"></div>' +
                        '<div class="vas-197-modal-foot"></div>' +
                        /* Sits over the panel, not over the body, so the header and
                           the pager stay readable while a page is in flight. */
                        '<div class="vas-197-modal-busy vas-197-hidden">' +
                            '<div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div>' +
                        '</div>' +
                    '</div>' +
                '</div>'
            );

            $('body').append($overlay);
            $modalBody = $overlay.find('.vas-197-modal-body');
            $modalPager = $overlay.find('.vas-197-modal-foot');
            $modalBusy = $overlay.find('.vas-197-modal-busy');

            /* The body holds exactly one page, so its height is FIXED - not merely
               floored. syncModalHeight() grows it to the measured height of a real
               full page on the first paint. */
            _bodyH = (MODAL_ROW_H * MODAL_PAGE_SIZE) + MODAL_HEAD_H + MODAL_BODY_SLACK;
            $modalBody.css('height', _bodyH + 'px');

            $overlay.find('.vas-197-modal-close').on('click', closeModal);
            /* Scrim click closes; a click inside the panel must not. */
            $overlay.on('click', function (e) { if (e.target === $overlay[0]) { closeModal(); } });

            $modalPager.on('click', '.vas-197-pgbtn', function () {
                var $btn = $(this);
                if ($btn.prop('disabled')) { return; }
                _page = $btn.attr('data-dir') === 'next' ? _page + 1 : Math.max(1, _page - 1);
                loadModalPage();
            });

            /* The document number IS the zoom affordance - it opens that record in
               the very screen the row stands for. */
            $modalBody.on('click', '.vas-197-doclink', function (e) {
                e.stopPropagation();
                var $link = $(this);
                zoomTo($link.attr('data-column'),
                    parseInt($link.attr('data-id'), 10) || 0,
                    parseInt($link.attr('data-window'), 10) || 0);
            });
        }

        function openModal(tableId, windowId, screenName) {
            if (!(tableId > 0) || _periodId <= 0) { return; }
            if (!$overlay) { buildModal(); }

            _tableId = tableId;
            _windowId = windowId || 0;
            _screenName = screenName || '';
            _page = 1;

            $overlay.find('.vas-197-modal-title').text(_screenName);
            $overlay.find('.vas-197-modal-sub').text(_periodName || '');

            /* Nothing of the previous screen may show through: the body opens empty
               (at its pinned height) with the indicator over it. */
            $modalBody.empty();
            $modalPager.empty();

            $overlay.removeClass('vas-197-hidden');
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
            if ($overlay) { $overlay.addClass('vas-197-hidden'); }
            $(document).off('keydown' + _ns + 'm');
        }

        function onModalKeyDown(e) {
            if (e.key === 'Escape' || e.keyCode === 27) { closeModal(); }
        }

        function showModalBusy(show) {
            if (!$modalBusy || !$modalBusy[0]) { return; }
            $modalBusy.toggleClass('vas-197-hidden', !show);
        }

        /* One page at a time from the server - the modal never receives the whole
           set. The previous page stays painted underneath the busy indicator, so the
           panel neither blanks nor resizes while the next one arrives. */
        function loadModalPage() {
            var mySeq = ++_detailSeq;
            var periodId = _periodId;

            showModalBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_197_UnProcessedDocumentsWidget/GetRecords',
                type: 'GET',
                cache: false,
                data: {
                    periodId: periodId,
                    tableId: _tableId,
                    windowId: _windowId,
                    pageNo: _page,
                    pageSize: MODAL_PAGE_SIZE
                },
                success: function (res) {
                    /* Stale response: another page, another screen, another period, or
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
            $modalBody.html('<div class="vas-197-modal-state' + (isError ? ' vas-197-modal-state-error' : '') +
                '">' + escapeHtml(text) + '</div>');
            renderModalPager({ Total: 0, PageSize: MODAL_PAGE_SIZE, PageNo: 1, Rows: [] });
        }

        /* One list shape serves every screen, except that a column with nothing
           behind it is DROPPED rather than printed blank - an inventory movement has
           no business partner and no document type, and a column of empty cells reads
           as missing data instead of as "not applicable".

           The status sits immediately before the amount - where a document has got to
           and what it is worth read together - so the last three columns are always
           the same three, and the two optional ones sit together ahead of them. That
           fixed tail is what lets the stylesheet shed the optional columns by position
           without knowing which is which.

           The date column is captioned from the column the SERVER actually bounded
           by: most screens are dated by DateAcct, but an inventory screen that has
           none is dated by MovementDate, and captioning that "Account Date" would be
           a quiet lie about which date the user is reading. */
        function columnsFor(data) {
            var dateColumn = data.DateColumn || 'DateAcct';

            var captions = [
                colLabel('DocumentNo', 'VAS_197_DocumentNo', 'Document No'),
                colLabel(dateColumn, 'VAS_200_DateAcct', 'Account Date')
            ];

            if (data.HasBPartner) {
                captions.push(colLabel('C_BPartner_ID', 'VAS_201_BusinessPartner', 'Business Partner'));
            }
            if (data.HasDocType) {
                captions.push(colLabel('C_DocType_ID', 'VIS_DocumentType', 'Document Type'));
            }

            captions.push(colLabel('DocStatus', 'VAS_197_Status', 'Status'));
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

        /* Grid variant. Two optional columns make four possible shapes, but what the
           stylesheet needs is the column COUNT - the proportions that suit five
           columns suit them whichever five they are. Four to six. */
        function gridVariant(captionCount) {
            return 'vas-197-dgrid-c' + captionCount;
        }

        function renderModalRows(data) {
            var rows = data.Rows || [];

            if (rows.length === 0) {
                renderModalState(label('VAS_201_NoRecords', 'No records in this category.'), false);
                return;
            }

            var captions = columnsFor(data);
            var numeric = numericColumns(captions.length);

            var head = '<div class="vas-197-dhead">';
            for (var c = 0; c < captions.length; c++) {
                head += cell(captions[c], numeric[c] ? 'vas-197-dcell-num' : '');
            }
            head += '</div>';

            var body = '';
            for (var i = 0; i < rows.length; i++) {
                body += buildDetailRow(rows[i], data);
            }

            $modalBody.html('<div class="vas-197-dgrid ' + gridVariant(captions.length) + '">' +
                head + body + '</div>');
            $modalBody.scrollTop(0);

            syncModalHeight(rows.length);
            renderModalPager(data);
        }

        function cell(text, extraClass) {
            return '<span class="vas-197-dcell' + (extraClass ? ' ' + extraClass : '') +
                '" title="' + escapeHtml(text) + '">' + escapeHtml(text) + '</span>';
        }

        /* The status as a pill. The tone is read off the STORED code, never off the
           translated name, so it survives every language. */
        function pillCell(text, tone) {
            if (!text) { return cell(''); }
            return '<span class="vas-197-dcell">' +
                '<span class="vas-197-pill vas-197-pill-' + tone + '" title="' + escapeHtml(text) + '">' +
                    escapeHtml(text) + '</span>' +
            '</span>';
        }

        /* Every row here is unfinished by definition, so the tone says HOW unfinished.
           Drafted is ordinary work not yet started; in progress and the approval
           states are work under way; invalid and not-approved are work that has gone
           wrong and needs a person. Anything the tenant has added that this widget
           does not know reads as ordinary. */
        function docStatusTone(code) {
            if (code === 'IN' || code === 'NA') { return 'fail'; }
            if (code === 'IP' || code === 'WC' || code === 'WP' || code === 'AP') { return 'warn'; }
            return 'plain';
        }

        /* A document number rendered as a link. A button, not an anchor: there is no
           URL to follow - the framework opens the window. Without a window or a key
           the number stays plain text rather than offering a dead link. */
        function docCell(row, data) {
            var text = row.DocumentNo || '';
            var windowId = Number(data.AD_Window_ID || 0);
            var keyColumn = data.KeyColumn || '';

            if (!(row.Record_ID > 0) || !(windowId > 0) || !keyColumn) {
                return cell(text, 'vas-197-dcell-b');
            }

            return '<span class="vas-197-dcell vas-197-dcell-doc">' +
                '<button type="button" class="vas-197-doclink" data-column="' + escapeHtml(keyColumn) +
                    '" data-id="' + row.Record_ID + '" data-window="' + windowId +
                    '" title="' + escapeHtml(text) + '">' +
                    escapeHtml(text) + '</button>' +
            '</span>';
        }

        function buildDetailRow(row, data) {
            /* The document's amount in its OWN currency - the figure printed on the
               document the user is about to open. The card's totals are the converted
               ones; a record list is not where currencies get added together.

               A screen with no currency column has none to label it with: it is
               valued from stored costs that are base currency already, so it carries
               the base symbol. A screen with no trusted amount strategy prints a dash,
               matching the card rather than contradicting it with a zero. */
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
            var html = '<div class="vas-197-drow">' +
                docCell(row, data) +
                cell(formatDate(row.DateAcct));

            if (data.HasBPartner) { html += cell(row.BusinessPartnerName || ''); }
            if (data.HasDocType) { html += cell(row.DocumentType || ''); }

            return html +
                pillCell(refName(data.DocStatusNames, row.DocStatus), docStatusTone(row.DocStatus)) +
                cell(documentAmount, 'vas-197-dcell-num') +
            '</div>';
        }

        /* Grows the fixed body to whatever a FULL page actually measures, so the last
           page never introduces a scrollbar the first page did not have. Only ever
           grows: shrinking back on a short page is the fluctuation this prevents. */
        function syncModalHeight(rowCount) {
            if (!$modalBody || !$modalBody[0] || rowCount <= 0) { return; }

            var headEl = $modalBody[0].querySelector('.vas-197-dhead');
            var rowEls = $modalBody[0].querySelectorAll('.vas-197-drow');
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
                '<span class="vas-197-pager-info" title="' + escapeHtml(showing) + '">' +
                    escapeHtml(showing) + '</span>' +
                '<span class="vas-197-pager-nav">' +
                    '<button type="button" class="vas-197-pgbtn" data-dir="prev" aria-label="' +
                        escapeHtml(label('VAS_026_Prev', 'Previous')) + '"' + prevDis + '>' + icon('chevL') + '</button>' +
                    '<span class="vas-197-pager-label">' + pageNo + ' ' + escapeHtml(ofTxt) + ' ' + totalPages + '</span>' +
                    '<button type="button" class="vas-197-pgbtn" data-dir="next" aria-label="' +
                        escapeHtml(label('VAS_026_Next', 'Next')) + '"' + nextDis + '>' + icon('chevNext') + '</button>' +
                '</span>'
            );
        }

        /* Opens one record in the screen the row stands for. The window id came from
           the server with the page, so there is no name lookup and no hard-coded
           AD_Window_ID anywhere in this widget. The dialog closes first: the record
           opens in its own window, and leaving the modal over it would hide what was
           just opened. Degrades silently - a click can never throw. */
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
               Period Control widgets since this card last loaded - and a document may
               have been completed, which is the whole point of the card. */
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

    VAS.VAS_197_UnProcessedDocumentsWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_197_UnProcessedDocumentsWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_197_UnProcessedDocumentsWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_197_UnProcessedDocumentsWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_197_UnProcessedDocumentsWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
