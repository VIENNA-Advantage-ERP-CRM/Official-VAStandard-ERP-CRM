/************************************************************
 * Module Name    : VAS
 * Purpose        : Budgets Near Limit - a 3x2 paginated grid for the Budgeting
 *                  dashboard.
 *
 *                  The ledger accounts that have consumed most of their approved
 *                  budget but have NOT yet passed it:
 *
 *                    [◎] Budgets near limit                   [ FY 2026 v ]
 *                        23 accounts between 80% and 100% of approved budget
 *
 *                    Account                       Budget   Actual   Used
 *                    11800 — Cash in Registers      $180K    $169K    94%
 *                    61000 — Salaries and wages     $3.12M   $2.87M   92%
 *                    62400 — Travel, domestic       $240K    $218K    91%
 *
 *                    Showing 1–5 of 23                    <  1 of 5  >
 *
 *                  AN EARLY-WARNING CARD, AND THE CEILING IS THE POINT OF IT. An
 *                  account already at or past 100% is an OVERRUN and belongs to
 *                  VAS_252, which reports exactly that; leaving it here would bury
 *                  the accounts still worth acting on under the ones it is already
 *                  too late for. The two cards partition the same population at 100%
 *                  and never show the same account twice.
 *
 *                  THE SUBTITLE COUNTS EVERY MATCHING ACCOUNT, not the page. "23
 *                  accounts between 80% and 100% of approved budget" is a property of
 *                  the whole result, and both the count and the threshold come from
 *                  the server - the card never spells the number out itself, so a
 *                  tenant that warns at 80% sees 80. The 100% is the card's own
 *                  ceiling, stated so the reader knows overruns are NOT in the count.
 *
 *                  USED IS TIERED, THE FIGURES ARE NOT. Below 90% the ratio is
 *                  stated plainly; from 90% it takes the risk tone, because that is
 *                  where the remaining room stops being comfortable. Budget and
 *                  actual stay neutral either way - they are what the ratio is
 *                  derived from, not the finding.
 *
 *                  THE ROWS ARE NOT INTERACTIVE. There is no drill-down here and no
 *                  row is a button: the card answers "what is about to run out",
 *                  which the four cells already say in full. That is a deliberate
 *                  difference from VAS_256, whose rows DO open a transaction list.
 *
 *                  ONE CURRENCY, THE SCHEMA'S OWN, AND ITS SYMBOL. Every figure is
 *                  an accounting amount in the primary accounting schema's currency,
 *                  printed with that currency's SYMBOL against the number - "$180K",
 *                  never "USD 180K" - falling back to the ISO code only when the
 *                  currency has no symbol configured. Nothing is converted and no
 *                  scale is assumed: the ISO code decides whether the compact form
 *                  steps in lakh/crore or thousand/million.
 *
 *                  Design: design.md -> dashboard-widgets.md (Glass Widget, Widget
 *                  Header, Grid Data Rows, Widget Footer Pager, No Inner Scrollbars)
 *                  supplies the shell, the header typography, the row grid and the
 *                  pager; the widget specification supplies the four columns and the
 *                  Used tone tiers.
 *
 *                  Summary Message Table
 *                  Rows marked (reuse) already exist under another key and are NOT
 *                  duplicated here.
 *                   # | Current Text                       | Message Key
 *                  ---+------------------------------------+--------------------------
 *                   1 | Budgets near limit                 | VAS_255_BudgetsNearLimit
 *                   2 | accounts between                   | VAS_255_AccountsBetween
 *                   3 | and 100% of approved budget        | VAS_255_AndFullBudget
 *                   4 | Used                               | VAS_255_Used
 *                   5 | No budgets are near the configured | VAS_255_NoNearLimit
 *                     |   limit.                           |
 *                   6 | Account                            | VAS_234_Account         (reuse)
 *                   7 | Budget                             | VAS_254_Budget          (reuse)
 *                   8 | Actual                             | VAS_252_Actual          (reuse)
 *                   9 | Financial year                     | VAS_256_FinancialYear   (reuse)
 *                  10 | No financial years available       | VAS_256_NoYears         (reuse)
 *                  11 | No primary calendar is configured  | VAS_256_NoCalendar      (reuse)
 *                  12 | No primary accounting schema is    | VAS_256_NoAcctSchema    (reuse)
 *                     |   configured                       |
 *                  13 | Showing                            | VAS_020_Showing         (reuse)
 *                  14 | of                                 | VAS_020_Of              (reuse)
 *                  15 | Previous                           | VAS_020_Prev            (reuse)
 *                  16 | Next                               | VAS_020_Next            (reuse)
 *                  17 | Couldn't load                      | VAS_192_CouldntLoad     (reuse)
 *
 * Chronological development:
 *   VAI145         Created  Date 2026-09-09
 ***********************************************************/
