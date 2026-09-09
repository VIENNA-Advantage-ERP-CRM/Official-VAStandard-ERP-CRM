/************************************************************
 * Module Name    : VAS
 * Purpose        : Largest Budget Variances - a 3x2 paginated list for the
 *                  Budgeting dashboard.
 *
 *                  The accounts whose approved budget and posted actual are furthest
 *                  apart, in BOTH directions:
 *
 *                    [↕] Largest budget variances        [ FY 2026 v ]
 *                        Ranked by absolute variance
 *
 *                    IT services                              +$480K
 *                    Budget $1.9M · actual $1.42M
 *
 *                    Cloud infrastructure                     −$117K
 *                    Budget $612K · actual $729K
 *
 *                    Showing 1–3 of 24                    <  1 of 8  >
 *
 *                  TWO-LINE ROWS, NOT A TABLE. The account is the row's identity and
 *                  takes the first line; the budget and actual it is derived from sit
 *                  under it as a meta line at the smaller tier; the variance is the
 *                  row's conclusion and sits right-aligned against the trailing edge.
 *                  A three-column table of the same figures would give the two
 *                  supporting numbers the same weight as the answer.
 *
 *                  BOTH DIRECTIONS, RANKED TOGETHER. Rows are ordered by ABSOLUTE
 *                  variance, so a large underspend and a large overrun sit next to
 *                  each other - the card is about the SIZE of the gap, not its
 *                  direction. Favourable (came in under budget) is green with an
 *                  explicit +, unfavourable is red with an explicit −. THE SIGN IS
 *                  NEVER LEFT TO COLOUR ALONE.
 *
 *                  NOTHING IS INFERRED. No burn rate, no run rate, no projected
 *                  exhaustion date - all of those need an assumed spending pattern,
 *                  and this card deliberately has none. It is budget minus actual.
 *
 *                  THE ROWS ARE NOT INTERACTIVE. There is no drill-down here and no
 *                  row is a button: the card answers "where are the biggest gaps",
 *                  which the two lines already say in full. That is a deliberate
 *                  difference from VAS_256, whose rows DO open a transaction list.
 *
 *                  ONE CURRENCY, THE SCHEMA'S OWN. Every figure is an accounting
 *                  amount in the primary accounting schema's currency, printed with
 *                  that currency's symbol against the number - "$480K", never
 *                  "USD 480K" - falling back to the ISO code only when the currency
 *                  has no symbol. Nothing is converted and no scale is assumed: the
 *                  ISO code decides whether the compact form steps in lakh/crore or
 *                  thousand/million.
 *
 *                  Design: design.md -> dashboard-widgets.md (Glass Widget, Widget
 *                  Header, Widget Footer Pager, Content Fit Budget, No Inner
 *                  Scrollbars) supplies the shell and the pager; the widget
 *                  specification supplies the two-line row and the signed variance.
 *
 *                  Summary Message Table
 *                  Rows marked (reuse) already exist under another key and are NOT
 *                  duplicated here.
 *                   # | Current Text                       | Message Key
 *                  ---+------------------------------------+--------------------------
 *                   1 | Largest budget variances           | VAS_254_LargestVariance
 *                   2 | Ranked by absolute variance        | VAS_254_VarianceHint
 *                   3 | actual                             | VAS_254_ActualLower
 *                   4 | Variance                           | VAS_254_Variance
 *                   5 | No budget variances found for the  | VAS_254_NoVariances
 *                     |   selected financial year.         |
 *                   6 | Budget                             | VAS_254_Budget
 *                   7 | Financial year                     | VAS_256_FinancialYear   (reuse)
 *                   8 | No financial years available       | VAS_256_NoYears         (reuse)
 *                   9 | No primary calendar is configured  | VAS_256_NoCalendar      (reuse)
 *                  10 | No primary accounting schema is    | VAS_256_NoAcctSchema    (reuse)
 *                     |   configured                       |
 *                  11 | Showing                            | VAS_020_Showing         (reuse)
 *                  12 | of                                 | VAS_020_Of              (reuse)
 *                  13 | Previous                           | VAS_020_Prev            (reuse)
 *                  14 | Next                               | VAS_020_Next            (reuse)
 *                  15 | Couldn't load                      | VAS_192_CouldntLoad     (reuse)
 *
 * Chronological development:
 *   VAI154         Created  Date 2026-09-08
 ***********************************************************/
