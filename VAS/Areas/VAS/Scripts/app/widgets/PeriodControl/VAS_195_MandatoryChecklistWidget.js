/**
 * VAS_195_MandatoryChecklistWidget
 * 6x2 grid widget for the Period Control dashboard.
 *
 * The 23 mandatory period-close checks, evaluated against ONE open accounting
 * period and sorted so the work comes first:
 *
 *   Checklist item                    Classification   Status
 *   -------------------------------------------------------------
 *   Unprocessed documents             Blocker          Fail
 *   Trial Balance debit <> credit     Blocker          Pass
 *   Payment allocation status         Warning          Warning
 *   Bank accounts fully reconciled    Check            Complete
 *
 * Seven rows per page with a footer pager, because 23 rows do not fit a dashboard
 * card and paging beats scrolling inside one. Clicking a row opens the records
 * behind that check.
 *
 * THE MODAL IS GENERIC. The 23 checks have 23 different row shapes, so the server
 * returns the COLUMNS each check declares alongside its rows, and this file renders
 * whatever it is given - captions, alignment, formatting and the grid template all
 * come from the column descriptors. Adding a check, or changing one's columns, is a
 * server change only; there is no per-check rendering code here to fall out of step
 * with it.
 *
 * Close verdict: the card footer reports whether the period can be closed. Only a
 * BLOCKER that is failing or misconfigured stops it - warnings stay visible and
 * reviewable but never gate the action, and a positive CHECK never does either. The
 * widget is READ-ONLY: it reports the verdict, it does not close anything.
 *
 * Period source: the open STANDARD periods of the tenant's primary calendar. A
 * period qualifies when at least one active C_PeriodControl row of it is Open,
 * because close readiness is not confined to one document base type. Nothing is
 * derived from the calendar month.
 *
 * Sizing follows design.md -> dashboard-widgets.md: the card carries the widget root
 * anchor clamp, the header reads --dash-inline-size (populated by
 * ensureDashInlineSizeVar), the checklist grid shares one column template between
 * its header and its rows, and nothing scrolls. Narrow cells are handled by
 * container queries, the body-level modal and picker by media queries.
 *
 * Summary Message Table
 * The 23 check titles and their summary lines are keyed VAS_195_Chk01..23,
 * VAS_195_Sum01..23, VAS_195_Clr01..23, VAS_195_Na* and VAS_195_Cfg* and are
 * supplied BY THE SERVER with an English fallback in the payload - they are not
 * listed here, since this file never hard-codes one. The keys below are the
 * widget's own chrome. Rows marked (reuse) already exist under another key.
 *  # | Current Text                       | Message Key
 * ---+------------------------------------+---------------------------------
 *  1 | Mandatory Close Checklist          | VAS_195_MandatoryCloseChecklist
 *  2 | Failed checks block the period close | VAS_195_BlockHint
 *  3 | Checklist item                     | VAS_195_ChecklistItem
 *  4 | Classification                     | VAS_195_Classification
 *  5 | Status                             | VAS_195_Status
 *  6 | Blocker                            | VAS_195_Blocker
 *  7 | Warning                            | VAS_195_ClassWarning
 *  8 | Check                              | VAS_195_ClassCheck
 *  9 | Pass                               | VAS_195_Pass
 * 10 | Fail                               | VAS_195_Fail
 * 11 | Complete                           | VAS_195_Complete
 * 12 | Incomplete                         | VAS_195_Incomplete
 * 13 | Not applicable                     | VAS_195_NotApplicable
 * 14 | Setup                              | VAS_195_ConfigError
 * 15 | sorted by close priority           | VAS_195_SortedBy
 * 16 | Period close is blocked            | VAS_195_CloseBlocked
 * 17 | Ready to close                     | VAS_195_ReadyToClose
 * 18 | No records for this check          | VAS_195_NoRecords
 * 19 | Nothing to show for this check      | VAS_195_NoDetail
 * 20 | Primary calendar not configured    | VAS_195_NoCalendar
 * 21 | Primary accounting schema not configured | VAS_195_NoAcctSchema
 * 22 | No open accounting period          | VAS_201_NoOpenPeriod    (reuse)
 * 23 | Dashboard period                   | VAS_201_DashboardPeriod (reuse)
 * 24 | Close                              | VAS_018_Close           (reuse)
 * 25 | Couldn't load                      | VAS_192_CouldntLoad     (reuse)
 * 26 | Showing                            | VAS_026_Showing         (reuse)
 * 27 | of                                 | VAS_026_Of              (reuse)
 * 28 | Previous                           | VAS_026_Prev            (reuse)
 * 29 | Next                               | VAS_026_Next            (reuse)
 * 30 | Debit                              | VAS_195_Debit
 * 31 | Credit                             | VAS_195_Credit
 *
 * 30 and 31 are the one exception to "captions come from the server": the ledger
 * side arrives as a stored token so that it CAN be translated here, which a string
 * composed in SQL could not be. Both fall back to the token, which reads as English.
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

    /* Classification tokens -> label and tone. Kept in lock-step with
       VASLogic.Models.VAS_195_MandatoryChecklistModel. */
    var CLASSIFICATIONS = {
        BLOCKER: { key: 'VAS_195_Blocker', text: 'Blocker', tone: 'crit' },
        WARNING: { key: 'VAS_195_ClassWarning', text: 'Warning', tone: 'med' },
        CHECK: { key: 'VAS_195_ClassCheck', text: 'Check', tone: 'low' }
    };

    /* Status tokens -> label and pill tone. */
    var STATUSES = {
        PASS: { key: 'VAS_195_Pass', text: 'Pass', tone: 'ok' },
        FAIL: { key: 'VAS_195_Fail', text: 'Fail', tone: 'fail' },
        WARNING: { key: 'VAS_195_ClassWarning', text: 'Warning', tone: 'warn' },
        COMPLETE: { key: 'VAS_195_Complete', text: 'Complete', tone: 'ok' },
        INCOMPLETE: { key: 'VAS_195_Incomplete', text: 'Incomplete', tone: 'warn' },
        NOT_APPLICABLE: { key: 'VAS_195_NotApplicable', text: 'Not applicable', tone: 'plain' },
        CONFIGURATION_ERROR: { key: 'VAS_195_ConfigError', text: 'Setup', tone: 'fail' }
    };

    /* Server error tokens -> the message that explains them. */
    var ERROR_LABELS = {
        NOCALENDAR: { key: 'VAS_195_NoCalendar', text: 'Primary calendar not configured.' },
        NOACCTSCHEMA: { key: 'VAS_195_NoAcctSchema', text: 'Primary accounting schema not configured.' },
        NOPERIOD: { key: 'VAS_201_NoOpenPeriod', text: 'No open accounting period.' },
        NODETAIL: { key: 'VAS_195_NoDetail', text: 'Nothing to show for this check.' },
        INVALID: { key: 'VAS_192_CouldntLoad', text: "Couldn't load" }
    };

    /* Card page size is ADAPTIVE: seven is the reference design's count at its own
       cell size, but a widget resized taller should show more rows rather than more
       pages, and one resized shorter must not spill rows behind a clipped edge. Seven
       is therefore only the starting guess, replaced by a measurement once the first
       page has been painted and whenever the cell changes size.
       The ceiling exists so a very tall cell cannot ask for more rows than the
       checklist has. */
    var CARD_PAGE_START = 7;
    var CARD_PAGE_MAX = 23;
    var MODAL_PAGE_SIZE = 25;

    /* Starting estimate for the detail grid's row / header heights, in px. It only
       has to hold until the first page is painted: the real heights are then measured
       and the body grown to fit a full page exactly. */
    var MODAL_ROW_H = 44;
    var MODAL_HEAD_H = 46;
    var MODAL_BODY_SLACK = 4;

    VAS.VAS_195_MandatoryChecklistWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas-195-root">');
        var $card;
        var $list;
        var $foot;
        var $periodBtn;
        var $busy;

        /* Both overlays live on <body>, so they are NOT inside $root - always address
           them through these references, never through $root.find. The card clips its
           own overflow, so an in-card popover would be cut off. */
        var $picker = null;
        var $overlay = null;
        var $modalBody = null;
        var $modalPager = null;
        var $modalBusy = null;

        /* Per-instance event namespace - a dashboard can hold two of this widget, and
           each must unbind only its own document/window handlers. */
        var _ns = '.vas195_' + (VAS.VAS_195_MandatoryChecklistWidget._seq =
            (VAS.VAS_195_MandatoryChecklistWidget._seq || 0) + 1);

        /* Current selection and the data painted from it. */
        var _periods = [];
        var _periodId = 0;
        var _periodName = '';
        var _data = null;
        var _schema = null;
        var _cardPage = 1;
        var _cardPageSize = CARD_PAGE_START;

        /* Re-entrancy guard: the repaint that follows a page-size change resizes the
           list, which wakes the observer that measured it. Without this the two would
           chase each other. */
        var _sizingPage = false;

        /* In-flight guard: a period change while one is already running is ignored
           rather than queued, so a rapid click-through cannot stack requests. */
        var _periodBusy = false;

        /* Detail modal state. _detailSeq drops the response of a page the user has
           already navigated away from. */
        var _checkCode = '';
        var _checkTitle = '';
        var _columns = [];

        /* Whether the open check declares a Screen column of its own. When it does not,
           the document cell carries the screen name as its second line instead. */
        var _hasScreenColumn = false;
        var _page = 1;
        var _detailSeq = 0;
        var _bodyH = 0;
        var _pickerOpen = false;
        var _modalOpen = false;
        var _busyCount = 0;

        /* The card row that opened the modal, so focus can be handed back to it. */
        var _returnFocusTo = null;

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

        function errorLabel(code) {
            var def = ERROR_LABELS[code];
            if (!def) { return label('VAS_192_CouldntLoad', "Couldn't load"); }
            return label(def.key, def.text);
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
            $busy.toggleClass('vas-195-hidden', _busyCount === 0);
        }

        function showError(text) {
            if (VIS && VIS.ADialog && VIS.ADialog.error) { VIS.ADialog.error("", "", text); }
        }

        function formatCount(value) {
            var n = Number(value || 0);
            if (!isFinite(n)) { n = 0; }
            return Math.round(n).toLocaleString(window.navigator.language);
        }

        function precision() {
            var p = _schema ? Number(_schema.Precision) : 2;
            return (isFinite(p) && p >= 0) ? p : 2;
        }

        function symbol() {
            return _schema ? (_schema.Symbol || _schema.Iso || '') : '';
        }

        /* An accounting-schema-currency amount, at the schema's own precision. */
        function formatAmount(value, withSymbol) {
            var n = Number(value || 0);
            if (!isFinite(n)) { n = 0; }
            var p = precision();

            var sign = n < 0 ? '-' : '';
            var text = Math.abs(n).toLocaleString(window.navigator.language, {
                minimumFractionDigits: p, maximumFractionDigits: p
            });
            return sign + (withSymbol ? symbol() : '') + text;
        }

        /* Quantities carry their own decimals, not a currency precision. */
        function formatQty(value) {
            var n = Number(value || 0);
            if (!isFinite(n)) { n = 0; }
            return n.toLocaleString(window.navigator.language, { maximumFractionDigits: 4 });
        }

        function formatNumber(value) {
            var n = Number(value || 0);
            if (!isFinite(n)) { return String(value == null ? '' : value); }
            return n.toLocaleString(window.navigator.language, { maximumFractionDigits: 2 });
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
            /* The widget's own glyph, from the reference design - a checked list. */
            if (name === 'checklist') { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M9 11l3 3L22 4"/><path d="M21 12v7a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11"/></svg>'; }
            return '';
        }

        // ── Build ────────────────────────────────────────────────────────────

        this.Initalize = function () {
            createWidget();
            setupResizeObserver();
            loadBootstrap();
        };

        function createWidget() {
            var title = label('VAS_195_MandatoryCloseChecklist', 'Mandatory Close Checklist');
            var subtitle = label('VAS_195_BlockHint', 'Failed checks block the period close');

            var colItem = label('VAS_195_ChecklistItem', 'Checklist item');
            var colClass = label('VAS_195_Classification', 'Classification');
            var colStatus = label('VAS_195_Status', 'Status');

            $card = $(
                '<div class="vas-195-card vas-widget-bg" role="group" aria-label="' + escapeHtml(title) + '">' +
                    '<div class="vas-195-header">' +
                        '<span class="vas-195-icon">' + icon('checklist') + '</span>' +
                        '<div class="vas-195-head-text">' +
                            '<div class="vas-195-title" title="' + escapeHtml(title) + '">' + escapeHtml(title) + '</div>' +
                            '<div class="vas-195-subtitle" title="' + escapeHtml(subtitle) + '">' + escapeHtml(subtitle) + '</div>' +
                        '</div>' +
                        /* Period chip: the widget's only filter. It names the period
                           every check was evaluated against, and opens the picker. */
                        '<button type="button" class="vas-195-periodchip" aria-haspopup="listbox">' +
                            icon('calendar') +
                            '<span class="vas-195-periodchip-label"></span>' +
                            icon('chevDown') +
                        '</button>' +
                    '</div>' +
                    '<div class="vas-195-body">' +
                        /* Column header and rows share ONE grid template, so the two
                           badge columns line up down the card. */
                        '<div class="vas-195-thead">' +
                            '<span class="vas-195-cell" title="' + escapeHtml(colItem) + '">' +
                                escapeHtml(colItem) + '</span>' +
                            '<span class="vas-195-cell" title="' + escapeHtml(colClass) + '">' +
                                escapeHtml(colClass) + '</span>' +
                            '<span class="vas-195-cell vas-195-cell-end" title="' + escapeHtml(colStatus) + '">' +
                                escapeHtml(colStatus) + '</span>' +
                        '</div>' +
                        '<div class="vas-195-list"></div>' +
                        '<div class="vas-195-foot"></div>' +
                    '</div>' +
                '</div>'
            );

            $list = $card.find('.vas-195-list');
            $foot = $card.find('.vas-195-foot');
            $periodBtn = $card.find('.vas-195-periodchip');

            $periodBtn.on('click', function (e) {
                e.stopPropagation();
                togglePicker();
            });

            /* Delegated so the handlers survive every repaint. Only rows the server
               marked drillable carry the -open class, so a NOT_APPLICABLE row cannot
               be activated by keyboard either. */
            $list.on('click', '.vas-195-row-open', function () {
                openModal($(this).attr('data-check'), $(this).attr('data-title'), this);
            });
            $list.on('keydown', '.vas-195-row-open', function (e) {
                if (e.key === 'Enter' || e.keyCode === 13 || e.key === ' ' || e.keyCode === 32) {
                    e.preventDefault();
                    $(this).trigger('click');
                }
            });

            /* Card pager - 23 checks do not fit one card. */
            $foot.on('click', '.vas-195-pgbtn', function () {
                var $btn = $(this);
                if ($btn.prop('disabled')) { return; }
                _cardPage = $btn.attr('data-dir') === 'next' ? _cardPage + 1 : Math.max(1, _cardPage - 1);
                paintList();
            });

            $root.append($card);

            $busy = $('<div class="vas-195-busy vas-195-hidden"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $root.append($busy);
        }

        /* Publishes the widget's own measured width so the card anchor can scale on the
           widget instead of the whole dashboard when the cell is unusual. */
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

                    /* A resize changes both the row height (the card's font scale is
                       driven off its width) and the space available, so the page size
                       is re-measured on every one. */
                    syncCardPageSize();
                });
                ro.observe($root[0]);
            } catch (e) { }
        }

        // ── Loads ────────────────────────────────────────────────────────────

        function loadBootstrap() {
            showBusy(true);
            _periodBusy = true;

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_195_MandatoryChecklistWidget/GetBootstrap',
                type: 'GET',
                cache: false,
                success: function (res) {
                    var data = null;
                    try { data = parseResponse(res); } catch (e) { }
                    if (!data || data.error) { renderState(label('VAS_192_CouldntLoad', "Couldn't load"), true); return; }

                    _schema = data.Schema || null;
                    _periods = data.Periods || [];
                    _periodId = data.C_Period_ID || 0;
                    _periodName = data.PeriodName || '';

                    paintPeriod();

                    /* A configuration error is not a load failure: it names exactly
                       what is missing so the user knows where to go. */
                    if (data.ErrorCode) { renderState(errorLabel(data.ErrorCode), false); return; }

                    if (_periods.length === 0 || _periodId <= 0) {
                        renderState(label('VAS_201_NoOpenPeriod', 'No open accounting period.'), false);
                        return;
                    }

                    applyData(data.Data);
                },
                error: function () { renderState(label('VAS_192_CouldntLoad', "Couldn't load"), true); },
                complete: function () { showBusy(false); _periodBusy = false; }
            });
        }

        function loadPeriodData(periodId) {
            showBusy(true);
            _periodBusy = true;

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_195_MandatoryChecklistWidget/GetPeriodData',
                type: 'GET',
                cache: false,
                data: { periodId: periodId },
                success: function (res) {
                    var data = null;
                    try { data = parseResponse(res); } catch (e) { }
                    if (!data || data.error) { renderState(label('VAS_192_CouldntLoad', "Couldn't load"), true); return; }

                    /* A late response for a period the user has already moved away from
                       must not overwrite the current card. */
                    if (periodId !== _periodId) { return; }

                    if (data.ErrorCode === 'NOPERIOD') {
                        /* The period stopped qualifying (closed elsewhere while the
                           dashboard was open) - re-read the whole selector. */
                        loadBootstrap();
                        return;
                    }

                    if (data.ErrorCode) { renderState(errorLabel(data.ErrorCode), false); return; }

                    applyData(data);
                },
                error: function () { renderState(label('VAS_192_CouldntLoad', "Couldn't load"), true); },
                complete: function () { showBusy(false); _periodBusy = false; }
            });
        }

        function applyData(data) {
            _data = data || null;
            if (_data && _data.Schema) { _schema = _data.Schema; }
            /* A new period starts at page 1 - leaving the pager on page 3 of a
               different evaluation shows rows nobody asked for. */
            _cardPage = 1;
            paintList();
        }

        // ── Card rendering ───────────────────────────────────────────────────

        function paintPeriod() {
            var text = _periodName || '—';
            $periodBtn.find('.vas-195-periodchip-label').text(text);
            $periodBtn.attr('title', text);
            $periodBtn.toggleClass('vas-195-hidden', _periods.length === 0);
        }

        function renderState(text, isError) {
            if (!$list) { return; }
            $list.html('<div class="vas-195-state' + (isError ? ' vas-195-state-error' : '') + '">' +
                escapeHtml(text) + '</div>');
            $foot.empty();
        }

        /* The server ships a key AND an English fallback for every title and summary,
           so this file never hard-codes one of the 23 strings. */
        function serverLabel(key, text) {
            if (!key) { return text || ''; }
            return label(key, text || key);
        }

        function paintList() {
            if (!$list) { return; }

            var items = (_data && _data.Items) ? _data.Items : [];
            if (items.length === 0) { renderState(label('VAS_192_CouldntLoad', "Couldn't load"), true); return; }

            var totalPages = Math.max(1, Math.ceil(items.length / _cardPageSize));
            if (_cardPage > totalPages) { _cardPage = totalPages; }

            var from = (_cardPage - 1) * _cardPageSize;
            var to = Math.min(from + _cardPageSize, items.length);

            var html = '';
            for (var i = from; i < to; i++) {
                html += buildRow(items[i]);
            }

            $list.html(html);
            paintFooter(items.length, from, to, totalPages);

            /* Measure only after the browser has laid the page out - offsetHeight on a
               row this statement has just written is not yet meaningful. */
            if (!_sizingPage) { window.setTimeout(syncCardPageSize, 0); }
        }

        /* How many rows actually fit the list area at its current size.
           The TALLEST painted row is the unit, not the first: a check with a summary
           line under its title is taller than one without, and sizing to the shorter
           kind would clip the taller ones. */
        function measureCardPageSize() {
            if (!$list || !$list[0]) { return _cardPageSize; }

            var rows = $list[0].querySelectorAll('.vas-195-row');
            if (!rows.length) { return _cardPageSize; }

            var rowH = 0;
            for (var i = 0; i < rows.length; i++) {
                if (rows[i].offsetHeight > rowH) { rowH = rows[i].offsetHeight; }
            }
            if (rowH <= 0) { return _cardPageSize; }

            var available = $list[0].clientHeight;
            if (available <= 0) { return _cardPageSize; }

            var fits = Math.floor(available / rowH);
            if (!isFinite(fits) || fits < 1) { fits = 1; }
            if (fits > CARD_PAGE_MAX) { fits = CARD_PAGE_MAX; }

            return fits;
        }

        /* Adopts a new page size and repaints, keeping the reader where they were:
           the item that led the old page leads the new one, so growing the widget
           reveals more rows around what you were looking at instead of throwing you
           back to page 1. */
        function syncCardPageSize() {
            if (_sizingPage || !_data) { return; }

            var next = measureCardPageSize();
            if (next === _cardPageSize) { return; }

            var firstIndex = (_cardPage - 1) * _cardPageSize;

            _sizingPage = true;
            try {
                _cardPageSize = next;
                _cardPage = Math.floor(firstIndex / _cardPageSize) + 1;
                paintList();
            } finally {
                _sizingPage = false;
            }
        }

        function buildRow(item) {
            var title = serverLabel(item.TitleKey, item.TitleText);
            var summary = buildSummary(item);

            var cls = CLASSIFICATIONS[item.Classification] ||
                { key: '', text: item.Classification || '', tone: 'low' };
            var status = STATUSES[item.Status] ||
                { key: '', text: item.Status || '', tone: 'plain' };

            var classText = serverLabel(cls.key, cls.text);
            var statusText = serverLabel(status.key, status.text);

            /* Only a row with records behind it advertises itself as clickable. */
            var open = !!item.DetailAvailable;
            var attrs = open
                ? ' class="vas-195-row vas-195-row-open" role="button" tabindex="0" data-check="' +
                  escapeHtml(item.CheckCode) + '" data-title="' + escapeHtml(title) +
                  '" aria-label="' + escapeHtml(title + ' · ' + classText + ' · ' + statusText) + '"'
                : ' class="vas-195-row"';

            /* A blocking row is tinted along its leading edge - the eye finds what
               stops the close before it reads a single word. */
            var blocking = item.IsBlocking ? ' vas-195-row-blocking' : '';

            return '<div' + attrs.replace('class="vas-195-row', 'class="vas-195-row' + blocking) + '>' +
                '<span class="vas-195-cell vas-195-cell-name">' +
                    '<span class="vas-195-row-main" title="' + escapeHtml(title) + '">' +
                        escapeHtml(title) + '</span>' +
                    (summary
                        ? '<span class="vas-195-row-sub" title="' + escapeHtml(summary) + '">' +
                              escapeHtml(summary) + '</span>'
                        : '') +
                '</span>' +
                '<span class="vas-195-cell">' +
                    '<span class="vas-195-sev vas-195-sev-' + cls.tone + '" title="' + escapeHtml(classText) +
                        '">' + escapeHtml(classText) + '</span>' +
                '</span>' +
                '<span class="vas-195-cell vas-195-cell-end">' +
                    '<span class="vas-195-pill vas-195-pill-' + status.tone + '" title="' +
                        escapeHtml(statusText) + '">' + escapeHtml(statusText) + '</span>' +
                '</span>' +
            '</div>';
        }

        /* The secondary line under a check's title. The server supplies the sentence;
           where the check counted something, the count leads it - "12 unprocessed
           documents..." reads as a finding, "unprocessed documents..." reads as a
           label. */
        function buildSummary(item) {
            var text = serverLabel(item.SummaryKey, item.SummaryText);
            if (!text) { return ''; }

            if (item.RecordCount > 0 && item.Status !== 'PASS' && item.Status !== 'COMPLETE') {
                return formatCount(item.RecordCount) + ' ' + text;
            }
            return text;
        }

        /* Footer: the close verdict on the left, the card pager on the right. The
           verdict is the server's - it is the one thing on this card a user might act
           on, and re-deriving it here from the painted page would be wrong on every
           page but the first. */
        function paintFooter(total, from, to, totalPages) {
            var verdict = _data && _data.CloseAllowed
                ? '<span class="vas-195-verdict vas-195-verdict-ok" title="' +
                      escapeHtml(label('VAS_195_ReadyToClose', 'Ready to close')) + '">' +
                      escapeHtml(label('VAS_195_ReadyToClose', 'Ready to close')) + '</span>'
                : '<span class="vas-195-verdict vas-195-verdict-blocked" title="' +
                      escapeHtml(label('VAS_195_CloseBlocked', 'Period close is blocked')) + '">' +
                      escapeHtml(label('VAS_195_CloseBlocked', 'Period close is blocked')) + '</span>';

            var ofTxt = label('VAS_026_Of', 'of');
            var showing = label('VAS_026_Showing', 'Showing') + ' ' + (from + 1) + '–' + to + ' ' +
                ofTxt + ' ' + formatCount(total) + ' · ' + label('VAS_195_SortedBy', 'sorted by close priority');

            var prevDis = _cardPage <= 1 ? ' disabled' : '';
            var nextDis = _cardPage >= totalPages ? ' disabled' : '';

            $foot.html(
                '<span class="vas-195-foot-info" title="' + escapeHtml(showing) + '">' +
                    verdict + '<span class="vas-195-foot-sep">·</span>' +
                    '<span class="vas-195-foot-count">' + escapeHtml(showing) + '</span>' +
                '</span>' +
                '<span class="vas-195-pager-nav">' +
                    '<button type="button" class="vas-195-pgbtn" data-dir="prev" aria-label="' +
                        escapeHtml(label('VAS_026_Prev', 'Previous')) + '"' + prevDis + '>' + icon('chevL') + '</button>' +
                    '<span class="vas-195-pager-label">' + _cardPage + ' ' + escapeHtml(ofTxt) + ' ' + totalPages + '</span>' +
                    '<button type="button" class="vas-195-pgbtn" data-dir="next" aria-label="' +
                        escapeHtml(label('VAS_026_Next', 'Next')) + '"' + nextDis + '>' + icon('chevNext') + '</button>' +
                '</span>'
            );
        }

        // ── Period picker ────────────────────────────────────────────────────

        function buildPicker() {
            $picker = $('<div class="vas-195-pp vas-195-hidden" role="listbox" aria-label="' +
                escapeHtml(label('VAS_201_DashboardPeriod', 'Dashboard period')) + '">');
            $('body').append($picker);

            $picker.on('click', '.vas-195-pp-opt', function () {
                var id = parseInt($(this).attr('data-id'), 10) || 0;
                closePicker();
                selectPeriod(id);
            });
        }

        function fillPicker() {
            var html = '<div class="vas-195-pp-h">' +
                escapeHtml(label('VAS_201_DashboardPeriod', 'Dashboard period')) + '</div>';

            for (var i = 0; i < _periods.length; i++) {
                var p = _periods[i];
                var selected = p.C_Period_ID === _periodId;
                var meta = p.FiscalYear ? String(p.FiscalYear) : '';

                html += '<button type="button" class="vas-195-pp-opt" role="option" data-id="' + p.C_Period_ID +
                        '" aria-selected="' + (selected ? 'true' : 'false') + '">' +
                    '<span class="vas-195-pp-name" title="' + escapeHtml(p.Name || '') + '">' +
                        escapeHtml(p.Name || '') + '</span>' +
                    (meta ? '<span class="vas-195-pp-meta">' + escapeHtml(meta) + '</span>' : '') +
                    '<span class="vas-195-pp-tick">' + icon('tick') + '</span>' +
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
            $picker.removeClass('vas-195-hidden');
            positionPicker();
            _pickerOpen = true;

            $(document).on('click' + _ns, onDocumentClick);
            $(document).on('keydown' + _ns, onPickerKeyDown);
            $(window).on('resize' + _ns + ' scroll' + _ns, closePicker);
        }

        function closePicker() {
            if (!_pickerOpen) { return; }
            _pickerOpen = false;
            if ($picker) { $picker.addClass('vas-195-hidden'); }

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

            /* 23 checks is an expensive evaluation - a second request is refused while
               one is running rather than queued behind it. */
            if (_periodBusy) { return; }

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
                '<div class="vas-195-overlay vas-195-hidden">' +
                    '<div class="vas-195-modal" role="dialog" aria-modal="true" aria-labelledby="' +
                        modalTitleId() + '">' +
                        '<div class="vas-195-modal-head">' +
                            '<span class="vas-195-modal-ico">' + icon('checklist') + '</span>' +
                            '<div class="vas-195-modal-heads">' +
                                '<div class="vas-195-modal-title" id="' + modalTitleId() + '"></div>' +
                                '<div class="vas-195-modal-sub"></div>' +
                            '</div>' +
                            '<button type="button" class="vas-195-modal-close" aria-label="' +
                                escapeHtml(label('VAS_018_Close', 'Close')) + '">' + icon('close') + '</button>' +
                        '</div>' +
                        '<div class="vas-195-modal-body"></div>' +
                        '<div class="vas-195-modal-foot"></div>' +
                        '<div class="vas-195-modal-busy vas-195-hidden">' +
                            '<div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div>' +
                        '</div>' +
                    '</div>' +
                '</div>'
            );

            $('body').append($overlay);
            $modalBody = $overlay.find('.vas-195-modal-body');
            $modalPager = $overlay.find('.vas-195-modal-foot');
            $modalBusy = $overlay.find('.vas-195-modal-busy');

            _bodyH = (MODAL_ROW_H * 12) + MODAL_HEAD_H + MODAL_BODY_SLACK;
            $modalBody.css('height', _bodyH + 'px');

            $overlay.find('.vas-195-modal-close').on('click', closeModal);
            $overlay.on('click', function (e) { if (e.target === $overlay[0]) { closeModal(); } });

            $modalPager.on('click', '.vas-195-pgbtn', function () {
                var $btn = $(this);
                if ($btn.prop('disabled')) { return; }
                _page = $btn.attr('data-dir') === 'next' ? _page + 1 : Math.max(1, _page - 1);
                loadModalPage();
            });

            $modalBody.on('click', '.vas-195-doclink', function (e) {
                e.stopPropagation();
                var $link = $(this);
                zoomTo($link.attr('data-key'),
                    parseInt($link.attr('data-record'), 10) || 0,
                    parseInt($link.attr('data-window'), 10) || 0);
            });

            /* Focus trap: Tab cycles inside the dialog while it is open. */
            $overlay.on('keydown', function (e) {
                if (e.key !== 'Tab' && e.keyCode !== 9) { return; }

                var $focusable = $overlay.find('button:visible:not(:disabled)');
                if ($focusable.length === 0) { return; }

                var first = $focusable[0];
                var last = $focusable[$focusable.length - 1];

                if (e.shiftKey && e.target === first) { e.preventDefault(); last.focus(); }
                else if (!e.shiftKey && e.target === last) { e.preventDefault(); first.focus(); }
            });
        }

        /* A per-instance id, so two of this widget on one dashboard do not both label
           their dialog by the same element. */
        function modalTitleId() {
            var seq = ($self.AD_UserHomeWidgetID || $self.windowNo || 0);
            return 'vas195ModalTitle_' + seq + _ns.replace(/[^a-zA-Z0-9_]/g, '');
        }

        function openModal(checkCode, checkTitle, rowEl) {
            if (!checkCode || _periodId <= 0) { return; }
            if (!$overlay) { buildModal(); }

            _checkCode = checkCode;
            _checkTitle = checkTitle || '';
            _columns = [];
            _page = 1;
            _returnFocusTo = rowEl || null;

            $overlay.find('.vas-195-modal-title').text(_checkTitle);
            $overlay.find('.vas-195-modal-sub').text(modalSubtitle());

            $modalBody.empty();
            $modalPager.empty();

            $overlay.removeClass('vas-195-hidden');
            _modalOpen = true;
            closePicker();

            $(document).on('keydown' + _ns + 'm', onModalKeyDown);
            $overlay.find('.vas-195-modal-close').focus();

            loadModalPage();
        }

        /* "<Period> · <Accounting schema> (<ISO>)" - the three things that scope every
           figure in the dialog. */
        function modalSubtitle() {
            var parts = [];
            if (_periodName) { parts.push(_periodName); }

            if (_schema && _schema.Name) {
                parts.push(_schema.Name + (_schema.Iso ? ' (' + _schema.Iso + ')' : ''));
            }

            return parts.join(' · ');
        }

        function closeModal() {
            if (!_modalOpen) { return; }
            _modalOpen = false;
            _detailSeq++;                       // drop any page still in flight
            showModalBusy(false);
            if ($overlay) { $overlay.addClass('vas-195-hidden'); }
            $(document).off('keydown' + _ns + 'm');

            /* Focus goes back to the row that opened the dialog - a keyboard user must
               not be dropped at the top of the document. */
            if (_returnFocusTo) {
                try { _returnFocusTo.focus(); } catch (e) { }
                _returnFocusTo = null;
            }
        }

        function onModalKeyDown(e) {
            if (e.key === 'Escape' || e.keyCode === 27) { closeModal(); }
        }

        function showModalBusy(show) {
            if (!$modalBusy || !$modalBusy[0]) { return; }
            $modalBusy.toggleClass('vas-195-hidden', !show);
        }

        function loadModalPage() {
            var mySeq = ++_detailSeq;
            var periodId = _periodId;
            var checkCode = _checkCode;

            showModalBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_195_MandatoryChecklistWidget/GetDetail',
                type: 'GET',
                cache: false,
                data: {
                    periodId: periodId,
                    checkCode: checkCode,
                    pageNo: _page,
                    pageSize: MODAL_PAGE_SIZE
                },
                success: function (res) {
                    /* Stale response: another page, another check, another period, or
                       the modal has been closed since. */
                    if (mySeq !== _detailSeq || periodId !== _periodId || checkCode !== _checkCode) { return; }

                    var data = null;
                    try { data = parseResponse(res); } catch (e) { }

                    if (!data || data.error) {
                        renderModalState(label('VAS_192_CouldntLoad', "Couldn't load"), true);
                        return;
                    }

                    if (data.ErrorCode) {
                        renderModalState(errorLabel(data.ErrorCode), data.ErrorCode !== 'NODETAIL');
                        return;
                    }

                    if (data.Schema) { _schema = data.Schema; }

                    _columns = data.Columns || [];
                    _hasScreenColumn = false;
                    for (var i = 0; i < _columns.length; i++) {
                        if (_columns[i].Type === 'SCREEN') { _hasScreenColumn = true; break; }
                    }

                    _page = data.PageNo || 1;
                    renderModalRows(data);
                },
                error: function () {
                    if (mySeq !== _detailSeq) { return; }
                    renderModalState(label('VAS_192_CouldntLoad', "Couldn't load"), true);
                },
                complete: function () {
                    if (mySeq === _detailSeq) { showModalBusy(false); }
                }
            });
        }

        function renderModalState(text, isError) {
            $modalBody.html('<div class="vas-195-modal-state' + (isError ? ' vas-195-modal-state-error' : '') +
                '">' + escapeHtml(text) + '</div>');
            renderModalPager({ Total: 0, PageSize: MODAL_PAGE_SIZE, PageNo: 1, Rows: [] });
        }

        /* The grid template is composed from the server's own column weights, so a
           check with four columns and a check with eleven both lay out correctly with
           no per-check CSS. */
        function gridTemplate() {
            var parts = [];
            for (var i = 0; i < _columns.length; i++) {
                var weight = Number(_columns[i].Weight);
                if (!isFinite(weight) || weight <= 0) { weight = 1; }
                parts.push('minmax(0,' + weight + 'fr)');
            }
            return parts.join(' ');
        }

        function columnCaption(column) {
            /* A column keyed to a real dictionary column is captioned the way the rest
               of the product captions it. */
            return colLabel(column.LabelKey, column.LabelKey, column.LabelText || column.Key);
        }

        function isNumericType(type) {
            return type === 'AMOUNT' || type === 'DOCAMOUNT' || type === 'QTY' || type === 'NUMBER';
        }

        function renderModalRows(data) {
            var rows = data.Rows || [];

            if (rows.length === 0 || _columns.length === 0) {
                renderModalState(label('VAS_195_NoRecords', 'No records for this check.'), false);
                return;
            }

            var template = gridTemplate();

            var head = '<div class="vas-195-dhead" style="grid-template-columns:' + template + '">';
            for (var c = 0; c < _columns.length; c++) {
                var caption = columnCaption(_columns[c]);
                head += '<span class="vas-195-dcell' +
                    (isNumericType(_columns[c].Type) ? ' vas-195-dcell-num' : '') +
                    '" title="' + escapeHtml(caption) + '">' + escapeHtml(caption) + '</span>';
            }
            head += '</div>';

            var body = '';
            for (var i = 0; i < rows.length; i++) {
                body += buildDetailRow(rows[i], template);
            }

            $modalBody.html('<div class="vas-195-dgrid">' + head + body + '</div>');
            $modalBody.scrollTop(0);

            syncModalHeight(rows.length);
            renderModalPager(data);
        }

        function buildDetailRow(row, template) {
            var html = '<div class="vas-195-drow" style="grid-template-columns:' + template + '">';

            for (var c = 0; c < _columns.length; c++) {
                html += buildCell(row, _columns[c]);
            }

            return html + '</div>';
        }

        /* One cell, formatted by its DECLARED type. The two document-ish types are the
           only ones that read from the row rather than from the cell map: SCREEN and
           DOC are resolved server-side by the shared metadata resolver and live
           alongside the cells, not inside them. */
        function buildCell(row, column) {
            var type = column.Type;
            var cells = row.Cells || {};
            var raw = cells[column.Key];

            if (type === 'SCREEN') {
                return textCell(row.ScreenDisplayName || '');
            }

            if (type === 'DOC') {
                return docCell(row, raw);
            }

            if (type === 'DATE') { return textCell(formatDate(raw)); }
            if (type === 'AMOUNT') { return textCell(formatAmount(raw, true), 'vas-195-dcell-num vas-195-dcell-amt'); }
            if (type === 'DOCAMOUNT') { return textCell(formatAmount(raw, false), 'vas-195-dcell-num vas-195-dcell-amt'); }
            if (type === 'QTY') { return textCell(formatQty(raw), 'vas-195-dcell-num'); }
            if (type === 'NUMBER') { return textCell(formatNumber(raw), 'vas-195-dcell-num'); }

            if (type === 'BADGE') {
                var code = raw == null ? '' : String(raw);
                if (!code) { return textCell(''); }

                var text = badgeText(code);
                return '<span class="vas-195-dcell">' +
                    '<span class="vas-195-dbadge" title="' + escapeHtml(text) + '">' +
                        escapeHtml(text) + '</span></span>';
            }

            return textCell(raw == null ? '' : String(raw));
        }

        function textCell(text, extraClass) {
            return '<span class="vas-195-dcell' + (extraClass ? ' ' + extraClass : '') +
                '" title="' + escapeHtml(text) + '">' + escapeHtml(text) + '</span>';
        }

        /* A badge carries a STORED code, and most of them have no translation to offer -
           a DocStatus reads 'CO' on every installation and is shown as it is stored, the
           way the rest of the product shows it. The ledger side is the exception: the
           server emits 'Debit' / 'Credit' as stable tokens precisely so this can put them
           through the message table, which a string composed in SQL could never be. An
           unseeded key falls back to the token, which is already readable English. */
        var BADGE_MESSAGE_KEYS = { 'Debit': 'VAS_195_Debit', 'Credit': 'VAS_195_Credit' };

        function badgeText(code) {
            var key = BADGE_MESSAGE_KEYS[code];
            return key ? label(key, code) : code;
        }

        /* A document number rendered as a link. A button, not an anchor: there is no
           URL to follow - the framework opens the window. The SERVER decides whether
           the row is navigable at all (an active window the role may open, a real
           record, and a key column to position by); without that it stays plain text
           rather than offering a dead link. Where the check supplied its own value in
           the cell (a recurring name, a period name) that wins over the resolver's.

           When the check declares NO Screen column - checks 01 and 02, whose rows are
           already grouped by screen - the screen name goes UNDER the document number
           instead. It is the same fact either way; a second line inside a cell that is
           already there costs no column width, and a reader landing mid-list still
           knows which screen a document belongs to without scrolling back to find the
           head of its group. */
        function docCell(row, raw) {
            var text = raw != null && String(raw) !== ''
                ? String(raw)
                : (row.DocumentDisplayValue || '');

            var screen = (!_hasScreenColumn && row.ScreenDisplayName) ? row.ScreenDisplayName : '';
            var sub = screen
                ? '<span class="vas-195-docsub" title="' + escapeHtml(screen) + '">' +
                      escapeHtml(screen) + '</span>'
                : '';

            if (!row.CanNavigate) {
                return '<span class="vas-195-dcell vas-195-dcell-doc">' +
                    '<span class="vas-195-docmain vas-195-dcell-b" title="' + escapeHtml(text) + '">' +
                        escapeHtml(text) + '</span>' + sub +
                '</span>';
            }

            return '<span class="vas-195-dcell vas-195-dcell-doc">' +
                '<button type="button" class="vas-195-doclink"' +
                    ' data-key="' + escapeHtml(row.KeyColumnName || '') + '"' +
                    ' data-record="' + (row.Record_ID || 0) + '"' +
                    ' data-window="' + (row.AD_Window_ID || 0) + '"' +
                    ' title="' + escapeHtml(text) + '">' + escapeHtml(text) + '</button>' +
                sub +
            '</span>';
        }

        /* Grows the fixed body to whatever a full page actually measures, so the last
           page never introduces a scrollbar the first page did not have. Only ever
           grows: shrinking back on a short page is the fluctuation this prevents. */
        function syncModalHeight(rowCount) {
            if (!$modalBody || !$modalBody[0] || rowCount <= 0) { return; }

            var headEl = $modalBody[0].querySelector('.vas-195-dhead');
            var rowEls = $modalBody[0].querySelectorAll('.vas-195-drow');
            if (!rowEls.length) { return; }

            var rowH = 0;
            for (var i = 0; i < rowEls.length; i++) {
                if (rowEls[i].offsetHeight > rowH) { rowH = rowEls[i].offsetHeight; }
            }
            if (rowH <= 0) { return; }

            var headH = headEl ? headEl.offsetHeight : MODAL_HEAD_H;
            /* Capped at 12 rows: a 25-row page would make the dialog taller than most
               viewports, and the body scrolls for the remainder. */
            var needed = headH + (rowH * Math.min(rowCount, 12)) + MODAL_BODY_SLACK;

            if (needed > _bodyH) {
                _bodyH = needed;
                $modalBody.css('height', _bodyH + 'px');
            }
        }

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
                '<span class="vas-195-pager-info" title="' + escapeHtml(showing) + '">' +
                    escapeHtml(showing) + '</span>' +
                '<span class="vas-195-pager-nav">' +
                    '<button type="button" class="vas-195-pgbtn" data-dir="prev" aria-label="' +
                        escapeHtml(label('VAS_026_Prev', 'Previous')) + '"' + prevDis + '>' + icon('chevL') + '</button>' +
                    '<span class="vas-195-pager-label">' + pageNo + ' ' + escapeHtml(ofTxt) + ' ' + totalPages + '</span>' +
                    '<button type="button" class="vas-195-pgbtn" data-dir="next" aria-label="' +
                        escapeHtml(label('VAS_026_Next', 'Next')) + '"' + nextDis + '>' + icon('chevNext') + '</button>' +
                '</span>'
            );
        }

        /* Opens a record in its own standard window. The window id and the key column
           both come from the server - it resolves the source table's primary window and
           confirms the role may open it - so this widget can sit on any dashboard, far
           from the screen the record belongs to, and still land on it. No window NAME
           is involved and no AD_Window_ID is hard-coded. Degrades silently: a click can
           never throw. */
        function zoomTo(keyColumnName, recordId, windowId) {
            if (!keyColumnName || recordId <= 0 || windowId <= 0 || !VAS.ZoomUtil) { return; }

            closeModal();

            try {
                VAS.ZoomUtil.zoomToRecord(keyColumnName, recordId, windowId, '', '');
            } catch (e) {
                if (window.console) { console.log(e); }
                showError(label('VAS_192_CouldntLoad', "Couldn't load"));
            }
        }

        // ── Framework contract ───────────────────────────────────────────────

        this.refreshWidget = function () {
            /* Refresh means "start clean": drop the open panels and re-read everything,
               because periods and setup may have changed since this card last loaded. */
            closePicker();
            closeModal();

            _periods = [];
            _periodId = 0;
            _periodName = '';
            _data = null;
            _schema = null;
            _cardPage = 1;

            loadBootstrap();
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            if ($list) { $list.off(); }
            if ($foot) { $foot.off(); }
            if ($periodBtn) { $periodBtn.off(); }

            /* Both overlays were appended to <body>, so removing $root would leave them
               behind - close each (which unbinds the document/window handlers under this
               instance's namespace) and tear them down explicitly. */
            closePicker();
            if ($picker) {
                $picker.off();
                $picker.remove();
                $picker = null;
            }

            _returnFocusTo = null;

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

    VAS.VAS_195_MandatoryChecklistWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_195_MandatoryChecklistWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_195_MandatoryChecklistWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_195_MandatoryChecklistWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_195_MandatoryChecklistWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
