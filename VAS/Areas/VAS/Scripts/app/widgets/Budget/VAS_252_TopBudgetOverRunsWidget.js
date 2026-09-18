/************************************************************
 * Module Name    : VAS
 * Purpose        : Top Budget Overruns - a 5x2 paginated exception list for the
 *                  Budgeting dashboard.
 *
 *                  The account / transaction-organization combinations whose actual has
 *                  already passed its approved budget, worst first:
 *
 *                    [!] Top budget overruns                        [ FY 2026 v ]
 *                        6 accounts with actual above approved
 *
 *                    Account              Organization Unit  Budget  Actual  Variance  Utilized
 *                    5410 — Cloud infra   Head Office        $612K   $729K    −$117K    119.1%
 *                    5500 — Plant maint   Mumbai Plant       $790K   $877K     −$87K    111.0%
 *
 *                    Showing 1–2 of 6 · sorted by variance          <  1 of 3  >
 *
 *                  THE GRAIN IS ACCOUNT + AD_OrgTrx_ID. Budget and actual are aggregated
 *                  at that pair and compared at it, so a budget set for one transaction
 *                  organization is never measured against actuals posted to another. The
 *                  Organization Unit column is therefore not decoration - it names the
 *                  second half of what each row's two figures are being compared at.
 *
 *                  ONLY COMBINATIONS THAT ARE GENUINELY OVER. Actual must exceed budget
 *                  strictly - one at exactly 100% has not passed it - and there must BE an
 *                  approved budget to pass. Spend with no budget behind it is unbudgeted
 *                  rather than over-budget, and has its own card (VAS_256) on this
 *                  dashboard; reporting it here too would double-count it and would make
 *                  Utilized a division by zero. Both tests are applied server-side, so a
 *                  row that is not over budget never reaches this file.
 *
 *                  SORTED BY VALUE, NOT BY PERCENTAGE. A large account 5% over outranks
 *                  a small one 40% over: the card exists to surface money, and the
 *                  Utilized column is context for the row rather than its ranking.
 *
 *                  THE VARIANCE IS NEGATIVE BY CONSTRUCTION, so it always prints with a
 *                  true minus sign in the danger tone. The sign and the colour say the
 *                  same thing twice, which is what keeps the reading alive in print and
 *                  for a colour-blind reader.
 *
 *                  PHASE 1 IS ACTUAL ONLY. Commitments are not included until commitment
 *                  accounting is defined. The server returns the basis as a token and
 *                  this file reports it rather than assuming it, so the day it becomes
 *                  "actual + commitment" the card says so. When that lands, the design
 *                  calls for an "Actual | Actual + commitment" pill PAIR in the header -
 *                  the column set does not change.
 *
 *                  THE ORGANIZATION UNIT IS AD_OrgTrx_ID, AND ONLY THAT. Not the
 *                  posting's owning organization (Fact_Acct.AD_Org_ID), not project,
 *                  activity, campaign, product or a user element. There is no
 *                  dimension-type selector and the column cannot be switched: the grain of
 *                  the whole card is the account and its transaction organization, so
 *                  changing it would change what is being COMPARED rather than merely what
 *                  is displayed. The cell shows the organization's own name; a combination
 *                  with no transaction organization shows the missing-value dash and is
 *                  still ranked and grouped separately from the named ones.
 *
 *                  The COLUMN is captioned "Organization Unit" - the wording this
 *                  dashboard uses for AD_OrgTrx_ID, shared verbatim with VAS_256 - while
 *                  the wire contract keeps the accounting term (DimensionName), because
 *                  the field is a Fact_Acct dimension and tying its name to one screen's
 *                  caption would be the wrong coupling.
 *
 *                  ONE CURRENCY, THE SCHEMA'S OWN. Every figure is an accounting amount in
 *                  the primary accounting schema's currency, printed with that currency's
 *                  symbol against the number - "$612K", never "USD 612K" - falling back to
 *                  the ISO code only when the currency has no symbol. Nothing is converted
 *                  and no scale is assumed: the ISO code decides whether the compact form
 *                  steps in lakh/crore or thousand/million.
 *
 *                  Design: design.md -> dashboard-widgets.md (Glass Widget, Widget Header,
 *                  Grid Data Rows, Widget Footer Pager, Content Fit Budget, No Inner
 *                  Scrollbars) supplies the shell, the row grid and the pager; the widget
 *                  specification supplies the six columns and the two danger tones.
 *
 *                  Summary Message Table
 *                  Rows marked (reuse) already exist under another key and are NOT
 *                  duplicated here.
 *                   # | Current Text                       | Message Key
 *                  ---+------------------------------------+--------------------------
 *                   1 | Top budget overruns                | VAS_252_TopBudgetOverRuns
 *                   2 | accounts with actual above         | VAS_252_OverRunHint
 *                     |   approved                          |
 *                   3 | Actual                             | VAS_252_Actual
 *                   4 | Utilized                           | VAS_252_Utilized
 *                   5 | sorted by variance                 | VAS_252_SortedByVariance
 *                   6 | No budget overruns found           | VAS_252_NoOverRuns
 *                   7 | Actual spending is within the      | VAS_252_NoOverRunsHint
 *                     |   approved budget for the          |
 *                     |   selected financial year.         |
 *                   8 | Account                            | VAS_234_Account         (reuse)
 *                   9 | Budget                             | VAS_254_Budget          (reuse)
 *                  10 | Variance                           | VAS_254_Variance        (reuse)
 *                  11 | Organization Unit                  | VAS_256_OrganizationUnit (reuse)
 *                  12 | Financial year                     | VAS_256_FinancialYear   (reuse)
 *                  13 | No financial years available       | VAS_256_NoYears         (reuse)
 *                  14 | No primary calendar is configured  | VAS_256_NoCalendar      (reuse)
 *                  15 | No primary accounting schema is    | VAS_256_NoAcctSchema    (reuse)
 *                     |   configured                       |
 *                  16 | Showing                            | VAS_020_Showing         (reuse)
 *                  17 | of                                 | VAS_020_Of              (reuse)
 *                  18 | Previous                           | VAS_020_Prev            (reuse)
 *                  19 | Next                               | VAS_020_Next            (reuse)
 *                  20 | Couldn't load                      | VAS_192_CouldntLoad     (reuse)
 *
 *                  Three keys are RETIRED and can be dropped from AD_Message:
 *                  VAS_252_Dimension (the column is captioned "Organization Unit" now,
 *                  reusing VAS_256_OrganizationUnit rather than seeding the same text
 *                  twice), VAS_252_LedgerAccountBudget (the column names the transaction
 *                  organization rather than the budget's grain) and
 *                  VAS_252_NoTransactionOrg (a row with no transaction organization now
 *                  shows the missing-value dash, which is a glyph and needs no key).
 *                  VAS_196_Organization is no longer reused here either.
 *
 * Chronological development:
 *   VAI154         Created  Date 2026-09-08
 ***********************************************************/