; VAS = window.VAS || {};

; (function (VAS, $) {

    /* CSS lives in VAS/Areas/VAS/Content/VAS_254_LargestBudgetVarianceWidget.css. All
       classes are namespaced `vas-254-` so they never collide with sibling widgets. */

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
        /* A two-headed vertical arrow: the card measures a gap that runs in both
           directions, which is exactly what this card is for. */
        variance: '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" ' +
            'stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
            '<path d="M12 3v18"></path><path d="m7 8 5-5 5 5"></path><path d="M7 16l5 5 5-5"></path></svg>',
        chevron: '<svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" ' +
            'stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
            '<polyline points="6 9 12 15 18 9"></polyline></svg>',
        tick: '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" ' +
            'stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
            '<polyline points="20 6 9 17 4 12"></polyline></svg>',
        prev: '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" ' +
            'stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
            '<polyline points="15 18 9 12 15 6"></polyline></svg>',
        next: '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" ' +
            'stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
            '<polyline points="9 18 15 12 9 6"></polyline></svg>'
    };

    /* The missing-value placeholder. A GLYPH rather than a word: it needs no AD_Message
       key and reads the same in every language. */
    var NIL = '-';

    /* Paging. Three rows fit at 1280px; the adaptive fit may ask for more in a taller
       cell, and the server clamps whatever is asked for. */
    var DEFAULT_PAGE_SIZE = 3;
    var MIN_PAGE_SIZE = 1;
    var MAX_PAGE_SIZE = 12;

    /* A two-line row is taller than a single-line one - this is only the pre-measurement
       fallback, replaced by the tallest rendered row on the first paint. */
    var ROW_HEIGHT_FALLBACK = 56;

    VAS.VAS_254_LargestBudgetVarianceWidget = function () {
        this.frame;
        this.windowNo;
        this.widgetInfo;

        var $self = this;
        var $root;
        var $card;
        var $yearBtn;
        var $list;
        var $foot;
        var $state;
        var $busy;
        var $picker;

        var widgetID = 0;

        /* Unique event namespace per instance - a widget can sit twice on one dashboard,
           and the picker binds document-level handlers. */
        var _ns = '';

        var _rows = [];
        var _years = [];
        var _schema = null;
        var _yearId = 0;
        var _fiscalYear = '';
        var _page = 1;
        var _pageSize = DEFAULT_PAGE_SIZE;
        var _totalRows = 0;
        var _totalPages = 0;

        var _rowH = 0;
        var _needsSync = false;
        var _loading = false;
        var _pickerOpen = false;
        var _disposed = false;
        var _rootObserver = null;
        var _listObserver = null;

        /* ------------------------------------------------------------ */
        /* Lifecycle                                                    */
        /* ------------------------------------------------------------ */
        this.initalize = function () {
            widgetID = (VIS.Utility && VIS.Utility.Util
                ? VIS.Utility.Util.getValueOfInt($self.widgetInfo.AD_UserHomeWidgetID)
                : 0);
            if (widgetID === 0) { widgetID = $self.windowNo; }
            _ns = '.vas254_' + widgetID;

            buildSkeleton();
            createBusyIndicator();
            setupRootObserver();
        };

        /* The framework's own widget loader, overlaid on the whole card while a read is in
           flight - the same treatment every sibling VAS widget gives its loads. It covers
           EVERY read: the initial load, the Refresh button, a page turn and a year change.
           Created visible so it is already up from the moment the widget mounts. */
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

        this.intialLoad = function () {
            fetchPage(1);
        };

        /* The dashboard's Refresh button calls this. It goes back to page 1: a refresh
           re-reads the whole year, postings may have landed since the last load, and the
           page the user was on is not reliably the same page afterwards. The chosen YEAR is
           kept - that is a filter the user set, not a position in a list. */
        this.refreshWidget = function () {
            closePicker();
            fetchPage(1);
        };

        /* ------------------------------------------------------------ */
        /* DOM skeleton                                                 */
        /* ------------------------------------------------------------ */
        function buildSkeleton() {
            $root = $('<div class="vas-254-root" id="vas-254-root-' + widgetID + '"></div>');

            var title = label('VAS_254_LargestVariance', 'Largest budget variances');
            var subtitle = label('VAS_254_VarianceHint', 'Ranked by absolute variance');

            $card = $(
                '<div class="vas-254-card">' +
                    '<div class="vas-254-header">' +
                        '<span class="vas-254-icon">' + ICONS.variance + '</span>' +
                        '<div class="vas-254-head-text">' +
                            '<div class="vas-254-title"></div>' +
                            '<div class="vas-254-subtitle"></div>' +
                        '</div>' +
                        '<button type="button" class="vas-254-year" aria-haspopup="listbox">' +
                            '<span class="vas-254-year-label"></span>' +
                            ICONS.chevron +
                        '</button>' +
                    '</div>' +
                    '<div class="vas-254-body">' +
                        '<div class="vas-254-list" role="list"></div>' +
                        '<div class="vas-254-pagerwrap"></div>' +
                    '</div>' +
                    '<div class="vas-254-state vas-254-hidden"></div>' +
                '</div>'
            );

            $card.find('.vas-254-title').text(title).attr('title', title);
            /* The subtitle truncates in a 3-column cell that is already sharing its header
               row with the year pill, so the full sentence also goes on the title attribute
               - it moves to the tooltip rather than being lost. */
            $card.find('.vas-254-subtitle').text(subtitle).attr('title', subtitle);
            $card.find('.vas-254-list').attr('aria-label', title);

            $yearBtn = $card.find('.vas-254-year');
            $list = $card.find('.vas-254-list');
            $foot = $card.find('.vas-254-pagerwrap');
            $state = $card.find('.vas-254-state');

            paintYearLabel();

            $yearBtn.on('click' + _ns, function (e) {
                e.preventDefault();
                e.stopPropagation();
                togglePicker();
            });

            $root.append($card);
        }

        /* ------------------------------------------------------------ */
        /* Data                                                         */
        /* ------------------------------------------------------------ */
        function fetchPage(pageNo) {
            if (_loading) { return; }
            _loading = true;
            showBusyIndicator();

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_254_LargestBudgetVarianceWidget/GetRows',
                type: 'GET',
                dataType: 'json',
                cache: false,
                /* Asynchronous, always - nothing here justifies blocking the UI thread. */
                async: true,
                data: { yearId: _yearId, pageNo: pageNo, pageSize: _pageSize },
                success: function (raw) {
                    _loading = false;
                    if (_disposed) { return; }
                    hideBusyIndicator();

                    var data = parseResponse(raw);
                    if (!data || data.error) {
                        renderState(label('VAS_192_CouldntLoad', "Couldn't load"));
                        return;
                    }

                    /* A configuration gap is a different answer from a failed read, and it
                       says which piece of setup is missing rather than "couldn't load". */
                    if (data.ErrorCode) {
                        renderState(errorLabel(data.ErrorCode));
                        return;
                    }

                    if (!data.Loaded) {
                        renderState(label('VAS_192_CouldntLoad', "Couldn't load"));
                        return;
                    }

                    _schema = data.Schema || null;

                    /* The year list comes back with every read, so a newly opened financial
                       year appears on the next refresh without a second endpoint. */
                    _years = data.Years || [];
                    _yearId = Number(data.C_Year_ID) || 0;
                    _fiscalYear = data.FiscalYear || '';
                    paintYearLabel();

                    _rows = data.Rows || [];
                    _page = Number(data.Page) || 1;
                    _pageSize = Number(data.PageSize) || _pageSize;
                    _totalRows = Number(data.TotalRows) || 0;
                    _totalPages = Number(data.TotalPages) || 0;

                    paintRows();
                    observeList();
                },
                error: function () {
                    _loading = false;
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
        /* Render                                                       */
        /* ------------------------------------------------------------ */

        /* A load failure or a missing configuration takes the card over. Having no
           variances does NOT - that is handled inside the list, so the header and the
           subtitle stay put. */
        function renderState(text) {
            $card.find('.vas-254-body').addClass('vas-254-hidden');
            $state.removeClass('vas-254-hidden').text(text);
        }

        function errorLabel(code) {
            if (code === 'NOCALENDAR') {
                return label('VAS_256_NoCalendar', 'No primary calendar is configured');
            }
            if (code === 'NOACCTSCHEMA') {
                return label('VAS_256_NoAcctSchema', 'No primary accounting schema is configured');
            }
            if (code === 'NOYEAR') {
                return label('VAS_256_NoYears', 'No financial years available');
            }
            return label('VAS_192_CouldntLoad', "Couldn't load");
        }

        function paintYearLabel() {
            var text = _fiscalYear || yearNameOf(_yearId);
            $yearBtn.find('.vas-254-year-label').text(text);
            $yearBtn.attr('title', label('VAS_256_FinancialYear', 'Financial year') + ': ' + text);
        }

        function yearNameOf(id) {
            for (var i = 0; i < _years.length; i++) {
                if (Number(_years[i].C_Year_ID) === id) { return _years[i].FiscalYear || ''; }
            }
            return '';
        }

        function paintRows() {
            $state.addClass('vas-254-hidden');
            $card.find('.vas-254-body').removeClass('vas-254-hidden');

            if (!_rows || _rows.length === 0) {
                /* A year with nothing budgeted or posted is a real answer, not an error -
                   and with no rows there is nothing to page, so the footer goes too. */
                $list.html('<div class="vas-254-empty">' +
                    escapeHtml(label('VAS_254_NoVariances',
                        'No budget variances found for the selected financial year.')) + '</div>');
                $foot.empty();
                return;
            }

            var html = '';
            for (var i = 0; i < _rows.length; i++) { html += rowHtml(_rows[i]); }
            $list.html(html);

            paintFooter();

            /* Adapt the page size to the list height ONLY on the first paint / after a
               resize - never on manual navigation, which would flip pageSize under the user
               and re-clamp the page they just moved to. */
            if (_needsSync) { scheduleSync(); }
        }

        /* A two-line row: the account on top, the two figures it is derived from beneath,
           and the variance right-aligned as the row's conclusion. Not a button - this card
           has no drill-down, so nothing here should look activatable. */
        function rowHtml(item) {
            var account = accountText(item);
            var meta = metaText(item);
            var variance = Number(item.Variance) || 0;

            /* Favourable = came in UNDER budget. The tone and the printed sign say the same
               thing twice, so the reading never rests on colour alone. */
            var tone = variance < 0 ? 'vas-254-neg' : 'vas-254-pos';

            return '<div class="vas-254-brow" role="listitem" title="' +
                        escapeHtml(rowTooltip(item, account)) + '">' +
                '<span class="vas-254-l">' +
                    lineCell('vas-254-name', account) +
                    lineCell('vas-254-meta', meta) +
                '</span>' +
                '<span class="vas-254-r">' +
                    '<span class="vas-254-var ' + tone + '" title="' +
                        escapeHtml(signedFullAmount(variance)) + '">' +
                        escapeHtml(signedCompactAmount(variance)) +
                    '</span>' +
                '</span>' +
            '</div>';
        }

        /* One line of the row's left column. An empty value renders the missing-value dash
           rather than collapsing the line, which would change the row's height and unsettle
           the adaptive row fit. */
        function lineCell(cls, text) {
            if (isBlank(text)) {
                return '<span class="' + cls + ' vas-254-nil">' + NIL + '</span>';
            }

            return '<span class="' + cls + '" title="' + escapeHtml(text) + '">' +
                escapeHtml(text) + '</span>';
        }

        /* "{Account Value} — {Account Name}". Either half may be missing on a badly seeded
           chart of accounts, so the em dash is only printed when there are two sides to it,
           and a row with neither falls through to the missing-value dash. */
        function accountText(item) {
            var value = item.AccountValue || '';
            var name = item.AccountName || '';

            if (value && name) { return value + ' — ' + name; }
            return value || name;
        }

        /* "Budget $1.9M · actual $1.42M" - the two figures the variance is derived from,
           so the reader can see WHY the gap is what it is without a second card. Composed
           from the same two numbers the variance was computed from server-side, so the meta
           line and the trailing value can never disagree. */
        function metaText(item) {
            return label('VAS_254_Budget', 'Budget') + ' ' + compactAmount(Number(item.Budget) || 0) +
                ' · ' + label('VAS_254_ActualLower', 'actual') + ' ' +
                compactAmount(Number(item.Actual) || 0);
        }

        /* Everything the row holds, at full precision - the cells above are compact. */
        function rowTooltip(item, account) {
            var lines = [];

            lines.push(isBlank(account) ? NIL : account);
            lines.push(label('VAS_254_Budget', 'Budget') + ': ' + fullAmount(Number(item.Budget) || 0));
            lines.push(label('VAS_254_ActualLower', 'actual') + ': ' + fullAmount(Number(item.Actual) || 0));
            lines.push(label('VAS_254_Variance', 'Variance') + ': ' +
                signedFullAmount(Number(item.Variance) || 0));

            return lines.join('\n');
        }

        /* ---- Canonical Widget Footer Pager (design.md): "Showing a–b of N" left, compact
             prev / next control right. Hidden on a single page. ---- */
        function paintFooter() {
            if (_totalPages <= 1) { $foot.empty(); return; }

            var from = (_page - 1) * _pageSize + 1;
            var to = Math.min(_page * _pageSize, _totalRows);

            var showing = label('VAS_020_Showing', 'Showing') + ' ' + from + '–' + to + ' ' +
                label('VAS_020_Of', 'of') + ' ' + _totalRows;

            var prevDis = _page <= 1 ? ' disabled' : '';
            var nextDis = _page >= _totalPages ? ' disabled' : '';

            $foot.html(
                '<div class="vas-254-pager">' +
                    '<span class="vas-254-pager-info" title="' + escapeHtml(showing) + '">' +
                        escapeHtml(showing) + '</span>' +
                    '<div class="vas-254-pager-nav">' +
                        '<button type="button" class="vas-254-pgbtn vas-254-pg-prev" aria-label="' +
                            escapeHtml(label('VAS_020_Prev', 'Previous')) + '"' + prevDis + '>' + ICONS.prev + '</button>' +
                        '<span class="vas-254-pager-label">' + _page + ' ' +
                            escapeHtml(label('VAS_020_Of', 'of')) + ' ' + _totalPages + '</span>' +
                        '<button type="button" class="vas-254-pgbtn vas-254-pg-next" aria-label="' +
                            escapeHtml(label('VAS_020_Next', 'Next')) + '"' + nextDis + '>' + ICONS.next + '</button>' +
                    '</div>' +
                '</div>'
            );

            /* A page change keeps the year filter - only the page number moves. */
            $foot.find('.vas-254-pg-prev').on('click', function () {
                if (!_loading && _page > 1) { fetchPage(_page - 1); }
            });
            $foot.find('.vas-254-pg-next').on('click', function () {
                if (!_loading && _page < _totalPages) { fetchPage(_page + 1); }
            });
        }

        /* ------------------------------------------------------------ */
        /* Adaptive row capacity (the VAS_020 pattern)                  */
        /* ------------------------------------------------------------ */
        function scheduleSync() {
            var raf = window.requestAnimationFrame || function (cb) { return window.setTimeout(cb, 16); };
            raf(function () { syncCapacity(); });
        }

        function syncCapacity() {
            if (_disposed || _loading || !$list || !$list[0]) { return; }
            if (!_rows || _rows.length === 0) { return; }

            var avail = $list[0].clientHeight;
            if (avail <= 0) {
                /* Layout has not settled yet - try again on the next frame. */
                if (_needsSync) { scheduleSync(); }
                return;
            }

            /* Size off the TALLEST rendered row. Every row here is two lines, but a long
               account name can still wrap the first one on a narrow cell. */
            var rendered = $list[0].querySelectorAll('.vas-254-brow');
            var maxH = 0;
            for (var i = 0; i < rendered.length; i++) {
                if (rendered[i].offsetHeight > maxH) { maxH = rendered[i].offsetHeight; }
            }
            if (maxH > 0) { _rowH = maxH; }
            var rowH = _rowH > 0 ? _rowH : ROW_HEIGHT_FALLBACK;

            _needsSync = false;

            var capacity = Math.floor(avail / rowH);
            if (capacity < MIN_PAGE_SIZE) { capacity = MIN_PAGE_SIZE; }
            if (capacity > MAX_PAGE_SIZE) { capacity = MAX_PAGE_SIZE; }

            if (capacity !== _pageSize) {
                _pageSize = capacity;
                fetchPage(_page);
            }
        }

        function observeList() {
            if (typeof ResizeObserver === 'undefined' || !$list || !$list[0]) { return; }
            if (_listObserver) { try { _listObserver.disconnect(); } catch (e) { /* ignore */ } }

            _listObserver = new ResizeObserver(function () {
                if (_disposed || _loading) { return; }
                _needsSync = true;
                syncCapacity();
            });
            _listObserver.observe($list[0]);
        }

        /* ------------------------------------------------------------ */
        /* Financial year picker - anchored under the pill, on <body>    */
        /* ------------------------------------------------------------ */
        function buildPicker() {
            $picker = $('<div class="vas-254-pp vas-254-hidden" role="listbox" aria-label="' +
                escapeHtml(label('VAS_256_FinancialYear', 'Financial year')) + '"></div>');
            $('body').append($picker);

            $picker.on('click', '.vas-254-pp-opt', function () {
                var id = parseInt($(this).attr('data-id'), 10) || 0;
                closePicker();
                selectYear(id);
            });
        }

        function fillPicker() {
            var html = '<div class="vas-254-pp-h">' +
                escapeHtml(label('VAS_256_FinancialYear', 'Financial year')) + '</div>';

            if (_years.length === 0) {
                html += '<div class="vas-254-pp-empty">' +
                    escapeHtml(label('VAS_256_NoYears', 'No financial years available')) + '</div>';
            }

            for (var i = 0; i < _years.length; i++) {
                var y = _years[i];
                html += optionHtml(Number(y.C_Year_ID) || 0, y.FiscalYear || '');
            }

            $picker.html(html);
        }

        function optionHtml(id, text) {
            var selected = id === _yearId;

            return '<button type="button" class="vas-254-pp-opt" role="option" data-id="' + id +
                    '" aria-selected="' + (selected ? 'true' : 'false') + '">' +
                '<span class="vas-254-pp-name" title="' + escapeHtml(text) + '">' +
                    escapeHtml(text) + '</span>' +
                '<span class="vas-254-pp-tick">' + ICONS.tick + '</span>' +
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
            if (!$picker || !$yearBtn || !$yearBtn[0]) { return; }

            var rect = $yearBtn[0].getBoundingClientRect();
            var gap = 6;
            var edge = 8;

            var roomBelow = window.innerHeight - rect.bottom - gap - edge;
            var roomAbove = rect.top - gap - edge;

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
            $picker.removeClass('vas-254-hidden');
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
            if ($picker) { $picker.addClass('vas-254-hidden'); }

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
            if ($yearBtn[0] && $yearBtn[0].contains(e.target)) { return; }
            closePicker();
        }

        function onPickerKeyDown(e) {
            if (e.key === 'Escape' || e.keyCode === 27) { closePicker(); }
        }

        /* Changing the year re-reads from page 1 - the row the user was looking at is not
           on the same page of a different year, and the ranking has to be recomputed. */
        function selectYear(id) {
            if (id <= 0 || id === _yearId) { return; }

            _yearId = id;
            _fiscalYear = yearNameOf(id);
            paintYearLabel();
            fetchPage(1);
        }

        /* ------------------------------------------------------------ */
        /* Formatting - the accounting schema's own currency            */
        /* ------------------------------------------------------------ */
        function precision() {
            var p = _schema ? Number(_schema.Precision) : NaN;
            return (isNaN(p) || p < 0 || p > 6) ? 2 : p;
        }

        function symbol() {
            return (_schema && _schema.Symbol) ? _schema.Symbol : '';
        }

        function isoCode() {
            return (_schema && _schema.Iso) ? _schema.Iso : '';
        }

        /* The compact magnitude with the currency SYMBOL printed directly against it -
           "$1.9M", "₹74K" - never "USD 1.9M", and only falling back to the ISO code when
           the currency has no symbol configured (which is what the server's own
           COALESCE(CurSymbol, ISO_Code) already resolved). The ISO code drives the scale,
           so no lakh/crore or million step is ever assumed here. */
        function compactAmount(value) {
            var v = Number(value) || 0;
            var magnitude;

            try {
                if (VIS.Util && typeof VIS.Util.formatCompactAmount === 'function') {
                    magnitude = VIS.Util.formatCompactAmount(v, isoCode(), precision());
                }
            }
            catch (e) { if (window.console) { console.log(e); } }

            if (magnitude === undefined) { magnitude = String(Math.abs(v)); }

            return (v < 0 ? '−' : '') + symbol() + magnitude;
        }

        /* The variance, with its sign ALWAYS printed - a plus on a favourable gap as much
           as a minus on an unfavourable one. The sign is what carries the direction when
           the colour cannot: in print, for a colour-blind reader, or anywhere the tone is
           lost. A true minus sign, not a hyphen. */
        function signedCompactAmount(value) {
            var v = Number(value) || 0;
            var magnitude;

            try {
                if (VIS.Util && typeof VIS.Util.formatCompactAmount === 'function') {
                    magnitude = VIS.Util.formatCompactAmount(v, isoCode(), precision());
                }
            }
            catch (e) { if (window.console) { console.log(e); } }

            if (magnitude === undefined) { magnitude = String(Math.abs(v)); }

            return (v < 0 ? '−' : '+') + symbol() + magnitude;
        }

        /* Full, non-compact amount for the tooltips: the exact figure behind the compact
           cell. Grouping and the decimal separator come from the browser locale, the
           decimals from the schema currency's precision. */
        function fullAmount(value) {
            var v = Number(value) || 0;
            var p = precision();
            var text;

            try {
                text = Math.abs(v).toLocaleString(window.navigator.language,
                    { minimumFractionDigits: p, maximumFractionDigits: p });
            }
            catch (e) { text = String(Math.abs(v)); }

            return (v < 0 ? '−' : '') + symbol() + text;
        }

        function signedFullAmount(value) {
            var v = Number(value) || 0;
            var text = fullAmount(v);
            return v < 0 ? text : '+' + text;
        }

        /* ------------------------------------------------------------ */
        /* Helpers                                                      */
        /* ------------------------------------------------------------ */

        /* True when a value is nothing the reader can be shown. A zero is NOT blank: it is
           a figure, and a budget of nought against an actual is the largest variance there
           is. */
        function isBlank(value) {
            return value === null || value === undefined || String(value) === '';
        }

        /* Every database-sourced string - and an account name is user-supplied text - goes
           through here before it reaches the DOM. */
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
            if (_listObserver) {
                try { _listObserver.disconnect(); } catch (e) { /* ignore */ }
                _listObserver = null;
            }

            if ($picker) { $picker.off(); $picker.remove(); $picker = null; }
            if ($busy) { $busy.remove(); $busy = null; }
            if ($yearBtn) { $yearBtn.off(_ns); }
            if ($foot) { $foot.off(); }

            _rows = [];
            _years = [];
        };
    };

    /* ---------------------------------------------------------------- */
    /* Required prototype hooks (same surface as other VAS widgets)     */
    /* ---------------------------------------------------------------- */
    VAS.VAS_254_LargestBudgetVarianceWidget.prototype.init = function (windowNo, frame) {
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

    VAS.VAS_254_LargestBudgetVarianceWidget.prototype.widgetSizeChange = function (widget) {
        this.widgetInfo = widget;
    };

    VAS.VAS_254_LargestBudgetVarianceWidget.prototype.dispose = function () {
        try { this.releasePanel(); } catch (e) { /* ignore */ }
        if (this.frame && typeof this.frame.dispose === 'function') {
            try { this.frame.dispose(); } catch (e) { /* ignore */ }
        }
        this.frame = null;
        this.windowNo = null;
    };

})(VAS, jQuery);
