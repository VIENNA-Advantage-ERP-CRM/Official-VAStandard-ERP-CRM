/************************************************************
 * Module Name    : VAS
 * Purpose        : Budget Summary - a 9x2 full-width band for the Budgeting
 *                  dashboard: four totals beside a three-row grid.
 *
 *                  Approved budget against posted actual for one financial year,
 *                  summarised by account type:
 *
 *                    [▤] Budget summary                        [ FY 2026 v ]
 *                        By account type · posted through Aug 2026
 *
 *                    Total budget      $12.40M │ Budget type   Budget  Actual …
 *                    Actual amount      $7.97M │ Expense budget $9.20M $6.48M …
 *                    Remaining balance  $4.43M │ Revenue budget $2.40M $1.18M …
 *                    Utilization         64.3% │ Asset budget    $800K  $310K …
 *                    ▬▬▬▬▬▬▬▬▬▬░░░░░░░ │
 *
 *                  THE STATS AND THE GRID ARE THE SAME NUMBERS. Every total is the
 *                  SUM of the rows printed beside it, computed on the server from the
 *                  one scan both come from - the band cannot drift from the grid it
 *                  is standing next to, whichever way the card is rendered.
 *
 *                  BUDGETED LEDGER ACCOUNTS ONLY. An account counts towards a figure on
 *                  this card - budget, actual, balance or utilization - only when that
 *                  account has a budget defined for the year. This is a BUDGET report:
 *                  an actual posted to an account nobody budgeted has no approved line
 *                  to be measured against, and it is not lost - it is the unbudgeted
 *                  card's subject (VAS_256). The scope is the server's and it is the
 *                  same one the drill-down uses, so a row's Actual cell and the panel's
 *                  Actual metric are the same number. The only figure NOT scoped this
 *                  way is the subtitle's "posted through" date, which says how far the
 *                  books have been posted and is a property of the ledger, not the grid.
 *
 *                  THREE ROWS, ALWAYS. Expense, Revenue and Asset are drawn whether or
 *                  not any account was budgeted under them; a type with nothing against
 *                  it is a ZERO row, never a missing one - the reader is told the figure
 *                  is nil, not that there is no figure.
 *
 *                  THE FIGURES ARRIVE SIGNED THE WAY THE BUSINESS READS THEM. The
 *                  server sums an expense or asset account Dr - Cr and a revenue
 *                  account Cr - Dr, so this file never re-signs, negates or ABSes an
 *                  amount it was handed. A negative balance keeps its minus sign,
 *                  because it means the budget has been overspent.
 *
 *                  UTILIZATION OF NOTHING IS NOT A NUMBER. A row with no approved
 *                  budget prints a dash rather than a division nobody can defend, and
 *                  the server - not this file - decides which rows those are, so
 *                  Infinity and NaN never reach the card.
 *
 *                  EVERY ROW OPENS, ON THE ACCOUNTS THAT WERE BUDGETED. A budget type is
 *                  an aggregate, and the next useful question is which ledger accounts
 *                  were approved under it - so all three rows are buttons and each opens
 *                  a paged panel listing THAT TYPE'S BUDGETED ACCOUNTS with the same
 *                  three figures the row itself carries. An account with an approved
 *                  budget and nothing spent yet is listed (that is what the Balance
 *                  column is for); an account posted to with no budget behind it is not,
 *                  because it has no approved line to report against - those are the
 *                  unbudgeted card's subject, not this one's.
 *
 *                  THE PANEL ADDS UP TO ITSELF, AND TO THE ROW THAT OPENED IT. Its four
 *                  metrics are read from the SERVER, summed over exactly the accounts it
 *                  lists, so every figure at the top is accounted for by a row
 *                  underneath - and since the card covers those same budgeted accounts,
 *                  those metrics equal the row's own cells. The same population, read at
 *                  two levels of detail.
 *
 *                  ONE CURRENCY, THE SCHEMA'S OWN, AND ITS SYMBOL. Every figure is an
 *                  accounting amount in the primary accounting schema's currency,
 *                  printed with that currency's SYMBOL against the number - "$12.40M",
 *                  never "USD 12.40M" - falling back to the ISO code only when the
 *                  currency has no symbol configured. Nothing is converted and no scale
 *                  is assumed: the ISO code decides whether the compact form steps in
 *                  lakh/crore or thousand/million. No symbol is ever printed against a
 *                  percentage.
 *
 *                  Design: design.md -> dashboard-widgets.md (Glass Widget, Widget
 *                  Header, Larger Multi-Stat Widgets, Grid Data Rows, Widget Footer
 *                  Pager, No Inner Scrollbars) supplies the shell, the header
 *                  typography, the stat scale, the row grid and the pager; the widget
 *                  specification supplies the four totals and the five columns.
 *
 *                  Summary Message Table
 *                  Rows marked (reuse) already exist under another key and are NOT
 *                  duplicated here.
 *                   # | Current Text                       | Message Key
 *                  ---+------------------------------------+--------------------------
 *                   1 | Budget summary                     | VAS_250_BudgetSummary
 *                   2 | By account type                    | VAS_250_ByAccountType
 *                   3 | no actual postings                 | VAS_250_NoActualPostings
 *                   4 | Total budget                       | VAS_250_TotalBudget
 *                   5 | Actual amount                      | VAS_250_ActualAmount
 *                   6 | Remaining balance                  | VAS_250_RemainingBalance
 *                   7 | Utilization                        | VAS_250_Utilization
 *                   8 | Budget type                        | VAS_250_BudgetType
 *                   9 | Expense budget                     | VAS_250_ExpenseBudget
 *                  10 | Revenue budget                     | VAS_250_RevenueBudget
 *                  11 | Asset budget                       | VAS_250_AssetBudget
 *                  12 | Balance                            | VAS_250_Balance
 *                  13 | budgeted ledger accounts           | VAS_250_LedgerAccounts
 *                  14 | Ledger                             | VAS_250_Ledger
 *                  15 | Budget and actual for              | VAS_250_BudgetActualFor
 *                  16 | No ledger account has a budget     | VAS_250_NoLedger
 *                     |   defined for this budget type.    |
 *                  17 | posted through                     | VAS_251_PostedThrough   (reuse)
 *                  18 | Budget                             | VAS_254_Budget          (reuse)
 *                  19 | Actual                             | VAS_252_Actual          (reuse)
 *                  20 | Utilized                           | VAS_252_Utilized        (reuse)
 *                  21 | Close                              | VAS_235_Close           (reuse)
 *                  22 | Financial year                     | VAS_256_FinancialYear   (reuse)
 *                  23 | No financial years available       | VAS_256_NoYears         (reuse)
 *                  24 | No primary calendar is configured  | VAS_256_NoCalendar      (reuse)
 *                  25 | No primary accounting schema is    | VAS_256_NoAcctSchema    (reuse)
 *                     |   configured                       |
 *                  26 | Showing                            | VAS_020_Showing         (reuse)
 *                  27 | of                                 | VAS_020_Of              (reuse)
 *                  28 | Previous                           | VAS_020_Prev            (reuse)
 *                  29 | Next                               | VAS_020_Next            (reuse)
 *                  30 | Couldn't load                      | VAS_192_CouldntLoad     (reuse)
 *
 * Chronological development:
 *   VAI145         Created  Date 2026-09-10
 ***********************************************************/