; VAS = window.VAS || {};

; (function (VAS, $) {

    /* CSS lives in VAS/Areas/VAS/Content/VAS_255_BudgetNearLimitWidge.css. All classes are
       namespaced `vas-255-` so they never collide with sibling widgets. */

    /* design.md §Widget Header / §Measurement Setup: keep --dash-inline-size on :root equal
       to the dashboard container's current pixel width so the header clamps resolve against
       the dashboard's visible content area, not the viewport. One document-level observer
       serves every widget. */
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

    /* Inline SVG, not an icon-font class - the host shell does not always load an icon font
       and a missing glyph leaves an empty box. Explicit width/height as well as a viewBox: an
       SVG with only a viewBox falls back to 300x150px if a stylesheet is stale, which would
       sprawl across the header. */
    var ICONS = {
        /* A target: the card is about how close each account is to a limit it has not yet
           crossed. */
        target: '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" ' +
            'stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
            '<circle cx="12" cy="12" r="9"></circle><circle cx="12" cy="12" r="5"></circle>' +
            '<circle cx="12" cy="12" r="1.4"></circle></svg>',
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

    /* The missing-value placeholder. A GLYPH rather than a word: it needs no AD_Message key
       and reads the same in every language. */
    var NIL = '-';

    /* Where the Used ratio stops being comfortable. Below this it is stated plainly; from
       here it takes the risk tone. The card's own ceiling - 100% - is the SERVER's: an
       account past it is an overrun and never reaches this file. */
    var RISK_PCT = 90;

    /* Paging. Five rows fit at 1280px in a 3x2 cell; the adaptive fit may ask for more in a
       taller cell, and the server clamps whatever is asked for. */
    var DEFAULT_PAGE_SIZE = 5;
    var MIN_PAGE_SIZE = 1;
    var MAX_PAGE_SIZE = 12;

    /* One grid line - this is only the pre-measurement fallback, replaced by the tallest
       rendered row on the first paint. */
    var ROW_HEIGHT_FALLBACK = 34;

    VAS.VAS_255_BudgetNearLimitWidge = function () {
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

        /* Unique event namespace per instance - a widget can sit twice on one dashboard, and
           the picker binds document-level handlers. */
        var _ns = '';

        var _rows = [];
        var _years = [];
        var _schema = null;
        var _yearId = 0;
        var _fiscalYear = '';
        var _threshold = 0;
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
            _ns = '.vas255_' + widgetID;

            buildSkeleton();
            createBusyIndicator();
            setupRootObserver();
        };

        /* The framework's own widget loader, overlaid on the whole card while a read is in
           flight - the same treatment every sibling VAS widget gives its loads. It covers
           EVERY read: the initial load, the Refresh button, a page turn and a year change.
           Created visible so it is already up from the moment the widget mounts, and the
           shell stays put underneath it so nothing shifts when the rows land. */
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

        /* Publishes THIS widget's own pixel width as --widget-inline-size on its root, which
           is the first variable the card's font-size clamp reads (the dashboard width is only
           the fallback). Every sibling widget does exactly this - without it the card measures
           itself against the whole dashboard and renders a size larger than its neighbours. */
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
           re-reads the whole year, postings may have landed since the last load, and the page
           the user was on is not reliably the same page afterwards. The chosen YEAR is kept -
           that is a filter the user set, not a position in a list. */
        this.refreshWidget = function () {
            closePicker();
            fetchPage(1);
        };

        /* ------------------------------------------------------------ */
        /* DOM skeleton                                                 */
        /* ------------------------------------------------------------ */
        function buildSkeleton() {
            $root = $('<div class="vas-255-root" id="vas-255-root-' + widgetID + '"></div>');

            var title = label('VAS_255_BudgetsNearLimit', 'Budgets near limit');

            $card = $(
                '<div class="vas-255-card">' +
                    '<div class="vas-255-header">' +
                        '<span class="vas-255-icon">' + ICONS.target + '</span>' +
                        '<div class="vas-255-head-text">' +
                            '<div class="vas-255-title"></div>' +
                            '<div class="vas-255-subtitle"></div>' +
                        '</div>' +
                        '<button type="button" class="vas-255-year" aria-haspopup="listbox">' +
                            '<span class="vas-255-year-label"></span>' +
                            ICONS.chevron +
                        '</button>' +
                    '</div>' +
                    '<div class="vas-255-body">' +
                        '<div class="vas-255-ghead vas-255-row" role="row"></div>' +
                        '<div class="vas-255-list" role="rowgroup"></div>' +
                        '<div class="vas-255-pagerwrap"></div>' +
                    '</div>' +
                    '<div class="vas-255-state vas-255-hidden"></div>' +
                '</div>'
            );

            $card.find('.vas-255-title').text(title).attr('title', title);
            $card.find('.vas-255-list').attr('aria-label', title);

            $yearBtn = $card.find('.vas-255-year');
            $list = $card.find('.vas-255-list');
            $foot = $card.find('.vas-255-pagerwrap');
            $state = $card.find('.vas-255-state');

            paintHead();
            paintYearLabel();
            paintSubtitle();

            $yearBtn.on('click' + _ns, function (e) {
                e.preventDefault();
                e.stopPropagation();
                togglePicker();
            });

            $root.append($card);
        }

        /* §Grid Data Rows header: transparent background, Medium, muted, with a divider under
           it. The type scale goes on the CELLS, not on the row - the row sizes its tracks in
           em, and an em resolves against the element's OWN font-size, so a font-size here
           would compute the header's columns smaller than the body's and every label would
           sit over the wrong column. */
        function paintHead() {
            $card.find('.vas-255-ghead').html(
                '<span role="columnheader">' + escapeHtml(label('VAS_234_Account', 'Account')) + '</span>' +
                /* The three figure columns are right-aligned - they are what a reader scans
                   vertically, and a column of numbers is read against its own edge. */
                '<span class="vas-255-num" role="columnheader">' +
                    escapeHtml(label('VAS_254_Budget', 'Budget')) + '</span>' +
                '<span class="vas-255-num" role="columnheader">' +
                    escapeHtml(label('VAS_252_Actual', 'Actual')) + '</span>' +
                '<span class="vas-255-num" role="columnheader">' +
                    escapeHtml(label('VAS_255_Used', 'Used')) + '</span>'
            );
        }

        /* ------------------------------------------------------------ */
        /* Data                                                         */
        /* ------------------------------------------------------------ */
        function fetchPage(pageNo) {
            if (_loading) { return; }
            _loading = true;
            showBusyIndicator();

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_255_BudgetNearLimitWidge/GetRows',
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
                    _threshold = Number(data.ThresholdPct) || 0;
                    paintYearLabel();

                    _rows = data.Rows || [];
                    _page = Number(data.Page) || 1;
                    _pageSize = Number(data.PageSize) || _pageSize;
                    _totalRows = Number(data.TotalRows) || 0;
                    _totalPages = Number(data.TotalPages) || 0;

                    paintSubtitle();
                    paintRows();
                    observeList();
                },
                error: function () {
                    _loading = false;
                    if (_disposed) { return; }
                    /* The overlay comes down on failure too - a spinner left running over an
                       error the user cannot see is the worst of both. And no stale row is left
                       standing as though it were current: renderState takes the body over. */
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

        /* A load failure or a missing configuration takes the card over. Having nothing near
           the limit does NOT - that is handled inside the list, so the header, the subtitle
           and the column labels stay put. */
        function renderState(text) {
            $card.find('.vas-255-body').addClass('vas-255-hidden');
            $state.removeClass('vas-255-hidden').text(text);
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

        /* "23 accounts between 80% and 100% of approved budget" - the count is the WHOLE
           result, never the page, and the threshold is the server's. The sentence states
           BOTH bounds of the band: the lower one is configuration, the upper one is what
           makes this the near-limit card rather than the overrun card (VAS_252), and a
           subtitle that named only the threshold read as if overruns were counted too.
           Before the first read lands there is neither, so the subtitle stays empty rather
           than printing a zero the card does not yet know. */
        function paintSubtitle() {
            var $sub = $card.find('.vas-255-subtitle');

            if (_threshold <= 0) { $sub.text('').attr('title', ''); return; }

            var text = _totalRows + ' ' +
                label('VAS_255_AccountsBetween', 'accounts between') + ' ' +
                percentText(_threshold, 0) + ' ' +
                label('VAS_255_AndFullBudget', 'and 100% of approved budget');

            /* The subtitle truncates in a 3-column cell that is already sharing its header row
               with the year pill, so the full sentence also goes on the title attribute - it
               moves to the tooltip rather than being lost. */
            $sub.text(text).attr('title', text);
        }

        function paintYearLabel() {
            var text = _fiscalYear || yearNameOf(_yearId);
            $yearBtn.find('.vas-255-year-label').text(text);
            $yearBtn.attr('title', label('VAS_256_FinancialYear', 'Financial year') + ': ' + text);
        }

        function yearNameOf(id) {
            for (var i = 0; i < _years.length; i++) {
                if (Number(_years[i].C_Year_ID) === id) { return _years[i].FiscalYear || ''; }
            }
            return '';
        }

        function paintRows() {
            $state.addClass('vas-255-hidden');
            $card.find('.vas-255-body').removeClass('vas-255-hidden');

            if (!_rows || _rows.length === 0) {
                /* Nothing near its limit is GOOD news, not an error - and with no rows there
                   is nothing to page, so the footer goes too. No sample or demo rows are ever
                   drawn in place of real ones. */
                $list.html('<div class="vas-255-empty">' +
                    escapeHtml(label('VAS_255_NoNearLimit',
                        'No budgets are near the configured limit.')) + '</div>');
                $foot.empty();
                return;
            }

            var html = '';
            for (var i = 0; i < _rows.length; i++) { html += rowHtml(_rows[i]); }
            $list.html(html);

            paintFooter();

            /* Adapt the page size to the list height ONLY on the first paint / after a resize
               - never on manual navigation, which would flip pageSize under the user and
               re-clamp the page they just moved to. */
            if (_needsSync) { scheduleSync(); }
        }

        /* Four cells - Account | Budget | Actual | Used. Not a button: this card has no
           drill-down, so nothing here should look activatable. */
        function rowHtml(item) {
            var account = accountText(item);
            var used = Number(item.UsedPct) || 0;

            /* The tone is the finding; the printed percentage says the same thing in text, so
               the reading never rests on colour alone. */
            var usedCls = used >= RISK_PCT ? 'vas-255-used vas-255-risk' : 'vas-255-used';

            return '<div class="vas-255-brow vas-255-row" role="row" title="' +
                        escapeHtml(rowTooltip(item, account)) + '">' +
                bodyCell('vas-255-account', account) +
                bodyCell('vas-255-fig vas-255-num', compactAmount(Number(item.Budget) || 0)) +
                bodyCell('vas-255-fig vas-255-num', compactAmount(Number(item.Actual) || 0)) +
                bodyCell(usedCls + ' vas-255-num', percentText(used, 0)) +
            '</div>';
        }

        /* One body cell. An empty value renders the missing-value dash rather than nothing at
           all, so a gap never reads as a rendering fault. */
        function bodyCell(cls, text) {
            if (isBlank(text)) {
                return '<span class="' + cls + ' vas-255-nil" role="cell">' + NIL + '</span>';
            }

            return '<span class="' + cls + '" role="cell" title="' + escapeHtml(text) + '">' +
                escapeHtml(text) + '</span>';
        }

        /* "{Account Value} — {Account Name}" - "11800 — Cash in Registers". The CODE leads
           because that is how the account is looked up, spoken about and found in the chart;
           the name alone leaves two similarly named accounts indistinguishable, which is
           exactly what the code exists to settle. Either half may be missing on a badly
           seeded chart of accounts, so the em dash is only printed when there are two sides
           to it, and a row with neither falls through to the missing-value dash. */
        function accountText(item) {
            var value = item.AccountValue || '';
            var name = item.AccountName || '';

            if (value && name) { return value + ' — ' + name; }
            return value || name;
        }

        /* Everything the row holds, at full precision - the cells above are compact. */
        function rowTooltip(item, account) {
            var lines = [];

            /* The account line is the CELL's own text - code and name together. It is not
               recomposed here: prefixing the code again would print it twice now that the
               cell carries it. */
            lines.push(isBlank(account) ? NIL : account);

            lines.push(label('VAS_254_Budget', 'Budget') + ': ' + fullAmount(Number(item.Budget) || 0));
            lines.push(label('VAS_252_Actual', 'Actual') + ': ' + fullAmount(Number(item.Actual) || 0));
            lines.push(label('VAS_255_Used', 'Used') + ': ' + percentText(Number(item.UsedPct) || 0, 1));

            return lines.join('\n');
        }

        /* ---- Canonical Widget Footer Pager (design.md): "Showing a–b of N" left, compact
             prev / next control right. Hidden on a single page, which is what makes it show
             ONLY when the data overflows the body. ---- */
        function paintFooter() {
            if (_totalPages <= 1) { $foot.empty(); return; }

            var from = (_page - 1) * _pageSize + 1;
            var to = Math.min(_page * _pageSize, _totalRows);

            var showing = label('VAS_020_Showing', 'Showing') + ' ' + from + '–' + to + ' ' +
                label('VAS_020_Of', 'of') + ' ' + _totalRows;

            var prevDis = _page <= 1 ? ' disabled' : '';
            var nextDis = _page >= _totalPages ? ' disabled' : '';

            $foot.html(
                '<div class="vas-255-pager">' +
                    '<span class="vas-255-pager-info" title="' + escapeHtml(showing) + '">' +
                        escapeHtml(showing) + '</span>' +
                    '<div class="vas-255-pager-nav">' +
                        '<button type="button" class="vas-255-pgbtn vas-255-pg-prev" aria-label="' +
                            escapeHtml(label('VAS_020_Prev', 'Previous')) + '"' + prevDis + '>' + ICONS.prev + '</button>' +
                        '<span class="vas-255-pager-label">' + _page + ' ' +
                            escapeHtml(label('VAS_020_Of', 'of')) + ' ' + _totalPages + '</span>' +
                        '<button type="button" class="vas-255-pgbtn vas-255-pg-next" aria-label="' +
                            escapeHtml(label('VAS_020_Next', 'Next')) + '"' + nextDis + '>' + ICONS.next + '</button>' +
                    '</div>' +
                '</div>'
            );

            /* A page change keeps the year filter - only the page number moves. */
            $foot.find('.vas-255-pg-prev').on('click', function () {
                if (!_loading && _page > 1) { fetchPage(_page - 1); }
            });
            $foot.find('.vas-255-pg-next').on('click', function () {
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

            /* Size off the TALLEST rendered row - a long account name can wrap on a narrow
               cell, and a page sized off the shortest row would overflow the body. */
            var rendered = $list[0].querySelectorAll('.vas-255-brow');
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
            $picker = $('<div class="vas-255-pp vas-255-hidden" role="listbox" aria-label="' +
                escapeHtml(label('VAS_256_FinancialYear', 'Financial year')) + '"></div>');
            $('body').append($picker);

            $picker.on('click', '.vas-255-pp-opt', function () {
                var id = parseInt($(this).attr('data-id'), 10) || 0;
                closePicker();
                selectYear(id);
            });
        }

        function fillPicker() {
            var html = '<div class="vas-255-pp-h">' +
                escapeHtml(label('VAS_256_FinancialYear', 'Financial year')) + '</div>';

            if (_years.length === 0) {
                html += '<div class="vas-255-pp-empty">' +
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

            return '<button type="button" class="vas-255-pp-opt" role="option" data-id="' + id +
                    '" aria-selected="' + (selected ? 'true' : 'false') + '">' +
                '<span class="vas-255-pp-name" title="' + escapeHtml(text) + '">' +
                    escapeHtml(text) + '</span>' +
                '<span class="vas-255-pp-tick">' + ICONS.tick + '</span>' +
            '</button>';
        }

        /* The panel is fixed and lives on <body>, so it only stays glued to the pill if
           something re-anchors it. The dashboard scrolls in its own container, not the window,
           and scroll events do not bubble - a CAPTURE listener on document is the only one
           that sees every scroll. Scrolling is not a dismissal: the panel travels with the
           pill and closes only on a pick, an outside click or Escape. */
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
            $picker.removeClass('vas-255-hidden');
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
            if ($picker) { $picker.addClass('vas-255-hidden'); }

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

        /* Changing the year re-reads from page 1 - the row the user was looking at is not on
           the same page of a different year, and the ranking has to be recomputed. */
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
           "$180K", "₹74K" - never "USD 180K", and only falling back to the ISO code when the
           currency has no symbol configured (which is what the server's own
           COALESCE(CurSymbol, ISO_Code) already resolved). The ISO code still drives the
           SCALE, so no lakh/crore or million step is ever assumed here. */
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

            /* The helper returns a magnitude by contract, so a negative keeps its sign here.
               Both figures on this card are absolutes by construction, but a card that would
               print "-" as "" the day that changes is a card that lies quietly. */
            return (v < 0 ? '−' : '') + symbol() + magnitude;
        }

        /* Full, non-compact amount for the tooltips: the exact figure behind the compact cell.
           Grouping and the decimal separator come from the browser locale, the decimals from
           the schema currency's precision. */
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

        /* The ratio. The cell rounds to whole percent - the row is a warning, not a
           reconciliation - and the tooltip keeps a decimal. The separator comes from the
           reader's own locale, never a hard-coded dot, which reads as a thousands mark in
           half of Europe. */
        function percentText(value, decimals) {
            var v = Number(value) || 0;
            var d = Number(decimals) || 0;

            try {
                return v.toLocaleString(window.navigator.language,
                    { minimumFractionDigits: d, maximumFractionDigits: d }) + '%';
            }
            catch (e) { return v.toFixed(d) + '%'; }
        }

        /* ------------------------------------------------------------ */
        /* Helpers                                                      */
        /* ------------------------------------------------------------ */

        /* True when a value is nothing the reader can be shown. A zero is NOT blank: it is a
           figure. */
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

        /* Release everything that outlives the card: the body-mounted picker, the document and
           window listeners it registers, and both observers - a ResizeObserver left running
           keeps the whole subtree alive. */
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
    VAS.VAS_255_BudgetNearLimitWidge.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.widgetInfo = frame.widgetInfo;
        this.windowNo = windowNo;
        this.initalize();
        this.frame.getContentGrid().append(this.getRoot());

        /* Self-wire the dashboard-width CSS variable the header clamps read. */
        ensureDashInlineSizeVar(this.getRoot());

        this.intialLoad();
    };

    /* No prototype refreshWidget: the constructor already defines the instance method, which
       shadows anything on the prototype. A prototype version calling this.refreshWidget()
       would be unreachable at best and infinite recursion the day the instance one is
       removed. */

    VAS.VAS_255_BudgetNearLimitWidge.prototype.widgetSizeChange = function (widget) {
        this.widgetInfo = widget;
    };

    VAS.VAS_255_BudgetNearLimitWidge.prototype.dispose = function () {
        try { this.releasePanel(); } catch (e) { /* ignore */ }
        if (this.frame && typeof this.frame.dispose === 'function') {
            try { this.frame.dispose(); } catch (e) { /* ignore */ }
        }
        this.frame = null;
        this.windowNo = null;
    };

})(VAS, jQuery);