; VAS = window.VAS || {};

; (function (VAS, $) {

    /* CSS lives in VAS/Areas/VAS/Content/VAS_252_TopBudgetOverRunsWidget.css. All classes
       are namespaced `vas-252-` so they never collide with sibling widgets. */

    /* design.md §Widget Header / §Measurement Setup: keep --dash-inline-size on :root
       equal to the dashboard container's current pixel width so the header clamps resolve
       against the dashboard's visible content area, not the viewport. One document-level
       observer serves every widget. */
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
        /* A warning triangle: this is an exception list, and every row on it is a
           problem someone has to answer for. */
        alert: '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" ' +
            'stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
            '<path d="m21.7 18-8-14a2 2 0 0 0-3.4 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.7-3z"></path>' +
            '<path d="M12 9v4"></path><path d="M12 17h.01"></path></svg>',
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

    /* Paging. Two rows plus a pager at 1280px, three at 1440px, four at 1920px - all
       measured at runtime rather than written down; these are only the bounds. */
    var DEFAULT_PAGE_SIZE = 2;
    var MIN_PAGE_SIZE = 1;
    var MAX_PAGE_SIZE = 12;
    var ROW_HEIGHT_FALLBACK = 44;

    VAS.VAS_252_TopBudgetOverRunsWidget = function () {
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
        var _overRunCount = 0;

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
            _ns = '.vas252_' + widgetID;

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
            $root = $('<div class="vas-252-root" id="vas-252-root-' + widgetID + '"></div>');

            var title = label('VAS_252_TopBudgetOverRuns', 'Top budget overruns');

            $card = $(
                '<div class="vas-252-card">' +
                    '<div class="vas-252-header">' +
                        '<span class="vas-252-icon">' + ICONS.alert + '</span>' +
                        '<div class="vas-252-head-text">' +
                            '<div class="vas-252-title"></div>' +
                            '<div class="vas-252-subtitle"></div>' +
                        '</div>' +
                        '<button type="button" class="vas-252-year" aria-haspopup="listbox">' +
                            '<span class="vas-252-year-label"></span>' +
                            ICONS.chevron +
                        '</button>' +
                    '</div>' +
                    '<div class="vas-252-body" role="table">' +
                        '<div class="vas-252-ghead vas-252-row" role="row"></div>' +
                        '<div class="vas-252-list" role="rowgroup"></div>' +
                        '<div class="vas-252-pagerwrap"></div>' +
                    '</div>' +
                    '<div class="vas-252-state vas-252-hidden"></div>' +
                '</div>'
            );

            $card.find('.vas-252-title').text(title).attr('title', title);
            $card.find('.vas-252-body').attr('aria-label', title);

            $yearBtn = $card.find('.vas-252-year');
            $list = $card.find('.vas-252-list');
            $foot = $card.find('.vas-252-pagerwrap');
            $state = $card.find('.vas-252-state');

            paintHead();
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
                url: VIS.Application.contextUrl + 'VAS_252_TopBudgetOverRunsWidget/GetRows',
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
                    _overRunCount = Number(data.OverRunCount) || 0;

                    paintSubtitle();
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

        /* A load failure or a missing configuration takes the card over. Having nothing
           over budget does NOT - that is handled inside the list, so the header, the
           subtitle and the column labels stay put. */
        function renderState(text) {
            $card.find('.vas-252-body').addClass('vas-252-hidden');
            $state.removeClass('vas-252-hidden').text(text);
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

        /* "6 accounts with actual above approved" - the FULL count of over-budget accounts,
           across every page. The table below shows the top slice; the subtitle is what says
           how much more there is, so it must not change when the reader turns a page. */
        function paintSubtitle() {
            var text = _overRunCount + ' ' +
                label('VAS_252_OverRunHint', 'accounts with actual above approved');

            $card.find('.vas-252-subtitle').text(text).attr('title', text);
        }

        function paintYearLabel() {
            var text = _fiscalYear || yearNameOf(_yearId);
            $yearBtn.find('.vas-252-year-label').text(text);
            $yearBtn.attr('title', label('VAS_256_FinancialYear', 'Financial year') + ': ' + text);
        }

        function yearNameOf(id) {
            for (var i = 0; i < _years.length; i++) {
                if (Number(_years[i].C_Year_ID) === id) { return _years[i].FiscalYear || ''; }
            }
            return '';
        }

        /* §Grid Data Rows header: transparent background, Medium, muted, with a divider
           under it. The type scale goes on the CELLS, not on the row - the row sizes its
           tracks in em, and an em resolves against the element's OWN font-size, so a
           font-size here would compute the header's columns smaller than the body's and
           every label would sit over the wrong column. */
        function paintHead() {
            $card.find('.vas-252-ghead').html(
                '<span role="columnheader">' + escapeHtml(label('VAS_234_Account', 'Account')) + '</span>' +
                '<span role="columnheader">' + escapeHtml(label('VAS_256_OrganizationUnit', 'Organization Unit')) + '</span>' +
                /* The four figure columns are right-aligned - they are what a reader scans
                   vertically, and a column of numbers is read against its own edge. */
                '<span class="vas-252-num" role="columnheader">' +
                    escapeHtml(label('VAS_254_Budget', 'Budget')) + '</span>' +
                '<span class="vas-252-num" role="columnheader">' +
                    escapeHtml(label('VAS_252_Actual', 'Actual')) + '</span>' +
                '<span class="vas-252-num" role="columnheader">' +
                    escapeHtml(label('VAS_254_Variance', 'Variance')) + '</span>' +
                '<span class="vas-252-num" role="columnheader">' +
                    escapeHtml(label('VAS_252_Utilized', 'Utilized')) + '</span>'
            );
        }

        function paintRows() {
            $state.addClass('vas-252-hidden');
            $card.find('.vas-252-body').removeClass('vas-252-hidden');

            if (!_rows || _rows.length === 0) {
                /* Nothing over budget is GOOD news, not an error - and with no rows there
                   is nothing to page, so the footer goes too.

                   TWO LINES, not one: the heading states the finding and the line under it
                   says what that MEANS, because "no overruns" on its own is as easily read
                   as "nothing loaded". No sample or demo rows are ever drawn in place of
                   real ones. */
                $list.html(
                    '<div class="vas-252-empty">' +
                        '<div class="vas-252-empty-t">' +
                            escapeHtml(label('VAS_252_NoOverRuns', 'No budget overruns found')) +
                        '</div>' +
                        '<div class="vas-252-empty-s">' +
                            escapeHtml(label('VAS_252_NoOverRunsHint',
                                'Actual spending is within the approved budget for the selected financial year.')) +
                        '</div>' +
                    '</div>'
                );
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

        /* Six cells - Account | Organization Unit | Budget | Actual | Variance | Utilized.
           Not a
           button: this card has no drill-down, so nothing here should look activatable. */
        function rowHtml(item) {
            var account = accountText(item);
            var dimension = dimensionText(item);

            return '<div class="vas-252-brow vas-252-row" role="row" title="' +
                        escapeHtml(rowTooltip(item, account, dimension)) + '">' +
                bodyCell('vas-252-account', account) +
                bodyCell('vas-252-dim', dimension) +
                bodyCell('vas-252-fig vas-252-num', compactAmount(Number(item.Budget) || 0)) +
                bodyCell('vas-252-fig vas-252-num', compactAmount(Number(item.Actual) || 0)) +
                /* Negative by construction, so the minus is never optional here. */
                bodyCell('vas-252-var vas-252-num', signedCompactAmount(Number(item.Variance) || 0)) +
                bodyCell('vas-252-util vas-252-num', percentText(Number(item.UtilizedPct) || 0)) +
            '</div>';
        }

        /* One body cell. An empty value renders the missing-value dash rather than nothing
           at all, so a gap never reads as a rendering fault. */
        function bodyCell(cls, text) {
            if (isBlank(text)) {
                return '<span class="' + cls + ' vas-252-nil" role="cell">' + NIL + '</span>';
            }

            return '<span class="' + cls + '" role="cell" title="' + escapeHtml(text) + '">' +
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

        /* THE ORGANIZATION UNIT IS AD_OrgTrx_ID AND NOTHING ELSE - the transaction organization the
           posting was stamped with. It is the card's grain as much as its display: budget
           and actual are matched at Account + AD_OrgTrx_ID, so this cell names the second
           half of what the row's two figures are actually being compared at.

           A row whose AD_OrgTrx_ID is null or zero was never attributed to a transaction
           organization, and says so. Those rows stay separate from the real ones rather
           than being folded into a named organization - the server groups them apart and
           only leaves the name empty. */
        function dimensionText(item) {
            /* EMPTY, not a sentence. A combination with no transaction organization falls
               through to the missing-value dash that bodyCell draws for every other empty
               cell on this card - "No Transaction Organization" spelled out was three
               words of prose sitting in a grid of names and figures, and in a 5-column
               cell it truncated to something less readable than the dash it replaced.

               The row is still ranked and grouped separately from the named organizations;
               only its LABEL changes here. */
            return item.DimensionName || '';
        }

        /* One decimal, and the separator from the reader's own locale - never a hard-coded
           dot, which reads as a thousands mark in half of Europe. */
        function percentText(value) {
            var v = Number(value) || 0;

            try {
                return v.toLocaleString(window.navigator.language,
                    { minimumFractionDigits: 1, maximumFractionDigits: 1 }) + '%';
            }
            catch (e) { return v.toFixed(1) + '%'; }
        }

        /* Everything the row holds, at full precision - the cells above are compact. */
        function rowTooltip(item, account, dimension) {
            var lines = [];

            lines.push(isBlank(account) ? NIL : account);
            lines.push(label('VAS_256_OrganizationUnit', 'Organization Unit') + ': ' +
                (isBlank(dimension) ? NIL : dimension));
            lines.push(label('VAS_254_Budget', 'Budget') + ': ' + fullAmount(Number(item.Budget) || 0));
            lines.push(label('VAS_252_Actual', 'Actual') + ': ' + fullAmount(Number(item.Actual) || 0));
            lines.push(label('VAS_254_Variance', 'Variance') + ': ' +
                signedFullAmount(Number(item.Variance) || 0));
            lines.push(label('VAS_252_Utilized', 'Utilized') + ': ' +
                percentText(Number(item.UtilizedPct) || 0));

            return lines.join('\n');
        }

        /* ---- Canonical Widget Footer Pager (design.md): "Showing a–b of N · sorted by
             variance" left, compact prev / next control right. Hidden on a single page.
             The hint earns its place: the ranking is by VALUE and not by the Utilized
             column sitting right above it, which is the one thing a reader could otherwise
             reasonably assume. ---- */
        function paintFooter() {
            if (_totalPages <= 1) { $foot.empty(); return; }

            var from = (_page - 1) * _pageSize + 1;
            var to = Math.min(_page * _pageSize, _totalRows);

            var showing = label('VAS_020_Showing', 'Showing') + ' ' + from + '–' + to + ' ' +
                label('VAS_020_Of', 'of') + ' ' + _totalRows + ' · ' +
                label('VAS_252_SortedByVariance', 'sorted by variance');

            var prevDis = _page <= 1 ? ' disabled' : '';
            var nextDis = _page >= _totalPages ? ' disabled' : '';

            $foot.html(
                '<div class="vas-252-pager">' +
                    '<span class="vas-252-pager-info" title="' + escapeHtml(showing) + '">' +
                        escapeHtml(showing) + '</span>' +
                    '<div class="vas-252-pager-nav">' +
                        '<button type="button" class="vas-252-pgbtn vas-252-pg-prev" aria-label="' +
                            escapeHtml(label('VAS_020_Prev', 'Previous')) + '"' + prevDis + '>' + ICONS.prev + '</button>' +
                        '<span class="vas-252-pager-label">' + _page + ' ' +
                            escapeHtml(label('VAS_020_Of', 'of')) + ' ' + _totalPages + '</span>' +
                        '<button type="button" class="vas-252-pgbtn vas-252-pg-next" aria-label="' +
                            escapeHtml(label('VAS_020_Next', 'Next')) + '"' + nextDis + '>' + ICONS.next + '</button>' +
                    '</div>' +
                '</div>'
            );

            /* A page change keeps the year filter - only the page number moves. */
            $foot.find('.vas-252-pg-prev').on('click', function () {
                if (!_loading && _page > 1) { fetchPage(_page - 1); }
            });
            $foot.find('.vas-252-pg-next').on('click', function () {
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

            /* Size off the TALLEST rendered row so a long account name never clips. */
            var rendered = $list[0].querySelectorAll('.vas-252-brow');
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
            $picker = $('<div class="vas-252-pp vas-252-hidden" role="listbox" aria-label="' +
                escapeHtml(label('VAS_256_FinancialYear', 'Financial year')) + '"></div>');
            $('body').append($picker);

            $picker.on('click', '.vas-252-pp-opt', function () {
                var id = parseInt($(this).attr('data-id'), 10) || 0;
                closePicker();
                selectYear(id);
            });
        }

        function fillPicker() {
            var html = '<div class="vas-252-pp-h">' +
                escapeHtml(label('VAS_256_FinancialYear', 'Financial year')) + '</div>';

            if (_years.length === 0) {
                html += '<div class="vas-252-pp-empty">' +
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

            return '<button type="button" class="vas-252-pp-opt" role="option" data-id="' + id +
                    '" aria-selected="' + (selected ? 'true' : 'false') + '">' +
                '<span class="vas-252-pp-name" title="' + escapeHtml(text) + '">' +
                    escapeHtml(text) + '</span>' +
                '<span class="vas-252-pp-tick">' + ICONS.tick + '</span>' +
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
            $picker.removeClass('vas-252-hidden');
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
            if ($picker) { $picker.addClass('vas-252-hidden'); }

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

        function magnitudeOf(value) {
            var v = Number(value) || 0;
            var magnitude;

            try {
                if (VIS.Util && typeof VIS.Util.formatCompactAmount === 'function') {
                    magnitude = VIS.Util.formatCompactAmount(v, isoCode(), precision());
                }
            }
            catch (e) { if (window.console) { console.log(e); } }

            return magnitude === undefined ? String(Math.abs(v)) : magnitude;
        }

        /* The compact magnitude with the currency SYMBOL printed directly against it -
           "$612K", "₹74K" - never "USD 612K", and only falling back to the ISO code when
           the currency has no symbol configured (which is what the server's own
           COALESCE(CurSymbol, ISO_Code) already resolved). The ISO code drives the scale,
           so no lakh/crore or million step is ever assumed here. */
        function compactAmount(value) {
            var v = Number(value) || 0;
            return (v < 0 ? '−' : '') + symbol() + magnitudeOf(v);
        }

        /* The variance, always with its sign. It is negative by construction on this card,
           so this is in practice always a minus - a TRUE minus sign, not a hyphen, so it
           matches the one the amount formatter prints elsewhere. */
        function signedCompactAmount(value) {
            var v = Number(value) || 0;
            return (v < 0 ? '−' : '+') + symbol() + magnitudeOf(v);
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
           a figure. */
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
    VAS.VAS_252_TopBudgetOverRunsWidget.prototype.init = function (windowNo, frame) {
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

    VAS.VAS_252_TopBudgetOverRunsWidget.prototype.widgetSizeChange = function (widget) {
        this.widgetInfo = widget;
    };

    VAS.VAS_252_TopBudgetOverRunsWidget.prototype.dispose = function () {
        try { this.releasePanel(); } catch (e) { /* ignore */ }
        if (this.frame && typeof this.frame.dispose === 'function') {
            try { this.frame.dispose(); } catch (e) { /* ignore */ }
        }
        this.frame = null;
        this.windowNo = null;
    };

})(VAS, jQuery);