; VAS = window.VAS || {};

; (function (VAS, $) {

    /* CSS lives in VAS/Areas/VAS/Content/VAS_250_BudgetSummaryWidget.css. All classes are
       namespaced `vas-250-` so they never collide with sibling widgets. */

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
        /* A wallet: the card is what has been set aside and what is left of it. */
        wallet: '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" ' +
            'stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
            '<path d="M3 7a2 2 0 0 1 2-2h12a2 2 0 0 1 2 2v2"></path>' +
            '<path d="M3 7v10a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-6H16a2 2 0 0 0 0 4h5"></path></svg>',
        chevron: '<svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" ' +
            'stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
            '<polyline points="6 9 12 15 18 9"></polyline></svg>',
        tick: '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" ' +
            'stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
            '<polyline points="20 6 9 17 4 12"></polyline></svg>',
        close: '<svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" ' +
            'stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
            '<path d="M18 6 6 18"></path><path d="M6 6l12 12"></path></svg>',
        prev: '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" ' +
            'stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
            '<polyline points="15 18 9 12 15 6"></polyline></svg>',
        next: '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" ' +
            'stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
            '<polyline points="9 18 15 12 9 6"></polyline></svg>'
    };

    /* The missing-value placeholder. A GLYPH rather than a word: it needs no AD_Message key
       and reads the same in every language. */
    var NIL = '—';

    /* Where a utilization stops being comfortable and where it stops being a budget at all.
       Both are printed as well as toned, so the reading never rests on colour alone. */
    var WARN_PCT = 85;
    var RISK_PCT = 100;

    /* Paging. The card carries three rows and a full-width band fits all three at every
       supported dashboard width, so the pager is expected never to appear - it is here
       because §No Inner Scrollbars has no other answer if a narrow cell ever fits fewer. */
    var MIN_PAGE_SIZE = 1;
    var ROW_HEIGHT_FALLBACK = 34;

    /* The panel's rows region is paged, never scrolled - the panel is a fixed height, so the
       number of rows that fit is measured from it rather than guessed at. These are only the
       floor and the pre-measurement fallback. */
    var DLG_MIN_PAGE_SIZE = 3;
    var DLG_ROW_HEIGHT_FALLBACK = 34;

    VAS.VAS_250_BudgetSummaryWidget = function () {
        this.frame;
        this.windowNo;
        this.widgetInfo;

        var $self = this;
        var $root;
        var $card;
        var $yearBtn;
        var $stats;
        var $list;
        var $foot;
        var $state;
        var $busy;
        var $picker;
        var $dlg;

        var widgetID = 0;

        /* Unique event namespace per instance - a widget can sit twice on one dashboard, and
           the picker binds document-level handlers. */
        var _ns = '';

        var _rows = [];
        var _years = [];
        var _schema = null;
        var _yearId = 0;
        var _fiscalYear = '';

        var _totalBudget = 0;
        var _totalActual = 0;
        var _balance = 0;
        var _utilized = 0;
        var _hasUtilization = false;
        var _postedThrough = '';
        var _hasActual = false;

        /* The three rows arrive whole - the read is one round trip and turning a page costs
           nothing - so the page is a slice of what is already here, not a second request. */
        var _page = 1;
        var _pageSize = 3;
        var _totalPages = 1;

        var _rowH = 0;
        var _needsSync = false;
        var _loading = false;
        var _pickerOpen = false;
        var _disposed = false;
        var _rootObserver = null;
        var _listObserver = null;

        /* Drill-down state. The trigger is kept only so the row that opened the panel can be
           blurred - focus is NOT returned to it when the panel closes. */
        var $dlgRows;
        var $dlgFoot;

        var _dlgType = '';
        var _dlgLoading = false;
        var _dlgTrigger = null;

        /* The ledger list, held in full and PAGED in the panel - the read is one round trip and
           turning a page costs nothing. */
        var _dlgRows = [];
        var _dlgTotalRows = 0;
        var _dlgPage = 1;
        var _dlgPageSize = DLG_MIN_PAGE_SIZE;
        var _dlgTotalPages = 0;
        var _dlgRowH = 0;
        var _dlgNeedsSync = false;
        var _dlgObserver = null;

        /* ------------------------------------------------------------ */
        /* Lifecycle                                                    */
        /* ------------------------------------------------------------ */
        this.initalize = function () {
            widgetID = (VIS.Utility && VIS.Utility.Util
                ? VIS.Utility.Util.getValueOfInt($self.widgetInfo.AD_UserHomeWidgetID)
                : 0);
            if (widgetID === 0) { widgetID = $self.windowNo; }
            _ns = '.vas250_' + widgetID;

            buildSkeleton();
            buildDialog();
            createBusyIndicator();
            setupRootObserver();
        };

        /* The framework's own widget loader, overlaid on the whole card while a read is in
           flight - the same treatment every sibling VAS widget gives its loads. Created visible
           so it is already up from the moment the widget mounts, and the shell stays put
           underneath it so nothing shifts when the figures land. */
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

        /* Publishes THIS widget's own pixel width as --widget-inline-size on its root, which is
           the first variable the card's font-size clamp reads (the dashboard width is only the
           fallback). Every sibling widget does exactly this - without it the card measures
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
            fetchSummary();
        };

        /* The dashboard's Refresh button calls this. The chosen YEAR is kept - that is a filter
           the user set, not a position in a list - and any open panel is closed first: its
           accounts belong to a read that is about to be replaced. */
        this.refreshWidget = function () {
            closePicker();
            closeDialog();
            fetchSummary();
        };

        /* ------------------------------------------------------------ */
        /* DOM skeleton                                                 */
        /* ------------------------------------------------------------ */
        function buildSkeleton() {
            $root = $('<div class="vas-250-root" id="vas-250-root-' + widgetID + '"></div>');

            var title = label('VAS_250_BudgetSummary', 'Budget summary');

            $card = $(
                '<div class="vas-250-card">' +
                    '<div class="vas-250-header">' +
                        '<span class="vas-250-icon">' + ICONS.wallet + '</span>' +
                        '<div class="vas-250-head-text">' +
                            '<div class="vas-250-title"></div>' +
                            '<div class="vas-250-subtitle"></div>' +
                        '</div>' +
                        '<button type="button" class="vas-250-year" aria-haspopup="listbox">' +
                            '<span class="vas-250-year-label"></span>' +
                            ICONS.chevron +
                        '</button>' +
                    '</div>' +
                    '<div class="vas-250-body">' +
                        '<div class="vas-250-wrap">' +
                            '<div class="vas-250-stats"></div>' +
                            '<div class="vas-250-table">' +
                                '<div class="vas-250-ghead vas-250-row" role="row"></div>' +
                                '<div class="vas-250-list" role="rowgroup"></div>' +
                            '</div>' +
                        '</div>' +
                        '<div class="vas-250-pagerwrap"></div>' +
                    '</div>' +
                    '<div class="vas-250-state vas-250-hidden"></div>' +
                '</div>'
            );

            $card.find('.vas-250-title').text(title).attr('title', title);

            $yearBtn = $card.find('.vas-250-year');
            $stats = $card.find('.vas-250-stats');
            $list = $card.find('.vas-250-list');
            $foot = $card.find('.vas-250-pagerwrap');
            $state = $card.find('.vas-250-state');

            $list.attr('aria-label', title);

            paintHead();
            paintYearLabel();
            paintSubtitle();

            $yearBtn.on('click' + _ns, function (e) {
                e.preventDefault();
                e.stopPropagation();
                togglePicker();
            });

            /* Delegated: the rows are rebuilt on every read and every page turn, so the handler
               is bound once here rather than per row. */
            $list.on('click' + _ns, '.vas-250-brow', function () {
                openDialog($(this).attr('data-type') || '', this);
            });

            $root.append($card);
        }

        /* §Grid Data Rows header: transparent background, Medium, muted, with a divider under
           it. The type scale goes on the CELLS, not on the row - the row sizes its tracks in
           em, and an em resolves against the element's OWN font-size, so a font-size here would
           compute the header's columns smaller than the body's and every label would sit over
           the wrong column. */
        function paintHead() {
            $card.find('.vas-250-ghead').html(
                '<span role="columnheader">' +
                    escapeHtml(label('VAS_250_BudgetType', 'Budget type')) + '</span>' +
                /* The four figure columns are right-aligned - they are what a reader scans
                   vertically, and a column of numbers is read against its own edge. */
                '<span class="vas-250-num" role="columnheader">' +
                    escapeHtml(label('VAS_254_Budget', 'Budget')) + '</span>' +
                '<span class="vas-250-num" role="columnheader">' +
                    escapeHtml(label('VAS_252_Actual', 'Actual')) + '</span>' +
                '<span class="vas-250-num" role="columnheader">' +
                    escapeHtml(label('VAS_250_Balance', 'Balance')) + '</span>' +
                '<span class="vas-250-num" role="columnheader">' +
                    escapeHtml(label('VAS_252_Utilized', 'Utilized')) + '</span>'
            );
        }

        /* ------------------------------------------------------------ */
        /* Data                                                         */
        /* ------------------------------------------------------------ */
        function fetchSummary() {
            if (_loading) { return; }
            _loading = true;
            showBusyIndicator();

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_250_BudgetSummaryWidget/GetSummary',
                type: 'GET',
                dataType: 'json',
                cache: false,
                /* Asynchronous, always - nothing here justifies blocking the UI thread. */
                async: true,
                data: { yearId: _yearId },
                success: function (raw) {
                    _loading = false;
                    if (_disposed) { return; }
                    hideBusyIndicator();

                    var data = parseResponse(raw);
                    if (!data || data.error) {
                        renderState(label('VAS_192_CouldntLoad', "Couldn't load"));
                        return;
                    }

                    /* A configuration gap is a different answer from a failed read, and it says
                       which piece of setup is missing rather than "couldn't load". */
                    if (data.ErrorCode) {
                        renderState(errorLabel(data.ErrorCode));
                        return;
                    }

                    if (!data.Loaded) {
                        renderState(label('VAS_192_CouldntLoad', "Couldn't load"));
                        return;
                    }

                    _schema = data.Schema || null;

                    /* The year list comes back with every read, so a newly opened financial year
                       appears on the next refresh without a second endpoint. */
                    _years = data.Years || [];
                    _yearId = Number(data.C_Year_ID) || 0;
                    _fiscalYear = data.FiscalYear || '';
                    paintYearLabel();

                    _rows = data.Rows || [];
                    _totalBudget = Number(data.TotalBudget) || 0;
                    _totalActual = Number(data.TotalActual) || 0;
                    _balance = Number(data.Balance) || 0;
                    _utilized = Number(data.UtilizedPct) || 0;
                    _hasUtilization = !!data.HasUtilization;
                    _postedThrough = data.PostedThrough || '';
                    _hasActual = !!data.HasActual;

                    _page = 1;
                    _needsSync = true;

                    paintSubtitle();
                    paintStats();
                    paintRows();
                    observeList();
                },
                error: function () {
                    _loading = false;
                    if (_disposed) { return; }
                    /* The overlay comes down on failure too - a spinner left running over an
                       error the user cannot see is the worst of both. And no stale figure is left
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

        /* A load failure or a missing configuration takes the card over. A year with nothing
           posted does NOT: zeros are a real answer and are printed as figures, so the header,
           the stats and the column labels all stay put. */
        function renderState(text) {
            $card.find('.vas-250-body').addClass('vas-250-hidden');
            $state.removeClass('vas-250-hidden').text(text);
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

        /* "By account type · posted through Aug 2026", or "By account type · no actual
           postings" when the year has none - never "posted through" with a blank after it. */
        function paintSubtitle() {
            var $sub = $card.find('.vas-250-subtitle');
            var text = label('VAS_250_ByAccountType', 'By account type');

            var through = _hasActual ? monthYearText(_postedThrough) : '';

            if (through) {
                text += ' · ' + label('VAS_251_PostedThrough', 'posted through') + ' ' + through;
            }
            else {
                text += ' · ' + label('VAS_250_NoActualPostings', 'no actual postings');
            }

            /* The subtitle shares its header row with the year pill, so on a narrow dashboard it
               WILL truncate; the full sentence also goes on the title attribute - it moves to
               the tooltip rather than being lost. */
            $sub.text(text).attr('title', text);
        }

        function paintYearLabel() {
            var text = _fiscalYear || yearNameOf(_yearId);
            $yearBtn.find('.vas-250-year-label').text(text);
            $yearBtn.attr('title', label('VAS_256_FinancialYear', 'Financial year') + ': ' + text);
        }

        function yearNameOf(id) {
            for (var i = 0; i < _years.length; i++) {
                if (Number(_years[i].C_Year_ID) === id) { return _years[i].FiscalYear || ''; }
            }
            return '';
        }

        /* The four totals. Every one of them is compact in the tile with the exact figure on its
           tooltip - a stat is read at a glance and reconciled on hover.

           A year with nothing budgeted and nothing posted prints zeros, not a "no data" state:
           the reader is told the figure is nil, which is an answer, rather than being told the
           card could not produce one. */
        function paintStats() {
            var utilText = _hasUtilization ? percentText(_utilized, 1) : NIL;

            $stats.html(
                statHtml(label('VAS_250_TotalBudget', 'Total budget'),
                    compactAmount(_totalBudget), fullAmount(_totalBudget)) +
                statHtml(label('VAS_250_ActualAmount', 'Actual amount'),
                    compactAmount(_totalActual), fullAmount(_totalActual)) +
                statHtml(label('VAS_250_RemainingBalance', 'Remaining balance'),
                    compactAmount(_balance), fullAmount(_balance)) +
                statHtml(label('VAS_250_Utilization', 'Utilization'), utilText, utilText,
                    utilBarHtml())
            );
        }

        function statHtml(labelText, valueText, titleText, extraHtml) {
            return '<div class="vas-250-stat">' +
                '<div class="vas-250-stat-l" title="' + escapeHtml(labelText) + '">' +
                    escapeHtml(labelText) + '</div>' +
                '<div class="vas-250-stat-v" title="' + escapeHtml(titleText) + '">' +
                    escapeHtml(valueText) + '</div>' +
                (extraHtml || '') +
            '</div>';
        }

        /* The utilization bar. It is a SECOND reading of the number printed above it, never the
           only one - the track is capped at 100% because a bar cannot overflow its own rail,
           and it is the tone and the printed percentage that report an overrun. A year with no
           budget has no ratio, so it gets an empty track rather than a full or an absent one. */
        function utilBarHtml() {
            var pct = _hasUtilization ? _utilized : 0;
            if (pct < 0) { pct = 0; }
            if (pct > 100) { pct = 100; }

            return '<div class="vas-250-stat-bar">' +
                '<i class="' + toneClass(_hasUtilization ? _utilized : 0) +
                    '" style="width:' + pct.toFixed(1) + '%"></i>' +
            '</div>';
        }

        /* Comfortable, tight, or past the line. The thresholds are the design's, and every one
           of them is also stated in text beside the bar. */
        function toneClass(pct) {
            var v = Number(pct) || 0;
            if (v >= RISK_PCT) { return 'vas-250-fill vas-250-fill-risk'; }
            if (v >= WARN_PCT) { return 'vas-250-fill vas-250-fill-warn'; }
            return 'vas-250-fill vas-250-fill-ok';
        }

        function paintRows() {
            $state.addClass('vas-250-hidden');
            $card.find('.vas-250-body').removeClass('vas-250-hidden');

            var pages = Math.max(1, Math.ceil(_rows.length / _pageSize));
            if (_page > pages) { _page = pages; }
            if (_page < 1) { _page = 1; }
            _totalPages = pages;

            var start = (_page - 1) * _pageSize;
            var end = Math.min(start + _pageSize, _rows.length);

            var html = '';
            for (var i = start; i < end; i++) { html += rowHtml(_rows[i]); }
            $list.html(html);

            paintFooter(start + 1, end);

            /* Adapt the page size to the list height ONLY on the first paint after a read or a
               resize - never on manual navigation, which would flip the page size under the
               reader mid-move. */
            if (_needsSync) { scheduleSync(); }
        }

        /* Five cells - Budget type | Budget | Actual | Balance | Utilized - and the whole row is
           a BUTTON: every type opens its ledger accounts, so every row is activatable and says
           so. The stored code travels on the row as data, never the label. */
        function rowHtml(item) {
            var name = typeName(item.AccountType);
            var budget = Number(item.Budget) || 0;
            var actual = Number(item.Actual) || 0;
            var balance = Number(item.Balance) || 0;

            var hasUtil = !!item.HasUtilization;
            var used = Number(item.UtilizedPct) || 0;
            var usedText = hasUtil ? percentText(used, 1) : NIL;

            /* The tone is the finding; the printed percentage says the same thing, so the
               reading never rests on colour alone. A type with actuals and no budget at all is
               over budget too - the server settles that, not a division here. */
            var usedCls = item.IsOverBudget ? 'vas-250-used vas-250-risk' : 'vas-250-used';

            return '<button type="button" class="vas-250-brow vas-250-row" role="row"' +
                        ' data-type="' + escapeHtml(item.AccountType || '') + '"' +
                        ' aria-label="' + escapeHtml(rowAriaLabel(name)) + '"' +
                        ' title="' + escapeHtml(rowTooltip(item, name)) + '">' +
                bodyCell('vas-250-type', name) +
                bodyCell('vas-250-fig vas-250-num', compactAmount(budget), fullAmount(budget)) +
                bodyCell('vas-250-fig vas-250-num', compactAmount(actual), fullAmount(actual)) +
                bodyCell('vas-250-fig vas-250-num', compactAmount(balance), fullAmount(balance)) +
                bodyCell(usedCls + ' vas-250-num', usedText,
                    hasUtil ? percentText(used, 2) : usedText) +
            '</button>';
        }

        /* What a screen reader announces on a row: the budget type, and that activating it opens
           its ledger accounts. */
        function rowAriaLabel(name) {
            return name + ' — ' + label('VAS_250_LedgerAccounts', 'budgeted ledger accounts');
        }

        /* One body cell. titleText defaults to the displayed text - it differs only where the
           cell is a rounded form of something exact. An empty value renders the missing-value
           dash rather than nothing at all, so a gap never reads as a rendering fault. */
        function bodyCell(cls, text, titleText) {
            if (isBlank(text)) {
                return '<span class="' + cls + ' vas-250-nil" role="cell">' + NIL + '</span>';
            }

            var tip = (titleText === undefined || titleText === null) ? text : titleText;

            return '<span class="' + cls + '" role="cell" title="' + escapeHtml(tip) + '">' +
                escapeHtml(text) + '</span>';
        }

        /* The row's name comes from AD_Message keyed on the STORED CODE, never from text on the
           wire. "Expense budget" is this card's own vocabulary rather than the account type's
           own label, which is why it is a message key here and not an AD_Ref_List lookup - the
           row names a kind of budget, not a kind of account. */
        function typeName(code) {
            if (code === 'E') { return label('VAS_250_ExpenseBudget', 'Expense budget'); }
            if (code === 'R') { return label('VAS_250_RevenueBudget', 'Revenue budget'); }
            if (code === 'A') { return label('VAS_250_AssetBudget', 'Asset budget'); }
            return '';
        }

        /* Everything the row holds, at full precision - the cells above are compact. */
        function rowTooltip(item, name) {
            var lines = [isBlank(name) ? NIL : name];

            lines.push(label('VAS_254_Budget', 'Budget') + ': ' + fullAmount(Number(item.Budget) || 0));
            lines.push(label('VAS_252_Actual', 'Actual') + ': ' + fullAmount(Number(item.Actual) || 0));
            lines.push(label('VAS_250_Balance', 'Balance') + ': ' + fullAmount(Number(item.Balance) || 0));
            lines.push(label('VAS_252_Utilized', 'Utilized') + ': ' +
                (item.HasUtilization ? percentText(Number(item.UtilizedPct) || 0, 1) : NIL));

            return lines.join('\n');
        }

        /* ---- Canonical Widget Footer Pager (design.md): "Showing a–b of N" left, compact prev
             / next control right. Hidden on a single page, which is what makes it show ONLY
             when the rows overflow the body. ---- */
        function paintFooter(from, to) {
            if (_totalPages <= 1) { $foot.empty(); return; }

            var showing = label('VAS_020_Showing', 'Showing') + ' ' + from + '–' + to + ' ' +
                label('VAS_020_Of', 'of') + ' ' + _rows.length;

            var prevDis = _page <= 1 ? ' disabled' : '';
            var nextDis = _page >= _totalPages ? ' disabled' : '';

            $foot.html(
                '<div class="vas-250-pager">' +
                    '<span class="vas-250-pager-info" title="' + escapeHtml(showing) + '">' +
                        escapeHtml(showing) + '</span>' +
                    '<div class="vas-250-pager-nav">' +
                        '<button type="button" class="vas-250-pgbtn vas-250-pg-prev" aria-label="' +
                            escapeHtml(label('VAS_020_Prev', 'Previous')) + '"' + prevDis + '>' + ICONS.prev + '</button>' +
                        '<span class="vas-250-pager-label">' + _page + ' ' +
                            escapeHtml(label('VAS_020_Of', 'of')) + ' ' + _totalPages + '</span>' +
                        '<button type="button" class="vas-250-pgbtn vas-250-pg-next" aria-label="' +
                            escapeHtml(label('VAS_020_Next', 'Next')) + '"' + nextDis + '>' + ICONS.next + '</button>' +
                    '</div>' +
                '</div>'
            );

            /* A page change moves the page number and nothing else - the rows are already here
               and the year filter is untouched. */
            $foot.find('.vas-250-pg-prev').on('click', function () {
                if (_page > 1) { _page--; paintRows(); }
            });
            $foot.find('.vas-250-pg-next').on('click', function () {
                if (_page < _totalPages) { _page++; paintRows(); }
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
            if (_disposed || !$list || !$list[0]) { return; }
            if (!_rows || _rows.length === 0) { return; }

            var avail = $list[0].clientHeight;
            if (avail <= 0) {
                /* Layout has not settled yet - try again on the next frame. */
                if (_needsSync) { scheduleSync(); }
                return;
            }

            /* Size off the TALLEST rendered row - a long type name can wrap on a narrow cell,
               and a page sized off the shortest row would overflow the body. */
            var rendered = $list[0].querySelectorAll('.vas-250-brow');
            var maxH = 0;
            for (var i = 0; i < rendered.length; i++) {
                if (rendered[i].offsetHeight > maxH) { maxH = rendered[i].offsetHeight; }
            }
            if (maxH > 0) { _rowH = maxH; }
            var rowH = _rowH > 0 ? _rowH : ROW_HEIGHT_FALLBACK;

            _needsSync = false;

            var capacity = Math.floor(avail / rowH);
            if (capacity < MIN_PAGE_SIZE) { capacity = MIN_PAGE_SIZE; }
            if (capacity > _rows.length) { capacity = _rows.length; }

            if (capacity !== _pageSize) {
                _pageSize = capacity;
                paintRows();
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
            $picker = $('<div class="vas-250-pp vas-250-hidden" role="listbox" aria-label="' +
                escapeHtml(label('VAS_256_FinancialYear', 'Financial year')) + '"></div>');
            $('body').append($picker);

            $picker.on('click', '.vas-250-pp-opt', function () {
                var id = parseInt($(this).attr('data-id'), 10) || 0;
                closePicker();
                selectYear(id);
            });
        }

        function fillPicker() {
            var html = '<div class="vas-250-pp-h">' +
                escapeHtml(label('VAS_256_FinancialYear', 'Financial year')) + '</div>';

            if (_years.length === 0) {
                html += '<div class="vas-250-pp-empty">' +
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

            return '<button type="button" class="vas-250-pp-opt" role="option" data-id="' + id +
                    '" aria-selected="' + (selected ? 'true' : 'false') + '">' +
                '<span class="vas-250-pp-name" title="' + escapeHtml(text) + '">' +
                    escapeHtml(text) + '</span>' +
                '<span class="vas-250-pp-tick">' + ICONS.tick + '</span>' +
            '</button>';
        }

        /* The panel is fixed and lives on <body>, so it only stays glued to the pill if
           something re-anchors it. The dashboard scrolls in its own container, not the window,
           and scroll events do not bubble - a CAPTURE listener on document is the only one that
           sees every scroll. Scrolling is not a dismissal: the panel travels with the pill and
           closes only on a pick, an outside click or Escape. */
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
            /* Right-aligned to the pill: it sits at the card's trailing edge, so a left-aligned
               panel would hang off the dashboard. */
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
            $picker.removeClass('vas-250-hidden');
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
            if ($picker) { $picker.addClass('vas-250-hidden'); }

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

        /* Changing the year re-reads everything and drops any open drill-down: the totals, the
           rows, the posted-through date and the panel's accounts all belong to the year being
           left behind. */
        function selectYear(id) {
            if (id <= 0 || id === _yearId) { return; }

            _yearId = id;
            _fiscalYear = yearNameOf(id);
            paintYearLabel();

            closeDialog();
            fetchSummary();
        }

        /* ------------------------------------------------------------ */
        /* Drill-down panel - the ledger accounts behind one row        */
        /* ------------------------------------------------------------ */
        function buildDialog() {
            $dlg = $(
                '<div class="vas-250-dlg-wrap vas-250-hidden">' +
                    '<div class="vas-250-dlg-backdrop"></div>' +
                    '<div class="vas-250-dlg" role="dialog" aria-modal="true" ' +
                            'aria-labelledby="vas-250-dlg-title-' + widgetID + '">' +
                        '<div class="vas-250-dlg-head">' +
                            /* THE CARD'S OWN ICON WELL, IN THE PANEL HEAD. The panel is the row
                               opened up, not a window of its own, and the well is what says so at
                               a glance - the same #EAF8FF tile and the same glyph the card is
                               headed with, sized off the panel's px chrome rather than the card's
                               em lever. */
                            '<span class="vas-250-icon vas-250-dlg-icon" aria-hidden="true">' +
                                ICONS.wallet +
                            '</span>' +
                            '<div class="vas-250-dlg-titles">' +
                                '<div class="vas-250-dlg-title" id="vas-250-dlg-title-' + widgetID + '"></div>' +
                                '<div class="vas-250-dlg-meta"></div>' +
                            '</div>' +
                            '<button type="button" class="vas-250-x" aria-label="' +
                                escapeHtml(label('VAS_235_Close', 'Close')) + '">' + ICONS.close + '</button>' +
                        '</div>' +
                        /* THE PANEL IS A FIXED HEIGHT AND NOTHING INSIDE IT SCROLLS. The metrics
                           and the column header are chrome and stay put; only the ROWS region
                           changes between pages, and it is paged rather than scrolled - which is
                           what stops the column header from scrolling away from the figures it
                           names. The total and the pager are bands of their own BELOW the rows. */
                        '<div class="vas-250-dlg-bodywrap">' +
                            '<div class="vas-250-dlg-body">' +
                                '<div class="vas-250-metrics"></div>' +
                                '<div class="vas-250-dg-h vas-250-dg-row vas-250-hidden" role="row"></div>' +
                                '<div class="vas-250-rows" role="rowgroup"></div>' +
                            '</div>' +
                            '<div class="vas-250-dlg-busy vis-busyindicatorouterwrap vas-250-hidden">' +
                                '<div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div>' +
                            '</div>' +
                        '</div>' +
                        /* No total band. The type's own Actual is already the second metric at
                           the top of the panel, and repeating it under the list would state the
                           same figure twice while costing a row of the fixed height the accounts
                           are paged into. */
                        '<div class="vas-250-dlg-foot"></div>' +
                    '</div>' +
                '</div>'
            );

            $('body').append($dlg);

            $dlgRows = $dlg.find('.vas-250-rows');
            $dlgFoot = $dlg.find('.vas-250-dlg-foot');

            /* Wrapped rather than passed straight through: jQuery hands the handler the event
               object, which would arrive as closeDialog's own argument. */
            $dlg.find('.vas-250-x').on('click', function () { closeDialog(); });

            /* THE BACKDROP DELIBERATELY DOES NOT DISMISS - no handler is bound to it at all. The
               panel is a paged list of accounts the reader works down, and a stray click on the
               surround throwing that away is a worse failure than the extra click it would have
               saved. Escape and the close button remain. */
        }

        function openDialog(accountType, trigger) {
            if (isBlank(accountType)) { return; }

            _dlgType = accountType;
            _dlgTrigger = trigger || null;
            _dlgRows = [];
            _dlgTotalRows = 0;
            _dlgPage = 1;
            _dlgTotalPages = 0;

            /* The title is known from the row before the accounts arrive, so the panel opens
               named rather than blank. */
            paintDialogHead(typeName(accountType));

            $dlg.find('.vas-250-metrics').empty();
            $dlg.find('.vas-250-dg-h').empty().addClass('vas-250-hidden');
            $dlgRows.empty();
            $dlgFoot.empty();

            $dlg.removeClass('vas-250-hidden');
            $(document).on('keydown' + _ns + '_dlg', onDialogKeyDown);

            /* NOTHING BEHIND THE SCRIM STAYS LIT. The class suppresses the row's hover and focus
               treatment for as long as the panel is up - the pointer is still sitting on the row
               that was just clicked, and a lit control outside the dialog reads as though it were
               still the one in play. */
            $root.addClass('vas-250-modal-open');

            blurWidgetFocus();

            fetchDetail(accountType);
            focusDialog();
        }

        /* "Expense budget · ledger accounts", over "Budget and actual for FY 2026" - the year is
           a value between two message keys, never inside one. */
        function paintDialogHead(name) {
            var title = name + ' · ' + label('VAS_250_LedgerAccounts', 'budgeted ledger accounts');

            $dlg.find('.vas-250-dlg-title').text(title).attr('title', title);
            $dlg.find('.vas-250-dlg-meta').text(
                label('VAS_250_BudgetActualFor', 'Budget and actual for') + ' ' + (_fiscalYear || ''));
        }

        /* THE WIDGET BEHIND GIVES UP FOCUS. The row that opened the panel is a button and keeps
           its focus treatment otherwise - a lit control behind a scrim, outside the dialog the
           reader is now in. Whatever inside the card holds focus is blurred, not only the
           trigger: the year pill can be the active element too. */
        function blurWidgetFocus() {
            var active = document.activeElement;

            if (active && $root && $root[0] && $root[0].contains(active) &&
                    typeof active.blur === 'function') {
                try { active.blur(); } catch (e) { /* ignore */ }
            }

            if (_dlgTrigger && typeof _dlgTrigger.blur === 'function') {
                try { _dlgTrigger.blur(); } catch (e) { /* ignore */ }
            }
        }

        /* Focus lands on the close button, which is what aria-modal promises a screen reader. It
           is set TWICE - once now and once on the next frame - because the click that opened the
           panel is still bubbling through the dashboard shell, and a host handler that focuses
           the widget frame on its way past would otherwise pull focus straight back out of the
           dialog. The second pass is a no-op when nothing did. */
        function focusDialog() {
            var focusClose = function () {
                if (_disposed || !$dlg || $dlg.hasClass('vas-250-hidden')) { return; }

                var x = $dlg.find('.vas-250-x')[0];
                if (!x || typeof x.focus !== 'function') { return; }
                if (document.activeElement === x) { return; }

                try { x.focus({ preventScroll: true }); }
                catch (e) { try { x.focus(); } catch (e2) { /* ignore */ } }
            };

            focusClose();

            var raf = window.requestAnimationFrame || function (cb) { return window.setTimeout(cb, 16); };
            raf(focusClose);
        }

        /* THE ROW IS NOT RE-LIT ON THE WAY OUT. Focus is not handed back to the row that opened
           the panel: the host shell's focus treatment is a filled, ringed box the full width of
           the grid, which on a closed panel reads as a type still selected rather than as a
           cursor position. The panel takes focus with it and leaves the card as it found it. */
        function closeDialog() {
            if (!$dlg) { return; }

            $dlg.addClass('vas-250-hidden');
            $(document).off('keydown' + _ns + '_dlg');

            /* The rows are activatable again the moment the scrim is gone. */
            if ($root) { $root.removeClass('vas-250-modal-open'); }

            _dlgType = '';
            _dlgLoading = false;

            if (_dlgObserver) {
                try { _dlgObserver.disconnect(); } catch (e) { /* ignore */ }
                _dlgObserver = null;
            }

            /* Whatever the panel left focused goes with it - the close button and the pager are
               inside a subtree that is now hidden, and a focused element inside a hidden subtree
               is a ring nobody can see driving a Tab order nobody can follow. */
            blurDialogFocus();

            _dlgTrigger = null;
        }

        /* Drops focus if it is still inside the panel or on the row behind it, leaving it at the
           document rather than on a control the reader has finished with. */
        function blurDialogFocus() {
            var active = document.activeElement;
            if (!active || typeof active.blur !== 'function') { return; }

            var inDialog = $dlg && $dlg[0] && $dlg[0].contains(active);
            var inCard = $root && $root[0] && $root[0].contains(active);

            if (inDialog || inCard) {
                try { active.blur(); } catch (e) { /* ignore */ }
            }
        }

        function onDialogKeyDown(e) {
            if (e.key === 'Escape' || e.keyCode === 27) { closeDialog(); }
        }

        function showDialogBusy(on) {
            $dlg.find('.vas-250-dlg-busy').toggleClass('vas-250-hidden', !on);
        }

        function fetchDetail(accountType) {
            if (_dlgLoading) { return; }
            _dlgLoading = true;
            showDialogBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_250_BudgetSummaryWidget/GetTypeDetail',
                type: 'GET',
                dataType: 'json',
                cache: false,
                async: true,
                data: { accountType: accountType, yearId: _yearId },
                success: function (raw) {
                    _dlgLoading = false;
                    if (_disposed) { return; }
                    showDialogBusy(false);

                    /* The reader may have closed the panel, or opened another type, while this
                       was in flight - in either case these rows are not what is on screen and
                       must not be painted into it. */
                    if (_dlgType !== accountType) { return; }

                    var data = parseResponse(raw);
                    if (!data || data.error || !data.Loaded) {
                        renderDetailMessage(label('VAS_192_CouldntLoad', "Couldn't load"));
                        return;
                    }

                    paintDetail(data);
                },
                error: function () {
                    _dlgLoading = false;
                    if (_disposed) { return; }
                    showDialogBusy(false);
                    renderDetailMessage(label('VAS_192_CouldntLoad', "Couldn't load"));
                }
            });
        }

        /* A failure or a type with no accounts is said in the ROWS region, not over the whole
           panel: the metrics above it are the type's own figures and are still true. */
        function renderDetailMessage(text) {
            $dlg.find('.vas-250-dg-h').empty().addClass('vas-250-hidden');
            $dlgFoot.empty();
            $dlgRows.html('<div class="vas-250-dlg-empty">' + escapeHtml(text) + '</div>');
        }

        /* The panel: four metrics and the ledger accounts they are summed from. Every metric is
           the SERVER's, over exactly the accounts listed below it - none of it is passed in from
           the row - so the panel accounts for itself. */
        function paintDetail(data) {
            var budget = Number(data.Budget) || 0;
            var actual = Number(data.Actual) || 0;
            var balance = Number(data.Balance) || 0;

            _dlgRows = data.Rows || [];
            _dlgTotalRows = Number(data.TotalRows) || _dlgRows.length;
            _dlgPage = 1;
            _dlgNeedsSync = true;

            /* The title is re-stated from the server's own account type, in case the row that
               opened the panel named it differently. */
            paintDialogHead(typeName(data.AccountType || _dlgType));

            var utilized = data.HasUtilization ? percentText(Number(data.UtilizedPct) || 0, 1) : NIL;

            /* The metrics are COMPACT - four figures across one narrow band, where the exact
               digits would wrap - with the full figure on each one's tooltip. */
            $dlg.find('.vas-250-metrics').html(
                metricHtml(label('VAS_254_Budget', 'Budget'), compactAmount(budget), fullAmount(budget)) +
                metricHtml(label('VAS_252_Actual', 'Actual'), compactAmount(actual), fullAmount(actual)) +
                metricHtml(label('VAS_250_Balance', 'Balance'), compactAmount(balance), fullAmount(balance)) +
                metricHtml(label('VAS_252_Utilized', 'Utilized'), utilized, utilized)
            );

            /* A type with actuals but not one budgeted account is a real state, and the panel
               says which one it is - "nothing was budgeted here", not "nothing loaded". The
               metrics above it read zero, which is the same answer stated as figures: there is
               no budgeted account, so there is nothing for them to sum. */
            if (_dlgRows.length === 0) {
                renderDetailMessage(label('VAS_250_NoLedger',
                    'No ledger account has a budget defined for this budget type.'));
                return;
            }

            $dlg.find('.vas-250-dg-h').removeClass('vas-250-hidden').html(
                '<span role="columnheader">' + escapeHtml(label('VAS_250_Ledger', 'Ledger')) + '</span>' +
                '<span class="vas-250-num" role="columnheader">' +
                    escapeHtml(label('VAS_254_Budget', 'Budget')) + '</span>' +
                '<span class="vas-250-num" role="columnheader">' +
                    escapeHtml(label('VAS_252_Actual', 'Actual')) + '</span>' +
                '<span class="vas-250-num" role="columnheader">' +
                    escapeHtml(label('VAS_250_Balance', 'Balance')) + '</span>'
            );

            paintDetailPage();
            observeDialogRows();
        }

        /* One page of accounts, and the footer that says where in the list it sits. */
        function paintDetailPage() {
            var pages = Math.max(1, Math.ceil(_dlgRows.length / _dlgPageSize));
            if (_dlgPage > pages) { _dlgPage = pages; }
            if (_dlgPage < 1) { _dlgPage = 1; }
            _dlgTotalPages = pages;

            var start = (_dlgPage - 1) * _dlgPageSize;
            var end = Math.min(start + _dlgPageSize, _dlgRows.length);

            var html = '';
            for (var i = start; i < end; i++) { html += detailRowHtml(_dlgRows[i]); }
            $dlgRows.html(html);

            paintDetailFooter(start + 1, end);

            /* Fit the page to the panel ONLY after a fresh read or a resize - never on a page
               turn, which would flip the page size under the reader mid-navigation. */
            if (_dlgNeedsSync) { scheduleDialogSync(); }
        }

        /* "Showing 1–8 of 34" left, compact prev / next right - the widget footer pager, in the
           panel. It is rendered even on a single page: it states where the list ends rather than
           leaving the reader to infer it from a missing control. */
        function paintDetailFooter(from, to) {
            var showing = label('VAS_020_Showing', 'Showing') + ' ' + from + '–' + to + ' ' +
                label('VAS_020_Of', 'of') + ' ' + _dlgRows.length;

            /* The server caps what it sends; when it did, the footer says so on hover rather than
               letting the reader assume they are seeing every account. */
            var tip = showing;
            if (_dlgTotalRows > _dlgRows.length) {
                tip = showing + ' (' + label('VAS_020_Of', 'of') + ' ' + _dlgTotalRows + ')';
            }

            var prevDis = _dlgPage <= 1 ? ' disabled' : '';
            var nextDis = _dlgPage >= _dlgTotalPages ? ' disabled' : '';

            $dlgFoot.html(
                '<span class="vas-250-foot-info" title="' + escapeHtml(tip) + '">' +
                    escapeHtml(showing) + '</span>' +
                '<div class="vas-250-dlg-pager">' +
                    '<button type="button" class="vas-250-pgbtn vas-250-pg-prev" aria-label="' +
                        escapeHtml(label('VAS_020_Prev', 'Previous')) + '"' + prevDis + '>' + ICONS.prev + '</button>' +
                    '<span class="vas-250-pager-label">' + _dlgPage + ' ' +
                        escapeHtml(label('VAS_020_Of', 'of')) + ' ' + _dlgTotalPages + '</span>' +
                    '<button type="button" class="vas-250-pgbtn vas-250-pg-next" aria-label="' +
                        escapeHtml(label('VAS_020_Next', 'Next')) + '"' + nextDis + '>' + ICONS.next + '</button>' +
                '</div>'
            );

            $dlgFoot.find('.vas-250-pg-prev').on('click', function () {
                if (_dlgPage > 1) { _dlgPage--; paintDetailPage(); }
            });
            $dlgFoot.find('.vas-250-pg-next').on('click', function () {
                if (_dlgPage < _dlgTotalPages) { _dlgPage++; paintDetailPage(); }
            });
        }

        /* One BUDGETED ledger account - "79200 — Suspense balancing" and its three figures. Not
           a button: the panel is the end of this card's drill-down. */
        function detailRowHtml(item) {
            var budget = Number(item.Budget) || 0;
            var actual = Number(item.Actual) || 0;
            var balance = Number(item.Balance) || 0;

            /* A balance that has gone negative means the account is overspent - the sign already
               says so, and the tone repeats it for the eye scanning the column. */
            var balCls = item.IsOverBudget ? 'vas-250-dg-n vas-250-risk' : 'vas-250-dg-n';

            return '<div class="vas-250-dg-r vas-250-dg-row" role="row">' +
                detailCell('vas-250-dg-p', accountText(item)) +
                detailCell('vas-250-dg-n vas-250-num', compactAmount(budget), fullAmount(budget)) +
                detailCell('vas-250-dg-n vas-250-num', compactAmount(actual), fullAmount(actual)) +
                detailCell(balCls + ' vas-250-num', compactAmount(balance), fullAmount(balance)) +
            '</div>';
        }

        /* One cell. titleText defaults to the displayed text - it differs only where the cell is a
           rounded form of something exact. An empty value renders the missing-value dash rather
           than nothing at all, so a gap never reads as a rendering fault. */
        function detailCell(cls, text, titleText) {
            if (isBlank(text)) {
                return '<span class="' + cls + ' vas-250-nil" role="cell">' + NIL + '</span>';
            }

            var tip = (titleText === undefined || titleText === null) ? text : titleText;

            return '<span class="' + cls + '" role="cell" title="' + escapeHtml(tip) + '">' +
                escapeHtml(text) + '</span>';
        }

        /* "{Account Value} — {Account Name}" - "79200 — Suspense balancing". The CODE leads
           because that is how the account is looked up, spoken about and found in the chart of
           accounts. Either half may be missing on a badly seeded chart, so the em dash is only
           printed when there are two sides to it. */
        function accountText(item) {
            var value = item.AccountValue || '';
            var name = item.AccountName || '';

            if (value && name) { return value + ' — ' + name; }
            return value || name;
        }

        /* ---- Fitting the page to the panel ----
           The panel is a fixed height, so the number of rows that fit is a property of the panel
           and not a constant to guess at: it is measured from the rows region and the tallest row
           actually rendered. */
        function scheduleDialogSync() {
            var raf = window.requestAnimationFrame || function (cb) { return window.setTimeout(cb, 16); };
            raf(function () { syncDialogCapacity(); });
        }

        function syncDialogCapacity() {
            if (_disposed || !$dlgRows || !$dlgRows[0]) { return; }
            if (!_dlgRows || _dlgRows.length === 0) { return; }

            var avail = $dlgRows[0].clientHeight;
            if (avail <= 0) {
                /* Layout has not settled yet - try again on the next frame. */
                if (_dlgNeedsSync) { scheduleDialogSync(); }
                return;
            }

            var rendered = $dlgRows[0].querySelectorAll('.vas-250-dg-r');
            var maxH = 0;
            for (var i = 0; i < rendered.length; i++) {
                if (rendered[i].offsetHeight > maxH) { maxH = rendered[i].offsetHeight; }
            }
            if (maxH > 0) { _dlgRowH = maxH; }
            var rowH = _dlgRowH > 0 ? _dlgRowH : DLG_ROW_HEIGHT_FALLBACK;

            _dlgNeedsSync = false;

            var capacity = Math.floor(avail / rowH);
            if (capacity < DLG_MIN_PAGE_SIZE) { capacity = DLG_MIN_PAGE_SIZE; }

            if (capacity !== _dlgPageSize) {
                _dlgPageSize = capacity;
                paintDetailPage();
            }
        }

        function observeDialogRows() {
            if (typeof ResizeObserver === 'undefined' || !$dlgRows || !$dlgRows[0]) { return; }
            if (_dlgObserver) { try { _dlgObserver.disconnect(); } catch (e) { /* ignore */ } }

            _dlgObserver = new ResizeObserver(function () {
                if (_disposed || _dlgLoading) { return; }
                _dlgNeedsSync = true;
                syncDialogCapacity();
            });
            _dlgObserver.observe($dlgRows[0]);
        }

        function metricHtml(labelText, valueText, titleText) {
            return '<div class="vas-250-metric">' +
                '<div class="vas-250-metric-l">' + escapeHtml(labelText) + '</div>' +
                '<div class="vas-250-metric-v" title="' + escapeHtml(titleText) + '">' +
                    escapeHtml(valueText) + '</div>' +
            '</div>';
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
           "$12.40M", "₹4.86Cr" - never "USD 12.40M", and only falling back to the ISO code when
           the currency has no symbol configured (which is what the server's own
           COALESCE(CurSymbol, ISO_Code) already resolved). The ISO code still drives the SCALE,
           so no lakh/crore or million step is ever assumed here.

           The helper returns a magnitude by contract, so the sign is restored here - a balance
           that has gone negative means the budget is overspent, and a card that printed
           "−$117K" as "$117K" would state the opposite of the figure behind it. */
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

        /* Full, non-compact amount for the tooltips: the exact figure behind the compact cell.
           Grouping and the decimal separator come from the browser locale, the decimals from the
           schema currency's precision. */
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

        /* A ratio, never a currency: no symbol is ever printed against a percentage. The
           separator comes from the reader's own locale, never a hard-coded dot, which reads as a
           thousands mark in half of Europe. */
        function percentText(value, decimals) {
            var v = Number(value) || 0;
            var d = Number(decimals) || 0;

            try {
                return v.toLocaleString(window.navigator.language,
                    { minimumFractionDigits: d, maximumFractionDigits: d }) + '%';
            }
            catch (e) { return v.toFixed(d) + '%'; }
        }

        /* "Aug 2026" from the server's yyyy-MM-dd. The parts are read out and handed to the
           Date constructor rather than parsed from the string: a bare "2026-08-31" is read as
           UTC midnight and prints as the previous month for every reader west of Greenwich. The
           month name is the reader's own locale's - the format is never baked into the SQL. */
        function monthYearText(iso) {
            if (isBlank(iso)) { return ''; }

            var parts = String(iso).split('-');
            if (parts.length < 3) { return ''; }

            var year = parseInt(parts[0], 10);
            var month = parseInt(parts[1], 10);
            var day = parseInt(parts[2], 10);
            if (isNaN(year) || isNaN(month) || isNaN(day)) { return ''; }

            var date = new Date(year, month - 1, day);
            if (isNaN(date.getTime())) { return ''; }

            try {
                return date.toLocaleDateString(window.navigator.language,
                    { year: 'numeric', month: 'short' });
            }
            catch (e) { return parts[1] + '-' + parts[0]; }
        }

        /* ------------------------------------------------------------ */
        /* Helpers                                                      */
        /* ------------------------------------------------------------ */

        /* True when a value is nothing the reader can be shown. A zero is NOT blank: it is a
           figure. */
        function isBlank(value) {
            return value === null || value === undefined || String(value) === '';
        }

        /* Every string that reaches the DOM goes through here first. */
        function escapeHtml(s) {
            var v = (s === null || s === undefined) ? '' : String(s);
            return v.replace(/&/g, '&amp;').replace(/"/g, '&quot;')
                .replace(/'/g, '&#39;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
        }

        /* Every user-facing string goes through AD_Message; the fallback keeps the card readable
           when a key has not been seeded yet. */
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

        /* Release everything that outlives the card: the body-mounted picker and panel, the
           document and window listeners they register, and every observer - a ResizeObserver
           left running keeps the whole subtree alive. */
        this.releasePanel = function () {
            _disposed = true;
            closePicker();
            closeDialog();

            if (_rootObserver) {
                try { _rootObserver.disconnect(); } catch (e) { /* ignore */ }
                _rootObserver = null;
            }
            if (_listObserver) {
                try { _listObserver.disconnect(); } catch (e) { /* ignore */ }
                _listObserver = null;
            }

            if ($picker) { $picker.off(); $picker.remove(); $picker = null; }
            if ($dlg) { $dlg.off(); $dlg.remove(); $dlg = null; }
            if ($busy) { $busy.remove(); $busy = null; }
            if ($yearBtn) { $yearBtn.off(_ns); }
            if ($list) { $list.off(_ns); }
            if ($foot) { $foot.off(); }

            _rows = [];
            _years = [];
        };
    };

    /* ---------------------------------------------------------------- */
    /* Required prototype hooks (same surface as other VAS widgets)     */
    /* ---------------------------------------------------------------- */
    VAS.VAS_250_BudgetSummaryWidget.prototype.init = function (windowNo, frame) {
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
       shadows anything on the prototype. A prototype version calling this.refreshWidget() would
       be unreachable at best and infinite recursion the day the instance one is removed. */

    VAS.VAS_250_BudgetSummaryWidget.prototype.widgetSizeChange = function (widget) {
        this.widgetInfo = widget;
    };

    VAS.VAS_250_BudgetSummaryWidget.prototype.dispose = function () {
        try { this.releasePanel(); } catch (e) { /* ignore */ }
        if (this.frame && typeof this.frame.dispose === 'function') {
            try { this.frame.dispose(); } catch (e) { /* ignore */ }
        }
        this.frame = null;
        this.windowNo = null;
    };

})(VAS, jQuery);
